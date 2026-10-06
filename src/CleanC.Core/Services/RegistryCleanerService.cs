// O:\OpenCode\AntiGravity\CleanC - NEW\src\CleanC.Core\Services\RegistryCleanerService.cs
using System;
using System.Collections.Generic;
using System.IO;
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
    }
}
