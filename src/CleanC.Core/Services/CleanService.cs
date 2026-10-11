// O:\OpenCode\AntiGravity\CleanC - NEW\src\CleanC.Core\Services\CleanService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;
using CleanC.Core.Native;

namespace CleanC.Core.Services
{
    public class CleanService
    {
        public static async Task<CleanResult> CleanItemsAsync(IEnumerable<JunkCategory> items, Action<string>? progress = null)
        {
            return await Task.Run(async () =>
            {
                var result = new CleanResult { Success = true };
                long totalFreed = 0;

                foreach (var item in items)
                {
                    if (!item.IsChecked) continue;

                    progress?.Invoke($"در حال پاک سازی: {item.Title}...");
                    long freed = CleanCategory(item);
                    totalFreed += freed;
                    result.CleanedItems.Add(item.Title);
                }

                // Automatic RAM boost + DNS flush as part of clean
                progress?.Invoke("بهینه سازی حافظه و رفرش شبکه...");
                var boost = await OptimizerService.BoostRamAsync();
                totalFreed += boost.FreedBytes;
                OptimizerService.FlushDns();

                result.FreedBytes = totalFreed;
                return result;
            });
        }

        /// <summary>
        /// یک دسته زائد را پاک می کند و «فقط» فضایی که واقعاً آزاد شده را برمی گرداند.
        /// هرگز مقدار تخمینی اسکن را به جای نتیجه واقعی گزارش نمی کند.
        /// </summary>
        public static long CleanCategory(JunkCategory item)
        {
            if (item == null) return 0;
            long freed = 0;

            if (item.Id == "recycleBin")
            {
                try
                {
                    // D5: تأیید خالی کردن سطل بازیافت را به خود ویندوز می سپاریم
                    // (SHERB_NOCONFIRMATION حذف شد) تا کاربر قبل از پاکسازی دائمی بداند چه می کند.
                    int hr = Win32.SHEmptyRecycleBin(IntPtr.Zero, null,
                        Win32.SHERB_NOPROGRESSUI | Win32.SHERB_NOSOUND);
                    // فقط در صورت موفقیت (S_OK) فضای تخمینی را به عنوان آزادشده بپذیر
                    return hr == 0 ? item.Bytes : 0;
                }
                catch
                {
                    return 0;
                }
            }

            if (item.Id == "orphanedRegistry")
            {
                try
                {
                    // کلیدهای رجیستری فضای دیسک آزاد نمی کنند؛ صفر برمی گردانیم تا گزارش دروغ نباشد
                    RegistryCleanerService.CleanOrphanedRegistry();
                    return 0;
                }
                catch
                {
                    return 0;
                }
            }

            if (item.Id == "emptyFolders")
            {
                if (item.TargetPaths != null)
                {
                    foreach (var path in item.TargetPaths)
                    {
                        try
                        {
                            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                            {
                                Directory.Delete(path, false);
                            }
                        }
                        catch (Exception __ex) { CcLog.Ignore(__ex); }
                    }
                }
                return 0;
            }

            if (item.Id == "browserDeadExtensions")
            {
                try
                {
                    return BrowserOptimizerService.CleanInactiveChromeExtensions();
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                    return 0;
                }
            }

            var paths = item.TargetPaths != null && item.TargetPaths.Count > 0
                ? item.TargetPaths
                : (!string.IsNullOrEmpty(item.Path) ? new List<string> { item.Path } : new List<string>());

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;

                // پوشه های رهاشده فقط با بازبینی کامل LeftoverCleaner حذف می شوند
                if (item.GroupId == "leftovers" && !LeftoverCleanerService.IsSafeToDelete(path)) continue;

                if (Directory.Exists(path))
                {
                    // اعمال آستانه سنی هوشمند (۱۴ روز) برای کش‌های مرورگر جهت حفظ سرعت لود صفحات
                    TimeSpan? minAge = (item.Id == "chromeCache" || item.Id == "edgeCache")
                        ? TimeSpan.FromDays(14)
                        : null;

                    freed += SafeCleanFolder(path, minAge);

                    if (item.GroupId == "leftovers")
                    {
                        try
                        {
                            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                            {
                                Directory.Delete(path, false);
                            }
                        }
                        catch (Exception __ex)
                        {
                            CcLog.Ignore(__ex);
                        }
                    }
                }
                else if (File.Exists(path))
                {
                    if (IsProtectedSystemPath(path)) continue;
                    try
                    {
                        var fi = new FileInfo(path);
                        if (IsReparsePoint(fi.Attributes)) continue;
                        long len = fi.Length;
                        if ((fi.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                        {
                            fi.Attributes = FileAttributes.Normal;
                        }
                        fi.Delete();
                        freed += len;
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
                }
            }

            return freed;
        }

        /// <summary>
        /// مسیرهایی که هرگز نباید پاک شوند: ریشه درایوها، ویندوز، Program Files،
        /// ریشه پروفایل کاربر، پوشه Users و پوشه های شخصی (Documents/Desktop/...).
        /// </summary>
        private static bool IsProtectedSystemPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\', '/');

                // Disallow cleaning drive roots (e.g. C:, C:\, D:, D:\)
                if (full.Length <= 3 && full.Contains(':')) return true;

                var userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var protectedExact = new List<string>
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.SystemDirectory,
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    userDir,
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    Path.Combine(Path.GetPathRoot(userDir) ?? "C:\\", "Users"),
                    // پوشه های شخصی: داخل اینها فایل کاربر است، نه زائد سیستم
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                };
                if (!string.IsNullOrEmpty(userDir))
                {
                    protectedExact.Add(Path.Combine(userDir, "Downloads"));
                    protectedExact.Add(Path.Combine(userDir, "OneDrive"));
                    protectedExact.Add(Path.Combine(userDir, "Desktop"));
                }

                foreach (var p in protectedExact)
                {
                    if (string.IsNullOrEmpty(p)) continue;
                    if (string.Equals(full, p.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return false;
        }

        /// <summary>
        /// جلوگیری از پیروی junction/symlink: اگر مسیر یک reparse point باشد،
        /// حذف recursive می تواند کل هدف را از بین ببرد.
        /// </summary>
        private static bool IsReparsePoint(FileAttributes attributes)
            => (attributes & FileAttributes.ReparsePoint) != 0;

        public static long SafeCleanFolder(string dirPath, TimeSpan? minAge = null)
        {
            if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath) || IsProtectedSystemPath(dirPath)) return 0;
            long freed = 0;

            var enumOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = 0
            };

            DateTime? thresholdUtc = minAge.HasValue ? DateTime.UtcNow - minAge.Value : null;

            try
            {
                var dir = new DirectoryInfo(dirPath);
                if (IsReparsePoint(dir.Attributes)) return 0;

                // Enumerate and delete files
                foreach (var file in dir.EnumerateFiles("*", enumOptions))
                {
                    try
                    {
                        if (IsReparsePoint(file.Attributes)) continue;

                        // کش زنده و تازه که لود صفحات را سریع نگه می‌دارد؛ دست نزن!
                        if (thresholdUtc.HasValue && file.LastWriteTimeUtc > thresholdUtc.Value)
                        {
                            continue;
                        }

                        long len = file.Length;
                        if ((file.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                        {
                            file.Attributes = FileAttributes.Normal;
                        }
                        file.Delete();
                        freed += len;
                    }
                    catch
                    {
                        // File locked by running process - skip safely
                    }
                }

                // Recursively clean subdirectories
                foreach (var sub in dir.EnumerateDirectories("*", enumOptions))
                {
                    try
                    {
                        if (IsReparsePoint(sub.Attributes)) continue;

                        freed += SafeCleanFolder(sub.FullName, minAge);

                        // اگر شرط زمانی نداشته باشیم و زیرپوشه خالی شده باشد، آن را پاک می‌کنیم
                        if (!thresholdUtc.HasValue)
                        {
                            if ((sub.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                            {
                                sub.Attributes = FileAttributes.Normal;
                            }
                            // فقط زمانی حذف می شود که واقعاً خالی مانده باشد
                            sub.Delete(false);
                        }
                    }
                    catch
                    {
                        // Subdirectory still in use or contains locked file - skip
                    }
                }
            }
            catch
            {
                // Inaccessible folder - skip
            }

            return freed;
        }
    }
}
