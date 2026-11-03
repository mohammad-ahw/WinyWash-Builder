using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class PendingUpdateItem
    {
        public string Title { get; set; } = string.Empty;
        public string KbArticleId { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string FormattedSize => SizeBytes > 0 ? Models.SystemStats.FormatBytes(SizeBytes) : LocalizationService.Get("حجم متغیر / آنلاین");
        public string Description { get; set; } = string.Empty;
        public bool IsOptional { get; set; }
        public bool IsNextGenFeatureUpdate { get; set; }
        public string CategoryName { get; set; } = LocalizationService.Get("آپدیت کیفی و امنیتی");
        public string BuildFlightVersion { get; set; } = string.Empty;
        public string SupportUrl { get; set; } = string.Empty;
    }

    public class UpdateStatusInfo
    {
        public bool IsPaused { get; set; }
        public string PauseExpiryDate { get; set; } = string.Empty;
        public bool IsServiceDisabled { get; set; }
        public string ServiceStatus { get; set; } = LocalizationService.Get("درحال اجرا");
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
                            info.PauseExpiryDate = expiryDate.ToLocalTime().ToString("yyyy.MM.dd");
                        }
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // 2. Check wuauserv Service Status via Registry
            try
            {
                using var sKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv");
                if (sKey != null)
                {
                    int startType = Convert.ToInt32(sKey.GetValue("Start", 3));
                    info.IsServiceDisabled = (startType == 4); // 4 = Disabled
                    info.ServiceStatus = (startType == 4) ? LocalizationService.Get("غیرفعال (Disabled)") : LocalizationService.Get("آماده به کار (فعال)");
                }
            }
            catch
            {
                info.ServiceStatus = LocalizationService.Get("نامشخص");
            }

            return info;
        }

        public static async Task<List<PendingUpdateItem>> CheckPendingUpdatesAsync()
        {
            return await Task.Run(async () =>
            {
                var list = new List<PendingUpdateItem>();
                var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // -------------------------------------------------------------
                // 1. Check Modern Windows 11 Update Orchestrator (USO store.db)
                //    This detects Next-Gen Feature Upgrades & Seeker Updates (e.g. Windows 11 version 26H2 / Insider flights)
                // -------------------------------------------------------------
                try
                {
                    var usoUpdates = await GetUsoPendingUpdatesAsync();
                    foreach (var u in usoUpdates)
                    {
                        if (seenTitles.Add(u.Title))
                        {
                            list.Add(u);
                        }
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // -------------------------------------------------------------
                // 2. Check Windows Update Agent (WUA COM API) with 12s timeout
                // -------------------------------------------------------------
                try
                {
                    var comSearchTask = Task.Run(() =>
                    {
                        var comList = new List<PendingUpdateItem>();
                        try
                        {
                            Type? sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
                            if (sessionType != null)
                            {
                                dynamic? session = Activator.CreateInstance(sessionType);
                                if (session != null)
                                {
                                    dynamic searcher = session.CreateUpdateSearcher();

                                    // 2A. Search Mandatory
                                    try
                                    {
                                        dynamic searchResult = searcher.Search("IsInstalled=0 and IsHidden=0 and BrowseOnly=0");
                                        int count = searchResult.Updates.Count;
                                        for (int i = 0; i < count; i++)
                                        {
                                            dynamic update = searchResult.Updates.Item(i);
                                            string title = update.Title?.ToString() ?? LocalizationService.Get("آپدیت ویندوز");
                                            string desc = update.Description?.ToString() ?? "";
                                            long size = 0;
                                            try { size = Convert.ToInt64(update.MaxDownloadSize); } catch (Exception __ex) { CcLog.Ignore(__ex); }
                                            string kb = "";
                                            try
                                            {
                                                if (update.KBArticleIDs.Count > 0)
                                                    kb = "KB" + update.KBArticleIDs.Item(0).ToString();
                                            } catch (Exception __ex) { CcLog.Ignore(__ex); }

                                            bool isNextGen = title.Contains("Windows 11, version", StringComparison.OrdinalIgnoreCase) ||
                                                             title.Contains("Feature update", StringComparison.OrdinalIgnoreCase);

                                            comList.Add(new PendingUpdateItem
                                            {
                                                Title = title,
                                                KbArticleId = kb,
                                                SizeBytes = size,
                                                Description = desc,
                                                IsOptional = false,
                                                IsNextGenFeatureUpdate = isNextGen,
                                                CategoryName = isNextGen ? LocalizationService.Get("ارتقای نسل سیستم عامل") : LocalizationService.Get("آپدیت کیفی و امنیتی")
                                            });
                                        }
                                    }
                                    catch (Exception __ex) { CcLog.Ignore(__ex); }

                                    // 2B. Search Optional
                                    try
                                    {
                                        dynamic optResult = searcher.Search("IsInstalled=0 and IsHidden=0 and BrowseOnly=1");
                                        int countOpt = optResult.Updates.Count;
                                        for (int i = 0; i < countOpt; i++)
                                        {
                                            dynamic update = optResult.Updates.Item(i);
                                            string title = update.Title?.ToString() ?? LocalizationService.Get("آپدیت اختیاری ویندوز");
                                            string desc = update.Description?.ToString() ?? "";
                                            long size = 0;
                                            try { size = Convert.ToInt64(update.MaxDownloadSize); } catch (Exception __ex) { CcLog.Ignore(__ex); }
                                            string kb = "";
                                            try
                                            {
                                                if (update.KBArticleIDs.Count > 0)
                                                    kb = "KB" + update.KBArticleIDs.Item(0).ToString();
                                            } catch (Exception __ex) { CcLog.Ignore(__ex); }

                                            bool isDriver = false;
                                            try { isDriver = (Convert.ToInt32(update.Type) == 2); } catch (Exception __ex) { CcLog.Ignore(__ex); }
                                            bool isNextGen = title.Contains("Windows 11, version", StringComparison.OrdinalIgnoreCase) ||
                                                             title.Contains("Feature update", StringComparison.OrdinalIgnoreCase);

                                            comList.Add(new PendingUpdateItem
                                            {
                                                Title = title,
                                                KbArticleId = kb,
                                                SizeBytes = size,
                                                Description = desc,
                                                IsOptional = true,
                                                IsNextGenFeatureUpdate = isNextGen,
                                                CategoryName = isNextGen ? LocalizationService.Get("ارتقای نسل جدید (اختیاری)") : (isDriver ? LocalizationService.Get("درایور سخت افزار (اختیاری)") : LocalizationService.Get("آپدیت اختیاری ویندوز"))
                                            });
                                        }
                                    }
                                    catch (Exception __ex) { CcLog.Ignore(__ex); }
                                }
                            }
                        }
                        catch (Exception __ex)
                        {
                            CcLog.Ignore(__ex);
                        }
                        return comList;
                    });

                    if (await Task.WhenAny(comSearchTask, Task.Delay(12000)) == comSearchTask)
                    {
                        var comUpdates = await comSearchTask;
                        foreach (var u in comUpdates)
                        {
                            if (seenTitles.Add(u.Title))
                                list.Add(u);
                        }
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                return list;
            });
        }

        private static async Task<List<PendingUpdateItem>> GetUsoPendingUpdatesAsync()
        {
            var results = new List<PendingUpdateItem>();
            string storePath = @"C:\ProgramData\USOPrivate\UpdateStore\store.db";

            if (!File.Exists(storePath)) return results;

            string tempFile = Path.Combine(Path.GetTempPath(), $"cleanc_uso_{Guid.NewGuid():N}.db");
            try
            {
                // Safely copy database with FileShare.ReadWrite to avoid locking active USO service
                using (var src = new FileStream(storePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var dst = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await src.CopyToAsync(dst);
                }

                using var conn = new SqliteConnection($"Data Source={tempFile};Mode=ReadOnly;");
                await conn.OpenAsync();

                // 1. Collect all completed/installed update IDs
                var completedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using var compCmd = conn.CreateCommand();
                    compCmd.CommandText = "SELECT UPDATEID FROM COMPLETEDUPDATES";
                    using var compReader = await compCmd.ExecuteReaderAsync();
                    while (await compReader.ReadAsync())
                    {
                        if (!compReader.IsDBNull(0))
                        {
                            completedIds.Add(compReader.GetString(0));
                        }
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // 2. Inspect UPDATES table for modern seeker/feature updates
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT UPDATEID, SERIALIZEDUPDATE FROM UPDATES";
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        string updateId = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        if (string.IsNullOrEmpty(updateId) || completedIds.Contains(updateId) || completedIds.Any(c => c.StartsWith(updateId, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        if (reader.IsDBNull(1)) continue;

                        byte[] blob = (byte[])reader[1];
                        var item = ParseUsoSerializedUpdate(blob, updateId);
                        if (item != null)
                        {
                            results.Add(item);
                        }
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }
            }

            return results;
        }

        private static PendingUpdateItem? ParseUsoSerializedUpdate(byte[] blob, string updateId)
        {
            try
            {
                string text16 = Encoding.Unicode.GetString(blob);
                string text8 = Encoding.UTF8.GetString(blob);

                // 1. Extract BuildFlight metadata from JSON (e.g. {"BuildFlightID":"NG:25E275", "BuildFlightVersion":"10.0.26300.9457"})
                string buildFlightId = "";
                string buildFlightVer = "";

                var mFlight = Regex.Match(text8, @"\{\s*""BuildFlightID""\s*:\s*""([^""]+)""\s*,\s*""BuildFlightVersion""\s*:\s*""([^""]+)""");
                if (!mFlight.Success)
                {
                    // Also try finding in text16
                    mFlight = Regex.Match(text16, @"\{\s*""BuildFlightID""\s*:\s*""([^""]+)""\s*,\s*""BuildFlightVersion""\s*:\s*""([^""]+)""");
                }

                if (mFlight.Success)
                {
                    buildFlightId = mFlight.Groups[1].Value;
                    buildFlightVer = mFlight.Groups[2].Value;
                }

                // 2. Extract Candidate Title from text16 lines
                string title = "";
                string desc = "";

                var parts = text16.Split('\0');
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim();
                    if (p.Length < 4) continue;

                    if ((p.Contains("Windows 11", StringComparison.OrdinalIgnoreCase) || p.Contains("Windows 10", StringComparison.OrdinalIgnoreCase)) &&
                        p.Contains("version", StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrEmpty(title))
                    {
                        // Clean up if it starts with "Install the latest version of Windows: "
                        if (p.StartsWith("Install the latest version of Windows: ", StringComparison.OrdinalIgnoreCase))
                        {
                            title = p.Substring("Install the latest version of Windows: ".Length).TrimEnd('.');
                            desc = p;
                        }
                        else
                        {
                            title = p.TrimEnd('.');
                        }
                    }
                }

                if (string.IsNullOrEmpty(title))
                {
                    if (!string.IsNullOrEmpty(buildFlightVer))
                    {
                        title = string.Format(LocalizationService.Get("بروزرسانی نسل جدید ویندوز ۱۱ (بیلد {0})"), buildFlightVer);
                    }
                    else
                    {
                        return null; // Not a valid update entry
                    }
                }

                if (string.IsNullOrEmpty(desc))
                {
                    // صادقانه: توضیح تبلیغاتیِ جعلی (قابلیت هوش مصنوعی و...) نمی سازیم؛
                    // اگر مایکروسافت توضیحی نفرستد، همان را می گوییم.
                    desc = LocalizationService.Get("توضیح رسمی از مایکروسافت منتشر نشده است");
                }

                string kbId = !string.IsNullOrEmpty(buildFlightId) ? string.Format(LocalizationService.Get("نسل بعد ({0})"), buildFlightId) : LocalizationService.Get("نسل جدید اختیاری");

                return new PendingUpdateItem
                {
                    Title = title,
                    KbArticleId = kbId,
                    Description = desc,
                    IsOptional = true,
                    IsNextGenFeatureUpdate = true,
                    CategoryName = LocalizationService.Get("ارتقای نسل سیستم عامل (Next-Gen OS Upgrade)"),
                    BuildFlightVersion = buildFlightVer,
                    SupportUrl = "https://go.microsoft.com/fwlink/?linkid=2132062#windowsupdate=26100"
                };
            }
            catch
            {
                return null;
            }
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
                    try { key.DeleteValue(v, false); } catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
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
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv", true))
                {
                    key?.SetValue("Start", startMode, RegistryValueKind.DWord);
                }

                // جایگزین sc.exe / net.exe — از API مدیریت سرویس ویندوز استفاده می شود
                // تا زنجیره ی فرمان های مشکوک برای آنتی ویروس حذف شود
                if (enable)
                {
                    StartService("wuauserv");
                }
                else
                {
                    StopService("wuauserv");
                }

                return true;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex, "WindowsUpdate.SetServiceState");
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
                    // 1. توقف سرویس ها از طریق ServiceController (جایگزین net.exe stop)
                    StopService("wuauserv");
                    StopService("bits");

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

                    // 3. روشن کردن مجدد سرویس ها
                    StartService("bits");
                    StartService("wuauserv");

                    return (true, freed, string.Format(LocalizationService.Get("کش آپدیت با موفقیت تخلیه گردید و سرویس ها نوسازی شدند. ({0} آزاد شد)"), Models.SystemStats.FormatBytes(freed)));
                }
                catch (Exception ex)
                {
                    // Ensure services restarted
                    StartService("wuauserv");
                    return (false, freed, string.Format(LocalizationService.Get("خطا در پاکسازی: {0}"), ex.Message));
                }
            });
        }

        /// <summary>توقف سرویس ویندوز با API مدیریت سرویس (جایگزین net.exe stop).</summary>
        private static void StopService(string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex, $"WindowsUpdate.StopService.{serviceName}");
            }
        }

        /// <summary>روشن کردن سرویس ویندوز با API مدیریت سرویس (جایگزین net.exe start).</summary>
        private static void StartService(string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex, $"WindowsUpdate.StartService.{serviceName}");
            }
        }

        private static long GetDirectorySize(string path)
        {
            long size = 0;
            try
            {
                var dir = new DirectoryInfo(path);
                foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { size += file.Length; } catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return size;
        }

        private static void DeleteDirectoryContents(string path)
        {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.EnumerateFiles())
            {
                try { file.Delete(); } catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }
            }
            foreach (var sub in dir.EnumerateDirectories())
            {
                try { sub.Delete(true); } catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }
            }
        }
    }
}
