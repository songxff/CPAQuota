using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CpaTaskbar {
 public class QuotaService:IDisposable {
  readonly object gate=new object();
  readonly HttpClient http;
  CancellationTokenSource cancel=new CancellationTokenSource();
  AppConfig config;
  string cpaKey="",cpampKey="";
  Snapshot current=new Snapshot();
  long accountsAt,accountAttempt;
  int generation;
  bool working;
  public event Action Changed;
  public Func<string,string,string,object,CancellationToken,Task<object>> RequestOverride;
  public QuotaService(AppConfig c){http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false});http.Timeout=TimeSpan.FromSeconds(12);Apply(c);}
  public void SetSelection(List<string> ids,string focus){lock(gate){config.SelectedIds=ids.ToList();config.FocusId=focus;}}
  public Snapshot State{get{lock(gate)return new Snapshot{Accounts=current.Accounts.ToList(),Quotas=new Dictionary<string,QuotaState>(current.Quotas),History=new Dictionary<string,HistoryState>(current.History),Error=current.Error,Busy=working};}}
  public void Apply(AppConfig value){
   var next=value.Clone();string a="",b="";string error="";
   try{a=AppConfig.Unprotect(next.CpaSecret);if(next.CpampEnabled)b=AppConfig.Unprotect(next.CpampSecret);}catch{error="无法解密连接密钥，请用当前Windows账号重新保存连接。";}
   lock(gate){
    bool changed=config==null||config.CpaUrl!=next.CpaUrl||config.CpaSecret!=next.CpaSecret;
    config=next;cpaKey=a;cpampKey=b;generation++;cancel.Cancel();cancel.Dispose();cancel=new CancellationTokenSource();
    if(changed){current=new Snapshot();accountsAt=0;}else{current.History.Clear();foreach(var q in current.Quotas.Values)q.AttemptedAt=0;}
    accountAttempt=0;if(error!="")current.Error=error;
   }
   Raise();
  }
  void Raise(){var handler=Changed;if(handler!=null)handler();}
  async Task<object> Request(string url,string endpoint,string key,object body,CancellationToken ct){
   if(RequestOverride!=null)return await RequestOverride(url,endpoint,key,body,ct).ConfigureAwait(false);
   if(String.IsNullOrEmpty(key))throw new Exception("请在设置中填写CPA管理密钥。");
   using(var r=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,url.TrimEnd('/')+endpoint)){
    r.Headers.TryAddWithoutValidation("Authorization","Bearer "+key);r.Headers.TryAddWithoutValidation("Accept","application/json");
    if(body!=null)r.Content=new StringContent(J.Json(body),Encoding.UTF8,"application/json");
    HttpResponseMessage response;
    try{response=await http.SendAsync(r,ct).ConfigureAwait(false);}catch(OperationCanceledException){if(ct.IsCancellationRequested)throw;throw new Exception("请求超时，请检查服务地址和网络。");}catch(HttpRequestException){throw new Exception("连接失败，请检查服务地址和网络。");}
    using(response){
     if(!response.IsSuccessStatusCode)throw new Exception(response.StatusCode==HttpStatusCode.Unauthorized||response.StatusCode==HttpStatusCode.Forbidden?"管理密钥无效或无权访问（HTTP "+(int)response.StatusCode+"）":"服务请求失败（HTTP "+(int)response.StatusCode+"）");
     string text=await response.Content.ReadAsStringAsync().ConfigureAwait(false);if(text.Length>8*1024*1024)throw new Exception("接口数据过大。");
     try{return J.Parse(text);}catch{throw new Exception("服务返回的不是有效JSON，请检查地址。");}
    }
   }
  }
  public async Task<string> TestAsync(AppConfig draft){
   using(var cts=new CancellationTokenSource()){
    string key=AppConfig.Unprotect(draft.CpaSecret);
    var list=QuotaParser.Accounts(await Request(draft.CpaUrl,"/v0/management/auth-files",key,null,cts.Token));
    if(draft.CpampEnabled){string admin=AppConfig.Unprotect(draft.CpampSecret);var other=QuotaParser.Accounts(await Request(draft.CpampUrl,"/v0/management/auth-files",admin,null,cts.Token));if(list.Any()&&!list.Any(a=>other.Any(b=>b.Id==a.Id&&b.AuthIndex==a.AuthIndex)))throw new Exception("CPAMP的上游账号与当前CPA不一致。");}
    return "连接成功，已读取 "+list.Count+" 个账号"+(draft.CpampEnabled?"；CPAMP也已连接。":"。");
   }
  }
  bool NeedsRefresh(long now){
   if(accountsAt==0)return now-accountAttempt>=60000;
   if(now-accountsAt>=600000||current.Error!=""&&now-accountAttempt>=60000)return true;
   var chosen=current.Accounts.Where(a=>a.Supported&&config.SelectedIds.Contains(a.Id)).ToList();
   if(chosen.Count==0&&config.SelectedIds.Count==0){var first=current.Accounts.FirstOrDefault(a=>a.Provider=="codex"&&!a.Disabled)??current.Accounts.FirstOrDefault(a=>a.Supported&&!a.Disabled);if(first!=null)chosen.Add(first);}
   foreach(var a in chosen){QuotaState q;if(!current.Quotas.TryGetValue(a.Id,out q)||now-q.AttemptedAt>=(q.RateLimited?Math.Max(300,config.RefreshSeconds):config.RefreshSeconds)*1000L)return true;}
   if(config.CpampEnabled&&cpampKey!=""&&chosen.Count>0){
    var focus=chosen.FirstOrDefault(a=>a.Id==config.FocusId)??chosen[0];HistoryState h;
    if(!current.History.TryGetValue(focus.Id,out h)||now-h.AttemptedAt>=config.HistorySeconds*1000L||h.TodayStart!=QuotaParser.DayStart(now,false))return true;
   }
   return false;
  }
  public async Task RefreshAsync(bool force=false){
   AppConfig c;string key,admin;int g;CancellationToken ct;
   lock(gate){if(working||!force&&!NeedsRefresh(J.Now))return;working=true;c=config.Clone();key=cpaKey;admin=cpampKey;g=generation;ct=cancel.Token;}
   Raise();
   try{
    bool getList;lock(gate)getList=force||current.Error!=""&&J.Now-accountAttempt>=60000||accountsAt==0&&J.Now-accountAttempt>60000||accountsAt>0&&J.Now-accountsAt>600000;
    if(getList){
     lock(gate)accountAttempt=J.Now;
     try{var list=QuotaParser.Accounts(await Request(c.CpaUrl,"/v0/management/auth-files",key,null,ct).ConfigureAwait(false));lock(gate){if(g!=generation)return;current.Accounts=list;current.Error="";accountsAt=J.Now;}Raise();}
     catch(OperationCanceledException){return;}
     catch(Exception e){lock(gate){if(g==generation){current.Error=e.Message;foreach(var q in current.Quotas.Values)q.Error="CPA连接异常";}}Raise();return;}
    }
    List<Account> chosen;lock(gate){
     chosen=current.Accounts.Where(a=>a.Supported&&c.SelectedIds.Contains(a.Id)).ToList();
     if(chosen.Count==0&&c.SelectedIds.Count==0){var first=current.Accounts.FirstOrDefault(a=>a.Provider=="codex"&&!a.Disabled)??current.Accounts.FirstOrDefault(a=>a.Supported&&!a.Disabled);if(first!=null)chosen.Add(first);}
    }
    using(var limit=new SemaphoreSlim(2)){
     await Task.WhenAll(chosen.Select(async account=>{
      QuotaState previous;lock(gate)current.Quotas.TryGetValue(account.Id,out previous);
      int ttl=previous!=null&&previous.RateLimited?Math.Max(300,c.RefreshSeconds):c.RefreshSeconds;
      if(!force&&previous!=null&&J.Now-previous.AttemptedAt<ttl*1000)return;
      await limit.WaitAsync(ct).ConfigureAwait(false);
      var q=new QuotaState{AttemptedAt=J.Now};
      try{q=await FetchQuota(c,key,account,ct).ConfigureAwait(false);q.AttemptedAt=J.Now;q.UpdatedAt=J.Now;}
      catch(OperationCanceledException){return;}
      catch(Exception e){q.Windows=previous==null?new List<QuotaWindow>():previous.Windows;q.UpdatedAt=previous==null?0:previous.UpdatedAt;q.Error=e.Message;q.RateLimited=e is ProviderException&&((ProviderException)e).Status==429;}
      finally{limit.Release();}
      lock(gate){if(g==generation)current.Quotas[account.Id]=q;}Raise();
     })).ConfigureAwait(false);
    }
    if(c.CpampEnabled&&!String.IsNullOrEmpty(admin)){
     Account focus=chosen.FirstOrDefault(a=>a.Id==c.FocusId)??chosen.FirstOrDefault();
     if(focus!=null){
      HistoryState old;lock(gate)current.History.TryGetValue(focus.Id,out old);
      long today=QuotaParser.DayStart(J.Now,false);
      if(force||old==null||J.Now-old.AttemptedAt>=c.HistorySeconds*1000||old.TodayStart!=today){
       var h=new HistoryState{AttemptedAt=J.Now,TodayStart=today};
       try{
        var cpAccounts=QuotaParser.Accounts(await Request(c.CpampUrl,"/v0/management/auth-files",admin,null,ct).ConfigureAwait(false));
        if(!cpAccounts.Any(a=>a.Id==focus.Id&&a.AuthIndex==focus.AuthIndex))throw new Exception("CPAMP上游账号与当前CPA不一致。");
        long now=J.Now;
        Func<bool,Task<object>> query=month=>Request(c.CpampUrl,"/v0/management/monitoring/analytics",admin,new{from_ms=QuotaParser.DayStart(now,month),to_ms=now,now_ms=now,time_zone="Asia/Shanghai",filters=new{auth_indices=new[]{focus.AuthIndex}},include=new{summary=true,summary_profile="compact",summary_percentiles=false,summary_comparison=false}},ct);
        var both=await Task.WhenAll(query(false),query(true)).ConfigureAwait(false);QuotaParser.History(both[0],h,false);QuotaParser.History(both[1],h,true);h.UpdatedAt=J.Now;
       }catch(OperationCanceledException){return;}catch(Exception e){h.Error=e.Message;}
       lock(gate){if(g==generation)current.History[focus.Id]=h;}Raise();
      }
     }
    }
   }catch(OperationCanceledException){}catch(Exception){lock(gate)if(g==generation)current.Error="刷新未完成，稍后会重试。";}
   finally{lock(gate)working=false;Raise();}
  }
  async Task<QuotaState> FetchQuota(AppConfig c,string key,Account a,CancellationToken ct){
   var header=new Dictionary<string,string>{{"Authorization","Bearer $TOKEN$"}};object payload=null;
   Func<string,string,string,Task<object>> call=async(method,url,data)=>{
    var req=new Dictionary<string,object>{{"auth_index",a.AuthIndex},{"method",method},{"url",url},{"header",header}};
    if(data!=null)req["data"]=data;
    return QuotaParser.Unwrap(await Request(c.CpaUrl,"/v0/management/api-call",key,req,ct).ConfigureAwait(false));
   };
   if(a.Provider=="codex"){
    header["User-Agent"]="codex-tui/0.149.1 (Mac OS 26.5.2; arm64) iTerm.app/3.6.11 (codex-tui; 0.149.1)";
    if(a.AccountId!="")header["Chatgpt-Account-Id"]=a.AccountId;
    payload=await call("GET","https://chatgpt.com/backend-api/wham/usage",null).ConfigureAwait(false);
    return new QuotaState{Windows=QuotaParser.Codex(payload),Plan=J.S(payload,"plan_type")};
   }
   if(a.Provider=="claude"){
    header["anthropic-beta"]="oauth-2025-04-20";payload=await call("GET","https://api.anthropic.com/api/oauth/usage",null).ConfigureAwait(false);
    return new QuotaState{Windows=QuotaParser.Claude(payload)};
   }
   header["Content-Type"]="application/json";
   if(a.Provider=="gemini"){
    if(String.IsNullOrEmpty(a.ProjectId))throw new Exception("Gemini CLI缺少project_id，请在CPA中补齐。");
    payload=await call("POST","https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota",J.Json(new{project=a.ProjectId})).ConfigureAwait(false);
    return new QuotaState{Windows=QuotaParser.Google(payload,true)};
   }
   header["User-Agent"]="antigravity/hub/2.23.0 darwin/arm64";
   Exception last=null;
   foreach(string method in new[]{"retrieveUserQuotaSummary","fetchAvailableModels"}){
    foreach(string host in new[]{"daily-cloudcode-pa.googleapis.com","daily-cloudcode-pa.sandbox.googleapis.com","cloudcode-pa.googleapis.com"}){
     try{payload=await call("POST","https://"+host+"/v1internal:"+method,J.Json(String.IsNullOrEmpty(a.ProjectId)?new Dictionary<string,object>():new Dictionary<string,object>{{"project",a.ProjectId}})).ConfigureAwait(false);var windows=QuotaParser.Google(payload);if(windows.Count>0)return new QuotaState{Windows=windows};}
     catch(OperationCanceledException){throw;}catch(ProviderException e){if(e.Status==429)throw;last=e;}catch(Exception e){last=e;}
    }
   }
   throw last??new Exception("供应商没有返回可识别的模型额度。");
  }
  public void Dispose(){cancel.Cancel();http.Dispose();}
 }
}
