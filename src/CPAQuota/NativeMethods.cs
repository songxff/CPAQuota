using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace CpaTaskbar
{
    /// <summary>Win32 declarations and thin helpers.</summary>
    public static class Native
    {
        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        public const int WS_CHILD = 0x40000000, WS_CLIPSIBLINGS = 0x04000000;
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int WS_EX_TOOLWINDOW = 0x00000080, WS_EX_LAYERED = 0x00080000, WS_EX_NOACTIVATE = 0x08000000;
        public const int WM_MOUSEACTIVATE = 0x0021, WM_NCHITTEST = 0x0084;
        public const int MA_NOACTIVATE = 3, HTCLIENT = 1;
        public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
        public static readonly IntPtr HWND_TOP = IntPtr.Zero, HWND_TOPMOST = new IntPtr(-1), HWND_BROADCAST = new IntPtr(0xffff);
        public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);
        const int RB_GETBANDCOUNT = 0x040C;
        const int ULW_ALPHA = 2;
        const byte AC_SRC_ALPHA = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;

            public Rectangle Bounds
            {
                get
                {
                    return Rectangle.FromLTRB(Left, Top, Right, Bottom);
                }
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect r);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insert, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);

        public static Rectangle Bounds(IntPtr hwnd)
        {
            Rect r;
            return GetWindowRect(hwnd, out r) ? r.Bounds : Rectangle.Empty;
        }

        public static string Class(IntPtr hwnd)
        {
            var text = new StringBuilder(256);
            GetClassName(hwnd, text, 256);
            return text.ToString();
        }

        /// <summary>Number of bands (toolbars) in a rebar control.</summary>
        public static int BandCount(IntPtr rebar)
        {
            return rebar == IntPtr.Zero ? 0 : (int)SendMessage(rebar, RB_GETBANDCOUNT, IntPtr.Zero, IntPtr.Zero);
        }

        // ----- Layered window presentation -----

        [StructLayout(LayoutKind.Sequential)]
        struct LayerPoint
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct LayerSize
        {
            public int Width, Height;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct LayerBlend
        {
            public byte Operation, Flags, Alpha, Format;
        }

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, IntPtr destination, ref LayerSize size, IntPtr source, ref LayerPoint origin, int color, ref LayerBlend blend, int flags);

        /// <summary>Shows a premultiplied-alpha bitmap as the content of a WS_EX_LAYERED window.</summary>
        public static void PresentLayered(IntPtr window, Bitmap bitmap)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr dc = CreateCompatibleDC(screen);
            IntPtr image = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                image = bitmap.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(dc, image);
                var size = new LayerSize { Width = bitmap.Width, Height = bitmap.Height };
                var origin = new LayerPoint();
                var blend = new LayerBlend { Alpha = 255, Format = AC_SRC_ALPHA };
                if (!UpdateLayeredWindow(window, screen, IntPtr.Zero, ref size, dc, ref origin, 0, ref blend, ULW_ALPHA))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (previous != IntPtr.Zero)
                    SelectObject(dc, previous);
                if (image != IntPtr.Zero)
                    DeleteObject(image);
                if (dc != IntPtr.Zero)
                    DeleteDC(dc);
                if (screen != IntPtr.Zero)
                    ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
