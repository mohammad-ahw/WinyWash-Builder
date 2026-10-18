using System;
using System.IO;
using System.Management;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;
using CleanC.Core.Native;

namespace CleanC.Core.Services
{
    public class SystemStatsService
    {
        private static long _prevIdleTime;
        private static long _prevKernelTime;
        private static long _prevUserTime;
        private static bool _firstCpuSample = true;

        private static string _cachedDiskModel = string.Empty;
        private static string _cachedDiskType = "SSD";
        private static string _cachedDiskHealth = LocalizationService.Get("سالم (Healthy)");
        private static string _cachedGpuSummary = string.Empty;
        private static bool _hardwareInitialized = false;

        // 3. Drive C: — TotalSize عملاً ثابت است و AvailableFreeSpace هر چند ثانیه یک بار کافی است.
        //    قبلاً هر تیک تایمر (۲٫۵ ثانیه) مستقیم درایو را چک می کردیم.
        private static long _cachedDiskTotalBytes;
        private static long _cachedDiskFreeBytes;
        private static DateTime _diskFreeCachedAtUtc = DateTime.MinValue;
        private static readonly TimeSpan DiskFreeCacheTtl = TimeSpan.FromSeconds(3);

        static SystemStatsService()
        {
            Task.Run(InitHardwareInfo);
        }

        public static SystemStats GetCurrentStats()
        {
            var stats = new SystemStats();

            // 1. RAM via GlobalMemoryStatusEx (0.001 ms)
            var memStatus = new Win32.MEMORYSTATUSEX();
            if (Win32.GlobalMemoryStatusEx(memStatus))
            {
                stats.TotalRamBytes = (long)memStatus.ullTotalPhys;
                stats.FreeRamBytes = (long)memStatus.ullAvailPhys;
                stats.UsedRamBytes = stats.TotalRamBytes - stats.FreeRamBytes;
                stats.RamPercent = Math.Round(((double)stats.UsedRamBytes / stats.TotalRamBytes) * 100.0, 1);
            }

            // 2. CPU via GetSystemTimes
            stats.CpuPercent = GetCpuUsage();

            // 3. Drive C: (با کش کوتاه مدت — جزئیات در فیلدهای بالا)
            try
            {
                var now = DateTime.UtcNow;
                if (_cachedDiskTotalBytes == 0 || (now - _diskFreeCachedAtUtc) >= DiskFreeCacheTtl)
                {
                    var driveC = new DriveInfo("C");
                    if (driveC.IsReady)
                    {
                        _cachedDiskTotalBytes = driveC.TotalSize;
                        _cachedDiskFreeBytes = driveC.AvailableFreeSpace;
                        _diskFreeCachedAtUtc = now;
                    }
                }

                if (_cachedDiskTotalBytes > 0)
                {
                    stats.DiskTotalBytes = _cachedDiskTotalBytes;
                    stats.DiskFreeBytes = _cachedDiskFreeBytes;
                    stats.DiskUsedBytes = stats.DiskTotalBytes - stats.DiskFreeBytes;
                    stats.DiskPercent = Math.Round(((double)stats.DiskUsedBytes / stats.DiskTotalBytes) * 100.0, 1);
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // 4. Hardware Details (Non-blocking background cache)
            stats.DiskModel = string.IsNullOrEmpty(_cachedDiskModel) ? LocalizationService.Get("درایو محلی ویندوز") : _cachedDiskModel;
            stats.DiskType = _cachedDiskType;
            stats.DiskHealth = _cachedDiskHealth;
            stats.GpuSummary = _cachedGpuSummary;

            // 5. Dynamic Power Status
            try
            {
                var pStatus = PowerPlanService.GetPowerStatus();
                stats.PowerSummary = pStatus.IsOnAcPower
                    ? (pStatus.IsLaptop ? LocalizationService.Get("متصل به شارژر") : LocalizationService.Get("متصل به برق"))
                    : string.Format(LocalizationService.Get("{0}% باتری"), pStatus.BatteryPercent);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return stats;
        }

        private static void InitHardwareInfo()
        {
            if (_hardwareInitialized) return;
            _hardwareInitialized = true;

            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, MediaType, BusType, HealthStatus FROM MSFT_PhysicalDisk");
                foreach (var mo in searcher.Get())
                {
                    string? name = mo["FriendlyName"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        _cachedDiskModel = name;
                    }

                    int mediaType = mo["MediaType"] != null ? Convert.ToInt32(mo["MediaType"]) : 0;
                    int busType = mo["BusType"] != null ? Convert.ToInt32(mo["BusType"]) : 0;
                    int health = mo["HealthStatus"] != null ? Convert.ToInt32(mo["HealthStatus"]) : 0;

                    string bType = busType == 17 ? "NVMe" : (busType == 11 ? "SATA" : "");
                    string mType = mediaType == 4 ? "SSD" : (mediaType == 3 ? "HDD" : "Drive");
                    _cachedDiskType = $"{bType} {mType}".Trim();

                    _cachedDiskHealth = health == 0 ? LocalizationService.Get("سالم (Healthy)") : (health == 1 ? LocalizationService.Get("هشدار (Warning)") : LocalizationService.Get("نیازمند بررسی"));
                    break;
                }
            }
            catch
            {
                _cachedDiskType = "SSD";
                _cachedDiskHealth = LocalizationService.Get("سالم (Healthy)");
            }

            try
            {
                var gpu = GpuOptimizerService.DetectGpuConfiguration();
                if (!string.IsNullOrEmpty(gpu.DedicatedGpuName))
                {
                    string gName = gpu.DedicatedGpuName.Replace("with Max-Q Design", "").Replace("Laptop GPU", "").Trim();
                    _cachedGpuSummary = gName;
                }
                else if (!string.IsNullOrEmpty(gpu.IntegratedGpuName))
                {
                    _cachedGpuSummary = gpu.IntegratedGpuName;
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private static double GetCpuUsage()
        {
            try
            {
                if (!Win32.GetSystemTimes(out long idleTime, out long kernelTime, out long userTime))
                    return 0;

                if (_firstCpuSample)
                {
                    // فقط baseline ذخیره می شود؛ عدد واقعی از نمونه دوم (تیک بعدی) به دست می آید
                    _prevIdleTime = idleTime;
                    _prevKernelTime = kernelTime;
                    _prevUserTime = userTime;
                    _firstCpuSample = false;
                    return 0.0;
                }

                long usr = userTime - _prevUserTime;
                long ker = kernelTime - _prevKernelTime;
                long idl = idleTime - _prevIdleTime;

                long sys = ker + usr;
                if (sys <= 0) return 0;

                double cpu = (double)(sys - idl) * 100.0 / sys;
                _prevIdleTime = idleTime;
                _prevKernelTime = kernelTime;
                _prevUserTime = userTime;

                return Math.Max(0.0, Math.Min(100.0, Math.Round(cpu, 1)));
            }
            catch
            {
                return 0;
            }
        }
    }
}
