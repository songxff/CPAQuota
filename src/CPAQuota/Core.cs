using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CpaTaskbar {
 public static class J {
  public static object Get(object value,params string[] keys){foreach(string key in keys){var d=value as IDictionary<string,object>;object next;if(d==null){var raw=value as IDictionary;if(raw==null||!raw.Contains(key))return null;next=raw[key];}else if(!d.TryGetValue(key,out next))return null;value=next;}return value;}
  public static string S(object v){return v==null?"":Convert.ToString(v,CultureInfo.InvariantCulture);}
  public static string S(object v,string key){return S(Get(v,key));}
  public static double? N(object v){double n;if(v==null||v is bool||String.IsNullOrWhiteSpace(S(v)))return null;return Double.TryParse(S(v),NumberStyles.Any,CultureInfo.InvariantCulture,out n)&&!Double.IsInfinity(n)&&!Double.IsNaN(n)?(double?)n:null;}
  public static double? N(object v,string key){return N(Get(v,key));}
  public static object[] A(object v){var a=v as IEnumerable;if(a==null||v is string||v is IDictionary<string,object>)return new object[0];return a.Cast<object>().ToArray();}
  public static IDictionary<string,object> D(object v){return v as IDictionary<string,object>??new Dictionary<string,object>();}
  public static object Parse(string text){return new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.DeserializeObject(text.TrimStart('\uFEFF'));}
  public static string Json(object value){return new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Serialize(value);}
  public static long Now{get{return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();}}
  public static long? Epoch(object v){if(v==null)return null;double? n=N(v);if(n.HasValue)return n>0?(long?)(n<1e12?n*1000:n):null;DateTimeOffset dt;return DateTimeOffset.TryParse(S(v),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out dt)?(long?)dt.ToUnixTimeMilliseconds():null;}
  public static string Hash(string s){using(var h=SHA256.Create())return String.Concat(h.ComputeHash(Encoding.UTF8.GetBytes(s)).Take(8).Select(b=>b.ToString("x2")));}
  public static string Compact(double? n){if(!n.HasValue)return "—";double v=n.Value;return v>=1e9?(v/1e9).ToString("0.##",CultureInfo.InvariantCulture)+"B":v>=1e6?(v/1e6).ToString("0.##",CultureInfo.InvariantCulture)+"M":v>=1e3?(v/1e3).ToString("0.#",CultureInfo.InvariantCulture)+"K":v.ToString("0",CultureInfo.InvariantCulture);}
  public static string Countdown(long? at){if(!at.HasValue)return "未提供";long minutes=(long)Math.Ceiling((at.Value-Now)/60000.0);return minutes<=0?"等待刷新":minutes>=1440?(minutes/1440)+"天 "+(minutes%1440/60)+"小时":minutes>=60?(minutes/60)+"小时 "+(minutes%60)+"分钟":minutes+"分钟";}
  public static string Provider(string p){return p=="codex"?"Codex":p=="claude"?"Claude":p=="antigravity"?"Antigravity":p=="gemini"?"Gemini CLI":p;}
 }
 public class AppConfig {
  public string CpaUrl="", CpaSecret="";
  public bool CpampEnabled=false;
  public string CpampUrl="",CpampSecret="";
  public List<string> SelectedIds=new List<string>();
  public string FocusId="",Model="",Alias="",Theme="auto";
  public bool ShowUsed=false,SingleLine=false,HistoryInBar=false,Rotate=false,Autostart=false;
  public int Width=152,Offset=4,FontSize=9,RefreshSeconds=120,HistorySeconds=300,RotateSeconds=12;
  public bool PhysicalPixels=false;
  public string ColorMode="default";
  public string ResetTimeMode="off";
  public int YellowUsedPercent=50,RedUsedPercent=90;
  public string LowUsageColor="#50D39A",MediumUsageColor="#F1C553",HighUsageColor="#F17788";
  public void NormalizeAppearance(){
   if(!new[]{"off","time","countdown"}.Contains(ResetTimeMode))ResetTimeMode="off";
   if(!new[]{"default","background","numbers","both"}.Contains(ColorMode))ColorMode="default";
   if(YellowUsedPercent<1||YellowUsedPercent>=RedUsedPercent||RedUsedPercent>100){YellowUsedPercent=50;RedUsedPercent=90;}
   if(!IsRgb(LowUsageColor))LowUsageColor="#50D39A";
   if(!IsRgb(MediumUsageColor))MediumUsageColor="#F1C553";
   if(!IsRgb(HighUsageColor))HighUsageColor="#F17788";
  }
  static bool IsRgb(string value){return value!=null&&value.Length==7&&value[0]=='#'&&value.Substring(1).All(ch=>"0123456789ABCDEFabcdef".IndexOf(ch)>=0);}
  public static string Folder=AppDomain.CurrentDomain.BaseDirectory;
  public static string ResolveFolder(string executableFolder,string localDataFolder){
   string folder=Path.GetFullPath(executableFolder);
   if(folder.Length>Path.GetPathRoot(folder).Length)folder=folder.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
   return File.Exists(Path.Combine(folder,"portable.flag"))||File.Exists(Path.Combine(folder,"data","desktop-settings.json"))?folder:Path.Combine(localDataFolder,"CPAQuota");
  }
  public static void InitializeStorage(){Folder=ResolveFolder(AppDomain.CurrentDomain.BaseDirectory,Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));}
  public static string FileName{get{return Path.Combine(Folder,"data","desktop-settings.json");}}
  public static string Protect(string value){return String.IsNullOrEmpty(value)?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser));}
  public static string Unprotect(string value){return String.IsNullOrEmpty(value)?"":Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value),null,DataProtectionScope.CurrentUser));}
  public static AppConfig Load(){
   if(File.Exists(FileName)){try{var c=new JavaScriptSerializer().Deserialize<AppConfig>(File.ReadAllText(FileName,Encoding.UTF8));if(c!=null){c.SelectedIds=c.SelectedIds??new List<string>();c.NormalizeAppearance();return c;}}catch{throw new Exception("独立版配置无法读取，请检查data/desktop-settings.json。");}}
   return new AppConfig();
  }

  public AppConfig Clone(){var copy=(AppConfig)MemberwiseClone();copy.SelectedIds=SelectedIds==null?new List<string>():SelectedIds.ToList();return copy;}
  public static string Url(string value){
   Uri u;if(!Uri.TryCreate(value.Trim(),UriKind.Absolute,out u)||(u.Scheme!="http"&&u.Scheme!="https")||u.UserInfo!=""||u.Query!=""||u.Fragment!="")throw new Exception("服务地址须为HTTP/HTTPS地址，不能包含密钥或查询参数。");
   return u.GetLeftPart(UriPartial.Path).TrimEnd('/').Replace("/management.html","").Replace("/v0/management","");
  }
  public void Save(){
   NormalizeAppearance();
   string dir=Path.GetDirectoryName(FileName);Directory.CreateDirectory(dir);string temp=FileName+"."+Guid.NewGuid().ToString("N")+".tmp";
   File.WriteAllText(temp,J.Json(this),new UTF8Encoding(false));
   try{if(File.Exists(FileName))File.Replace(temp,FileName,null);else File.Move(temp,FileName);}finally{if(File.Exists(temp))File.Delete(temp);}
  }
  public void SetAutostart(){
   using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){
    if(Autostart)key.SetValue("CPAQuotaTaskbar","\""+System.Windows.Forms.Application.ExecutablePath+"\" --background");
    else key.DeleteValue("CPAQuotaTaskbar",false);
   }
  }
 }
 public class Account {
  public string Id,Name,Provider,Label,AuthIndex,AccountId,ProjectId;
  public bool Disabled;
  public bool Supported{get{return new[]{"codex","claude","antigravity","gemini"}.Contains(Provider);}}
  public override string ToString(){return J.Provider(Provider)+"  ·  "+Label;}
 }
 public class QuotaWindow {
  public string Id,Label,Scope="";
  public double? Used;
  public long? ResetAt;
  public double? Duration;
  public double? Remaining{get{return Used.HasValue?(double?)(100-Used.Value):null;}}
  public bool Valid{get{return Used.HasValue&&(!ResetAt.HasValue||ResetAt>J.Now);}}
 }
 public class QuotaState {
  public List<QuotaWindow> Windows=new List<QuotaWindow>();
  public string Plan="",Error="";
  public long UpdatedAt,AttemptedAt;
  public bool RateLimited;
 }
 public class HistoryState {
  public double? TodayTokens,TodayCalls,MonthTokens,MonthCalls;
  public long UpdatedAt,AttemptedAt,TodayStart;
  public string Error="";
 }
 public class Snapshot {
  public List<Account> Accounts=new List<Account>();
  public Dictionary<string,QuotaState> Quotas=new Dictionary<string,QuotaState>();
  public Dictionary<string,HistoryState> History=new Dictionary<string,HistoryState>();
  public string Error="";
  public bool Busy;
 }
 public static class QuotaParser {
  static readonly TimeZoneInfo ChinaZone=TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
  public static List<Account> Accounts(object payload){
   if(!(J.Get(payload,"files") is IEnumerable))throw new Exception("CPA账号列表格式不兼容。");
   var result=new List<Account>();
   foreach(object x in J.A(J.Get(payload,"files"))){
    object index=J.Get(x,"auth_index")??J.Get(x,"authIndex");if(index==null)continue;
    string provider=J.S(x,"provider");if(provider=="")provider=J.S(x,"type");provider=provider.ToLowerInvariant();if(provider=="gemini-cli"||provider=="gemini_cli")provider="gemini";
    string name=J.S(x,"name");if(name=="")name=J.S(index);
    object token=J.Get(x,"id_token");if(token is string){try{token=J.Parse((string)token);}catch{token=null;}}
    string accountId=J.S(token,"chatgpt_account_id");if(accountId=="")accountId=J.S(x,"account_id");
    string project=J.S(x,"project_id");if(project=="")project=J.S(J.Get(x,"metadata"),"project_id");if(project=="")project=J.S(J.Get(x,"attributes"),"project_id");
    result.Add(new Account{Id=J.Hash(provider+":"+name),Name=name,Provider=provider,AuthIndex=J.S(index),Label=J.S(x,"email")!=""?J.S(x,"email"):J.S(x,"label")!=""?J.S(x,"label"):name,AccountId=accountId,ProjectId=project,Disabled=J.Get(x,"disabled") is bool&&(bool)J.Get(x,"disabled")});
   }
   return result;
  }
  static QuotaWindow Window(string id,string label,double? used,object reset,double? duration=null,string scope=""){
   return new QuotaWindow{Id=id,Label=label,Used=used.HasValue?Math.Max(0,Math.Min(100,used.Value)):(double?)null,ResetAt=J.Epoch(reset),Duration=duration,Scope=scope};
  }
  public static List<QuotaWindow> Codex(object payload){
   var result=new List<QuotaWindow>();
   Action<object,string,string> add=(rate,prefix,scope)=>{
    foreach(string kind in new[]{"primary","secondary"}){
     object w=J.Get(rate,kind+"_window")??J.Get(rate,kind+"Window");if(w==null)continue;
     double? seconds=J.N(J.Get(w,"limit_window_seconds")??J.Get(w,"limitWindowSeconds"));
     string id=seconds==18000?"5h":seconds==604800?"week":seconds>=2419200&&seconds<=2678400?"month":kind;
     string label=id=="5h"?"5小时":id=="week"?"一周":id=="month"?"月额度":seconds.HasValue?(seconds.Value/3600).ToString("0.#")+"小时":"周期未知";
     object reset=J.Get(w,"reset_at")??J.Get(w,"resetAt");double? after=J.N(J.Get(w,"reset_after_seconds")??J.Get(w,"resetAfterSeconds"));if(reset==null&&after.HasValue)reset=J.Now+after*1000;
     result.Add(Window(prefix+id,label,J.N(J.Get(w,"used_percent")??J.Get(w,"usedPercent")),reset,seconds,scope));
    }
   };
   add(J.Get(payload,"rate_limit")??J.Get(payload,"rateLimit"),"","");
   add(J.Get(payload,"code_review_rate_limit")??J.Get(payload,"codeReviewRateLimit"),"review-","代码审查");
   int n=0;foreach(object item in J.A(J.Get(payload,"additional_rate_limits")??J.Get(payload,"additionalRateLimits"))){string label=J.S(item,"limit_name");if(label=="")label=J.S(item,"metered_feature");if(label=="")label="额外额度"+(++n);add(J.Get(item,"rate_limit")??J.Get(item,"rateLimit"),"extra-"+J.Hash(label)+"-",label);}
   return result;
  }
  public static List<QuotaWindow> Claude(object payload){
   var result=new List<QuotaWindow>();
   foreach(var pair in J.D(payload)){
    if(pair.Key=="extra_usage"||!J.D(pair.Value).ContainsKey("utilization"))continue;
    string id=pair.Key=="five_hour"?"5h":pair.Key=="seven_day"?"week":pair.Key;
    string label=id=="5h"?"5小时":id=="week"?"一周":pair.Key.Replace("seven_day_","一周 · ").Replace('_',' ');
    result.Add(Window(id,label,J.N(pair.Value,"utilization"),J.Get(pair.Value,"resets_at"),id=="5h"?18000:id=="week"?604800:(double?)null));
   }
   return result;
  }
  public static List<QuotaWindow> Google(object payload,bool geminiCli=false){
   var result=new List<QuotaWindow>();
   foreach(object g in J.A(J.Get(payload,"groups"))){
    string name=J.S(g,"displayName");if(name=="")name=J.S(g,"display_name");
    int i=0;foreach(object b in J.A(J.Get(g,"buckets"))){
     string label=J.S(b,"displayName");if(label=="")label=J.S(b,"window");if(label=="")label="模型额度"+(++i);
     double? f=J.N(J.Get(b,"remainingFraction")??J.Get(b,"remaining_fraction"));
     string id=J.S(b,"bucketId");if(id=="")id=J.S(b,"bucket_id");if(id=="")id=label;
     result.Add(Window("ag-"+J.Hash(name+":"+id),label,f.HasValue?100-f*100:null,J.Get(b,"resetTime")??J.Get(b,"reset_time"),null,name));
    }
   }
   if(result.Count>0)return result;
   foreach(var model in J.D(J.Get(payload,"models"))){
    object q=J.Get(model.Value,"quotaInfo")??J.Get(model.Value,"quota_info");if(q==null)continue;
    double? f=J.N(J.Get(q,"remainingFraction")??J.Get(q,"remaining_fraction")??J.Get(q,"remaining"));
    string label=J.S(model.Value,"displayName");if(label=="")label=J.S(model.Value,"display_name");if(label=="")label=model.Key;
    result.Add(Window("model-"+J.Hash(model.Key),label,f.HasValue?100-f*100:null,J.Get(q,"resetTime")??J.Get(q,"reset_time"),null,model.Key));
   }
   if(geminiCli){int i=0;foreach(object b in J.A(J.Get(payload,"buckets"))){string name=J.S(b,"modelId");if(name=="")name=J.S(b,"model_id");double? f=J.N(J.Get(b,"remainingFraction")??J.Get(b,"remaining_fraction"));result.Add(Window("gemini-"+J.Hash(name+":"+J.S(b,"tokenType")+":"+(i++)),name,f.HasValue?100-f*100:null,J.Get(b,"resetTime")??J.Get(b,"reset_time"),null,name));}}
   return result;
  }
  public static object Unwrap(object envelope){
   int status=(int)(J.N(J.Get(envelope,"status_code")??J.Get(envelope,"statusCode"))??0);
   if(status<200||status>=300)throw new ProviderException(status==401?"账号授权失效，请在CPA重新授权":status==429?"供应商查询限流，稍后自动重试":"供应商查询失败（HTTP "+status+"）",status);
   object body=J.Get(envelope,"body");if(body is string){try{body=J.Parse((string)body);}catch{throw new Exception("供应商返回的JSON无法解析。");}}
   if(!(body is IDictionary<string,object>))throw new Exception("供应商返回格式不兼容。");return body;
  }
  public static long DayStart(long at,bool month){
   var zone=ChinaZone;
   var local=TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(at),zone);
   var start=new DateTime(local.Year,local.Month,month?1:local.Day,0,0,0,DateTimeKind.Unspecified);
   return new DateTimeOffset(start,zone.GetUtcOffset(start)).ToUnixTimeMilliseconds();
  }
  public static void History(object payload,HistoryState h,bool month){
   object s=J.Get(payload,"summary");double? tokens=J.N(s,"total_tokens"),calls=J.N(s,"total_calls");if(!tokens.HasValue||!calls.HasValue)throw new Exception("CPAMP统计格式不兼容。");
   if(month){h.MonthTokens=tokens;h.MonthCalls=calls;}else{h.TodayTokens=tokens;h.TodayCalls=calls;}
  }
 }
 public class ProviderException:Exception{public int Status;public ProviderException(string message,int status):base(message){Status=status;}}
}
