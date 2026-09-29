using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CleanC.Core.Services
{
    public class SpeedTestResult
    {
        public double PingMs { get; set; }
        public double JitterMs { get; set; }
        public double DownloadSpeedMbps { get; set; }
        public double DownloadSpeedMBs => DownloadSpeedMbps / 8.0;
        public long BytesDownloaded { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class InternetSpeedTestService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25)
        };

        private static readonly List<SpeedTestResult> _testHistory = new List<SpeedTestResult>();
        public static IReadOnlyList<SpeedTestResult> History => _testHistory.AsReadOnly();

        public static async Task<SpeedTestResult> RunSpeedTestAsync(Action<double, int>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var result = new SpeedTestResult();

            // 1. Measure Ping & Jitter (4 requests)
            var pings = new List<double>();
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var sw = Stopwatch.StartNew();
                    using var resp = await _httpClient.GetAsync("https://speed.cloudflare.com/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    sw.Stop();
                    if (resp.IsSuccessStatusCode)
                    {
                        pings.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    await Task.Delay(50, cancellationToken);
                }

                if (pings.Count > 0)
                {
                    pings.Sort();
                    result.PingMs = Math.Round(pings[pings.Count / 2], 1); // Median
                    // Jitter = Mean absolute deviation between consecutive pings
                    if (pings.Count >= 2)
                    {
                        double sumDiff = 0;
                        for (int i = 1; i < pings.Count; i++) sumDiff += Math.Abs(pings[i] - pings[i - 1]);
                        result.JitterMs = Math.Round(sumDiff / (pings.Count - 1), 1);
                    }
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                result.PingMs = 0;
                result.ErrorMessage = "امکان برقراری ارتباط با سرور سنجش پینگ وجود ندارد.";
            }

            // 2. Measure Download Speed with 4 parallel streams (12 MB each = 48 MB total)
            try
            {
                int streamCount = 4;
                long bytesPerStream = 12_000_000; // 12 MB
                long totalTargetBytes = streamCount * bytesPerStream;
                long sharedTotalRead = 0;

                var overallSw = Stopwatch.StartNew();
                var sampleTimes = new List<(double timeSec, long bytes)>();

                var tasks = new Task[streamCount];
                for (int i = 0; i < streamCount; i++)
                {
                    tasks[i] = Task.Run(async () =>
                    {
                        try
                        {
                            string url = $"https://speed.cloudflare.com/__down?bytes={bytesPerStream}";
                            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                            response.EnsureSuccessStatusCode();

                            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                            byte[] buffer = new byte[64 * 1024];
                            int bytesRead;

                            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                            {
                                long currentTotal = Interlocked.Add(ref sharedTotalRead, bytesRead);
                                double elapsed = overallSw.Elapsed.TotalSeconds;

                                if (elapsed > 0.1)
                                {
                                    double mbps = (currentTotal * 8.0) / (elapsed * 1_000_000.0);
                                    int pct = (int)Math.Clamp((currentTotal * 100) / totalTargetBytes, 0, 99);
                                    onProgress?.Invoke(Math.Round(mbps, 2), pct);
                                }
                            }
                        }
                        catch { }
                    }, cancellationToken);
                }

                await Task.WhenAll(tasks);
                overallSw.Stop();

                double totalSec = overallSw.Elapsed.TotalSeconds;
                result.BytesDownloaded = sharedTotalRead;

                if (sharedTotalRead > 0 && totalSec > 0.2)
                {
                    result.DownloadSpeedMbps = Math.Round((sharedTotalRead * 8.0) / (totalSec * 1_000_000.0), 2);
                    result.IsSuccess = true;
                    onProgress?.Invoke(result.DownloadSpeedMbps, 100);
                }
                else
                {
                    result.ErrorMessage = "پاسخی از سرور دانلود دریافت نشد.";
                }
            }
            catch (Exception)
            {
                result.ErrorMessage = cancellationToken.IsCancellationRequested ? "تست توسط کاربر متوقف شد." : "خطا در اتصال به سرور.";
            }

            if (result.IsSuccess)
            {
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
