// src/CleanC.UI/MainWindow.Hero.cs
// ارکستریتور ۱۴ مرحله‌ای بهینه‌سازی + پل WebView2 هیروکارت.
//
// این فایل دو کار می‌کند:
//   ۱) مسیر اجرای مراحل را نگه می‌دارد — هم دکمهٔ هیرو/ترِی و هم WebView2
//      (HeroHostApi.run.step) از همین متدها استفاده می‌کنند تا منطق دو نسخه نشود.
//   ۲) پیام‌های CleanC.hero.html را می‌گیرد و به تنظیمات/تم/آمار زنده ترجمه می‌کند.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;
using CleanC.Core.Services;
using CleanC.UI.Hero;
using CleanC.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace CleanC.UI
{
    public sealed partial class MainWindow
    {
        // ====================================================================
        // State
        // ====================================================================

        private HeroHost? _heroHost;
        private HeroHostApi? _heroApi;
        private HeroRunSession? _heroSession;

        /// <summary>لغوی واقعی: مراحل در جریانِ اجرا این توکن را می‌بینند و سریع می‌ایستند.</summary>
        private CancellationTokenSource? _heroCts;

        /// <summary>پس از لغو، فراخوانی مرحلهٔ بعدی نباید جلسهٔ تازه بسازد و ادامه دهد.</summary>
        private bool _heroAborted;

        /// <summary>کاندیداهای مرزیِ همین اجرا؛ ایندکس‌هایی که JS می‌فرستد به همین لیست می‌خورد.</summary>
        private List<CandidateAppItem> _heroBorderline = new();

        // سنسورهایی که سمت میزبان API نداریم و باید جدا محاسبه شوند
        private string _heroTopRam = "";
        private string _heroTopCpu = "";
        private long _heroBrowserMb;
        private DateTime _heroSensorAt = DateTime.MinValue;
        private DateTime _heroCpuAt = DateTime.MinValue;
        private DateTime _heroProxyAt = DateTime.MinValue;
        private bool _heroProxyDead;
        private DateTime _heroBrowserAlertSuppressedUntil = DateTime.MinValue;
        private DateTime _heroSnoozedUntil = DateTime.MinValue;
        private double _heroLastRamPercent = 0.0;
        private DateTime _heroRamAlertSuppressedUntil = DateTime.MinValue;
        private DateTime _heroProxyAlertSuppressedUntil = DateTime.MinValue;
        private DateTime _heroCpuAlertSuppressedUntil = DateTime.MinValue;
        private bool _heroBorderlineDismissed;

        /// <summary>آخرین JSON فرستاده‌شده — برای جلوگیری از ارسال بی‌معنی در هر تیک تایمر.</summary>
        private string? _heroLastStateJson;

        /// <summary>
        /// آیا تا به حال یک اسکن واقعی داشته‌ایم؟ وقتی false است یعنی تازه استارت شده‌ایم و
        /// هنوز چیزی روی دیسک نگشته‌ایم — در این حالت آیتم‌های پیش‌فرض را به هیرو می‌دهیم
        /// تا دکمهٔ اصلی «بهینه‌سازی سیستم» باشد، نه «بررسی مجدد».
        /// </summary>
        private bool _heroScanSeen;

        /// <summary>قفلِ اسکنِ در جریان — استارت‌آپ، «بررسی مجدد» و مرحلهٔ ۱ پروسه نباید همزمان سه اسکن بگیرند.</summary>
        private readonly object _heroScanLock = new();

        /// <summary>اسکنِ در جریان (یا همین‌حالا تمام‌شده). null یعنی هنوز اسکنی شروع نشده است.</summary>
        private Task<ScanResult>? _heroScanInFlight;

        /// <summary>
        /// اسکن سیستم با ضمانتِ «حداکثر یکی در هر لحظه»: اگر اسکنی در جریان باشد همان را
        /// برمی‌گرداند. این باعث می‌شود اسکنِ پس‌زمینهٔ استارت‌آپ و دکمهٔ «بررسی مجدد»
        /// و مرحلهٔ ۱ پروسه همزمان چند بار کل دیسک را نگردند (کندیِ چند دقیقه‌ای).
        /// </summary>
        private Task<ScanResult> GetHeroScanAsync()
        {
            lock (_heroScanLock)
            {
                var running = _heroScanInFlight;
                if (running != null && !running.IsCompleted) return running;

                _heroScanLanguageVersion = _uiLanguageVersion;
                var next = SystemScanService.ScanAsync();
                _heroScanInFlight = next;
                return next;
            }
        }

        /// <summary>
        /// نتیجهٔ یک اسکن را به‌عنوان اسکنِ فعلی می‌پذیرد، تیک‌های ذخیره‌شدهٔ کاربر را اعمال می‌کند،
        /// کنترل‌های وابسته را تازه می‌کند و state هیرو را می‌فرستد.
        /// فقط روی UI thread صدا زده شود.
        /// </summary>
        internal void ApplyHeroScanResult(ScanResult? scan, bool renderAdvanced = true)
        {
            if (scan == null || _isExiting) return;

            _cachedScanResult = scan;
            _heroScanSeen = true;
            ApplyPersistedJunkSelections(scan);
            if (renderAdvanced) RenderAdvancedGroups(scan);
            AdvancedCleanTotalText.Text =
                $"{LocalizationService.Get("TotalDetected", "مجموع شناسایی شده")}: {scan.FormattedTotal}";
            UpdateHeroCleanable(scan.TotalBytes);
            _heroLastStateJson = null;
            PushHeroState(force: true);
        }

        // ====================================================================
        // Wiring
        // ====================================================================

        /// <summary>کنترل هیرو را به پل پیام وصل می‌کند. بعد از InitializeComponent صدا زده می‌شود.</summary>
        internal void InitHeroHost()
        {
            _heroHost ??= HeroHostXaml;
            if (_heroHost == null || _heroApi != null) return;

            _heroApi = new HeroHostApi(this);
            _heroHost.MessageReceived += OnHeroMessage;
            _heroHost.Ready += (_, _) =>
            {
                // صفحهٔ HTML آماده است: اول تنظیمات و تم را بفرست تا قبل از اولین رندر اعمال شوند
                PushHeroSettings();
                PushHeroTheme();
                PushHeroState(force: true);
            };

            SafeInvoker.Run(InitHeroHostAsync());
        }

        private async Task InitHeroHostAsync()
        {
            try
            {
                await _heroHost!.InitializeAsync();
            }
            catch (Exception ex)
            {
                // اگر WebView2 Runtime نصب نباشد، برنامه باید بدون هیرو هم کار کند
                CcLog.Report("Hero.HostInit", ex);
            }
        }

        private async void OnHeroMessage(object? sender, HeroMessage msg)
        {
            try
            {
                if (_heroApi != null) await _heroApi.DispatchAsync(msg);
            }
            catch (Exception ex)
            {
                CcLog.Report("Hero.Dispatch." + msg.Type, ex);
            }
        }

        /// <summary>پیام خام به JS؛ از همهٔ متدهای Push* و پاسخ‌های RPC استفاده می‌شود.</summary>
        internal void SendToHero(string json) => _heroHost?.Send(json);

        // ====================================================================
        // Outbound: theme / settings / state
        // ====================================================================

        internal void PushHeroTheme()
        {
            var mode = _settings.Theme?.ToLowerInvariant() switch
            {
                "dark" => "dark",
                "light" => "light",
                _ => "system"
            };

            SendToHero(HeroOutbound.Theme(new HeroThemeMessage
            {
                Mode = mode,
                Accent = GetWindowsAccentHex()
            }));
        }

        /// <summary>
        /// رنگ Accent ویندوز را از UISettings می‌گیرد. اگر سیستم رنگ خاموشی (مونو) داشته باشد
        /// یا دسترسی نباشد، رنگ پیش‌فرض برنامه برمی‌گردد تا هیرو همیشه یک هالهٔ معتبر بگیرد.
        /// </summary>
        internal string GetWindowsAccentHex()
        {
            try
            {
                var c = _uiSettings.GetColorValue(UIColorType.Accent);
                if (c.A < 16) return "#1fa971";
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Hero.Accent");
                return "#1fa971";
            }
        }

        internal void PushHeroSettings()
        {
            var s = _settings;
            SendToHero(HeroOutbound.Settings(new HeroCharacterSettings
            {
                Tone = s.CharacterTone,
                // اسم کاربر: اگر دستی تنظیم نشده باشد، پیش فرض «رفیق» با زبان فعلی
                // ترجمه و به هیرو داده می شود؛ مقدار دستی کاربر هرگز بازنویسی نمی شود.
                UserName = string.IsNullOrWhiteSpace(s.CharacterUserName)
                    ? LocalizationService.Get("رفیق")
                    : s.CharacterUserName.Trim(),
                CharacterName = s.CharacterName,
                City = s.CharacterCity,
                Calm = s.CharacterCalm,
                Fast = s.CharacterFast,
                Intensity = s.CharacterIntensity,
                Bubble = s.CharacterBubble,
                Env = s.CharacterEnv,
                Sound = s.CharacterSound,
                Look = s.CharacterLook,
                OccLook = s.CharacterOccLook,
                Size = s.CharacterSize,
                Lun = s.CharacterLun,
                Lang = LocalizationService.CurrentLanguage.Code
            }));
        }

        internal void PushHeroState(bool force = false)
        {
            if (_heroHost == null) return;

            RefreshHeroSensors();

            var stats = SystemStatsService.GetCurrentStats();
            var power = PowerPlanService.GetPowerStatus();

            double totalRam = stats.TotalRamBytes > 0 ? stats.TotalRamBytes : 1;
            double ramPercent = stats.UsedRamBytes * 100.0 / totalRam;
            _heroLastRamPercent = ramPercent;
            double cFreePercent = stats.DiskTotalBytes > 0
                ? stats.DiskFreeBytes * 100.0 / stats.DiskTotalBytes
                : 0;

            var state = new HeroState
            {
                Cpu = Math.Clamp(stats.CpuPercent, 0, 100),
                Ram = Math.Clamp(ramPercent, 0, 100),
                CFree = Math.Clamp(cFreePercent, 0, 100),
                Batt = power.BatteryPercent,
                Charging = power.IsOnAcPower,
                ProxyDead = _heroProxyDead,
                UptimeDays = (int)(Environment.TickCount64 / 86_400_000L),
                Offline = !HasActiveNetwork(),
                BrowserMb = _heroBrowserMb,
                TopRam = _heroTopRam,
                TopCpu = _heroTopCpu,
                DaysSince = _lastCleanDateTime == DateTime.MinValue
                    ? null
                    : (int)(DateTime.Now - _lastCleanDateTime).TotalDays,
                Items = BuildHeroItems(),
                Sizes = BuildHeroSizes(),
                Chips = new List<string[]>(),
                TotalFreedGb = _cumulativeFreedBytes / (1024.0 * 1024.0 * 1024.0),
                LastClean = _lastCleanDateTime == DateTime.MinValue || _lastCleanFreedBytes <= 0
                    ? null
                    : string.Format(LocalizationService.Get("{0}، {1:HH:mm}"), SystemStats.FormatBytes(_lastCleanFreedBytes), _lastCleanDateTime)
            };

            var json = HeroOutbound.State(state);
            if (!force && json == _heroLastStateJson) return;
            _heroLastStateJson = json;
            SendToHero(json);
        }

        /// <summary>
        /// سنسورهایی که سمت میزبان API نداریم: پرمصرف‌ترین پردازه از نظر رم، حجم کل مرورگرها،
        /// پراکسی شبح. هر ۵ ثانیه — enumeration پروسه‌ها گران است و نباید در تایمر ۲.۵ ثانیه‌ای بیفتد.
        /// </summary>
        private void RefreshHeroSensors()
        {
            if ((DateTime.UtcNow - _heroSensorAt).TotalSeconds < 5) return;
            _heroSensorAt = DateTime.UtcNow;

            try
            {
                _heroTopRam = "";
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id <= 4 || p.HasExited) continue;
                        if (p.WorkingSet64 > 400L * 1024 * 1024)
                        {
                            var name = p.ProcessName;
                            if (!_heroTopRam.Contains(name, StringComparison.OrdinalIgnoreCase))
                                _heroTopRam = string.IsNullOrEmpty(_heroTopRam) ? name : $"{name} · {_heroTopRam}";
                        }
                    }
                    catch { /* پروسه بین نمونه‌گیری بسته شده */ }
                    finally { p.Dispose(); }
                }
                if (_heroTopRam.Length > 40) _heroTopRam = _heroTopRam[..40] + "…";

                if (DateTime.UtcNow < _heroBrowserAlertSuppressedUntil)
                {
                    _heroBrowserMb = 0;
                }
                else
                {
                    long browserBytes = 0;
                    foreach (var name in new[] { "chrome", "msedge", "brave", "opera" })
                    {
                        foreach (var p in Process.GetProcessesByName(name))
                        {
                            try { browserBytes += p.WorkingSet64; } catch { }
                            finally { p.Dispose(); }
                        }
                    }
                    _heroBrowserMb = browserBytes / (1024 * 1024);
                }

                // پراکسی شبح ثابت نیست؛ کاربر ممکن است هر لحظه آن را درست یا خراب کند
                if (DateTime.UtcNow < _heroProxyAlertSuppressedUntil)
                {
                    _heroProxyDead = false;
                }
                else if ((DateTime.UtcNow - _heroProxyAt).TotalSeconds >= 15)
                {
                    _heroProxyAt = DateTime.UtcNow;
                    _heroProxyDead = ReadGhostProxyConflict();
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Hero.Sensors");
            }

            if (DateTime.UtcNow < _heroCpuAlertSuppressedUntil)
            {
                _heroTopCpu = "";
            }
            else if ((DateTime.UtcNow - _heroCpuAt).TotalSeconds >= 20)
            {
                _heroCpuAt = DateTime.UtcNow;
                SafeInvoker.Run(SampleTopCpuAsync(), "Hero.TopCpu");
            }
        }

        /// <summary>پروسهٔ پرمصرف CPU: دو نمونه‌گیری از TotalProcessorTime با فاصلهٔ ۴۰۰ms.</summary>
        private async Task SampleTopCpuAsync()
        {
            try
            {
                var procs = new List<Process>();
                var first = new Dictionary<int, TimeSpan>();
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id <= 4 || p.HasExited) continue;
                        first[p.Id] = p.TotalProcessorTime;
                        procs.Add(p);
                    }
                    catch { p.Dispose(); }
                }

                await Task.Delay(400);

                string best = "";
                double bestPct = 0;
                int cores = Math.Max(1, Environment.ProcessorCount);
                foreach (var p in procs)
                {
                    try
                    {
                        if (!p.HasExited && first.TryGetValue(p.Id, out var t0))
                        {
                            double pct = (p.TotalProcessorTime - t0).TotalMilliseconds / 400.0 / cores * 100.0;
                            if (pct > bestPct) { bestPct = pct; best = p.ProcessName; }
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }

                _heroTopCpu = bestPct >= 8 ? best : "";
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Hero.TopCpu");
            }
        }

        /// <summary>آیا پراکسی شبح (ghost proxy) دارد؟ — آیتم بحرانی «اینترنت» در هیرو.</summary>
        private static bool ReadGhostProxyConflict()
        {
            try { return NetworkProxyDiagnosticService.DetectGhostProxyConflict().HasConflict; }
            catch { return false; }
        }

        /// <summary>آداپتور شبکه‌ای بالا هست؟ — نزدیک‌ترین معیار آنلاین بودن بدون پینگ.</summary>
        private static bool HasActiveNetwork()
        {
            try { return DnsManagerService.GetActiveAdapterNames().Count > 0; }
            catch { return false; }
        }

        /// <summary>
        /// آیتم‌های بهینه‌سازی که بدون اسکن هم قابل اجرا هستند (همان‌هایی که گروه system_boost
        /// در نبودِ خودِ اسکن هم تیک‌شان می‌خورد). فقط برای پر کردن state هیرو تا اولین اسکن.
        /// </summary>
        private static readonly string[] DefaultHeroBoostItems =
        {
            "dns", "browser", "reg", "startup", "tweak", "fast", "gpu", "debloat"
        };

        /// <summary>کلیدهای IT که فعلاً مشکل‌دارند — سمت JS با فهرست مسائل جفت می‌شوند.</summary>
        private List<string> BuildHeroItems()
        {
            var items = new List<string>();
            var scan = _cachedScanResult;

            bool cleanedRecently = _isCleanedRecently ||
                (_lastCleanDateTime != DateTime.MinValue && (DateTime.Now - _lastCleanDateTime).TotalHours < 24);

            // اگر کاربر کلید «بعداً» را زده، هیرو تا پایان مهلت در آرامش کامل می‌ماند
            if (DateTime.UtcNow < _heroSnoozedUntil)
            {
                return items;
            }

            if (scan == null)
            {
                // برنامه در حال بالا آمدن است؛ تا زمان تکمیل اولین اسکن، هیرو آرام و پایدار می‌ماند
                return items;
            }

            long junkBytes = scan.Groups
                .Where(g => g.Id != "system_boost")
                .SelectMany(g => g.Items)
                .Where(i => i.IsChecked)
                .Sum(i => i.Bytes);

            long junkThreshold = cleanedRecently ? 1024L * 1024 * 1024 : 500L * 1024 * 1024;
            if (junkBytes > junkThreshold) items.Add("junk");

            // رجیستری یتیم فقط در صورتی اضافه می‌شود که واقعاً کلید معلقی در اسکن باشد
            var regItem = scan.Groups
                .SelectMany(g => g.Items)
                .FirstOrDefault(i => i.Id == "orphanedRegistry" && i.IsChecked);
            if (!cleanedRecently && regItem != null && regItem.Bytes > 0)
            {
                items.Add("reg");
            }

            // مرورگر فقط اگر مصرف غیرعادی (بالای ۳ گیگابایت) داشته باشد یا مصرف رم کلی سیستم بالای ۸۰ درصد باشد
            bool browserHeavy = _heroBrowserMb >= 3000 || (_heroBrowserMb >= 1800 && _heroLastRamPercent >= 80);
            if (browserHeavy && DateTime.UtcNow >= _heroBrowserAlertSuppressedUntil)
            {
                items.Add("browser");
            }

            // سایر گزینه‌های بهینه‌سازی عمومی: فقط در صورتی که اخیراً پاکسازی نشده باشد
            if (!cleanedRecently)
            {
                bool BoostOn(string id)
                {
                    var group = scan.Groups.FirstOrDefault(g => g.Id == "system_boost");
                    if (group == null) return false;
                    var item = group.Items.FirstOrDefault(i => i.Id == id);
                    return item != null && item.IsChecked;
                }

                if (BoostOn("dnsFlush")) items.Add("dns");
                if (BoostOn("startupOptimize")) items.Add("startup");
                if (BoostOn("winTweaks")) items.Add("tweak");
                if (BoostOn("fastStartup")) items.Add("fast");
                if (BoostOn("gpuOptimize")) items.Add("gpu");
                if (BoostOn("bloatwareClean")) items.Add("debloat");
            }

            return items;
        }

        /// <summary>
        /// سایز واقعی هر کلیدِ <c>IT</c> به بایت — سمت JS با <c>syncSizes</c> روی جزئیات و
        /// «قابل پاکسازی …» اعمال می‌شود. آیتم‌هایی که هسته عدد حجمی ندارند (dns، توئیک‌ها،
        /// استارتاپ، …) صفر می‌فرستیم تا اعداد demoی hardcode‌شدهٔ HTML جایگزین نشوند.
        /// </summary>
        private Dictionary<string, long> BuildHeroSizes()
        {
            var sizes = new Dictionary<string, long>
            {
                ["junk"] = 0,
                ["dns"] = 0,
                ["browser"] = 0,
                ["reg"] = 0,
                ["startup"] = 0,
                ["tweak"] = 0,
                ["fast"] = 0,
                ["gpu"] = 0,
                ["debloat"] = 0
            };

            // کش/حجم مرورگر از اندازه‌گیری زندهٔ میزبان می‌آید (مگابایت → بایت)
            sizes["browser"] = Math.Max(0, _heroBrowserMb) * 1024L * 1024L;

            var scan = _cachedScanResult;
            if (scan == null) return sizes;

            // زباله‌ها: همهٔ گروه‌های غیر system_boost که تیک‌شان فعال است.
            // orphanedRegistry عمداً کنار گذاشته می‌شود چون هم داخل junk و هم زیر «reg» می‌آید
            // و دوباره شمردنش مجموع را کمی بالا می‌برد.
            sizes["junk"] = scan.Groups
                .Where(g => g.Id != "system_boost")
                .SelectMany(g => g.Items)
                .Where(i => i.IsChecked && i.Id != "orphanedRegistry")
                .Sum(i => i.Bytes);

            // رجیستری یتیم: مقدار آیتم به بایت است (تعداد ورودی × 1024)
            var regItem = scan.Groups
                .SelectMany(g => g.Items)
                .FirstOrDefault(i => i.Id == "orphanedRegistry" && i.IsChecked);
            if (regItem != null) sizes["reg"] = Math.Max(0, regItem.Bytes);

            return sizes;
        }

        // ====================================================================
        // Run session + the 14 steps
        // ====================================================================

        /// <summary>
        /// وضعیت یک اجرای بهینه‌سازی. شمارنده‌ها و گزارش اینجا جمع می‌شوند تا
        /// فراخواننده (دکمه یا WebView2) فقط ترتیب مراحل را تعیین کند.
        /// </summary>
        private sealed class HeroRunSession
        {
            public ScanResult? Scan { get; set; }
            public List<CleanupReportGroup> ReportGroups { get; } = new();
            public List<CleanupReportItem> FailedSteps { get; } = new();
            public List<AttentionDecisionItem> Attention { get; set; } = new();
            public long TotalFreed { get; set; }

            public long RamFreed { get; set; }
            public long ExtFreed { get; set; }
            public int RegCleaned { get; set; }
            public int LeftoverFolders { get; set; }
            public long LeftoverFreed { get; set; }

            /// <summary>حجمی که فقط به سطل/قرنطینه جابه جا شد (آزاد نشده، قابل بازگردانی).</summary>
            public long LeftoverMoved { get; set; }

            /// <summary>تعداد پوشه های رهاشده ای که واقعاً حذف/جابه جا شدند.</summary>
            public int LeftoverDeleted { get; set; }
            public int DisabledStartups { get; set; }
            public int WinTweaks { get; set; }
            public int GpuCount { get; set; }
            public string GpuName { get; set; } = "";
            public int BloatRemoved { get; set; }
            public long BloatFreed { get; set; }
            public int BorderlineRemoved { get; set; }
            public long BorderlineFreed { get; set; }
            public CleanupReportItem? FastStartup { get; set; }

            /// <summary>آیا آن boost در اسکن تیک خورده؟ (منطقِ قدیمیِ IsBoostEnabled)</summary>
            public bool IsBoostEnabled(string id)
            {
                if (Scan == null) return true;
                var group = Scan.Groups.FirstOrDefault(g => g.Id == "system_boost");
                if (group == null) return true;
                var item = group.Items.FirstOrDefault(i => i.Id == id);
                return item == null || item.IsChecked;
            }

            public void Fail(string id, string title, string glyph, Exception ex, string why)
            {
                CcLog.Report("Hero." + id, ex);
                FailedSteps.Add(new CleanupReportItem
                {
                    GroupId = "system_boost",
                    Id = id,
                    Title = title,
                    IconGlyph = glyph,
                    IsFailed = true,
                    StatusSummary = string.Format(LocalizationService.Get("ناموفق ✗ — {0}"), why)
                });
            }
        }

        private void Live(string title, string subtitle, double percent)
        {
            if (LiveCleanupCard == null) return;
            LiveCleanupCard.Visibility = Visibility.Visible;
            LiveCleanProgressBar.Value = percent;
            LiveCleanStatusTitle.Text = title;
            LiveCleanPhaseSubtitle.Text = subtitle;
            if (LiveCleanSpinner != null) LiveCleanSpinner.Visibility = Visibility.Visible;
            if (LiveCleanDoneIcon != null) LiveCleanDoneIcon.Visibility = Visibility.Collapsed;
        }

        /// <summary>آماده‌سازی UI و کش اسکن قبل از مرحلهٔ صفر (آماده‌سازی).</summary>
        private HeroRunSession HeroBeginRun()
        {
            var s = new HeroRunSession();

            Live(LocalizationService.Get("در حال آماده سازی و ارزیابی سرفصل های سیستم..."),
                LocalizationService.Get("بررسی متوالی فایل های موقت، کش ها و فضاهای ذخیره سازی"), 5);
            LiveFreedCounterText.Text = LocalizationService.Get("+ 0 بایت آزاد شد");

            _activeCleaningItems.Clear();
            _displayedFreedBytes = 0;
            _heroBorderline.Clear();
            _isBusy = true;
            _heroSession = s;
            _heroAborted = false;
            _heroCts = new CancellationTokenSource();
            return s;
        }

        /// <summary>اجرای یک مرحله؛ خروجی برای RPC سمت JS (معمولاً MB آزادشده).</summary>
        internal async Task<object?> HeroRunStepAsync(int index, int[] selected)
        {
            // پس از لغو، جلسهٔ تازه ساخته نمی شود — به JS هم صریحاً خبر می دهیم
            if (_heroAborted) return new { aborted = true, mb = 0L };

            var s = _heroSession ?? HeroBeginRun();
            var ct = _heroCts?.Token ?? CancellationToken.None;
            if (ct.IsCancellationRequested) return new { aborted = true, mb = 0L };

            switch (index)
            {
                case 0: await HeroStepPrepareAsync(s); return new { mb = 0L };
                case 1: await HeroStepScanAsync(s); return new { mb = 0L };
                case 2: return await HeroStepCleanAsync(s);
                case 3: return await HeroStepRamAsync(s);
                case 4: await HeroStepDnsAsync(s); return new { mb = 0L };
                case 5: return await HeroStepBrowserAsync(s);
                case 6: return await HeroStepRegistryAsync(s);
                case 7: return await HeroStepStartupAsync(s);
                case 8: return await HeroStepTweaksAsync(s);
                case 9: await HeroStepFastStartupAsync(s); return new { mb = 0L };
                case 10: return await HeroStepGpuAsync(s);
                case 11: return await HeroStepDebloatAsync(s, selected ?? Array.Empty<int>());
                case 12: await HeroStepHardwareAsync(s); return new { mb = 0L };
                case 13: await HeroStepFinalizeAsync(s); return new { mb = 0L };
                default: return new { mb = 0L };
            }
        }

        // ---------------------------------------------------------------- 0
        private Task HeroStepPrepareAsync(HeroRunSession s)
        {
            // اسکن پیش‌زمینه تا مرحلهٔ ۱ سریع شود؛ اگر قبلاً اسکن شده باشد کافی است
            if (_cachedScanResult != null) s.Scan = _cachedScanResult;
            return Task.CompletedTask;
        }

        // ---------------------------------------------------------------- 1
        private async Task HeroStepScanAsync(HeroRunSession s)
        {
            Live(LocalizationService.Get("در حال پویش سریع سرفصل های سیستم..."), LocalizationService.Get("شناسایی فایل های زائد و گزینه های قابل بهینه سازی"), 8);
            // اسکنِ مشترک: اگر اسکنِ پس‌زمینهٔ استارت‌آپ هنوز در جریان است همان را می‌گیریم
            // تا مرحلهٔ ۱ کلِ دیسک را دوباره نگردد.
            _cachedScanResult ??= await GetHeroScanAsync();
            if (_cachedScanResult == null) return;
            _heroScanSeen = true;
            ApplyPersistedJunkSelections(_cachedScanResult);
            RenderAdvancedGroups(_cachedScanResult);
            AdvancedCleanTotalText.Text =
                $"{LocalizationService.Get("TotalDetected", "مجموع شناسایی شده")}: {_cachedScanResult.FormattedTotal}";
            UpdateHeroCleanable(_cachedScanResult.TotalBytes);

            s.Scan = _cachedScanResult;
            s.ReportGroups.Clear();
            foreach (var group in _cachedScanResult.Groups)
            {
                s.ReportGroups.Add(new CleanupReportGroup
                {
                    GroupId = group.Id,
                    Title = group.Title,
                    IconGlyph = group.IconGlyph
                });
            }
            s.ReportGroups.Add(new CleanupReportGroup
            {
                GroupId = "system_boost",
                Title = LocalizationService.Get("بهینه سازی زنده سیستم و پایداری"),
                IconGlyph = "\uE9D2"
            });
        }

        // ---------------------------------------------------------------- 2
        private async Task<object?> HeroStepCleanAsync(HeroRunSession s)
        {
            s.Scan ??= _cachedScanResult;
            if (s.Scan == null)
            {
                await HeroStepScanAsync(s);
                if (s.Scan == null) return new { mb = 0L };
            }

            // گروه «leftovers» فقط در مرحلهٔ ۶ (پاکسازی عمیق) با بررسی کامل و حالت
            // حذف امنِ کاربر حذف می شود؛ اینجا در لیست نمی آید تا دوباره حذف نشود.
            var itemsToProcess = s.Scan.Groups
                .Where(g => g.Id != "system_boost" && g.Id != "leftovers")
                .SelectMany(g => g.Items)
                .Where(i => i.IsChecked && i.Bytes > 0)
                .ToList();

            if (itemsToProcess.Count == 0) return new { mb = 0L };

            if (CleanupReportCard != null) CleanupReportCard.Visibility = Visibility.Collapsed;
            _activeCleaningItems.Clear();
            foreach (var item in itemsToProcess)
            {
                _activeCleaningItems.Add(new JunkItemViewModel(item)
                {
                    IsLoading = false,
                    IsSuccess = false,
                    WasEmpty = false,
                    StatusText = item.FormattedBytes
                });
            }

            long freed = 0;
            int total = itemsToProcess.Count;
            var ct = _heroCts?.Token ?? CancellationToken.None;

            try
            {
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = itemsToProcess[i];
                var vm = _activeCleaningItems.FirstOrDefault(v => v.Id == item.Id);
                if (vm == null) continue;

                string detail = item.Bytes > 0
                    ? string.Format(LocalizationService.Get("پویش سکتورها و پاک‌سازی فضای شناسایی‌شده · حجم: {0}"), item.FormattedBytes)
                    : string.Format(LocalizationService.Get("شناسایی و پاک‌سازی فایل‌های زائد و کش سرفصل {0}"), vm.Title);

                Live(string.Format(LocalizationService.Get("پاک‌سازی فعال: {0}"), vm.Title),
                    string.Format(LocalizationService.Get("سرفصل {0} از {1} · {2}"), i + 1, total, detail),
                    10 + (i * 70.0 / total));

                vm.IsLoading = true;
                vm.IsSuccess = false;
                vm.StatusText = LocalizationService.Get("در حال پردازش سکتورها...");
                LiveCleanListView?.ScrollIntoView(vm);
                await Task.Delay(60, ct);

                long itemFreed = await Task.Run(() => CleanService.CleanCategory(item), ct);
                bool wasEmpty = itemFreed <= 0;
                freed += itemFreed;
                s.TotalFreed += itemFreed;

                vm.IsLoading = false;
                vm.IsSuccess = true;
                vm.WasEmpty = wasEmpty;
                vm.StatusText = wasEmpty
                    ? (item.Bytes > 0 ? LocalizationService.Get("قبلاً پاک بود ✓") : LocalizationService.Get("بدون فایل زائد (پاک)"))
                    : string.Format(LocalizationService.Get("+ {0} آزاد شد ✓"), SystemStats.FormatBytes(itemFreed));

                if (s.TotalFreed > _displayedFreedBytes) AnimateCountUp(s.TotalFreed);
                await Task.Delay(90, ct);

                s.ReportGroups.FirstOrDefault(g => g.GroupId == item.GroupId)?.Items.Add(new CleanupReportItem
                {
                    GroupId = item.GroupId,
                    Id = item.Id,
                    Title = item.Title,
                    IconGlyph = item.IconGlyph,
                    FreedBytes = itemFreed,
                    WasEmpty = wasEmpty,
                    StatusSummary = wasEmpty
                        ? (item.Bytes > 0 ? LocalizationService.Get("قبلاً پاک بود") : LocalizationService.Get("خالی بود (بدون تغییر)"))
                        : string.Format(LocalizationService.Get("+ {0} آزاد شد"), SystemStats.FormatBytes(itemFreed))
                });
                await Task.Delay(20, ct);
            }
            }
            catch (OperationCanceledException)
            {
                // لغو توسط کاربر — به JS صریح بگو تا انیمیشن هیرو بایستد
                return new { aborted = true, mb = 0L };
            }

            long mbFreed = freed > 0 ? Math.Max(1L, freed / (1024L * 1024L)) : 0L;
            return new { mb = mbFreed };
        }

        // ---------------------------------------------------------------- 3
        private async Task<object?> HeroStepRamAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("ramBoost")) return new { mb = 0L };
            Live(LocalizationService.Get("بهینه‌سازی عمیق حافظه رم (RAM Working Set)"),
                LocalizationService.Get("تخلیه Working Set پردازه‌ها و بازیابی بلوک‌های غیرفعال حافظه فیزیکی"), 82);
            await Task.Delay(60);

            long freed = 0;
            try
            {
                var boost = await OptimizerService.BoostRamAsync();
                freed = boost.FreedBytes;
                s.RamFreed = freed;
                if (!boost.Success && freed <= 0)
                {
                    s.Fail("ram_boost_fail", LocalizationService.Get("آزادسازی حافظه رم (RAM Working Set)"), "\uE9D2",
                        new InvalidOperationException(LocalizationService.Get("BoostRamAsync نتیجه‌ای برنگرداند")),
                        LocalizationService.Get("دسترسی لازم برای تخلیه Working Set فراهم نشد"));
                }
            }
            catch (Exception ex)
            {
                s.Fail("ram_boost_fail", LocalizationService.Get("آزادسازی حافظه رم (RAM Working Set)"), "\uE9D2", ex, ex.Message);
            }

            s.TotalFreed += freed;
            if (freed > 0) AnimateCountUp(s.TotalFreed);
            long mbFreed = freed > 0 ? Math.Max(1L, freed / (1024L * 1024L)) : 0L;
            return new { mb = mbFreed };
        }

        // ---------------------------------------------------------------- 4
        private async Task HeroStepDnsAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("dnsFlush") && !s.IsBoostEnabled("backgroundHelpers")) return;
            Live(LocalizationService.Get("نوسازی کش DNS و سوکت‌های شبکه"),
                LocalizationService.Get("تخلیه Resolve Cache، پاک‌سازی جدول ARP و ریست بافرهای Winsock"), 90);
            await Task.Delay(50);
            try
            {
                await Task.Run(() =>
                {
                    if (s.IsBoostEnabled("dnsFlush")) OptimizerService.FlushDns();
                    if (s.IsBoostEnabled("backgroundHelpers")) ProcessManagerService.CleanBackgroundHelpers();
                });
            }
            catch (Exception ex)
            {
                s.Fail("dns_flush_fail", LocalizationService.Get("تخلیه کش DNS و نوسازی کانکشن ها"), "\uE774", ex, ex.Message);
            }
        }

        // ---------------------------------------------------------------- 5
        private async Task<object?> HeroStepBrowserAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("browserBoost")) return new { mb = 0L };
            Live(LocalizationService.Get("سبک‌سازی مرورگر و توقف پردازه‌های مخفی"),
                LocalizationService.Get("بستن Background Tasks مرورگرهای Edge و Chrome و آزادسازی کش افزونه‌ها"), 92);
            long freed = 0;
            try
            {
                await BrowserOptimizerService.SuppressBrowserBackgroundModeAsync();
                // افزونه‌های مرورگر هرگز بدون تأیید صریح کاربر حذف نمی‌شوند
                s.ExtFreed = 0;
                _heroBrowserMb = 0;
            }
            catch (Exception ex)
            {
                s.Fail("browser_opt_fail", LocalizationService.Get("بهینه سازی مرورگر و پاکسازی افزونه ها"), "\uE774", ex, ex.Message);
            }
            await Task.Delay(50);
            long mbFreed = freed > 0 ? Math.Max(1L, freed / (1024L * 1024L)) : 0L;
            return new { mb = mbFreed };
        }

        // ---------------------------------------------------------------- 6
        private async Task<object?> HeroStepRegistryAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("orphanedRegistry") && !s.IsBoostEnabled("leftovers")) return new { mb = 0L };
            Live(LocalizationService.Get("پویش عمیق رجیستری و بقایای نرم‌افزارها"),
                LocalizationService.Get("شناسایی ارجاعات مفقود در AppData و حذف کلیدهای نامعتبر رجیستری"), 94);
            await Task.Delay(50);
            long freed = 0;
            try
            {
                var stepResult = await Task.Run(() =>
                {
                    int cleaned = s.IsBoostEnabled("orphanedRegistry")
                        ? RegistryCleanerService.CleanOrphanedRegistry()
                        : 0;
                    var folders = LeftoverCleanerService.ScanLeftoverFolders();
                    // حذف با حالت انتخابی کاربر (پیش فرض: سطل بازیافت) — فقط فضای
                    // واقعاً آزاد شده در FreedBytes می آید؛ سِیو بازی در خطر نیست
                    // خط قرمز: پوشه های بازمانده هرگز در کلین ۱-کلیک به طور خودکار پاک نمی شوند تا هیچ خطری متوجه سِیو بازی ها و فایل های کاربر نشود
                    var batch = new SafeDeleteBatchResult();
                    return (Cleaned: cleaned, FolderCount: folders.Count, Batch: batch);
                });
                s.RegCleaned = stepResult.Cleaned;
                s.LeftoverFolders = stepResult.FolderCount;
                s.LeftoverFreed = stepResult.Batch.FreedBytes;
                s.LeftoverMoved = stepResult.Batch.MovedBytes;
                s.LeftoverDeleted = stepResult.Batch.SucceededCount;
                freed = stepResult.Batch.FreedBytes;
                s.TotalFreed += freed;
                if (freed > 0) AnimateCountUp(s.TotalFreed);
            }
            catch (Exception ex)
            {
                s.Fail("deep_clean_leftovers_fail", LocalizationService.Get("پاکسازی عمیق بقایای نرم افزارها"), "\uE74D", ex, ex.Message);
            }
            long mbFreed = freed > 0 ? Math.Max(1L, freed / (1024L * 1024L)) : 0L;
            return new { mb = mbFreed };
        }

        // ---------------------------------------------------------------- 7
        private async Task<object?> HeroStepStartupAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("startupOptimize")) return new { mb = 0L };
            Live(LocalizationService.Get("ارزیابی و بهینه‌سازی استارتاپ ویندوز"),
                LocalizationService.Get("بررسی اولویت بارگذاری و غیرفعال‌سازی سرویس‌ها و لانچرهای کندکننده بوت"), 95);
            try
            {
                s.DisabledStartups = await StartupOptimizerService.OptimizeStartupAppsAsync();
                int userConfirmed = await PromptUserForOptionalOptimizationsAsync();
                s.DisabledStartups += userConfirmed;
            }
            catch (Exception ex)
            {
                s.Fail("startup_opt_fail", LocalizationService.Get("بهینه سازی استارتاپ ویندوز"), "\uE7B5", ex, ex.Message);
            }
            await Task.Delay(50);
            return new { mb = 0L };
        }

        // ---------------------------------------------------------------- 8
        private async Task<object?> HeroStepTweaksAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("winTweaks")) return new { mb = 0L };
            Live(LocalizationService.Get("توقف تله‌متری و روان‌سازی پردازشی ویندوز"),
                LocalizationService.Get("غیرفعال‌سازی DiagTrack و اعمال تنظیمات افزایش پاسخ‌دهی سیستم‌عامل"), 96);
            try
            {
                s.WinTweaks = await WindowsTweakService.ApplyLowRiskPerformanceTweaksAsync();
                s.WinTweaks += await TelemetryOptimizerService.DisableTelemetryAndDiagTrackAsync();
            }
            catch (Exception ex)
            {
                s.Fail("win_tweaks_fail", LocalizationService.Get("توقف تله متری و تنظیمات عملکرد ویندوز"), "\uE770", ex, ex.Message);
            }
            await Task.Delay(50);
            return new { mb = 0L };
        }

        // ---------------------------------------------------------------- 9
        private async Task HeroStepFastStartupAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("fastStartup")) return;
            Live(LocalizationService.Get("در حال بررسی و اعمال Fast Startup..."),
                LocalizationService.Get("کوتاه کردن زمان خاموش شدن سیستم (Fast Startup)"), 97);
            string title = LocalizationService.Get("Fast Startup (خاموش شدن سریع ویندوز)");
            const string glyph = "\uE7E8";
            try
            {
                var fs = await Task.Run(FastStartupService.GetStatus);
                if (fs.IsFastStartupEnabled)
                {
                    s.FastStartup = new CleanupReportItem
                    {
                        GroupId = "system_boost", Id = "fast_startup",
                        Title = title, IconGlyph = glyph,
                        WasEmpty = true, StatusSummary = LocalizationService.Get("از قبل فعال بود ✓")
                    };
                }
                else if (!fs.IsHibernationEnabled)
                {
                    s.FastStartup = new CleanupReportItem
                    {
                        GroupId = "system_boost", Id = "fast_startup",
                        Title = title, IconGlyph = glyph, IsFailed = true,
                        StatusSummary = LocalizationService.Get("انجام نشد ✗ — هایبرنیشن خاموش است (فعال سازی آن فایل hiberfil.sys می سازد)")
                    };
                }
                else if (FastStartupService.SetFastStartup(true))
                {
                    s.FastStartup = new CleanupReportItem
                    {
                        GroupId = "system_boost", Id = "fast_startup",
                        Title = title, IconGlyph = glyph,
                        WasEmpty = false, StatusSummary = LocalizationService.Get("فعال شد ✓")
                    };
                }
                else
                {
                    s.FastStartup = new CleanupReportItem
                    {
                        GroupId = "system_boost", Id = "fast_startup",
                        Title = title, IconGlyph = glyph, IsFailed = true,
                        StatusSummary = LocalizationService.Get("ناموفق ✗ — دسترسی Administrator لازم است")
                    };
                }
            }
            catch (Exception ex)
            {
                s.Fail("fast_startup", title, glyph, ex, ex.Message);
            }
            await Task.Delay(50);
        }

        // ---------------------------------------------------------------- 10
        private async Task<object?> HeroStepGpuAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("gpuOptimize")) return new { mb = 0L };
            Live(LocalizationService.Get("در حال تخصیص هوشمند کارت گرافیک..."),
                LocalizationService.Get("هدایت برنامه های سنگین و بازی ها به پردازنده گرافیکی قدرتمند"), 98);
            try
            {
                var gpu = await GpuOptimizerService.OptimizeGpuPreferencesAsync();
                s.GpuCount = gpu.ConfiguredAppsCount;
                s.GpuName = gpu.DedicatedGpuName;
            }
            catch (Exception ex)
            {
                s.Fail("gpu_opt_fail", LocalizationService.Get("تخصیص کارت گرافیک مجزا"), "\uE790", ex, ex.Message);
            }
            await Task.Delay(40);
            return new { mb = 0L };
        }

        // ---------------------------------------------------------------- 11
        private async Task<object?> HeroStepDebloatAsync(HeroRunSession s, int[] selected)
        {
            if (!s.IsBoostEnabled("bloatwareClean")) return new { mb = 0L };
            Live(LocalizationService.Get("در حال حذف برنامه ها و بازی های تبلیغاتی (Debloat)..."),
                LocalizationService.Get("پاکسازی Candy Crush و بازی های پیش فرض و بررسی برنامه های کم استفاده"), 99);
            long freed = 0;
            try
            {
                var (autoRemoved, autoFreed) = await BloatwareService.CleanAutomaticBloatwareAsync();
                s.BloatRemoved = autoRemoved;
                s.BloatFreed = autoFreed;
                freed += autoFreed;

                // حذف موارد مرزی که کاربر در دیالوگ تأیید کرده
                foreach (var idx in selected)
                {
                    if (idx < 0 || idx >= _heroBorderline.Count) continue;
                    var app = _heroBorderline[idx];
                    if (await BloatwareService.RemoveAppAsync(app))
                    {
                        s.BorderlineRemoved++;
                        // اندازه تخمینی در آمار آزادسازی واقعی حساب نمی شود
                        s.BorderlineFreed += app.SizeIsEstimate ? 0 : app.SizeBytes;
                        freed += app.SizeIsEstimate ? 0 : app.SizeBytes;
                    }
                }

                if (freed > 0)
                {
                    s.TotalFreed += freed;
                    AnimateCountUp(s.TotalFreed);
                }
            }
            catch (Exception ex)
            {
                s.Fail("bloatware_clean_fail", LocalizationService.Get("حذف برنامه ها و بازی های تبلیغاتی"), "\uE74C", ex, ex.Message);
            }
            await Task.Delay(40);
            long mbFreed = freed > 0 ? Math.Max(1L, freed / (1024L * 1024L)) : 0L;
            return new { mb = mbFreed };
        }

        // ---------------------------------------------------------------- 12
        private async Task HeroStepHardwareAsync(HeroRunSession s)
        {
            Live(LocalizationService.Get("در حال بررسی وضعیت مانیتور و توان سیستم..."),
                LocalizationService.Get("سنجش نرخ نوسازی تصویر و پلن مصرف انرژی"), 99.5);
            try
            {
                s.Attention = await HardwareDiagnosticService.RunDiagnosticsAsync();
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Hero.hardware_diagnostics");
            }
            await Task.Delay(150);
        }

        // ---------------------------------------------------------------- 13
        private async Task HeroStepFinalizeAsync(HeroRunSession s)
        {
            BuildHeroBoostGroup(s);

            LiveCleanProgressBar.Value = 100;
            if (LiveCleanSpinner != null) LiveCleanSpinner.Visibility = Visibility.Collapsed;
            if (LiveCleanDoneIcon != null) LiveCleanDoneIcon.Visibility = Visibility.Visible;
            LiveCleanStatusTitle.Text = LocalizationService.Get("بهینه سازی خودکار سیستم با موفقیت به پایان رسید!");
            LiveCleanPhaseSubtitle.Text = string.Format(LocalizationService.Get("مجموعاً {0} آزاد گردید."), SystemStats.FormatBytes(s.TotalFreed));
            _displayedFreedBytes = s.TotalFreed;
            LiveFreedCounterText.Text = string.Format(LocalizationService.Get("+ {0} آزاد شد"), SystemStats.FormatBytes(s.TotalFreed));

            await Task.Delay(500);

            LiveCleanupCard.Visibility = Visibility.Collapsed;
            RenderHierarchicalReport(s.ReportGroups, s.TotalFreed, s.Attention);
            CleanupReportCard.Visibility = Visibility.Visible;

            _lastCleanFreedBytes = s.TotalFreed;
            _lastCleanDateTime = DateTime.Now;
            _isCleanedRecently = true;
            _heroBrowserAlertSuppressedUntil = DateTime.UtcNow.AddHours(24);
            _heroSnoozedUntil = DateTime.UtcNow.AddHours(24);
            if (s.TotalFreed > 0)
            {
                _cumulativeFreedBytes += s.TotalFreed;
                RecordCleanupSession(s.TotalFreed);
            }
            SaveCumulativeFreedStats();
            UpdateCumulativeChipsUI();
            RenderCleanupHistory();

            _cachedScanResult = null;
            AdvancedCleanTotalText.Text =
                LocalizationService.Get("RefreshedAfterClean", "نوسازی پس از پاکسازی");
            UpdateHeroCleanable(0);
            // keepResult: نتیجهٔ این اسکن نباید اسکنِ فعلی شود تا بعد از تمیز کردن
            // دکمهٔ هیرو «بررسی مجدد» بماند (چیپ شمارش اما تازه می شود).
            ScheduleHeroCleanableRefresh(keepResult: false);

            double prev = _currentHealthScore;
            RefreshSystemStats();
            double target = _currentHealthScore;
            if (Math.Abs(target - prev) > 0.5)
            {
                SafeInvoker.Run(AnimateHealthScoreTransition(prev, target));
            }

            _isBusy = false;
            // اگر زبان در طول پاکسازی عوض شده باشد، پنل پیشرفته باید با کاتالوگ جدید تازه شود.
            if (_advancedRelocalizePending) RelocalizeAdvancedGroupsAfterLanguageChange();
            _heroSession = null;
            PushHeroState(force: true);
        }

        /// <summary>گروه «بهینه سازی زنده» گزارش را از شمارنده‌های جمع‌شده می‌سازد.</summary>
        private void BuildHeroBoostGroup(HeroRunSession s)
        {
            var group = s.ReportGroups.FirstOrDefault(g => g.GroupId == "system_boost");
            if (group == null) return;
            bool HasFailure(string id) => s.FailedSteps.Any(f => f.Id == id);

            if (s.IsBoostEnabled("ramBoost") && !HasFailure("ram_boost_fail"))
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "ram_boost",
                    Title = LocalizationService.Get("آزادسازی حافظه رم (RAM Working Set)"), IconGlyph = "\uE9D2",
                    FreedBytes = s.RamFreed, WasEmpty = s.RamFreed == 0,
                    StatusSummary = s.RamFreed > 0 ? string.Format(LocalizationService.Get("+ {0} آزاد شد"), SystemStats.FormatBytes(s.RamFreed)) : LocalizationService.Get("بدون تغییر")
                });

            if (s.IsBoostEnabled("dnsFlush") && !HasFailure("dns_flush_fail"))
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "dns_flush",
                    Title = LocalizationService.Get("تخلیه کش DNS و نوسازی کانکشن ها"), IconGlyph = "\uE774",
                    StatusSummary = LocalizationService.Get("با موفقیت نوسازی شد ✓")
                });

            if (s.ExtFreed > 0)
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "browser_opt",
                    Title = LocalizationService.Get("بهینه سازی مرورگرها و حذف افزونه های زائد"), IconGlyph = "\uE774",
                    FreedBytes = s.ExtFreed,
                    StatusSummary = string.Format(LocalizationService.Get("+ {0} آزاد شد ✓"), SystemStats.FormatBytes(s.ExtFreed))
                });

            if (s.DisabledStartups > 0)
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "startup_opt",
                    Title = string.Format(LocalizationService.Get("بهینه سازی استارتاپ ({0} برنامه غیرضروری)"), s.DisabledStartups), IconGlyph = "\uE7B5",
                    StatusSummary = LocalizationService.Get("غیرفعال شد ✓")
                });

            if (s.GpuCount > 0)
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "gpu_opt",
                    Title = string.Format(LocalizationService.Get("تخصیص کارت گرافیک مجزا ({0})"), s.GpuName), IconGlyph = "\uE790",
                    StatusSummary = string.Format(LocalizationService.Get("{0} برنامه بهینه سازی شد ✓"), s.GpuCount)
                });

            if (s.BloatRemoved > 0)
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "bloat_clean",
                    Title = string.Format(LocalizationService.Get("حذف خودکار برنامه ها و بازی های تبلیغاتی ({0} برنامه)"), s.BloatRemoved), IconGlyph = "\uE74C",
                    FreedBytes = s.BloatFreed,
                    StatusSummary = s.BloatFreed > 0 ? string.Format(LocalizationService.Get("+ {0} آزاد شد ✓"), SystemStats.FormatBytes(s.BloatFreed)) : LocalizationService.Get("پاکسازی شد ✓")
                });

            if (s.BorderlineRemoved > 0)
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "borderline_clean",
                    Title = string.Format(LocalizationService.Get("پاکسازی برنامه های اختیاری تاییدشده ({0} برنامه)"), s.BorderlineRemoved), IconGlyph = "\uE74C",
                    FreedBytes = s.BorderlineFreed,
                    StatusSummary = s.BorderlineFreed > 0 ? string.Format(LocalizationService.Get("+ {0} آزاد شد ✓"), SystemStats.FormatBytes(s.BorderlineFreed)) : LocalizationService.Get("پاکسازی شد ✓")
                });

            if (s.WinTweaks > 0)
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "win_tweaks",
                    Title = LocalizationService.Get("توقف تله‌متری، افزایش سرعت سرچ، اکسپلورر و تبلیغات ویندوز"), IconGlyph = "\uE770",
                    StatusSummary = LocalizationService.Get("اعمال شد ✓")
                });

            if (s.FastStartup != null) group.Items.Add(s.FastStartup);

            if (s.RegCleaned > 0 || s.LeftoverFreed > 0 || s.LeftoverMoved > 0)
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "deep_clean_leftovers",
                    Title = string.Format(LocalizationService.Get("پاکسازی عمیق ({0} کلید نامعتبر رجیستری + {1} پوشه رهاشده)"), s.RegCleaned, s.LeftoverDeleted),
                    IconGlyph = "\uE74D",
                    FreedBytes = s.LeftoverFreed,
                    WasEmpty = s.RegCleaned == 0 && s.LeftoverFreed == 0 && s.LeftoverMoved == 0,
                    StatusSummary = s.LeftoverFreed > 0
                        ? string.Format(LocalizationService.Get("+ {0} آزاد شد ✓"), SystemStats.FormatBytes(s.LeftoverFreed))
                        : (s.LeftoverMoved > 0
                            ? string.Format(LocalizationService.Get("{0} پوشه به سطل بازیافت منتقل شد (با خالی کردن سطل فضا آزاد می شود)"), s.LeftoverDeleted)
                            : LocalizationService.Get("کلیدهای نامعتبر پاکسازی شدند ✓"))
                });

            group.Items.AddRange(s.FailedSteps);
        }

        /// <summary>لغو اجرا (کاربر یا ترِی)؛ به JS هم خبر می‌دهد تا انیمیشن هیرو بایستد.</summary>
        internal void HeroAbortRun()
        {
            // لغوی واقعی: توکن کنسل می شود تا هر مرحلهٔ در جریان سریع بایستد؛
            // و پرچم aborted جلوی ساخته شدن جلسهٔ تازه در run.step بعدی را می گیرد.
            _heroAborted = true;
            try
            {
                _heroCts?.Cancel();
            }
            catch (ObjectDisposedException) { /* توکن قبلاً از رده خارج شده */ }
            _heroCts = null;

            _heroSession = null;
            _isBusy = false;
            if (_advancedRelocalizePending) RelocalizeAdvancedGroupsAfterLanguageChange();
            if (LiveCleanupCard != null) LiveCleanupCard.Visibility = Visibility.Collapsed;
            _activeCleaningItems.Clear();
        }

        // ====================================================================
        // Scan / borderline / codex / quick actions
        // ====================================================================

        /// <summary>پویش دوباره — توسط دکمهٔ «بررسی مجدد» هیرو صدا زده می‌شود.</summary>
        internal async Task HeroRescanAsync()
        {
            _heroSnoozedUntil = DateTime.MinValue;
            _heroBrowserAlertSuppressedUntil = DateTime.MinValue;
            SaveCumulativeFreedStats();
            ApplyHeroScanResult(await GetHeroScanAsync());
        }

        /// <summary>به تعویق انداختن هشدارهای غیرضروری هیرو به مدت ۲۴ ساعت با کلیک روی «بعداً».</summary>
        internal void HandleHeroSnooze()
        {
            _heroSnoozedUntil = DateTime.UtcNow.AddHours(24);
            _heroBrowserAlertSuppressedUntil = DateTime.UtcNow.AddHours(24);
            _heroLastStateJson = null;
            SaveCumulativeFreedStats();
            PushHeroState(force: true);
        }

        /// <summary>کاندیداهای مرزی را برمی‌گرداند و ایندکس‌ها را برای مرحلهٔ ۱۱ کش می‌کند.</summary>
        internal async Task<List<HeroBorderlineItem>> HeroGetBorderlineAsync()
        {
            if (_heroBorderlineDismissed || _isCleanedRecently)
            {
                _heroBorderline = new List<CandidateAppItem>();
                return new List<HeroBorderlineItem>();
            }

            try
            {
                _heroBorderline = await BloatwareService.ScanBorderlineReviewAppsAsync() ?? new List<CandidateAppItem>();
            }
            catch (Exception ex)
            {
                CcLog.Report("Hero.BorderlineScan", ex);
                _heroBorderline = new List<CandidateAppItem>();
            }

            return _heroBorderline
                .Select(a => new HeroBorderlineItem { Name = a.DisplayName, Size = a.FormattedSize })
                .ToList();
        }

        /// <summary>دیالوگ نیتیو انتخاب برنامه‌های مرزی؛ ایندکس‌های تأییدشده را به JS برمی‌گرداند.</summary>
        internal async Task ShowHeroBorderlineDialogAsync(List<HeroBorderlineItem> items)
        {
            int[] picked;
            if (items == null || items.Count == 0)
            {
                picked = Array.Empty<int>();
            }
            else
            {
                try
                {
                    var panel = new StackPanel { Spacing = 4 };
                    var boxes = new List<CheckBox>();
                    foreach (var item in items)
                    {
                        var cb = new CheckBox
                        {
                            IsChecked = false,
                            Content = new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 8,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = item.Name,
                                        VerticalAlignment = VerticalAlignment.Center
                                    },
                                    new TextBlock
                                    {
                                        Text = item.Size,
                                        Opacity = 0.6,
                                        FontSize = 12,
                                        VerticalAlignment = VerticalAlignment.Center
                                    }
                                }
                            }
                        };
                        boxes.Add(cb);
                        panel.Children.Add(cb);
                    }

                    var root = new StackPanel { Spacing = 6 };
                    root.Children.Add(new TextBlock
                    {
                        Text = LocalizationService.Get("این برنامه‌ها ممکن است به دردت بخورند؛ هر کدام را خواستی انتخاب کن."),
                        TextWrapping = TextWrapping.Wrap
                    });
                    root.Children.Add(new ScrollViewer { MaxHeight = 340, Content = panel });

                    var dialog = new ContentDialog
                    {
                        XamlRoot = Content.XamlRoot,
                        Title = LocalizationService.Get("برنامه‌های مرزی پیدا شد"),
                        Content = root,
                        PrimaryButtonText = LocalizationService.Get("حذف انتخاب‌شده‌ها"),
                        CloseButtonText = LocalizationService.Get("انصراف"),
                        DefaultButton = ContentDialogButton.Close
                    };

                    _heroBorderlineDismissed = true;
                    var result = await dialog.ShowAsync();
                    picked = result == ContentDialogResult.Primary
                        ? boxes.Select((b, i) => b.IsChecked == true ? i : -1).Where(i => i >= 0).ToArray()
                        : Array.Empty<int>();
                }
                catch (Exception ex)
                {
                    CcLog.Report("Hero.BorderlineDialog", ex);
                    picked = Array.Empty<int>();
                }
            }

            SendToHero(HeroOutbound.BorderlineResult(picked));
        }

        /// <summary>
        /// دفترچهٔ کشفیات (codex). WebView هیرو ۱۷۰px است و overlay داخلی آن بریده می‌شود؛
        /// به همین دلیل مدل را از JS می‌گیریم و در ContentDialog نیتیو نشان می‌دهیم.
        /// </summary>
        internal async Task ShowHeroCodexDialogAsync(System.Text.Json.JsonElement? data)
        {
            if (data is not { ValueKind: System.Text.Json.JsonValueKind.Object }) return;
            try
            {
                var d = data.Value;
                var root = new StackPanel { Spacing = 8 };

                root.Children.Add(new TextBlock
                {
                    Text = string.Format(LocalizationService.Get("خلق و خو: {0} · {1} روز حضور"), Str(d, "mood"), Int(d, "days")),
                    TextWrapping = TextWrapping.Wrap
                });
                root.Children.Add(new TextBlock
                {
                    Text = string.Format(LocalizationService.Get("{0} از {1} کشف شده · {2} اجرا در هفته"), Int(d, "found"), Int(d, "total"), Int(d, "weekRuns")),
                    Opacity = 0.75,
                    TextWrapping = TextWrapping.Wrap
                });

                if (d.TryGetProperty("ach", out var ach) && ach.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var a in ach.EnumerateArray())
                    {
                        root.Children.Add(new TextBlock
                        {
                            Text = (a.TryGetProperty("done", out var dn) && dn.GetBoolean() ? "✓ " : "○ ") + Str(a, "title"),
                            TextWrapping = TextWrapping.Wrap
                        });
                    }
                }

                if (d.TryGetProperty("groups", out var groups) && groups.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var g in groups.EnumerateArray())
                    {
                        if (!g.TryGetProperty("items", out var its) || its.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                        int found = 0, totalItems = 0;
                        foreach (var it in its.EnumerateArray())
                        {
                            totalItems++;
                            if (it.TryGetProperty("found", out var f) && f.ValueKind == System.Text.Json.JsonValueKind.True) found++;
                        }
                        root.Children.Add(new TextBlock
                        {
                            Text = $"{Str(g, "name")}: {found}/{totalItems}",
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            TextWrapping = TextWrapping.Wrap
                        });
                    }
                }

                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = LocalizationService.Get("دفترچه کشفیات"),
                    Content = new ScrollViewer { MaxHeight = 420, Content = root },
                    CloseButtonText = LocalizationService.Get("بستن"),
                    DefaultButton = ContentDialogButton.Close
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                CcLog.Report("Hero.CodexDialog", ex);
            }
        }

        private static string Str(System.Text.Json.JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
                ? (v.GetString() ?? "") : "";

        private static int Int(System.Text.Json.JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number
                ? v.GetInt32() : 0;

        /// <summary>روی چیپ‌های پیشنهادی (فایل حجیم / برنامه سنگین) کلیک شد.</summary>
        internal void HandleHeroChip(string? key)
        {
            var page = key switch
            {
                "files" => "deep_storage",
                "apps" => "app_uninstaller",
                "startup" => "startup_manager",
                _ => null
            };
            if (page == null) return;

            SwitchToView(AppView.Tools);
            NavigateToToolPage(page);
        }

        /// <summary>اکشن‌های سریع هیرو: رفع پراکسی شبح، رم، مرورگر، پردازنده.</summary>
        internal async Task HandleHeroQuickAsync(string? action)
        {
            try
            {
                switch (action)
                {
                    case "fixproxy":
                        await Task.Run(NetworkProxyDiagnosticService.FixGhostProxy);
                        _heroProxyDead = false;
                        _heroProxyAlertSuppressedUntil = DateTime.UtcNow.AddMinutes(10);
                        ShowInfoBar(LocalizationService.Get("اصلاح اتصال اینترنت"), LocalizationService.Get("پراکسی نامعتبر ویندوز خاموش شد و تنظیمات شبکه نوسازی گردید."), InfoBarSeverity.Success);
                        break;
                    case "ram":
                    {
                        var boost = await OptimizerService.BoostRamAsync();
                        // هم‌زمان با رم، روندهای به‌روزرسانی پس‌زمینه هم بسته شوند تا CPU پشتِ رم
                        // واقعاً سبک شود — طبق درخواست: «هم رم هم پردازش، نه الکی»
                        int ramHelpers = await Task.Run(ProcessManagerService.CleanBackgroundHelpers);
                        _heroRamAlertSuppressedUntil = DateTime.UtcNow.AddMinutes(5);
                        string ramMsg = ramHelpers > 0
                            ? string.Format(LocalizationService.Get("حافظه رم بهینه شد ({0} آزاد شد) و {1} روندهای پس‌زمینه بسته شدند."), SystemStats.FormatBytes(boost.FreedBytes), ramHelpers)
                            : boost.FreedBytes > 0
                                ? string.Format(LocalizationService.Get("حافظه رم با موفقیت بهینه‌سازی شد ({0} آزاد شد)."), SystemStats.FormatBytes(boost.FreedBytes))
                                : LocalizationService.Get("حافظه رم بررسی و تمام کش‌های غیرضروری تخلیه شدند.");
                        ShowInfoBar(LocalizationService.Get("آزادسازی رم"), ramMsg, InfoBarSeverity.Success);
                        break;
                    }
                    case "browser":
                    {
                        long beforeBytes = 0;
                        long afterBytes = 0;
                        int trimmedCount = 0;
                        foreach (var name in new[] { "chrome", "msedge", "brave", "opera" })
                        {
                            foreach (var p in Process.GetProcessesByName(name))
                            {
                                try
                                {
                                    if (!p.HasExited && p.Id > 4)
                                    {
                                        beforeBytes += p.WorkingSet64;
                                        if (CleanC.Core.Native.Win32.EmptyWorkingSet(p.Handle)) trimmedCount++;
                                        p.Refresh();
                                        afterBytes += p.WorkingSet64;
                                    }
                                }
                                catch { }
                                finally { p.Dispose(); }
                            }
                        }
                        await BrowserOptimizerService.SuppressBrowserBackgroundModeAsync();
                        _heroBrowserAlertSuppressedUntil = DateTime.UtcNow.AddHours(24);
                        _heroBrowserMb = 0;
                        SaveCumulativeFreedStats();
                        long freedMb = Math.Max(0, (beforeBytes - afterBytes) / (1024 * 1024));
                        string bMsg = freedMb > 0
                            ? string.Format(LocalizationService.Get("پردازه‌ها و تب‌های پس‌زمینهٔ مرورگر با موفقیت سبک شدند ({0} مگابایت رم آزاد شد)."), freedMb)
                            : LocalizationService.Get("پردازه‌ها و وظایف پس‌زمینهٔ مرورگر با موفقیت بهینه‌سازی شدند.");
                        ShowInfoBar(LocalizationService.Get("سبک‌سازی مرورگر"), bMsg, InfoBarSeverity.Success);
                        break;
                    }
                    case "cpu":
                    {
                        // «نه الکی»: اولویت پردازه‌های سنگین واقعاً به BelowNormal می‌رود و
                        // روندهای به‌روزرسانی پس‌زمینه بسته می‌شوند — متن InfoBar دقیق گزارش می‌شود
                        int helpers = await Task.Run(ProcessManagerService.CleanBackgroundHelpers);
                        var (lowered, topName) = await Task.Run(() => ProcessManagerService.EaseHighCpuProcesses());
                        _heroTopCpu = "";
                        _heroCpuAlertSuppressedUntil = DateTime.UtcNow.AddMinutes(5);
                        string top = string.IsNullOrEmpty(topName) ? "" : string.Format(LocalizationService.Get(" پرمصرف‌ترین: {0}."), topName);
                        string cpuMsg = lowered > 0
                            ? string.Format(LocalizationService.Get("{0} پردازهٔ سنگین به اولویت پایین منتقل و {1} روندهای پس‌زمینه بسته شد.{2}"), lowered, helpers, top)
                            : helpers > 0
                                ? string.Format(LocalizationService.Get("{0} روندهای پس‌زمینه بسته شد و پردازنده پایش شد.{1}"), helpers, top)
                                : string.Format(LocalizationService.Get("پردازنده پایش شد؛ پردازهٔ قابل مداخله‌ای پیدا نشد.{0}"), top);
                        ShowInfoBar(LocalizationService.Get("مدیریت پردازش‌ها"), cpuMsg, InfoBarSeverity.Success);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Report("Hero.Quick." + action, ex);
            }
            _heroLastStateJson = null;
            PushHeroState(force: true);
        }

        // ====================================================================
        // Post-run helpers
        // ====================================================================

        /// <summary>پیشنهادهای «کنجکاوی» بعد از اتمام اجرا (پاسخ RPC <c>run.after</c>).</summary>
        internal async Task<object> HeroAfterAsync()
        {
            var chips = new List<string[]>();
            try
            {
                var files = await LargeFileScannerService.ScanUserLargeFilesAsync();
                foreach (var f in files.Take(2))
                    chips.Add(new[] { $"{f.FileName} · {f.FormattedSize}", "files" });

                var apps = await AppUninstallerService.GetInstalledAppsAsync();
                foreach (var a in apps.OrderByDescending(x => x.EstimatedSizeBytes).Take(2))
                    chips.Add(new[] { $"{a.DisplayName} · {a.FormattedSize}", "apps" });
            }
            catch (Exception ex)
            {
                CcLog.Report("Hero.After", ex);
            }

            return new { chips };
        }

        private async Task<int> PromptUserForOptionalOptimizationsAsync()
        {
            try
            {
                var reviewableStartups = StartupOptimizerService.GetReviewableStartupItems();
                var reviewableExts = BrowserOptimizerService.GetReviewableDeadExtensions();

                if (reviewableStartups.Count == 0 && reviewableExts.Count == 0)
                    return 0;

                var root = new StackPanel { Spacing = 12 };

                var desc = new TextBlock
                {
                    Text = LocalizationService.Get("OptReviewDialogDesc", "برنامه‌های زیر در استارتاپ سیستم یا مرورگر فعال هستند. برای افزایش سرعت بوت و سبک‌سازی سیستم، مواردی که مایل به غیرفعال‌سازی آن‌ها هستید را تأیید کنید (تمامی موارد به‌صورت امن ذخیره می‌شوند و هر زمان از مدیریت استارتاپ قابل فعال‌سازی مجدد هستند):"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush")
                };
                root.Children.Add(desc);

                var listStack = new StackPanel { Spacing = 6 };
                var startupBoxes = new List<(CheckBox Box, StartupItem Item)>();
                var extBoxes = new List<(CheckBox Box, string ExtId)>();

                // ۱. استارتاپ‌ها (Gemini, AutoClaw, Docker, Telegram, VPNs, ...)
                foreach (var item in reviewableStartups)
                {
                    var card = new Border
                    {
                        Background = GetThemeBrush("AppCardSecondaryBrush"),
                        BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(7),
                        Padding = new Thickness(10, 6, 10, 6)
                    };

                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var chk = new CheckBox
                    {
                        IsChecked = false,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    Grid.SetColumn(chk, 0);
                    row.Children.Add(chk);

                    var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
                    info.Children.Add(new TextBlock { Text = item.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 });
                    info.Children.Add(new TextBlock { Text = item.Command, FontSize = 10, Foreground = GetThemeBrush("TextFillColorTertiaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
                    Grid.SetColumn(info, 1);
                    row.Children.Add(info);

                    var badge = new Border
                    {
                        Background = GetThemeBrush("AppCardBackgroundBrush"),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = item.Category, FontSize = 10, Foreground = GetThemeBrush("AccentFillColorDefaultBrush") }
                    };
                    Grid.SetColumn(badge, 2);
                    row.Children.Add(badge);

                    card.Child = row;
                    listStack.Children.Add(card);
                    startupBoxes.Add((chk, item));
                }

                // ۲. افزونه‌های کروم
                foreach (var ext in reviewableExts)
                {
                    var card = new Border
                    {
                        Background = GetThemeBrush("AppCardSecondaryBrush"),
                        BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(7),
                        Padding = new Thickness(10, 6, 10, 6)
                    };

                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var chk = new CheckBox
                    {
                        IsChecked = false,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    Grid.SetColumn(chk, 0);
                    row.Children.Add(chk);

                    var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
                    info.Children.Add(new TextBlock { Text = ext.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 });
                    info.Children.Add(new TextBlock { Text = ext.Reason, FontSize = 10, Foreground = GetThemeBrush("TextFillColorTertiaryBrush") });
                    Grid.SetColumn(info, 1);
                    row.Children.Add(info);

                    var badge = new Border
                    {
                        Background = GetThemeBrush("AppCardBackgroundBrush"),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = LocalizationService.Get("OptReviewExtBadge", "افزونه کروم"), FontSize = 10, Foreground = GetThemeBrush("SystemFillColorCautionBrush") }
                    };
                    Grid.SetColumn(badge, 2);
                    row.Children.Add(badge);

                    card.Child = row;
                    listStack.Children.Add(card);
                    extBoxes.Add((chk, ext.Id));
                }

                var scroll = new ScrollViewer { MaxHeight = 300, Content = listStack };
                root.Children.Add(scroll);

                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = LocalizationService.Get("OptReviewDialogTitle", "تأیید بهینه‌سازی ابزارها و برنامه‌های پس‌زمینه"),
                    Content = root,
                    PrimaryButtonText = LocalizationService.Get("OptReviewDialogConfirm", "غیرفعال‌سازی موارد انتخاب‌شده"),
                    CloseButtonText = LocalizationService.Get("OptReviewDialogCancel", "صرف‌نظر و ادامه"),
                    DefaultButton = ContentDialogButton.Primary,
                    FlowDirection = LocalizationService.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
                };

                var res = await dialog.ShowAsync();
                if (res != ContentDialogResult.Primary) return 0;

                int disabled = 0;
                foreach (var (box, item) in startupBoxes)
                {
                    if (box.IsChecked == true)
                    {
                        if (StartupOptimizerService.DisableStartupItem(item))
                            disabled++;
                    }
                }

                var extsToClean = extBoxes.Where(b => b.Box.IsChecked == true).Select(b => b.ExtId).ToHashSet();
                if (extsToClean.Count > 0)
                {
                    BrowserOptimizerService.CleanSpecificExtensions(extsToClean);
                    disabled += extsToClean.Count;
                }

                return disabled;
            }
            catch (Exception ex)
            {
                CcLog.Report("PromptUserForOptionalOptimizationsAsync", ex);
                return 0;
            }
        }
    }
}
