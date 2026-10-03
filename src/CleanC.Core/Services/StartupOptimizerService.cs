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
        public string Location { get; set; } = string.Empty; // "HKCU", "HKLM", "HKLM (32-bit)", "UserStartupFolder", "CommonStartupFolder", "WindowsService"
        public bool IsNonEssential { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsVpnOrProxy { get; set; }
        public string Category { get; set; } = "برنامه کاربردی";
    }

    public class StartupOptimizerService
    {
        private const string CleanCBackupKey = @"Software\CleanC\DisabledStartup";

        // Comprehensive VPN & Proxy client signatures (Local & International)
        private static readonly string[] VpnSignatures = new[]
        {
            "warp", "cloudflare", "wireguard", "openvpn", "v2ray", "v2rayn", "xray",
            "sing-box", "singbox", "hiddify", "clash", "clash-verge", "clash verge",
            "flclash", "mihomo", "nekoray", "nekobox", "karing", "amnezia", "shadowsocks",
            "psiphon", "outline", "windscribe", "surfshark", "nordvpn", "expressvpn",
            "protonvpn", "proton vpn", "x-vpn", "xvpn", "hotspot shield", "hotspotshield",
            "mullvad", "tailscale", "zerotier", "cisco anyconnect", "anyconnect",
            "forticlient", "fortinet", "kerio", "softether", "lantern", "ultrasurf",
            "freegate", "geph", "tor browser", "tor.exe", "privado", "purevpn", "cyberghost"
        };

        // Signatures of non-essential launchers, cloud sync, and updaters
        private static readonly string[] NonEssentialSignatures = new[]
        {
            // Cloud & Sync
            "onedrive", "dropbox", "icloud", "mega", "megasync", "nextcloud", "googledrive", "google drive",

            // Software Updaters & Background Schedulers
            "googleupdate", "google update", "edgeupdate", "edge update", "jusched", "java update",
            "adobearm", "adobe updater", "acrotray", "creative cloud", "ccleaner", "driver booster",
            "driverbooster", "iobit", "advanced systemcare", "ituneshelper",

            // Social, Chat & Communication
            "discord", "spotify", "telegram", "whatsapp", "zoom", "skype", "viber", "slack", "teams", "msteams",

            // Browsers
            "operagx", "opera gx", "brave", "yandex",

            // Gaming Launchers & Clients
            "steam", "epicgames", "epic games", "battle.net", "battlenet", "origin", "ea app",
            "eadestktop", "ubisoft connect", "uplay", "gog galaxy", "riotclient", "riot client", "xboxapp",

            // Torrent & Remote desktop auto-starts
            "utorrent", "bittorrent", "qbittorrent", "anydesk", "teamviewer",

            // Unnecessary OEM Support Bloat
            "hp support assistant", "hpsupportassistant", "dell supportassist", "supportassist",
            "acer care center", "acer care", "lenovosystemupdate"
        };

        // Safe auto-disable targets during 1-Click Quick Clean (updaters and bloatware helpers ONLY)
        private static readonly string[] SafeAutoDisableSignatures = new[]
        {
            "googleupdate", "edgeupdate", "jusched", "adobearm", "adobe updater", "acrotray",
            "ccleaner", "driver booster", "driverbooster", "iobit", "advanced systemcare", "ituneshelper",
            "hp support assistant", "dell supportassist", "acer care center", "lenovosystemupdate"
        };

        // Whitelist signatures (hardware drivers, keyboard lighting, audio, touchpad, security) - NEVER TOUCH
        private static readonly string[] WhitelistSignatures = new[]
        {
            // Core Security
            "security", "defender", "securityhealth", "antivirus",

            // Audio Drivers
            "realtek", "conexant", "waves", "smartaudio", "audio", "sound", "nahimic",

            // Touchpad, Input & Keyboard
            "synaptics", "elan", "alps", "touchpad", "trackpoint",

            // Hardware Lighting & Performance Controllers
            "dragon center", "msi center", "mystic light", "alienware", "armoury crate",
            "legion space", "backlight", "keyboard", "fan control",

            // Graphics & Display
            "nvidia", "nvbackend", "nvdisplay", "amd", "radeon", "intel", "igfx",

            // Network Hardware
            "bluetooth", "wireless", "wlan", "wifi", "ethernet"
        };

        public static List<StartupItem> GetStartupItems()
        {
            var list = new List<StartupItem>();

            // 1. HKCU Run
            ScanRegistryRunKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU", list);

            // 2. HKLM Run (64-bit native)
            ScanRegistryRunKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM", list);

            // 3. HKLM WOW6432Node Run (32-bit apps on 64-bit Windows - very common for VPNs and tools!)
            ScanRegistryRunKey(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM (32-bit)", list);

            // 4. User Startup Folder
            string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            ScanStartupFolder(userStartup, "UserStartupFolder", list);

            // 5. Common Startup Folder
            string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            ScanStartupFolder(commonStartup, "CommonStartupFolder", list);

            // 6. VPN Background Windows Services (Automatic Start)
            ScanVpnWindowsServices(list);

            // 7. Items previously disabled and backed up by CleanC
            ScanCleanCDisabledItems(list);

            return list;
        }

        public static async Task<int> OptimizeStartupAppsAsync(bool aggressive = false)
        {
            return await Task.Run(() =>
            {
                int disabledCount = 0;
                var items = GetStartupItems();

                foreach (var item in items)
                {
                    // Never silently disable VPNs or Chat apps during Quick Boost!
                    if (item.IsVpnOrProxy || item.Category.Contains("پیام‌رسان")) continue;

                    bool candidate = aggressive
                        ? (item.IsNonEssential && !item.IsDisabled)
                        : (!item.IsDisabled && IsSafeToAutoDisable(item.Name, item.Command));

                    if (candidate)
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
                else if (item.Location == "HKLM (32-bit)")
                {
                    using var runKey = Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", true);
                    runKey?.DeleteValue(item.Name, false);
                    return true;
                }
                else if (item.Location == "WindowsService")
                {
                    using var svcKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{item.Name}", true);
                    if (svcKey != null)
                    {
                        svcKey.SetValue("Start", 3, RegistryValueKind.DWord); // 3 = Manual (Demand start)
                        return true;
                    }
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
                else if (location == "HKLM (32-bit)")
                {
                    using var runKey = Registry.LocalMachine.CreateSubKey(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run");
                    runKey?.SetValue(itemName, command);
                }
                else if (location == "WindowsService")
                {
                    using var svcKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{itemName}", true);
                    svcKey?.SetValue("Start", 2, RegistryValueKind.DWord); // 2 = Automatic
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
                    bool isVpn = IsVpnApp(valName, cmd);
                    bool isNonEssential = !isProtected;
                    string cat = ResolveStartupCategory(valName, cmd, isProtected);

                    bool isDisabledInTaskManager = IsDisabledInStartupApproved(root, valName);

                    list.Add(new StartupItem
                    {
                        Id = $"{locationName}_{valName}",
                        Name = valName,
                        Command = cmd,
                        Location = locationName,
                        IsNonEssential = isNonEssential,
                        IsDisabled = isDisabledInTaskManager,
                        IsVpnOrProxy = isVpn,
                        Category = cat
                    });
                }
            }
            catch { }
        }

        private static bool IsDisabledInStartupApproved(RegistryKey root, string valName)
        {
            try
            {
                using var saKey = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", false);
                if (saKey != null)
                {
                    var bytes = saKey.GetValue(valName) as byte[];
                    if (bytes != null && bytes.Length > 0)
                    {
                        // In Windows 10/11 Task Manager: odd byte = disabled (0x01, 0x03, etc.)
                        return (bytes[0] % 2 != 0);
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool IsDisabledInStartupApprovedFolder(string fileName)
        {
            try
            {
                var roots = new[] { Registry.CurrentUser, Registry.LocalMachine };
                foreach (var root in roots)
                {
                    using var saKey = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", false);
                    if (saKey != null)
                    {
                        var bytes = saKey.GetValue(fileName) as byte[];
                        if (bytes != null && bytes.Length > 0 && (bytes[0] % 2 != 0))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static void ScanStartupFolder(string folderPath, string locationName, List<StartupItem> list)
        {
            try
            {
                if (!Directory.Exists(folderPath)) return;

                foreach (var file in Directory.GetFiles(folderPath, "*.*"))
                {
                    string fname = Path.GetFileName(file);
                    bool isDisabledInApproved = IsDisabledInStartupApprovedFolder(fname);
                    bool isRenamedDisabled = fname.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    bool isDisabled = isRenamedDisabled || isDisabledInApproved;
                    string cleanName = isRenamedDisabled ? fname.Substring(0, fname.Length - 9) : fname;

                    bool isProtected = IsWhitelisted(cleanName, file);
                    bool isVpn = IsVpnApp(cleanName, file);
                    bool isNonEssential = !isProtected;
                    string cat = ResolveStartupCategory(cleanName, file, isProtected);

                    list.Add(new StartupItem
                    {
                        Id = $"{locationName}_{cleanName}",
                        Name = cleanName,
                        Command = file,
                        Location = locationName,
                        IsNonEssential = isNonEssential,
                        IsDisabled = isDisabled,
                        IsVpnOrProxy = isVpn,
                        Category = cat
                    });
                }
            }
            catch { }
        }

        private static void ScanCleanCDisabledItems(List<StartupItem> list)
        {
            try
            {
                using var bkKey = Registry.CurrentUser.OpenSubKey(CleanCBackupKey, false);
                if (bkKey == null) return;

                foreach (var valName in bkKey.GetValueNames())
                {
                    if (list.Any(x => x.Name.Equals(valName, StringComparison.OrdinalIgnoreCase))) continue;

                    string? data = bkKey.GetValue(valName)?.ToString();
                    if (string.IsNullOrWhiteSpace(data)) continue;

                    string loc = "غیرفعال (CleanC)";
                    string cmd = data;
                    var parts = data.Split('|', 2);
                    if (parts.Length == 2)
                    {
                        loc = parts[0];
                        cmd = parts[1];
                    }

                    bool isVpn = IsVpnApp(valName, cmd);
                    string cat = ResolveStartupCategory(valName, cmd, false);

                    list.Add(new StartupItem
                    {
                        Id = $"CleanC_{valName}",
                        Name = valName,
                        Command = cmd,
                        Location = loc,
                        IsNonEssential = true,
                        IsDisabled = true,
                        IsVpnOrProxy = isVpn,
                        Category = cat
                    });
                }
            }
            catch { }
        }

        private static void ScanVpnWindowsServices(List<StartupItem> list)
        {
            try
            {
                using var servicesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", false);
                if (servicesKey == null) return;

                foreach (var subName in servicesKey.GetSubKeyNames())
                {
                    try
                    {
                        using var svc = servicesKey.OpenSubKey(subName, false);
                        if (svc == null) continue;

                        int startType = Convert.ToInt32(svc.GetValue("Start") ?? 3);
                        // Start = 2 (Automatic), 3 (Manual), 4 (Disabled)
                        if (startType != 2 && startType != 3) continue;

                        string dispName = svc.GetValue("DisplayName")?.ToString() ?? subName;
                        string imagePath = svc.GetValue("ImagePath")?.ToString() ?? "";

                        if (IsVpnApp(subName, $"{dispName} {imagePath}"))
                        {
                            list.Add(new StartupItem
                            {
                                Id = $"Service_{subName}",
                                Name = dispName,
                                Command = imagePath,
                                Location = "سرویس ویندوز",
                                IsNonEssential = true,
                                IsDisabled = (startType != 2),
                                IsVpnOrProxy = true,
                                Category = "فیلترشکن و پروکسی (VPN)"
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static string ResolveStartupCategory(string name, string cmd, bool isProtected)
        {
            if (isProtected) return "سخت‌افزار و سیستم (ضروری)";

            string lower = $"{name} {cmd}".ToLowerInvariant();

            if (IsVpnApp(name, cmd))
                return "فیلترشکن و پروکسی (VPN)";

            if (lower.Contains("telegram") || lower.Contains("discord") || lower.Contains("whatsapp") ||
                lower.Contains("skype") || lower.Contains("slack") || lower.Contains("teams") || lower.Contains("viber"))
                return "پیام‌رسان و چت (اختیاری)";

            if (lower.Contains("idm") || lower.Contains("idman") || lower.Contains("download") ||
                lower.Contains("torrent") || lower.Contains("utorrent") || lower.Contains("qbittorrent") ||
                lower.Contains("onedrive") || lower.Contains("dropbox") || lower.Contains("mega"))
                return "دانلود و ابزار ابری (اختیاری)";

            if (lower.Contains("steam") || lower.Contains("epic") || lower.Contains("riot") ||
                lower.Contains("origin") || lower.Contains("battle.net") || lower.Contains("uplay") || lower.Contains("galaxy"))
                return "لانچر بازی و گیمینگ (اختیاری)";

            if (lower.Contains("update") || lower.Contains("updater") || lower.Contains("helper") || lower.Contains("jusched"))
                return "آپدیتر پس‌زمینه (غیرضروری)";

            return "برنامه کاربردی (اختیاری)";
        }

        public static bool IsVpnApp(string name, string cmd)
        {
            string combined = $"{name} {cmd}".ToLowerInvariant();
            return VpnSignatures.Any(v => combined.Contains(v));
        }

        private static bool IsWhitelisted(string name, string cmd)
        {
            string combined = $"{name} {cmd}".ToLowerInvariant();
            return WhitelistSignatures.Any(w => combined.Contains(w));
        }

        public static bool IsNonEssentialApp(string name, string cmd)
        {
            string combined = $"{name} {cmd}".ToLowerInvariant();
            return IsVpnApp(name, cmd) || NonEssentialSignatures.Any(s => combined.Contains(s));
        }

        private static bool IsSafeToAutoDisable(string name, string cmd)
        {
            string combined = $"{name} {cmd}".ToLowerInvariant();
            return SafeAutoDisableSignatures.Any(s => combined.Contains(s));
        }
    }
}
