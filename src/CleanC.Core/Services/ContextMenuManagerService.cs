using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public static class ContextMenuManagerService
    {
        private const string ClassicMenuClsidKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

        public static bool IsClassicMenuEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ClassicMenuClsidKey, false);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> SetClassicMenuEnabledAsync(bool enabled)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (enabled)
                    {
                        using var key = Registry.CurrentUser.CreateSubKey(ClassicMenuClsidKey);
                        key?.SetValue("", "", RegistryValueKind.String);
                    }
                    else
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", false);
                    }

                    RestartExplorer();
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public static void RestartExplorer()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("explorer"))
                {
                    try { p.Kill(); } catch { }
                }

                // Give Windows a moment to auto-restart explorer, or launch it if not restarted
                Task.Delay(500).Wait();
                if (Process.GetProcessesByName("explorer").Length == 0)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                }
            }
            catch { }
        }
    }
}
