// src/CleanC.UI/Hero/HeroHost.xaml.cs
// میزبان WebView2 برای هیروکارت: راه‌اندازی هسته، بارگذاری CleanC.hero.html،
// صف کردن پیام‌ها تا آماده شدن صفحه، و تحویل پیام‌های JS به MainWindow.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace CleanC.UI.Hero
{
    public sealed partial class HeroHost : UserControl
    {
        /// <summary>نام میزبان مجازی؛ مسیر فایل‌ها به پوشهٔ Assets مپ می‌شود تا URL ها تمیز بمانند و CORS نگیریم.</summary>
        internal const string VirtualHostName = "cleanc.hero";

        /// <summary>هر پیام خروجی از C# از اینجا می‌گذرد؛ در صورت نبودِ هسته در صف می‌ماند.</summary>
        public event EventHandler<string>? Outbound;

        /// <summary>پیام واردشده از JS؛ MainWindow این را به HeroHostApi وصل می‌کند.</summary>
        public event EventHandler<HeroMessage>? MessageReceived;

        /// <summary>صفحهٔ HTML بالا آمد و اولین پیام (<c>settings.request</c>) را فرستاد.</summary>
        public event EventHandler? Ready;

        public bool IsReady { get; private set; }

        private bool _coreReady;
        private bool _initializing;
        private readonly List<string> _outbox = new();

        public HeroHost()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        /// <summary>برای اجبار به راه‌اندازی زودتر (مثلاً بعد از ساخت پنجره).</summary>
        public Task InitializeAsync() => EnsureCoreAsync();

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await EnsureCoreAsync();
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "HeroHost.Loaded");
            }
        }

        private async Task EnsureCoreAsync()
        {
            if (_coreReady || _initializing) return;
            _initializing = true;
            try
            {
                // پروجکشن WebView2 در این TFM فقط متد بدون-پارامتر CreateAsync() را ارائه می‌دهد
                // (پارامترهای optional winmd در projection حذف شده‌اند)، پس پوشهٔ user data
                // به‌صورت پیش‌فرض توسط WebView2 انتخاب می‌شود و ما دخالتی در مسیرش نداریم.
                // پس‌زمینهٔ WebView2 شفاف می‌شود تا گوشه‌های گرد هیرو (radius در HTML) لایهٔ پشتی
                // کارت را نشان دهد. باید قبل از EnsureCoreWebView2Async ست شود تا هنگام لودِ اولیه
                // سفید پیش‌فرض WebView2 پشتِ چهار گوشهٔ شفاف نمایان نشود.
                try { WebView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent; }
                catch (Exception ex) { CcLog.Ignore(ex, "HeroHost.DefaultBackgroundColor"); }

                var env = await CoreWebView2Environment.CreateAsync();

                await WebView.EnsureCoreWebView2Async(env);
                try { WebView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent; }
                catch (Exception ex) { CcLog.Ignore(ex, "HeroHost.DefaultBackgroundColorPost"); }

                var core = WebView.CoreWebView2;
                if (core == null) return;

                // ظاهر: بدون منوی راست‌کلیک، بدون نوار وضعیت، بدون ابزار توسعه، بدون میان‌برهای مرورگر
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsSwipeNavigationEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;

                // مسیر فایل‌ها: <base>\Assets  ← https://cleanc.hero/Hero/CleanC.hero.html
                var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
                if (Directory.Exists(assets))
                {
                    core.SetVirtualHostNameToFolderMapping(VirtualHostName, assets, CoreWebView2HostResourceAccessKind.Allow);
                }

                // لینک‌های خارجی (target=_blank) و تلاش برای خروج از میزبان مجازی بسته می‌شوند
                core.NewWindowRequested += (_, args) => { args.Handled = true; };
                core.NavigationStarting += (_, args) =>
                {
                    if (!args.Uri.StartsWith($"https://{VirtualHostName}/", StringComparison.OrdinalIgnoreCase))
                    {
                        args.Cancel = true;
                    }
                };
                core.WebMessageReceived += OnWebMessageReceived;
                core.Navigate($"https://{VirtualHostName}/Hero/CleanC.hero.html");

                _coreReady = true;
                FlushOutbox();
            }
            finally
            {
                _initializing = false;
            }
        }

        /// <summary>%LOCALAPPDATA%\CleanC\WebView2 — جدا از کش مرورگر کاربر تا پاک‌سازی آن به اپ آسیب نزند.</summary>
        private static string UserDataFolder()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CleanC", "WebView2");

        private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                var msg = HeroMessage.Parse(args.WebMessageAsJson);
                if (msg == null) return;

                // «settings.request» یعنی صفحهٔ HTML تا انتهای init بالا آمده؛ همان سیگنال آمادگی است.
                if (!IsReady && (msg.Type == "settings.request" || msg.Type == "settings" || msg.RpcId.HasValue))
                {
                    IsReady = true;
                    Ready?.Invoke(this, EventArgs.Empty);
                }

                MessageReceived?.Invoke(this, msg);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "HeroHost.WebMessageReceived");
            }
        }

        /// <summary>ارسال پیام به JS. اگر هسته هنوز آماده نیست در صف می‌ماند و بعداً flush می‌شود.</summary>
        public void Send(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (!_coreReady || WebView?.CoreWebView2 == null)
            {
                _outbox.Add(json);
                return;
            }
            Post(json);
        }

        private void FlushOutbox()
        {
            if (_outbox.Count == 0) return;
            var pending = _outbox.ToArray();
            _outbox.Clear();
            foreach (var json in pending) Post(json);
        }

        private void Post(string json)
        {
            // WebView2 فقط روی UI thread پاسخ می‌دهد؛ همه‌ی فراخواننده‌ها معمولاً UI هستند
            // ولی اینجا برای اطمینان از Dispatcher استفاده می‌کنیم.
            var dq = DispatcherQueue;
            if (dq.HasThreadAccess) PostCore(json);
            else dq.TryEnqueue(() => PostCore(json));
        }

        private void PostCore(string json)
        {
            try
            {
                WebView?.CoreWebView2?.PostWebMessageAsJson(json);
                Outbound?.Invoke(this, json);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "HeroHost.Post");
            }
        }
    }
}
