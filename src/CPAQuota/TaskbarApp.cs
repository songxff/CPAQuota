using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CpaTaskbar
{
    /// <summary>
    /// Owns the panel, tray icon, details popup and settings window, and drives them from a
    /// one-second UI timer. Service change notifications arrive on worker threads and are
    /// coalesced into a single RefreshUI on the UI thread.
    /// </summary>
    public sealed class TaskbarApp : ApplicationContext
    {
        const int PollEverySeconds = 5;
        const long StatusMinIntervalMs = 2000, StatusMaxIntervalMs = 30000;

        public AppConfig Config;

        readonly QuotaService service;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Control ui = new Control();
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly CommandHost commands;
        QuotaStrip strip;
        DetailsForm details;
        SettingsForm settings;
        bool closing, refreshingUi, statusDirty;
        int queuedUi, seconds;
        long lastRotate, lastStatus, lastDetailsClick;

        public TaskbarApp(AppConfig config, bool openDetails = false)
        {
            Config = config;
            var handle = ui.Handle; // Creates the marshalling control's handle on the UI thread.
            service = new QuotaService(config);
            strip = NewStrip();
            tray = new NotifyIcon { Text = "CPA 额度状态栏", Icon = AppIcon(), Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    ToggleDetails();
            };
            menu.Opening += (s, e) => BuildMenu();
            commands = new CommandHost(ShowSettings, () => ExitThread(), ShowDetails);
            service.Changed += OnServiceChanged;
            tick.Tick += (s, e) => OnTick();
            tick.Start();
            RefreshUI();
            Refresh(true);
            if (String.IsNullOrEmpty(config.CpaSecret) || !AppConfig.CanUnprotect(config.CpaSecret))
                ui.BeginInvoke((Action)ShowSettings);
            else if (openDetails)
                ui.BeginInvoke((Action)ShowDetails);
        }

        /// <summary>Pushes the current service state to every visible surface.</summary>
        public void RefreshUI()
        {
            var state = service.State;
            EnsureDefaultSelection(state);
            strip.Config = Config;
            strip.RefreshState(state);
            try
            {
                strip.Docking.Update(TaskbarMetrics.TotalWidth(Config), Config.Offset, Config.PhysicalPixels);
            }
            catch
            {
            }
            // Hidden details are rebuilt by ShowDetails when opened; skip rebuilding all controls in the background.
            if (details != null && details.Visible)
                WithoutSelectionEvents(() => details.UpdateData(state, Config));
            if (settings != null && !settings.IsDisposed)
                settings.UpdateAccounts(state);
            UpdateTrayText(state);
            // One refresh raises several change events; the timer writes the status file at most every 2 seconds.
            statusDirty = true;
        }

        public void ShowDetails()
        {
            if (closing)
                return;
            EnsureDetails();
            WithoutSelectionEvents(() => details.UpdateData(service.State, Config));
            details.ShowNear(strip.Docking.ScreenBounds);
        }

        public void ShowSettings()
        {
            if (closing)
                return;
            if (details != null)
                details.Hide();
            if (settings != null && !settings.IsDisposed)
            {
                settings.Show();
                settings.Activate();
                return;
            }
            settings = new SettingsForm(Config, service, service.State);
            settings.Icon = tray.Icon;
            settings.Applied += next =>
            {
                Config = next;
                service.Apply(next);
                RefreshUI();
                Refresh(true);
            };
            settings.Show();
            settings.Activate();
            Native.SetForegroundWindow(settings.Handle);
        }

        protected override void ExitThreadCore()
        {
            if (closing)
                return;
            closing = true;
            tick.Stop();
            service.Changed -= OnServiceChanged;
            service.Dispose();
            if (settings != null && !settings.IsDisposed)
                settings.Close();
            if (details != null)
                details.Dispose();
            strip.Dispose(); // Also gives the reserved taskbar space back.
            Look.ReleaseStripFonts();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            commands.Dispose();
            ui.Dispose();
            tick.Dispose();
            base.ExitThreadCore();
        }

        // ----- Timer and service events -----

        void OnTick()
        {
            if (closing)
                return;
            // Log failures with details (throttled) instead of letting the same exception reach
            // ThreadException once per second.
            try
            {
                if (strip.IsDisposed)
                    strip = NewStrip();
                try
                {
                    strip.Docking.Update(TaskbarMetrics.TotalWidth(Config), Config.Offset, Config.PhysicalPixels);
                }
                catch (Exception error)
                {
                    AppLog.Write("任务栏定位失败，下一秒重试", error);
                }
                strip.RefreshClock();
                if (details != null && details.Visible)
                    details.Canvas.RefreshClock();
                if (settings != null && !settings.IsDisposed)
                    settings.RefreshClock();
                if (++seconds % PollEverySeconds == 0)
                    Refresh(false);
                RotateAccount();
                long sinceStatus = J.Now - lastStatus;
                if (statusDirty && sinceStatus > StatusMinIntervalMs || sinceStatus > StatusMaxIntervalMs)
                    WriteStatus();
            }
            catch (Exception error)
            {
                AppLog.Write("定时刷新异常", error);
            }
        }

        void OnServiceChanged()
        {
            if (closing || Interlocked.Exchange(ref queuedUi, 1) == 1)
                return;
            try
            {
                ui.BeginInvoke((Action)(() =>
                {
                    Interlocked.Exchange(ref queuedUi, 0);
                    if (!closing)
                        RefreshUI();
                }));
            }
            catch
            {
            }
        }

        void Refresh(bool force)
        {
            if (!closing)
                Task.Run(() => service.RefreshAsync(force));
        }

        // ----- Accounts -----

        /// <summary>On first connection, selects and saves the default account.</summary>
        void EnsureDefaultSelection(Snapshot state)
        {
            if (Config.SelectedIds.Count > 0)
                return;
            var first = Account.PickDefault(state.Accounts);
            if (first == null)
                return;
            Config.SelectedIds.Add(first.Id);
            Config.FocusId = first.Id;
            service.SetSelection(Config.SelectedIds, first.Id);
            TrySave();
        }

        void SelectAccount(string id, bool persist)
        {
            if (Config.FocusId == id)
                return;
            Config.FocusId = id;
            service.SetSelection(Config.SelectedIds, id);
            if (persist)
                TrySave();
            RefreshUI();
            Refresh(false);
        }

        void RotateAccount()
        {
            if (!Config.Rotate || QuotaAggregation.Enabled(Config) || J.Now - lastRotate <= Config.RotateSeconds * 1000)
                return;
            var selected = service.State.Accounts.Where(a => a.Supported && Config.SelectedIds.Contains(a.Id)).ToList();
            if (selected.Count > 1)
            {
                int index = selected.FindIndex(a => a.Id == Config.FocusId);
                SelectAccount(selected[(index + 1 + selected.Count) % selected.Count].Id, false);
            }
            lastRotate = J.Now;
        }

        void TrySave()
        {
            try
            {
                Config.Save();
            }
            catch
            {
            }
        }

        // ----- Surfaces -----

        QuotaStrip NewStrip()
        {
            var panel = new QuotaStrip(Config);
            panel.Clicked += ToggleDetails;
            panel.ContextMenuStrip = menu;
            return panel;
        }

        void BuildMenu()
        {
            menu.Items.Clear();
            menu.Items.Add(new ToolStripMenuItem("CPA 额度状态栏") { Enabled = false });
            var switcher = new ToolStripMenuItem("切换账号");
            foreach (var account in service.State.Accounts.Where(a => a.Supported && (Config.SelectedIds.Count == 0 || Config.SelectedIds.Contains(a.Id))))
            {
                string id = account.Id;
                var item = new ToolStripMenuItem(account.ToString()) { Checked = id == Config.FocusId };
                item.Click += (s, e) => SelectAccount(id, true);
                switcher.DropDownItems.Add(item);
            }
            menu.Items.Add(switcher);
            menu.Items.Add("查看详情", null, (s, e) => ShowDetails());
            menu.Items.Add("刷新额度", null, (s, e) => Refresh(true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("设置…", null, (s, e) => ShowSettings());
            menu.Items.Add("退出", null, (s, e) => ExitThread());
        }

        void UpdateTrayText(Snapshot state)
        {
            string text;
            if (QuotaAggregation.Enabled(Config))
                text = QuotaAggregation.Build(state, Config).Title(Config);
            else
            {
                var account = Look.Focus(state, Config);
                text = account == null ? "CPA 额度状态栏" : "CPA 额度 · " + J.Provider(account.Provider);
            }
            // NotifyIcon.Text is limited to 63 characters.
            if (text.Length <= 63)
                tray.Text = text;
        }

        void EnsureDetails()
        {
            if (details != null)
                return;
            details = new DetailsForm();
            details.SettingsRequested += ShowSettings;
            details.RefreshRequested += () => Refresh(true);
            details.Deactivate += (s, e) => lastDetailsClick = J.Now;
            details.Accounts.SelectedIndexChanged += (s, e) =>
            {
                var account = details.Accounts.SelectedItem as Account;
                if (!refreshingUi && account != null)
                    SelectAccount(account.Id, true);
            };
            details.Icon = tray.Icon;
        }

        /// <summary>
        /// Panel and tray clicks toggle the popup. Clicking the panel while the popup is open first
        /// deactivates (hides) it, so clicks within a double-click interval of that are ignored.
        /// </summary>
        void ToggleDetails()
        {
            if (J.Now - lastDetailsClick < SystemInformation.DoubleClickTime)
                return;
            lastDetailsClick = J.Now;
            EnsureDetails();
            if (details.Visible)
                details.Hide();
            else
                ShowDetails();
        }

        /// <summary>Updating the details account list must not be mistaken for a user selection.</summary>
        void WithoutSelectionEvents(Action update)
        {
            refreshingUi = true;
            try
            {
                update();
            }
            finally
            {
                refreshingUi = false;
            }
        }

        /// <summary>Writes data/desktop-status.json for troubleshooting.</summary>
        void WriteStatus()
        {
            try
            {
                lastStatus = J.Now;
                statusDirty = false;
                var s = service.State;
                var account = Look.Focus(s, Config);
                var quota = Look.Quota(s, account);
                var history = Look.History(s, account);
                string folder = Path.Combine(AppConfig.Folder, "data");
                Directory.CreateDirectory(folder);
                var status = new
                {
                    app = "CPAQuotaTaskbar",
                    version = Program.Version,
                    pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                    dock = strip.Docking.Diagnostics(),
                    appearance = new
                    {
                        width = Config.Width,
                        offset = Config.Offset,
                        physicalPixels = Config.PhysicalPixels,
                        fontSize = Config.FontSize,
                        theme = Config.Theme,
                        singleLine = Config.SingleLine,
                        resetTimeMode = Config.ResetTimeMode,
                        transparentBackground = Config.TransparentBackground,
                        progressStyle = Config.ProgressStyle,
                    },
                    presentation = new
                    {
                        lastPresentedAt = strip.LastPresentedAt,
                        count = strip.PresentationCount
                    },
                    detailsVisible = details != null && details.Visible,
                    accountCount = s.Accounts.Count,
                    provider = account == null ? "" : account.Provider,
                    busy = s.Busy,
                    error = s.Error,
                    quotaError = quota == null ? "" : quota.Error,
                    quotaUpdatedAt = quota == null ? 0 : quota.UpdatedAt,
                    historyError = history == null ? "" : history.Error,
                    historyUpdatedAt = history == null ? 0 : history.UpdatedAt,
                    updatedAt = J.Now,
                };
                File.WriteAllText(Path.Combine(folder, "desktop-status.json"), J.Json(status), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        /// <summary>Blue circle with three white bars.</summary>
        static Icon AppIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(Look.Blue))
                    g.FillEllipse(brush, 1, 1, 30, 30);
                using (var pen = new Pen(Color.White, 2.6f))
                {
                    g.DrawLine(pen, 10, 21, 10, 15);
                    g.DrawLine(pen, 16, 21, 16, 9);
                    g.DrawLine(pen, 22, 21, 22, 12);
                }
                IntPtr icon = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(icon).Clone();
                }
                finally
                {
                    Native.DestroyIcon(icon);
                }
            }
        }
    }
}
