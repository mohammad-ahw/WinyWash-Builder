// src/CleanC.UI/MainWindow.Quarantine.cs
// F3 — بخش «فایل‌های قرنطینه‌شده» داخل مرکز بازیابی (Undo Center).
// منطق واقعی فایل‌سیستم در CleanC.Core/Services/SafeDeleteService.cs است؛
// این فایل فقط لایه‌ی نمایش و کنترل رویدادهاست.

using System;
using System.IO;
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
        /// <summary>
        /// کارت فایل‌های قرنطینه‌شده برای مرکز بازیابی. برای هر نشست یک ردیف با
        /// بازگردانی و حذف قطعی (تأیید دو مرحله‌ای) ساخته می‌شود.
        /// </summary>
        private UIElement BuildQuarantineSection()
        {
            var content = new StackPanel { Spacing = 8 };

            // بازسازی کل بدنه بعد از هر عملیات، تا ردیف‌های بازگردانده‌شده/حذف‌شده
            // از فهرست خارج شوند و UI با وضعیت واقعی همگام بماند.
            void Rebuild()
            {
                content.Children.Clear();

                var sessions = SafeDeleteService.ListQuarantineSessions();
                if (sessions.Count == 0)
                {
                    content.Children.Add(new TextBlock
                    {
                        Text = "قرنطینه خالی است؛ چیزی برای بازگردانی وجود ندارد.",
                        FontSize = 11.5,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
                    });
                    return;
                }

                foreach (var session in sessions)
                    content.Children.Add(BuildQuarantineRow(session, Rebuild));
            }

            Rebuild();

            var header = new StackPanel { Spacing = 4 };
            header.Children.Add(new TextBlock
            {
                Text = "فایل‌های قرنطینه‌شده CleanC",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
            });
            header.Children.Add(new TextBlock
            {
                Text = "حذف‌هایی که در حالت «قرنطینه» انجام شده‌اند؛ تا زمانی که خودتان حذف قطعی نکنید، قابل بازگردانی هستند.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });

            var stack = new StackPanel { Spacing = 10 };
            stack.Children.Add(header);
            stack.Children.Add(content);

            return new Border
            {
                Background = (Brush)Application.Current.Resources["AppCardSecondaryBrush"],
                BorderBrush = (Brush)Application.Current.Resources["AppCardBorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Child = stack
            };
        }

        /// <summary>ردیف یک نشست قرنطینه: خلاصه + دکمه‌ی بازگردانی + دکمه‌ی حذف (تأیید دومرحله‌ای).</summary>
        private UIElement BuildQuarantineRow(QuarantineManifest session, Action rebuild)
        {
            long totalBytes = session.Entries.Sum(e => e.SizeBytes);
            string firstPath = session.Entries.FirstOrDefault()?.OriginalPath ?? string.Empty;
            string folder = string.IsNullOrWhiteSpace(firstPath)
                ? "مسیر نامشخص"
                : Path.GetDirectoryName(firstPath) ?? "مسیر نامشخص";

            var summary = new StackPanel { Spacing = 2 };
            summary.Children.Add(new TextBlock
            {
                Text = $"{session.Entries.Count} مورد · {SystemStats.FormatBytes(totalBytes)} · {session.CreatedAt:yyyy/MM/dd HH:mm}",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
            });
            summary.Children.Add(new TextBlock
            {
                Text = $"از: {folder}",
                FontSize = 10.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            });

            var restoreBtn = new Button
            {
                Content = "بازگردانی همه",
                MinWidth = 96,
                Padding = new Thickness(10, 4, 10, 4)
            };
            AutomationProperties.SetName(restoreBtn, $"بازگردانی نشست {session.SessionId}");

            var deleteBtn = new Button
            {
                Content = "حذف قطعی",
                MinWidth = 96,
                Padding = new Thickness(10, 4, 10, 4)
            };
            AutomationProperties.SetName(deleteBtn, $"حذف قطعی نشست {session.SessionId}");

            restoreBtn.Click += (_, _) =>
                SafeInvoker.Run(RestoreQuarantineSessionAsync(session, restoreBtn, deleteBtn, rebuild));

            // تأیید دو مرحله‌ای: اولین کلیک فقط هشدار می‌دهد، کلیک دوم اجرا می‌کند.
            bool armed = false;
            deleteBtn.Click += (_, _) =>
            {
                if (!armed)
                {
                    armed = true;
                    deleteBtn.Content = "حذف شود؟";
                    return;
                }

                SafeInvoker.Run(DeleteQuarantineSessionAsync(session, restoreBtn, deleteBtn, rebuild));
            };

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            actions.Children.Add(restoreBtn);
            actions.Children.Add(deleteBtn);

            var grid = new Grid { ColumnSpacing = 16 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(summary, 0);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(summary);
            grid.Children.Add(actions);

            return new Border
            {
                Background = (Brush)Application.Current.Resources["AppCardBackgroundBrush"],
                BorderBrush = (Brush)Application.Current.Resources["AppCardBorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Child = grid
            };
        }

        /// <summary>بازگردانی همه‌ی ورودی‌های یک نشست قرنطینه به مسیر اصلی‌شان.</summary>
        private async Task RestoreQuarantineSessionAsync(
            QuarantineManifest session, Button restoreBtn, Button deleteBtn, Action rebuild)
        {
            try
            {
                restoreBtn.IsEnabled = false;
                deleteBtn.IsEnabled = false;
                restoreBtn.Content = "در حال بازگردانی...";

                var (restored, failed, error) = await Task.Run(() =>
                    SafeDeleteService.RestoreSession(session.SessionId));

                if (failed == 0 && restored > 0)
                {
                    ShowInfoBar(
                        "قرنطینه",
                        $"{restored} مورد به مسیر اصلی خود بازگردانده شد.",
                        InfoBarSeverity.Success);
                }
                else
                {
                    string detail = string.IsNullOrWhiteSpace(error) ? string.Empty : $" ({error})";
                    ShowInfoBar(
                        "قرنطینه",
                        $"{restored} مورد بازگردانده شد؛ {failed} مورد ناموفق{detail}.",
                        restored > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Error);
                }

                rebuild();
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "MainWindow.Quarantine.Restore");
                ShowInfoBar("قرنطینه", "بازگردانی انجام نشد: " + ex.Message, InfoBarSeverity.Error);
                restoreBtn.IsEnabled = true;
                deleteBtn.IsEnabled = true;
                restoreBtn.Content = "بازگردانی همه";
            }
        }

        /// <summary>حذف قطعی یک نشست قرنطینه؛ بعد از این عملیات بازگردانی ممکن نیست.</summary>
        private async Task DeleteQuarantineSessionAsync(
            QuarantineManifest session, Button restoreBtn, Button deleteBtn, Action rebuild)
        {
            try
            {
                restoreBtn.IsEnabled = false;
                deleteBtn.IsEnabled = false;

                bool ok = await Task.Run(() => SafeDeleteService.DeleteSession(session.SessionId));

                ShowInfoBar(
                    "قرنطینه",
                    ok ? "نشست قرنطینه و فایل‌های آن برای همیشه حذف شد." : "حذف نشست قرنطینه انجام نشد.",
                    ok ? InfoBarSeverity.Success : InfoBarSeverity.Error);

                rebuild();
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "MainWindow.Quarantine.Delete");
                ShowInfoBar("قرنطینه", "حذف انجام نشد: " + ex.Message, InfoBarSeverity.Error);
                deleteBtn.IsEnabled = true;
                restoreBtn.IsEnabled = true;
            }
        }
    }
}
