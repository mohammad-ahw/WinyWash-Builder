using System;
using System.IO;

namespace CleanC.UI
{
    public static class WallpaperService
    {
        private static bool? _isCachedDark = null;
        private static DateTime _lastCheck = DateTime.MinValue;

        public static bool IsWallpaperDark(bool forceRefresh = false)
        {
            if (!forceRefresh && _isCachedDark.HasValue && (DateTime.Now - _lastCheck).TotalSeconds < 5)
            {
                return _isCachedDark.Value;
            }

            try
            {
                string wallpaperPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Themes\TranscodedWallpaper");

                if (!File.Exists(wallpaperPath))
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                    wallpaperPath = key?.GetValue("WallPaper") as string ?? "";
                }

                if (!File.Exists(wallpaperPath))
                {
                    // Check solid color background
                    using var colorKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
                    string? bgRgb = colorKey?.GetValue("Background") as string;
                    if (!string.IsNullOrWhiteSpace(bgRgb))
                    {
                        var parts = bgRgb.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 3 &&
                            byte.TryParse(parts[0], out byte r) &&
                            byte.TryParse(parts[1], out byte g) &&
                            byte.TryParse(parts[2], out byte b))
                        {
                            double lum = 0.299 * r + 0.587 * g + 0.114 * b;
                            _isCachedDark = lum < 130.0;
                            _lastCheck = DateTime.Now;
                            return _isCachedDark.Value;
                        }
                    }

                    _isCachedDark = false;
                    return false;
                }

                // Fast non-blocking GDI+ analysis without WinRT deadlocks
                using var fs = new FileStream(wallpaperPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var bmp = new System.Drawing.Bitmap(fs);

                double totalLuminance = 0;
                int samples = 0;
                int stepX = Math.Max(1, bmp.Width / 12);
                int stepY = Math.Max(1, bmp.Height / 12);

                for (int x = 0; x < bmp.Width; x += stepX)
                {
                    for (int y = 0; y < bmp.Height; y += stepY)
                    {
                        var color = bmp.GetPixel(x, y);
                        totalLuminance += (0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
                        samples++;
                    }
                }

                double avgLuminance = totalLuminance / Math.Max(1, samples);
                _isCachedDark = avgLuminance < 130.0;
                _lastCheck = DateTime.Now;
                return _isCachedDark.Value;
            }
            catch
            {
                // Fallback to false (standard light theme)
                _isCachedDark = false;
                return false;
            }
        }
    }
}
