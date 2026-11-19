using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CpaTaskbar
{
    public static class AggregationTests
    {
        static void Check(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
        }
        static Snapshot Example()
        {
            long now = J.Now;
            var state = new Snapshot();
            for (int i = 0; i < 2; i++)
            {
                var account = new Account { Id = "summary-" + i, AccountId = "account-" + i, Provider = "codex", Label = "示例账号 " + (i + 1) };
                state.Accounts.Add(account);
                state.Quotas[account.Id] = new QuotaState
                {
                    UpdatedAt = now,
                    Windows = new List<QuotaWindow>{
     new QuotaWindow{Id="5h",Label="5小时",Duration=18000,Used=i==0?20:60,ResetAt=now+(i==0?900000:7200000)},
     new QuotaWindow{Id="week",Label="一周",Duration=604800,Used=i==0?40:80,ResetAt=now+(i==0?3:1)*86400000}
    }
                };
            }
            return state;
        }
        static AppConfig Config()
        {
            return new AppConfig { CpaUrl = "https://cpa.example.test", CpaSecret = AppConfig.Protect("fixture-key"), FocusId = "summary-0", SelectedIds = new List<string> { "summary-0", "summary-1" }, QuotaDisplayMode = "average", ResetTimeMode = "countdown", ColorMode = "background", Theme = "dark", Width = 420, PhysicalPixels = true };
        }
        static SummaryWindow Window(QuotaSummary summary, string id)
        {
            return summary.Quota.Windows.Cast<SummaryWindow>().Single(w => w.Id == id && w.Scope == "");
        }
        public static void Run(Action<string, Action> test)
        {
            test("多账号进度与显示比例一致，累加超过100时进度封顶", () =>
            {
                var state = Example();
                var c = Config();
                var w = Window(QuotaAggregation.Build(state, c), "5h");
                Check(w.Used == 40 && w.Count == 2 && w.Total == 2 && Look.Value(w, true, c) == "60%", "mean remaining");
                Check(QuotaAggregation.Caption(w, "5h余", c) == "均5h余[2/2]", "mean caption");
                c.ProgressStyle = "thin";
                using (var mean = Strip(state, c, 420, 96))
                {
                    c.QuotaDisplayMode = "sum";
                    Check(Look.Value(w, true, c) == "120%" && w.Used == 40, "sum remaining preserves true total");
                    using (var sum = Strip(state, c, 420, 96))
                    {
                        var track = QuotaColors.ProgressBounds(new Rectangle(0, -4, 420, 48));
                        int x = track.Left + track.Width * 3 / 4, y = track.Top + track.Height / 2;
                        Check(mean.GetPixel(x, y).ToArgb() != sum.GetPixel(x, y).ToArgb(), "mean 60% and sum capped at 100% must differ");
                    }
                }
                c.ShowUsed = true;
                Check(Look.Value(w, true, c) == "80%" && Look.DisplayPercent(w, c) == 80, "sum used");
                c.QuotaDisplayMode = "single";
                Check(Object.ReferenceEquals(QuotaAggregation.BarQuota(state, c), state.Quotas["summary-0"]), "legacy single account");
            });
            test("汇总的五小时与周重置各取最近值，个别未知时间保留说明", () =>
            {
                var state = Example();
                var c = Config();
                var summary = QuotaAggregation.Build(state, c);
                var h = Window(summary, "5h");
                var week = Window(summary, "week");
                Check(h.ResetAt == state.Quotas["summary-0"].Windows[0].ResetAt && week.ResetAt == state.Quotas["summary-1"].Windows[1].ResetAt, "independent earliest resets");
                Check(QuotaReset.Text(h, true, c, J.Now).StartsWith("近") && h.KnownResets == 2, "nearest indicator");
                state.Quotas["summary-0"].Windows[0].ResetAt = null;
                h = Window(QuotaAggregation.Build(state, c), "5h");
                Check(h.Count == 2 && h.KnownResets == 1 && h.ResetAt == state.Quotas["summary-1"].Windows[0].ResetAt, "unknown time still has valid percentage");
                Check(c.RefreshSeconds == 120, "poll interval changed");
            });
            test("汇总排除失败、陈旧、到期、缺失和非法额度", () =>
            {
                var c = Config();
                foreach (string kind in new[] { "failed", "stale", "expired", "missing", "nan", "infinity" })
                {
                    var state = Example();
                    var q = state.Quotas["summary-1"];
                    if (kind == "failed")
                        q.Error = "HTTP 401";
                    if (kind == "stale")
                        q.UpdatedAt = J.Now - c.RefreshSeconds * 2500 - 1000;
                    if (kind == "expired")
                        q.Windows[0].ResetAt = J.Now - 1;
                    if (kind == "missing")
                        q.Windows[0].Used = null;
                    if (kind == "nan")
                        q.Windows[0].Used = Double.NaN;
                    if (kind == "infinity")
                        q.Windows[0].Used = Double.PositiveInfinity;
                    var w = Window(QuotaAggregation.Build(state, c), "5h");
                    Check(w.Count == 1 && w.Total == 2 && Look.Value(w, true, c) == "80%", "invalid entry " + kind);
                }
                var empty = Example();
                empty.Quotas.Clear();
                var unknown = Window(QuotaAggregation.Build(empty, c), "5h");
                Check(unknown.Count == 0 && Look.Value(unknown, true, c) == "—", "missing is not zero");
            });
            test("汇总按渠道、模型和周期隔离，重复账号优先使用新鲜记录", () =>
            {
                var state = Example();
                var c = Config();
                var other = new Account { Id = "other", Provider = "claude", Label = "other" };
                state.Accounts.Add(other);
                c.SelectedIds.Add(other.Id);
                state.Quotas[other.Id] = state.Quotas["summary-0"];
                Check(QuotaAggregation.Build(state, c).Accounts.Count == 2, "cross provider mixed");
                state.Accounts[1].AccountId = state.Accounts[0].AccountId;
                state.Quotas["summary-0"].Error = "failed";
                var summary = QuotaAggregation.Build(state, c);
                Check(summary.Accounts.Single().Id == "summary-1" && Window(summary, "5h").Used == 60, "duplicate chose failed quota");
                state = Example();
                state.Quotas["summary-1"].Windows[0].Scope = "opus";
                summary = QuotaAggregation.Build(state, c);
                Check(Window(summary, "5h").Count == 1 && summary.Quota.Windows.Single(w => w.Scope == "opus").Used == 60, "model scopes mixed");
                state = Example();
                state.Quotas["summary-1"].Windows[0].Duration = 36000;
                Check(QuotaAggregation.Build(state, c).Quota.Windows.Count(w => w.Id == "5h") == 2, "durations mixed");
                state = Example();
                state.Accounts.ForEach(a => a.Provider = "antigravity");
                c.Model = "week";
                summary = QuotaAggregation.Build(state, c);
                Check(Look.BarWindows(summary.Quota, c, summary.Accounts[0]).Single().Id == "week", "model filter ignored");
            });
            test("重置到期后任务栏与详情自动移除旧值并切换最近时间", () =>
            {
                var state = Example();
                var c = Config();
                using (var strip = new QuotaStrip(c))
                using (var form = new DetailsForm())
                {
                    var handle = strip.Handle;
                    strip.RefreshState(state);
                    form.UpdateData(state, c);
                    string old = QuotaAggregation.Build(state, c).Key;
                    int invalidated = 0, presented = strip.PresentationCount;
                    strip.Invalidated += (s, e) => invalidated++;
                    state.Quotas["summary-0"].Windows[0].ResetAt = J.Now - 1;
                    strip.RefreshClock();
                    form.Canvas.RefreshClock();
                    var summary = QuotaAggregation.Build(state, c);
                    Check(summary.Key != old && Window(summary, "5h").Count == 1 && Window(summary, "5h").ResetAt == state.Quotas["summary-1"].Windows[0].ResetAt, "expired account retained");
                    Check((invalidated > 0 || strip.PresentationCount > presented) && form.Canvas.Controls.Find("summaryValue", true).Any(x => x.Text.Contains("1/2")), "clock did not refresh display");
                    Check(form.Canvas.Controls.Find("summaryMemberReset", true).Length == 4, "individual resets lost");
                }
            });
            test("汇总设置保存和预览一致，关闭轮播且不重新勾选当前账号", () =>
            {
                var c = Config();
                c.Rotate = true;
                var state = Example();
                using (var service = new QuotaService(c))
                using (var form = new SettingsForm(c, service, state))
                {
                    form.Opacity = 0;
                    form.Show();
                    Application.DoEvents();
                    var mode = (ComboBox)form.Controls.Find("quotaDisplayMode", true).Single();
                    var accounts = (CheckedListBox)typeof(SettingsForm).GetField("accounts", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    var rotate = (CheckBox)typeof(SettingsForm).GetField("rotate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    var draftMethod = typeof(SettingsForm).GetMethod("Draft", BindingFlags.Instance | BindingFlags.NonPublic);
                    mode.SelectedIndex = 2;
                    accounts.SetItemChecked(0, false);
                    Application.DoEvents();
                    var draft = (AppConfig)draftMethod.Invoke(form, new object[] { true });
                    var preview = (StripPreview)form.Controls.Find("quotaColorPreview", true).Single();
                    Check(draft.QuotaDisplayMode == "sum" && !draft.Rotate && !rotate.Enabled, "mode and rotation");
                    Check(draft.SelectedIds.SequenceEqual(new[] { "summary-1" }) && preview.Config.SelectedIds.SequenceEqual(draft.SelectedIds) && draft.FocusId == "summary-1" && preview.Config.FocusId == draft.FocusId, "selection mismatch");
                    mode.SelectedIndex = 0;
                    Check(rotate.Enabled, "single mode rotation disabled");
                }
                var legacy = new AppConfig { QuotaDisplayMode = "invalid" };
                legacy.NormalizeAppearance();
                Check(legacy.QuotaDisplayMode == "single", "legacy fallback");
            });
            test("汇总详情窄窗口保留完整文字与滚动布局", () =>
            {
                var state = Example();
                var c = Config();
                c.QuotaDisplayMode = "sum";
                using (var form = new DetailsForm())
                {
                    form.Opacity = 0;
                    form.UpdateData(state, c);
                    form.Show();
                    form.MinimumSize = Size.Empty;
                    form.ClientSize = new Size(480, 650);
                    Application.DoEvents();
                    foreach (var label in All(form.Canvas).OfType<Label>())
                        Check(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, "summary label clipped: " + label.Name);
                    Check(form.Canvas.Controls.Find("summaryReset", true).Length == 2 && form.Canvas.Controls.Find("currentAccountHeading", true).Length == 1, "summary structure");
                }
            });
        }
        static IEnumerable<Control> All(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var next in All(child))
                    yield return next;
            }
        }
        static Bitmap Strip(Snapshot state, AppConfig config, int width, int height)
        {
            var image = new Bitmap(width, height);
            using (var g = Graphics.FromImage(image))
                Look.Strip(g, new Rectangle(0, 0, width, height), state, config);
            return image;
        }
        public static void Render(string folder)
        {
            var state = Example();
            var c = Config();
            using (var image = new Bitmap(800, 330))
            using (var g = Graphics.FromImage(image))
            using (var title = Look.Font(14, FontStyle.Bold))
            using (var text = Look.Font(10))
            {
                g.Clear(Color.FromArgb(246, 248, 252));
                Look.Text(g, "多账号汇总 · 示例数据", title, Look.Ink, new Rectangle(20, 10, 760, 40));
                Look.Text(g, "账号1：剩余80% / 60%；账号2：剩余40% / 20%", text, Look.Muted, new Rectangle(20, 55, 760, 40));
                for (int i = 0; i < 2; i++)
                {
                    c.QuotaDisplayMode = i == 0 ? "average" : "sum";
                    Look.Text(g, i == 0 ? "平均百分比" : "累加百分比", text, Look.Ink, new Rectangle(20, 115 + i * 110, 200, 96));
                    using (var bar = Strip(state, c, 480, 96))
                        g.DrawImageUnscaled(bar, 250, 115 + i * 110);
                }
                image.Save(Path.Combine(folder, "aggregation-modes.png"));
            }
            using (var service = new QuotaService(c))
            using (var form = new SettingsForm(c, service, state))
            {
                form.Opacity = 0;
                form.Show();
                form.Controls.OfType<TabControl>().Single().SelectedIndex = 1;
                Application.DoEvents();
                using (var image = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    image.Save(Path.Combine(folder, "settings-aggregation.png"));
                }
            }
            using (var form = new DetailsForm())
            {
                form.Opacity = 0;
                form.UpdateData(state, c);
                form.Show();
                form.MinimumSize = Size.Empty;
                form.ClientSize = new Size(720, 1000);
                Application.DoEvents();
                ((Panel)form.Controls.Find("detailsScroll", true).Single()).AutoScrollPosition = Point.Empty;
                Application.DoEvents();
                using (var image = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    image.Save(Path.Combine(folder, "details-aggregation.png"));
                }
            }
        }
    }
}
