using System;
using System.Diagnostics;
using CleanC.Core.Diagnostics;

namespace CleanC.Core.Native
{
    /// <summary>
    /// الگوی مشترک اجرای پردازه‌های خارجی (PowerShell/DISM/wsl/diskpart و ...).
    /// </summary>
    public static class ProcessRunner
    {
        /// <summary>
        /// انتظار یکپارچه و امن:
        /// 1) حداکثر <paramref name="timeoutMs"/> صبر می‌کند؛ اگر پردازه تمام نشد، می‌کُشد
        ///    تا خواندن خروجی (پایپ) و آزادسازی منابع هرگز قفل نشود.
        /// 2) رفتار خروجی: true یعنی پردازه واقعاً تمام شد؛ exitCode فقط در این حالت معتبر است.
        /// 3) false یعنی تایم‌اوت/کشیده‌شده — فراخواننده باید «موفق نشد» را گزارش کند نه نجوماً موفقیت.
        /// </summary>
        public static bool WaitAndKillOnTimeout(Process? p, int timeoutMs, out int exitCode)
        {
            exitCode = -1;
            if (p == null) return false;

            bool exited = false;
            try
            {
                exited = p.WaitForExit(timeoutMs);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            if (!exited)
            {
                // تایم‌اوت: پردازه را می‌کُشیم تا پایپ خروجی بسته شود و ریسوم عملیات نصفه
                // (مثل متراکم‌سازی دیسک مجازی) منبع را برای همیشه مشغول نکند
                try { p.Kill(); } catch { /* دسترسی/قبلاً خارج شده */ }
                try { p.WaitForExit(3000); } catch { }
            }
            else
            {
                // درصورت خروج طبیعی، نوتیفیکیشن‌های ناهمگام (دریافت کامل خروجی) هم سنکرون شوند
                try { p.WaitForExit(); } catch { }
            }

            try { exitCode = p.ExitCode; } catch { }
            return exited;
        }
    }
}
