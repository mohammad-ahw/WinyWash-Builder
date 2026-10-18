using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;

namespace CleanC.Core.Services
{
    public class DnsPreset
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PrimaryDns { get; set; } = string.Empty;
        public string SecondaryDns { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = "تحریم شکن"; // "تحریم شکن", "بین المللی", "امنیت"
        public string IconGlyph { get; set; } = "\uE774";
        public bool IsDhcp { get; set; }
    }

    public static class DnsManagerService
    {
        public static readonly List<DnsPreset> Presets = new()
        {
            new DnsPreset
            {
                Id = "shecan",
                Name = "تحریم شکن شکن (Shecan)",
                PrimaryDns = "178.22.122.100",
                SecondaryDns = "185.51.200.2",
                Description = "عبور مطمئن از تحریم های توسعه دهندگان، داکر، انویدیا، اوراکل و ادوبی",
                Category = "تحریم شکن قانونی",
                IconGlyph = "\uE774"
            },
            new DnsPreset
            {
                Id = "electro",
                Name = "تحریم شکن الکترو (Electro)",
                PrimaryDns = "78.157.42.101",
                SecondaryDns = "78.157.42.100",
                Description = "مخصوص عبور از تحریم های گیمینگ، دیسکورد، اپیک گیمز و دانلود سریع",
                Category = "تحریم شکن قانونی",
                IconGlyph = "\uE7FC"
            },
            new DnsPreset
            {
                Id = "403online",
                Name = "تحریم شکن ۴۰۳ آنلاین",
                PrimaryDns = "10.202.10.202",
                SecondaryDns = "10.202.10.102",
                Description = "سرویس رفع تحریم سرویس های خارجی برای کاربران، استارتاپ ها و توسعه دهندگان",
                Category = "تحریم شکن قانونی",
                IconGlyph = "\uE895"
            },
            new DnsPreset
            {
                Id = "radar",
                Name = "رادار بازی (Radar Game)",
                PrimaryDns = "10.202.10.10",
                SecondaryDns = "10.202.10.11",
                Description = "کاهش پینگ و رفع اختلال اتصال به سرور بازی های آنلاین",
                Category = "گیمینگ و پینگ",
                IconGlyph = "\uE7FC"
            },
            new DnsPreset
            {
                Id = "cloudflare",
                Name = "کلودفلر (Cloudflare 1.1.1.1)",
                PrimaryDns = "1.1.1.1",
                SecondaryDns = "1.0.0.1",
                Description = "سریع ترین و امن ترین دی ان اس عمومی جهان با حداقل تاخیر",
                Category = "بین المللی",
                IconGlyph = "\uEB55"
            },
            new DnsPreset
            {
                Id = "google",
                Name = "گوگل (Google Public DNS)",
                PrimaryDns = "8.8.8.8",
                SecondaryDns = "8.8.4.4",
                Description = "پایدارترین دی ان اس جهانی با کش گسترده سرورها در سراسر دنیا",
                Category = "بین المللی",
                IconGlyph = "\uE774"
            },
            new DnsPreset
            {
                Id = "adguard",
                Name = "ادگارد ضد تبلیغات (AdGuard DNS)",
                PrimaryDns = "94.140.14.14",
                SecondaryDns = "94.140.15.15",
                Description = "مسدودسازی تبلیغات وب، ترکرها و پاپ آپ های مزاحم اینترنتی",
                Category = "امنیت و حریم خصوصی",
                IconGlyph = "\uE8F8"
            },
            new DnsPreset
            {
                Id = "dhcp",
                Name = "خودکار شبکه (DHCP پیش فرض)",
                PrimaryDns = "",
                SecondaryDns = "",
                Description = "تنظیم دریافت خودکار آدرس DNS از مودم یا ارائه دهنده اینترنت",
                Category = "پیش فرض سیستم",
                IconGlyph = "\uE777",
                IsDhcp = true
            }
        };

        public static List<string> GetActiveAdapterNames()
        {
            var names = new List<string>();
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in interfaces)
                {
                    if (nic.OperationalStatus == OperationalStatus.Up &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                        !nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                        !nic.Description.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) &&
                        !nic.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(nic.Name);
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // If none found with filter, grab whatever is up
            if (names.Count == 0)
            {
                try
                {
                    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        {
                            names.Add(nic.Name);
                        }
                    }
                }
                catch (Exception __ex)
                {
                    CcLog.Ignore(__ex);
                }
            }

            return names;
        }

        public static async Task<(bool success, string message)> SetDnsAsync(string primaryDns, string secondaryDns, string? adapterName = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var adapters = string.IsNullOrEmpty(adapterName) ? GetActiveAdapterNames() : new List<string> { adapterName };
                    if (adapters.Count == 0)
                    {
                        return (false, "هیچ کارت شبکه فعال متصل به اینترنت یافت نشد.");
                    }

                    int okCount = 0;
                    var errors = new List<string>();
                    foreach (var adapter in adapters)
                    {
                        string[] servers = string.IsNullOrWhiteSpace(secondaryDns)
                            ? new[] { primaryDns }
                            : new[] { primaryDns, secondaryDns };

                        var (ok, error) = ApplyDnsViaWmi(adapter, servers);
                        if (ok) okCount++;
                        else errors.Add($"{adapter}: {error}");
                    }

                    // Flush DNS resolver cache
                    FlushDns();

                    if (okCount == adapters.Count)
                    {
                        return (true, $"تنظیمات DNS با موفقیت به {primaryDns} اعمال و کش شبکه نوسازی شد.");
                    }
                    if (okCount > 0)
                    {
                        return (true, $"DNSِ {okCount} کارت از {adapters.Count} کارت تنظیم شد. " + string.Join("؛ ", errors));
                    }
                    return (false, "تنظیم DNS ناموفق بود — " + string.Join("؛ ", errors));
                }
                catch (Exception ex)
                {
                    return (false, $"خطا در تنظیم DNS: {ex.Message}");
                }
            });
        }

        public static async Task<(bool success, string message)> ResetDnsToDhcpAsync(string? adapterName = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var adapters = string.IsNullOrEmpty(adapterName) ? GetActiveAdapterNames() : new List<string> { adapterName };
                    if (adapters.Count == 0)
                    {
                        return (false, "هیچ کارت شبکه فعال متصل به اینترنت یافت نشد.");
                    }

                    int okCount = 0;
                    var errors = new List<string>();
                    foreach (var adapter in adapters)
                    {
                        var (ok, error) = ApplyDnsViaWmi(adapter, null);
                        if (ok) okCount++;
                        else errors.Add($"{adapter}: {error}");
                    }

                    FlushDns();

                    if (okCount == adapters.Count)
                    {
                        return (true, "تنظیمات DNS تمامی کارت های شبکه به حالت خودکار (DHCP) بازگردانده شد.");
                    }
                    if (okCount > 0)
                    {
                        return (true, $"DNSِ {okCount} کارت از {adapters.Count} کارت به حالت خودکار برگشت. " + string.Join("؛ ", errors));
                    }
                    return (false, "بازگرداندن DNS به حالت خودکار ناموفق بود — " + string.Join("؛ ", errors));
                }
                catch (Exception ex)
                {
                    return (false, $"خطا در ریست DNS: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// اعمال DNS روی یک کارت شبکه از طریق WMI (جایگزین netsh که الگوی مشکوک آنتی ویروسی داشت).
        /// اگر dnsServers برابر null باشد یعنی برگشت به DNS خودکارِ DHCP؛ برخلاف EnableDHCP فقط
        /// تنظیمات DNS تغییر می کند و آدرس IP کارت دست نمی خورد.
        /// </summary>
        private static (bool ok, string error) ApplyDnsViaWmi(string adapterName, string[]? dnsServers)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\cimv2",
                    "SELECT NetConnectionID FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
                foreach (ManagementObject mo in searcher.Get())
                {
                    using var _mo = mo;
                    string netId = mo["NetConnectionID"]?.ToString() ?? "";
                    if (!string.Equals(netId, adapterName, StringComparison.OrdinalIgnoreCase)) continue;

                    using var inParams = mo.GetMethodParameters("SetDNSServerSearchOrder");
                    inParams["DNSServerSearchOrder"] = dnsServers; // null = برگشت به DNS خودکار
                    using var outParams = mo.InvokeMethod("SetDNSServerSearchOrder", inParams, null);

                    uint code = Convert.ToUInt32(outParams?["ReturnValue"] ?? 1u);
                    if (code == 0 || code == 1) return (true, "");
                    return (false, $"کد خطای WMI: {code}");
                }
                return (false, "کارت شبکه در WMI پیدا نشد");
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Dns.WMI");
                return (false, ex.Message);
            }
        }

        public static (string primary, string secondary, string matchedPresetName) GetCurrentDns()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in interfaces)
                {
                    if (nic.OperationalStatus == OperationalStatus.Up &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        !nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                    {
                        var props = nic.GetIPProperties();
                        var dnsServers = props.DnsAddresses.Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToList();
                        if (dnsServers.Count > 0)
                        {
                            string p = dnsServers[0].ToString();
                            string s = dnsServers.Count > 1 ? dnsServers[1].ToString() : "";

                            var match = Presets.FirstOrDefault(x => !x.IsDhcp && x.PrimaryDns == p);
                            string matchName = match != null ? match.Name : "سفارشی / مودم";

                            return (p, s, matchName);
                        }
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            return ("خودکار", "", "خودکار (DHCP)");
        }

        public static void FlushDns()
        {
            try
            {
                var psi = new ProcessStartInfo("ipconfig", "/flushdns")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }
        }
    }
}
