# پرامپت آماده برای ارسال به Claude (همراه با فایل‌های این پوشه)

می‌توانید متن زیر را به همراه فایل‌های این پوشه (یا فایل زیپ آن) مستقیماً در Claude کپی و ارسال کنید:

---

### متن پرامپت (فارسی):

```text
سلام Claude. من فایل‌های رابط کاربری (UI) یک نرم‌افزار دسکتاپ مدرن ویندوز ۱۱ به نام CleanC رو در اختیارت قرار دادم.
این نرم‌افزار با WinUI 3 (Windows App SDK) و زبان C# و XAML نوشته شده و تمامی کدهای بک‌اند، فراخوانی‌های سیستمی و رجیستری از آن حذف شده‌اند تا تو صرفاً روی بازطراحی ظاهر (UI/UX) و زیباتر کردن آن تمرکز کنی.

🎨 زبان طراحی و مشخصات فعلی پروژه:
۱. سبک طراحی: بومی ویندوز ۱۱ (Windows 11 Fluent Design 2) با افکت شیشه‌ای و مات شفاف (Desktop Acrylic / Mica) و گوشه‌های گرد.
۲. جهت نوشتار: راست‌به‌چپ (RTL) به زبان فارسی و با فونت وزیرمتن (Vazirmatn). مقادیر عددی و انگلیسی به شکل استاندارد چپ‌به‌راست نمایش داده می‌شوند.
۳. رنگ‌بندی: پشتیبانی کامل از تم تیره (Dark) و تم روشن (Light) از طریق منابع رنگی در App_Themes_And_Styles.xaml.

📁 فایل‌های همراه:
- App_Themes_And_Styles.xaml: منابع رنگ، براش‌های گرادیان و شیشه‌ای، فونت‌ها و استایل دکمه‌ها.
- MainWindow_Complete.xaml: کل ساختار XAML برنامه شامل هدر، داشبورد و صفحات همه ابزارها.
- Dynamic_Card_Templates.cs: کدهای ساخت کارت‌های داینامیک (لیست آپدیت‌ها، درایورها، برنامه‌های استارتاپ، آنیستالر، تحریم‌شکن DNS و...).
- پوشه Sections: فایل‌های مجزای XAML برای هر صفحه و ابزار (برای بازطراحی جزء‌به‌جزء).
- README_FOR_CLAUDE.md: راهنمای دقیق اجزا و ساختار برنامه.

🎯 خواسته من از تو:
می‌خوام این رابط کاربری رو بازطراحی کنی تا فوق‌العاده شیک‌تر، مدرن‌تر، چشم‌نوازتر و با ارگونومی بهتری به سبک جدیدترین طراحی‌های مایکروسافت (Fluent 2) دربیاد.
به طور خاص:
۱. صفحه اصلی (داشبورد) شامل کارت اصلی Boost، گیج‌های سخت‌افزاری (RAM، دیسک، CPU) و بخش وضعیت سلامت سیستم چطور می‌تواند جذاب‌تر و خلوت‌تر شود؟
۲. هدر، تب‌بار معلق و نوار بالا چگونه مدرن‌تر و ظریف‌تر دیده شوند؟
۳. کارت‌های لیست‌ها در ابزارها (کارت‌های آپدیت ویندوز، درایورها، برنامه‌های استارتاپ، تنظیمات حریم خصوصی و تست سرعت) چطور زیباتر چیدمان شوند؟

لطفاً ایده‌ها و کدهای XAML یا C# پیشنهادی‌ات را با ذکر علت تغییرات ارائه بده.
```

---

### Prompt Text (English - Alternative):

```text
Hello Claude. I have provided the presentation and UI files for CleanC, a modern Windows 11 system optimizer desktop app built with WinUI 3 and C#/XAML.
All backend logic, Win32 APIs, and registry calls have been stripped out so you can focus entirely on redesigning the visual appearance, UI/UX ergonomics, and aesthetics.

Key Design Elements:
- Windows 11 Native Fluent Design 2 (Desktop Acrylic / Mica Alt frosted glass material).
- Right-to-Left (RTL) Persian typography (Vazirmatn) with crisp Latin numerical chips.
- Seamless Custom Titlebar, Segmented Pill switcher, Bento grid hardware metrics, and dynamic utility cards.

Files provided:
- App_Themes_And_Styles.xaml (Theme dictionaries & brushes)
- MainWindow_Complete.xaml (Master XAML layout)
- Dynamic_Card_Templates.cs (WinUI 3 card building templates)
- Sections/ (Individual page XAML files from Dashboard to all tools)
- README_FOR_CLAUDE.md (Detailed architectural guide)

Please review the design and propose a modern, visually stunning overhaul with improved layouts, spacing, micro-details, and refined typography. You can provide XAML snippets or full redesigned page code.
```
