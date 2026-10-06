using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
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
                                    catch (Exception __ex)
                                    {
                                        CcLog.Ignore(__ex);
                                    }
                                }

                                if (!apps.ContainsKey(displayName))
                                {
                                    if (string.IsNullOrEmpty(displayIcon) && !string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                                    {
                                        try
                                        {
                                            var candidate = Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
                                            if (candidate != null) displayIcon = candidate;
                                        }
                                        catch { }
                                    }

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
                            catch (Exception __ex)
                            {
                                CcLog.Ignore(__ex);
                            }
                        }
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
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
                            try { installed = pkg.InstalledDate.DateTime; } catch (Exception __ex)
{
    CcLog.Ignore(__ex);
}

                            string? logoPath = null;
                            try
                            {
                                string installPath = pkg.InstalledLocation.Path;
                                if (Directory.Exists(installPath))
                                {
                                    string assetsDir = Path.Combine(installPath, "Assets");
                                    if (Directory.Exists(assetsDir))
                                    {
                                        logoPath = Directory.EnumerateFiles(assetsDir, "*logo*.png", SearchOption.AllDirectories)
                                            .OrderByDescending(f => f.Contains("44") || f.Contains("150") || f.Contains("Store"))
                                            .FirstOrDefault();
                                    }
                                    if (string.IsNullOrEmpty(logoPath))
                                    {
                                        logoPath = Directory.EnumerateFiles(installPath, "*.png", SearchOption.TopDirectoryOnly).FirstOrDefault();
                                    }
                                }
                            }
                            catch (Exception __ex)
                            {
                                CcLog.Ignore(__ex);
                            }

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
                                DisplayIcon = logoPath ?? string.Empty,
                                IsStoreApp = true,
                                PackageFullName = fName
                            };
                        }
                        catch (Exception __ex)
                        {
                            CcLog.Ignore(__ex);
                        }
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                // Default sorting: Most recently installed apps first, then alphabetically
                return apps.Values
                    .OrderByDescending(a => a.ParsedInstallDate.HasValue)
                    .ThenByDescending(a => a.ParsedInstallDate ?? DateTime.MinValue)
                    .ThenBy(a => a.DisplayName)
                    .ToList();
            });
        }

        public static (string fileName, string arguments) ParseUninstallCommand(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
                return (string.Empty, string.Empty);

            string trimmed = commandLine.Trim();

            // 1. Quoted executable path
            if (trimmed.StartsWith("\""))
            {
                int endQuote = trimmed.IndexOf('"', 1);
                if (endQuote > 1)
                {
                    string fileName = trimmed.Substring(1, endQuote - 1).Trim();
                    string args = endQuote + 1 < trimmed.Length ? trimmed.Substring(endQuote + 1).Trim() : string.Empty;
                    return (fileName, args);
                }
            }

            // 2. MsiExec command (e.g. MsiExec.exe /I{...} or msiexec /X{...})
            if (trimmed.StartsWith("MsiExec.exe", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                int spaceIdx = trimmed.IndexOf(' ');
                string args = spaceIdx > 0 ? trimmed.Substring(spaceIdx + 1).Trim() : string.Empty;
                // Normalize /I or -I to /X (uninstall instead of install)
                if (args.StartsWith("/I", StringComparison.OrdinalIgnoreCase))
                {
                    args = "/X" + args.Substring(2);
                }
                else if (args.StartsWith("-I", StringComparison.OrdinalIgnoreCase))
                {
                    args = "-X" + args.Substring(2);
                }
                return ("msiexec.exe", args);
            }

            // 3. Unquoted executable with spaces (e.g. C:\Program Files (x86)\App\Uninstall.exe /s)
            string lower = trimmed.ToLowerInvariant();
            int exeIdx = lower.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx > 0)
            {
                int fileEnd = exeIdx + 4;
                string candidateFile = trimmed.Substring(0, fileEnd).Trim();
                string candidateArgs = fileEnd < trimmed.Length ? trimmed.Substring(fileEnd).Trim() : string.Empty;
                return (candidateFile, candidateArgs);
            }

            // 4. rundll32.exe special case
            if (lower.StartsWith("rundll32.exe") || lower.StartsWith("rundll32"))
            {
                int spaceIdx = trimmed.IndexOf(' ');
                return ("rundll32.exe", spaceIdx > 0 ? trimmed.Substring(spaceIdx + 1).Trim() : string.Empty);
            }

            // 5. Fallback split on first space
            var parts = trimmed.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                return (parts[0], parts[1]);
            }

            return (trimmed, string.Empty);
        }

        /// <summary>
        /// اجرای فرمان حذف. اول QuietUninstallString (بی صدا) ترجیح داده می شود،
        /// بعد UninstallString. اگر فایل اجرایی مسیر مطلقِ ناموجود باشد، اجرا نمی شود.
        /// </summary>
        private static (string fileName, string arguments) ResolveUninstallCommand(InstalledAppInfo app)
        {
            // Quiet اول — حذف بی صدا بدون پنجره ای که کاربر منتظرش نیست
            string cmd = !string.IsNullOrWhiteSpace(app.QuietUninstallString)
                ? app.QuietUninstallString
                : app.UninstallString;

            if (string.IsNullOrWhiteSpace(cmd)) return (string.Empty, string.Empty);
            return ParseUninstallCommand(cmd);
        }

        private static bool IsSafeToStart(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;

            // مسیر مطلق باید واقعاً وجود داشته باشد؛ در غیر این صورت اجرای آن یا شکست می خورد
            // یا (در UseShellExecute) چیز دیگری را باز می کند
            if (Path.IsPathRooted(fileName))
            {
                try
                {
                    if (!File.Exists(fileName)) return false;
                }
                catch { return false; }
            }

            return true;
        }

        public static bool LaunchUninstall(InstalledAppInfo app)
        {
            try
            {
                if (app.IsStoreApp && !string.IsNullOrEmpty(app.PackageFullName))
                {
                    // PackageFullName داخل بولکوی PowerShell قرار می گیرد؛ آپاستریک را دوبل می کنیم
                    string escaped = app.PackageFullName.Replace("'", "''");
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -WindowStyle Hidden -Command \"Remove-AppxPackage -Package '{escaped}'\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    Process.Start(psi);
                    return true;
                }

                var (fileName, args) = ResolveUninstallCommand(app);
                if (!IsSafeToStart(fileName)) return false;

                Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
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
                    string escaped = app.PackageFullName.Replace("'", "''");
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -WindowStyle Hidden -Command \"Remove-AppxPackage -Package '{escaped}'\"",
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

                var (fileName, args) = ResolveUninstallCommand(app);
                if (!IsSafeToStart(fileName)) return false;

                var psiToRun = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = true
                };

                using var process = Process.Start(psiToRun);
                if (process == null) return false;

                try
                {
                    await process.WaitForExitAsync();
                    return process.ExitCode == 0;
                }
                catch
                {
                    // بعضی uninstallerها با UseShellExecute قابل ردیابی نیستند؛
                    // در این حالت فرض می کنیم اجرا شده و کاربر پنجره را می بیند
                    await Task.Delay(3000);
                    return true;
                }
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
