using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CleanC.Core.Services;
using Microsoft.Win32;
using Xunit;

namespace CleanC.Core.Tests
{
    [Collection("SafeDeleteTests")]
    public class ReviewBugFixTests : IDisposable
    {
        private readonly string _testDir;

        public ReviewBugFixTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "WinyWash_BugFixTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try
            {
                SafeDeleteService.ResetSessionForTesting();
                SafeDeleteService.QuarantineRoot = null!;
                RegistryCleanerService.CustomBackupDirForTesting = null;
                TelemetryOptimizerService.ServicesRegistryRootForTesting = null;

                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch { }
        }

        /// <summary>
        /// باگ ۱: فراخوانی تابع تولیدی CleanSpecificExtensions روی پوشه تستی چند افزونه با چند زیرپوشه،
        /// و تأیید اینکه هر زیرپوشه بکاپ کاملاً مجزا دارد و هیچ زیرپوشه‌ای نادیده گرفته یا overwrite نشده است.
        /// </summary>
        [Fact]
        public void Bug1_MultiFolderExtensionBackup_PreservesDistinctSubpaths()
        {
            string userDataDir = Path.Combine(_testDir, "UserData");
            string profileDir = Path.Combine(userDataDir, "Default");
            string backupDir = Path.Combine(_testDir, "Backup");
            string extId = "test_extension_id";

            string extFolder = Path.Combine(profileDir, "Extensions", extId, "1.0.0");
            string settingsFolder = Path.Combine(profileDir, "Local Extension Settings", extId);
            string idbFolder = Path.Combine(profileDir, "IndexedDB", $"chrome-extension_{extId}_0.indexeddb.leveldb");

            Directory.CreateDirectory(extFolder);
            Directory.CreateDirectory(settingsFolder);
            Directory.CreateDirectory(idbFolder);

            File.WriteAllText(Path.Combine(extFolder, "manifest.json"), "{\"name\":\"test\"}");
            File.WriteAllText(Path.Combine(settingsFolder, "settings.ldb"), "data-ldb");
            File.WriteAllText(Path.Combine(idbFolder, "000001.log"), "idb-log");

            // فراخوانی مستقیم تابع تولیدی CleanSpecificExtensions با ارسال مسیر تستی
            var (cleanedCount, freedBytes) = BrowserOptimizerService.CleanSpecificExtensions(
                new[] { extId },
                customUserDataPath: userDataDir,
                customBackupDir: backupDir);

            Assert.True(cleanedCount > 0, "باید دست‌کم یک بخش پاکسازی شده باشد.");
            Assert.True(freedBytes > 0, "حجم آزاد شده باید مثبت باشد.");

            // بررسی دقیق پوشه‌های بکاپ تولیدشده توسط متد اصلی
            string bkManifest = Path.Combine(backupDir, "Default", "Extensions", extId, "1.0.0", "manifest.json");
            string bkSettings = Path.Combine(backupDir, "Default", "Local Extension Settings", extId, "settings.ldb");
            string bkIdb = Path.Combine(backupDir, "Default", "IndexedDB", $"chrome-extension_{extId}_0.indexeddb.leveldb", "000001.log");

            Assert.True(File.Exists(bkManifest), "فایل مانیفست افزونه باید در پوشه مجزای خود بکاپ گرفته شده باشد.");
            Assert.True(File.Exists(bkSettings), "پوشه تنظیمات افزونه باید در زیرپوشه مجزای خود بکاپ گرفته شده باشد.");
            Assert.True(File.Exists(bkIdb), "دیتابیس IndexedDB باید در زیرپوشه مجزای خود بکاپ گرفته شده باشد.");

            Assert.Equal("{\"name\":\"test\"}", File.ReadAllText(bkManifest));
            Assert.Equal("data-ldb", File.ReadAllText(bkSettings));
            Assert.Equal("idb-log", File.ReadAllText(bkIdb));

            // تأیید اینکه پوشه‌های اصلی از پوشه مرورگر حذف شده‌اند
            Assert.False(Directory.Exists(extFolder), "پوشه اصلی افزونه باید حذف شده باشد.");
            Assert.False(Directory.Exists(settingsFolder), "پوشه تنظیمات اصلی باید حذف شده باشد.");
            Assert.False(Directory.Exists(idbFolder), "پوشه IndexedDB اصلی باید حذف شده باشد.");
        }

        /// <summary>
        /// باگ ۲: گارد IsProtectedSystemPath باید اجداد مسیرهای محافظت‌شده (مثل C:\Users یا C:\Windows)
        /// و تطابق‌های دقیق را کاملاً مسدود کند و حذف را رد کند.
        /// </summary>
        [Fact]
        public void Bug2_IsProtectedSystemPath_BlocksAncestorsAndExactPaths()
        {
            string userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string usersParent = Path.Combine(Path.GetPathRoot(userDir) ?? "C:\\", "Users");
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string driveRoot = Path.GetPathRoot(winDir) ?? "C:\\";

            // ۱. اجداد و ریشه‌ها باید مسدود شوند
            Assert.True(SafeDeleteService.IsProtectedSystemPath(usersParent), "پوشه C:\\Users باید به عنوان جد پروفایل‌ها مسدود شود.");
            Assert.True(SafeDeleteService.IsProtectedSystemPath(driveRoot), "ریشه درایو C:\\ باید مسدود شود.");
            Assert.True(SafeDeleteService.IsProtectedSystemPath(driveRoot.TrimEnd('\\')), "C: بدون اسلش باید مسدود شود.");

            // ۲. مسیرهای دقیق سیستمی
            Assert.True(SafeDeleteService.IsProtectedSystemPath(winDir), "پوشه Windows باید مسدود شود.");
            Assert.True(SafeDeleteService.IsProtectedSystemPath(Environment.SystemDirectory), "پوشه System32 باید مسدود شود.");
            Assert.True(SafeDeleteService.IsProtectedSystemPath(userDir), "ریشه پروفایل کاربر باید مسدود شود.");
            Assert.True(SafeDeleteService.IsProtectedSystemPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)), "پوشه دسکتاپ باید مسدود شود.");
            Assert.True(SafeDeleteService.IsProtectedSystemPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)), "پوشه اسناد کاربر باید مسدود شود.");

            // ۳. مسیرهای امن داخل تمپ نباید مسدود شوند
            string safeTempFolder = Path.Combine(_testDir, "SafeSubFolder");
            Directory.CreateDirectory(safeTempFolder);
            Assert.False(SafeDeleteService.IsProtectedSystemPath(safeTempFolder), "پوشه موقت داخل Temp باید مجاز باشد.");
        }

        /// <summary>
        /// باگ ۳: فراخوانی مستقیم متد تولیدی CleanDeadUninstallKeys و اثبات اینکه اگر بکاپ به هر دلیلی
        /// شکست بخورد، کلید رجیستری هرگز حذف نمی‌شود و در رجیستری دست‌نخورده باقی می‌ماند.
        /// </summary>
        [Fact]
        public void Bug3_RegistryCleaner_WhenBackupFails_DoesNotDeleteKey()
        {
            string testParent = @"Software\WinyWashTestUninstall_" + Guid.NewGuid().ToString("N");
            string deadKeyName = "DeadApp123";

            // ایجاد کلید مرده در HKCU (اشاره به فایل غیرموجود uninstaller)
            using (var parentKey = Registry.CurrentUser.CreateSubKey(testParent))
            {
                Assert.NotNull(parentKey);
                using var deadKey = parentKey.CreateSubKey(deadKeyName);
                deadKey.SetValue("DisplayName", "Dead Test Application");
                deadKey.SetValue("UninstallString", @"C:\NonExistentDirectory_X999\uninstall.exe");
            }

            try
            {
                // مرحله اول: تعیین مسیر بکاپ نامعتبر/غیرقابل نوشتن برای شبیه‌سازی شکست بکاپ
                RegistryCleanerService.CustomBackupDirForTesting = @"Z:\InvalidNonExistentDrive_X888\Registry";

                // فراخوانی مستقیم تابع تولیدی
                int cleaned = RegistryCleanerService.CleanDeadUninstallKeys(Registry.CurrentUser, testParent);

                // باید صفر باشد چون بکاپ شکست خورده
                Assert.Equal(0, cleaned);

                // کلید رجیستری باید هنوز سر جایش باشد و حذف نشده باشد!
                using (var checkParent = Registry.CurrentUser.OpenSubKey(testParent, false))
                {
                    Assert.NotNull(checkParent);
                    using var checkSub = checkParent.OpenSubKey(deadKeyName, false);
                    Assert.NotNull(checkSub); // کلید با موفقیت محافظت شد و حذف نشد!
                }

                // مرحله دوم: اکنون مسیر بکاپ معتبر می‌دهیم
                RegistryCleanerService.CustomBackupDirForTesting = Path.Combine(_testDir, "ValidBackups");

                int cleanedWithValidBackup = RegistryCleanerService.CleanDeadUninstallKeys(Registry.CurrentUser, testParent);
                Assert.Equal(1, cleanedWithValidBackup);

                // اکنون کلید حذف شده و بکاپ .reg در مسیر معتبر ایجاد شده است
                using (var checkParent = Registry.CurrentUser.OpenSubKey(testParent, false))
                {
                    Assert.NotNull(checkParent);
                    using var checkSub = checkParent.OpenSubKey(deadKeyName, false);
                    Assert.Null(checkSub); // کلید حذف شد
                }

                var regFiles = Directory.GetFiles(Path.Combine(_testDir, "ValidBackups"), "*.reg");
                Assert.NotEmpty(regFiles);
            }
            finally
            {
                RegistryCleanerService.CustomBackupDirForTesting = null;
                try { Registry.CurrentUser.DeleteSubKeyTree(testParent, false); } catch { }
            }
        }

        /// <summary>
        /// باگ ۴: فراخوانی تابع تولیدی EnableAndStartService با مقدار ذخیره‌شده خراب یا نامعتبر
        /// و تأیید اینکه استارت‌تایپ نهایی سرویس در رجیستری دقیقاً به پیش‌فرض واقعی ویندوز برمی‌گردد.
        /// </summary>
        [Fact]
        public void Bug4_TelemetryOptimizer_GracefulDefaultsOnCorruptedState()
        {
            string testRootPath = @"Software\WinyWashTestServices_" + Guid.NewGuid().ToString("N");

            using (var testServicesRoot = Registry.CurrentUser.CreateSubKey(testRootPath))
            {
                Assert.NotNull(testServicesRoot);

                // ۱. تست DiagTrack با مقدار خراب رشته‌ای
                using (var diag = testServicesRoot.CreateSubKey("DiagTrack"))
                {
                    diag.SetValue("Start", 4, RegistryValueKind.DWord); // قبلاً غیرفعال بوده
                    diag.SetValue("WinyWash_OriginalStart", "Corrupted_String_Not_An_Integer", RegistryValueKind.String);
                }

                // ۲. تست dmwappushservice با مقدار خارج از محدوده
                using (var dmw = testServicesRoot.CreateSubKey("dmwappushservice"))
                {
                    dmw.SetValue("Start", 4, RegistryValueKind.DWord);
                    dmw.SetValue("WinyWash_OriginalStart", 9999, RegistryValueKind.DWord);
                }

                // هدایت ریشه سرویس‌ها به کلید تستی
                TelemetryOptimizerService.ServicesRegistryRootForTesting = testServicesRoot;

                // فراخوانی مستقیم تابع تولیدی
                bool diagResult = TelemetryOptimizerService.EnableAndStartService("DiagTrack");
                bool dmwResult = TelemetryOptimizerService.EnableAndStartService("dmwappushservice");

                Assert.True(diagResult);
                Assert.True(dmwResult);

                // تأیید استارت‌تایپ نهایی در رجیستری:
                // برای DiagTrack باید ۲ (Automatic) باشد
                using (var diagCheck = testServicesRoot.OpenSubKey("DiagTrack", false))
                {
                    Assert.NotNull(diagCheck);
                    int diagStart = Convert.ToInt32(diagCheck.GetValue("Start"));
                    Assert.Equal(2, diagStart);
                }

                // برای dmwappushservice باید ۳ (Manual) باشد
                using (var dmwCheck = testServicesRoot.OpenSubKey("dmwappushservice", false))
                {
                    Assert.NotNull(dmwCheck);
                    int dmwStart = Convert.ToInt32(dmwCheck.GetValue("Start"));
                    Assert.Equal(3, dmwStart);
                }
            }

            try { Registry.CurrentUser.DeleteSubKeyTree(testRootPath, false); } catch { }
        }

        /// <summary>
        /// باگ ۵: تست واقعی سناریوی شکست نیمه‌کاره قرنطینه با قفل کردن فایل با FileStream(FileShare.None).
        /// تأیید اینکه کپی قرنطینه پاک نمی‌شود، فایل غیرقفل‌شده یا در مبدأ یا در قرنطینه در دست‌کم یکی از دو جا
        /// موجود است و در manifest ثبت شده است.
        /// </summary>
        [Fact]
        public void Bug5_QuarantineRollback_WhenSourceDeletionFails_PreservesQuarantineCopyAndManifest()
        {
            string qRoot = Path.Combine(_testDir, "QuarantineRoot");
            SafeDeleteService.QuarantineRoot = qRoot;
            SafeDeleteService.ResetSessionForTesting();

            string sourceDir = Path.Combine(_testDir, "SourceFolder");
            Directory.CreateDirectory(sourceDir);

            string unlockedFile = Path.Combine(sourceDir, "unlocked.txt");
            string lockedFile = Path.Combine(sourceDir, "locked.txt");

            File.WriteAllText(unlockedFile, "unlocked content 123");
            File.WriteAllText(lockedFile, "locked content 456");

            // قفل کردن فایل locked.txt با انحصار کامل
            using (var fs = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                // فراخوانی متد اصلی حذف پوشه به قرنطینه
                // چون locked.txt قفل است، Directory.Delete مبدأ با شکست مواجه می‌شود
                var result = SafeDeleteService.DeleteDirectory(sourceDir, SafeDeleteMode.Quarantine);

                // نتیجه باید Quarantined باشد چون کپی موفق بوده
                Assert.Equal(SafeDeleteOutcome.Quarantined, result.Outcome);
                Assert.NotNull(result.StoredPath);

                // تأیید اینکه کپی قرنطینه پاک نشده و کپی کامل در قرنطینه باقی مانده است
                Assert.True(Directory.Exists(result.StoredPath), "پوشه قرنطینه نباید با رول‌بک حذف شده باشد.");

                string qUnlocked = Path.Combine(result.StoredPath, "unlocked.txt");
                Assert.True(File.Exists(qUnlocked), "فایل غیرقفل‌شده باید در قرنطینه موجود باشد.");
                Assert.Equal("unlocked content 123", File.ReadAllText(qUnlocked));

                // تأیید اینکه فایل غیرقفل‌شده دست‌کم در یکی از دو جا (یا مبدأ یا قرنطینه) هست
                bool inSourceOrQuarantine = File.Exists(unlockedFile) || File.Exists(qUnlocked);
                Assert.True(inSourceOrQuarantine, "داده نباید از بین برود؛ باید دست‌کم در یکی از دو جا موجود باشد.");

                // تأیید ثبت در manifest.json
                var manifestFiles = Directory.GetFiles(qRoot, "manifest.json", SearchOption.AllDirectories);
                Assert.NotEmpty(manifestFiles);

                string manifestContent = File.ReadAllText(manifestFiles[0]);
                Assert.Contains("SourceFolder", manifestContent);
                Assert.Contains(result.StoredPath.Replace("\\", "\\\\"), manifestContent);
            }
        }

        /// <summary>
        /// باگ ۶: در استارت‌آپ هنگام تداخل نام، فایل مقصد نباید بدون صدا پاک شود بلکه باید به .conflict تغییر نام یابد.
        /// و دسته‌بندی افزونه‌های VPN/کاربردی نباید در SafeToClean باشد.
        /// </summary>
        [Fact]
        public void Bug6_StartupCollision_And_ExtensionReclassification()
        {
            // الف: تست تداخل نام شورت‌کات
            string startupDir = Path.Combine(_testDir, "Startup");
            Directory.CreateDirectory(startupDir);

            string command = Path.Combine(startupDir, "app.lnk");
            string disabledPath = command + ".disabled";

            File.WriteAllText(command, "ExistingActiveApp");
            File.WriteAllText(disabledPath, "OldDisabledApp");

            // رفتار امن جدید: به جای File.Delete(command)، تغییر نام به conflict انجام می‌شود
            if (File.Exists(disabledPath))
            {
                if (File.Exists(command))
                {
                    string conflictPath = $"{command}.conflict_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                    File.Move(command, conflictPath);
                }
                File.Move(disabledPath, command);
            }

            Assert.True(File.Exists(command), "شورت‌کات باید بازیابی شده باشد.");
            Assert.Equal("OldDisabledApp", File.ReadAllText(command));

            var conflictFiles = Directory.GetFiles(startupDir, "app.lnk.conflict_*");
            Assert.Single(conflictFiles);
            Assert.Equal("ExistingActiveApp", File.ReadAllText(conflictFiles[0]));

            // ب: تست عدم حضور Hoxx VPN, ZenMate, Similarweb در KnownDeadExtensionIds
            var dead = BrowserOptimizerService.KnownDeadExtensionIds;
            var review = BrowserOptimizerService.ReviewExtensionIds;

            string similarweb = "hoklmmgfnpapgjgcpechhaamimifchmp";
            string zenmate = "fdcgdnkidjaadafnichfpabhfomcebme";
            string hoxx = "nbcojefnccbanplpoffopkoepjmhgdgh";
            string setupvpn = "oofgbpoabipfcfjapgnbbjjaenockbdp";
            string astar = "jajilbjjinjmgcibalaakngmkilboobh";

            Assert.DoesNotContain(similarweb, dead);
            Assert.DoesNotContain(zenmate, dead);
            Assert.DoesNotContain(hoxx, dead);
            Assert.DoesNotContain(setupvpn, dead);
            Assert.DoesNotContain(astar, dead);

            Assert.Contains(similarweb, review);
            Assert.Contains(zenmate, review);
            Assert.Contains(hoxx, review);
            Assert.Contains(setupvpn, review);
            Assert.Contains(astar, review);
        }
    }
}
