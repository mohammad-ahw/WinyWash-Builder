// O:\OpenCode\AntiGravity\CleanC - NEW\src\CleanC.UI\App.xaml.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CleanC.Core.Services;
using Microsoft.UI.Xaml;

namespace CleanC.UI
{
    public partial class App : Application
    {
        private Window? _window;

        // جلوگیری از اجرای همزمان دو نمونه از CleanC (Single Instance)
        private static Mutex? _singleInstanceMutex;
        private static bool _singleInstanceCreatedNew;

        /// <summary>
        /// ساخت Mutex تک‌نمونه: اول Global\ (کل نشست‌ها) و در صورت خطای دسترسی، Local\.
        /// خطاها لاگ می شوند تا مشکلات محیطی بی صدای دفن نشوند.
        /// </summary>
        private static Mutex CreateSingleInstanceMutex()
        {
            try
            {
                var m = new Mutex(true, @"Global\WinyWash.SingleInstance", out bool createdNew);
                _singleInstanceCreatedNew = createdNew;
                return m;
            }
            catch (Exception globalEx)
            {
                LogCrash("App.MutexGlobalFallback", globalEx);
                try
                {
                    var m = new Mutex(true, @"Local\WinyWash.SingleInstance", out bool createdNew);
                    _singleInstanceCreatedNew = createdNew;
                    return m;
                }
                catch (Exception localEx)
                {
                    // در بدترین حالت هم از اجرای دوباره مطلع می شویم و اجازه می دهیم ادامه یابد
                    LogCrash("App.MutexCreate", localEx);
                    _singleInstanceCreatedNew = true;
                    return new Mutex(true, "WinyWash.SingleInstance.ProcessOnly", out _);
                }
            }
        }

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
            catch (Exception cultureEx)
            {
                // تنظیم فرهنگ نباید بی سر و صدا شکست بخورد؛ برای تشخیصِ بعدی لاگ می شود
                LogCrash("App.CultureInit", cultureEx);
            }

            this.InitializeComponent();

            // مهاجرت یک باره ی داده ی کاربر از پوشه ی قدیمی CleanC به WinyWash
            // باید قبل از ساخته شدن سرویس ها اجرا شود تا مسیرها از همان ابتدا درست باشند.
            AppPaths.MigrateLegacyFolders();

            // ریشه ی ترکیب DI همین ابتدا ساخته می شود تا خطای ثبت سرویس ها
            // در همان لحظه ی اجرا دیده شود، نه وسط کارِ کاربر.
            AppServices.EnsureBuilt();

            this.UnhandledException += (sender, e) =>
            {
                // عمداً Handled=false مانده: ادامه ی اجرا با state ناسازگار باعث
                // خراب شدنِ بی صدای داده (تنظیمات/آمار/رجیستری) می شود. لاگ ثبت می شود
                // و برنامه متوقف می شود تا خطا دفن نشود.
                LogCrash("UnhandledException", e.Exception ?? new Exception(e.Message));
                Debug.WriteLine($"[App UnhandledException] {e.Message}");
            };
        }

        /// <summary>
        /// ثبت خطای غیرمنتظره در %LOCALAPPDATA%\WinyWash\crash.log
        /// همه ی catchهای برنامه باید از اینجا استفاده کنند تا خطاها سکوت نشوند.
        /// </summary>
        public static void LogCrash(string context, Exception ex)
        {
            try
            {
                string logDir = AppPaths.LocalRoot;
                Directory.CreateDirectory(logDir);
                File.AppendAllText(AppPaths.CrashLogFile,
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
            //
            // Mutex با پیشوند Global\ ساخته می شود تا کل نشست های همه کاربران را بپوشاند
            // (Local\ فقط برای حساب فعلی بود)؛ اگر ساخت Global به هر دلیل مجاز نشد
            // (سیاست های محیطی)، برای پایداری به Local\ برمی گردیم.
            _singleInstanceMutex = CreateSingleInstanceMutex();
            if (!_singleInstanceCreatedNew)
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
