using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class PendingUpdateItem
    {
        public string Title { get; set; } = string.Empty;
        public string KbArticleId { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string FormattedSize => Models.SystemStats.FormatBytes(SizeBytes);
        public string Description { get; set; } = string.Empty;
    }

    public class UpdateStatusInfo
    {
        public bool IsPaused { get; set; }
        public string PauseExpiryDate { get; set; } = string.Empty;
        public bool IsServiceDisabled { get; set; }
        public string ServiceStatus { get; set; } = "درحال اجرا";
    }

    public static class WindowsUpdateService
    {
        private const string UxSettingsPath = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

        public static UpdateStatusInfo GetCurrentStatus()
        {
            var info = new UpdateStatusInfo();

            // 1. Check Pause Status via Registry
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(UxSettingsPath);
                if (key != null)
                {
                    string? expiryStr = key.GetValue("PauseUpdatesExpiryTime")?.ToString();
                    if (!string.IsNullOrEmpty(expiryStr) && DateTime.TryParse(expiryStr, out var expiryDate))
                    {
                        if (expiryDate > DateTime.UtcNow)
                        {
                            info.IsPaused = true;
                            info.PauseExpiryDate = expiryDate.ToLocalTime().ToString("yyyy/MM/dd");
                        }
                    }
                }
            }
            catch { }

            // 2. Check wuauserv Service Status via Registry
            try
            {
                using var sKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv");
                if (sKey != null)
                {
                    int startType = Convert.ToInt32(sKey.GetValue("Start", 3));
                    info.IsServiceDisabled = (startType == 4); // 4 = Disabled
                    info.ServiceStatus = (startType == 4) ? "غیرفعال (Disabled)" : "آماده به کار (فعال)";
                }
            }
            catch
            {
                info.ServiceStatus = "نامشخص";
            }

            return info;
        }

        public static async Task<List<PendingUpdateItem>> CheckPendingUpdatesAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<PendingUpdateItem>();

                try
                {
                    Type? sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
                    if (sessionType == null) return list;

                    dynamic? session = Activator.CreateInstance(sessionType);
                    if (session == null) return list;

                    dynamic searcher = session.CreateUpdateSearcher();
                    // Search for pending, non-installed software updates
                    dynamic searchResult = searcher.Search("IsInstalled=0 and Type='Software' and IsHidden=0");

                    int count = searchResult.Updates.Count;
                    for (int i = 0; i < count; i++)
                    {
                        dynamic update = searchResult.Updates.Item(i);
                        string title = update.Title?.ToString() ?? "آپدیت ویندوز";
                        string desc = update.Description?.ToString() ?? "";
                        long size = 0;
                        try { size = Convert.ToInt64(update.MaxDownloadSize); } catch { }

                        string kb = "";
                        try
                        {
                            int kbCount = update.KBArticleIDs.Count;
                            if (kbCount > 0)
                            {
                                kb = "KB" + update.KBArticleIDs.Item(0).ToString();
                            }
                        }
                        catch { }

                        list.Add(new PendingUpdateItem
                        {
                            Title = title,
                            KbArticleId = kb,
                            SizeBytes = size,
                            Description = desc
                        });
                    }
                }
                catch { }

                return list;
            });
        }

        public static bool PauseUpdates(int weeks = 5)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(UxSettingsPath);
                if (key == null) return false;

                DateTime startTime = DateTime.UtcNow;
                DateTime endTime = startTime.AddDays(7 * weeks);
                string startIso = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string endIso = endTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

                key.SetValue("PauseUpdatesExpiryTime", endIso, RegistryValueKind.String);
                key.SetValue("PauseFeatureUpdatesStartTime", startIso, RegistryValueKind.String);
                key.SetValue("PauseFeatureUpdatesEndTime", endIso, RegistryValueKind.String);
                key.SetValue("PauseQualityUpdatesStartTime", startIso, RegistryValueKind.String);
                key.SetValue("PauseQualityUpdatesEndTime", endIso, RegistryValueKind.String);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool ResumeUpdates()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(UxSettingsPath, true);
                if (key == null) return false;

                string[] valuesToRemove = new[]
                {
                    "PauseUpdatesExpiryTime",
                    "PauseFeatureUpdatesStartTime",
                    "PauseFeatureUpdatesEndTime",
                    "PauseQualityUpdatesStartTime",
                    "PauseQualityUpdatesEndTime"
                };

                foreach (var v in valuesToRemove)
                {
                    try { key.DeleteValue(v, false); } catch { }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool SetUpdateServiceState(bool enable)
        {
            try
            {
                int startMode = enable ? 2 : 4; // 2 = Automatic, 4 = Disabled
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv", true);
                if (key != null)
                {
                    key.SetValue("Start", startMode, RegistryValueKind.DWord);
                }

                string arg = enable ? "config wuauserv start= auto" : "config wuauserv start= disabled";
                RunCommandSilent("sc.exe", arg);

                if (enable)
                {
                    RunCommandSilent("net.exe", "start wuauserv");
                }
                else
                {
                    RunCommandSilent("net.exe", "stop wuauserv /y");
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<(bool success, long freedBytes, string message)> FixStuckUpdatesAndClearCacheAsync()
        {
            return await Task.Run(() =>
            {
                long freed = 0;

                try
                {
                    // 1. Stop services
                    RunCommandSilent("net.exe", "stop wuauserv /y");
                    RunCommandSilent("net.exe", "stop bits /y");

                    // 2. Clear SoftwareDistribution
                    string distPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution");
                    if (Directory.Exists(distPath))
                    {
                        string downloadDir = Path.Combine(distPath, "Download");
                        if (Directory.Exists(downloadDir))
                        {
                            freed += GetDirectorySize(downloadDir);
                            DeleteDirectoryContents(downloadDir);
                        }

                        string dataStore = Path.Combine(distPath, "DataStore");
                        if (Directory.Exists(dataStore))
                        {
                            freed += GetDirectorySize(dataStore);
                            DeleteDirectoryContents(dataStore);
                        }
                    }

                    // 3. Restart services
                    RunCommandSilent("net.exe", "start bits");
                    RunCommandSilent("net.exe", "start wuauserv");

                    return (true, freed, $"کش آپدیت با موفقیت تخلیه گردید و سرویس‌ها نوسازی شدند. ({Models.SystemStats.FormatBytes(freed)} آزاد شد)");
                }
                catch (Exception ex)
                {
                    // Ensure services restarted
                    RunCommandSilent("net.exe", "start wuauserv");
                    return (false, freed, $"خطا در پاکسازی: {ex.Message}");
                }
            });
        }

        private static void RunCommandSilent(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            catch { }
        }

        private static long GetDirectorySize(string path)
        {
            long size = 0;
            try
            {
                var dir = new DirectoryInfo(path);
                foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { size += file.Length; } catch { }
                }
            }
            catch { }
            return size;
        }

        private static void DeleteDirectoryContents(string path)
        {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.EnumerateFiles())
            {
                try { file.Delete(); } catch { }
            }
            foreach (var sub in dir.EnumerateDirectories())
            {
                try { sub.Delete(true); } catch { }
            }
        }
    }
}
