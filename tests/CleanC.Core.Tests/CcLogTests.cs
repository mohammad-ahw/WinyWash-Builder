// CcLogTests.cs
using CleanC.Core.Diagnostics;

namespace CleanC.Core.Tests
{
    /// <summary>
    /// تست های لاگ مرکزیِ خطاهای نادیده گرفته شده.
    /// هر تست پوشه ی خروجی را به یک مسیر موقت منتقل می کند تا روی داده ی واقعی کاربر اثر نگذارد.
    /// </summary>
    public class CcLogTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalDir;

        public CcLogTests()
        {
            _originalDir = CcLog.LogDirectory;
            _tempDir = Path.Combine(Path.GetTempPath(),
                "CleanC_cclog_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            CcLog.LogDirectory = _tempDir;
        }

        public void Dispose()
        {
            CcLog.LogDirectory = _originalDir;
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public void Ignore_Null_IsIgnored()
        {
            long before = CcLog.TotalIgnored;
            CcLog.Ignore(null);
            Assert.Equal(before, CcLog.TotalIgnored);
        }

        [Fact]
        public void Ignore_Exception_IsCountedAndRemembered()
        {
            long before = CcLog.TotalIgnored;

            CcLog.Ignore(new InvalidOperationException("boom"), "unit-test");

            Assert.True(CcLog.TotalIgnored > before);
            Assert.True(CcLog.Recent().Count > 0);
        }

        [Fact]
        public void Recent_IsCappedSoALoopCannotEatMemory()
        {
            for (int i = 0; i < 400; i++)
                CcLog.Ignore(new IOException($"loop {i}"));

            Assert.True(CcLog.Recent().Count <= 256,
                $"Recent() must stay bounded but had {CcLog.Recent().Count} entries.");
        }

        [Fact]
        public void Flush_WritesThePendingEntriesToTheConfiguredDirectory()
        {
            CcLog.Ignore(new TimeoutException("flush me"));
            CcLog.Flush();

            string file = Path.Combine(_tempDir, "ignored.log");
            Assert.True(File.Exists(file), "expected ignored.log to be created");
            Assert.True(new FileInfo(file).Length > 0, "expected ignored.log to be non-empty");
        }

        [Fact]
        public void LoggingToABrokenPath_NeverThrows()
        {
            try
            {
                // مسیر نامعتبر → باید بی صدا رد شود، نه اینکه خودِ لاگ crash کند
                CcLog.LogDirectory = "\0not-a-path";
                CcLog.Ignore(new AccessViolationException("cannot write"));
                CcLog.Flush();
            }
            finally
            {
                CcLog.LogDirectory = _tempDir;
            }
        }
    }
}
