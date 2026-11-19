using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CpaTaskbar
{
    /// <summary>Flat progress bar for the details popup.</summary>
    sealed class QuotaProgress : Control
    {
        readonly double percent;
        readonly Color color;

        public QuotaProgress(double value, Color fill)
        {
            percent = value;
            color = fill;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(234, 239, 246);
            AccessibleRole = AccessibleRole.ProgressBar;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            float width = (float)(ClientSize.Width * Math.Max(0, Math.Min(100, percent)) / 100);
            using (var brush = new SolidBrush(color))
                e.Graphics.FillRectangle(brush, 0, 0, width, ClientSize.Height);
        }
    }

    /// <summary>Table layout that wraps labels to their column width (re-laying out until stable).</summary>
    public class DetailsLayout : TableLayoutPanel
    {
        bool fitting;

        public static int Dpi(Control control)
        {
            try
            {
                if (control.IsHandleCreated)
                {
                    int dpi = (int)Native.GetDpiForWindow(control.Handle);
                    if (dpi > 0)
                        return dpi;
                }
                using (var graphics = control.CreateGraphics())
                    return (int)Math.Round(graphics.DpiY);
            }
            catch
            {
                return 96;
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (fitting)
                return;
            fitting = true;
            try
            {
                for (int pass = 0; pass < 3; pass++)
                {
                    if (!WrapLabels())
                        break;
                    base.OnLayout(e);
                }
            }
            finally
            {
                fitting = false;
            }
        }

        /// <summary>Limits labels in percent-sized columns to the column width. Returns true if any changed.</summary>
        bool WrapLabels()
        {
            bool changed = false;
            var columns = GetColumnWidths();
            foreach (Control child in Controls)
            {
                if (!(child is Label))
                    continue;
                int column = GetColumn(child);
                if (column < 0 || column >= columns.Length)
                    continue;
                if (column < ColumnStyles.Count && ColumnStyles[column].SizeType == SizeType.AutoSize)
                    continue;
                var maximum = new Size(Math.Max(1, columns[column] - child.Margin.Horizontal), 0);
                if (child.MaximumSize != maximum)
                {
                    child.MaximumSize = maximum;
                    changed = true;
                }
            }
            return changed;
        }
    }

    /// <summary>
    /// Content of the details popup: optional summary, the focused account's windows and the
    /// optional CPAMP history. Rebuilt on every update; reset-time labels tick via RefreshClock.
    /// </summary>
    public sealed class DetailsCanvas : DetailsLayout
    {
        public Snapshot State;
        public AppConfig Config;

        /// <summary>A label showing "prefix + reset description" that is refreshed every second.</summary>
        sealed class ResetLabel
        {
            public Label Label;
            public QuotaWindow Window;
            public QuotaState Quota;
            public string Prefix;
        }

        readonly Font heading = Look.Font(10, FontStyle.Bold), small = Look.Font(8.5f), body = Look.Font(9);
        readonly List<ResetLabel> resets = new List<ResetLabel>();
        string summaryKey = "";
        int layoutDpi = 96;

        public DetailsCanvas()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            AccessibleName = "当前账号额度和历史用量";
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Top;
            ColumnCount = 1;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        }

        public void UpdateData(Snapshot state, AppConfig config)
        {
            State = state;
            Config = config;
            layoutDpi = Dpi(this);
            SuspendLayout();
            try
            {
                Clear();
                Padding = new Padding(Pixels(12), Pixels(6), Pixels(12), Pixels(2));
                if (QuotaAggregation.Enabled(config))
                    AddSummary(QuotaAggregation.Build(state, config));
                var account = Look.Focus(state, config);
                AddAccount(state, account);
                if (config.CpampEnabled)
                    AddHistory(Look.History(state, account));
                RefreshClock();
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        /// <summary>Updates reset texts; rebuilds when the summary changed (e.g. an account's window expired).</summary>
        public void RefreshClock()
        {
            if (State == null || Config == null)
                return;
            if (QuotaAggregation.Enabled(Config) && summaryKey != QuotaAggregation.Build(State, Config).Key)
            {
                UpdateData(State, Config);
                return;
            }
            foreach (var reset in resets)
            {
                string text = reset.Prefix + QuotaReset.Description(reset.Window, Look.Fresh(reset.Quota, Config), J.Now);
                if (reset.Label.Text != text)
                    reset.Label.Text = text;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                heading.Dispose();
                small.Dispose();
                body.Dispose();
            }
            base.Dispose(disposing);
        }

        void Clear()
        {
            var old = Controls.Cast<Control>().ToArray();
            Controls.Clear();
            foreach (var control in old)
                control.Dispose();
            RowStyles.Clear();
            RowCount = 0;
            resets.Clear();
            summaryKey = "";
        }

        void AddSummary(QuotaSummary summary)
        {
            summaryKey = summary.Key;
            AddRow(TextLabel("summaryHeading", summary.Title(Config), heading, Look.Ink));
            string rule = Config.QuotaDisplayMode == "sum"
                ? "百分比为有效账号的累加值，可超过100%；进度条与显示比例一致，超过100%时封顶。"
                : "百分比为有效账号的平均值。";
            AddRow(TextLabel("summaryRule", rule + "不同套餐的百分比不等同于实际请求总量；仅汇总当前渠道的已选账号。", small, Look.Muted));
            foreach (var window in summary.Quota.Windows.Cast<SummaryWindow>())
            {
                string text = ScopedLabel(window) + "：" + Look.Value(window, true, Config) + "（有效 " + window.Count + "/" + window.Total + " 个账号）";
                AddRow(TextLabel("summaryValue", text, body, Look.Ink));
                AddReset("summaryReset", window, summary.Quota, "最近已知重置（时间已知 " + window.KnownResets + "/" + window.Count + "）：", 4);
            }
            AddRow(TextLabel("summaryMembers", "各账号额度与重置时间", body, Look.Ink));
            foreach (var account in summary.Accounts)
            {
                var quota = Look.Quota(State, account);
                AddRow(TextLabel("summaryMember", account.Label, body, Look.Ink));
                foreach (var window in Look.BarWindows(quota, Config, account))
                    AddReset("summaryMemberReset", window, quota, window.Label + " " + Look.Value(window, Look.Fresh(quota, Config), Config) + " · ", 4);
            }
            AddSeparator(8);
            AddRow(TextLabel("currentAccountHeading", "当前单个账号详情", heading, Look.Ink));
        }

        void AddAccount(Snapshot state, Account account)
        {
            var quota = Look.Quota(state, account);
            bool fresh = Look.Fresh(quota, Config);
            string plan = account == null ? "连接CPA后查看额度" : J.Provider(account.Provider) + (quota != null && quota.Plan != "" ? "  " + quota.Plan : "");
            AddRow(TextLabel("detailsPlan", plan, heading, Look.Ink));

            bool failed = state.Error != "" || quota != null && quota.Error != "";
            string status = state.Error != "" ? state.Error
                : quota != null && quota.Error != "" ? quota.Error
                : quota == null ? "正在读取额度…"
                : fresh ? "实时额度 · 更新于 " + DateTimeOffset.FromUnixTimeMilliseconds(quota.UpdatedAt).LocalDateTime.ToString("HH:mm:ss")
                : "数据已过期，等待刷新";
            AddRow(TextLabel("detailsStatus", status, small, failed ? Color.Firebrick : Look.Muted));

            if (quota != null)
            {
                foreach (var window in quota.Windows.Take(20))
                    AddWindow(window, quota, fresh);
            }
        }

        /// <summary>Name and value, a progress bar, then the reset time.</summary>
        void AddWindow(QuotaWindow window, QuotaState quota, bool fresh)
        {
            bool valid = fresh && window.Valid;
            Color color = Look.Status(valid ? window.Remaining : null, false);
            string value = Look.Value(window, fresh, Config);

            var row = Table(2);
            row.ColumnStyles.Clear();
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var valueLabel = TextLabel("quotaValue", value, heading, color);
            valueLabel.Dock = DockStyle.None;
            valueLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            row.Controls.Add(TextLabel("quotaLabel", ScopedLabel(window), body, Look.Ink), 0, 0);
            row.Controls.Add(valueLabel, 1, 0);
            AddRow(row);

            AddRow(new QuotaProgress(valid ? Look.DisplayPercent(window, Config) : 0, color)
            {
                Height = Pixels(5),
                Margin = new Padding(0, 0, 0, Pixels(4)),
                AccessibleName = window.Label + (Config.ShowUsed ? "已用额度 " : "剩余额度 ") + value,
            });
            AddReset("quotaReset", window, quota, "", 10);
        }

        void AddHistory(HistoryState history)
        {
            AddSeparator(4);
            AddRow(TextLabel("historyHeading", "历史用量", heading, Look.Ink));
            bool valid = Look.HistoryValid(history, Config);
            string[] names = { "今日Token", "今日请求", "本月Token", "本月请求" };
            double?[] values = history == null
                ? new double?[4]
                : new[] { history.TodayTokens, history.TodayCalls, history.MonthTokens, history.MonthCalls };
            var metrics = Table(2);
            for (int i = 0; i < names.Length; i++)
            {
                var card = Table(1);
                card.Margin = new Padding(0, 0, Pixels(8), Pixels(10));
                card.Controls.Add(TextLabel("historyMetric", names[i], small, Look.Muted), 0, 0);
                card.Controls.Add(TextLabel("historyValue", valid ? J.Compact(values[i]) : "—", heading, Look.Ink), 0, 1);
                metrics.Controls.Add(card, i % 2, i / 2);
            }
            AddRow(metrics);
            bool failed = history != null && history.Error != "";
            AddRow(TextLabel("historyStatus", failed ? history.Error : "CPAMP · 仅当前账号 · 按北京时间统计", small, failed ? Color.Firebrick : Look.Muted));
        }

        // ----- Building blocks -----

        int Pixels(int value)
        {
            return TaskbarMetrics.Scale(value, layoutDpi);
        }

        static string ScopedLabel(QuotaWindow window)
        {
            return (window.Scope != "" ? window.Scope + " / " : "") + window.Label;
        }

        Label TextLabel(string name, string text, Font font, Color color)
        {
            return new Label { Name = name, Text = text, Font = font, ForeColor = color, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, Pixels(4)), UseMnemonic = false };
        }

        void AddReset(string name, QuotaWindow window, QuotaState quota, string prefix, int bottomMargin)
        {
            var label = TextLabel(name, "", small, Look.Muted);
            label.Margin = new Padding(0, 0, 0, Pixels(bottomMargin));
            resets.Add(new ResetLabel { Label = label, Window = window, Quota = quota, Prefix = prefix });
            AddRow(label);
        }

        void AddSeparator(int topMargin)
        {
            AddRow(new Panel { Height = Pixels(1), BackColor = Look.Line, Margin = new Padding(0, Pixels(topMargin), 0, Pixels(14)) });
        }

        void AddRow(Control control)
        {
            int row = RowCount++;
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(control, 0, row);
        }

        TableLayoutPanel Table(int columns)
        {
            var table = new DetailsLayout { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = columns, Dock = DockStyle.Top, Margin = Padding.Empty };
            for (int i = 0; i < columns; i++)
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            return table;
        }
    }
}
