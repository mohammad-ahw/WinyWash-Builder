using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class RestorePointService
    {
        public static bool IsSystemRestoreAvailable()
        {
            return File.Exists(Path.Combine(Environment.SystemDirectory, "rstrui.exe"));
        }

        public static void LaunchSystemRestoreWizard()
        {
            try
            {
                var rstrui = Path.Combine(Environment.SystemDirectory, "rstrui.exe");
                if (File.Exists(rstrui))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = rstrui,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        public static async Task<(bool Success, string Message)> CreateRestorePointAsync(string description = "CleanC Optimization Point")
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 1. Remove 24h frequency limit so restore points can be created on demand
                    try
                    {
                        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
                        key?.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }

                    // 2. نقطه بازگردانی فقط از طریق WMI ساخته می شود — fallback قبلی با
                    // PowerShell (-ExecutionPolicy Bypass) به دلیل الگوی مشکوک برای آنتی ویروس حذف شد
                    try
                    {
                        var scope = new ManagementScope(@"\\localhost\root\default");
                        scope.Connect();
                        var path = new ManagementPath("SystemRestore");
                        using var mc = new ManagementClass(scope, path, null);
                        using var inParams = mc.GetMethodParameters("CreateRestorePoint");
                        inParams["Description"] = description;
                        inParams["RestorePointType"] = 12; // MODIFY_SETTINGS
                        inParams["EventType"] = 100; // BEGIN_SYSTEM_CHANGE

                        using var outParams = mc.InvokeMethod("CreateRestorePoint", inParams, null);
                        int returnCode = Convert.ToInt32(outParams["ReturnValue"]);
                        if (returnCode == 0)
                        {
                            PruneOldRestorePoints(2);
                            return (true, LocalizationService.Get("نقطه بازگردانی ویندوز با موفقیت ثبت شد (نسخه های قدیمی تر به صورت خودکار پاکسازی شدند)."));
                        }

                        return (false, string.Format(LocalizationService.Get("ویندوز ساخت نقطه بازگردانی را رد کرد (کد {0}) — محافظت از سیستم (System Protection) را فعال کنید."), returnCode));
                    }
                    catch (Exception wmiEx)
                    {
                        CcLog.Ignore(wmiEx, "RestorePoint.WMI");
                        return (false, LocalizationService.Get("ساخت نقطه بازگردانی ناموفق بود: ") + wmiEx.Message);
                    }
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });
        }

        public static void PruneOldRestorePoints(int maxToKeep = 2)
        {
            try
            {
                // Method 1: Delete via WMI Win32_ShadowCopy (fast & native)
                using var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT * FROM Win32_ShadowCopy");
                var copies = new System.Collections.Generic.List<(DateTime Date, ManagementObject Mo)>();
                foreach (ManagementObject mo in searcher.Get())
                {
                    string dateStr = mo["InstallDate"]?.ToString() ?? "";
                    try
                    {
                        var dt = ManagementDateTimeConverter.ToDateTime(dateStr);
                        copies.Add((dt, mo));
                    }
                    catch
                    {
                        copies.Add((DateTime.MinValue, mo));
                    }
                }

                if (copies.Count > maxToKeep)
                {
                    var oldestToDelete = System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderBy(copies, c => c.Date), copies.Count - maxToKeep);
                    foreach (var item in oldestToDelete)
                    {
                        try
                        {
                            item.Mo.Delete();
                        }
                        catch (Exception __ex)
                        {
                            CcLog.Ignore(__ex);
                        }
                    }
                }
            }
            catch (Exception __ex)
            {
                // fallback قبلی (PowerShell + Bypass) حذف شد؛ اگر WMI در دسترس نباشد
                // پاکسازی نقطه های قدیمی انجام نمی شود و فقط لاگ ثبت می شود
                CcLog.Ignore(__ex, "RestorePoint.Prune");
            }
        }
    }
}
