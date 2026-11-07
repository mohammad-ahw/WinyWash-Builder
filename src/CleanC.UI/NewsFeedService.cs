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
using System.Xml.Linq;
using CleanC.Core.Diagnostics;
using CleanC.Core.Services;

namespace CleanC.UI
{
    // Record for holding news headline information
    public sealed record NewsItem(string Title, string Url, string Category, string Source = "News");

    public static class NewsFeedService
    {
        private static readonly HttpClient _httpClient = new(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private static List<NewsItem>? _cachedFaNews;
        private static DateTime _cacheTimeFa = DateTime.MinValue;

        private static List<NewsItem>? _cachedEnNews;
        private static DateTime _cacheTimeEn = DateTime.MinValue;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
        private static readonly SemaphoreSlim _lock = new(1, 1);

        static NewsFeedService()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9,fa;q=0.8");
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

        public static bool IsCurrentLanguagePersian()
        {
            try
            {
                return LocalizationService.IsRtl || string.Equals(LocalizationService.CurrentLanguage?.Code, "fa-IR", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }

        public static Task<List<NewsItem>?> GetLatestNewsAsync(bool forceRefresh = false, CancellationToken ct = default)
        {
            return GetLatestNewsAsync(IsCurrentLanguagePersian(), forceRefresh, ct);
        }

        public static async Task<List<NewsItem>?> GetLatestNewsAsync(bool isPersian, bool forceRefresh = false, CancellationToken ct = default)
        {
            if (!IsInternetConnected())
                return null;

            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (isPersian)
                {
                    if (!forceRefresh && _cachedFaNews != null && (DateTime.UtcNow - _cacheTimeFa) < CacheDuration)
                    {
                        return _cachedFaNews;
                    }

                    var faList = await FetchPersianNewsAsync(ct).ConfigureAwait(false);
                    if (faList.Count > 0)
                    {
                        _cachedFaNews = faList;
                        _cacheTimeFa = DateTime.UtcNow;
                        return faList;
                    }
                    return _cachedFaNews;
                }
                else
                {
                    if (!forceRefresh && _cachedEnNews != null && (DateTime.UtcNow - _cacheTimeEn) < CacheDuration)
                    {
                        return _cachedEnNews;
                    }

                    var enList = await FetchInternationalNewsAsync(ct).ConfigureAwait(false);
                    if (enList.Count > 0)
                    {
                        _cachedEnNews = enList;
                        _cacheTimeEn = DateTime.UtcNow;
                        return enList;
                    }
                    return _cachedEnNews;
                }
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

        // ====================================================================
        // INTERNATIONAL NEWS ENGINE (The Verge, TechCrunch, Wired, Ars Technica, Yahoo Sports)
        // ====================================================================

        private static async Task<List<NewsItem>> FetchInternationalNewsAsync(CancellationToken ct)
        {
            var list = new List<NewsItem>();
            try
            {
                // Fetch top international feeds in parallel
                var vergeTask = FetchRssFeedAsync("https://www.theverge.com/rss/index.xml", "The Verge", "Tech", ct);
                var tcTask = FetchRssFeedAsync("https://techcrunch.com/category/artificial-intelligence/feed/", "TechCrunch", "AI", ct);
                var wiredTask = FetchRssFeedAsync("https://www.wired.com/feed/rss", "Wired", "Tech", ct);
                var arsTask = FetchRssFeedAsync("https://feeds.arstechnica.com/arstechnica/index", "Ars Technica", "Science", ct);
                var sportsTask = FetchRssFeedAsync("https://sports.yahoo.com/rss/", "Yahoo Sports", "Sports", ct);

                await Task.WhenAll(vergeTask, tcTask, wiredTask, arsTask, sportsTask).ConfigureAwait(false);

                var verge = await vergeTask.ConfigureAwait(false);
                var tc = await tcTask.ConfigureAwait(false);
                var wired = await wiredTask.ConfigureAwait(false);
                var ars = await arsTask.ConfigureAwait(false);
                var sports = await sportsTask.ConfigureAwait(false);

                // 1. The Verge (Top Tech/AI story)
                if (verge.Count > 0) list.Add(verge[0]);

                // 2. TechCrunch (Top AI & Startup story)
                if (tc.Count > 0) list.Add(tc[0]);

                // 3. Wired (Top Tech & Innovation story)
                if (wired.Count > 0) list.Add(wired[0]);

                // 4. Ars Technica (Top Technology Feature)
                if (ars.Count > 0) list.Add(ars[0]);

                // 5. Yahoo Sports (Top Sports story)
                if (sports.Count > 0) list.Add(sports[0]);

                // If any primary feed failed, backfill from available feed items to guarantee 5 stories
                var pool = new[] { verge, tc, wired, ars, sports };
                for (int idx = 1; list.Count < 5 && idx < 4; idx++)
                {
                    foreach (var feed in pool)
                    {
                        if (feed.Count > idx && !list.Any(x => x.Url == feed[idx].Url))
                        {
                            list.Add(feed[idx]);
                            if (list.Count >= 5) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchInternationalNewsAsync");
            }
            return list;
        }

        private static async Task<List<NewsItem>> FetchRssFeedAsync(string feedUrl, string sourceName, string category, CancellationToken ct)
        {
            var items = new List<NewsItem>();
            try
            {
                var xml = await _httpClient.GetStringAsync(feedUrl, ct).ConfigureAwait(false);
                var doc = XDocument.Parse(xml);
                var entries = doc.Descendants().Where(e => e.Name.LocalName == "entry" || e.Name.LocalName == "item");
                foreach (var entry in entries)
                {
                    var titleEl = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "title");
                    string title = titleEl?.Value ?? "";

                    var linkEl = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "link");
                    string link = "";
                    if (linkEl != null)
                    {
                        var hrefAttr = linkEl.Attribute("href");
                        link = hrefAttr != null ? hrefAttr.Value : linkEl.Value;
                    }

                    title = CleanTitle(title);
                    title = Regex.Replace(title, "<.*?>", string.Empty).Trim();
                    link = link.Trim();

                    if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(link) && !link.Contains("#comments"))
                    {
                        items.Add(new NewsItem(title, link, category, sourceName));
                        if (items.Count >= 4) break;
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, $"NewsFeedService.FetchRssFeedAsync.{sourceName}");
            }
            return items;
        }

        // ====================================================================
        // PERSIAN NEWS ENGINE (Akharin Khabar, Digiato, Zoomit)
        // ====================================================================

        private static async Task<List<NewsItem>> FetchPersianNewsAsync(CancellationToken ct)
        {
            var list = new List<NewsItem>();
            try
            {
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
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchPersianNewsAsync");
            }
            return list;
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
                var items = ParseViewNews(commentHtml);
                if (items.Count > 0)
                {
                    return items[0] with { Category = "پربحث‌ترین روز", Source = "آخرین خبر" };
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchComment");
            }
            return null;
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
                CcLog.Ignore(ex, "NewsFeedService.FetchDigiatoHotNews");
            }
            return null;
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
                CcLog.Ignore(ex, "NewsFeedService.FetchZoomitAiNews");
            }
            return null;
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

        private static async Task<NewsItem?> FetchSportHeroNewsAsync(CancellationToken ct)
        {
            try
            {
                var sportHtml = await _httpClient.GetStringAsync("https://akharinkhabar.ir/sport", ct).ConfigureAwait(false);
                return ParseSportHeroNews(sportHtml);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchSportHeroNews");
            }
            return null;
        }

        private static List<NewsItem> ParseViewNews(string html)
        {
            var items = new List<NewsItem>();
            if (string.IsNullOrEmpty(html)) return items;

            try
            {
                var matches = Regex.Matches(html, @"<a\s+[^>]*href=""([^""]+)""[^>]*>\s*<h2[^>]*>([^<]+)</h2>");
                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        string href = match.Groups[1].Value.Trim();
                        string title = CleanTitle(match.Groups[2].Value);

                        if (!string.IsNullOrWhiteSpace(title) && !title.Contains("آخرین خبر") && title.Length > 8)
                        {
                            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                href = "https://akharinkhabar.ir" + href;

                            items.Add(new NewsItem(title, href, "اخبار برگزیده", "آخرین خبر"));
                            if (items.Count >= 4) break;
                        }
                    }
                }

                if (items.Count == 0)
                {
                    var fallbackMatches = Regex.Matches(html, @"<a\s+[^>]*href=""(/[^""]+)""[^>]*class=""[^""]*title[^""]*""[^>]*>([^<]+)</a>");
                    foreach (Match m in fallbackMatches)
                    {
                        string href = "https://akharinkhabar.ir" + m.Groups[1].Value.Trim();
                        string title = CleanTitle(m.Groups[2].Value);
                        if (!string.IsNullOrWhiteSpace(title) && title.Length > 8)
                        {
                            items.Add(new NewsItem(title, href, "اخبار برگزیده", "آخرین خبر"));
                            if (items.Count >= 4) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.ParseViewNews");
            }

            return items;
        }

        private static List<NewsItem> ParseTechNews(string html)
        {
            var items = new List<NewsItem>();
            if (string.IsNullOrEmpty(html)) return items;

            try
            {
                var matches = Regex.Matches(html, @"<a\s+[^>]*href=""([^""]+)""[^>]*>\s*<h2[^>]*>([^<]+)</h2>");
                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        string href = match.Groups[1].Value.Trim();
                        string title = CleanTitle(match.Groups[2].Value);

                        if (!string.IsNullOrWhiteSpace(title) && !title.Contains("آخرین خبر") && title.Length > 8)
                        {
                            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                href = "https://akharinkhabar.ir" + href;

                            items.Add(new NewsItem(title, href, "فناوری و هوش مصنوعی", "آخرین خبر"));
                            if (items.Count >= 3) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.ParseTechNews");
            }

            return items;
        }

        private static NewsItem? ParseZoomitAiHero(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;

            try
            {
                var match = Regex.Match(html, @"<a\s+[^>]*href=""(/ai-articles/\d+-[^""]+)""[^>]*>\s*<span\s+class=""typography__Root[^""]*"">([^<]+)</span>");
                if (match.Success)
                {
                    string href = "https://www.zoomit.ir" + match.Groups[1].Value.Trim();
                    string title = CleanTitle(match.Groups[2].Value);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        return new NewsItem(title, href, "هوش مصنوعی", "زومیت");
                    }
                }

                var fallbackMatch = Regex.Match(html, @"<a\s+[^>]*href=""(https://www\.zoomit\.ir/ai-articles/\d+-[^""]+)""[^>]*title=""([^""]+)""");
                if (fallbackMatch.Success)
                {
                    string href = fallbackMatch.Groups[1].Value.Trim();
                    string title = CleanTitle(fallbackMatch.Groups[2].Value);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        return new NewsItem(title, href, "هوش مصنوعی", "زومیت");
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.ParseZoomitAiHero");
            }

            return null;
        }

        private static NewsItem? ParseDigiatoHotNews(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;

            try
            {
                int footerIdx = html.IndexOf("id=\"homeCardsFooter\"", StringComparison.OrdinalIgnoreCase);
                string searchArea = footerIdx >= 0 ? html.Substring(footerIdx, Math.Min(4000, html.Length - footerIdx)) : html;

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
                        return new NewsItem(title, url, "داغ‌ترین‌های روز", "دیجیاتو");
                    }
                }

                if (footerIdx >= 0)
                {
                    var fallbackMatch = Regex.Match(searchArea, @"href=""(https://digiato\.com/[^""]+)""[^>]*class=""[^""]*title[^""]*"">\s*([^<]{10,120})\s*</a>");
                    if (fallbackMatch.Success)
                    {
                        string url = fallbackMatch.Groups[1].Value.Trim();
                        string title = CleanTitle(fallbackMatch.Groups[2].Value);
                        if (!string.IsNullOrWhiteSpace(title))
                        {
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

        private static NewsItem? ParseSportHeroNews(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;

            try
            {
                var match = Regex.Match(html, @"class=""[^""]*main-module[^""]*__title""[^>]*>\s*<a\s+href=""([^""]+)""[^>]*>([^<]+)</a>");
                if (match.Success)
                {
                    string href = match.Groups[1].Value.Trim();
                    string title = CleanTitle(match.Groups[2].Value);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            href = "https://akharinkhabar.ir" + href;

                        return new NewsItem(title, href, "هیرو ورزشی", "آخرین خبر");
                    }
                }

                var fallbackMatch = Regex.Match(html, @"<div\s+class=""[^""]*title[^""]*"">\s*<a\s+href=""(/sport/\d+/[^""]+)""[^>]*>([^<]+)</a>");
                if (fallbackMatch.Success)
                {
                    string href = fallbackMatch.Groups[1].Value.Trim();
                    string title = CleanTitle(fallbackMatch.Groups[2].Value);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            href = "https://akharinkhabar.ir" + href;

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
