using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;
using CleanC.Core.Native;

namespace CleanC.Core.Services
{
    public class LargeFileScannerService
    {
        public static async Task<List<LargeFileInfo>> ScanUserLargeFilesAsync(long minSizeBytes = 500L * 1024 * 1024, Action<string>? progress = null)
        {
            return await Task.Run(() =>
            {
                var results = new List<LargeFileInfo>();

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var scanFolders = new List<string>
                {
                    Path.Combine(userProfile, "Downloads"),
                    Path.Combine(userProfile, "Desktop"),
                    Path.Combine(userProfile, "Documents"),
                    Path.Combine(userProfile, "Videos"),
                    Path.Combine(userProfile, "Music"),
                    Path.Combine(userProfile, "AppData", "Local", "Temp"),
                    Path.Combine(userProfile, "AppData", "Roaming"),
                    @"C:\Games",
                    @"C:\Tools"
                };

                // Add existing folders to list (پوشه های قفل شده توسط کاربر اسکن نمی شوند)
                var activeFolders = scanFolders
                    .Where(f => Directory.Exists(f) && !LockRegistryService.IsUserLockedPath(f))
                    .ToList();

                var enumOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
                };

                foreach (var folder in activeFolders)
                {
                    try
                    {
                        progress?.Invoke($"در حال اسکن پوشه: {Path.GetFileName(folder)}...");
                        var dirInfo = new DirectoryInfo(folder);
                        foreach (var file in dirInfo.EnumerateFiles("*", enumOptions))
                        {
                            try
                            {
                                // قفل کاربر: فایل های داخل محدوده ی قفل شده پیشنهاد نمی شوند
                                if (LockRegistryService.IsUserLockedPath(file.FullName)) continue;

                                if (file.Length >= minSizeBytes)
                                {
                                    results.Add(new LargeFileInfo
                                    {
                                        FilePath = file.FullName,
                                        FileName = file.Name,
                                        DirectoryPath = file.DirectoryName ?? string.Empty,
                                        SizeBytes = file.Length,
                                        LastModified = file.LastWriteTime
                                    });
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

                // Sort descending by size
                return results.OrderByDescending(f => f.SizeBytes).Take(100).ToList();
            });
        }

        /// <summary>
        /// انتقال فایل حجیم به سطل بازیافت (نه حذف دائمی).
        /// </summary>
        public static bool DeleteLargeFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                if (LockRegistryService.IsUserLockedPath(filePath)) return false;

                var fi = new FileInfo(filePath);
                if ((fi.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden)) != 0)
                {
                    fi.Attributes = FileAttributes.Normal;
                }

                // یک فایل بالای ۵۰۰ مگابایت با یک کلیک قابل بازگردانی باشد.
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    filePath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

                return true;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return false;
        }
    }
}
