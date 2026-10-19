// src/CleanC.UI/Expedition/ExpeditionContentService.cs
// پوشه‌ی داده‌ی بازی در %LOCALAPPDATA%\CleanC\exped:
//   • wallpaper.img — کپیِ والپیپر واقعی ویندوز تا HTML با میزبان مجازی cleanc.wall بخواند
//   • expedition.save.json — وضعیت بازی (XP، سطح، پرونده‌های دستگیرشده و …)
//
// چرا کپی؟ والپیپر ممکن است در رجیستری مسیر دلخواه کاربر باشد یا TranscodedWallpaper
// بدون پسوند باشد؛ مپ کردن پوشه‌ی مبدا عملاً انجام‌شدنی نیست، پس یک‌بار در بازشدن
// بازی یک نسخه‌ی محلی می‌سازیم. کپی فقط وقتی مبدا تازه‌تر است انجام می‌شود.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CleanC.Core.Diagnostics;

namespace CleanC.UI.Expedition
{
    public static class ExpeditionContentService
    {
        /// <summary>میزبان مجازی HTML برای فایل‌ها داخل پوشه‌ی داده.</summary>
        public const string WallHostName = "cleanc.wall";

        /// <summary>میزبان مجازی برای بندانگشتی‌های واقعی تصاویرِ Zone 1.</summary>
        public const string DownloadsHostName = "cleanc.dl";

        public static string DataDir
        {
            get
            {
                try { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CleanC", "exped"); }
                catch (Exception ex) { CcLog.Ignore(ex, "Expedition.DataDir"); return string.Empty; }
            }
        }

        private static string WallpaperPath => Path.Combine(DataDir, "wallpaper.img");
        private static string SavePath => Path.Combine(DataDir, "expedition.save.json");

        /// <summary>پوشه‌ی داده را می‌سازد و برمی‌گرداند؛ خروجی خالی یعنی شکست.</summary>
        public static string EnsureDataDir()
        {
            try
            {
                var dir = DataDir;
                if (dir.Length == 0) return string.Empty;
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Expedition.EnsureDataDir");
                return string.Empty;
            }
        }

        /// <summary>
        /// والپیپر واقعی ویندوز را در wallpaper.img کپی می‌کند.
        /// خروجی: true یعنی فایلی قابل نمایش وجود دارد (HTML پس‌زمینه را می‌گیرد).
        /// </summary>
        public static bool PrepareWallpaper()
        {
            try
            {
                if (EnsureDataDir().Length == 0) return false;

                string source = ResolveWallpaperSourcePath();
                if (source.Length == 0 || !File.Exists(source))
                {
                    return false; // پس‌زمینه‌ى رنگ خالص یا در دسترس نیست → گرادیان HTML
                }

                var srcInfo = new FileInfo(source);
                var dstInfo = new FileInfo(WallpaperPath);

                // کپی فقط در صورت لزوم: dst از قبل تازه است یا اندازه‌ی مبدا صفر است
                bool needCopy = !dstInfo.Exists
                    || srcInfo.LastWriteTimeUtc > dstInfo.LastWriteTimeUtc
                    || srcInfo.Length != dstInfo.Length;

                if (needCopy)
                {
                    // Copy با FileStream تا قفل اشتراکی TranscodedWallpaper (که explorer نگه می‌دارد) نشکند
                    using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var dst = new FileStream(WallpaperPath, FileMode.Create, FileAccess.Write);
                    src.CopyTo(dst);
                    dst.Flush();
                }
                return dstInfo.Exists && new FileInfo(WallpaperPath).Length > 0;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Expedition.PrepareWallpaper");
                return false;
            }
        }

        /// <summary>مسیر والپیپر: اول TranscodedWallpaper، بعد رجیستری Desktop\WallPaper.</summary>
        private static string ResolveWallpaperSourcePath()
        {
            try
            {
                var transcoded = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (File.Exists(transcoded) && new FileInfo(transcoded).Length > 0)
                    return transcoded;
            }
            catch (Exception ex) { CcLog.Ignore(ex, "Expedition.Transcoded"); }

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                var reg = key?.GetValue("WallPaper") as string ?? string.Empty;
                if (reg.Length > 0 && File.Exists(reg))
                    return reg;
            }
            catch (Exception ex) { CcLog.Ignore(ex, "Expedition.RegistryWallpaper"); }

            return string.Empty;
        }

        // ====================================================================
        // وضعیت بازی (ذخیره/خواندن)
        // ====================================================================

        /// <summary>وضعیت ذخیره‌شده‌ی بازی؛ null یعنی هنوز هیچ بازی‌ای انجام نشده.</summary>
        public static JsonNode? LoadGameState()
        {
            try
            {
                if (!File.Exists(SavePath)) return null;
                var json = File.ReadAllText(SavePath);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonNode.Parse(json);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Expedition.LoadState");
                return null;
            }
        }

        /// <summary>ذخیره‌ی وضعیت بازی — هر تغییرِ معنادار سمت JS پس از debounce از اینجا می‌گذرد.</summary>
        public static bool SaveGameState(string json)
        {
            try
            {
                if (EnsureDataDir().Length == 0) return false;
                File.WriteAllText(SavePath, json);
                return true;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Expedition.SaveState");
                return false;
            }
        }
    }
}
