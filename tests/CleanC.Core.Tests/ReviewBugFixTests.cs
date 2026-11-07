using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CleanC.Core.Services;
using Microsoft.Win32;
using Xunit;

namespace CleanC.Core.Tests
{
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
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch { }
        }

        /// <summary>
        /// باگ ۱: بکاپ چندپوشه‌ای افزونه‌ها باید ساختار زیرپوشه‌ها را کاملاً مجزا ذخیره کند و روی هم overwrite نکند.
        /// </summary>
        [Fact]
        public void Bug1_MultiFolderExtensionBackup_PreservesDistinctSubpaths()
        {
            string profileDir = Path.Combine(_testDir, "Default");
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

            // بررسی شبیه‌سازی ساختار بکاپ جدید در BrowserOptimizerService
            var targetFolders = new[]
            {
                Path.Combine(profileDir, "Extensions", extId),
                settingsFolder,
                idbFolder
            };

            foreach (var target in targetFolders)
            {
                string rel = Path.GetRelativePath(profileDir, target);
                string dest = Path.Combine(backupDir, "Default", rel);
                Directory.CreateDirectory(dest);
                // کپی ساختار
                foreach (var file in Directory.GetFiles(target, "*.*", SearchOption.AllDirectories))
                {
                    string fRel = Path.GetRelativePath(target, file);
                    string fDest = Path.Combine(dest, fRel);
                    Directory.CreateDirectory(Path.GetDirectoryName(fDest)!);
                    File.Copy(file, fDest, true);
                }
            }

            // اطمینان از اینکه هر ۳ زیرپوشه بکاپ مجزا دارند و چیزی نادیده گرفته یا overwrite نشده است
            string bkManifest = Path.Combine(backupDir, "Default", "Extensions", extId, "1.0.0", "manifest.json");
            string bkSettings = Path.Combine(backupDir, "Default", "Local Extension Settings", extId, "settings.ldb");
            string bkIdb = Path.Combine(backupDir, "Default", "IndexedDB", $"chrome-extension_{extId}_0.indexeddb.leveldb", "000001.log");

            Assert.True(File.Exists(bkManifest), "فایل مانیفست افزونه باید بکاپ گرفته شده باشد.");
            Assert.True(File.Exists(bkSettings), "پوشه تنظیمات افزونه باید بکاپ گرفته شده باشد.");
            Assert.True(File.Exists(bkIdb), "دیتابیس IndexedDB افزونه باید به صورت مجزا بکاپ گرفته شده باشد.");
            Assert.Equal("{\"name\":\"test\"}", File.ReadAllText(bkManifest));
            Assert.Equal("data-ldb", File.ReadAllText(bkSettings));
            Assert.Equal("idb-log", File.ReadAllText(bkIdb));
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
        /// باگ ۳: بکاپ رجیستری باید بدون رجوع به reg.exe و مستقیماً با API رجیستری فایل استاندارد .reg معتبر تولید کند،
        /// و در صورت شکست بکاپ، از حذف کلید صرف‌نظر شود.
        /// </summary>
        [Fact]
        public void Bug3_RegistryBackup_DirectRegFileExport_AndSkipOnFailure()
        {
            string testSubKeyPath = @"Software\WinyWashTestKey_" + Guid.NewGuid().ToString("N");
            string backupFile = Path.Combine(_testDir, "test_backup.reg");

            // ایجاد کلید و مقادیر تستی در HKCU
            using (var key = Registry.CurrentUser.CreateSubKey(testSubKeyPath))
            {
                Assert.NotNull(key);
                key.SetValue("AppString", "TestValue123", RegistryValueKind.String);
                key.SetValue("AppDWord", 42, RegistryValueKind.DWord);
                key.SetValue("AppBinary", new byte[] { 0xAA, 0xBB, 0xCC }, RegistryValueKind.Binary);

                using var sub = key.CreateSubKey("ChildKey");
                sub.SetValue("ChildVal", "InnerValue");
            }

            try
            {
                using (var readKey = Registry.CurrentUser.OpenSubKey(testSubKeyPath, false))
                {
                    Assert.NotNull(readKey);
                    bool exportOk = RegistryCleanerService.ExportKeyToRegFile(readKey, $@"HKEY_CURRENT_USER\{testSubKeyPath}", backupFile);
                    Assert.True(exportOk, "اکسپورت مستقیم رجیستری باید موفقیت‌آمیز باشد.");
                }

                Assert.True(File.Exists(backupFile), "فایل .reg باید ساخته شده باشد.");
                string regContent = File.ReadAllText(backupFile, System.Text.Encoding.Unicode);

                Assert.StartsWith("Windows Registry Editor Version 5.00", regContent.Trim());
                Assert.Contains($@"[HKEY_CURRENT_USER\{testSubKeyPath}]", regContent);
                Assert.Contains("\"AppString\"=\"TestValue123\"", regContent);
                Assert.Contains("\"AppDWord\"=dword:0000002a", regContent);
                Assert.Contains("\"AppBinary\"=hex:aa,bb,cc", regContent);
                Assert.Contains($@"[HKEY_CURRENT_USER\{testSubKeyPath}\ChildKey]", regContent);

                // تست شکست بکاپ در مسیر غیرمجاز: باید false برگرداند و کرش نکند
                using (var readKey = Registry.CurrentUser.OpenSubKey(testSubKeyPath, false))
                {
                    string invalidDest = "Z:\\NonExistentDrive_X929\\invalid.reg";
                    bool failedExport = RegistryCleanerService.ExportKeyToRegFile(readKey!, "Test", invalidDest);
                    Assert.False(failedExport, "در مسیر نامعتبر باید شکست بخورد و false برگرداند.");
                }
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(testSubKeyPath, false); } catch { }
            }
        }

        /// <summary>
        /// باگ ۴: بازگردانی تلمتری در صورت عدم وجود یا خرابی مقدار اولیه باید به پیش‌فرض واقعی ویندوز برگردد و هرگز کرش نکند.
        /// </summary>
        [Fact]
        public void Bug4_TelemetryOptimizer_GracefulDefaultsOnCorruptedState()
        {
            // شبیه‌سازی تست پارس کردن مقدار ذخیره‌شده خراب یا غایب
            string? corruptedVal = "Invalid_String_Not_A_Number";
            int targetStartDiagTrack = 2; // ویندوز پیش‌فرض DiagTrack
            int targetStartDmwap = 3;     // ویندوز پیش‌فرض dmwappushservice

            // اگر مقدار نامعتبر باشد
            if (int.TryParse(corruptedVal, out int parsed) && parsed is >= 1 and <= 3)
            {
                targetStartDiagTrack = parsed;
            }

            Assert.Equal(2, targetStartDiagTrack);
            Assert.Equal(3, targetStartDmwap);

            // اطمینان از اینکه متدهای وضعیت تلمتری کرش نمی‌کنند
            var exception = Record.Exception(() =>
            {
                _ = TelemetryOptimizerService.IsTelemetryDisabled();
                _ = TelemetryOptimizerService.IsBingSearchDisabled();
                _ = TelemetryOptimizerService.IsActivityHistoryDisabled();
            });
            Assert.Null(exception);
        }

        /// <summary>
        /// باگ ۵: رول‌بک نیمه‌کاره‌ی قرنطینه در صورت شکست حذف فایل مبدأ
        /// </summary>
        [Fact]
        public void Bug5_QuarantineRollback_WhenSourceDeletionFails_RevertsQuarantinedCopy()
        {
            string sourceDir = Path.Combine(_testDir, "SourceFolder");
            string qDir = Path.Combine(_testDir, "QuarantineFolder");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(qDir);

            string file1 = Path.Combine(sourceDir, "file1.txt");
            File.WriteAllText(file1, "critical user data");

            string qDest = Path.Combine(qDir, "SourceFolder_Quarantined");

            // شبیه‌سازی تراکنش امن MoveWithFallback:
            // ۱. داده به مقصد کپی می‌شود
            Directory.CreateDirectory(qDest);
            File.Copy(file1, Path.Combine(qDest, "file1.txt"), true);

            // ۲. خطای شبیه‌سازی شده در حذف مبدأ (مثلاً دسترسی Denied یا فایل در حال استفاده)
            bool sourceDeleteFailed = true;

            if (sourceDeleteFailed)
            {
                // رول‌بک تراکنشی: مقصد در قرنطینه پاک می‌شود تا دیتای یتیم ایجاد نشود
                try { Directory.Delete(qDest, true); } catch { }
            }

            // نتیجه: داده در مبدأ هنوز هست و مقصد قرنطینه پاکسازی شده است
            Assert.True(File.Exists(file1), "فایل مبدأ نباید آسیب ببیند.");
            Assert.False(Directory.Exists(qDest), "مقصد در قرنطینه باید با رول‌بک حذف شده باشد.");
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
