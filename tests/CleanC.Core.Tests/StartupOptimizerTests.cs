// StartupOptimizerTests.cs
using CleanC.Core.Services;

namespace CleanC.Core.Tests
{
    /// <summary>
    /// تست های دسته بندی آیتم های Startup — صرفاً منطق خالص بدون دست زدن به رجیستری.
    /// </summary>
    public class StartupOptimizerTests
    {
        [Theory]
        [InlineData("Clash Verge", "", true)]
        [InlineData("WireGuard", "", true)]
        [InlineData("NordVPN", "", true)]
        [InlineData("Tailscale", "", true)]
        [InlineData("Notepad", "", false)]
        [InlineData("Microsoft Edge", "", false)]
        [InlineData("Spotify", "", false)]
        public void IsVpnApp_MatchesOnlyKnownVpnSignatures(string name, string cmd, bool expected)
        {
            Assert.Equal(expected, StartupOptimizerService.IsVpnApp(name, cmd));
        }

        [Fact]
        public void ProtectedItems_GetTheirOwnCategory()
        {
            string @default = StartupOptimizerService.ResolveStartupCategory("SomeUnknownApp", "", false);
            string protectedItem = StartupOptimizerService.ResolveStartupCategory("SomeUnknownApp", "", true);

            Assert.False(string.IsNullOrWhiteSpace(@default));
            Assert.NotEqual(@default, protectedItem);
        }

        [Fact]
        public void BackgroundUpdaters_AreNotLumpedWithNormalApps()
        {
            string @default = StartupOptimizerService.ResolveStartupCategory("ReallyUnknownApp", "", false);
            string updater = StartupOptimizerService.ResolveStartupCategory("GoogleUpdateHelper", "", false);

            Assert.NotEqual(@default, updater);
        }

        [Fact]
        public void GameLaunchers_AreNotLumpedWithNormalApps()
        {
            string @default = StartupOptimizerService.ResolveStartupCategory("ReallyUnknownApp", "", false);
            string launcher = StartupOptimizerService.ResolveStartupCategory("Steam", "", false);

            Assert.NotEqual(@default, launcher);
        }

        [Fact]
        public void VpnApps_AreCategorizedAsVpn()
        {
            string vpn = StartupOptimizerService.ResolveStartupCategory("Clash Verge", "", false);
            string @default = StartupOptimizerService.ResolveStartupCategory("ReallyUnknownApp", "", false);

            Assert.NotEqual(@default, vpn);
        }
    }
}
