using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CleanC.Core.Models;
using CleanC.Core.Native;

namespace CleanC.Core.Services
{
    public class CleanService
    {
        public static async Task<CleanResult> CleanItemsAsync(IEnumerable<JunkCategory> items, Action<string>? progress = null)
        {
            return await Task.Run(async () =>
            {
                var result = new CleanResult { Success = true };
                long totalFreed = 0;

                foreach (var item in items)
                {
                    if (!item.IsChecked) continue;

                    progress?.Invoke($"در حال پاک‌سازی: {item.Title}...");
                    long freed = CleanCategory(item);
                    totalFreed += freed;
                    result.CleanedItems.Add(item.Title);
                }

                // Automatic RAM boost + DNS flush as part of clean
                progress?.Invoke("بهینه‌سازی حافظه و رفرش شبکه...");
                var boost = await OptimizerService.BoostRamAsync();
                totalFreed += boost.FreedBytes;
                OptimizerService.FlushDns();

                result.FreedBytes = totalFreed;
                return result;
            });
        }

        public static long CleanCategory(JunkCategory item)
        {
            if (item == null) return 0;
            long freed = 0;

            if (item.Id == "recycleBin")
            {
                try
                {
                    Win32.SHEmptyRecycleBin(IntPtr.Zero, null,
                        Win32.SHERB_NOCONFIRMATION | Win32.SHERB_NOPROGRESSUI | Win32.SHERB_NOSOUND);
                    return item.Bytes;
                }
                catch
                {
                    return 0;
                }
            }

            if (item.Id == "orphanedRegistry")
            {
                try
                {
                    int cleanedCount = RegistryCleanerService.CleanOrphanedRegistry();
                    return Math.Max(cleanedCount * 1024, item.Bytes);
                }
                catch
                {
                    return 0;
                }
            }

            var paths = item.TargetPaths != null && item.TargetPaths.Count > 0
                ? item.TargetPaths
                : (!string.IsNullOrEmpty(item.Path) ? new List<string> { item.Path } : new List<string>());

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;

                if (Directory.Exists(path))
                {
                    freed += SafeCleanFolder(path);

                    if (item.GroupId == "leftovers")
                    {
                        try
                        {
                            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                            {
                                Directory.Delete(path, false);
                            }
                        }
                        catch { }
                    }
                }
                else if (File.Exists(path))
                {
                    try
                    {
                        var fi = new FileInfo(path);
                        long len = fi.Length;
                        if ((fi.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                        {
                            fi.Attributes = FileAttributes.Normal;
                        }
                        fi.Delete();
                        freed += len;
                    }
                    catch { }
                }
            }

            return freed;
        }

        private static bool IsProtectedSystemPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\', '/');
                // Disallow cleaning drive roots (e.g. C:, C:\, D:, D:\)
                if (full.Length <= 3 && full.Contains(':')) return true;

                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
                string sysDir = Environment.SystemDirectory.TrimEnd('\\');
                string progDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd('\\');
                string progX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).TrimEnd('\\');
                string userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');

                if (string.Equals(full, winDir, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(full, sysDir, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(full, progDir, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(full, progX86, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(full, userDir, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static long SafeCleanFolder(string dirPath)
        {
            if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath) || IsProtectedSystemPath(dirPath)) return 0;
            long freed = 0;

            var enumOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = 0
            };

            try
            {
                var dir = new DirectoryInfo(dirPath);

                // Enumerate and delete files
                foreach (var file in dir.EnumerateFiles("*", enumOptions))
                {
                    try
                    {
                        long len = file.Length;
                        if ((file.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                        {
                            file.Attributes = FileAttributes.Normal;
                        }
                        file.Delete();
                        freed += len;
                    }
                    catch
                    {
                        // File locked by running process - skip safely
                    }
                }

                // Recursively clean subdirectories
                foreach (var sub in dir.EnumerateDirectories("*", enumOptions))
                {
                    try
                    {
                        freed += SafeCleanFolder(sub.FullName);

                        if ((sub.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                        {
                            sub.Attributes = FileAttributes.Normal;
                        }
                        sub.Delete(true);
                    }
                    catch
                    {
                        // Subdirectory still in use or contains locked file - skip
                    }
                }
            }
            catch
            {
                // Inaccessible folder - skip
            }

            return freed;
        }
    }
}
