using System;
using System.IO;
using System.Runtime.InteropServices;
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

        private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        private IntPtr _hIcon = IntPtr.Zero;

        public TrayIconManager(Window window, Action onRestore, Action onQuickBoost, Action onNavigateDns)
        {
            _window = window;
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            _onRestore = onRestore;
            _onQuickBoost = onQuickBoost;
            _onNavigateDns = onNavigateDns;
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
                    szTip = "CleanC - بهینه‌ساز هوشمند ویندوز"
                };

                Shell_NotifyIcon(NIM_ADD, ref nid);
            }
            catch { }
        }

        private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_TRAYICON)
            {
                uint msg = (uint)(lParam.ToInt64() & 0xFFFF);
                if (msg == WM_LBUTTONUP || msg == WM_LBUTTONDBLCLK)
                {
                    RestoreWindow();
                    return IntPtr.Zero;
                }
                else if (msg == WM_RBUTTONUP)
                {
                    ShowTrayMenu();
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
                var stats = SystemStatsService.GetCurrentStats();
                string statusHeader = $"CleanC (پردازنده: {stats.CpuPercent}% • رم: {stats.RamPercent}%)";

                AppendMenu(hMenu, MF_STRING, 0, statusHeader);
                AppendMenu(hMenu, MF_SEPARATOR, 0, "");
                AppendMenu(hMenu, MF_STRING, IDM_RESTORE, "🪟 نمایش پنجره اصلی CleanC");
                AppendMenu(hMenu, MF_STRING, IDM_BOOST_RAM, "🚀 آزادسازی آنی حافظه رم (RAM Boost)");
                AppendMenu(hMenu, MF_STRING, IDM_QUICK_BOOST, "🧹 بهینه‌سازی کامل سیستم (Quick Boost)");
                AppendMenu(hMenu, MF_STRING, IDM_DNS, "🌐 تغییر سریع DNS و تحریم‌شکن");
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
                    Application.Current.Exit();
                    break;
            }
        }

        public void ShowBalloonNotification(string title, string message)
        {
            try
            {
                var nid = new NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = TRAY_ID,
                    uFlags = NIF_INFO,
                    szInfoTitle = title,
                    szInfo = message,
                    dwInfoFlags = 0x00000001 /* NIIF_INFO */
                };
                Shell_NotifyIcon(NIM_MODIFY, ref nid);
            }
            catch { }
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
            }
            catch { }
        }
    }
}
