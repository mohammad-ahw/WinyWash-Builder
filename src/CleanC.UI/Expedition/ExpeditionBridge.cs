// src/CleanC.UI/Expedition/ExpeditionBridge.cs
// قرارداد پیام بین Winy.expedition.html و میزبان C# — همسو با HeroBridge.
//
// JS → C#:  window.chrome.webview.postMessage({type:'...', ...})
// C# → JS:  CoreWebView2.PostWebMessageAsJson(json)
//
// انواع پیام:
//   1) اطلاعاتی: exit / fire
//   2) RPC: {type:'rpc', id, api:'zone.scan'|'zone.execute'|'state.get'|'state.save', paths?, state?}
//      پاسخ: {type:'rpc', id, ok, result|error}
//   3) hydrate (C# → JS): تم/پروفایل/والپیپر/وضعیت ذخیره‌شده — فقط یک‌بار بعد از ready

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using CleanC.Core.Diagnostics;

namespace CleanC.UI.Expedition
{
    /// <summary>پیام خوانده‌شده از JS بازی.</summary>
    public sealed class ExpeditionMessage
    {
        public string Type { get; init; } = "";
        public int? RpcId { get; init; }

        /// <summary>نام تابع درخواستی در پیام‌های rpc (zone.scan و …).</summary>
        public string? Api { get; init; }

        /// <summary>مسیرهای فایل در zone.execute.</summary>
        public string[] Paths { get; init; } = Array.Empty<string>();

        /// <summary>هر payload دیگر (state.save و …).</summary>
        public JsonElement? Data { get; init; }

        public static ExpeditionMessage? Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                string type = ReadString(root, "type") ?? "";
                if (type.Length == 0) return null;

                return new ExpeditionMessage
                {
                    Type = type,
                    RpcId = ReadInt(root, "id"),
                    Api = ReadString(root, "api"),
                    Paths = ReadStringArray(root, "paths"),
                    Data = ReadElement(root, "state")
                };
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "ExpeditionBridge.Parse");
                return null;
            }
        }

        private static string? ReadString(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static int? ReadInt(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number)
            {
                if (v.TryGetInt32(out var asInt)) return asInt;
                try { return (int)Math.Round(v.GetDouble()); }
                catch (Exception ex) { CcLog.Ignore(ex, "ExpeditionBridge.ReadInt"); return null; }
            }
            return null;
        }

        private static string[] ReadStringArray(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();
            var list = new List<string>();
            foreach (var item in v.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString() ?? "");
            }
            return list.ToArray();
        }

        private static JsonElement? ReadElement(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) ? v.Clone() : null;
    }

    /// <summary>ساخت پیام‌های خروجی (C# → JS).</summary>
    public static class ExpeditionOutbound
    {
        private static readonly JsonSerializerOptions Opts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>پیام hydration کامل: تم، پروفایل، والپیپر و ذخیره‌شده.</summary>
        public static string Hydrate(object payload)
            => JsonSerializer.Serialize(new Dictionary<string, object?> { ["type"] = "hydrate", ["pay"] = payload }, Opts);

        /// <summary>اطلاع‌رسانی ساده (مانند HeroOutbound.Fire) برای بازخوردهای لحظه‌ای.</summary>
        public static string Fire(string id)
            => JsonSerializer.Serialize(new Dictionary<string, object?> { ["type"] = "fire", ["id"] = id }, Opts);

        public static string RpcOk(int id, object? result)
            => JsonSerializer.Serialize(new ExRpc { Type = "rpc", Id = id, Ok = true, Result = result }, Opts);

        public static string RpcError(int id, string error)
            => JsonSerializer.Serialize(new ExRpc { Type = "rpc", Id = id, Ok = false, Error = error }, Opts);

        private sealed class ExRpc
        {
            [JsonPropertyName("type")] public string Type { get; set; } = "rpc";
            [JsonPropertyName("id")] public int Id { get; set; }
            [JsonPropertyName("ok")] public bool Ok { get; set; }
            [JsonPropertyName("result")] public object? Result { get; set; }
            [JsonPropertyName("error")] public string? Error { get; set; }
        }
    }
}
