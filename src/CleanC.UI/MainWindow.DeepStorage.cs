using System.Runtime.CompilerServices;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using System.Numerics;
using WinRT;
using CleanC.Core.Models;
using CleanC.Core.Services;
using CleanC.UI.ViewModels;

namespace CleanC.UI
{
    public sealed partial class MainWindow : Window
    {
        // ====================================================================
        // Tool 9: Deep Storage & Duplicate Cleaner
        // ====================================================================

        private async Task LoadDeepStoragePageAsync()
        {
            if (_deepScanResult == null)
            {
                await RunDeepStorageScanAsync();
            }
            else
            {
                RenderDeepStorageItems();
            }
        }

        private void DeepStorageScanBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(DeepStorageScanBtn_ClickAsync(sender, e));

        private async Task DeepStorageScanBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            await RunDeepStorageScanAsync();
        }

        private async Task RunDeepStorageScanAsync()
        {
            if (DeepStorageLoadingPanel != null) DeepStorageLoadingPanel.Visibility = Visibility.Visible;
            if (DeepStorageScanBtn != null) DeepStorageScanBtn.IsEnabled = false;
            DeepStorageItemsStack.Children.Clear();
            if (DeepStorageItemsList != null)
            {
                DeepStorageItemsList.ItemsSource = null;
                DeepStorageItemsList.Visibility = Visibility.Collapsed;
            }

            try
            {
                _deepScanResult = await DeepStorageCleanService.ScanDeepStorageAsync(p =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (DeepStorageScanStatusText != null) DeepStorageScanStatusText.Text = p;
                    });
                });

                if (DeepStorageDuplicatesCountText != null)
                    DeepStorageDuplicatesCountText.Text = string.Format(LocalizationService.Get("{0} گروه تکراری"), _deepScanResult.Duplicates.Count);
                if (DeepStorageDuplicatesSizeText != null)
                    DeepStorageDuplicatesSizeText.Text = string.Format(LocalizationService.Get("{0} فضا"), SystemStats.FormatBytes(_deepScanResult.TotalDuplicatesWastedBytes));

                if (DeepStorageEmptyFoldersCountText != null)
                    DeepStorageEmptyFoldersCountText.Text = string.Format(LocalizationService.Get("{0} پوشه خالی"), _deepScanResult.EmptyFolders.Count);

                if (DeepStorageMessCountText != null)
                    DeepStorageMessCountText.Text = string.Format(LocalizationService.Get("{0} فایل زائد و نامرتب"), _deepScanResult.MessItems.Count);
                if (DeepStorageMessSizeText != null)
                    DeepStorageMessSizeText.Text = string.Format(LocalizationService.Get("{0} فضا"), SystemStats.FormatBytes(_deepScanResult.TotalMessBytes));

                if (DeepStorageLargeFilesCountText != null)
                    DeepStorageLargeFilesCountText.Text = string.Format(LocalizationService.Get("{0} فایل حجیم"), _deepScanResult.LargeFiles.Count);
                if (DeepStorageLargeFilesSizeText != null)
                    DeepStorageLargeFilesSizeText.Text = string.Format(LocalizationService.Get("{0} فضا"), SystemStats.FormatBytes(_deepScanResult.TotalLargeFilesBytes));

                RenderDeepStorageItems();
            }
            catch (Exception ex)
            {
                ShowInfoBar(LocalizationService.Get("خطا در پویش عمیق"), ex.Message, InfoBarSeverity.Warning);
            }
            finally
            {
                if (DeepStorageLoadingPanel != null) DeepStorageLoadingPanel.Visibility = Visibility.Collapsed;
                if (DeepStorageScanBtn != null) DeepStorageScanBtn.IsEnabled = true;
            }
        }

        private string _deepStorageSortMode = "size";

        private void DeepStorageSort_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sort)
            {
                _deepStorageSortMode = sort;
                SetTabActive(DeepStorageSortSizeBtn, sort == "size");
                SetTabActive(DeepStorageSortDateBtn, sort == "date");
                SetTabActive(DeepStorageSortNameBtn, sort == "name");
                RenderDeepStorageItems();
            }
        }

        private void DeepStorageTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                _deepStorageActiveTab = tag;

                SetTabActive(DeepStorageTabDuplicates, tag == "duplicates");
                SetTabActive(DeepStorageTabEmptyFolders, tag == "empty_folders");
                SetTabActive(DeepStorageTabMess, tag == "mess");
                SetTabActive(DeepStorageTabLargeFiles, tag == "large_files");

                RenderDeepStorageItems();
            }
        }

        private void SetTabActive(Button? btn, bool isActive)
        {
            if (btn == null) return;
            if (isActive)
            {
                btn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
                btn.Background = null;
                btn.BorderBrush = null;
            }
            else
            {
                btn.Style = null;
                btn.Background = GetThemeBrush("AppCardSecondaryBrush");
                btn.BorderBrush = GetThemeBrush("AppCardBorderBrush");
            }
        }

        private void RenderDeepStorageItems()
        {
            DeepStorageItemsStack.Children.Clear();
            if (DeepStorageItemsList != null)
            {
                DeepStorageItemsList.ItemsSource = null;
                DeepStorageItemsList.Visibility = Visibility.Collapsed;
            }
            if (DeepStorageSelectAllCheck != null) DeepStorageSelectAllCheck.IsChecked = false;

            if (_deepScanResult == null) return;

            switch (_deepStorageActiveTab)
            {
                case "duplicates":
                    RenderDuplicateFiles();
                    break;
                case "empty_folders":
                    RenderEmptyFolders();
                    break;
                case "mess":
                    RenderStorageMess();
                    break;
                case "large_files":
                    RenderLargeFiles();
                    break;
            }

            UpdateDeepStorageSelectionCount();
        }

        private void RenderDuplicateFiles()
        {
            if (_deepScanResult == null || _deepScanResult.Duplicates.Count == 0)
            {
                DeepStorageItemsStack.Children.Add(CreateEmptyStateMessage(LocalizationService.Get("هیچ فایل تکراری با محتوا و سایز یکسان در سیستم یافت نشد. دیسک بهینه است!")));
                return;
            }

            var sortedGroups = _deepStorageSortMode switch
            {
                "date" => _deepScanResult.Duplicates.OrderByDescending(g => g.Files.Max(f => f.LastModified)).ToList(),
                "name" => _deepScanResult.Duplicates.OrderBy(g => g.Files.FirstOrDefault()?.FileName ?? "").ToList(),
                _ => _deepScanResult.Duplicates.OrderByDescending(g => g.WastedSizeBytes).ToList()
            };

            foreach (var group in sortedGroups)
            {
                var groupCard = new Border
                {
                    Background = GetThemeBrush("AppCardBackgroundBrush"),
                    BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 12, 14, 12),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var groupStack = new StackPanel { Spacing = 8 };

                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var iconBorder = new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(8),
                    Background = GetThemeBrush("AppAccentTintBrush"),
                    BorderBrush = GetThemeBrush("AppAccentBorderBrush"),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 10, 0),
                    Child = new FontIcon { Glyph = "\uE8C8", FontSize = 13, Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] }
                };
                Grid.SetColumn(iconBorder, 0);
                headerGrid.Children.Add(iconBorder);

                var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
                titleStack.Children.Add(new TextBlock { Text = string.Format(LocalizationService.Get("{0} نسخه یکسان · {1}"), group.Files.Count, group.Files.FirstOrDefault()?.FileName), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12.5 });
                titleStack.Children.Add(new TextBlock { Text = string.Format(LocalizationService.Get("حجم هر فایل: {0} · هش محتوا: {1}"), group.FormattedSize, group.HashPreview), FontSize = 10.5, Foreground = GetThemeBrush("TextFillColorTertiaryBrush") });
                Grid.SetColumn(titleStack, 1);
                headerGrid.Children.Add(titleStack);

                var wastedBadge = new Border
                {
                    Background = GetThemeBrush("AppCardSecondaryBrush"),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8, 3, 8, 3),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = string.Format(LocalizationService.Get("+ {0} قابل آزادسازی"), group.FormattedWastedSize), FontSize = 10.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] }
                };
                Grid.SetColumn(wastedBadge, 2);
                headerGrid.Children.Add(wastedBadge);

                groupStack.Children.Add(headerGrid);
                groupStack.Children.Add(new Border { Height = 1, Background = GetThemeBrush("AppCardBorderBrush"), Margin = new Thickness(0, 2, 0, 2) });

                for (int i = 0; i < group.Files.Count; i++)
                {
                    var file = group.Files[i];
                    var fileRow = new Grid { Margin = new Thickness(4, 2, 4, 2) };
                    fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var chk = new CheckBox
                    {
                        IsChecked = file.IsSelectedForDeletion,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    var capturedFile = file;
                    chk.Click += (s, e) =>
                    {
                        capturedFile.IsSelectedForDeletion = chk.IsChecked == true;
                        UpdateDeepStorageSelectionCount();
                    };
                    Grid.SetColumn(chk, 0);
                    fileRow.Children.Add(chk);

                    var pathStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
                    var pathText = new TextBlock
                    {
                        Text = file.FilePath,
                        FontSize = 11,
                        Foreground = file.IsOriginal ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] : GetThemeBrush("TextFillColorSecondaryBrush"),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        FlowDirection = FlowDirection.LeftToRight
                    };
                    pathStack.Children.Add(pathText);
                    Grid.SetColumn(pathStack, 1);
                    fileRow.Children.Add(pathStack);

                    var tag = new Border
                    {
                        Background = file.IsOriginal ? GetThemeBrush("AppAccentTintBrush") : GetThemeBrush("AppCardSecondaryBrush"),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = file.IsOriginal ? LocalizationService.Get("نسخه اصلی (نگه داری)") : LocalizationService.Get("نسخه کپی (اضافی)"),
                            FontSize = 10,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = file.IsOriginal ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] : GetThemeBrush("TextFillColorTertiaryBrush")
                        }
                    };
                    Grid.SetColumn(tag, 2);
                    fileRow.Children.Add(tag);

                    groupStack.Children.Add(fileRow);
                }

                groupCard.Child = groupStack;
                DeepStorageItemsStack.Children.Add(groupCard);
            }
        }

        private void RenderEmptyFolders()
        {
            if (_deepScanResult == null || _deepScanResult.EmptyFolders.Count == 0)
            {
                DeepStorageItemsStack.Children.Add(CreateEmptyStateMessage(LocalizationService.Get("هیچ پوشه خالی بلااستفاده ای در دایرکتوری های انتخابی یافت نشد.")));
                return;
            }

            var sortedFolders = _deepStorageSortMode switch
            {
                "name" => _deepScanResult.EmptyFolders.OrderBy(f => f.FolderName).ToList(),
                _ => _deepScanResult.EmptyFolders.OrderBy(f => f.FolderPath).ToList()
            };

            // B2: ViewModel — ListView مجازی فقط کارت های داخل viewport را می سازد
            var vms = new List<DeepStorageItemViewModel>(sortedFolders.Count);
            foreach (var folder in sortedFolders)
            {
                var capturedFolder = folder;

                // D09b: پوشه های خالی ریشه ی درایو نیاز به ادمین دارند — مثل RenderStorageMess هشدار داده می شود
                string evidenceText = string.IsNullOrEmpty(folder.Evidence)
                    ? string.Empty
                    : folder.RequiresAdmin
                        ? $"{LocalizationService.Get("AdminRequired", "نیازمند دسترسی ادمین")} · {folder.Evidence}"
                        : folder.Evidence;

                vms.Add(new DeepStorageItemViewModel(
                    glyph: "\uE8B7",
                    fileName: folder.FolderName,
                    directoryPath: folder.FolderPath,
                    evidenceText: evidenceText,
                    pillText: LocalizationService.Get("پوشه خالی"),
                    sizeText: string.Empty,
                    filePath: folder.FolderPath,
                    hasLock: true,
                    getSelected: () => capturedFolder.IsSelected,
                    setSelected: v => capturedFolder.IsSelected = v));
            }
            SetDeepStorageItemsSource(vms);
        }

        private void RenderStorageMess()
        {
            if (_deepScanResult == null || _deepScanResult.MessItems.Count == 0)
            {
                DeepStorageItemsStack.Children.Add(CreateEmptyStateMessage(LocalizationService.Get("هیچ فایل زائد، موقت یا نصاب رها شده ای یافت نشد. دیسک کاملاً پاکیزه است.")));
                RenderSensitiveFilesSection();
                return;
            }

            var sortedMess = _deepStorageSortMode switch
            {
                "date" => _deepScanResult.MessItems.OrderByDescending(m => m.LastModified).ToList(),
                "name" => _deepScanResult.MessItems.OrderBy(m => m.FileName).ToList(),
                _ => _deepScanResult.MessItems.OrderByDescending(m => m.SizeBytes).ToList()
            };

            // B2: ViewModel — لیست مجازی؛ شواهد و ستون های pill/size/lock داخل تمپلیت مشترک
            var vms = new List<DeepStorageItemViewModel>(sortedMess.Count);
            foreach (var item in sortedMess)
            {
                var capturedItem = item;
                string evidenceText = string.IsNullOrEmpty(item.Evidence)
                    ? string.Empty
                    : item.RequiresAdmin
                        ? $"{LocalizationService.Get("AdminRequired", "نیازمند دسترسی ادمین")} · {item.Evidence}"
                        : item.Evidence;

                vms.Add(new DeepStorageItemViewModel(
                    glyph: "\uE896",
                    fileName: item.FileName,
                    directoryPath: item.DirectoryPath,
                    evidenceText: evidenceText,
                    pillText: item.CategoryTitle,
                    sizeText: item.FormattedSize,
                    filePath: item.FilePath,
                    hasLock: true,
                    getSelected: () => capturedItem.IsSelected,
                    setSelected: v => capturedItem.IsSelected = v));
            }
            SetDeepStorageItemsSource(vms);

            RenderSensitiveFilesSection();
        }

        // B2: اتصال لیست مجازی پویش عمیق
        private void SetDeepStorageItemsSource(List<DeepStorageItemViewModel> vms)
        {
            DeepStorageItemsList.ItemsSource = vms;
            DeepStorageItemsList.Visibility = Visibility.Visible;
        }

        // B2: کلیک چک باکس — مقدار از طریق TwoWay binding در VM و مدل اصلی نوشته شده؛ فقط شمارش تازه می شود
        private void DeepStorageItemCheck_Click(object sender, RoutedEventArgs e)
        {
            UpdateDeepStorageSelectionCount();
        }

        /// <summary>
        /// B2: کلیک دکمه ی قفل داخل تمپلیت — همان رفتار قبلی CreateLockToggleButton:
        /// افزودن/حذف قفل در locks.json + برداشتن انتخاب + به روزرسانی آیکن (بدون رندر کل لیست).
        /// </summary>
        private void DeepStorageItemLock_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not DeepStorageItemViewModel vm) return;

            bool currentlyLocked = LockRegistryService.IsUserLockedPath(vm.FilePath);
            bool ok = currentlyLocked
                ? LockRegistryService.RemoveLock(vm.FilePath)
                : LockRegistryService.AddLock(vm.FilePath, LockTypes.Folder, LocalizationService.Get("LockReasonDeepScan", "قفل شده از صفحه ی پویش عمیق"));

            if (ok)
            {
                vm.IsSelected = false; // رفتار قبلی: بعد از قفل/رفع قفل، مورد از انتخاب خارج می شود
                vm.RefreshLockState();
                UpdateDeepStorageSelectionCount();
            }
            else
            {
                ShowInfoBar(LocalizationService.Get("LockInfoBarTitle", "قفل مسیر"),
                    LocalizationService.Get("LockSaveFailed", "ذخیره ی قفل ناموفق بود — دسترسی به locks.json بررسی شود."), InfoBarSeverity.Warning);
            }
        }

        /// <summary>
        /// D17 — بخش «فایل های حساس» (فقط گزارش): بدون چک باکس و بدون هیچ دکمه ی حذف.
        /// این موارد عمداً در MessItems نیستند تا از چرخه ی حذف خارج بمانند.
        /// </summary>
        private void RenderSensitiveFilesSection()
        {
            if (_deepScanResult == null || _deepScanResult.SensitiveFiles.Count == 0) return;

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 14, 0, 6)
            };
            header.Children.Add(new FontIcon
            {
                Glyph = "\uE83D",
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            });
            header.Children.Add(new TextBlock
            {
                Text = string.Format(LocalizationService.Get("SensitiveSectionTitle", "فایل های حساس شناسایی شده ({0} مورد — فقط گزارش، بدون حذف)"), _deepScanResult.SensitiveFiles.Count),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            DeepStorageItemsStack.Children.Add(header);

            foreach (var sensitive in _deepScanResult.SensitiveFiles)
            {
                var card = new Border
                {
                    Background = GetThemeBrush("AppCardBackgroundBrush"),
                    BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 4)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = new FontIcon
                {
                    Glyph = "\uE8D7",
                    FontSize = 14,
                    Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(icon, 0);
                grid.Children.Add(icon);

                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
                info.Children.Add(new TextBlock { Text = sensitive.FileName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                info.Children.Add(new TextBlock { Text = sensitive.DirectoryPath, FontSize = 10.5, Foreground = GetThemeBrush("TextFillColorTertiaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = FlowDirection.LeftToRight });
                Grid.SetColumn(info, 1);
                grid.Children.Add(info);

                var reasonPill = new Border
                {
                    Background = GetThemeBrush("AppCardSecondaryBrush"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    Child = new TextBlock { Text = sensitive.MatchReason, FontSize = 10, Foreground = GetThemeBrush("TextFillColorSecondaryBrush") }
                };
                Grid.SetColumn(reasonPill, 2);
                grid.Children.Add(reasonPill);

                var sizeBadge = new Border
                {
                    Background = GetThemeBrush("AppCardSecondaryBrush"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(7, 2, 7, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = sensitive.FormattedSize, FontSize = 10.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FlowDirection = FlowDirection.LeftToRight }
                };
                Grid.SetColumn(sizeBadge, 3);
                grid.Children.Add(sizeBadge);

                card.Child = grid;
                DeepStorageItemsStack.Children.Add(card);
            }
        }

        private void RenderLargeFiles()
        {
            if (_deepScanResult == null || _deepScanResult.LargeFiles.Count == 0)
            {
                DeepStorageItemsStack.Children.Add(CreateEmptyStateMessage(LocalizationService.Get("فایل حجیم تر از ۵۰۰ مگابایت در پوشه های شخصی کاربر یافت نشد.")));
                return;
            }

            var files = _deepScanResult.LargeFiles.AsEnumerable();
            switch (_deepStorageSortMode)
            {
                case "date":
                    files = files.OrderByDescending(f => f.LastModified);
                    break;
                case "name":
                    files = files.OrderBy(f => f.FileName);
                    break;
                case "size":
                default:
                    files = files.OrderByDescending(f => f.SizeBytes);
                    break;
            }

            // B2: ViewModel — قفل مسیر برای فایل های حجیم ارائه نمی شود (hasLock: false)
            var vms = new List<DeepStorageItemViewModel>(_deepScanResult.LargeFiles.Count);
            foreach (var item in files)
            {
                var capturedItem = item;
                vms.Add(new DeepStorageItemViewModel(
                    glyph: "\uE7C3",
                    fileName: item.FileName,
                    directoryPath: item.DirectoryPath,
                    evidenceText: string.Empty,
                    pillText: string.Empty,
                    sizeText: item.FormattedSize,
                    filePath: item.FilePath,
                    hasLock: false,
                    getSelected: () => capturedItem.IsSelected,
                    setSelected: v => capturedItem.IsSelected = v));
            }
            SetDeepStorageItemsSource(vms);
        }

        /// <summary>
        /// کارت حالت خالی — برای همه ی لیست های ابزارها استفاده می شود (جستجوی بی نتیجه،
        /// لیست خالی، پاکسازی بدون مورد و ...). glyph پیش فرض نماد «همه چیز مرتب است».
        /// </summary>
        private Border CreateEmptyStateMessage(string message, string glyph = "\uE73E")
        {
            return new Border
            {
                Background = GetThemeBrush("AppCardBackgroundBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = new StackPanel
                {
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new FontIcon { Glyph = glyph, FontSize = 22, Foreground = GetThemeBrush("SystemFillColorSuccessBrush"), HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = message, FontSize = 12.5, Foreground = GetThemeBrush("TextFillColorSecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center }
                    }
                }
            };
        }

        private void DeepStorageSelectAllCheck_Click(object sender, RoutedEventArgs e)
        {
            bool isChecked = DeepStorageSelectAllCheck?.IsChecked == true;
            if (_deepScanResult == null) return;

            switch (_deepStorageActiveTab)
            {
                case "duplicates":
                    foreach (var group in _deepScanResult.Duplicates)
                    {
                        foreach (var f in group.Files)
                        {
                            if (!f.IsOriginal) f.IsSelectedForDeletion = isChecked;
                        }
                    }
                    break;
                case "empty_folders":
                    foreach (var f in _deepScanResult.EmptyFolders) f.IsSelected = isChecked;
                    break;
                case "mess":
                    foreach (var m in _deepScanResult.MessItems) m.IsSelected = isChecked;
                    break;
                case "large_files":
                    foreach (var l in _deepScanResult.LargeFiles) l.IsSelected = isChecked;
                    break;
            }

            RenderDeepStorageItems();
        }

        private void UpdateDeepStorageSelectionCount()
        {
            if (_deepScanResult == null) return;

            // C6: جمع‌بندی در Core تست‌پذیر شد (DeepStorageSelection)
            var summary = DeepStorageSelection.Summarize(_deepScanResult, _deepStorageActiveTab);
            int selectedCount = summary.Count;
            long selectedBytes = summary.Bytes;

            if (DeepStorageSelectedCountText != null)
            {
                string sizeInfo = selectedBytes > 0 ? $" ({SystemStats.FormatBytes(selectedBytes)})" : "";
                DeepStorageSelectedCountText.Text = $"{selectedCount} {LocalizationService.Get("ItemsSelected", "مورد انتخاب شده")}{sizeInfo}";
            }
        }

        /// <summary>
        /// تأیید حذف گروهی پیش از انتقال انتخاب ها به سطل بازیافت.
        /// تعداد و حجم موارد انتخاب شده را نشان می دهد؛ اگر چیزی انتخاب نشده باشد
        /// همان پیام قبلی InfoBar را نمایش می دهد و بدون باز کردن دیالوگ برمی گردد.
        /// </summary>
        private async Task<bool> ConfirmDeepStorageBatchDeleteAsync()
        {
            // C6: تعداد/حجم از منبع حقیقت مشترک؛ فقط عنوان مخصوص هر تب اینجا می ماند
            var summary = DeepStorageSelection.Summarize(_deepScanResult, _deepStorageActiveTab);
            int count = summary.Count;
            long bytes = summary.Bytes;

            string title = _deepStorageActiveTab switch
            {
                "duplicates" => LocalizationService.Get("پاکسازی فایل های تکراری"),
                "empty_folders" => LocalizationService.Get("پاکسازی پوشه های خالی"),
                "mess" => LocalizationService.Get("سازماندهی دانلودها و دسکتاپ"),
                "large_files" => LocalizationService.Get("پاکسازی فایل های حجیم"),
                _ => ""
            };

            if (count == 0)
            {
                if (title.Length > 0)
                    ShowInfoBar(title, LocalizationService.Get("هیچ موردی انتخاب نشده است."), InfoBarSeverity.Informational);
                return false;
            }

            string sizeInfo = bytes > 0 ? string.Format(LocalizationService.Get(" مجموع حجم: {0}،"), SystemStats.FormatBytes(bytes)) : "";
            // دیالوگ دقیقاً همان رفتاری را وعده می دهد که واقعاً رخ می دهد
            string modeNote = SafeDeleteService.DescribeMode(SafeDeleteService.DefaultMode);
            var dialog = new ContentDialog
            {
                Title = LocalizationService.Get("تأیید حذف گروهی"),
                Content = new TextBlock
                {
                    Text = string.Format(LocalizationService.Get("{0} مورد انتخاب شده{1} {2}. ادامه می دهید؟"), count, sizeInfo, modeNote),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20
                },
                PrimaryButtonText = LocalizationService.Get("بله، حذف شود"),
                CloseButtonText = LocalizationService.Get("انصراف"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot,
                FlowDirection = FlowDirection.RightToLeft
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private void DeepStorageBatchDeleteBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(DeepStorageBatchDeleteBtn_ClickAsync(sender, e));

        private async Task DeepStorageBatchDeleteBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (_deepScanResult == null) return;
            if (!await ConfirmDeepStorageBatchDeleteAsync()) return;

            long freedBytes = 0;
            int deletedCount = 0;
            SafeDeleteBatchResult? batch = null;

            try
            {
                if (DeepStorageBatchDeleteBtn != null) DeepStorageBatchDeleteBtn.IsEnabled = false;
                if (DeepStorageBatchDeleteSpinner != null)
                {
                    DeepStorageBatchDeleteSpinner.Visibility = Visibility.Visible;
                    DeepStorageBatchDeleteSpinner.IsActive = true;
                }
                if (DeepStorageBatchDeleteIcon != null) DeepStorageBatchDeleteIcon.Visibility = Visibility.Collapsed;
                if (DeepStorageBatchDeleteBtnText != null) DeepStorageBatchDeleteBtnText.Text = LocalizationService.Get("در حال حذف ایمن...");

                switch (_deepStorageActiveTab)
                {
                    case "duplicates":
                        var dupFiles = _deepScanResult.Duplicates
                            .SelectMany(g => g.Files)
                            .Where(f => f.IsSelectedForDeletion)
                            .Select(f => f.FilePath)
                            .ToList();
                        batch = await DeepStorageCleanService.DeleteDuplicateFilesAsync(dupFiles);
                        break;

                    case "empty_folders":
                        var folders = _deepScanResult.EmptyFolders
                            .Where(f => f.IsSelected)
                            .Select(f => f.FolderPath)
                            .ToList();
                        batch = await DeepStorageCleanService.DeleteEmptyFoldersAsync(folders);
                        break;

                    case "mess":
                        var messFiles = _deepScanResult.MessItems
                            .Where(m => m.IsSelected)
                            .Select(m => m.FilePath)
                            .ToList();
                        batch = await DeepStorageCleanService.DeleteMessItemsAsync(messFiles);
                        break;

                    case "large_files":
                        var largeFiles = _deepScanResult.LargeFiles
                            .Where(l => l.IsSelected)
                            .Select(l => l.FilePath)
                            .ToList();
                        batch = await DeepStorageCleanService.DeleteLargeFilesAsync(largeFiles);
                        break;
                }

                if (batch == null) return;

                deletedCount = batch.SucceededCount;
                freedBytes = batch.FreedBytes;

                if (deletedCount > 0)
                {
                    // فقط فضایی که واقعاً آزاد شده در آمار می نشیند؛ اقلامی که فقط
                    // به سطل رفته اند جابه جا شده اند نه آزاد، پس آمار را خراب نمی کنیم.
                    if (freedBytes > 0)
                    {
                        _cumulativeFreedBytes += freedBytes;
                        _lastCleanFreedBytes = freedBytes;
                        _lastCleanDateTime = DateTime.Now;
                        RecordCleanupSession(freedBytes);
                        SaveCumulativeFreedStats();
                        UpdateCumulativeChipsUI();
                        RenderCleanupHistory();
                    }

                    string detail = SafeDeleteService.DescribeBatch(batch);
                    bool canUndo = batch.RecycledCount + batch.QuarantinedCount > 0;
                    if (canUndo) detail += LocalizationService.Get(" — از «مرکز بازیابی» قابل بازگردانی است.");

                    var severity = batch.FailedCount > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
                    ShowInfoBar(LocalizationService.Get("پاکسازی عمیق دیسک"), detail + ".", severity);

                    // فیدبک بصری موفقیت با رنگ سبز زمردی
                    if (DeepStorageBatchDeleteSpinner != null)
                    {
                        DeepStorageBatchDeleteSpinner.Visibility = Visibility.Collapsed;
                        DeepStorageBatchDeleteSpinner.IsActive = false;
                    }
                    if (DeepStorageBatchDeleteIcon != null)
                    {
                        DeepStorageBatchDeleteIcon.Glyph = "\uE73E";
                        DeepStorageBatchDeleteIcon.Visibility = Visibility.Visible;
                    }
                    if (DeepStorageBatchDeleteBtnText != null)
                    {
                        DeepStorageBatchDeleteBtnText.Text = string.Format(LocalizationService.Get("✓ {0} مورد با موفقیت پاکسازی شد"), deletedCount);
                    }
                    if (DeepStorageBatchDeleteBtn != null)
                    {
                        DeepStorageBatchDeleteBtn.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 16, 185, 129));
                        DeepStorageBatchDeleteBtn.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
                    }

                    await Task.Delay(2500);

                    await RunDeepStorageScanAsync();
                }
                else if (batch.FailedCount > 0)
                {
                    ShowInfoBar(LocalizationService.Get("پاکسازی عمیق دیسک"), SafeDeleteService.DescribeBatch(batch) + ".", InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                ShowInfoBar(LocalizationService.Get("خطا در پاکسازی"), ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                if (DeepStorageBatchDeleteBtn != null)
                {
                    DeepStorageBatchDeleteBtn.ClearValue(Button.BackgroundProperty);
                    DeepStorageBatchDeleteBtn.ClearValue(Button.ForegroundProperty);
                    DeepStorageBatchDeleteBtn.IsEnabled = true;
                }
                if (DeepStorageBatchDeleteSpinner != null)
                {
                    DeepStorageBatchDeleteSpinner.Visibility = Visibility.Collapsed;
                    DeepStorageBatchDeleteSpinner.IsActive = false;
                }
                if (DeepStorageBatchDeleteIcon != null)
                {
                    DeepStorageBatchDeleteIcon.Glyph = "\uE74D";
                    DeepStorageBatchDeleteIcon.Visibility = Visibility.Visible;
                }
                if (DeepStorageBatchDeleteBtnText != null)
                {
                    DeepStorageBatchDeleteBtnText.Text = LocalizationService.Get("حذف ایمن موارد انتخاب شده");
                }
            }
        }
    }
}
