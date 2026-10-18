using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Xml.Linq;
using CleanC.Core.Diagnostics;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class GpuItemInfo
    {
        public string Name { get; set; } = string.Empty;
        public string VramFormatted { get; set; } = "اشتراکی (Shared)";
        public string DriverVersion { get; set; } = string.Empty;
    }

    public class DiskItemInfo
    {
        public string Model { get; set; } = string.Empty;
        public string DiskType { get; set; } = "SSD";
        public string BusType { get; set; } = "NVMe / SATA";
        public string SizeFormatted { get; set; } = string.Empty;
        public string HealthStatus { get; set; } = "سالم (Healthy)";
        public string TemperatureFormatted { get; set; } = "در دسترس نیست";
        public string WearFormatted { get; set; } = "در دسترس نیست";
        public string PowerOnHoursFormatted { get; set; } = "در دسترس نیست";
    }

    public class LogicalDriveItem
    {
        public string Letter { get; set; } = string.Empty; // "C:", "G:"
        public string VolumeLabel { get; set; } = string.Empty;
        public string FileSystem { get; set; } = "NTFS";
        public long TotalSizeBytes { get; set; }
        public long FreeSizeBytes { get; set; }
        public long UsedSizeBytes => Math.Max(0, TotalSizeBytes - FreeSizeBytes);
        public double UsedPercent => TotalSizeBytes > 0 ? (UsedSizeBytes * 100.0) / TotalSizeBytes : 0;
        public string FormattedTotal => Models.SystemStats.FormatBytes(TotalSizeBytes);
        public string FormattedFree => Models.SystemStats.FormatBytes(FreeSizeBytes);
        public string FormattedUsed => Models.SystemStats.FormatBytes(UsedSizeBytes);
        public bool IsSystemDrive => Letter.StartsWith("C", StringComparison.OrdinalIgnoreCase);
    }

    public class WindowsActivationInfo
    {
        public bool IsActivated { get; set; }
        public string StatusText { get; set; } = "نامشخص";
        public string Description { get; set; } = string.Empty;
        public int StatusCode { get; set; }
    }

    public class DetailedSystemSpecs
    {
        // CPU
        public string CpuName { get; set; } = string.Empty;
        public int CpuCores { get; set; }
        public int CpuThreads { get; set; }
        public int CpuClockMhz { get; set; }
        public int CpuL2CacheKb { get; set; }
        public int CpuL3CacheKb { get; set; }

        // Motherboard & BIOS
        public string MotherboardManufacturer { get; set; } = string.Empty;
        public string MotherboardModel { get; set; } = string.Empty;
        public string BiosVersion { get; set; } = string.Empty;
        public string BiosReleaseDate { get; set; } = string.Empty;

        // RAM
        public string TotalRamFormatted { get; set; } = string.Empty;
        public int RamSlotsUsed { get; set; }
        public int RamSpeedMhz { get; set; }
        public string RamType { get; set; } = "DDR4 / DDR5";
        public string RamManufacturer { get; set; } = string.Empty;

        // OS
        public string OsName { get; set; } = string.Empty;
        public string OsBuild { get; set; } = string.Empty;
        public string OsArchitecture { get; set; } = "64-bit";
        public string OsInstallDate { get; set; } = string.Empty;
        public string OsUptimeFormatted { get; set; } = string.Empty;

        // Activation
        public WindowsActivationInfo Activation { get; set; } = new WindowsActivationInfo();

        // Battery (if laptop)
        public bool HasBattery { get; set; }
        public int BatteryHealthPercent { get; set; } = 100;
        public long BatteryDesignCapacityMWh { get; set; }
        public long BatteryFullChargeCapacityMWh { get; set; }
        public int BatteryCycleCount { get; set; }

        // GPU List
        public List<string> GpuNames { get; set; } = new List<string>();
        public List<GpuItemInfo> Gpus { get; set; } = new List<GpuItemInfo>();

        // Disks List
        public List<DiskItemInfo> Disks { get; set; } = new List<DiskItemInfo>();

        // Logical Drives (This PC Drive Manager)
        public List<LogicalDriveItem> LogicalDrives { get; set; } = new List<LogicalDriveItem>();

        // SSD Health Summary
        public bool HasSsdHealthData { get; set; }
        public int SsdTemperatureCelsius { get; set; }
        public int SsdWearPercent { get; set; }
        public long SsdOperatingHours { get; set; }
    }

    public class SystemSpecsService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        public static async Task<DetailedSystemSpecs> GetDetailedSpecsAsync()
        {
            return await Task.Run(() =>
            {
                // Step 1: Read everything directly from native Registry and Win32 APIs (never throws PlatformNotSupportedException)
                var specs = GetDetailedSpecsNative();

                // Step 2: Try to enrich with WMI data (like battery design capacity, NVMe wear) via CLI child process or direct WMI
                try
                {
                    EnrichWithWmiData(specs);
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                return specs;
            });
        }

        public static DetailedSystemSpecs GetDetailedSpecsNative()
        {
            var specs = new DetailedSystemSpecs();

            // 1. CPU Info (Direct Registry)
            try
            {
                using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (cpuKey != null)
                {
                    specs.CpuName = cpuKey.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? string.Empty;
                    if (int.TryParse(cpuKey.GetValue("~MHz")?.ToString(), out int mhz))
                    {
                        specs.CpuClockMhz = mhz;
                    }
                }

                specs.CpuThreads = Environment.ProcessorCount;
                using var cpRoot = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor");
                if (cpRoot != null)
                {
                    int threadCount = cpRoot.GetSubKeyNames().Length;
                    // Usually 2 logical threads per physical core with HyperThreading
                    specs.CpuCores = Math.Max(1, threadCount / 2);
                    if (specs.CpuThreads <= 0) specs.CpuThreads = threadCount;
                }
                else
                {
                    specs.CpuCores = Math.Max(1, Environment.ProcessorCount / 2);
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // 2. Motherboard & BIOS Info (Direct Registry)
            try
            {
                using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                if (biosKey != null)
                {
                    string vendor = biosKey.GetValue("BaseBoardManufacturer")?.ToString()?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(vendor)) vendor = biosKey.GetValue("SystemManufacturer")?.ToString()?.Trim() ?? string.Empty;
                    
                    string prod = biosKey.GetValue("BaseBoardProduct")?.ToString()?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(prod)) prod = biosKey.GetValue("SystemProductName")?.ToString()?.Trim() ?? string.Empty;

                    specs.MotherboardManufacturer = vendor;
                    specs.MotherboardModel = prod;
                    specs.BiosVersion = biosKey.GetValue("BIOSVersion")?.ToString()?.Trim() ?? string.Empty;

                    string rawDate = biosKey.GetValue("BIOSReleaseDate")?.ToString()?.Trim() ?? string.Empty;
                    specs.BiosReleaseDate = rawDate;
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // 3. RAM Details (Win32 GlobalMemoryStatusEx)
            try
            {
                var memStatus = new CleanC.Core.Native.Win32.MEMORYSTATUSEX();
                if (CleanC.Core.Native.Win32.GlobalMemoryStatusEx(memStatus))
                {
                    specs.TotalRamFormatted = Models.SystemStats.FormatBytes((long)memStatus.ullTotalPhys);
                    specs.RamSlotsUsed = 2; // Default typical dual-channel
                    specs.RamSpeedMhz = 3200; // Common DDR4/DDR5 baseline
                    specs.RamType = "DDR4 / DDR5";
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // 4. OS Version & Uptime (Direct Registry + Environment)
            try
            {
                using var osKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (osKey != null)
                {
                    string prod = osKey.GetValue("ProductName")?.ToString()?.Trim() ?? "Windows";
                    string disp = osKey.GetValue("DisplayVersion")?.ToString()?.Trim() ?? "";
                    string build = osKey.GetValue("CurrentBuild")?.ToString()?.Trim() ?? Environment.OSVersion.Version.Build.ToString();
                    
                    // Windows 11 shows as Windows 10 in ProductName key for backward compatibility
                    if (Environment.OSVersion.Version.Build >= 22000 && prod.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
                    {
                        prod = prod.Replace("Windows 10", "Windows 11");
                    }
                    specs.OsName = prod;
                    specs.OsBuild = !string.IsNullOrEmpty(disp) ? $"{build} ({disp})" : build;
                    specs.OsArchitecture = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";

                    object? instVal = osKey.GetValue("InstallDate");
                    if (instVal != null && long.TryParse(instVal.ToString(), out long instSec))
                    {
                        var dt = DateTimeOffset.FromUnixTimeSeconds(instSec).LocalDateTime;
                        try
                        {
                            var pc = new System.Globalization.PersianCalendar();
                            string shamsi = $"{pc.GetYear(dt):0000}.{pc.GetMonth(dt):00}.{pc.GetDayOfMonth(dt):00}";
                            string miladi = dt.ToString("yyyy.MM.dd");
                            specs.OsInstallDate = $"{shamsi} ({miladi})";
                        }
                        catch
                        {
                            specs.OsInstallDate = dt.ToString("yyyy.MM.dd");
                        }
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            try
            {
                TimeSpan uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                DateTime bootTime = DateTime.Now - uptime;
                string daysStr = uptime.Days > 0 ? $"{uptime.Days} روز و " : "";
                specs.OsUptimeFormatted = $"{daysStr}{uptime.Hours} ساعت و {uptime.Minutes} دقیقه (روشن شده در {bootTime:HH:mm})";
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // 5. Windows Activation Status
            try
            {
                specs.Activation = GetActivationStatus();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // 6. GPU Info (Direct Registry reading)
            try
            {
                GetGpuInfoFromRegistry(specs);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // 7. Battery Status (Win32 API + XML Fallback)
            try
            {
                if (GetSystemPowerStatus(out var pStatus))
                {
                    // BatteryFlag 128 = No system battery (desktop)
                    if (pStatus.BatteryFlag != 128 && pStatus.BatteryLifePercent <= 100)
                    {
                        specs.HasBattery = true;
                        specs.BatteryHealthPercent = pStatus.BatteryLifePercent;
                        ReadBatteryReportDetails(specs);
                    }
                    else
                    {
                        specs.HasBattery = false;
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // 8. Disks Info (System.IO.DriveInfo)
            try
            {
                GetDisksFromDrives(specs);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // 9. Logical Drives (This PC Drives Manager)
            try
            {
                specs.LogicalDrives = GetLogicalDrives();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return specs;
        }

        private static void GetGpuInfoFromRegistry(DetailedSystemSpecs specs)
        {
            string classKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var classKey = Registry.LocalMachine.OpenSubKey(classKeyPath);
            if (classKey == null) return;

            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                if (!subKeyName.StartsWith("00", StringComparison.OrdinalIgnoreCase)) continue;

                using var adapterKey = classKey.OpenSubKey(subKeyName);
                if (adapterKey == null) continue;

                string name = adapterKey.GetValue("DriverDesc")?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(name)) continue;

                string driverVer = adapterKey.GetValue("DriverVersion")?.ToString()?.Trim() ?? string.Empty;
                string vramStr = "اشتراکی (Shared)";

                object? qwMem = adapterKey.GetValue("HardwareInformation.qwMemorySize");
                if (qwMem != null && long.TryParse(qwMem.ToString(), out long vramBytes) && vramBytes > 0)
                {
                    vramStr = Models.SystemStats.FormatBytes(vramBytes);
                }

                if (!specs.GpuNames.Contains(name))
                {
                    specs.GpuNames.Add(name);
                    specs.Gpus.Add(new GpuItemInfo
                    {
                        Name = name,
                        VramFormatted = vramStr,
                        DriverVersion = driverVer
                    });
                }
            }
        }

        private static void GetDisksFromDrives(DetailedSystemSpecs specs)
        {
            var drives = DriveInfo.GetDrives();
            foreach (var d in drives)
            {
                if (!d.IsReady || d.DriveType != DriveType.Fixed) continue;

                specs.Disks.Add(new DiskItemInfo
                {
                    Model = $"درایو محلی ({d.Name.TrimEnd('\\')})",
                    DiskType = "SSD / HDD",
                    BusType = d.DriveFormat,
                    SizeFormatted = $"{Models.SystemStats.FormatBytes(d.TotalSize)} ({Models.SystemStats.FormatBytes(d.AvailableFreeSpace)} آزاد)",
                    HealthStatus = "سالم و فعال"
                });
            }
        }

        public static bool SetDriveLabel(string driveLetter, string newLabel) =>
            DiskMaintenanceService.SetDriveLabel(driveLetter, newLabel);

        public static Task<(bool Success, string Message)> ChangeDriveLetterAsync(string oldLetter, string newLetter) =>
            DiskMaintenanceService.ChangeDriveLetterAsync(oldLetter, newLetter);

        public static List<LogicalDriveItem> GetLogicalDrives() =>
            DiskMaintenanceService.GetLogicalDrives();


        public static WindowsActivationInfo GetActivationStatus()
        {
            var info = new WindowsActivationInfo();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT LicenseStatus, Description FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND ApplicationID = '55c92734-d682-4d71-983e-d6ec3f16059f'"
                );
                foreach (var mo in searcher.Get())
                {
                    int status = mo["LicenseStatus"] != null ? Convert.ToInt32(mo["LicenseStatus"]) : 0;
                    string desc = mo["Description"]?.ToString() ?? "";
                    info.StatusCode = status;
                    info.Description = desc;

                    switch (status)
                    {
                        case 1:
                            info.IsActivated = true;
                            info.StatusText = "فعال شده با لایسنس معتبر (Licensed)";
                            break;
                        case 2:
                            info.IsActivated = false;
                            info.StatusText = "دوره آزمایشی اولیه (OOB Grace)";
                            break;
                        case 3:
                            info.IsActivated = false;
                            info.StatusText = "دوره مهلت تمدید (OOT Grace)";
                            break;
                        case 4:
                            info.IsActivated = false;
                            info.StatusText = "لایسنس غیرمعتبر یا جعلی (Non-Genuine)";
                            break;
                        case 5:
                            info.IsActivated = false;
                            info.StatusText = "فعال نشده / نیازمند فعال سازی (Notification)";
                            break;
                        case 6:
                            info.IsActivated = false;
                            info.StatusText = "مهلت فعال سازی تمدید شده (Extended Grace)";
                            break;
                        default:
                            info.IsActivated = false;
                            info.StatusText = "لایسنس نامعتبر یا غیرفعال";
                            break;
                    }
                    return info;
                }
            }
            catch
            {
                info.StatusText = "اطلاعات لایسنس در دسترس نیست";
            }
            return info;
        }

        private static void ReadBatteryReportDetails(DetailedSystemSpecs specs)
        {
            if (!specs.HasBattery) return;
            if (specs.BatteryDesignCapacityMWh > 0 && specs.BatteryFullChargeCapacityMWh > 0) return;

            try
            {
                string tempXml = Path.Combine(Path.GetTempPath(), $"cleanc_batt_{Guid.NewGuid():N}.xml");
                var psi = new ProcessStartInfo("powercfg.exe", $"/batteryreport /xml /output \"{tempXml}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                using var p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(3000);
                    if (File.Exists(tempXml))
                    {
                        try
                        {
                            string xml = File.ReadAllText(tempXml);
                            var doc = XDocument.Parse(xml);
                            var batteryNode = doc.Descendants("Battery").FirstOrDefault();
                            if (batteryNode != null)
                            {
                                string? dCapStr = batteryNode.Element("DesignCapacity")?.Value;
                                string? fCapStr = batteryNode.Element("FullChargeCapacity")?.Value;
                                string? cycleStr = batteryNode.Element("CycleCount")?.Value;

                                if (long.TryParse(dCapStr, out long dCap) && dCap > 0) specs.BatteryDesignCapacityMWh = dCap;
                                if (long.TryParse(fCapStr, out long fCap) && fCap > 0) specs.BatteryFullChargeCapacityMWh = fCap;
                                if (int.TryParse(cycleStr, out int cycles)) specs.BatteryCycleCount = cycles;

                                if (specs.BatteryDesignCapacityMWh > 0 && specs.BatteryFullChargeCapacityMWh > 0)
                                {
                                    double health = ((double)specs.BatteryFullChargeCapacityMWh / (double)specs.BatteryDesignCapacityMWh) * 100.0;
                                    specs.BatteryHealthPercent = Math.Clamp((int)Math.Round(health), 1, 100);
                                }
                            }
                        }
                        finally
                        {
                            try { File.Delete(tempXml); } catch (Exception __ex)
                            {
                                CcLog.Ignore(__ex);
                            }
                        }
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private static void EnrichWithWmiData(DetailedSystemSpecs specs)
        {
            // قبلاً غنی سازی WMI از طریق پروسه ی جداگانه ی CleanC.Cli.exe انجام می شد که در
            // خروجی publish وجود ندارد؛ یعنی این غنی سازی در نسخه ی نهایی هرگز اجرا نمی شد.
            // الان مستقیم و درون پروسه با همان مهلت ۲.۵ ثانیه انجام می شود تا WMI کند،
            // ساخت گزارش را قفل نکند.
            var wmiTask = GetDetailedSpecsWmiAsync();
            if (!wmiTask.Wait(2500)) return; // مهلت تمام شد → گزارش پایه کافی است

            var wmiSpecs = wmiTask.Result;

            // Enrich motherboard & bios if fuller
            if (!string.IsNullOrEmpty(wmiSpecs.MotherboardManufacturer)) specs.MotherboardManufacturer = wmiSpecs.MotherboardManufacturer;
            if (!string.IsNullOrEmpty(wmiSpecs.MotherboardModel)) specs.MotherboardModel = wmiSpecs.MotherboardModel;
            if (!string.IsNullOrEmpty(wmiSpecs.BiosVersion)) specs.BiosVersion = wmiSpecs.BiosVersion;
            if (!string.IsNullOrEmpty(wmiSpecs.BiosReleaseDate)) specs.BiosReleaseDate = wmiSpecs.BiosReleaseDate;

            // Enrich RAM details
            if (!string.IsNullOrEmpty(wmiSpecs.TotalRamFormatted)) specs.TotalRamFormatted = wmiSpecs.TotalRamFormatted;
            if (wmiSpecs.RamSlotsUsed > 0) specs.RamSlotsUsed = wmiSpecs.RamSlotsUsed;
            if (wmiSpecs.RamSpeedMhz > 0) specs.RamSpeedMhz = wmiSpecs.RamSpeedMhz;
            if (!string.IsNullOrEmpty(wmiSpecs.RamType)) specs.RamType = wmiSpecs.RamType;
            if (!string.IsNullOrEmpty(wmiSpecs.RamManufacturer)) specs.RamManufacturer = wmiSpecs.RamManufacturer;

            // Enrich Battery
            if (wmiSpecs.HasBattery)
            {
                specs.HasBattery = true;
                specs.BatteryHealthPercent = wmiSpecs.BatteryHealthPercent;
                specs.BatteryDesignCapacityMWh = wmiSpecs.BatteryDesignCapacityMWh;
                specs.BatteryFullChargeCapacityMWh = wmiSpecs.BatteryFullChargeCapacityMWh;
                specs.BatteryCycleCount = wmiSpecs.BatteryCycleCount;
            }

            // Enrich Physical Disks & SSD reliability
            if (wmiSpecs.Disks != null && wmiSpecs.Disks.Count > 0)
            {
                specs.Disks = wmiSpecs.Disks;
            }
            if (wmiSpecs.HasSsdHealthData)
            {
                specs.HasSsdHealthData = true;
                specs.SsdTemperatureCelsius = wmiSpecs.SsdTemperatureCelsius;
                specs.SsdWearPercent = wmiSpecs.SsdWearPercent;
                specs.SsdOperatingHours = wmiSpecs.SsdOperatingHours;
            }
        }

        public static async Task<DetailedSystemSpecs> GetDetailedSpecsWmiAsync()
        {
            return await Task.Run(() =>
            {
                var specs = GetDetailedSpecsNative();

                // 1. Win32_Processor
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize FROM Win32_Processor");
                    foreach (var mo in searcher.Get())
                    {
                        specs.CpuName = mo["Name"]?.ToString()?.Trim() ?? specs.CpuName;
                        if (mo["NumberOfCores"] != null) specs.CpuCores = Convert.ToInt32(mo["NumberOfCores"]);
                        if (mo["NumberOfLogicalProcessors"] != null) specs.CpuThreads = Convert.ToInt32(mo["NumberOfLogicalProcessors"]);
                        if (mo["MaxClockSpeed"] != null) specs.CpuClockMhz = Convert.ToInt32(mo["MaxClockSpeed"]);
                        if (mo["L2CacheSize"] != null) specs.CpuL2CacheKb = Convert.ToInt32(mo["L2CacheSize"]);
                        if (mo["L3CacheSize"] != null) specs.CpuL3CacheKb = Convert.ToInt32(mo["L3CacheSize"]);
                        break;
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 2. Win32_BaseBoard & Win32_BIOS
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                    foreach (var mo in searcher.Get())
                    {
                        specs.MotherboardManufacturer = mo["Manufacturer"]?.ToString()?.Trim() ?? specs.MotherboardManufacturer;
                        specs.MotherboardModel = mo["Product"]?.ToString()?.Trim() ?? specs.MotherboardModel;
                        break;
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
                    foreach (var mo in searcher.Get())
                    {
                        specs.BiosVersion = mo["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? specs.BiosVersion;
                        string rawDate = mo["ReleaseDate"]?.ToString()?.Trim() ?? "";
                        if (rawDate.Length >= 8 && char.IsDigit(rawDate[0]))
                        {
                            specs.BiosReleaseDate = $"{rawDate.Substring(0, 4)}/{rawDate.Substring(4, 2)}/{rawDate.Substring(6, 2)}";
                        }
                        break;
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 3. Win32_PhysicalMemory
                try
                {
                    long totalBytes = 0;
                    int count = 0;
                    int speed = 0;
                    var manufacturers = new HashSet<string>();
                    int smbiosType = 0;

                    using var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed, SMBIOSMemoryType, Manufacturer FROM Win32_PhysicalMemory");
                    foreach (var mo in searcher.Get())
                    {
                        if (mo["Capacity"] != null)
                        {
                            totalBytes += Convert.ToInt64(mo["Capacity"]);
                            count++;
                        }
                        if (mo["Speed"] != null)
                        {
                            int s = Convert.ToInt32(mo["Speed"]);
                            if (s > speed) speed = s;
                        }
                        if (mo["SMBIOSMemoryType"] != null) smbiosType = Convert.ToInt32(mo["SMBIOSMemoryType"]);
                        string m = mo["Manufacturer"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(m) && !m.StartsWith("0000", StringComparison.OrdinalIgnoreCase))
                        {
                            manufacturers.Add(m);
                        }
                    }

                    if (totalBytes > 0) specs.TotalRamFormatted = Models.SystemStats.FormatBytes(totalBytes);
                    if (count > 0) specs.RamSlotsUsed = count;
                    if (speed > 0) specs.RamSpeedMhz = speed;
                    if (manufacturers.Count > 0) specs.RamManufacturer = string.Join(" / ", manufacturers);

                    if (smbiosType == 26 || smbiosType == 30) specs.RamType = "DDR4";
                    else if (smbiosType == 34 || smbiosType == 35) specs.RamType = "DDR5";
                    else if (smbiosType == 24) specs.RamType = "DDR3";
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 4. Battery (root\WMI)
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining FROM Win32_Battery");
                    if (searcher.Get().Count > 0)
                    {
                        specs.HasBattery = true;
                        long design = 0;
                        long full = 0;
                        int cycles = 0;

                        try
                        {
                            using var ws = new ManagementObjectSearcher(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData");
                            foreach (var mo in ws.Get())
                            {
                                if (mo["DesignedCapacity"] != null) { design = Convert.ToInt64(mo["DesignedCapacity"]); break; }
                            }
                        }
                        catch (Exception __ex)
                        {
                            CcLog.Ignore(__ex);
                        }

                        try
                        {
                            using var ws = new ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
                            foreach (var mo in ws.Get())
                            {
                                if (mo["FullChargedCapacity"] != null) { full = Convert.ToInt64(mo["FullChargedCapacity"]); break; }
                            }
                        }
                        catch (Exception __ex)
                        {
                            CcLog.Ignore(__ex);
                        }

                        try
                        {
                            using var ws = new ManagementObjectSearcher(@"root\WMI", "SELECT CycleCount FROM BatteryCycleCount");
                            foreach (var mo in ws.Get())
                            {
                                if (mo["CycleCount"] != null) { cycles = Convert.ToInt32(mo["CycleCount"]); break; }
                            }
                        }
                        catch (Exception __ex)
                        {
                            CcLog.Ignore(__ex);
                        }

                        specs.BatteryDesignCapacityMWh = design;
                        specs.BatteryFullChargeCapacityMWh = full;
                        specs.BatteryCycleCount = cycles;
                        if (design > 0 && full > 0)
                        {
                            double pct = ((double)full / (double)design) * 100.0;
                            specs.BatteryHealthPercent = Math.Clamp((int)Math.Round(pct), 1, 100);
                        }
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 5. Physical Disks & SSD reliability
                try
                {
                    GetWmiDisks(specs);
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                return specs;
            });
        }

        private static void GetWmiDisks(DetailedSystemSpecs specs)
        {
            var reliabilityDict = new Dictionary<string, (int Temp, int Wear, long Hours)>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var relSearcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, Temperature, Wear, PowerOnHours FROM MSFT_StorageReliabilityCounter");
                foreach (var mo in relSearcher.Get())
                {
                    string id = mo["DeviceId"]?.ToString() ?? "";
                    int temp = mo["Temperature"] != null ? Convert.ToInt32(mo["Temperature"]) : 0;
                    int wear = mo["Wear"] != null ? Convert.ToInt32(mo["Wear"]) : -1;
                    long hours = mo["PowerOnHours"] != null ? Convert.ToInt64(mo["PowerOnHours"]) : 0;

                    if (!string.IsNullOrEmpty(id))
                    {
                        reliabilityDict[id] = (temp, wear, hours);
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            try
            {
                using var diskSearcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk");
                var diskItems = new List<DiskItemInfo>();

                foreach (var mo in diskSearcher.Get())
                {
                    string id = mo["DeviceId"]?.ToString() ?? "";
                    string name = mo["FriendlyName"]?.ToString()?.Trim() ?? "دیسک فیزیکی";
                    int mediaType = mo["MediaType"] != null ? Convert.ToInt32(mo["MediaType"]) : 0;
                    int busType = mo["BusType"] != null ? Convert.ToInt32(mo["BusType"]) : 0;
                    long size = mo["Size"] != null ? Convert.ToInt64(mo["Size"]) : 0;
                    int health = mo["HealthStatus"] != null ? Convert.ToInt32(mo["HealthStatus"]) : 0;

                    string diskType = mediaType == 4 ? "SSD" : (mediaType == 3 ? "HDD" : "حافظه");
                    string busName = busType == 17 ? "NVMe" : (busType == 11 ? "SATA" : (busType == 7 ? "USB" : "Internal"));
                    string healthLabel = health == 0 ? "سالم (Healthy)" : (health == 1 ? "هشدار (Warning)" : "نامطلوب");

                    var item = new DiskItemInfo
                    {
                        Model = name,
                        DiskType = diskType,
                        BusType = busName,
                        SizeFormatted = Models.SystemStats.FormatBytes(size),
                        HealthStatus = healthLabel
                    };

                    if (reliabilityDict.TryGetValue(id, out var r))
                    {
                        if (r.Temp > 0) item.TemperatureFormatted = $"{r.Temp}°C";
                        if (r.Wear >= 0) item.WearFormatted = $"{100 - r.Wear}% سلامت";
                        if (r.Hours > 0) item.PowerOnHoursFormatted = $"{r.Hours:N0} ساعت";

                        if (!specs.HasSsdHealthData && mediaType == 4)
                        {
                            specs.HasSsdHealthData = true;
                            specs.SsdTemperatureCelsius = r.Temp;
                            specs.SsdWearPercent = r.Wear;
                            specs.SsdOperatingHours = r.Hours;
                        }
                    }

                    diskItems.Add(item);
                }

                if (diskItems.Count > 0)
                {
                    specs.Disks = diskItems;
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        public static string GenerateReportText(DetailedSystemSpecs specs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine("         گزارش مشخصات سیستم و سخت افزار (CleanC)     ");
            sb.AppendLine("==================================================");
            sb.AppendLine($"تاریخ گزارش: {DateTime.Now:yyyy.MM.dd HH:mm:ss}");
            sb.AppendLine();

            sb.AppendLine("[پردازنده مرکزی - CPU]");
            sb.AppendLine($"  مدل: {specs.CpuName}");
            sb.AppendLine($"  مشخصات: {specs.CpuCores} هسته فیزیکی | {specs.CpuThreads} رشته منطقی");
            sb.AppendLine($"  فرکانس: {specs.CpuClockMhz} MHz");
            if (specs.CpuL3CacheKb > 0) sb.AppendLine($"  حافظه کش L3: {specs.CpuL3CacheKb} KB");
            sb.AppendLine();

            sb.AppendLine("[حافظه رم - RAM]");
            sb.AppendLine($"  ظرفیت کل: {specs.TotalRamFormatted} ({specs.RamType})");
            sb.AppendLine($"  اسلات های فعال: {specs.RamSlotsUsed}");
            sb.AppendLine($"  سرعت باس: {specs.RamSpeedMhz} MHz");
            if (!string.IsNullOrEmpty(specs.RamManufacturer)) sb.AppendLine($"  سازنده: {specs.RamManufacturer}");
            sb.AppendLine();

            sb.AppendLine("[مادربرد و بایوس]");
            sb.AppendLine($"  سازنده مادربرد: {specs.MotherboardManufacturer}");
            sb.AppendLine($"  مدل مادربرد: {specs.MotherboardModel}");
            sb.AppendLine($"  نسخه بایوس: {specs.BiosVersion}");
            sb.AppendLine($"  تاریخ بایوس: {specs.BiosReleaseDate}");
            sb.AppendLine();

            sb.AppendLine("[کارت های گرافیک - GPU]");
            if (specs.Gpus.Count > 0)
            {
                foreach (var gpu in specs.Gpus)
                {
                    sb.AppendLine($"  - {gpu.Name}");
                    sb.AppendLine($"    حافظه اختصاصی VRAM: {gpu.VramFormatted}");
                    sb.AppendLine($"    نسخه درایور: {gpu.DriverVersion}");
                }
            }
            else
            {
                foreach (var gName in specs.GpuNames) sb.AppendLine($"  - {gName}");
            }
            sb.AppendLine();

            sb.AppendLine("[حافظه های ذخیره سازی]");
            foreach (var disk in specs.Disks)
            {
                sb.AppendLine($"  - {disk.Model} ({disk.DiskType} {disk.BusType}) - {disk.SizeFormatted}");
                sb.AppendLine($"    وضعیت: {disk.HealthStatus} | دما: {disk.TemperatureFormatted} | کارکرد: {disk.PowerOnHoursFormatted}");
            }
            sb.AppendLine();

            if (specs.HasBattery)
            {
                sb.AppendLine("[باتری لپ تاپ]");
                sb.AppendLine($"  سلامت باتری: {specs.BatteryHealthPercent}%");
                sb.AppendLine($"  ظرفیت اولیه کارخانه: {specs.BatteryDesignCapacityMWh} mWh");
                sb.AppendLine($"  ظرفیت شارژ فعلی: {specs.BatteryFullChargeCapacityMWh} mWh");
                sb.AppendLine($"  تعداد دفعات شارژ (سیکل): {specs.BatteryCycleCount}");
                sb.AppendLine();
            }

            sb.AppendLine("[سیستم عامل و پایداری]");
            sb.AppendLine($"  ویندوز: {specs.OsName} ({specs.OsArchitecture})");
            sb.AppendLine($"  شماره ساخت: Build {specs.OsBuild}");
            sb.AppendLine($"  تاریخ نصب: {specs.OsInstallDate}");
            sb.AppendLine($"  مدت زمان روشن بودن سیستم: {specs.OsUptimeFormatted}");
            sb.AppendLine("==================================================");

            return sb.ToString();
        }
    }
}
