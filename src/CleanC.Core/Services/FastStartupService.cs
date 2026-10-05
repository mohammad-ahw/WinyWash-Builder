using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class FastStartupStatus
    {
        public bool IsFastStartupEnabled { get; set; }
        public bool IsHibernationEnabled { get; set; }
        public long HiberfilSizeBytes { get; set; }
        public string FormattedHiberfilSize => HiberfilSizeBytes > 0 ? SystemStats.FormatBytes(HiberfilSizeBytes) : "۰ بایت (حذف‌شده)";
    }

    public static class FastStartupService
    {
        private const string PowerKeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";

        public static FastStartupStatus GetStatus()
        {
            var status = new FastStartupStatus();

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(PowerKeyPath, false);
                if (key != null)
                {
                    var val = key.GetValue("HiberbootEnabled");
                    status.IsFastStartupEnabled = (val != null && Convert.ToInt32(val) == 1);
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            try
            {
                string sysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
                string hiberFile = Path.Combine(sysDrive, "hiberfil.sys");

                if (File.Exists(hiberFile))
                {
                    status.IsHibernationEnabled = true;
                    try
                    {
                        var fi = new FileInfo(hiberFile);
                        status.HiberfilSizeBytes = fi.Length;
                    }
                    catch
                    {
                        // اندازه واقعی خوانده نشد → صفر گزارش می‌شود، نه مقدار حدسی
                        status.HiberfilSizeBytes = 0;
                    }
                }
                else
                {
                    status.IsHibernationEnabled = false;
                    status.HiberfilSizeBytes = 0;
                }

                // بدون هیبرنیشن، Fast Startup عملاً اجرا نمی‌شود (ویندوز HiberbootEnabled را نادیده می‌گیرد)
                if (!status.IsHibernationEnabled) status.IsFastStartupEnabled = false;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return status;
        }

        public static bool SetFastStartup(bool enabled)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(PowerKeyPath);
                if (key != null)
                {
                    key.SetValue("HiberbootEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return false;
        }

        public static async Task<bool> SetHibernationAsync(bool enabled)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
                        Arguments = enabled ? "-h on" : "-h off",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var p = Process.Start(psi);
                    if (p == null) return false;

                    if (!p.WaitForExit(10000))
                    {
                        // طولانی‌تر از حد انتظار → فرآیند را متوقف کن تا جزء آویزان نماند
                        try { p.Kill(); } catch (Exception __ex)
{
    CcLog.Ignore(__ex);
}
                        return false;
                    }

                    return p.ExitCode == 0;
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                return false;
            });
        }
    }
}
