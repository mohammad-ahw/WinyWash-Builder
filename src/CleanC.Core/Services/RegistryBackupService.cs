using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class RegistryBackupService
    {
        private const string FlagFileName = "tweaks_applied_flag.json";

        /// <summary>
        /// مسیر اصلی (جدید): %ProgramData%\WinyWash\Backups
        /// دلیل جابجایی: %LOCALAPPDATA% داخل مسیر اسکن پاک سازهاست؛
        /// نگه داشتن پرچم و بکاپ ها آنجا یعنی ممکن است توسط خود برنامه پاک شوند
        /// و «بازگردانی تغییرات» از کار بیفتد. ProgramData سراسری است و
        /// عمری برابر با نصب نرم افزار دارد.
        /// </summary>
        private static readonly string PreferredBackupDir = AppPaths.CommonBackupsDir;

        /// <summary>مسیر قدیمی — فقط برای مهاجرت و پشتیبانی خواندن رکوردهای موجود.</summary>
        private static readonly string LegacyBackupDir = AppPaths.LocalBackupsDir;

        private static string? _resolvedBackupDir;

        private static string ResolveBackupDir()
        {
            if (_resolvedBackupDir != null) return _resolvedBackupDir;

            try
            {
                Directory.CreateDirectory(PreferredBackupDir);
                MigrateLegacyFiles(LegacyBackupDir, PreferredBackupDir);
                _resolvedBackupDir = PreferredBackupDir;
            }
            catch
            {
                // بدون دسترسی Administrator یا ProgramData در دسترس نبود
                _resolvedBackupDir = LegacyBackupDir;
            }

            return _resolvedBackupDir;
        }

        /// <summary>
        /// انتقال یک باره ی فایل های مسیر قدیمی به مسیر جدید (بدون حذف منبع).
        /// </summary>
        private static void MigrateLegacyFiles(string from, string to)
        {
            try
            {
                if (!Directory.Exists(from)) return;
                foreach (var file in Directory.GetFiles(from))
                {
                    string dest = Path.Combine(to, Path.GetFileName(file));
                    if (File.Exists(dest)) continue;
                    File.Copy(file, dest, false);
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private static void TryDeleteFlag(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        public static void MarkTweaksApplied()
        {
            try
            {
                string dir = ResolveBackupDir();
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, FlagFileName),
                    JsonSerializer.Serialize(new { AppliedAt = DateTime.UtcNow }));
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
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

                    // Remove flag file (هر دو مسیر — مهاجرتِ ناقص را هم جبران می کند)
                    TryDeleteFlag(Path.Combine(ResolveBackupDir(), FlagFileName));
                    TryDeleteFlag(Path.Combine(LegacyBackupDir, FlagFileName));
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                return revertedCount;
            });
        }
    }
}
