// LeftoverCleanerSafetyTests.cs
using CleanC.Core.Services;

namespace CleanC.Core.Tests
{
    /// <summary>
    /// تست های ایمنی حذف پوشه های رهاشده.
    /// مهم ترین ضامن این است که یک باگ در اسکنر هرگز به حذف دسته ایِ پوشه های
    /// سیستمی یا شخصی منجر نشود.
    /// </summary>
    public class LeftoverCleanerSafetyTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(@"relative\path")]
        [InlineData(@"C:")]
        public void IsSafeToDelete_RejectsInvalidInput(string? path)
        {
            Assert.False(LeftoverCleanerService.IsSafeToDelete(path!));
        }

        [Theory]
        [InlineData(@"C:\Windows")]
        [InlineData(@"C:\Program Files")]
        [InlineData(@"C:\Users")]
        [InlineData(@"C:\")]
        public void IsSafeToDelete_RejectsProtectedSystemLocations(string path)
        {
            Assert.False(LeftoverCleanerService.IsSafeToDelete(path));
        }

        [Fact]
        public void IsSafeToDelete_AcceptsAFreshFolderDirectlyUnderTheUserProfile()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "CleanC_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));

            Directory.CreateDirectory(dir);
            try
            {
                // پسوند بی ربط تا قاعده ی «اسناد شخصی» فعال نشود
                File.WriteAllText(Path.Combine(dir, "dummy.xyz"), "x");

                Assert.True(LeftoverCleanerService.IsSafeToDelete(dir));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void IsSafeToDelete_StillRejectsTheSameFolderAfterItBecomesAGitRepo()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "CleanC_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));

            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "dummy.xyz"), "x");
                Assert.True(LeftoverCleanerService.IsSafeToDelete(dir));

                // وجود .git یعنی پروژه ی کاربر — دیگر حذف پذیر نیست
                Directory.CreateDirectory(Path.Combine(dir, ".git"));
                Assert.False(LeftoverCleanerService.IsSafeToDelete(dir));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
