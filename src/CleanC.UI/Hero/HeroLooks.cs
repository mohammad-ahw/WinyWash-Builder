// src/CleanC.UI/Hero/HeroLooks.cs
// فهرست اکسسوری های کاراکتر — باید دقیقاً با ACC داخل CleanC.hero.html یکی باشد
// (همان کدها و همان برچسب های فارسی، چون هیرو فارسی/RTL است).
// هر وقت HTML اکسسوری اضافه/حذف کرد، اینجا هم باید به روز شود.

using System.Collections.Generic;
using CleanC.Core.Services;

namespace CleanC.UI.Hero
{
    internal static class HeroLooks
    {
        /// <summary>(کد، برچسب) — ترتیب باید با آرایهٔ ACC در HTML یکی بماند.</summary>
        internal static (string Id, string Name)[] All { get; private set; } = BuildAll();

        /// <summary>
        /// فهرست را با زبان فعلی می سازد. بعد از تغییر زبان دوباره صدا زده می شود
        /// تا برچسب های کش شده در کمبوباکس ها قدیمی نمانند.
        /// </summary>
        internal static void RefreshLocalizedNames() => All = BuildAll();

        private static (string Id, string Name)[] BuildAll() => new[]
        {
            ("none",    LocalizationService.Get("بدون اکسسوری")),
            ("tech",    LocalizationService.Get("تکنسین")),
            ("gamer",   LocalizationService.Get("گیمر")),
            ("student", LocalizationService.Get("دانشجو")),
            ("formal",  LocalizationService.Get("رسمی")),
            ("detective", LocalizationService.Get("کارآگاه")),
            ("wizard",  LocalizationService.Get("جادوگر")),
            ("astro",   LocalizationService.Get("فضانورد")),
            ("ninja",   LocalizationService.Get("نینجا")),
            ("scientist", LocalizationService.Get("دانشمند")),
            ("explorer", LocalizationService.Get("ماجراجو")),
            ("artist",  LocalizationService.Get("هنرمند")),
            ("photo",   LocalizationService.Get("عکاس")),
            ("music",   LocalizationService.Get("موسیقی‌دان")),
            ("pirate",  LocalizationService.Get("دزد دریایی")),
            ("nature",  LocalizationService.Get("طبیعت‌گرا")),
            ("ice",     LocalizationService.Get("یخی")),
            ("fire",    LocalizationService.Get("آتشین")),
            ("disco",   LocalizationService.Get("دیسکو")),
            ("crystal", LocalizationService.Get("کریستالی"))
        };

        /// <summary>گزینه های لحن گفتار: همان سه مقداری که HTML می خواند.</summary>
        internal static readonly (string Id, string Name)[] Tones =
        {
            ("joke",    LocalizationService.Get("شوخ‌طبع")),
            ("calm",    LocalizationService.Get("آرام")),
            ("serious", LocalizationService.Get("جدی"))
        };

        /// <summary>گزینه های شدت — BUD داخل HTML: off/low/mid/high.</summary>
        internal static readonly (string Id, string Name)[] Intensities =
        {
            ("off", LocalizationService.Get("خاموش")),
            ("low", LocalizationService.Get("کم")),
            ("mid", LocalizationService.Get("متوسط")),
            ("high",LocalizationService.Get("زیاد"))
        };

        /// <summary>لیست ظاهر مناسبتی: اول «بدون» (یعنی غیرفعال)، بعد همهٔ اکسسوری ها.</summary>
        internal static (string Id, string Name)[] Occasion()
        {
            var list = new List<(string Id, string Name)> { ("", LocalizationService.Get("بدون")) };
            list.AddRange(All);
            return list.ToArray();
        }

        /// <summary>ایندکس یک کد در فهرست؛ پیدا نشد → 0 (یعنی گزینهٔ اول).</summary>
        internal static int IndexOf((string Id, string Name)[] items, string? id)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (string.Equals(items[i].Id, id, System.StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }

        /// <summary>ایندکس در فهرست ظاهر مناسبتی؛ خالی/پیدا نشد → 0 («بدون»).</summary>
        internal static int IndexOfOccasion(string? id)
        {
            var items = Occasion();
            if (string.IsNullOrEmpty(id)) return 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (string.Equals(items[i].Id, id, System.StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }
    }
}
