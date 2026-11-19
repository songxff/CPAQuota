using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CpaTaskbar
{
    /// <summary>
    /// Borderless popup above the taskbar panel: account picker, scrolling details and a footer.
    /// It hides when it loses activation, on Esc, or when closed by the user, and is reused.
    /// </summary>
    public sealed class DetailsForm : Form
    {
        public readonly ComboBox Accounts = new ComboBox { Name = "detailsAccounts", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        public readonly DetailsCanvas Canvas = new DetailsCanvas();
        public event Action SettingsRequested, RefreshRequested;

        readonly Panel scroll = new Panel { Name = "detailsScroll", Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
        readonly TableLayoutPanel header = new TableLayoutPanel { Name = "detailsHeader", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(10, 8, 10, 4), BackColor = Color.White };
        readonly FlowLayoutPanel footer = new FlowLayoutPanel { Name = "detailsFooter", Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 3, 8, 3), FlowDirection = FlowDirection.RightToLeft, BackColor = Color.FromArgb(247, 249, 252) };
        Rectangle popupAnchor;

        public DetailsForm()
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Text = "CPA 额度详情";
            Font = Look.Font();
            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(320, 240);
            Padding = new Padding(1);
            BackColor = Look.Line;

            var settings = FooterButton("detailsSettings", "设置", Look.Muted);
            var refresh = FooterButton("detailsRefresh", "刷新额度", Look.Blue);
            footer.Controls.Add(settings);
            footer.Controls.Add(refresh);
            settings.Click += (s, e) =>
            {
                Hide();
                if (SettingsRequested != null)
                    SettingsRequested();
            };
            refresh.Click += (s, e) =>
            {
                if (RefreshRequested != null)
                    RefreshRequested();
            };

            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.Controls.Add(Accounts, 0, 0);
            scroll.Controls.Add(Canvas);
            Controls.Add(scroll);
            Controls.Add(footer);
            Controls.Add(header);
            scroll.Layout += (s, e) =>
            {
                var maximum = new Size(Math.Max(1, scroll.ClientSize.Width), 0);
                if (Canvas.MaximumSize != maximum)
                    Canvas.MaximumSize = maximum;
            };
            Accounts.DropDown += (s, e) => FitDropDownWidth();
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Escape)
                    return;
                Hide();
                e.Handled = true;
            };
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ResumeLayout(true);
        }

        /// <summary>Updates the account picker and content; resizes when visible.</summary>
        public void UpdateData(Snapshot state, AppConfig config)
        {
            var selected = Accounts.SelectedItem as Account;
            string current = selected == null ? "" : selected.Id;
            var listed = state.Accounts.Where(a => a.Supported && (config.SelectedIds.Count == 0 || config.SelectedIds.Contains(a.Id))).ToList();
            if (!Accounts.Items.Cast<Account>().Select(a => a.Id).SequenceEqual(listed.Select(a => a.Id)))
            {
                Accounts.Items.Clear();
                Accounts.Items.AddRange(listed.Cast<object>().ToArray());
            }
            string focus = config.FocusId != "" ? config.FocusId : current;
            var items = Accounts.Items.Cast<Account>();
            var desired = items.FirstOrDefault(a => a.Id == focus) ?? items.FirstOrDefault();
            if (desired != null && Accounts.SelectedItem != desired)
                Accounts.SelectedItem = desired;
            Canvas.UpdateData(state, config);
            if (Visible)
                FitContent();
        }

        /// <summary>Shows the popup above (or, without room, below) the anchor and activates it.</summary>
        public void ShowNear(Rectangle anchor)
        {
            popupAnchor = anchor;
            Location = Screen.FromRectangle(anchor).WorkingArea.Location;
            FitContent();
            Show();
            FitContent();
            Native.SetForegroundWindow(Handle);
            Activate();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            BeginInvoke((Action)(() =>
            {
                if (IsDisposed || Canvas.State == null)
                    return;
                Canvas.UpdateData(Canvas.State, Canvas.Config);
                if (Visible)
                    FitContent();
            }));
        }

        /// <summary>320 logical pixels wide, as tall as the content up to 480, next to the anchor.</summary>
        void FitContent()
        {
            var work = Screen.FromRectangle(popupAnchor).WorkingArea;
            int dpi = DetailsLayout.Dpi(this);
            ClientSize = new Size(Math.Min(TaskbarMetrics.Scale(320, dpi), work.Width), ClientSize.Height);
            PerformLayout();
            Canvas.PerformLayout();
            int contentHeight = Canvas.Height + header.Height + footer.Height + Padding.Vertical;
            ClientSize = new Size(ClientSize.Width, Math.Min(Math.Min(TaskbarMetrics.Scale(480, dpi), work.Height), contentHeight));

            int gap = TaskbarMetrics.Scale(8, dpi);
            int x = Math.Max(work.Left, Math.Min(popupAnchor.Right - Width, work.Right - Width));
            int y = popupAnchor.Top - Height - gap;
            if (y < work.Top)
                y = Math.Min(work.Bottom - Height, popupAnchor.Bottom + gap);
            Location = new Point(x, Math.Max(work.Top, y));
            WindowPlacement.ClampToWorkingArea(this);
        }

        void FitDropDownWidth()
        {
            int widest = Accounts.Items.Cast<object>()
                .Select(a => TextRenderer.MeasureText(a.ToString(), Accounts.Font).Width + SystemInformation.VerticalScrollBarWidth)
                .DefaultIfEmpty(0).Max();
            Accounts.DropDownWidth = Math.Min(Screen.FromHandle(Handle).WorkingArea.Width, Math.Max(Accounts.Width, widest));
        }

        static Button FooterButton(string name, string text, Color color)
        {
            var button = new Button { Name = name, Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 2, 6, 2), FlatStyle = FlatStyle.Flat, ForeColor = color, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }
    }
}
