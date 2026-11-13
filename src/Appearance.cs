using System;
using System.Drawing;

namespace CpaTaskbar {
 public static class QuotaColors {
  public static bool HasBackground(AppConfig c){return c.ColorMode=="background"||c.ColorMode=="both";}
  public static bool HasNumbers(AppConfig c){return c.ColorMode=="numbers"||c.ColorMode=="both";}
  public static bool Valid(QuotaWindow w,bool fresh){return fresh&&w!=null&&w.Valid&&!Double.IsNaN(w.Used.Value)&&!Double.IsInfinity(w.Used.Value);}
  public static Color UsageColor(double used,AppConfig c){
   string hex=used>=c.RedUsedPercent?c.HighUsageColor:used>=c.YellowUsedPercent?c.MediumUsageColor:c.LowUsageColor;
   return ColorTranslator.FromHtml(hex);
  }
  public static Color Blend(Color surface,Color color,double amount){return Color.FromArgb((int)Math.Round(surface.R*(1-amount)+color.R*amount),(int)Math.Round(surface.G*(1-amount)+color.G*amount),(int)Math.Round(surface.B*(1-amount)+color.B*amount));}
  static double Linear(byte c){double v=c/255.0;return v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4);}
  public static double Luminance(Color c){return .2126*Linear(c.R)+.7152*Linear(c.G)+.0722*Linear(c.B);}
  public static double Contrast(Color a,Color b){double x=Luminance(a),y=Luminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
  public static Color Readable(Color color,Color surface,Color filled,bool dark){
   Color result=color,target=dark?Color.White:Color.Black;
   for(int i=0;i<24&&(Contrast(result,surface)<4.5||Contrast(result,filled)<4.5);i++)result=Blend(result,target,.15);
   return result;
  }
  public static Color ProgressSurface(Color surface,QuotaWindow w,bool fresh,AppConfig c,bool dark){
   return HasBackground(c)&&Valid(w,fresh)?Blend(surface,UsageColor(w.Used.Value,c),dark?.26:.22):surface;
  }
  public static int FillWidth(int width,double used){if(width<=0||Double.IsNaN(used)||Double.IsInfinity(used))return 0;return (int)Math.Round(width*Math.Max(0,Math.Min(100,used))/100,MidpointRounding.AwayFromZero);}
  public static void PaintProgress(Graphics g,Rectangle area,QuotaWindow w,bool fresh,AppConfig c,Color surface,bool dark){
   if(!HasBackground(c)||!Valid(w,fresh))return;
   int width=FillWidth(area.Width,w.Used.Value);if(width==0)return;
   using(var brush=new SolidBrush(ProgressSurface(surface,w,fresh,c,dark)))g.FillRectangle(brush,area.X,area.Y,width,area.Height);
  }
  public static Color NumberColor(QuotaWindow w,bool fresh,AppConfig c,bool dark,Color surface,Color defaultColor){
   if(!HasNumbers(c)||!Valid(w,fresh))return defaultColor;
   return Readable(UsageColor(w.Used.Value,c),surface,ProgressSurface(surface,w,fresh,c,dark),dark);
  }
 }
}
