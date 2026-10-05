using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;

namespace CleanC.Core.Services
{
    public class ProcessManagerService
    {
        private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "csrss", "smss", "wininit", "services", "lsass", "svchost", "dwm", "explorer", "winlogon"
        };

        public static List<ProcessInfo> GetActiveProcesses(int topCount = 50)
        {
            var list = new List<ProcessInfo>();
            var processes = Process.GetProcesses();

            foreach (var p in processes)
            {
                try
                {
                    if (p.Id > 4 && !p.HasExited)
                    {
                        list.Add(new ProcessInfo
                        {
                            Id = p.Id,
                            Name = p.ProcessName,
                            MemoryBytes = p.WorkingSet64,
                            IsCritical = CriticalProcesses.Contains(p.ProcessName)
                        });
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }
                finally
                {
                    p.Dispose();
                }
            }

            return list.OrderByDescending(p => p.MemoryBytes).Take(topCount).ToList();
        }

        public static bool KillProcess(int pid)
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                if (CriticalProcesses.Contains(proc.ProcessName))
                    return false;

                proc.Kill();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static readonly HashSet<string> SafeBackgroundHelpers = new(StringComparer.OrdinalIgnoreCase)
        {
            "GoogleUpdate", "MicrosoftEdgeUpdate", "AdobeUpdateService", "OneDriveStandaloneUpdater", "jusched"
        };

        public static int CleanBackgroundHelpers()
        {
            int closedCount = 0;
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    if (p.Id > 4 && !p.HasExited && SafeBackgroundHelpers.Contains(p.ProcessName))
                    {
                        // Check that it does not have an active visible window
                        if (string.IsNullOrEmpty(p.MainWindowTitle))
                        {
                            p.Kill();
                            closedCount++;
                        }
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }
                finally
                {
                    p.Dispose();
                }
            }
            return closedCount;
        }
    }
}
