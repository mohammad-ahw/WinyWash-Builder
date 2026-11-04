// i18n-launcher: پیام‌های خطای استارتاپ لانچر مستقل WinyWash
using System;
using System.Diagnostics;
using System.IO;

namespace WinyWash.Launcher
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string targetExe = Path.Combine(baseDir, "app", "WinyWash.exe");
                if (!File.Exists(targetExe))
                {
                    targetExe = Path.Combine(baseDir, "bin", "WinyWash.exe");
                }

                if (!File.Exists(targetExe))
                {
                    // i18n
                    System.Windows.Forms.MessageBox.Show(
                        "فایل‌های اصلی نرم‌افزار در پوشه app یافت نشدند.\nمسیر مورد انتظار:\n" + targetExe,
                        "WinyWash",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error);
                    return 1;
                }

                string cmdLine = Environment.CommandLine;
                string argsStr = "";
                string[] sysArgs = Environment.GetCommandLineArgs();
                if (sysArgs.Length > 1)
                {
                    int firstSpace = cmdLine.IndexOf(sysArgs[0]);
                    if (firstSpace >= 0)
                    {
                        argsStr = cmdLine.Substring(firstSpace + sysArgs[0].Length).Trim();
                    }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = targetExe,
                    Arguments = argsStr,
                    WorkingDirectory = Path.GetDirectoryName(targetExe),
                    UseShellExecute = true
                };

                Process.Start(psi);
                return 0;
            }
            catch (Exception ex)
            {
                // i18n
                System.Windows.Forms.MessageBox.Show(
                    "خطا در اجرای برنامه:\n" + ex.Message,
                    "WinyWash",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
