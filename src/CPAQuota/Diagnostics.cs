using System;
namespace CpaTaskbar {
 public static class ShellDiagnostics {
  public static object Capture(){
   var taskbar=Native.FindWindow("Shell_TrayWnd",null);var rebar=Native.FindWindowEx(taskbar,IntPtr.Zero,"ReBarWindow32",null);var list=Native.FindWindowEx(rebar,IntPtr.Zero,"MSTaskSwWClass",null);var tray=Native.FindWindowEx(taskbar,IntPtr.Zero,"TrayNotifyWnd",null);
   return new{taskbar=Native.Bounds(taskbar),rebar=Native.Bounds(rebar),tasklist=Native.Bounds(list),tray=Native.Bounds(tray),taskbarClass=Native.Class(taskbar)};
  }
 }
}
