using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class LeftoverFolderItem
    {
        public string FolderName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string LocationType { get; set; } = "AppData"; // "AppData", "LocalAppData", "ProgramData"
    }

    public class LeftoverCleanerService
    {
        // Protected vendor & system directories that should NEVER be considered leftovers
        private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft", "Windows", "WindowsApps", "Packages", "Common Files", "Internet Explorer",
            "NVIDIA Corporation", "NVIDIA", "AMD", "Intel", "Realtek", "Synaptics", "Google",
            "Mozilla", "CleanC", "Git", "dotnet", "Adobe", "Steam", "Epic Games", "Discord",
            "Telegram Desktop", "Spotify", "Temp", "CrashDumps", "Programs", "System",
            "Downloaded Installations", "Package Cache", "OEM", "ASUS", "Lenovo", "Dell", "HP", "MSI",
            "PowerShell", "dotnet-tools", "yarn", "npm", "pip", "vcpkg", "VirtualStore"
        };

        public static List<LeftoverFolderItem> ScanLeftoverFolders()
        {
            var leftovers = new List<LeftoverFolderItem>();

            try
            {
                // 1. Collect all installed application names and executable directories
                var installedNames = GetInstalledSoftwareSignatures();

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

                // 2. Scan AppData (Roaming)
                ScanCandidateDirectory(appData, "AppData", installedNames, leftovers);

                // 3. Scan LocalAppData
                ScanCandidateDirectory(localApp, "LocalAppData", installedNames, leftovers);

                // 4. Scan ProgramData
                ScanCandidateDirectory(programData, "ProgramData", installedNames, leftovers);
            }
            catch { }

            return leftovers;
        }

        private static void ScanCandidateDirectory(string rootPath, string locType, HashSet<string> installedSignatures, List<LeftoverFolderItem> result)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return;

            try
            {
                foreach (var dir in Directory.GetDirectories(rootPath))
                {
                    string dirName = Path.GetFileName(dir);

                    if (ProtectedNames.Contains(dirName)) continue;
                    if (dirName.StartsWith(".") || dirName.StartsWith("$")) continue;

                    // Check if folder name matches any currently installed software or Program Files folder
                    bool isMatched = installedSignatures.Any(sig =>
                        sig.Contains(dirName, StringComparison.OrdinalIgnoreCase) ||
                        dirName.Contains(sig, StringComparison.OrdinalIgnoreCase));

                    if (!isMatched)
                    {
                        // Check if corresponding executable exists in Program Files or Program Files (x86)
                        if (!ExistsInProgramFiles(dirName))
                        {
                            long size = GetDirectorySizeSafe(dir);
                            if (size > 1024 * 1024) // Only consider significant leftovers (> 1 MB)
                            {
                                result.Add(new LeftoverFolderItem
                                {
                                    FolderName = dirName,
                                    FullPath = dir,
                                    SizeBytes = size,
                                    LocationType = locType
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static bool ExistsInProgramFiles(string name)
        {
            string p1 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string p2 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            if (Directory.Exists(Path.Combine(p1, name)) || Directory.Exists(Path.Combine(p2, name))) return true;

            return false;
        }

        private static HashSet<string> GetInstalledSoftwareSignatures()
        {
            var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] keys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var keyPath in keys)
            {
                try
                {
                    using var hklmKey = Registry.LocalMachine.OpenSubKey(keyPath);
                    if (hklmKey != null)
                    {
                        foreach (var sub in hklmKey.GetSubKeyNames())
                        {
                            using var app = hklmKey.OpenSubKey(sub);
                            string? name = app?.GetValue("DisplayName")?.ToString();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                foreach (var word in name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (word.Length >= 4) signatures.Add(word);
                                }
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    using var hkcuKey = Registry.CurrentUser.OpenSubKey(keyPath);
                    if (hkcuKey != null)
                    {
                        foreach (var sub in hkcuKey.GetSubKeyNames())
                        {
                            using var app = hkcuKey.OpenSubKey(sub);
                            string? name = app?.GetValue("DisplayName")?.ToString();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                foreach (var word in name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (word.Length >= 4) signatures.Add(word);
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // Also add all current folders in Program Files
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (Directory.Exists(pf))
                {
                    foreach (var d in Directory.GetDirectories(pf)) signatures.Add(Path.GetFileName(d));
                }
                string pfx = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (Directory.Exists(pfx))
                {
                    foreach (var d in Directory.GetDirectories(pfx)) signatures.Add(Path.GetFileName(d));
                }
            }
            catch { }

            return signatures;
        }

        private static long GetDirectorySizeSafe(string dirPath)
        {
            long size = 0;
            try
            {
                var dir = new DirectoryInfo(dirPath);
                foreach (var fi in dir.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true }))
                {
                    size += fi.Length;
                }
            }
            catch { }
            return size;
        }
    }
}
