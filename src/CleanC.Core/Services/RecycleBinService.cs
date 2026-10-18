// src/CleanC.Core/Services/RecycleBinService.cs
// مدیریت سطل بازیافت با پیش نمایش آیتم به آیتم.
//
// ویندوز برای هر شیءِ دور ریخته شده دو فایل می سازد:
//   $I<random> : فایل اطلاعات (مسیر اصلی، حجم، زمان حذف)
//   $R<random> : خودِ داده (فایل یا پوشه)
// پارس کردن $I (بدون COM و بدون دیالوگ سیستم) هم قابل تست است
// و هم اجازه می دهد حذف/بازگردانی کاملاً کنترل شده انجام شود.
//
// فرمت $I:
//   نسخه ۱ (ویندوز XP):  0..8 نسخه | 8..528 مسیر (۲۶۰ کاراکتر UTF-16) | 528..536 حجم (int64)
//   نسخه ۲ (ویستا+):    0..8 نسخه | 8..16 حجم (int64) | 16..24 زمان FILETIME | 24..28 طول مسیر (کاراکتر) | 28.. مسیر (UTF-16)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;

namespace CleanC.Core.Services
{
    public class RecycleBinItem
    {
        /// <summary>مسیر کامل فایل $I؛ به عنوان شناسه ی یکتا در UI استفاده می شود.</summary>
        public string Id { get; set; } = string.Empty;
        public string InfoPath { get; set; } = string.Empty;
        public string PayloadPath { get; set; } = string.Empty;
        /// <summary>مسیر اصلی شیء پیش از حذف.</summary>
        public string OriginalPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime DeletedAt { get; set; }
        public bool IsDirectory { get; set; }
        /// <summary>آیا $R هنوز روی دیسک وجود دارد (شیء سالم برای بازگردانی).</summary>
        public bool PayloadExists { get; set; }
        public string FormattedSize => SystemStats.FormatBytes(SizeBytes);
    }

    public static class RecycleBinService
    {
        /// <summary>سقف پیمایش هنگام محاسبه ی حجم پوشه ها تا لود صفحه طولانی نشود.</summary>
        private const int DirectoryWalkEntryCap = 50_000;

        // ── پارس فایل اطلاعاتی ─────────────────────────────────────────────

        /// <summary>پارس بافر فایل $I. اگر ساختار نامعتبر باشد null برمی گرداند.</summary>
        public static RecycleBinItem? ParseInfoFile(byte[]? buffer)
        {
            if (buffer == null || buffer.Length < 16) return null;

            long version = BitConverter.ToInt64(buffer, 0);
            long size = 0;
            DateTime deletedAt = DateTime.MinValue;
            string path;

            if (version == 1)
            {
                if (buffer.Length < 536) return null;
                path = ReadUnicode(buffer, 8, 520);
                size = BitConverter.ToInt64(buffer, 528);
            }
            else if (version == 2)
            {
                if (buffer.Length < 28) return null;
                size = BitConverter.ToInt64(buffer, 8);
                long fileTime = BitConverter.ToInt64(buffer, 16);
                int chars = BitConverter.ToInt32(buffer, 24);

                if (chars <= 0 || chars > 32768 || 28L + chars * 2L > buffer.Length) return null;
                path = ReadUnicode(buffer, 28, chars * 2);

                if (fileTime > 0)
                {
                    try { deletedAt = DateTime.FromFileTime(fileTime); }
                    catch (Exception ex) { CcLog.Ignore(ex, "RecycleBin.ParseInfoFile.Date"); }
                }
            }
            else
            {
                // نسخه ی ناشناخته (احتمال خرابی داده)
                return null;
            }

            path = path.Trim().TrimEnd('\0');
            if (string.IsNullOrEmpty(path)) return null;

            return new RecycleBinItem
            {
                OriginalPath = path,
                FileName = GetFileName(path),
                SizeBytes = size < 0 ? 0 : size,
                DeletedAt = deletedAt
            };
        }

        private static string ReadUnicode(byte[] buffer, int offset, int byteCount)
        {
            if (offset < 0 || byteCount <= 0 || offset + byteCount > buffer.Length) return string.Empty;
            return Encoding.Unicode.GetString(buffer, offset, byteCount);
        }

        /// <summary>نام آخرین بخش مسیر؛ برای پوشه های ریشه ای مقادیر خالی نمی دهد.</summary>
        public static string GetFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string trimmed = path.TrimEnd('\\', '/');
            int i = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            string name = i >= 0 ? trimmed.Substring(i + 1) : trimmed;
            return string.IsNullOrEmpty(name) ? path : name;
        }

        // ── فهرست سازی ─────────────────────────────────────────────────────

        private static IEnumerable<string> GetRecycleBinRoots(string sid)
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                string root;
                try { root = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid); }
                catch (Exception ex) { CcLog.Ignore(ex, "RecycleBin.RootPath"); continue; }

                bool exists;
                try { exists = Directory.Exists(root); }
                catch (Exception ex) { CcLog.Ignore(ex, "RecycleBin.RootExists"); continue; }

                if (exists) yield return root;
            }
        }

        private static string GetCurrentSid()
        {
            try { return WindowsIdentity.GetCurrent().User?.Value ?? string.Empty; }
            catch (Exception ex) { CcLog.Ignore(ex, "RecycleBin.Sid"); return string.Empty; }
        }

        /// <summary>همه ی اشیاء سطل بازیافت کاربر جاری را برمی گرداند (مرتب شده بر اساس تازگی حذف).</summary>
        public static List<RecycleBinItem> ListItems()
        {
            var result = new List<RecycleBinItem>();
            string sid = GetCurrentSid();
            if (string.IsNullOrEmpty(sid)) return result;

            foreach (string root in GetRecycleBinRoots(sid))
            {
                try
                {
                    foreach (string infoPath in Directory.EnumerateFiles(root, "$I*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            string name = Path.GetFileName(infoPath);
                            if (name.Length < 3) continue;

                            var item = ParseInfoFile(File.ReadAllBytes(infoPath));
                            if (item == null) continue;

                            item.Id = infoPath;
                            item.InfoPath = infoPath;
                            item.PayloadPath = Path.Combine(root, "$R" + name.Substring(2));
                            item.IsDirectory = Directory.Exists(item.PayloadPath);
                            item.PayloadExists = item.IsDirectory || File.Exists(item.PayloadPath);

                            // حجم پوشه معمولاً در $I صفر ثبت می شود؛ در صورت نبود، خودمان می سنجیم
                            if (item.IsDirectory && item.SizeBytes == 0)
                                item.SizeBytes = ComputeDirectorySize(item.PayloadPath);

                            result.Add(item);
                        }
                        catch (Exception ex)
                        {
                            CcLog.Ignore(ex, "RecycleBin.ListItem");
                        }
                    }
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex, "RecycleBin.ListItems");
                }
            }

            return result.OrderByDescending(i => i.DeletedAt).ToList();
        }

        /// <summary>حجم واقعی پوشه با سقف پیمایش؛ در صورت برخورد به سقف، همان مقدار جزئی برمی گردد.</summary>
        public static long ComputeDirectorySize(string path)
        {
            long total = 0;
            int visited = 0;
            var pending = new Stack<string>();
            pending.Push(path);

            while (pending.Count > 0 && visited < DirectoryWalkEntryCap)
            {
                string current = pending.Pop();
                try
                {
                    foreach (string file in Directory.EnumerateFiles(current))
                    {
                        visited++;
                        try { total += new FileInfo(file).Length; }
                        catch (Exception ex) { CcLog.Ignore(ex, "RecycleBin.FileSize"); }
                        if (visited >= DirectoryWalkEntryCap) break;
                    }

                    foreach (string dir in Directory.EnumerateDirectories(current))
                    {
                        pending.Push(dir);
                        if (visited >= DirectoryWalkEntryCap) break;
                    }
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex, "RecycleBin.ComputeDirectorySize");
                }
            }

            return total;
        }

        // ── عملیات ────────────────────────────────────────────────────────

        /// <summary>
        /// بازگردانی شیء به مسیر اصلی. اگر در مقصد نام تکراری وجود داشته باشد،
        /// نام یکتا ساخته می شود؛ پوشه ی مقصد در صورت نبود ساخته می شود.
        /// </summary>
        public static bool Restore(RecycleBinItem item, out string error)
        {
            error = string.Empty;
            if (item == null) { error = LocalizationService.Get("شیء انتخاب نشده است."); return false; }
            if (!item.PayloadExists) { error = LocalizationService.Get("داده ی شیء دیگر در سطل موجود نیست."); return false; }

            try
            {
                string dest = item.OriginalPath;
                string? destDir = Path.GetDirectoryName(dest);
                if (string.IsNullOrWhiteSpace(destDir)) { error = LocalizationService.Get("مسیر مقصد نامعتبر است."); return false; }

                Directory.CreateDirectory(destDir);

                if (File.Exists(dest) || Directory.Exists(dest))
                    dest = MakeUniquePath(dest);

                if (item.IsDirectory)
                {
                    try
                    {
                        Directory.Move(item.PayloadPath, dest);
                    }
                    catch (IOException)
                    {
                        // حجم/قفل/تفاوت درایو → کپی کامل و سپس حذف منبع
                        CopyDirectory(item.PayloadPath, dest);
                        Directory.Delete(item.PayloadPath, true);
                    }
                }
                else
                {
                    File.Move(item.PayloadPath, dest);
                }

                TryDeleteFile(item.InfoPath);
                return true;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "RecycleBin.Restore");
                error = ex.Message;
                return false;
            }
        }

        /// <summary>حذف قطعی یک شیء (داده + فایل اطلاعات).</summary>
        public static bool Delete(RecycleBinItem item, out string error)
        {
            error = string.Empty;
            if (item == null) { error = LocalizationService.Get("شیء انتخاب نشده است."); return false; }

            try
            {
                if (Directory.Exists(item.PayloadPath)) Directory.Delete(item.PayloadPath, true);
                else if (File.Exists(item.PayloadPath)) File.Delete(item.PayloadPath);

                TryDeleteFile(item.InfoPath);
                return true;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "RecycleBin.Delete");
                error = ex.Message;
                return false;
            }
        }

        /// <summary>خالی کردن کامل سطل؛ تعداد اشیاء حذف شده را برمی گرداند.</summary>
        public static int EmptyAll(out string error)
        {
            error = string.Empty;
            int removed = 0;
            var items = ListItems();

            foreach (var item in items)
            {
                if (Delete(item, out string itemError))
                {
                    removed++;
                }
                else if (!string.IsNullOrEmpty(itemError))
                {
                    error = itemError;
                }
            }

            return removed;
        }

        /// <summary>
        /// ساخت مسیر تکراری برای بازگردانی بدون بازنویسی شیءِ موجود:
        /// name.txt → name (restored).txt → name (restored 2).txt
        /// </summary>
        public static string MakeUniquePath(string path)
        {
            // اگر مقصد خالی است، همان نام اصلی برگردانده می شود
            if (!File.Exists(path) && !Directory.Exists(path)) return path;

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

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);

            foreach (string file in Directory.EnumerateFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);

            foreach (string sub in Directory.EnumerateDirectories(source))
                CopyDirectory(sub, Path.Combine(dest, Path.GetFileName(sub)));
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "RecycleBin.TryDeleteFile");
            }
        }
    }
}
