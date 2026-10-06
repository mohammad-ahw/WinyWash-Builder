// O:\OpenCode\AntiGravity\CleanC - NEW\src\CleanC.Core\Diagnostics\CcLog.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace CleanC.Core.Diagnostics
{
    /// <summary>
    /// لاگ مرکزی برای خطاهایی که عمداً نادیده گرفته می شوند (best-effort).
    ///
    /// چرا لازم است: تا پیش از این ۲۱۶ تا <c>catch { }</c> خالی در پروژه بود و هیچ کس
    /// نمی فهمید عملیاتِ شکست خورده کدام بود. حالا هر خطای نادیده گرفته شده:
    ///   ۱) در یک حلقه ی حافظه (۲۵۶ ردیف آخر) نگه داشته می شود،
    ///   ۲) با حداکثر ۲ خط در ثانیه در %LOCALAPPDATA%\CleanC\logs\ignored.log می نویسد.
    ///
    /// سقفِ نرخ عمداً پایین است: بعضی از این catchها داخل حلقه های اسکن فایل اجرا
    /// می شوند و نباید به خاطر لاگ، روی سرعت پاکسازی اثر بگذارند.
    /// </summary>
    public static class CcLog
    {
        private const int MaxMemoryEntries = 256;
        private const int FlushIntervalMs = 500;

        private static readonly object _gate = new();
        private static readonly Queue<string> _recent = new();
        private static readonly Queue<string> _pending = new();

        private static long _total;
        private static long _lastFlushTick;

        /// <summary>
        /// پوشه ی خروجی. پیش فرض %LOCALAPPDATA%\CleanC\logs است و فقط برای تست ها
        /// قابل تغییر است تا اجرای تست ها روی داده ی واقعی کاربر اثر نگذارد.
        /// </summary>
        public static string LogDirectory { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanC", "logs");

        /// <summary>تعداد کل خطاهای نادیده گرفته شده در این اجرا.</summary>
        public static long TotalIgnored => Interlocked.Read(ref _total);

        /// <summary>
        /// ورودی استانداردِ داخل <c>catch</c>. هرگز استثنا پرتاب نمی کند.
        /// </summary>
        public static void Ignore(Exception? ex, string? context = null)
        {
            if (ex == null) return;
            Report(context ?? "ignored", ex);
        }

        /// <summary>
        /// ثبت یک خطا. هرگز استثنا پرتاب نمی کند تا لاگ کردن خودش باعث crash نشود.
        /// </summary>
        public static void Report(string context, Exception ex)
        {
            try
            {
                long total = Interlocked.Increment(ref _total);
                long now = Environment.TickCount64;

                // تصمیم بگیر که آیا اصلاً ارزش ساختن رشته (alloc) را دارد یا نه.
                bool capture;
                lock (_gate)
                {
                    capture = _recent.Count < MaxMemoryEntries ||
                              now - _lastFlushTick >= FlushIntervalMs;
                }

                if (!capture) return;

                // تاریخ باید میلادی/غیرفرهنگی باشد؛ روی سیستم های fa-IR قالب پیش فرض
                // از تقویم جلالی استفاده می کند و لاگ را به «۱۴۰۵-۰۷-۱۳» تبدیل می کند
                // که خواندنش در یک فایل عیب یابی گیج کننده است.
                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                string line = $"[{stamp}] #{total} {context}: " +
                              $"{ex.GetType().Name}: {ex.Message}";

                bool shouldFlush;
                lock (_gate)
                {
                    if (_recent.Count >= MaxMemoryEntries) _recent.Dequeue();
                    _recent.Enqueue(line);

                    _pending.Enqueue(line);
                    shouldFlush = now - _lastFlushTick >= FlushIntervalMs;
                    if (shouldFlush) _lastFlushTick = now;
                }

                if (shouldFlush) FlushCore();
            }
            catch
            {
                // لاگ کردن هرگز نباید خودش باعث crash شود
            }
        }

        /// <summary>نوشتن هر چیزِ مانده روی دیکس. بی خطر و بدون پرتاب استثنا.</summary>
        public static void Flush()
        {
            try { FlushCore(); }
            catch
            {
                // لاگ کردن هرگز نباید خودش باعث crash شود
            }
        }

        /// <summary>۲۵۶ خط آخرِ ثبت شده در این اجرا (برای دیباگ و صفحه ی عیب یابی).</summary>
        public static IReadOnlyList<string> Recent()
        {
            lock (_gate) return _recent.ToArray();
        }

        private static void FlushCore()
        {
            string[] lines;
            lock (_gate)
            {
                if (_pending.Count == 0) return;
                lines = _pending.ToArray();
                _pending.Clear();
            }

            string dir = LogDirectory;
            Directory.CreateDirectory(dir);
            string logPath = Path.Combine(dir, "ignored.log");

            // حجم لاگ کنترل می شود تا پوشه CleanC به مرور زمان سنگین نشود (حداکثر ۱.۵ مگابایت)
            try
            {
                var fi = new FileInfo(logPath);
                if (fi.Exists && fi.Length > 1_500_000)
                {
                    var existing = File.ReadAllLines(logPath);
                    var retained = existing.Skip(Math.Max(0, existing.Length - 500));
                    File.WriteAllLines(logPath, retained);
                }
            }
            catch
            {
                // نادیده گرفتن خطای چرخش لاگ
            }

            File.AppendAllLines(logPath, lines);
        }
    }
}
