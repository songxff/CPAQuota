using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CpaTaskbar {
 public static class ProgressStyleTests {
  static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
  static Snapshot Example(){
   var a=new Account{Id="style-example",Provider="codex",Label="示例账号"};
   return new Snapshot{Accounts=new List<Account>{a},Quotas=new Dictionary<string,QuotaState>{{a.Id,new QuotaState{UpdatedAt=J.Now,Windows=new List<QuotaWindow>{new QuotaWindow{Id="5h",Label="5小时",Used=72,ResetAt=J.Now+8100000},new QuotaWindow{Id="week",Label="一周",Used=35,ResetAt=J.Now+280800000}}}}}};
  }
  static AppConfig Config(){return new AppConfig{CpaUrl="https://cpa.example.test",CpaSecret=AppConfig.Protect("fixture-key"),FocusId="style-example",SelectedIds=new List<string>{"style-example"},ColorMode="background",Theme="dark",Width=300,PhysicalPixels=true,ResetTimeMode="countdown"};}
  public static void Run(Action<string,Action> test){
   test("粗条沿用e2ee03a字体，细条沿用18336bd字体，切换后缓存不串用",()=>{
    foreach(int height in new[]{32,40,50,60,96})foreach(int size in new[]{8,9,12})foreach(string style in new[]{"thick","thin"}){
     var c=Config();c.FontSize=size;c.ProgressStyle=style;
     using(var image=new Bitmap(420,height))using(var g=Graphics.FromImage(image))Look.Strip(g,new Rectangle(0,0,image.Width,height),Example(),c);
     bool thick=style=="thick";float scale=Math.Max(.8f,height/(thick?40f:48f))*size/9f;
     foreach(string name in new[]{"stripLabel","stripNumber","stripTime"}){
      var font=(Font)typeof(Look).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
      bool number=name=="stripNumber",time=name=="stripTime";float expected=number?Math.Max(11,13*scale):thick?Math.Max(10,11*scale):time?Math.Max(9,9.5f*scale):Math.Max(10,10.5f*scale);
      Check(Math.Abs(font.Size-expected)<.001f,"legacy font size "+height+" "+size+" "+style+" "+name);
      Check(font.Name==(number||thick?"Segoe UI":"Microsoft YaHei UI")&&font.Unit==GraphicsUnit.Pixel&&font.Style==(number||!thick?FontStyle.Bold:FontStyle.Regular),"legacy typeface "+name);
     }
    }
   });
   test("粗进度条逐像素复用原版整行填充，颜色和已用比例保持一致",()=>{
    foreach(bool dark in new[]{false,true})foreach(bool showUsed in new[]{false,true})foreach(int used in new[]{0,35,72,100}){
     var c=Config();c.ShowUsed=showUsed;var surface=Look.StripSurface(dark,false);var area=new Rectangle(4,3,200,23);var w=new QuotaWindow{Used=used};
     using(var actual=new Bitmap(208,30))using(var expected=new Bitmap(208,30)){
      using(var g=Graphics.FromImage(actual)){g.Clear(surface);QuotaColors.PaintProgress(g,area,w,true,c,surface,dark);}
      using(var g=Graphics.FromImage(expected)){
       g.Clear(surface);int width=(int)Math.Round(area.Width*used/100.0,MidpointRounding.AwayFromZero);
       var originalFill=QuotaColors.Blend(surface,QuotaColors.UsageColor(used,c),dark?.26:.22);
       if(width>0)using(var brush=new SolidBrush(originalFill))g.FillRectangle(brush,area.X,area.Y,width,area.Height);
      }
      for(int y=0;y<actual.Height;y++)for(int x=0;x<actual.Width;x++)Check(actual.GetPixel(x,y)==expected.GetPixel(x,y),"legacy mismatch "+dark+" "+showUsed+" "+used);
     }
    }
   });
   test("样式设置默认恢复粗条，切换同步预览且持久化后保留其他外观",()=>{
    string previousFolder=AppConfig.Folder,temp=Path.Combine(Path.GetTempPath(),"CPAQuota-style-"+Guid.NewGuid().ToString("N"));
    try{
     AppConfig.Folder=temp;Directory.CreateDirectory(Path.GetDirectoryName(AppConfig.FileName));
     File.WriteAllText(AppConfig.FileName,"{\"ColorMode\":\"background\",\"Width\":300}");
     Check(AppConfig.Load().ProgressStyle=="thick","old settings restore thick");
     var c=Config();c.TransparentBackground=true;
     using(var service=new QuotaService(c))using(var form=new SettingsForm(c,service,Example())){
      var picker=(ComboBox)form.Controls.Find("quotaProgressStyle",true).Single();Check(picker.SelectedIndex==0,"thick selected");
      Check(picker.Items.Cast<string>().SequenceEqual(new[]{"粗进度条","细进度条"}),"style labels");
      var draftMethod=typeof(SettingsForm).GetMethod("Draft",BindingFlags.Instance|BindingFlags.NonPublic);
      foreach(int index in new[]{1,0}){
       picker.SelectedIndex=index;var draft=(AppConfig)draftMethod.Invoke(form,new object[]{true});
       Check(draft.ProgressStyle==(index==0?"thick":"thin"),"selected style");
       Check(draft.ColorMode==c.ColorMode&&draft.TransparentBackground==c.TransparentBackground&&draft.Width==c.Width&&draft.ShowUsed==c.ShowUsed&&draft.ResetTimeMode==c.ResetTimeMode,"other appearance preserved");
       foreach(string name in new[]{"quotaColorPreview","quotaResetPreview"})Check(((StripPreview)form.Controls.Find(name,true).Single()).Config.ProgressStyle==draft.ProgressStyle,"preview "+name);
       draft.Save();Check(AppConfig.Load().ProgressStyle==draft.ProgressStyle,"persist style");
      }
      Check(c.ProgressStyle=="thick","preview must not mutate applied settings");
     }
     var invalid=Config();invalid.ProgressStyle="invalid";invalid.NormalizeAppearance();Check(invalid.ProgressStyle=="thick","invalid fallback");
    }finally{AppConfig.Folder=previousFolder;File.Delete(Path.Combine(temp,"data","desktop-settings.json"));Directory.Delete(Path.Combine(temp,"data"));Directory.Delete(temp);}
   });
   test("粗细样式适配单双行和透明背景，保存后的重绘立即生效",()=>{
    var state=Example();var c=Config();
    foreach(bool single in new[]{false,true})foreach(bool transparent in new[]{false,true}){
     c.SingleLine=single;c.TransparentBackground=transparent;
     using(var thick=new Bitmap(300,50))using(var thin=new Bitmap(300,50)){
      c.ProgressStyle="thick";using(var g=Graphics.FromImage(thick))Look.Strip(g,new Rectangle(0,0,300,50),state,c);
      c.ProgressStyle="thin";using(var g=Graphics.FromImage(thin))Look.Strip(g,new Rectangle(0,0,300,50),state,c);
      Check(thick.GetPixel(1,1)!=thin.GetPixel(1,1),"row background restored "+single+" "+transparent);
     }
     using(var strip=new QuotaStrip(c)){
      var handle=strip.Handle;strip.RefreshState(state);int count=strip.PresentationCount,invalidations=0;strip.Invalidated+=(s,e)=>invalidations++;
      c=c.Clone();c.ProgressStyle="thick";strip.Config=c;strip.RefreshState(state);
      Check(transparent?strip.PresentationCount>count:invalidations>0,"applied style repaint "+transparent);
     }
    }
   });
  }
  public static void Render(string folder){
   var state=Example();
   using(var image=new Bitmap(760,220))using(var g=Graphics.FromImage(image))using(var font=Look.Font(10)){
    g.Clear(Color.White);Look.Text(g,"粗进度条 / 细进度条 · 示例数据",font,Look.Ink,new Rectangle(20,10,720,30));
    foreach(bool thin in new[]{false,true}){
     int y=thin?128:58;Look.Text(g,thin?"细进度条":"粗进度条",font,Look.Ink,new Rectangle(20,y,100,50));
     foreach(bool dark in new[]{true,false}){
      var c=Config();c.ProgressStyle=thin?"thin":"thick";c.Theme=dark?"dark":"light";c.TransparentBackground=false;
      using(var strip=new Bitmap(300,50)){using(var sg=Graphics.FromImage(strip))Look.Strip(sg,new Rectangle(0,0,300,50),state,c);g.DrawImageUnscaled(strip,dark?130:450,y);}
     }
    }
    image.Save(Path.Combine(folder,"progress-styles.png"));
   }
   var config=Config();using(var service=new QuotaService(config))using(var form=new SettingsForm(config,service,state)){
    form.Opacity=0;form.Show();form.Controls.OfType<TabControl>().Single().SelectedIndex=2;Application.DoEvents();
    using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(folder,"settings-progress-style.png"));}
   }
  }
 }
}
