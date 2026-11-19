using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CpaTaskbar {
 public static class Native {
  public const int GWL_STYLE=-16,WS_CHILD=0x40000000,WS_CLIPSIBLINGS=0x04000000;
  public const int WS_POPUP=unchecked((int)0x80000000);
  public const int WS_EX_LAYERED=0x00080000;
  [StructLayout(LayoutKind.Sequential)]struct LayerPoint{public int X,Y;}
  [StructLayout(LayoutKind.Sequential)]struct LayerSize{public int Width,Height;}
  [StructLayout(LayoutKind.Sequential,Pack=1)]struct LayerBlend{public byte Operation,Flags,Alpha,Format;}
  [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr window);
  [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
  [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
  [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
  [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
  [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr obj);
  [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr window,IntPtr screen,IntPtr destination,ref LayerSize size,IntPtr source,ref LayerPoint origin,int color,ref LayerBlend blend,int flags);
  public static void PresentLayered(IntPtr window,Bitmap bitmap){
   IntPtr screen=GetDC(IntPtr.Zero),dc=CreateCompatibleDC(screen),image=IntPtr.Zero,previous=IntPtr.Zero;
   try{
    image=bitmap.GetHbitmap(Color.FromArgb(0));previous=SelectObject(dc,image);
    var size=new LayerSize{Width=bitmap.Width,Height=bitmap.Height};var origin=new LayerPoint();var blend=new LayerBlend{Alpha=255,Format=1};
    if(!UpdateLayeredWindow(window,screen,IntPtr.Zero,ref size,dc,ref origin,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   }finally{if(previous!=IntPtr.Zero)SelectObject(dc,previous);if(image!=IntPtr.Zero)DeleteObject(image);if(dc!=IntPtr.Zero)DeleteDC(dc);if(screen!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,screen);}
  }
  [StructLayout(LayoutKind.Sequential)]public struct Rect{public int Left,Top,Right,Bottom;public Rectangle Bounds{get{return Rectangle.FromLTRB(Left,Top,Right,Bottom);}}}
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindow(string cls,string title);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
  [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr hwnd,out Rect r);
  [DllImport("user32.dll")]public static extern IntPtr GetParent(IntPtr hwnd);
  [DllImport("user32.dll")]public static extern IntPtr SetParent(IntPtr child,IntPtr parent);
  [DllImport("user32.dll")]public static extern int GetWindowLong(IntPtr hwnd,int index);
  [DllImport("user32.dll")]public static extern int SetWindowLong(IntPtr hwnd,int index,int value);
  [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr hwnd,IntPtr insert,int x,int y,int width,int height,uint flags);
  [DllImport("user32.dll")]public static extern bool MoveWindow(IntPtr hwnd,int x,int y,int width,int height,bool repaint);
  [DllImport("user32.dll")]public static extern bool IsWindow(IntPtr hwnd);
  [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")]public static extern uint GetDpiForWindow(IntPtr hwnd);
  [DllImport("user32.dll")]public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]public static extern IntPtr WindowFromPoint(Point point);
  [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int RegisterWindowMessage(string name);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern bool PostMessage(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam);
  [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam);
  public static int BandCount(IntPtr rebar){return rebar==IntPtr.Zero?0:(int)SendMessage(rebar,0x040C,IntPtr.Zero,IntPtr.Zero);} // RB_GETBANDCOUNT
  [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetClassName(IntPtr hwnd,StringBuilder text,int max);
  public static Rectangle Bounds(IntPtr hwnd){Rect r;return GetWindowRect(hwnd,out r)?r.Bounds:Rectangle.Empty;}
  public static string Class(IntPtr hwnd){var sb=new StringBuilder(256);GetClassName(hwnd,sb,256);return sb.ToString();}
 }
 public static class TaskbarCompatibility {
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct OsVersionInfo {
   public int Size,Major,Minor,Build,Platform;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string ServicePack;
  }
  [DllImport("ntdll.dll",CharSet=CharSet.Unicode)]static extern int RtlGetVersion(ref OsVersionInfo version);
  public static readonly int WindowsBuild=ReadWindowsBuild();
  static int ReadWindowsBuild(){
   try{var version=new OsVersionInfo();version.Size=Marshal.SizeOf(typeof(OsVersionInfo));if(RtlGetVersion(ref version)==0&&version.Build>0)return version.Build;}catch{}
   // An unknown platform should use the floating panel rather than modify Explorer.
   return Int32.MaxValue;
  }
  public static bool CanEmbed(int build,bool rebarVisible,bool tasklistVisible,Rectangle taskbar){
   // Windows 11 can expose legacy taskbar windows which are not the displayed taskbar.
   return build>0&&build<22000&&rebarVisible&&tasklistVisible&&taskbar.Width>taskbar.Height;
  }
  // Width the task list should have so its right edge stops at the embedded panel (panelX is
  // relative to the rebar), or null when it must be left alone. This dock owns only the right
  // edge: other taskbar widgets (e.g. LiteMonitor) move the task list's left edge, and that is
  // kept as is. Ownership is tracked by the right edge we last set (ownedRight, screen x), which
  // survives those widgets shifting the left edge. Without ownership the list is only shrunk
  // when it overlaps the panel, and never when a second rebar band (a toolbar) may sit to its right.
  public static int? ReserveTarget(Rectangle rebar,Rectangle tasklist,int panelX,int ownedRight,int bands){
   if(rebar.IsEmpty||tasklist.IsEmpty)return null;
   int panelLeft=rebar.Left+panelX;
   bool owned=ownedRight>0&&Math.Abs(tasklist.Right-ownedRight)<=1;
   if(!owned&&tasklist.Right<=panelLeft)return null;
   if(!owned&&bands>1&&tasklist.Right<rebar.Right-2)return null;
   int width=panelLeft-tasklist.Left;
   return width<64?(int?)null:width;
  }
  public static bool IsFullscreenApp(Rectangle foreground,Rectangle monitor,string windowClass){
   return windowClass!="Progman"&&windowClass!="WorkerW"&&windowClass!="Shell_TrayWnd"&&windowClass!="Shell_SecondaryTrayWnd"&&!monitor.IsEmpty&&foreground.Contains(monitor);
  }
 }
 public static class TaskbarMetrics {
  public static int Dpi(){try{var shell=Native.FindWindow("Shell_TrayWnd",null);int dpi=(int)Native.GetDpiForWindow(shell);return dpi>0?dpi:96;}catch{return 96;}}
  public static int Scale(int value,int dpi){return value*dpi/96;}
  public static int Width(int value,int dpi,bool physical){return physical?Math.Max(80,value):Scale(Math.Max(100,value),dpi);}
  public static int Offset(int value,int dpi,bool physical){return physical?Math.Max(0,value):Scale(Math.Max(0,value),dpi);}
  public static int TotalWidth(AppConfig c){return c.Width+(c.HistoryInBar&&c.CpampEnabled?(c.PhysicalPixels?Scale(76,Dpi()):76):0);}
  public static int PixelWidth(AppConfig c){return Width(c.Width,Dpi(),c.PhysicalPixels);}
  public static int PixelOffset(AppConfig c){return Offset(c.Offset,Dpi(),c.PhysicalPixels);}
 }
 public sealed class TaskbarDock:IDisposable {
  readonly Form form;
  IntPtr taskbar,rebar,tasklist;
  int ownedRight=0,reserved=0;
  bool embedded;
  public string Mode{get;private set;}
  public TaskbarDock(Form value){form=value;Mode="等待任务栏";}
  public Rectangle ScreenBounds{get{return form.IsHandleCreated?Native.Bounds(form.Handle):Rectangle.Empty;}}
  public void Update(int logicalWidth,int logicalOffset,bool physicalPixels=false){
   IntPtr shell=Native.FindWindow("Shell_TrayWnd",null);
   if(shell==IntPtr.Zero){form.Hide();Mode="任务栏暂不可用";return;}
   var shellRect=Native.Bounds(shell);
   if(shellRect.IsEmpty){form.Hide();Mode="任务栏暂不可用";return;}
   int dpi=96;try{dpi=(int)Native.GetDpiForWindow(shell);if(dpi==0)dpi=96;}catch{}
   int width=TaskbarMetrics.Width(logicalWidth,dpi,physicalPixels),offset=TaskbarMetrics.Offset(logicalOffset,dpi,physicalPixels);
   IntPtr bar=Native.FindWindowEx(shell,IntPtr.Zero,"ReBarWindow32",null);
   IntPtr list=bar==IntPtr.Zero?IntPtr.Zero:Native.FindWindowEx(bar,IntPtr.Zero,"MSTaskSwWClass",null);
   if(list==IntPtr.Zero&&bar!=IntPtr.Zero)list=Native.FindWindowEx(bar,IntPtr.Zero,"MSTaskListWClass",null);
   if(TaskbarCompatibility.CanEmbed(TaskbarCompatibility.WindowsBuild,bar!=IntPtr.Zero&&Native.IsWindowVisible(bar),list!=IntPtr.Zero&&Native.IsWindowVisible(list),shellRect)){
    if(taskbar!=shell||rebar!=bar||tasklist!=list){Restore();taskbar=shell;rebar=bar;tasklist=list;}
    var br=Native.Bounds(bar);var lr=Native.Bounds(list);
    if(br.Width<=width+80){form.Hide();Mode="任务栏空间不足";return;}
    int x=br.Width-width-offset,y=0,h=br.Height;
    int bands=Native.BandCount(bar);
    int? reserve=TaskbarCompatibility.ReserveTarget(br,lr,x,ownedRight,bands);
    if(!reserve.HasValue){
     // Still at our edge but no room left: give it back. Moved by someone else: just forget it.
     if(ownedRight>0&&Math.Abs(lr.Right-ownedRight)<=1)Restore();else{ownedRight=0;reserved=0;}
    }else{
     int rel=lr.Left-br.Left;
     if(lr.Width!=reserve.Value){
      // With a single band, a left offset comes from another embedded widget (e.g. LiteMonitor). Such
      // widgets re-apply their layout from the task list's current left edge on every change while
      // keeping its right edge, so resizing in place makes them drift right. Starting from the rebar
      // origin lets them land back in their own place; the right edge we set is preserved.
      if(bands<=1&&rel>16)Native.MoveWindow(list,0,lr.Top-br.Top,x,lr.Height,true);
      else Native.MoveWindow(list,rel,lr.Top-br.Top,reserve.Value,lr.Height,true);
     }
     ownedRight=br.Left+x;reserved=Math.Max(0,br.Right-ownedRight);
    }
    if(Native.GetParent(form.Handle)!=bar){
     int style=Native.GetWindowLong(form.Handle,Native.GWL_STYLE);
     Native.SetWindowLong(form.Handle,Native.GWL_STYLE,(style&~Native.WS_POPUP)|Native.WS_CHILD|Native.WS_CLIPSIBLINGS);
     Native.SetParent(form.Handle,bar);
    }
    if(Native.GetParent(form.Handle)!=bar){
     Detach();UpdateCompatible(shell,shellRect,width,offset,dpi);return;
    }
    embedded=true;if(form.TopMost)form.TopMost=false;
    if(ScreenBounds!=new Rectangle(br.Left+x,br.Top+y,width,h)||!Native.IsWindowVisible(form.Handle))Native.SetWindowPos(form.Handle,new IntPtr(0),x,y,width,h,0x0010|0x0040);
    if(!form.Visible)form.Show();
    Mode="Windows 10 任务栏嵌入";
   }else{
    if(embedded)Detach();
    UpdateCompatible(shell,shellRect,width,offset,dpi);
   }
  }
  void Detach(){
   Restore();Native.SetParent(form.Handle,IntPtr.Zero);int style=Native.GetWindowLong(form.Handle,Native.GWL_STYLE);Native.SetWindowLong(form.Handle,Native.GWL_STYLE,(style&~Native.WS_CHILD)|Native.WS_POPUP);embedded=false;
   rebar=IntPtr.Zero;tasklist=IntPtr.Zero;
  }
  void UpdateCompatible(IntPtr shell,Rectangle shellRect,int width,int offset,int dpi){
    taskbar=shell;Mode="托盘左侧定位（兼容模式）";
    IntPtr tray=Native.FindWindowEx(shell,IntPtr.Zero,"TrayNotifyWnd",null);
    var tr=tray==IntPtr.Zero?shellRect:Native.Bounds(tray);
    int height=Math.Max(1,Math.Min(TaskbarMetrics.Scale(64,dpi),shellRect.Height));
    var foreground=Native.GetForegroundWindow();var fr=Native.Bounds(foreground);var monitor=Screen.FromHandle(shell).Bounds;
    bool fullscreen=foreground!=form.Handle&&foreground!=shell&&TaskbarCompatibility.IsFullscreenApp(fr,monitor,Native.Class(foreground));
    if(fullscreen||!Native.IsWindowVisible(shell)){form.Hide();Mode=fullscreen?"兼容模式：全屏时隐藏":"兼容模式：任务栏已隐藏";return;}
    // Reassigning TopMost can activate the panel and dismiss the open details popup.
    if(!form.TopMost)form.TopMost=true;form.SetBounds(Math.Max(monitor.Left,(tray==IntPtr.Zero?shellRect.Right-220*dpi/96:tr.Left)-width-offset),shellRect.Top+(shellRect.Height-height)/2,width,height);if(!form.Visible)form.Show();
    Native.SetWindowPos(form.Handle,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010);
  }
  public object Diagnostics(){return new{mode=Mode,windowsBuild=TaskbarCompatibility.WindowsBuild,taskbar=Native.Bounds(taskbar),parent=Native.Class(Native.GetParent(form.Handle)),widget=ScreenBounds,visible=Native.IsWindowVisible(form.Handle),tasklist=Native.Bounds(tasklist),reservedWidth=reserved};}
  public void Restore(){
   if(tasklist!=IntPtr.Zero&&Native.IsWindow(tasklist)&&rebar!=IntPtr.Zero&&ownedRight>0){
    var rect=Native.Bounds(tasklist);
    if(Math.Abs(rect.Right-ownedRight)<=1){
     var bar=Native.Bounds(rebar);
     // With a single band, return to Explorer's default layout; widgets that shift the left
     // edge (LiteMonitor) then re-apply their own layout from the rebar origin. Extending the
     // width in place instead would make such a widget shift itself right once more.
     if(Native.BandCount(rebar)<=1)Native.MoveWindow(tasklist,0,0,bar.Width,bar.Height,true);
     else Native.MoveWindow(tasklist,rect.Left-bar.Left,rect.Top-bar.Top,bar.Right-rect.Left,rect.Height,true);
    }
   }
   ownedRight=0;reserved=0;
  }
  public void Dispose(){Restore();}
 }
}
