using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CpaTaskbar
{
    /// <summary>Radio buttons that behave as one selection with an index and a change event.</summary>
    sealed class RadioGroup
    {
        public readonly RadioButton[] Buttons;
        int selected = -1;

        public RadioGroup(string[] labels, Func<string, RadioButton> create)
        {
            Buttons = new RadioButton[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var radio = create(labels[i]);
                Buttons[i] = radio;
                radio.CheckedChanged += (s, e) =>
                {
                    if (!radio.Checked || selected == index)
                        return;
                    selected = index;
                    if (Changed != null)
                        Changed(this, EventArgs.Empty);
                };
            }
        }

        public event EventHandler Changed;

        public int Selected
        {
            get
            {
                return selected;
            }
            set
            {
                if (value < 0 || value >= Buttons.Length)
                    throw new ArgumentOutOfRangeException("value");
                Buttons[value].Checked = true;
            }
        }
    }

    /// <summary>Quota colour mode: two columns of radio buttons, one column when narrow.</summary>
    public sealed class ColorModePicker : TableLayoutPanel
    {
        readonly RadioGroup group;

        public ColorModePicker()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Padding.Empty;
            string[] labels = { "默认（简洁数字）", "仅进度条", "仅百分比数字", "进度条 + 百分比数字" };
            group = new RadioGroup(labels, text => new RadioButton { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 8, 4), AccessibleName = text });
            group.Changed += (s, e) =>
            {
                if (SelectedIndexChanged != null)
                    SelectedIndexChanged(this, EventArgs.Empty);
            };
            Arrange(2);
            SelectedIndex = 0;
        }

        public event EventHandler SelectedIndexChanged;

        public int SelectedIndex
        {
            get
            {
                return group.Selected;
            }
            set
            {
                group.Selected = value;
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (group != null && ClientSize.Width > 0)
            {
                int needed = group.Buttons.Max(r => r.GetPreferredSize(Size.Empty).Width + r.Margin.Horizontal);
                int columns = ClientSize.Width >= needed * 2 ? 2 : 1;
                if (ColumnCount != columns)
                    Arrange(columns);
            }
            base.OnLayout(e);
        }

        void Arrange(int columns)
        {
            SuspendLayout();
            Controls.Clear();
            ColumnCount = columns;
            RowCount = group.Buttons.Length / columns;
            ColumnStyles.Clear();
            RowStyles.Clear();
            for (int i = 0; i < columns; i++)
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            for (int i = 0; i < RowCount; i++)
                RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int i = 0; i < group.Buttons.Length; i++)
                Controls.Add(group.Buttons[i], i % columns, i / columns);
            ResumeLayout(false);
        }
    }

    /// <summary>Reset time mode: off, clock time or countdown, wrapping as needed.</summary>
    public sealed class ResetModePicker : FlowLayoutPanel
    {
        readonly RadioGroup group;

        public ResetModePicker()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            WrapContents = true;
            Margin = Padding.Empty;
            string[] labels = { "不显示", "具体时间", "倒计时" };
            group = new RadioGroup(labels, text => new RadioButton { Text = text, AutoSize = true, Margin = new Padding(0, 5, 20, 5), AccessibleName = "额度重置时间：" + text });
            group.Changed += (s, e) =>
            {
                if (SelectedIndexChanged != null)
                    SelectedIndexChanged(this, EventArgs.Empty);
            };
            Controls.AddRange(group.Buttons);
            SelectedIndex = 0;
        }

        public event EventHandler SelectedIndexChanged;

        public int SelectedIndex
        {
            get
            {
                return group.Selected;
            }
            set
            {
                group.Selected = value;
            }
        }
    }

    /// <summary>Two-column caption/input grids used by the settings tabs.</summary>
    static class SettingsLayout
    {
        /// <summary>A scrolling page holding a caption/input grid that wraps labels to the page width.</summary>
        public static TableLayoutPanel Page(TabPage page)
        {
            page.BackColor = Color.White;
            page.Padding = new Padding(18);
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Padding = new Padding(0, 4, 12, 12) };
            ConfigureGrid(grid);
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            scroll.Controls.Add(grid);
            page.Controls.Add(scroll);
            scroll.Layout += (s, e) => WrapToWidth(grid, scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            return grid;
        }

        /// <summary>Auto-sized caption column, stretching input column whose labels and checkboxes wrap.</summary>
        public static void ConfigureGrid(TableLayoutPanel grid)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.Layout += (s, e) =>
            {
                var columns = grid.GetColumnWidths();
                if (columns.Length < 2 || columns[1] <= 0)
                    return;
                foreach (Control control in grid.Controls)
                {
                    if (grid.GetColumn(control) == 1 && (control is Label || control is CheckBox))
                        WrapToWidth(control, columns[1] - control.Margin.Horizontal);
                }
            };
        }

        public static void Add(TableLayoutPanel grid, string caption, Control input)
        {
            int row = NewRow(grid);
            var label = new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Look.Ink, Margin = new Padding(0, 5, 12, 5) };
            input.Margin = new Padding(0, 5, 0, 5);
            input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            grid.Controls.Add(label, 0, row);
            grid.Controls.Add(input, 1, row);
        }

        /// <summary>A muted explanatory line under the previous input.</summary>
        public static void Hint(TableLayoutPanel grid, string text)
        {
            int row = NewRow(grid);
            grid.Controls.Add(new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, ForeColor = Look.Muted, Font = Look.Font(8.5f), Margin = new Padding(0, 0, 0, 12) }, 1, row);
        }

        /// <summary>A control spanning both columns.</summary>
        public static void AddWide(TableLayoutPanel grid, Control control)
        {
            int row = NewRow(grid);
            grid.Controls.Add(control, 0, row);
            grid.SetColumnSpan(control, 2);
        }

        public static void WrapToWidth(Control control, int width)
        {
            var maximum = new Size(Math.Max(1, width), 0);
            if (control.MaximumSize != maximum)
                control.MaximumSize = maximum;
        }

        public static Button Button(string text, Padding padding, string name = null)
        {
            var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = padding };
            if (name != null)
                button.Name = name;
            return button;
        }

        static int NewRow(TableLayoutPanel grid)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return row;
        }
    }

    static class WindowPlacement
    {
        /// <summary>Shrinks and moves the window so it fits the working area of its monitor.</summary>
        public static void ClampToWorkingArea(Form form)
        {
            if (form.IsDisposed)
                return;
            var work = Screen.FromHandle(form.Handle).WorkingArea;
            form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, work.Width), Math.Min(form.MinimumSize.Height, work.Height));
            form.Size = new Size(Math.Min(form.Width, work.Width), Math.Min(form.Height, work.Height));
            form.Location = new Point(Math.Max(work.Left, Math.Min(form.Left, work.Right - form.Width)), Math.Max(work.Top, Math.Min(form.Top, work.Bottom - form.Height)));
        }
    }
}
