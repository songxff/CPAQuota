using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CpaTaskbar
{
    /// <summary>
    /// Settings window with four tabs. Every visual control updates the three previews live
    /// (VisualDraft); Save validates the whole draft (Draft), persists it and raises Applied.
    /// Control names are used by the tests.
    /// </summary>
    public sealed class SettingsForm : Form
    {
        static readonly string[] DisplayModes = { "single", "average", "sum" };
        static readonly string[] ProgressStyles = { "thick", "thin" };
        static readonly string[] ColorModes = { "default", "background", "numbers", "both" };
        static readonly string[] ResetModes = { "off", "time", "countdown" };
        static readonly string[] Themes = { "auto", "dark", "light" };

        public Action<AppConfig> Applied;

        readonly QuotaService service;
        readonly AppConfig original;

        // Connection tab.
        readonly TextBox cpaUrl = new TextBox(), cpaKey = new TextBox { UseSystemPasswordChar = true };
        readonly TextBox cpampUrl = new TextBox(), cpampKey = new TextBox { UseSystemPasswordChar = true };
        readonly CheckBox cpamp = new CheckBox { Text = "启用CPAMP历史用量", AutoSize = true };
        readonly Button test = SettingsLayout.Button("测试连接", new Padding(8, 4, 8, 4));

        // Accounts and metrics tab.
        readonly CheckedListBox accounts = new CheckedListBox { CheckOnClick = true, IntegralHeight = false, Height = 190 };
        readonly ComboBox quotaDisplay = new ComboBox { Name = "quotaDisplayMode", DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ComboBox focus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox model = new TextBox();
        readonly CheckBox used = new CheckBox { Text = "显示已用百分比（默认显示剩余额度）", AutoSize = true };
        readonly ResetModePicker resetMode = new ResetModePicker { Name = "quotaResetMode" };
        readonly StripPreview resetPreview = new StripPreview { Name = "quotaResetPreview", Height = 64 };
        readonly CheckBox historyBar = new CheckBox { Text = "在任务栏右侧追加今日Token", AutoSize = true };
        readonly CheckBox rotate = new CheckBox { Text = "自动轮播已勾选的账号", AutoSize = true };
        readonly NumericUpDown rotateInterval = new NumericUpDown { Minimum = 5, Maximum = 120 };

        // Appearance and startup tab.
        readonly ComboBox progressStyle = new ComboBox { Name = "quotaProgressStyle", AccessibleName = "进度条样式", DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ComboBox theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextColorPicker textColor = new TextColorPicker { Name = "textColor" };
        readonly CheckBox transparent = new CheckBox { Name = "transparentBackground", Text = "透明背景（融入任务栏）", AutoSize = true };
        readonly NumericUpDown width = new NumericUpDown { Name = "taskbarWidth", Minimum = 80, Maximum = 840 };
        readonly NumericUpDown offset = new NumericUpDown { Name = "taskbarOffset", Minimum = 0, Maximum = 800 };
        readonly NumericUpDown fontSize = new NumericUpDown { Minimum = 8, Maximum = 12 };
        readonly CheckBox single = new CheckBox { Text = "使用单行紧凑显示", AutoSize = true };
        readonly NumericUpDown interval = new NumericUpDown { Minimum = 60, Maximum = 3600, Increment = 60 };
        readonly CheckBox autostart = new CheckBox { Text = "登录Windows后自动启动", AutoSize = true };
        readonly StripPreview preview = new StripPreview { Height = 64 };

        // Quota colours tab.
        readonly ColorModePicker colorMode = new ColorModePicker { Name = "quotaColorMode" };
        readonly StripPreview colorPreview = new StripPreview { Name = "quotaColorPreview", Height = 64 };
        readonly TableLayoutPanel colorOptions = new TableLayoutPanel { Name = "quotaColorOptions", AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
        readonly Button lowColor = ColorButton("lowUsageColor"), mediumColor = ColorButton("mediumUsageColor"), highColor = ColorButton("highUsageColor");
        readonly NumericUpDown yellowUsed = new NumericUpDown { Name = "yellowUsedPercent", Minimum = 1, Maximum = 99 };
        readonly NumericUpDown redUsed = new NumericUpDown { Name = "redUsedPercent", Minimum = 2, Maximum = 100 };
        readonly Label colorRanges = new Label { AutoSize = true, ForeColor = Look.Muted };
        readonly Button resetColors = SettingsLayout.Button("恢复默认颜色与阈值", new Padding(8, 4, 8, 4));

        // Footer.
        readonly Label feedback = new Label { Name = "settingsFeedback", AutoSize = true, Dock = DockStyle.Top, ForeColor = Look.Muted };
        readonly Button save = new Button { Name = "saveSettings", Text = "保存并应用", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12, 6, 12, 6), BackColor = Look.Blue, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        readonly Button cancel = SettingsLayout.Button("取消", new Padding(12, 6, 12, 6), "cancelSettings");

        /// <summary>Suppresses account-check events while the account list is rebuilt.</summary>
        bool updating;
        /// <summary>The user changed account checks; keep them when the account list reloads.</summary>
        bool dirtyAccounts;

        public SettingsForm(AppConfig config, QuotaService quotaService, Snapshot state)
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            original = config.Clone();
            service = quotaService;
            Text = "CPA 状态栏 · 设置";
            Font = Look.Font(9.5f);
            BackColor = Color.FromArgb(246, 248, 252);
            ClientSize = new Size(760, 770);
            MinimumSize = new Size(600, 460);
            StartPosition = FormStartPosition.CenterScreen;

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(20, 7) };
            var connection = new TabPage("连接");
            var display = new TabPage("账号与指标");
            var appearance = new TabPage("外观与启动");
            var colors = new TabPage("额度配色");
            tabs.TabPages.AddRange(new[] { connection, display, appearance, colors });
            BuildConnectionTab(connection, config);
            BuildDisplayTab(display);
            BuildAppearanceTab(appearance);
            BuildColorTab(colors);
            Controls.Add(tabs);
            Controls.Add(BuildHeader());
            Controls.Add(BuildFooter());

            LoadValues(config);
            WireEvents();
            AcceptButton = save;
            CancelButton = cancel;
            UpdateAccounts(state);
            UpdatePreview(state);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ResumeLayout(true);
        }

        /// <summary>Rebuilds the account lists when the accounts change, keeping the user's checks.</summary>
        public void UpdateAccounts(Snapshot s)
        {
            var all = s.Accounts.Where(a => a.Supported).ToList();
            if (!accounts.Items.Cast<Account>().Select(a => a.Id).SequenceEqual(all.Select(a => a.Id)))
            {
                var checkedIds = dirtyAccounts ? CheckedAccountIds() : original.SelectedIds;
                var focused = focus.SelectedItem as Account;
                string focusId = focused == null ? original.FocusId : focused.Id;
                updating = true;
                accounts.Items.Clear();
                focus.Items.Clear();
                var first = all.FirstOrDefault(a => a.Provider == "codex" && !a.Disabled) ?? all.FirstOrDefault();
                foreach (var account in all)
                {
                    accounts.Items.Add(account, checkedIds.Contains(account.Id) || checkedIds.Count == 0 && account == first);
                    focus.Items.Add(account);
                }
                focus.SelectedItem = all.FirstOrDefault(a => a.Id == focusId) ?? first;
                updating = false;
            }
            UpdatePreview(s);
        }

        /// <summary>Repaints previews that show a countdown.</summary>
        public void RefreshClock()
        {
            foreach (var strip in Previews)
            {
                if (strip.Visible && strip.Config != null && QuotaReset.Enabled(strip.Config))
                    strip.Invalidate();
            }
        }

        protected override void OnShown(EventArgs e)
        {
            WindowPlacement.ClampToWorkingArea(this);
            base.OnShown(e);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            BeginInvoke((Action)(() => WindowPlacement.ClampToWorkingArea(this)));
        }

        StripPreview[] Previews
        {
            get
            {
                return new[] { preview, colorPreview, resetPreview };
            }
        }

        // ----- Layout -----

        Control BuildHeader()
        {
            var header = new TableLayoutPanel { Name = "settingsHeader", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, Padding = new Padding(20, 14, 20, 12) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var title = new Label { Name = "settingsTitle", Text = "CPA 额度状态栏", Font = Look.Font(17, FontStyle.Bold), ForeColor = Look.Ink, AutoSize = true, Dock = DockStyle.Top, Margin = Padding.Empty };
            var subtitle = new Label { Text = "点击任务栏面板或托盘图标查看额度，右键打开菜单。", ForeColor = Look.Muted, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 0) };
            header.Controls.Add(title, 0, 0);
            header.Controls.Add(subtitle, 0, 1);
            header.Layout += (s, e) =>
            {
                int available = header.ClientSize.Width - header.Padding.Horizontal;
                SettingsLayout.WrapToWidth(title, available);
                SettingsLayout.WrapToWidth(subtitle, available);
            };
            return header;
        }

        void BuildConnectionTab(TabPage page, AppConfig config)
        {
            var grid = SettingsLayout.Page(page);
            SettingsLayout.Add(grid, "CPA服务地址", cpaUrl);
            SettingsLayout.Add(grid, "CPA管理密钥", cpaKey);
            string keyHint = config.CpaSecret == "" ? "填写CPA管理页面的密钥，模型API Key不适用。"
                : !AppConfig.CanUnprotect(config.CpaSecret) ? "已保存的管理密钥无法用当前Windows账号解密（可能更换了用户或电脑），请重新输入。"
                : "管理密钥已加密保存，留空保持。更换地址时请重新输入。";
            SettingsLayout.Hint(grid, keyHint);
            SettingsLayout.Add(grid, "", cpamp);
            SettingsLayout.Add(grid, "CPAMP服务地址", cpampUrl);
            SettingsLayout.Add(grid, "CPAMP管理员密钥", cpampKey);
            SettingsLayout.Hint(grid, config.CpampSecret != "" ? "CPAMP管理员密钥已保存，留空保持。" : "可选。启用后显示当前账号今日/本月Token与请求数。");
            SettingsLayout.Add(grid, "", test);
            SettingsLayout.Hint(grid, "密钥只保存为当前Windows用户的DPAPI密文，程序直接请求服务，不需要Node或本地API端口。");
        }

        void BuildDisplayTab(TabPage page)
        {
            var grid = SettingsLayout.Page(page);
            SettingsLayout.Add(grid, "监控账号", accounts);
            quotaDisplay.Items.AddRange(new object[] { "当前单个账号", "已选账号：平均百分比", "已选账号：累加百分比" });
            SettingsLayout.Add(grid, "额度显示方式", quotaDisplay);
            SettingsLayout.Add(grid, "任务栏当前账号", focus);
            SettingsLayout.Hint(grid, "汇总当前账号同渠道的已勾选账号，各额度周期分别计算。累加可超过100%，进度条跟随剩余或已用百分比，累加超过100%时封顶；失败或过期数据不参与，面板标明有效/总账号数。不同套餐的百分比不代表实际请求总量。最近重置是其中最早的一次，详情保留各账号时间。历史Token仍属于当前单个账号。");
            SettingsLayout.Add(grid, "模型ID / 片段", model);
            SettingsLayout.Hint(grid, "Google渠道可指定模型；留空显示余量最低的模型，并在详情中保留所有窗口。");
            SettingsLayout.Add(grid, "百分比含义", used);
            SettingsLayout.Add(grid, "额度重置时间", resetMode);
            SettingsLayout.Add(grid, "显示预览", resetPreview);
            SettingsLayout.Hint(grid, "分别显示每个额度周期的重置时间，与查询刷新间隔无关。具体时间跟随Windows时区；倒计时本地自动更新。开启后可在“外观与启动”调大宽度（双行建议260—320像素）。");
            SettingsLayout.Add(grid, "可选历史", historyBar);
            SettingsLayout.Add(grid, "账号切换", rotate);
            SettingsLayout.Add(grid, "轮播间隔（秒）", rotateInterval);
        }

        void BuildAppearanceTab(TabPage page)
        {
            var grid = SettingsLayout.Page(page);
            theme.Items.AddRange(new object[] { "跟随Windows系统", "深色", "浅色" });
            progressStyle.Items.AddRange(new object[] { "粗进度条", "细进度条" });
            SettingsLayout.Add(grid, "进度条样式", progressStyle);
            SettingsLayout.Add(grid, "主题", theme);
            SettingsLayout.Add(grid, "文字颜色", textColor);
            SettingsLayout.Hint(grid, "文字看不清时可选“自适应任务栏背景”（按面板后方的实际颜色自动选深色或浅色文字），或自定义色号，可用色盘或取色器选取。");
            SettingsLayout.Add(grid, "背景", transparent);
            SettingsLayout.Add(grid, "宽度（像素）", width);
            SettingsLayout.Add(grid, "左移（像素）", offset);
            SettingsLayout.Add(grid, "字号", fontSize);
            SettingsLayout.Add(grid, "排版", single);
            SettingsLayout.Add(grid, "额度刷新（秒）", interval);
            SettingsLayout.Add(grid, "启动", autostart);
            SettingsLayout.Hint(grid, "宽度和偏移按屏幕实际像素应用，不再叠加Windows缩放。偏移越大，面板越靠左。");
            SettingsLayout.Add(grid, "任务栏预览", preview);
            SettingsLayout.Hint(grid, "点击任务栏数字打开详情，右键打开菜单。历史默认在详情里展示，也可追加到任务栏。");
        }

        void BuildColorTab(TabPage page)
        {
            var grid = SettingsLayout.Page(page);
            SettingsLayout.Add(grid, "着色方式", colorMode);
            SettingsLayout.Add(grid, "任务栏预览", colorPreview);
            SettingsLayout.Hint(grid, "粗进度条使用整行底色，按已用比例填充；细进度条跟随显示的剩余/已用百分比。可在“外观与启动”切换样式，颜色均按已用比例判断。");
            SettingsLayout.ConfigureGrid(colorOptions);
            SettingsLayout.Add(colorOptions, "低使用率", lowColor);
            SettingsLayout.Add(colorOptions, "中等使用率", mediumColor);
            SettingsLayout.Add(colorOptions, "高使用率", highColor);
            SettingsLayout.Add(colorOptions, "黄色起点（%）", yellowUsed);
            SettingsLayout.Add(colorOptions, "红色起点（%）", redUsed);
            SettingsLayout.Add(colorOptions, "颜色范围", colorRanges);
            SettingsLayout.Add(colorOptions, "", resetColors);
            SettingsLayout.AddWide(grid, colorOptions);
            SettingsLayout.Hint(grid, "默认模式使用统一底色和简洁数字。读取失败、额度未知或数据过期时不显示进度条。");
        }

        Control BuildFooter()
        {
            var footer = new TableLayoutPanel { Name = "settingsFooter", Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(20, 10, 20, 10), BackColor = Color.White };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Margin = Padding.Empty };
            actions.Controls.Add(save);
            actions.Controls.Add(cancel);
            footer.Controls.Add(feedback, 0, 0);
            footer.Controls.Add(actions, 0, 1);
            footer.Layout += (s, e) => SettingsLayout.WrapToWidth(feedback, footer.ClientSize.Width - footer.Padding.Horizontal - feedback.Margin.Horizontal);
            return footer;
        }

        // ----- Values and events -----

        void LoadValues(AppConfig c)
        {
            cpaUrl.Text = c.CpaUrl;
            cpampUrl.Text = c.CpampUrl;
            cpamp.Checked = c.CpampEnabled;
            cpampUrl.Enabled = cpampKey.Enabled = c.CpampEnabled;
            resetMode.SelectedIndex = IndexOf(ResetModes, c.ResetTimeMode);
            quotaDisplay.SelectedIndex = IndexOf(DisplayModes, c.QuotaDisplayMode);
            model.Text = c.Model;
            used.Checked = c.ShowUsed;
            historyBar.Checked = c.HistoryInBar;
            single.Checked = c.SingleLine;
            rotate.Checked = c.Rotate;
            autostart.Checked = c.Autostart;
            transparent.Checked = c.TransparentBackground;
            SetNumber(width, TaskbarMetrics.PixelWidth(c));
            SetNumber(offset, TaskbarMetrics.PixelOffset(c));
            SetNumber(fontSize, c.FontSize);
            SetNumber(interval, c.RefreshSeconds);
            SetNumber(rotateInterval, c.RotateSeconds);
            colorMode.SelectedIndex = IndexOf(ColorModes, c.ColorMode);
            yellowUsed.Value = c.YellowUsedPercent;
            redUsed.Value = c.RedUsedPercent;
            SetColor(lowColor, c.LowUsageColor);
            SetColor(mediumColor, c.MediumUsageColor);
            SetColor(highColor, c.HighUsageColor);
            theme.SelectedIndex = IndexOf(Themes, c.Theme);
            textColor.CustomColor = c.TextColor;
            textColor.Mode = c.TextColorMode;
            progressStyle.SelectedIndex = IndexOf(ProgressStyles, c.ProgressStyle);
        }

        void WireEvents()
        {
            EventHandler changed = (s, e) => UpdatePreview(service.State);
            resetMode.SelectedIndexChanged += changed;
            quotaDisplay.SelectedIndexChanged += changed;
            colorMode.SelectedIndexChanged += changed;
            yellowUsed.ValueChanged += changed;
            redUsed.ValueChanged += changed;
            foreach (var number in new[] { width, offset, fontSize })
                number.ValueChanged += changed;
            foreach (var combo in new[] { theme, progressStyle, focus })
                combo.SelectedIndexChanged += changed;
            foreach (var check in new[] { transparent, single, used, historyBar })
                check.CheckedChanged += changed;
            model.TextChanged += changed;
            textColor.Changed += changed;

            cpamp.CheckedChanged += (s, e) =>
            {
                cpampUrl.Enabled = cpampKey.Enabled = cpamp.Checked;
                UpdatePreview(service.State);
            };
            accounts.ItemCheck += (s, e) =>
            {
                if (updating)
                    return;
                dirtyAccounts = true;
                // ItemCheck fires before the check state changes; preview after it is applied.
                if (IsHandleCreated)
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (!IsDisposed)
                            UpdatePreview(service.State);
                    }));
                }
            };
            foreach (var button in new[] { lowColor, mediumColor, highColor })
                button.Click += (s, e) => PickColor((Button)s);
            resetColors.Click += (s, e) =>
            {
                yellowUsed.Value = 50;
                redUsed.Value = 90;
                SetColor(lowColor, "#50D39A");
                SetColor(mediumColor, "#F1C553");
                SetColor(highColor, "#F17788");
                UpdatePreview(service.State);
            };
            test.Click += async (s, e) =>
            {
                test.Enabled = false;
                ShowFeedback("正在连接…", Look.Muted);
                try
                {
                    ShowFeedback(await service.TestAsync(Draft(false)), Color.SeaGreen);
                }
                catch (Exception error)
                {
                    ShowFeedback(error.Message, Color.Firebrick);
                }
                finally
                {
                    test.Enabled = true;
                }
            };
            cancel.Click += (s, e) => Close();
            save.Click += (s, e) => Save();
        }

        void Save()
        {
            try
            {
                var next = Draft(true);
                if (!ConfirmPlainHttp(next))
                    return;
                next.Save();
                next.SetAutostart();
                if (Applied != null)
                    Applied(next);
                Close();
            }
            catch (Exception error)
            {
                ShowFeedback(error.Message, Color.Firebrick);
            }
        }

        bool ConfirmPlainHttp(AppConfig next)
        {
            var plain = new[] { next.CpaUrl, next.CpampEnabled ? next.CpampUrl : "" }.Where(AppConfig.IsPlainRemote).ToList();
            if (plain.Count == 0)
                return true;
            string message = "以下地址使用HTTP，管理密钥会以明文在网络上传输：\n" + String.Join("\n", plain) + "\n\n建议改用HTTPS。仍要保存吗？";
            return MessageBox.Show(this, message, "CPA 状态栏", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        void ShowFeedback(string text, Color color)
        {
            feedback.Text = text;
            feedback.ForeColor = color;
        }

        void UpdatePreview(Snapshot s)
        {
            var draft = VisualDraft();
            colorOptions.Visible = draft.ColorMode != "default";
            rotate.Enabled = !QuotaAggregation.Enabled(draft);
            rotateInterval.Enabled = rotate.Enabled;
            colorRanges.Text = String.Format("已用 <{0}%：低使用率；{0}%–<{1}%：中等使用率；≥{1}%：高使用率。", draft.YellowUsedPercent, draft.RedUsedPercent);
            foreach (var strip in Previews)
            {
                strip.Config = draft;
                strip.State = s;
                strip.Invalidate();
            }
        }

        // ----- Drafts -----

        /// <summary>The original settings with every visual option from the form applied (no validation).</summary>
        AppConfig VisualDraft()
        {
            var c = original.Clone();
            c.ResetTimeMode = ResetModes[Math.Max(0, resetMode.SelectedIndex)];
            c.Model = model.Text.Trim();
            c.ShowUsed = used.Checked;
            c.HistoryInBar = historyBar.Checked;
            c.SingleLine = single.Checked;
            c.CpampEnabled = cpamp.Checked;
            c.Width = (int)width.Value;
            c.Offset = (int)offset.Value;
            c.PhysicalPixels = true;
            c.FontSize = (int)fontSize.Value;
            c.Theme = Themes[Math.Max(0, theme.SelectedIndex)];
            c.TransparentBackground = transparent.Checked;
            c.TextColorMode = textColor.Mode;
            c.TextColor = textColor.CustomColor;
            c.ProgressStyle = ProgressStyles[Math.Max(0, progressStyle.SelectedIndex)];
            c.QuotaDisplayMode = DisplayModes[Math.Max(0, quotaDisplay.SelectedIndex)];
            if (accounts.Items.Count > 0)
                c.SelectedIds = CheckedAccountIds();
            c.ColorMode = ColorModes[Math.Max(0, colorMode.SelectedIndex)];
            c.YellowUsedPercent = (int)yellowUsed.Value;
            c.RedUsedPercent = (int)redUsed.Value;
            c.LowUsageColor = (string)lowColor.Tag;
            c.MediumUsageColor = (string)mediumColor.Tag;
            c.HighUsageColor = (string)highColor.Tag;
            var selected = focus.SelectedItem as Account;
            if (selected != null)
                c.FocusId = selected.Id;
            if (QuotaAggregation.Enabled(c) && c.SelectedIds.Count > 0 && !c.SelectedIds.Contains(c.FocusId))
                c.FocusId = c.SelectedIds[0];
            return c;
        }

        /// <summary>
        /// The complete validated settings. Empty key boxes keep the saved secrets unless the
        /// address changed. With selection, the account checks are applied and validated.
        /// </summary>
        AppConfig Draft(bool selection)
        {
            var c = VisualDraft();
            if (c.ColorMode != "default" && c.YellowUsedPercent >= c.RedUsedPercent)
                throw new Exception("黄色起点必须小于红色起点。");

            c.CpaUrl = AppConfig.Url(cpaUrl.Text);
            c.CpampUrl = String.IsNullOrWhiteSpace(cpampUrl.Text) && !cpamp.Checked ? "" : AppConfig.Url(cpampUrl.Text);
            if (c.CpaUrl != original.CpaUrl && cpaKey.Text == "")
                throw new Exception("更换CPA地址时需要重新输入管理密钥。");
            if (c.CpampUrl != original.CpampUrl && c.CpampEnabled && cpampKey.Text == "")
                throw new Exception("更换CPAMP地址时需要重新输入管理员密钥。");
            if (cpaKey.Text == "" && c.CpaSecret != "" && !AppConfig.CanUnprotect(c.CpaSecret))
                throw new Exception("已保存的CPA管理密钥无法用当前Windows账号解密，请重新输入。");
            if (c.CpampEnabled && cpampKey.Text == "" && c.CpampSecret != "" && !AppConfig.CanUnprotect(c.CpampSecret))
                throw new Exception("已保存的CPAMP管理员密钥无法用当前Windows账号解密，请重新输入。");
            if (cpaKey.Text != "")
                c.CpaSecret = AppConfig.Protect(cpaKey.Text.Trim());
            if (cpampKey.Text != "")
                c.CpampSecret = AppConfig.Protect(cpampKey.Text.Trim());
            if (c.CpaSecret == "")
                throw new Exception("请填写CPA管理密钥。");
            if (c.CpampEnabled && c.CpampSecret == "")
                throw new Exception("启用CPAMP需要管理员密钥。");

            c.RefreshSeconds = (int)interval.Value;
            c.Rotate = rotate.Checked && !QuotaAggregation.Enabled(c);
            c.RotateSeconds = (int)rotateInterval.Value;
            c.Autostart = autostart.Checked;
            if (selection && accounts.Items.Count > 0)
            {
                c.SelectedIds = CheckedAccountIds();
                if (QuotaAggregation.Enabled(c) && c.SelectedIds.Count == 0)
                    throw new Exception("汇总模式请至少勾选一个监控账号。");
                if (!QuotaAggregation.Enabled(c) && c.FocusId != "" && !c.SelectedIds.Contains(c.FocusId))
                    c.SelectedIds.Add(c.FocusId);
            }
            return c;
        }

        System.Collections.Generic.List<string> CheckedAccountIds()
        {
            return accounts.CheckedItems.Cast<Account>().Select(a => a.Id).ToList();
        }

        // ----- Helpers -----

        static int IndexOf(string[] values, string value)
        {
            return Math.Max(0, Array.IndexOf(values, value));
        }

        static void SetNumber(NumericUpDown box, decimal value)
        {
            box.Value = Math.Max(box.Minimum, Math.Min(box.Maximum, value));
        }

        static Button ColorButton(string name)
        {
            return SettingsLayout.Button("", new Padding(8, 4, 8, 4), name);
        }

        static void SetColor(Button button, string hex)
        {
            string upper = hex.ToUpperInvariant();
            button.Tag = upper;
            button.Text = upper;
            button.BackColor = ColorTranslator.FromHtml(hex);
            button.ForeColor = QuotaColors.Luminance(button.BackColor) > .4 ? Color.Black : Color.White;
            button.UseVisualStyleBackColor = false;
            button.AccessibleName = button.Name + " " + hex;
        }

        void PickColor(Button button)
        {
            using (var dialog = new ColorDialog { Color = ColorTranslator.FromHtml((string)button.Tag), FullOpen = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                SetColor(button, "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2"));
                UpdatePreview(service.State);
            }
        }
    }
}
