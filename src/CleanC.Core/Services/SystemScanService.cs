using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
                progressCallback?.Invoke("در حال ارزیابی فایل‌های موقت و موارد توصیه‌شده...");
                var grpRec = new JunkGroup
                {
                    Id = "recommended",
                    Title = "موارد پیشنهادی (Recommended items)",
                    Description = "فایل‌های زائد و موقتی که پاکسازی آنها ۱۰۰٪ ایمن است",
                    IconGlyph = "\uE8B7"
                };

                // Temp Files (User Temp + Windows Temp) - Always include
                var tempPaths = new List<string> { tempDir, @"C:\Windows\Temp" };
                long tempBytes = SafeGetPathsSize(tempPaths);
                grpRec.Items.Add(new JunkCategory
                {
                    Id = "tempFiles",
                    GroupId = "recommended",
                    Title = "فایل‌های موقت سیستم و کاربر",
                    Description = "فایل‌های موقت، کش برنامه‌ها و پردازه‌ها در مسیرهای Temp",
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
                    Description = "فایل‌های حذف‌شده موجود در سطل بازیافت تمام درایوها",
                    Path = "RecycleBin",
                    TargetPaths = new List<string> { "RecycleBin" },
                    Bytes = binBytes,
                    IconGlyph = "\uE74D"
                });

                // Crash Logs & WER - Always include
                var crashPaths = new List<string>
                {
                    Path.Combine(localApp, "CrashDumps"),
                    Path.Combine(localApp, @"Microsoft\Windows\WER"),
                    @"C:\Windows\Minidump"
                };
                long crashBytes = SafeGetPathsSize(crashPaths);
                grpRec.Items.Add(new JunkCategory
                {
                    Id = "crashLogs",
                    GroupId = "recommended",
                    Title = "گزارش‌های خطا و کرش‌دامپ‌ها",
                    Description = "لاگ‌های گزارش خرابی، فایل‌های WER و دامپ‌های حافظه",
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
                        Description = "کش صفحات وب، تصاویر و کدهای ذخیره‌شده مرورگر اج",
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
                        Description = "کش صفحات وب، تصاویر و کدهای کامپایل‌شده مرورگر کروم",
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
                            Title = "کش نصبی آپدیت‌های ویندوز",
                            Description = "فایل‌های بسته‌های نصب‌شده Windows Update",
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
                        Description = "فایل‌های موقت بسته‌های دانلود آپدیت‌ها و استور مایکروسافت",
                        Path = doPaths[0],
                        TargetPaths = doPaths,
                        Bytes = doBytes,
                        RequiresElevation = true,
                        IconGlyph = "\uE896"
                    });
                }

                // Explorer Thumbnail Cache
                string explorerCacheDir = Path.Combine(localApp, @"Microsoft\Windows\Explorer");
                if (Directory.Exists(explorerCacheDir))
                {
                    var thumbFiles = Directory.GetFiles(explorerCacheDir, "thumbcache_*.db").ToList();
                    long thumbBytes = 0;
                    foreach (var f in thumbFiles) { try { thumbBytes += new FileInfo(f).Length; } catch { } }
                    if (thumbBytes > 0)
                    {
                        grpRec.Items.Add(new JunkCategory
                        {
                            Id = "thumbnailCache",
                            GroupId = "recommended",
                            Title = "حافظه کش بندانگشتی‌ها (Thumbnail Cache)",
                            Description = "پیش‌نمایش ذخیره‌شده فایل‌ها و تصاویر در فایل اکسپلورر",
                            Path = explorerCacheDir,
                            TargetPaths = thumbFiles,
                            Bytes = thumbBytes,
                            IconGlyph = "\uEB9F"
                        });
                    }
                }

                result.Groups.Add(grpRec);

                // -------------------------------------------------------------
                // Group 2: Other system items (سایر موارد سیستمی)
                // -------------------------------------------------------------
                progressCallback?.Invoke("در حال ارزیابی شیدرهای گرافیکی و حافظه‌های سیستمی...");
                var grpSys = new JunkGroup
                {
                    Id = "system",
                    Title = "سایر موارد سیستمی (Other system items)",
                    Description = "شیدرهای گرافیکی دایرکت‌ایکس و گزارش‌های سیستمی",
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
                    Description = "شیدرهای رندر گرافیکی ذخیره‌شده بازی‌ها و کارت گرافیک",
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
                        Title = "ویجت‌های ویندوز (Windows Widgets)",
                        Description = "داده‌های آفلاین و کش ویجت‌های ویندوز ۱۱",
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
                    Title = "کش گواهی‌ها و شبکه ویندوز",
                    Description = "فایل‌های موقت ارتباطی شبکه و ابزارهای سیستم",
                    Path = inetPaths[0],
                    TargetPaths = inetPaths,
                    Bytes = inetBytes,
                    IconGlyph = "\uE839"
                });

                result.Groups.Add(grpSys);

                // -------------------------------------------------------------
                // Group 3: Other application items (Dynamic Probing)
                // -------------------------------------------------------------
                progressCallback?.Invoke("در حال پویش نرم‌افزارهای نصب‌شده کاربر...");
                var grpApps = new JunkGroup
                {
                    Id = "apps",
                    Title = "سایر موارد برنامه‌ها (Other application items)",
                    Description = "کش و لاگ‌های نرم‌افزارهای کشف‌شده در سیستم",
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
                        Title = "کش پکیج‌های Node.js (npm)",
                        Description = "بسته‌های موقت دانلودشده npm در سیستم",
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
                        Description = "فایل‌های موقت و شیدرهای رندر کارت گرافیک انویدیا",
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
                        Title = "لاگ‌ها و گزارش‌های OneDrive",
                        Description = "فایل‌های عیب‌یابی و لاگ‌های هماهنگ‌سازی وان‌درایو",
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
                        Title = "کش پکیج‌های پایتون (Pip)",
                        Description = "بسته‌های دانلودشده مخزن پایتون",
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
                        Title = "کش محیط برنامه‌نویسی VS Code",
                        Description = "فایل‌های کش و داده‌های موقت ویرایشگر کد",
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
                        Title = "کش پیام‌رسان دیسکورد (Discord)",
                        Description = "تصاویر و رسانه‌های موقت دانلودشده در دیسکورد",
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
                        Title = "کش پیام‌رسان تلگرام (Telegram)",
                        Description = "پروفایل‌ها و رسانه‌های موقت کش‌شده تلگرام",
                        Path = tgPath,
                        TargetPaths = new List<string> { tgPath },
                        Bytes = tgBytes,
                        IconGlyph = "\uE71D"
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
                        Title = "کش نرم‌افزار اسپاتیفای (Spotify)",
                        Description = "فایل‌های موقت موسیقی و کاور آلبوم‌ها",
                        Path = spotPaths[0],
                        TargetPaths = spotPaths,
                        Bytes = spotBytes,
                        IconGlyph = "\uE768"
                    });
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
                    Description = "لیست فایل‌های اخیر، پرش سریع و تاریخچه‌های سیستم",
                    IconGlyph = "\uE81C"
                };

                // Recent Items - Always include
                string recentPath = Path.Combine(appData, @"Microsoft\Windows\Recent");
                long recentBytes = SafeGetDirectorySize(recentPath);
                grpTraces.Items.Add(new JunkCategory
                {
                    Id = "recentFiles",
                    GroupId = "traces",
                    Title = "تاریخچه فایل‌های اخیر (Recent Items)",
                    Description = "شورتکات‌های ثبت‌شده فایل‌های اخیراً باز شده در ویندوز",
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
                    Title = "لیست‌های پرش اکسپلورر (Jump Lists)",
                    Description = "میانبرهای برنامه‌های اخیر در تسک‌بار ویندوز",
                    Path = jumpPaths[0],
                    TargetPaths = jumpPaths,
                    Bytes = jumpBytes,
                    IconGlyph = "\uE8A9"
                });

                result.Groups.Add(grpTraces);

                return result;
            });
        }

        public static long SafeGetDirectorySize(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return 0;

            long size = 0;
            try
            {
                var dir = new DirectoryInfo(path);
                foreach (var file in dir.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true }))
                {
                    try { size += file.Length; } catch { }
                }
            }
            catch { }

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
            catch { }

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
            catch { }
            return 0;
        }
    }
}
