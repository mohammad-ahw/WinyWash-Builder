// src/CleanC.UI/AppServices.cs
// C6 — ریشه‌ی ترکیب (composition root): ثبت سرویس‌ها و ViewModelها در کانتینر DI.
// کانتینر از App.Services در دسترس است؛ لایه‌ی نمایش دیگر نباید سرویس‌ها را با new می‌سازد.

using CleanC.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CleanC.UI
{
    public static class AppServices
    {
        private static ServiceProvider? _services;

        /// <summary>کانتینر برنامه (تنها یک بار ساخته می‌شود).</summary>
        public static IServiceProvider Services => _services ??= Build();

        /// <summary>ساخت زودهنگام در شروع برنامه تا خطای ثبت، همان ابتدا دیده شود.</summary>
        public static void EnsureBuilt() => _ = Services;

        public static T GetRequired<T>() where T : notnull => Services.GetRequiredService<T>();

        private static ServiceProvider Build()
            => new ServiceCollection().AddCleanCServices().BuildServiceProvider();

        /// <summary>
        /// همه‌ی ثبت‌های برنامه همین‌جا انجام می‌شوند؛ برای افزودن سرویس جدید فقط
        /// این متد باید عوض شود (بدون پخش‌شدنِ new در سراسر کد).
        /// </summary>
        public static IServiceCollection AddCleanCServices(this IServiceCollection services)
        {
            // تنظیماتِ زنده‌ی برنامه + debounce ذخیره (یک نمونه در کل اجرا)
            services.AddSingleton<ISettingsStore, SettingsStore>();

            // نوتیفیکیشن‌های ویندوزی (toast / badge)
            services.AddSingleton<WindowsNotificationService>();

            // ViewModel صفحه‌ی تنظیمات
            services.AddSingleton<SettingsViewModel>();

            return services;
        }
    }
}
