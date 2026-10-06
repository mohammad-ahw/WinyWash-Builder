// src/CleanC.Core/Services/LocalizationService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CleanC.Core.Diagnostics;

namespace CleanC.Core.Services
{
    public sealed class LanguageInfo
    {
        public string Code { get; set; } = string.Empty;
        public string NativeName { get; set; } = string.Empty;
        public string FlagName { get; set; } = string.Empty;
        public bool IsRtl { get; set; }

        public override string ToString() => FlagName;
    }

    /// <summary>
    /// Multi-language localization service for CleanC.
    /// Supports dynamic resw resource files in Resources/Strings/Strings.{code}.resw.
    /// Automatically detects default language from Windows timezone and region.
    /// </summary>
    public static class LocalizationService
    {
        private static readonly Dictionary<string, Dictionary<string, string>> _catalogs = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<LanguageInfo> _languages = new();
        private static LanguageInfo _current = new() { Code = "fa-IR", NativeName = "فارسی", FlagName = "🇮🇷 فارسی", IsRtl = true };

        public static event Action? LanguageChanged;

        public static IReadOnlyList<LanguageInfo> SupportedLanguages => _languages;
        public static LanguageInfo CurrentLanguage => _current;
        public static bool IsRtl => _current.IsRtl;

        static LocalizationService()
        {
            RegisterDefaultLanguageMetadata();
            LoadCatalogs();
        }

        private static void RegisterDefaultLanguageMetadata()
        {
            _languages.Clear();
            _languages.AddRange(new[]
            {
                new LanguageInfo { Code = "fa-IR", NativeName = "فارسی", FlagName = "🇮🇷 فارسی", IsRtl = true },
                new LanguageInfo { Code = "en-US", NativeName = "English", FlagName = "🇬🇧 English", IsRtl = false },
                new LanguageInfo { Code = "ar-SA", NativeName = "العربية", FlagName = "🇸🇦 العربية", IsRtl = true },
                new LanguageInfo { Code = "zh-CN", NativeName = "简体中文", FlagName = "🇨🇳 简体中文", IsRtl = false },
                new LanguageInfo { Code = "ja-JP", NativeName = "日本語", FlagName = "🇯🇵 日本語", IsRtl = false },
                new LanguageInfo { Code = "ko-KR", NativeName = "한국어", FlagName = "🇰🇷 한국어", IsRtl = false },
                new LanguageInfo { Code = "ru-RU", NativeName = "Русский", FlagName = "🇷🇺 Русский", IsRtl = false },
                new LanguageInfo { Code = "de-DE", NativeName = "Deutsch", FlagName = "🇩🇪 Deutsch", IsRtl = false },
                new LanguageInfo { Code = "fr-FR", NativeName = "Français", FlagName = "🇫🇷 Français", IsRtl = false },
                new LanguageInfo { Code = "es-ES", NativeName = "Español", FlagName = "🇪🇸 Español", IsRtl = false }
            });
        }

        public static void LoadCatalogs(string? customDirectory = null)
        {
            try
            {
                var candidateDirs = new List<string>();
                if (!string.IsNullOrWhiteSpace(customDirectory) && Directory.Exists(customDirectory))
                {
                    candidateDirs.Add(customDirectory);
                }

                string baseDir = AppContext.BaseDirectory;
                candidateDirs.Add(Path.Combine(baseDir, "Resources", "Strings"));
                candidateDirs.Add(Path.Combine(baseDir, "Strings"));

                // Fallback for development/source tree
                try
                {
                    var parent = Directory.GetParent(baseDir);
                    while (parent != null && parent.Exists)
                    {
                        string test = Path.Combine(parent.FullName, "src", "CleanC.UI", "Resources", "Strings");
                        if (Directory.Exists(test))
                        {
                            candidateDirs.Add(test);
                            break;
                        }
                        parent = parent.Parent;
                    }
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex, "LocalizationService.SourcePathProbe");
                }

                foreach (var dir in candidateDirs.Where(Directory.Exists))
                {
                    foreach (var file in Directory.GetFiles(dir, "*.resw", SearchOption.AllDirectories))
                    {
                        ParseReswFile(file);
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "LocalizationService.LoadCatalogs");
            }
        }

        private static void ParseReswFile(string filePath)
        {
            try
            {
                string filename = Path.GetFileNameWithoutExtension(filePath); // Strings.en-US or Resources
                string langCode;
                if (filename.StartsWith("Strings.", StringComparison.OrdinalIgnoreCase))
                {
                    langCode = filename.Substring("Strings.".Length);
                    if (langCode.StartsWith("lang-", StringComparison.OrdinalIgnoreCase))
                        langCode = langCode.Substring("lang-".Length);
                }
                else
                {
                    langCode = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "";
                }

                if (string.IsNullOrWhiteSpace(langCode)) return;

                var doc = XDocument.Load(filePath);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var data in doc.Descendants("data"))
                {
                    string? name = data.Attribute("name")?.Value;
                    string? value = data.Element("value")?.Value;
                    if (!string.IsNullOrEmpty(name) && value != null)
                    {
                        dict[name] = value;
                    }
                }

                _catalogs[langCode] = dict;

                // If not in metadata list, register dynamically
                if (!_languages.Any(l => string.Equals(l.Code, langCode, StringComparison.OrdinalIgnoreCase)))
                {
                    bool rtl = langCode.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ||
                               langCode.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ||
                               langCode.StartsWith("he", StringComparison.OrdinalIgnoreCase) ||
                               langCode.StartsWith("ur", StringComparison.OrdinalIgnoreCase);

                    _languages.Add(new LanguageInfo
                    {
                        Code = langCode,
                        NativeName = langCode,
                        FlagName = $"🌐 {langCode}",
                        IsRtl = rtl
                    });
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, $"LocalizationService.ParseResw:{filePath}");
            }
        }

        public static string DetectDefaultLanguage()
        {
            try
            {
                // 1. Timezone detection (High confidence for local user)
                string tzId = TimeZoneInfo.Local.Id;
                if (tzId.Contains("Iran", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Tehran", StringComparison.OrdinalIgnoreCase))
                {
                    return "fa-IR";
                }
                if (tzId.Contains("Arab", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Riyadh", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Baghdad", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Cairo", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Kuwait", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Jordan", StringComparison.OrdinalIgnoreCase))
                {
                    return "ar-SA";
                }
                if (tzId.Contains("China", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Beijing", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Shanghai", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Taipei", StringComparison.OrdinalIgnoreCase))
                {
                    return "zh-CN";
                }
                if (tzId.Contains("Tokyo", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Japan", StringComparison.OrdinalIgnoreCase))
                {
                    return "ja-JP";
                }
                if (tzId.Contains("Seoul", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Korea", StringComparison.OrdinalIgnoreCase))
                {
                    return "ko-KR";
                }
                if (tzId.Contains("Russian", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Moscow", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Ekaterinburg", StringComparison.OrdinalIgnoreCase))
                {
                    return "ru-RU";
                }
                if (tzId.Contains("Berlin", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("W. Europe", StringComparison.OrdinalIgnoreCase))
                {
                    return "de-DE";
                }
                if (tzId.Contains("Paris", StringComparison.OrdinalIgnoreCase) ||
                    tzId.Contains("Romance", StringComparison.OrdinalIgnoreCase))
                {
                    return "fr-FR";
                }
                if (tzId.Contains("Madrid", StringComparison.OrdinalIgnoreCase))
                {
                    return "es-ES";
                }

                // 2. Region / Geo detection
                try
                {
                    string region = RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant();
                    switch (region)
                    {
                        case "IR": return "fa-IR";
                        case "SA":
                        case "AE":
                        case "EG":
                        case "IQ":
                        case "KW":
                        case "QA":
                        case "OM": return "ar-SA";
                        case "CN": return "zh-CN";
                        case "JP": return "ja-JP";
                        case "KR": return "ko-KR";
                        case "RU":
                        case "BY":
                        case "KZ": return "ru-RU";
                        case "DE":
                        case "AT":
                        case "CH": return "de-DE";
                        case "FR": return "fr-FR";
                        case "ES":
                        case "MX":
                        case "AR":
                        case "CO": return "es-ES";
                    }
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex, "LocalizationService.RegionProbe");
                }

                // 3. UI Culture detection
                string cultureName = CultureInfo.CurrentUICulture.Name;
                foreach (var lang in _languages)
                {
                    if (string.Equals(lang.Code, cultureName, StringComparison.OrdinalIgnoreCase) ||
                        cultureName.StartsWith(lang.Code.Split('-')[0], StringComparison.OrdinalIgnoreCase))
                    {
                        return lang.Code;
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "LocalizationService.DetectDefaultLanguage");
            }

            return "en-US";
        }

        public static void SetLanguage(string langCode)
        {
            if (string.IsNullOrWhiteSpace(langCode) || string.Equals(langCode, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                langCode = DetectDefaultLanguage();
            }

            var target = _languages.FirstOrDefault(l => string.Equals(l.Code, langCode, StringComparison.OrdinalIgnoreCase))
                         ?? _languages.FirstOrDefault(l => l.Code.StartsWith(langCode.Split('-')[0], StringComparison.OrdinalIgnoreCase))
                         ?? _languages.First();

            _current = target;

            try
            {
                var culture = (CultureInfo)CultureInfo.GetCultureInfo(target.Code).Clone();
                culture.NumberFormat.NumberDecimalSeparator = ".";
                culture.NumberFormat.PercentDecimalSeparator = ".";
                culture.NumberFormat.CurrencyDecimalSeparator = ".";
                culture.DateTimeFormat.DateSeparator = ".";
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
            }
            catch { }

            LanguageChanged?.Invoke();
        }

        public static string Get(string key, string? defaultVal = null)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;

            // Try current language
            if (_catalogs.TryGetValue(_current.Code, out var dict) && dict.TryGetValue(key, out var val))
            {
                return val;
            }

            // Fallback to English
            if (!string.Equals(_current.Code, "en-US", StringComparison.OrdinalIgnoreCase) &&
                _catalogs.TryGetValue("en-US", out var enDict) && enDict.TryGetValue(key, out var enVal))
            {
                return enVal;
            }

            // Fallback to Persian
            if (!string.Equals(_current.Code, "fa-IR", StringComparison.OrdinalIgnoreCase) &&
                _catalogs.TryGetValue("fa-IR", out var faDict) && faDict.TryGetValue(key, out var faVal))
            {
                return faVal;
            }

            return defaultVal ?? key;
        }

        public static string GetFontFamilyName()
        {
            return _current.Code switch
            {
                "zh-CN" => "Microsoft YaHei, Segoe UI",
                "ja-JP" => "Yu Gothic UI, Meiryo, Segoe UI",
                "ko-KR" => "Malgun Gothic, Segoe UI",
                "ar-SA" => "Segoe UI, Tahoma",
                "fa-IR" => "Segoe UI, Tahoma",
                _ => "Segoe UI Variable, Segoe UI"
            };
        }
    }
}
