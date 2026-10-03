# CleanC - UI Architecture & Design System Guide (For Claude)

Welcome! This folder contains **ONLY the presentation and visual design files** for **CleanC**, an advanced Windows 11 system optimizer, cleaner, and utility suite.

All backend logic, Win32 APIs, WMI queries, Registry modifications, and CLI tools have been **completely stripped out**. You only need to focus on **UI/UX, visual layout, animations, typography, glassmorphism/Mica, and modern Windows 11 Fluent 2 aesthetics**.

---

## 🎨 Design Language & Aesthetic Guidelines

CleanC is built with **WinUI 3 (Windows App SDK)** and follows the **Windows 11 Native Fluent Design 2** guidelines:
1. **Backdrop & Materials:**
   - Native **Desktop Acrylic** / **Mica Alt** frosted glass canvas (`WindowCanvasBrush = #00000000`).
   - Cards use translucent frosted glass brushes (`AppCardBackgroundBrush`, `AppCardBorderBrush` with specular rim highlights).
   - High contrast text (`TextFillColorPrimaryBrush`, `TextFillColorSecondaryBrush`) so text never washes out over wallpapers.
2. **Typography & RTL:**
   - **Primary Font:** `Vazirmatn` (with fallback to `Segoe UI Variable` and `Tahoma`).
   - **FlowDirection:** `RightToLeft` (Native Persian UI).
   - Numeric and English values (e.g. `84.5 Mbps`, `10.0.26300.9457`, `nv_dispi.inf`) remain crisp LTR / Latin.
3. **Corner Radii & Elevation:**
   - Cards: `CornerRadius="10"` to `"14"`.
   - Small buttons/pills: `CornerRadius="6"` to `"8"`.
   - Header segmented switcher & badges: `CornerRadius="16"` to `"20"`.
   - Theme shadows and subtle specular border strokes.
4. **Icons:**
   - Windows 11 native **Segoe Fluent Icons** (`FontIcon Glyph="&#x...;"`).

---

## 📁 Folder Contents & File Map

### Core Theme & Layout
* **`App_Themes_And_Styles.xaml`** (`App.xaml`)
  Defines all theme brushes (`Light` and `Dark` ResourceDictionaries), glass cards, button styles, typography, and corner radius tokens.
* **`MainWindow_Complete.xaml`** (`MainWindow.xaml`)
  The complete master XAML layout containing the titlebar, header, dashboard, and all tool pages.
* **`Dynamic_Card_Templates.cs`**
  Clean C# WinUI 3 methods that construct dynamic cards (Windows Update items, App Uninstaller rows, Driver items, Startup toggles, DNS presets, Hardware specs chips, Logical drives). Contains **zero backend code**—only visual control tree building!
* **`JunkItemViewModel.cs`**
  UI data-binding properties for expandable scan categories and checkboxes.

### Modular Sections (`Sections/` folder)
For easy redesigning view-by-view:
1. **`01_Window_Header_And_TitleBar.xaml`**: Custom titlebar, logo, drag region, quick actions, theme toggle, and floating segmented switcher ("داشبورد بهینه‌سازی" vs "ابزارهای سیستمی ویندوز").
2. **`02_Dashboard_HomePage.xaml`**: Hero 1-Click clean card, visual RAM ring, live cleaning sequence panel, post-cleaning report card, Bento metrics grid (RAM, SSD, CPU), and advanced items expander.
3. **`03_Tools_Launcher_Hub.xaml`**: 4-section grid launching all 10 utility tools with color-coded badges and icons.
4. **`04_SystemSpecs_Tool.xaml`**: Detailed hardware specs, CPU/GPU chips, motherboard, DirectX, and drive partitions.
5. **`05_AppUninstaller_Tool.xaml`**: App search, sort filter, batch queue action bar, and app list cards.
6. **`06_InternetSpeedTest_Tool.xaml`**: Network metadata chips (IP, ISP, Location), central speed dial gauge, Ping/Download/Upload cards, and test controls.
7. **`07_Personalization_Tool.xaml`**: Windows 11 Classic Context Menu switcher, system themes, and visual effects.
8. **`08_StartupManager_Tool.xaml`**: Startup programs list, impact badges, disable/enable toggles, and faded disabled items.
9. **`09_WindowsUpdate_Tool.xaml`**: Status banner, 5-week pause button, service state toggle, cache flush, and pending updates list (with purple Next-Gen OS upgrade cards).
10. **`10_DriverManager_Tool.xaml`**: DriverStore stats, duplicate driver cleanup banner, batch deletion bar, search, and device cards.
11. **`11_DnsManager_Tool.xaml`**: Active DNS card, DHCP reset, and presets (Shecan, Electro, 403, Radar, Cloudflare).
12. **`12_PrivacyDebloater_Tool.xaml`**: Auto-save indicator badge, telemetry & Bing search toggles, and bulk action buttons.
13. **`13_Toasts_And_Overlays.xaml`**: Floating in-app toast notification card and dialogs.

---

## 💡 What You Can Redesign
* **Layout & Structure:** Change grid layouts, card groupings, bento boxes, or navigation flows.
* **Modern Visual Styles:** Introduce smoother gradients, micro-interactions, cleaner headers, improved gauges, or enhanced telemetry graphs.
* **Card Ergonomics:** Improve readability, button placements, badge styling, and spacing.
* **Responsiveness:** Optimize spacing and scrolling behaviors for different window dimensions.

Feel free to suggest changes either as complete XAML code, specific component snippets, or design suggestions!
