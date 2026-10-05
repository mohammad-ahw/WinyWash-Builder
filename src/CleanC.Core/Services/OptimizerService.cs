using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;
using CleanC.Core.Native;

namespace CleanC.Core.Services
{
    public class OptimizerService
    {
        public static async Task<BoostResult> BoostRamAsync()
        {
            return await Task.Run(() =>
            {
                var beforeStats = SystemStatsService.GetCurrentStats();
                int trimmedCount = 0;

                var processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    try
                    {
                        if (proc.Id > 4 && !proc.HasExited)
                        {
                            if (Win32.EmptyWorkingSet(proc.Handle))
                            {
                                trimmedCount++;
                            }
                        }
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }

                var afterStats = SystemStatsService.GetCurrentStats();
                long freed = Math.Max(0, beforeStats.UsedRamBytes - afterStats.UsedRamBytes);

                return new BoostResult
                {
                    Success = true,
                    FreedBytes = freed,
                    ProcessCount = trimmedCount,
                    CurrentRamPercent = afterStats.RamPercent
                };
            });
        }

        public static bool FlushDns()
        {
            try
            {
                return Win32.DnsFlushResolverCache() != 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
