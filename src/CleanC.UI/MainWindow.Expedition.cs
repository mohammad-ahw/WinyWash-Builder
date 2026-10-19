// src/CleanC.UI/MainWindow.Expedition.cs
// ارکستریتور «اکسپدیشن وینی» — باز/بستن لایه‌ی بازی و اتصال پل پیام.
//
// تصمیم فاز ۱: WebView2 بازی prompt-based (on-demand) راه‌اندازی می‌شود، نه در استارت‌آپ؛
// چون بازینده معمولاً گاه‌به‌گاه وارد می‌شود و هزینه‌ی حافظه لازم نیست. هر باز کردن
// یعنی بارگذاری تازه‌ی صفحه‌ی بازی؛ وضعیتِ پیشرفت از فایل save برمی‌گردد.

using System;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;
using CleanC.Core.Services;
using CleanC.UI.Expedition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CleanC.UI
{
    public sealed partial class MainWindow
    {
        // ====================================================================
        // State
        // ====================================================================

        private ExpeditionHost? _expHost;
        private ExpeditionApi? _expApi;
        private bool _isExpeditionOpen;

        /// <summary>تنظیمات برنامه برای hydration بازی — کپسول‌شده‌ی خواندنی.</summary>
        internal AppSettings ExpeditionSettings => _settings;

        // ====================================================================
        // Wiring
        // ====================================================================

        /// <summary>اتصال پل بازی؛ روی اولین باز شدن صدا زده می‌شود (نه استارت‌آپ).</summary>
        private void EnsureExpeditionWiring()
        {
            _expHost ??= ExpeditionHostXaml;
            if (_expHost == null || _expApi != null) return;

            // WebMessageReceived پیش از Navigate در EnsureCoreAsync وصل می‌شود؛
            // پیام ready سمت api خودش hydrate را می‌فرستد و نیازی به هندلر Ready از اینجا نیست.
            _expApi = new ExpeditionApi(this);
            _expHost.MessageReceived += OnExpeditionMessage;
        }

        private async void OnExpeditionMessage(object? sender, ExpeditionMessage msg)
        {
            try
            {
                if (_expApi != null) await _expApi.DispatchAsync(msg);
            }
            catch (Exception ex)
            {
                CcLog.Report("Expedition.Dispatch." + msg.Type, ex);
            }
        }

        internal void SendToExpedition(string json) => _expHost?.Send(json);

        // ====================================================================
        // Open / Close
        // ====================================================================

        public async void ExpeditionEntryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isExpeditionOpen) return;
            _isExpeditionOpen = true;

            try
            {
                EnsureExpeditionWiring();
                if (_expHost == null) { _isExpeditionOpen = false; return; }

                ExpeditionOverlay.Visibility = Visibility.Visible;

                // والپیپر امروز را تازه کن (کپی یک‌باره در بازِ هر دفعه)
                ExpeditionContentService.PrepareWallpaper();

                if (_expHost.IsReady)
                {
                    // بازی از قبل بالا آمده: ناوبری دوباره = ورود تازه با وضعیت ذخیره‌شده
                    _expHost.ReloadGame();
                }
                else
                {
                    await _expHost.InitializeAsync();
                }
            }
            catch (Exception ex)
            {
                // شکست باز کردن: پوشش بسته می‌شود تا کاربر در برنامه گیر نکند
                CcLog.Report("Expedition.Open", ex);
                _isExpeditionOpen = false;
                ExpeditionOverlay.Visibility = Visibility.Collapsed;
                ShowInfoBar(LocalizationService.Get("اکسپدیشن وینی"), LocalizationService.Get("اجرای بازی گرافیکی در دسترس نیست (WebView2 Runtime نصب شده باشد)."), InfoBarSeverity.Warning);
            }
        }

        public void CloseExpedition()
        {
            if (!_isExpeditionOpen) return;
            _isExpeditionOpen = false;
            ExpeditionOverlay.Visibility = Visibility.Collapsed;

            try
            {
                // فوکوس را به ریشه‌ی محتوا برگردان تا کیبورد (F5 و ...) در برنامه کار کند
                (Content as Microsoft.UI.Xaml.FrameworkElement)?.Focus(FocusState.Programmatic);
            }
            catch (Exception ex) { CcLog.Ignore(ex, "Expedition.Close.Focus"); }
        }
    }
}
