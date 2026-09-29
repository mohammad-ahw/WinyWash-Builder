using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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

                // 1. Scan Win32 Applications from Registry
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

                                string publisher = appKey.GetValue("Publisher")?.ToString() ?? string.Empty;

                                // Filter out system drivers, VC++ redistributables, system components
                                if (IsSystemOrDriverPackage(displayName, publisher)) continue;

                                // Ignore system components and updates
                                var sysComp = appKey.GetValue("SystemComponent");
                                if (sysComp != null && Convert.ToInt32(sysComp) == 1) continue;

                                var parentKey = appKey.GetValue("ParentKeyName");
                                if (parentKey != null && !string.IsNullOrWhiteSpace(parentKey.ToString())) continue;

                                string? uninstall = appKey.GetValue("UninstallString")?.ToString();
                                string? quietUninstall = appKey.GetValue("QuietUninstallString")?.ToString();
                                if (string.IsNullOrWhiteSpace(uninstall) && string.IsNullOrWhiteSpace(quietUninstall)) continue;

                                string version = appKey.GetValue("DisplayVersion")?.ToString() ?? string.Empty;
                                string installDate = appKey.GetValue("InstallDate")?.ToString() ?? string.Empty;
                                string displayIcon = appKey.GetValue("DisplayIcon")?.ToString() ?? string.Empty;
                                string? installLocation = appKey.GetValue("InstallLocation")?.ToString()?.Trim();

                                DateTime? parsedDate = null;
                                if (!string.IsNullOrEmpty(installDate) && installDate.Length == 8 && int.TryParse(installDate, out _))
                                {
                                    if (DateTime.TryParseExact(installDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                                    {
                                        parsedDate = dt;
                                    }
                                }
                                else if (!string.IsNullOrEmpty(installDate))
                                {
                                    if (DateTime.TryParse(installDate, out var dt))
                                    {
                                        parsedDate = dt;
                                    }
                                }

                                long sizeBytes = 0;
                                var sizeObj = appKey.GetValue("EstimatedSize");
                                if (sizeObj != null)
                                {
                                    sizeBytes = Convert.ToInt64(sizeObj) * 1024L; // in KB to bytes
                                }
                                else if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                                {
                                    try
                                    {
                                        var di = new DirectoryInfo(installLocation);
                                        long dirTotal = 0;
                                        foreach (var f in di.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                                        {
                                            dirTotal += f.Length;
                                        }
                                        foreach (var d in di.EnumerateDirectories())
                                        {
                                            foreach (var f in d.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                                            {
                                                dirTotal += f.Length;
                                            }
                                        }
                                        if (dirTotal > 0) sizeBytes = dirTotal;
                                    }
                                    catch { }
                                }

                                if (!apps.ContainsKey(displayName))
                                {
                                    apps[displayName] = new InstalledAppInfo
                                    {
                                        DisplayName = displayName,
                                        DisplayVersion = version,
                                        Publisher = publisher,
                                        InstallDate = installDate,
                                        ParsedInstallDate = parsedDate,
                                        EstimatedSizeBytes = sizeBytes,
                                        UninstallString = uninstall ?? string.Empty,
                                        QuietUninstallString = quietUninstall ?? string.Empty,
                                        DisplayIcon = displayIcon,
                                        IsStoreApp = false
                                    };
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                // 2. Scan Windows Store Apps (AppX / MSIX)
                try
                {
                    var pkgMgr = new Windows.Management.Deployment.PackageManager();
                    var packages = pkgMgr.FindPackagesForUser("");
                    foreach (var pkg in packages)
                    {
                        try
                        {
                            if (pkg.IsFramework || pkg.IsResourcePackage) continue;
                            if (pkg.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System) continue;

                            string dName = pkg.DisplayName;
                            if (string.IsNullOrWhiteSpace(dName) || apps.ContainsKey(dName)) continue;

                            string fName = pkg.Id.FullName;
                            if (fName.StartsWith("Microsoft.Windows.", StringComparison.OrdinalIgnoreCase) ||
                                fName.StartsWith("Microsoft.DesktopAppInstaller", StringComparison.OrdinalIgnoreCase) ||
                                fName.StartsWith("Microsoft.UI.Xaml", StringComparison.OrdinalIgnoreCase) ||
                                fName.StartsWith("Microsoft.VCLibs", StringComparison.OrdinalIgnoreCase)) continue;

                            DateTime? installed = null;
                            try { installed = pkg.InstalledDate.DateTime; } catch { }

                            apps[dName] = new InstalledAppInfo
                            {
                                DisplayName = dName,
                                DisplayVersion = $"{pkg.Id.Version.Major}.{pkg.Id.Version.Minor}.{pkg.Id.Version.Build}",
                                Publisher = pkg.PublisherDisplayName,
                                InstallDate = installed.HasValue ? installed.Value.ToString("yyyyMMdd") : "",
                                ParsedInstallDate = installed,
                                EstimatedSizeBytes = 0,
                                UninstallString = "",
                                QuietUninstallString = "",
                                IsStoreApp = true,
                                PackageFullName = fName
                            };
                        }
                        catch { }
                    }
                }
                catch { }

                // Default sorting: Most recently installed apps first, then alphabetically
                return apps.Values
                    .OrderByDescending(a => a.ParsedInstallDate.HasValue)
                    .ThenByDescending(a => a.ParsedInstallDate ?? DateTime.MinValue)
                    .ThenBy(a => a.DisplayName)
                    .ToList();
            });
        }

        public static bool LaunchUninstall(InstalledAppInfo app)
        {
            try
            {
                if (app.IsStoreApp && !string.IsNullOrEmpty(app.PackageFullName))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -WindowStyle Hidden -Command \"Remove-AppxPackage -Package '{app.PackageFullName}'\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    Process.Start(psi);
                    return true;
                }

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

        public static async Task<bool> LaunchUninstallAsync(InstalledAppInfo app)
        {
            try
            {
                if (app.IsStoreApp && !string.IsNullOrEmpty(app.PackageFullName))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -WindowStyle Hidden -Command \"Remove-AppxPackage -Package '{app.PackageFullName}'\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        await proc.WaitForExitAsync();
                        return proc.ExitCode == 0;
                    }
                    return false;
                }

                string cmd = !string.IsNullOrWhiteSpace(app.UninstallString) ? app.UninstallString : app.QuietUninstallString;
                if (string.IsNullOrWhiteSpace(cmd)) return false;

                cmd = cmd.Trim();
                ProcessStartInfo? psiToRun = null;

                if (cmd.StartsWith("MsiExec.exe", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase))
                {
                    string args = cmd.Length > 11 ? cmd.Substring(11).Trim() : "";
                    psiToRun = new ProcessStartInfo
                    {
                        FileName = "msiexec.exe",
                        Arguments = args,
                        UseShellExecute = true
                    };
                }
                else if (cmd.StartsWith("\""))
                {
                    int endQuote = cmd.IndexOf('"', 1);
                    if (endQuote > 1)
                    {
                        string exe = cmd.Substring(1, endQuote - 1);
                        string args = endQuote + 1 < cmd.Length ? cmd.Substring(endQuote + 1).Trim() : "";
                        psiToRun = new ProcessStartInfo
                        {
                            FileName = exe,
                            Arguments = args,
                            UseShellExecute = true
                        };
                    }
                }

                if (psiToRun == null)
                {
                    var parts = cmd.Split(' ', 2);
                    psiToRun = new ProcessStartInfo
                    {
                        FileName = parts[0],
                        Arguments = parts.Length > 1 ? parts[1] : "",
                        UseShellExecute = true
                    };
                }

                using var process = Process.Start(psiToRun);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSystemOrDriverPackage(string displayName, string publisher)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return true;

            // Windows updates & internal KBs
            if (displayName.StartsWith("KB", StringComparison.OrdinalIgnoreCase) && displayName.Length > 2 && char.IsDigit(displayName[2]))
                return true;

            // VC++ Redistributables & Toolsets
            if (displayName.Contains("Visual C++", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Redistributable", StringComparison.OrdinalIgnoreCase))
                return true;

            // Driver packages and hardware helpers
            if (displayName.StartsWith("Windows Driver Package", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Realtek High Definition Audio", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Realtek Audio Driver", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Realtek Card Reader", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Intel(R) Management Engine", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Intel(R) Dynamic Tuning", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Intel(R) Processor Graphics", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Intel(R) Wireless", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("NVIDIA Graphics Driver", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("NVIDIA PhysX", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("NVIDIA FrameView", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("NVIDIA HD Audio", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Synaptics Pointing Device", StringComparison.OrdinalIgnoreCase))
                return true;

            // System Runtimes
            if (displayName.StartsWith("Microsoft .NET", StringComparison.OrdinalIgnoreCase) ||
                displayName.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase) ||
                displayName.StartsWith("Microsoft Windows Desktop Runtime", StringComparison.OrdinalIgnoreCase) ||
                displayName.StartsWith("Microsoft ASP.NET Core", StringComparison.OrdinalIgnoreCase) ||
                displayName.StartsWith("Microsoft Edge WebView2", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }
}
