using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using CleanC.Core.Diagnostics;
using Microsoft.Win32;

namespace CleanC.Core.Services
{
    public class ProxyConflictInfo
    {
        public bool HasConflict { get; set; }
        public string ProxyServer { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Details { get; set; } = string.Empty;
    }

    public static class NetworkProxyDiagnosticService
    {
        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 85;

        private static readonly string[] VpnProcessSignatures = new[]
        {
            "v2rayN", "v2ray", "xray", "sing-box", "clash", "clash-verge", "clash-meta",
            "nekoray", "nekobox", "hiddify", "qv2ray", "shadowsocks", "outline-service",
            "warp-plus", "trojan", "openvpn", "wireguard"
        };

        public static ProxyConflictInfo DetectGhostProxyConflict()
        {
            var info = new ProxyConflictInfo();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false);
                if (key == null) return info;

                int proxyEnable = key.GetValue("ProxyEnable") is int p ? p : 0;
                if (proxyEnable != 1) return info;

                string? proxyServer = key.GetValue("ProxyServer")?.ToString()?.Trim();
                if (string.IsNullOrEmpty(proxyServer)) return info;

                info.ProxyServer = proxyServer;

                bool isLocalhost = proxyServer.Contains("127.0.0.1") || proxyServer.Contains("localhost");
                if (!isLocalhost) return info;

                int port = ExtractPort(proxyServer);
                info.Port = port;

                // 1. Is any VPN client running?
                bool vpnRunning = false;
                try
                {
                    var processes = Process.GetProcesses();
                    foreach (var proc in processes)
                    {
                        try
                        {
                            foreach (var sig in VpnProcessSignatures)
                            {
                                if (proc.ProcessName.Contains(sig, StringComparison.OrdinalIgnoreCase))
                                {
                                    vpnRunning = true;
                                    break;
                                }
                            }
                        }
                        catch { }
                        finally { proc.Dispose(); }
                        if (vpnRunning) break;
                    }
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex);
                }

                if (vpnRunning)
                {
                    return info; // Active VPN client found, proxy is valid
                }

                // 2. If VPN is not running, check if localhost proxy port is listening
                if (port > 0)
                {
                    bool isPortListening = false;
                    try
                    {
                        using var client = new TcpClient();
                        var result = client.BeginConnect("127.0.0.1", port, null, null);
                        bool connected = result.AsyncWaitHandle.WaitOne(60); // 60ms fast probe
                        if (connected)
                        {
                            client.EndConnect(result);
                            isPortListening = true;
                        }
                    }
                    catch
                    {
                        isPortListening = false;
                    }

                    if (!isPortListening)
                    {
                        info.HasConflict = true;
                        info.Details = string.Format(LocalizationService.Get("پراکسی سیستم روی پورت {0} فعال مانده در حالی که کلاینت VPN بسته شده است و اینترنت ترمینال و ویندوز قطع می باشد."), port);
                    }
                }
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex);
            }

            return info;
        }

        public static bool FixGhostProxy()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                if (key != null)
                {
                    key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                }

                // Clear HTTP_PROXY/HTTPS_PROXY in user environment if pointing to loopback
                try
                {
                    string? httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY", EnvironmentVariableTarget.User);
                    if (!string.IsNullOrEmpty(httpProxy) && (httpProxy.Contains("127.0.0.1") || httpProxy.Contains("localhost")))
                    {
                        Environment.SetEnvironmentVariable("HTTP_PROXY", null, EnvironmentVariableTarget.User);
                    }
                    string? httpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY", EnvironmentVariableTarget.User);
                    if (!string.IsNullOrEmpty(httpsProxy) && (httpsProxy.Contains("127.0.0.1") || httpsProxy.Contains("localhost")))
                    {
                        Environment.SetEnvironmentVariable("HTTPS_PROXY", null, EnvironmentVariableTarget.User);
                    }
                }
                catch (Exception ex)
                {
                    CcLog.Ignore(ex);
                }

                // Notify Windows/WinINet system-wide
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);

                return true;
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex);
                return false;
            }
        }

        public static int ExtractPort(string proxyStr)
        {
            if (string.IsNullOrWhiteSpace(proxyStr)) return 0;
            try
            {
                int colonIdx = proxyStr.LastIndexOf(':');
                if (colonIdx > 0 && colonIdx < proxyStr.Length - 1)
                {
                    string portStr = proxyStr.Substring(colonIdx + 1).Trim().TrimEnd(';');
                    if (int.TryParse(portStr, out int p)) return p;
                }
            }
            catch { }
            return 0;
        }
    }
}
