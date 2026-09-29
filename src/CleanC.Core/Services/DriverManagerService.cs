using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
    }

    public class DriverSummaryStats
    {
        public int TotalDrivers { get; set; }
        public int ErrorCount { get; set; }
        public int DuplicateOldCount { get; set; }
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
                catch { }

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
                            PublishedName = "سخت‌افزار معیوب",
                            OriginalName = devName,
                            ProviderName = "System Device",
                            ClassName = "Device Error",
                            DriverVersion = $"Error Code {errCode}",
                            DriverDate = DateTime.Now.ToString("yyyy/MM/dd"),
                            HasError = true,
                            ErrorCode = errCode,
                            StatusDescription = GetErrorCodeDescription(errCode)
                        });
                    }
                }
                catch { }

                var stats = new DriverSummaryStats
                {
                    TotalDrivers = list.Count,
                    ErrorCount = list.Count(d => d.HasError),
                    DuplicateOldCount = list.Count(d => d.IsDuplicateOldVersion)
                };

                return (list, stats);
            });
        }

        public static async Task<(bool success, string message)> DeleteDriverPackageAsync(string publishedName)
        {
            if (string.IsNullOrWhiteSpace(publishedName) || !publishedName.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "نام بسته درایور نامعتبر است.");
            }

            return await Task.Run(() =>
            {
                try
                {
                    // Safe execution without /force
                    var psi = new ProcessStartInfo("pnputil.exe", $"/delete-driver {publishedName.Trim()}")
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
                        return (true, $"بسته درایور {publishedName} با موفقیت از سیستم حذف شد.");
                    }

                    if (stdout.Contains("in use", StringComparison.OrdinalIgnoreCase) || stderr.Contains("in use", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, $"امکان حذف درایور {publishedName} وجود ندارد زیرا در حال حاضر توسط سیستم در حال استفاده (In use) است.");
                    }

                    return (false, $"خطا در حذف درایور: {(string.IsNullOrEmpty(stderr) ? stdout : stderr)}".Trim());
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
                catch { }

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
            catch { }
            return new Version(0, 0, 0, 0);
        }

        private static DateTime ParseDate(string dateStr)
        {
            try
            {
                if (DateTime.TryParse(dateStr, out var dt)) return dt;
            }
            catch { }
            return DateTime.MinValue;
        }

        private static string GetErrorCodeDescription(int code)
        {
            return code switch
            {
                1 => "دستگاه به درستی پیکربندی نشده است (Code 1)",
                10 => "دستگاه امکان راه‌اندازی ندارد (Code 10)",
                14 => "سیستم برای استفاده نیازمند ریستارت است (Code 14)",
                22 => "دستگاه توسط ویندوز غیرفعال شده است (Code 22)",
                28 => "درایور این سخت‌افزار نصب نشده است (Code 28)",
                31 => "ویندوز نمی‌تواند درایور را بارگذاری کند (Code 31)",
                39 => "درایور آسیب‌دیده یا مفقود شده است (Code 39)",
                43 => "سخت‌افزار به دلیل گزارش خطا متوقف شده است (Code 43)",
                _ => $"کد خطای سخت‌افزاری ({code})"
            };
        }
    }
}
