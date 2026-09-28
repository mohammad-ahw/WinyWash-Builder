using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class StartupItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty; // "HKCU", "HKLM", "UserStartupFolder", "CommonStartupFolder"
        public bool IsNonEssential { get; set; }
        public bool IsDisabled { get; set; }
        public string Category { get; set; } = "برنامه کاربردی";
    }

    public class StartupOptimizerService
    {
        private const string CleanCBackupKey = @"Software\CleanC\DisabledStartup";

        // Signatures of non-essential launchers, social apps, updaters
        private static readonly string[] NonEssentialSignatures = new[]
        {
            "discord", "spotify", "steam", "epicgames", "telegram", "whatsapp",
            "zoom", "adobe", "creative cloud", "googledrive", "skype", "battle.net",
            "riotclient", "viber", "teams", "ituneshelper", "ccleaner", "torrent",
            "utorrent", "bittorrent", "anydesk", "teamviewer", "vlc", "origin", "ea desktop"
        };

        // Whitelist signatures (drivers, audio, touchpad, security) - NEVER TOUCH
        private static readonly string[] WhitelistSignatures = new[]
        {
            "security", "defender", "realtek", "synaptics", "elan", "conexant",
            "waves", "nvidia", "amd", "intel", "logitech", "corsair", "razer",
            "bluetooth", "audio", "touchpad", "driver", "antivirus", "smartaudio", "vpn"
        };

        public static List<StartupItem> GetStartupItems()
        {
            var list = new List<StartupItem>();

            // 1. HKCU Run
            ScanRegistryRunKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU", list);

            // 2. HKLM Run
            ScanRegistryRunKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM", list);

            // 3. User Startup Folder
            string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            ScanStartupFolder(userStartup, "UserStartupFolder", list);

            // 4. Common Startup Folder
            string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            ScanStartupFolder(commonStartup, "CommonStartupFolder", list);

            return list;
        }

        public static async Task<int> OptimizeStartupAppsAsync()
        {
            return await Task.Run(() =>
            {
                int disabledCount = 0;
                var items = GetStartupItems();

                foreach (var item in items)
                {
                    if (item.IsNonEssential && !item.IsDisabled)
                    {
                        if (DisableStartupItem(item))
                        {
                            disabledCount++;
                        }
                    }
                }

                return disabledCount;
            });
        }

        public static bool DisableStartupItem(StartupItem item)
        {
            try
            {
                // Save backup to CleanC key for easy 1-click restore
                using (var bkKey = Registry.CurrentUser.CreateSubKey(CleanCBackupKey))
                {
                    if (bkKey != null)
                    {
                        string entryData = $"{item.Location}|{item.Command}";
                        bkKey.SetValue(item.Name, entryData, RegistryValueKind.String);
                    }
                }

                // Remove from active location
                if (item.Location == "HKCU")
                {
                    using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    runKey?.DeleteValue(item.Name, false);
                    return true;
                }
                else if (item.Location == "HKLM")
                {
                    using var runKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    runKey?.DeleteValue(item.Name, false);
                    return true;
                }
                else if (item.Location == "UserStartupFolder" || item.Location == "CommonStartupFolder")
                {
                    if (File.Exists(item.Command))
                    {
                        string disabledPath = item.Command + ".disabled";
                        if (File.Exists(disabledPath)) File.Delete(disabledPath);
                        File.Move(item.Command, disabledPath);
                        return true;
                    }
                }
            }
            catch
            {
                // Access denied or item locked
            }

            return false;
        }

        public static bool RestoreStartupItem(string itemName)
        {
            try
            {
                using var bkKey = Registry.CurrentUser.OpenSubKey(CleanCBackupKey, true);
                if (bkKey == null) return false;

                string? val = bkKey.GetValue(itemName) as string;
                if (string.IsNullOrEmpty(val)) return false;

                var parts = val.Split('|');
                if (parts.Length < 2) return false;

                string location = parts[0];
                string command = parts[1];

                if (location == "HKCU")
                {
                    using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                    runKey?.SetValue(itemName, command);
                }
                else if (location == "HKLM")
                {
                    using var runKey = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                    runKey?.SetValue(itemName, command);
                }
                else if (location == "UserStartupFolder" || location == "CommonStartupFolder")
                {
                    string disabledPath = command + ".disabled";
                    if (File.Exists(disabledPath))
                    {
                        File.Move(disabledPath, command);
                    }
                }

                bkKey.DeleteValue(itemName, false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ScanRegistryRunKey(RegistryKey root, string subKeyPath, string locationName, List<StartupItem> list)
        {
            try
            {
                using var key = root.OpenSubKey(subKeyPath, false);
                if (key == null) return;

                foreach (var valName in key.GetValueNames())
                {
                    string? cmd = key.GetValue(valName)?.ToString();
                    if (string.IsNullOrWhiteSpace(cmd)) continue;

                    bool isProtected = IsWhitelisted(valName, cmd);
                    bool isNonEssential = !isProtected && IsNonEssentialApp(valName, cmd);

                    list.Add(new StartupItem
                    {
                        Id = $"{locationName}_{valName}",
                        Name = valName,
                        Command = cmd,
                        Location = locationName,
                        IsNonEssential = isNonEssential,
                        IsDisabled = false,
                        Category = isNonEssential ? "لانچر / بهینه‌سازی شده" : "ضروری سیستمی"
                    });
                }
            }
            catch { }
        }

        private static void ScanStartupFolder(string folderPath, string locationName, List<StartupItem> list)
        {
            try
            {
                if (!Directory.Exists(folderPath)) return;

                foreach (var file in Directory.GetFiles(folderPath, "*.*"))
                {
                    string fname = Path.GetFileName(file);
                    bool isDisabled = fname.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    string cleanName = isDisabled ? fname.Substring(0, fname.Length - 9) : fname;

                    bool isProtected = IsWhitelisted(cleanName, file);
                    bool isNonEssential = !isProtected && IsNonEssentialApp(cleanName, file);

                    list.Add(new StartupItem
                    {
                        Id = $"{locationName}_{cleanName}",
                        Name = cleanName,
                        Command = file,
                        Location = locationName,
                        IsNonEssential = isNonEssential,
                        IsDisabled = isDisabled,
                        Category = isNonEssential ? "میانبر استارتاپ" : "ضروری سیستمی"
                    });
                }
            }
            catch { }
        }

        private static bool IsWhitelisted(string name, string cmd)
        {
            string combined = $"{name} {cmd}".ToLowerInvariant();
            return WhitelistSignatures.Any(w => combined.Contains(w));
        }

        private static bool IsNonEssentialApp(string name, string cmd)
        {
            string combined = $"{name} {cmd}".ToLowerInvariant();
            return NonEssentialSignatures.Any(s => combined.Contains(s));
        }
    }
}
