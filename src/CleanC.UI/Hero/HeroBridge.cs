// src/CleanC.UI/Hero/HeroBridge.cs
// قرارداد پیام بین CleanC.hero.html (داخل WebView2) و میزبان C#.
//
// سمت JS → C#:  window.chrome.webview.postMessage({type:'...', ...})
// سمت C# → JS:  CoreWebView2.PostWebMessageAsJson(json)  که سمت JS به صورت شیء روی e.data می‌آید.
//
// دو نوع پیام داریم:
//   1) اطلاعاتی (fire-and-forget): update / theme / settings / borderline.result
//   2) RPC: JS یک شماره می‌فرستد ({type:'run.step', id:7, i:3}) و C# با
//      {type:'rpc', id:7, ok:true, result:{...}} جواب می‌دهد تا promise سمت JS resolve شود.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using CleanC.Core.Diagnostics;

namespace CleanC.UI.Hero
{
    /// <summary>پیام خوانده‌شده از JS — مقدارهای مشترک در یک جا جمع شده تا dispatch ساده بماند.</summary>
    public sealed class HeroMessage
    {
        public string Type { get; init; } = "";

        /// <summary>شناسهٔ RPC؛ فقط وقتی مقدار دارد که JS منتظر جواب مانده باشد.</summary>
        public int? RpcId { get; init; }

        /// <summary>ایندکس مرحلهٔ ۱۴تایی (فقط <c>run.step</c>).</summary>
        public int StepIndex { get; init; }

        /// <summary>ایندکس‌های انتخاب‌شده در دیالوگ برنامه‌های مرزی (فقط مرحلهٔ ۱۱).</summary>
        public int[] Selected { get; init; } = Array.Empty<int>();

        /// <summary>کلید چیپ پیشنهادی (پیام <c>chip</c>).</summary>
        public string? Key { get; init; }

        /// <summary>کلید اکشن سریع: fixproxy | ram | browser | cpu (پیام <c>quick</c>).</summary>
        public string? Action { get; init; }

        /// <summary>مدل دفترچهٔ کشفیات (پیام <c>codex.open</c>).</summary>
        public JsonElement? Data { get; init; }

        /// <summary>لیست برگشتی دیالوگ مرزی از میزبان (پیام <c>borderline.confirm</c>).</summary>
        public List<HeroBorderlineItem> Borderline { get; init; } = new();

        public static HeroMessage? Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                string type = ReadString(root, "type") ?? "";
                if (type.Length == 0) return null;

                int? rpcId = ReadInt(root, "id");
                int step = ReadInt(root, "i") ?? 0;

                return new HeroMessage
                {
                    Type = type,
                    RpcId = rpcId,
                    StepIndex = step,
                    Selected = ReadIntArray(root, "sel"),
                    Key = ReadString(root, "k"),
                    Action = ReadString(root, "act"),
                    Data = ReadElement(root, "data"),
                    Borderline = ReadBorderline(root, "list")
                };
            }
            catch (Exception ex)
            {
                CcLog.Ignore(ex, "HeroBridge.Parse");
                return null;
            }
        }

        private static string? ReadString(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static int? ReadInt(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v)) return null;
            return v.ValueKind switch
            {
                JsonValueKind.Number => v.GetInt32(),
                JsonValueKind.String when int.TryParse(v.GetString(), out var n) => n,
                _ => null
            };
        }

        private static int[] ReadIntArray(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return Array.Empty<int>();
            var list = new List<int>();
            foreach (var item in v.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number) list.Add(item.GetInt32());
            }
            return list.ToArray();
        }

        private static JsonElement? ReadElement(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) ? v.Clone() : null;

        private static List<HeroBorderlineItem> ReadBorderline(JsonElement e, string name)
        {
            var result = new List<HeroBorderlineItem>();
            if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return result;
            foreach (var row in v.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array) continue;
                var cells = row.EnumerateArray();
                string a = "", b = "";
                int idx = 0;
                foreach (var cell in cells)
                {
                    if (cell.ValueKind != JsonValueKind.String) continue;
                    if (idx == 0) a = cell.GetString() ?? "";
                    else if (idx == 1) b = cell.GetString() ?? "";
                    idx++;
                }
                if (a.Length > 0) result.Add(new HeroBorderlineItem { Name = a, Size = b });
            }
            return result;
        }
    }

    /// <summary>ساخت پیام‌های خروجی (C# → JS).</summary>
    public static class HeroOutbound
    {
        private static readonly JsonSerializerOptions s_opts = new()
        {
            // نام فیلدها در DTOها از قبل با JsonPropertyName قفل شده؛ camelCase فقط برای fallback
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string State(HeroState state) => Wrap("host.update", "state", state);
        public static string Settings(HeroCharacterSettings settings) => Wrap("settings", "settings", settings);
        public static string Theme(HeroThemeMessage theme) => Wrap("theme", "theme", theme);
        public static string BorderlineResult(int[] selected) => Wrap("borderline.result", "sel", selected);

        public static string RpcOk(int id, object? result)
            => JsonSerializer.Serialize(new HeroRpc { Type = "rpc", Id = id, Ok = true, Result = result }, s_opts);

        public static string RpcError(int id, string error)
            => JsonSerializer.Serialize(new HeroRpc { Type = "rpc", Id = id, Ok = false, Error = error }, s_opts);

        /// <summary>
        /// پیام با کلید مشخص: <c>{type:'...', [key]: payload}</c>.
        /// سمت JS هر پیام را با <c>d.type</c> تشخیص می‌دهد و payload را از همان کلید برمی‌دارد؛
        /// یکنواختی این قالب باعث می‌شود افزودن پیام جدید فقط یک خط باشد.
        /// </summary>
        private static string Wrap(string type, string key, object payload)
            => JsonSerializer.Serialize(new Dictionary<string, object?> { ["type"] = type, [key] = payload }, s_opts);

        private sealed class HeroRpc
        {
            [JsonPropertyName("type")] public string Type { get; set; } = "rpc";
            [JsonPropertyName("id")] public int Id { get; set; }
            [JsonPropertyName("ok")] public bool Ok { get; set; }
            [JsonPropertyName("result")] public object? Result { get; set; }
            [JsonPropertyName("error")] public string? Error { get; set; }
        }
    }
}
