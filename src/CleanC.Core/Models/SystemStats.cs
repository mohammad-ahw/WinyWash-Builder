namespace CleanC.Core.Models
{
    public class SystemStats
    {
        public long TotalRamBytes { get; set; }
        public long FreeRamBytes { get; set; }
        public long UsedRamBytes { get; set; }
        public double RamPercent { get; set; }

        public double CpuPercent { get; set; }

        public long DiskTotalBytes { get; set; }
        public long DiskFreeBytes { get; set; }
        public long DiskUsedBytes { get; set; }
        public double DiskPercent { get; set; }
        public string DiskModel { get; set; } = string.Empty;
        public string DiskType { get; set; } = "NVMe SSD";
        public string DiskHealth { get; set; } = "سالم (Healthy)";
        public string GpuSummary { get; set; } = string.Empty;
        public string PowerSummary { get; set; } = "متصل به برق";

        public string TotalRamFormatted => FormatBytes(TotalRamBytes);
        public string UsedRamFormatted => FormatBytes(UsedRamBytes);
        public string FreeRamFormatted => FormatBytes(FreeRamBytes);

        public string DiskTotalFormatted => FormatBytes(DiskTotalBytes);
        public string DiskUsedFormatted => FormatBytes(DiskUsedBytes);
        public string DiskFreeFormatted => FormatBytes(DiskFreeBytes);

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 بایت";
            string[] units = { "بایت", "کیلوبایت", "مگابایت", "گیگابایت", "ترابایت" };
            int digitGroups = (int)(Math.Log(bytes) / Math.Log(1024));
            if (digitGroups >= units.Length) digitGroups = units.Length - 1;
            double num = bytes / Math.Pow(1024, digitGroups);
            return $"{num:0.##} {units[digitGroups]}";
        }
    }
}
