// Phase6DetectorTests.cs
using CleanC.Core.Models;
using CleanC.Core.Services;

namespace CleanC.Core.Tests
{
    /// <summary>
    /// تست های دیتکتورهای فاز ۶ (D04 / D05 / D10 / D12 / D17 / D18) + صحت کاتالوگ ترجمه.
    /// همه ی تست ها روی پوشه های موقت اجرا می شوند و به داده ی واقعی کاربر دست نمی زنند.
    /// </summary>
    public class Phase6DetectorTests
    {
        // ── کمک های عمومی ─────────────────────────────────────────────────

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "CleanC_p6_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Cleanup(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        private static void WriteFile(string path, int sizeBytes = 64)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Path.GetTempPath());
            File.WriteAllBytes(path, new byte[sizeBytes]);
        }

        // ── D10 — بازمانده ی نصاب سازنده در ریشه ی درایو ──────────────────

        [Fact]
        public void D10_NvidiaFolderWithSetup_IsDetectedWithEvidenceAndT2Defaults()
        {
            string root = NewTempDir();
            try
            {
                WriteFile(Path.Combine(root, "NVIDIA", "NVAPP2", "setup.exe"));

                var found = DeepStorageCleanService.ScanOemDriveRootLeftovers(new[] { root });

                var item = Assert.Single(found);
                Assert.Equal(StorageMessCategory.OemDriveRootLeftover, item.Category);
                Assert.True(item.RequiresAdmin);  // ریشه ی درایو → حذف باید با دسترسی ادمین انجام شود
                Assert.True(item.IsSelected);     // T2: مدرک قطعی → پیش فرض تیک خورده
                Assert.Contains("NVIDIA", item.Evidence);
                Assert.Contains("setup.exe", item.Evidence);
            }
            finally { Cleanup(root); }
        }

        [Fact]
        public void D10_AllowlistedFolderWithoutHardEvidence_IsNotSuggested()
        {
            string root = NewTempDir();
            try
            {
                // پوشه ی NVIDIA هست ولی هیچ فایل نصب/آرتیفکتی داخلش نیست → سکوت
                WriteFile(Path.Combine(root, "NVIDIA", "readme.txt"));

                Assert.Empty(DeepStorageCleanService.ScanOemDriveRootLeftovers(new[] { root }));
            }
            finally { Cleanup(root); }
        }

        [Fact]
        public void D10_NonAllowlistedFolder_IsNeverTouched()
        {
            string root = NewTempDir();
            try
            {
                WriteFile(Path.Combine(root, "MyRandomStuff", "setup.exe"));

                Assert.Empty(DeepStorageCleanService.ScanOemDriveRootLeftovers(new[] { root }));
            }
            finally { Cleanup(root); }
        }

        [Fact]
        public void D10_VendorOwnArtifact_CountsAsHardEvidence()
        {
            string root = NewTempDir();
            try
            {
                // سناریوی واقعی C:\Aomei که فقط AomeiBoot.wim دارد (بدون setup)
                WriteFile(Path.Combine(root, "Aomei", "AomeiBoot.wim"), 128);

                var found = DeepStorageCleanService.ScanOemDriveRootLeftovers(new[] { root });

                var item = Assert.Single(found);
                Assert.Contains("AomeiBoot.wim", item.Evidence);
            }
            finally { Cleanup(root); }
        }

        // ── D04 — بازمانده ی آپدیترهای Electron/Squirrel ──────────────────

        [Fact]
        public void D04_UpdaterFolderWithPendingInstaller_IsDetected_ButAlwaysDefaultOff()
        {
            string localApp = NewTempDir();
            try
            {
                WriteFile(Path.Combine(localApp, "MyApp-updater", "pending", "MyApp-2.0.exe"), 2 * 1024 * 1024);

                var found = DeepStorageCleanService.ScanUpdaterLeftovers(localApp);

                var item = Assert.Single(found);
                Assert.Equal(StorageMessCategory.UpdaterLeftover, item.Category);
                Assert.False(item.IsSelected);   // T3: همیشه پیش فرض خاموش
                Assert.False(item.RequiresAdmin);
                Assert.Contains("MyApp-2.0.exe", item.Evidence);
            }
            finally { Cleanup(localApp); }
        }

        [Fact]
        public void D04_TinyUpdaterFolder_IsIgnored()
        {
            string localApp = NewTempDir();
            try
            {
                WriteFile(Path.Combine(localApp, "Small-updater", "x.txt"), 128); // زیر ۱ مگابایت

                Assert.Empty(DeepStorageCleanService.ScanUpdaterLeftovers(localApp));
            }
            finally { Cleanup(localApp); }
        }

        [Fact]
        public void D04_OldBlockmap_IsDetected_RecentIsIgnored()
        {
            string localApp = NewTempDir();
            try
            {
                string old = Path.Combine(localApp, "release-old.blockmap");
                WriteFile(old, 512);
                File.SetLastWriteTime(old, DateTime.Now.AddDays(-45));

                WriteFile(Path.Combine(localApp, "release-fresh.blockmap"), 512); // همین حالا

                var found = DeepStorageCleanService.ScanUpdaterLeftovers(localApp);

                var item = Assert.Single(found); // فقط فایل قدیمی
                Assert.Equal("release-old.blockmap", item.FileName);
            }
            finally { Cleanup(localApp); }
        }

        // ── D05 — تطبیق فازی نصب کننده با محصولات نصب شده ─────────────────

        [Fact]
        public void D05_DistinctiveToken_MatchesInstalledProduct()
        {
            var products = new List<string> { "NVIDIA GeForce Experience", "Steam" };

            string? match = DeepStorageCleanService.MatchInstalledProduct("NVIDIA_GeForce_Experience_Setup.exe", products);

            Assert.Equal("NVIDIA GeForce Experience", match);
        }

        [Fact]
        public void D05_GenericOnlyNames_DoNotMatch()
        {
            var products = new List<string> { "Anything Here" };

            Assert.Null(DeepStorageCleanService.MatchInstalledProduct("setup.exe", products));
            Assert.Null(DeepStorageCleanService.MatchInstalledProduct("installer_x64.exe", products));
        }

        [Fact]
        public void D05_NoMatchingInstalledProduct_ReturnsNull()
        {
            var products = new List<string> { "Completely Different App" };

            Assert.Null(DeepStorageCleanService.MatchInstalledProduct("SuperToolPro_2.3.exe", products));
        }

        // ── D12 — هدف ناموجود استارتاپ (مسیر exe قابل استخراج) ────────────

        [Fact]
        public void D12_QuotedExeWithArgs_ResolvesToExePath()
        {
            bool ok = StartupOptimizerService.TryResolveExecutablePath(
                "\"C:\\Program Files\\Foo\\foo.exe\" --silent", out string exePath);

            Assert.True(ok);
            Assert.Equal(@"C:\Program Files\Foo\foo.exe", exePath);
        }

        [Fact]
        public void D12_EnvironmentVariable_IsExpanded()
        {
            string expected = Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Foo\foo.exe");

            bool ok = StartupOptimizerService.TryResolveExecutablePath(@"%ProgramFiles%\Foo\foo.exe", out string exePath);

            Assert.True(ok);
            Assert.Equal(expected, exePath);
        }

        [Fact]
        public void D12_RelativeOrUnjudgeableCommands_AreNotFlagged()
        {
            // مسیر نسبی (بدون درایو) → قضاوت نکن
            Assert.False(StartupOptimizerService.TryResolveExecutablePath("foo.exe --run", out _));

            // متغیر محیطی حل نشده → ممکن است به مسیر کاربر دیگری اشاره کند → قضاوت نکن
            Assert.False(StartupOptimizerService.TryResolveExecutablePath(@"%CLEANC_NO_SUCH_VAR_123%\foo.exe", out _));

            // فرمان هایی که exe ندارند (مثل msiexec) → قضاوت نکن
            Assert.False(StartupOptimizerService.TryResolveExecutablePath("msiexec /i pkg.msi", out _));
            Assert.False(StartupOptimizerService.TryResolveExecutablePath("", out _));
        }

        // ── D17 — فایل های حساس (فقط گزارش) ──────────────────────────────

        [Fact]
        public void D17_SensitiveNames_AreDetected()
        {
            string? license = DeepStorageCleanService.GetSensitiveMatchReason("my license key.txt");
            Assert.NotNull(license);
            Assert.True(license!.Contains("license", StringComparison.OrdinalIgnoreCase) || license.Contains("لایسنس"));

            string? ssh = DeepStorageCleanService.GetSensitiveMatchReason("id_rsa");
            Assert.NotNull(ssh);
            Assert.Contains("SSH", ssh!);

            string? db = DeepStorageCleanService.GetSensitiveMatchReason("vault.kdbx");
            Assert.NotNull(db);
            Assert.Contains(".kdbx", db!);
        }

        [Fact]
        public void D17_NormalFiles_AreNotFlagged()
        {
            Assert.Null(DeepStorageCleanService.GetSensitiveMatchReason("photo.png"));
            Assert.Null(DeepStorageCleanService.GetSensitiveMatchReason("report.docx"));
            Assert.Null(DeepStorageCleanService.GetSensitiveMatchReason("budget.xlsx"));
        }

        // ── D18 — تشخیص نسخه ی آزمایشی (فقط badge) ───────────────────────

        [Fact]
        public void D18_TrialMarker_IsDetected_WithWordBoundary()
        {
            Assert.True(AppUninstallerService.IsTrialProduct("WinRAR Trial", out _));
            Assert.True(AppUninstallerService.IsTrialProduct("برنامه نسخه آزمایشی من", out _));

            // مرز کلمه: «Trials» و «Triathlon» نباید مثبت کاذب بدهند
            Assert.False(AppUninstallerService.IsTrialProduct("Trials of Mana", out _));
            Assert.False(AppUninstallerService.IsTrialProduct("Triathlon Trainer 2000", out _));
            Assert.False(AppUninstallerService.IsTrialProduct("Notepad++", out _));
        }

        // ── کاتالوگ ترجمه: همه ی کلیدهای فاز ۶ باید مقدار داشته باشند ────

        [Fact]
        public void Phase6_LocalizationKeys_AllExistInCatalog()
        {
            string[] keys =
            {
                "MessCatOldInstaller", "MessCatOldArchive", "MessCatDuplicateCopy", "MessCatOldScreenshot",
                "MessCatTempJunk", "MessCatLargeFile", "MessCatExtractedArchiveDup", "MessCatIncompleteDownload",
                "MessCatOemDriveRoot", "MessCatUpdater", "MessCatSyncDevice", "MessCatOther",
                "EvEmptyFolder30Days", "EvDownloadCompleted", "EvDownloadAbandoned", "EvTmpOld72h",
                "EvInstallerMatched", "EvInstallerNoMatch", "EvCrossDevice", "EvCrossDeviceSub",
                "EvOneDriveTemp", "EvOneDriveTempSub", "EvDriveRootLeftover", "EvRemnantInstallerFile",
                "EvVendorArtifact", "EvUpdaterPendingInstaller", "EvUpdaterNoPending", "EvBlockmapOld",
                "EvDriverNoHardware", "EvStartupFileMissing", "EvStartupExeMissing",
                "EvTrialInName", "EvTrialInNameLocalized", "NoteProvisioned",
                "SensitiveLicense", "SensitiveSerial", "SensitiveProductKey", "SensitiveActivation",
                "SensitivePasswords", "SensitiveSshKey", "SensitivePrivateKey", "SensitiveKeyFile",
                "LockUserVetoError", "LockTooltipLock", "LockTooltipUnlock", "LockInfoBarTitle",
                "LockSaveFailed", "LockReasonDeepScan", "AdminRequired", "SensitiveSectionTitle",
                "BadgeTargetMissing", "BadgeTrial", "BadgeNoHardware", "ScanProgressDriveRoots",
                "DescCrashLogs", "DriverBannerDupAndOrphan", "DriverBannerOrphan", "DriverBannerDup",
                "DriverDeleteOrphanTitle", "DriverDeleteOldTitle", "DriverDeleteOrphanBody", "DriverDeleteOldBody"
            };

            foreach (string key in keys)
            {
                string value = LocalizationService.Get(key);
                Assert.False(string.IsNullOrWhiteSpace(value), $"کلید «{key}» در کاتالوگ خالی یا غایب است");
                Assert.NotEqual(key, value); // اگر کلید پیدا نشود، Get خودِ کلید را برمی گرداند
            }
        }
    }
}
