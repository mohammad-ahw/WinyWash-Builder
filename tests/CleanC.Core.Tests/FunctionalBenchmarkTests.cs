// tests/CleanC.Core.Tests/FunctionalBenchmarkTests.cs
// بنچمارک کارکرد v1.1.0 — لیست مرجع ~۷۰ موردی کاربر (تخمین ۱۸–۲۵GB).
// فقط با CLEANC_BENCHMARK=1 اجرا می شود؛ در اجرای عادی فوراً return می کند.
//
// اجرا:
//   $env:CLEANC_BENCHMARK=1
//   dotnet test CleanC.sln --filter FunctionalBenchmark -v q > %TEMP%\cleanc_benchmark.log 2>&1
//
// گزارش: برای هر آیتم لیست کاربر → پیدا شد / نشد + حجم و مدرک.
// قواعد سخت (assert): موارد «قفل شده» هرگز نباید پیشنهاد حذف/غیرفعال‌سازی بگیرند.
using CleanC.Core.Models;
using CleanC.Core.Services;
using System.Text;
using Xunit.Abstractions;

namespace CleanC.Core.Tests
{
    public class FunctionalBenchmarkTests
    {
        // خروجی همزمان روی ITestOutputHelper و فایل UTF-8 می رود؛ چون کنسول PowerShell 5.1
        // متن فارسی را به ???? تبدیل می کند و گزارش باید خوانا بماند.
        private readonly TextWriter _out;
        private static bool Enabled => Environment.GetEnvironmentVariable("CLEANC_BENCHMARK") == "1";

        public FunctionalBenchmarkTests(ITestOutputHelper output)
        {
            if (Enabled)
            {
                string path = Environment.GetEnvironmentVariable("CLEANC_BENCHMARK_OUT") is { Length: > 0 } custom
                    ? custom
                    : Path.Combine(Path.GetTempPath(), "cleanc_benchmark.log");
                _out = new MirrorOutputWriter(output, path);
            }
            else
            {
                _out = new TestOutputAdapter(output);
            }
        }

        private static bool Has(string? haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

        private sealed record Probe(string Section, string Label, bool Found, long Bytes, string Detail);

        [Fact]
        public async Task RunFunctionalBenchmark_AgainstUserCleanupList()
        {
            if (!Enabled)
            {
                _out.WriteLine("CLEANC_BENCHMARK=1 ست نیست → بنچمارک اجرا نشد (رد شد).");
                return;
            }

            // وضعیت دسترسی مهم است: بخشی از اسکن ها (Prefetch، LiveKernelReports، DriverStore) بدون ادمین خالی برمی گردند.
            bool isAdmin;
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                isAdmin = new System.Security.Principal.WindowsPrincipal(identity)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            _out.WriteLine($"=== BENCHMARK | Admin={isAdmin} | {DateTime.Now:yyyy-MM-dd HH:mm} ===");

            // ── جمع آوری داده از همه ی منابع تشخیص ──
            DeepStorageScanResult deep;
            ScanResult sys;
            try
            {
                deep = await DeepStorageCleanService.ScanDeepStorageAsync(p => _out.WriteLine("  · " + p));
            }
            catch (Exception ex)
            {
                _out.WriteLine("DeepStorage scan failed: " + ex.Message);
                return;
            }

            try
            {
                sys = await SystemScanService.ScanAsync(p => _out.WriteLine("  · " + p));
            }
            catch (Exception ex)
            {
                _out.WriteLine("SystemScan failed: " + ex.Message);
                return;
            }

            var allJunkItems = sys.Groups.SelectMany(g => g.Items).ToList(); // بدون فیلتر حجم — برای probe های «وجود» (مثل سطل بازیافت خالی)
            var junkItems = allJunkItems.Where(i => i.Bytes > 0).ToList();
            var (drivers, driverStats) = await DriverManagerService.GetDriversAsync();
            var startup = StartupOptimizerService.GetStartupItems(includeExtraSources: true);
            var bloat = await BloatwareService.ScanCandidateAppsAsync();
            var borderline = await BloatwareService.ScanBorderlineReviewAppsAsync();
            var apps = await AppUninstallerService.GetInstalledAppsAsync();
            var orphanServices = RegistryCleanerService.ScanOrphanedServices();
            var leftovers = LeftoverCleanerService.ScanLeftoverFolders();

            _out.WriteLine($"داده ها: junk={junkItems.Count} mess={deep.MessItems.Count} dupGroups={deep.DuplicateGroups.Count} " +
                           $"emptyFolders={deep.EmptyFolders.Count} drivers={drivers.Count} startup={startup.Count} " +
                           $"bloat={bloat.Count} borderline={borderline.Count} apps={apps.Count} leftovers={leftovers.Count}");

            // ── دامپ خام کامل — مرجع تشخیص گپ ها (برای هر ابهام، اینجا مسیر/حجم/مدرک دیده می شود) ──
            _out.WriteLine("");
            _out.WriteLine("================= [DUMP] Junk — تمام گروه ها =================");
            foreach (var group in sys.Groups)
            {
                _out.WriteLine($"[group] {group.Title} | کل: {group.FormattedTotal}");
                foreach (var item in group.Items)
                {
                    _out.WriteLine($"    - {item.Title} | {item.Path} | {item.FormattedBytes} | {(item.RequiresElevation ? "elev" : "")} | targets={string.Join("; ", item.TargetPaths.Take(4))}");
                }
            }

            _out.WriteLine("");
            _out.WriteLine("================= [DUMP] EmptyFolders =================");
            foreach (var f in deep.EmptyFolders)
            {
                _out.WriteLine($"    - {f.FolderPath} | sel={f.IsSelected} | admin={f.RequiresAdmin} | {f.Evidence}");
            }

            _out.WriteLine("");
            _out.WriteLine("================= [DUMP] DuplicateGroups — تمام گروه ها =================");
            foreach (var g in deep.DuplicateGroups.OrderByDescending(g => g.WastedSizeBytes))
            {
                _out.WriteLine($"[dup] {g.SampleName} ×{g.Files.Count} | هر نسخه: {g.FormattedSingleSize} | اتلاف: {g.FormattedWastedSize}");
                foreach (var f in g.Files.Take(6))
                {
                    _out.WriteLine($"    - {(f.IsOriginal ? "[اصل] " : "")}{f.FilePath}");
                }
            }

            _out.WriteLine("");
            _out.WriteLine("================= [DUMP] Mess — تمام آیتم ها =================");
            foreach (var m in deep.MessItems.OrderBy(m => m.Category).ThenByDescending(m => m.SizeBytes))
            {
                _out.WriteLine($"    [{m.Category}] sel={m.IsSelected} | {m.FilePath} | {m.FormattedSize} | {Truncate(m.Evidence, 90)}");
            }

            _out.WriteLine("");
            _out.WriteLine("================= [DUMP] Drivers — تمام بسته ها =================");
            foreach (var d in drivers.OrderBy(d => d.ProviderName))
            {
                _out.WriteLine($"    - {d.PublishedName} | orig={d.OriginalName} | prov={d.ProviderName} | orphan={d.IsOrphanNoHardware} | {Truncate(d.Evidence, 70)}");
            }

            _out.WriteLine("");
            _out.WriteLine("================= [DUMP] Bloatware + Borderline =================");
            foreach (var a in bloat)
            {
                _out.WriteLine($"    [candidate] {a.DisplayName} | {a.FormattedSize} | provisioned={a.IsProvisioned} | {a.CategoryType} | {Truncate(a.Description, 80)}");
            }
            foreach (var b in borderline)
            {
                _out.WriteLine($"    [borderline] {b.DisplayName} | {b.CategoryLabel} | {b.FormattedSize}");
            }

            _out.WriteLine("");
            _out.WriteLine("================= [DUMP] Apps — Uninstaller (کامپوننت ها مشخص شده اند) =================");
            foreach (var a in apps.OrderBy(a => a.DisplayName))
            {
                _out.WriteLine($"    - {a.DisplayName} | v{a.DisplayVersion} | {a.FormattedSize} | comp={a.IsSystemComponent} | trial={a.IsTrialBadge} | store={a.IsStoreApp}");
            }

            _out.WriteLine("");
            _out.WriteLine("================= [DUMP] Startup — تمام آیتم ها =================");
            foreach (var s in startup)
            {
                _out.WriteLine($"    - {s.Name} | missing={s.TargetMissing} | {Truncate(s.Command, 100)}");
            }

            _out.WriteLine("");
            _out.WriteLine("================= [DUMP] Leftovers + OrphanServices =================");
            foreach (var l in leftovers)
            {
                _out.WriteLine($"    [leftover] {l.LocationType} | {l.FullPath} | {SystemStats.FormatBytes(l.SizeBytes)}");
            }
            foreach (var s in orphanServices)
            {
                _out.WriteLine($"    [orphan-svc] {s.ServiceName} ({s.DisplayName}) → {s.MissingImagePath}");
            }

            // ── بررسی مستقیم دیسک برای مواردی که سرویس ها ممکن است نبینند ──
            _out.WriteLine("");
            _out.WriteLine("================= [DUMP] FS probes =================");
            DumpIfExists("C:\\Program Files\\ENE");
            DumpIfExists("C:\\Program Files (x86)\\ENE");
            DumpIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bun"));
            DumpIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads\\Coucou-Windows.msi"));
            try
            {
                string repo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "DriverStore", "FileRepository");
                if (Directory.Exists(repo))
                {
                    var hits = Directory.EnumerateDirectories(repo)
                        .Where(d => Has(d, "ENE") || Has(d, "WD_BLACK") || Has(d, "wdc") || Has(d, "TbtLegacy") ||
                                    Has(d, "dch_setup") || Has(d, "intel") && Has(d, "legacy"))
                        .Take(30).ToList();
                    _out.WriteLine($"    [driverstore] FileRepository قابل خواندن است | تطبیق ها: {hits.Count}");
                    foreach (var h in hits) _out.WriteLine($"        - {Path.GetFileName(h)}");
                }
                else
                {
                    _out.WriteLine("    [driverstore] FileRepository خوانده نشد (بدون ادمین؟)");
                }
            }
            catch (Exception ex)
            {
                _out.WriteLine("    [driverstore] خطا: " + ex.Message);
            }

            string Truncate(string? s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");

            void DumpIfExists(string path)
            {
                try
                {
                    if (Directory.Exists(path)) _out.WriteLine($"    [exists] {path} (پوشه)");
                    else if (File.Exists(path)) _out.WriteLine($"    [exists] {path} ({new FileInfo(path).Length / 1024.0 / 1024.0:0.0} MB)");
                    else _out.WriteLine($"    [missing] {path}");
                }
                catch (Exception ex)
                {
                    _out.WriteLine($"    [error] {path}: {ex.Message}");
                }
            }

            // ── ابزارهای probe ──
            var messHits = deep.MessItems
                .Where(m => true)
                .ToList();

            bool Mess(string needle) => messHits.Any(m => Has(m.FilePath, needle) || Has(m.Evidence, needle));
            long MessSize(string needle) => messHits.Where(m => Has(m.FilePath, needle) || Has(m.Evidence, needle)).Sum(m => m.SizeBytes);
            string MessEvidence(string needle) => messHits.FirstOrDefault(m => Has(m.FilePath, needle) || Has(m.Evidence, needle))?.Evidence ?? "";

            bool Junk(string needle) => junkItems.Any(i => Has(i.Path, needle) || Has(i.Title, needle) || i.TargetPaths.Any(t => Has(t, needle)));
            long JunkSize(string needle) => junkItems.Where(i => Has(i.Path, needle) || Has(i.Title, needle) || i.TargetPaths.Any(t => Has(t, needle))).Sum(i => i.Bytes);
            // «وجود آیتم» بدون فیلتر حجم — سطل بازیافت خالی هم باید دیده شود
            bool JunkExists(string needle) => allJunkItems.Any(i => Has(i.Path, needle) || Has(i.Title, needle) || i.TargetPaths.Any(t => Has(t, needle)));

            bool OldInstaller(string needle) => messHits.Any(m =>
                m.Category == StorageMessCategory.OldInstaller && (Has(m.FilePath, needle) || Has(m.Evidence, needle)));

            bool Dup(string needle) => deep.DuplicateGroups.Any(g =>
                Has(g.SampleName, needle) || g.Files.Any(f => Has(f.FilePath, needle)));
            int DupGroups(string needle) => deep.DuplicateGroups.Count(g =>
                Has(g.SampleName, needle) || g.Files.Any(f => Has(f.FilePath, needle)));

            bool EmptyFolder(string needle) => deep.EmptyFolders.Any(f => Has(f.FolderPath, needle));

            // تطبیق دقیق مسیر پوشه (برای پوشه های ریشه و قفل شده ها؛ substring اینجا گمراه کننده است)
            static string NormPath(string p) => p.TrimEnd('\\', '/');
            bool EmptyFolderExact(string full) => deep.EmptyFolders.Any(f =>
                string.Equals(NormPath(f.FolderPath), NormPath(full), StringComparison.OrdinalIgnoreCase));

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            int DriverOrphans(string needle) => drivers.Count(d => d.IsOrphanNoHardware &&
                (Has(d.ProviderName, needle) || Has(d.OriginalName, needle) || Has(d.PublishedName, needle)));

            bool Startup(string needle) => startup.Any(s => Has(s.Command, needle) || Has(s.Name, needle));
            int StartupCount(string needle) => startup.Count(s => Has(s.Command, needle) || Has(s.Name, needle));

            bool Bloat(string needle) => bloat.Any(a => Has(a.DisplayName, needle));
            int BloatCount(string needle) => bloat.Count(a => Has(a.DisplayName, needle));
            bool Borderline(string needle) => borderline.Any(a => Has(a.DisplayName, needle));

            bool AppInstalled(string needle) => apps.Any(a => Has(a.DisplayName, needle));
            bool Leftover(string needle) => leftovers.Any(l => Has(l.FullPath, needle));

            var probes = new List<Probe>();

            void P(string section, string label, bool found, long bytes = 0, string detail = "")
                => probes.Add(new Probe(section, label, found, bytes, detail));

            // ================= A) فایل های تکراری =================
            P("A", "NVIDIA — نصاب های تکراری", Dup("NVIDIA"), 0, $"{DupGroups("NVIDIA")} گروه");
            P("A", "ChatGPT — عکس تکراری", Dup("ChatGPT"), 0, $"{DupGroups("ChatGPT")} گروه");
            P("A", "Rufus ×3", Dup("Rufus") || OldInstaller("Rufus"), 0,
                $"{DupGroups("Rufus")} گروه dup | نصاب قدیمی: {(OldInstaller("Rufus") ? "بله" : "خیر")} (سه معماری متفاوت — تکراری نیست)");
            P("A", "cursor-rtl.vsix ×2", Dup("cursor-rtl") || OldInstaller("cursor-rtl"), 0,
                $"{DupGroups("cursor-rtl")} گروه dup | نصاب قدیمی (.vsix): {(OldInstaller("cursor-rtl") ? "بله" : "خیر")} (دو فایل متفاوت اند)");

            // ================= B) زباله ی سیستمی =================
            P("B", "LiveKernelReports", Junk("LiveKernelReports"), JunkSize("LiveKernelReports"));
            P("B", @"C:\NVIDIA (ریشه)", Mess(@"C:\NVIDIA"), MessSize(@"C:\NVIDIA"), MessEvidence(@"C:\NVIDIA"));
            P("B", @"C:\AOMEI + AMTAG.BIN", Mess("AOMEI") || Mess("AMTAG"), MessSize("AOMEI") + MessSize("AMTAG"), MessEvidence("AOMEI"));
            P("B", "پوشه خالی Intel ریشه", EmptyFolderExact(@"C:\Intel"));
            P("B", "پوشه خالی MSI ریشه", EmptyFolderExact(@"C:\MSI"));
            P("B", "پوشه خالی PerfLogs ریشه", EmptyFolderExact(@"C:\PerfLogs"));
            P("B", "پوشه خالی XboxGames ریشه", EmptyFolderExact(@"C:\XboxGames"));
            P("B", @"C:\AomeiPartAssistantPE (لاگ های نصاب)", Mess("AomeiPartAssistantPE"), MessSize("AomeiPartAssistantPE"), MessEvidence("AomeiPartAssistantPE"));
            P("B", @"C:\User Manual", Mess("User Manual"), MessSize("User Manual"), MessEvidence("User Manual"));
            P("B", @"C:\Lock Screen", Mess("Lock Screen") || Junk("Lock Screen"), MessSize("Lock Screen") + JunkSize("Lock Screen"));
            P("B", "WER (گزارش خطا)", Junk("WER") || Junk("Error Reporting"), JunkSize("WER") + JunkSize("Error Reporting"));
            P("B", "سطل بازیافت (وجود آیتم — حتی خالی)", JunkExists("Recycle"), JunkSize("Recycle"));
            P("B", "Prefetch", Junk("Prefetch"), JunkSize("Prefetch"));
            P("B", @"Downloads\edata", EmptyFolder("edata") || Mess("edata"), MessSize("edata"));
            P("B", "skydrive (بازمانده)", Mess("skydrive") || EmptyFolder("skydrive") || Leftover("skydrive"), MessSize("skydrive"));
            P("B", "Tiny11 / stub بازمانده", Mess("Tiny11") || Mess("stub"));
            P("B", "فایل قفل ~$ دفتر", Mess("~$"));
            P("B", "Battle.net بازمانده ها", Leftover("Battle.net") || Mess("Battle.net"), MessSize("Battle.net"));

            // ================= C) آپدیترها (D04) =================
            P("C", "LM Studio updater", Mess("LM Studio") || Mess("lm-studio"), MessSize("LM Studio"));
            P("C", "AntiGravity updater", Mess("antigravity"), MessSize("antigravity"));
            P("C", "codebuff updater", Mess("codebuff"), MessSize("codebuff"));
            P("C", "opencode updater", Mess("opencode"), MessSize("opencode"));

            // ================= D) نصاب ها =================
            var programInstallers = messHits.Where(m =>
                m.Category == StorageMessCategory.OldInstaller &&
                Has(m.DirectoryPath, @"Downloads\Programs")).ToList();
            P("D", @"Downloads\Programs — نصاب ها", programInstallers.Count > 0, programInstallers.Sum(m => m.SizeBytes), $"{programInstallers.Count} مورد");
            P("D", "tailscale نصاب", Mess("tailscale"), MessSize("tailscale"));
            P("D", "Intelligent Terminal نصاب", Mess("Intelligent Terminal"), MessSize("Intelligent Terminal"));
            P("D", "Coucou-Windows.msi", Mess("Coucou"), MessSize("Coucou"));
            P("D", @"O:\Apps آرشیو", Mess(@"O:\Apps"), MessSize(@"O:\Apps"), "خارج از محدوده ی اسکن پروفایل است");

            // ================= E) کش ها =================
            P("E", "Chrome cache", Junk("Chrome") || Mess("Chrome"), JunkSize("Chrome") + MessSize("Chrome"));
            P("E", "Edge cache", Junk("Edge") || Mess("Edge"), JunkSize("Edge") + MessSize("Edge"));
            P("E", "NuGet cache", Junk("NuGet") || Leftover("NuGet") || Mess("NuGet"), JunkSize("NuGet") + MessSize("NuGet"));
            P("E", "Bun cache", Junk("bun") || Mess("bun") || Leftover("bun"), JunkSize("bun") + MessSize("bun"));
            P("E", "Gradle cache", Junk("Gradle") || Mess("Gradle") || Leftover("Gradle"), JunkSize("Gradle") + MessSize("Gradle"));
            P("E", ".cache", Leftover(".cache") || Mess(".cache"), MessSize(".cache"));

            // ================= F) bloatware =================
            P("F", "WSA", Bloat("Windows Subsystem") || Bloat("Amazon Appstore") || Bloat("WSA"));
            P("F", "Power Automate", Bloat("Power Automate") || Borderline("Power Automate"));
            P("F", "CyberLink", Bloat("CyberLink") || Borderline("CyberLink"), 0, $"{BloatCount("CyberLink")} مورد");
            P("F", "Provisioned apps", bloat.Any(a => a.IsProvisioned), 0, $"{bloat.Count(a => a.IsProvisioned)} بسته");

            // ================= G) نرم افزار قدیمی / درایور =================
            P("G", "HWiNFO64 (ابزار کاربر — عمداً پیشنهاد نمی شود)", AppInstalled("HWiNFO") && (Bloat("HWiNFO") || Borderline("HWiNFO")), 0,
                AppInstalled("HWiNFO") ? "نصب است — طبق تصمیم جزو لیست پیشنهادها نیست (ابزار کاربر)" : "نصب نیست");
            P("G", "Universal Adb Driver", AppInstalled("Adb Driver") && (Bloat("Adb Driver") || Borderline("Adb Driver")),
                0, AppInstalled("Adb Driver") ? "نصب است" : "نصب نیست");
            P("G", "#59 dch_setup + TbtLegacyPlug (کامپوننت)", apps.Any(a => a.IsSystemComponent && (Has(a.DisplayName, "dch_setup") || Has(a.DisplayName, "TbtLegacy"))), 0,
                $"dch={apps.Count(a => a.IsSystemComponent && Has(a.DisplayName, "dch_setup"))} | Tbt={apps.Count(a => a.IsSystemComponent && Has(a.DisplayName, "TbtLegacy"))}");
            P("G", "#60 WD_BLACK + ENE (کامپوننت)", apps.Any(a => a.IsSystemComponent && (Has(a.DisplayName, "WD_BLACK") || Has(a.DisplayName, "ENE"))), 0,
                $"ENE={apps.Count(a => a.IsSystemComponent && Has(a.DisplayName, "ENE"))} | WD={apps.Count(a => a.IsSystemComponent && Has(a.DisplayName, "WD_BLACK"))}");
            P("G", "PC Health Check", AppInstalled("PC Health") && (Bloat("PC Health") || Borderline("PC Health")), 0,
                AppInstalled("PC Health") ? "نصب است" : "نصب نیست");
            P("G", "ManyCam بازمانده", Leftover("ManyCam") || Mess("ManyCam"), MessSize("ManyCam"));
            P("G", @"C:\Program Files\ENE + WD_BLACK (کامپوننت)", apps.Any(a => a.IsSystemComponent), 0,
                $"کامپوننت نمایان: {apps.Count(a => a.IsSystemComponent)}" +
                $" | ENE={apps.Count(a => a.IsSystemComponent && Has(a.DisplayName, "ENE"))}" +
                $" | WD={apps.Count(a => a.IsSystemComponent && Has(a.DisplayName, "WD_BLACK"))}" +
                $" | dch={apps.Count(a => a.IsSystemComponent && Has(a.DisplayName, "dch_setup"))}" +
                $" | Tbt={apps.Count(a => a.IsSystemComponent && Has(a.DisplayName, "TbtLegacy"))}");
            P("G", "ansel (بازمانده NVIDIA)", Mess("ansel") || Leftover("ansel"), MessSize("ansel"));
            P("G", ".NET 6 Runtime (EOL)", AppInstalled(".NET") && Bloat(".NET"), 0, $"{BloatCount(".NET")} کاندید");
            P("G", "Apple Mobile + Bonjour", AppInstalled("Apple") || Borderline("Apple") || Bloat("Apple"), 0,
                $"نصب: {(AppInstalled("Apple Mobile") ? "بله" : "خیر")} | borderline: {(Borderline("Apple") ? "بله" : "خیر")}");
            P("G", "X-VPN", AppInstalled("X-VPN") && (Borderline("X-VPN") || Bloat("X-VPN")), 0,
                AppInstalled("X-VPN") ? "نصب است" : "نصب نیست");

            // ================= H) استارتاپ و تسک =================
            P("H", "IDMan /onboot", Startup("IDMan"), 0, $"آیتم: {StartupCount("IDMan")}");
            P("H", "MicrosoftEdgeAutoLaunch", Startup("MicrosoftEdgeAutoLaunch"), 0, $"آیتم: {StartupCount("MicrosoftEdgeAutoLaunch")}");
            P("H", "Send to OneNote", Startup("Send to OneNote") || Startup("OneNote"), 0, $"آیتم: {StartupCount("OneNote")}");
            P("H", "OneDrive", Startup("OneDrive"), 0, $"آیتم: {StartupCount("OneDrive")}");
            P("H", "GoogleUpdater", Startup("GoogleUpdater") || Startup("Google Update"), 0, $"آیتم: {StartupCount("GoogleUpdater") + StartupCount("Google Update")}");
            P("H", "SoftLandingTask", Startup("SoftLanding"), 0, $"آیتم: {StartupCount("SoftLanding")} — خارج از دامنه (trigger آن COM handler / One Time است نه logon)");
            P("H", "CreateExplorerShellUnelevatedTask", Startup("CreateExplorerShell"), 0, $"آیتم: {StartupCount("CreateExplorerShell")}");

            // ================= گزارش =================
            _out.WriteLine("");
            _out.WriteLine("================= BENCHMARK — ماتریس لیست کاربر =================");
            string currentSection = "";
            int found = 0;
            foreach (var p in probes)
            {
                if (p.Section != currentSection)
                {
                    currentSection = p.Section;
                    _out.WriteLine($"");
                    _out.WriteLine($"── بخش {currentSection} ──");
                }

                string size = p.Bytes > 0 ? " | " + SystemStats.FormatBytes(p.Bytes) : "";
                string status = p.Found ? "[✓]" : "[ ]";
                if (p.Found) found++;
                _out.WriteLine($"  {status} {p.Label}{size}" + (string.IsNullOrEmpty(p.Detail) ? "" : $" — {p.Detail}"));
            }

            _out.WriteLine("");
            _out.WriteLine($"پوشش: {found}/{probes.Count} آیتم لیست مرجع پیدا شد.");
            foreach (var grp in probes.GroupBy(p => p.Section))
            {
                _out.WriteLine($"  بخش {grp.Key}: {grp.Count(p => p.Found)}/{grp.Count()}");
            }

            // ================= قواعد سخت: قفل شده ها =================
            _out.WriteLine("");
            _out.WriteLine("================= قفل شده ها (نباید پیشنهاد شوند) =================");
            var violations = new List<string>();

            void Locked(string label, bool suggested, string detail = "")
            {
                _out.WriteLine($"  {(suggested ? "[VIOLATION]" : "[OK]")} {label}" + (string.IsNullOrEmpty(detail) ? "" : $" — {detail}"));
                if (suggested) violations.Add(label + (detail.Length > 0 ? $" ({detail})" : ""));
            }

            Locked("Duet Display — درایور یتیم", DriverOrphans("Duet") > 0 || DriverOrphans("A-Volute") > 0,
                $"orphan={DriverOrphans("Duet") + DriverOrphans("A-Volute")}");
            Locked("MSI Sound Tune — درایور یتیم", DriverOrphans("Sound Tune") > 0 || DriverOrphans("MSI") > 0,
                $"orphan={DriverOrphans("Sound Tune") + DriverOrphans("MSI")}");
            Locked("Duet — استارتاپ/تسک مفقود", startup.Any(s => s.TargetMissing && (Has(s.Name, "Duet") || Has(s.Name, "A-Volute"))));
            Locked("HP — تسک مفقود", startup.Any(s => s.TargetMissing && Has(s.Name, "HP")));
            Locked("MSI — تسک مفقود", startup.Any(s => s.TargetMissing && Has(s.Name, "MSI")));
            Locked("OmApSvcBroker / OneDC / Detect_Monitor / DisplayID — سرویس یتیم",
                orphanServices.Any(s => Has(s.ServiceName, "OmApSvcBroker") || Has(s.ServiceName, "OneDC") ||
                                        Has(s.ServiceName, "Detect_Monitor") || Has(s.ServiceName, "DisplayID")));
            Locked("v2rayN — پیشنهاد حذف/غیرفعال", Bloat("v2rayN") || Borderline("v2rayN") ||
                messHits.Any(m => Has(m.FilePath, "v2rayN")));
            Locked("LM Studio — خود برنامه نباید پیشنهاد حذف شود", Bloat("LM Studio") || Borderline("LM Studio") ||
                startup.Any(s => s.TargetMissing && Has(s.Name, "LM Studio")));
            Locked("LM Studio models — مسیر .lmstudio نباید در هیچ پیشنهاد حذفی باشد",
                junkItems.Any(i => Has(i.Path, ".lmstudio") || i.TargetPaths.Any(t => Has(t, ".lmstudio"))) ||
                messHits.Any(m => Has(m.FilePath, ".lmstudio")));
            Locked("CrossDevice — نباید تیک پیش فرض داشته باشد",
                deep.MessItems.Any(m => Has(m.FilePath, "CrossDevice") && m.IsSelected));
            Locked("Dragon Center — نباید پیشنهاد حذف شود", Bloat("Dragon") || Borderline("Dragon") ||
                startup.Any(s => s.TargetMissing && Has(s.Name, "Dragon")));
            Locked("CleanC — نباید پیشنهاد حذف شود", Bloat("CleanC") || Borderline("CleanC") ||
                messHits.Any(m => Has(m.FilePath, @"\CleanC") && !Has(m.FilePath, "CleanC-old")));
            Locked("دیسک O: (پروژه ها) — خارج از پیشنهاد", messHits.Any(m => Has(m.FilePath, @"O:\")));
            Locked("Win-Blur — نباید پیشنهاد حذف شود", Bloat("Win-Blur") || Mess("Win-Blur"));
            Locked("CrossDevice — نباید به عنوان پوشه قابل حذف بیاید", EmptyFolderExact(Path.Combine(userProfile, "CrossDevice")));
            Locked("XboxGames (GameSave) — نباید به عنوان پوشه خالی/قابل حذف بیاید", EmptyFolderExact(@"C:\XboxGames"));
            Locked("Saved Pictures — نباید به عنوان پوشه قابل حذف بیاید", EmptyFolderExact(Path.Combine(userProfile, "Saved Pictures")));
            Locked("Desktop (خودش) — نباید پیشنهاد حذف بگیرد", EmptyFolderExact(desktopDir));
            Locked(@"Desktop\Desktop — نباید پیشنهاد حذف بگیرد", EmptyFolderExact(Path.Combine(desktopDir, "Desktop")));
            Locked("پوشه ی Downloads (خودش) — نباید پیشنهاد حذف داشته باشد", EmptyFolderExact(Path.Combine(userProfile, "Downloads")));

            _out.WriteLine("");
            if (violations.Count == 0)
            {
                _out.WriteLine("===== BENCHMARK: هیچ نقض قفل مشاهده نشد =====");
            }
            else
            {
                _out.WriteLine("===== BENCHMARK: نقض قفل ها =====");
                foreach (var v in violations) _out.WriteLine("  - " + v);
            }

            Assert.True(violations.Count == 0, "نقض قفل ها: " + string.Join(" | ", violations));
        }

        /// <summary>پوشش ITestOutputHelper به شکل TextWriter برای اجرای عادی (بنچمارک خاموش).</summary>
        private sealed class TestOutputAdapter : TextWriter
        {
            private readonly ITestOutputHelper _inner;
            public TestOutputAdapter(ITestOutputHelper inner) => _inner = inner;
            public override Encoding Encoding => Encoding.UTF8;
            public override void Write(char value) { /* همه ی فراخوانی ها WriteLine هستند */ }
            public override void WriteLine(string? value) => _inner.WriteLine(value ?? string.Empty);
        }

        /// <summary>خروجی همزمان روی TestOutput و فایل UTF-8 (گزارش خوانا در %TEMP%\cleanc_benchmark.log).</summary>
        private sealed class MirrorOutputWriter : TextWriter
        {
            private readonly ITestOutputHelper _inner;
            private readonly StreamWriter _file;

            public MirrorOutputWriter(ITestOutputHelper inner, string path)
            {
                _inner = inner;
                _file = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = true // حتی اگر Dispose صدا نشود، گزارش کامل روی دیسک باشد
                };
            }

            public override Encoding Encoding => Encoding.UTF8;
            public override void Write(char value) => _file.Write(value);
            public override void WriteLine(string? value) { _inner.WriteLine(value ?? string.Empty); _file.WriteLine(value); }
            public override void Flush() => _file.Flush();
            protected override void Dispose(bool disposing)
            {
                if (disposing) _file.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
