using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;
using CleanC.Core.Native;

namespace CleanC.Core.Services
{
    public class SystemScanService
    {
        public static async Task<ScanResult> ScanAsync(Action<string>? progressCallback = null)
        {
            return await Task.Run(() =>
            {
                var result = new ScanResult();

                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string tempDir = Path.GetTempPath();

                // -------------------------------------------------------------
                // Group 1: Recommended items (موارد پیشنهادی)
                // -------------------------------------------------------------
                progressCallback?.Invoke("در حال ارزیابی فایل های موقت و موارد توصیه شده...");
                var grpRec = new JunkGroup
                {
                    Id = "recommended",
                    Title = "موارد پیشنهادی (Recommended items)",
                    Description = "فایل های زائد و موقتی که پاکسازی آنها ۱۰۰٪ ایمن است",
                    IconGlyph = "\uE8B7"
                };

                // Temp Files (User Temp + Windows Temp) - Always include
                var tempPaths = new List<string> { tempDir, @"C:\Windows\Temp" };
                long tempBytes = SafeGetPathsSize(tempPaths);
                grpRec.Items.Add(new JunkCategory
                {
                    Id = "tempFiles",
                    GroupId = "recommended",
                    Title = "فایل های موقت سیستم و کاربر",
                    Description = "فایل های موقت، کش برنامه ها و پردازه ها در مسیرهای Temp",
                    Path = tempDir,
                    TargetPaths = tempPaths,
                    Bytes = tempBytes,
                    IconGlyph = "\uE7B8"
                });

                // Recycle Bin - Always include
                long binBytes = GetRecycleBinSize();
                grpRec.Items.Add(new JunkCategory
                {
                    Id = "recycleBin",
                    GroupId = "recommended",
                    Title = "سطل بازیافت (Recycle Bin)",
                    Description = "فایل های حذف شده موجود در سطل بازیافت تمام درایوها",
                    Path = "RecycleBin",
                    TargetPaths = new List<string> { "RecycleBin" },
                    Bytes = binBytes,
                    IconGlyph = "\uE74D"
                });

                // Crash Logs & WER - Always include
                // D03: LiveKernelReports هم اضافه شد — گزارش های کرش کرنل که روی برخی
                // سیستم ها به چند گیگابایت می رسند و حذف آنها با SafeCleanFolder امن است.
                var crashPaths = new List<string>
                {
                    Path.Combine(localApp, "CrashDumps"),
                    Path.Combine(localApp, @"Microsoft\Windows\WER"),
                    @"C:\Windows\Minidump",
                    @"C:\Windows\LiveKernelReports"
                };
                long crashBytes = SafeGetPathsSize(crashPaths);
                grpRec.Items.Add(new JunkCategory
                {
                    Id = "crashLogs",
                    GroupId = "recommended",
                    Title = "گزارش های خطا و کرش دامپ ها",
                    Description = LocalizationService.Get("DescCrashLogs", "لاگ های گزارش خرابی، فایل های WER، دامپ های حافظه و گزارش های کرنل (LiveKernelReports)"),
                    Path = crashPaths[0],
                    TargetPaths = crashPaths,
                    Bytes = crashBytes,
                    IconGlyph = "\uE7BA"
                });

                // Edge Cache (All Profiles & Subcaches) - Include if Edge installed
                string edgeUserData = Path.Combine(localApp, @"Microsoft\Edge\User Data");
                if (Directory.Exists(edgeUserData))
                {
                    var edgePaths = GetChromiumCachePaths(edgeUserData);
                    long edgeBytes = SafeGetPathsSize(edgePaths);
                    grpRec.Items.Add(new JunkCategory
                    {
                        Id = "edgeCache",
                        GroupId = "recommended",
                        Title = "حافظه کش مایکروسافت اج (Microsoft Edge)",
                        Description = "کش صفحات وب، تصاویر و کدهای ذخیره شده مرورگر اج",
                        Path = edgePaths.FirstOrDefault() ?? Path.Combine(edgeUserData, @"Default\Cache"),
                        TargetPaths = edgePaths,
                        Bytes = edgeBytes,
                        IconGlyph = "\uE774"
                    });
                }

                // Chrome Cache (All Profiles & Subcaches) - Include if Chrome installed
                string chromeUserData = Path.Combine(localApp, @"Google\Chrome\User Data");
                if (Directory.Exists(chromeUserData))
                {
                    var chromePaths = GetChromiumCachePaths(chromeUserData);
                    long chromeBytes = SafeGetPathsSize(chromePaths);
                    grpRec.Items.Add(new JunkCategory
                    {
                        Id = "chromeCache",
                        GroupId = "recommended",
                        Title = "حافظه کش گوگل کروم (Google Chrome)",
                        Description = "کش صفحات وب، تصاویر و کدهای کامپایل شده مرورگر کروم",
                        Path = chromePaths.FirstOrDefault() ?? Path.Combine(chromeUserData, @"Default\Cache"),
                        TargetPaths = chromePaths,
                        Bytes = chromeBytes,
                        IconGlyph = "\uE774"
                    });
                }

                // Windows Update Download Cache
                string updateCachePath = @"C:\Windows\SoftwareDistribution\Download";
                if (Directory.Exists(updateCachePath))
                {
                    long updateBytes = SafeGetDirectorySize(updateCachePath);
                    if (updateBytes > 0)
                    {
                        grpRec.Items.Add(new JunkCategory
                        {
                            Id = "updateCache",
                            GroupId = "recommended",
                            Title = "کش نصبی آپدیت های ویندوز",
                            Description = "فایل های بسته های نصب شده Windows Update",
                            Path = updateCachePath,
                            TargetPaths = new List<string> { updateCachePath },
                            Bytes = updateBytes,
                            RequiresElevation = true,
                            IconGlyph = "\uE895"
                        });
                    }
                }

                // Delivery Optimization Cache
                var doPaths = new List<string>
                {
                    @"C:\Windows\SoftwareDistribution\DeliveryOptimization",
                    Path.Combine(programData, @"Microsoft\Network\Downloader")
                };
                long doBytes = SafeGetPathsSize(doPaths);
                if (doBytes > 0)
                {
                    grpRec.Items.Add(new JunkCategory
                    {
                        Id = "deliveryOpt",
                        GroupId = "recommended",
                        Title = "کش توزیع دانلود ویندوز (Delivery Optimization)",
                        Description = "فایل های موقت بسته های دانلود آپدیت ها و استور مایکروسافت",
                        Path = doPaths[0],
                        TargetPaths = doPaths,
                        Bytes = doBytes,
                        RequiresElevation = true,
                        IconGlyph = "\uE896"
                    });
                }

                // Prefetch (Windows Performance Cache) — فایل های .pf که ویندوز برای تندتر
                // کردن اجرای برنامه ها می سازد. حذفشان بی خطر است (خودکار بازسازی می شوند) ولی
                // تا مدتی سرعت اجرای برنامه ها کمی کم می شود؛ برای همین پیش فرض خاموش است
                // و فقط حجم و توضیح نمایش داده می شود تا کاربر خودش تصمیم بگیرد.
                string prefetchPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
                if (Directory.Exists(prefetchPath))
                {
                    long prefetchBytes = 0;
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(prefetchPath, "*.pf"))
                        {
                            try { prefetchBytes += new FileInfo(f).Length; }
                            catch (Exception __ex) { CcLog.Ignore(__ex); }
                        }
                    }
                    catch (Exception __ex) { CcLog.Ignore(__ex); }

                    if (prefetchBytes > 0)
                    {
                        grpRec.Items.Add(new JunkCategory
                        {
                            Id = "prefetch",
                            GroupId = "recommended",
                            Title = LocalizationService.Get("JunkPrefetchTitle", "کش Prefetch ویندوز (فایل های .pf)"),
                            Description = LocalizationService.Get("JunkPrefetchDesc", "حافظه سرعت اجرای برنامه های ویندوز — خودکار بازسازی می شود و حذفش بی خطر است"),
                            Path = prefetchPath,
                            TargetPaths = new List<string> { prefetchPath },
                            Bytes = prefetchBytes,
                            RequiresElevation = true,
                            IconGlyph = "\uE945",
                            IsChecked = false
                        });
                    }
                }

                // Explorer Thumbnail Cache
                string explorerCacheDir = Path.Combine(localApp, @"Microsoft\Windows\Explorer");
                if (Directory.Exists(explorerCacheDir))
                {
                    var thumbFiles = Directory.GetFiles(explorerCacheDir, "thumbcache_*.db").ToList();
                    long thumbBytes = 0;
                    foreach (var f in thumbFiles) { try { thumbBytes += new FileInfo(f).Length; } catch (Exception __ex)
{
    CcLog.Ignore(__ex);
} }
                    if (thumbBytes > 0)
                    {
                        grpRec.Items.Add(new JunkCategory
                        {
                            Id = "thumbnailCache",
                            GroupId = "recommended",
                            Title = "حافظه کش بندانگشتی ها (Thumbnail Cache)",
                            Description = "پیش نمایش ذخیره شده فایل ها و تصاویر در فایل اکسپلورر",
                            Path = explorerCacheDir,
                            TargetPaths = thumbFiles,
                            Bytes = thumbBytes,
                            IconGlyph = "\uEB9F"
                        });
                    }
                }

                // Empty Folders in Downloads and Desktop
                try
                {
                    var emptyFolderPaths = new List<string>();
                    var candidateRoots = new[] { Path.Combine(userProfile, "Downloads"), Path.Combine(userProfile, "Desktop") };
                    foreach (var rPath in candidateRoots)
                    {
                        if (Directory.Exists(rPath))
                        {
                            var rDir = new DirectoryInfo(rPath);
                            foreach (var sub in rDir.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                            {
                                try
                                {
                                    if (!sub.EnumerateFileSystemInfos().Any())
                                    {
                                        emptyFolderPaths.Add(sub.FullName);
                                    }
                                }
                                catch (Exception __ex) { CcLog.Ignore(__ex); }
                            }
                        }
                    }

                    grpRec.Items.Add(new JunkCategory
                    {
                        Id = "emptyFolders",
                        GroupId = "recommended",
                        Title = "پوشه‌های خالی رهاشده (Empty Folders)",
                        Description = emptyFolderPaths.Count > 0
                            ? $"{emptyFolderPaths.Count} پوشه کاملاً خالی در Downloads و Desktop"
                            : "پوشه خالی در پوشه‌های کاربری یافت نشد (سیستم مرتب است)",
                        Path = Path.Combine(userProfile, "Downloads"),
                        TargetPaths = emptyFolderPaths,
                        Bytes = 0,
                        IconGlyph = "\uE838",
                        IsChecked = emptyFolderPaths.Count > 0
                    });
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                result.Groups.Add(grpRec);

                // -------------------------------------------------------------
                // Group 2: Other system items (سایر موارد سیستمی)
                // -------------------------------------------------------------
                progressCallback?.Invoke("در حال ارزیابی شیدرهای گرافیکی و حافظه های سیستمی...");
                var grpSys = new JunkGroup
                {
                    Id = "system",
                    Title = "سایر موارد سیستمی (Other system items)",
                    Description = "شیدرهای گرافیکی دایرکت ایکس و گزارش های سیستمی",
                    IconGlyph = "\uE770"
                };

                // DirectX Shader Cache - Always include
                var d3dPaths = new List<string>
                {
                    Path.Combine(localApp, "D3DSCache"),
                    Path.Combine(localApp, @"NVIDIA\DXCache"),
                    Path.Combine(localApp, @"NVIDIA\GLCache"),
                    Path.Combine(localApp, @"AMD\DxCache"),
                    Path.Combine(localApp, @"Intel\ShaderCache")
                };
                long d3dBytes = SafeGetPathsSize(d3dPaths);
                grpSys.Items.Add(new JunkCategory
                {
                    Id = "directxCache",
                    GroupId = "system",
                    Title = "کش شیدرهای گرافیکی (DirectX Shader Cache)",
                    Description = "شیدرهای رندر گرافیکی ذخیره شده بازی ها و کارت گرافیک",
                    Path = d3dPaths[0],
                    TargetPaths = d3dPaths,
                    Bytes = d3dBytes,
                    IconGlyph = "\uE790"
                });

                // Windows Widgets - Include if present
                var widgetPaths = new List<string>
                {
                    Path.Combine(localApp, @"Microsoft\Windows\WebPlatformHost"),
                    Path.Combine(localApp, @"Packages\MicrosoftWindows.Client.WebExperience_cw5n1h2txyewy\AC\INetCache"),
                    Path.Combine(localApp, @"Packages\MicrosoftWindows.Client.WebExperience_cw5n1h2txyewy\LocalCache")
                };
                if (widgetPaths.Any(Directory.Exists))
                {
                    long widgetsBytes = SafeGetPathsSize(widgetPaths);
                    grpSys.Items.Add(new JunkCategory
                    {
                        Id = "windowsWidgets",
                        GroupId = "system",
                        Title = "ویجت های ویندوز (Windows Widgets)",
                        Description = "داده های آفلاین و کش ویجت های ویندوز ۱۱",
                        Path = widgetPaths[0],
                        TargetPaths = widgetPaths,
                        Bytes = widgetsBytes,
                        IconGlyph = "\uE80F"
                    });
                }

                // Network Cache - Always include
                var inetPaths = new List<string>
                {
                    Path.Combine(localApp, @"Microsoft\Windows\INetCache"),
                    Path.Combine(localApp, @"Microsoft\Windows\INetCookies")
                };
                long inetBytes = SafeGetPathsSize(inetPaths);
                grpSys.Items.Add(new JunkCategory
                {
                    Id = "networkCerts",
                    GroupId = "system",
                    Title = "کش گواهی ها و شبکه ویندوز",
                    Description = "فایل های موقت ارتباطی شبکه و ابزارهای سیستم",
                    Path = inetPaths[0],
                    TargetPaths = inetPaths,
                    Bytes = inetBytes,
                    IconGlyph = "\uE839"
                });

                // Orphaned Registry entries
                try
                {
                    var (deadMui, _) = RegistryCleanerService.ScanOrphanedRegistry();
                    if (deadMui > 0)
                    {
                        grpSys.Items.Add(new JunkCategory
                        {
                            Id = "orphanedRegistry",
                            GroupId = "system",
                            Title = "مسیرها و کلیدهای نامعتبر رجیستری (Orphaned Registry)",
                            Description = $"{deadMui} مسیر نامعتبر از برنامه های حذف شده در MuiCache رجیستری",
                            Path = "Registry",
                            TargetPaths = new List<string>(),
                            Bytes = deadMui * 1024,
                            IconGlyph = "\uE770"
                        });
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                result.Groups.Add(grpSys);

                // -------------------------------------------------------------
                // Group 3: Other application items (Dynamic Probing)
                // -------------------------------------------------------------
                progressCallback?.Invoke("در حال پویش نرم افزارهای نصب شده کاربر...");
                var grpApps = new JunkGroup
                {
                    Id = "apps",
                    Title = "سایر موارد برنامه ها (Other application items)",
                    Description = "کش و لاگ های نرم افزارهای کشف شده در سیستم",
                    IconGlyph = "\uE71D"
                };

                // Node.js npm cache
                string npmPath = Path.Combine(localApp, "npm-cache");
                long npmBytes = SafeGetDirectorySize(npmPath);
                if (npmBytes > 0)
                {
                    grpApps.Items.Add(new JunkCategory
                    {
                        Id = "nodejsCache",
                        GroupId = "apps",
                        Title = "کش پکیج های Node.js (npm)",
                        Description = "بسته های موقت دانلودشده npm در سیستم",
                        Path = npmPath,
                        TargetPaths = new List<string> { npmPath },
                        Bytes = npmBytes,
                        IconGlyph = "\uE71D"
                    });
                }

                // NVIDIA Cache
                var nvPaths = new List<string>
                {
                    Path.Combine(localApp, @"NVIDIA\DXCache"),
                    Path.Combine(localApp, @"NVIDIA\GLCache"),
                    Path.Combine(programData, @"NVIDIA Corporation\Downloader"),
                    Path.Combine(programData, @"NVIDIA Corporation\NetService")
                };
                long nvBytes = SafeGetPathsSize(nvPaths);
                if (nvBytes > 0)
                {
                    grpApps.Items.Add(new JunkCategory
                    {
                        Id = "nvidiaCache",
                        GroupId = "apps",
                        Title = "کش درایور و شیدرهای انویدیا (NVIDIA)",
                        Description = "فایل های موقت و شیدرهای رندر کارت گرافیک انویدیا",
                        Path = nvPaths[0],
                        TargetPaths = nvPaths,
                        Bytes = nvBytes,
                        IconGlyph = "\uE790"
                    });
                }

                // OneDrive Logs
                string onedrivePath = Path.Combine(localApp, @"Microsoft\OneDrive\logs");
                long odBytes = SafeGetDirectorySize(onedrivePath);
                if (odBytes > 0)
                {
                    grpApps.Items.Add(new JunkCategory
                    {
                        Id = "onedriveLogs",
                        GroupId = "apps",
                        Title = "لاگ ها و گزارش های OneDrive",
                        Description = "فایل های عیب یابی و لاگ های هماهنگ سازی وان درایو",
                        Path = onedrivePath,
                        TargetPaths = new List<string> { onedrivePath },
                        Bytes = odBytes,
                        IconGlyph = "\uE753"
                    });
                }

                // Pip Cache
                string pipPath = Path.Combine(localApp, @"pip\cache");
                long pipBytes = SafeGetDirectorySize(pipPath);
                if (pipBytes > 0)
                {
                    grpApps.Items.Add(new JunkCategory
                    {
                        Id = "pipCache",
                        GroupId = "apps",
                        Title = "کش پکیج های پایتون (Pip)",
                        Description = "بسته های دانلودشده مخزن پایتون",
                        Path = pipPath,
                        TargetPaths = new List<string> { pipPath },
                        Bytes = pipBytes,
                        IconGlyph = "\uE71D"
                    });
                }

                // VS Code Cache
                var vscPaths = new List<string>
                {
                    Path.Combine(appData, @"Code\Cache"),
                    Path.Combine(appData, @"Code\CachedData"),
                    Path.Combine(appData, @"Code\CachedExtensions"),
                    Path.Combine(appData, @"Code\logs")
                };
                long vscBytes = SafeGetPathsSize(vscPaths);
                if (vscBytes > 0)
                {
                    grpApps.Items.Add(new JunkCategory
                    {
                        Id = "vscodeCache",
                        GroupId = "apps",
                        Title = "کش محیط برنامه نویسی VS Code",
                        Description = "فایل های کش و داده های موقت ویرایشگر کد",
                        Path = vscPaths[0],
                        TargetPaths = vscPaths,
                        Bytes = vscBytes,
                        IconGlyph = "\uE756"
                    });
                }

                // Discord Cache
                var discordPaths = new List<string>
                {
                    Path.Combine(appData, @"discord\Cache"),
                    Path.Combine(appData, @"discord\Code Cache"),
                    Path.Combine(appData, @"discord\GPUCache")
                };
                long discBytes = SafeGetPathsSize(discordPaths);
                if (discBytes > 0)
                {
                    grpApps.Items.Add(new JunkCategory
                    {
                        Id = "discordCache",
                        GroupId = "apps",
                        Title = "کش پیام رسان دیسکورد (Discord)",
                        Description = "تصاویر و رسانه های موقت دانلودشده در دیسکورد",
                        Path = discordPaths[0],
                        TargetPaths = discordPaths,
                        Bytes = discBytes,
                        IconGlyph = "\uE71D"
                    });
                }

                // Telegram Cache
                string tgPath = Path.Combine(appData, @"Telegram Desktop\tdata\user_data");
                long tgBytes = SafeGetDirectorySize(tgPath);
                if (tgBytes > 0)
                {
                    grpApps.Items.Add(new JunkCategory
                    {
                        Id = "telegramCache",
                        GroupId = "apps",
                        Title = "دیتای پیام رسان تلگرام (Telegram Desktop)",
                        Description = "پروفایل و رسانه های کش شده تلگرام؛ حاوی نشست ورود حساب شماست - پاکسازی خودکار عمداً غیرفعال است و فقط با تیک دستی انجام می شود",
                        Path = tgPath,
                        TargetPaths = new List<string> { tgPath },
                        Bytes = tgBytes,
                        IconGlyph = "\uE71D",
                        IsChecked = false
                    });
                }

                // Spotify Cache
                var spotPaths = new List<string>
                {
                    Path.Combine(localApp, @"Spotify\Data"),
                    Path.Combine(localApp, @"Spotify\Storage")
                };
                long spotBytes = SafeGetPathsSize(spotPaths);
                if (spotBytes > 0)
                {
                    grpApps.Items.Add(new JunkCategory
                    {
                        Id = "spotifyCache",
                        GroupId = "apps",
                        Title = "کش نرم افزار اسپاتیفای (Spotify)",
                        Description = "فایل های موقت موسیقی و کاور آلبوم ها",
                        Path = spotPaths[0],
                        TargetPaths = spotPaths,
                        Bytes = spotBytes,
                        IconGlyph = "\uE768"
                    });
                }

                // AI & LLM Models & Caches (Ollama, HuggingFace)
                // توجه: مسیرهای .lmstudio و lm-studio عمداً اینجا نیستند — طبق لیست
                // «قفل/دست نزن» کاربر، LM Studio و مدل هایش هرگز نباید پیشنهاد پاک شدن بگیرند.
                var aiPaths = new List<string>
                {
                    Path.Combine(userProfile, @".ollama\models"),
                    Path.Combine(userProfile, @".cache\huggingface\hub")
                }.Where(Directory.Exists).ToList();

                long aiBytes = SafeGetPathsSize(aiPaths);
                if (aiBytes > 0)
                {
                    grpApps.Items.Add(new JunkCategory
                    {
                        Id = "aiModelsCache",
                        GroupId = "apps",
                        Title = "مدل ها و کش ابزارهای هوش مصنوعی (AI & LLMs)",
                        Description = "مدل های دانلودشده Ollama و کش HuggingFace",
                        Path = aiPaths[0],
                        TargetPaths = aiPaths,
                        Bytes = aiBytes,
                        IconGlyph = "\uE946",
                        IsChecked = false
                    });
                }

                // Mobile Phone Old Device Backups (Apple MobileSync, Samsung SmartSwitch, Huawei HiSuite)
                var phoneBackupPaths = new List<string>
                {
                    Path.Combine(userProfile, @"Apple\MobileSync\Backup"),
                    Path.Combine(appData, @"Apple Computer\MobileSync\Backup"),
                    Path.Combine(userProfile, @"Documents\Samsung\Smart Switch\backup"),
                    Path.Combine(appData, @"Samsung\SmartSwitch\backup"),
                    Path.Combine(userProfile, @"Documents\HiSuite\backup"),
                    Path.Combine(userProfile, @"Documents\MiPhoneManager\Backup")
                }.Where(Directory.Exists).ToList();

                long phoneBytes = SafeGetPathsSize(phoneBackupPaths);
                grpApps.Items.Add(new JunkCategory
                {
                    Id = "phoneBackups",
                    GroupId = "apps",
                    Title = "بکاپ‌های قدیمی گوشی و تبلت (Apple، سامسونگ، هواوی)",
                    Description = phoneBytes > 0
                        ? "نسخه‌های پشتیبان حجیم دستگاه‌های موبایل؛ این تنها نسخه پشتیبان شماست - حذف خودکار عمداً غیرفعال است و فقط بعد از تیک دستی و مرور مسیر انجام می شود"
                        : "هیچ بکاپ قدیمی از گوشی یا تبلت در کامپیوتر یافت نشد",
                    Path = phoneBackupPaths.FirstOrDefault() ?? Path.Combine(userProfile, @"Apple\MobileSync\Backup"),
                    TargetPaths = phoneBackupPaths,
                    Bytes = phoneBytes,
                    IconGlyph = "\uE71D",
                    IsChecked = false
                });

                // Browser Dead Extensions & Orphaned IndexedDB Storage
                try
                {
                    var deadExtList = BrowserOptimizerService.ScanExtensions()
                        .Where(e => e.Category == ExtensionRiskCategory.SafeToClean || BrowserOptimizerService.KnownDeadExtensionIds.Contains(e.Id))
                        .ToList();
                    long deadExtBytes = deadExtList.Sum(e => e.SizeBytes);
                    grpApps.Items.Add(new JunkCategory
                    {
                        Id = "browserDeadExtensions",
                        GroupId = "apps",
                        Title = "افزونه‌های زائد و دیتابیس‌های منسوخ مرورگرها (Chrome / Edge)",
                        Description = deadExtList.Count > 0
                            ? $"{deadExtList.Count} افزونه منسوخ یا دیتابیس رهاشده با حفظ ۱۰۰٪ اطلاعات سایت‌ها"
                            : "افزونه منسوخ یا دیتابیس زائد مرورگر یافت نشد (مرورگرها مرتب هستند)",
                        Path = "Browsers",
                        TargetPaths = deadExtList.Select(e => e.InstallPath).Where(p => !string.IsNullOrEmpty(p)).ToList(),
                        Bytes = deadExtBytes,
                        IconGlyph = "\uE774",
                        IsChecked = deadExtList.Count > 0
                    });
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                result.Groups.Add(grpApps);

                // -------------------------------------------------------------
                // Group 4: Computer Usage Traces (ردپای استفاده از کامپیوتر)
                // -------------------------------------------------------------
                progressCallback?.Invoke("در حال بررسی تاریخچه و ردپای استفاده از سیستم...");
                var grpTraces = new JunkGroup
                {
                    Id = "traces",
                    Title = "ردپای استفاده از کامپیوتر (Usage Traces)",
                    Description = "لیست فایل های اخیر، پرش سریع و تاریخچه های سیستم",
                    IconGlyph = "\uE81C"
                };

                // Recent Items - Always include
                string recentPath = Path.Combine(appData, @"Microsoft\Windows\Recent");
                long recentBytes = SafeGetDirectorySize(recentPath);
                grpTraces.Items.Add(new JunkCategory
                {
                    Id = "recentFiles",
                    GroupId = "traces",
                    Title = "تاریخچه فایل های اخیر (Recent Items)",
                    Description = "شورتکات های ثبت شده فایل های اخیراً باز شده در ویندوز",
                    Path = recentPath,
                    TargetPaths = new List<string> { recentPath },
                    Bytes = recentBytes,
                    IconGlyph = "\uE81C"
                });

                // Explorer Jump Lists - Always include
                var jumpPaths = new List<string>
                {
                    Path.Combine(appData, @"Microsoft\Windows\Recent\AutomaticDestinations"),
                    Path.Combine(appData, @"Microsoft\Windows\Recent\CustomDestinations")
                };
                long jumpBytes = SafeGetPathsSize(jumpPaths);
                grpTraces.Items.Add(new JunkCategory
                {
                    Id = "explorerJumpList",
                    GroupId = "traces",
                    Title = "لیست های پرش اکسپلورر (Jump Lists)",
                    Description = "میانبرهای برنامه های اخیر در تسک بار ویندوز",
                    Path = jumpPaths[0],
                    TargetPaths = jumpPaths,
                    Bytes = jumpBytes,
                    IconGlyph = "\uE8A9"
                });

                result.Groups.Add(grpTraces);

                // -------------------------------------------------------------
                // Group 5: Download Managers & Incomplete Files
                // -------------------------------------------------------------
                progressCallback?.Invoke("در حال ارزیابی دانلود منیجرها و فایل های ناقص...");
                var grpDownloads = new JunkGroup
                {
                    Id = "downloads",
                    Title = "دانلود منیجرها و فایل های ناتمام (Download Managers)",
                    Description = "چانک های موقت، تکه های دانلود ناقص و فایل های رهاشده",
                    IconGlyph = "\uE896"
                };

                // IDM Incomplete download chunks
                string idmDataDir = Path.Combine(appData, @"IDM\DwnlData");
                if (Directory.Exists(idmDataDir))
                {
                    long idmBytes = SafeGetDirectorySize(idmDataDir);
                    if (idmBytes > 0)
                    {
                        grpDownloads.Items.Add(new JunkCategory
                        {
                            Id = "idmChunks",
                            GroupId = "downloads",
                            Title = "فایل های موقت و چانک های ناتمام IDM",
                            Description = "قطعات موقت دانلودهای متوقف شده، لغوشده و رهاشده در اینترنت دانلود منیجر",
                            Path = idmDataDir,
                            TargetPaths = new List<string> { idmDataDir },
                            Bytes = idmBytes,
                            IconGlyph = "\uE896"
                        });
                    }
                }

                // Free Download Manager (FDM) temp
                var fdmPaths = new List<string>
                {
                    Path.Combine(localApp, @"Softdeluxe\Free Download Manager"),
                    Path.Combine(appData, @"Free Download Manager")
                };
                long fdmBytes = SafeGetPathsSize(fdmPaths);
                if (fdmBytes > 0)
                {
                    grpDownloads.Items.Add(new JunkCategory
                    {
                        Id = "fdmTemp",
                        GroupId = "downloads",
                        Title = "فایل های موقت Free Download Manager (FDM)",
                        Description = "کش و قطعات موقت دانلودهای ناتمام FDM",
                        Path = fdmPaths[0],
                        TargetPaths = fdmPaths,
                        Bytes = fdmBytes,
                        IconGlyph = "\uE896"
                    });
                }

                // Abandoned Browser Downloads (*.crdownload, *.part older than 3 days)
                string userDownloads = Path.Combine(userProfile, "Downloads");
                if (Directory.Exists(userDownloads))
                {
                    try
                    {
                        var abandonedFiles = new List<string>();
                        long abandonedBytes = 0;
                        var dir = new DirectoryInfo(userDownloads);
                        var cutoff = DateTime.Now.AddDays(-3);

                        foreach (var fi in dir.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly))
                        {
                            if (fi.Extension.Equals(".crdownload", StringComparison.OrdinalIgnoreCase) ||
                                fi.Extension.Equals(".part", StringComparison.OrdinalIgnoreCase) ||
                                fi.Extension.Equals(".download", StringComparison.OrdinalIgnoreCase))
                            {
                                if (fi.LastWriteTime < cutoff)
                                {
                                    abandonedFiles.Add(fi.FullName);
                                    abandonedBytes += fi.Length;
                                }
                            }
                        }

                        if (abandonedFiles.Count > 0)
                        {
                            grpDownloads.Items.Add(new JunkCategory
                            {
                                Id = "abandonedDownloads",
                                GroupId = "downloads",
                                Title = "دانلودهای ناتمام و ناقص قدیمی مرورگرها",
                                Description = $"{abandonedFiles.Count} فایل ناقص رهاشده در پوشه Downloads (.crdownload / .part)",
                                Path = userDownloads,
                                TargetPaths = abandonedFiles,
                                Bytes = abandonedBytes,
                                IconGlyph = "\uE896"
                            });
                        }

                        // Old Installers & Setups (.exe, .msi, .iso older than 14 days in Downloads)
                        var installerFiles = new List<string>();
                        long installerBytes = 0;
                        var cutoffInstallers = DateTime.Now.AddDays(-14);

                        foreach (var fi in dir.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly))
                        {
                            if (fi.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                                fi.Extension.Equals(".msi", StringComparison.OrdinalIgnoreCase) ||
                                fi.Extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
                            {
                                if (fi.LastWriteTime < cutoffInstallers)
                                {
                                    installerFiles.Add(fi.FullName);
                                    installerBytes += fi.Length;
                                }
                            }
                        }

                        grpDownloads.Items.Add(new JunkCategory
                        {
                            Id = "oldInstallers",
                            GroupId = "downloads",
                            Title = "فایل‌های نصبی و اینستالرهای قدیمی (.exe / .iso)",
                            Description = installerFiles.Count > 0
                                ? $"{installerFiles.Count} فایل نصبی با بیش از ۱۴ روز عدم استفاده در Downloads"
                                : "اینستالر یا ستاپ قدیمی با بیش از ۱۴ روز قدمت یافت نشد",
                            Path = userDownloads,
                            TargetPaths = installerFiles,
                            Bytes = installerBytes,
                            IconGlyph = "\uE896",
                            IsChecked = false
                        });

                        // Large Files > 200MB older than 21 days in Downloads
                        var largeFiles = new List<string>();
                        long largeFilesBytes = 0;
                        var cutoffLarge = DateTime.Now.AddDays(-21);

                        foreach (var fi in dir.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly))
                        {
                            if (fi.Length >= 200L * 1024 * 1024 && fi.LastWriteTime < cutoffLarge)
                            {
                                largeFiles.Add(fi.FullName);
                                largeFilesBytes += fi.Length;
                            }
                        }

                        grpDownloads.Items.Add(new JunkCategory
                        {
                            Id = "largeUserFiles",
                            GroupId = "downloads",
                            Title = "فایل‌های بسیار حجیم قدیمی (بالای ۲۰۰ مگابایت)",
                            Description = largeFiles.Count > 0
                                ? $"{largeFiles.Count} فایل حجیم با بیش از ۲۱ روز عدم تغییر در پوشه Downloads"
                                : "فایل حجیم قدیمی بالای ۲۰۰ مگابایت در Downloads یافت نشد",
                            Path = userDownloads,
                            TargetPaths = largeFiles,
                            Bytes = largeFilesBytes,
                            IconGlyph = "\uEDA2",
                            IsChecked = false
                        });
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
                }

                result.Groups.Add(grpDownloads);

                // -------------------------------------------------------------
                // Group 6: Leftover Folders of Uninstalled Apps
                // -------------------------------------------------------------
                progressCallback?.Invoke("در حال پویش پوشه های باقیمانده نرم افزارهای حذف شده...");
                try
                {
                    var leftovers = LeftoverCleanerService.ScanLeftoverFolders();
                    if (leftovers.Count > 0)
                    {
                        var grpLeftovers = new JunkGroup
                        {
                            Id = "leftovers",
                            Title = "پوشه های باقیمانده نرم افزارهای حذف شده (Leftovers)",
                            Description = "پوشه های حجیم برنامه هایی که از سیستم حذف شده اند ولی داده هایشان باقی مانده است",
                            IconGlyph = "\uE74D"
                        };

                        foreach (var lo in leftovers)
                        {
                            grpLeftovers.Items.Add(new JunkCategory
                            {
                                Id = $"leftover_{lo.FolderName}",
                                GroupId = "leftovers",
                                Title = $"پوشه رهاشده: {lo.FolderName} ({lo.LocationType})",
                                Description = $"مسیر: {lo.FullPath}",
                                Path = lo.FullPath,
                                TargetPaths = new List<string> { lo.FullPath },
                                Bytes = lo.SizeBytes,
                                IconGlyph = "\uE74D"
                            });
                        }

                        result.Groups.Add(grpLeftovers);
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // -------------------------------------------------------------
                // Group 7: Live System Optimization & Performance Boost
                // -------------------------------------------------------------
                progressCallback?.Invoke("در حال ارزیابی گزینه های بهینه سازی زنده و پایداری...");
                var grpBoost = new JunkGroup
                {
                    Id = "system_boost",
                    Title = "بهینه سازی زنده و پایداری سیستم (Performance & Boost)",
                    Description = "آزادسازی رم، کش شبکه، پروسس های اضافی، استارتاپ، Debloat و پلن انرژی",
                    IconGlyph = "\uE9D2"
                };

                grpBoost.Items.Add(new JunkCategory
                {
                    Id = "ramBoost",
                    GroupId = "system_boost",
                    Title = "آزادسازی حافظه فعال رم (RAM Working Set)",
                    Description = "تخلیه Working Set پردازه ها و آزادسازی فوری حافظه اشغال شده بدون بستن برنامه ها",
                    Path = "Memory",
                    TargetPaths = new List<string>(),
                    Bytes = 0,
                    IconGlyph = "\uE9D2",
                    IsChecked = true
                });

                grpBoost.Items.Add(new JunkCategory
                {
                    Id = "dnsFlush",
                    GroupId = "system_boost",
                    Title = "تخلیه کش DNS و نوسازی ارتباطات شبکه",
                    Description = "پاکسازی رکوردهای نامعتبر دامنه از کش ویندوز و رفع کندی ارتباطات اینترنتی",
                    Path = "Network",
                    TargetPaths = new List<string>(),
                    Bytes = 0,
                    IconGlyph = "\uE774",
                    IsChecked = true
                });

                grpBoost.Items.Add(new JunkCategory
                {
                    Id = "backgroundHelpers",
                    GroupId = "system_boost",
                    Title = "بستن پردازه ها و کمک کننده های اضافی در پس زمینه",
                    Description = "توقف سرویس های جانبی، پروسه های گزارش گیری و آپدیترهای مقیم در پس زمینه",
                    Path = "Processes",
                    TargetPaths = new List<string>(),
                    Bytes = 0,
                    IconGlyph = "\uE9F5",
                    IsChecked = true
                });

                grpBoost.Items.Add(new JunkCategory
                {
                    Id = "browserBoost",
                    GroupId = "system_boost",
                    Title = "خاموش سازی تسک های پس زمینه مرورگرها (Edge / Chrome)",
                    Description = "غیرفعال سازی Startup Boost و توقف فعالیت پنهان مرورگرها پس از بسته شدن",
                    Path = "Browsers",
                    TargetPaths = new List<string>(),
                    Bytes = 0,
                    IconGlyph = "\uE774",
                    IsChecked = true
                });

                grpBoost.Items.Add(new JunkCategory
                {
                    Id = "startupOptimize",
                    GroupId = "system_boost",
                    Title = "بهینه سازی استارتاپ ویندوز",
                    Description = "غیرفعال سازی برنامه ها و لانچرهای سنگین شروع خودکار جهت افزایش سرعت بوت سیستم",
                    Path = "Startup",
                    TargetPaths = new List<string>(),
                    Bytes = 0,
                    IconGlyph = "\uE7B5",
                    IsChecked = true
                });

                grpBoost.Items.Add(new JunkCategory
                {
                    Id = "bloatwareClean",
                    GroupId = "system_boost",
                    Title = "حذف برنامه ها و بازی های تبلیغاتی پیش فرض (Debloat)",
                    Description = "حذف نرم افزارها و بازی های تبلیغاتی پیش فرض و بررسی برنامه های کم استفاده",
                    Path = "Bloatware",
                    TargetPaths = new List<string>(),
                    Bytes = 0,
                    IconGlyph = "\uE74C",
                    IsChecked = true
                });

                grpBoost.Items.Add(new JunkCategory
                {
                    Id = "fastStartup",
                    GroupId = "system_boost",
                    Title = "کوتاه کردن زمان خاموش شدن سیستم (Fast Startup)",
                    Description = "فعال سازی خاموش سازی و راه اندازی فوق سریع ویندوز",
                    Path = "FastStartup",
                    TargetPaths = new List<string>(),
                    Bytes = 0,
                    IconGlyph = "\uE7E8",
                    IsChecked = true
                });

                grpBoost.Items.Add(new JunkCategory
                {
                    Id = "gpuOptimize",
                    GroupId = "system_boost",
                    Title = "تخصیص هوشمند کارت گرافیک مجزا",
                    Description = "هدایت بازی ها و برنامه های سنگین به پردازنده گرافیکی قدرتمند اصلی",
                    Path = "GPU",
                    TargetPaths = new List<string>(),
                    Bytes = 0,
                    IconGlyph = "\uE790",
                    IsChecked = true
                });

                grpBoost.Items.Add(new JunkCategory
                {
                    Id = "winTweaks",
                    GroupId = "system_boost",
                    Title = "بهینه سازی تنظیمات عملکرد و حریم خصوصی ویندوز",
                    Description = "غیرفعال سازی تله متری، سرویس DiagTrack، تبلیغات منوی استارت و سرچ ویندوز",
                    Path = "Tweaks",
                    TargetPaths = new List<string>(),
                    Bytes = 0,
                    IconGlyph = "\uE770",
                    IsChecked = true
                });

                grpBoost.Items.Add(new JunkCategory
                {
                    Id = "inactiveHeavyApps",
                    GroupId = "system_boost",
                    Title = "بازبینی نرم‌افزارهای حجیم و کم‌استفاده (> 500MB با بیش از ۳۰ روز عدم اجرا)",
                    Description = "مکث تعاملی جهت نمایش لیست برنامه‌های سنگین بلااستفاده و تایید اختیاری کاربر برای حذف",
                    Path = "AppsReview",
                    TargetPaths = new List<string>(),
                    Bytes = 0,
                    IconGlyph = "\uE74C",
                    IsChecked = true
                });

                result.Groups.Add(grpBoost);

                return result;
            });
        }

        // گارد پیمایش پوشه‌ها: تحلیلگر اندازه هرگز نباید روی درخت‌های پوشه‌های
        // پهن/گودافتاده (مثل کش‌های سنگین) چند ده ثانیه گیر کند
        private const int ScanMaxDepth = 6;
        private const int ScanMaxFiles = 20000;
        private static readonly TimeSpan ScanTimeBudget = TimeSpan.FromSeconds(2);

        public static long SafeGetDirectorySize(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return 0;

            long size = 0;
            int files = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            void Walk(string dir, int depth)
            {
                if (files >= ScanMaxFiles || sw.Elapsed > ScanTimeBudget) return;
                var opts = new EnumerationOptions { IgnoreInaccessible = true };
                try
                {
                    foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*", opts))
                    {
                        if (files >= ScanMaxFiles || sw.Elapsed > ScanTimeBudget) return;
                        try { size += file.Length; files++; } catch (Exception __ex)
                        {
                            CcLog.Ignore(__ex);
                        }
                    }
                    if (depth < ScanMaxDepth)
                    {
                        foreach (var sub in new DirectoryInfo(dir).EnumerateDirectories("*", opts))
                            Walk(sub.FullName, depth + 1);
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }
            }

            Walk(path, 0);
            return size;
        }

        public static long SafeGetPathsSize(IEnumerable<string> paths)
        {
            long sum = 0;
            foreach (var p in paths)
            {
                sum += SafeGetDirectorySize(p);
            }
            return sum;
        }

        public static List<string> GetChromiumCachePaths(string baseUserDataDir)
        {
            var paths = new List<string>();
            if (string.IsNullOrWhiteSpace(baseUserDataDir) || !Directory.Exists(baseUserDataDir))
                return paths;

            try
            {
                var userDir = new DirectoryInfo(baseUserDataDir);
                foreach (var profDir in userDir.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true }))
                {
                    if (profDir.Name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                        profDir.Name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase) ||
                        profDir.Name.Equals("System Profile", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] cacheNames = { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", "optimization_guide_hint_cache_store" };
                        foreach (var cName in cacheNames)
                        {
                            string cPath = Path.Combine(profDir.FullName, cName);
                            if (Directory.Exists(cPath))
                            {
                                paths.Add(cPath);
                            }
                        }
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return paths;
        }

        private static long GetRecycleBinSize()
        {
            try
            {
                var rbInfo = new Win32.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Win32.SHQUERYRBINFO)) };
                int hr = Win32.SHQueryRecycleBin(null, ref rbInfo);
                if (hr == 0) return rbInfo.i64Size;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return 0;
        }
    }
}
