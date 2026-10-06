// src/CleanC.UI/ViewModels/SettingsViewModel.cs
// C6 — ViewModel صفحه‌ی «تنظیمات برنامه» با CommunityToolkit.Mvvm.
// وضعیت در VM زندگی می‌کند، نوشتن در تنظیمات از مسیر ISettingsStore انجام می‌شود،
// و اثراتِ سمت رابط کاربری (اعمال تم، InfoBar هشدار حذف قطعی) از طریق رویداد به
// MainWindow می‌رسد تا VM وابسته به WinUI نشود.

using System;
using CleanC.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CleanC.UI.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsStore _store;

        // محافظ hydration: تا وقتی داریم مقادیر را از تنظیمات می‌خوانیم،
        // هیچ رویدادِ ذخیره/اعمالِ جانبی نباید منتشر شود.
        private bool _loading;

        public SettingsViewModel(ISettingsStore store)
        {
            _store = store;

            // مقادیر پیش‌فرضِ کنترل‌ها تا فراخوانی Hydrate (که صفحه همان ابتدا انجام می‌دهد).
            // _loading روشن است تا این مقداردهی، تنظیماتِ خوانده‌شده از دیسک را بازنویسی نکند.
            _loading = true;
            Theme = "System";
            DeleteMode = "RecycleBin";
            CloseToTray = true;
            SavePlacement = true;
            SaveLastView = true;
            _loading = false;
        }

        /// <summary>لایه‌ی نمایش با این رویداد ذخیره‌ی تنظیمات را debounce می‌کند.</summary>
        public event Action? PersistRequested;

        /// <summary>تغییر تم باید در لایه‌ی نمایش روی ElementTheme اعمال شود.</summary>
        public event Action<string>? ThemeChangeRequested;

        /// <summary>تغییر رفتار حذف — برای نشان دادن هشدار حالت Permanent.</summary>
        public event Action<string>? DeleteModeChangeRequested;

        /// <summary>تغییر زبان برنامه.</summary>
        public event Action<string>? LanguageChangeRequested;

        // ── وضعیت‌های قابل مشاهده ──────────────────────────────────────
        // از syntax «partial property» استفاده می‌شود (نه فیلد) تا کدِ تولیدیِ
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

        // ── حالت‌های محاسبه‌شده برای RadioButton ها (x:Bind OneWay) ─────

        public bool IsThemeSystem => string.Equals(Theme, "System", StringComparison.OrdinalIgnoreCase);
        public bool IsThemeLight => string.Equals(Theme, "Light", StringComparison.OrdinalIgnoreCase);
        public bool IsThemeDark => string.Equals(Theme, "Dark", StringComparison.OrdinalIgnoreCase);

        public bool IsDeleteRecycle => string.Equals(DeleteMode, "RecycleBin", StringComparison.OrdinalIgnoreCase);
        public bool IsDeleteQuarantine => string.Equals(DeleteMode, "Quarantine", StringComparison.OrdinalIgnoreCase);
        public bool IsDeletePermanent => string.Equals(DeleteMode, "Permanent", StringComparison.OrdinalIgnoreCase);

        /// <summary>پر کردن VM از تنظیماتِ ذخیره‌شده؛ هیچ ذخیره/اعمالِ جانبی رخ نمی‌دهد.</summary>
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
            }
            finally
            {
                _loading = false;
            }

            // سرویس‌ها باید با تنظیماتِ خوانده‌شده همگام شوند (حتی در اولین اجرا)
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

        partial void OnSelectedLanguageCodeChanged(string value)
        {
            if (_loading) return;
            _store.Current.Language = value;
            LanguageChangeRequested?.Invoke(value);
            PersistRequested?.Invoke();
        }
    }
}
