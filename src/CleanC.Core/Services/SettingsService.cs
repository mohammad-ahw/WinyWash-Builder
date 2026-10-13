// O:\OpenCode\AntiGravity\CleanC - NEW\src\CleanC.Core\Services\SettingsService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CleanC.Core.Diagnostics;

namespace CleanC.Core.Services
{
    /// <summary>
    /// تنظیمات پایدار برنامه: تم، رفتار تری آیکون، هندسه ی پنجره، نمای آخر و انتخاب های اسکن.
    /// در %LOCALAPPDATA%\CleanC\settings.json نگهداری می شود.
    /// </summary>
    public sealed class AppSettings
    {
        /// <summary>سه حالت مجاز: "System" | "Light" | "Dark"</summary>
        public string Theme { get; set; } = "System";

        /// <summary>کد زبان برنامه یا "Auto" برای تشخیص خودکار از ساعت/منطقه ویندوز</summary>
        public string Language { get; set; } = "Auto";

        /// <summary>بستن پنجره به جای خروج، برنامه را در تری آیکون نگه می دارد.</summary>
        public bool CloseToTray { get; set; } = true;

        /// <summary>کوچک نمایی پنجره آن را در تری آیکون پنهان می کند.</summary>
        public bool MinimizeToTray { get; set; }

        /// <summary>در اجراهای بعدی اندازه و موقعیت پنجره بازیابی شود.</summary>
        public bool SaveWindowPlacement { get; set; } = true;

        /// <summary>نمای آخر (داشبورد / ابزارها / تنظیمات) در اجراهای بعدی بازیابی شود.</summary>
        public bool SaveLastView { get; set; } = true;

        /// <summary>تیک خوردن/ندوردن آیتم های اسکن پاکسازی بین اجراها حفظ شود.</summary>
        public bool SaveScanSelections { get; set; } = true;

        // هندسه ی پنجره در پیکسل فیزیکی (همان چیزی که AppWindow برمی گرداند)
        public int WindowWidth { get; set; } = 1080;
        public int WindowHeight { get; set; } = 800;
        public int WindowX { get; set; } = int.MinValue;
        public int WindowY { get; set; } = int.MinValue;

        /// <summary>"dashboard" | "tools" | "settings"</summary>
        public string LastView { get; set; } = "dashboard";

        /// <summary>شناسه ی آیتم زباله دان → آیا در اسکن تیک بخورد.</summary>
        public Dictionary<string, bool> JunkSelections { get; set; } = new();

        /// <summary>
        /// F3 — رفتار حذفِ فایل های شخصی کاربر (تکراری/حجیم/دانلودها/دسکتاپ):
        /// "RecycleBin" (پیش فرض) | "Quarantine" | "Permanent".
        /// کش و زباله ها همیشه قطعی حذف می شوند چون هدفشان آزادسازی فضاست.
        /// </summary>
        public string DeleteMode { get; set; } = "RecycleBin";

        /// <summary>
        /// B4 — تاریخچه ی اعلان ها؛ باقی مانده ی ری استارت ها (سقف در Normalize).
        /// Severity به صورت رشته ذخیره می شود تا JSON ساده بماند.
        /// </summary>
        public List<NotificationHistoryEntry> NotificationHistory { get; set; } = new();

        /// <summary>آیا مکان یابی معتبری ذخیره شده و ارزش بازیابی دارد.</summary>
        [JsonIgnore]
        public bool HasSavedPlacement =>
            WindowWidth >= 660 && WindowHeight >= 550 &&
            WindowX != int.MinValue && WindowY != int.MinValue;

        // ====================================================================
        // کاراکتر هیرو (بخش «کاراکتر» در Settings)
        //
        // همه nullable هستند و null پیش فرض است: وقتی null باشد در پیام «settings»
        // اصلاً ارسال نمی شود و CleanC.hero.html مقدار پیش فرض / ذخیره شده ی خودش
        // را نگه می دارد. فقط وقتی کاربر واقعاً چیزی را عوض کرده مقدار می فرستیم.
        // ====================================================================

        /// <summary>لحن گفتار: "joke" (شوخ) | "serious" (جدی) | "calm" (آرام)</summary>
        public string? CharacterTone { get; set; }

        /// <summary>اسمی که کاراکتر کاربر را با آن صدا می زند.</summary>
        public string? CharacterUserName { get; set; }

        /// <summary>اسم خود کاراکتر (پیش فرض خود HTML «سی‌سی» است).</summary>
        public string? CharacterName { get; set; }

        /// <summary>شهر برای ماژول آب و هوا؛ خالی یعنی تشخیص خودکار از IP.</summary>
        public string? CharacterCity { get; set; }

        /// <summary>رفتار آرام: کمتر حرف می زند و کمتر تکان می خورد.</summary>
        public bool? CharacterCalm { get; set; }

        /// <summary>حالت تند و سریع در اجرای مراحل (کوتاه کردن انیمیشن ها).</summary>
        public bool? CharacterFast { get; set; }

        /// <summary>شدت رفتارها: "off" | "low" | "mid" | "high"</summary>
        public string? CharacterIntensity { get; set; }

        /// <summary>حباب گفتار روی کاراکتر نمایش داده شود.</summary>
        public bool? CharacterBubble { get; set; }

        /// <summary>صحنه/محیط پشت کاراکتر (آسمان، اتاق و…) فعال باشد.</summary>
        public bool? CharacterEnv { get; set; }

        /// <summary>صدای کاراکتر فعال باشد.</summary>
        public bool? CharacterSound { get; set; }

        /// <summary>کد ظاهر کاراکتر (ACC)؛ null یعنی ظاهر پیش فرض روز.</summary>
        public string? CharacterLook { get; set; }

        /// <summary>کد ظاهر مناسبتی؛ null یعنی غیرفعال.</summary>
        public string? CharacterOccLook { get; set; }

        /// <summary>مقیاس اندازهٔ کاراکتر: 0.85 | 1 | 1.15</summary>
        public double? CharacterSize { get; set; }

        /// <summary>افست روز برای تقویم قمری (مناسبت ها): -1 | 0 | 1 — طرفداران تقویم ایران/عربی ممکن است یک روز جابه جا ببینند.</summary>
        public int? CharacterLun { get; set; }

        /// <summary>نگاشت تم ذخیره شده به ElementTheme (پیش فرض: از تنظیمات سیستم).</summary>
        [JsonIgnore]
        public bool IsDarkRequested => string.Equals(Theme, "Dark", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>یک رکورد از تاریخچه ی اعلان ها (B4) — در settings.json نگه داشته می شود.</summary>
    public sealed class NotificationHistoryEntry
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        /// <summary>Informational | Success | Warning | Error</summary>
        public string Severity { get; set; } = "Informational";
        public DateTime At { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// بارگذاری/ذخیره ی <see cref="AppSettings"/> به صورت اتمیک و thread-safe.
    /// هیچ خطای دیسک به بالا پرتاب نمی شود؛ خطاها در ignored.log ثبت می شوند.
    /// </summary>
    public static class SettingsService
    {
        private static readonly object Sync = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string _filePath = BuildDefaultPath();

        /// <summary>مسیر فایل تنظیمات — برای تست قابل تغییر است.</summary>
        public static string FilePath
        {
            get => _filePath;
            set => _filePath = string.IsNullOrWhiteSpace(value) ? BuildDefaultPath() : value;
        }

        private static string BuildDefaultPath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanC", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                string path = FilePath;
                if (File.Exists(path))
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
                    if (loaded != null)
                    {
                        Normalize(loaded);
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SettingsService.Load");
            }

            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            if (settings == null) return;

            lock (Sync)
            {
                try
                {
                    string path = FilePath;
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    // نوشتن در فایل موقت و جایگزینی اتمیک تا نیمه نوشته ها خراب نشوند
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
                    File.Move(tmp, path, overwrite: true);
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex, "SettingsService.Save");
                }
            }
        }

        private static void Normalize(AppSettings settings)
        {
            // دی سریالایز کردن مقایسه گر پیش فرض می سازد؛ برای id های غیرحساس به حروف بهتر است
            // یک دیکشنری با مقایسه گر نادیده گرفتن حروف بسازیم.
            var normalized = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (settings.JunkSelections != null)
            {
                foreach (var kv in settings.JunkSelections)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key)) normalized[kv.Key] = kv.Value;
                }
            }
            settings.JunkSelections = normalized;

            if (settings.WindowWidth < 660 || settings.WindowHeight < 550)
            {
                settings.WindowWidth = 1080;
                settings.WindowHeight = 800;
            }

            string? view = settings.LastView?.Trim().ToLowerInvariant();
            settings.LastView = view is "tools" or "settings" or "dashboard" ? view! : "dashboard";

            string? theme = settings.Theme?.Trim();
            settings.Theme = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light"
                           : string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark"
                           : "System";

            if (string.IsNullOrWhiteSpace(settings.Language)) settings.Language = "Auto";

            // F3 — مقدار نامعتبر حذف به پیش فرض برمی گردد
            settings.DeleteMode = SafeDeleteService.ModeToString(SafeDeleteService.ParseMode(settings.DeleteMode));

            // B4 — تاریخچه ی اعلان ها: سقف ۵۰ مورد، مرتب سازی جدیدترین اول،
            // حذف رکوردهای خالی و کوتاه کردن پیام های خیلی بلند.
            const int MaxNotifications = 50;
            settings.NotificationHistory = (settings.NotificationHistory ?? new List<NotificationHistoryEntry>())
                .Where(e => e != null &&
                            (!string.IsNullOrWhiteSpace(e.Title) || !string.IsNullOrWhiteSpace(e.Message)))
                .OrderByDescending(e => e.At)
                .Take(MaxNotifications)
                .ToList();

            foreach (var entry in settings.NotificationHistory)
            {
                entry.Title = (entry.Title ?? string.Empty).Trim();
                entry.Message = (entry.Message ?? string.Empty).Trim();
                if (entry.Message.Length > 400) entry.Message = entry.Message.Substring(0, 400);

                entry.Severity = entry.Severity is "Success" or "Warning" or "Error" or "Informational"
                    ? entry.Severity
                    : "Informational";
            }
        }
    }
}
