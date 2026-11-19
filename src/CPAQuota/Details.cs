using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CpaTaskbar {
 sealed class QuotaProgress:Control {
  readonly double percent;readonly Color color;
  public QuotaProgress(double value,Color fill){percent=value;color=fill;DoubleBuffered=true;BackColor=Color.FromArgb(234,239,246);AccessibleRole=AccessibleRole.ProgressBar;}
  protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(BackColor);using(var brush=new SolidBrush(color))e.Graphics.FillRectangle(brush,0,0,(float)(ClientSize.Width*Math.Max(0,Math.Min(100,percent))/100),ClientSize.Height);}
 }
 public class DetailsLayout:TableLayoutPanel {
  bool fitting;
  protected override void OnLayout(LayoutEventArgs e){
   base.OnLayout(e);if(fitting)return;fitting=true;
   try{
    for(int pass=0;pass<3;pass++){
     bool changed=false;var columns=GetColumnWidths();
     foreach(Control child in Controls){
      if(!(child is Label))continue;int column=GetColumn(child);if(column<0||column>=columns.Length||column<ColumnStyles.Count&&ColumnStyles[column].SizeType==SizeType.AutoSize)continue;
      var max=new Size(Math.Max(1,columns[column]-child.Margin.Horizontal),0);if(child.MaximumSize!=max){child.MaximumSize=max;changed=true;}
     }
     if(!changed)break;base.OnLayout(e);
    }
   }finally{fitting=false;}
  }
  public static int Dpi(Control control){
   try{if(control.IsHandleCreated){int dpi=(int)Native.GetDpiForWindow(control.Handle);if(dpi>0)return dpi;}using(var graphics=control.CreateGraphics())return (int)Math.Round(graphics.DpiY);}catch{return 96;}
  }
 }
 public sealed class DetailsCanvas:DetailsLayout {
  public Snapshot State;public AppConfig Config;
  readonly Font heading=Look.Font(10,FontStyle.Bold),small=Look.Font(8.5f),body=Look.Font(9);
  readonly List<Tuple<Label,QuotaWindow,QuotaState,string>> resets=new List<Tuple<Label,QuotaWindow,QuotaState,string>>();
  string summaryKey="";
  int layoutDpi=96;
  public DetailsCanvas(){
   DoubleBuffered=true;BackColor=Color.White;AccessibleName="当前账号额度和历史用量";
   AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;Dock=DockStyle.Top;ColumnCount=1;
   ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  }
  int Pixels(int value){return TaskbarMetrics.Scale(value,layoutDpi);}
  Label TextLabel(string name,string text,Font font,Color color){return new Label{Name=name,Text=text,Font=font,ForeColor=color,AutoSize=true,Dock=DockStyle.Top,Margin=new Padding(0,0,0,Pixels(4)),UseMnemonic=false};}
  void AddRow(Control control){int row=RowCount++;RowStyles.Add(new RowStyle(SizeType.AutoSize));control.Anchor=AnchorStyles.Left|AnchorStyles.Right;Controls.Add(control,0,row);}
  TableLayoutPanel Table(int columns){
   var table=new DetailsLayout{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=columns,Dock=DockStyle.Top,Margin=Padding.Empty};
   for(int i=0;i<columns;i++)table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/columns));return table;
  }
  public void UpdateData(Snapshot state,AppConfig config){
   State=state;Config=config;layoutDpi=DetailsLayout.Dpi(this);SuspendLayout();
   try{
    var old=Controls.Cast<Control>().ToArray();Controls.Clear();foreach(var control in old)control.Dispose();
    RowStyles.Clear();RowCount=0;resets.Clear();Padding=new Padding(Pixels(12),Pixels(6),Pixels(12),Pixels(2));
    summaryKey="";if(QuotaAggregation.Enabled(config))AddSummary(QuotaAggregation.Build(state,config));
    var account=Look.Focus(state,config);var quota=Look.Quota(state,account);bool fresh=Look.Fresh(quota,config);
    AddRow(TextLabel("detailsPlan",account==null?"连接CPA后查看额度":J.Provider(account.Provider)+(quota!=null&&quota.Plan!=""?"  "+quota.Plan:""),heading,Look.Ink));
    string status=state.Error!=""?state.Error:quota!=null&&quota.Error!=""?quota.Error:quota==null?"正在读取额度…":fresh?"实时额度 · 更新于 "+DateTimeOffset.FromUnixTimeMilliseconds(quota.UpdatedAt).LocalDateTime.ToString("HH:mm:ss"):"数据已过期，等待刷新";
    AddRow(TextLabel("detailsStatus",status,small,state.Error!=""||quota!=null&&quota.Error!=""?Color.Firebrick:Look.Muted));
    if(quota!=null)foreach(var window in quota.Windows.Take(20)){
     var row=Table(2);row.ColumnStyles.Clear();row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
     var name=TextLabel("quotaLabel",(window.Scope!=""?window.Scope+" / ":"")+window.Label,body,Look.Ink);
     var value=TextLabel("quotaValue",Look.Value(window,fresh,config),heading,Look.Status(fresh&&window.Valid?window.Remaining:null,false));
     value.Dock=DockStyle.None;value.Anchor=AnchorStyles.Top|AnchorStyles.Right;row.Controls.Add(name,0,0);row.Controls.Add(value,1,0);AddRow(row);
     AddRow(new QuotaProgress(fresh&&window.Valid?Look.DisplayPercent(window,config):0,Look.Status(fresh&&window.Valid?window.Remaining:null,false)){Height=Pixels(5),Margin=new Padding(0,0,0,Pixels(4)),AccessibleName=window.Label+(config.ShowUsed?"已用额度 ":"剩余额度 ")+Look.Value(window,fresh,config)});
     var reset=TextLabel("quotaReset","",small,Look.Muted);reset.Margin=new Padding(0,0,0,Pixels(10));resets.Add(Tuple.Create(reset,window,quota,""));AddRow(reset);
    }
    if(config.CpampEnabled){
     AddRow(new Panel{Height=Pixels(1),BackColor=Look.Line,Margin=new Padding(0,Pixels(4),0,Pixels(14))});
     AddRow(TextLabel("historyHeading","历史用量",heading,Look.Ink));
     var history=Look.History(state,account);bool valid=history!=null&&history.Error==""&&history.UpdatedAt>0&&history.TodayStart==QuotaParser.DayStart(J.Now,false)&&J.Now-history.UpdatedAt<config.HistorySeconds*2500;
     string[] names={"今日Token","今日请求","本月Token","本月请求"};
     double?[] values={history==null?null:history.TodayTokens,history==null?null:history.TodayCalls,history==null?null:history.MonthTokens,history==null?null:history.MonthCalls};
     var metrics=Table(2);
     for(int i=0;i<4;i++){
      var card=Table(1);card.Margin=new Padding(0,0,Pixels(8),Pixels(10));
      card.Controls.Add(TextLabel("historyMetric",names[i],small,Look.Muted),0,0);card.Controls.Add(TextLabel("historyValue",valid?J.Compact(values[i]):"—",heading,Look.Ink),0,1);metrics.Controls.Add(card,i%2,i/2);
     }
     AddRow(metrics);
     AddRow(TextLabel("historyStatus",history!=null&&history.Error!=""?history.Error:"CPAMP · 仅当前账号 · 按北京时间统计",small,history!=null&&history.Error!=""?Color.Firebrick:Look.Muted));
    }
    RefreshClock();
   }finally{ResumeLayout(true);}
  }
  void AddSummary(QuotaSummary summary){
   summaryKey=summary.Key;
   AddRow(TextLabel("summaryHeading",summary.Title(Config),heading,Look.Ink));
   AddRow(TextLabel("summaryRule",(Config.QuotaDisplayMode=="sum"?"百分比为有效账号的累加值，可超过100%；进度条与显示比例一致，超过100%时封顶。":"百分比为有效账号的平均值。")+"不同套餐的百分比不等同于实际请求总量；仅汇总当前渠道的已选账号。",small,Look.Muted));
   foreach(var window in summary.Quota.Windows.Cast<SummaryWindow>()){
    AddRow(TextLabel("summaryValue",(window.Scope!=""?window.Scope+" / ":"")+window.Label+"："+Look.Value(window,true,Config)+"（有效 "+window.Count+"/"+window.Total+" 个账号）",body,Look.Ink));
    var reset=TextLabel("summaryReset","",small,Look.Muted);resets.Add(Tuple.Create(reset,(QuotaWindow)window,summary.Quota,"最近已知重置（时间已知 "+window.KnownResets+"/"+window.Count+"）："));AddRow(reset);
   }
   AddRow(TextLabel("summaryMembers","各账号额度与重置时间",body,Look.Ink));
   foreach(var account in summary.Accounts){
    var quota=Look.Quota(State,account);AddRow(TextLabel("summaryMember",account.Label,body,Look.Ink));
    foreach(var window in Look.BarWindows(quota,Config,account)){
     var label=TextLabel("summaryMemberReset","",small,Look.Muted);resets.Add(Tuple.Create(label,window,quota,window.Label+" "+Look.Value(window,Look.Fresh(quota,Config),Config)+" · "));AddRow(label);
    }
   }
   AddRow(new Panel{Height=Pixels(1),BackColor=Look.Line,Margin=new Padding(0,Pixels(8),0,Pixels(14))});
   AddRow(TextLabel("currentAccountHeading","当前单个账号详情",heading,Look.Ink));
  }
  public void RefreshClock(){
   if(State==null||Config==null)return;
   if(QuotaAggregation.Enabled(Config)&&summaryKey!=QuotaAggregation.Build(State,Config).Key){UpdateData(State,Config);return;}
   foreach(var entry in resets){
    string text=entry.Item4+QuotaReset.Description(entry.Item2,Look.Fresh(entry.Item3,Config),J.Now);
    if(entry.Item1.Text!=text)entry.Item1.Text=text;
   }
  }
  protected override void Dispose(bool disposing){if(disposing){heading.Dispose();small.Dispose();body.Dispose();}base.Dispose(disposing);}
 }
 public sealed class DetailsForm:Form {
  public readonly ComboBox Accounts=new ComboBox{Name="detailsAccounts",DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top};
  readonly Panel scroll=new Panel{Name="detailsScroll",Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.White};
  public readonly DetailsCanvas Canvas=new DetailsCanvas();
  public event Action SettingsRequested,RefreshRequested;
  Rectangle popupAnchor;
  public DetailsForm(){
   SuspendLayout();AutoScaleMode=AutoScaleMode.None;
   Text="CPA 额度详情";Font=Look.Font();FormBorderStyle=FormBorderStyle.None;ControlBox=false;ShowInTaskbar=false;MinimizeBox=false;MaximizeBox=false;TopMost=true;StartPosition=FormStartPosition.Manual;
   ClientSize=new Size(320,240);Padding=new Padding(1);BackColor=Look.Line;
   var footer=new FlowLayoutPanel{Name="detailsFooter",Dock=DockStyle.Bottom,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(8,3,8,3),FlowDirection=FlowDirection.RightToLeft,BackColor=Color.FromArgb(247,249,252)};
   var settings=new Button{Name="detailsSettings",Text="设置",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(6,2,6,2),FlatStyle=FlatStyle.Flat,ForeColor=Look.Muted,Cursor=Cursors.Hand};
   var refresh=new Button{Name="detailsRefresh",Text="刷新额度",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(6,2,6,2),FlatStyle=FlatStyle.Flat,ForeColor=Look.Blue,Cursor=Cursors.Hand};
   settings.FlatAppearance.BorderSize=0;refresh.FlatAppearance.BorderSize=0;
   footer.Controls.Add(settings);footer.Controls.Add(refresh);settings.Click+=(s,e)=>{Hide();if(SettingsRequested!=null)SettingsRequested();};refresh.Click+=(s,e)=>{if(RefreshRequested!=null)RefreshRequested();};
   var header=new TableLayoutPanel{Name="detailsHeader",Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,Padding=new Padding(10,8,10,4),BackColor=Color.White};header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.Controls.Add(Accounts,0,0);
   scroll.Controls.Add(Canvas);Controls.Add(scroll);Controls.Add(footer);Controls.Add(header);
   scroll.Layout+=(s,e)=>{var maximum=new Size(Math.Max(1,scroll.ClientSize.Width),0);if(Canvas.MaximumSize!=maximum)Canvas.MaximumSize=maximum;};
   Accounts.DropDown+=(s,e)=>{Accounts.DropDownWidth=Math.Min(Screen.FromHandle(Handle).WorkingArea.Width,Math.Max(Accounts.Width,Accounts.Items.Cast<object>().Select(a=>TextRenderer.MeasureText(a.ToString(),Accounts.Font).Width+SystemInformation.VerticalScrollBarWidth).DefaultIfEmpty(0).Max()));};
   KeyPreview=true;KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){Hide();e.Handled=true;}};
   AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);ResumeLayout(true);
  }
  protected override void OnDeactivate(EventArgs e){base.OnDeactivate(e);Hide();}
  protected override void OnFormClosing(FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}base.OnFormClosing(e);}
  protected override void OnDpiChanged(DpiChangedEventArgs e){base.OnDpiChanged(e);BeginInvoke((Action)(()=>{if(!IsDisposed&&Canvas.State!=null){Canvas.UpdateData(Canvas.State,Canvas.Config);if(Visible)FitContent();}}));}
  public void UpdateData(Snapshot state,AppConfig config){
   string current=(Accounts.SelectedItem as Account)==null?"":((Account)Accounts.SelectedItem).Id;
   var filtered=state.Accounts.Where(a=>a.Supported&&(config.SelectedIds.Count==0||config.SelectedIds.Contains(a.Id))).ToList();
   if(!Accounts.Items.Cast<Account>().Select(a=>a.Id).SequenceEqual(filtered.Select(a=>a.Id))){Accounts.Items.Clear();Accounts.Items.AddRange(filtered.Cast<object>().ToArray());}
   string focus=config.FocusId!=""?config.FocusId:current;var desired=Accounts.Items.Cast<Account>().FirstOrDefault(a=>a.Id==focus)??Accounts.Items.Cast<Account>().FirstOrDefault();
   if(desired!=null&&Accounts.SelectedItem!=desired)Accounts.SelectedItem=desired;
   Canvas.UpdateData(state,config);
   if(Visible)FitContent();
  }
  void FitToWorkingArea(){
   var work=Screen.FromHandle(Handle).WorkingArea;
   MinimumSize=new Size(Math.Min(MinimumSize.Width,work.Width),Math.Min(MinimumSize.Height,work.Height));
   Size=new Size(Math.Min(Width,work.Width),Math.Min(Height,work.Height));
   Location=new Point(Math.Max(work.Left,Math.Min(Left,work.Right-Width)),Math.Max(work.Top,Math.Min(Top,work.Bottom-Height)));
  }
  void FitContent(){
   var work=Screen.FromRectangle(popupAnchor).WorkingArea;
   int dpi=DetailsLayout.Dpi(this);
   ClientSize=new Size(Math.Min(TaskbarMetrics.Scale(320,dpi),work.Width),ClientSize.Height);PerformLayout();Canvas.PerformLayout();
   var header=Controls.Find("detailsHeader",true).Single();var footer=Controls.Find("detailsFooter",true).Single();
   ClientSize=new Size(ClientSize.Width,Math.Min(Math.Min(TaskbarMetrics.Scale(480,dpi),work.Height),Canvas.Height+header.Height+footer.Height+Padding.Vertical));
   int x=Math.Max(work.Left,Math.Min(popupAnchor.Right-Width,work.Right-Width)),y=popupAnchor.Top-Height-TaskbarMetrics.Scale(8,dpi);
   if(y<work.Top)y=Math.Min(work.Bottom-Height,popupAnchor.Bottom+TaskbarMetrics.Scale(8,dpi));
   Location=new Point(x,Math.Max(work.Top,y));FitToWorkingArea();
  }
  public void ShowNear(Rectangle anchor){
   popupAnchor=anchor;Location=Screen.FromRectangle(anchor).WorkingArea.Location;
   FitContent();Show();FitContent();Native.SetForegroundWindow(Handle);Activate();
  }
 }
}
