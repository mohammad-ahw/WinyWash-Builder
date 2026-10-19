// src/CleanC.UI/Expedition/ExpeditionHost.xaml.cs
// میزبان WebView2 اکسپدیشن — همسو با HeroHost:
//   • www آدرس‌های مجازی: cleanc.exp (Assets\Expedition)، cleanc.wall (پوشه‌ی داده) و
//     cleanc.dl (Downloads واقعی — فقط برای بندانگشتی تصاویرِ پرونده‌ها)
//   • پیام‌های JS فقط تا Ready در صف می‌مانند، بعد flush می‌شوند
//   • در نبود WebView2 Runtime به FallbackPanel سقوط می‌کند

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace CleanC.UI.Expedition
{
    public sealed partial class ExpeditionHost : UserControl
    {
        internal const string HostName = "cleanc.exp";

        /// <summary>پیام خروجی C# → JS از اینجا می‌گذرد (برای لاگ/دیباگ).</summary>
        public event EventHandler<string>? Outbound;

        /// <summary>پیام JS → C#؛ MainWindow.Expedition.api آن را dispatch می‌کند.</summary>
        public event EventHandler<ExpeditionMessage>? MessageReceived;

        /// <summary>HTML تا انتها بالا آمد و پیام ready را فرستاد.</summary>
        public event EventHandler? Ready;

        public bool IsReady { get; private set; }

        private bool _coreReady;
        private bool _initializing;
        private readonly List<string> _outbox = new();

        public ExpeditionHost()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        public Task InitializeAsync() => EnsureCoreAsync();

        /// <summary>بارگذاری مجدد صفحه‌ی بازی — هر باز شدن، ورودی تازه با وضعیت ذخیره‌شده.</summary>
        public void ReloadGame()
        {
            try
            {
                if (!_coreReady || WebView?.CoreWebView2 == null) return;
                WebView.CoreWebView2.Navigate(GameUrl());
            }
            catch (Exception ex) { CcLog.Ignore(ex, "ExpeditionHost.Reload"); }
        }

        /// <summary>
        /// آدرس بازی همراه با مُهر نسخه‌ی فایل (mtime) — WebView2 کش HTTP دارد و
        /// بدون این پارامتر ممکن است نسخه‌ی قدیمی HTML را برگرداند؛ با تغییر فایل،
        /// رشته‌ی کوئری عوض می‌شود و همیشه محتوای تازه لود می‌شود.
        /// </summary>
        private string GameUrl()
        {
            try
            {
                var html = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Expedition", "Winy.expedition.html");
                var rev = System.IO.File.GetLastWriteTimeUtc(html).Ticks.ToString("x");
                return $"https://{HostName}/Winy.expedition.html?v={rev}";
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "ExpeditionHost.GameUrl");
                return $"https://{HostName}/Winy.expedition.html";
            }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try { await EnsureCoreAsync(); }
            catch (Exception ex) { CcLog.Ignore(ex, "ExpeditionHost.Loaded"); }
        }

        private async Task EnsureCoreAsync()
        {
            if (_coreReady || _initializing) return;
            _initializing = true;
            try
            {
                // WebView2 تا لودِ اولیه سیاه رندر نشود؛ صفحه‌ی بازی والپیپر را با خودش می‌کشد
                try { WebView.DefaultBackgroundColor = Microsoft.UI.Colors.Black; }
                catch (Exception ex) { CcLog.Ignore(ex, "ExpeditionHost.BackgroundColor"); }

                var env = await CoreWebView2Environment.CreateAsync();
                await WebView.EnsureCoreWebView2Async(env);

                var core = WebView.CoreWebView2;
                if (core == null) return;

                // ظاهر و امنیت: بدون منو/نوار وضعیت/DevTools، لینک خارجی بسته، ناوبری به میزبان مجازی فقط
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsSwipeNavigationEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsBuiltInErrorPageEnabled = false;

                core.NewWindowRequested += (_, args) => { args.Handled = true; };
                core.NavigationStarting += (_, args) =>
                {
                    if (!args.Uri.StartsWith($"https://{HostName}/", StringComparison.OrdinalIgnoreCase))
                    {
                        args.Cancel = true;
                    }
                };

                // ۱) میزبان بازی: Assets\Expedition  ← https://cleanc.exp/Winy.expedition.html
                var gameDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Expedition");
                core.SetVirtualHostNameToFolderMapping(
                    HostName, gameDir, CoreWebView2HostResourceAccessKind.Allow);

                // ۲) میزبان داده (والپیپر کپی‌شده): %LOCALAPPDATA%\CleanC\exped ← https://cleanc.wall/
                var dataDir = ExpeditionContentService.EnsureDataDir();
                if (dataDir.Length > 0)
                {
                    core.SetVirtualHostNameToFolderMapping(
                        ExpeditionContentService.WallHostName, dataDir, CoreWebView2HostResourceAccessKind.Allow);
                }

                // ۳) میزبان دانلودها: بندانگشتی واقعی تصاویرِ پرونده‌ها  ← https://cleanc.dl/<file name>
                var downloads = CleanC.Core.Services.ExpeditionService.DownloadsDir;
                if (downloads.Length > 0 && Directory.Exists(downloads))
                {
                    core.SetVirtualHostNameToFolderMapping(
                        ExpeditionContentService.DownloadsHostName, downloads, CoreWebView2HostResourceAccessKind.Allow);
                }

                core.WebMessageReceived += OnWebMessageReceived;
                core.Navigate(GameUrl());

                _coreReady = true;
                FlushOutbox();
            }
            catch (Exception ex)
            {
                // WebView2 نبود / asset مفقود / خطای سخت: پیام کوتاه و بازگشت
                CcLog.Ignore(ex, "ExpeditionHost.EnsureCore");
                ShowFallbackPanel();
            }
            finally
            {
                _initializing = false;
            }
        }

        private void ShowFallbackPanel()
        {
            try
            {
                if (FallbackPanel != null) FallbackPanel.Visibility = Visibility.Visible;
                if (WebView != null) WebView.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) { CcLog.Ignore(ex, "ExpeditionHost.Fallback"); }
        }

        private void FallbackCloseBtn_Click(object sender, RoutedEventArgs e)
            => MessageReceived?.Invoke(this, new ExpeditionMessage { Type = "exit" });

        private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                var msg = ExpeditionMessage.Parse(args.WebMessageAsJson);
                if (msg == null) return;

                if (!IsReady && msg.Type == "ready")
                {
                    IsReady = true;
                    Ready?.Invoke(this, EventArgs.Empty);
                }

                MessageReceived?.Invoke(this, msg);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "ExpeditionHost.WebMessage");
            }
        }

        public void Send(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (!_coreReady || WebView?.CoreWebView2 == null)
            {
                // پیام‌های پیش از آماده شدن کاری بی‌معنی نیستند ولی flush آنها بی‌ضرر است
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
                CcLog.Ignore(ex, "ExpeditionHost.Post");
            }
        }
    }
}
