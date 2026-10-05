// BootImpactServiceTests.cs
using CleanC.Core.Services;

namespace CleanC.Core.Tests
{
    /// <summary>
    /// تست‌های واحدِ F6 (امتیاز تأثیر روی بوت).
    /// تست اینجا «تئوری» است: فقط منطقِ محاسبه و نرمال‌سازی نام فایل
    /// بدون دست زدن به رجیستری/رویدادِ ویندوز بررسی می‌شود.
    /// </summary>
    public class BootImpactServiceTests
    {
        [Theory]
        // مسیرِ رویدادهای ویندوز (Device path)
        [InlineData(@"\Device\HarddiskVolume3\Windows\System32\foo.exe", "foo.exe")]
        // مسیر عادی + آرگومان
        [InlineData(@"C:\Program Files\Bar\foo.exe --flag", "foo.exe")]
        // نقل‌قول‌دار و آرگومان
        [InlineData("\"C:\\a\\foo.exe\" -x", "foo.exe")]
        // فقط نام، بدون مسیر
        [InlineData("foo.exe", "foo.exe")]
        // نام فایلی که exe نیست
        [InlineData(@"\Device\HarddiskVolume3\foo.dll", "foo.dll")]
        [InlineData("", "")]
        public void NormalizeFileName_ExtractsExecutableName(string input, string expected)
        {
            Assert.Equal(expected, BootImpactService.NormalizeFileName(input));
        }

        [Fact]
        public void AssignImpact_UsesMeasuredDegradation_WhenEventExists()
        {
            var item = new StartupItem { Command = @"C:\Tools\heavy.exe", Category = "برنامه کاربردی (اختیاری)" };
            var impacts = new Dictionary<string, BootImpact>
            {
                ["heavy.exe"] = new BootImpact { FileName = "heavy.exe", TotalMs = 9000, DegradationMs = 7495, Samples = 3 }
            };

            BootImpactService.AssignImpact(item, impacts);

            Assert.True(item.ImpactMeasured);
            Assert.Equal(7495, item.ImpactMs);
            Assert.Equal(74, item.ImpactScore);
            Assert.Equal("زیاد", item.ImpactLabel);
        }

        [Fact]
        public void AssignImpact_FallsBackToTotal_WhenDegradationIsZero()
        {
            var item = new StartupItem { Command = "light.exe" };
            var impacts = new Dictionary<string, BootImpact>
            {
                ["light.exe"] = new BootImpact { FileName = "light.exe", TotalMs = 400, DegradationMs = 0 }
            };

            BootImpactService.AssignImpact(item, impacts);

            Assert.True(item.ImpactMeasured);
            Assert.Equal(400, item.ImpactMs);
            Assert.Equal(4, item.ImpactScore);
            Assert.Equal("کم", item.ImpactLabel);
        }

        [Fact]
        public void AssignImpact_UsesCategoryEstimate_WhenNoEvent()
        {
            var impacts = new Dictionary<string, BootImpact>();

            var gaming = new StartupItem { Command = @"C:\Steam\steam.exe", Category = "لانچر بازی و گیمینگ (اختیاری)" };
            var vpn = new StartupItem { Command = "vpn.exe", Category = "فیلترشکن و پروکسی (VPN)" };
            var generic = new StartupItem { Command = "app.exe", Category = "برنامه کاربردی (اختیاری)" };

            BootImpactService.AssignImpact(gaming, impacts);
            BootImpactService.AssignImpact(vpn, impacts);
            BootImpactService.AssignImpact(generic, impacts);

            Assert.False(gaming.ImpactMeasured);
            Assert.Equal(75, gaming.ImpactScore);
            Assert.Equal("زیاد", gaming.ImpactLabel);

            Assert.Equal(15, vpn.ImpactScore);
            Assert.Equal("کم", vpn.ImpactLabel);

            Assert.Equal(40, generic.ImpactScore);
            Assert.Equal("متوسط", generic.ImpactLabel);
        }

        [Fact]
        public void AssignImpact_AlwaysProducesValidScore_EvenOnOddInputs()
        {
            // نه استثنا پرتاب می‌شود، نه امتیاز از دامنه ۱..۱۰۰ بیرون می‌زند
            var empty = new StartupItem();
            BootImpactService.AssignImpact(empty, new Dictionary<string, BootImpact>());
            Assert.InRange(empty.ImpactScore, 1, 100);
            Assert.False(string.IsNullOrWhiteSpace(empty.ImpactLabel));

            var huge = new StartupItem { Command = "big.exe" };
            BootImpactService.AssignImpact(huge, new Dictionary<string, BootImpact>
            {
                ["big.exe"] = new BootImpact { FileName = "big.exe", TotalMs = 445_282, DegradationMs = 445_282 }
            });
            Assert.Equal(100, huge.ImpactScore);

            // null/بدون Category هم نباید خراب شود
            var noCategory = new StartupItem { Command = "x.exe", Category = "" };
            BootImpactService.AssignImpact(noCategory, new Dictionary<string, BootImpact>());
            Assert.InRange(noCategory.ImpactScore, 1, 100);
        }
    }
}
