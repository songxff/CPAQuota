using System;
namespace CpaTaskbar {
 public static class ShellDiagnostics {
  public static object Capture(){
   var taskbar=Native.FindWindow("Shell_TrayWnd",null);var rebar=Native.FindWindowEx(taskbar,IntPtr.Zero,"ReBarWindow32",null);var list=Native.FindWindowEx(rebar,IntPtr.Zero,"MSTaskSwWClass",null);var tray=Native.FindWindowEx(taskbar,IntPtr.Zero,"TrayNotifyWnd",null);
   return new{windowsBuild=TaskbarCompatibility.WindowsBuild,canEmbed=TaskbarCompatibility.CanEmbed(TaskbarCompatibility.WindowsBuild,rebar!=IntPtr.Zero&&Native.IsWindowVisible(rebar),list!=IntPtr.Zero&&Native.IsWindowVisible(list),Native.Bounds(taskbar)),taskbar=Native.Bounds(taskbar),rebar=Native.Bounds(rebar),tasklist=Native.Bounds(list),tray=Native.Bounds(tray),taskbarClass=Native.Class(taskbar)};
  }
 }
}
