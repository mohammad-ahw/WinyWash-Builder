using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class LeftoverFolderItem
    {
        public string FolderName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string LocationType { get; set; } = "AppData"; // "AppData (Roaming)", "AppData (Local)", "AppData (LocalLow)", "ProgramData", "User Profile", "Public"
    }

    public class LeftoverCleanerService
    {
        // Protected vendor & system directories that should NEVER be considered leftovers
        private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft", "Windows", "WindowsApps", "Packages", "Common Files", "Internet Explorer",
            "NVIDIA Corporation", "NVIDIA", "AMD", "Intel", "Realtek", "Synaptics", "Google",
            "Mozilla", "CleanC", "Git", "dotnet", "Adobe", "Steam", "Epic Games", "Discord",
            "Telegram Desktop", "Spotify", "Temp", "CrashDumps", "Programs", "System",
            "Downloaded Installations", "Package Cache", "OEM", "ASUS", "Lenovo", "Dell", "HP", "MSI",
            "PowerShell", "dotnet-tools", "yarn", "npm", "pip", "vcpkg", "VirtualStore",
            "Apple", "Apple Computer", "Sony", "Logitech", "Corsair", "Razer", "SteelSeries",
            "Kingston", "Samsung", "Crucial", "Western Digital", "Seagate", "Oracle", "Java",
            "Android", "JetBrains", "Unity", "EpicGamesLauncher"
        };

        // Protected personal, system, and workspace folders in C:\Users\<Username> that must NEVER be touched under any circumstances
        private static readonly HashSet<string> ProtectedUserProfileFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            // Standard Windows Shell & User data folders
            "Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos",
            "Favorites", "Links", "Contacts", "Searches", "Saved Games", "3D Objects",
            "OneDrive", "AppData", "Cookies", "NetHood", "PrintHood", "Recent", "SendTo",
            "Start Menu", "Templates", "Application Data", "Local Settings", "My Documents",

            // User Work, Code Repositories, and Cloud Sync folders
            "Projects", "Source", "source", "Repos", "repos", "Workspace", "workspace",
            "Development", "Codes", "Code", "Work", "my files", "Dropbox", "Google Drive",
            "iCloudDrive", "Virtual Machines", "VMs", "Box", "Creative Cloud Files",

            // Active Developer & System Tools dot-directories
            ".ssh", ".git", ".gemini", ".antigravity", ".antigravity-code", ".vscode",
            ".vscode-insiders", ".cursor", ".copilot", ".claude", ".openai", ".npm",
            ".node", ".yarn", ".nuget", ".dotnet", ".cargo", ".rustup", ".conda",
            ".anaconda", ".jupyter", ".matplotlib", ".aws", ".azure", ".kube", ".docker",
            ".config", ".local", ".gnupg", ".pki", ".platformio", ".android", ".gradle",
            ".m2", ".atom", ".eclipse"
        };

        // Protected folders in C:\Users\Public
        private static readonly HashSet<string> ProtectedPublicFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos",
            "Libraries", "AccountPictures", "Recorded Calls"
        };

        // Extensions that identify personal user documents, media, or source code projects - if found, folder is NEVER a leftover!
        private static readonly HashSet<string> PersonalDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf",
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".psd", ".ai",
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".mp3", ".wav", ".flac",
            ".zip", ".rar", ".7z", ".tar", ".gz",
            ".sln", ".csproj", ".vbproj", ".fsproj", ".vcxproj",
            ".cs", ".cpp", ".c", ".h", ".py", ".rs", ".go", ".java", ".ts", ".blend"
        };

        public static List<LeftoverFolderItem> ScanLeftoverFolders()
        {
            var leftovers = new List<LeftoverFolderItem>();

            try
            {
                // 1. Collect all installed application names and signatures
                var installedNames = GetInstalledSoftwareSignatures();

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string localLow = Path.Combine(userProfile, @"AppData\LocalLow");
                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string publicDir = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";

                // 2. Scan AppData (Roaming)
                ScanCandidateDirectory(appData, "AppData (Roaming)", installedNames, leftovers);

                // 3. Scan LocalAppData
                ScanCandidateDirectory(localApp, "AppData (Local)", installedNames, leftovers);

                // 4. Scan LocalLow (games, unity, legacy tools)
                if (Directory.Exists(localLow))
                {
                    ScanCandidateDirectory(localLow, "AppData (LocalLow)", installedNames, leftovers);
                }

                // 5. Scan ProgramData
                ScanCandidateDirectory(programData, "ProgramData", installedNames, leftovers);

                // 6. Scan User Profile root (C:\Users\<Username>) with strict personal file protection
                ScanUserProfileDirectory(userProfile, installedNames, leftovers);

                // 7. Scan Public folder (C:\Users\Public)
                if (Directory.Exists(publicDir))
                {
                    ScanPublicDirectory(publicDir, installedNames, leftovers);
                }
            }
            catch { }

            return leftovers;
        }

        private static void ScanCandidateDirectory(string rootPath, string locType, HashSet<string> installedSignatures, List<LeftoverFolderItem> result)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return;

            try
            {
                foreach (var dir in Directory.GetDirectories(rootPath))
                {
                    string dirName = Path.GetFileName(dir);

                    if (ProtectedNames.Contains(dirName)) continue;
                    if (dirName.StartsWith(".") || dirName.StartsWith("$")) continue;

                    // Skip junctions / symlinks
                    try
                    {
                        var di = new DirectoryInfo(dir);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { continue; }

                    // Check if folder name matches any currently installed software or Program Files folder
                    bool isMatched = installedSignatures.Any(sig =>
                        sig.Equals(dirName, StringComparison.OrdinalIgnoreCase) ||
                        (dirName.Length >= 4 && sig.Contains(dirName, StringComparison.OrdinalIgnoreCase)) ||
                        (sig.Length >= 4 && dirName.Contains(sig, StringComparison.OrdinalIgnoreCase)));

                    if (!isMatched)
                    {
                        // Check if corresponding executable exists in Program Files or LocalAppData\Programs
                        if (!ExistsInProgramFiles(dirName) && !ExistsInLocalAppDataPrograms(dirName))
                        {
                            long size = GetDirectorySizeSafe(dir);
                            if (size > 1024 * 1024) // Only consider significant leftovers (> 1 MB)
                            {
                                result.Add(new LeftoverFolderItem
                                {
                                    FolderName = dirName,
                                    FullPath = dir,
                                    SizeBytes = size,
                                    LocationType = locType
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void ScanUserProfileDirectory(string userProfile, HashSet<string> installedSignatures, List<LeftoverFolderItem> result)
        {
            if (string.IsNullOrWhiteSpace(userProfile) || !Directory.Exists(userProfile)) return;

            try
            {
                foreach (var dir in Directory.GetDirectories(userProfile))
                {
                    string dirName = Path.GetFileName(dir);

                    // 1. Never touch protected user folders (Documents, Desktop, Downloads, Projects, .ssh, .git, etc.)
                    if (ProtectedUserProfileFolders.Contains(dirName) || ProtectedNames.Contains(dirName))
                        continue;

                    // 2. Ignore system or hidden NTUSER/desktop files/junctions
                    try
                    {
                        var di = new DirectoryInfo(dir);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { continue; }

                    // 3. Ignore IDE dot-directories (e.g. .IntelliJIdea2023.1, .PyCharm2024.1, etc.)
                    if (dirName.StartsWith(".IntelliJ", StringComparison.OrdinalIgnoreCase) ||
                        dirName.StartsWith(".PyCharm", StringComparison.OrdinalIgnoreCase) ||
                        dirName.StartsWith(".WebStorm", StringComparison.OrdinalIgnoreCase) ||
                        dirName.StartsWith(".Rider", StringComparison.OrdinalIgnoreCase) ||
                        dirName.StartsWith(".CLion", StringComparison.OrdinalIgnoreCase) ||
                        dirName.StartsWith(".RustRover", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 4. Ironclad check: Directory MUST NOT contain personal user documents or git projects
                    if (HasPersonalOrProjectFiles(dir))
                        continue;

                    // 5. Check if folder name matches currently installed software
                    bool isMatched = installedSignatures.Any(sig =>
                        sig.Equals(dirName, StringComparison.OrdinalIgnoreCase) ||
                        (dirName.StartsWith(".") && sig.Equals(dirName.TrimStart('.'), StringComparison.OrdinalIgnoreCase)) ||
                        (dirName.Length >= 4 && sig.Contains(dirName, StringComparison.OrdinalIgnoreCase)) ||
                        (sig.Length >= 4 && dirName.Contains(sig, StringComparison.OrdinalIgnoreCase)));

                    if (!isMatched)
                    {
                        string strippedName = dirName.TrimStart('.');
                        if (!ExistsInProgramFiles(strippedName) && !ExistsInLocalAppDataPrograms(strippedName))
                        {
                            long size = GetDirectorySizeSafe(dir);
                            if (size > 1024 * 1024) // Only consider significant leftovers (> 1 MB)
                            {
                                result.Add(new LeftoverFolderItem
                                {
                                    FolderName = dirName,
                                    FullPath = dir,
                                    SizeBytes = size,
                                    LocationType = "C:\\Users (پروفایل کاربر)"
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void ScanPublicDirectory(string publicDir, HashSet<string> installedSignatures, List<LeftoverFolderItem> result)
        {
            if (string.IsNullOrWhiteSpace(publicDir) || !Directory.Exists(publicDir)) return;

            try
            {
                foreach (var dir in Directory.GetDirectories(publicDir))
                {
                    string dirName = Path.GetFileName(dir);

                    if (ProtectedPublicFolders.Contains(dirName) || ProtectedNames.Contains(dirName))
                        continue;

                    try
                    {
                        var di = new DirectoryInfo(dir);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { continue; }

                    if (HasPersonalOrProjectFiles(dir)) continue;

                    bool isMatched = installedSignatures.Any(sig =>
                        sig.Equals(dirName, StringComparison.OrdinalIgnoreCase) ||
                        (dirName.Length >= 4 && sig.Contains(dirName, StringComparison.OrdinalIgnoreCase)) ||
                        (sig.Length >= 4 && dirName.Contains(sig, StringComparison.OrdinalIgnoreCase)));

                    if (!isMatched && !ExistsInProgramFiles(dirName) && !ExistsInLocalAppDataPrograms(dirName))
                    {
                        long size = GetDirectorySizeSafe(dir);
                        if (size > 1024 * 1024)
                        {
                            result.Add(new LeftoverFolderItem
                            {
                                FolderName = dirName,
                                FullPath = dir,
                                SizeBytes = size,
                                LocationType = "C:\\Users\\Public"
                            });
                        }
                    }
                }
            }
            catch { }
        }

        private static bool HasPersonalOrProjectFiles(string dirPath)
        {
            try
            {
                if (Directory.Exists(Path.Combine(dirPath, ".git"))) return true;

                var dir = new DirectoryInfo(dirPath);
                int scannedCount = 0;

                foreach (var fi in dir.EnumerateFiles("*", new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 3
                }))
                {
                    scannedCount++;
                    if (PersonalDocumentExtensions.Contains(fi.Extension))
                    {
                        return true; // Contains personal document/media/project - protect it!
                    }

                    if (scannedCount > 100) break;
                }
            }
            catch { }

            return false;
        }

        private static bool ExistsInProgramFiles(string name)
        {
            string p1 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string p2 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            if (Directory.Exists(Path.Combine(p1, name)) || Directory.Exists(Path.Combine(p2, name))) return true;

            return false;
        }

        private static bool ExistsInLocalAppDataPrograms(string name)
        {
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programsDir = Path.Combine(localApp, "Programs");
                if (Directory.Exists(programsDir))
                {
                    if (Directory.Exists(Path.Combine(programsDir, name))) return true;
                }
            }
            catch { }
            return false;
        }

        private static HashSet<string> GetInstalledSoftwareSignatures()
        {
            var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] keys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var keyPath in keys)
            {
                try
                {
                    using var hklmKey = Registry.LocalMachine.OpenSubKey(keyPath);
                    if (hklmKey != null)
                    {
                        foreach (var sub in hklmKey.GetSubKeyNames())
                        {
                            using var app = hklmKey.OpenSubKey(sub);
                            string? name = app?.GetValue("DisplayName")?.ToString();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                foreach (var word in name.Split(new[] { ' ', '-', '_', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (word.Length >= 3) signatures.Add(word);
                                }
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    using var hkcuKey = Registry.CurrentUser.OpenSubKey(keyPath);
                    if (hkcuKey != null)
                    {
                        foreach (var sub in hkcuKey.GetSubKeyNames())
                        {
                            using var app = hkcuKey.OpenSubKey(sub);
                            string? name = app?.GetValue("DisplayName")?.ToString();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                foreach (var word in name.Split(new[] { ' ', '-', '_', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (word.Length >= 3) signatures.Add(word);
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // Top-level Software registry keys
            try
            {
                using var cuSoftware = Registry.CurrentUser.OpenSubKey("Software");
                if (cuSoftware != null)
                {
                    foreach (var sub in cuSoftware.GetSubKeyNames())
                    {
                        if (sub.Length >= 3) signatures.Add(sub);
                    }
                }
            }
            catch { }

            try
            {
                using var lmSoftware = Registry.LocalMachine.OpenSubKey("Software");
                if (lmSoftware != null)
                {
                    foreach (var sub in lmSoftware.GetSubKeyNames())
                    {
                        if (sub.Length >= 3) signatures.Add(sub);
                    }
                }
            }
            catch { }

            // Program Files and Program Files (x86)
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (Directory.Exists(pf))
                {
                    foreach (var d in Directory.GetDirectories(pf)) signatures.Add(Path.GetFileName(d));
                }
                string pfx = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (Directory.Exists(pfx))
                {
                    foreach (var d in Directory.GetDirectories(pfx)) signatures.Add(Path.GetFileName(d));
                }
            }
            catch { }

            // LocalAppData\Programs
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programs = Path.Combine(localApp, "Programs");
                if (Directory.Exists(programs))
                {
                    foreach (var d in Directory.GetDirectories(programs)) signatures.Add(Path.GetFileName(d));
                }
            }
            catch { }

            return signatures;
        }

        private static long GetDirectorySizeSafe(string dirPath)
        {
            long size = 0;
            try
            {
                var dir = new DirectoryInfo(dirPath);
                foreach (var fi in dir.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true }))
                {
                    size += fi.Length;
                }
            }
            catch { }
            return size;
        }
    }
}
