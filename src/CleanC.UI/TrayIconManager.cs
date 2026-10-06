using System;
using System.IO;
using System.Runtime.InteropServices;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;
using CleanC.Core.Services;
using Microsoft.UI.Xaml;

namespace CleanC.UI
{
    public class TrayIconManager : IDisposable
    {
        private readonly Window _window;
        private readonly IntPtr _hwnd;
        private readonly Action _onRestore;
        private readonly Action _onQuickBoost;
        private readonly Action _onNavigateDns;
        private readonly Action _onExit;
        private readonly SUBCLASSPROC _subclassProc;
        private bool _disposed;
        private const uint WM_TRAYICON = 0x8000 + 101; // WM_APP + 101
        private const uint TRAY_ID = 1001;

        // Tray Menu Commands
        private const int IDM_RESTORE = 2001;
        private const int IDM_BOOST_RAM = 2002;
        private const int IDM_QUICK_BOOST = 2003;
        private const int IDM_DNS = 2004;
        private const int IDM_EXIT = 2005;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;
        private const uint NIF_INFO = 0x00000010;

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;

        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_RBUTTONUP = 0x0205;

        // D13 — انتخاب آیکون تری با کیبورد (Shift+F10 / Enter در ناحیه اعلان)
        private const uint NIN_SELECT = 0x0400;
        private const uint NIN_KEYSELECT = 0x0401;

        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint TPM_RIGHTALIGN = 0x0008;
        private const uint TPM_BOTTOMALIGN = 0x0020;
        private const uint TPM_RETURNCMD = 0x0100;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lpTPMParams);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        private IntPtr _hIcon = IntPtr.Zero;

        // D13 — هِدِر منوی تری با کش ۱ ثانیه ای؛ GetCurrentStats نباید داخل
        // window-proc هر بار اجرا شود چون پیام های پنجره را معطل می کند.
        private string _statusHeader = "CleanC";
        private DateTime _statusHeaderAtUtc = DateTime.MinValue;

        // onExit: خروج باید از مسیر MainWindow بگذرد تا تنظیمات ذخیره شود و
        // جلوی بستنِ لغو شده ی Close-to-Tray گرفته شود؛ Application.Current.Exit این مسیر را دور می زند.
        public TrayIconManager(Window window, Action onRestore, Action onQuickBoost, Action onNavigateDns, Action onExit)
        {
            _window = window;
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            _onRestore = onRestore;
            _onQuickBoost = onQuickBoost;
            _onNavigateDns = onNavigateDns;
            _onExit = onExit;
            _subclassProc = WindowSubclassProc;

            InitializeTray();
        }

        private void InitializeTray()
        {
            try
            {
                // Load App Icon from Assets
                string baseDir = AppContext.BaseDirectory;
                string iconPath = Path.Combine(baseDir, "Assets", "app.ico");
                if (File.Exists(iconPath))
                {
                    _hIcon = LoadImage(IntPtr.Zero, iconPath, 1 /* IMAGE_ICON */, 16, 16, 0x00000010 /* LR_LOADFROMFILE */);
                }

                // Subclass window to intercept WM_TRAYICON
                SetWindowSubclass(_hwnd, _subclassProc, new IntPtr(1001), IntPtr.Zero);

                var nid = new NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = TRAY_ID,
                    uFlags = NIF_MESSAGE | NIF_TIP | (_hIcon != IntPtr.Zero ? NIF_ICON : 0),
                    uCallbackMessage = WM_TRAYICON,
                    hIcon = _hIcon,
                    szTip = "CleanC - بهینه ساز هوشمند ویندوز"
                };

                Shell_NotifyIcon(NIM_ADD, ref nid);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_TRAYICON)
            {
                uint msg = (uint)(lParam.ToInt64() & 0xFFFF);

                switch (msg)
                {
                    case WM_LBUTTONUP:
                    case WM_LBUTTONDBLCLK:
                        RestoreWindow();
                        return IntPtr.Zero;

                    case WM_RBUTTONUP:
                        ShowTrayMenu();
                        return IntPtr.Zero;

                    // D13 — دسترسی با کیبورد: وقتی آیکون تری با Enter/Space انتخاب می شود،
                    // باید مثل کلیکِ ماوس پنجره فعال شود وگرنه تری با کیبورد غیرقابل دسترس است.
                    case NIN_SELECT:
                    case NIN_KEYSELECT:
                        RestoreWindow();
                        return IntPtr.Zero;
                }
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        public void RestoreWindow()
        {
            ShowWindow(_hwnd, 9 /* SW_RESTORE */);
            SetForegroundWindow(_hwnd);
            _window.DispatcherQueue?.TryEnqueue(() => _onRestore?.Invoke());
        }

        private void ShowTrayMenu()
        {
            IntPtr hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            try
            {
                // D13 — آمار سیستم از کش ۱ ثانیه ای می آید (کلیکِ پشت سرهم روی تری
                // نباید چند بار سیستم را اسکن کند)
                DateTime nowUtc = DateTime.UtcNow;
                if ((nowUtc - _statusHeaderAtUtc).TotalSeconds >= 1.0)
                {
                    _statusHeaderAtUtc = nowUtc;
                    try
                    {
                        var stats = SystemStatsService.GetCurrentStats();
                        _statusHeader = $"CleanC (پردازنده: {stats.CpuPercent}% • رم: {stats.RamPercent}%)";
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex, "ShowTrayMenu.Stats");
                    }
                }

                AppendMenu(hMenu, MF_STRING, 0, _statusHeader);
                AppendMenu(hMenu, MF_SEPARATOR, 0, "");
                AppendMenu(hMenu, MF_STRING, IDM_RESTORE, "🪟 نمایش پنجره اصلی CleanC");
                AppendMenu(hMenu, MF_STRING, IDM_BOOST_RAM, "🚀 آزادسازی آنی حافظه رم (RAM Boost)");
                AppendMenu(hMenu, MF_STRING, IDM_QUICK_BOOST, "🧹 بهینه سازی کامل سیستم (Quick Boost)");
                AppendMenu(hMenu, MF_STRING, IDM_DNS, "🌐 تغییر سریع DNS و تحریم شکن");
                AppendMenu(hMenu, MF_SEPARATOR, 0, "");
                AppendMenu(hMenu, MF_STRING, IDM_EXIT, "❌ خروج از CleanC");

                GetCursorPos(out POINT pt);
                SetForegroundWindow(_hwnd);

                int cmd = TrackPopupMenuEx(hMenu, TPM_RIGHTALIGN | TPM_BOTTOMALIGN | TPM_RETURNCMD, pt.X, pt.Y, _hwnd, IntPtr.Zero);
                if (cmd > 0)
                {
                    HandleMenuCommand(cmd);
                }
            }
            finally
            {
                DestroyMenu(hMenu);
            }
        }

        private void HandleMenuCommand(int cmd)
        {
            switch (cmd)
            {
                case IDM_RESTORE:
                    RestoreWindow();
                    break;

                case IDM_BOOST_RAM:
                    _window.DispatcherQueue?.TryEnqueue(async () =>
                    {
                        var res = await OptimizerService.BoostRamAsync();
                        ShowBalloonNotification("آزادسازی حافظه رم", $"مقدار {SystemStats.FormatBytes(res.FreedBytes)} حافظه رم با موفقیت آزاد شد.");
                    });
                    break;

                case IDM_QUICK_BOOST:
                    RestoreWindow();
                    _window.DispatcherQueue?.TryEnqueue(() => _onQuickBoost?.Invoke());
                    break;

                case IDM_DNS:
                    RestoreWindow();
                    _window.DispatcherQueue?.TryEnqueue(() => _onNavigateDns?.Invoke());
                    break;

                case IDM_EXIT:
                    Dispose();
                    if (_onExit != null)
                    {
                        _window.DispatcherQueue?.TryEnqueue(() => _onExit.Invoke());
                    }
                    else
                    {
                        Application.Current.Exit();
                    }
                    break;
            }
        }

        public void ShowBalloonNotification(string title, string message)
            => ShowBalloonNotification(title, message, WinNotifySeverity.Info);

        /// <summary>
        /// F5 — balloon با شدت واقعی: آیکون و رنگ هشدار/خطا متناسب با severity
        /// تا کاربر از پشت پنجره هم بتواند فوریت پیام را تشخیص دهد.
        /// </summary>
        public void ShowBalloonNotification(string title, string message, WinNotifySeverity severity)
        {
            try
            {
                uint flags = severity switch
                {
                    WinNotifySeverity.Error => 0x00000003,   // NIIF_ERROR
                    WinNotifySeverity.Warning => 0x00000002, // NIIF_WARNING
                    _ => 0x00000001                           // NIIF_INFO
                };

                var nid = new NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = TRAY_ID,
                    uFlags = NIF_INFO,
                    szInfoTitle = title,
                    szInfo = message,
                    dwInfoFlags = flags
                };
                Shell_NotifyIcon(NIM_MODIFY, ref nid);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        /// <summary>
        /// F5 — به روزرسانی tooltip تری آیکون؛ جایگزین badge ویندوز وقتی
        /// API آن برای اپ unpackaged در دسترس نباشد.
        /// </summary>
        public void UpdateTooltip(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return;

                var nid = new NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = TRAY_ID,
                    uFlags = NIF_TIP,
                    szTip = text
                };
                Shell_NotifyIcon(NIM_MODIFY, ref nid);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                var nid = new NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = TRAY_ID
                };
                Shell_NotifyIcon(NIM_DELETE, ref nid);

                RemoveWindowSubclass(_hwnd, _subclassProc, new IntPtr(1001));

                // D13 — آیکونِ بارگذاری شده با LR_LOADFROMFILE یک handle دستی دارد
                // و بدون DestroyIcon در GDI نشت می کند (حتی بعد از NIM_DELETE).
                if (_hIcon != IntPtr.Zero)
                {
                    DestroyIcon(_hIcon);
                    _hIcon = IntPtr.Zero;
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }
    }
}
