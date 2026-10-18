// src/CleanC.Core/Services/DeepStorageSelection.cs
// C6: جمع‌بندی انتخاب‌های پویش عمیق — این منطق قبلاً در سه جای code-behind تکرار شده بود
// (شمارش انتخاب‌ها، دیالوگ تأیید و حذف گروهی). حالا یک منبع حقیقت تست‌پذیر است.
using System.Collections.Generic;
using System.Linq;
using CleanC.Core.Models;

namespace CleanC.Core.Services
{
    public readonly record struct DeepStorageSelectionSummary(int Count, long Bytes);

    public static class DeepStorageSelection
    {
        /// <summary>
        /// بر اساس تب فعال، تعداد و حجم انتخاب‌شده‌ها را برمی‌گرداند.
        /// تب تکراری‌ها فقط نسخه‌های غیراصلی که برای حذف انتخاب شده‌اند را می‌شمارد.
        /// </summary>
        public static DeepStorageSelectionSummary Summarize(DeepStorageScanResult? result, string tab)
        {
            if (result == null) return new DeepStorageSelectionSummary(0, 0);

            switch (tab)
            {
                case "duplicates":
                    var dupFiles = result.Duplicates
                        .SelectMany(g => g.Files)
                        .Where(f => f.IsSelectedForDeletion)
                        .ToList();
                    return new DeepStorageSelectionSummary(dupFiles.Count, dupFiles.Sum(f => f.SizeBytes));

                case "empty_folders":
                    return new DeepStorageSelectionSummary(
                        result.EmptyFolders.Count(f => f.IsSelected),
                        0);

                case "mess":
                    return SummarizeItems(result.MessItems);

                case "large_files":
                    return SummarizeItems(result.LargeFiles);

                default:
                    return new DeepStorageSelectionSummary(0, 0);
            }
        }

        private static DeepStorageSelectionSummary SummarizeItems(List<StorageMessItem> items)
        {
            var selected = items.Where(i => i.IsSelected).ToList();
            return new DeepStorageSelectionSummary(selected.Count, selected.Sum(i => i.SizeBytes));
        }
    }
}
