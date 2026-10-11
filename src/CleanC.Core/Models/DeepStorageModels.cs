using System;
using System.Collections.Generic;

namespace CleanC.Core.Models
{
    public class DuplicateFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string FormattedSize => SystemStats.FormatBytes(SizeBytes);
        public DateTime LastModified { get; set; }
        public bool IsOriginal { get; set; }
        public bool IsSelectedForDeletion { get; set; }
    }

    public class DuplicateFileGroup
    {
        public string FileHash { get; set; } = string.Empty;
        public string SampleName { get; set; } = string.Empty;
        public long SingleSizeBytes { get; set; }
        public string FormattedSingleSize => SystemStats.FormatBytes(SingleSizeBytes);
        public string FormattedSize => FormattedSingleSize;
        public string HashPreview => FileHash.Length > 8 ? FileHash[..8] : FileHash;
        public List<DuplicateFileInfo> Files { get; set; } = new();
        public long WastedSizeBytes => SingleSizeBytes * Math.Max(0, Files.Count - 1);
        public string FormattedWastedSize => SystemStats.FormatBytes(WastedSizeBytes);
    }

    public class EmptyFolderInfo
    {
        public string FolderPath { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string ParentPath { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = false;
    }

    public enum StorageMessCategory
    {
        OldInstaller,
        OldArchive,
        DuplicateCopy,
        OldScreenshot,
        TempJunk,
        LargeFile,
        ExtractedArchiveDuplicate,
        IncompleteDownload
    }

    public class StorageMessItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string FormattedSize => SystemStats.FormatBytes(SizeBytes);
        public DateTime LastModified { get; set; }
        public StorageMessCategory Category { get; set; }
        public string CategoryName => Category switch
        {
            StorageMessCategory.OldInstaller => "فایل نصب قدیمی (Setup / Installer)",
            StorageMessCategory.OldArchive => "فایل فشرده (Zip / Rar)",
            StorageMessCategory.DuplicateCopy => "نسخه کپی شده (Copy / Duplicated)",
            StorageMessCategory.OldScreenshot => "اسکرین شات قدیمی",
            StorageMessCategory.TempJunk => "فایل موقت و زائد (Temp / Junk)",
            StorageMessCategory.LargeFile => "فایل بسیار حجیم (>200MB)",
            StorageMessCategory.ExtractedArchiveDuplicate => "فایل آرشیو آنزیپ‌شده (پوشه هم‌نام موجود است)",
            StorageMessCategory.IncompleteDownload => "دانلود ناتمام و موقت (crdownload / part)",
            _ => "سایر"
        };
        public string CategoryTitle => CategoryName;
        public bool IsSelected { get; set; } = false;
    }

    public class DeepStorageScanResult
    {
        public List<DuplicateFileGroup> DuplicateGroups { get; set; } = new();
        public List<DuplicateFileGroup> Duplicates => DuplicateGroups;
        public List<EmptyFolderInfo> EmptyFolders { get; set; } = new();
        public List<StorageMessItem> MessItems { get; set; } = new();

        public List<StorageMessItem> LargeFiles => MessItems.FindAll(m => m.Category == StorageMessCategory.LargeFile);
        public List<StorageMessItem> OrganizationMess => MessItems.FindAll(m => m.Category != StorageMessCategory.LargeFile);

        public long TotalDuplicateWastedBytes { get; set; }
        public long TotalDuplicatesWastedBytes => TotalDuplicateWastedBytes;
        public long TotalMessSizeBytes { get; set; }
        public long TotalMessBytes => TotalMessSizeBytes;
        public long TotalLargeFilesBytes
        {
            get
            {
                long sum = 0;
                foreach (var f in LargeFiles) sum += f.SizeBytes;
                return sum;
            }
        }
        public long TotalReclaimableBytes => TotalDuplicateWastedBytes + TotalMessSizeBytes;
        public string FormattedTotalReclaimable => SystemStats.FormatBytes(TotalReclaimableBytes);
    }
}
