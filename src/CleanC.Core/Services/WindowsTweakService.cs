using System;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class WindowsTweakService
    {
        public static async Task<int> ApplyLowRiskPerformanceTweaksAsync()
        {
            return await Task.Run(() =>
            {
                int appliedCount = 0;

                try
                {
                    // 1. ContentDeliveryManager: Turn off advertising ID, tips and background suggestions
                    using (var cdm = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"))
                    {
                        if (cdm != null)
                        {
                            cdm.SetValue("SubscribedContent-338388Enabled", 0, RegistryValueKind.DWord);
                            cdm.SetValue("SubscribedContent-338389Enabled", 0, RegistryValueKind.DWord);
                            cdm.SetValue("SubscribedContent-353696Enabled", 0, RegistryValueKind.DWord);
                            cdm.SetValue("SubscribedContent-353698Enabled", 0, RegistryValueKind.DWord);
                            cdm.SetValue("SystemPaneSuggestionsEnabled", 0, RegistryValueKind.DWord);
                            cdm.SetValue("SoftLandingEnabled", 0, RegistryValueKind.DWord);
                            cdm.SetValue("SilentInstalledAppsEnabled", 0, RegistryValueKind.DWord);
                            appliedCount += 7;
                        }
                    }

                    // 2. Disable Search Box suggestions bloat in Start / Explorer
                    using (var search = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\Explorer"))
                    {
                        if (search != null)
                        {
                            search.SetValue("DisableSearchBoxSuggestions", 1, RegistryValueKind.DWord);
                            appliedCount++;
                        }
                    }
                }
                catch
                {
                    // Ignore non-critical registry limits
                }

                return appliedCount;
            });
        }
    }
}
