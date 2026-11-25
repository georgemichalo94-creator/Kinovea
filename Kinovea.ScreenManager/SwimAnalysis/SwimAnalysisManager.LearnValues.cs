#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.38.3) The answers of the analyst about the values the program gives, kept in the learning as his answers about the frames
    /// are (numbers only, shared with the other Kinovea in the same way):
    /// - the strokes of a segment: the count of its stroke marks (the automatic count, shown at the checkpoint or on a double click on
    ///   the strokes of the segment) and the count he kept;
    /// - the underwater distance of the start or of a turn: the estimate from the times (≈) and the distance he wrote or measured
    ///   instead (in metres), once the distance has settled (the wheel and the keys change it at each step).
    /// They teach nothing yet: they say how far the automatic reading is from the analyst (the accuracy of a championship) and are
    /// what a later learning of these values starts from. An answer about the same segment or wall replaces the one before it (as the
    /// answers about the same event of a race); a value taken away takes its answer back. Nothing of the analysis changes.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>The strokes of a segment: index the checkpoint at its end; Error the count kept − the count of the marks (strokes).</summary>
        public const string KindRaceStrokes = "race.strokes";

        /// <summary>The underwater distance after the start (index 0) or a turn (index its wall); Error the distance kept − the estimate (m).</summary>
        public const string KindRaceUnderwater = "race.underwater";

        #region Strokes
        /// <summary>
        /// The answer about the strokes of the segment of this row: 'shown' the count of its stroke marks (the automatic count), 'kept' the
        /// count kept (typed, accepted as shown, or the marks again when the typed number is taken away). Only where the automatic reading
        /// counted (automatic marks in the segment, or the lap read now): a count of strokes marked by hand says nothing about it.
        /// </summary>
        private void LearnStrokeCount(SwimRow row, double shown, double kept, double cycle, bool readNow)
        {
            if (row == null || Race == null || Race.IsTraining || string.IsNullOrEmpty(videoPath))
                return;

            int auto, hand;
            StrokeMarksOf(row, out auto, out hand);
            if (auto == 0 && !readNow)
                return;

            Dictionary<string, double> measures = new Dictionary<string, double>();
            measures["from"] = row.PrevD;
            measures["len"] = Math.Round((row.D - row.PrevD) * (Race.K > 0 ? Race.K : 1.0), 2);
            measures["style"] = StyleCode(row.Style);
            measures["auto"] = auto;
            measures["hand"] = hand;
            if (cycle > 0)
                measures["cycle"] = Math.Round(cycle, 3);

            SwimLearningRecord record = ValueAnswer(KindRaceStrokes, SwimLearning.VideoCode(videoPath), Race.Id, row.D, shown, kept, measures, DateTime.Now);
            if (record == null)
                return;

            Learning.Add(record);
            Learning.Save();
            LearningChanged();
        }

        /// <summary>The stroke marks of the segment of this row (from the checkpoint before it, or the start, to its checkpoint): automatic and by hand.</summary>
        private void StrokeMarksOf(SwimRow row, out int auto, out int hand)
        {
            auto = 0;
            hand = 0;
            SwimMark end = Race.GetSplit(row.D);
            SwimMark begin = row.PrevD > 0 ? Race.GetSplit(row.PrevD) : null;
            long? from = row.PrevD > 0 ? (begin != null ? begin.Timestamp : (long?)null) : Race.StartTimestamp;
            if (end == null || !from.HasValue)
                return;

            foreach (SwimMark m in Race.Marks)
            {
                if (m.Type != SwimMarkType.Stroke || m.Timestamp <= from.Value || m.Timestamp > end.Timestamp)
                    continue;
                if (m.Batch != null)
                    auto++;
                else
                    hand++;
            }
        }
        #endregion

        #region Underwater distances
        /// <summary>Quiet this long after the last change of a distance (ms): its answer is kept.</summary>
        private const int ValueSettle = 2000;

        /// <summary>The answers about the underwater distances that wait for the distance to settle, by their key.</summary>
        private readonly Dictionary<string, PendingValue> pendingValues = new Dictionary<string, PendingValue>();
        private System.Windows.Forms.Timer pendingValuesTimer;

        private sealed class PendingValue
        {
            public SwimRace Race;
            public string Video;
            public int Wall;

            /// <summary>The estimate (m): the one there before the first change, or the one of the answer kept before.</summary>
            public double Shown;

            /// <summary>The distance of the analyst now (race units), null if none.</summary>
            public double? Kept;
        }

        /// <summary>
        /// The analyst wrote or measured the underwater distance at this wall (0: the start): 'estimate' the estimate from the times that was
        /// there (null: none), 'kept' his distance now (null: none), in race units. With an estimate, or an answer about it kept before (a
        /// distance changed again), the estimate and his distance are the answer once the distance has settled; no distance takes the
        /// answer back. Without either: nothing (a distance of the analyst only).
        /// </summary>
        internal void LearnUnderwater(int wall, double? estimate, double? kept)
        {
            if (Race == null || Race.IsTraining || string.IsNullOrEmpty(videoPath))
                return;

            string key = ValueKey(SwimLearning.VideoCode(videoPath), Race.Id, KindRaceUnderwater, wall);
            PendingValue p;
            if (!pendingValues.TryGetValue(key, out p))
            {
                double k = Race.K > 0 ? Race.K : 1.0;
                double? shown = estimate.HasValue ? estimate.Value * k : ShownOf(key);
                if (!shown.HasValue)
                    return;

                p = new PendingValue { Race = Race, Video = videoPath, Wall = wall, Shown = shown.Value };
                pendingValues[key] = p;
            }

            p.Kept = kept;
            if (pendingValuesTimer == null)
            {
                pendingValuesTimer = new System.Windows.Forms.Timer { Interval = ValueSettle };
                pendingValuesTimer.Tick += (s, e) => FlushValueAnswers();
            }

            pendingValuesTimer.Stop();
            pendingValuesTimer.Start();
        }

        /// <summary>The answers about the distances that waited: kept (their distance settled, or Kinovea closes).</summary>
        internal void FlushValueAnswers()
        {
            if (pendingValuesTimer != null)
                pendingValuesTimer.Stop();
            if (pendingValues.Count == 0)
                return;

            List<KeyValuePair<string, PendingValue>> all = pendingValues.ToList();
            pendingValues.Clear();
            SwimLearning learning = Learning;
            bool changed = false;
            foreach (KeyValuePair<string, PendingValue> e in all)
            {
                PendingValue p = e.Value;
                if (!p.Kept.HasValue)
                {
                    if (learning.Records.Any(r => r.Key == e.Key))
                    {
                        learning.Forget(e.Key);
                        changed = true;
                    }

                    continue;
                }

                SwimRace race = p.Race;
                double k = race.K > 0 ? race.K : 1.0;
                int pool = Math.Max(1, race.Pool);
                Dictionary<string, double> measures = new Dictionary<string, double>();
                measures["style"] = StyleCode(SwimPresets.StyleAt(race, Math.Min(race.Distance, p.Wall + pool)));
                measures["dive"] = p.Wall == 0 || (race.Relay && race.Distance >= 4 && p.Wall % (race.Distance / 4) == 0) ? 1 : 0;
                if (Math.Abs(k - 1.0) > 1e-9)
                    measures["k"] = k;

                SwimLearningRecord record = ValueAnswer(KindRaceUnderwater, SwimLearning.VideoCode(p.Video), race.Id, p.Wall, p.Shown, p.Kept.Value * k,
                    measures, DateTime.Now);
                if (record == null)
                    continue;

                learning.Add(record);
                changed = true;
            }

            if (!changed)
                return;

            learning.Save();
            LearningChanged();
        }

        /// <summary>The start's distances before a change (for StartChanged): the estimate (≈) and the distance of the analyst.</summary>
        internal double?[] StartDistances()
        {
            if (Race == null)
                return null;
            return new[] { StartEstimate(Race), StartKept(Race) };
        }

        /// <summary>
        /// After a change of the distances of the start (the underwater one, the breakout one): the answer when the distance of the analyst
        /// changed or the estimate is no longer there (written over or taken away).
        /// </summary>
        internal void StartChanged(double?[] before)
        {
            if (before == null || before.Length < 2 || Race == null)
                return;

            double? kept = StartKept(Race);
            bool estimateGone = before[0].HasValue && !StartEstimate(Race).HasValue;
            if (Same(before[1], kept) && !estimateGone)
                return;

            LearnUnderwater(0, before[0], kept);
        }

        /// <summary>The estimate of the start from the times (≈), null if none.</summary>
        private static double? StartEstimate(SwimRace race)
        {
            return race.UnderwaterEstimated && race.UnderwaterDist.HasValue ? race.UnderwaterDist : null;
        }

        /// <summary>The distance of the start of the analyst, as the report takes it: the breakout distance, else the underwater one he wrote.</summary>
        private static double? StartKept(SwimRace race)
        {
            return race.BreakoutDist ?? (race.UnderwaterEstimated ? null : race.UnderwaterDist);
        }
        #endregion

        #region Answers about values
        /// <summary>The value shown of the answer kept about this value (the estimate it was), null if none.</summary>
        private double? ShownOf(string key)
        {
            SwimLearningRecord r = Learning.Records.LastOrDefault(x => x.Key == key);
            double shown;
            if (r == null || r.Measures == null || !r.Measures.TryGetValue("shown", out shown) || !Finite(shown))
                return null;
            return shown;
        }

        /// <summary>The event of an answer about a value, as the answers about the frames (video|race|kind|index, the race id cut at 12).</summary>
        internal static string ValueKey(string video, string raceId, string kind, int index)
        {
            string race = string.IsNullOrEmpty(raceId) ? "" : raceId.Length > 12 ? raceId.Substring(0, 12) : raceId;
            return (video ?? "") + "|" + race + "|" + (kind ?? "") + "|" + index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The answer about a value (numbers only): Error what was kept − what was shown, Right when they are the same (within half a
        /// hundredth); "shown" and "kept" also in its measures (a later answer about the same value keeps the value shown), with the
        /// measures given that are numbers. Null for a kind missing or a value that is not a number.
        /// </summary>
        internal static SwimLearningRecord ValueAnswer(string kind, string video, string raceId, int index, double shown, double kept,
            IDictionary<string, double> measures, DateTime date)
        {
            if (string.IsNullOrEmpty(kind) || !Finite(shown) || !Finite(kept))
                return null;

            Dictionary<string, double> m = new Dictionary<string, double>();
            if (measures != null)
            {
                foreach (KeyValuePair<string, double> p in measures)
                {
                    if (!string.IsNullOrEmpty(p.Key) && Finite(p.Value))
                        m[p.Key] = p.Value;
                }
            }

            m["shown"] = Math.Round(shown, 3);
            m["kept"] = Math.Round(kept, 3);
            double error = Math.Round(kept - shown, 3);
            return new SwimLearningRecord
            {
                Date = date,
                Kind = kind,
                Video = video ?? "",
                Right = Math.Abs(error) < 0.005,
                Error = error,
                Measures = m,
                Key = ValueKey(video, raceId, kind, index)
            };
        }

        /// <summary>The style as a number for the measures of an answer: 1 freestyle, 2 backstroke, 3 breaststroke, 4 butterfly, 0 other.</summary>
        internal static double StyleCode(string style)
        {
            switch (style ?? "")
            {
                case "fs": return 1;
                case "bk": return 2;
                case "br": return 3;
                case "fly": return 4;
                default: return 0;
            }
        }

        private static bool Finite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }
        #endregion
    }
}
