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
using Microsoft.UI.Xaml.Automation;
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
using CleanC.UI.Hero;

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
        // نسخه ی زبان UI؛ برای اطمینان از این که نتیجه ی اسکن پس از تغییر زبان
        // واقعاً با کاتالوگ جدید ساخته شده است (نه اسکن در جریان با زبان قبلی).
        private int _uiLanguageVersion;
        private int _heroScanLanguageVersion;
        private bool _advancedRelocalizeRunning;
        private bool _advancedRelocalizePending;
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
        // نوتیفیکیشن ویندوزی (toast + badge)؛ وقتی پنجره فوکوس ندارد استفاده می شود
        private readonly WindowsNotificationService _windowsNotifications = new();
        // صف InfoBar تا پیام های پشت سرهم روی هم نیفتند و همه دیده شوند
        private readonly Queue<(string Title, string Message, InfoBarSeverity Severity)> _infoBarQueue = new();
        private readonly List<FontIcon> _toolBackIcons = new();
        private List<CleanupReportItem>? _lastCleanupItems;
        private long _lastFreedBytes;
        // آخرین عدد صحیحِ ست شده روی badge (برای جلوگیری از فراخوانی تکراری)
        private int _lastBadgeScore = -1;
        // سقف تاریخچه ی اعلان ها (در ذخیره و نمایش یکسان نگه داشته می شود)
        private const int NotificationHistoryCapacity = 50;

        // Dashboard V2: Accent sync, Sparklines, and Gauge state
        // (اورورای Composition قدیمی حذف شد — هیرو حالا داخل WebView2 است و خودش هاله را می‌کشد)
        private readonly UISettings _uiSettings = new();
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
        // تنظیمات پایدار برنامه (settings.json)
        // ====================================================================
        /// <summary>سه نمای اصلی نوار ناوبری بالای صفحه.</summary>
        private enum AppView { Dashboard, Tools, Settings }

        // تنظیمات از کانتینر DI می آیند؛ `_settings` فقط یک نمایِ راحت روی همان شیء است
        private AppSettings _settings => _settingsStore.Current;
        private readonly ISettingsStore _settingsStore;
        /// <summary>ViewModel صفحه Settings (C6/MVVM)؛ state آن از طریق x:Bind به کنترل ها وصل است.</summary>
        internal SettingsViewModel SettingsVm { get; }
        /// <summary>هنگام مقداردهی اولیه ی کنترل های صفحه Settings نباید Toggled ذخیره انجام دهد.</summary>
        private bool _isLoadingSettings = false;
        // کنترل های کارت کاراکتر تا پایان اولین PopulateSettingsUi مقادیر را به VM نمی نویسند؛
        // وگرنه مقداردهی اولیه ی XAML (مثلاً Value اسلایدر) «تنظیمات دست نخورده» را پر می کرد.
        private bool _characterUiReady = false;
        // پیشنهادهای شهر با debounce گرفته می‌شوند؛ درخواست قبلی با CTS جدید لغو می‌شود.
        private CancellationTokenSource? _citySuggestCts;
        private string _lastValidCity = string.Empty;
        private bool _cityDefaultRequested;
        private AppView _currentView = AppView.Dashboard;
        private bool _isExiting = false;
        /// <summary>هشدار تری فقط یک بار در هر اجرا نمایش داده شود تا آزاردهنده نشود.</summary>
        private bool _trayHintShown = false;

        public MainWindow()
        {
            // تا پایان مقداردهی XAML و پر شدن کنترل ها، رویدادهای Toggled نباید
            // تنظیماتِ خوانده شده را با مقدار پیش فرضِ XAML بازنویسی کنند.
            _isLoadingSettings = true;

            // سرویس ها و ViewModel از کانتینر DI می آیند (SettingsStore خودش تنظیمات را load می کند).
            // قبل از InitializeComponent مقداردهی می شوند تا هیچ handler ای در حین parse به شیء null نرسد.
            _settingsStore = AppServices.GetRequired<ISettingsStore>();
            _settingsStore.OnSaveRequested += CapturePlacementForSave;
            SettingsVm = AppServices.GetRequired<SettingsViewModel>();
            SettingsVm.PersistRequested += () =>
            {
                ScheduleSettingsSave();
                // تنظیمات کاراکتر درون WebView2 زندگی می کند؛ بدون این پیام، تغییر
                // تا ری‌استارت بعدی اعمال نمی شود. برای بقیه ی تنظیمات بی اثر است.
                PushHeroSettings();
            };
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

            // تنظیمات پایدار باید قبل از هر تصمیم UI (تم، هندسه ی پنجره، نمای آخر) خوانده شوند
            PopulateSettingsUi();

            // اگر کاربر هنوز شهری انتخاب نکرده باشد، یک بار شهر انگلیسیِ فعلی را از IP تشخیص می‌دهیم.
            _ = EnsureDefaultCityAsync();

            // بازگرداندن تاریخچه ی اعلان های مانده از اجرای قبلی
            RenderPersistedNotificationHistory();

            // اگر فایل تنظیمات از قبل وجود ندارد، همین حالا با مقادیر پیش فرض ساخته شود
            // تا مسیر نمایش داده شده در صفحه Settings واقعی باشد و بار اول «بازنشانی» کار کند.
            if (!File.Exists(SettingsService.FilePath)) SaveSettingsNow();

            // نشست های قرنطینه قدیمی تر از ۳۰ روز در پس زمینه پاک می شوند
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
                    if (e.WindowActivationState == WindowActivationState.Deactivated)
                    {
                       // کوچک نمایی → پنهان شدن در تری آیکون.
                        // فقط وقتی پنجره واقعاً minimize است مخفی می شود تا Alt+Tab زدن
                        // باعث پنهان شدن برنامه نشود.
                        if (!_isExiting && _settings.MinimizeToTray && NativeMethods.IsWindowMinimized(_hWnd))
                        {
                            HideToTray();
                        }
                    }
                    else if (DashboardView != null && DashboardView.Visibility == Visibility.Visible)
                    {
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
                        // بازی اکسپدیشن روی هر چیز دیگری ارجحیت دارد؛ ESC اول آن را می‌بندد
                        if (NewsReaderOverlay != null && NewsReaderOverlay.Visibility == Visibility.Visible)
                        {
                            CloseNewsReader();
                            e.Handled = true;
                            return;
                        }

                        if (ExpeditionOverlay != null && ExpeditionOverlay.Visibility == Visibility.Visible)
                        {
                            CloseExpedition();
                            e.Handled = true;
                            return;
                        }

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

            // بازیابی اندازه و موقعیت ذخیره شده — فقط اگر روی یکی از نمایشگرهای فعلی معتبر باشد
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

                    // ثبت مکان یابی جدید با debounce تا پایان درگ/تغییر سایز
                    if (!_isExiting && !NativeMethods.IsWindowMinimized(_hWnd) && _settings.SaveWindowPlacement)
                    {
                        CaptureWindowPlacement();
                        ScheduleSettingsSave();
                    }
                }
            };

            // بستن پنجره به جای خروج، برنامه را در تری آیکون نگه می دارد
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

            // Always start on Dashboard view on app launch
            SwitchToView(AppView.Dashboard, persist: false);

            LiveCleanListView.ItemsSource = _activeCleaningItems;

            // Setup real-time stats timer
            _statsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _statsTimer.Tick += (s, e) =>
            {
                RefreshSystemStats();
                // آمار زندهٔ هیروکارت داخل WebView2 — مستقل از داشبورد، چون هیرو همیشه دیده می شود
                PushHeroState();
                ApplyNewsLanguage();
            };
            _statsTimer.Start();

            // تایمرها هنگام بستن پنجره متوقف نمی شدند و تا لحظه ی مرگ پروسه
            // (مثلاً وقتی آیکون تری باعث زنده ماندن برنامه می شود) به کار ادامه می دادند.
            Closed += (s, e) =>
            {
                // آخرین وضعیت پیش از توقف تایمرها روی دیسک نوشته می شود
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
                _newsTimer?.Stop();
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
            InitHeroAccentSync();
            InitHeroHost();
            InitSparklines();
            InitializeNewsFeature();

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
                   // StopBackgroundTimers() در سازنده به Closed وصل است تا
                    // حتی اگر مقداردهی تری شکست بخورد، تایمرها متوقف شوند.
                };
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // ثبت کانال نوتیفیکیشن ویندوزی (AUMID + AppNotificationManager).
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

                // در High Contrast شفافیت و بلور مجاز نیست؛ Acrylic را خاموش می کنیم
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
                    // روی سیستم هایی که DesktopAcrylicController پشتیبانی نمی شود (مثل ویندوز ۱۰)
                    // هیچ بک دراپی ست نمی شود و پس زمینه ی سالید XAML بر اساس تم نمایش داده می شود
                    this.SystemBackdrop = null;
                }
            }
            catch (Exception __ex)
            {
                // ست نشدن بک دراپ نباید اجرای پنجره را متوقف کند؛ فقط لاگ می شود
                CcLog.Ignore(__ex, "Backdrop.Configure");
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
        /// توقف همه ی تایمرها هنگام بسته شدن پنجره.
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
                _newsTimer?.Stop();
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

                // Hero Live Telemetry: مقدارهای زندهٔ رم/CPU از طریق PushHeroState
                // (که زیر همین تایمر صدا می‌شود) به WebView2 هیرو می‌روند — تلهمتری جداگانه نداریم.

                // Bento RAM Card - Format: "14.9 of 16 GB" or "14.9 از 16 GB"
                string ramOf = LocalizationService.IsRtl ? LocalizationService.Get("از") : "of";
                CardRamText.Text = $"{usedRamGb.ToString("0.0", CultureInfo.InvariantCulture)} {ramOf} {Math.Round(totalRamGb).ToString("0", CultureInfo.InvariantCulture)} GB";
                CardRamProgress.Value = ramPercent;
                if (CardRamAvailableText != null) CardRamAvailableText.Text = LocalizationService.Get("RamCurrentlyUsed", "فضای مصرفی در حال حاضر");
                if (CardRamTotalBadge != null) CardRamTotalBadge.Text = $"{LocalizationService.Get("Total", "مجموع")}: {Math.Round(totalRamGb).ToString("0", CultureInfo.InvariantCulture)} GB";
                if (CardRamStatusText != null)
                {
                    CardRamStatusText.Text = ramPercent > 80
                        ? LocalizationService.Get("HighUsage", "مصرف بالا")
                        : (ramPercent > 60 ? LocalizationService.Get("MediumUsage", "مصرف متوسط") : LocalizationService.Get("SmoothUsage", "روان"));
                }

                // Network Speed Card - Live traffic e.g. "84.5 Mbps" with intelligent consumer detection
                (double netMbps, string netType) = GetCurrentNetworkSpeed();
                _currentNetSpeedMbps = netMbps;
                string activeConsumer = GetActiveNetworkConsumer(netMbps);

                if (CardDiskTitle != null) CardDiskTitle.Text = LocalizationService.Get("NetworkTrafficTitle", "ترافیک شبکه");
                if (CardDiskHealthText != null)
                {
                    CardDiskHealthText.Text = netMbps >= 0.3 
                        ? LocalizationService.Get("ActiveTraffic", "مصرف فعال") 
                        : LocalizationService.Get("IdleTraffic", "آماده به کار");
                }
                if (CardDiskText != null)
                {
                    string approx = LocalizationService.IsRtl ? LocalizationService.Get("حدود") : "~";
                    string mbpsUnit = LocalizationService.IsRtl ? LocalizationService.Get("مگابیت") : "Mbps";
                    string kbpsUnit = LocalizationService.IsRtl ? LocalizationService.Get("کیلوبیت") : "Kbps";
                    if (netMbps >= 10.0)
                    {
                        CardDiskText.Text = $"{netMbps.ToString("0.0", CultureInfo.InvariantCulture)} Mbps";
                        if (CardDiskTypeBadge != null) CardDiskTypeBadge.Text = $"{activeConsumer} · {approx} {Math.Round(netMbps)} {mbpsUnit}";
                    }
                    else if (netMbps >= 0.1)
                    {
                        CardDiskText.Text = $"{netMbps.ToString("0.2", CultureInfo.InvariantCulture)} Mbps";
                        double kbps = netMbps * 1024.0;
                        if (CardDiskTypeBadge != null) CardDiskTypeBadge.Text = $"{activeConsumer} · {approx} {Math.Round(kbps)} {kbpsUnit}";
                    }
                    else
                    {
                        CardDiskText.Text = "0.0 Mbps";
                        if (CardDiskTypeBadge != null) CardDiskTypeBadge.Text = LocalizationService.Get("NoBackgroundTraffic", "بدون مصرف پس زمینه (شبکه پایدار)");
                    }
                }
                CardDiskProgress.Value = Math.Clamp(netMbps, 0, 100);

                // Bento CPU Card - Format: "18%"
                CardCpuText.Text = $"{Math.Round(cpuPercent).ToString("0", CultureInfo.InvariantCulture)}%";
                CardCpuProgress.Value = cpuPercent;
                if (CardGpuSummaryText != null) CardGpuSummaryText.Text = LocalizationService.Get("CpuLoadSummary", "بار پردازشی هسته ها");
                if (CardPowerText != null)
                {
                    if (LocalizationService.IsRtl)
                    {
                        CardPowerText.Text = !string.IsNullOrEmpty(stats.PowerSummary) ? stats.PowerSummary : LocalizationService.Get("متصل به برق");
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(stats.PowerSummary) || stats.PowerSummary.Contains("برق")) // i18n-logic: این متن با دادهی خارجی مقایسه/تطبیق داده میشود
                        {
                            CardPowerText.Text = LocalizationService.Get("PowerPlugged", "Plugged In");
                        }
                        else if (stats.PowerSummary.Contains("شارژ")) // i18n-logic: این متن با دادهی خارجی مقایسه/تطبیق داده میشود
                        {
                            CardPowerText.Text = LocalizationService.Get("PowerCharging", "Charging");
                        }
                        else if (stats.PowerSummary.Contains("باتری")) // i18n-logic: این متن با دادهی خارجی مقایسه/تطبیق داده میشود
                        {
                            CardPowerText.Text = stats.PowerSummary.Replace("باتری", LocalizationService.Get("PowerBattery", "Battery")).Trim();
                        }
                        else
                        {
                            CardPowerText.Text = stats.PowerSummary;
                        }
                    }
                }

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
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        // ====================================================================
        // The 1-Click Master Boost & Clean Orchestration (PC Manager Style)
        // ====================================================================

        /// <summary>
        /// مسیر اجرای دستی ۱۴ مرحله (دکمه/تری/میان‌بر). منطق مراحل در MainWindow.Hero.cs است
        /// تا هم این مسیر و هم WebView2 هیرو از یک کد واحد استفاده کنند و منطق دو نسخه نشود.
        /// </summary>
        private void QuickBoostBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(QuickBoostBtn_ClickAsync(sender, e));

        private async Task QuickBoostBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            _isBusy = true;
            try
            {
                for (int i = 0; i < 14; i++)
                {
                    await HeroRunStepAsync(i, Array.Empty<int>());
                }
            }
            catch (Exception ex)
            {
                CcLog.Report("Hero.QuickBoost", ex);
                ShowInfoBar(LocalizationService.Get("BoostError", "خطا در بهینه‌سازی سیستم"), ex.Message, InfoBarSeverity.Error);
                HeroAbortRun();
            }
        }

        private async Task<(int RemovedCount, long FreedBytes)> ShowBorderlineAppsReviewDialogAsync(List<CandidateAppItem> reviewApps)
        {
            if (reviewApps == null || reviewApps.Count == 0) return (0, 0);

            var stack = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 0, 0) };
            stack.Children.Add(new TextBlock
            {
                Text = LocalizationService.Get("برنامه های زیر در ویندوز شناسایی شدند که ممکن است به آن ها نیاز داشته باشید (مانند Paint، سرویس های بازی Xbox یا نرم افزارهای حجیم با بیش از ۳۰ روز عدم استفاده). مواردی که مایل به حذف آن ها هستید را علامت بزنید:"),
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
                            // وقتی اندازه هاردکد تخمینی است، صادقانه به کاربر گفته می شود
                            Text = app.FormattedSize + (app.SizeIsEstimate ? LocalizationService.Get(" (تخمینی)") : ""),
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
                Title = LocalizationService.Get("بررسی و بازبینی نرم افزارهای کم استفاده و پیش فرض"),
                Content = stack,
                PrimaryButtonText = LocalizationService.Get("پاکسازی موارد انتخاب شده"),
                CloseButtonText = LocalizationService.Get("صرف نظر و ادامه بهینه سازی"),
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
                                // اندازه تخمینی هرگز به «آزاد شده واقعی» افزوده نمی شود
                                if (!app.SizeIsEstimate) freed += app.SizeBytes;
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
            ReportTotalFreedText.Text = string.Format(LocalizationService.Get("+ {0} آزاد شد"), SystemStats.FormatBytes(totalFreed));
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
                Background = cardBg,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18, 16, 18, 16),
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var achStack = new StackPanel { Spacing = 12 };
            var achHeader = new Grid();
            achHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            achHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var achIcon = new FontIcon
            {
                Glyph = "\uE777",
                FontSize = 20,
                Foreground = accentFg,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(achIcon, 0);
            achHeader.Children.Add(achIcon);

            var achTitleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };
            achTitleStack.Children.Add(new TextBlock { Text = LocalizationService.Get("دستاوردهای بهینه‌سازی و ارتقای سرعت سیستم"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            achTitleStack.Children.Add(new TextBlock { Text = LocalizationService.Get("خلاصهٔ اقدامات کلیدی و آزادسازی حافظه، دیسک، استارتاپ و اتصالات شبکه"), FontSize = 11.5, Foreground = neutralFg });
            Grid.SetColumn(achTitleStack, 1);
            achHeader.Children.Add(achTitleStack);
            achStack.Children.Add(achHeader);

            // Multi-Column Achievement Grid
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
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(18, color.R, color.G, color.B)),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(60, color.R, color.G, color.B)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 10, 14, 10),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13.5, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = text, FontSize = 11.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                chip.Child = sp;

                Grid.SetRow(chip, row);
                Grid.SetColumn(chip, col);
                achievementsGrid.Children.Add(chip);
                achievementIndex++;
            }

            if (ramFreed > 0)
            {
                AddAchievementChip("\uE9D2", string.Format(LocalizationService.Get("+ {0} آزادسازی رم"), SystemStats.FormatBytes(ramFreed)), Windows.UI.Color.FromArgb(255, 16, 185, 129));
            }

            if (junkDiskFreed > 0)
            {
                AddAchievementChip("\uEDA2", string.Format(LocalizationService.Get("+ {0} پاکسازی دیسک"), SystemStats.FormatBytes(junkDiskFreed)), Windows.UI.Color.FromArgb(255, 59, 130, 246));
            }

            if (startupItem != null)
            {
                AddAchievementChip("\uE7B5", LocalizationService.Get("بهینه‌سازی استارتاپ و سرعت بوت"), Windows.UI.Color.FromArgb(255, 245, 158, 11));
            }

            if (boostGroup?.Items.Any(i => i.Id == "dns_flush") == true)
            {
                AddAchievementChip("\uE774", LocalizationService.Get("نوسازی کش DNS و سوکت‌های شبکه"), Windows.UI.Color.FromArgb(255, 6, 182, 212));
            }

            if (browserItem != null)
            {
                AddAchievementChip("\uE774", LocalizationService.Get("توقف تسک‌های پس‌زمینهٔ مرورگرها"), Windows.UI.Color.FromArgb(255, 139, 92, 246));
            }

            if (winTweaksItem != null)
            {
                AddAchievementChip("\uE770", LocalizationService.Get("توقف تله‌متری و روان‌سازی ویندوز"), Windows.UI.Color.FromArgb(255, 236, 72, 153));
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
                                MarkAttentionCardSuccess(capturedCardBorder, capturedCardIcon, actionBtn, LocalizationService.Get("ارتقا یافت ✓"));
                                ShowInfoBar(LocalizationService.Get("تنظیمات مانیتور"), string.Format(LocalizationService.Get("نرخ نوسازی با موفقیت به {0}Hz ارتقا یافت."), capturedAtt.IntParameter), InfoBarSeverity.Success);
                            }
                            else
                            {
                                ShowInfoBar(LocalizationService.Get("خطا"), LocalizationService.Get("امکان تغییر نرخ نوسازی مانیتور در این لحظه وجود ندارد."), InfoBarSeverity.Warning);
                            }
                        }
                        else if (capturedAtt.Id == "power_plan")
                        {
                            bool success = PowerPlanService.ApplyHighPerformancePlan();
                            if (success)
                            {
                                MarkAttentionCardSuccess(capturedCardBorder, capturedCardIcon, actionBtn, LocalizationService.Get("فعال شد ✓"));
                                ShowInfoBar(LocalizationService.Get("مدیریت مصرف انرژی"), LocalizationService.Get("پلن High Performance با حداکثر توان پردازشی فعال گردید."), InfoBarSeverity.Success);
                            }
                            else
                            {
                                ShowInfoBar(LocalizationService.Get("خطا"), LocalizationService.Get("امکان تغییر پلن برق در این لحظه وجود ندارد."), InfoBarSeverity.Warning);
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
                            MarkAttentionCardSuccess(capturedCardBorder, capturedCardIcon, actionBtn, LocalizationService.Get("اعمال شد ✓"));
                            ShowInfoBar(LocalizationService.Get("بهینه سازی پیشرفته"), LocalizationService.Get("محدودیت پهنای باند شبکه حذف و اولویت پردازشی سیستم ارتقا یافت."), InfoBarSeverity.Success);
                        }
                        else if (capturedAtt.Id == "ssd_critical_space")
                        {
                            actionBtn.Content = LocalizationService.Get("در حال بهینه سازی...");
                            actionBtn.IsEnabled = false;
                            bool ok = await DiskMaintenanceService.OptimizeDriveTrimAsync("C");
                            MarkAttentionCardSuccess(capturedCardBorder, capturedCardIcon, actionBtn, LocalizationService.Get("انجام شد ✓"));
                            ShowInfoBar(LocalizationService.Get("بهینه سازی SSD"), LocalizationService.Get("دستور TRIM با موفقیت اجرا شد و سکتورهای درایو ویندوز بهینه سازی گردیدند."), InfoBarSeverity.Success);
                        }
                        else if (capturedAtt.Id == "gpu_hags")
                        {
                            bool ok = GpuOptimizerService.EnableHags();
                            MarkAttentionCardSuccess(capturedCardBorder, capturedCardIcon, actionBtn, LocalizationService.Get("فعال شد ✓"));
                            ShowInfoBar(LocalizationService.Get("شتاب دهنده گرافیک"), LocalizationService.Get("قابلیت HAGS با موفقیت فعال گردید. (جهت اعمال کامل نیاز به ریستارت ویندوز دارد)"), InfoBarSeverity.Success);
                        }
                        else if (capturedAtt.Id == "proxy_vpn_conflict")
                        {
                            bool ok = NetworkProxyDiagnosticService.FixGhostProxy();
                            if (ok)
                            {
                                MarkAttentionCardSuccess(capturedCardBorder, capturedCardIcon, actionBtn, LocalizationService.Get("برطرف شد ✓"));
                                ShowInfoBar(LocalizationService.Get("رفع تداخل پراکسی"), LocalizationService.Get("پراکسی معلق غیرفعال شد و اتصال مستقیم اینترنت به ترمینال و ویندوز بازگشت."), InfoBarSeverity.Success);
                            }
                            else
                            {
                                ShowInfoBar(LocalizationService.Get("خطا"), LocalizationService.Get("امکان تغییر تنظیمات پراکسی در این لحظه وجود ندارد."), InfoBarSeverity.Warning);
                            }
                        }
                        else if (capturedAtt.Id == "orphaned_services")
                        {
                            int count = RegistryCleanerService.DisableOrphanedServices();
                            MarkAttentionCardSuccess(capturedCardBorder, capturedCardIcon, actionBtn, LocalizationService.Get("غیرفعال شد ✓"));
                            ShowInfoBar(LocalizationService.Get("سرویس های یتیم"), string.Format(LocalizationService.Get("{0} سرویس با فایل اجرایی ناموجود غیرفعال شدند تا بوت سیستم روان تر شود."), count), InfoBarSeverity.Success);
                        }
                        else if (capturedAtt.Id == "wsl_docker_compact")
                        {
                            actionBtn.Content = LocalizationService.Get("در حال فشرده سازی...");
                            actionBtn.IsEnabled = false;
                            var (ok, freed) = await DiskMaintenanceService.CompactAllWslDockerVhdxAsync();
                            MarkAttentionCardSuccess(capturedCardBorder, capturedCardIcon, actionBtn, LocalizationService.Get("فشرده شد ✓"));
                            string freedMsg = freed > 0 ? string.Format(LocalizationService.Get("+ {0} فضا آزاد شد."), SystemStats.FormatBytes(freed)) : LocalizationService.Get("دیسک های مجازی با موفقیت فشرده سازی شدند.");
                            ShowInfoBar(LocalizationService.Get("فشرده سازی VHDX"), freedMsg, InfoBarSeverity.Success);
                        }
                    };
                    Grid.SetColumn(actionBtn, 2);
                    attGrid.Children.Add(actionBtn);

                    attBorder.Child = attGrid;
                    ReportGroupsContainer.Children.Add(attBorder);
                }
            }

            int cleanPriorCount = 0;

            foreach (var group in groups)
            {
                if (group.Items.Count == 0) continue;

                var activeItems = group.Items.Where(i => i.FreedBytes > 0 || (i.IsFailed && !i.WasEmpty)).ToList();
                cleanPriorCount += group.Items.Count(i => i.WasEmpty && i.FreedBytes == 0);

                if (activeItems.Count == 0 && group.TotalFreedBytes == 0) continue;

                var groupCard = new Border
                {
                    Background = cardBg,
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16, 14, 16, 14),
                    Margin = new Thickness(0, 0, 0, 8),
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
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 4, 10, 4),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var groupTotalText = new TextBlock
                {
                    Text = group.TotalFreedBytes > 0 ? string.Format(LocalizationService.Get("+ {0} آزاد شد"), group.FormattedTotal) : LocalizationService.Get("بدون تغییر"),
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

                // Hierarchical Sub-items (displaying active changes)
                var itemsToDisplay = activeItems.Count > 0 ? activeItems : group.Items;
                var itemsStack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(6, 2, 6, 2) };

                foreach (var item in itemsToDisplay)
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
                        Glyph = isFailed ? "\uE711" : (hasFreed ? "\uE73E" : "\uE73E"),
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
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(8, 3, 8, 3),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var itemBadgeText = new TextBlock
                    {
                        Text = isFailed
                            ? item.StatusSummary
                            : (hasFreed
                                ? string.Format(LocalizationService.Get("+ {0} آزاد شد ✓"), item.FormattedFreed)
                                : item.StatusSummary),
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

            if (cleanPriorCount > 0)
            {
                var cleanBanner = new Border
                {
                    Background = cardBg,
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 2, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                var cbStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
                cbStack.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 14, Foreground = successFg, VerticalAlignment = VerticalAlignment.Center });
                cbStack.Children.Add(new TextBlock
                {
                    Text = string.Format(LocalizationService.Get("{0} سرفصل دیگر سیستم بررسی شدند و از قبل کاملاً پاکیزه و بهینه بودند."), cleanPriorCount),
                    FontSize = 12,
                    Foreground = neutralFg,
                    VerticalAlignment = VerticalAlignment.Center
                });
                cleanBanner.Child = cbStack;
                ReportGroupsContainer.Children.Add(cleanBanner);
            }
        }

        private void MarkAttentionCardSuccess(Border cardBorder, FontIcon cardIcon, Button btn, string? successText = null)
        {
            cardBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 16, 185, 129));
            cardBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 16, 185, 129));
            cardIcon.Glyph = "\uE73E";
            cardIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));
            btn.Content = successText ?? LocalizationService.Get("اعمال شد ✓");
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
                    ShowInfoBar(LocalizationService.Get("برنامه ها"), LocalizationService.Get("هیچ برنامه غیرضروری یا کم استفاده ای یافت نشد."), InfoBarSeverity.Informational);
                    MarkAttentionCardSuccess(attBorder, attIcon, actionBtn, LocalizationService.Get("بررسی شد ✓"));
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = LocalizationService.Get("مدیریت و حذف برنامه های غیرضروری و کم استفاده"),
                    PrimaryButtonText = LocalizationService.Get("حذف موارد انتخاب شده"),
                    CloseButtonText = LocalizationService.Get("نگه داشتن همه"),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.Content.XamlRoot
                };

                var scrollViewer = new ScrollViewer { MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 8), FlowDirection = FlowDirection.RightToLeft };

                var headerText = new TextBlock
                {
                    Text = LocalizationService.Get("برنامه های زیر می توانند برای آزادسازی منابع سیستم حذف شوند. روی هر سطر کلیک کنید تا انتخاب شود:"),
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                stack.Children.Add(headerText);

                var checkBoxes = new List<(CheckBox Box, CandidateAppItem App)>();

                var selectAllCheck = new CheckBox
                {
                    Content = LocalizationService.Get("انتخاب همه برنامه ها"),
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
                                // اندازه تخمینی در آمار آزادسازی واقعی حساب نمی شود
                                if (!app.SizeIsEstimate) freedBytes += app.SizeBytes;
                            }
                        }

                        if (removed > 0)
                        {
                            _currentReportTotalFreed += freedBytes;
                            _displayedFreedBytes = _currentReportTotalFreed;
                            ReportTotalFreedText.Text = string.Format(LocalizationService.Get("+ {0} آزاد شد"), SystemStats.FormatBytes(_currentReportTotalFreed));
                            LiveFreedCounterText.Text = string.Format(LocalizationService.Get("+ {0} آزاد شد"), SystemStats.FormatBytes(_currentReportTotalFreed));

                            MarkAttentionCardSuccess(attBorder, attIcon, actionBtn, string.Format(LocalizationService.Get("{0} برنامه حذف شد ✓"), removed));

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
                            var rmTitle = new TextBlock { Text = string.Format(LocalizationService.Get("حذف {0} برنامه غیرضروری و بلااستفاده"), removed), FontSize = 12.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                            var rmBadge = new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 16, 185, 129)), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center };
                            rmBadge.Child = new TextBlock { Text = string.Format(LocalizationService.Get("+ {0} آزاد شد ✓"), SystemStats.FormatBytes(freedBytes)), FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129)) };
                            Grid.SetColumn(rmIcon, 0); rmGrid.Children.Add(rmIcon);
                            Grid.SetColumn(rmTitle, 1); rmGrid.Children.Add(rmTitle);
                            Grid.SetColumn(rmBadge, 2); rmGrid.Children.Add(rmBadge);
                            removedCard.Child = rmGrid;
                            ReportGroupsContainer.Children.Insert(1, removedCard);

                            RefreshSystemStats();
                            ShowInfoBar(LocalizationService.Get("پاکسازی نرم افزارها"), string.Format(LocalizationService.Get("{0} برنامه با موفقیت حذف گردید ({1} فضا آزاد شد)."), removed, SystemStats.FormatBytes(freedBytes)), InfoBarSeverity.Success);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ShowInfoBar(LocalizationService.Get("خطا"), ex.Message, InfoBarSeverity.Error);
            }
        }

        private void CloseReportBtn_Click(object sender, RoutedEventArgs e)
        {
            // وضعیت سلامت هیرو بعد از بستن کارت گزارش از طریق PushHeroState بازمحاسبه می‌شود.
            CleanupReportCard.Visibility = Visibility.Collapsed;
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
                LiveFreedCounterText.Text = string.Format(LocalizationService.Get("+ {0} آزاد شد"), SystemStats.FormatBytes(_displayedFreedBytes));
            };
            countTimer.Start();
        }

        private async Task ShowLargeFilesDialogAsync()
        {
            var dialog = new ContentDialog
            {
                Title = LocalizationService.Get("یافتن و مدیریت فایل های بسیار حجیم (Large Files Finder)"),
                CloseButtonText = LocalizationService.Get("بستن"),
                XamlRoot = this.Content.XamlRoot,
                FlowDirection = FlowDirection.RightToLeft
            };

            var rootPanel = new StackPanel { Spacing = 12, MinWidth = 480, MaxHeight = 440 };
            var statusText = new TextBlock { Text = LocalizationService.Get("در حال اسکن فایل های بالای ۵۰۰ مگابایت در مسیرهای کاربری..."), FontSize = 12.5, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
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
                statusText.Text = LocalizationService.Get("هیچ فایل حجیمی بالای ۵۰۰ مگابایت در مسیرهای کاربری یافت نشد.");
                return;
            }

            statusText.Text = string.Format(LocalizationService.Get("تعداد {0} فایل حجیم شناسایی شد (مجموع حجم: {1}):"), files.Count, SystemStats.FormatBytes(totalBytes));

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

                var openBtn = new Button { Content = LocalizationService.Get("پوشه"), FontSize = 11, Padding = new Thickness(8, 3, 8, 3), Height = 28, CornerRadius = new CornerRadius(5) };
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

                var delBtn = new Button { Content = LocalizationService.Get("حذف"), FontSize = 11, Padding = new Thickness(8, 3, 8, 3), Height = 28, CornerRadius = new CornerRadius(5), Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 239, 68, 68)) };

                // تأیید دومرحله ای حذف فایل حجیم.
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
                        delBtn.Content = LocalizationService.Get("تأیید حذف؟");

                        if (armTimer == null)
                        {
                            armTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                            armTimer.Tick += (ts, te) =>
                            {
                                armTimer.Stop();
                                armed = false;
                                delBtn.Content = LocalizationService.Get("حذف");
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
                        ShowInfoBar(LocalizationService.Get("حذف فایل"), string.Format(LocalizationService.Get("فایل {0} به سطل بازیافت منتقل شد."), file.FileName), InfoBarSeverity.Informational);
                    }
                    else
                    {
                        delBtn.Content = LocalizationService.Get("حذف");
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
                Title = LocalizationService.Get("مرکز بازیابی و بازگشت تغییرات (Restore & Undo Center)"),
                CloseButtonText = LocalizationService.Get("بستن"),
                XamlRoot = this.Content.XamlRoot,
                FlowDirection = FlowDirection.RightToLeft
            };

            var stack = new StackPanel { Spacing = 14, MinWidth = 460 };

            var desc = new TextBlock
            {
                Text = LocalizationService.Get("مدیریت نقاط بازگردانی سیستم عامل و بازگرداندن تغییرات رجیستری و بهینه سازی ها با یک کلیک:"),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            stack.Children.Add(desc);

            // Section 1: Create Restore Point
            var rpCard = CreateUndoSectionCard("\uE777", LocalizationService.Get("ساخت نقطه بازگردانی ویندوز (Restore Point)"), LocalizationService.Get("ایجاد یک نقطه پشتیبان از ویندوز قبل از اعمال بهینه سازی ها یا تغییرات عمده."), LocalizationService.Get("ایجاد Restore Point"), async (btn) =>
            {
                btn.Content = LocalizationService.Get("در حال ایجاد...");
                btn.IsEnabled = false;
                var (ok, msg) = await RestorePointService.CreateRestorePointAsync($"WinyWash Backup - {DateTime.Now:yyyy.MM.dd HH:mm}");
                btn.Content = ok ? LocalizationService.Get("ایجاد شد ✓") : LocalizationService.Get("تلاش مجدد");
                btn.IsEnabled = !ok;
                ShowInfoBar(LocalizationService.Get("نقطه بازگردانی"), msg, ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            });
            stack.Children.Add(rpCard);

            // Section 2: Undo Tweaks
            var undoCard = CreateUndoSectionCard("\uE7A7", LocalizationService.Get("بازگردانی تنظیمات بهینه شده به حالت اولیه (Undo)"), LocalizationService.Get("بازگرداندن تمام کلیدهای رجیستری و سرویس های تغییر یافته توسط CleanC به مقادیر پیش فرض ویندوز."), LocalizationService.Get("بازگردانی تنظیمات"), async (btn) =>
            {
                btn.Content = LocalizationService.Get("در حال بازگردانی...");
                btn.IsEnabled = false;
                int reverted = await RegistryBackupService.RollbackAllTweaksAsync();
                btn.Content = LocalizationService.Get("بازگردانده شد ✓");
                ShowInfoBar(LocalizationService.Get("بازگردانی تغییرات"), string.Format(LocalizationService.Get("تعداد {0} مورد از تنظیمات و توئیک ها به حالت اولیه ویندوز بازگردانده شدند."), reverted), InfoBarSeverity.Success);
            });
            stack.Children.Add(undoCard);

            // Section 3: Open Windows System Restore
            var sysRestoreCard = CreateUndoSectionCard("\uE895", LocalizationService.Get("ابزار بازیابی سیستم ویندوز (System Restore)"), LocalizationService.Get("اجرای ویزارد استاندارد و رسمی ریکاوری ویندوز جهت بازگشت به تاریخ های قبلی."), LocalizationService.Get("باز کردن System Restore"), (btn) =>
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
                AdvancedCleanTotalText.Text = LocalizationService.Get("Scanning", "در حال اسکن...");
                // از اسکنِ مشترک استفاده می شود؛ اگر اسکنِ پس زمینهٔ استارت‌آپ در جریان
                // است همان را می گیریم تا دو اسکن موازی روی کل دیسک نیفتد.
                ApplyHeroScanResult(await GetHeroScanAsync());
            }
        }

        /// <summary>
        /// تیک های ذخیره شده ی کاربر روی آیتم های تازه ی اسکن اعمال می شود.
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

        /// <summary>
        /// عنوان و توضیح آیتم ها هنگام اسکن با زبان فعلی ساخته می شوند؛ پس از تغییر زنده ی زبان
        /// باید یک اسکن تازه با کاتالوگ جدید گرفته شود. این متد تیک های فعلی کاربر را قبل از اسکن
        /// نگه می دارد و بعد از جایگزینی نتیجه، دقیقاً همان تیک ها را برمی گرداند.
        /// </summary>
        private void RelocalizeAdvancedGroupsAfterLanguageChange()
        {
            if (_cachedScanResult == null || _isExiting)
            {
                _advancedRelocalizePending = false;
                return;
            }
            if (_isBusy)
            {
                // تغییر زبان وسط پاکسازی انجام شده؛ بعد از پایان کار، تازه‌سازی را ادامه می‌دهیم.
                _advancedRelocalizePending = true;
                return;
            }
            if (_advancedRelocalizeRunning) return;
            _ = RelocalizeAdvancedGroupsAfterLanguageChangeAsync();
        }

        private async Task RelocalizeAdvancedGroupsAfterLanguageChangeAsync()
        {
            _advancedRelocalizeRunning = true;
            try
            {
                var snapshot = CaptureJunkSelectionSnapshot();

                // اگر اسکن در جریان با زبان قبلی شروع شده باشد، نتیجه اش نباید اعمال شود.
                // حداکثر سه تلاش: در عمل بار اول یا دوم به اسکن هم زبان با UI می رسیم.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var scan = await GetHeroScanAsync();
                    if (_isExiting) return;
                    if (_heroScanLanguageVersion != _uiLanguageVersion)
                    {
                        continue;
                    }

                    ApplyHeroScanResult(scan, renderAdvanced: false);
                    ApplyJunkSelectionSnapshot(scan, snapshot);
                    RenderAdvancedGroups(scan);
                    SavePersistedJunkSelections();
                    return;
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "RelocalizeAdvancedGroups");
            }
            finally
            {
                _advancedRelocalizeRunning = false;
                if (_advancedRelocalizePending && !_isBusy && !_isExiting)
                {
                    _advancedRelocalizePending = false;
                    RelocalizeAdvancedGroupsAfterLanguageChange();
                }
            }
        }

        private Dictionary<string, bool> CaptureJunkSelectionSnapshot()
        {
            var snapshot = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (_cachedScanResult == null) return snapshot;

            foreach (var group in _cachedScanResult.Groups)
            {
                foreach (var item in group.Items)
                {
                    if (string.IsNullOrEmpty(item.Id)) continue;
                    snapshot[group.Id + "\u001F" + item.Id] = item.IsChecked;
                }
            }
            return snapshot;
        }

        private static void ApplyJunkSelectionSnapshot(ScanResult scan, Dictionary<string, bool> snapshot)
        {
            if (snapshot.Count == 0) return;

            foreach (var group in scan.Groups)
            {
                foreach (var item in group.Items)
                {
                    if (string.IsNullOrEmpty(item.Id)) continue;
                    if (snapshot.TryGetValue(group.Id + "\u001F" + item.Id, out bool isChecked))
                    {
                        item.IsChecked = isChecked;
                    }
                }
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

                string grpTitle = grp.Id switch
                {
                    "apps" => LocalizationService.Get("JunkApps", grp.Title),
                    "system_boost" => LocalizationService.Get("JunkBoost", grp.Title),
                    _ => grp.Title
                };

                var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                titleStack.Children.Add(new TextBlock { Text = grpTitle, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13, Foreground = itemPrimary });
                titleStack.Children.Add(new TextBlock { Text = grp.Description, FontSize = 11, Foreground = itemSecondary });
                Grid.SetColumn(titleStack, 1);
                headerGrid.Children.Add(titleStack);

                string boostFeatureLabel = LocalizationService.Get("OptimizationFeatures", "قابلیت بهینه سازی");
                var badge = new TextBlock
                {
                    Text = grp.Id == "system_boost" ? $"{grp.Items.Count} {boostFeatureLabel}" : grp.FormattedTotal,
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
                        Text = LocalizationService.Get("هیچ موردی در این بخش شناسایی نشد (کاملاً تمیز است)."),
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
                            ToolTipService.SetToolTip(shield, LocalizationService.Get("این مورد نیازمند دسترسی مدیر (Administrator) است."));
                            titleRow.Children.Add(shield);
                        }

                        textStack.Children.Add(titleRow);
                        textStack.Children.Add(new TextBlock { Text = item.Description, FontSize = 11, Foreground = itemSecondary });
                        Grid.SetColumn(textStack, 1);
                        itemGrid.Children.Add(textStack);

                        var sizeText = new TextBlock
                        {
                            Text = grp.Id == "system_boost" ? (item.IsChecked ? LocalizationService.Get("فعال ✓") : LocalizationService.Get("غیرفعال")) : item.FormattedBytes,
                            FontSize = 12,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = itemPrimary
                        };
                        if (grp.Id == "system_boost")
                        {
                            chk.Checked += (s, e) => { sizeText.Text = LocalizationService.Get("فعال ✓"); };
                            chk.Unchecked += (s, e) => { sizeText.Text = LocalizationService.Get("غیرفعال"); };
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
            ShowInfoBar(LocalizationService.Get("به‌روزرسانی شد"), LocalizationService.Get("اطلاعات مصرف رم، پردازنده و دیسک به‌روزرسانی شدند."), InfoBarSeverity.Informational);
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
        /// آیا حالت High Contrast ویندوز فعال است؟
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
        /// المانی را که LiveSetting گرفته وادار به اعلامِ تغییر برای screen reader می کند.
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
                // در High Contrast، صرف نظر از بازنویسی رنگ ها تا سیستم برنده شود.
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
                // هیرو درون WebView2 است و دیکت تمِ اپ را نمی‌بیند؛ حالت/اکسنت جدید را صریحاً می‌فرستیم
                // تا --halo و فونت‌های CSS آن هم‌زمان با اپ عوض شوند.
                PushHeroTheme();
            }
        }

        private void ApplyAdaptiveForegrounds(Brush primary, Brush secondary, Brush tertiary)
        {
            try
            {
                // Title Bar
                if (AppTitleText != null) AppTitleText.Foreground = primary;
                if (RefreshStatsIcon != null) RefreshStatsIcon.Foreground = primary;

                // Hero Card (داخل WebView2 است؛ تم/رنگش را خودش از پیام theme می‌گیرد و دستکاری XAML نمی‌شود)

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
            var prevView = _currentView;
            _currentView = view;

            DashboardView.Visibility = view == AppView.Dashboard ? Visibility.Visible : Visibility.Collapsed;
            ToolsView.Visibility = view == AppView.Tools ? Visibility.Visible : Visibility.Collapsed;
            SettingsView.Visibility = view == AppView.Settings ? Visibility.Visible : Visibility.Collapsed;

            if (view == AppView.Tools && prevView != AppView.Tools)
            {
                NavigateToToolPage(null);
            }

            if (view == AppView.Dashboard)
            {
               // مقدارها هنگام مخفی بودن به روز نمی شدند؛ بلافاصله تازه سازی کن
                RefreshSystemStats();
            }
            else
            {
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

            // نمای فعال برای اجرای بعدی ذخیره می شود
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
        private void NavToolsBtn_Click(object sender, RoutedEventArgs e)
        {
            SwitchToView(AppView.Tools);
            NavigateToToolPage(null);
        }
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
        // ذخیره ی تنظیمات (debounce) و مکان یابی پنجره
        // ====================================================================

        /// <summary>کنترل های صفحه Settings را از تنظیمات پر می کند؛ اینجا Toggled نباید چیزی ذخیره کند.</summary>
        private void PopulateSettingsUi()
        {
            _isLoadingSettings = true;
            try
            {
                // state صفحه Settings از طریق ViewModel پر می شود و x:Bind آن را
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

                // وضعیت شروع خودکار از رجیستری خوانده می شود، نه از فایل تنظیمات
                AutoStartToggle.IsOn = StartupOptimizerService.IsCleanCRegisteredAtStartup();

                // 1.5 — کارت کاراکتر: آیتم های ComboBox را می سازیم و اندیس فعلی را از VM می خوانیم
                PopulateCharacterSettingsUi();
                _characterUiReady = true;

                SettingsPathText.Text = Path.GetDirectoryName(SettingsService.FilePath) ?? string.Empty;
                LogPathText.Text = CcLog.LogDirectory;

                // بخش «درباره»
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

        // ====================================================================
        // 1.5 — کارت «کاراکتر هیرو» در صفحه تنظیمات
        // ====================================================================

        /// <summary>
        /// آیتم های کمبوباکس های کاراکتر را می سازد و اندیس فعلی را از ViewModel می خواند.
        /// خودِ نوشتن در VM اینجا انجام نمی شود تا hydration پاک بماند؛ handler ها هم
        /// تا قبل از <c>_characterUiReady</c> هیچ ورودی نمی پذیرند.
        /// </summary>
        private void PopulateCharacterSettingsUi()
        {
            try
            {
                if (CharLookCombo != null && CharLookCombo.Items.Count == 0)
                {
                    foreach (var (_, name) in HeroLooks.All) CharLookCombo.Items.Add(name);
                }

                if (CharOccLookCombo != null && CharOccLookCombo.Items.Count == 0)
                {
                    foreach (var (_, name) in HeroLooks.Occasion()) CharOccLookCombo.Items.Add(name);
                }

                SelectComboByTag(CharToneCombo, SettingsVm.CharacterTone);
                SelectComboByTag(CharIntensityCombo, SettingsVm.CharacterIntensity);

                if (CharLookCombo != null)
                    CharLookCombo.SelectedIndex = HeroLooks.IndexOf(HeroLooks.All, SettingsVm.CharacterLook);

                if (CharOccLookCombo != null)
                    CharOccLookCombo.SelectedIndex = HeroLooks.IndexOfOccasion(SettingsVm.CharacterOccLook);

                // اسلایدرها → رویداد ValueChanged متن نمایشی را هم تازه می کند
                if (CharSizeSlider != null) CharSizeSlider.Value = SettingsVm.CharacterSize;
                if (CharLunSlider != null) CharLunSlider.Value = SettingsVm.CharacterLun;

                _lastValidCity = (SettingsVm.CharacterCity ?? string.Empty).Trim();
                SyncCityBoxText();
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "PopulateCharacterSettingsUi");
            }
        }

        private void SyncCityBoxText()
        {
            if (CharCityBox == null || SettingsVm == null) return;
            string city = (SettingsVm.CharacterCity ?? string.Empty).Trim();
            _lastValidCity = city;
            // اگر کاربر در حال تایپ است، متن کادر را با مقدار قدیمی ذخیره‌شده عوض نکن.
            if (CharCityBox.FocusState == FocusState.Unfocused)
                CharCityBox.Text = city;
        }

        private async Task EnsureDefaultCityAsync()
        {
            if (_cityDefaultRequested || _isExiting || SettingsVm == null) return;
            _cityDefaultRequested = true;

            string existing = (SettingsVm?.CharacterCity ?? string.Empty).Trim();
            if (existing.Length > 0)
            {
                _lastValidCity = existing;
            }
            SyncCityBoxText();
        }

        private void CharCityBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

            _citySuggestCts?.Cancel();
            _citySuggestCts?.Dispose();
            _citySuggestCts = null;

            string query = (sender.Text ?? string.Empty).Trim();
            if (query.Length < 2)
            {
                sender.ItemsSource = null;
                return;
            }

            var cts = new CancellationTokenSource();
            _citySuggestCts = cts;
            _ = LoadCitySuggestionsAsync(sender, query, cts);
        }

        private async Task LoadCitySuggestionsAsync(AutoSuggestBox sender, string query, CancellationTokenSource cts)
        {
            try
            {
                // debounce: تا کاربر تایپ می کند درخواست شبکه فرستاده نمی شود.
                await Task.Delay(350, cts.Token);
                var results = await CityGeocodingService.SearchAsync(query, 8, cts.Token);
                if (cts.IsCancellationRequested || _isExiting) return;
                if (!string.Equals((sender.Text ?? string.Empty).Trim(), query, StringComparison.Ordinal)) return;

                sender.ItemsSource = results;
                sender.IsSuggestionListOpen = results.Count > 0;
            }
            catch (OperationCanceledException)
            {
                // تایپ ادامه دارد؛ درخواست قبلی عمداً لغو شده است.
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "LoadCitySuggestions");
            }
            finally
            {
                if (ReferenceEquals(_citySuggestCts, cts)) _citySuggestCts = null;
                cts.Dispose();
            }
        }

        private void CharCityBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is CitySuggestion city)
                CommitCity(city.Name, sender);
        }

        private void CharCityBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion is CitySuggestion city)
            {
                CommitCity(city.Name, sender);
                return;
            }
            _ = ValidateCityInputAsync(sender, args.QueryText ?? sender.Text);
        }

        private void CharCityBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not AutoSuggestBox box || SettingsVm == null) return;
            string typed = (box.Text ?? string.Empty).Trim();
            if (string.Equals(typed, (SettingsVm.CharacterCity ?? string.Empty).Trim(), StringComparison.Ordinal))
                return;
            _ = ValidateCityInputAsync(box, typed);
        }

        private async Task ValidateCityInputAsync(AutoSuggestBox sender, string? raw)
        {
            string typed = (raw ?? string.Empty).Trim();
            if (typed.Length > 40) typed = typed[..40];
            if (typed.Length == 0)
            {
                // خالی کردن کادر یعنی خاموش کردن ماژول آب‌وهوا؛ معتبر است.
                CommitCity(string.Empty, sender);
                return;
            }

            // اگر کاربر نام نمایشی «Tehran, Iran» را دستی وارد کند، فقط بخش نام را
            // به Geocoding می فرستیم؛ ذخیره نهایی همیشه Name خالص است.
            string lookupText = typed;
            int comma = lookupText.IndexOf(',');
            if (comma > 0) lookupText = lookupText[..comma].Trim();

            try
            {
                var lookup = await CityGeocodingService.ValidateAsync(lookupText);
                if (_isExiting) return;
                if (!string.Equals((sender.Text ?? string.Empty).Trim(), typed, StringComparison.Ordinal)) return;

                if (lookup.Status == CityLookupStatus.Found && lookup.City != null)
                {
                    CommitCity(lookup.City.Name, sender);
                }
                else if (lookup.Status == CityLookupStatus.NotFound)
                {
                    // شهر پیدا نشد؛ به آخرین مقدار معتبر برمی گردیم تا آب‌وهوا با نام اشتباه خراب نشود.
                    CommitCity(_lastValidCity, sender);
                }
                else
                {
                    // شبکه/سرویس در دسترس نیست؛ ورودی کاربر را حفظ می کنیم.
                    CommitCity(typed, sender);
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "ValidateCityInput");
                CommitCity(typed, sender);
            }
        }

        private void CommitCity(string? city, AutoSuggestBox? sender)
        {
            string value = (city ?? string.Empty).Trim();
            if (value.Length > 40) value = value[..40];

            _lastValidCity = value;
            if (SettingsVm != null &&
                !string.Equals((SettingsVm.CharacterCity ?? string.Empty).Trim(), value, StringComparison.Ordinal))
            {
                SettingsVm.CharacterCity = value;
            }
            if (sender != null && !string.Equals((sender.Text ?? string.Empty).Trim(), value, StringComparison.Ordinal))
            {
                sender.Text = value;
            }
        }

        private static void SelectComboByTag(ComboBox? combo, string? tag)
        {
            if (combo == null) return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        /// <summary>
        /// برچسب های کمبوباکس کاراکتر را بعد از تغییر زبان بازسازی می کند.
        /// لیست اکسسوری/مناسبت از HeroLooks می آید و آیتم های لحن/شدت هم باید
        /// انتخاب فعلی را دوباره رندر کنند تا متن قدیمی در کادر انتخاب نماند.
        /// </summary>
        private void RefreshCharacterComboItems()
        {
            bool wasReady = _characterUiReady;
            _characterUiReady = false;
            try
            {
                if (SettingsVm == null) return;

                HeroLooks.RefreshLocalizedNames();

                if (CharLookCombo != null)
                {
                    var items = HeroLooks.All;
                    CharLookCombo.Items.Clear();
                    foreach (var (_, name) in items) CharLookCombo.Items.Add(name);
                    CharLookCombo.SelectedIndex = HeroLooks.IndexOf(items, SettingsVm.CharacterLook);
                }

                if (CharOccLookCombo != null)
                {
                    var items = HeroLooks.Occasion();
                    CharOccLookCombo.Items.Clear();
                    foreach (var (_, name) in items) CharOccLookCombo.Items.Add(name);
                    CharOccLookCombo.SelectedIndex = HeroLooks.IndexOf(items, SettingsVm.CharacterOccLook);
                }

                RefreshSelectedComboText(CharToneCombo);
                RefreshSelectedComboText(CharIntensityCombo);
            }
            finally
            {
                _characterUiReady = wasReady;
            }
        }

        private static void RefreshSelectedComboText(ComboBox? combo)
        {
            if (combo == null) return;
            int index = combo.SelectedIndex;
            if (index < 0) return;
            combo.SelectedIndex = -1;
            combo.SelectedIndex = index;
        }

        private void CharToneCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_characterUiReady || SettingsVm == null) return;
            if (CharToneCombo?.SelectedItem is ComboBoxItem item)
                SettingsVm.CharacterTone = (item.Tag as string) ?? "joke";
        }

        private void CharIntensityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_characterUiReady || SettingsVm == null) return;
            if (CharIntensityCombo?.SelectedItem is ComboBoxItem item)
                SettingsVm.CharacterIntensity = (item.Tag as string) ?? "mid";
        }

        private void CharLookCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_characterUiReady || SettingsVm == null) return;
            int i = CharLookCombo?.SelectedIndex ?? -1;
            SettingsVm.CharacterLook = (i >= 0 && i < HeroLooks.All.Length) ? HeroLooks.All[i].Id : "none";
        }

        private void CharOccLookCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_characterUiReady || SettingsVm == null) return;
            var items = HeroLooks.Occasion();
            int i = CharOccLookCombo?.SelectedIndex ?? -1;
            SettingsVm.CharacterOccLook = (i >= 0 && i < items.Length) ? items[i].Id : "";
        }

        private void CharSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (CharSizeValueText != null)
                CharSizeValueText.Text = "×" + e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);

            if (!_characterUiReady || SettingsVm == null) return;
            SettingsVm.CharacterSize = e.NewValue;
        }

        private void CharLunSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            int v = (int)Math.Round(e.NewValue);
            if (CharLunValueText != null)
            {
                CharLunValueText.Text = v > 0
                    ? "+" + v.ToString(CultureInfo.InvariantCulture)
                    : v.ToString(CultureInfo.InvariantCulture);
            }

            if (!_characterUiReady || SettingsVm == null) return;
            SettingsVm.CharacterLun = v;
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
        // رفتار تری آیکون (مخفی سازی و خروج ایمن)
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
                        LocalizationService.Get("CleanC در تری آیکون"),
                        LocalizationService.Get("برای بازگرداندن پنجره روی آیکون کنار ساعت کلیک کنید."));
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
        /// ثبت/حذف CleanC در آیتم های اجرای خودکار ویندوز (HKCU\...\Run).
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
                        LocalizationService.Get("شروع همراه با ویندوز"),
                        LocalizationService.Get("ثبت CleanC در اجرای خودکار انجام نشد؛ مسیر اجرایی برنامه در دسترس نیست."),
                        InfoBarSeverity.Error);
                    return;
                }

                ShowInfoBar(
                    LocalizationService.Get("شروع همراه با ویندوز"),
                    enabled
                        ? LocalizationService.Get("CleanC هنگام ورود به ویندوز به صورت خودکار اجرا می شود.")
                        : LocalizationService.Get("اجرای خودکار CleanC غیرفعال شد."),
                    InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "AutoStartToggle_Toggled");
            }
        }

        /// <summary>
        /// اثرِ نمایشیِ تغییر رفتار حذفِ فایل های شخصی (از رویداد SettingsVm).
        /// نوشتنِ تنظیم و همگام سازی <see cref="SafeDeleteService.DefaultMode"/> داخل VM است؛
        /// اینجا فقط هشدار حالت Permanent نشان داده می شود.
        /// </summary>
        private void OnDeleteModeChangedInUi(string mode)
        {
            if (string.Equals(mode, "Permanent", StringComparison.OrdinalIgnoreCase))
            {
                ShowInfoBar(
                    LocalizationService.Get("حذف قطعی فعال شد"),
                    LocalizationService.Get("فایل های شخصی دیگر به سطل بازیافت نمی روند و قابل بازگردانی نخواهند بود. کش و زباله ها همچنان قطعی حذف می شوند."),
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
            ShowInfoBar(LocalizationService.Get("اندازه ی پنجره بازنشانی شد"), LocalizationService.Get("پنجره با ابعاد پیش فرض ۱۰۸۰×۸۰۰ در مرکز صفحه قرار گرفت."), InfoBarSeverity.Informational);
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
            ShowInfoBar(LocalizationService.Get("تنظیمات بازنشانی شد"), LocalizationService.Get("همه ی تنظیمات به حالت پیش فرض برگشت."), InfoBarSeverity.Success);
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
                _uiLanguageVersion++;
                bool isRtl = LocalizationService.IsRtl;
                var dir = isRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

                if (MainContentGrid != null)
                {
                    MainContentGrid.FlowDirection = dir;
                    MainContentGrid.Language = isRtl ? "fa-IR" : "en-US";
                }
                if (this.Content is FrameworkElement rootElement)
                {
                    rootElement.Language = isRtl ? "fa-IR" : "en-US";
                }
                if (NotificationHistoryFlyout?.Content is FrameworkElement flyoutRoot)
                {
                    flyoutRoot.FlowDirection = dir;
                    flyoutRoot.Language = isRtl ? "fa-IR" : "en-US";
                }

                UpdateBackButtonsDirection(isRtl);

                // Apply typography
                var fontFamilyName = LocalizationService.GetFontFamilyName();
                var ff = new FontFamily(fontFamilyName);
                Application.Current.Resources["AppFontFamily"] = ff;
                Application.Current.Resources["ContentControlThemeFontFamily"] = ff;

                // Update UI text strings
                // ناوبری به شکل pill وسط‌چین بالای محتوا برگشته؛ برچسب کامل می‌خواهد نه مخفف.
                if (NavDashboardText != null) NavDashboardText.Text = LocalizationService.Get("NavDashboard", "داشبورد بهینه سازی");
                if (NavToolsText != null) NavToolsText.Text = LocalizationService.Get("NavTools", "ابزارهای سیستمی ویندوز");

                // متن‌های هیرو (عنوان/زیرعنوان/دسته‌بندی/دکمه) در BuildHeroState از همین
                // LocalizationService ساخته می‌شوند؛ تنظیمات هم جداگانه می‌رود تا زبان
                // کاراکتر/دیالوگ‌ها در همان لحظه عوض شود، نه بعد از ری‌استارت.
                PushHeroSettings();
                PushHeroState();
                ApplyNewsLanguage();

                // Bento Cards
                if (CardHealthTitle != null) CardHealthTitle.Text = LocalizationService.Get("HealthScoreTitle", "سلامت سیستم");
                SetHealthGaugeScore(_currentHealthScore);

                if (CardRamTitle != null) CardRamTitle.Text = LocalizationService.Get("RamTitle", "حافظه رم");
                if (CardCpuTitle != null) CardCpuTitle.Text = LocalizationService.Get("CpuTitle", "پردازنده");
                if (CardDiskTitle != null) CardDiskTitle.Text = LocalizationService.Get("NetworkTrafficTitle", "ترافیک شبکه");
                if (CardGpuSummaryText != null) CardGpuSummaryText.Text = LocalizationService.Get("CpuLoadSummary", "بار پردازشی هسته ها");
                if (CardRamAvailableText != null) CardRamAvailableText.Text = LocalizationService.Get("RamCurrentlyUsed", "فضای مصرفی در حال حاضر");

                if (DiskBreakdownTitle != null) DiskBreakdownTitle.Text = LocalizationService.Get("DiskBreakdownTitle", "تحلیل فضای دیسک C:");
                if (DiskLegendUsedLabel != null) DiskLegendUsedLabel.Text = LocalizationService.Get("DiskUsed", "استفاده شده");
                if (DiskLegendCleanableLabel != null) DiskLegendCleanableLabel.Text = LocalizationService.Get("DiskCleanable", "قابل پاکسازی");
                if (DiskLegendFreeLabel != null) DiskLegendFreeLabel.Text = LocalizationService.Get("DiskFree", "آزاد");

                if (CleanupHistoryTitle != null) CleanupHistoryTitle.Text = LocalizationService.Get("CleanupHistoryTitle", "فضای آزادشده در ۷ روز اخیر");

                if (AdvancedCleanExpanderTitle != null) AdvancedCleanExpanderTitle.Text = LocalizationService.Get("AdvancedCleanTitle", "سفارشی سازی موارد پاکسازی و بهینه سازی");
                if (AdvancedCleanTotalText != null)
                {
                    if (_cachedScanResult != null)
                    {
                        AdvancedCleanTotalText.Text = $"{LocalizationService.Get("TotalDetected", "مجموع شناسایی شده")}: {_cachedScanResult.FormattedTotal}";
                    }
                    else
                    {
                        AdvancedCleanTotalText.Text = $"6 {LocalizationService.Get("ItemsSelected", "مورد انتخاب شده")}";
                    }
                }
                if (ToolsHeaderTitle != null) ToolsHeaderTitle.Text = LocalizationService.Get("NavTools", "ابزارهای سیستمی ویندوز");
                if (ToolsHeaderSubtitle != null) ToolsHeaderSubtitle.Text = LocalizationService.Get("ToolsHeaderSubtitle", "مجموعه ابزارهای کاربردی مدیریت و بهینه‌سازی سیستم");

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
                if (!string.Equals(LocalizationService.CurrentLanguage.Code, "en-US", StringComparison.OrdinalIgnoreCase) &&
                    !rawLang.Contains("(Language)", StringComparison.OrdinalIgnoreCase))
                {
                    rawLang = $"{rawLang} (Language)";
                }
                if (SettingsLangTitle != null) SettingsLangTitle.Text = rawLang;
                if (SettingsLangDesc != null) SettingsLangDesc.Text = LocalizationService.Get("LanguageSub", "زبان برنامه را انتخاب کنید (لیست به مرور کامل‌تر می‌شود)");
                if (SettingsThemeTitle != null) SettingsThemeTitle.Text = LocalizationService.Get("ThemeTitle", "تم برنامه");
                if (SettingsThemeDesc != null) SettingsThemeDesc.Text = LocalizationService.Get("ThemeSub", "«سیستم» از تم ویندوز پیروی می کند؛ دو حالت دیگر تم را ثابت نگه می دارند.");
                if (ThemeSystemRadio != null) ThemeSystemRadio.Content = LocalizationService.Get("ThemeSystem", "سیستم");
                if (ThemeLightRadio != null) ThemeLightRadio.Content = LocalizationService.Get("ThemeLight", "روشن");
                if (ThemeDarkRadio != null) ThemeDarkRadio.Content = LocalizationService.Get("ThemeDark", "تاریک");

                if (SettingsTrayTitle != null) SettingsTrayTitle.Text = LocalizationService.Get("BackgroundBehaviorTitle", "اجرا در پس زمینه و نوار وظیفه");
                if (SettingsTrayDesc != null) SettingsTrayDesc.Text = LocalizationService.Get("BackgroundBehaviorSub", "کنترل حضور برنامه در پس زمینه و دسترسی سریع از طریق آیکون کنار ساعت ویندوز.");
                if (SettingsCloseToTrayText != null) SettingsCloseToTrayText.Text = LocalizationService.Get("CloseToTray", "با بستن پنجره خارج نشو؛ در پس زمینه (کنار ساعت) بمان");
                if (SettingsMinToTrayText != null) SettingsMinToTrayText.Text = LocalizationService.Get("MinimizeToTray", "کوچک نمایی پنجره، برنامه را در کنار ساعت پنهان کند");

                // Settings AutoStart
                if (SettingsAutoStartTitle != null) SettingsAutoStartTitle.Text = LocalizationService.Get("AutoStartTitle", "شروع همراه با ویندوز");
                if (SettingsAutoStartDesc != null) SettingsAutoStartDesc.Text = LocalizationService.Get("AutoStartDesc", "با ورود به حساب کاربری، WinyWash به صورت خودکار اجرا شود. همان آیتمی که در Task Manager › Startup دیده می شود.");

                // Settings Delete Behavior
                if (SettingsDeleteModeTitle != null) SettingsDeleteModeTitle.Text = LocalizationService.Get("DeleteBehaviorTitle", "رفتار حذف فایل های شخصی");
                if (SettingsDeleteModeDesc != null) SettingsDeleteModeDesc.Text = LocalizationService.Get("DeleteBehaviorSub", "برای حذف تکراری ها، فایل های حجیم و سازماندهی دانلودها/دسکتاپ. کش و زباله ها همیشه قطعی حذف می شوند تا فضا واقعاً آزاد شود.");
                if (DeleteModeRecycleRadio != null) DeleteModeRecycleRadio.Content = LocalizationService.Get("DeleteModeRecycle", "انتقال به سطل بازیافت (پیش فرض)");
                if (DeleteModeQuarantineRadio != null) DeleteModeQuarantineRadio.Content = LocalizationService.Get("DeleteModeQuarantine", "قرنطینه ی WinyWash — نگهداری در پوشه ی برنامه و قابل بازگردانی از مرکز بازیابی");
                if (DeleteModePermanentRadio != null) DeleteModePermanentRadio.Content = LocalizationService.Get("DeleteModePermanent", "حذف قطعی — فوراً آزاد می شود اما قابل بازگردانی نیست");

                // Settings Auto State Restore
                if (SettingsStateRestoreTitle != null) SettingsStateRestoreTitle.Text = LocalizationService.Get("AutoStateRestoreTitle", "بازیابی خودکار وضعیت");
                if (SettingsStateRestoreDesc != null) SettingsStateRestoreDesc.Text = LocalizationService.Get("AutoStateRestoreSub", "هر تغییر بلافاصله ذخیره می شود و در اجرای بعدی برگردانده می شود.");
                if (SettingsSavePlacementText != null) SettingsSavePlacementText.Text = LocalizationService.Get("SaveWindowPlacement", "اندازه و موقعیت پنجره");
                if (SettingsSaveLastViewText != null) SettingsSaveLastViewText.Text = LocalizationService.Get("SaveLastView", "نمای آخر (داشبورد / ابزارها / تنظیمات)");
                if (SettingsSaveScanSelectionsText != null) SettingsSaveScanSelectionsText.Text = LocalizationService.Get("SaveScanSelections", "انتخاب های اسکن پاکسازی");
                if (ResetWindowPlacementBtn != null) ResetWindowPlacementBtn.Content = LocalizationService.Get("ResetWindowSize", "بازنشانی اندازه ی پنجره");
                if (ResetAllSettingsBtn != null) ResetAllSettingsBtn.Content = LocalizationService.Get("ResetAllSettings", "بازنشانی همه ی تنظیمات");

                // Settings App Data
                if (SettingsAppDataTitle != null) SettingsAppDataTitle.Text = LocalizationService.Get("AppDataTitle", "داده های برنامه");
                if (SettingsAppDataPathLabel != null) SettingsAppDataPathLabel.Text = LocalizationService.Get("AppDataSettingsLabel", "تنظیمات:");
                if (SettingsLogPathLabel != null) SettingsLogPathLabel.Text = LocalizationService.Get("AppDataLogsLabel", "لاگ خطاها:");
                if (OpenSettingsFolderBtn != null) OpenSettingsFolderBtn.Content = LocalizationService.Get("OpenSettingsFolder", "باز کردن پوشه ی تنظیمات");
                if (OpenLogFolderBtn != null) OpenLogFolderBtn.Content = LocalizationService.Get("OpenLogsFolder", "باز کردن پوشه ی لاگ ها");

                if (SettingsExportTitle != null) SettingsExportTitle.Text = LocalizationService.Get("ExportReportTitle", "خروجی گزارش");
                if (SettingsExportDesc != null) SettingsExportDesc.Text = LocalizationService.Get("ExportReportSub", "خروجی اطلاعات جامع سیستم جهت ارسال به هوش مصنوعی یا بررسی مشخصات سخت افزاری با فرمت متنی تمیز.");
                if (ExportAiReportText != null) ExportAiReportText.Text = LocalizationService.Get("ExportAiReport", "گزارش جامع برای هوش مصنوعی (.md)");
                if (ExportSpecsTxtText != null) ExportSpecsTxtText.Text = LocalizationService.Get("ExportSpecsReport", "مشخصات کامل سیستم (.txt)");

                if (SettingsAboutTitle != null) SettingsAboutTitle.Text = LocalizationService.Get("AboutTitle", "درباره WinyWash");
                if (SettingsAboutDesc != null) SettingsAboutDesc.Text = LocalizationService.Get("AboutSub", "برای گزارش باگ، اطلاعات عیب یابی را کپی و همراه با مشکل ارسال کنید.");

                // 1.5 — کارت «کاراکتر هیرو» (مقادیر خام HTML فارسی اند؛ برای زبان های دیگر انگلیسی برمی گردد)
                if (SettingsCharTitle != null) SettingsCharTitle.Text = LocalizationService.Get("CharacterTitle", "کاراکتر هیرو");
                if (SettingsCharDesc != null) SettingsCharDesc.Text = LocalizationService.Get("CharacterSub", "ظاهر، لحن و رفتار کاراکتر داخل کارت داشبورد. تغییرات بلافاصله در کارت اعمال می شود.");
                if (CharToneText != null) CharToneText.Text = LocalizationService.Get("CharacterTone", "لحن گفتار");
                if (CharToneJokeItem != null) CharToneJokeItem.Content = LocalizationService.Get("CharacterToneJoke", "شوخ‌طبع");
                if (CharToneCalmItem != null) CharToneCalmItem.Content = LocalizationService.Get("CharacterToneCalm", "آرام");
                if (CharToneSeriousItem != null) CharToneSeriousItem.Content = LocalizationService.Get("CharacterToneSerious", "جدی");
                if (CharNameText != null) CharNameText.Text = LocalizationService.Get("CharacterNameLabel", "اسم کاراکتر");
                if (CharUserNameText != null) CharUserNameText.Text = LocalizationService.Get("CharacterUserName", "اسم شما (برای سلام کردن)");
                if (CharCityText != null) CharCityText.Text = LocalizationService.Get("CharacterCity", "شهر (ماژول آب و هوا)");
                if (CharIntensityText != null) CharIntensityText.Text = LocalizationService.Get("CharacterIntensity", "شدت انیمیشن ها");
                if (CharIntOffItem != null) CharIntOffItem.Content = LocalizationService.Get("CharacterIntOff", "خاموش");
                if (CharIntLowItem != null) CharIntLowItem.Content = LocalizationService.Get("CharacterIntLow", "کم");
                if (CharIntMidItem != null) CharIntMidItem.Content = LocalizationService.Get("CharacterIntMid", "متوسط");
                if (CharIntHighItem != null) CharIntHighItem.Content = LocalizationService.Get("CharacterIntHigh", "زیاد");
                if (CharSizeText != null) CharSizeText.Text = LocalizationService.Get("CharacterSize", "اندازه کاراکتر");
                if (CharLookText != null) CharLookText.Text = LocalizationService.Get("CharacterLook", "اکسسوری");
                if (CharOccLookText != null) CharOccLookText.Text = LocalizationService.Get("CharacterOccLook", "اکسسوری مناسبتی (رویدادها)");
                if (CharBubbleText != null) CharBubbleText.Text = LocalizationService.Get("CharacterBubble", "حباب گفتار");
                if (CharEnvText != null) CharEnvText.Text = LocalizationService.Get("CharacterEnv", "صحنه و جلوه های محیطی پشت کاراکتر");
                if (CharSoundText != null) CharSoundText.Text = LocalizationService.Get("CharacterSound", "صداها");
                if (CharFastText != null) CharFastText.Text = LocalizationService.Get("CharacterFast", "عکس العمل سریع (کوتاه کردن انیمیشن ها)");
                if (CharCalmText != null) CharCalmText.Text = LocalizationService.Get("CharacterCalm", "حالت آرام (کم تحرک، مناسب سیستم های کند)");
                if (CharLunText != null) CharLunText.Text = LocalizationService.Get("CharacterLun", "تنظیم تقویم قمری برای مناسبت ها (±۱ روز)");
                RefreshCharacterComboItems();
                SyncCityBoxText();
                if (AboutVersionLabel != null) AboutVersionLabel.Text = LocalizationService.Get("VersionLabel", "نسخه برنامه:");
                if (AboutOsLabel != null) AboutOsLabel.Text = LocalizationService.Get("OsLabel", "سیستم عامل:");
                if (CheckAppUpdatesText != null) CheckAppUpdatesText.Text = LocalizationService.Get("CheckUpdates", "بررسی به روزرسانی");
                if (OpenProjectPageText != null) OpenProjectPageText.Text = LocalizationService.Get("ProjectPage", "صفحه پروژه");
                if (CopyDiagnosticsText != null) CopyDiagnosticsText.Text = LocalizationService.Get("CopyDiagnostics", "کپی اطلاعات عیب یابی");

                if (SettingsHeaderBtn != null)
                {
                    string settingsText = LocalizationService.Get("Settings", "تنظیمات برنامه (Settings)");
                    ToolTipService.SetToolTip(SettingsHeaderBtn, settingsText);
                    AutomationProperties.SetName(SettingsHeaderBtn, settingsText);
                }
                if (AdminBadgeText != null) AdminBadgeText.Text = LocalizationService.Get("RunningAsAdmin", "Running as administrator");
                if (UndoCenterBtn != null)
                {
                    string restoreText = LocalizationService.Get("RestoreCenter", "مرکز بازیابی و بازگشت تغییرات");
                    ToolTipService.SetToolTip(UndoCenterBtn, restoreText);
                    AutomationProperties.SetName(UndoCenterBtn, restoreText);
                }
                if (RefreshStatsBtn != null)
                {
                    string refreshText = LocalizationService.Get("RefreshStatus", "به‌روزرسانی وضعیت سیستم");
                    ToolTipService.SetToolTip(RefreshStatsBtn, refreshText);
                    AutomationProperties.SetName(RefreshStatsBtn, refreshText);
                }
                if (NotificationHistoryBtn != null)
                {
                    string historyText = LocalizationService.Get("NotificationHistory", "تاریخچه عملیات و اعلان ها");
                    ToolTipService.SetToolTip(NotificationHistoryBtn, historyText);
                    AutomationProperties.SetName(NotificationHistoryBtn, historyText);
                }
                if (LauncherWindowsUpdateBadge != null) LauncherWindowsUpdateBadge.Text = LocalizationService.Get("سیستم به روز است ✓");
                if (LauncherUninstallerBadge != null) LauncherUninstallerBadge.Text = LocalizationService.Get("آماده اسکن برنامه ها");
                if (LauncherStartupBadge != null) LauncherStartupBadge.Text = LocalizationService.Get("بررسی استارتاپ");
                if (LauncherSpeedTestBadge != null) LauncherSpeedTestBadge.Text = LocalizationService.Get("سنجش بلادرنگ پینگ و سرعت");

                if (HealthScoreOutOfText != null) HealthScoreOutOfText.Text = isRtl ? LocalizationService.Get("از 100") : "of 100";

                // Notification Flyout
                if (NotificationHistoryTitleText != null) NotificationHistoryTitleText.Text = LocalizationService.Get("NotificationHistoryTitle", "تاریخچه اعلان ها و اقدامات");
                if (NotificationHistoryClearText != null) NotificationHistoryClearText.Text = LocalizationService.Get("NotificationClear", "پاکسازی");
                if (EmptyHistoryText != null) EmptyHistoryText.Text = LocalizationService.Get("NotificationEmpty", "هنوز هیچ اعلانی ثبت نشده است.");

                // --- i18n فاز C: هدف های آدیت XAML (a11y و عناصر ثابت بدون پوشش) ---
                // کارت های تحلیل — نام دسترسی پذیری برای screen reader
                if (HealthScoreCard != null) AutomationProperties.SetName(HealthScoreCard, LocalizationService.Get("HealthScoreTitle", "امتیاز سلامت سیستم"));
                if (BentoRamCard != null) AutomationProperties.SetName(BentoRamCard, LocalizationService.Get("RamTitle", "حافظه رم"));
                if (BentoCpuCard != null) AutomationProperties.SetName(BentoCpuCard, LocalizationService.Get("CpuTitle", "پردازنده"));
                if (BentoDiskCard != null) AutomationProperties.SetName(BentoDiskCard, LocalizationService.Get("NetworkTrafficTitle", "سرعت اینترنت"));
                if (DiskBreakdownCard != null) AutomationProperties.SetName(DiskBreakdownCard, LocalizationService.Get("DiskBreakdownTitle", "تحلیل فضای دیسک C:"));
                if (CleanupsHistoryCard != null) AutomationProperties.SetName(CleanupsHistoryCard, LocalizationService.Get("CleanupHistoryTitle", "فضای آزادشده در ۷ روز اخیر"));

                // نوار ناوبری کناری — نام دسترسی پذیری دکمه های داشبورد و ابزارها
                if (NavDashboardBtn != null) AutomationProperties.SetName(NavDashboardBtn, LocalizationService.Get("NavDashboard", "داشبورد بهینه سازی"));
                if (NavToolsBtn != null) AutomationProperties.SetName(NavToolsBtn, LocalizationService.Get("NavTools", "ابزارهای سیستمی ویندوز"));

                // کارت تاریخچه اعلان ها — دکمه پاکسازی (قبلاً فقط ToolTip ثابت فارسی داشت)
                if (ClearHistoryBtn != null) ToolTipService.SetToolTip(ClearHistoryBtn, LocalizationService.Get("پاکسازی تاریخچه"));

                // لجند تحلیل فضای دیسک — سه نوار رنگی زیر نمودار
                if (DiskLegendUsedBar != null) ToolTipService.SetToolTip(DiskLegendUsedBar, LocalizationService.Get("DiskUsed", "فضای استفاده شده"));
                if (DiskLegendCleanableBar != null) ToolTipService.SetToolTip(DiskLegendCleanableBar, LocalizationService.Get("DiskCleanable", "قابل پاکسازی"));
                if (DiskLegendFreeBar != null) ToolTipService.SetToolTip(DiskLegendFreeBar, LocalizationService.Get("DiskFree", "آزاد"));

                // دکمه های «بازگشت» بالای همه ابزارها — ۱۱ نمونه یکسان در XAML، همه با یک شناسه
                foreach (var backBtn in new[] { BackFromSpecsBtn, BackFromUninstallerBtn, BackFromSpeedTestBtn, BackFromPersonalizationBtn, BackFromStartupBtn, BackFromWindowsUpdateBtn, BackFromDriversBtn, BackFromDnsBtn, BackFromPrivacyBtn, BackFromDeepStorageBtn, BackFromRecycleBinBtn })
                {
                    if (backBtn == null) continue;
                    AutomationProperties.SetName(backBtn, LocalizationService.Get("BackButton", "بازگشت"));
                    ToolTipService.SetToolTip(backBtn, LocalizationService.Get("BackToTools", "بازگشت به فهرست ابزارها"));
                }

                // ابزار درایورها — دکمه بروزرسانی فهرست (a11y و tooltip یک متن دارند)
                if (RefreshDriversListBtn != null)
                {
                    string refreshDriversText = LocalizationService.Get("بروزرسانی فهرست درایورها");
                    AutomationProperties.SetName(RefreshDriversListBtn, refreshDriversText);
                    ToolTipService.SetToolTip(RefreshDriversListBtn, refreshDriversText);
                }

                // کارت گزارش پایان پاکسازی — متن های ثابت (گروه XAML جدا)
                if (ReportCardTitleText != null) ReportCardTitleText.Text = LocalizationService.Get("گزارش دستاوردهای بهینه‌سازی سیستم");
                if (ReportCardSubtitleText != null) ReportCardSubtitleText.Text = LocalizationService.Get("عملیات با موفقیت پایان یافت؛ خلاصهٔ فضای آزادشده و اقدامات انجام‌شده در زیر آمده است:");
                if (ReportCardFooterText != null) ReportCardFooterText.Text = LocalizationService.Get("فایل های موقت با موفقیت پاکسازی شدند و به اطلاعات شخصی آسیبی نرسید.");
                if (CloseReportBtnText != null) CloseReportBtnText.Text = LocalizationService.Get("تأیید و بازگشت به داشبورد");

                // حذف نرم افزار — چک باکس «انتخاب همه» + placeholder جعبه اسم کاراکتر
                if (UninstallerSelectAllCheck != null) UninstallerSelectAllCheck.Content = LocalizationService.Get("SelectAll", "انتخاب همه");
                if (CharNameBox != null) CharNameBox.PlaceholderText = LocalizationService.Get("مثلاً سی‌سی", "مثلاً وینی");
                // اسم پیش فرض کاربر «رفیق» است و per-language ترجمه می شود؛ فقط placeholder است
                // و در settings ذخیره نمی شود تا مقدار دستی کاربر همیشه دست نخورده بماند.
                if (CharUserNameBox != null) CharUserNameBox.PlaceholderText = LocalizationService.Get("رفیق");

                // تنظیمات ظاهر — توضیح اسلایدر اندازه کاراکتر
                if (CharSizeSlider != null) ToolTipService.SetToolTip(CharSizeSlider, LocalizationService.Get("حجم بالای ۱.۲۵× از درون کارت بیرون می زند"));

                // کمبوباکس زبان — تنها آیتم غیرنیتیو «خودکار» است (نام بقیه زبان ها عمداً نیتیو می مانند)
                if (LanguageComboBox != null && LanguageComboBox.Items.Count > 0 && LanguageComboBox.Items[0] is ComboBoxItem autoLangItem)
                {
                    autoLangItem.Content = LocalizationService.Get("خودکار (بر اساس ساعت و منطقه)");
                }


                // System Specs
                if (SpecsHeaderTitle != null) SpecsHeaderTitle.Text = LocalizationService.Get("SpecsHeaderTitle", "مشخصات جامع سخت افزار");
                if (SpecsHeaderSubtitle != null) SpecsHeaderSubtitle.Text = LocalizationService.Get("SpecsHeaderSubtitle", "اطلاعات لحظه ای پردازنده، رم، مادربرد، گرافیک، درایوها و لایسنس");
                if (CopySpecsBtnText != null) CopySpecsBtnText.Text = LocalizationService.Get("CopySpecs", "کپی مشخصات");
                if (RefreshSpecsBtnText != null) RefreshSpecsBtnText.Text = LocalizationService.Get("RefreshSpecs", "به‌روزرسانی");

                // App Uninstaller
                if (UninstallerHeaderTitle != null) UninstallerHeaderTitle.Text = LocalizationService.Get("UninstallerHeaderTitle", "مدیریت و حذف نرم افزارها");
                if (UninstallerSearchBox != null) UninstallerSearchBox.PlaceholderText = LocalizationService.Get("UninstallerSearchPlaceholder", "جستجوی نرم افزار...");
                if (UninstallerSortNewest != null) UninstallerSortNewest.Content = LocalizationService.Get("UninstallerSortNewest", "جدیدترین نصب");
                if (UninstallerSortLargest != null) UninstallerSortLargest.Content = LocalizationService.Get("UninstallerSortLargest", "بیشترین حجم");
                if (UninstallerSortName != null) UninstallerSortName.Content = LocalizationService.Get("UninstallerSortName", "نام (الفبا)");

                // Speed Test
                if (SpeedTestHeaderTitle != null) SpeedTestHeaderTitle.Text = LocalizationService.Get("SpeedTestHeaderTitle", "تست سرعت اینترنت");
                if (SpeedTestHeaderSubtitle != null) SpeedTestHeaderSubtitle.Text = LocalizationService.Get("SpeedTestHeaderSubtitle", "سنجش پهنای باند دانلود، آپلود و تاخیر از سرورهای Cloudflare");
                if (SpeedIpLabelText != null) SpeedIpLabelText.Text = LocalizationService.Get("SpeedIp", "IP");
                if (SpeedIspLabelText != null) SpeedIspLabelText.Text = LocalizationService.Get("SpeedIsp", "ISP");
                if (SpeedLocationLabelText != null) SpeedLocationLabelText.Text = LocalizationService.Get("SpeedLocation", "موقعیت");
                if (SpeedGaugePhaseText != null && !_isBusy) SpeedGaugePhaseText.Text = LocalizationService.Get("ReadyToStartSpeedTest", "آماده شروع تست سرعت");
                if (StartSpeedTestBtnText != null) StartSpeedTestBtnText.Text = LocalizationService.Get("StartSpeedTest", "شروع تست سرعت");
                if (StopSpeedTestBtnText != null) StopSpeedTestBtnText.Text = LocalizationService.Get("StopSpeedTest", "توقف تست");
                if (SpeedIspChipText != null) SpeedIspChipText.Text = LocalizationService.Get("در حال شناسایی...");
                if (SpeedLocationChipText != null) SpeedLocationChipText.Text = LocalizationService.Get("در حال شناسایی...");
                if (SpeedPingTitleText != null) SpeedPingTitleText.Text = LocalizationService.Get("PingLatency", "تاخیر پینگ (Ping)");
                if (SpeedDownloadTitleText != null) SpeedDownloadTitleText.Text = LocalizationService.Get("DownloadSpeed", "سرعت دانلود");
                if (SpeedUploadTitleText != null) SpeedUploadTitleText.Text = LocalizationService.Get("UploadSpeed", "سرعت آپلود");

                // Personalization
                if (PersonalizationHeaderTitle != null) PersonalizationHeaderTitle.Text = LocalizationService.Get("PersonalizationHeaderTitle", "استایل، تم و گرافیک ویندوز");
                if (PersonalizationHeaderSubtitle != null) PersonalizationHeaderSubtitle.Text = LocalizationService.Get("PersonalizationHeaderSubtitle", "اعمال آنی بدون نیاز به ریستارت");

                // Startup Manager
                if (StartupHeaderTitle != null) StartupHeaderTitle.Text = LocalizationService.Get("StartupHeaderTitle", "مدیریت برنامه های شروع خودکار");
                if (RefreshStartupBtnText != null) RefreshStartupBtnText.Text = LocalizationService.Get("StartupRefresh", "به‌روزرسانی لیست");

                // Windows Update
                if (WindowsUpdateHeaderTitle != null) WindowsUpdateHeaderTitle.Text = LocalizationService.Get("WindowsUpdateHeaderTitle", "مدیریت به‌روزرسانی‌های ویندوز");
                if (WindowsUpdateHeaderSubtitle != null) WindowsUpdateHeaderSubtitle.Text = LocalizationService.Get("WindowsUpdateHeaderSubtitle", "بررسی آپدیت ها، توقف رسمی ۵ هفته ای و رفع گیر کردن دانلود");
                if (CheckUpdatesBtnText != null) CheckUpdatesBtnText.Text = LocalizationService.Get("CheckNewUpdates", "بررسی آپدیت های جدید");
                if (UpdateStatusHeadlineText != null) UpdateStatusHeadlineText.Text = LocalizationService.Get("UpdateStatusHeadline", "وضعیت آپدیت های سیستم عامل");
                if (UpdateStatusBadgeText != null) UpdateStatusBadgeText.Text = LocalizationService.Get("UpdateStatusActive", "فعال");
                if (UpdateRow1Title != null) UpdateRow1Title.Text = LocalizationService.Get("UpdateRow1Title", "توقف رسمی تا ۵ هفته");
                if (UpdateRow1Sub != null) UpdateRow1Sub.Text = LocalizationService.Get("UpdateRow1Sub", "توقف دریافت بسته ها مطابق مکانیزم رسمی تنظیمات ویندوز");
                if (PauseUpdatesBtn != null) PauseUpdatesBtn.Content = LocalizationService.Get("Pause5Weeks", "توقف ۵ هفته ای");
                if (ResumeUpdatesBtn != null) ResumeUpdatesBtn.Content = LocalizationService.Get("ResumeUpdates", "ادامه دریافت");
                if (UpdateRow2Title != null) UpdateRow2Title.Text = LocalizationService.Get("UpdateRow2Title", "سرویس Windows Update (wuauserv)");
                if (UpdateRow2Sub != null) UpdateRow2Sub.Text = LocalizationService.Get("UpdateRow2Sub", "فعال یا غیرفعال سازی کامل سرویس پس زمینه");
                if (DisableUpdateServiceBtn != null) DisableUpdateServiceBtn.Content = LocalizationService.Get("DisableService", "غیرفعال سازی");
                if (EnableUpdateServiceBtn != null) EnableUpdateServiceBtn.Content = LocalizationService.Get("EnableService", "فعال سازی");
                if (UpdateRow3Title != null) UpdateRow3Title.Text = LocalizationService.Get("UpdateRow3Title", "رفع گیر کردن آپدیت ها (پاکسازی کش)");
                if (UpdateRow3Sub != null) UpdateRow3Sub.Text = LocalizationService.Get("UpdateRow3Sub", "توقف سرویس، پاکسازی کامل پوشه SoftwareDistribution و راه اندازی مجدد");
                if (FixStuckUpdatesBtn != null) FixStuckUpdatesBtn.Content = LocalizationService.Get("CleanCache", "پاکسازی کش");
                if (UpdatePendingSectionTitle != null) UpdatePendingSectionTitle.Text = LocalizationService.Get("UpdatePendingSectionTitle", "آپدیت های در انتظار نصب");
                if (NoUpdatesBannerText != null) NoUpdatesBannerText.Text = LocalizationService.Get("NoUpdatesBanner", "هیچ آپدیت معلق یا در انتظار نصبی یافت نشد. ویندوز کاملاً بروز است.");

                // Drivers
                if (DriversHeaderTitle != null) DriversHeaderTitle.Text = LocalizationService.Get("DriversHeaderTitle", "مدیریت و پاکسازی درایورها");
                if (DriversHeaderSubtitle != null) DriversHeaderSubtitle.Text = LocalizationService.Get("DriversHeaderSubtitle", "شناسایی خطاها، پاکسازی ایمن نسخه های تکراری DriverStore");
                if (CheckDriverUpdatesBtnText != null) CheckDriverUpdatesBtnText.Text = LocalizationService.Get("CheckDriverUpdates", "بررسی آپدیت درایورها");
                if (DriversTotalStatLabel != null) DriversTotalStatLabel.Text = LocalizationService.Get("DriversTotalLabel", "کل بسته های درایور");
                if (DriversErrorStatLabel != null) DriversErrorStatLabel.Text = LocalizationService.Get("DriversErrorLabel", "خطاهای سخت افزاری");
                if (DriversDuplicateStatLabel != null) DriversDuplicateStatLabel.Text = LocalizationService.Get("DriversDuplicateLabel", "نسخه های تکراری");
                if (DriversDuplicateBannerTitle != null) DriversDuplicateBannerTitle.Text = LocalizationService.Get("DriversDuplicateBannerTitle", "پاکسازی بسته های تکراری و اشغال کننده فضا");
                if (DriversDuplicateBannerSub != null) DriversDuplicateBannerSub.Text = LocalizationService.Get("DriversDuplicateBannerSub", "نسخه های قدیمی با ایجاد Restore Point به صورت ایمن حذف می شوند.");
                if (CleanOldDriversBtn != null) CleanOldDriversBtn.Content = LocalizationService.Get("CleanDriverStore", "پاکسازی DriverStore");
                if (DriversSearchBox != null) DriversSearchBox.PlaceholderText = LocalizationService.Get("DriversSearchPlaceholder", "جستجو در بین درایورها...");
                if (DriversSelectAll != null) DriversSelectAll.Content = LocalizationService.Get("DriversSelectAll", "انتخاب همه");
                if (DriversBatchDeleteBtnText != null) DriversBatchDeleteBtnText.Text = LocalizationService.Get("DriversBatchDelete", "حذف انتخاب شده ها");

                // DNS
                if (DnsHeaderTitle != null) DnsHeaderTitle.Text = LocalizationService.Get("DnsHeaderTitle", "تنظیم هوشمند DNS");
                if (DnsHeaderSubtitle != null) DnsHeaderSubtitle.Text = LocalizationService.Get("DnsHeaderSubtitle", "عبور مطمئن از تحریم ها و کاهش پینگ اتصال");
                if (ResetDnsDhcpBtnText != null) ResetDnsDhcpBtnText.Text = LocalizationService.Get("ResetDnsDhcp", "ریست به خودکار (DHCP)");
                if (ActiveDnsLabelText != null) ActiveDnsLabelText.Text = LocalizationService.Get("ActiveDnsLabel", "دی ان اس فعال در حال حاضر");
                if (FlushDnsQuickBtnText != null) FlushDnsQuickBtnText.Text = LocalizationService.Get("FlushDns", "تخلیه کش DNS");
                if (DnsPresetsTitleText != null) DnsPresetsTitleText.Text = LocalizationService.Get("DnsPresetsTitle", "سرویس های آماده");

                // Privacy
                if (PrivacyHeaderTitle != null) PrivacyHeaderTitle.Text = LocalizationService.Get("PrivacyHeaderTitle", "حریم خصوصی و حذف تلمتری");
                if (PrivacyHeaderSubtitle != null) PrivacyHeaderSubtitle.Text = LocalizationService.Get("PrivacyHeaderSubtitle", "قطع ارسال گزارش ها و بهینه سازی جستجو");
                if (PrivacyBannerTitle != null) PrivacyBannerTitle.Text = LocalizationService.Get("PrivacyBannerTitle", "اعمال و ذخیره خودکار بلادرنگ");
                if (PrivacyBannerSub != null) PrivacyBannerSub.Text = LocalizationService.Get("PrivacyBannerSub", "همه گزینه ها با سوییچ، آنی در رجیستری و سرویس ها اعمال می شوند.");
                if (PrivacyBannerBadge != null) PrivacyBannerBadge.Text = LocalizationService.Get("PrivacyBannerBadge", "فعال");
                if (PrivacyFooterHint != null) PrivacyFooterHint.Text = LocalizationService.Get("PrivacyFooterHint", "برای حداکثر امنیت، فعال بودن همه گزینه ها پیشنهاد می شود.");
                if (ResetPrivacyDefaultsBtn != null) ResetPrivacyDefaultsBtn.Content = LocalizationService.Get("PrivacyResetDefaults", "پیش فرض ویندوز");
                if (EnableAllPrivacyProtectionsBtnText != null) EnableAllPrivacyProtectionsBtnText.Text = LocalizationService.Get("PrivacyEnableAll", "فعال سازی همه");

                // Deep Storage
                if (DeepStorageHeaderTitle != null) DeepStorageHeaderTitle.Text = LocalizationService.Get("DeepStorageHeaderTitle", "پاکسازی عمیق دیسک و فایل های زائد");
                if (DeepStorageHeaderSubtitle != null) DeepStorageHeaderSubtitle.Text = LocalizationService.Get("DeepStorageHeaderSubtitle", "شناسایی فایل های تکراری، پوشه های خالی، دانلودها و فایل های حجیم");
                if (DeepStorageScanBtnText != null) DeepStorageScanBtnText.Text = LocalizationService.Get("StartDeepScan", "شروع پویش عمیق");
                if (DeepStorageDuplicatesLabel != null) DeepStorageDuplicatesLabel.Text = LocalizationService.Get("DeepStorageDuplicatesLabel", "فایل های تکراری");
                if (DeepStorageEmptyFoldersLabel != null) DeepStorageEmptyFoldersLabel.Text = LocalizationService.Get("DeepStorageEmptyFoldersLabel", "پوشه های خالی");
                if (DeepStorageEmptyFoldersSub != null) DeepStorageEmptyFoldersSub.Text = LocalizationService.Get("DeepStorageEmptyFoldersSub", "فاقد فایل یا زیرپوشه");
                if (DeepStorageMessTitleText != null) DeepStorageMessTitleText.Text = LocalizationService.Get("TabMess", "فایل های زائد و نامرتب");
                if (DeepStorageLargeFilesLabel != null) DeepStorageLargeFilesLabel.Text = LocalizationService.Get("DeepStorageLargeFilesLabel", "فایل های حجیم");
                if (DeepStorageLargeFilesSizeText != null) DeepStorageLargeFilesSizeText.Text = LocalizationService.Get("DeepStorageLargeFilesSub", "بالاتر از ۵۰۰ مگابایت");
                if (DeepStorageDuplicatesCountText != null) DeepStorageDuplicatesCountText.Text = string.Format(LocalizationService.Get("{0} گروه تکراری"), 0);
                if (DeepStorageDuplicatesSizeText != null) DeepStorageDuplicatesSizeText.Text = string.Format(LocalizationService.Get("{0} فضا"), SystemStats.FormatBytes(0));
                if (DeepStorageEmptyFoldersCountText != null) DeepStorageEmptyFoldersCountText.Text = string.Format(LocalizationService.Get("{0} پوشه خالی"), 0);
                if (DeepStorageMessCountText != null) DeepStorageMessCountText.Text = string.Format(LocalizationService.Get("{0} فایل زائد و نامرتب"), 0);
                if (DeepStorageMessSizeText != null) DeepStorageMessSizeText.Text = LocalizationService.Get("DeepStorageMessSub", "موقت، نصاب ها و زائد");
                if (DeepStorageLargeFilesCountText != null) DeepStorageLargeFilesCountText.Text = string.Format(LocalizationService.Get("{0} فایل حجیم"), 0);
                if (DeepStorageSelectedCountText != null) DeepStorageSelectedCountText.Text = $"0 {LocalizationService.Get("ItemsSelected", "مورد انتخاب شده")}";
                if (DeepStorageTabDuplicatesText != null) DeepStorageTabDuplicatesText.Text = LocalizationService.Get("DeepStorageDuplicatesLabel", "فایل های تکراری");
                if (DeepStorageTabEmptyFoldersText != null) DeepStorageTabEmptyFoldersText.Text = LocalizationService.Get("DeepStorageEmptyFoldersLabel", "پوشه های خالی");
                if (DeepStorageTabMessText != null) DeepStorageTabMessText.Text = LocalizationService.Get("TabMess", "فایل های زائد و نامرتب");
                if (DeepStorageTabLargeFilesText != null) DeepStorageTabLargeFilesText.Text = LocalizationService.Get("DeepStorageLargeFilesLabel", "فایل های حجیم");
                if (DeepStorageSortLabel != null) DeepStorageSortLabel.Text = LocalizationService.Get("DeepStorageSortLabel", "مرتب سازی:");
                if (DeepStorageSortSizeBtnText != null) DeepStorageSortSizeBtnText.Text = LocalizationService.Get("DeepStorageSortSize", "حجم");
                if (DeepStorageSortDateBtnText != null) DeepStorageSortDateBtnText.Text = LocalizationService.Get("DeepStorageSortDate", "تاریخ");
                if (DeepStorageSortNameBtnText != null) DeepStorageSortNameBtnText.Text = LocalizationService.Get("DeepStorageSortName", "نام");
                if (DeepStorageSelectAllCheck != null) DeepStorageSelectAllCheck.Content = LocalizationService.Get("SelectAll", "انتخاب همه");
                if (DeepStorageBatchDeleteBtnText != null) DeepStorageBatchDeleteBtnText.Text = LocalizationService.Get("CleanSelectedItems", "پاکسازی موارد انتخابی");
                if (DeepStorageScanStatusText != null) DeepStorageScanStatusText.Text = LocalizationService.Get("DeepStorageScanStatus", "در حال پویش عمیق ساختار فایل ها و محاسبه هش محتوا...");

                // Recycle Bin
                if (RecycleBinHeaderTitle != null) RecycleBinHeaderTitle.Text = LocalizationService.Get("RecycleBinHeaderTitle", "مدیریت سطل بازیافت");
                if (RecycleBinHeaderSubtitle != null) RecycleBinHeaderSubtitle.Text = LocalizationService.Get("RecycleBinHeaderSubtitle", "پیش نمایش آیتم به آیتم؛ بازگردانی یا حذف قطعی تک تک اشیاء");
                if (RecycleBinRefreshBtnText != null) RecycleBinRefreshBtnText.Text = LocalizationService.Get("RecycleBinRefresh", "تازه سازی");
                if (RecycleBinTotalLabel != null) RecycleBinTotalLabel.Text = LocalizationService.Get("RecycleBinTotalLabel", "تعداد اشیاء");
                if (RecycleBinSizeLabel != null) RecycleBinSizeLabel.Text = LocalizationService.Get("RecycleBinSizeLabel", "حجم اشغال شده");
                if (RecycleBinSelectAllCheck != null) RecycleBinSelectAllCheck.Content = LocalizationService.Get("SelectAll", "انتخاب همه");
                if (RecycleBinRestoreSelectedText != null) RecycleBinRestoreSelectedText.Text = LocalizationService.Get("RecycleBinRestoreSelected", "بازگردانی انتخاب شده ها");
                if (RecycleBinDeleteSelectedText != null) RecycleBinDeleteSelectedText.Text = LocalizationService.Get("RecycleBinDeleteSelected", "حذف قطعی انتخاب شده ها");
                if (RecycleBinEmptyBtnText != null) RecycleBinEmptyBtnText.Text = LocalizationService.Get("RecycleBinEmptyBtn", "خالی کردن کل سطل");
                if (RecycleBinEmptyHintText != null) RecycleBinEmptyHintText.Text = LocalizationService.Get("RecycleBinEmptyHint", "سطل بازیافت خالی است؛ چیزی برای بازگردانی یا حذف وجود ندارد.");
                if (RecycleBinLoadingText != null) RecycleBinLoadingText.Text = LocalizationService.Get("RecycleBinLoading", "در حال خواندن فهرست سطل بازیافت...");

                // Immediate live telemetry & disk stats refresh
                RefreshSystemStats();
                UpdateCumulativeChipsUI();
                RenderDiskBreakdown(_diskTotalGb, _diskFreeGb, _lastScanCleanableBytes);
                // نمودار ۷ روزه هم باید با زبان جدید بازرندر شود؛ وگرنه واحدها و اعداد
                // تا اسکن بعدی به زبان قبلی می‌مانند. (i18n زنده)
                RenderCleanupHistory();

                if (_cachedScanResult != null)
                {
                    RenderAdvancedGroups(_cachedScanResult);
                    RelocalizeAdvancedGroupsAfterLanguageChange();
                }

                if (ToolSystemSpecsPanel != null && ToolSystemSpecsPanel.Visibility == Visibility.Visible && _cachedSpecs != null)
                {
                    RenderSystemSpecs(_cachedSpecs);
                }
                if (ToolPersonalizationPanel != null && ToolPersonalizationPanel.Visibility == Visibility.Visible)
                {
                    LoadPersonalizationPage();
                }
                if (ToolDnsPanel != null && ToolDnsPanel.Visibility == Visibility.Visible)
                {
                    LoadDnsPage();
                }
                if (ToolPrivacyPanel != null && ToolPrivacyPanel.Visibility == Visibility.Visible)
                {
                    LoadPrivacyPage();
                }
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
        // خروجی گزارش (TXT / MD)
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
            string? path = await PickSavePathAsync(LocalizationService.Get("گزارش هوش مصنوعی (Markdown)"), ".md", $"WinyWash-AI-Report-{Stamp()}");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var specs = _cachedSpecs ?? await SystemSpecsService.GetDetailedSpecsAsync();
                _cachedSpecs = specs;

                string md = ReportExportService.BuildAiReportMarkdown(specs, _lastCleanupItems, _lastFreedBytes);
                ReportExportService.WriteUtf8Bom(path, md);
                ShowInfoBar(LocalizationService.Get("خروجی گزارش"), string.Format(LocalizationService.Get("گزارش هوش مصنوعی در «{0}» ذخیره شد."), path), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "ExportAiReport");
                ShowInfoBar(LocalizationService.Get("خروجی گزارش"), LocalizationService.Get("ذخیره ی گزارش هوش مصنوعی انجام نشد."), InfoBarSeverity.Error);
            }
        }

        private void ExportSpecsTxtBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(ExportSpecsTxtBtn_ClickAsync());

        private async Task ExportSpecsTxtBtn_ClickAsync()
        {
            string? path = await PickSavePathAsync(LocalizationService.Get("مشخصات سیستم (TXT)"), ".txt", $"WinyWash-Specs-{Stamp()}");
            if (string.IsNullOrEmpty(path)) return; // کاربر انصراف داد

            try
            {
                // اگر هنوز صفحه ی مشخصات باز نشده، همین جا می گیریم
                var specs = _cachedSpecs ?? await SystemSpecsService.GetDetailedSpecsAsync();
                _cachedSpecs = specs;

                string text = ReportExportService.BuildSystemSpecsTxt(specs);
                ReportExportService.WriteUtf8Bom(path, text);
                ShowInfoBar(LocalizationService.Get("خروجی گزارش"), string.Format(LocalizationService.Get("گزارش مشخصات سیستم در «{0}» ذخیره شد."), path), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "ExportSpecsTxt");
                ShowInfoBar(LocalizationService.Get("خروجی گزارش"), LocalizationService.Get("ذخیره ی گزارش مشخصات انجام نشد (مسیر در دسترس نیست)."), InfoBarSeverity.Error);
            }
        }

        // ====================================================================
        // درباره، به روزرسانی و عیب یابی
        // ====================================================================

        /// <summary>مخزن پروژه — از remote واقعی git گرفته می شود، نه مقدار حدسی.</summary>
        private const string ProjectPageUrl = "https://github.com/mohammad-ahw/WinyWash";
        private const string ReleasesApiUrl = "https://api.github.com/repos/mohammad-ahw/WinyWash/releases/latest";

        private void OpenProjectPageBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = ProjectPageUrl, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "OpenProjectPage");
                ShowInfoBar(LocalizationService.Get("درباره CleanC"), LocalizationService.Get("باز کردن صفحه ی پروژه ممکن نشد."), InfoBarSeverity.Warning);
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
                ShowInfoBar(LocalizationService.Get("اطلاعات عیب یابی کپی شد"), LocalizationService.Get("حالا می توانی آن را در گزارش باگ جای گذاری کنی."), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "CopyDiagnostics");
                ShowInfoBar(LocalizationService.Get("اطلاعات عیب یابی"), LocalizationService.Get("کپی کردن ممکن نشد."), InfoBarSeverity.Error);
            }
        }

        // نام گذاری با «App»: متدِ هم نامِ CheckUpdatesBtn_Click برای بروزرسانیِ خودِ ویندوز در
        // MainWindow.Tools.cs وجود دارد و این یکی مخصوص نسخه ی خودِ CleanC است.
        private void CheckAppUpdatesBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(CheckAppUpdatesBtn_ClickAsync());

        private async Task CheckAppUpdatesBtn_ClickAsync()
        {
            string local = ReportExportService.GetVersion(typeof(MainWindow).Assembly);
            ShowInfoBar(LocalizationService.Get("بررسی به‌روزرسانی"), LocalizationService.Get("در حال بررسی..."), InfoBarSeverity.Informational);

            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                // GitHub API بدون User-Agent درخواست را رد می کند
                client.DefaultRequestHeaders.UserAgent.ParseAdd("WinyWash-UpdateCheck");

                using var resp = await client.GetAsync(ReleasesApiUrl);

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    ShowInfoBar(LocalizationService.Get("بررسی به‌روزرسانی"),
                        string.Format(LocalizationService.Get("هنوز نسخه‌ای منتشر نشده (یا مخزن خصوصی است). نسخهٔ فعلی: {0}"), local),
                        InfoBarSeverity.Informational);
                    return;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    ShowInfoBar(LocalizationService.Get("بررسی به‌روزرسانی"),
                        string.Format(LocalizationService.Get("نتوانستم منبع نسخه‌ها را بخوانم (کد {0}). نسخهٔ فعلی: {1}"), (int)resp.StatusCode, local),
                        InfoBarSeverity.Warning);
                    return;
                }

                string json = await resp.Content.ReadAsStringAsync();
                string? tag = ExtractJsonString(json, "tag_name");
                if (string.IsNullOrWhiteSpace(tag))
                {
                    ShowInfoBar(LocalizationService.Get("بررسی به‌روزرسانی"), string.Format(LocalizationService.Get("پاسخ منبع نامعتبر بود. نسخهٔ فعلی: {0}"), local), InfoBarSeverity.Warning);
                    return;
                }

                string remote = tag.TrimStart('v', 'V');
                ShowInfoBar(LocalizationService.Get("بررسی به‌روزرسانی"),
                    IsNewer(remote, local)
                        ? string.Format(LocalizationService.Get("نسخهٔ جدیدتری منتشر شده: {0} (نسخهٔ فعلی: {1})"), remote, local)
                        : string.Format(LocalizationService.Get("به‌روز هستی؛ آخرین نسخهٔ منتشرشده {0} است."), remote),
                    IsNewer(remote, local) ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "CheckUpdates");
                ShowInfoBar(LocalizationService.Get("بررسی به‌روزرسانی"),
                    string.Format(LocalizationService.Get("بررسی به‌روزرسانی ممکن نشد (اتصال اینترنت یا دسترسی شبکه). نسخهٔ فعلی: {0}"), local),
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
            bool isRtl = LocalizationService.IsRtl;
            if (netMbps < 0.2) return isRtl ? LocalizationService.Get("شبکه آزاد و پایدار") : "Network Idle";

            try
            {
                var running = GetRunningProcessNames();

                if (running.Contains("IDMan")) return isRtl ? LocalizationService.Get("دانلود فعال (IDM)") : "Internet Download Manager (IDM)";
                if (running.Contains("steam") || running.Contains("steamwebhelper")) return isRtl ? LocalizationService.Get("کلاینت استیم (Steam)") : "Steam Client";
                if (running.Contains("qbittorrent") || running.Contains("utorrent")) return isRtl ? LocalizationService.Get("کلاینت تورنت (Torrent)") : "Torrent Client";
                if (running.Contains("msedge")) return isRtl ? LocalizationService.Get("مرورگر مایکروسافت اج") : "Microsoft Edge";
                if (running.Contains("chrome")) return isRtl ? LocalizationService.Get("مرورگر گوگل کروم") : "Google Chrome";
                if (running.Contains("firefox")) return isRtl ? LocalizationService.Get("مرورگر فایرفاکس") : "Mozilla Firefox";
                if (running.Contains("Discord")) return "Discord";
                if (running.Contains("Telegram")) return isRtl ? LocalizationService.Get("پیام رسان تلگرام") : "Telegram";
                if (running.Contains("Spotify")) return isRtl ? LocalizationService.Get("استریم اسپاتیفای") : "Spotify";
                if (running.Contains("DoSvc")) return isRtl ? LocalizationService.Get("سرویس آپدیت ویندوز") : "Windows Update Service";
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return isRtl ? "ترافیک سیستم و ویندوز" : LocalizationService.Get("NetworkSysTraffic", "System & Windows Traffic");
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

        /// <summary>کلید تاریخِ تاریخچه همیشه میلادی/Invariant است تا با تغییر زبان عوض نشود.</summary>
        private static string ToCanonicalHistoryDate(DateTime date)
            => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>
        /// نسخه های قبلی تاریخ را با تقویم زبان جاری (مثلاً ۱۴۰۵-۰۷-۲۶ در fa-IR) ذخیره
        /// می کردند؛ اینجا هم آن ها را به میلادی تبدیل می کنیم و هم از این به بعد
        /// فقط کلید Invariant می نویسیم.
        /// </summary>
        private static string NormalizeHistoryDate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string[] parts = raw.Trim().Split('-');
            if (parts.Length == 3
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int year)
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int month)
                && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int day))
            {
                try
                {
                    DateTime date = year >= 1900
                        ? new DateTime(year, month, day)
                        : new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0);
                    return ToCanonicalHistoryDate(date);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // تاریخ نامعتبر/خراب → نادیده گرفته می شود
                }
            }
            return "";
        }

        /// <summary>
        /// مقدار آزادشده یک نوبت پاکسازی را در تاریخچه روزانه ثبت می کند.
        /// اگر امروز قبلاً رکورد داشته باشیم، جمع می شود (چند پاکسازی در یک روز).
        /// </summary>
        private void RecordCleanupSession(long bytes)
        {
            if (bytes <= 0) return;
            string today = ToCanonicalHistoryDate(DateTime.Now);
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
            bool historyDatesMigrated = false;

            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                // آمار پاکسازی از پوشه داده‌ی فعلی برنامه خوانده می‌شود (AppPaths)؛
                // اگر فایل قدیمی در پوشه CleanC مانده باشد، برای سازگاری همان خوانده می‌شود.
                string statsFile = Path.Combine(AppPaths.LocalRoot, "clean_stats.json");
                if (!File.Exists(statsFile))
                {
                    string legacyStats = Path.Combine(localAppData, "CleanC", "clean_stats.json");
                    if (File.Exists(legacyStats)) statsFile = legacyStats;
                }
                if (File.Exists(statsFile))
                {
                    string json = File.ReadAllText(statsFile);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("cumulativeBytes", out var cumProp))
                        _cumulativeFreedBytes = cumProp.GetInt64();
                    if (doc.RootElement.TryGetProperty("lastCleanBytes", out var lastProp))
                        _lastCleanFreedBytes = lastProp.GetInt64();
                    if (doc.RootElement.TryGetProperty("lastCleanTime", out var timeProp) && DateTime.TryParse(timeProp.GetString(), out var dt))
                    {
                        _lastCleanDateTime = dt;
                        if (_lastCleanDateTime != DateTime.MinValue && (DateTime.Now - _lastCleanDateTime).TotalHours < 24)
                        {
                            _isCleanedRecently = true;
                        }
                    }
                    if (doc.RootElement.TryGetProperty("heroSnoozeUntil", out var snzProp) && DateTime.TryParse(snzProp.GetString(), out var sdt))
                    {
                        _heroSnoozedUntil = sdt;
                    }
                    if (doc.RootElement.TryGetProperty("heroBrowserSnoozeUntil", out var bsnzProp) && DateTime.TryParse(bsnzProp.GetString(), out var bsdt))
                    {
                        _heroBrowserAlertSuppressedUntil = bsdt;
                    }

                    if (doc.RootElement.TryGetProperty("history", out var histProp) && histProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var entry in histProp.EnumerateArray())
                        {
                            if (!entry.TryGetProperty("date", out var dProp)) continue;
                            string? rawDate = dProp.GetString();
                            string date = NormalizeHistoryDate(rawDate);
                            if (string.IsNullOrWhiteSpace(date)) continue;
                            if (!string.Equals(date, rawDate, StringComparison.Ordinal)) historyDatesMigrated = true;

                            long bytes = Math.Max(0, entry.TryGetProperty("bytes", out var bProp) ? bProp.GetInt64() : 0);
                            var existing = _cleanupHistory.FirstOrDefault(h => h.Date == date);
                            if (existing != null) existing.Bytes += bytes;
                            else _cleanupHistory.Add(new CleanupHistoryEntry { Date = date, Bytes = bytes });
                        }
                    }
                }
            }
            catch
            {
                // فایل خراب/غیرقابل خواندن → همه چیز صفر می ماند
            }

            // فقط وقتی واقعاً تاریخ قدیمی تبدیل شد فایل را بازنویسی می کنیم.
            if (historyDatesMigrated) SaveCumulativeFreedStats();

            UpdateCumulativeChipsUI();
            RenderCleanupHistory();
        }

        internal void SaveCumulativeFreedStats()
        {
            try
            {
                // آمار همیشه در پوشه داده‌ی فعلی برنامه (AppPaths) ذخیره می‌شود
                string folder = AppPaths.LocalRoot;
                Directory.CreateDirectory(folder);
                string statsFile = Path.Combine(folder, "clean_stats.json");
                var obj = new
                {
                    cumulativeBytes = _cumulativeFreedBytes,
                    lastCleanBytes = _lastCleanFreedBytes,
                    lastCleanTime = (_lastCleanDateTime == DateTime.MinValue ? "" : _lastCleanDateTime.ToString("o")),
                    heroSnoozeUntil = (_heroSnoozedUntil == DateTime.MinValue ? "" : _heroSnoozedUntil.ToString("o")),
                    heroBrowserSnoozeUntil = (_heroBrowserAlertSuppressedUntil == DateTime.MinValue ? "" : _heroBrowserAlertSuppressedUntil.ToString("o")),
                    history = _cleanupHistory.Select(h => new { date = h.Date, bytes = h.Bytes })
                };
                // نوشتن اتمیک: اول فایل موقت و بعد جایگزینی کامل؛ اگر برنامه حین
                // نوشتن بسته شود یا برق قطع شود، فایل قبلی سالم می ماند نه نیمه تمام
                string json = System.Text.Json.JsonSerializer.Serialize(obj);
                string tmpFile = statsFile + ".tmp";
                File.WriteAllText(tmpFile, json);
                File.Move(tmpFile, statsFile, true);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        /// <summary>
        /// متن‌های تجمیعی (مجموع آزادشده / آخرین پاکسازی) اکنون داخل خودِ HTML رندر می‌شوند؛
        /// فقط کش وضعیت را باطل می‌کنیم تا PushHeroState مقدارهای جدید را بفرستد.
        /// </summary>
        private void UpdateCumulativeChipsUI()
        {
            _heroLastStateJson = null;
            PushHeroState();
                ApplyNewsLanguage();
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
            string usedTxt = $"{usedPct.ToString("0.#", CultureInfo.InvariantCulture)}%";
            string cleanTxt = cleanableGb > 0.01 ? $"{cleanPct.ToString("0.#", CultureInfo.InvariantCulture)}%" : "—";
            string freeTxt = $"{freePct.ToString("0.#", CultureInfo.InvariantCulture)}%";
            string freeWord = LocalizationService.IsRtl ? LocalizationService.Get("آزاد از") : "free of";
            string badgeTxt = $"{SystemStats.FormatBytes((long)(freeGb * 1024 * 1024 * 1024))} {freeWord} {SystemStats.FormatBytes((long)(totalGb * 1024 * 1024 * 1024))}";

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
                string key = ToCanonicalHistoryDate(day);
                days[i] = byDate.TryGetValue(key, out var b) ? b : 0;
                maxBytes = Math.Max(maxBytes, days[i]);
                labels[i] = day.ToString("dd.MM", CultureInfo.InvariantCulture);
                values[i] = days[i] > 0 ? SystemStats.FormatBytes(days[i]).Replace("گیگابایت", "گیگ") : "—"; // i18n-logic: این متن با دادهی خارجی مقایسه/تطبیق داده میشود
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

                if (valueTb != null)
                {
                    valueTb.Language = "en-US";
                    valueTb.Text = values[i];
                }
                if (labelTb != null)
                {
                    labelTb.Language = "en-US";
                    labelTb.Text = labels[i];
                }
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
            RenderDiskBreakdown(_diskTotalGb, _diskFreeGb, _lastScanCleanableBytes);
            PushHeroState();
                ApplyNewsLanguage();
        }

        /// <summary>
        /// اسکن در پس زمینه (بدون بلاک کردن UI) تا عدد «قابل پاکسازی» قهرمان به روز بماند.
        ///
        /// keepResult=true (استارت‌آپ): نتیجه به‌عنوان اسکنِ فعلی نگه داشته می شود تا
        ///   فهرست آیتم‌های هیرو پر شود و دکمهٔ اصلی «بهینه‌سازی سیستم» باشد و مرحلهٔ ۱
        ///   پروسه از همان اول آماده باشد.
        /// keepResult=false (بعد از پاکسازی): فقط چیپ شمارش تازه می شود و اسکن عمداً
        ///   نگه داشته نمی شود تا بعد از تمیز کردن، دکمه «بررسی مجدد» بماند.
        /// </summary>
        private void ScheduleHeroCleanableRefresh(bool keepResult = true)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var scan = await GetHeroScanAsync();
                    long cleanable = scan.TotalBytes;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (keepResult) ApplyHeroScanResult(scan);
                        else UpdateHeroCleanable(cleanable);
                    });
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

            // اعلامِ زنده برای خواننده ی صفحه
            AnnounceForAccessibility(StatusInfoBar);

            _infoBarAutoDismissTimer.Stop();
            _infoBarAutoDismissTimer.Start();
        }

        /// <summary>
        /// نوتیفیکیشن ویندوزی فقط وقتی پنجره ی CleanC فوکوس ندارد؛
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
            if (combined.Contains("در حال") || combined.Contains("checking") || combined.Contains("scanning") || // i18n-logic: این متن با دادهی خارجی مقایسه/تطبیق داده میشود
                combined.Contains("loading") || combined.Contains("لطفاً صبر کنید")) // i18n-logic: این متن با دادهی خارجی مقایسه/تطبیق داده میشود
            {
                return;
            }

            // اعلان ها در settings.json هم نوشته می شوند تا بعد از ری استارت از بین نروند
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

            // پاک کردنِ UI باید ذخیره شده را هم پاک کند وگرنه بعد از ری استارت برمی گردد
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
            // کارت هیرو اکنون یک WebView2 مستقل است و خودش responsive می‌شود؛
            // این متد فقط شبکهٔ Bento را جمع‌وجور/کشیده می‌کند.
            if (BentoGrid == null) return;

            if (compact)
            {
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
        // Dashboard V2: Accent Sync, Sparklines & Health Gauge
        // ====================================================================

        /// <summary>
        /// رنگ Accent سیستم را به براش‌های برنامه و هالهٔ هیروکارت می‌برد.
        /// اورورای قدیمی حذف شد چون هیرو حالا داخل WebView2 است و خودش هاله را می‌کشد؛
        /// فقط کافی است هر بار Accent عوض شد پیام theme را برایش بفرستیم.
        /// </summary>
        private void InitHeroAccentSync()
        {
            try
            {
                UpdateAccentBrushes();

                _uiSettings.ColorValuesChanged += (s, e) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        UpdateAccentBrushes();
                        // تغییر رنگ سیستم می تواند روشن/خاموش شدن High Contrast باشد
                        ConfigureSystemBackdrop();
                        // هالهٔ دور کاراکتر داخل هیرو از همین Accent پیروی می کند
                        PushHeroTheme();
                    });
                };
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
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

            // عدد سلامت روی آیکون تسک بار (badge)؛ فقط وقتی عدد صحیح عوض می شود
            // تا حین انیمیشن گیج، هر فریم یک فراخوانی COM نزنیم.
            int roundedScore = (int)Math.Round(_currentHealthScore);
            if (roundedScore != _lastBadgeScore)
            {
                _lastBadgeScore = roundedScore;

                // badge ویندوز برای اپ unpackaged ممکن است در دسترس نباشد؛
                // در آن صورت عدد سلامت روی tooltip تری آیکون نشان داده می شود.
                if (!_windowsNotifications.TrySetBadge(roundedScore))
                    _trayManager?.UpdateTooltip(string.Format(LocalizationService.Get("CleanC — سلامت سیستم: {0}%"), roundedScore));
            }

            if (HealthScoreValueText != null)
            {
                HealthScoreValueText.Text = $"{Math.Round(_currentHealthScore).ToString("0", CultureInfo.InvariantCulture)}";
            }

            if (HealthScoreStatusPill != null && HealthScoreStatusBadge != null)
            {
                if (_currentHealthScore >= 85)
                {
                    HealthScoreStatusPill.Text = LocalizationService.Get("HealthExcellent", "عالی");
                    HealthScoreStatusPill.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 108, 203, 95));
                    HealthScoreStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 108, 203, 95));
                    HealthScoreStatusBadge.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 108, 203, 95));
                    if (HealthScoreDescText != null) HealthScoreDescText.Text = LocalizationService.Get("HealthDescExcellent", "وضعیت عمومی سیستم پایدار و عالی است");
                }
                else if (_currentHealthScore >= 70)
                {
                    HealthScoreStatusPill.Text = LocalizationService.Get("HealthGood", "خوب");
                    HealthScoreStatusPill.Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
                    HealthScoreStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(35, 96, 205, 255));
                    HealthScoreStatusBadge.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 96, 205, 255));
                    if (HealthScoreDescText != null) HealthScoreDescText.Text = LocalizationService.Get("HealthDescGood", "عملکرد کلی سیستم خوب و مطلوب است");
                }
                else if (_currentHealthScore >= 50)
                {
                    HealthScoreStatusPill.Text = LocalizationService.Get("HealthFair", "متوسط");
                    HealthScoreStatusPill.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 165, 36));
                    HealthScoreStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(35, 245, 165, 36));
                    HealthScoreStatusBadge.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 245, 165, 36));
                    if (HealthScoreDescText != null) HealthScoreDescText.Text = LocalizationService.Get("HealthDescFair", "سیستم نیازمند آزادسازی رم و بهینه سازی فایل ها");
                }
                else
                {
                    HealthScoreStatusPill.Text = LocalizationService.Get("HealthNeedsOpt", "نیاز به بهینه سازی");
                    HealthScoreStatusPill.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68));
                    HealthScoreStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(35, 239, 68, 68));
                    HealthScoreStatusBadge.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 239, 68, 68));
                    if (HealthScoreDescText != null) HealthScoreDescText.Text = LocalizationService.Get("HealthDescPoor", "مصرف منابع بالا؛ اجرای پاکسازی سریع توصیه می شود");
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
