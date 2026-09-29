using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CleanC.Core.Services
{
    public class SpeedTestResult
    {
        public double PingMs { get; set; }
        public double DownloadSpeedMbps { get; set; }
        public double DownloadSpeedMBs => DownloadSpeedMbps / 8.0;
        public long BytesDownloaded { get; set; }
    }

    public class InternetSpeedTestService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static async Task<SpeedTestResult> RunSpeedTestAsync(Action<double, int>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var result = new SpeedTestResult();

            // 1. Measure Ping (Latency)
            try
            {
                var pingSw = Stopwatch.StartNew();
                using var pingResp = await _httpClient.GetAsync("https://speed.cloudflare.com/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                pingSw.Stop();
                result.PingMs = Math.Round(pingSw.Elapsed.TotalMilliseconds, 1);
            }
            catch
            {
                result.PingMs = 0;
            }

            // 2. Measure Download Speed (Stream 20 MB)
            try
            {
                long targetBytes = 20_000_000; // 20 MB
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

                    if (elapsedSec > 0.1)
                    {
                        double currentMbps = (totalRead * 8.0) / (elapsedSec * 1_000_000.0);
                        int progressPct = (int)Math.Min(100, (totalRead * 100) / targetBytes);
                        onProgress?.Invoke(Math.Round(currentMbps, 2), progressPct);
                    }
                }

                sw.Stop();
                result.BytesDownloaded = totalRead;
                result.DownloadSpeedMbps = Math.Round((totalRead * 8.0) / (sw.Elapsed.TotalSeconds * 1_000_000.0), 2);
            }
            catch
            {
                // Return what was measured
            }

            return result;
        }
    }
}
