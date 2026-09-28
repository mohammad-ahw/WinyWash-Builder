using System;

namespace CleanC.Core.Models
{
    public class LargeFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string FormattedSize => SystemStats.FormatBytes(SizeBytes);
        public DateTime LastModified { get; set; }
        public string FormattedDate => LastModified.ToString("yyyy-MM-dd");
    }
}
