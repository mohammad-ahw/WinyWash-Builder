using System.Collections.Generic;

namespace CleanC.Core.Models
{
    public class JunkGroup
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = "\uE74C";
        public List<JunkCategory> Items { get; set; } = new List<JunkCategory>();

        public long TotalBytes
        {
            get
            {
                long sum = 0;
                foreach (var it in Items) sum += it.Bytes;
                return sum;
            }
        }

        public string FormattedTotal => SystemStats.FormatBytes(TotalBytes);
    }

    public class JunkCategory
    {
        public string Id { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public List<string> TargetPaths { get; set; } = new List<string>();
        public long Bytes { get; set; }
        public bool IsChecked { get; set; } = true;
        public bool RequiresElevation { get; set; }
        public string IconGlyph { get; set; } = "\uE7B8";

        public string FormattedBytes => SystemStats.FormatBytes(Bytes);
    }

    public class ScanResult
    {
        public List<JunkGroup> Groups { get; set; } = new List<JunkGroup>();
        public long TotalBytes
        {
            get
            {
                long sum = 0;
                foreach (var g in Groups) sum += g.TotalBytes;
                return sum;
            }
        }
        public string FormattedTotal => SystemStats.FormatBytes(TotalBytes);
    }

    public class CleanResult
    {
        public bool Success { get; set; }
        public long FreedBytes { get; set; }
        public string FormattedFreed => SystemStats.FormatBytes(FreedBytes);
        public List<string> CleanedItems { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class BoostResult
    {
        public bool Success { get; set; }
        public long FreedBytes { get; set; }
        public string FormattedFreed => SystemStats.FormatBytes(FreedBytes);
        public int ProcessCount { get; set; }
        public double CurrentRamPercent { get; set; }
    }

    public class CleanupStepResult
    {
        public string StepId { get; set; } = string.Empty; // "probe_init", "probe_item", "clean_start", "clean_item", "ram_boost", "complete"
        public string Phase { get; set; } = string.Empty; // "probe", "clean", "boost", "complete"
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ItemStatusText { get; set; } = string.Empty;
        public long FreedBytesInStep { get; set; }
        public long CumulativeFreedBytes { get; set; }
        public double ProgressPercent { get; set; }
        public bool IsComplete { get; set; }
        public JunkCategory? TargetItem { get; set; }
        public List<JunkCategory>? InitialItems { get; set; }
        public int DisabledStartupAppsCount { get; set; }
        public int BrowserTweaksCount { get; set; }
        public int WindowsTweaksCount { get; set; }
        public List<Services.AttentionDecisionItem> AttentionItems { get; set; } = new List<Services.AttentionDecisionItem>();
    }

    public class CleanupReportItem
    {
        public string GroupId { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = "\uE7B8";
        public long FreedBytes { get; set; }
        public bool WasEmpty { get; set; }
        public string StatusSummary { get; set; } = string.Empty;
        public string FormattedFreed => SystemStats.FormatBytes(FreedBytes);
    }

    public class CleanupReportGroup
    {
        public string GroupId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = "\uE74C";
        public List<CleanupReportItem> Items { get; set; } = new List<CleanupReportItem>();
        public long TotalFreedBytes
        {
            get
            {
                long sum = 0;
                foreach (var i in Items) sum += i.FreedBytes;
                return sum;
            }
        }
        public string FormattedTotal => SystemStats.FormatBytes(TotalFreedBytes);
    }
}
