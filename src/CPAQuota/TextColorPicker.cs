using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace CpaTaskbar
{
    /// <summary>
    /// Panel text colour: follow the theme, adapt to the taskbar behind the panel, or a custom
    /// colour typed as #RRGGBB, chosen from the palette or picked from the screen.
    /// </summary>
    public sealed class TextColorPicker : FlowLayoutPanel
    {
        static readonly string[] Modes = { "theme", "adaptive", "custom" };

        readonly ComboBox mode = new ComboBox { Name = "textColorMode", AccessibleName = "文字颜色方式", DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        readonly TextBox hex = new TextBox { Name = "textColorHex", AccessibleName = "文字色号", Width = 84, MaxLength = 7 };
        readonly Button swatch = SettingsLayout.Button("色盘", new Padding(8, 2, 8, 2), "textColorPalette");
        readonly Button eyedropper = SettingsLayout.Button("取色器", new Padding(8, 2, 8, 2), "textColorEyedropper");
        string color = AppConfig.DefaultTextColor;
        bool loading;

        public TextColorPicker()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            WrapContents = true;
            Margin = Padding.Empty;
            mode.Items.AddRange(new object[] { "跟随主题", "自适应任务栏背景", "自定义颜色" });
            mode.SelectedIndex = 0;
            foreach (Control control in new Control[] { mode, hex, swatch, eyedropper })
            {
                control.Margin = new Padding(0, 2, 8, 2);
                Controls.Add(control);
            }
            swatch.AccessibleName = "打开色盘";
            eyedropper.AccessibleName = "从屏幕取色";

            mode.SelectedIndexChanged += (s, e) =>
            {
                UpdateEnabled();
                Raise();
            };
            hex.TextChanged += (s, e) =>
            {
                string text = hex.Text.Trim();
                if (!text.StartsWith("#"))
                    text = "#" + text;
                bool valid = AppConfig.IsRgb(text);
                hex.ForeColor = valid ? SystemColors.WindowText : Color.Firebrick;
                if (valid && !String.Equals(text, color, StringComparison.OrdinalIgnoreCase))
                {
                    color = text.ToUpperInvariant();
                    ShowSwatch();
                    Raise();
                }
            };
            hex.Leave += (s, e) => hex.Text = color;
            swatch.Click += (s, e) => OpenPalette();
            eyedropper.Click += (s, e) => PickFromScreen();
            ShowSwatch();
            UpdateEnabled();
        }

        public event EventHandler Changed;

        /// <summary>theme | adaptive | custom</summary>
        public string Mode
        {
            get
            {
                return Modes[Math.Max(0, mode.SelectedIndex)];
            }
            set
            {
                mode.SelectedIndex = Math.Max(0, Array.IndexOf(Modes, value));
            }
        }

        /// <summary>The custom colour as #RRGGBB.</summary>
        public string CustomColor
        {
            get
            {
                return color;
            }
            set
            {
                loading = true;
                color = AppConfig.IsRgb(value) ? value.ToUpperInvariant() : AppConfig.DefaultTextColor;
                hex.Text = color;
                ShowSwatch();
                loading = false;
            }
        }

        void SetPicked(Color picked)
        {
            if (mode.SelectedIndex != 2)
                mode.SelectedIndex = 2;
            hex.Text = ScreenColorPicker.Hex(picked);
        }

        void OpenPalette()
        {
            using (var dialog = new ColorDialog { Color = ColorTranslator.FromHtml(color), FullOpen = true, AnyColor = true })
            {
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                    SetPicked(dialog.Color);
            }
        }

        void PickFromScreen()
        {
            // Hide the settings window so the taskbar and anything under it can be picked.
            var form = FindForm();
            if (form != null)
            {
                form.Hide();
                Application.DoEvents();
                Thread.Sleep(250);
            }
            Color? picked;
            try
            {
                picked = ScreenColorPicker.Pick(form);
            }
            finally
            {
                if (form != null && !form.IsDisposed)
                {
                    form.Show();
                    form.Activate();
                }
            }
            if (picked.HasValue)
                SetPicked(picked.Value);
        }

        void ShowSwatch()
        {
            var c = ColorTranslator.FromHtml(color);
            swatch.BackColor = c;
            swatch.ForeColor = QuotaColors.Luminance(c) > .4 ? Color.Black : Color.White;
            swatch.UseVisualStyleBackColor = false;
        }

        void UpdateEnabled()
        {
            // The palette and eyedropper stay enabled: picking a colour switches to custom.
            hex.Enabled = mode.SelectedIndex == 2;
        }

        void Raise()
        {
            if (!loading && Changed != null)
                Changed(this, EventArgs.Empty);
        }
    }
}
