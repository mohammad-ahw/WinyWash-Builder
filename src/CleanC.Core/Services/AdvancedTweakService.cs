using System;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class AdvancedTweakService
    {
        public static async Task<int> ApplyAdvancedTweaksAsync()
        {
            return await Task.Run(() =>
            {
                int count = 0;

                try
                {
                    // 1. Network Throttling Index & System Responsiveness
                    using (var sysProfile = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
                    {
                        if (sysProfile != null)
                        {
                            // 0xffffffff (4294967295) = Disable network throttling during media/gaming
                            sysProfile.SetValue("NetworkThrottlingIndex", unchecked((int)0xffffffff), RegistryValueKind.DWord);
                            // 0 = Dedicate 100% CPU to foreground applications instead of reserving 20% for background
                            sysProfile.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
                            count += 2;
                        }
                    }

                    // 2. Disable GameDVR Background Recording (eliminates silent FPS drops and GPU latency)
                    using (var gameStore = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore"))
                    {
                        if (gameStore != null)
                        {
                            gameStore.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                            count++;
                        }
                    }

                    using (var gamePolicy = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR"))
                    {
                        if (gamePolicy != null)
                        {
                            gamePolicy.SetValue("AllowGameDVR", 0, RegistryValueKind.DWord);
                            count++;
                        }
                    }

                    // 3. App Responsiveness & Quick Hung App Handling
                    using (var desktopKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
                    {
                        if (desktopKey != null)
                        {
                            // Reduce freeze wait time from 5000ms to 2000ms
                            desktopKey.SetValue("HungAppTimeout", "2000", RegistryValueKind.String);
                            desktopKey.SetValue("WaitToKillAppTimeout", "2000", RegistryValueKind.String);
                            count += 2;
                        }
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                return count;
            });
        }

        public static AttentionDecisionItem? CheckAdvancedTweaksAttention()
        {
            try
            {
                bool needsTweak = false;

                using (var sysProfile = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
                {
                    if (sysProfile != null)
                    {
                        var netThrottle = sysProfile.GetValue("NetworkThrottlingIndex");
                        if (netThrottle == null || Convert.ToInt64(netThrottle) != unchecked((int)0xffffffff))
                        {
                            needsTweak = true;
                        }
                    }
                    else
                    {
                        needsTweak = true;
                    }
                }

                if (needsTweak)
                {
                    return new AttentionDecisionItem
                    {
                        Id = "advanced_tweaks",
                        Title = "بهینه سازی پیشرفته شبکه و پاسخ دهی سیستم (Advanced Tweaks)",
                        Description = "حذف محدودیت عمدی ویندوز برای پکت های شبکه، غیرفعال سازی GameDVR و افزایش اولویت پردازشی برنامه ها.",
                        Category = "شبکه و کارایی",
                        ActionText = "اعمال بهینه سازی ها",
                        IconGlyph = "\uE9D2"
                    };
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return null;
        }
    }
}
