// LockRegistryServiceTests.cs
using CleanC.Core.Services;

namespace CleanC.Core.Tests
{
    /// <summary>
    /// تست های رجیستری قفل مسیرها (LockRegistryService).
    /// تضمین ها: قفل کاربر روی فایل ذخیره/بازخوانی می شود، نوع folder زیرمسیرها را
    /// هم می گیرد، قفل های پیش فرض همیشه فعالند و فایل خراب اپ را نمی شکند.
    /// </summary>
    public class LockRegistryServiceTests
    {
        private static string NewTempLocksPath() =>
            Path.Combine(Path.GetTempPath(), "CleanC_locktest_" + Guid.NewGuid().ToString("N") + ".json");

        /// <summary>هر تست روی فایل قفل مستقل خودش اجرا می شود تا به داده واقعی کاربر دست نزنیم.</summary>
        private static void WithFreshRegistry(Action<string> body)
        {
            string path = NewTempLocksPath();
            LockRegistryService.LocksFilePath = path;
            LockRegistryService.ResetCacheForTests();
            try
            {
                body(path);
            }
            finally
            {
                LockRegistryService.LocksFilePath = string.Empty; // برگشت به مسیر پیش فرض
                LockRegistryService.ResetCacheForTests();
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void AddLock_FolderType_PersistsAndCoversChildren()
        {
            WithFreshRegistry(path =>
            {
                Assert.True(LockRegistryService.AddLock(@"D:\CleanC_LockTest", LockTypes.Folder, "تست"));

                // فایل باید واقعاً روی دیسک نوشته شده باشد
                Assert.True(File.Exists(path));

                // کش را بی اعتبار کن → باید از فایل بازخوانی شود
                LockRegistryService.ResetCacheForTests();

                Assert.True(LockRegistryService.IsUserLockedPath(@"D:\CleanC_LockTest"));
                Assert.True(LockRegistryService.IsUserLockedPath(@"D:\CleanC_LockTest\sub\file.bin"));

                // مرز نام: پوشه ی همسایه که فقط پیشوند مشترک دارد نباید قفل شود
                Assert.False(LockRegistryService.IsUserLockedPath(@"D:\CleanC_LockTest2\file.bin"));
            });
        }

        [Fact]
        public void AddLock_PathType_MatchesExactPathOnly()
        {
            WithFreshRegistry(_ =>
            {
                Assert.True(LockRegistryService.AddLock(@"D:\CleanC_Exact", LockTypes.Path, "تست"));

                Assert.True(LockRegistryService.IsUserLockedPath(@"D:\CleanC_Exact"));
                Assert.False(LockRegistryService.IsUserLockedPath(@"D:\CleanC_Exact\child"));
            });
        }

        [Fact]
        public void Locks_AreCaseInsensitive()
        {
            WithFreshRegistry(_ =>
            {
                Assert.True(LockRegistryService.AddLock(@"d:\cleanC_case", LockTypes.Folder, "تست"));
                Assert.True(LockRegistryService.IsUserLockedPath(@"D:\CleanC_CaSe\X"));
            });
        }

        [Fact]
        public void RemoveLock_RemovesUserLock_ButKeepsDefaults()
        {
            WithFreshRegistry(_ =>
            {
                Assert.True(LockRegistryService.AddLock(@"D:\CleanC_Remove", LockTypes.Folder, "تست"));
                Assert.True(LockRegistryService.RemoveLock(@"D:\CleanC_Remove"));
                Assert.False(LockRegistryService.IsUserLockedPath(@"D:\CleanC_Remove"));

                // حذف قفل غیرموجود → false
                Assert.False(LockRegistryService.RemoveLock(@"D:\CleanC_NotThere"));

                // قفل پیش فرض (لیست نام ها) قابل حذف نیست چون اصلاً قفل کاربر نیست
                Assert.True(LockRegistryService.IsProtectedName("Steam"));
                Assert.False(LockRegistryService.RemoveLock("Steam"));
                Assert.True(LockRegistryService.IsProtectedName("Steam"));
            });
        }

        [Fact]
        public void Defaults_AreAlwaysActive_WithoutAnyFile()
        {
            WithFreshRegistry(_ =>
            {
                // نام های پیش فرض منتقل شده از LeftoverCleanerService
                Assert.True(LockRegistryService.IsProtectedName("Microsoft"));
                Assert.True(LockRegistryService.IsProtectedName("Documents"));
                Assert.True(LockRegistryService.IsProtectedName("$Recycle.Bin"));
                Assert.True(LockRegistryService.IsProtectedName("steam")); // بدون حساسیت به بزرگی/کوچکی

                Assert.False(LockRegistryService.IsProtectedName("SomeRandomLeftoverApp123"));

                // ریشه ی ویندوز قفل folderِ پیش فرض است (برای اسکن ها)
                string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                Assert.True(LockRegistryService.IsLockedPath(Path.Combine(windowsDir, "System32", "driver.dll")));

                // ولی برای حذف، فقط قفل کاربری وتو می کند → پیش فرض ها اینجا اعمال نمی شوند
                Assert.False(LockRegistryService.IsUserLockedPath(windowsDir));
            });
        }

        [Fact]
        public void GetAll_ContainsDefaultsAndUserLocks()
        {
            WithFreshRegistry(_ =>
            {
                Assert.True(LockRegistryService.AddLock(@"D:\CleanC_GetAll", LockTypes.Folder, "دلیل تست"));

                var all = LockRegistryService.GetAll();
                Assert.Contains(all, e => e.IsDefault && string.Equals(e.Path, "Steam", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(all, e => !e.IsDefault && string.Equals(e.Path, @"D:\CleanC_GetAll", StringComparison.OrdinalIgnoreCase));
            });
        }

        [Fact]
        public void CorruptFile_DoesNotThrow_AndCanBeRewritten()
        {
            WithFreshRegistry(path =>
            {
                File.WriteAllText(path, "{{{ this is not valid json");

                LockRegistryService.ResetCacheForTests();

                // فایل خراب → لیست خالی، بدون استثنا
                Assert.Empty(LockRegistryService.GetUserLocks());

                // نوشتن بعدی باید فایل را سالم بازسازی کند
                Assert.True(LockRegistryService.AddLock(@"D:\CleanC_Corrupt", LockTypes.Folder, "تست"));
                LockRegistryService.ResetCacheForTests();
                Assert.True(LockRegistryService.IsUserLockedPath(@"D:\CleanC_Corrupt"));
            });
        }

        [Fact]
        public void AddLock_RejectsInvalidInput()
        {
            WithFreshRegistry(_ =>
            {
                Assert.False(LockRegistryService.AddLock("", LockTypes.Folder, ""));
                Assert.False(LockRegistryService.AddLock(@"D:\X", "invalid-type", ""));
            });
        }
    }
}
