using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CpaTaskbar {
 public static class Native {
  public const int GWL_STYLE=-16,WS_CHILD=0x40000000,WS_CLIPSIBLINGS=0x04000000;
  public const int WS_POPUP=unchecked((int)0x80000000);
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
  [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int RegisterWindowMessage(string name);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern bool PostMessage(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetClassName(IntPtr hwnd,StringBuilder text,int max);
  public static Rectangle Bounds(IntPtr hwnd){Rect r;return GetWindowRect(hwnd,out r)?r.Bounds:Rectangle.Empty;}
  public static string Class(IntPtr hwnd){var sb=new StringBuilder(256);GetClassName(hwnd,sb,256);return sb.ToString();}
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
  int squeezed=-1,reserved=0;
  Rectangle original;
  bool embedded;
  public string Mode{get;private set;}
  public TaskbarDock(Form value){form=value;Mode="等待任务栏";}
  public Rectangle ScreenBounds{get{return form.IsHandleCreated?Native.Bounds(form.Handle):Rectangle.Empty;}}
  public void Update(int logicalWidth,int logicalOffset,bool physicalPixels=false){
   IntPtr shell=Native.FindWindow("Shell_TrayWnd",null);
   if(shell==IntPtr.Zero){form.Hide();Mode="任务栏暂不可用";return;}
   var shellRect=Native.Bounds(shell);
   if(shellRect.IsEmpty)return;
   int dpi=96;try{dpi=(int)Native.GetDpiForWindow(shell);if(dpi==0)dpi=96;}catch{}
   int width=TaskbarMetrics.Width(logicalWidth,dpi,physicalPixels),offset=TaskbarMetrics.Offset(logicalOffset,dpi,physicalPixels);
   IntPtr bar=Native.FindWindowEx(shell,IntPtr.Zero,"ReBarWindow32",null);
   IntPtr list=bar==IntPtr.Zero?IntPtr.Zero:Native.FindWindowEx(bar,IntPtr.Zero,"MSTaskSwWClass",null);
   if(list==IntPtr.Zero&&bar!=IntPtr.Zero)list=Native.FindWindowEx(bar,IntPtr.Zero,"MSTaskListWClass",null);
   if(bar!=IntPtr.Zero&&list!=IntPtr.Zero&&shellRect.Width>shellRect.Height){
    if(taskbar!=shell||rebar!=bar||tasklist!=list){Restore();taskbar=shell;rebar=bar;tasklist=list;squeezed=-1;}
    var br=Native.Bounds(bar);var lr=Native.Bounds(list);
    if(br.Width<=width+80){form.Hide();Mode="任务栏空间不足";return;}
    int x=br.Width-width-offset,y=0,h=br.Height;
    int relX=lr.Left-br.Left,relY=lr.Top-br.Top;
    // A nonzero origin belongs to an existing toolbar layout; leave that layout intact.
    bool externalLayout=relX>0||relY>0;
    if(externalLayout){if(reserved>0)Restore();}
    else{
     int target=Math.Max(64,x-relX);
     if(reserved==0)original=new Rectangle(relX,relY,br.Width-relX,lr.Height);
     if(lr.Width!=target)Native.MoveWindow(list,relX,relY,target,lr.Height,true);
     reserved=Math.Max(0,br.Width-relX-target);squeezed=target;
    }
    if(Native.GetParent(form.Handle)!=bar){
     int style=Native.GetWindowLong(form.Handle,Native.GWL_STYLE);
     Native.SetWindowLong(form.Handle,Native.GWL_STYLE,(style&~Native.WS_POPUP)|Native.WS_CHILD|Native.WS_CLIPSIBLINGS);
     Native.SetParent(form.Handle,bar);
    }
    embedded=true;form.TopMost=false;
    if(ScreenBounds!=new Rectangle(br.Left+x,br.Top+y,width,h)||!Native.IsWindowVisible(form.Handle))Native.SetWindowPos(form.Handle,new IntPtr(0),x,y,width,h,0x0010|0x0040);
    if(!form.Visible)form.Show();
    Mode="Windows 10 任务栏嵌入";
   }else{
    if(embedded){Restore();Native.SetParent(form.Handle,IntPtr.Zero);int style=Native.GetWindowLong(form.Handle,Native.GWL_STYLE);Native.SetWindowLong(form.Handle,Native.GWL_STYLE,(style&~Native.WS_CHILD)|Native.WS_POPUP);embedded=false;}
    taskbar=shell;
    IntPtr tray=Native.FindWindowEx(shell,IntPtr.Zero,"TrayNotifyWnd",null);
    var tr=tray==IntPtr.Zero?shellRect:Native.Bounds(tray);
    int height=Math.Max(32,Math.Min(64,shellRect.Height));
    if(shellRect.Width<shellRect.Height){width=TaskbarMetrics.Width(logicalWidth,dpi,physicalPixels);height=48;}
    var foreground=Native.GetForegroundWindow();var fr=Native.Bounds(foreground);var monitor=Screen.FromHandle(shell).Bounds;
    bool fullscreen=foreground!=form.Handle&&foreground!=shell&&fr.Width>=monitor.Width&&fr.Height>=monitor.Height;
    if(fullscreen||!Native.IsWindowVisible(shell)){form.Hide();return;}
    form.TopMost=true;form.SetBounds(Math.Max(Screen.FromHandle(shell).Bounds.Left,(tray==IntPtr.Zero?shellRect.Right-220*dpi/96:tr.Left)-width-offset),shellRect.Top+(shellRect.Height-height)/2,width,height);if(!form.Visible)form.Show();
    Mode="托盘左侧定位（兼容模式）";
   }
  }
  public object Diagnostics(){return new{mode=Mode,taskbar=Native.Bounds(taskbar),parent=Native.Class(Native.GetParent(form.Handle)),widget=ScreenBounds,visible=Native.IsWindowVisible(form.Handle),tasklist=Native.Bounds(tasklist),reservedWidth=reserved};}
  public void Restore(){
   if(tasklist!=IntPtr.Zero&&Native.IsWindow(tasklist)&&reserved>0){
    var rect=Native.Bounds(tasklist);
    if(Math.Abs(rect.Width-squeezed)<=1){var bar=Native.Bounds(rebar);Native.MoveWindow(tasklist,original.X,original.Y,Math.Max(original.Width,bar.Width-original.X),original.Height,true);}
   }
   squeezed=-1;reserved=0;
  }
  public void Dispose(){Restore();}
 }
}
