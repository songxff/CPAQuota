using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace CpaTaskbar
{
    /// <summary>
    /// The taskbar panel. With a transparent background it is a layered window whose bitmap is
    /// pushed with UpdateLayeredWindow; otherwise it paints normally with a rounded region. It never
    /// takes activation, so clicking it does not steal focus from other windows.
    /// </summary>
    public sealed class QuotaStrip : Form
    {
        public Snapshot State = new Snapshot();
        public AppConfig Config;
        public TaskbarDock Docking;
        public event Action Clicked;

        readonly ToolTip tip = new ToolTip { InitialDelay = 350, ReshowDelay = 150, AutoPopDelay = 10000 };
        bool hover;
        string clockKey = "", tooltipText = "";

        public QuotaStrip(AppConfig config)
        {
            Config = config;
            Text = "CPA 额度状态栏";
            AccessibleName = "CPA额度状态栏";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Size = new Size(config.Width, 48);
            Docking = new TaskbarDock(this);
            MouseEnter += (s, e) =>
            {
                hover = true;
                RefreshPresentation();
            };
            MouseLeave += (s, e) =>
            {
                hover = false;
                RefreshPresentation();
            };
            MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && Clicked != null)
                    Clicked();
            };
        }

        /// <summary>Time of the last layered bitmap update (diagnostics and tests).</summary>
        public long LastPresentedAt
        {
            get; private set;
        }

        /// <summary>Number of layered bitmap updates (diagnostics and tests).</summary>
        public int PresentationCount
        {
            get; private set;
        }

        protected override bool ShowWithoutActivation
        {
            get
            {
                return true;
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
                if (Config != null && Config.TransparentBackground)
                    cp.ExStyle |= Native.WS_EX_LAYERED;
                return cp;
            }
        }

        /// <summary>Applies new data and repaints; also refreshes the tooltip.</summary>
        public void RefreshState(Snapshot s)
        {
            if (IsHandleCreated)
            {
                // The transparency setting can change at runtime.
                int style = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
                int next = Config.TransparentBackground ? style | Native.WS_EX_LAYERED : style & ~Native.WS_EX_LAYERED;
                if (style != next)
                    Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, next);
                UpdateShape();
            }
            State = s;
            UpdateTooltip(s);
            clockKey = ClockKey();
            RefreshPresentation();
        }

        /// <summary>Called every second; repaints only when freshness, the summary or a reset text changed.</summary>
        public void RefreshClock()
        {
            if (ClockKey() != clockKey)
                RefreshState(State);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Config != null && Config.TransparentBackground)
                RenderLayered();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width < 1 || Height < 1 || Config == null)
                return;
            UpdateShape();
            if (IsHandleCreated && Config.TransparentBackground)
                RenderLayered();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = new IntPtr(Native.MA_NOACTIVATE);
                return;
            }
            // Layered windows pass clicks through transparent pixels; claim the whole client area.
            if (m.Msg == Native.WM_NCHITTEST && Config != null && Config.TransparentBackground)
            {
                m.Result = new IntPtr(Native.HTCLIENT);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Config == null)
                return;
            if (Config.TransparentBackground)
                RenderLayered();
            else
                Look.Strip(e.Graphics, ClientRectangle, State, Config, hover);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Docking.Dispose();
                tip.Dispose();
            }
            base.Dispose(disposing);
        }

        void UpdateTooltip(Snapshot s)
        {
            var view = Look.ResolveBar(s, Config);
            string info;
            if (view.Summary != null)
                info = QuotaAggregation.Description(s, Config);
            else if (view.Account == null)
                info = "点击打开设置，连接CPA。";
            else
            {
                bool fresh = Look.Fresh(view.Quota, Config);
                var lines = Look.BarWindows(view.Quota, Config, view.Account).Select(w =>
                    w.Label + "剩余 " + (fresh && w.Valid ? Math.Round(w.Remaining.Value) + "%" : "—") + " · 重置：" + QuotaReset.Description(w, fresh, J.Now));
                info = J.Provider(view.Account.Provider) + "\n" + view.Account.Label + "\n" + String.Join("\n", lines);
            }
            if (s.Error != "")
                info = s.Error;
            if (view.Quota != null && view.Quota.Error != "")
                info += "\n" + view.Quota.Error;
            if (info != tooltipText)
            {
                tooltipText = info;
                tip.SetToolTip(this, info);
                AccessibleName = info.Replace("\n", "，");
            }
        }

        string ClockKey()
        {
            var view = Look.ResolveBar(State, Config);
            bool fresh = Look.Fresh(view.Quota, Config);
            string summaryKey = view.Summary == null ? "" : view.Summary.Key;
            string resets = QuotaReset.Enabled(Config)
                ? String.Join("|", Look.BarWindows(view.Quota, Config, view.Account).Select(w => QuotaReset.Text(w, fresh, Config, J.Now)))
                : "";
            return fresh + "|" + summaryKey + "|" + resets;
        }

        void UpdateShape()
        {
            var old = Region;
            if (Config.TransparentBackground)
                Region = null;
            else
            {
                using (var shape = QuotaColors.Rounded(ClientRectangle, Math.Max(5, Height / 7)))
                    Region = new Region(shape);
            }
            if (old != null)
                old.Dispose();
        }

        void RenderLayered()
        {
            using (var bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                    Look.Strip(g, ClientRectangle, State, Config, hover);
                Native.PresentLayered(Handle, bitmap);
                LastPresentedAt = J.Now;
                PresentationCount++;
            }
        }

        void RefreshPresentation()
        {
            // UpdateLayeredWindow owns the displayed bitmap; Invalidate alone does not replace it.
            if (IsHandleCreated && Config.TransparentBackground)
                RenderLayered();
            else
                Invalidate();
        }
    }

    /// <summary>Settings preview of the panel at the current taskbar height, on the opaque panel surface.</summary>
    public sealed class StripPreview : Control
    {
        public Snapshot State;
        public AppConfig Config;

        public StripPreview()
        {
            DoubleBuffered = true;
            Height = 56;
            AccessibleName = "任务栏显示预览";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.White);
            if (Config == null || State == null)
                return;
            int width = Math.Min(Width, TaskbarMetrics.Width(TaskbarMetrics.TotalWidth(Config), TaskbarMetrics.Dpi(), Config.PhysicalPixels));
            int taskbarHeight = Native.Bounds(Native.FindWindow("Shell_TrayWnd", null)).Height;
            int height = Math.Max(24, Math.Min(Height - 8, taskbarHeight));
            var bounds = new Rectangle(0, 4, width, height);
            using (var image = new Bitmap(Math.Max(1, width), height))
            {
                using (var g = Graphics.FromImage(image))
                    Look.Strip(g, new Rectangle(0, 0, width, height), State, Config);
                var saved = e.Graphics.Save();
                using (var shape = QuotaColors.Rounded(bounds, Math.Max(5, height / 7)))
                    e.Graphics.SetClip(shape, CombineMode.Intersect);
                using (var surface = new SolidBrush(Look.StripSurface(Look.Dark(Config), false)))
                    e.Graphics.FillRectangle(surface, bounds);
                e.Graphics.DrawImageUnscaled(image, 0, 4);
                e.Graphics.Restore(saved);
            }
        }
    }
}
