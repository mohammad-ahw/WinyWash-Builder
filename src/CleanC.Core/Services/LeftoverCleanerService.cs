using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CleanC.Core.Diagnostics;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class LeftoverFolderItem
    {
        public string FolderName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string LocationType { get; set; } = "AppData"; // "AppData (Roaming)", "AppData (Local)", "AppData (LocalLow)", "ProgramData", "User Profile", "Public"
    }

    public class LeftoverCleanerService
    {
        // توجه: سه لیست حفاظتی قبلی (ProtectedNames / ProtectedUserProfileFolders /
        // ProtectedPublicFolders) به LockRegistryService منتقل شدند تا منبع واحدِ «قفل»
        // برای همه ی دیتکتورها وجود داشته باشد. رفتار معادل، اتحاد همان نام هاست.

        // Extensions that identify personal user documents, media, or source code projects - if found, folder is NEVER a leftover!
        // نکته ی حیاتی: پسوندهای سِیو بازی هم اینجا هستند — پوشه ی بازمانده ای که سِیو
        // بازی (مثلاً Stardew Valley، Dark Souls، Resident Evil) داخلش باشد هرگز
        // «زباله» حساب نمی شود و حذف نمی شود. (باگ واقعی: پاک شدن سِیوهای کاربر)
        private static readonly HashSet<string> PersonalDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf",
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".psd", ".ai",
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".mp3", ".wav", ".flac",
            ".zip", ".rar", ".7z", ".tar", ".gz",
            ".sln", ".csproj", ".vbproj", ".fsproj", ".vcxproj",
            ".cs", ".cpp", ".c", ".h", ".py", ".rs", ".go", ".java", ".ts", ".blend",
            // سِیو/ذخیره ی بازی ها و فایل های پشتیبان — اکثر موتورهای بازی از اینها استفاده می کنند
            ".sav", ".save", ".bak", ".vdf", ".wld", ".psv", ".stg", ".sl2", ".ssf",
            ".d2s", ".mlx", ".gam", ".plrk", ".gci", ".srm", ".dsv", ".mcr", ".vmc",
            ".dat", ".bin", ".sqlite", ".db", ".bytes", ".profile", ".lsv", ".co2", ".sgd", ".gamedata",
            ".state", ".opt", ".cfg", ".pak", ".asset", ".unity3d", ".bundle", ".rpf"
        };

        public static List<LeftoverFolderItem> ScanLeftoverFolders()
        {
            var leftovers = new List<LeftoverFolderItem>();

            try
            {
                // 1. Collect all installed application names and signatures
                var installedNames = GetInstalledSoftwareSignatures();

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                // AppData\LocalLow is strictly protected and never scanned
                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string publicDir = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";

                // 2. Scan AppData (Roaming)
                ScanCandidateDirectory(appData, "AppData (Roaming)", installedNames, leftovers);

                // 3. Scan LocalAppData
                ScanCandidateDirectory(localApp, "AppData (Local)", installedNames, leftovers);



                // 5. Scan ProgramData
                ScanCandidateDirectory(programData, "ProgramData", installedNames, leftovers);

                // 6. Scan User Profile root (C:\Users\<Username>) with strict personal file protection
                ScanUserProfileDirectory(userProfile, installedNames, leftovers);

                // 7. Scan Public folder (C:\Users\Public)
                if (Directory.Exists(publicDir))
                {
                    ScanPublicDirectory(publicDir, installedNames, leftovers);
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

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

                    if (LockRegistryService.IsLockedPath(dir)) continue;
                    if (LockRegistryService.IsProtectedName(dirName)) continue;
                    if (dirName.StartsWith(".") || dirName.StartsWith("$")) continue;
                    if (HasPersonalOrProjectFiles(dir)) continue;

                    // Skip junctions / symlinks
                    try
                    {
                        var di = new DirectoryInfo(dir);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { continue; }

                    // Check if folder name matches any currently installed software or Program Files folder
                    bool isMatched = installedSignatures.Any(sig =>
                        sig.Equals(dirName, StringComparison.OrdinalIgnoreCase) ||
                        (dirName.Length >= 4 && sig.Contains(dirName, StringComparison.OrdinalIgnoreCase)) ||
                        (sig.Length >= 4 && dirName.Contains(sig, StringComparison.OrdinalIgnoreCase)));

                    if (!isMatched)
                    {
                        // Check if corresponding executable exists in Program Files or LocalAppData\Programs
                        if (!ExistsInProgramFiles(dirName) && !ExistsInLocalAppDataPrograms(dirName))
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
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private static void ScanUserProfileDirectory(string userProfile, HashSet<string> installedSignatures, List<LeftoverFolderItem> result)
        {
            if (string.IsNullOrWhiteSpace(userProfile) || !Directory.Exists(userProfile)) return;

            try
            {
                foreach (var dir in Directory.GetDirectories(userProfile))
                {
                    string dirName = Path.GetFileName(dir);

                    // 1. Never touch protected user folders (Documents, Desktop, Downloads, Projects, .ssh, .git, etc.)
                    if (LockRegistryService.IsProtectedName(dirName))
                        continue;

                    // 2. Ignore system or hidden NTUSER/desktop files/junctions
                    try
                    {
                        var di = new DirectoryInfo(dir);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { continue; }

                    // 3. Ignore IDE dot-directories (e.g. .IntelliJIdea2023.1, .PyCharm2024.1, etc.)
                    if (dirName.StartsWith(".IntelliJ", StringComparison.OrdinalIgnoreCase) ||
                        dirName.StartsWith(".PyCharm", StringComparison.OrdinalIgnoreCase) ||
                        dirName.StartsWith(".WebStorm", StringComparison.OrdinalIgnoreCase) ||
                        dirName.StartsWith(".Rider", StringComparison.OrdinalIgnoreCase) ||
                        dirName.StartsWith(".CLion", StringComparison.OrdinalIgnoreCase) ||
                        dirName.StartsWith(".RustRover", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 4. Ironclad check: Directory MUST NOT contain personal user documents or git projects
                    if (HasPersonalOrProjectFiles(dir))
                        continue;

                    // 5. Check if folder name matches currently installed software
                    bool isMatched = installedSignatures.Any(sig =>
                        sig.Equals(dirName, StringComparison.OrdinalIgnoreCase) ||
                        (dirName.StartsWith(".") && sig.Equals(dirName.TrimStart('.'), StringComparison.OrdinalIgnoreCase)) ||
                        (dirName.Length >= 4 && sig.Contains(dirName, StringComparison.OrdinalIgnoreCase)) ||
                        (sig.Length >= 4 && dirName.Contains(sig, StringComparison.OrdinalIgnoreCase)));

                    if (!isMatched)
                    {
                        string strippedName = dirName.TrimStart('.');
                        if (!ExistsInProgramFiles(strippedName) && !ExistsInLocalAppDataPrograms(strippedName))
                        {
                            long size = GetDirectorySizeSafe(dir);
                            if (size > 1024 * 1024) // Only consider significant leftovers (> 1 MB)
                            {
                                result.Add(new LeftoverFolderItem
                                {
                                    FolderName = dirName,
                                    FullPath = dir,
                                    SizeBytes = size,
                                    LocationType = LocalizationService.Get("C:\\Users (پروفایل کاربر)")
                                });
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

        private static void ScanPublicDirectory(string publicDir, HashSet<string> installedSignatures, List<LeftoverFolderItem> result)
        {
            if (string.IsNullOrWhiteSpace(publicDir) || !Directory.Exists(publicDir)) return;

            try
            {
                foreach (var dir in Directory.GetDirectories(publicDir))
                {
                    string dirName = Path.GetFileName(dir);

                    if (LockRegistryService.IsProtectedName(dirName))
                        continue;

                    try
                    {
                        var di = new DirectoryInfo(dir);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { continue; }

                    if (HasPersonalOrProjectFiles(dir)) continue;

                    bool isMatched = installedSignatures.Any(sig =>
                        sig.Equals(dirName, StringComparison.OrdinalIgnoreCase) ||
                        (dirName.Length >= 4 && sig.Contains(dirName, StringComparison.OrdinalIgnoreCase)) ||
                        (sig.Length >= 4 && dirName.Contains(sig, StringComparison.OrdinalIgnoreCase)));

                    if (!isMatched && !ExistsInProgramFiles(dirName) && !ExistsInLocalAppDataPrograms(dirName))
                    {
                        long size = GetDirectorySizeSafe(dir);
                        if (size > 1024 * 1024)
                        {
                            result.Add(new LeftoverFolderItem
                            {
                                FolderName = dirName,
                                FullPath = dir,
                                SizeBytes = size,
                                LocationType = "C:\\Users\\Public"
                            });
                        }
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private static bool HasPersonalOrProjectFiles(string dirPath)
        {
            try
            {
                if (Directory.Exists(Path.Combine(dirPath, ".git"))) return true;

                var dir = new DirectoryInfo(dirPath);
                int scannedCount = 0;

                foreach (var fi in dir.EnumerateFiles("*", new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 3
                }))
                {
                    scannedCount++;
                    if (PersonalDocumentExtensions.Contains(fi.Extension))
                    {
                        return true; // Contains personal document/media/project - protect it!
                    }

                    if (scannedCount > 500) break;
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return false;
        }

        // گارد «پوشه ی خالی»: پوشه ی باقی مانده ی یک برنامه ی حذف شده (مثل
        // C:\Program Files (x86)\Battle.net که فقط زیرپوشه ی خالی دارد) نباید
        // باعث شود بازمانده های واقعیِ ProgramData «متعلق به برنامه ی نصب شده» حساب شوند.
        internal static bool HasAnyFile(string dir)
        {
            try
            {
                return Directory.EnumerateFiles(dir, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true
                }).Any();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
                return false;
            }
        }

        private static bool ExistsInProgramFiles(string name)
        {
            string p1 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string p2 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            string c1 = Path.Combine(p1, name);
            string c2 = Path.Combine(p2, name);
            if (Directory.Exists(c1) && HasAnyFile(c1)) return true;
            if (Directory.Exists(c2) && HasAnyFile(c2)) return true;

            return false;
        }

        private static bool ExistsInLocalAppDataPrograms(string name)
        {
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programsDir = Path.Combine(localApp, "Programs");
                if (Directory.Exists(programsDir))
                {
                    string candidate = Path.Combine(programsDir, name);
                    if (Directory.Exists(candidate) && HasAnyFile(candidate)) return true;
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return false;
        }

        // کلماتی که در نام برنامه های نصب شده زیاد تکرار می شوند و به تنهایی
        // مدرک «نصب بودن» نیستند. اگر این کلمات امضا شوند، پوشه هایی مثل
        // «Battle.net_components» با substring-match اشتباهاً «متعلق به برنامه ی
        // نصب شده» حساب می شوند و از لیست بازمانده ها حذف می شوند.
        // نکته: «update/updates» عمداً اینجا نیست — همان سرکوب جلوی دوباره
        // دیده شدن پوشه های *-updater (که دیتکتور اختصاصی D04 مدیریتشان می کند) را می گیرد.
        private static readonly HashSet<string> GenericAppWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "components", "component", "runtime", "service", "services", "driver", "drivers",
            "installer", "setup", "framework", "package", "packages", "toolchain", "templates",
            "tools", "manifest", "sdk", "host", "extension", "extensions", "additional",
            "toolset", "emscripten", "classes", "policies", "registeredapplications", "wow6432node"
        };

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
                                foreach (var word in name.Split(new[] { ' ', '-', '_', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (word.Length >= 3 && !GenericAppWords.Contains(word)) signatures.Add(word);
                                }
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
                    using var hkcuKey = Registry.CurrentUser.OpenSubKey(keyPath);
                    if (hkcuKey != null)
                    {
                        foreach (var sub in hkcuKey.GetSubKeyNames())
                        {
                            using var app = hkcuKey.OpenSubKey(sub);
                            string? name = app?.GetValue("DisplayName")?.ToString();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                foreach (var word in name.Split(new[] { ' ', '-', '_', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (word.Length >= 3 && !GenericAppWords.Contains(word)) signatures.Add(word);
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

            // توجه (فیکس باگ ManyCam): عمداً کلیدهای مستقیم زیر HKCU\Software و HKLM\Software
            // را «امضای نصب» حساب نمی کنیم. این کلیدها بعد از حذف برنامه هم می مانند —
            // مثال واقعی: ManyCam حذف شده بود ولی HKCU\SOFTWARE\ManyCam مانده بود و باعث
            // می شد پوشه های بازمانده اش (ProgramData و AppData) اشتباهاً «برنامه ی نصب شده»
            // تلقی شوند و هرگز پیشنهاد پاک شدن نگیرند. تشخیص نصب فقط بر شواهد قابل اعتماد
            // تکیه می کند: ورودی های Uninstall، پوشه های Program Files دارای فایل واقعی، و
            // LocalAppData\Programs (پایین تر همین تابع).

            // Program Files and Program Files (x86) — فقط پوشه های دارای فایل واقعی؛
            // پوشه ی خالیِ باقی مانده ی یک برنامه ی حذف شده نباید «برنامه ی نصب شده» حساب شود
            // (وگرنه بازمانده های ProgramData آن برنامه هم اشتباهاً حذف از لیست می شوند).
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (Directory.Exists(pf))
                {
                    foreach (var d in Directory.GetDirectories(pf))
                    {
                        if (HasAnyFile(d)) signatures.Add(Path.GetFileName(d));
                    }
                }
                string pfx = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (Directory.Exists(pfx))
                {
                    foreach (var d in Directory.GetDirectories(pfx))
                    {
                        if (HasAnyFile(d)) signatures.Add(Path.GetFileName(d));
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // LocalAppData\Programs
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programs = Path.Combine(localApp, "Programs");
                if (Directory.Exists(programs))
                {
                    foreach (var d in Directory.GetDirectories(programs))
                    {
                        if (HasAnyFile(d)) signatures.Add(Path.GetFileName(d));
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

                        // Game libraries on all drives (Steam, Epic, GOG, Riot, Ubisoft, etc.)
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    string root = drive.RootDirectory.FullName;
                    string[] gameDirs = {
                        Path.Combine(root, "Games"),
                        Path.Combine(root, "Game"),
                        Path.Combine(root, "SteamLibrary", "steamapps", "common"),
                        Path.Combine(root, "Program Files", "Epic Games"),
                        Path.Combine(root, "GOG Games"),
                        Path.Combine(root, "Riot Games"),
                        Path.Combine(root, "Ubisoft", "Ubisoft Game Launcher", "games"),
                        Path.Combine(root, "XboxGames")
                    };

                    foreach (var gDir in gameDirs)
                    {
                        if (Directory.Exists(gDir))
                        {
                            foreach (var sub in Directory.GetDirectories(gDir))
                            {
                                if (HasAnyFile(sub))
                                {
                                    string gName = Path.GetFileName(sub);
                                    signatures.Add(gName);
                                    foreach (var word in gName.Split(new[] { ' ', '-', '_', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
                                    {
                                        if (word.Length >= 3 && !GenericAppWords.Contains(word)) signatures.Add(word);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

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
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return size;
        }

        // ====================================================================
        // حذف ایمن (guard در برابر حذف تصادفی مسیرهای حساس)
        // ====================================================================

        /// <summary>
        /// ریشه های مجازی که اسکنر فقط یک سطح داخل آنها را بررسی می کند.
        /// هر مسیر حذف باید دقیقاً یک سطح زیر یکی از اینها باشد.
        /// </summary>
        private static string[] GetAllowedRoots()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                // LocalLow excluded from allowed roots
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                userProfile,
                Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public",
            };
            return roots.Where(r => !string.IsNullOrWhiteSpace(r)).ToArray();
        }

        /// <summary>
        /// بازبینی مجدد یک مسیر پیش از حذف. اسکن و حذف ممکن است با فاصله انجام شوند،
        /// پس همه شرط های ایمنی باید دوباره اعمال شوند.
        /// </summary>
        public static bool IsSafeToDelete(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return false;

            try
            {
                if (!Path.IsPathRooted(fullPath)) return false;
                if (!Directory.Exists(fullPath)) return false;

                string full = Path.GetFullPath(fullPath).TrimEnd('\\', '/');
                string dirName = Path.GetFileName(full);

                if (string.IsNullOrEmpty(dirName)) return false;
                                if (LockRegistryService.IsLockedPath(full)) return false;
                if (LockRegistryService.IsProtectedName(dirName)) return false;
                if (dirName.StartsWith(".") || dirName.StartsWith("$")) return false;

                // فقط یک سطح زیر ریشه مجاز (دقیقاً همان چیزی که اسکنر دیده)
                var root = GetAllowedRoots().FirstOrDefault(r =>
                    full.StartsWith(Path.GetFullPath(r).TrimEnd('\\', '/') + "\\", StringComparison.OrdinalIgnoreCase));
                if (root == null) return false;
                string relative = full.Substring(Path.GetFullPath(root).TrimEnd('\\', '/').Length + 1);
                if (relative.Contains('\\')) return false; // عمق بیش از ۱ سطح → خارج از محدوده اسکن

                // جلوگیری از حذف از طریق junction/symlink
                var di = new DirectoryInfo(full);
                if ((di.Attributes & FileAttributes.ReparsePoint) != 0) return false;

                // پوشه شخصی / پروژه گیت هرگز حذف نمی شود
                if (HasPersonalOrProjectFiles(full)) return false;

                // اگر هنوز در Program Files یا LocalAppData\Programs نصب است، باقی می ماند
                if (ExistsInProgramFiles(dirName) || ExistsInLocalAppDataPrograms(dirName)) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// پوشه های رهاشده را با حالت حذفِ انتخابی کاربر (تنظیمات → حالت حذف؛ پیش فرض:
        /// سطل بازیافت) حذف می کند و نتیجه ی صادقانه ی دسته را برمی گرداند.
        ///
        /// چرا SafeDeleteService و نه Directory.Delete؟ چون پوشه ی «بازمانده» ممکن است
        /// سِیو بازی یا تنظیمات شخصی کاربر باشد — حذف قطعیِ پوشه ی اشتباه داده
        /// غیرقابل بازگردانی از بین می برد. با مسیر SafeDeleteService حتی اگر
        /// تشخیص اشتباه بود، داده در سطل/قرنطینه باقی می ماند.
        /// فقط فضای واقعاً آزاد شده (حالت Permanent) در FreedBytes برمی گردد؛
        /// جابه جایی به سطل/قرنطینه در MovedBytes گزارش می شود.
        /// هر مسیر پیش از حذف دوباره بررسی ایمنی می شود.
        /// </summary>
        public static SafeDeleteBatchResult DeleteLeftoverFolders(IEnumerable<LeftoverFolderItem> items)
        {
            var batch = new SafeDeleteBatchResult();
            if (items == null) return batch;

            var safePaths = new List<string>();
            foreach (var item in items)
            {
                if (item == null) continue;
                try
                {
                    if (!IsSafeToDelete(item.FullPath)) continue;

                    // پوشه ی کاملاً خالی ارزش حرکت دادن به سطل ندارد؛ حذف قطعی بی خطر است
                    if (!Directory.EnumerateFileSystemEntries(item.FullPath).Any())
                    {
                        Directory.Delete(item.FullPath, false);
                        continue;
                    }

                    safePaths.Add(item.FullPath);
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex, "LeftoverCleaner.DeleteLeftoverFolders");
                }
            }

            if (safePaths.Count == 0) return batch;

            // حالت حذفِ کاربر (پیش فرض RecycleBin) — پوشه ی اشتباهی هم به سطل می رود نه نابودی
            var mode = SafeDeleteService.ParseMode(SettingsService.Load().DeleteMode);
            return SafeDeleteService.DeleteDirectories(safePaths, mode);
        }
    }
}
