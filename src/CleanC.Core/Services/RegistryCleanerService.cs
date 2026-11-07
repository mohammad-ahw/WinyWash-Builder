// O:\OpenCode\AntiGravity\CleanC - NEW\src\CleanC.Core\Services\RegistryCleanerService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using CleanC.Core.Diagnostics;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class RegistryCleanerService
    {
        // هم اسکن و هم پاکسازی از همین لیست استفاده می کنند تا عدد گزارش شده با عدد واقعی یکی باشد.
        // به صورت method ساخته می شود تا handleهای ریشه در زمان بارگذاری نوع باز نمانند.
        private static (RegistryKey Root, string Path)[] GetUninstallRoots() => new (RegistryKey, string)[]
        {
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        };

        public static (int DeadMuiEntries, int OrphanedKeys) ScanOrphanedRegistry()
        {
            int deadMui = CountDeadMuiEntries();
            int orphanedKeys = 0;
            foreach (var (root, path) in GetUninstallRoots())
            {
                orphanedKeys += CollectDeadUninstallKeys(root, path).Count;
            }
            return (deadMui, orphanedKeys);
        }

        public static int CleanOrphanedRegistry()
        {
            int cleaned = 0;

            cleaned += CleanDeadMuiEntries();

            // هم HKCU و هم HKLM — بدون دسترسی Administrator پاکسازی HKLM سکوت می کند و چیزی شمرده نمی شود
            foreach (var (root, path) in GetUninstallRoots())
            {
                cleaned += CleanDeadUninstallKeys(root, path);
            }

            return cleaned;
        }

        // --------------------------------------------------------------------
        // MuiCache
        // --------------------------------------------------------------------

        private static bool IsMuiCacheCandidate(string valName, out string cleanPath)
        {
            cleanPath = string.Empty;
            try
            {
                cleanPath = valName.Split(new[] { ".FriendlyAppName", ".ApplicationCompany" }, StringSplitOptions.None)[0];
                if (!cleanPath.Contains(":")) return false;
                if (!cleanPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !cleanPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return false;

                // اگر درایو اصلاً وصل نیست (فلش/شبکه قطع) فایل «مفقود» نیست، فقط در دسترس نیست
                string? root = Path.GetPathRoot(cleanPath);
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return false;

                return !File.Exists(cleanPath);
            }
            catch
            {
                return false;
            }
        }

        private static int CountDeadMuiEntries()
        {
            int count = 0;
            try
            {
                using var muiKey = Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", false);
                if (muiKey == null) return 0;

                foreach (var valName in muiKey.GetValueNames())
                {
                    if (IsMuiCacheCandidate(valName, out _)) count++;
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return count;
        }

        private static int CleanDeadMuiEntries()
        {
            int cleaned = 0;
            try
            {
                using var muiKey = Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", true);
                if (muiKey == null) return 0;

                var toDelete = new List<string>();
                foreach (var valName in muiKey.GetValueNames())
                {
                    if (IsMuiCacheCandidate(valName, out _)) toDelete.Add(valName);
                }

                foreach (var del in toDelete)
                {
                    try
                    {
                        muiKey.DeleteValue(del, false);
                        // فقط چیزی را بشمار که واقعاً حذف شده باشد
                        if (muiKey.GetValue(del) == null) cleaned++;
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
            return cleaned;
        }

        // --------------------------------------------------------------------
        // Uninstall keys
        // --------------------------------------------------------------------

        /// <summary>
        /// کلیدهای Uninstall را جمع می کند که هم UninstallString دارند، هم مسیرشان
        /// روی درایوی موجود است و هم فایل واقعاً وجود ندارد.
        /// </summary>
        private static List<string> CollectDeadUninstallKeys(RegistryKey root, string path)
        {
            var dead = new List<string>();
            try
            {
                using var unKey = root.OpenSubKey(path, false);
                if (unKey == null) return dead;

                foreach (var sub in unKey.GetSubKeyNames())
                {
                    try
                    {
                        using var app = unKey.OpenSubKey(sub, false);
                        if (app == null) continue;

                        string? installLoc = app.GetValue("InstallLocation")?.ToString();
                        if (!string.IsNullOrWhiteSpace(installLoc) && Directory.Exists(installLoc))
                            continue; // Still exists

                        string? unString = app.GetValue("UninstallString")?.ToString();
                        if (string.IsNullOrWhiteSpace(unString)) continue;

                        string exePath = ExtractExePath(unString);
                        if (string.IsNullOrEmpty(exePath)) continue;
                        if (!exePath.Contains(":")) continue; // نام نسبی مثل MsiExec.exe → قابل قضاوت نیست

                        string? rootDrive = Path.GetPathRoot(exePath);
                        if (string.IsNullOrEmpty(rootDrive) || !Directory.Exists(rootDrive)) continue;

                        if (!File.Exists(exePath)) dead.Add(sub);
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
            return dead;
        }

        /// <summary>
        /// پشتیبان‌گیری مستقیم از کلیدهای رجیستری در قالب فایل استاندارد .reg با استفاده از APIهای داخلی دات‌نت
        /// (بدون اجرای reg.exe برای جلوگیری از حساسیت و هشدارهای غیرضروری آنتی‌ویروس‌ها).
        /// </summary>
        public static bool ExportKeyToRegFile(RegistryKey key, string fullKeyPath, string destinationFile)
        {
            try
            {
                string? dir = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                using var sw = new StreamWriter(destinationFile, false, System.Text.Encoding.Unicode);
                sw.WriteLine("Windows Registry Editor Version 5.00");
                ExportRegistryTreeRecursive(key, fullKeyPath, sw);
                sw.Flush();

                return File.Exists(destinationFile) && new FileInfo(destinationFile).Length > 0;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "RegistryCleaner.ExportKeyToRegFile");
                return false;
            }
        }

        private static void ExportRegistryTreeRecursive(RegistryKey key, string fullKeyPath, StreamWriter writer)
        {
            writer.WriteLine();
            writer.WriteLine($"[{fullKeyPath}]");

            foreach (var valueName in key.GetValueNames())
            {
                try
                {
                    var kind = key.GetValueKind(valueName);
                    var val = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    string nameFormatted = string.IsNullOrEmpty(valueName) ? "@" : $"\"{EscapeRegString(valueName)}\"";

                    switch (kind)
                    {
                        case RegistryValueKind.String:
                            writer.WriteLine($"{nameFormatted}=\"{EscapeRegString(val?.ToString() ?? "")}\"");
                            break;

                        case RegistryValueKind.DWord:
                            uint dw = val is int i ? (uint)i : Convert.ToUInt32(val);
                            writer.WriteLine($"{nameFormatted}=dword:{dw:x8}");
                            break;

                        case RegistryValueKind.QWord:
                            ulong qw = val is long l ? (ulong)l : Convert.ToUInt64(val);
                            byte[] qwBytes = BitConverter.GetBytes(qw);
                            writer.WriteLine($"{nameFormatted}=hex(b):{FormatHexBytes(qwBytes)}");
                            break;

                        case RegistryValueKind.Binary:
                            byte[] b = (byte[])(val ?? Array.Empty<byte>());
                            writer.WriteLine($"{nameFormatted}=hex:{FormatHexBytes(b)}");
                            break;

                        case RegistryValueKind.ExpandString:
                            byte[] expBytes = System.Text.Encoding.Unicode.GetBytes((val?.ToString() ?? "") + "\0");
                            writer.WriteLine($"{nameFormatted}=hex(2):{FormatHexBytes(expBytes)}");
                            break;

                        case RegistryValueKind.MultiString:
                            string[] parts = (string[])(val ?? Array.Empty<string>());
                            var msBytes = new List<byte>();
                            foreach (var s in parts)
                            {
                                msBytes.AddRange(System.Text.Encoding.Unicode.GetBytes(s + "\0"));
                            }
                            msBytes.AddRange(System.Text.Encoding.Unicode.GetBytes("\0"));
                            writer.WriteLine($"{nameFormatted}=hex(7):{FormatHexBytes(msBytes.ToArray())}");
                            break;

                        default:
                            if (val != null)
                                writer.WriteLine($"{nameFormatted}=\"{EscapeRegString(val.ToString() ?? "")}\"");
                            break;
                    }
                }
                catch { }
            }

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subKeyName, false);
                    if (subKey != null)
                    {
                        ExportRegistryTreeRecursive(subKey, $@"{fullKeyPath}\{subKeyName}", writer);
                    }
                }
                catch { }
            }
        }

        private static string EscapeRegString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string FormatHexBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            var sb = new System.Text.StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }

        private static bool BackupUninstallKey(RegistryKey root, string parentPath, string subKeyName)
        {
            try
            {
                string rootPrefix = root.Name; // E.g., HKEY_CURRENT_USER or HKEY_LOCAL_MACHINE
                string fullKeyPath = $@"{rootPrefix}\{parentPath}\{subKeyName}";

                string backupDir = Path.Combine(AppPaths.CommonBackupsDir, "Registry");
                string safeName = string.Join("_", subKeyName.Split(Path.GetInvalidFileNameChars()));
                string fileName = $"Uninstall_{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.reg";
                string destFile = Path.Combine(backupDir, fileName);

                using var subKey = root.OpenSubKey($@"{parentPath}\{subKeyName}", false);
                if (subKey == null) return false;

                return ExportKeyToRegFile(subKey, fullKeyPath, destFile);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "RegistryCleaner.BackupUninstallKey");
                return false;
            }
        }

        private static int CleanDeadUninstallKeys(RegistryKey root, string path)
        {
            int cleaned = 0;
            try
            {
                var dead = CollectDeadUninstallKeys(root, path);
                if (dead.Count == 0) return 0;

                using var unKey = root.OpenSubKey(path, true);
                if (unKey == null) return 0;

                foreach (var subKey in dead)
                {
                    try
                    {
                        // شرط حیاتی: اگر پشتیبان‌گیری به هر دلیل ناموفق بود، حذف آن کلید انجام نشود
                        if (!BackupUninstallKey(root, path, subKey))
                        {
                            continue;
                        }

                        unKey.DeleteSubKeyTree(subKey, false);
                        // تأیید حذف واقعی پیش از شمارش
                        if (unKey.OpenSubKey(subKey, false) == null) cleaned++;
                    }
                    catch
                    {
                        // بدون دسترسی Administrator یا کلید توسط فرآیند دیگری نگه داشته شده
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return cleaned;
        }

        /// <summary>
        /// مسیر فایل آن اینستالر را از داخل UninstallString بیرون می کشد.
        ///
        /// نکته ی امنیتی: خروجی فقط باید «مسیر مطلق» باشد. اگر چیزی مثل
        /// `MsiExec.exe /X{GUID}` یا `cmd /c MsiExec.exe ...` استخراج شود،
        /// رشته خالی برمی گردد تا کلید رجیستری «موجود» تلقی شود و حذف نگردد.
        /// (حذف کلید معتبر بدتر از نگه داشتن کلید مرده است.)
        ///
        /// فرمت های پوشش داده شده:
        ///   "C:\Program Files\App\unins.exe" /S
        ///   cmd /c "C:\Program Files\App\unins.exe"        ← نقل قول در وسط
        ///   C:\Program Files\App\unins.exe --silent
        ///   MsiExec.exe /X{GUID}                            ← نام نسبی → رد می شود
        /// </summary>
        private static string ExtractExePath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            try
            {
                raw = raw.Trim().Trim('"');

                // ۱) اولین بازه ی نقل قولیِ موجود در رشته (پوشش پیشوندهای cmd/powershell)
                int firstQuote = raw.IndexOf('"');
                if (firstQuote >= 0)
                {
                    int endQuote = raw.IndexOf('"', firstQuote + 1);
                    if (endQuote > firstQuote + 1)
                    {
                        string quoted = raw.Substring(firstQuote + 1, endQuote - firstQuote - 1).Trim();
                        if (IsRootedExecutable(quoted)) return quoted;
                    }
                }

                // ۲) بدون نقل قول: همه چیز تا انتهای `.exe`
                int exeIdx = raw.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exeIdx > 0)
                {
                    string candidate = raw.Substring(0, exeIdx + 4).Trim().Trim('"', '\'');
                    if (IsRootedExecutable(candidate)) return candidate;
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return "";
        }

        /// <summary>
        /// فقط مسیرهای مطلق با درایوِ حاضر قابل قضاوت هستند.
        /// نام نسبی (`MsiExec.exe`) یا پیشوند shell (`cmd /c ...`) این شرط را رد نمی کنند.
        /// </summary>
        private static bool IsRootedExecutable(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                if (!Path.IsPathRooted(path)) return false;
                if (!path.Contains(':')) return false;          // UNC و مسیر نسبی رد می شود
                return true;
            }
            catch
            {
                return false;
            }
        }

        // --------------------------------------------------------------------
        // Orphaned Services (سرویس های یتیم با فایل اجرایی ناموجود)
        // --------------------------------------------------------------------

        public static List<OrphanedServiceItem> ScanOrphanedServices()
        {
            var list = new List<OrphanedServiceItem>();
            try
            {
                using var servicesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", false);
                if (servicesKey == null) return list;

                foreach (var subName in servicesKey.GetSubKeyNames())
                {
                    try
                    {
                        using var svcKey = servicesKey.OpenSubKey(subName, false);
                        if (svcKey == null) continue;

                        int type = svcKey.GetValue("Type") is int t ? t : 0;
                        if (type == 1 || type == 2) continue; // Skip kernel / filesystem drivers

                        int start = svcKey.GetValue("Start") is int s ? s : 3;
                        if (start == 4) continue; // Already disabled

                        string? rawImagePath = svcKey.GetValue("ImagePath")?.ToString();
                        if (string.IsNullOrWhiteSpace(rawImagePath)) continue;

                        if (rawImagePath.Contains("svchost.exe", StringComparison.OrdinalIgnoreCase)) continue;
                        if (rawImagePath.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) continue;

                        string expanded = Environment.ExpandEnvironmentVariables(rawImagePath);
                        string exePath = ExtractExePath(expanded);
                        if (string.IsNullOrEmpty(exePath)) continue;

                        string? root = Path.GetPathRoot(exePath);
                        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                        if (IsExeReallyMissing(exePath)
                            || (start == 3 && IsOrphanedProgramDataService(subName, exePath)))
                        {
                            string disp = svcKey.GetValue("DisplayName")?.ToString() ?? subName;
                            list.Add(new OrphanedServiceItem
                            {
                                ServiceName = subName,
                                DisplayName = disp,
                                MissingImagePath = exePath,
                                StartType = start
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
            return list;
        }

        // قانون دومِ یتیم بودنی سرویس: فایل اجرایی وجود دارد ولی زیر پوشه ی سطح اولِ
        // ProgramData افتاده (مثل battlenet_helpersvc بازمانده از Battle.net حذف شده).
        // این حکم فقط وقتی صادر می شود که: نوع راه اندازی Manual باشد، نام سرویس با نام
        // پوشه هم ریشه باشد (battlenet ↔ Battle.net_components) و هیچ برنامه ی نصب شده ای
        // با آن ریشه هم نام نباشد — در غیر این صورت هرگز چیزی یتیم اعلام نمی شود.
        internal static bool IsOrphanedProgramDataService(string serviceName, string exePath)
        {
            try
            {
                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (string.IsNullOrEmpty(programData)) return false;

                string prefix = programData.TrimEnd('\\') + "\\";
                if (!exePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

                string rel = exePath[prefix.Length..];
                int sep = rel.IndexOfAny(new[] { '\\', '/' });
                string segment = sep >= 0 ? rel[..sep] : rel;
                if (segment.Length < 4) return false;
                if (LockRegistryService.IsProtectedName(segment)) return false;

                // توکن های >=۴ حرفیِ نام پوشه (battle / components / ...)
                var tokens = segment.ToLowerInvariant()
                    .Split(new[] { '.', '_', '-', ' ', '+', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Length >= 4)
                    .ToList();
                if (tokens.Count == 0) return false;

                // سرویس باید با پوشه هم ریشه باشد: battlenet_helpersvc ↔ Battle.net
                string compactName = new(serviceName.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                var relatedTokens = tokens.Where(t => compactName.Contains(t)).ToList();
                if (relatedTokens.Count == 0) return false;

                // اگر برنامه ی نصب شده ای همان ریشه را داشته باشد → محصول حذف نشده → یتیم نیست
                var roots = new List<(RegistryKey Root, string SubKeyPath)>
                {
                    (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                    (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                    (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
                };

                foreach (var (root, subPath) in roots)
                {
                    using var uninstallKey = root.OpenSubKey(subPath, false);
                    if (uninstallKey == null) continue;

                    foreach (var sub in uninstallKey.GetSubKeyNames())
                    {
                        using var appKey = uninstallKey.OpenSubKey(sub, false);
                        string displayName = appKey?.GetValue("DisplayName")?.ToString() ?? string.Empty;
                        if (displayName.Length > 0 &&
                            relatedTokens.Any(t => displayName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
                return false;
            }
        }

        // تشخیص واقعی «فایل اجرایی ناموجود» برای صدور حکم «سرویس یتیم»:
        // File.Exists کافی نیست؛ خطای دسترسی (بدون ادمین) نباید یتیم حساب شود.
        // همچنین در پروسس ۳۲ بیتی، System32 به SysWOW64 ریدایرکت می شود و
        // سرویس های ۶۴ بیتی سالم باید از مسیر Sysnative سنجیده شوند.
        private static bool IsExeReallyMissing(string exePath)
        {
            string probe = exePath;
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
            {
                const string sys32 = @"C:\Windows\System32";
                if (probe.StartsWith(sys32, StringComparison.OrdinalIgnoreCase))
                    probe = string.Concat(@"C:\Windows\Sysnative", probe.AsSpan(sys32.Length));
            }
            try
            {
                using var fs = File.OpenRead(probe);
                return false;
            }
            catch (FileNotFoundException) { return true; }
            catch (DirectoryNotFoundException) { return true; }
            // دسترسی/قفل بودن، حکم یتیم بودن را ثابت نمی کند → نادیده بگیر
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
                return false;
            }
        }

        public static int DisableOrphanedServices(IEnumerable<string>? serviceNames = null)
        {
            int disabledCount = 0;
            try
            {
                var targets = serviceNames != null ? new HashSet<string>(serviceNames, StringComparer.OrdinalIgnoreCase) : null;
                var orphaned = ScanOrphanedServices();

                using var servicesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", true);
                if (servicesKey == null) return 0;

                foreach (var item in orphaned)
                {
                    if (targets != null && !targets.Contains(item.ServiceName)) continue;

                    try
                    {
                        using var svcKey = servicesKey.OpenSubKey(item.ServiceName, true);
                        if (svcKey != null)
                        {
                            svcKey.SetValue("Start", 4, RegistryValueKind.DWord);
                            disabledCount++;
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
            return disabledCount;
        }
    }

    public class OrphanedServiceItem
    {
        public string ServiceName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string MissingImagePath { get; set; } = string.Empty;
        public int StartType { get; set; }
    }
}
