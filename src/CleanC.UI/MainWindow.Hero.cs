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
        internal void ApplyHeroScanResult(ScanResult? scan)
        {
            if (scan == null || _isExiting) return;

            _cachedScanResult = scan;
            _heroScanSeen = true;
            ApplyPersistedJunkSelections(scan);
            RenderAdvancedGroups(scan);
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
        private string GetWindowsAccentHex()
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
                UserName = s.CharacterUserName,
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
                Lun = s.CharacterLun
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
                    : $"{SystemStats.FormatBytes(_lastCleanFreedBytes)}، {_lastCleanDateTime:HH:mm}"
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

                long browserBytes = 0;
                foreach (var name in new[] { "chrome", "msedge" })
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try { browserBytes += p.WorkingSet64; } catch { }
                        finally { p.Dispose(); }
                    }
                }
                _heroBrowserMb = browserBytes / (1024 * 1024);

                // پراکسی شبح ثابت نیست؛ کاربر ممکن است هر لحظه آن را درست یا خراب کند
                if ((DateTime.UtcNow - _heroProxyAt).TotalSeconds >= 15)
                {
                    _heroProxyAt = DateTime.UtcNow;
                    _heroProxyDead = ReadGhostProxyConflict();
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Hero.Sensors");
            }

            if ((DateTime.UtcNow - _heroCpuAt).TotalSeconds >= 20)
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
            if (scan == null)
            {
                // اسکنی در دست نداریم. دو حالت دارد:
                //   • تازه استارت شده‌ایم → آیتم‌های پیش‌فرض را می‌فرستیم تا دکمهٔ هیرو
                //     «بهینه‌سازی سیستم · N مورد» باشد و با کلیک مستقیم وارد ۱۴ مرحله شود.
                //     (این رفتارِ دکمهٔ قدیمی Quick Boost است؛ حالت «بررسی مجدد» فقط اسکن
                //     می‌کند و کاربر را چند دقیقه معطل می‌گذارد بدون دیدن نتیجه.)
                //   • تازه پاک کرده‌ایم → لیست خالی تا اسکنِ تازه، دکمه «بررسی مجدد» بماند.
                if (!_heroScanSeen) items.AddRange(DefaultHeroBoostItems);
                return items;
            }

            long junkBytes = scan.Groups
                .Where(g => g.Id != "system_boost")
                .SelectMany(g => g.Items)
                .Where(i => i.IsChecked)
                .Sum(i => i.Bytes);

            if (junkBytes > 0) items.Add("junk");

            bool BoostOn(string id)
            {
                var group = scan.Groups.FirstOrDefault(g => g.Id == "system_boost");
                if (group == null) return true;
                var item = group.Items.FirstOrDefault(i => i.Id == id);
                return item == null || item.IsChecked;
            }

            if (BoostOn("dnsFlush")) items.Add("dns");
            if (BoostOn("browserBoost")) items.Add("browser");
            if (BoostOn("orphanedRegistry")) items.Add("reg");
            if (BoostOn("startupOptimize")) items.Add("startup");
            if (BoostOn("winTweaks")) items.Add("tweak");
            if (BoostOn("fastStartup")) items.Add("fast");
            if (BoostOn("gpuOptimize")) items.Add("gpu");
            if (BoostOn("bloatwareClean")) items.Add("debloat");

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
                    StatusSummary = $"ناموفق ✗ — {why}"
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

            Live("در حال آماده سازی و ارزیابی سرفصل های سیستم...",
                "بررسی متوالی فایل های موقت، کش ها و فضاهای ذخیره سازی", 5);
            LiveFreedCounterText.Text = "+ 0 بایت آزاد شد";

            _activeCleaningItems.Clear();
            _displayedFreedBytes = 0;
            _heroBorderline.Clear();
            _isBusy = true;
            _heroSession = s;
            return s;
        }

        /// <summary>اجرای یک مرحله؛ خروجی برای RPC سمت JS (معمولاً MB آزادشده).</summary>
        internal async Task<object?> HeroRunStepAsync(int index, int[] selected)
        {
            var s = _heroSession ?? HeroBeginRun();

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
            Live("در حال پویش سریع سرفصل های سیستم...", "شناسایی فایل های زائد و گزینه های قابل بهینه سازی", 8);
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
                Title = "بهینه سازی زنده سیستم و پایداری",
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

            var itemsToProcess = s.Scan.Groups
                .Where(g => g.Id != "system_boost")
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
            for (int i = 0; i < total; i++)
            {
                var item = itemsToProcess[i];
                var vm = _activeCleaningItems.FirstOrDefault(v => v.Id == item.Id);
                if (vm == null) continue;

                Live($"در حال پردازش: {vm.Title}...",
                    $"مرحله {i + 1} از {total}: پاکسازی و بهینه سازی متوالی",
                    10 + (i * 70.0 / total));

                vm.IsLoading = true;
                vm.IsSuccess = false;
                vm.StatusText = "در حال بررسی و پاکسازی...";
                await Task.Delay(60);

                long itemFreed = await Task.Run(() => CleanService.CleanCategory(item));
                bool wasEmpty = itemFreed <= 0;
                freed += itemFreed;
                s.TotalFreed += itemFreed;

                vm.IsLoading = false;
                vm.IsSuccess = true;
                vm.WasEmpty = wasEmpty;
                vm.StatusText = wasEmpty
                    ? (item.Bytes > 0 ? "قبلاً پاک بود ✓" : "بدون فایل زائد (پاک)")
                    : $"+ {SystemStats.FormatBytes(itemFreed)} آزاد شد ✓";

                if (s.TotalFreed > _displayedFreedBytes) AnimateCountUp(s.TotalFreed);
                await Task.Delay(90);

                s.ReportGroups.FirstOrDefault(g => g.GroupId == item.GroupId)?.Items.Add(new CleanupReportItem
                {
                    GroupId = item.GroupId,
                    Id = item.Id,
                    Title = item.Title,
                    IconGlyph = item.IconGlyph,
                    FreedBytes = itemFreed,
                    WasEmpty = wasEmpty,
                    StatusSummary = wasEmpty
                        ? (item.Bytes > 0 ? "قبلاً پاک بود" : "خالی بود (بدون تغییر)")
                        : $"+ {SystemStats.FormatBytes(itemFreed)} آزاد شد"
                });

                _activeCleaningItems.Remove(vm);
                await Task.Delay(20);
            }

            return new { mb = freed };
        }

        // ---------------------------------------------------------------- 3
        private async Task<object?> HeroStepRamAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("ramBoost")) return new { mb = 0L };
            Live("در حال آزادسازی حافظه فعال رم (RAM)...",
                "تخلیه Working Set و بهینه سازی تخصیص حافظه پردازه ها", 82);
            await Task.Delay(60);

            long freed = 0;
            try
            {
                var boost = await OptimizerService.BoostRamAsync();
                freed = boost.FreedBytes;
                s.RamFreed = freed;
                if (!boost.Success && freed <= 0)
                {
                    s.Fail("ram_boost_fail", "آزادسازی حافظه رم (RAM Working Set)", "\uE9D2",
                        new InvalidOperationException("BoostRamAsync نتیجه‌ای برنگرداند"),
                        "دسترسی لازم برای تخلیه Working Set فراهم نشد");
                }
            }
            catch (Exception ex)
            {
                s.Fail("ram_boost_fail", "آزادسازی حافظه رم (RAM Working Set)", "\uE9D2", ex, ex.Message);
            }

            s.TotalFreed += freed;
            if (freed > 0) AnimateCountUp(s.TotalFreed);
            return new { mb = freed };
        }

        // ---------------------------------------------------------------- 4
        private async Task HeroStepDnsAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("dnsFlush") && !s.IsBoostEnabled("backgroundHelpers")) return;
            Live("در حال تخلیه کش DNS و نوسازی کانکشن ها...",
                "رفرش جدول آدرس های شبکه و پاکسازی پردازه های زائد پس زمینه", 90);
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
                s.Fail("dns_flush_fail", "تخلیه کش DNS و نوسازی کانکشن ها", "\uE774", ex, ex.Message);
            }
        }

        // ---------------------------------------------------------------- 5
        private async Task<object?> HeroStepBrowserAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("browserBoost")) return new { mb = 0L };
            Live("در حال بهینه سازی سرعت و حذف افزونه های زائد مرورگرها...",
                "فعال سازی شتاب سخت افزاری، توقف تسک های پس زمینه و حذف افزونه های مرده", 92);
            long freed = 0;
            try
            {
                await BrowserOptimizerService.SuppressBrowserBackgroundModeAsync();
                freed = await Task.Run(BrowserOptimizerService.CleanInactiveChromeExtensions);
                s.ExtFreed = freed;
                if (freed > 0)
                {
                    s.TotalFreed += freed;
                    AnimateCountUp(s.TotalFreed);
                }
                _heroBrowserMb = 0;
            }
            catch (Exception ex)
            {
                s.Fail("browser_opt_fail", "بهینه سازی مرورگر و پاکسازی افزونه ها", "\uE774", ex, ex.Message);
            }
            await Task.Delay(50);
            return new { mb = freed };
        }

        // ---------------------------------------------------------------- 6
        private async Task<object?> HeroStepRegistryAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("orphanedRegistry") && !s.IsBoostEnabled("leftovers")) return new { mb = 0L };
            Live("در حال پاکسازی عمیق و پویش بقایای نرم افزارهای حذف شده...",
                "اسکن پوشه های رهاشده در AppData و کلیدهای نامعتبر رجیستری", 94);
            await Task.Delay(50);
            long freed = 0;
            try
            {
                (s.RegCleaned, s.LeftoverFolders, freed) = await Task.Run(() =>
                {
                    int cleaned = s.IsBoostEnabled("orphanedRegistry")
                        ? RegistryCleanerService.CleanOrphanedRegistry()
                        : 0;
                    var folders = LeftoverCleanerService.ScanLeftoverFolders();
                    long f = s.IsBoostEnabled("leftovers")
                        ? LeftoverCleanerService.DeleteLeftoverFolders(folders)
                        : 0;
                    return (cleaned, folders.Count, f);
                });
                s.LeftoverFreed = freed;
                s.TotalFreed += freed;
                if (freed > 0) AnimateCountUp(s.TotalFreed);
            }
            catch (Exception ex)
            {
                s.Fail("deep_clean_leftovers_fail", "پاکسازی عمیق بقایای نرم افزارها", "\uE74D", ex, ex.Message);
            }
            return new { mb = freed };
        }

        // ---------------------------------------------------------------- 7
        private async Task<object?> HeroStepStartupAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("startupOptimize")) return new { mb = 0L };
            Live("در حال بهینه سازی استارتاپ ویندوز...",
                "غیرفعال سازی برنامه ها و لانچرهای سنگین شروع خودکار", 95);
            try
            {
                s.DisabledStartups = await StartupOptimizerService.OptimizeStartupAppsAsync();
            }
            catch (Exception ex)
            {
                s.Fail("startup_opt_fail", "بهینه سازی استارتاپ ویندوز", "\uE7B5", ex, ex.Message);
            }
            await Task.Delay(50);
            return new { mb = 0L };
        }

        // ---------------------------------------------------------------- 8
        private async Task<object?> HeroStepTweaksAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("winTweaks")) return new { mb = 0L };
            Live("در حال اعمال تنظیمات بهینه سازی ویندوز...",
                "غیرفعال سازی پیشنهادات و سرویس های درگیرکننده پردازنده", 96);
            try
            {
                s.WinTweaks = await WindowsTweakService.ApplyLowRiskPerformanceTweaksAsync();
                s.WinTweaks += await TelemetryOptimizerService.DisableTelemetryAndDiagTrackAsync();
            }
            catch (Exception ex)
            {
                s.Fail("win_tweaks_fail", "توقف تله متری و تنظیمات عملکرد ویندوز", "\uE770", ex, ex.Message);
            }
            await Task.Delay(50);
            return new { mb = 0L };
        }

        // ---------------------------------------------------------------- 9
        private async Task HeroStepFastStartupAsync(HeroRunSession s)
        {
            if (!s.IsBoostEnabled("fastStartup")) return;
            Live("در حال بررسی و اعمال Fast Startup...",
                "کوتاه کردن زمان خاموش شدن سیستم (Fast Startup)", 97);
            const string title = "Fast Startup (خاموش شدن سریع ویندوز)";
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
                        WasEmpty = true, StatusSummary = "از قبل فعال بود ✓"
                    };
                }
                else if (!fs.IsHibernationEnabled)
                {
                    s.FastStartup = new CleanupReportItem
                    {
                        GroupId = "system_boost", Id = "fast_startup",
                        Title = title, IconGlyph = glyph, IsFailed = true,
                        StatusSummary = "انجام نشد ✗ — هایبرنیشن خاموش است (فعال سازی آن فایل hiberfil.sys می سازد)"
                    };
                }
                else if (FastStartupService.SetFastStartup(true))
                {
                    s.FastStartup = new CleanupReportItem
                    {
                        GroupId = "system_boost", Id = "fast_startup",
                        Title = title, IconGlyph = glyph,
                        WasEmpty = false, StatusSummary = "فعال شد ✓"
                    };
                }
                else
                {
                    s.FastStartup = new CleanupReportItem
                    {
                        GroupId = "system_boost", Id = "fast_startup",
                        Title = title, IconGlyph = glyph, IsFailed = true,
                        StatusSummary = "ناموفق ✗ — دسترسی Administrator لازم است"
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
            Live("در حال تخصیص هوشمند کارت گرافیک...",
                "هدایت برنامه های سنگین و بازی ها به پردازنده گرافیکی قدرتمند", 98);
            try
            {
                var gpu = await GpuOptimizerService.OptimizeGpuPreferencesAsync();
                s.GpuCount = gpu.ConfiguredAppsCount;
                s.GpuName = gpu.DedicatedGpuName;
            }
            catch (Exception ex)
            {
                s.Fail("gpu_opt_fail", "تخصیص کارت گرافیک مجزا", "\uE790", ex, ex.Message);
            }
            await Task.Delay(40);
            return new { mb = 0L };
        }

        // ---------------------------------------------------------------- 11
        private async Task<object?> HeroStepDebloatAsync(HeroRunSession s, int[] selected)
        {
            if (!s.IsBoostEnabled("bloatwareClean")) return new { mb = 0L };
            Live("در حال حذف برنامه ها و بازی های تبلیغاتی (Debloat)...",
                "پاکسازی Candy Crush و بازی های پیش فرض و بررسی برنامه های کم استفاده", 99);
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
                        s.BorderlineFreed += app.SizeBytes;
                        freed += app.SizeBytes;
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
                s.Fail("bloatware_clean_fail", "حذف برنامه ها و بازی های تبلیغاتی", "\uE74C", ex, ex.Message);
            }
            await Task.Delay(40);
            return new { mb = freed };
        }

        // ---------------------------------------------------------------- 12
        private async Task HeroStepHardwareAsync(HeroRunSession s)
        {
            Live("در حال بررسی وضعیت مانیتور و توان سیستم...",
                "سنجش نرخ نوسازی تصویر و پلن مصرف انرژی", 99.5);
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
            LiveCleanStatusTitle.Text = "بهینه سازی خودکار سیستم با موفقیت به پایان رسید!";
            LiveCleanPhaseSubtitle.Text = $"مجموعاً {SystemStats.FormatBytes(s.TotalFreed)} آزاد گردید.";
            _displayedFreedBytes = s.TotalFreed;
            LiveFreedCounterText.Text = $"+ {SystemStats.FormatBytes(s.TotalFreed)} آزاد شد";

            await Task.Delay(500);

            LiveCleanupCard.Visibility = Visibility.Collapsed;
            RenderHierarchicalReport(s.ReportGroups, s.TotalFreed, s.Attention);
            CleanupReportCard.Visibility = Visibility.Visible;

            _lastCleanFreedBytes = s.TotalFreed;
            if (s.TotalFreed > 0)
            {
                _cumulativeFreedBytes += s.TotalFreed;
                _lastCleanDateTime = DateTime.Now;
                _isCleanedRecently = true;
                RecordCleanupSession(s.TotalFreed);
                SaveCumulativeFreedStats();
                UpdateCumulativeChipsUI();
                RenderCleanupHistory();
            }

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
                    Title = "آزادسازی حافظه رم (RAM Working Set)", IconGlyph = "\uE9D2",
                    FreedBytes = s.RamFreed, WasEmpty = s.RamFreed == 0,
                    StatusSummary = s.RamFreed > 0 ? $"+ {SystemStats.FormatBytes(s.RamFreed)} آزاد شد" : "بدون تغییر"
                });

            if (s.IsBoostEnabled("dnsFlush") && !HasFailure("dns_flush_fail"))
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "dns_flush",
                    Title = "تخلیه کش DNS و نوسازی کانکشن ها", IconGlyph = "\uE774",
                    StatusSummary = "با موفقیت نوسازی شد ✓"
                });

            if (s.ExtFreed > 0)
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "browser_opt",
                    Title = "بهینه سازی مرورگرها و حذف افزونه های زائد", IconGlyph = "\uE774",
                    FreedBytes = s.ExtFreed,
                    StatusSummary = $"+ {SystemStats.FormatBytes(s.ExtFreed)} آزاد شد ✓"
                });

            if (s.DisabledStartups > 0)
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "startup_opt",
                    Title = $"بهینه سازی استارتاپ ({s.DisabledStartups} برنامه غیرضروری)", IconGlyph = "\uE7B5",
                    StatusSummary = "غیرفعال شد ✓"
                });

            if (s.GpuCount > 0)
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "gpu_opt",
                    Title = $"تخصیص کارت گرافیک مجزا ({s.GpuName})", IconGlyph = "\uE790",
                    StatusSummary = $"{s.GpuCount} برنامه بهینه سازی شد ✓"
                });

            if (s.BloatRemoved > 0)
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "bloat_clean",
                    Title = $"حذف خودکار برنامه ها و بازی های تبلیغاتی ({s.BloatRemoved} برنامه)", IconGlyph = "\uE74C",
                    FreedBytes = s.BloatFreed,
                    StatusSummary = s.BloatFreed > 0 ? $"+ {SystemStats.FormatBytes(s.BloatFreed)} آزاد شد ✓" : "پاکسازی شد ✓"
                });

            if (s.BorderlineRemoved > 0)
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "borderline_clean",
                    Title = $"پاکسازی برنامه های اختیاری تاییدشده ({s.BorderlineRemoved} برنامه)", IconGlyph = "\uE74C",
                    FreedBytes = s.BorderlineFreed,
                    StatusSummary = s.BorderlineFreed > 0 ? $"+ {SystemStats.FormatBytes(s.BorderlineFreed)} آزاد شد ✓" : "پاکسازی شد ✓"
                });

            if (s.WinTweaks > 0)
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "win_tweaks",
                    Title = "توقف تله متری، افزایش سرعت سرچ، اکسپلورر و تبلیغات ویندوز", IconGlyph = "\uE770",
                    StatusSummary = "اعمال شد ✓"
                });

            if (s.FastStartup != null) group.Items.Add(s.FastStartup);

            if (s.RegCleaned > 0 || s.LeftoverFreed > 0)
                group.Items.Add(new CleanupReportItem
                {
                    GroupId = "system_boost", Id = "deep_clean_leftovers",
                    Title = $"پاکسازی عمیق ({s.RegCleaned} کلید نامعتبر رجیستری + {s.LeftoverFolders} پوشه رهاشده)",
                    IconGlyph = "\uE74D",
                    FreedBytes = s.LeftoverFreed,
                    WasEmpty = s.RegCleaned == 0 && s.LeftoverFreed == 0,
                    StatusSummary = s.LeftoverFreed > 0
                        ? $"+ {SystemStats.FormatBytes(s.LeftoverFreed)} آزاد شد ✓"
                        : "کلیدهای نامعتبر پاکسازی شدند ✓"
                });

            group.Items.AddRange(s.FailedSteps);
        }

        /// <summary>لغو اجرا (کاربر یا ترِی)؛ به JS هم خبر می‌دهد تا انیمیشن هیرو بایستد.</summary>
        internal void HeroAbortRun()
        {
            _heroSession = null;
            _isBusy = false;
            if (LiveCleanupCard != null) LiveCleanupCard.Visibility = Visibility.Collapsed;
            _activeCleaningItems.Clear();
        }

        // ====================================================================
        // Scan / borderline / codex / quick actions
        // ====================================================================

        /// <summary>پویش دوباره — توسط دکمهٔ «بررسی مجدد» هیرو صدا زده می‌شود.</summary>
        internal async Task HeroRescanAsync()
        {
            ApplyHeroScanResult(await GetHeroScanAsync());
        }

        /// <summary>کاندیداهای مرزی را برمی‌گرداند و ایندکس‌ها را برای مرحلهٔ ۱۱ کش می‌کند.</summary>
        internal async Task<List<HeroBorderlineItem>> HeroGetBorderlineAsync()
        {
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
                        Text = "این برنامه‌ها ممکن است به دردت بخورند؛ هر کدام را خواستی انتخاب کن.",
                        TextWrapping = TextWrapping.Wrap
                    });
                    root.Children.Add(new ScrollViewer { MaxHeight = 340, Content = panel });

                    var dialog = new ContentDialog
                    {
                        XamlRoot = Content.XamlRoot,
                        Title = "برنامه‌های مرزی پیدا شد",
                        Content = root,
                        PrimaryButtonText = "حذف انتخاب‌شده‌ها",
                        CloseButtonText = "انصراف",
                        DefaultButton = ContentDialogButton.Close
                    };

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
                    Text = $"خلق و خو: {Str(d, "mood")} · {Int(d, "days")} روز حضور",
                    TextWrapping = TextWrapping.Wrap
                });
                root.Children.Add(new TextBlock
                {
                    Text = $"{Int(d, "found")} از {Int(d, "total")} کشف شده · {Int(d, "weekRuns")} اجرا در هفته",
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
                    Title = "دفترچه کشفیات",
                    Content = new ScrollViewer { MaxHeight = 420, Content = root },
                    CloseButtonText = "بستن",
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
                        break;
                    case "ram":
                        await OptimizerService.BoostRamAsync();
                        break;
                    case "browser":
                        await BrowserOptimizerService.SuppressBrowserBackgroundModeAsync();
                        await Task.Run(BrowserOptimizerService.CleanInactiveChromeExtensions);
                        _heroBrowserMb = 0;
                        break;
                    case "cpu":
                        _heroTopCpu = "";
                        break;
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
    }
}
