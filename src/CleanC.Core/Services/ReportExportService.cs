// src/CleanC.Core/Services/ReportExportService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using CleanC.Core.Diagnostics;
using CleanC.Core.Models;

namespace CleanC.Core.Services
{
    /// <summary>یک سطر از تاریخچه‌ی پاکسازی برای خروجی CSV.</summary>
    public sealed class CleanupHistoryRow
    {
        public string Date { get; set; } = string.Empty;
        public long Bytes { get; set; }
    }

    /// <summary>
    /// ساختِ خروجی‌های گزارش:
    /// - گزارش مارک‌داون (.md) متناسب برای بافت پرامپت هوش مصنوعی (LLM Context)
    /// - مشخصات سلسله‌مراتبی فنی سیستم (.txt) با سبک حرفه‌ای (مشابه llmfit)
    /// - خروجی اطلاعات عیب‌یابی برای باگ‌ها
    /// </summary>
    public static class ReportExportService
    {
        public static string EscapeCsv(string? value)
        {
            string s = value ?? string.Empty;
            bool needsQuotes = s.IndexOfAny(new[] { ',', '"', '\r', '\n', '\t' }) >= 0;
            if (!needsQuotes) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static string CsvRow(params string[] fields)
        {
            var sb = new StringBuilder(fields.Length * 24);
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(EscapeCsv(fields[i]));
            }
            sb.Append("\r\n");
            return sb.ToString();
        }

        public static string BuildStartupCsv(IReadOnlyList<StartupItem> items)
        {
            var sb = new StringBuilder();
            sb.Append(CsvRow("Name", "Location", "Category", "Enabled", "ImpactLabel", "ImpactScore", "ImpactMs", "ImpactMeasured", "Command"));

            if (items != null)
            {
                foreach (var it in items)
                {
                    sb.Append(CsvRow(
                        it.Name,
                        it.Location,
                        it.Category,
                        it.IsDisabled ? "No" : "Yes",
                        it.ImpactLabel,
                        it.ImpactScore.ToString(CultureInfo.InvariantCulture),
                        it.ImpactMs.ToString(CultureInfo.InvariantCulture),
                        it.ImpactMeasured ? "Yes" : "No",
                        it.Command));
                }
            }

            return sb.ToString();
        }

        public static string BuildCleanupHistoryCsv(IReadOnlyList<CleanupHistoryRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append(CsvRow("Date", "Bytes", "Megabytes", "Gigabytes"));

            if (rows != null)
            {
                foreach (var r in rows)
                {
                    double mb = r.Bytes / 1048576.0;
                    double gb = r.Bytes / 1073741824.0;
                    sb.Append(CsvRow(
                        r.Date,
                        r.Bytes.ToString(CultureInfo.InvariantCulture),
                        mb.ToString("0.##", CultureInfo.InvariantCulture),
                        gb.ToString("0.###", CultureInfo.InvariantCulture)));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// ساخت گزارش مارک‌داون (.md) ساختاریافته به عنوان بافت کامل برای هوش مصنوعی (Claude / ChatGPT / Gemini)
        /// </summary>
        public static string BuildAiReportMarkdown(DetailedSystemSpecs? specs, IReadOnlyList<CleanupReportItem>? cleanupItems, long freedBytes, string? osFlight = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# CleanC System State & Optimization Diagnostic Report");
            sb.AppendLine();
            sb.AppendLine("> **Generated:** " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture));
            sb.AppendLine("> **CleanC Version:** " + GetVersion(typeof(ReportExportService).Assembly));
            sb.AppendLine("> **Target Environment:** Windows 11 / x64 Architecture");
            sb.AppendLine();

            sb.AppendLine("## 1. Executive Optimization Summary");
            sb.AppendLine();
            sb.AppendLine($"- **Total Disk Space Reclaimed:** {SystemStats.FormatBytes(freedBytes)}");
            sb.AppendLine($"- **Optimization Status:** Completed Successfully");
            sb.AppendLine($"- **Total Tasks Processed:** {(cleanupItems?.Count ?? 0).ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine();

            if (cleanupItems != null && cleanupItems.Count > 0)
            {
                sb.AppendLine("### Actions & State Transitions Audit");
                sb.AppendLine();
                sb.AppendLine("| Component / Area | Status | Result / Detail |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var item in cleanupItems)
                {
                    string status = item.IsFailed ? "❌ Failed" : item.WasEmpty ? "⚪ Already Clean" : "✅ Succeeded";
                    string title = item.Title.Replace("|", "\\|");
                    string desc = item.StatusSummary.Replace("|", "\\|");
                    sb.AppendLine($"| {title} | {status} | {desc} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("## 2. Hardware Topology & Specifications");
            sb.AppendLine();

            if (specs != null)
            {
                sb.AppendLine("### Central Processing Unit (CPU)");
                sb.AppendLine();
                sb.AppendLine($"- **Processor:** {specs.CpuName}");
                sb.AppendLine($"- **Topology:** {specs.CpuCores} Physical Cores, {specs.CpuThreads} Logical Threads");
                sb.AppendLine($"- **Base / Boost Frequency:** {specs.CpuClockMhz} MHz");
                sb.AppendLine($"- **Cache Architecture:** L2: {specs.CpuL2CacheKb} KB, L3: {specs.CpuL3CacheKb} KB");
                sb.AppendLine();

                sb.AppendLine("### Memory (RAM)");
                sb.AppendLine();
                sb.AppendLine($"- **Total Physical Memory:** {specs.TotalRamFormatted}");
                sb.AppendLine($"- **Memory Type:** {specs.RamType}");
                sb.AppendLine($"- **Rated Speed:** {specs.RamSpeedMhz} MT/s");
                sb.AppendLine($"- **Slots Used:** {specs.RamSlotsUsed} (Manufacturer: {specs.RamManufacturer})");
                sb.AppendLine();

                sb.AppendLine("### Graphics & Accelerators (GPU)");
                sb.AppendLine();
                if (specs.Gpus.Count > 0)
                {
                    sb.AppendLine("| Device Name | Dedicated VRAM | Driver Version |");
                    sb.AppendLine("| :--- | :--- | :--- |");
                    foreach (var gpu in specs.Gpus)
                    {
                        sb.AppendLine($"| {gpu.Name} | {gpu.VramFormatted} | {gpu.DriverVersion} |");
                    }
                }
                else if (specs.GpuNames.Count > 0)
                {
                    foreach (var gName in specs.GpuNames)
                    {
                        sb.AppendLine($"- {gName}");
                    }
                }
                else
                {
                    sb.AppendLine("- No dedicated discrete accelerator detected.");
                }
                sb.AppendLine();

                sb.AppendLine("### Storage & Volumes");
                sb.AppendLine();
                if (specs.Disks.Count > 0)
                {
                    sb.AppendLine("| Disk Model | Type | Bus Interface | Capacity | SMART Health |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                    foreach (var disk in specs.Disks)
                    {
                        sb.AppendLine($"| {disk.Model} | {disk.DiskType} | {disk.BusType} | {disk.SizeFormatted} | {disk.HealthStatus} |");
                    }
                }
                sb.AppendLine();

                if (specs.LogicalDrives.Count > 0)
                {
                    sb.AppendLine("#### Logical Partitions");
                    sb.AppendLine();
                    sb.AppendLine("| Drive | Volume Label | File System | Total Capacity | Free Space | Used % |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
                    foreach (var drv in specs.LogicalDrives)
                    {
                        sb.AppendLine($"| {drv.Letter} | {drv.VolumeLabel} | {drv.FileSystem} | {drv.FormattedTotal} | {drv.FormattedFree} | {drv.UsedPercent.ToString("0.0", CultureInfo.InvariantCulture)}% |");
                    }
                }
                sb.AppendLine();

                sb.AppendLine("### Motherboard & BIOS");
                sb.AppendLine();
                sb.AppendLine($"- **Board Manufacturer / Model:** {specs.MotherboardManufacturer} {specs.MotherboardModel}");
                sb.AppendLine($"- **BIOS Version / Date:** {specs.BiosVersion} ({specs.BiosReleaseDate})");
                sb.AppendLine();
            }

            sb.AppendLine("## 3. Operating System & Software Environment");
            sb.AppendLine();
            sb.AppendLine($"- **OS Name / Build:** {specs?.OsName} (Build {specs?.OsBuild}, {specs?.OsArchitecture})");
            sb.AppendLine($"- **OS Description:** {RuntimeInformation.OSDescription}");
            sb.AppendLine($"- **Process Architecture:** {RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($"- **.NET CLR Version:** {Environment.Version}");
            sb.AppendLine($"- **Activation State:** {(specs?.Activation?.StatusText ?? "Active")}");
            sb.AppendLine($"- **System Uptime:** {specs?.OsUptimeFormatted ?? "-"}");
            if (specs?.HasBattery == true)
            {
                sb.AppendLine($"- **Battery Health:** {specs.BatteryHealthPercent}% (Design: {specs.BatteryDesignCapacityMWh} mWh, Current: {specs.BatteryFullChargeCapacityMWh} mWh, Cycles: {specs.BatteryCycleCount})");
            }
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine("*End of CleanC Diagnostic Export. This markdown document can be directly supplied to language models for system context and tuning.*");

            return sb.ToString();
        }

        /// <summary>
        /// ساخت گزارش مشخصات جامع و سلسله‌مراتبی (.txt) با فرمت دقیق مشابه llmfit / Speccy
        /// </summary>
        public static string BuildSystemSpecsTxt(DetailedSystemSpecs? specs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("           CLEANC SYSTEM HARDWARE & PERFORMANCE SPECIFICATION REPORT            ");
            sb.AppendLine("================================================================================");
            sb.AppendLine("Generated      : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("Machine Name   : " + Environment.MachineName);
            sb.AppendLine("OS Version     : " + RuntimeInformation.OSDescription);
            sb.AppendLine("CleanC Core    : " + GetVersion(typeof(ReportExportService).Assembly));
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine();

            if (specs != null)
            {
                sb.AppendLine("[1] CENTRAL PROCESSOR (CPU)");
                sb.AppendLine("    • Model Name       : " + specs.CpuName);
                sb.AppendLine("    • Physical Cores   : " + specs.CpuCores.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("    • Logical Threads  : " + specs.CpuThreads.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("    • Base Clock       : " + specs.CpuClockMhz.ToString(CultureInfo.InvariantCulture) + " MHz");
                sb.AppendLine("    • L2 Cache         : " + (specs.CpuL2CacheKb / 1024.0).ToString("0.##", CultureInfo.InvariantCulture) + " MB");
                sb.AppendLine("    • L3 Cache         : " + (specs.CpuL3CacheKb / 1024.0).ToString("0.##", CultureInfo.InvariantCulture) + " MB");
                sb.AppendLine();

                sb.AppendLine("[2] SYSTEM MEMORY (RAM)");
                sb.AppendLine("    • Total Physical   : " + specs.TotalRamFormatted);
                sb.AppendLine("    • Generation       : " + specs.RamType);
                sb.AppendLine("    • Clock / MT/s     : " + specs.RamSpeedMhz.ToString(CultureInfo.InvariantCulture) + " MT/s");
                sb.AppendLine("    • Channel Topology : " + specs.RamSlotsUsed.ToString(CultureInfo.InvariantCulture) + " slots occupied");
                sb.AppendLine("    • Manufacturer     : " + specs.RamManufacturer);
                sb.AppendLine();

                sb.AppendLine("[3] GRAPHICS & DISPLAY (GPU)");
                if (specs.Gpus.Count > 0)
                {
                    int gIdx = 1;
                    foreach (var gpu in specs.Gpus)
                    {
                        sb.AppendLine($"    • GPU #{gIdx} Name    : " + gpu.Name);
                        sb.AppendLine($"      - VRAM Memory   : " + gpu.VramFormatted);
                        sb.AppendLine($"      - Driver        : " + gpu.DriverVersion);
                        gIdx++;
                    }
                }
                else if (specs.GpuNames.Count > 0)
                {
                    foreach (var gName in specs.GpuNames)
                    {
                        sb.AppendLine($"    • GPU Name        : " + gName);
                    }
                }
                else
                {
                    sb.AppendLine("    • No graphics adapter enumerated.");
                }
                sb.AppendLine();

                sb.AppendLine("[4] STORAGE SUBSYSTEM");
                if (specs.Disks.Count > 0)
                {
                    int dIdx = 1;
                    foreach (var disk in specs.Disks)
                    {
                        sb.AppendLine($"    • Disk #{dIdx} Model   : " + disk.Model);
                        sb.AppendLine($"      - Media / Bus   : " + disk.DiskType + " / " + disk.BusType);
                        sb.AppendLine($"      - Capacity      : " + disk.SizeFormatted);
                        sb.AppendLine($"      - Health Status : " + disk.HealthStatus);
                        sb.AppendLine($"      - Temperature   : " + disk.TemperatureFormatted);
                        sb.AppendLine($"      - Power On Hours: " + disk.PowerOnHoursFormatted);
                        dIdx++;
                    }
                }
                sb.AppendLine();
                sb.AppendLine("    • Logical Volumes:");
                foreach (var drv in specs.LogicalDrives)
                {
                    sb.AppendLine($"      [{drv.Letter}] {drv.VolumeLabel} ({drv.FileSystem}) - Total: {drv.FormattedTotal}, Free: {drv.FormattedFree} ({drv.UsedPercent.ToString("0.0", CultureInfo.InvariantCulture)}% used)");
                }
                sb.AppendLine();

                sb.AppendLine("[5] MOTHERBOARD & FIRMWARE");
                sb.AppendLine("    • Board Model      : " + specs.MotherboardManufacturer + " " + specs.MotherboardModel);
                sb.AppendLine("    • BIOS Version     : " + specs.BiosVersion + " (" + specs.BiosReleaseDate + ")");
                sb.AppendLine();

                sb.AppendLine("[6] OPERATING SYSTEM & LICENSING");
                sb.AppendLine("    • Windows Edition  : " + specs.OsName + " (" + specs.OsArchitecture + ") Build " + specs.OsBuild);
                sb.AppendLine("    • License State    : " + (specs.Activation?.StatusText ?? "Active"));
                sb.AppendLine("    • Install Date     : " + specs.OsInstallDate);
                sb.AppendLine("    • System Uptime    : " + specs.OsUptimeFormatted);
                if (specs.HasBattery)
                {
                    sb.AppendLine("    • Battery Health   : " + specs.BatteryHealthPercent + "% (" + specs.BatteryCycleCount + " cycles)");
                }
                sb.AppendLine();
            }

            sb.AppendLine("================================================================================");
            sb.AppendLine("                          END OF SYSTEM SPECIFICATION                           ");
            sb.AppendLine("================================================================================");
            return sb.ToString();
        }

        public static string BuildDiagnosticsText()
        {
            Assembly asm = typeof(ReportExportService).Assembly;
            string version = GetVersion(asm);
            string? builtAt = null;
            try
            {
                string? loc = asm.Location;
                if (!string.IsNullOrEmpty(loc)) builtAt = File.GetLastWriteTime(loc).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch (Exception ex) { CcLog.Ignore(ex, "Diagnostics.BuiltAt"); }

            var sb = new StringBuilder();
            sb.AppendLine("=== CleanC — Diagnostics ===");
            sb.AppendLine($"App Version   : {version}");
            sb.AppendLine($"Build Time    : {builtAt ?? "-"}");
            sb.AppendLine($"Framework     : {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($".NET Runtime  : {Environment.Version}");
            sb.AppendLine($"OS            : {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            sb.AppendLine($"Process Arch  : {RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($"CPU Count     : {Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"64-bit OS     : {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"Settings File : {SettingsService.FilePath}");
            sb.AppendLine($"Log Directory : {CcLog.LogDirectory}");
            sb.AppendLine($"LocalAppData  : {Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}");
            sb.AppendLine($"Time Zone     : {TimeZoneInfo.Local.Id}");
            sb.AppendLine($"Timestamp     : {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");
            sb.AppendLine("============================");
            return sb.ToString();
        }

        public static string GetVersion(Assembly asm)
        {
            try
            {
                string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (string.IsNullOrWhiteSpace(info))
                    info = asm.GetName().Version?.ToString();
                if (string.IsNullOrWhiteSpace(info)) return "—";

                int plus = info.IndexOf('+');
                if (plus > 0) info = info.Substring(0, plus);
                return info.Trim();
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "Diagnostics.Version");
                return "—";
            }
        }

        public static void WriteUtf8Bom(string path, string content)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path", nameof(path));

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content ?? string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            File.Move(tmp, path, overwrite: true);
        }
    }
}
