using System;
using System.ServiceProcess;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
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
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return false;
        }

        public static bool SetDiagTrackDisabled(bool disable)
        {
            try
            {
                bool ok1;
                if (disable)
                {
                    ok1 = StopAndDisableService("DiagTrack");
                    // سرویس جانبی؛ روی بیلد های جدید ویندوز حذف شده و نبودنش طبیعی است
                    StopAndDisableService("dmwappushservice");
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                    key?.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord);
                }
                else
                {
                    ok1 = EnableAndStartService("DiagTrack");
                    EnableAndStartService("dmwappushservice");
                    // مقدار AllowTelemetry را حذف می کنیم تا Windows به پیش فرض edition خودش
                    // (Home/Pro=Basic(1)، Enterprise/Education=0) برگردد.
                    // قبلاً 3 (Full) می گذاشتیم که بدترین حالت تله متری است و عملاً
                    // «خاموش کردن» را به «حداکثر تله متری» تبدیل می کرد.
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                    key?.DeleteValue("AllowTelemetry", false);
                }

                // ملاک اصلی موفقیت، سرویس DiagTrack است (سرویس اصلی تله متری ویندوز)
                return ok1;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
                return false;
            }
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
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return false;
        }

        public static bool SetBingSearchDisabled(bool disable)
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
                return true;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
                return false;
            }
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
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return false;
        }

        public static bool SetAdvertisingIdDisabled(bool disable)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo");
                key?.SetValue("Enabled", disable ? 0 : 1, RegistryValueKind.DWord);
                return true;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
                return false;
            }
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
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return false;
        }

        public static bool SetActivityHistoryDisabled(bool disable)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System");
                key?.SetValue("PublishUserActivities", disable ? 0 : 1, RegistryValueKind.DWord);
                key?.SetValue("EnableActivityFeed", disable ? 0 : 1, RegistryValueKind.DWord);
                return true;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
                return false;
            }
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
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return false;
        }

        public static bool SetFeedbackTrackingDisabled(bool disable)
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
                return true;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
                return false;
            }
        }

        // Master 1-Click Method
        public static async Task<int> DisableTelemetryAndDiagTrackAsync()
        {
            return await Task.Run(() =>
            {
                // شمارش فقط برای مواردی که واقعاً اعمال شده اند (به جای شمارش تلاش ها)
                int count = 0;
                if (SetDiagTrackDisabled(true)) count++;
                if (SetBingSearchDisabled(true)) count++;
                if (SetAdvertisingIdDisabled(true)) count++;
                if (SetActivityHistoryDisabled(true)) count++;
                if (SetFeedbackTrackingDisabled(true)) count++;
                return count;
            });
        }

        public static async Task<int> RestoreDefaultPrivacySettingsAsync()
        {
            return await Task.Run(() =>
            {
                int count = 0;
                if (SetDiagTrackDisabled(false)) count++;
                if (SetBingSearchDisabled(false)) count++;
                if (SetAdvertisingIdDisabled(false)) count++;
                if (SetActivityHistoryDisabled(false)) count++;
                if (SetFeedbackTrackingDisabled(false)) count++;
                return count;
            });
        }

        public static bool IsTelemetryDisabled()
        {
            return IsDiagTrackDisabled();
        }

        /// <summary>
        /// غیرفعال کردن نوع راه اندازی سرویس از طریق رجیستری و توقف آن با ServiceController
        /// (جایگزین sc.exe که الگوی مشکوک آنتی ویروسی داشت). خروجی true یعنی سرویس واقعاً
        /// در حالت Disabled ثبت شد؛ روی بیلد هایی که سرویس وجود ندارد false برمی گردد.
        /// </summary>
        private static bool StopAndDisableService(string serviceName)
        {
            bool registryChanged = false;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", true))
                {
                    if (key != null)
                    {
                        var currentStart = key.GetValue("Start");
                        if (currentStart != null && Convert.ToInt32(currentStart) != 4)
                        {
                            key.SetValue("WinyWash_OriginalStart", currentStart, RegistryValueKind.DWord);
                        }
                        key.SetValue("Start", 4, RegistryValueKind.DWord); // 4 = Disabled
                        registryChanged = true;
                    }
                }

                using var sc = new ServiceController(serviceName);
                if (sc.Status != ServiceControllerStatus.Stopped)
                {
                    try
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(12));
                    }
                    catch (Exception __ex)
                    {
                        // سرویس هست ولی متوقف نشد (مثلاً Protected Process)؛
                        // تغییر نوع راه اندازی به Disabled برای دفعات بعد کافی است
                        CcLog.Ignore(__ex, $"Telemetry.StopService.{serviceName}");
                    }
                }

                return registryChanged;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex, $"Telemetry.Service.{serviceName}");
                return false;
            }
        }

        /// <summary>
        /// بازگرداندن سرویس به حالت Manual (پیش فرض DiagTrack) و روشن کردن آن.
        /// </summary>
        private static bool EnableAndStartService(string serviceName)
        {
            bool registryChanged = false;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", true))
                {
                    if (key != null)
                    {
                        // مقادیر پیش‌فرض رسمی ویندوز: DiagTrack = 2 (Automatic)، dmwappushservice = 3 (Manual)
                        int targetStart = serviceName.Equals("dmwappushservice", StringComparison.OrdinalIgnoreCase) ? 3 : 2;

                        try
                        {
                            var orig = key.GetValue("WinyWash_OriginalStart");
                            if (orig != null && int.TryParse(orig.ToString(), out int parsed) && parsed is >= 1 and <= 3)
                            {
                                targetStart = parsed;
                            }
                        }
                        catch { }

                        try { key.DeleteValue("WinyWash_OriginalStart", false); } catch { }

                        key.SetValue("Start", targetStart, RegistryValueKind.DWord);
                        registryChanged = true;
                    }
                }

                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    try
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(12));
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex, $"Telemetry.StartService.{serviceName}");
                    }
                }

                return registryChanged;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex, $"Telemetry.Service.{serviceName}");
                return false;
            }
        }
    }
}
