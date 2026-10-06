// SettingsServiceTests.cs
using CleanC.Core.Diagnostics;
using CleanC.Core.Services;

namespace CleanC.Core.Tests
{
    /// <summary>
    /// تست های سرویس تنظیمات — مهم ترین بخش این است که هیچ وقت استثنا پرتاب نکند
    /// و داده ی خراب (JSON ناقص، مقدار خارج از دامنه) به حالت پیش فرض برگردد
    /// نه اینکه برنامه را با تنظیمات نامعتبر بالا بیاورد.
    /// </summary>
    public class SettingsServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalSettingsPath;
        private readonly string _originalLogDir;

        public SettingsServiceTests()
        {
            // FilePath و LogDirectory هر دو استاتیک اند؛ مسیر واقعی کاربر هرگز نباید لمس شود
            _originalSettingsPath = SettingsService.FilePath;
            _originalLogDir = CcLog.LogDirectory;
            _tempDir = Path.Combine(Path.GetTempPath(), "cleanc-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            SettingsService.FilePath = Path.Combine(_tempDir, "settings.json");
            // لاگ هایی که حین تست (مثلاً JSON خراب) تولید می شوند هم باید در پوشه ی موقت بمانند
            CcLog.LogDirectory = Path.Combine(_tempDir, "logs");
        }

        public void Dispose()
        {
            SettingsService.FilePath = _originalSettingsPath;
            CcLog.LogDirectory = _originalLogDir;
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* پوشه ی موقت ممکن است هنوز توسط فرآیند دیگری در اختیار باشد */ }
        }

        [Fact]
        public void Load_WhenFileDoesNotExist_ReturnsDefaults()
        {
            var settings = SettingsService.Load();

            Assert.Equal("System", settings.Theme);
            Assert.True(settings.CloseToTray);
            Assert.False(settings.MinimizeToTray);
            Assert.Equal(1080, settings.WindowWidth);
            Assert.Equal(800, settings.WindowHeight);
            Assert.Equal("dashboard", settings.LastView);
            Assert.Empty(settings.JunkSelections);
        }

        [Fact]
        public void SaveThenLoad_RoundTripsEveryKnob()
        {
            var toSave = new AppSettings
            {
                Theme = "Dark",
                CloseToTray = false,
                MinimizeToTray = true,
                SaveWindowPlacement = false,
                SaveLastView = false,
                SaveScanSelections = false,
                WindowWidth = 1440,
                WindowHeight = 900,
                WindowX = 120,
                WindowY = 64,
                LastView = "settings"
            };
            toSave.JunkSelections["tempFiles"] = false;
            toSave.JunkSelections["recycleBin"] = true;

            SettingsService.Save(toSave);
            var loaded = SettingsService.Load();

            Assert.Equal("Dark", loaded.Theme);
            Assert.False(loaded.CloseToTray);
            Assert.True(loaded.MinimizeToTray);
            Assert.False(loaded.SaveWindowPlacement);
            Assert.False(loaded.SaveLastView);
            Assert.False(loaded.SaveScanSelections);
            Assert.Equal(1440, loaded.WindowWidth);
            Assert.Equal(900, loaded.WindowHeight);
            Assert.Equal(120, loaded.WindowX);
            Assert.Equal(64, loaded.WindowY);
            Assert.Equal("settings", loaded.LastView);
            Assert.False(loaded.JunkSelections["tempFiles"]);
            Assert.True(loaded.JunkSelections["recycleBin"]);
            Assert.True(loaded.HasSavedPlacement);
        }

        [Fact]
        public void Save_ReplacesExistingFileWithoutLeavingTempFile()
        {
            SettingsService.Save(new AppSettings { Theme = "Light" });
            SettingsService.Save(new AppSettings { Theme = "Dark" });

            var loaded = SettingsService.Load();
            Assert.Equal("Dark", loaded.Theme);
            Assert.False(File.Exists(SettingsService.FilePath + ".tmp"));
        }

        [Fact]
        public void Load_WhenJsonIsCorrupt_FallsBackToDefaultsInsteadOfThrowing()
        {
            File.WriteAllText(SettingsService.FilePath, "{ this is not json ][[");

            var settings = SettingsService.Load();

            Assert.Equal("System", settings.Theme);
            Assert.True(settings.CloseToTray);
        }

        [Fact]
        public void Load_WhenFileIsOnlyWhitespace_FallsBackToDefaults()
        {
            File.WriteAllText(SettingsService.FilePath, "   ");

            var settings = SettingsService.Load();

            Assert.Equal("dashboard", settings.LastView);
        }

        [Theory]
        [InlineData("blue", "System")]
        [InlineData("", "System")]
        [InlineData("dark", "Dark")]
        [InlineData("DARK", "Dark")]
        [InlineData("Light", "Light")]
        public void Load_NormalizesUnknownThemeValues(string stored, string expected)
        {
            File.WriteAllText(SettingsService.FilePath, $"{{\"Theme\":\"{stored}\"}}");

            Assert.Equal(expected, SettingsService.Load().Theme);
        }

        [Theory]
        [InlineData("tools", "tools")]
        [InlineData("settings", "settings")]
        [InlineData("dashboard", "dashboard")]
        [InlineData("inventory", "dashboard")]
        [InlineData("", "dashboard")]
        public void Load_NormalizesUnknownLastViewValues(string stored, string expected)
        {
            File.WriteAllText(SettingsService.FilePath, $"{{\"LastView\":\"{stored}\"}}");

            Assert.Equal(expected, SettingsService.Load().LastView);
        }

        [Theory]
        [InlineData("RecycleBin", "RecycleBin")]
        [InlineData("recyclebin", "RecycleBin")]
        [InlineData("Quarantine", "Quarantine")]
        [InlineData("permanent", "Permanent")]
        [InlineData("Permanent", "Permanent")]
        [InlineData("something-else", "RecycleBin")]
        [InlineData("", "RecycleBin")]
        public void Load_NormalizesDeleteModeValues(string stored, string expected)
        {
            File.WriteAllText(SettingsService.FilePath, $"{{\"DeleteMode\":\"{stored}\"}}");

            Assert.Equal(expected, SettingsService.Load().DeleteMode);
        }

        [Fact]
        public void Load_DefaultDeleteMode_IsRecycleBin()
        {
            File.WriteAllText(SettingsService.FilePath, "{}");

            Assert.Equal("RecycleBin", SettingsService.Load().DeleteMode);
        }

        [Fact]
        public void Load_CapsNotificationHistoryAtFiftyEntries()
        {
            var entries = new System.Text.StringBuilder();
            entries.Append("{\"NotificationHistory\":[");
            for (int i = 0; i < 80; i++)
            {
                if (i > 0) entries.Append(',');
                entries.Append($"{{\"Title\":\"t{i}\",\"Message\":\"m{i}\",\"Severity\":\"Informational\",\"At\":\"2026-01-01T{i / 60:D2}:{i % 60:D2}:00\"}}");
            }
            entries.Append("]}");

            File.WriteAllText(SettingsService.FilePath, entries.ToString());

            var settings = SettingsService.Load();
            Assert.Equal(50, settings.NotificationHistory.Count);
            // جدیدترین باید بالای فهرست باشد
            Assert.Equal("t79", settings.NotificationHistory[0].Title);
        }

        [Fact]
        public void Load_NormalizesNotificationSeverity_AndTrimsLongMessages()
        {
            string longMessage = new string('x', 1000);
            File.WriteAllText(SettingsService.FilePath,
                $"{{\"NotificationHistory\":[{{\"Title\":\"a\",\"Message\":\"{longMessage}\",\"Severity\":\"whatever\",\"At\":\"2026-01-01T00:00:00\"}}]}}");

            var entry = Assert.Single(SettingsService.Load().NotificationHistory);

            Assert.Equal("Informational", entry.Severity);
            Assert.Equal(400, entry.Message.Length);
        }

        [Fact]
        public void Load_WhenWindowSizeIsImpossiblySmall_UsesSaneDefault()
        {
            File.WriteAllText(SettingsService.FilePath, "{\"WindowWidth\":10,\"WindowHeight\":5}");

            var settings = SettingsService.Load();

            Assert.Equal(1080, settings.WindowWidth);
            Assert.Equal(800, settings.WindowHeight);
            Assert.False(settings.HasSavedPlacement);
        }

        [Fact]
        public void Load_WhenWindowWasNeverPositioned_ReportsNoSavedPlacement()
        {
            File.WriteAllText(SettingsService.FilePath, "{\"WindowWidth\":1200,\"WindowHeight\":700}");

            Assert.False(SettingsService.Load().HasSavedPlacement);
        }

        [Fact]
        public void LoadedJunkSelections_AreCaseInsensitiveForIds()
        {
            File.WriteAllText(SettingsService.FilePath, "{\"JunkSelections\":{\"TempFiles\":false}}");

            var settings = SettingsService.Load();

            // اسکنرها id را با حروف کوچک/مختلف می سازند؛ نباید کلید از دست برود
            Assert.True(settings.JunkSelections.ContainsKey("tempfiles"));
            Assert.False(settings.JunkSelections["TEMPFILES"]);
        }

        [Fact]
        public void Save_WithMissingDirectory_CreatesItAndSucceeds()
        {
            SettingsService.FilePath = Path.Combine(_tempDir, "nested", "deep", "settings.json");

            SettingsService.Save(new AppSettings { Theme = "Light" });

            Assert.True(File.Exists(SettingsService.FilePath));
            Assert.Equal("Light", SettingsService.Load().Theme);
        }

        [Fact]
        public void Save_WithNullArgument_DoesNotThrow()
        {
            SettingsService.Save(null!);
            Assert.True(Directory.Exists(_tempDir));
        }
    }
}
