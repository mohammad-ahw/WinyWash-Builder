// ==============================================================================
// CleanC - WinUI 3 Dynamic UI Card Templates & Visual Builders
// NOTE FOR CLAUDE: This file contains ONLY presentation and visual layout logic.
// All backend logic, registry access, and system calls have been removed.
// You can freely redesign the layout, colors, spacing, borders, typography, and controls.
// ==============================================================================

using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CleanC.UI.PresentationTemplates
{
    #region UI Data Models (Properties displayed on cards)

    public class UpdateCardModel
    {
        public string Title { get; set; } = "Windows 11, version 26H2";
        public string KbArticleId { get; set; } = "نسل بعد (NG:25E275)";
        public string FormattedSize { get; set; } = "حجم متغیر / آنلاین";
        public string Description { get; set; } = "ارتقای رسمی به نسخه نسل جدید ویندوز ۱۱";
        public bool IsOptional { get; set; } = true;
        public bool IsNextGenFeatureUpdate { get; set; } = true;
        public string CategoryName { get; set; } = "ارتقای نسل سیستم‌عامل (Next-Gen OS Upgrade)";
    }

    public class AppCardModel
    {
        public string DisplayName { get; set; } = "Google Chrome";
        public string Publisher { get; set; } = "Google LLC";
        public string DisplayVersion { get; set; } = "131.0.6778.86";
        public string InstalledDateFormatted { get; set; } = "2024/11/20";
        public string FormattedSize { get; set; } = "450 MB";
        public bool IsSelected { get; set; }
    }

    public class DriverCardModel
    {
        public string PublishedName { get; set; } = "oem42.inf";
        public string OriginalName { get; set; } = "nv_dispi.inf";
        public string ProviderName { get; set; } = "NVIDIA Corporation";
        public string ClassName { get; set; } = "Display";
        public string DriverVersion { get; set; } = "32.0.15.6094";
        public string DriverDate { get; set; } = "2024/08/20";
        public bool HasError { get; set; }
        public bool IsDuplicateOldVersion { get; set; }
        public bool IsSelected { get; set; }
    }

    public class StartupCardModel
    {
        public string Name { get; set; } = "Spotify";
        public string Command { get; set; } = @"C:\Users\User\AppData\Roaming\Spotify\Spotify.exe --autostart";
        public string Category { get; set; } = "برنامه عمومی کاربر";
        public string ImpactLevel { get; set; } = "متوسط";
        public bool IsDisabled { get; set; }
    }

    public class DnsPresetModel
    {
        public string Name { get; set; } = "الکترو (Electro DNS)";
        public string Category { get; set; } = "تحریم‌شکن";
        public string Description { get; set; } = "رفع سریع تحریم‌های توسعه، داکر، انویدیا، هوش مصنوعی و گیمینگ";
        public string PrimaryDns { get; set; } = "78.157.42.100";
        public string SecondaryDns { get; set; } = "78.157.42.101";
        public bool IsActive { get; set; }
    }

    public class LogicalDriveModel
    {
        public string Letter { get; set; } = "C";
        public string VolumeLabel { get; set; } = "Windows";
        public string FileSystem { get; set; } = "NTFS";
        public long TotalSizeBytes { get; set; } = 512_000_000_000;
        public long FreeSizeBytes { get; set; } = 120_000_000_000;
        public double UsedPercent => 76.5;
        public string TotalFormatted => "512 GB";
        public string FreeFormatted => "120 GB";
    }

    #endregion

    public class DynamicCardTemplates
    {
        // Helper to retrieve theme brushes from App.xaml
        private Brush GetThemeBrush(string brushKey) =>
            Application.Current.Resources.TryGetValue(brushKey, out var b) && b is Brush brush
                ? brush
                : new SolidColorBrush(Colors.Transparent);

        // ====================================================================
        // 1. Windows Update Card (Mandatory / Optional / Next-Gen Feature Upgrades)
        // ====================================================================
        public Border CreatePendingUpdateCard(UpdateCardModel item)
        {
            var border = new Border
            {
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = item.IsNextGenFeatureUpdate
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 139, 92, 246)) // Purple for Next-Gen
                    : (item.IsOptional
                        ? new SolidColorBrush(ColorHelper.FromArgb(255, 6, 182, 212)) // Cyan for Optional
                        : GetThemeBrush("AppCardBorderBrush")),
                BorderThickness = new Thickness(item.IsNextGenFeatureUpdate ? 1.5 : 1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 1. Icon Box
            var iconBox = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(10),
                Background = item.IsNextGenFeatureUpdate
                    ? new SolidColorBrush(ColorHelper.FromArgb(35, 139, 92, 246))
                    : (item.IsOptional ? new SolidColorBrush(ColorHelper.FromArgb(30, 6, 182, 212)) : new SolidColorBrush(ColorHelper.FromArgb(30, 2, 132, 199))),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var icon = new FontIcon
            {
                Glyph = item.IsNextGenFeatureUpdate ? "\uE7FB" : (item.IsOptional ? "\uE896" : "\uE895"),
                FontSize = 18,
                Foreground = item.IsNextGenFeatureUpdate
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 167, 139, 250))
                    : (item.IsOptional ? new SolidColorBrush(ColorHelper.FromArgb(255, 6, 182, 212)) : new SolidColorBrush(ColorHelper.FromArgb(255, 2, 132, 199))),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBox.Child = icon;
            Grid.SetColumn(iconBox, 0);
            grid.Children.Add(iconBox);

            // 2. Text Stack
            var stack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

            // Badge
            var badgeBorder = new Border
            {
                Background = item.IsNextGenFeatureUpdate
                    ? new SolidColorBrush(ColorHelper.FromArgb(40, 139, 92, 246))
                    : (item.IsOptional ? new SolidColorBrush(ColorHelper.FromArgb(35, 6, 182, 212)) : new SolidColorBrush(ColorHelper.FromArgb(30, 2, 132, 199))),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeBorder.Child = new TextBlock
            {
                Text = item.CategoryName,
                FontSize = 10.5,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = item.IsNextGenFeatureUpdate
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 192, 132, 252))
                    : (item.IsOptional ? new SolidColorBrush(ColorHelper.FromArgb(255, 6, 182, 212)) : new SolidColorBrush(ColorHelper.FromArgb(255, 2, 132, 199)))
            };
            titleStack.Children.Add(badgeBorder);

            if (!string.IsNullOrEmpty(item.KbArticleId))
            {
                var kbBadge = new Border
                {
                    Background = GetThemeBrush("AppHeaderPillBrush"),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                kbBadge.Child = new TextBlock
                {
                    Text = item.KbArticleId,
                    FontSize = 10.5,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush")
                };
                titleStack.Children.Add(kbBadge);
            }

            var titleText = new TextBlock
            {
                Text = item.Title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(titleText);
            stack.Children.Add(titleStack);

            if (!string.IsNullOrEmpty(item.Description))
            {
                var descText = new TextBlock
                {
                    Text = item.Description,
                    FontSize = 11,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                stack.Children.Add(descText);
            }

            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            // 3. Actions / Size Column
            var actionStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

            var openBtn = new Button
            {
                Content = item.IsNextGenFeatureUpdate ? "مشاهده و دریافت در ویندوز" : (item.IsOptional ? "مشاهده در آپدیت" : "تنظیمات آپدیت"),
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(6),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Background = item.IsNextGenFeatureUpdate ? new SolidColorBrush(ColorHelper.FromArgb(40, 139, 92, 246)) : GetThemeBrush("AppHeaderPillBrush"),
                Foreground = item.IsNextGenFeatureUpdate ? new SolidColorBrush(ColorHelper.FromArgb(255, 192, 132, 252)) : GetThemeBrush("TextFillColorPrimaryBrush"),
                BorderBrush = item.IsNextGenFeatureUpdate ? new SolidColorBrush(ColorHelper.FromArgb(80, 139, 92, 246)) : GetThemeBrush("AppCardBorderBrush")
            };
            actionStack.Children.Add(openBtn);

            Grid.SetColumn(actionStack, 2);
            grid.Children.Add(actionStack);

            border.Child = grid;
            return border;
        }

        // ====================================================================
        // 2. Dynamic Privacy & Personalization Toggle Card (Auto-Save on toggle)
        // ====================================================================
        public Border CreateToggleCard(string glyph, string title, string description, bool initialValue, Windows.UI.Color iconBgColor, Action<bool>? onToggle = null)
        {
            var border = new Border
            {
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Icon
            var iconBg = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(iconBgColor),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var icon = new FontIcon
            {
                Glyph = glyph,
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
            };
            iconBg.Child = icon;
            Grid.SetColumn(iconBg, 0);
            grid.Children.Add(iconBg);

            // Labels
            var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var tBlock = new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13 };
            var dBlock = new TextBlock { Text = description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = GetThemeBrush("TextFillColorSecondaryBrush") };
            textStack.Children.Add(tBlock);
            textStack.Children.Add(dBlock);
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            // ToggleSwitch
            var toggle = new ToggleSwitch
            {
                IsOn = initialValue,
                OnContent = "فعال",
                OffContent = "غیرفعال",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0)
            };
            toggle.Toggled += (s, e) => onToggle?.Invoke(toggle.IsOn);
            Grid.SetColumn(toggle, 2);
            grid.Children.Add(toggle);

            border.Child = grid;
            return border;
        }

        // ====================================================================
        // 3. App Uninstaller Row Card (Checkbox, Title, Publisher, Size, Action)
        // ====================================================================
        public Border CreateAppUninstallerCard(AppCardModel app)
        {
            var itemBorder = new Border
            {
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = app.IsSelected ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] : GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(app.IsSelected ? 1.5 : 1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Col 0: CheckBox for batch queue
            var chk = new CheckBox
            {
                IsChecked = app.IsSelected,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(chk, 0);
            grid.Children.Add(chk);

            // Col 1: App Info
            var infoStack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            var nameText = new TextBlock { Text = app.DisplayName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis };
            
            string pubStr = !string.IsNullOrEmpty(app.Publisher) ? $"{app.Publisher}  •  " : "";
            string verStr = !string.IsNullOrEmpty(app.DisplayVersion) ? $"نسخه {app.DisplayVersion}  •  " : "";
            string dateStr = !string.IsNullOrEmpty(app.InstalledDateFormatted) ? $"تاریخ نصب: {app.InstalledDateFormatted}" : "";
            var subText = new TextBlock { Text = $"{pubStr}{verStr}{dateStr}".TrimEnd(' ', '•'), FontSize = 11.5, Foreground = GetThemeBrush("TextFillColorSecondaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis };

            infoStack.Children.Add(nameText);
            infoStack.Children.Add(subText);
            Grid.SetColumn(infoStack, 1);
            grid.Children.Add(infoStack);

            // Col 2: Size Badge
            var sizeBadge = new Border
            {
                Background = GetThemeBrush("AppHeaderPillBrush"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0)
            };
            sizeBadge.Child = new TextBlock { Text = app.FormattedSize, FontSize = 11.5, Foreground = GetThemeBrush("TextFillColorSecondaryBrush") };
            Grid.SetColumn(sizeBadge, 2);
            grid.Children.Add(sizeBadge);

            // Col 3: Single Uninstall Button
            var uninstBtn = new Button
            {
                Content = "حذف نرم‌افزار",
                FontSize = 11.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                MinWidth = 105,
                Height = 34,
                CornerRadius = new CornerRadius(6),
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 239, 68, 68)),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 239, 68, 68)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(uninstBtn, 3);
            grid.Children.Add(uninstBtn);

            itemBorder.Child = grid;
            return itemBorder;
        }

        // ====================================================================
        // 4. Driver Manager Card (Checkbox, Icon, Name, Category, Version, Delete)
        // ====================================================================
        public Border CreateDriverCard(DriverCardModel d)
        {
            var card = new Border
            {
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = d.HasError 
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 239, 68, 68)) 
                    : (d.IsDuplicateOldVersion ? new SolidColorBrush(ColorHelper.FromArgb(255, 245, 158, 11)) : GetThemeBrush("AppCardBorderBrush")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10)
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
            Grid.SetColumn(chk, 0);
            grid.Children.Add(chk);

            // Icon
            var iconBorder = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(8),
                Background = d.HasError 
                    ? new SolidColorBrush(ColorHelper.FromArgb(40, 239, 68, 68)) 
                    : (d.IsDuplicateOldVersion ? new SolidColorBrush(ColorHelper.FromArgb(40, 245, 158, 11)) : new SolidColorBrush(ColorHelper.FromArgb(40, 13, 148, 136))),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBorder.Child = new FontIcon
            {
                Glyph = d.HasError ? "\uE783" : "\uE772",
                FontSize = 16,
                Foreground = d.HasError 
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 239, 68, 68)) 
                    : (d.IsDuplicateOldVersion ? new SolidColorBrush(ColorHelper.FromArgb(255, 245, 158, 11)) : new SolidColorBrush(ColorHelper.FromArgb(255, 13, 148, 136))),
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
            };
            textStack.Children.Add(titleRow);

            string sub = $"{d.ProviderName}  •  دسته: {d.ClassName}  •  نسخه: {d.DriverVersion}";
            if (!string.IsNullOrEmpty(d.DriverDate)) sub += $" ({d.DriverDate})";
            textStack.Children.Add(new TextBlock
            {
                Text = sub,
                FontSize = 11,
                Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(textStack, 2);
            grid.Children.Add(textStack);

            // Delete Action Button
            var delBtn = new Button
            {
                Content = "حذف درایور",
                FontSize = 11,
                Height = 32,
                CornerRadius = new CornerRadius(6),
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 239, 68, 68)),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 239, 68, 68)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            Grid.SetColumn(delBtn, 3);
            grid.Children.Add(delBtn);

            card.Child = grid;
            return card;
        }

        // ====================================================================
        // 5. Startup Item Card (Toggle, Category Badge, Path, Faded if Disabled)
        // ====================================================================
        public Border CreateStartupCard(StartupCardModel item)
        {
            var card = new Border
            {
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12),
                Opacity = item.IsDisabled ? 0.55 : 1.0 // Faded when disabled
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Icon
            var iconBorder = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(ColorHelper.FromArgb(30, 245, 158, 11)),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBorder.Child = new FontIcon
            {
                Glyph = "\uE7B5",
                FontSize = 17,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 245, 158, 11)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconBorder, 0);
            grid.Children.Add(iconBorder);

            // Text info
            var textStack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

            topRow.Children.Add(new TextBlock
            {
                Text = item.Name,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13.5
            });

            // Category Badge
            var catBadge = new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(25, 245, 158, 11)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            catBadge.Child = new TextBlock
            {
                Text = item.Category,
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 245, 158, 11))
            };
            topRow.Children.Add(catBadge);

            if (item.IsDisabled)
            {
                var disBadge = new Border
                {
                    Background = new SolidColorBrush(ColorHelper.FromArgb(30, 239, 68, 68)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                disBadge.Child = new TextBlock
                {
                    Text = "خاموش / غیرفعال",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 239, 68, 68))
                };
                topRow.Children.Add(disBadge);
            }

            textStack.Children.Add(topRow);
            textStack.Children.Add(new TextBlock
            {
                Text = $"مسیر: {item.Command}",
                FontSize = 11,
                Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            // Toggle
            var toggle = new ToggleSwitch
            {
                IsOn = !item.IsDisabled,
                OnContent = "فعال",
                OffContent = "غیرفعال",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(toggle, 2);
            grid.Children.Add(toggle);

            card.Child = grid;
            return card;
        }

        // ====================================================================
        // 6. DNS Preset Card (Name, Anti-Sanction Badge, IPs, Activation Button)
        // ====================================================================
        public Border CreateDnsPresetCard(DnsPresetModel preset)
        {
            var card = new Border
            {
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = preset.IsActive ? new SolidColorBrush(ColorHelper.FromArgb(255, 6, 182, 212)) : GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(preset.IsActive ? 1.5 : 1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Icon Box
            var iconBox = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(10),
                Background = preset.IsActive ? new SolidColorBrush(ColorHelper.FromArgb(40, 6, 182, 212)) : GetThemeBrush("AppHeaderPillBrush"),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBox.Child = new FontIcon
            {
                Glyph = preset.IsActive ? "\uE73E" : "\uE774",
                FontSize = 16,
                Foreground = preset.IsActive ? new SolidColorBrush(ColorHelper.FromArgb(255, 6, 182, 212)) : GetThemeBrush("TextFillColorPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconBox, 0);
            grid.Children.Add(iconBox);

            // Info Stack
            var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(new TextBlock
            {
                Text = preset.Name,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13
            });

            if (preset.Category.Contains("تحریم"))
            {
                var badge = new Border
                {
                    Background = new SolidColorBrush(ColorHelper.FromArgb(30, 16, 185, 129)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2)
                };
                badge.Child = new TextBlock { Text = "تحریم‌شکن تخصصی", FontSize = 10, Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 185, 129)) };
                titleRow.Children.Add(badge);
            }
            textStack.Children.Add(titleRow);

            string ipInfo = $"DNS: {preset.PrimaryDns}  |  {preset.SecondaryDns}  •  ";
            textStack.Children.Add(new TextBlock
            {
                Text = $"{ipInfo}{preset.Description}",
                FontSize = 11.5,
                Foreground = GetThemeBrush("TextFillColorSecondaryBrush")
            });
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            // Apply Button
            var applyBtn = new Button
            {
                Content = preset.IsActive ? "فعال در سیستم" : "فعال‌سازی این DNS",
                Height = 32,
                FontSize = 11.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = !preset.IsActive,
                Background = preset.IsActive ? new SolidColorBrush(ColorHelper.FromArgb(40, 6, 182, 212)) : GetThemeBrush("AppCardBackgroundBrush")
            };
            Grid.SetColumn(applyBtn, 2);
            grid.Children.Add(applyBtn);

            card.Child = grid;
            return card;
        }

        // ====================================================================
        // 7. System Specs Tile (Hardware category, value, feature chips)
        // ====================================================================
        public Border CreateSpecCard(string glyph, string category, string value, List<string>? chips, Windows.UI.Color iconBgColor, string extra = "")
        {
            var border = new Border
            {
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconBg = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(21),
                Background = new SolidColorBrush(iconBgColor),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            iconBg.Child = new FontIcon
            {
                Glyph = glyph,
                FontSize = 19,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
            };
            Grid.SetColumn(iconBg, 0);
            grid.Children.Add(iconBg);

            var textStack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock { Text = category, FontSize = 11.5, Foreground = GetThemeBrush("TextFillColorSecondaryBrush") });
            textStack.Children.Add(new TextBlock { Text = value, FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontSize = 14.5, Foreground = GetThemeBrush("TextFillColorPrimaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis });

            if (chips != null && chips.Count > 0)
            {
                var chipsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 3, 0, 0) };
                foreach (var chip in chips)
                {
                    if (string.IsNullOrWhiteSpace(chip)) continue;
                    var pill = new Border
                    {
                        Background = GetThemeBrush("AppHeaderPillBrush"),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(7, 2, 7, 2)
                    };
                    pill.Child = new TextBlock
                    {
                        Text = chip,
                        FontSize = 10.5,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = GetThemeBrush("TextFillColorSecondaryBrush")
                    };
                    chipsRow.Children.Add(pill);
                }
                textStack.Children.Add(chipsRow);
            }

            if (!string.IsNullOrEmpty(extra))
            {
                textStack.Children.Add(new TextBlock { Text = extra, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = GetThemeBrush("TextFillColorSecondaryBrush"), Margin = new Thickness(0, 2, 0, 0) });
            }

            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            border.Child = grid;
            return border;
        }

        // ====================================================================
        // 8. Logical Drive Tile (Drive letter, Capacity bar, Label & Letter edit)
        // ====================================================================
        public Border CreateDriveCard(LogicalDriveModel drive)
        {
            var driveCard = new Border
            {
                Background = GetThemeBrush("AppCardSecondaryBrush"),
                BorderBrush = GetThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var dGrid = new Grid();
            dGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Drive Icon
            var driveIconBg = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(ColorHelper.FromArgb(25, 2, 132, 199)),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            driveIconBg.Child = new FontIcon
            {
                Glyph = "\uEDA2",
                FontSize = 18,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 2, 132, 199)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(driveIconBg, 0);
            dGrid.Children.Add(driveIconBg);

            // Drive Info Stack
            var dInfo = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            var dTitleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            dTitleRow.Children.Add(new TextBlock
            {
                Text = $"{drive.VolumeLabel} ({drive.Letter}:)",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13.5
            });

            var fsBadge = new Border
            {
                Background = GetThemeBrush("AppHeaderPillBrush"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2)
            };
            fsBadge.Child = new TextBlock { Text = drive.FileSystem, FontSize = 10, Foreground = GetThemeBrush("TextFillColorSecondaryBrush") };
            dTitleRow.Children.Add(fsBadge);
            dInfo.Children.Add(dTitleRow);

            var dProgress = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = drive.UsedPercent,
                Height = 5,
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = drive.UsedPercent > 90 ? new SolidColorBrush(ColorHelper.FromArgb(255, 239, 68, 68)) : new SolidColorBrush(ColorHelper.FromArgb(255, 2, 132, 199))
            };
            dInfo.Children.Add(dProgress);

            dInfo.Children.Add(new TextBlock
            {
                Text = $"{drive.FreeFormatted} فضای خالی از مجموع {drive.TotalFormatted}",
                FontSize = 11,
                Foreground = GetThemeBrush("TextFillColorSecondaryBrush")
            });
            Grid.SetColumn(dInfo, 1);
            dGrid.Children.Add(dInfo);

            // Action Buttons
            var dActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            dActions.Children.Add(new Button { Content = "تغییر نام", Height = 30, FontSize = 11, CornerRadius = new CornerRadius(6) });
            dActions.Children.Add(new Button { Content = "تغییر حرف", Height = 30, FontSize = 11, CornerRadius = new CornerRadius(6) });
            Grid.SetColumn(dActions, 2);
            dGrid.Children.Add(dActions);

            driveCard.Child = dGrid;
            return driveCard;
        }
    }
}
