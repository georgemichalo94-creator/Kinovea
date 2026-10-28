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
    }

    /// <summary>(2.13.0) How often the program's judgement agrees with the coach on a criterion (answers where it judged).</summary>
    public class SwimTechAgreement
    {
        public int Samples;
        public int Agree;

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
    /// an automatic one he confirmed) is kept with the measurement, on this computer only (SwimTechLearning.json in the settings folder;
    /// no names: the ids of the analysis and of the phase). From them:
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

        public int Version = 1;
        public List<SwimTechSample> Samples = new List<SwimTechSample>();

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
                if (l != null && l.Samples == null)
                    l.Samples = new List<SwimTechSample>();
                if (l != null)
                    l.Samples.RemoveAll(s => s == null || string.IsNullOrEmpty(s.Key) || string.IsNullOrEmpty(s.Check) || double.IsNaN(s.Measured) || double.IsInfinity(s.Measured));
                return l;
            }
            catch (Exception e)
            {
                log.Error("Could not read the technique learning file.", e);
                return null;
            }
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

        /// <summary>The answer of the coach (replaces the one with the same key).</summary>
        public void Put(SwimTechSample sample)
        {
            if (sample == null || string.IsNullOrEmpty(sample.Key) || string.IsNullOrEmpty(sample.Check) || double.IsNaN(sample.Measured) || double.IsInfinity(sample.Measured))
                return;

            int i = Samples.FindIndex(s => s.Key == sample.Key);
            if (i >= 0)
            {
                SwimTechSample old = Samples[i];
                if (old.Check == sample.Check && old.Good == sample.Good && Math.Abs(old.Measured - sample.Measured) < 1e-9)
                    return;
                Samples[i] = sample;
            }
            else
            {
                Samples.Add(sample);
            }

            Save();
        }

        /// <summary>Takes away the answers whose key starts so (an answer cleared, an evaluation deleted).</summary>
        public void Remove(string keyOrPrefix, bool prefix)
        {
            if (string.IsNullOrEmpty(keyOrPrefix))
                return;
            int n = Samples.RemoveAll(s => prefix ? s.Key.StartsWith(keyOrPrefix, StringComparison.Ordinal) : s.Key == keyOrPrefix);
            if (n > 0)
                Save();
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
            SwimTechAgreement a = new SwimTechAgreement();
            if (string.IsNullOrEmpty(criterion) || check == null)
                return a;

            foreach (SwimTechSample s in Samples)
            {
                if (s.Criterion != criterion || s.Check != check.Id)
                    continue;
                bool? judged = SwimTechEvaluator.Judge(check, s.Measured);
                if (!judged.HasValue)
                    continue;
                a.Samples++;
                if (judged.Value == s.Good)
                    a.Agree++;
            }

            return a;
        }
    }
}
