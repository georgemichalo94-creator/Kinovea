#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.19.0) AI pro: the password of its owner, asked once in a session of Kinovea. Only a salted PBKDF2-SHA256 of it is here: the
    /// password itself is in no file and no repository.
    /// </summary>
    public static class SwimAiGate
    {
        private const string Salt = "f12d7f30271e591e472aab2041cea0f6";
        private const string Hash = "3cc6feaa2185b1820f059d3ef7e93798459724d905cb7e74c255147880913b7a";
        private const int Iterations = 120000;

        /// <summary>After this many wrong passwords in a row, a wait before the next try.</summary>
        private const int MaxFailures = 5;
        private const int WaitAfterFailures = 30;

        private static int failures;
        private static DateTime lockedUntil = DateTime.MinValue;

        /// <summary>The password was given in this session of Kinovea.</summary>
        public static bool Unlocked { get; private set; }

        /// <summary>Seconds to wait before another try (after 5 wrong passwords), 0 when it can be tried.</summary>
        public static int WaitSeconds
        {
            get
            {
                double s = (lockedUntil - DateTime.Now).TotalSeconds;
                return s > 0 ? (int)Math.Ceiling(s) : 0;
            }
        }

        public static bool Unlock(string password)
        {
            if (Unlocked)
                return true;
            if (WaitSeconds > 0)
                return false;

            if (Verify(password, Salt, Hash, Iterations))
            {
                Unlocked = true;
                failures = 0;
                return true;
            }

            failures++;
            if (failures >= MaxFailures)
            {
                failures = 0;
                lockedUntil = DateTime.Now.AddSeconds(WaitAfterFailures);
            }

            return false;
        }

        public static void Lock()
        {
            Unlocked = false;
        }

        /// <summary>The password (spaces at its ends do not count) gives this PBKDF2-SHA256 with this salt (hex), compared in constant time.</summary>
        public static bool Verify(string password, string saltHex, string hashHex, int iterations)
        {
            if (string.IsNullOrEmpty(password) || iterations < 1)
                return false;

            byte[] salt = FromHex(saltHex);
            byte[] expected = FromHex(hashHex);
            if (salt == null || expected == null || salt.Length < 8 || expected.Length < 16)
                return false;

            byte[] actual;
            using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password.Trim()), salt, iterations, HashAlgorithmName.SHA256))
                actual = kdf.GetBytes(expected.Length);

            int diff = 0;
            for (int i = 0; i < expected.Length; i++)
                diff |= actual[i] ^ expected[i];
            return diff == 0;
        }

        private static byte[] FromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0)
                return null;

            byte[] b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++)
            {
                if (!byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b[i]))
                    return null;
            }

            return b;
        }
    }

    /// <summary>
    /// (2.19.0) The key of the Claude API of the owner: kept in the settings protected for his Windows user (DPAPI), so that no other user
    /// and no other computer can read it. Kinovea never shows it whole.
    /// </summary>
    public static class SwimAiKey
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("kinovea-swim-ai-pro");

        public static string Protect(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "";
            byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(data);
        }

        /// <summary>The key kept in the settings, null when there is none or it cannot be read (another user or computer).</summary>
        public static string Unprotect(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return null;

            try
            {
                byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser);
                string key = Encoding.UTF8.GetString(data);
                return key.Length > 0 ? key : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The key as it may be shown: its start and its last 4 characters ("sk-ant-…a1b2").</summary>
        public static string Masked(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "";
            string tail = key.Length > 8 ? key.Substring(key.Length - 4) : "";
            return (key.StartsWith("sk-ant-", StringComparison.Ordinal) ? "sk-ant-" : "") + "…" + tail;
        }

        /// <summary>A key of the Claude API as the Console gives it (sk-ant-…, no spaces).</summary>
        public static bool LooksValid(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;
            string k = key.Trim();
            return k.StartsWith("sk-ant-", StringComparison.Ordinal) && k.Length >= 40 && !k.Any(char.IsWhiteSpace);
        }
    }

    /// <summary>(2.19.0) The models of AI pro and the prices of the Claude API (US dollars per million tokens, November 2026).</summary>
    public static class SwimAiModels
    {
        public const string Opus = "claude-opus-5-5";
        public const string Sonnet = "claude-sonnet-5-5";
        public static readonly string[] All = new string[] { Opus, Sonnet };

        /// <summary>The included API credits of the Claude Max plan of the owner, per month (US dollars).</summary>
        public const double MonthlyCredits = 100;

        public static string Label(string model)
        {
            if (model == Opus)
                return SwimLang.T("Claude Opus 5.5 (η καλύτερη ανάλυση)");
            if (model == Sonnet)
                return SwimLang.T("Claude Sonnet 5.5 (φθηνότερη, πιο γρήγορη)");
            return model ?? "";
        }

        /// <summary>The short name in the notes of the report.</summary>
        public static string Short(string model)
        {
            if (string.IsNullOrEmpty(model))
                return "Claude";
            if (model.StartsWith("claude-opus-5-5", StringComparison.Ordinal))
                return "Claude Opus 5.5";
            if (model.StartsWith("claude-sonnet-5-5", StringComparison.Ordinal))
                return "Claude Sonnet 5.5";
            return model;
        }

        public static void Price(string model, out double input, out double output)
        {
            string m = model ?? "";
            if (m.StartsWith("claude-sonnet", StringComparison.Ordinal))
            {
                input = 2;
                output = 10;
            }
            else if (m.StartsWith("claude-haiku", StringComparison.Ordinal))
            {
                input = 0.10;
                output = 0.50;
            }
            else if (m.StartsWith("claude-fable", StringComparison.Ordinal))
            {
                input = 10;
                output = 50;
            }
            else
            {
                input = 4;
                output = 20;
            }
        }

        public static double Usd(string model, long inputTokens, long outputTokens)
        {
            double pi, po;
            Price(model, out pi, out po);
            return (inputTokens * pi + outputTokens * po) / 1e6;
        }

        /// <summary>The tokens of an image (patches of 28 × 28 pixels, at most 4,784 for the newest models).</summary>
        public static int ImageTokens(int width, int height)
        {
            if (width <= 0 || height <= 0)
                return 0;
            int t = (int)(Math.Ceiling(width / 28.0) * Math.Ceiling(height / 28.0));
            return Math.Min(4784, t);
        }

        /// <summary>The cost of a request before it goes: its images, its text and an answer of about this length.</summary>
        public static double Estimate(string model, IEnumerable<SwimAiImage> images, int textChars, int answerTokens, out long inputTokens)
        {
            long t = 1800 + textChars / 3;
            if (images != null)
            {
                foreach (SwimAiImage i in images)
                    t += ImageTokens(i.Width, i.Height);
            }

            inputTokens = t;
            return Usd(model, t, answerTokens);
        }

        public static string Dollars(double usd)
        {
            return "$" + usd.ToString(usd < 10 ? "0.00" : "0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>(2.19.0) What AI pro spent in a month (kept in the settings by month, "yyyy-MM").</summary>
    public class SwimAiUsage
    {
        public int Requests;
        public long Input;
        public long Output;
        public double Usd;
    }

    public static class SwimAiBook
    {
        public static string MonthKey(DateTime when)
        {
            return when.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        public static SwimAiUsage Month(Dictionary<string, SwimAiUsage> book, DateTime when)
        {
            SwimAiUsage u;
            if (book != null && book.TryGetValue(MonthKey(when), out u) && u != null)
                return u;
            return new SwimAiUsage();
        }

        /// <summary>A request answered (well or not: the tokens of an answer are paid either way). Months older than a year go.</summary>
        public static void Add(Dictionary<string, SwimAiUsage> book, DateTime when, SwimAiReply reply)
        {
            if (book == null || reply == null || (reply.InputTokens <= 0 && reply.OutputTokens <= 0))
                return;

            string key = MonthKey(when);
            SwimAiUsage u;
            if (!book.TryGetValue(key, out u) || u == null)
            {
                u = new SwimAiUsage();
                book[key] = u;
            }

            u.Requests++;
            u.Input += reply.InputTokens;
            u.Output += reply.OutputTokens;
            u.Usd = Math.Round(u.Usd + reply.Usd, 4);

            string oldest = MonthKey(when.AddMonths(-12));
            foreach (string k in book.Keys.Where(k => string.CompareOrdinal(k, oldest) < 0).ToList())
                book.Remove(k);
        }
    }

    /// <summary>
    /// (2.19.0) What the coach gives about the athlete for AI pro, kept with the analysis. Never his name: the requests have no name of
    /// a person (the analysis of the video keeps the name for the report, on this computer).
    /// </summary>
    public class SwimAiAthlete
    {
        public string Gender = "";
        public string Category = "";
        public string Age = "";
        public string Height = "";
        public string Best = "";
        public string Level = "";
        public string Focus = "";
    }

    /// <summary>(2.19.0) Claude's answer on a point of a list (SwimTechEval.Ai): 1 ✓, 0 ✗, -1 not visible (a list of errors: 1 the error is there).</summary>
    public class SwimAiMark
    {
        public int Value;
        public double Sure;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? Moment;

        public string Why = "";
    }

    /// <summary>(2.19.0) A moment of the video sent to Claude: its number, its time in the phase and what it is (the moments of the list).</summary>
    public class SwimAiMoment
    {
        public int N;
        public long Timestamp;
        public double Seconds;
        public List<string> Anchors = new List<string>();
        public string Label = "";
    }

    /// <summary>(2.19.0) The frame of a screen at a moment (JPEG).</summary>
    public class SwimAiImage
    {
        public int Moment;
        public int Screen;
        public int Width;
        public int Height;
        public byte[] Jpeg;
    }

    /// <summary>(2.19.0) The answer of the Claude API to a request of AI pro.</summary>
    public class SwimAiReply
    {
        public bool Ok;
        public bool Cancelled;
        public string Error;
        public int Status;
        public string Model;
        public string StopReason;
        public string Tool;
        public JObject Input;
        public string Text = "";
        public long InputTokens;
        public long OutputTokens;
        public double Usd;
    }

    /// <summary>
    /// (2.19.0) One request to the Claude API (Messages, https, TLS 1.2): on a thread of the pool, its answer on the thread that started
    /// it. A busy API (429, 529) is asked again after the time it says (up to twice). Cancel aborts it.
    /// </summary>
    public sealed class SwimAiCall
    {
        public const string Url = "https://api.anthropic.com/v1/messages";
        public const string ApiVersion = "2023-06-01";

        /// <summary>Where the requests go: the Claude API (a fake API on this computer in the tests).</summary>
        internal static string Endpoint = Url;
        private const int Timeout = 300000;

        private volatile HttpWebRequest request;
        private volatile bool cancelled;

        public bool Cancelled
        {
            get { return cancelled; }
        }

        public void Cancel()
        {
            cancelled = true;
            HttpWebRequest r = request;
            if (r == null)
                return;
            try
            {
                r.Abort();
            }
            catch (Exception)
            {
            }
        }

        public void Start(string key, JObject body, Action<SwimAiReply> done)
        {
            SynchronizationContext ui = SynchronizationContext.Current ?? new SynchronizationContext();
            string json = body.ToString(Formatting.None);
            string model = (string)body["model"] ?? "";
            Task.Factory.StartNew(() => Send(key, json, model)).ContinueWith(t =>
            {
                SwimAiReply r = t.IsFaulted ? new SwimAiReply { Error = t.Exception.GetBaseException().Message, Model = model } : t.Result;
                ui.Post(_ => done(r), null);
            });
        }

        public SwimAiReply Send(string key, string json, string model)
        {
            for (int attempt = 0; ; attempt++)
            {
                int wait;
                SwimAiReply r = SendOnce(key, json, model, out wait);
                if (r.Ok || cancelled || attempt >= 2 || wait <= 0)
                    return r;
                for (int i = 0; i < Math.Min(60, wait) * 10 && !cancelled; i++)
                    Thread.Sleep(100);
                if (cancelled)
                    return new SwimAiReply { Cancelled = true, Error = SwimLang.T("Ακυρώθηκε."), Model = model };
            }
        }

        private SwimAiReply SendOnce(string key, string json, string model, out int retryAfter)
        {
            retryAfter = 0;
            if (cancelled)
                return new SwimAiReply { Cancelled = true, Error = SwimLang.T("Ακυρώθηκε."), Model = model };

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (Exception)
            {
            }

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Endpoint);
            request = req;
            try
            {
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Accept = "application/json";
                req.UserAgent = "Kinovea-Swim-AI-pro";
                req.Headers["x-api-key"] = (key ?? "").Trim();
                req.Headers["anthropic-version"] = ApiVersion;
                req.Timeout = Timeout;
                req.ReadWriteTimeout = Timeout;
                byte[] data = Encoding.UTF8.GetBytes(json);
                req.ContentLength = data.Length;
                using (Stream s = req.GetRequestStream())
                    s.Write(data, 0, data.Length);

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    SwimAiReply r = Parse(rd.ReadToEnd(), model);
                    r.Status = (int)resp.StatusCode;
                    return r;
                }
            }
            catch (WebException e)
            {
                if (cancelled)
                    return new SwimAiReply { Cancelled = true, Error = SwimLang.T("Ακυρώθηκε."), Model = model };

                HttpWebResponse resp = e.Response as HttpWebResponse;
                if (resp == null)
                    return new SwimAiReply { Error = SwimLang.T("Χωρίς σύνδεση με τον Claude (") + e.Status + SwimLang.T("). Έλεγξε το ίντερνετ."), Model = model };

                string text = "";
                try
                {
                    using (StreamReader rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        text = rd.ReadToEnd();
                }
                catch (Exception)
                {
                }

                int code = (int)resp.StatusCode;
                int seconds;
                string after = resp.Headers["retry-after"];
                SwimAiReply r = ErrorReply(code, text, model);
                if (!r.Cancelled && (code == 429 || code == 529 || code >= 500) && !SpendLimit(text))
                    retryAfter = int.TryParse(after, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds) ? Math.Max(1, seconds) : (code == 429 ? 20 : 5);
                return r;
            }
            catch (Exception e)
            {
                if (cancelled)
                    return new SwimAiReply { Cancelled = true, Error = SwimLang.T("Ακυρώθηκε."), Model = model };
                return new SwimAiReply { Error = e.Message, Model = model };
            }
            finally
            {
                request = null;
            }
        }

        /// <summary>The answer of the API: the JSON of the evaluation (structured outputs: in its text), the tokens and their cost.</summary>
        public static SwimAiReply Parse(string text, string model)
        {
            SwimAiReply r = new SwimAiReply { Model = model };
            JObject o;
            try
            {
                o = JObject.Parse(text ?? "");
            }
            catch (Exception)
            {
                r.Error = SwimLang.T("Η απάντηση του Claude δεν διαβάστηκε.");
                return r;
            }

            if ((string)o["type"] == "error")
                return ErrorReply(400, text, model);

            JObject usage = o["usage"] as JObject;
            if (usage != null)
            {
                r.InputTokens = ((long?)usage["input_tokens"] ?? 0) + ((long?)usage["cache_creation_input_tokens"] ?? 0) + ((long?)usage["cache_read_input_tokens"] ?? 0);
                r.OutputTokens = (long?)usage["output_tokens"] ?? 0;
            }

            r.Model = (string)o["model"] ?? model;
            r.StopReason = (string)o["stop_reason"] ?? "";
            r.Usd = SwimAiModels.Usd(r.Model, r.InputTokens, r.OutputTokens);
            JArray content = o["content"] as JArray;
            if (content != null)
            {
                foreach (JToken c in content)
                {
                    string type = (string)c["type"];
                    if (type == "tool_use" && r.Input == null)
                    {
                        r.Tool = (string)c["name"];
                        r.Input = c["input"] as JObject;
                    }
                    else if (type == "text")
                    {
                        r.Text += (string)c["text"] ?? "";
                    }
                }
            }

            // The answer as JSON in its text (structured outputs); a tool's input when a model answered with one.
            if (r.Input == null && r.Text.Trim().Length > 0)
            {
                r.Input = JsonOf(r.Text);
                if (r.Input != null)
                    r.Tool = "json";
            }

            if (r.StopReason == "max_tokens")
            {
                r.Error = SwimLang.T("Η απάντηση του Claude κόπηκε (πολύ μεγάλη). Δοκίμασε λιγότερα καρέ.");
                r.Input = null;
                return r;
            }

            if (r.StopReason == "refusal")
            {
                r.Error = SwimLang.T("Ο Claude δεν έδωσε απάντηση σε αυτή την αίτηση.");
                r.Input = null;
                return r;
            }

            if (r.Input == null)
            {
                r.Error = SwimLang.T("Ο Claude δεν έδωσε αξιολόγηση.") + (r.Text.Length > 0 ? " " + Shorten(r.Text, 200) : "");
                return r;
            }

            r.Ok = true;
            return r;
        }

        /// <summary>The JSON object of an answer's text (between its first { and its last }, without the fences of a code block).</summary>
        internal static JObject JsonOf(string text)
        {
            string t = (text ?? "").Trim();
            int a = t.IndexOf('{'), b = t.LastIndexOf('}');
            if (a < 0 || b <= a)
                return null;
            try
            {
                return JObject.Parse(t.Substring(a, b - a + 1));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool SpendLimit(string text)
        {
            return (text ?? "").IndexOf("enforced_spend_limit_reached", StringComparison.Ordinal) >= 0;
        }

        /// <summary>The error of the API in the words of the coach.</summary>
        public static SwimAiReply ErrorReply(int code, string text, string model)
        {
            SwimAiReply r = new SwimAiReply { Status = code, Model = model };
            string message = "";
            try
            {
                JObject o = JObject.Parse(text ?? "");
                message = (string)o.SelectToken("error.message") ?? "";
            }
            catch (Exception)
            {
            }

            string m = message.ToLowerInvariant();
            if (m.Contains("credit balance") || m.Contains("credits"))
                r.Error = SwimLang.T("Δεν υπάρχουν credits στο API (τελείωσαν, ή δεν συνδέθηκαν ακόμη με το Max: claude.ai › Settings › Billing › API credits).");
            else if (code == 401)
                r.Error = SwimLang.T("Το κλειδί API δεν είναι σωστό (401). Βάλε ξανά το κλειδί από το Claude Console.");
            else if (code == 403)
                r.Error = SwimLang.T("Το κλειδί δεν έχει δικαίωμα για αυτό (403). ") + Shorten(message, 160);
            else if (code == 404)
                r.Error = SwimLang.T("Δεν βρέθηκε το μοντέλο ") + model + " (404).";
            else if (code == 413)
                r.Error = SwimLang.T("Η αίτηση είναι πολύ μεγάλη (413): λιγότερα καρέ ή κανονική λεπτομέρεια.");
            else if (code == 429 && SpendLimit(text))
                r.Error = SwimLang.T("Έφτασες το μηνιαίο όριο του API. Θα ξαναδουλέψει τον επόμενο μήνα.");
            else if (code == 429)
                r.Error = SwimLang.T("Πολλές αιτήσεις μαζί (429): ξαναδοκίμασε σε λίγο.");
            else if (code == 529 || code >= 500)
                r.Error = SwimLang.T("Ο Claude είναι φορτωμένος αυτή τη στιγμή (") + code.ToString(CultureInfo.InvariantCulture) + SwimLang.T("): ξαναδοκίμασε σε λίγο.");
            else
                r.Error = SwimLang.T("Ο Claude δεν δέχτηκε την αίτηση (") + code.ToString(CultureInfo.InvariantCulture) + "): " + Shorten(message, 200);
            return r;
        }

        internal static string Shorten(string text, int max)
        {
            string t = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return t.Length <= max ? t : t.Substring(0, max - 1) + "…";
        }
    }
}
