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

                    // 3. Disable Bing Web Search in Start Menu (makes local file & app search instantaneous)
                    using (var winSearch = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search"))
                    {
                        if (winSearch != null)
                        {
                            winSearch.SetValue("BingSearchEnabled", 0, RegistryValueKind.DWord);
                            winSearch.SetValue("CortanaConsent", 0, RegistryValueKind.DWord);
                            appliedCount += 2;
                        }
                    }

                    // 4. File Explorer Optimization (Launch to 'This PC' for faster opening, Show File Extensions for security)
                    using (var explorerAdv = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                    {
                        if (explorerAdv != null)
                        {
                            explorerAdv.SetValue("LaunchTo", 1, RegistryValueKind.DWord); // 1 = This PC
                            explorerAdv.SetValue("HideFileExt", 0, RegistryValueKind.DWord); // 0 = Always show file extensions
                            appliedCount += 2;
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
