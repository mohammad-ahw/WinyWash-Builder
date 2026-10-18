// src/CleanC.UI/Hero/HeroHostApi.cs
// ترجمهٔ پیام‌های CleanC.hero.html به فراخوانی‌های MainWindow.
//
// هر پیامِ JS یکی از این دو حالت را دارد:
//   • RPC   — شمارهٔ id دارد و منتظر جواب است → باید حتماً RpcOk/RpcError بفرستیم تا
//             promise سمت JS resolve شود وگرنه حلقهٔ ۱۴ مرحله‌ای آن می‌ماند.
//   • ساده  — فقط اطلاع‌رسانی است (settings.request / chip / quick / run.abort / …).

using System;
using System.Linq;
using System.Threading.Tasks;
using CleanC.Core.Diagnostics;

namespace CleanC.UI.Hero
{
    public sealed class HeroHostApi
    {
        private readonly MainWindow _window;

        public HeroHostApi(MainWindow window) => _window = window;

        public async Task DispatchAsync(HeroMessage msg)
        {
            var id = msg.RpcId;
            try
            {
                switch (msg.Type)
                {
                    // ---------- دستهٔ ۱: پاسخ‌دار (RPC) ----------
                    case "run.step":
                        // نتیجهٔ {mb:...} را JS برای شمارندهٔ «آزادشده» لازم دارد؛ null هم پذیرفته است
                        ReplyOk(id, await _window.HeroRunStepAsync(msg.StepIndex, msg.Selected));
                        break;

                    case "run.borderline":
                        // سمت JS این لیست را آرایهٔ [نام, حجم] می‌خواند، نه شیء
                        var rows = (await _window.HeroGetBorderlineAsync())
                            .Select(x => new[] { x.Name, x.Size })
                            .ToArray();
                        ReplyOk(id, rows);
                        break;

                    case "run.scan":
                        await _window.HeroRescanAsync();
                        ReplyOk(id, new { ok = true });
                        break;

                    case "run.after":
                        ReplyOk(id, await _window.HeroAfterAsync());
                        break;

                    // ---------- دستهٔ ۲: ساده ----------
                    case "settings.request":
                        // سیگنال آمادگی صفحه؛ تنظیمات و تم را همان لحظه بفرست
                        _window.PushHeroSettings();
                        _window.PushHeroTheme();
                        _window.PushHeroState(force: true);
                        break;

                    case "run.abort":
                        _window.HeroAbortRun();
                        break;

                    case "borderline.confirm":
                        // دیالوگ نیتیو باز می‌شود و خودش borderline.result را می‌فرستد
                        await _window.ShowHeroBorderlineDialogAsync(msg.Borderline);
                        break;

                    case "codex.open":
                        await _window.ShowHeroCodexDialogAsync(msg.Data);
                        break;

                    case "chip":
                        _window.HandleHeroChip(msg.Key);
                        break;

                    case "quick":
                        await _window.HandleHeroQuickAsync(msg.Action);
                        break;

                    case "hero.snooze":
                    case "snooze":
                        _window.HandleHeroSnooze();
                        break;

                    case "js.error":
                        // خطای زمانِ اجرای اسکریپت هیرو → در لاگ ثبت می‌شود تا «کاراکتر لود
                        // نمی‌شود/هنگ می‌کند» بدون حدس و گمان عیب‌یابی شود
                        CcLog.Report("Hero.JSError", new InvalidOperationException(
                            "خطای اسکریپت هیرو: " + (msg.Data.HasValue ? msg.Data.Value.ToString() : "بدون جزئیات")));
                        break;

                    default:
                        var unknown = "پیام ناشناخته از هیرو: " + msg.Type;
                        CcLog.Report("Hero.HostApi", new InvalidOperationException(unknown));
                        ReplyErr(id, unknown);
                        break;
                }
            }
            catch (Exception ex)
            {
                CcLog.Report("Hero.HostApi." + msg.Type, ex);
                ReplyErr(id, ex.Message);
            }
        }

        private void ReplyOk(int? id, object? payload)
        {
            if (id is int n) _window.SendToHero(HeroOutbound.RpcOk(n, payload));
        }

        private void ReplyErr(int? id, string error)
        {
            if (id is int n) _window.SendToHero(HeroOutbound.RpcError(n, error));
        }
    }
}
