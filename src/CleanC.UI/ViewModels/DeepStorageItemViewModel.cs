// src/CleanC.UI/ViewModels/DeepStorageItemViewModel.cs
// B2 (virtualization): آیتم لیست پویش عمیق (فایل زائد / پوشه خالی / فایل حجیم) در ListView مجازی.
// انتخاب به مدل اصلی write-through است تا منطق انتخاب همه و حذف گروهی بدون تغییر کار کند.
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using CleanC.Core.Services;

namespace CleanC.UI.ViewModels
{
    public class DeepStorageItemViewModel : INotifyPropertyChanged
    {
        private readonly Func<bool> _getSelected;
        private readonly Action<bool> _setSelected;

        public string Glyph { get; }
        public string FileName { get; }
        public string DirectoryPath { get; }
        public string EvidenceText { get; }
        public string FilePath { get; }

        public string PillText { get; }
        public Visibility PillVisibility { get; }

        public string SizeText { get; }
        public Visibility SizeVisibility { get; }

        public bool HasLock { get; }
        public Visibility LockVisibility => HasLock ? Visibility.Visible : Visibility.Collapsed;
        public Visibility EvidenceVisibility => string.IsNullOrEmpty(EvidenceText) ? Visibility.Collapsed : Visibility.Visible;

        public DeepStorageItemViewModel(
            string glyph,
            string fileName,
            string directoryPath,
            string evidenceText,
            string pillText,
            string sizeText,
            string filePath,
            bool hasLock,
            Func<bool> getSelected,
            Action<bool> setSelected)
        {
            Glyph = glyph;
            FileName = fileName;
            DirectoryPath = directoryPath;
            EvidenceText = evidenceText;
            PillText = pillText;
            SizeText = sizeText;
            FilePath = filePath;
            HasLock = hasLock;
            _getSelected = getSelected;
            _setSelected = setSelected;

            _isSelected = getSelected();
            RefreshLockState();
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                _setSelected(value);
                OnPropertyChanged();
            }
        }

        private bool _isLocked;

        /// <summary>وضعیت قفل مسیر از locks.json (همان منبع قبلی دکمه ی قفل).</summary>
        public bool IsLocked => _isLocked;

        public void RefreshLockState()
        {
            bool newVal = HasLock && LockRegistryService.IsUserLockedPath(FilePath);
            if (newVal == _isLocked) return;
            _isLocked = newVal;
            OnPropertyChanged(nameof(IsLocked));
            OnPropertyChanged(nameof(LockGlyph));
            OnPropertyChanged(nameof(LockBrush));
            OnPropertyChanged(nameof(LockTooltip));
        }

        public string LockGlyph => _isLocked ? "\uE785" : "\uE72E";

        public Brush LockBrush => _isLocked
            ? GetBrush("AccentFillColorDefaultBrush", Windows.UI.Color.FromArgb(255, 0, 120, 215))
            : GetBrush("TextFillColorTertiaryBrush", Windows.UI.Color.FromArgb(160, 128, 128, 128));

        public string LockTooltip => _isLocked
            ? LocalizationService.Get("LockTooltipUnlock", "رفع قفل — این مسیر دوباره قابل پیشنهاد می شود")
            : LocalizationService.Get("LockTooltipLock", "قفل کردن — این مسیر هرگز پیشنهاد یا حذف نمی شود");

        private static Brush GetBrush(string key, Windows.UI.Color fallback)
        {
            if (Application.Current?.Resources != null &&
                Application.Current.Resources.TryGetValue(key, out var val) &&
                val is Brush brush)
            {
                return brush;
            }
            return new SolidColorBrush(fallback);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
