// tests/CleanC.Core.Tests/RecycleBinServiceTests.cs
// پوشش F8 — پارس فایل اطلاعاتی $I و کمک‌تابع‌های نام‌گذاری سطل بازیافت.

using System;
using System.IO;
using System.Text;
using CleanC.Core.Services;
using Xunit;

namespace CleanC.Core.Tests
{
    public class RecycleBinServiceTests
    {
        private static byte[] BuildVersion2(string path, long size, DateTime deletedAt)
        {
            byte[] pathBytes = Encoding.Unicode.GetBytes(path);
            int chars = (pathBytes.Length / 2) + 1; // +1 یعنی کاراکتر null پایانی
            var buffer = new byte[28 + chars * 2];

            BitConverter.GetBytes(2L).CopyTo(buffer, 0);
            BitConverter.GetBytes(size).CopyTo(buffer, 8);
            BitConverter.GetBytes(deletedAt.ToFileTime()).CopyTo(buffer, 16);
            BitConverter.GetBytes(chars).CopyTo(buffer, 24);
            pathBytes.CopyTo(buffer, 28);

            return buffer;
        }

        private static byte[] BuildVersion1(string path, long size)
        {
            byte[] pathBytes = Encoding.Unicode.GetBytes(path);
            Assert.True(pathBytes.Length <= 520, "مسیر تستی باید در ۲۶۰ کاراکتر بگنجد");

            var buffer = new byte[536];
            BitConverter.GetBytes(1L).CopyTo(buffer, 0);
            pathBytes.CopyTo(buffer, 8);
            BitConverter.GetBytes(size).CopyTo(buffer, 528);

            return buffer;
        }

        [Fact]
        public void ParseV2_ReturnsPathSizeAndDeletionTime()
        {
            var deletedAt = new DateTime(2024, 1, 2, 3, 4, 5);
            byte[] buffer = BuildVersion2(@"C:\Users\test\Documents\report.pdf", 123456, deletedAt);

            var item = RecycleBinService.ParseInfoFile(buffer);

            Assert.NotNull(item);
            Assert.Equal(@"C:\Users\test\Documents\report.pdf", item!.OriginalPath);
            Assert.Equal("report.pdf", item.FileName);
            Assert.Equal(123456, item.SizeBytes);
            Assert.Equal(deletedAt, item.DeletedAt);
        }

        [Fact]
        public void ParseV1_ReturnsPathAndSizeWithUnknownDate()
        {
            byte[] buffer = BuildVersion1(@"C:\Temp\legacy-file.txt", 2048);

            var item = RecycleBinService.ParseInfoFile(buffer);

            Assert.NotNull(item);
            Assert.Equal(@"C:\Temp\legacy-file.txt", item!.OriginalPath);
            Assert.Equal("legacy-file.txt", item.FileName);
            Assert.Equal(2048, item.SizeBytes);
            Assert.Equal(DateTime.MinValue, item.DeletedAt);
        }

        [Fact]
        public void Parse_WhenBufferNull_ReturnsNull()
        {
            Assert.Null(RecycleBinService.ParseInfoFile(null));
        }

        [Fact]
        public void Parse_WhenBufferEmpty_ReturnsNull()
        {
            Assert.Null(RecycleBinService.ParseInfoFile(Array.Empty<byte>()));
        }

        [Fact]
        public void Parse_WhenBufferTruncated_ReturnsNull()
        {
            Assert.Null(RecycleBinService.ParseInfoFile(new byte[] { 1, 2, 3 }));
        }

        [Fact]
        public void Parse_WhenVersionUnknown_ReturnsNull()
        {
            var buffer = new byte[64];
            BitConverter.GetBytes(99L).CopyTo(buffer, 0);

            Assert.Null(RecycleBinService.ParseInfoFile(buffer));
        }

        [Fact]
        public void Parse_WhenDeclaredPathLongerThanBuffer_ReturnsNull()
        {
            var buffer = new byte[40];
            BitConverter.GetBytes(2L).CopyTo(buffer, 0);
            BitConverter.GetBytes((long)100).CopyTo(buffer, 8);
            BitConverter.GetBytes(0L).CopyTo(buffer, 16);
            BitConverter.GetBytes(5000).CopyTo(buffer, 24); // ۵۰۰۰ کاراکتر ادعا شده

            Assert.Null(RecycleBinService.ParseInfoFile(buffer));
        }

        [Fact]
        public void Parse_WhenPathEmpty_ReturnsNull()
        {
            byte[] buffer = BuildVersion2("   ", 10, DateTime.Now);

            Assert.Null(RecycleBinService.ParseInfoFile(buffer));
        }

        [Theory]
        [InlineData(@"C:\Users\me\Desktop\file.zip", "file.zip")]
        [InlineData(@"D:\folder\", "folder")]
        [InlineData("single.txt", "single.txt")]
        [InlineData("", "")]
        public void GetFileName_ReturnsLastPathSegment(string path, string expected)
        {
            Assert.Equal(expected, RecycleBinService.GetFileName(path));
        }

        [Fact]
        public void MakeUniquePath_WhenTargetExists_AddsRestoredSuffix()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cleanc-rb-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                string original = Path.Combine(dir, "notes.txt");
                string firstSuffix = Path.Combine(dir, "notes (restored).txt");
                string expected = Path.Combine(dir, "notes (restored 2).txt");

                File.WriteAllText(original, "original");
                File.WriteAllText(firstSuffix, "already restored once");

                string unique = RecycleBinService.MakeUniquePath(original);

                Assert.Equal(expected, unique);
                Assert.NotEqual(original, unique);
                Assert.False(File.Exists(unique), "مسیر برگشتی باید خالی باشد");
            }
            finally
            {
                try { Directory.Delete(dir, true); }
                catch (Exception) { /* پاک‌سازی تستی */ }
            }
        }

        [Fact]
        public void ListItems_OnRealMachine_DoesNotThrow()
        {
            // سطل بازیافت واقعی سیستم تست را می‌خوانیم؛ خطاهای دسترسی باید بلعیده شوند
            var items = RecycleBinService.ListItems();

            Assert.NotNull(items);
            Assert.All(items, item =>
            {
                Assert.False(string.IsNullOrWhiteSpace(item.InfoPath));
                Assert.False(string.IsNullOrWhiteSpace(item.OriginalPath));
            });
        }

        [Fact]
        public void MakeUniquePath_WhenTargetFree_ReturnsSameName()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cleanc-rb-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                string original = Path.Combine(dir, "free-name.txt");

                Assert.Equal(original, RecycleBinService.MakeUniquePath(original));
            }
            finally
            {
                try { Directory.Delete(dir, true); }
                catch (Exception) { /* پاک‌سازی تستی */ }
            }
        }
    }
}
