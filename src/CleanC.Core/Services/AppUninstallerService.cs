using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CleanC.Core.Models;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class AppUninstallerService
    {
        public static async Task<List<InstalledAppInfo>> GetInstalledAppsAsync()
        {
            return await Task.Run(() =>
            {
                var apps = new Dictionary<string, InstalledAppInfo>(StringComparer.OrdinalIgnoreCase);

                var registryRoots = new[]
                {
                    (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                    (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                    (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall")
                };

                foreach (var (root, subKeyPath) in registryRoots)
                {
                    try
                    {
                        using var key = root.OpenSubKey(subKeyPath);
                        if (key == null) continue;

                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            try
                            {
                                using var appKey = key.OpenSubKey(subKeyName);
                                if (appKey == null) continue;

                                string? displayName = appKey.GetValue("DisplayName")?.ToString()?.Trim();
                                if (string.IsNullOrWhiteSpace(displayName)) continue;

                                // Ignore system components and updates
                                var sysComp = appKey.GetValue("SystemComponent");
                                if (sysComp != null && Convert.ToInt32(sysComp) == 1) continue;

                                var parentKey = appKey.GetValue("ParentKeyName");
                                if (parentKey != null && !string.IsNullOrWhiteSpace(parentKey.ToString())) continue;

                                if (displayName.StartsWith("KB", StringComparison.OrdinalIgnoreCase) && displayName.Length > 2 && char.IsDigit(displayName[2]))
                                    continue;

                                string? uninstall = appKey.GetValue("UninstallString")?.ToString();
                                string? quietUninstall = appKey.GetValue("QuietUninstallString")?.ToString();
                                if (string.IsNullOrWhiteSpace(uninstall) && string.IsNullOrWhiteSpace(quietUninstall)) continue;

                                string version = appKey.GetValue("DisplayVersion")?.ToString() ?? string.Empty;
                                string publisher = appKey.GetValue("Publisher")?.ToString() ?? string.Empty;
                                string installDate = appKey.GetValue("InstallDate")?.ToString() ?? string.Empty;

                                long sizeBytes = 0;
                                var sizeObj = appKey.GetValue("EstimatedSize");
                                if (sizeObj != null)
                                {
                                    sizeBytes = Convert.ToInt64(sizeObj) * 1024L; // in KB
                                }

                                if (!apps.ContainsKey(displayName))
                                {
                                    apps[displayName] = new InstalledAppInfo
                                    {
                                        DisplayName = displayName,
                                        DisplayVersion = version,
                                        Publisher = publisher,
                                        InstallDate = installDate,
                                        EstimatedSizeBytes = sizeBytes,
                                        UninstallString = uninstall ?? string.Empty,
                                        QuietUninstallString = quietUninstall ?? string.Empty,
                                        IsStoreApp = false
                                    };
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                return apps.Values.OrderBy(a => a.DisplayName).ToList();
            });
        }

        public static bool LaunchUninstall(InstalledAppInfo app)
        {
            try
            {
                string cmd = !string.IsNullOrWhiteSpace(app.UninstallString) ? app.UninstallString : app.QuietUninstallString;
                if (string.IsNullOrWhiteSpace(cmd)) return false;

                cmd = cmd.Trim();

                // If msiexec command
                if (cmd.StartsWith("MsiExec.exe", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase))
                {
                    string args = cmd.Length > 11 ? cmd.Substring(11).Trim() : "";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "msiexec.exe",
                        Arguments = args,
                        UseShellExecute = true
                    });
                    return true;
                }

                // If quoted executable
                if (cmd.StartsWith("\""))
                {
                    int endQuote = cmd.IndexOf('"', 1);
                    if (endQuote > 1)
                    {
                        string exe = cmd.Substring(1, endQuote - 1);
                        string args = endQuote + 1 < cmd.Length ? cmd.Substring(endQuote + 1).Trim() : "";
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = exe,
                            Arguments = args,
                            UseShellExecute = true
                        });
                        return true;
                    }
                }

                // General launch via cmd or shell
                var parts = cmd.Split(' ', 2);
                Process.Start(new ProcessStartInfo
                {
                    FileName = parts[0],
                    Arguments = parts.Length > 1 ? parts[1] : "",
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
