// src/CleanC.Core/Services/ExpeditionZonesService.cs
// زون‌های ۲ تا ۷ «اکسپدیشن وینی»: استارتاپ، رجیستری، تسک‌ها، ساکنان سنگین (رم/CPU)، پوشه‌ها، تنظیمات
//
// اصل i18n: این سرویس هیچ رشته‌ی فارسی‌ای برنمی‌گرداند — فقط داده‌ی خام/فنی (مسیر، دستور،
// اعداد). متن فارسی و «مدرک‌ها» در لایه‌ی HTML بازی ساخته می‌شود تا گیت i18n دست نخورد.
//
// قواعد امنیتی:
//   • اجرا (disable/delete) فقط روی شناسه‌هایی که همین سرویس در همان session اسکن کرده ممکن است
//     (کش داخلی) — بازی نمی‌تواند شناسه‌ی دلخواه تزریق کند.
//   • حذف کلید رجیستری همیشه ابتدا backup با reg export می‌گیرد؛ اگر export شکست، حذف لغو می‌شود.
//   • زون procs و settings فقط مشاهده‌ای‌اند (ExecuteZone چیزی اجرا نمی‌کند).
//   • حذف پوشه‌ها فقط سطح اول Downloads و از مسیر SafeDeleteService.DeleteDirectories (سطل بازیافت).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;

namespace CleanC.Core.Services
{
    /// <summary>پرونده‌ی عمومی زون‌های غیر-Downloads. Data فقط داده‌ی فنی حمل می‌کند.</summary>
    public sealed class ExpeditionGenericCase
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime LastWriteUtc { get; set; }
        /// <summary>startup | registry | tasks | procs | folders | settings</summary>
        public string Kind { get; set; } = string.Empty;
        public int Confidence { get; set; }
        /// <summary>متن فنی (مسیر/دستور) — انگلیسی/فنی، بدون متن بازی.</summary>
        public string? Detail { get; set; }
        public Dictionary<string, object?> Data { get; } = new();
    }

    /// <summary>خروجی اسکن زون‌های عمومی.</summary>
    public sealed class ExpeditionGenericScan
    {
        public string ZoneId { get; set; } = string.Empty;
        public string Dir { get; set; } = string.Empty;
        public int ScannedCount { get; set; }
        public List<ExpeditionGenericCase> Cases { get; } = new();
    }

    public static class ExpeditionZonesService
    {
        // ── کش session اسکن → اجرا (بازی فقط شناسه‌های اسکن‌شده را می‌تواند اجرا کند) ──
        private static readonly Dictionary<string, StartupItem> StartupCache = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> TaskCache = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, RegTarget> RegCache = new(StringComparer.Ordinal);

        private sealed class RegTarget
        {
            public string Hive = "";      // HKCU | HKLM
            public string KeyPath = "";   // مسیر کامل بدون hive
            public string? ValueName;     // null = کلیدِ کامل، مقدار = حذف یک value (MuiCache)
        }

        private static string DataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CleanC", "exped");

        // ================================================================
        // SCAN
        // ================================================================

        public static ExpeditionGenericScan ScanZone(string zoneId)
        {
            var scan = new ExpeditionGenericScan { ZoneId = zoneId };
            try
            {
                switch (zoneId)
                {
                    case "startup": FillStartup(scan); break;
                    case "registry": FillRegistry(scan); break;
                    case "tasks": FillTasks(scan); break;
                    case "procs": FillProcs(scan); break;
                    case "folders": FillFolders(scan); break;
                    case "settings": FillSettings(scan); break;
                }
            }
            catch (Exception ex)
            {
                CcLog.Report("Expedition.Zones.Scan." + zoneId, ex);
            }
            return scan;
        }

        // ── Zone: استارتاپ ──

        private static void FillStartup(ExpeditionGenericScan scan)
        {
            List<StartupItem> items;
            try { items = StartupOptimizerService.GetStartupItems(true); }
            catch (Exception ex) { CcLog.Ignore(ex, "Expedition.Startup"); return; }

            foreach (var it in items.Where(i => !i.IsDisabled)
                                     .OrderByDescending(i => i.ImpactScore)
                                     .ThenByDescending(i => i.ImpactMs)
                                     .Take(24))
            {
                int conf = it.TargetMissing ? 3 : (it.ImpactMs >= 400 || it.IsNonEssential ? 2 : 1);
                var id = "startup:" + it.Id;
                StartupCache[id] = it;
                scan.Cases.Add(new ExpeditionGenericCase
                {
                    Path = id,
                    Name = it.Name,
                    Kind = "startup",
                    Confidence = conf,
                    Detail = it.Command,
                    Data =
                    {
                        ["location"] = it.Location,
                        ["impactMs"] = it.ImpactMs,
                        ["impactMeasured"] = it.ImpactMeasured,
                        ["targetMissing"] = it.TargetMissing,
                        ["scheduled"] = it.IsScheduledTask
                    }
                });
            }
            scan.ScannedCount = items.Count;
        }

        // ── Zone: رجیستری (سبک — فقط دو الگوی شناخته‌شده) ──

        private static void FillRegistry(ExpeditionGenericScan scan)
        {
            // ۱) MuiCache: ورودی‌هایی که فایل مقصدشان دیگر وجود ندارد
            try
            {
                const string mui = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
                using var k = Registry.CurrentUser.OpenSubKey(mui);
                if (k != null)
                {
                    int added = 0;
                    foreach (var valName in k.GetValueNames())
                    {
                        if (added >= 10) break;
                        var exe = ExtractMuiExePath(valName);
                        if (exe.Length == 0 || File.Exists(exe)) continue;
                        var id = "reg:mui|" + valName;
                        RegCache[id] = new RegTarget { Hive = "HKCU", KeyPath = mui, ValueName = valName };
                        scan.Cases.Add(new ExpeditionGenericCase
                        {
                            Path = id,
                            Name = Path.GetFileName(exe),
                            Kind = "registry",
                            Confidence = 2,
                            Detail = exe,
                            Data = { ["regPath"] = "HKCU\\" + mui, ["valueName"] = valName, ["target"] = exe }
                        });
                        added++;
                    }
                }
            }
            catch (Exception ex) { CcLog.Ignore(ex, "Expedition.RegMui"); }

            // ۲) کلیدهای Uninstall شکسته: بدون دستور حذف + مسیر نصبِ گمشده
            var roots = new List<(RegistryKey Root, string Hive, string Base)>
            {
                (Registry.LocalMachine, "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.LocalMachine, "HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.CurrentUser, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Uninstall")
            };
            foreach (var (root, hive, basePath) in roots)
            {
                if (scan.Cases.Count >= 14) break;
                try
                {
                    using var bk = root.OpenSubKey(basePath);
                    if (bk == null) continue;
                    int visited = 0;
                    foreach (var sub in bk.GetSubKeyNames())
                    {
                        if (++visited > 300 || scan.Cases.Count >= 14) break;
                        using var sk = bk.OpenSubKey(sub);
                        if (sk == null) continue;
                        var un = (sk.GetValue("UninstallString") as string)?.Trim();
                        var qu = (sk.GetValue("QuietUninstallString") as string)?.Trim();
                        if (!string.IsNullOrEmpty(un) || !string.IsNullOrEmpty(qu)) continue;
                        var loc = (sk.GetValue("InstallLocation") as string)?.Trim().Trim('"') ?? "";
                        if (loc.Length < 4 || Directory.Exists(loc)) continue;
                        var display = (sk.GetValue("DisplayName") as string)?.Trim();
                        var id = "reg:uninstall|" + hive + "|" + basePath + "\\" + sub;
                        RegCache[id] = new RegTarget { Hive = hive, KeyPath = basePath + "\\" + sub };
                        scan.Cases.Add(new ExpeditionGenericCase
                        {
                            Path = id,
                            Name = string.IsNullOrEmpty(display) ? sub : display!,
                            Kind = "registry",
                            Confidence = 2,
                            Detail = loc,
                            Data = { ["regPath"] = hive + "\\" + basePath + "\\" + sub }
                        });
                    }
                }
                catch (Exception ex) { CcLog.Ignore(ex, "Expedition.RegUninstall"); }
            }
        }

        private static string ExtractMuiExePath(string valueName)
        {
            foreach (var suffix in new[] { ".FriendlyAppName", ".ApplicationCompany" })
            {
                if (valueName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return valueName.Substring(0, valueName.Length - suffix.Length);
            }
            return string.Empty;
        }

        // ── Zone: تسک‌های زمان‌بندی (schtasks) ──

        private static void FillTasks(ExpeditionGenericScan scan)
        {
            var (code, rows) = RunCommand("schtasks", "/query /fo csv /nh", 15_000);
            if (code != 0 || rows == null) return;

            // یک‌بار تجزیه‌ی همه‌ی ردیف‌ها: نام، زمان اجرای بعدی، وضعیت
            var parsed = new List<(string Name, string NextRun, string Status)>();
            foreach (var line in rows.Split('\n'))
            {
                var f = SplitCsv(line);
                if (f.Count >= 3 && f[0].StartsWith("\\") && !f[0].StartsWith("\\Microsoft", StringComparison.OrdinalIgnoreCase))
                {
                    if (f[0].Contains("CleanC", StringComparison.OrdinalIgnoreCase)) continue;
                    parsed.Add((f[0], f[1], f[2]));
                }
                if (parsed.Count >= 24) break;
            }

            foreach (var (tn, nextRun, status) in parsed.Take(16))
            {
                string command = "";
                bool targetMissing = false;
                var (xcode, xml) = RunCommand("schtasks", "/query /tn \"" + tn.TrimStart('\\') + "\" /xml", 8_000);
                if (xcode == 0 && xml != null)
                {
                    var m = Regex.Match(xml, "<Command>(.*?)</Command>", RegexOptions.Singleline);
                    if (m.Success)
                    {
                        command = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
                        var exe = FirstToken(command);
                        targetMissing = exe.Length > 0 && !File.Exists(Environment.ExpandEnvironmentVariables(exe));
                    }
                }

                var id = "task:" + tn;
                TaskCache[id] = tn;
                scan.Cases.Add(new ExpeditionGenericCase
                {
                    Path = id,
                    Name = tn.Substring(tn.LastIndexOf('\\') + 1),
                    Kind = "tasks",
                    Confidence = targetMissing ? 3 : 1,
                    Detail = command.Length > 0 ? command : tn,
                    Data = { ["nextRun"] = nextRun, ["status"] = status, ["targetMissing"] = targetMissing }
                });
            }
        }

        private static string FirstToken(string cmd)
        {
            cmd = cmd.Trim();
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                return end > 1 ? cmd.Substring(1, end - 1) : cmd;
            }
            int sp = cmd.IndexOf(' ');
            return sp > 0 ? cmd.Substring(0, sp) : cmd;
        }

        /// <summary>تجزیه‌کننده‌ی CSV تک‌خطی با پشتیبانی از نقل‌قول.</summary>
        private static List<string> SplitCsv(string line)
        {
            var outp = new List<string>();
            if (string.IsNullOrEmpty(line)) return outp;
            var sb = new StringBuilder();
            bool inQ = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inQ)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQ = false;
                    }
                    else sb.Append(ch);
                }
                else if (ch == '"') inQ = true;
                else if (ch == ',') { outp.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            outp.Add(sb.ToString());
            return outp;
        }

        // ── Zone: ساکنان سنگین (پروسه‌ها — فقط مشاهده) ──

        private static void FillProcs(ExpeditionGenericScan scan)
        {
            List<ProcessInfo> top;
            try { top = ProcessManagerService.GetActiveProcesses(14); }
            catch (Exception ex) { CcLog.Ignore(ex, "Expedition.Procs"); return; }

            foreach (var p in top)
            {
                scan.Cases.Add(new ExpeditionGenericCase
                {
                    Path = "proc:" + p.Id,
                    Name = p.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p.Name : p.Name + ".exe",
                    SizeBytes = p.MemoryBytes,
                    Kind = "procs",
                    Confidence = p.MemoryBytes >= 1_000_000_000 ? 2 : 1,
                    Data = { ["pid"] = p.Id, ["critical"] = p.IsCritical }
                });
            }
            scan.ScannedCount = top.Count;
        }

        // ── Zone: پوشه‌ها (فقط سطح اول Downloads) ──

        private static void FillFolders(ExpeditionGenericScan scan)
        {
            string dir = ExpeditionService.DownloadsDir;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            scan.Dir = dir;

            var opts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
                RecurseSubdirectories = true
            };

            var rows = new List<(string Path, long Size, int Files, DateTime Newest, bool Approx)>();
            try
            {
                foreach (var d in new DirectoryInfo(dir).EnumerateDirectories())
                {
                    if (rows.Count >= 20) break;
                    try
                    {
                        long size = 0; int files = 0; var newest = DateTime.MinValue; bool approx = false;
                        foreach (var f in d.EnumerateFiles("*", opts))
                        {
                            files++;
                            size += f.Length;
                            if (f.LastWriteTimeUtc > newest) newest = f.LastWriteTimeUtc;
                            if (files >= 3000) { approx = true; break; }
                        }
                        rows.Add((d.FullName, size, files, newest, approx));
                    }
                    catch (Exception ex) { CcLog.Ignore(ex, "Expedition.Folder"); }
                }
            }
            catch (Exception ex) { CcLog.Ignore(ex, "Expedition.Folders"); return; }

            foreach (var r in rows.OrderByDescending(x => x.Size).Take(16))
            {
                int days = r.Newest == DateTime.MinValue ? 999 : (int)((DateTime.UtcNow - r.Newest).TotalDays);
                int conf = r.Files == 0 ? 3 : (days >= 120 ? 2 : (days >= 45 ? 2 : 1));
                scan.Cases.Add(new ExpeditionGenericCase
                {
                    Path = r.Path,
                    Name = Path.GetFileName(r.Path.TrimEnd('\\')) + "\\",
                    SizeBytes = r.Size,
                    LastWriteUtc = r.Newest,
                    Kind = "folders",
                    Confidence = conf,
                    Data = { ["files"] = r.Files, ["approx"] = r.Approx, ["days"] = days }
                });
            }
            scan.ScannedCount = rows.Count;
        }

        // ── Zone: تنظیمات سنگین (فقط مشاهده) ──

        private static void FillSettings(ExpeditionGenericScan scan)
        {
            // هیبرنیشن: hiberfil.sys روی C:
            try
            {
                var hf = new FileInfo(@"C:\hiberfil.sys");
                if (hf.Exists)
                    scan.Cases.Add(new ExpeditionGenericCase
                    {
                        Path = "setting:hibernate",
                        Name = "hiberfil.sys",
                        SizeBytes = SafeLength(hf),
                        Kind = "settings",
                        Confidence = 2,
                        Detail = "powercfg /hibernate"
                    });
            }
            catch (Exception ex) { CcLog.Ignore(ex, "Expedition.Hiber"); }

            AddServiceSetting(scan, "wsearch", "WSearch");
            AddServiceSetting(scan, "sysmain", "SysMain");

            // شفافیت ویندوز
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Themes\Personalize");
                if (k != null && k.GetValue("EnableTransparency") is int v && v == 1)
                    scan.Cases.Add(new ExpeditionGenericCase
                    {
                        Path = "setting:transparency",
                        Name = "EnableTransparency",
                        Kind = "settings",
                        Confidence = 1,
                        Detail = "Settings > Personalization > Colors"
                    });
            }
            catch (Exception ex) { CcLog.Ignore(ex, "Expedition.Transparency"); }
        }

        private static void AddServiceSetting(ExpeditionGenericScan scan, string key, string service)
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + service);
                if (k == null || k.GetValue("Start") is not int start || start == 4) return;
                scan.Cases.Add(new ExpeditionGenericCase
                {
                    Path = "setting:" + key,
                    Name = service,
                    Kind = "settings",
                    Confidence = 1,
                    Detail = "services.msc > " + service,
                    Data = { ["start"] = start }
                });
            }
            catch (Exception ex) { CcLog.Ignore(ex, "Expedition.ServiceSetting"); }
        }

        private static long SafeLength(FileInfo fi)
        {
            try { return fi.Length; } catch { return 0; }
        }

        // ================================================================
        // EXECUTE
        // ================================================================

        /// <summary>اجرا در پایان گشت. خروجی: (performed, failed, freedBytes).</summary>
        public static (int Performed, int Failed, long FreedBytes) ExecuteZone(string zoneId, List<string> ids)
        {
            int ok = 0, fail = 0; long freed = 0;
            switch (zoneId)
            {
                case "startup":
                    foreach (var id in ids)
                    {
                        if (!StartupCache.TryGetValue(id, out var item)) { fail++; continue; }
                        try { if (StartupOptimizerService.DisableStartupItem(item)) ok++; else fail++; }
                        catch (Exception ex) { CcLog.Report("Expedition.Exec.Startup", ex); fail++; }
                    }
                    break;

                case "tasks":
                    foreach (var id in ids)
                    {
                        if (!TaskCache.TryGetValue(id, out var tn)) { fail++; continue; }
                        var (code, _) = RunCommand("schtasks", "/change /tn \"" + tn.TrimStart('\\') + "\" /disable", 10_000);
                        if (code == 0) ok++; else fail++;
                    }
                    break;

                case "registry":
                    foreach (var id in ids)
                    {
                        if (!RegCache.TryGetValue(id, out var t)) { fail++; continue; }
                        if (DeleteRegTarget(t)) ok++; else fail++;
                    }
                    break;

                case "folders":
                    // مسیر واقعی پوشه (drive:\…) — فقط شناسه‌های با قالب مسیر درایو پذیرفته می‌شوند
                    var realPaths = ids.Where(p => p.Length > 3 && p[1] == ':' && (p[2] == '\\' || p[2] == '/')).ToList();
                    try
                    {
                        var batch = SafeDeleteService.DeleteDirectories(realPaths);
                        ok = batch.SucceededCount; fail = batch.FailedCount; freed = batch.MovedBytes + batch.FreedBytes;
                    }
                    catch (Exception ex) { CcLog.Report("Expedition.Exec.Folders", ex); fail += realPaths.Count; }
                    break;

                // procs / settings: مشاهده‌ای — چیزی اجرا نمی‌شود
                default:
                    break;
            }
            return (ok, fail, freed);
        }

        /// <summary>حذف امنِ هدف رجیستری: اول backup با reg export، بعد حذف. اگر export شکست → حذف نمی‌شود.</summary>
        private static bool DeleteRegTarget(RegTarget t)
        {
            try
            {
                // گارد مسیر: فقط دو الگوی شناخته‌شده‌ی همین سرویس مجازند
                bool okPrefix =
                    (t.ValueName != null && t.KeyPath.EndsWith(@"Windows\Shell\MuiCache", StringComparison.OrdinalIgnoreCase)) ||
                    t.KeyPath.EndsWith(@"\Uninstall", StringComparison.OrdinalIgnoreCase) ||
                    t.KeyPath.Contains(@"\Uninstall\", StringComparison.OrdinalIgnoreCase) ||
                    t.KeyPath.Contains(@"\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", StringComparison.OrdinalIgnoreCase);
                if (!okPrefix) return false;

                string backupDir = Path.Combine(DataDir, "reg-backups");
                Directory.CreateDirectory(backupDir);
                string safe = Regex.Replace(t.Hive + t.KeyPath, "[^A-Za-z0-9._-]", "_");
                string backup = Path.Combine(backupDir, safe.Substring(0, Math.Min(safe.Length, 120)) + ".reg");
                var (exCode, _) = RunCommand("reg", "export \"" + t.Hive + "\\" + t.KeyPath + "\" \"" + backup + "\" /y", 10_000);
                if (exCode != 0)
                    return false; // بدون backup حذف ممنوع

                using var root = t.Hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                var parentPath = t.KeyPath.Contains('\\') ? t.KeyPath.Substring(0, t.KeyPath.LastIndexOf('\\')) : t.KeyPath;
                var leaf = t.KeyPath.Substring(t.KeyPath.LastIndexOf('\\') + 1);
                using var parent = root.OpenSubKey(parentPath, writable: true);
                if (parent == null) return false;

                if (t.ValueName != null)
                {
                    parent.DeleteValue(t.ValueName, throwOnMissingValue: false);
                }
                else
                {
                    parent.DeleteSubKeyTree(leaf, throwOnMissingSubKey: false);
                }
                return true;
            }
            catch (Exception ex)
            {
                CcLog.Report("Expedition.Exec.Reg", ex);
                return false;
            }
        }

        // ================================================================
        // file.inspect — بازجویی از یک فایل
        // ================================================================

        /// <summary>
        /// بازجویی: منبع دانلود (Zone.Identifier ADS)، تاریخ ذخیره، محتوای zip و پیش‌نمایش متن.
        /// خروجی data-only است؛ روایت در HTML ساخته می‌شود.
        /// </summary>
        public static object? InspectFile(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return null;

                string? host = null, referrer = null; DateTime? downloaded = null;
                try
                {
                    using var ads = File.OpenRead(path + ":Zone.Identifier");
                    using var sr = new StreamReader(ads);
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (host == null && line.StartsWith("HostUrl=", StringComparison.Ordinal)) host = line.Substring(8).Trim();
                        else if (referrer == null && line.StartsWith("ReferrerUrl=", StringComparison.Ordinal)) referrer = line.Substring(12).Trim();
                        else if (downloaded == null && line.StartsWith("DownloadTime=", StringComparison.Ordinal) &&
                                 long.TryParse(line.Substring(13).Trim(), System.Globalization.NumberStyles.HexNumber, null, out var ft))
                            downloaded = DateTime.FromFileTimeUtc(ft);
                    }
                }
                catch { /* بدون ADS = فایل محلی/کپی‌شده */ }

                var ext = fi.Extension.ToLowerInvariant();
                object? result;

                if (ext == ".zip")
                {
                    long uncompressed = 0; int exeCount = 0; DateTime oldest = DateTime.MaxValue, newest = DateTime.MinValue;
                    List<object> top;
                    int count;
                    using (var za = ZipFile.OpenRead(path))
                    {
                        var entries = za.Entries.Where(e => e.FullName.Length > 0 && !e.FullName.EndsWith("/")).ToList();
                        count = entries.Count;
                        foreach (var e in entries)
                        {
                            uncompressed += e.Length;
                            if (e.LastWriteTime.DateTime < oldest) oldest = e.LastWriteTime.DateTime;
                            if (e.LastWriteTime.DateTime > newest) newest = e.LastWriteTime.DateTime;
                            var en = e.Name;
                            if (en.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                en.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                                en.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                                en.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)) exeCount++;
                        }
                        top = entries.OrderByDescending(e => e.Length).Take(12)
                            .Select(e => (object)new { n = e.FullName, s = e.Length }).ToList();
                    }
                    result = new
                    {
                        host, referrer,
                        created = fi.CreationTimeUtc,
                        downloaded,
                        entryCount = count,
                        uncompressed,
                        exeCount,
                        oldestEntry = oldest == DateTime.MaxValue ? null : (DateTime?)oldest.ToUniversalTime(),
                        newestEntry = newest == DateTime.MinValue ? null : (DateTime?)newest.ToUniversalTime(),
                        entries = top
                    };
                }
                else if (ext is ".rar" or ".7z" or ".tar" or ".gz" or ".tgz")
                {
                    result = new { host, referrer, created = fi.CreationTimeUtc, downloaded, archiveUnsupported = true };
                }
                else if (ext is ".txt" or ".log" or ".md" or ".csv" or ".json" or ".ini" or ".nfo" or ".srt")
                {
                    string? text = null;
                    try
                    {
                        using var fs = File.OpenRead(path);
                        var buf = new byte[2048];
                        int n = fs.Read(buf, 0, buf.Length);
                        text = Encoding.UTF8.GetString(buf, 0, n);
                    }
                    catch { }
                    result = new { host, referrer, created = fi.CreationTimeUtc, downloaded, text };
                }
                else
                {
                    result = new { host, referrer, created = fi.CreationTimeUtc, downloaded };
                }
                return result;
            }
            catch (Exception ex)
            {
                CcLog.Report("Expedition.Inspect", ex);
                return null;
            }
        }

        // ================================================================
        // file.stage — آماده‌سازی پیش‌نمایش رسانه‌ای
        // ================================================================

        /// <summary>
        /// فایل‌های داخل ریشه‌ی Downloads مستقیم از cleanc.dl سرو می‌شوند؛ بقیه یک‌بار به
        /// پوشه‌ی داده‌ی اکسپدیشن کپی می‌شوند و از cleanc.wall سرو می‌شوند (سقف حجم دارد).
        /// </summary>
        public static object? StageFile(string path, long sizeBytes)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return new { tooBig = false, failed = true };

                string downloads = ExpeditionService.DownloadsDir;
                if (!string.IsNullOrEmpty(downloads) &&
                    path.StartsWith(downloads + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    path.AsSpan(downloads.Length + 1).IndexOf('\\') < 0)
                {
                    return new { direct = true, url = "https://cleanc.dl/" + Uri.EscapeDataString(fi.Name) };
                }

                if (sizeBytes <= 0) sizeBytes = fi.Length;
                if (sizeBytes > 800_000_000) return new { tooBig = true };

                var ext = fi.Extension.ToLowerInvariant();
                if (ext.Length is < 1 or > 6 || !Regex.IsMatch(ext, @"^\.[a-z0-9]+$")) ext = ".bin";
                Directory.CreateDirectory(DataDir);
                string dest = Path.Combine(DataDir, "preview" + ext);
                File.Copy(path, dest, overwrite: true);
                return new { url = "https://cleanc.wall/preview" + ext };
            }
            catch (Exception ex)
            {
                CcLog.Report("Expedition.Stage", ex);
                return new { failed = true };
            }
        }

        // ================================================================
        // sys.stat — نبض سیستم
        // ================================================================

        public static object GetSysStat()
        {
            try
            {
                var s = SystemStatsService.GetCurrentStats();
                return new { cpu = (int)Math.Round(s.CpuPercent), ram = (int)Math.Round(s.RamPercent), disk = (int)Math.Round(s.DiskPercent) };
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Expedition.SysStat");
                return new { cpu = 0, ram = 0, disk = 0 };
            }
        }

        // ================================================================
        // ابزار
        // ================================================================

        /// <summary>اجرای یک دستور کمکی؛ خروجی: (ExitCode، متن خروجی). متن محلی‌شده هرگز معیار تصمیم نیست — فقط ExitCode.</summary>
        private static (int ExitCode, string? Output) RunCommand(string fileName, string args, int timeoutMs)
        {
            try
            {
                using var psi = new Process();
                psi.StartInfo = new ProcessStartInfo(fileName, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.Start();
                if (!psi.WaitForExit(timeoutMs)) { try { psi.Kill(); } catch { } return (-1, null); }
                var o = psi.StandardOutput.ReadToEnd();
                var e = psi.StandardError.ReadToEnd();
                return (psi.ExitCode, string.IsNullOrEmpty(o) ? e : o + e);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Expedition.Cmd");
                return (-1, null);
            }
        }
    }
}
