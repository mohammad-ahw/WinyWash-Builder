using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Text;
using System.Threading.Tasks;
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

        // SSD Health Summary (Compatibility)
        public bool HasSsdHealthData { get; set; }
        public int SsdTemperatureCelsius { get; set; }
        public int SsdWearPercent { get; set; }
        public long SsdOperatingHours { get; set; }
    }

    public class SystemSpecsService
    {
        public static async Task<DetailedSystemSpecs> GetDetailedSpecsAsync()
        {
            return await Task.Run(() =>
            {
                var specs = new DetailedSystemSpecs();

                // 1. CPU Info (Win32_Processor)
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize FROM Win32_Processor");
                    foreach (var mo in searcher.Get())
                    {
                        specs.CpuName = mo["Name"]?.ToString()?.Trim() ?? string.Empty;
                        specs.CpuCores = mo["NumberOfCores"] != null ? Convert.ToInt32(mo["NumberOfCores"]) : 0;
                        specs.CpuThreads = mo["NumberOfLogicalProcessors"] != null ? Convert.ToInt32(mo["NumberOfLogicalProcessors"]) : 0;
                        specs.CpuClockMhz = mo["MaxClockSpeed"] != null ? Convert.ToInt32(mo["MaxClockSpeed"]) : 0;
                        if (mo["L2CacheSize"] != null) specs.CpuL2CacheKb = Convert.ToInt32(mo["L2CacheSize"]);
                        if (mo["L3CacheSize"] != null) specs.CpuL3CacheKb = Convert.ToInt32(mo["L3CacheSize"]);
                        break;
                    }
                }
                catch { }

                // 2. Motherboard Info (Win32_BaseBoard) & BIOS (Win32_BIOS)
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                    foreach (var mo in searcher.Get())
                    {
                        specs.MotherboardManufacturer = mo["Manufacturer"]?.ToString()?.Trim() ?? string.Empty;
                        specs.MotherboardModel = mo["Product"]?.ToString()?.Trim() ?? string.Empty;
                        break;
                    }
                }
                catch { }

                try
                {
                    using var biosSearcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate, Manufacturer FROM Win32_BIOS");
                    foreach (var mo in biosSearcher.Get())
                    {
                        specs.BiosVersion = mo["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? string.Empty;
                        string rawDate = mo["ReleaseDate"]?.ToString()?.Trim() ?? string.Empty;
                        if (rawDate.Length >= 8 && char.IsDigit(rawDate[0]))
                        {
                            specs.BiosReleaseDate = $"{rawDate.Substring(0, 4)}/{rawDate.Substring(4, 2)}/{rawDate.Substring(6, 2)}";
                        }
                        else
                        {
                            specs.BiosReleaseDate = rawDate;
                        }
                        break;
                    }
                }
                catch { }

                // 3. RAM Details (Win32_PhysicalMemory)
                try
                {
                    long totalBytes = 0;
                    int count = 0;
                    int speed = 0;
                    var manufacturers = new HashSet<string>();
                    int smbiosType = 0;

                    using var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed, MemoryType, SMBIOSMemoryType, Manufacturer FROM Win32_PhysicalMemory");
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
                        if (mo["SMBIOSMemoryType"] != null)
                        {
                            smbiosType = Convert.ToInt32(mo["SMBIOSMemoryType"]);
                        }
                        string m = mo["Manufacturer"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(m) && !m.StartsWith("0000", StringComparison.OrdinalIgnoreCase))
                        {
                            manufacturers.Add(m);
                        }
                    }

                    specs.TotalRamFormatted = Models.SystemStats.FormatBytes(totalBytes);
                    specs.RamSlotsUsed = count;
                    specs.RamSpeedMhz = speed;
                    specs.RamManufacturer = string.Join(" / ", manufacturers);

                    // SMBIOS Memory Type: 26 = DDR4, 30 = LPDDR4, 34 = DDR5, 35 = LPDDR5
                    if (smbiosType == 26 || smbiosType == 30) specs.RamType = "DDR4";
                    else if (smbiosType == 34 || smbiosType == 35) specs.RamType = "DDR5";
                    else if (smbiosType == 24) specs.RamType = "DDR3";
                    else specs.RamType = speed >= 4800 ? "DDR5" : "DDR4";
                }
                catch { }

                // 4. OS Version & Uptime (Win32_OperatingSystem)
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Caption, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem");
                    foreach (var mo in searcher.Get())
                    {
                        specs.OsName = mo["Caption"]?.ToString()?.Trim() ?? string.Empty;
                        specs.OsBuild = mo["BuildNumber"]?.ToString()?.Trim() ?? string.Empty;
                        specs.OsArchitecture = mo["OSArchitecture"]?.ToString()?.Trim() ?? "64-bit";

                        if (mo["LastBootUpTime"] != null)
                        {
                            try
                            {
                                DateTime bootTime = ManagementDateTimeConverter.ToDateTime(mo["LastBootUpTime"].ToString());
                                TimeSpan uptime = DateTime.Now - bootTime;
                                specs.OsUptimeFormatted = $"{(int)uptime.TotalHours} ساعت و {uptime.Minutes} دقیقه";
                            }
                            catch { }
                        }

                        if (mo["InstallDate"] != null)
                        {
                            try
                            {
                                DateTime inst = ManagementDateTimeConverter.ToDateTime(mo["InstallDate"].ToString());
                                specs.OsInstallDate = inst.ToString("yyyy/MM/dd");
                            }
                            catch { }
                        }
                        break;
                    }
                }
                catch { }

                // 5. Battery Health (root\WMI)
                try
                {
                    GetBatteryHealthInfo(specs);
                }
                catch { }

                // 6. GPU Info (Win32_VideoController + Registry for accurate VRAM > 4GB)
                try
                {
                    GetGpuInfo(specs);
                }
                catch { }

                // 7. Physical Disks & SSD Health (MSFT_PhysicalDisk & MSFT_StorageReliabilityCounter)
                try
                {
                    GetDiskAndHealthInfo(specs);
                }
                catch { }

                return specs;
            });
        }

        private static void GetGpuInfo(DetailedSystemSpecs specs)
        {
            var vramDict = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            // Read accurate VRAM from registry: HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\000*
            try
            {
                using var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (classKey != null)
                {
                    foreach (var subName in classKey.GetSubKeyNames())
                    {
                        if (!subName.StartsWith("0")) continue;
                        using var subKey = classKey.OpenSubKey(subName);
                        if (subKey == null) continue;

                        string? desc = subKey.GetValue("DriverDesc")?.ToString();
                        object? memObj = subKey.GetValue("HardwareInformation.qwMemorySize");
                        if (!string.IsNullOrEmpty(desc) && memObj != null)
                        {
                            long memBytes = Convert.ToInt64(memObj);
                            vramDict[desc.Trim()] = memBytes;
                        }
                    }
                }
            }
            catch { }

            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
            foreach (var mo in searcher.Get())
            {
                string name = mo["Name"]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(name)) continue;

                specs.GpuNames.Add(name);

                string driver = mo["DriverVersion"]?.ToString()?.Trim() ?? string.Empty;
                string vramStr = "اشتراکی (Shared)";

                if (vramDict.TryGetValue(name, out long regBytes) && regBytes > 0)
                {
                    vramStr = Models.SystemStats.FormatBytes(regBytes);
                }
                else if (mo["AdapterRAM"] != null)
                {
                    long ram = Convert.ToInt64(mo["AdapterRAM"]);
                    if (ram > 0 && ram < 4294967295L)
                    {
                        vramStr = Models.SystemStats.FormatBytes(ram);
                    }
                }

                specs.Gpus.Add(new GpuItemInfo
                {
                    Name = name,
                    DriverVersion = driver,
                    VramFormatted = vramStr
                });
            }
        }

        private struct DiskReliability
        {
            public int Temp;
            public int Wear;
            public long Hours;
        }

        private static void GetDiskAndHealthInfo(DetailedSystemSpecs specs)
        {
            // First check reliability counters (Admin accessible)
            var reliabilityDict = new Dictionary<string, DiskReliability>();
            try
            {
                using var relSearcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, Temperature, Wear, PowerOnHours FROM MSFT_StorageReliabilityCounter");
                foreach (var rc in relSearcher.Get())
                {
                    string id = rc["DeviceId"]?.ToString() ?? "";
                    int temp = rc["Temperature"] != null ? Convert.ToInt32(rc["Temperature"]) : 0;
                    int wear = rc["Wear"] != null ? Convert.ToInt32(rc["Wear"]) : 0;
                    long hours = rc["PowerOnHours"] != null ? Convert.ToInt64(rc["PowerOnHours"]) : 0;
                    if (!string.IsNullOrEmpty(id))
                    {
                        reliabilityDict[id] = new DiskReliability { Temp = temp, Wear = wear, Hours = hours };
                    }
                }
            }
            catch { }

            // Query physical disks
            using var diskSearcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk");
            foreach (var disk in diskSearcher.Get())
            {
                string id = disk["DeviceId"]?.ToString() ?? "";
                string name = disk["FriendlyName"]?.ToString()?.Trim() ?? "Storage Disk";
                int mediaType = disk["MediaType"] != null ? Convert.ToInt32(disk["MediaType"]) : 0;
                int busType = disk["BusType"] != null ? Convert.ToInt32(disk["BusType"]) : 0;
                long size = disk["Size"] != null ? Convert.ToInt64(disk["Size"]) : 0;
                int health = disk["HealthStatus"] != null ? Convert.ToInt32(disk["HealthStatus"]) : 0;

                string diskType = (mediaType == 4) ? "SSD" : ((mediaType == 3) ? "HDD" : "حافظه دیسک");
                string busName = (busType == 17) ? "NVMe PCIe" : ((busType == 11) ? "SATA" : "مستقیم");
                string healthLabel = (health == 0) ? "کاملاً سالم (Healthy)" : "نیازمند بررسی";

                var item = new DiskItemInfo
                {
                    Model = name,
                    DiskType = diskType,
                    BusType = busName,
                    SizeFormatted = Models.SystemStats.FormatBytes(size),
                    HealthStatus = healthLabel
                };

                if (reliabilityDict.TryGetValue(id, out var rel) || reliabilityDict.Count > 0)
                {
                    var r = reliabilityDict.ContainsKey(id) ? rel : System.Linq.Enumerable.First(reliabilityDict.Values);
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

                specs.Disks.Add(item);
            }
        }

        private static void GetBatteryHealthInfo(DetailedSystemSpecs specs)
        {
            try
            {
                // Check if battery exists via WMI
                using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining FROM Win32_Battery");
                var bColl = searcher.Get();
                if (bColl.Count == 0)
                {
                    specs.HasBattery = false;
                    return;
                }
                specs.HasBattery = true;

                long design = 0;
                long full = 0;
                int cycles = 0;

                try
                {
                    using var wmiSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData");
                    foreach (var mo in wmiSearcher.Get())
                    {
                        if (mo["DesignedCapacity"] != null)
                        {
                            design = Convert.ToInt64(mo["DesignedCapacity"]);
                            break;
                        }
                    }
                }
                catch { }

                try
                {
                    using var wmiSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
                    foreach (var mo in wmiSearcher.Get())
                    {
                        if (mo["FullChargedCapacity"] != null)
                        {
                            full = Convert.ToInt64(mo["FullChargedCapacity"]);
                            break;
                        }
                    }
                }
                catch { }

                try
                {
                    using var wmiSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CycleCount FROM BatteryCycleCount");
                    foreach (var mo in wmiSearcher.Get())
                    {
                        if (mo["CycleCount"] != null)
                        {
                            cycles = Convert.ToInt32(mo["CycleCount"]);
                            break;
                        }
                    }
                }
                catch { }

                specs.BatteryDesignCapacityMWh = design;
                specs.BatteryFullChargeCapacityMWh = full;
                specs.BatteryCycleCount = cycles;

                if (design > 0 && full > 0)
                {
                    double pct = ((double)full / (double)design) * 100.0;
                    specs.BatteryHealthPercent = Math.Clamp((int)Math.Round(pct), 1, 100);
                }
            }
            catch { }
        }

        public static string GenerateReportText(DetailedSystemSpecs specs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=================================================");
            sb.AppendLine("         CLEANC - گزارش کامل مشخصات سیستم        ");
            sb.AppendLine("=================================================");
            sb.AppendLine();
            sb.AppendLine($"[پردازنده CPU]: {specs.CpuName}");
            sb.AppendLine($"  • هسته‌ها: {specs.CpuCores} هسته واقعی / {specs.CpuThreads} ترد منطقی");
            sb.AppendLine($"  • حداکثر فرکانس: {specs.CpuClockMhz} MHz");
            if (specs.CpuL3CacheKb > 0) sb.AppendLine($"  • کش L3: {specs.CpuL3CacheKb} KB");
            sb.AppendLine();

            sb.AppendLine($"[حافظه رم RAM]: {specs.TotalRamFormatted} ({specs.RamType})");
            sb.AppendLine($"  • اسلات‌های فعال: {specs.RamSlotsUsed}");
            sb.AppendLine($"  • سرعت باس: {specs.RamSpeedMhz} MHz");
            if (!string.IsNullOrEmpty(specs.RamManufacturer)) sb.AppendLine($"  • سازنده: {specs.RamManufacturer}");
            sb.AppendLine();

            sb.AppendLine($"[مادربرد و بایوس]: {specs.MotherboardManufacturer} {specs.MotherboardModel}");
            if (!string.IsNullOrEmpty(specs.BiosVersion)) sb.AppendLine($"  • نسخه بایوس: {specs.BiosVersion} ({specs.BiosReleaseDate})");
            sb.AppendLine();

            sb.AppendLine("[پردازنده‌های گرافیکی GPU]:");
            foreach (var gpu in specs.Gpus)
            {
                sb.AppendLine($"  • {gpu.Name} | حافظه VRAM: {gpu.VramFormatted} | درایور: {gpu.DriverVersion}");
            }
            sb.AppendLine();

            sb.AppendLine("[دیسک‌ها و حافظه ذخیره‌سازی]:");
            foreach (var d in specs.Disks)
            {
                sb.AppendLine($"  • {d.Model} ({d.SizeFormatted} - {d.DiskType} {d.BusType}) - {d.HealthStatus}");
                sb.AppendLine($"    دمای دیسک: {d.TemperatureFormatted} | کارکرد: {d.PowerOnHoursFormatted} | سلامت: {d.WearFormatted}");
            }
            sb.AppendLine();

            if (specs.HasBattery)
            {
                sb.AppendLine("[باتری لپ‌تاپ]:");
                sb.AppendLine($"  • درصد سلامت باتری: {specs.BatteryHealthPercent}%");
                sb.AppendLine($"  • ظرفیت کارخانه (Design): {specs.BatteryDesignCapacityMWh} mWh");
                sb.AppendLine($"  • ظرفیت فعلی شارژ کامل: {specs.BatteryFullChargeCapacityMWh} mWh");
                if (specs.BatteryCycleCount > 0) sb.AppendLine($"  • سیکل شارژ: {specs.BatteryCycleCount}");
                sb.AppendLine();
            }

            sb.AppendLine($"[سیستم‌عامل]: {specs.OsName} ({specs.OsArchitecture})");
            sb.AppendLine($"  • شماره ساخت Build: {specs.OsBuild}");
            if (!string.IsNullOrEmpty(specs.OsInstallDate)) sb.AppendLine($"  • تاریخ نصب: {specs.OsInstallDate}");
            if (!string.IsNullOrEmpty(specs.OsUptimeFormatted)) sb.AppendLine($"  • زمان روشن بودن سیستم: {specs.OsUptimeFormatted}");
            sb.AppendLine();
            sb.AppendLine($"تولید شده توسط CleanC در {DateTime.Now:yyyy/MM/dd HH:mm:ss}");

            return sb.ToString();
        }
    }
}
