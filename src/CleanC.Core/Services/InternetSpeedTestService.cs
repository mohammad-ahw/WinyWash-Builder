using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;

namespace CleanC.Core.Services
{
    public class SpeedTestResult
    {
        public double PingMs { get; set; }
        public int PingMsInt => (int)Math.Round(PingMs);
        public double DownloadSpeedMbps { get; set; }
        public int DownloadSpeedMbpsInt => (int)Math.Round(DownloadSpeedMbps);
        public double DownloadSpeedMBs => Math.Round(DownloadSpeedMbps / 8.0, 2);
        public int DownloadSpeedMBsInt => (int)Math.Round(DownloadSpeedMbps / 8.0);
        public double PeakDownloadSpeedMbps { get; set; }
        public int PeakDownloadSpeedMbpsInt => (int)Math.Round(PeakDownloadSpeedMbps);
        public double PeakDownloadSpeedMBs => Math.Round(PeakDownloadSpeedMbps / 8.0, 2);
        public int PeakDownloadSpeedMBsInt => (int)Math.Round(PeakDownloadSpeedMbps / 8.0);
        public double UploadSpeedMbps { get; set; }
        public int UploadSpeedMbpsInt => (int)Math.Round(UploadSpeedMbps);
        public double UploadSpeedMBs => Math.Round(UploadSpeedMbps / 8.0, 2);
        public int UploadSpeedMBsInt => (int)Math.Round(UploadSpeedMbps / 8.0);
        public int ActiveConnections { get; set; } = 8;
        public bool IsMultiConnectionIdmMode { get; set; } = true;
        
        public string ClientIp { get; set; } = string.Empty;
        public string IspName { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public bool IsVpnActive { get; set; }
        public string FlagEmoji => InternetSpeedTestService.GetCountryFlagEmoji(CountryCode);

        // Experience ratings (Speedtest.net style)
        public string WebBrowsingRating => PingMs <= 90 && DownloadSpeedMbps >= 10 ? LocalizationService.Get("عالی") : (PingMs <= 180 && DownloadSpeedMbps >= 5 ? LocalizationService.Get("خوب") : LocalizationService.Get("متوسط"));
        public string VideoStreamingRating => DownloadSpeedMbps >= 25 ? "4K UHD" : (DownloadSpeedMbps >= 10 ? "Full HD 1080p" : (DownloadSpeedMbps >= 3 ? "HD 720p" : "SD 480p"));
        public string GamingRating => PingMs <= 40 ? LocalizationService.Get("عالی (بدون لگ)") : (PingMs <= 70 ? LocalizationService.Get("بسیار خوب") : (PingMs <= 120 ? LocalizationService.Get("قابل قبول") : LocalizationService.Get("دارای تاخیر")));
        public string VideoCallingRating => PingMs <= 110 && UploadSpeedMbps >= 2.5 ? LocalizationService.Get("کیفیت عالی (HD)") : (PingMs <= 180 && UploadSpeedMbps >= 1 ? LocalizationService.Get("کیفیت مطلوب") : LocalizationService.Get("کیفیت پایه"));

        public long BytesDownloaded { get; set; }
        public long BytesUploaded { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime TestedAt { get; set; } = DateTime.Now;
    }

    public class InternetSpeedTestService
    {
        private static readonly HttpClient _httpClient;

        static InternetSpeedTestService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://speed.cloudflare.com/");
            _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
        }

        public static string GetCountryFlagEmoji(string? countryCode)
        {
            if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2) return "🌐";
            try
            {
                countryCode = countryCode.ToUpperInvariant();
                if (countryCode[0] < 'A' || countryCode[0] > 'Z' || countryCode[1] < 'A' || countryCode[1] > 'Z') return "🌐";
                int first = 0x1F1E6 + (countryCode[0] - 'A');
                int second = 0x1F1E6 + (countryCode[1] - 'A');
                return char.ConvertFromUtf32(first) + char.ConvertFromUtf32(second);
            }
            catch
            {
                return "🌐";
            }
        }

        public static bool IsVpnOrProxyActive()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    if (desc.Contains("tap") || desc.Contains("tun") || desc.Contains("wireguard") ||
                        desc.Contains("wintun") || desc.Contains("openvpn") || desc.Contains("tailscale") ||
                        desc.Contains("sing-box") || desc.Contains("v2ray") || desc.Contains("xray") ||
                        desc.Contains("clash") || desc.Contains("vpn") || desc.Contains("neko") ||
                        desc.Contains("pptp") || desc.Contains("l2tp") || desc.Contains("sstp") ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static readonly List<SpeedTestResult> _testHistory = new();

        public static IReadOnlyList<SpeedTestResult> GetHistory()
        {
            lock (_testHistory)
            {
                return new List<SpeedTestResult>(_testHistory);
            }
        }

        public static async Task<SpeedTestResult> RunSpeedTestAsync(
            Action<string, double, int, SpeedTestResult>? onProgress = null, 
            CancellationToken cancellationToken = default)
        {
            var result = new SpeedTestResult();
            result.IsVpnActive = IsVpnOrProxyActive();

            // 1. Fetch IP, ISP, and Location metadata fast
            try
            {
                using var ipCts = new CancellationTokenSource(TimeSpan.FromSeconds(2.5));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ipCts.Token);

                string json = await _httpClient.GetStringAsync("https://ipwho.is/", linkedCts.Token);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("ip", out var q)) result.ClientIp = q.GetString() ?? "";
                if (root.TryGetProperty("country", out var cnt)) result.Country = cnt.GetString() ?? "";
                if (root.TryGetProperty("country_code", out var cc)) result.CountryCode = cc.GetString() ?? "";
                if (root.TryGetProperty("city", out var city)) result.City = city.GetString() ?? "";
                if (root.TryGetProperty("connection", out var conn) && conn.TryGetProperty("isp", out var isp))
                {
                    result.IspName = isp.GetString() ?? "";
                }
            }
            catch
            {
                // Fallback 1: Cloudflare __down headers
                try
                {
                    using var cfCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    using var linkedCf = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cfCts.Token);
                    using var headResp = await _httpClient.GetAsync("https://speed.cloudflare.com/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, linkedCf.Token);
                    if (headResp.Headers.TryGetValues("cf-meta-ip", out var ips)) result.ClientIp = ips.FirstOrDefault() ?? "";
                    if (headResp.Headers.TryGetValues("cf-meta-country", out var cnts))
                    {
                        result.CountryCode = cnts.FirstOrDefault() ?? "";
                        result.Country = result.CountryCode;
                    }
                    if (headResp.Headers.TryGetValues("cf-meta-city", out var cities)) result.City = cities.FirstOrDefault() ?? "";
                    result.IspName = "Cloudflare CDN";
                }
                catch
                {
                    result.ClientIp = LocalizationService.Get("اتصال فعال");
                    result.IspName = LocalizationService.Get("شبکه اینترنت");
                }
            }

            // Immediately notify UI that metadata is ready!
            onProgress?.Invoke("meta", 0, 0, result);

            // 2. Measure Ping (Native ICMP Ping with fallback)
            try
            {
                var pings = new List<long>();
                string[] pingHosts = new[] { "1.1.1.1", "8.8.8.8", "4.2.2.4" };
                using var pingSender = new Ping();

                foreach (var host in pingHosts)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        var reply = await pingSender.SendPingAsync(host, 1000);
                        if (reply.Status == IPStatus.Success)
                        {
                            pings.Add(reply.RoundtripTime);
                        }
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
                }

                if (pings.Count > 0)
                {
                    result.PingMs = pings.Min();
                }
                else
                {
                    // Fallback to HTTP HEAD latency if ICMP is blocked
                    var sw = Stopwatch.StartNew();
                    using var pingResp = await _httpClient.GetAsync("https://speed.cloudflare.com/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    sw.Stop();
                    if (pingResp.IsSuccessStatusCode)
                    {
                        result.PingMs = Math.Round((double)sw.ElapsedMilliseconds, 0);
                    }
                }

                onProgress?.Invoke("ping", result.PingMs, 0, result);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                result.PingMs = 0;
            }

            // 3. Multi-Stream Concurrent Download Test (Real 8-Thread IDM-Style Architecture)
            long totalBytesDownloaded = 0;
            const int streamCount = 8;
            const int testDurationSec = 6;
            using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            downloadCts.CancelAfter(TimeSpan.FromSeconds(testDurationSec + 1));

            var downloadUrls = new[]
            {
                "https://speed.cloudflare.com/__down?bytes=25000000",
                "https://cachefly.cachefly.net/10mb.test",
                "https://speed.cloudflare.com/__down?bytes=20000000",
                "https://speed.cloudflare.com/__down?bytes=30000000",
                "https://cachefly.cachefly.net/10mb.test",
                "https://speed.cloudflare.com/__down?bytes=15000000"
            };

            var downloadTasks = new List<Task>();
            var streamStopwatch = Stopwatch.StartNew();

            for (int i = 0; i < streamCount; i++)
            {
                string url = downloadUrls[i % downloadUrls.Length];
                downloadTasks.Add(Task.Run(async () =>
                {
                    byte[] buffer = new byte[64 * 1024]; // 64 KB buffer
                    while (!downloadCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, downloadCts.Token);
                            if (!response.IsSuccessStatusCode) break;

                            using var stream = await response.Content.ReadAsStreamAsync(downloadCts.Token);
                            int read;
                            while (!downloadCts.Token.IsCancellationRequested && (read = await stream.ReadAsync(buffer, 0, buffer.Length, downloadCts.Token)) > 0)
                            {
                                Interlocked.Add(ref totalBytesDownloaded, read);
                            }
                        }
                        catch
                        {
                            break;
                        }
                    }
                }));
            }

            // Progress reporting loop with smoothed moving speed and peak capture
            double smoothedMbps = 0;
            double peakSpeedMbps = 0;
            long lastSampleBytes = 0;
            var lastSampleTime = streamStopwatch.Elapsed;
            var recordedSpeeds = new List<double>();

            while (streamStopwatch.Elapsed.TotalSeconds < testDurationSec && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);

                var curTime = streamStopwatch.Elapsed;
                double deltaSec = (curTime - lastSampleTime).TotalSeconds;
                long curBytes = Interlocked.Read(ref totalBytesDownloaded);
                long deltaBytes = curBytes - lastSampleBytes;

                if (deltaSec > 0.05 && deltaBytes >= 0)
                {
                    double instantMbps = (deltaBytes * 8.0) / (deltaSec * 1_000_000.0);
                    if (instantMbps > peakSpeedMbps) peakSpeedMbps = instantMbps;

                    smoothedMbps = smoothedMbps == 0 ? instantMbps : (0.4 * instantMbps + 0.6 * smoothedMbps);

                    if (curTime.TotalSeconds > 0.5 && instantMbps > 0)
                    {
                        recordedSpeeds.Add(instantMbps);
                    }

                    result.PeakDownloadSpeedMbps = Math.Round(peakSpeedMbps, 2);
                    int progressPct = (int)Math.Clamp((curTime.TotalSeconds / testDurationSec) * 100.0, 1, 98);
                    onProgress?.Invoke("download", Math.Round(smoothedMbps, 2), progressPct, result);

                    lastSampleBytes = curBytes;
                    lastSampleTime = curTime;
                }
            }

            // Cancel any remaining download workers and await
            try
            {
                downloadCts.Cancel();
                await Task.WhenAll(downloadTasks).ConfigureAwait(false);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            streamStopwatch.Stop();
            result.BytesDownloaded = Interlocked.Read(ref totalBytesDownloaded);
            result.PeakDownloadSpeedMbps = Math.Round(peakSpeedMbps, 2);

            if (result.BytesDownloaded > 0)
            {
                double finalAvg = 0;
                if (recordedSpeeds.Count >= 4)
                {
                    var sorted = recordedSpeeds.OrderBy(x => x).ToList();
                    int skip = sorted.Count / 2;
                    finalAvg = sorted.Skip(skip).Average();
                }
                else if (recordedSpeeds.Count > 0)
                {
                    finalAvg = recordedSpeeds.Average();
                }
                else
                {
                    finalAvg = (result.BytesDownloaded * 8.0) / (Math.Max(streamStopwatch.Elapsed.TotalSeconds, 0.5) * 1_000_000.0);
                }

                result.DownloadSpeedMbps = Math.Round(finalAvg, 2);
                onProgress?.Invoke("download", result.DownloadSpeedMbps, 100, result);
            }
            else
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.ErrorMessage = LocalizationService.Get("تست توسط کاربر لغو گردید.");
                    return result;
                }
                result.ErrorMessage = LocalizationService.Get("خطا در اتصال به سرور دانلود اینترنت.");
                return result;
            }

            // 4. Parallel Multi-Stream Upload Speed Test
            try
            {
                if (!cancellationToken.IsCancellationRequested && result.DownloadSpeedMbps > 0)
                {
                    onProgress?.Invoke("upload", 0, 5, result);

                    long totalBytesUploaded = 0;
                    using var uploadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    uploadCts.CancelAfter(TimeSpan.FromSeconds(3.5));

                    byte[] uploadChunk = new byte[1_500_000]; // 1.5 MB chunk
                    Random.Shared.NextBytes(uploadChunk);

                    var upSw = Stopwatch.StartNew();
                    var uploadTasks = new List<Task>();

                    for (int u = 0; u < 2; u++)
                    {
                        uploadTasks.Add(Task.Run(async () =>
                        {
                            while (!uploadCts.Token.IsCancellationRequested)
                            {
                                try
                                {
                                    using var content = new ByteArrayContent(uploadChunk);
                                    using var upResp = await _httpClient.PostAsync("https://speed.cloudflare.com/__up", content, uploadCts.Token);
                                    if (upResp.IsSuccessStatusCode)
                                    {
                                        Interlocked.Add(ref totalBytesUploaded, uploadChunk.Length);
                                    }
                                    else break;
                                }
                                catch { break; }
                            }
                        }));
                    }

                    while (upSw.Elapsed.TotalSeconds < 3.0 && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                        long curUpBytes = Interlocked.Read(ref totalBytesUploaded);
                        double elapsed = upSw.Elapsed.TotalSeconds;
                        if (elapsed > 0.1 && curUpBytes > 0)
                        {
                            double curUpMbps = (curUpBytes * 8.0) / (elapsed * 1_000_000.0);
                            int upPct = (int)Math.Clamp((elapsed / 3.0) * 100.0, 5, 95);
                            onProgress?.Invoke("upload", Math.Round(curUpMbps, 2), upPct, result);
                        }
                    }

                    try
                    {
                        uploadCts.Cancel();
                        await Task.WhenAll(uploadTasks).ConfigureAwait(false);
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }

                    upSw.Stop();
                    result.BytesUploaded = Interlocked.Read(ref totalBytesUploaded);
                    if (result.BytesUploaded > 0 && upSw.Elapsed.TotalSeconds > 0.2)
                    {
                        double finalUpMbps = (result.BytesUploaded * 8.0) / (upSw.Elapsed.TotalSeconds * 1_000_000.0);
                        result.UploadSpeedMbps = Math.Round(finalUpMbps, 2);
                        onProgress?.Invoke("upload", result.UploadSpeedMbps, 100, result);
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            result.IsSuccess = true;
            lock (_testHistory)
            {
                _testHistory.Insert(0, result);
                if (_testHistory.Count > 10) _testHistory.RemoveAt(_testHistory.Count - 1);
            }

            return result;
        }
    }
}
