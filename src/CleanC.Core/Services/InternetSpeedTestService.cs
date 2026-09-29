using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CleanC.Core.Services
{
    public class SpeedTestResult
    {
        public double PingMs { get; set; }
        public double DownloadSpeedMbps { get; set; }
        public double DownloadSpeedMBs => Math.Round(DownloadSpeedMbps / 8.0, 2);
        public double UploadSpeedMbps { get; set; }
        public double UploadSpeedMBs => Math.Round(UploadSpeedMbps / 8.0, 2);
        
        public string ClientIp { get; set; } = string.Empty;
        public string IspName { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;

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
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                ServerCertificateCustomValidationCallback = (m, c, ch, e) => true
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://speed.cloudflare.com/");
            _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
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
                    if (headResp.Headers.TryGetValues("cf-meta-country", out var cnts)) result.Country = cnts.FirstOrDefault() ?? "";
                    if (headResp.Headers.TryGetValues("cf-meta-city", out var cities)) result.City = cities.FirstOrDefault() ?? "";
                    result.IspName = "Cloudflare CDN";
                }
                catch
                {
                    // Fallback 2: ipify
                    try
                    {
                        using var ipifyCts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
                        result.ClientIp = await _httpClient.GetStringAsync("https://api.ipify.org", ipifyCts.Token);
                        result.IspName = "شبکه اینترنت";
                        result.Country = "جهانی";
                    }
                    catch
                    {
                        result.ClientIp = "اتصال فعال";
                        result.IspName = "شبکه اینترنت";
                    }
                }
            }

            // Immediately notify UI that metadata is ready!
            onProgress?.Invoke("meta", 0, 0, result);

            // 2. Measure Ping (Latency)
            try
            {
                var pings = new List<double>();
                string[] pingTargets = new[]
                {
                    "https://speed.cloudflare.com/__down?bytes=0",
                    "https://1.1.1.1",
                    "https://www.google.com"
                };

                foreach (var target in pingTargets)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        using var pingResp = await _httpClient.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        sw.Stop();
                        if (pingResp.IsSuccessStatusCode)
                        {
                            pings.Add(sw.Elapsed.TotalMilliseconds);
                        }
                    }
                    catch { }
                }

                if (pings.Count > 0)
                {
                    result.PingMs = Math.Round(pings.Min(), 1);
                    onProgress?.Invoke("ping", result.PingMs, 0, result);
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                result.PingMs = 0;
            }

            // 3. Multi-Stream Concurrent Download Test (Ookla / IDM architecture)
            // 4 parallel connections saturate available bandwidth without single-stream throttling
            long totalBytesDownloaded = 0;
            const int streamCount = 4;
            const int testDurationSec = 6;
            using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            downloadCts.CancelAfter(TimeSpan.FromSeconds(testDurationSec + 1));

            var downloadUrls = new[]
            {
                "https://speed.cloudflare.com/__down?bytes=15000000",
                "https://cachefly.cachefly.net/10mb.test",
                "https://speed.cloudflare.com/__down?bytes=20000000",
                "https://speed.cloudflare.com/__down?bytes=10000000"
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
                            // If stream times out or was cancelled, exit stream loop
                            break;
                        }
                    }
                }));
            }

            // Progress reporting loop with smoothed moving speed (eliminated initial TCP burst)
            double smoothedMbps = 0;
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
                    // Exponential smoothing for realistic gauge feel
                    smoothedMbps = smoothedMbps == 0 ? instantMbps : (0.35 * instantMbps + 0.65 * smoothedMbps);

                    // Ignore initial 0.5s ramp-up for final average calculation
                    if (curTime.TotalSeconds > 0.5 && instantMbps > 0)
                    {
                        recordedSpeeds.Add(instantMbps);
                    }

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
            catch { }

            streamStopwatch.Stop();
            result.BytesDownloaded = Interlocked.Read(ref totalBytesDownloaded);

            if (result.BytesDownloaded > 0)
            {
                double finalAvg = recordedSpeeds.Count > 0 
                    ? recordedSpeeds.Average() 
                    : (result.BytesDownloaded * 8.0) / (Math.Max(streamStopwatch.Elapsed.TotalSeconds, 0.5) * 1_000_000.0);

                result.DownloadSpeedMbps = Math.Round(finalAvg, 2);
                onProgress?.Invoke("download", result.DownloadSpeedMbps, 100, result);
            }
            else
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.ErrorMessage = "تست توسط کاربر لغو گردید.";
                    return result;
                }
                result.ErrorMessage = "خطا در اتصال به سرور دانلود اینترنت.";
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
                    catch { }

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
            catch { }

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
