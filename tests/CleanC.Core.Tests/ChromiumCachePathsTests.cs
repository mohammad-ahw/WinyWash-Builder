// ChromiumCachePathsTests.cs
using CleanC.Core.Services;

namespace CleanC.Core.Tests
{
    /// <summary>
    /// تست اسکن کش مرورگرهای کروم محور — فقط پروفایل های مجاز و فقط کش های شناخته شده.
    /// </summary>
    public class ChromiumCachePathsTests
    {
        [Fact]
        public void ReturnsOnlyKnownCacheFoldersOfKnownProfiles()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "CleanC_chrome_" + Guid.NewGuid().ToString("N").Substring(0, 8));

            Directory.CreateDirectory(Path.Combine(root, "Default", "Cache"));
            Directory.CreateDirectory(Path.Combine(root, "Default", "Code Cache"));
            Directory.CreateDirectory(Path.Combine(root, "Default", "NotACache"));
            Directory.CreateDirectory(Path.Combine(root, "Profile 2", "GPUCache"));
            Directory.CreateDirectory(Path.Combine(root, "RandomFolder", "Cache"));

            try
            {
                var paths = SystemScanService.GetChromiumCachePaths(root);

                Assert.Equal(3, paths.Count);
                Assert.Contains(paths, p => p.EndsWith(Path.Combine("Default", "Cache"), StringComparison.OrdinalIgnoreCase));
                Assert.Contains(paths, p => p.EndsWith(Path.Combine("Default", "Code Cache"), StringComparison.OrdinalIgnoreCase));
                Assert.Contains(paths, p => p.EndsWith(Path.Combine("Profile 2", "GPUCache"), StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void EmptyOrNullInput_ReturnsAnEmptyListWithoutThrowing()
        {
            Assert.Empty(SystemScanService.GetChromiumCachePaths(""));
            Assert.Empty(SystemScanService.GetChromiumCachePaths("   "));
            Assert.Empty(SystemScanService.GetChromiumCachePaths(null!));
            Assert.Empty(SystemScanService.GetChromiumCachePaths(
                Path.Combine(Path.GetTempPath(), "CleanC_does_not_exist_" + Guid.NewGuid().ToString("N"))));
        }
    }
}
