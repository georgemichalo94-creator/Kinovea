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
    /// A mark proposed by the automatic analysis of the video (skeleton and image), waiting for the analyst:
    /// Enter = right, arrows then Enter = corrected, Delete = wrong. Kept in the analysis file until answered.
    /// </summary>
    public class SwimProposal
    {
        public string Id = Guid.NewGuid().ToString("N");
        public SwimMarkType Type;

        /// <summary>Frame proposed (with what was learned from the corrections).</summary>
        public long Timestamp;

        /// <summary>What found it: start.reaction, start.hands, start.back, start.feet, start.entry, under.kick, under.breakout, turn.contact, turn.feet.</summary>
        public string Kind;

        /// <summary>0–1.</summary>
        public double Confidence;

        /// <summary>Order of the event of its kind in the reading (the dolphin kicks: 1, 2, 3…).</summary>
        public int Index;

        /// <summary>The reading of the video that made it (proposals of an older reading of the same part are replaced).</summary>
        public string Batch;

        /// <summary>Frames found by each cue, before what was learned (for the learning when the analyst answers).</summary>
        public Dictionary<string, long> Cues = new Dictionary<string, long>();

        /// <summary>Values measured on the image at the cues (for example the water level of the entry), for the learning.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, double> Measures;
    }

    /// <summary>
    /// One answer of the analyst to a proposal: numbers only (no name, no image), kept on this computer for the learning.
    /// </summary>
    public class SwimLearningRecord
    {
        public DateTime Date;
        public string Kind;

        /// <summary>Short code of the video file (the learning of the same video counts more: same camera).</summary>
        public string Video;

        /// <summary>True: the event is there (at the proposed frame or corrected). False: there is no such event (Delete).</summary>
        public bool Right;

        /// <summary>Frame accepted − frame proposed (s).</summary>
        public double Error;

        /// <summary>Frame accepted − frame of each cue (s).</summary>
        public Dictionary<string, double> Cues = new Dictionary<string, double>();

        /// <summary>Values of the image at the frame accepted (for example the water level of the entry in body sizes).</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, double> Measures;

        public double Confidence;
    }

    /// <summary>What was learned for one cue: its usual delay (to add) and how far from the analyst it is (s).</summary>
    public struct SwimCueLearning
    {
        public double Bias;
        public double Spread;
        public int Count;
    }

    /// <summary>
    /// The learning of the automatic proposals from the answers of the analyst, in the settings folder of this computer
    /// (SwimLearning.json). Only the answers given from now on teach it: each cue learns its usual delay against the frames
    /// the analyst keeps, the cues closer to the analyst count more, and the proposals the analyst deletes lower the confidence.
    /// </summary>
    public class SwimLearning
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Records kept (the oldest go first).</summary>
        public const int MaxRecords = 4000;

        /// <summary>Answers of a kind used for its learning (the latest).</summary>
        public const int Window = 40;

        public int Version = 1;
        public List<SwimLearningRecord> Records = new List<SwimLearningRecord>();

        [JsonIgnore]
        public string Path { get; set; }

        public static string DefaultPath
        {
            get { return System.IO.Path.Combine(Software.SettingsDirectory, "SwimLearning.json"); }
        }

        public static SwimLearning Load(string path = null)
        {
            path = path ?? DefaultPath;
            SwimLearning learning = null;
            try
            {
                if (File.Exists(path))
                    learning = JsonConvert.DeserializeObject<SwimLearning>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e)
            {
                log.Error("Could not read the learning of the swim analysis.", e);
            }

            if (learning == null)
                learning = new SwimLearning();
            if (learning.Records == null)
                learning.Records = new List<SwimLearningRecord>();
            learning.Records.RemoveAll(r => r == null || string.IsNullOrEmpty(r.Kind));
            foreach (SwimLearningRecord r in learning.Records)
            {
                if (r.Cues == null)
                    r.Cues = new Dictionary<string, double>();
            }

            learning.Path = path;
            return learning;
        }

        public void Save()
        {
            try
            {
                string path = Path ?? DefaultPath;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception e)
            {
                log.Error("Could not save the learning of the swim analysis.", e);
            }
        }

        public void Add(SwimLearningRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.Kind))
                return;

            Records.Add(record);
            if (Records.Count > MaxRecords)
                Records.RemoveRange(0, Records.Count - MaxRecords);
        }

        public void Reset()
        {
            Records.Clear();
        }

        /// <summary>Short code of a video file (its name), the same on every computer.</summary>
        public static string VideoCode(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";

            // The name of the file only (folders differ between computers), the same with \ or /.
            string name = path.Substring(Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/')) + 1);
            using (SHA1 sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(name.ToLowerInvariant()));
                return string.Concat(hash.Take(5).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>The latest answers of a kind: of the same video when it has at least 'sameVideo' of them, else of all the videos.</summary>
        private List<SwimLearningRecord> Latest(string kind, string video, bool right, int sameVideo)
        {
            List<SwimLearningRecord> all = Records.Where(r => r.Kind == kind && r.Right == right).ToList();
            if (!string.IsNullOrEmpty(video))
            {
                List<SwimLearningRecord> same = all.Where(r => r.Video == video).ToList();
                if (same.Count >= sameVideo)
                    all = same;
            }

            return all.Skip(Math.Max(0, all.Count - Window)).ToList();
        }

        /// <summary>
        /// The usual delay of a cue against the analyst (added to the cue) and its spread (s). Without answers: no delay and the
        /// spread given; with few answers they move towards what the answers show.
        /// </summary>
        public SwimCueLearning Cue(string kind, string cue, string video, double priorSpread)
        {
            List<double> errors = Latest(kind, video, true, 2).Where(r => r.Cues.ContainsKey(cue)).Select(r => r.Cues[cue]).ToList();
            int n = errors.Count;
            if (n == 0)
                return new SwimCueLearning { Bias = 0, Spread = priorSpread, Count = 0 };

            double median = Median(errors);

            // One answer moves half way, more answers move further (as one imaginary answer with no delay).
            double bias = median * n / (n + 1.0);
            double squares = errors.Sum(e => (e - bias) * (e - bias));
            double spread = Math.Sqrt((squares + 2 * priorSpread * priorSpread) / (n + 2.0));
            return new SwimCueLearning { Bias = bias, Spread = Math.Max(0.004, spread), Count = n };
        }

        /// <summary>
        /// A value measured at the frames kept by the analyst (for example the water level of the entry): in the same video (same
        /// camera) their median, from other videos their median moved towards the fallback, without answers the fallback.
        /// </summary>
        public double Measure(string kind, string name, string video, double fallback, out int count)
        {
            Func<SwimLearningRecord, bool> has = r => r.Kind == kind && r.Right && r.Measures != null && r.Measures.ContainsKey(name) &&
                !double.IsNaN(r.Measures[name]) && !double.IsInfinity(r.Measures[name]);
            List<double> same = string.IsNullOrEmpty(video) ? new List<double>() : Records.Where(r => has(r) && r.Video == video).Select(r => r.Measures[name]).ToList();
            if (same.Count > 0)
            {
                count = same.Count;
                return Median(same.Skip(Math.Max(0, same.Count - Window)).ToList());
            }

            List<double> all = Records.Where(has).Select(r => r.Measures[name]).ToList();
            all = all.Skip(Math.Max(0, all.Count - Window)).ToList();
            count = all.Count;
            if (count == 0)
                return fallback;

            return (Median(all) * count + fallback) / (count + 1.0);
        }

        /// <summary>Part of the proposals of a kind deleted by the analyst lately (0–1), with one imaginary right answer.</summary>
        public double WrongRate(string kind, string video)
        {
            List<SwimLearningRecord> all = Records.Where(r => r.Kind == kind).ToList();
            if (!string.IsNullOrEmpty(video) && all.Count(r => r.Video == video) >= 3)
                all = all.Where(r => r.Video == video).ToList();
            all = all.Skip(Math.Max(0, all.Count - Window)).ToList();
            return all.Count(r => !r.Right) / (all.Count + 1.0);
        }

        /// <summary>Answers of a kind and their mean correction (for the panel).</summary>
        public void Stats(string kind, out int right, out int wrong, out double meanAbsError)
        {
            List<SwimLearningRecord> all = Records.Where(r => r.Kind == kind).ToList();
            right = all.Count(r => r.Right);
            wrong = all.Count - right;
            List<SwimLearningRecord> latest = all.Where(r => r.Right).ToList();
            latest = latest.Skip(Math.Max(0, latest.Count - Window)).ToList();
            meanAbsError = latest.Count == 0 ? 0 : latest.Average(r => Math.Abs(r.Error));
        }

        public static double Median(List<double> values)
        {
            if (values == null || values.Count == 0)
                return 0;

            List<double> v = values.OrderBy(x => x).ToList();
            int n = v.Count;
            return n % 2 == 1 ? v[n / 2] : 0.5 * (v[n / 2 - 1] + v[n / 2]);
        }
    }

    /// <summary>An event found by the detectors: the frames of its cues (samples of the reading, fractions allowed) and how sure they are.</summary>
    public sealed class SwimDetection
    {
        public string Kind;
        public SwimMarkType Type;
        public int Index;

        /// <summary>0–1: how clearly the detector saw the event.</summary>
        public double Confidence;

        /// <summary>Sample of each cue.</summary>
        public readonly Dictionary<string, double> Cues = new Dictionary<string, double>();

        /// <summary>Weight of each cue before any learning (1 if missing) and its usual spread (s).</summary>
        public readonly Dictionary<string, double> Weights = new Dictionary<string, double>();
        public readonly Dictionary<string, double> Spreads = new Dictionary<string, double>();

        /// <summary>Values measured on the image (stored with the proposal, learned at the frame the analyst keeps).</summary>
        public readonly Dictionary<string, double> Measures = new Dictionary<string, double>();

        public SwimDetection(string kind, SwimMarkType type, int index = 0)
        {
            Kind = kind;
            Type = type;
            Index = index;
        }

        public SwimDetection Cue(string name, double sample, double weight, double spread)
        {
            if (double.IsNaN(sample) || double.IsInfinity(sample))
                return this;

            Cues[name] = sample;
            Weights[name] = weight;
            Spreads[name] = spread;
            return this;
        }
    }

    /// <summary>The proposal of a detection with what was learned: each cue moved by its usual delay, the cues closer to the analyst count more.</summary>
    public static class SwimProposalMath
    {
        /// <summary>
        /// The sample proposed and its confidence. samplesPerSecond: samples of the reading per second of the video.
        /// </summary>
        public static double Combine(SwimDetection d, SwimLearning learning, string video, double samplesPerSecond, out double confidence)
        {
            confidence = 0;
            if (d == null || d.Cues.Count == 0 || samplesPerSecond <= 0)
                return double.NaN;

            List<double> values = new List<double>(), weights = new List<double>(), spreads = new List<double>();
            foreach (KeyValuePair<string, double> c in d.Cues)
            {
                double prior = d.Spreads.ContainsKey(c.Key) ? d.Spreads[c.Key] : 0.05;
                SwimCueLearning l = learning != null ? learning.Cue(d.Kind, c.Key, video, prior) : new SwimCueLearning { Spread = prior };
                double w = (d.Weights.ContainsKey(c.Key) ? d.Weights[c.Key] : 1.0) / (l.Spread * l.Spread);
                values.Add(c.Value + l.Bias * samplesPerSecond);
                weights.Add(w);
                spreads.Add(l.Spread * samplesPerSecond);
            }

            // A cue far from the others (more than 3 of its spreads from the weighted median) does not count.
            double median = WeightedMedian(values, weights);
            double sum = 0, total = 0;
            int kept = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (values.Count > 1 && Math.Abs(values[i] - median) > 3 * Math.Max(1.0, spreads[i]))
                    continue;

                sum += weights[i] * values[i];
                total += weights[i];
                kept++;
            }

            double result = total > 0 ? sum / total : median;

            // Confidence: the detector, the agreement of its cues, and the answers of the analyst for this kind.
            double agreement = kept == values.Count ? 1.0 : 0.75;
            double wrong = learning != null ? learning.WrongRate(d.Kind, video) : 0;
            confidence = Math.Max(0, Math.Min(1, d.Confidence * agreement * (1 - 0.8 * wrong)));
            return result;
        }

        public static double WeightedMedian(List<double> values, List<double> weights)
        {
            List<int> order = Enumerable.Range(0, values.Count).OrderBy(i => values[i]).ToList();
            double total = weights.Sum(), run = 0;
            foreach (int i in order)
            {
                run += weights[i];
                if (run >= total / 2)
                    return values[i];
            }

            return values.Count > 0 ? values[order[order.Count - 1]] : double.NaN;
        }
    }
}
