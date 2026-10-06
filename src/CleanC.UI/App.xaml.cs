// O:\OpenCode\AntiGravity\CleanC - NEW\src\CleanC.UI\App.xaml.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.UI.Xaml;

namespace CleanC.UI
{
    public partial class App : Application
    {
        private Window? _window;

        // جلوگیری از اجرای همزمان دو نمونه از CleanC (Single Instance)
        private static Mutex? _singleInstanceMutex;

        public App()
        {
            try
            {
                var culture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.CurrentCulture.Clone();
                culture.NumberFormat.NumberDecimalSeparator = ".";
                culture.NumberFormat.PercentDecimalSeparator = ".";
                culture.NumberFormat.CurrencyDecimalSeparator = ".";
                culture.DateTimeFormat.DateSeparator = ".";
                System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            }
            catch { }

            this.InitializeComponent();

            // C6 — ریشه ی ترکیب DI همین ابتدا ساخته می شود تا خطای ثبت سرویس ها
            // در همان لحظه ی اجرا دیده شود، نه وسط کارِ کاربر.
            AppServices.EnsureBuilt();

            this.UnhandledException += (sender, e) =>
            {
                // C9 — عمداً Handed=false مانده: ادامه ی اجرا با state ناسازگار باعث
                // خراب شدنِ بی صدای داده (تنظیمات/آمار/رجیستری) می شود. لاگ ثبت می شود
                // و برنامه متوقف می شود تا خطا دفن نشود.
                LogCrash("UnhandledException", e.Exception ?? new Exception(e.Message));
                Debug.WriteLine($"[App UnhandledException] {e.Message}");
            };
        }

        /// <summary>
        /// ثبت خطای غیرمنتظره در %LOCALAPPDATA%\CleanC\crash.log
        /// همه ی catchهای برنامه باید از اینجا استفاده کنند تا خطاها سکوت نشوند.
        /// </summary>
        public static void LogCrash(string context, Exception ex)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CleanC");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "crash.log");
                File.AppendAllText(logFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {context}: {ex.Message}\n{ex}\n\n");
            }
            catch
            {
                // لاگ کردن هرگز نباید خودش باعث crash شود
            }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // اگر نمونه ای از قبل در حال اجراست، همان را به جلو بیاور و از ساخت پنجره دوم جلوگیری کن
            _singleInstanceMutex = new Mutex(true, "Local\\CleanC.SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                NativeMethods.ActivateExistingInstance();
                Environment.Exit(0);
                return;
            }

            _window = new MainWindow();
            _window.Activate();
        }
    }
}
