// src/CleanC.UI/Expedition/ExpeditionApi.cs
// dispatch پیام‌های بازی:
//   ready            → hydrate (تم/اکسنت/پروفایل/والپیپر/ذخیره‌شده)
//   rpc zone.scan    → ExpeditionService.ScanZoneDownloadsAsync (Zone 1 واقعی)
//   rpc zone.execute → ExpeditionService.ExecuteZoneBatchAsync (حذف امن پس از تأیید یک‌باره)
//   rpc state.get    → وضعیت ذخیره‌شده‌ی بازی
//   rpc state.save   → ذخیرهٔ وضعیت بازی
//   exit             → بستن بازی (سقوط نرم یا ESC داخل HTML)
//
// نکته‌ی امنیتی: zone.execute فقط مسیرهای داخل Downloads را می‌پذیرد (گاردِ سرویس در
// Core) و رفتار حذف همیشه از SafeDeleteService می‌گذرد — بازی هرگز مستقیم فایل حذف نمی‌کند
// و هیچ حذفی بدون تأیید end-of-run رخ نمی‌دهد (قرارداد طراحی).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;
using CleanC.Core.Services;

namespace CleanC.UI.Expedition
{
    public sealed class ExpeditionApi
    {
        private readonly MainWindow _window;

        public ExpeditionApi(MainWindow window) => _window = window;

        public async Task DispatchAsync(ExpeditionMessage msg)
        {
            switch (msg.Type)
            {
                case "ready":
                    _window.SendToExpedition(ExpeditionOutbound.Hydrate(BuildHydratePayload()));
                    break;

                case "exit":
                    _window.CloseExpedition();
                    break;

                case "rpc" when msg.RpcId.HasValue:
                    await DispatchRpcAsync(msg);
                    break;
            }
        }

        private async Task DispatchRpcAsync(ExpeditionMessage msg)
        {
            int id = msg.RpcId!.Value;
            try
            {
                object? result = msg.Api switch
                {
                    "zone.scan" => await ZoneScanAsync(ReadZoneId(msg.Data)),
                    "zone.execute" => await ZoneExecuteAsync(msg.Data),
                    "state.get" => ExpeditionContentService.LoadGameState(),
                    "state.save" => SaveState(msg.Data),
                    "sys.stat" => ExpeditionZonesService.GetSysStat(),
                    "file.inspect" => InspectFile(msg.Data),
                    "file.stage" => StageFile(msg.Data),
                    _ => null
                };

                if (result == null)
                {
                    _window.SendToExpedition(ExpeditionOutbound.RpcError(id, string.Format(LocalizationService.Get("API ناشناخته است: {0}"), msg.Api)));
                }
                else
                {
                    _window.SendToExpedition(ExpeditionOutbound.RpcOk(id, result));
                }
            }
            catch (Exception ex)
            {
                CcLog.Report("Expedition.Api." + msg.Api, ex);
                _window.SendToExpedition(ExpeditionOutbound.RpcError(id, ex.Message));
            }
        }

        // ================================================================
        // zone.scan
        // ================================================================

        private static string ReadZoneId(JsonElement? data)
        {
            if (data is JsonElement el && el.ValueKind == JsonValueKind.Object &&
                el.TryGetProperty("zoneId", out var z) && z.ValueKind == JsonValueKind.String)
                return z.GetString() ?? "downloads_dehleez";
            return "downloads_dehleez";
        }

        /// <summary>
        /// اسکن زون‌ها. Zone 1 (Downloads) از ExpeditionService؛ بقیه‌ی زون‌ها از
        /// ExpeditionZonesService (استارتاپ/رجیستری/تسک/پروسس/پوشه/تنظیمات).
        /// </summary>
        private static async Task<object> ZoneScanAsync(string zoneId)
        {
            if (zoneId is "downloads" or "downloads_dehleez")
            {
                var scan = await ExpeditionService.ScanZoneDownloadsAsync();

                var cases = scan.Cases.Select(c => new
                {
                    path = c.Path,
                    name = c.Name,
                    sizeBytes = c.SizeBytes,
                    lastWrite = c.LastWriteUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    bucket = (int)c.Bucket,
                    confidence = (int)c.Confidence,
                    evidence = c.Evidence.ToArray(),
                    pairName = c.PairName,
                    pairSize = c.PairSize,
                    isImage = c.IsImage
                }).ToArray();

                var ambient = scan.Ambient.Select(a => new
                {
                    name = a.Name,
                    sizeBytes = a.SizeBytes,
                    isImage = a.IsImage,
                    isVideo = a.IsVideo
                }).ToArray();

                return new
                {
                    zoneId = scan.ZoneId,
                    dir = scan.Dir,
                    scannedCount = scan.ScannedCount,
                    cases,
                    ambient
                };
            }

            // زون‌های عمومی — اسکن روی thread جدا تا UI نفس بکشد
            var gen = await Task.Run(() => ExpeditionZonesService.ScanZone(zoneId));
            return new
            {
                zoneId = gen.ZoneId,
                dir = gen.Dir,
                scannedCount = gen.ScannedCount,
                cases = gen.Cases.Select(c => new
                {
                    path = c.Path,
                    name = c.Name,
                    sizeBytes = c.SizeBytes,
                    lastWrite = c.LastWriteUtc == default
                        ? null
                        : c.LastWriteUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    bucket = -1,
                    confidence = c.Confidence,
                    evidence = Array.Empty<string>(),
                    kind = c.Kind,
                    detail = c.Detail,
                    data = c.Data
                }).ToArray(),
                ambient = Array.Empty<object>()
            };
        }

        // ================================================================
        // zone.execute
        // ================================================================

        /// <summary>
        /// پردازش end-of-run. مسیریابی بر اساس zoneId:
        ///   downloads → SafeDelete فایل‌ها | folders → SafeDelete پوشه‌ها
        ///   startup/tasks/registry → اجرای خاص همان زون در ExpeditionZonesService
        ///   procs/settings → مشاهده‌ای؛ چیزی اجرا نمی‌شود
        /// </summary>
        private async Task<object> ZoneExecuteAsync(JsonElement? data)
        {
            string zoneId = "downloads_dehleez";
            var paths = new List<string>();
            if (data is JsonElement pay && pay.ValueKind == JsonValueKind.Object)
            {
                if (pay.TryGetProperty("zoneId", out var z) && z.ValueKind == JsonValueKind.String)
                    zoneId = z.GetString() ?? zoneId;
                if (pay.TryGetProperty("paths", out var pathsEl) && pathsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in pathsEl.EnumerateArray())
                        if (row.ValueKind == JsonValueKind.String) paths.Add(row.GetString() ?? "");
                }
            }

            switch (zoneId)
            {
                case "startup":
                case "tasks":
                case "registry":
                case "folders":
                {
                    var r = await Task.Run(() => ExpeditionZonesService.ExecuteZone(zoneId, paths));
                    return new { performed = r.Performed, failed = r.Failed, moved = 0L, freed = r.FreedBytes, mode = SafeDeleteService.ModeToString(SafeDeleteService.DefaultMode) };
                }
                case "procs":
                case "settings":
                    return new { performed = 0, failed = 0, moved = 0L, freed = 0L, mode = "observe" };
                default:
                {
                    var result = await ExpeditionService.ExecuteZoneBatchAsync(paths);
                    return new
                    {
                        performed = result.SucceededCount,
                        failed = result.FailedCount,
                        moved = result.MovedBytes,
                        freed = result.FreedBytes,
                        mode = SafeDeleteService.ModeToString(SafeDeleteService.DefaultMode)
                    };
                }
            }
        }

        // ================================================================
        // file.inspect / file.stage
        // ================================================================

        private static object? InspectFile(JsonElement? data)
        {
            var path = ReadStringArg(data, "path");
            return path.Length == 0 ? null : ExpeditionZonesService.InspectFile(path);
        }

        private static object? StageFile(JsonElement? data)
        {
            if (data is not JsonElement pay || pay.ValueKind != JsonValueKind.Object ||
                !pay.TryGetProperty("path", out var p) || p.ValueKind != JsonValueKind.String)
                return new { failed = true };
            long size = 0;
            if (pay.TryGetProperty("sizeBytes", out var sz) && sz.ValueKind == JsonValueKind.Number)
                size = sz.GetInt64();
            return ExpeditionZonesService.StageFile(p.GetString() ?? "", size);
        }

        private static string ReadStringArg(JsonElement? data, string prop)
        {
            if (data is JsonElement el && el.ValueKind == JsonValueKind.Object &&
                el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? "";
            return "";
        }

        // ================================================================
        // state.save
        // ================================================================

        private object SaveState(JsonElement? data)
        {
            if (data is not JsonElement st || st.ValueKind != JsonValueKind.Object)
                return new { saved = false };

            bool saved = ExpeditionContentService.SaveGameState(st.GetRawText());
            return new { saved };
        }

        // ================================================================
        // hydrate
        // ================================================================

        internal object BuildHydratePayload()
        {
            var settings = _window.ExpeditionSettings;
            return new
            {
                theme = new
                {
                    mode = settings.Theme?.ToLowerInvariant() switch
                    {
                        "dark" => "dark",
                        "light" => "light",
                        _ => "system"
                    },
                    accent = GetAccentHex()
                },
                profile = new
                {
                    name = settings.CharacterUserName,
                    lang = LocalizationService.CurrentLanguage.Code,
                    calm = settings.CharacterCalm
                },
                wallpaper = new
                {
                    ok = ExpeditionContentService.PrepareWallpaper()
                },
                saved = ExpeditionContentService.LoadGameState(),
                host = new
                {
                    wall = ExpeditionContentService.WallHostName,
                    downloads = ExpeditionContentService.DownloadsHostName,
                    downloadsDir = ExpeditionService.DownloadsDir
                }
            };
        }

        private string GetAccentHex() => _window.GetWindowsAccentHex();
    }
}
