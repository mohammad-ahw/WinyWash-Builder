using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading.Tasks;
using System.Xml.Linq;

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

                // Run powercfg /batteryreport /xml to get precise design vs full charge capacity
                string tempXml = Path.Combine(Path.GetTempPath(), $"cleanc_bat_{Guid.NewGuid():N}.xml");
                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
                    Arguments = $"/batteryreport /xml /output \"{tempXml}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    p?.WaitForExit(5000);
                }

                if (File.Exists(tempXml))
                {
                    try
                    {
                        var doc = XDocument.Load(tempXml);
                        var batteryNode = doc.Descendants("Battery").FirstOrDefault();
                        if (batteryNode != null)
                        {
                            long design = 0;
                            long full = 0;
                            int cycles = 0;

                            long.TryParse(batteryNode.Element("DesignCapacity")?.Value, out design);
                            long.TryParse(batteryNode.Element("FullChargeCapacity")?.Value, out full);
                            int.TryParse(batteryNode.Element("CycleCount")?.Value, out cycles);

                            specs.BatteryDesignCapacityMWh = design;
                            specs.BatteryFullChargeCapacityMWh = full;
                            specs.BatteryCycleCount = cycles;

                            if (design > 0 && full > 0)
                            {
                                double pct = ((double)full / design) * 100.0;
                                specs.BatteryHealthPercent = Math.Clamp((int)Math.Round(pct), 1, 100);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        try { File.Delete(tempXml); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}
