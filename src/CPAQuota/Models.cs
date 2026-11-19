using System;
using System.Collections.Generic;
using System.Linq;

namespace CpaTaskbar
{
    /// <summary>An auth file registered in CPA.</summary>
    public class Account
    {
        static readonly string[] SupportedProviders = { "codex", "claude", "antigravity", "gemini" };

        public string Id, Name, Provider, Label, AuthIndex, AccountId, ProjectId;
        public bool Disabled;

        public bool Supported
        {
            get
            {
                return SupportedProviders.Contains(Provider);
            }
        }

        /// <summary>
        /// The account shown when the user has not chosen one: the first enabled Codex account,
        /// otherwise the first enabled supported account, otherwise null.
        /// </summary>
        public static Account PickDefault(IEnumerable<Account> accounts)
        {
            var list = accounts as IList<Account> ?? accounts.ToList();
            return list.FirstOrDefault(a => a.Provider == "codex" && !a.Disabled)
                ?? list.FirstOrDefault(a => a.Supported && !a.Disabled);
        }

        public override string ToString()
        {
            return J.Provider(Provider) + "  ·  " + Label;
        }
    }

    /// <summary>One quota period (5 hours, a week, a model bucket, ...) as a used percentage.</summary>
    public class QuotaWindow
    {
        public string Id, Label, Scope = "";
        /// <summary>Used percentage, 0–100; null when the provider did not report it.</summary>
        public double? Used;
        /// <summary>Reset time in Unix milliseconds, when known.</summary>
        public long? ResetAt;
        /// <summary>Period length in seconds, when known.</summary>
        public double? Duration;

        public double? Remaining
        {
            get
            {
                return Used.HasValue ? (double?)(100 - Used.Value) : null;
            }
        }

        /// <summary>Has a value and has not passed its reset time.</summary>
        public bool Valid
        {
            get
            {
                return Used.HasValue && (!ResetAt.HasValue || ResetAt > J.Now);
            }
        }
    }

    public class QuotaState
    {
        public List<QuotaWindow> Windows = new List<QuotaWindow>();
        public string Plan = "", Error = "";
        public long UpdatedAt, AttemptedAt;
        public bool RateLimited;
    }

    /// <summary>CPAMP token and request totals for one account (Beijing calendar day and month).</summary>
    public class HistoryState
    {
        public double? TodayTokens, TodayCalls, MonthTokens, MonthCalls;
        public long UpdatedAt, AttemptedAt, TodayStart;
        public string Error = "";
    }

    /// <summary>Immutable-by-convention copy of the service state handed to the UI.</summary>
    public class Snapshot
    {
        public List<Account> Accounts = new List<Account>();
        public Dictionary<string, QuotaState> Quotas = new Dictionary<string, QuotaState>();
        public Dictionary<string, HistoryState> History = new Dictionary<string, HistoryState>();
        public string Error = "";
        public bool Busy;
    }

    /// <summary>Upstream provider error forwarded by CPA's api-call endpoint.</summary>
    public class ProviderException : Exception
    {
        public int Status;

        public ProviderException(string message, int status) : base(message)
        {
            Status = status;
        }
    }
}
