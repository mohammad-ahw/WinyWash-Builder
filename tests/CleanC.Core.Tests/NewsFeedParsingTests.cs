using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace CleanC.Core.Tests
{
    public class NewsFeedParsingTests
    {
        [Fact]
        public void AkharinKhabarView_ParsesHeadlines_Successfully()
        {
            string sampleHtml = @"
                <a href=""/social/11064342/red-alert-weather"" class=""card"">
                    <div class=""simple-card-module__GpOBiG__image_wrapper""><img src=""pic.jpg"" alt=""alt title""/></div>
                    <div class=""simple-card-module__GpOBiG__title"">هشدار سطح قرمز هواشناسی در 5 استان</div>
                    <time>14 ساعت پیش</time>
                </a>
                <a href=""/money/11064177/fuel-card"" class=""card"">
                    اطلاعیه شرکت ملی پخش فرآورده‌های نفتی<!-- --> •
                </a>";

            var items = ParseAkharinKhabarLinks(sampleHtml, "اخبار برگزیده");
            Assert.Equal(2, items.Count);
            Assert.Equal("هشدار سطح قرمز هواشناسی در 5 استان", items[0].Title);
            Assert.Equal("اطلاعیه شرکت ملی پخش فرآورده‌های نفتی", items[1].Title);
        }

        [Fact]
        public void ZoomitAi_ParsesHero_Successfully()
        {
            string sampleHtml = @"
                <a href=""/ai-articles/470441-people-using-ai-divorce-advice-featured/"">
                    <div></div><span class=""typography__StyledDynamicTypographyComponent-sc-c97798ae-0 gaYFrG text-h2"">مردم برای طلاق از هوش مصنوعی مشاوره می‌گیرند</span>
                </a>
                <a href=""/ai-articles/470441-people-using-ai-divorce-advice-featured/"">
                    مشاهده مطالب
                </a>";

            var items = ParseZoomitAiNews(sampleHtml);
            Assert.Single(items);
            Assert.Equal("مردم برای طلاق از هوش مصنوعی مشاوره می‌گیرند", items[0].Title);
        }

        [Fact]
        public void DigiatoHot_ParsesMiniCard_Successfully()
        {
            string sampleHtml = @"
                <div id=""homeCardsFooter"">
                    <a href=""https://digiato.com/ai/anthropic"" class=""miniCard__title"">
                        مدل معرفی‌نشده آنتروپیک معمای رایانش کوانتومی را حل کرد
                    </a>
                </div>";

            var items = ParseDigiatoHotNews(sampleHtml);
            Assert.Single(items);
            Assert.Equal("مدل معرفی‌نشده آنتروپیک معمای رایانش کوانتومی را حل کرد", items[0].Title);
        }

        // --- Mirror parsers to test logic ---
        private static List<TestNewsItem> ParseAkharinKhabarLinks(string html, string category)
        {
            var items = new List<TestNewsItem>();
            var matches = Regex.Matches(html, @"<a\s+[^>]*href=""(/[^""]+/\d+/[^""]*)""[^>]*>([\s\S]*?)</a>");
            foreach (Match match in matches)
            {
                string href = match.Groups[1].Value.Trim();
                string title = ExtractCleanNewsText(match.Groups[2].Value);
                if (IsValidPersianTitle(title))
                {
                    items.Add(new TestNewsItem(title, "https://akharinkhabar.ir" + href, category, "آخرین خبر"));
                }
            }
            return items;
        }

        private static List<TestNewsItem> ParseZoomitAiNews(string html)
        {
            var items = new List<TestNewsItem>();
            var matches = Regex.Matches(html, @"<a\s+[^>]*href=""([^""]*ai-articles/\d+-[^""]*)""[^>]*>([\s\S]*?)</a>");
            foreach (Match match in matches)
            {
                string href = match.Groups[1].Value.Trim();
                string title = ExtractCleanNewsText(match.Groups[2].Value);
                if (IsValidPersianTitle(title))
                {
                    items.Add(new TestNewsItem(title, "https://www.zoomit.ir" + href, "هوش مصنوعی", "زومیت"));
                }
            }
            return items;
        }

        private static List<TestNewsItem> ParseDigiatoHotNews(string html)
        {
            var items = new List<TestNewsItem>();
            int footerIdx = html.IndexOf("id=\"homeCardsFooter\"", StringComparison.OrdinalIgnoreCase);
            string searchArea = footerIdx >= 0 ? html.Substring(footerIdx) : html;
            var matches = Regex.Matches(searchArea, @"<a\s+[^>]*href=""([^""]+)""\s+class=""miniCard__title"">\s*([^<]+)\s*</a>");
            foreach (Match m in matches)
            {
                string url = m.Groups[1].Value.Trim();
                string title = CleanTitle(m.Groups[2].Value);
                if (IsValidPersianTitle(title))
                {
                    items.Add(new TestNewsItem(title, url, "داغ‌ترین‌های روز", "دیجیاتو"));
                }
            }
            return items;
        }

        private static string ExtractCleanNewsText(string innerHtml)
        {
            if (string.IsNullOrWhiteSpace(innerHtml)) return string.Empty;
            var mTitle = Regex.Match(innerHtml, @"class=""[^""]*title[^""]*""[^>]*>([^<]+)<", RegexOptions.IgnoreCase);
            if (mTitle.Success && mTitle.Groups[1].Value.Trim().Length > 8)
                return CleanTitle(mTitle.Groups[1].Value);

            var mH = Regex.Match(innerHtml, @"<h[1-5][^>]*>([^<]+)</h[1-5]>", RegexOptions.IgnoreCase);
            if (mH.Success && mH.Groups[1].Value.Trim().Length > 8)
                return CleanTitle(mH.Groups[1].Value);

            var mAlt = Regex.Match(innerHtml, @"<img[^>]+alt=""([^""]+)""", RegexOptions.IgnoreCase);
            if (mAlt.Success && mAlt.Groups[1].Value.Trim().Length > 8)
                return CleanTitle(mAlt.Groups[1].Value);

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

        private record TestNewsItem(string Title, string Url, string Category, string Source);
    }
}
