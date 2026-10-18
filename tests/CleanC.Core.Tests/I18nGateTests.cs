// tests/CleanC.Core.Tests/I18nGateTests.cs
// گیت های i18n — این تست ها تضمین می کنند که:
// ۱) هیچ متن فارسی خامی خارج از LocalizationService.Get به کد برنگردد
// ۲) هر کلیدِ فارسی که در Get استفاده شده، ترجمه ی انگلیسی (resw) داشته باشد
// ۳) همه ی فایل های زبان، کلیدهای identifier انگلیسی را کامل داشته باشند
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CleanC.Core.Tests
{
    public class I18nGateTests
    {
        // ریشه ی ریپو از روی CleanC.sln پیدا می شود (تست ها از پوشه ی bin اجرا می شوند)
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "CleanC.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("CleanC.sln در والدهای مسیر تست پیدا نشد: " + AppContext.BaseDirectory);
        }

        private static string StringsRoot => Path.Combine(FindRepoRoot(), "src", "CleanC.UI", "Resources", "Strings");

        private static IEnumerable<string> EnumerateSourceFiles()
        {
            string root = Path.Combine(FindRepoRoot(), "src");
            char sep = Path.DirectorySeparatorChar;
            return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}obj{sep}"))
                .Where(f => Path.GetFileName(f) != "LocalizationService.cs");
        }

        private static bool ContainsPersianChars(string text)
        {
            foreach (char c in text)
                if (c >= '\u0600' && c <= '\u06FF') return true;
            return false;
        }

        // حذف بخش های کامنتی خط (بلوکی تک خطی و // تا آخر خط) تا فقط «بخش کد» بررسی شود
        private static string StripComments(string codePart, ref bool inBlockComment)
        {
            if (inBlockComment)
            {
                int end = codePart.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0) return string.Empty;
                codePart = codePart.Substring(end + 2);
                inBlockComment = false;
            }

            while (true)
            {
                int start = codePart.IndexOf("/*", StringComparison.Ordinal);
                if (start < 0) break;
                int end = codePart.IndexOf("*/", start + 2, StringComparison.Ordinal);
                if (end < 0) { codePart = codePart.Substring(0, start); inBlockComment = true; break; }
                codePart = codePart.Remove(start, end + 2 - start);
            }

            int lineComment = codePart.IndexOf("//", StringComparison.Ordinal);
            if (lineComment >= 0) codePart = codePart.Substring(0, lineComment);

            return codePart;
        }

        [Fact]
        public void NoBarePersianLiterals_OutsideGetCalls()
        {
            var offenders = new List<string>();

            foreach (var file in EnumerateSourceFiles())
            {
                var lines = File.ReadAllLines(file, Encoding.UTF8);
                bool inBlockComment = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string codePart = StripComments(lines[i], ref inBlockComment);

                    if (!ContainsPersianChars(codePart)) continue;
                    if (codePart.Contains("LocalizationService.Get")) continue;

                    // خطوط منطقی/چندخطی با مارکر i18n مجاز هستند (تا ۱۲ خط قبل)
                    bool marked = false;
                    for (int j = Math.Max(0, i - 12); j <= i; j++)
                    {
                        if (lines[j].Contains("// i18n")) { marked = true; break; }
                    }
                    if (marked) continue;

                    offenders.Add($"{Path.GetRelativePath(FindRepoRoot(), file)}:{i + 1}: {lines[i].Trim()}");
                }
            }

            Assert.True(offenders.Count == 0,
                $"متن فارسی خام خارج از Get پیدا شد ({offenders.Count} مورد):\n" +
                string.Join("\n", offenders.Take(40)));
        }

        private static readonly Regex GetCallRegex =
            new("LocalizationService\\.Get\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

        // تبدیل escape های C# به متن واقعی، تا با name موجود در resw (XML-decoded) یکی شود
        private static string UnescapeCSharpString(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '\\' => '\\',
                        '"' => '"',
                        '\'' => '\'',
                        '0' => '\0',
                        _ => n
                    });
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static HashSet<string> LoadReswKeys(string path)
        {
            var doc = XDocument.Load(path);
            return doc.Descendants("data")
                .Select(d => d.Attribute("name")?.Value)
                .Where(k => !string.IsNullOrEmpty(k))
                .Select(k => k!)
                .ToHashSet(StringComparer.Ordinal);
        }

        [Fact]
        public void AllPersianKeysUsedInGet_HaveEnglishTranslationEntry()
        {
            var englishKeys = LoadReswKeys(Path.Combine(StringsRoot, "en-US", "Resources.resw"));
            var missing = new List<string>();

            foreach (var file in EnumerateSourceFiles())
            {
                var lines = File.ReadAllLines(file, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in GetCallRegex.Matches(lines[i]))
                    {
                        string key = UnescapeCSharpString(m.Groups[1].Value);
                        if (!ContainsPersianChars(key)) continue;
                        if (!englishKeys.Contains(key))
                        {
                            missing.Add($"{Path.GetRelativePath(FindRepoRoot(), file)}:{i + 1}: {key.Replace("\n", "\\n")}");
                        }
                    }
                }
            }

            Assert.True(missing.Count == 0,
                $"کلیدهای فارسیِ بدون ترجمه ی انگلیسی ({missing.Count} مورد):\n" +
                string.Join("\n", missing.Distinct().Take(40)));
        }

        [Fact]
        public void AllLanguageFiles_ContainEveryNonPersianKeyFromEnglish()
        {
            var englishKeys = LoadReswKeys(Path.Combine(StringsRoot, "en-US", "Resources.resw"));
            var identifierKeys = englishKeys.Where(k => !ContainsPersianChars(k)).ToList();
            var problems = new List<string>();

            foreach (var langDir in Directory.GetDirectories(StringsRoot))
            {
                string lang = Path.GetFileName(langDir);
                if (lang == "en-US") continue;

                string reswPath = Path.Combine(langDir, "Resources.resw");
                if (!File.Exists(reswPath)) { problems.Add($"{lang}: Resources.resw وجود ندارد"); continue; }

                var langKeys = LoadReswKeys(reswPath);
                foreach (var key in identifierKeys)
                {
                    if (!langKeys.Contains(key))
                        problems.Add($"{lang}: کلید «{key}» غایب است");
                }
            }

            Assert.True(problems.Count == 0,
                $"کمبود کلید در فایل های زبان ({problems.Count} مورد):\n" +
                string.Join("\n", problems.Take(40)));
        }
    }
}
