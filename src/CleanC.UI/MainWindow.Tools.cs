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
using CleanC.Core.Diagnostics;
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
        // Tool 1: System Specs & Battery Health
        // ====================================================================

        private async Task LoadSystemSpecsPageAsync(bool forceRefresh = false)
        {
            if (_cachedSpecs != null && !forceRefresh)
            {
                RenderSystemSpecs(_cachedSpecs);
                return;
            }

            SpecsLoadingSpinner.Visibility = Visibility.Visible;
            SpecsCardsContainer.Visibility = Visibility.Collapsed;

            try
            {
                _cachedSpecs = await SystemSpecsService.GetDetailedSpecsAsync();
                RenderSystemSpecs(_cachedSpecs);
            }
            catch (Exception ex)
            {
                SpecsLoadingSpinner.Visibility = Visibility.Collapsed;
                SpecsCardsContainer.Visibility = Visibility.Visible;
                SpecsCardsContainer.Children.Clear();
                SpecsCardsContainer.Children.Add(new TextBlock
                {
                    Text = $"خطا در خواندن مشخصات سیستم: {ex.Message}",
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red)
                });
            }
        }

        private void RenderSystemSpecs(DetailedSystemSpecs specs)
        {
            SpecsLoadingSpinner.Visibility = Visibility.Collapsed;
            SpecsCardsContainer.Visibility = Visibility.Visible;
            SpecsCardsContainer.Children.Clear();

            var cards = new List<FrameworkElement>();

            // 1. CPU Card
            var cpuChips = new List<string> { $"{specs.CpuCores} هسته", $"{specs.CpuThreads} رشته", $"{specs.CpuClockMhz} MHz" };
            if (specs.CpuL3CacheKb > 0) cpuChips.Add($"L3: {specs.CpuL3CacheKb / 1024} MB");
            cards.Add(CreateSpecCard("\uE774", "پردازنده", specs.CpuName, cpuChips));

            // 2. RAM Card
            var ramChips = new List<string> { specs.TotalRamFormatted, specs.RamType, $"{specs.RamSpeedMhz} MHz", $"{specs.RamSlotsUsed} اسلات" };
            if (!string.IsNullOrEmpty(specs.RamManufacturer)) ramChips.Add(specs.RamManufacturer);
            cards.Add(CreateSpecCard("\uE950", "حافظه رم", $"{specs.TotalRamFormatted} {specs.RamType}", ramChips));

            // 3. GPU Cards
            if (specs.Gpus.Count > 0)
            {
                foreach (var gpu in specs.Gpus)
                {
                    var gpuChips = new List<string> { gpu.VramFormatted };
                    if (!string.IsNullOrEmpty(gpu.DriverVersion)) gpuChips.Add($"درایور: {gpu.DriverVersion}");
                    cards.Add(CreateSpecCard("\uE7F4", "گرافیک", gpu.Name, gpuChips));
                }
            }
            else if (specs.GpuNames.Count > 0)
            {
                foreach (var gName in specs.GpuNames)
                {
                    cards.Add(CreateSpecCard("\uE7F4", "گرافیک", gName, new List<string> { "رندرینگ گرافیکی" }));
                }
            }

            // 4. Motherboard & BIOS Card
            var mbChips = new List<string>();
            if (!string.IsNullOrEmpty(specs.BiosVersion)) mbChips.Add($"BIOS {specs.BiosVersion}");
            if (!string.IsNullOrEmpty(specs.BiosReleaseDate)) mbChips.Add(specs.BiosReleaseDate);
            cards.Add(CreateSpecCard("\uE770", "مادربرد", $"{specs.MotherboardManufacturer} {specs.MotherboardModel}", mbChips));

            // 5. Windows OS Card
            var osChips = new List<string> { specs.OsArchitecture, $"Build {specs.OsBuild}" };
            if (!string.IsNullOrEmpty(specs.OsUptimeFormatted)) osChips.Add($"روشن: {specs.OsUptimeFormatted}");
            cards.Add(CreateSpecCard("\uE8A9", "سیستم عامل", $"{specs.OsName}", osChips));

            // 6. Windows Activation Status Card
            if (specs.Activation != null)
            {
                string actGlyph = specs.Activation.IsActivated ? "\uE73E" : "\uE783";
                var actChips = new List<string> { specs.Activation.StatusText };
                if (!string.IsNullOrEmpty(specs.Activation.Description)) actChips.Add(specs.Activation.Description);
                cards.Add(CreateSpecCard(actGlyph, "لایسنس ویندوز", specs.Activation.StatusText, actChips));
            }

            // 7. Battery Card (Laptop only)
            if (specs.HasBattery)
            {
                var batChips = new List<string>
                {
                    $"{specs.BatteryHealthPercent}% سلامت",
                    $"{specs.BatteryFullChargeCapacityMWh} mWh",
                    $"سیکل: {specs.BatteryCycleCount}"
                };
                cards.Add(CreateSpecCard("\uEBB5", "باتری و برق", $"{specs.BatteryHealthPercent}% سلامت باتری", batChips));
            }

            // Layout in 2-Column Responsive Grid
            var grid = new Grid
            {
                ColumnSpacing = 10,
                RowSpacing = 10,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int row = 0;
            for (int i = 0; i < cards.Count; i += 2)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var leftCard = cards[i];
                Grid.SetRow(leftCard, row);
                Grid.SetColumn(leftCard, 0);
                grid.Children.Add(leftCard);

                if (i + 1 < cards.Count)
                {
                    var rightCard = cards[i + 1];
                    Grid.SetRow(rightCard, row);
                    Grid.SetColumn(rightCard, 1);
                    grid.Children.Add(rightCard);
                }
                else
                {
                    Grid.SetColumnSpan(leftCard, 2);
                }
                row++;
            }

            SpecsCardsContainer.Children.Add(grid);

            // 8. Logical Drives Management (This PC Drives - Full Width)
            if (specs.LogicalDrives != null && specs.LogicalDrives.Count > 0)
            {
                SpecsCardsContainer.Children.Add(CreateLogicalDrivesSection(specs.LogicalDrives));
            }

            // 9. Windows Power Plan Management
            SpecsCardsContainer.Children.Add(CreatePowerPlanManagementCard());
        }

        private void CopySpecsReport_Click(object sender, RoutedEventArgs e)
        {
            if (_cachedSpecs == null) return;
            string text = SystemSpecsService.GenerateReportText(_cachedSpecs);
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            ShowInfoBar("کپی گزارش مشخصات", "مشخصات جامع سیستم با فرمت استاندارد در کلیپ بورد کپی شد.", InfoBarSeverity.Success);
        }

        private void RefreshSpecsPage_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(RefreshSpecsPage_ClickAsync(sender, e));

        private async Task RefreshSpecsPage_ClickAsync(object sender, RoutedEventArgs e)
        {
            await LoadSystemSpecsPageAsync(forceRefresh: true);
            ShowInfoBar("بروزرسانی مشخصات", "مشخصات سخت افزار و سنسورهای سیستم بروزرسانی شدند.", InfoBarSeverity.Informational);
        }

        private Border CreateSpecCard(string glyph, string category, string value, List<string>? chips, Windows.UI.Color? iconBgColor = null, string extra = "")
        {
            var border = new Border
            {
                Background = GetThemeBrush("AppCardBackgroundBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12)
            };

            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconBg = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(8),
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            
            var icon = new FontIcon
            {
                Glyph = glyph,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = GetThemeBrush("AccentFillColorDefaultBrush")
            };
            iconBg.Child = icon;
            
            Grid.SetColumn(iconBg, 0);
            grid.Children.Add(iconBg);

            var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var catText = new TextBlock { Text = category, FontSize = 11, Foreground = GetThemeBrush("TextFillColorTertiaryBrush") };
            var valText = new TextBlock
            {
                Text = value,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13.5,
                Foreground = GetThemeBrush("TextFillColorPrimaryBrush"),
                FlowDirection = FlowDirection.LeftToRight,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            textStack.Children.Add(catText);
            textStack.Children.Add(valText);

            if (chips != null && chips.Count > 0)
            {
                string combinedChips = string.Join(" · ", chips.Where(c => !string.IsNullOrWhiteSpace(c)));
                if (!string.IsNullOrEmpty(combinedChips))
                {
                    var chipsText = new TextBlock
                    {
                        Text = combinedChips,
                        FontSize = 10.5,
                        Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
                        FlowDirection = FlowDirection.LeftToRight,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    textStack.Children.Add(chipsText);
                }
            }

            if (!string.IsNullOrEmpty(extra))
            {
                var extraText = new TextBlock { Text = extra, FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Foreground = GetThemeBrush("TextFillColorSecondaryBrush"), Margin = new Thickness(0, 2, 0, 0) };
                textStack.Children.Add(extraText);
            }

            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            border.Child = grid;
            return border;
        }

        private Border CreateLogicalDrivesSection(List<LogicalDriveItem> drives)
        {
            var container = new Border
            {
                Background = GetThemeBrush("AppCardBackgroundBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20, 16, 20, 18),
                Translation = new System.Numerics.Vector3(0, 0, 16)
            };

            var mainStack = new StackPanel { Spacing = 14 };

            // Section Header
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var titleStack = new StackPanel { Spacing = 2 };
            titleStack.Children.Add(new TextBlock
            {
                Text = "مدیریت درایوهای سیستم عامل (This PC Drives)",
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                FontSize = 14
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "مشاهده ظرفیت، تغییر نام برچسب درایوها و تغییر حرف درایو (Drive Letter) با ایمنی کامل",
                FontSize = 11,
                Foreground = GetThemeBrush("TextFillColorSecondaryBrush")
            });
            Grid.SetColumn(titleStack, 0);
            headerGrid.Children.Add(titleStack);

            mainStack.Children.Add(headerGrid);

            // Drives List
            var drivesList = new StackPanel { Spacing = 10 };
            foreach (var drive in drives)
            {
                var driveCard = new Border
                {
                    Background = GetThemeBrush("AppCardSecondaryBrush"),
                    BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16, 12, 16, 12)
                };

                var dGrid = new Grid();
                dGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Icon
                dGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Info & Bar
                dGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Actions

                // Drive Icon
                var iconBorder = new Border
                {
                    Width = 34,
                    Height = 34,
                    CornerRadius = new CornerRadius(8),
                    Background = GetThemeBrush("AppCardSecondaryBrush"),
                    BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconBorder.Child = new FontIcon
                {
                    Glyph = drive.IsSystemDrive ? "\uEDA2" : "\uE7FC",
                    FontSize = 15,
                    Foreground = GetThemeBrush("AccentFillColorDefaultBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(iconBorder, 0);
                dGrid.Children.Add(iconBorder);

                // Middle Info & Progress
                var infoStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                nameRow.Children.Add(new TextBlock
                {
                    Text = $"{drive.Letter} {drive.VolumeLabel}",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 12.5
                });
                nameRow.Children.Add(new TextBlock
                {
                    Text = $"({drive.FileSystem})",
                    FontSize = 11,
                    Foreground = GetThemeBrush("TextFillColorTertiaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                infoStack.Children.Add(nameRow);

                // Progress Bar
                var pBar = new ProgressBar
                {
                    Value = drive.UsedPercent,
                    Maximum = 100,
                    Height = 5,
                    CornerRadius = new CornerRadius(2.5),
                    Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                };
                infoStack.Children.Add(pBar);

                // Space detail
                var spaceDetail = new TextBlock
                {
                    Text = $"{drive.FormattedFree} free of {drive.FormattedTotal} ({drive.UsedPercent.ToString("0.0", CultureInfo.InvariantCulture)}%)",
                    FontSize = 10.5,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
                    FlowDirection = FlowDirection.LeftToRight,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                infoStack.Children.Add(spaceDetail);

                Grid.SetColumn(infoStack, 1);
                dGrid.Children.Add(infoStack);

                // Actions (Rename, Change Letter)
                var actionsStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

                // Rename Button
                var renameBtn = new Button
                {
                    Content = "تغییر نام",
                    Height = 30,
                    FontSize = 11,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 0, 10, 0)
                };
                renameBtn.Click += async (s, e) =>
                {
                    await PromptRenameDriveAsync(drive);
                };
                actionsStack.Children.Add(renameBtn);

                // Change Letter Button
                var changeLetterBtn = new Button
                {
                    Content = "تغییر حرف",
                    Height = 30,
                    FontSize = 11,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 0, 10, 0)
                };
                if (drive.IsSystemDrive)
                {
                    changeLetterBtn.IsEnabled = false;
                    ToolTipService.SetToolTip(changeLetterBtn, "امکان تغییر حرف درایو ویندوز (C:) به دلیل حفظ پایداری سیستم وجود ندارد.");
                }
                else
                {
                    changeLetterBtn.Click += async (s, e) =>
                    {
                        await PromptChangeDriveLetterAsync(drive);
                    };
                }
                actionsStack.Children.Add(changeLetterBtn);

                Grid.SetColumn(actionsStack, 2);
                dGrid.Children.Add(actionsStack);

                driveCard.Child = dGrid;
                drivesList.Children.Add(driveCard);
            }

            mainStack.Children.Add(drivesList);
            container.Child = mainStack;
            return container;
        }

        private UIElement CreatePowerPlanManagementCard()
        {
            var card = new Border
            {
                Background = GetThemeBrush("AppCardBackgroundBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 10, 0, 0)
            };

            var mainStack = new StackPanel { Spacing = 10 };

            // Header Row
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { Spacing = 2 };
            bool isRtl = LocalizationService.IsRtl;
            titleStack.Children.Add(new TextBlock
            {
                Text = isRtl ? "طرح مصرف انرژی ویندوز (Power Plan)" : "Windows Power Plan",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13.5
            });

            var status = PowerPlanService.GetPowerStatus();
            var subText = new TextBlock
            {
                Text = isRtl 
                    ? $"اعمال آنی powercfg بدون نیاز به ریستارت | پلن فعلی: {status.ActiveSchemeName}"
                    : $"Instant powercfg tuning without restart | Current Plan: {status.ActiveSchemeName}",
                FontSize = 11,
                Foreground = GetThemeBrush("TextFillColorSecondaryBrush")
            };
            titleStack.Children.Add(subText);
            Grid.SetColumn(titleStack, 0);
            headerGrid.Children.Add(titleStack);

            var activeBadge = new Border
            {
                Background = GetThemeBrush("AppAccentTintBrush"),
                BorderBrush = GetThemeBrush("AppAccentBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            var badgeText = new TextBlock
            {
                Text = $"⚡ {status.ActiveSchemeName}",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            };
            activeBadge.Child = badgeText;
            Grid.SetColumn(activeBadge, 1);
            headerGrid.Children.Add(activeBadge);

            mainStack.Children.Add(headerGrid);

            // 4 Plans Horizontal Segmented Bar (1 Row, 4 Columns)
            var plansGrid = new Grid { ColumnSpacing = 8 };
            for (int col = 0; col < 4; col++)
            {
                plansGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            var plans = isRtl
                ? new[]
                {
                    ("ultimate", "حداکثر کارایی", "Ultimate", "\uE7E8"),
                    ("high", "کارایی بالا", "High", "\uE945"),
                    ("balanced", "متعادل", "Balanced", "\uE953"),
                    ("saver", "ذخیره انرژی", "Power Saver", "\uEC0A")
                }
                : new[]
                {
                    ("ultimate", "Ultimate", "Max Speed", "\uE7E8"),
                    ("high", "High Perf", "Optimal", "\uE945"),
                    ("balanced", "Balanced", "Recommended", "\uE953"),
                    ("saver", "Power Saver", "Eco Mode", "\uEC0A")
                };

            var planButtons = new List<(string key, Button btn, TextBlock titleBlock, FontIcon iconBlock, Border activeIndicator)>();

            Action updatePlanSelectionVisuals = () =>
            {
                var cur = PowerPlanService.GetPowerStatus();
                string curKey = cur.ActiveSchemeKey?.ToLowerInvariant() ?? "";
                foreach (var item in planButtons)
                {
                    bool isSelected = curKey.Contains(item.key) || item.key.Contains(curKey);
                    item.btn.Background = isSelected ? GetThemeBrush("AppAccentTintBrush") : GetThemeBrush("AppCardSecondaryBrush");
                    item.btn.BorderBrush = isSelected ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] : GetThemeBrush("AppCardBorderBrush");
                    item.activeIndicator.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
                }
            };

            for (int i = 0; i < plans.Length; i++)
            {
                var (key, title, subtitle, icon) = plans[i];
                var planBtn = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(8, 8, 8, 8),
                    CornerRadius = new CornerRadius(8),
                    Background = GetThemeBrush("AppCardSecondaryBrush"),
                    BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                    BorderThickness = new Thickness(1)
                };

                var itemStack = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };

                var pIcon = new FontIcon
                {
                    Glyph = icon,
                    FontSize = 15,
                    Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                itemStack.Children.Add(pIcon);

                var pTitle = new TextBlock
                {
                    Text = title,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 11.5,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                itemStack.Children.Add(pTitle);

                var pSub = new TextBlock
                {
                    Text = subtitle,
                    FontSize = 9.5,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                itemStack.Children.Add(pSub);

                var dot = new Border
                {
                    Width = 6,
                    Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0),
                    Visibility = Visibility.Collapsed
                };
                itemStack.Children.Add(dot);

                planBtn.Content = itemStack;

                string captureKey = key;
                string captureTitle = title;
                planBtn.Click += async (s, e) =>
                {
                    planBtn.IsEnabled = false;
                    bool ok = await Task.Run(() => PowerPlanService.SetActivePlan(captureKey));
                    planBtn.IsEnabled = true;
                    if (ok)
                    {
                        var newStatus = PowerPlanService.GetPowerStatus();
                        badgeText.Text = $"⚡ {newStatus.ActiveSchemeName}";
                        subText.Text = $"اعمال آنی powercfg بدون نیاز به ریستارت | پلن فعلی: {newStatus.ActiveSchemeName}";
                        updatePlanSelectionVisuals();
                        ShowInfoBar("تغییر پلن انرژی", $"پلن انرژی ویندوز با موفقیت به «{captureTitle}» تغییر یافت.", InfoBarSeverity.Success);
                    }
                    else
                    {
                        ShowInfoBar("خطا در تغییر پلن", "امکان تغییر پلن انرژی وجود ندارد (نیازمند دسترسی Administrator).", InfoBarSeverity.Error);
                    }
                };

                Grid.SetColumn(planBtn, i);
                plansGrid.Children.Add(planBtn);
                planButtons.Add((key, planBtn, pTitle, pIcon, dot));
            }

            updatePlanSelectionVisuals();
            mainStack.Children.Add(plansGrid);
            card.Child = mainStack;
            return card;
        }

        private async Task PromptRenameDriveAsync(LogicalDriveItem drive)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.Content.XamlRoot,
                Title = $"تغییر نام درایو {drive.Letter}",
                PrimaryButtonText = "ذخیره نام جدید",
                CloseButtonText = "انصراف",
                DefaultButton = ContentDialogButton.Primary
            };

            var inputStack = new StackPanel { Spacing = 10, Margin = new Thickness(0, 12, 0, 0) };
            inputStack.Children.Add(new TextBlock
            {
                Text = "نام یا برچسب (Volume Label) جدید را وارد نمایید:",
                FontSize = 12
            });

            var textBox = new TextBox
            {
                Text = drive.VolumeLabel == "درایو محلی" ? "" : drive.VolumeLabel,
                PlaceholderText = "مثال: Data, Games, Works",
                MaxLength = 32
            };
            inputStack.Children.Add(textBox);
            dialog.Content = inputStack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                string newLabel = textBox.Text.Trim();
                bool ok = SystemSpecsService.SetDriveLabel(drive.Letter, newLabel);
                if (ok)
                {
                    ShowInfoBar("تغییر نام درایو", $"نام درایو {drive.Letter} با موفقیت به '{newLabel}' تغییر یافت.", InfoBarSeverity.Success);
                    await LoadSystemSpecsPageAsync(forceRefresh: true);
                }
                else
                {
                    ShowInfoBar("خطا", $"عدم موفقیت در تغییر نام درایو {drive.Letter}.", InfoBarSeverity.Error);
                }
            }
        }

        private async Task PromptChangeDriveLetterAsync(LogicalDriveItem drive)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.Content.XamlRoot,
                Title = $"تغییر حرف درایو {drive.Letter}",
                PrimaryButtonText = "تغییر و اعمال",
                CloseButtonText = "انصراف",
                DefaultButton = ContentDialogButton.Primary
            };

            var stack = new StackPanel { Spacing = 12, Margin = new Thickness(0, 12, 0, 0) };
            stack.Children.Add(new TextBlock
            {
                Text = $"توجه: تغییر حرف درایو {drive.Letter} ممکن است دسترسی میانبرهایی که به این درایو اشاره دارند را موقتاً تحت تاثیر قرار دهد.",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 245, 158, 11)),
                TextWrapping = TextWrapping.Wrap
            });

            stack.Children.Add(new TextBlock
            {
                Text = "حرف جدید مورد نظر را انتخاب کنید:",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            var usedLetters = DriveInfo.GetDrives().Select(d => d.Name.Substring(0, 1).ToUpperInvariant()).ToHashSet();
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            for (char c = 'D'; c <= 'Z'; c++)
            {
                string letterStr = c.ToString();
                if (!usedLetters.Contains(letterStr) || letterStr == drive.Letter.Substring(0, 1).ToUpperInvariant())
                {
                    combo.Items.Add(new ComboBoxItem { Content = $"{letterStr}:" });
                }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;

            stack.Children.Add(combo);
            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && combo.SelectedItem is ComboBoxItem item && item.Content is string chosen)
            {
                string newLetter = chosen.TrimEnd(':');
                var (success, msg) = await SystemSpecsService.ChangeDriveLetterAsync(drive.Letter, newLetter);
                if (success)
                {
                    ShowInfoBar("تغییر حرف درایو", msg, InfoBarSeverity.Success);
                    await LoadSystemSpecsPageAsync(forceRefresh: true);
                }
                else
                {
                    ShowInfoBar("خطا در تغییر حرف درایو", msg, InfoBarSeverity.Error);
                }
            }
        }

        // ====================================================================
        // Tool 2: App Uninstaller
        // ====================================================================

        private async Task LoadAppUninstallerPageAsync(bool forceReload = false)
        {
            if (_allInstalledApps.Count > 0 && !forceReload)
            {
                FilterAndRenderApps();
                return;
            }

            UninstallerLoadingSpinner.Visibility = Visibility.Visible;
            UninstallerAppsList.Visibility = Visibility.Collapsed;
            if (UninstallerEmptyStack != null)
            {
                UninstallerEmptyStack.Children.Clear();
                UninstallerEmptyStack.Visibility = Visibility.Collapsed;
            }

            try
            {
                _allInstalledApps = await AppUninstallerService.GetInstalledAppsAsync();
                long totalSize = _allInstalledApps.Sum(a => a.EstimatedSizeBytes);
                UninstallerSummaryText.Text = $"تعداد کل: {_allInstalledApps.Count} نرم افزار  •  حجم کل: {SystemStats.FormatBytes(totalSize)}";
                FilterAndRenderApps();
            }
            catch (Exception ex)
            {
                UninstallerLoadingSpinner.Visibility = Visibility.Collapsed;
                UninstallerAppsList.Visibility = Visibility.Visible;
                UninstallerSummaryText.Text = $"خطا در بارگذاری برنامه ها: {ex.Message}";
            }
        }

        private void FilterAndRenderApps()
        {
            // فیلد به local منتقل می شود تا null-analysis رزرو شود (بعد از هر فراخوانی
            // متد، حالت null فیلدها ریست می شود و کامپایلر هشدار CS8604 می دهد)
            var apps = _allInstalledApps;
            if (UninstallerAppsList == null || UninstallerSearchBox == null || UninstallerSortBox == null || apps == null)
                return;

            if (UninstallerLoadingSpinner != null) UninstallerLoadingSpinner.Visibility = Visibility.Collapsed;

            if (UninstallerEmptyStack != null)
            {
                UninstallerEmptyStack.Children.Clear();
                UninstallerEmptyStack.Visibility = Visibility.Collapsed;
            }

            if (LauncherUninstallerBadge != null)
            {
                long totalSize = apps.Sum(a => a.EstimatedSizeBytes);
                string sizeStr = totalSize > 0 ? $" · {SystemStats.FormatBytes(totalSize)}" : "";
                LauncherUninstallerBadge.Text = $"{apps.Count} برنامه{sizeStr}";
            }

            string query = UninstallerSearchBox.Text?.Trim() ?? string.Empty;
            int sortIndex = UninstallerSortBox.SelectedIndex;

            // C6: فیلتر/مرتب‌سازی در Core تست‌پذیر شد (AppListQuery)
            var filtered = AppListQuery.Sort(AppListQuery.Filter(apps, query), sortIndex);

            // B3: ساخت ViewModel به جای ساخت دستی کارت ها — ListView فقط کارت های داخل viewport را واقعی می کند
            Brush normalCardBrush = GetThemeBrush("AppCardBorderBrush");
            var selectedCardBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];

            var itemVms = new List<AppListItemViewModel>(filtered.Count);
            foreach (var app in filtered)
            {
                string? iconPath = null;
                if (!string.IsNullOrEmpty(app.DisplayIcon))
                {
                    iconPath = GetOrExtractAppIconPath(app.DisplayIcon, app.DisplayName);
                }
                if (string.IsNullOrEmpty(iconPath) && !string.IsNullOrEmpty(app.UninstallString))
                {
                    iconPath = GetOrExtractAppIconPath(app.UninstallString, app.DisplayName);
                }
                itemVms.Add(new AppListItemViewModel(app, iconPath, normalCardBrush, selectedCardBrush));
            }
            UninstallerAppsList.ItemsSource = itemVms;

            // حالت خالی: یا جستجوی بی نتیجه، یا لیست نصب شده ها واقعاً خالی است
            if (filtered.Count == 0)
            {
                UninstallerAppsList.Visibility = Visibility.Collapsed;
                var emptyStack = UninstallerEmptyStack;
                if (emptyStack != null)
                {
                    emptyStack.Visibility = Visibility.Visible;
                    emptyStack.Children.Add(CreateEmptyStateMessage(
                        string.IsNullOrEmpty(query)
                            ? "هیچ نرم افزاری روی سیستم شناسایی نشد."
                            : $"هیچ نرم افزاری مطابق با «{query}» پیدا نشد.",
                        "\uE721"));
                }
            }
            else
            {
                UninstallerAppsList.Visibility = Visibility.Visible;
            }

            UpdateBatchUninstallerBar();
        }

        // B3: کلیک CheckBox — مقدار از طریق TwoWay binding در VM (و مدل اصلی) نوشته شده؛ فقط نوار گروهی را تازه می کنیم
        private void AppListCheck_Click(object sender, RoutedEventArgs e)
        {
            UpdateBatchUninstallerBar();
        }

        // D5: دکمه حذف داخل DataTemplate — اپ متناظر از DataContext خوانده می شود
        private void AppListUninstall_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AppListItemViewModel vm)
            {
                SafeInvoker.Run(UninstallSingleAppAsync(vm.App));
            }
        }

        private void UpdateBatchUninstallerBar()
        {
            if (_allInstalledApps == null || UninstallerBatchBar == null) return;
            var selected = _allInstalledApps.Where(a => a.IsSelected).ToList();
            if (selected.Count > 0)
            {
                UninstallerBatchBar.Visibility = Visibility.Visible;
                long totalBytes = selected.Sum(a => a.EstimatedSizeBytes);
                string sizeStr = totalBytes > 0 ? $" ({SystemStats.FormatBytes(totalBytes)})" : "";
                UninstallerSelectedCountText.Text = $"{selected.Count} نرم افزار انتخاب گردیده{sizeStr}";
                UninstallerBatchDeleteBtnText.Text = $"حذف {selected.Count} برنامه انتخاب شده";
            }
            else
            {
                UninstallerBatchBar.Visibility = Visibility.Collapsed;
            }
        }

        private void UninstallerSelectAll_Checked(object sender, RoutedEventArgs e)
        {
            if (_allInstalledApps == null) return;
            string query = UninstallerSearchBox?.Text?.Trim() ?? string.Empty;
            // C6: همان فیلتر لیست (نام + ناشر) تا «انتخاب همه» دقیقاً موارد دیده‌شده را انتخاب کند
            var targetList = AppListQuery.Filter(_allInstalledApps, query);

            foreach (var app in targetList) app.IsSelected = true;
            FilterAndRenderApps();
        }

        private void UninstallerSelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_allInstalledApps == null) return;
            foreach (var app in _allInstalledApps) app.IsSelected = false;
            FilterAndRenderApps();
        }

        private void UninstallerBatchDeleteBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(UninstallerBatchDeleteBtn_ClickAsync(sender, e));

        private async Task UninstallerBatchDeleteBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (_allInstalledApps == null) return;
            var selected = _allInstalledApps.Where(a => a.IsSelected).ToList();
            if (selected.Count == 0) return;

            var confirmDialog = new ContentDialog
            {
                Title = "تأیید حذف گروهی نرم افزارها",
                Content = new TextBlock
                {
                    Text = $"آیا از حذف {selected.Count} برنامه انتخاب شده اطمینان دارید؟ به منظور جلوگیری از تداخل فرآیندهای ویندوز، حذف هر برنامه به ترتیب (FIFO) و با باز شدن ایمن آنیستالر انجام خواهد شد.",
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20
                },
                PrimaryButtonText = "شروع حذف گروهی",
                CloseButtonText = "انصراف",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot,
                FlowDirection = FlowDirection.RightToLeft
            };

            var dialogResult = await confirmDialog.ShowAsync();
            if (dialogResult != ContentDialogResult.Primary) return;

            UninstallerBatchDeleteBtn.IsEnabled = false;
            int total = selected.Count;
            int current = 0;

            foreach (var app in selected)
            {
                current++;
                ShowInfoBar("فرآیند حذف گروهی", $"در حال اجرای آنیستالر {app.DisplayName} ({current} از {total})...", InfoBarSeverity.Informational);
                bool ok = await AppUninstallerService.LaunchUninstallAsync(app);
                await Task.Delay(500);
            }

            UninstallerBatchDeleteBtn.IsEnabled = true;
            ShowInfoBar("پایان عملیات حذف", $"تمام برنامه های انتخاب شده ({total} مورد) در صف آنیستال قرار گرفتند.", InfoBarSeverity.Success);
            await LoadAppUninstallerPageAsync();
        }

        // D5: حذف تکی نرم افزار هم مثل حذف گروهی نیاز به تأیید صریح کاربر دارد
        private async Task UninstallSingleAppAsync(InstalledAppInfo app)
        {
            var confirmDialog = new ContentDialog
            {
                Title = "تأیید حذف نرم افزار",
                Content = new TextBlock
                {
                    Text = $"آیا از حذف «{app.DisplayName}» اطمینان دارید؟\nآنیستالر رسمی برنامه باز خواهد شد و ادامه ی حذف در اختیار خودش است.",
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20
                },
                PrimaryButtonText = "حذف",
                CloseButtonText = "انصراف",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot,
                FlowDirection = FlowDirection.RightToLeft
            };

            if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary) return;

            bool launched = AppUninstallerService.LaunchUninstall(app);
            if (launched)
            {
                ShowInfoBar("حذف نرم افزار", $"آنیستالر رسمی {app.DisplayName} با موفقیت اجرا گردید.", InfoBarSeverity.Informational);
            }
            else
            {
                ShowInfoBar("خطا در حذف", $"امکان اجرای آنیستالر برای {app.DisplayName} وجود ندارد.", InfoBarSeverity.Warning);
            }
        }

        private void UninstallerSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (UninstallerSearchBox == null || UninstallerAppsList == null || UninstallerSortBox == null || _allInstalledApps == null)
                return;
            _uninstallerSearchDebounce.Stop();
            _uninstallerSearchDebounce.Start();
        }

        private void UninstallerSortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UninstallerSearchBox == null || UninstallerAppsList == null || UninstallerSortBox == null || _allInstalledApps == null)
                return;
            FilterAndRenderApps();
        }

        // ====================================================================
        // Tool 3: Internet Speed Test
        // ====================================================================

        private void StartSpeedTestBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(StartSpeedTestBtn_ClickAsync(sender, e));

        private void StopSpeedTestBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _speedTestCts?.Cancel();
            }
            catch { }
            if (StartSpeedTestBtn != null) StartSpeedTestBtn.IsEnabled = true;
            if (StopSpeedTestBtn != null) StopSpeedTestBtn.IsEnabled = false;
            if (SpeedGaugePhaseText != null) SpeedGaugePhaseText.Text = LocalizationService.Get("SpeedTestCancelled", "تست متوقف شد");
        }

        private async Task StartSpeedTestBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            StartSpeedTestBtn.IsEnabled = false;
            if (StopSpeedTestBtn != null) StopSpeedTestBtn.IsEnabled = true;
            _speedTestCts = new CancellationTokenSource();

            SpeedGaugeValueText.Text = "0";
            SpeedGaugeMBsText.Text = "در حال اتصال به سرور...";
            SpeedGaugePhaseText.Text = "در حال آماده سازی و سنجش تاخیر...";
            SpeedPingValText.Text = "-- ms";
            SpeedDownloadValText.Text = "-- Mbps";
            SpeedUploadValText.Text = "-- Mbps";
            SpeedGaugeRing.IsIndeterminate = false;
            SpeedGaugeRing.Value = 0;

            try
            {
                var result = await InternetSpeedTestService.RunSpeedTestAsync((phase, mbps, pct, curResult) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (curResult != null && !string.IsNullOrEmpty(curResult.ClientIp))
                        {
                            SpeedIpChipText.Text = curResult.ClientIp;
                            SpeedIspChipText.Text = !string.IsNullOrEmpty(curResult.IspName) ? curResult.IspName : "ارائه دهنده فعال";
                            string loc = $"{curResult.City}, {curResult.Country}".Trim(',', ' ');
                            string flag = !string.IsNullOrEmpty(curResult.FlagEmoji) ? curResult.FlagEmoji + " " : "";
                            SpeedLocationChipText.Text = !string.IsNullOrEmpty(loc) ? $"{flag}{loc}" : "سرور ابری جهانی";
                        }

                        int curMbpsInt = (int)Math.Round(mbps);
                        int curMbsInt = (int)Math.Round(mbps / 8.0);

                        if (phase == "meta")
                        {
                            SpeedGaugePhaseText.Text = "اطلاعات شبکه دریافت شد. در حال سنجش تاخیر...";
                        }
                        else if (phase == "ping")
                        {
                            SpeedGaugePhaseText.Text = "در حال سنجش تاخیر پینگ...";
                            SpeedPingValText.Text = $"{curMbpsInt} ms";
                        }
                        else if (phase == "download")
                        {
                            SpeedGaugePhaseText.Text = "در حال سنجش سرعت دانلود چندکاناله...";
                            SpeedGaugeValueText.Text = $"{curMbpsInt}";
                            int peakMBsInt = curResult != null && curResult.PeakDownloadSpeedMbps > 0 ? (int)Math.Round(curResult.PeakDownloadSpeedMbps / 8.0) : curMbsInt;
                            SpeedGaugeMBsText.Text = $"{curMbsInt} MB/s (پیک: {peakMBsInt} MB/s)";
                            SpeedDownloadValText.Text = $"{curMbpsInt} Mbps ({curMbsInt} MB/s)";
                            SpeedGaugeRing.Value = pct;
                        }
                        else if (phase == "upload")
                        {
                            SpeedGaugePhaseText.Text = "در حال ارسال داده (آپلود)...";
                            SpeedGaugeValueText.Text = $"{curMbpsInt}";
                            SpeedGaugeMBsText.Text = $"{curMbsInt} MB/s";
                            SpeedUploadValText.Text = $"{curMbpsInt} Mbps ({curMbsInt} MB/s)";
                            SpeedGaugeRing.Value = pct;
                        }
                    });
                }, _speedTestCts.Token);

                if (result.IsSuccess)
                {
                    SpeedIpChipText.Text = !string.IsNullOrEmpty(result.ClientIp) ? result.ClientIp : "نامشخص";
                    SpeedIspChipText.Text = !string.IsNullOrEmpty(result.IspName) ? result.IspName : "شبکه اینترنت";
                    string loc = $"{result.City}, {result.Country}".Trim(',', ' ');
                    string flag = !string.IsNullOrEmpty(result.FlagEmoji) ? result.FlagEmoji + " " : "";
                    SpeedLocationChipText.Text = !string.IsNullOrEmpty(loc) ? $"{flag}{loc}" : "سرور ابری جهانی";

                    SpeedGaugePhaseText.Text = "تست با موفقیت تکمیل شد";
                    SpeedGaugeValueText.Text = $"{result.DownloadSpeedMbpsInt}";
                    SpeedGaugeMBsText.Text = $"{result.DownloadSpeedMBsInt} MB/s (اوج دانلود: {result.PeakDownloadSpeedMBsInt} MB/s)";
                    SpeedPingValText.Text = $"{result.PingMsInt} ms";
                    SpeedDownloadValText.Text = $"{result.DownloadSpeedMbpsInt} Mbps ({result.DownloadSpeedMBsInt} MB/s)";
                    SpeedUploadValText.Text = result.UploadSpeedMbps > 0 ? $"{result.UploadSpeedMbpsInt} Mbps ({result.UploadSpeedMBsInt} MB/s)" : "--";
                    SpeedGaugeRing.Value = 100;

                    if (LauncherSpeedTestBadge != null)
                    {
                        LauncherSpeedTestBadge.Text = $"دانلود: {result.DownloadSpeedMbpsInt} Mbps ({result.DownloadSpeedMBsInt} MB/s)";
                    }
                    ShowInfoBar("تست سرعت اینترنت", $"تست تکمیل شد: دانلود {result.DownloadSpeedMbpsInt} Mbps ({result.DownloadSpeedMBsInt} MB/s) | آپلود {result.UploadSpeedMbpsInt} Mbps | پینگ: {result.PingMsInt} ms", InfoBarSeverity.Success);
                }
                else
                {
                    SpeedGaugePhaseText.Text = "خطا در تست";
                    SpeedGaugeMBsText.Text = result.ErrorMessage;
                    ShowInfoBar("نتیجه تست", result.ErrorMessage, InfoBarSeverity.Warning);
                }
            }
            catch (Exception)
            {
                SpeedGaugePhaseText.Text = "تست متوقف شد";
                SpeedGaugeMBsText.Text = "تست با خطا مواجه شد.";
            }
            finally
            {
                StartSpeedTestBtn.IsEnabled = true;
                if (StopSpeedTestBtn != null) StopSpeedTestBtn.IsEnabled = false;
            }
        }

        // ====================================================================
        // Tool 4: Windows Appearance & Personalization
        // ====================================================================

        private void LoadPersonalizationPage()
        {
            PersonalizationCardsContainer.Children.Clear();
            var current = WindowsPersonalizationService.GetCurrentSettings();

            // 1. Transparency
            PersonalizationCardsContainer.Children.Add(CreateToggleCard("\uE2B1", "شفافیت شیشه ای ویندوز (Transparency)", "فعال سازی افکت شیشه ای در تسک بار، استارت و پنجره های ویندوز", current.IsTransparencyEnabled, default, (isOn) =>
            {
                WindowsPersonalizationService.SetTransparency(isOn);
                ShowInfoBar("شفافیت ویندوز", isOn ? "افکت شیشه ای فعال شد." : "افکت شیشه ای غیرفعال شد.", InfoBarSeverity.Success);
            }));

            // 2. Auto Accent Color
            PersonalizationCardsContainer.Children.Add(CreateToggleCard("\uE790", "رنگ Accent هوشمند والپیپر", "استخراج و هماهنگ سازی خودکار رنگ اصلی ویندوز از تصویر دسکتاپ", current.IsAutoColorizationEnabled, default, (isOn) =>
            {
                WindowsPersonalizationService.SetAutoColorization(isOn);
                ShowInfoBar("رنگ ویندوز", isOn ? "هماهنگی خودکار رنگ با والپیپر فعال شد." : "هماهنگی خودکار غیرفعال شد.", InfoBarSeverity.Success);
            }));

            // 3. Dark Mode
            PersonalizationCardsContainer.Children.Add(CreateToggleCard("\uE706", "تم تاریک ویندوز (Dark Mode)", "اعمال پوسته تیره روی محیط ویندوز و نرم افزارهای سیستمی", current.IsDarkModeEnabled, default, (isOn) =>
            {
                WindowsPersonalizationService.SetWindowsDarkMode(isOn);
                ShowInfoBar("تم ویندوز", isOn ? "حالت تاریک فعال شد." : "حالت روشن فعال شد.", InfoBarSeverity.Success);
            }));

            // 4. Performance Visual Effects
            PersonalizationCardsContainer.Children.Add(CreateToggleCard("\uE9A1", "حالت عملکرد و چابکی بالا (Best Performance)", "کاهش انیمیشن های اضافی ویندوز برای افزایش سرعت پاسخ دهی سیستم", current.IsBestPerformanceFx, default, (isOn) =>
            {
                WindowsPersonalizationService.SetVisualEffectsPerformance(isOn);
                ShowInfoBar("افکت های بصری", isOn ? "تنظیمات روی حداکثر کارایی قرار گرفت." : "تنظیمات روی حالت پیش فرض قرار گرفت.", InfoBarSeverity.Success);
            }));

            // 5. Show File Extensions
            PersonalizationCardsContainer.Children.Add(CreateToggleCard("\uE8E5", "نمایش پسوند فایل ها در ویندوز", "نمایش پسوند نام فایل ها (مانند .exe, .pdf, .docx) در File Explorer", current.IsShowFileExtensionsEnabled, default, (isOn) =>
            {
                WindowsPersonalizationService.SetShowFileExtensions(isOn);
                ShowInfoBar("نمایش پسوندها", isOn ? "نمایش پسوند فایل ها فعال شد." : "پسوند فایل ها پنهان شد.", InfoBarSeverity.Success);
            }));

            // 6. Show Hidden Files
            PersonalizationCardsContainer.Children.Add(CreateToggleCard("\uE8B7", "نمایش فایل ها و پوشه های مخفی", "آشکارسازی فایل ها و پوشه های پنهان سیستمی در فایل اکسپلورر", current.IsShowHiddenFilesEnabled, default, (isOn) =>
            {
                WindowsPersonalizationService.SetShowHiddenFiles(isOn);
                ShowInfoBar("فایل های مخفی", isOn ? "فایل های مخفی آشکار شدند." : "فایل های مخفی پنهان شدند.", InfoBarSeverity.Success);
            }));

            // 7. Classic Context Menu
            PersonalizationCardsContainer.Children.Add(CreateToggleCard("\uE8BD", "منوی راست کلیک کلاسیک ویندوز ۱۱", "بازگشت منوی راست کلیک کامل و سنتی بدون نیاز به فشردن Show more options", current.IsClassicContextMenuEnabled, default, (isOn) =>
            {
                WindowsPersonalizationService.SetClassicContextMenu(isOn);
                ShowInfoBar("منوی راست کلیک", isOn ? "منوی راست کلیک کلاسیک فعال شد." : "منوی راست کلیک مدرن ویندوز ۱۱ فعال شد.", InfoBarSeverity.Success);
            }));

            // 8. Fast Startup (Hiberboot) Optimization
            var fastStatus = FastStartupService.GetStatus();
            PersonalizationCardsContainer.Children.Add(CreateToggleCard("\uE7E8", "بوت سریع ویندوز (Fast Startup / Hiberboot)", "خاموش سازی حالت هایبرید ویندوز جهت ریست کامل درایورها هنگام خاموشی، پیشگیری از خطای کارت شبکه و کاهش استهلاک SSD", fastStatus.IsFastStartupEnabled, default, (isOn) =>
            {
                bool ok = FastStartupService.SetFastStartup(isOn);
                if (ok)
                {
                    ShowInfoBar("بوت سریع ویندوز", isOn ? "قابلیت بوت سریع فعال شد." : "قابلیت Fast Startup غیرفعال شد (خاموشی کامل و سالم درایورها).", InfoBarSeverity.Success);
                }
                else
                {
                    ShowInfoBar("خطای دسترسی", "جهت تغییر تنظیمات بوت سریع نیاز به اجرای برنامه با دسترسی Administrator است.", InfoBarSeverity.Error);
                }
            }));

            // 9. Hibernation & hiberfil.sys Disk Cleanup
            string hiberDesc = fastStatus.IsHibernationEnabled
                ? $"فعال است (حجم اشغال شده توسط hiberfil.sys: {fastStatus.FormattedHiberfilSize}) - غیرفعال سازی فضای عظیمی را روی درایو C آزاد می کند."
                : "غیرفعال است (فایل hiberfil.sys حذف شده و فضای درایو C آزاد است).";

            PersonalizationCardsContainer.Children.Add(CreateToggleCard("\uE9A6", "خواب زمستانی و پاکسازی hiberfil.sys", hiberDesc, fastStatus.IsHibernationEnabled, default, async (isOn) =>
            {
                bool ok = await FastStartupService.SetHibernationAsync(isOn);
                if (ok)
                {
                    ShowInfoBar("هایبرنیشن و دیسک", isOn ? "خواب زمستانی فعال شد." : "خواب زمستانی غیرفعال و فایل hiberfil.sys از درایو C حذف گردید.", InfoBarSeverity.Success);
                    LoadPersonalizationPage(); // Refresh card to update file size info
                }
                else
                {
                    ShowInfoBar("خطا", "برای تغییر وضعیت هایبرنیشن اجرای برنامه با دسترسی Administrator الزامی است.", InfoBarSeverity.Error);
                }
            }));

            // 10. Windows Security & Defender (Shortcut to Tamper Protection & Antivirus)
            PersonalizationCardsContainer.Children.Add(CreateActionCard("\uE838", "امنیت و آنتی ویروس ویندوز (Windows Security & Defender)", "دسترسی مستقیم به تنظیمات محافظت بلادرنگ، فایروال و Tamper Protection", "تنظیمات امنیت", default, () =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo("windowsdefender://threatsettings") { UseShellExecute = true });
                }
                catch
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true });
                    }
                    catch
                    {
                        ShowInfoBar("امنیت ویندوز", "امکان باز کردن مستقیم مرکز امنیت ویندوز فراهم نشد.", InfoBarSeverity.Warning);
                    }
                }
            }));

            // 11. Glass / Mica Presets & Options
            PersonalizationCardsContainer.Children.Add(CreateGlassConfigCard());

            // 12. Wallpaper Management & CleanC Gallery
            PersonalizationCardsContainer.Children.Add(CreateWallpaperManagementCard());
        }

        private Border CreateActionCard(string glyph, string title, string description, string actionBtnText, Windows.UI.Color iconBgColor = default, Action? onClick = null)
        {
            var border = new Border
            {
                Background = GetThemeBrush("AppCardBackgroundBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBg = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(8),
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            
            var icon = new FontIcon
            {
                Glyph = glyph,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = GetThemeBrush("AccentFillColorDefaultBrush")
            };
            iconBg.Child = icon;
            Grid.SetColumn(iconBg, 0);
            grid.Children.Add(iconBg);

            var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var tBlock = new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12.5 };
            var dBlock = new TextBlock { Text = description, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = GetThemeBrush("TextFillColorSecondaryBrush") };
            textStack.Children.Add(tBlock);
            if (!string.IsNullOrEmpty(description))
            {
                textStack.Children.Add(dBlock);
            }
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            var btn = new Button
            {
                Content = actionBtnText,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Height = 32,
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            if (onClick != null) btn.Click += (s, e) => onClick();
            Grid.SetColumn(btn, 2);
            grid.Children.Add(btn);

            border.Child = grid;
            return border;
        }

        private Border CreateGlassConfigCard()
        {
            var card = new Border
            {
                Background = GetThemeBrush("AppCardBackgroundBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var mainStack = new StackPanel { Spacing = 12 };

            // Header
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconBg = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(8),
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                Child = new FontIcon { Glyph = "\uE790", FontSize = 15, Foreground = GetThemeBrush("AccentFillColorDefaultBrush") }
            };
            Grid.SetColumn(iconBg, 0);
            headerGrid.Children.Add(iconBg);

            var titleStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock { Text = "پیکربندی شیشه ای و بلور ویندوز", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12.5 });
            titleStack.Children.Add(new TextBlock { Text = "تنظیمات پیشرفته شفافیت پنجره ها، نوار آدرس و حاشیه ها با اعمال بلادرنگ", FontSize = 11, Foreground = GetThemeBrush("TextFillColorSecondaryBrush") });
            Grid.SetColumn(titleStack, 1);
            headerGrid.Children.Add(titleStack);

            mainStack.Children.Add(headerGrid);

            // Sub options
            var cfg = WindowsPersonalizationService.LoadGlassConfig();

            var optionsStack = new StackPanel { Spacing = 8, Margin = new Thickness(46, 0, 0, 0) };

            var chkClearAddress = new CheckBox { Content = "شفاف سازی کامل نوار آدرس فایل اکسپلورر", IsChecked = cfg.ClearAddress, FontSize = 11.5 };
            var chkClearBarBg = new CheckBox { Content = "شفاف سازی نوار ابزار و تب ها", IsChecked = cfg.ClearBarBg, FontSize = 11.5 };
            var chkClearWinUI = new CheckBox { Content = "شفاف سازی پس زمینه کنترل های مدرن WinUI", IsChecked = cfg.ClearWinUIBg, FontSize = 11.5 };
            var chkShowLine = new CheckBox { Content = "نمایش حاشیه و خط تفکیک دور پنجره ها", IsChecked = cfg.ShowLine, FontSize = 11.5 };

            optionsStack.Children.Add(chkClearAddress);
            optionsStack.Children.Add(chkClearBarBg);
            optionsStack.Children.Add(chkClearWinUI);
            optionsStack.Children.Add(chkShowLine);

            // Action buttons
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            var saveBtn = new Button
            {
                Content = "ذخیره و اعمال تنظیمات",
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                Height = 30, FontSize = 11.5, CornerRadius = new CornerRadius(6)
            };
            saveBtn.Click += (s, e) =>
            {
                cfg.ClearAddress = chkClearAddress.IsChecked == true;
                cfg.ClearBarBg = chkClearBarBg.IsChecked == true;
                cfg.ClearWinUIBg = chkClearWinUI.IsChecked == true;
                cfg.ShowLine = chkShowLine.IsChecked == true;
                WindowsPersonalizationService.SaveGlassConfig(cfg);
                WindowsPersonalizationService.BroadcastSettingsChange();
                ShowInfoBar("تنظیمات شیشه ای", "تنظیمات شفافیت با موفقیت ذخیره و در ویندوز اعمال شد.", InfoBarSeverity.Success);
            };

            var resetBtn = new Button
            {
                Content = "بازنشانی به پیش فرض",
                Height = 30, FontSize = 11.5, CornerRadius = new CornerRadius(6),
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush")
            };
            resetBtn.Click += (s, e) =>
            {
                cfg = new GlassPresetConfig();
                chkClearAddress.IsChecked = true;
                chkClearBarBg.IsChecked = true;
                chkClearWinUI.IsChecked = true;
                chkShowLine.IsChecked = true;
                WindowsPersonalizationService.SaveGlassConfig(cfg);
                WindowsPersonalizationService.BroadcastSettingsChange();
                ShowInfoBar("بازنشانی تنظیمات", "تنظیمات به مقادیر پیش فرض بازنشانی شد.", InfoBarSeverity.Informational);
            };

            btnRow.Children.Add(saveBtn);
            btnRow.Children.Add(resetBtn);
            optionsStack.Children.Add(btnRow);

            mainStack.Children.Add(optionsStack);
            card.Child = mainStack;
            return card;
        }

        private Border CreateWallpaperManagementCard()
        {
            var card = new Border
            {
                Background = GetThemeBrush("AppCardBackgroundBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var mainStack = new StackPanel { Spacing = 12 };

            // Header
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconBg = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(8),
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                Child = new FontIcon { Glyph = "\uEB9F", FontSize = 15, Foreground = GetThemeBrush("AccentFillColorDefaultBrush") }
            };
            Grid.SetColumn(iconBg, 0);
            headerGrid.Children.Add(iconBg);

            var titleStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock { Text = "گالری والپیپرهای CleanC و تصویر پس زمینه ویندوز", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12.5 });
            var wallpapers = WindowsPersonalizationService.GetCleanCWallpapers();
            titleStack.Children.Add(new TextBlock { Text = $"انتخاب بین والپیپر فعلی ویندوز یا گالری والپیپرهای سفارشی CleanC ({wallpapers.Count} والپیپر گلچین 4K)", FontSize = 11, Foreground = GetThemeBrush("TextFillColorSecondaryBrush") });
            Grid.SetColumn(titleStack, 1);
            headerGrid.Children.Add(titleStack);

            mainStack.Children.Add(headerGrid);

            // Sub options
            var optionsStack = new StackPanel { Spacing = 10, Margin = new Thickness(46, 0, 0, 0) };

            string curWp = WindowsPersonalizationService.GetCurrentWindowsWallpaper();
            string wpName = !string.IsNullOrEmpty(curWp) ? Path.GetFileName(curWp) : "والپیپر پیش فرض سیستم";
            var curText = new TextBlock
            {
                Text = $"تصویر فعلی پس زمینه: {wpName}",
                FontSize = 11.5,
                Foreground = GetThemeBrush("TextFillColorSecondaryBrush")
            };
            optionsStack.Children.Add(curText);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            var randomWpBtn = new Button
            {
                Content = "🎲 اعمال والپیپر تصادفی جدید CleanC",
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                Height = 32, FontSize = 11.5, CornerRadius = new CornerRadius(6)
            };
            randomWpBtn.Click += (s, e) =>
            {
                var list = WindowsPersonalizationService.GetCleanCWallpapers();
                if (list.Count > 0)
                {
                    string chosen = list[Random.Shared.Next(list.Count)];
                    bool ok = WindowsPersonalizationService.SetDesktopWallpaper(chosen);
                    if (ok)
                    {
                        curText.Text = $"تصویر فعلی پس زمینه: {Path.GetFileName(chosen)}";
                        ShowInfoBar("والپیپر دسکتاپ", $"تصویر پس زمینه دسکتاپ به «{Path.GetFileName(chosen)}» تغییر یافت.", InfoBarSeverity.Success);
                    }
                    else
                    {
                        ShowInfoBar("خطا در تغییر والپیپر", "امکان تغییر والپیپر دسکتاپ وجود ندارد.", InfoBarSeverity.Warning);
                    }
                }
                else
                {
                    ShowInfoBar("والپیپر", "هیچ فایلی در پوشه والپیپرها یافت نشد.", InfoBarSeverity.Informational);
                }
            };

            var openFolderBtn = new Button
            {
                Content = "باز کردن پوشه والپیپرها",
                Height = 32, FontSize = 11.5, CornerRadius = new CornerRadius(6),
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush")
            };
            openFolderBtn.Click += (s, e) =>
            {
                string dir = Path.Combine(Directory.GetCurrentDirectory(), "wallpaper");
                if (!Directory.Exists(dir)) dir = @"C:\Users\moham\Pictures\Wallpaper";
                OpenFolder(dir);
            };

            btnRow.Children.Add(randomWpBtn);
            btnRow.Children.Add(openFolderBtn);
            optionsStack.Children.Add(btnRow);

            mainStack.Children.Add(optionsStack);
            card.Child = mainStack;
            return card;
        }

        private Border CreateToggleCard(string glyph, string title, string description, bool initialValue, Windows.UI.Color iconBgColor = default, Action<bool>? onToggle = null)
        {
            var border = new Border
            {
                Background = GetThemeBrush("AppCardBackgroundBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBg = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(8),
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            
            var icon = new FontIcon
            {
                Glyph = glyph,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = GetThemeBrush("AccentFillColorDefaultBrush")
            };
            iconBg.Child = icon;
            
            Grid.SetColumn(iconBg, 0);
            grid.Children.Add(iconBg);

            var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var tBlock = new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12.5 };
            var dBlock = new TextBlock { Text = description, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = GetThemeBrush("TextFillColorSecondaryBrush") };
            textStack.Children.Add(tBlock);
            if (!string.IsNullOrEmpty(description))
            {
                textStack.Children.Add(dBlock);
            }
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            var toggle = new ToggleSwitch
            {
                IsOn = initialValue,
                OnContent = null,
                OffContent = null,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            if (onToggle != null) toggle.Toggled += (s, e) => onToggle(toggle.IsOn);
            Grid.SetColumn(toggle, 2);
            grid.Children.Add(toggle);

            border.Child = grid;
            return border;
        }

        // ====================================================================
        // Tool 5: Startup Manager
        // ====================================================================

        private static string ExtractCleanExePath(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;
            string s = command.Trim();

            // Strip comma arguments like "C:\...\app.exe,0"
            if (s.Contains(','))
            {
                s = s.Split(',')[0].Trim('"', ' ');
            }

            // 1. Quoted path: "C:\path to\app.exe" /args
            if (s.StartsWith("\""))
            {
                int end = s.IndexOf('"', 1);
                if (end > 1) return s.Substring(1, end - 1).Trim();
            }

            // 2. Unquoted path ending in .exe followed by arguments: C:\Program Files\IDM\IDMan.exe /onboot
            int exeIdx = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx > 0)
            {
                string candidate = s.Substring(0, exeIdx + 4).Trim();
                if (File.Exists(candidate)) return candidate;
            }

            // 3. Fallback: try splitting on first space
            int sp = s.IndexOf(' ');
            if (sp > 0)
            {
                string first = s.Substring(0, sp).Trim();
                if (File.Exists(first)) return first;
            }

            if (File.Exists(s)) return s;
            return s;
        }

        private static string? GetOrExtractAppIconPath(string command, string appName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(command)) return null;

                string rawTrimmed = command.Trim('"', ' ');
                if (File.Exists(rawTrimmed))
                {
                    string rawExt = Path.GetExtension(rawTrimmed).ToLowerInvariant();
                    if (rawExt == ".png" || rawExt == ".jpg" || rawExt == ".ico") return rawTrimmed;
                }

                string exe = ExtractCleanExePath(command);
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    string ext = Path.GetExtension(exe).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".ico") return exe;

                    // هش قطعی به جای GetHashCode — جزئیات در StableHash32
                    string hash = StableHash32(exe + appName);
                    string cacheDir = Path.Combine(Path.GetTempPath(), "CleanC", "Icons");
                    string iconPath = Path.Combine(cacheDir, $"{hash}.png");
                    if (File.Exists(iconPath)) return iconPath;

                    Directory.CreateDirectory(cacheDir);
                    PurgeStaleIconCache(cacheDir);

                    using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                    if (sysIcon != null)
                    {
                        using var bmp = sysIcon.ToBitmap();
                        bmp.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
                        return iconPath;
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
            return null;
        }

        private static bool _iconCachePurged;

        /// <summary>
        /// پاک سازی یک باره ی کش آیکون های قدیمی.
        ///
        /// نسخه های قبلی با هش randomize شده ی <c>GetHashCode()</c> فایل می ساختند که
        /// هیچ وقت دوباره پیدا نمی شد؛ در نتیجه پوشه ی کش انباشته می شد.
        /// فایل های بالای ۳ روز امن حذف می شوند چون در صورت نیاز بازسازی می شوند.
        /// </summary>
        private static void PurgeStaleIconCache(string cacheDir)
        {
            if (_iconCachePurged) return;
            _iconCachePurged = true;

            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-3);
                long totalBytes = 0;
                var files = new DirectoryInfo(cacheDir).GetFiles("*.png")
                    .OrderBy(f => f.LastWriteTimeUtc)
                    .ToList();

                foreach (var file in files)
                {
                    try
                    {
                        if (file.LastWriteTimeUtc < cutoff || totalBytes > 15_000_000)
                        {
                            file.Delete();
                        }
                        else
                        {
                            totalBytes += file.Length;
                        }
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }

        /// <summary>
        /// هش FNV-1a (32 بیت) قطعی برای نام فایل کش آیکون.
        ///
        /// دلیل رد شدن از GetHashCode: در .NET Core رشته ها per-process randomize
        /// می شوند (یعنی کش هر بار از نو ساخته می شد) و <c>Math.Abs(int.MinValue)</c>
        /// همیشه <c>OverflowException</c> پرتاب می کند.
        /// </summary>
        private static string StableHash32(string input)
        {
            unchecked
            {
                uint hash = 2166136261;   // FNV offset basis
                foreach (char c in input)
                {
                    hash ^= c;
                    hash *= 16777619;     // FNV prime
                }
                return hash.ToString("X8");
            }
        }

        private async Task LoadStartupManagerPageAsync()
        {
            StartupCardsContainer.Children.Clear();
            // اسکنِ تسک های زمان بندی شده چند ثانیه طول می کشد؛ از همین جا پیام بده
            // (این صفحه اسپینرِ مخصوص خودش را ندارد)
            StartupManagerSummaryText.Text = "در حال دریافت فهرست برنامه های استارتاپ...";

            var rawItems = await Task.Run(() => StartupOptimizerService.GetStartupItems());
            // Sort: Enabled apps first (IsDisabled == false), then disabled apps (IsDisabled == true)
            var items = rawItems.OrderBy(x => x.IsDisabled).ThenBy(x => x.Name).ToList();

            int activeCount = items.Count(x => !x.IsDisabled);
            int inactiveCount = items.Count(x => x.IsDisabled);
            StartupManagerSummaryText.Text = $"تعداد کل: {items.Count} برنامه  •  {activeCount} برنامه فعال  •  {inactiveCount} برنامه غیرفعال";

            if (LauncherStartupBadge != null)
            {
                LauncherStartupBadge.Text = $"{activeCount} فعال از {items.Count}";
            }

            if (items.Count == 0)
            {
                StartupCardsContainer.Children.Add(new TextBlock
                {
                    Text = "هیچ برنامه ای در استارتاپ ویندوز یافت نشد.",
                    FontSize = 13,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 24, 0, 0)
                });
                return;
            }

            bool headerAddedForDisabled = false;
            foreach (var item in items)
            {
                // Add a clear section divider before disabled apps if both exist
                if (item.IsDisabled && !headerAddedForDisabled && activeCount > 0)
                {
                    headerAddedForDisabled = true;
                    var divider = new Border
                    {
                        Margin = new Thickness(4, 12, 4, 6),
                        Child = new TextBlock
                        {
                            Text = "برنامه های غیرفعال در شروع ویندوز",
                            FontSize = 11.5,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = GetThemeBrush("TextFillColorTertiaryBrush")
                        }
                    };
                    StartupCardsContainer.Children.Add(divider);
                }
                var card = new Border
                {
                    Background = GetThemeBrush("AppCardBackgroundBrush"),
                    BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 12, 14, 12),
                    Margin = new Thickness(0, 0, 0, 6),
                    Opacity = item.IsDisabled ? 0.5 : 1.0
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // 1. App Icon Chip (34x34 Fluent 2 chip)
                var iconBox = new Border
                {
                    Width = 34,
                    Height = 34,
                    CornerRadius = new CornerRadius(8),
                    Background = GetThemeBrush("AppCardSecondaryBrush"),
                    BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                var fIcon = new FontIcon
                {
                    Glyph = "\uE7E8",
                    FontSize = 15,
                    Foreground = GetThemeBrush("AccentFillColorDefaultBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                iconBox.Child = fIcon;
                FrameworkElement iconElement = iconBox;

                var capturedItem = item;
                var capturedBox = iconBox;
                _ = Task.Run(() =>
                {
                    try
                    {
                        string? iconPath = GetOrExtractAppIconPath(capturedItem.Command, capturedItem.Name);
                        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                var img = new Image
                                {
                                    Width = 20,
                                    Height = 20,
                                    Source = new BitmapImage(new Uri(iconPath)),
                                    VerticalAlignment = VerticalAlignment.Center,
                                    HorizontalAlignment = HorizontalAlignment.Center
                                };
                                capturedBox.Child = img;
                            });
                        }
                    }
                    catch (Exception __ex)
                    {
                        CcLog.Ignore(__ex);
                    }
                });

                Grid.SetColumn(iconElement, 0);
                grid.Children.Add(iconElement);

                // 2. Texts
                var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                
                var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                var nameText = new TextBlock { Text = item.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
                topRow.Children.Add(nameText);

                // امتیاز واقعی تأثیر روی بوت (اندازه گیری شده از رویدادهای ویندوز
                // یا تخمین بر اساس دسته بندی) به جای هیوریستیک ثابت
                var impactBadge = new Border
                {
                    Background = GetThemeBrush("AppCardSecondaryBrush"),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8, 2, 8, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };

                string impactText = item.ImpactScore > 0
                    ? $"تأثیر {item.ImpactLabel} ({item.ImpactScore})"
                    : "تأثیر عادی";

                impactBadge.Child = new TextBlock
                {
                    Text = impactText,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = item.ImpactScore >= 65
                        ? GetThemeBrush("SystemFillColorCautionBrush")
                        : GetThemeBrush("TextFillColorSecondaryBrush")
                };
                topRow.Children.Add(impactBadge);

                if (item.IsDisabled)
                {
                    var disBadge = new Border
                    {
                        Background = GetThemeBrush("AppCardSecondaryBrush"),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(8, 2, 8, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    disBadge.Child = new TextBlock
                    {
                        Text = "غیرفعال",
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = GetThemeBrush("SystemFillColorCriticalBrush")
                    };
                    topRow.Children.Add(disBadge);
                }

                // D12 — نشان یتیم بودن: فایل اجرایی هدف دیگر روی دیسک نیست
                if (item.TargetMissing)
                {
                    var orphanBadge = new Border
                    {
                        Background = GetThemeBrush("AppCardSecondaryBrush"),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(8, 2, 8, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    orphanBadge.Child = new TextBlock
                    {
                        Text = LocalizationService.Get("BadgeTargetMissing", "هدف ناموجود"),
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = GetThemeBrush("SystemFillColorCautionBrush")
                    };
                    topRow.Children.Add(orphanBadge);
                }

                textStack.Children.Add(topRow);

                string cleanCmd = ExtractCleanExePath(item.Command);
                if (string.IsNullOrWhiteSpace(cleanCmd)) cleanCmd = item.Category;
                var cmdText = new TextBlock
                {
                    Text = cleanCmd,
                    FontSize = 11.5,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FlowDirection = FlowDirection.LeftToRight
                };
                textStack.Children.Add(cmdText);

                // اگر ویندوز برای این برنامه تأخیر بوت ثبت کرده باشد، همان عدد واقعی را نشان بده
                if (item.ImpactMeasured && item.ImpactMs > 0)
                {
                    textStack.Children.Add(new TextBlock
                    {
                        Text = $"تأخیر اندازه گیری شده در بالا آمدن ویندوز: {item.ImpactMs} میلی ثانیه",
                        FontSize = 10.5,
                        Foreground = GetThemeBrush("TextFillColorTertiaryBrush"),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }

                // شاهد D12 — «چرا» یتیم تشخیص داده شد (مسیر exe گم شده)
                if (item.TargetMissing && !string.IsNullOrEmpty(item.Evidence))
                {
                    textStack.Children.Add(new TextBlock
                    {
                        Text = item.Evidence,
                        FontSize = 10.5,
                        Foreground = GetThemeBrush("SystemFillColorCautionBrush"),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }

                Grid.SetColumn(textStack, 1);
                grid.Children.Add(textStack);

                // 3. ToggleSwitch
                var toggle = new ToggleSwitch
                {
                    IsOn = !item.IsDisabled,
                    OnContent = null,
                    OffContent = null,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                var capturedCard = card;
                // اگر غیرفعال/فعال سازی شکست بخورد (دسترسی نداشتن، قفل بودن، تسک ویندوزی و...)
                // باید کلید به حالت قبلی برگردد؛ وگرنه UI دروغ می گوید
                bool toggleGuard = false;
                toggle.Toggled += (s, e) =>
                {
                    if (toggleGuard) return;
                    bool willBeOn = toggle.IsOn;
                    bool ok;

                    if (!willBeOn)
                    {
                        ok = StartupOptimizerService.DisableStartupItem(item);
                        if (!ok)
                        {
                            toggleGuard = true;
                            toggle.IsOn = true;
                            toggleGuard = false;
                            capturedCard.Opacity = 1.0;
                            ShowInfoBar("استارتاپ ویندوز", $"غیرفعال کردن «{item.Name}» انجام نشد (دسترسی غیرمجاز یا آیتم مربوط به ویندوز است).", InfoBarSeverity.Error);
                            return;
                        }
                        item.IsDisabled = true;
                        capturedCard.Opacity = 0.5;
                        ShowInfoBar("استارتاپ ویندوز", $"برنامه {item.Name} غیرفعال شد و از اجرای خودکار آن جلوگیری می شود.", InfoBarSeverity.Informational);
                    }
                    else
                    {
                        // رکورد بکاپ با کلید «Location|Name» ذخیره شده؛ مکان هم جستجو می
                        // شود وگرنه برمی گردد به فرمت قدیمی «فقط نام»
                        ok = StartupOptimizerService.RestoreStartupItem(item.Name, item.Location);
                        if (!ok)
                        {
                            toggleGuard = true;
                            toggle.IsOn = false;
                            toggleGuard = false;
                            capturedCard.Opacity = 0.5;
                            ShowInfoBar("استارتاپ ویندوز", $"فعال کردن «{item.Name}» انجام نشد.", InfoBarSeverity.Error);
                            return;
                        }
                        item.IsDisabled = false;
                        capturedCard.Opacity = 1.0;
                        ShowInfoBar("استارتاپ ویندوز", $"برنامه {item.Name} مجدداً فعال شد.", InfoBarSeverity.Success);
                    }
                };
                Grid.SetColumn(toggle, 2);
                grid.Children.Add(toggle);

                card.Child = grid;
                StartupCardsContainer.Children.Add(card);
            }
        }

        private void RefreshStartupManager_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(RefreshStartupManager_ClickAsync(sender, e));

        private async Task RefreshStartupManager_ClickAsync(object sender, RoutedEventArgs e)
        {
            await LoadStartupManagerPageAsync();
            ShowInfoBar("استارتاپ ویندوز", "لیست برنامه های استارتاپ بروزرسانی شد.", InfoBarSeverity.Informational);
        }

        // ====================================================================
        // Tool 6: Windows Update Manager
        // ====================================================================

        private DispatcherTimer? _updateIconRotationTimer;

        private void StartUpdateIconRotation()
        {
            if (_updateIconRotationTimer == null)
            {
                _updateIconRotationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(25)
                };
                _updateIconRotationTimer.Tick += (s, e) =>
                {
                    if (UpdateStatusIconRotate != null)
                    {
                        UpdateStatusIconRotate.Angle = (UpdateStatusIconRotate.Angle + 12) % 360;
                    }
                    if (LauncherUpdateIconRotate != null)
                    {
                        LauncherUpdateIconRotate.Angle = (LauncherUpdateIconRotate.Angle + 12) % 360;
                    }
                };
            }
            _updateIconRotationTimer.Start();
        }

        private void StopUpdateIconRotation()
        {
            _updateIconRotationTimer?.Stop();
            if (UpdateStatusIconRotate != null) UpdateStatusIconRotate.Angle = 0;
            if (LauncherUpdateIconRotate != null) LauncherUpdateIconRotate.Angle = 0;
        }

        private async Task LoadWindowsUpdatePageAsync()
        {
            RefreshWindowsUpdateStatus();

            StartUpdateIconRotation();
            UpdatesLoadingSpinner.Visibility = Visibility.Visible;
            UpdatesLoadingSpinner.IsActive = true;
            PendingUpdatesContainer.Children.Clear();
            NoUpdatesBanner.Visibility = Visibility.Collapsed;
            PendingUpdatesCountText.Text = "در حال بررسی...";

            if (CheckUpdatesBtn != null) CheckUpdatesBtn.IsEnabled = false;
            if (CheckUpdatesBtnText != null) CheckUpdatesBtnText.Text = "در حال بررسی...";
            if (LauncherWindowsUpdateBadge != null)
            {
                LauncherWindowsUpdateBadge.Text = "در حال بررسی...";
            }

            try
            {
                var updates = await WindowsUpdateService.CheckPendingUpdatesAsync();
                UpdatesLoadingSpinner.Visibility = Visibility.Collapsed;
                UpdatesLoadingSpinner.IsActive = false;

                PendingUpdatesCountText.Text = $"{updates.Count} بروزرسانی";

                if (LauncherWindowsUpdateBadge != null)
                {
                    LauncherWindowsUpdateBadge.Text = updates.Count == 0 ? "سیستم به روز است ✓" : $"{updates.Count} آپدیت در انتظار";
                }

                if (updates.Count == 0)
                {
                    NoUpdatesBanner.Visibility = Visibility.Visible;
                }
                else
                {
                    NoUpdatesBanner.Visibility = Visibility.Collapsed;
                    foreach (var up in updates)
                    {
                        PendingUpdatesContainer.Children.Add(CreatePendingUpdateCard(up));
                    }
                }
            }
            catch (Exception ex)
            {
                UpdatesLoadingSpinner.Visibility = Visibility.Collapsed;
                UpdatesLoadingSpinner.IsActive = false;
                PendingUpdatesCountText.Text = "خطا در بررسی";
                ShowInfoBar("بروزرسانی ویندوز", $"خطا در جستجوی آپدیت ها: {ex.Message}", InfoBarSeverity.Warning);
            }
            finally
            {
                StopUpdateIconRotation();
                if (CheckUpdatesBtn != null) CheckUpdatesBtn.IsEnabled = true;
                if (CheckUpdatesBtnText != null) CheckUpdatesBtnText.Text = "بررسی آپدیت های جدید";
            }
        }

        private void RefreshWindowsUpdateStatus()
        {
            var status = WindowsUpdateService.GetCurrentStatus();

            if (status.IsPaused)
            {
                UpdateStatusHeadlineText.Text = $"آپدیت های سیستم عامل تا تاریخ {status.PauseExpiryDate} متوقف هستند";
                UpdatePauseDetailText.Text = $"توقف رسمی ۵ هفته ای فعال است. دریافت هرگونه بسته و آپدیت خودکار تا {status.PauseExpiryDate} به تعویق افتاده است.";
                PauseUpdatesBtn.IsEnabled = false;
                ResumeUpdatesBtn.IsEnabled = true;
            }
            else
            {
                UpdateStatusHeadlineText.Text = "وضعیت بروزرسانی ها: فعال و استاندارد ویندوز";
                UpdatePauseDetailText.Text = "توقف موقت فعال نیست. ویندوز مجاز به بررسی و دانلود دوره ای آپدیت ها است.";
                PauseUpdatesBtn.IsEnabled = true;
                ResumeUpdatesBtn.IsEnabled = false;
            }

            UpdateServiceDetailText.Text = $"وضعیت سرویس اصلی (wuauserv): {status.ServiceStatus}";
            DisableUpdateServiceBtn.IsEnabled = !status.IsServiceDisabled;
            EnableUpdateServiceBtn.IsEnabled = status.IsServiceDisabled;
        }

        private Border CreatePendingUpdateCard(PendingUpdateItem item)
        {
            var border = new Border
            {
                Background = GetThemeBrush("AppCardBackgroundBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 1. Icon Box (34x34 chip)
            var iconBox = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(8),
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var icon = new FontIcon
            {
                Glyph = item.IsNextGenFeatureUpdate ? "\uE7FB" : "\uE895",
                FontSize = 15,
                Foreground = GetThemeBrush("AccentFillColorDefaultBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBox.Child = icon;
            Grid.SetColumn(iconBox, 0);
            grid.Children.Add(iconBox);

            // 2. Text Stack
            var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var titleText = new TextBlock
            {
                Text = item.Title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(titleText);

            string sub = !string.IsNullOrEmpty(item.KbArticleId) ? item.KbArticleId : "";
            if (item.SizeBytes > 0)
            {
                sub = string.IsNullOrEmpty(sub) ? item.FormattedSize : $"{sub} · {item.FormattedSize}";
            }
            if (!string.IsNullOrEmpty(item.Description))
            {
                sub = string.IsNullOrEmpty(sub) ? item.Description : $"{sub} · {item.Description}";
            }

            var subText = new TextBlock
            {
                Text = sub,
                FontSize = 11.5,
                Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FlowDirection = FlowDirection.LeftToRight
            };
            stack.Children.Add(subText);

            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            // 3. Actions Column
            var actionStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

            var badgeBorder = new Border
            {
                Background = GetThemeBrush("AppAccentTintBrush"),
                BorderBrush = GetThemeBrush("AppAccentBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeBorder.Child = new TextBlock
            {
                Text = !string.IsNullOrWhiteSpace(item.CategoryName) ? item.CategoryName : "امنیتی",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = GetThemeBrush("AccentFillColorDefaultBrush")
            };
            actionStack.Children.Add(badgeBorder);

            var openBtn = new Button
            {
                Content = "نصب",
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                CornerRadius = new CornerRadius(6),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["AccentButtonStyle"]
            };
            openBtn.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true });
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }
            };
            actionStack.Children.Add(openBtn);

            Grid.SetColumn(actionStack, 2);
            grid.Children.Add(actionStack);

            border.Child = grid;
            return border;
        }

        private void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(CheckUpdatesBtn_ClickAsync(sender, e));

        private async Task CheckUpdatesBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var psi = new ProcessStartInfo("usoclient.exe", "StartInteractiveScan")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p = Process.Start(psi);
                        p?.WaitForExit(2000);
                    }
                    catch (Exception __ex) { CcLog.Ignore(__ex); }
                });
            }
            catch (Exception __ex) { CcLog.Ignore(__ex); }

            await LoadWindowsUpdatePageAsync();
            ShowInfoBar("بروزرسانی ویندوز", "وضعیت و لیست بروزرسانی‌های سیستم به‌روزرسانی شد.", InfoBarSeverity.Informational);
        }

        private void PauseUpdatesBtn_Click(object sender, RoutedEventArgs e)
        {
            bool ok = WindowsUpdateService.PauseUpdates(5);
            if (ok)
            {
                RefreshWindowsUpdateStatus();
                ShowInfoBar("توقف آپدیت ها", "دریافت آپدیت های سیستم عامل به مدت ۵ هفته متوقف شد.", InfoBarSeverity.Success);
            }
            else
            {
                ShowInfoBar("خطا", "تغییر تنظیمات توقف نیازمند دسترسی Administrator است.", InfoBarSeverity.Warning);
            }
        }

        private void ResumeUpdatesBtn_Click(object sender, RoutedEventArgs e)
        {
            bool ok = WindowsUpdateService.ResumeUpdates();
            if (ok)
            {
                RefreshWindowsUpdateStatus();
                ShowInfoBar("ادامه آپدیت ها", "توقف برداشته شد و روند عادی آپدیت ها از سر گرفته شد.", InfoBarSeverity.Success);
            }
            else
            {
                ShowInfoBar("خطا", "امکان لغو توقف آپدیت ها میسر نشد.", InfoBarSeverity.Warning);
            }
        }

        private void DisableUpdateServiceBtn_Click(object sender, RoutedEventArgs e)
        {
            bool ok = WindowsUpdateService.SetUpdateServiceState(false);
            if (ok)
            {
                RefreshWindowsUpdateStatus();
                ShowInfoBar("سرویس آپدیت", "سرویس Windows Update (wuauserv) با موفقیت متوقف و غیرفعال (Disabled) شد.", InfoBarSeverity.Success);
            }
            else
            {
                ShowInfoBar("خطا", "تغییر سرویس نیازمند دسترسی Administrator است.", InfoBarSeverity.Warning);
            }
        }

        private void EnableUpdateServiceBtn_Click(object sender, RoutedEventArgs e)
        {
            bool ok = WindowsUpdateService.SetUpdateServiceState(true);
            if (ok)
            {
                RefreshWindowsUpdateStatus();
                ShowInfoBar("سرویس آپدیت", "سرویس Windows Update به حالت خودکار (Automatic) بازگردانی و شروع به کار کرد.", InfoBarSeverity.Success);
            }
            else
            {
                ShowInfoBar("خطا", "تغییر سرویس نیازمند دسترسی Administrator است.", InfoBarSeverity.Warning);
            }
        }

        private void FixStuckUpdatesBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(FixStuckUpdatesBtn_ClickAsync(sender, e));

        private async Task FixStuckUpdatesBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            FixStuckUpdatesBtn.IsEnabled = false;
            try
            {
                var (success, freed, message) = await WindowsUpdateService.FixStuckUpdatesAndClearCacheAsync();
                RefreshWindowsUpdateStatus();
                if (success)
                {
                    ShowInfoBar("رفع مشکل آپدیت", message, InfoBarSeverity.Success);
                    _ = LoadWindowsUpdatePageAsync();
                }
                else
                {
                    ShowInfoBar("خطا در رفع مشکل", message, InfoBarSeverity.Warning);
                }
            }
            finally
            {
                FixStuckUpdatesBtn.IsEnabled = true;
            }
        }

        // ====================================================================
        // Tool 7: Driver Manager
        // ====================================================================

        private List<DriverItem>? _cachedDrivers;
        private DriverSummaryStats? _cachedDriverStats;

        private async Task LoadDriversPageAsync(bool forceRefresh = false)
        {
            if (_cachedDrivers != null && !forceRefresh)
            {
                RenderDrivers(_cachedDrivers);
                return;
            }

            DriversLoadingSpinner.Visibility = Visibility.Visible;
            DriversCardsContainer.Visibility = Visibility.Collapsed;

            try
            {
                var (drivers, stats) = await DriverManagerService.GetDriversAsync();
                _cachedDrivers = drivers;
                _cachedDriverStats = stats;

                DriversTotalCountText.Text = $"{stats.TotalDrivers} بسته";
                DriversErrorCountText.Text = $"{stats.ErrorCount} خطا";
                DriversDuplicateCountText.Text = $"{stats.DuplicateOldCount} بسته";

                // D14 — بنر خلاصه هم تکراری/قدیمی ها را پوشش می دهد هم بسته های بدون سخت افزار
                bool hasDuplicates = stats.DuplicateOldCount > 0;
                bool hasOrphans = stats.OrphanNoHardwareCount > 0;
                DriversDuplicateBanner.Visibility = (hasDuplicates || hasOrphans) ? Visibility.Visible : Visibility.Collapsed;
                if (hasDuplicates && hasOrphans)
                {
                    DriversDuplicateBannerTitle.Text = string.Format(
                        LocalizationService.Get("DriverBannerDupAndOrphan", "شناسایی {0} بسته تکراری و قدیمی + {1} بسته بدون سخت افزار در DriverStore ویندوز"),
                        stats.DuplicateOldCount, stats.OrphanNoHardwareCount);
                }
                else if (hasOrphans)
                {
                    DriversDuplicateBannerTitle.Text = string.Format(
                        LocalizationService.Get("DriverBannerOrphan", "شناسایی {0} بسته بدون سخت افزار (بازمانده ی دستگاه های جدا شده) در DriverStore ویندوز"),
                        stats.OrphanNoHardwareCount);
                }
                else if (hasDuplicates)
                {
                    DriversDuplicateBannerTitle.Text = string.Format(
                        LocalizationService.Get("DriverBannerDup", "شناسایی {0} بسته تکراری و قدیمی در DriverStore ویندوز"),
                        stats.DuplicateOldCount);
                }

                RenderDrivers(drivers);
            }
            catch (Exception ex)
            {
                DriversLoadingSpinner.Visibility = Visibility.Collapsed;
                DriversCardsContainer.Visibility = Visibility.Visible;
                DriversCardsContainer.Children.Clear();
                DriversCardsContainer.Children.Add(new TextBlock
                {
                    Text = $"خطا در واکشی اطلاعات درایورها: {ex.Message}",
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red)
                });
            }
        }

        private void RenderDrivers(List<DriverItem> drivers)
        {
            DriversLoadingSpinner.Visibility = Visibility.Collapsed;
            DriversCardsContainer.Visibility = Visibility.Visible;
            DriversCardsContainer.Children.Clear();

            string q = DriversSearchBox?.Text?.Trim().ToLowerInvariant() ?? "";
            var filtered = drivers;
            if (!string.IsNullOrEmpty(q))
            {
                filtered = drivers.Where(d => 
                    d.PublishedName.ToLowerInvariant().Contains(q) ||
                    d.OriginalName.ToLowerInvariant().Contains(q) ||
                    d.ProviderName.ToLowerInvariant().Contains(q) ||
                    d.ClassName.ToLowerInvariant().Contains(q) ||
                    d.DriverVersion.ToLowerInvariant().Contains(q)
                ).ToList();
            }

            if (filtered.Count == 0)
            {
                DriversCardsContainer.Children.Add(new TextBlock
                {
                    Text = "هیچ درایوری با مشخصات جستجو شده یافت نشد.",
                    FontSize = 12,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 16, 0, 0)
                });
                return;
            }

            foreach (var d in filtered.Take(120))
            {
                var card = new Border
                {
                    Background = GetThemeBrush("AppCardBackgroundBrush"),
                    BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // CheckBox
                var chk = new CheckBox
                {
                    IsChecked = d.IsSelected,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                var capturedD = d;
                chk.Click += (s, e) =>
                {
                    capturedD.IsSelected = chk.IsChecked == true;
                    UpdateDriversBatchBar();
                };
                Grid.SetColumn(chk, 0);
                grid.Children.Add(chk);

                // Icon (34x34 chip)
                var iconBorder = new Border
                {
                    Width = 34,
                    Height = 34,
                    CornerRadius = new CornerRadius(8),
                    Background = GetThemeBrush("AppCardSecondaryBrush"),
                    BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                iconBorder.Child = new FontIcon
                {
                    Glyph = d.HasError ? "\uE783" : "\uE772",
                    FontSize = 15,
                    Foreground = d.HasError 
                        ? GetThemeBrush("SystemFillColorCriticalBrush") 
                        : (d.IsDuplicateOldVersion ? GetThemeBrush("SystemFillColorCautionBrush") : GetThemeBrush("AccentFillColorDefaultBrush")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(iconBorder, 1);
                grid.Children.Add(iconBorder);

                // Driver Info
                var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                titleRow.Children.Add(new TextBlock
                {
                    Text = !string.IsNullOrEmpty(d.OriginalName) ? d.OriginalName : d.PublishedName,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 12.5
                });
                titleRow.Children.Add(new TextBlock
                {
                    Text = $"({d.PublishedName})",
                    FontSize = 11,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                textStack.Children.Add(titleRow);

                string sub = $"{d.ProviderName}  •  {d.ClassName}  •  {d.DriverVersion}";
                if (!string.IsNullOrEmpty(d.DriverDate)) sub += $" ({d.DriverDate})";
                textStack.Children.Add(new TextBlock
                {
                    Text = sub,
                    FontSize = 11,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FlowDirection = FlowDirection.LeftToRight
                });

                // شاهد D14 — «چرا» این بسته بدون سخت افزار تشخیص داده شد
                if (d.IsOrphanNoHardware && !string.IsNullOrEmpty(d.Evidence))
                {
                    textStack.Children.Add(new TextBlock
                    {
                        Text = d.Evidence,
                        FontSize = 10.5,
                        Foreground = GetThemeBrush("SystemFillColorCautionBrush"),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }

                Grid.SetColumn(textStack, 2);
                grid.Children.Add(textStack);

                // Status Badge & Action
                var rightStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

                // D14 — نشان بسته ی بدون سخت افزار (یتیم) کنار وضعیت سلامت
                if (d.IsOrphanNoHardware)
                {
                    var orphanBadge = new Border
                    {
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(8, 2, 8, 2),
                        Background = GetThemeBrush("AppCardSecondaryBrush")
                    };
                    orphanBadge.Child = new TextBlock
                    {
                        Text = LocalizationService.Get("BadgeNoHardware", "بدون سخت افزار"),
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = GetThemeBrush("SystemFillColorCautionBrush")
                    };
                    rightStack.Children.Add(orphanBadge);
                }
                
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8, 2, 8, 2),
                    Background = GetThemeBrush("AppCardSecondaryBrush")
                };
                badge.Child = new TextBlock
                {
                    Text = d.StatusDescription,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = d.HasError 
                        ? GetThemeBrush("SystemFillColorCriticalBrush") 
                        : (d.IsDuplicateOldVersion ? GetThemeBrush("SystemFillColorCautionBrush") : GetThemeBrush("SystemFillColorSuccessBrush"))
                };
                rightStack.Children.Add(badge);

                if (d.IsDuplicateOldVersion || d.IsOrphanNoHardware)
                {
                    var delBtn = new Button
                    {
                        Content = "حذف بسته",
                        Height = 30,
                        FontSize = 11,
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10, 0, 10, 0),
                        Foreground = GetThemeBrush("SystemFillColorCriticalBrush")
                    };
                    delBtn.Click += async (s, e) =>
                    {
                        await DeleteSingleDriverAsync(d);
                    };
                    rightStack.Children.Add(delBtn);
                }

                Grid.SetColumn(rightStack, 3);
                grid.Children.Add(rightStack);

                card.Child = grid;
                DriversCardsContainer.Children.Add(card);
            }

            UpdateDriversBatchBar();
        }

        private void UpdateDriversBatchBar()
        {
            if (_cachedDrivers == null || DriversBatchBar == null) return;
            int selectedCount = _cachedDrivers.Count(d => d.IsSelected);
            if (selectedCount > 0)
            {
                DriversBatchBar.Visibility = Visibility.Visible;
                DriversSelectedCountText.Text = $"{selectedCount} درایور انتخاب شده است";
                DriversBatchDeleteBtnText.Text = $"حذف {selectedCount} درایور انتخاب شده";
            }
            else
            {
                DriversBatchBar.Visibility = Visibility.Collapsed;
            }
        }

        private void DriversSelectAll_Checked(object sender, RoutedEventArgs e)
        {
            if (_cachedDrivers == null) return;
            foreach (var d in _cachedDrivers) d.IsSelected = true;
            RenderDrivers(_cachedDrivers);
            UpdateDriversBatchBar();
        }

        private void DriversSelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_cachedDrivers == null) return;
            foreach (var d in _cachedDrivers) d.IsSelected = false;
            RenderDrivers(_cachedDrivers);
            UpdateDriversBatchBar();
        }

        private void DriversBatchDeleteBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(DriversBatchDeleteBtn_ClickAsync(sender, e));

        private async Task DriversBatchDeleteBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (_cachedDrivers == null) return;
            var selected = _cachedDrivers.Where(d => d.IsSelected).ToList();
            if (selected.Count == 0) return;

            var dialog = new ContentDialog
            {
                XamlRoot = this.Content.XamlRoot,
                Title = "حذف دسته جمعی درایورهای انتخاب شده",
                Content = $"آیا از حذف {selected.Count} بسته درایور انتخاب شده اطمینان دارید؟\nاین عملیات با بررسی ایمنی انجام خواهد شد.",
                PrimaryButtonText = "حذف درایورها",
                CloseButtonText = "انصراف",
                DefaultButton = ContentDialogButton.Primary
            };

            var res = await dialog.ShowAsync();
            if (res != ContentDialogResult.Primary) return;

            DriversBatchDeleteBtn.IsEnabled = false;
            int successCount = 0;
            int failCount = 0;

            foreach (var d in selected)
            {
                var (ok, _) = await DriverManagerService.DeleteDriverPackageAsync(d.PublishedName);
                if (ok) successCount++;
                else failCount++;
            }

            DriversBatchDeleteBtn.IsEnabled = true;
            ShowInfoBar("نتیجه حذف دسته ای درایورها", $"{successCount} بسته درایور حذف شد. ({failCount} بسته در استفاده فعال سیستم بودند)", InfoBarSeverity.Success);
            await LoadDriversPageAsync(forceRefresh: true);
        }

        private async Task DeleteSingleDriverAsync(DriverItem driver)
        {
            // متن تأیید بر اساس نوع تشخیص (تکراری/قدیمی در برابر بدون سخت افزار) تنظیم می شود
            bool isOrphanOnly = driver.IsOrphanNoHardware && !driver.IsDuplicateOldVersion;
            string dialogTitle = isOrphanOnly
                ? string.Format(LocalizationService.Get("DriverDeleteOrphanTitle", "حذف بسته بدون سخت افزار {0}"), driver.PublishedName)
                : string.Format(LocalizationService.Get("DriverDeleteOldTitle", "حذف درایور قدیمی {0}"), driver.PublishedName);
            string dialogContent = isOrphanOnly
                ? string.Format(LocalizationService.Get("DriverDeleteOrphanBody", "بسته '{0}' متعلق به سخت افزاری است که دیگر به سیستم متصل نیست.\nآیا از حذف این بسته اطمینان دارید؟\nاین عملیات بدون سوئیچ اجباری انجام شده و در صورت استفاده سیستم متوقف خواهد شد."), driver.PublishedName)
                : string.Format(LocalizationService.Get("DriverDeleteOldBody", "آیا از حذف بسته درایور قدیمی '{0}' ({1}) اطمینان دارید؟\nاین عملیات بدون سوئیچ اجباری انجام شده و در صورت استفاده سیستم متوقف خواهد شد."), driver.OriginalName, driver.PublishedName);

            var dialog = new ContentDialog
            {
                XamlRoot = this.Content.XamlRoot,
                Title = dialogTitle,
                Content = dialogContent,
                PrimaryButtonText = "حذف ایمن",
                CloseButtonText = "انصراف",
                DefaultButton = ContentDialogButton.Primary
            };

            var res = await dialog.ShowAsync();
            if (res == ContentDialogResult.Primary)
            {
                var (success, msg) = await DriverManagerService.DeleteDriverPackageAsync(driver.PublishedName);
                if (success)
                {
                    ShowInfoBar("حذف درایور", msg, InfoBarSeverity.Success);
                    await LoadDriversPageAsync(forceRefresh: true);
                }
                else
                {
                    ShowInfoBar("عدم امکان حذف", msg, InfoBarSeverity.Warning);
                }
            }
        }

        private void CleanOldDriversBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(CleanOldDriversBtn_ClickAsync(sender, e));

        private async Task CleanOldDriversBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (_cachedDrivers == null) return;
            var selectedByUser = _cachedDrivers.Where(d => d.IsSelected).ToList();
            var targets = selectedByUser.Count > 0 ? selectedByUser : _cachedDrivers.Where(d => d.IsDuplicateOldVersion).ToList();
            if (targets.Count == 0)
            {
                ShowInfoBar("پاکسازی درایورها", "هیچ درایور قدیمی یا انتخاب‌شده‌ای برای حذف وجود ندارد.", InfoBarSeverity.Informational);
                return;
            }

            string dialogTitle = selectedByUser.Count > 0 ? "پاکسازی درایورهای انتخاب‌شده" : "پاکسازی درایورهای قدیمی DriverStore";
            string dialogContent = selectedByUser.Count > 0
                ? $"تعداد {targets.Count} بسته درایور انتخاب‌شده توسط شما شناسایی شده است.\n\nپیش از شروع، یک نقطه بازیابی سیستم (Restore Point) جهت امنیت کامل ساخته خواهد شد.\nآیا مایل به حذف بسته‌های انتخاب‌شده هستید؟"
                : $"تعداد {targets.Count} بسته درایور قدیمی و تکراری شناسایی شده است.\n\nپیش از شروع، یک نقطه بازیابی سیستم (Restore Point) جهت امنیت کامل ساخته خواهد شد.\nآیا مایل به شروع فرآیند پاکسازی هستید؟";

            var dialog = new ContentDialog
            {
                XamlRoot = this.Content.XamlRoot,
                Title = dialogTitle,
                Content = dialogContent,
                PrimaryButtonText = "شروع پاکسازی ایمن",
                CloseButtonText = "انصراف",
                DefaultButton = ContentDialogButton.Primary
            };

            var res = await dialog.ShowAsync();
            if (res != ContentDialogResult.Primary) return;

            CleanOldDriversBtn.IsEnabled = false;
            ShowInfoBar("ایجاد نقطه بازیابی", "در حال ایجاد Restore Point قبل از پاکسازی درایورها...", InfoBarSeverity.Informational);

            try
            {
                await RestorePointService.CreateRestorePointAsync("قبل از پاکسازی درایورهای DriverStore");
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            int deleted = 0;
            int skipped = 0;

            foreach (var d in targets)
            {
                var (success, _) = await DriverManagerService.DeleteDriverPackageAsync(d.PublishedName);
                if (success) deleted++;
                else skipped++;
            }

            CleanOldDriversBtn.IsEnabled = true;
            ShowInfoBar("نتیجه پاکسازی درایورها", $"پاکسازی تکمیل شد: {deleted} بسته با موفقیت حذف شد. ({skipped} بسته در حال استفاده فعال سیستم بودند و حفظ شدند)", InfoBarSeverity.Success);
            await LoadDriversPageAsync(forceRefresh: true);
        }

        private void CheckDriverUpdatesBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(CheckDriverUpdatesBtn_ClickAsync(sender, e));

        private async Task CheckDriverUpdatesBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            CheckDriverUpdatesBtn.IsEnabled = false;
            ShowInfoBar("بررسی درایورها", "در حال جستجوی آپدیت های درایور از سرور مایکروسافت...", InfoBarSeverity.Informational);

            var updates = await DriverManagerService.CheckDriverUpdatesAsync();
            CheckDriverUpdatesBtn.IsEnabled = true;

            if (updates.Count > 0)
            {
                string msg = string.Join("\n", updates.Select(u => $"• {u}"));
                var dialog = new ContentDialog
                {
                    XamlRoot = this.Content.XamlRoot,
                    Title = $"تعداد {updates.Count} بروزرسانی درایور یافت شد",
                    Content = new ScrollViewer
                    {
                        MaxHeight = 280,
                        Content = new TextBlock
                        {
                            Text = $"{msg}\n\nجهت دریافت و نصب، به بخش 'آپدیت ویندوز' مراجعه فرمایید.",
                            FontSize = 12,
                            LineHeight = 20
                        }
                    },
                    CloseButtonText = "متوجه شدم"
                };
                await dialog.ShowAsync();
            }
            else
            {
                ShowInfoBar("بروزرسانی درایورها", "تمامی درایورهای سخت افزاری شما بروز و آپدیت هستند.", InfoBarSeverity.Success);
            }
        }

        private void RefreshDrivers_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(RefreshDrivers_ClickAsync(sender, e));

        private async Task RefreshDrivers_ClickAsync(object sender, RoutedEventArgs e)
        {
            await LoadDriversPageAsync(forceRefresh: true);
            ShowInfoBar("بروزرسانی درایورها", "لیست درایورها و بسته های سخت افزاری بروزرسانی شد.", InfoBarSeverity.Informational);
        }

        private void DriversSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            _driversSearchDebounce.Stop();
            _driversSearchDebounce.Start();
        }

        // ====================================================================
        // Tool 8: Smart DNS & Anti-Sanction Switcher
        // ====================================================================

        private void LoadDnsPage()
        {
            if (DnsPresetsContainer == null) return;
            DnsPresetsContainer.Children.Clear();

            var currentDns = DnsManagerService.GetCurrentDns();
            if (ActiveDnsNameText != null)
            {
                ActiveDnsNameText.Text = currentDns.matchedPresetName;
            }
            if (ActiveDnsIpText != null)
            {
                ActiveDnsIpText.Text = (currentDns.primary == "خودکار") ? "(دریافت خودکار از مودم / DHCP)" : $"{currentDns.primary}, {currentDns.secondary}";
            }

            var presets = DnsManagerService.Presets;
            foreach (var preset in presets)
            {
                bool isActive = (currentDns.primary == preset.PrimaryDns);

                var card = new Border
                {
                    Background = GetThemeBrush("AppCardBackgroundBrush"),
                    BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 12, 14, 12),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Icon (34x34 chip)
                var iconBorder = new Border
                {
                    Width = 34,
                    Height = 34,
                    CornerRadius = new CornerRadius(8),
                    Background = GetThemeBrush("AppCardSecondaryBrush"),
                    BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                iconBorder.Child = new FontIcon
                {
                    Glyph = "\uE774",
                    FontSize = 15,
                    Foreground = GetThemeBrush("AccentFillColorDefaultBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(iconBorder, 0);
                grid.Children.Add(iconBorder);

                var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                titleRow.Children.Add(new TextBlock
                {
                    Text = preset.Name,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 12.5
                });
                if (preset.Category.Contains("تحریم"))
                {
                    var badge = new Border
                    {
                        Background = GetThemeBrush("AppAccentTintBrush"),
                        BorderBrush = GetThemeBrush("AppAccentBorderBrush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(8, 2, 8, 2)
                    };
                    badge.Child = new TextBlock { Text = "تحریم شکن تخصصی", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = GetThemeBrush("AccentFillColorDefaultBrush") };
                    titleRow.Children.Add(badge);
                }
                textStack.Children.Add(titleRow);

                string ipInfo = !string.IsNullOrEmpty(preset.PrimaryDns) ? $"{preset.PrimaryDns} - {preset.SecondaryDns}  •  " : "";
                textStack.Children.Add(new TextBlock
                {
                    Text = $"{ipInfo}{preset.Description}",
                    FontSize = 11.5,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
                    FlowDirection = FlowDirection.LeftToRight
                });
                Grid.SetColumn(textStack, 1);
                grid.Children.Add(textStack);

                if (isActive)
                {
                    var activeBadge = new Border
                    {
                        Background = GetThemeBrush("AppCardSecondaryBrush"),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(10, 4, 10, 4),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    activeBadge.Child = new TextBlock
                    {
                        Text = "فعال",
                        FontSize = 10.5,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = GetThemeBrush("SystemFillColorSuccessBrush")
                    };
                    Grid.SetColumn(activeBadge, 2);
                    grid.Children.Add(activeBadge);
                }
                else
                {
                    var applyBtn = new Button
                    {
                        Content = "اعمال",
                        Height = 32,
                        FontSize = 11.5,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(14, 0, 14, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var captured = preset;
                    applyBtn.Click += async (s, e) =>
                    {
                        applyBtn.IsEnabled = false;
                        var (ok, msg) = await DnsManagerService.SetDnsAsync(captured.PrimaryDns, captured.SecondaryDns);
                        applyBtn.IsEnabled = true;
                        if (ok)
                        {
                            ShowInfoBar("تغییر DNS", msg, InfoBarSeverity.Success);
                            LoadDnsPage();
                        }
                        else
                        {
                            ShowInfoBar("خطا در تنظیم DNS", msg, InfoBarSeverity.Error);
                        }
                    };
                    Grid.SetColumn(applyBtn, 2);
                    grid.Children.Add(applyBtn);
                }

                card.Child = grid;
                DnsPresetsContainer.Children.Add(card);
            }
        }

        private void ResetDnsDhcpBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(ResetDnsDhcpBtn_ClickAsync(sender, e));

        private async Task ResetDnsDhcpBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            ResetDnsDhcpBtn.IsEnabled = false;
            var (ok, msg) = await DnsManagerService.ResetDnsToDhcpAsync();
            ResetDnsDhcpBtn.IsEnabled = true;
            if (ok)
            {
                ShowInfoBar("بازنشانی DNS", msg, InfoBarSeverity.Success);
                LoadDnsPage();
            }
            else
            {
                ShowInfoBar("خطا در ریست DNS", msg, InfoBarSeverity.Warning);
            }
        }

        private void FlushDnsQuickBtn_Click(object sender, RoutedEventArgs e)
        {
            DnsManagerService.FlushDns();
            ShowInfoBar("تخلیه کش DNS", "کش DNS ویندوز با موفقیت تخلیه شد.", InfoBarSeverity.Success);
        }

        // ====================================================================
        // Tool 9: Windows 11 Privacy & Debloater (Auto-Applying Live Toggles)
        // ====================================================================

        private void LoadPrivacyPage()
        {
            if (PrivacyCardsContainer == null) return;
            PrivacyCardsContainer.Children.Clear();

            // 1. DiagTrack / Connected User Experiences
            bool isDiagDisabled = TelemetryOptimizerService.IsDiagTrackDisabled();
            PrivacyCardsContainer.Children.Add(CreateToggleCard(
                "\uE895",
                "قطع کامل سرویس تلمتری و گزارش DiagTrack",
                "غیرفعال سازی سرویس Connected User Experiences و توقف ارسال بسته های تشخیصی و لاگ به سرورهای مایکروسافت",
                isDiagDisabled,
                default,
                (isOn) =>
                {
                    TelemetryOptimizerService.SetDiagTrackDisabled(isOn);
                    ShowInfoBar("تلمتری DiagTrack", isOn ? "سرویس تلمتری قطع و غیرفعال شد." : "سرویس تلمتری به حالت پیش فرض ویندوز بازگشت.", InfoBarSeverity.Success);
                }));

            // 2. Bing Search in Start Menu
            bool isBingDisabled = TelemetryOptimizerService.IsBingSearchDisabled();
            PrivacyCardsContainer.Children.Add(CreateToggleCard(
                "\uE721",
                "حذف جستجوی وب بینگ از استارت منو (Bing Search)",
                "جلوگیری از ارسال متن های تایپ شده شما در منوی استارت به وب و افزایش چشمگیر سرعت باز شدن و جستجوی فایل ها",
                isBingDisabled,
                default,
                (isOn) =>
                {
                    TelemetryOptimizerService.SetBingSearchDisabled(isOn);
                    ShowInfoBar("جستجوی استارت منو", isOn ? "سرچ وب بینگ غیرفعال شد (جستجوی محلی فوق سریع)." : "جستجوی وب در استارت فعال شد.", InfoBarSeverity.Success);
                }));

            // 3. Advertising ID
            bool isAdvDisabled = TelemetryOptimizerService.IsAdvertisingIdDisabled();
            PrivacyCardsContainer.Children.Add(CreateToggleCard(
                "\uE719",
                "غیرفعال سازی شناسه تبلیغاتی ویندوز (Advertising ID)",
                "جلوگیری از ساخت و ردگیری پروفایل کاربری تبلیغاتی توسط برنامه ها و مایکروسافت استور",
                isAdvDisabled,
                default,
                (isOn) =>
                {
                    TelemetryOptimizerService.SetAdvertisingIdDisabled(isOn);
                    ShowInfoBar("شناسه تبلیغاتی", isOn ? "شناسه تبلیغاتی ویندوز مسدود گردید." : "شناسه تبلیغاتی فعال شد.", InfoBarSeverity.Success);
                }));

            // 4. Activity History
            bool isActDisabled = TelemetryOptimizerService.IsActivityHistoryDisabled();
            PrivacyCardsContainer.Children.Add(CreateToggleCard(
                "\uE81C",
                "توقف ثبت تاریخچه فعالیت ها و فایل ها (Activity History)",
                "توقف ذخیره و همگام سازی سوابق باز کردن برنامه ها، اسناد و مرور وب توسط ویندوز",
                isActDisabled,
                default,
                (isOn) =>
                {
                    TelemetryOptimizerService.SetActivityHistoryDisabled(isOn);
                    ShowInfoBar("تاریخچه فعالیت ها", isOn ? "ثبت فعالیت ها متوقف و غیرفعال شد." : "ثبت سوابق فعالیت ها فعال شد.", InfoBarSeverity.Success);
                }));

            // 5. Tailored Experiences & Feedback Tracking
            bool isFeedDisabled = TelemetryOptimizerService.IsFeedbackTrackingDisabled();
            PrivacyCardsContainer.Children.Add(CreateToggleCard(
                "\uE9E9",
                "قطع نظرسنجی ها و تجربیات شخصی سازی مایکروسافت",
                "جلوگیری از نمایش پاپ آپ های ارزیابی و توقف پایش نحوه استفاده از ابزارهای ویندوز",
                isFeedDisabled,
                default,
                (isOn) =>
                {
                    TelemetryOptimizerService.SetFeedbackTrackingDisabled(isOn);
                    ShowInfoBar("بازخورد مایکروسافت", isOn ? "نظرسنجی ها و پایش تجربیات قطع شدند." : "پایش تجربیات فعال شد.", InfoBarSeverity.Success);
                }));
        }

        private void EnableAllPrivacyProtectionsBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(EnableAllPrivacyProtectionsBtn_ClickAsync(sender, e));

        private async Task EnableAllPrivacyProtectionsBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            int count = await TelemetryOptimizerService.DisableTelemetryAndDiagTrackAsync();
            LoadPrivacyPage();
            ShowInfoBar("حریم خصوصی ویندوز ۱۱", $"تمامی {count} گزینه محافظتی با موفقیت فعال و در ویندوز ذخیره شدند.", InfoBarSeverity.Success);
        }

        private void ResetPrivacyDefaultsBtn_Click(object sender, RoutedEventArgs e)
            => SafeInvoker.Run(ResetPrivacyDefaultsBtn_ClickAsync(sender, e));

        private async Task ResetPrivacyDefaultsBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            int count = await TelemetryOptimizerService.RestoreDefaultPrivacySettingsAsync();
            LoadPrivacyPage();
            ShowInfoBar("حریم خصوصی ویندوز ۱۱", "تنظیمات به حالت استاندارد و پیش فرض اولیه ویندوز بازگردانی شدند.", InfoBarSeverity.Informational);
        }
    }
}
