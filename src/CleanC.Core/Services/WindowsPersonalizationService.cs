using System;
using System.Runtime.InteropServices;
using CleanC.Core.Diagnostics;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class PersonalizationSettings
    {
        public bool IsTransparencyEnabled { get; set; }
        public bool IsAutoColorizationEnabled { get; set; }
        public bool IsDarkModeEnabled { get; set; }
        public bool IsBestPerformanceFx { get; set; }
        public bool IsShowFileExtensionsEnabled { get; set; }
        public bool IsShowHiddenFilesEnabled { get; set; }
        public bool IsClassicContextMenuEnabled { get; set; }
    }

    public class WindowsPersonalizationService
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

        private const uint WM_SETTINGCHANGE = 0x001A;
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        public static void BroadcastSettingsChange()
        {
            try
            {
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "Environment", 2, 1000, out _);
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "TraySettings", 2, 1000, out _);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        public static PersonalizationSettings GetCurrentSettings()
        {
            var s = new PersonalizationSettings();

            try
            {
                using var pKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (pKey != null)
                {
                    var trans = pKey.GetValue("EnableTransparency");
                    s.IsTransparencyEnabled = trans != null && Convert.ToInt32(trans) == 1;

                    var light = pKey.GetValue("AppsUseLightTheme");
                    s.IsDarkModeEnabled = light != null && Convert.ToInt32(light) == 0;
                }

                using var dtKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                if (dtKey != null)
                {
                    var autoColor = dtKey.GetValue("AutoColorization");
                    s.IsAutoColorizationEnabled = autoColor != null && Convert.ToInt32(autoColor) == 1;
                }

                using var fxKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
                if (fxKey != null)
                {
                    var fx = fxKey.GetValue("VisualFXSetting");
                    s.IsBestPerformanceFx = fx != null && Convert.ToInt32(fx) == 2;
                }

                using var advKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                if (advKey != null)
                {
                    var ext = advKey.GetValue("HideFileExt");
                    s.IsShowFileExtensionsEnabled = ext != null && Convert.ToInt32(ext) == 0; // 0 = show extensions

                    var hidden = advKey.GetValue("Hidden");
                    s.IsShowHiddenFilesEnabled = hidden != null && Convert.ToInt32(hidden) == 1; // 1 = show hidden
                }

                using var menuKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32");
                s.IsClassicContextMenuEnabled = (menuKey != null);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return s;
        }

        public static void SetTransparency(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                key?.SetValue("EnableTransparency", enable ? 1 : 0, RegistryValueKind.DWord);
                BroadcastSettingsChange();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        public static void SetAutoColorization(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
                key?.SetValue("AutoColorization", enable ? 1 : 0, RegistryValueKind.DWord);
                BroadcastSettingsChange();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        public static void SetWindowsDarkMode(bool dark)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    int val = dark ? 0 : 1;
                    key.SetValue("AppsUseLightTheme", val, RegistryValueKind.DWord);
                    key.SetValue("SystemUsesLightTheme", val, RegistryValueKind.DWord);
                }
                BroadcastSettingsChange();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        public static void SetVisualEffectsPerformance(bool bestPerformance)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
                key?.SetValue("VisualFXSetting", bestPerformance ? 2 : 1, RegistryValueKind.DWord);
                BroadcastSettingsChange();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        public static void SetShowFileExtensions(bool show)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                key?.SetValue("HideFileExt", show ? 0 : 1, RegistryValueKind.DWord);
                BroadcastSettingsChange();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        public static void SetShowHiddenFiles(bool show)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                key?.SetValue("Hidden", show ? 1 : 2, RegistryValueKind.DWord);
                BroadcastSettingsChange();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        public static void SetClassicContextMenu(bool enable)
        {
            try
            {
                string keyPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
                if (enable)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(keyPath);
                    key?.SetValue("", "", RegistryValueKind.String);
                }
                else
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", false);
                }
                BroadcastSettingsChange();
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }
    }
}
