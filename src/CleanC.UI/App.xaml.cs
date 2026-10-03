using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace CleanC.UI
{
    public partial class App : Application
    {
        private Window? _window;

        public App()
        {
            this.InitializeComponent();
            this.UnhandledException += (sender, e) =>
            {
                e.Handled = true;
                try
                {
                    string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CleanC");
                    Directory.CreateDirectory(logDir);
                    string logFile = Path.Combine(logDir, "crash.log");
                    File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UnhandledException: {e.Message}\n{e.Exception}\n\n");
                }
                catch { }

                System.Diagnostics.Debug.WriteLine($"[App UnhandledException Handled] {e.Message}");
            };
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }
    }
}
