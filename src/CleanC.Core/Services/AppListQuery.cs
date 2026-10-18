// src/CleanC.Core/Services/AppListQuery.cs
// C6: منطق فیلتر/مرتب‌سازی لیست نرم‌افزارها از code-behind بیرون کشیده شد تا تست‌پذیر باشد.
// جستجو هم در نام و هم در ناشر انجام می‌شود (OrdinalIgnoreCase) — همان رفتار قبلی UI.
using System;
using System.Collections.Generic;
using System.Linq;
using CleanC.Core.Models;

namespace CleanC.Core.Services
{
    public static class AppListQuery
    {
        /// <summary>فیلتر بر اساس نام یا ناشر؛ کوئری خالی یعنی همه.</summary>
        public static List<InstalledAppInfo> Filter(IEnumerable<InstalledAppInfo> apps, string? query)
        {
            string q = query?.Trim() ?? string.Empty;
            if (q.Length == 0)
            {
                return apps.ToList();
            }

            return apps.Where(a =>
                    a.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    a.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// مرتب‌سازی: 0 = جدیدترین نصب (پیش‌فرض)، 1 = بیشترین حجم، 2 = نام (الفبا).
        /// پیش‌فرض حتی برای موارد بدون تاریخ هم ترتیب قطعی دارد.
        /// </summary>
        public static List<InstalledAppInfo> Sort(IEnumerable<InstalledAppInfo> apps, int sortIndex) => sortIndex switch
        {
            1 => apps.OrderByDescending(a => a.EstimatedSizeBytes).ToList(),
            2 => apps.OrderBy(a => a.DisplayName).ToList(),
            _ => apps.OrderByDescending(a => a.ParsedInstallDate.HasValue)
                     .ThenByDescending(a => a.ParsedInstallDate ?? DateTime.MinValue)
                     .ThenBy(a => a.DisplayName)
                     .ToList()
        };
    }
}
