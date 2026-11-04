using System;
using System.Collections.Generic;
using CleanC.Core.Services;

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
        public string Evidence { get; set; } = string.Empty;
        // حذف این پوشه نیاز به دسترسی ادمین دارد (D09b — پوشه های خالی ریشه ی درایو)
        public bool RequiresAdmin { get; set; } = false;
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
        IncompleteDownload,
        // D10 — بازمانده ی نصاب سازنده در ریشه ی درایو (C:\NVIDIA, C:\Aomei, ...)
        OemDriveRootLeftover,
        // D04 — بازمانده ی آپدیترهای Electron/Squirrel در LocalAppData
        UpdaterLeftover,
        // D16 — بازمانده ی انتقال دستگاه ها (CrossDevice) و پوشه ی موقت OneDrive
        SyncDeviceLeftover,
        // فایل قفل موقت آفیس (~$name.docx) — بازمانده ی کرش Word/Excel
        OfficeLockFile,
        // کش و پکیج‌های محیط توسعه و کانتینرها (npm, Docker, pip, NuGet, Gradle, Cargo)
        DevToolsCache
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
            StorageMessCategory.OldInstaller => LocalizationService.Get("MessCatOldInstaller", "فایل نصب قدیمی (Setup / Installer)"),
            StorageMessCategory.OldArchive => LocalizationService.Get("MessCatOldArchive", "فایل فشرده (Zip / Rar)"),
            StorageMessCategory.DuplicateCopy => LocalizationService.Get("MessCatDuplicateCopy", "نسخه کپی شده (Copy / Duplicated)"),
            StorageMessCategory.OldScreenshot => LocalizationService.Get("MessCatOldScreenshot", "اسکرین شات قدیمی"),
            StorageMessCategory.TempJunk => LocalizationService.Get("MessCatTempJunk", "فایل موقت و زائد (Temp / Junk)"),
            StorageMessCategory.LargeFile => LocalizationService.Get("MessCatLargeFile", "فایل بسیار حجیم (>200MB)"),
            StorageMessCategory.ExtractedArchiveDuplicate => LocalizationService.Get("MessCatExtractedArchiveDup", "فایل آرشیو آنزیپ‌شده (پوشه هم‌نام موجود است)"),
            StorageMessCategory.IncompleteDownload => LocalizationService.Get("MessCatIncompleteDownload", "دانلود ناتمام و موقت (crdownload / part)"),
            StorageMessCategory.OemDriveRootLeftover => LocalizationService.Get("MessCatOemDriveRoot", "بازمانده ی نصب سازنده در ریشه ی درایو"),
            StorageMessCategory.UpdaterLeftover => LocalizationService.Get("MessCatUpdater", "بازمانده ی آپدیتر (Electron / Squirrel)"),
            StorageMessCategory.SyncDeviceLeftover => LocalizationService.Get("MessCatSyncDevice", "بازمانده ی انتقال دستگاه / تمپ OneDrive"),
            StorageMessCategory.OfficeLockFile => LocalizationService.Get("MessCatOfficeLock", "فایل قفل موقت آفیس (~$)"),
            StorageMessCategory.DevToolsCache => LocalizationService.Get("MessCatDevTools", "کش ابزارهای برنامه‌نویسی و کانتینرها (npm, Docker, pip)"),
            _ => LocalizationService.Get("MessCatOther", "سایر")
        };
        public string CategoryTitle => CategoryName;
        public bool IsSelected { get; set; } = false;

        // شواهد تشخیص — «چرا» این مورد پیشنهاد شده (مثلاً نام فایل نصب پیدا شده).
        // UI این متن را کنار آیتم نشان می دهد تا کاربر با اطلاعات کامل تصمیم بگیرد.
        public string Evidence { get; set; } = string.Empty;

        // حذف این مورد برای انجام شدن نیاز به دسترسی ادمین دارد (مثلاً ریشه ی درایو).
        public bool RequiresAdmin { get; set; } = false;
    }

    /// <summary>
    /// D17 — فایل حساس کاربر (لایسنس/سریال/کلید خصوصی/دیتابیس رمز).
    /// عمداً از StorageMessItem جدا است تا «فقط گزارش» بماند:
    /// هیچ مسیر حذفی برای این نوع وجود ندارد و UI چک‌باکس نمی دهد.
    /// </summary>
    public class SensitiveFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string FormattedSize => SystemStats.FormatBytes(SizeBytes);
        public DateTime LastModified { get; set; }
        public string MatchReason { get; set; } = string.Empty;
    }

    public class DeepStorageScanResult
    {
        public List<DuplicateFileGroup> DuplicateGroups { get; set; } = new();
        public List<DuplicateFileGroup> Duplicates => DuplicateGroups;
        public List<EmptyFolderInfo> EmptyFolders { get; set; } = new();
        public List<StorageMessItem> MessItems { get; set; } = new();

        // D17 — فایل های حساس: فقط برای نمایش گزارش؛ برای حذف در هیچ جای UI نمی آیند.
        public List<SensitiveFileInfo> SensitiveFiles { get; set; } = new();

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
