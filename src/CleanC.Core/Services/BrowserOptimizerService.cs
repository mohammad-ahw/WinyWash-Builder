using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class BrowserDetails
    {
        public string Name { get; set; } = "نامشخص";
        public string ProgId { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public bool IsHardwareAccelerationActive { get; set; }
        public bool IsDedicatedGpuAssigned { get; set; }
    }

    public enum ExtensionRiskCategory
    {
        SafeToClean,       // منسوخ‌شده، خراب با ارور، باقیمانده بدون استفاده
        ReviewWarning,     // سنگین، تزریق به همه سایت‌ها، غیرفعال یا تکراری
        ProtectedWhitelist // حیاتی و غیرقابل حذف (IDM، مترجم، AI RTL و...)
    }

    public class BrowserExtensionInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BrowserName { get; set; } = "Google Chrome";
        public string ProfileName { get; set; } = "Default";
        public long SizeBytes { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool HasGlobalUrlAccess { get; set; }
        public ExtensionRiskCategory Category { get; set; } = ExtensionRiskCategory.ReviewWarning;
        public string Reason { get; set; } = string.Empty;
        public string InstallPath { get; set; } = string.Empty;
    }

    public class BrowserOptimizerService
    {
        public static readonly HashSet<string> ProtectedExtensionIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "ngpampappnmepgilojfohadhhmbhlaek", // IDM Integration Module
            "aapbdbdomjkkjkaonfhkkikfgjllcleb", // Google Translate
            "pjpmebofipgpjaincgoboibbmicccbne", // AI RTL (Persian/Farsi)
            "majdfhpaihoncoakbjgbdhglocklcgno", // Free VPN for Chrome - VeePN
            "dgboeiippahndmcfgbkgnedecdkcklgk"  // Badesaba
        };

        public static readonly HashSet<string> KnownDeadExtensionIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "bpconcjcammlapcogcnnelfmaeghhagj", // Nimbus Screenshot (corrupted error 8388608)
            "cconfdckijkfakepljihmbmfjpafmjij", // Highlight Links
            "jdocbkpgdakpekjlhemmfcncgdjeiika", // Absolute Enable Right Click
            "ljglajjnnkapghbckkcmodicjhacbfhk", // Power Automate (corrupted error 8192)
            "hoklmmgfnpapgjgcpechhaamimifchmp", // Similarweb (inactive/redundant)
            "fdcgdnkidjaadafnichfpabhfomcebme", // ZenMate
            "nbcojefnccbanplpoffopkoepjmhgdgh", // Hoxx VPN Proxy
            "oofgbpoabipfcfjapgnbbjjaenockbdp", // SetupVPN
            "jajilbjjinjmgcibalaakngmkilboobh", // Astar VPN
            "haafibkemckmbknhfkiiniobjpgkebko", // Panda New Tab
            "nmmhkkegccagdldgiimedpiccmgmieda", // Chrome Web Store Payments (deprecated by Google)
            "nkeimhogjdpnpccoofpliimaahmaaome", // Google Hangouts (deprecated by Google)
            "diimdfmlegodnjbcgajllidffehegdbc", // RTL toggle (superseded by AI RTL)
            "hbapdpeemoojbophdfndmlgdhppljgmp", // Keywords Everywhere (deprecated)
            "jabopobgcpjmedljpbcaablpmlmfcogm", // WhatFont (redundant font tool)
            "hgcbkniobaplodikkjlpfjeiajopjidj", // Web2Elementor (redundant)
            "gppongmhjkpfnbhagpmjfkannfbllamg", // Wappalyzer (heavy DOM observer)
            "chdafcbnfkfenoeejpaeenpdamhmalhe", // Share Extensions (bloat)
            "ghbmnnjooekpmoecnnnilnnbdlolhkhi"  // Google Docs Offline (unused)
        };

        public static BrowserDetails GetDefaultBrowser()
        {
            var info = new BrowserDetails();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice", false);
                string progId = key?.GetValue("ProgId")?.ToString() ?? string.Empty;
                info.ProgId = progId;

                if (progId.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
                {
                    info.Name = "Google Chrome";
                    info.ExecutablePath = GetAppPath("chrome.exe") ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe";
                }
                else if (progId.Contains("MSEdge", StringComparison.OrdinalIgnoreCase))
                {
                    info.Name = "Microsoft Edge";
                    info.ExecutablePath = GetAppPath("msedge.exe") ?? @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
                }
                else if (progId.Contains("Firefox", StringComparison.OrdinalIgnoreCase))
                {
                    info.Name = "Mozilla Firefox";
                    info.ExecutablePath = GetAppPath("firefox.exe") ?? @"C:\Program Files\Mozilla Firefox\firefox.exe";
                }
                else if (progId.Contains("Brave", StringComparison.OrdinalIgnoreCase))
                {
                    info.Name = "Brave";
                    info.ExecutablePath = GetAppPath("brave.exe") ?? @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe";
                }
                else if (progId.Contains("Opera", StringComparison.OrdinalIgnoreCase))
                {
                    info.Name = "Opera";
                    info.ExecutablePath = GetAppPath("opera.exe") ?? @"C:\Program Files\Opera\launcher.exe";
                }

                // Check policy or Local State for HW Acceleration
                using var pKey = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Google\Chrome", false);
                int? hw = pKey?.GetValue("HardwareAccelerationModeEnabled") as int?;
                info.IsHardwareAccelerationActive = hw == 1;

                // Check DirectX GPU assignment
                using var dxKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", false);
                if (!string.IsNullOrEmpty(info.ExecutablePath) && dxKey != null)
                {
                    string? pref = dxKey.GetValue(info.ExecutablePath)?.ToString();
                    info.IsDedicatedGpuAssigned = pref != null && pref.Contains("GpuPreference=2");
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex);
            }
            return info;
        }

        private static string? GetAppPath(string exeName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}", false);
                return key?.GetValue(null)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        public static async Task<int> OptimizeBrowserPerformanceAsync()
        {
            return await Task.Run(() =>
            {
                int appliedCount = 0;

                try
                {
                    // 1. Google Chrome: Hardware Acceleration & GPU Steering & Background Suppression
                    using (var chromePolicy = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Google\Chrome"))
                    {
                        if (chromePolicy != null)
                        {
                            chromePolicy.SetValue("HardwareAccelerationModeEnabled", 1, RegistryValueKind.DWord);
                            chromePolicy.SetValue("BackgroundModeEnabled", 0, RegistryValueKind.DWord);
                            appliedCount += 2;
                        }
                    }

                    // 2. Microsoft Edge: Hardware Acceleration, Startup Boost Suppression & Sleeping Tabs
                    using (var edgePolicy = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Edge"))
                    {
                        if (edgePolicy != null)
                        {
                            edgePolicy.SetValue("HardwareAccelerationModeEnabled", 1, RegistryValueKind.DWord);
                            edgePolicy.SetValue("StartupBoostEnabled", 0, RegistryValueKind.DWord);
                            edgePolicy.SetValue("BackgroundModeEnabled", 0, RegistryValueKind.DWord);
                            edgePolicy.SetValue("SleepingTabsEnabled", 1, RegistryValueKind.DWord);
                            appliedCount += 4;
                        }
                    }

                    // 3. DirectX GPU Preferences (Direct Chrome & Edge to Dedicated High-Performance GPU)
                    using (var dxKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences"))
                    {
                        if (dxKey != null)
                        {
                            string chromeExe = GetAppPath("chrome.exe") ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe";
                            if (File.Exists(chromeExe))
                            {
                                dxKey.SetValue(chromeExe, "GpuPreference=2;", RegistryValueKind.String);
                                appliedCount++;
                            }

                            string edgeExe = GetAppPath("msedge.exe") ?? @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
                            if (File.Exists(edgeExe))
                            {
                                dxKey.SetValue(edgeExe, "GpuPreference=2;", RegistryValueKind.String);
                                appliedCount++;
                            }
                        }
                    }

                    // 4. Update Chrome Local State for GPU rasterization flags
                    appliedCount += UpdateChromiumLocalStateFast();
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex);
                }

                return appliedCount;
            });
        }

        public static Task<int> SuppressBrowserBackgroundModeAsync()
        {
            return OptimizeBrowserPerformanceAsync();
        }

        private static int UpdateChromiumLocalStateFast()
        {
            int count = 0;
            try
            {
                string localStatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data\Local State");
                if (File.Exists(localStatePath))
                {
                    string jsonStr = File.ReadAllText(localStatePath);
                    var node = JsonNode.Parse(jsonStr);
                    if (node is JsonObject root)
                    {
                        // Ensure hardware_acceleration_mode is enabled
                        if (root["hardware_acceleration_mode"] is JsonObject hwObj)
                        {
                            hwObj["enabled"] = true;
                        }
                        else
                        {
                            root["hardware_acceleration_mode"] = new JsonObject { ["enabled"] = true };
                        }
                        count++;

                        File.WriteAllText(localStatePath, root.ToJsonString());
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex);
            }
            return count;
        }

        /// <summary>
        /// اسکن هوشمند افزونه‌های مرورگر با رده‌بندی ریسک ۳ سطحی
        /// </summary>
        public static List<BrowserExtensionInfo> ScanExtensions()
        {
            var results = new List<BrowserExtensionInfo>();
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var browserBases = new (string Name, string Path)[]
            {
                ("Google Chrome", Path.Combine(localApp, @"Google\Chrome\User Data")),
                ("Microsoft Edge", Path.Combine(localApp, @"Microsoft\Edge\User Data")),
                ("Brave", Path.Combine(localApp, @"BraveSoftware\Brave-Browser\User Data"))
            };

            foreach (var (bName, basePath) in browserBases)
            {
                if (!Directory.Exists(basePath)) continue;

                var profileDirs = Directory.GetDirectories(basePath, "Default")
                    .Concat(Directory.GetDirectories(basePath, "Profile *"));

                foreach (var profPath in profileDirs)
                {
                    string profName = Path.GetFileName(profPath);
                    string extDir = Path.Combine(profPath, "Extensions");
                    string secPrefPath = Path.Combine(profPath, "Secure Preferences");
                    if (!File.Exists(secPrefPath))
                    {
                        secPrefPath = Path.Combine(profPath, "Preferences");
                    }

                    var prefSettings = new Dictionary<string, (string Name, int[] DisableReasons)>(StringComparer.OrdinalIgnoreCase);
                    if (File.Exists(secPrefPath))
                    {
                        try
                        {
                            string rawJson = File.ReadAllText(secPrefPath);
                            using var doc = JsonDocument.Parse(rawJson);
                            if (doc.RootElement.TryGetProperty("extensions", out var exts) &&
                                exts.TryGetProperty("settings", out var settings))
                            {
                                foreach (var prop in settings.EnumerateObject())
                                {
                                    string extId = prop.Name;
                                    string name = extId;
                                    var reasons = new List<int>();

                                    if (prop.Value.TryGetProperty("manifest", out var m) &&
                                        m.TryGetProperty("name", out var n))
                                    {
                                        name = n.GetString() ?? extId;
                                    }

                                    if (prop.Value.TryGetProperty("disable_reasons", out var dr) &&
                                        dr.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var item in dr.EnumerateArray())
                                        {
                                            if (item.TryGetInt32(out int code)) reasons.Add(code);
                                        }
                                    }

                                    prefSettings[extId] = (name, reasons.ToArray());
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            CcLog.Ignore(ex);
                        }
                    }

                    if (Directory.Exists(extDir))
                    {
                        foreach (var subDir in Directory.GetDirectories(extDir))
                        {
                            string extId = Path.GetFileName(subDir);
                            var info = ParseExtension(subDir, extId, bName, profName, prefSettings);
                            results.Add(info);
                        }
                    }

                    // بررسی پسماندهای Local Extension Settings برای افزونه‌های حذف‌شده
                    string localSettingsDir = Path.Combine(profPath, "Local Extension Settings");
                    if (Directory.Exists(localSettingsDir))
                    {
                        foreach (var orphanDir in Directory.GetDirectories(localSettingsDir))
                        {
                            string orphanId = Path.GetFileName(orphanDir);
                            if (!results.Any(r => r.Id.Equals(orphanId, StringComparison.OrdinalIgnoreCase)) &&
                                !ProtectedExtensionIds.Contains(orphanId))
                            {
                                long size = GetDirectorySize(orphanDir);
                                results.Add(new BrowserExtensionInfo
                                {
                                    Id = orphanId,
                                    Name = $"پسماند تنظیمات افزونه ({orphanId})",
                                    BrowserName = bName,
                                    ProfileName = profName,
                                    SizeBytes = size,
                                    IsEnabled = false,
                                    Category = ExtensionRiskCategory.SafeToClean,
                                    Reason = "دایرکتوری باقیمانده از افزونه حذف‌شده (Orphaned Remnant)",
                                    InstallPath = orphanDir
                                });
                            }
                        }
                    }
                }
            }

            return results;
        }

        private static BrowserExtensionInfo ParseExtension(
            string extFolder,
            string extId,
            string browserName,
            string profileName,
            Dictionary<string, (string Name, int[] DisableReasons)> prefSettings)
        {
            var info = new BrowserExtensionInfo
            {
                Id = extId,
                BrowserName = browserName,
                ProfileName = profileName,
                InstallPath = extFolder,
                SizeBytes = GetDirectorySize(extFolder)
            };

            // خواندن مانیفست
            string? manifestFile = Directory.GetFiles(extFolder, "manifest.json", SearchOption.AllDirectories).FirstOrDefault();
            if (manifestFile != null)
            {
                try
                {
                    string mText = File.ReadAllText(manifestFile);
                    using var mDoc = JsonDocument.Parse(mText);
                    var root = mDoc.RootElement;

                    if (root.TryGetProperty("name", out var n))
                    {
                        info.Name = n.GetString() ?? extId;
                        if (info.Name.StartsWith("__MSG_") && info.Name.EndsWith("__"))
                        {
                            string key = info.Name.Trim('_');
                            if (key.StartsWith("MSG_")) key = key[4..];
                            string dir = Path.GetDirectoryName(manifestFile)!;
                            string locPath = Path.Combine(dir, "_locales", "en", "messages.json");
                            if (File.Exists(locPath))
                            {
                                string locJson = File.ReadAllText(locPath);
                                using var lDoc = JsonDocument.Parse(locJson);
                                if (lDoc.RootElement.TryGetProperty(key, out var msgObj) &&
                                    msgObj.TryGetProperty("message", out var msgVal))
                                {
                                    info.Name = msgVal.GetString() ?? info.Name;
                                }
                            }
                        }
                    }

                    // بررسی دسترسی سراسری URL
                    if (mText.Contains("<all_urls>") || mText.Contains("*://*/*"))
                    {
                        info.HasGlobalUrlAccess = true;
                    }
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex);
                }
            }

            if (prefSettings.TryGetValue(extId, out var prefData))
            {
                if (string.IsNullOrEmpty(info.Name) || info.Name == extId)
                {
                    info.Name = prefData.Name;
                }

                if (prefData.DisableReasons.Length > 0)
                {
                    info.IsEnabled = false;
                    if (prefData.DisableReasons.Any(r => r > 1))
                    {
                        info.Category = ExtensionRiskCategory.SafeToClean;
                        info.Reason = "افزونه دارای خطای داخلی یا مسدود شده توسط استور (Corrupted/Policy)";
                        return info;
                    }
                }
            }

            // قانون رده‌بندی ۳ سطحی CleanC
            if (ProtectedExtensionIds.Contains(extId))
            {
                info.Category = ExtensionRiskCategory.ProtectedWhitelist;
                info.Reason = "افزونه کلیدی و حفاظت‌شده (دانلود منیجر، مترجم، AI RTL)";
            }
            else if (KnownDeadExtensionIds.Contains(extId))
            {
                info.Category = ExtensionRiskCategory.SafeToClean;
                info.Reason = "افزونه منسوخ‌شده، غیرفعال یا جایگزین‌شده (Safe to Clean)";
            }
            else if (info.SizeBytes > 50 * 1024 * 1024)
            {
                info.Category = ExtensionRiskCategory.ReviewWarning;
                info.Reason = "حجم بسیار بالا (> 50MB) با احتمال مصرف سنگین رم";
            }
            else if (info.HasGlobalUrlAccess)
            {
                info.Category = ExtensionRiskCategory.ReviewWarning;
                info.Reason = "تزریق اسکریپت به تمام صفحات (<all_urls>) - پایش دائمی DOM";
            }
            else if (!info.IsEnabled)
            {
                info.Category = ExtensionRiskCategory.ReviewWarning;
                info.Reason = "افزونه غیرفعال توسط کاربر";
            }
            else
            {
                info.Category = ExtensionRiskCategory.ReviewWarning;
                info.Reason = "افزونه فعال استاندارد";
            }

            return info;
        }

        private static long GetDirectorySize(string path)
        {
            try
            {
                var di = new DirectoryInfo(path);
                return di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }

        public static (int cleanedCount, long freedBytes) CleanSpecificExtensions(IEnumerable<string> extensionIds)
        {
            int count = 0;
            long freed = 0;
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string backupDir = Path.Combine(localApp, @"CleanC\ExtensionsBackup");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

            var targets = new HashSet<string>(extensionIds, StringComparer.OrdinalIgnoreCase);

            // 1. اعمال پالیسی رجیستری برای حذف بومی و رسمی توسط خود مرورگر و جلوگیری از بازگشت توسط Sync
            ApplyExtensionBlocklistPolicies(targets);

            var browserBases = new (string Name, string Path)[]
            {
                ("Google Chrome", Path.Combine(localApp, @"Google\Chrome\User Data")),
                ("Microsoft Edge", Path.Combine(localApp, @"Microsoft\Edge\User Data")),
                ("Brave", Path.Combine(localApp, @"BraveSoftware\Brave-Browser\User Data"))
            };

            foreach (var (_, basePath) in browserBases)
            {
                if (!Directory.Exists(basePath)) continue;

                var profileDirs = Directory.GetDirectories(basePath, "Default")
                    .Concat(Directory.GetDirectories(basePath, "Profile *"));

                foreach (var profPath in profileDirs)
                {
                    string profName = Path.GetFileName(profPath);

                    foreach (var id in targets)
                    {
                        // هرگز لیست محافظت شده پاک نشود
                        if (ProtectedExtensionIds.Contains(id)) continue;

                        var checkPaths = new[]
                        {
                            Path.Combine(profPath, "Extensions", id),
                            Path.Combine(profPath, "Local Extension Settings", id),
                            Path.Combine(profPath, "Sync Extension Settings", id)
                        };

                        foreach (var targetFolder in checkPaths)
                        {
                            if (Directory.Exists(targetFolder))
                            {
                                long size = GetDirectorySize(targetFolder);
                                freed += size;

                                try
                                {
                                    // تهیه بک‌آپ ایمن قبل از پاک‌سازی
                                    string destBackup = Path.Combine(backupDir, $"{profName}-{id}");
                                    if (!Directory.Exists(destBackup))
                                    {
                                        Directory.CreateDirectory(destBackup);
                                        CopyDirectory(targetFolder, destBackup);
                                    }

                                    Directory.Delete(targetFolder, true);
                                    count++;
                                }
                                catch (Exception ex)
                                {
                                    CcLog.Ignore(ex);
                                }
                            }
                        }
                    }
                }
            }

            return (count, freed);
        }

        public static void ApplyExtensionBlocklistPolicies(IEnumerable<string> extensionIds)
        {
            try
            {
                var idsToBlock = extensionIds
                    .Where(id => !ProtectedExtensionIds.Contains(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (idsToBlock.Count == 0) return;

                var policies = new[]
                {
                    @"Software\Policies\Google\Chrome\ExtensionInstallBlocklist",
                    @"Software\Policies\Microsoft\Edge\ExtensionInstallBlocklist"
                };

                foreach (var subKeyPath in policies)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(subKeyPath);
                    if (key != null)
                    {
                        var existingValues = key.GetValueNames()
                            .Select(n => key.GetValue(n)?.ToString())
                            .Where(v => !string.IsNullOrEmpty(v))
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        // بیشترین ایندکس عددی موجود + ۱ — ValueCount ساده در صورت وجود
                        // حفره در ایندکس ها یا مقادیر غیرعددی، روی مقدار موجود overwrite می کرد
                        int maxIndex = 0;
                        foreach (var name in key.GetValueNames())
                        {
                            if (int.TryParse(name, out int n) && n > maxIndex) maxIndex = n;
                        }

                        int index = maxIndex + 1;
                        foreach (var id in idsToBlock)
                        {
                            if (!existingValues.Contains(id))
                            {
                                key.SetValue(index.ToString(), id, RegistryValueKind.String);
                                index++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex);
            }
        }

        public static bool IsBrowserRunning(string processName = "chrome")
        {
            try
            {
                return System.Diagnostics.Process.GetProcessesByName(processName).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// پاک‌سازی پایگاه‌های داده IndexedDB یتیم متعلق به افزونه‌های پاک‌شده یا منسوخ
        /// با حفاظت ۱۰۰٪ سخت‌گیرانه از دیتابیس سایت‌ها و دامنه‌های وب (http_*, https_*)
        /// </summary>
        public static (int cleanedCount, long freedBytes) CleanOrphanedExtensionStorage()
        {
            int count = 0;
            long freed = 0;
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var browserBases = new (string Name, string Path)[]
            {
                ("Google Chrome", Path.Combine(localApp, @"Google\Chrome\User Data")),
                ("Microsoft Edge", Path.Combine(localApp, @"Microsoft\Edge\User Data")),
                ("Brave", Path.Combine(localApp, @"BraveSoftware\Brave-Browser\User Data"))
            };

            foreach (var (_, basePath) in browserBases)
            {
                if (!Directory.Exists(basePath)) continue;

                var profileDirs = Directory.GetDirectories(basePath, "Default")
                    .Concat(Directory.GetDirectories(basePath, "Profile *"));

                foreach (var profPath in profileDirs)
                {
                    string idbPath = Path.Combine(profPath, "IndexedDB");
                    if (!Directory.Exists(idbPath)) continue;

                    string extDir = Path.Combine(profPath, "Extensions");
                    var installedExts = Directory.Exists(extDir)
                        ? Directory.GetDirectories(extDir).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase)
                        : new HashSet<string?>(StringComparer.OrdinalIgnoreCase);

                    foreach (var dir in Directory.GetDirectories(idbPath))
                    {
                        string dirName = Path.GetFileName(dir);

                        // خط قرمز: هرگز به دیتابیس دامنه‌های وب دست نزن!
                        if (dirName.StartsWith("http_", StringComparison.OrdinalIgnoreCase) ||
                            dirName.StartsWith("https_", StringComparison.OrdinalIgnoreCase) ||
                            dirName.StartsWith("file_", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (dirName.StartsWith("chrome-extension_", StringComparison.OrdinalIgnoreCase))
                        {
                            // فرمت معمول نام پوشه: chrome-extension_<extId>_0.indexeddb.leveldb
                            string[] parts = dirName.Split('_');
                            if (parts.Length >= 2)
                            {
                                string extId = parts[1];

                                // اگر در لیست سفید باشد هرگز پاک نشود
                                if (ProtectedExtensionIds.Contains(extId)) continue;

                                // اگر در لیست افزونه‌های مرده باشد یا اصلاً نصب نباشد
                                if (KnownDeadExtensionIds.Contains(extId) || !installedExts.Contains(extId))
                                {
                                    long size = GetDirectorySize(dir);
                                    try
                                    {
                                        Directory.Delete(dir, true);
                                        freed += size;
                                        count++;
                                    }
                                    catch (Exception ex)
                                    {
                                        CcLog.Ignore(ex);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return (count, freed);
        }

        public static long CleanInactiveChromeExtensions()
        {
            var (_, freedBytes) = CleanSpecificExtensions(KnownDeadExtensionIds);
            var (_, idbFreed) = CleanOrphanedExtensionStorage();
            return freedBytes + idbFreed;
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(sourceDir, file.FullName);
                string destFile = Path.Combine(destinationDir, rel);
                string? destSub = Path.GetDirectoryName(destFile);
                if (destSub != null && !Directory.Exists(destSub)) Directory.CreateDirectory(destSub);
                file.CopyTo(destFile, true);
            }
        }
    }
}
