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
                var commentItems = await commentTask.ConfigureAwait(false);
                var digiatoItems = await digiatoTask.ConfigureAwait(false);
                var zoomitItems = await zoomitTask.ConfigureAwait(false);
                var techItems = await techTask.ConfigureAwait(false);
                var sportItems = await sportTask.ConfigureAwait(false);

                var addedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                void TryAdd(NewsItem? item)
                {
                    if (item != null && !string.IsNullOrWhiteSpace(item.Url) && addedUrls.Add(item.Url))
                    {
                        list.Add(item);
                    }
                }

                // ۱. اولویت اول: ۳ خبر از https://akharinkhabar.ir/?type=view (پربازدیدترین‌ها)
                foreach (var it in viewItems.Take(3))
                    TryAdd(it);

                // ۲. اولویت دوم: ۱ خبر از https://akharinkhabar.ir/?type=comment (پربحث‌ترین روز)
                TryAdd(commentItems.FirstOrDefault(c => !addedUrls.Contains(c.Url)));

                // ۳. اولویت سوم: خبر داغ دیجیاتو
                TryAdd(digiatoItems.FirstOrDefault());

                // ۴. اولویت چهارم: هیرو مقاله هوش مصنوعی زومیت
                TryAdd(zoomitItems.FirstOrDefault());

                // ۵. اولویت پنجم: ۱ خبر از https://akharinkhabar.ir/tech (فناوری و هوش مصنوعی)
                TryAdd(techItems.FirstOrDefault(t => !addedUrls.Contains(t.Url)));

                // ۶. اولویت ششم: هیرو کارت ورزشی از https://akharinkhabar.ir/sport
                TryAdd(sportItems.FirstOrDefault());

                // پشتیبان و پر کردن خودکار در صورت قطعی هر کدام از منابع تا سقف ۷ خبر
                var pool = new[] { viewItems, commentItems, digiatoItems, zoomitItems, techItems, sportItems };
                for (int round = 1; list.Count < 6 && round < 6; round++)
                {
                    foreach (var feed in pool)
                    {
                        if (feed.Count > round)
                        {
                            TryAdd(feed[round]);
                            if (list.Count >= 7) break;
                        }
                    }
                }
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
                return ParseAkharinKhabarLinks(viewHtml, "اخبار برگزیده");
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchView");
                return new List<NewsItem>();
            }
        }

        private static async Task<List<NewsItem>> FetchCommentNewsAsync(CancellationToken ct)
        {
            try
            {
                var commentHtml = await _httpClient.GetStringAsync("https://akharinkhabar.ir/?type=comment", ct).ConfigureAwait(false);
                return ParseAkharinKhabarLinks(commentHtml, "پربحث‌ترین روز");
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchComment");
                return new List<NewsItem>();
            }
        }

        private static async Task<List<NewsItem>> FetchDigiatoHotNewsAsync(CancellationToken ct)
        {
            try
            {
                var digiatoHtml = await _httpClient.GetStringAsync("https://digiato.com/", ct).ConfigureAwait(false);
                return ParseDigiatoHotNews(digiatoHtml);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchDigiatoHotNews");
                return new List<NewsItem>();
            }
        }

        private static async Task<List<NewsItem>> FetchZoomitAiNewsAsync(CancellationToken ct)
        {
            try
            {
                var zoomitHtml = await _httpClient.GetStringAsync("https://www.zoomit.ir/ai-articles/", ct).ConfigureAwait(false);
                return ParseZoomitAiNews(zoomitHtml);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchZoomitAiNews");
                return new List<NewsItem>();
            }
        }

        private static async Task<List<NewsItem>> FetchTechNewsAsync(CancellationToken ct)
        {
            try
            {
                var techHtml = await _httpClient.GetStringAsync("https://akharinkhabar.ir/tech", ct).ConfigureAwait(false);
                return ParseAkharinKhabarLinks(techHtml, "فناوری و هوش مصنوعی", "/tech/");
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchTech");
                return new List<NewsItem>();
            }
        }

        private static async Task<List<NewsItem>> FetchSportHeroNewsAsync(CancellationToken ct)
        {
            try
            {
                var sportHtml = await _httpClient.GetStringAsync("https://akharinkhabar.ir/sport", ct).ConfigureAwait(false);
                return ParseSportHeroNews(sportHtml);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.FetchSportHeroNews");
                return new List<NewsItem>();
            }
        }

        internal static List<NewsItem> ParseAkharinKhabarLinks(string html, string category, string pathFilter = "")
        {
            var items = new List<NewsItem>();
            if (string.IsNullOrEmpty(html)) return items;

            try
            {
                var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // ۱. تطابق لینک‌های استاندارد جدید آخرین خبر با الگوی /category/id/slug
                var matches = Regex.Matches(html, @"<a\s+[^>]*href=""(/[^""]+/\d+/[^""]*)""[^>]*>([\s\S]*?)</a>");
                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        string href = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(pathFilter) && !href.Contains(pathFilter, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string rawInner = match.Groups[2].Value;
                        string title = ExtractCleanNewsText(rawInner);

                        if (IsValidPersianTitle(title))
                        {
                            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                href = "https://akharinkhabar.ir" + href;

                            if (seenUrls.Add(href))
                            {
                                items.Add(new NewsItem(title, href, category, "آخرین خبر"));
                                if (items.Count >= 6) break;
                            }
                        }
                    }
                }

                // ۲. الگوی پشتیبان سنتی (h2 درون a)
                if (items.Count == 0)
                {
                    var oldMatches = Regex.Matches(html, @"<a\s+[^>]*href=""([^""]+)""[^>]*>\s*<h2[^>]*>([^<]+)</h2>");
                    foreach (Match m in oldMatches)
                    {
                        string href = m.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(pathFilter) && !href.Contains(pathFilter, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string title = CleanTitle(m.Groups[2].Value);
                        if (IsValidPersianTitle(title))
                        {
                            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                href = "https://akharinkhabar.ir" + href;

                            if (seenUrls.Add(href))
                            {
                                items.Add(new NewsItem(title, href, category, "آخرین خبر"));
                                if (items.Count >= 6) break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.ParseAkharinKhabarLinks");
            }

            return items;
        }

        internal static List<NewsItem> ParseZoomitAiNews(string html)
        {
            var items = new List<NewsItem>();
            if (string.IsNullOrEmpty(html)) return items;

            try
            {
                var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // ۱. تطابق لینک‌های مقالات هوش مصنوعی زومیت با ساختار روز
                var matches = Regex.Matches(html, @"<a\s+[^>]*href=""([^""]*ai-articles/\d+-[^""]*)""[^>]*>([\s\S]*?)</a>");
                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        string href = match.Groups[1].Value.Trim();
                        string rawInner = match.Groups[2].Value;
                        string title = ExtractCleanNewsText(rawInner);

                        if (IsValidPersianTitle(title))
                        {
                            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                href = "https://www.zoomit.ir" + (href.StartsWith("/") ? href : "/" + href);

                            if (seenUrls.Add(href))
                            {
                                items.Add(new NewsItem(title, href, "هوش مصنوعی", "زومیت"));
                                if (items.Count >= 4) break;
                            }
                        }
                    }
                }

                // ۲. الگوی پشتیبان زومیت با تگ عنوان
                if (items.Count == 0)
                {
                    var fallbackMatches = Regex.Matches(html, @"<a\s+[^>]*href=""([^""]*ai-articles/\d+-[^""]*)""[^>]*title=""([^""]+)""");
                    foreach (Match m in fallbackMatches)
                    {
                        string href = m.Groups[1].Value.Trim();
                        string title = CleanTitle(m.Groups[2].Value);
                        if (IsValidPersianTitle(title))
                        {
                            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                href = "https://www.zoomit.ir" + (href.StartsWith("/") ? href : "/" + href);

                            if (seenUrls.Add(href))
                            {
                                items.Add(new NewsItem(title, href, "هوش مصنوعی", "زومیت"));
                                if (items.Count >= 4) break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.ParseZoomitAiNews");
            }

            return items;
        }

        internal static List<NewsItem> ParseDigiatoHotNews(string html)
        {
            var items = new List<NewsItem>();
            if (string.IsNullOrEmpty(html)) return items;

            try
            {
                int footerIdx = html.IndexOf("id=\"homeCardsFooter\"", StringComparison.OrdinalIgnoreCase);
                string searchArea = footerIdx >= 0 ? html.Substring(footerIdx, Math.Min(6000, html.Length - footerIdx)) : html;
                var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var matches = Regex.Matches(searchArea, @"<a\s+[^>]*href=""([^""]+)""\s+class=""miniCard__title"">\s*([^<]+)\s*</a>");
                foreach (Match m in matches)
                {
                    string url = m.Groups[1].Value.Trim();
                    string title = CleanTitle(m.Groups[2].Value);
                    if (IsValidPersianTitle(title) && !string.IsNullOrWhiteSpace(url))
                    {
                        if (seenUrls.Add(url))
                        {
                            items.Add(new NewsItem(title, url, "داغ‌ترین‌های روز", "دیجیاتو"));
                            if (items.Count >= 3) break;
                        }
                    }
                }

                if (items.Count == 0)
                {
                    var matches2 = Regex.Matches(searchArea, @"<a\s+[^>]*href=""([^""]+)""\s+class=""miniCard__picture""\s+aria-label=""([^""]+)""");
                    foreach (Match m in matches2)
                    {
                        string url = m.Groups[1].Value.Trim();
                        string title = CleanTitle(m.Groups[2].Value);
                        if (IsValidPersianTitle(title) && !string.IsNullOrWhiteSpace(url))
                        {
                            if (seenUrls.Add(url))
                            {
                                items.Add(new NewsItem(title, url, "داغ‌ترین‌های روز", "دیجیاتو"));
                                if (items.Count >= 3) break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.ParseDigiatoHotNews");
            }

            return items;
        }

        internal static List<NewsItem> ParseSportHeroNews(string html)
        {
            var items = new List<NewsItem>();
            if (string.IsNullOrEmpty(html)) return items;

            try
            {
                var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var matches = Regex.Matches(html, @"<a\s+[^>]*href=""(/sport/\d+/[^""]*)""[^>]*>([\s\S]*?)</a>");
                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        string href = match.Groups[1].Value.Trim();
                        string rawInner = match.Groups[2].Value;
                        string title = ExtractCleanNewsText(rawInner);

                        if (IsValidPersianTitle(title))
                        {
                            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                href = "https://akharinkhabar.ir" + href;

                            if (seenUrls.Add(href))
                            {
                                items.Add(new NewsItem(title, href, "هیرو ورزشی", "آخرین خبر"));
                                if (items.Count >= 3) break;
                            }
                        }
                    }
                }

                if (items.Count == 0)
                {
                    var match = Regex.Match(html, @"class=""[^""]*main-module[^""]*__title""[^>]*>\s*<a\s+href=""([^""]+)""[^>]*>([^<]+)</a>");
                    if (match.Success)
                    {
                        string href = match.Groups[1].Value.Trim();
                        string title = CleanTitle(match.Groups[2].Value);
                        if (IsValidPersianTitle(title))
                        {
                            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                href = "https://akharinkhabar.ir" + href;

                            items.Add(new NewsItem(title, href, "هیرو ورزشی", "آخرین خبر"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "NewsFeedService.ParseSportHeroNews");
            }

            return items;
        }

        private static string ExtractCleanNewsText(string innerHtml)
        {
            if (string.IsNullOrWhiteSpace(innerHtml)) return string.Empty;

            // ۱. جستجو در کلاس‌های مشخص عنوان
            var mTitle = Regex.Match(innerHtml, @"class=""[^""]*title[^""]*""[^>]*>([^<]+)<", RegexOptions.IgnoreCase);
            if (mTitle.Success && mTitle.Groups[1].Value.Trim().Length > 8)
            {
                return CleanTitle(mTitle.Groups[1].Value);
            }

            // ۲. تگ‌های سرتیتر h1-h5
            var mH = Regex.Match(innerHtml, @"<h[1-5][^>]*>([^<]+)</h[1-5]>", RegexOptions.IgnoreCase);
            if (mH.Success && mH.Groups[1].Value.Trim().Length > 8)
            {
                return CleanTitle(mH.Groups[1].Value);
            }

            // ۳. متن جایگزین تصویر (Alt)
            var mAlt = Regex.Match(innerHtml, @"<img[^>]+alt=""([^""]+)""", RegexOptions.IgnoreCase);
            if (mAlt.Success && mAlt.Groups[1].Value.Trim().Length > 8)
            {
                return CleanTitle(mAlt.Groups[1].Value);
            }

            // ۴. پاکسازی کامل تگ‌های HTML، کامنت‌ها و علامت‌های نگارشی تکراری
            string stripped = Regex.Replace(innerHtml, @"<!--.*?-->", string.Empty);
            stripped = Regex.Replace(stripped, @"<[^>]+>", " ");
            stripped = stripped.Replace("•", " ");
            return CleanTitle(stripped);
        }

        private static bool IsValidPersianTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            if (title.Length < 10) return false;
            if (title.StartsWith("مشاهده", StringComparison.OrdinalIgnoreCase)) return false;
            if (title.Equals("آخرین خبر", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static string CleanTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string decoded = WebUtility.HtmlDecode(raw).Trim();
            decoded = decoded.Replace("•", " ");
            return Regex.Replace(decoded, @"\s+", " ").Trim();
        }
    }
}
