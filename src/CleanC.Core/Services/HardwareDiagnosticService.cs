using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CleanC.Core.Models;
using CleanC.Core.Native;

namespace CleanC.Core.Services
{
    public class AttentionDecisionItem
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = "سخت‌افزار";
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
                catch { }

                // 2. Power Plan Optimization Check
                try
                {
                    var powerItem = PowerPlanService.CheckPowerPlanAttention();
                    if (powerItem != null) list.Add(powerItem);
                }
                catch { }

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
                            Title = $"{bloatCandidates.Count} برنامه غیرضروری، تبلیغاتی یا کم‌استفاده شناسایی شد",
                            Description = $"برنامه‌های تبلیغاتی و بلااستفاده پیش‌فرض که حدود {SystemStats.FormatBytes(totalBloatBytes)} از فضای دیسک و منابع سیستم را درگیر کرده‌اند.",
                            Category = "مدیریت نرم‌افزارها",
                            ActionText = "بررسی و حذف",
                            IconGlyph = "\uE74D",
                            IntParameter = bloatCandidates.Count
                        });
                    }
                }
                catch { }

                // 4. Advanced Network & Responsiveness Tweaks Check
                try
                {
                    var advItem = AdvancedTweakService.CheckAdvancedTweaksAttention();
                    if (advItem != null) list.Add(advItem);
                }
                catch { }

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
                    Description = $"مانیتور شما با رزولوشن {width}×{height} از فرکانس بالاتر {maxHz}Hz پشتیبانی می‌کند که روانی و سرعت تصویر را ۲ برابر خواهد کرد.",
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
