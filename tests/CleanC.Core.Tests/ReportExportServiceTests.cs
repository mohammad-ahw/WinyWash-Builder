// ReportExportServiceTests.cs
using System.Text;
using CleanC.Core.Services;

namespace CleanC.Core.Tests
{
    /// <summary>
    /// تست‌های F7/D8 — ساخت خروجی TXT/CSV و نوشتن اتمیک با BOM.
    /// نکته‌ی محوری: BOM نباید دوبار نوشته شود (یکی از WriteUtf8Bom می‌آید)
    /// و متن فارسی/کاما/نقل‌قول باید سالم از CSV عبور کند.
    /// </summary>
    public class ReportExportServiceTests : IDisposable
    {
        private readonly string _tempDir;

        public ReportExportServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "cleanc-report-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { /* پوشه‌ی موقت تست */ }
        }

        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("has,comma", "\"has,comma\"")]
        [InlineData("has\"quote", "\"has\"\"quote\"")]
        [InlineData("line\nbreak", "\"line\nbreak\"")]
        [InlineData("", "")]
        public void EscapeCsv_FollowsRfc4180(string input, string expected)
        {
            Assert.Equal(expected, ReportExportService.EscapeCsv(input));
        }

        [Fact]
        public void BuildStartupCsv_HasHeaderAndEscapesValues()
        {
            var items = new List<StartupItem>
            {
                new StartupItem { Name = "app,with,commas", Location = "HKCU", Category = "dual \"quoted\"",
                                  ImpactLabel = "کم", ImpactScore = 12, ImpactMs = 0, Command = "C:\\x.exe" },
                new StartupItem { Name = "disabled-one", Location = "TaskScheduler", Category = "cat",
                                  IsDisabled = true, ImpactLabel = "زیاد", ImpactScore = 90, ImpactMs = 9000,
                                  ImpactMeasured = true, Command = "task" }
            };

            string csv = ReportExportService.BuildStartupCsv(items);
            var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(3, lines.Length);
            Assert.StartsWith("Name,Location,Category,Enabled,ImpactLabel", lines[0]);
            Assert.Contains("\"app,with,commas\"", lines[1]);
            Assert.Contains("\"dual \"\"quoted\"\"\"", lines[1]);
            Assert.Contains(",No,", lines[1]);
            Assert.Contains(",Yes,", lines[2]);
            Assert.Contains("9000", lines[2]);
        }

        [Fact]
        public void BuildCleanupHistoryCsv_WritesIsoDatesAndSizes()
        {
            var rows = new List<CleanupHistoryRow>
            {
                new CleanupHistoryRow { Date = "2026-10-05", Bytes = 1048576 },
                new CleanupHistoryRow { Date = "2026-10-04", Bytes = 0 }
            };

            string csv = ReportExportService.BuildCleanupHistoryCsv(rows);
            var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(3, lines.Length);
            Assert.Equal("Date,Bytes,Megabytes,Gigabytes", lines[0]);
            Assert.Equal("2026-10-05,1048576,1,0.001", lines[1]);
            Assert.Equal("2026-10-04,0,0,0", lines[2]);
        }

        [Fact]
        public void WriteUtf8Bom_WritesSingleBom_AndOverwritesAtomically()
        {
            string path = Path.Combine(_tempDir, "out.csv");
            ReportExportService.WriteUtf8Bom(path, "a,b\r\n1,2\r\n");

            byte[] bytes = File.ReadAllBytes(path);
            // فقط و فقط یک BOM (EF BB BF) — BOM دوم باعث می‌شود Excel فایل را باز نکند
            Assert.True(bytes.Length >= 3);
            Assert.Equal(0xEF, bytes[0]);
            Assert.Equal(0xBB, bytes[1]);
            Assert.Equal(0xBF, bytes[2]);
            Assert.NotEqual(0xEF, bytes[3]);
            Assert.Equal("a,b\r\n1,2\r\n", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
            Assert.False(File.Exists(path + ".tmp"));

            ReportExportService.WriteUtf8Bom(path, "second");
            Assert.Equal("second", File.ReadAllText(path, new UTF8Encoding(false)));
        }

        [Fact]
        public void BuildDiagnosticsText_ContainsVersionAndPaths()
        {
            string text = ReportExportService.BuildDiagnosticsText();

            Assert.StartsWith("=== CleanC", text);
            Assert.Contains("App Version", text);
            Assert.Contains("Framework", text);
            Assert.Contains("Settings File", text);
            Assert.Contains("Log Directory", text);
            Assert.Contains("Timestamp", text);
        }

        [Fact]
        public void GetVersion_StripsBuildHash()
        {
            string v = ReportExportService.GetVersion(typeof(ReportExportService).Assembly);
            Assert.False(string.IsNullOrWhiteSpace(v));
            Assert.DoesNotContain("+", v);
        }
    }
}
