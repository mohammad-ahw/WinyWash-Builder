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
                    DeepStorageDuplicatesCountText.Text = $"{_deepScanResult.Duplicates.Count} گروه تکراری";
                if (DeepStorageDuplicatesSizeText != null)
                    DeepStorageDuplicatesSizeText.Text = $"{SystemStats.FormatBytes(_deepScanResult.TotalDuplicatesWastedBytes)} فضا";

                if (DeepStorageEmptyFoldersCountText != null)
                    DeepStorageEmptyFoldersCountText.Text = $"{_deepScanResult.EmptyFolders.Count} پوشه خالی";

                if (DeepStorageMessCountText != null)
                    DeepStorageMessCountText.Text = $"{_deepScanResult.MessItems.Count} فایل زائد و نامرتب";
                if (DeepStorageMessSizeText != null)
                    DeepStorageMessSizeText.Text = $"{SystemStats.FormatBytes(_deepScanResult.TotalMessBytes)} فضا";

                if (DeepStorageLargeFilesCountText != null)
                    DeepStorageLargeFilesCountText.Text = $"{_deepScanResult.LargeFiles.Count} فایل حجیم";
                if (DeepStorageLargeFilesSizeText != null)
                    DeepStorageLargeFilesSizeText.Text = $"{SystemStats.FormatBytes(_deepScanResult.TotalLargeFilesBytes)} فضا";

                RenderDeepStorageItems();
            }
            catch (Exception ex)
            {
                ShowInfoBar("خطا در پویش عمیق", ex.Message, InfoBarSeverity.Warning);
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
                DeepStorageItemsStack.Children.Add(CreateEmptyStateMessage("هیچ فایل تکراری با محتوا و سایز یکسان در سیستم یافت نشد. دیسک بهینه است!"));
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
                titleStack.Children.Add(new TextBlock { Text = $"{group.Files.Count} نسخه یکسان · {group.Files.FirstOrDefault()?.FileName}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12.5 });
                titleStack.Children.Add(new TextBlock { Text = $"حجم هر فایل: {group.FormattedSize} · هش محتوا: {group.HashPreview}", FontSize = 10.5, Foreground = GetThemeBrush("TextFillColorTertiaryBrush") });
                Grid.SetColumn(titleStack, 1);
                headerGrid.Children.Add(titleStack);

                var wastedBadge = new Border
                {
                    Background = GetThemeBrush("AppCardSecondaryBrush"),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8, 3, 8, 3),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = $"+ {group.FormattedWastedSize} قابل آزادسازی", FontSize = 10.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] }
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
                            Text = file.IsOriginal ? "نسخه اصلی (نگه داری)" : "نسخه کپی (اضافی)",
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
                DeepStorageItemsStack.Children.Add(CreateEmptyStateMessage("هیچ پوشه خالی بلااستفاده ای در دایرکتوری های انتخابی یافت نشد."));
                return;
            }

            var sortedFolders = _deepStorageSortMode switch
            {
                "name" => _deepScanResult.EmptyFolders.OrderBy(f => f.FolderName).ToList(),
                _ => _deepScanResult.EmptyFolders.OrderBy(f => f.FolderPath).ToList()
            };

            foreach (var folder in sortedFolders)
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
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var chk = new CheckBox
                {
                    IsChecked = folder.IsSelected,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var capturedFolder = folder;
                chk.Click += (s, e) =>
                {
                    capturedFolder.IsSelected = chk.IsChecked == true;
                    UpdateDeepStorageSelectionCount();
                };
                Grid.SetColumn(chk, 0);
                grid.Children.Add(chk);

                var icon = new FontIcon
                {
                    Glyph = "\uE8B7",
                    FontSize = 14,
                    Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(icon, 1);
                grid.Children.Add(icon);

                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
                info.Children.Add(new TextBlock { Text = folder.FolderName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 });
                info.Children.Add(new TextBlock { Text = folder.FolderPath, FontSize = 10.5, Foreground = GetThemeBrush("TextFillColorTertiaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = FlowDirection.LeftToRight });
                Grid.SetColumn(info, 2);
                grid.Children.Add(info);

                var pill = new Border
                {
                    Background = GetThemeBrush("AppCardSecondaryBrush"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(7, 2, 7, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "پوشه خالی", FontSize = 10, Foreground = GetThemeBrush("TextFillColorSecondaryBrush") }
                };
                Grid.SetColumn(pill, 3);
                grid.Children.Add(pill);

                card.Child = grid;
                DeepStorageItemsStack.Children.Add(card);
            }
        }

        private void RenderStorageMess()
        {
            if (_deepScanResult == null || _deepScanResult.MessItems.Count == 0)
            {
                DeepStorageItemsStack.Children.Add(CreateEmptyStateMessage("هیچ فایل زائد، موقت یا نصاب رها شده ای یافت نشد. دیسک کاملاً پاکیزه است."));
                return;
            }

            var sortedMess = _deepStorageSortMode switch
            {
                "date" => _deepScanResult.MessItems.OrderByDescending(m => m.LastModified).ToList(),
                "name" => _deepScanResult.MessItems.OrderBy(m => m.FileName).ToList(),
                _ => _deepScanResult.MessItems.OrderByDescending(m => m.SizeBytes).ToList()
            };

            foreach (var item in sortedMess)
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
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var chk = new CheckBox
                {
                    IsChecked = item.IsSelected,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var capturedItem = item;
                chk.Click += (s, e) =>
                {
                    capturedItem.IsSelected = chk.IsChecked == true;
                    UpdateDeepStorageSelectionCount();
                };
                Grid.SetColumn(chk, 0);
                grid.Children.Add(chk);

                var icon = new FontIcon
                {
                    Glyph = "\uE896",
                    FontSize = 14,
                    Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(icon, 1);
                grid.Children.Add(icon);

                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
                info.Children.Add(new TextBlock { Text = item.FileName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                info.Children.Add(new TextBlock { Text = item.DirectoryPath, FontSize = 10.5, Foreground = GetThemeBrush("TextFillColorTertiaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = FlowDirection.LeftToRight });
                Grid.SetColumn(info, 2);
                grid.Children.Add(info);

                var catPill = new Border
                {
                    Background = GetThemeBrush("AppAccentTintBrush"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    Child = new TextBlock { Text = item.CategoryTitle, FontSize = 10, Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] }
                };
                Grid.SetColumn(catPill, 3);
                grid.Children.Add(catPill);

                var sizeBadge = new Border
                {
                    Background = GetThemeBrush("AppCardSecondaryBrush"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(7, 2, 7, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = item.FormattedSize, FontSize = 10.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FlowDirection = FlowDirection.LeftToRight }
                };
                Grid.SetColumn(sizeBadge, 4);
                grid.Children.Add(sizeBadge);

                card.Child = grid;
                DeepStorageItemsStack.Children.Add(card);
            }
        }

        private void RenderLargeFiles()
        {
            if (_deepScanResult == null || _deepScanResult.LargeFiles.Count == 0)
            {
                DeepStorageItemsStack.Children.Add(CreateEmptyStateMessage("فایل حجیم تر از ۵۰۰ مگابایت در پوشه های شخصی کاربر یافت نشد."));
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

            foreach (var item in files)
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
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var chk = new CheckBox
                {
                    IsChecked = item.IsSelected,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var capturedItem = item;
                chk.Click += (s, e) =>
                {
                    capturedItem.IsSelected = chk.IsChecked == true;
                    UpdateDeepStorageSelectionCount();
                };
                Grid.SetColumn(chk, 0);
                grid.Children.Add(chk);

                var icon = new FontIcon
                {
                    Glyph = "\uE7C3",
                    FontSize = 14,
                    Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(icon, 1);
                grid.Children.Add(icon);

                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
                info.Children.Add(new TextBlock { Text = item.FileName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                info.Children.Add(new TextBlock { Text = item.DirectoryPath, FontSize = 10.5, Foreground = GetThemeBrush("TextFillColorTertiaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = FlowDirection.LeftToRight });
                Grid.SetColumn(info, 2);
                grid.Children.Add(info);

                var sizeBadge = new Border
                {
                    Background = GetThemeBrush("AppCardSecondaryBrush"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 2, 8, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = item.FormattedSize, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"], FlowDirection = FlowDirection.LeftToRight }
                };
                Grid.SetColumn(sizeBadge, 3);
                grid.Children.Add(sizeBadge);

                card.Child = grid;
                DeepStorageItemsStack.Children.Add(card);
            }
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

            int selectedCount = 0;
            long selectedBytes = 0;

            switch (_deepStorageActiveTab)
            {
                case "duplicates":
                    selectedCount = _deepScanResult.Duplicates.SelectMany(g => g.Files).Count(f => f.IsSelectedForDeletion);
                    selectedBytes = _deepScanResult.Duplicates.SelectMany(g => g.Files).Where(f => f.IsSelectedForDeletion).Sum(f => f.SizeBytes);
                    break;
                case "empty_folders":
                    selectedCount = _deepScanResult.EmptyFolders.Count(f => f.IsSelected);
                    break;
                case "mess":
                    selectedCount = _deepScanResult.MessItems.Count(m => m.IsSelected);
                    selectedBytes = _deepScanResult.MessItems.Where(m => m.IsSelected).Sum(m => m.SizeBytes);
                    break;
                case "large_files":
                    selectedCount = _deepScanResult.LargeFiles.Count(l => l.IsSelected);
                    selectedBytes = _deepScanResult.LargeFiles.Where(l => l.IsSelected).Sum(l => l.SizeBytes);
                    break;
            }

            if (DeepStorageSelectedCountText != null)
            {
                string sizeInfo = selectedBytes > 0 ? $" ({SystemStats.FormatBytes(selectedBytes)})" : "";
                DeepStorageSelectedCountText.Text = $"{selectedCount} {LocalizationService.Get("ItemsSelected", "مورد انتخاب شده")}{sizeInfo}";
            }
        }

        /// <summary>
        /// D5 — تأیید حذف گروهی پیش از انتقال انتخاب ها به سطل بازیافت.
        /// تعداد و حجم موارد انتخاب شده را نشان می دهد؛ اگر چیزی انتخاب نشده باشد
        /// همان پیام قبلی InfoBar را نمایش می دهد و بدون باز کردن دیالوگ برمی گردد.
        /// </summary>
        private async Task<bool> ConfirmDeepStorageBatchDeleteAsync()
        {
            int count = 0;
            long bytes = 0;
            string title = "";

            switch (_deepStorageActiveTab)
            {
                case "duplicates":
                    count = _deepScanResult!.Duplicates.SelectMany(g => g.Files).Count(f => f.IsSelectedForDeletion);
                    bytes = _deepScanResult.Duplicates.SelectMany(g => g.Files).Where(f => f.IsSelectedForDeletion).Sum(f => f.SizeBytes);
                    title = "پاکسازی فایل های تکراری";
                    break;
                case "empty_folders":
                    count = _deepScanResult!.EmptyFolders.Count(f => f.IsSelected);
                    title = "پاکسازی پوشه های خالی";
                    break;
                case "mess":
                    count = _deepScanResult!.MessItems.Count(m => m.IsSelected);
                    bytes = _deepScanResult.MessItems.Where(m => m.IsSelected).Sum(m => m.SizeBytes);
                    title = "سازماندهی دانلودها و دسکتاپ";
                    break;
                case "large_files":
                    count = _deepScanResult!.LargeFiles.Count(l => l.IsSelected);
                    bytes = _deepScanResult.LargeFiles.Where(l => l.IsSelected).Sum(l => l.SizeBytes);
                    title = "پاکسازی فایل های حجیم";
                    break;
            }

            if (count == 0)
            {
                if (title.Length > 0)
                    ShowInfoBar(title, "هیچ موردی انتخاب نشده است.", InfoBarSeverity.Informational);
                return false;
            }

            string sizeInfo = bytes > 0 ? $" مجموع حجم: {SystemStats.FormatBytes(bytes)}،" : "";
            // F3 — دیالوگ دقیقاً همان رفتاری را وعده می دهد که واقعاً رخ می دهد
            string modeNote = SafeDeleteService.DescribeMode(SafeDeleteService.DefaultMode);
            var dialog = new ContentDialog
            {
                Title = "تأیید حذف گروهی",
                Content = new TextBlock
                {
                    Text = $"{count} مورد انتخاب شده{sizeInfo} {modeNote}. ادامه می دهید؟",
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20
                },
                PrimaryButtonText = "بله، حذف شود",
                CloseButtonText = "انصراف",
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
                if (DeepStorageBatchDeleteBtnText != null) DeepStorageBatchDeleteBtnText.Text = "در حال حذف ایمن...";

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
                    if (canUndo) detail += " — از «مرکز بازیابی» قابل بازگردانی است.";

                    var severity = batch.FailedCount > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
                    ShowInfoBar("پاکسازی عمیق دیسک", detail + ".", severity);

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
                        DeepStorageBatchDeleteBtnText.Text = $"✓ {deletedCount} مورد با موفقیت پاکسازی شد";
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
                    ShowInfoBar("پاکسازی عمیق دیسک", SafeDeleteService.DescribeBatch(batch) + ".", InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                ShowInfoBar("خطا در پاکسازی", ex.Message, InfoBarSeverity.Error);
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
                    DeepStorageBatchDeleteBtnText.Text = "حذف ایمن موارد انتخاب شده";
                }
            }
        }
    }
}
