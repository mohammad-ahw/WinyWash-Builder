using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class TelemetryOptimizerService
    {
        public static async Task<int> DisableTelemetryAndDiagTrackAsync()
        {
            return await Task.Run(() =>
            {
                int appliedCount = 0;

                // 1. Stop and disable DiagTrack (Connected User Experiences and Telemetry)
                if (StopAndDisableService("DiagTrack")) appliedCount++;

                // 2. Stop and disable dmwappushservice (WAP Push Message Routing Service)
                if (StopAndDisableService("dmwappushservice")) appliedCount++;

                // 3. Configure Windows Data Collection Policies in Registry
                try
                {
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection"))
                    {
                        if (key != null)
                        {
                            key.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord);
                            appliedCount++;
                        }
                    }
                }
                catch { }

                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Privacy"))
                    {
                        if (key != null)
                        {
                            key.SetValue("TailoredExperiencesWithDiagnosticDataEnabled", 0, RegistryValueKind.DWord);
                            appliedCount++;
                        }
                    }
                }
                catch { }

                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Siuf\Rules"))
                    {
                        if (key != null)
                        {
                            key.SetValue("NumberOfSIUFInPeriod", 0, RegistryValueKind.DWord);
                            appliedCount++;
                        }
                    }
                }
                catch { }

                return appliedCount;
            });
        }

        private static bool StopAndDisableService(string serviceName)
        {
            try
            {
                // Set start= disabled
                var psiConfig = new ProcessStartInfo("sc.exe", $"config {serviceName} start= disabled")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p1 = Process.Start(psiConfig);
                p1?.WaitForExit(3000);

                // Stop service if currently running
                var psiStop = new ProcessStartInfo("sc.exe", $"stop {serviceName}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p2 = Process.Start(psiStop);
                p2?.WaitForExit(3000);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
