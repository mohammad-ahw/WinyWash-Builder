// src/CleanC.Core/Services/LocalizationService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CleanC.Core.Diagnostics;

namespace CleanC.Core.Services
{
    public sealed class LanguageInfo
    {
        public string Code { get; set; } = string.Empty;
        public string NativeName { get; set; } = string.Empty;
        public string FlagName { get; set; } = string.Empty;
        public bool IsRtl { get; set; }

        public override string ToString() => FlagName;
    }

    /// <summary>
    /// Multi-language localization service for CleanC.
    /// Supports dynamic resw resource files in Resources/Strings/Strings.{code}.resw.
    /// Automatically detects default language from Windows timezone and region.
    /// </summary>
    public static class LocalizationService
    {
        private static readonly Dictionary<string, Dictionary<string, string>> _catalogs = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<LanguageInfo> _languages = new();
        private static LanguageInfo _current = new() { Code = "fa-IR", NativeName = "فارسی", FlagName = "🇮🇷 فارسی", IsRtl = true };

        public static event Action? LanguageChanged;

        public static IReadOnlyList<LanguageInfo> SupportedLanguages => _languages;
        public static LanguageInfo CurrentLanguage => _current;
        public static bool IsRtl => _current.IsRtl;

        static LocalizationService()
        {
            RegisterDefaultLanguageMetadata();
            RegisterBuiltInFallbackCatalogs();
            LoadCatalogs();
        }

        private static void RegisterDefaultLanguageMetadata()
        {
            _languages.Clear();
            _languages.AddRange(new[]
            {
                new LanguageInfo { Code = "fa-IR", NativeName = "فارسی", FlagName = "🇮🇷 فارسی", IsRtl = true },
                new LanguageInfo { Code = "en-US", NativeName = "English", FlagName = "🇬🇧 English", IsRtl = false },
                new LanguageInfo { Code = "ar-SA", NativeName = "العربية", FlagName = "🇸🇦 العربية", IsRtl = true },
                new LanguageInfo { Code = "zh-CN", NativeName = "简体中文", FlagName = "🇨🇳 简体中文", IsRtl = false },
                new LanguageInfo { Code = "ja-JP", NativeName = "日本語", FlagName = "🇯🇵 日本語", IsRtl = false },
                new LanguageInfo { Code = "ko-KR", NativeName = "한국어", FlagName = "🇰🇷 한국어", IsRtl = false },
                new LanguageInfo { Code = "ru-RU", NativeName = "Русский", FlagName = "🇷🇺 Русский", IsRtl = false },
                new LanguageInfo { Code = "de-DE", NativeName = "Deutsch", FlagName = "🇩🇪 Deutsch", IsRtl = false },
                new LanguageInfo { Code = "fr-FR", NativeName = "Français", FlagName = "🇫🇷 Français", IsRtl = false },
                new LanguageInfo { Code = "es-ES", NativeName = "Español", FlagName = "🇪🇸 Español", IsRtl = false }
            });
        }

        private static void RegisterBuiltInFallbackCatalogs()
        {
            var faFallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "WinyWash",
                ["RunningAsAdmin"] = "اجرا با دسترسی مدیر (Admin)",
                ["RestoreCenter"] = "مرکز بازیابی و بازگشت تغییرات",
                ["RefreshStatus"] = "بروزرسانی وضعیت سیستم",
                ["NotificationHistory"] = "تاریخچه عملیات و اعلان ها",
                ["ToggleTheme"] = "تغییر تم (تاریک / روشن)",
                ["Settings"] = "تنظیمات برنامه (Settings)",
                ["NavDashboard"] = "داشبورد بهینه سازی",
                ["NavTools"] = "ابزارهای سیستمی ویندوز",
                ["HeroCategory"] = "وضعیت سلامت و کارایی سیستم",
                ["HeroTitleReady"] = "سیستم آماده بهینه سازی است",
                ["HeroSubtitleReady"] = "با یک کلیک فایل های موقت پاکسازی، حافظه رم آزاد و سرویس ها تنظیم می شوند.",
                ["HeroTitleOptimized"] = "سیستم در وضعیت بهینه و پرسرعت قرار دارد",
                ["HeroSubtitleOptimized"] = "تمام فایل های موقت پاکسازی و سرویس ها بهینه سازی شدند.",
                ["CleanableSpace"] = "فضای قابل آزادسازی",
                ["LastCleanup"] = "آخرین پاکسازی",
                ["TotalFreed"] = "مجموع آزاد شده",
                ["QuickBoost"] = "بهینه سازی و ارتقای سرعت سیستم",
                ["QuickBoostSub"] = "پاکسازی متوالی فایل های زائد، رم و سرویس ها",
                ["AdvancedCleanTitle"] = "سفارشی سازی موارد پاکسازی و بهینه سازی",
                ["AdvancedCleanSub"] = "انتخاب دسته های فایل های موقت، کش ها و تنظیمات",
                ["ScanSystem"] = "اسکن تفصیلی سیستم",
                ["SelectAll"] = "انتخاب همه",
                ["DeselectAll"] = "لغو انتخاب",
                ["CleanSelected"] = "پاکسازی موارد انتخابی",
                ["RamTitle"] = "حافظه رم",
                ["CpuTitle"] = "پردازنده",
                ["DiskBreakdownTitle"] = "تحلیل فضای دیسک C:",
                ["NetworkTrafficTitle"] = "ترافیک شبکه",
                ["CleanupHistoryTitle"] = "فضای آزادشده در ۷ روز اخیر",
                ["ToolsHeaderTitle"] = "ابزارهای سیستمی ویندوز",
                ["ToolsHeaderSubtitle"] = "مجموعه ابزارهای کاربردی مدیریت و بهینه‌سازی سیستم",
                ["ToolsSectionFrequentlyUsed"] = "پرکاربرد",
                ["ToolsSectionOtherTools"] = "سایر ابزارها",
                ["LauncherWindowsUpdateTitle"] = "آپدیت ویندوز",
                ["LauncherWindowsUpdateDesc"] = "بررسی، توقف ۵ هفته ای و رفع گیر کردن دانلود",
                ["LauncherUninstallerTitle"] = "حذف نرم افزارها",
                ["LauncherUninstallerDesc"] = "لیست برنامه ها با آیکون اختصاصی و حذف آسان",
                ["LauncherStartupTitle"] = "برنامه های استارتاپ",
                ["LauncherStartupDesc"] = "مدیریت برنامه های شروع خودکار و سرعت بوت",
                ["LauncherSpeedTestTitle"] = "تست سرعت اینترنت",
                ["LauncherSpeedTestDesc"] = "سنجش دانلود واقعی، آپلود، پینگ و ISP",
                ["LauncherToolDeepStorage"] = "پاکسازی عمیق دیسک",
                ["LauncherToolSpecs"] = "مشخصات سیستم",
                ["LauncherToolPersonalization"] = "استایل و گرافیک",
                ["LauncherToolDrivers"] = "مدیریت درایورها",
                ["LauncherToolDns"] = "تنظیم DNS و تحریم شکن",
                ["LauncherToolPrivacy"] = "حریم خصوصی و ضدجاسوسی",
                ["LauncherToolRecycleBin"] = "سطل بازیافت",
                ["SettingsTitle"] = "تنظیمات برنامه",
                ["SettingsSub"] = "شخصی سازی ظاهر، زبان، رفتار پس زمینه و تهیه گزارش",
                ["LanguageTitle"] = "زبان برنامه",
                ["LanguageSub"] = "انتخاب زبان رابط کاربری، جهت نوشتاری (RTL / LTR) و تایپوگرافی نیتیو سیستم عامل.",
                ["ThemeTitle"] = "تم برنامه",
                ["ThemeSub"] = "«سیستم» از تم ویندوز پیروی می کند؛ دو حالت دیگر تم را ثابت نگه می دارند.",
                ["ThemeSystem"] = "سیستم",
                ["ThemeLight"] = "روشن",
                ["ThemeDark"] = "تاریک",
                ["BackgroundBehaviorTitle"] = "اجرا در پس زمینه و نوار وظیفه",
                ["BackgroundBehaviorSub"] = "کنترل حضور برنامه در پس زمینه و دسترسی سریع از طریق آیکون کنار ساعت ویندوز.",
                ["CloseToTray"] = "با بستن پنجره خارج نشو؛ در پس زمینه (کنار ساعت) بمان",
                ["MinimizeToTray"] = "کوچک نمایی پنجره، برنامه را در کنار ساعت پنهان کند",
                ["AutoStartTitle"] = "شروع همراه با ویندوز",
                ["AutoStartSub"] = "با ورود به حساب کاربری، WinyWash به صورت خودکار اجرا شود.",
                ["DeleteBehaviorTitle"] = "رفتار حذف فایل های شخصی",
                ["DeleteBehaviorSub"] = "برای حذف تکراری ها، فایل های حجیم و دانلودها. فایل های زائد سیستم همیشه قطعی پاک می شوند.",
                ["DeleteModeRecycle"] = "انتقال به سطل بازیافت ویندوز (پیشنهادی)",
                ["DeleteModeQuarantine"] = "قرنطینه WinyWash — قابل بازیابی از مرکز بازیابی",
                ["DeleteModePermanent"] = "حذف قطعی — آزادسازی فوری فضا",
                ["AutoStateRestoreTitle"] = "بازیابی خودکار وضعیت",
                ["AutoStateRestoreSub"] = "اندازه پنجره، موقعیت و انتخاب های اسکن در اجرای بعدی حفظ شوند.",
                ["SaveWindowPlacement"] = "اندازه و موقعیت پنجره",
                ["SaveLastView"] = "نمای آخر (داشبورد / ابزارها / تنظیمات)",
                ["SaveScanSelections"] = "انتخاب های اسکن پاکسازی",
                ["ResetWindowSize"] = "بازنشانی اندازه پنجره",
                ["ResetAllSettings"] = "بازنشانی همه تنظیمات",
                ["AppDataTitle"] = "داده های برنامه",
                ["OpenSettingsFolder"] = "پوشه تنظیمات",
                ["OpenLogsFolder"] = "پوشه لاگ ها",
                ["ExportReportTitle"] = "خروجی گزارش",
                ["ExportReportSub"] = "خروجی اطلاعات جامع سیستم جهت ارسال به هوش مصنوعی یا مشخصات سخت افزاری.",
                ["ExportAiReport"] = "گزارش جامع برای هوش مصنوعی (.md)",
                ["ExportSpecsReport"] = "مشخصات کامل سیستم (.txt)",
                ["AboutTitle"] = "درباره WinyWash",
                ["AboutSub"] = "ابزار مدرن بهینه سازی، پاکسازی و عیب یابی سخت افزار ویندوز.",
                ["CheckUpdates"] = "بررسی به روزرسانی",
                ["ProjectPage"] = "صفحه پروژه",
                ["CopyDiagnostics"] = "کپی اطلاعات عیب یابی",
                ["BackToTools"] = "بازگشت به فهرست ابزارها",
                ["BackButton"] = "بازگشت",
                ["WindowsUpdateTitle"] = "مدیریت بروزرسانی های ویندوز",
                ["WindowsUpdateSub"] = "بررسی آپدیت ها، توقف رسمی ۵ هفته ای و رفع گیر کردن دانلود",
                ["CheckNewUpdates"] = "بررسی آپدیت های جدید",
                ["Pause5Weeks"] = "توقف رسمی تا ۵ هفته",
                ["ResumeUpdates"] = "ادامه دریافت",
                ["UpdateService"] = "سرویس ویندوز آپدیت (wuauserv)",
                ["DisableService"] = "غیرفعال سازی",
                ["EnableService"] = "فعال سازی",
                ["FixStuckUpdates"] = "رفع گیر کردن و خطاها",
                ["CleanCache"] = "پاکسازی کش",
                ["PendingUpdates"] = "آپدیت های در انتظار نصب",
                ["NoPendingUpdates"] = "هیچ آپدیت معلق یا در انتظار نصبی یافت نشد. ویندوز کاملاً بروز است.",
                ["SpeedTestTitle"] = "تست سرعت و کیفیت اینترنت",
                ["SpeedTestSub"] = "سنجش دانلود واقعی، آپلود، پینگ و اطلاعات ارتباطی شبکه",
                ["StartSpeedTest"] = "شروع تست سرعت",
                ["StopSpeedTest"] = "توقف تست",
                ["PingLatency"] = "تاخیر پینگ (Ping)",
                ["DownloadSpeed"] = "سرعت دانلود",
                ["UploadSpeed"] = "سرعت آپلود",
                ["DeepStorageTitle"] = "پاکسازی عمیق و سازماندهی دیسک",
                ["DeepStorageSub"] = "شناسایی فایل های تکراری، پوشه های خالی، دانلودها، دسکتاپ و فایل های حجیم",
                ["StartDeepScan"] = "شروع پویش عمیق",
                ["TabDuplicates"] = "فایل های تکراری",
                ["TabEmptyFolders"] = "پوشه های خالی",
                ["TabMess"] = "فایل های زائد و نامرتب",
                ["TabLargeFiles"] = "فایل های حجیم",
                ["CleanSelectedItems"] = "پاکسازی موارد انتخابی",
                ["UninstallerTitle"] = "مدیریت و حذف نرم افزارها",
                ["SearchApps"] = "جستجوی نرم افزار...",
                ["SortRecent"] = "جدیدترین نصب",
                ["SortSize"] = "بیشترین حجم",
                ["SortName"] = "نام (الفبا)",
                ["UninstallBatch"] = "حذف انتخاب شده ها",
                ["StartupTitle"] = "مدیریت برنامه های شروع خودکار",
                ["StartupSub"] = "مدیریت برنامه های استارتاپ و سرعت بوت ویندوز",
                ["RefreshList"] = "بروزرسانی لیست",
                ["DriversTitle"] = "مدیریت و پاکسازی درایورها",
                ["DriversSub"] = "شناسایی خطاها، پاکسازی ایمن نسخه های تکراری DriverStore",
                ["CheckDriverUpdates"] = "بررسی آپدیت درایورها",
                ["DnsTitle"] = "تنظیم و بهینه سازی DNS",
                ["DnsSub"] = "کاهش پینگ بازی، تحریم شکن و امنیت وبگردی",
                ["PrivacyTitle"] = "حریم خصوصی و غیرفعال سازی تلمتری",
                ["PrivacySub"] = "قطع ارسال داده های تشخیصی، تبلیغات و سرویس های ردیابی مایکروسافت",
                ["SpecsTitle"] = "مشخصات جامع سخت افزار",
                ["SpecsSub"] = "اطلاعات لحظه ای پردازنده، رم، مادربرد، گرافیک، درایوها و لایسنس",
                ["PersonalizationTitle"] = "استایل، تم و گرافیک ویندوز",
                ["PersonalizationSub"] = "اعمال آنی بدون نیاز به ریستارت",
                ["RecycleBinTitle"] = "مدیریت سطل بازیافت و فایل های حذف شده",
                ["RecycleBinSub"] = "مشاهده اشیاء داخل سطل، بازیابی و آزادسازی قطعی فضا",
                ["HealthScoreTitle"] = "امتیاز سلامت سیستم",
                ["HealthExcellent"] = "عالی",
                ["HealthGood"] = "خوب",
                ["HealthFair"] = "متوسط",
                ["HealthNeedsOpt"] = "نیاز به بهینه سازی",
                ["HealthDescExcellent"] = "وضعیت عمومی سیستم پایدار و عالی است",
                ["HealthDescGood"] = "عملکرد کلی سیستم خوب و مطلوب است",
                ["HealthDescFair"] = "سیستم نیازمند آزادسازی رم و بهینه سازی فایل ها",
                ["HealthDescPoor"] = "مصرف منابع بالا؛ اجرای پاکسازی سریع توصیه می شود",
                ["RamCurrentlyUsed"] = "فضای مصرفی در حال حاضر",
                ["Total"] = "مجموع",
                ["HighUsage"] = "مصرف بالا",
                ["MediumUsage"] = "مصرف متوسط",
                ["SmoothUsage"] = "روان",
                ["CpuLoadSummary"] = "بار پردازشی هسته ها",
                ["PowerPlugged"] = "متصل به برق",
                ["PowerCharging"] = "در حال شارژ",
                ["PowerBattery"] = "روی باتری",
                ["NetworkTrafficTitle"] = "پایش ترافیک اینترنت",
                ["ActiveTraffic"] = "مصرف فعال",
                ["IdleTraffic"] = "آماده به کار",
                ["NoBackgroundTraffic"] = "بدون مصرف پس زمینه (شبکه پایدار)",
                ["NetworkSysTraffic"] = "ترافیک سیستم و ویندوز",
                ["DiskBreakdownTitle"] = "تحلیل فضای دیسک C:",
                ["DiskUsed"] = "استفاده شده",
                ["DiskCleanable"] = "قابل پاکسازی",
                ["DiskFree"] = "آزاد",
                ["FreeOf"] = "آزاد از",
                ["CleanupHistoryTitle"] = "فضای آزادشده در ۷ روز اخیر",
                ["ItemsSelected"] = "مورد انتخاب شده",
                ["TotalDetected"] = "مجموع شناسایی شده",
                ["Scanning"] = "در حال اسکن...",
                ["RefreshedAfterClean"] = "نوسازی پس از پاکسازی",
                ["AutoStartTitle"] = "شروع همراه با ویندوز",
                ["AutoStartDesc"] = "با ورود به حساب کاربری، WinyWash به صورت خودکار اجرا شود. همان آیتمی که در Task Manager › Startup دیده می شود.",
                ["DeleteBehaviorTitle"] = "رفتار حذف فایل های شخصی",
                ["DeleteBehaviorSub"] = "برای حذف تکراری ها، فایل های حجیم و سازماندهی دانلودها/دسکتاپ. کش و زباله ها همیشه قطعی حذف می شوند تا فضا واقعاً آزاد شود.",
                ["DeleteModeRecycle"] = "انتقال به سطل بازیافت (پیش فرض)",
                ["DeleteModeQuarantine"] = "قرنطینه ی WinyWash — نگهداری در پوشه ی برنامه و قابل بازگردانی از مرکز بازیابی",
                ["DeleteModePermanent"] = "حذف قطعی — فوراً آزاد می شود اما قابل بازگردانی نیست",
                ["AutoStateRestoreTitle"] = "بازیابی خودکار وضعیت",
                ["AutoStateRestoreSub"] = "هر تغییر بلافاصله ذخیره می شود و در اجرای بعدی برگردانده می شود.",
                ["SaveWindowPlacement"] = "اندازه و موقعیت پنجره",
                ["SaveLastView"] = "نمای آخر (داشبورد / ابزارها / تنظیمات)",
                ["SaveScanSelections"] = "انتخاب های اسکن پاکسازی",
                ["ResetWindowSize"] = "بازنشانی اندازه ی پنجره",
                ["ResetAllSettings"] = "بازنشانی همه ی تنظیمات",
                ["AppDataTitle"] = "داده های برنامه",
                ["AppDataSettingsLabel"] = "تنظیمات:",
                ["AppDataLogsLabel"] = "لاگ خطاها:",
                ["OpenSettingsFolder"] = "باز کردن پوشه ی تنظیمات",
                ["OpenLogsFolder"] = "باز کردن پوشه ی لاگ ها",
                ["VersionLabel"] = "نسخه برنامه:",
                ["OsLabel"] = "سیستم عامل:",
                ["NotificationHistoryTitle"] = "تاریخچه اعلان ها و اقدامات",
                ["NotificationClear"] = "پاکسازی",
                ["NotificationEmpty"] = "هنوز هیچ اعلانی ثبت نشده است.",
                ["SpecsHeaderTitle"] = "مشخصات جامع سخت افزار",
                ["SpecsHeaderSubtitle"] = "اطلاعات لحظه ای پردازنده، رم، مادربرد، گرافیک، درایوها و لایسنس",
                ["CopySpecs"] = "کپی مشخصات",
                ["RefreshSpecs"] = "بروزرسانی",
                ["SpecCores"] = "هسته",
                ["SpecThreads"] = "رشته",
                ["SpecSlots"] = "اسلات",
                ["SpecDriver"] = "درایور",
                ["UninstallerHeaderTitle"] = "مدیریت و حذف نرم افزارها",
                ["UninstallerSearchPlaceholder"] = "جستجوی نرم افزار...",
                ["UninstallerSortNewest"] = "جدیدترین نصب",
                ["UninstallerSortLargest"] = "بیشترین حجم",
                ["UninstallerSortName"] = "نام (الفبا)",
                ["SpeedTestHeaderTitle"] = "تست سرعت اینترنت",
                ["SpeedTestHeaderSubtitle"] = "سنجش پهنای باند دانلود، آپلود و تاخیر از سرورهای Cloudflare",
                ["SpeedIp"] = "IP",
                ["SpeedIsp"] = "ISP",
                ["SpeedLocation"] = "موقعیت",
                ["SpeedDetecting"] = "در حال شناسایی...",
                ["PersonalizationHeaderTitle"] = "استایل، تم و گرافیک ویندوز",
                ["PersonalizationHeaderSubtitle"] = "اعمال آنی بدون نیاز به ریستارت",
                ["StartupHeaderTitle"] = "مدیریت برنامه های شروع خودکار",
                ["StartupRefresh"] = "بروزرسانی لیست",
                ["WindowsUpdateHeaderTitle"] = "مدیریت بروزرسانی های ویندوز",
                ["WindowsUpdateHeaderSubtitle"] = "بررسی آپدیت ها، توقف رسمی ۵ هفته ای و رفع گیر کردن دانلود",
                ["UpdateStatusHeadline"] = "وضعیت آپدیت های سیستم عامل",
                ["UpdateStatusActive"] = "فعال",
                ["UpdateRow1Title"] = "توقف رسمی تا ۵ هفته",
                ["UpdateRow1Sub"] = "توقف دریافت بسته ها مطابق مکانیزم رسمی تنظیمات ویندوز",
                ["UpdateRow2Title"] = "سرویس Windows Update (wuauserv)",
                ["UpdateRow2Sub"] = "فعال یا غیرفعال سازی کامل سرویس پس زمینه",
                ["UpdateRow3Title"] = "رفع گیر کردن آپدیت ها (پاکسازی کش)",
                ["UpdateRow3Sub"] = "توقف سرویس، پاکسازی کامل پوشه SoftwareDistribution و راه اندازی مجدد",
                ["UpdateRow4Title"] = "تنظیمات بروزرسانی ویندوز",
                ["UpdateRow4Sub"] = "باز کردن مستقیم صفحه Windows Update در اپلیکیشن Settings ویندوز",
                ["UpdatePendingSectionTitle"] = "آپدیت های در انتظار نصب",
                ["NoUpdatesBanner"] = "هیچ آپدیت معلق یا در انتظار نصبی یافت نشد. ویندوز کاملاً بروز است.",
                ["DriversHeaderTitle"] = "مدیریت و پاکسازی درایورها",
                ["DriversHeaderSubtitle"] = "شناسایی خطاها، پاکسازی ایمن نسخه های تکراری DriverStore",
                ["DriversTotalLabel"] = "کل بسته های درایور",
                ["DriversErrorLabel"] = "خطاهای سخت افزاری",
                ["DriversDuplicateLabel"] = "نسخه های تکراری",
                ["DriversDuplicateBannerTitle"] = "پاکسازی بسته های تکراری و اشغال کننده فضا",
                ["DriversDuplicateBannerSub"] = "نسخه های قدیمی با ایجاد Restore Point به صورت ایمن حذف می شوند.",
                ["DriversSearchPlaceholder"] = "جستجو در بین درایورها...",
                ["DriversSelectAll"] = "انتخاب همه",
                ["DriversBatchDelete"] = "حذف انتخاب شده ها",
                ["DnsHeaderTitle"] = "تنظیم هوشمند DNS",
                ["DnsHeaderSubtitle"] = "عبور مطمئن از تحریم ها و کاهش پینگ اتصال",
                ["ResetDnsDhcp"] = "ریست به خودکار (DHCP)",
                ["ActiveDnsLabel"] = "دی ان اس فعال در حال حاضر",
                ["FlushDns"] = "تخلیه کش DNS",
                ["DnsPresetsTitle"] = "سرویس های آماده",
                ["PrivacyHeaderTitle"] = "حریم خصوصی و حذف تلمتری",
                ["PrivacyHeaderSubtitle"] = "قطع ارسال گزارش ها و بهینه سازی جستجو",
                ["PrivacyBannerTitle"] = "اعمال و ذخیره خودکار بلادرنگ",
                ["PrivacyBannerSub"] = "همه گزینه ها با سوییچ، آنی در رجیستری و سرویس ها اعمال می شوند.",
                ["PrivacyBannerBadge"] = "فعال",
                ["PrivacyFooterHint"] = "برای حداکثر امنیت، فعال بودن همه گزینه ها پیشنهاد می شود.",
                ["PrivacyResetDefaults"] = "پیش فرض ویندوز",
                ["PrivacyEnableAll"] = "فعال سازی همه",
                ["DeepStorageHeaderTitle"] = "پاکسازی عمیق دیسک و فایل های زائد",
                ["DeepStorageHeaderSubtitle"] = "شناسایی فایل های تکراری، پوشه های خالی، دانلودها و فایل های حجیم",
                ["DeepStorageDuplicatesLabel"] = "فایل های تکراری",
                ["DeepStorageEmptyFoldersLabel"] = "پوشه های خالی",
                ["DeepStorageEmptyFoldersSub"] = "فاقد فایل یا زیرپوشه",
                ["DeepStorageLargeFilesLabel"] = "فایل های حجیم",
                ["DeepStorageLargeFilesSub"] = "بالاتر از ۵۰۰ مگابایت",
                ["DeepStorageSortLabel"] = "مرتب سازی:",
                ["DeepStorageSortSize"] = "حجم 🔻",
                ["DeepStorageSortDate"] = "تاریخ 📅",
                ["DeepStorageSortName"] = "نام 🔤",
                ["DeepStorageScanStatus"] = "در حال پویش عمیق ساختار فایل ها و محاسبه هش محتوا...",
                ["RecycleBinHeaderTitle"] = "مدیریت سطل بازیافت",
                ["RecycleBinHeaderSubtitle"] = "پیش نمایش آیتم به آیتم؛ بازگردانی یا حذف قطعی تک تک اشیاء",
                ["RecycleBinRefresh"] = "تازه سازی",
                ["RecycleBinTotalLabel"] = "تعداد اشیاء",
                ["RecycleBinSizeLabel"] = "حجم اشغال شده",
                ["RecycleBinRestoreSelected"] = "بازگردانی انتخاب شده ها",
                ["RecycleBinDeleteSelected"] = "حذف قطعی انتخاب شده ها",
                ["RecycleBinEmptyBtn"] = "خالی کردن کل سطل",
                ["RecycleBinEmptyHint"] = "سطل بازیافت خالی است؛ چیزی برای بازگردانی یا حذف وجود ندارد.",
                ["RecycleBinLoading"] = "در حال خواندن فهرست سطل بازیافت...",
                ["HeroTitleHighRam"] = "مصرف حافظه رم بالاست",
                ["HeroSubtitleHighRam"] = "با ۱ کلیک فایل های موقت پاک، حافظه رم آزاد و پردازه های اضافی بسته می شوند.",
                ["QuickBoostRunning"] = "در حال بهینه سازی سیستم...",
                ["OptimizationFeatures"] = "قابلیت بهینه سازی",
                ["JunkCleanSystem"] = "فایل های زائد سیستم",
                ["JunkBrowsers"] = "کش و سابقه مرورگرها",
                ["JunkApps"] = "نرم افزارها و ابزارهای توسعه",
                ["JunkWindowsCaches"] = "کش های ساختار ویندوز",
                ["JunkLogs"] = "لاگ ها و گزارش های خطا",
                ["JunkDelivery"] = "فایل های بروزرسانی ویندوز",
                ["JunkBoost"] = "بهینه سازی زنده سیستم",

                // ── فاز ۶: دسته بندی های پویش عمیق ─────────────────────────────
                ["MessCatOldInstaller"] = "فایل نصب قدیمی (Setup / Installer)",
                ["MessCatOldArchive"] = "فایل فشرده (Zip / Rar)",
                ["MessCatDuplicateCopy"] = "نسخه کپی شده (Copy / Duplicated)",
                ["MessCatOldScreenshot"] = "اسکرین شات قدیمی",
                ["MessCatTempJunk"] = "فایل موقت و زائد (Temp / Junk)",
                ["MessCatLargeFile"] = "فایل بسیار حجیم (>200MB)",
                ["MessCatExtractedArchiveDup"] = "فایل آرشیو آنزیپ‌شده (پوشه هم‌نام موجود است)",
                ["MessCatIncompleteDownload"] = "دانلود ناتمام و موقت (crdownload / part)",
                ["MessCatOemDriveRoot"] = "بازمانده ی نصب سازنده در ریشه ی درایو",
                ["MessCatUpdater"] = "بازمانده ی آپدیتر (Electron / Squirrel)",
                ["MessCatSyncDevice"] = "بازمانده ی انتقال دستگاه / تمپ OneDrive",
                ["MessCatOfficeLock"] = "فایل قفل موقت آفیس (~$)",
                ["MessCatDevTools"] = "کش محیط‌های برنامه‌نویسی و کانتینرها (npm, Docker, pip)",
                ["EvDevToolsCache"] = "کش و پکیج‌های محیط توسعه و کانتینر — حذف فقط با تأیید صریح و انتقال امن به قرنطینه انجام می‌شود",
                ["StartupCatAiDev"] = "ابزار هوش مصنوعی و توسعه (اختیاری)",
                ["OptReviewDialogTitle"] = "تأیید بهینه‌سازی ابزارها و برنامه‌های پس‌زمینه",
                ["OptReviewDialogDesc"] = "برنامه‌های زیر در استارتاپ سیستم یا مرورگر فعال هستند. برای افزایش سرعت بوت و سبک‌سازی سیستم، مواردی که مایل به غیرفعال‌سازی آن‌ها هستید را تأیید کنید (تمامی موارد به‌صورت امن ذخیره می‌شوند و هر زمان از مدیریت استارتاپ قابل فعال‌سازی مجدد هستند):",
                ["OptReviewDialogConfirm"] = "غیرفعال‌سازی موارد انتخاب‌شده",
                ["OptReviewDialogCancel"] = "صرف‌نظر و ادامه",
                ["OptReviewExtBadge"] = "افزونه کروم",
                ["DevQuarantineTitle"] = "انتقال امن پکیج‌های برنامه‌نویسی به قرنطینه",
                ["DevQuarantineDesc"] = "موارد انتخاب‌شده شامل پکیج‌ها و کش ابزارهای برنامه‌نویسی (مانند npm، Docker، pip و ...) است. این موارد به «قرنطینه» منتقل می‌شوند و هر زمان نیاز داشتید می‌توانید آن‌ها را از بخش «مرکز بازیابی» به‌راحتی بازگردانی کنید.",
                ["DevQuarantineSuccessNotice"] = "(موارد محیط برنامه‌نویسی به قرنطینه منتقل شدند و هر زمان نیاز داشتید از مرکز بازیابی قابل بازگردانی هستند)",
                ["MessCatOther"] = "سایر",

                // ── فاز ۶: شواهد دیتکتورها (Evidence) ───────────────────────────
                ["EvEmptyFolder30Days"] = "بیش از ۳۰ روز خالی مانده (آخرین تغییر: {0:yyyy/MM/dd})",
                ["EvDownloadCompleted"] = "فایل کامل «{0}» موجود است — بازمانده ی دانلود تمام شده",
                ["EvDownloadAbandoned"] = "بیش از ۲۴ ساعت بدون تغییر — دانلود رهاشده",
                ["EvTmpOld72h"] = "فایل موقت .tmp قدیمی تر از ۷۲ ساعت در پوشه ی دانلود/دسکتاپ",
                ["EvInstallerMatched"] = "نصب کننده ی «{0}» — برنامه نصب شده و این فایل دیگر لازم نیست",
                ["EvInstallerNoMatch"] = "نصب کننده ی قدیمی (بیش از ۱۴ روز) — برنامه ی هم نامی روی سیستم پیدا نشد",
                ["EvCrossDevice"] = "مبادله ی موقت دستگاه ها (CrossDevice)",
                ["EvCrossDeviceSub"] = "پوشه ی واسط Link to Windows / Phone Link برای انتقال فایل بین گوشی و کامپیوتر",
                ["EvOneDriveTemp"] = "پوشه ی موقت OneDrive",
                ["EvOneDriveTempSub"] = "انبار موقت فایل های در حال آپلود OneDrive که رها شده است",
                ["EvDriveRootLeftover"] = "پوشه ی ریشه ی درایو با نام سازنده ی {0} — مدرک قطعی: {1}",
                ["EvRemnantInstallerFile"] = "فایل نصب باقی مانده «{0}»",
                ["EvVendorArtifact"] = "آرتیفکت اختصاصی سازنده «{0}» (تصویر بوت/بسته ی درایور)",
                ["EvPreloadFile"] = "فایل پیش نصب سازنده «{0}»",
                ["EvOfficeLockFile"] = "فایل قفل موقت آفیس — سند بسته شده و این فایل رهاشده است",
                ["EvRootEmptyFolder"] = "پوشه ی خالی در ریشه ی درایو — بیش از ۳۰ روز بدون محتوا (آخرین تغییر: {0:yyyy/MM/dd})",
                ["EvUpdaterPendingInstaller"] = "پوشه ی آپدیتر شامل نصب کننده ی باقی مانده «{0}» — آپدیت نیمه تمام",
                ["EvUpdaterNoPending"] = "پوشه ی آپدیتر با الگوی *-updater / SquirrelTemp (نصب در انتظاری پیدا نشد)",
                ["EvBlockmapOld"] = "فایل .blockmap بازمانده ی آپدیتر الکترون (بیش از ۳۰ روز دست نخورده)",
                ["EvDriverNoHardware"] = "هیچ دستگاهی (حتی جدا شده) به این بسته متصل نیست — سخت افزار مربوطه هرگز یا دیگر روی سیستم نیست",
                ["EvStartupFileMissing"] = "فایل استارتاپ «{0}» روی دیسک وجود ندارد",
                ["EvStartupExeMissing"] = "فایل اجرایی «{0}» وجود ندارد — بازمانده ی برنامه ی حذف شده",
                ["EvTrialInName"] = "نام برنامه شامل «Trial» است",
                ["EvTrialInNameLocalized"] = "نام برنامه شامل «نسخه آزمایشی» است",
                ["NoteProvisioned"] = " — به صورت provisioned در تصویر ویندوز ثبت است و برای کاربران جدید خودکار نصب می شود",

                // ── فاز ۶: فایل های حساس (D17) ─────────────────────────────────
                ["SensitiveLicense"] = "فایل لایسنس نرم‌افزار",
                ["SensitiveSerial"] = "شماره سریال نرم‌افزار",
                ["SensitiveProductKey"] = "کلید محصول (Product Key)",
                ["SensitiveActivation"] = "کد فعال‌سازی",
                ["SensitivePasswords"] = "فایل رمزهای عبور",
                ["SensitiveSshKey"] = "کلید خصوصی SSH",
                ["SensitivePrivateKey"] = "کلید خصوصی",
                ["SensitiveKeyFile"] = "فایل کلید/امنیتی ({0})",

                // ── فاز ۶: قفل مسیرها و برچسب های UI ───────────────────────────
                ["LockUserVetoError"] = "این مسیر توسط کاربر قفل شده است",
                ["LockTooltipLock"] = "قفل کردن — این مسیر هرگز پیشنهاد یا حذف نمی شود",
                ["LockTooltipUnlock"] = "رفع قفل — این مسیر دوباره قابل پیشنهاد می شود",
                ["LockInfoBarTitle"] = "قفل مسیر",
                ["LockSaveFailed"] = "ذخیره ی قفل ناموفق بود — دسترسی به locks.json بررسی شود.",
                ["LockReasonDeepScan"] = "قفل شده از صفحه ی پویش عمیق",
                ["AdminRequired"] = "نیازمند دسترسی ادمین",
                ["SensitiveSectionTitle"] = "فایل های حساس شناسایی شده ({0} مورد — فقط گزارش، بدون حذف)",
                ["BadgeTargetMissing"] = "هدف ناموجود",
                ["BadgeTrial"] = "نسخه آزمایشی",
                ["BadgeComponent"] = "کامپوننت / درایور",
                ["EvSystemComponent"] = "ورودی کامپوننت سیستمی (SystemComponent) — معمولاً درایور یا ابزار سخت افزار؛ فقط با آگاهی کامل حذف کنید",
                ["JunkPrefetchTitle"] = "کش Prefetch ویندوز (فایل های .pf)",
                ["JunkPrefetchDesc"] = "حافظه سرعت اجرای برنامه های ویندوز — خودکار بازسازی می شود و حذفش بی خطر است",
                ["BadgeNoHardware"] = "بدون سخت افزار",
                ["ScanProgressDriveRoots"] = "در حال بررسی ریشه ی درایوها و بازمانده ی آپدیترها...",
                ["DescCrashLogs"] = "لاگ های گزارش خرابی، فایل های WER، دامپ های حافظه و گزارش های کرنل (LiveKernelReports)",

                // ── فاز ۶: درایورها (بنر + دیالوگ حذف) ─────────────────────────
                ["DriverBannerDupAndOrphan"] = "شناسایی {0} بسته تکراری و قدیمی + {1} بسته بدون سخت افزار در DriverStore ویندوز",
                ["DriverBannerOrphan"] = "شناسایی {0} بسته بدون سخت افزار (بازمانده ی دستگاه های جدا شده) در DriverStore ویندوز",
                ["DriverBannerDup"] = "شناسایی {0} بسته تکراری و قدیمی در DriverStore ویندوز",
                ["DriverDeleteOrphanTitle"] = "حذف بسته بدون سخت افزار {0}",
                ["DriverDeleteOldTitle"] = "حذف درایور قدیمی {0}",
                ["DriverDeleteOrphanBody"] = "بسته '{0}' متعلق به سخت افزاری است که دیگر به سیستم متصل نیست.\nآیا از حذف این بسته اطمینان دارید؟\nاین عملیات بدون سوئیچ اجباری انجام شده و در صورت استفاده سیستم متوقف خواهد شد.",
                ["DriverDeleteOldBody"] = "آیا از حذف بسته درایور قدیمی '{0}' ({1}) اطمینان دارید؟\nاین عملیات بدون سوئیچ اجباری انجام شده و در صورت استفاده سیستم متوقف خواهد شد."
            };

            var enFallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "WinyWash",
                ["RunningAsAdmin"] = "Running as administrator",
                ["RestoreCenter"] = "Undo & Recovery Center",
                ["RefreshStatus"] = "Refresh system statistics",
                ["NotificationHistory"] = "Notification & action history",
                ["ToggleTheme"] = "Toggle theme (Dark / Light)",
                ["Settings"] = "Application Settings",
                ["NavDashboard"] = "Optimization Dashboard",
                ["NavTools"] = "Windows System Utilities",
                ["HeroCategory"] = "System Health & Performance Status",
                ["HeroTitleReady"] = "System is ready for optimization",
                ["HeroSubtitleReady"] = "Clean temporary files, free up memory, and boost performance with one click.",
                ["HeroTitleOptimized"] = "System is running at peak performance",
                ["HeroSubtitleOptimized"] = "All temporary items cleaned, memory freed, and services tuned.",
                ["CleanableSpace"] = "Cleanable Space",
                ["LastCleanup"] = "Last Cleanup",
                ["TotalFreed"] = "Total Freed",
                ["QuickBoost"] = "Quick Boost & System Optimization",
                ["QuickBoostSub"] = "Sequential clean of junk files, RAM, registry, and services",
                ["AdvancedCleanTitle"] = "Customize Cleanup & Optimization Items",
                ["AdvancedCleanSub"] = "Custom scan items and detailed junk category selection",
                ["ScanSystem"] = "Deep System Scan",
                ["SelectAll"] = "Select All",
                ["DeselectAll"] = "Deselect All",
                ["CleanSelected"] = "Clean Selected Items",
                ["RamTitle"] = "RAM",
                ["CpuTitle"] = "CPU",
                ["DiskBreakdownTitle"] = "Disk Space Breakdown (C:)",
                ["NetworkTrafficTitle"] = "Network",
                ["CleanupHistoryTitle"] = "Space Freed in Last 7 Days",
                ["ToolsHeaderTitle"] = "Windows System Utilities",
                ["ToolsHeaderSubtitle"] = "Comprehensive system management and optimization utilities",
                ["ToolsSectionFrequentlyUsed"] = "Frequently Used",
                ["ToolsSectionOtherTools"] = "Other Utilities",
                ["LauncherWindowsUpdateTitle"] = "Windows Update",
                ["LauncherWindowsUpdateDesc"] = "Check updates, 5-week pause, and stuck download repair",
                ["LauncherUninstallerTitle"] = "App Uninstaller",
                ["LauncherUninstallerDesc"] = "Manage installed apps with app icons and batch uninstall",
                ["LauncherStartupTitle"] = "Startup Manager",
                ["LauncherStartupDesc"] = "Control auto-start apps and boot performance",
                ["LauncherSpeedTestTitle"] = "Internet Speed Test",
                ["LauncherSpeedTestDesc"] = "Accurate download, upload, ping, and network diagnostics",
                ["LauncherToolDeepStorage"] = "Deep Storage Cleaner",
                ["LauncherToolSpecs"] = "System Specs",
                ["LauncherToolPersonalization"] = "Personalization & Visuals",
                ["LauncherToolDrivers"] = "Driver Manager",
                ["LauncherToolDns"] = "DNS & Anti-Sanction",
                ["LauncherToolPrivacy"] = "Privacy & Anti-Telemetry",
                ["LauncherToolRecycleBin"] = "Recycle Bin",
                ["SettingsTitle"] = "Application Settings",
                ["SettingsSub"] = "Appearance, language, background behavior, and report exports",
                ["LanguageTitle"] = "Language",
                ["LanguageSub"] = "Switches layout direction (RTL/LTR) and optimizes typography automatically.",
                ["ThemeTitle"] = "Color Theme",
                ["ThemeSub"] = "Follow Windows system theme or lock to light/dark mode.",
                ["ThemeSystem"] = "System",
                ["ThemeLight"] = "Light",
                ["ThemeDark"] = "Dark",
                ["BackgroundBehaviorTitle"] = "Background & System Tray",
                ["BackgroundBehaviorSub"] = "Control notification area icon and background state when window is closed or minimized.",
                ["CloseToTray"] = "Keep app running in background when closing window",
                ["MinimizeToTray"] = "Minimize window directly to notification area",
                ["AutoStartTitle"] = "Start with Windows",
                ["AutoStartSub"] = "Launch WinyWash automatically when logging into Windows.",
                ["DeleteBehaviorTitle"] = "Personal File Deletion Policy",
                ["DeleteBehaviorSub"] = "Applies to duplicates, large files, and downloads. System junk is always permanently deleted.",
                ["DeleteModeRecycle"] = "Move to Windows Recycle Bin (Recommended)",
                ["DeleteModeQuarantine"] = "WinyWash Quarantine — Restorable from Undo Center",
                ["DeleteModePermanent"] = "Permanent Deletion — Frees disk space immediately",
                ["AutoStateRestoreTitle"] = "Automatic State Restoration",
                ["AutoStateRestoreSub"] = "Preserve window position, scan selections, and last view across sessions.",
                ["SaveWindowPlacement"] = "Window size and position",
                ["SaveLastView"] = "Last active view",
                ["SaveScanSelections"] = "Cleanup scan checkbox selections",
                ["ResetWindowSize"] = "Reset window placement",
                ["ResetAllSettings"] = "Reset all settings",
                ["AppDataTitle"] = "Application Data",
                ["OpenSettingsFolder"] = "Open settings folder",
                ["OpenLogsFolder"] = "Open logs folder",
                ["ExportReportTitle"] = "Export Reports",
                ["ExportReportSub"] = "Generate comprehensive diagnostic files for AI prompts and detailed hardware specs.",
                ["ExportAiReport"] = "AI Diagnostic Context Report (.md)",
                ["ExportSpecsReport"] = "Hardware Specifications Report (.txt)",
                ["AboutTitle"] = "About WinyWash",
                ["AboutSub"] = "Modern PC Manager, optimization suite, and hardware diagnostics for Windows 11.",
                ["CheckUpdates"] = "Check for updates",
                ["ProjectPage"] = "GitHub Project Page",
                ["CopyDiagnostics"] = "Copy diagnostic information",
                ["BackToTools"] = "Back to Windows Tools",
                ["BackButton"] = "Back",
                ["WindowsUpdateTitle"] = "Windows Update Manager",
                ["WindowsUpdateSub"] = "Check updates, official 5-week pause, and fix stuck downloads",
                ["CheckNewUpdates"] = "Check for Updates",
                ["Pause5Weeks"] = "Official 5-Week Pause",
                ["ResumeUpdates"] = "Resume Updates",
                ["UpdateService"] = "Windows Update Service (wuauserv)",
                ["DisableService"] = "Disable",
                ["EnableService"] = "Enable",
                ["FixStuckUpdates"] = "Fix Stuck Updates & Errors",
                ["CleanCache"] = "Flush Cache",
                ["PendingUpdates"] = "Pending Updates",
                ["NoPendingUpdates"] = "No pending updates found. Windows is fully up to date.",
                ["SpeedTestTitle"] = "Internet Speed & Quality Test",
                ["SpeedTestSub"] = "Real download speed, upload, ping latency, and network details",
                ["StartSpeedTest"] = "Start Speed Test",
                ["StopSpeedTest"] = "Stop Test",
                ["PingLatency"] = "Ping Latency",
                ["DownloadSpeed"] = "Download Speed",
                ["UploadSpeed"] = "Upload Speed",
                ["DeepStorageTitle"] = "Deep Storage Cleaner & Organizer",
                ["DeepStorageSub"] = "Identify duplicate files, empty folders, downloads, and large files",
                ["StartDeepScan"] = "Start Deep Scan",
                ["TabDuplicates"] = "Duplicate Files",
                ["TabEmptyFolders"] = "Empty Folders",
                ["TabMess"] = "Temp & Junk Files",
                ["TabLargeFiles"] = "Large Files",
                ["CleanSelectedItems"] = "Clean Selected Items",
                ["UninstallerTitle"] = "App & Bloatware Uninstaller",
                ["SearchApps"] = "Search installed apps...",
                ["SortRecent"] = "Recently Installed",
                ["SortSize"] = "Largest Size",
                ["SortName"] = "Name (Alphabetical)",
                ["UninstallBatch"] = "Uninstall Selected",
                ["StartupTitle"] = "Startup Manager",
                ["StartupSub"] = "Manage startup applications and improve boot performance",
                ["RefreshList"] = "Refresh List",
                ["DriversTitle"] = "Hardware Drivers Manager",
                ["DriversSub"] = "Diagnose driver issues and safely clean duplicate DriverStore packages",
                ["CheckDriverUpdates"] = "Check Driver Updates",
                ["DnsTitle"] = "DNS & Anti-Sanction Manager",
                ["DnsSub"] = "Improve ping, bypass geo-blocks, and secure browsing",
                ["PrivacyTitle"] = "Privacy & Anti-Telemetry",
                ["PrivacySub"] = "Disable diagnostic telemetry, targeted ads, and tracking services",
                ["SpecsTitle"] = "Comprehensive Hardware Specs",
                ["SpecsSub"] = "Real-time specs for CPU, RAM, Motherboard, GPU, Disks, and Windows License",
                ["PersonalizationTitle"] = "Windows Visuals & Personalization",
                ["PersonalizationSub"] = "Instant styling without needing a system reboot",
                ["RecycleBinTitle"] = "Recycle Bin Manager",
                ["RecycleBinSub"] = "Browse deleted items, restore files, or permanently purge space",
                ["HealthScoreTitle"] = "System Health Score",
                ["HealthExcellent"] = "Excellent",
                ["HealthGood"] = "Good",
                ["HealthFair"] = "Fair",
                ["HealthNeedsOpt"] = "Needs Optimization",
                ["HealthDescExcellent"] = "System is running smoothly and in peak condition",
                ["HealthDescGood"] = "Overall system performance is good and stable",
                ["HealthDescFair"] = "System would benefit from RAM cleanup and junk removal",
                ["HealthDescPoor"] = "High resource usage; Quick Boost is recommended",
                ["RamCurrentlyUsed"] = "Currently in use",
                ["Total"] = "Total",
                ["HighUsage"] = "High Usage",
                ["MediumUsage"] = "Moderate",
                ["SmoothUsage"] = "Smooth",
                ["CpuLoadSummary"] = "Core processing load",
                ["PowerPlugged"] = "Plugged In",
                ["PowerCharging"] = "Charging",
                ["PowerBattery"] = "On Battery",
                ["NetworkTrafficTitle"] = "Network Traffic Monitor",
                ["ActiveTraffic"] = "Active",
                ["IdleTraffic"] = "Idle",
                ["NoBackgroundTraffic"] = "No background traffic (stable)",
                ["NetworkSysTraffic"] = "System & Windows Traffic",
                ["DiskBreakdownTitle"] = "Disk Space Breakdown (C:)",
                ["DiskUsed"] = "Used",
                ["DiskCleanable"] = "Cleanable",
                ["DiskFree"] = "Free",
                ["FreeOf"] = "free of",
                ["CleanupHistoryTitle"] = "Space Freed in Last 7 Days",
                ["ItemsSelected"] = "items selected",
                ["TotalDetected"] = "Total detected",
                ["Scanning"] = "Scanning...",
                ["RefreshedAfterClean"] = "Optimized after cleanup",
                ["AutoStartTitle"] = "Start with Windows",
                ["AutoStartDesc"] = "Automatically launch WinyWash when signing in to Windows (as seen in Task Manager > Startup).",
                ["DeleteBehaviorTitle"] = "Personal File Deletion Policy",
                ["DeleteBehaviorSub"] = "For duplicates, large files, and downloads tidy up. Cache and temp files are always deleted permanently to actually free space.",
                ["DeleteModeRecycle"] = "Move to Recycle Bin (Default)",
                ["DeleteModeQuarantine"] = "WinyWash Quarantine — safely kept in app folder, restorable from Restore Center",
                ["DeleteModePermanent"] = "Permanent Delete — immediately freed, cannot be undone",
                ["AutoStateRestoreTitle"] = "Automatic State Restoration",
                ["AutoStateRestoreSub"] = "Changes are saved immediately and restored on subsequent launches.",
                ["SaveWindowPlacement"] = "Window size and position",
                ["SaveLastView"] = "Last active view (Dashboard / Tools / Settings)",
                ["SaveScanSelections"] = "Cleanup scan selections",
                ["ResetWindowSize"] = "Reset Window Size",
                ["ResetAllSettings"] = "Reset All Settings",
                ["AppDataTitle"] = "Application Data",
                ["AppDataSettingsLabel"] = "Settings:",
                ["AppDataLogsLabel"] = "Error logs:",
                ["OpenSettingsFolder"] = "Open Settings Folder",
                ["OpenLogsFolder"] = "Open Logs Folder",
                ["VersionLabel"] = "Version:",
                ["OsLabel"] = "Operating System:",
                ["NotificationHistoryTitle"] = "Action & Notification History",
                ["NotificationClear"] = "Clear",
                ["NotificationEmpty"] = "No notifications recorded yet.",
                ["SpecsHeaderTitle"] = "Hardware Specifications",
                ["SpecsHeaderSubtitle"] = "Real-time CPU, RAM, Motherboard, GPU, Storage & Windows License info",
                ["CopySpecs"] = "Copy Specs",
                ["RefreshSpecs"] = "Refresh",
                ["SpecCores"] = "Cores",
                ["SpecThreads"] = "Threads",
                ["SpecSlots"] = "Slots",
                ["SpecDriver"] = "Driver",
                ["UninstallerHeaderTitle"] = "App Manager & Uninstaller",
                ["UninstallerSearchPlaceholder"] = "Search applications...",
                ["UninstallerSortNewest"] = "Date Installed",
                ["UninstallerSortLargest"] = "Size (Largest)",
                ["UninstallerSortName"] = "Name (A-Z)",
                ["SpeedTestHeaderTitle"] = "Internet Speed Test",
                ["SpeedTestHeaderSubtitle"] = "Measure download, upload bandwidth and latency via Cloudflare edge",
                ["SpeedIp"] = "IP",
                ["SpeedIsp"] = "ISP",
                ["SpeedLocation"] = "Location",
                ["SpeedDetecting"] = "Detecting...",
                ["PersonalizationHeaderTitle"] = "Windows Visuals & Personalization",
                ["PersonalizationHeaderSubtitle"] = "Instant styling and power plans without rebooting",
                ["StartupHeaderTitle"] = "Startup Apps Manager",
                ["StartupRefresh"] = "Refresh List",
                ["WindowsUpdateHeaderTitle"] = "Windows Update Manager",
                ["WindowsUpdateHeaderSubtitle"] = "Check updates, pause for 5 weeks, and fix stuck downloads",
                ["UpdateStatusHeadline"] = "Operating System Update Status",
                ["UpdateStatusActive"] = "Active",
                ["UpdateRow1Title"] = "Pause Updates for 5 Weeks",
                ["UpdateRow1Sub"] = "Pause update downloads according to official Windows policy",
                ["UpdateRow2Title"] = "Windows Update Service (wuauserv)",
                ["UpdateRow2Sub"] = "Enable or disable background Windows update service",
                ["UpdateRow3Title"] = "Fix Stuck Updates (Clear Cache)",
                ["UpdateRow3Sub"] = "Stop service, clear SoftwareDistribution cache, and restart",
                ["UpdateRow4Title"] = "Windows Update Settings",
                ["UpdateRow4Sub"] = "Open native Windows Update page in Windows Settings",
                ["UpdatePendingSectionTitle"] = "Pending Updates",
                ["NoUpdatesBanner"] = "No pending updates found. Windows is fully up to date.",
                ["DriversHeaderTitle"] = "Device Drivers & DriverStore",
                ["DriversHeaderSubtitle"] = "Detect driver issues and safely clean duplicate DriverStore packages",
                ["DriversTotalLabel"] = "Total Drivers",
                ["DriversErrorLabel"] = "Hardware Errors",
                ["DriversDuplicateLabel"] = "Duplicate Versions",
                ["DriversDuplicateBannerTitle"] = "Clean Space-Consuming Duplicate Drivers",
                ["DriversDuplicateBannerSub"] = "Older versions are safely removed with an automatic Restore Point.",
                ["DriversSearchPlaceholder"] = "Search drivers...",
                ["DriversSelectAll"] = "Select All",
                ["DriversBatchDelete"] = "Delete Selected",
                ["DnsHeaderTitle"] = "Smart DNS & Anti-Sanction",
                ["DnsHeaderSubtitle"] = "Bypass restrictions and optimize gaming and browsing latency",
                ["ResetDnsDhcp"] = "Reset to Auto (DHCP)",
                ["ActiveDnsLabel"] = "Active DNS Configuration",
                ["FlushDns"] = "Flush DNS Cache",
                ["DnsPresetsTitle"] = "Available DNS Presets",
                ["PrivacyHeaderTitle"] = "Privacy & Windows Debloater",
                ["PrivacyHeaderSubtitle"] = "Disable telemetry, tracking services, and web search",
                ["PrivacyBannerTitle"] = "Instant Real-time Protection",
                ["PrivacyBannerSub"] = "All toggles take effect immediately in the Windows registry and services.",
                ["PrivacyBannerBadge"] = "Active",
                ["PrivacyFooterHint"] = "For maximum privacy and responsiveness, enabling all options is recommended.",
                ["PrivacyResetDefaults"] = "Windows Default",
                ["PrivacyEnableAll"] = "Enable All Protections",
                ["DeepStorageHeaderTitle"] = "Deep Disk & Junk Cleaner",
                ["DeepStorageHeaderSubtitle"] = "Identify duplicate files, empty folders, bloated downloads, and large files",
                ["DeepStorageDuplicatesLabel"] = "Duplicate Files",
                ["DeepStorageEmptyFoldersLabel"] = "Empty Folders",
                ["DeepStorageEmptyFoldersSub"] = "Contains no files or subfolders",
                ["DeepStorageLargeFilesLabel"] = "Large Files",
                ["DeepStorageLargeFilesSub"] = "Larger than 500 MB",
                ["DeepStorageSortLabel"] = "Sort by:",
                ["DeepStorageSortSize"] = "Size 🔻",
                ["DeepStorageSortDate"] = "Date 📅",
                ["DeepStorageSortName"] = "Name 🔤",
                ["DeepStorageScanStatus"] = "Deep scanning file system and computing hashes...",
                ["RecycleBinHeaderTitle"] = "Recycle Bin Manager",
                ["RecycleBinHeaderSubtitle"] = "Item-by-item preview; restore or permanently purge individual objects",
                ["RecycleBinRefresh"] = "Refresh",
                ["RecycleBinTotalLabel"] = "Total Items",
                ["RecycleBinSizeLabel"] = "Space Occupied",
                ["RecycleBinRestoreSelected"] = "Restore Selected",
                ["RecycleBinDeleteSelected"] = "Delete Selected",
                ["RecycleBinEmptyBtn"] = "Empty Recycle Bin",
                ["RecycleBinEmptyHint"] = "Recycle Bin is empty; nothing to restore or delete.",
                ["RecycleBinLoading"] = "Reading Recycle Bin contents...",
                ["HeroTitleHighRam"] = "High RAM usage detected",
                ["HeroSubtitleHighRam"] = "Free memory, clean temp caches, and terminate bloatware in 1 click.",
                ["QuickBoostRunning"] = "Optimizing system...",
                ["OptimizationFeatures"] = "optimization features",
                ["JunkCleanSystem"] = "System Junk Files",
                ["JunkBrowsers"] = "Browser Caches",
                ["JunkApps"] = "App & Developer Caches",
                ["JunkWindowsCaches"] = "Windows System Caches",
                ["JunkLogs"] = "Event Logs & Crash Dumps",
                ["JunkDelivery"] = "Delivery Optimization",
                ["JunkBoost"] = "Live System Boost",

                // ── Phase 6: Deep-scan mess categories ──────────────────────────
                ["MessCatOldInstaller"] = "Old Installer (Setup / Installer)",
                ["MessCatOldArchive"] = "Archive File (Zip / Rar)",
                ["MessCatDuplicateCopy"] = "Duplicate Copy",
                ["MessCatOldScreenshot"] = "Old Screenshot",
                ["MessCatTempJunk"] = "Temp & Junk File",
                ["MessCatLargeFile"] = "Very Large File (>200 MB)",
                ["MessCatExtractedArchiveDup"] = "Extracted Archive (matching folder exists)",
                ["MessCatIncompleteDownload"] = "Incomplete Download (crdownload / part)",
                ["MessCatOemDriveRoot"] = "OEM Installer Leftover at Drive Root",
                ["MessCatUpdater"] = "Updater Leftover (Electron / Squirrel)",
                ["MessCatSyncDevice"] = "Device Transfer / OneDrive Temp Leftover",
                ["MessCatOfficeLock"] = "Office temp lock file (~$)",
                ["MessCatDevTools"] = "Developer & Container Caches (npm, Docker, pip)",
                ["EvDevToolsCache"] = "Development and container caches — deletion requires confirmation and moves safely to quarantine",
                ["StartupCatAiDev"] = "AI & Developer Tool (Optional)",
                ["OptReviewDialogTitle"] = "Confirm Optimization of Background Apps & Tools",
                ["OptReviewDialogDesc"] = "The following tools and apps were detected in startup or browser. To boost boot speed, select any items you wish to disable (they are backed up safely and can be re-enabled anytime from Startup Manager):",
                ["OptReviewDialogConfirm"] = "Disable Selected Items",
                ["OptReviewDialogCancel"] = "Skip and Continue",
                ["OptReviewExtBadge"] = "Chrome Extension",
                ["DevQuarantineTitle"] = "Safe Quarantine for Developer Packages",
                ["DevQuarantineDesc"] = "Selected items include development packages and container caches (e.g., npm, Docker, pip). They will be moved to Quarantine and can be restored easily from the Undo Center at any time.",
                ["DevQuarantineSuccessNotice"] = "(Developer items were moved to quarantine and can be restored from the Undo Center)",
                ["MessCatOther"] = "Other",

                // ── Phase 6: Detector evidence strings ──────────────────────────
                ["EvEmptyFolder30Days"] = "Empty for over 30 days (last change: {0:yyyy/MM/dd})",
                ["EvDownloadCompleted"] = "Complete file \"{0}\" exists — leftover of a finished download",
                ["EvDownloadAbandoned"] = "No changes for over 24 hours — abandoned download",
                ["EvTmpOld72h"] = "Temp .tmp file older than 72 hours in Downloads/Desktop",
                ["EvInstallerMatched"] = "Installer for \"{0}\" — the app is installed; this file is no longer needed",
                ["EvInstallerNoMatch"] = "Old installer (over 14 days) — no matching installed app was found",
                ["EvCrossDevice"] = "Device swap staging (CrossDevice)",
                ["EvCrossDeviceSub"] = "Link to Windows / Phone Link staging folder for phone-to-PC file transfers",
                ["EvOneDriveTemp"] = "OneDrive temporary folder",
                ["EvOneDriveTempSub"] = "Abandoned OneDrive staging area for in-progress uploads",
                ["EvDriveRootLeftover"] = "Drive-root folder named after vendor {0} — hard evidence: {1}",
                ["EvRemnantInstallerFile"] = "Remaining setup file \"{0}\"",
                ["EvVendorArtifact"] = "Vendor-specific artifact \"{0}\" (boot image / driver package)",
                ["EvPreloadFile"] = "Vendor preload file \"{0}\"",
                ["EvOfficeLockFile"] = "Office temp lock file — the document is closed and this file was left behind",
                ["EvRootEmptyFolder"] = "Empty folder at the drive root — no content for over 30 days (last change: {0:yyyy/MM/dd})",
                ["EvUpdaterPendingInstaller"] = "Updater folder with leftover installer \"{0}\" — unfinished update",
                ["EvUpdaterNoPending"] = "Updater folder matching *-updater / SquirrelTemp (no pending installer found)",
                ["EvBlockmapOld"] = "Leftover .blockmap from an Electron updater (untouched for over 30 days)",
                ["EvDriverNoHardware"] = "No device (not even a detached one) references this package — the hardware is no longer on this system",
                ["EvStartupFileMissing"] = "Startup file \"{0}\" does not exist on disk",
                ["EvStartupExeMissing"] = "Executable \"{0}\" is missing — leftover of an uninstalled app",
                ["EvTrialInName"] = "App name contains \"Trial\"",
                ["EvTrialInNameLocalized"] = "App name contains a trial marker",
                ["NoteProvisioned"] = " — provisioned into the Windows image; it will auto-install for new users",

                // ── Phase 6: Sensitive files (D17) ──────────────────────────────
                ["SensitiveLicense"] = "Software license file",
                ["SensitiveSerial"] = "Software serial number",
                ["SensitiveProductKey"] = "Product key",
                ["SensitiveActivation"] = "Activation code",
                ["SensitivePasswords"] = "Passwords file",
                ["SensitiveSshKey"] = "SSH private key",
                ["SensitivePrivateKey"] = "Private key",
                ["SensitiveKeyFile"] = "Key/security file ({0})",

                // ── Phase 6: Path locks & UI badges ─────────────────────────────
                ["LockUserVetoError"] = "This path is locked by the user",
                ["LockTooltipLock"] = "Lock — this path will never be suggested or deleted",
                ["LockTooltipUnlock"] = "Unlock — this path can be suggested again",
                ["LockInfoBarTitle"] = "Path lock",
                ["LockSaveFailed"] = "Saving the lock failed — check access to locks.json.",
                ["LockReasonDeepScan"] = "Locked from the Deep Scan page",
                ["AdminRequired"] = "Requires admin rights",
                ["SensitiveSectionTitle"] = "Sensitive files detected ({0} items — report only, no deletion)",
                ["BadgeTargetMissing"] = "Target missing",
                ["BadgeTrial"] = "Trial",
                ["BadgeComponent"] = "Component / Driver",
                ["EvSystemComponent"] = "System component entry (SystemComponent) — usually a driver or hardware utility; remove only with full awareness",
                ["JunkPrefetchTitle"] = "Windows Prefetch cache (.pf files)",
                ["JunkPrefetchDesc"] = "Windows app-launch speed cache — rebuilds automatically; deleting it is safe",
                ["BadgeNoHardware"] = "No hardware",
                ["ScanProgressDriveRoots"] = "Scanning drive roots and updater leftovers...",
                ["DescCrashLogs"] = "Crash logs, WER reports, memory dumps, and kernel reports (LiveKernelReports)",

                // ── Phase 6: Drivers (banner + delete dialog) ───────────────────
                ["DriverBannerDupAndOrphan"] = "Found {0} duplicate/outdated + {1} no-hardware driver packages in the Windows DriverStore",
                ["DriverBannerOrphan"] = "Found {0} no-hardware driver packages (leftovers of detached devices) in the Windows DriverStore",
                ["DriverBannerDup"] = "Found {0} duplicate/outdated driver packages in the Windows DriverStore",
                ["DriverDeleteOrphanTitle"] = "Remove no-hardware package {0}",
                ["DriverDeleteOldTitle"] = "Remove old driver {0}",
                ["DriverDeleteOrphanBody"] = "Package '{0}' belongs to hardware that is no longer connected to this system.\nDelete this package?\nThe operation runs without a force switch and will stop if the package is in use.",
                ["DriverDeleteOldBody"] = "Delete the old driver package '{0}' ({1})?\nThe operation runs without a force switch and will stop if the package is in use."
            };

            _catalogs["fa-IR"] = faFallback;
            _catalogs["en-US"] = enFallback;
        }

        public static void LoadCatalogs(string? customDirectory = null)
        {
            try
            {
                var candidateDirs = new List<string>();
                if (!string.IsNullOrWhiteSpace(customDirectory) && Directory.Exists(customDirectory))
                {
                    candidateDirs.Add(customDirectory);
                }

                string baseDir = AppContext.BaseDirectory;
                candidateDirs.Add(Path.Combine(baseDir, "Resources", "Strings"));
                candidateDirs.Add(Path.Combine(baseDir, "Strings"));

                // Fallback for development/source tree
                try
                {
                    var parent = Directory.GetParent(baseDir);
                    while (parent != null && parent.Exists)
                    {
                        string test = Path.Combine(parent.FullName, "src", "CleanC.UI", "Resources", "Strings");
                        if (Directory.Exists(test))
                        {
                            candidateDirs.Add(test);
                            break;
                        }
                        parent = parent.Parent;
                    }
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex, "LocalizationService.SourcePathProbe");
                }

                foreach (var dir in candidateDirs.Where(Directory.Exists))
                {
                    foreach (var file in Directory.GetFiles(dir, "*.resw", SearchOption.AllDirectories))
                    {
                        ParseReswFile(file);
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "LocalizationService.LoadCatalogs");
            }
        }

        private static void ParseReswFile(string filePath)
        {
            try
            {
                string filename = Path.GetFileNameWithoutExtension(filePath); // Strings.en-US or Resources
                string langCode;
                if (filename.StartsWith("Strings.", StringComparison.OrdinalIgnoreCase))
                {
                    langCode = filename.Substring("Strings.".Length);
                    if (langCode.StartsWith("lang-", StringComparison.OrdinalIgnoreCase))
                        langCode = langCode.Substring("lang-".Length);
                }
                else
                {
                    langCode = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "";
                }

                if (string.IsNullOrWhiteSpace(langCode)) return;

                var doc = XDocument.Load(filePath);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var data in doc.Descendants("data"))
                {
                    string? name = data.Attribute("name")?.Value;
                    string? value = data.Element("value")?.Value;
                    if (!string.IsNullOrEmpty(name) && value != null)
                    {
                        dict[name] = value;
                    }
                }

                if (!_catalogs.TryGetValue(langCode, out var existing))
                {
                    _catalogs[langCode] = dict;
                }
                else
                {
                    foreach (var kvp in dict)
                    {
                        existing[kvp.Key] = kvp.Value;
                    }
                }

                // If not in metadata list, register dynamically
                if (!_languages.Any(l => string.Equals(l.Code, langCode, StringComparison.OrdinalIgnoreCase)))
                {
                    bool rtl = langCode.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ||
                               langCode.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ||
                               langCode.StartsWith("he", StringComparison.OrdinalIgnoreCase) ||
                               langCode.StartsWith("ur", StringComparison.OrdinalIgnoreCase);

                    _languages.Add(new LanguageInfo
                    {
                        Code = langCode,
                        NativeName = langCode,
                        FlagName = $"🌐 {langCode}",
                        IsRtl = rtl
                    });
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, $"LocalizationService.ParseResw:{filePath}");
            }
        }

        public static string DetectDefaultLanguage()
        {
            try
            {
                // 1. Timezone detection (High confidence for local user)
                string tzId = TimeZoneInfo.Local.Id;
                if (tzId.Contains("Iran", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Tehran", StringComparison.OrdinalIgnoreCase))
                {
                    return "fa-IR";
                }
                if (tzId.Contains("Arab", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Riyadh", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Baghdad", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Cairo", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Kuwait", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Jordan", StringComparison.OrdinalIgnoreCase))
                {
                    return "ar-SA";
                }
                if (tzId.Contains("China", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Beijing", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Shanghai", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Taipei", StringComparison.OrdinalIgnoreCase))
                {
                    return "zh-CN";
                }
                if (tzId.Contains("Tokyo", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Japan", StringComparison.OrdinalIgnoreCase))
                {
                    return "ja-JP";
                }
                if (tzId.Contains("Seoul", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Korea", StringComparison.OrdinalIgnoreCase))
                {
                    return "ko-KR";
                }
                if (tzId.Contains("Russian", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Moscow", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Ekaterinburg", StringComparison.OrdinalIgnoreCase))
                {
                    return "ru-RU";
                }
                if (tzId.Contains("Berlin", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("W. Europe", StringComparison.OrdinalIgnoreCase))
                {
                    return "de-DE";
                }
                if (tzId.Contains("Paris", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Romance", StringComparison.OrdinalIgnoreCase))
                {
                    return "fr-FR";
                }
                if (tzId.Contains("Madrid", StringComparison.OrdinalIgnoreCase))
                {
                    return "es-ES";
                }

                // 2. Region / Geo detection
                try
                {
                    string region = RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant();
                    switch (region)
                    {
                        case "IR": return "fa-IR";
                        case "SA":
                        case "AE":
                        case "EG":
                        case "IQ":
                        case "KW":
                        case "QA":
                        case "OM": return "ar-SA";
                        case "CN": return "zh-CN";
                        case "JP": return "ja-JP";
                        case "KR": return "ko-KR";
                        case "RU":
                        case "BY":
                        case "KZ": return "ru-RU";
                        case "DE":
                        case "AT":
                        case "CH": return "de-DE";
                        case "FR": return "fr-FR";
                        case "ES":
                        case "MX":
                        case "AR":
                        case "CO": return "es-ES";
                    }
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex, "LocalizationService.RegionProbe");
                }

                // 3. UI Culture detection
                string cultureName = CultureInfo.CurrentUICulture.Name;
                foreach (var lang in _languages)
                {
                    if (string.Equals(lang.Code, cultureName, StringComparison.OrdinalIgnoreCase) ||
                        cultureName.StartsWith(lang.Code.Split('-')[0], StringComparison.OrdinalIgnoreCase))
                    {
                        return lang.Code;
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "LocalizationService.DetectDefaultLanguage");
            }

            return "en-US";
        }

        public static void SetLanguage(string langCode)
        {
            if (string.IsNullOrWhiteSpace(langCode) || string.Equals(langCode, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                langCode = DetectDefaultLanguage();
            }

            var target = _languages.FirstOrDefault(l => string.Equals(l.Code, langCode, StringComparison.OrdinalIgnoreCase))
                         ?? _languages.FirstOrDefault(l => l.Code.StartsWith(langCode.Split('-')[0], StringComparison.OrdinalIgnoreCase))
                         ?? _languages.First();

            _current = target;

            try
            {
                var culture = (CultureInfo)CultureInfo.GetCultureInfo(target.Code).Clone();
                culture.NumberFormat.NumberDecimalSeparator = ".";
                culture.NumberFormat.PercentDecimalSeparator = ".";
                culture.NumberFormat.CurrencyDecimalSeparator = ".";
                culture.DateTimeFormat.DateSeparator = ".";
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
            }
            catch { }

            LanguageChanged?.Invoke();
        }

        public static string Get(string key, string? defaultVal = null)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;

            // Try current language
            if (_catalogs.TryGetValue(_current.Code, out var dict) && dict.TryGetValue(key, out var val))
            {
                return WithBrand(val);
            }

            // Fallback to English
            if (!string.Equals(_current.Code, "en-US", StringComparison.OrdinalIgnoreCase) &&
                _catalogs.TryGetValue("en-US", out var enDict) && enDict.TryGetValue(key, out var enVal))
            {
                // الگوی gettext: وقتی خودِ key متن فارسی است (Get("متن فارسی")), برای کاربر فارسی
                // همان متن منبع ترجمه ی درست است و نباید به انگلیسی fallback کند.
                if (string.Equals(_current.Code, "fa-IR", StringComparison.OrdinalIgnoreCase) && ContainsPersian(key))
                {
                    return WithBrand(key);
                }

                return WithBrand(enVal);
            }

            // Fallback to Persian
            if (!string.Equals(_current.Code, "fa-IR", StringComparison.OrdinalIgnoreCase) &&
                _catalogs.TryGetValue("fa-IR", out var faDict) && faDict.TryGetValue(key, out var faVal))
            {
                return WithBrand(faVal);
            }

            return WithBrand(defaultVal ?? key);
        }

        /// <summary>
        /// نام برند در همه ی رشته های UI یکسان می ماند؛ کلیدها و ترجمه های قدیمی
        /// هنوز CleanC دارند و این لایه بدون دست زدن به ۱۰ فایل resw آن ها را هماهنگ می کند.
        /// </summary>
        private static string WithBrand(string value) =>
            value.Replace("CleanC", "WinyWash", StringComparison.Ordinal);
        /// <summary>
        /// بررسی اینکه رشته حاوی حداقل یک کاراکتر فارسی/عربی است — برای تشخیص کلیدهای gettext-style
        /// (کلیدهایی که خودشان متن منبع فارسی هستند).
        /// </summary>
        public static bool ContainsPersian(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                if (c >= '\u0600' && c <= '\u06FF') return true;
            }
            return false;
        }

        public static string GetFontFamilyName()
        {
            return _current.Code switch
            {
                "zh-CN" => "Microsoft YaHei, Segoe UI",
                "ja-JP" => "Yu Gothic UI, Meiryo, Segoe UI",
                "ko-KR" => "Malgun Gothic, Segoe UI",
                "ar-SA" => "Segoe UI, Tahoma",
                "fa-IR" => "Segoe UI, Tahoma",
                _ => "Segoe UI Variable, Segoe UI"
            };
        }
    }
}
