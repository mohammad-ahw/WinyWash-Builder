// src/CleanC.Core/Services/ExpeditionService.cs
// موتور «اکسپدیشن وینی» — Zone 1: Downloads
//
// بازی، لایه‌ی «چشم انسانی» روی موتور تشخیص است: این سرویس هرگز چیزی را حذف نمی‌کند؛
// فقط «پرونده» (candidate + evidence) می‌سازد. اجرای حذف فقط از مسیر ExecuteZoneBatchAsync
// می‌گذرد که به SafeDeleteService (سطل بازیافت/قرنطینه) سپرده می‌شود و فقط پس از
// تأیید یک‌باره‌ی کاربر در پایان گشت (قرارداد طراحی بازی).
//
// قواعد امنیتی Zone 1 (نسخه‌ی اول):
//   • فقط فایل‌های سطح اولِ Downloads — بدون ورود به زیرپوشه‌ها (فاز ۳ = غارهای عمیق)
//   • عبور از reparse point (junction/symlink) همسو با بقیه‌ی موتور پاکسازی
//   • رسانه‌ی شخصی (عکس/ویدیو/سند) بدون الگوی کپی هرگز کاندید نمی‌شود (D17: فقط محیط)
//   • سقف تعداد و سقف شمارش تا اسکن همیشه سریع بماند

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;

namespace CleanC.Core.Services
{
    /// <summary>دسته‌ی پرونده — هر bucket یک معیارِ اثبات مستقل دارد.</summary>
    public enum ExpeditionBucket
    {
        /// <summary>دانلودِ ناتمام (.crdownload/.part) — مدرک قطعی.</summary>
        UnfinishedDownload = 0,
        /// <summary>زباله‌ی موقت (.tmp/.temp قدیمی، فایل خالی قدیمی) — قطعی/محتمل.</summary>
        TempTrash = 1,
        /// <summary>نصب‌کننده‌ی قدیمی بی‌استفاده — محتمل.</summary>
        OldInstaller = 2,
        /// <summary>کپیِ نام‌دار «(1)» با همتای موجود — مدرک قطعی.</summary>
        DuplicateCopy = 3,
        /// <summary>فایل خیلی حجیم و قدیمیِ غیر رسانه‌ای — مرزی (داوری کاربر).</summary>
        LargeOldFile = 4
    }

    /// <summary>قوت ادعا: ۳=قطعی، ۲=محتمل، ۱=مرزی (داوری حس خود کاربر).</summary>
    public enum ExpeditionConfidence
    {
        Borderline = 1,
        Likely = 2,
        Strong = 3
    }

    /// <summary>یک پرونده‌ی قابل داوری.</summary>
    public sealed class ExpeditionCaseItem
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime LastWriteUtc { get; set; }
        public ExpeditionBucket Bucket { get; set; }
        public ExpeditionConfidence Confidence { get; set; }
        /// <summary>خطوط مدرک — صادقانه؛ هر ادعایی که سرویس انجام نداده ذکر نمی‌شود.</summary>
        public List<string> Evidence { get; } = new();
        /// <summary>نام همتای اصلی برای کپی‌های نام‌دار (فقط DuplicateCopy).</summary>
        public string? PairName { get; set; }
        public long PairSize { get; set; }
        /// <summary>نام فایل بندانگشتی داخل پوشه‌ی داده‌ی اکسپدیشن (گاهی خالی).</summary>
        public string? ThumbFile { get; set; }
        public bool IsImage { get; set; }
    }

    /// <summary>یک آیتم محیطی (غیرِ قابل داوری) برای زنده بودنِ دنیای بازی.</summary>
    public sealed class ExpeditionAmbientItem
    {
        public string Name { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime LastWriteUtc { get; set; }
        public bool IsImage { get; set; }
        public bool IsVideo { get; set; }
    }

    /// <summary>خروجی یک اسکن Zone 1.</summary>
    public sealed class ExpeditionZoneScan
    {
        public string ZoneId { get; set; } = "downloads_dehleez";
        public string Dir { get; set; } = string.Empty;
        public List<ExpeditionCaseItem> Cases { get; } = new();
        public List<ExpeditionAmbientItem> Ambient { get; } = new();
        public int ScannedCount { get; set; }
    }

    public static class ExpeditionService
    {
        /// <summary>سقف پرونده — Zone 1 هرگز یک لیست خسته‌کننده تلنبار نمی‌کند.</summary>
        public const int MaxCases = 160;
        public const int MaxAmbient = 60;

        private static readonly Lazy<string> CachedDownloads = new(() =>
        {
            try
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Expedition.Downloads");
                return string.Empty;
            }
        });

        private static readonly HashSet<string> UnfinishedExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".crdownload", ".part"
        };

        private static readonly HashSet<string> TempExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".tmp", ".temp"
        };

        private static readonly HashSet<string> InstallerExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".msi", ".msix"
        };

        private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".heic", ".avif"
        };

        private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".m4v"
        };

        private static readonly HashSet<string> DocExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".one"
        };

        private static readonly Regex CopyNameRegex = new(
            @"^(?<base>.+?) \((?<n>\d+)\)(?<ext>\.[^.]+)?$",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        public static string DownloadsDir => CachedDownloads.Value ?? string.Empty;

        public static ExpeditionCaseItem? TryMakeDuplicateCopyCase(
            FileInfo fi, string ext, HashSet<string> filesInView, string root)
        {
            // رسانه‌ی شخصی فقط در صورت وجود همتای واقعی کاندید می‌شود؛ بدون همتا هرگز نه
            var m = CopyNameRegex.Match(fi.Name);
            if (!m.Success) return null;

            var baseName = m.Groups["base"].Value + (m.Groups["ext"].Success ? m.Groups["ext"].Value : string.Empty);
            var basePath = Path.Combine(root, baseName);
            bool baseExists = filesInView.Contains(basePath) || File.Exists(basePath);
            if (!baseExists) return null;

            long pairSize = 0;
            try { pairSize = new FileInfo(basePath).Length; }
            catch (Exception ex) { CcLog.Ignore(ex); }

            var item = NewCase(fi, ExpeditionBucket.DuplicateCopy, ExpeditionConfidence.Strong);
            item.Evidence.Add(string.Format(LocalizationService.Get("کپی نام‌دار «({0})»"), m.Groups["n"].Value));
            item.Evidence.Add(string.Format(LocalizationService.Get("دوقلوی اصلی همین پوشه: {0}"), baseName));
            item.Evidence.Add(string.Format(LocalizationService.Get("حجم همتا: {0}"), SystemStats.FormatBytes(pairSize)));
            item.PairName = baseName;
            item.PairSize = pairSize;
            return item;
        }

        /// <summary>اسکن Zone 1 — همیشه سریع (فقط سطح اول، بدون هش).</summary>
        public static Task<ExpeditionZoneScan> ScanZoneDownloadsAsync()
            => Task.Run(() => ScanZoneDownloads());

        public static ExpeditionZoneScan ScanZoneDownloads()
        {
            var zone = new ExpeditionZoneScan();
            var root = DownloadsDir;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                zone.Dir = root;
                return zone;
            }
            zone.Dir = root;

            // پنهان/سیستم/reparse از قبل در AttributesToSkip رد می‌شوند؛ سطح اول بدون بازگشتی
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
                RecurseSubdirectories = false
            };

            int scanned = 0;
            var candidates = new List<ExpeditionCaseItem>();
            var ambient = new List<ExpeditionAmbientItem>();
            var filesInView = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var fi in new DirectoryInfo(root).EnumerateFiles("*", options))
            {
                scanned++;
                filesInView.Add(fi.FullName);
            }

            foreach (var fi in new DirectoryInfo(root).EnumerateFiles("*", options))
            {
                if (candidates.Count >= MaxCases && ambient.Count >= MaxAmbient) break;

                var ext = fi.Extension;
                bool isImage = ImageExts.Contains(ext);
                bool isVideo = VideoExts.Contains(ext);
                bool isDoc = DocExts.Contains(ext);
                var age = DateTime.UtcNow - fi.LastWriteTimeUtc;
                long mb = fi.Length / (1024 * 1024);

                // —— ۱) کپیِ نام‌دار «(1)»: فقط اگر همتای اصلی در همان پوشه باشد (مدرک قطعی)
                var copyCase = TryMakeDuplicateCopyCase(fi, ext, filesInView, root);
                if (copyCase != null)
                {
                    copyCase.IsImage = isImage;
                    candidates.Add(copyCase);
                    continue;
                }

                // —— ۲) دانلودِ ناتمام: .crdownload/.part با عمر ≥۶ ساعت
                // (دانلود در جریان با عمر کم دست نمی‌خورَد — گارد مهم)
                if (UnfinishedExts.Contains(ext))
                {
                    if (age.TotalHours >= 6)
                    {
                        var item = NewCase(fi, ExpeditionBucket.UnfinishedDownload, ExpeditionConfidence.Strong);
                        item.Evidence.Add(string.Format(LocalizationService.Get("پسوند نیمه‌تمام مرورگر ({0})"), ext));
                        item.Evidence.Add(string.Format(LocalizationService.Get("عمر: {0}"), FormatDays(age)));
                        candidates.Add(item);
                    }
                    continue;
                }

                // —— ۳) زباله‌ی موقت: .tmp/.temp با عمر ≥۷ روز
                if (TempExts.Contains(ext) && age.TotalDays >= 7)
                {
                    var item = NewCase(fi, ExpeditionBucket.TempTrash, ExpeditionConfidence.Strong);
                    item.Evidence.Add(string.Format(LocalizationService.Get("پسوند موقت ({0})"), ext));
                    item.Evidence.Add(string.Format(LocalizationService.Get("عمر: {0}"), FormatDays(age)));
                    candidates.Add(item);
                    continue;
                }

                // —— ۴) نصب‌کننده‌ی قدیمی: ≥۲۰MB با عمر ≥۴۵ روز (منطقِ محتمل)
                if (InstallerExts.Contains(ext) && mb >= 20 && age.TotalDays >= 45)
                {
                    var item = NewCase(fi, ExpeditionBucket.OldInstaller, ExpeditionConfidence.Likely);
                    item.Evidence.Add(string.Format(LocalizationService.Get("نصب‌کننده ({0}) به حجم {1}"), ext, SystemStats.FormatBytes(fi.Length)));
                    item.Evidence.Add(string.Format(LocalizationService.Get("عمر: {0}"), FormatDays(age)));
                    item.Evidence.Add(LocalizationService.Get("نصب‌کننده پس از نصب معمولاً بی‌استفاده می‌ماند"));
                    candidates.Add(item);
                    continue;
                }

                // —— ۵) فایل خیلی حجیم و قدیمیِ غیر رسانه‌ای (فقط مرزی — داوری چشم کاربر)
                if (fi.Length >= 512L * 1024 * 1024 && age.TotalDays >= 120 && !isImage && !isVideo && !isDoc)
                {
                    var item = NewCase(fi, ExpeditionBucket.LargeOldFile, ExpeditionConfidence.Borderline);
                    item.Evidence.Add(string.Format(LocalizationService.Get("حجم {0}"), SystemStats.FormatBytes(fi.Length)));
                    item.Evidence.Add(string.Format(LocalizationService.Get("عمر: {0}"), FormatDays(age)));
                    item.Evidence.Add(LocalizationService.Get("هیچ الگوی کپی یا موقتی ندارد؛ داوری با شماست"));
                    candidates.Add(item);
                    continue;
                }

                // —— ۶) فایل خالیِ قدیمی
                if (fi.Length == 0 && age.TotalDays >= 14)
                {
                    var item = NewCase(fi, ExpeditionBucket.TempTrash, ExpeditionConfidence.Likely);
                    item.Evidence.Add(LocalizationService.Get("حجم صفر (خالی) و قدیمی"));
                    item.Evidence.Add(string.Format(LocalizationService.Get("عمر: {0}"), FormatDays(age)));
                    candidates.Add(item);
                    continue;
                }

                // —— محیطی: چند مورد واقعی برای اینکه پوشه در بازی زنده به‌نظر برسد
                if (ambient.Count < MaxAmbient && scanned <= 1200)
                {
                    ambient.Add(new ExpeditionAmbientItem
                    {
                        Name = fi.Name,
                        SizeBytes = fi.Length,
                        LastWriteUtc = fi.LastWriteTimeUtc,
                        IsImage = isImage,
                        IsVideo = isVideo
                    });
                }
            }

            // ترتیب نهایی: پرونده‌های قطعی‌تر اول، سپس بر اساس نام
            var ordered = candidates
                .OrderByDescending(c => (int)c.Confidence)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxCases)
                .ToList();

            zone.Cases.AddRange(ordered);
            zone.Ambient.AddRange(ambient
                .OrderByDescending(a => a.LastWriteUtc)
                .Take(MaxAmbient));
            zone.ScannedCount = scanned;
            return zone;
        }

        private static ExpeditionCaseItem NewCase(FileInfo fi, ExpeditionBucket bucket, ExpeditionConfidence conf)
        {
            return new ExpeditionCaseItem
            {
                Path = fi.FullName,
                Name = fi.Name,
                SizeBytes = fi.Length,
                LastWriteUtc = fi.LastWriteTimeUtc,
                Bucket = bucket,
                Confidence = conf
            };
        }

        private static string FormatDays(TimeSpan age)
        {
            if (age.TotalDays >= 365) return $"{(int)(age.Days / 365)} {LocalizationService.Get("سال")}";
            if (age.TotalDays >= 90) return $"{(int)(age.Days / 30)} {LocalizationService.Get("ماه")}";
            if (age.TotalDays >= 14) return $"{(int)(age.Days / 7)} {LocalizationService.Get("هفته")}";
            return $"{(int)age.TotalDays} {LocalizationService.Get("روز")}";
        }

        // ====================================================================
        // اجرای نهایی (فقط پس از تأیید پایان گشت)
        // ====================================================================

        /// <summary>
        /// اجرای پرونده‌ی نهایی گشت: حذف امن.
        /// گارد مسیر: فقط مسیرهای داخل Downloads — بیرون از Zone هرگز رد نمی‌شود.
        /// </summary>
        public static Task<SafeDeleteBatchResult> ExecuteZoneBatchAsync(IEnumerable<string> paths, SafeDeleteMode? mode = null)
        {
            var emptyResult = new SafeDeleteBatchResult();
            var root = DownloadsDir;
            if (string.IsNullOrEmpty(root))
            {
                emptyResult.Errors.Add(LocalizationService.Get("مسیر دانلودها در دسترس نیست"));
                return Task.FromResult(emptyResult);
            }

            var list = new List<string>();
            foreach (var p in paths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                string full;
                try { full = Path.GetFullPath(p); }
                catch (Exception) { continue; }

                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // هرگز بیرون Zone
                if (full.Equals(root, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(full) || Directory.Exists(full)) list.Add(full);
            }

            return Task.Run(() => SafeDeleteService.DeleteFiles(list, mode ?? SafeDeleteService.DefaultMode));
        }
    }
}
