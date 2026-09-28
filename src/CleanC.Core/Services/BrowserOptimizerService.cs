using System;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class BrowserOptimizerService
    {
        public static async Task<int> SuppressBrowserBackgroundModeAsync()
        {
            return await Task.Run(() =>
            {
                int appliedCount = 0;

                try
                {
                    // 1. Microsoft Edge Background & Startup Boost Suppression
                    using (var edgePolicy = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Edge"))
                    {
                        if (edgePolicy != null)
                        {
                            edgePolicy.SetValue("StartupBoostEnabled", 0, RegistryValueKind.DWord);
                            edgePolicy.SetValue("BackgroundModeEnabled", 0, RegistryValueKind.DWord);
                            appliedCount += 2;
                        }
                    }

                    // 2. Google Chrome Background Mode Suppression
                    using (var chromePolicy = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Google\Chrome"))
                    {
                        if (chromePolicy != null)
                        {
                            chromePolicy.SetValue("BackgroundModeEnabled", 0, RegistryValueKind.DWord);
                            appliedCount++;
                        }
                    }
                }
                catch
                {
                    // Ignore permission boundaries
                }

                return appliedCount;
            });
        }
    }
}
