// O:\OpenCode\AntiGravity\CleanC - NEW\src\CleanC.UI\NativeMethods.cs
using System;
using System.Runtime.InteropServices;

namespace CleanC.UI
{
    /// <summary>
    /// Win32 interop مربوط به لایه ی برنامه (single-instance، پنجره و ...)
    /// </summary>
    internal static class NativeMethods
    {
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindowW(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// آیا پنجره ی اصلی همین حالا فوکوس دارد؟
        /// نوتیفیکیشن ویندوزی فقط زمانی می فرستیم که کاربر پنجره را نمی بیند،
        /// وگرنه InfoBar داخلی خودش کافی است.
        /// </summary>
        internal static bool IsWindowFocused(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            try { return GetForegroundWindow() == hWnd; }
            catch { return true; /* در صورت شکست، اعلان داخلی همچنان نمایش داده می شود */ }
        }

        /// <summary>
        /// پنجره ی بازِ CleanC را از حالت minimize درمی آورد و به جلو می آورد.
        /// در صورت نبود پنجره بی اثر برمی گردد.
        /// </summary>
        internal static void ActivateExistingInstance()
        {
            try
            {
                IntPtr hWnd = FindWindowW(null, "CleanC");
                if (hWnd == IntPtr.Zero) return;

                if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
                else ShowWindow(hWnd, SW_SHOW);

                SetForegroundWindow(hWnd);
            }
            catch
            {
                // فعال سازی پنجره هرگز نباید مانع اجرای برنامه شود
            }
        }

        /// <summary>
        /// آیا پنجره در حالت minimize است؟
        /// WinAppSDK نسخه ی فعلی AppWindow.PresenterState ندارد، پس مستقیم از Win32 می پرسیم.
        /// </summary>
        internal static bool IsWindowMinimized(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            try { return IsIconic(hWnd); }
            catch { return false; }
        }
    }
}
