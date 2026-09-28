using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class RegistryBackupService
    {
        private static string BackupDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CleanC", "Backups");
        private static string StateFilePath => Path.Combine(BackupDir, "tweaks_applied_flag.json");

        public static bool HasAppliedTweaks()
        {
            try
            {
                return File.Exists(StateFilePath);
            }
            catch { return false; }
        }

        public static void MarkTweaksApplied()
        {
            try
            {
                Directory.CreateDirectory(BackupDir);
                File.WriteAllText(StateFilePath, JsonSerializer.Serialize(new { AppliedAt = DateTime.UtcNow }));
            }
            catch { }
        }

        public static async Task<int> RollbackAllTweaksAsync()
        {
            return await Task.Run(() =>
            {
                int revertedCount = 0;

                try
                {
                    // 1. Rollback Windows Tweaks (ContentDeliveryManager & Search)
                    using (var winSearch = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search"))
                    {
                        if (winSearch != null)
                        {
                            winSearch.SetValue("BingSearchEnabled", 1, RegistryValueKind.DWord);
                            winSearch.DeleteValue("CortanaConsent", false);
                            revertedCount++;
                        }
                    }

                    using (var searchPolicy = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\Explorer"))
                    {
                        searchPolicy?.DeleteValue("DisableSearchBoxSuggestions", false);
                        revertedCount++;
                    }

                    // 2. Rollback Advanced Network & Responsiveness
                    using (var sysProfile = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
                    {
                        if (sysProfile != null)
                        {
                            // Windows default for NetworkThrottlingIndex is 10
                            sysProfile.SetValue("NetworkThrottlingIndex", 10, RegistryValueKind.DWord);
                            // Windows default for SystemResponsiveness is 20
                            sysProfile.SetValue("SystemResponsiveness", 20, RegistryValueKind.DWord);
                            revertedCount += 2;
                        }
                    }

                    // 3. Rollback GameDVR
                    using (var gameStore = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore"))
                    {
                        gameStore?.SetValue("GameDVR_Enabled", 1, RegistryValueKind.DWord);
                        revertedCount++;
                    }
                    using (var gamePolicy = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR"))
                    {
                        gamePolicy?.DeleteValue("AllowGameDVR", false);
                        revertedCount++;
                    }

                    // 4. Rollback Desktop Timeout
                    using (var desktopKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
                    {
                        if (desktopKey != null)
                        {
                            desktopKey.SetValue("HungAppTimeout", "5000", RegistryValueKind.String);
                            desktopKey.SetValue("WaitToKillAppTimeout", "5000", RegistryValueKind.String);
                            revertedCount += 2;
                        }
                    }

                    // 5. Rollback Telemetry Policy
                    using (var tcKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection"))
                    {
                        tcKey?.DeleteValue("AllowTelemetry", false);
                        revertedCount++;
                    }

                    // 6. Rollback Browser Policies
                    using (var edgePolicy = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Edge"))
                    {
                        edgePolicy?.DeleteValue("StartupBoostEnabled", false);
                        edgePolicy?.DeleteValue("BackgroundModeEnabled", false);
                    }
                    using (var chromePolicy = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Google\Chrome"))
                    {
                        chromePolicy?.DeleteValue("BackgroundModeEnabled", false);
                    }

                    // Remove flag file
                    if (File.Exists(StateFilePath))
                    {
                        File.Delete(StateFilePath);
                    }
                }
                catch { }

                return revertedCount;
            });
        }
    }
}
