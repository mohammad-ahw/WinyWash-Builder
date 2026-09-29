using System;
using System.Collections.Generic;
using System.Management;
using System.Threading.Tasks;

namespace CleanC.Core.Services
{
    public class DetailedSystemSpecs
    {
        // CPU
        public string CpuName { get; set; } = string.Empty;
        public int CpuCores { get; set; }
        public int CpuThreads { get; set; }
        public int CpuClockMhz { get; set; }

        // Motherboard
        public string MotherboardManufacturer { get; set; } = string.Empty;
        public string MotherboardModel { get; set; } = string.Empty;

        // RAM
        public string TotalRamFormatted { get; set; } = string.Empty;
        public int RamSlotsUsed { get; set; }
        public int RamSpeedMhz { get; set; }
        public string RamType { get; set; } = "DDR4 / DDR5";

        // OS
        public string OsName { get; set; } = string.Empty;
        public string OsBuild { get; set; } = string.Empty;

        // Battery (if laptop)
        public bool HasBattery { get; set; }
        public int BatteryHealthPercent { get; set; } = 100;
        public long BatteryDesignCapacityMWh { get; set; }
        public long BatteryFullChargeCapacityMWh { get; set; }
        public int BatteryCycleCount { get; set; }

        // GPU
        public List<string> GpuNames { get; set; } = new List<string>();

        // SSD Health
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

                // 1. CPU Info
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
                    foreach (var mo in searcher.Get())
                    {
                        specs.CpuName = mo["Name"]?.ToString()?.Trim() ?? string.Empty;
                        specs.CpuCores = mo["NumberOfCores"] != null ? Convert.ToInt32(mo["NumberOfCores"]) : 0;
                        specs.CpuThreads = mo["NumberOfLogicalProcessors"] != null ? Convert.ToInt32(mo["NumberOfLogicalProcessors"]) : 0;
                        specs.CpuClockMhz = mo["MaxClockSpeed"] != null ? Convert.ToInt32(mo["MaxClockSpeed"]) : 0;
                        break;
                    }
                }
                catch { }

                // 2. Motherboard Info
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

                // 3. RAM Details
                try
                {
                    long totalBytes = 0;
                    int count = 0;
                    int speed = 0;
                    using var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed, MemoryType, SMBIOSMemoryType FROM Win32_PhysicalMemory");
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
                    }
                    specs.TotalRamFormatted = Models.SystemStats.FormatBytes(totalBytes);
                    specs.RamSlotsUsed = count;
                    specs.RamSpeedMhz = speed;
                }
                catch { }

                // 4. OS Version
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Caption, BuildNumber FROM Win32_OperatingSystem");
                    foreach (var mo in searcher.Get())
                    {
                        specs.OsName = mo["Caption"]?.ToString()?.Trim() ?? string.Empty;
                        specs.OsBuild = mo["BuildNumber"]?.ToString()?.Trim() ?? string.Empty;
                        break;
                    }
                }
                catch { }

                // 5. Battery Health (Laptop)
                try
                {
                    GetBatteryHealthInfo(specs);
                }
                catch { }

                // 6. GPU Info
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                    foreach (var mo in searcher.Get())
                    {
                        var name = mo["Name"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(name))
                        {
                            specs.GpuNames.Add(name);
                        }
                    }
                }
                catch { }

                // 7. SSD Health
                try
                {
                    using var diskSearcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DeviceId FROM MSFT_PhysicalDisk WHERE MediaType=4");
                    string? ssdDeviceId = null;
                    foreach (var disk in diskSearcher.Get())
                    {
                        ssdDeviceId = disk["DeviceId"]?.ToString();
                        break; // Just take the first SSD for now
                    }

                    if (!string.IsNullOrEmpty(ssdDeviceId))
                    {
                        using var healthSearcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", $"SELECT Temperature, Wear, PowerOnHours FROM MSFT_StorageReliabilityCounter WHERE DeviceId='{ssdDeviceId}'");
                        foreach (var health in healthSearcher.Get())
                        {
                            if (health["Temperature"] != null)
                                specs.SsdTemperatureCelsius = Convert.ToInt32(health["Temperature"]);
                            if (health["Wear"] != null)
                                specs.SsdWearPercent = Convert.ToInt32(health["Wear"]);
                            if (health["PowerOnHours"] != null)
                                specs.SsdOperatingHours = Convert.ToInt64(health["PowerOnHours"]);
                            
                            specs.HasSsdHealthData = true;
                            break;
                        }
                    }
                }
                catch { }

                return specs;
            });
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
    }
}
