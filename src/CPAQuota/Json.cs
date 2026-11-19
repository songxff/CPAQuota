using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CpaTaskbar
{
    /// <summary>
    /// Loose JSON helpers for the dictionaries produced by JavaScriptSerializer, plus small shared
    /// formatting utilities. Every accessor tolerates missing keys and wrong types by returning
    /// null/empty values instead of throwing.
    /// </summary>
    public static class J
    {
        const int MaxJsonLength = 16 * 1024 * 1024;

        /// <summary>Walks nested objects by key; returns null as soon as a key is missing.</summary>
        public static object Get(object value, params string[] keys)
        {
            foreach (string key in keys)
            {
                object next;
                var typed = value as IDictionary<string, object>;
                if (typed != null)
                {
                    if (!typed.TryGetValue(key, out next))
                        return null;
                }
                else
                {
                    var raw = value as IDictionary;
                    if (raw == null || !raw.Contains(key))
                        return null;
                    next = raw[key];
                }
                value = next;
            }
            return value;
        }

        /// <summary>Value as an invariant string; null becomes "".</summary>
        public static string S(object value)
        {
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static string S(object value, string key)
        {
            return S(Get(value, key));
        }

        /// <summary>Value as a finite number, or null for missing, boolean, blank or non-numeric values.</summary>
        public static double? N(object value)
        {
            if (value == null || value is bool || String.IsNullOrWhiteSpace(S(value)))
                return null;
            double number;
            if (!Double.TryParse(S(value), NumberStyles.Any, CultureInfo.InvariantCulture, out number))
                return null;
            return Double.IsInfinity(number) || Double.IsNaN(number) ? (double?)null : number;
        }

        public static double? N(object value, string key)
        {
            return N(Get(value, key));
        }

        /// <summary>Value as an array; strings, objects and non-sequences become empty.</summary>
        public static object[] A(object value)
        {
            var sequence = value as IEnumerable;
            if (sequence == null || value is string || value is IDictionary<string, object>)
                return new object[0];
            return sequence.Cast<object>().ToArray();
        }

        /// <summary>Value as an object; anything else becomes an empty dictionary.</summary>
        public static IDictionary<string, object> D(object value)
        {
            return value as IDictionary<string, object> ?? new Dictionary<string, object>();
        }

        public static object Parse(string text)
        {
            return new JavaScriptSerializer { MaxJsonLength = MaxJsonLength }.DeserializeObject(text.TrimStart('﻿'));
        }

        public static string Json(object value)
        {
            return new JavaScriptSerializer { MaxJsonLength = MaxJsonLength }.Serialize(value);
        }

        /// <summary>Current time as Unix milliseconds.</summary>
        public static long Now
        {
            get
            {
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }

        /// <summary>
        /// Parses a timestamp to Unix milliseconds. Numbers below 1e12 are treated as seconds;
        /// strings are parsed as dates (UTC when no offset is given).
        /// </summary>
        public static long? Epoch(object value)
        {
            if (value == null)
                return null;
            double? number = N(value);
            if (number.HasValue)
            {
                if (number <= 0)
                    return null;
                return (long?)(number < 1e12 ? number * 1000 : number);
            }
            DateTimeOffset parsed;
            if (DateTimeOffset.TryParse(S(value), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed))
                return parsed.ToUnixTimeMilliseconds();
            return null;
        }

        /// <summary>Short stable identifier: the first 8 bytes of SHA-256 as hex.</summary>
        public static string Hash(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                return String.Concat(digest.Take(8).Select(b => b.ToString("x2")));
            }
        }

        /// <summary>Compact count such as 1.2K, 3.45M or 1.5B; "—" when unknown.</summary>
        public static string Compact(double? value)
        {
            if (!value.HasValue)
                return "—";
            double v = value.Value;
            var culture = CultureInfo.InvariantCulture;
            if (v >= 1e9)
                return (v / 1e9).ToString("0.##", culture) + "B";
            if (v >= 1e6)
                return (v / 1e6).ToString("0.##", culture) + "M";
            if (v >= 1e3)
                return (v / 1e3).ToString("0.#", culture) + "K";
            return v.ToString("0", culture);
        }

        /// <summary>Display name of a provider id.</summary>
        public static string Provider(string provider)
        {
            switch (provider)
            {
                case "codex":
                    return "Codex";
                case "claude":
                    return "Claude";
                case "antigravity":
                    return "Antigravity";
                case "gemini":
                    return "Gemini CLI";
                default:
                    return provider;
            }
        }
    }
}
