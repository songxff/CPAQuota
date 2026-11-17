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
  public static double DisplayPercent(QuotaWindow w,AppConfig c){var summary=w as SummaryWindow;return Math.Round((c.ShowUsed?w.Used.Value:w.Remaining.Value)*(summary!=null&&c.QuotaDisplayMode=="sum"?summary.Count:1));}
  public static string Value(QuotaWindow w,bool fresh,AppConfig c){return !fresh||w==null||!w.Valid?"—":DisplayPercent(w,c).ToString()+"%";}
  static Font stripLabel,stripNumber,stripTime;static float stripScale;
  public static void ReleaseStripFonts(){if(stripLabel!=null)stripLabel.Dispose();if(stripNumber!=null)stripNumber.Dispose();if(stripTime!=null)stripTime.Dispose();stripLabel=stripNumber=stripTime=null;}
  static void StripFonts(float scale,out Font label,out Font number,out Font time){
   if(stripLabel==null||Math.Abs(stripScale-scale)>.001f){
    ReleaseStripFonts();stripScale=scale;
    stripLabel=new Font("Microsoft YaHei UI",Math.Max(10,10.5f*scale),FontStyle.Bold,GraphicsUnit.Pixel);
    stripNumber=new Font("Segoe UI",Math.Max(11,13*scale),FontStyle.Bold,GraphicsUnit.Pixel);
    stripTime=new Font("Microsoft YaHei UI",Math.Max(9,9.5f*scale),FontStyle.Bold,GraphicsUnit.Pixel);
   }
   label=stripLabel;number=stripNumber;time=stripTime;
  }
  public static Color StripSurface(bool dark,bool hover){return hover?(dark?Color.FromArgb(45,47,51):Color.FromArgb(244,244,241)):(dark?Color.FromArgb(34,36,40):Color.FromArgb(249,249,246));}
  static void StripText(Graphics g,AppConfig c,string text,Font font,Color color,Rectangle area,TextFormatFlags flags=TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding){
   if(!c.TransparentBackground){Text(g,text,font,color,area,flags);return;}
   if(area.Width<=0||area.Height<=0)return;
   var hint=g.TextRenderingHint;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
   using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())using(var brush=new SolidBrush(color)){
    format.FormatFlags|=StringFormatFlags.NoWrap;format.Alignment=(flags&TextFormatFlags.Right)!=0?StringAlignment.Far:StringAlignment.Near;
    format.LineAlignment=StringAlignment.Center;format.Trimming=StringTrimming.EllipsisCharacter;
    g.DrawString(text??"",font,brush,area,format);
   }
   g.TextRenderingHint=hint;
  }
  public static void Strip(Graphics g,Rectangle rect,Snapshot s,AppConfig c,bool hover=false){
   bool dark=Dark(c);Color bg=StripSurface(dark,hover),fg=dark?Color.FromArgb(244,245,247):Color.FromArgb(20,23,27),muted=dark?Color.FromArgb(211,215,221):Color.FromArgb(52,55,60);
   // Fully transparent pixels pass mouse input through layered windows. A 1/255
   // alpha surface stays visually transparent while keeping the entire panel clickable.
   g.Clear(c.TransparentBackground?Color.FromArgb(1,0,0,0):bg);
   int lift=Math.Max(2,Math.Min(6,rect.Height/24));
   Account a=Focus(s,c);QuotaState q=QuotaAggregation.BarQuota(s,c);bool fresh=Fresh(q,c);
   if(QuotaAggregation.Enabled(c)){var summary=QuotaAggregation.Build(s,c);a=summary.Accounts.FirstOrDefault()??a;}
   float scale=Math.Max(.8f,rect.Height/48f)*c.FontSize/9f;
   Font label,number,time;StripFonts(scale,out label,out number,out time);
   {
    int inset=Math.Max(7,(int)(9*scale));
    int historyWidth=c.HistoryInBar&&c.CpampEnabled?(int)(74*scale):0;
    int quotaWidth=Math.Max(1,rect.Width-historyWidth);
    if(a==null||s.Error!=""){
     string first=s.Error!=""?"连接异常":String.IsNullOrEmpty(c.CpaSecret)?"未配置":"连接中";
     StripText(g,c,first,number,fg,new Rectangle(inset,1-lift,Math.Max(1,quotaWidth-2*inset),rect.Height/2));
     StripText(g,c,"点击查看",label,muted,new Rectangle(inset,rect.Height/2-lift,Math.Max(1,quotaWidth-2*inset),rect.Height/2));return;
    }
    var windows=BarWindows(q,c,a);
    int resetWidth=QuotaReset.Enabled(c)?windows.Select(w=>TextRenderer.MeasureText(QuotaReset.Text(w,fresh,c,J.Now),time,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+2).DefaultIfEmpty(0).Max():0;
    bool single=c.SingleLine||rect.Height<35;
    if(single&&windows.Count>0){
     int count=windows.Count;
     for(int i=0;i<count;i++){
      int left=i*quotaWidth/count,right=(i+1)*quotaWidth/count;
      var area=new Rectangle(left,-lift,right-left,rect.Height);
      var window=windows[i];
      string caption=window.Id=="5h"?"5h":window.Id=="week"?"周":c.ShowUsed?"用":"余";
      caption=QuotaAggregation.Caption(window,caption,c);
      DrawStripRow(g,area,window,caption,fresh,c,dark,bg,muted,fg,label,number,time,inset,resetWidth);
     }
    }else if(windows.Count>1){
     for(int i=0;i<2;i++){
      int top=i*rect.Height/2-lift,bottom=(i+1)*rect.Height/2-lift;
      var area=new Rectangle(0,top,quotaWidth,bottom-top);
      var window=windows[i];string caption=(window.Id=="5h"?"5h":"周")+(c.ShowUsed?"用":"余");
      caption=QuotaAggregation.Caption(window,caption,c);
      DrawStripRow(g,area,window,caption,fresh,c,dark,bg,muted,fg,label,number,time,inset,resetWidth);
     }
    }else{
     var window=windows.FirstOrDefault();
     var area=new Rectangle(0,-lift,quotaWidth,rect.Height);
     QuotaColors.PaintProgress(g,area,window,fresh,c,bg,dark);
     Color labelColor=muted;
     Color normal=window==null?muted:fg;
     Color numberColor=QuotaColors.NumberColor(window,fresh,c,dark,bg,normal);
     var top=new Rectangle(0,-lift,quotaWidth,rect.Height/2);
     DrawStripText(g,top,window,QuotaAggregation.Caption(window,c.ShowUsed?"已用":"余量",c),fresh,c,labelColor,numberColor,label,number,time,inset,resetWidth);
     StripText(g,c,window==null?"请选择模型":window.Label,label,labelColor,new Rectangle(inset,rect.Height/2-lift,Math.Max(1,quotaWidth-2*inset),rect.Height/2));
    }
    if(historyWidth>0){
     using(var pen=new Pen(dark?Color.FromArgb(64,76,92):Line))g.DrawLine(pen,quotaWidth,8-lift,quotaWidth,rect.Height-8-lift);
     var h=History(s,Focus(s,c));bool ok=h!=null&&h.Error==""&&h.TodayStart==QuotaParser.DayStart(J.Now,false)&&J.Now-h.UpdatedAt<c.HistorySeconds*2500;
     StripText(g,c,QuotaAggregation.Enabled(c)?"当前Token":"今日Token",label,muted,new Rectangle(quotaWidth+inset,-lift,Math.Max(1,historyWidth-inset*2),rect.Height/2));
     StripText(g,c,ok?J.Compact(h.TodayTokens):"—",number,fg,new Rectangle(quotaWidth+inset,rect.Height/2-lift,Math.Max(1,historyWidth-inset*2),rect.Height/2));
    }
   }
  }
  static void DrawStripRow(Graphics g,Rectangle area,QuotaWindow window,string caption,bool fresh,AppConfig c,bool dark,Color surface,Color muted,Color defaultNumber,Font label,Font number,Font time,int inset,int resetWidth){
   QuotaColors.PaintProgress(g,area,window,fresh,c,surface,dark);
   Color labelColor=muted;
   Color numberColor=QuotaColors.NumberColor(window,fresh,c,dark,surface,defaultNumber);
   var textArea=area;if(QuotaColors.HasBackground(c))textArea.Height=Math.Max(1,QuotaColors.ProgressBounds(area).Top-area.Top-2);
   DrawStripText(g,textArea,window,caption,fresh,c,labelColor,numberColor,label,number,time,inset,resetWidth);
  }
  static void DrawStripText(Graphics g,Rectangle area,QuotaWindow window,string caption,bool fresh,AppConfig c,Color labelColor,Color numberColor,Font label,Font number,Font time,int inset,int resetWidth){
   string value=Value(window,fresh,c);
   int numberWidth=TextRenderer.MeasureText(value,number,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+2;
   if(!(window is SummaryWindow)||c.QuotaDisplayMode!="sum")numberWidth=Math.Max(numberWidth,TextRenderer.MeasureText("100%",number,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+2);
   int timeWidth=0,gap=0;
   if(QuotaReset.Enabled(c)){
    int content=Math.Max(0,area.Width-2*inset);
    int captionWidth=TextRenderer.MeasureText(caption,label,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width;
    timeWidth=Math.Min(resetWidth,Math.Max(0,content-captionWidth-numberWidth-12));
    if(timeWidth>0)gap=Math.Max(8,inset);
   }
   int timeX=area.Right-inset-timeWidth;
   int numberX=Math.Max(area.Left+inset,timeX-gap-numberWidth);
   int labelWidth=Math.Max(1,numberX-area.Left-inset-3);
   StripText(g,c,caption,label,labelColor,new Rectangle(area.Left+inset,area.Top,labelWidth,area.Height),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);
   StripText(g,c,value,number,numberColor,new Rectangle(numberX,area.Top,Math.Max(1,timeX-gap-numberX),area.Height),TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
   if(timeWidth>0)StripText(g,c,QuotaReset.Text(window,fresh,c,J.Now),time,labelColor,new Rectangle(timeX,area.Top,timeWidth,area.Height),TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);
  }


 }
 public sealed class QuotaStrip:Form {
  public Snapshot State=new Snapshot();
  public AppConfig Config;
  public TaskbarDock Docking;
  public long LastPresentedAt{get;private set;}
  public int PresentationCount{get;private set;}
  public event Action Clicked;
  bool hover;string clockKey="",tooltipText="";
  readonly ToolTip tip=new ToolTip{InitialDelay=350,ReshowDelay=150,AutoPopDelay=10000};
  public QuotaStrip(AppConfig c){
   Config=c;Text="CPA 额度状态栏";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
   AutoScaleMode=AutoScaleMode.None;DoubleBuffered=true;Cursor=Cursors.Hand;AccessibleName="CPA额度状态栏";SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint,true);
   Size=new Size(c.Width,48);Docking=new TaskbarDock(this);
   MouseEnter+=(s,e)=>{hover=true;RefreshPresentation();};MouseLeave+=(s,e)=>{hover=false;RefreshPresentation();};
   MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left&&Clicked!=null)Clicked();};
  }
  protected override bool ShowWithoutActivation{get{return true;}}
  protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);if(Config!=null&&Config.TransparentBackground)RenderLayered();}
  protected override void OnSizeChanged(EventArgs e){
   base.OnSizeChanged(e);if(Width<1||Height<1)return;
   if(Config!=null){UpdateShape();if(IsHandleCreated&&Config.TransparentBackground)RenderLayered();}
  }
  void UpdateShape(){var old=Region;if(Config.TransparentBackground)Region=null;else using(var shape=QuotaColors.Rounded(ClientRectangle,Math.Max(5,Height/7)))Region=new Region(shape);if(old!=null)old.Dispose();}
  protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=0x08000000|0x80;if(Config!=null&&Config.TransparentBackground)cp.ExStyle|=Native.WS_EX_LAYERED;return cp;}}
  protected override void WndProc(ref Message m){if(m.Msg==0x21){m.Result=new IntPtr(3);return;}if(m.Msg==0x84&&Config!=null&&Config.TransparentBackground){m.Result=new IntPtr(1);return;}base.WndProc(ref m);}
  protected override void OnPaint(PaintEventArgs e){
   if(Config==null)return;
   if(!Config.TransparentBackground){Look.Strip(e.Graphics,ClientRectangle,State,Config,hover);return;}
   RenderLayered();
  }
  void RenderLayered(){
   using(var bitmap=new Bitmap(Math.Max(1,Width),Math.Max(1,Height),System.Drawing.Imaging.PixelFormat.Format32bppPArgb)){
    using(var g=Graphics.FromImage(bitmap))Look.Strip(g,ClientRectangle,State,Config,hover);
    Native.PresentLayered(Handle,bitmap);
    LastPresentedAt=J.Now;PresentationCount++;
   }
  }
  void RefreshPresentation(){
   // UpdateLayeredWindow owns the displayed bitmap; Invalidate alone does not replace it.
   if(IsHandleCreated&&Config.TransparentBackground)RenderLayered();else Invalidate();
  }
  string ClockKey(){
   var a=Look.Focus(State,Config);var q=QuotaAggregation.BarQuota(State,Config);bool fresh=Look.Fresh(q,Config);
   if(QuotaAggregation.Enabled(Config))a=QuotaAggregation.Build(State,Config).Accounts.FirstOrDefault()??a;
   return fresh.ToString()+"|"+(QuotaAggregation.Enabled(Config)?QuotaAggregation.Build(State,Config).Key:"")+"|"+(QuotaReset.Enabled(Config)?String.Join("|",Look.BarWindows(q,Config,a).Select(w=>QuotaReset.Text(w,fresh,Config,J.Now))):"");
  }
  public void RefreshClock(){string next=ClockKey();if(next!=clockKey)RefreshState(State);}
  public void RefreshState(Snapshot s){
   if(IsHandleCreated){int style=Native.GetWindowLong(Handle,-20);int next=Config.TransparentBackground?style|Native.WS_EX_LAYERED:style&~Native.WS_EX_LAYERED;if(style!=next)Native.SetWindowLong(Handle,-20,next);UpdateShape();}
   State=s;var a=Look.Focus(s,Config);var q=QuotaAggregation.BarQuota(s,Config);string info=QuotaAggregation.Enabled(Config)?QuotaAggregation.Description(s,Config):a==null?"点击打开设置，连接CPA。":J.Provider(a.Provider)+"\n"+a.Label+"\n"+String.Join("\n",Look.BarWindows(q,Config,a).Select(w=>w.Label+"剩余 "+(Look.Fresh(q,Config)&&w.Valid?Math.Round(w.Remaining.Value)+"%":"—")+" · 重置："+QuotaReset.Description(w,Look.Fresh(q,Config),J.Now)));
   if(s.Error!="")info=s.Error;if(q!=null&&q.Error!="")info+="\n"+q.Error;if(info!=tooltipText){tooltipText=info;tip.SetToolTip(this,info);AccessibleName=info.Replace("\n","，");}clockKey=ClockKey();RefreshPresentation();
  }
  protected override void Dispose(bool disposing){if(disposing){Docking.Dispose();tip.Dispose();}base.Dispose(disposing);}
 }
 public sealed class StripPreview:Control {
  public Snapshot State;
  public AppConfig Config;
  public StripPreview(){DoubleBuffered=true;Height=56;AccessibleName="任务栏显示预览";}
  protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Color.White);if(Config!=null&&State!=null){int width=Math.Min(Width,TaskbarMetrics.Width(TaskbarMetrics.TotalWidth(Config),TaskbarMetrics.Dpi(),Config.PhysicalPixels));int height=Math.Max(24,Math.Min(Height-8,Native.Bounds(Native.FindWindow("Shell_TrayWnd",null)).Height));using(var b=new Bitmap(Math.Max(1,width),height)){using(var g=Graphics.FromImage(b))Look.Strip(g,new Rectangle(0,0,width,height),State,Config);var saved=e.Graphics.Save();using(var shape=QuotaColors.Rounded(new Rectangle(0,4,width,height),Math.Max(5,height/7)))e.Graphics.SetClip(shape,CombineMode.Intersect);e.Graphics.DrawImageUnscaled(b,0,4);e.Graphics.Restore(saved);}}}
 }
}
