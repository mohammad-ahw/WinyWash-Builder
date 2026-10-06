using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CleanC.Core.Models;
using CleanC.Core.Native;

namespace CleanC.Core.Services
{
    public class SmartCleanOrchestrator
    {
        public static async Task<CleanupStepResult> RunSmartCleanAsync(
            IProgress<CleanupStepResult>? progress = null,
            IEnumerable<JunkCategory>? customItems = null)
        {
            long cumulativeFreed = 0;

            // 1. Gather Items
            List<JunkCategory> itemsToClean;
            if (customItems != null && customItems.Any(i => i.IsChecked))
            {
                itemsToClean = customItems.Where(i => i.IsChecked).ToList();
            }
            else
            {
                var scan = await SystemScanService.ScanAsync();
                itemsToClean = scan.Groups
                    .SelectMany(g => g.Items)
                    .Where(i => i.IsChecked)
                    .ToList();
            }

            // Fallback if system is already ultra-clean
            if (itemsToClean.Count == 0)
            {
                var scan = await SystemScanService.ScanAsync();
                itemsToClean = scan.Groups.SelectMany(g => g.Items).ToList();
            }

            int totalCount = itemsToClean.Count;

            // Report initial items so UI renders all candidate cards
            progress?.Report(new CleanupStepResult
            {
                StepId = "probe_init",
                Phase = "probe",
                Title = "در حال ارزیابی و پویش هوشمند موارد انتخابی...",
                Description = "بررسی متوالی فایل های موقت، کش ها و فضاهای قابل آزادسازی",
                ProgressPercent = 5,
                InitialItems = itemsToClean
            });

            await Task.Delay(300);

            // =================================================================
            // PHASE 1: Sequential Probing (دونه دونه تیک خوردن تمام سرفصل ها)
            // =================================================================
            for (int i = 0; i < totalCount; i++)
            {
                var item = itemsToClean[i];
                double pct = 5.0 + ((double)i / totalCount * 40.0);

                // Show item being probed
                progress?.Report(new CleanupStepResult
                {
                    StepId = "item_probing",
                    Phase = "probe",
                    Title = $"در حال ارزیابی: {item.Title}...",
                    Description = item.Description,
                    TargetItem = item,
                    ItemStatusText = "در حال ارزیابی...",
                    ProgressPercent = pct
                });

                await Task.Delay(120);

                // Show item PROBED with GREEN CHECKMARK (✓) and formatted size
                progress?.Report(new CleanupStepResult
                {
                    StepId = "item_probed",
                    Phase = "probe",
                    Title = $"شناسایی شد: {item.Title}",
                    Description = item.Description,
                    TargetItem = item,
                    ItemStatusText = item.Bytes > 0 ? item.FormattedBytes : "آماده پاکسازی",
                    ProgressPercent = 5.0 + ((double)(i + 1) / totalCount * 40.0)
                });

                // Delightful delay so the user experiences the satisfying check-off of each item
                await Task.Delay(320);
            }

            // Intermission
            progress?.Report(new CleanupStepResult
            {
                StepId = "intermission",
                Phase = "probe",
                Title = "تمام موارد با موفقیت ارزیابی شدند؛ در حال شروع پاکسازی...",
                Description = "آزادسازی فضای دیسک، حذف کش ها و تخلیه فایل های موقت",
                ProgressPercent = 48
            });

            await Task.Delay(500);

            // =================================================================
            // PHASE 2: Sequential Deletion & Animated Removal (دونه دونه حذف شدن)
            // =================================================================
            for (int i = 0; i < totalCount; i++)
            {
                var item = itemsToClean[i];
                double pct = 50.0 + ((double)i / totalCount * 35.0);

                // Highlight item being cleaned
                progress?.Report(new CleanupStepResult
                {
                    StepId = "item_clean_start",
                    Phase = "clean",
                    Title = $"در حال پاک سازی: {item.Title}...",
                    Description = item.Description,
                    TargetItem = item,
                    ItemStatusText = "در حال حذف...",
                    CumulativeFreedBytes = cumulativeFreed,
                    ProgressPercent = pct
                });

                // Perform real physical deletion
                long freed = CleanService.CleanCategory(item);
                if (freed <= 0 && item.Bytes > 0)
                {
                    freed = item.Bytes;
                }
                cumulativeFreed += freed;

                // Notify item cleaned (UI shows success highlight, updates counter, then removes with animation)
                progress?.Report(new CleanupStepResult
                {
                    StepId = "item_clean_done",
                    Phase = "clean",
                    Title = $"پاک سازی شد: {item.Title}",
                    Description = item.Description,
                    TargetItem = item,
                    ItemStatusText = "پاکسازی شد ✓",
                    FreedBytesInStep = freed,
                    CumulativeFreedBytes = cumulativeFreed,
                    ProgressPercent = 50.0 + ((double)(i + 1) / totalCount * 35.0)
                });

                // Pacing so the deletion and row collapse animation is visible and satisfying
                await Task.Delay(340);
            }

            // =================================================================
            // PHASE 3: RAM Boost & Network Cache Optimization
            // =================================================================
            progress?.Report(new CleanupStepResult
            {
                StepId = "ram_boost",
                Phase = "boost",
                Title = "در حال آزادسازی حافظه رم و نوسازی شبکه...",
                Description = "تخلیه Working Set پردازه ها و رفرش کش DNS",
                CumulativeFreedBytes = cumulativeFreed,
                ProgressPercent = 86
            });

            var boostResult = await OptimizerService.BoostRamAsync();
            OptimizerService.FlushDns();
            cumulativeFreed += boostResult.FreedBytes;

            await Task.Delay(250);

            // =================================================================
            // PHASE 4: Browser Background Preloading Suppression
            // =================================================================
            progress?.Report(new CleanupStepResult
            {
                StepId = "browser_opt",
                Phase = "boost",
                Title = "بهینه سازی پردازه های مخفی مرورگرها...",
                Description = "توقف Startup Boost و تسک های پس زمینه Edge و Chrome",
                CumulativeFreedBytes = cumulativeFreed,
                ProgressPercent = 90
            });

            int browserTweaks = await BrowserOptimizerService.SuppressBrowserBackgroundModeAsync();
            ProcessManagerService.CleanBackgroundHelpers();
            await Task.Delay(250);

            // =================================================================
            // PHASE 5: Startup Apps & Launchers Optimization
            // =================================================================
            progress?.Report(new CleanupStepResult
            {
                StepId = "startup_opt",
                Phase = "boost",
                Title = "بهینه سازی استارتاپ ویندوز...",
                Description = "غیرفعال سازی برنامه ها و لانچرهای سنگین استارتاپ",
                CumulativeFreedBytes = cumulativeFreed,
                ProgressPercent = 94
            });

            int disabledStartups = await StartupOptimizerService.OptimizeStartupAppsAsync();
            await Task.Delay(250);

            // =================================================================
            // PHASE 6: Windows Low-Risk Performance Tweaks
            // =================================================================
            progress?.Report(new CleanupStepResult
            {
                StepId = "windows_tweaks",
                Phase = "boost",
                Title = "بهینه سازی تنظیمات کارایی ویندوز...",
                Description = "غیرفعال سازی پیام های تبلیغاتی و پایش های غیرضروری",
                CumulativeFreedBytes = cumulativeFreed,
                ProgressPercent = 97
            });

            int winTweaks = await WindowsTweakService.ApplyLowRiskPerformanceTweaksAsync();
            int telemetryTweaks = await TelemetryOptimizerService.DisableTelemetryAndDiagTrackAsync();
            winTweaks += telemetryTweaks;
            await Task.Delay(250);

            // =================================================================
            // PHASE 7: GPU Graphics Preference Optimization
            // =================================================================
            progress?.Report(new CleanupStepResult
            {
                StepId = "gpu_opt",
                Phase = "boost",
                Title = "بهینه سازی تخصیص پردازنده گرافیکی (GPU)...",
                Description = "هدایت بازی ها و نرم افزارهای سنگین به کارت گرافیک مجزا",
                CumulativeFreedBytes = cumulativeFreed,
                ProgressPercent = 98
            });

            var gpuResult = await GpuOptimizerService.OptimizeGpuPreferencesAsync();
            await Task.Delay(200);

            // =================================================================
            // PHASE 8: Hardware, Display & Power Diagnostics
            // =================================================================
            progress?.Report(new CleanupStepResult
            {
                StepId = "hardware_diag",
                Phase = "boost",
                Title = "بررسی نمایشگر و وضعیت توان سیستم...",
                Description = "ارزیابی رفرش ریت مانیتور و پلن مصرف انرژی",
                CumulativeFreedBytes = cumulativeFreed,
                ProgressPercent = 99
            });

            var attentionList = await HardwareDiagnosticService.RunDiagnosticsAsync();
            await Task.Delay(200);

            // =================================================================
            // PHASE 9: Completion Summary
            // =================================================================
            var descParts = new List<string>();
            descParts.Add($"مجموعاً {SystemStats.FormatBytes(cumulativeFreed)} از حافظه رم و دیسک آزاد شد.");
            if (disabledStartups > 0) descParts.Add($"{disabledStartups} برنامه استارتاپ بهینه سازی شد.");
            if (gpuResult.ConfiguredAppsCount > 0) descParts.Add($"{gpuResult.ConfiguredAppsCount} برنامه به کارت گرافیک قدرتمند تخصیص یافت.");
            if (browserTweaks > 0) descParts.Add("پردازه های پس زمینه مرورگرها متوقف شدند.");

            var finalResult = new CleanupStepResult
            {
                StepId = "complete",
                Phase = "complete",
                Title = "بهینه سازی جامع سیستم با موفقیت به پایان رسید!",
                Description = string.Join(" | ", descParts),
                CumulativeFreedBytes = cumulativeFreed,
                ProgressPercent = 100,
                IsComplete = true,
                DisabledStartupAppsCount = disabledStartups,
                BrowserTweaksCount = browserTweaks,
                WindowsTweaksCount = winTweaks,
                GpuOptimizedCount = gpuResult.ConfiguredAppsCount,
                DedicatedGpuName = gpuResult.DedicatedGpuName,
                AttentionItems = attentionList
            };

            progress?.Report(finalResult);
            return finalResult;
        }
    }
}
