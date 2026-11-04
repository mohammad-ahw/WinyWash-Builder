// src/CleanC.UI/MainWindow.News.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace CleanC.UI
{
    public sealed partial class MainWindow
    {
        private List<NewsItem> _newsItems = new();
        private int _currentNewsIndex = 0;
        private DispatcherTimer? _newsTimer;
        private double _slideProgress = 0.0;
        private const double SlideDurationSec = 6.0;
        private const double TimerIntervalMs = 50.0;
        private bool _isNewsHovered = false;
        private bool _newsWebReady = false;
        private double _savedDashboardScroll = 0.0;

        private const string ReaderModeScript = """
            (function() {
                function applyReaderMode() {
                    var host = (window.location && window.location.hostname) ? window.location.hostname.toLowerCase() : '';
                    var styleId = 'winywash-reader-style';
                    if (!document.getElementById(styleId)) {
                        var style = document.createElement('style');
                        style.id = styleId;
                        style.textContent = `
                            /* Universal Distraction Cleanup: Headers, Footers, Sidebars, Ads & Promos */
                            header:not([class*="ArticleHeader"]),
                            footer,
                            aside,
                            div[class*="sidebar"]:not(.no-sidebar):not([class*="content"]),
                            section[class*="sidebar"],
                            .articleNewsPosts__sidebar,
                            [class*="__sidebar"],
                            [class*="header-module__"]:not([class*="title"]),
                            [class*="footer-module__"],
                            [class*="StickySidebar"],
                            [class*="SidebarWrapper"],
                            [class*="socials-module__"],
                            [class*="social_container"],
                            [class*="tags-module__"],
                            [class*="__tags"],
                            .site-header,
                            .site-footer,
                            .main-navigation,
                            #site-navigation,
                            .navigation,
                            .megaMenu,
                            .pollDigiato,
                            .backtotop,
                            .breadcrumbAndAds,
                            .digiBannerArea,
                            .nativeSuggestions,
                            .digiatoSuggestions,
                            .registerWidget,
                            .videoWidget,
                            .stickyDownSingle,
                            .postTools,
                            .postRelated,
                            .postComments,
                            [class*="ad_"],
                            [class*="yn-"],
                            [id*="ynpos"],
                            [class*="related_news_container"],
                            [class*="selected_videos_container"],
                            [class*="related_videos_container"],
                            [id*="pos-article-"],
                            [id*="ad-"],
                            [class*="advertising"],
                            [class*="baleBanner"],
                            [class*="telegramBanner"],
                            [class*="download-module"],
                            [id*="div_eRasaneh"],
                            [class*="floating-banner"],
                            [class*="app-banner"],
                            [id*="sticky-banner"],
                            div[class*="top-0"][class*="sticky"],
                            header.bg-black1,
                            div[class*="row-start-3"],
                            div[class*="scroll-mt-10"],
                            a[href*="rubika.ir"],
                            a[href*="eitaa.com"],
                            a[href*="ble.ir"],
                            a[href*="bale.ai"] {
                                display: none !important;
                                visibility: hidden !important;
                                height: 0 !important;
                                max-height: 0 !important;
                                overflow: hidden !important;
                                opacity: 0 !important;
                                pointer-events: none !important;
                            }

                            /* Akharin Khabar: Full Width & Centering */
                            [class*="news_detail-module"][class*="__section"] {
                                display: block !important;
                                width: 100% !important;
                                max-width: 1150px !important;
                                margin-left: auto !important;
                                margin-right: auto !important;
                                padding-left: 20px !important;
                                padding-right: 20px !important;
                            }
                            [class*="news_detail-module"][class*="__main"] {
                                grid-column: 1 / -1 !important;
                                width: 100% !important;
                                max-width: 100% !important;
                                display: block !important;
                                margin-left: auto !important;
                                margin-right: auto !important;
                            }

                            /* Digiato: Full Width & Centering */
                            .articleNewsPosts__content {
                                max-width: 1150px !important;
                                width: 100% !important;
                                flex: 1 1 100% !important;
                                margin-left: auto !important;
                                margin-right: auto !important;
                                padding-left: 20px !important;
                                padding-right: 20px !important;
                            }

                            /* Zoomit: Spacious Wide Layout & Container Unlocking */
                            main#main-content > div:first-child,
                            div[class*="grid-rows-[auto_auto_auto_1fr]"],
                            div[class*="lg:grid-cols-"],
                            div[class*="xxl:grid-cols-"] {
                                grid-template-columns: 100% !important;
                                max-width: 1150px !important;
                                width: 100% !important;
                                margin-left: auto !important;
                                margin-right: auto !important;
                                padding-left: 24px !important;
                                padding-right: 24px !important;
                                border: none !important;
                                box-shadow: none !important;
                            }
                            [class*="BlockContainer__Root"] {
                                margin-left: 0 !important;
                                margin-right: 0 !important;
                                padding-left: 0 !important;
                                padding-right: 0 !important;
                                width: 100% !important;
                                max-width: 100% !important;
                                --margin-left: 0px !important;
                                --margin-right: 0px !important;
                            }
                            [class*="BlockContainer__InnerArticleContainer"],
                            .long-form,
                            article {
                                max-width: 100% !important;
                                width: 100% !important;
                            }

                            /* Universal Polish */
                            html, body {
                                overflow-x: hidden !important;
                            }
                        `;
                        (document.head || document.documentElement).appendChild(style);
                    }

                    // --- 1. AKHARIN KHABAR ISOLATED LOGIC ---
                    if (host.includes('akharinkhabar')) {
                        var akSections = document.querySelectorAll('[class*="news_detail-module"][class*="__section"]');
                        for (var i = 0; i < akSections.length; i++) {
                            akSections[i].style.setProperty('display', 'block', 'important');
                            akSections[i].style.setProperty('width', '100%', 'important');
                            akSections[i].style.setProperty('max-width', '1150px', 'important');
                            akSections[i].style.setProperty('margin-left', 'auto', 'important');
                            akSections[i].style.setProperty('margin-right', 'auto', 'important');
                        }

                        var akMains = document.querySelectorAll('[class*="news_detail-module"][class*="__main"]');
                        for (var i = 0; i < akMains.length; i++) {
                            akMains[i].style.setProperty('grid-column', '1 / -1', 'important');
                            akMains[i].style.setProperty('width', '100%', 'important');
                            akMains[i].style.setProperty('max-width', '100%', 'important');
                            akMains[i].style.setProperty('display', 'block', 'important');
                        }

                        var akSidebars = document.querySelectorAll('[class*="news_detail-module"][class*="sidebar"], [class*="__sidebar"], aside');
                        for (var i = 0; i < akSidebars.length; i++) {
                            akSidebars[i].style.setProperty('display', 'none', 'important');
                        }

                        // Hide messenger links and their parent paragraphs
                        var promoLinks = document.querySelectorAll('a[href*="rubika.ir"], a[href*="eitaa.com"], a[href*="ble.ir"], a[href*="bale.ai"], a[href*="t.me/akharinkhabar"]');
                        for (var i = 0; i < promoLinks.length; i++) {
                            var pBox = promoLinks[i].closest('p');
                            if (pBox) {
                                pBox.style.setProperty('display', 'none', 'important');
                            } else {
                                promoLinks[i].style.setProperty('display', 'none', 'important');
                            }
                        }

                        // Hide search tags
                        var akTags = document.querySelectorAll('[class*="tags-module"], a[href*="/search/"]');
                        for (var i = 0; i < akTags.length; i++) {
                            akTags[i].style.setProperty('display', 'none', 'important');
                        }
                    }

                    // --- 2. ZOOMIT ISOLATED LOGIC ---
                    if (host.includes('zoomit')) {
                        var zoomitMain = document.getElementById('main-content');
                        if (zoomitMain) {
                            // Clean preceding headers & top ads before main
                            var prev = zoomitMain.previousElementSibling;
                            while (prev) {
                                prev.style.setProperty('display', 'none', 'important');
                                prev = prev.previousElementSibling;
                            }
                            // Clean siblings after article grid (comments, study lists, related stories, ads)
                            if (zoomitMain.firstElementChild) {
                                var afterGrid = zoomitMain.firstElementChild.nextElementSibling;
                                while (afterGrid) {
                                    afterGrid.style.setProperty('display', 'none', 'important');
                                    afterGrid = afterGrid.nextElementSibling;
                                }
                            }
                        }

                        // Unlock wide layout on Zoomit inner blocks
                        var rootBlocks = document.querySelectorAll('[class*="BlockContainer__Root"]');
                        for (var i = 0; i < rootBlocks.length; i++) {
                            rootBlocks[i].style.setProperty('margin-left', '0px', 'important');
                            rootBlocks[i].style.setProperty('margin-right', '0px', 'important');
                            rootBlocks[i].style.setProperty('width', '100%', 'important');
                            rootBlocks[i].style.setProperty('max-width', '100%', 'important');
                        }
                        var innerBlocks = document.querySelectorAll('[class*="BlockContainer__InnerArticleContainer"], .long-form');
                        for (var i = 0; i < innerBlocks.length; i++) {
                            innerBlocks[i].style.setProperty('max-width', '100%', 'important');
                            innerBlocks[i].style.setProperty('width', '100%', 'important');
                        }
                    }

                    // --- 3. DIGIATO ISOLATED LOGIC ---
                    if (host.includes('digiato')) {
                        var digiatoSidebar = document.querySelector('.articleNewsPosts__sidebar');
                        if (digiatoSidebar) {
                            digiatoSidebar.style.setProperty('display', 'none', 'important');
                        }
                        var digContent = document.querySelector('.articleNewsPosts__content');
                        if (digContent) {
                            digContent.style.setProperty('max-width', '1150px', 'important');
                            digContent.style.setProperty('width', '100%', 'important');
                            digContent.style.setProperty('flex', '1 1 100%', 'important');
                        }
                        var digiatoPost = document.querySelector('.articlePost');
                        if (digiatoPost) {
                            var digNext = digiatoPost.nextElementSibling;
                            while (digNext) {
                                digNext.style.setProperty('display', 'none', 'important');
                                digNext = digNext.nextElementSibling;
                            }
                        }
                        var digPoll = document.querySelectorAll('.pollDigiato, .backtotop, .main-navigation');
                        for (var i = 0; i < digPoll.length; i++) {
                            digPoll[i].style.setProperty('display', 'none', 'important');
                        }
                    }
                }

                if (document.readyState === 'loading') {
                    document.addEventListener('DOMContentLoaded', applyReaderMode);
                } else {
                    applyReaderMode();
                }

                try {
                    var observer = new MutationObserver(function() {
                        applyReaderMode();
                    });
                    if (document.documentElement) {
                        observer.observe(document.documentElement, { childList: true, subtree: true });
                    }
                } catch (e) {}
            })();
            """;

        private void InitializeNewsFeature()
        {
            if (LatestNewsCard == null) return;

            LatestNewsCard.PointerEntered += (s, e) => _isNewsHovered = true;
            LatestNewsCard.PointerExited += (s, e) => _isNewsHovered = false;

            _newsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(TimerIntervalMs)
            };
            _newsTimer.Tick += OnNewsTimerTick;

            _ = LoadNewsAsync();
        }

        private async Task LoadNewsAsync(bool forceRefresh = false)
        {
            try
            {
                if (!NewsFeedService.IsInternetConnected())
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (LatestNewsCard != null)
                            LatestNewsCard.Visibility = Visibility.Collapsed;
                    });
                    return;
                }

                var items = await NewsFeedService.GetLatestNewsAsync(forceRefresh).ConfigureAwait(false);

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (items != null && items.Count > 0)
                    {
                        _newsItems = items;
                        ShowNewsSlide(0);
                        if (LatestNewsCard != null)
                            LatestNewsCard.Visibility = Visibility.Visible;

                        _slideProgress = 0.0;
                        _newsTimer?.Start();
                    }
                    else
                    {
                        if (LatestNewsCard != null)
                            LatestNewsCard.Visibility = Visibility.Collapsed;
                    }
                });
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "MainWindow.LoadNewsAsync");
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (LatestNewsCard != null)
                        LatestNewsCard.Visibility = Visibility.Collapsed;
                });
            }
        }

        private void OnNewsTimerTick(object? sender, object e)
        {
            if (_isNewsHovered || _newsItems.Count == 0) return;

            _slideProgress += TimerIntervalMs / (SlideDurationSec * 1000.0);

            if (_slideProgress >= 1.0)
            {
                _currentNewsIndex = (_currentNewsIndex + 1) % _newsItems.Count;
                ShowNewsSlide(_currentNewsIndex);
            }
            else
            {
                UpdateNewsIconProgress(_slideProgress);
            }
        }

        private void ShowNewsSlide(int index)
        {
            if (_newsItems.Count == 0) return;
            _currentNewsIndex = Math.Clamp(index, 0, _newsItems.Count - 1);
            _slideProgress = 0.0;

            var item = _newsItems[_currentNewsIndex];

            if (NewsHeadlineText != null)
                NewsHeadlineText.Text = item.Title;

            UpdateNewsIconProgress(0.0);
        }

        private void UpdateNewsIconProgress(double pct)
        {
            if (NewsIconProgressArc == null) return;

            pct = Math.Clamp(pct, 0.0, 1.0);
            if (pct <= 0.005)
            {
                NewsIconProgressArc.Data = null;
                return;
            }

            double cx = 15.0;
            double cy = 15.0;
            double r = 13.0;

            // شروع از رأس دایره: زاویه -pi / 2
            double startX = cx;
            double startY = cy - r;

            // گردش در جهت عقربه‌های ساعت
            double angle = -Math.PI / 2.0 + (pct * 2.0 * Math.PI);
            double endX = cx + r * Math.Cos(angle);
            double endY = cy + r * Math.Sin(angle);

            if (pct >= 0.999)
            {
                endX = cx - 0.01;
                endY = cy - r;
            }

            var figure = new PathFigure
            {
                StartPoint = new Point(startX, startY),
                IsClosed = false
            };

            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(endX, endY),
                Size = new Size(r, r),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = pct > 0.5
            });

            var geom = new PathGeometry();
            geom.Figures.Add(figure);
            NewsIconProgressArc.Data = geom;
        }

        private void NewsNextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_newsItems.Count == 0) return;
            _currentNewsIndex = (_currentNewsIndex + 1) % _newsItems.Count;
            ShowNewsSlide(_currentNewsIndex);
        }

        private void NewsPrevBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_newsItems.Count == 0) return;
            _currentNewsIndex = (_currentNewsIndex - 1 + _newsItems.Count) % _newsItems.Count;
            ShowNewsSlide(_currentNewsIndex);
        }

        private void NewsHeadlineClickArea_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _isNewsHovered = true;
            if (NewsHeadlineText != null)
            {
                NewsHeadlineText.FontWeight = FontWeights.SemiBold;
                NewsHeadlineText.Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            }
            if (NewsActionText != null)
            {
                NewsActionText.Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            }
            if (NewsActionIcon != null)
            {
                NewsActionIcon.Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            }
        }

        private void NewsHeadlineClickArea_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isNewsHovered = false;
            if (NewsHeadlineText != null)
            {
                NewsHeadlineText.FontWeight = FontWeights.Normal;
                NewsHeadlineText.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            }
            if (NewsActionText != null)
            {
                NewsActionText.Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
            }
            if (NewsActionIcon != null)
            {
                NewsActionIcon.Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
            }
        }

        private async void NewsHeadlineClickArea_Click(object sender, RoutedEventArgs e)
        {
            if (_newsItems.Count == 0 || _currentNewsIndex >= _newsItems.Count) return;
            var item = _newsItems[_currentNewsIndex];
            await OpenNewsReaderAsync(item);
        }

        private async void NewsHeadlineClickArea_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_newsItems.Count == 0 || _currentNewsIndex >= _newsItems.Count) return;
            var item = _newsItems[_currentNewsIndex];
            await OpenNewsReaderAsync(item);
        }

        public async Task OpenNewsReaderAsync(NewsItem item)
        {
            try
            {
                if (NewsReaderOverlay == null) return;

                // ذخیره وضعیت اسکرول داشبورد و مخفی کردن آن تا از هرگونه اسکرول ناخواسته جلوگیری شود
                if (DashboardView != null)
                {
                    _savedDashboardScroll = DashboardView.VerticalOffset;
                    DashboardView.Visibility = Visibility.Collapsed;
                }

                if (NewsReaderHeaderTitle != null)
                    NewsReaderHeaderTitle.Text = item.Title;

                if (NewsReaderProgressBar != null)
                    NewsReaderProgressBar.Visibility = Visibility.Visible;

                NewsReaderOverlay.Visibility = Visibility.Visible;

                await EnsureNewsWebViewAsync();

                if (NewsWebView?.CoreWebView2 != null)
                {
                    NewsWebView.CoreWebView2.Navigate(item.Url);
                }
            }
            catch (Exception ex)
            {
                CcLog.Report("OpenNewsReader", ex);
            }
        }

        private async Task EnsureNewsWebViewAsync()
        {
            if (_newsWebReady || NewsWebView == null) return;
            try
            {
                var env = await CoreWebView2Environment.CreateAsync();
                await NewsWebView.EnsureCoreWebView2Async(env);

                if (NewsWebView.CoreWebView2 != null)
                {
                    NewsWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    NewsWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;

                    // تزریق اسکریپت حالت مطالعه پیش از اجرای اسکریپت‌های صفحه
                    try
                    {
                        await NewsWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ReaderModeScript);
                    }
                    catch (Exception exScript)
                    {
                        CcLog.Ignore(exScript, "AddScriptToExecuteOnDocumentCreatedAsync");
                    }

                    NewsWebView.CoreWebView2.NavigationStarting += (s, e) =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (NewsReaderProgressBar != null)
                                NewsReaderProgressBar.Visibility = Visibility.Visible;
                        });
                    };

                    NewsWebView.CoreWebView2.NavigationCompleted += (s, e) =>
                    {
                        DispatcherQueue.TryEnqueue(async () =>
                        {
                            if (NewsReaderProgressBar != null)
                                NewsReaderProgressBar.Visibility = Visibility.Collapsed;

                            // اعمال مجدد حالت مطالعه و جلوگیری از اسکرول خودکار صفحه وب به پایین
                            try
                            {
                                await NewsWebView.CoreWebView2.ExecuteScriptAsync(ReaderModeScript);
                                await NewsWebView.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0, 0);");
                            }
                            catch
                            {
                            }
                        });
                    };

                    _newsWebReady = true;
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "EnsureNewsWebViewAsync");
            }
        }

        public void CloseNewsReader()
        {
            if (NewsReaderOverlay == null) return;
            NewsReaderOverlay.Visibility = Visibility.Collapsed;

            // بازگرداندن داشبورد و بازنشانی اسکرول به حالت پیشین
            if (DashboardView != null)
            {
                DashboardView.Visibility = Visibility.Visible;
                DashboardView.ChangeView(null, _savedDashboardScroll, null, true);
            }

            try
            {
                NewsWebView?.CoreWebView2?.Stop();
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsWebView.Stop");
            }
        }

        private void NewsReaderBackBtn_Click(object sender, RoutedEventArgs e)
        {
            CloseNewsReader();
        }

        private void NewsReaderRefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                NewsWebView?.Reload();
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsReaderRefresh");
            }
        }

        private void NewsReaderExternalBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_newsItems.Count == 0 || _currentNewsIndex >= _newsItems.Count) return;
            try
            {
                var url = _newsItems[_currentNewsIndex].Url;
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsReaderExternalBtn_Click");
            }
        }
    }
}
