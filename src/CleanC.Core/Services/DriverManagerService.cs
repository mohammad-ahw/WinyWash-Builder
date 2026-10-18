using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class DriverItem
    {
        public string PublishedName { get; set; } = string.Empty; // e.g. oem14.inf
        public string OriginalName { get; set; } = string.Empty;  // e.g. realtekhsa.inf
        public string ProviderName { get; set; } = string.Empty;  // e.g. Realtek
        public string ClassName { get; set; } = string.Empty;     // e.g. Net, SoftwareComponent
        public string DriverVersion { get; set; } = string.Empty; // e.g. 11.0.6000.397
        public string DriverDate { get; set; } = string.Empty;    // e.g. 07/07/2026
        public bool IsDuplicateOldVersion { get; set; }
        public bool HasError { get; set; }
        public int ErrorCode { get; set; }
        public string StatusDescription { get; set; } = "آماده و سالم";
        public bool IsSelected { get; set; }

        // D14 — بسته ی یتیم: هیچ دستگاه فعالی از این بسته در DriverStore استفاده نمی کند
        // (سخت افزار مربوطه دیگر روی سیستم حاضر نیست یا بسته تکراری/قدیمی است)
        public bool IsOrphanNoHardware { get; set; }
        public string Evidence { get; set; } = string.Empty;
    }

    public class DriverSummaryStats
    {
        public int TotalDrivers { get; set; }
        public int ErrorCount { get; set; }
        public int DuplicateOldCount { get; set; }
        // D14 — تعداد بسته هایی که سخت افزار حاضر از آنها استفاده نمی کند
        public int OrphanNoHardwareCount { get; set; }
    }

    public static class DriverManagerService
    {
        public static async Task<(List<DriverItem> drivers, DriverSummaryStats stats)> GetDriversAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<DriverItem>();

                // 1. Scan DriverStore packages via pnputil /enum-drivers
                try
                {
                    var psi = new ProcessStartInfo("pnputil.exe", "/enum-drivers")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var p = Process.Start(psi);
                    if (p != null)
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(8000);
                        list = ParsePnpUtilDrivers(output);
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 2. Identify duplicate older versions in DriverStore
                // Group by OriginalName (e.g. realtekhsa.inf)
                var groups = list.GroupBy(d => d.OriginalName.ToLowerInvariant())
                                 .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() > 1);

                foreach (var g in groups)
                {
                    // Sort items by version descending
                    var sorted = g.OrderByDescending(x => ParseVersion(x.DriverVersion))
                                  .ThenByDescending(x => ParseDate(x.DriverDate))
                                  .ToList();

                    // The first is the newest; all others are duplicates/older!
                    for (int i = 1; i < sorted.Count; i++)
                    {
                        sorted[i].IsDuplicateOldVersion = true;
                        sorted[i].StatusDescription = "نسخه قدیمی DriverStore (تکراری)";
                    }
                }

                // 2.5 D14 — کراس چک با نودهای دستگاه: هر بسته ی DriverStore که هیچ
                //     دستگاه نصب شده ای (حتی جدا شده / phantom) به آن متصل نیست،
                //     کاندید «یتیم» است. منبع حقیقت: HKLM\SYSTEM\...\Control\Class
                //     که برای هر دستگاه مقدار InfPath (مثل oem14.inf) را نگه می دارد.
                //     اگر شمارش رجیستری شکست بخورد، هیچ چیز پرچم نمی خورد تا
                //     پیشنهاد نادرست ندهیم.
                try
                {
                    var referencedInfPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    using (var classRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class"))
                    {
                        if (classRoot != null)
                        {
                            foreach (var guidName in classRoot.GetSubKeyNames())
                            {
                                try
                                {
                                    using var classKey = classRoot.OpenSubKey(guidName);
                                    if (classKey == null) continue;

                                    foreach (var instanceName in classKey.GetSubKeyNames())
                                    {
                                        try
                                        {
                                            using var instanceKey = classKey.OpenSubKey(instanceName);
                                            string? infPath = instanceKey?.GetValue("InfPath")?.ToString();
                                            if (!string.IsNullOrEmpty(infPath)) referencedInfPaths.Add(infPath);
                                        }
                                        catch (Exception __ex)
                                        {
                                            CcLog.Ignore(__ex);
                                        }
                                    }
                                }
                                catch (Exception __ex)
                                {
                                    CcLog.Ignore(__ex);
                                }
                            }
                        }
                    }

                    if (referencedInfPaths.Count > 0)
                    {
                        foreach (var d in list)
                        {
                            if (d.HasError || string.IsNullOrEmpty(d.PublishedName)) continue;
                            if (!d.PublishedName.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)) continue;
                            if (referencedInfPaths.Contains(d.PublishedName)) continue;

                            d.IsOrphanNoHardware = true;
                            d.Evidence = LocalizationService.Get("EvDriverNoHardware", "هیچ دستگاهی (حتی جدا شده) به این بسته متصل نیست — سخت افزار مربوطه هرگز یا دیگر روی سیستم نیست");
                        }
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 3. Query Win32_PNPEntity for hardware error codes
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT Name, ConfigManagerErrorCode, Status FROM Win32_PNPEntity WHERE ConfigManagerErrorCode <> 0"
                    );

                    foreach (var mo in searcher.Get())
                    {
                        string devName = mo["Name"]?.ToString() ?? "دستگاه نامشخص";
                        int errCode = mo["ConfigManagerErrorCode"] != null ? Convert.ToInt32(mo["ConfigManagerErrorCode"]) : 0;
                        string status = mo["Status"]?.ToString() ?? "Error";

                        // Add error device to the list
                        list.Insert(0, new DriverItem
                        {
                            PublishedName = "سخت افزار معیوب",
                            OriginalName = devName,
                            ProviderName = "System Device",
                            ClassName = "Device Error",
                            DriverVersion = $"Error Code {errCode}",
                            DriverDate = DateTime.Now.ToString("yyyy.MM.dd"),
                            HasError = true,
                            ErrorCode = errCode,
                            StatusDescription = GetErrorCodeDescription(errCode)
                        });
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                var stats = new DriverSummaryStats
                {
                    TotalDrivers = list.Count,
                    ErrorCount = list.Count(d => d.HasError),
                    DuplicateOldCount = list.Count(d => d.IsDuplicateOldVersion),
                    OrphanNoHardwareCount = list.Count(d => d.IsOrphanNoHardware)
                };

                return (list, stats);
            });
        }

        public static async Task<(bool success, string message)> DeleteDriverPackageAsync(string publishedName, bool isErrorDevice = false, string deviceName = "")
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 1. Defective / Error device removal
                    if (isErrorDevice || publishedName == "سخت افزار معیوب" || !publishedName.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
                    {
                        string target = !string.IsNullOrEmpty(deviceName) ? deviceName : publishedName;
                        var psiDev = new ProcessStartInfo("pnputil.exe", $"/remove-device /deviceid \"{target.Trim()}\" /force")
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var pDev = Process.Start(psiDev);
                        if (pDev != null)
                        {
                            pDev.WaitForExit(8000);
                            return (true, $"دستگاه معیوب '{target}' با موفقیت از سیستم حذف و جدا شد.");
                        }
                        return (false, "عدم توانایی در اجرای ابزار جداسازی سخت افزار.");
                    }

                    string infName = publishedName.Trim();

                    // 2. Safely delete driver package from DriverStore without uninstalling active devices
                    var psi = new ProcessStartInfo("pnputil.exe", $"/delete-driver {infName}")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var p = Process.Start(psi);
                    if (p == null) return (false, "عدم توانایی در اجرای ابزار pnputil.");

                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(10000);

                    if (p.ExitCode == 0 || stdout.Contains("successfully", StringComparison.OrdinalIgnoreCase) || stdout.Contains("حذف شد", StringComparison.OrdinalIgnoreCase))
                    {
                        return (true, $"بسته درایور {infName} با موفقیت از مخزن درایورها حذف شد.");
                    }

                    // If in use by an active device, safely preserve it rather than forcibly breaking hardware
                    if (stdout.Contains("in use", StringComparison.OrdinalIgnoreCase) || stderr.Contains("in use", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, $"بسته درایور {infName} در حال استفاده توسط سخت‌افزار فعال سیستم است و جهت حفظ پایداری حذف نشد.");
                    }

                    // 4. Fallback to DISM /Online /Remove-Driver
                    try
                    {
                        var psiDism = new ProcessStartInfo("dism.exe", $"/Online /Remove-Driver /Driver:{infName}")
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var pDism = Process.Start(psiDism);
                        if (pDism != null)
                        {
                            string dismOut = pDism.StandardOutput.ReadToEnd();
                            pDism.WaitForExit(10000);
                            if (pDism.ExitCode == 0 || dismOut.Contains("100.0%", StringComparison.OrdinalIgnoreCase) || dismOut.Contains("The operation completed successfully", StringComparison.OrdinalIgnoreCase))
                            {
                                return (true, $"بسته درایور {infName} از طریق سرویس DISM با موفقیت حذف شد.");
                            }
                        }
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }

                    if (stdout.Contains("in use", StringComparison.OrdinalIgnoreCase) || stderr.Contains("in use", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, $"امکان حذف درایور {infName} وجود ندارد زیرا توسط هسته سیستم در حال استفاده فعال است.");
                    }

                    return (false, $"عدم موفقیت در حذف درایور: {(string.IsNullOrEmpty(stderr) ? stdout : stderr)}".Trim());
                }
                catch (Exception ex)
                {
                    return (false, $"خطا در فرآیند حذف: {ex.Message}");
                }
            });
        }

        public static async Task<List<string>> CheckDriverUpdatesAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<string>();
                try
                {
                    Type? sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
                    if (sessionType == null) return list;

                    dynamic? session = Activator.CreateInstance(sessionType);
                    if (session == null) return list;

                    dynamic searcher = session.CreateUpdateSearcher();
                    dynamic searchResult = searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0");

                    int count = searchResult.Updates.Count;
                    for (int i = 0; i < count; i++)
                    {
                        dynamic update = searchResult.Updates.Item(i);
                        string title = update.Title?.ToString() ?? "آپدیت درایور";
                        list.Add(title);
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                return list;
            });
        }

        private static List<DriverItem> ParsePnpUtilDrivers(string rawText)
        {
            var list = new List<DriverItem>();
            if (string.IsNullOrWhiteSpace(rawText)) return list;

            var blocks = rawText.Split(new[] { "Published Name:", "Published Name :" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var block in blocks)
            {
                if (!block.Contains(".inf", StringComparison.OrdinalIgnoreCase)) continue;

                var lines = block.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var item = new DriverItem();

                if (lines.Length > 0)
                {
                    item.PublishedName = lines[0].Trim();
                }

                foreach (var line in lines.Skip(1))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length < 2) continue;

                    string key = parts[0].Trim().ToLowerInvariant();
                    string val = parts[1].Trim();

                    switch (key)
                    {
                        case "original name":
                            item.OriginalName = val;
                            break;
                        case "provider name":
                            item.ProviderName = val;
                            break;
                        case "class name":
                            item.ClassName = val;
                            break;
                        case "driver version":
                            // Often format: "07/07/2026 11.0.6000.397"
                            var vParts = val.Split(' ', 2);
                            if (vParts.Length == 2)
                            {
                                item.DriverDate = vParts[0];
                                item.DriverVersion = vParts[1];
                            }
                            else
                            {
                                item.DriverVersion = val;
                            }
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(item.PublishedName) && item.PublishedName.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(item);
                }
            }

            return list;
        }

        private static Version ParseVersion(string verStr)
        {
            try
            {
                if (Version.TryParse(verStr, out var v)) return v;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return new Version(0, 0, 0, 0);
        }

        private static DateTime ParseDate(string dateStr)
        {
            try
            {
                if (DateTime.TryParse(dateStr, out var dt)) return dt;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return DateTime.MinValue;
        }

        private static string GetErrorCodeDescription(int code)
        {
            return code switch
            {
                1 => "دستگاه به درستی پیکربندی نشده است (Code 1)",
                10 => "دستگاه امکان راه اندازی ندارد (Code 10)",
                14 => "سیستم برای استفاده نیازمند ریستارت است (Code 14)",
                22 => "دستگاه توسط ویندوز غیرفعال شده است (Code 22)",
                28 => "درایور این سخت افزار نصب نشده است (Code 28)",
                31 => "ویندوز نمی تواند درایور را بارگذاری کند (Code 31)",
                39 => "درایور آسیب دیده یا مفقود شده است (Code 39)",
                43 => "سخت افزار به دلیل گزارش خطا متوقف شده است (Code 43)",
                _ => $"کد خطای سخت افزاری ({code})"
            };
        }
    }
}
