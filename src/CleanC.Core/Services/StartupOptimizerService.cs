using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class StartupItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty; // "HKCU", "HKLM", "HKLM (32-bit)", "UserStartupFolder", "CommonStartupFolder", "WindowsService"
        /// <summary>نام کلید سرویس در رجیستری (فقط Location=WindowsService). از DisplayName فرق دارد.</summary>
        public string ServiceKey { get; set; } = string.Empty;
        /// <summary>مقدار اولیه پیش از تغییر (مثلاً مقدار Start سرویس) تا بازگردانی دقیق باشد.</summary>
        public string OriginalValue { get; set; } = string.Empty;
        /// <summary>
        /// نوع مقدار رجیستری پیش از حذف (`String` یا `ExpandString`).
        /// اگر ذخیره نشود، بازگردانی `REG_EXPAND_SZ` را به `REG_SZ` تنزل می دهد
        /// و متغیرهایی مثل <c>%windir%</c> دیگر expand نمی شوند.
        /// </summary>
        public string OriginalKind { get; set; } = nameof(RegistryValueKind.String);
        public bool IsNonEssential { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsVpnOrProxy { get; set; }
        public string Category { get; set; } = "برنامه کاربردی (اختیاری)";

        // ====================================================================
        // A10: منابعی که قبلاً اصلاً دیده نمی شدند
        // ====================================================================
        /// <summary>آیا این مورد از `RunOnce` است؟ (یک بار اجرا می شود و بعد پاک می شود)</summary>
        public bool IsRunOnce { get; set; }
        /// <summary>آیا این مورد یک تسکِ Task Scheduler با تریگر Logon/Boot است؟</summary>
        public bool IsScheduledTask { get; set; }
        /// <summary>آیا این مورد StartupTaskِ یک پکیج MSIX/UWP است؟</summary>
        public bool IsUwpStartupTask { get; set; }

        // ====================================================================
        // F6: Impact Score — از لاگ واقعیِ بوت اندازه گیری می شود
        // ====================================================================
        /// <summary>امتیاز تأثیر روی بوت، از ۰ (بی اثر) تا ۱۰۰ (بحرانی).</summary>
        public int ImpactScore { get; set; }
        /// <summary>برچسب نمایشی: «کم» / «متوسط» / «زیاد».</summary>
        public string ImpactLabel { get; set; } = string.Empty;
        /// <summary>
        /// آیا امتیاز از لاگ `Microsoft-Windows-Diagnostics-Performance` آمده؟
        /// اگر false باشد یعنی ویندوز هنوز برای این برنامه داده ای ثبت نکرده و امتیاز تخمینی است.
        /// </summary>
        public bool ImpactMeasured { get; set; }
        /// <summary>میلی ثانیه تأخیرِ واقعیِ ثبت شده در لاگ بوت (فقط وقتی <see cref="ImpactMeasured"/>).</summary>
        public long ImpactMs { get; set; }
    }

    public class StartupOptimizerService
    {
        private const string CleanCBackupKey = @"Software\CleanC\DisabledStartup";

        // Comprehensive VPN & Proxy client signatures (Local & International)
        private static readonly string[] VpnSignatures = new[]
        {
            "warp", "cloudflare", "wireguard", "openvpn", "v2ray", "v2rayn", "xray",
            "sing-box", "singbox", "hiddify", "clash", "clash-verge", "clash verge",
            "flclash", "mihomo", "nekoray", "nekobox", "karing", "amnezia", "shadowsocks",
            "psiphon", "outline", "windscribe", "surfshark", "nordvpn", "expressvpn",
            "protonvpn", "proton vpn", "x-vpn", "xvpn", "hotspot shield", "hotspotshield",
            "mullvad", "tailscale", "zerotier", "cisco anyconnect", "anyconnect",
            "forticlient", "fortinet", "kerio", "softether", "lantern", "ultrasurf",
            "freegate", "geph", "tor browser", "tor.exe", "privado", "purevpn", "cyberghost"
        };

        // Safe auto-disable targets during 1-Click Quick Clean (updaters and bloatware helpers ONLY)
        private static readonly string[] SafeAutoDisableSignatures = new[]
        {
            "googleupdate", "edgeupdate", "jusched", "adobearm", "adobe updater", "acrotray",
            "ccleaner", "driver booster", "driverbooster", "iobit", "advanced systemcare", "ituneshelper",
            "hp support assistant", "dell supportassist", "acer care center", "lenovosystemupdate"
        };

        // Whitelist signatures (hardware drivers, keyboard lighting, audio, touchpad, security) - NEVER TOUCH
        private static readonly string[] WhitelistSignatures = new[]
        {
            // Core Security
            "security", "defender", "securityhealth", "antivirus",

            // Audio Drivers
            "realtek", "conexant", "waves", "smartaudio", "audio", "sound", "nahimic",

            // Touchpad, Input & Keyboard
            "synaptics", "elan", "alps", "touchpad", "trackpoint",

            // Hardware Lighting & Performance Controllers
            "dragon center", "msi center", "mystic light", "alienware", "armoury crate",
            "legion space", "backlight", "keyboard", "fan control",

            // Graphics & Display
            "nvidia", "nvbackend", "nvdisplay", "amd", "radeon", "intel", "igfx",

            // Network Hardware
            "bluetooth", "wireless", "wlan", "wifi", "ethernet"
        };

        /// <param name="includeExtraSources">
        /// منابع اضافه (RunOnce/StartupTask MSIX/تسک‌های Logon-Boot با schtasks) فقط برای
        /// نمایشِ کاملِ لیستِ مدیر استارتاپ لازم است؛ در بهینه‌سازی خودکار این موارد
        /// عمداً دست‌نخورده می‌مانند و پیمایشِ گران‌شان (schtasks حدود چند ثانیه) مصرف
        /// ندارد — درخواستِ فاز ۱۹: اجرای کامل هیرو چند ثانیه در «مرتب‌سازی استارتاپ» معطل نفماند.
        /// </param>
        public static List<StartupItem> GetStartupItems(bool includeExtraSources = true)
        {
            var list = new List<StartupItem>();

            // 1. HKCU Run
            ScanRegistryRunKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU", list);

            // 2. HKLM Run (64-bit native)
            ScanRegistryRunKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM", list);

            // 3. HKLM WOW6432Node Run (32-bit apps on 64-bit Windows - very common for VPNs and tools!)
            ScanRegistryRunKey(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM (32-bit)", list);

            // 4. User Startup Folder
            string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            ScanStartupFolder(userStartup, "UserStartupFolder", list);

            // 5. Common Startup Folder
            string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            ScanStartupFolder(commonStartup, "CommonStartupFolder", list);

            // 6. VPN Background Windows Services (Automatic Start)
            ScanVpnWindowsServices(list);

            // 6.5. A10: RunOnce + StartupTaskهای MSIX + تسک های Logon/Boot
            //      (قبل از ScanCleanCDisabledItems تا آیتم های غیرفعال شده توسط خودمان دوباره اضافه نشوند)
            if (includeExtraSources)
            {
                StartupExtraSources.Scan(list);
            }

            // 7. Items previously disabled and backed up by CleanC
            ScanCleanCDisabledItems(list);

            // F6: امتیاز تأثیر روی بوت را برای همه ی موارد محاسبه کن
            var impacts = BootImpactService.GetBootImpacts();
            foreach (var item in list)
            {
                BootImpactService.AssignImpact(item, impacts);
            }

            return list;
        }

        public static async Task<int> OptimizeStartupAppsAsync(bool aggressive = false)
        {
            return await Task.Run(() =>
            {
                int disabledCount = 0;
                // بدون منابع اضافه: RunOnce/تسک زمان‌بندی‌شده/StartupTask MSIX در همین حلقه
                // عمداً رد می شوند؛ طلبِ چند ثانیه‌ایِ schtasks برایشان بیهوده است
                var items = GetStartupItems(includeExtraSources: false);

                foreach (var item in items)
                {
                    // Never silently disable VPNs or Chat apps during Quick Boost!
                    if (item.IsVpnOrProxy || item.Category.Contains("پیام رسان")) continue;

                    // A10: RunOnce / تسک های زمان بندی شده / StartupTaskهای MSIX عمداً
                    // وارد بهینه سازی خودکار نمی شوند؛ غیرفعال کردنشان باید دستی و آگاهانه باشد
                    // چون برخلاف مقدارِ Run، خاموش کردنشان ممکن است سرویسی را از کار بیندازد.
                    if (item.IsRunOnce || item.IsScheduledTask || item.IsUwpStartupTask) continue;

                    bool candidate = aggressive
                        ? (item.IsNonEssential && !item.IsDisabled)
                        : (!item.IsDisabled && IsSafeToAutoDisable(item.Name, item.Command));

                    if (candidate)
                    {
                        if (DisableStartupItem(item))
                        {
                            disabledCount++;
                        }
                    }
                }

                return disabledCount;
            });
        }

        public static bool DisableStartupItem(StartupItem item)
        {
            if (item == null) return false;

            string serviceKey = item.ServiceKey;
            string originalValue = item.OriginalValue;
            // نوع مقدار (REG_SZ در برابر REG_EXPAND_SZ) باید قبل از حذف خوانده شود
            // تا بازگردانی دقیق باشد؛ وگرنه متغیرهایی مثل %windir% expand نمی شوند.
            string originalKind = nameof(RegistryValueKind.String);
            bool disabled = false;

            try
            {
                // Disable اول، بکاپ بعد — تا اگر دسترسی نبود، رکورد دروغین ساخته نشود
                if (item.Location == "HKCU")
                {
                    using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    if (runKey == null) return false;
                    string current = runKey.GetValue(item.Name)?.ToString() ?? "";
                    originalValue = current;
                    originalKind = TryGetValueKind(runKey, item.Name);
                    runKey.DeleteValue(item.Name, false);
                    disabled = runKey.GetValue(item.Name) == null;
                }
                else if (item.Location == "HKLM")
                {
                    using var runKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    if (runKey == null) return false; // بدون دسترسی Administrator
                    originalValue = runKey.GetValue(item.Name)?.ToString() ?? "";
                    originalKind = TryGetValueKind(runKey, item.Name);
                    runKey.DeleteValue(item.Name, false);
                    disabled = runKey.GetValue(item.Name) == null;
                }
                else if (item.Location == "HKLM (32-bit)")
                {
                    using var runKey = Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", true);
                    if (runKey == null) return false;
                    originalValue = runKey.GetValue(item.Name)?.ToString() ?? "";
                    originalKind = TryGetValueKind(runKey, item.Name);
                    runKey.DeleteValue(item.Name, false);
                    disabled = runKey.GetValue(item.Name) == null;
                }
                else if (item.Location == "WindowsService")
                {
                    string svcPath = string.IsNullOrEmpty(serviceKey)
                        ? null!
                        : $@"SYSTEM\CurrentControlSet\Services\{serviceKey}";
                    if (string.IsNullOrEmpty(svcPath)) return false;

                    using var svcKey = Registry.LocalMachine.OpenSubKey(svcPath, true);
                    if (svcKey == null) return false;

                    int currentStart = Convert.ToInt32(svcKey.GetValue("Start") ?? 3);
                    originalValue = currentStart.ToString();
                    // از Automatic(2) به Manual(3) می بریم؛ اگر از قبل غیرفعال/دستی بود، کاری لازم نیست
                    if (currentStart == 3 || currentStart == 4) return false;

                    svcKey.SetValue("Start", 3, RegistryValueKind.DWord); // 3 = Manual (Demand start)
                    disabled = Convert.ToInt32(svcKey.GetValue("Start") ?? -1) == 3;
                }
                else if (item.Location == "UserStartupFolder" || item.Location == "CommonStartupFolder")
                {
                    if (!File.Exists(item.Command)) return false;

                    string disabledPath = item.Command + ".disabled";
                    if (File.Exists(disabledPath)) File.Delete(disabledPath);
                    File.Move(item.Command, disabledPath);
                    disabled = File.Exists(disabledPath) && !File.Exists(item.Command);
                }
                // A10: RunOnce — مثل Run فقط در کلید دیگر
                else if (item.Location == "HKCU RunOnce" || item.Location == "HKLM RunOnce")
                {
                    var runRoot = item.Location == "HKCU RunOnce" ? Registry.CurrentUser : Registry.LocalMachine;
                    using var runKey = runRoot.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", true);
                    if (runKey == null) return false;
                    originalValue = runKey.GetValue(item.Name)?.ToString() ?? "";
                    originalKind = TryGetValueKind(runKey, item.Name);
                    runKey.DeleteValue(item.Name, false);
                    disabled = runKey.GetValue(item.Name) == null;
                }
                // A10: تسک Logon/Boot — با schtasks غیرفعال می شود (نیازی به بکاپ نیست، همان enable مجدد کافی است)
                else if (item.Location == "TaskScheduler")
                {
                    if (string.IsNullOrWhiteSpace(serviceKey)) return false;
                    originalValue = "";
                    disabled = StartupExtraSources.RunSchtasks($"/change /tn \"{serviceKey}\" /disable", out _);
                }
                // A10: StartupTaskِ MSIX/UWP — مقدار State در SystemAppData
                else if (item.Location == "StartupTask")
                {
                    if (string.IsNullOrWhiteSpace(serviceKey)) return false; // PackageFamilyName\AppId
                    using var uwpKey = Registry.CurrentUser.CreateSubKey(
                        $@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\{serviceKey}", true);
                    if (uwpKey == null) return false;
                    originalValue = Convert.ToInt32(uwpKey.GetValue("State") ?? 1).ToString();
                    // 0 = Disabled (همان مقداری که Task Manager هم می نویسد)
                    uwpKey.SetValue("State", 0, RegistryValueKind.DWord);
                    disabled = Convert.ToInt32(uwpKey.GetValue("State") ?? -1) == 0;
                }
                else
                {
                    return false; // Location ناشناخته (مثلاً «غیرفعال (CleanC)»)
                }

                if (!disabled) return false;

                // Save backup to CleanC key for easy 1-click restore
                using (var bkKey = Registry.CurrentUser.CreateSubKey(CleanCBackupKey))
                {
                    if (bkKey != null)
                    {
                        // قالب v3: v3|Location|ServiceKey|OriginalValue|Command|OriginalKind
                        // (v2 و قالب قدیمی فقط برای خواندن رکوردهای موجود پشتیبانی می شوند)
                        string entryData = $"v3|{item.Location}|{serviceKey}|{originalValue}|{item.Command}|{originalKind}";
                        // کلید رکورد بکاپ پیش‌تر صرفاً Name بود؛ برنامه های هم‌نام در
                        // مکان های مختلف (HKCU/HKLM/…) بکاپ یکدیگر را بازنویسی می کردند
                        // و فعال‌سازی فقط یکی از آن ها بازگردانیِ درست داشت.
                        bkKey.SetValue(BuildBackupToken(item.Location, item.Name), entryData, RegistryValueKind.String);
                    }
                }

                return true;
            }
            catch
            {
                // Access denied or item locked
            }

            return false;
        }

        /// <summary>
        /// اسامی معتبر RegistryValueKind — برای تشخیص پسوند قالب v3
        /// </summary>
        private static readonly string[] KnownRegistryValueKinds =
        {
            nameof(RegistryValueKind.Binary), nameof(RegistryValueKind.DWord),
            nameof(RegistryValueKind.ExpandString), nameof(RegistryValueKind.MultiString),
            nameof(RegistryValueKind.QWord), nameof(RegistryValueKind.String),
            nameof(RegistryValueKind.None),
        };

        /// <summary>
        /// خواندن نوع مقدار یک value بدون پرتاب استثنا (اگر value نبود → String).
        /// </summary>
        private static string TryGetValueKind(RegistryKey key, string name)
        {
            try
            {
                if (key.GetValue(name) == null) return nameof(RegistryValueKind.String);
                return key.GetValueKind(name).ToString();
            }
            catch
            {
                return nameof(RegistryValueKind.String);
            }
        }

        /// <summary>
        /// اگر Command با یک نوع معتبر رجیستری تمام شده باشد، آن را برمی دارد
        /// (چون خود Command ممکن است شامل «|» باشد، از انتها تشخیص می دهیم).
        /// </summary>
        private static string TakeTrailingKind(ref string command)
        {
            int idx = command.LastIndexOf('|');
            if (idx > 0)
            {
                string tail = command.Substring(idx + 1);
                foreach (var kind in KnownRegistryValueKinds)
                {
                    if (string.Equals(tail, kind, StringComparison.OrdinalIgnoreCase))
                    {
                        command = command.Substring(0, idx);
                        return kind;
                    }
                }
            }
            return nameof(RegistryValueKind.String);
        }

        private static RegistryValueKind ParseKind(string kind)
        {
            if (string.Equals(kind, nameof(RegistryValueKind.ExpandString), StringComparison.OrdinalIgnoreCase))
                return RegistryValueKind.ExpandString;
            return RegistryValueKind.String;
        }

        /// <summary>
        /// پارس رکورد بکاپ.
        /// قالب فعلی: v3|Location|ServiceKey|OriginalValue|Command|OriginalKind
        /// قالب قبلی: v2|Location|ServiceKey|OriginalValue|Command
        /// قالب قدیمی: Location|Command
        /// </summary>
        private static bool TryParseBackup(string data, out string location, out string serviceKey,
            out string originalValue, out string command, out string originalKind)
        {
            location = string.Empty;
            serviceKey = string.Empty;
            originalValue = string.Empty;
            command = string.Empty;
            originalKind = nameof(RegistryValueKind.String);

            if (string.IsNullOrWhiteSpace(data)) return false;

            var parts = data.Split('|', 5);
            if (parts.Length == 5 && (parts[0] == "v3" || parts[0] == "v2"))
            {
                location = parts[1];
                serviceKey = parts[2];
                originalValue = parts[3];
                command = parts[4];
                if (parts[0] == "v3") originalKind = TakeTrailingKind(ref command);
                return true;
            }

            var legacy = data.Split('|', 2);
            if (legacy.Length < 2) return false;
            location = legacy[0];
            command = legacy[1];
            return true;
        }

        // کلید رکورد بکاپ: «Location|Name» تا هم‌نام‌ها از هم جدا بمانند
        private static string BuildBackupToken(string location, string name)
            => $"{location}|{name}";

        public static bool RestoreStartupItem(string itemName, string? location = null)
        {
            try
            {
                using var bkKey = Registry.CurrentUser.OpenSubKey(CleanCBackupKey, true);
                if (bkKey == null) return false;

                // رکوردهای جدید با کلید «Location|Name» ذخیره شده اند؛ رکوردهای
                // نسخه های قبلی فقط «Name» — اول جدید و بعد قدیمی جستجو می شود
                string recordKey = itemName;
                if (!string.IsNullOrEmpty(location))
                {
                    string token = BuildBackupToken(location, itemName);
                    string? tokenVal = bkKey.GetValue(token) as string;
                    if (!string.IsNullOrEmpty(tokenVal))
                    {
                        recordKey = token;
                    }
                }
                string? val = bkKey.GetValue(recordKey) as string;
                if (string.IsNullOrEmpty(val)) return false;

                if (!TryParseBackup(val, out string bkLocation, out string serviceKey, out string originalValue,
                        out string command, out string originalKind))
                    return false;

                // نوع مقدار اصلی (REG_EXPAND_SZ در برابر REG_SZ) را حفظ می کنیم
                RegistryValueKind kind = ParseKind(originalKind);

                if (bkLocation == "HKCU")
                {
                    using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                    if (runKey == null) return false;
                    runKey.SetValue(itemName, command, kind);
                }
                else if (bkLocation == "HKLM")
                {
                    using var runKey = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                    if (runKey == null) return false;
                    runKey.SetValue(itemName, command, kind);
                }
                else if (bkLocation == "HKLM (32-bit)")
                {
                    using var runKey = Registry.LocalMachine.CreateSubKey(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run");
                    if (runKey == null) return false;
                    runKey.SetValue(itemName, command, kind);
                }
                else if (bkLocation == "WindowsService")
                {
                    // ServiceKey واقعی نام کلید است، نه DisplayName
                    string keyName = string.IsNullOrEmpty(serviceKey) ? itemName : serviceKey;
                    using var svcKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{keyName}", true);
                    if (svcKey == null) return false;

                    // مقدار اولیه را برگردان؛ فقط در صورت نبود اطلاعات، Automatic قرار بده
                    int restoreStart = int.TryParse(originalValue, out int ov) ? ov : 2;
                    svcKey.SetValue("Start", restoreStart, RegistryValueKind.DWord);
                }
                else if (bkLocation == "UserStartupFolder" || bkLocation == "CommonStartupFolder")
                {
                    string disabledPath = command + ".disabled";
                    if (File.Exists(disabledPath))
                    {
                        if (File.Exists(command)) File.Delete(command);
                        File.Move(disabledPath, command);
                    }
                }
                // A10
                else if (bkLocation == "HKCU RunOnce" || bkLocation == "HKLM RunOnce")
                {
                    var runRoot = bkLocation == "HKCU RunOnce" ? Registry.CurrentUser : Registry.LocalMachine;
                    using var runKey = runRoot.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce");
                    if (runKey == null) return false;
                    runKey.SetValue(itemName, command, kind);
                }
                else if (bkLocation == "TaskScheduler")
                {
                    if (string.IsNullOrWhiteSpace(serviceKey)) return false;
                    if (!StartupExtraSources.RunSchtasks($"/change /tn \"{serviceKey}\" /enable", out _)) return false;
                }
                else if (bkLocation == "StartupTask")
                {
                    if (string.IsNullOrWhiteSpace(serviceKey)) return false;
                    using var uwpKey = Registry.CurrentUser.CreateSubKey(
                        $@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\{serviceKey}", true);
                    if (uwpKey == null) return false;
                    int restoreState = int.TryParse(originalValue, out int st) ? st : 1;
                    uwpKey.SetValue("State", restoreState, RegistryValueKind.DWord);
                }
                else
                {
                    return false;
                }

                bkKey.DeleteValue(recordKey, false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ScanRegistryRunKey(RegistryKey root, string subKeyPath, string locationName, List<StartupItem> list)
        {
            try
            {
                using var key = root.OpenSubKey(subKeyPath, false);
                if (key == null) return;

                foreach (var valName in key.GetValueNames())
                {
                    string? cmd = key.GetValue(valName)?.ToString();
                    if (string.IsNullOrWhiteSpace(cmd)) continue;

                    bool isProtected = IsWhitelisted(valName, cmd);
                    bool isVpn = IsVpnApp(valName, cmd);
                    bool isNonEssential = !isProtected;
                    string cat = ResolveStartupCategory(valName, cmd, isProtected);

                    bool isDisabledInTaskManager = IsDisabledInStartupApproved(root, valName);

                    list.Add(new StartupItem
                    {
                        Id = $"{locationName}_{valName}",
                        Name = valName,
                        Command = cmd,
                        Location = locationName,
                        IsNonEssential = isNonEssential,
                        IsDisabled = isDisabledInTaskManager,
                        IsVpnOrProxy = isVpn,
                        Category = cat
                    });
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private static bool IsDisabledInStartupApproved(RegistryKey root, string valName)
        {
            try
            {
                using var saKey = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", false);
                if (saKey != null)
                {
                    var bytes = saKey.GetValue(valName) as byte[];
                    if (bytes != null && bytes.Length > 0)
                    {
                        // In Windows 10/11 Task Manager: odd byte = disabled (0x01, 0x03, etc.)
                        return (bytes[0] % 2 != 0);
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return false;
        }

        private static bool IsDisabledInStartupApprovedFolder(string fileName)
        {
            try
            {
                var roots = new[] { Registry.CurrentUser, Registry.LocalMachine };
                foreach (var root in roots)
                {
                    using var saKey = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", false);
                    if (saKey != null)
                    {
                        var bytes = saKey.GetValue(fileName) as byte[];
                        if (bytes != null && bytes.Length > 0 && (bytes[0] % 2 != 0))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return false;
        }

        // فایل های سیستمی پوشه Startup که هرگز نباید به عنوان آیتم اجرا نمایش داده شوند
        private static readonly string[] StartupFolderSystemFiles = new[]
        {
            "desktop.ini", "thumbs.db", "folder.hta", "ntuser.dat", "ntuser.ini"
        };

        private static void ScanStartupFolder(string folderPath, string locationName, List<StartupItem> list)
        {
            try
            {
                if (!Directory.Exists(folderPath)) return;

                foreach (var file in Directory.GetFiles(folderPath, "*.*"))
                {
                    string fname = Path.GetFileName(file);

                    // desktop.ini و همتایانش آیتم اجرا نیستند
                    if (StartupFolderSystemFiles.Any(s => s.Equals(fname, StringComparison.OrdinalIgnoreCase))) continue;

                    bool isDisabledInApproved = IsDisabledInStartupApprovedFolder(fname);
                    bool isRenamedDisabled = fname.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    bool isDisabled = isRenamedDisabled || isDisabledInApproved;
                    string cleanName = isRenamedDisabled ? fname.Substring(0, fname.Length - ".disabled".Length) : fname;

                    // Command باید همیشه مسیر «اصلی» (بدون .disabled) باشد تا Disable/Restore درست کار کند
                    string originalPath = Path.Combine(folderPath, cleanName);

                    bool isProtected = IsWhitelisted(cleanName, originalPath);
                    bool isVpn = IsVpnApp(cleanName, originalPath);
                    bool isNonEssential = !isProtected;
                    string cat = ResolveStartupCategory(cleanName, originalPath, isProtected);

                    list.Add(new StartupItem
                    {
                        Id = $"{locationName}_{cleanName}",
                        Name = cleanName,
                        Command = originalPath,
                        Location = locationName,
                        IsNonEssential = isNonEssential,
                        IsDisabled = isDisabled,
                        IsVpnOrProxy = isVpn,
                        Category = cat
                    });
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private static void ScanCleanCDisabledItems(List<StartupItem> list)
        {
            try
            {
                using var bkKey = Registry.CurrentUser.OpenSubKey(CleanCBackupKey, false);
                if (bkKey == null) return;

                foreach (var valName in bkKey.GetValueNames())
                {
                    // رکوردهای جدید کلیدشان «Location|Name» است؛ نام نمایشی = بخش بعد از آخرین «|»
                    string displayName = valName;
                    int tokIdx = valName.LastIndexOf('|');
                    if (tokIdx > 0)
                        displayName = valName.Substring(tokIdx + 1);
                    if (displayName.Length == 0) continue;

                    if (list.Any(x => x.Name.Equals(displayName, StringComparison.OrdinalIgnoreCase))) continue;

                    string? data = bkKey.GetValue(valName)?.ToString();
                    if (string.IsNullOrWhiteSpace(data)) continue;

                    // serviceKey هم باید برگردد؛ برای تسک ها و StartupTaskها بدون آن غیرفعال سازیِ مجدد ناممکن است
                    if (!TryParseBackup(data, out string loc, out string skey, out string _, out string cmd, out string _))
                        continue;

                    bool isVpn = IsVpnApp(displayName, cmd);
                    string cat = ResolveStartupCategory(displayName, cmd, false);

                    list.Add(new StartupItem
                    {
                        Id = $"CleanC_{valName}",
                        Name = displayName,
                        Command = cmd,
                        Location = loc,
                        ServiceKey = skey,
                        IsRunOnce = loc == StartupExtraSources.LocationRunOnceUser || loc == StartupExtraSources.LocationRunOnceMachine,
                        IsScheduledTask = loc == StartupExtraSources.LocationTask,
                        IsUwpStartupTask = loc == StartupExtraSources.LocationUwp,
                        IsNonEssential = true,
                        IsDisabled = true,
                        IsVpnOrProxy = isVpn,
                        Category = cat
                    });
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        private static void ScanVpnWindowsServices(List<StartupItem> list)
        {
            try
            {
                using var servicesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", false);
                if (servicesKey == null) return;

                foreach (var subName in servicesKey.GetSubKeyNames())
                {
                    try
                    {
                        using var svc = servicesKey.OpenSubKey(subName, false);
                        if (svc == null) continue;

                        int startType = Convert.ToInt32(svc.GetValue("Start") ?? 3);
                        // Start = 2 (Automatic), 3 (Manual), 4 (Disabled)
                        if (startType < 2 || startType > 4) continue;

                        string dispName = svc.GetValue("DisplayName")?.ToString() ?? subName;
                        string imagePath = svc.GetValue("ImagePath")?.ToString() ?? "";

                        if (IsVpnApp(subName, $"{dispName} {imagePath}"))
                        {
                            list.Add(new StartupItem
                            {
                                Id = $"Service_{subName}",
                                Name = dispName,
                                Command = imagePath,
                                // باید دقیقاً با شاخه های Disable/Restore یکی باشد (قبلاً «سرویس ویندوز» بود و هیچ وقت disable نمی شد)
                                Location = "WindowsService",
                                // نام کلید سرویس، نه DisplayName — چون مسیر رجیستری از subName ساخته می شود
                                ServiceKey = subName,
                                OriginalValue = startType.ToString(),
                                IsNonEssential = true,
                                IsDisabled = (startType != 2),
                                IsVpnOrProxy = true,
                                Category = "فیلترشکن و پروکسی (VPN)"
                            });
                        }
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        public static string ResolveStartupCategory(string name, string cmd, bool isProtected)
        {
            if (isProtected) return "سخت افزار و سیستم (ضروری)";

            string lower = $"{name} {cmd}".ToLowerInvariant();

            if (IsVpnApp(name, cmd))
                return "فیلترشکن و پروکسی (VPN)";

            if (lower.Contains("telegram") || lower.Contains("discord") || lower.Contains("whatsapp") ||
                lower.Contains("skype") || lower.Contains("slack") || lower.Contains("teams") || lower.Contains("viber"))
                return "پیام رسان و چت (اختیاری)";

            if (lower.Contains("idm") || lower.Contains("idman") || lower.Contains("download") ||
                lower.Contains("torrent") || lower.Contains("utorrent") || lower.Contains("qbittorrent") ||
                lower.Contains("onedrive") || lower.Contains("dropbox") || lower.Contains("mega"))
                return "دانلود و ابزار ابری (اختیاری)";

            if (lower.Contains("steam") || lower.Contains("epic") || lower.Contains("riot") ||
                lower.Contains("origin") || lower.Contains("battle.net") || lower.Contains("uplay") || lower.Contains("galaxy"))
                return "لانچر بازی و گیمینگ (اختیاری)";

            if (lower.Contains("update") || lower.Contains("updater") || lower.Contains("helper") || lower.Contains("jusched"))
                return "آپدیتر پس زمینه (غیرضروری)";

            return "برنامه کاربردی (اختیاری)";
        }

        public static bool IsVpnApp(string name, string cmd)
        {
            string combined = $"{name} {cmd}".ToLowerInvariant();
            return VpnSignatures.Any(v => combined.Contains(v));
        }

        // A10: StartupExtraSources برای تشخیص ایمنیِ آیتم های RunOnce به همین سفیدفهرست نیاز دارد
        public static bool IsWhitelisted(string name, string cmd)
        {
            string combined = $"{name} {cmd}".ToLowerInvariant();
            return WhitelistSignatures.Any(w => combined.Contains(w));
        }

        private static bool IsSafeToAutoDisable(string name, string cmd)
        {
            string combined = $"{name} {cmd}".ToLowerInvariant();
            return SafeAutoDisableSignatures.Any(s => combined.Contains(s));
        }

        // ── F1: شروع خودکار همراه با ویندوز ─────────────────────────────────
        // منبع حقیقت «رجیستری» است نه AppSettings، چون کاربر می تواند همین آیتم را
        // از Task Manager هم غیرفعال کند؛ ذخیره در فایل تنظیمات باعث ناهمگامی می شد.
        private const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "CleanC";

        /// <summary>مسیر کلید رجیستری؛ فقط تست ها آن را به یک کلید موقت تغییر می دهند.</summary>
        private static string? _runKeyPathOverride;

        internal static void SetRunKeyPathOverrideForTests(string? path) => _runKeyPathOverride = path;

        private static string RunKeyPath => _runKeyPathOverride ?? DefaultRunKeyPath;

        /// <summary>
        /// مسیر اجرایی که در رجیستری ثبت می شود. فقط برای تست ست می شود تا
        /// تست ها بدون دست زدن به رجیستری واقعی اجرا شوند.
        /// </summary>
        private static string? _executableOverride;

        internal static void SetExecutableOverrideForTests(string? path) => _executableOverride = path;

        private static string GetCurrentExecutablePath()
        {
            if (!string.IsNullOrWhiteSpace(_executableOverride)) return _executableOverride!;

            // مسیر exe در حال اجرا؛ در unit-test این testhost است، پس fallback دارد
            string exe = Environment.ProcessPath ?? string.Empty;
            if (!string.IsNullOrEmpty(exe)) return exe;

            return Path.Combine(AppContext.BaseDirectory, "CleanC.UI.exe");
        }

        /// <summary>مقدار رجیستری را به مسیر فایل تبدیل می کند (نقل قول ها را برمی دارد).</summary>
        private static string NormalizeCommandPath(string raw)
        {
            string s = raw.Trim();
            if (s.Length > 1 && s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                if (end > 0) return s.Substring(1, end - 1);
            }
            return s;
        }

        /// <summary>آیا CleanC همراه با ورود به ویندوز ثبت شده است؟</summary>
        public static bool IsCleanCRegisteredAtStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                if (key?.GetValue(RunValueName) is not string registered || string.IsNullOrWhiteSpace(registered))
                    return false;

                return string.Equals(
                    NormalizeCommandPath(registered),
                    GetCurrentExecutablePath(),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "IsCleanCRegisteredAtStartup");
                return false;
            }
        }

        /// <summary>ثبت (یا حذف) CleanC در آیتم های اجرای خودکار ویندوز.</summary>
        public static bool SetCleanCStartupRegistration(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    string exe = GetCurrentExecutablePath();
                    // مسیر ناموجود را ثبت نکن؛ ویندوز در هر ورود خطا می دهد
                    if (!File.Exists(exe)) return false;
                }

                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
                if (key == null) return false;

                if (enabled)
                    key.SetValue(RunValueName, $"\"{GetCurrentExecutablePath()}\"", RegistryValueKind.String);
                else
                    key.DeleteValue(RunValueName, throwOnMissingValue: false);

                return true;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SetCleanCStartupRegistration");
                return false;
            }
        }
    }
}
