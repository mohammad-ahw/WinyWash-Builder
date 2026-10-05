// tests/CleanC.Core.Tests/SafeDeleteServiceTests.cs
// تست‌های F3 — حذف امنِ فایل‌های شخصی: سطل بازیافت / قرنطینه / قطعی.

using System;
using System.IO;
using System.Linq;
using CleanC.Core.Services;

namespace CleanC.Core.Tests
{
    public class SafeDeleteServiceTests : IDisposable
    {
        private readonly string _root;
        private readonly string _work;

        public SafeDeleteServiceTests()
        {
            // ریشه‌ی قرنطینه هیچ‌وقت نباید به داده‌ی واقعی کاربر بزند
            _root = Path.Combine(Path.GetTempPath(), "cleanc-tests", Guid.NewGuid().ToString("N"));
            _work = Path.Combine(Path.GetTempPath(), "cleanc-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_work);

            SafeDeleteService.QuarantineRoot = _root;
            SafeDeleteService.DefaultMode = SafeDeleteMode.RecycleBin;
        }

        public void Dispose()
        {
            SafeDeleteService.QuarantineRoot = null!;
            SafeDeleteService.DefaultMode = SafeDeleteMode.RecycleBin;

            TryDelete(_work);
            TryDelete(_root);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // پاک‌سازی محیط تست در برابر قفل‌بودن فایل شکست می‌خورد؛ مهم نیست
            }
        }

        private string NewFile(string name, string content = "hello cleanc")
        {
            string path = Path.Combine(_work, name);
            File.WriteAllText(path, content);
            return path;
        }

        // ── نگاشت تنظیمات ────────────────────────────────────────────────

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("nonsense")]
        public void ParseMode_UnknownValue_FallsBackToRecycleBin(string? raw)
        {
            Assert.Equal(SafeDeleteMode.RecycleBin, SafeDeleteService.ParseMode(raw));
        }

        [Theory]
        [InlineData("Quarantine")]
        [InlineData("quarantine")]
        [InlineData("  QUARANTINE  ")]
        public void ParseMode_AcceptsCaseAndWhitespace(string raw)
        {
            Assert.Equal(SafeDeleteMode.Quarantine, SafeDeleteService.ParseMode(raw));
        }

        [Fact]
        public void ModeToString_ParseMode_RoundTrips()
        {
            foreach (var mode in new[] { SafeDeleteMode.RecycleBin, SafeDeleteMode.Quarantine, SafeDeleteMode.Permanent })
            {
                Assert.Equal(mode, SafeDeleteService.ParseMode(SafeDeleteService.ModeToString(mode)));
            }
        }

        // ── قرنطینه ──────────────────────────────────────────────────────

        [Fact]
        public void QuarantineMode_MovesFile_AndCountsMovedBytesOnly()
        {
            string file = NewFile("quarantine-me.txt");

            var result = SafeDeleteService.DeleteFile(file, SafeDeleteMode.Quarantine);

            Assert.Equal(SafeDeleteOutcome.Quarantined, result.Outcome);
            Assert.True(result.IsSuccess);
            Assert.False(result.FreesSpace, "جابه‌جایی نباید فضا را آزاد نشان دهد");
            Assert.False(File.Exists(file));
        }

        [Fact]
        public void QuarantineBatch_MovedBytesReported_FreedBytesZero()
        {
            string a = NewFile("a.txt");
            string b = NewFile("b.txt");
            long size = new FileInfo(a).Length + new FileInfo(b).Length;

            var batch = SafeDeleteService.DeleteFiles(new[] { a, b }, SafeDeleteMode.Quarantine);

            Assert.Equal(2, batch.QuarantinedCount);
            Assert.Equal(0, batch.PermanentCount);
            Assert.Equal(0, batch.FreedBytes);
            Assert.True(batch.MovedBytes >= size, "حجم جابه‌جا شده باید ثبت شود");
        }

        [Fact]
        public void Quarantine_CreatesManifest_AndListsSession()
        {
            string file = NewFile("tracked.txt");
            SafeDeleteService.DeleteFile(file, SafeDeleteMode.Quarantine);

            var sessions = SafeDeleteService.ListQuarantineSessions();

            Assert.NotEmpty(sessions);
            var session = Assert.Single(sessions);
            Assert.Single(session.Entries);
            Assert.Equal(file, session.Entries[0].OriginalPath);
            Assert.True(File.Exists(session.Entries[0].StoredPath));
        }

        [Fact]
        public void Quarantine_RestoreSession_ReturnsFileToOriginalPath()
        {
            string file = NewFile("restore-me.txt", "original content");
            SafeDeleteService.DeleteFile(file, SafeDeleteMode.Quarantine);

            string sessionId = SafeDeleteService.ListQuarantineSessions().Single().SessionId;
            var (restored, failed, _) = SafeDeleteService.RestoreSession(sessionId);

            Assert.Equal(1, restored);
            Assert.Equal(0, failed);
            Assert.True(File.Exists(file));
            Assert.Equal("original content", File.ReadAllText(file));
        }

        [Fact]
        public void Quarantine_DeleteSession_RemovesStoredCopies()
        {
            string file = NewFile("throw-away.txt");
            SafeDeleteService.DeleteFile(file, SafeDeleteMode.Quarantine);

            var session = SafeDeleteService.ListQuarantineSessions().Single();
            string stored = session.Entries[0].StoredPath;

            Assert.True(SafeDeleteService.DeleteSession(session.SessionId));
            Assert.False(File.Exists(stored));
            Assert.Empty(SafeDeleteService.ListQuarantineSessions());
        }

        [Fact]
        public void QuarantineMode_MovesDirectoryTree()
        {
            string dir = Path.Combine(_work, "folder");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "inner.txt"), "tree");

            var result = SafeDeleteService.DeleteDirectory(dir, SafeDeleteMode.Quarantine);

            Assert.Equal(SafeDeleteOutcome.Quarantined, result.Outcome);
            Assert.False(Directory.Exists(dir));

            var session = SafeDeleteService.ListQuarantineSessions().Single();
            Assert.True(session.Entries[0].IsDirectory);

            var (restored, failed, _) = SafeDeleteService.RestoreSession(session.SessionId);
            Assert.Equal(1, restored);
            Assert.Equal(0, failed);
            Assert.True(File.Exists(Path.Combine(dir, "inner.txt")));
        }

        [Fact]
        public void QuarantineMode_ReadOnlyFile_IsStillMoved()
        {
            string file = NewFile("locked.txt");
            File.SetAttributes(file, FileAttributes.ReadOnly);

            var result = SafeDeleteService.DeleteFile(file, SafeDeleteMode.Quarantine);

            Assert.Equal(SafeDeleteOutcome.Quarantined, result.Outcome);
            Assert.False(File.Exists(file));
        }

        [Fact]
        public void QuarantineRootChange_StartsANewSession()
        {
            string file1 = NewFile("one.txt");
            SafeDeleteService.DeleteFile(file1, SafeDeleteMode.Quarantine);

            string otherRoot = _root + "-second";
            SafeDeleteService.QuarantineRoot = otherRoot;
            try
            {
                string file2 = NewFile("two.txt");
                SafeDeleteService.DeleteFile(file2, SafeDeleteMode.Quarantine);

                // هر ریشه فقط نشستِ خودش را می‌بیند
                Assert.Single(SafeDeleteService.ListQuarantineSessions());
                Assert.Equal(otherRoot, SafeDeleteService.QuarantineRoot);
            }
            finally
            {
                SafeDeleteService.QuarantineRoot = null!;
                TryDelete(otherRoot);
            }
        }

        // ── حذف قطعی ─────────────────────────────────────────────────────

        [Fact]
        public void PermanentMode_DeletesFile_AndFreesSpace()
        {
            string file = NewFile("gone.txt");

            var result = SafeDeleteService.DeleteFile(file, SafeDeleteMode.Permanent);

            Assert.Equal(SafeDeleteOutcome.Permanent, result.Outcome);
            Assert.True(result.FreesSpace);
            Assert.False(File.Exists(file));
        }

        [Fact]
        public void PermanentMode_ReadOnlyFile_IsDeleted()
        {
            string file = NewFile("readonly.txt");
            File.SetAttributes(file, FileAttributes.ReadOnly);

            var result = SafeDeleteService.DeleteFile(file, SafeDeleteMode.Permanent);

            Assert.Equal(SafeDeleteOutcome.Permanent, result.Outcome);
            Assert.False(File.Exists(file));
        }

        [Fact]
        public void PermanentBatch_ReportsFreedBytesOnlyForRealDeletions()
        {
            string a = NewFile("x.txt");
            long size = new FileInfo(a).Length;

            var batch = SafeDeleteService.DeleteFiles(new[] { a, Path.Combine(_work, "missing.txt") },
                SafeDeleteMode.Permanent);

            Assert.Equal(1, batch.PermanentCount);
            Assert.Equal(1, batch.NotFoundCount);
            Assert.Equal(size, batch.FreedBytes);
            Assert.Equal(0, batch.MovedBytes);
            Assert.Equal(1, batch.SucceededCount);
        }

        [Fact]
        public void PermanentMode_MissingFile_CountsAsNotFound_NotFailed()
        {
            var result = SafeDeleteService.DeleteFile(Path.Combine(_work, "nope.txt"), SafeDeleteMode.Permanent);

            Assert.Equal(SafeDeleteOutcome.NotFound, result.Outcome);
            Assert.True(result.IsSuccess);
        }

        // ── زباله/کش همیشه قطعی ─────────────────────────────────────────

        [Fact]
        public void JunkDeletion_IsPermanent_EvenWhenDefaultModeIsQuarantine()
        {
            SafeDeleteService.DefaultMode = SafeDeleteMode.Quarantine;
            string file = NewFile("cache.bin");

            var result = SafeDeleteService.DeleteJunkFile(file);

            Assert.Equal(SafeDeleteOutcome.Permanent, result.Outcome);
            Assert.False(File.Exists(file));
            Assert.Empty(SafeDeleteService.ListQuarantineSessions());
        }

        [Fact]
        public void DefaultMode_IsUsedWhenModeArgumentIsOmitted()
        {
            SafeDeleteService.DefaultMode = SafeDeleteMode.Quarantine;
            string file = NewFile("default-mode.txt");

            var result = SafeDeleteService.DeleteFile(file);

            Assert.Equal(SafeDeleteOutcome.Quarantined, result.Outcome);
        }

        // ── متن‌های گزارش ────────────────────────────────────────────────

        [Theory]
        [InlineData(SafeDeleteMode.RecycleBin, "سطل بازیافت")]
        [InlineData(SafeDeleteMode.Quarantine, "قرنطینه")]
        [InlineData(SafeDeleteMode.Permanent, "برای همیشه حذف")]
        public void DescribeMode_MatchesWhatActuallyHappens(SafeDeleteMode mode, string expected)
        {
            Assert.Contains(expected, SafeDeleteService.DescribeMode(mode));
        }

        [Fact]
        public void DescribeBatch_SaysMovedNotFreed_WhenOnlyRecycled()
        {
            var batch = new SafeDeleteBatchResult
            {
                Total = 3,
                RecycledCount = 3,
                MovedBytes = 4096
            };

            string text = SafeDeleteService.DescribeBatch(batch);

            Assert.Contains("سطل بازیافت", text);
            Assert.Contains("جابه‌جا شد", text);
            Assert.DoesNotContain("فضا آزاد", text);
        }

        [Fact]
        public void DescribeBatch_ReportsFailuresAndFreedSpace()
        {
            var batch = new SafeDeleteBatchResult
            {
                Total = 3,
                PermanentCount = 2,
                FailedCount = 1,
                FreedBytes = 2048
            };

            string text = SafeDeleteService.DescribeBatch(batch);

            Assert.Contains("حذف قطعی شد", text);
            Assert.Contains("ناموفق", text);
            Assert.Contains("فضا آزاد", text);
        }
    }
}
