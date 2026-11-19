using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CpaTaskbar
{
    /// <summary>What the taskbar panel shows: the account, its quota and, in summary modes, the aggregate.</summary>
    public sealed class BarView
    {
        public Account Account;
        public QuotaState Quota;
        /// <summary>Null unless average/sum display is enabled.</summary>
        public QuotaSummary Summary;
    }

    /// <summary>Shared colours, data lookups and the taskbar panel painter.</summary>
    public static class Look
    {
        public static Color Ink = Color.FromArgb(34, 47, 68), Muted = Color.FromArgb(99, 112, 133), Line = Color.FromArgb(221, 227, 237), Blue = Color.FromArgb(48, 91, 207);

        const TextFormatFlags DefaultTextFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        const TextFormatFlags MeasureFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

        public static Font Font(float size = 9, FontStyle style = FontStyle.Regular)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        public static void Text(Graphics g, string text, Font font, Color color, Rectangle rect, TextFormatFlags flags = DefaultTextFlags)
        {
            TextRenderer.DrawText(g, text ?? "", font, rect, color, flags);
        }

        // ----- Theme -----

        static bool systemDark = true;
        static long systemDarkAt = long.MinValue;

        /// <summary>Whether to use dark colours. "auto" follows Windows, read from the registry at most every 5 seconds.</summary>
        public static bool Dark(AppConfig c)
        {
            if (c.Theme == "dark")
                return true;
            if (c.Theme == "light")
                return false;
            long now = J.Now;
            if (now - systemDarkAt < 5000)
                return systemDark;
            bool dark;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    dark = Convert.ToInt32(key == null ? 0 : key.GetValue("SystemUsesLightTheme", 0)) == 0;
            }
            catch
            {
                dark = true;
            }
            systemDark = dark;
            systemDarkAt = now;
            return dark;
        }

        /// <summary>Status colour by remaining percentage: red ≤10, amber ≤25, green otherwise.</summary>
        public static Color Status(double? remaining, bool dark)
        {
            if (!remaining.HasValue)
                return dark ? Color.FromArgb(181, 195, 213) : Muted;
            if (remaining <= 10)
                return dark ? Color.FromArgb(255, 149, 163) : Color.FromArgb(186, 45, 66);
            if (remaining <= 25)
                return dark ? Color.FromArgb(247, 205, 124) : Color.FromArgb(150, 93, 15);
            return dark ? Color.FromArgb(151, 227, 195) : Color.FromArgb(28, 119, 80);
        }

        public static Color StripSurface(bool dark, bool hover)
        {
            if (hover)
                return dark ? Color.FromArgb(45, 47, 51) : Color.FromArgb(244, 244, 241);
            return dark ? Color.FromArgb(34, 36, 40) : Color.FromArgb(249, 249, 246);
        }

        // ----- Data lookups -----

        /// <summary>Quota data younger than 2.5 refresh intervals without an error.</summary>
        public static bool Fresh(QuotaState q, AppConfig c)
        {
            return q != null && q.Error == "" && q.UpdatedAt > 0 && J.Now - q.UpdatedAt < c.RefreshSeconds * 2500;
        }

        /// <summary>History for today (Beijing time) younger than 2.5 history intervals without an error.</summary>
        public static bool HistoryValid(HistoryState h, AppConfig c)
        {
            return h != null && h.Error == "" && h.UpdatedAt > 0
                && h.TodayStart == QuotaParser.DayStart(J.Now, false)
                && J.Now - h.UpdatedAt < c.HistorySeconds * 2500;
        }

        /// <summary>The focused account, else the first selected, else the default account.</summary>
        public static Account Focus(Snapshot s, AppConfig c)
        {
            return s.Accounts.FirstOrDefault(a => a.Id == c.FocusId && a.Supported)
                ?? s.Accounts.FirstOrDefault(a => c.SelectedIds.Contains(a.Id) && a.Supported)
                ?? Account.PickDefault(s.Accounts);
        }

        public static QuotaState Quota(Snapshot s, Account a)
        {
            QuotaState q;
            return a != null && s.Quotas.TryGetValue(a.Id, out q) ? q : null;
        }

        public static HistoryState History(Snapshot s, Account a)
        {
            HistoryState h;
            return a != null && s.History.TryGetValue(a.Id, out h) ? h : null;
        }

        /// <summary>Resolves what the panel shows, building the aggregate once when summaries are enabled.</summary>
        public static BarView ResolveBar(Snapshot s, AppConfig c)
        {
            var view = new BarView { Account = Focus(s, c) };
            if (QuotaAggregation.Enabled(c))
            {
                view.Summary = QuotaAggregation.Build(s, c);
                view.Quota = view.Summary.Quota;
                view.Account = view.Summary.Accounts.FirstOrDefault() ?? view.Account;
            }
            else
                view.Quota = Quota(s, view.Account);
            return view;
        }

        /// <summary>
        /// Windows shown on the panel: 5-hour and weekly for Codex/Claude (placeholders when
        /// missing); for model-based providers, the matching window with the least remaining.
        /// </summary>
        public static List<QuotaWindow> BarWindows(QuotaState q, AppConfig c, Account a)
        {
            var windows = q == null ? new List<QuotaWindow>() : q.Windows;
            if (a == null || a.Provider == "codex" || a.Provider == "claude")
            {
                return new List<QuotaWindow>
                {
                    windows.FirstOrDefault(w => w.Id == "5h") ?? new QuotaWindow { Id = "5h", Label = QuotaParser.PeriodLabel("5h") },
                    windows.FirstOrDefault(w => w.Id == "week") ?? new QuotaWindow { Id = "week", Label = QuotaParser.PeriodLabel("week") },
                };
            }
            var matching = windows.Where(w => c.Model == "" || (w.Label + " " + w.Scope + " " + w.Id).IndexOf(c.Model, StringComparison.OrdinalIgnoreCase) >= 0);
            return matching.OrderBy(w => w.Remaining ?? 101).Take(1).ToList();
        }

        /// <summary>Displayed percentage (remaining or used); a sum summary multiplies the average by the valid count.</summary>
        public static double DisplayPercent(QuotaWindow w, AppConfig c)
        {
            var summary = w as SummaryWindow;
            int factor = summary != null && c.QuotaDisplayMode == "sum" ? summary.Count : 1;
            return Math.Round((c.ShowUsed ? w.Used.Value : w.Remaining.Value) * factor);
        }

        public static string Value(QuotaWindow w, bool fresh, AppConfig c)
        {
            return !fresh || w == null || !w.Valid ? "—" : DisplayPercent(w, c).ToString() + "%";
        }

        // ----- Panel fonts (cached; field names are checked by the tests) -----

        static Font stripLabel, stripNumber, stripTime;
        static float stripScale;
        static bool stripThick;

        public static void ReleaseStripFonts()
        {
            if (stripLabel != null)
                stripLabel.Dispose();
            if (stripNumber != null)
                stripNumber.Dispose();
            if (stripTime != null)
                stripTime.Dispose();
            stripLabel = stripNumber = stripTime = null;
        }

        /// <summary>Thick style: Segoe UI labels; thin style: bold Microsoft YaHei UI labels. Numbers are bold Segoe UI.</summary>
        static void StripFonts(float scale, bool thick, out Font label, out Font number, out Font time)
        {
            if (stripLabel == null || stripThick != thick || Math.Abs(stripScale - scale) > .001f)
            {
                ReleaseStripFonts();
                stripScale = scale;
                stripThick = thick;
                stripLabel = thick
                    ? new Font("Segoe UI", Math.Max(10, 11 * scale), FontStyle.Regular, GraphicsUnit.Pixel)
                    : new Font("Microsoft YaHei UI", Math.Max(10, 10.5f * scale), FontStyle.Bold, GraphicsUnit.Pixel);
                stripNumber = new Font("Segoe UI", Math.Max(11, 13 * scale), FontStyle.Bold, GraphicsUnit.Pixel);
                stripTime = thick
                    ? (Font)stripLabel.Clone()
                    : new Font("Microsoft YaHei UI", Math.Max(9, 9.5f * scale), FontStyle.Bold, GraphicsUnit.Pixel);
            }
            label = stripLabel;
            number = stripNumber;
            time = stripTime;
        }

        // ----- Panel painter -----

        /// <summary>Everything the row painters need for one paint pass.</summary>
        sealed class StripPaint
        {
            public Graphics G;
            public AppConfig Config;
            public bool Dark, Fresh;
            public Color Surface, Foreground, Muted;
            public Font Label, Number, Time;
            public int Inset, ResetWidth;
        }

        /// <summary>Paints the taskbar panel. With a transparent background the surface is alpha 1/255.</summary>
        public static void Strip(Graphics g, Rectangle rect, Snapshot s, AppConfig c, bool hover = false)
        {
            bool dark = Dark(c);
            var paint = new StripPaint
            {
                G = g,
                Config = c,
                Dark = dark,
                Surface = StripSurface(dark, hover),
                Foreground = dark ? Color.FromArgb(244, 245, 247) : Color.FromArgb(20, 23, 27),
                Muted = dark ? Color.FromArgb(211, 215, 221) : Color.FromArgb(52, 55, 60),
            };
            // Fully transparent pixels pass mouse input through layered windows. A 1/255
            // alpha surface stays visually transparent while keeping the entire panel clickable.
            g.Clear(c.TransparentBackground ? Color.FromArgb(1, 0, 0, 0) : paint.Surface);

            // Lift the content slightly to leave room below the progress line (not needed for full-row fills).
            int lift = QuotaColors.ThickBackground(c) ? 0 : Math.Max(2, Math.Min(6, rect.Height / 24));
            var view = ResolveBar(s, c);
            paint.Fresh = Fresh(view.Quota, c);

            float scale = Math.Max(.8f, rect.Height / 48f) * c.FontSize / 9f;
            bool thick = QuotaColors.Thick(c);
            float fontScale = thick ? Math.Max(.8f, rect.Height / 40f) * c.FontSize / 9f : scale;
            StripFonts(fontScale, thick, out paint.Label, out paint.Number, out paint.Time);
            paint.Inset = Math.Max(7, (int)(9 * scale));

            int historyWidth = c.HistoryInBar && c.CpampEnabled ? (int)(74 * scale) : 0;
            int quotaWidth = Math.Max(1, rect.Width - historyWidth);
            int half = rect.Height / 2;
            int textWidth = Math.Max(1, quotaWidth - 2 * paint.Inset);

            if (view.Account == null || s.Error != "")
            {
                string status = s.Error != "" ? "连接异常" : String.IsNullOrEmpty(c.CpaSecret) ? "未配置" : "连接中";
                DrawText(paint, status, paint.Number, paint.Foreground, new Rectangle(paint.Inset, 1 - lift, textWidth, half));
                DrawText(paint, "点击查看", paint.Label, paint.Muted, new Rectangle(paint.Inset, half - lift, textWidth, half));
                return;
            }

            var windows = BarWindows(view.Quota, c, view.Account);
            paint.ResetWidth = QuotaReset.Enabled(c)
                ? windows.Select(w => TextRenderer.MeasureText(QuotaReset.Text(w, paint.Fresh, c, J.Now), paint.Time, Size.Empty, MeasureFlags).Width + 2).DefaultIfEmpty(0).Max()
                : 0;

            bool singleLine = c.SingleLine || rect.Height < 35;
            if (singleLine && windows.Count > 0)
            {
                // Side by side: "5h", "周", or 用/余 for model windows.
                for (int i = 0; i < windows.Count; i++)
                {
                    int left = i * quotaWidth / windows.Count, right = (i + 1) * quotaWidth / windows.Count;
                    var window = windows[i];
                    string caption = window.Id == "5h" ? "5h" : window.Id == "week" ? "周" : c.ShowUsed ? "用" : "余";
                    DrawRow(paint, new Rectangle(left, -lift, right - left, rect.Height), window, QuotaAggregation.Caption(window, caption, c));
                }
            }
            else if (windows.Count > 1)
            {
                // Two rows: 5-hour on top, weekly below.
                for (int i = 0; i < 2; i++)
                {
                    int top = i * half - lift, bottom = (i + 1) * rect.Height / 2 - lift;
                    var window = windows[i];
                    string caption = (window.Id == "5h" ? "5h" : "周") + (c.ShowUsed ? "用" : "余");
                    DrawRow(paint, new Rectangle(0, top, quotaWidth, bottom - top), window, QuotaAggregation.Caption(window, caption, c));
                }
            }
            else
                DrawModelWindow(paint, rect, windows.FirstOrDefault(), quotaWidth, lift);

            if (historyWidth > 0)
            {
                using (var pen = new Pen(dark ? Color.FromArgb(64, 76, 92) : Line))
                    g.DrawLine(pen, quotaWidth, 8 - lift, quotaWidth, rect.Height - 8 - lift);
                var history = History(s, Focus(s, c));
                int columnWidth = Math.Max(1, historyWidth - paint.Inset * 2);
                string caption = QuotaAggregation.Enabled(c) ? "当前Token" : "今日Token";
                DrawText(paint, caption, paint.Label, paint.Muted, new Rectangle(quotaWidth + paint.Inset, -lift, columnWidth, half));
                DrawText(paint, HistoryValid(history, c) ? J.Compact(history.TodayTokens) : "—", paint.Number, paint.Foreground, new Rectangle(quotaWidth + paint.Inset, half - lift, columnWidth, half));
            }
        }

        /// <summary>A single model window: value on the top row, the model name below.</summary>
        static void DrawModelWindow(StripPaint paint, Rectangle rect, QuotaWindow window, int quotaWidth, int lift)
        {
            var c = paint.Config;
            var area = new Rectangle(0, -lift, quotaWidth, rect.Height);
            QuotaColors.PaintProgress(paint.G, area, window, paint.Fresh, c, paint.Surface, paint.Dark);
            Color labelColor = LabelColor(paint, window);
            Color numberColor = QuotaColors.NumberColor(window, paint.Fresh, c, paint.Dark, paint.Surface, window == null ? paint.Muted : paint.Foreground);
            int half = rect.Height / 2;
            DrawRowText(paint, new Rectangle(0, -lift, quotaWidth, half), window, QuotaAggregation.Caption(window, c.ShowUsed ? "已用" : "余量", c), labelColor, numberColor);
            DrawText(paint, window == null ? "请选择模型" : window.Label, paint.Label, labelColor, new Rectangle(paint.Inset, half - lift, Math.Max(1, quotaWidth - 2 * paint.Inset), half));
        }

        /// <summary>One quota row: progress fill, then caption, value and optional reset time.</summary>
        static void DrawRow(StripPaint paint, Rectangle area, QuotaWindow window, string caption)
        {
            var c = paint.Config;
            QuotaColors.PaintProgress(paint.G, area, window, paint.Fresh, c, paint.Surface, paint.Dark);
            Color numberColor = QuotaColors.NumberColor(window, paint.Fresh, c, paint.Dark, paint.Surface, paint.Foreground);
            var textArea = area;
            // The thin progress line sits at the bottom of the row; keep the text above it.
            if (QuotaColors.HasBackground(c) && !QuotaColors.Thick(c))
                textArea.Height = Math.Max(1, QuotaColors.ProgressBounds(area).Top - area.Top - 2);
            DrawRowText(paint, textArea, window, caption, LabelColor(paint, window), numberColor);
        }

        /// <summary>Muted label colour, adjusted for contrast when it sits on a thick progress fill.</summary>
        static Color LabelColor(StripPaint paint, QuotaWindow window)
        {
            if (!QuotaColors.ThickBackground(paint.Config))
                return paint.Muted;
            Color fill = QuotaColors.ProgressSurface(paint.Surface, window, paint.Fresh, paint.Config, paint.Dark);
            return QuotaColors.Readable(paint.Muted, paint.Surface, fill, paint.Dark);
        }

        /// <summary>Caption on the left, value right-aligned to a fixed "100%" width, reset time at the right edge.</summary>
        static void DrawRowText(StripPaint paint, Rectangle area, QuotaWindow window, string caption, Color labelColor, Color numberColor)
        {
            var c = paint.Config;
            int inset = paint.Inset;
            string value = Value(window, paint.Fresh, c);
            int numberWidth = TextRenderer.MeasureText(value, paint.Number, Size.Empty, MeasureFlags).Width + 2;
            if (!(window is SummaryWindow) || c.QuotaDisplayMode != "sum")
                numberWidth = Math.Max(numberWidth, TextRenderer.MeasureText("100%", paint.Number, Size.Empty, MeasureFlags).Width + 2);

            int timeWidth = 0, gap = 0;
            if (QuotaReset.Enabled(c))
            {
                int content = Math.Max(0, area.Width - 2 * inset);
                int captionWidth = TextRenderer.MeasureText(caption, paint.Label, Size.Empty, MeasureFlags).Width;
                timeWidth = Math.Min(paint.ResetWidth, Math.Max(0, content - captionWidth - numberWidth - 12));
                if (timeWidth > 0)
                    gap = Math.Max(8, inset);
            }
            int timeX = area.Right - inset - timeWidth;
            int numberX = Math.Max(area.Left + inset, timeX - gap - numberWidth);
            int labelWidth = Math.Max(1, numberX - area.Left - inset - 3);

            DrawText(paint, caption, paint.Label, labelColor, new Rectangle(area.Left + inset, area.Top, labelWidth, area.Height), DefaultTextFlags | TextFormatFlags.SingleLine);
            DrawText(paint, value, paint.Number, numberColor, new Rectangle(numberX, area.Top, Math.Max(1, timeX - gap - numberX), area.Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (timeWidth > 0)
            {
                var flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                DrawText(paint, QuotaReset.Text(window, paint.Fresh, c, J.Now), paint.Time, labelColor, new Rectangle(timeX, area.Top, timeWidth, area.Height), flags);
            }
        }

        static void DrawText(StripPaint paint, string text, Font font, Color color, Rectangle area, TextFormatFlags flags = DefaultTextFlags)
        {
            if (paint.Config.TransparentBackground)
                TransparentText.Draw(paint.G, text, font, color, area, flags);
            else
                Text(paint.G, text, font, color, area, flags);
        }
    }
}
