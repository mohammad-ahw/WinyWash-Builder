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
                return (false, "تغییر حرف درایو سیستم‌عامل ویندوز (C:) به دلایل امنیتی و پایداری سیستم مجاز نیست.");
            }

            if (oldLetter == newLetter)
            {
                return (false, "حرف انتخابی با حرف فعلی درایو یکسان است.");
            }

            var existingDrives = DriveInfo.GetDrives();
            if (existingDrives.Any(d => d.Name.StartsWith(newLetter, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, $"حرف درایو {newLetter}: در حال حاضر توسط درایو دیگری در سیستم در حال استفاده است.");
            }

            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"Get-Partition -DriveLetter {oldLetter} | Set-Partition -NewDriveLetter {newLetter}\"")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var p = Process.Start(psi);
                    if (p == null) return (false, "عدم توانایی در فراخوانی ابزار مدیریت پارتیشن.");

                    string err = p.StandardError.ReadToEnd();
                    p.WaitForExit(8000);

                    if (p.ExitCode == 0)
                    {
                        return (true, $"حرف درایو با موفقیت از {oldLetter}: به {newLetter}: تغییر یافت.");
                    }

                    return (false, $"خطا در تغییر حرف درایو: {err}".Trim());
                }
                catch (Exception ex)
                {
                    return (false, $"خطای سیستمی: {ex.Message}");
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
                        VolumeLabel = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "درایو محلی" : d.VolumeLabel,
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
    }
}
