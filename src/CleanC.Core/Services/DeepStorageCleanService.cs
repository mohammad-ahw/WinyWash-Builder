using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;
using Microsoft.Win32;

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
                }.Where(d => Directory.Exists(d) && !LockRegistryService.IsUserLockedPath(d)).ToList();

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

                // D05: نام محصولات نصب شده برای تطبیق فازی با نصب کننده ها — فقط در صورت
                // نیاز لود می شود (lazy) تا هزینه ی خواندن رجیستری بیهوده پرداخت نشود.
                List<string>? installedProducts = null;

                foreach (var dir in targetDirectories)
                {
                    progress?.Invoke($"در حال بررسی پوشه: {Path.GetFileName(dir)}...");

                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);

                        // Empty folders check (D09 — فقط در پوشه های کاربری/موقت، هرگز در پروژه/سیستم)
                        try
                        {
                            bool isSafeDirForEmptyFolders = dir.Contains("Downloads", StringComparison.OrdinalIgnoreCase) ||
                                                           dir.Contains("Desktop", StringComparison.OrdinalIgnoreCase) ||
                                                           dir.Contains("Documents", StringComparison.OrdinalIgnoreCase) ||
                                                           dir.Contains("Videos", StringComparison.OrdinalIgnoreCase) ||
                                                           dir.Contains("Music", StringComparison.OrdinalIgnoreCase) ||
                                                           dir.Contains("Temp", StringComparison.OrdinalIgnoreCase);

                            if (isSafeDirForEmptyFolders)
                            {
                                foreach (var subDir in dirInfo.EnumerateDirectories("*", enumOptions))
                                {
                                    try
                                    {
                                        string fullName = subDir.FullName;

                                        // قفل کاربر: پوشه های قفل شده پیشنهاد نمی شوند
                                        if (LockRegistryService.IsUserLockedPath(fullName)) continue;

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
                                            // D09: گیت زمانی ۳۰ روزه — پوشه ای که تازه خالی شده
                                            // ممکن است بخشی از کار جاری کاربر باشد و پیشنهاد نمی شود.
                                            if ((now - subDir.LastWriteTime).TotalDays < 30) continue;

                                            emptyFolders.Add(new EmptyFolderInfo
                                            {
                                                FolderPath = subDir.FullName,
                                                FolderName = subDir.Name,
                                                ParentPath = subDir.Parent?.FullName ?? string.Empty,
                                                IsSelected = false,
                                                Evidence = string.Format(LocalizationService.Get("EvEmptyFolder30Days", "بیش از ۳۰ روز خالی مانده (آخرین تغییر: {0:yyyy/MM/dd})"), subDir.LastWriteTime)
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
                                // قفل کاربر: فایل های قفل شده (یا داخل پوشه ی قفل شده) پیشنهاد نمی شوند
                                if (LockRegistryService.IsUserLockedPath(file.FullName)) continue;

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
                                    // Smart Scenario 2: Incomplete / Broken Downloads (D06 — آستانه ۲۴h، temp ۷۲h)
                                    else if (ext.Equals(".crdownload", StringComparison.OrdinalIgnoreCase) ||
                                             ext.Equals(".part", StringComparison.OrdinalIgnoreCase) ||
                                             ext.Equals(".download", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // فایلی که در حال دانلود است مدام mtime عوض می کند؛ ۲۴ ساعت
                                        // بدون تغییر یعنی دانلود رها شده. (تب Clean برای حذف دائمی
                                        // محافظه کارتر است و ۷۲ ساعت می گیرد.)
                                        if ((now - file.LastWriteTime).TotalHours >= 24)
                                        {
                                            // شرط orphan: اگر فایل کاملِ نهایی همین دانلود وجود دارد،
                                            // قطعاً این فایل بازمانده ی یک دانلود تمام شده است.
                                            string completedPath = Path.Combine(
                                                file.DirectoryName ?? string.Empty,
                                                Path.GetFileNameWithoutExtension(file.Name));
                                            bool completedExists = File.Exists(completedPath);

                                            messItems.Add(new StorageMessItem
                                            {
                                                FilePath = file.FullName,
                                                FileName = file.Name,
                                                DirectoryPath = file.DirectoryName ?? string.Empty,
                                                SizeBytes = size,
                                                LastModified = file.LastWriteTime,
                                                Category = StorageMessCategory.IncompleteDownload,
                                                Evidence = completedExists
                                                    ? string.Format(LocalizationService.Get("EvDownloadCompleted", "فایل کامل «{0}» موجود است — بازمانده ی دانلود تمام شده"), Path.GetFileName(completedPath))
                                                    : LocalizationService.Get("EvDownloadAbandoned", "بیش از ۲۴ ساعت بدون تغییر — دانلود رهاشده"),
                                                IsSelected = false
                                            });
                                        }
                                    }
                                    else if (ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase) && (now - file.LastWriteTime).TotalHours >= 72)
                                    {
                                        messItems.Add(new StorageMessItem
                                        {
                                            FilePath = file.FullName,
                                            FileName = file.Name,
                                            DirectoryPath = file.DirectoryName ?? string.Empty,
                                            SizeBytes = size,
                                            LastModified = file.LastWriteTime,
                                            Category = StorageMessCategory.IncompleteDownload,
                                            Evidence = LocalizationService.Get("EvTmpOld72h", "فایل موقت .tmp قدیمی تر از ۷۲ ساعت در پوشه ی دانلود/دسکتاپ"),
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
                                    // Old setup/installers in downloads/desktop > 14 days old (D05 — تطبیق فازی)
                                    else if (installerExtensions.Contains(ext) && (now - file.LastWriteTime).TotalDays > 14)
                                    {
                                        installedProducts ??= LoadInstalledProductNames();
                                        string? matchedProduct = MatchInstalledProduct(name, installedProducts);

                                        messItems.Add(new StorageMessItem
                                        {
                                            FilePath = file.FullName,
                                            FileName = file.Name,
                                            DirectoryPath = file.DirectoryName ?? string.Empty,
                                            SizeBytes = size,
                                            LastModified = file.LastWriteTime,
                                            Category = StorageMessCategory.OldInstaller,
                                            Evidence = matchedProduct != null
                                                ? string.Format(LocalizationService.Get("EvInstallerMatched", "نصب کننده ی «{0}» — برنامه نصب شده و این فایل دیگر لازم نیست"), matchedProduct)
                                                : LocalizationService.Get("EvInstallerNoMatch", "نصب کننده ی قدیمی (بیش از ۱۴ روز) — برنامه ی هم نامی روی سیستم پیدا نشد"),
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
                // 1.7 D16 — بازمانده های sync / دستگاه (CrossDevice، تمپ OneDrive)
                //      همیشه پیش فرض خاموش (تصمیم T3) — فقط با تیک دستی کاربر.
                //      این پوشه ها انبار واسط انتقال فایل هستند و حذفشان
                //      هیچ فایل اصلی کاربرها را از بین نمی برد.
                // -------------------------------------------------------------
                var syncLeftovers = new (string Path, string Name, string EvidenceDesc)[]
                {
                    (Path.Combine(userProfile, "CrossDevice"),
                        LocalizationService.Get("EvCrossDevice", "مبادله ی موقت دستگاه ها (CrossDevice)"),
                        LocalizationService.Get("EvCrossDeviceSub", "پوشه ی واسط Link to Windows / Phone Link برای انتقال فایل بین گوشی و کامپیوتر")),
                    (Path.Combine(userProfile, "OneDrive", ".849C9593-D756-4E56-8D6E-42412F2A707B"),
                        LocalizationService.Get("EvOneDriveTemp", "پوشه ی موقت OneDrive"),
                        LocalizationService.Get("EvOneDriveTempSub", "انبار موقت فایل های در حال آپلود OneDrive که رها شده است"))
                };

                foreach (var (sPath, sName, sEvidence) in syncLeftovers)
                {
                    try
                    {
                        if (!Directory.Exists(sPath)) continue;
                        if (LockRegistryService.IsLockedPath(sPath)) continue;

                        long size = RecycleBinService.ComputeDirectorySize(sPath);
                        if (size < 1024 * 1024) continue; // زیر ۱ مگابایت ارزش پیشنهاد ندارد

                        messItems.Add(new StorageMessItem
                        {
                            FilePath = sPath,
                            FileName = sName,
                            DirectoryPath = Path.GetDirectoryName(sPath) ?? sPath,
                            SizeBytes = size,
                            LastModified = Directory.GetLastWriteTime(sPath),
                            Category = StorageMessCategory.SyncDeviceLeftover,
                            Evidence = sEvidence,
                            IsSelected = false
                        });
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
                        // مرحله دوم — هش کامل فایل: هش ۶۴ کیلوبایتی نخست فقط «نامزدِ»
                        // یکی بودن را پیدا می کند؛ فایل هایی که فقط ۶۴ کیلوبایت اولشان
                        // یکسان است ولی ادامه شان متفاوت (ایزو، ستاپ، آتلاس تصویری)
                        // باید با هش کامل جدا شوند وگرنه بین دوتایی های واقعی جعل می شدند.
                        var filesByFullHash = new Dictionary<string, List<FileInfo>>();
                        foreach (var file in hashGroup.Value.OrderBy(f => f.CreationTime))
                        {
                            try
                            {
                                string fullHash = ComputeFullFileHash(file.FullName, md5);
                                if (string.IsNullOrEmpty(fullHash)) continue;
                                if (!filesByFullHash.TryGetValue(fullHash, out var list))
                                {
                                    list = new List<FileInfo>();
                                    filesByFullHash[fullHash] = list;
                                }
                                list.Add(file);
                            }
                            catch (Exception __ex)
                            {
                                CcLog.Ignore(__ex);
                            }
                        }

                        foreach (var fullGroup in filesByFullHash.Where(kvp => kvp.Value.Count > 1))
                        {
                            duplicateGroups.Add(BuildDuplicateGroup(
                                fullGroup.Key,
                                fullGroup.Value.OrderBy(f => f.CreationTime).ToList()));
                        }
                    }
                }

                // -------------------------------------------------------------
                // 2.5 Detectors فاز ۶ — D10 (ریشه ی درایو) / D04 (بازمانده ی آپدیتر) / D17 (فایل حساس)
                //     (D03 یعنی LiveKernelReports در تب Clean و SystemScanService است، نه اینجا)
                // -------------------------------------------------------------
                progress?.Invoke(LocalizationService.Get("ScanProgressDriveRoots", "در حال بررسی ریشه ی درایوها و بازمانده ی آپدیترها..."));
                messItems.AddRange(ScanOemDriveRootLeftovers());
                messItems.AddRange(ScanUpdaterLeftovers());

                // D17: فایل های حساس از دل همان فایل های اسکن شده ی قبلی جدا می شوند (بدون IO اضافه)
                // و فقط در SensitiveFiles می نشینند — نه در MessItems، چون باید صرفاً گزارش باشند.
                result.SensitiveFiles = ScanSensitiveFiles(filesBySize);

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

        // هش کامل — فقط برای گروه های کوچکی که از فیلتر اول (۶۴KB اول + هم اندازه بودن) رد شده اند؛
        // هزینه واقعی خواندن تمام فایل فقط برای همین نومزدهای نهایی پرداخته می شود.
        private static string ComputeFullFileHash(string filePath, MD5 md5)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
                byte[] hash = md5.ComputeHash(stream);
                return Convert.ToHexString(hash);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return string.Empty;
        }

        private static DuplicateFileGroup BuildDuplicateGroup(string hash, List<FileInfo> sortedFiles)
        {
            if (sortedFiles.Count == 0) throw new ArgumentException(nameof(sortedFiles));
            var group = new DuplicateFileGroup
            {
                FileHash = hash,
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
            return group;
        }

        // ====================================================================
        // D05 helpers — تطبیق فازی نصب کننده با برنامه های نصب شده
        // ====================================================================

        // توکن هایی که در نام نصب کننده ها عمومی هستند و به تنهایی مدرک تطبیق نیستند
        // (وگرنه «setup.exe» با هر محصولی که کلمه Setup دارد تطبیق داده می شد).
        private static readonly HashSet<string> GenericInstallerTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "setup", "install", "installer", "update", "updater", "driver", "drivers",
            "win", "windows", "x64", "x86", "amd64", "arm64", "full", "online",
            "offline", "web", "beta", "final", "latest", "new", "app", "desktop"
        };

        // نام محصولات نصب شده از Uninstall registry (HKLM 64/32 + HKCU)
        private static List<string> LoadInstalledProductNames()
        {
            var names = new List<string>();
            string[] keyPaths =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var keyPath in keyPaths)
                {
                    try
                    {
                        using var key = hive.OpenSubKey(keyPath);
                        if (key == null) continue;

                        foreach (var sub in key.GetSubKeyNames())
                        {
                            using var app = key.OpenSubKey(sub);
                            string? display = app?.GetValue("DisplayName")?.ToString();
                            if (!string.IsNullOrWhiteSpace(display)) names.Add(display);
                        }
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
                }
            }

            return names;
        }

        // تطبیق فازی نام فایل نصب کننده با نام محصولات نصب شده (D05):
        // توکن های ≥۴ حرفی و غیرعمومی نام فایل را در نام محصولات جستجو می کنیم.
        // برگشت: نام محصول منطبق — یا null اگر تطبیق مطمئنی پیدا نشد.
        private static string? MatchInstalledProduct(string fileName, List<string> installedProducts)
        {
            if (installedProducts.Count == 0) return null;

            string baseName = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            var tokens = baseName
                .Split(new[] { ' ', '-', '_', '.', '(', ')', '+' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 4 && t.All(char.IsLetterOrDigit) && !GenericInstallerTokens.Contains(t))
                .Distinct()
                .ToList();

            if (tokens.Count == 0) return null;

            foreach (var product in installedProducts)
            {
                string lowerProduct = product.ToLowerInvariant();
                if (tokens.Any(t => lowerProduct.Contains(t, StringComparison.Ordinal)))
                {
                    return product;
                }
            }

            return null;
        }

        // ====================================================================
        // Detectors فاز ۶ (قدم ۲): D10 / D04 / D17
        // ====================================================================

        // نام پوشه های ریشه ی درایو که «بازمانده ی نصب سازنده» محسوب می شوند.
        // عمداً فقط همین ۵ مورد (طبق SPEC) — لیست بسته، بدون الگوی باز، تا
        // هیچ وقت سراغ پوشه های عادی کاربر در ریشه ی درایو نرویم.
        private static readonly Dictionary<string, string> OemRootFolderAllowlist = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NVIDIA"] = "NVIDIA",
            ["Aomei"] = "AOMEI",
            ["Intel"] = "Intel",
            ["AMD"] = "AMD",
            ["MSI"] = "MSI"
        };

        /// <summary>
        /// D10 — بازمانده ی نصاب سازنده ها در «ریشه ی» درایوهای ثابت (C:\NVIDIA، C:\Aomei، ...).
        /// فقط وقتی پیشنهاد می شود که مدرک قطعی وجود داشته باشد: یک فایل Setup/install/msi
        /// در چند سطح اول پوشه پیدا شود. چون مدرک قطعی است، پیش فرض تیک خورده (تصمیم T2).
        /// </summary>
        private static List<StorageMessItem> ScanOemDriveRootLeftovers()
        {
            var found = new List<StorageMessItem>();

            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                        string root = drive.RootDirectory.FullName;

                        foreach (var dir in Directory.EnumerateDirectories(root))
                        {
                            try
                            {
                                string name = Path.GetFileName(dir);
                                if (!OemRootFolderAllowlist.TryGetValue(name, out string? vendor)) continue;

                                // قفل پیش فرض یا کاربر = اصلاً پیشنهاد نده
                                if (LockRegistryService.IsLockedPath(dir)) continue;

                                string? evidence = FindDriveRootEvidence(dir, vendor, maxDepth: 3);
                                if (evidence == null) continue; // بدون مدرک قطعی، سکوت می کنیم

                                long size = RecycleBinService.ComputeDirectorySize(dir);
                                if (size <= 0) continue;

                                found.Add(new StorageMessItem
                                {
                                    FilePath = dir,
                                    FileName = name,
                                    DirectoryPath = root,
                                    SizeBytes = size,
                                    LastModified = Directory.GetLastWriteTime(dir),
                                    Category = StorageMessCategory.OemDriveRootLeftover,
                                    Evidence = string.Format(LocalizationService.Get("EvDriveRootLeftover", "پوشه ی ریشه ی درایو با نام سازنده ی {0} — مدرک قطعی: {1}"), vendor, evidence),
                                    RequiresAdmin = true,
                                    IsSelected = true
                                });
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
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return found;
        }

        // جستجوی «مدرک قطعی» برای D10 در عمق محدود. دو نوع مدرک قابل قبول است:
        //   1) فایل نصاب واقعی: نامش شامل setup/install باشد و پسوندش exe/msi باشد
        //   2) آرتیفکت اختصاصی سازنده: نام فایل با نام سازنده شروع شود و تصویر/بسته باشد
        //      (مثل AomeiBoot.wim برای AOMEI یا بسته های درایور)
        // FirstOrDefault باعث می شود به محض پیدا شدن اولین مدرک، پیمایش فایل ها قطع شود
        // (پوشه های NVIDIA می توانند چند گیگابایت فایل داشته باشند).
        private static string? FindDriveRootEvidence(string dir, string vendor, int maxDepth)
        {
            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = maxDepth,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden
                };

                string? hit = Directory.EnumerateFiles(dir, "*", options).FirstOrDefault(file =>
                {
                    string n = Path.GetFileName(file);
                    string lower = n.ToLowerInvariant();

                    bool looksLikeInstaller =
                        (lower.Contains("setup", StringComparison.Ordinal) || lower.Contains("install", StringComparison.Ordinal)) &&
                        (lower.EndsWith(".exe", StringComparison.Ordinal) || lower.EndsWith(".msi", StringComparison.Ordinal));

                    bool isVendorArtifact =
                        lower.StartsWith(vendor.ToLowerInvariant(), StringComparison.Ordinal) &&
                        (lower.EndsWith(".wim", StringComparison.Ordinal) || lower.EndsWith(".sdi", StringComparison.Ordinal) ||
                         lower.EndsWith(".esd", StringComparison.Ordinal) || lower.EndsWith(".iso", StringComparison.Ordinal) ||
                         lower.EndsWith(".img", StringComparison.Ordinal) || lower.EndsWith(".exe", StringComparison.Ordinal) ||
                         lower.EndsWith(".msi", StringComparison.Ordinal));

                    return looksLikeInstaller || isVendorArtifact;
                });

                if (hit == null) return null;

                string hitName = Path.GetFileName(hit);
                string hitLower = hitName.ToLowerInvariant();
                bool isInstaller = (hitLower.Contains("setup", StringComparison.Ordinal) || hitLower.Contains("install", StringComparison.Ordinal)) &&
                                   (hitLower.EndsWith(".exe", StringComparison.Ordinal) || hitLower.EndsWith(".msi", StringComparison.Ordinal));

                return isInstaller
                    ? string.Format(LocalizationService.Get("EvRemnantInstallerFile", "فایل نصب باقی مانده «{0}»"), hitName)
                    : string.Format(LocalizationService.Get("EvVendorArtifact", "آرتیفکت اختصاصی سازنده «{0}» (تصویر بوت/بسته ی درایور)"), hitName);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
                return null;
            }
        }

        /// <summary>
        /// D04 — بازمانده ی آپدیترهای Electron/Squirrel در LocalAppData:
        /// پوشه های *-updater، پوشه ی SquirrelTemp و فایل های .blockmap قدیمی.
        /// همیشه پیش فرض «خاموش» (تصمیم T3) — فقط کاربر خودش با تیک دستی انتخاب می کند.
        /// </summary>
        private static List<StorageMessItem> ScanUpdaterLeftovers()
        {
            var found = new List<StorageMessItem>();

            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(localApp) || !Directory.Exists(localApp)) return found;

                // 1) پوشه های *-updater و SquirrelTemp
                foreach (var dir in Directory.EnumerateDirectories(localApp))
                {
                    try
                    {
                        string name = Path.GetFileName(dir);
                        bool looksLikeUpdater =
                            name.EndsWith("-updater", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("SquirrelTemp", StringComparison.OrdinalIgnoreCase);
                        if (!looksLikeUpdater) continue;

                        if (LockRegistryService.IsLockedPath(dir)) continue;

                        long size = RecycleBinService.ComputeDirectorySize(dir);
                        if (size < 1024 * 1024) continue; // زیر ۱ مگابایت ارزش پیشنهاد ندارد

                        string? pendingInstaller = FindUpdaterInstallerEvidence(dir);

                        found.Add(new StorageMessItem
                        {
                            FilePath = dir,
                            FileName = name,
                            DirectoryPath = localApp,
                            SizeBytes = size,
                            LastModified = Directory.GetLastWriteTime(dir),
                            Category = StorageMessCategory.UpdaterLeftover,
                            Evidence = pendingInstaller != null
                                ? string.Format(LocalizationService.Get("EvUpdaterPendingInstaller", "پوشه ی آپدیتر شامل نصب کننده ی باقی مانده «{0}» — آپدیت نیمه تمام"), pendingInstaller)
                                : LocalizationService.Get("EvUpdaterNoPending", "پوشه ی آپدیتر با الگوی *-updater / SquirrelTemp (نصب در انتظاری پیدا نشد)"),
                            RequiresAdmin = false,
                            IsSelected = false
                        });
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
                }

                // 2) فایل های .blockmap قدیمی (بازمانده ی دانلود آپدیت Squirrel)
                foreach (var file in Directory.EnumerateFiles(localApp, "*.blockmap", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if ((DateTime.Now - fi.LastWriteTime).TotalDays < 30) continue;
                        if (LockRegistryService.IsLockedPath(file)) continue;

                        found.Add(new StorageMessItem
                        {
                            FilePath = fi.FullName,
                            FileName = fi.Name,
                            DirectoryPath = fi.DirectoryName ?? localApp,
                            SizeBytes = fi.Length,
                            LastModified = fi.LastWriteTime,
                            Category = StorageMessCategory.UpdaterLeftover,
                            Evidence = LocalizationService.Get("EvBlockmapOld", "فایل .blockmap بازمانده ی آپدیتر الکترون (بیش از ۳۰ روز دست نخورده)"),
                            RequiresAdmin = false,
                            IsSelected = false
                        });
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

            return found;
        }

        // آیا پوشه ی آپدیتر «نصب در انتظار» دارد؟ (pending/*.exe|msi|nupkg یا installer/setup در سطح اول)
        private static string? FindUpdaterInstallerEvidence(string dir)
        {
            try
            {
                string pending = Path.Combine(dir, "pending");
                if (Directory.Exists(pending))
                {
                    string? pendingHit = Directory.EnumerateFiles(pending, "*", SearchOption.TopDirectoryOnly).FirstOrDefault(f =>
                        f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
                    if (pendingHit != null) return Path.GetFileName(pendingHit);
                }

                string? topHit = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly).FirstOrDefault(f =>
                {
                    string n = Path.GetFileName(f);
                    return n.StartsWith("install", StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith("setup", StringComparison.OrdinalIgnoreCase);
                });
                return topHit == null ? null : Path.GetFileName(topHit);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
                return null;
            }
        }

        // الگوهای نامِ فایل حساس (D17). اگر نام فایل یکی از این توکن ها را داشته باشد
        // یا پسوندش جزو پسوندهای امنیتی باشد، فقط «گزارش» می شود.
        // برچسب ها به صورت کلید کاتالوگ ذخیره می شوند تا در زمان تطبیق
        // (نه در زمان بارگذاری کلاس) به زبان جاری ترجمه شوند.
        private static readonly (string Token, string LabelKey, string LabelFa)[] SensitiveNamePatterns =
        {
            ("license", "SensitiveLicense", "فایل لایسنس نرم‌افزار"),
            ("serial", "SensitiveSerial", "شماره سریال نرم‌افزار"),
            ("productkey", "SensitiveProductKey", "کلید محصول (Product Key)"),
            ("product key", "SensitiveProductKey", "کلید محصول (Product Key)"),
            ("activation", "SensitiveActivation", "کد فعال‌سازی"),
            ("passwords", "SensitivePasswords", "فایل رمزهای عبور"),
            ("id_rsa", "SensitiveSshKey", "کلید خصوصی SSH"),
            ("privatekey", "SensitivePrivateKey", "کلید خصوصی"),
            ("private key", "SensitivePrivateKey", "کلید خصوصی")
        };

        private static readonly string[] SensitiveExtensions = { ".kdbx", ".pem", ".pfx", ".key", ".pgp" };

        /// <summary>
        /// D17 — فایل های حساس در پوشه های کاربری (Downloads / Desktop / Documents).
        /// داده از همان فایل های اسکن اصلی می آید (filesBySize) پس IO اضافه ای ندارد.
        /// این لیست «فقط گزارش» است؛ UI هیچ گزینه ی حذفی برایش نشان نمی دهد و
        /// عمداً از MessItems جدا نگه داشته می شود.
        /// </summary>
        private static List<SensitiveFileInfo> ScanSensitiveFiles(Dictionary<long, List<FileInfo>> filesBySize)
        {
            var found = new List<SensitiveFileInfo>();

            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string downloads = Path.Combine(userProfile, "Downloads");
                string desktop = Path.Combine(userProfile, "Desktop");
                string documents = Path.Combine(userProfile, "Documents");

                foreach (var list in filesBySize.Values)
                {
                    foreach (var fi in list)
                    {
                        if (found.Count >= 100) break;

                        // فقط پوشه های کاربری — Temp و مسیرهای سیستمی کاری نداریم
                        string full = fi.FullName;
                        if (!IsUnderPath(full, downloads) && !IsUnderPath(full, desktop) && !IsUnderPath(full, documents))
                            continue;

                        string? reason = GetSensitiveMatchReason(fi.Name);
                        if (reason == null) continue;

                        found.Add(new SensitiveFileInfo
                        {
                            FilePath = full,
                            FileName = fi.Name,
                            DirectoryPath = fi.DirectoryName ?? string.Empty,
                            SizeBytes = fi.Length,
                            LastModified = fi.LastWriteTime,
                            MatchReason = reason
                        });
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return found.OrderByDescending(f => f.LastModified).ToList();
        }

        private static string? GetSensitiveMatchReason(string fileName)
        {
            string lower = fileName.ToLowerInvariant();

            foreach (var (token, labelKey, labelFa) in SensitiveNamePatterns)
            {
                if (lower.Contains(token, StringComparison.Ordinal)) return LocalizationService.Get(labelKey, labelFa);
            }

            string ext = Path.GetExtension(lower);
            foreach (var sensitiveExt in SensitiveExtensions)
            {
                if (ext == sensitiveExt)
                    return string.Format(LocalizationService.Get("SensitiveKeyFile", "فایل کلید/امنیتی ({0})"), sensitiveExt);
            }

            return null;
        }

        private static bool IsUnderPath(string fullPath, string root)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(root)) return false;
            string prefix = root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        // همه ی حذف های فایلِ شخصی از اینجا به SafeDeleteService می روند تا
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
                    // پراِن داکر روی کش های حجیم می تواند طولانی باشد؛ تایم اوت قبلی
                    // سرانجام فریم های خروجی را نصفه می گرداند و خطای ناشناخته می داد
                    bool ok = CleanC.Core.Native.ProcessRunner.WaitAndKillOnTimeout(p, 300000, out int dockerCode);
                    return ok && dockerCode == 0
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
                        CleanC.Core.Native.ProcessRunner.WaitAndKillOnTimeout(p, 30000, out _);
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
                    int compactionFailures = 0;
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
                            // متراکم‌سازی دیسک مجازی حجیم می‌تواند چند دقیقه طول بکشد؛
                            // قبلاً ۶۰ ثانیه‌ای بود و وسط کار کشیده می‌شد و از موفقیت هم شمرده می‌شد!
                            if (!CleanC.Core.Native.ProcessRunner.WaitAndKillOnTimeout(dp, 900000, out _))
                            {
                                compactionFailures++;
                            }
                            else
                            {
                                compacted++;
                            }
                        }
                        finally
                        {
                            try { File.Delete(tmpScript); } catch { }
                        }
                    }

                    string finalMsg = compactionFailures > 0
                        ? $"تعداد {compacted} دیسک مجازی WSL2 متراکم شد؛ {compactionFailures} مورد کامل نشد (دیسک حجم غالب می تواند کامل نشود)."
                        : $"تعداد {compacted} دیسک مجازی WSL2 با موفقیت بهینه سازی و متراکم شد.";
                    return (true, finalMsg);
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
                    // پاکسازی WinSxS معمولاً چند دقیقه طول می کشد؛ ۹۰ ثانیه ی قبلی
                    // Escaped زمان خروج را نصفه می کشید و بی سر و صدا «خطا» می گذاشت.
                    bool ok = CleanC.Core.Native.ProcessRunner.WaitAndKillOnTimeout(p, 900000, out int code);
                    return ok && code == 0
                        ? (true, "فروشگاه قطعات ویندوز (WinSxS / DISM) با موفقیت پاکسازی و بسته های قدیمی حذف شدند.")
                        : (false, "عملیات DISM با خطا، تایم اوت یا نیاز به دسترسی Administrator پایان یافت.");
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
