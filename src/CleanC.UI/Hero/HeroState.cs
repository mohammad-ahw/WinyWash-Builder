// src/CleanC.UI/Hero/HeroState.cs
// payload هایی که میزبان (C#) داخل WebView2 می‌فرستاده تا در CleanC.hero.html اعمال شوند.
// نام فیلدها عمداً با camelCase همان کلیدهای جاوااسکریپت یکی است؛ هر جا نام C# با JS
// نمی‌خواند از JsonJsonProperty استفاده شده تا سمت JS تغییری لازم نباشد.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CleanC.UI.Hero
{
    /// <summary>
    /// حالت زندهٔ سیستم برای <c>window.CleanC.update(p)</c>.
    /// هر بار که تایمر آمار یا نتیجهٔ یک مرحله چیزی را عوض کرد، یکی از اینها ساخته و فرستاده می‌شود.
    /// </summary>
    public sealed class HeroState
    {
        [JsonPropertyName("cpu")]
        public double Cpu { get; set; }

        [JsonPropertyName("ram")]
        public double Ram { get; set; }

        /// <summary>درصد فضای آزاد درایو C — سمت JS به <c>dsk = 100 - cFree</c> تبدیل می‌شود.</summary>
        [JsonPropertyName("cFree")]
        public double CFree { get; set; }

        [JsonPropertyName("batt")]
        public int Batt { get; set; }

        [JsonPropertyName("charging")]
        public bool Charging { get; set; }

        /// <summary>پراکسی شبح فعال است؟ — آیتم «اینترنت» را با شدت بحرانی نشان می‌دهد.</summary>
        [JsonPropertyName("proxyDead")]
        public bool ProxyDead { get; set; }

        [JsonPropertyName("uptimeDays")]
        public int UptimeDays { get; set; }

        [JsonPropertyName("offline")]
        public bool Offline { get; set; }

        [JsonPropertyName("browserMB")]
        public long BrowserMb { get; set; }

        /// <summary>
        /// شمار تب‌های باز مرورگر. از C# قابل گرفتن نیست (وابسته به پروسه و پروفایل) و
        /// عملاً همیشه صفر می‌ماند؛ سمت JS یک fallback به <c>browserMB</c> دارد.
        /// </summary>
        [JsonPropertyName("tabs")]
        public int Tabs { get; set; }

        [JsonPropertyName("topRam")]
        public string TopRam { get; set; } = "";

        [JsonPropertyName("topCpu")]
        public string TopCpu { get; set; } = "";

        /// <summary>روزهایی که از آخرین پاکسازی واقعی گذشته؛ <c>null</c> یعنی هنوز چیزی پاک نشده.</summary>
        [JsonPropertyName("daysSince")]
        public int? DaysSince { get; set; }

        /// <summary>
        /// کلیدهای آیتم‌های نگهداری که فعلاً مشکل‌دارند (junk, dns, reg, startup, tweak, fast, gpu, debloat, …)؛
        /// سمت JS با <c>IT</c> جفت می‌شوند و فهرست مسائل را می‌سازند.
        /// </summary>
        [JsonPropertyName("items")]
        public List<string> Items { get; set; } = new();

        /// <summary>
        /// سایز واقعی هر آیتم به <b>بایت</b>؛ کلیدها همان کلیدهای <c>IT</c> در CleanC.hero.html.
        /// آیتمی که هسته عدد حجمی ندارد صفر می‌آید تا سمت JS اعداد demo را نگه ندارد
        /// و «قابل پاکسازی …» دروغ نشان ندهد (JS با <c>syncSizes</c> اعمال می‌کند).
        /// </summary>
        [JsonPropertyName("sizes")]
        public Dictionary<string, long> Sizes { get; set; } = new();

        /// <summary>پیشنهادهای کنجکاوی بعد از بهینه‌سازی: هر ردیف [متن, کلید].</summary>
        [JsonPropertyName("chips")]
        public List<string[]> Chips { get; set; } = new();

        /// <summary>مجموع فضای آزادشده در کل عمر برنامه، به گیگابایت.</summary>
        [JsonPropertyName("totalFreedGb")]
        public double TotalFreedGb { get; set; }

        /// <summary>متن «آخرین پاکسازی»؛ سمت JS داخل تیتر و زیرعنوان جایگزین <c>{last}</c> می‌شود.</summary>
        [JsonPropertyName("lastClean")]
        public string? LastClean { get; set; }
    }

    /// <summary>
    /// تنظیمات بخش «کاراکتر» که از صفحهٔ Settings می‌آید و داخل JS با
    /// <c>applySettings(s)</c> اعمال می‌شود. نام فیلدها همان چیزی است که JS می‌خواند.
    /// </summary>
    public sealed class HeroCharacterSettings
    {
        [JsonPropertyName("tone")]
        public string? Tone { get; set; }

        [JsonPropertyName("uName")]
        public string? UserName { get; set; }

        [JsonPropertyName("cName")]
        public string? CharacterName { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("calm")]
        public bool? Calm { get; set; }

        [JsonPropertyName("fast")]
        public bool? Fast { get; set; }

        /// <summary>شدت رفتارها: off | low | mid | high</summary>
        [JsonPropertyName("intensity")]
        public string? Intensity { get; set; }

        [JsonPropertyName("bubble")]
        public bool? Bubble { get; set; }

        [JsonPropertyName("env")]
        public bool? Env { get; set; }

        [JsonPropertyName("sound")]
        public bool? Sound { get; set; }

        /// <summary>کد ظاهر کاراکتر (ACC)؛ خالی یعنی «ظاهر پیش‌فرض روز».</summary>
        [JsonPropertyName("look")]
        public string? Look { get; set; }

        [JsonPropertyName("occLook")]
        public string? OccLook { get; set; }

        /// <summary>مقیاس اندازهٔ کاراکتر: 0.85 | 1 | 1.15</summary>
        [JsonPropertyName("size")]
        public double? Size { get; set; }

        [JsonPropertyName("lun")]
        public int? Lun { get; set; }
    }

    /// <summary>پیام تم — رنگ Accent ویندوز به هالهٔ دور کاراکتر می‌رود.</summary>
    public sealed class HeroThemeMessage
    {
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "system";

        /// <summary>رنگ Accent ویندوز به شکل <c>#rrggbb</c>.</summary>
        [JsonPropertyName("accent")]
        public string Accent { get; set; } = "#1fa971";
    }

    /// <summary>ردیف دیالوگ برنامه‌های مرزی که سمت میزبان ساخته می‌شود.</summary>
    public sealed class HeroBorderlineItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("size")]
        public string Size { get; set; } = "";
    }
}
