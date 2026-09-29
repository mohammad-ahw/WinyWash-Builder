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
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25)
        };

        private static readonly List<SpeedTestResult> _testHistory = new();

        public static IReadOnlyList<SpeedTestResult> GetHistory()
        {
            lock (_testHistory)
            {
                return new List<SpeedTestResult>(_testHistory);
            }
        }

        public static async Task<SpeedTestResult> RunSpeedTestAsync(
            Action<string, double, int>? onProgress = null, 
            CancellationToken cancellationToken = default)
        {
            var result = new SpeedTestResult();

            // 1. Fetch IP, ISP, and Location metadata
            try
            {
                using var ipCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
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
                // Non-critical, continue testing
                result.ClientIp = "نامشخص";
                result.IspName = "شبکه محلی / اینترنت";
            }

            // 2. Measure Ping (Latency)
            try
            {
                var pings = new List<double>();
                for (int i = 0; i < 3; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var sw = Stopwatch.StartNew();
                    using var pingResp = await _httpClient.GetAsync("https://speed.cloudflare.com/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    sw.Stop();
                    pings.Add(sw.Elapsed.TotalMilliseconds);
                    await Task.Delay(40, cancellationToken);
                }

                if (pings.Count > 0)
                {
                    pings.Sort();
                    result.PingMs = Math.Round(pings[0], 1); // Best latency
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                result.PingMs = 0;
            }

            // 3. Measure Download Speed (Single reliable Cloudflare stream - 15 MB)
            try
            {
                long targetBytes = 15_000_000; // 15 MB
                string url = $"https://speed.cloudflare.com/__down?bytes={targetBytes}";

                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

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
                        onProgress?.Invoke("download", Math.Round(currentMbps, 2), progressPct);
                    }
                }

                sw.Stop();
                result.BytesDownloaded = totalRead;
                if (sw.Elapsed.TotalSeconds > 0.1 && totalRead > 0)
                {
                    result.DownloadSpeedMbps = Math.Round((totalRead * 8.0) / (sw.Elapsed.TotalSeconds * 1_000_000.0), 2);
                    onProgress?.Invoke("download", result.DownloadSpeedMbps, 100);
                }
            }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.ErrorMessage = "تست توسط کاربر لغو گردید.";
                    return result;
                }
                result.ErrorMessage = "خطا در اتصال به سرور دانلود اینترنت.";
            }

            // 4. Measure Upload Speed (POST 3 MB to https://speed.cloudflare.com/__up)
            try
            {
                if (!cancellationToken.IsCancellationRequested && result.DownloadSpeedMbps > 0)
                {
                    int uploadBytesCount = 3_000_000; // 3 MB payload
                    byte[] uploadPayload = new byte[uploadBytesCount];
                    Random.Shared.NextBytes(uploadPayload);

                    onProgress?.Invoke("upload", 0, 10);

                    using var upContent = new ByteArrayContent(uploadPayload);
                    var upSw = Stopwatch.StartNew();
                    using var upResp = await _httpClient.PostAsync("https://speed.cloudflare.com/__up", upContent, cancellationToken);
                    upSw.Stop();

                    if (upResp.IsSuccessStatusCode && upSw.Elapsed.TotalSeconds > 0.1)
                    {
                        double upMbps = (uploadBytesCount * 8.0) / (upSw.Elapsed.TotalSeconds * 1_000_000.0);
                        result.UploadSpeedMbps = Math.Round(upMbps, 2);
                        result.BytesUploaded = uploadBytesCount;
                        onProgress?.Invoke("upload", result.UploadSpeedMbps, 100);
                    }
                }
            }
            catch { }

            if (result.DownloadSpeedMbps > 0)
            {
                result.IsSuccess = true;
                lock (_testHistory)
                {
                    _testHistory.Insert(0, result);
                    if (_testHistory.Count > 10) _testHistory.RemoveAt(_testHistory.Count - 1);
                }
            }

            return result;
        }
    }
}
