using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CpaTaskbar
{
    /// <summary>
    /// Polls CPA (accounts and per-account quotas through its api-call proxy) and optionally CPAMP
    /// (token history). Thread-safe: state lives behind <c>gate</c>, the UI reads copies through
    /// <see cref="State"/>, and <see cref="Changed"/> fires on any thread. Every Apply bumps
    /// <c>generation</c> and cancels in-flight work so results for an old configuration are dropped.
    /// </summary>
    public class QuotaService : IDisposable
    {
        const long AccountRetryMs = 60000, AccountListTtlMs = 600000;
        const int RateLimitedMinSeconds = 300, ParallelQuotaRequests = 2;
        const string AuthFilesEndpoint = "/v0/management/auth-files";

        readonly object gate = new object();
        readonly HttpClient http;
        CancellationTokenSource cancel = new CancellationTokenSource();
        AppConfig config;
        string cpaKey = "", cpampKey = "", cpaKeyError = "", cpampKeyError = "";
        Snapshot current = new Snapshot();
        long accountsAt, accountAttempt;
        int generation;
        bool working;

        public event Action Changed;

        /// <summary>Test hook replacing HTTP: (baseUrl, endpoint, key, body, token) → parsed JSON.</summary>
        public Func<string, string, string, object, CancellationToken, Task<object>> RequestOverride;

        public QuotaService(AppConfig config)
        {
            // Redirects are not followed so the management key is never forwarded to another host.
            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            http.Timeout = TimeSpan.FromSeconds(12);
            Apply(config);
        }

        public Snapshot State
        {
            get
            {
                lock (gate)
                {
                    return new Snapshot
                    {
                        Accounts = current.Accounts.ToList(),
                        Quotas = new Dictionary<string, QuotaState>(current.Quotas),
                        History = new Dictionary<string, HistoryState>(current.History),
                        Error = current.Error,
                        Busy = working,
                    };
                }
            }
        }

        public void SetSelection(List<string> ids, string focus)
        {
            lock (gate)
            {
                config.SelectedIds = ids.ToList();
                config.FocusId = focus;
            }
        }

        /// <summary>
        /// Switches to new settings. A different CPA address or key clears all data; otherwise
        /// quotas are kept but marked due, and history is dropped.
        /// </summary>
        public void Apply(AppConfig value)
        {
            var next = value.Clone();
            string key = "", admin = "", keyError = "", adminError = "";
            try
            {
                key = AppConfig.Reveal(next.CpaSecret, "CPA管理密钥");
            }
            catch (Exception error)
            {
                keyError = error.Message;
            }
            if (next.CpampEnabled)
            {
                try
                {
                    admin = AppConfig.Reveal(next.CpampSecret, "CPAMP管理员密钥");
                }
                catch (Exception error)
                {
                    adminError = error.Message;
                }
            }

            lock (gate)
            {
                bool connectionChanged = config == null || config.CpaUrl != next.CpaUrl || config.CpaSecret != next.CpaSecret;
                config = next;
                cpaKey = key;
                cpampKey = admin;
                cpaKeyError = keyError;
                cpampKeyError = adminError;
                generation++;
                cancel.Cancel();
                cancel.Dispose();
                cancel = new CancellationTokenSource();
                if (connectionChanged)
                {
                    current = new Snapshot();
                    accountsAt = 0;
                }
                else
                {
                    current.History.Clear();
                    foreach (var quota in current.Quotas.Values)
                        quota.AttemptedAt = 0;
                }
                accountAttempt = 0;
                if (keyError != "")
                    current.Error = keyError;
            }
            Raise();
        }

        /// <summary>Checks a draft configuration without touching the service state.</summary>
        public async Task<string> TestAsync(AppConfig draft)
        {
            using (var cts = new CancellationTokenSource())
            {
                string key = AppConfig.Reveal(draft.CpaSecret, "CPA管理密钥");
                var accounts = QuotaParser.Accounts(await Request(draft.CpaUrl, AuthFilesEndpoint, key, null, cts.Token));
                if (draft.CpampEnabled)
                {
                    string admin = AppConfig.Reveal(draft.CpampSecret, "CPAMP管理员密钥");
                    var upstream = QuotaParser.Accounts(await Request(draft.CpampUrl, AuthFilesEndpoint, admin, null, cts.Token));
                    if (accounts.Any() && !accounts.Any(a => upstream.Any(b => b.Id == a.Id && b.AuthIndex == a.AuthIndex)))
                        throw new Exception("CPAMP的上游账号与当前CPA不一致。");
                }
                return "连接成功，已读取 " + accounts.Count + " 个账号" + (draft.CpampEnabled ? "；CPAMP也已连接。" : "。");
            }
        }

        /// <summary>
        /// Refreshes whatever is due (everything when forced). Only one refresh runs at a time;
        /// calls made while one is running return immediately.
        /// </summary>
        public async Task RefreshAsync(bool force = false)
        {
            Run run;
            lock (gate)
            {
                if (working || !force && !NeedsRefresh(J.Now))
                    return;
                working = true;
                run = new Run
                {
                    Config = config.Clone(),
                    Key = cpaKey,
                    Admin = cpampKey,
                    KeyError = cpaKeyError,
                    AdminError = cpampKeyError,
                    Generation = generation,
                    Token = cancel.Token,
                    Force = force,
                };
            }
            Raise();
            try
            {
                if (!await RefreshAccountsAsync(run).ConfigureAwait(false))
                    return;
                List<Account> chosen;
                lock (gate)
                    chosen = ChosenAccounts(current.Accounts, run.Config);
                await RefreshQuotasAsync(run, chosen).ConfigureAwait(false);
                await RefreshHistoryAsync(run, chosen).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                lock (gate)
                {
                    if (run.Generation == generation)
                        current.Error = "刷新未完成，稍后会重试。";
                }
            }
            finally
            {
                lock (gate)
                    working = false;
                Raise();
            }
        }

        public void Dispose()
        {
            cancel.Cancel();
            http.Dispose();
        }

        // ----- Refresh steps -----

        /// <summary>Inputs captured under the lock at the start of a refresh.</summary>
        sealed class Run
        {
            public AppConfig Config;
            public string Key, Admin, KeyError, AdminError;
            public int Generation;
            public CancellationToken Token;
            public bool Force;
        }

        /// <summary>Reloads the account list when due. Returns false when the refresh must stop.</summary>
        async Task<bool> RefreshAccountsAsync(Run run)
        {
            // An undecryptable key must surface as such instead of a misleading "please fill in the key".
            if (run.KeyError != "")
            {
                lock (gate)
                {
                    accountAttempt = J.Now;
                    if (run.Generation == generation)
                        current.Error = run.KeyError;
                }
                Raise();
                return false;
            }

            bool due;
            lock (gate)
            {
                long now = J.Now;
                due = run.Force
                    || current.Error != "" && now - accountAttempt >= AccountRetryMs
                    || accountsAt == 0 && now - accountAttempt > AccountRetryMs
                    || accountsAt > 0 && now - accountsAt > AccountListTtlMs;
                if (due)
                    accountAttempt = now;
            }
            if (!due)
                return true;

            try
            {
                var accounts = QuotaParser.Accounts(await Request(run.Config.CpaUrl, AuthFilesEndpoint, run.Key, null, run.Token).ConfigureAwait(false));
                lock (gate)
                {
                    if (run.Generation != generation)
                        return false;
                    current.Accounts = accounts;
                    current.Error = "";
                    accountsAt = J.Now;
                }
                Raise();
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception error)
            {
                lock (gate)
                {
                    if (run.Generation == generation)
                    {
                        current.Error = error.Message;
                        foreach (var quota in current.Quotas.Values)
                            quota.Error = "CPA连接异常";
                    }
                }
                Raise();
                return false;
            }
        }

        /// <summary>Fetches due quotas, two at a time. A failure keeps the previous windows with an error.</summary>
        async Task RefreshQuotasAsync(Run run, List<Account> chosen)
        {
            using (var limit = new SemaphoreSlim(ParallelQuotaRequests))
            {
                await Task.WhenAll(chosen.Select(async account =>
                {
                    QuotaState previous;
                    lock (gate)
                        current.Quotas.TryGetValue(account.Id, out previous);
                    if (!run.Force && previous != null && J.Now - previous.AttemptedAt < TtlSeconds(previous, run.Config) * 1000L)
                        return;

                    await limit.WaitAsync(run.Token).ConfigureAwait(false);
                    var quota = new QuotaState { AttemptedAt = J.Now };
                    try
                    {
                        quota = await FetchQuota(run.Config, run.Key, account, run.Token).ConfigureAwait(false);
                        quota.AttemptedAt = J.Now;
                        quota.UpdatedAt = J.Now;
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception error)
                    {
                        quota.Windows = previous == null ? new List<QuotaWindow>() : previous.Windows;
                        quota.UpdatedAt = previous == null ? 0 : previous.UpdatedAt;
                        quota.Error = error.Message;
                        var provider = error as ProviderException;
                        quota.RateLimited = provider != null && provider.Status == 429;
                    }
                    finally
                    {
                        limit.Release();
                    }
                    lock (gate)
                    {
                        if (run.Generation == generation)
                            current.Quotas[account.Id] = quota;
                    }
                    Raise();
                })).ConfigureAwait(false);
            }
        }

        /// <summary>Fetches CPAMP day and month totals for the focused account when due.</summary>
        async Task RefreshHistoryAsync(Run run, List<Account> chosen)
        {
            var c = run.Config;
            if (!c.CpampEnabled || String.IsNullOrEmpty(run.Admin) && run.AdminError == "")
                return;
            Account focus = chosen.FirstOrDefault(a => a.Id == c.FocusId) ?? chosen.FirstOrDefault();
            if (focus == null)
                return;
            HistoryState previous;
            lock (gate)
                current.History.TryGetValue(focus.Id, out previous);
            long now = J.Now;
            if (!run.Force && !HistoryDue(previous, now, c))
                return;

            var history = new HistoryState { AttemptedAt = now, TodayStart = QuotaParser.DayStart(now, false) };
            try
            {
                if (run.AdminError != "")
                    throw new Exception(run.AdminError);
                var upstream = QuotaParser.Accounts(await Request(c.CpampUrl, AuthFilesEndpoint, run.Admin, null, run.Token).ConfigureAwait(false));
                if (!upstream.Any(a => a.Id == focus.Id && a.AuthIndex == focus.AuthIndex))
                    throw new Exception("CPAMP上游账号与当前CPA不一致。");
                long end = J.Now;
                Func<bool, Task<object>> query = month => Request(c.CpampUrl, "/v0/management/monitoring/analytics", run.Admin, new
                {
                    from_ms = QuotaParser.DayStart(end, month),
                    to_ms = end,
                    now_ms = end,
                    time_zone = "Asia/Shanghai",
                    filters = new
                    {
                        auth_indices = new[] { focus.AuthIndex }
                    },
                    include = new
                    {
                        summary = true,
                        summary_profile = "compact",
                        summary_percentiles = false,
                        summary_comparison = false
                    },
                }, run.Token);
                var results = await Task.WhenAll(query(false), query(true)).ConfigureAwait(false);
                QuotaParser.History(results[0], history, false);
                QuotaParser.History(results[1], history, true);
                history.UpdatedAt = J.Now;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error)
            {
                history.Error = error.Message;
            }
            lock (gate)
            {
                if (run.Generation == generation)
                    current.History[focus.Id] = history;
            }
            Raise();
        }

        // ----- Scheduling -----

        /// <summary>Called under the lock by the periodic timer to skip refreshes when nothing is due.</summary>
        bool NeedsRefresh(long now)
        {
            if (accountsAt == 0)
                return now - accountAttempt >= AccountRetryMs;
            if (now - accountsAt >= AccountListTtlMs || current.Error != "" && now - accountAttempt >= AccountRetryMs)
                return true;
            var chosen = ChosenAccounts(current.Accounts, config);
            foreach (var account in chosen)
            {
                QuotaState quota;
                if (!current.Quotas.TryGetValue(account.Id, out quota) || now - quota.AttemptedAt >= TtlSeconds(quota, config) * 1000L)
                    return true;
            }
            if (config.CpampEnabled && cpampKey != "" && chosen.Count > 0)
            {
                var focus = chosen.FirstOrDefault(a => a.Id == config.FocusId) ?? chosen[0];
                HistoryState history;
                current.History.TryGetValue(focus.Id, out history);
                if (HistoryDue(history, now, config))
                    return true;
            }
            return false;
        }

        /// <summary>Selected supported accounts; with nothing selected yet, the default account.</summary>
        static List<Account> ChosenAccounts(List<Account> accounts, AppConfig c)
        {
            var chosen = accounts.Where(a => a.Supported && c.SelectedIds.Contains(a.Id)).ToList();
            if (chosen.Count == 0 && c.SelectedIds.Count == 0)
            {
                var first = Account.PickDefault(accounts);
                if (first != null)
                    chosen.Add(first);
            }
            return chosen;
        }

        /// <summary>Refresh interval; rate-limited accounts wait at least five minutes.</summary>
        static int TtlSeconds(QuotaState quota, AppConfig c)
        {
            return quota.RateLimited ? Math.Max(RateLimitedMinSeconds, c.RefreshSeconds) : c.RefreshSeconds;
        }

        static bool HistoryDue(HistoryState history, long now, AppConfig c)
        {
            return history == null
                || now - history.AttemptedAt >= c.HistorySeconds * 1000L
                || history.TodayStart != QuotaParser.DayStart(now, false);
        }

        // ----- HTTP -----

        void Raise()
        {
            var handler = Changed;
            if (handler != null)
                handler();
        }

        async Task<object> Request(string url, string endpoint, string key, object body, CancellationToken ct)
        {
            if (RequestOverride != null)
                return await RequestOverride(url, endpoint, key, body, ct).ConfigureAwait(false);
            if (String.IsNullOrEmpty(key))
                throw new Exception("请在设置中填写CPA管理密钥。");
            using (var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, url.TrimEnd('/') + endpoint))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                if (body != null)
                    request.Content = new StringContent(J.Json(body), Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                try
                {
                    response = await http.SendAsync(request, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (ct.IsCancellationRequested)
                        throw;
                    throw new Exception("请求超时，请检查服务地址和网络。");
                }
                catch (HttpRequestException)
                {
                    throw new Exception("连接失败，请检查服务地址和网络。");
                }
                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        int status = (int)response.StatusCode;
                        bool denied = response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden;
                        throw new Exception(denied ? "管理密钥无效或无权访问（HTTP " + status + "）" : "服务请求失败（HTTP " + status + "）");
                    }
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (text.Length > 8 * 1024 * 1024)
                        throw new Exception("接口数据过大。");
                    try
                    {
                        return J.Parse(text);
                    }
                    catch
                    {
                        throw new Exception("服务返回的不是有效JSON，请检查地址。");
                    }
                }
            }
        }

        /// <summary>Queries one account's provider through CPA's api-call proxy (CPA substitutes $TOKEN$).</summary>
        async Task<QuotaState> FetchQuota(AppConfig c, string key, Account account, CancellationToken ct)
        {
            var header = new Dictionary<string, string> { { "Authorization", "Bearer $TOKEN$" } };
            Func<string, string, string, Task<object>> call = async (method, url, data) =>
            {
                var proxied = new Dictionary<string, object>
                {
                    { "auth_index", account.AuthIndex },
                    { "method", method },
                    { "url", url },
                    { "header", header },
                };
                if (data != null)
                    proxied["data"] = data;
                return QuotaParser.Unwrap(await Request(c.CpaUrl, "/v0/management/api-call", key, proxied, ct).ConfigureAwait(false));
            };

            object payload;
            switch (account.Provider)
            {
                case "codex":
                    header["User-Agent"] = UserAgent(c.CodexUserAgent, AppConfig.DefaultCodexUserAgent);
                    if (account.AccountId != "")
                        header["Chatgpt-Account-Id"] = account.AccountId;
                    payload = await call("GET", "https://chatgpt.com/backend-api/wham/usage", null).ConfigureAwait(false);
                    return new QuotaState { Windows = QuotaParser.Codex(payload), Plan = J.S(payload, "plan_type") };

                case "claude":
                    header["anthropic-beta"] = "oauth-2025-04-20";
                    payload = await call("GET", "https://api.anthropic.com/api/oauth/usage", null).ConfigureAwait(false);
                    return new QuotaState { Windows = QuotaParser.Claude(payload) };

                case "gemini":
                    header["Content-Type"] = "application/json";
                    if (String.IsNullOrEmpty(account.ProjectId))
                        throw new Exception("Gemini CLI缺少project_id，请在CPA中补齐。");
                    payload = await call("POST", "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota", J.Json(new
                    {
                        project = account.ProjectId
                    })).ConfigureAwait(false);
                    return new QuotaState { Windows = QuotaParser.Google(payload, true) };

                default:
                    header["Content-Type"] = "application/json";
                    header["User-Agent"] = UserAgent(c.AntigravityUserAgent, AppConfig.DefaultAntigravityUserAgent);
                    return await FetchAntigravity(call, account).ConfigureAwait(false);
            }
        }

        /// <summary>Tries each Antigravity method and host until one returns quota windows. A 429 stops the search.</summary>
        static async Task<QuotaState> FetchAntigravity(Func<string, string, string, Task<object>> call, Account account)
        {
            var project = String.IsNullOrEmpty(account.ProjectId)
                ? new Dictionary<string, object>()
                : new Dictionary<string, object> { { "project", account.ProjectId } };
            Exception last = null;
            foreach (string method in new[] { "retrieveUserQuotaSummary", "fetchAvailableModels" })
            {
                foreach (string host in new[] { "daily-cloudcode-pa.googleapis.com", "daily-cloudcode-pa.sandbox.googleapis.com", "cloudcode-pa.googleapis.com" })
                {
                    try
                    {
                        object payload = await call("POST", "https://" + host + "/v1internal:" + method, J.Json(project)).ConfigureAwait(false);
                        var windows = QuotaParser.Google(payload);
                        if (windows.Count > 0)
                            return new QuotaState { Windows = windows };
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (ProviderException error)
                    {
                        if (error.Status == 429)
                            throw;
                        last = error;
                    }
                    catch (Exception error)
                    {
                        last = error;
                    }
                }
            }
            throw last ?? new Exception("供应商没有返回可识别的模型额度。");
        }

        static string UserAgent(string configured, string fallback)
        {
            return String.IsNullOrWhiteSpace(configured) ? fallback : configured;
        }
    }
}
