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
    public enum SwimPhaseKind
    {
        Start,
        Turn
    }

    /// <summary>
    /// One start or one turn of the technical analysis: the start and the turns of a race, or the attempts of a
    /// training video. Times are in seconds from the origin of the phase: the start signal or the contact with the wall.
    /// </summary>
    public class SwimTechPhase
    {
        /// <summary>Race: "start" or "turn-50". Training: id of the mark that begins the attempt (signal or wall contact).</summary>
        public string Id;
        public SwimPhaseKind Kind;

        /// <summary>Race: distance of the wall (0 for the start). Training: 0.</summary>
        public int Wall;

        /// <summary>Training: number of the attempt of this kind (1, 2, …). Race: 0.</summary>
        public int Attempt;
        public string Title;
        public bool Flip;

        /// <summary>Video timestamp of t = 0 of the phase.</summary>
        public long Origin;

        /// <summary>The marks with a timestamp in [WindowStart, WindowEnd) belong to the phase.</summary>
        public long WindowStart;
        public long WindowEnd;

        // Events, in seconds from the origin (negative before the contact of a turn).
        public double? Reaction;
        public double? HandsOff;
        public double? BackFoot;
        public double? Block;
        public double? Entry;
        public double? LastStroke;
        public double? RotationStart;
        public double? Pushoff;
        public double? Breakout;
        public double? FirstStroke;
        public List<double> Kicks = new List<double>();

        /// <summary>Head at a technical distance: offset (5, 10, 15 or −5, +5, +10, +15) → seconds from the origin.</summary>
        public SortedDictionary<int, double> Splits = new SortedDictionary<int, double>();

        /// <summary>Distances measured on the video (pool units from the wall).</summary>
        public double? EntryDist;
        public double? BreakoutDist;

        /// <summary>Values sent to the report, with the keys of SWIM_TECH_METRICS of the backend.</summary>
        public Dictionary<string, double> Values = new Dictionary<string, double>();

        public double? Value(string key)
        {
            double v;
            return Values.TryGetValue(key, out v) ? (double?)v : null;
        }

        public bool Contains(long timestamp)
        {
            return timestamp >= WindowStart && timestamp < WindowEnd;
        }
    }

    /// <summary>
    /// A technical value: key (as in the backend), label and unit for the panel.
    /// </summary>
    public class SwimTechMetric
    {
        public string Key;
        public string Label;

        /// <summary>s, d (distance), v (m/s), n (count), r (kicks/s).</summary>
        public string Unit;
        public SwimPhaseKind? Kind;

        public SwimTechMetric(string key, string label, string unit, SwimPhaseKind? kind)
        {
            Key = key;
            Label = label;
            Unit = unit;
            Kind = kind;
        }

        public string Format(double value)
        {
            switch (Unit)
            {
                case "n": return value.ToString("0", CultureInfo.InvariantCulture);
                case "s": return value.ToString("0.00", CultureInfo.InvariantCulture);
                default: return value.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// Phases and values of the technical analysis of starts and turns, from the marks of the video.
    /// </summary>
    public static class SwimTechCalculator
    {
        public static readonly SwimTechMetric[] Metrics = new SwimTechMetric[]
        {
            new SwimTechMetric("reaction", "Αντίδραση – πρώτη κίνηση", "s", SwimPhaseKind.Start),
            new SwimTechMetric("handsOff", "Χέρια φεύγουν από τον βατήρα", "s", SwimPhaseKind.Start),
            new SwimTechMetric("backFoot", "Πίσω πόδι φεύγει", "s", SwimPhaseKind.Start),
            new SwimTechMetric("blockTime", "Αποδέσμευση (μπροστινό πόδι)", "s", SwimPhaseKind.Start),
            new SwimTechMetric("blockPhase", "Φάση βατήρα: κίνηση → αποδέσμευση", "s", SwimPhaseKind.Start),
            new SwimTechMetric("flight", "Πτήση: αποδέσμευση → είσοδος", "s", SwimPhaseKind.Start),
            new SwimTechMetric("entryTime", "Είσοδος χεριών", "s", SwimPhaseKind.Start),
            new SwimTechMetric("entryDist", "Απόσταση εισόδου", "d", SwimPhaseKind.Start),
            new SwimTechMetric("in5", "Προσέγγιση −5 → τοίχος", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("lastStrokeGap", "Τελευταία χεριά → τοίχος", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("rotation", "Περιστροφή: αρχή → επαφή", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("contact", "Επαφή → αποκόλληση", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("turnTime", "Χρόνος στροφής", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("t5", "Χρόνος 5", "s", SwimPhaseKind.Start),
            new SwimTechMetric("t10", "Χρόνος 10", "s", SwimPhaseKind.Start),
            new SwimTechMetric("t15", "Χρόνος 15", "s", SwimPhaseKind.Start),
            new SwimTechMetric("out5", "Τοίχος → +5", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("out10", "Τοίχος → +10", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("out15", "Τοίχος → +15", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("inOut5", "−5 → +5", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("inOut10", "−5 → +10", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("inOut15", "−5 → +15", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("v0_5", "Ταχύτητα 0 → 5", "v", null),
            new SwimTechMetric("v5_10", "Ταχύτητα 5 → 10", "v", null),
            new SwimTechMetric("v10_15", "Ταχύτητα 10 → 15", "v", null),
            new SwimTechMetric("underwater", "Υποβρύχια φάση", "s", null),
            new SwimTechMetric("kicks", "Δελφινισμοί", "n", null),
            new SwimTechMetric("kickRate", "Συχνότητα δελφινισμών (/s)", "r", null),
            new SwimTechMetric("kickDist", "Απόσταση ανά δελφινισμό", "d", null),
            new SwimTechMetric("breakoutTime", "Έξοδος από το νερό", "s", null),
            new SwimTechMetric("breakoutDist", "Απόσταση εξόδου", "d", null),
            new SwimTechMetric("uwVel", "Ταχύτητα υποβρύχιας φάσης", "v", null),
            new SwimTechMetric("firstStroke", "Πρώτη χεριά", "s", null)
        };

        /// <summary>Technical distances of a start and of a turn, in the order they happen.</summary>
        public static readonly int[] StartOffsets = new int[] { 5, 10, 15 };
        public static readonly int[] TurnOffsets = new int[] { -5, 5, 10, 15 };

        /// <summary>A training attempt keeps the marks up to this long after its origin.</summary>
        private const double AttemptSeconds = 25;

        /// <summary>Marks of the approach of a turn (last stroke, rotation, −5) are at most this long before the contact.</summary>
        private const double ApproachSeconds = 5;

        public static List<SwimTechPhase> Compute(SwimRace race, SwimComputed computed, SwimTiming timing)
        {
            List<SwimTechPhase> phases = new List<SwimTechPhase>();
            if (race == null || timing == null || !timing.IsValid)
                return phases;

            if (race.IsTraining)
                TrainingPhases(race, timing, phases);
            else
                RacePhases(race, computed, timing, phases);

            foreach (SwimTechPhase phase in phases)
                ComputeValues(race, phase);

            return phases;
        }

        /// <summary>
        /// The phase that contains a video timestamp (or null).
        /// </summary>
        public static SwimTechPhase PhaseAt(List<SwimTechPhase> phases, long timestamp)
        {
            if (phases == null)
                return null;

            return phases.LastOrDefault(p => p.Contains(timestamp));
        }

        public static string Label(SwimTechMetric metric, string unit)
        {
            string label = metric.Label;
            if (label.EndsWith(" 5", StringComparison.Ordinal) || label.EndsWith(" 10", StringComparison.Ordinal) || label.EndsWith(" 15", StringComparison.Ordinal) ||
                label.EndsWith("+5", StringComparison.Ordinal) || label.EndsWith("+10", StringComparison.Ordinal) || label.EndsWith("+15", StringComparison.Ordinal))
                label += unit;

            switch (metric.Unit)
            {
                case "s": return label + " (s)";
                case "d": return label + " (" + unit + ")";
                case "v": return label + " (m/s)";
                default: return label;
            }
        }

        #region Phases
        private static void RacePhases(SwimRace race, SwimComputed computed, SwimTiming timing, List<SwimTechPhase> phases)
        {
            if (!race.StartTimestamp.HasValue || computed == null)
                return;

            long origin = race.StartTimestamp.Value;
            int pool = race.Pool <= 0 ? 50 : race.Pool;
            List<int> walls = new List<int>();
            for (int w = pool; w < race.Distance; w += pool)
                walls.Add(w);

            Func<int, long?> splitAt = d =>
            {
                SwimMark m = race.GetSplit(d);
                return m != null ? (long?)m.Timestamp : null;
            };

            // Boundaries: the start signal and the walls marked, in time order.
            List<long> wallTimes = walls.Select(w => splitAt(w)).Where(t => t.HasValue).Select(t => t.Value).OrderBy(t => t).ToList();
            long? finish = splitAt(race.Distance);
            long end = finish.HasValue ? finish.Value + 1 : long.MaxValue;

            SwimTechPhase start = new SwimTechPhase();
            start.Id = "start";
            start.Kind = SwimPhaseKind.Start;
            start.Wall = 0;
            start.Title = "Εκκίνηση";
            start.Origin = origin;
            start.WindowStart = origin - (long)Math.Round(timing.AverageTimestampsPerFrame);
            start.WindowEnd = wallTimes.Count > 0 ? wallTimes[0] - Before(timing, ApproachSeconds) : end;
            Fill(race, timing, start, wallMarks: true);
            if (race.Reaction.HasValue)
                start.Reaction = race.Reaction.Value;
            start.EntryDist = race.EntryDistHands;
            start.BreakoutDist = race.BreakoutDist;
            ApplyCheckpointSplits(race, timing, start, splitAt);
            phases.Add(start);

            for (int i = 0; i < walls.Count; i++)
            {
                int wall = walls[i];
                long? tw = splitAt(wall);
                if (!tw.HasValue)
                    continue;

                SwimTurnComputed turn = computed.Turns.FirstOrDefault(t => t.Wall == wall);
                SwimTechPhase phase = new SwimTechPhase();
                phase.Id = "turn-" + wall.ToString(CultureInfo.InvariantCulture);
                phase.Kind = SwimPhaseKind.Turn;
                phase.Wall = wall;
                phase.Title = (turn != null && turn.Exchange ? "Αλλαγή " : "Στροφή ") + wall.ToString(CultureInfo.InvariantCulture) + race.Unit;
                phase.Flip = turn != null && turn.Flip;
                phase.Origin = tw.Value;
                phase.WindowStart = tw.Value - Before(timing, ApproachSeconds);
                long next = wallTimes.Where(t => t > tw.Value).DefaultIfEmpty(end).First();
                phase.WindowEnd = Math.Min(next - Before(timing, ApproachSeconds), end);
                if (phase.WindowEnd <= tw.Value)
                    phase.WindowEnd = next;
                Fill(race, timing, phase, wallMarks: true);

                // The turn values of the race analysis (manual turn time, underwater distance) also hold here.
                SwimTurnManual manual = race.GetTurn(wall, false);
                if (manual != null)
                    phase.BreakoutDist = manual.UnderwaterDist;
                if (turn != null && turn.LastStrokeGap.HasValue)
                    phase.LastStroke = -turn.LastStrokeGap.Value;
                // Relay exchange: the next swimmer may leave the block a little before the touch.
                SwimRow wallRow = computed.Rows.FirstOrDefault(r => r.D == wall);
                if (turn != null && turn.Pushoff.HasValue && wallRow != null && wallRow.T.HasValue)
                    phase.Pushoff = turn.Pushoff.Value - wallRow.T.Value;

                ApplyCheckpointSplits(race, timing, phase, splitAt);
                phases.Add(phase);
            }
        }

        /// <summary>
        /// Race: the checkpoints at the technical distances count when there is no technical split
        /// (15 of the start, −5 and +15 of a turn, 20 in a 25 pool).
        /// </summary>
        private static void ApplyCheckpointSplits(SwimRace race, SwimTiming timing, SwimTechPhase phase, Func<int, long?> splitAt)
        {
            int[] offsets = phase.Kind == SwimPhaseKind.Start ? StartOffsets : TurnOffsets;
            foreach (int offset in offsets)
            {
                if (phase.Splits.ContainsKey(offset))
                    continue;

                int d = phase.Wall + offset;
                if (d <= 0 || d >= race.Distance || !race.Checkpoints.Contains(d))
                    continue;

                long? t = splitAt(d);
                if (t.HasValue)
                    phase.Splits[offset] = timing.Seconds(t.Value, phase.Origin);
            }
        }

        private static void TrainingPhases(SwimRace race, SwimTiming timing, List<SwimTechPhase> phases)
        {
            List<SwimMark> origins = race.Marks
                .Where(m => m.Type == SwimMarkType.Signal || m.Type == SwimMarkType.WallContact)
                .OrderBy(m => m.Timestamp)
                .ToList();

            int starts = 0, turns = 0;
            for (int i = 0; i < origins.Count; i++)
            {
                SwimMark o = origins[i];
                SwimTechPhase phase = new SwimTechPhase();
                phase.Id = o.Id;
                phase.Kind = o.Type == SwimMarkType.Signal ? SwimPhaseKind.Start : SwimPhaseKind.Turn;
                phase.Attempt = phase.Kind == SwimPhaseKind.Start ? ++starts : ++turns;
                phase.Title = (phase.Kind == SwimPhaseKind.Start ? "Εκκίνηση " : "Στροφή ") + phase.Attempt.ToString(CultureInfo.InvariantCulture);
                phase.Flip = phase.Kind == SwimPhaseKind.Turn && (race.Style == "fs" || race.Style == "bk");
                phase.Origin = o.Timestamp;
                phases.Add(phase);
            }

            // Windows: a turn begins a little before its contact (approach), a start at its signal; each one ends where the next begins.
            for (int i = 0; i < phases.Count; i++)
            {
                SwimTechPhase p = phases[i];
                long begin = p.Kind == SwimPhaseKind.Turn ? p.Origin - Before(timing, ApproachSeconds) : p.Origin;
                if (i > 0)
                    begin = Math.Max(begin, phases[i - 1].Origin + 1);
                p.WindowStart = begin;
            }

            for (int i = 0; i < phases.Count; i++)
            {
                SwimTechPhase p = phases[i];
                long limit = p.Origin + Before(timing, AttemptSeconds);
                p.WindowEnd = i + 1 < phases.Count ? Math.Min(phases[i + 1].WindowStart, limit) : limit;
                Fill(race, timing, p, wallMarks: false);
                SwimPhaseManual manual = race.GetPhaseValues(p.Id, false);
                if (manual != null)
                {
                    p.EntryDist = manual.EntryDist;
                    p.BreakoutDist = manual.BreakoutDist;
                }
            }
        }

        private static long Before(SwimTiming timing, double seconds)
        {
            // Inverse of SwimTiming.Seconds: timestamps for a duration in race time.
            double hsf = timing.HighSpeedFactor <= 0 ? 1.0 : timing.HighSpeedFactor;
            double frames = seconds * 1000.0 * hsf / timing.FrameIntervalMilliseconds;
            return (long)Math.Round(frames * timing.AverageTimestampsPerFrame);
        }

        /// <summary>
        /// Events of the phase from the marks inside its window. A race uses the marks of its walls
        /// (technical splits with the wall of the phase), a training video the offsets alone.
        /// </summary>
        private static void Fill(SwimRace race, SwimTiming timing, SwimTechPhase p, bool wallMarks)
        {
            List<SwimMark> marks = race.Marks.Where(m => p.Contains(m.Timestamp)).OrderBy(m => m.Timestamp).ToList();
            Func<SwimMark, double> rel = m => timing.Seconds(m.Timestamp, p.Origin);
            Func<SwimMarkType, Func<double, bool>, double?> first = (type, predicate) =>
            {
                foreach (SwimMark m in marks)
                {
                    if (m.Type != type)
                        continue;
                    double t = rel(m);
                    if (predicate(t))
                        return t;
                }

                return null;
            };
            Func<SwimMarkType, Func<double, bool>, double?> last = (type, predicate) =>
            {
                double? found = null;
                foreach (SwimMark m in marks)
                {
                    if (m.Type != type)
                        continue;
                    double t = rel(m);
                    if (predicate(t))
                        found = t;
                }

                return found;
            };

            // Technical splits: in a race the mark says its wall; in training the offset decides (−5 before the contact).
            foreach (SwimMark m in marks.Where(x => x.Type == SwimMarkType.TechSplit))
            {
                if (wallMarks && m.Wall >= 0 && m.Wall != p.Wall)
                    continue;

                double t = rel(m);
                if (p.Kind == SwimPhaseKind.Start && m.Distance > 0 && t > 0)
                    p.Splits[m.Distance] = t;
                else if (p.Kind == SwimPhaseKind.Turn && ((m.Distance < 0 && t <= 0) || (m.Distance > 0 && t > 0)))
                    p.Splits[m.Distance] = t;
            }

            if (p.Kind == SwimPhaseKind.Start)
            {
                p.Reaction = first(SwimMarkType.Reaction, t => t > 0);
                p.HandsOff = first(SwimMarkType.HandsOff, t => t > 0);
                p.BackFoot = first(SwimMarkType.BackFoot, t => t > 0);
                p.Block = first(SwimMarkType.FeetOff, t => t > 0);
                p.Entry = first(SwimMarkType.Entry, t => t > 0);
                double after = p.Entry ?? p.Block ?? 0;
                p.Breakout = first(SwimMarkType.Breakout, t => t > after);
            }
            else
            {
                p.LastStroke = last(SwimMarkType.Stroke, t => t < 0);
                p.RotationStart = last(SwimMarkType.RotationStart, t => t <= 0);
                p.Pushoff = first(SwimMarkType.FeetOff, t => t >= 0);
                double after = p.Pushoff ?? 0;
                p.Breakout = first(SwimMarkType.Breakout, t => t > after);
            }

            double from = p.Kind == SwimPhaseKind.Start ? (p.Entry ?? p.Block ?? 0) : (p.Pushoff ?? 0);
            double to = p.Breakout ?? double.MaxValue;
            p.Kicks = marks.Where(m => m.Type == SwimMarkType.Kick).Select(rel).Where(t => t > from && t <= to).OrderBy(t => t).ToList();
            if (p.Breakout.HasValue)
            {
                double b = p.Breakout.Value;
                p.FirstStroke = first(SwimMarkType.Stroke, t => t > b);
            }
        }
        #endregion

        #region Values
        private static void ComputeValues(SwimRace race, SwimTechPhase p)
        {
            double k = race.K;
            Dictionary<string, double> v = p.Values;
            v.Clear();
            Action<string, double?> set = (key, value) =>
            {
                if (value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value))
                    v[key] = Math.Round(value.Value, 3);
            };
            Func<int, double?> split = offset =>
            {
                double t;
                return p.Splits.TryGetValue(offset, out t) ? (double?)t : null;
            };
            Func<double?, double?, double?> diff = (a, b) => a.HasValue && b.HasValue ? a.Value - b.Value : (double?)null;
            Func<double, double?, double?> vel = (units, time) => time.HasValue && time.Value > 0 ? units * k / time.Value : (double?)null;

            if (p.Kind == SwimPhaseKind.Start)
            {
                set("reaction", p.Reaction);
                set("handsOff", p.HandsOff);
                set("backFoot", p.BackFoot);
                set("blockTime", p.Block);
                set("blockPhase", diff(p.Block, p.Reaction));
                set("flight", diff(p.Entry, p.Block));
                set("entryTime", p.Entry);
                set("entryDist", p.EntryDist);
                double? t5 = split(5), t10 = split(10), t15 = split(15);
                set("t5", t5);
                set("t10", t10);
                set("t15", t15);
                set("v0_5", vel(5, t5));
                set("v5_10", vel(5, diff(t10, t5)));
                set("v10_15", vel(5, diff(t15, t10)));
                set("underwater", diff(p.Breakout, p.Entry));
                if (p.EntryDist.HasValue && p.BreakoutDist.HasValue && p.Kicks.Count > 0 && p.BreakoutDist.Value > p.EntryDist.Value)
                    set("kickDist", (p.BreakoutDist.Value - p.EntryDist.Value) * k / p.Kicks.Count);
                if (p.EntryDist.HasValue && p.BreakoutDist.HasValue && p.Breakout.HasValue && p.Entry.HasValue && p.Breakout.Value > p.Entry.Value)
                    set("uwVel", (p.BreakoutDist.Value - p.EntryDist.Value) * k / (p.Breakout.Value - p.Entry.Value));
            }
            else
            {
                double? tIn = split(-5), o5 = split(5), o10 = split(10), o15 = split(15);
                set("in5", tIn.HasValue ? -tIn.Value : (double?)null);
                set("lastStrokeGap", p.LastStroke.HasValue ? -p.LastStroke.Value : (double?)null);
                if (p.RotationStart.HasValue && p.RotationStart.Value <= 0)
                    set("rotation", -p.RotationStart.Value);
                set("contact", p.Pushoff);
                if (p.Pushoff.HasValue)
                {
                    if (!p.Flip)
                        set("turnTime", p.Pushoff);
                    else if (p.LastStroke.HasValue)
                        set("turnTime", p.Pushoff.Value - p.LastStroke.Value);
                }

                if (!race.IsTraining)
                {
                    SwimTurnManual manual = race.GetTurn(p.Wall, false);
                    if (manual != null && manual.TurnTime.HasValue)
                        set("turnTime", manual.TurnTime);
                }

                set("out5", o5);
                set("out10", o10);
                set("out15", o15);
                set("inOut5", diff(o5, tIn));
                set("inOut10", diff(o10, tIn));
                set("inOut15", diff(o15, tIn));
                set("v0_5", vel(5, o5));
                set("v5_10", vel(5, diff(o10, o5)));
                set("v10_15", vel(5, diff(o15, o10)));
                set("underwater", diff(p.Breakout, p.Pushoff));
                if (p.BreakoutDist.HasValue && p.Kicks.Count > 0)
                    set("kickDist", p.BreakoutDist.Value * k / p.Kicks.Count);
                if (p.BreakoutDist.HasValue && p.Breakout.HasValue && p.Pushoff.HasValue && p.Breakout.Value > p.Pushoff.Value)
                    set("uwVel", p.BreakoutDist.Value * k / (p.Breakout.Value - p.Pushoff.Value));
            }

            if (p.Kicks.Count > 0 || p.Breakout.HasValue)
                set("kicks", p.Kicks.Count);
            if (p.Kicks.Count >= 2)
            {
                double span = p.Kicks[p.Kicks.Count - 1] - p.Kicks[0];
                if (span > 0)
                    set("kickRate", (p.Kicks.Count - 1) / span);
            }

            set("breakoutTime", p.Breakout);
            set("breakoutDist", p.BreakoutDist);
            set("firstStroke", p.FirstStroke);
        }
        #endregion

        #region Next technical split
        /// <summary>
        /// Offset of the next technical split for a press at this timestamp: the first missing distance of the phase
        /// (5, 10, 15 after a start; +5, +10, +15 after a contact), otherwise −5 of the next turn.
        /// </summary>
        public static int NextOffset(List<SwimTechPhase> phases, long timestamp, out SwimTechPhase phase)
        {
            phase = null;
            SwimTechPhase current = phases == null ? null : phases.Where(p => p.Origin <= timestamp && p.Contains(timestamp)).LastOrDefault();
            if (current != null)
            {
                phase = current;
                foreach (int offset in current.Kind == SwimPhaseKind.Start ? StartOffsets : TurnOffsets)
                {
                    if (offset > 0 && !current.Splits.ContainsKey(offset))
                        return offset;
                }
            }

            return -5;
        }
        #endregion
    }
}
