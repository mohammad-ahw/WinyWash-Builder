// O:\OpenCode\AntiGravity\CleanC - NEW\src\CleanC.Core\Services\CleanService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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

                    progress?.Invoke(string.Format(LocalizationService.Get("در حال پاک سازی: {0}..."), item.Title));
                    long freed = CleanCategory(item);
                    totalFreed += freed;
                    result.CleanedItems.Add(item.Title);
                }

                // Automatic RAM boost + DNS flush as part of clean
                progress?.Invoke(LocalizationService.Get("بهینه سازی حافظه و رفرش شبکه..."));
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
                    // تأیید خالی کردن سطل بازیافت را به خود ویندوز می سپاریم
                    // (SHERB_NOCONFIRMATION حذف شد) تا کاربر قبل از پاکسازی دائمی بداند چه می کند.
                    //
                    // SHEmptyRecycleBin نیازمند apartment سازگار با شل (STA/MMSTA) است؛
                    // صدا زدن مستقیم روی Thread موجود در Task.Run دچار خطای E_UNEXPECTED
                    // می شود، پس روی Thread مخصوص STA اجرا می کنیم و نتیجه برمی گردد.
                    int hr = -1;
                    var staThread = new Thread(() =>
                    {
                        try
                        {
                            hr = Win32.SHEmptyRecycleBin(IntPtr.Zero, null,
                                Win32.SHERB_NOPROGRESSUI | Win32.SHERB_NOSOUND);
                        }
                        catch
                        {
                            hr = -1;
                        }
                    })
                    {
                        IsBackground = true,
                        Name = "CleanC-RecycleBin-STA"
                    };
                    staThread.SetApartmentState(System.Threading.ApartmentState.STA);
                    staThread.Start();
                    if (!staThread.Join(TimeSpan.FromSeconds(120)))
                    {
                        // ویندوز دیالوگ حذف امنیت را باز نگه داشته است؛ برای گیر نکردن کل
                        // روند پاکسازی صرفنظر می کنیم (Thread پس زمینه است و می ماند)
                        return 0;
                    }
                    // فقط در صورت موفقیت (S_OK) فضای تخمینی را به عنوان آزادشده بپذیر
                    // (بر خلاف قبل، فضای واقعی سنجیده نمی شود چون شل خودش حساب می کند)
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

            if (item.GroupId == "system_boost" || item.Id == "ramBoost" || item.Id == "dnsFlush" ||
                item.Id == "backgroundHelpers" || item.Id == "browserBoost" || item.Id == "startupOptimize" ||
                item.Id == "bloatwareClean" || item.Id == "fastStartup" || item.Id == "gpuOptimize" ||
                item.Id == "winTweaks" || item.Id == "inactiveHeavyApps")
            {
                try
                {
                    switch (item.Id)
                    {
                        case "ramBoost":
                            var boost = OptimizerService.BoostRamAsync().GetAwaiter().GetResult();
                            return boost.FreedBytes;
                        case "dnsFlush":
                            OptimizerService.FlushDns();
                            return 0;
                        case "backgroundHelpers":
                            ProcessManagerService.CleanBackgroundHelpers();
                            return 0;
                        case "browserBoost":
                            BrowserOptimizerService.SuppressBrowserBackgroundModeAsync().GetAwaiter().GetResult();
                            return 0;
                        case "startupOptimize":
                            StartupOptimizerService.OptimizeStartupAppsAsync().GetAwaiter().GetResult();
                            return 0;
                        case "bloatwareClean":
                            BloatwareService.CleanAutomaticBloatwareAsync().GetAwaiter().GetResult();
                            return 0;
                        case "fastStartup":
                            FastStartupService.SetFastStartup(true);
                            return 0;
                        case "gpuOptimize":
                            GpuOptimizerService.OptimizeGpuPreferencesAsync().GetAwaiter().GetResult();
                            return 0;
                        case "winTweaks":
                            WindowsTweakService.ApplyLowRiskPerformanceTweaksAsync().GetAwaiter().GetResult();
                            return 0;
                        case "inactiveHeavyApps":
                            ProcessManagerService.EaseHighCpuProcesses();
                            return 0;
                        default:
                            return 0;
                    }
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

            // حالت حذفِ کاربر برای پوشه های رهاشده — یک بار در ابتدای دسته خوانده می شود
            SafeDeleteMode? leftoverMode = item.GroupId == "leftovers"
                ? SafeDeleteService.ParseMode(SettingsService.Load().DeleteMode)
                : null;

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;

                // پوشه های رهاشده فقط با بازبینی کامل LeftoverCleaner حذف می شوند
                if (item.GroupId == "leftovers" && !LeftoverCleanerService.IsSafeToDelete(path)) continue;

                // پوشه ی بازمانده ممکن است سِیو بازی باشد → کل پوشه با حالت انتخابی کاربر
                // حذف می شود (پیش فرض: سطل بازیافت)، نه پاک کردن قطعیِ محتوا.
                // فقط فضای واقعاً آزاد شده (حالت Permanent) در «freed» شمرده می شود.
                if (leftoverMode.HasValue)
                {
                    if (!Directory.Exists(path)) continue;
                    var leftoverResult = SafeDeleteService.DeleteDirectory(path, leftoverMode.Value);
                    if (leftoverResult.Outcome == SafeDeleteOutcome.Permanent)
                        freed += leftoverResult.SizeBytes;
                    continue;
                }

                if (Directory.Exists(path))
                {
                    // اعمال آستانه سنی هوشمند (۱۴ روز) برای کش‌های مرورگر جهت حفظ سرعت لود صفحات
                    TimeSpan? minAge = (item.Id == "chromeCache" || item.Id == "edgeCache")
                        ? TimeSpan.FromDays(14)
                        : null;

                    freed += SafeCleanFolder(path, minAge);
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
        private static bool IsProtectedSystemPath(string path) => SafeDeleteService.IsProtectedSystemPath(path);

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
