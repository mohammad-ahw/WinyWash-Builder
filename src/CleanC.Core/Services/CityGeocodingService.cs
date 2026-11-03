// src/CleanC.Core/Services/CityGeocodingService.cs
// جست‌وجو و اعتبارسنجی شهر برای ماژول آب‌وهوای هیرو.
// نام شهر همیشه به انگلیسی نگه داشته می‌شود تا مستقل از زبان UI باشد و
// Open-Meteo بتواند مختصات درست را پیدا کند.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CleanC.Core.Services
{
    /// <summary>یک نتیجهٔ شهر از سرویس Geocoding؛ نام‌ها انگلیسی هستند.</summary>
    public sealed record CitySuggestion(
        string Name,
        string Country,
        string Admin1,
        double Latitude,
        double Longitude,
        string CountryCode)
    {
        /// <summary>
        /// پرچم کشور برای نمایش خوانا در لیست پیشنهادها (مثل «🇮🇷»).
        /// منبع: کد دو حرفی ISO که Open-Meteo در فیلد country_code برمی‌گرداند.
        /// </summary>
        public string FlagEmoji => InternetSpeedTestService.GetCountryFlagEmoji(CountryCode);

        /// <summary>برچسب خوانا برای لیست پیشنهادها؛ بخش‌های تکراری حذف می‌شوند.</summary>
        public string DisplayName
        {
            get
            {
                var parts = new List<string>();
                foreach (var part in new[] { Name, Admin1, Country })
                {
                    if (string.IsNullOrWhiteSpace(part)) continue;
                    if (parts.Any(p => string.Equals(p, part, StringComparison.OrdinalIgnoreCase))) continue;
                    parts.Add(part.Trim());
                }
                return string.Join(", ", parts);
            }
        }
    }

    public enum CityLookupStatus
    {
        Found,
        NotFound,
        Unavailable
    }

    public sealed record CityLookupResult(CityLookupStatus Status, CitySuggestion? City);

    public static class CityGeocodingService
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        static CityGeocodingService()
        {
            // بعضی APIها درخواست بدون User-Agent را رد می‌کنند.
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("WinyWash/1.0");
        }

        /// <summary>حداکثر ۸ پیشنهاد انگلیسی برای متن واردشده برمی‌گرداند.</summary>
        public static async Task<IReadOnlyList<CitySuggestion>> SearchAsync(
            string query,
            int count = 8,
            CancellationToken cancellationToken = default)
        {
            string trimmed = (query ?? string.Empty).Trim();
            if (trimmed.Length < 2) return Array.Empty<CitySuggestion>();

            try
            {
                string url =
                    "https://geocoding-api.open-meteo.com/v1/search" +
                    "?count=" + Math.Clamp(count, 1, 20).ToString(CultureInfo.InvariantCulture) +
                    "&language=en&name=" + Uri.EscapeDataString(trimmed);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(6));
                string json = await Http.GetStringAsync(url, timeout.Token).ConfigureAwait(false);
                return ParseSuggestions(json);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return Array.Empty<CitySuggestion>();
            }
        }

        /// <summary>
        /// متن آزاد را با نتیجهٔ دقیق Geocoding اعتبارسنجی می‌کند.
        /// Unavailable یعنی شبکه/سرویس در دسترس نبود و نباید ورودی کاربر رد شود.
        /// </summary>
        public static async Task<CityLookupResult> ValidateAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            string trimmed = (query ?? string.Empty).Trim();
            if (trimmed.Length < 2)
                return new CityLookupResult(CityLookupStatus.NotFound, null);

            try
            {
                string url =
                    "https://geocoding-api.open-meteo.com/v1/search" +
                    "?count=10&language=en&name=" + Uri.EscapeDataString(trimmed);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(6));
                string json = await Http.GetStringAsync(url, timeout.Token).ConfigureAwait(false);
                var suggestions = ParseSuggestions(json);
                if (suggestions.Count == 0)
                    return new CityLookupResult(CityLookupStatus.NotFound, null);

                var exact = suggestions.FirstOrDefault(c =>
                    string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase));
                return exact != null
                    ? new CityLookupResult(CityLookupStatus.Found, exact)
                    : new CityLookupResult(CityLookupStatus.NotFound, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return new CityLookupResult(CityLookupStatus.Unavailable, null);
            }
        }

        /// <summary>
        /// شهر فعلی کاربر را از IP تشخیص می‌دهد. خروجی انگلیسی است تا در تنظیمات
        /// مستقل از زبان رابط، همان مقدار اولیهٔ قابل‌نمایش باشد.
        /// </summary>
        public static async Task<string?> DetectEnglishCityAsync(CancellationToken cancellationToken = default)
        {
            string? city = await TryIpWhoIsAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(city))
                city = await TryIpApiCoAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(city)) return null;

            string trimmed = city.Trim();
            var lookup = await ValidateAsync(trimmed, cancellationToken).ConfigureAwait(false);
            // اگر Geocoding در دسترس نبود، همان نام شهر IP را نگه می‌داریم.
            return lookup.Status == CityLookupStatus.Found && lookup.City != null
                ? lookup.City.Name
                : trimmed;
        }

        private static async Task<string?> TryIpWhoIsAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                string json = await Http.GetStringAsync("https://ipwho.is/", timeout.Token).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
                    return null;
                return root.TryGetProperty("city", out var cityEl) ? cityEl.GetString() : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string?> TryIpApiCoAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                string json = await Http.GetStringAsync("https://ipapi.co/json/", timeout.Token).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("city", out var cityEl) ? cityEl.GetString() : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private static IReadOnlyList<CitySuggestion> ParseSuggestions(string json)
        {
            var list = new List<CitySuggestion>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("results", out var results) ||
                    results.ValueKind != JsonValueKind.Array)
                {
                    return list;
                }

                foreach (var item in results.EnumerateArray())
                {
                    string name = ReadString(item, "name");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    double lat = ReadDouble(item, "latitude");
                    double lon = ReadDouble(item, "longitude");
                    if (double.IsNaN(lat) || double.IsNaN(lon)) continue;

                    string key = name + "\u001F" + ReadString(item, "admin1") + "\u001F" + ReadString(item, "country");
                    if (list.Any(x => (x.Name + "\u001F" + x.Admin1 + "\u001F" + x.Country)
                            .Equals(key, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    list.Add(new CitySuggestion(
                        name.Trim(),
                        ReadString(item, "country").Trim(),
                        ReadString(item, "admin1").Trim(),
                        lat,
                        lon,
                        ReadString(item, "country_code").Trim()));
                }
            }
            catch
            {
                // JSON ناقص یا تغییر شکل API؛ لیست خالی برگردانده می‌شود.
            }
            return list;
        }

        private static string ReadString(JsonElement item, string property)
            => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

        private static double ReadDouble(JsonElement item, string property)
            => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
               value.TryGetDouble(out double result)
                ? result
                : double.NaN;
    }
}
