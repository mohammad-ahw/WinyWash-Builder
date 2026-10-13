// src/CleanC.UI/Hero/HeroLooks.cs
// فهرست اکسسوری های کاراکتر — باید دقیقاً با ACC داخل CleanC.hero.html یکی باشد
// (همان کدها و همان برچسب های فارسی، چون هیرو فارسی/RTL است).
// هر وقت HTML اکسسوری اضافه/حذف کرد، اینجا هم باید به روز شود.

using System.Collections.Generic;

namespace CleanC.UI.Hero
{
    internal static class HeroLooks
    {
        /// <summary>(کد، برچسب) — ترتیب باید با آرایهٔ ACC در HTML یکی بماند.</summary>
        internal static readonly (string Id, string Name)[] All =
        {
            ("none",    "بدون اکسسوری"),
            ("tech",    "تکنسین"),
            ("gamer",   "گیمر"),
            ("student", "دانشجو"),
            ("formal",  "رسمی"),
            ("detective","کارآگاه"),
            ("wizard",  "جادوگر"),
            ("astro",   "فضانورد"),
            ("ninja",   "نینجا"),
            ("scientist","دانشمند"),
            ("explorer","ماجراجو"),
            ("artist",  "هنرمند"),
            ("photo",   "عکاس"),
            ("music",   "موسیقی‌دان"),
            ("pirate",  "دزد دریایی"),
            ("nature",  "طبیعت‌گرا"),
            ("ice",     "یخی"),
            ("fire",    "آتشین"),
            ("disco",   "دیسکو"),
            ("crystal", "کریستالی")
        };

        /// <summary>گزینه های لحن گفتار: همان سه مقداری که HTML می خواند.</summary>
        internal static readonly (string Id, string Name)[] Tones =
        {
            ("joke",    "شوخ‌طبع"),
            ("calm",    "آرام"),
            ("serious", "جدی")
        };

        /// <summary>گزینه های شدت — BUD داخل HTML: off/low/mid/high.</summary>
        internal static readonly (string Id, string Name)[] Intensities =
        {
            ("off", "خاموش"),
            ("low", "کم"),
            ("mid", "متوسط"),
            ("high","زیاد")
        };

        /// <summary>لیست ظاهر مناسبتی: اول «بدون» (یعنی غیرفعال)، بعد همهٔ اکسسوری ها.</summary>
        internal static (string Id, string Name)[] Occasion()
        {
            var list = new List<(string Id, string Name)> { ("", "بدون") };
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
