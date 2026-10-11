using System;
using System.IO;
using CleanC.Core.Services;
using Xunit;

namespace CleanC.Core.Tests
{
    [Collection("CcLog")]
    public class NewFeaturesTests
    {
        [Theory]
        [InlineData("127.0.0.1:10809", 10809)]
        [InlineData("127.0.0.1:7890;", 7890)]
        [InlineData("http=127.0.0.1:8080;https=127.0.0.1:8080", 8080)]
        [InlineData("localhost:2081", 2081)]
        [InlineData("127.0.0.1", 0)]
        [InlineData("", 0)]
        [InlineData(null, 0)]
        public void ExtractPort_ParsesCorrectly(string? proxyStr, int expectedPort)
        {
            int port = NetworkProxyDiagnosticService.ExtractPort(proxyStr ?? string.Empty);
            Assert.Equal(expectedPort, port);
        }

        [Fact]
        public void DetectGhostProxyConflict_DoesNotThrow()
        {
            var exception = Record.Exception(() =>
            {
                var result = NetworkProxyDiagnosticService.DetectGhostProxyConflict();
                Assert.NotNull(result);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void ScanOrphanedServices_DoesNotThrow()
        {
            var exception = Record.Exception(() =>
            {
                var list = RegistryCleanerService.ScanOrphanedServices();
                Assert.NotNull(list);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void GetWslDockerVhdxFiles_DoesNotThrow()
        {
            var exception = Record.Exception(() =>
            {
                var list = DiskMaintenanceService.GetWslDockerVhdxFiles();
                Assert.NotNull(list);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void BrowserOptimizer_ScanExtensions_DoesNotThrow()
        {
            var exception = Record.Exception(() =>
            {
                var list = BrowserOptimizerService.ScanExtensions();
                Assert.NotNull(list);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void BrowserOptimizer_ProtectedWhitelist_ContainsEssentialExtensions()
        {
            Assert.Contains("ngpampappnmepgilojfohadhhmbhlaek", BrowserOptimizerService.ProtectedExtensionIds); // IDM
            Assert.Contains("aapbdbdomjkkjkaonfhkkikfgjllcleb", BrowserOptimizerService.ProtectedExtensionIds); // Translate
            Assert.Contains("pjpmebofipgpjaincgoboibbmicccbne", BrowserOptimizerService.ProtectedExtensionIds); // AI RTL (Persian/Farsi)
        }

        [Fact]
        public void BrowserOptimizer_KnownDeadExtensions_ContainsDeprecatedAndRedundantTools()
        {
            Assert.Contains("diimdfmlegodnjbcgajllidffehegdbc", BrowserOptimizerService.KnownDeadExtensionIds); // RTL toggle
            Assert.Contains("hbapdpeemoojbophdfndmlgdhppljgmp", BrowserOptimizerService.KnownDeadExtensionIds); // Keywords Everywhere
            Assert.Contains("jabopobgcpjmedljpbcaablpmlmfcogm", BrowserOptimizerService.KnownDeadExtensionIds); // WhatFont
            Assert.Contains("nkeimhogjdpnpccoofpliimaahmaaome", BrowserOptimizerService.KnownDeadExtensionIds); // Google Hangouts
            Assert.Contains("nmmhkkegccagdldgiimedpiccmgmieda", BrowserOptimizerService.KnownDeadExtensionIds); // Web Store Payments
        }

        [Fact]
        public void SafeCleanFolder_WithMinAge_PreservesRecentFilesAndDeletesOldFiles()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "CleanC_test_cache_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string recentFile = Path.Combine(tempDir, "recent_cache.dat");
                string oldFile = Path.Combine(tempDir, "old_cache.dat");

                File.WriteAllText(recentFile, "active fast cache content");
                File.WriteAllText(oldFile, "stale zombie cache content");

                // Set timestamps: recent is 2 days ago, old is 30 days ago
                File.SetLastWriteTimeUtc(recentFile, DateTime.UtcNow.AddDays(-2));
                File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-30));

                long freed = CleanService.SafeCleanFolder(tempDir, minAge: TimeSpan.FromDays(14));

                Assert.True(File.Exists(recentFile), "Recent cache must be preserved for fast page loads!");
                Assert.False(File.Exists(oldFile), "Old cache must be cleaned!");
                Assert.True(freed > 0, "Freed bytes should reflect deleted old file.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void CleanOrphanedExtensionStorage_DoesNotThrow()
        {
            var exception = Record.Exception(() =>
            {
                var result = BrowserOptimizerService.CleanOrphanedExtensionStorage();
                Assert.True(result.cleanedCount >= 0);
            });

            Assert.Null(exception);
        }

        [Fact]
        public async System.Threading.Tasks.Task ScanSystemAsync_ContainsAllNewOptimizationItems()
        {
            var scan = await SystemScanService.ScanAsync();
            Assert.NotNull(scan);

            var allItems = new System.Collections.Generic.List<CleanC.Core.Models.JunkCategory>();
            foreach (var grp in scan.Groups)
            {
                allItems.AddRange(grp.Items);
            }

            // Verify items exist
            Assert.Contains(allItems, x => x.Id == "emptyFolders");
            Assert.Contains(allItems, x => x.Id == "phoneBackups");
            Assert.Contains(allItems, x => x.Id == "browserDeadExtensions");
            Assert.Contains(allItems, x => x.Id == "oldInstallers");
            Assert.Contains(allItems, x => x.Id == "largeUserFiles");
            Assert.Contains(allItems, x => x.Id == "inactiveHeavyApps");

            // Verify powerPlanBoost is NOT in scan
            Assert.DoesNotContain(allItems, x => x.Id == "powerPlanBoost");
        }
    }
}
