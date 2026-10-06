// src/CleanC.Core/Services/SafeDeleteService.cs
// F3 — لایه ی حذف ایمن و قابل بازگردانی.
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
        Failed
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
            SafeDeleteMode.Quarantine => "در قرنطینه ی CleanC ذخیره خواهد شد (قابل بازگردانی از مرکز بازیابی)",
            SafeDeleteMode.Permanent => "برای همیشه حذف خواهد شد (قابل بازگردانی نیست)",
            _ => "به سطل بازیافت منتقل خواهد شد"
        };

        /// <summary>
        /// مسیر ریشه ی قرنطینه — برای تست قابل تغییر است.
        /// عوض شدن ریشه یعنی نشستِ باز باید بسته شود تا ورودی ها در ریشه ی جدید ثبت شوند.
        /// </summary>
        public static string QuarantineRoot
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_quarantineRootOverride)) return _quarantineRootOverride!;
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CleanC", "quarantine");
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
            if (batch == null) return "هیچ موردی حذف نشد.";

            var parts = new List<string>();
            if (batch.RecycledCount > 0) parts.Add($"{batch.RecycledCount} مورد به سطل بازیافت منتقل شد");
            if (batch.QuarantinedCount > 0) parts.Add($"{batch.QuarantinedCount} مورد در قرنطینه ذخیره شد");
            if (batch.PermanentCount > 0) parts.Add($"{batch.PermanentCount} مورد حذف قطعی شد");

            string message = parts.Count > 0 ? string.Join("، ", parts) : "هیچ موردی حذف نشد";

            if (batch.NotFoundCount > 0) message += $"؛ {batch.NotFoundCount} مورد از قبل وجود نداشت";
            if (batch.FailedCount > 0) message += $"؛ {batch.FailedCount} مورد ناموفق";

            if (batch.FreedBytes > 0)
                message += $" — {Models.SystemStats.FormatBytes(batch.FreedBytes)} فضا آزاد گردید";
            else if (batch.MovedBytes > 0)
                message += $" — {Models.SystemStats.FormatBytes(batch.MovedBytes)} جابه جا شد (تا خالی کردن سطل آزاد نمی شود)";

            return message;
        }

        // ── حذف فایل/پوشه ی شخصی ─────────────────────────────────────────

        public static SafeDeleteResult DeleteFile(string path, SafeDeleteMode? mode = null)
            => DeleteCore(path, mode ?? DefaultMode, isDirectory: false);

        public static SafeDeleteResult DeleteDirectory(string path, SafeDeleteMode? mode = null)
            => DeleteCore(path, mode ?? DefaultMode, isDirectory: true);

        /// <summary>حذف دسته ای فایل شخصی؛ خلاصه ی آماری برای گزارش UI.</summary>
        public static SafeDeleteBatchResult DeleteFiles(IEnumerable<string> paths, SafeDeleteMode? mode = null)
            => DeleteMany(paths, mode, isDirectory: false);

        public static SafeDeleteBatchResult DeleteDirectories(IEnumerable<string> paths, SafeDeleteMode? mode = null)
            => DeleteMany(paths, mode, isDirectory: true);

        /// <summary>
        /// حذف زباله/کش — همیشه قطعی، مستقل از <see cref="DefaultMode"/>.
        /// هدف این دسته ها آزادسازی فضاست؛ فرستادنشان به سطل فقط فضا را معطل می کند.
        /// </summary>
        public static SafeDeleteResult DeleteJunkFile(string path)
            => DeleteCore(path, SafeDeleteMode.Permanent, isDirectory: false);

        public static SafeDeleteResult DeleteJunkDirectory(string path)
            => DeleteCore(path, SafeDeleteMode.Permanent, isDirectory: true);

        private static SafeDeleteBatchResult DeleteMany(IEnumerable<string> paths, SafeDeleteMode? mode, bool isDirectory)
        {
            var batch = new SafeDeleteBatchResult();
            if (paths == null) return batch;

            foreach (string path in paths)
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
                    default:
                        batch.FailedCount++;
                        if (!string.IsNullOrWhiteSpace(result.Error)) batch.Errors.Add($"{path}: {result.Error}");
                        break;
                }
            }

            return batch;
        }

        private static SafeDeleteResult DeleteCore(string path, SafeDeleteMode mode, bool isDirectory)
        {
            var result = new SafeDeleteResult { OriginalPath = path ?? string.Empty };

            if (string.IsNullOrWhiteSpace(path)) { result.Outcome = SafeDeleteOutcome.NotFound; return result; }

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
                        if (TryQuarantine(path, isDirectory, out string storedPath, out string quarantineError))
                        {
                            result.Outcome = SafeDeleteOutcome.Quarantined;
                            result.StoredPath = storedPath;
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
                        // سطل در دسترس نیست → قرنطینه تا داده از بین نرود
                        if (TryQuarantine(path, isDirectory, out string stored, out string qError))
                        {
                            result.Outcome = SafeDeleteOutcome.Quarantined;
                            result.StoredPath = stored;
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

        private static bool TryRecycleBin(string path, bool isDirectory, out string error)
        {
            error = string.Empty;
            try
            {
                ClearBlockingAttributes(path, isDirectory);

                if (isDirectory)
                    FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                else
                    FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

                return !Exists(path, isDirectory);
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
            UnauthorizedAccessException => "دسترسی کافی نیست",
            DirectoryNotFoundException => "مسیر پیدا نشد",
            FileNotFoundException => "فایل پیدا نشد",
            IOException io when io.Message.Contains("recycle", StringComparison.OrdinalIgnoreCase) => "سطل بازیافت در دسترس نیست",
            IOException => "فایل توسط برنامه ی دیگری قفل است",
            _ => ex.Message
        };

        // ── قرنطینه ───────────────────────────────────────────────────────

        private static bool TryQuarantine(string path, bool isDirectory, out string storedPath, out string error)
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
                    MoveWithFallback(path, target, isDirectory);
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
                    return true;
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SafeDeleteService.Quarantine");
                error = FriendlyError(ex);
                return false;
            }
        }

        private static void MoveWithFallback(string source, string target, bool isDirectory)
        {
            try
            {
                if (isDirectory) Directory.Move(source, target);
                else File.Move(source, target);
            }
            catch (IOException)
            {
                // درایو متفاوت یا قفل موقت → کپی کامل و سپس حذف منبع
                if (isDirectory) CopyDirectory(source, target);
                else File.Copy(source, target, overwrite: false);

                if (isDirectory) Directory.Delete(source, true);
                else
                {
                    var fi = new FileInfo(source);
                    if ((fi.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                        fi.Attributes = FileAttributes.Normal;
                    fi.Delete();
                }
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);

            foreach (string file in Directory.EnumerateFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);

            foreach (string sub in Directory.EnumerateDirectories(source))
                CopyDirectory(sub, Path.Combine(dest, Path.GetFileName(sub)));
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

            if (manifest == null) return (0, 0, "نشست قرنطینه پیدا نشد.");

            foreach (var entry in manifest.Entries)
            {
                try
                {
                    if (!Exists(entry.StoredPath, entry.IsDirectory)) { failed++; continue; }

                    string dest = entry.OriginalPath;
                    string? destDir = Path.GetDirectoryName(dest);
                    if (string.IsNullOrWhiteSpace(destDir)) { failed++; lastError = "مسیر مقصد نامعتبر است."; continue; }

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
