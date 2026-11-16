using System;
using System.Globalization;

namespace CpaTaskbar {
 public static class QuotaReset {
  public static bool Enabled(AppConfig c){return c.ResetTimeMode=="time"||c.ResetTimeMode=="countdown";}
  public static string Text(QuotaWindow w,bool fresh,AppConfig c,long now){
   if(!Enabled(c))return "";
   if(!fresh||w==null||!w.ResetAt.HasValue)return "—";
   return (w is SummaryWindow?"近":"")+Format(w.ResetAt.Value,c.ResetTimeMode,now,TimeZoneInfo.Local);
  }
  public static string Format(long at,string mode,long now,TimeZoneInfo zone){
   DateTimeOffset target,current;
   try{target=TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(at),zone);current=TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(now),zone);}catch(ArgumentException){return "—";}
   if(at<=now)return "等待刷新";
   if(mode=="countdown"){
    long seconds=(at-now+999)/1000;
    if(seconds>=86400){long days=seconds/86400,hours=seconds%86400/3600;return days+"天"+(hours>0?hours+"小时":"");}
    if(seconds>=3600){long minutes=(seconds+59)/60;return minutes/60+"小时"+(minutes%60>0?minutes%60+"分":"");}
    if(seconds>=60)return ((seconds+59)/60)+"分";
    return seconds+"秒";
   }
   if(mode!="time")return "";
   int dates=(target.Date-current.Date).Days;
   string clock=target.ToString("HH:mm",CultureInfo.InvariantCulture);
   if(dates==0)return clock;
   if(dates==1)return "明天 "+clock;
   if(dates<7)return new[]{"周日","周一","周二","周三","周四","周五","周六"}[(int)target.DayOfWeek]+" "+clock;
   return target.ToString(target.Year==current.Year?"MM-dd HH:mm":"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture);
  }
  public static string Description(QuotaWindow w,bool fresh,long now){
   if(!fresh||w==null||!w.ResetAt.HasValue)return "未提供或数据已过期";
   string full;
   try{full=DateTimeOffset.FromUnixTimeMilliseconds(w.ResetAt.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");}catch(ArgumentException){return "未提供";}
   return w.ResetAt.Value<=now?"等待刷新":full+"（"+Format(w.ResetAt.Value,"countdown",now,TimeZoneInfo.Local)+"后）";
  }
 }
}
