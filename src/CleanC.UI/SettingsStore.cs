// src/CleanC.UI/SettingsStore.cs
// C6 — لایه ی instance برای تنظیمات: به جای فراخوانی مستقیم های استاتیکِ SettingsService،
// یک سرویسِ قابلِ تزریق (ISettingsStore) داریم که debounce ذخیره را هم خودش مدیریت می کند.
// برنامه ی میزبان (MainWindow) با OnSaveRequested می تواند قبل از نوشتن فایل،
// داده ی زنده ی خودش (مثل موقعیت پنجره) را در AppSettings ثبت کند.

using System;
using CleanC.Core.Diagnostics;
using CleanC.Core.Services;
using Microsoft.UI.Dispatching;

namespace CleanC.UI
{
    public interface ISettingsStore
    {
        /// <summary>تنظیماتِ زنده ی برنامه؛ همان شیئی که همه ی صفحات می خوانند/می نویسند.</summary>
        AppSettings Current { get; }

        /// <summary>مسیر فایل settings.json در دیسک.</summary>
        string FilePath { get; }

        /// <summary>قبل از نوشتن فایل اجرا می شود (ثبت موقعیت پنجره و …).</summary>
        Action? OnSaveRequested { get; set; }

        /// <summary>جایگزین کردن کل تنظیمات (دکمه ی بازنشانی همه ی تنظیمات).</summary>
        void Replace(AppSettings settings);

        /// <summary>شروع debounce ذخیره؛ با هر فراخوانی تایمر از نو شمرده می شود.</summary>
        void ScheduleSave();

        /// <summary>ذخیره ی فوری در دیسک.</summary>
        void SaveNow();
    }

    public sealed class SettingsStore : ISettingsStore, IDisposable
    {
        private readonly DispatcherQueueTimer _debounce;
        private bool _disposed;

        public SettingsStore()
        {
            Current = SettingsService.Load();

            var queue = DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("SettingsStore باید روی UI thread ساخته شود.");

            _debounce = queue.CreateTimer();
            _debounce.Interval = TimeSpan.FromMilliseconds(600);
            _debounce.Tick += (_, _) => SaveNow();
        }

        public AppSettings Current { get; private set; }

        public string FilePath => SettingsService.FilePath;

        public Action? OnSaveRequested { get; set; }

        public void Replace(AppSettings settings) => Current = settings ?? new AppSettings();

        public void ScheduleSave()
        {
            if (_disposed) return;
            _debounce.Stop();
            _debounce.Start();
        }

        public void SaveNow()
        {
            if (_disposed) return;
            _debounce.Stop();

            try
            {
                OnSaveRequested?.Invoke();
                SettingsService.Save(Current);
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "SettingsStore.SaveNow");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _debounce.Stop();
        }
    }
}
