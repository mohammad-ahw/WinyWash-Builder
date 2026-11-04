// src/CleanC.UI/NewsFeedService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;

namespace CleanC.UI
{
    // i18n
    public sealed record NewsItem(string Title, string Url, string Category, string Source = "آخرین خبر");

    public static class NewsFeedService
    {
        private static readonly HttpClient _httpClient = new(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private static List<NewsItem>? _cachedNews;
        private static DateTime _cacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
        private static readonly SemaphoreSlim _lock = new(1, 1);

        static NewsFeedService()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                _httpClient.DefaultRequestHeaders.Add("Accept-Language", "fa-IR,fa;q=0.9,en-US;q=0.8,en;q=0.7");
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.Headers");
            }
        }

        public static bool IsInternetConnected()
        {
            try
            {
                return NetworkInterface.GetIsNetworkAvailable();
            }
            catch
            {
                return false;
            }
        }

        public static async Task<List<NewsItem>?> GetLatestNewsAsync(bool forceRefresh = false, CancellationToken ct = default)
        {
            if (!IsInternetConnected())
                return null;

            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!forceRefresh && _cachedNews != null && (DateTime.UtcNow - _cacheTime) < CacheDuration)
                {
                    return _cachedNews;
                }

                var list = new List<NewsItem>();

                // دریافت موازی و فوق‌سریع تمامی ۶ فید به صورت همزمان
                var viewTask = FetchViewNewsAsync(ct);
                var commentTask = FetchCommentNewsAsync(ct);
                var digiatoTask = FetchDigiatoHotNewsAsync(ct);
                var zoomitTask = FetchZoomitAiNewsAsync(ct);
                var techTask = FetchTechNewsAsync(ct);
                var sportTask = FetchSportHeroNewsAsync(ct);

                await Task.WhenAll(viewTask, commentTask, digiatoTask, zoomitTask, techTask, sportTask).ConfigureAwait(false);

                var viewItems = await viewTask.ConfigureAwait(false);
                var commentItem = await commentTask.ConfigureAwait(false);
                var digiatoItem = await digiatoTask.ConfigureAwait(false);
                var zoomitItem = await zoomitTask.ConfigureAwait(false);
                var techItems = await techTask.ConfigureAwait(false);
                var sportItem = await sportTask.ConfigureAwait(false);

                // ۱. اولویت اول: ۳ خبر از https://akharinkhabar.ir/?type=view
                list.AddRange(viewItems.Take(3));

                // ۲. اولویت دوم: ۱ خبر از https://akharinkhabar.ir/?type=comment (پربحث‌ترین روز)
                if (commentItem != null)
                    list.Add(commentItem);

                // ۳. اولویت سوم: خبر داغ دیجیاتو
                if (digiatoItem != null)
                    list.Add(digiatoItem);

                // ۴. اولویت چهارم: هیرو مقاله هوش مصنوعی زومیت
                if (zoomitItem != null)
                    list.Add(zoomitItem);

                // ۵. اولویت پنجم: ۱ خبر از https://akharinkhabar.ir/tech
                if (techItems.Count > 0)
                    list.Add(techItems[0]);

                // ۶. اولویت ششم (خبر آخر): هیرو کارت از https://akharinkhabar.ir/sport
                if (sportItem != null)
                    list.Add(sportItem);

                if (list.Count > 0)
                {
                    _cachedNews = list;
                    _cacheTime = DateTime.UtcNow;
                    return list;
                }

                return null;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.GetLatestNewsAsync");
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        private static async Task<List<NewsItem>> FetchViewNewsAsync(CancellationToken ct)
        {
            try
            {
                var viewHtml = await _httpClient.GetStringAsync("https://akharinkhabar.ir/?type=view", ct).ConfigureAwait(false);
                return ParseViewNews(viewHtml);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchView");
                return new List<NewsItem>();
            }
        }

        private static async Task<NewsItem?> FetchCommentNewsAsync(CancellationToken ct)
        {
            try
            {
                var commentHtml = await _httpClient.GetStringAsync("https://akharinkhabar.ir/?type=comment", ct).ConfigureAwait(false);
                return ParseCommentNews(commentHtml);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchComment");
                return null;
            }
        }

        private static async Task<List<NewsItem>> FetchTechNewsAsync(CancellationToken ct)
        {
            try
            {
                var techHtml = await _httpClient.GetStringAsync("https://akharinkhabar.ir/tech", ct).ConfigureAwait(false);
                return ParseTechNews(techHtml);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchTech");
                return new List<NewsItem>();
            }
        }

        private static async Task<NewsItem?> FetchZoomitAiNewsAsync(CancellationToken ct)
        {
            try
            {
                var zoomitHtml = await _httpClient.GetStringAsync("https://www.zoomit.ir/ai-articles/", ct).ConfigureAwait(false);
                return ParseZoomitAiHero(zoomitHtml);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchZoomitAi");
                return null;
            }
        }

        private static async Task<NewsItem?> FetchDigiatoHotNewsAsync(CancellationToken ct)
        {
            try
            {
                var digiatoHtml = await _httpClient.GetStringAsync("https://digiato.com/", ct).ConfigureAwait(false);
                return ParseDigiatoHotNews(digiatoHtml);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchDigiatoHot");
                return null;
            }
        }

        private static async Task<NewsItem?> FetchSportHeroNewsAsync(CancellationToken ct)
        {
            try
            {
                var sportHtml = await _httpClient.GetStringAsync("https://akharinkhabar.ir/sport", ct).ConfigureAwait(false);
                return ParseSportHeroNews(sportHtml);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchSportHero");
                return null;
            }
        }

        // i18n
        private static List<NewsItem> ParseViewNews(string html)
        {
            var results = new List<NewsItem>();
            if (string.IsNullOrEmpty(html)) return results;

            int newslistIdx = html.IndexOf("id=\"newslist\"", StringComparison.OrdinalIgnoreCase);
            string searchArea = newslistIdx >= 0 ? html.Substring(newslistIdx) : html;

            var matches = Regex.Matches(searchArea, @"<a\s+class=""[^""]*simple-card-module[^""]*wrapper""[^>]*href=""([^""]+)""[\s\S]*?<div\s+class=""[^""]*title[^""]*"">([^<]+)</div>");
            foreach (Match m in matches)
            {
                if (results.Count >= 3) break;
                string href = m.Groups[1].Value.Trim();
                string title = CleanTitle(m.Groups[2].Value);
                if (string.IsNullOrWhiteSpace(title)) continue;

                if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    href = "https://akharinkhabar.ir" + href;

                // i18n
                results.Add(new NewsItem(title, href, "پربازدید روز", "آخرین خبر"));
            }

            // i18n
            if (results.Count < 3)
            {
                var fallbackMatches = Regex.Matches(searchArea, @"href=""(/(?:world|politics|sport|social|money)/\d+/[^""]+)""[\s\S]*?<div\s+class=""[^""]*title[^""]*"">([^<]+)</div>");
                foreach (Match m in fallbackMatches)
                {
                    if (results.Count >= 3) break;
                    string href = m.Groups[1].Value.Trim();
                    string title = CleanTitle(m.Groups[2].Value);
                    if (string.IsNullOrWhiteSpace(title) || results.Exists(r => r.Url.EndsWith(href, StringComparison.OrdinalIgnoreCase))) continue;

                    if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        href = "https://akharinkhabar.ir" + href;

                    // i18n
                    results.Add(new NewsItem(title, href, "پربازدید روز", "آخرین خبر"));
                }
            }

            return results;
        }

        // i18n
        private static NewsItem? ParseCommentNews(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;

            int newslistIdx = html.IndexOf("id=\"newslist\"", StringComparison.OrdinalIgnoreCase);
            string searchArea = newslistIdx >= 0 ? html.Substring(newslistIdx) : html;

            var match = Regex.Match(searchArea, @"<a\s+class=""[^""]*simple-card-module[^""]*wrapper""[^>]*href=""([^""]+)""[\s\S]*?<div\s+class=""[^""]*title[^""]*"">([^<]+)</div>");
            if (match.Success)
            {
                string href = match.Groups[1].Value.Trim();
                string title = CleanTitle(match.Groups[2].Value);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        href = "https://akharinkhabar.ir" + href;

                    // i18n
                    return new NewsItem(title, href, "پربحث‌ترین روز", "آخرین خبر");
                }
            }

            var fallbackMatch = Regex.Match(searchArea, @"href=""(/(?:world|politics|sport|social|money)/\d+/[^""]+)""[\s\S]*?<div\s+class=""[^""]*title[^""]*"">([^<]+)</div>");
            if (fallbackMatch.Success)
            {
                string href = fallbackMatch.Groups[1].Value.Trim();
                string title = CleanTitle(fallbackMatch.Groups[2].Value);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        href = "https://akharinkhabar.ir" + href;

                    // i18n
                    return new NewsItem(title, href, "پربحث‌ترین روز", "آخرین خبر");
                }
            }

            return null;
        }

        // i18n
        private static List<NewsItem> ParseTechNews(string html)
        {
            var results = new List<NewsItem>();
            if (string.IsNullOrEmpty(html)) return results;

            int mostIdx = html.IndexOf("پربازدیدترین اخبار", StringComparison.OrdinalIgnoreCase);
            string searchArea = mostIdx >= 0 ? html.Substring(mostIdx) : html;

            var matches = Regex.Matches(searchArea, @"<a\s+class=""[^""]*simple-card-module[^""]*wrapper""[^>]*href=""([^""]+)""[\s\S]*?<div\s+class=""[^""]*title[^""]*"">([^<]+)</div>");
            foreach (Match m in matches)
            {
                if (results.Count >= 1) break;
                string href = m.Groups[1].Value.Trim();
                string title = CleanTitle(m.Groups[2].Value);
                if (string.IsNullOrWhiteSpace(title)) continue;

                if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    href = "https://akharinkhabar.ir" + href;

                // i18n
                results.Add(new NewsItem(title, href, "دنیای فناوری", "آخرین خبر"));
            }

            // i18n
            if (results.Count < 1)
            {
                var fallbackMatches = Regex.Matches(searchArea, @"href=""(/tech/\d+/[^""]+)""[\s\S]*?<div\s+class=""[^""]*title[^""]*"">([^<]+)</div>");
                foreach (Match m in fallbackMatches)
                {
                    if (results.Count >= 1) break;
                    string href = m.Groups[1].Value.Trim();
                    string title = CleanTitle(m.Groups[2].Value);
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        href = "https://akharinkhabar.ir" + href;

                    // i18n
                    results.Add(new NewsItem(title, href, "دنیای فناوری", "آخرین خبر"));
                }
            }

            return results;
        }

        // i18n
        private static NewsItem? ParseZoomitAiHero(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;

            try
            {
                // روش ۱: اسکریپت JSON-LD (پایدارترین روش ضد تغییر ساختار DOM)
                var jsonLdMatch = Regex.Match(html, @"\{""@type"":""ListItem"",""name"":""([^""]+)"",""url"":""(https://www\.zoomit\.ir/ai-articles/[^""]+)""");
                if (jsonLdMatch.Success)
                {
                    string title = CleanTitle(jsonLdMatch.Groups[1].Value);
                    string url = jsonLdMatch.Groups[2].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
                    {
                        // i18n
                        return new NewsItem(title, url, "هوش مصنوعی", "زومیت");
                    }
                }

                // روش ۲: تگ هیرو مقاله در DOM با کلاس‌های فونت هدر
                var domMatch = Regex.Match(html, @"<a\s+[^>]*href=""(/ai-articles/[^""]+)""[^>]*>[\s\S]*?<span\s+class=""[^""]*text-h[12][^""]*""[^>]*>([^<]+)</span>");
                if (domMatch.Success)
                {
                    string path = domMatch.Groups[1].Value.Trim();
                    string title = CleanTitle(domMatch.Groups[2].Value);
                    string url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : "https://www.zoomit.ir" + path;
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        // i18n
                        return new NewsItem(title, url, "هوش مصنوعی", "زومیت");
                    }
                }

                // روش ۳: فال‌بک عمومی اسلاگ‌های هوش مصنوعی
                var fallbackMatch = Regex.Match(html, @"href=""(/ai-articles/\d+-[^""]+)""[^>]*>[\s\S]*?<span[^>]*>([^<]{15,120})</span>");
                if (fallbackMatch.Success)
                {
                    string path = fallbackMatch.Groups[1].Value.Trim();
                    string title = CleanTitle(fallbackMatch.Groups[2].Value);
                    string url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : "https://www.zoomit.ir" + path;
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        // i18n
                        return new NewsItem(title, url, "هوش مصنوعی", "زومیت");
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.ParseZoomitAiHero");
            }

            return null;
        }

        // i18n
        private static NewsItem? ParseDigiatoHotNews(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;

            try
            {
                int footerIdx = html.IndexOf("id=\"homeCardsFooter\"", StringComparison.OrdinalIgnoreCase);
                string searchArea = footerIdx >= 0 ? html.Substring(footerIdx, Math.Min(4000, html.Length - footerIdx)) : html;

                // استخراج تیتر و لینک اولین کارت داغ‌ترین‌های امروز
                var match = Regex.Match(searchArea, @"<a\s+[^>]*href=""([^""]+)""\s+class=""miniCard__title"">\s*([^<]+)\s*</a>");
                if (!match.Success)
                {
                    match = Regex.Match(searchArea, @"<a\s+[^>]*href=""([^""]+)""\s+class=""miniCard__picture""\s+aria-label=""([^""]+)""");
                }

                if (match.Success)
                {
                    string url = match.Groups[1].Value.Trim();
                    string title = CleanTitle(match.Groups[2].Value);
                    if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
                    {
                        // i18n
                        return new NewsItem(title, url, "داغ‌ترین‌های روز", "دیجیاتو");
                    }
                }

                // فال‌بک عمومی در صورت جابجایی تگ‌های کارت
                if (footerIdx >= 0)
                {
                    var fallbackMatch = Regex.Match(searchArea, @"href=""(https://digiato\.com/[^""]+)""[^>]*class=""[^""]*title[^""]*"">\s*([^<]{10,120})\s*</a>");
                    if (fallbackMatch.Success)
                    {
                        string url = fallbackMatch.Groups[1].Value.Trim();
                        string title = CleanTitle(fallbackMatch.Groups[2].Value);
                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            // i18n
                            return new NewsItem(title, url, "داغ‌ترین‌های روز", "دیجیاتو");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.ParseDigiatoHotNews");
            }

            return null;
        }

        // i18n
        private static NewsItem? ParseSportHeroNews(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;

            try
            {
                // روش ۱: کارت اصلی هیرو ورزشی (main-module__title)
                var match = Regex.Match(html, @"class=""[^""]*main-module[^""]*__title""[^>]*>\s*<a\s+href=""([^""]+)""[^>]*>([^<]+)</a>");
                if (match.Success)
                {
                    string href = match.Groups[1].Value.Trim();
                    string title = CleanTitle(match.Groups[2].Value);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            href = "https://akharinkhabar.ir" + href;

                        // i18n
                        return new NewsItem(title, href, "هیرو ورزشی", "آخرین خبر");
                    }
                }

                // روش ۲: فال‌بک اولین لینک خبری با الگوی ورزشی
                var fallbackMatch = Regex.Match(html, @"<div\s+class=""[^""]*title[^""]*"">\s*<a\s+href=""(/sport/\d+/[^""]+)""[^>]*>([^<]+)</a>");
                if (fallbackMatch.Success)
                {
                    string href = fallbackMatch.Groups[1].Value.Trim();
                    string title = CleanTitle(fallbackMatch.Groups[2].Value);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            href = "https://akharinkhabar.ir" + href;

                        // i18n
                        return new NewsItem(title, href, "هیرو ورزشی", "آخرین خبر");
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.ParseSportHeroNews");
            }

            return null;
        }

        private static string CleanTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string decoded = WebUtility.HtmlDecode(raw).Trim();
            return Regex.Replace(decoded, @"\s+", " ");
        }
    }
}
