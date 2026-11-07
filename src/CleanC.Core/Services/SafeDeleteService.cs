// src/CleanC.Core/Services/SafeDeleteService.cs
// لایه ی حذف ایمن و قابل بازگردانی.
//
// پیش از این، فایل های «شخصی» کاربر (تکراری ها، حجیم ها، دانلودها و دسکتاپ) با
// `FileInfo.Delete()` برای همیشه نابود می شدند، در حالی که دیالوگِ تأیید ادعا می کرد
// «به سطل بازیافت منتقل خواهد شد». این سرویس همان ادعا را واقعی می کند.
//
// سه حالت:
//   RecycleBin  → انتقال به سطل بازیافت ویندوز (پیش فرض؛ همان الگوی از قبل کارکرده در
//                 LargeFileScannerService). فضا تا خالی کردن سطل آزاد نمی شود.
//   Quarantine  → انتقال به قرنطینه ی داخلیِ CleanC با manifest؛ برای وقتی که سطل
//                 در دسترس نیست (درایو شبکه، سطل غیرفعال، مجوز ناکافی) یا کاربر
//                 می خواهد سطل را شلوغ نکند.
//   Permanent   → حذف قطعی؛ فقط برای کش/زباله که هدفشان آزادسازی واقعی فضا است.
//
// قاعده: اگر نه سطل کار کرد نه قرنطینه، شیء «حذف نمی شود» و خطا گزارش می شود.
// هیچ وقت بدون اجازه ی صریح، داده ی کاربر را برای همیشه از بین نمی بریم.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CleanC.Core.Diagnostics;
using Microsoft.VisualBasic.FileIO;

namespace CleanC.Core.Services
{
    public enum SafeDeleteMode
    {
        /// <summary>انتقال به سطل بازیافت ویندوز (پیش فرض).</summary>
        RecycleBin = 0,
        /// <summary>انتقال به قرنطینه ی داخلی CleanC با manifest قابل بازگردانی.</summary>
        Quarantine = 1,
        /// <summary>حذف قطعی.</summary>
        Permanent = 2
    }

    public enum SafeDeleteOutcome
    {
        Recycled,
        Quarantined,
        Permanent,
        /// <summary>مسیر از اول وجود نداشت؛ نه موفق است نه شکست.</summary>
        NotFound,
        Failed,
        /// <summary>فایل کپی شد اما به دلیل قفل بودن از مبدأ حذف نشد؛ برای جلوگیری از گزارش اشتباه موفقیت.</summary>
        QuarantinedSourceLocked
    }

    public sealed class SafeDeleteResult
    {
        public SafeDeleteOutcome Outcome { get; set; } = SafeDeleteOutcome.Failed;
        public string OriginalPath { get; set; } = string.Empty;
        /// <summary>در حالت Quarantine، مسیر ذخیره شده ی شیء داخل قرنطینه.</summary>
        public string StoredPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Error { get; set; } = string.Empty;

        public bool IsSuccess => Outcome is SafeDeleteOutcome.Recycled
            or SafeDeleteOutcome.Quarantined
            or SafeDeleteOutcome.Permanent
            or SafeDeleteOutcome.NotFound;

        /// <summary>فقط در حذف قطعی فضای دیسک واقعاً آزاد می شود؛ انتقال به سطل/قرنطینه فقط جابه جایی است.</summary>
        public bool FreesSpace => Outcome == SafeDeleteOutcome.Permanent;
    }

    public sealed class SafeDeleteBatchResult
    {
        public int Total { get; set; }
        public int RecycledCount { get; set; }
        public int QuarantinedCount { get; set; }
        public int PermanentCount { get; set; }
        public int FailedCount { get; set; }
        public int NotFoundCount { get; set; }

        /// <summary>فضایی که واقعاً از دیسک آزاد شد (فقط حذف قطعی).</summary>
        public long FreedBytes { get; set; }
        /// <summary>حجمی که فقط جابه جا شد (سطل بازیافت / قرنطینه) — آزاد نشده، ولی قابل بازگردانی است.</summary>
        public long MovedBytes { get; set; }
        public List<string> Errors { get; } = new();

        public int SucceededCount => Total - FailedCount - NotFoundCount;
    }

    /// <summary>یک نشست قرنطینه؛ معادل «یک بار حذف» از دید کاربر.</summary>
    public sealed class QuarantineManifest
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<QuarantineEntry> Entries { get; set; } = new();

        [JsonIgnore]
        public long TotalBytes => Entries.Sum(e => e.SizeBytes);
    }

    public sealed class QuarantineEntry
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string StoredPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long SizeBytes { get; set; }
        public DateTime QuarantinedAt { get; set; }
    }

    public static class SafeDeleteService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetVolumeNameForVolumeMountPoint(string lpszVolumeMountPoint, StringBuilder lpszVolumeName, uint cchBufferLength);

        private static string? GetVolumeGuid(string rootPath)
        {
            try
            {
                var sb = new StringBuilder(100);
                string root = rootPath.EndsWith("\\") ? rootPath : rootPath + "\\";
                if (GetVolumeNameForVolumeMountPoint(root, sb, (uint)sb.Capacity))
                {
                    string str = sb.ToString();
                    int start = str.IndexOf('{');
                    int end = str.IndexOf('}');
                    if (start >= 0 && end > start)
                    {
                        return str.Substring(start, end - start + 1);
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static (bool IsNuke, long? MaxCapBytes) GetBitBucketSettings(string rootPath)
        {
            try
            {
                string? guid = GetVolumeGuid(rootPath);
                if (string.IsNullOrEmpty(guid)) return (false, null);

                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\" + guid);
                if (key == null) return (false, null);

                var nukeObj = key.GetValue("NukeOnDelete");
                bool isNuke = nukeObj is int nukeVal && nukeVal == 1;

                var capObj = key.GetValue("MaxCapacity");
                long? capBytes = capObj is int capMb && capMb > 0 ? (long)capMb * 1024L * 1024L : null;

                return (isNuke, capBytes);
            }
            catch
            {
                return (false, null);
            }
        }

        public static bool HasRecycleBinSupport(string path)
        {
            try
            {
                string? root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root)) return false;

                var drive = new DriveInfo(root);
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                {
                    return false;
                }

                var (isNuke, _) = GetBitBucketSettings(root);
                if (isNuke) return false;

                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                int hr = SHQueryRecycleBin(root, ref info);
                return hr == 0;
            }
            catch
            {
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszProgressTitle;
        }

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;
        private const ushort FOF_WANTNUKEWARNING = 0x4000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        private static readonly object Sync = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static string? _quarantineRootOverride;
        private static string? _currentSessionId;
        private static QuarantineManifest? _currentManifest;

        /// <summary>رفتار پیش فرض برای فایل های شخصی کاربر؛ از تنظیمات برنامه ست می شود.</summary>
        public static SafeDeleteMode DefaultMode { get; set; } = SafeDeleteMode.RecycleBin;

        /// <summary>تبدیل مقدار رشته ای تنظیمات ({"RecycleBin"|"Quarantine"|"Permanent"}) به enum.</summary>
        public static SafeDeleteMode ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            "quarantine" => SafeDeleteMode.Quarantine,
            "permanent" => SafeDeleteMode.Permanent,
            _ => SafeDeleteMode.RecycleBin
        };

        /// <summary>نگاشت enum به همان رشته ای که در settings.json ذخیره می شود.</summary>
        public static string ModeToString(SafeDeleteMode mode) => mode switch
        {
            SafeDeleteMode.Quarantine => "Quarantine",
            SafeDeleteMode.Permanent => "Permanent",
            _ => "RecycleBin"
        };

        /// <summary>توصیف فارسیِ رفتار حذف برای نمایش در دیالوگ تأیید (صادقانه، نه وعده ی قطعی).</summary>
        public static string DescribeMode(SafeDeleteMode mode) => mode switch
        {
            SafeDeleteMode.Quarantine => LocalizationService.Get("در قرنطینه ی CleanC ذخیره خواهد شد (قابل بازگردانی از مرکز بازیابی)"),
            SafeDeleteMode.Permanent => LocalizationService.Get("برای همیشه حذف خواهد شد (قابل بازگردانی نیست)"),
            _ => LocalizationService.Get("به سطل بازیافت منتقل خواهد شد")
        };

        /// <summary>
        /// مسیر ریشه ی قرنطینه — برای تست قابل تغییر است.
        /// عوض شدن ریشه یعنی نشستِ باز باید بسته شود تا ورودی ها در ریشه ی جدید ثبت شوند.
        /// </summary>
        public static void ResetSessionForTesting()
        {
            lock (Sync)
            {
                _currentSessionId = null;
                _currentManifest = null;
            }
        }

        public static string QuarantineRoot
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_quarantineRootOverride)) return _quarantineRootOverride!;
                return AppPaths.QuarantineDir;
            }
            set
            {
                if (string.Equals(_quarantineRootOverride, value, StringComparison.OrdinalIgnoreCase)) return;
                _quarantineRootOverride = value;

                lock (Sync)
                {
                    _currentSessionId = null;
                    _currentManifest = null;
                }
            }
        }

        /// <summary>
        /// متن فارسیِ صادقانه برای گزارش یک دسته ی حذف — بسته به اینکه واقعاً چه اتفاقی
        /// افتاده (سطل / قرنطینه / قطعی / ناموفق)، نه یک متن ثابتِ احتمالی.
        /// </summary>
        public static string DescribeBatch(SafeDeleteBatchResult batch)
        {
            if (batch == null) return LocalizationService.Get("هیچ موردی حذف نشد.");

            var parts = new List<string>();
            if (batch.RecycledCount > 0) parts.Add(string.Format(LocalizationService.Get("{0} مورد به سطل بازیافت منتقل شد"), batch.RecycledCount));
            if (batch.QuarantinedCount > 0) parts.Add(string.Format(LocalizationService.Get("{0} مورد در قرنطینه ذخیره شد"), batch.QuarantinedCount));
            if (batch.PermanentCount > 0) parts.Add(string.Format(LocalizationService.Get("{0} مورد حذف قطعی شد"), batch.PermanentCount));

            string message = parts.Count > 0 ? string.Join(LocalizationService.Get("، "), parts) : LocalizationService.Get("هیچ موردی حذف نشد");

            if (batch.NotFoundCount > 0) message += string.Format(LocalizationService.Get("؛ {0} مورد از قبل وجود نداشت"), batch.NotFoundCount);
            if (batch.FailedCount > 0) message += string.Format(LocalizationService.Get("؛ {0} مورد ناموفق"), batch.FailedCount);

            if (batch.FreedBytes > 0)
                message += string.Format(LocalizationService.Get(" — {0} فضا آزاد گردید"), Models.SystemStats.FormatBytes(batch.FreedBytes));
            else if (batch.MovedBytes > 0)
                message += string.Format(LocalizationService.Get(" — {0} جابه جا شد (تا خالی کردن سطل آزاد نمی شود)"), Models.SystemStats.FormatBytes(batch.MovedBytes));

            return message;
        }

        // ── حذف فایل/پوشه ی شخصی ─────────────────────────────────────────

        public static SafeDeleteResult DeleteFile(string path, SafeDeleteMode? mode = null)
            => DeleteCore(path, mode ?? DefaultMode, isDirectory: false);

        public static SafeDeleteResult DeleteDirectory(string path, SafeDeleteMode? mode = null)
            => DeleteCore(path, mode ?? DefaultMode, isDirectory: true);

        /// <summary>حذف دسته ای فایل شخصی؛ خلاصه ی آماری برای گزارش UI.</summary>
        public static SafeDeleteBatchResult DeleteFiles(IEnumerable<string> paths, SafeDeleteMode? mode = null, Action<int, int>? progress = null)
            => DeleteMany(paths, mode, isDirectory: false, progress);

        public static SafeDeleteBatchResult DeleteDirectories(IEnumerable<string> paths, SafeDeleteMode? mode = null, Action<int, int>? progress = null)
            => DeleteMany(paths, mode, isDirectory: true, progress);

        /// <summary>
        /// حذف زباله/کش — همیشه قطعی، مستقل از <see cref="DefaultMode"/>.
        /// هدف این دسته ها آزادسازی فضاست؛ فرستادنشان به سطل فقط فضا را معطل می کند.
        /// </summary>
        public static SafeDeleteResult DeleteJunkFile(string path)
            => DeleteCore(path, SafeDeleteMode.Permanent, isDirectory: false);

        public static SafeDeleteResult DeleteJunkDirectory(string path)
            => DeleteCore(path, SafeDeleteMode.Permanent, isDirectory: true);

        private static SafeDeleteBatchResult DeleteMany(IEnumerable<string> paths, SafeDeleteMode? mode, bool isDirectory, Action<int, int>? progress = null)
        {
            var batch = new SafeDeleteBatchResult();
            if (paths == null) return batch;

            var list = paths as IList<string> ?? paths.ToList();
            int total = list.Count;
            int done = 0;

            foreach (string path in list)
            {
                batch.Total++;
                var result = isDirectory ? DeleteDirectory(path, mode) : DeleteFile(path, mode);

                switch (result.Outcome)
                {
                    case SafeDeleteOutcome.Recycled:
                        batch.RecycledCount++;
                        batch.MovedBytes += result.SizeBytes;
                        break;
                    case SafeDeleteOutcome.Quarantined:
                        batch.QuarantinedCount++;
                        batch.MovedBytes += result.SizeBytes;
                        break;
                    case SafeDeleteOutcome.Permanent:
                        batch.PermanentCount++;
                        batch.FreedBytes += result.SizeBytes;
                        break;
                    case SafeDeleteOutcome.NotFound:
                        batch.NotFoundCount++;
                        break;
                    case SafeDeleteOutcome.QuarantinedSourceLocked:
                        batch.FailedCount++;
                        batch.MovedBytes += result.SizeBytes;
                        if (!string.IsNullOrWhiteSpace(result.Error)) batch.Errors.Add($"{path}: {result.Error}");
                        break;
                    default:
                        batch.FailedCount++;
                        if (!string.IsNullOrWhiteSpace(result.Error)) batch.Errors.Add($"{path}: {result.Error}");
                        break;
                }

                done++;
                progress?.Invoke(done, total);
            }

            return batch;
        }

        /// <summary>
        /// مسیرهایی که هرگز نباید پاک شوند: ریشه درایوها، ویندوز، Program Files،
        /// ریشه پروفایل کاربر، پوشه Users و پوشه های شخصی (Documents/Desktop/...).
        /// </summary>
        public static bool IsProtectedSystemPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            if (LockRegistryService.IsUserLockedPath(path)) return true;

            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\', '/');
                if (full.Length <= 3 && full.Contains(':')) return true; // ریشه درایوها (C:, C:\, D:\)

                char sep = Path.DirectorySeparatorChar;

                // محافظت از ریشه‌ها و پوشه‌های خود برنامه
                if (!string.IsNullOrEmpty(AppPaths.LocalRoot))
                {
                    string lr = Path.GetFullPath(AppPaths.LocalRoot).TrimEnd('\\', '/');
                    if (string.Equals(full, lr, StringComparison.OrdinalIgnoreCase) ||
                        full.StartsWith(lr + sep, StringComparison.OrdinalIgnoreCase) ||
                        lr.StartsWith(full + sep, StringComparison.OrdinalIgnoreCase)) return true;
                }
                if (!string.IsNullOrEmpty(AppPaths.CommonRoot))
                {
                    string cr = Path.GetFullPath(AppPaths.CommonRoot).TrimEnd('\\', '/');
                    if (string.Equals(full, cr, StringComparison.OrdinalIgnoreCase) ||
                        full.StartsWith(cr + sep, StringComparison.OrdinalIgnoreCase) ||
                        cr.StartsWith(full + sep, StringComparison.OrdinalIgnoreCase)) return true;
                }
                if (!string.IsNullOrEmpty(AppContext.BaseDirectory))
                {
                    string bd = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\', '/');
                    if (string.Equals(full, bd, StringComparison.OrdinalIgnoreCase) ||
                        full.StartsWith(bd + sep, StringComparison.OrdinalIgnoreCase) ||
                        bd.StartsWith(full + sep, StringComparison.OrdinalIgnoreCase)) return true;
                }

                var userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var systemDir = Environment.SystemDirectory;
                var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var usersFolder = Path.Combine(Path.GetPathRoot(userDir) ?? "C:\\", "Users");

                var protectedTargets = new List<string>
                {
                    windowsDir,
                    systemDir,
                    progFiles,
                    progFilesX86,
                    usersFolder,
                    userDir,
                    progData,
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
                    protectedTargets.Add(Path.Combine(userDir, "Downloads"));
                    protectedTargets.Add(Path.Combine(userDir, "OneDrive"));
                    protectedTargets.Add(Path.Combine(userDir, "Desktop"));
                }

                foreach (var p in protectedTargets)
                {
                    if (string.IsNullOrEmpty(p)) continue;
                    string prot = Path.GetFullPath(p).TrimEnd('\\', '/');

                    // ۱. تطابق دقیق (Exact Match)
                    if (string.Equals(full, prot, StringComparison.OrdinalIgnoreCase))
                        return true;

                    // ۲. اجداد مسیرهای محافظت‌شده (Ancestors)
                    // اگر مسیر مورد بررسی، والد یا جد یک مسیر محافظت‌شده باشد (مثلاً انتخاب C:\Users یا C:\Windows)
                    if (prot.StartsWith(full + sep, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex, "SafeDeleteService.IsProtectedSystemPath");
                return true; // در صورت بروز هرگونه استثنا، حذف به عنوان اقدام احتیاطی رد شود
            }
            return false;
        }

        private static SafeDeleteResult DeleteCore(string path, SafeDeleteMode mode, bool isDirectory)
        {
            var result = new SafeDeleteResult { OriginalPath = path ?? string.Empty };

            if (string.IsNullOrWhiteSpace(path)) { result.Outcome = SafeDeleteOutcome.NotFound; return result; }

            // وتوی قفل کاربر: مسیری که کاربر قفل کرده حتی از Deep Cleanup هم حذف نمی شود
            if (LockRegistryService.IsUserLockedPath(path))
            {
                result.Outcome = SafeDeleteOutcome.Failed;
                result.Error = LocalizationService.Get("LockUserVetoError", "این مسیر توسط کاربر قفل شده است");
                return result;
            }

            // گارد امنیتی مرکزی: مسیرهای حساس سیستمی تحت هیچ شرایطی پاک نمی شوند
            if (IsProtectedSystemPath(path))
            {
                result.Outcome = SafeDeleteOutcome.Failed;
                result.Error = LocalizationService.Get("PathIsProtectedError", "این مسیر محافظت شده است و قابل پاک‌سازی نیست");
                return result;
            }

            try
            {
                bool fileExists = !isDirectory && File.Exists(path);
                bool dirExists = isDirectory && Directory.Exists(path);

                if (!fileExists && !dirExists)
                {
                    // فایل قبلاً حذف شده؛ نه موفقیت است نه خطا
                    result.Outcome = SafeDeleteOutcome.NotFound;
                    return result;
                }

                result.SizeBytes = Measure(path, isDirectory);

                switch (mode)
                {
                    case SafeDeleteMode.Permanent:
                        DeletePermanent(path, isDirectory);
                        result.Outcome = SafeDeleteOutcome.Permanent;
                        return result;

                    case SafeDeleteMode.Quarantine:
                        var qStatus = TryQuarantine(path, isDirectory, out string storedPath, out string quarantineError);
                        if (qStatus == QuarantineResult.Success)
                        {
                            result.Outcome = SafeDeleteOutcome.Quarantined;
                            result.StoredPath = storedPath;
                            return result;
                        }
                        if (qStatus == QuarantineResult.SourceLocked)
                        {
                            result.Outcome = SafeDeleteOutcome.QuarantinedSourceLocked;
                            result.StoredPath = storedPath;
                            result.Error = quarantineError;
                            return result;
                        }
                        // قرنطینه شکست خورد → آخرین شانس: سطل بازیافت
                        if (TryRecycleBin(path, isDirectory, out string recycleError))
                        {
                            result.Outcome = SafeDeleteOutcome.Recycled;
                            return result;
                        }
                        result.Error = FirstNonEmpty(quarantineError, recycleError);
                        result.Outcome = SafeDeleteOutcome.Failed;
                        return result;

                    default: // RecycleBin
                        if (TryRecycleBin(path, isDirectory, out string binError))
                        {
                            result.Outcome = SafeDeleteOutcome.Recycled;
                            return result;
                        }
                        // سطل در دسترس نیست یا فایل حجیم است → قرنطینه تا داده از بین نرود
                        var qBinStatus = TryQuarantine(path, isDirectory, out string stored, out string qError);
                        if (qBinStatus == QuarantineResult.Success)
                        {
                            result.Outcome = SafeDeleteOutcome.Quarantined;
                            result.StoredPath = stored;
                            return result;
                        }
                        if (qBinStatus == QuarantineResult.SourceLocked)
                        {
                            result.Outcome = SafeDeleteOutcome.QuarantinedSourceLocked;
                            result.StoredPath = stored;
                            result.Error = qError;
                            return result;
                        }
                        result.Error = FirstNonEmpty(binError, qError);
                        result.Outcome = SafeDeleteOutcome.Failed;
                        return result;
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SafeDeleteService.Delete");
                result.Error = ex.Message;
                result.Outcome = SafeDeleteOutcome.Failed;
                return result;
            }
        }

        private static string FirstNonEmpty(string a, string b)
            => !string.IsNullOrWhiteSpace(a) ? a : (b ?? string.Empty);

        // ── اجراکننده ها ──────────────────────────────────────────────────

        /// <summary>حذف قطعی؛ اتریبیوت های ReadOnly/Hidden/System اول برداشته می شوند.</summary>
        private static void DeletePermanent(string path, bool isDirectory)
        {
            if (isDirectory)
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            else
            {
                var fi = new FileInfo(path);
                if ((fi.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                    fi.Attributes = FileAttributes.Normal;
                fi.Delete();
            }
        }

        public static long? MaxRecycleItemSizeForTesting { get; set; }

        public static long GetRecycleBinMaxItemLimit(string path)
        {
            try
            {
                string? root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root))
                {
                    var (isNuke, regCap) = GetBitBucketSettings(root);
                    if (isNuke) return 0;
                    if (regCap.HasValue && regCap.Value > 0)
                    {
                        // سقف محافظه‌کارانه: حداکثر نیمی از ظرفیت کل سطل یا ۲ گیگابایت
                        return Math.Min(regCap.Value / 2, 2L * 1024 * 1024 * 1024);
                    }

                    var drive = new DriveInfo(root);
                    if (drive.IsReady && drive.TotalSize > 0)
                    {
                        long fivePercent = drive.TotalSize / 20;
                        return Math.Min(fivePercent, 2L * 1024 * 1024 * 1024);
                    }
                }
            }
            catch
            {
            }
            return 2L * 1024 * 1024 * 1024; // ۲ گیگابایت
        }

        private static bool TryRecycleBin(string path, bool isDirectory, out string error)
        {
            error = string.Empty;
            try
            {
                string fullPath = Path.GetFullPath(path);

                // ۱. بررسی پیشینی پشتیبانی درایو از سطل بازیافت با SHQueryRecycleBin و نوع درایو
                // در درایوهای مجازی/شبکه/بدون سطل، ویندوز حتی با FOF_WANTNUKEWARNING به دلیل FOF_NOCONFIRMATION ممکن است دائم پاک کند
                if (!HasRecycleBinSupport(fullPath))
                {
                    error = "Drive does not support Recycle Bin. Protected fallback to Quarantine.";
                    return false;
                }

                // ۲. بررسی اندازه آیتم: آیتم‌های بزرگ به دلیل بی‌اثر بودن تجربی FOF_WANTNUKEWARNING مستقیم به قرنطینه هدایت می‌شوند
                long limit = MaxRecycleItemSizeForTesting ?? GetRecycleBinMaxItemLimit(fullPath);
                long itemSize = Measure(fullPath, isDirectory);
                if (itemSize > limit)
                {
                    error = $"Item size ({itemSize} bytes) exceeds safe Recycle Bin threshold ({limit} bytes). Protected fallback to Quarantine.";
                    return false;
                }

                ClearBlockingAttributes(path, isDirectory);

                // SHFileOperation نیازمند بافر با انتهای دابل نال (\0\0) است
                string doubleNullPath = fullPath + "\0\0";

                var shf = new SHFILEOPSTRUCT
                {
                    hwnd = IntPtr.Zero,
                    wFunc = FO_DELETE,
                    pFrom = doubleNullPath,
                    pTo = null!,
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT | FOF_WANTNUKEWARNING,
                    fAnyOperationsAborted = false,
                    hNameMappings = IntPtr.Zero,
                    lpszProgressTitle = null!
                };

                int ret = SHFileOperation(ref shf);

                // اگر ویندوز عملیات را رد یا لغو کرد (مثلاً حجم فراتر از سطل بازیافت)
                if (ret != 0 || shf.fAnyOperationsAborted)
                {
                    error = $"Recycle Bin transfer aborted (Code: {ret}, Aborted: {shf.fAnyOperationsAborted}). Protected fallback to Quarantine.";
                    return false;
                }

                // بازیافت تنها در صورتی موفق اعلام می‌شود که واقعاً فایل از مبدأ رفته باشد
                return !Exists(fullPath, isDirectory);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SafeDeleteService.RecycleBin");
                error = FriendlyError(ex);
                return false;
            }
        }

        private static void ClearBlockingAttributes(string path, bool isDirectory)
        {
            try
            {
                if (isDirectory)
                {
                    var di = new DirectoryInfo(path);
                    if ((di.Attributes & FileAttributes.ReadOnly) != 0) di.Attributes = FileAttributes.Normal;
                }
                else
                {
                    var fi = new FileInfo(path);
                    if ((fi.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                        fi.Attributes = FileAttributes.Normal;
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SafeDeleteService.ClearAttributes");
            }
        }

        private static bool Exists(string path, bool isDirectory)
            => isDirectory ? Directory.Exists(path) : File.Exists(path);

        private static string FriendlyError(Exception ex) => ex switch
        {
            UnauthorizedAccessException => LocalizationService.Get("دسترسی کافی نیست"),
            DirectoryNotFoundException => LocalizationService.Get("مسیر پیدا نشد"),
            FileNotFoundException => LocalizationService.Get("فایل پیدا نشد"),
            IOException io when io.Message.Contains("recycle", StringComparison.OrdinalIgnoreCase) => LocalizationService.Get("سطل بازیافت در دسترس نیست"),
            IOException => LocalizationService.Get("فایل توسط برنامه ی دیگری قفل است"),
            _ => ex.Message
        };

        // ── قرنطینه ───────────────────────────────────────────────────────

        private enum QuarantineResult
        {
            Success,
            SourceLocked,
            Failed
        }

        private static QuarantineResult TryQuarantine(string path, bool isDirectory, out string storedPath, out string error)
        {
            storedPath = string.Empty;
            error = string.Empty;

            try
            {
                lock (Sync)
                {
                    EnsureSession();

                    // حجم را قبل از جابه جایی بگیر، چون بعد از move دیگر مسیر اصلی وجود ندارد
                    long size = Measure(path, isDirectory);

                    string index = (_currentManifest!.Entries.Count + 1).ToString("D4");
                    string safeName = Sanitize(Path.GetFileName(path.TrimEnd('\\', '/')));
                    string dataDir = Path.Combine(QuarantineRoot, _currentSessionId!, "data");
                    string target = Path.Combine(dataDir, $"{index}_{safeName}");

                    Directory.CreateDirectory(dataDir);
                    MoveWithFallback(path, target, isDirectory, out bool sourceDeletionFailed);
                    storedPath = target;

                    _currentManifest.Entries.Add(new QuarantineEntry
                    {
                        OriginalPath = path,
                        StoredPath = target,
                        IsDirectory = isDirectory,
                        SizeBytes = size,
                        QuarantinedAt = DateTime.Now
                    });

                    WriteManifestLocked(_currentManifest);

                    if (!isDirectory && sourceDeletionFailed)
                    {
                        error = LocalizationService.Get("SourceLockedAfterQuarantineCopy", "آیتم در قرنطینه کپی شد اما به دلیل قفل بودن از مبدأ حذف نشد");
                        return QuarantineResult.SourceLocked;
                    }

                    return QuarantineResult.Success;
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SafeDeleteService.Quarantine");
                error = FriendlyError(ex);
                return QuarantineResult.Failed;
            }
        }

        private static void MoveWithFallback(string source, string target, bool isDirectory)
            => MoveWithFallback(source, target, isDirectory, out _);

        private static void MoveWithFallback(string source, string target, bool isDirectory, out bool sourceDeletionFailed)
        {
            sourceDeletionFailed = false;
            try
            {
                if (isDirectory) Directory.Move(source, target);
                else File.Move(source, target);
            }
            catch (IOException)
            {
                // درایو متفاوت یا قفل موقت → کپی کامل و سپس حذف منبع
                if (isDirectory)
                {
                    bool copied = CopyDirectory(source, target);
                    if (!copied)
                    {
                        // اگر کپی کامل نبود، کپی ناقص را پاک کن (مبدأ دست‌نخورده است) و خطا بده تا قرنطینه متوقف شود
                        try
                        {
                            if (Directory.Exists(target)) Directory.Delete(target, true);
                        }
                        catch (Exception cleanEx)
                        {
                            CcLog.Ignore(cleanEx, "SafeDeleteService.CleanIncompleteTarget");
                        }
                        throw new IOException($"Failed to copy all files from '{source}' to quarantine target '{target}'. Source preserved.");
                    }

                    // فقط وقتی کپی کامل بود Directory.Delete(source) را صدا بزن و خطای قفل بودن را مدیریت کن
                    try
                    {
                        Directory.Delete(source, true);
                    }
                    catch (Exception delEx)
                    {
                        sourceDeletionFailed = true;
                        CcLog.Ignore(delEx, "SafeDeleteService.DeleteSourceFailedAfterCopy");
                    }
                }
                else
                {
                    File.Copy(source, target, overwrite: false);
                    try
                    {
                        var fi = new FileInfo(source);
                        if ((fi.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                            fi.Attributes = FileAttributes.Normal;
                        fi.Delete();
                    }
                    catch (Exception delEx)
                    {
                        sourceDeletionFailed = true;
                        CcLog.Ignore(delEx, "SafeDeleteService.DeleteSourceFileFailedAfterCopy");
                    }
                }
            }
        }

        internal static bool CopyDirectory(string source, string dest)
        {
            try
            {
                Directory.CreateDirectory(dest);
                bool allSuccess = true;

                foreach (string file in Directory.EnumerateFiles(source))
                {
                    try
                    {
                        File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
                    }
                    catch (Exception ex)
                    {
                        CcLog.Ignore(ex, "SafeDeleteService.CopyDirectory.File");
                        allSuccess = false;
                    }
                }

                foreach (string sub in Directory.EnumerateDirectories(source))
                {
                    try
                    {
                        var attrs = File.GetAttributes(sub);
                        var di = new DirectoryInfo(sub);
                        if ((attrs & FileAttributes.ReparsePoint) != 0 || (di.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            // Skip reparse points (junctions/symlinks) to avoid recursion or copying external data
                            continue;
                        }

                        if (!CopyDirectory(sub, Path.Combine(dest, Path.GetFileName(sub))))
                        {
                            allSuccess = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        CcLog.Ignore(ex, "SafeDeleteService.CopyDirectory.SubDir");
                        allSuccess = false;
                    }
                }

                return allSuccess;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SafeDeleteService.CopyDirectory.Root");
                return false;
            }
        }
        private static void EnsureSession()
        {
            if (_currentManifest != null && _currentSessionId != null) return;

            _currentSessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string dir = Path.Combine(QuarantineRoot, _currentSessionId);
            Directory.CreateDirectory(dir);

            _currentManifest = new QuarantineManifest
            {
                SessionId = _currentSessionId,
                CreatedAt = DateTime.Now
            };
            WriteManifestLocked(_currentManifest);
        }

        private static void WriteManifestLocked(QuarantineManifest manifest)
        {
            string dir = Path.Combine(QuarantineRoot, manifest.SessionId);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "manifest.json");
            string tmp = path + ".tmp";

            File.WriteAllText(tmp, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "item";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

            string cleaned = sb.ToString().Trim();
            if (cleaned.Length == 0) return "item";
            return cleaned.Length > 80 ? cleaned.Substring(0, 80) : cleaned;
        }

        // ── خواندن/بازگردانی قرنطینه ─────────────────────────────────────

        /// <summary>همه ی نشست های قرنطینه موجود (جدیدترین اول).</summary>
        public static List<QuarantineManifest> ListQuarantineSessions()
        {
            var sessions = new List<QuarantineManifest>();
            try
            {
                if (!Directory.Exists(QuarantineRoot)) return sessions;

                foreach (string dir in Directory.EnumerateDirectories(QuarantineRoot))
                {
                    string manifestPath = Path.Combine(dir, "manifest.json");
                    if (!File.Exists(manifestPath)) continue;

                    try
                    {
                        var manifest = JsonSerializer.Deserialize<QuarantineManifest>(
                            File.ReadAllText(manifestPath), JsonOptions);
                        if (manifest?.Entries != null && manifest.Entries.Count > 0)
                            sessions.Add(manifest);
                    }
                    catch (Exception ex)
                    {
                        CcLog.Ignore(ex, "SafeDeleteService.ListQuarantineSessions");
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SafeDeleteService.ListQuarantineSessions.Root");
            }

            return sessions.OrderByDescending(s => s.CreatedAt).ToList();
        }

        /// <summary>بازگرداندن همه ی ورودی های یک نشست به مسیرهای اصلی.</summary>
        public static (int Restored, int Failed, string Error) RestoreSession(string sessionId)
        {
            int restored = 0, failed = 0;
            string lastError = string.Empty;

            var manifest = ListQuarantineSessions()
                .FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

            if (manifest == null) return (0, 0, LocalizationService.Get("نشست قرنطینه پیدا نشد."));

            foreach (var entry in manifest.Entries)
            {
                try
                {
                    if (!Exists(entry.StoredPath, entry.IsDirectory)) { failed++; continue; }

                    string dest = entry.OriginalPath;
                    string? destDir = Path.GetDirectoryName(dest);
                    if (string.IsNullOrWhiteSpace(destDir)) { failed++; lastError = LocalizationService.Get("مسیر مقصد نامعتبر است."); continue; }

                    Directory.CreateDirectory(destDir);
                    if (Exists(dest, entry.IsDirectory)) dest = MakeUniquePath(dest);

                    MoveWithFallback(entry.StoredPath, dest, entry.IsDirectory);
                    restored++;
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex, "SafeDeleteService.RestoreSession");
                    failed++;
                    lastError = ex.Message;
                }
            }

            // اگر همه بازگردانده شد، پوشه ی نشست را پاک کن
            if (failed == 0) TryDeleteSessionDir(sessionId);
            else RefreshManifestAfterRestore(sessionId, manifest);

            return (restored, failed, lastError);
        }

        private static void RefreshManifestAfterRestore(string sessionId, QuarantineManifest manifest)
        {
            try
            {
                manifest.Entries.RemoveAll(e => !Exists(e.StoredPath, e.IsDirectory));
                if (manifest.Entries.Count == 0) { TryDeleteSessionDir(sessionId); return; }

                lock (Sync) WriteManifestLocked(manifest);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SafeDeleteService.RefreshManifestAfterRestore");
            }
        }

        /// <summary>حذف قطعی یک نشست قرنطینه (دور ریختن امن).</summary>
        public static bool DeleteSession(string sessionId)
        {
            var manifest = ListQuarantineSessions()
                .FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
            if (manifest == null) return false;

            foreach (var entry in manifest.Entries)
            {
                try
                {
                    if (Directory.Exists(entry.StoredPath)) Directory.Delete(entry.StoredPath, true);
                    else if (File.Exists(entry.StoredPath)) File.Delete(entry.StoredPath);
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex, "SafeDeleteService.DeleteSession.Entry");
                }
            }

            TryDeleteSessionDir(sessionId);
            return true;
        }

        /// <summary>پاک سازی نشست های قدیمی تر از X روز؛ تعداد نشست های حذف شده را برمی گرداند.</summary>
        public static int PurgeOldSessions(int maxAgeDays, long maxTotalBytes = 500_000_000)
        {
            if (maxAgeDays < 1) return 0;
            int purged = 0;

            var sessions = ListQuarantineSessions();
            foreach (var session in sessions)
            {
                if ((DateTime.Now - session.CreatedAt).TotalDays >= maxAgeDays)
                {
                    if (DeleteSession(session.SessionId)) purged++;
                }
            }

            // کنترل حجم کل قرنطینه در AppData (سقف ۵۰۰ مگابایت)
            try
            {
                var remaining = ListQuarantineSessions().OrderBy(s => s.CreatedAt).ToList();
                long total = remaining.Sum(s => s.TotalBytes);
                while (total > maxTotalBytes && remaining.Count > 0)
                {
                    var oldest = remaining[0];
                    remaining.RemoveAt(0);
                    total -= oldest.TotalBytes;
                    if (DeleteSession(oldest.SessionId)) purged++;
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SafeDeleteService.CapQuarantineSize");
            }

            return purged;
        }

        private static void TryDeleteSessionDir(string sessionId)
        {
            try
            {
                string dir = Path.Combine(QuarantineRoot, sessionId);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SafeDeleteService.TryDeleteSessionDir");
            }
        }

        private static string MakeUniquePath(string path)
        {
            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);

            for (int i = 1; i < 1000; i++)
            {
                string suffix = i == 1 ? " (restored)" : $" (restored {i})";
                string candidate = Path.Combine(dir, name + suffix + ext);
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }

            return Path.Combine(dir, $"{name} ({Guid.NewGuid():N}){ext}");
        }

        // ── اندازه گیری ───────────────────────────────────────────────────

        private static long Measure(string path, bool isDirectory)
        {
            try
            {
                if (isDirectory)
                {
                    if (!Directory.Exists(path)) return 0;
                    return RecycleBinService.ComputeDirectorySize(path);
                }

                if (File.Exists(path)) return new FileInfo(path).Length;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SafeDeleteService.Measure");
            }

            return 0;
        }
    }
}
