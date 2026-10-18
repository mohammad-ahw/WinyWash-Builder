using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;

namespace CleanC.Core.Services
{
    public class PowerStatusInfo
    {
        public bool IsLaptop { get; set; }
        public bool IsOnAcPower { get; set; }
        public int BatteryPercent { get; set; }
        public string ActiveSchemeName { get; set; } = string.Empty;
        public string ActiveSchemeGuid { get; set; } = string.Empty;
        public bool IsHighPerformanceOrUltimate { get; set; }
        public string ActiveSchemeKey
        {
            get
            {
                if (ActiveSchemeGuid.Equals("e9a42b02-d5df-448d-aa00-03f14749eb61", StringComparison.OrdinalIgnoreCase) ||
                    ActiveSchemeName.Contains("Ultimate", StringComparison.OrdinalIgnoreCase))
                    return "ultimate";
                if (ActiveSchemeGuid.Equals("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase) ||
                    ActiveSchemeName.Contains("High", StringComparison.OrdinalIgnoreCase) ||
                    ActiveSchemeName.Contains("حداکثر کارایی", StringComparison.OrdinalIgnoreCase)) // i18n-logic: این متن با داده‌ی خارجی مقایسه/تطبیق داده م‌یشود
                    return "high";
                if (ActiveSchemeGuid.Equals("a1841308-3541-4fab-bc81-f71556f20b4a", StringComparison.OrdinalIgnoreCase) ||
                    ActiveSchemeName.Contains("Saver", StringComparison.OrdinalIgnoreCase) ||
                    ActiveSchemeName.Contains("ذخیره انرژی", StringComparison.OrdinalIgnoreCase)) // i18n-logic: این متن با داده‌ی خارجی مقایسه/تطبیق داده م‌یشود
                    return "saver";
                return "balanced";
            }
        }
    }

    public class PowerSchemeInfo
    {
        public string Guid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public class PowerPlanService
    {
        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus lpSystemPowerStatus);

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus; // 0 = Offline, 1 = Online, 255 = Unknown
            public byte BatteryFlag;  // 128 = No system battery, 255 = Unknown
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        /// <summary>
        /// کش پلن برق فعال.
        /// <c>GetPowerStatus</c> هر ۲٫۵ ثانیه (تایمر آمار داشبورد) صدا زده می شود و بدون کش،
        /// هر بار یک پروسه ی <c>powercfg.exe</c> بالا می آورد (چند ده میلی ثانیه + مصرف CPU
        /// و افت فریم UI). پلن برق به ندرت عوض می شود؛ بنابراین نتیجه را ۳۰ ثانیه نگه می داریم
        /// و فقط بعد از تغییر واقعی (SetActiveScheme) تازه سازی می کنیم.
        /// </summary>
        private static readonly TimeSpan SchemeCacheTtl = TimeSpan.FromSeconds(30);
        private static readonly object SchemeLock = new();
        private static (string Guid, string Name)? _schemeCache;
        private static DateTime _schemeCacheAtUtc = DateTime.MinValue;

        public static PowerStatusInfo GetPowerStatus()
        {
            var info = new PowerStatusInfo();

            try
            {
                if (GetSystemPowerStatus(out var status))
                {
                    // BatteryFlag: 128 means no system battery (Desktop PC)
                    info.IsLaptop = (status.BatteryFlag != 128 && status.BatteryFlag != 255);
                    info.IsOnAcPower = (status.ACLineStatus == 1 || !info.IsLaptop);
                    info.BatteryPercent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : 100;
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            try
            {
                var (guid, name) = GetActiveScheme();
                info.ActiveSchemeGuid = guid;
                info.ActiveSchemeName = name;

                if (name.Contains("High performance", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("حداکثر کارایی", StringComparison.OrdinalIgnoreCase) || // i18n-logic: این متن با داده‌ی خارجی مقایسه/تطبیق داده م‌یشود
                    guid.Equals("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase) ||
                    guid.Equals("e9a42b02-d5df-448d-aa00-03f14749eb61", StringComparison.OrdinalIgnoreCase))
                {
                    info.IsHighPerformanceOrUltimate = true;
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return info;
        }

        public static (string Guid, string Name) GetActiveScheme(bool forceRefresh = false)
        {
            lock (SchemeLock)
            {
                if (!forceRefresh && _schemeCache.HasValue &&
                    (DateTime.UtcNow - _schemeCacheAtUtc) < SchemeCacheTtl)
                {
                    return _schemeCache.Value;
                }
            }

            var result = ReadActiveSchemeFromPowercfg();

            lock (SchemeLock)
            {
                _schemeCache = result;
                _schemeCacheAtUtc = DateTime.UtcNow;
            }

            return result;
        }

        /// <summary>
        /// اجرای واقعی <c>powercfg /getactivescheme</c>. فقط از داخل GetActiveScheme صدا زده می شود.
        /// </summary>
        private static (string Guid, string Name) ReadActiveSchemeFromPowercfg()
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);

                    var match = Regex.Match(output, @"Power Scheme GUID:\s*([a-fA-F0-9\-]+)\s*\((.*?)\)");
                    if (match.Success)
                    {
                        return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return (string.Empty, "Balanced");
        }

        public static AttentionDecisionItem? CheckPowerPlanAttention()
        {
            try
            {
                var status = GetPowerStatus();

                // If currently connected to AC power and not already in High/Ultimate performance
                if (status.IsOnAcPower && !status.IsHighPerformanceOrUltimate)
                {
                    string deviceType = status.IsLaptop ? LocalizationService.Get("لپ تاپ شما به شارژر متصل است") : LocalizationService.Get("سیستم دسکتاپ شما با برق مستقیم کار می کند");

                    return new AttentionDecisionItem
                    {
                        Id = "power_plan",
                        Title = LocalizationService.Get("فعال سازی پلن مصرف انرژی با حداکثر کارایی (High Performance)"),
                        Description = string.Format(LocalizationService.Get("{0}. برای حذف تأخیر کلاک پردازنده و پایدار ماندن حداکثر توان پردازشی، پلن High Performance فعال شود؟"), deviceType),
                        Category = LocalizationService.Get("انرژی و پردازنده"),
                        ActionText = LocalizationService.Get("فعال سازی High Performance"),
                        IconGlyph = "\uE945"
                    };
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return null;
        }

        public static bool ApplyHighPerformancePlan()
        {
            var status = GetPowerStatus();
            if (status.IsLaptop)
            {
                // On laptops, High Performance locks CPU min clock at 100%, causing fan revving and battery drain.
                // Keep laptop on Balanced plan.
                return SetActivePlan("balanced");
            }
            return SetActivePlan("high");
        }

        public static List<PowerSchemeInfo> GetAllSchemes()
        {
            var list = new List<PowerSchemeInfo>();
            try
            {
                var psi = new ProcessStartInfo("powercfg", "/list")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    var matches = Regex.Matches(output, @"Power Scheme GUID:\s*([a-fA-F0-9\-]+)\s*\((.*?)\)(\s*\*?)");
                    foreach (Match m in matches)
                    {
                        list.Add(new PowerSchemeInfo
                        {
                            Guid = m.Groups[1].Value.Trim(),
                            Name = m.Groups[2].Value.Trim(),
                            IsActive = m.Groups[3].Value.Contains("*")
                        });
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return list;
        }

        public static bool SetActivePlan(string planKey)
        {
            try
            {
                string ultimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
                string highPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
                string balancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
                string powerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";

                string targetGuid = planKey.ToLowerInvariant() switch
                {
                    "ultimate" => ultimateGuid,
                    "high" => highPerfGuid,
                    "balanced" => balancedGuid,
                    "saver" => powerSaverGuid,
                    _ => planKey
                };

                // Check existing schemes first
                var existing = GetAllSchemes();
                var found = existing.Find(s => s.Guid.Equals(targetGuid, StringComparison.OrdinalIgnoreCase) ||
                                              (planKey == "ultimate" && s.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)) ||
                                              (planKey == "high" && s.Name.Contains("High", StringComparison.OrdinalIgnoreCase)) ||
                                              (planKey == "balanced" && s.Name.Contains("Balanced", StringComparison.OrdinalIgnoreCase)) ||
                                              (planKey == "saver" && s.Name.Contains("Saver", StringComparison.OrdinalIgnoreCase)));
                if (found != null)
                {
                    return SetActiveScheme(found.Guid);
                }

                // If not found, try setting target GUID directly
                if (SetActiveScheme(targetGuid)) return true;

                // Otherwise, duplicate from standard GUID
                string? dup = DuplicateScheme(targetGuid);
                if (!string.IsNullOrEmpty(dup))
                {
                    return SetActiveScheme(dup);
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return false;
        }

        public static bool SetActiveScheme(string guid)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", $"/setactive {guid}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);

                bool ok = p?.ExitCode == 0;
                if (ok)
                {
                    // پلن واقعاً عوض شد؛ کش را باطل کن تا خواندن بعدی تازه باشد
                    lock (SchemeLock)
                    {
                        _schemeCache = null;
                        _schemeCacheAtUtc = DateTime.MinValue;
                    }
                }
                return ok;
            }
            catch
            {
                return false;
            }
        }

        private static string? DuplicateScheme(string sourceGuid)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", $"/duplicatescheme {sourceGuid}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);

                    var match = Regex.Match(output, @"Power Scheme GUID:\s*([a-fA-F0-9\-]+)");
                    if (match.Success)
                    {
                        return match.Groups[1].Value.Trim();
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return null;
        }
    }
}
