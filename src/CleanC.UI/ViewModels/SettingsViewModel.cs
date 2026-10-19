// src/CleanC.UI/ViewModels/SettingsViewModel.cs
// ViewModel صفحه ی «تنظیمات برنامه» با CommunityToolkit.Mvvm.
// وضعیت در VM زندگی می کند، نوشتن در تنظیمات از مسیر ISettingsStore انجام می شود،
// و اثراتِ سمت رابط کاربری (اعمال تم، InfoBar هشدار حذف قطعی) از طریق رویداد به
// MainWindow می رسد تا VM وابسته به WinUI نشود.

using System;
using CleanC.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CleanC.UI.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsStore _store;

        // محافظ hydration: تا وقتی داریم مقادیر را از تنظیمات می خوانیم،
        // هیچ رویدادِ ذخیره/اعمالِ جانبی نباید منتشر شود.
        private bool _loading;

        public SettingsViewModel(ISettingsStore store)
        {
            _store = store;

            // مقادیر پیش فرضِ کنترل ها تا فراخوانی Hydrate (که صفحه همان ابتدا انجام می دهد).
            // _loading روشن است تا این مقداردهی، تنظیماتِ خوانده شده از دیسک را بازنویسی نکند.
            _loading = true;
            Theme = "System";
            DeleteMode = "RecycleBin";
            CloseToTray = true;
            SavePlacement = true;
            SaveLastView = true;
            _loading = false;
        }

        /// <summary>لایه ی نمایش با این رویداد ذخیره ی تنظیمات را debounce می کند.</summary>
        public event Action? PersistRequested;

        /// <summary>تغییر تم باید در لایه ی نمایش روی ElementTheme اعمال شود.</summary>
        public event Action<string>? ThemeChangeRequested;

        /// <summary>تغییر رفتار حذف — برای نشان دادن هشدار حالت Permanent.</summary>
        public event Action<string>? DeleteModeChangeRequested;

        /// <summary>تغییر زبان برنامه.</summary>
        public event Action<string>? LanguageChangeRequested;

        // ── وضعیت های قابل مشاهده ──────────────────────────────────────
        // از syntax «partial property» استفاده می شود (نه فیلد) تا کدِ تولیدیِ
        // MVVM Toolkit برای WinUI/CsWinRT سازگار با AOT باشد و هشدار MVVMTK0045 ندهد.

        [ObservableProperty]
        public partial string Theme { get; set; }

        [ObservableProperty]
        public partial string DeleteMode { get; set; }

        [ObservableProperty]
        public partial bool CloseToTray { get; set; }

        [ObservableProperty]
        public partial bool MinimizeToTray { get; set; }

        [ObservableProperty]
        public partial string SelectedLanguageCode { get; set; } = "Auto";

        [ObservableProperty]
        public partial bool SavePlacement { get; set; }

        [ObservableProperty]
        public partial bool SaveLastView { get; set; }

        [ObservableProperty]
        public partial bool SaveScanSelections { get; set; }

        // ── کاراکتر هیرو (سکوی ارائه داخل WebView2) ─────────────────────
        // null ماندنِ فیلد متناظر در AppSettings یعنی «دست نخورده»: در آن حالت چیزی
        // به JS فرستاده نمی شود و خود CleanC.hero.html از پیش فرض خودش استفاده می کند.
        // به همین دلیل Hydrate فقط می خواند و هیچ مقداری را به تنظیمات برنمی گرداند.

        /// <summary>لحن گفتار: joke | calm | serious</summary>
        [ObservableProperty]
        public partial string CharacterTone { get; set; } = "joke";

        /// <summary>اسمی که کاراکتر کاربر را با آن صدا می زند.</summary>
        [ObservableProperty]
        public partial string CharacterUserName { get; set; } = "";

        /// <summary>اسم خود کاراکتر (پیش فرض خود HTML).</summary>
        [ObservableProperty]
        public partial string CharacterName { get; set; } = "Winy";

        /// <summary>شهر ماژول آب و هوا؛ خالی یعنی تشخیص خودکار.</summary>
        [ObservableProperty]
        public partial string CharacterCity { get; set; } = "";

        /// <summary>کم تحرک: کمتر حرف می زند، ذرات و انیمیشن های تزئینی خاموش می شوند.</summary>
        [ObservableProperty]
        public partial bool CharacterCalm { get; set; }

        /// <summary>عکس العمل سریع (کوتاه کردن انیمیشن ها).</summary>
        [ObservableProperty]
        public partial bool CharacterFast { get; set; }

        /// <summary>شدت رفتارها: off | low | mid | high</summary>
        [ObservableProperty]
        public partial string CharacterIntensity { get; set; } = "mid";

        [ObservableProperty]
        public partial bool CharacterBubble { get; set; } = true;

        [ObservableProperty]
        public partial bool CharacterEnv { get; set; } = true;

        [ObservableProperty]
        public partial bool CharacterSound { get; set; }

        /// <summary>کد اکسسوری (ACC)؛ none یعنی بدون اکسسوری.</summary>
        [ObservableProperty]
        public partial string CharacterLook { get; set; } = "none";

        /// <summary>کد اکسسوری مناسبتی؛ خالی یعنی غیرفعال.</summary>
        [ObservableProperty]
        public partial string CharacterOccLook { get; set; } = "";

        /// <summary>مقیاس اندازهٔ کاراکتر (0.7 تا 1.4).</summary>
        [ObservableProperty]
        public partial double CharacterSize { get; set; } = 1.0;

        /// <summary>افست روز تقویم قمری برای مناسبت ها: -1 | 0 | 1</summary>
        [ObservableProperty]
        public partial int CharacterLun { get; set; }

        // ── حالت های محاسبه شده برای RadioButton ها (x:Bind OneWay) ─────

        public bool IsThemeSystem => string.Equals(Theme, "System", StringComparison.OrdinalIgnoreCase);
        public bool IsThemeLight => string.Equals(Theme, "Light", StringComparison.OrdinalIgnoreCase);
        public bool IsThemeDark => string.Equals(Theme, "Dark", StringComparison.OrdinalIgnoreCase);

        public bool IsDeleteRecycle => string.Equals(DeleteMode, "RecycleBin", StringComparison.OrdinalIgnoreCase);
        public bool IsDeleteQuarantine => string.Equals(DeleteMode, "Quarantine", StringComparison.OrdinalIgnoreCase);
        public bool IsDeletePermanent => string.Equals(DeleteMode, "Permanent", StringComparison.OrdinalIgnoreCase);

        /// <summary>پر کردن VM از تنظیماتِ ذخیره شده؛ هیچ ذخیره/اعمالِ جانبی رخ نمی دهد.</summary>
        public void Hydrate()
        {
            _loading = true;
            try
            {
                AppSettings s = _store.Current;

                Theme = s.Theme;
                SelectedLanguageCode = string.IsNullOrWhiteSpace(s.Language) ? "Auto" : s.Language;
                DeleteMode = SafeDeleteService.ModeToString(SafeDeleteService.ParseMode(s.DeleteMode));
                CloseToTray = s.CloseToTray;
                MinimizeToTray = s.MinimizeToTray;
                SavePlacement = s.SaveWindowPlacement;
                SaveLastView = s.SaveLastView;
                SaveScanSelections = s.SaveScanSelections;

                CharacterTone = s.CharacterTone ?? "joke";
                CharacterUserName = s.CharacterUserName ?? "";
                CharacterName = string.IsNullOrWhiteSpace(s.CharacterName) ? "Winy" : s.CharacterName;
                CharacterCity = s.CharacterCity ?? "";
                CharacterCalm = s.CharacterCalm ?? false;
                CharacterFast = s.CharacterFast ?? false;
                CharacterIntensity = s.CharacterIntensity ?? "mid";
                CharacterBubble = s.CharacterBubble ?? true;
                CharacterEnv = s.CharacterEnv ?? true;
                CharacterSound = s.CharacterSound ?? false;
                CharacterLook = s.CharacterLook ?? "none";
                CharacterOccLook = s.CharacterOccLook ?? "";
                CharacterSize = s.CharacterSize ?? 1.0;
                CharacterLun = s.CharacterLun ?? 0;
            }
            finally
            {
                _loading = false;
            }

            // سرویس ها باید با تنظیماتِ خوانده شده همگام شوند (حتی در اولین اجرا)
            SafeDeleteService.DefaultMode = SafeDeleteService.ParseMode(DeleteMode);
        }

        // ── نوشتنِ تغییرات ─────────────────────────────────────────────

        partial void OnThemeChanged(string value)
        {
            OnPropertyChanged(nameof(IsThemeSystem));
            OnPropertyChanged(nameof(IsThemeLight));
            OnPropertyChanged(nameof(IsThemeDark));

            if (_loading) return;

            _store.Current.Theme = value;
            ThemeChangeRequested?.Invoke(value);
            PersistRequested?.Invoke();
        }

        partial void OnDeleteModeChanged(string value)
        {
            OnPropertyChanged(nameof(IsDeleteRecycle));
            OnPropertyChanged(nameof(IsDeleteQuarantine));
            OnPropertyChanged(nameof(IsDeletePermanent));

            if (_loading) return;

            _store.Current.DeleteMode = value;
            SafeDeleteService.DefaultMode = SafeDeleteService.ParseMode(value);
            DeleteModeChangeRequested?.Invoke(value);
            PersistRequested?.Invoke();
        }

        partial void OnCloseToTrayChanged(bool value)
        {
            if (_loading) return;
            _store.Current.CloseToTray = value;
            PersistRequested?.Invoke();
        }

        partial void OnMinimizeToTrayChanged(bool value)
        {
            if (_loading) return;
            _store.Current.MinimizeToTray = value;
            PersistRequested?.Invoke();
        }

        partial void OnSavePlacementChanged(bool value)
        {
            if (_loading) return;
            _store.Current.SaveWindowPlacement = value;
            if (!value)
            {
                _store.Current.WindowX = int.MinValue;
                _store.Current.WindowY = int.MinValue;
            }
            PersistRequested?.Invoke();
        }

        partial void OnSaveLastViewChanged(bool value)
        {
            if (_loading) return;
            _store.Current.SaveLastView = value;
            if (!value)
            {
                _store.Current.LastView = "dashboard";
            }
            PersistRequested?.Invoke();
        }

        partial void OnSaveScanSelectionsChanged(bool value)
        {
            if (_loading) return;
            _store.Current.SaveScanSelections = value;
            PersistRequested?.Invoke();
        }

        // ── کاراکتر: هر تغییر بلافاصله در تنظیمات می نشیند؛ لایه ی نمایش با
        //    PersistRequested ذخیره می کند و همان رویداد پیام settings را به JS هم می فرستد.

        partial void OnCharacterToneChanged(string value)
        {
            if (_loading) return;
            _store.Current.CharacterTone = value;
            PersistRequested?.Invoke();
        }

        partial void OnCharacterUserNameChanged(string value)
        {
            if (_loading) return;
            _store.Current.CharacterUserName = value;
            PersistRequested?.Invoke();
        }

        partial void OnCharacterNameChanged(string value)
        {
            if (_loading) return;
            _store.Current.CharacterName = value;
            PersistRequested?.Invoke();
        }

        partial void OnCharacterCityChanged(string value)
        {
            if (_loading) return;
            _store.Current.CharacterCity = value;
            PersistRequested?.Invoke();
        }

        partial void OnCharacterCalmChanged(bool value)
        {
            if (_loading) return;
            _store.Current.CharacterCalm = value;
            PersistRequested?.Invoke();
        }

        partial void OnCharacterFastChanged(bool value)
        {
            if (_loading) return;
            _store.Current.CharacterFast = value;
            PersistRequested?.Invoke();
        }

        partial void OnCharacterIntensityChanged(string value)
        {
            if (_loading) return;
            _store.Current.CharacterIntensity = value;
            PersistRequested?.Invoke();
        }

        partial void OnCharacterBubbleChanged(bool value)
        {
            if (_loading) return;
            _store.Current.CharacterBubble = value;
            PersistRequested?.Invoke();
        }

        partial void OnCharacterEnvChanged(bool value)
        {
            if (_loading) return;
            _store.Current.CharacterEnv = value;
            PersistRequested?.Invoke();
        }

        partial void OnCharacterSoundChanged(bool value)
        {
            if (_loading) return;
            _store.Current.CharacterSound = value;
            PersistRequested?.Invoke();
        }

        partial void OnCharacterLookChanged(string value)
        {
            if (_loading) return;
            _store.Current.CharacterLook = value;
            PersistRequested?.Invoke();
        }

        partial void OnCharacterOccLookChanged(string value)
        {
            if (_loading) return;
            _store.Current.CharacterOccLook = value;
            PersistRequested?.Invoke();
        }

        partial void OnCharacterSizeChanged(double value)
        {
            if (_loading) return;
            _store.Current.CharacterSize = Math.Round(value, 2);
            PersistRequested?.Invoke();
        }

        partial void OnCharacterLunChanged(int value)
        {
            if (_loading) return;
            _store.Current.CharacterLun = value;
            PersistRequested?.Invoke();
        }

        partial void OnSelectedLanguageCodeChanged(string value)
        {
            if (_loading) return;
            _store.Current.Language = value;
            LanguageChangeRequested?.Invoke(value);
            PersistRequested?.Invoke();
        }
    }
}
