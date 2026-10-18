using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;

namespace CleanC.Core.Services
{
    public class DiskMaintenanceService
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetVolumeLabel(string lpRootPathName, string? lpVolumeName);

        public static bool SetDriveLabel(string driveLetter, string newLabel)
        {
            try
            {
                string root = driveLetter.Trim().TrimEnd('\\') + "\\";
                return SetVolumeLabel(root, newLabel);
            }
            catch
            {
                return false;
            }
        }

        public static async Task<(bool Success, string Message)> ChangeDriveLetterAsync(string oldLetter, string newLetter)
        {
            oldLetter = oldLetter.Trim().TrimEnd(':').ToUpperInvariant();
            newLetter = newLetter.Trim().TrimEnd(':').ToUpperInvariant();

            if (oldLetter == "C" || newLetter == "C")
            {
                return (false, LocalizationService.Get("تغییر حرف درایو سیستم عامل ویندوز (C:) به دلایل امنیتی و پایداری سیستم مجاز نیست."));
            }

            if (oldLetter == newLetter)
            {
                return (false, LocalizationService.Get("حرف انتخابی با حرف فعلی درایو یکسان است."));
            }

            var existingDrives = DriveInfo.GetDrives();
            if (existingDrives.Any(d => d.Name.StartsWith(newLetter, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, string.Format(LocalizationService.Get("حرف درایو {0}: در حال حاضر توسط درایو دیگری در سیستم در حال استفاده است."), newLetter));
            }

            return await Task.Run(() =>
            {
                // تغییر حرف درایو با اسکریپت diskpart انجام می شود؛ fallback قبلی با
                // PowerShell + ExecutionPolicy Bypass برای آنتی ویروس الگوی مشکوکی بود
                string tempScript = Path.Combine(Path.GetTempPath(), $"chgletter_{Guid.NewGuid():N}.txt");
                string scriptContent = $"select volume {oldLetter}\r\nassign letter={newLetter}\r\n";

                try
                {
                    File.WriteAllText(tempScript, scriptContent);

                    var psi = new ProcessStartInfo("diskpart.exe", $"/s \"{tempScript}\"")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var p = Process.Start(psi);
                    if (p == null) return (false, LocalizationService.Get("عدم توانایی در فراخوانی ابزار مدیریت پارتیشن."));

                    p.WaitForExit(15000);

                    // خروجی diskpart برای پارتیشن های داده قفل/محافظت شده همیشه خطا برنمی گرداند؛
                    // بنابراین نتیجه ی واقعی از خود سیستم خوانده می شود (نه از exit code)
                    bool newLetterExists = DriveInfo.GetDrives().Any(d =>
                        d.Name.StartsWith(newLetter, StringComparison.OrdinalIgnoreCase));

                    if (newLetterExists)
                    {
                        return (true, string.Format(LocalizationService.Get("حرف درایو با موفقیت از {0}: به {1}: تغییر یافت."), oldLetter, newLetter));
                    }

                    return (false, LocalizationService.Get("تغییر حرف درایو انجام نشد — احتمالاً پارتیشن در حال استفاده یا رزرو شده است."));
                }
                catch (Exception ex)
                {
                    return (false, string.Format(LocalizationService.Get("خطای سیستمی: {0}"), ex.Message));
                }
                finally
                {
                    try { File.Delete(tempScript); } catch (Exception __ex) { CcLog.Ignore(__ex); }
                }
            });
        }

        public static List<LogicalDriveItem> GetLogicalDrives()
        {
            var result = new List<LogicalDriveItem>();
            try
            {
                var drives = DriveInfo.GetDrives();
                foreach (var d in drives)
                {
                    if (!d.IsReady || d.DriveType != DriveType.Fixed) continue;
                    result.Add(new LogicalDriveItem
                    {
                        Letter = d.Name.TrimEnd('\\'),
                        VolumeLabel = string.IsNullOrWhiteSpace(d.VolumeLabel) ? LocalizationService.Get("درایو محلی") : d.VolumeLabel,
                        FileSystem = d.DriveFormat,
                        TotalSizeBytes = d.TotalSize,
                        FreeSizeBytes = d.AvailableFreeSpace
                    });
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return result;
        }

        public static async Task<bool> OptimizeDriveTrimAsync(string driveLetter = "C")
        {
            return await Task.Run(() =>
            {
                try
                {
                    string cleanLetter = driveLetter.TrimEnd(':', '\\', '/');
                    var psi = new ProcessStartInfo
                    {
                        FileName = Path.Combine(Environment.SystemDirectory, "defrag.exe"),
                        Arguments = $"{cleanLetter}: /O /U",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        process.WaitForExit(60000); // 1 minute max
                        return process.ExitCode == 0;
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }

                return false;
            });
        }

        public static List<FileInfo> GetWslDockerVhdxFiles()
        {
            var list = new List<FileInfo>();
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                var searchFolders = new List<string>
                {
                    Path.Combine(localApp, @"Docker\wsl\data"),
                    Path.Combine(localApp, @"Docker\wsl\distro"),
                    Path.Combine(localApp, @"wsl"),
                    Path.Combine(localApp, @"Packages")
                };

                foreach (var folder in searchFolders)
                {
                    if (!Directory.Exists(folder)) continue;

                    try
                    {
                        var di = new DirectoryInfo(folder);
                        foreach (var file in di.EnumerateFiles("*.vhdx", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true }))
                        {
                            if (file.Length >= 500L * 1024 * 1024) // 500MB+
                            {
                                list.Add(file);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        CcLog.Ignore(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex);
            }
            return list;
        }

        public static async Task<(bool Success, long FreedBytes)> CompactAllWslDockerVhdxAsync()
        {
            return await Task.Run(() =>
            {
                long totalFreed = 0;
                bool anySuccess = false;

                try
                {
                    var files = GetWslDockerVhdxFiles();
                    if (files.Count == 0) return (false, 0);

                    // 1. Terminate WSL instances cleanly so disk files are released
                    try
                    {
                        var wslPsi = new ProcessStartInfo("wsl.exe", "--shutdown")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var wslProc = Process.Start(wslPsi);
                        wslProc?.WaitForExit(5000);
                    }
                    catch (Exception ex)
                    {
                        CcLog.Ignore(ex);
                    }

                    // 2. Compact each VHDX using diskpart
                    foreach (var file in files)
                    {
                        try
                        {
                            file.Refresh();
                            long beforeSize = file.Length;

                            string tempScript = Path.Combine(Path.GetTempPath(), $"compact_{Guid.NewGuid():N}.txt");
                            string scriptContent = $"select vdisk file=\"{file.FullName}\"\r\nattach vdisk readonly\r\ncompact vdisk\r\ndetach vdisk\r\n";
                            File.WriteAllText(tempScript, scriptContent);

                            var dpPsi = new ProcessStartInfo("diskpart.exe", $"/s \"{tempScript}\"")
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            };

                            using (var dpProc = Process.Start(dpPsi))
                            {
                                dpProc?.WaitForExit(60000);
                            }

                            try { File.Delete(tempScript); } catch { }

                            file.Refresh();
                            long afterSize = file.Length;
                            long freed = Math.Max(0, beforeSize - afterSize);
                            totalFreed += freed;
                            anySuccess = true;
                        }
                        catch (Exception ex)
                        {
                            CcLog.Ignore(ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex);
                }

                return (anySuccess, totalFreed);
            });
        }
    }
}
