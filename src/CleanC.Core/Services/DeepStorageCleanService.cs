using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                    Path.Combine(userProfile, "AppData", "Local", "Temp"),
                    Path.Combine(userProfile, "AppData", "Local", "CrashDumps"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")
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

                        // Empty folders check (Strictly safe: only in Downloads/Desktop/Temp, never in system/app/project folders)
                        try
                        {
                            bool isSafeDirForEmptyFolders = dir.Contains("Downloads", StringComparison.OrdinalIgnoreCase) ||
                                                           dir.Contains("Desktop", StringComparison.OrdinalIgnoreCase) ||
                                                           dir.Contains("Temp", StringComparison.OrdinalIgnoreCase);

                            if (isSafeDirForEmptyFolders)
                            {
                                foreach (var subDir in dirInfo.EnumerateDirectories("*", enumOptions))
                                {
                                    try
                                    {
                                        string fullName = subDir.FullName;
                                        if (fullName.Contains(".git", StringComparison.OrdinalIgnoreCase) ||
                                            fullName.Contains(".vs", StringComparison.OrdinalIgnoreCase) ||
                                            fullName.Contains(".idea", StringComparison.OrdinalIgnoreCase) ||
                                            fullName.Contains("node_modules", StringComparison.OrdinalIgnoreCase) ||
                                            fullName.Contains("AppData", StringComparison.OrdinalIgnoreCase) ||
                                            fullName.Contains("bin", StringComparison.OrdinalIgnoreCase) ||
                                            fullName.Contains("obj", StringComparison.OrdinalIgnoreCase) ||
                                            fullName.Contains("Windows", StringComparison.OrdinalIgnoreCase) ||
                                            fullName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                                        {
                                            continue;
                                        }

                                        if (!subDir.EnumerateFileSystemInfos().Any())
                                        {
                                            emptyFolders.Add(new EmptyFolderInfo
                                            {
                                                FolderPath = subDir.FullName,
                                                FolderName = subDir.Name,
                                                ParentPath = subDir.Parent?.FullName ?? string.Empty,
                                                IsSelected = false
                                            });
                                        }
                                    }
                                    catch (Exception __ex)
                                    {
                                        CcLog.Ignore(__ex);
                                    }
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
                                    // Smart Scenario 1: Unzipped Archives (Both .zip/.rar and extracted directory exist)
                                    if (archiveExtensions.Contains(ext))
                                    {
                                        string folderNameWithoutExt = Path.GetFileNameWithoutExtension(file.Name);
                                        string potentialDir = Path.Combine(file.DirectoryName ?? string.Empty, folderNameWithoutExt);
                                        if (Directory.Exists(potentialDir))
                                        {
                                            messItems.Add(new StorageMessItem
                                            {
                                                FilePath = file.FullName,
                                                FileName = file.Name,
                                                DirectoryPath = file.DirectoryName ?? string.Empty,
                                                SizeBytes = size,
                                                LastModified = file.LastWriteTime,
                                                Category = StorageMessCategory.ExtractedArchiveDuplicate,
                                                IsSelected = false
                                            });
                                        }
                                        else if ((now - file.LastWriteTime).TotalDays > 21)
                                        {
                                            messItems.Add(new StorageMessItem
                                            {
                                                FilePath = file.FullName,
                                                FileName = file.Name,
                                                DirectoryPath = file.DirectoryName ?? string.Empty,
                                                SizeBytes = size,
                                                LastModified = file.LastWriteTime,
                                                Category = StorageMessCategory.OldArchive,
                                                IsSelected = false
                                            });
                                        }
                                    }
                                    // Smart Scenario 2: Incomplete / Broken Downloads (.crdownload, .part)
                                    else if (ext.Equals(".crdownload", StringComparison.OrdinalIgnoreCase) ||
                                             ext.Equals(".part", StringComparison.OrdinalIgnoreCase) ||
                                             (ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase) && (now - file.LastWriteTime).TotalDays > 2))
                                    {
                                        messItems.Add(new StorageMessItem
                                        {
                                            FilePath = file.FullName,
                                            FileName = file.Name,
                                            DirectoryPath = file.DirectoryName ?? string.Empty,
                                            SizeBytes = size,
                                            LastModified = file.LastWriteTime,
                                            Category = StorageMessCategory.IncompleteDownload,
                                            IsSelected = false
                                        });
                                    }
                                    // Smart Scenario 3: Copied duplicates like "name - Copy.ext" or "name (1).ext"
                                    else if (lowerName.Contains(" - copy") || lowerName.Contains(" (1)") || lowerName.Contains(" (2)") || lowerName.Contains("نسخه کپی"))
                                    {
                                        messItems.Add(new StorageMessItem
                                        {
                                            FilePath = file.FullName,
                                            FileName = file.Name,
                                            DirectoryPath = file.DirectoryName ?? string.Empty,
                                            SizeBytes = size,
                                            LastModified = file.LastWriteTime,
                                            Category = StorageMessCategory.DuplicateCopy,
                                            IsSelected = false
                                        });
                                    }
                                    // Old setup/installers in downloads/desktop > 14 days old
                                    else if (installerExtensions.Contains(ext) && (now - file.LastWriteTime).TotalDays > 14)
                                    {
                                        messItems.Add(new StorageMessItem
                                        {
                                            FilePath = file.FullName,
                                            FileName = file.Name,
                                            DirectoryPath = file.DirectoryName ?? string.Empty,
                                            SizeBytes = size,
                                            LastModified = file.LastWriteTime,
                                            Category = StorageMessCategory.OldInstaller,
                                            IsSelected = false
                                        });
                                    }
                                    // Old screenshots > 21 days old
                                    else if ((lowerName.StartsWith("screenshot") || lowerName.StartsWith("اسکرین شات") || lowerName.StartsWith("capture")) && (now - file.LastWriteTime).TotalDays > 21)
                                    {
                                        messItems.Add(new StorageMessItem
                                        {
                                            FilePath = file.FullName,
                                            FileName = file.Name,
                                            DirectoryPath = file.DirectoryName ?? string.Empty,
                                            SizeBytes = size,
                                            LastModified = file.LastWriteTime,
                                            Category = StorageMessCategory.OldScreenshot,
                                            IsSelected = false
                                        });
                                    }
                                }
                                else if (dir.Contains("Temp", StringComparison.OrdinalIgnoreCase) || dir.Contains("CrashDumps", StringComparison.OrdinalIgnoreCase))
                                {
                                    if ((now - file.LastWriteTime).TotalDays > 2)
                                    {
                                        messItems.Add(new StorageMessItem
                                        {
                                            FilePath = file.FullName,
                                            FileName = file.Name,
                                            DirectoryPath = file.DirectoryName ?? string.Empty,
                                            SizeBytes = size,
                                            LastModified = file.LastWriteTime,
                                            Category = StorageMessCategory.TempJunk
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
                // 1.5 Scan Developer Caches & Container Junk (If Present)
                // -------------------------------------------------------------
                progress?.Invoke("در حال بررسی حافظه های موقت توسعه دهندگان و کانتینرها...");
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                var devCaches = new (string Path, string Name)[]
                {
                    (Path.Combine(localAppData, "npm-cache"), "کش پکیج های npm"),
                    (Path.Combine(appDataRoaming, "npm-cache"), "کش پکیج های npm (Roaming)"),
                    (Path.Combine(userProfile, ".nuget", "packages"), "کش پکیج های NuGet"),
                    (Path.Combine(localAppData, "NuGet", "v3-cache"), "کش HTTP پکیج های NuGet"),
                    (Path.Combine(localAppData, "pip", "cache"), "کش پکیج های Python pip"),
                    (Path.Combine(userProfile, ".gradle", "caches"), "کش بیلد و ابزارهای Gradle"),
                    (Path.Combine(userProfile, ".cargo", "registry", "cache"), "کش پکیج های Rust Cargo"),
                    (Path.Combine(userProfile, ".android", "build-cache"), "کش کامپایل Android Studio"),
                    (Path.Combine(appDataRoaming, "Code", "Cache"), "کش ادیتور VS Code"),
                    (Path.Combine(appDataRoaming, "Code", "CachedData"), "داده های کش شده VS Code"),
                    (Path.Combine(userProfile, ".docker"), "کش و لاگ های Docker"),
                    (Path.Combine(userProfile, ".ollama", "models"), "مدل های هوش مصنوعی Ollama (.gguf)"),
                    (Path.Combine(userProfile, ".lmstudio", "models"), "مدل های هوش مصنوعی LM Studio"),
                    (Path.Combine(localAppData, "lm-studio", "models"), "مدل های هوش مصنوعی LM Studio (Local)"),
                    (Path.Combine(userProfile, ".cache", "huggingface", "hub"), "کش مدل های HuggingFace"),
                    (Path.Combine(userProfile, "Apple", "MobileSync", "Backup"), "بکاپ های آیفون (Apple MobileSync)"),
                    (Path.Combine(appDataRoaming, "Apple Computer", "MobileSync", "Backup"), "بکاپ های آیفون (iTunes)")
                };

                foreach (var (cPath, cName) in devCaches)
                {
                    try
                    {
                        if (Directory.Exists(cPath))
                        {
                            long dirSize = 0;
                            var di = new DirectoryInfo(cPath);
                            foreach (var f in di.EnumerateFiles("*", enumOptions))
                            {
                                try { dirSize += f.Length; } catch { }
                            }

                            if (dirSize >= 5 * 1024 * 1024) // 5 MB or more
                            {
                                messItems.Add(new StorageMessItem
                                {
                                    FilePath = cPath,
                                    FileName = cName,
                                    DirectoryPath = Path.GetDirectoryName(cPath) ?? cPath,
                                    SizeBytes = dirSize,
                                    LastModified = di.LastWriteTime,
                                    Category = StorageMessCategory.TempJunk
                                });
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
                progress?.Invoke("در حال تحلیل و مقایسه فایل های یکسان...");

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

        // F3 — همه ی حذف های فایلِ شخصی از اینجا به SafeDeleteService می روند تا
        // واقعاً به سطل بازیافت (یا قرنطینه) برسند و امکان بازگردانی داشته باشند.

        public static async Task<SafeDeleteBatchResult> DeleteDuplicateFilesAsync(IEnumerable<string> filePaths)
        {
            return await Task.Run(() => SafeDeleteService.DeleteFiles(filePaths));
        }

        public static async Task<SafeDeleteBatchResult> DeleteEmptyFoldersAsync(IEnumerable<string> folderPaths)
        {
            return await Task.Run(() =>
            {
                // فقط پوشه ای که هنوز واقعاً خالی است حذف می شود
                var empty = folderPaths
                    .Where(p => Directory.Exists(p) && !Directory.EnumerateFileSystemEntries(p).Any())
                    .ToList();

                return SafeDeleteService.DeleteDirectories(empty);
            });
        }

        public static async Task<SafeDeleteBatchResult> DeleteMessItemsAsync(IEnumerable<string> paths)
        {
            return await Task.Run(() =>
            {
                var files = new List<string>();
                var dirs = new List<string>();
                foreach (var p in paths)
                {
                    if (Directory.Exists(p)) dirs.Add(p);
                    else if (File.Exists(p)) files.Add(p);
                }

                var res = SafeDeleteService.DeleteFiles(files);
                if (dirs.Count > 0)
                {
                    var dirRes = SafeDeleteService.DeleteDirectories(dirs);
                    res.Total += dirRes.Total;
                    res.RecycledCount += dirRes.RecycledCount;
                    res.QuarantinedCount += dirRes.QuarantinedCount;
                    res.PermanentCount += dirRes.PermanentCount;
                    res.FailedCount += dirRes.FailedCount;
                    res.NotFoundCount += dirRes.NotFoundCount;
                    res.FreedBytes += dirRes.FreedBytes;
                    res.MovedBytes += dirRes.MovedBytes;
                    res.Errors.AddRange(dirRes.Errors);
                }
                return res;
            });
        }

        public static async Task<SafeDeleteBatchResult> DeleteLargeFilesAsync(IEnumerable<string> filePaths)
        {
            return await DeleteDuplicateFilesAsync(filePaths);
        }

        // ====================================================================
        // Extended Deep Cleaners (Docker, Dev Caches, WSL2, DISM)
        // ====================================================================

        public static async Task<(bool Success, string Message)> CleanDockerCacheAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("docker", "system prune -f")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    if (p == null) return (false, "سرویس Docker روی سیستم نصب یا فعال نیست.");
                    p.WaitForExit(30000);
                    return p.ExitCode == 0
                        ? (true, "کش داکر، ایمیج های بی استفاده و کانتینرهای متوقف شده با موفقیت پاکسازی شدند.")
                        : (false, "خطا در پاکسازی کش Docker.");
                }
                catch
                {
                    return (false, "سرویس Docker در دسترس نیست.");
                }
            });
        }

        public static async Task<(bool Success, long FreedBytes, string Message)> CleanDevCachesAsync()
        {
            return await Task.Run(() =>
            {
                long freed = 0;
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                var devCacheDirs = new[]
                {
                    Path.Combine(localAppData, "npm-cache"),
                    Path.Combine(localAppData, "pip", "cache"),
                    Path.Combine(localAppData, "NuGet", "v3-cache"),
                    Path.Combine(localAppData, "NuGet", "plugins-cache"),
                    Path.Combine(userProfile, ".gradle", "caches"),
                    Path.Combine(appData, "Code", "Cache"),
                    Path.Combine(appData, "Code", "CachedData"),
                    Path.Combine(appData, "Code", "CachedExtensionVSIXs")
                };

                foreach (var dir in devCacheDirs)
                {
                    if (Directory.Exists(dir))
                    {
                        try
                        {
                            var di = new DirectoryInfo(dir);
                            foreach (var file in di.EnumerateFiles("*", SearchOption.AllDirectories))
                            {
                                try
                                {
                                    long len = file.Length;
                                    file.Delete();
                                    freed += len;
                                }
                                catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            CcLog.Ignore(ex);
                        }
                    }
                }

                return (true, freed, $"کش های محیط های برنامه نویسی (npm, pip, NuGet, Gradle, VS Code) پاکسازی شدند. ({SystemStats.FormatBytes(freed)} آزاد شد)");
            });
        }

        public static async Task<(bool Success, string Message)> CompactWsl2DiskAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("wsl", "--shutdown")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        p?.WaitForExit(10000);
                    }

                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var vhdxFiles = new List<string>();
                    string packagesDir = Path.Combine(localAppData, "Packages");
                    if (Directory.Exists(packagesDir))
                    {
                        try
                        {
                            vhdxFiles.AddRange(Directory.GetFiles(packagesDir, "ext4.vhdx", SearchOption.AllDirectories));
                        }
                        catch { }
                    }
                    string dockerWsl = Path.Combine(localAppData, "Docker", "wsl", "data");
                    if (Directory.Exists(dockerWsl))
                    {
                        try
                        {
                            vhdxFiles.AddRange(Directory.GetFiles(dockerWsl, "ext4.vhdx", SearchOption.AllDirectories));
                        }
                        catch { }
                    }

                    if (vhdxFiles.Count == 0)
                    {
                        return (true, "سرویس WSL2 خاموش و حافظه رم آزاد شد. فایل دیسک مجازی vhdx مجزا یافت نشد.");
                    }

                    int compacted = 0;
                    foreach (var vhdx in vhdxFiles)
                    {
                        string script = $"select vdisk file=\"{vhdx}\"\r\nattach vdisk readonly\r\ncompact vdisk\r\ndetach vdisk\r\n";
                        string tmpScript = Path.Combine(Path.GetTempPath(), $"compact_{Guid.NewGuid():N}.txt");
                        try
                        {
                            File.WriteAllText(tmpScript, script);
                            var dpPsi = new ProcessStartInfo("diskpart", $"/s \"{tmpScript}\"")
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            using var dp = Process.Start(dpPsi);
                            dp?.WaitForExit(60000);
                            compacted++;
                        }
                        finally
                        {
                            try { File.Delete(tmpScript); } catch { }
                        }
                    }

                    return (true, $"تعداد {compacted} دیسک مجازی WSL2 با موفقیت بهینه سازی و متراکم شد.");
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex);
                    return (false, "خطا در متراکم سازی دیسک WSL2.");
                }
            });
        }

        public static async Task<(bool Success, string Message)> CleanDismComponentsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    if (p == null) return (false, "امکان اجرای ابزار DISM وجود ندارد.");
                    p.WaitForExit(90000);
                    return p.ExitCode == 0
                        ? (true, "فروشگاه قطعات ویندوز (WinSxS / DISM) با موفقیت پاکسازی و بسته های قدیمی حذف شدند.")
                        : (false, "عملیات DISM با خطا یا نیاز به دسترسی Administrator پایان یافت.");
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex);
                    return (false, "خطا در اجرای ابزار پاکسازی DISM.");
                }
            });
        }
    }
}
