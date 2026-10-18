// src/CleanC.UI/MainWindow.RecycleBin.cs
// پنل مدیریت سطل بازیافت: پیش نمایش آیتم به آیتم + بازگردانی/حذف قطعی.
// منطق واقعی فایل سیستم در CleanC.Core/Services/RecycleBinService.cs است؛
// این فایل فقط لایه ی نمایش و کنترل رویدادهاست.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;
using CleanC.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CleanC.UI
{
    public sealed partial class MainWindow : Window
    {
        private readonly List<(CheckBox Box, RecycleBinItem Item)> _recycleBinRows = new();
        private bool _recycleBinLoading;

        // ── بارگذاری ──────────────────────────────────────────────────────

        private async Task LoadRecycleBinPageAsync()
        {
            if (_recycleBinLoading) return;
            _recycleBinLoading = true;

            try
            {
                RecycleBinItemsStack.Children.Clear();
                _recycleBinRows.Clear();
                RecycleBinEmptyHint.Visibility = Visibility.Collapsed;
                RecycleBinLoadingPanel.Visibility = Visibility.Visible;
                RecycleBinLoadingText.Text = "در حال خواندن فهرست سطل بازیافت...";
                RecycleBinCountText.Text = "…";
                RecycleBinSizeText.Text = "…";
                RecycleBinSelectAllCheck.IsChecked = false;
                UpdateRecycleBinSelectedCount();

                // پویش درایوها و (در صورت نیاز) محاسبه ی حجم پوشه ها سنگین است
                List<RecycleBinItem> items = await Task.Run(RecycleBinService.ListItems);

                RecycleBinLoadingPanel.Visibility = Visibility.Collapsed;

                foreach (RecycleBinItem item in items)
                {
                    var box = new CheckBox
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 12, 0)
                    };
                    AutomationProperties.SetName(box, $"انتخاب {item.FileName}");
                    box.Checked += (_, _) => UpdateRecycleBinSelectedCount();
                    box.Unchecked += (_, _) => UpdateRecycleBinSelectedCount();

                    RecycleBinItemsStack.Children.Add(BuildRecycleBinRow(box, item));
                    _recycleBinRows.Add((box, item));
                }

                RecycleBinCountText.Text = $"{items.Count} شیء";
                RecycleBinSizeText.Text = SystemStats.FormatBytes(items.Sum(i => i.SizeBytes));
                RecycleBinEmptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "LoadRecycleBinPageAsync");
                RecycleBinLoadingPanel.Visibility = Visibility.Collapsed;
                ShowInfoBar("سطل بازیافت", "فهرست سطل بازیافت قابل خواندن نیست.", InfoBarSeverity.Error);
            }
            finally
            {
                _recycleBinLoading = false;
            }
        }

        // ── ساخت سطر هر شیء ───────────────────────────────────────────────

        private Border BuildRecycleBinRow(CheckBox box, RecycleBinItem item)
        {
            var root = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Background = GetThemeBrush("AppCardBackgroundBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var grid = new Grid { ColumnSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(box, 0);
            grid.Children.Add(box);

            var info = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };

            // نام شیء
            info.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(item.FileName) ? item.OriginalPath : item.FileName,
                FontSize = 12.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = GetThemeBrush("TextFillColorPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            // مسیر اصلی (همیشه چپ به راست تا مسیرهای انگلیسی خراب دیده نشوند)
            info.Children.Add(new TextBlock
            {
                Text = item.OriginalPath,
                FontSize = 11,
                Foreground = GetThemeBrush("TextFillColorTertiaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FlowDirection = FlowDirection.LeftToRight,
                TextAlignment = TextAlignment.Left
            });

            // ردیف اطلاعات تکمیلی: نوع + زمان حذف + حجم
            var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            meta.Children.Add(MetaChip(item.IsDirectory ? "پوشه" : "فایل", "\uE8B7"));
            meta.Children.Add(MetaChip(
                item.DeletedAt == DateTime.MinValue ? "زمان حذف نامشخص" : item.DeletedAt.ToString("yyyy.MM.dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                "\uE81C"));
            meta.Children.Add(MetaChip(item.FormattedSize, "\uE964"));

            if (!item.PayloadExists)
                meta.Children.Add(MetaChip("داده موجود نیست", "\uE711"));

            info.Children.Add(meta);

            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            // اکشن های هر سطر
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };

            var restoreBtn = new Button
            {
                Tag = item,
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                CornerRadius = new CornerRadius(6),
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                FontSize = 11,
                Content = "بازگردانی",
                IsEnabled = item.PayloadExists
            };
            AutomationProperties.SetName(restoreBtn, $"بازگردانی {item.FileName}");
            restoreBtn.Click += RecycleBinItemRestoreBtn_Click;

            var deleteBtn = new Button
            {
                Tag = item,
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                CornerRadius = new CornerRadius(6),
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68)),
                Content = "حذف قطعی"
            };
            AutomationProperties.SetName(deleteBtn, $"حذف قطعی {item.FileName}");
            deleteBtn.Click += RecycleBinItemDeleteBtn_Click;

            actions.Children.Add(restoreBtn);
            actions.Children.Add(deleteBtn);

            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            root.Child = grid;
            return root;
        }

        private static Border MetaChip(string text, string glyph)
        {
            var chip = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(18, 120, 120, 130)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1)
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new FontIcon { Glyph = glyph, FontSize = 9.5 });
            sp.Children.Add(new TextBlock { Text = text, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            chip.Child = sp;
            return chip;
        }

        // ── انتخاب گروهی ──────────────────────────────────────────────────

        private void UpdateRecycleBinSelectedCount()
        {
            int selected = _recycleBinRows.Count(r => r.Box.IsChecked == true);
            RecycleBinSelectedCountText.Text = $"{selected} {LocalizationService.Get("ItemsSelected", "مورد انتخاب شده")}";

            bool any = _recycleBinRows.Count > 0;
            RecycleBinRestoreSelectedBtn.IsEnabled = selected > 0;
            RecycleBinDeleteSelectedBtn.IsEnabled = selected > 0;
            RecycleBinEmptyBtn.IsEnabled = any;
            RecycleBinSelectAllCheck.IsEnabled = any;
        }

        private List<RecycleBinItem> GetSelectedRecycleBinItems()
            => _recycleBinRows.Where(r => r.Box.IsChecked == true).Select(r => r.Item).ToList();

        private void RecycleBinSelectAllCheck_Click(object sender, RoutedEventArgs e)
        {
            bool on = RecycleBinSelectAllCheck.IsChecked == true;
            foreach (var row in _recycleBinRows) row.Box.IsChecked = on;
            UpdateRecycleBinSelectedCount();
        }

        // ── اکشن های گروهی ────────────────────────────────────────────────
        // handlerها به الگوی SafeInvoker برده شده اند؛ استثناهای خارج از catch
        // دیگر خفه نمی شوند و می توانند کل برنامه را از پا دربیاورند.

        private void RecycleBinRestoreSelectedBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(RecycleBinRestoreSelectedBtn_Async(sender, e));

        private async Task RecycleBinRestoreSelectedBtn_Async(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedRecycleBinItems();
            if (selected.Count == 0)
            {
                ShowInfoBar("سطل بازیافت", "ابتدا دست کم یک شیء را انتخاب کنید.", InfoBarSeverity.Informational);
                return;
            }

            var errors = new List<string>();
            int restored = 0;

            await Task.Run(() =>
            {
                foreach (var item in selected)
                {
                    if (RecycleBinService.Restore(item, out string error)) restored++;
                    else if (!string.IsNullOrEmpty(error)) errors.Add($"{item.FileName}: {error}");
                }
            });

            if (errors.Count == 0)
                ShowInfoBar("سطل بازیافت", $"{restored} شیء به مسیر اصلی بازگردانده شد.", InfoBarSeverity.Success);
            else
                ShowInfoBar("سطل بازیافت", $"{restored} شیء بازگردانده شد؛ {errors.Count} مورد ناموفق ({errors[0]})", InfoBarSeverity.Warning);

            await LoadRecycleBinPageAsync();
        }

        private void RecycleBinDeleteSelectedBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(RecycleBinDeleteSelectedBtn_Async(sender, e));

        private async Task RecycleBinDeleteSelectedBtn_Async(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedRecycleBinItems();
            if (selected.Count == 0)
            {
                ShowInfoBar("سطل بازیافت", "ابتدا دست کم یک شیء را انتخاب کنید.", InfoBarSeverity.Informational);
                return;
            }

            long total = selected.Sum(i => i.SizeBytes);
            bool confirmed = await ConfirmRecycleBinAsync(
                "حذف قطعی اشیاء انتخاب شده",
                $"{selected.Count} شیء با حجم {SystemStats.FormatBytes(total)} برای همیشه حذف می شود.\nاین عملیات قابل بازگشت نیست.",
                "حذف قطعی");

            if (!confirmed) return;

            var errors = new List<string>();
            int deleted = 0;

            await Task.Run(() =>
            {
                foreach (var item in selected)
                {
                    if (RecycleBinService.Delete(item, out string error)) deleted++;
                    else if (!string.IsNullOrEmpty(error)) errors.Add($"{item.FileName}: {error}");
                }
            });

            if (errors.Count == 0)
                ShowInfoBar("سطل بازیافت", $"{deleted} شیء برای همیشه حذف شد.", InfoBarSeverity.Success);
            else
                ShowInfoBar("سطل بازیافت", $"{deleted} شیء حذف شد؛ {errors.Count} مورد ناموفق ({errors[0]})", InfoBarSeverity.Warning);

            await LoadRecycleBinPageAsync();
        }

        private void RecycleBinEmptyBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(RecycleBinEmptyBtn_Async(sender, e));

        private async Task RecycleBinEmptyBtn_Async(object sender, RoutedEventArgs e)
        {
            var items = _recycleBinRows.Select(r => r.Item).ToList();
            if (items.Count == 0) return;

            long total = items.Sum(i => i.SizeBytes);
            bool confirmed = await ConfirmRecycleBinAsync(
                "خالی کردن کل سطل بازیافت",
                $"{items.Count} شیء با حجم {SystemStats.FormatBytes(total)} برای همیشه حذف می شود.\nپس از خالی کردن، هیچ شیء قابل بازگردانی نخواهد بود.",
                "خالی کردن");

            if (!confirmed) return;

            int removed = 0;
            string error = string.Empty;
            await Task.Run(() => removed = RecycleBinService.EmptyAll(out error));

            if (removed > 0)
                ShowInfoBar("سطل بازیافت", $"سطل خالی شد؛ {removed} شیء حذف قطعی گردید.", InfoBarSeverity.Success);
            else if (!string.IsNullOrEmpty(error))
                ShowInfoBar("سطل بازیافت", $"خالی کردن سطل کامل نشد: {error}", InfoBarSeverity.Error);
            else
                ShowInfoBar("سطل بازیافت", "چیزی برای حذف وجود نداشت.", InfoBarSeverity.Informational);

            await LoadRecycleBinPageAsync();
        }

        // ── اکشن های تک شیئی ──────────────────────────────────────────────

        private void RecycleBinItemRestoreBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(RecycleBinItemRestoreBtn_Async(sender, e));

        private async Task RecycleBinItemRestoreBtn_Async(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not RecycleBinItem item) return;
            btn.IsEnabled = false;

            try
            {
                string error = string.Empty;
                bool ok = await Task.Run(() => RecycleBinService.Restore(item, out error));

                if (ok)
                {
                    ShowInfoBar("سطل بازیافت", $"«{item.FileName}» به مسیر اصلی بازگردانده شد.", InfoBarSeverity.Success);
                    await LoadRecycleBinPageAsync();
                }
                else
                {
                    ShowInfoBar("سطل بازیافت", string.IsNullOrEmpty(error) ? "بازگردانی انجام نشد." : error, InfoBarSeverity.Error);
                    btn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "RecycleBinItemRestoreBtn_Click");
                ShowInfoBar("سطل بازیافت", "بازگردانی شیء با خطای غیرمنتظره مواجه شد.", InfoBarSeverity.Error);
                btn.IsEnabled = true;
            }
        }

        private void RecycleBinItemDeleteBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(RecycleBinItemDeleteBtn_Async(sender, e));

        private async Task RecycleBinItemDeleteBtn_Async(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not RecycleBinItem item) return;

            bool confirmed = await ConfirmRecycleBinAsync(
                "حذف قطعی شیء",
                $"«{item.FileName}» ({item.FormattedSize}) برای همیشه حذف می شود.\nاین عملیات قابل بازگشت نیست.",
                "حذف قطعی");

            if (!confirmed) return;

            btn.IsEnabled = false;
            try
            {
                string error = string.Empty;
                bool ok = await Task.Run(() => RecycleBinService.Delete(item, out error));

                if (ok)
                {
                    ShowInfoBar("سطل بازیافت", $"«{item.FileName}» برای همیشه حذف شد.", InfoBarSeverity.Success);
                    await LoadRecycleBinPageAsync();
                }
                else
                {
                    ShowInfoBar("سطل بازیافت", string.IsNullOrEmpty(error) ? "حذف انجام نشد." : error, InfoBarSeverity.Error);
                    btn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "RecycleBinItemDeleteBtn_Click");
                ShowInfoBar("سطل بازیافت", "حذف شیء با خطای غیرمنتظره مواجه شد.", InfoBarSeverity.Error);
                btn.IsEnabled = true;
            }
        }

        private void RecycleBinRefreshBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(LoadRecycleBinPageAsync());

        // ── دیالوگ تأیید ─────────────────────────────────────────────────

        private async Task<bool> ConfirmRecycleBinAsync(string title, string message, string primaryText)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    PrimaryButtonText = primaryText,
                    CloseButtonText = "انصراف",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot
                };

                return await dialog.ShowAsync() == ContentDialogResult.Primary;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "ConfirmRecycleBinAsync");
                return false;
            }
        }
    }
}
