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

            // 1. Fetch IP, ISP, and Location metadata
            try
            {
                using var ipCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ipCts.Token);
                
                string json = await _httpClient.GetStringAsync("http://ip-api.com/json", linkedCts.Token);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("query", out var q)) result.ClientIp = q.GetString() ?? "";
                if (root.TryGetProperty("isp", out var isp)) result.IspName = isp.GetString() ?? "";
                if (root.TryGetProperty("country", out var cnt)) result.Country = cnt.GetString() ?? "";
                if (root.TryGetProperty("city", out var city)) result.City = city.GetString() ?? "";
            }
            catch
            {
                // Fallback to ipify if ip-api timed out
                try
                {
                    using var ipifyCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    result.ClientIp = await _httpClient.GetStringAsync("https://api.ipify.org", ipifyCts.Token);
                    result.IspName = "شبکه اینترنت";
                    result.Country = "جهانی";
                }
                catch
                {
                    result.ClientIp = "نامشخص";
                    result.IspName = "شبکه اینترنت";
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
                            break;
                        }
                    }
                    catch { }
                }

                if (pings.Count > 0)
                {
                    result.PingMs = Math.Round(pings[0], 1);
                    onProgress?.Invoke("ping", result.PingMs, 0, result);
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                result.PingMs = 0;
            }

            // 3. Measure Download Speed with robust multi-server cascade
            var downloadCandidates = new (string url, long targetBytes)[]
            {
                ("https://speed.cloudflare.com/__down?bytes=10000000", 10_000_000L),
                ("https://cachefly.cachefly.net/10mb.test", 10_485_760L),
                ("https://proof.ovh.net/files/10Mb.dat", 10_485_760L)
            };

            bool downloadSucceeded = false;

            foreach (var (url, targetBytes) in downloadCandidates)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!response.IsSuccessStatusCode) continue;

                    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    byte[] buffer = new byte[64 * 1024]; // 64 KB buffer

                    var sw = Stopwatch.StartNew();
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        totalRead += bytesRead;
                        double elapsedSec = sw.Elapsed.TotalSeconds;

                        if (elapsedSec > 0.08)
                        {
                            double currentMbps = (totalRead * 8.0) / (elapsedSec * 1_000_000.0);
                            int progressPct = (int)Math.Clamp((totalRead * 100) / targetBytes, 1, 99);
                            onProgress?.Invoke("download", Math.Round(currentMbps, 2), progressPct, result);
                        }
                    }

                    sw.Stop();
                    result.BytesDownloaded = totalRead;

                    if (sw.Elapsed.TotalSeconds > 0.1 && totalRead > 0)
                    {
                        result.DownloadSpeedMbps = Math.Round((totalRead * 8.0) / (sw.Elapsed.TotalSeconds * 1_000_000.0), 2);
                        onProgress?.Invoke("download", result.DownloadSpeedMbps, 100, result);
                        downloadSucceeded = true;
                        break;
                    }
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // Try next candidate
                }
            }

            if (!downloadSucceeded)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.ErrorMessage = "تست توسط کاربر لغو گردید.";
                    return result;
                }
                result.ErrorMessage = "خطا در اتصال به سرور دانلود اینترنت.";
                return result;
            }

            // 4. Measure Upload Speed (POST 2.5 MB to Cloudflare __up)
            try
            {
                if (!cancellationToken.IsCancellationRequested && result.DownloadSpeedMbps > 0)
                {
                    int uploadBytesCount = 2_500_000; // 2.5 MB payload
                    byte[] uploadPayload = new byte[uploadBytesCount];
                    Random.Shared.NextBytes(uploadPayload);

                    onProgress?.Invoke("upload", 0, 5, result);

                    using var upContent = new ByteArrayContent(uploadPayload);
                    var upSw = Stopwatch.StartNew();
                    using var upResp = await _httpClient.PostAsync("https://speed.cloudflare.com/__up", upContent, cancellationToken);
                    upSw.Stop();

                    if (upResp.IsSuccessStatusCode && upSw.Elapsed.TotalSeconds > 0.1)
                    {
                        double upMbps = (uploadBytesCount * 8.0) / (upSw.Elapsed.TotalSeconds * 1_000_000.0);
                        result.UploadSpeedMbps = Math.Round(upMbps, 2);
                        result.BytesUploaded = uploadBytesCount;
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
