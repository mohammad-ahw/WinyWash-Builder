# CleanC — سند ادغام موتور تشخیص «موارد اضافی سیستم» (فاز ۶)

مسیر: `O:\OpenCode\New folder\CleanC - NZW\docs\DETECTION-ENGINE-SPEC.md`
وضعیت: پیش‌نویس فاز ۶ برای تأیید و پیاده‌سازی — تاریخ: 2026-10-18

---

## 1. هدف و دامنه

- ادغام منطق بریف ۱۸ دیتکتوری («Scan → Classify → Act») در اپ C# فعلی، با شرط اصلی کاربر:
  **هر مورد فایل/پوشه فقط پس از پیش‌نمایش و تأیید کاربر پاک می‌شود** (پیش‌فرض انتخاب: خاموش).
- دامنه: `CleanC.Core` + `CleanC.UI` در `O:\OpenCode\New folder\CleanC - NZW`.
- نسخه Node.js در `O:\Apps\CleanC` هدف ادغام نیست (نمونه اولیه قدیمی؛ ایرادهای `getDirStats` بدون محافظ reparse و `safeCleanFolder` با del/rd در آن باقی می‌ماند).
- اصول بریف که در اپ C# **از قبل رعایت شده**: پاکسازی شخصی از طریق سطل بازیافت/قرنطینه (`SafeDeleteService`)، عبور از reparse/junction (بخش ۵).

---

## 2. معماری فعلی اسکن/پاکسازی (نقاط اتصال)

| موتور | وظیفه | مسیر پاکسازی |
|---|---|---|
| `SystemScanService` | دسته‌های «Clean» (temp/cache/dumps/recycle/...) | `CleanService` → junk سیستمی: حذف دائمی |
| `DeepStorageCleanService` | Deep Cleanup (duplicates / empty folders / downloads mess / large files) | `SafeDeleteService` → Recycle Bin یا Quarantine + manifest |
| `RegistryCleanerService` | یتیم‌های رجیستری + سرویس‌های یتیم | حذف مستقیم کلید/disable سرویس |
| `StartupExtraSources` + `StartupOptimizerService` | Run keys / Startup folders / تسک‌های schtasks | Backup + disable/enable |
| `BloatwareService` + `AppUninstallerService` | برنامه‌های نصب‌شده/Appx + UserAssist | Uninstaller / Remove-AppxPackage |
| `LeftoverCleanerService` | داده‌های بازمانده برنامه‌های حذف‌شده | SafeDelete + ProtectedNames |
| `DriverManagerService` | بسته‌های تکراری/قدیمی DriverStore | pnputil + Restore Point |
| `HardwareDiagnosticService` | یتیم‌ها در قالب تشخیص سخت‌افزار (M08) | گزارش/اصلاح |
| `SafeDeleteService` | موتور مرکزی حذف امن | Recycle Bin (Microsoft.VisualBasic) + Quarantine |
| `SettingsService` | تنظیمات | `%LOCALAPPDATA%\CleanC\settings.json` |

---

## 3. جدول نگاشت دیتکتورها (D01–D18)

وضعیت‌ها: **موجود** / **ناقص** / **جدید**

| ID | دیتکتور | وضعیت | محل فعلی | کمبود / کار لازم | مقصد |
|---|---|---|---|---|---|
| D01 | کش مرورگرها (همه پروفایل‌ها) | موجود | `SystemScanService.cs:1075` (+462) | تأیید پوشش همه پروفایل‌ها + قفل «مرورگر در حال اجراست» | Clean (junk) |
| D02 | کش‌های سیستم (thumb/D3DS/INetCache) | موجود | `SystemScanService.cs:173,256,279,301` | بررسی افزودن کش شیدر NVIDIA/AMD | Clean (junk) |
| D03 | temp / log / dump | ناقص | CrashDumps: `SystemScanService.cs:69`، `DeepStorageCleanService.cs:30` | افزودن `C:\Windows\LiveKernelReports` (در آدیت واقعی ~1.15GB) | Clean (dumps) |
| D04 | بازمانده آپدیترها (فایل‌سطح) | جدید | فقط پروسه/سرویس: `ProcessManagerService.cs:69`, `StartupOptimizerService.cs:78` | اسکنر الگو: `*-updater`، `installer.exe`/`pending/`/`*.blockmap` الکترونی | Deep Cleanup |
| D05 | نصب‌کننده‌های قدیمی (تطبیق فازی) | ناقص | `DeepStorageCleanService.cs:48` + `SystemScanService.cs:706` | تطبیق با Uninstall registry — زیرساخت آماده: `LeftoverCleanerService.cs:356` | Deep Cleanup |
| D06 | دانلودهای ناتمام (.crdownload/.part) | موجود | `DeepStorageCleanService.cs:170` + `SystemScanService.cs:706` (>۳ روز) | هماهنگ‌سازی آستانه‌ها (24h / 72h) و شرط orphan | Deep Cleanup + Clean |
| D07 | فایل‌های تکراری | موجود (کامل) | `DeepStorageCleanService.cs:336-409` (size → hash سریع → hash کامل + wasted) | — | Deep Cleanup |
| D08 | کپی‌های نام‌دار «(1) / Copy» | موجود | `DeepStorageCleanService.cs:186-196` (`DuplicateCopy`) | — | Deep Cleanup |
| D09 | پوشه‌های خالی | ناقص | `DeepStorageCleanService.cs:61-104` (فقط Downloads/Desktop/Temp، یک سطح، بدون گیت زمانی) | بازگشتی + شرط >۳۰ روز + گسترش به Desktop/Documents/Videos/Music | Deep Cleanup |
| D10 | ریشه درایوها (استخراج‌شده‌های OEM) | جدید | — | allowlist + امضای سازنده (NVIDIA/Intel/AMD/MSI/Aomei) | Deep Cleanup |
| D11 | یتیم‌های رجیستری/سرویس | موجود | `RegistryCleanerService.cs:21,287` + `HardwareDiagnosticService.cs:158` | تأیید شده: `battlenet_helpersvc` واقعاً یتیم است (Stopped/Manual) | Registry/Tools |
| D12 | تسک/استارتاپ یتیم | ناقص | `StartupExtraSources.cs:190` (فقط اسکن/نمایش) | تشخیص هدف ناموجود (exe گمشده) + disable با evidence (پیش‌فرض خاموش) | Startup |
| D13 | Appx bloat | ناقص | `BloatwareService.cs:217,586` (UserAssist ROT13) | تفکیک provisioned vs installed + blocklist بحرانی (VCLibs/UI.Xaml/WindowsAppRuntime/Store) | Apps |
| D14 | درایور/OEM یتیم | ناقص | `DriverManagerService.cs:66` (فقط duplicate DriverStore) | کراس‌چک با حضور سخت‌افزار (PnP) — مثال آدیت: ENE/WD_BLACK | Drivers |
| D15 | داده برنامه‌های حذف‌شده | موجود | `LeftoverCleanerService.cs` (signatures + `ProtectedNames:21-65`) | — | Clean/Leftover |
| D16 | sync / دستگاه (بکاپ موبایل) | ناقص | `DeepStorageCleanService.cs:298`، `SystemScanService.cs:547` | افزودن `CrossDevice` + بازمانده‌های OneDrive | Deep Cleanup |
| D17 | نظم‌دهی + فایل‌های حساس | ناقص | Downloads mess موجود؛ فایل حساس: ندارد | فایل‌های حساس (کلید/سریال) فقط **report-only** | Deep Cleanup |
| D18 | آدیت نرم‌افزار نصب‌شده | ناقص | `BloatwareService` + `AppUninstallerService` (UserAssist موجود) | تشخیص trial (اختیاری) | Apps |

---

## 4. کارهای فاز ۶ (جمع‌بندی اجرایی)

### A) زیرساخت ایمنی (پیش‌نیاز همه)
1. **فایل قفل ماشین‌خوان** `%LOCALAPPDATA%\CleanC\locks.json` با ساختار `{path, lockType, reason, createdAt}` — معادل فعلی: لیست‌های هاردکد (`LeftoverCleanerService.cs:21-65`، `CleanService.cs:239`).
2. افزودن فیلدهای `requiresAdmin` / `confidence` / `evidence[]` به مدل‌های دیتکتور جدید (طبق قرارداد JSON بریف).
3. ~~تأیید/افزودن Restore Point قبل از پاکسازی Deep (فعلاً فقط DriverManager دارد).~~ **تصمیم نهایی ۲۰۲۶/۱۰/۱۸: اضافه نمی‌شود** — چرخه‌ی Deep برای فایل‌های کاربر از سطل بازیافت/قرنطینه (`SafeDeleteService`) رد می‌شود که مؤثرتر از System Restore است (Restore Point فقط سیستم/رجیستری را برمی‌گرداند، نه فایل کاربر). عملیات DriverStore همچنان Restore Point خودکار دارد (`MainWindow.Tools.cs`).

### B) دیتکتورهای جدید
- D04 (بازمانده آپدیترها)، D10 (ریشه درایوها/OEM)، D17 (فایل‌های حساس report-only)، D03-افزودنی (LiveKernelReports).

### C) تکمیل دیتکتورهای موجود
- D05 (تطبیق فازی با registry)، D09 (بازگشتی + گیت زمانی)، D12 (هدف ناموجود)، D13 (provisioned)، D14 (PnP cross-check)، D16 (CrossDevice).

### D) UI و تجربه کاربر
- دسته‌های جدید در تب Deep Cleanup با الگوی موجود «پیش‌نمایش + انتخاب‌نشده به‌صورت پیش‌فرض + پاکسازی با تأیید».
- برچسب سایز قابل بازپس‌گیری و دلیل (evidence) برای هر آیتم.

### E) تست
- تست واحد برای هر دیتکتور جدید + تست Acceptance با ground truth واقعی (بخش ۶).

---

## 5. قواعد ایمنی سراسری — وضعیت فعلی اپ

| قاعده بریف | وضعیت در اپ C# | مرجع |
|---|---|---|
| بدون حذف مستقیم؛ سطل بازیافت/قرنطینه | رعایت شده | `SafeDeleteService.cs:357-359` |
| عبور از reparse/junction | رعایت شده | `DeepStorageCleanService.cs:38`، `LargeFileScannerService.cs:41`، `LeftoverCleanerService.cs:140,194,262`، `CleanService.cs:184` |
| junk سیستمی = حذف دائمی (طبق طراحی) | رعایت شده | متن `DeleteBehaviorSub` در Localization |
| Restore Point قبل از عملیات | فقط درایورها (Deep: سطل/قرنطینه به‌جای آن — تصمیم ۲۰۲۶/۱۰/۱۸) | `DriverManagerService` + متن بند ۴-الف-۳ |
| قفل‌ها (locks) | ناقص — لیست‌های هاردکد | نیاز به `locks.json` |
| آستانه‌ها (24h/72h) | ناهماهنگ | D06 |
| requiresAdmin | پراکنده (UAC manifest اپ) | در دیتکتورهای جدید صریح شود |

---

## 6. معیار پذیرش (Acceptance — از آدیت واقعی سیستم کاربر)

**باید پیدا شود:**
- ۳ نصب‌کننده تکراری NVIDIA (~945MB هر کدام، ~1.9GB اتلاف) — D07/D05
- `C:\Windows\LiveKernelReports` (~1.15GB) — D03
- `C:\NVIDIA` (~655MB)، `C:\Aomei` (~791MB)، بازمانده‌های `C:\Intel` / `C:\MSI` — D10
- نصب‌کننده‌های قدیمی Downloads — D05
- `battlenet_helpersvc` یتیم (تأیید شد: Stopped/Manual) + بازمانده‌های `C:\ProgramData\Battle.net_components` — D11/D15
- ~۱۰ درایور ENE/WD_BLACK بدون سخت‌افزار + بسته Thunderbolt قدیمی — D14
- Appx bloat (بازی/پرومو/CyberLink/MAGIX) — D13
- نرم‌افزار بدون استفاده (Apple Mobile Device/Bonjour، X-VPN، JDK 8) — D18

**باید هرگز پیشنهاد/لمس نشود:**
- اکوسیستم MSI/Nahimic/Dragon Center (کنترل فن لپ‌تاپ)، اپ‌ها/تسک‌های HP (پرینتر وصل است)
- Duet Display (در حال استفاده)، بازی‌ها (~138GB)
- عکس‌های شخصی، حتی با نام (1)(2)؛ فایل‌های شخصی در ریشه درایو
- pagefile / hiberfil، فایل کلید فعال‌سازی/سریال
- هر مورد user-locked

---

## 7. مراحل پیاده‌سازی فاز ۶

1. `locks.json` + فیلدهای evidence/requiresAdmin در مدل‌ها (زیرساخت).
2. دیتکتورهای جدید B در `DeepStorageCleanService` + مدل‌ها + دسته‌بندی UI.
3. تکمیل C در سرویس‌های مربوطه.
4. اتصال UI (پیش‌نمایش + تأیید) + متن‌های Localization (fa/en).
5. تست‌ها + Acceptance با ground truth.

---

## 8. تصمیمات نهایی (تأییدشده توسط کاربر — 2026-10-18)

1. **فایل قفل ماشین‌خوان**: `%LOCALAPPDATA%\CleanC\locks.json` جدید ساخته می‌شود (ساختار بند ۴-الف). قفل/آنلاک از UI قابل انجام است؛ لیست‌های هاردکد فعلی به قفل‌های پیش‌فرض منتقل می‌شوند.
2. **پیش‌فرض دیتکتورها**: D10 و D12 با مدرک قطعی پیش‌فرض روشن (T2). D04 و D16 **همیشه پیش‌فرض خاموش** و فقط با تیک دستی کاربر فعال می‌شوند (T3).
3. **`LiveKernelReports`**: تب Clean — حذف دائمی.
4. **تشخیص trial (D18)**: در همین فاز پیاده می‌شود — فقط نمایش badge در تب Apps، بدون حذف خودکار.
5. **تأیید حذف تکی نرم‌افزار (D5)**: دیالوگ تأیید صریح اضافه شد — هم‌سان با حذف گروهی.
6. **Restore Point دیپ‌کلین**: اضافه نمی‌شود — سطل بازیافت/قرنطینه محافظت مؤثرتر و متناسب‌تری برای فایل‌های کاربر است.

وضعیت سند: تأییدشده — **فاز ۶ پیاده‌سازی و Acceptance شد (۲۰۲۶/۱۰/۱۸)؛ همه‌ی بندها بسته شدند.**
