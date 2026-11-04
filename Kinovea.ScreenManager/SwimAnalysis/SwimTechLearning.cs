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
using System.Security.Cryptography;
using System.Text;
using Kinovea.Services;
using Newtonsoft.Json;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.13.0) One answer of the coach on a criterion the program had measured: the measurement of its check and whether the coach found
    /// the criterion done well (on a list of errors: the error not there). One answer per race, phase, sheet and criterion (Key).
    /// </summary>
    public class SwimTechSample
    {
        public string Key = "";
        public string Check = "";
        public string Criterion = "";
        public string Sheet = "";
        public string Style = "";
        public double Measured;
        public bool Good;
        public string Date = "";

        /// <summary>(2.16.5) The answer's own id: every Kinovea keeps each answer once. Older answers get one from what they are (StableId).</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Id;

        /// <summary>(2.16.5) The Kinovea (its folder of settings) the answer was given in (SwimLearning.Here), null for answers given here before.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Origin;

        /// <summary>(2.16.5) When the answer was given (UTC); older answers: their day (Date).</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? At;
    }

    /// <summary>
    /// (2.16.5) Answers taken away (a score cleared or "not visible": its key; an evaluation deleted: the start of the keys of its answers),
    /// for the other Kinovea too: the answers of the key given before it go there as well.
    /// </summary>
    public class SwimTechForget
    {
        public string Key = "";

        /// <summary>The answers whose key starts with Key (an evaluation deleted).</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Prefix;

        public DateTime At;
        public string Id;

        /// <summary>It came from another Kinovea (it is not sent again).</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Shared;

        /// <summary>It was sent to the Google Sheet.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Sent;
    }

    /// <summary>(2.13.0) How often the program's judgement agrees with the coach on a criterion (answers where it judged).</summary>
    public class SwimTechAgreement
    {
        public int Samples;
        public int Agree;

        /// <summary>(2.16.5) Of them, answers given in the other Kinovea (shared learning).</summary>
        public int Shared;

        /// <summary>At least 10 answers and 9 in 10 agree: the program may score the criterion on its own (also a partly measured one).</summary>
        public bool Reliable
        {
            get { return Samples >= SwimTechLearning.ReliableSamples && Agree * 10 >= Samples * 9; }
        }

        /// <summary>At least 10 answers and fewer than 7 in 10 agree: the program stops proposing the criterion (the coach scores it).</summary>
        public bool Disagrees
        {
            get { return Samples >= SwimTechLearning.ReliableSamples && Agree * 10 < Samples * 7; }
        }

        public string Text
        {
            get { return string.Format(CultureInfo.InvariantCulture, "{0}/{1}", Agree, Samples); }
        }
    }

    /// <summary>(2.13.0) The limit of a check learned from the coach's answers: how many answers it agrees with, and the starting one.</summary>
    public class SwimTechLearnedLimit
    {
        public double? Min;
        public double? Max;
        public int Samples;
        public int Agree;
        public int StartAgree;
    }

    /// <summary>
    /// (2.13.0) What the program learns from the coach. Each answer of the coach on a criterion the program measured (a score he gave, or
    /// an automatic one he confirmed) is kept with the measurement (SwimTechLearning.json in the settings folder; no names: the ids of the
    /// analysis and of the phase). (2.16.5) The answers of every Kinovea connected to the same Google Sheet are shared there (sheet
    /// "Μάθηση τεχνικής"): each Kinovea learns from the coaches of all of them (Merge; SwimAnalysisManager.TechLearningShare). From them:
    /// - for each check the limit that agrees best with the coach: it replaces the starting limit of the catalogue once it agrees with
    ///   clearly more answers (12 answers at least, 3 of each kind);
    /// - for each criterion how often the program agrees with the coach: a criterion the program measures only in part is scored on
    ///   its own once 9 in 10 of at least 10 answers agree; a criterion it measures stops being proposed when fewer than 7 in 10 agree.
    /// </summary>
    public class SwimTechLearning
    {
        public const int ReliableSamples = 10;
        public const int LearnSamples = 12;
        private const int MaxSamplesPerCheck = 400;

        /// <summary>(2.16.5) Answers kept (the oldest go first): the answers of every Kinovea of the team.</summary>
        public const int MaxSamples = 40000;

        /// <summary>(2.16.5) Answers taken away kept (the oldest go first).</summary>
        public const int MaxForgotten = 4000;

        /// <summary>(2.16.5) The kinds of the rows of the shared learning: an answer, an answer taken away.</summary>
        public const string ShareKind = "techeval";
        public const string ShareForgetKind = "techeval~forget";

        public int Version = 1;

        /// <summary>The answers, oldest first (since 2.16.5 in the order they were given, When).</summary>
        public List<SwimTechSample> Samples = new List<SwimTechSample>();

        /// <summary>(2.16.5) The answers taken away here (for the other Kinovea too) and the ones the others took away.</summary>
        public List<SwimTechForget> Forgotten = new List<SwimTechForget>();

        [JsonIgnore]
        private readonly Dictionary<string, SwimTechLearnedLimit> limits = new Dictionary<string, SwimTechLearnedLimit>();
        [JsonIgnore]
        private string path;

        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static SwimTechLearning current;
        private static string currentPath;
        private static DateTime currentWrite;

        /// <summary>Used instead of the file when set (the checks of the program).</summary>
        public static SwimTechLearning Override;

        public static string FilePath
        {
            get
            {
                try
                {
                    string dir = Software.SettingsDirectory;
                    return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "SwimTechLearning.json");
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>The answers of this computer (read again when the file changed), null when there is no settings folder.</summary>
        public static SwimTechLearning Current
        {
            get
            {
                if (Override != null)
                    return Override;

                string p = FilePath;
                if (p == null)
                    return null;

                DateTime write = DateTime.MinValue;
                try
                {
                    if (File.Exists(p))
                        write = File.GetLastWriteTimeUtc(p);
                }
                catch
                {
                }

                if (current == null || currentPath != p || write != currentWrite)
                {
                    current = Load(p) ?? new SwimTechLearning();
                    current.path = p;
                    currentPath = p;
                    currentWrite = write;
                }

                return current;
            }
        }

        private static SwimTechLearning Load(string p)
        {
            try
            {
                if (!File.Exists(p))
                    return null;
                SwimTechLearning l = JsonConvert.DeserializeObject<SwimTechLearning>(File.ReadAllText(p));
                if (l != null)
                    l.Clean();
                return l;
            }
            catch (Exception e)
            {
                log.Error("Could not read the technique learning file.", e);
                return null;
            }
        }

        /// <summary>Without broken answers; (2.16.5) every answer with its id.</summary>
        private void Clean()
        {
            if (Samples == null)
                Samples = new List<SwimTechSample>();
            Samples.RemoveAll(s => !Valid(s));
            foreach (SwimTechSample s in Samples)
                if (string.IsNullOrEmpty(s.Id))
                    s.Id = StableId(s);
            if (Forgotten == null)
                Forgotten = new List<SwimTechForget>();
            Forgotten.RemoveAll(f => f == null || string.IsNullOrEmpty(f.Key) || string.IsNullOrEmpty(f.Id));
        }

        private static bool Valid(SwimTechSample s)
        {
            return s != null && !string.IsNullOrEmpty(s.Key) && !string.IsNullOrEmpty(s.Check) && !double.IsNaN(s.Measured) && !double.IsInfinity(s.Measured);
        }

        /// <summary>
        /// (2.16.5) When an answer was given (UTC): its moment, else its day (answers before 2.16.5), else the oldest.
        /// </summary>
        public static DateTime When(SwimTechSample s)
        {
            if (s == null)
                return DateTime.MinValue;
            if (s.At.HasValue)
                return s.At.Value.Kind == DateTimeKind.Local ? s.At.Value.ToUniversalTime() : DateTime.SpecifyKind(s.At.Value, DateTimeKind.Utc);
            DateTime day;
            if (!string.IsNullOrEmpty(s.Date) && DateTime.TryParseExact(s.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out day))
                return day;
            return DateTime.MinValue;
        }

        private static DateTime Utc(DateTime d)
        {
            return d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc);
        }

        /// <summary>
        /// (2.16.5) The id of an answer older than 2.16.5 from what it is (key, check, measurement, answer, day): the same answer copied to
        /// another Kinovea has the same id there.
        /// </summary>
        public static string StableId(SwimTechSample s)
        {
            string text = string.Join("|", s.Key ?? "", s.Check ?? "", s.Measured.ToString("R", CultureInfo.InvariantCulture), s.Good ? "1" : "0", s.Date ?? "");
            using (SHA1 sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                return "t" + string.Concat(hash.Take(10).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>(2.16.5) An answer taken away covers the answers of its key (or keys) given before it.</summary>
        private static bool Covers(SwimTechForget f, SwimTechSample s)
        {
            bool key = f.Prefix ? s.Key.StartsWith(f.Key, StringComparison.Ordinal) : s.Key == f.Key;
            return key && When(s) <= Utc(f.At);
        }

        private void Save()
        {
            limits.Clear();
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.None));
                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);
                if (current == this)
                    currentWrite = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception e)
            {
                log.Error("Could not save the technique learning file.", e);
            }
        }

        /// <summary>
        /// The answer of the coach (replaces the one with the same key, from any Kinovea). (2.16.5) With its id, this Kinovea and the moment:
        /// it goes to the other Kinovea.
        /// </summary>
        public void Put(SwimTechSample sample)
        {
            if (!Valid(sample))
                return;

            int i = Samples.FindIndex(s => s.Key == sample.Key);
            if (i >= 0)
            {
                SwimTechSample old = Samples[i];
                if (old.Check == sample.Check && old.Good == sample.Good && Math.Abs(old.Measured - sample.Measured) < 1e-9)
                    return;
                Samples.RemoveAt(i);
            }

            sample.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(sample.Origin))
                sample.Origin = SwimLearning.Here;
            // After every answer of this Kinovea (they go to the others in this order).
            DateTime now = DateTime.UtcNow;
            DateTime last = Samples.Where(Own).Select(When).DefaultIfEmpty(DateTime.MinValue).Max();
            sample.At = now > last ? now : last.AddTicks(1);
            Samples.Add(sample);
            Trim();
            Save();
        }

        /// <summary>
        /// Takes away the answers whose key starts so (an answer cleared, an evaluation deleted). (2.16.5) In the other Kinovea too.
        /// </summary>
        public void Remove(string keyOrPrefix, bool prefix)
        {
            if (string.IsNullOrEmpty(keyOrPrefix))
                return;
            int n = Samples.RemoveAll(s => prefix ? s.Key.StartsWith(keyOrPrefix, StringComparison.Ordinal) : s.Key == keyOrPrefix);
            if (n == 0)
                return;

            Forgotten.Add(new SwimTechForget { Key = keyOrPrefix, Prefix = prefix, At = DateTime.UtcNow, Id = Guid.NewGuid().ToString("N") });
            Trim();
            Save();
        }

        /// <summary>(2.16.5) Saved after an exchange of the shared learning (answers taken away sent, ids of this Kinovea).</summary>
        public void SaveShared()
        {
            Save();
        }

        private void Trim()
        {
            if (Samples.Count > MaxSamples)
                Samples.RemoveRange(0, Samples.Count - MaxSamples);
            if (Forgotten.Count > MaxForgotten)
                Forgotten.RemoveRange(0, Forgotten.Count - MaxForgotten);
        }

        /// <summary>(2.16.5) This answer was given in this Kinovea (or here before the shared learning).</summary>
        public static bool Own(SwimTechSample s)
        {
            return s != null && (string.IsNullOrEmpty(s.Origin) || s.Origin == SwimLearning.Here);
        }

        /// <summary>(2.16.5) Answers given in the other Kinovea.</summary>
        public int SharedCount
        {
            get { return Samples.Count(s => !Own(s)); }
        }

        /// <summary>
        /// (2.16.5) The answers of the other Kinovea (shared learning) into this one: each answer once (its id); of one key the latest (a later
        /// answer about the same criterion of the same analysis replaces it, from whichever Kinovea); not the ones given before they were
        /// taken away; an answer taken away takes away the answers of its key given before it. The answers stay in the order they were
        /// given (the latest teach the limits). Saved when something changed. Returns how many answers came in or went.
        /// </summary>
        public int Merge(IEnumerable<SwimTechSample> incoming, IEnumerable<SwimTechForget> forgets)
        {
            List<SwimTechSample> list = new List<SwimTechSample>(Samples);
            HashSet<string> forgetIds = new HashSet<string>(Forgotten.Select(f => f.Id));
            int changed = 0;
            bool keep = false;
            foreach (SwimTechForget f in forgets ?? Enumerable.Empty<SwimTechForget>())
            {
                if (f == null || string.IsNullOrEmpty(f.Key) || string.IsNullOrEmpty(f.Id) || forgetIds.Contains(f.Id))
                    continue;
                SwimTechForget kept = new SwimTechForget { Key = f.Key, Prefix = f.Prefix, At = Utc(f.At), Id = f.Id, Shared = true };
                changed += list.RemoveAll(s => Covers(kept, s));
                Forgotten.Add(kept);
                forgetIds.Add(f.Id);
                keep = true;
            }

            // The latest answer taken away of each key, and the ones of the starts of keys (evaluations deleted).
            Dictionary<string, DateTime> gone = new Dictionary<string, DateTime>();
            List<SwimTechForget> goneStarts = new List<SwimTechForget>();
            foreach (SwimTechForget f in Forgotten)
            {
                if (f.Prefix)
                {
                    goneStarts.Add(f);
                    continue;
                }

                DateTime d;
                if (!gone.TryGetValue(f.Key, out d) || Utc(f.At) > d)
                    gone[f.Key] = Utc(f.At);
            }

            HashSet<string> ids = new HashSet<string>(list.Select(s => s.Id).Where(x => x != null));
            Dictionary<string, SwimTechSample> byKey = new Dictionary<string, SwimTechSample>();
            foreach (SwimTechSample s in list)
                byKey[s.Key] = s;

            bool added = false;
            foreach (SwimTechSample s in incoming ?? Enumerable.Empty<SwimTechSample>())
            {
                if (!Valid(s))
                    continue;
                if (string.IsNullOrEmpty(s.Id))
                    s.Id = StableId(s);
                if (ids.Contains(s.Id))
                    continue;

                DateTime when = When(s), forgot;
                if (gone.TryGetValue(s.Key, out forgot) && when <= forgot)
                    continue;
                if (goneStarts.Any(f => Covers(f, s)))
                    continue;

                SwimTechSample same;
                if (byKey.TryGetValue(s.Key, out same))
                {
                    DateTime sameWhen = When(same);
                    if (sameWhen > when || (sameWhen == when && string.CompareOrdinal(same.Id ?? "", s.Id) >= 0))
                        continue;
                    list.Remove(same);
                    ids.Remove(same.Id);
                }

                if (s.At.HasValue)
                    s.At = Utc(s.At.Value);
                list.Add(s);
                byKey[s.Key] = s;
                ids.Add(s.Id);
                changed++;
                added = true;
            }

            if (changed == 0 && !keep)
                return 0;

            // In the order they were given (stable: answers of the same moment keep their order).
            if (added)
                list = list.OrderBy(When).ToList();
            Samples = list;
            Trim();
            Save();
            return changed;
        }

        /// <summary>The learned limit of a check, null while the answers do not give a clearly better one (and for yes/no checks).</summary>
        public SwimTechLearnedLimit LimitOf(SwimTechCheck c)
        {
            if (c == null || c.Eq.HasValue)
                return null;

            SwimTechLearnedLimit l;
            if (limits.TryGetValue(c.Id, out l))
                return l;

            List<SwimTechSample> s = Samples.Where(x => x.Check == c.Id).ToList();
            if (s.Count > MaxSamplesPerCheck)
                s = s.Skip(s.Count - MaxSamplesPerCheck).ToList();
            l = Learn(c, s);
            limits[c.Id] = l;
            return l;
        }

        /// <summary>
        /// The limit that agrees with the most answers: the side(s) of the starting range moved to a cut between two answers (a check with
        /// no starting range: one side, either). Kept when it agrees with at least max(2, 5%) more answers than the starting one (a check
        /// with no starting range: 85% of them); on a tie the cut nearest to the starting limit.
        /// </summary>
        public static SwimTechLearnedLimit Learn(SwimTechCheck c, List<SwimTechSample> s)
        {
            if (c == null || s == null || c.Eq.HasValue)
                return null;
            int n = s.Count, good = s.Count(x => x.Good);
            if (n < LearnSamples || good < 3 || n - good < 3)
                return null;

            Func<double?, double?, int> agree = (min, max) => s.Count(x => x.Good == ((!min.HasValue || x.Measured >= min.Value - 1e-9) && (!max.HasValue || x.Measured <= max.Value + 1e-9)));
            bool hasRange = c.Min.HasValue || c.Max.HasValue;
            int start = hasRange ? agree(c.Min, c.Max) : 0;

            List<double> values = s.Select(x => x.Measured).Distinct().OrderBy(x => x).ToList();
            List<double> cuts = new List<double>();
            for (int i = 1; i < values.Count; i++)
                cuts.Add(Math.Round(0.5 * (values[i - 1] + values[i]), 4));
            if (cuts.Count == 0)
                return null;

            double median = values[values.Count / 2];
            double? bestMin = c.Min, bestMax = c.Max;
            int best = hasRange ? start : -1;
            double bestDistance = 0;
            Action<double?, double?> consider = (min, max) =>
            {
                int a = agree(min, max);
                double distance = Distance(min, hasRange ? c.Min : median) + Distance(max, hasRange ? c.Max : median);
                if (a > best || (a == best && distance < bestDistance))
                {
                    best = a;
                    bestMin = min;
                    bestMax = max;
                    bestDistance = distance;
                }
            };

            if (!hasRange)
            {
                foreach (double cut in cuts)
                {
                    consider(cut, null);
                    consider(null, cut);
                }
            }
            else if (c.Min.HasValue && c.Max.HasValue)
            {
                List<double?> mins = new List<double?> { c.Min };
                mins.AddRange(cuts.Where(x => x < c.Max.Value).Select(x => (double?)x));
                List<double?> maxs = new List<double?> { c.Max };
                maxs.AddRange(cuts.Where(x => x > c.Min.Value).Select(x => (double?)x));
                foreach (double? min in mins)
                    foreach (double? max in maxs)
                        if (min.Value < max.Value)
                            consider(min, max);
            }
            else if (c.Min.HasValue)
            {
                foreach (double cut in cuts)
                    consider(cut, null);
            }
            else
            {
                foreach (double cut in cuts)
                    consider(null, cut);
            }

            bool better = hasRange ? best >= start + Math.Max(2, (int)Math.Ceiling(0.05 * n)) : best * 100 >= n * 85;
            if (!better)
                return null;

            return new SwimTechLearnedLimit { Min = bestMin, Max = bestMax, Samples = n, Agree = best, StartAgree = start };
        }

        private static double Distance(double? a, double? b)
        {
            if (a.HasValue != b.HasValue)
                return 1e6;
            return a.HasValue ? Math.Abs(a.Value - b.Value) : 0;
        }

        /// <summary>The check with the limit it is judged with now: the learned one when there is one, else the catalogue's.</summary>
        public static SwimTechCheck Effective(SwimTechCheck c)
        {
            SwimTechLearning l = Current;
            SwimTechLearnedLimit limit = l != null ? l.LimitOf(c) : null;
            if (limit == null)
                return c;

            return new SwimTechCheck { Id = c.Id, Kind = c.Kind, Label = c.Label, Unit = c.Unit, Min = limit.Min, Max = limit.Max, Eq = c.Eq };
        }

        /// <summary>How often the program agrees with the coach's answers on a criterion (with the limit it judges with now).</summary>
        public SwimTechAgreement AgreementOf(string criterion, SwimTechCheck check)
        {
            return AgreementOf(new[] { criterion }, check);
        }

        /// <summary>
        /// (2.16.0) The same for a point of a single list: the coach's answers on it and on the points of the older lists it stands for (an
        /// answer keeps whether the technique was good, whatever the list said it as).
        /// </summary>
        public SwimTechAgreement AgreementOf(IEnumerable<string> criteria, SwimTechCheck check)
        {
            SwimTechAgreement a = new SwimTechAgreement();
            HashSet<string> keys = criteria != null ? new HashSet<string>(criteria.Where(k => !string.IsNullOrEmpty(k))) : new HashSet<string>();
            if (keys.Count == 0 || check == null)
                return a;

            foreach (SwimTechSample s in Samples)
            {
                if (s.Criterion == null || !keys.Contains(s.Criterion) || s.Check != check.Id)
                    continue;
                bool? judged = SwimTechEvaluator.Judge(check, s.Measured);
                if (!judged.HasValue)
                    continue;
                a.Samples++;
                if (judged.Value == s.Good)
                    a.Agree++;
                if (!Own(s))
                    a.Shared++;
            }

            return a;
        }
    }
}
