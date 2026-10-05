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
        /// <c>GetPowerStatus</c> هر ۲٫۵ ثانیه (تایمر آمار داشبورد) صدا زده می‌شود و بدون کش،
        /// هر بار یک پروسه‌ی <c>powercfg.exe</c> بالا می‌آورد (چند ده میلی‌ثانیه + مصرف CPU
        /// و افت فریم UI). پلن برق به‌ندرت عوض می‌شود؛ بنابراین نتیجه را ۳۰ ثانیه نگه می‌داریم
        /// و فقط بعد از تغییر واقعی (SetActiveScheme) تازه‌سازی می‌کنیم.
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
                    name.Contains("حداکثر کارایی", StringComparison.OrdinalIgnoreCase) ||
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
        /// اجرای واقعی <c>powercfg /getactivescheme</c>. فقط از داخل GetActiveScheme صدا زده می‌شود.
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
                    string deviceType = status.IsLaptop ? "لپ‌تاپ شما به شارژر متصل است" : "سیستم دسکتاپ شما با برق مستقیم کار می‌کند";

                    return new AttentionDecisionItem
                    {
                        Id = "power_plan",
                        Title = "فعال‌سازی پلن مصرف انرژی با حداکثر کارایی (High Performance)",
                        Description = $"{deviceType}. برای حذف تأخیر کلاک پردازنده و پایدار ماندن حداکثر توان پردازشی، پلن High Performance فعال شود؟",
                        Category = "انرژی و پردازنده",
                        ActionText = "فعال‌سازی High Performance",
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
            try
            {
                // Standard High Performance GUID
                string highPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

                // Try setting directly
                if (SetActiveScheme(highPerfGuid)) return true;

                // If scheme not yet present in schemes list, duplicate it
                string? duplicatedGuid = DuplicateScheme(highPerfGuid);
                if (!string.IsNullOrEmpty(duplicatedGuid))
                {
                    return SetActiveScheme(duplicatedGuid);
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return false;
        }

        private static bool SetActiveScheme(string guid)
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
