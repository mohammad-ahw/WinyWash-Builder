// O:\OpenCode\AntiGravity\CleanC - NEW\src\CleanC.Core\Services\NetworkSpeedService.cs
using System;
using System.Net.NetworkInformation;

namespace CleanC.Core.Services
{
    /// <summary>
    /// نمونه گیری سرعت شبکه با کش زنی.
    ///
    /// چرا لازم است: لیست interfaceها هر بار گرفتنش هزینه بر است (چند ده میلی ثانیه)
    /// و چند timer همزمان (تایمر آمار ۲٫۵ ثانیه ای + تایمر اسپارک لاین ۱٫۵ ثانیه ای)
    /// باعث می شدند چندین بار در همان لحظه نمونه گیری شود و مقدار نادرست تولید شود.
    /// </summary>
    public static class NetworkSpeedService
    {
        // کش لیست interfaceها — مگر اینکه کارت شبکه اضافه/حذف شود تغییر نمی کند
        private static NetworkInterface[]? _interfaces;
        private static DateTime _interfacesRefreshedUtc = DateTime.MinValue;

        // کش نتیجه — جلوگیری از نمونه گیری های پشت سرهم در همان تیک تایمر
        private static (double Mbps, string TypeName) _cached = (0.0, "پایدار");
        private static DateTime _cachedAtUtc = DateTime.MinValue;

        private static long _prevBytes = 0;
        private static DateTime _prevTimeUtc = DateTime.MinValue;

        private static readonly TimeSpan InterfaceCacheTtl = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan SampleCacheTtl = TimeSpan.FromMilliseconds(600);

        public static (double Mbps, string TypeName) Sample()
        {
            try
            {
                var now = DateTime.UtcNow;

                if ((now - _cachedAtUtc) < SampleCacheTtl)
                    return _cached;

                if (_interfaces == null || (now - _interfacesRefreshedUtc) > InterfaceCacheTtl)
                {
                    try
                    {
                        _interfaces = NetworkInterface.GetAllNetworkInterfaces();
                        _interfacesRefreshedUtc = now;
                    }
                    catch
                    {
                        _interfaces = Array.Empty<NetworkInterface>();
                    }
                }

                long totalBytes = 0;
                string detectedType = "پایدار";

                foreach (var ni in _interfaces)
                {
                    try
                    {
                        if (ni.OperationalStatus != OperationalStatus.Up) continue;
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                        var ipStats = ni.GetIPStatistics();
                        totalBytes += ipStats.BytesReceived + ipStats.BytesSent;

                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                        {
                            detectedType = "Wi-Fi";
                        }
                        else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet &&
                                 detectedType != "Wi-Fi")
                        {
                            detectedType = "Ethernet";
                        }
                    }
                    catch
                    {
                        // interface مسدود/غیرفعال → از همین نمونه رد می شویم
                    }
                }

                double mbps = 0.0;
                if (_prevTimeUtc != DateTime.MinValue && totalBytes >= _prevBytes)
                {
                    double elapsedSec = (now - _prevTimeUtc).TotalSeconds;
                    if (elapsedSec >= 0.05) // جلوگیری از تقسیم بر زمان خیلی کوچک
                    {
                        long delta = totalBytes - _prevBytes;
                        mbps = (delta * 8.0) / (1024.0 * 1024.0 * elapsedSec);
                    }
                }

                _prevBytes = totalBytes;
                _prevTimeUtc = now;

                if (double.IsNaN(mbps) || double.IsInfinity(mbps) || mbps < 0) mbps = 0.0;

                _cached = (mbps, detectedType);
                _cachedAtUtc = now;
                return _cached;
            }
            catch
            {
                return (0.0, "پایدار");
            }
        }
    }
}
