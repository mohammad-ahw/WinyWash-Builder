using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using CleanC.Core.Diagnostics;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class GlassPresetConfig
    {
        public int Effect { get; set; } = 1;
        public bool ClearAddress { get; set; } = true;
        public bool ClearBarBg { get; set; } = true;
        public bool ClearWinUIBg { get; set; } = true;
        public bool ShowLine { get; set; } = true;

        public byte LightR { get; set; } = 255;
        public byte LightG { get; set; } = 255;
        public byte LightB { get; set; } = 255;
        public byte LightA { get; set; } = 200;

        public byte DarkR { get; set; } = 0;
        public byte DarkG { get; set; } = 0;
        public byte DarkB { get; set; } = 0;
        public byte DarkA { get; set; } = 120;

        public string ToIniString()
        {
            return $"[config]\r\neffect={Effect}\r\nclearAddress={(ClearAddress ? "true" : "false")}\r\nclearBarBg={(ClearBarBg ? "true" : "false")}\r\nclearWinUIBg={(ClearWinUIBg ? "true" : "false")}\r\nshowLine={(ShowLine ? "true" : "false")}\r\n\r\n[light]\r\nr={LightR}\r\ng={LightG}\r\nb={LightB}\r\na={LightA}\r\n\r\n[dark]\r\nr={DarkR}\r\ng={DarkG}\r\nb={DarkB}\r\na={DarkA}\r\n";
        }
    }

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

        // ====================================================================
        // Glass / Mica Presets & Configuration
        // ====================================================================

        public static string GetGlassConfigPath()
        {
            string dir = AppPaths.LocalRoot;
            Directory.CreateDirectory(dir);
            return AppPaths.GlassConfigFile;
        }

        public static GlassPresetConfig LoadGlassConfig()
        {
            string path = GetGlassConfigPath();
            var cfg = new GlassPresetConfig();
            if (!File.Exists(path))
            {
                SaveGlassConfig(cfg);
                return cfg;
            }

            try
            {
                var lines = File.ReadAllLines(path);
                string section = "";
                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).ToLowerInvariant();
                        continue;
                    }
                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;
                    var key = parts[0].Trim().ToLowerInvariant();
                    var val = parts[1].Trim();

                    if (section == "config")
                    {
                        if (key == "effect" && int.TryParse(val, out int eff)) cfg.Effect = eff;
                        else if (key == "clearaddress") cfg.ClearAddress = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                        else if (key == "clearbarbg") cfg.ClearBarBg = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                        else if (key == "clearwinuibg") cfg.ClearWinUIBg = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                        else if (key == "showline") cfg.ShowLine = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (section == "light")
                    {
                        if (key == "r" && byte.TryParse(val, out byte r)) cfg.LightR = r;
                        else if (key == "g" && byte.TryParse(val, out byte g)) cfg.LightG = g;
                        else if (key == "b" && byte.TryParse(val, out byte b)) cfg.LightB = b;
                        else if (key == "a" && byte.TryParse(val, out byte a)) cfg.LightA = a;
                    }
                    else if (section == "dark")
                    {
                        if (key == "r" && byte.TryParse(val, out byte r)) cfg.DarkR = r;
                        else if (key == "g" && byte.TryParse(val, out byte g)) cfg.DarkG = g;
                        else if (key == "b" && byte.TryParse(val, out byte b)) cfg.DarkB = b;
                        else if (key == "a" && byte.TryParse(val, out byte a)) cfg.DarkA = a;
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex);
            }
            return cfg;
        }

        public static void SaveGlassConfig(GlassPresetConfig cfg)
        {
            try
            {
                string path = GetGlassConfigPath();
                File.WriteAllText(path, cfg.ToIniString());
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex);
            }
        }

        // ====================================================================
        // Wallpaper Management & CleanC Gallery
        // ====================================================================

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        private const int SPI_SETDESKWALLPAPER = 20;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;

        public static string GetCurrentWindowsWallpaper()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                return key?.GetValue("WallPaper")?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static bool SetDesktopWallpaper(string imagePath)
        {
            if (!File.Exists(imagePath)) return false;
            try
            {
                int result = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                return result != 0;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex);
                return false;
            }
        }

        public static List<string> GetCleanCWallpapers()
        {
            var list = new List<string>();
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string wpDir = Path.Combine(baseDir, "wallpaper");
                if (!Directory.Exists(wpDir) || Directory.GetFiles(wpDir).Length == 0)
                {
                    string parentWp = Path.Combine(baseDir, "..", "wallpaper");
                    if (Directory.Exists(parentWp) && Directory.GetFiles(parentWp).Length > 0) wpDir = parentWp;
                    else
                    {
                        string rootWp = Path.Combine(Directory.GetCurrentDirectory(), "wallpaper");
                        if (Directory.Exists(rootWp) && Directory.GetFiles(rootWp).Length > 0) wpDir = rootWp;
                        else
                        {
                            string picWp = @"C:\Users\moham\Pictures\Wallpaper";
                            if (Directory.Exists(picWp)) wpDir = picWp;
                        }
                    }
                }

                if (Directory.Exists(wpDir))
                {
                    var files = Directory.GetFiles(wpDir, "*.*")
                        .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    list.AddRange(files);
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex);
            }
            return list;
        }
    }
}
