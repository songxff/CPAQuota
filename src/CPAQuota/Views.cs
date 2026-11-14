using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CpaTaskbar {
 public static class Look {
  public static Color Ink=Color.FromArgb(34,47,68),Muted=Color.FromArgb(99,112,133),Line=Color.FromArgb(221,227,237),Blue=Color.FromArgb(48,91,207);
  public static Font Font(float size=9,FontStyle style=FontStyle.Regular){return new Font("Segoe UI",size,style,GraphicsUnit.Point);}
  public static bool Dark(AppConfig c){if(c.Theme=="dark")return true;if(c.Theme=="light")return false;try{using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))return Convert.ToInt32(k==null?0:k.GetValue("SystemUsesLightTheme",0))==0;}catch{return true;}}
  public static Color Status(double? remaining,bool dark){return !remaining.HasValue?(dark?Color.FromArgb(181,195,213):Muted):remaining<=10?(dark?Color.FromArgb(255,149,163):Color.FromArgb(186,45,66)):remaining<=25?(dark?Color.FromArgb(247,205,124):Color.FromArgb(150,93,15)):(dark?Color.FromArgb(151,227,195):Color.FromArgb(28,119,80));}
  public static void Text(Graphics g,string text,Font f,Color color,Rectangle rect,TextFormatFlags flags=TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding){TextRenderer.DrawText(g,text??"",f,rect,color,flags);}
  public static bool Fresh(QuotaState q,AppConfig c){return q!=null&&q.Error==""&&q.UpdatedAt>0&&J.Now-q.UpdatedAt<c.RefreshSeconds*2500;}
  public static Account Focus(Snapshot s,AppConfig c){return s.Accounts.FirstOrDefault(a=>a.Id==c.FocusId&&a.Supported)??s.Accounts.FirstOrDefault(a=>c.SelectedIds.Contains(a.Id)&&a.Supported)??s.Accounts.FirstOrDefault(a=>a.Provider=="codex"&&!a.Disabled)??s.Accounts.FirstOrDefault(a=>a.Supported&&!a.Disabled);}
  public static QuotaState Quota(Snapshot s,Account a){QuotaState q;return a!=null&&s.Quotas.TryGetValue(a.Id,out q)?q:null;}
  public static HistoryState History(Snapshot s,Account a){HistoryState h;return a!=null&&s.History.TryGetValue(a.Id,out h)?h:null;}
  public static List<QuotaWindow> BarWindows(QuotaState q,AppConfig c,Account a){
   var windows=q==null?new List<QuotaWindow>():q.Windows;
   if(a==null||a.Provider=="codex"||a.Provider=="claude"){
    return new List<QuotaWindow>{windows.FirstOrDefault(w=>w.Id=="5h")??new QuotaWindow{Id="5h",Label="5小时"},windows.FirstOrDefault(w=>w.Id=="week")??new QuotaWindow{Id="week",Label="一周"}};
   }
   var filtered=windows.Where(w=>c.Model==""||(w.Label+" "+w.Scope+" "+w.Id).IndexOf(c.Model,StringComparison.OrdinalIgnoreCase)>=0).ToList();
   return filtered.OrderBy(w=>w.Remaining??101).Take(1).ToList();
  }
  public static string Value(QuotaWindow w,bool fresh,AppConfig c){return !fresh||w==null||!w.Valid?"—":Math.Round(c.ShowUsed?w.Used.Value:w.Remaining.Value).ToString()+"%";}
  static Font stripLabel,stripNumber;static float stripScale;
  public static void ReleaseStripFonts(){if(stripLabel!=null)stripLabel.Dispose();if(stripNumber!=null)stripNumber.Dispose();stripLabel=stripNumber=null;}
  static void StripFonts(float scale,out Font label,out Font number){
   if(stripLabel==null||Math.Abs(stripScale-scale)>.001f){
    ReleaseStripFonts();stripScale=scale;
    stripLabel=new Font("Segoe UI",Math.Max(10,11*scale),FontStyle.Regular,GraphicsUnit.Pixel);
    stripNumber=new Font("Segoe UI",Math.Max(11,13*scale),FontStyle.Bold,GraphicsUnit.Pixel);
   }
   label=stripLabel;number=stripNumber;
  }
  public static Color StripSurface(bool dark,bool hover){return hover?(dark?Color.FromArgb(39,50,64):Color.FromArgb(223,231,243)):(dark?Color.FromArgb(26,35,45):Color.FromArgb(238,241,246));}
  public static void Strip(Graphics g,Rectangle rect,Snapshot s,AppConfig c,bool hover=false){
   bool dark=Dark(c);Color bg=StripSurface(dark,hover),fg=dark?Color.FromArgb(232,239,249):Ink,muted=dark?Color.FromArgb(180,192,207):Muted;
   g.Clear(bg);
   Account a=Focus(s,c);QuotaState q=Quota(s,a);bool fresh=Fresh(q,c);
   float scale=Math.Max(.8f,rect.Height/40f)*c.FontSize/9f;
   Font label,number;StripFonts(scale,out label,out number);
   {
    int inset=Math.Max(5,(int)(7*scale));
    int historyWidth=c.HistoryInBar&&c.CpampEnabled?(int)(74*scale):0;
    int quotaWidth=Math.Max(1,rect.Width-historyWidth);
    if(a==null||s.Error!=""){
     string first=s.Error!=""?"连接异常":String.IsNullOrEmpty(c.CpaSecret)?"未配置":"连接中";
     Look.Text(g,first,number,fg,new Rectangle(inset,1,Math.Max(1,quotaWidth-2*inset),rect.Height/2));
     Look.Text(g,"点击查看",label,muted,new Rectangle(inset,rect.Height/2,Math.Max(1,quotaWidth-2*inset),rect.Height/2));return;
    }
    var windows=BarWindows(q,c,a);
    int resetWidth=QuotaReset.Enabled(c)?windows.Select(w=>TextRenderer.MeasureText("· "+QuotaReset.Text(w,fresh,c,J.Now),label,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+2).DefaultIfEmpty(0).Max():0;
    bool single=c.SingleLine||rect.Height<35;
    if(single&&windows.Count>0){
     int count=windows.Count;
     for(int i=0;i<count;i++){
      int left=i*quotaWidth/count,right=(i+1)*quotaWidth/count;
      var area=new Rectangle(left,0,right-left,rect.Height);
      var window=windows[i];
      string caption=window.Id=="5h"?"5h":window.Id=="week"?"周":c.ShowUsed?"用":"余";
      DrawStripRow(g,area,window,caption,fresh,c,dark,bg,muted,fg,label,number,inset,resetWidth);
     }
    }else if(windows.Count>1){
     for(int i=0;i<2;i++){
      int top=i*rect.Height/2,bottom=(i+1)*rect.Height/2;
      var area=new Rectangle(0,top,quotaWidth,bottom-top);
      var window=windows[i];string caption=(window.Id=="5h"?"5h":"周")+(c.ShowUsed?"用":"余");
      DrawStripRow(g,area,window,caption,fresh,c,dark,bg,muted,Status(fresh&&window.Valid?window.Remaining:null,dark),label,number,inset,resetWidth);
     }
    }else{
     var window=windows.FirstOrDefault();
     var area=new Rectangle(0,0,quotaWidth,rect.Height);
     QuotaColors.PaintProgress(g,area,window,fresh,c,bg,dark);
     Color filled=QuotaColors.ProgressSurface(bg,window,fresh,c,dark);
     Color labelColor=QuotaColors.HasBackground(c)?QuotaColors.Readable(muted,bg,filled,dark):muted;
     Color normal=window==null?muted:Status(fresh&&window.Valid?window.Remaining:null,dark);
     Color numberColor=QuotaColors.NumberColor(window,fresh,c,dark,bg,normal);
     var top=new Rectangle(0,0,quotaWidth,rect.Height/2);
     DrawStripText(g,top,window,c.ShowUsed?"已用":"余量",fresh,c,labelColor,numberColor,label,number,inset,resetWidth);
     Look.Text(g,window==null?"请选择模型":window.Label,label,labelColor,new Rectangle(inset,rect.Height/2,Math.Max(1,quotaWidth-2*inset),rect.Height/2));
    }
    if(historyWidth>0){
     using(var pen=new Pen(dark?Color.FromArgb(64,76,92):Line))g.DrawLine(pen,quotaWidth,8,quotaWidth,rect.Height-8);
     var h=History(s,a);bool ok=h!=null&&h.Error==""&&h.TodayStart==QuotaParser.DayStart(J.Now,false)&&J.Now-h.UpdatedAt<c.HistorySeconds*2500;
     Look.Text(g,"今日Token",label,muted,new Rectangle(quotaWidth+inset,0,Math.Max(1,historyWidth-inset*2),rect.Height/2));
     Look.Text(g,ok?J.Compact(h.TodayTokens):"—",number,fg,new Rectangle(quotaWidth+inset,rect.Height/2,Math.Max(1,historyWidth-inset*2),rect.Height/2));
    }
   }
  }
  static void DrawStripRow(Graphics g,Rectangle area,QuotaWindow window,string caption,bool fresh,AppConfig c,bool dark,Color surface,Color muted,Color defaultNumber,Font label,Font number,int inset,int resetWidth){
   QuotaColors.PaintProgress(g,area,window,fresh,c,surface,dark);
   Color fill=QuotaColors.ProgressSurface(surface,window,fresh,c,dark);
   Color labelColor=QuotaColors.HasBackground(c)?QuotaColors.Readable(muted,surface,fill,dark):muted;
   Color numberColor=QuotaColors.NumberColor(window,fresh,c,dark,surface,defaultNumber);
   DrawStripText(g,area,window,caption,fresh,c,labelColor,numberColor,label,number,inset,resetWidth);
  }
  static void DrawStripText(Graphics g,Rectangle area,QuotaWindow window,string caption,bool fresh,AppConfig c,Color labelColor,Color numberColor,Font label,Font number,int inset,int resetWidth){
   string value=Value(window,fresh,c);
   int numberWidth=TextRenderer.MeasureText(value,number,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+2;
   int timeWidth=0,gap=0;
   if(QuotaReset.Enabled(c)){
    int content=Math.Max(0,area.Width-2*inset);
    int captionWidth=TextRenderer.MeasureText(caption,label,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width;
    timeWidth=Math.Min(resetWidth,Math.Max(0,content-captionWidth-numberWidth-8));
    if(timeWidth>0)gap=4;
   }
   int timeX=area.Right-inset-timeWidth;
   int numberX=Math.Max(area.Left+inset,timeX-gap-numberWidth);
   int labelWidth=Math.Max(1,numberX-area.Left-inset-3);
   Text(g,caption,label,labelColor,new Rectangle(area.Left+inset,area.Top,labelWidth,area.Height));
   Text(g,value,number,numberColor,new Rectangle(numberX,area.Top,Math.Max(1,timeX-gap-numberX),area.Height),TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
   if(timeWidth>0)Text(g,"· "+QuotaReset.Text(window,fresh,c,J.Now),label,labelColor,new Rectangle(timeX,area.Top,timeWidth,area.Height),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);
  }


 }
 public sealed class QuotaStrip:Form {
  public Snapshot State=new Snapshot();
  public AppConfig Config;
  public TaskbarDock Docking;
  public event Action Clicked;
  bool hover;string clockKey="",tooltipText="";
  readonly ToolTip tip=new ToolTip{InitialDelay=350,ReshowDelay=150,AutoPopDelay=10000};
  public QuotaStrip(AppConfig c){
   Config=c;Text="CPA 额度状态栏";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
   AutoScaleMode=AutoScaleMode.None;DoubleBuffered=true;Cursor=Cursors.Hand;AccessibleName="CPA额度状态栏";SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint,true);
   Size=new Size(c.Width,48);Docking=new TaskbarDock(this);
   MouseEnter+=(s,e)=>{hover=true;Invalidate();};MouseLeave+=(s,e)=>{hover=false;Invalidate();};
   MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left&&Clicked!=null)Clicked();};
  }
  protected override bool ShowWithoutActivation{get{return true;}}
  protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=0x08000000|0x80;return cp;}}
  protected override void WndProc(ref Message m){if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);}
  protected override void OnPaint(PaintEventArgs e){Look.Strip(e.Graphics,ClientRectangle,State,Config,hover);}
  string ClockKey(){
   var a=Look.Focus(State,Config);var q=Look.Quota(State,a);bool fresh=Look.Fresh(q,Config);
   return fresh.ToString()+"|"+(QuotaReset.Enabled(Config)?String.Join("|",Look.BarWindows(q,Config,a).Select(w=>QuotaReset.Text(w,fresh,Config,J.Now))):"");
  }
  public void RefreshClock(){string next=ClockKey();if(next!=clockKey)RefreshState(State);}
  public void RefreshState(Snapshot s){
   State=s;var a=Look.Focus(s,Config);var q=Look.Quota(s,a);string info=a==null?"点击打开设置，连接CPA。":J.Provider(a.Provider)+"\n"+a.Label+"\n"+String.Join("\n",Look.BarWindows(q,Config,a).Select(w=>w.Label+"剩余 "+(Look.Fresh(q,Config)&&w.Valid?Math.Round(w.Remaining.Value)+"%":"—")+" · 重置："+QuotaReset.Description(w,Look.Fresh(q,Config),J.Now)));
   if(s.Error!="")info=s.Error;if(q!=null&&q.Error!="")info+="\n"+q.Error;if(info!=tooltipText){tooltipText=info;tip.SetToolTip(this,info);AccessibleName=info.Replace("\n","，");}clockKey=ClockKey();Invalidate();
  }
  protected override void Dispose(bool disposing){if(disposing){Docking.Dispose();tip.Dispose();}base.Dispose(disposing);}
 }
 public sealed class StripPreview:Control {
  public Snapshot State;
  public AppConfig Config;
  public StripPreview(){DoubleBuffered=true;Height=56;AccessibleName="任务栏显示预览";}
  protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Color.White);if(Config!=null&&State!=null){int width=Math.Min(Width,TaskbarMetrics.Width(TaskbarMetrics.TotalWidth(Config),TaskbarMetrics.Dpi(),Config.PhysicalPixels));int height=Math.Max(24,Math.Min(Height-8,Native.Bounds(Native.FindWindow("Shell_TrayWnd",null)).Height));using(var b=new Bitmap(Math.Max(1,width),height)){using(var g=Graphics.FromImage(b))Look.Strip(g,new Rectangle(0,0,width,height),State,Config);e.Graphics.DrawImageUnscaled(b,0,4);}}}
 }
 public sealed class DetailsCanvas:Control {
  public Snapshot State;public AppConfig Config;
  public DetailsCanvas(){DoubleBuffered=true;BackColor=Color.White;AccessibleName="当前账号额度和历史用量";}
  public void UpdateData(Snapshot s,AppConfig c){
   State=s;Config=c;var a=Look.Focus(s,c);var q=Look.Quota(s,a);Height=100+(q==null?0:Math.Min(q.Windows.Count,20)*85)+(c.CpampEnabled?180:0);Invalidate();
  }
  protected override void OnPaint(PaintEventArgs e){
   base.OnPaint(e);if(State==null||Config==null)return;
   var g=e.Graphics;var a=Look.Focus(State,Config);var q=Look.Quota(State,a);bool fresh=Look.Fresh(q,Config);int y=12,w=Width-36;
   using(var title=Look.Font(12,FontStyle.Bold))using(var small=Look.Font(8.5f))using(var body=Look.Font(10))using(var bold=Look.Font(12,FontStyle.Bold)){
    Look.Text(g,a==null?"连接CPA后查看额度":J.Provider(a.Provider)+(q!=null&&q.Plan!=""?"  "+q.Plan:""),title,Look.Ink,new Rectangle(18,y,w,28));y+=32;
    string status=State.Error!=""?State.Error:q!=null&&q.Error!=""?q.Error:q==null?"正在读取额度…":fresh?"实时额度 · 更新于 "+DateTimeOffset.FromUnixTimeMilliseconds(q.UpdatedAt).LocalDateTime.ToString("HH:mm:ss"):"数据已过期，等待刷新";
    Look.Text(g,status,small,State.Error!=""||q!=null&&q.Error!=""?Color.Firebrick:Look.Muted,new Rectangle(18,y,w,38),TextFormatFlags.Left|TextFormatFlags.WordBreak|TextFormatFlags.NoPadding);y+=43;
    if(q!=null)foreach(var window in q.Windows.Take(20)){
     Look.Text(g,(window.Scope!=""?window.Scope+" / ":"")+window.Label,body,Look.Ink,new Rectangle(18,y,w-65,23));
     Look.Text(g,Look.Value(window,fresh,Config),bold,Look.Status(fresh&&window.Valid?window.Remaining:null,false),new Rectangle(Width-90,y,70,23),TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
     using(var track=new SolidBrush(Color.FromArgb(234,239,246)))g.FillRectangle(track,18,y+30,w,5);
     if(fresh&&window.Valid)using(var brush=new SolidBrush(Look.Status(window.Remaining,false)))g.FillRectangle(brush,18,y+30,(float)(w*window.Remaining.Value/100),5);
     string reset=window.ResetAt.HasValue?J.Countdown(window.ResetAt)+"后重置 · "+DateTimeOffset.FromUnixTimeMilliseconds(window.ResetAt.Value).LocalDateTime.ToString("MM-dd HH:mm"):"未提供重置时间";
     Look.Text(g,reset,small,Look.Muted,new Rectangle(18,y+42,w,22));y+=85;
    }
    if(Config.CpampEnabled){
     using(var pen=new Pen(Look.Line))g.DrawLine(pen,18,y,Width-18,y);y+=16;
     Look.Text(g,"历史用量",title,Look.Ink,new Rectangle(18,y,w,26));y+=32;
     var h=Look.History(State,a);bool ok=h!=null&&h.Error==""&&h.UpdatedAt>0&&h.TodayStart==QuotaParser.DayStart(J.Now,false)&&J.Now-h.UpdatedAt<Config.HistorySeconds*2500;
     string[] labels={"今日Token","今日请求","本月Token","本月请求"};
     double?[] values={h==null?null:h.TodayTokens,h==null?null:h.TodayCalls,h==null?null:h.MonthTokens,h==null?null:h.MonthCalls};
     for(int i=0;i<4;i++){int x=18+(i%2)*(w/2);int top=y+(i/2)*48;Look.Text(g,labels[i],small,Look.Muted,new Rectangle(x,top,w/2,18));Look.Text(g,ok?J.Compact(values[i]):"—",bold,Look.Ink,new Rectangle(x,top+17,w/2,26));}
     y+=102;Look.Text(g,h!=null&&h.Error!=""?h.Error:"CPAMP · 仅当前账号 · 按北京时间统计",small,h!=null&&h.Error!=""?Color.Firebrick:Look.Muted,new Rectangle(18,y,w,35),TextFormatFlags.Left|TextFormatFlags.WordBreak|TextFormatFlags.NoPadding);
    }
   }
  }
 }
 public sealed class DetailsForm:Form {
  public readonly ComboBox Accounts=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,Height=32};
  readonly Panel scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.White};
  public readonly DetailsCanvas Canvas=new DetailsCanvas();
  public event Action SettingsRequested,RefreshRequested;
  public DetailsForm(){
   Text="CPA 额度详情";Font=Look.Font();FormBorderStyle=FormBorderStyle.FixedSingle;ShowInTaskbar=false;MinimizeBox=false;MaximizeBox=false;TopMost=true;StartPosition=FormStartPosition.Manual;
   ClientSize=new Size(392,480);BackColor=Color.White;
   var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,Padding=new Padding(12,8,10,8),FlowDirection=FlowDirection.RightToLeft,BackColor=Color.FromArgb(247,249,252)};
   var settings=new Button{Text="设置",Width=70,Height=28};var refresh=new Button{Text="刷新额度",Width=88,Height=28};
   footer.Controls.Add(settings);footer.Controls.Add(refresh);settings.Click+=(s,e)=>{Hide();if(SettingsRequested!=null)SettingsRequested();};refresh.Click+=(s,e)=>{if(RefreshRequested!=null)RefreshRequested();};
   var header=new Panel{Dock=DockStyle.Top,Height=48,Padding=new Padding(12,10,12,10)};header.Controls.Add(Accounts);
   scroll.Controls.Add(Canvas);Controls.Add(scroll);Controls.Add(footer);Controls.Add(header);
   scroll.Resize+=(s,e)=>Canvas.Width=Math.Max(100,scroll.ClientSize.Width-SystemInformation.VerticalScrollBarWidth);
   Deactivate+=(s,e)=>BeginInvoke((Action)(()=>{if(!Accounts.DroppedDown)Hide();}));
   KeyPreview=true;KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape)Hide();};
  }
  public void UpdateData(Snapshot s,AppConfig c){
   string current=(Accounts.SelectedItem as Account)==null?"":((Account)Accounts.SelectedItem).Id;
   var filtered=s.Accounts.Where(a=>a.Supported&&(c.SelectedIds.Count==0||c.SelectedIds.Contains(a.Id))).ToList();
   if(Accounts.Items.Count!=filtered.Count||!Accounts.Items.Cast<Account>().Select(a=>a.Id).SequenceEqual(filtered.Select(a=>a.Id))){
    Accounts.Items.Clear();Accounts.Items.AddRange(filtered.Cast<object>().ToArray());
   }
   string focus=c.FocusId!=""?c.FocusId:current;var desired=Accounts.Items.Cast<Account>().FirstOrDefault(a=>a.Id==focus)??Accounts.Items.Cast<Account>().FirstOrDefault();
   if(desired!=null&&Accounts.SelectedItem!=desired)Accounts.SelectedItem=desired;
   Canvas.Width=Math.Max(100,scroll.ClientSize.Width-SystemInformation.VerticalScrollBarWidth);Canvas.UpdateData(s,c);int desiredHeight=Math.Min(620,Canvas.Height+98);if(ClientSize.Height!=desiredHeight)ClientSize=new Size(ClientSize.Width,desiredHeight);
  }
  public void ShowNear(Rectangle anchor){
   var work=Screen.FromRectangle(anchor).WorkingArea;
   int x=Math.Max(work.Left,Math.Min(anchor.Right-Width,work.Right-Width));int y=anchor.Top-Height-8;
   if(y<work.Top)y=Math.Min(work.Bottom-Height,anchor.Bottom+8);
   Location=new Point(x,Math.Max(work.Top,y));Show();Native.SetForegroundWindow(Handle);Activate();
  }
 }
}
