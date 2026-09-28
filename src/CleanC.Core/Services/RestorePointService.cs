using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading.Tasks;
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
            catch { }
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
                    catch { }

                    // 2. Try native WMI
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
                            return (true, "نقطه بازگردانی ویندوز با موفقیت ثبت شد.");
                        }
                    }
                    catch { }

                    // 3. Fallback to PowerShell Checkpoint-Computer
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Enable-ComputerRestore -Drive 'C:\\' -ErrorAction SilentlyContinue; Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var ps = Process.Start(psi);
                    if (ps != null)
                    {
                        ps.WaitForExit(45000);
                        if (ps.ExitCode == 0)
                        {
                            return (true, "نقطه بازگردانی ویندوز با موفقیت ثبت شد.");
                        }
                        else
                        {
                            string err = ps.StandardError.ReadToEnd();
                            if (string.IsNullOrWhiteSpace(err)) err = ps.StandardOutput.ReadToEnd();
                            return (false, string.IsNullOrWhiteSpace(err) ? "خطا در ایجاد نقطه بازگردانی ویندوز." : err.Trim());
                        }
                    }

                    return (false, "عدم امکان اجرای دستور ساخت نقطه بازگردانی.");
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });
        }
    }
}
