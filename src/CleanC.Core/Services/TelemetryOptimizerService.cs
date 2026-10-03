using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class TelemetryOptimizerService
    {
        // 1. DiagTrack / Connected User Experiences
        public static bool IsDiagTrackDisabled()
        {
            try
            {
                using var scKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DiagTrack", false);
                if (scKey != null && Convert.ToInt32(scKey.GetValue("Start", 2)) == 4) return true;

                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", false);
                var val = key?.GetValue("AllowTelemetry");
                if (val != null && Convert.ToInt32(val) == 0) return true;
            }
            catch { }
            return false;
        }

        public static void SetDiagTrackDisabled(bool disable)
        {
            try
            {
                if (disable)
                {
                    StopAndDisableService("DiagTrack");
                    StopAndDisableService("dmwappushservice");
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                    key?.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord);
                }
                else
                {
                    EnableAndStartService("DiagTrack");
                    EnableAndStartService("dmwappushservice");
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                    key?.SetValue("AllowTelemetry", 3, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        // 2. Bing Search in Start Menu
        public static bool IsBingSearchDisabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\Explorer", false);
                var val = key?.GetValue("DisableSearchBoxSuggestions");
                if (val != null && Convert.ToInt32(val) == 1) return true;

                using var k2 = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search", false);
                var v2 = k2?.GetValue("BingSearchEnabled");
                if (v2 != null && Convert.ToInt32(v2) == 0) return true;
            }
            catch { }
            return false;
        }

        public static void SetBingSearchDisabled(bool disable)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\Explorer"))
                {
                    key?.SetValue("DisableSearchBoxSuggestions", disable ? 1 : 0, RegistryValueKind.DWord);
                }
                using (var k2 = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search"))
                {
                    k2?.SetValue("BingSearchEnabled", disable ? 0 : 1, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        // 3. Advertising ID
        public static bool IsAdvertisingIdDisabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", false);
                var val = key?.GetValue("Enabled");
                if (val != null && Convert.ToInt32(val) == 0) return true;
            }
            catch { }
            return false;
        }

        public static void SetAdvertisingIdDisabled(bool disable)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo");
                key?.SetValue("Enabled", disable ? 0 : 1, RegistryValueKind.DWord);
            }
            catch { }
        }

        // 4. Activity History & Feed Tracking
        public static bool IsActivityHistoryDisabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System", false);
                var val = key?.GetValue("PublishUserActivities");
                if (val != null && Convert.ToInt32(val) == 0) return true;
            }
            catch { }
            return false;
        }

        public static void SetActivityHistoryDisabled(bool disable)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System");
                key?.SetValue("PublishUserActivities", disable ? 0 : 1, RegistryValueKind.DWord);
                key?.SetValue("EnableActivityFeed", disable ? 0 : 1, RegistryValueKind.DWord);
            }
            catch { }
        }

        // 5. Tailored Experiences & Diagnostic Feedback
        public static bool IsFeedbackTrackingDisabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Privacy", false);
                var val = key?.GetValue("TailoredExperiencesWithDiagnosticDataEnabled");
                if (val != null && Convert.ToInt32(val) == 0) return true;
            }
            catch { }
            return false;
        }

        public static void SetFeedbackTrackingDisabled(bool disable)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Privacy"))
                {
                    key?.SetValue("TailoredExperiencesWithDiagnosticDataEnabled", disable ? 0 : 1, RegistryValueKind.DWord);
                }
                using (var k2 = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Siuf\Rules"))
                {
                    k2?.SetValue("NumberOfSIUFInPeriod", disable ? 0 : 1, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        // Master 1-Click Method
        public static async Task<int> DisableTelemetryAndDiagTrackAsync()
        {
            return await Task.Run(() =>
            {
                int count = 0;
                SetDiagTrackDisabled(true); count++;
                SetBingSearchDisabled(true); count++;
                SetAdvertisingIdDisabled(true); count++;
                SetActivityHistoryDisabled(true); count++;
                SetFeedbackTrackingDisabled(true); count++;
                return count;
            });
        }

        public static async Task<int> RestoreDefaultPrivacySettingsAsync()
        {
            return await Task.Run(() =>
            {
                int count = 0;
                SetDiagTrackDisabled(false); count++;
                SetBingSearchDisabled(false); count++;
                SetAdvertisingIdDisabled(false); count++;
                SetActivityHistoryDisabled(false); count++;
                SetFeedbackTrackingDisabled(false); count++;
                return count;
            });
        }

        public static bool IsTelemetryDisabled()
        {
            return IsDiagTrackDisabled();
        }

        private static bool StopAndDisableService(string serviceName)
        {
            try
            {
                var psiConfig = new ProcessStartInfo("sc.exe", $"config {serviceName} start= disabled")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p1 = Process.Start(psiConfig);
                p1?.WaitForExit(3000);

                var psiStop = new ProcessStartInfo("sc.exe", $"stop {serviceName}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p2 = Process.Start(psiStop);
                p2?.WaitForExit(3000);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool EnableAndStartService(string serviceName)
        {
            try
            {
                var psiConfig = new ProcessStartInfo("sc.exe", $"config {serviceName} start= demand")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p1 = Process.Start(psiConfig);
                p1?.WaitForExit(3000);

                var psiStart = new ProcessStartInfo("sc.exe", $"start {serviceName}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p2 = Process.Start(psiStart);
                p2?.WaitForExit(3000);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
