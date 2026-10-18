// src/CleanC.UI/ViewModels/AppListItemViewModel.cs
// B3 (virtualization): ViewModel آیتم لیست «حذف نرم افزار» — جایگزین ساخت کارت های UI در کد.
// هر آیتم فقط زمانی که وارد viewport شود render و آیکونش decode می شود (ListView مجازی).
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CleanC.Core.Models;
using CleanC.Core.Services;

namespace CleanC.UI.ViewModels
{
    public class AppListItemViewModel : INotifyPropertyChanged
    {
        public InstalledAppInfo App { get; }

        private readonly Brush _normalCardBrush;
        private readonly Brush _selectedCardBrush;
        private bool _isSelected;

        public AppListItemViewModel(InstalledAppInfo app, string? iconPath, Brush normalCardBrush, Brush selectedCardBrush)
        {
            App = app;
            _normalCardBrush = normalCardBrush;
            _selectedCardBrush = selectedCardBrush;
            _isSelected = app.IsSelected;

            // آیکون واقعی اپ — decode تعویقی به عهده BitmapImage است (فقط آیتم های دیده شده)
            if (!string.IsNullOrEmpty(iconPath) && System.IO.File.Exists(iconPath))
            {
                try { IconSource = new BitmapImage(new Uri(iconPath)); }
                catch { IconSource = null; }
            }

            string pubStr = !string.IsNullOrEmpty(app.Publisher) ? $"{app.Publisher} · " : "";
            string dateStr = !string.IsNullOrEmpty(app.InstalledDateFormatted) ? $"نصب: {app.InstalledDateFormatted}" : "";
            SubLine = $"{pubStr}{dateStr}".TrimEnd(' ', '·');
        }

        public string DisplayName => App.DisplayName;
        public string SubLine { get; }
        public string TrialLabel => LocalizationService.Get("BadgeTrial", "نسخه آزمایشی");
        public string TrialEvidence => App.TrialEvidence;

        public ImageSource? IconSource { get; }
        public Visibility IconVisibility => IconSource != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GlyphVisibility => IconSource == null ? Visibility.Visible : Visibility.Collapsed;

        public Visibility TrialVisibility => App.IsTrialBadge ? Visibility.Visible : Visibility.Collapsed;
        public Visibility EvidenceVisibility => App.IsTrialBadge && !string.IsNullOrEmpty(App.TrialEvidence) ? Visibility.Visible : Visibility.Collapsed;

        public string SizeText => App.FormattedSize;
        public Visibility SizeVisibility => App.EstimatedSizeBytes > 0 && !string.IsNullOrEmpty(App.FormattedSize) ? Visibility.Visible : Visibility.Collapsed;

        // انتخاب از UI بلافاصله در مدل اصلی هم نوشته می شود تا منطق حذف گروهی همان را بخواند
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                App.IsSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CardBorderBrush));
            }
        }

        public Brush CardBorderBrush => _isSelected ? _selectedCardBrush : _normalCardBrush;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
