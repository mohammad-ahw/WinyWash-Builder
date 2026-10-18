// src/CleanC.Core/Services/BootImpactService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using CleanC.Core.Diagnostics;

namespace CleanC.Core.Services
{
    /// <summary>
    /// اثر اندازه گیری شده ی یک برنامه روی بالا آمدن ویندوز.
    /// از رویداد <c>EventID=101</c> کانال <c>Microsoft-Windows-Diagnostics-Performance/Operational</c>
    /// استخراج می شود؛ ویندوز برای هر برنامه ای که بوت را کند کرده باشد این رویداد را ثبت می کند.
    /// </summary>
    public sealed class BootImpact
    {
        public string FileName { get; set; } = string.Empty;
        public long TotalMs { get; set; }
        public long DegradationMs { get; set; }
        public int Samples { get; set; }
    }

    /// <summary>
    /// «امتیاز تأثیر» هر آیتم استارت آپ را می سازد.
    ///
    /// دو منبع دارد:
    ///  1. <b>اندازه گیری شده</b> — رویداد ۱۰۱ِ Diagnostics-Performance؛ اگر موجود باشد
    ///     <see cref="StartupItem.ImpactMeasured"/> می شود و <see cref="StartupItem.ImpactMs"/>
    ///     برابرِ تأخیر واقعی بوت آن برنامه است.
    ///  2. <b>تخمین</b> — اگر رویدادی برای آن فایل نباشد، از دسته بندی (Category) تخمین زده می شود.
    ///
    /// معنای امتیاز: «این آیتم چقدر روی بالا آمدن ویندوز اثر دارد؟»
    /// مقیاسِ مشترکِ هر دو منبع «تأخیر/۱۰۰» است:
    ///   ۱–۳۴ کم  •  ۳۵–۶۴ متوسط  •  ۶۵–۱۰۰ زیاد
    /// پس مثلاً امتیاز ۷۵ یا از تأخیرِ ۷٫۵ ثانیه ای می آید یا از دسته بندیِ پرمصرف.
    /// </summary>
    public static class BootImpactService
    {
        private const string ChannelName = "Microsoft-Windows-Diagnostics-Performance/Operational";
        private const string QueryXml = "*[System[(EventID=101)]]";
        private const int MaxEvents = 1500;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

        private static readonly object CacheLock = new();
        private static Dictionary<string, BootImpact>? _cache;
        private static DateTime _cacheAt = DateTime.MinValue;

        // مثال واقعی رویداد:
        //   File Name: \Device\HarddiskVolume3\Windows\System32\NahimicService.exe
        //   Total Time: 925 ms
        //   Degradation Time: 443 ms
        private static readonly Regex FileLine = new(@"File Name\s*:\s*(.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TotalLine = new(@"Total Time\s*:\s*(\d+)\s*(?:msec|ms)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DegradationLine = new(@"Degradation Time\s*:\s*(\d+)\s*(?:msec|ms)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// همه ی رویدادهای اخیر را می خواند (کش شده برای ۱۵ دقیقه).
        /// اگر دسترسی به کانال نبود یا خطا داد، دیکشنری خالی برمی گرداند
        /// تا فراخواننده به حالت «تخمین» برگردد.
        /// </summary>
        public static IReadOnlyDictionary<string, BootImpact> GetBootImpacts()
        {
            lock (CacheLock)
            {
                if (_cache != null && DateTime.UtcNow - _cacheAt < CacheTtl) return _cache;

                var result = new Dictionary<string, BootImpact>(StringComparer.OrdinalIgnoreCase);
                try { ReadEvents(result); }
                catch (Exception ex) { CcLog.Ignore(ex, "BootImpact.Read"); }

                _cache = result;
                _cacheAt = DateTime.UtcNow;
                return _cache;
            }
        }

        private static void ReadEvents(Dictionary<string, BootImpact> sink)
        {
            var query = new EventLogQuery(ChannelName, PathType.LogName, QueryXml);
            using var reader = new EventLogReader(query);

            int read = 0;
            while (read < MaxEvents)
            {
                EventRecord? record;
                try { record = reader.ReadEvent(); }
                catch (EventLogException ex) { CcLog.Ignore(ex, "BootImpact.ReadEvent"); break; }

                if (record == null) break;
                read++;

                using (record)
                {
                    string description;
                    try { description = record.FormatDescription() ?? string.Empty; }
                    catch { CcLog.Ignore(null, "BootImpact.FormatDescription"); continue; }

                    if (description.Length == 0) continue;

                    string fileName = FileLine.Match(description).Groups[1].Value.Trim();
                    if (fileName.Length == 0) continue;

                    long total = ParseMs(TotalLine.Match(description));
                    long degradation = ParseMs(DegradationLine.Match(description));
                    if (total <= 0 && degradation <= 0) continue;

                    string key = NormalizeFileName(fileName);
                    if (key.Length == 0) continue;

                    if (sink.TryGetValue(key, out var existing))
                    {
                        // چندین بوت = همان برنامه؛ بیشترین تأخیر را نگه می داریم
                        existing.Samples++;
                        if (total > existing.TotalMs) existing.TotalMs = total;
                        if (degradation > existing.DegradationMs) existing.DegradationMs = degradation;
                    }
                    else
                    {
                        sink[key] = new BootImpact
                        {
                            FileName = key,
                            TotalMs = total,
                            DegradationMs = degradation,
                            Samples = 1
                        };
                    }
                }
            }
        }

        private static long ParseMs(Match m)
        {
            if (!m.Success) return 0;
            return long.TryParse(m.Groups[1].Value, out long v) ? v : 0;
        }

        /// <summary>
        /// از مسیرِ رویداد فقط نامِ فایل را نگه می داریم تا با هر شکلِ مسیرِ آیتمِ استارت آپ بخواند.
        /// </summary>
        public static string NormalizeFileName(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName)) return string.Empty;
            string s = pathOrName.Trim().Trim('"');

            // ممکن است فرمان شامل آرگومان باشد:
            //   "C:\Program Files\Foo\bar.exe" --silent
            // در آن صورت فقط خودِ فایلِ اجراپذیر را نگه می داریم.
            int exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe < 0) exe = s.IndexOf(".com", StringComparison.OrdinalIgnoreCase);
            if (exe >= 0) s = s.Substring(0, exe + 4);

            // \Device\HarddiskVolume3\Windows\...  یا  C:\Windows\...
            int idx = Math.Max(s.LastIndexOf('\\'), s.LastIndexOf('/'));
            if (idx >= 0 && idx < s.Length - 1) s = s.Substring(idx + 1);

            return s.Trim();
        }

        /// <summary>
        /// امتیاز و برچسب تأثیر را روی آیتم می گذارد. هیچ وقت استثنا پرتاب نمی کند.
        /// </summary>
        public static void AssignImpact(StartupItem item, IReadOnlyDictionary<string, BootImpact> impacts)
        {
            if (item == null) return;

            long measuredMs = 0;
            if (impacts != null && !string.IsNullOrWhiteSpace(item.Command))
            {
                string key = NormalizeFileName(item.Command);
                if (key.Length != 0 && impacts.TryGetValue(key, out var hit))
                {
                    item.ImpactMeasured = true;
                    // Degradation Time = تأخیرِ اضافه شده (دقیقاً همان چیزی که غیرفعال کردن حذفش می کند)
                    measuredMs = hit.DegradationMs > 0 ? hit.DegradationMs : hit.TotalMs;
                    item.ImpactMs = measuredMs;
                }
            }

            int score;
            if (item.ImpactMeasured)
            {
                // هر ۱۰۰ms تأخیر ≈ ۱ واحد امتیاز. این بازه طوری انتخاب شده که
                // با آستانه های برچسب هم خوان باشد:
                //   < 3500ms  → کم
                //   3500–6500ms → متوسط
                //   > 6500ms  → زیاد
                score = (int)(measuredMs / 100);
            }
            else
            {
                score = EstimateFromCategory(item.Category);
            }

            item.ImpactScore = Math.Clamp(score, 1, 100);
            item.ImpactLabel = item.ImpactScore switch
            {
                >= 65 => LocalizationService.Get("زیاد"),
                >= 35 => LocalizationService.Get("متوسط"),
                _ => LocalizationService.Get("کم")
            };
        }

        /// <summary>
        /// تخمین امتیاز بر اساس دسته بندی، وقتی رویدادی برای این فایل ثبت نشده است.
        /// مقادیر عمداً روی همان مقیاسِ «تأخیر/۱۰۰» تنظیم شده اند
        /// (مثلاً ۷۵ یعنی «معادل ۷٫۵ ثانیه تأخیر بوت»).
        /// </summary>
        private static int EstimateFromCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return 40;

            string c = category;
            if (c.Contains("ضروری", StringComparison.Ordinal)) return 85;           // سخت افزار و سیستم // i18n-logic: این متن با داده‌ی خارجی مقایسه/تطبیق داده م‌یشود
            if (c.Contains("گیمینگ", StringComparison.Ordinal)) return 75;          // لانچر بازی // i18n-logic: این متن با داده‌ی خارجی مقایسه/تطبیق داده م‌یشود
            if (c.Contains("پیام رسان", StringComparison.Ordinal)) return 60; // i18n-logic: این متن با داده‌ی خارجی مقایسه/تطبیق داده م‌یشود
            if (c.Contains("دانلود", StringComparison.Ordinal) || c.Contains("ابری", StringComparison.Ordinal)) return 50; // i18n-logic: این متن با داده‌ی خارجی مقایسه/تطبیق داده م‌یشود
            if (c.Contains("VPN", StringComparison.OrdinalIgnoreCase)) return 15;
            if (c.Contains("آپدیتر", StringComparison.Ordinal)) return 25; // i18n-logic: این متن با داده‌ی خارجی مقایسه/تطبیق داده م‌یشود
            if (c.Contains("MSIX", StringComparison.OrdinalIgnoreCase)) return 30;
            return 40; // برنامه کاربردی (اختیاری)
        }
    }
}
