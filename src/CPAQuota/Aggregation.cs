using System;
using System.Collections.Generic;
using System.Linq;

namespace CpaTaskbar {
 public sealed class SummaryWindow:QuotaWindow {
  public int Count,Total,KnownResets;
 }
 public sealed class QuotaSummary {
  public string Provider="";
  public List<Account> Accounts=new List<Account>();
  public QuotaState Quota=new QuotaState();
  public string Key{get{return Provider+"|"+Accounts.Count+"|"+String.Join(";",Quota.Windows.Cast<SummaryWindow>().Select(w=>w.Id+"/"+w.Scope+"/"+w.Used+"/"+w.Count+"/"+w.Total+"/"+w.ResetAt));}}
  public string Title(AppConfig config){return J.Provider(Provider)+" · "+(config.QuotaDisplayMode=="sum"?"累加":"平均")+" · "+Accounts.Count+"个账号";}
 }
 public static class QuotaAggregation {
  public static bool Enabled(AppConfig config){return config.QuotaDisplayMode=="average"||config.QuotaDisplayMode=="sum";}
  public static QuotaSummary Build(Snapshot state,AppConfig config){
   var result=new QuotaSummary();
   var selected=state.Accounts.Where(a=>a.Supported&&!a.Disabled&&config.SelectedIds.Contains(a.Id)).ToList();
   var focus=Look.Focus(state,config);result.Provider=focus==null?"":focus.Provider;
   if(!selected.Any(a=>a.Provider==result.Provider)&&selected.Count>0)result.Provider=selected[0].Provider;
   result.Accounts=selected.Where(a=>a.Provider==result.Provider).GroupBy(a=>String.IsNullOrEmpty(a.AccountId)?"file:"+a.Id:"account:"+a.AccountId,StringComparer.Ordinal).Select(g=>g.OrderByDescending(a=>Look.Fresh(Look.Quota(state,a),config)).ThenByDescending(a=>Look.Quota(state,a)==null?0:Look.Quota(state,a).UpdatedAt).First()).ToList();
   var windows=result.Accounts.SelectMany(a=>{
    var quota=Look.Quota(state,a);
    return (quota==null?new List<QuotaWindow>():quota.Windows).Select(w=>new{Account=a,Quota=quota,Window=w});
   });
   foreach(var group in windows.GroupBy(x=>new{x.Window.Id,x.Window.Scope,x.Window.Duration})){
    var first=group.First().Window;
    var valid=group.Where(x=>Look.Fresh(x.Quota,config)&&x.Window.Valid&&!Double.IsNaN(x.Window.Used.Value)&&!Double.IsInfinity(x.Window.Used.Value)).GroupBy(x=>x.Account.Id).Select(g=>g.First()).ToList();
    var resets=valid.Where(x=>x.Window.ResetAt.HasValue).Select(x=>x.Window.ResetAt.Value).ToList();
    result.Quota.Windows.Add(new SummaryWindow{Id=first.Id,Label=first.Label,Scope=first.Scope,Duration=first.Duration,Used=valid.Count==0?(double?)null:valid.Average(x=>x.Window.Used.Value),ResetAt=resets.Count==0?(long?)null:resets.Min(),Count=valid.Count,Total=result.Accounts.Count,KnownResets=resets.Count});
   }
   result.Quota.UpdatedAt=result.Accounts.Select(a=>Look.Quota(state,a)).Where(q=>Look.Fresh(q,config)).Select(q=>q.UpdatedAt).DefaultIfEmpty(0).Min();
   if(result.Provider=="codex"||result.Provider=="claude")foreach(string id in new[]{"5h","week"})if(!result.Quota.Windows.Any(w=>w.Id==id))result.Quota.Windows.Add(new SummaryWindow{Id=id,Label=id=="5h"?"5小时":"一周",Count=0,Total=result.Accounts.Count});
   return result;
  }
  public static QuotaState BarQuota(Snapshot state,AppConfig config){return Enabled(config)?Build(state,config).Quota:Look.Quota(state,Look.Focus(state,config));}
  public static string Caption(QuotaWindow window,string caption,AppConfig config){
   var summary=window as SummaryWindow;
   return summary==null?caption:(config.QuotaDisplayMode=="sum"?"合":"均")+caption+"["+summary.Count+"/"+summary.Total+"]";
  }
  public static string Description(Snapshot state,AppConfig config){
   var summary=Build(state,config);
   string text=summary.Title(config)+"\n仅合并同渠道、同周期；进度条跟随显示的剩余或已用比例，累加超过100%时封顶。\n";
   text+=String.Join("\n",summary.Quota.Windows.Cast<SummaryWindow>().Select(w=>w.Label+"："+Look.Value(w,true,config)+"，有效 "+w.Count+"/"+w.Total+" 个账号；最近已知重置："+QuotaReset.Description(w,true,J.Now)));
   return text+"\n"+String.Join("\n",summary.Accounts.Select(a=>{
    var quota=Look.Quota(state,a);return a.Label+"："+(Look.Fresh(quota,config)?String.Join("；",quota.Windows.Select(w=>w.Label+" "+Look.Value(w,true,config)+"，"+QuotaReset.Description(w,true,J.Now))):"数据缺失、过期或查询失败");
   }));
  }
 }
}
