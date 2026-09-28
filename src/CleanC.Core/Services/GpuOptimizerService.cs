using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class GpuSystemInfo
    {
        public bool HasDualGpu { get; set; }
        public string DedicatedGpuName { get; set; } = string.Empty;
        public string IntegratedGpuName { get; set; } = string.Empty;
        public List<string> AllGpuNames { get; set; } = new();
    }

    public class GpuOptimizationResult
    {
        public bool HasDualGpu { get; set; }
        public int ConfiguredAppsCount { get; set; }
        public List<string> ConfiguredAppNames { get; set; } = new();
        public string DedicatedGpuName { get; set; } = string.Empty;
    }

    public class GpuOptimizerService
    {
        private static readonly string[] HeavyAppSignatures = new[]
        {
            "Photoshop.exe", "Premiere Pro.exe", "AfterFX.exe", "Illustrator.exe",
            "Lightroom.exe", "Audition.exe", "blender.exe", "3dsmax.exe", "maya.exe",
            "cinema4d.exe", "UnrealEditor.exe", "Unity.exe", "Resolve.exe",
            "acad.exe", "SLDWORKS.exe"
        };

        public static GpuSystemInfo DetectGpuConfiguration()
        {
            var info = new GpuSystemInfo();

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (var mo in searcher.Get())
                {
                    string? name = mo["Name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        info.AllGpuNames.Add(name);

                        if (IsDedicatedGpu(name))
                        {
                            info.DedicatedGpuName = name;
                        }
                        else if (IsIntegratedGpu(name))
                        {
                            info.IntegratedGpuName = name;
                        }
                    }
                }

                info.HasDualGpu = !string.IsNullOrEmpty(info.DedicatedGpuName) && !string.IsNullOrEmpty(info.IntegratedGpuName);
            }
            catch { }

            return info;
        }

        public static async Task<GpuOptimizationResult> OptimizeGpuPreferencesAsync()
        {
            return await Task.Run(() =>
            {
                var result = new GpuOptimizationResult();
                var gpuInfo = DetectGpuConfiguration();

                result.HasDualGpu = gpuInfo.HasDualGpu;
                result.DedicatedGpuName = gpuInfo.DedicatedGpuName;

                if (!gpuInfo.HasDualGpu)
                {
                    return result; // Single GPU system does not require discrete vs integrated preference steering
                }

                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
                    if (key == null) return result;

                    // 1. Discover installed creative / 3D / engineering apps
                    var heavyExes = FindHeavyApplicationExecutables();

                    // 2. Discover installed games (Steam, Epic, Riot)
                    var gameExes = FindGameExecutables();

                    var candidateExes = heavyExes.Concat(gameExes).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    foreach (var exePath in candidateExes)
                    {
                        if (!File.Exists(exePath)) continue;

                        string? existingVal = key.GetValue(exePath)?.ToString();

                        // If not set or set to Windows Default (0), route to Dedicated High-Performance GPU (2)
                        if (string.IsNullOrEmpty(existingVal) || existingVal.Contains("GpuPreference=0"))
                        {
                            key.SetValue(exePath, "GpuPreference=2;", RegistryValueKind.String);
                            result.ConfiguredAppsCount++;
                            result.ConfiguredAppNames.Add(Path.GetFileName(exePath));
                        }
                    }

                    // 3. Ensure browsers are not draining battery on dedicated GPU
                    OptimizeBrowserPreferences(key);
                }
                catch { }

                return result;
            });
        }

        private static void OptimizeBrowserPreferences(RegistryKey key)
        {
            try
            {
                var browserNames = new[] { "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe" };
                var valueNames = key.GetValueNames();

                foreach (var valName in valueNames)
                {
                    string fileName = Path.GetFileName(valName);
                    if (browserNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                    {
                        string? currentVal = key.GetValue(valName)?.ToString();
                        // If browser was mistakenly forced to dedicated GPU (2), set to Power-saving (1)
                        if (currentVal != null && currentVal.Contains("GpuPreference=2"))
                        {
                            key.SetValue(valName, "GpuPreference=1;", RegistryValueKind.String);
                        }
                    }
                }
            }
            catch { }
        }

        private static List<string> FindHeavyApplicationExecutables()
        {
            var found = new List<string>();

            string p1 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string p2 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var searchRoots = new[]
            {
                Path.Combine(p1, "Adobe"),
                Path.Combine(p2, "Adobe"),
                Path.Combine(p1, "Blender Foundation"),
                Path.Combine(p1, "Autodesk"),
                Path.Combine(p1, "Blackmagic Design"),
                Path.Combine(p1, "Unity"),
                Path.Combine(p1, "Epic Games")
            };

            foreach (var root in searchRoots)
            {
                if (!Directory.Exists(root)) continue;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories))
                    {
                        string fileName = Path.GetFileName(file);
                        if (HeavyAppSignatures.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                        {
                            found.Add(file);
                        }
                    }
                }
                catch { }
            }

            return found;
        }

        private static List<string> FindGameExecutables()
        {
            var found = new List<string>();

            try
            {
                // Steam library games
                string pfx = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string steamCommon = Path.Combine(pfx, @"Steam\steamapps\common");
                if (Directory.Exists(steamCommon))
                {
                    foreach (var gameDir in Directory.GetDirectories(steamCommon))
                    {
                        try
                        {
                            foreach (var exe in Directory.EnumerateFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly))
                            {
                                // Only main game executables (> 5 MB, not unins*.exe or crash report)
                                var fi = new FileInfo(exe);
                                if (fi.Length > 5 * 1024 * 1024 && !fi.Name.StartsWith("unins", StringComparison.OrdinalIgnoreCase))
                                {
                                    found.Add(exe);
                                }
                            }
                        }
                        catch { }
                    }
                }

                // Epic Games
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string epicCommon = Path.Combine(pf, @"Epic Games");
                if (Directory.Exists(epicCommon))
                {
                    foreach (var gameDir in Directory.GetDirectories(epicCommon))
                    {
                        try
                        {
                            foreach (var exe in Directory.EnumerateFiles(gameDir, "*.exe", SearchOption.AllDirectories))
                            {
                                var fi = new FileInfo(exe);
                                if (fi.Length > 10 * 1024 * 1024 && !fi.Name.StartsWith("unins", StringComparison.OrdinalIgnoreCase))
                                {
                                    found.Add(exe);
                                }
                            }
                        }
                        catch { }
                    }
                }

                // Riot Games
                string riotDir = @"C:\Riot Games";
                if (Directory.Exists(riotDir))
                {
                    foreach (var exe in Directory.EnumerateFiles(riotDir, "*.exe", SearchOption.AllDirectories))
                    {
                        if (exe.Contains("VALORANT", StringComparison.OrdinalIgnoreCase) ||
                            exe.Contains("LeagueClient", StringComparison.OrdinalIgnoreCase))
                        {
                            found.Add(exe);
                        }
                    }
                }
            }
            catch { }

            return found;
        }

        private static bool IsDedicatedGpu(string name)
        {
            return name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Arc(TM)", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIntegratedGpu(string name)
        {
            return name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Vega", StringComparison.OrdinalIgnoreCase);
        }
    }
}
