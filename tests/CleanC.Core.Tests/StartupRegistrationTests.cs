// tests/CleanC.Core.Tests/StartupRegistrationTests.cs
// پوشش F1: ثبت/حذف CleanC در آیتم های اجرای خودکار ویندوز.
// به جای دست زدن به HKCU\...\Run واقعی، کلید به یک مسیر موقت منتقل می شود.

using System;
using System.IO;
using CleanC.Core.Services;
using Microsoft.Win32;

namespace CleanC.Core.Tests
{
    public sealed class StartupRegistrationTests : IDisposable
    {
        private readonly string _runKeyPath = $@"Software\CleanC.Tests\{Guid.NewGuid():N}";
        private readonly string _realFile;

        public StartupRegistrationTests()
        {
            _realFile = typeof(StartupRegistrationTests).Assembly.Location;
            StartupOptimizerService.SetRunKeyPathOverrideForTests(_runKeyPath);
            StartupOptimizerService.SetExecutableOverrideForTests(_realFile);
        }

        public void Dispose()
        {
            StartupOptimizerService.SetRunKeyPathOverrideForTests(null);
            StartupOptimizerService.SetExecutableOverrideForTests(null);

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(_runKeyPath, false);
            }
            catch (Exception)
            {
                // پاک سازی تستی است؛ شکستش نباید تست را خراب کند
            }
        }

        [Fact]
        public void RegisterThenUnregister_RoundTrips()
        {
            Assert.True(StartupOptimizerService.SetCleanCStartupRegistration(true));
            Assert.True(StartupOptimizerService.IsCleanCRegisteredAtStartup());

            Assert.True(StartupOptimizerService.SetCleanCStartupRegistration(false));
            Assert.False(StartupOptimizerService.IsCleanCRegisteredAtStartup());
        }

        [Fact]
        public void IsRegistered_WhenRegisteredValuePointsElsewhere_ReturnsFalse()
        {
            StartupOptimizerService.SetCleanCStartupRegistration(false);
            StartupOptimizerService.SetExecutableOverrideForTests(
                Path.Combine(Path.GetTempPath(), "CleanC-Not-Installed.exe"));

            Assert.False(StartupOptimizerService.IsCleanCRegisteredAtStartup());
        }

        [Fact]
        public void Register_WhenExecutableMissing_ReturnsFalseAndWritesNothing()
        {
            StartupOptimizerService.SetExecutableOverrideForTests(
                Path.Combine(Path.GetTempPath(), "CleanC-Not-Installed.exe"));

            Assert.False(StartupOptimizerService.SetCleanCStartupRegistration(true));
            Assert.False(StartupOptimizerService.IsCleanCRegisteredAtStartup());
        }

        [Fact]
        public void Unregister_WhenNothingRegistered_StillSucceeds()
        {
            Assert.True(StartupOptimizerService.SetCleanCStartupRegistration(false));
            Assert.False(StartupOptimizerService.IsCleanCRegisteredAtStartup());
        }
    }
}
