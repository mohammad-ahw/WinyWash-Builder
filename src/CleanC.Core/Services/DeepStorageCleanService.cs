using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;

namespace CleanC.Core.Services
{
    public static class DeepStorageCleanService
    {
        public static async Task<DeepStorageScanResult> ScanDeepStorageAsync(Action<string>? progress = null)
        {
            return await Task.Run(() =>
            {
                var result = new DeepStorageScanResult();
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                var targetDirectories = new List<string>
                {
                    Path.Combine(userProfile, "Downloads"),
                    Path.Combine(userProfile, "Desktop"),
                    Path.Combine(userProfile, "Documents"),
                    Path.Combine(userProfile, "Videos"),
                    Path.Combine(userProfile, "Music"),
                    Path.Combine(userProfile, "AppData", "Local", "Temp")
                }.Where(Directory.Exists).ToList();

                var enumOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
                };

                // -------------------------------------------------------------
                // 1. Scan Files for Duplicates & Organization Mess
                // -------------------------------------------------------------
                var filesBySize = new Dictionary<long, List<FileInfo>>();
                var messItems = new List<StorageMessItem>();
                var emptyFolders = new List<EmptyFolderInfo>();

                var installerExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi", ".iso", ".bat", ".cmd" };
                var archiveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z", ".tar", ".gz" };

                var now = DateTime.Now;

                foreach (var dir in targetDirectories)
                {
                    progress?.Invoke($"در حال بررسی پوشه: {Path.GetFileName(dir)}...");

                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);

                        // Empty folders check
                        try
                        {
                            foreach (var subDir in dirInfo.EnumerateDirectories("*", enumOptions))
                            {
                                try
                                {
                                    if (!subDir.EnumerateFileSystemInfos().Any())
                                    {
                                        emptyFolders.Add(new EmptyFolderInfo
                                        {
                                            FolderPath = subDir.FullName,
                                            FolderName = subDir.Name,
                                            ParentPath = subDir.Parent?.FullName ?? string.Empty
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

                        // Files check
                        foreach (var file in dirInfo.EnumerateFiles("*", enumOptions))
                        {
                            try
                            {
                                long size = file.Length;
                                string ext = file.Extension;
                                string name = file.Name;
                                string lowerName = name.ToLowerInvariant();

                                // Group potential duplicates (> 500 KB)
                                if (size >= 500 * 1024)
                                {
                                    if (!filesBySize.TryGetValue(size, out var list))
                                    {
                                        list = new List<FileInfo>();
                                        filesBySize[size] = list;
                                    }
                                    list.Add(file);
                                }

                                // Storage Organization items (Desktop / Downloads focused)
                                bool isUserDesktopOrDownloads = dir.Contains("Desktop", StringComparison.OrdinalIgnoreCase) ||
                                                               dir.Contains("Downloads", StringComparison.OrdinalIgnoreCase);

                                if (isUserDesktopOrDownloads)
                                {
                                    // Copied duplicates like "name - Copy.ext" or "name (1).ext"
                                    if (lowerName.Contains(" - copy") || lowerName.Contains(" (1)") || lowerName.Contains(" (2)") || lowerName.Contains("نسخه کپی"))
                                    {
                                        messItems.Add(new StorageMessItem
                                        {
                                            FilePath = file.FullName,
                                            FileName = file.Name,
                                            DirectoryPath = file.DirectoryName ?? string.Empty,
                                            SizeBytes = size,
                                            LastModified = file.LastWriteTime,
                                            Category = StorageMessCategory.DuplicateCopy
                                        });
                                    }
                                    // Old setup/installers in downloads/desktop > 7 days old
                                    else if (installerExtensions.Contains(ext) && (now - file.LastWriteTime).TotalDays > 7)
                                    {
                                        messItems.Add(new StorageMessItem
                                        {
                                            FilePath = file.FullName,
                                            FileName = file.Name,
                                            DirectoryPath = file.DirectoryName ?? string.Empty,
                                            SizeBytes = size,
                                            LastModified = file.LastWriteTime,
                                            Category = StorageMessCategory.OldInstaller
                                        });
                                    }
                                    // Old archives > 14 days old
                                    else if (archiveExtensions.Contains(ext) && (now - file.LastWriteTime).TotalDays > 14)
                                    {
                                        messItems.Add(new StorageMessItem
                                        {
                                            FilePath = file.FullName,
                                            FileName = file.Name,
                                            DirectoryPath = file.DirectoryName ?? string.Empty,
                                            SizeBytes = size,
                                            LastModified = file.LastWriteTime,
                                            Category = StorageMessCategory.OldArchive
                                        });
                                    }
                                    // Old screenshots > 14 days old
                                    else if ((lowerName.StartsWith("screenshot") || lowerName.StartsWith("اسکرین‌شات") || lowerName.StartsWith("capture")) && (now - file.LastWriteTime).TotalDays > 14)
                                    {
                                        messItems.Add(new StorageMessItem
                                        {
                                            FilePath = file.FullName,
                                            FileName = file.Name,
                                            DirectoryPath = file.DirectoryName ?? string.Empty,
                                            SizeBytes = size,
                                            LastModified = file.LastWriteTime,
                                            Category = StorageMessCategory.OldScreenshot
                                        });
                                    }
                                }

                                // Large files > 200MB
                                if (size >= 200L * 1024 * 1024)
                                {
                                    if (!messItems.Any(m => m.FilePath.Equals(file.FullName, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        messItems.Add(new StorageMessItem
                                        {
                                            FilePath = file.FullName,
                                            FileName = file.Name,
                                            DirectoryPath = file.DirectoryName ?? string.Empty,
                                            SizeBytes = size,
                                            LastModified = file.LastWriteTime,
                                            Category = StorageMessCategory.LargeFile
                                        });
                                    }
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

                // -------------------------------------------------------------
                // 2. Compute Fast Hashes for Potential Duplicates
                // -------------------------------------------------------------
                progress?.Invoke("در حال تحلیل و مقایسه فایل‌های یکسان...");

                var duplicateCandidates = filesBySize.Where(kvp => kvp.Value.Count > 1).ToList();
                var duplicateGroups = new List<DuplicateFileGroup>();

                using var md5 = MD5.Create();

                foreach (var candidate in duplicateCandidates)
                {
                    var filesByHash = new Dictionary<string, List<FileInfo>>();

                    foreach (var file in candidate.Value)
                    {
                        try
                        {
                            string hash = ComputeFastFileHash(file.FullName, md5);
                            if (!string.IsNullOrEmpty(hash))
                            {
                                if (!filesByHash.TryGetValue(hash, out var list))
                                {
                                    list = new List<FileInfo>();
                                    filesByHash[hash] = list;
                                }
                                list.Add(file);
                            }
                        }
                        catch (Exception __ex)
                        {
                            CcLog.Ignore(__ex);
                        }
                    }

                    foreach (var hashGroup in filesByHash.Where(kvp => kvp.Value.Count > 1))
                    {
                        var sortedFiles = hashGroup.Value.OrderBy(f => f.CreationTime).ToList();
                        var group = new DuplicateFileGroup
                        {
                            FileHash = hashGroup.Key,
                            SampleName = sortedFiles[0].Name,
                            SingleSizeBytes = sortedFiles[0].Length
                        };

                        for (int i = 0; i < sortedFiles.Count; i++)
                        {
                            var f = sortedFiles[i];
                            group.Files.Add(new DuplicateFileInfo
                            {
                                FilePath = f.FullName,
                                FileName = f.Name,
                                DirectoryPath = f.DirectoryName ?? string.Empty,
                                SizeBytes = f.Length,
                                LastModified = f.LastWriteTime,
                                IsOriginal = (i == 0),
                                IsSelectedForDeletion = (i > 0)
                            });
                        }

                        duplicateGroups.Add(group);
                    }
                }

                result.DuplicateGroups = duplicateGroups.OrderByDescending(g => g.WastedSizeBytes).ToList();
                result.EmptyFolders = emptyFolders.Take(150).ToList();
                result.MessItems = messItems.OrderByDescending(m => m.SizeBytes).Take(150).ToList();

                result.TotalDuplicateWastedBytes = result.DuplicateGroups.Sum(g => g.WastedSizeBytes);
                result.TotalMessSizeBytes = result.MessItems.Sum(m => m.SizeBytes);

                return result;
            });
        }

        private static string ComputeFastFileHash(string filePath, MD5 md5)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
                byte[] buffer = new byte[64 * 1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    byte[] hash = md5.ComputeHash(buffer, 0, bytesRead);
                    return Convert.ToHexString(hash);
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return string.Empty;
        }

        // F3 — همه‌ی حذف‌های فایلِ شخصی از اینجا به SafeDeleteService می‌روند تا
        // واقعاً به سطل بازیافت (یا قرنطینه) برسند و امکان بازگردانی داشته باشند.

        public static async Task<SafeDeleteBatchResult> DeleteDuplicateFilesAsync(IEnumerable<string> filePaths)
        {
            return await Task.Run(() => SafeDeleteService.DeleteFiles(filePaths));
        }

        public static async Task<SafeDeleteBatchResult> DeleteEmptyFoldersAsync(IEnumerable<string> folderPaths)
        {
            return await Task.Run(() =>
            {
                // فقط پوشه‌ای که هنوز واقعاً خالی است حذف می‌شود
                var empty = folderPaths
                    .Where(p => Directory.Exists(p) && !Directory.EnumerateFileSystemEntries(p).Any())
                    .ToList();

                return SafeDeleteService.DeleteDirectories(empty);
            });
        }

        public static async Task<SafeDeleteBatchResult> DeleteMessItemsAsync(IEnumerable<string> filePaths)
        {
            return await DeleteDuplicateFilesAsync(filePaths);
        }

        public static async Task<SafeDeleteBatchResult> DeleteLargeFilesAsync(IEnumerable<string> filePaths)
        {
            return await DeleteDuplicateFilesAsync(filePaths);
        }
    }
}
