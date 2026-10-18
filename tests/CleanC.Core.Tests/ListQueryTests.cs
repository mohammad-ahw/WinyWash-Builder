// tests/CleanC.Core.Tests/ListQueryTests.cs
// C6: تست‌های منطق بیرون‌کشیده‌شده از code-behind (فیلتر/مرتب‌سازی اپ‌ها + جمع‌بندی انتخاب پویش عمیق).
using System;
using System.Collections.Generic;
using CleanC.Core.Models;
using CleanC.Core.Services;
using Xunit;

namespace CleanC.Core.Tests
{
    public class ListQueryTests
    {
        private static InstalledAppInfo App(string name, string publisher, long size, DateTime? date = null) => new()
        {
            DisplayName = name,
            Publisher = publisher,
            EstimatedSizeBytes = size,
            ParsedInstallDate = date
        };

        private static List<InstalledAppInfo> SampleApps() => new()
        {
            App("VLC media player", "VideoLAN", 150_000_000, new DateTime(2024, 5, 1)),
            App("Chrome", "Google LLC", 900_000_000, new DateTime(2025, 1, 15)),
            App("Notepad++", "Notepad++ Team", 20_000_000, null),
            App("Google Drive", "Google LLC", 300_000_000, null)
        };

        [Fact]
        public void Filter_EmptyQuery_ReturnsAll()
        {
            var result = AppListQuery.Filter(SampleApps(), null);
            Assert.Equal(4, result.Count);

            result = AppListQuery.Filter(SampleApps(), "   ");
            Assert.Equal(4, result.Count);
        }

        [Fact]
        public void Filter_MatchesName_CaseInsensitive()
        {
            var result = AppListQuery.Filter(SampleApps(), "chrome");
            Assert.Single(result);
            Assert.Equal("Chrome", result[0].DisplayName);
        }

        [Fact]
        public void Filter_MatchesPublisher_Too()
        {
            var result = AppListQuery.Filter(SampleApps(), "google");
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void Filter_NoMatch_ReturnsEmpty()
        {
            var result = AppListQuery.Filter(SampleApps(), "xyz-does-not-exist");
            Assert.Empty(result);
        }

        [Fact]
        public void Sort_Default_NewestFirst_ThenName()
        {
            var result = AppListQuery.Sort(SampleApps(), 0);
            Assert.Equal("Chrome", result[0].DisplayName);        // جدیدترین تاریخ
            Assert.Equal("VLC media player", result[1].DisplayName);
            // بدون تاریخ‌ها بر اساس نام: Google Drive قبل از Notepad++
            Assert.Equal("Google Drive", result[2].DisplayName);
            Assert.Equal("Notepad++", result[3].DisplayName);
        }

        [Fact]
        public void Sort_BySize_Descending()
        {
            var result = AppListQuery.Sort(SampleApps(), 1);
            Assert.Equal("Chrome", result[0].DisplayName);
            Assert.Equal("Google Drive", result[1].DisplayName);
            Assert.Equal("VLC media player", result[2].DisplayName);
            Assert.Equal("Notepad++", result[3].DisplayName);
        }

        [Fact]
        public void Sort_ByName_Ascending()
        {
            var result = AppListQuery.Sort(SampleApps(), 2);
            Assert.Equal("Chrome", result[0].DisplayName);
            Assert.Equal("Google Drive", result[1].DisplayName);
            Assert.Equal("Notepad++", result[2].DisplayName);
            Assert.Equal("VLC media player", result[3].DisplayName);
        }

        // -------------------- DeepStorageSelection --------------------

        private static DeepStorageScanResult MessResult()
        {
            return new DeepStorageScanResult
            {
                MessItems = new List<StorageMessItem>
                {
                    new() { FileName = "a.tmp", SizeBytes = 100, IsSelected = true },
                    new() { FileName = "b.tmp", SizeBytes = 50, IsSelected = false },
                    new() { FileName = "c.tmp", SizeBytes = 25, IsSelected = true }
                },
                EmptyFolders = new List<EmptyFolderInfo>
                {
                    new() { FolderName = "x", IsSelected = true },
                    new() { FolderName = "y", IsSelected = false }
                },
                DuplicateGroups = new List<DuplicateFileGroup>
                {
                    new()
                    {
                        Files = new List<DuplicateFileInfo>
                        {
                            new() { FilePath = @"C:\o\1.jpg", SizeBytes = 20, IsOriginal = true, IsSelectedForDeletion = false },
                            new() { FilePath = @"C:\o\2.jpg", SizeBytes = 20, IsOriginal = false, IsSelectedForDeletion = true },
                            new() { FilePath = @"C:\o\3.jpg", SizeBytes = 20, IsOriginal = false, IsSelectedForDeletion = false }
                        }
                    }
                }
            };
        }

        [Fact]
        public void Summarize_Mess_CountsSelectedAndBytes()
        {
            var summary = DeepStorageSelection.Summarize(MessResult(), "mess");
            Assert.Equal(2, summary.Count);
            Assert.Equal(125, summary.Bytes);
        }

        [Fact]
        public void Summarize_EmptyFolders_CountsButNoBytes()
        {
            var summary = DeepStorageSelection.Summarize(MessResult(), "empty_folders");
            Assert.Equal(1, summary.Count);
            Assert.Equal(0, summary.Bytes);
        }

        [Fact]
        public void Summarize_Duplicates_IgnoresOriginals()
        {
            var summary = DeepStorageSelection.Summarize(MessResult(), "duplicates");
            Assert.Equal(1, summary.Count);   // فقط نسخه کپی انتخاب‌شده
            Assert.Equal(20, summary.Bytes);
        }

        [Fact]
        public void Summarize_LargeFiles_UsesWideFileSubset()
        {
            var result = MessResult();
            result.MessItems.Add(new StorageMessItem
            {
                FileName = "movie.mkv",
                SizeBytes = 700_000_000,
                Category = StorageMessCategory.LargeFile,
                IsSelected = true
            });

            var summary = DeepStorageSelection.Summarize(result, "large_files");
            Assert.Equal(1, summary.Count);
            Assert.Equal(700_000_000, summary.Bytes);
        }

        [Fact]
        public void Summarize_NullOrUnknownTab_IsZero()
        {
            var zero = DeepStorageSelection.Summarize(null, "mess");
            Assert.Equal(0, zero.Count);
            Assert.Equal(0, zero.Bytes);

            var unknown = DeepStorageSelection.Summarize(MessResult(), "something");
            Assert.Equal(0, unknown.Count);
        }
    }
}
