using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public enum AppCategoryType
    {
        Bloatware,
        OemTrial,
        BorderlineOptional,
        LargeUnused
    }

    public class CandidateAppItem
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string PackageFullName { get; set; } = string.Empty;
        public string UninstallString { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        // صادقانه بودن آمار: اندازه تخمینی (هاردکد) هرگز نباید به «آزاد شده واقعی» اضافه شود
        public bool SizeIsEstimate { get; set; }
        public string FormattedSize => SystemStats.FormatBytes(SizeBytes);
        public AppCategoryType CategoryType { get; set; }
        public string CategoryLabel { get; set; } = "Bloatware";
        public string Description { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = true;
        public bool IsPackagedApp { get; set; } = true;

        // D13 — بسته ی provisioned تصویر ویندوز: برای هر کاربر جدید دوباره نصب می شود.
        // هنگام حذف، علاوه بر RemovePackageAsync باید Deprovision هم بشود وگرنه برمی گردد.
        public bool IsProvisioned { get; set; }
    }

    public class BloatwareService
    {
        // Protected system apps that must NEVER be removed
        private static readonly HashSet<string> EssentialSystemPackages = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.WindowsStore",
            "Microsoft.SecHealthUI",
            "Microsoft.MicrosoftEdge",
            "Microsoft.Windows.Photos",
            "Microsoft.WindowsCalculator",
            "Microsoft.WindowsTerminal",
            "Microsoft.WindowsNotepad",
            "Microsoft.WindowsCamera",
            "Microsoft.DesktopAppInstaller",
            "Microsoft.ScreenSketch",
            "Microsoft.WindowsAlarms",
            "Microsoft.WindowsSoundRecorder",
            "Microsoft.WindowsMaps",
            "Microsoft.StorePurchaseApp",
            "Microsoft.ZuneVideo",
            "Microsoft.ZuneMusic"
        };

        // Vendor / Hardware / System signatures that must NEVER be flagged as bloatware
        private static readonly HashSet<string> ProtectedVendors = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft Visual C++", "Windows", "CleanC", "NVIDIA", "AMD", "Intel", "Realtek",
            "Synaptics", "DragonCenter", "Dragon Center", "MSI Center", "Nahimic", "ELAN",
            "Touchpad", "Audio", "Driver", "DirectX", "DotNet", ".NET"
        };

        // Borderline apps that should NEVER be deleted automatically without user prompt
        private static readonly Dictionary<string, (string Name, string Desc)> BorderlineSignatures = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft.Paint", ("Microsoft Paint", "نرم افزار نقاشی پیش فرض ویندوز") },
            { "Microsoft.XboxApp", ("Xbox App", "برنامه اصلی ایکس باکس و بازی های ویندوز") },
            { "Microsoft.XboxGamingOverlay", ("Xbox Game Bar", "نوار ابزار و ضبط ویدیوی بازی") },
            { "Microsoft.XboxGameOverlay", ("Xbox Game Overlay", "پوشش درون بازی ایکس باکس") },
            { "Microsoft.XboxIdentityProvider", ("Xbox Identity Provider", "سرویس ورود به اکانت مایکروسافت در بازی ها") },
            { "Microsoft.XboxSpeechToTextOverlay", ("Xbox Speech To Text", "ابزار تبدیل گفتار به نوشتار ایکس باکس") },
            { "Microsoft.YourPhone", ("Phone Link (پیوند با گوشی)", "برنامه اتصال و همگام سازی گوشی با ویندوز") },
            { "Microsoft.PowerAutomateDesktop", ("Power Automate Desktop", "ابزار اتوماسیون کارهای تکراری مایکروسافت (RPA) — اگر استفاده نمی کنید لازم نیست") }
        };

        // کلاسیک هایی که تقریباً همیشه بدون استفاده اند (اکوسیستم قدیمی Apple، VPN بی استفاده، JDK منسوخ).
        // فقط در «بازبینی دستی» نمایش داده می شوند — هرگز تیک پیش فرض نمی خورند.
        private static readonly Dictionary<string, (string Name, string Desc)> KnownUnusedSignatures = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Apple Mobile Device Support", ("Apple Mobile Device Support", "برای همگام سازی آیفون با iTunes قدیمی — اگر آیفون وصل نمی کنید لازم نیست") },
            { "Bonjour", ("Bonjour", "سرویس کشف شبکه ی اپل — فقط برای iTunes و چاپگرهای اپل لازم است") },
            { "X-VPN", ("X-VPN", "کلاینت VPN بدون استفاده") },
            { "Java SE Development Kit 8", ("Java SE Development Kit 8", "نسخه ی منسوخ جاوا (JDK 8) — اگر پروژه ی قدیمی ندارید لازم نیست") },
            { "PC Health Check", ("Windows PC Health Check", "ابزار منسوخ بررسی سلامت ویندوز — مایکروسافت بازنشسته اش کرده؛ جایگزین: PC Manager") },
            { "Universal Adb Driver", ("Universal Adb Driver", "درایور ADB قدیمی و عمومی — برای گوشی های جدید، درایور رسمی گوگل یا سازنده بهتر است") }
        };

        // Known promotional pre-installed packages
        private static readonly Dictionary<string, (string Name, string Desc)> PromoBloatwareSignatures = new(StringComparer.OrdinalIgnoreCase)
        {
            { "king.com.CandyCrushSaga", ("Candy Crush Saga", "بازی تبلیغاتی پیش فرض ویندوز") },
            { "king.com.CandyCrushSodaSaga", ("Candy Crush Soda", "بازی تبلیغاتی پیش فرض ویندوز") },
            { "ByteDance.TikTok", ("TikTok", "برنامه تبلیغاتی تیک تاک") },
            { "Disney", ("Disney+", "برنامه اشتراک دیزنی پلاس") },
            { "Facebook", ("Facebook", "برنامه فیس بوک") },
            { "Instagram", ("Instagram", "برنامه اینستاگرام") },
            { "AmazonVideo.PrimeVideo", ("Amazon Prime Video", "برنامه تبلیغاتی آمازون پرایم") },
            { "Netflix", ("Netflix", "برنامه پخش فیلم نتفلیکس") },
            { "SpotifyAB.SpotifyMusic", ("Spotify (پیش فرض)", "نسخه استور از پیش نصب شده اسپاتیفای") },
            { "A949DE3C.MarchofEmpires", ("March of Empires", "بازی تبلیغاتی پیش فرض") },
            { "A949DE3C.HiddenCityMysteryofShadows", ("Hidden City", "بازی تبلیغاتی پیش فرض") },
            { "flaregamesGmbH.RoyalRevolt2", ("Royal Revolt 2", "بازی تبلیغاتی پیش فرض") },
            { "9E2F88E3.TWITTER", ("Twitter (منسوخ)", "اپ قدیمی توییتر از پیش نصب شده — سرویس آن سال ها پیش تعطیل شده است") }
        };

        // Non-essential Windows components that can be safely removed
        private static readonly Dictionary<string, (string Name, string Desc)> WindowsBloatwareSignatures = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft.549981C3F5F10", ("Cortana (کورتانا)", "دستیار صوتی قدیمی و غیرفعال مایکروسافت") },
            { "Microsoft.BingNews", ("Microsoft News (اخبار)", "فید اخبار و مقالات مایکروسافت") },
            { "Microsoft.BingWeather", ("Weather (آب و هوا)", "برنامه هواشناسی پیش فرض ویندوز") },
            { "Microsoft.Getstarted", ("Tips (نکات ویندوز)", "برنامه راهنما و پیشنهادات ویندوز") },
            { "Microsoft.GetHelp", ("Get Help (راهنمایی)", "ابزار پشتیبانی و راهنمایی مایکروسافت") },
            { "Clipchamp.Clipchamp", ("Clipchamp (ویرایشگر ویدیو)", "ویرایشگر ویدیوی پیش فرض مایکروسافت") },
            { "Microsoft.MicrosoftSolitaireCollection", ("Solitaire Collection", "بازی های ورق پیش فرض ویندوز") },
            { "Microsoft.WindowsFeedbackHub", ("Feedback Hub", "مرکز ارسال بازخورد و باگ به مایکروسافت") },
            { "Microsoft.MixedReality.Portal", ("Mixed Reality Portal", "پورتال واقعیت ترکیبی ویندوز") },
            { "Microsoft.Microsoft3DViewer", ("3D Viewer", "نمایشگر فایل های سه بعدی ویندوز") },
            { "Microsoft.People", ("People (مخاطبین)", "برنامه مخاطبین قدیمی ویندوز") },
            { "Microsoft.BingSports", ("MSN Sports (ورزش)", "برنامه اخبار ورزشی مایکروسافت") },
            { "Microsoft.BingFinance", ("MSN Money (اقتصاد)", "برنامه اخبار مالی مایکروسافت") }
        };

        // OEM Trials signatures
        private static readonly string[] OemBloatSignatures = new[]
        {
            "McAfee", "Norton", "WildTangent", "Dell SupportAssist",
            "HP Support Assistant", "HP JumpStart", "Lenovo Now"
        };

        public static async Task<List<CandidateAppItem>> ScanCandidateAppsAsync()
        {
            return await Task.Run(() =>
            {
                var candidates = new List<CandidateAppItem>();

                // 1. Scan Packaged AppX / Store Bloatware
                try
                {
                    ScanPackagedBloatware(candidates);
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 2. Scan OEM Bloatware and Unused Win32 Apps
                try
                {
                    ScanWin32Apps(candidates);
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                return candidates;
            });
        }

        private static void ScanPackagedBloatware(List<CandidateAppItem> result)
        {
            using var packagesKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if (packagesKey == null) return;

            // D13 — لیست provisioned فقط اگر واقعاً کاندیدی پیدا شد لود می شود (lazy)
            HashSet<string>? provisionedNames = null;

            foreach (var packageFullName in packagesKey.GetSubKeyNames())
            {
                // Format: Name_Version_Arch_ResourceId_PublisherId
                string packageName = packageFullName.Split('_')[0];

                if (IsEssentialPackage(packageName, packageFullName))
                    continue;

                // Check Promo Bloatware
                foreach (var kvp in PromoBloatwareSignatures)
                {
                    if (packageName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        long size = EstimatePackageSize(packageFullName, out bool sizeIsEstimate);
                        result.Add(new CandidateAppItem
                        {
                            Id = packageFullName,
                            DisplayName = kvp.Value.Name,
                            PackageFullName = packageFullName,
                            SizeBytes = size,
                            SizeIsEstimate = sizeIsEstimate,
                            CategoryType = AppCategoryType.Bloatware,
                            CategoryLabel = "تبلیغاتی / پیش فرض",
                            Description = kvp.Value.Desc,
                            IsPackagedApp = true,
                            IsSelected = true
                        });
                        break;
                    }
                }

                // Check Windows Non-Essential Bloatware
                foreach (var kvp in WindowsBloatwareSignatures)
                {
                    if (packageName.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        long size = EstimatePackageSize(packageFullName, out bool sizeIsEstimate);
                        result.Add(new CandidateAppItem
                        {
                            Id = packageFullName,
                            DisplayName = kvp.Value.Name,
                            PackageFullName = packageFullName,
                            SizeBytes = size,
                            SizeIsEstimate = sizeIsEstimate,
                            CategoryType = AppCategoryType.Bloatware,
                            CategoryLabel = "ویندوز (غیرضروری)",
                            Description = kvp.Value.Desc,
                            IsPackagedApp = true,
                            IsSelected = true
                        });
                        break;
                    }
                }
            }

            // D13 — پرچم گذاری provisioned روی همه ی کاندیدهای پیدا شده (یک بار محاسبه)
            if (result.Any(c => c.IsPackagedApp))
            {
                provisionedNames = GetProvisionedPackageNames();
                if (provisionedNames.Count > 0)
                {
                    foreach (var item in result.Where(c => c.IsPackagedApp))
                    {
                        string name = item.PackageFullName.Split('_')[0];
                        if (!provisionedNames.Contains(name)) continue;
                        item.IsProvisioned = true;
                        item.Description = item.Description +
                            LocalizationService.Get("NoteProvisioned", " — به صورت provisioned در تصویر ویندوز ثبت است و برای کاربران جدید خودکار نصب می شود");
                    }
                }
            }
        }

        // D13 — نام پکیج های provisioned (تصویر ویندوز) از PackageManager
        private static HashSet<string> GetProvisionedPackageNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var packageManager = new Windows.Management.Deployment.PackageManager();
                foreach (var pkg in packageManager.FindProvisionedPackages())
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(pkg.Id.Name)) names.Add(pkg.Id.Name);
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex, "Bloatware.Provisioned");
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex, "Bloatware.Provisioned");
            }
            return names;
        }

        private static void ScanWin32Apps(List<CandidateAppItem> result)
        {
            var userAssistMap = GetUserAssistLastRunMap();

            string[] keys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var keyPath in keys)
            {
                ScanUninstallRegistryKey(Registry.LocalMachine, keyPath, userAssistMap, result);
                ScanUninstallRegistryKey(Registry.CurrentUser, keyPath, userAssistMap, result);
            }
        }

        private static void ScanUninstallRegistryKey(RegistryKey hive, string keyPath, Dictionary<string, DateTime> userAssistMap, List<CandidateAppItem> result)
        {
            try
            {
                using var baseKey = hive.OpenSubKey(keyPath);
                if (baseKey == null) return;

                foreach (var subName in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var appKey = baseKey.OpenSubKey(subName);
                        if (appKey == null) continue;

                        string? displayName = appKey.GetValue("DisplayName")?.ToString();
                        if (string.IsNullOrWhiteSpace(displayName)) continue;

                        // Check if system / driver / vendor protected
                        if (ProtectedVendors.Any(v => displayName.Contains(v, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        string? uninstallString = appKey.GetValue("UninstallString")?.ToString();
                        string? installLocation = appKey.GetValue("InstallLocation")?.ToString();

                        // 1. Check if OEM Trial bloatware
                        if (OemBloatSignatures.Any(sig => displayName.Contains(sig, StringComparison.OrdinalIgnoreCase)))
                        {
                            result.Add(new CandidateAppItem
                            {
                                Id = subName,
                                DisplayName = displayName,
                                UninstallString = uninstallString ?? string.Empty,
                                SizeBytes = 250 * 1024 * 1024,
                                SizeIsEstimate = true,
                                CategoryType = AppCategoryType.OemTrial,
                                CategoryLabel = "نرم افزار لپ تاپ / تریال",
                                Description = "نرم افزار تبلیغاتی یا آزمایشی سازنده سیستم",
                                IsPackagedApp = false,
                                IsSelected = true
                            });
                            continue;
                        }

                        // 2. Check if Large Unused Application (> 400 MB & not run in > 60 days)
                        long sizeBytes = 0;
                        if (appKey.GetValue("EstimatedSize") is int sizeKb)
                        {
                            sizeBytes = (long)sizeKb * 1024;
                        }
                        else if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                        {
                            sizeBytes = GetDirectorySizeFast(installLocation);
                        }

                        if (sizeBytes > 500 * 1024 * 1024) // > 500 MB
                        {
                            bool isUnused = false;
                            string desc = "برنامه حجیم";

                            // Match executables in userAssistMap
                            var matchingRuns = userAssistMap.Where(kvp => displayName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                                                                         kvp.Key.Contains(displayName, StringComparison.OrdinalIgnoreCase)).ToList();

                            if (matchingRuns.Count > 0)
                            {
                                var lastRun = matchingRuns.Max(r => r.Value);
                                if (lastRun < DateTime.Now.AddDays(-30))
                                {
                                    isUnused = true;
                                    desc = $"بیش از ۳۰ روز است اجرا نشده (آخرین اجرا: {lastRun:yyyy.MM.dd})";
                                }
                            }
                            else
                            {
                                // Check InstallDate
                                string? installDate = appKey.GetValue("InstallDate")?.ToString();
                                if (!string.IsNullOrEmpty(installDate) && installDate.Length == 8)
                                {
                                    if (DateTime.TryParseExact(installDate, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var iDate))
                                    {
                                        if (iDate < DateTime.Now.AddDays(-30))
                                        {
                                            isUnused = true;
                                            desc = $"نصب شده در تاریخ {iDate:yyyy.MM.dd} ولی ثبت استفاده ای از آن نشده است";
                                        }
                                    }
                                }
                            }

                            if (isUnused && !result.Any(r => r.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase)))
                            {
                                result.Add(new CandidateAppItem
                                {
                                    Id = subName,
                                    DisplayName = displayName,
                                    UninstallString = uninstallString ?? string.Empty,
                                    SizeBytes = sizeBytes,
                                    CategoryType = AppCategoryType.LargeUnused,
                                    CategoryLabel = "حجیم و کم استفاده",
                                    Description = desc,
                                    IsPackagedApp = false,
                                    IsSelected = false // Unused apps are unchecked by default for extra safety!
                                });
                            }
                        }
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        public static async Task<bool> RemoveAppAsync(CandidateAppItem app)
        {
            if (app.IsPackagedApp && !string.IsNullOrEmpty(app.PackageFullName))
            {
                return await RemoveAppxPackageAsync(app.PackageFullName, app.IsProvisioned);
            }
            if (!string.IsNullOrEmpty(app.UninstallString))
            {
                return await Task.Run(() => LaunchWin32Uninstaller(app.UninstallString));
            }
            return false;
        }

        public static async Task<List<CandidateAppItem>> ScanBorderlineReviewAppsAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<CandidateAppItem>();

                // 1. Packaged apps (Paint, Xbox suite, Phone Link)
                try
                {
                    using var packagesKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
                    if (packagesKey != null)
                    {
                        foreach (var packageFullName in packagesKey.GetSubKeyNames())
                        {
                            string packageName = packageFullName.Split('_')[0];
                            foreach (var kvp in BorderlineSignatures)
                            {
                                if (packageName.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                                    packageFullName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!list.Any(x => x.DisplayName.Equals(kvp.Value.Name, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        long size = EstimatePackageSize(packageFullName, out bool sizeIsEstimate);
                                        list.Add(new CandidateAppItem
                                        {
                                            Id = packageFullName,
                                            DisplayName = kvp.Value.Name,
                                            PackageFullName = packageFullName,
                                            SizeBytes = size,
                                            SizeIsEstimate = sizeIsEstimate,
                                            CategoryType = AppCategoryType.BorderlineOptional,
                                            CategoryLabel = "اختیاری / در صورت عدم نیاز",
                                            Description = kvp.Value.Desc,
                                            IsPackagedApp = true,
                                            IsSelected = false // Unchecked by default for safety
                                        });
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex);
                }

                // 2. Large unused Win32 apps (> 500 MB, unused > 30 days)
                try
                {
                    var allCandidates = ScanCandidateAppsAsync().GetAwaiter().GetResult();
                    var largeUnused = allCandidates.Where(c => c.CategoryType == AppCategoryType.LargeUnused).ToList();
                    foreach (var item in largeUnused)
                    {
                        if (!list.Any(x => x.DisplayName.Equals(item.DisplayName, StringComparison.OrdinalIgnoreCase)))
                        {
                            item.IsSelected = false; // Unchecked by default
                            list.Add(item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex);
                }

                // 3. کلاسیک های بدون استفاده (Apple Mobile Device/Bonjour/X-VPN/JDK 8) — بدون تیک پیش فرض
                try
                {
                    var roots = new List<(RegistryKey Root, string SubKeyPath)>
                    {
                        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                        (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
                    };

                    foreach (var (root, subPath) in roots)
                    {
                        using var uninstallKey = root.OpenSubKey(subPath, false);
                        if (uninstallKey == null) continue;

                        foreach (var sub in uninstallKey.GetSubKeyNames())
                        {
                            using var appKey = uninstallKey.OpenSubKey(sub, false);
                            if (appKey == null) continue;

                            string displayName = appKey.GetValue("DisplayName")?.ToString() ?? string.Empty;
                            if (string.IsNullOrEmpty(displayName)) continue;

                            foreach (var kvp in KnownUnusedSignatures)
                            {
                                if (displayName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase)
                                    && !list.Any(x => x.DisplayName.Equals(kvp.Value.Name, StringComparison.OrdinalIgnoreCase)))
                                {
                                    long sizeBytes = appKey.GetValue("EstimatedSize") is int sizeKb ? (long)sizeKb * 1024 : 0;
                                    list.Add(new CandidateAppItem
                                    {
                                        Id = sub,
                                        DisplayName = kvp.Value.Name,
                                        UninstallString = appKey.GetValue("UninstallString")?.ToString() ?? string.Empty,
                                        SizeBytes = sizeBytes,
                                        SizeIsEstimate = false,
                                        CategoryType = AppCategoryType.BorderlineOptional,
                                        CategoryLabel = "اختیاری / در صورت عدم نیاز",
                                        Description = kvp.Value.Desc,
                                        IsPackagedApp = false,
                                        IsSelected = false // فقط بازبینی دستی
                                    });
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex);
                }

                return list;
            });
        }

        public static async Task<(int RemovedCount, long FreedBytes)> CleanAutomaticBloatwareAsync()
        {
            var candidates = await ScanCandidateAppsAsync();
            var autoTargets = candidates
                .Where(c => c.CategoryType == AppCategoryType.Bloatware || c.CategoryType == AppCategoryType.OemTrial)
                .ToList();

            int removed = 0;
            long freed = 0;
            foreach (var app in autoTargets)
            {
                try
                {
                    if (await RemoveAppAsync(app))
                    {
                        removed++;
                        // اندازه تخمینی را به «آزادسازی واقعی» نمی افزاییم
                        if (!app.SizeIsEstimate) freed += app.SizeBytes;
                    }
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex);
                }
            }
            return (removed, freed);
        }

        /// <summary>
        /// حذف پکیج استور از طریق PackageManager ویندوز (جایگزین PowerShell + Remove-AppxPackage
        /// که برای آنتی ویروس ها الگوی مشکوکی بود و نیاز به escape دستی کوتیشن داشت).
        /// خطای واقعی حذف در ExtendedErrorCode نتیجه برمی گردد؛ حذف سنگین ممکن است
        /// چند ده ثانیه طول بکشد و این متد تا پایان آن await می ماند.
        ///
        /// D13 — اگر پکیج provisioned باشد، بعد از حذف Deprovision هم می شود تا برای
        /// کاربران جدید ویندوز دوباره نصب نشود (best-effort؛ شکست آن حذف را باطل نمی کند).
        /// </summary>
        private static async Task<bool> RemoveAppxPackageAsync(string packageFullName, bool deprovision = false)
        {
            try
            {
                var packageManager = new Windows.Management.Deployment.PackageManager();
                var result = await packageManager.RemovePackageAsync(packageFullName);
                if (result.ExtendedErrorCode != null)
                {
                    CcLog.Report("Bloatware.Appx", new InvalidOperationException(
                        $"حذف {packageFullName} ناموفق بود: {result.ExtendedErrorCode.Message}"));
                    return false;
                }

                if (deprovision)
                {
                    string packageName = packageFullName.Split('_')[0];
                    try
                    {
                        // Deprovision برای همه ی کاربران: بدون این کار پکیج برای کاربران
                        // جدید ویندوز دوباره نصب می شود.
                        var deprovResult = await packageManager.DeprovisionPackageForAllUsersAsync(packageName);
                        if (deprovResult.ExtendedErrorCode != null)
                        {
                            CcLog.Ignore(new InvalidOperationException(deprovResult.ExtendedErrorCode.Message), "Bloatware.Deprovision");
                        }
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex, "Bloatware.Deprovision");
                    }
                }

                return true;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex, "Bloatware.Appx");
                return false;
            }
        }

        private static bool LaunchWin32Uninstaller(string uninstallString)
        {
            try
            {
                var match = Regex.Match(uninstallString.Trim(), "^\"([^\"]+)\"(.*)$");
                string file = match.Success ? match.Groups[1].Value : uninstallString;
                string args = match.Success ? match.Groups[2].Value.Trim() : string.Empty;

                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsEssentialPackage(string packageName, string packageFullName)
        {
            if (EssentialSystemPackages.Contains(packageName)) return true;

            // D13 — بسته های زیرساختی بحرانی: حذفشان باعث از کار افتادن ده ها اپ می شود
            // (VCLibs / UI.Xaml / WindowsAppRuntime / NET.Native موتور اجرای اپ های استور هستند
            //  و Store خودش فروشگاه است)
            if (packageName.StartsWith("Microsoft.VCLibs", StringComparison.OrdinalIgnoreCase) ||
                packageName.StartsWith("Microsoft.UI.Xaml", StringComparison.OrdinalIgnoreCase) ||
                packageName.StartsWith("Microsoft.WindowsAppRuntime", StringComparison.OrdinalIgnoreCase) ||
                packageName.StartsWith("Microsoft.NET.Native", StringComparison.OrdinalIgnoreCase) ||
                packageName.Equals("Microsoft.WindowsStore", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Protect frameworks & language packs
            if (packageFullName.Contains("VCLibs", StringComparison.OrdinalIgnoreCase) ||
                packageFullName.Contains("NET.Native", StringComparison.OrdinalIgnoreCase) ||
                packageFullName.Contains("UI.Xaml", StringComparison.OrdinalIgnoreCase) ||
                packageFullName.Contains("DirectXRuntime", StringComparison.OrdinalIgnoreCase) ||
                packageFullName.Contains("LanguageExperiencePack", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Protect hardware & audio tools
            if (packageFullName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                packageFullName.Contains("Realtek", StringComparison.OrdinalIgnoreCase) ||
                packageFullName.Contains("Synaptics", StringComparison.OrdinalIgnoreCase) ||
                packageFullName.Contains("DragonCenter", StringComparison.OrdinalIgnoreCase) ||
                packageFullName.Contains("MSI", StringComparison.OrdinalIgnoreCase) ||
                packageFullName.Contains("Nahimic", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static long EstimatePackageSize(string packageFullName, out bool isEstimate)
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps", packageFullName);
                if (Directory.Exists(path))
                {
                    isEstimate = false;
                    return GetDirectorySizeFast(path);
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            // تخمین هاردکد: نه اندازه واقعی، فقط عدد تقریبی برای نمایش
            isEstimate = true;
            return 85 * 1024 * 1024;
        }

        private static long GetDirectorySizeFast(string dirPath)
        {
            long size = 0;
            try
            {
                var dir = new DirectoryInfo(dirPath);
                foreach (var fi in dir.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, MaxRecursionDepth = 3 }))
                {
                    size += fi.Length;
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return size;
        }

        private static string DecodeRot13(string input)
        {
            var chars = input.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c >= 'a' && c <= 'm') chars[i] = (char)(c + 13);
                else if (c >= 'n' && c <= 'z') chars[i] = (char)(c - 13);
                else if (c >= 'A' && c <= 'M') chars[i] = (char)(c + 13);
                else if (c >= 'N' && c <= 'Z') chars[i] = (char)(c - 13);
            }
            return new string(chars);
        }

        private static Dictionary<string, DateTime> GetUserAssistLastRunMap()
        {
            var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var userAssist = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist");
                if (userAssist == null) return map;

                foreach (var guid in userAssist.GetSubKeyNames())
                {
                    using var countKey = userAssist.OpenSubKey($@"{guid}\Count");
                    if (countKey == null) continue;

                    foreach (var valName in countKey.GetValueNames())
                    {
                        string decoded = DecodeRot13(valName);
                        string exeName = Path.GetFileName(decoded);
                        if (string.IsNullOrWhiteSpace(exeName)) continue;

                        if (countKey.GetValue(valName) is byte[] data && data.Length >= 68)
                        {
                            long fileTime = BitConverter.ToInt64(data, 60);
                            if (fileTime > 0)
                            {
                                try
                                {
                                    var dt = DateTime.FromFileTime(fileTime);
                                    if (!map.ContainsKey(exeName) || dt > map[exeName])
                                    {
                                        map[exeName] = dt;
                                    }
                                }
                                catch (Exception __ex)
                                {
                                    CcLog.Ignore(__ex);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return map;
        }
    }
}
