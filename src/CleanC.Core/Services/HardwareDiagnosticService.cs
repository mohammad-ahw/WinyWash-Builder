using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;
using CleanC.Core.Native;

namespace CleanC.Core.Services
{
    public class AttentionDecisionItem
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = "سخت افزار";
        public string ActionText { get; set; } = "اعمال تغییر";
        public string IconGlyph { get; set; } = "\uE7F4";
        public bool IsApplied { get; set; }
        public int IntParameter { get; set; }
    }

    public class HardwareDiagnosticService
    {
        public static async Task<List<AttentionDecisionItem>> RunDiagnosticsAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<AttentionDecisionItem>();

                // 1. Display Refresh Rate Check
                try
                {
                    var displayItem = CheckRefreshRate();
                    if (displayItem != null) list.Add(displayItem);
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 2. Power Plan Optimization Check
                try
                {
                    var powerItem = PowerPlanService.CheckPowerPlanAttention();
                    if (powerItem != null) list.Add(powerItem);
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 3. Bloatware & Unused Apps Check
                try
                {
                    var bloatCandidates = BloatwareService.ScanCandidateAppsAsync().GetAwaiter().GetResult();
                    if (bloatCandidates.Count > 0)
                    {
                        long totalBloatBytes = bloatCandidates.Sum(b => b.SizeBytes);
                        list.Add(new AttentionDecisionItem
                        {
                            Id = "bloatware_apps",
                            Title = $"{bloatCandidates.Count} برنامه غیرضروری، تبلیغاتی یا کم استفاده شناسایی شد",
                            Description = $"برنامه های تبلیغاتی و بلااستفاده پیش فرض که حدود {SystemStats.FormatBytes(totalBloatBytes)} از فضای دیسک و منابع سیستم را درگیر کرده اند.",
                            Category = "مدیریت نرم افزارها",
                            ActionText = "بررسی و حذف",
                            IconGlyph = "\uE74D",
                            IntParameter = bloatCandidates.Count
                        });
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 4. Advanced Network & Responsiveness Tweaks Check
                try
                {
                    var advItem = AdvancedTweakService.CheckAdvancedTweaksAttention();
                    if (advItem != null) list.Add(advItem);
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 5. SSD Critical Space Check
                try
                {
                    var drive = new DriveInfo("C");
                    if (drive.IsReady)
                    {
                        double freePercent = (double)drive.AvailableFreeSpace / drive.TotalSize * 100.0;
                        long freeGb = drive.AvailableFreeSpace / (1024L * 1024L * 1024L);
                        if (freePercent < 15.0 || freeGb < 20)
                        {
                            list.Add(new AttentionDecisionItem
                            {
                                Id = "ssd_critical_space",
                                Title = $"کاهش بحرانی فضای درایو ویندوز ({freeGb} GB معادل {freePercent:0}% آزاد)",
                                Description = "پر بودن بیش از ۸۵٪ درایو SSD موجب افت ۵۰ درصدی سرعت و استهلاک زودهنگام سلول های فلش می شود. توصیه می شود پاکسازی و دستور TRIM انجام شود.",
                                Category = "حافظه و دیسک",
                                ActionText = "بهینه سازی TRIM دیسک",
                                IconGlyph = "\uEDA2"
                            });
                        }
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 6. HAGS (Hardware-Accelerated GPU Scheduling) Check
                try
                {
                    if (GpuOptimizerService.IsHagsNeeded())
                    {
                        list.Add(new AttentionDecisionItem
                        {
                            Id = "gpu_hags",
                            Title = "شتاب دهنده سخت افزاری گرافیک (HAGS) فعال نیست",
                            Description = "با فعال سازی Hardware Accelerated GPU Scheduling، پردازش زمان بندی رندرینگ مستقیماً به کارت گرافیک سپرده شده و تاخیر فریم ها کاهش می یابد.",
                            Category = "کارت گرافیک و گیمینگ",
                            ActionText = "فعال سازی HAGS",
                            IconGlyph = "\uE790"
                        });
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 7. Ghost Proxy & VPN Conflict Check
                try
                {
                    var proxyConflict = NetworkProxyDiagnosticService.DetectGhostProxyConflict();
                    if (proxyConflict.HasConflict)
                    {
                        list.Insert(0, new AttentionDecisionItem
                        {
                            Id = "proxy_vpn_conflict",
                            Title = $"تداخل پراکسی سیستم و قطعی اینترنت ({proxyConflict.ProxyServer})",
                            Description = proxyConflict.Details,
                            Category = "شبکه و ارتباطات",
                            ActionText = "رفع قطعی و حذف پراکسی",
                            IconGlyph = "\uE774"
                        });
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 8. Orphaned Services Check
                try
                {
                    var orphanedServices = RegistryCleanerService.ScanOrphanedServices();
                    if (orphanedServices.Count > 0)
                    {
                        list.Add(new AttentionDecisionItem
                        {
                            Id = "orphaned_services",
                            Title = $"{orphanedServices.Count} سرویس یتیم در ویندوز شناسایی شد",
                            Description = "سرویس‌هایی که فایل اجرایی آن‌ها از روی دیسک پاک شده اما ورودی آن‌ها در ویندوز مانده و باعث کندی استارتاپ سیستم می‌شوند.",
                            Category = "سرویس‌ها و بوت",
                            ActionText = "غیرفعال‌سازی سرویس‌ها",
                            IconGlyph = "\uE7B5",
                            IntParameter = orphanedServices.Count
                        });
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 9. WSL / Docker VHDX Compaction Check
                try
                {
                    var vhdxFiles = DiskMaintenanceService.GetWslDockerVhdxFiles();
                    long totalVhdx = vhdxFiles.Sum(f => f.Length);
                    if (totalVhdx >= 2L * 1024 * 1024 * 1024) // 2GB+
                    {
                        list.Add(new AttentionDecisionItem
                        {
                            Id = "wsl_docker_compact",
                            Title = $"فشرده‌سازی دیسک‌های مجازی WSL و داکر ({SystemStats.FormatBytes(totalVhdx)})",
                            Description = "دیسک‌های مجازی پس از حذف فایل‌ها و کانتینرها حجم خود را پس نمی‌دهند. فشرده‌سازی VHDX می‌تواند گیگابایت‌ها فضا را آزاد کند.",
                            Category = "مجازی‌سازی و دیسک",
                            ActionText = "فشرده‌سازی دیسک‌ها",
                            IconGlyph = "\uEDA2"
                        });
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                return list;
            });
        }

        public static AttentionDecisionItem? CheckRefreshRate()
        {
            var current = new Win32.DEVMODE();
            current.dmSize = (short)Marshal.SizeOf(typeof(Win32.DEVMODE));

            if (!Win32.EnumDisplaySettings(null, Win32.ENUM_CURRENT_SETTINGS, ref current))
            {
                return null;
            }

            int currentHz = current.dmDisplayFrequency;
            int width = current.dmPelsWidth;
            int height = current.dmPelsHeight;
            int maxHz = currentHz;

            int modeNum = 0;
            var testMode = new Win32.DEVMODE();
            testMode.dmSize = (short)Marshal.SizeOf(typeof(Win32.DEVMODE));

            while (Win32.EnumDisplaySettings(null, modeNum, ref testMode))
            {
                if (testMode.dmPelsWidth == width && testMode.dmPelsHeight == height)
                {
                    if (testMode.dmDisplayFrequency > maxHz)
                    {
                        maxHz = testMode.dmDisplayFrequency;
                    }
                }
                modeNum++;
            }

            // If monitor supports higher refresh rate than currently configured (e.g. 60Hz configured on 144Hz monitor)
            if (maxHz > currentHz && maxHz >= 75)
            {
                return new AttentionDecisionItem
                {
                    Id = "refresh_rate",
                    Title = $"ارتقای نرخ نوسازی مانیتور از {currentHz}Hz به {maxHz}Hz",
                    Description = $"مانیتور شما با رزولوشن {width}×{height} از فرکانس بالاتر {maxHz}Hz پشتیبانی می کند که روانی و سرعت تصویر را ۲ برابر خواهد کرد.",
                    Category = "نمایشگر و مانیتور",
                    ActionText = $"ارتقا به {maxHz}Hz",
                    IconGlyph = "\uE7F4",
                    IntParameter = maxHz
                };
            }

            return null;
        }

        public static bool ApplyRefreshRate(int targetHz)
        {
            try
            {
                var mode = new Win32.DEVMODE();
                mode.dmSize = (short)Marshal.SizeOf(typeof(Win32.DEVMODE));

                if (!Win32.EnumDisplaySettings(null, Win32.ENUM_CURRENT_SETTINGS, ref mode))
                {
                    return false;
                }

                mode.dmDisplayFrequency = targetHz;
                mode.dmFields |= Win32.DM_DISPLAYFREQUENCY;

                int result = Win32.ChangeDisplaySettings(ref mode, Win32.CDS_UPDATEREGISTRY);
                return result == Win32.DISP_CHANGE_SUCCESSFUL;
            }
            catch
            {
                return false;
            }
        }
    }
}
