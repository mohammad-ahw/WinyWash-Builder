using System;

namespace CleanC.Core.Models
{
    public class InstalledAppInfo
    {
        public string DisplayName { get; set; } = string.Empty;
        public string DisplayVersion { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string InstallDate { get; set; } = string.Empty;
        public long EstimatedSizeBytes { get; set; }
        public string FormattedSize => EstimatedSizeBytes > 0 ? SystemStats.FormatBytes(EstimatedSizeBytes) : "نامشخص";
        public string UninstallString { get; set; } = string.Empty;
        public string QuietUninstallString { get; set; } = string.Empty;
        public bool IsStoreApp { get; set; }
        public string PackageFullName { get; set; } = string.Empty;
    }
}
