using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CpaTaskbar {
 public sealed class CommandHost:NativeWindow,IDisposable{
  public static string InstanceCaption{get{return "CPAQuotaTaskbar."+J.Hash(Application.ExecutablePath.ToLowerInvariant());}}
  public static readonly int OpenMessage=Native.RegisterWindowMessage("CPAQuotaTaskbar.Open.Settings.v1"),ExitMessage=Native.RegisterWindowMessage("CPAQuotaTaskbar.Exit.v1");
  readonly Action open,exit;
  public CommandHost(Action show,Action close){open=show;exit=close;CreateHandle(new CreateParams{Caption=InstanceCaption,Style=unchecked((int)0x80000000),ExStyle=0x80});}
  protected override void WndProc(ref Message m){if(m.Msg==OpenMessage){open();return;}if(m.Msg==ExitMessage){exit();return;}base.WndProc(ref m);}
  public void Dispose(){DestroyHandle();}
 }
 public sealed class TaskbarApp:ApplicationContext{
  public AppConfig Config;
  readonly QuotaService service;
  QuotaStrip strip;
  DetailsForm details;
  SettingsForm settings;
  readonly NotifyIcon tray;
  readonly ContextMenuStrip menu=new ContextMenuStrip();
  readonly Control ui=new Control();
  readonly System.Windows.Forms.Timer tick=new System.Windows.Forms.Timer{Interval=1000};
  readonly CommandHost commands;
  bool closing,refreshingUi;
  int queuedUi,seconds;
  long lastRotate,lastStatus;
  public TaskbarApp(AppConfig c){
   Config=c;var handle=ui.Handle;service=new QuotaService(c);
   strip=NewStrip();
   tray=new NotifyIcon{Text="CPA 额度状态栏",Icon=AppIcon(),Visible=false,ContextMenuStrip=menu};tray.DoubleClick+=(s,e)=>ToggleDetails();tray.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left)ToggleDetails();};
   menu.Opening+=(s,e)=>BuildMenu();commands=new CommandHost(ShowSettings,()=>ExitThread());
   service.Changed+=OnChanged;
   tick.Tick+=(s,e)=>{
    if(closing)return;
    if(strip.IsDisposed)strip=NewStrip();
    try{strip.Docking.Update(DesiredWidth(),Config.Offset,Config.PhysicalPixels);tray.Visible=!strip.Docking.Mode.StartsWith("Windows 10");}catch{ /* Explorer重建时下一轮重试 */ }
    strip.RefreshClock();
    if(details!=null&&details.Visible)details.Canvas.Invalidate();
    if(settings!=null&&!settings.IsDisposed)settings.RefreshClock();
    if(++seconds%5==0)Refresh(false);
    if(Config.Rotate&&J.Now-lastRotate>Config.RotateSeconds*1000){
     var a=service.State.Accounts.Where(x=>x.Supported&&Config.SelectedIds.Contains(x.Id)).ToList();
     if(a.Count>1){int index=a.FindIndex(x=>x.Id==Config.FocusId);SelectAccount(a[(index+1+a.Count)%a.Count].Id,false);}lastRotate=J.Now;
    }
    if(J.Now-lastStatus>30000){WriteStatus();lastStatus=J.Now;}
   };
   tick.Start();RefreshUI();Refresh(true);
   if(String.IsNullOrEmpty(c.CpaSecret))ui.BeginInvoke((Action)ShowSettings);
  }
  QuotaStrip NewStrip(){var f=new QuotaStrip(Config);f.Clicked+=ToggleDetails;f.ContextMenuStrip=menu;return f;}
  int DesiredWidth(){return TaskbarMetrics.TotalWidth(Config);}
  void BuildMenu(){
   menu.Items.Clear();var caption=new ToolStripMenuItem("CPA 额度状态栏"){Enabled=false};menu.Items.Add(caption);
   var submenu=new ToolStripMenuItem("切换账号");
   foreach(var a in service.State.Accounts.Where(a=>a.Supported&&(Config.SelectedIds.Count==0||Config.SelectedIds.Contains(a.Id)))){
    string id=a.Id;var item=new ToolStripMenuItem(a.ToString()){Checked=a.Id==Config.FocusId};item.Click+=(s,e)=>SelectAccount(id,true);submenu.DropDownItems.Add(item);
   }
   menu.Items.Add(submenu);menu.Items.Add("查看详情",null,(s,e)=>ToggleDetails());menu.Items.Add("刷新额度",null,(s,e)=>Refresh(true));menu.Items.Add(new ToolStripSeparator());menu.Items.Add("设置…",null,(s,e)=>ShowSettings());menu.Items.Add("退出",null,(s,e)=>ExitThread());
  }
  void Refresh(bool force){if(closing)return;Task.Run(()=>service.RefreshAsync(force));}
  void OnChanged(){
   if(closing||Interlocked.Exchange(ref queuedUi,1)==1)return;
   try{ui.BeginInvoke((Action)(()=>{Interlocked.Exchange(ref queuedUi,0);if(!closing)RefreshUI();}));}catch{}
  }
  public void RefreshUI(){
   var state=service.State;
   if(Config.SelectedIds.Count==0&&state.Accounts.Any(a=>a.Supported&&!a.Disabled)){
    var first=state.Accounts.FirstOrDefault(a=>a.Provider=="codex"&&!a.Disabled)??state.Accounts.First(a=>a.Supported&&!a.Disabled);
    Config.SelectedIds.Add(first.Id);Config.FocusId=first.Id;service.SetSelection(Config.SelectedIds,first.Id);
    try{Config.Save();}catch{}
   }
   strip.Config=Config;strip.RefreshState(state);
   try{strip.Docking.Update(DesiredWidth(),Config.Offset,Config.PhysicalPixels);tray.Visible=!strip.Docking.Mode.StartsWith("Windows 10");}catch{}
   if(details!=null){refreshingUi=true;try{details.UpdateData(state,Config);}finally{refreshingUi=false;}}
   if(settings!=null&&!settings.IsDisposed)settings.UpdateAccounts(state);
   var account=Look.Focus(state,Config);
   string tip=account==null?"CPA 额度状态栏":"CPA 额度 · "+J.Provider(account.Provider);
   if(tip.Length<=63)tray.Text=tip;
   WriteStatus();
  }
  void SelectAccount(string id,bool persist){
   if(Config.FocusId==id)return;Config.FocusId=id;service.SetSelection(Config.SelectedIds,id);
   if(persist)try{Config.Save();}catch{}RefreshUI();Refresh(false);
  }
  void EnsureDetails(){
   if(details!=null)return;
   details=new DetailsForm();details.SettingsRequested+=ShowSettings;details.RefreshRequested+=()=>Refresh(true);
   details.Accounts.SelectedIndexChanged+=(s,e)=>{if(!refreshingUi){var a=details.Accounts.SelectedItem as Account;if(a!=null)SelectAccount(a.Id,true);}};
   details.Icon=tray.Icon;
  }
  void ToggleDetails(){EnsureDetails();if(details.Visible){details.Hide();return;}refreshingUi=true;try{details.UpdateData(service.State,Config);}finally{refreshingUi=false;}details.ShowNear(strip.Docking.ScreenBounds);}
  public void ShowSettings(){
   if(closing)return;if(details!=null)details.Hide();
   if(settings!=null&&!settings.IsDisposed){settings.Show();settings.Activate();return;}
   settings=new SettingsForm(Config,service,service.State);settings.Icon=tray.Icon;
   settings.Applied+=next=>{Config=next;service.Apply(next);RefreshUI();Refresh(true);};
   settings.Show();settings.Activate();Native.SetForegroundWindow(settings.Handle);
  }
  void WriteStatus(){
   try{lastStatus=J.Now;Directory.CreateDirectory(Path.Combine(AppConfig.Folder,"data"));var s=service.State;var a=Look.Focus(s,Config);
    File.WriteAllText(Path.Combine(AppConfig.Folder,"data","desktop-status.json"),J.Json(new{app="CPAQuotaTaskbar",version="0.6.0",pid=System.Diagnostics.Process.GetCurrentProcess().Id,dock=strip.Docking.Diagnostics(),appearance=new{width=Config.Width,offset=Config.Offset,physicalPixels=Config.PhysicalPixels,fontSize=Config.FontSize,theme=Config.Theme,singleLine=Config.SingleLine,resetTimeMode=Config.ResetTimeMode},accountCount=s.Accounts.Count,provider=a==null?"":a.Provider,busy=s.Busy,error=s.Error,quotaError=Look.Quota(s,a)==null?"":Look.Quota(s,a).Error,quotaUpdatedAt=Look.Quota(s,a)==null?0:Look.Quota(s,a).UpdatedAt,historyError=Look.History(s,a)==null?"":Look.History(s,a).Error,historyUpdatedAt=Look.History(s,a)==null?0:Look.History(s,a).UpdatedAt,updatedAt=J.Now}),new System.Text.UTF8Encoding(false));}
   catch{}
  }
  protected override void ExitThreadCore(){
   if(closing)return;closing=true;tick.Stop();service.Changed-=OnChanged;service.Dispose();
   if(settings!=null&&!settings.IsDisposed)settings.Close();if(details!=null)details.Dispose();strip.Dispose();Look.ReleaseStripFonts();tray.Visible=false;tray.Dispose();menu.Dispose();commands.Dispose();ui.Dispose();tick.Dispose();base.ExitThreadCore();
  }
  static Icon AppIcon(){
   using(var bitmap=new Bitmap(32,32))using(var g=Graphics.FromImage(bitmap)){
    g.Clear(Color.Transparent);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    using(var b=new SolidBrush(Look.Blue))g.FillEllipse(b,1,1,30,30);
    using(var pen=new Pen(Color.White,2.6f)){g.DrawLine(pen,10,21,10,15);g.DrawLine(pen,16,21,16,9);g.DrawLine(pen,22,21,22,12);}
    IntPtr icon=bitmap.GetHicon();try{return (Icon)Icon.FromHandle(icon).Clone();}finally{DestroyIcon(icon);}
   }
  }
  [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr h);
 }
 static class Program{
  [STAThread]static int Main(string[] args){
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   try{Native.SetProcessDpiAwarenessContext(new IntPtr(-4));}catch{}
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   try{
    if(args.Length>1&&args[0]=="--inspect-shell"){File.WriteAllText(args[1],J.Json(ShellDiagnostics.Capture()));return 0;}
    if(args.Contains("--uninstall-cleanup")){
     IntPtr host=Native.FindWindow(null,CommandHost.InstanceCaption);
     if(host!=IntPtr.Zero)Native.PostMessage(host,CommandHost.ExitMessage,IntPtr.Zero,IntPtr.Zero);
     using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)){
      string expected="\""+Application.ExecutablePath+"\" --background";
      if(key!=null&&String.Equals(key.GetValue("CPAQuotaTaskbar") as string,expected,StringComparison.OrdinalIgnoreCase))key.DeleteValue("CPAQuotaTaskbar",false);
     }
     return 0;
    }
    if(args.Length>1&&args[0]=="--inspect-storage"){
     AppConfig.InitializeStorage();File.WriteAllText(args[1],J.Json(new{folder=AppConfig.Folder,portable=String.Equals(Path.GetFullPath(AppConfig.Folder).TrimEnd(Path.DirectorySeparatorChar),Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)}));return 0;
    }
    if(args.Contains("--exit")){Native.PostMessage(new IntPtr(0xffff),CommandHost.ExitMessage,IntPtr.Zero,IntPtr.Zero);return 0;}
    bool created;using(var mutex=new Mutex(true,@"Local\CPAQuotaTaskbar."+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value,out created)){
     if(!created){Native.PostMessage(new IntPtr(0xffff),CommandHost.OpenMessage,IntPtr.Zero,IntPtr.Zero);return 0;}
     Application.ThreadException+=(s,e)=>Log("界面发生异常，可退出后重新启动。");
     AppConfig.InitializeStorage();var c=AppConfig.Load();Application.Run(new TaskbarApp(c));GC.KeepAlive(mutex);
    }
    return 0;
   }catch(Exception e){
    Log(e.Message);MessageBox.Show("CPA状态栏启动失败："+e.Message,"CPA 状态栏",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;
   }
  }
  static void Log(string message){try{Directory.CreateDirectory(Path.Combine(AppConfig.Folder,"logs"));File.AppendAllText(Path.Combine(AppConfig.Folder,"logs","desktop.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine);}catch{}}
 }
}
