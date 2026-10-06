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
                ["AppTitle"] = "CleanC",
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
                ["RamTitle"] = "مصرف رم (RAM)",
                ["CpuTitle"] = "پردازنده (CPU)",
                ["DiskBreakdownTitle"] = "فضای دیسک و درایوها (Storage)",
                ["NetworkTitle"] = "سرعت اینترنت",
                ["CleanupHistoryTitle"] = "فضای آزادشده در ۷ روز اخیر",
                ["ToolsHeaderTitle"] = "ابزارهای سیستمی ویندوز",
                ["ToolsHeaderSubtitle"] = "پرکاربردترین ابزارها همین جا، بقیه یک کلیک آن طرف تر",
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
                ["AutoStartSub"] = "با ورود به حساب کاربری، CleanC به صورت خودکار اجرا شود.",
                ["DeleteBehaviorTitle"] = "رفتار حذف فایل های شخصی",
                ["DeleteBehaviorSub"] = "برای حذف تکراری ها، فایل های حجیم و دانلودها. فایل های زائد سیستم همیشه قطعی پاک می شوند.",
                ["DeleteModeRecycle"] = "انتقال به سطل بازیافت ویندوز (پیشنهادی)",
                ["DeleteModeQuarantine"] = "قرنطینه CleanC — قابل بازیابی از مرکز بازیابی",
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
                ["AboutTitle"] = "درباره CleanC",
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
                ["RecycleBinSub"] = "مشاهده اشیاء داخل سطل، بازیابی و آزادسازی قطعی فضا"
            };

            var enFallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "CleanC",
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
                ["RamTitle"] = "Memory (RAM)",
                ["CpuTitle"] = "Processor (CPU)",
                ["DiskBreakdownTitle"] = "Disk Storage Breakdown",
                ["NetworkTitle"] = "Internet Speed & Traffic",
                ["CleanupHistoryTitle"] = "Space Freed in Last 7 Days",
                ["ToolsHeaderTitle"] = "Windows System Utilities",
                ["ToolsHeaderSubtitle"] = "Most frequently used utilities at your fingertips",
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
                ["AutoStartSub"] = "Launch CleanC automatically when logging into Windows.",
                ["DeleteBehaviorTitle"] = "Personal File Deletion Policy",
                ["DeleteBehaviorSub"] = "Applies to duplicates, large files, and downloads. System junk is always permanently deleted.",
                ["DeleteModeRecycle"] = "Move to Windows Recycle Bin (Recommended)",
                ["DeleteModeQuarantine"] = "CleanC Quarantine — Restorable from Undo Center",
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
                ["AboutTitle"] = "About CleanC",
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
                ["RecycleBinSub"] = "Browse deleted items, restore files, or permanently purge space"
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

                _catalogs[langCode] = dict;

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
                return val;
            }

            // Fallback to English
            if (!string.Equals(_current.Code, "en-US", StringComparison.OrdinalIgnoreCase) &&
                _catalogs.TryGetValue("en-US", out var enDict) && enDict.TryGetValue(key, out var enVal))
            {
                return enVal;
            }

            // Fallback to Persian
            if (!string.Equals(_current.Code, "fa-IR", StringComparison.OrdinalIgnoreCase) &&
                _catalogs.TryGetValue("fa-IR", out var faDict) && faDict.TryGetValue(key, out var faVal))
            {
                return faVal;
            }

            return defaultVal ?? key;
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
