// FormatBytesTests.cs
using CleanC.Core.Models;

namespace CleanC.Core.Tests
{
    /// <summary>
    /// تست های فرمت بندی حجم — مرزهای عددی که قبلاً باعث خروجی غلط یا استثنا می شدند.
    /// </summary>
    public class FormatBytesTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-1024)]
        [InlineData(long.MinValue)]
        public void NonPositiveValues_AreAllReportedAsZero(long bytes)
        {
            // واحد صفر برای همه ی مقادیر غیرمثبت باید یکسان باشد
            Assert.Equal(SystemStats.FormatBytes(0), SystemStats.FormatBytes(bytes));
        }

        [Fact]
        public void OneKilobyte_UsesTheSecondUnit()
        {
            Assert.StartsWith("1 ", SystemStats.FormatBytes(1024));
        }

        [Fact]
        public void OneMegabyte_IsNotConfusedWithOneKilobyte()
        {
            string oneKb = SystemStats.FormatBytes(1024);
            string oneMb = SystemStats.FormatBytes(1024L * 1024);

            Assert.NotEqual(oneKb, oneMb);
            Assert.StartsWith("1 ", oneMb);
        }

        [Fact]
        public void FractionalValues_ProduceADifferentLabelThanTheRoundedOne()
        {
            Assert.NotEqual(SystemStats.FormatBytes(1024), SystemStats.FormatBytes(1536));
        }

        [Fact]
        public void VeryLargeValues_AreClampedInsteadOfOverflowingTheUnitArray()
        {
            // digitGroups اینجا ۶ است در حالی که آرایه ی واحدها فقط ۵ خانه دارد
            Assert.StartsWith("8388608", SystemStats.FormatBytes(long.MaxValue));
        }
    }
}
