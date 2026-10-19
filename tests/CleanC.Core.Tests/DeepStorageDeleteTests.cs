// tests/CleanC.Core.Tests/DeepStorageDeleteTests.cs
// تست های رفتاری حذف در Deep Storage:
// ۱) زباله/کش همیشه قطعی حذف می شود (حتی وقتی حالت سطل بازیافت خواسته شود)
// ۲) حالت Permanent واقعاً فایل را پاک می کند و فضای آزادشده را گزارش می دهد
// ۳) حذف پوشه های خالی فقط پوشه ی واقعاً خالی را می برد
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CleanC.Core.Services;

namespace CleanC.Core.Tests
{
    public class DeepStorageDeleteTests : IDisposable
    {
        private readonly string _work;

        public DeepStorageDeleteTests()
        {
            // مسیر تست داخل %TEMP% ساخته می شود تا از شاخه ی junk شناسایی شود.
            _work = Path.Combine(Path.GetTempPath(), "cleanc-deepdelete", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_work);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_work)) Directory.Delete(_work, true);
            }
            catch
            {
                // پاکسازی تست نباید نتیجه را خراب کند
            }
        }

        private string NewFile(string name, int size = 1024)
        {
            string path = Path.Combine(_work, name);
            File.WriteAllBytes(path, Enumerable.Repeat((byte)0x41, size).ToArray());
            return path;
        }

        [Fact]
        public async Task DeleteMessItems_JunkPath_IsPermanentEvenWhenRecycleRequested()
        {
            string file = NewFile("temp-junk.log");
            long length = new FileInfo(file).Length;

            var result = await DeepStorageCleanService.DeleteMessItemsAsync(
                new[] { file }, SafeDeleteMode.RecycleBin);

            Assert.False(File.Exists(file));
            Assert.Equal(1, result.PermanentCount);
            Assert.Equal(0, result.RecycledCount);
            Assert.Equal(length, result.FreedBytes);
        }

        [Fact]
        public async Task DeleteDuplicateFiles_PermanentMode_RemovesFileAndReportsFreedBytes()
        {
            string file = NewFile("duplicate.bin");
            long length = new FileInfo(file).Length;

            var result = await DeepStorageCleanService.DeleteDuplicateFilesAsync(
                new[] { file }, SafeDeleteMode.Permanent);

            Assert.False(File.Exists(file));
            Assert.Equal(1, result.PermanentCount);
            Assert.Equal(length, result.FreedBytes);
        }

        [Fact]
        public async Task DeleteEmptyFolders_RemovesOnlyTrulyEmptyFolder()
        {
            string empty = Path.Combine(_work, "empty-folder");
            string notEmpty = Path.Combine(_work, "non-empty-folder");
            Directory.CreateDirectory(empty);
            Directory.CreateDirectory(notEmpty);
            File.WriteAllText(Path.Combine(notEmpty, "keep.txt"), "keep");

            var result = await DeepStorageCleanService.DeleteEmptyFoldersAsync(new[] { empty, notEmpty });

            Assert.False(Directory.Exists(empty));
            Assert.True(Directory.Exists(notEmpty));
            Assert.Equal(1, result.PermanentCount);
        }

        [Fact]
        public void DeleteFiles_ProgressReportsEveryItem()
        {
            string a = NewFile("a.txt");
            string b = NewFile("b.txt");
            var reports = new List<(int Done, int Total)>();

            var result = SafeDeleteService.DeleteFiles(
                new[] { a, b }, SafeDeleteMode.Permanent,
                (done, total) => reports.Add((done, total)));

            Assert.Equal(2, result.PermanentCount);
            Assert.Equal(2, reports.Count);
            Assert.Equal((1, 2), reports[0]);
            Assert.Equal((2, 2), reports[1]);
        }
    }
}
