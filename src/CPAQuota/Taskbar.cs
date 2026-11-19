using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CpaTaskbar
{
    /// <summary>Decisions about how the panel can be attached to the taskbar.</summary>
    public static class TaskbarCompatibility
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OsVersionInfo
        {
            public int Size, Major, Minor, Build, Platform;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string ServicePack;
        }

        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)] static extern int RtlGetVersion(ref OsVersionInfo version);

        public static readonly int WindowsBuild = ReadWindowsBuild();

        static int ReadWindowsBuild()
        {
            try
            {
                var version = new OsVersionInfo();
                version.Size = Marshal.SizeOf(typeof(OsVersionInfo));
                if (RtlGetVersion(ref version) == 0 && version.Build > 0)
                    return version.Build;
            }
            catch
            {
            }
            // An unknown platform should use the floating panel rather than modify Explorer.
            return Int32.MaxValue;
        }

        /// <summary>Embedding is limited to the visible horizontal Windows 10 taskbar.</summary>
        public static bool CanEmbed(int build, bool rebarVisible, bool tasklistVisible, Rectangle taskbar)
        {
            // Windows 11 can expose legacy taskbar windows which are not the displayed taskbar.
            return build > 0 && build < 22000 && rebarVisible && tasklistVisible && taskbar.Width > taskbar.Height;
        }

        /// <summary>
        /// Width the task list should have so its right edge stops at the embedded panel (panelX is
        /// relative to the rebar), or null when it must be left alone. The dock owns only the right
        /// edge: other taskbar widgets (e.g. LiteMonitor) move the task list's left edge, and that is
        /// kept. Ownership is tracked by the right edge we last set (ownedRight, screen x), which
        /// survives those widgets shifting the left edge. Without ownership the list is only shrunk
        /// when it overlaps the panel, and never when a second rebar band (a toolbar) may sit to its right.
        /// </summary>
        public static int? ReserveTarget(Rectangle rebar, Rectangle tasklist, int panelX, int ownedRight, int bands)
        {
            if (rebar.IsEmpty || tasklist.IsEmpty)
                return null;
            int panelLeft = rebar.Left + panelX;
            bool owned = ownedRight > 0 && Math.Abs(tasklist.Right - ownedRight) <= 1;
            if (!owned && tasklist.Right <= panelLeft)
                return null;
            if (!owned && bands > 1 && tasklist.Right < rebar.Right - 2)
                return null;
            int width = panelLeft - tasklist.Left;
            return width < 64 ? (int?)null : width;
        }

        /// <summary>The foreground window covers the whole monitor and is not the desktop or a taskbar.</summary>
        public static bool IsFullscreenApp(Rectangle foreground, Rectangle monitor, string windowClass)
        {
            bool shell = windowClass == "Progman" || windowClass == "WorkerW" || windowClass == "Shell_TrayWnd" || windowClass == "Shell_SecondaryTrayWnd";
            return !shell && !monitor.IsEmpty && foreground.Contains(monitor);
        }
    }

    /// <summary>Converts configured sizes to screen pixels.</summary>
    public static class TaskbarMetrics
    {
        /// <summary>Extra width of the optional "today's tokens" column, in logical pixels.</summary>
        public const int HistoryColumnWidth = 76;

        public static int Dpi()
        {
            return DpiOf(Native.FindWindow("Shell_TrayWnd", null));
        }

        public static int DpiOf(IntPtr window)
        {
            try
            {
                int dpi = (int)Native.GetDpiForWindow(window);
                return dpi > 0 ? dpi : 96;
            }
            catch
            {
                return 96;
            }
        }

        public static int Scale(int value, int dpi)
        {
            return value * dpi / 96;
        }

        public static int Width(int value, int dpi, bool physical)
        {
            return physical ? Math.Max(80, value) : Scale(Math.Max(100, value), dpi);
        }

        public static int Offset(int value, int dpi, bool physical)
        {
            return physical ? Math.Max(0, value) : Scale(Math.Max(0, value), dpi);
        }

        /// <summary>Panel width in the config's unit, including the optional history column.</summary>
        public static int TotalWidth(AppConfig c)
        {
            if (!c.HistoryInBar || !c.CpampEnabled)
                return c.Width;
            return c.Width + (c.PhysicalPixels ? Scale(HistoryColumnWidth, Dpi()) : HistoryColumnWidth);
        }

        public static int PixelWidth(AppConfig c)
        {
            return Width(c.Width, Dpi(), c.PhysicalPixels);
        }

        public static int PixelOffset(AppConfig c)
        {
            return Offset(c.Offset, Dpi(), c.PhysicalPixels);
        }
    }

    /// <summary>
    /// Keeps the panel form attached to the taskbar. On Windows 10 it becomes a child of the
    /// taskbar's rebar and the task list is narrowed to make room; elsewhere it is a topmost popup
    /// left of the notification area. Call Update periodically: Explorer can restart or re-layout.
    /// </summary>
    public sealed class TaskbarDock : IDisposable
    {
        readonly Form form;
        IntPtr taskbar, rebar, tasklist;
        int ownedRight = 0, reserved = 0;
        bool embedded;

        public TaskbarDock(Form value)
        {
            form = value;
            Mode = "等待任务栏";
        }

        public string Mode
        {
            get; private set;
        }

        public Rectangle ScreenBounds
        {
            get
            {
                return form.IsHandleCreated ? Native.Bounds(form.Handle) : Rectangle.Empty;
            }
        }

        public void Update(int logicalWidth, int logicalOffset, bool physicalPixels = false)
        {
            IntPtr shell = Native.FindWindow("Shell_TrayWnd", null);
            var shellRect = shell == IntPtr.Zero ? Rectangle.Empty : Native.Bounds(shell);
            if (shellRect.IsEmpty)
            {
                form.Hide();
                Mode = "任务栏暂不可用";
                return;
            }
            int dpi = TaskbarMetrics.DpiOf(shell);
            int width = TaskbarMetrics.Width(logicalWidth, dpi, physicalPixels);
            int offset = TaskbarMetrics.Offset(logicalOffset, dpi, physicalPixels);

            IntPtr bar = Native.FindWindowEx(shell, IntPtr.Zero, "ReBarWindow32", null);
            IntPtr list = bar == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(bar, IntPtr.Zero, "MSTaskSwWClass", null);
            if (list == IntPtr.Zero && bar != IntPtr.Zero)
                list = Native.FindWindowEx(bar, IntPtr.Zero, "MSTaskListWClass", null);
            bool rebarVisible = bar != IntPtr.Zero && Native.IsWindowVisible(bar);
            bool tasklistVisible = list != IntPtr.Zero && Native.IsWindowVisible(list);

            if (TaskbarCompatibility.CanEmbed(TaskbarCompatibility.WindowsBuild, rebarVisible, tasklistVisible, shellRect))
            {
                if (!Embed(shell, bar, list, width, offset))
                    UpdateCompatible(shell, shellRect, width, offset, dpi);
            }
            else
            {
                if (embedded)
                    Detach();
                UpdateCompatible(shell, shellRect, width, offset, dpi);
            }
        }

        public object Diagnostics()
        {
            return new
            {
                mode = Mode,
                windowsBuild = TaskbarCompatibility.WindowsBuild,
                taskbar = Native.Bounds(taskbar),
                parent = Native.Class(Native.GetParent(form.Handle)),
                widget = ScreenBounds,
                visible = Native.IsWindowVisible(form.Handle),
                tasklist = Native.Bounds(tasklist),
                reservedWidth = reserved,
            };
        }

        /// <summary>Gives the reserved width back to the task list if it is still where we left it.</summary>
        public void Restore()
        {
            if (tasklist != IntPtr.Zero && Native.IsWindow(tasklist) && rebar != IntPtr.Zero && ownedRight > 0)
            {
                var rect = Native.Bounds(tasklist);
                if (Math.Abs(rect.Right - ownedRight) <= 1)
                {
                    var bar = Native.Bounds(rebar);
                    // With a single band, return to Explorer's default layout; widgets that shift the left
                    // edge (LiteMonitor) then re-apply their own layout from the rebar origin. Extending the
                    // width in place instead would make such a widget shift itself right once more.
                    if (Native.BandCount(rebar) <= 1)
                        Native.MoveWindow(tasklist, 0, 0, bar.Width, bar.Height, true);
                    else
                        Native.MoveWindow(tasklist, rect.Left - bar.Left, rect.Top - bar.Top, bar.Right - rect.Left, rect.Height, true);
                }
            }
            ownedRight = 0;
            reserved = 0;
        }

        public void Dispose()
        {
            Restore();
        }

        /// <summary>Places the panel inside the rebar. Returns false when re-parenting failed (detached again).</summary>
        bool Embed(IntPtr shell, IntPtr bar, IntPtr list, int width, int offset)
        {
            if (taskbar != shell || rebar != bar || tasklist != list)
            {
                Restore();
                taskbar = shell;
                rebar = bar;
                tasklist = list;
            }
            var barRect = Native.Bounds(bar);
            if (barRect.Width <= width + 80)
            {
                form.Hide();
                Mode = "任务栏空间不足";
                return true;
            }
            int x = barRect.Width - width - offset;
            ReserveTaskList(bar, list, barRect, x);

            if (Native.GetParent(form.Handle) != bar)
            {
                int style = Native.GetWindowLong(form.Handle, Native.GWL_STYLE);
                Native.SetWindowLong(form.Handle, Native.GWL_STYLE, (style & ~Native.WS_POPUP) | Native.WS_CHILD | Native.WS_CLIPSIBLINGS);
                Native.SetParent(form.Handle, bar);
            }
            if (Native.GetParent(form.Handle) != bar)
            {
                Detach();
                return false;
            }

            embedded = true;
            if (form.TopMost)
                form.TopMost = false;
            var target = new Rectangle(barRect.Left + x, barRect.Top, width, barRect.Height);
            if (ScreenBounds != target || !Native.IsWindowVisible(form.Handle))
                Native.SetWindowPos(form.Handle, Native.HWND_TOP, x, 0, width, barRect.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            if (!form.Visible)
                form.Show();
            Mode = "Windows 10 任务栏嵌入";
            return true;
        }

        /// <summary>Narrows the task list so it ends where the panel (rebar-relative x) begins.</summary>
        void ReserveTaskList(IntPtr bar, IntPtr list, Rectangle barRect, int x)
        {
            var listRect = Native.Bounds(list);
            int bands = Native.BandCount(bar);
            int? reserve = TaskbarCompatibility.ReserveTarget(barRect, listRect, x, ownedRight, bands);
            if (!reserve.HasValue)
            {
                // Still at our edge but no room left: give it back. Moved by someone else: just forget it.
                if (ownedRight > 0 && Math.Abs(listRect.Right - ownedRight) <= 1)
                    Restore();
                else
                {
                    ownedRight = 0;
                    reserved = 0;
                }
                return;
            }

            int left = listRect.Left - barRect.Left, top = listRect.Top - barRect.Top;
            if (listRect.Width != reserve.Value)
            {
                // With a single band, a left offset comes from another embedded widget (e.g. LiteMonitor).
                // Such widgets re-apply their layout from the task list's current left edge on every change
                // while keeping its right edge, so resizing in place makes them drift right. Starting from
                // the rebar origin lets them land back in their own place; the right edge we set is kept.
                if (bands <= 1 && left > 16)
                    Native.MoveWindow(list, 0, top, x, listRect.Height, true);
                else
                    Native.MoveWindow(list, left, top, reserve.Value, listRect.Height, true);
            }
            ownedRight = barRect.Left + x;
            reserved = Math.Max(0, barRect.Right - ownedRight);
        }

        void Detach()
        {
            Restore();
            Native.SetParent(form.Handle, IntPtr.Zero);
            int style = Native.GetWindowLong(form.Handle, Native.GWL_STYLE);
            Native.SetWindowLong(form.Handle, Native.GWL_STYLE, (style & ~Native.WS_CHILD) | Native.WS_POPUP);
            embedded = false;
            rebar = IntPtr.Zero;
            tasklist = IntPtr.Zero;
        }

        /// <summary>Floating topmost panel left of the notification area; hidden for full-screen apps.</summary>
        void UpdateCompatible(IntPtr shell, Rectangle shellRect, int width, int offset, int dpi)
        {
            taskbar = shell;
            Mode = "托盘左侧定位（兼容模式）";
            IntPtr tray = Native.FindWindowEx(shell, IntPtr.Zero, "TrayNotifyWnd", null);
            int height = Math.Max(1, Math.Min(TaskbarMetrics.Scale(64, dpi), shellRect.Height));
            var monitor = Screen.FromHandle(shell).Bounds;

            var foreground = Native.GetForegroundWindow();
            bool fullscreen = foreground != form.Handle && foreground != shell
                && TaskbarCompatibility.IsFullscreenApp(Native.Bounds(foreground), monitor, Native.Class(foreground));
            if (fullscreen || !Native.IsWindowVisible(shell))
            {
                form.Hide();
                Mode = fullscreen ? "兼容模式：全屏时隐藏" : "兼容模式：任务栏已隐藏";
                return;
            }

            // Reassigning TopMost can activate the panel and dismiss the open details popup.
            if (!form.TopMost)
                form.TopMost = true;
            int trayLeft = tray == IntPtr.Zero ? shellRect.Right - 220 * dpi / 96 : Native.Bounds(tray).Left;
            form.SetBounds(Math.Max(monitor.Left, trayLeft - width - offset), shellRect.Top + (shellRect.Height - height) / 2, width, height);
            if (!form.Visible)
                form.Show();
            Native.SetWindowPos(form.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOMOVE | Native.SWP_NOACTIVATE);
        }
    }
}
