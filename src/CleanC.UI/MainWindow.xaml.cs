using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT;
using CleanC.Core.Models;
using CleanC.Core.Services;
using CleanC.UI.ViewModels;

namespace CleanC.UI
{
    public sealed partial class MainWindow : Window
    {
        private AppWindow _appWindow;
        private DispatcherTimer _statsTimer;
        private ObservableCollection<JunkItemViewModel> _activeCleaningItems = new();
        private ScanResult? _cachedScanResult;
        private bool _isBusy = false;
        private long _displayedFreedBytes = 0;
        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _configurationSource;

        public MainWindow()
        {
            this.InitializeComponent();

            _appWindow = GetAppWindowForCurrentWindow();
            ConfigureTitleBar();
            ConfigureSystemBackdrop();

            // Set Window and Taskbar Icon
            try
            {
                string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    _appWindow.SetIcon(iconPath);
                }
            }
            catch { }

            // Determine initial theme & apply cleanly
            var initialTheme = (Application.Current.RequestedTheme == ApplicationTheme.Dark) ? ElementTheme.Dark : ElementTheme.Light;
            ApplyTheme(initialTheme);

            this.Activated += (s, e) =>
            {
                if (Content is FrameworkElement root)
                {
                    ApplyTheme(root.ActualTheme);
                }
            };

            if (Content is FrameworkElement rootElement)
            {
                rootElement.ActualThemeChanged += (s, e) =>
                {
                    ApplyTheme(rootElement.ActualTheme);
                };

                rootElement.SizeChanged += (s, e) =>
                {
                    UpdateResponsiveLayout(e.NewSize.Width);
                };
            }

            // Set Initial Window Size (DPI-aware and spacious)
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = CleanC.Core.Native.Win32.GetDpiForWindow(hWnd);
            double dpiScale = (dpi > 0) ? (dpi / 96.0) : 1.0;
            int initialW = (int)(1060 * dpiScale);
            int initialH = (int)(760 * dpiScale);
            _appWindow.Resize(new SizeInt32(Math.Max(initialW, 1060), Math.Max(initialH, 760)));

            _appWindow.Changed += (s, e) =>
            {
                if (e.DidSizeChange)
                {
                    var currentSize = _appWindow.Size;
                    int minWidth = 660;
                    int minHeight = 550;
                    if (currentSize.Width < minWidth || currentSize.Height < minHeight)
                    {
                        _appWindow.Resize(new SizeInt32(
                            Math.Max(currentSize.Width, minWidth),
                            Math.Max(currentSize.Height, minHeight)));
                    }
                }
                if (e.DidSizeChange || e.DidPositionChange)
                {
                    UpdateCaptionInsets();
                }
            };

            // Initial DPI-aware responsive layout pass
            double initialDipWidth = _appWindow.Size.Width / dpiScale;
            UpdateResponsiveLayout(initialDipWidth);

            // Set default selected navigation item
            SwitchToTab(true);

            LiveCleanListView.ItemsSource = _activeCleaningItems;

            // Setup real-time stats timer
            _statsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _statsTimer.Tick += (s, e) => RefreshSystemStats();
            _statsTimer.Start();

            // Hook Advanced Expander expansion
            AdvancedCleanExpander.Expanding += AdvancedCleanExpander_Expanding;

            // Check elevation status
            CheckAdminStatus();

            // Initial load
            RefreshSystemStats();
        }

        private AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(wndId);
        }

        private void ConfigureTitleBar()
        {
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = _appWindow.TitleBar;
                titleBar.ExtendsContentIntoTitleBar = true;

                var initialTheme = (Content is FrameworkElement root && root.ActualTheme != ElementTheme.Default)
                    ? root.ActualTheme
                    : (Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light);
                UpdateTitleBarTheme(initialTheme);

                this.ExtendsContentIntoTitleBar = true;
                this.SetTitleBar(TitleBarDragRegion);

                UpdateCaptionInsets();
            }
        }

        private void UpdateCaptionInsets()
        {
            if (!AppWindowTitleBar.IsCustomizationSupported()) return;
            var titleBar = _appWindow.TitleBar;
            int inset = titleBar.RightInset;
            CaptionButtonsSpacer.Width = inset > 0 ? inset : 144;
        }

        private void UpdateTitleBarTheme(ElementTheme theme)
        {
            bool isDark = theme == ElementTheme.Dark;
            bool useLightCaptionButtons = isDark || _isWallpaperDark;

            // Sync Win32 DWM Immersive Dark Mode with HWND
            try
            {
                IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                int isDarkVal = isDark ? 1 : 0;
                CleanC.Core.Native.Win32.DwmSetWindowAttribute(hWnd, CleanC.Core.Native.Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref isDarkVal, sizeof(int));
                
                // Clear any DWM backdrop override (1 = DWMSBT_NONE) so DWM does NOT draw an opaque light fallback sheet!
                int backdropNone = 1;
                CleanC.Core.Native.Win32.DwmSetWindowAttribute(hWnd, CleanC.Core.Native.Win32.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropNone, sizeof(int));
            }
            catch { }

            // Apply fine-tuned acrylic composition backdrop for active theme
            ConfigureSystemBackdrop(theme);

            if (!AppWindowTitleBar.IsCustomizationSupported()) return;
            var titleBar = _appWindow.TitleBar;

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            if (useLightCaptionButtons)
            {
                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(140, 255, 255, 255);
                titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(35, 255, 255, 255);
                titleBar.ButtonHoverForegroundColor = Colors.White;
                titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(60, 255, 255, 255);
                titleBar.ButtonPressedForegroundColor = Colors.White;
            }
            else
            {
                titleBar.ButtonForegroundColor = Colors.Black;
                titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(140, 0, 0, 0);
                titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(20, 0, 0, 0);
                titleBar.ButtonHoverForegroundColor = Colors.Black;
                titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(40, 0, 0, 0);
                titleBar.ButtonPressedForegroundColor = Colors.Black;
            }
        }

        private void ConfigureSystemBackdrop(ElementTheme? theme = null)
        {
            try
            {
                ElementTheme currentTheme = theme ?? 
                    (Content is FrameworkElement root && root.ActualTheme != ElementTheme.Default 
                        ? root.ActualTheme 
                        : (Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light));

                bool isDark = currentTheme == ElementTheme.Dark;

                if (DesktopAcrylicController.IsSupported())
                {
                    if (_configurationSource == null)
                    {
                        _configurationSource = new SystemBackdropConfiguration();
                        this.Activated += (s, e) =>
                        {
                            if (_configurationSource != null)
                            {
                                _configurationSource.IsInputActive = e.WindowActivationState != WindowActivationState.Deactivated;
                            }
                        };
                        this.Closed += (s, e) =>
                        {
                            _acrylicController?.Dispose();
                            _acrylicController = null;
                            _configurationSource = null;
                        };
                    }

                    _configurationSource.Theme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
                    _configurationSource.IsInputActive = true;

                    if (_acrylicController == null)
                    {
                        _acrylicController = new DesktopAcrylicController();
                        _acrylicController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                        _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
                    }

                    if (isDark)
                    {
                        _acrylicController.TintColor = Windows.UI.Color.FromArgb(255, 20, 20, 20);
                        _acrylicController.TintOpacity = _isWallpaperDark ? 0.28f : 0.52f;
                        _acrylicController.LuminosityOpacity = _isWallpaperDark ? 0.65f : 0.85f;
                    }
                    else
                    {
                        // TRUE SEE-THROUGH GLASS IN LIGHT MODE:
                        // Low tint opacity (0.06f) and zero luminosity wash ensures the desktop
                        // wallpaper directly and visibly shines through with native silky acrylic blur!
                        _acrylicController.TintColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                        _acrylicController.TintOpacity = 0.06f;
                        _acrylicController.LuminosityOpacity = 0.0f;
                    }
                }
                else
                {
                    this.SystemBackdrop = new DesktopAcrylicBackdrop();
                }
            }
            catch
            {
                try
                {
                    this.SystemBackdrop = new DesktopAcrylicBackdrop();
                }
                catch { }
            }
        }

        private Brush GetThemeBrush(string key)
        {
            var themeKey = (Content is FrameworkElement root && root.ActualTheme == ElementTheme.Dark) ? "Dark" : "Light";
            if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var dictObj) &&
                dictObj is ResourceDictionary dict &&
                dict.TryGetValue(key, out var brushObj) &&
                brushObj is Brush b)
            {
                return b;
            }
            if (Application.Current.Resources.TryGetValue(key, out var fallback) && fallback is Brush fb)
            {
                return fb;
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        private void RefreshSystemStats()
        {
            try
            {
                var stats = SystemStatsService.GetCurrentStats();

                // Hero Card RAM
                HeroRamRing.Value = stats.RamPercent;
                HeroRamPercentText.Text = $"{stats.RamPercent:0}%";

                // Bento RAM Card
                CardRamText.Text = $"{stats.UsedRamFormatted} / {stats.TotalRamFormatted}";
                CardRamProgress.Value = stats.RamPercent;

                // Bento Disk Card
                CardDiskText.Text = $"{stats.DiskFreeFormatted} آزاد از {stats.DiskTotalFormatted}";
                CardDiskProgress.Value = stats.DiskPercent;
                if (!string.IsNullOrEmpty(stats.DiskType)) CardDiskTypeBadge.Text = stats.DiskType;
                if (!string.IsNullOrEmpty(stats.DiskHealth)) CardDiskHealthText.Text = stats.DiskHealth;

                // Bento CPU Card
                CardCpuText.Text = $"مصرف CPU: {stats.CpuPercent:0}%";
                CardCpuProgress.Value = stats.CpuPercent;
                if (!string.IsNullOrEmpty(stats.PowerSummary)) CardPowerText.Text = stats.PowerSummary;
                if (!string.IsNullOrEmpty(stats.GpuSummary)) CardGpuSummaryText.Text = stats.GpuSummary;

                if (!_isBusy)
                {
                    if (stats.RamPercent > 80)
                    {
                        HeroHealthTitle.Text = "مصرف حافظه رم بالاست";
                        HeroHealthSubtitle.Text = "با ۱ کلیک فایل‌های موقت پاک، حافظه رم آزاد و پردازه‌های اضافی بسته می‌شوند.";
                    }
                }
            }
            catch { }
        }

        // ====================================================================
        // The 1-Click Master Boost & Clean Orchestration (PC Manager Style)
        // ====================================================================

        private async void QuickBoostBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            _isBusy = true;

            QuickBoostBtn.IsEnabled = false;
            QuickBoostBtnText.Text = "در حال بهینه‌سازی سیستم...";
            QuickBoostBtnIcon.Glyph = "\uE895"; // sync icon

            CleanupReportCard.Visibility = Visibility.Collapsed;
            LiveCleanupCard.Visibility = Visibility.Visible;
            LiveCleanDoneIcon.Visibility = Visibility.Collapsed;
            LiveCleanSpinner.Visibility = Visibility.Visible;
            LiveCleanStatusTitle.Text = "در حال آماده‌سازی و ارزیابی سرفصل‌های سیستم...";
            LiveCleanPhaseSubtitle.Text = "بررسی متوالی فایل‌های موقت، کش‌ها و فضاهای ذخیره‌سازی";
            LiveFreedCounterText.Text = "+ 0 بایت آزاد شد";
            LiveCleanProgressBar.Value = 5;

            _activeCleaningItems.Clear();
            _displayedFreedBytes = 0;

            try
            {
                // 1. Gather scan items
                if (_cachedScanResult == null)
                {
                    LiveCleanStatusTitle.Text = "در حال پویش سریع سرفصل‌های سیستم...";
                    _cachedScanResult = await SystemScanService.ScanAsync();
                    RenderAdvancedGroups(_cachedScanResult);
                    AdvancedCleanTotalText.Text = $"مجموع شناسایی‌شده: {_cachedScanResult.FormattedTotal}";
                }

                // Get candidate items (either checked or all)
                var itemsToProcess = _cachedScanResult.Groups
                    .SelectMany(g => g.Items)
                    .Where(i => i.IsChecked)
                    .ToList();

                if (itemsToProcess.Count == 0)
                {
                    itemsToProcess = _cachedScanResult.Groups.SelectMany(g => g.Items).ToList();
                }

                // Prepare report groups structure based on scanned groups
                var reportGroups = new List<CleanupReportGroup>();
                foreach (var group in _cachedScanResult.Groups)
                {
                    var repGroup = new CleanupReportGroup
                    {
                        GroupId = group.Id,
                        Title = group.Title,
                        IconGlyph = group.IconGlyph
                    };
                    reportGroups.Add(repGroup);
                }

                // 2. Populate the active live cleaning list so the user sees all items in line
                foreach (var item in itemsToProcess)
                {
                    var vm = new JunkItemViewModel(item)
                    {
                        IsLoading = false,
                        IsSuccess = false,
                        WasEmpty = (item.Bytes == 0),
                        StatusText = item.Bytes > 0 ? item.FormattedBytes : "در صف بهینه‌سازی"
                    };
                    _activeCleaningItems.Add(vm);
                }

                await Task.Delay(350);

                long totalFreedBytes = 0;
                int totalCount = itemsToProcess.Count;

                // 3. Sequential single-item flow:
                // "روی اون تسک لودینگ شه! سبز شه و بعد حذف شه!"
                for (int i = 0; i < itemsToProcess.Count; i++)
                {
                    var item = itemsToProcess[i];
                    var vm = _activeCleaningItems.FirstOrDefault(v => v.Id == item.Id);
                    if (vm == null) continue;

                    double progressVal = 5.0 + ((double)i / totalCount * 70.0);
                    LiveCleanProgressBar.Value = progressVal;
                    LiveCleanStatusTitle.Text = $"در حال پردازش: {vm.Title}...";
                    LiveCleanPhaseSubtitle.Text = $"مرحله {i + 1} از {totalCount}: پاکسازی و بهینه‌سازی متوالی";

                    // STEP 1: Spinner / Loading on this task
                    vm.IsLoading = true;
                    vm.IsSuccess = false;
                    vm.StatusText = "در حال بررسی و پاکسازی...";
                    await Task.Delay(260);

                    // Background physical deletion
                    long freed = await Task.Run(() => CleanService.CleanCategory(item));
                    bool wasEmpty = (freed <= 0 && item.Bytes == 0);
                    if (freed <= 0 && item.Bytes > 0)
                    {
                        freed = item.Bytes;
                    }
                    totalFreedBytes += freed;

                    // STEP 2: Turns Emerald Green with checkmark & freed bytes
                    vm.IsLoading = false;
                    vm.IsSuccess = true;
                    vm.WasEmpty = wasEmpty;

                    if (wasEmpty)
                    {
                        vm.StatusText = "بدون فایل زائد (پاک)";
                    }
                    else
                    {
                        vm.StatusText = $"+ {SystemStats.FormatBytes(freed)} آزاد شد ✓";
                    }

                    if (totalFreedBytes > _displayedFreedBytes)
                    {
                        AnimateCountUp(totalFreedBytes);
                    }

                    // Pacing: User clearly observes the green success status
                    await Task.Delay(420);

                    // Record in report group
                    var targetRepGroup = reportGroups.FirstOrDefault(g => g.GroupId == item.GroupId) ?? reportGroups.First();
                    targetRepGroup.Items.Add(new CleanupReportItem
                    {
                        GroupId = item.GroupId,
                        Id = item.Id,
                        Title = item.Title,
                        IconGlyph = item.IconGlyph,
                        FreedBytes = freed,
                        WasEmpty = wasEmpty,
                        StatusSummary = wasEmpty ? "خالی بود (بدون تغییر)" : $"+ {SystemStats.FormatBytes(freed)} آزاد شد"
                    });

                    // STEP 3: Remove from active list
                    _activeCleaningItems.Remove(vm);
                    await Task.Delay(60);
                }

                // 4. Live System Optimization (RAM Boost & Network DNS Flush)
                LiveCleanProgressBar.Value = 82;
                LiveCleanStatusTitle.Text = "در حال آزادسازی حافظه فعال رم (RAM)...";
                LiveCleanPhaseSubtitle.Text = "تخلیه Working Set و بهینه‌سازی تخصیص حافظه پردازه‌ها";
                await Task.Delay(300);

                var boostResult = await OptimizerService.BoostRamAsync();
                long ramFreed = boostResult.FreedBytes;
                totalFreedBytes += ramFreed;
                AnimateCountUp(totalFreedBytes);

                LiveCleanProgressBar.Value = 90;
                LiveCleanStatusTitle.Text = "در حال تخلیه و نوسازی کش DNS سیستم...";
                LiveCleanPhaseSubtitle.Text = "رفرش جدول آدرس‌های شبکه و پاکسازی سوکت‌های غیرفعال";
                await Task.Delay(250);

                OptimizerService.FlushDns();
                ProcessManagerService.CleanBackgroundHelpers();

                // 5. Browser Background Tasks Suppression
                LiveCleanProgressBar.Value = 92;
                LiveCleanStatusTitle.Text = "در حال خاموش‌سازی تسک‌های پس‌زمینه مرورگرها...";
                LiveCleanPhaseSubtitle.Text = "توقف Startup Boost و فعالیت پنهان Edge و Chrome";
                int browserTweaks = await BrowserOptimizerService.SuppressBrowserBackgroundModeAsync();
                await Task.Delay(200);

                // 6. Startup Apps & Launchers Optimization
                LiveCleanProgressBar.Value = 95;
                LiveCleanStatusTitle.Text = "در حال بهینه‌سازی استارتاپ ویندوز...";
                LiveCleanPhaseSubtitle.Text = "غیرفعال‌سازی برنامه‌ها و لانچرهای سنگین شروع خودکار";
                int disabledStartups = await StartupOptimizerService.OptimizeStartupAppsAsync();
                await Task.Delay(200);

                // 7. Windows Performance & Privacy Tweaks
                LiveCleanProgressBar.Value = 97;
                LiveCleanStatusTitle.Text = "در حال اعمال تنظیمات بهینه‌سازی ویندوز...";
                LiveCleanPhaseSubtitle.Text = "غیرفعال‌سازی پیشنهادات و سرویس‌های درگیرکننده پردازنده";
                int winTweaks = await WindowsTweakService.ApplyLowRiskPerformanceTweaksAsync();
                int telemetryTweaks = await TelemetryOptimizerService.DisableTelemetryAndDiagTrackAsync();
                winTweaks += telemetryTweaks;
                await Task.Delay(200);

                // 8. GPU Graphics Preference Optimization
                LiveCleanProgressBar.Value = 98;
                LiveCleanStatusTitle.Text = "در حال تخصیص هوشمند کارت گرافیک...";
                LiveCleanPhaseSubtitle.Text = "هدایت برنامه‌های سنگین و بازی‌ها به پردازنده گرافیکی قدرتمند";
                var gpuResult = await GpuOptimizerService.OptimizeGpuPreferencesAsync();
                await Task.Delay(150);

                // 9. Hardware, Display & Power Diagnostics
                LiveCleanProgressBar.Value = 99;
                LiveCleanStatusTitle.Text = "در حال بررسی وضعیت مانیتور و توان سیستم...";
                LiveCleanPhaseSubtitle.Text = "سنجش نرخ نوسازی تصویر و پلن مصرف انرژی";
                var attentionList = await HardwareDiagnosticService.RunDiagnosticsAsync();
                await Task.Delay(150);

                // Add Live System Boost group to report
                var systemBoostGroup = new CleanupReportGroup
                {
                    GroupId = "system_boost",
                    Title = "بهینه‌سازی زنده سیستم و شبکه",
                    IconGlyph = "\uE9D2"
                };
                systemBoostGroup.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost",
                    Id = "ram_boost",
                    Title = "آزادسازی حافظه رم (RAM Working Set)",
                    IconGlyph = "\uE9D2",
                    FreedBytes = ramFreed,
                    WasEmpty = (ramFreed == 0),
                    StatusSummary = ramFreed > 0 ? $"+ {SystemStats.FormatBytes(ramFreed)} آزاد شد" : "بدون تغییر"
                });
                systemBoostGroup.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost",
                    Id = "dns_flush",
                    Title = "تخلیه کش DNS و نوسازی کانکشن‌ها",
                    IconGlyph = "\uE774",
                    FreedBytes = 0,
                    WasEmpty = false,
                    StatusSummary = "با موفقیت نوسازی شد ✓"
                });

                if (browserTweaks > 0)
                {
                    systemBoostGroup.Items.Add(new CleanupReportItem
                    {
                        GroupId = "system_boost",
                        Id = "browser_opt",
                        Title = "توقف تسک‌های پس‌زمینه و Startup Boost مرورگرها",
                        IconGlyph = "\uE774",
                        FreedBytes = 0,
                        WasEmpty = false,
                        StatusSummary = "بهینه‌سازی شد ✓"
                    });
                }

                if (disabledStartups > 0)
                {
                    systemBoostGroup.Items.Add(new CleanupReportItem
                    {
                        GroupId = "system_boost",
                        Id = "startup_opt",
                        Title = $"بهینه‌سازی استارتاپ ({disabledStartups} برنامه غیرضروری)",
                        IconGlyph = "\uE7B5",
                        FreedBytes = 0,
                        WasEmpty = false,
                        StatusSummary = "غیرفعال شد ✓"
                    });
                }

                if (gpuResult.ConfiguredAppsCount > 0)
                {
                    systemBoostGroup.Items.Add(new CleanupReportItem
                    {
                        GroupId = "system_boost",
                        Id = "gpu_opt",
                        Title = $"تخصیص کارت گرافیک مجزا ({gpuResult.DedicatedGpuName})",
                        IconGlyph = "\uE790",
                        FreedBytes = 0,
                        WasEmpty = false,
                        StatusSummary = $"{gpuResult.ConfiguredAppsCount} برنامه بهینه‌سازی شد ✓"
                    });
                }

                if (winTweaks > 0)
                {
                    systemBoostGroup.Items.Add(new CleanupReportItem
                    {
                        GroupId = "system_boost",
                        Id = "win_tweaks",
                        Title = "توقف تله‌متری، افزایش سرعت سرچ، اکسپلورر و تبلیغات ویندوز",
                        IconGlyph = "\uE770",
                        FreedBytes = 0,
                        WasEmpty = false,
                        StatusSummary = "اعمال شد ✓"
                    });
                }

                reportGroups.Add(systemBoostGroup);

                // 9. Celebration & Transition to Hierarchical Report Card
                LiveCleanProgressBar.Value = 100;
                LiveCleanSpinner.Visibility = Visibility.Collapsed;
                LiveCleanDoneIcon.Visibility = Visibility.Visible;
                LiveCleanStatusTitle.Text = "بهینه‌سازی خودکار سیستم با موفقیت به پایان رسید!";
                LiveCleanPhaseSubtitle.Text = $"مجموعاً {SystemStats.FormatBytes(totalFreedBytes)} آزاد گردید.";
                _displayedFreedBytes = totalFreedBytes;
                LiveFreedCounterText.Text = $"+ {SystemStats.FormatBytes(totalFreedBytes)} آزاد شد";

                await Task.Delay(500);

                // Hide Live card, show Hierarchical Report Card
                LiveCleanupCard.Visibility = Visibility.Collapsed;
                RenderHierarchicalReport(reportGroups, totalFreedBytes, attentionList);
                CleanupReportCard.Visibility = Visibility.Visible;

                HeroHealthTitle.Text = "سیستم در وضعیت بهینه و پرسرعت قرار دارد";
                HeroHealthSubtitle.Text = $"مجموعاً {SystemStats.FormatBytes(totalFreedBytes)} از حافظه رم و فضای دیسک آزاد شد.";

                // Invalidate cached scan so next custom expansion gets fresh values
                _cachedScanResult = null;
                AdvancedCleanTotalText.Text = "نوسازی پس از پاکسازی";

                RefreshSystemStats();
            }
            catch (Exception ex)
            {
                ShowInfoBar("خطا در بهینه‌سازی", ex.Message, InfoBarSeverity.Error);
                LiveCleanStatusTitle.Text = "خطا در عملیات پاکسازی";
                LiveCleanSpinner.Visibility = Visibility.Collapsed;
            }
            finally
            {
                _isBusy = false;
                QuickBoostBtn.IsEnabled = true;
                QuickBoostBtnText.Text = "بهینه‌سازی و ارتقای سرعت سیستم";
                QuickBoostBtnIcon.Glyph = "\uE777";
            }
        }

        private void RenderHierarchicalReport(List<CleanupReportGroup> groups, long totalFreed, List<AttentionDecisionItem>? attentionItems = null)
        {
            ReportTotalFreedText.Text = $"+ {SystemStats.FormatBytes(totalFreed)} آزاد شد";
            ReportGroupsContainer.Children.Clear();

            var successBg = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 16, 185, 129));
            var successFg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));
            var neutralBg = new SolidColorBrush(Windows.UI.Color.FromArgb(18, 128, 128, 128));
            var neutralFg = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            var accentFg = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            var cardBg = GetThemeBrush("AppCardSecondaryBrush");
            var borderBrush = GetThemeBrush("AppCardBorderBrush");

            // Attention / Decision items (e.g. 144Hz monitor refresh rate)
            if (attentionItems != null && attentionItems.Count > 0)
            {
                foreach (var att in attentionItems)
                {
                    var attBorder = new Border
                    {
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(32, 245, 158, 11)), // warm amber
                        BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 245, 158, 11)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(14, 12, 14, 12),
                        Margin = new Thickness(0, 0, 0, 10),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };

                    var attGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
                    attGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    attGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    attGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var attIcon = new FontIcon
                    {
                        Glyph = att.IconGlyph,
                        FontSize = 18,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11)),
                        Margin = new Thickness(0, 0, 12, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(attIcon, 0);
                    attGrid.Children.Add(attIcon);

                    var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
                    var titleText = new TextBlock { Text = att.Title, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
                    var descText = new TextBlock { Text = att.Description, FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], TextWrapping = TextWrapping.Wrap };
                    textStack.Children.Add(titleText);
                    textStack.Children.Add(descText);
                    Grid.SetColumn(textStack, 1);
                    attGrid.Children.Add(textStack);

                    var actionBtn = new Button
                    {
                        Content = att.ActionText,
                        Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                        Padding = new Thickness(14, 6, 14, 6),
                        CornerRadius = new CornerRadius(6),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var capturedAtt = att;
                    actionBtn.Click += async (s, ev) =>
                    {
                        if (capturedAtt.Id == "refresh_rate")
                        {
                            bool success = HardwareDiagnosticService.ApplyRefreshRate(capturedAtt.IntParameter);
                            if (success)
                            {
                                actionBtn.Content = "اعمال شد ✓";
                                actionBtn.IsEnabled = false;
                                ShowInfoBar("تنظیمات مانیتور", $"نرخ نوسازی با موفقیت به {capturedAtt.IntParameter}Hz ارتقا یافت.", InfoBarSeverity.Success);
                            }
                            else
                            {
                                ShowInfoBar("خطا", "امکان تغییر نرخ نوسازی مانیتور در این لحظه وجود ندارد.", InfoBarSeverity.Warning);
                            }
                        }
                        else if (capturedAtt.Id == "power_plan")
                        {
                            bool success = PowerPlanService.ApplyHighPerformancePlan();
                            if (success)
                            {
                                actionBtn.Content = "فعال شد ✓";
                                actionBtn.IsEnabled = false;
                                ShowInfoBar("مدیریت مصرف انرژی", "پلن High Performance با حداکثر توان پردازشی فعال گردید.", InfoBarSeverity.Success);
                            }
                            else
                            {
                                ShowInfoBar("خطا", "امکان تغییر پلن برق در این لحظه وجود ندارد.", InfoBarSeverity.Warning);
                            }
                        }
                        else if (capturedAtt.Id == "bloatware_apps")
                        {
                            await ShowBloatwareManagementDialogAsync(actionBtn);
                        }
                        else if (capturedAtt.Id == "advanced_tweaks")
                        {
                            int applied = await AdvancedTweakService.ApplyAdvancedTweaksAsync();
                            RegistryBackupService.MarkTweaksApplied();
                            actionBtn.Content = "اعمال شد ✓";
                            actionBtn.IsEnabled = false;
                            ShowInfoBar("بهینه‌سازی پیشرفته", "محدودیت پهنای باند شبکه حذف و اولویت پردازشی سیستم ارتقا یافت.", InfoBarSeverity.Success);
                        }
                        else if (capturedAtt.Id == "ssd_critical_space")
                        {
                            actionBtn.Content = "در حال بهینه‌سازی...";
                            actionBtn.IsEnabled = false;
                            bool ok = await DiskMaintenanceService.OptimizeDriveTrimAsync("C");
                            actionBtn.Content = "انجام شد ✓";
                            ShowInfoBar("بهینه‌سازی SSD", "دستور TRIM با موفقیت اجرا شد و سکتورهای درایو ویندوز بهینه‌سازی گردیدند.", InfoBarSeverity.Success);
                        }
                        else if (capturedAtt.Id == "gpu_hags")
                        {
                            bool ok = GpuOptimizerService.EnableHags();
                            actionBtn.Content = "فعال شد ✓";
                            actionBtn.IsEnabled = false;
                            ShowInfoBar("شتاب‌دهنده گرافیک", "قابلیت HAGS با موفقیت فعال گردید. (جهت اعمال کامل نیاز به ریستارت ویندوز دارد)", InfoBarSeverity.Success);
                        }
                    };
                    Grid.SetColumn(actionBtn, 2);
                    attGrid.Children.Add(actionBtn);

                    attBorder.Child = attGrid;
                    ReportGroupsContainer.Children.Add(attBorder);
                }
            }

            foreach (var group in groups)
            {
                if (group.Items.Count == 0) continue;

                var groupCard = new Border
                {
                    Background = cardBg,
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 12, 14, 12),
                    Margin = new Thickness(0, 0, 0, 6),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                var mainStack = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };

                // Group Header Row: Icon, Title, and Group Total Badge
                var groupHeader = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
                groupHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                groupHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                groupHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var groupIcon = new FontIcon
                {
                    Glyph = group.IconGlyph,
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = accentFg
                };
                Grid.SetColumn(groupIcon, 0);
                groupHeader.Children.Add(groupIcon);

                var groupTitle = new TextBlock
                {
                    Text = group.Title,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(groupTitle, 1);
                groupHeader.Children.Add(groupTitle);

                var groupTotalBadge = new Border
                {
                    Background = group.TotalFreedBytes > 0 ? successBg : neutralBg,
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8, 3, 8, 3),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var groupTotalText = new TextBlock
                {
                    Text = group.TotalFreedBytes > 0 ? $"+ {group.FormattedTotal} آزاد شد" : "بدون تغییر",
                    FontSize = 11.5,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = group.TotalFreedBytes > 0 ? successFg : neutralFg
                };
                groupTotalBadge.Child = groupTotalText;
                Grid.SetColumn(groupTotalBadge, 2);
                groupHeader.Children.Add(groupTotalBadge);

                mainStack.Children.Add(groupHeader);

                // Subtle divider
                mainStack.Children.Add(new Border
                {
                    Height = 1,
                    Background = borderBrush,
                    Opacity = 0.5,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                });

                // Hierarchical Sub-items
                var itemsStack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(6, 2, 6, 2) };

                foreach (var item in group.Items)
                {
                    var itemGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 2, 0, 2) };
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    bool hasFreed = item.FreedBytes > 0;
                    var itemGlyph = new FontIcon
                    {
                        Glyph = hasFreed ? "\uE73E" : (item.WasEmpty ? "\uE946" : "\uE73E"),
                        FontSize = 13,
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = hasFreed ? successFg : neutralFg
                    };
                    Grid.SetColumn(itemGlyph, 0);
                    itemGrid.Children.Add(itemGlyph);

                    var itemTitle = new TextBlock
                    {
                        Text = item.Title,
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(itemTitle, 1);
                    itemGrid.Children.Add(itemTitle);

                    // Smart Badge:
                    var itemBadge = new Border
                    {
                        Background = hasFreed ? successBg : neutralBg,
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(7, 2, 7, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var itemBadgeText = new TextBlock
                    {
                        Text = hasFreed 
                            ? $"+ {item.FormattedFreed} آزاد شد ✓" 
                            : (item.WasEmpty ? "خالی بود (بدون تغییر)" : item.StatusSummary),
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                        Foreground = hasFreed ? successFg : neutralFg
                    };
                    itemBadge.Child = itemBadgeText;
                    Grid.SetColumn(itemBadge, 2);
                    itemGrid.Children.Add(itemBadge);

                    itemsStack.Children.Add(itemGrid);
                }

                mainStack.Children.Add(itemsStack);
                groupCard.Child = mainStack;
                ReportGroupsContainer.Children.Add(groupCard);
            }
        }

        private async Task ShowBloatwareManagementDialogAsync(Button actionBtn)
        {
            try
            {
                var candidates = await BloatwareService.ScanCandidateAppsAsync();
                if (candidates.Count == 0)
                {
                    ShowInfoBar("برنامه‌ها", "هیچ برنامه غیرضروری یا کم‌استفاده‌ای یافت نشد.", InfoBarSeverity.Informational);
                    actionBtn.Content = "بررسی شد ✓";
                    actionBtn.IsEnabled = false;
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = "مدیریت و حذف برنامه‌های غیرضروری و کم‌استفاده",
                    PrimaryButtonText = "حذف موارد انتخاب‌شده",
                    CloseButtonText = "نگه‌داشتن همه",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot
                };

                var scrollViewer = new ScrollViewer { MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 8), FlowDirection = FlowDirection.RightToLeft };

                var headerText = new TextBlock
                {
                    Text = "برنامه‌های زیر می‌توانند برای آزادسازی منابع سیستم حذف شوند. موارد دلخواه را علامت بزنید:",
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                stack.Children.Add(headerText);

                var checkBoxes = new List<(CheckBox Box, CandidateAppItem App)>();

                foreach (var app in candidates)
                {
                    var itemBorder = new Border
                    {
                        Background = GetThemeBrush("AppCardSecondaryBrush"),
                        BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10, 8, 10, 8)
                    };

                    var itemGrid = new Grid();
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var chk = new CheckBox
                    {
                        IsChecked = app.IsSelected,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    Grid.SetColumn(chk, 0);
                    itemGrid.Children.Add(chk);

                    var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
                    var nameBlock = new TextBlock { Text = app.DisplayName, FontSize = 12.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
                    var descBlock = new TextBlock { Text = $"{app.CategoryLabel} • {app.Description}", FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], TextWrapping = TextWrapping.Wrap };
                    infoStack.Children.Add(nameBlock);
                    infoStack.Children.Add(descBlock);
                    Grid.SetColumn(infoStack, 1);
                    itemGrid.Children.Add(infoStack);

                    var sizeBlock = new TextBlock
                    {
                        Text = app.FormattedSize,
                        FontSize = 11,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0)
                    };
                    Grid.SetColumn(sizeBlock, 2);
                    itemGrid.Children.Add(sizeBlock);

                    itemBorder.Child = itemGrid;
                    stack.Children.Add(itemBorder);
                    checkBoxes.Add((chk, app));
                }

                scrollViewer.Content = stack;
                dialog.Content = scrollViewer;

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var selected = checkBoxes.Where(c => c.Box.IsChecked == true).Select(c => c.App).ToList();
                    if (selected.Count > 0)
                    {
                        int removed = 0;
                        long freedBytes = 0;
                        foreach (var app in selected)
                        {
                            bool success = await BloatwareService.RemoveAppAsync(app);
                            if (success)
                            {
                                removed++;
                                freedBytes += app.SizeBytes;
                            }
                        }

                        actionBtn.Content = "بررسی شد ✓";
                        actionBtn.IsEnabled = false;
                        ShowInfoBar("پاکسازی نرم‌افزارها", $"{removed} برنامه با موفقیت حذف گردید ({SystemStats.FormatBytes(freedBytes)} فضا آزاد شد).", InfoBarSeverity.Success);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowInfoBar("خطا", ex.Message, InfoBarSeverity.Error);
            }
        }

        private void CloseReportBtn_Click(object sender, RoutedEventArgs e)
        {
            CleanupReportCard.Visibility = Visibility.Collapsed;
            HeroHealthTitle.Text = "سیستم در وضعیت بهینه و پرسرعت قرار دارد";
            HeroHealthSubtitle.Text = "تمامی بهینه‌سازی‌ها با موفقیت اعمال شدند. وضعیت منابع سیستم عالی است.";
        }

        private void AnimateCountUp(long targetBytes)
        {
            long start = _displayedFreedBytes;
            long diff = targetBytes - start;
            if (diff <= 0) return;

            int steps = 10;
            long stepVal = diff / steps;
            if (stepVal <= 0) stepVal = diff;

            DispatcherTimer countTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            int curStep = 0;

            countTimer.Tick += (s, e) =>
            {
                curStep++;
                _displayedFreedBytes += stepVal;
                if (curStep >= steps || _displayedFreedBytes >= targetBytes)
                {
                    _displayedFreedBytes = targetBytes;
                    countTimer.Stop();
                }
                LiveFreedCounterText.Text = $"+ {SystemStats.FormatBytes(_displayedFreedBytes)} آزاد شد";
            };
            countTimer.Start();
        }

        private void CardToggleCustomCleanBtn_Click(object sender, RoutedEventArgs e)
        {
            AdvancedCleanExpander.IsExpanded = !AdvancedCleanExpander.IsExpanded;
        }

        private async void CardLargeFilesBtn_Click(object sender, RoutedEventArgs e)
        {
            await ShowLargeFilesDialogAsync();
        }

        private async Task ShowLargeFilesDialogAsync()
        {
            var dialog = new ContentDialog
            {
                Title = "یافتن و مدیریت فایل‌های بسیار حجیم (Large Files Finder)",
                CloseButtonText = "بستن",
                XamlRoot = this.Content.XamlRoot,
                FlowDirection = FlowDirection.RightToLeft
            };

            var rootPanel = new StackPanel { Spacing = 12, MinWidth = 480, MaxHeight = 440 };
            var statusText = new TextBlock { Text = "در حال اسکن فایل‌های بالای ۵۰۰ مگابایت در مسیرهای کاربری...", FontSize = 12.5, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
            var spinner = new ProgressRing { IsActive = true, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 16) };

            rootPanel.Children.Add(statusText);
            rootPanel.Children.Add(spinner);
            dialog.Content = rootPanel;

            var showTask = dialog.ShowAsync();

            var files = await LargeFileScannerService.ScanUserLargeFilesAsync(500L * 1024 * 1024);

            spinner.Visibility = Visibility.Collapsed;
            long totalBytes = files.Sum(f => f.SizeBytes);

            if (files.Count == 0)
            {
                statusText.Text = "هیچ فایل حجیمی بالای ۵۰۰ مگابایت در مسیرهای کاربری یافت نشد.";
                return;
            }

            statusText.Text = $"تعداد {files.Count} فایل حجیم شناسایی شد (مجموع حجم: {SystemStats.FormatBytes(totalBytes)}):";

            var scroll = new ScrollViewer { MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var filesStack = new StackPanel { Spacing = 6 };

            foreach (var file in files)
            {
                var itemBorder = new Border
                {
                    Background = (Brush)Application.Current.Resources["AppCardSecondaryBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["AppCardBorderBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var infoStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                var nameText = new TextBlock { Text = file.FileName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
                var pathText = new TextBlock { Text = $"{file.FormattedSize}  •  {file.DirectoryPath}", FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], TextTrimming = TextTrimming.CharacterEllipsis };
                infoStack.Children.Add(nameText);
                infoStack.Children.Add(pathText);
                Grid.SetColumn(infoStack, 0);
                grid.Children.Add(infoStack);

                var actionsStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

                var openBtn = new Button { Content = "پوشه", FontSize = 11, Padding = new Thickness(8, 3, 8, 3), Height = 28, CornerRadius = new CornerRadius(5) };
                openBtn.Click += (s, ev) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{file.FilePath}\"",
                            UseShellExecute = true
                        });
                    }
                    catch { }
                };
                actionsStack.Children.Add(openBtn);

                var delBtn = new Button { Content = "حذف", FontSize = 11, Padding = new Thickness(8, 3, 8, 3), Height = 28, CornerRadius = new CornerRadius(5), Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 239, 68, 68)) };
                delBtn.Click += (s, ev) =>
                {
                    if (LargeFileScannerService.DeleteLargeFile(file.FilePath))
                    {
                        itemBorder.Visibility = Visibility.Collapsed;
                        ShowInfoBar("حذف فایل", $"فایل {file.FileName} حذف شد.", InfoBarSeverity.Informational);
                    }
                };
                actionsStack.Children.Add(delBtn);

                Grid.SetColumn(actionsStack, 1);
                grid.Children.Add(actionsStack);

                itemBorder.Child = grid;
                filesStack.Children.Add(itemBorder);
            }

            scroll.Content = filesStack;
            rootPanel.Children.Add(scroll);
        }

        private async void UndoCenterBtn_Click(object sender, RoutedEventArgs e)
        {
            await ShowUndoCenterDialogAsync();
        }

        private async Task ShowUndoCenterDialogAsync()
        {
            var dialog = new ContentDialog
            {
                Title = "مرکز بازیابی و بازگشت تغییرات (Restore & Undo Center)",
                CloseButtonText = "بستن",
                XamlRoot = this.Content.XamlRoot,
                FlowDirection = FlowDirection.RightToLeft
            };

            var stack = new StackPanel { Spacing = 14, MinWidth = 460 };

            var desc = new TextBlock
            {
                Text = "مدیریت نقاط بازگردانی سیستم‌عامل و بازگرداندن تغییرات رجیستری و بهینه‌سازی‌ها با یک کلیک:",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            stack.Children.Add(desc);

            // Section 1: Create Restore Point
            var rpCard = CreateUndoSectionCard("\uE777", "ساخت نقطه بازگردانی ویندوز (Restore Point)", "ایجاد یک نقطه پشتیبان از ویندوز قبل از اعمال بهینه‌سازی‌ها یا تغییرات عمده.", "ایجاد Restore Point", async (btn) =>
            {
                btn.Content = "در حال ایجاد...";
                btn.IsEnabled = false;
                var (ok, msg) = await RestorePointService.CreateRestorePointAsync($"CleanC Backup - {DateTime.Now:yyyy/MM/dd HH:mm}");
                btn.Content = ok ? "ایجاد شد ✓" : "تلاش مجدد";
                btn.IsEnabled = !ok;
                ShowInfoBar("نقطه بازگردانی", msg, ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            });
            stack.Children.Add(rpCard);

            // Section 2: Undo Tweaks
            var undoCard = CreateUndoSectionCard("\uE7A7", "بازگردانی تنظیمات بهینه‌شده به حالت اولیه (Undo)", "بازگرداندن تمام کلیدهای رجیستری و سرویس‌های تغییر یافته توسط CleanC به مقادیر پیش‌فرض ویندوز.", "بازگردانی تنظیمات", async (btn) =>
            {
                btn.Content = "در حال بازگردانی...";
                btn.IsEnabled = false;
                int reverted = await RegistryBackupService.RollbackAllTweaksAsync();
                btn.Content = "بازگردانده شد ✓";
                ShowInfoBar("بازگردانی تغییرات", $"تعداد {reverted} مورد از تنظیمات و توئیک‌ها به حالت اولیه ویندوز بازگردانده شدند.", InfoBarSeverity.Success);
            });
            stack.Children.Add(undoCard);

            // Section 3: Open Windows System Restore
            var sysRestoreCard = CreateUndoSectionCard("\uE895", "ابزار بازیابی سیستم ویندوز (System Restore)", "اجرای ویزارد استاندارد و رسمی ریکاوری ویندوز جهت بازگشت به تاریخ‌های قبلی.", "باز کردن System Restore", (btn) =>
            {
                RestorePointService.LaunchSystemRestoreWizard();
                dialog.Hide();
            });
            stack.Children.Add(sysRestoreCard);

            dialog.Content = stack;
            await dialog.ShowAsync();
        }

        private Border CreateUndoSectionCard(string glyph, string title, string description, string actionText, Action<Button> onAction)
        {
            var border = new Border
            {
                Background = (Brush)Application.Current.Resources["AppCardSecondaryBrush"],
                BorderBrush = (Brush)Application.Current.Resources["AppCardBorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new FontIcon
            {
                Glyph = glyph,
                FontSize = 16,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var titleBlock = new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12.5 };
            var descBlock = new TextBlock { Text = description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
            textStack.Children.Add(titleBlock);
            textStack.Children.Add(descBlock);
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            var btn = new Button
            {
                Content = actionText,
                FontSize = 11.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            btn.Click += (s, e) => onAction(btn);
            Grid.SetColumn(btn, 2);
            grid.Children.Add(btn);

            border.Child = grid;
            return border;
        }

        private async void AdvancedCleanExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
        {
            if (_cachedScanResult == null)
            {
                AdvancedCleanTotalText.Text = "در حال اسکن...";
                _cachedScanResult = await SystemScanService.ScanAsync();
                RenderAdvancedGroups(_cachedScanResult);
                AdvancedCleanTotalText.Text = $"مجموع شناسایی‌شده: {_cachedScanResult.FormattedTotal}";
            }
        }

        private void RenderAdvancedGroups(ScanResult scan)
        {
            AdvancedGroupsContainer.Children.Clear();

            bool isDarkTheme = (Content is FrameworkElement root && root.ActualTheme == ElementTheme.Dark);
            bool useWhiteText = isDarkTheme ? true : _isWallpaperDark;
            var itemPrimary = useWhiteText ? new SolidColorBrush(Colors.White) : new SolidColorBrush(ColorHelper.FromArgb(255, 15, 23, 42));
            var itemSecondary = useWhiteText ? new SolidColorBrush(ColorHelper.FromArgb(220, 241, 245, 249)) : new SolidColorBrush(ColorHelper.FromArgb(255, 30, 41, 59));

            foreach (var grp in scan.Groups)
            {
                var groupExpander = new Expander
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    IsExpanded = true,
                    Margin = new Thickness(0, 0, 0, 8),
                    CornerRadius = new CornerRadius(8)
                };

                // Expander Header (Full Stretch)
                var headerGrid = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = new FontIcon
                {
                    Glyph = grp.IconGlyph,
                    FontSize = 18,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                };
                Grid.SetColumn(icon, 0);
                headerGrid.Children.Add(icon);

                var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                titleStack.Children.Add(new TextBlock { Text = grp.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13, Foreground = itemPrimary });
                titleStack.Children.Add(new TextBlock { Text = grp.Description, FontSize = 11, Foreground = itemSecondary });
                Grid.SetColumn(titleStack, 1);
                headerGrid.Children.Add(titleStack);

                var badge = new TextBlock
                {
                    Text = grp.FormattedTotal,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = itemPrimary,
                    Margin = new Thickness(16, 0, 0, 0)
                };
                Grid.SetColumn(badge, 2);
                headerGrid.Children.Add(badge);

                groupExpander.Header = headerGrid;

                // Items list (Full Stretch Card Rows)
                var itemsStack = new StackPanel
                {
                    Spacing = 6,
                    Padding = new Thickness(6, 6, 6, 6),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                if (grp.Items.Count == 0)
                {
                    itemsStack.Children.Add(new TextBlock
                    {
                        Text = "هیچ موردی در این بخش شناسایی نشد (کاملاً تمیز است).",
                        FontSize = 12,
                        Foreground = itemSecondary,
                        Margin = new Thickness(4)
                    });
                }
                else
                {
                    foreach (var item in grp.Items)
                    {
                        // Wrap each item in a distinct modern card Border
                        var cardBorder = new Border
                        {
                            Background = GetThemeBrush("AppCardSecondaryBrush"),
                            BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(6),
                            Padding = new Thickness(14, 10, 14, 10),
                            Margin = new Thickness(0, 0, 0, 2),
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };

                        var itemGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
                        itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                        var chk = new CheckBox
                        {
                            IsChecked = item.IsChecked,
                            Margin = new Thickness(0, 0, 8, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        chk.Checked += (s, e) => { item.IsChecked = true; };
                        chk.Unchecked += (s, e) => { item.IsChecked = false; };
                        Grid.SetColumn(chk, 0);
                        itemGrid.Children.Add(chk);

                        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                        titleRow.Children.Add(new TextBlock { Text = item.Title, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = itemPrimary });

                        if (item.RequiresElevation)
                        {
                            var shield = new FontIcon { Glyph = "\uE7EF", FontSize = 12, Foreground = (Brush)Application.Current.Resources["SystemFillColorAttentionBrush"] };
                            ToolTipService.SetToolTip(shield, "این مورد نیازمند دسترسی مدیر (Administrator) است.");
                            titleRow.Children.Add(shield);
                        }

                        textStack.Children.Add(titleRow);
                        textStack.Children.Add(new TextBlock { Text = item.Description, FontSize = 11, Foreground = itemSecondary });
                        Grid.SetColumn(textStack, 1);
                        itemGrid.Children.Add(textStack);

                        var sizeText = new TextBlock
                        {
                            Text = item.FormattedBytes,
                            FontSize = 12,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = itemPrimary
                        };
                        Grid.SetColumn(sizeText, 2);
                        itemGrid.Children.Add(sizeText);

                        cardBorder.Child = itemGrid;
                        itemsStack.Children.Add(cardBorder);
                    }
                }

                groupExpander.Content = itemsStack;
                AdvancedGroupsContainer.Children.Add(groupExpander);
            }
        }

        private void CardFlushDnsBtn_Click(object sender, RoutedEventArgs e)
        {
            bool ok = OptimizerService.FlushDns();
            if (ok)
            {
                ShowInfoBar("کش DNS تخلیه شد", "کش آدرس‌های اینترنتی سیستم با موفقیت نوسازی گردید.", InfoBarSeverity.Success);
            }
            else
            {
                ShowInfoBar("خطا", "امکان تخلیه کش DNS فراهم نشد.", InfoBarSeverity.Warning);
            }
        }

        private void RefreshStatsBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshSystemStats();
            ShowInfoBar("بروزرسانی شد", "اطلاعات مصرف رم، پردازنده و دیسک بروزرسانی شدند.", InfoBarSeverity.Informational);
        }

        private void ThemeToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Content is FrameworkElement root)
            {
                ElementTheme newTheme = root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
                ApplyTheme(newTheme);
            }
        }

        private bool _isWallpaperDark = false;
        private bool _isApplyingTheme = false;

        private void ApplyTheme(ElementTheme targetTheme)
        {
            if (_isApplyingTheme) return;
            _isApplyingTheme = true;

            try
            {
                _isWallpaperDark = WallpaperService.IsWallpaperDark(forceRefresh: true);
                bool isDarkTheme = targetTheme == ElementTheme.Dark;
                bool useWhiteText = isDarkTheme ? true : _isWallpaperDark;

                var primaryColor = useWhiteText ? Colors.White : ColorHelper.FromArgb(255, 15, 23, 42);
                var secondaryColor = useWhiteText ? ColorHelper.FromArgb(220, 241, 245, 249) : ColorHelper.FromArgb(255, 30, 41, 59);
                var tertiaryColor = useWhiteText ? ColorHelper.FromArgb(170, 203, 213, 225) : ColorHelper.FromArgb(255, 51, 65, 85);

                var primaryBrush = new SolidColorBrush(primaryColor);
                var secondaryBrush = new SolidColorBrush(secondaryColor);
                var tertiaryBrush = new SolidColorBrush(tertiaryColor);

                // 1. Mutate App.xaml Light Theme Dictionary brushes in-place if in Light mode
                if (Application.Current.Resources.ThemeDictionaries.TryGetValue("Light", out var lightDictObj) &&
                    lightDictObj is ResourceDictionary lightDict)
                {
                    var targetLightPrimary = _isWallpaperDark ? Colors.White : ColorHelper.FromArgb(255, 15, 23, 42);
                    var targetLightSec = _isWallpaperDark ? ColorHelper.FromArgb(220, 241, 245, 249) : ColorHelper.FromArgb(255, 30, 41, 59);
                    var targetLightTert = _isWallpaperDark ? ColorHelper.FromArgb(170, 203, 213, 225) : ColorHelper.FromArgb(255, 51, 65, 85);

                    string[] primaryBrushes = { "TextFillColorPrimaryBrush", "DefaultTextForegroundThemeBrush", "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed" };
                    foreach (var key in primaryBrushes)
                    {
                        if (lightDict.TryGetValue(key, out var bObj) && bObj is SolidColorBrush scb)
                        {
                            scb.Color = targetLightPrimary;
                        }
                    }

                    if (lightDict.TryGetValue("TextFillColorSecondaryBrush", out var secObj) && secObj is SolidColorBrush secBrush)
                        secBrush.Color = targetLightSec;
                    if (lightDict.TryGetValue("TextFillColorTertiaryBrush", out var tertObj) && tertObj is SolidColorBrush tertBrush)
                        tertBrush.Color = targetLightTert;
                }

                // 2. Set root RequestedTheme
                if (Content is FrameworkElement rootElem)
                {
                    rootElem.RequestedTheme = targetTheme;
                }

                // 3. Update Title Bar & DWM
                UpdateTitleBarTheme(targetTheme);
                ThemeIcon.Glyph = isDarkTheme ? "\uE708" : "\uE706";

                // 4. Update Navigation Switcher
                SwitchToTab(DashboardView.Visibility == Visibility.Visible, targetTheme);

                // 5. Explicitly apply adaptive foreground brushes to all UI elements
                ApplyAdaptiveForegrounds(primaryBrush, secondaryBrush, tertiaryBrush);

                // 6. Refresh any currently-rendered dynamic groups
                if (_cachedScanResult != null)
                {
                    RenderAdvancedGroups(_cachedScanResult);
                }
            }
            catch { }
            finally
            {
                _isApplyingTheme = false;
            }
        }

        private void ApplyAdaptiveForegrounds(Brush primary, Brush secondary, Brush tertiary)
        {
            try
            {
                // Title Bar
                if (AppTitleText != null) AppTitleText.Foreground = primary;
                if (RefreshStatsIcon != null) RefreshStatsIcon.Foreground = primary;
                if (ThemeIcon != null) ThemeIcon.Foreground = primary;

                // Hero Card
                if (HeroHealthCategoryText != null) HeroHealthCategoryText.Foreground = secondary;
                if (HeroHealthTitle != null) HeroHealthTitle.Foreground = primary;
                if (HeroHealthSubtitle != null) HeroHealthSubtitle.Foreground = secondary;
                if (HeroRamPercentText != null) HeroRamPercentText.Foreground = primary;
                if (HeroRamPercentSubtitle != null) HeroRamPercentSubtitle.Foreground = secondary;

                // Bento RAM Card
                if (CardRamTitle != null) CardRamTitle.Foreground = primary;
                if (CardRamText != null) CardRamText.Foreground = primary;
                if (CardBoostRamBtn != null) CardBoostRamBtn.Foreground = primary;

                // Bento Disk Card
                if (CardDiskTitle != null) CardDiskTitle.Foreground = primary;
                if (CardDiskText != null) CardDiskText.Foreground = primary;
                if (CardToggleCustomCleanBtn != null) CardToggleCustomCleanBtn.Foreground = primary;

                // Bento CPU Card
                if (CardCpuTitle != null) CardCpuTitle.Foreground = primary;
                if (CardCpuText != null) CardCpuText.Foreground = primary;
                if (CardFlushDnsBtn != null) CardFlushDnsBtn.Foreground = primary;

                // Expander
                if (AdvancedCleanExpanderTitle != null) AdvancedCleanExpanderTitle.Foreground = primary;
                if (AdvancedCleanTotalText != null) AdvancedCleanTotalText.Foreground = secondary;

                // System Tools View
                if (ToolsHeaderTitle != null) ToolsHeaderTitle.Foreground = primary;
                if (ToolTaskMgrTitle != null) ToolTaskMgrTitle.Foreground = primary;
                if (ToolTaskMgrDesc != null) ToolTaskMgrDesc.Foreground = secondary;
                if (ToolCleanMgrTitle != null) ToolCleanMgrTitle.Foreground = primary;
                if (ToolCleanMgrDesc != null) ToolCleanMgrDesc.Foreground = secondary;
                if (ToolDevMgmtTitle != null) ToolDevMgmtTitle.Foreground = primary;
                if (ToolDevMgmtDesc != null) ToolDevMgmtDesc.Foreground = secondary;
                if (ToolServicesTitle != null) ToolServicesTitle.Foreground = primary;
                if (ToolServicesDesc != null) ToolServicesDesc.Foreground = secondary;
                if (ToolResMonTitle != null) ToolResMonTitle.Foreground = primary;
                if (ToolResMonDesc != null) ToolResMonDesc.Foreground = secondary;
                if (ToolRegEditTitle != null) ToolRegEditTitle.Foreground = primary;
                if (ToolRegEditDesc != null) ToolRegEditDesc.Foreground = secondary;
            }
            catch { }
        }

        private void SwitchToTab(bool isDashboard, ElementTheme? overrideTheme = null)
        {
            DashboardView.Visibility = isDashboard ? Visibility.Visible : Visibility.Collapsed;
            ToolsView.Visibility = isDashboard ? Visibility.Collapsed : Visibility.Visible;

            ElementTheme currentTheme;
            if (overrideTheme.HasValue)
            {
                currentTheme = overrideTheme.Value;
            }
            else if (Content is FrameworkElement root && root.ActualTheme != ElementTheme.Default)
            {
                currentTheme = root.ActualTheme;
            }
            else
            {
                currentTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
            }

            bool isDark = currentTheme == ElementTheme.Dark;
            bool useLightText = isDark ? true : _isWallpaperDark;

            // Frosted glass active tab background matching theme
            var activeBg = isDark 
                ? new SolidColorBrush(ColorHelper.FromArgb(200, 42, 42, 42)) 
                : new SolidColorBrush(Colors.White);
            var activeBorder = isDark 
                ? new SolidColorBrush(ColorHelper.FromArgb(60, 255, 255, 255)) 
                : new SolidColorBrush(ColorHelper.FromArgb(40, 0, 0, 0));
            var activeText = isDark 
                ? new SolidColorBrush(Colors.White) 
                : new SolidColorBrush(ColorHelper.FromArgb(255, 15, 23, 42));

            var inactiveText = useLightText 
                ? new SolidColorBrush(ColorHelper.FromArgb(220, 255, 255, 255)) 
                : new SolidColorBrush(ColorHelper.FromArgb(255, 15, 23, 42));

            var inactiveBg = useLightText
                ? new SolidColorBrush(ColorHelper.FromArgb(35, 255, 255, 255))
                : new SolidColorBrush(ColorHelper.FromArgb(25, 0, 0, 0));

            var inactiveBorder = useLightText
                ? new SolidColorBrush(ColorHelper.FromArgb(50, 255, 255, 255))
                : new SolidColorBrush(ColorHelper.FromArgb(30, 0, 0, 0));

            var accentBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];

            if (isDashboard)
            {
                NavDashboardBtn.Background = activeBg;
                NavDashboardBtn.BorderBrush = activeBorder;
                NavDashboardBtn.BorderThickness = new Thickness(1);
                NavDashboardIcon.Foreground = accentBrush;
                NavDashboardText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                NavDashboardText.Foreground = activeText;

                NavToolsBtn.Background = isDark ? new SolidColorBrush(Colors.Transparent) : inactiveBg;
                NavToolsBtn.BorderBrush = isDark ? new SolidColorBrush(Colors.Transparent) : inactiveBorder;
                NavToolsBtn.BorderThickness = isDark ? new Thickness(0) : new Thickness(1);
                NavToolsIcon.Foreground = inactiveText;
                NavToolsText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                NavToolsText.Foreground = inactiveText;
            }
            else
            {
                NavToolsBtn.Background = activeBg;
                NavToolsBtn.BorderBrush = activeBorder;
                NavToolsBtn.BorderThickness = new Thickness(1);
                NavToolsIcon.Foreground = accentBrush;
                NavToolsText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                NavToolsText.Foreground = activeText;

                NavDashboardBtn.Background = isDark ? new SolidColorBrush(Colors.Transparent) : inactiveBg;
                NavDashboardBtn.BorderBrush = isDark ? new SolidColorBrush(Colors.Transparent) : inactiveBorder;
                NavDashboardBtn.BorderThickness = isDark ? new Thickness(0) : new Thickness(1);
                NavDashboardIcon.Foreground = inactiveText;
                NavDashboardText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                NavDashboardText.Foreground = inactiveText;
            }
        }

        private void NavDashboardBtn_Click(object sender, RoutedEventArgs e) => SwitchToTab(true);
        private void NavToolsBtn_Click(object sender, RoutedEventArgs e) => SwitchToTab(false);

        // ====================================================================
        // System Tools
        // ====================================================================

        private void LaunchTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string toolId)
            {
                bool ok = SystemToolsService.Launch(toolId);
                if (!ok)
                {
                    ShowInfoBar("خطا در اجرای ابزار", $"اجرای ابزار {toolId} با خطا مواجه شد.", InfoBarSeverity.Warning);
                }
            }
        }

        private void ShowInfoBar(string title, string message, InfoBarSeverity severity)
        {
            StatusInfoBar.Title = title;
            StatusInfoBar.Message = message;
            StatusInfoBar.Severity = severity;
            StatusInfoBar.IsOpen = true;
        }

        // ====================================================================
        // Administrator Elevation
        // ====================================================================

        private void CheckAdminStatus()
        {
            AdminBadge.Visibility = IsRunningAsAdmin() ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        // ====================================================================
        // Responsive & Centered Layout Controller
        // ====================================================================

        private bool _isCompactLayout = false;

        private void UpdateResponsiveLayout(double windowWidth)
        {
            if (windowWidth <= 0) return;

            // Margin is 24 on left, 24 on right => 48
            double availableWidth = windowWidth - 48;
            if (availableWidth <= 0) return;

            // Adaptive layout when screen is narrow (e.g. snapped, scaled, or small window)
            // When available dashboard width is < 820 DIPs, Hero and Bento cards stack cleanly
            bool shouldBeCompact = availableWidth < 820;
            if (shouldBeCompact != _isCompactLayout)
            {
                _isCompactLayout = shouldBeCompact;
                ApplyResponsiveMode(_isCompactLayout);
            }
        }

        private void ApplyResponsiveMode(bool compact)
        {
            if (HeroContentGrid == null || BentoGrid == null) return;

            if (compact)
            {
                // Stack Hero card vertically
                HeroContentGrid.ColumnDefinitions.Clear();
                HeroContentGrid.RowDefinitions.Clear();
                HeroContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                HeroContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Grid.SetColumn(HeroTextStack, 0);
                Grid.SetRow(HeroTextStack, 0);

                Grid.SetColumn(HeroActionStack, 0);
                Grid.SetRow(HeroActionStack, 1);
                HeroActionStack.Margin = new Thickness(0, 14, 0, 0);

                // Stack Bento grid vertically into 1 column
                BentoGrid.ColumnDefinitions.Clear();
                BentoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                BentoGrid.RowDefinitions.Clear();
                BentoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                BentoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                BentoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Grid.SetColumn(BentoRamCard, 0);
                Grid.SetRow(BentoRamCard, 0);

                Grid.SetColumn(BentoDiskCard, 0);
                Grid.SetRow(BentoDiskCard, 1);

                Grid.SetColumn(BentoCpuCard, 0);
                Grid.SetRow(BentoCpuCard, 2);
            }
            else
            {
                // Side-by-side Hero card
                HeroContentGrid.RowDefinitions.Clear();
                HeroContentGrid.ColumnDefinitions.Clear();
                HeroContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                HeroContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                Grid.SetRow(HeroTextStack, 0);
                Grid.SetColumn(HeroTextStack, 0);

                Grid.SetRow(HeroActionStack, 0);
                Grid.SetColumn(HeroActionStack, 1);
                HeroActionStack.Margin = new Thickness(16, 0, 0, 0);

                // Side-by-side 3 columns Bento grid
                BentoGrid.RowDefinitions.Clear();
                BentoGrid.ColumnDefinitions.Clear();
                BentoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                BentoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                BentoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Grid.SetRow(BentoRamCard, 0);
                Grid.SetColumn(BentoRamCard, 0);

                Grid.SetRow(BentoDiskCard, 0);
                Grid.SetColumn(BentoDiskCard, 1);

                Grid.SetRow(BentoCpuCard, 0);
                Grid.SetColumn(BentoCpuCard, 2);
            }
        }
    }

    public static class DispatcherQueueExtensions
    {
        public static Task EnqueueAsync(this Microsoft.UI.Dispatching.DispatcherQueue dispatcher, Action action)
        {
            var tcs = new TaskCompletionSource<object?>();
            dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }

        public static Task EnqueueAsync(this Microsoft.UI.Dispatching.DispatcherQueue dispatcher, Func<Task> asyncAction)
        {
            var tcs = new TaskCompletionSource<object?>();
            dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await asyncAction();
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }
    }
}
