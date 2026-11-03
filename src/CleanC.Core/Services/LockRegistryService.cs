// src/CleanC.Core/Services/LockRegistryService.cs
// رجیستری قفلِ مسیرها — منبع واحدِ «دست نزن» برای همه ی دیتکتورهای اسکن/پاکسازی.
//
// دو نوع قفل:
//   ۱) قفل های پیش فرض (کامپایل شده): لیست های حاردکد قبلیِ LeftoverCleanerService
//      (حفاظت نام ها) + ریشه های سیستمی (Windows / Program Files). این ها همیشه فعالند
//      و از فایل json قابل حذف نیستند (حذف فایل هم آن ها را از بین نمی برد).
//   ۲) قفل های کاربر (فایل %LOCALAPPDATA%\WinyWash\locks.json): از UI اضافه/حذف می شوند.
//
// دو سطح بررسی:
//   IsLockedPath      → پیش فرض + کاربر؛ برای «پیشنهاد ندادن» در اسکن ها
//   IsUserLockedPath  → فقط کاربر؛ وتوی سخت روی حذف (حتی زباله ی سیستمی که اپ خودش پاک می کند)
//
// ساختار هر قفل: { path, lockType, reason, createdAt, isDefault }
//   lockType: "path" (تطبیق دقیق) | "folder" (خود پوشه و همه ی زیرمسیرها) | "name" (نام یک پوشه/فایل)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CleanC.Core.Diagnostics;

namespace CleanC.Core.Services
{
    /// <summary>انواع قفل پشتیبانی شده در locks.json.</summary>
    public static class LockTypes
    {
        public const string Path = "path";
        public const string Folder = "folder";
        public const string Name = "name";
    }

    /// <summary>یک قاعده ی قفل — در locks.json ذخیره می شود.</summary>
    public sealed class LockEntry
    {
        public string Path { get; set; } = string.Empty;
        public string LockType { get; set; } = LockTypes.Folder;
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsDefault { get; set; }
    }

    public static class LockRegistryService
    {
        private static readonly object Sync = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string _filePath = BuildDefaultPath();
        private static List<LockEntry>? _userCache;

        /// <summary>مسیر فایل قفل — برای تست قابل تغییر است (الگوی SettingsService).</summary>
        public static string LocksFilePath
        {
            get => _filePath;
            set
            {
                lock (Sync)
                {
                    _filePath = string.IsNullOrWhiteSpace(value) ? BuildDefaultPath() : value;
                    _userCache = null; // مسیر عوض شد → کش بی اعتبار است
                }
            }
        }

        private static string BuildDefaultPath() => AppPaths.LocksFile;

        // ── قفل های پیش فرض (کامپایل شده) ─────────────────────────────────

        /// <summary>
        /// اتحاد سه لیست حفاظتیِ قبلیِ LeftoverCleanerService (نام های سیستمی/سازنده،
        /// پوشه های پروفایل کاربر و پوشه های Public) به علاوه چند نام سیستمیِ ریشه ی درایو.
        /// </summary>
        private static readonly HashSet<string> DefaultProtectedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            // سازنده ها و پوشه های سیستمی / برنامه ها
            "Microsoft", "Windows", "WindowsApps", "Packages", "Common Files", "Internet Explorer",
            "NVIDIA Corporation", "NVIDIA", "AMD", "Intel", "Realtek", "Synaptics", "Google",
            "Mozilla", "CleanC", "WinyWash", "CleanC.UI", "WinyWash.exe.WebView2", "CleanC.exe.WebView2", "EBWebView",
            "Git", "dotnet", "Adobe", "Steam", "Epic Games", "Discord",
            "Telegram Desktop", "Spotify", "Temp", "CrashDumps", "USOShared", "NhNotifSys",
            "Programs", "System",
            "Downloaded Installations", "Package Cache", "OEM", "ASUS", "Lenovo", "Dell", "HP", "MSI",
            "PowerShell", "dotnet-tools", "yarn", "npm", "pip", "vcpkg", "VirtualStore",
            "Apple", "Apple Computer", "Sony", "Logitech", "Corsair", "Razer", "SteelSeries",
            "Kingston", "Samsung", "Crucial", "Western Digital", "Seagate", "Oracle", "Java",
            "Android", "JetBrains", "Unity", "EpicGamesLauncher",

            // اکوسیستم و استودیوهای بازی‌سازی و لانچرها (حفاظت خط قرمز از سیو و دیتای بازی)
            "Ubisoft", "Ubisoft Game Launcher", "Electronic Arts", "EA Games", "EA Desktop", "Origin",
            "Battle.net", "Blizzard", "Blizzard Entertainment", "Riot Games", "GOG.com", "GOG Galaxy",
            "FromSoftware", "Larian Studios", "CD Projekt Red", "Bethesda", "Bethesda.net",
            "Rockstar Games", "Capcom", "Bandai Namco", "Bandai Namco Entertainment", "SEGA",
            "Square Enix", "Konami", "2K Games", "Paradox Interactive", "Team Cherry",
            "IronGate", "Klei", "Ludeon Studios", "miHoYo", "GenshinImpact", "Roblox", "Mojang",
            "Minecraft", "Valve", "DarkSouls", "EldenRing", "BaldursGate3", "Cyberpunk 2077",
            "The Witcher 3", "Euro Truck Simulator 2", "American Truck Simulator",

            // نام های ریشه ی درایو (برای دیتکتور D10 و اسکن های سطح درایو)
            "Users", "ProgramData", "Recovery", "$Recycle.Bin", "System Volume Information",

            // پوشه های شخصیِ پروفایل کاربر و محل‌های ذخیره سیو بازی‌ها
            "Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos",
            "Favorites", "Links", "Contacts", "Searches", "Saved Games", "My Games", "GameSaves", "SaveGames", "Saves", "3D Objects",
            "OneDrive", "AppData", "Cookies", "NetHood", "PrintHood", "Recent", "SendTo",
            "Start Menu", "Templates", "Application Data", "Local Settings", "My Documents",
            "Projects", "Source", "Repos", "Workspace", "Development", "Codes", "Code",
            "Work", "my files", "Dropbox", "Google Drive", "iCloudDrive", "Virtual Machines",
            "VMs", "Box", "Creative Cloud Files",

            // پوشه های مخفیِ ابزارهای فعال توسعه (dot-folders)
            ".ssh", ".git", ".gemini", ".antigravity", ".antigravity-code", ".vscode",
            ".vscode-insiders", ".cursor", ".copilot", ".claude", ".openai", ".npm",
            ".node", ".yarn", ".nuget", ".dotnet", ".cargo", ".rustup", ".conda",
            ".anaconda", ".jupyter", ".matplotlib", ".aws", ".azure", ".kube", ".docker",
            ".config", ".local", ".gnupg", ".pki", ".platformio", ".android", ".gradle",
            ".m2", ".atom", ".eclipse",

            // پوشه های Public
            "Libraries", "AccountPictures", "Recorded Calls"
        };

        private static IEnumerable<LockEntry> DefaultEntries()
        {
            foreach (string dir in GetDefaultSystemFolderLocks())
            {
                yield return new LockEntry
                {
                    Path = dir,
                    LockType = LockTypes.Folder,
                    Reason = LocalizationService.Get("ریشه ی سیستمی ویندوز — هرگز حذف نمی شود"),
                    IsDefault = true,
                    CreatedAt = DateTime.MinValue
                };
            }

            foreach (string name in DefaultProtectedNames)
            {
                yield return new LockEntry
                {
                    Path = name,
                    LockType = LockTypes.Name,
                    Reason = LocalizationService.Get("لیست حفاظت پیش فرض CleanC"),
                    IsDefault = true,
                    CreatedAt = DateTime.MinValue
                };
            }
        }

        private static List<string> GetDefaultSystemFolderLocks()
        {
            var list = new List<string>();

            void Add(Environment.SpecialFolder folder)
            {
                try
                {
                    string p = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrWhiteSpace(p)) list.Add(p);
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex, "LockRegistry.DefaultFolders");
                }
            }

            Add(Environment.SpecialFolder.Windows);
            Add(Environment.SpecialFolder.ProgramFiles);
            Add(Environment.SpecialFolder.ProgramFilesX86);

            // خط قرمز: پوشه های سیو بازی ها و دایرکتوری های حیاتی کاربر
            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                if (!string.IsNullOrWhiteSpace(userProfile))
                {
                    list.Add(Path.Combine(userProfile, "Saved Games"));
                    list.Add(Path.Combine(userProfile, @"AppData\LocalLow"));
                }
                if (!string.IsNullOrWhiteSpace(myDocs))
                {
                    list.Add(Path.Combine(myDocs, "My Games"));
                }
                if (!string.IsNullOrWhiteSpace(localApp))
                {
                    list.Add(Path.Combine(localApp, "WinyWash.exe.WebView2"));
                    list.Add(Path.Combine(localApp, "CleanC.exe.WebView2"));
                    list.Add(Path.Combine(localApp, "CleanC.UI.exe.WebView2"));
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex, "LockRegistry.DefaultFolders.GameSaves");
            }

            // فیکس «حذف دیتای خود برنامه»: ریشه های داده ی WinyWash (تنظیمات، قفل ها،
            // قرنطینه، لاگ ها و بکاپ ها) و پوشه جاری اجرای برنامه باید همیشه قفل باشند.
            try
            {
                if (!string.IsNullOrWhiteSpace(AppPaths.LocalRoot)) list.Add(AppPaths.LocalRoot);
                if (!string.IsNullOrWhiteSpace(AppPaths.CommonRoot)) list.Add(AppPaths.CommonRoot);
                if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory)) list.Add(AppContext.BaseDirectory);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex, "LockRegistry.DefaultFolders.AppPaths");
            }
            return list;
        }

        // ── خواندن ────────────────────────────────────────────────────────

        /// <summary>همه ی قفل ها (پیش فرض + کاربر) — برای نمایش در UI.</summary>
        public static IReadOnlyList<LockEntry> GetAll()
        {
            var result = new List<LockEntry>();
            result.AddRange(DefaultEntries());
            result.AddRange(GetUserEntries());
            return result;
        }

        /// <summary>فقط قفل های کاربر (نسخه ی کپی — تغییر لیست برگشتی روی رجیستری اثر ندارد).</summary>
        public static IReadOnlyList<LockEntry> GetUserLocks()
            => GetUserEntries().Select(Clone).ToList();

        /// <summary>
        /// آیا مسیر قفل است؟ (پیش فرض + کاربر) — برای فیلترکردن پیشنهادهای اسکن.
        /// </summary>
        public static bool IsLockedPath(string fullPath)
        {
            string norm = NormalizePath(fullPath);
            if (norm.Length == 0) return false;

            foreach (var entry in DefaultEntries())
            {
                if (MatchesPath(entry, norm)) return true;
            }
            foreach (var entry in GetUserEntries())
            {
                if (MatchesPath(entry, norm)) return true;
            }
            return false;
        }

        /// <summary>
        /// وتوی سختِ کاربر: مسیری که خودِ کاربر قفل کرده هرگز حذف نمی شود
        /// (حتی توسط پاکسازی زباله ی سیستمی). قفل های پیش فرض اینجا اعمال نمی شوند.
        /// </summary>
        public static bool IsUserLockedPath(string fullPath)
        {
            string norm = NormalizePath(fullPath);
            if (norm.Length == 0) return false;

            foreach (var entry in GetUserEntries())
            {
                if (MatchesPath(entry, norm)) return true;
            }
            return false;
        }

        /// <summary>آیا این نامِ پوشه/فایل در لیست حفاظت (پیش فرض + کاربر) است؟</summary>
        public static bool IsProtectedName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            string trimmed = name.Trim();

            // خط قرمز: دیتای خود برنامه، WebView2 و پوشه های مشخص سیو
            if (trimmed.EndsWith(".WebView2", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "WinyWash", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "CleanC", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "CleanC.UI", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("WinyWash.", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("CleanC.", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "EBWebView", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Saved Games", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "My Games", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "LocalLow", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (DefaultProtectedNames.Contains(trimmed)) return true;

            foreach (var entry in GetUserEntries())
            {
                if (!string.Equals(entry.LockType, LockTypes.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(entry.Path.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // ── نوشتن (فقط قفل های کاربر) ─────────────────────────────────────

        /// <summary>افزودن/به روزرسانی یک قفل کاربر. خروجی: موفقیتِ ذخیره روی دیسک.</summary>
        public static bool AddLock(string path, string lockType, string reason)
        {
            string normalizedType = NormalizeLockType(lockType);
            if (normalizedType.Length == 0 || string.IsNullOrWhiteSpace(path)) return false;

            string value = normalizedType == LockTypes.Name ? path.Trim() : NormalizePath(path);
            if (value.Length == 0) return false;

            lock (Sync)
            {
                var entries = GetUserEntries();
                var existing = entries.FirstOrDefault(e =>
                    string.Equals(e.LockType, normalizedType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Path, value, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    existing.Reason = reason ?? string.Empty;
                    return SaveUserEntriesLocked();
                }

                entries.Add(new LockEntry
                {
                    Path = value,
                    LockType = normalizedType,
                    Reason = reason ?? string.Empty,
                    CreatedAt = DateTime.Now,
                    IsDefault = false
                });
                return SaveUserEntriesLocked();
            }
        }

        /// <summary>حذف یک قفل کاربر (قفل های پیش فرض قابل حذف نیستند).</summary>
        public static bool RemoveLock(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            string raw = path.Trim();
            string norm = NormalizePath(path);

            lock (Sync)
            {
                var entries = GetUserEntries();
                int removed = entries.RemoveAll(e =>
                {
                    if (string.Equals(e.LockType, LockTypes.Name, StringComparison.OrdinalIgnoreCase))
                        return string.Equals(e.Path.Trim(), raw, StringComparison.OrdinalIgnoreCase);
                    return string.Equals(NormalizePath(e.Path), norm, StringComparison.OrdinalIgnoreCase);
                });

                if (removed == 0) return false;
                return SaveUserEntriesLocked();
            }
        }

        /// <summary>پاک کردن کش برای تست ها (در اجرای عادی لازم نیست).</summary>
        public static void ResetCacheForTests()
        {
            lock (Sync)
            {
                _userCache = null;
            }
        }

        // ── کمکی های داخلی ────────────────────────────────────────────────

        private static LockEntry Clone(LockEntry entry) => new()
        {
            Path = entry.Path,
            LockType = entry.LockType,
            Reason = entry.Reason,
            CreatedAt = entry.CreatedAt,
            IsDefault = entry.IsDefault
        };

        private static List<LockEntry> GetUserEntries()
        {
            lock (Sync)
            {
                _userCache ??= LoadUserEntriesLocked();
                return _userCache;
            }
        }

        private static List<LockEntry> LoadUserEntriesLocked()
        {
            var list = new List<LockEntry>();
            try
            {
                string path = _filePath;
                if (!File.Exists(path)) return list;

                var loaded = JsonSerializer.Deserialize<List<LockEntry>>(File.ReadAllText(path), JsonOptions);
                if (loaded == null) return list;

                foreach (var entry in loaded)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Path)) continue;

                    string type = NormalizeLockType(entry.LockType);
                    if (type.Length == 0) continue; // نوع نامعتبر → نادیده گرفته می شود

                    entry.LockType = type;
                    entry.IsDefault = false; // فایل فقط قفل های کاربر را نگه می دارد
                    list.Add(entry);
                }
            }
            catch (Exception ex)
            {
                // فایل خراب نباید اپ را بشکند؛ با لیست خالی ادامه می دهیم
                CcLog.Ignore(ex, "LockRegistry.Load");
            }
            return list;
        }

        private static bool SaveUserEntriesLocked()
        {
            try
            {
                string path = _filePath;
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_userCache, JsonOptions), new UTF8Encoding(false));
                File.Move(tmp, path, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "LockRegistry.Save");
                return false;
            }
        }

        private static string NormalizeLockType(string? lockType) => lockType?.Trim().ToLowerInvariant() switch
        {
            LockTypes.Path => LockTypes.Path,
            LockTypes.Folder => LockTypes.Folder,
            LockTypes.Name => LockTypes.Name,
            _ => string.Empty
        };

        /// <summary>نرمال سازی مسیر: مسیر کامل + حذف جداکننده ی انتهایی (به جز ریشه مثل C:\).</summary>
        public static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string p = path.Trim();

            try { p = Path.GetFullPath(p); }
            catch (Exception __ex) { CcLog.Ignore(__ex, "LockRegistry.NormalizePath"); }

            if (p.Length > 3) p = p.TrimEnd('\\', '/');
            return p;
        }

        private static bool MatchesPath(LockEntry entry, string normalizedPath)
        {
            if (string.Equals(entry.LockType, LockTypes.Name, StringComparison.OrdinalIgnoreCase)) return false;

            string lockPath = NormalizePath(entry.Path);
            if (lockPath.Length == 0) return false;

            if (string.Equals(entry.LockType, LockTypes.Path, StringComparison.OrdinalIgnoreCase))
                return string.Equals(normalizedPath, lockPath, StringComparison.OrdinalIgnoreCase);

            // folder: خود پوشه یا هر زیرمسیر آن
            if (string.Equals(normalizedPath, lockPath, StringComparison.OrdinalIgnoreCase)) return true;
            return normalizedPath.StartsWith(lockPath + '\\', StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(lockPath + '/', StringComparison.OrdinalIgnoreCase);
        }
    }
}
