using System;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class PersonalizationSettings
    {
        public bool IsTransparencyEnabled { get; set; }
        public bool IsAutoColorizationEnabled { get; set; }
        public bool IsDarkModeEnabled { get; set; }
        public bool IsBestPerformanceFx { get; set; }
    }

    public class WindowsPersonalizationService
    {
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
            }
            catch { }

            return s;
        }

        public static void SetTransparency(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                key?.SetValue("EnableTransparency", enable ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        public static void SetAutoColorization(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
                key?.SetValue("AutoColorization", enable ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
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
            }
            catch { }
        }

        public static void SetVisualEffectsPerformance(bool bestPerformance)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
                // 2 = Best performance (minimal animation latency), 1 = Best appearance
                key?.SetValue("VisualFXSetting", bestPerformance ? 2 : 1, RegistryValueKind.DWord);
            }
            catch { }
        }
    }
}
