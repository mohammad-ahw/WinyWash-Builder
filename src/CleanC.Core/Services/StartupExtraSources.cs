// src/CleanC.Core/Services/StartupExtraSources.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    /// <summary>
    /// منابعِ استارت آپ که قبلاً هیچ کدام اسکن نمی شدند:
    ///
    ///  1. <c>RunOnce</c> (HKCU و HKLM) — کلیدهایی که یک بار اجرا و بعد پاک می شوند.
    ///  2. <c>StartupTask</c> برنامه های MSIX/UWP — مثل Spotify/Teams/OneDrive نسخه ی Store،
    ///     که در رجیستریِ Run ظاهر نمی شوند و فقط در
    ///     <c>AppModel\SystemAppData\&lt;PackageFamilyName&gt;\&lt;AppId&gt;</c>
    ///     با مقدارهای <c>State</c> / <c>UserEnabledStartupOnce</c> / <c>LastDisabledTime</c> ثبت شده اند.
    ///  3. تسک های Task Scheduler با تریگر <c>At logon time</c> یا <c>At system start up</c>
    ///     (روی همین سیستم ۸۲ تسک از این جنس پیدا شد که هیچ کدام در لیست نبودند).
    ///
    /// همه با همان مدلِ <see cref="StartupItem"/> برمی گردند تا UI و جریان غیرفعال سازی یکسان بماند.
    /// </summary>
    public static class StartupExtraSources
    {
        public const string LocationRunOnceUser = "HKCU RunOnce";
        public const string LocationRunOnceMachine = "HKLM RunOnce";
        public const string LocationTask = "TaskScheduler";
        public const string LocationUwp = "StartupTask";

        private const string SystemAppDataRoot =
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

        /// <summary>
        /// هر سه منبع را اسکن می کند. جست وجوی تسک ها (schtasks حدود چند ثانیه طول می کشد)
        /// هم زمان با بقیه انجام می شود تا بارِ صفحه ی Startup Manager زیاد نشود.
        /// </summary>
        public static void Scan(List<StartupItem> list)
        {
            var taskItems = new List<StartupItem>();
            var taskScan = Task.Run(() => ScanScheduledTasks(taskItems));

            ScanRunOnce(list);
            ScanUwpStartupTasks(list);

            try { taskScan.Wait(); }
            catch (Exception ex) { CcLog.Ignore(ex, "Startup.ScheduledTasks.Wait"); }

            list.AddRange(taskItems);
        }

        // ====================================================================
        // 1) RunOnce
        // ====================================================================
        private static void ScanRunOnce(List<StartupItem> list)
        {
            ScanRunOnceKey(Registry.CurrentUser, LocationRunOnceUser, list);
            ScanRunOnceKey(Registry.LocalMachine, LocationRunOnceMachine, list);
        }

        private static void ScanRunOnceKey(RegistryKey root, string location, List<StartupItem> list)
        {
            try
            {
                using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", false);
                if (key == null) return;

                foreach (var name in key.GetValueNames())
                {
                    string cmd = key.GetValue(name)?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(cmd)) continue;
                    if (list.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && x.Location == location))
                        continue;

                    bool isProtected = StartupOptimizerService.IsWhitelisted(name, cmd);
                    list.Add(new StartupItem
                    {
                        Id = $"{location}_{name}",
                        Name = name,
                        Command = cmd,
                        Location = location,
                        ServiceKey = name,
                        IsRunOnce = true,
                        IsNonEssential = !isProtected,
                        IsDisabled = false,
                        IsVpnOrProxy = StartupOptimizerService.IsVpnApp(name, cmd),
                        Category = StartupOptimizerService.ResolveStartupCategory(name, cmd, isProtected)
                    });
                }
            }
            catch (Exception ex) { CcLog.Ignore(ex, "Startup.RunOnce"); }
        }

        // ====================================================================
        // 2) StartupTaskهای MSIX/UWP
        // ====================================================================
        private static void ScanUwpStartupTasks(List<StartupItem> list)
        {
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(SystemAppDataRoot, false);
                if (root == null) return;

                foreach (var packageFamily in root.GetSubKeyNames())
                {
                    try
                    {
                        using var pkgKey = root.OpenSubKey(packageFamily, false);
                        if (pkgKey == null) continue;

                        foreach (var appId in pkgKey.GetSubKeyNames())
                        {
                            using var appKey = pkgKey.OpenSubKey(appId, false);
                            if (appKey == null) continue;

                            // فقط زیرکلیدهایی StartupTask هستند که State و یکی از
                            // UserEnabledStartupOnce / LastDisabledTime را داشته باشند؛
                            // بقیه ی زیرکلیدهای هر پکیج (HAM، SplashScreen، ...) ارتباطی با استارت آپ ندارند.
                            object? stateValue = appKey.GetValue("State");
                            bool isStartupTask = stateValue != null &&
                                (appKey.GetValue("UserEnabledStartupOnce") != null
                              || appKey.GetValue("LastDisabledTime") != null);
                            if (!isStartupTask) continue;

                            int state = Convert.ToInt32(stateValue ?? 1);
                            // 1 و 4 یعنی فعال؛ 0 و 2 و 3 یعنی غیرفعال (Disabled / DisabledByUser / DisabledByPolicy)
                            bool enabled = state == 1 || state == 4;

                            string friendly = FriendlyPackageName(packageFamily, appId);
                            string keyPath = $@"{packageFamily}\{appId}";

                            if (list.Any(x => x.Location == LocationUwp && x.ServiceKey == keyPath)) continue;

                            list.Add(new StartupItem
                            {
                                Id = $"{LocationUwp}_{keyPath}",
                                Name = friendly,
                                // برای نمایش، خانواده ی پکیج را نگه می داریم
                                Command = packageFamily,
                                Location = LocationUwp,
                                ServiceKey = keyPath,
                                IsUwpStartupTask = true,
                                IsNonEssential = true,
                                IsDisabled = !enabled,
                                IsVpnOrProxy = false,
                                Category = "برنامه MSIX/UWP (اختیاری)"
                            });
                        }
                    }
                    catch (Exception ex) { CcLog.Ignore(ex, "Startup.Uwp.Package"); }
                }
            }
            catch (Exception ex) { CcLog.Ignore(ex, "Startup.Uwp"); }
        }

        /// <summary>
        /// ساختن یک نام قابل خواندن از شناسه ی پکیج (مثل
        /// <c>OneMSI_Dragon_Center_UWP</c> یا <c>9E2F88E3.TWITTER_wgeqdkkx372wm!App</c>).
        /// </summary>
        private static string FriendlyPackageName(string packageFamilyName, string appId)
        {
            string name = appId;
            int bang = name.IndexOf('!');
            if (bang > 0) name = name.Substring(0, bang);

            name = name.Replace('_', ' ').Trim();

            // پسوندِ هشِ خانواده ی پکیج را حذف کن (۱۰+ کاراکتر alphanumeric در انتها)
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+[A-Za-z0-9]{10,}$", "").Trim();

            // شناسه ی ناشر مثل «9E2F88E3.» را هم حذف کن
            name = System.Text.RegularExpressions.Regex.Replace(name, @"^[0-9A-Fa-f]{8}\.\s*", "").Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                name = packageFamilyName;
                int us = name.IndexOf('_');
                if (us > 0) name = name.Substring(0, us);
                name = name.Replace('.', ' ').Trim();
            }

            return string.IsNullOrWhiteSpace(name) ? appId : name;
        }

        // ====================================================================
        // 3) تسک های Task Scheduler با تریگر Logon/Boot
        // ====================================================================
        private static void ScanScheduledTasks(List<StartupItem> list)
        {
            try
            {
                if (!RunSchtasks("/query /fo CSV /v", out string output)) return;

                var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var lines = output.Split('\n');

                foreach (var raw in lines)
                {
                    string line = raw.TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var fields = ParseCsvLine(line);

                    // ساختارِ schtasks برای هر پوشه یک خطِ تکراریِ هدر چاپ می کند
                    if (fields.Count > 0 && fields[0] == "HostName")
                    {
                        columns.Clear();
                        for (int i = 0; i < fields.Count; i++) columns[fields[i]] = i;
                        continue;
                    }

                    if (columns.Count == 0) continue;

                    string taskPath = GetField(fields, columns, "TaskName");
                    string schedule = GetField(fields, columns, "Schedule Type");
                    string state = GetField(fields, columns, "Scheduled Task State");
                    string action = GetField(fields, columns, "Task To Run");

                    if (string.IsNullOrWhiteSpace(taskPath)) continue;
                    if (!IsStartupTrigger(schedule)) continue;

                    // تسک های خودِ ویندوز نه در Task Manager دیده می شوند و نه جای غیرفعال کردن دارند
                    if (taskPath.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrWhiteSpace(action)) continue;

                    string displayName = taskPath.TrimStart('\\');

                    list.Add(new StartupItem
                    {
                        Id = $"{LocationTask}_{taskPath}",
                        Name = displayName,
                        Command = action,
                        Location = LocationTask,
                        ServiceKey = taskPath,
                        IsScheduledTask = true,
                        // تسک های \Microsoft\ و \Google\ و ... عموماً آپدیتر هستند
                        IsNonEssential = !taskPath.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase),
                        IsDisabled = state.Equals("Disabled", StringComparison.OrdinalIgnoreCase),
                        IsVpnOrProxy = StartupOptimizerService.IsVpnApp(displayName, action),
                        Category = StartupOptimizerService.ResolveStartupCategory(displayName, action, false)
                    });
                }
            }
            catch (Exception ex) { CcLog.Ignore(ex, "Startup.ScheduledTasks"); }
        }

        /// <summary>فقط تریگرهایی که واقعاً «شروعِ ویندوز» را معنا می کنند.</summary>
        private static bool IsStartupTrigger(string scheduleType)
        {
            if (string.IsNullOrWhiteSpace(scheduleType)) return false;
            string s = scheduleType.Trim();

            if (s.StartsWith("At logon", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.StartsWith("At system start", StringComparison.OrdinalIgnoreCase)) return true;

            // نسخه های محلی سازی شده ی خروجی schtasks؛ هر دو حالت «فاصله ساده» و
            // «نیم فاصله (ZWNJ)» ممکن است در خروجی محلی ظاهر شوند
            if (s.Contains("ورود", StringComparison.Ordinal)) return true;
            if (s.Contains("راه اندازی", StringComparison.Ordinal) || s.Contains("راه\u200Cاندازی", StringComparison.Ordinal)) return true;

            return false;
        }

        private static string GetField(List<string> fields, Dictionary<string, int> columns, string name)
        {
            if (!columns.TryGetValue(name, out int idx)) return string.Empty;
            if (idx < 0 || idx >= fields.Count) return string.Empty;
            return fields[idx];
        }

        /// <summary>پارسر CSV با پشتیبانی از نقل قول دوتایی («"») و کاما داخل فیلد.</summary>
        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }

            fields.Add(sb.ToString());
            return fields;
        }

        // ====================================================================
        // اجرای schtasks — مشترک بین اسکن و غیرفعال/فعال سازی
        // ====================================================================

        /// <summary>
        /// schtasks را اجرا می کند. فقط stdout هدایت می شود (نه stderr) تا بافر پر نشود و dead-lock نشود.
        /// </summary>
        public static bool RunSchtasks(string arguments, out string output)
        {
            output = string.Empty;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                // خواندنِ stdout قبل از WaitExit تا اگر خروجی زیاد بود، بافر پر نشود
                output = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(30_000))
                {
                    try { proc.Kill(); } catch { CcLog.Ignore(null, "Schtasks.Kill"); }
                    return false;
                }

                return proc.ExitCode == 0;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Schtasks");
                return false;
            }
        }
    }
}
