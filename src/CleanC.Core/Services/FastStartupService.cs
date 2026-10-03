using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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
            catch { }

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
                        // File locked by system, estimate typical size if cannot read directly
                        status.HiberfilSizeBytes = 6L * 1024 * 1024 * 1024; // ~6 GB baseline
                    }
                }
                else
                {
                    status.IsHibernationEnabled = false;
                    status.HiberfilSizeBytes = 0;
                }
            }
            catch { }

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
            catch { }

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
                    if (p != null)
                    {
                        p.WaitForExit(10000);
                        return p.ExitCode == 0;
                    }
                }
                catch { }

                return false;
            });
        }
    }
}
