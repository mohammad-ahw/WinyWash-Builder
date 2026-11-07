// i18n-launcher: Clean, international startup error messages for WinyWash launcher
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
                    System.Windows.Forms.MessageBox.Show(
                        "Core application files could not be found in the 'app' directory.\nExpected path:\n" + targetExe,
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
                System.Windows.Forms.MessageBox.Show(
                    "Error launching WinyWash:\n" + ex.Message,
                    "WinyWash",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
