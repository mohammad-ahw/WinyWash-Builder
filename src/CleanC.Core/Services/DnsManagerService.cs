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
        // i18n-logic: این متن با دادهی خارجی مقایسه/تطبیق داده میشود (UI با Contains("تحریم") روی Category تطبیق میدهد)
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
                Name = LocalizationService.Get("تحریم شکن شکن (Shecan)"),
                PrimaryDns = "178.22.122.100",
                SecondaryDns = "185.51.200.2",
                Description = LocalizationService.Get("عبور مطمئن از تحریم های توسعه دهندگان، داکر، انویدیا، اوراکل و ادوبی"),
                // i18n-logic: این متن با دادهی خارجی مقایسه/تطبیق داده میشود (UI با Contains("تحریم") روی Category تطبیق میدهد)
                Category = "تحریم شکن قانونی",
                IconGlyph = "\uE774"
            },
            new DnsPreset
            {
                Id = "electro",
                Name = LocalizationService.Get("تحریم شکن الکترو (Electro)"),
                PrimaryDns = "78.157.42.101",
                SecondaryDns = "78.157.42.100",
                Description = LocalizationService.Get("مخصوص عبور از تحریم های گیمینگ، دیسکورد، اپیک گیمز و دانلود سریع"),
                // i18n-logic: این متن با دادهی خارجی مقایسه/تطبیق داده میشود (UI با Contains("تحریم") روی Category تطبیق میدهد)
                Category = "تحریم شکن قانونی",
                IconGlyph = "\uE7FC"
            },
            new DnsPreset
            {
                Id = "403online",
                Name = LocalizationService.Get("تحریم شکن ۴۰۳ آنلاین"),
                PrimaryDns = "10.202.10.202",
                SecondaryDns = "10.202.10.102",
                Description = LocalizationService.Get("سرویس رفع تحریم سرویس های خارجی برای کاربران، استارتاپ ها و توسعه دهندگان"),
                // i18n-logic: این متن با دادهی خارجی مقایسه/تطبیق داده میشود (UI با Contains("تحریم") روی Category تطبیق میدهد)
                Category = "تحریم شکن قانونی",
                IconGlyph = "\uE895"
            },
            new DnsPreset
            {
                Id = "radar",
                Name = LocalizationService.Get("رادار بازی (Radar Game)"),
                PrimaryDns = "10.202.10.10",
                SecondaryDns = "10.202.10.11",
                Description = LocalizationService.Get("کاهش پینگ و رفع اختلال اتصال به سرور بازی های آنلاین"),
                Category = LocalizationService.Get("گیمینگ و پینگ"),
                IconGlyph = "\uE7FC"
            },
            new DnsPreset
            {
                Id = "cloudflare",
                Name = LocalizationService.Get("کلودفلر (Cloudflare 1.1.1.1)"),
                PrimaryDns = "1.1.1.1",
                SecondaryDns = "1.0.0.1",
                Description = LocalizationService.Get("سریع ترین و امن ترین دی ان اس عمومی جهان با حداقل تاخیر"),
                Category = LocalizationService.Get("بین المللی"),
                IconGlyph = "\uEB55"
            },
            new DnsPreset
            {
                Id = "google",
                Name = LocalizationService.Get("گوگل (Google Public DNS)"),
                PrimaryDns = "8.8.8.8",
                SecondaryDns = "8.8.4.4",
                Description = LocalizationService.Get("پایدارترین دی ان اس جهانی با کش گسترده سرورها در سراسر دنیا"),
                Category = LocalizationService.Get("بین المللی"),
                IconGlyph = "\uE774"
            },
            new DnsPreset
            {
                Id = "adguard",
                Name = LocalizationService.Get("ادگارد ضد تبلیغات (AdGuard DNS)"),
                PrimaryDns = "94.140.14.14",
                SecondaryDns = "94.140.15.15",
                Description = LocalizationService.Get("مسدودسازی تبلیغات وب، ترکرها و پاپ آپ های مزاحم اینترنتی"),
                Category = LocalizationService.Get("امنیت و حریم خصوصی"),
                IconGlyph = "\uE8F8"
            },
            new DnsPreset
            {
                Id = "dhcp",
                Name = LocalizationService.Get("خودکار شبکه (DHCP پیش فرض)"),
                PrimaryDns = "",
                SecondaryDns = "",
                Description = LocalizationService.Get("تنظیم دریافت خودکار آدرس DNS از مودم یا ارائه دهنده اینترنت"),
                Category = LocalizationService.Get("پیش فرض سیستم"),
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
                        return (false, LocalizationService.Get("هیچ کارت شبکه فعال متصل به اینترنت یافت نشد."));
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
                        return (true, string.Format(LocalizationService.Get("تنظیمات DNS با موفقیت به {0} اعمال و کش شبکه نوسازی شد."), primaryDns));
                    }
                    if (okCount > 0)
                    {
                        return (true, string.Format(LocalizationService.Get("DNSِ {0} کارت از {1} کارت تنظیم شد. "), okCount, adapters.Count) + string.Join(LocalizationService.Get("؛ "), errors));
                    }
                    return (false, LocalizationService.Get("تنظیم DNS ناموفق بود — ") + string.Join(LocalizationService.Get("؛ "), errors));
                }
                catch (Exception ex)
                {
                    return (false, string.Format(LocalizationService.Get("خطا در تنظیم DNS: {0}"), ex.Message));
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
                        return (false, LocalizationService.Get("هیچ کارت شبکه فعال متصل به اینترنت یافت نشد."));
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
                        return (true, LocalizationService.Get("تنظیمات DNS تمامی کارت های شبکه به حالت خودکار (DHCP) بازگردانده شد."));
                    }
                    if (okCount > 0)
                    {
                        return (true, string.Format(LocalizationService.Get("DNSِ {0} کارت از {1} کارت به حالت خودکار برگشت. "), okCount, adapters.Count) + string.Join(LocalizationService.Get("؛ "), errors));
                    }
                    return (false, LocalizationService.Get("بازگرداندن DNS به حالت خودکار ناموفق بود — ") + string.Join(LocalizationService.Get("؛ "), errors));
                }
                catch (Exception ex)
                {
                    return (false, string.Format(LocalizationService.Get("خطا در ریست DNS: {0}"), ex.Message));
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
                    return (false, string.Format(LocalizationService.Get("کد خطای WMI: {0}"), code));
                }
                return (false, LocalizationService.Get("کارت شبکه در WMI پیدا نشد"));
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
                            string matchName = match != null ? match.Name : LocalizationService.Get("سفارشی / مودم");

                            return (p, s, matchName);
                        }
                    }
                }
            }
            catch (Exception __ex)
            {
                CcLog.Ignore(__ex);
            }

            // i18n-logic: این متن با دادهی خارجی مقایسه/تطبیق داده میشود (UI مقدار primary را با "خودکار" مقایسه میکند)
            return ("خودکار", "", LocalizationService.Get("خودکار (DHCP)"));
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
