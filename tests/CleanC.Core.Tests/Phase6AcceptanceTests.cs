// Phase6AcceptanceTests.cs
using CleanC.Core.Models;
using CleanC.Core.Services;
using Xunit.Abstractions;

namespace CleanC.Core.Tests
{
    /// <summary>
    /// هارنس Acceptance فاز ۶ — فقط وقتی متغیر محیطی CLEANC_ACCEPTANCE=1 ست باشد اجرا می شود.
    /// هدف: اجرای اسکن های واقعی روی سیستم کاربر و چاپ گزارش ساختاریافته برای تطبیق با
    /// Ground Truth اسپک (docs/DETECTION-ENGINE-SPEC.md). در اجرای معمولی/CI بلافاصله
    /// return می کند تا تست ها سبز بمانند.
    ///
    /// اجرا:
    ///   $env:CLEANC_ACCEPTANCE=1; dotnet test CleanC.sln --filter Phase6Acceptance -v q
    /// </summary>
    public class Phase6AcceptanceTests
    {
        private readonly ITestOutputHelper _out;
        private static bool Enabled => Environment.GetEnvironmentVariable("CLEANC_ACCEPTANCE") == "1";

        public Phase6AcceptanceTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public async Task RunRealScans_AndPrintAcceptanceReport()
        {
            if (!Enabled)
            {
                _out.WriteLine("CLEANC_ACCEPTANCE=1 ست نیست → اسکن واقعی اجرا نشد (رد شد).");
                return;
            }

            var failures = new List<string>();

            // ── ۱) اسکن عمیق ذخیره سازی (D04/D05/D06/D09/D10/D12/D16/D17…) ──
            DeepStorageScanResult? result = null;
            try
            {
                result = await DeepStorageCleanService.ScanDeepStorageAsync(p => _out.WriteLine("  · " + p));
                _out.WriteLine("[OK] ScanDeepStorageAsync بدون استثنا تمام شد.");
            }
            catch (Exception ex)
            {
                failures.Add("ScanDeepStorageAsync استثنا داد: " + ex);
            }

            if (result != null)
            {
                PrintDeepStorageReport(result, failures);
            }

            // ── ۲) اسکن سیستم (شامل D03 — LiveKernelReports در CrashLogs) ──
            try
            {
                var scan = await SystemScanService.ScanAsync(p => _out.WriteLine("  · " + p));
                _out.WriteLine("");
                _out.WriteLine("=====[ D03 — گزارش های کرش (LiveKernelReports) ]=====");
                foreach (var group in scan.Groups)
                {
                    foreach (var item in group.Items)
                    {
                        if (item.Bytes > 0 && (item.Id.Contains("crash", StringComparison.OrdinalIgnoreCase)
                            || item.Title.Contains("Crash", StringComparison.OrdinalIgnoreCase)
                            || item.Title.Contains("کرش")))
                        {
                            _out.WriteLine($"  {item.Title}: {item.FormattedBytes} ({item.Bytes:N0} بایت) | {item.Path}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failures.Add("SystemScanService.ScanAsync استثنا داد: " + ex.Message);
            }

            // ── ۳) درایورها (D14) ──
            try
            {
                var (drivers, stats) = await DriverManagerService.GetDriversAsync();
                _out.WriteLine("");
                _out.WriteLine("=====[ D14 — درایورها ]=====");
                _out.WriteLine($"  کل: {stats.TotalDrivers} | خطا: {stats.ErrorCount} | تکراری/قدیمی: {stats.DuplicateOldCount} | یتیم بدون سخت‌افزار: {stats.OrphanNoHardwareCount}");

                foreach (var d in drivers.Where(d => d.IsOrphanNoHardware).Take(15))
                {
                    _out.WriteLine($"  [ORPHAN] {d.PublishedName} ({d.ProviderName}) — {d.Evidence}");
                }

                // تفکیک providerها برای بازبینی حفاظت‌ها (اکوسیستم ها نباید یتیم فلگ شوند)
                var orphanProviders = drivers.Where(d => d.IsOrphanNoHardware)
                    .GroupBy(d => d.ProviderName ?? "?")
                    .OrderByDescending(g => g.Count())
                    .Take(12)
                    .Select(g => $"{g.Key}:{g.Count()}");
                _out.WriteLine($"  [orphan-providers] {string.Join(" | ", orphanProviders)}");

                // Ground Truth خط ۱۰۶: بسته‌های ENE / WD_BLACK / Thunderbolt — و مرزبانی Duet (نباید حذف پیشنهادی شود)
                foreach (string probe in new[] { "ENE", "Western Digital", "Thunderbolt", "Duet" })
                {
                    var hits = drivers.Where(d =>
                        (d.ProviderName + " " + d.OriginalName + " " + d.PublishedName)
                            .Contains(probe, StringComparison.OrdinalIgnoreCase)).ToList();
                    _out.WriteLine($"  [probe] {probe}: {hits.Count} بسته | یتیم: {hits.Count(h => h.IsOrphanNoHardware)}"
                        + (hits.Count > 0 ? " — " + string.Join(", ", hits.Take(4).Select(h => h.PublishedName + "/" + h.ProviderName)) : ""));
                }
            }
            catch (Exception ex)
            {
                failures.Add("DriverManagerService.GetDriversAsync استثنا داد: " + ex.Message);
            }

            // ── ۴) استارتاپ‌ها (D12) ──
            try
            {
                var startup = StartupOptimizerService.GetStartupItems(includeExtraSources: true);
                var missing = startup.Where(i => i.TargetMissing).ToList();
                _out.WriteLine("");
                _out.WriteLine("=====[ D12 — استارتاپ‌های با هدف ناموجود ]=====");
                _out.WriteLine($"  کل آیتم‌ها: {startup.Count} | هدف ناموجود: {missing.Count}");
                foreach (var s in missing)
                {
                    _out.WriteLine($"  [MISSING] {s.Name} — {s.Evidence} | Command: {s.Command}");
                }
            }
            catch (Exception ex)
            {
                failures.Add("StartupOptimizerService.GetStartupItems استثنا داد: " + ex.Message);
            }

            // ── ۵) هوشمند‌سازی برنامه‌های پیش‌فرض (D13) ──
            try
            {
                var candidates = await BloatwareService.ScanCandidateAppsAsync();
                var provisioned = candidates.Where(a => a.IsProvisioned).ToList();
                _out.WriteLine("");
                _out.WriteLine("=====[ D13 — بسته‌های Provisioned ]=====");
                _out.WriteLine($"  کاندیدها: {candidates.Count} | Provisioned: {provisioned.Count}");
                foreach (var a in provisioned.Take(15))
                {
                    _out.WriteLine($"  [PROVISIONED] {a.DisplayName} ({a.PackageFullName})");
                }

                var borderline = await BloatwareService.ScanBorderlineReviewAppsAsync();
                _out.WriteLine($"  Borderline (بازبینی دستی): {borderline.Count}");
                foreach (var b in borderline.Take(12))
                {
                    _out.WriteLine($"  [BORDERLINE] {b.DisplayName} — {b.CategoryLabel} | {b.FormattedSize}");
                }

                var largeUnused = candidates.Where(c => c.CategoryType == AppCategoryType.LargeUnused).ToList();
                _out.WriteLine($"  حجیم و کم‌استفاده (>500MB و >30 روز): {largeUnused.Count}");
                foreach (var u in largeUnused.Take(10))
                {
                    _out.WriteLine($"  [UNUSED] {u.DisplayName} — {u.FormattedSize} | {u.Description}");
                }
            }
            catch (Exception ex)
            {
                failures.Add("BloatwareService.ScanCandidateAppsAsync استثنا داد: " + ex.Message);
            }

            // ── ۶) نسخه‌های آزمایشی (D18) ──
            try
            {
                var apps = await AppUninstallerService.GetInstalledAppsAsync();
                var trials = apps.Where(a => a.IsTrialBadge).ToList();
                _out.WriteLine("");
                _out.WriteLine("=====[ D18 — نسخه آزمایشی (فقط badge) ]=====");
                _out.WriteLine($"  کل برنامه‌ها: {apps.Count} | Trial: {trials.Count}");
                foreach (var t in trials.Take(15))
                {
                    _out.WriteLine($"  [TRIAL] {t.DisplayName} — {t.TrialEvidence}");
                }
            }
            catch (Exception ex)
            {
                failures.Add("AppUninstallerService.GetInstalledAppsAsync استثنا داد: " + ex.Message);
            }

            // ── ۷) سرویس‌های یتیم (D11) ──
            try
            {
                var services = RegistryCleanerService.ScanOrphanedServices();
                _out.WriteLine("");
                _out.WriteLine("=====[ D11 — سرویس‌های یتیم ]=====");
                _out.WriteLine($"  کل: {services.Count}");
                foreach (var s in services.Take(15))
                {
                    _out.WriteLine($"  [ORPHAN-SVC] {s.ServiceName} ({s.DisplayName}) → {s.MissingImagePath}");
                }
            }
            catch (Exception ex)
            {
                failures.Add("RegistryCleanerService.ScanOrphanedServices استثنا داد: " + ex.Message);
            }

            // ── ۸) پوشه‌های بازمانده برنامه‌های حذف‌شده (D15) ──
            try
            {
                var leftovers = LeftoverCleanerService.ScanLeftoverFolders();
                _out.WriteLine("");
                _out.WriteLine("=====[ D15 — پوشه‌های بازمانده ]=====");
                _out.WriteLine($"  کل: {leftovers.Count}");
                foreach (var l in leftovers.OrderByDescending(l => l.SizeBytes).Take(20))
                {
                    _out.WriteLine($"  [LEFTOVER] {l.LocationType}: {l.FullPath} | {SystemStats.FormatBytes(l.SizeBytes)}");
                }
            }
            catch (Exception ex)
            {
                failures.Add("LeftoverCleanerService.ScanLeftoverFolders استثنا داد: " + ex.Message);
            }

            _out.WriteLine("");
            _out.WriteLine(failures.Count == 0
                ? "===== ACCEPTANCE: همه‌ی بخش‌ها بدون خطا اجرا شدند ====="
                : "===== ACCEPTANCE FAILURES =====\n" + string.Join("\n", failures));

            Assert.True(failures.Count == 0, string.Join(" | ", failures));
        }

        /// <summary>گزارش اسکن عمیق + قوانین ثابت (مستقل از سیستم): T2/T3، شواهد اجباری، جدایی بخش حساس.</summary>
        private void PrintDeepStorageReport(DeepStorageScanResult result, List<string> failures)
        {
            var mess = result.MessItems;
            _out.WriteLine("");
            _out.WriteLine("=====[ اسکن عمیق — خلاصه ]=====");
            _out.WriteLine($"  DuplicateGroups: {result.DuplicateGroups.Count} گروه (هدررفته: {SystemStats.FormatBytes(result.TotalDuplicateWastedBytes)})");
            _out.WriteLine($"  MessItems: {mess.Count} | EmptyFolders: {result.EmptyFolders.Count} | Sensitive: {result.SensitiveFiles.Count}");

            foreach (var grp in mess.GroupBy(m => m.Category).OrderByDescending(g => g.Count()))
            {
                long total = grp.Sum(m => m.SizeBytes);
                int selected = grp.Count(m => m.IsSelected);
                _out.WriteLine($"  · {grp.Key}: {grp.Count()} مورد | {SystemStats.FormatBytes(total)} | پیش‌فرض تیک: {selected}");
            }

            _out.WriteLine("");
            _out.WriteLine("=====[ D07 — گروه‌های تکراری (جزئیات) ]=====");
            foreach (var g in result.DuplicateGroups.OrderByDescending(g => g.WastedSizeBytes).Take(6))
            {
                _out.WriteLine($"  {g.SampleName} ×{g.Files.Count} | هر نسخه: {g.FormattedSingleSize} | اتلاف: {g.FormattedWastedSize}");
                foreach (var f in g.Files.Take(4))
                {
                    _out.WriteLine($"      ↳ {(f.IsOriginal ? "[اصل] " : "")}{f.FilePath}");
                }
            }

            _out.WriteLine("");
            _out.WriteLine("=====[ فایل‌های حجیم (>200MB) ]=====");
            foreach (var f in mess.Where(m => m.Category == StorageMessCategory.LargeFile).OrderByDescending(m => m.SizeBytes))
            {
                _out.WriteLine($"  {f.FilePath} | {f.FormattedSize}");
            }

            _out.WriteLine("");
            _out.WriteLine("=====[ دیتکتورهای جدید — جزئیات ]=====");
            var newCategories = new[]
            {
                StorageMessCategory.OldInstaller, StorageMessCategory.OemDriveRootLeftover,
                StorageMessCategory.UpdaterLeftover, StorageMessCategory.SyncDeviceLeftover,
                StorageMessCategory.IncompleteDownload
            };

            foreach (var cat in newCategories)
            {
                var items = mess.Where(m => m.Category == cat).ToList();

                // قاعده ۱: هر آیتم دیتکتور جدید باید مدرک (Evidence) داشته باشد
                foreach (var bad in items.Where(m => string.IsNullOrWhiteSpace(m.Evidence)))
                {
                    failures.Add($"آیتم بدون مدرک در {cat}: {bad.FilePath}");
                }

                // قاعده ۲ (T3): دسته‌های همیشه-خاموش نباید پیش‌فرض تیک بخورند
                if (cat is StorageMessCategory.UpdaterLeftover or StorageMessCategory.SyncDeviceLeftover or StorageMessCategory.IncompleteDownload)
                {
                    foreach (var bad in items.Where(m => m.IsSelected))
                    {
                        failures.Add($"آیتم T3 پیش‌فرض تیک خورده در {cat}: {bad.FilePath}");
                    }
                }

                // قاعده ۳ (T2): ریشه‌ی درایو باید پیش‌فرض تیک + نیازمند ادمین باشد
                if (cat == StorageMessCategory.OemDriveRootLeftover)
                {
                    foreach (var bad in items.Where(m => !m.IsSelected))
                    {
                        failures.Add($"آیتم T2 ریشه‌ی درایو بدون تیک پیش‌فرض: {bad.FilePath}");
                    }
                    foreach (var bad in items.Where(m => !m.RequiresAdmin))
                    {
                        failures.Add($"آیتم ریشه‌ی درایو بدون RequiresAdmin: {bad.FilePath}");
                    }
                }

                foreach (var m in items.OrderByDescending(m => m.SizeBytes).Take(12))
                {
                    string sel = m.IsSelected ? "☑" : "☐";
                    _out.WriteLine($"  {sel} [{cat}] {m.FilePath} | {m.FormattedSize}");
                    _out.WriteLine($"      ↳ مدرک: {m.Evidence}");
                }
            }

            // قاعده ۴: مسیر فایل حساس هرگز نباید داخل MessItems (قابل حذف) باشد — report-only یعنی همین
            var sensitivePaths = new HashSet<string>(result.SensitiveFiles.Select(s => s.FilePath), StringComparer.OrdinalIgnoreCase);
            foreach (var bad in mess.Where(m => sensitivePaths.Contains(m.FilePath)))
            {
                failures.Add($"فایل حساس وارد لیست حذف شد: {bad.FilePath}");
            }

            _out.WriteLine("");
            _out.WriteLine("=====[ D17 — فایل‌های حساس (فقط گزارش، بدون مسیر حذف) ]=====");
            foreach (var s in result.SensitiveFiles.OrderByDescending(s => s.SizeBytes).Take(25))
            {
                _out.WriteLine($"  {s.FilePath} | {s.FormattedSize} | دلیل: {s.MatchReason}");
            }

            // Ground Truth (سیستم کاربر) — فقط نمایش، نه assert؛ برای تطبیق دستی با اسپک
            _out.WriteLine("");
            _out.WriteLine("=====[ Ground Truth — تطبیق دستی با اسپک ]=====");

            // خط ۱۰۱: «۳ نصاب تکراری NVIDIA ~۱.۹GB» → باید در گروه‌های تکراری D07 باشد
            var nvidiaGroups = result.DuplicateGroups.Where(g =>
                g.SampleName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                g.Files.Any(f => f.FilePath.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))).ToList();
            _out.WriteLine(nvidiaGroups.Count > 0
                ? $"  [✓] NVIDIA در گروه‌های تکراری D07: {nvidiaGroups.Count} گروه | اتلاف کل: {SystemStats.FormatBytes(nvidiaGroups.Sum(g => g.WastedSizeBytes))}"
                : "  [ ] NVIDIA در گروه‌های تکراری D07 پیدا نشد");

            PrintGroundTruthProbe(mess, "NVIDIA");
            PrintGroundTruthProbe(mess, @"C:\NVIDIA");
            PrintGroundTruthProbe(mess, @"C:\Aomei");
        }

        private void PrintGroundTruthProbe(List<StorageMessItem> mess, string needle)
        {
            var hits = mess.Where(m =>
                m.FilePath.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                m.Evidence.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();

            if (hits.Count == 0)
            {
                _out.WriteLine($"  [ ] {needle}: پیدا نشد");
                return;
            }

            foreach (var h in hits.Take(6))
            {
                _out.WriteLine($"  [✓] {needle}: {h.FilePath} | {h.FormattedSize} | {h.Evidence}");
            }
        }
    }
}
