using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;

namespace CpaTaskbar {
 public static class SelfTests{
  static void Check(bool value,string message){if(!value)throw new Exception(message);}
  static object Parse(string value){return J.Parse(value);}
  public static object Shell(){
   var taskbar=Native.FindWindow("Shell_TrayWnd",null);var rebar=Native.FindWindowEx(taskbar,IntPtr.Zero,"ReBarWindow32",null);var list=Native.FindWindowEx(rebar,IntPtr.Zero,"MSTaskSwWClass",null);var tray=Native.FindWindowEx(taskbar,IntPtr.Zero,"TrayNotifyWnd",null);
   return new{taskbar=Native.Bounds(taskbar),rebar=Native.Bounds(rebar),tasklist=Native.Bounds(list),tray=Native.Bounds(tray),taskbarClass=Native.Class(taskbar)};
  }
  public static void Run(string report){
   var results=new List<object>();
   Action<string,Action> test=(name,run)=>{run();results.Add(new{name=name,passed=true});};
   test("Codex按实际周期识别5小时与周，保留0使用率",()=>{
    var w=QuotaParser.Codex(Parse("{\"rate_limit\":{\"primary_window\":{\"used_percent\":0,\"limit_window_seconds\":604800},\"secondary_window\":{\"used_percent\":28,\"limit_window_seconds\":18000,\"reset_after_seconds\":3600}}}"));
    Check(w[0].Id=="week"&&w[0].Remaining==100,"weekly swapped");Check(w[1].Id=="5h"&&w[1].Remaining==72,"5h");Check(w[1].ResetAt>J.Now,"reset");
   });
   test("Claude未知额度保留null，模型周窗口独立",()=>{
    var w=QuotaParser.Claude(Parse("{\"five_hour\":{\"utilization\":null},\"seven_day_opus\":{\"utilization\":40}}"));
    Check(w.Count==2&&!w[0].Remaining.HasValue&&w[1].Remaining==60,"claude null");
   });
   test("Google兼容模型与quota summary结构",()=>{
    var w=QuotaParser.Google(Parse("{\"models\":{\"gemini-pro\":{\"quotaInfo\":{\"remainingFraction\":0.85}}}}"));
    Check(w[0].Remaining==85,"google models");
    w=QuotaParser.Google(Parse("{\"groups\":[{\"displayName\":\"Claude\",\"buckets\":[{\"bucketId\":\"week\",\"remainingFraction\":0}]}]}"));
    Check(w[0].Remaining==0&&!w[0].Duration.HasValue,"google groups");
   });
   test("CPA外层200仍检查上游status_code并解析body字符串",()=>{
    var p=QuotaParser.Unwrap(Parse("{\"status_code\":200,\"body\":\"{\\\"ok\\\":true}\"}"));Check((bool)J.Get(p,"ok"),"unwrap");
    bool failed=false;try{QuotaParser.Unwrap(Parse("{\"status_code\":401,\"body\":\"secret\"}"));}catch(ProviderException ex){failed=ex.Status==401&&ex.Message.IndexOf("secret")<0;}Check(failed,"status validation");
   });
   test("账号标识稳定且不保留access token",()=>{
    var a=QuotaParser.Accounts(Parse("{\"files\":[{\"name\":\"工作.json\",\"provider\":\"codex\",\"auth_index\":1,\"access_token\":\"must-not-leak\"}]}"))[0];
    var b=QuotaParser.Accounts(Parse("{\"files\":[{\"name\":\"工作.json\",\"provider\":\"codex\",\"auth_index\":2}]}"))[0];
    Check(a.Id==b.Id&&!J.Json(a).Contains("must-not-leak"),"account identity");
   });
   test("北京时间今日和月起点",()=>{
    long at=DateTimeOffset.Parse("2026-11-13T02:06:00Z").ToUnixTimeMilliseconds();
    Check(QuotaParser.DayStart(at,false)==DateTimeOffset.Parse("2026-11-12T16:00:00Z").ToUnixTimeMilliseconds(),"day");
    Check(QuotaParser.DayStart(at,true)==DateTimeOffset.Parse("2026-10-31T16:00:00Z").ToUnixTimeMilliseconds(),"month");
   });
   test("DPAPI加密真实往返，JSON配置无明文密钥",()=>{
    var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",CpaSecret=AppConfig.Protect("test-only-中文-$-secret")};Check(AppConfig.Unprotect(c.CpaSecret)=="test-only-中文-$-secret","DPAPI");Check(!J.Json(c).Contains("test-only"),"plaintext");
   });
   test("窗口过期和读取失败不会显示伪造0",()=>{
    var w=new QuotaWindow{Used=20,ResetAt=J.Now-1000};Check(!w.Valid,"expired");
    var c=new AppConfig();Check(Look.Value(w,true,c)=="—","expired text");Check(Look.Value(new QuotaWindow{Used=20},false,c)=="—","failed text");
   });
   test("Google指定模型只选择匹配窗口",()=>{
    var a=new Account{Provider="antigravity"};var q=new QuotaState{Windows=new List<QuotaWindow>{new QuotaWindow{Id="a",Label="Gemini Pro",Used=20},new QuotaWindow{Id="b",Label="Claude",Used=90}}};
    var w=Look.BarWindows(q,new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",Model="Gemini"},a);Check(w.Count==1&&w[0].Id=="a","model filter");
   });
   test("CPAMP默认关闭，默认任务栏宽度紧凑",()=>{var c=new AppConfig();Check(!c.CpampEnabled&&!c.HistoryInBar&&c.Width==152,"defaults");});
   test("原生HTTP调用链：账号、缓存、可选历史与错误隔离",()=>Integration().GetAwaiter().GetResult());
   test("旧配置升级默认不启用自定义配色，新配置完整保存",()=>{
    string oldFolder=AppConfig.Folder;
    string temp=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"appearance-fixture-"+Guid.NewGuid().ToString("N"));
    try{
     AppConfig.Folder=temp;Directory.CreateDirectory(Path.GetDirectoryName(AppConfig.FileName));
     File.WriteAllText(AppConfig.FileName,"{\"Width\":188,\"ShowUsed\":true}");
     var c=AppConfig.Load();Check(c.ColorMode=="default"&&c.YellowUsedPercent==50&&c.RedUsedPercent==90&&c.Width==188&&c.ShowUsed,"legacy defaults");
     c.ColorMode="both";c.ResetTimeMode="countdown";c.YellowUsedPercent=40;c.RedUsedPercent=85;c.LowUsageColor="#12785A";c.MediumUsageColor="#D4A928";c.HighUsageColor="#DA304F";c.Save();
     var loaded=AppConfig.Load();Check(loaded.ResetTimeMode=="countdown"&&loaded.ColorMode=="both"&&loaded.YellowUsedPercent==40&&loaded.RedUsedPercent==85&&loaded.LowUsageColor=="#12785A"&&loaded.MediumUsageColor=="#D4A928"&&loaded.HighUsageColor=="#DA304F","appearance roundtrip");
     loaded.ColorMode="invalid";loaded.YellowUsedPercent=95;loaded.RedUsedPercent=80;loaded.LowUsageColor="#XXYYZZ";loaded.NormalizeAppearance();
     Check(loaded.ColorMode=="default"&&loaded.YellowUsedPercent==50&&loaded.RedUsedPercent==90&&loaded.LowUsageColor=="#50D39A","invalid config fallback");
    }finally{
     if(File.Exists(AppConfig.FileName)&&AppConfig.Folder==temp)File.Delete(AppConfig.FileName);
     if(Directory.Exists(Path.Combine(temp,"data")))Directory.Delete(Path.Combine(temp,"data"));
     if(Directory.Exists(temp))Directory.Delete(temp);AppConfig.Folder=oldFolder;
    }
   });
   test("按已用比例准确判断默认和自定义阈值",()=>{
    var c=new AppConfig();Color green=ColorTranslator.FromHtml(c.LowUsageColor),yellow=ColorTranslator.FromHtml(c.MediumUsageColor),red=ColorTranslator.FromHtml(c.HighUsageColor);
    Check(QuotaColors.UsageColor(0,c)==green&&QuotaColors.UsageColor(49.99,c)==green,"green boundary");
    Check(QuotaColors.UsageColor(50,c)==yellow&&QuotaColors.UsageColor(89.99,c)==yellow,"yellow boundary");
    Check(QuotaColors.UsageColor(90,c)==red&&QuotaColors.UsageColor(100,c)==red,"red boundary");
    c.YellowUsedPercent=30;c.RedUsedPercent=80;
    Check(QuotaColors.UsageColor(29.99,c)==green&&QuotaColors.UsageColor(30,c)==yellow&&QuotaColors.UsageColor(80,c)==red,"custom boundaries");
   });
   test("两行背景分别按已用72和35填充，数字仍显示余量28和65",()=>{
    var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",ColorMode="background",Theme="dark",FocusId="example"};var s=ColorExample();Color bg=Look.StripSurface(true,false);
    using(var b=StripBitmap(s,c,200,48)){
     var windows=s.Quotas["example"].Windows;
     Color top=QuotaColors.ProgressSurface(bg,windows[0],true,c,true),bottom=QuotaColors.ProgressSurface(bg,windows[1],true,c,true);
     Check(b.GetPixel(143,0).ToArgb()==top.ToArgb()&&b.GetPixel(144,0).ToArgb()==bg.ToArgb(),"top 72% fill");
     Check(b.GetPixel(69,24).ToArgb()==bottom.ToArgb()&&b.GetPixel(70,24).ToArgb()==bg.ToArgb(),"bottom 35% fill");
     Check(Look.Value(windows[0],true,c)=="28%"&&Look.Value(windows[1],true,c)=="65%","remaining numbers");
     c.ShowUsed=true;Check(Look.Value(windows[0],true,c)=="72%","used toggle");
    }
   });
   test("默认及仅数字模式无背景，自定义背景不污染历史列",()=>{
    var s=ColorExample();var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",Theme="dark",FocusId="example"};Color bg=Look.StripSurface(true,false);
    foreach(string mode in new[]{"default","numbers"}){
     c.ColorMode=mode;using(var b=StripBitmap(s,c,200,48))Check(b.GetPixel(0,0).ToArgb()==bg.ToArgb()&&b.GetPixel(0,24).ToArgb()==bg.ToArgb(),"no fill "+mode);
    }
    c.ColorMode="both";c.CpampEnabled=true;c.HistoryInBar=true;
    using(var b=StripBitmap(s,c,276,48))Check(b.GetPixel(275,0).ToArgb()==bg.ToArgb(),"history untouched");
   });
   test("未知、过期、读取失败数据不填充",()=>{
    var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",ColorMode="background"};Color bg=Look.StripSurface(true,false);
    using(var b=new Bitmap(100,10))using(var g=Graphics.FromImage(b)){
     foreach(var w in new[]{new QuotaWindow(),new QuotaWindow{Used=72,ResetAt=J.Now-1},new QuotaWindow{Used=Double.NaN},new QuotaWindow{Used=Double.PositiveInfinity}}){
      g.Clear(bg);QuotaColors.PaintProgress(g,new Rectangle(0,0,100,10),w,true,c,bg,true);Check(b.GetPixel(0,0).ToArgb()==bg.ToArgb(),"invalid no fill");
     }
     g.Clear(bg);QuotaColors.PaintProgress(g,new Rectangle(0,0,100,10),new QuotaWindow{Used=72},false,c,bg,true);Check(b.GetPixel(0,0).ToArgb()==bg.ToArgb(),"failed no fill");
    }
    Check(QuotaColors.FillWidth(200,0)==0&&QuotaColors.FillWidth(200,100)==200&&QuotaColors.FillWidth(200,-1)==0&&QuotaColors.FillWidth(200,101)==200,"clamp fill");
   });
   test("数字配色仅指定模式生效，默认保留原色",()=>{
    var c=new AppConfig();var w=new QuotaWindow{Used=72};Color bg=Look.StripSurface(true,false),normal=Color.Magenta;
    foreach(string mode in new[]{"default","background"}){c.ColorMode=mode;Check(QuotaColors.NumberColor(w,true,c,true,bg,normal)==normal,"preserve normal "+mode);}
    foreach(string mode in new[]{"numbers","both"}){c.ColorMode=mode;Check(QuotaColors.NumberColor(w,true,c,true,bg,normal)!=normal,"custom numbers "+mode);}
    Check(QuotaColors.NumberColor(w,false,c,true,bg,normal)==normal,"failure preserves fallback");
   });
   test("深浅主题及自定义颜色的数字对比度达标",()=>{
    foreach(bool dark in new[]{true,false})foreach(string hex in new[]{"#50D39A","#F1C553","#F17788","#000000","#FFFFFF","#808080"}){
     var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",ColorMode="both",LowUsageColor=hex,MediumUsageColor=hex,HighUsageColor=hex};var w=new QuotaWindow{Used=72};
     Color bg=Look.StripSurface(dark,false),fill=QuotaColors.ProgressSurface(bg,w,true,c,dark),number=QuotaColors.NumberColor(w,true,c,dark,bg,Look.Muted);
     Check(QuotaColors.Contrast(number,bg)>=4.5&&QuotaColors.Contrast(number,fill)>=4.5,"contrast "+dark+" "+hex);
    }
   });
   test("旧简称完全不参与绘制，无左侧徽标占位",()=>{
    var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",Theme="dark",FocusId="example"};var s=ColorExample();
    using(var a=StripBitmap(s,c,152,48)){
     c.Alias="CL";using(var b=StripBitmap(s,c,152,48))for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)Check(a.GetPixel(x,y)==b.GetPixel(x,y),"alias influences rendering");
    }
   });
   test("配色设置按模式展开、预览同步，并拒绝倒置阈值",()=>{
    var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",FocusId="example",CpaSecret=AppConfig.Protect("fixture-key")};var state=ColorExample();
    using(var service=new QuotaService(c))using(var form=new SettingsForm(c,service,state)){
     form.Opacity=0;form.Show();var tabs=form.Controls.OfType<TabControl>().Single();tabs.SelectedIndex=3;Application.DoEvents();
     var mode=(ColorModePicker)form.Controls.Find("quotaColorMode",true).Single();var options=form.Controls.Find("quotaColorOptions",true).Single();var preview=(StripPreview)form.Controls.Find("quotaColorPreview",true).Single();
     Check(mode.SelectedIndex==0&&!options.Visible,"default hidden");
     mode.SelectedIndex=1;Application.DoEvents();Check(options.Visible&&preview.Config.ColorMode=="background","expanded background");
     mode.SelectedIndex=2;Check(preview.Config.ColorMode=="numbers","numbers preview");
     var yellow=(NumericUpDown)form.Controls.Find("yellowUsedPercent",true).Single();var red=(NumericUpDown)form.Controls.Find("redUsedPercent",true).Single();
     yellow.Value=95;red.Value=90;bool rejected=false;
     try{typeof(SettingsForm).GetMethod("Draft",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{true});}catch(TargetInvocationException ex){rejected=ex.InnerException.Message.Contains("黄色起点");}
     Check(rejected,"invalid boundary rejected");yellow.Value=40;red.Value=80;
     var draft=(AppConfig)typeof(SettingsForm).GetMethod("Draft",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{true});
     Check(draft.ColorMode=="numbers"&&draft.YellowUsedPercent==40&&draft.RedUsedPercent==80,"UI draft");
     mode.SelectedIndex=0;Check(!options.Visible,"hide again");
    }
   });
   test("宽度及偏移按实际像素应用，兼容旧配置的系统缩放",()=>{
    foreach(int dpi in new[]{96,120,144,192}){
     Check(TaskbarMetrics.Width(120,dpi,true)==120,"physical width "+dpi);
     Check(TaskbarMetrics.Offset(24,dpi,true)==24,"physical offset "+dpi);
     Check(TaskbarMetrics.Width(120,dpi,false)==120*dpi/96,"legacy width "+dpi);
     Check(TaskbarMetrics.Offset(4,dpi,false)==4*dpi/96,"legacy offset "+dpi);
    }
    Check(TaskbarMetrics.Width(80,120,true)==80,"compact width");
   });
   test("外观设置兼容旧尺寸，键入宽度和偏移后提交为实际像素",()=>{
    var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",Width=120,Offset=4,FocusId="example",CpaSecret=AppConfig.Protect("fixture-key")};
    using(var service=new QuotaService(c))using(var form=new SettingsForm(c,service,ColorExample())){
     form.Opacity=0;form.Show();form.Controls.OfType<TabControl>().Single().SelectedIndex=2;Application.DoEvents();
     var width=(NumericUpDown)form.Controls.Find("taskbarWidth",true).Single();var offset=(NumericUpDown)form.Controls.Find("taskbarOffset",true).Single();
     Check(width.Value==TaskbarMetrics.PixelWidth(c)&&offset.Value==TaskbarMetrics.PixelOffset(c),"legacy display conversion");
     var method=typeof(SettingsForm).GetMethod("Draft",BindingFlags.Instance|BindingFlags.NonPublic);
     var unchanged=(AppConfig)method.Invoke(form,new object[]{true});
     Check(unchanged.PhysicalPixels&&TaskbarMetrics.PixelWidth(unchanged)==TaskbarMetrics.PixelWidth(c)&&TaskbarMetrics.PixelOffset(unchanged)==TaskbarMetrics.PixelOffset(c),"save without edit preserves position");
     width.Text="120";offset.Text="28";
     var next=(AppConfig)method.Invoke(form,new object[]{true});
     Check(next.PhysicalPixels&&next.Width==120&&next.Offset==28,"typed values committed");
     Check(TaskbarMetrics.PixelWidth(next)==120&&TaskbarMetrics.PixelOffset(next)==28,"no second scaling");
     var roundtrip=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<AppConfig>(J.Json(next));
     Check(roundtrip.PhysicalPixels&&roundtrip.Width==120&&roundtrip.Offset==28,"pixel units persisted");
    }
   });
   test("旧配置重置时间默认关闭，非法模式回退关闭",()=>{
    var c=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<AppConfig>("{\"Width\":120}");
    Check(c.ResetTimeMode=="off"&&!QuotaReset.Enabled(c),"legacy reset mode");
    foreach(string mode in new[]{"off","time","countdown"}){
     c.ResetTimeMode=mode;c.NormalizeAppearance();Check(c.ResetTimeMode==mode&&c.Clone().ResetTimeMode==mode,"mode roundtrip "+mode);
    }
    c.ResetTimeMode="invalid";c.NormalizeAppearance();Check(c.ResetTimeMode=="off","invalid fallback");
   });
   test("具体重置时间处理时区、跨天、周日期和跨年",()=>{
    long now=DateTimeOffset.Parse("2026-11-13T02:06:00Z").ToUnixTimeMilliseconds();
    var zone=TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
    Func<string,long> stamp=value=>DateTimeOffset.Parse(value).ToUnixTimeMilliseconds();
    Check(QuotaReset.Format(stamp("2026-11-13T10:30:00Z"),"time",now,zone)=="18:30","today");
    Check(QuotaReset.Format(stamp("2026-11-13T17:00:00Z"),"time",now,zone)=="明天 01:00","midnight");
    Check(QuotaReset.Format(stamp("2026-11-16T01:00:00Z"),"time",now,zone)=="周一 09:00","weekday");
    Check(QuotaReset.Format(stamp("2026-11-21T01:00:00Z"),"time",now,zone)=="11-21 09:00","date");
    Check(QuotaReset.Format(stamp("2027-01-01T01:00:00Z"),"time",now,zone)=="2027-01-01 09:00","year");
    Check(QuotaReset.Format(stamp("2026-11-13T17:00:00Z"),"time",now,TimeZoneInfo.Utc)=="17:00","zone conversion");
   });
   test("倒计时随本地时钟减少，两个额度窗口独立",()=>{
    long now=J.Now;var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",ResetTimeMode="countdown"};
    var five=new QuotaWindow{ResetAt=now+8100000};var week=new QuotaWindow{ResetAt=now+280800000};
    Check(QuotaReset.Text(five,true,c,now)=="2小时15分","5h countdown");
    Check(QuotaReset.Text(week,true,c,now)=="3天6小时","week countdown");
    var soon=new QuotaWindow{ResetAt=now+59000};
    Check(QuotaReset.Text(soon,true,c,now)=="59秒"&&QuotaReset.Text(soon,true,c,now+1000)=="58秒","local ticking");
    Check(QuotaReset.Text(new QuotaWindow{ResetAt=now+100},true,c,now)=="1秒","round up second");
   });
   test("未知和过期时间不会伪造，到期显示等待刷新",()=>{
    long now=J.Now;var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",ResetTimeMode="countdown"};
    Check(QuotaReset.Text(new QuotaWindow(),true,c,now)=="—","unknown");
    Check(QuotaReset.Text(new QuotaWindow{ResetAt=now+100000},false,c,now)=="—","stale");
    Check(QuotaReset.Text(new QuotaWindow{ResetAt=now},true,c,now)=="等待刷新","due");
    c.ResetTimeMode="time";Check(QuotaReset.Text(new QuotaWindow{ResetAt=now-1},true,c,now)=="等待刷新","past time");
    Check(QuotaReset.Text(new QuotaWindow{ResetAt=Int64.MaxValue},true,c,now)=="—","invalid epoch");
    c.ResetTimeMode="off";Check(QuotaReset.Text(null,false,c,now)=="","off");
   });
   test("重置时间设置和预览同步，不修改面板宽度或配色",()=>{
    var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",Width=300,PhysicalPixels=true,FocusId="example",ColorMode="background",CpaSecret=AppConfig.Protect("fixture-key")};
    using(var service=new QuotaService(c))using(var form=new SettingsForm(c,service,ColorExample())){
     form.Opacity=0;form.Show();form.Controls.OfType<TabControl>().Single().SelectedIndex=1;Application.DoEvents();
     var picker=(ResetModePicker)form.Controls.Find("quotaResetMode",true).Single();Check(picker.SelectedIndex==0,"default off");
     var method=typeof(SettingsForm).GetMethod("Draft",BindingFlags.Instance|BindingFlags.NonPublic);
     foreach(int index in new[]{1,2,0}){
      picker.SelectedIndex=index;var draft=(AppConfig)method.Invoke(form,new object[]{true});
      Check(draft.ResetTimeMode==new[]{"off","time","countdown"}[index]&&draft.Width==300&&draft.ColorMode=="background","draft "+index);
      var preview=(StripPreview)form.Controls.Find("quotaColorPreview",true).Single();Check(preview.Config.ResetTimeMode==draft.ResetTimeMode&&((StripPreview)form.Controls.Find("quotaResetPreview",true).Single()).Config.ResetTimeMode==draft.ResetTimeMode,"preview "+index);
     }
    }
   });
   test("重置时间绘制覆盖双行、单行和Google模型，关闭时保持原样",()=>{
    var state=ColorExample();var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",Width=300,PhysicalPixels=true,Theme="dark",FocusId="example"};
    using(var before=StripBitmap(state,c,300,50)){
     state.Quotas["example"].Windows[0].ResetAt=J.Now+300000;
     using(var stillOff=StripBitmap(state,c,300,50))Check(DifferentPixels(before,stillOff,new Rectangle(0,0,300,50))==0,"off unchanged");
     c.ResetTimeMode="countdown";using(var after=StripBitmap(state,c,300,50)){
      Check(DifferentPixels(before,after,new Rectangle(180,0,120,25))>20,"5h time drawn");
      Check(DifferentPixels(before,after,new Rectangle(180,25,120,25))>20,"weekly time drawn");
     }
    }
    c.SingleLine=true;using(var single=StripBitmap(state,c,520,50))Check(single.Width==520,"single");
    state.Accounts[0].Provider="antigravity";c.SingleLine=false;
    using(var model=StripBitmap(state,c,300,50))Check(model.Width==300,"model");
    foreach(int width in new[]{80,120,260})using(var narrow=StripBitmap(state,c,width,50))Check(narrow.Width==width,"narrow layout");
   });
   test("发布默认配置不包含服务地址或凭据，配置复制相互独立",()=>{
    var c=new AppConfig();Check(c.CpaUrl==""&&c.CpampUrl==""&&c.CpaSecret==""&&c.CpampSecret=="","empty connection defaults");
    c.SelectedIds.Add("first");var copy=c.Clone();copy.SelectedIds.Add("second");copy.Width=200;
    Check(c.SelectedIds.Count==1&&copy.SelectedIds.Count==2&&c.Width==152,"clone isolation");
   });
   test("未启用CPAMP时允许地址留空，启用后必须验证地址",()=>{
    var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="",CpaSecret=AppConfig.Protect("fixture-key"),FocusId="example"};
    using(var service=new QuotaService(c))using(var form=new SettingsForm(c,service,ColorExample())){
     var method=typeof(SettingsForm).GetMethod("Draft",BindingFlags.Instance|BindingFlags.NonPublic);
     var draft=(AppConfig)method.Invoke(form,new object[]{true});Check(!draft.CpampEnabled&&draft.CpampUrl=="","optional blank URL");
     var enabled=(CheckBox)typeof(SettingsForm).GetField("cpamp",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);enabled.Checked=true;
     bool rejected=false;try{method.Invoke(form,new object[]{true});}catch(TargetInvocationException){rejected=true;}Check(rejected,"enabled empty URL rejected");
    }
   });
   test("倒计时内容未变化时不触发重绘，变化后刷新",()=>{
    var c=new AppConfig{Theme="dark",ResetTimeMode="countdown",FocusId="example"};var state=ColorExample();
    state.Quotas["example"].Windows[0].ResetAt=J.Now+8130000;state.Quotas["example"].Windows[1].ResetAt=J.Now+282600000;
    using(var strip=new QuotaStrip(c)){
     var handle=strip.Handle;strip.RefreshState(state);int invalidations=0;strip.Invalidated+=(s,e)=>invalidations++;
     for(int i=0;i<10;i++)strip.RefreshClock();Check(invalidations==0,"stable clock should stay idle");
     state.Quotas["example"].Windows[0].ResetAt=J.Now+59000;strip.RefreshClock();Check(invalidations>0,"changed clock refreshes");
    }
   });
   File.WriteAllText(report,J.Json(new{passed=true,count=results.Count,tests=results,shell=Shell()}),new System.Text.UTF8Encoding(false));
  }
  static async Task Integration(){
   int quotaReads=0,historyReads=0,status=200;bool historyFail=false;
   var cfg=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",CpaSecret=AppConfig.Protect("fixture-key"),CpampSecret=AppConfig.Protect("fixture-admin")};
   using(var service=new QuotaService(cfg)){
    service.RequestOverride=(url,endpoint,key,body,ct)=>{
     object response;
     if(endpoint.EndsWith("auth-files"))response=Parse("{\"files\":[{\"name\":\"fixture.json\",\"provider\":\"codex\",\"auth_index\":\"index-1\",\"email\":\"fixture@example.test\"}]}");
     else if(endpoint.EndsWith("api-call")){
      quotaReads++;Check(J.S(J.Get(body,"header"),"Authorization")=="Bearer $TOKEN$","TOKEN literal");
      response=Parse("{\"status_code\":"+status+",\"body\":\"{\\\"rate_limit\\\":{\\\"primary_window\\\":{\\\"used_percent\\\":28,\\\"limit_window_seconds\\\":18000,\\\"reset_after_seconds\\\":3600}}}\"}");
     }else{
      historyReads++;string serialized=J.Json(body);Check(serialized.Contains("\"auth_indices\":[\"index-1\"]"),"history account scope");
      if(historyFail)throw new Exception("HTTP 503");
      response=Parse("{\"summary\":{\"total_tokens\":1250000,\"total_calls\":42}}");
     }
     return Task.FromResult(response);
    };
    await service.RefreshAsync(true);int cachedChanges=0;service.Changed+=()=>cachedChanges++;await service.RefreshAsync(false);Check(quotaReads==1&&historyReads==0&&cachedChanges==0,"cache avoids network and UI notifications");
    var a=service.State.Accounts[0];Check(service.State.Quotas[a.Id].Windows[0].Remaining==72,"quota fixture");
    cfg.CpampEnabled=true;cfg.FocusId=a.Id;service.Apply(cfg);await service.RefreshAsync(true);Check(historyReads==2&&service.State.History[a.Id].TodayTokens==1250000,"history enabled");
    status=401;await service.RefreshAsync(true);Check(service.State.Quotas[a.Id].Error!=""&&service.State.History[a.Id].TodayTokens==1250000,"quota failure isolation");
    status=200;historyFail=true;await service.RefreshAsync(true);Check(service.State.Quotas[a.Id].Error==""&&service.State.History[a.Id].Error!="","history failure isolation");
   }
  }
  public static Snapshot Example(){
   var a=new Account{Id="example",Provider="codex",Name="example.json",Label="示例账号 · 非真实用量"};
   var q=new QuotaState{UpdatedAt=J.Now,Plan="Plus",Windows=new List<QuotaWindow>{new QuotaWindow{Id="5h",Label="5小时",Used=28,ResetAt=J.Now+2*3600000},new QuotaWindow{Id="week",Label="一周",Used=54,ResetAt=J.Now+3*86400000}}};
   return new Snapshot{Accounts=new List<Account>{a},Quotas=new Dictionary<string,QuotaState>{{a.Id,q}},History=new Dictionary<string,HistoryState>{{a.Id,new HistoryState{TodayTokens=1250000,TodayCalls=142,MonthTokens=18640000,MonthCalls=2386,UpdatedAt=J.Now,TodayStart=QuotaParser.DayStart(J.Now,false)}}}};
  }
  static Snapshot ColorExample(){
   var state=Example();state.Quotas["example"].Windows[0].Used=72;state.Quotas["example"].Windows[1].Used=35;return state;
  }
  static Bitmap StripBitmap(Snapshot state,AppConfig config,int width,int height){
   var bitmap=new Bitmap(width,height);using(var g=Graphics.FromImage(bitmap))Look.Strip(g,new Rectangle(0,0,width,height),state,config);return bitmap;
  }
  static void RenderColorModes(string folder){
   var state=ColorExample();string[] modes={"default","background","numbers","both"},labels={"默认：保留当前样式","仅背景填充","仅百分比数字","背景填充 + 百分比数字"};
   using(var image=new Bitmap(700,380))using(var g=Graphics.FromImage(image))using(var heading=Look.Font(14,FontStyle.Bold))using(var text=Look.Font(10)){
    g.Clear(Color.FromArgb(246,248,252));
    Look.Text(g,"CPA 额度配色 · 示例数据",heading,Look.Ink,new Rectangle(20,10,660,35));
    Look.Text(g,"5小时剩余28%（已用72%） · 一周剩余65%（已用35%）",text,Look.Muted,new Rectangle(20,45,660,30));
    Look.Text(g,"深色任务栏",text,Look.Muted,new Rectangle(280,80,170,25));Look.Text(g,"浅色任务栏",text,Look.Muted,new Rectangle(490,80,170,25));
    for(int i=0;i<modes.Length;i++){
     int y=112+i*64;Look.Text(g,labels[i],text,Look.Ink,new Rectangle(20,y,250,48));
     foreach(bool dark in new[]{true,false}){
      var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",Theme=dark?"dark":"light",ColorMode=modes[i],FocusId="example"};
      using(var strip=StripBitmap(state,c,152,48))g.DrawImageUnscaled(strip,dark?280:490,y);
     }
    }
    image.Save(Path.Combine(folder,"color-modes-example.png"));
   }
  }
  static int DifferentPixels(Bitmap a,Bitmap b,Rectangle area){int count=0;for(int y=area.Top;y<area.Bottom;y++)for(int x=area.Left;x<area.Right;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))count++;return count;}
  static void RenderResetModes(string folder){
   var state=ColorExample();state.Quotas["example"].Windows[0].ResetAt=J.Now+8100000;state.Quotas["example"].Windows[1].ResetAt=J.Now+280800000;
   using(var image=new Bitmap(900,340))using(var g=Graphics.FromImage(image))using(var title=Look.Font(14,FontStyle.Bold))using(var text=Look.Font(10)){
    g.Clear(Color.FromArgb(246,248,252));Look.Text(g,"CPA 额度重置时间 · 示例数据",title,Look.Ink,new Rectangle(20,10,860,32));
    Look.Text(g,"每行独立显示下一次重置；倒计时在本地自动更新。",text,Look.Muted,new Rectangle(20,45,860,28));
    Look.Text(g,"深色任务栏",text,Look.Muted,new Rectangle(210,80,300,25));Look.Text(g,"浅色任务栏",text,Look.Muted,new Rectangle(560,80,300,25));
    string[] modes={"off","time","countdown"},labels={"不显示（默认）","具体时间","倒计时"};
    for(int i=0;i<modes.Length;i++){
     int y=116+i*68;Look.Text(g,labels[i],text,Look.Ink,new Rectangle(20,y,170,50));
     foreach(bool dark in new[]{true,false}){
      var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",Width=300,PhysicalPixels=true,Theme=dark?"dark":"light",ColorMode="background",FocusId="example",ResetTimeMode=modes[i]};
      using(var strip=StripBitmap(state,c,300,50))g.DrawImageUnscaled(strip,dark?210:560,y);
     }
    }
    image.Save(Path.Combine(folder,"reset-time-modes.png"));
   }
   var cfg=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",Width=300,PhysicalPixels=true,Theme="dark",FocusId="example",ResetTimeMode="countdown"};
   using(var service=new QuotaService(cfg))using(var settings=new SettingsForm(cfg,service,state)){
    settings.Opacity=0;settings.Show();settings.Controls.OfType<TabControl>().Single().SelectedIndex=1;Application.DoEvents();
    using(var image=new Bitmap(settings.Width,settings.Height)){settings.DrawToBitmap(image,new Rectangle(Point.Empty,settings.Size));image.Save(Path.Combine(folder,"settings-reset-time.png"));}
   }
  }
  public static void Render(string folder){
   Directory.CreateDirectory(folder);RenderColorModes(folder);RenderResetModes(folder);var c=new AppConfig{CpaUrl="https://cpa.example.test",CpampUrl="https://cpamp.example.test",Theme="dark",FocusId="example",SelectedIds=new List<string>{"example"},CpampEnabled=true};var state=Example();
   using(var image=new Bitmap(152,48)){using(var g=Graphics.FromImage(image))Look.Strip(g,new Rectangle(0,0,152,48),state,c);image.Save(Path.Combine(folder,"taskbar-example.png"));}
   using(var service=new QuotaService(c))using(var settings=new SettingsForm(c,service,state)){
    settings.Text="CPA状态栏设置 · 示例预览";settings.Opacity=0;settings.Show();System.Windows.Forms.Application.DoEvents();
    using(var image=new Bitmap(settings.Width,settings.Height)){settings.DrawToBitmap(image,new Rectangle(Point.Empty,settings.Size));image.Save(Path.Combine(folder,"settings-example.png"));}
    var tabs=settings.Controls.OfType<TabControl>().Single();tabs.SelectedIndex=2;Application.DoEvents();
    using(var image=new Bitmap(settings.Width,settings.Height)){settings.DrawToBitmap(image,new Rectangle(Point.Empty,settings.Size));image.Save(Path.Combine(folder,"settings-appearance.png"));}
    tabs.SelectedIndex=3;Application.DoEvents();
    using(var image=new Bitmap(settings.Width,settings.Height)){settings.DrawToBitmap(image,new Rectangle(Point.Empty,settings.Size));image.Save(Path.Combine(folder,"settings-colors-default.png"));}
    ((ColorModePicker)settings.Controls.Find("quotaColorMode",true).Single()).SelectedIndex=3;settings.UpdateAccounts(ColorExample());Application.DoEvents();
    using(var image=new Bitmap(settings.Width,settings.Height)){settings.DrawToBitmap(image,new Rectangle(Point.Empty,settings.Size));image.Save(Path.Combine(folder,"settings-colors-custom.png"));}   }
   using(var popup=new DetailsForm()){
    popup.UpdateData(state,c);popup.Opacity=0;popup.Show();System.Windows.Forms.Application.DoEvents();popup.PerformLayout();
    using(var image=new Bitmap(popup.Width,popup.Height)){popup.DrawToBitmap(image,new Rectangle(Point.Empty,popup.Size));image.Save(Path.Combine(folder,"details-example.png"));}
   }
  }
 }
}
