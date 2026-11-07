using System;
using System.IO;
using System.Threading.Tasks;
using CleanC.Core.Services;
using Xunit;

namespace CleanC.Core.Tests
{
    [Collection("SafeDeleteTests")]
    public class P0P1SecurityTests : IDisposable
    {
        private readonly string _testDir;

        public P0P1SecurityTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "WinyWash_P0P1_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try
            {
                SafeDeleteService.ResetSessionForTesting();
                SafeDeleteService.QuarantineRoot = null!;
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch { }
        }

        /// <summary>
        /// P0-1: فایل‌های شخصی کاربر در صورت مشخص نشدن mode، هرگز دائمی (Permanent) پاک نمی‌شوند
        /// و پیش‌فرض برنامه (RecycleBin یا Quarantine) روی آن‌ها اعمال می‌شود.
        /// </summary>
        [Fact]
        public async Task P0_1_UserPersonalFiles_DefaultToNonPermanent()
        {
            string userDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "WinyWash_UserDocTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(userDir);
            try
            {
                string userFile = Path.Combine(userDir, "MyPersonalDocument.docx");
                File.WriteAllText(userFile, "Important user content");

                var result = await DeepStorageCleanService.DeleteMessItemsAsync(new[] { userFile }, mode: null);

                Assert.Equal(1, result.Total);
                Assert.Equal(0, result.PermanentCount);
                Assert.True(result.RecycledCount > 0 || result.QuarantinedCount > 0, "فایل شخصی در حالت mode=null باید بازیافت یا قرنطینه شود نه دائمی.");
            }
            finally
            {
                try { if (Directory.Exists(userDir)) Directory.Delete(userDir, true); } catch { }
            }
        }

        /// <summary>
        /// P0-4: CopyDirectory اتریبیوت ReparsePoint (Junction / Symlink) را رد می‌کند
        /// و محتویات لینک‌شده را کپی نمی‌کند.
        /// </summary>
        [Fact]
        public void P0_4_CopyDirectory_SkipsReparsePoints()
        {
            string sourceDir = Path.Combine(_testDir, "SourceWithLink");
            string normalSub = Path.Combine(sourceDir, "NormalSub");
            string targetSub = Path.Combine(_testDir, "ExternalTarget");
            string linkedSub = Path.Combine(sourceDir, "LinkedJunction");

            Directory.CreateDirectory(normalSub);
            Directory.CreateDirectory(targetSub);

            File.WriteAllText(Path.Combine(normalSub, "normal.txt"), "normal file");
            File.WriteAllText(Path.Combine(targetSub, "target_secret.txt"), "should not be copied");

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /j \"{linkedSub}\" \"{targetSub}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit(5000);
            }
            catch { }

            if (Directory.Exists(linkedSub))
            {
                string copyDest = Path.Combine(_testDir, "CopiedDestination");
                bool result = SafeDeleteService.CopyDirectory(sourceDir, copyDest);

                Assert.True(result);
                Assert.True(File.Exists(Path.Combine(copyDest, "NormalSub", "normal.txt")));
                Assert.False(File.Exists(Path.Combine(copyDest, "LinkedJunction", "target_secret.txt")), "پوشه ReparsePoint نباید کپی شده باشد.");

                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c rmdir \"{linkedSub}\"") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(3000); } catch { }
            }
        }

        /// <summary>
        /// P0-5: اگر فایل تکی در قرنطینه کپی شد ولی حذف مبدأ به دلیل قفل شکست خورد،
        /// وضعیت QuarantinedSourceLocked برمی‌گرداند.
        /// </summary>
        [Fact]
        public void P0_5_FileLockedSource_ReturnsQuarantinedSourceLocked()
        {
            string sourceFile = Path.Combine(_testDir, "locked_single_file.dat");
            File.WriteAllText(sourceFile, "locked payload data");

            string quarantineDir = Path.Combine(_testDir, "QuarantineLockDir");
            SafeDeleteService.QuarantineRoot = quarantineDir;
            SafeDeleteService.ResetSessionForTesting();

            using (var lockStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var deleteResult = SafeDeleteService.DeleteFile(sourceFile, SafeDeleteMode.Quarantine);

                Assert.Equal(SafeDeleteOutcome.QuarantinedSourceLocked, deleteResult.Outcome);
                Assert.NotNull(deleteResult.StoredPath);
                Assert.True(File.Exists(deleteResult.StoredPath), "کپی فایل در قرنطینه باید وجود داشته باشد.");
                Assert.True(File.Exists(sourceFile), "مبدأ قفل‌شده نباید حذف شده باشد.");
            }
        }

        /// <summary>
        /// P1-9: بررسی امن مسیرهای آن‌اینستالر قبل از اجرا.
        /// </summary>
        [Fact]
        public void P1_9_LaunchWin32Uninstaller_RejectsSuspiciousOrMissingPaths()
        {
            Assert.True(BloatwareService.IsValidUninstallerPath("msiexec.exe"));
            Assert.True(BloatwareService.IsValidUninstallerPath("MsiExec.exe"));

            Assert.False(BloatwareService.IsValidUninstallerPath(string.Empty));
            Assert.False(BloatwareService.IsValidUninstallerPath("   "));
            Assert.False(BloatwareService.IsValidUninstallerPath(@"C:\Windows\Temp\malicious_setup.exe"));
            Assert.False(BloatwareService.IsValidUninstallerPath(@"C:\NonExistentDirectory\uninst.exe"));
            Assert.False(BloatwareService.IsValidUninstallerPath(@"D:\RandomFolder\setup.exe"));
        }
    }
}
