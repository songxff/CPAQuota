using System;
using System.Collections.Generic;
using System.Linq;

namespace CpaTaskbar
{
    /// <summary>A quota window averaged over several accounts.</summary>
    public sealed class SummaryWindow : QuotaWindow
    {
        /// <summary>Accounts with valid data for this window / accounts in the summary / of those, with a known reset time.</summary>
        public int Count, Total, KnownResets;
    }

    public sealed class QuotaSummary
    {
        public string Provider = "";
        public List<Account> Accounts = new List<Account>();
        public QuotaState Quota = new QuotaState();

        /// <summary>Changes whenever anything displayed by the summary changes.</summary>
        public string Key
        {
            get
            {
                var windows = Quota.Windows.Cast<SummaryWindow>().Select(w => w.Id + "/" + w.Scope + "/" + w.Used + "/" + w.Count + "/" + w.Total + "/" + w.ResetAt);
                return Provider + "|" + Accounts.Count + "|" + String.Join(";", windows);
            }
        }

        public string Title(AppConfig config)
        {
            return J.Provider(Provider) + " · " + (config.QuotaDisplayMode == "sum" ? "累加" : "平均") + " · " + Accounts.Count + "个账号";
        }
    }

    /// <summary>
    /// Average/sum display across the selected accounts of one provider. Windows are matched by
    /// id, scope and duration; only fresh, unexpired values count. The reset time is the earliest
    /// known one among the counted accounts.
    /// </summary>
    public static class QuotaAggregation
    {
        public static bool Enabled(AppConfig config)
        {
            return config.QuotaDisplayMode == "average" || config.QuotaDisplayMode == "sum";
        }

        public static QuotaSummary Build(Snapshot state, AppConfig config)
        {
            var result = new QuotaSummary();
            var selected = state.Accounts.Where(a => a.Supported && !a.Disabled && config.SelectedIds.Contains(a.Id)).ToList();

            // The focused account's provider, else the first selected account's provider.
            var focus = Look.Focus(state, config);
            result.Provider = focus == null ? "" : focus.Provider;
            if (selected.Count > 0 && !selected.Any(a => a.Provider == result.Provider))
                result.Provider = selected[0].Provider;

            // The same upstream account can appear in several auth files; keep its freshest file.
            result.Accounts = selected
                .Where(a => a.Provider == result.Provider)
                .GroupBy(a => String.IsNullOrEmpty(a.AccountId) ? "file:" + a.Id : "account:" + a.AccountId, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(a => Look.Fresh(Look.Quota(state, a), config)).ThenByDescending(a => UpdatedAt(Look.Quota(state, a))).First())
                .ToList();

            var windows = result.Accounts.SelectMany(account =>
            {
                var quota = Look.Quota(state, account);
                var list = quota == null ? new List<QuotaWindow>() : quota.Windows;
                return list.Select(window => new { Account = account, Quota = quota, Window = window });
            });
            foreach (var group in windows.GroupBy(x => new { x.Window.Id, x.Window.Scope, x.Window.Duration }))
            {
                var first = group.First().Window;
                var valid = group
                    .Where(x => Look.Fresh(x.Quota, config) && x.Window.Valid && !Double.IsNaN(x.Window.Used.Value) && !Double.IsInfinity(x.Window.Used.Value))
                    .GroupBy(x => x.Account.Id)
                    .Select(g => g.First())
                    .ToList();
                var resets = valid.Where(x => x.Window.ResetAt.HasValue).Select(x => x.Window.ResetAt.Value).ToList();
                result.Quota.Windows.Add(new SummaryWindow
                {
                    Id = first.Id,
                    Label = first.Label,
                    Scope = first.Scope,
                    Duration = first.Duration,
                    Used = valid.Count == 0 ? (double?)null : valid.Average(x => x.Window.Used.Value),
                    ResetAt = resets.Count == 0 ? (long?)null : resets.Min(),
                    Count = valid.Count,
                    Total = result.Accounts.Count,
                    KnownResets = resets.Count,
                });
            }

            result.Quota.UpdatedAt = result.Accounts
                .Select(a => Look.Quota(state, a))
                .Where(q => Look.Fresh(q, config))
                .Select(q => q.UpdatedAt)
                .DefaultIfEmpty(0)
                .Min();

            // Codex and Claude always show both standard periods, even with no data.
            if (result.Provider == "codex" || result.Provider == "claude")
            {
                foreach (string id in new[] { "5h", "week" })
                {
                    if (!result.Quota.Windows.Any(w => w.Id == id))
                        result.Quota.Windows.Add(new SummaryWindow { Id = id, Label = QuotaParser.PeriodLabel(id), Count = 0, Total = result.Accounts.Count });
                }
            }
            return result;
        }

        /// <summary>The quota the panel shows: the summary when enabled, else the focused account's.</summary>
        public static QuotaState BarQuota(Snapshot state, AppConfig config)
        {
            return Look.ResolveBar(state, config).Quota;
        }

        /// <summary>Prefixes 合/均 and appends [valid/total] for summary windows.</summary>
        public static string Caption(QuotaWindow window, string caption, AppConfig config)
        {
            var summary = window as SummaryWindow;
            if (summary == null)
                return caption;
            return (config.QuotaDisplayMode == "sum" ? "合" : "均") + caption + "[" + summary.Count + "/" + summary.Total + "]";
        }

        /// <summary>Tooltip text: rules, each summary window, then each account.</summary>
        public static string Description(Snapshot state, AppConfig config)
        {
            var summary = Build(state, config);
            var windows = summary.Quota.Windows.Cast<SummaryWindow>().Select(w =>
                w.Label + "：" + Look.Value(w, true, config) + "，有效 " + w.Count + "/" + w.Total + " 个账号；最近已知重置：" + QuotaReset.Description(w, true, J.Now));
            var accounts = summary.Accounts.Select(account =>
            {
                var quota = Look.Quota(state, account);
                if (!Look.Fresh(quota, config))
                    return account.Label + "：数据缺失、过期或查询失败";
                return account.Label + "：" + String.Join("；", quota.Windows.Select(w => w.Label + " " + Look.Value(w, true, config) + "，" + QuotaReset.Description(w, true, J.Now)));
            });
            return summary.Title(config) + "\n仅合并同渠道、同周期；进度条跟随显示的剩余或已用比例，累加超过100%时封顶。\n"
                + String.Join("\n", windows) + "\n" + String.Join("\n", accounts);
        }

        static long UpdatedAt(QuotaState quota)
        {
            return quota == null ? 0 : quota.UpdatedAt;
        }
    }
}
