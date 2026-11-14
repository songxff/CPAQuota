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
   ColumnCount=2;RowCount=2;Height=66;Margin=Padding.Empty;
   ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
   string[] labels={"默认（保留当前样式）","仅背景填充","仅百分比数字","背景填充 + 百分比数字"};
   for(int i=0;i<choices.Length;i++){
    int index=i;var radio=new RadioButton{Text=labels[i],AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,4,8,4),AccessibleName=labels[i]};
    choices[i]=radio;Controls.Add(radio,i%2,i/2);
    radio.CheckedChanged+=(s,e)=>{if(radio.Checked&&selected!=index){selected=index;if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}};
   }
   SelectedIndex=0;
  }
 }
 public sealed class ResetModePicker:FlowLayoutPanel {
  readonly RadioButton[] choices=new RadioButton[3];int selected=-1;
  public event EventHandler SelectedIndexChanged;
  public int SelectedIndex{get{return selected;}set{if(value<0||value>=choices.Length)throw new ArgumentOutOfRangeException("value");choices[value].Checked=true;}}
  public ResetModePicker(){
   Height=34;WrapContents=false;Margin=Padding.Empty;
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
  readonly TextBox model=new TextBox();
  readonly CheckBox used=new CheckBox{Text="显示已用百分比（默认显示剩余额度）",AutoSize=true},historyBar=new CheckBox{Text="在任务栏右侧追加今日Token",AutoSize=true},single=new CheckBox{Text="使用单行紧凑显示",AutoSize=true},rotate=new CheckBox{Text="自动轮播已勾选的账号",AutoSize=true},autostart=new CheckBox{Text="登录Windows后自动启动",AutoSize=true};
  readonly NumericUpDown width=new NumericUpDown{Name="taskbarWidth",Minimum=80,Maximum=840},offset=new NumericUpDown{Name="taskbarOffset",Minimum=0,Maximum=800},fontSize=new NumericUpDown{Minimum=8,Maximum=12},interval=new NumericUpDown{Minimum=60,Maximum=3600,Increment=60},rotateInterval=new NumericUpDown{Minimum=5,Maximum=120};
  readonly ComboBox theme=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
  readonly Label feedback=new Label{AutoSize=false,Dock=DockStyle.Fill,ForeColor=Look.Muted};
  readonly StripPreview preview=new StripPreview(),colorPreview=new StripPreview{Name="quotaColorPreview"},resetPreview=new StripPreview{Name="quotaResetPreview"};
  readonly ColorModePicker colorMode=new ColorModePicker{Name="quotaColorMode"};
  readonly NumericUpDown yellowUsed=new NumericUpDown{Name="yellowUsedPercent",Minimum=1,Maximum=99},redUsed=new NumericUpDown{Name="redUsedPercent",Minimum=2,Maximum=100};
  readonly Button lowColor=new Button{Name="lowUsageColor",Height=30},mediumColor=new Button{Name="mediumUsageColor",Height=30},highColor=new Button{Name="highUsageColor",Height=30};
  readonly TableLayoutPanel colorOptions=new TableLayoutPanel{Name="quotaColorOptions",AutoSize=true,ColumnCount=2,Dock=DockStyle.Top};
  readonly Label colorRanges=new Label{AutoSize=true,MaximumSize=new Size(460,0),ForeColor=Look.Muted};
  static readonly string[] ColorModes={"default","background","numbers","both"};
  readonly ResetModePicker resetMode=new ResetModePicker{Name="quotaResetMode"};
  static readonly string[] ResetModes={"off","time","countdown"};
  bool updating,dirtyAccounts;
  public Action<AppConfig> Applied;
  public SettingsForm(AppConfig c,QuotaService svc,Snapshot state){
   original=c.Clone();service=svc;Text="CPA 状态栏 · 设置";Font=Look.Font(9.5f);BackColor=Color.FromArgb(246,248,252);
   AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
   ClientSize=new Size(760,770);MinimumSize=new Size(700,650);StartPosition=FormStartPosition.CenterScreen;
   var title=new Label{Text="CPA 额度状态栏",Font=Look.Font(17,FontStyle.Bold),ForeColor=Look.Ink,AutoSize=false,Dock=DockStyle.Top,Height=44,Padding=new Padding(20,10,0,0)};
   var subtitle=new Label{Text="独立运行，固定显示在系统托盘左侧。",ForeColor=Look.Muted,Dock=DockStyle.Top,Height=30,Padding=new Padding(22,2,0,0)};
   var tabs=new TabControl{Dock=DockStyle.Fill,Padding=new Point(20,7)};
   var connection=new TabPage("连接");var display=new TabPage("账号与指标");var appearance=new TabPage("外观与启动");var colors=new TabPage("额度配色");
   tabs.TabPages.AddRange(new[]{connection,display,appearance,colors});
   var links=Grid(connection);
   Add(links,"CPA服务地址",cpaUrl);Add(links,"CPA管理密钥",cpaKey);Hint(links,c.CpaSecret!=""?"管理密钥已加密保存，留空保持。更换地址时请重新输入。":"填写CPA管理页面的密钥，模型API Key不适用。");
   Add(links,"",cpamp);Add(links,"CPAMP服务地址",cpampUrl);Add(links,"CPAMP管理员密钥",cpampKey);Hint(links,c.CpampSecret!=""?"CPAMP管理员密钥已保存，留空保持。":"可选。启用后显示当前账号今日/本月Token与请求数。");
   var test=new Button{Text="测试连接",AutoSize=true,Height=32};Add(links,"",test);
   Hint(links,"密钥只保存为当前Windows用户的DPAPI密文，程序直接请求服务，不需要Node或本地API端口。");
   var targets=Grid(display);Add(targets,"监控账号",accounts);Add(targets,"任务栏当前账号",focus);Add(targets,"模型ID / 片段",model);Hint(targets,"Google渠道可指定模型；留空显示余量最低的模型，并在详情中保留所有窗口。");

   Add(targets,"百分比含义",used);Add(targets,"额度重置时间",resetMode);resetPreview.Height=64;Add(targets,"显示预览",resetPreview);Hint(targets,"分别显示每个额度周期的重置时间，与查询刷新间隔无关。具体时间跟随Windows时区；倒计时本地自动更新。开启后可在“外观与启动”调大宽度（双行建议260—320像素）。");Add(targets,"可选历史",historyBar);Add(targets,"账号切换",rotate);Add(targets,"轮播间隔（秒）",rotateInterval);
   var looks=Grid(appearance);theme.Items.AddRange(new object[]{"跟随Windows系统","深色","浅色"});
   Add(looks,"主题",theme);Add(looks,"宽度（像素）",width);Add(looks,"左移（像素）",offset);Add(looks,"字号",fontSize);Add(looks,"排版",single);Add(looks,"额度刷新（秒）",interval);Add(looks,"启动",autostart);
   Hint(looks,"宽度和偏移按屏幕实际像素应用，不再叠加Windows缩放。偏移越大，面板越靠左。");
   preview.Height=64;Add(looks,"任务栏预览",preview);Hint(looks,"点击任务栏数字打开详情，右键打开菜单。历史默认在详情里展示，也可追加到任务栏。");
   var coloring=Grid(colors);


   Add(coloring,"着色方式",colorMode);
   colorPreview.Height=64;Add(coloring,"任务栏预览",colorPreview);
   Hint(coloring,"颜色和填充长度都按已用比例计算；数字仍按“账号与指标”中的选择显示剩余或已用。");
   colorOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));colorOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   Add(colorOptions,"低使用率",lowColor);Add(colorOptions,"中等使用率",mediumColor);Add(colorOptions,"高使用率",highColor);
   Add(colorOptions,"黄色起点（%）",yellowUsed);Add(colorOptions,"红色起点（%）",redUsed);
   Add(colorOptions,"颜色范围",colorRanges);
   var resetColors=new Button{Text="恢复默认颜色与阈值",AutoSize=true,Height=30};Add(colorOptions,"",resetColors);
   int optionsRow=coloring.RowCount++;coloring.Controls.Add(colorOptions,0,optionsRow);coloring.SetColumnSpan(colorOptions,2);
   Hint(coloring,"默认模式保持原有底色与数字样式。读取失败、额度未知或数据过期时不显示进度填充。");
   var footer=new TableLayoutPanel{Dock=DockStyle.Bottom,Height=70,ColumnCount=3,Padding=new Padding(20,12,20,12),BackColor=Color.White};
   footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,84));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,118));
   var cancel=new Button{Text="取消",Dock=DockStyle.Fill};var save=new Button{Text="保存并应用",Dock=DockStyle.Fill,BackColor=Look.Blue,ForeColor=Color.White,FlatStyle=FlatStyle.Flat};
   footer.Controls.Add(feedback,0,0);footer.Controls.Add(cancel,1,0);footer.Controls.Add(save,2,0);
   Controls.Add(tabs);Controls.Add(subtitle);Controls.Add(title);Controls.Add(footer);
   cpaUrl.Text=c.CpaUrl;cpampUrl.Text=c.CpampUrl;cpamp.Checked=c.CpampEnabled;
   resetMode.SelectedIndex=Math.Max(0,Array.IndexOf(ResetModes,c.ResetTimeMode));resetMode.SelectedIndexChanged+=(s,e)=>UpdatePreview(service.State);
   model.Text=c.Model;used.Checked=c.ShowUsed;historyBar.Checked=c.HistoryInBar;single.Checked=c.SingleLine;rotate.Checked=c.Rotate;autostart.Checked=c.Autostart;
   width.Value=Math.Max(width.Minimum,Math.Min(width.Maximum,TaskbarMetrics.PixelWidth(c)));offset.Value=Math.Max(offset.Minimum,Math.Min(offset.Maximum,TaskbarMetrics.PixelOffset(c)));fontSize.Value=Math.Max(fontSize.Minimum,Math.Min(fontSize.Maximum,c.FontSize));interval.Value=Math.Max(interval.Minimum,Math.Min(interval.Maximum,c.RefreshSeconds));rotateInterval.Value=Math.Max(rotateInterval.Minimum,Math.Min(rotateInterval.Maximum,c.RotateSeconds));
   colorMode.SelectedIndex=Math.Max(0,Array.IndexOf(ColorModes,c.ColorMode));yellowUsed.Value=c.YellowUsedPercent;redUsed.Value=c.RedUsedPercent;
   SetColor(lowColor,c.LowUsageColor);SetColor(mediumColor,c.MediumUsageColor);SetColor(highColor,c.HighUsageColor);
   colorMode.SelectedIndexChanged+=(s,e)=>UpdatePreview(service.State);
   yellowUsed.ValueChanged+=(s,e)=>UpdatePreview(service.State);redUsed.ValueChanged+=(s,e)=>UpdatePreview(service.State);
   foreach(var button in new[]{lowColor,mediumColor,highColor})button.Click+=(s,e)=>PickColor((Button)s);
   resetColors.Click+=(s,e)=>{yellowUsed.Value=50;redUsed.Value=90;SetColor(lowColor,"#50D39A");SetColor(mediumColor,"#F1C553");SetColor(highColor,"#F17788");UpdatePreview(service.State);};
   theme.SelectedIndex=c.Theme=="dark"?1:c.Theme=="light"?2:0;
   cpamp.CheckedChanged+=(s,e)=>{cpampUrl.Enabled=cpampKey.Enabled=cpamp.Checked;UpdatePreview(state);};cpampUrl.Enabled=cpampKey.Enabled=cpamp.Checked;
   accounts.ItemCheck+=(s,e)=>{if(!updating)dirtyAccounts=true;};
   foreach(Control input in new Control[]{width,offset,fontSize,theme,single,used,historyBar,model,focus}){var n=input as NumericUpDown;if(n!=null)n.ValueChanged+=(s,e)=>UpdatePreview(service.State);var b=input as CheckBox;if(b!=null)b.CheckedChanged+=(s,e)=>UpdatePreview(service.State);var combo=input as ComboBox;if(combo!=null)combo.SelectedIndexChanged+=(s,e)=>UpdatePreview(service.State);var text=input as TextBox;if(text!=null)text.TextChanged+=(s,e)=>UpdatePreview(service.State);}
   test.Click+=async(s,e)=>{test.Enabled=false;feedback.ForeColor=Look.Muted;feedback.Text="正在连接…";try{feedback.Text=await service.TestAsync(Draft(false));feedback.ForeColor=Color.SeaGreen;}catch(Exception ex){feedback.Text=ex.Message;feedback.ForeColor=Color.Firebrick;}finally{test.Enabled=true;}};
   cancel.Click+=(s,e)=>Close();
   save.Click+=(s,e)=>{try{var next=Draft(true);next.Save();next.SetAutostart();if(Applied!=null)Applied(next);Close();}catch(Exception ex){feedback.Text=ex.Message;feedback.ForeColor=Color.Firebrick;}};
   AcceptButton=save;CancelButton=cancel;UpdateAccounts(state);UpdatePreview(state);
  }
  static TableLayoutPanel Grid(TabPage page){
   page.BackColor=Color.White;page.Padding=new Padding(18);
   var panel=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(0,4,12,12)};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};scroll.Controls.Add(panel);page.Controls.Add(scroll);
   scroll.Resize+=(s,e)=>panel.Width=Math.Max(500,scroll.ClientSize.Width-20);return panel;
  }
  static void Add(TableLayoutPanel grid,string caption,Control input){
   int row=grid.RowCount++;grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   var label=new Label{Text=caption,AutoSize=false,Height=30,Dock=DockStyle.Top,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Look.Ink,Margin=new Padding(0,5,8,5)};
   input.Margin=new Padding(0,5,0,5);input.Anchor=AnchorStyles.Left|AnchorStyles.Right;
   if(input is TextBox)input.Height=28;if(input is ComboBox)input.Height=28;
   grid.Controls.Add(label,0,row);grid.Controls.Add(input,1,row);
  }
  static void Hint(TableLayoutPanel grid,string text){int row=grid.RowCount++;var label=new Label{Text=text,AutoSize=true,MaximumSize=new Size(470,0),ForeColor=Look.Muted,Font=Look.Font(8.5f),Margin=new Padding(0,0,0,12)};grid.Controls.Add(label,1,row);}
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
   var c=original.Clone();c.ResetTimeMode=ResetModes[Math.Max(0,resetMode.SelectedIndex)];c.Model=model.Text.Trim();c.ShowUsed=used.Checked;c.HistoryInBar=historyBar.Checked;c.SingleLine=single.Checked;c.CpampEnabled=cpamp.Checked;c.Width=(int)width.Value;c.Offset=(int)offset.Value;c.PhysicalPixels=true;c.FontSize=(int)fontSize.Value;c.Theme=theme.SelectedIndex==1?"dark":theme.SelectedIndex==2?"light":"auto";
   c.ColorMode=ColorModes[Math.Max(0,colorMode.SelectedIndex)];c.YellowUsedPercent=(int)yellowUsed.Value;c.RedUsedPercent=(int)redUsed.Value;
   c.LowUsageColor=(string)lowColor.Tag;c.MediumUsageColor=(string)mediumColor.Tag;c.HighUsageColor=(string)highColor.Tag;
   var selected=focus.SelectedItem as Account;if(selected!=null)c.FocusId=selected.Id;return c;
  }
  AppConfig Draft(bool selection){
   var c=VisualDraft();
   if(c.ColorMode!="default"&&c.YellowUsedPercent>=c.RedUsedPercent)throw new Exception("黄色起点必须小于红色起点。");
   c.CpaUrl=AppConfig.Url(cpaUrl.Text);c.CpampUrl=String.IsNullOrWhiteSpace(cpampUrl.Text)&&!cpamp.Checked?"":AppConfig.Url(cpampUrl.Text);
   if(c.CpaUrl!=original.CpaUrl&&cpaKey.Text=="")throw new Exception("更换CPA地址时需要重新输入管理密钥。");
   if(c.CpampUrl!=original.CpampUrl&&c.CpampEnabled&&cpampKey.Text=="")throw new Exception("更换CPAMP地址时需要重新输入管理员密钥。");
   if(cpaKey.Text!="")c.CpaSecret=AppConfig.Protect(cpaKey.Text.Trim());if(cpampKey.Text!="")c.CpampSecret=AppConfig.Protect(cpampKey.Text.Trim());
   if(c.CpaSecret=="")throw new Exception("请填写CPA管理密钥。");if(c.CpampEnabled&&c.CpampSecret=="")throw new Exception("启用CPAMP需要管理员密钥。");
   c.RefreshSeconds=(int)interval.Value;c.Rotate=rotate.Checked;c.RotateSeconds=(int)rotateInterval.Value;c.Autostart=autostart.Checked;
   if(selection&&accounts.Items.Count>0){c.SelectedIds=accounts.CheckedItems.Cast<Account>().Select(a=>a.Id).ToList();if(c.FocusId!=""&&!c.SelectedIds.Contains(c.FocusId))c.SelectedIds.Add(c.FocusId);}
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
   colorRanges.Text=String.Format("已用 <{0}%：低使用率；{0}%–<{1}%：中等使用率；≥{1}%：高使用率。",draft.YellowUsedPercent,draft.RedUsedPercent);
   foreach(var strip in new[]{preview,colorPreview,resetPreview}){strip.Config=draft;strip.State=s;strip.Invalidate();}
  }
 }
}
