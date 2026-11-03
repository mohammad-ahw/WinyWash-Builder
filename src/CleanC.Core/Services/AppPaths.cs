// O:\OpenCode\New folder\CleanC - NZW\src\CleanC.Core\Services\AppPaths.cs
using System;
using System.IO;
using CleanC.Core.Diagnostics;

namespace CleanC.Core.Services
{
    /// <summary>
    /// مسیرهای دادهٔ برنامه در یک نقطهٔ واحد.
    ///
    /// چرا: تا فاز ۲۳ مسیر داده در ده‌ها فایل به‌صورت رشتهٔ هاردکد «CleanC» بود.
    /// حالا همه‌جا از همین کلاس خوانده می‌شود تا نام پوشه فقط یک جا تعریف شده باشد.
    /// مهاجرت یک‌بارهٔ پوشهٔ قدیمی (CleanC → WinyWash) در استارتاپ انجام می‌شود تا
    /// تنظیمات، تاریخچه، قفل‌ها، قرنطینه و بکاپ‌های کاربران قبلی از دست نرود.
    /// </summary>
    public static class AppPaths
    {
        private const string OldFolderName = "CleanC";
        private const string NewFolderName = "WinyWash";

        private static bool _migrated;

        /// <summary>ریشهٔ دادهٔ محلی: %LOCALAPPDATA%\WinyWash</summary>
        public static string LocalRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), NewFolderName);

        /// <summary>ریشهٔ دادهٔ سراسری: %ProgramData%\WinyWash</summary>
        public static string CommonRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), NewFolderName);

        public static string LogsDir => Path.Combine(LocalRoot, "logs");
        public static string SettingsFile => Path.Combine(LocalRoot, "settings.json");
        public static string LocksFile => Path.Combine(LocalRoot, "locks.json");
        public static string QuarantineDir => Path.Combine(LocalRoot, "quarantine");
        public static string ExpedDir => Path.Combine(LocalRoot, "exped");
        public static string GlassConfigFile => Path.Combine(LocalRoot, "glass_config.ini");
        public static string CrashLogFile => Path.Combine(LocalRoot, "crash.log");
        public static string LocalBackupsDir => Path.Combine(LocalRoot, "Backups");
        public static string CommonBackupsDir => Path.Combine(CommonRoot, "Backups");

        /// <summary>
        /// انتقال یک‌بارهٔ پوشهٔ CleanC به WinyWash در LocalAppData و ProgramData.
        /// اگر مقصد از قبل موجود باشد، فقط فایل‌های غایب از مبدأ کپی می‌شوند
        /// (دادهٔ تازه‌ترِ مقصد هرگز بازنویسی نمی‌شود). هر خطایی بی‌صدا لاگ می‌شود.
        /// </summary>
        public static void MigrateLegacyFolders()
        {
            if (_migrated) return;
            _migrated = true;

            MigrateFolder(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), OldFolderName),
                LocalRoot);

            MigrateFolder(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), OldFolderName),
                CommonRoot);
        }

        private static void MigrateFolder(string oldPath, string newPath)
        {
            try
            {
                if (!Directory.Exists(oldPath)) return;

                if (!Directory.Exists(newPath))
                {
                    string? parent = Path.GetDirectoryName(newPath);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                    Directory.Move(oldPath, newPath);
                    return;
                }

                // هر دو پوشه موجودند: فقط فایل‌های موجود در مقصد را دست نمی‌زنیم
                foreach (string file in Directory.EnumerateFiles(oldPath, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(oldPath, file);
                    string destination = Path.Combine(newPath, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (!File.Exists(destination)) File.Copy(file, destination);
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "AppPaths.MigrateLegacyFolders");
            }
        }
    }
}
