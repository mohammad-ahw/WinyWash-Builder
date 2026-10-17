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

        /// <summary>
        /// «سبک‌سازی واقعی پردازنده»: بعد از دو نمونه‌گیری از TotalProcessorTime (فاصلهٔ ۵۰۰ms —
        /// همان روشِ نمونه‌گیرِ هیرو)، پردازه‌های سنگینِ امن (غیرِ لیستِ حیاتی، خودِ برنامه) را
        /// به اولویت BelowNormal می‌برد؛ چیزی بسته نمی‌شود و کاملاً برگشت‌پذیر است، ولی زمان‌بند
        /// ویندوز فشارِ آن‌ها را واقعاً کم می‌کند. نام پرمصرف‌ترین مورد هم برمی‌گردد.
        /// </summary>
        /// <param name="minPercent">حداقل درصد مصرف CPU (نسبت به کل هسته‌ها) برای مداخله</param>
        public static (int Lowered, string TopName) EaseHighCpuProcesses(double minPercent = 12.0)
        {
            var first = new Dictionary<int, TimeSpan>();
            var list = new List<Process>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id <= 4 || p.HasExited || p.Id == Environment.ProcessId) continue;
                    first[p.Id] = p.TotalProcessorTime;
                    list.Add(p);
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                    try { p.Dispose(); } catch { }
                }
            }

            System.Threading.Thread.Sleep(500);

            int lowered = 0;
            string topName = "";
            double topPct = 0;
            int cores = Math.Max(1, Environment.ProcessorCount);
            foreach (var p in list)
            {
                try
                {
                    if (p.HasExited || !first.TryGetValue(p.Id, out var t0)) continue;
                    double pct = (p.TotalProcessorTime - t0).TotalMilliseconds / 500.0 / cores * 100.0;
                    if (pct > topPct) { topPct = pct; topName = p.ProcessName; }
                    if (pct < minPercent) continue;
                    if (CriticalProcesses.Contains(p.ProcessName)) continue;
                    var pc = p.PriorityClass;
                    if (pc == ProcessPriorityClass.Idle || pc == ProcessPriorityClass.BelowNormal || pc == ProcessPriorityClass.RealTime) continue;
                    p.PriorityClass = ProcessPriorityClass.BelowNormal;
                    lowered++;
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

            return (lowered, topName);
        }
    }
}
