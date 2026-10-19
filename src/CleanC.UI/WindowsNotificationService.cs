// src/CleanC.UI/WindowsNotificationService.cs
// نوتیفیکیشن های واقعی ویندوز: toast (WinAppSDK AppNotification) + badge عدد سلامت.
//
// چون CleanC unpackaged و با دسترسی administrator اجرا می شود، ثبت AUMID در HKCU
// (Software\Classes\AppUserModelId) الزامی است و فعال سازی (کلیک روی toast) ممکن است
// به خاطر UIPI کار نکند. برای همین همه ی مسیرها try/catch دارند و در صورت شکست،
// لایه ی قابل اعتماد پشتیبان (balloon تری آیکون) استفاده می شود.

using System;
using System.IO;
using CleanC.Core.Diagnostics;
using Microsoft.Win32;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace CleanC.UI
{
    /// <summary>شدت نوتیفیکیشن ویندوزی — معادل سطح های InfoBarSeverity.</summary>
    public enum WinNotifySeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class WindowsNotificationService : IDisposable
    {
        /// <summary>AUMID ثبت شده در HKCU برای اپ های unpackaged.</summary>
        public const string AumId = "WinyWash.App";

        private readonly string _iconUri;
        private bool _toastAvailable;
        private bool _badgeAvailable = true;
        private bool _disposed;

        public WindowsNotificationService()
        {
            string exe = Environment.ProcessPath ?? string.Empty;
            _iconUri = string.IsNullOrEmpty(exe) ? string.Empty : exe;
        }

        /// <summary>آیا toast فعال است؟ (اگر نه، باید از balloon استفاده شود)</summary>
        public bool IsToastAvailable => _toastAvailable;

        /// <summary>
        /// ثبت AUMID در رجیستری و سپس ثبت سرویس toast. هیچ استثنایی بیرون نمی رود؛
        /// در صورت شکست <see cref="IsToastAvailable"/> می ماند روی false.
        /// </summary>
        public void TryRegister()
        {
            if (_toastAvailable || _disposed) return;

            try
            {
                RegisterAumId();
                AppNotificationManager.Default.Register();
                _toastAvailable = true;
            }
            catch (Exception ex)
            {
                _toastAvailable = false;
                CcLog.Ignore(ex, "WindowsNotificationService.TryRegister");
            }
        }

        /// <summary>نمایش یک toast ویندوزی؛ در صورت شکست false برمی گرداند تا فراخواننده fallback کند.</summary>
        public bool TryShowToast(string title, string message, WinNotifySeverity severity)
        {
            if (_disposed || !_toastAvailable || string.IsNullOrWhiteSpace(message)) return false;

            try
            {
                var builder = new AppNotificationBuilder()
                    .AddText(string.IsNullOrWhiteSpace(title) ? "WinyWash" : title)
                    .AddText(message);

                AppNotificationManager.Default.Show(builder.BuildNotification());
                return true;
            }
            catch (Exception ex)
            {
                _toastAvailable = false;
                CcLog.Ignore(ex, "WindowsNotificationService.TryShowToast");
                return false;
            }
        }

        /// <summary>
        /// عدد سلامت را روی آیکون تسک بار نشان می دهد (badge).
        /// مقدار کمتر از صورت badge را پاک می کند.
        /// چون اپ unpackaged است و میانبر Start Menu با AUMID ما ندارد، این API ممکن است
        /// COMException بدهد؛ در آن صورت برای همیشه غیرفعال می شود تا هر فریمِ تغییرِ
        /// سلامت یک استثنای بیهوده در لاگ ننویسد.
        /// </summary>
        /// <param name="value">عدد سلامت (۰–۱۰۰)؛ مقادیر کمتر از صورت badge را پاک می کنند.</param>
        /// <returns>true یعنی badge اعمال شد؛ false یعنی در دسترس نیست و باید جایگزین دیگری استفاده شود.</returns>
        public bool TrySetBadge(int value)
        {
            if (_disposed || !_badgeAvailable) return false;

            try
            {
                var updater = BadgeUpdateManager.CreateBadgeUpdaterForApplication(AumId);

                if (value <= 0)
                {
                    updater.Clear();
                    return true;
                }

                int clamped = Math.Clamp(value, 1, 99);
                var xml = new XmlDocument();
                xml.LoadXml($"<badge version=\"1\" value=\"{clamped}\"/>");
                updater.Update(new BadgeNotification(xml));
                return true;
            }
            catch (Exception ex)
            {
                _badgeAvailable = false;
                CcLog.Ignore(ex, "WindowsNotificationService.TrySetBadge");
                return false;
            }
        }

        /// <summary>HKCU\Software\Classes\AppUserModelId\CleanC.App — بدون این، toast unpackaged ثبت نمی شود.</summary>
        private void RegisterAumId()
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\AppUserModelId\{AumId}");

            key.SetValue("DisplayName", "WinyWash", RegistryValueKind.String);
            if (!string.IsNullOrWhiteSpace(_iconUri))
                key.SetValue("IconUri", _iconUri, RegistryValueKind.String);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_toastAvailable) return;

            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "WindowsNotificationService.Dispose");
            }
        }
    }
}
