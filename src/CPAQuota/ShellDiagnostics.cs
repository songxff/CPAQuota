using System;

namespace CpaTaskbar
{
    /// <summary>Taskbar window geometry for --inspect-shell.</summary>
    public static class ShellDiagnostics
    {
        public static object Capture()
        {
            var taskbar = Native.FindWindow("Shell_TrayWnd", null);
            var rebar = Native.FindWindowEx(taskbar, IntPtr.Zero, "ReBarWindow32", null);
            var list = Native.FindWindowEx(rebar, IntPtr.Zero, "MSTaskSwWClass", null);
            var tray = Native.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            bool rebarVisible = rebar != IntPtr.Zero && Native.IsWindowVisible(rebar);
            bool listVisible = list != IntPtr.Zero && Native.IsWindowVisible(list);
            return new
            {
                windowsBuild = TaskbarCompatibility.WindowsBuild,
                canEmbed = TaskbarCompatibility.CanEmbed(TaskbarCompatibility.WindowsBuild, rebarVisible, listVisible, Native.Bounds(taskbar)),
                taskbar = Native.Bounds(taskbar),
                rebar = Native.Bounds(rebar),
                tasklist = Native.Bounds(list),
                tray = Native.Bounds(tray),
                taskbarClass = Native.Class(taskbar),
                rebarBands = Native.BandCount(rebar),
            };
        }
    }
}
