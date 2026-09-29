using System;
using System.Diagnostics;

namespace CleanC.Core.Services
{
    public class SystemToolsService
    {
        public static bool Launch(string toolId)
        {
            try
            {
                string target = toolId.ToLowerInvariant() switch
                {
                    "taskmgr" => "taskmgr.exe",
                    "cleanmgr" => "cleanmgr.exe",
                    "devmgmt" => "devmgmt.msc",
                    "services" => "services.msc",
                    "diskmgmt" => "diskmgmt.msc",
                    "sysdm" => "sysdm.cpl",
                    "regedit" => "regedit.exe",
                    "resmon" => "resmon.exe",
                    "perfmon" => "perfmon.exe",
                    "control" => "control.exe",
                    "winver" => "winver.exe",
                    "terminal" => "wt.exe",
                    _ => string.Empty
                };

                if (string.IsNullOrEmpty(target)) return false;

                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
