// O:\OpenCode\AntiGravity\CleanC - NEW\src\CleanC.UI\SafeInvoker.cs
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace CleanC.UI
{
    /// <summary>
    /// C8 — جای امن برای event handlerهای async.
    ///
    /// یک <c>async void</c> معمولی اگر استثنا پرتاب کند، آن استثنا از تهِ زنجیره به
    /// <c>Application.UnhandledException</c> می‌رسد؛ یعنی کل برنامه باید یک تصمیم
    /// سراسری برای همه‌ی خطاها بگیرد. این کلاس هر handler را «جدا» نگه می‌دارد:
    /// خطای هر دکمه فقط با همان دکمه log می‌شود و بقیه‌ی UI از کار نمی‌افتد.
    ///
    /// الگوی استفاده:
    /// <code>
    /// private void Foo_Click(object sender, RoutedEventArgs e)
    ///     => SafeInvoker.Run(Foo_ClickAsync(sender, e));
    /// private async Task Foo_ClickAsync(object sender, RoutedEventArgs e) { ... }
    /// </code>
    /// </summary>
    internal static class SafeInvoker
    {
        /// <summary>
        /// اجرای ناهمگامِ handler و ثبت خطای احتمالی با همان شناسه‌ی handler.
        /// </summary>
        public static async Task RunAsync(string name, Task task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                App.LogCrash(name, ex);
            }
        }

        /// <summary>
        /// همان <see cref="RunAsync"/>؛ نام handler را از محل فراخوانی برمی‌دارد.
        /// </summary>
        public static void Run(Task task, [CallerMemberName] string name = "handler")
            => _ = RunAsync(name, task);
    }
}
