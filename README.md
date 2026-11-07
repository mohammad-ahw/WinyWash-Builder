<div align="center">

  <img src="assets/logo.png" alt="WinyWash Logo" width="160" />

  # WinyWash

  ### Next-Generation Native Windows 11 PC Optimizer & Deep Cleaner
  **بهینه‌ساز و پاک‌کننده هوشمند نسل جدید ویندوز ۱۱ با طراحی مات شیشه‌ای (Desktop Acrylic)**

  <p align="center">
    <a href="https://github.com/mohammad-ahw/WinyWash/releases/tag/v1.2.0">
      <img src="https://img.shields.io/badge/Release-v1.2.0-0078D4?style=for-the-badge&logo=github&logoColor=white" alt="Release v1.0.0" />
    </a>
    <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D4?style=for-the-badge&logo=windows11&logoColor=white" alt="Platform Windows" />
    <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10" />
    <img src="https://img.shields.io/badge/UI-WinUI%203%20%7C%20Desktop%20Acrylic-0284C7?style=for-the-badge&logo=windows" alt="WinUI 3 Acrylic" />
    <img src="https://img.shields.io/badge/License-Proprietary-475569?style=for-the-badge" alt="License Proprietary" />
  </p>

  <p align="center">
    <strong><a href="#-english">English</a></strong> • <strong><a href="#-فارسی-persian">فارسی (Persian)</a></strong> • <strong><a href="#-download--getting-started">Download</a></strong> • <strong><a href="#-key-features">Features</a></strong> • <strong><a href="#-building-from-source">Build</a></strong>
  </p>

</div>

---

## 🇬🇧 English

### Overview

**WinyWash** is a modern, ultra-fast, and privacy-respecting system cleaner and performance optimizer tailored for **Windows 11 and Windows 10**. Built natively with **.NET 10** and **WinUI 3 (Windows App SDK 2.5)**, WinyWash ditches legacy bloatware practices—no background daemon services, no aggressive telemetry, and no advertisements.

Featuring a signature **Desktop Acrylic glassmorphism interface**, WinyWash blends seamlessly into your Windows desktop while delivering low-level memory defragmentation, safe multi-layer junk scanning, and network DNS optimization.

---

### ✨ Key Features

- 🪟 **True Desktop Acrylic Glassmorphism**: Stunning translucent frosted glass UI that blends into your desktop wallpaper with native Windows 11 compositor effects (`DesktopAcrylicController`).
- 🎨 **Adaptive Wallpaper Luminance & Windows Accent Color**:
  - Dynamically samples the active desktop wallpaper brightness to adjust text contrast for **100% legibility** across any background (dark landscapes, vibrant themes, or bright snow/glaciers).
  - The primary boost button dynamically inherits your **Windows 11 Accent Color**, creating a cohesive OS-level visual harmony.
- ⚡ **1-Click Deep System Boost**:
  - Clears working set memory for inactive processes (`EmptyWorkingSet` Win32 API) without terminating tasks.
  - Flushes Windows DNS client cache (`DnsFlushResolverCache`).
  - Purges recommended temporary clutter in the background, reporting exactly what each step did (and what it could not do).
- 🧹 **Granular & Safe Disk Cleaner**:
  - Deep scans User & System Temp folders (`%TEMP%`, `C:\Windows\Temp`).
  - Cleans Windows Error Reporting crash dumps (`WER`), memory logs, and update caches.
  - Recycle Bin manager with itemized previews and real-time freed storage stats.
- 💻 **10 Built-in Tools**:
  - System Specs, App Uninstaller, Internet Speed Test, Personalization, Startup Manager, Windows Update, Driver Manager, DNS & Anti-Sanction, Privacy Debloater and Deep Storage — all inside a single in-tab navigation.
- 🛠️ **Built-in Windows Diagnostic Quick-Launcher**:
  - Instant 1-click access to Task Manager, Registry Editor, Services, Resource Monitor, Device Manager, and Windows Terminal.
- 📦 **100% Portable & Lightweight**:
  - Self-contained x64 deployment. Zero installation required, runs right out of the folder, and touches the registry only when you explicitly apply a tweak or turn on a feature such as start-with-Windows.

---

### 📸 Visual Showcase

<div align="center">

#### Dark Mode with Translucent Desktop Acrylic & Fluent Controls
![WinyWash Dark Acrylic](assets/screenshots/acrylic-dark.png)

#### Light Mode with Wallpaper-Adaptive Contrast
![WinyWash Light Mode](assets/screenshots/acrylic-light.png)

#### Itemized Safe Scan & Advanced Clean Preferences
![WinyWash Scan View](assets/screenshots/scan-expanded.png)

</div>

---

### 🚀 Download & Getting Started

1. Head to the **[Releases](https://github.com/mohammad-ahw/WinyWash/releases/tag/v1.2.0)** page.
2. Download the latest `WinyWash-Release-win-x64.zip`.
3. Extract the archive anywhere on your machine (e.g., `C:\Tools\WinyWash` or your Desktop).
4. Run `WinyWash.exe` (or run `CreateShortcut.bat` to place an icon on your desktop).

> [!NOTE]
> WinyWash requests administrator privileges (`requireAdministrator`) in order to clean system-level temp directories (`C:\Windows\Temp`) and perform memory working set compaction across all processes.

---

### 🛠️ Building from Source

Requirements: **.NET 10 SDK** + **Windows 11 SDK (10.0.26100)** + **Visual Studio 2022 (v17.14+) with the Windows App SDK workload**.

```powershell
# 1. Restore & build (the solution contains only CleanC.Core + CleanC.UI)
dotnet build WinyWash.sln -c Release

# 2. Publish a self-contained, portable win-x64 build
#    Output: publish\WinyWash\WinyWash.exe
.\publish.ps1

# 3. (optional) create a desktop shortcut pointing at the build
.\CreateShortcut.bat
```

> [!IMPORTANT]
> Always publish through `publish.ps1`. A plain `dotnet publish` without `-r win-x64`
> pulls the entire runtime graph (Linux, macOS, win-arm64, browser-wasm) and the output
> jumps from ~210 MB to ~300 MB of files that Windows will never load.

`publish.ps1` accepts `-Runtime win-arm64|win-x86`, `-Configuration Debug`, `-Symbols` (keeps `.pdb`) and `-NoReadyToRun`.

---

### 📊 WinyWash vs Traditional Cleaners

| Feature | WinyWash | Legacy Cleaners (CCleaner, etc.) |
| :--- | :---: | :---: |
| **Privacy & Security** | 🛡️ **100% Private (Zero Telemetry)** | ⚠️ Trackers & Data Collection |
| **UI Design** | 💎 **WinUI 3 Fluent + Acrylic Glass** | 🪟 Outdated Win32 / Electron |
| **Wallpaper Contrast Adaptation** | ✅ **Automatic** | ❌ No |
| **System Accent Color Sync** | ✅ **Automatic** | ❌ No |
| **Background Daemons / Services** | ❌ **Zero (Clean)** | ⚠️ Yes (Persistent telemetry) |
| **Bundled Ads or Offers** | ❌ **None** | ⚠️ Yes (Upsells, adware) |
| **Native Working Set Compaction** | ✅ **Safe Win32 API** | ⚠️ Aggressive kill |
| **Built-in Tool Suite** | ✅ **10 tools included free** | ⚠️ Paid edition only |

---

<br/>

## 🇮🇷 فارسی (Persian)

### معرفی نرم‌افزار

**وینی‌وش (WinyWash)** یک ابزار بهینه‌ساز، پاک‌کننده فایل‌های زائد و تقویت‌کننده عملکرد سیستم برای **ویندوز ۱۱ و ۱۰** است که با معماری مدرن **.NET 10** و **WinUI 3 (Windows App SDK 2.5)** توسعه یافته است. بر خلاف نرم‌افزارهای تجاری رایج که سرشار از تبلیغات آزاردهنده، سرویس‌های مخفی در پس‌زمینه و ردیاب‌ها هستند، WinyWash کاملاً **سبک، امن، حفظ‌کننده حریم خصوصی و بدون هیچ‌گونه سربار اضافی** است.

این نرم‌افزار با بهره‌گیری از فناوری بومی **Desktop Acrylic** پنجره‌ای شیشه‌ای و چشم‌نواز ایجاد می‌کند که پس‌زمینه دسکتاپ شما را به زیباترین شکل با بلور ملایم به نمایش می‌گذارد.

---

### 🌟 قابلیت‌های برجسته

- 🪟 **طراحی مدرن شیشه‌ای مات (Desktop Acrylic)**: بهره‌گیری از ترنسپرنسی عمیق و بلور شیشه‌ای طبیعی ویندوز ۱۱ (`DesktopAcrylicController`) منطبق بر زبان طراحی Fluent Design.
- 🎨 **هوش تطبیقی با والپیپر و رنگ اختصاصی ویندوز (Accent Color)**:
  - سنجش خودکار میزان روشنایی (Luminance) تصویر زمینه برای تنظیم هوشمند کنتراست متون (سفید یا مشکی) به منظور **خوانایی ۱۰۰٪** در انواع والپیپرها (از تصاویر برفی و بیابانی روشن تا والپیپرهای تاریک شب).
  - هماهنگی خودکار دکمه بهینه‌سازی اصلی با **Accent Color انتخابی ویندوز شما** (رنگ استخراج شده از تصویر زمینه).
- ⚡ **بهینه‌سازی سریع ۱-کلیکی (Quick Boost)**:
  - آزادسازی آنی رم با تکنیک استاندارد `EmptyWorkingSet` از حافظه پروسه‌های غیرفعال بدون بستن برنامه‌های شما.
  - تخلیه و نوسازی آنی کش آدرس‌های شبکه (`Flush DNS`).
  - پاکسازی فایل‌های موقت در پس‌زمینه، با گزارش شفاف از نتیجه‌ی هر مرحله و مقدار واقعی فضای آزادشده.
- 🧹 **پاک‌کننده عمیق و هوشمند دیسک**:
  - اسکن عمیق و ایمن پوشه‌های موقت سیستم و کاربر (`%TEMP%` و `C:\Windows\Temp`).
  - پاکسازی فایل‌های گزارش خرابی ویندوز (`WER Crash Dumps`) و لاگ‌های زائد سیستمی.
  - مدیریت و تخلیه سطل بازیافت با امکان بازبینی جزئیات و پیش‌نمایش حجم آزاد شده.
- 💻 **۱۰ ابزار کامل داخل برنامه**:
  - مشخصات سیستم، حذف برنامه‌ها، تست سرعت اینترنت، شخصی‌سازی، مدیریت استارتاپ، بروزرسانی ویندوز، مدیریت درایورها، DNS و ضد تحریم، پاک‌سازی حریم خصوصی و فضای عمیق دیسک — همه در یک ناوبری تب‌دار.
- 🛠️ **دسترسی سریع به ابزارهای مدیریتی سیستم‌عامل**:
  - لانچر مستقیم و ۱-کلیکی Task Manager، ویرایشگر رجیستری، Device Manager، مدیریت سرویس‌ها، Resource Monitor و ترمینال ویندوز.
- 📦 **کاملاً پرتابل و بدون ردپا**:
  - بدون نیاز به نصب (Portable) و قابل اجرا از روی فلش یا هر پوشه دلخواه؛ در رجیستری فقط وقتی چیزی ثبت می‌شود که خودتان توئیکی را اعمال کنید یا گزینه‌ای مثل «شروع با ویندوز» را روشن کنید.

---

### 📥 راهنمای دریافت و اجرا

1. به بخش **[Releases](https://github.com/mohammad-ahw/WinyWash/releases/tag/v1.2.0)** مراجعه کنید.
2. نسخه `WinyWash-Release-win-x64.zip` را دانلود کنید.
3. فایل فشرده را در محل مورد نظر خود (مانند `C:\Tools\WinyWash` یا دسکتاپ) استخراج کنید.
4. فایل `WinyWash.exe` را اجرا کنید (یا با اجرای `CreateShortcut.bat` آیکون میانبر آن را به دسکتاپ خود اضافه کنید).

> [!NOTE]
> نرم‌افزار برای پاکسازی دایرکتوری‌های سیستمی ویندوز و بهینه‌سازی حافظه رم پروسه‌های سیستمی، نیازمند دسترسی ادمین (`Run as Administrator`) می‌باشد که به صورت خودکار از کاربر درخواست می‌شود.

---

### 🛠️ نحوه ساخت از سورس

پیش‌نیازها: **.NET 10 SDK** + **Windows 11 SDK (10.0.26100)** + **Visual Studio 2022 با workload مربوط به Windows App SDK**.

```powershell
# ۱. بازیابی و build کامل (راه‌حل فقط شامل CleanC.Core و CleanC.UI است)
dotnet build WinyWash.sln -c Release

# ۲. ساخت نسخه نهایی قابل حمل و self-contained برای win-x64
#    خروجی: publish\WinyWash\WinyWash.exe
.\publish.ps1

# ۳. (اختیاری) ساخت میانبر دسکتاپ روی همین خروجی
.\CreateShortcut.bat
```

> [!IMPORTANT]
> حتماً از `publish.ps1` استفاده کنید. اجرای `dotnet publish` بدون `-r win-x64`
> کل گراف runtime را دانلود می‌کند (لینوکس، مک، win-arm64 و …) و حجم خروجی از
> حدود ۲۱۰ مگابایت به بیش از ۳۰۰ مگابایت می‌رسد؛ بخشی که ویندوز هرگز بارگذاری نمی‌کند.

اسکریپت `publish.ps1` پارامترهای `-Runtime win-arm64|win-x86`، `-Configuration Debug`،
`-Symbols` (نگه‌داشتن فایل‌های pdb) و `-NoReadyToRun` را پشتیبانی می‌کند.

---

### ⚖️ حقوق نرم‌افزار و کپی‌رایت

تمامی حقوق این نرم‌افزار محفوظ و متعلق به **محمد (mohammad-ahw)** می‌باشد. هرگونه کپی‌برداری غیرمجاز، مهندسی معکوس یا انتشار سورس‌کد ممنوع است.

<div align="center">

Copyright © 2026 **[Mohammad (mohammad-ahw)](https://github.com/mohammad-ahw)**. All Rights Reserved.

</div>


---

## 🔒 Privacy & Network Transparency

WinyWash values your privacy above all else. **The application contains zero telemetry, zero analytics tracking, and zero personal data collection.**

For complete transparency, here is the full list of external network endpoints used exclusively for optional, user-facing features:

| Endpoint / Domain | Purpose | Data Transmitted |
| :--- | :--- | :--- |
| `open-meteo.com` | Fetches local temperature and weather condition codes for the desk companion | Latitude & longitude coordinates only (no personal data) |
| `ipwho.is` / `ipapi.co` | Automatic city detection (only used if you don't specify a city manually) | IP address strictly for city name lookup |
| News RSS Feeds (`theverge.com`, `techcrunch.com`, `wired.com`, `arstechnica.com`, `yahoo.com`, `akharinkhabar.ir`, `digiato.com`, `zoomit.ir`) | Retrieves public headlines and reader mode articles | None (standard anonymous HTTP GET) |
| `speed.cloudflare.com` / `cachefly` | Measures network download latency and bandwidth (only when you explicitly click "Run Speed Test") | Anonymous byte download for throughput calculation |

> [!IMPORTANT]
> All core cleaning, system scanning, RAM optimization, startup management, and registry checks run **100% locally and offline** on your machine.
