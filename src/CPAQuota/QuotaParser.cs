using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CpaTaskbar
{
    /// <summary>Turns CPA, provider and CPAMP JSON payloads into the app's models.</summary>
    public static class QuotaParser
    {
        const double FiveHours = 18000, OneWeek = 604800;
        static readonly TimeZoneInfo ChinaZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

        /// <summary>Accounts from /v0/management/auth-files. Tokens are read only for the ChatGPT account id.</summary>
        public static List<Account> Accounts(object payload)
        {
            if (!(J.Get(payload, "files") is IEnumerable))
                throw new Exception("CPA账号列表格式不兼容。");
            var result = new List<Account>();
            foreach (object file in J.A(J.Get(payload, "files")))
            {
                object index = J.Get(file, "auth_index") ?? J.Get(file, "authIndex");
                if (index == null)
                    continue;
                string provider = FirstNonEmpty(J.S(file, "provider"), J.S(file, "type")).ToLowerInvariant();
                if (provider == "gemini-cli" || provider == "gemini_cli")
                    provider = "gemini";
                string name = FirstNonEmpty(J.S(file, "name"), J.S(index));

                object token = J.Get(file, "id_token");
                if (token is string)
                {
                    try
                    {
                        token = J.Parse((string)token);
                    }
                    catch
                    {
                        token = null;
                    }
                }

                result.Add(new Account
                {
                    Id = J.Hash(provider + ":" + name),
                    Name = name,
                    Provider = provider,
                    AuthIndex = J.S(index),
                    Label = FirstNonEmpty(J.S(file, "email"), J.S(file, "label"), name),
                    AccountId = FirstNonEmpty(J.S(token, "chatgpt_account_id"), J.S(file, "account_id")),
                    ProjectId = FirstNonEmpty(J.S(file, "project_id"), J.S(J.Get(file, "metadata"), "project_id"), J.S(J.Get(file, "attributes"), "project_id")),
                    Disabled = J.Get(file, "disabled") is bool && (bool)J.Get(file, "disabled"),
                });
            }
            return result;
        }

        /// <summary>ChatGPT /backend-api/wham/usage: main, code review and additional rate limits.</summary>
        public static List<QuotaWindow> Codex(object payload)
        {
            var result = new List<QuotaWindow>();
            Action<object, string, string> add = (rate, prefix, scope) =>
            {
                foreach (string kind in new[] { "primary", "secondary" })
                {
                    object window = J.Get(rate, kind + "_window") ?? J.Get(rate, kind + "Window");
                    if (window == null)
                        continue;
                    double? seconds = J.N(J.Get(window, "limit_window_seconds") ?? J.Get(window, "limitWindowSeconds"));
                    string id = seconds == FiveHours ? "5h"
                        : seconds == OneWeek ? "week"
                        : seconds >= 2419200 && seconds <= 2678400 ? "month"
                        : kind;
                    string label = id == "month" ? "月额度"
                        : id == "5h" || id == "week" ? PeriodLabel(id)
                        : seconds.HasValue ? (seconds.Value / 3600).ToString("0.#") + "小时"
                        : "周期未知";
                    object reset = J.Get(window, "reset_at") ?? J.Get(window, "resetAt");
                    double? after = J.N(J.Get(window, "reset_after_seconds") ?? J.Get(window, "resetAfterSeconds"));
                    if (reset == null && after.HasValue)
                        reset = J.Now + after * 1000;
                    double? used = J.N(J.Get(window, "used_percent") ?? J.Get(window, "usedPercent"));
                    result.Add(Window(prefix + id, label, used, reset, seconds, scope));
                }
            };
            add(J.Get(payload, "rate_limit") ?? J.Get(payload, "rateLimit"), "", "");
            add(J.Get(payload, "code_review_rate_limit") ?? J.Get(payload, "codeReviewRateLimit"), "review-", "代码审查");
            int unnamed = 0;
            foreach (object item in J.A(J.Get(payload, "additional_rate_limits") ?? J.Get(payload, "additionalRateLimits")))
            {
                string label = FirstNonEmpty(J.S(item, "limit_name"), J.S(item, "metered_feature"));
                if (label == "")
                    label = "额外额度" + (++unnamed);
                add(J.Get(item, "rate_limit") ?? J.Get(item, "rateLimit"), "extra-" + J.Hash(label) + "-", label);
            }
            return result;
        }

        /// <summary>Anthropic /api/oauth/usage: every object with a "utilization" field is a window.</summary>
        public static List<QuotaWindow> Claude(object payload)
        {
            var result = new List<QuotaWindow>();
            foreach (var pair in J.D(payload))
            {
                if (pair.Key == "extra_usage" || !J.D(pair.Value).ContainsKey("utilization"))
                    continue;
                string id = pair.Key == "five_hour" ? "5h" : pair.Key == "seven_day" ? "week" : pair.Key;
                string label = id == "5h" || id == "week" ? PeriodLabel(id) : pair.Key.Replace("seven_day_", "一周 · ").Replace('_', ' ');
                double? duration = id == "5h" ? FiveHours : id == "week" ? OneWeek : (double?)null;
                result.Add(Window(id, label, J.N(pair.Value, "utilization"), J.Get(pair.Value, "resets_at"), duration));
            }
            return result;
        }

        /// <summary>
        /// Google cloudcode quota: grouped summary buckets, else per-model quotaInfo, plus Gemini CLI
        /// buckets when geminiCli is set. Remaining fractions are converted to used percentages.
        /// </summary>
        public static List<QuotaWindow> Google(object payload, bool geminiCli = false)
        {
            var result = new List<QuotaWindow>();
            foreach (object group in J.A(J.Get(payload, "groups")))
            {
                string name = FirstNonEmpty(J.S(group, "displayName"), J.S(group, "display_name"));
                int unnamed = 0;
                foreach (object bucket in J.A(J.Get(group, "buckets")))
                {
                    string label = FirstNonEmpty(J.S(bucket, "displayName"), J.S(bucket, "window"));
                    if (label == "")
                        label = "模型额度" + (++unnamed);
                    string id = FirstNonEmpty(J.S(bucket, "bucketId"), J.S(bucket, "bucket_id"), label);
                    result.Add(Window("ag-" + J.Hash(name + ":" + id), label, UsedFromRemaining(bucket), ResetTime(bucket), null, name));
                }
            }
            if (result.Count > 0)
                return result;

            foreach (var model in J.D(J.Get(payload, "models")))
            {
                object quota = J.Get(model.Value, "quotaInfo") ?? J.Get(model.Value, "quota_info");
                if (quota == null)
                    continue;
                double? fraction = J.N(J.Get(quota, "remainingFraction") ?? J.Get(quota, "remaining_fraction") ?? J.Get(quota, "remaining"));
                string label = FirstNonEmpty(J.S(model.Value, "displayName"), J.S(model.Value, "display_name"), model.Key);
                result.Add(Window("model-" + J.Hash(model.Key), label, fraction.HasValue ? 100 - fraction * 100 : null, ResetTime(quota), null, model.Key));
            }

            if (geminiCli)
            {
                int position = 0;
                foreach (object bucket in J.A(J.Get(payload, "buckets")))
                {
                    string name = FirstNonEmpty(J.S(bucket, "modelId"), J.S(bucket, "model_id"));
                    string id = "gemini-" + J.Hash(name + ":" + J.S(bucket, "tokenType") + ":" + (position++));
                    result.Add(Window(id, name, UsedFromRemaining(bucket), ResetTime(bucket), null, name));
                }
            }
            return result;
        }

        /// <summary>Unpacks CPA's api-call envelope, raising ProviderException for non-2xx upstream status.</summary>
        public static object Unwrap(object envelope)
        {
            int status = (int)(J.N(J.Get(envelope, "status_code") ?? J.Get(envelope, "statusCode")) ?? 0);
            if (status < 200 || status >= 300)
            {
                string message = status == 401 ? "账号授权失效，请在CPA重新授权"
                    : status == 429 ? "供应商查询限流，稍后自动重试"
                    : "供应商查询失败（HTTP " + status + "）";
                throw new ProviderException(message, status);
            }
            object body = J.Get(envelope, "body");
            if (body is string)
            {
                try
                {
                    body = J.Parse((string)body);
                }
                catch
                {
                    throw new Exception("供应商返回的JSON无法解析。");
                }
            }
            if (!(body is IDictionary<string, object>))
                throw new Exception("供应商返回格式不兼容。");
            return body;
        }

        /// <summary>Start of the Beijing-time day (or month) containing the given Unix milliseconds.</summary>
        public static long DayStart(long at, bool month)
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(at), ChinaZone);
            var start = new DateTime(local.Year, local.Month, month ? 1 : local.Day, 0, 0, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(start, ChinaZone.GetUtcOffset(start)).ToUnixTimeMilliseconds();
        }

        /// <summary>Copies a CPAMP analytics summary into the day or month fields.</summary>
        public static void History(object payload, HistoryState history, bool month)
        {
            object summary = J.Get(payload, "summary");
            double? tokens = J.N(summary, "total_tokens"), calls = J.N(summary, "total_calls");
            if (!tokens.HasValue || !calls.HasValue)
                throw new Exception("CPAMP统计格式不兼容。");
            if (month)
            {
                history.MonthTokens = tokens;
                history.MonthCalls = calls;
            }
            else
            {
                history.TodayTokens = tokens;
                history.TodayCalls = calls;
            }
        }

        /// <summary>Chinese label of the two standard periods ("5h", "week").</summary>
        public static string PeriodLabel(string id)
        {
            return id == "5h" ? "5小时" : "一周";
        }

        static QuotaWindow Window(string id, string label, double? used, object reset, double? duration = null, string scope = "")
        {
            return new QuotaWindow
            {
                Id = id,
                Label = label,
                Used = used.HasValue ? Math.Max(0, Math.Min(100, used.Value)) : (double?)null,
                ResetAt = J.Epoch(reset),
                Duration = duration,
                Scope = scope,
            };
        }

        static double? UsedFromRemaining(object bucket)
        {
            double? fraction = J.N(J.Get(bucket, "remainingFraction") ?? J.Get(bucket, "remaining_fraction"));
            return fraction.HasValue ? 100 - fraction * 100 : null;
        }

        static object ResetTime(object value)
        {
            return J.Get(value, "resetTime") ?? J.Get(value, "reset_time");
        }

        static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(v => v != "") ?? "";
        }
    }
}
