using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class RegistryCleanerService
    {
        public static (int DeadMuiEntries, int OrphanedKeys) ScanOrphanedRegistry()
        {
            int deadMui = 0;
            int orphanedKeys = 0;

            // 1. Scan MuiCache for deleted application paths
            try
            {
                using var muiKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache");
                if (muiKey != null)
                {
                    foreach (var valName in muiKey.GetValueNames())
                    {
                        // ValNames are often in format: "C:\path\to\app.exe.FriendlyAppName" or "C:\path\to\app.exe"
                        string cleanPath = valName.Split(new[] { ".FriendlyAppName", ".ApplicationCompany" }, StringSplitOptions.None)[0];
                        if (cleanPath.Contains(":") && (cleanPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || cleanPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (!File.Exists(cleanPath))
                            {
                                deadMui++;
                            }
                        }
                    }
                }
            }
            catch { }

            return (deadMui, orphanedKeys);
        }

        public static int CleanOrphanedRegistry()
        {
            int cleaned = 0;

            // Clean dead MuiCache entries
            try
            {
                using var muiKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", true);
                if (muiKey != null)
                {
                    var toDelete = new List<string>();
                    foreach (var valName in muiKey.GetValueNames())
                    {
                        string cleanPath = valName.Split(new[] { ".FriendlyAppName", ".ApplicationCompany" }, StringSplitOptions.None)[0];
                        if (cleanPath.Contains(":") && (cleanPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || cleanPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (!File.Exists(cleanPath))
                            {
                                toDelete.Add(valName);
                            }
                        }
                    }

                    foreach (var del in toDelete)
                    {
                        try
                        {
                            muiKey.DeleteValue(del, false);
                            cleaned++;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return cleaned;
        }
    }
}
