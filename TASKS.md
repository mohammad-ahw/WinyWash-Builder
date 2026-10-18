# TASKS — وضعیت پروژه CleanC

> آخرین بروزرسانی: **۲۰۲۶/۱۰/۱۸ — پلن عرضه بین‌المللی v1.1.0: فاز ۶ (موتور دیتکتور) کامل شد ✅ — فاز بعدی: ۴ (i18n)**
> وضعیت هر آیتم با بازبینی مستقیم کد مشخص شده (نه حافظه).
> نشان‌گرها: ✅ انجام شده | ⚠️ ناقص/جزئی | ❌ انجام نشده | ⏳ در جریان | ⬜ نشده

---

## پلن عرضه بین‌المللی v1.1.0

> ترتیب اجرای مصوب: **۰ → ۲ → ۳ → ۶ → ۴ → ۱ → ۵** · جزئیات موتور دیتکتور: `docs/DETECTION-ENGINE-SPEC.md`

| فاز | موضوع | وضعیت | کامیت‌ها |
| :-- | :--- | :--: | :--- |
| **۰** | بکاپ امن + پاک‌سازی شاخه‌ها | ✅ | ۳ کامیت بکاپ + push؛ برنچ `stable` حذف شد |
| **۲** | بازبینی پیش از انتشار: فیکس بلاک‌لیست/escape، غنی‌سازی WMI درون‌پروسه، پاک‌سازی کد/کامنت مرده | ✅ | `235d945` |
| **۳** | سخت‌سازی سطح AV: جایگزینی LOLbinها با APIهای نیتیو | ✅ | `586eb9c` |
| **۶** | موتور دیتکتور D01–D18 + قفل‌ها + UI + تست/Acceptance | ✅ | `4ba215e`, `193c019`, `b0a69cd`, `908544a`, `af7e843`, `dc2b7a7`, `2811b5f`, `020ba02`, `31fb854` |
| **۴** | بین‌المللی‌سازی (i18n): ~۱۰۰۰ رشته fa هاردکد → کاتالوگ + ۸ زبان | ⏳ بعدی | — |
| **۱** | گیت‌هاب: rename ریپو به `CleanC-Dev` + ریپوی عمومی `CleanC` (لازمه‌ی update checker) + پاک‌سازی مسیرهای شخصی | ⬜ | — |
| **۵** | انتشار: بیلد نهایی + ZIP + `SHA256.txt` + VirusTotal + GitHub Releases | ⬜ | — |

**فاز ۶ — قدم‌ها:** ۱ قفل `LockRegistryService` + وتوی سخت حذف (`4ba215e`) · ۲ دیتکتور D03/D04/D10/D17 (`193c019`) · ۳-الف D05/D06/D09/D16 (`b0a69cd`) · ۳-ب D12/D13/D14/D18 (`908544a`) · ۴ UI (`af7e843`) · ۵ کاتالوگ fa/en (`dc2b7a7`) · ۶ تست‌ها (`2811b5f` = ۱۷ تست واحد + seamها) + هارنس Acceptance و فیکس‌های Acceptance-driven (`020ba02`، `31fb854`).

**فاز ۶ — نتیجهٔ Acceptance (۴ اجرای واقعی، همه EXIT=0؛ اجرای آخر: `%TEMP%\cleanc_acceptance4.log`):** همه‌ی موارد Ground Truth اسپک تطبیق شد — ۳ نصاب تکراری NVIDIA (1.85GiB، بدون پیش‌انتخاب عکس‌های شخصی)، LiveKernelReports 1.21GiB، `C:\NVIDIA` 654.7MB + `C:\Aomei` 790.9MB با مدرک T2، ۱۵ نصاب قدیمی، ۳۳ یتیم درایور (همه مشروع؛ وندورهای اکوسیستم Duet/A-Volute/MSI/Apple/Tonec/IDM حفاظت‌شده)، سرویس یتیم `battlenet_helpersvc` + هر دو پوشهٔ بازماندهٔ Battle.net، Twitter منسوخ به‌عنوان کاندید D13 + آدیت Win32 (Apple Mobile/Bonjour/X-VPN/JDK8) در Borderline، Sensitive=0 (واقعاً صفر). **تست‌ها: ۱۷۹ سبز** (شامل هارنس Acceptance که بدون env اسکیپ می‌شود). بدون push (طبق پلن).

**دور تکمیلی نهایی (مصوب کاربر — ۲۰۲۶/۱۰/۱۸، بدون گیت‌هاب):**

| مورد | وضعیت | یادداشت |
| :--- | :--: | :--- |
| **D5** — تأیید حذف تکی نرم‌افزار | ✅ | دیالوگ تأیید صریح (`UninstallSingleAppAsync`) هم‌سان با حذف گروهی |
| **Restore Point دیپ‌کلین** | ✅ تصمیم ثبت شد | سطل بازیافت/قرنطینه کافی و مؤثرتر است (Restore Point فایل کاربر را برنمی‌گرداند) — در اسپک بند ۸ ثبت شد |
| **B3** — virtualization لیست‌ها | ✅ | ✅ اپ‌ها (`ListView` + `AppListItemViewModel`) و ✅ سه مود مسطح پویش عمیق — زائد/پوشه خالی/فایل حجیم (`DeepStorageItemViewModel` + قفل داخل تمپلیت). مود تکراری‌ها (کارت گروهی تودرتو) و Advanced/گزارش عمداً دست‌نخورده: تعداد گروه‌ها محدود است و تبدیلشان سود محسوس ندارد |
| **B5** — پنل‌های ابزار → UserControl + ساخت تنبل در ناوبری | ⏳ در جریان | راه‌حل درست به‌جای `x:Load` که در WinUI 3 کار نمی‌کند |
| **C6** — MVVM صفحات داشبورد/ابزارها | ⏳ در جریان | منطق به ViewModel + تست VM؛ گام‌به‌گام |
| **فاز ۴ (i18n)** | ⏳ بعد از refactorها | روی معماری جدید تا دوباره‌کاری نشود |
| **بنچمارک کارکرد** | ⏳ | تست ~۷۰ موردی سیستم واقعی (لیست کاربر) — پیدا شد/نشد + قفل‌ها |
| **فاز ۵ (بدون گیت‌هاب)** | ⏳ | publish + ZIP + SHA256 + smoke محلی |

---


## خلاصه

| گروه | موضوع | انجام‌شده | ناقص | نشده |
| :--- | :--- | :--: | :--: | :--: |
| **A** | باگ‌های P0 | 10/10 | 0 | 0 |
| **B** | کندی، لگ، ساختار | 9/11 | 2 | 0 |
| **C** | تکنولوژی | 8/9 | 1 | 0 |
| **D** | چیزهای جاافتاده | 12/13 | 1 | 0 |
| **E** | کد مرده | 5/5 | 0 | 0 |
| **F** | فیچر | 8/8 | 0 | 0 |
| **G** | بازطراحی هیرو و هدر (فاز ۱۷) | 10/10 | 0 | 0 |
| | **مجموع** | **62/66** | **4** | **0** |

---

## گروه A — باگ‌های بحرانی (P0)

| # | وضعیت | یادداشت |
| :-- | :--: | :--- |
| A1 لایت مود | ✅ | ۱۹ hex حذف شد؛ `useWhiteText = isDarkTheme` (والپیپر دیگه روی متن اثر ندارد)؛ `catch` حالا `App.LogCrash` می‌زند؛ هیچ `Foreground="#FFFFFF"` در Hero نمانده. `_isWallpaperDark` فقط برای نور Acrylic. |
| A2 دیتای جعلی | ✅ | سیدها صفر شدند (`_cumulativeFreedBytes=0`، تاریخ `MinValue`)؛ `history` در `clean_stats.json` (۹۰ روز)؛ نمودار ۷ روزه واقعی؛ نوار دیسک واقعی؛ `HeroCleanableText="-"` و با اسکن پس‌زمینه پر می‌شود؛ `freed <= 0` دیگر گزارش نمی‌شود (پیام «قبلاً پاک بود»). |
| A3 Quick Boost بدون رعایت چک‌باکس | ✅ | `Directory.Delete(...,true)` حذف شده و `DeleteLeftoverFolders` با گارد `IsSafeToDelete` صدا زده می‌شود — و **fallback «اگر هیچی تیک نخورده بود، همه پاک شوند» حذف شد** (اگر هیچ موردی انتخاب نشده باشد، InfoBar هشدار و توقف). دیالوگ تأیید طبق درخواست کاربر حذف شد. |
| A4 پوشه‌های AppData/ProgramData | ✅ | `GetAllowedRoots()` + `IsSafeToDelete()` (عمق، reparse-point، نام محافظت‌شده، فایل شخصی/پروژه‌ای) + `DeleteLeftoverFolders` فقط در صورت موفقیت گزارش می‌کند. |
| A5 حذف recursive | ✅ | `sub.Delete(false)` ✅ + `IsProtectedSystemPath` (ریشه درایو، Windows، Program Files، UserProfile، `C:\Users`، پوشه‌های شخصی، AppData roots) ✅ + `IsReparsePoint` ✅. |
| A6 گارد پکیج‌های ویندوز | ✅ | `PackageSignatureKind.System` + پیشوندها (`Microsoft.Windows.`, DesktopAppInstaller, UI.Xaml, VCLibs). |
| A7 پاک‌سازی رجیستری | ✅ | `GetUninstallRoots` مشترک بین اسکن/پاک، تأیید حذف واقعی، گارد درایو — و `ExtractExePath` بازنویسی شد: اول بازه‌ی نقل‌قولی وسط رشته (پوشش `cmd /c "..."`)، بعد پیشوند نسبی مثل `MsiExec.exe` با `Path.IsPathRooted` رد می‌شود. |
| A8 فایل‌های Deep Storage در گیت | ✅ | هر ۶ فایل (`DeepStorageModels.cs`، `DeepStorageCleanService.cs`، `MainWindow.Tools.cs`، `MainWindow.DeepStorage.cs`، `NetworkSpeedService.cs`، `NativeMethods.cs`) در فاز ۱ staged شدند و در کامیت نهایی commit می‌شوند → کلون تمیز build می‌شود. |
| A9 استارت‌آپ | ✅ | فیلتر `desktop.ini/thumbs.db/...`، `Location=WindowsService`، `ServiceKey`/`OriginalValue`، بازگردانی با startType اولیه — و قالب بکاپ به `v3|Location|ServiceKey|OriginalValue|Command|OriginalKind` ارتقا یافت (خواندن v2 و legacy حفظ شد)؛ بازگردانی حالا `REG_EXPAND_SZ` را برمی‌گرداند. |
| A10 UWP/StartupTask/RunOnce/Task | ✅ | **سه منبع جدید در `StartupExtraSources.cs`** — (۱) `HKCU/HKLM RunOnce`؛ (۲) **StartupTaskهای MSIX/UWP** از `AppModel\SystemAppData\<PackageFamilyName>\<AppId>` با پارامتر `State`/`UserEnabledStartupOnce`/`LastDisabledTime` (نام نمایشی از شناسه‌ی پکیج پاک می‌شود)؛ (۳) **تسک‌های Task Scheduler** با تریگر `At logon time` / `At system start up` از خروجی CSV `schtasks /query /fo CSV /v` (پارسر CSV با پشتیبانی از `""`، رد کردن ۱۳۵ خط هِدِرِ تکراریِ هر پوشه، حذف تسک‌های `\Microsoft\Windows\`) که در `Task.Run` موازی اجرا می‌شود (حدود ۴ ثانیه). غیرفعال/بازگردانی برای هر سه شاخه پیاده شده (delete value / `schtasks /change /disable|/enable` / مقدار `State=0`) + `ServiceKey` حالا در رکورد بکاپ حفظ می‌شود تا غیرفعال‌سازیِ مجدد هم ممکن باشد. **این سه مورد عمداً وارد Quick Boost خودکار نمی‌شوند.** روی همین سیستم: ۱۷ تسک + ۹ StartupTask. |

---

## گروه B — کندی، لگ و سنگینی (P1)

| # | وضعیت | یادداشت |
| :-- | :--: | :--- |
| B1 کار سنگین روی UI thread | ✅ | `FlushDns + CleanBackgroundHelpers` و `CleanOrphanedRegistry + Scan/DeleteLeftoverFolders` حالا داخل `await Task.Run(...)` اجرا می‌شوند؛ بقیه‌ی مراقبت‌ها از قبل `Task.Run` داشتند. |
| B2 تایمرهای 2.5s/1.5s | ✅ | چهار ریشه هزینه بسته شد: (۱) `PowerPlanService.GetActiveScheme` هر ۲٫۵s یک `powercfg.exe` بالا می‌آورد → **کش ۳۰ ثانیه‌ای + باطل‌سازی در `SetActiveScheme`**؛ (۲) `GetActiveNetworkConsumer` با ۱۰ بار `GetProcessesByName` → **یک بار `Process.GetProcesses()` با کش ۲ ثانیه‌ای**؛ (۳) `DriveInfo("C")` هر تیک → **کش ۳ ثانیه‌ای**؛ (۴) **تایمر ۱٫۵ ثانیه‌ای اسپارک‌لاین حذف شد** (کاملاً تکراری با `RefreshSystemStats` بود و هر دو بافرهای نمونه را دو نرخ مختلف push می‌کردند → تاریخچه نمودار خراب می‌شد). |
| B3 virtualization + icon cache | ⚠️ | **هش کش آیکون اصلاح شد (فاز ۴):** `StableHash32` (FNV-1a قطعی) به‌جای `Math.Abs((exe+appName).GetHashCode())` — هم تصادفِ per-process بودن و هم `OverflowException` روی `int.MinValue` حذف شد + پاک‌سازی یک‌باره فایل‌های قدیمی کش. **virtualization هنوز مانده:** `UninstallerAppsStack`، `DeepStorageItemsStack`، `AdvancedGroupsContainer`، `ReportGroupsContainer` همگی StackPanel هستند؛ فقط `LiveCleanListView` ListView است. |
| B4 تاریخچه اعلان | ✅ | تاریخچه ایجاد شد ✅ + دکمه پاک کردن ✅ + نماد «خالی» ✅ — و در **فاز ۱۳**: با **سقف ۵۰ مورد** (در ذخیره و نمایش) + **persist در `AppSettings.NotificationHistory`** (با `Normalize`: مرتب‌سازی جدیدترین اول، حذف رکوردهای خالی، کوتاه‌کردن پیام‌ها به ۴۰۰ کاراکتر، نرمال‌سازی Severity) → بعد از ری‌استارت برمی‌گردد؛ «پاک کردن» هم ذخیره‌شده را پاک می‌کند. (`StatusInfoBar` در فاز ۵ از `DashboardView` بیرون آمد و حالا در هر دو تب دیده می‌شود ✅) |
| B5 پنل‌های eager | ⚠️ | **`x:Load` در این نسخه از WinUI 3 عملاً کار نمی‌کند** (در زمان اجرا بررسی شد): کدِ تولیدیِ `x:Load` برای ساختِ تنبل، `dataRoot.FindName(...)` را صدا می‌زند، ولی `Window` متد `FindName` ندارد → یک shim روی `Content` (ریشه‌ی واقعی درخت) گذاشته شد. با این حال `Content.FindName` برای المان‌های معمولی جواب می‌دهد ولی برای المان‌های `x:Load` حتی بعد از `true` شدنِ binding هم `null` برمی‌گرداند (ورودیِ deferred در name scope ثبت نمی‌شود) و صفِ `UnloadedElementsToUpdate` کدِ تولیدی هرگز پر نمی‌شود. `x:Load` از ۱۰ پنل حذف و همه‌چیز به حالت eager برگردانده شد → رفتار قبلی کامل حفظ شد. **راه‌حل درست: بیرون بردن پنل‌ها به `UserControl`های جدا و ساختِ آن‌ها در `NavigateToToolPage`** — یک refactor مجزا و پرریسک که فعلاً انجام نشده. |
| B6 توقف تایمرها | ✅ | `Closed += StopBackgroundTimers()` → `_statsTimer`، `_toastDismissTimer` و هر دو debounce توقف پیدا می‌کنند (`_sparklineTimer` در فاز ۳ حذف شد). |
| B7 فایل debug | ✅ | نویسنده `specs_test_from_ui.txt` حذف شد. |
| B8 حجم bin | ✅ | در **فاز ۱۵**: خروجی‌های **کهنه‌** پاک شدند — ۴ پوشه‌ی `net8.0-windows10.0.26100.0` باقی‌مانده از قبل از مهاجرت .NET 10 (۵۳۸MB) + یک publish کهنه‌ی self-contained به‌نام `win-x64` که داخل build output جا خوش کرده بود (۱۲۳MB) → **`bin` از ۷۴۱MB به ۷۹MB رسید (۸۹٪ کمتر)** و build بعدی آنها را دوباره نساخت. نکته: پوشه‌ی `runtimes\` (همه‌ی RIDها) عمدیِ رفتارِ build بدون RID است و فقط publish آن را جمع می‌کند (پس `publish.ps1` را دست نزدیم). `bin` در `.gitignore` است و هرگز commit نمی‌شود. |
| B9 زائد بودن publish | ✅ | RIDهای غیر ویندوز حذف شد (۳۰۷→۲۱۱MB) ✅، فایل‌های debug حذف شدند ✅ + در **فاز ۱۵** `onnxruntime.dll` (20.7MB) و `DirectML.dll` (17.8MB) هم حذف شدند: این دو از پکیجِ transitive `Microsoft.Windows.AI.MachineLearning` می‌آمدند → رفرنسِ مستقیم با `ExcludeAssets="native"` در `CleanC.UI.csproj` (نسخه در `Directory.Packages.props`) → **publish از ۲۱۱MB به ۱۷۳MB رسید (۲۵۴ فایل)**. خودِ publish (`publish\`) هم اجرا شد و سالم بالا آمد تا مطمئن شویم چیزی از AI/ML در startup لود نمی‌شود. |
| B10 فایل‌های اضافه root | ✅ | `inspect_preview.py` و `preview_*.txt` حذف شدند و پوشه `CleanC_UI_For_Claude` هم از ایندکس git حذف شده (`D` staged). |
| B11 آیکون‌های تکراری | ✅ | `Icon.ico` و `Logo.png` حذف شدند (~657KB)؛ `MainWindow.xaml` حالا `Assets/app-icon.png` را نشان می‌دهد. فایل‌های مرجع باقی‌مانده: `app.ico` و `app-icon.png`. |

---

## گروه C — تکنولوژی (P1)

| # | وضعیت | یادداشت |
| :-- | :--: | :--- |
| C1 .NET 10 | ✅ | SDK 10.0.401 نصب شد؛ TFM → `net10.0-windows10.0.26100.0` / `net10.0`. |
| C2 LangVersion + ImplicitUsings | ✅ | `LangVersion=latest` (C# 14) و `ImplicitUsings=enable` در هر دو پروژه. |
| C3 حذف وابستگی بلااستفاده | ✅ | **`Microsoft.Data.Sqlite` بلااستفاده نیست** و باید بماند — در `WindowsUpdateService.cs:8,233` برای خواندن USO `store.db` استفاده می‌شود (حذفش خطا می‌دهد). **`onnxruntime`/`DirectML`/`Microsoft.Windows.AI.*` در فاز ۱۵ از خروجی حذف شدند** (native آنها با `ExcludeAssets="native"`؛ اینها رفرنسِ مستقیم نبودند و فقط از WindowsAppSDK می‌آمدند) و `System.Numerics.Tensors` (0.4MB) که وابسته‌ی همان است باقی ماند چون managed و بی‌خطر است. |
| C4 publish درست | ✅ | `publish.ps1` با `-r win-x64 --self-contained` → **۲۱۱MB / ۲۵۴ فایل** (هدف ۱۱۰-۱۳۰MB بود؛ فاصله عمدتاً WinAppSDK AI + WinUI.dll). |
| C5 WindowsAppSDK | ✅ | 2.5.1 = آخرین stable (بررسی شد). BuildTools هم latest. |
| C6 MVVM + DI | ⚠️ | **زیرساخت در فاز ۱۴ کامل شد:** `Microsoft.Extensions.DependencyInjection` (10.0.12) + `CommunityToolkit.Mvvm` (8.4.2) اضافه شد (هر دو در `Directory.Packages.props`)، **ریشه‌ی ترکیب `AppServices.AddCleanCServices()`** در `AppServices.cs` + `AppServices.EnsureBuilt()` در سازنده‌ی `App` (خطای ثبت همان ابتدا دیده می‌شود)، **سرویس instance برای تنظیمات** (`ISettingsStore`/`SettingsStore` با debounce ذخیره درون خودش + هوک `OnSaveRequested` برای ثبت موقعیت پنجره) و **`SettingsViewModel`** با `[ObservableProperty]` (syntax partial-property برای سازگاری AOT/WinRT و بدون هشدار MVVMTK0045) + `Hydrate()` با گاردِ سکوتِ رویداد. صفحه‌ی تنظیمات با **`x:Bind`** (سوییچ‌ها TwoWay، رادیوها OneWay روی حالت‌های محاسبه‌شده) به VM وصل شد و handlerهای جایگزین (`TraySetting_Toggled`، `PersistenceSetting_Toggled`، `ApplyDeleteModeToUi`، `SyncThemeRadios`) حذف شدند؛ `AutoStartToggle` (F1، رجیستری) عمداً در code-behind ماند. **باقی‌مانده:** بقیه‌ی صفحات (داشبورد/ابزارها) هنوز code-behind هستند — مهاجرت کاملِ MVVM یک refactor بزرگِ جداگانه است؛ سرویس‌های `CleanC.Core` همچنان static هستند و فقط از مسیر `ISettingsStore` instance شدند. |
| C7 زیرساخت | ✅ | `.editorconfig` ✅، `Directory.Build.props` ✅، `AnalysisLevel=latest` ✅ + **`Directory.Packages.props` ✅** (Central Package Management، هیچ csproj-ای Version= ندارد) + **پروژه‌ی تست `tests\CleanC.Core.Tests` ✅ (۷۷ تست xUnit)** + **CI در `.github/workflows/ci.yml` ✅** (restore → build → test روی windows-latest). |
| C8 catch خالی / async void | ✅ | **۲۱۶ `catch {}` خالی** از طریق `CleanC.Core.Diagnostics.CcLog` ثبت می‌شوند (حلقه‌ی ۲۵۶تایی حافظه + حداکثر ۲ خط در ثانیه در `%LOCALAPPDATA%\CleanC\logs\ignored.log`)؛ **همه‌ی هندلرهای `async void` از `SafeInvoker.Run` رد می‌شوند** — در **بازبینی نهایی (فاز ۱۶)** ۴ هندلرِ سطل بازیافت که در فاز ۱۱ بدون پوشش مانده بودند هم به الگوی `Task + SafeInvoker` برگردانده شدند (۲ هندلرِ تکی هم try/catch داخلی داشتند و همگی یکدست شدند) → **تعداد `private async void` در کل `CleanC.UI` الان صفر است** (۳۳ فراخوانی `SafeInvoker.Run`). |
| C9 UnhandledException | ✅ | `LogCrash` می‌زند ✅ + در **فاز ۱۳** `e.Handled = true` حذف شد → خطا دفن نمی‌شود و برنامه با state ناسازگار ادامه نمی‌دهد (fail-fast؛ چون `catch`های خالی از فاز ۶ از طریق `CcLog` و هندلرهای `async void` از `SafeInvoker` پوشش داده شده‌اند). |

---

## گروه D — چیزهای جاافتاده (فنی و UX)

| # | وضعیت | یادداشت |
| :-- | :--: | :--- |
| D1 اعلان‌ها | ✅ | `FloatingToastCard` حالا **سطحی** است و روی هر تب دیده می‌شود ✅ + تاریخچه اعلان ✅ + **`StatusInfoBar` از داخل `DashboardView` بیرون آمد و به سطح `Grid` والد رفت (فاز ۵) → در هر دو تب دیده می‌شود ✅** + **نوتیفیکیشن ویندوزی واقعی در فاز ۱۲ (F5)**: toast وقتی پنجره فوکوس ندارد، balloon با شدت واقعی، صفِ toastها و badge/tooltip عدد سلامت ✅ — **بدون شمارنده/دسته‌بندیِ تاریخچه (بدون سقف و بدون persist).** |
| D2 ذخیره تنظیمات / صفحه Settings | ✅ | `AppSettings` + `SettingsService` (`%LOCALAPPDATA%\CleanC\settings.json`، نوشتن اتمیک با فایل موقت) ✅ + **صفحه سومِ نوار ناوبری: «تنظیمات برنامه»** ✅. ذخیره می‌شود: تم (System/Light/Dark)، اندازه/موقعیت پنجره (با اعتبارسنجی روی نمایشگرهای فعلی)، نمای آخر (داشبورد/ابزارها/تنظیمات)، انتخاب‌های اسکن (`JunkSelections`) + ۳ کلید جداگانه برای خاموش کردن هرکدام + دکمه‌های بازنشانی و مسیرهای دیتا/لاگ. ذخیره‌ی debounce‌شده (۶۰۰ms) + ذخیره‌ی نهایی هنگام `Closed`. |
| D3 minimize/close to tray | ✅ | **بستن پنجره** → `AppWindow.Closing` لغو می‌شود و پنجره در تری می‌ماند (هشدار اول فقط یک‌بار در اجرا) ✅. **کوچک‌نمایی** → در `Window.Activated` با `NativeMethods.IsWindowMinimized` چک می‌شود (نه `Alt+Tab`) و مخفی می‌شود ✅. **خروج واقعی** از مسیر `ExitApplication` می‌گذرد (ذخیره‌ی تنظیمات + `Dispose` تری) ✅. |
| D4 Fast Startup در Quick Boost | ✅ | در **فاز ۱۱ (F2)** به‌عنوان مرحله‌ی ۷.۵ به Quick Boost اضافه شد: سه حالتِ صریح (از قبل فعال / هایبرنیشن خاموش → «انجام نشد» بدون دست زدن به `hiberfil.sys` / `SetFastStartup(true)` با گزارش موفق-ناموفق). `HiberbootEnabled` هم خوانده می‌شود. |
| D5 تأیید حذف | ⚠️ | confirm برای: حذف دسته‌ای Uninstall ✅، درایور/دسته درایور ✅، فرمت درایو ✅، پاک‌سازی عمیق سیستم ✅ + **هر ۴ حذف Deep Storage** (دیالوگ با شمارش و حجم) ✅ + **حذف تکی فایل حجیم** (تأیید دومرحله روی دکمه، چون دیالوگ دوم باز است) ✅ + **خالی کردن Recycle Bin** (حذف `SHERB_NOCONFIRMATION` → تأیید خودِ ویندوز) ✅ + فایل حجیم حالا به سطل بازیافت می‌رود نه حذف دائمی ✅. **بدون confirm فقط «حذف تکی اپ» باقی مانده.** |
| D6 کیبورد/دسترس‌پذیری | ✅ | **میان‌برها:** `Ctrl+1/2/3` روی نوار ناوبری (داشبورد/ابزارها/تنظیمات) + `F5` روی بروزرسانی وضعیت. **نام‌های دسترسی:** `AutomationProperties.Name` روی هر دکمه‌ی فقط-آیکون (۴ دکمه‌ی هدر، دکمه‌ی بستنِ toast، ۱۰ دکمه‌ی بازگشت به فهرست ابزارها، بروزرسانی درایورها) + tooltip هم‌ارز. **High Contrast:** `IsHighContrastEnabled()` خوانده می‌شود و در `ApplyTheme` بازنویسی brushها/رنگ‌های متن، در `ConfigureSystemBackdrop` Acrylic خاموش می‌شود (شفافیت در HC ممنوع) + با `UISettings.ColorValuesChanged` دوباره ارزیابی می‌شود. **LiveRegion:** `AutomationProperties.LiveSetting="Polite"` روی `ToastMessageText` و `StatusInfoBar` + اعلامِ زنده در `ShowInfoBar`/`ShowFloatingToast` از مسیر `OnCreateAutomationPeer` (کارخانه‌ی `AutomationPeer.CreatePeerForElement` در WinUI 3 وجود ندارد؛ با reflection گرفته می‌شود). **`TabIndex` عمداً تنظیم نشد:** ترتیبِ فوکوسِ پیش‌فرض از ترتیبِ درخت (و جهتِ RTL) پیروی می‌کند و درست است؛ دادنِ عددِ صریحِ مثبت، فوکوسِ هدر/ناوبری را به **آخر** می‌برد. (`Ctrl+,` برای تنظیمات امکان‌پذیر نبود: `VirtualKey.OemComma` در WinUI 3 وجود ندارد.) |
| D7 single instance | ✅ | `Mutex("Local\\CleanC.SingleInstance")` + `NativeMethods.ActivateExistingInstance()`. |
| D8 About / آپدیت / خروجی گزارش | ✅ | بخش «درباره CleanC» در صفحه‌ی تنظیمات: نسخه‌ی برنامه (بدون هشِ build)، نسخه‌ی .NET و توضیح سیستم‌عامل. **«بررسی به‌روزرسانی»** releases API گیت‌هاب را می‌خواند و سه حالت را صریح گزارش می‌کند: نسخه‌ی جدیدتر هست / به‌روز هستی / منبع در دسترس نیست (۴۰۴ یا خطای شبکه) — چون مخزن خصوصی است، حالت سوم دیده می‌شود. **«صفحه‌ی پروژه»** از remote واقعی git باز می‌شود. **«کپی اطلاعات عیب‌یابی»** گزارش متنی (نسخه، زمان بیلد، رانتایم، OS، مسیرها) تولید می‌کند. |
| D9 ادعاهای README | ✅ | README بازنویسی شد: .NET 8→10، حذف بخش CLI، اضافه شدن بخش Build ✅ + در **فاز ۱۳** ادعاهای غیرقابل‌اثبات حذف شدند: **«under 500 milliseconds»** → «گزارش شفاف از نتیجه‌ی هر مرحله» (EN) و «در پس‌زمینه… مقدار واقعی فضای آزادشده» (FA)؛ **«zero footprint in registry»** → «فقط وقتی توئیک یا «شروع با ویندوز» را فعال کنید» (EN و FA). |
| D10 مسیر بکاپ رجیستری | ✅ | `PreferredBackupDir = %ProgramData%\CleanC\Backups` با مهاجرت یک‌باره از `%LOCALAPPDATA%` (منبع حذف نمی‌شود)؛ rollback هر دو مسیر را پاک می‌کند. (متد بلااستفاده‌ی `HasAppliedTweaks` در فاز E4 حذف شد.) |
| D11 AllowTelemetry | ✅ | `SetDiagTrackDisabled(false)` حالا `DeleteValue("AllowTelemetry")` می‌کند تا پیش‌فرض edition (Home/Pro=1، Enterprise=0) برگردد؛ با `RollbackAllTweaksAsync` هم هماهنگ است. |
| D12 پیام «بهینه‌سازی انجام شد» | ✅ | `HeroHealthTitle` (XAML:200) و `HeroHealthSubtitle` (XAML:207) حالا `x:Name` دارند و مرئی‌اند. |
| D13 Tray | ✅ | هر سه مورد در **فاز ۱۳**: `DestroyIcon(_hIcon)` در `Dispose` (نشت handle آیکونِ بارگذاری‌شده با `LR_LOADFROMFILE`) ✅ + `NIN_SELECT`/`NIN_KEYSELECT` در window-proc → انتخاب آیکون با کیبورد پنجره را فعال می‌کند ✅ + `GetCurrentStats()` حالا پشت **کشِ ۱ ثانیه‌ای** در `ShowTrayMenu` است تا داخل window-proc اجرا نشود. |

---

## گروه E — کد مرده و بهم‌ریختگی

| # | وضعیت | یادداشت |
| :-- | :--: | :--- |
| E1 هندلرهای بدون binding | ✅ | ۱۰ هندلر حذف شد (`LaunchTool_Click`، `CardFlushDnsBtn_Click`، `CardToggleCustomCleanBtn_Click`، `CardLargeFilesBtn_Click` و ۶ هندلر داشبورد). |
| E2 سرویس‌های بلااستفاده | ✅ | `SmartCleanOrchestrator.cs` و `ContextMenuManagerService.cs` حذف شدند. |
| E3 کنترل‌های collapsed / XAML بلااستفاده | ✅ | ۳ `Border` کاملاً خالی حذف شد (`CardRamStatusBadge`، `CardPowerBadge`، `CardDiskHealthBadge` — هر سه داخل بلوک‌های `<!-- Collapsed legacy elements -->`) + ۲۸ `x:Name` بلااستفاده روی المان‌های نمودار تاریخچه پاک‌سازی حذف شد (المان‌ها سر جایشان ماندند، فقط فیلد تولیدی کم شد). هیچ المان `Collapsed` بدون مرجع کد پیدا نشد. |
| E4 `HasAppliedTweaks` / `NonEssentialSignatures` | ✅ | هر دو واقعاً مرده بودند: `HasAppliedTweaks` هیچ فراخواننده‌ای نداشت؛ `NonEssentialSignatures` فقط از طریق `IsNonEssentialApp` خوانده می‌شد که خودش فراخواننده نداشت → هر سه حذف شدند. `SafeAutoDisableSignatures`، `WhitelistSignatures` و `VpnSignatures` استفاده می‌شوند و ماندند. |
| E5 فایل‌های اضافه | ✅ | `preview_*.txt` و `inspect_preview.py` حذف شدند ✅ + پوشه‌ی `CleanC_UI_For_Claude` دیگر هیچ فایلِ tracked ندارد (حذف در ایندکس staged است) ✅. |

---

## گروه F — فیچر مکمل

| # | وضعیت | یادداشت |
| :-- | :--: | :--- |
| F1 صفحه Settings (تم ۳حالته + ذخیره پنجره + close-to-tray + startup) | ✅ | صفحه + تم ۳حالته + ذخیره‌ی پنجره + close-to-tray در **فاز ۷ (D2/D3)** انجام شد ✅ + «شروع همراه با ویندوز» در **فاز ۱۱** اضافه شد: کارت toggle در تنظیمات + ثبت/حذف مقدار `CleanC` در `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` از `StartupOptimizerService`. منبع حقیقت **رجیستری** است نه `AppSettings` (کاربر می‌تواند همان آیتم را از Task Manager هم خاموش کند)، مسیرِ ناموجود ثبت نمی‌شود، و اگر نوشتن شکست بخورد کلید برمی‌گردد + `ShowInfoBar` خطا. ۴ تست `StartupRegistrationTests` (کلید رجیستری در تست‌ها به مسیر موقت منتقل می‌شود؛ `InternalsVisibleTo` به `CleanC.Core.csproj` اضافه شد). |
| F2 Quick Boost حرفه‌ای‌تر | ✅ | خلاصه‌ی گزارش هر مرحله با `StatusSummary` و `WasEmpty` ✅ + در **فاز ۱۱**: گام‌های ۴ تا ۹ هرکدام داخل `try/catch` جدا افتادند تا شکستِ یکی کلِ بهینه‌سازی را قطع نکند؛ شکست هر گام به‌صورت ردیفِ `IsFailed=true` در گزارش (glyph ✕ قرمز + badge قرمز) نمایش داده می‌شود و ردیف موفقِ همان گام حذف می‌شود (`HasFailure`). گام جدید **Fast Startup** (مرحله‌ی ۷.۵) اضافه شد: اگر از قبل فعال → «از قبل فعال بود»، اگر هایبرنیشن خاموش → **بدون دست زدن به `hiberfil.sys`** «انجام نشد» گزارش می‌شود، وگرنه `SetFastStartup(true)` با گزارش موفق/ناموفق (دسترسی Administrator). `CleanupReportItem.IsFailed` به مدل اضافه شد. |
| F3 Undo واقعی حذف فایل (Recycle Bin / quarantine) | ✅ | سرویس جدید **`SafeDeleteService`** با سه حالت `SafeDeleteMode`: **RecycleBin (پیش‌فرض)** / **Quarantine** / **Permanent** — `DefaultMode` از تنظیمات (`AppSettings.DeleteMode` با `Normalize` سه‌حالته) خوانده می‌شود. فقط فایل‌های شخصی (چهار حذف Deep Storage) از حالت پیروی می‌کنند؛ **زباله/کش با `DeleteJunk*` همیشه قطعی حذف می‌شوند** تا وعده‌ی «X فضا آزاد شد» دروغ نباشد. `DeleteFiles/DeleteDirectories` یک `SafeDeleteBatchResult` برمی‌گرداند که `FreedBytes` (فقط قطعی) و `MovedBytes` (سطل/قرنطینه) را جدا نگه می‌دارد؛ اگر نه سطل کار کرد نه قرنطینه، فایل حذف **نمی‌شود** و `Failed` گزارش می‌شود (داده نابود نمی‌شود). **متن دروغین «به سطل بازیافت منتقل خواهد شد» در `ConfirmDeepStorageBatchDeleteAsync` حالا از `DescribeMode` می‌آید و متن نتیجه از `DescribeBatch`** (صادق: «جابه‌جا شد (تا خالی کردن سطل آزاد نمی‌شود)» به‌جای «آزاد گردید»). سه رادیوی «رفتار حذف» در تنظیمات + کارت **«فایل‌های قرنطینه‌شده» در مرکز بازیابی** (فهرست نشست‌ها، بازگردانی، حذف قطعیِ دومرحله‌ای) + پاک‌سازی خودکار نشست‌های قدیمی‌تر از ۳۰ روز در هر اجرا. ۲۷ تست `SafeDeleteServiceTests` + ۸ تست `DeleteMode` در `SettingsServiceTests`. |
| F4 تاریخچه پاکسازی + نمودار واقعی ۷ روزه | ✅ | `history` در `clean_stats.json` + `RenderCleanupHistory`. |
| F5 AppNotification ویندوز + badge واقعی | ✅ | سرویس جدید **`WindowsNotificationService`**: ثبت **AUMID** در `HKCU\Software\Classes\AppUserModelId\CleanC.App` + `AppNotificationManager.Register()` + نمایش با `AppNotificationBuilder` — همه با فلگ «در دسترس بودن» و try/catch. **سیاست نمایش:** نوتیفیکیشن ویندوزی فقط وقتی پنجره‌ی CleanC فوکوس ندارد (`NativeMethods.IsWindowFocused`)، وگرنه همان InfoBar/toast داخلی کافی است؛ اول toast و اگر ثبت نشده باشد **balloon تری‌آیکون با `dwInfoFlags` واقعی** (`NIIF_WARNING=2` / `NIIF_ERROR=3`) از `ShowBalloonNotification(title, message, severity)`. **صف floating toast** (سقف ۸ مورد) تا پیام‌های پشت‌سرهم روی هم نیفتند. **badge عدد سلامت** با `BadgeUpdateManager` — چون اپ unpackaged است و میانبرِ Start Menu با AUMID ما ندارد، روی این سیستم COMException می‌دهد؛ در آن صورت فقط یک‌بار ثبت می‌شود و برای همیشه غیرفعال می‌گردد و عدد سلامت روی **tooltip تری‌آیکون** نمایش داده می‌شود (بدون سیلِ استثنا در لاگ). |
| F6 Startup planner (UWP/StartupTask/Task + Impact Score) | ✅ | **`BootImpactService.cs`** از رویداد **`EventID=101`** کانال `Microsoft-Windows-Diagnostics-Performance/Operational` تأخیر واقعیِ هر برنامه در بالا آمدن ویندوز را می‌خواند (`File Name` / `Total Time` / `Degradation Time`، کش ۱۵ دقیقه‌ای، حداکثر ۱۵۰۰ رویداد، با `System.Diagnostics.EventLog` پکیج شد). مقیاس مشترکِ «تأخیر/۱۰۰»: ۱–۳۴ کم، ۳۵–۶۴ متوسط، ۶۵–۱۰۰ زیاد — هم برای داده‌ی اندازه‌گیری‌شده و هم برای تخمینِ دسته‌بندی (تا badge هیچ‌وقت بی‌معنا نشود). نتیجه در badge کارتِ استارت‌آپ + خط «تأخیر اندازه‌گیری‌شده: X میلی‌ثانیه» نمایش داده می‌شود و شکستِ غیرفعال‌سازی حالا کلید را به حالت قبل برمی‌گرداند. ۱۰ تست واحد `BootImpactServiceTests` اضافه شد. |
| F7 Export گزارش TXT/CSV | ✅ | بخش «خروجی گزارش» در تنظیمات با سه خروجی: **مشخصات سیستم (TXT)**، **فهرست استارت‌آپ (CSV)** و **تاریخچه‌ی پاکسازی (CSV)** — هر سه از دیالوگ `FileSavePicker` (با `InitializeWithWindow`) و ذخیره‌ی **اتمیک با UTF-8 + BOM** از مسیر `ReportExportService.WriteUtf8Bom`. CSV با escape طبق RFC 4180 ساخته می‌شود تا نام‌ها/مسیرهای دارای کاما و نقل‌قول خراب نشوند؛ اسکن استارت‌آپ داخل `Task.Run` انجام می‌شود. ۱۰ تست واحد `ReportExportServiceTests` اضافه شد. |
| F8 Recycle Bin manager + Diagnostic quick-launcher | ✅ | Quick-launcher واقعاً وجود دارد ✅ + در **فاز ۱۱**: سرویس جدید `RecycleBinService` (پارس فایل‌های `$I` نسخه‌ی ۱/۲ **بدون COM و بدون دیالوگ سیستم**، محاسبه‌ی حجم پوشه‌ها با سقف ۵۰هزار ورودی، `Restore` با جایگزینی نام تکراریِ ` (restored N)` و fallback کپی برای پوشه‌ها، `Delete`، `EmptyAll`) + ابزار جدید **«سطل بازیافت»** در Tools با **پیش‌نمایش آیتم‌به‌آیتم** (مسیر اصلی LTR، نوع، زمان حذف، حجم)، انتخاب گروهی، و بازگردانی/حذف قطعیِ تکی و گروهی با `ContentDialog` تأیید. ۱۵ تست `RecycleBinServiceTests`. |

---

## گروه G — بازطراحی هیرو و هدر (فاز ۱۷)

> مرجع طراحی: `CleanC_hero_New-v1.html` (دست‌نخورده) · نتیجه: `src/CleanC.UI/Assets/Hero/CleanC.hero.html`
> **وضعیت: build پاک ۰ هشدار/۰ خطا · ۱۴۹ تست سبز · اجرای Debug و Release هر دو بدون کرش**

| # | وضعیت | یادداشت |
| :-- | :--: | :--- |
| G1 جایگزینی هیروکارت | ✅ | `HeroHost` (UserControl با WebView2) به‌جای `HeroCard` قدیمی. قرارداد پیام در `HeroBridge.cs`: JS→C# با `postMessage` + پاسخ RPC `{type:'rpc',id,ok,result}` تا حلقهٔ ۱۴ مرحله‌ای resolve شود؛ C#→JS با `PostWebMessageAsJson`. **۱۰ پیام ورودی و ۴ پیام خروجی از نظر اسمی با HTML cross-check شدند** (`run.step/borderline/scan/after`، `settings.request`، `run.abort`، `borderline.confirm`، `codex.open`، `chip`، `quick` ← `host.update`، `settings`، `theme`، `borderline.result`). |
| G2 HOST bridge واقعی | ✅ | `HeroHostApi.cs` هر پیام را به `MainWindow` ترجمه می‌کند؛ `HeroRunStepAsync` هر ۱۴ case (`0..13`) را دارد و `{mb}` برمی‌گرداند؛ `ShowHeroBorderlineDialogAsync`/`ShowHeroCodexDialogAsync` پیاده‌سازی واقعی دارند و `[[name,size]]` ارسال می‌کنند. |
| G3 سینک Accent Color | ✅ | `InitHeroAccentSync` (نام‌گذاری مجدد از `InitHeroAurora`) روی `UISettings.ColorValuesChanged` → `UpdateAccentBrushes + ConfigureSystemBackdrop + PushHeroTheme`؛ رنگ Windows Accent به `--halo` هیرو می‌رود (`GetWindowsAccentHex`، fallback `#1fa971`). |
| G4 حذف اورورا و کد مرده | ✅ | `StartAuroraAnimations`/`StopAuroraAnimations`/`SetupButtonShine`/`SetupStatusPulse` و همهٔ فراخوانی‌هاشان حذف شدند؛ فیلدهای `_auroraRootVisual`، `_auroraBlobVisuals`، `_auroraBrushes`، `_shineVisual`، `_isAuroraAnimating` پاک شدند؛ ۶ پراپرتی `HeroHealth*`/`HeroLive*`/`QuickBoostBtnText` و… حذف و به `PushHeroState()` تبدیل شدند؛ `QuickBoostBtn_ClickAsync` (۶۷۰ خط) به حلقهٔ نازک ۱۴مرحله‌ای تبدیل شد. |
| G5 ناوبری در هدر | ↩️ | **با G13 برگشت خورد.** نسخهٔ کوتاه: `AppTitleBar` ۵ ستون شد و دکمه‌های `Ctrl+1/2` به هدر رفتند؛ کاربر ساختار قبلی را خواست و حالا دوباره pill وسط‌چینِ `MainContentGrid` است (`CaptionButtonsSpacer.Width` داینامیک `inset > 0 ? inset : 144` همچنان پابرجاست). |
| G6 کارت «کاراکتر» در Settings | ✅ | ۱۴ فیلد `Character*` در `AppSettings` (nullable ⇒ null = دست‌نخورده) + ۱۴ `[ObservableProperty]` در `SettingsViewModel` با گارد `_loading` و `PersistRequested` → `ScheduleSettingsSave + PushHeroSettings` + کارت XAML با ۲۵ کنترل. `CharacterLun` حالا `int?` (افست تقویم قمری ±۱، نه bool). handler ها با گارد `_characterUiReady` تا اولین `PopulateSettingsUi` هیچ ورودی نمی‌پذیرند تا مقداردهی اولیهٔ XAML «تنظیمات دست‌نخورده» را پر نکند. |
| G7 لکالایزیشن | ✅ | هیرو عمداً Persian-only است؛ کارت Settings و هدر از `LocalizationService` لکالایز می‌شوند. **۲۵ کلید جدید** (`Character*` × ۲۳ + `NavDashboardShort`/`NavToolsShort`) به `en-US` و `fa-IR/Resources.resw` اضافه شد (هر دو ۱۱۳ data، XML معتبر). `AdminBadgeText` هم بالاخره لکالایز شد (`RunningAsAdmin` از قبل موجود بود). |
| G8 سینک وان‌وی‌واي | ✅ | `InitHeroHost` در constructor · `_statsTimer` tick → `PushHeroState()` · تم → `finally`ِ `ApplyTheme` → `PushHeroTheme()` · تغییر تنظیمات → `PushHeroSettings()`. `settings.request` هنگام آماده شدن صفحه هر سه را همان لحظه می‌فرستد. |
| G9 WebView2 در publish | ✅ | `CoreWebView2Environment.CreateAsync()` **بدون پارامتر** (با پروب build تأیید شد؛ ۲/۳ آرگومانی کامپایل نمی‌شود). user data پیش‌فرض کنار exe. publish Release self-contained: **۱۷۴MB / ۲۶۷ فایل**، `CleanC.hero.html` + `WebView2Loader.dll` + هر دو resw موجود، و اجرای خروجی publish ۲۰ ثانیه بدون کرش با پروفایل WebView2 ساخته‌شده. |
| G10 تست | ✅ | `dotnet test`: **۱۴۹/۱۴۹ سبز** · smoke run دیباگ ۱۸ ثانیه بدون کرش · لاگ فقط `TrySetBadge: COMException` قدیمی، **هیچ خطای Hero نیست**. |
| G11 دکمهٔ «بررسی مجدد» به‌جای «بهینه‌سازی» | ✅ | **ریشه (سطح کد):** `BuildHeroItems()` وقتی `_cachedScanResult == null` (یعنی هنوز هیچ اسکنی نشده، که حالت عادیِ استارت‌آپ است) آرایهٔ خالی برمی‌گرداند → دکمهٔ اصلی هیرو `re` می‌شد. **رفع:** (۱) در نبودِ اسکن و قبل از اولین اسکن، `DefaultHeroBoostItems` (۸ آیتم) فرستاده می‌شود؛ (۲) `ApplyHeroScanResult` نتیجهٔ اسکن را واقعاً نگه می‌دارد؛ (۳) `GetHeroScanAsync()` با قفل، همهٔ مسیرها را به **یک** اسکن مشترک می‌چسباند؛ (۴) ترتیب `issues()` طوری شد که بین مسائلِ هم‌شدت، آیتم‌های `act=='run'` جلوتر بایستند — وگرنه کارتِ «آپ‌تایم ۷ روزه» (که `btn:['بررسی مجدد','ghost','re']` دارد) می‌نشست روی دکمهٔ اصلی. |
| G12 ریشهٔ واقعی: پل پیام WebView2 مرده بود | ✅ | **این باعِ اصلیِ همان باگِ گزارش‌شده بود.** JS هیرو روی `window.addEventListener('message')` گوش می‌داد، ولی WebView2 پیام‌های `PostWebMessageAsJson` میزبان را **فقط** روی `window.chrome.webview` تحویل می‌دهد. نتیجه: هیرو هیچ‌وقت `state`/`settings`/`theme`/پاسخِ RPC را نمی‌گرفت → `active` همیشه `[]` (دکمه «بررسی مجدد»)، `issues()` خالی، سنسورها بی‌حرکت، و `await HOST.scan()` تا timeout **۱۸۰ ثانیه‌ای** گیر می‌کرد — یعنی دقیقاً همان «چند دقیقه اسکن و بی‌نتیجه». **رفع:** شنونده به `_rt = chrome.webview ?? window` منتقل شد + پارسِ رشتهٔ JSON برای حالت fallback. **اثبات:** اسکرین‌شات publish بعد از فیکس → دکمه **«بهینه‌سازی سیستم · ۸ مورد»**، چیپ‌ها و سنسورهای زنده، «رفع همه · ۹ مورد». لاگِ موقتِ ریشه‌یابی تأیید کرد میزبان درست می‌فرستاد (`items=[junk,dns,browser,reg,startup,tweak,fast,gpu,debloat]`) و مشکل فقط گیرنده بود. لاگ موقت بعد از تأیید حذف شد. |
| G13 چهار ایراد گزارش‌شدهٔ کاربر | ✅ | **۱) سایزهای واقعی `IT` از هسته:** `HeroState.Sizes` (`Dictionary<string,long>` به بایت) + `BuildHeroSizes()` — junk از اسکن (بدون `orphanedRegistry` تا دوباره شمرده نشود)، browser از `_heroBrowserMb`، reg از `orphanedRegistry.Bytes`، بقیه صفر. سمت JS تابع `syncSizes()` از `window.CleanC.update` صدا می‌خورد و `IT[k][5]` را عوض می‌کند + فقط اگر متن جزئیات با «عدد + واحد حجم» شروع شود (regex) عددِ ابتدایش را هم به‌روز می‌کند — متن‌های شمارشی («312 ورودی»، «9 برنامه») دست نمی‌خورند. `meta` هم وقتی جمع صفر است پیشوند «قابل پاکسازی» را حذف می‌کند. **اثبات:** «1.53 گیگابایت فایل زائد و کش…» به‌جای `796 مگابایت` دمویی. |
| G14 گوشه‌های هیرو (لایهٔ مربعی زیرِ radius) | ✅ | **ریشه:** `body{background:var(--sf)}` یک مستطیل opaque و **مربعی** بود که چهار گوشهٔ `border-radius:12px` هیرو را می‌پوشاند؛ ضمناً `.hero{height:170px}` در باکسِ ۱۶۸پیکسلیِ `Height="170"` با `BorderThickness=1` سرریز می‌کرد و گوشه‌های پایین عملاً مربعی می‌ماندند. **رفع:** `body{background:transparent}` + `html/body/.app/.hero{height:100%}` + `HeroHost`: `WebView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent` (باید **قبل از** `EnsureCoreWebView2Async` ست شود) + `HeroCard.BorderThickness="0"` (کادر و radius را خودِ HTML می‌کشد تا کادر دوبل نشود). **اثبات:** بزرگ‌نمایی ۸× روی گوشهٔ بالا-چپ و پاین-چپ → کاملاً گرد، بدون سفید و بدون وصلهٔ مربعی. |
| G15 حذف «کشف‌شده N از M» | ✅ | `<div id="coll">` + CSS `.coll` + توابع `updColl` (هر دو تعریفش) + `$('coll').onclick` + `#coll` در selectorِ pointerover کامل حذف شدند؛ `found()` فقط `FOUND` را نگه می‌دارد. **نکته:** `codex.open` تنها درِ کدکس دستاوردها بود، پس به خط وضعیت هیرو (`.st`) منتقل شد (`title` + `cursor:pointer`) تا پنل `ShowHeroCodexDialogAsync` از دست نرود. |
| G16 سوئیچ ابزار/داشبورد به ساختار قبلی | ✅ | پنل nav از `AppTitleBar` حذف شد (۵→۴ ستون؛ `TitleBarDragRegion` 2→1، ابزارهای سریع 3→2، `CaptionButtonsSpacer` 4→3) و **pill شناورِ قبلی** (`Border` با `CornerRadius=19`، `Margin=0,0,0,10`، دو دکمهٔ ۳۴پیکسلی با متن کامل) به `Grid.Row="0"`ِ `MainContentGrid` برگشت؛ نام‌ها/هاهنلرها/`KeyboardAccelerator`ها دست‌نخورده ماندند پس `StyleNav` و Ctrl+1/2 کار می‌کنند. `ApplyLanguage` حالا کلیدهای کامل `NavDashboard`/`NavTools` را می‌خواند (کلیدهای `*Short` دیگر استفاده نمی‌شوند). **اثبات:** UIA invoke روی `NavToolsBtn` ← نمای ابزارها باز شد و حالت فعالِ pill جابه‌جا شد؛ سپس به داشبورد برگشت (`LastView` درست ذخیره شد). |
| G17 تثبیت کامل هیرو و سینک بدون تداخل با نرم‌افزار | ✅ | **ریشه و رفع ۴ ایراد اصلی:** (۱) **رفع اعلام مداوم «نیاز به بروزرسانی/بهینه‌سازی»:** `BuildHeroItems()` در هر بار لود، توئیک‌ها را به اشتباه به عنوان آیتم‌های نیازمند پاکسازی مجدد برمی‌گرداند؛ با شرط بررسی پاکسازی اخیر (`cleanedRecently` بر پایهٔ `_lastCleanDateTime` و `_isCleanedRecently`)، در صورت تمیز بودن سیستم و صفر بودن زباله‌ها لیست خالی برمی‌گردد و هیرو در حالت آماده و سلامت کامل (`ready`) می‌ماند؛ (۲) **تبدیل بایت به مگابایت در RPC:** در مراحل ۲، ۳، ۵، ۶ و ۱۱، خروجی RPC بایت خام برمی‌گرداند که در JS باعث نمایش اعداد نامتعارف چندصدهزار گیگابایت می‌شد؛ به MB تبدیل شد؛ (۳) **سینک آمار واقعی:** فیلدهای `totalFreedGb` و `lastClean` به متغیرهای واقعی هیرو متصل و داده‌های دموی هاردکدشده حذف شدند؛ (۴) **رفع سوئیچ و پرش بین حالت‌ها:** هیسترزیس سنسورها اصلاح شد (RAM به 88%/76% و CPU به 85%/72%)، مسئلهٔ آپ‌تایم ویندوز مانع سلامت نشد، تایمرهای خواب به بازه‌های استاندارد افزایش یافت و آنتی‌فلیکر به ۴ ثانیه تنظیم شد. |

**انجام‌شده در این فاز:** `MainWindow.xaml(.cs)`، `MainWindow.Hero.cs` (جدید)، `SettingsViewModel.cs`، `SettingsService.cs`، `Hero/*` (جدید: `HeroBridge`، `HeroState`، `HeroHost.xaml(.cs)`، `HeroHostApi`، `HeroLooks`)، `Assets/Hero/CleanC.hero.html` (جدید)، `Resources.resw` × ۲، `Directory.Packages.props` + `CleanC.UI.csproj` (WebView2).

**باقی‌مانده (نیاز به دستی/بازبینی انسانی):**
- ⚠️ راستی‌آزمایی بصری زنده: تغییر هر ۱۴ تنظیمات کاراکتر و دیدن اعمال فوری در کارت داشبورد.
- ⚠️ اجرای واقعیِ ۱۴ مرحلهٔ پاکسازی از هیرو (عمداً خودکار نشد: عملیات پاکسازی واقعی روی دیسک انجام می‌دهد).
- ✅ اجرای `.\publish.ps1` کامل: `CleanC-Release-win-x64.zip` بازنویسی شد (**۳۶۴ فایل / ۴۴۹.۲ مگابایت** خروجی، zip **۳۳۲.۲ مگابایت**)؛ در `publish\Assets\Hero\CleanC.hero.html` چک شد: `syncSizes`/`background:transparent`/`height:100%` موجود، `updColl`/`id="coll"` ناموجود.
- 💤 `CleanC_hero_New-v1.html` در root هنوز untracked است (مرجع طراحی؛ commit شود یا حذف؟).

---

## اولویت پیشنهادی برای ادامه

**بلوکر + خطرناک (دیتای کاربر) — همه انجام شد:**
1. ✅ **A8** — `git add` کردن فایل‌های Deep Storage/NetworkSpeed/NativeMethods (فاز ۱، staged)
2. ✅ **A7** — باگ پارسر `cmd /c "..."` (فاز ۲)
3. ✅ **A9** — تنزل `REG_EXPAND_SZ` (فاز ۲)
4. ✅ **D10** — بکاپ در پوشه‌ای که پاک‌کننده‌ها پاک می‌کنند (فاز ۲)
5. ✅ **D11** — `AllowTelemetry=3` (فاز ۲)

**کندی + رفتار خطرناک Quick Boost — همه انجام شد:**
6. ✅ **B1** — `Task.Run` برای کارهای سنگین Quick Boost (فاز ۳)
7. ✅ **B2** — تایمرها: `powercfg` هر ۲٫۵s، `GetProcessesByName` ×10، `DriveInfo`، تایمر ۱٫۵s تکراری (فاز ۳)
8. ✅ **B6** — `_statsTimer.Stop()` موقع بستن (فاز ۳)
9. ✅ **A3** — رعایت چک‌باکس‌ها (دیالوگ تأیید طبق درخواست کاربر حذف شد) (فاز ۳)
10. ✅ **B3** — ایمن‌سازی `Math.Abs(GetHashCode())` (فاز ۴)

**بقیه‌ی فاز ۴ و ۵ — همه انجام شد (build پاک: ۰ هشدار / ۰ خطا):**
11. ✅ **B11** — حذف `Icon.ico` و `Logo.png` (~657KB) (فاز ۴)
12. ✅ **E3** — ۳ `Border` خالی + ۲۸ `x:Name` بلااستفاده (فاز ۴)
13. ✅ **E4** — `HasAppliedTweaks` / `NonEssentialSignatures` / `IsNonEssentialApp` (فاز ۴)
14. ✅ **D5** — تأیید حذف Deep Storage + فایل حجیم + Recycle Bin (فاز ۵)
15. ✅ **D1** — `StatusInfoBar` به سطح Grid والد منتقل شد (فاز ۵)

**فاز ۶ — همه انجام شد (build پاک + ۳۷ تست سبز):**
16. ✅ **C8** — ۲۱۶ `catch {}` خالی → `CcLog`؛ ۲۰ `async void` → `SafeInvoker` / `async Task`
17. ✅ **C7** — `Directory.Packages.props` + پروژه‌ی تست xUnit + CI workflow

**فاز ۷ — همه انجام شد (build پاک ۰/۰ + ۵۷ تست سبز):**
18. ✅ **D2** — `SettingsService`/`AppSettings` + صفحه «تنظیمات برنامه» + ذخیره‌ی تم/پنجره/نمای آخر/انتخاب‌های اسکن
19. ✅ **D3** — close-to-tray، minimize-to-tray (با `IsIconic`، نه Alt+Tab) و خروج ایمن از مسیر تری
20. ✅ تایم‌استمپ `CcLog` به میلادی ثابت تبدیل شد (روی `fa-IR` داشت ۱۴۰۵ می‌نوشت) + تست‌ها دیگر روی لاگ واقعی کاربر نمی‌نویسند

**فاز ۸ — انجام شد (build پاک ۰/۰ + ۵۷ تست سبز + اجرای واقعی بدون کرش):**
21. ⚠️ **B5** — ساخت تنبلِ پنل‌های ابزار: `x:Load` در WinUI 3 روی ریشه‌ی `Window` عملاً کار نمی‌کند (بررسی زمان اجرا)؛ کد به حالت eager برگردانده شد و راه‌حل واقعی (بیرون بردن پنل‌ها به `UserControl`) به‌عنوان کار مجزا یادداشت شد.
22. ✅ **D6** — میان‌برهای `Ctrl+1/2/3` و `F5` + `AutomationProperties.Name` روی دکمه‌های فقط-آیکون + پشتیبانی High Contrast (خاموش کردن بازنویسی رنگ و Acrylic) + `LiveSetting` و اعلامِ زنده برای toast و نوار وضعیت.

**فاز ۹ — انجام شد (build پاک ۰/۰ + ۶۷ تست سبز + اجرای واقعی بدون کرش):**
23. ✅ **A10** — سه منبع جدید استارت‌آپ (`RunOnce`، StartupTaskهای MSIX/UWP، تسک‌های Logon/Boot) + غیرفعال/بازگردانیِ آنها + حفظ `ServiceKey` در بکاپ.
24. ✅ **F6** — Impact Score از رویداد واقعی `EventID=101` (لاگ بوت ویندوز) + مقیاس یکسان برای داده‌ی اندازه‌گیری‌شده و تخمینِ دسته‌بندی + نمایش در UI و مدیریت خطای toggle.

**فاز ۱۰ — انجام شد (build پاک ۰/۰ + ۷۷ تست سبز + اجرای واقعی بدون کرش):**
25. ✅ **D8** — بخش «درباره CleanC» (نسخه/‌.NET/OS) + «بررسی به‌روزرسانی» با سه حالت مشخص + «صفحه‌ی پروژه» + «کپی اطلاعات عیب‌یابی».
26. ✅ **F7** — خروجی گزارش TXT/CSV از صفحه‌ی تنظیمات با `FileSavePicker`، ذخیره‌ی اتمیک UTF-8+BOM و escape طبق RFC 4180.

**فاز ۱۱ — انجام شد (build پاک ۰/۰ + ۹۶ تست سبز + اجرای واقعی بدون کرش):**
27. ✅ **F1** — «شروع همراه با ویندوز»: کارت toggle در تنظیمات + ثبت/حذف `HKCU\...\Run\CleanC` از `StartupOptimizerService` (منبع حقیقت = رجیستری، برگرداندن کلید در صورت شکست) + ۴ تست.
28. ✅ **F2** — گام‌های Quick Boost جداگانه محافظت شدند و شکست هر گام با `IsFailed` در گزارش دیده می‌شود + افزودن گام **Fast Startup** با سه حالت صریح.
29. ✅ **F8** — سرویس `RecycleBinService` (پارس `$I` + بازگردانی/حذف/خالی‌سازی) + ابزار «سطل بازیافت» با پیش‌نمایش آیتم‌به‌آیتم + ۱۵ تست.

**فاز ۱۲ — انجام شد (build پاک ۰/۰ + ۱۳۱ تست سبز + اجرای واقعی بدون کرش):**
30. ✅ **F3** — حذف امن: `SafeDeleteService` با سه حالت (سطل/قرنطینه/قطعی) + تفکیک `FreedBytes` از `MovedBytes` + اصلاح پیام دروغین Deep Storage + کارت قرنطینه در مرکز بازیابی + تنظیمات «رفتار حذف» + ۲۷ تست.
31. ✅ **F5** — نوتیفیکیشن ویندوزی: `WindowsNotificationService` (AUMID + toast) + balloon با شدت واقعی + صفِ toastها + badge/tooltip عدد سلامت؛ فقط وقتی پنجره فوکوس ندارد.

**فاز ۱۳ — انجام شد (build پاک ۰/۰ + ۱۳۳ تست سبز + اجرای واقعی بدون کرش و بدون crash.log):**
32. ✅ **C9** — `e.Handled = true` حذف شد (لاگ crash می‌ماند، اجرای بعدی متوقف می‌شود).
33. ✅ **D13** — `DestroyIcon` + `NIN_SELECT`/`NIN_KEYSELECT` + کشِ ۱ ثانیه‌ای `GetCurrentStats` در menu-tray.
34. ✅ **B4** — سقف ۵۰تایی + persist تاریخچه‌ی اعلان‌ها در `settings.json` (۲ تست).
35. ✅ **D9** — ادعاهای «زیر ۵۰۰ms» و «صفر footprint در رجیستری» از README (EN/FA) حذف شدند.
36. ✅ **D4** — تأیید شد که در فاز ۱۱ (گام ۷.۵ Quick Boost) انجام شده بود.

**فاز ۱۴ — انجام شد (build پاک ۰/۰ + ۱۳۳ تست سبز + اجرای واقعی بدون کرش و بدون crash.log):**
37. ⚠️ **C6** — زیرساخت MVVM/DI ساخته شد: `Directory.Packages.props` ← DI 10.0.12 + CommunityToolkit.Mvvm 8.4.2، `AppServices.cs` (composition root)، `ISettingsStore`/`SettingsStore` (ذخیره‌ی debounce‌شده به‌جای مستقیم‌های استاتیک)، `SettingsViewModel` (partial-property) و `x:Bind` در XAML؛ handlerهای جایگزین حذف شدند. **جزئی ماند:** فقط صفحه‌ی تنظیمات به VM رفت (بقیه‌ی صفحات code-behind‌اند).

**فاز ۱۵ — انجام شد (build پاک ۰/۰ + ۱۳۳ تست سبز + اجرای build و publish هر دو بدون کرش):**
38. ✅ **B8** — خروجی‌های کهنه‌ی `bin` پاک شد (۴ پوشه‌ی net8.0 + یک publish کهنه‌ی `win-x64`) → **۷۴۱MB → ۷۹MB**.
39. ✅ **B9** — `onnxruntime.dll` + `DirectML.dll` با `ExcludeAssets="native"` از خروجی حذف شدند → **publish ۲۱۱MB → ۱۷۳MB** (اجرای publish هم سالم بود).
40. ✅ **C3** — وابستگی‌های بلااستفاده‌ی AI/ML بسته شد؛ `Microsoft.Data.Sqlite` عمداً ماند (USO `store.db`).

**فاز ۱۶ — بازبینی نهایی (build پاک ۰/۰ + ۱۳۳ تست سبز):**
41. ✅ هر ۶۱ handler در XAML کدِ پشتیبان دارد و هیچ handlerِ تعریف‌شده‌ای (به‌جز ۲ موردِ `Click` که در کد `+=` می‌شوند) بدون مصرف نبود.
42. ✅ `catch {}` خالی = صفر · `e.Handled = true` فقط در هندلر کیبوردِ Escape ( legitimately) · `TODO/FIXME` = صفر · کلید/توکن/پسورد در کد = صفر.
43. ✅ **رفع باگ:** ۴ هندلرِ `async void` سطل بازیافت (بازگردانی/حذف گروهی/خالی‌سازی/بروزرسانی) که در فاز ۱۱ بدون `SafeInvoker` مانده بودند → به الگوی `Task + SafeInvoker.Run` برگردانده شدند؛ حالا `private async void` در کل UI صفر است (با fail-fast فاز ۱۳ اینها می‌توانستند کل برنامه را بکشند).

**پیشرفت فاز‌ها:** ۱ ✅ | ۲ ✅ | ۳ ✅ | ۴ ✅ | ۵ ✅ | ۶ ✅ | ۷ ✅ | ۸ ✅ | ۹ ✅ | ۱۰ ✅ | ۱۱ ✅ | ۱۲ ✅ | ۱۳ ✅ | ۱۴ ✅ | ۱۵ ✅ | ۱۶ ✅ | ۱۷ ✅ | ۱۸ ✅ | **۱۹ ✅** — **همه‌ی فاز‌ها تمام شد (فاز ۱۹ = رفع باگ‌های گزارش زنده)**

**بقیه:** ✅ **همه‌چیز تمام شد** — کامیت نهایی `dcf9b62` (۹۹ فایل، working tree تمیز) + بیلد نهایی Release **۰ هشدار / ۰ خطا** + ۱۳۳ تست سبز + اجرای نهایی ۱۸ ثانیه‌ای بدون کرش (crash.log خالی؛ تنها لاگِ شناخته‌شده همان `TrySetBadge: COMException` یک‌بار در هر اجراست).

**فاز ۱۸ — انجام شد (۲۴ ایراد بازبینی نوع‌دوم + باگ شماره ۲۶ فلیکر کارت‌های هیرو — build پاک ۰/۰ + ۱۴۹ تست سبز):**

| # | وضعیت | موضوع |
| :-- | :--: | :--- |
| 1 | ✅ | **P0-1/2 (`SystemScanService`)**: تلگرام tdata عنوان و توضیح صادقانه گرفت + `IsChecked=false`؛ phoneBackups هم `IsChecked=false`؛ `SafeGetDirectorySize` بازنویسی شد با `Walk` بازگشتی و سقف عمق ۶ / ۲۰هزار فایل / بودجه‌ی ۲ ثانیه و `IgnoreInaccessible=true` — اسکن دیگر روی درخت‌های عظیم نمی‌ماند و در خطای دسترسی فرو نمی‌ریزد. |
| 2 | ✅ | **P0-3/P1-6 (`DeepStorageCleanService` + فایل جدید `Core/Native/ProcessRunner.cs`)**: تشخیص فایل‌های تکراری دومرحله‌ای شد (هش سریع ۶۴KB → هش کامل MD5 فقط داخل باکت‌های مشترک)؛ چهار فراخوانی خارجی (wsl shutdown / diskpart / DISM / docker) از `WaitForExit` خام به `WaitAndKillOnTimeout` رفتند تا پروسسِ معطلی در تایم‌اوت کشته شود و نتیجه‌ی موفقیت واقعی `exit code` را بخوانیم + شمارنده‌ی `compactionFailures` و پیام نهاییِ تفکیک‌شده. |
| 3 | ✅ | **P0-4 (`SmartCleanOrchestrator`)**: fallback جعلی «اسکن دوباره و تیک‌همه» حذف شد؛ عدد freed جعلی (`freed<=0 && Bytes>0`) حذف شد → فقط خروجی واقعی گام‌ها جمع می‌شود؛ وقتی هیچ موردی انتخاب نشده پیام صادقانه می‌دهد؛ Delayها یکنواخت و کوتاه‌تر شدند (بازه‌های قبلی پراکنده ۲۰۰–۵۰۰ به ۱۲۰–۲۵۰). |
| 4 | ✅ | **P0-5 (`RegistryCleanerService`)**: `File.Exists` جایش `IsExeReallyMissing` نشست — با `File.OpenRead` و ریدایرکت WOW64 به `Sysnative` (اورفان‌های واقعی `C:\Windows\System32` دیگر بین‌سازگارِ معمولی جا نمی‌سد)؛ فقط FileNotFoundException/DirectoryNotFound به‌عنوان orphan پاک می‌شوند و خطاهای عمومی log می‌شوند و بدون حذف. |
| 5 | ✅ | **P1-7 (`BloatwareService` + UIها)**: حجم‌های تخمینی (برآورد ８۵MB و OEM ۲۵۰MB) با پرچم `SizeIsEstimate` مشخص می‌شوند؛ هر ۳ فراخوانیِ `EstimatePackageSize` و هر ۳ محاسبه‌ی فضای آزادشده (دو دیالوگ + جمعِ هیرو) فقط سایزهای اندازه‌گرفته‌شده را می‌شمارند؛ بج اندازه در UI پسوند «(تخمینی)» می‌گیرد. |
| 6 | ✅ | **P1-8 (`CleanService`)**: خالی‌کردن سطل بازیافت روی Thread اختصاصی STA با نام `CleanC-RecycleBin-STA` و `Join ۱۲۰ ثانیه` (فراخوانی COM سطل از ریزثرد دلخواه امن نیست)؛ اگر Thread در تایم‌اوت تمام نشود، نتیجه ۰ گزارش می‌شود. |
| 7 | ✅ | **P1-9 (`TrayIconManager`)**: ترمیم BoostRam از تری در try/catch — شکست balloon Warning می‌دهد و به `CcLog.Ignore` می‌رود؛ موفقیت هم balloonInfo با عدد واقعی. |
| 8 | ✅ | **P1-10 (`HeroHost.xaml(.cs)`)**: پنل `FallbackPanel` اضافه شد و `ShowFallbackPanel` در catch `EnsureCoreAsync` — فقدان WebView2 Runtime به‌جای کارت خالی، راهنمای نصب نشان می‌دهد؛ متد مرده `UserDataFolder` حذف شد؛ کامنت ارتفاع ۱۷۰ به ۲۰۵ اصلاح شد. |
| 9 | ✅ | **P1-11 (`StartupOptimizerService`)**: رکورد بکاپ با کلید `Location|Name` (توسط `BuildBackupToken`) — دو آیتم هم‌نام در HKCU/HKLM دیگر هم دیگر جابه‌جا نمی‌شوند؛ `RestoreStartupItem(item.Name, item.Location)` در UI؛ اسکن آیتم‌های `CleanC_` حالا نام نمایشی (بعد از آخرین `|`) را نشان می‌دهد و بر اساس آن dedupe می‌کند (سازگار با بکاپ‌های قدیمی). |
| 10 | ✅ | **P2**: `HeroBridge.ReadInt` خواندن صحیح اعداد اعشاری با fallback و `CcLog.Ignore`؛ نوشتن `clean_stats.json` اتمیک شد (tmp + File.Move + overwrite:true)؛ `CcLog.FlushCore` قفل `_fileGate` روی چرخش/append تا خطوط بدون آشفتگی نوشته شوند. |
| 11 | ✅ | **باگ ۲۶ (فلیکر کارت‌های هیرو)**: ریشه — شناور بودن `sev` بین ۲↔۳ در هر تیک سنسور و جابه‌جایی کارت شاخص با هر مرتب‌سازی. `sense()` حالا شدت چسبنده (`ramSevHold`/`hotPeakSev`) دارد و `view()` کارت شاخص را با `featuredId` ثابت نگه می‌دارد (مورد قبلی تا وقتی در لیست است و شدتش مجاز است، می‌ماند) + سومین سگمنت نوار وضعیت فقط «N بحرانی» است (بدون شمارش دوتایی). |
| 12 | ✅ | **P2-21 (سایز کاراکتر + poke هیرو)**: `applySize` clamp به بازه‌ی [۰٫۷–۱٫۲۵]؛ اسلایدر XAML `Maximum=1.25` + ToolTip؛ رویدادهای pointer (poke/jolt/clickR/dblclick) با `dz` (شعاع ۱۰۰px از مرکز بوم) گیت می‌شوند — ناحیه‌ی شفاف بیرون بوم دیگر فعال‌کننده نیست؛ cursor روی ناحیه‌ی زنده «pointer» و بیرون «default؛» `.st` دیگر تغییر خط نمی‌دهد (`min-width:0` + ellipsis) و دایره‌های وضعیت `flex:none`. |
| 13 | ✅ | **P2-17/18/25 (UI آیتم‌های ظاهری)**: `LiveCleanListView` ارتفاع ثابت ۲۲۰ حذف شد (بازه‌ی طبیعی لیست)؛ ComboBox زبان‌ها بدون پرچم/ایموجی (فقط نام زبان)؛ هر سه دکمه‌ی سورت deep-storage از ایموجی (🔻📅🔤) به `FontIcon E70D` با متن فارسی + متنِ fallback در کد هم بدون ایموجی. |
| 14 | ✅ | **P2-22**: پیل وضعیت سلامت ۱۰٫۵→۱۲ و `CharSizeValueText` ۱۱٫۵→۱۲ (نمودار تاریخچه از قبل فرمت LTR با `Language=en-US` داشت). |
| 15 | ✅ | **P2-23 (`DiskBreakdownBar`)**: با `FlowDirection=LeftToRight + Language=en-US` سگمنت‌ها (استفاده‌شده/قابل‌پاکسازی/آزاد) در RTL جابه‌جا و آینه نمی‌شوند و با ستون‌های ثابت و درصد‌های LTR همسو می‌مانند. |
| 16 | ✅ | **P3-24/25 (`App.xaml`)**: دیکشنری `HighContrast` اضافه شد — همه‌ی سطوح مات تعریف شده‌اند (کارت/هدر/پیل/ادمین‌بَج/toast/hero/گِیج/sparkline/accent) تا در طرح‌های High Contrast ویندوز متن خوانا بماند؛ ریسورس‌های مرده حذف شدند: ۳ براش `Boost*`، توکن‌های `CornerRadius` ×۳، `AppFontBold`/`AppFontSemiBold` و دوبارِ تکراریِ `AppAccentTint/Border` بیرون از ThemeDictionaries (با findstr بلااستفادگی صفر تأیید شد؛ نسخه‌ی تم‌محور برای ۱۴ رفرنس باقی است). |
| 17 | ✅ | کامنت ابزار «۷ روز» به «۳ روز» اصلاح شد تا با کد واقعی (`AddDays(-3)`) بخورد؛ call site بازگردانی استارت‌آپ در UI با دو آرگومان صحیح `(Name, Location)`. |

**باقی‌مانده‌ی فاز ۱۸ (راستی‌آزمایی زنده‌ی انسانی):**
- فعال‌کردن High Contrast ویندوز و مرور داشبورد/ابزارها (بدون بیلدِ مجدد — صرفاً check بصری).
- چرخ کامل «بهینه‌سازی» از هیرو با انتخاب محدود (تا پیام صادقانه‌ی «هیچ موردی انتخاب نشده» هم دیده شود).

---

**فاز ۱۹ — انجام شد (گزارش زنده‌ی کاربر: سبک‌سازی واقعی CPU+RAM، معطلی مرحله‌ی استارتاپ، حس‌سنجی «سیستم شلوغه»، هدفونِ گیمر — build پاک ۰/۰ + ۱۴۹ تست سبز):**

| # | وضعیت | موضوع |
| :-- | :--: | :--- |
| 1 | ✅ | **هدفونِ روی چشم‌ها (`CleanC.hero.html` §ACCD)**: مسیر اکسسوری گیمر (`M-34 5…` با cups در x∈±(۳۰..۳۸)، y∈(-۸..+۶)) دقیقاً روی مستطیل چشم‌ها می‌افتاد (چشم‌ها x∈±(۱۸..۳۶)، y∈(-۲۱..+۹)). هندسه بازنویسی شد: cups به طرفین سر — `x∈±(۶۰..۷۲)` در سطح «گوش» (y∈(-۲۴..+۱۰)) — و باند قوسی بالای سر با اوجِ y≈-۶۳/۵ (۴px بالاتر از لبه‌ی بالای بدنه، بدون تداخل با بج `SYM` در (۶۲,-۵۸)). |
| 2 | ✅ | **درگِ مضاعف اکسسوری**: `ACCD` داخل transform بدنه (bob+lean+drag) رسم می‌شود ولی خودش هم `translate(sp.dx, sp.dy)` داشت → جابه‌جایی ۲× موقع درگ. translate اضافه حذف شد. |
| 3 | ✅ | **«نه الکی» — سبک‌سازی واقعی پردازش خود هیرو** (`CleanC.hero.html`): حلقه‌ی `frame()` گارد اقتصادی دارد — وقتی `ramHigh` یا `ramv/cpu>=88` یا `CleanC.busy` یا `run` → نرخ رسم ~۳۰fps (نصف بارِ رندر)، در غیر این‌صورت ۶۰fps؛ تا ۱/۶ ثانیه اولِ بوت همیشه full speed تا انیمیشن ورود روان باشد؛ `document.hidden` نیز فریمِ کامل رد می‌شود و `requestAnimationFrame` به سرِ حلقه منتقل شد تا skip←حلقه نشکند. ⚠️ **نسخه‌ی اول (۱۱fps) روی سیستمِ «رم سنگین» همیشه فعال می‌ماند و کاراکتر را یخ‌نما می‌کرد (گزارشِ زنده‌ی کاربر، فاز ۱۹) — به ۳۰fps حداقلِ زنده اصلاح شد.** WebView2 واقعاً سبک‌تر می‌شود و کاراکتر هم کاملاً زنده دیده می‌شود. 🔴 **باگِ رشدِ نمایی پیدا و رفع شد (بعد از گزارشِ «همچنان هنگ»)**: هنگام انتقالِ `requestAnimationFrame` به سرِ حلقه، re-arm انتهایی (`x.restore();requestAnimationFrame(frame)` پایانِ frame) حذف نشده بود → در هر تیک **دو بار** زمان‌بندی مجدد → صفِ rAF هر فریمِ visible دو برابر می‌شد (رشد 2^N) → رندرر خفه، کاراکتر یخ/لگ، وب‌ویو سوزان (renderer انباشته ۷۱s CPU در ۱۷ دقیقه). re-arm انتهایی حذف شد — حالا فقط سرِ حلقه زمان‌بندی می‌کند. ➕ **نگهبانِ حلقه‌ی رسم**: WinUI/WebView2 گاهی بعد از occlusion/mینیمایز زنجیره‌ی rAF را برنمی‌گرداند و کاراکتر روی آخرین فریم یخ می‌زند (known bug)؛ watchdog با interval ۲ ثانیه‌ای چک می‌کند اگر >۲/۵s فریمی کشیده نشد ولی صفحه visible است، یک‌بار برای همیشه به حلقه‌ی setTimeout (۳۰fps) سوئیچ می‌کند و `RAF_STALL` را برای لاگ پست می‌کند. ➕ گزارش‌گر خطای JS: `window.onerror`/`unhandledrejection` بالای اسکریپت `js.error` می‌فرستد و `HeroHostApi` آن را در `ignored.log` با برچسب `Hero.JSError` ثبت می‌کند. |
| 4 | ✅ | **اکشن سریع «cpu» از «الکی» به واقعی تبدیل شد** (`MainWindow.Hero.cs` + `ProcessManagerService`): قبلاً فقط هشدار را ۵ دقیقه سرکوب می‌کرد و ادعای «بهینه شد» داشت بدون هیچ کاری! متد جدید `EaseHighCpuProcesses()`: دو نمونه‌گیری `TotalProcessorTime` (۵۰۰ms، روش مشابه نمونه‌گیر هیرو) → پردازه‌های ≥۱۲٪ به اولویت `BelowNormal` منتقل می‌شوند (بسته نمی‌شوند، کاملاً برگشت‌پذیر؛ لیست حیاتی + خودِ برنامه + Idle/BelowNormal/RealTime مستثنی) + `CleanBackgroundHelpers` (updaterهای بی‌پنجره)؛ InfoBar تعداد واقعی + پرمصرف‌ترین را گزارش می‌دهد. |
| 5 | ✅ | **اکشن سریع «ram» همزمان CPU را هم سبک می‌کند**: بعد از `BoostRamAsync`، بستنِ روندهای پس‌زمینه (`CleanBackgroundHelpers`) هم اجرا و در پیام InfoBar شمرده می‌شود — «هم رم هم پردازش، هم واقعی». |
| 6 | ✅ | **معطلی «در حال مرتب‌سازی استارتاپ …»** (`StartupOptimizerService`): ریشه — `OptimizeStartupAppsAsync` از `GetStartupItems()` استفاده می‌کرد که `StartupExtraSources.Scan` (چند ثانیه‌ای چون `schtasks /query CSV`) را بلاک می‌کند، ولی همان آیتم‌ها (RunOnce/TaskScheduler/StartupTask MSIX) در بهینه‌سازی خودکار عمداً رد می‌شوند. امضای `GetStartupItems(bool includeExtraSources = true)` اضافه شد: بهینه‌سازی خودکار با `false` صدا می‌زند (سه فراخوانی: هیرو گام ۷، SmartCleanOrchestrator، Quick Boost) و صفحه‌ی مدیر استارتاپ با پیش‌فرض `true` لیست کامل می‌گیرد. فراخوانیِ `Task.Run(method group)` در `MainWindow.xaml.cs` (خروجی CSV) با لامبدا صریح جابه‌جا شد (ایجاد overload → CS1503). |

---

## دستورهای مهم

```powershell
dotnet build CleanC.sln -c Release     # صفر خطا / صفر هشدار
dotnet test    CleanC.sln -c Release --no-build   # تست‌ها (۱۳۳ عدد)
.\publish.ps1                          # خروجی توزیع
dotnet list CleanC.sln package --vulnerable --include-transitive
```

> `dotnet build` را با `-p:Platform` اجرا نکنید؛ پیکربندی solution فقط `Any CPU` است.
