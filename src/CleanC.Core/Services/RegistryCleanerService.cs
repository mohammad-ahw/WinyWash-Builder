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

            // 2. Scan orphaned Uninstall keys (apps uninstalled but left broken registry entries)
            try
            {
                orphanedKeys += ScanUninstallPath(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                orphanedKeys += ScanUninstallPath(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                orphanedKeys += ScanUninstallPath(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            }
            catch { }

            return (deadMui, orphanedKeys);
        }

        private static int ScanUninstallPath(RegistryKey root, string path)
        {
            int count = 0;
            try
            {
                using var unKey = root.OpenSubKey(path, false);
                if (unKey == null) return 0;

                foreach (var sub in unKey.GetSubKeyNames())
                {
                    try
                    {
                        using var app = unKey.OpenSubKey(sub, false);
                        if (app == null) continue;

                        string? unString = app.GetValue("UninstallString")?.ToString();
                        string? installLoc = app.GetValue("InstallLocation")?.ToString();

                        if (!string.IsNullOrWhiteSpace(installLoc) && Directory.Exists(installLoc))
                            continue; // Still exists

                        if (!string.IsNullOrWhiteSpace(unString))
                        {
                            string exePath = ExtractExePath(unString);
                            if (!string.IsNullOrEmpty(exePath) && exePath.Contains(":") && !File.Exists(exePath))
                            {
                                count++;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return count;
        }

        private static string ExtractExePath(string raw)
        {
            try
            {
                raw = raw.Trim();
                if (raw.StartsWith("\""))
                {
                    int endQuote = raw.IndexOf('"', 1);
                    if (endQuote > 1) return raw.Substring(1, endQuote - 1);
                }
                int spaceIdx = raw.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (spaceIdx > 0) return raw.Substring(0, spaceIdx + 4).Trim('"');
            }
            catch { }
            return "";
        }

        public static int CleanOrphanedRegistry()
        {
            int cleaned = 0;

            // 1. Clean dead MuiCache entries
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

            // 2. Clean orphaned HKCU Uninstall subkeys (safe user scope)
            try
            {
                using var unKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true);
                if (unKey != null)
                {
                    var deadSubs = new List<string>();
                    foreach (var sub in unKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var app = unKey.OpenSubKey(sub, false);
                            if (app == null) continue;

                            string? unString = app.GetValue("UninstallString")?.ToString();
                            string? installLoc = app.GetValue("InstallLocation")?.ToString();

                            if (!string.IsNullOrWhiteSpace(installLoc) && Directory.Exists(installLoc))
                                continue;

                            if (!string.IsNullOrWhiteSpace(unString))
                            {
                                string exePath = ExtractExePath(unString);
                                if (!string.IsNullOrEmpty(exePath) && exePath.Contains(":") && !File.Exists(exePath))
                                {
                                    deadSubs.Add(sub);
                                }
                            }
                        }
                        catch { }
                    }

                    foreach (var dead in deadSubs)
                    {
                        try
                        {
                            unKey.DeleteSubKeyTree(dead, false);
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
