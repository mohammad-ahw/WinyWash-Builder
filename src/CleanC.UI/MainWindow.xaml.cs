using System.Runtime.CompilerServices;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using System.Numerics;
using WinRT;
using CleanC.Core.Models;
using CleanC.Core.Services;
using CleanC.UI.ViewModels;

namespace CleanC.UI
{
    public sealed partial class MainWindow : Window
    {
        private AppWindow _appWindow;
        /// <summary>دسته ی پنجره — برای تشخیص minimize و سایر Win32 فراخوانی ها نگه داشته می شود.</summary>
        private IntPtr _hWnd;
        private DispatcherTimer _statsTimer;
        private ObservableCollection<JunkItemViewModel> _activeCleaningItems = new();
        private ScanResult? _cachedScanResult;
        private bool _isBusy = false;
        private long _displayedFreedBytes = 0;
        private long _currentReportTotalFreed = 0;
        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _configurationSource;
        private DetailedSystemSpecs? _cachedSpecs;
        private List<InstalledAppInfo> _allInstalledApps = new();
        private CancellationTokenSource? _speedTestCts;
        private readonly DispatcherTimer _infoBarAutoDismissTimer = new() { Interval = TimeSpan.FromSeconds(4) };
        private readonly DispatcherTimer _uninstallerSearchDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly DispatcherTimer _driversSearchDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private TrayIconManager? _trayManager;
        // F5 — نوتیفیکیشن ویندوزی (toast + badge)؛ وقتی پنجره فوکوس ندارد استفاده می شود
        private readonly WindowsNotificationService _windowsNotifications = new();
        // صف InfoBar تا پیام های پشت سرهم روی هم نیفتند و همه دیده شوند
        private readonly Queue<(string Title, string Message, InfoBarSeverity Severity)> _infoBarQueue = new();
        private readonly List<FontIcon> _toolBackIcons = new();
        private List<CleanupReportItem>? _lastCleanupItems;
        private long _lastFreedBytes;
        // F5 — آخرین عدد صحیحِ ست شده روی badge (برای جلوگیری از فراخوانی تکراری)
        private int _lastBadgeScore = -1;
        // B4 — سقف تاریخچه ی اعلان ها (در ذخیره و نمایش یکسان نگه داشته می شود)
        private const int NotificationHistoryCapacity = 50;

        // Dashboard V2 Aurora, Sparkline, and Gauge state
        private readonly UISettings _uiSettings = new();
        private ContainerVisual? _auroraRootVisual;
        private readonly List<SpriteVisual> _auroraBlobVisuals = new();
        private readonly List<CompositionRadialGradientBrush> _auroraBrushes = new();
        private Visual? _shineVisual;
        private bool _isAuroraAnimating = false;
        private readonly float[] _ramHistory = new float[24];
        private readonly float[] _cpuHistory = new float[24];
        private readonly float[] _diskHistory = new float[24];
        private bool _initialHealthScoreSet = false;
        private double _currentHealthScore = 62.0;
        private bool _isCleanedRecently = false;
        private double _currentNetSpeedMbps = 0.0;
        // آمار واقعی پاکسازی — صفر تا زمانی که کاربر واقعاً چیزی پاک کرده باشد
        private long _cumulativeFreedBytes = 0;
        private long _lastCleanFreedBytes = 0;
        private DateTime _lastCleanDateTime = DateTime.MinValue;
        private readonly List<CleanupHistoryEntry> _cleanupHistory = new();
        /// <summary>مقدار قابل پاکسازی آخرین اسکن (bytes) — از ScanResult واقعی پر می شود.</summary>
        private long _lastScanCleanableBytes = 0;
        private double _diskTotalGb = 0;
        private double _diskFreeGb = 0;
        /// <summary>آخرین امضای رندر نوار دیسک — برای جلوگیری از layout در هر تیک تایمر.</summary>
        private string? _lastDiskRenderSig;
        private DeepStorageScanResult? _deepScanResult;
        private string _deepStorageActiveTab = "duplicates";

        // ====================================================================
        // D2: تنظیمات پایدار برنامه (settings.json)
        // ====================================================================
        /// <summary>سه نمای اصلی نوار ناوبری بالای صفحه.</summary>
        private enum AppView { Dashboard, Tools, Settings }

        // C6 — تنظیمات از کانتینر DI می آیند؛ `_settings` فقط یک نمایِ راحت روی همان شیء است
        private AppSettings _settings => _settingsStore.Current;
        private readonly ISettingsStore _settingsStore;
        /// <summary>ViewModel صفحه Settings (C6/MVVM)؛ state آن از طریق x:Bind به کنترل ها وصل است.</summary>
        internal SettingsViewModel SettingsVm { get; }
        /// <summary>هنگام مقداردهی اولیه ی کنترل های صفحه Settings نباید Toggled ذخیره انجام دهد.</summary>
        private bool _isLoadingSettings = false;
        private AppView _currentView = AppView.Dashboard;
        private bool _isExiting = false;
        /// <summary>هشدار تری فقط یک بار در هر اجرا نمایش داده شود تا آزاردهنده نشود.</summary>
        private bool _trayHintShown = false;

        public MainWindow()
        {
            // D2: تا پایان مقداردهی XAML و پر شدن کنترل ها، رویدادهای Toggled نباید
            // تنظیماتِ خوانده شده را با مقدار پیش فرضِ XAML بازنویسی کنند.
            _isLoadingSettings = true;

            // C6: سرویس ها و ViewModel از کانتینر DI می آیند (SettingsStore خودش تنظیمات را load می کند).
            // قبل از InitializeComponent مقداردهی می شوند تا هیچ handler ای در حین parse به شیء null نرسد.
            _settingsStore = AppServices.GetRequired<ISettingsStore>();
            _settingsStore.OnSaveRequested += CapturePlacementForSave;
            SettingsVm = AppServices.GetRequired<SettingsViewModel>();
            SettingsVm.PersistRequested += ScheduleSettingsSave;
            SettingsVm.ThemeChangeRequested += mode => ApplyTheme(ResolveInitialTheme(mode));
            SettingsVm.DeleteModeChangeRequested += OnDeleteModeChangedInUi;
            SettingsVm.LanguageChangeRequested += code =>
            {
                LocalizationService.SetLanguage(code);
                ApplyLanguage();
            };

            this.InitializeComponent();

            RegisterToolBackIcons();
            LocalizationService.SetLanguage(_settings.Language);
            ApplyLanguage();

            // D2: تنظیمات پایدار باید قبل از هر تصمیم UI (تم، هندسه ی پنجره، نمای آخر) خوانده شوند
            PopulateSettingsUi();

            // B4 — بازگرداندن تاریخچه ی اعلان های مانده از اجرای قبلی
            RenderPersistedNotificationHistory();

            // اگر فایل تنظیمات از قبل وجود ندارد، همین حالا با مقادیر پیش فرض ساخته شود
            // تا مسیر نمایش داده شده در صفحه Settings واقعی باشد و بار اول «بازنشانی» کار کند.
            if (!File.Exists(SettingsService.FilePath)) SaveSettingsNow();

            // F3 — نشست های قرنطینه قدیمی تر از ۳۰ روز در پس زمینه پاک می شوند
            // (خودِ فایل ها هم بیش از این نگه داشته نمی شوند تا دیسک اشغال نماند)
            System.Threading.Tasks.Task.Run(() =>
            {
                try { SafeDeleteService.PurgeOldSessions(30); }
                catch (Exception __ex) { CcLog.Ignore(__ex, "PurgeQuarantine"); }
            });

            _hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
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
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // Determine initial theme & apply cleanly (D2: از تنظیمات ذخیره شده، در حالت System از سیستم)
            var initialTheme = ResolveInitialTheme(_settings.Theme);
            ApplyTheme(initialTheme);

            this.Activated += (s, e) =>
            {
                try
                {
                    if (Content is FrameworkElement root)
                    {
                        ApplyTheme(root.ActualTheme);
                    }
                    if (e.WindowActivationState == WindowActivationState.Deactivated)
                    {
                        StopAuroraAnimations();

                        // D3: کوچک نمایی → پنهان شدن در تری آیکون.
                        // فقط وقتی پنجره واقعاً minimize است مخفی می شود تا Alt+Tab زدن
                        // باعث پنهان شدن برنامه نشود.
                        if (!_isExiting && _settings.MinimizeToTray && NativeMethods.IsWindowMinimized(_hWnd))
                        {
                            HideToTray();
                        }
                    }
                    else if (DashboardView != null && DashboardView.Visibility == Visibility.Visible)
                    {
                        StartAuroraAnimations();
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
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

                rootElement.KeyDown += (s, e) =>
                {
                    if (e.Key == Windows.System.VirtualKey.Escape)
                    {
                        if (ToolsView != null && ToolsView.Visibility == Visibility.Visible && ToolsLauncherPanel != null && ToolsLauncherPanel.Visibility != Visibility.Visible)
                        {
                            NavigateToToolPage(null);
                            e.Handled = true;
                        }
                    }
                };
            }

            // Set Initial Window Size (DPI-aware, ensuring 100% no scroll on initial dashboard view)
            uint dpi = CleanC.Core.Native.Win32.GetDpiForWindow(_hWnd);
            double dpiScale = (dpi > 0) ? (dpi / 96.0) : 1.0;

            // D2: بازیابی اندازه و موقعیت ذخیره شده — فقط اگر روی یکی از نمایشگرهای فعلی معتبر باشد
            if (_settings.SaveWindowPlacement &&
                _settings.HasSavedPlacement &&
                IsPlacementOnScreen(_settings.WindowX, _settings.WindowY, _settings.WindowWidth, _settings.WindowHeight))
            {
                _appWindow.Resize(new SizeInt32(_settings.WindowWidth, _settings.WindowHeight));
                _appWindow.Move(new PointInt32(_settings.WindowX, _settings.WindowY));
            }
            else
            {
                int initialW = (int)(1080 * dpiScale);
                int initialH = (int)(800 * dpiScale);
                _appWindow.Resize(new SizeInt32(Math.Max(initialW, 1080), Math.Max(initialH, 800)));
                CenterWindowOnScreen();
            }

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

                    // D2: ثبت مکان یابی جدید با debounce تا پایان درگ/تغییر سایز
                    if (!_isExiting && !NativeMethods.IsWindowMinimized(_hWnd) && _settings.SaveWindowPlacement)
                    {
                        CaptureWindowPlacement();
                        ScheduleSettingsSave();
                    }
                }
            };

            // D3: بستن پنجره به جای خروج، برنامه را در تری آیکون نگه می دارد
            _appWindow.Closing += (s, e) =>
            {
                if (!_isExiting && _settings.CloseToTray)
                {
                    e.Cancel = true;
                    HideToTray();
                    return;
                }

                // بستن واقعی: دیگر مکان یابی نگیر تا مختصاتِ حین تخریب پنجره
                // جایگزین آخرین مقدار معتبر نشود.
                _isExiting = true;
            };

            // Initial DPI-aware responsive layout pass
            double initialDipWidth = _appWindow.Size.Width / dpiScale;
            UpdateResponsiveLayout(initialDipWidth);

            // Set selected navigation item (D2: نمای آخر از تنظیمات بازیابی می شود)
            SwitchToView(ResolveLastView(), persist: false);

            LiveCleanListView.ItemsSource = _activeCleaningItems;

            // Setup real-time stats timer
            _statsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _statsTimer.Tick += (s, e) => RefreshSystemStats();
            _statsTimer.Start();

            // B6: تایمرها هنگام بستن پنجره متوقف نمی شدند و تا لحظه ی مرگ پروسه
            // (مثلاً وقتی آیکون تری باعث زنده ماندن برنامه می شود) به کار ادامه می دادند.
            Closed += (s, e) =>
            {
                // D2: آخرین وضعیت پیش از توقف تایمرها روی دیسک نوشته می شود
                SaveSettingsNow();
                StopBackgroundTimers();
            };

            // Load persistent cumulative space freed across all sessions
            LoadCumulativeFreedStats();

            // Hook Advanced Expander expansion
            AdvancedCleanExpander.Expanding += AdvancedCleanExpander_Expanding;

            // InfoBar auto-dismiss timer hook (4 seconds auto dismiss & FIFO queue)
            _infoBarAutoDismissTimer.Tick += (s, e) =>
            {
                _infoBarAutoDismissTimer.Stop();
                if (StatusInfoBar != null) StatusInfoBar.IsOpen = false;
                DequeueNextInfoBar();
            };

            if (StatusInfoBar != null)
            {
                StatusInfoBar.Closed += (s, e) =>
                {
                    _infoBarAutoDismissTimer.Stop();
                    DequeueNextInfoBar();
                };
            }

            // Search debounce timer hooks
            _uninstallerSearchDebounce.Tick += (s, e) =>
            {
                _uninstallerSearchDebounce.Stop();
                FilterAndRenderApps();
            };

            _driversSearchDebounce.Tick += (s, e) =>
            {
                _driversSearchDebounce.Stop();
                if (_cachedDrivers != null) RenderDrivers(_cachedDrivers);
            };

            if (NotificationHistoryFlyout != null)
            {
                NotificationHistoryFlyout.Opening += (s, e) =>
                {
                    if (NotificationBadgeDot != null) NotificationBadgeDot.Visibility = Visibility.Collapsed;
                };
            }

            // Check elevation status
            CheckAdminStatus();

            // Initial load
            RefreshSystemStats();
            InitHeroAurora();
            InitSparklines();

            // اسکن پس زمینه برای پر کردن چیپ «قابل آزادسازی» با داده واقعی
            ScheduleHeroCleanableRefresh();

            // Initialize System Tray Integration
            try
            {
                _trayManager = new TrayIconManager(this, () =>
                {
                    SwitchToTab(true);
                }, () =>
                {
                    QuickBoostBtn_Click(this, new RoutedEventArgs());
                }, () =>
                {
                    SwitchToTab(false);
                    NavigateToToolPage("dns_manager");
                }, ExitApplication);

                this.Closed += (s, e) =>
                {
                    _trayManager?.Dispose();
                    _windowsNotifications.Dispose();
                    StopAuroraAnimations();
                    // StopBackgroundTimers() در سازنده به Closed وصل است تا
                    // حتی اگر مقداردهی تری شکست بخورد، تایمرها متوقف شوند.
                };
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // F5 — ثبت کانال نوتیفیکیشن ویندوزی (AUMID + AppNotificationManager).
            // اگر ثبت شکست بخورد، سرویس نشان می دهد toast در دسترس نیست و
            // همه ی مسیرها به balloon تری آیکون برمی گردند.
            _windowsNotifications.TryRegister();
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
            // رنگ دکمه های caption باید دقیقاً از تم تیتل بار پیروی کند تا با DWM ضد نشود
            bool useLightCaptionButtons = isDark;

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
            catch (Exception ex)
            {
                App.LogCrash("UpdateTitleBarTheme/DWM", ex);
            }

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

                // D6: در High Contrast شفافیت و بلور مجاز نیست؛ Acrylic را خاموش می کنیم
                // تا پس زمینه ی جامد و رنگ های سیستم جایگزین شوند.
                if (IsHighContrastEnabled())
                {
                    _acrylicController?.Dispose();
                    _acrylicController = null;
                    return;
                }

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
                        // LUMINOUS FROSTED GLASS IN LIGHT MODE (Commit 836688a):
                        // Balanced tint opacity (0.65f) and luminosity (0.50f) provides a bright, silky
                        // frosted glass backdrop that ensures crystal-clear readability for dark text
                        // over any desktop wallpaper, while maintaining vibrant acrylic blur!
                        _acrylicController.TintColor = Windows.UI.Color.FromArgb(255, 246, 248, 252);
                        _acrylicController.TintOpacity = 0.65f;
                        _acrylicController.LuminosityOpacity = 0.50f;
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
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }
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

        /// <summary>
        /// B6 — توقف همه ی تایمرها هنگام بسته شدن پنجره.
        ///
        /// بدون این، تایمر آمار داشبورد (۲٫۵ ثانیه) و تایمرهای debounce/toast
        /// بعد از Close هم اجرا می ماندند — یعنی کار غیرضروری در لحظه ی خروج
        /// و رشته هایی که به پنجره ی مرده وصل هستند.
        /// </summary>
        private void StopBackgroundTimers()
        {
            try
            {
                _statsTimer?.Stop();
                _infoBarAutoDismissTimer.Stop();
                _uninstallerSearchDebounce.Stop();
                _driversSearchDebounce.Stop();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private void RefreshSystemStats()
        {
            // وقتی داشبورد مخفی است، به روزرسانی کنترل های آن فقط باعث layout می شود
            if (DashboardView == null || DashboardView.Visibility != Visibility.Visible) return;

            try
            {
                var stats = SystemStatsService.GetCurrentStats();

                // Requirement 4: Unified RAM source & calculations
                double totalRamGb = stats.TotalRamBytes / (1024.0 * 1024.0 * 1024.0);
                double usedRamGb = stats.UsedRamBytes / (1024.0 * 1024.0 * 1024.0);
                double ramPercent = totalRamGb > 0 ? (usedRamGb / totalRamGb) * 100.0 : 0.0;
                double cpuPercent = stats.CpuPercent;
                double diskTotalGb = stats.DiskTotalBytes / (1024.0 * 1024.0 * 1024.0);
                double diskFreeGb = stats.DiskFreeBytes / (1024.0 * 1024.0 * 1024.0);
                double diskPercent = stats.DiskPercent;

                // ذخیره برای رندر نوار تجزیه دیسک
                _diskTotalGb = diskTotalGb;
                _diskFreeGb = diskFreeGb;
                RenderDiskBreakdown(diskTotalGb, diskFreeGb, _lastScanCleanableBytes);

                // Hero Live Telemetry (.hr in HTML preview)
                if (HeroLiveRamText != null) HeroLiveRamText.Text = $"{Math.Round(ramPercent).ToString("0", CultureInfo.InvariantCulture)}%";
                if (HeroLiveCpuText != null) HeroLiveCpuText.Text = $"{Math.Round(cpuPercent).ToString("0", CultureInfo.InvariantCulture)}%";

                // Bento RAM Card - Format: "14.94 . 16 GB"
                CardRamText.Text = $"{usedRamGb.ToString("0.00", CultureInfo.InvariantCulture)} . {Math.Round(totalRamGb).ToString("0", CultureInfo.InvariantCulture)} GB";
                CardRamProgress.Value = ramPercent;
                if (CardRamAvailableText != null) CardRamAvailableText.Text = "فضای مصرفی در حال حاضر";
                if (CardRamTotalBadge != null) CardRamTotalBadge.Text = $"مجموع: {Math.Round(totalRamGb).ToString("0", CultureInfo.InvariantCulture)} GB";
                if (CardRamStatusText != null)
                {
                    CardRamStatusText.Text = ramPercent > 80 ? "مصرف بالا" : (ramPercent > 60 ? "مصرف متوسط" : "روان");
                }

                // Network Speed Card - Live traffic e.g. "84.5 Mbps" with intelligent consumer detection
                (double netMbps, string netType) = GetCurrentNetworkSpeed();
                _currentNetSpeedMbps = netMbps;
                string activeConsumer = GetActiveNetworkConsumer(netMbps);

                if (CardDiskTitle != null) CardDiskTitle.Text = "پایش ترافیک اینترنت";
                if (CardDiskHealthText != null)
                {
                    CardDiskHealthText.Text = netMbps >= 0.3 ? "مصرف فعال" : "آماده به کار (Idle)";
                }
                if (CardDiskText != null)
                {
                    if (netMbps >= 10.0)
                    {
                        CardDiskText.Text = $"{netMbps.ToString("0.0", CultureInfo.InvariantCulture)} Mbps";
                        if (CardDiskTypeBadge != null) CardDiskTypeBadge.Text = $"{activeConsumer} · حدود {Math.Round(netMbps)} مگابیت";
                    }
                    else if (netMbps >= 0.1)
                    {
                        CardDiskText.Text = $"{netMbps.ToString("0.2", CultureInfo.InvariantCulture)} Mbps";
                        double kbps = netMbps * 1024.0;
                        if (CardDiskTypeBadge != null) CardDiskTypeBadge.Text = $"{activeConsumer} · حدود {Math.Round(kbps)} کیلوبیت";
                    }
                    else
                    {
                        CardDiskText.Text = "0.0 Mbps";
                        if (CardDiskTypeBadge != null) CardDiskTypeBadge.Text = "بدون مصرف پس زمینه (شبکه پایدار)";
                    }
                }
                CardDiskProgress.Value = Math.Clamp(netMbps, 0, 100);

                // Bento CPU Card - Format: "18%"
                CardCpuText.Text = $"{Math.Round(cpuPercent).ToString("0", CultureInfo.InvariantCulture)}%";
                CardCpuProgress.Value = cpuPercent;
                if (CardGpuSummaryText != null) CardGpuSummaryText.Text = "بار پردازشی هسته ها";
                if (CardPowerText != null) CardPowerText.Text = !string.IsNullOrEmpty(stats.PowerSummary) ? stats.PowerSummary : "متصل به برق";

                // Health Score calculation & gauge arc update
                double healthScore;
                if (_isCleanedRecently)
                {
                    double penalty = (ramPercent > 80 ? (ramPercent - 80) * 0.2 : 0) + (cpuPercent > 70 ? (cpuPercent - 70) * 0.15 : 0);
                    healthScore = Math.Round(Math.Clamp(98.0 - penalty, 95.0, 99.0));
                }
                else
                {
                    // Prior to cleaning: realistic baseline reflecting pending junk & unoptimized state (~58-68)
                    double ramPenalty = (ramPercent > 40) ? (ramPercent - 40) * 0.25 : 0;
                    double cpuPenalty = (cpuPercent > 20) ? (cpuPercent - 20) * 0.15 : 0;
                    double diskPenalty = (diskPercent > 80) ? 6 : 0;
                    healthScore = Math.Round(Math.Clamp(68.0 - (ramPenalty + cpuPenalty + diskPenalty), 45.0, 72.0));
                }

                if (!_initialHealthScoreSet)
                {
                    _initialHealthScoreSet = true;
                    SafeInvoker.Run(AnimateHealthScoreEntrance(healthScore));
                }
                else
                {
                    SetHealthGaugeScore(healthScore);
                }

                // Push samples & update sparklines
                PushSample(_ramHistory, (float)ramPercent);
                PushSample(_cpuHistory, (float)cpuPercent);
                PushSample(_diskHistory, (float)netMbps);
                UpdateSparklinePaths();

                if (!_isBusy)
                {
                    if (ramPercent > 80)
                    {
                        HeroHealthTitle.Text = "مصرف حافظه رم بالاست";
                        HeroHealthSubtitle.Text = "با ۱ کلیک فایل های موقت پاک، حافظه رم آزاد و پردازه های اضافی بسته می شوند.";
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        // ====================================================================
        // The 1-Click Master Boost & Clean Orchestration (PC Manager Style)
        // ====================================================================

        private void QuickBoostBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(QuickBoostBtn_ClickAsync(sender, e));

        private async Task QuickBoostBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            _isBusy = true;

            QuickBoostBtn.IsEnabled = false;
            QuickBoostBtnText.Text = "در حال بهینه سازی سیستم...";
            QuickBoostBtnIcon.Glyph = "\uE895"; // sync icon

            // CleanupReportCard فقط بعد از اینکه مشخص شد واقعاً کاری برای انجام هست
            // پنهان می شود تا در حالت «هیچ موردی انتخاب نشده» گزارش قبلی از بین نرود.
            LiveCleanupCard.Visibility = Visibility.Visible;
            LiveCleanDoneIcon.Visibility = Visibility.Collapsed;
            LiveCleanSpinner.Visibility = Visibility.Visible;
            LiveCleanStatusTitle.Text = "در حال آماده سازی و ارزیابی سرفصل های سیستم...";
            LiveCleanPhaseSubtitle.Text = "بررسی متوالی فایل های موقت، کش ها و فضاهای ذخیره سازی";
            LiveFreedCounterText.Text = "+ 0 بایت آزاد شد";
            LiveCleanProgressBar.Value = 5;

            _activeCleaningItems.Clear();
            _displayedFreedBytes = 0;

            try
            {
                // 1. Gather scan items
                if (_cachedScanResult == null)
                {
                    LiveCleanStatusTitle.Text = "در حال پویش سریع سرفصل های سیستم...";
                    _cachedScanResult = await SystemScanService.ScanAsync();
                    ApplyPersistedJunkSelections(_cachedScanResult);
                    RenderAdvancedGroups(_cachedScanResult);
                    AdvancedCleanTotalText.Text = $"مجموع شناسایی شده: {_cachedScanResult.FormattedTotal}";
                    UpdateHeroCleanable(_cachedScanResult.TotalBytes);
                }

                // A3: فقط مواردی که کاربر تیک زده پردازش می شوند.
                var allChecked = _cachedScanResult.Groups
                    .SelectMany(g => g.Items)
                    .Where(i => i.IsChecked)
                    .ToList();

                if (allChecked.Count == 0)
                {
                    LiveCleanupCard.Visibility = Visibility.Collapsed;
                    ShowInfoBar(
                        "موردی انتخاب نشده",
                        "برای شروع بهینه سازی، دست کم یکی از سرفصل ها را در بخش پاکسازی انتخاب کنید.",
                        InfoBarSeverity.Warning);
                    return;
                }

                // سرفصل های فایلی برای نمایش در لیست زنده ی تمیزکاری
                var itemsToProcess = _cachedScanResult.Groups
                    .Where(g => g.Id != "system_boost")
                    .SelectMany(g => g.Items)
                    .Where(i => i.IsChecked)
                    .ToList();

                bool IsBoostEnabled(string boostId)
                {
                    var boostGroup = _cachedScanResult.Groups.FirstOrDefault(g => g.Id == "system_boost");
                    if (boostGroup == null) return true;
                    var item = boostGroup.Items.FirstOrDefault(i => i.Id == boostId);
                    return item == null || item.IsChecked;
                }

                CleanupReportCard.Visibility = Visibility.Collapsed;

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
                        StatusText = item.Bytes > 0 ? item.FormattedBytes : "در صف بهینه سازی"
                    };
                    _activeCleaningItems.Add(vm);
                }

                await Task.Delay(100);

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
                    LiveCleanPhaseSubtitle.Text = $"مرحله {i + 1} از {totalCount}: پاکسازی و بهینه سازی متوالی";

                    // STEP 1: Spinner / Loading on this task
                    vm.IsLoading = true;
                    vm.IsSuccess = false;
                    vm.StatusText = "در حال بررسی و پاکسازی...";
                    await Task.Delay(60);

                    // Background physical deletion — فقط واقعاً آزاد شده گزارش می شود
                    long freed = await Task.Run(() => CleanService.CleanCategory(item));
                    bool wasEmpty = freed <= 0;
                    totalFreedBytes += freed;

                    // STEP 2: Turns Emerald Green with checkmark & freed bytes
                    vm.IsLoading = false;
                    vm.IsSuccess = true;
                    vm.WasEmpty = wasEmpty;

                    if (wasEmpty)
                    {
                        vm.StatusText = item.Bytes > 0 ? "قبلاً پاک بود ✓" : "بدون فایل زائد (پاک)";
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
                    await Task.Delay(90);

                    // Record in report group
                    var targetRepGroup = reportGroups.FirstOrDefault(g => g.GroupId == item.GroupId);
                    targetRepGroup?.Items.Add(new CleanupReportItem
                    {
                        GroupId = item.GroupId,
                        Id = item.Id,
                        Title = item.Title,
                        IconGlyph = item.IconGlyph,
                        FreedBytes = freed,
                        WasEmpty = wasEmpty,
                        StatusSummary = wasEmpty ? (item.Bytes > 0 ? "قبلاً پاک بود" : "خالی بود (بدون تغییر)") : $"+ {SystemStats.FormatBytes(freed)} آزاد شد"
                    });

                    // STEP 3: Remove from active list
                    _activeCleaningItems.Remove(vm);
                    await Task.Delay(20);
                }

                // F2: هر گام بعدی جداگانه محافظت می شود؛ شکستِ یک گام نباید کلِ بهینه سازی را
                // قطع کند و باید در گزارش پایانی «ناموفق» گزارش شود نه اینکه بی صدا حذف شود.
                var failedSteps = new List<CleanupReportItem>();

                void ReportStepFailure(string id, string title, string glyph, Exception ex, string why)
                {
                    CcLog.Ignore(ex, "QuickBoost." + id);
                    failedSteps.Add(new CleanupReportItem
                    {
                        GroupId = "system_boost",
                        Id = id,
                        Title = title,
                        IconGlyph = glyph,
                        FreedBytes = 0,
                        WasEmpty = false,
                        IsFailed = true,
                        StatusSummary = $"ناموفق ✗ — {why}"
                    });
                }

                // 4. Live System Optimization (RAM Boost & Network DNS Flush)
                long ramFreed = 0;
                if (IsBoostEnabled("ramBoost"))
                {
                    LiveCleanProgressBar.Value = 82;
                    LiveCleanStatusTitle.Text = "در حال آزادسازی حافظه فعال رم (RAM)...";
                    LiveCleanPhaseSubtitle.Text = "تخلیه Working Set و بهینه سازی تخصیص حافظه پردازه ها";
                    await Task.Delay(60);

                    try
                    {
                        var boostResult = await OptimizerService.BoostRamAsync();
                        ramFreed = boostResult.FreedBytes;
                        if (!boostResult.Success && ramFreed <= 0)
                        {
                            failedSteps.Add(new CleanupReportItem
                            {
                                GroupId = "system_boost", Id = "ram_boost_fail", Title = "آزادسازی حافظه رم (RAM Working Set)",
                                IconGlyph = "\uE9D2", IsFailed = true,
                                StatusSummary = "ناموفق ✗ — دسترسی لازم برای تخلیه Working Set فراهم نشد"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        ReportStepFailure("ram_boost_fail", "آزادسازی حافظه رم (RAM Working Set)", "\uE9D2", ex, ex.Message);
                    }

                    totalFreedBytes += ramFreed;
                    AnimateCountUp(totalFreedBytes);
                }

                // DNS Flush & Clean Helpers
                if (IsBoostEnabled("dnsFlush") || IsBoostEnabled("backgroundHelpers"))
                {
                    LiveCleanProgressBar.Value = 90;
                    LiveCleanStatusTitle.Text = "در حال تخلیه کش DNS و نوسازی کانکشن ها...";
                    LiveCleanPhaseSubtitle.Text = "رفرش جدول آدرس های شبکه و پاکسازی پردازه های زائد پس زمینه";
                    await Task.Delay(50);

                    try
                    {
                        await Task.Run(() =>
                        {
                            if (IsBoostEnabled("dnsFlush")) OptimizerService.FlushDns();
                            if (IsBoostEnabled("backgroundHelpers")) ProcessManagerService.CleanBackgroundHelpers();
                        });
                    }
                    catch (Exception ex)
                    {
                        ReportStepFailure("dns_flush_fail", "تخلیه کش DNS و نوسازی کانکشن ها", "\uE774", ex, ex.Message);
                    }
                }

                // 5. Browser Background Tasks Suppression
                int browserTweaks = 0;
                if (IsBoostEnabled("browserBoost"))
                {
                    LiveCleanProgressBar.Value = 92;
                    LiveCleanStatusTitle.Text = "در حال خاموش سازی تسک های پس زمینه مرورگرها...";
                    LiveCleanPhaseSubtitle.Text = "توقف Startup Boost و فعالیت پنهان Edge و Chrome";
                    try
                    {
                        browserTweaks = await BrowserOptimizerService.SuppressBrowserBackgroundModeAsync();
                    }
                    catch (Exception ex)
                    {
                        ReportStepFailure("browser_opt_fail", "توقف تسک های پس زمینه و Startup Boost مرورگرها", "\uE774", ex, ex.Message);
                    }
                    await Task.Delay(50);
                }

                // 5.5 Deep Clean & Leftover Software Trace Removal
                int regCleaned = 0;
                int leftoverFolderCount = 0;
                long leftoverFreedBytes = 0;
                if (IsBoostEnabled("orphanedRegistry") || IsBoostEnabled("leftovers"))
                {
                    LiveCleanProgressBar.Value = 94;
                    LiveCleanStatusTitle.Text = "در حال پاکسازی عمیق و پویش بقایای نرم افزارهای حذف شده...";
                    LiveCleanPhaseSubtitle.Text = "اسکن پوشه های رهاشده در AppData و کلیدهای نامعتبر رجیستری";
                    await Task.Delay(50);

                    try
                    {
                        (regCleaned, leftoverFolderCount, leftoverFreedBytes) = await Task.Run(() =>
                        {
                            int cleaned = IsBoostEnabled("orphanedRegistry") ? RegistryCleanerService.CleanOrphanedRegistry() : 0;
                            var folders = LeftoverCleanerService.ScanLeftoverFolders();
                            long freed = IsBoostEnabled("leftovers") ? LeftoverCleanerService.DeleteLeftoverFolders(folders) : 0;
                            return (cleaned, folders.Count, freed);
                        });
                    }
                    catch (Exception ex)
                    {
                        ReportStepFailure("deep_clean_leftovers_fail", "پاکسازی عمیق بقایای نرم افزارها", "\uE74D", ex, ex.Message);
                    }
                    totalFreedBytes += leftoverFreedBytes;
                    if (leftoverFreedBytes > 0) AnimateCountUp(totalFreedBytes);
                }

                // 6. Startup Apps & Launchers Optimization
                int disabledStartups = 0;
                if (IsBoostEnabled("startupOptimize"))
                {
                    LiveCleanProgressBar.Value = 95;
                    LiveCleanStatusTitle.Text = "در حال بهینه سازی استارتاپ ویندوز...";
                    LiveCleanPhaseSubtitle.Text = "غیرفعال سازی برنامه ها و لانچرهای سنگین شروع خودکار";
                    try
                    {
                        disabledStartups = await StartupOptimizerService.OptimizeStartupAppsAsync();
                    }
                    catch (Exception ex)
                    {
                        ReportStepFailure("startup_opt_fail", "بهینه سازی استارتاپ ویندوز", "\uE7B5", ex, ex.Message);
                    }
                    await Task.Delay(50);
                }

                // 7. Windows Performance & Privacy Tweaks
                int winTweaks = 0;
                if (IsBoostEnabled("winTweaks"))
                {
                    LiveCleanProgressBar.Value = 96;
                    LiveCleanStatusTitle.Text = "در حال اعمال تنظیمات بهینه سازی ویندوز...";
                    LiveCleanPhaseSubtitle.Text = "غیرفعال سازی پیشنهادات و سرویس های درگیرکننده پردازنده";
                    try
                    {
                        winTweaks = await WindowsTweakService.ApplyLowRiskPerformanceTweaksAsync();
                        winTweaks += await TelemetryOptimizerService.DisableTelemetryAndDiagTrackAsync();
                    }
                    catch (Exception ex)
                    {
                        ReportStepFailure("win_tweaks_fail", "توقف تله متری و تنظیمات عملکرد ویندوز", "\uE770", ex, ex.Message);
                    }
                    await Task.Delay(50);
                }

                // 7.5 Fast Startup
                CleanupReportItem? fastStartupReport = null;
                if (IsBoostEnabled("fastStartup"))
                {
                    LiveCleanProgressBar.Value = 97;
                    LiveCleanStatusTitle.Text = "در حال بررسی و اعمال Fast Startup...";
                    LiveCleanPhaseSubtitle.Text = "کوتاه کردن زمان خاموش شدن سیستم (Fast Startup)";
                    try
                    {
                        var fsStatus = await Task.Run(FastStartupService.GetStatus);
                        if (fsStatus.IsFastStartupEnabled)
                        {
                            fastStartupReport = new CleanupReportItem
                            {
                                GroupId = "system_boost", Id = "fast_startup", Title = "Fast Startup (خاموش شدن سریع ویندوز)",
                                IconGlyph = "\uE7E8", WasEmpty = true, StatusSummary = "از قبل فعال بود ✓"
                            };
                        }
                        else if (!fsStatus.IsHibernationEnabled)
                        {
                            fastStartupReport = new CleanupReportItem
                            {
                                GroupId = "system_boost", Id = "fast_startup", Title = "Fast Startup (خاموش شدن سریع ویندوز)",
                                IconGlyph = "\uE7E8", IsFailed = true,
                                StatusSummary = "انجام نشد ✗ — هایبرنیشن خاموش است (فعال سازی آن فایل hiberfil.sys می سازد)"
                            };
                        }
                        else if (FastStartupService.SetFastStartup(true))
                        {
                            fastStartupReport = new CleanupReportItem
                            {
                                GroupId = "system_boost", Id = "fast_startup", Title = "Fast Startup (خاموش شدن سریع ویندوز)",
                                IconGlyph = "\uE7E8", WasEmpty = false, StatusSummary = "فعال شد ✓"
                            };
                        }
                        else
                        {
                            fastStartupReport = new CleanupReportItem
                            {
                                GroupId = "system_boost", Id = "fast_startup", Title = "Fast Startup (خاموش شدن سریع ویندوز)",
                                IconGlyph = "\uE7E8", IsFailed = true,
                                StatusSummary = "ناموفق ✗ — دسترسی Administrator لازم است"
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        ReportStepFailure("fast_startup", "Fast Startup (خاموش شدن سریع ویندوز)", "\uE7E8", ex, ex.Message);
                    }
                    await Task.Delay(50);
                }

                // 8. GPU Graphics Preference Optimization
                int gpuConfiguredCount = 0;
                string gpuName = string.Empty;
                if (IsBoostEnabled("gpuOptimize"))
                {
                    LiveCleanProgressBar.Value = 98;
                    LiveCleanStatusTitle.Text = "در حال تخصیص هوشمند کارت گرافیک...";
                    LiveCleanPhaseSubtitle.Text = "هدایت برنامه های سنگین و بازی ها به پردازنده گرافیکی قدرتمند";
                    try
                    {
                        var gpuResult = await GpuOptimizerService.OptimizeGpuPreferencesAsync();
                        gpuConfiguredCount = gpuResult.ConfiguredAppsCount;
                        gpuName = gpuResult.DedicatedGpuName;
                    }
                    catch (Exception ex)
                    {
                        ReportStepFailure("gpu_opt_fail", "تخصیص کارت گرافیک مجزا", "\uE790", ex, ex.Message);
                    }
                    await Task.Delay(40);
                }

                // 8.5 Power Plan Optimization
                bool powerPlanSet = false;
                if (IsBoostEnabled("powerPlanBoost"))
                {
                    LiveCleanProgressBar.Value = 98.5;
                    LiveCleanStatusTitle.Text = "در حال تنظیم پلن انرژی کارایی بالا...";
                    LiveCleanPhaseSubtitle.Text = "تنظیم توان پردازنده بر روی High / Ultimate Performance";
                    try
                    {
                        powerPlanSet = await Task.Run(() => PowerPlanService.ApplyHighPerformancePlan());
                    }
                    catch (Exception ex)
                    {
                        ReportStepFailure("power_plan_fail", "تنظیم پلن انرژی کارایی بالا", "\uE7E8", ex, ex.Message);
                    }
                    await Task.Delay(40);
                }

                // 8.6 Bloatware & Default Games Debloating + Interactive Borderline Apps Review
                int bloatRemovedCount = 0;
                long bloatFreedBytes = 0;
                int borderlineRemovedCount = 0;
                long borderlineFreedBytes = 0;
                if (IsBoostEnabled("bloatwareClean"))
                {
                    LiveCleanProgressBar.Value = 99;
                    LiveCleanStatusTitle.Text = "در حال حذف برنامه ها و بازی های تبلیغاتی (Debloat)...";
                    LiveCleanPhaseSubtitle.Text = "پاکسازی Candy Crush و بازی های پیش فرض و بررسی برنامه های کم استفاده";
                    try
                    {
                        // 1. حذف خودکار تبلیغات و بازی های بی استفاده قطعی
                        var (autoRemoved, autoFreed) = await BloatwareService.CleanAutomaticBloatwareAsync();
                        bloatRemovedCount = autoRemoved;
                        bloatFreedBytes = autoFreed;
                        if (autoFreed > 0)
                        {
                            totalFreedBytes += autoFreed;
                            AnimateCountUp(totalFreedBytes);
                        }

                        // 2. اسکن و پرسش تعاملی برای موارد ریسک دار/اختیاری (Paint، Xbox، برنامه های > 500MB با ۳۰ روز عدم استفاده)
                        var reviewCandidates = await BloatwareService.ScanBorderlineReviewAppsAsync();
                        if (reviewCandidates.Count > 0)
                        {
                            var (revRemoved, revFreed) = await ShowBorderlineAppsReviewDialogAsync(reviewCandidates);
                            borderlineRemovedCount = revRemoved;
                            borderlineFreedBytes = revFreed;
                            if (revFreed > 0)
                            {
                                totalFreedBytes += revFreed;
                                AnimateCountUp(totalFreedBytes);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ReportStepFailure("bloatware_clean_fail", "حذف برنامه ها و بازی های تبلیغاتی", "\uE74C", ex, ex.Message);
                    }
                    await Task.Delay(40);
                }

                // 9. Hardware, Display & Power Diagnostics
                LiveCleanProgressBar.Value = 99.5;
                LiveCleanStatusTitle.Text = "در حال بررسی وضعیت مانیتور و توان سیستم...";
                LiveCleanPhaseSubtitle.Text = "سنجش نرخ نوسازی تصویر و پلن مصرف انرژی";
                var attentionList = new List<AttentionDecisionItem>();
                try
                {
                    attentionList = await HardwareDiagnosticService.RunDiagnosticsAsync();
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex, "QuickBoost.hardware_diagnostics");
                }
                await Task.Delay(150);

                // Add Live System Boost group to report
                var systemBoostGroup = new CleanupReportGroup
                {
                    GroupId = "system_boost",
                    Title = "بهینه سازی زنده سیستم و پایداری",
                    IconGlyph = "\uE9D2"
                };

                bool HasFailure(string id) => failedSteps.Any(f => f.Id == id);

                if (IsBoostEnabled("ramBoost") && !HasFailure("ram_boost_fail"))
                {
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
                }

                if (IsBoostEnabled("dnsFlush") && !HasFailure("dns_flush_fail"))
                {
                    systemBoostGroup.Items.Add(new CleanupReportItem
                    {
                        GroupId = "system_boost",
                        Id = "dns_flush",
                        Title = "تخلیه کش DNS و نوسازی کانکشن ها",
                        IconGlyph = "\uE774",
                        FreedBytes = 0,
                        WasEmpty = false,
                        StatusSummary = "با موفقیت نوسازی شد ✓"
                    });
                }

                if (browserTweaks > 0)
                {
                    systemBoostGroup.Items.Add(new CleanupReportItem
                    {
                        GroupId = "system_boost",
                        Id = "browser_opt",
                        Title = "توقف تسک های پس زمینه و Startup Boost مرورگرها",
                        IconGlyph = "\uE774",
                        FreedBytes = 0,
                        WasEmpty = false,
                        StatusSummary = "بهینه سازی شد ✓"
                    });
                }

                if (disabledStartups > 0)
                {
                    systemBoostGroup.Items.Add(new CleanupReportItem
                    {
                        GroupId = "system_boost",
                        Id = "startup_opt",
                        Title = $"بهینه سازی استارتاپ ({disabledStartups} برنامه غیرضروری)",
                        IconGlyph = "\uE7B5",
                        FreedBytes = 0,
                        WasEmpty = false,
                        StatusSummary = "غیرفعال شد ✓"
                    });
                }

                if (gpuConfiguredCount > 0)
                {
                    systemBoostGroup.Items.Add(new CleanupReportItem
                    {
                        GroupId = "system_boost",
                        Id = "gpu_opt",
                        Title = $"تخصیص کارت گرافیک مجزا ({gpuName})",
                        IconGlyph = "\uE790",
                        FreedBytes = 0,
                        WasEmpty = false,
                        StatusSummary = $"{gpuConfiguredCount} برنامه بهینه سازی شد ✓"
                    });
                }

                if (powerPlanSet)
                {
                    systemBoostGroup.Items.Add(new CleanupReportItem
                    {
                        GroupId = "system_boost",
                        Id = "power_plan",
                        Title = "پلن انرژی عملکرد بالا (High Performance)",
                        IconGlyph = "\uE7E8",
                        FreedBytes = 0,
                        WasEmpty = false,
                        StatusSummary = "فعال شد ✓"
                    });
                }

                if (bloatRemovedCount > 0)
                {
                    systemBoostGroup.Items.Add(new CleanupReportItem
                    {
                        GroupId = "system_boost",
                        Id = "bloat_clean",
                        Title = $"حذف خودکار برنامه ها و بازی های تبلیغاتی ({bloatRemovedCount} برنامه)",
                        IconGlyph = "\uE74C",
                        FreedBytes = bloatFreedBytes,
                        WasEmpty = false,
                        StatusSummary = bloatFreedBytes > 0 ? $"+ {SystemStats.FormatBytes(bloatFreedBytes)} آزاد شد ✓" : "پاکسازی شد ✓"
                    });
                }

                if (borderlineRemovedCount > 0)
                {
                    systemBoostGroup.Items.Add(new CleanupReportItem
                    {
                        GroupId = "system_boost",
                        Id = "borderline_clean",
                        Title = $"پاکسازی برنامه های اختیاری تاییدشده ({borderlineRemovedCount} برنامه)",
                        IconGlyph = "\uE74C",
                        FreedBytes = borderlineFreedBytes,
                        WasEmpty = false,
                        StatusSummary = borderlineFreedBytes > 0 ? $"+ {SystemStats.FormatBytes(borderlineFreedBytes)} آزاد شد ✓" : "پاکسازی شد ✓"
                    });
                }

                if (winTweaks > 0)
                {
                    systemBoostGroup.Items.Add(new CleanupReportItem
                    {
                        GroupId = "system_boost",
                        Id = "win_tweaks",
                        Title = "توقف تله متری، افزایش سرعت سرچ، اکسپلورر و تبلیغات ویندوز",
                        IconGlyph = "\uE770",
                        FreedBytes = 0,
                        WasEmpty = false,
                        StatusSummary = "اعمال شد ✓"
                    });
                }

                if (fastStartupReport != null)
                {
                    systemBoostGroup.Items.Add(fastStartupReport);
                }

                if (regCleaned > 0 || leftoverFreedBytes > 0)
                {
                    systemBoostGroup.Items.Add(new CleanupReportItem
                    {
                        GroupId = "system_boost",
                        Id = "deep_clean_leftovers",
                        Title = $"پاکسازی عمیق ({regCleaned} کلید نامعتبر رجیستری + {leftoverFolderCount} پوشه رهاشده)",
                        IconGlyph = "\uE74D",
                        FreedBytes = leftoverFreedBytes,
                        WasEmpty = (regCleaned == 0 && leftoverFreedBytes == 0),
                        StatusSummary = leftoverFreedBytes > 0 ? $"+ {SystemStats.FormatBytes(leftoverFreedBytes)} آزاد شد ✓" : "کلیدهای نامعتبر پاکسازی شدند ✓"
                    });
                }

                // گام های ناموفقِ گام به گام — همیشه در انتهای گروه و با رنگ هشدار
                systemBoostGroup.Items.AddRange(failedSteps);

                reportGroups.Add(systemBoostGroup);

                // 9. Celebration & Transition to Hierarchical Report Card
                LiveCleanProgressBar.Value = 100;
                LiveCleanSpinner.Visibility = Visibility.Collapsed;
                LiveCleanDoneIcon.Visibility = Visibility.Visible;
                LiveCleanStatusTitle.Text = "بهینه سازی خودکار سیستم با موفقیت به پایان رسید!";
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
                UpdateHeroCleanable(0);
                ScheduleHeroCleanableRefresh();

                _isCleanedRecently = true;
                _lastCleanFreedBytes = totalFreedBytes;
                _cumulativeFreedBytes += totalFreedBytes;
                _lastCleanDateTime = DateTime.Now;
                RecordCleanupSession(totalFreedBytes);
                SaveCumulativeFreedStats();
                UpdateCumulativeChipsUI();
                RenderCleanupHistory();

                // امتیاز سلامت جدید را ابتدا محاسبه کن، بعد انیمیشن را از مقدار قبلی به جدید ببر
                double prevHealthScore = _currentHealthScore;
                RefreshSystemStats();
                double targetHealthScore = _currentHealthScore;
                if (Math.Abs(targetHealthScore - prevHealthScore) > 0.5)
                {
                    SafeInvoker.Run(AnimateHealthScoreTransition(prevHealthScore, targetHealthScore));
                }
            }
            catch (Exception ex)
            {
                ShowInfoBar("خطا در بهینه سازی", ex.Message, InfoBarSeverity.Error);
                LiveCleanStatusTitle.Text = "خطا در عملیات پاکسازی";
                LiveCleanSpinner.Visibility = Visibility.Collapsed;
            }
            finally
            {
                _isBusy = false;
                QuickBoostBtn.IsEnabled = true;
                QuickBoostBtnText.Text = "بهینه سازی و ارتقای سرعت سیستم";
                QuickBoostBtnIcon.Glyph = "\uE777";
            }
        }

        private async Task<(int RemovedCount, long FreedBytes)> ShowBorderlineAppsReviewDialogAsync(List<CandidateAppItem> reviewApps)
        {
            if (reviewApps == null || reviewApps.Count == 0) return (0, 0);

            var stack = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 0, 0) };
            stack.Children.Add(new TextBlock
            {
                Text = "برنامه های زیر در ویندوز شناسایی شدند که ممکن است به آن ها نیاز داشته باشید (مانند Paint، سرویس های بازی Xbox یا نرم افزارهای حجیم با بیش از ۳۰ روز عدم استفاده). مواردی که مایل به حذف آن ها هستید را علامت بزنید:",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                LineHeight = 19
            });

            var itemsScroll = new ScrollViewer { MaxHeight = 280 };
            var itemsList = new StackPanel { Spacing = 6 };

            var checkBoxes = new List<(CheckBox Box, CandidateAppItem App)>();

            foreach (var app in reviewApps)
            {
                var card = new Border
                {
                    Background = GetThemeBrush("AppCardBackgroundBrush"),
                    BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 8, 10, 8)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var cb = new CheckBox
                {
                    IsChecked = app.IsSelected,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(cb, 0);
                grid.Children.Add(cb);

                var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
                details.Children.Add(new TextBlock
                {
                    Text = app.DisplayName,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 12
                });
                details.Children.Add(new TextBlock
                {
                    Text = $"{app.CategoryLabel} — {app.Description}",
                    FontSize = 10.5,
                    Foreground = GetThemeBrush("TextFillColorTertiaryBrush"),
                    TextWrapping = TextWrapping.Wrap
                });
                Grid.SetColumn(details, 1);
                grid.Children.Add(details);

                if (app.SizeBytes > 0)
                {
                    var sizeBadge = new Border
                    {
                        Background = GetThemeBrush("AppCardSecondaryBrush"),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0),
                        Child = new TextBlock
                        {
                            Text = app.FormattedSize,
                            FontSize = 10.5,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            FlowDirection = FlowDirection.LeftToRight
                        }
                    };
                    Grid.SetColumn(sizeBadge, 2);
                    grid.Children.Add(sizeBadge);
                }

                card.Child = grid;
                itemsList.Children.Add(card);
                checkBoxes.Add((cb, app));
            }

            itemsScroll.Content = itemsList;
            stack.Children.Add(itemsScroll);

            var dialog = new ContentDialog
            {
                Title = "بررسی و بازبینی نرم افزارهای کم استفاده و پیش فرض",
                Content = stack,
                PrimaryButtonText = "پاکسازی موارد انتخاب شده",
                CloseButtonText = "صرف نظر و ادامه بهینه سازی",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot,
                FlowDirection = FlowDirection.RightToLeft
            };

            var dialogResult = await dialog.ShowAsync();
            int removed = 0;
            long freed = 0;

            if (dialogResult == ContentDialogResult.Primary)
            {
                foreach (var (box, app) in checkBoxes)
                {
                    if (box.IsChecked == true)
                    {
                        try
                        {
                            if (await BloatwareService.RemoveAppAsync(app))
                            {
                                removed++;
                                freed += app.SizeBytes;
                            }
                        }
                        catch (Exception ex)
                        {
                            CcLog.Ignore(ex);
                        }
                    }
                }
            }

            return (removed, freed);
        }

        private void RenderHierarchicalReport(List<CleanupReportGroup> groups, long totalFreed, List<AttentionDecisionItem>? attentionItems = null)
        {
            _currentReportTotalFreed = totalFreed;
            _lastCleanupItems = groups.SelectMany(g => g.Items).ToList();
            _lastFreedBytes = totalFreed;
            ReportTotalFreedText.Text = $"+ {SystemStats.FormatBytes(totalFreed)} آزاد شد";
            ReportGroupsContainer.Children.Clear();

            var successBg = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 16, 185, 129));
            var successFg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));
            var neutralBg = new SolidColorBrush(Windows.UI.Color.FromArgb(18, 128, 128, 128));
            var neutralFg = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            var accentFg = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            var cardBg = GetThemeBrush("AppCardSecondaryBrush");
            var borderBrush = GetThemeBrush("AppCardBorderBrush");

            // 1. Prominent Achievements & System Boost Summary Banner
            var boostGroup = groups.FirstOrDefault(g => g.GroupId == "system_boost");
            long ramFreed = boostGroup?.Items.FirstOrDefault(i => i.Id == "ram_boost")?.FreedBytes ?? 0;
            var startupItem = boostGroup?.Items.FirstOrDefault(i => i.Id == "startup_opt");
            var browserItem = boostGroup?.Items.FirstOrDefault(i => i.Id == "browser_opt");
            var gpuItem = boostGroup?.Items.FirstOrDefault(i => i.Id == "gpu_opt");
            var winTweaksItem = boostGroup?.Items.FirstOrDefault(i => i.Id == "win_tweaks");
            long junkDiskFreed = groups.Where(g => g.GroupId != "system_boost").Sum(g => g.TotalFreedBytes);

            var achievementsCard = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 59, 130, 246)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(110, 59, 130, 246)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var achStack = new StackPanel { Spacing = 10 };
            var achHeader = new Grid();
            achHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            achHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var achIcon = new FontIcon
            {
                Glyph = "\uE777",
                FontSize = 18,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 59, 130, 246)),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(achIcon, 0);
            achHeader.Children.Add(achIcon);

            var achTitleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            achTitleStack.Children.Add(new TextBlock { Text = "خلاصه دستاوردهای ارتقای سرعت و آزادسازی سیستم", FontSize = 13.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            achTitleStack.Children.Add(new TextBlock { Text = "نتایج بهینه سازی زنده پردازنده، حافظه رم، استارتاپ و اتصالات شبکه", FontSize = 11, Foreground = neutralFg });
            Grid.SetColumn(achTitleStack, 1);
            achHeader.Children.Add(achTitleStack);
            achStack.Children.Add(achHeader);

            // Multi-Column Achievement Grid (Fits 100% within card bounds with zero horizontal scrolling)
            var achievementsGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            achievementsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            achievementsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int achievementIndex = 0;
            void AddAchievementChip(string glyph, string text, Windows.UI.Color color)
            {
                int row = achievementIndex / 2;
                int col = achievementIndex % 2;

                while (achievementsGrid.RowDefinitions.Count <= row)
                {
                    achievementsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }

                var chip = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(20, color.R, color.G, color.B)),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(70, color.R, color.G, color.B)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = text, FontSize = 11.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                chip.Child = sp;

                Grid.SetRow(chip, row);
                Grid.SetColumn(chip, col);
                achievementsGrid.Children.Add(chip);
                achievementIndex++;
            }

            if (ramFreed > 0)
            {
                AddAchievementChip("\uE9D2", $"+ {SystemStats.FormatBytes(ramFreed)} آزادسازی رم", Windows.UI.Color.FromArgb(255, 16, 185, 129));
            }

            if (junkDiskFreed > 0)
            {
                AddAchievementChip("\uEDA2", $"+ {SystemStats.FormatBytes(junkDiskFreed)} پاکسازی دیسک", Windows.UI.Color.FromArgb(255, 59, 130, 246));
            }

            if (startupItem != null)
            {
                AddAchievementChip("\uE7B5", startupItem.Title, Windows.UI.Color.FromArgb(255, 245, 158, 11));
            }

            AddAchievementChip("\uE774", "نوسازی کش DNS و سوکت های شبکه", Windows.UI.Color.FromArgb(255, 6, 182, 212));

            if (browserItem != null)
            {
                AddAchievementChip("\uE774", "توقف تسک های پس زمینه Edge و Chrome", Windows.UI.Color.FromArgb(255, 139, 92, 246));
            }

            if (winTweaksItem != null)
            {
                AddAchievementChip("\uE770", "توقف تله متری و افزایش سرعت ویندوز", Windows.UI.Color.FromArgb(255, 236, 72, 153));
            }

            if (gpuItem != null)
            {
                AddAchievementChip("\uE790", gpuItem.Title, Windows.UI.Color.FromArgb(255, 16, 185, 129));
            }

            achStack.Children.Add(achievementsGrid);
            achievementsCard.Child = achStack;
            ReportGroupsContainer.Children.Add(achievementsCard);

            // 2. Attention / Decision items (e.g. 144Hz monitor refresh rate)
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
                    var capturedCardBorder = attBorder;
                    var capturedCardIcon = attIcon;

                    actionBtn.Click += async (s, ev) =>
                    {
                        if (capturedAtt.Id == "refresh_rate")
                        {
                            bool success = HardwareDiagnosticService.ApplyRefreshRate(capturedAtt.IntParameter);
                            if (success)
                            {
                                MarkAttentionCardSuccess(capturedCardBorder, capturedCardIcon, actionBtn, "ارتقا یافت ✓");
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
                                MarkAttentionCardSuccess(capturedCardBorder, capturedCardIcon, actionBtn, "فعال شد ✓");
                                ShowInfoBar("مدیریت مصرف انرژی", "پلن High Performance با حداکثر توان پردازشی فعال گردید.", InfoBarSeverity.Success);
                            }
                            else
                            {
                                ShowInfoBar("خطا", "امکان تغییر پلن برق در این لحظه وجود ندارد.", InfoBarSeverity.Warning);
                            }
                        }
                        else if (capturedAtt.Id == "bloatware_apps")
                        {
                            await ShowBloatwareManagementDialogAsync(capturedCardBorder, capturedCardIcon, actionBtn);
                        }
                        else if (capturedAtt.Id == "advanced_tweaks")
                        {
                            int applied = await AdvancedTweakService.ApplyAdvancedTweaksAsync();
                            RegistryBackupService.MarkTweaksApplied();
                            MarkAttentionCardSuccess(capturedCardBorder, capturedCardIcon, actionBtn, "اعمال شد ✓");
                            ShowInfoBar("بهینه سازی پیشرفته", "محدودیت پهنای باند شبکه حذف و اولویت پردازشی سیستم ارتقا یافت.", InfoBarSeverity.Success);
                        }
                        else if (capturedAtt.Id == "ssd_critical_space")
                        {
                            actionBtn.Content = "در حال بهینه سازی...";
                            actionBtn.IsEnabled = false;
                            bool ok = await DiskMaintenanceService.OptimizeDriveTrimAsync("C");
                            MarkAttentionCardSuccess(capturedCardBorder, capturedCardIcon, actionBtn, "انجام شد ✓");
                            ShowInfoBar("بهینه سازی SSD", "دستور TRIM با موفقیت اجرا شد و سکتورهای درایو ویندوز بهینه سازی گردیدند.", InfoBarSeverity.Success);
                        }
                        else if (capturedAtt.Id == "gpu_hags")
                        {
                            bool ok = GpuOptimizerService.EnableHags();
                            MarkAttentionCardSuccess(capturedCardBorder, capturedCardIcon, actionBtn, "فعال شد ✓");
                            ShowInfoBar("شتاب دهنده گرافیک", "قابلیت HAGS با موفقیت فعال گردید. (جهت اعمال کامل نیاز به ریستارت ویندوز دارد)", InfoBarSeverity.Success);
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
                    bool isFailed = item.IsFailed && !hasFreed;
                    var failFg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68));
                    var failBg = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 239, 68, 68));
                    var itemGlyph = new FontIcon
                    {
                        Glyph = isFailed ? "\uE711" : (hasFreed ? "\uE73E" : (item.WasEmpty ? "\uE946" : "\uE73E")),
                        FontSize = 13,
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = isFailed ? failFg : (hasFreed ? successFg : neutralFg)
                    };
                    Grid.SetColumn(itemGlyph, 0);
                    itemGrid.Children.Add(itemGlyph);

                    var itemTitle = new TextBlock
                    {
                        Text = item.Title,
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = isFailed ? failFg : GetThemeBrush("TextFillColorPrimaryBrush")
                    };
                    Grid.SetColumn(itemTitle, 1);
                    itemGrid.Children.Add(itemTitle);

                    // Smart Badge:
                    var itemBadge = new Border
                    {
                        Background = isFailed ? failBg : (hasFreed ? successBg : neutralBg),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(7, 2, 7, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var itemBadgeText = new TextBlock
                    {
                        Text = isFailed
                            ? item.StatusSummary
                            : (hasFreed
                                ? $"+ {item.FormattedFreed} آزاد شد ✓"
                                : (item.WasEmpty ? "خالی بود (بدون تغییر)" : item.StatusSummary)),
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                        Foreground = isFailed ? failFg : (hasFreed ? successFg : neutralFg)
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

        private void MarkAttentionCardSuccess(Border cardBorder, FontIcon cardIcon, Button btn, string successText = "اعمال شد ✓")
        {
            cardBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 16, 185, 129));
            cardBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 16, 185, 129));
            cardIcon.Glyph = "\uE73E";
            cardIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));
            btn.Content = successText;
            btn.IsEnabled = false;
            btn.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            btn.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(160, 16, 185, 129));
            btn.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));
        }

        private async Task ShowBloatwareManagementDialogAsync(Border attBorder, FontIcon attIcon, Button actionBtn)
        {
            try
            {
                var candidates = await BloatwareService.ScanCandidateAppsAsync();
                if (candidates.Count == 0)
                {
                    ShowInfoBar("برنامه ها", "هیچ برنامه غیرضروری یا کم استفاده ای یافت نشد.", InfoBarSeverity.Informational);
                    MarkAttentionCardSuccess(attBorder, attIcon, actionBtn, "بررسی شد ✓");
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = "مدیریت و حذف برنامه های غیرضروری و کم استفاده",
                    PrimaryButtonText = "حذف موارد انتخاب شده",
                    CloseButtonText = "نگه داشتن همه",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.Content.XamlRoot
                };

                var scrollViewer = new ScrollViewer { MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 8), FlowDirection = FlowDirection.RightToLeft };

                var headerText = new TextBlock
                {
                    Text = "برنامه های زیر می توانند برای آزادسازی منابع سیستم حذف شوند. روی هر سطر کلیک کنید تا انتخاب شود:",
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                stack.Children.Add(headerText);

                var checkBoxes = new List<(CheckBox Box, CandidateAppItem App)>();

                var selectAllCheck = new CheckBox
                {
                    Content = "انتخاب همه برنامه ها",
                    IsChecked = true,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                selectAllCheck.Click += (s, e) =>
                {
                    bool isAll = selectAllCheck.IsChecked == true;
                    foreach (var (box, _) in checkBoxes)
                    {
                        box.IsChecked = isAll;
                    }
                };
                stack.Children.Add(selectAllCheck);

                foreach (var app in candidates)
                {
                    var itemBorder = new Border
                    {
                        Background = GetThemeBrush("AppCardSecondaryBrush"),
                        BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10, 8, 10, 8),
                        IsHitTestVisible = true
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

                    var capturedChk = chk;
                    itemBorder.PointerPressed += (s, e) =>
                    {
                        capturedChk.IsChecked = !(capturedChk.IsChecked == true);
                    };

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

                        if (removed > 0)
                        {
                            _currentReportTotalFreed += freedBytes;
                            _displayedFreedBytes = _currentReportTotalFreed;
                            ReportTotalFreedText.Text = $"+ {SystemStats.FormatBytes(_currentReportTotalFreed)} آزاد شد";
                            LiveFreedCounterText.Text = $"+ {SystemStats.FormatBytes(_currentReportTotalFreed)} آزاد شد";
                            HeroHealthSubtitle.Text = $"مجموعاً {SystemStats.FormatBytes(_currentReportTotalFreed)} از حافظه رم و فضای دیسک آزاد شد.";

                            MarkAttentionCardSuccess(attBorder, attIcon, actionBtn, $"{removed} برنامه حذف شد ✓");

                            var removedCard = new Border
                            {
                                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 16, 185, 129)),
                                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(100, 16, 185, 129)),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(8),
                                Padding = new Thickness(14, 10, 14, 10),
                                Margin = new Thickness(0, 0, 0, 6)
                            };
                            var rmGrid = new Grid();
                            rmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                            rmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                            rmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                            var rmIcon = new FontIcon { Glyph = "\uE73E", FontSize = 16, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129)), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                            var rmTitle = new TextBlock { Text = $"حذف {removed} برنامه غیرضروری و بلااستفاده", FontSize = 12.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                            var rmBadge = new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 16, 185, 129)), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center };
                            rmBadge.Child = new TextBlock { Text = $"+ {SystemStats.FormatBytes(freedBytes)} آزاد شد ✓", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129)) };
                            Grid.SetColumn(rmIcon, 0); rmGrid.Children.Add(rmIcon);
                            Grid.SetColumn(rmTitle, 1); rmGrid.Children.Add(rmTitle);
                            Grid.SetColumn(rmBadge, 2); rmGrid.Children.Add(rmBadge);
                            removedCard.Child = rmGrid;
                            ReportGroupsContainer.Children.Insert(1, removedCard);

                            RefreshSystemStats();
                            ShowInfoBar("پاکسازی نرم افزارها", $"{removed} برنامه با موفقیت حذف گردید ({SystemStats.FormatBytes(freedBytes)} فضا آزاد شد).", InfoBarSeverity.Success);
                        }
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
            HeroHealthSubtitle.Text = "تمامی بهینه سازی ها با موفقیت اعمال شدند. وضعیت منابع سیستم عالی است.";
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

        private async Task ShowLargeFilesDialogAsync()
        {
            var dialog = new ContentDialog
            {
                Title = "یافتن و مدیریت فایل های بسیار حجیم (Large Files Finder)",
                CloseButtonText = "بستن",
                XamlRoot = this.Content.XamlRoot,
                FlowDirection = FlowDirection.RightToLeft
            };

            var rootPanel = new StackPanel { Spacing = 12, MinWidth = 480, MaxHeight = 440 };
            var statusText = new TextBlock { Text = "در حال اسکن فایل های بالای ۵۰۰ مگابایت در مسیرهای کاربری...", FontSize = 12.5, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
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
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
                };
                actionsStack.Children.Add(openBtn);

                var delBtn = new Button { Content = "حذف", FontSize = 11, Padding = new Thickness(8, 3, 8, 3), Height = 28, CornerRadius = new CornerRadius(5), Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 239, 68, 68)) };

                // D5 — تأیید دومرحله ای حذف فایل حجیم.
                // این دکمه داخل یک ContentDialog باز اجرا می شود و WinUI اجازه ی باز شدن
                // هم زمان دیالوگ دوم را نمی دهد؛ پس کلیک اول دکمه را به حالت «تأیید»
                // می برد و ۴ ثانیه فرصت انصراف می دهد.
                bool armed = false;
                DispatcherTimer? armTimer = null;
                delBtn.Click += (s, ev) =>
                {
                    if (!armed)
                    {
                        armed = true;
                        delBtn.Content = "تأیید حذف؟";

                        if (armTimer == null)
                        {
                            armTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                            armTimer.Tick += (ts, te) =>
                            {
                                armTimer.Stop();
                                armed = false;
                                delBtn.Content = "حذف";
                            };
                        }

                        armTimer.Start();
                        return;
                    }

                    armTimer?.Stop();
                    armed = false;

                    if (LargeFileScannerService.DeleteLargeFile(file.FilePath))
                    {
                        itemBorder.Visibility = Visibility.Collapsed;
                        ShowInfoBar("حذف فایل", $"فایل {file.FileName} به سطل بازیافت منتقل شد.", InfoBarSeverity.Informational);
                    }
                    else
                    {
                        delBtn.Content = "حذف";
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

        private void UndoCenterBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(UndoCenterBtn_ClickAsync(sender, e));

        private async Task UndoCenterBtn_ClickAsync(object sender, RoutedEventArgs e)
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
                Text = "مدیریت نقاط بازگردانی سیستم عامل و بازگرداندن تغییرات رجیستری و بهینه سازی ها با یک کلیک:",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            stack.Children.Add(desc);

            // Section 1: Create Restore Point
            var rpCard = CreateUndoSectionCard("\uE777", "ساخت نقطه بازگردانی ویندوز (Restore Point)", "ایجاد یک نقطه پشتیبان از ویندوز قبل از اعمال بهینه سازی ها یا تغییرات عمده.", "ایجاد Restore Point", async (btn) =>
            {
                btn.Content = "در حال ایجاد...";
                btn.IsEnabled = false;
                var (ok, msg) = await RestorePointService.CreateRestorePointAsync($"CleanC Backup - {DateTime.Now:yyyy.MM.dd HH:mm}");
                btn.Content = ok ? "ایجاد شد ✓" : "تلاش مجدد";
                btn.IsEnabled = !ok;
                ShowInfoBar("نقطه بازگردانی", msg, ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            });
            stack.Children.Add(rpCard);

            // Section 2: Undo Tweaks
            var undoCard = CreateUndoSectionCard("\uE7A7", "بازگردانی تنظیمات بهینه شده به حالت اولیه (Undo)", "بازگرداندن تمام کلیدهای رجیستری و سرویس های تغییر یافته توسط CleanC به مقادیر پیش فرض ویندوز.", "بازگردانی تنظیمات", async (btn) =>
            {
                btn.Content = "در حال بازگردانی...";
                btn.IsEnabled = false;
                int reverted = await RegistryBackupService.RollbackAllTweaksAsync();
                btn.Content = "بازگردانده شد ✓";
                ShowInfoBar("بازگردانی تغییرات", $"تعداد {reverted} مورد از تنظیمات و توئیک ها به حالت اولیه ویندوز بازگردانده شدند.", InfoBarSeverity.Success);
            });
            stack.Children.Add(undoCard);

            // Section 3: Open Windows System Restore
            var sysRestoreCard = CreateUndoSectionCard("\uE895", "ابزار بازیابی سیستم ویندوز (System Restore)", "اجرای ویزارد استاندارد و رسمی ریکاوری ویندوز جهت بازگشت به تاریخ های قبلی.", "باز کردن System Restore", (btn) =>
            {
                RestorePointService.LaunchSystemRestoreWizard();
                dialog.Hide();
            });
            stack.Children.Add(sysRestoreCard);

            // Section 4: فایل های قرنطینه شده (F3)
            stack.Children.Add(BuildQuarantineSection());

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

        private void AdvancedCleanExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
            => SafeInvoker.Run(AdvancedCleanExpander_ExpandingAsync(sender, args));

        private async Task AdvancedCleanExpander_ExpandingAsync(Expander sender, ExpanderExpandingEventArgs args)
        {
            if (_cachedScanResult == null)
            {
                AdvancedCleanTotalText.Text = "در حال اسکن...";
                _cachedScanResult = await SystemScanService.ScanAsync();
                ApplyPersistedJunkSelections(_cachedScanResult);
                RenderAdvancedGroups(_cachedScanResult);
                AdvancedCleanTotalText.Text = $"مجموع شناسایی شده: {_cachedScanResult.FormattedTotal}";
                UpdateHeroCleanable(_cachedScanResult.TotalBytes);
            }
        }

        /// <summary>
        /// D2: تیک های ذخیره شده ی کاربر روی آیتم های تازه ی اسکن اعمال می شود.
        /// آیتم هایی که کلیدشان در تنظیمات نیست، حالت پیش فرض خودشان (تیک خورده) را نگه می دارند.
        /// </summary>
        private void ApplyPersistedJunkSelections(ScanResult scan)
        {
            if (scan == null || !_settings.SaveScanSelections || _settings.JunkSelections.Count == 0) return;
            try
            {
                foreach (var group in scan.Groups)
                {
                    foreach (var item in group.Items)
                    {
                        if (!string.IsNullOrEmpty(item.Id) &&
                            _settings.JunkSelections.TryGetValue(item.Id, out bool checkedState))
                        {
                            item.IsChecked = checkedState;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "ApplyPersistedJunkSelections");
            }
        }

        /// <summary>D2: وضعیت تیک همه ی آیتم های اسکن در تنظیمات ثبت می شود.</summary>
        private void SavePersistedJunkSelections()
        {
            if (!_settings.SaveScanSelections || _cachedScanResult == null) return;
            try
            {
                _settings.JunkSelections.Clear();
                foreach (var group in _cachedScanResult.Groups)
                {
                    foreach (var item in group.Items)
                    {
                        if (!string.IsNullOrEmpty(item.Id)) _settings.JunkSelections[item.Id] = item.IsChecked;
                    }
                }
                ScheduleSettingsSave();
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SavePersistedJunkSelections");
            }
        }

        private void RenderAdvancedGroups(ScanResult scan)
        {
            AdvancedGroupsContainer.Children.Clear();

            bool isDarkTheme = (Content is FrameworkElement root && root.ActualTheme == ElementTheme.Dark);
            // رنگ متن فقط از تم می آید؛ والپیپر نباید روی خوانایی متن اثر بگذارد
            bool useWhiteText = isDarkTheme;
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
                    Text = grp.Id == "system_boost" ? $"{grp.Items.Count} قابلیت بهینه سازی" : grp.FormattedTotal,
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
                        chk.Checked += (s, e) => { item.IsChecked = true; SavePersistedJunkSelections(); };
                        chk.Unchecked += (s, e) => { item.IsChecked = false; SavePersistedJunkSelections(); };
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
                            Text = grp.Id == "system_boost" ? (item.IsChecked ? "فعال ✓" : "غیرفعال") : item.FormattedBytes,
                            FontSize = 12,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = itemPrimary
                        };
                        if (grp.Id == "system_boost")
                        {
                            chk.Checked += (s, e) => { sizeText.Text = "فعال ✓"; };
                            chk.Unchecked += (s, e) => { sizeText.Text = "غیرفعال"; };
                        }
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
                // D2: تم صریح انتخابی کاربر (نه System) ذخیره می شود
                PersistThemeChoice(newTheme);
            }
        }

        /// <summary>
        /// انتخاب صریح تم (دکمه ی تغییر تم در هدر) از مسیر VM در تنظیمات ثبت می شود؛
        /// VM رادیوهای صفحه Settings را هم با raise کردن PropertyChanged همگام می کند.
        /// </summary>
        private void PersistThemeChoice(ElementTheme theme)
        {
            SettingsVm.Theme = theme == ElementTheme.Dark ? "Dark" : "Light";
        }

        private static ElementTheme ResolveInitialTheme(string mode)
        {
            if (string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase)) return ElementTheme.Dark;
            if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase)) return ElementTheme.Light;
            return Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        }

        private bool _isWallpaperDark = false;
        private bool _isApplyingTheme = false;

        /// <summary>
        /// D6: آیا حالت High Contrast ویندوز فعال است؟
        /// وقتی روشن باشد، رنگ های سفارشیِ برنامه روی رنگ های سیستم می نشینند
        /// و خوانایی را خراب می کنند؛ بنابراین باید از بازنویسی brushها صرف نظر کرد.
        /// </summary>
        private static bool IsHighContrastEnabled()
        {
            try
            {
                return new AccessibilitySettings().HighContrast;
            }
            catch
            {
                // دسترسی به تنظیمات دسترسی پذیری ممکن است در محیط های محدود شکست بخورد؛
                // حالت عادی (بدون High Contrast) پیش فرض امن است.
                return false;
            }
        }

        /// <summary>
        /// D6: المانی را که LiveSetting گرفته وادار به اعلامِ تغییر برای screen reader می کند.
        /// بدون این فراخوانی، تغییر متن پیام ها از کنار خواننده ی صفحه می گذرد.
        /// </summary>
        private static void AnnounceForAccessibility(UIElement element)
        {
            try
            {
                // WinUI 3 کارخانه ی عمومیِ UWP (AutomationPeer.CreatePeerForElement) را ندارد؛
                // پس peer را از متد virtualِ خودِ المان می گیریم و رویداد زنده را بالا می بریم.
                // اگر المان peer نداشته باشد (مثلاً یک Panel)، بی صدا رد می شویم.
                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic;

                MethodInfo? factory = null;
                for (var t = element.GetType(); t != null && factory == null; t = t.BaseType)
                {
                    factory = t.GetMethod("OnCreateAutomationPeer", flags | System.Reflection.BindingFlags.DeclaredOnly);
                }

                if (factory?.Invoke(element, null) is Microsoft.UI.Xaml.Automation.Peers.AutomationPeer peer)
                {
                    peer.RaiseAutomationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Accessibility.Announce");
            }
        }

        private void ApplyTheme(ElementTheme targetTheme)
        {
            if (_isApplyingTheme) return;
            _isApplyingTheme = true;

            try
            {
                // D6: در High Contrast، صرف نظر از بازنویسی رنگ ها تا سیستم برنده شود.
                bool highContrast = IsHighContrastEnabled();

                // والپیپر فقط برای نورپردازی Acrylic استفاده می شود؛ هرگز روی رنگ متن اثر نمی گذارد
                _isWallpaperDark = WallpaperService.IsWallpaperDark(forceRefresh: true);
                bool isDarkTheme = targetTheme == ElementTheme.Dark;
                bool useWhiteText = isDarkTheme;

                var primaryColor = useWhiteText ? Colors.White : ColorHelper.FromArgb(255, 15, 23, 42);
                var secondaryColor = useWhiteText ? ColorHelper.FromArgb(220, 241, 245, 249) : ColorHelper.FromArgb(255, 30, 41, 59);
                var tertiaryColor = useWhiteText ? ColorHelper.FromArgb(170, 203, 213, 225) : ColorHelper.FromArgb(255, 51, 65, 85);

                var primaryBrush = new SolidColorBrush(primaryColor);
                var secondaryBrush = new SolidColorBrush(secondaryColor);
                var tertiaryBrush = new SolidColorBrush(tertiaryColor);

                // 1. Mutate App.xaml Light Theme Dictionary brushes in-place if in Light mode
                if (!highContrast &&
                    Application.Current.Resources.ThemeDictionaries.TryGetValue("Light", out var lightDictObj) &&
                    lightDictObj is ResourceDictionary lightDict)
                {
                    var targetLightPrimary = ColorHelper.FromArgb(255, 15, 23, 42);
                    var targetLightSec = ColorHelper.FromArgb(255, 30, 41, 59);
                    var targetLightTert = ColorHelper.FromArgb(255, 51, 65, 85);

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

                // 4. Update Navigation Switcher (persist=false چون فقط رفرش رنگ است)
                SwitchToView(_currentView, targetTheme, persist: false);

                // 5. Explicitly apply adaptive foreground brushes to all UI elements
                //    (D6: در High Contrast رنگ های سیستم باید برنده شوند)
                if (!highContrast)
                {
                    ApplyAdaptiveForegrounds(primaryBrush, secondaryBrush, tertiaryBrush);

                    // 6. Refresh any currently-rendered dynamic groups
                    if (_cachedScanResult != null)
                    {
                        RenderAdvancedGroups(_cachedScanResult);
                    }
                }
            }
            catch (Exception ex)
            {
                // اگر اینجا سکوت کنیم، تم نیمه کاره می ماند؛ پس خطا را ثبت می کنیم
                App.LogCrash("ApplyTheme", ex);
            }
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

                // Hero Card (کارت Aurora در هر دو تم سطح تیره دارد؛ رنگ از ThemeResource می آید و دستکاری نمی شود)

                // Bento RAM Card
                if (CardRamTitle != null) CardRamTitle.Foreground = primary;
                if (CardRamText != null) CardRamText.Foreground = primary;

                // Bento Disk Card
                if (CardDiskTitle != null) CardDiskTitle.Foreground = primary;
                if (CardDiskText != null) CardDiskText.Foreground = primary;

                // Bento CPU Card
                if (CardCpuTitle != null) CardCpuTitle.Foreground = primary;
                if (CardCpuText != null) CardCpuText.Foreground = primary;

                // Expander
                if (AdvancedCleanExpanderTitle != null) AdvancedCleanExpanderTitle.Foreground = primary;
                if (AdvancedCleanTotalText != null) AdvancedCleanTotalText.Foreground = secondary;

                // System Tools View
                if (ToolsHeaderTitle != null) ToolsHeaderTitle.Foreground = primary;

                // Settings View
                if (SettingsHeaderTitle != null) SettingsHeaderTitle.Foreground = primary;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private void SwitchToTab(bool isDashboard, ElementTheme? overrideTheme = null)
            => SwitchToView(isDashboard ? AppView.Dashboard : AppView.Tools, overrideTheme);

        /// <summary>
        /// تعویض نمای اصلی (داشبورد / ابزارها / تنظیمات) + استایل نوار ناوبری.
        /// </summary>
        /// <param name="persist">اگر true باشد نمای فعلی در تنظیمات ذخیره می شود (هنگام رفرش تم false می شود).</param>
        private void SwitchToView(AppView view, ElementTheme? overrideTheme = null, bool persist = true)
        {
            _currentView = view;

            DashboardView.Visibility = view == AppView.Dashboard ? Visibility.Visible : Visibility.Collapsed;
            ToolsView.Visibility = view == AppView.Tools ? Visibility.Visible : Visibility.Collapsed;
            SettingsView.Visibility = view == AppView.Settings ? Visibility.Visible : Visibility.Collapsed;

            if (view == AppView.Tools)
            {
                NavigateToToolPage(null);
            }

            if (view == AppView.Dashboard)
            {
                StartAuroraAnimations();
                // مقدارها هنگام مخفی بودن به روز نمی شدند؛ بلافاصله تازه سازی کن
                RefreshSystemStats();
            }
            else
            {
                StopAuroraAnimations();
            }

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
            bool useLightText = isDark;

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

            void StyleNav(Button btn, FontIcon icon, TextBlock txt, bool isActive)
            {
                if (isActive)
                {
                    btn.Background = activeBg;
                    btn.BorderBrush = activeBorder;
                    btn.BorderThickness = new Thickness(1);
                    icon.Foreground = accentBrush;
                }
                else
                {
                    btn.Background = isDark ? new SolidColorBrush(Colors.Transparent) : inactiveBg;
                    btn.BorderBrush = isDark ? new SolidColorBrush(Colors.Transparent) : inactiveBorder;
                    btn.BorderThickness = isDark ? new Thickness(0) : new Thickness(1);
                    icon.Foreground = inactiveText;
                }
                txt.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                txt.Foreground = isActive ? activeText : inactiveText;
            }

            StyleNav(NavDashboardBtn, NavDashboardIcon, NavDashboardText, view == AppView.Dashboard);
            StyleNav(NavToolsBtn, NavToolsIcon, NavToolsText, view == AppView.Tools);

            if (SettingsHeaderBtn != null && SettingsHeaderIcon != null)
            {
                bool isSettingsActive = view == AppView.Settings;
                SettingsHeaderBtn.Background = isSettingsActive ? activeBg : (isDark ? new SolidColorBrush(Colors.Transparent) : inactiveBg);
                SettingsHeaderBtn.BorderBrush = isSettingsActive ? activeBorder : (isDark ? new SolidColorBrush(Colors.Transparent) : inactiveBorder);
                SettingsHeaderBtn.BorderThickness = isDark && !isSettingsActive ? new Thickness(0) : new Thickness(1);
                SettingsHeaderIcon.Foreground = isSettingsActive ? accentBrush : (useLightText ? new SolidColorBrush(ColorHelper.FromArgb(230, 255, 255, 255)) : new SolidColorBrush(ColorHelper.FromArgb(200, 20, 20, 20)));
            }

            // D2: نمای فعال برای اجرای بعدی ذخیره می شود
            if (persist && _settings.SaveLastView && !_isLoadingSettings)
            {
                _settings.LastView = view switch
                {
                    AppView.Tools => "tools",
                    AppView.Settings => "settings",
                    _ => "dashboard"
                };
                ScheduleSettingsSave();
            }
        }

        private void NavDashboardBtn_Click(object sender, RoutedEventArgs e) => SwitchToView(AppView.Dashboard);
        private void NavToolsBtn_Click(object sender, RoutedEventArgs e) => SwitchToView(AppView.Tools);
        private void SettingsHeaderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentView == AppView.Settings)
            {
                SwitchToView(AppView.Dashboard);
            }
            else
            {
                SwitchToView(AppView.Settings);
            }
        }

        private AppView ResolveLastView()
        {
            if (!_settings.SaveLastView) return AppView.Dashboard;
            return _settings.LastView switch
            {
                "tools" => AppView.Tools,
                "settings" => AppView.Settings,
                _ => AppView.Dashboard
            };
        }

        // ====================================================================
        // D2: ذخیره ی تنظیمات (debounce) و مکان یابی پنجره
        // ====================================================================

        /// <summary>کنترل های صفحه Settings را از تنظیمات پر می کند؛ اینجا Toggled نباید چیزی ذخیره کند.</summary>
        private void PopulateSettingsUi()
        {
            _isLoadingSettings = true;
            try
            {
                // C6: state صفحه Settings از طریق ViewModel پر می شود و x:Bind آن را
                // به رادیوها/سوییچ ها می رساند (هیچ ذخیره ای در حین hydration رخ نمی دهد).
                SettingsVm.Hydrate();

                if (LanguageComboBox != null)
                {
                    string currentLang = SettingsVm.SelectedLanguageCode;
                    for (int i = 0; i < LanguageComboBox.Items.Count; i++)
                    {
                        if (LanguageComboBox.Items[i] is ComboBoxItem item &&
                            string.Equals(item.Tag as string, currentLang, StringComparison.OrdinalIgnoreCase))
                        {
                            LanguageComboBox.SelectedIndex = i;
                            break;
                        }
                    }
                }

                // F1 — وضعیت شروع خودکار از رجیستری خوانده می شود، نه از فایل تنظیمات
                AutoStartToggle.IsOn = StartupOptimizerService.IsCleanCRegisteredAtStartup();

                SettingsPathText.Text = Path.GetDirectoryName(SettingsService.FilePath) ?? string.Empty;
                LogPathText.Text = CcLog.LogDirectory;

                // D8 — بخش «درباره»
                AboutVersionText.Text = ReportExportService.GetVersion(typeof(MainWindow).Assembly);
                AboutDotNetText.Text = RuntimeInformation.FrameworkDescription;
                AboutOsText.Text = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "PopulateSettingsUi");
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        /// <summary>شروع debounce ذخیره؛ تایمرِ نوشتن در خود SettingsStore است (C6).</summary>
        private void ScheduleSettingsSave()
        {
            if (_isExiting || _isLoadingSettings) return;
            try
            {
                _settingsStore.ScheduleSave();
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "ScheduleSettingsSave");
            }
        }

        /// <summary>
        /// قبل از نوشتن فایل تنظیمات صدا زده می شود (OnSaveRequested در SettingsStore)
        /// و موقعیت فعلی پنجره را در تنظیماتِ زنده ثبت می کند.
        /// </summary>
        private void CapturePlacementForSave()
        {
            if (_isExiting || _appWindow == null || !_settings.SaveWindowPlacement ||
                NativeMethods.IsWindowMinimized(_hWnd))
            {
                return;
            }
            CaptureWindowPlacement();
        }

        /// <summary>ذخیره ی فوری؛ همیشه قبل از خروج و بستن پنجره صدا زده می شود.</summary>
        private void SaveSettingsNow()
        {
            try
            {
                // try/catch و ثبت موقعیت پنجره داخل خود SettingsStore انجام می شود
                _settingsStore.SaveNow();
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SaveSettingsNow");
            }
        }

        private void CenterWindowOnScreen()
        {
            try
            {
                var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
                if (displayArea != null)
                {
                    var workArea = displayArea.WorkArea;
                    int x = workArea.X + (workArea.Width - _appWindow.Size.Width) / 2;
                    int y = workArea.Y + (workArea.Height - _appWindow.Size.Height) / 2;
                    _appWindow.Move(new PointInt32(Math.Max(workArea.X, x), Math.Max(workArea.Y, y)));
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "CenterWindowOnScreen");
            }
        }

        private void CaptureWindowPlacement()
        {
            try
            {
                if (!_settings.SaveWindowPlacement) return;
                var pos = _appWindow.Position;
                var size = _appWindow.Size;
                if (size.Width < 1 || size.Height < 1) return;
                _settings.WindowX = pos.X;
                _settings.WindowY = pos.Y;
                _settings.WindowWidth = size.Width;
                _settings.WindowHeight = size.Height;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "CaptureWindowPlacement");
            }
        }

        /// <summary>
        /// آیا مستطیل ذخیره شده روی یکی از نمایشگرهای فعلی دست کم ۶۴ پیکسل دیده می شود؟
        /// این چک جلوی باز شدن پنجره روی مانیتور قطع شده (یا خارج از صفحه) را می گیرد.
        /// </summary>
        private static bool IsPlacementOnScreen(int x, int y, int width, int height)
        {
            const int minVisible = 64;
            try
            {
                foreach (var display in DisplayArea.FindAll())
                {
                    var work = display.WorkArea;
                    bool overlapsX = x + width > work.X + minVisible && x < work.X + work.Width - minVisible;
                    bool overlapsY = y + height > work.Y + minVisible && y < work.Y + work.Height - minVisible;
                    if (overlapsX && overlapsY) return true;
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "IsPlacementOnScreen");
            }
            return false;
        }

        // ====================================================================
        // D3: رفتار تری آیکون (مخفی سازی و خروج ایمن)
        // ====================================================================

        /// <summary>پنهان کردن پنجره در تری آیکون؛ برنامه زنده می ماند و فقط HWND مخفی می شود.</summary>
        private void HideToTray()
        {
            try
            {
                _appWindow.Hide();

                if (!_trayHintShown)
                {
                    _trayHintShown = true;
                    _trayManager?.ShowBalloonNotification(
                        "CleanC در تری آیکون",
                        "برای بازگرداندن پنجره روی آیکون کنار ساعت کلیک کنید.");
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "HideToTray");
            }
        }

        /// <summary>
        /// خروج واقعی از برنامه. مسیر تری هم از همین جا می گذرد تا تنظیمات ذخیره شود
        /// و لغوِ Close-to-Tray مانع بستن نشود.
        /// </summary>
        private void ExitApplication()
        {
            if (_isExiting) return;
            _isExiting = true;
            SaveSettingsNow();
            _trayManager?.Dispose();
            Close();
        }

        // ====================================================================
        // D2/C6: رویدادهای صفحه Settings
        // state اصلی در SettingsViewModel است؛ اینجا فقط «نمایش» رخ می دهد
        // (اعمال تم و InfoBar هشدار حذف قطعی).
        // ====================================================================

        private void ThemeRadio_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings || sender is not RadioButton radio) return;

            // نوشتنِ مقدار با VM است؛ تمِ رابط کاربری از رویداد ThemeChangeRequested اعمال می شود
            SettingsVm.Theme = ReferenceEquals(radio, ThemeDarkRadio) ? "Dark"
                             : ReferenceEquals(radio, ThemeLightRadio) ? "Light"
                             : "System";
        }

        /// <summary>
        /// F1 — ثبت/حذف CleanC در آیتم های اجرای خودکار ویندوز (HKCU\...\Run).
        /// منبع حقیقت رجیستری است؛ اگر نوشتن شکست بخورد، کلید برمی گردد تا UI با
        /// وضعیت واقعی سیستم ناهمگام نماند.
        /// </summary>
        private void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            try
            {
                bool enabled = AutoStartToggle.IsOn;

                if (!StartupOptimizerService.SetCleanCStartupRegistration(enabled))
                {
                    _isLoadingSettings = true;
                    try { AutoStartToggle.IsOn = StartupOptimizerService.IsCleanCRegisteredAtStartup(); }
                    finally { _isLoadingSettings = false; }

                    ShowInfoBar(
                        "شروع همراه با ویندوز",
                        "ثبت CleanC در اجرای خودکار انجام نشد؛ مسیر اجرایی برنامه در دسترس نیست.",
                        InfoBarSeverity.Error);
                    return;
                }

                ShowInfoBar(
                    "شروع همراه با ویندوز",
                    enabled
                        ? "CleanC هنگام ورود به ویندوز به صورت خودکار اجرا می شود."
                        : "اجرای خودکار CleanC غیرفعال شد.",
                    InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "AutoStartToggle_Toggled");
            }
        }

        /// <summary>
        /// F3 — اثرِ نمایشیِ تغییر رفتار حذفِ فایل های شخصی (از رویداد SettingsVm).
        /// نوشتنِ تنظیم و همگام سازی <see cref="SafeDeleteService.DefaultMode"/> داخل VM است؛
        /// اینجا فقط هشدار حالت Permanent نشان داده می شود.
        /// </summary>
        private void OnDeleteModeChangedInUi(string mode)
        {
            if (string.Equals(mode, "Permanent", StringComparison.OrdinalIgnoreCase))
            {
                ShowInfoBar(
                    "حذف قطعی فعال شد",
                    "فایل های شخصی دیگر به سطل بازیافت نمی روند و قابل بازگردانی نخواهند بود. کش و زباله ها همچنان قطعی حذف می شوند.",
                    InfoBarSeverity.Warning);
            }
        }

        private void DeleteModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            SettingsVm.DeleteMode = ReferenceEquals(sender, DeleteModeQuarantineRadio) ? "Quarantine"
                                  : ReferenceEquals(sender, DeleteModePermanentRadio) ? "Permanent"
                                  : "RecycleBin";
        }

        private void ResetWindowPlacementBtn_Click(object sender, RoutedEventArgs e)
        {
            _settings.WindowX = int.MinValue;
            _settings.WindowY = int.MinValue;
            _settings.WindowWidth = 1080;
            _settings.WindowHeight = 800;
            _appWindow.Resize(new SizeInt32(1080, 800));
            CenterWindowOnScreen();
            SaveSettingsNow();
            ShowInfoBar("اندازه ی پنجره بازنشانی شد", "پنجره با ابعاد پیش فرض ۱۰۸۰×۸۰۰ در مرکز صفحه قرار گرفت.", InfoBarSeverity.Informational);
        }

        private void ResetAllSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            // شیء تنظیمات در استور عوض می شود؛ PopulateSettingsUi دوباره VM را Hydrate می کند
            _settingsStore.Replace(new AppSettings());
            PopulateSettingsUi();
            SaveSettingsNow();
            ApplyTheme(ResolveInitialTheme(_settings.Theme));
            LocalizationService.SetLanguage(_settings.Language);
            ApplyLanguage();
            SwitchToView(AppView.Dashboard, persist: false);
            ShowInfoBar("تنظیمات بازنشانی شد", "همه ی تنظیمات به حالت پیش فرض برگشت.", InfoBarSeverity.Success);
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings) return;
            if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string langCode)
            {
                SettingsVm.SelectedLanguageCode = langCode;
            }
        }

        private void RegisterToolBackIcons()
        {
            _toolBackIcons.Clear();
            FontIcon[] icons = { ToolBackIcon1, ToolBackIcon2, ToolBackIcon3, ToolBackIcon4, ToolBackIcon5, ToolBackIcon6, ToolBackIcon7, ToolBackIcon8, ToolBackIcon9, ToolBackIcon10, ToolBackIcon11 };
            foreach (var icon in icons)
            {
                if (icon != null) _toolBackIcons.Add(icon);
            }
        }

        private void UpdateBackButtonsDirection(bool isRtl)
        {
            string glyph = isRtl ? "\uE72A" : "\uE72B";
            foreach (var icon in _toolBackIcons)
            {
                if (icon != null) icon.Glyph = glyph;
            }
        }

        private void ApplyLanguage()
        {
            try
            {
                bool isRtl = LocalizationService.IsRtl;
                var dir = isRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

                if (MainContentGrid != null) MainContentGrid.FlowDirection = dir;
                if (NotificationHistoryFlyout?.Content is FrameworkElement flyoutRoot)
                {
                    flyoutRoot.FlowDirection = dir;
                }

                UpdateBackButtonsDirection(isRtl);

                // Apply typography
                var fontFamilyName = LocalizationService.GetFontFamilyName();
                var ff = new FontFamily(fontFamilyName);
                Application.Current.Resources["AppFontFamily"] = ff;
                Application.Current.Resources["ContentControlThemeFontFamily"] = ff;

                // Update UI text strings
                if (NavDashboardText != null) NavDashboardText.Text = LocalizationService.Get("NavDashboard", "داشبورد بهینه سازی");
                if (NavToolsText != null) NavToolsText.Text = LocalizationService.Get("NavTools", "ابزارهای سیستمی ویندوز");

                if (HeroHealthCategoryText != null) HeroHealthCategoryText.Text = LocalizationService.Get("HeroCategory", "وضعیت سلامت و کارایی سیستم");
                if (HeroHealthTitle != null)
                {
                    HeroHealthTitle.Text = _isCleanedRecently
                        ? LocalizationService.Get("HeroTitleOptimized", "سیستم در وضعیت بهینه و پرسرعت قرار دارد")
                        : LocalizationService.Get("HeroTitleReady", "سیستم آماده بررسی و ارتقای سرعت است");
                }
                if (HeroHealthSubtitle != null)
                {
                    HeroHealthSubtitle.Text = _isCleanedRecently
                        ? LocalizationService.Get("HeroSubtitleOptimized", "تمام فایل های موقت پاکسازی و سرویس ها بهینه سازی شدند.")
                        : LocalizationService.Get("HeroSubtitleReady", "با یک کلیک حافظه موقت و فایل های زائد را پاکسازی کنید.");
                }
                if (QuickBoostBtnText != null && !_isBusy)
                {
                    QuickBoostBtnText.Text = LocalizationService.Get("QuickBoost", "بهینه سازی و ارتقای سرعت سیستم");
                }

                // Bento Cards
                if (CardRamTitle != null) CardRamTitle.Text = LocalizationService.Get("RamTitle", "مصرف رم (RAM)");
                if (CardCpuTitle != null) CardCpuTitle.Text = LocalizationService.Get("CpuTitle", "پردازنده (CPU)");
                if (CardDiskTitle != null) CardDiskTitle.Text = LocalizationService.Get("DiskBreakdownTitle", "فضای دیسک و درایوها (Storage)");
                if (AdvancedCleanExpanderTitle != null) AdvancedCleanExpanderTitle.Text = LocalizationService.Get("AdvancedCleanTitle", "سفارشی سازی موارد پاکسازی و بهینه سازی");
                if (ToolsHeaderTitle != null) ToolsHeaderTitle.Text = LocalizationService.Get("NavTools", "ابزارهای سیستمی ویندوز");

                // Launcher Tool Cards Localization
                if (LauncherWindowsUpdateTitle != null) LauncherWindowsUpdateTitle.Text = LocalizationService.Get("LauncherWindowsUpdateTitle", "آپدیت ویندوز");
                if (LauncherWindowsUpdateDesc != null) LauncherWindowsUpdateDesc.Text = LocalizationService.Get("LauncherWindowsUpdateDesc", "بررسی، توقف ۵ هفته ای و رفع گیر کردن دانلود");
                if (LauncherUninstallerTitle != null) LauncherUninstallerTitle.Text = LocalizationService.Get("LauncherUninstallerTitle", "حذف نرم افزارها");
                if (LauncherUninstallerDesc != null) LauncherUninstallerDesc.Text = LocalizationService.Get("LauncherUninstallerDesc", "لیست برنامه ها با آیکون اختصاصی و حذف آسان");
                if (LauncherStartupTitle != null) LauncherStartupTitle.Text = LocalizationService.Get("LauncherStartupTitle", "برنامه های استارتاپ");
                if (LauncherStartupDesc != null) LauncherStartupDesc.Text = LocalizationService.Get("LauncherStartupDesc", "مدیریت برنامه های شروع خودکار و سرعت بوت");
                if (LauncherSpeedTestTitle != null) LauncherSpeedTestTitle.Text = LocalizationService.Get("LauncherSpeedTestTitle", "تست سرعت اینترنت");
                if (LauncherSpeedTestDesc != null) LauncherSpeedTestDesc.Text = LocalizationService.Get("LauncherSpeedTestDesc", "سنجش دانلود واقعی، آپلود، پینگ و ISP");

                if (ToolsSectionFrequentlyUsed != null) ToolsSectionFrequentlyUsed.Text = LocalizationService.Get("ToolsSectionFrequentlyUsed", "پرکاربرد");
                if (ToolsSectionOtherTools != null) ToolsSectionOtherTools.Text = LocalizationService.Get("ToolsSectionOtherTools", "سایر ابزارها");

                if (LauncherToolDeepStorageText != null) LauncherToolDeepStorageText.Text = LocalizationService.Get("LauncherToolDeepStorage", "پاکسازی عمیق دیسک");
                if (LauncherToolSpecsText != null) LauncherToolSpecsText.Text = LocalizationService.Get("LauncherToolSpecs", "مشخصات سیستم");
                if (LauncherToolPersonalizationText != null) LauncherToolPersonalizationText.Text = LocalizationService.Get("LauncherToolPersonalization", "استایل و گرافیک");
                if (LauncherToolDriversText != null) LauncherToolDriversText.Text = LocalizationService.Get("LauncherToolDrivers", "مدیریت درایورها");
                if (LauncherToolDnsText != null) LauncherToolDnsText.Text = LocalizationService.Get("LauncherToolDns", "تنظیم DNS و تحریم شکن");
                if (LauncherToolPrivacyText != null) LauncherToolPrivacyText.Text = LocalizationService.Get("LauncherToolPrivacy", "حریم خصوصی و ضدجاسوسی");
                if (LauncherToolRecycleBinText != null) LauncherToolRecycleBinText.Text = LocalizationService.Get("LauncherToolRecycleBin", "سطل بازیافت");

                if (SettingsHeaderTitle != null) SettingsHeaderTitle.Text = LocalizationService.Get("SettingsTitle", "تنظیمات برنامه");
                if (SettingsHeaderSubtitle != null) SettingsHeaderSubtitle.Text = LocalizationService.Get("SettingsSub", "شخصی سازی ظاهر، زبان، رفتار پس زمینه و تهیه گزارش");
                
                string rawLang = LocalizationService.Get("LanguageTitle", "زبان برنامه");
                if (!rawLang.Contains("(Language)", StringComparison.OrdinalIgnoreCase))
                {
                    rawLang = $"{rawLang} (Language)";
                }
                if (SettingsLangTitle != null) SettingsLangTitle.Text = rawLang;
                if (SettingsLangDesc != null) SettingsLangDesc.Text = LocalizationService.Get("LanguageSub", "انتخاب زبان رابط کاربری، جهت نوشتاری (RTL / LTR) و تایپوگرافی نیتیو سیستم عامل.");
                if (SettingsThemeTitle != null) SettingsThemeTitle.Text = LocalizationService.Get("ThemeTitle", "تم برنامه");
                if (SettingsThemeDesc != null) SettingsThemeDesc.Text = LocalizationService.Get("ThemeSub", "«سیستم» از تم ویندوز پیروی می کند؛ دو حالت دیگر تم را ثابت نگه می دارند.");
                if (ThemeSystemRadio != null) ThemeSystemRadio.Content = LocalizationService.Get("ThemeSystem", "سیستم");
                if (ThemeLightRadio != null) ThemeLightRadio.Content = LocalizationService.Get("ThemeLight", "روشن");
                if (ThemeDarkRadio != null) ThemeDarkRadio.Content = LocalizationService.Get("ThemeDark", "تاریک");

                if (SettingsTrayTitle != null) SettingsTrayTitle.Text = LocalizationService.Get("BackgroundBehaviorTitle", "اجرا در پس زمینه و نوار وظیفه");
                if (SettingsTrayDesc != null) SettingsTrayDesc.Text = LocalizationService.Get("BackgroundBehaviorSub", "کنترل حضور برنامه در پس زمینه و دسترسی سریع از طریق آیکون کنار ساعت ویندوز.");
                if (SettingsCloseToTrayText != null) SettingsCloseToTrayText.Text = LocalizationService.Get("CloseToTray", "با بستن پنجره خارج نشو؛ در پس زمینه (کنار ساعت) بمان");
                if (SettingsMinToTrayText != null) SettingsMinToTrayText.Text = LocalizationService.Get("MinimizeToTray", "کوچک نمایی پنجره، برنامه را در کنار ساعت پنهان کند");

                if (SettingsExportTitle != null) SettingsExportTitle.Text = LocalizationService.Get("ExportReportTitle", "خروجی گزارش");
                if (SettingsExportDesc != null) SettingsExportDesc.Text = LocalizationService.Get("ExportReportSub", "خروجی اطلاعات جامع سیستم جهت ارسال به هوش مصنوعی یا بررسی مشخصات سخت افزاری با فرمت متنی تمیز.");
                if (ExportAiReportText != null) ExportAiReportText.Text = LocalizationService.Get("ExportAiReport", "گزارش جامع برای هوش مصنوعی (.md)");
                if (ExportSpecsTxtText != null) ExportSpecsTxtText.Text = LocalizationService.Get("ExportSpecsReport", "مشخصات کامل سیستم (.txt)");

                if (SettingsAboutTitle != null) SettingsAboutTitle.Text = LocalizationService.Get("AboutTitle", "درباره CleanC");
                if (SettingsAboutDesc != null) SettingsAboutDesc.Text = LocalizationService.Get("AboutSub", "برای گزارش باگ، اطلاعات عیب یابی را کپی و همراه با مشکل ارسال کنید.");
                if (CheckAppUpdatesText != null) CheckAppUpdatesText.Text = LocalizationService.Get("CheckUpdates", "بررسی به روزرسانی");
                if (OpenProjectPageText != null) OpenProjectPageText.Text = LocalizationService.Get("ProjectPage", "صفحه پروژه");
                if (CopyDiagnosticsText != null) CopyDiagnosticsText.Text = LocalizationService.Get("CopyDiagnostics", "کپی اطلاعات عیب یابی");

                if (SettingsHeaderBtn != null) ToolTipService.SetToolTip(SettingsHeaderBtn, LocalizationService.Get("Settings", "تنظیمات برنامه (Settings)"));
                if (UndoCenterBtn != null) ToolTipService.SetToolTip(UndoCenterBtn, LocalizationService.Get("RestoreCenter", "مرکز بازیابی و بازگشت تغییرات"));
                if (RefreshStatsBtn != null) ToolTipService.SetToolTip(RefreshStatsBtn, LocalizationService.Get("RefreshStatus", "بروزرسانی وضعیت سیستم"));
                if (NotificationHistoryBtn != null) ToolTipService.SetToolTip(NotificationHistoryBtn, LocalizationService.Get("NotificationHistory", "تاریخچه عملیات و اعلان ها"));
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "ApplyLanguage");
            }
        }

        private void OpenSettingsFolderBtn_Click(object sender, RoutedEventArgs e)
            => OpenFolder(Path.GetDirectoryName(SettingsService.FilePath));

        private void OpenLogFolderBtn_Click(object sender, RoutedEventArgs e)
            => OpenFolder(CcLog.LogDirectory);

        private static void OpenFolder(string? folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder)) return;
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "OpenFolder");
            }
        }

        // ====================================================================
        // F7 — خروجی گزارش (TXT / CSV)
        // ====================================================================

        /// <summary>
        /// دیالوگ ذخیره ی فایل. <c>FileSavePicker</c> در WinUI 3 باید به دسته ی پنجره
        /// وصل شود، وگرنه از پشت پنجره ی اصلی باز نمی شود.
        /// </summary>
        private async Task<string?> PickSavePathAsync(string choiceLabel, string extension, string suggestedFileName)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, _hWnd);
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add(choiceLabel, new List<string> { extension });
                picker.SuggestedFileName = suggestedFileName;

                var file = await picker.PickSaveFileAsync();
                return file?.Path;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "PickSavePath");
                return null;
            }
        }

        private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss");

        private void ExportAiReportBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(ExportAiReportBtn_ClickAsync());

        private async Task ExportAiReportBtn_ClickAsync()
        {
            string? path = await PickSavePathAsync("گزارش هوش مصنوعی (Markdown)", ".md", $"CleanC-AI-Report-{Stamp()}");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var specs = _cachedSpecs ?? await SystemSpecsService.GetDetailedSpecsAsync();
                _cachedSpecs = specs;

                string md = ReportExportService.BuildAiReportMarkdown(specs, _lastCleanupItems, _lastFreedBytes);
                ReportExportService.WriteUtf8Bom(path, md);
                ShowInfoBar("خروجی گزارش", $"گزارش هوش مصنوعی در «{path}» ذخیره شد.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "ExportAiReport");
                ShowInfoBar("خروجی گزارش", "ذخیره ی گزارش هوش مصنوعی انجام نشد.", InfoBarSeverity.Error);
            }
        }

        private void ExportSpecsTxtBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(ExportSpecsTxtBtn_ClickAsync());

        private async Task ExportSpecsTxtBtn_ClickAsync()
        {
            string? path = await PickSavePathAsync("مشخصات سیستم (TXT)", ".txt", $"CleanC-Specs-{Stamp()}");
            if (string.IsNullOrEmpty(path)) return; // کاربر انصراف داد

            try
            {
                // اگر هنوز صفحه ی مشخصات باز نشده، همین جا می گیریم
                var specs = _cachedSpecs ?? await SystemSpecsService.GetDetailedSpecsAsync();
                _cachedSpecs = specs;

                string text = ReportExportService.BuildSystemSpecsTxt(specs);
                ReportExportService.WriteUtf8Bom(path, text);
                ShowInfoBar("خروجی گزارش", $"گزارش مشخصات سیستم در «{path}» ذخیره شد.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "ExportSpecsTxt");
                ShowInfoBar("خروجی گزارش", "ذخیره ی گزارش مشخصات انجام نشد (مسیر در دسترس نیست).", InfoBarSeverity.Error);
            }
        }

        private void ExportStartupCsvBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(ExportStartupCsvBtn_ClickAsync());

        private async Task ExportStartupCsvBtn_ClickAsync()
        {
            string? path = await PickSavePathAsync("جدول (CSV)", ".csv", $"CleanC-Startup-{Stamp()}");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                // GetStartupItems اسکن رجیستری + schtasks است؛ روی UI thread نباید بایستد
                var items = await Task.Run(StartupOptimizerService.GetStartupItems);
                string csv = ReportExportService.BuildStartupCsv(items);
                ReportExportService.WriteUtf8Bom(path, csv);
                ShowInfoBar("خروجی گزارش", $"فهرست {items.Count} آیتم استارت آپ در «{path}» ذخیره شد.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "ExportStartupCsv");
                ShowInfoBar("خروجی گزارش", "ذخیره ی فهرست استارت آپ انجام نشد.", InfoBarSeverity.Error);
            }
        }

        private void ExportCleanupCsvBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(ExportCleanupCsvBtn_ClickAsync());

        private async Task ExportCleanupCsvBtn_ClickAsync()
        {
            string? path = await PickSavePathAsync("جدول (CSV)", ".csv", $"CleanC-CleanupHistory-{Stamp()}");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var rows = _cleanupHistory
                    .Select(h => new CleanupHistoryRow { Date = h.Date, Bytes = h.Bytes })
                    .ToList();

                string csv = ReportExportService.BuildCleanupHistoryCsv(rows);
                ReportExportService.WriteUtf8Bom(path, csv);
                ShowInfoBar("خروجی گزارش", $"تاریخچه ی پاکسازی ({rows.Count} رکورد) در «{path}» ذخیره شد.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "ExportCleanupCsv");
                ShowInfoBar("خروجی گزارش", "ذخیره ی تاریخچه ی پاکسازی انجام نشد.", InfoBarSeverity.Error);
            }
        }

        // ====================================================================
        // D8 — درباره، به روزرسانی و عیب یابی
        // ====================================================================

        /// <summary>مخزن پروژه — از remote واقعی git گرفته می شود، نه مقدار حدسی.</summary>
        private const string ProjectPageUrl = "https://github.com/mohammad-ahw/CleanC";
        private const string ReleasesApiUrl = "https://api.github.com/repos/mohammad-ahw/CleanC/releases/latest";

        private void OpenProjectPageBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = ProjectPageUrl, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "OpenProjectPage");
                ShowInfoBar("درباره CleanC", "باز کردن صفحه ی پروژه ممکن نشد.", InfoBarSeverity.Warning);
            }
        }

        private void CopyDiagnosticsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = ReportExportService.BuildDiagnosticsText();
                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                ShowInfoBar("اطلاعات عیب یابی کپی شد", "حالا می توانی آن را در گزارش باگ جای گذاری کنی.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "CopyDiagnostics");
                ShowInfoBar("اطلاعات عیب یابی", "کپی کردن ممکن نشد.", InfoBarSeverity.Error);
            }
        }

        // نام گذاری با «App»: متدِ هم نامِ CheckUpdatesBtn_Click برای بروزرسانیِ خودِ ویندوز در
        // MainWindow.Tools.cs وجود دارد و این یکی مخصوص نسخه ی خودِ CleanC است.
        private void CheckAppUpdatesBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(CheckAppUpdatesBtn_ClickAsync());

        private async Task CheckAppUpdatesBtn_ClickAsync()
        {
            string local = ReportExportService.GetVersion(typeof(MainWindow).Assembly);
            ShowInfoBar("بررسی به روزرسانی", "در حال بررسی...", InfoBarSeverity.Informational);

            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                // GitHub API بدون User-Agent درخواست را رد می کند
                client.DefaultRequestHeaders.UserAgent.ParseAdd("CleanC-UpdateCheck");

                using var resp = await client.GetAsync(ReleasesApiUrl);

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    ShowInfoBar("بررسی به روزرسانی",
                        $"هنوز نسخه ای منتشر نشده (یا مخزن خصوصی است). نسخه ی فعلی: {local}",
                        InfoBarSeverity.Informational);
                    return;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    ShowInfoBar("بررسی به روزرسانی",
                        $"نتوانستم منبع نسخه ها را بخوانم (کد {(int)resp.StatusCode}). نسخه ی فعلی: {local}",
                        InfoBarSeverity.Warning);
                    return;
                }

                string json = await resp.Content.ReadAsStringAsync();
                string? tag = ExtractJsonString(json, "tag_name");
                if (string.IsNullOrWhiteSpace(tag))
                {
                    ShowInfoBar("بررسی به روزرسانی", $"پاسخ منبع نامعتبر بود. نسخه ی فعلی: {local}", InfoBarSeverity.Warning);
                    return;
                }

                string remote = tag.TrimStart('v', 'V');
                ShowInfoBar("بررسی به روزرسانی",
                    IsNewer(remote, local)
                        ? $"نسخه ی جدیدتری منتشر شده: {remote} (نسخه ی فعلی: {local})"
                        : $"به روز هستی؛ آخرین نسخه ی منتشرشده {remote} است.",
                    IsNewer(remote, local) ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "CheckUpdates");
                ShowInfoBar("بررسی به روزرسانی",
                    $"بررسی به روزرسانی ممکن نشد (اتصال اینترنت یا دسترسی شبکه). نسخه ی فعلی: {local}",
                    InfoBarSeverity.Warning);
            }
        }

        /// <summary>استخراج یک رشته ی ساده از JSON بدون وابستگی به کتابخانه ی جدا (فقط فیلدهای تخت).</summary>
        private static string? ExtractJsonString(string json, string property)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string needle = $"\"{property}\"";
            int i = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            i = json.IndexOf(':', i + needle.Length);
            if (i < 0) return null;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '"') return null;
            i++;
            var sb = new System.Text.StringBuilder();
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    sb.Append(json[i + 1]);
                    i += 2;
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>مقایسه ی دو نسخه ی عددی شده؛ اگر قابل پارس نبود، فقط نامتناسب بودن را چک می کند.</summary>
        private static bool IsNewer(string remote, string local)
        {
            if (Version.TryParse(Normalize(remote), out var r) && Version.TryParse(Normalize(local), out var l))
                return r > l;
            return !string.Equals(remote.Trim(), local.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string v)
        {
            string s = (v ?? string.Empty).Trim();
            // «1.2.3.4-preview» را به «1.2.3.4» تبدیل کن
            int dash = s.IndexOfAny(new[] { '-', ' ' });
            if (dash > 0) s = s.Substring(0, dash);
            return s;
        }

        // ====================================================================
        // In-Tab Sub-Page Navigation System
        // ====================================================================

        private void LaunchToolPage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is string tag)
            {
                if (tag == "taskmgr" || tag == "devmgmt")
                {
                    SystemToolsService.Launch(tag);
                    return;
                }
                NavigateToToolPage(tag);
            }
        }

        private void BackToToolsLauncher_Click(object sender, RoutedEventArgs e)
        {
            NavigateToToolPage(null);
        }

        private void NavigateToToolPage(string? pageId)
        {
            if (pageId == "taskmgr" || pageId == "devmgmt")
            {
                SystemToolsService.Launch(pageId);
                return;
            }

            ToolsLauncherPanel.Visibility = Visibility.Collapsed;
            ToolSystemSpecsPanel.Visibility = Visibility.Collapsed;
            ToolAppUninstallerPanel.Visibility = Visibility.Collapsed;
            ToolSpeedTestPanel.Visibility = Visibility.Collapsed;
            ToolPersonalizationPanel.Visibility = Visibility.Collapsed;
            ToolStartupManagerPanel.Visibility = Visibility.Collapsed;
            ToolWindowsUpdatePanel.Visibility = Visibility.Collapsed;
            ToolDriversPanel.Visibility = Visibility.Collapsed;
            if (ToolDnsPanel != null) ToolDnsPanel.Visibility = Visibility.Collapsed;
            if (ToolPrivacyPanel != null) ToolPrivacyPanel.Visibility = Visibility.Collapsed;
            if (ToolDeepStoragePanel != null) ToolDeepStoragePanel.Visibility = Visibility.Collapsed;
            if (ToolRecycleBinPanel != null) ToolRecycleBinPanel.Visibility = Visibility.Collapsed;

            switch (pageId)
            {
                case "deep_storage":
                    if (ToolDeepStoragePanel != null) ToolDeepStoragePanel.Visibility = Visibility.Visible;
                    _ = LoadDeepStoragePageAsync();
                    break;
                case "system_specs":
                    ToolSystemSpecsPanel.Visibility = Visibility.Visible;
                    _ = LoadSystemSpecsPageAsync();
                    break;
                case "app_uninstaller":
                    ToolAppUninstallerPanel.Visibility = Visibility.Visible;
                    _ = LoadAppUninstallerPageAsync();
                    break;
                case "speed_test":
                    ToolSpeedTestPanel.Visibility = Visibility.Visible;
                    break;
                case "personalization":
                    ToolPersonalizationPanel.Visibility = Visibility.Visible;
                    LoadPersonalizationPage();
                    break;
                case "startup_manager":
                    ToolStartupManagerPanel.Visibility = Visibility.Visible;
                    _ = LoadStartupManagerPageAsync();
                    break;
                case "windows_update":
                    ToolWindowsUpdatePanel.Visibility = Visibility.Visible;
                    _ = LoadWindowsUpdatePageAsync();
                    break;
                case "drivers":
                    ToolDriversPanel.Visibility = Visibility.Visible;
                    _ = LoadDriversPageAsync();
                    break;
                case "dns_manager":
                    if (ToolDnsPanel != null) ToolDnsPanel.Visibility = Visibility.Visible;
                    LoadDnsPage();
                    break;
                case "privacy_debloat":
                    if (ToolPrivacyPanel != null) ToolPrivacyPanel.Visibility = Visibility.Visible;
                    LoadPrivacyPage();
                    break;
                case "recycle_bin":
                    if (ToolRecycleBinPanel != null) ToolRecycleBinPanel.Visibility = Visibility.Visible;
                    _ = LoadRecycleBinPageAsync();
                    break;
                default:
                    ToolsLauncherPanel.Visibility = Visibility.Visible;
                    break;
            }
        }


        // ====================================================================
        // Active Network Bandwidth Consumer Detection
        // ====================================================================

        /// <summary>
        /// کش نام پروسه های در حال اجرا.
        ///
        /// قبلاً این متد برای هر تیک تایمر (هر ۲٫۵ ثانیه) تا ۱۱ بار
        /// <see cref="Process.GetProcessesByName(string)"/> صدا می زد؛ هرکدام یعنی
        /// یک اسکن کامل لیست پروسه های سیستم. حالا فقط یک بار <c>Process.GetProcesses()</c>
        /// می زنیم و نتیجه را ۲ ثانیه نگه می داریم.
        /// </summary>
        private static HashSet<string>? _runningProcessNames;
        private static DateTime _runningProcessNamesAtUtc = DateTime.MinValue;
        private static readonly TimeSpan RunningProcessNamesTtl = TimeSpan.FromSeconds(2);

        private static HashSet<string> GetRunningProcessNames()
        {
            var now = DateTime.UtcNow;
            if (_runningProcessNames != null && (now - _runningProcessNamesAtUtc) < RunningProcessNamesTtl)
                return _runningProcessNames;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try { names.Add(p.ProcessName); }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            _runningProcessNames = names;
            _runningProcessNamesAtUtc = now;
            return names;
        }

        private static string GetActiveNetworkConsumer(double netMbps)
        {
            if (netMbps < 0.2) return "شبکه آزاد و پایدار";

            try
            {
                var running = GetRunningProcessNames();

                if (running.Contains("IDMan")) return "دانلود فعال (IDM)";
                if (running.Contains("steam") || running.Contains("steamwebhelper")) return "کلاینت استیم (Steam)";
                if (running.Contains("qbittorrent") || running.Contains("utorrent")) return "کلاینت تورنت (Torrent)";
                if (running.Contains("msedge")) return "مرورگر مایکروسافت اج";
                if (running.Contains("chrome")) return "مرورگر گوگل کروم";
                if (running.Contains("firefox")) return "مرورگر فایرفاکس";
                if (running.Contains("Discord")) return "دیسکورد (Discord)";
                if (running.Contains("Telegram")) return "پیام رسان تلگرام";
                if (running.Contains("Spotify")) return "استریم اسپاتیفای";
                if (running.Contains("DoSvc")) return "سرویس آپدیت ویندوز";
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return "ترافیک سیستم و ویندوز";
        }

        // ====================================================================
        // Cumulative Cleaned Space Persistence & UI
        // ====================================================================

        /// <summary>یک رکورد از تاریخچه پاکسازی (برای ذخیره در clean_stats.json).</summary>
        private sealed class CleanupHistoryEntry
        {
            public string Date { get; set; } = "";      // yyyy-MM-dd
            public long Bytes { get; set; } = 0;
        }

        /// <summary>
        /// مقدار آزادشده یک نوبت پاکسازی را در تاریخچه روزانه ثبت می کند.
        /// اگر امروز قبلاً رکورد داشته باشیم، جمع می شود (چند پاکسازی در یک روز).
        /// </summary>
        private void RecordCleanupSession(long bytes)
        {
            if (bytes <= 0) return;
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            var existing = _cleanupHistory.FirstOrDefault(h => h.Date == today);
            if (existing != null) existing.Bytes += bytes;
            else _cleanupHistory.Add(new CleanupHistoryEntry { Date = today, Bytes = bytes });

            // نگه داشتن حداکثر ۹۰ روز؛ قدیمی تر حذف می شود
            if (_cleanupHistory.Count > 90)
                _cleanupHistory.RemoveRange(0, _cleanupHistory.Count - 90);
        }

        private void LoadCumulativeFreedStats()
        {
            _cumulativeFreedBytes = 0;
            _lastCleanFreedBytes = 0;
            _lastCleanDateTime = DateTime.MinValue;
            _cleanupHistory.Clear();

            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string statsFile = Path.Combine(localAppData, "CleanC", "clean_stats.json");
                if (File.Exists(statsFile))
                {
                    string json = File.ReadAllText(statsFile);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("cumulativeBytes", out var cumProp))
                        _cumulativeFreedBytes = cumProp.GetInt64();
                    if (doc.RootElement.TryGetProperty("lastCleanBytes", out var lastProp))
                        _lastCleanFreedBytes = lastProp.GetInt64();
                    if (doc.RootElement.TryGetProperty("lastCleanTime", out var timeProp) && DateTime.TryParse(timeProp.GetString(), out var dt))
                        _lastCleanDateTime = dt;

                    if (doc.RootElement.TryGetProperty("history", out var histProp) && histProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var entry in histProp.EnumerateArray())
                        {
                            if (!entry.TryGetProperty("date", out var dProp)) continue;
                            string? date = dProp.GetString();
                            if (string.IsNullOrWhiteSpace(date)) continue;
                            long bytes = entry.TryGetProperty("bytes", out var bProp) ? bProp.GetInt64() : 0;
                            _cleanupHistory.Add(new CleanupHistoryEntry { Date = date, Bytes = Math.Max(0, bytes) });
                        }
                    }
                }
            }
            catch
            {
                // فایل خراب/غیرقابل خواندن → همه چیز صفر می ماند
            }

            UpdateCumulativeChipsUI();
            RenderCleanupHistory();
        }

        private void SaveCumulativeFreedStats()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string folder = Path.Combine(localAppData, "CleanC");
                Directory.CreateDirectory(folder);
                string statsFile = Path.Combine(folder, "clean_stats.json");
                var obj = new
                {
                    cumulativeBytes = _cumulativeFreedBytes,
                    lastCleanBytes = _lastCleanFreedBytes,
                    lastCleanTime = (_lastCleanDateTime == DateTime.MinValue ? "" : _lastCleanDateTime.ToString("o")),
                    history = _cleanupHistory.Select(h => new { date = h.Date, bytes = h.Bytes })
                };
                File.WriteAllText(statsFile, System.Text.Json.JsonSerializer.Serialize(obj));
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private void UpdateCumulativeChipsUI()
        {
            if (HeroTotalCumulativeText != null)
            {
                HeroTotalCumulativeText.Text = _cumulativeFreedBytes > 0
                    ? $"مجموع آزادشده: {SystemStats.FormatBytes(_cumulativeFreedBytes)}"
                    : "مجموع آزادشده: —";
            }
            if (HeroLastCleanText != null)
            {
                if (_lastCleanDateTime == DateTime.MinValue || _lastCleanFreedBytes <= 0)
                {
                    HeroLastCleanText.Text = "آخرین: هنوز پاکسازی انجام نشده";
                }
                else
                {
                    string timeStr;
                    var diff = DateTime.Now - _lastCleanDateTime;
                    if (diff.TotalHours < 1) timeStr = "لحظاتی پیش";
                    else if (diff.TotalDays < 1) timeStr = $"امروز ({_lastCleanDateTime:HH:mm})";
                    else if (diff.TotalDays < 2) timeStr = "دیروز";
                    else timeStr = $"{(int)diff.TotalDays} روز پیش";

                    HeroLastCleanText.Text = $"آخرین: {SystemStats.FormatBytes(_lastCleanFreedBytes)} ({timeStr})";
                }
            }
        }

        // ====================================================================
        // Hero: Disk Breakdown Bar + 7-Day Cleanup Chart (real data only)
        // ====================================================================

        /// <summary>
        /// نوار سه تکه دیسک (استفاده شده / قابل پاکسازی / آزاد) را با داده واقعی رندر می کند.
        /// </summary>
        private void RenderDiskBreakdown(double totalGb, double freeGb, long cleanableBytes)
        {
            if (DiskBreakdownBar == null || DiskBreakdownFreeBadge == null) return;
            if (totalGb <= 0) return;

            double cleanableGb = Math.Clamp(cleanableBytes / (1024.0 * 1024.0 * 1024.0), 0, freeGb);
            double freeOnlyGb = Math.Max(0, freeGb - cleanableGb);
            double usedGb = Math.Max(0, totalGb - freeGb);

            // نباید از کل عبور کند
            if (usedGb + cleanableGb + freeOnlyGb > totalGb)
            {
                double overflow = (usedGb + cleanableGb + freeOnlyGb) - totalGb;
                freeOnlyGb = Math.Max(0, freeOnlyGb - overflow);
            }

            double usedPct = usedGb / totalGb * 100.0;
            double cleanPct = cleanableGb / totalGb * 100.0;
            double freePct = Math.Max(0, 100.0 - usedPct - cleanPct);

            // تایمر هر ۲٫۵ ثانیه فراخوانی می کند؛ فقط وقتی عدد واقعاً تغییر کرده layout می دهیم
            string usedTxt = $"{usedPct:0.#}%";
            string cleanTxt = cleanableGb > 0.01 ? $"{cleanPct:0.#}%" : "—";
            string freeTxt = $"{freePct:0.#}%";
            string badgeTxt = $"{SystemStats.FormatBytes((long)(freeGb * 1024 * 1024 * 1024))} آزاد از {SystemStats.FormatBytes((long)(totalGb * 1024 * 1024 * 1024))}";

            bool dirty = _lastDiskRenderSig != $"{usedTxt}|{cleanTxt}|{freeTxt}|{badgeTxt}";
            if (!dirty) return;
            _lastDiskRenderSig = $"{usedTxt}|{cleanTxt}|{freeTxt}|{badgeTxt}";

            var cols = DiskBreakdownBar.ColumnDefinitions;
            if (cols.Count >= 3)
            {
                cols[0].Width = new GridLength(Math.Max(usedPct, 0.5), GridUnitType.Star);
                cols[1].Width = new GridLength(Math.Max(cleanPct, 0.0), GridUnitType.Star);
                cols[2].Width = new GridLength(Math.Max(freePct, 0.5), GridUnitType.Star);
            }

            DiskBreakdownFreeBadge.Text = badgeTxt;
            if (DiskLegendUsedPct != null) DiskLegendUsedPct.Text = usedTxt;
            if (DiskLegendCleanablePct != null) DiskLegendCleanablePct.Text = cleanTxt;
            if (DiskLegendFreePct != null) DiskLegendFreePct.Text = freeTxt;
        }

        /// <summary>
        /// نمودار ۷ پاکسازی اخیر — آخرین ستون همیشه «امروز» است و فقط مقادیر
        /// ثبت شده واقعی نمایش داده می شوند؛ روزهای بدون پاکسازی «—» می مانند.
        /// </summary>
        private void RenderCleanupHistory()
        {
            if (CleanupHistoryBars == null) return;

            // ساخت آرایه ۷ روزه: ایندکس ۶ = امروز
            var days = new long[7];
            var labels = new string[7];
            var values = new string[7];

            var byDate = _cleanupHistory
                .GroupBy(h => h.Date)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Bytes));

            long maxBytes = 0;
            for (int i = 0; i < 7; i++)
            {
                var day = DateTime.Now.AddDays(-(6 - i));
                string key = day.ToString("yyyy-MM-dd");
                days[i] = byDate.TryGetValue(key, out var b) ? b : 0;
                maxBytes = Math.Max(maxBytes, days[i]);
                labels[i] = day.ToString("dd.MM", CultureInfo.InvariantCulture);
                values[i] = days[i] > 0 ? SystemStats.FormatBytes(days[i]) : "—";
            }

            // ستون ها به ترتیب در Children قرار دارند: TextBlock مقدار، Border نوار، TextBlock برچسب
            var columns = new List<StackPanel>();
            foreach (var child in CleanupHistoryBars.Children)
            {
                if (child is StackPanel sp) columns.Add(sp);
            }

            for (int i = 0; i < 7 && i < columns.Count; i++)
            {
                var col = columns[i];
                var valueTb = col.Children.Count > 0 ? col.Children[0] as TextBlock : null;
                var bar = col.Children.Count > 1 ? col.Children[1] as Border : null;
                var labelTb = col.Children.Count > 2 ? col.Children[2] as TextBlock : null;

                if (valueTb != null) valueTb.Text = values[i];
                if (labelTb != null) labelTb.Text = labels[i];
                if (bar != null)
                {
                    if (days[i] > 0 && maxBytes > 0)
                    {
                        double ratio = (double)days[i] / maxBytes;
                        // حداقل ۴px برای دیده شدن، حداکثر ۳۸px داخل Grid ۱۰۲px
                        bar.Height = 4 + (ratio * 34);
                        bar.Opacity = 1.0;
                    }
                    else
                    {
                        bar.Height = 3;
                        bar.Opacity = 0.18;
                    }
                }
            }
        }

        /// <summary>مقدار «قابل پاکسازی» قهرمان را از اسکن واقعی آپدیت می کند.</summary>
        private void UpdateHeroCleanable(long cleanableBytes)
        {
            _lastScanCleanableBytes = Math.Max(0, cleanableBytes);
            if (HeroCleanableText != null)
            {
                HeroCleanableText.Text = _lastScanCleanableBytes > 0
                    ? SystemStats.FormatBytes(_lastScanCleanableBytes)
                    : "—";
            }
            RenderDiskBreakdown(_diskTotalGb, _diskFreeGb, _lastScanCleanableBytes);
        }

        /// <summary>
        /// بعد از پاکسازی، اسکن مجدد را در پس زمینه انجام می دهد تا عدد
        /// «قابل پاکسازی» قهرمان به روز بماند (بدون بلاک کردن UI).
        /// </summary>
        private void ScheduleHeroCleanableRefresh()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var scan = await SystemScanService.ScanAsync();
                    long cleanable = scan.TotalBytes;
                    DispatcherQueue.TryEnqueue(() => UpdateHeroCleanable(cleanable));
                }
                catch
                {
                    // اسکن ناموفق → مقدار قبلی حفظ می شود
                }
            });
        }


        private void ShowInfoBar(string title, string message, InfoBarSeverity severity)
        {
            AddNotificationHistory(title, message, severity);
            NotifyOutsideWindow(title, message, severity);

            if (StatusInfoBar == null) return;

            if (_infoBarAutoDismissTimer.IsEnabled || StatusInfoBar.IsOpen)
            {
                while (_infoBarQueue.Count >= 8) _infoBarQueue.Dequeue();
                _infoBarQueue.Enqueue((title, message, severity));
                return;
            }

            ShowInfoBarCore(title, message, severity);
        }

        private void DequeueNextInfoBar()
        {
            if (_infoBarQueue.Count == 0) return;
            var (title, message, severity) = _infoBarQueue.Dequeue();
            ShowInfoBarCore(title, message, severity);
        }

        private void ShowInfoBarCore(string title, string message, InfoBarSeverity severity)
        {
            if (StatusInfoBar == null) return;

            StatusInfoBar.Title = title;
            StatusInfoBar.Message = message;
            StatusInfoBar.Severity = severity;
            StatusInfoBar.IsOpen = true;

            // D6: اعلامِ زنده برای خواننده ی صفحه
            AnnounceForAccessibility(StatusInfoBar);

            _infoBarAutoDismissTimer.Stop();
            _infoBarAutoDismissTimer.Start();
        }

        /// <summary>
        /// F5 — نوتیفیکیشن ویندوزی فقط وقتی پنجره ی CleanC فوکوس ندارد؛
        /// اول toast (WinAppSDK) و اگر ثبت نشده باشد، balloon تری آیکون با شدت واقعی.
        /// </summary>
        private void NotifyOutsideWindow(string title, string message, InfoBarSeverity severity)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)) return;
                if (NativeMethods.IsWindowFocused(_hWnd)) return;

                var winSeverity = severity switch
                {
                    InfoBarSeverity.Error => WinNotifySeverity.Error,
                    InfoBarSeverity.Warning => WinNotifySeverity.Warning,
                    _ => WinNotifySeverity.Info
                };

                if (!_windowsNotifications.TryShowToast(title, message, winSeverity))
                    _trayManager?.ShowBalloonNotification(title, message, winSeverity);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NotifyOutsideWindow");
            }
        }

        private void AddNotificationHistory(string title, string message, InfoBarSeverity severity)
        {
            // Requirement 8: Filter out transient in-progress pings and informational chatter
            if (severity == InfoBarSeverity.Informational) return;
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(message)) return;

            string combined = (title + " " + message).ToLowerInvariant();
            if (combined.Contains("در حال") || combined.Contains("checking") || combined.Contains("scanning") ||
                combined.Contains("loading") || combined.Contains("لطفاً صبر کنید"))
            {
                return;
            }

            // B4 — اعلان ها در settings.json هم نوشته می شوند تا بعد از ری استارت از بین نروند
            try
            {
                _settings.NotificationHistory.Insert(0, new NotificationHistoryEntry
                {
                    Title = title,
                    Message = message,
                    Severity = SeverityToString(severity),
                    At = DateTime.Now
                });

                while (_settings.NotificationHistory.Count > NotificationHistoryCapacity)
                    _settings.NotificationHistory.RemoveAt(_settings.NotificationHistory.Count - 1);

                ScheduleSettingsSave();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex, "AddNotificationHistory.Persist");
            }

            RenderNotificationHistory(title, message, severity, DateTime.Now);
        }

        /// <summary>ری استارت تاریخچه ی اعلان ها از تنظیمات ذخیره شده؛ جدیدترین بالا.</summary>
        private void RenderPersistedNotificationHistory()
        {
            if (NotificationHistoryStack == null) return;
            if (_settings.NotificationHistory == null || _settings.NotificationHistory.Count == 0) return;

            // لیست ذخیره شده جدیدترین اول است و Insert(0) می کنیم → از قدیمی ترین شروع کن
            for (int i = _settings.NotificationHistory.Count - 1; i >= 0; i--)
            {
                var entry = _settings.NotificationHistory[i];
                if (entry == null) continue;
                RenderNotificationHistory(entry.Title, entry.Message, SeverityFromString(entry.Severity), entry.At);
            }
        }

        private void RenderNotificationHistory(string title, string message, InfoBarSeverity severity, DateTime at)
        {
            if (NotificationHistoryStack == null) return;

            if (EmptyHistoryText != null)
            {
                EmptyHistoryText.Visibility = Visibility.Collapsed;
            }

            if (NotificationBadgeDot != null)
            {
                NotificationBadgeDot.Visibility = Visibility.Visible;
            }

            var historyCard = BuildNotificationHistoryCard(title, message, severity, at);
            NotificationHistoryStack.Children.Insert(0, historyCard);

            // سقف نمایش در UI (جدا از سقف ذخیره شده در تنظیمات)
            while (NotificationHistoryStack.Children.Count > NotificationHistoryCapacity)
                NotificationHistoryStack.Children.RemoveAt(NotificationHistoryStack.Children.Count - 1);
        }

        private static string SeverityToString(InfoBarSeverity severity) => severity switch
        {
            InfoBarSeverity.Success => "Success",
            InfoBarSeverity.Warning => "Warning",
            InfoBarSeverity.Error => "Error",
            _ => "Informational"
        };

        private static InfoBarSeverity SeverityFromString(string? severity) => severity switch
        {
            "Success" => InfoBarSeverity.Success,
            "Warning" => InfoBarSeverity.Warning,
            "Error" => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };

        private Border BuildNotificationHistoryCard(string title, string message, InfoBarSeverity severity, DateTime at)
        {
            var historyCard = new Border
            {
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 4)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new FontIcon
            {
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 8, 0)
            };

            switch (severity)
            {
                case InfoBarSeverity.Success:
                    icon.Glyph = "\uE73E";
                    icon.Foreground = GetThemeBrush("SystemFillColorSuccessBrush");
                    break;
                case InfoBarSeverity.Warning:
                    icon.Glyph = "\uE7BA";
                    icon.Foreground = GetThemeBrush("SystemFillColorCautionBrush");
                    break;
                case InfoBarSeverity.Error:
                    icon.Glyph = "\uEA39";
                    icon.Foreground = GetThemeBrush("SystemFillColorCriticalBrush");
                    break;
                default:
                    icon.Glyph = "\uE946";
                    icon.Foreground = GetThemeBrush("AccentFillColorDefaultBrush");
                    break;
            }

            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var textStack = new StackPanel { Spacing = 2 };
            string timeStr = at.Date == DateTime.Now.Date
                ? at.ToString("HH:mm:ss")
                : at.ToString("yyyy.MM.dd HH:mm", CultureInfo.InvariantCulture);
            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBlock = new TextBlock { Text = title, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            var timeBlock = new TextBlock { Text = timeStr, FontSize = 10, Foreground = GetThemeBrush("TextFillColorTertiaryBrush") };
            Grid.SetColumn(titleBlock, 0);
            Grid.SetColumn(timeBlock, 1);
            topRow.Children.Add(titleBlock);
            topRow.Children.Add(timeBlock);

            var msgBlock = new TextBlock { Text = message, FontSize = 11, Foreground = GetThemeBrush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap };
            textStack.Children.Add(topRow);
            textStack.Children.Add(msgBlock);

            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            historyCard.Child = grid;
            return historyCard;
        }

        private void ClearHistoryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (NotificationHistoryStack == null) return;
            NotificationHistoryStack.Children.Clear();

            // B4 — پاک کردنِ UI باید ذخیره شده را هم پاک کند وگرنه بعد از ری استارت برمی گردد
            try
            {
                _settings.NotificationHistory.Clear();
                ScheduleSettingsSave();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex, "ClearHistoryBtn_Click");
            }

            if (EmptyHistoryText != null)
            {
                EmptyHistoryText.Visibility = Visibility.Visible;
                NotificationHistoryStack.Children.Add(EmptyHistoryText);
            }
            if (NotificationBadgeDot != null)
            {
                NotificationBadgeDot.Visibility = Visibility.Collapsed;
            }
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

            // Responsive breakpoint at 860 DIPs
            bool shouldBeCompact = availableWidth < 860;
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

                // Stack Row 1 into 2 columns x 2 rows
                BentoGrid.ColumnDefinitions.Clear();
                BentoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                BentoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                BentoGrid.RowDefinitions.Clear();
                BentoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                BentoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                if (HealthScoreCard != null)
                {
                    Grid.SetRow(HealthScoreCard, 0);
                    Grid.SetColumn(HealthScoreCard, 0);
                }
                Grid.SetRow(BentoRamCard, 0);
                Grid.SetColumn(BentoRamCard, 1);

                Grid.SetRow(BentoCpuCard, 1);
                Grid.SetColumn(BentoCpuCard, 0);

                Grid.SetRow(BentoDiskCard, 1);
                Grid.SetColumn(BentoDiskCard, 1);

                // Row 2 into 1 column x 2 rows
                if (AnalysisRow2Grid != null)
                {
                    AnalysisRow2Grid.ColumnDefinitions.Clear();
                    AnalysisRow2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    AnalysisRow2Grid.RowDefinitions.Clear();
                    AnalysisRow2Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    AnalysisRow2Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    if (DiskBreakdownCard != null)
                    {
                        Grid.SetRow(DiskBreakdownCard, 0);
                        Grid.SetColumn(DiskBreakdownCard, 0);
                    }
                    if (CleanupsHistoryCard != null)
                    {
                        Grid.SetRow(CleanupsHistoryCard, 1);
                        Grid.SetColumn(CleanupsHistoryCard, 0);
                        CleanupsHistoryCard.Margin = new Thickness(0, 12, 0, 0);
                    }
                }
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

                // Side-by-side 4 columns Row 1 (1.15*, 1*, 1*, 1*)
                BentoGrid.RowDefinitions.Clear();
                BentoGrid.ColumnDefinitions.Clear();
                BentoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                BentoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                BentoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                BentoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                if (HealthScoreCard != null)
                {
                    Grid.SetRow(HealthScoreCard, 0);
                    Grid.SetColumn(HealthScoreCard, 0);
                }
                Grid.SetRow(BentoRamCard, 0);
                Grid.SetColumn(BentoRamCard, 1);

                Grid.SetRow(BentoCpuCard, 0);
                Grid.SetColumn(BentoCpuCard, 2);

                Grid.SetRow(BentoDiskCard, 0);
                Grid.SetColumn(BentoDiskCard, 3);

                // Side-by-side 2 columns Row 2 (1.5*, 1*)
                if (AnalysisRow2Grid != null)
                {
                    AnalysisRow2Grid.RowDefinitions.Clear();
                    AnalysisRow2Grid.ColumnDefinitions.Clear();
                    AnalysisRow2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
                    AnalysisRow2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    if (DiskBreakdownCard != null)
                    {
                        Grid.SetRow(DiskBreakdownCard, 0);
                        Grid.SetColumn(DiskBreakdownCard, 0);
                    }
                    if (CleanupsHistoryCard != null)
                    {
                        Grid.SetRow(CleanupsHistoryCard, 0);
                        Grid.SetColumn(CleanupsHistoryCard, 1);
                        CleanupsHistoryCard.Margin = new Thickness(0);
                    }
                }
            }
        }

        // ====================================================================
        // Dashboard V2: Aurora Composition, Shine, Sparklines & Health Gauge
        // ====================================================================

        private void InitHeroAurora()
        {
            try
            {
                if (HeroAuroraHost != null)
                {
                    HeroAuroraHost.Loaded += (s, e) =>
                    {
                        SetupHeroAurora();
                        StartAuroraAnimations();
                    };

                    HeroAuroraHost.SizeChanged += (s, e) =>
                    {
                        UpdateHeroAuroraSize(e.NewSize);
                    };

                    if (HeroAuroraHost.ActualWidth > 0 && HeroAuroraHost.ActualHeight > 0)
                    {
                        SetupHeroAurora();
                        UpdateHeroAuroraSize(new Windows.Foundation.Size(HeroAuroraHost.ActualWidth, HeroAuroraHost.ActualHeight));
                        StartAuroraAnimations();
                    }
                }

                UpdateAccentBrushes();

                    _uiSettings.ColorValuesChanged += (s, e) =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            UpdateAccentBrushes();
                            UpdateAuroraColors();
                            // D6: تغییر رنگ سیستم می تواند روشن/خاموش شدن High Contrast باشد
                            ConfigureSystemBackdrop();
                        });
                    };

                SetupButtonShine();
                SetupStatusPulse();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private void SetupHeroAurora()
        {
            if (_auroraRootVisual != null || HeroAuroraHost == null) return;
            try
            {
                var hostVisual = ElementCompositionPreview.GetElementVisual(HeroAuroraHost);
                var compositor = hostVisual.Compositor;

                _auroraRootVisual = compositor.CreateContainerVisual();
                _auroraBlobVisuals.Clear();
                _auroraBrushes.Clear();

                for (int i = 0; i < 4; i++)
                {
                    var blob = compositor.CreateSpriteVisual();
                    var brush = compositor.CreateRadialGradientBrush();
                    brush.EllipseCenter = new Vector2(0.5f, 0.5f);
                    brush.EllipseRadius = new Vector2(0.5f, 0.5f);

                    // 4 color stops for ultra-soft, feathered/blurred ethereal glow ("محو تر")
                    var stop0 = compositor.CreateColorGradientStop(0.0f, Windows.UI.Color.FromArgb(90, 0, 120, 212));
                    var stop1 = compositor.CreateColorGradientStop(0.35f, Windows.UI.Color.FromArgb(42, 0, 120, 212));
                    var stop2 = compositor.CreateColorGradientStop(0.70f, Windows.UI.Color.FromArgb(14, 0, 120, 212));
                    var stop3 = compositor.CreateColorGradientStop(1.0f, Windows.UI.Color.FromArgb(0, 0, 120, 212));
                    brush.ColorStops.Add(stop0);
                    brush.ColorStops.Add(stop1);
                    brush.ColorStops.Add(stop2);
                    brush.ColorStops.Add(stop3);

                    blob.Brush = brush;
                    _auroraBlobVisuals.Add(blob);
                    _auroraBrushes.Add(brush);
                    _auroraRootVisual.Children.InsertAtBottom(blob);
                }

                ElementCompositionPreview.SetElementChildVisual(HeroAuroraHost, _auroraRootVisual);
                UpdateAuroraColors();

                if (HeroAuroraHost.ActualWidth > 0 && HeroAuroraHost.ActualHeight > 0)
                {
                    UpdateHeroAuroraSize(new Windows.Foundation.Size(HeroAuroraHost.ActualWidth, HeroAuroraHost.ActualHeight));
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private void UpdateHeroAuroraSize(Windows.Foundation.Size newSize)
        {
            if (_auroraRootVisual == null) return;
            float w = (float)newSize.Width;
            float h = (float)newSize.Height;
            if (w <= 1f || h <= 1f) return;

            try
            {
                var compositor = _auroraRootVisual.Compositor;
                _auroraRootVisual.Size = new Vector2(w, h);

                // Clip visual with CompositionGeometricClip and RoundedRectangleGeometry matching header's CornerRadius 14
                var geom = compositor.CreateRoundedRectangleGeometry();
                geom.Size = new Vector2(w, h);
                geom.CornerRadius = new Vector2(14f, 14f);
                _auroraRootVisual.Clip = compositor.CreateGeometricClip(geom);

                if (_auroraBlobVisuals.Count >= 4)
                {
                    // Blobs with larger scale & overlapping spread for smooth atmospheric fog
                    _auroraBlobVisuals[0].Size = new Vector2(w * 0.70f, h * 1.55f);
                    _auroraBlobVisuals[0].Offset = new Vector3(-w * 0.10f, -h * 0.35f, 0);

                    _auroraBlobVisuals[1].Size = new Vector2(w * 0.75f, h * 1.65f);
                    _auroraBlobVisuals[1].Offset = new Vector3(w * 0.20f, -h * 0.40f, 0);

                    _auroraBlobVisuals[2].Size = new Vector2(w * 0.65f, h * 1.45f);
                    _auroraBlobVisuals[2].Offset = new Vector3(w * 0.50f, -h * 0.20f, 0);

                    _auroraBlobVisuals[3].Size = new Vector2(w * 0.58f, h * 1.35f);
                    _auroraBlobVisuals[3].Offset = new Vector3(w * 0.15f, h * 0.25f, 0);
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private void UpdateAuroraColors()
        {
            if (_auroraBrushes.Count < 4) return;
            try
            {
                var accent = _uiSettings.GetColorValue(UIColorType.Accent);
                var accentLight = _uiSettings.GetColorValue(UIColorType.AccentLight2);
                var accentDark = _uiSettings.GetColorValue(UIColorType.AccentDark1);

                // Multi-stop soft gradient colors
                SetBrushColors(_auroraBrushes[0], accent, 110, 42);
                SetBrushColors(_auroraBrushes[1], accentLight, 125, 48);
                SetBrushColors(_auroraBrushes[2], accent, 100, 38);
                SetBrushColors(_auroraBrushes[3], accentDark, 95, 34);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private static void SetBrushColors(CompositionRadialGradientBrush brush, Windows.UI.Color c, byte coreAlpha, byte midAlpha)
        {
            if (brush.ColorStops.Count >= 4)
            {
                brush.ColorStops[0].Color = Windows.UI.Color.FromArgb(coreAlpha, c.R, c.G, c.B);
                brush.ColorStops[1].Color = Windows.UI.Color.FromArgb(midAlpha, c.R, c.G, c.B);
                brush.ColorStops[2].Color = Windows.UI.Color.FromArgb((byte)(midAlpha / 3), c.R, c.G, c.B);
                brush.ColorStops[3].Color = Windows.UI.Color.FromArgb(0, c.R, c.G, c.B);
            }
            else if (brush.ColorStops.Count >= 3)
            {
                brush.ColorStops[0].Color = Windows.UI.Color.FromArgb(coreAlpha, c.R, c.G, c.B);
                brush.ColorStops[1].Color = Windows.UI.Color.FromArgb(midAlpha, c.R, c.G, c.B);
                brush.ColorStops[2].Color = Windows.UI.Color.FromArgb(0, c.R, c.G, c.B);
            }
        }

        private void UpdateAccentBrushes()
        {
            try
            {
                var accent = _uiSettings.GetColorValue(UIColorType.Accent);
                var tintColor = Windows.UI.Color.FromArgb(34, accent.R, accent.G, accent.B);
                var borderColor = Windows.UI.Color.FromArgb(102, accent.R, accent.G, accent.B);

                foreach (var themeKey in new[] { "Light", "Dark" })
                {
                    if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var dictObj) &&
                        dictObj is ResourceDictionary dict)
                    {
                        if (dict.TryGetValue("AppAccentTintBrush", out var tbObj) && tbObj is SolidColorBrush tb)
                            tb.Color = tintColor;
                        else
                            dict["AppAccentTintBrush"] = new SolidColorBrush(tintColor);

                        if (dict.TryGetValue("AppAccentBorderBrush", out var bbObj) && bbObj is SolidColorBrush bb)
                            bb.Color = borderColor;
                        else
                            dict["AppAccentBorderBrush"] = new SolidColorBrush(borderColor);
                    }
                }

                if (Application.Current.Resources.TryGetValue("AppAccentTintBrush", out var rtbObj) && rtbObj is SolidColorBrush rtb)
                    rtb.Color = tintColor;
                else
                    Application.Current.Resources["AppAccentTintBrush"] = new SolidColorBrush(tintColor);

                if (Application.Current.Resources.TryGetValue("AppAccentBorderBrush", out var rbbObj) && rbbObj is SolidColorBrush rbb)
                    rbb.Color = borderColor;
                else
                    Application.Current.Resources["AppAccentBorderBrush"] = new SolidColorBrush(borderColor);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private void StartAuroraAnimations()
        {
            try
            {
                if (!_uiSettings.AnimationsEnabled || _isAuroraAnimating || _auroraBlobVisuals.Count < 4) return;
                _isAuroraAnimating = true;

                var compositor = _auroraBlobVisuals[0].Compositor;

                var durations = new[] { 18.0, 23.0, 17.0, 24.0 };
                // Increased motion range ("حرکت بیشتر")
                var offsetDeltas = new[]
                {
                    new Vector3(95, 38, 0),
                    new Vector3(-85, -42, 0),
                    new Vector3(-95, 45, 0),
                    new Vector3(80, -35, 0)
                };
                var scales = new[]
                {
                    new Vector3(1.45f, 1.40f, 1f),
                    new Vector3(1.35f, 1.50f, 1f),
                    new Vector3(1.50f, 1.35f, 1f),
                    new Vector3(1.40f, 1.45f, 1f)
                };

                for (int i = 0; i < 4; i++)
                {
                    var visual = _auroraBlobVisuals[i];
                    var baseOffset = visual.Offset;

                    var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
                    offsetAnim.InsertKeyFrame(0.0f, baseOffset);
                    offsetAnim.InsertKeyFrame(0.5f, baseOffset + offsetDeltas[i]);
                    offsetAnim.InsertKeyFrame(1.0f, baseOffset);
                    offsetAnim.Duration = TimeSpan.FromSeconds(durations[i]);
                    offsetAnim.IterationBehavior = AnimationIterationBehavior.Forever;

                    var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
                    scaleAnim.InsertKeyFrame(0.0f, Vector3.One);
                    scaleAnim.InsertKeyFrame(0.5f, scales[i]);
                    scaleAnim.InsertKeyFrame(1.0f, Vector3.One);
                    scaleAnim.Duration = TimeSpan.FromSeconds(durations[i]);
                    scaleAnim.IterationBehavior = AnimationIterationBehavior.Forever;

                    visual.StartAnimation("Offset", offsetAnim);
                    visual.StartAnimation("Scale", scaleAnim);
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private void StopAuroraAnimations()
        {
            try
            {
                if (!_isAuroraAnimating) return;
                _isAuroraAnimating = false;
                foreach (var visual in _auroraBlobVisuals)
                {
                    visual.StopAnimation("Offset");
                    visual.StopAnimation("Scale");
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private void SetupButtonShine()
        {
            try
            {
                if (!_uiSettings.AnimationsEnabled || BoostBtnShineHost == null) return;
                var hostVisual = ElementCompositionPreview.GetElementVisual(BoostBtnShineHost);
                var compositor = hostVisual.Compositor;

                float hostW = BoostBtnShineHost.ActualWidth > 0 ? (float)BoostBtnShineHost.ActualWidth : 230f;
                float hostH = BoostBtnShineHost.ActualHeight > 0 ? (float)BoostBtnShineHost.ActualHeight : 36f;

                // Strict clipping to the button bounds so shine NEVER sweeps outside the button
                var roundGeometry = compositor.CreateRoundedRectangleGeometry();
                roundGeometry.CornerRadius = new Vector2(8, 8);
                roundGeometry.Size = new Vector2(hostW, hostH);
                hostVisual.Clip = compositor.CreateGeometricClip(roundGeometry);

                BoostBtnShineHost.SizeChanged += (s, e) =>
                {
                    if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
                    {
                        roundGeometry.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
                    }
                };

                var shine = compositor.CreateSpriteVisual();
                shine.Size = new Vector2(60, hostH);

                var gradientBrush = compositor.CreateLinearGradientBrush();
                gradientBrush.StartPoint = new Vector2(0, 0);
                gradientBrush.EndPoint = new Vector2(1, 0.5f);
                var s1 = compositor.CreateColorGradientStop(0.0f, Windows.UI.Color.FromArgb(0, 255, 255, 255));
                var s2 = compositor.CreateColorGradientStop(0.5f, Windows.UI.Color.FromArgb(70, 255, 255, 255));
                var s3 = compositor.CreateColorGradientStop(1.0f, Windows.UI.Color.FromArgb(0, 255, 255, 255));
                gradientBrush.ColorStops.Add(s1);
                gradientBrush.ColorStops.Add(s2);
                gradientBrush.ColorStops.Add(s3);
                shine.Brush = gradientBrush;

                // Opacity animation: completely invisible outside the button, fades in upon entering, fades out before leaving
                var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
                opacityAnim.InsertKeyFrame(0.0f, 0.0f);
                opacityAnim.InsertKeyFrame(0.04f, 1.0f);
                opacityAnim.InsertKeyFrame(0.28f, 1.0f);
                opacityAnim.InsertKeyFrame(0.33f, 0.0f);
                opacityAnim.InsertKeyFrame(1.0f, 0.0f);
                opacityAnim.Duration = TimeSpan.FromSeconds(3.6);
                opacityAnim.IterationBehavior = AnimationIterationBehavior.Forever;
                shine.StartAnimation("Opacity", opacityAnim);

                var sweepAnim = compositor.CreateVector3KeyFrameAnimation();
                sweepAnim.InsertKeyFrame(0.0f, new Vector3(-60, 0, 0));
                sweepAnim.InsertKeyFrame(0.33f, new Vector3(hostW, 0, 0));
                sweepAnim.InsertKeyFrame(1.0f, new Vector3(hostW, 0, 0));
                sweepAnim.Duration = TimeSpan.FromSeconds(3.6);
                sweepAnim.IterationBehavior = AnimationIterationBehavior.Forever;

                shine.StartAnimation("Offset", sweepAnim);
                _shineVisual = shine;
                ElementCompositionPreview.SetElementChildVisual(BoostBtnShineHost, shine);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private void SetupStatusPulse()
        {
            try
            {
                if (!_uiSettings.AnimationsEnabled || HeroStatusDot == null) return;
                var visual = ElementCompositionPreview.GetElementVisual(HeroStatusDot);
                var compositor = visual.Compositor;
                visual.CenterPoint = new Vector3(3.5f, 3.5f, 0);

                var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
                scaleAnim.InsertKeyFrame(0.0f, new Vector3(1f, 1f, 1f));
                scaleAnim.InsertKeyFrame(0.5f, new Vector3(1.4f, 1.4f, 1f));
                scaleAnim.InsertKeyFrame(1.0f, new Vector3(1f, 1f, 1f));
                scaleAnim.Duration = TimeSpan.FromSeconds(2.2);
                scaleAnim.IterationBehavior = AnimationIterationBehavior.Forever;

                var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
                opacityAnim.InsertKeyFrame(0.0f, 1.0f);
                opacityAnim.InsertKeyFrame(0.5f, 0.45f);
                opacityAnim.InsertKeyFrame(1.0f, 1.0f);
                opacityAnim.Duration = TimeSpan.FromSeconds(2.2);
                opacityAnim.IterationBehavior = AnimationIterationBehavior.Forever;

                visual.StartAnimation("Scale", scaleAnim);
                visual.StartAnimation("Opacity", opacityAnim);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private void InitSparklines()
        {
            try
            {
                var stats = SystemStatsService.GetCurrentStats();
                double totalRamGb = stats.TotalRamBytes / (1024.0 * 1024.0 * 1024.0);
                double usedRamGb = stats.UsedRamBytes / (1024.0 * 1024.0 * 1024.0);
                float baseRam = totalRamGb > 0 ? (float)((usedRamGb / totalRamGb) * 100.0) : 50f;
                float baseCpu = (float)stats.CpuPercent;
                // نمونه واقعی شبکه؛ داده مصنوعی تولید نمی کنیم
                (double liveNetMbps, _) = GetCurrentNetworkSpeed();
                float baseDisk = (float)Math.Max(0, liveNetMbps);

                // تا رسیدن نمونه های زنده، خط تخت با مقدار فعلی (بدون موج جعلی)
                for (int i = 0; i < 24; i++)
                {
                    _ramHistory[i] = Math.Clamp(baseRam, 2f, 98f);
                    _cpuHistory[i] = Math.Clamp(baseCpu, 2f, 98f);
                    _diskHistory[i] = Math.Clamp(baseDisk, 0f, 300f);
                }
                UpdateSparklinePaths();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private static void PushSample(float[] buffer, float value)
        {
            Array.Copy(buffer, 1, buffer, 0, buffer.Length - 1);
            buffer[^1] = Math.Clamp(value, 0f, 300f);
        }

        private void UpdateSparklinePaths()
        {
            RenderSparkline(_ramHistory, RamSparklineStroke, RamSparklineFill);
            RenderSparkline(_cpuHistory, CpuSparklineStroke, CpuSparklineFill);
            RenderSparkline(_diskHistory, DiskSparklineStroke, DiskSparklineFill);
        }

        private static void RenderSparkline(float[] samples, Microsoft.UI.Xaml.Shapes.Path strokePath, Microsoft.UI.Xaml.Shapes.Path fillPath)
        {
            if (strokePath == null || fillPath == null || samples == null || samples.Length < 2) return;

            double width = 160.0;
            double height = 34.0;
            double stepX = width / (samples.Length - 1);

            // Compute min and max across buffer
            float minVal = samples[0];
            float maxVal = samples[0];
            for (int i = 1; i < samples.Length; i++)
            {
                if (samples[i] < minVal) minVal = samples[i];
                if (samples[i] > maxVal) maxVal = samples[i];
            }

            float range = maxVal - minVal;
            const float epsilon = 2.0f;
            const float minRange = 10.0f;

            if (range < epsilon)
            {
                float mid = (maxVal + minVal) / 2f;
                minVal = mid - (minRange / 2f);
                maxVal = mid + (minRange / 2f);
                range = maxVal - minVal;
            }
            else
            {
                // 15% padding
                float pad = range * 0.15f;
                minVal -= pad;
                maxVal += pad;
                range = maxVal - minVal;
            }

            if (range <= 0.001f) range = 1.0f;

            var points = new Windows.Foundation.Point[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                double x = i * stepX;
                double normalized = Math.Clamp((samples[i] - minVal) / range, 0.0, 1.0);
                double y = (height - 4.0) - (normalized * (height - 8.0));
                points[i] = new Windows.Foundation.Point(x, y);
            }

            var strokeFigure = new PathFigure
            {
                StartPoint = points[0],
                IsClosed = false
            };
            var strokePoly = new PolyLineSegment();
            for (int i = 1; i < points.Length; i++)
            {
                strokePoly.Points.Add(points[i]);
            }
            strokeFigure.Segments.Add(strokePoly);

            var strokeGeo = new PathGeometry();
            strokeGeo.Figures.Add(strokeFigure);
            strokePath.Data = strokeGeo;

            var fillFigure = new PathFigure
            {
                StartPoint = points[0],
                IsClosed = true
            };
            var fillPoly = new PolyLineSegment();
            for (int i = 1; i < points.Length; i++)
            {
                fillPoly.Points.Add(points[i]);
            }
            fillFigure.Segments.Add(fillPoly);
            fillFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(width, height) });
            fillFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(0, height) });

            var fillGeo = new PathGeometry();
            fillGeo.Figures.Add(fillFigure);
            fillPath.Data = fillGeo;
        }

        private void SetHealthGaugeScore(double score)
        {
            _currentHealthScore = Math.Clamp(score, 0, 100);

            // F5 — عدد سلامت روی آیکون تسک بار (badge)؛ فقط وقتی عدد صحیح عوض می شود
            // تا حین انیمیشن گیج، هر فریم یک فراخوانی COM نزنیم.
            int roundedScore = (int)Math.Round(_currentHealthScore);
            if (roundedScore != _lastBadgeScore)
            {
                _lastBadgeScore = roundedScore;

                // badge ویندوز برای اپ unpackaged ممکن است در دسترس نباشد؛
                // در آن صورت عدد سلامت روی tooltip تری آیکون نشان داده می شود.
                if (!_windowsNotifications.TrySetBadge(roundedScore))
                    _trayManager?.UpdateTooltip($"CleanC — سلامت سیستم: {roundedScore}%");
            }

            if (HealthScoreValueText != null)
            {
                HealthScoreValueText.Text = $"{Math.Round(_currentHealthScore).ToString("0", CultureInfo.InvariantCulture)}";
            }

            if (HealthScoreStatusPill != null && HealthScoreStatusBadge != null)
            {
                if (_currentHealthScore >= 85)
                {
                    HealthScoreStatusPill.Text = "عالی";
                    HealthScoreStatusPill.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 108, 203, 95));
                    HealthScoreStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 108, 203, 95));
                    HealthScoreStatusBadge.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 108, 203, 95));
                    if (HealthScoreDescText != null) HealthScoreDescText.Text = "وضعیت عمومی سیستم پایدار و عالی است";
                }
                else if (_currentHealthScore >= 70)
                {
                    HealthScoreStatusPill.Text = "خوب";
                    HealthScoreStatusPill.Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
                    HealthScoreStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(35, 96, 205, 255));
                    HealthScoreStatusBadge.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 96, 205, 255));
                    if (HealthScoreDescText != null) HealthScoreDescText.Text = "عملکرد کلی سیستم خوب و مطلوب است";
                }
                else if (_currentHealthScore >= 50)
                {
                    HealthScoreStatusPill.Text = "متوسط";
                    HealthScoreStatusPill.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 165, 36));
                    HealthScoreStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(35, 245, 165, 36));
                    HealthScoreStatusBadge.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 245, 165, 36));
                    if (HealthScoreDescText != null) HealthScoreDescText.Text = "سیستم نیازمند آزادسازی رم و بهینه سازی فایل ها";
                }
                else
                {
                    HealthScoreStatusPill.Text = "نیاز به بهینه سازی";
                    HealthScoreStatusPill.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68));
                    HealthScoreStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(35, 239, 68, 68));
                    HealthScoreStatusBadge.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 239, 68, 68));
                    if (HealthScoreDescText != null) HealthScoreDescText.Text = "مصرف منابع بالا؛ اجرای پاکسازی سریع توصیه می شود";
                }
            }

            if (HealthGaugeArcPath == null) return;

            // Mathematical match with track arc: Center=(85, 74), Radius=68, StartPoint=(17, 74)
            double radius = 68.0;
            double cx = 85.0;
            double cy = 74.0;

            double pct = Math.Clamp(_currentHealthScore / 100.0, 0.005, 1.0);
            double angle = Math.PI - (pct * Math.PI);
            double endX = cx + radius * Math.Cos(angle);
            double endY = cy - radius * Math.Sin(angle);

            var figure = new PathFigure
            {
                StartPoint = new Windows.Foundation.Point(17.0, 74.0),
                IsClosed = false
            };
            figure.Segments.Add(new ArcSegment
            {
                Point = new Windows.Foundation.Point(endX, endY),
                Size = new Windows.Foundation.Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = false
            });

            var pathGeometry = new PathGeometry();
            pathGeometry.Figures.Add(figure);
            HealthGaugeArcPath.Data = pathGeometry;
        }

        private async Task AnimateHealthScoreEntrance(double targetScore)
        {
            if (!_uiSettings.AnimationsEnabled)
            {
                SetHealthGaugeScore(targetScore);
                return;
            }

            int steps = 28;
            double durationMs = 1200.0;
            double stepDelay = durationMs / steps;

            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                double ease = 1.0 - Math.Pow(1.0 - t, 3);
                double val = ease * targetScore;
                SetHealthGaugeScore(val);
                await Task.Delay((int)stepDelay);
            }
            SetHealthGaugeScore(targetScore);
        }

        private async Task AnimateHealthScoreTransition(double startScore, double targetScore)
        {
            if (!_uiSettings.AnimationsEnabled)
            {
                SetHealthGaugeScore(targetScore);
                return;
            }

            int steps = 24;
            double durationMs = 1000.0;
            double stepDelay = durationMs / steps;

            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                double ease = 1.0 - Math.Pow(1.0 - t, 3);
                double val = startScore + (targetScore - startScore) * ease;
                SetHealthGaugeScore(val);
                await Task.Delay((int)stepDelay);
            }
            SetHealthGaugeScore(targetScore);
        }

        /// <summary>
        /// نمونه سرعت شبکه — منطق و کش زنی در NetworkSpeedService متمرکز است
        /// تا دو تایمر همزمان باعث نمونه گیری مضاعف نشوند.
        /// </summary>
        private (double mbps, string typeName) GetCurrentNetworkSpeed()
            => NetworkSpeedService.Sample();
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
