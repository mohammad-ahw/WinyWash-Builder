using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace CleanC.Core.Services
{
    public class DiskMaintenanceService
    {
        public static async Task<bool> OptimizeDriveTrimAsync(string driveLetter = "C")
        {
            return await Task.Run(() =>
            {
                try
                {
                    string cleanLetter = driveLetter.TrimEnd(':', '\\', '/');
                    var psi = new ProcessStartInfo
                    {
                        FileName = Path.Combine(Environment.SystemDirectory, "defrag.exe"),
                        Arguments = $"{cleanLetter}: /O /U",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        process.WaitForExit(60000); // 1 minute max
                        return process.ExitCode == 0;
                    }
                }
                catch { }

                return false;
            });
        }
    }
}
