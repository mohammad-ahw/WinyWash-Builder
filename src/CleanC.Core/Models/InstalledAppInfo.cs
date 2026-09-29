using System;

namespace CleanC.Core.Models
{
    public class InstalledAppInfo
    {
        public string DisplayName { get; set; } = string.Empty;
        public string DisplayVersion { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string InstallDate { get; set; } = string.Empty;
        public DateTime? ParsedInstallDate { get; set; }
        public string InstalledDateFormatted => ParsedInstallDate.HasValue 
            ? ParsedInstallDate.Value.ToString("yyyy/MM/dd") 
            : (!string.IsNullOrEmpty(InstallDate) ? InstallDate : "نامشخص");

        public long EstimatedSizeBytes { get; set; }
        public string FormattedSize => EstimatedSizeBytes > 0 ? SystemStats.FormatBytes(EstimatedSizeBytes) : string.Empty;

        public string UninstallString { get; set; } = string.Empty;
        public string QuietUninstallString { get; set; } = string.Empty;
        public string DisplayIcon { get; set; } = string.Empty;
        public bool IsStoreApp { get; set; }
        public string PackageFullName { get; set; } = string.Empty;
    }
}
