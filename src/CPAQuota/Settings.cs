using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CpaTaskbar {
 public sealed class ColorModePicker:TableLayoutPanel {
  readonly RadioButton[] choices=new RadioButton[4];int selected=-1;
  public event EventHandler SelectedIndexChanged;
  public int SelectedIndex{get{return selected;}set{if(value<0||value>=choices.Length)throw new ArgumentOutOfRangeException("value");choices[value].Checked=true;}}
  public ColorModePicker(){
   ColumnCount=2;RowCount=2;AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;Margin=Padding.Empty;
   ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
   RowStyles.Add(new RowStyle(SizeType.AutoSize));RowStyles.Add(new RowStyle(SizeType.AutoSize));
   string[] labels={"默认（简洁数字）","仅进度条","仅百分比数字","进度条 + 百分比数字"};
   for(int i=0;i<choices.Length;i++){
    int index=i;var radio=new RadioButton{Text=labels[i],AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,4,8,4),AccessibleName=labels[i]};
    choices[i]=radio;Controls.Add(radio,i%2,i/2);
    radio.CheckedChanged+=(s,e)=>{if(radio.Checked&&selected!=index){selected=index;if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}};
   }
   SelectedIndex=0;
  }
  protected override void OnLayout(LayoutEventArgs e){
   if(choices!=null&&choices.All(r=>r!=null)&&ClientSize.Width>0){
    int needed=choices.Max(r=>r.GetPreferredSize(Size.Empty).Width+r.Margin.Horizontal);
    int columns=ClientSize.Width>=needed*2?2:1;
    if(ColumnCount!=columns){
     SuspendLayout();Controls.Clear();ColumnCount=columns;RowCount=choices.Length/columns;ColumnStyles.Clear();RowStyles.Clear();
     for(int i=0;i<columns;i++)ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/columns));
     for(int i=0;i<RowCount;i++)RowStyles.Add(new RowStyle(SizeType.AutoSize));
     for(int i=0;i<choices.Length;i++)Controls.Add(choices[i],i%columns,i/columns);
     ResumeLayout(false);
    }
   }
   base.OnLayout(e);
  }
 }
 public sealed class ResetModePicker:FlowLayoutPanel {
  readonly RadioButton[] choices=new RadioButton[3];int selected=-1;
  public event EventHandler SelectedIndexChanged;
  public int SelectedIndex{get{return selected;}set{if(value<0||value>=choices.Length)throw new ArgumentOutOfRangeException("value");choices[value].Checked=true;}}
  public ResetModePicker(){
   AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;WrapContents=true;Margin=Padding.Empty;
   string[] labels={"不显示","具体时间","倒计时"};
   for(int i=0;i<choices.Length;i++){
    int index=i;var radio=new RadioButton{Text=labels[i],AutoSize=true,Margin=new Padding(0,5,20,5),AccessibleName="额度重置时间："+labels[i]};
    choices[i]=radio;Controls.Add(radio);
    radio.CheckedChanged+=(s,e)=>{if(radio.Checked&&selected!=index){selected=index;if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}};
   }
   SelectedIndex=0;
  }
 }
 public sealed class SettingsForm:Form {
  readonly QuotaService service;
  readonly AppConfig original;
  readonly TextBox cpaUrl=new TextBox(),cpaKey=new TextBox{UseSystemPasswordChar=true},cpampUrl=new TextBox(),cpampKey=new TextBox{UseSystemPasswordChar=true};
  readonly CheckBox cpamp=new CheckBox{Text="启用CPAMP历史用量",AutoSize=true};
  readonly CheckedListBox accounts=new CheckedListBox{CheckOnClick=true,IntegralHeight=false,Height=190};
  readonly ComboBox focus=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
  readonly ComboBox quotaDisplay=new ComboBox{Name="quotaDisplayMode",DropDownStyle=ComboBoxStyle.DropDownList};
  static readonly string[] DisplayModes={"single","average","sum"};
  readonly TextBox model=new TextBox();
  readonly CheckBox used=new CheckBox{Text="显示已用百分比（默认显示剩余额度）",AutoSize=true},historyBar=new CheckBox{Text="在任务栏右侧追加今日Token",AutoSize=true},single=new CheckBox{Text="使用单行紧凑显示",AutoSize=true},rotate=new CheckBox{Text="自动轮播已勾选的账号",AutoSize=true},autostart=new CheckBox{Text="登录Windows后自动启动",AutoSize=true};
  readonly NumericUpDown width=new NumericUpDown{Name="taskbarWidth",Minimum=80,Maximum=840},offset=new NumericUpDown{Name="taskbarOffset",Minimum=0,Maximum=800},fontSize=new NumericUpDown{Minimum=8,Maximum=12},interval=new NumericUpDown{Minimum=60,Maximum=3600,Increment=60},rotateInterval=new NumericUpDown{Minimum=5,Maximum=120};
  readonly ComboBox theme=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
  readonly ComboBox progressStyle=new ComboBox{Name="quotaProgressStyle",AccessibleName="进度条样式",DropDownStyle=ComboBoxStyle.DropDownList};
  static readonly string[] ProgressStyles={"thick","thin"};
  readonly CheckBox transparent=new CheckBox{Name="transparentBackground",Text="透明背景（融入任务栏）",AutoSize=true};
  readonly Label feedback=new Label{Name="settingsFeedback",AutoSize=true,Dock=DockStyle.Top,ForeColor=Look.Muted};
  readonly StripPreview preview=new StripPreview(),colorPreview=new StripPreview{Name="quotaColorPreview"},resetPreview=new StripPreview{Name="quotaResetPreview"};
  readonly ColorModePicker colorMode=new ColorModePicker{Name="quotaColorMode"};
  readonly NumericUpDown yellowUsed=new NumericUpDown{Name="yellowUsedPercent",Minimum=1,Maximum=99},redUsed=new NumericUpDown{Name="redUsedPercent",Minimum=2,Maximum=100};
  readonly Button lowColor=new Button{Name="lowUsageColor",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(8,4,8,4)},mediumColor=new Button{Name="mediumUsageColor",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(8,4,8,4)},highColor=new Button{Name="highUsageColor",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(8,4,8,4)};
  readonly TableLayoutPanel colorOptions=new TableLayoutPanel{Name="quotaColorOptions",AutoSize=true,ColumnCount=2,Dock=DockStyle.Top};
  readonly Label colorRanges=new Label{AutoSize=true,ForeColor=Look.Muted};
  static readonly string[] ColorModes={"default","background","numbers","both"};
  readonly ResetModePicker resetMode=new ResetModePicker{Name="quotaResetMode"};
  static readonly string[] ResetModes={"off","time","countdown"};
  bool updating,dirtyAccounts;
  public Action<AppConfig> Applied;
  public SettingsForm(AppConfig c,QuotaService svc,Snapshot state){
   SuspendLayout();AutoScaleMode=AutoScaleMode.None;
   original=c.Clone();service=svc;Text="CPA 状态栏 · 设置";Font=Look.Font(9.5f);BackColor=Color.FromArgb(246,248,252);
   ClientSize=new Size(760,770);MinimumSize=new Size(600,460);StartPosition=FormStartPosition.CenterScreen;
   var header=new TableLayoutPanel{Name="settingsHeader",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,ColumnCount=1,Padding=new Padding(20,14,20,12)};
   header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   var title=new Label{Name="settingsTitle",Text="CPA 额度状态栏",Font=Look.Font(17,FontStyle.Bold),ForeColor=Look.Ink,AutoSize=true,Dock=DockStyle.Top,Margin=Padding.Empty};
   var subtitle=new Label{Text="点击任务栏面板或托盘图标查看额度，右键打开菜单。",ForeColor=Look.Muted,AutoSize=true,Dock=DockStyle.Top,Margin=new Padding(0,4,0,0)};
   header.Controls.Add(title,0,0);header.Controls.Add(subtitle,0,1);header.Layout+=(s,e)=>{WrapToWidth(title,header.ClientSize.Width-header.Padding.Horizontal);WrapToWidth(subtitle,header.ClientSize.Width-header.Padding.Horizontal);};
   var tabs=new TabControl{Dock=DockStyle.Fill,Padding=new Point(20,7)};
   var connection=new TabPage("连接");var display=new TabPage("账号与指标");var appearance=new TabPage("外观与启动");var colors=new TabPage("额度配色");
   tabs.TabPages.AddRange(new[]{connection,display,appearance,colors});
   var links=Grid(connection);
   Add(links,"CPA服务地址",cpaUrl);Add(links,"CPA管理密钥",cpaKey);Hint(links,c.CpaSecret!=""&&!AppConfig.CanUnprotect(c.CpaSecret)?"已保存的管理密钥无法用当前Windows账号解密（可能更换了用户或电脑），请重新输入。":c.CpaSecret!=""?"管理密钥已加密保存，留空保持。更换地址时请重新输入。":"填写CPA管理页面的密钥，模型API Key不适用。");
   Add(links,"",cpamp);Add(links,"CPAMP服务地址",cpampUrl);Add(links,"CPAMP管理员密钥",cpampKey);Hint(links,c.CpampSecret!=""?"CPAMP管理员密钥已保存，留空保持。":"可选。启用后显示当前账号今日/本月Token与请求数。");
   var test=new Button{Text="测试连接",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(8,4,8,4)};Add(links,"",test);
   Hint(links,"密钥只保存为当前Windows用户的DPAPI密文，程序直接请求服务，不需要Node或本地API端口。");
   var targets=Grid(display);Add(targets,"监控账号",accounts);quotaDisplay.Items.AddRange(new object[]{"当前单个账号","已选账号：平均百分比","已选账号：累加百分比"});Add(targets,"额度显示方式",quotaDisplay);Add(targets,"任务栏当前账号",focus);
   Hint(targets,"汇总当前账号同渠道的已勾选账号，各额度周期分别计算。累加可超过100%，进度条跟随剩余或已用百分比，累加超过100%时封顶；失败或过期数据不参与，面板标明有效/总账号数。不同套餐的百分比不代表实际请求总量。最近重置是其中最早的一次，详情保留各账号时间。历史Token仍属于当前单个账号。");
   Add(targets,"模型ID / 片段",model);Hint(targets,"Google渠道可指定模型；留空显示余量最低的模型，并在详情中保留所有窗口。");

   Add(targets,"百分比含义",used);Add(targets,"额度重置时间",resetMode);resetPreview.Height=64;Add(targets,"显示预览",resetPreview);Hint(targets,"分别显示每个额度周期的重置时间，与查询刷新间隔无关。具体时间跟随Windows时区；倒计时本地自动更新。开启后可在“外观与启动”调大宽度（双行建议260—320像素）。");Add(targets,"可选历史",historyBar);Add(targets,"账号切换",rotate);Add(targets,"轮播间隔（秒）",rotateInterval);
   var looks=Grid(appearance);theme.Items.AddRange(new object[]{"跟随Windows系统","深色","浅色"});
   progressStyle.Items.AddRange(new object[]{"粗进度条","细进度条"});
    Add(looks,"进度条样式",progressStyle);
    Add(looks,"主题",theme);Add(looks,"背景",transparent);Add(looks,"宽度（像素）",width);Add(looks,"左移（像素）",offset);Add(looks,"字号",fontSize);Add(looks,"排版",single);Add(looks,"额度刷新（秒）",interval);Add(looks,"启动",autostart);
   Hint(looks,"宽度和偏移按屏幕实际像素应用，不再叠加Windows缩放。偏移越大，面板越靠左。");
   preview.Height=64;Add(looks,"任务栏预览",preview);Hint(looks,"点击任务栏数字打开详情，右键打开菜单。历史默认在详情里展示，也可追加到任务栏。");
   var coloring=Grid(colors);


   Add(coloring,"着色方式",colorMode);
   colorPreview.Height=64;Add(coloring,"任务栏预览",colorPreview);
   Hint(coloring,"粗进度条使用整行底色，按已用比例填充；细进度条跟随显示的剩余/已用百分比。可在“外观与启动”切换样式，颜色均按已用比例判断。");
   ConfigureGrid(colorOptions);
   Add(colorOptions,"低使用率",lowColor);Add(colorOptions,"中等使用率",mediumColor);Add(colorOptions,"高使用率",highColor);
   Add(colorOptions,"黄色起点（%）",yellowUsed);Add(colorOptions,"红色起点（%）",redUsed);
   Add(colorOptions,"颜色范围",colorRanges);
   var resetColors=new Button{Text="恢复默认颜色与阈值",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(8,4,8,4)};Add(colorOptions,"",resetColors);
   int optionsRow=coloring.RowCount++;coloring.RowStyles.Add(new RowStyle(SizeType.AutoSize));coloring.Controls.Add(colorOptions,0,optionsRow);coloring.SetColumnSpan(colorOptions,2);
   Hint(coloring,"默认模式使用统一底色和简洁数字。读取失败、额度未知或数据过期时不显示进度条。");
   var footer=new TableLayoutPanel{Name="settingsFooter",Dock=DockStyle.Bottom,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,Padding=new Padding(20,10,20,10),BackColor=Color.White};
   footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.RightToLeft,WrapContents=true,Margin=Padding.Empty};
   var cancel=new Button{Name="cancelSettings",Text="取消",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(12,6,12,6)};var save=new Button{Name="saveSettings",Text="保存并应用",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(12,6,12,6),BackColor=Look.Blue,ForeColor=Color.White,FlatStyle=FlatStyle.Flat};
   actions.Controls.Add(save);actions.Controls.Add(cancel);footer.Controls.Add(feedback,0,0);footer.Controls.Add(actions,0,1);
   footer.Layout+=(s,e)=>WrapToWidth(feedback,footer.ClientSize.Width-footer.Padding.Horizontal-feedback.Margin.Horizontal);
   Controls.Add(tabs);Controls.Add(header);Controls.Add(footer);
   cpaUrl.Text=c.CpaUrl;cpampUrl.Text=c.CpampUrl;cpamp.Checked=c.CpampEnabled;
   resetMode.SelectedIndex=Math.Max(0,Array.IndexOf(ResetModes,c.ResetTimeMode));resetMode.SelectedIndexChanged+=(s,e)=>UpdatePreview(service.State);
   model.Text=c.Model;used.Checked=c.ShowUsed;historyBar.Checked=c.HistoryInBar;single.Checked=c.SingleLine;rotate.Checked=c.Rotate;autostart.Checked=c.Autostart;transparent.Checked=c.TransparentBackground;
   quotaDisplay.SelectedIndex=Math.Max(0,Array.IndexOf(DisplayModes,c.QuotaDisplayMode));quotaDisplay.SelectedIndexChanged+=(s,e)=>UpdatePreview(service.State);
   width.Value=Math.Max(width.Minimum,Math.Min(width.Maximum,TaskbarMetrics.PixelWidth(c)));offset.Value=Math.Max(offset.Minimum,Math.Min(offset.Maximum,TaskbarMetrics.PixelOffset(c)));fontSize.Value=Math.Max(fontSize.Minimum,Math.Min(fontSize.Maximum,c.FontSize));interval.Value=Math.Max(interval.Minimum,Math.Min(interval.Maximum,c.RefreshSeconds));rotateInterval.Value=Math.Max(rotateInterval.Minimum,Math.Min(rotateInterval.Maximum,c.RotateSeconds));
   colorMode.SelectedIndex=Math.Max(0,Array.IndexOf(ColorModes,c.ColorMode));yellowUsed.Value=c.YellowUsedPercent;redUsed.Value=c.RedUsedPercent;
   SetColor(lowColor,c.LowUsageColor);SetColor(mediumColor,c.MediumUsageColor);SetColor(highColor,c.HighUsageColor);
   colorMode.SelectedIndexChanged+=(s,e)=>UpdatePreview(service.State);
   yellowUsed.ValueChanged+=(s,e)=>UpdatePreview(service.State);redUsed.ValueChanged+=(s,e)=>UpdatePreview(service.State);
   foreach(var button in new[]{lowColor,mediumColor,highColor})button.Click+=(s,e)=>PickColor((Button)s);
   resetColors.Click+=(s,e)=>{yellowUsed.Value=50;redUsed.Value=90;SetColor(lowColor,"#50D39A");SetColor(mediumColor,"#F1C553");SetColor(highColor,"#F17788");UpdatePreview(service.State);};
   theme.SelectedIndex=c.Theme=="dark"?1:c.Theme=="light"?2:0;
    progressStyle.SelectedIndex=Math.Max(0,Array.IndexOf(ProgressStyles,c.ProgressStyle));
   cpamp.CheckedChanged+=(s,e)=>{cpampUrl.Enabled=cpampKey.Enabled=cpamp.Checked;UpdatePreview(state);};cpampUrl.Enabled=cpampKey.Enabled=cpamp.Checked;
   accounts.ItemCheck+=(s,e)=>{if(!updating){dirtyAccounts=true;if(IsHandleCreated)BeginInvoke((Action)(()=>{if(!IsDisposed)UpdatePreview(service.State);}));}};
   foreach(Control input in new Control[]{width,offset,fontSize,theme,progressStyle,transparent,single,used,historyBar,model,focus}){var n=input as NumericUpDown;if(n!=null)n.ValueChanged+=(s,e)=>UpdatePreview(service.State);var b=input as CheckBox;if(b!=null)b.CheckedChanged+=(s,e)=>UpdatePreview(service.State);var combo=input as ComboBox;if(combo!=null)combo.SelectedIndexChanged+=(s,e)=>UpdatePreview(service.State);var text=input as TextBox;if(text!=null)text.TextChanged+=(s,e)=>UpdatePreview(service.State);}
   test.Click+=async(s,e)=>{test.Enabled=false;feedback.ForeColor=Look.Muted;feedback.Text="正在连接…";try{feedback.Text=await service.TestAsync(Draft(false));feedback.ForeColor=Color.SeaGreen;}catch(Exception ex){feedback.Text=ex.Message;feedback.ForeColor=Color.Firebrick;}finally{test.Enabled=true;}};
   cancel.Click+=(s,e)=>Close();
   save.Click+=(s,e)=>{try{var next=Draft(true);
     var plain=new[]{next.CpaUrl,next.CpampEnabled?next.CpampUrl:""}.Where(AppConfig.IsPlainRemote).ToList();
     if(plain.Count>0&&MessageBox.Show(this,"以下地址使用HTTP，管理密钥会以明文在网络上传输：\n"+String.Join("\n",plain)+"\n\n建议改用HTTPS。仍要保存吗？","CPA 状态栏",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
     next.Save();next.SetAutostart();if(Applied!=null)Applied(next);Close();}catch(Exception ex){feedback.Text=ex.Message;feedback.ForeColor=Color.Firebrick;}};
   AcceptButton=save;CancelButton=cancel;UpdateAccounts(state);UpdatePreview(state);
   AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);ResumeLayout(true);
  }
  protected override void OnShown(EventArgs e){
   FitToWorkingArea();base.OnShown(e);
  }
  protected override void OnDpiChanged(DpiChangedEventArgs e){
   base.OnDpiChanged(e);BeginInvoke((Action)FitToWorkingArea);
  }
  void FitToWorkingArea(){
   if(IsDisposed)return;
   var work=Screen.FromHandle(Handle).WorkingArea;
   MinimumSize=new Size(Math.Min(MinimumSize.Width,work.Width),Math.Min(MinimumSize.Height,work.Height));
   Size=new Size(Math.Min(Width,work.Width),Math.Min(Height,work.Height));
   Location=new Point(Math.Max(work.Left,Math.Min(Left,work.Right-Width)),Math.Max(work.Top,Math.Min(Top,work.Bottom-Height)));
  }
  static void WrapToWidth(Control control,int width){
   var maximum=new Size(Math.Max(1,width),0);if(control.MaximumSize!=maximum)control.MaximumSize=maximum;
  }
  static void ConfigureGrid(TableLayoutPanel grid){
   grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   grid.Layout+=(s,e)=>{
    var columns=grid.GetColumnWidths();if(columns.Length<2||columns[1]<=0)return;
    foreach(Control control in grid.Controls)if(grid.GetColumn(control)==1&&(control is Label||control is CheckBox))WrapToWidth(control,columns[1]-control.Margin.Horizontal);
   };
  }
  static TableLayoutPanel Grid(TabPage page){
   page.BackColor=Color.White;page.Padding=new Padding(18);
   var panel=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,Padding=new Padding(0,4,12,12)};ConfigureGrid(panel);
   var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};scroll.Controls.Add(panel);page.Controls.Add(scroll);
   scroll.Layout+=(s,e)=>{var maximum=new Size(Math.Max(1,scroll.ClientSize.Width-SystemInformation.VerticalScrollBarWidth),0);if(panel.MaximumSize!=maximum)panel.MaximumSize=maximum;};return panel;
  }
  static void Add(TableLayoutPanel grid,string caption,Control input){
   int row=grid.RowCount++;grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   var label=new Label{Text=caption,AutoSize=true,Anchor=AnchorStyles.Left,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Look.Ink,Margin=new Padding(0,5,12,5)};
   input.Margin=new Padding(0,5,0,5);input.Anchor=AnchorStyles.Left|AnchorStyles.Right;
   grid.Controls.Add(label,0,row);grid.Controls.Add(input,1,row);
  }
  static void Hint(TableLayoutPanel grid,string text){int row=grid.RowCount++;grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));var label=new Label{Text=text,AutoSize=true,Dock=DockStyle.Top,ForeColor=Look.Muted,Font=Look.Font(8.5f),Margin=new Padding(0,0,0,12)};grid.Controls.Add(label,1,row);}
  public void UpdateAccounts(Snapshot s){
   var all=s.Accounts.Where(a=>a.Supported).ToList();
   if(!accounts.Items.Cast<Account>().Select(a=>a.Id).SequenceEqual(all.Select(a=>a.Id))){
    var checkedIds=dirtyAccounts?accounts.CheckedItems.Cast<Account>().Select(a=>a.Id).ToList():original.SelectedIds;
    string focusId=(focus.SelectedItem as Account)==null?original.FocusId:((Account)focus.SelectedItem).Id;
    updating=true;accounts.Items.Clear();focus.Items.Clear();
    var first=all.FirstOrDefault(a=>a.Provider=="codex"&&!a.Disabled)??all.FirstOrDefault();
    foreach(var a in all){accounts.Items.Add(a,checkedIds.Contains(a.Id)||checkedIds.Count==0&&a==first);focus.Items.Add(a);}
    focus.SelectedItem=all.FirstOrDefault(a=>a.Id==focusId)??first;updating=false;
   }
   UpdatePreview(s);
  }
  AppConfig VisualDraft(){
   var displayMode=DisplayModes[Math.Max(0,quotaDisplay.SelectedIndex)];
   var c=original.Clone();c.ResetTimeMode=ResetModes[Math.Max(0,resetMode.SelectedIndex)];c.Model=model.Text.Trim();c.ShowUsed=used.Checked;c.HistoryInBar=historyBar.Checked;c.SingleLine=single.Checked;c.CpampEnabled=cpamp.Checked;c.Width=(int)width.Value;c.Offset=(int)offset.Value;c.PhysicalPixels=true;c.FontSize=(int)fontSize.Value;c.Theme=theme.SelectedIndex==1?"dark":theme.SelectedIndex==2?"light":"auto";
   c.TransparentBackground=transparent.Checked;c.ProgressStyle=ProgressStyles[Math.Max(0,progressStyle.SelectedIndex)];
   c.QuotaDisplayMode=displayMode;if(accounts.Items.Count>0)c.SelectedIds=accounts.CheckedItems.Cast<Account>().Select(a=>a.Id).ToList();
   c.ColorMode=ColorModes[Math.Max(0,colorMode.SelectedIndex)];c.YellowUsedPercent=(int)yellowUsed.Value;c.RedUsedPercent=(int)redUsed.Value;
   c.LowUsageColor=(string)lowColor.Tag;c.MediumUsageColor=(string)mediumColor.Tag;c.HighUsageColor=(string)highColor.Tag;
   var selected=focus.SelectedItem as Account;if(selected!=null)c.FocusId=selected.Id;
   if(QuotaAggregation.Enabled(c)&&c.SelectedIds.Count>0&&!c.SelectedIds.Contains(c.FocusId))c.FocusId=c.SelectedIds[0];return c;
  }
  AppConfig Draft(bool selection){
   var c=VisualDraft();
   if(c.ColorMode!="default"&&c.YellowUsedPercent>=c.RedUsedPercent)throw new Exception("黄色起点必须小于红色起点。");
   c.CpaUrl=AppConfig.Url(cpaUrl.Text);c.CpampUrl=String.IsNullOrWhiteSpace(cpampUrl.Text)&&!cpamp.Checked?"":AppConfig.Url(cpampUrl.Text);
   if(c.CpaUrl!=original.CpaUrl&&cpaKey.Text=="")throw new Exception("更换CPA地址时需要重新输入管理密钥。");
   if(c.CpampUrl!=original.CpampUrl&&c.CpampEnabled&&cpampKey.Text=="")throw new Exception("更换CPAMP地址时需要重新输入管理员密钥。");
   if(cpaKey.Text==""&&c.CpaSecret!=""&&!AppConfig.CanUnprotect(c.CpaSecret))throw new Exception("已保存的CPA管理密钥无法用当前Windows账号解密，请重新输入。");
   if(c.CpampEnabled&&cpampKey.Text==""&&c.CpampSecret!=""&&!AppConfig.CanUnprotect(c.CpampSecret))throw new Exception("已保存的CPAMP管理员密钥无法用当前Windows账号解密，请重新输入。");
   if(cpaKey.Text!="")c.CpaSecret=AppConfig.Protect(cpaKey.Text.Trim());if(cpampKey.Text!="")c.CpampSecret=AppConfig.Protect(cpampKey.Text.Trim());
   if(c.CpaSecret=="")throw new Exception("请填写CPA管理密钥。");if(c.CpampEnabled&&c.CpampSecret=="")throw new Exception("启用CPAMP需要管理员密钥。");
   c.RefreshSeconds=(int)interval.Value;c.Rotate=rotate.Checked&&!QuotaAggregation.Enabled(c);c.RotateSeconds=(int)rotateInterval.Value;c.Autostart=autostart.Checked;
   if(selection&&accounts.Items.Count>0){c.SelectedIds=accounts.CheckedItems.Cast<Account>().Select(a=>a.Id).ToList();if(QuotaAggregation.Enabled(c)&&c.SelectedIds.Count==0)throw new Exception("汇总模式请至少勾选一个监控账号。");if(!QuotaAggregation.Enabled(c)&&c.FocusId!=""&&!c.SelectedIds.Contains(c.FocusId))c.SelectedIds.Add(c.FocusId);}
   return c;
  }
  static void SetColor(Button button,string hex){
   button.Tag=hex.ToUpperInvariant();button.Text=hex.ToUpperInvariant();button.BackColor=ColorTranslator.FromHtml(hex);button.ForeColor=QuotaColors.Luminance(button.BackColor)>.4?Color.Black:Color.White;button.UseVisualStyleBackColor=false;
   button.AccessibleName=button.Name+" "+hex;
  }
  void PickColor(Button button){using(var dialog=new ColorDialog{Color=ColorTranslator.FromHtml((string)button.Tag),FullOpen=true})if(dialog.ShowDialog(this)==DialogResult.OK){SetColor(button,"#"+dialog.Color.R.ToString("X2")+dialog.Color.G.ToString("X2")+dialog.Color.B.ToString("X2"));UpdatePreview(service.State);}}
  public void RefreshClock(){foreach(var strip in new[]{preview,colorPreview,resetPreview})if(strip.Visible&&strip.Config!=null&&QuotaReset.Enabled(strip.Config))strip.Invalidate();}
  void UpdatePreview(Snapshot s){
   var draft=VisualDraft();colorOptions.Visible=draft.ColorMode!="default";
   rotate.Enabled=!QuotaAggregation.Enabled(draft);rotateInterval.Enabled=rotate.Enabled;
   colorRanges.Text=String.Format("已用 <{0}%：低使用率；{0}%–<{1}%：中等使用率；≥{1}%：高使用率。",draft.YellowUsedPercent,draft.RedUsedPercent);
   foreach(var strip in new[]{preview,colorPreview,resetPreview}){strip.Config=draft;strip.State=s;strip.Invalidate();}
  }
 }
}
