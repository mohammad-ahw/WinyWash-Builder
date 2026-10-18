using CleanC.Core.Services;

namespace CleanC.Core.Models
{
    public class ProcessInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public long MemoryBytes { get; set; }
        public string MemoryFormatted => SystemStats.FormatBytes(MemoryBytes);
        public string PidFormatted => string.Format(LocalizationService.Get("شناسه (PID): {0}"), Id);
        public bool IsCritical { get; set; }
        public bool CanKill => !IsCritical;
    }
}
