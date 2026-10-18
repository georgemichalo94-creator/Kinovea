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

        /// <summary>
        /// Part of the video shown as this phase (panel, overlay, snapshots and angles): [WindowStart, WindowEnd).
        /// A start begins a little before its signal, a turn with its approach; the phase lasts at least until its last event.
        /// </summary>
        public long WindowStart;
        public long WindowEnd;

        /// <summary>
        /// The events of the phase are searched in [EventStart, EventEnd): from the previous origin to the next one
        /// (the last stroke, rotation and −5 before the contact, the first push-off, breakout and stroke after it).
        /// </summary>
        public long EventStart;
        public long EventEnd;

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

        /// <summary>On the panel a turn begins this long before its contact (approach: −5, last stroke, rotation).</summary>
        private const double ApproachSeconds = 5;

        /// <summary>On the panel a start begins this long before its signal (position on the block).</summary>
        private const double LeadSeconds = 3;

        /// <summary>Training: the approach of a turn is searched this long before its contact (after the previous attempt).</summary>
        private const double ApproachSearchSeconds = 10;

        /// <summary>The key T marks 5/10/15 (+5/+10/+15) up to this long after the signal or the contact, then −5 of the next turn.</summary>
        private const double PositiveSeconds = 15;

        /// <summary>The first stroke is at most this long after the breakout, the last stroke at most this long before the contact.</summary>
        private const double FirstStrokeSeconds = 2.5;
        private const double LastStrokeSeconds = 4;

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
            Func<int, long?> splitAt = d =>
            {
                SwimMark m = race.GetSplit(d);
                return m != null ? (long?)m.Timestamp : null;
            };

            long? finish = splitAt(race.Distance);
            long end = finish.HasValue ? finish.Value + 1 : long.MaxValue;

            SwimTechPhase start = new SwimTechPhase();
            start.Id = "start";
            start.Kind = SwimPhaseKind.Start;
            start.Wall = 0;
            start.Title = "Εκκίνηση";
            start.Origin = origin;
            phases.Add(start);

            // Contacts in time order: the start signal and every wall marked. A relay exchange bounds the phases
            // around it but is not a turn (the next swimmer starts from the block).
            List<KeyValuePair<long, SwimTechPhase>> contacts = new List<KeyValuePair<long, SwimTechPhase>>();
            contacts.Add(new KeyValuePair<long, SwimTechPhase>(origin, start));
            foreach (int wall in TurnWalls(race))
            {
                long? tw = splitAt(wall);
                if (!tw.HasValue || tw.Value <= origin)
                    continue;

                if (IsExchange(race, wall))
                {
                    contacts.Add(new KeyValuePair<long, SwimTechPhase>(tw.Value, null));
                    continue;
                }

                SwimTurnComputed turn = computed.Turns.FirstOrDefault(t => t.Wall == wall);
                SwimTechPhase phase = new SwimTechPhase();
                phase.Id = "turn-" + wall.ToString(CultureInfo.InvariantCulture);
                phase.Kind = SwimPhaseKind.Turn;
                phase.Wall = wall;
                phase.Title = "Στροφή " + wall.ToString(CultureInfo.InvariantCulture) + race.Unit;
                phase.Flip = turn != null && turn.Flip;
                phase.Origin = tw.Value;
                phases.Add(phase);
                contacts.Add(new KeyValuePair<long, SwimTechPhase>(tw.Value, phase));
            }

            contacts.Sort((a, b) => a.Key.CompareTo(b.Key));
            long lead = Before(timing, LeadSeconds);
            for (int i = 0; i < contacts.Count; i++)
            {
                SwimTechPhase p = contacts[i].Value;
                if (p == null)
                    continue;

                p.EventStart = p == start ? origin - lead : contacts[i - 1].Key + 1;
                p.EventEnd = i + 1 < contacts.Count ? contacts[i + 1].Key : end;
                Fill(race, timing, p, true);

                if (p == start)
                {
                    if (race.Reaction.HasValue)
                        p.Reaction = race.Reaction.Value;
                    p.EntryDist = race.EntryDistHands;
                    p.BreakoutDist = race.BreakoutDist;
                }
                else
                {
                    // The turn values of the race analysis (underwater distance, last stroke, push-off) also hold here.
                    SwimTurnManual manual = race.GetTurn(p.Wall, false);
                    if (manual != null)
                        p.BreakoutDist = manual.UnderwaterDist;

                    SwimTurnComputed turn = computed.Turns.FirstOrDefault(t => t.Wall == p.Wall);
                    if (turn != null && turn.LastStrokeGap.HasValue)
                        p.LastStroke = -turn.LastStrokeGap.Value;
                    SwimRow wallRow = computed.Rows.FirstOrDefault(r => r.D == p.Wall);
                    if (turn != null && turn.Pushoff.HasValue && wallRow != null && wallRow.T.HasValue && turn.Pushoff.Value >= wallRow.T.Value)
                        p.Pushoff = turn.Pushoff.Value - wallRow.T.Value;
                }

                ApplyCheckpointSplits(race, timing, p, splitAt);
            }

            // What the panel shows: the approach of a turn (at most half of the length) after the last event of the previous phase.
            for (int i = 0; i < contacts.Count; i++)
            {
                SwimTechPhase p = contacts[i].Value;
                if (p == null)
                    continue;

                p.WindowStart = p == start ? origin - lead : Boundary(timing, contacts[i - 1].Value, contacts[i - 1].Key, p, p.Origin);
                p.WindowEnd = i + 1 < contacts.Count ? Boundary(timing, p, p.Origin, contacts[i + 1].Value, contacts[i + 1].Key) : end;
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
                {
                    double seconds = timing.Seconds(t.Value, phase.Origin);
                    if ((offset < 0 && seconds <= 0) || (offset > 0 && seconds > 0))
                        phase.Splits[offset] = seconds;
                }
            }
        }

        private static void TrainingPhases(SwimRace race, SwimTiming timing, List<SwimTechPhase> phases)
        {
            List<SwimMark> origins = race.Marks
                .Where(m => m.Type == SwimMarkType.Signal || m.Type == SwimMarkType.WallContact)
                .OrderBy(m => m.Timestamp)
                .ToList();

            int starts = 0, turns = 0;
            foreach (SwimMark o in origins)
            {
                SwimTechPhase phase = new SwimTechPhase();
                phase.Id = o.Id;
                phase.Kind = o.Type == SwimMarkType.Signal ? SwimPhaseKind.Start : SwimPhaseKind.Turn;
                phase.Attempt = phase.Kind == SwimPhaseKind.Start ? ++starts : ++turns;
                phase.Title = (phase.Kind == SwimPhaseKind.Start ? "Εκκίνηση " : "Στροφή ") + phase.Attempt.ToString(CultureInfo.InvariantCulture);
                phase.Flip = phase.Kind == SwimPhaseKind.Turn && (race.Style == "fs" || race.Style == "bk");
                phase.Origin = o.Timestamp;
                phases.Add(phase);
            }

            // Events: the approach after the previous attempt, the rest until the next attempt (at most 25 s).
            long lead = Before(timing, LeadSeconds);
            long search = Before(timing, ApproachSearchSeconds);
            long attempt = Before(timing, AttemptSeconds);
            for (int i = 0; i < phases.Count; i++)
            {
                SwimTechPhase p = phases[i];
                long before = p.Origin - (p.Kind == SwimPhaseKind.Start ? lead : search);
                p.EventStart = i > 0 ? Math.Max(phases[i - 1].Origin + 1, before) : before;
                p.EventEnd = Math.Min(i + 1 < phases.Count ? phases[i + 1].Origin : long.MaxValue, p.Origin + attempt);
                Fill(race, timing, p, false);
                SwimPhaseManual manual = race.GetPhaseValues(p.Id, false);
                if (manual != null)
                {
                    p.EntryDist = manual.EntryDist;
                    p.BreakoutDist = manual.BreakoutDist;
                }
            }

            long frame = Frame(timing);
            for (int i = 0; i < phases.Count; i++)
            {
                SwimTechPhase p = phases[i];
                long approach = Math.Max(Before(timing, ApproachSeconds), Before(timing, -FirstApproachEvent(p)) + frame);
                long begin = p.Origin - (p.Kind == SwimPhaseKind.Start ? lead : approach);
                long finish = Math.Max(p.Origin + attempt, p.Origin + Before(timing, LastAfterEvent(p)) + frame);
                p.WindowStart = i > 0 ? Math.Max(begin, Boundary(timing, phases[i - 1], phases[i - 1].Origin, p, p.Origin)) : begin;
                p.WindowEnd = i + 1 < phases.Count ? Math.Min(finish, Boundary(timing, p, p.Origin, phases[i + 1], phases[i + 1].Origin)) : finish;
            }
        }

        /// <summary>
        /// Where the panel passes from one phase to the next (b begins at bTime): the approach of a turn or the lead of a start,
        /// at most half of the time between them, earlier for an approach marked before it (slow −5),
        /// never before the last event of the previous phase and never after the contact.
        /// </summary>
        private static long Boundary(SwimTiming timing, SwimTechPhase a, long aTime, SwimTechPhase b, long bTime)
        {
            long lead = Before(timing, b != null && b.Kind == SwimPhaseKind.Start ? LeadSeconds : ApproachSeconds);
            // One frame of margin around the events (the seconds of the marks are rounded to frames).
            long frame = Frame(timing);
            long boundary = Math.Max(bTime - lead, aTime + (bTime - aTime) / 2);
            long afterA = long.MinValue;
            if (a != null)
            {
                double last = LastAfterEvent(a);
                if (last > 0)
                    afterA = aTime + Before(timing, last) + frame;
            }

            boundary = Math.Max(boundary, afterA);
            double approach = b != null ? FirstApproachEvent(b) : 0;
            if (approach < 0)
            {
                long approachStart = bTime - Before(timing, -approach) - frame;
                if (approachStart < boundary)
                    boundary = Math.Max(approachStart, afterA);
            }

            // Walls marked in the wrong order: the windows still follow each other.
            boundary = Math.Max(boundary, aTime + 1);
            return Math.Min(boundary, bTime);
        }

        /// <summary>Seconds (negative) of the first event of the approach of a turn (−5, last stroke, rotation), 0 without one.</summary>
        private static double FirstApproachEvent(SwimTechPhase p)
        {
            double first = 0;
            double tIn;
            if (p.Splits.TryGetValue(-5, out tIn) && tIn < first)
                first = tIn;
            if (p.LastStroke.HasValue && p.LastStroke.Value < first)
                first = p.LastStroke.Value;
            if (p.RotationStart.HasValue && p.RotationStart.Value < first)
                first = p.RotationStart.Value;
            return first;
        }

        /// <summary>Seconds of the last event after the origin of a phase (push-off, kicks, breakout, first stroke, 5/10/15).</summary>
        private static double LastAfterEvent(SwimTechPhase p)
        {
            double last = 0;
            foreach (double? v in new double?[] { p.Block, p.Entry, p.Pushoff, p.Breakout, p.FirstStroke })
            {
                if (v.HasValue && v.Value > last)
                    last = v.Value;
            }

            if (p.Kicks.Count > 0 && p.Kicks[p.Kicks.Count - 1] > last)
                last = p.Kicks[p.Kicks.Count - 1];
            foreach (KeyValuePair<int, double> split in p.Splits)
            {
                if (split.Key > 0 && split.Value > last)
                    last = split.Value;
            }

            return last;
        }

        private static long Before(SwimTiming timing, double seconds)
        {
            // Inverse of SwimTiming.Seconds: timestamps for a duration in race time.
            double hsf = timing.HighSpeedFactor <= 0 ? 1.0 : timing.HighSpeedFactor;
            double frames = seconds * 1000.0 * hsf / timing.FrameIntervalMilliseconds;
            return (long)Math.Round(frames * timing.AverageTimestampsPerFrame);
        }

        /// <summary>Timestamps for a duration in race time (seconds).</summary>
        public static long Ticks(SwimTiming timing, double seconds)
        {
            return timing == null || !timing.IsValid ? 0 : Before(timing, seconds);
        }

        private static long Frame(SwimTiming timing)
        {
            return Math.Max(1, (long)Math.Round(timing.AverageTimestampsPerFrame));
        }

        /// <summary>Walls where the swimmer turns or a relay leg ends.</summary>
        public static List<int> TurnWalls(SwimRace race)
        {
            List<int> walls = new List<int>();
            int pool = race == null || race.Pool <= 0 ? 50 : race.Pool;
            if (race == null)
                return walls;

            for (int w = pool; w < race.Distance; w += pool)
                walls.Add(w);
            return walls;
        }

        /// <summary>Relay: the end of a leg is an exchange, not a turn (as in SwimCalculator).</summary>
        public static bool IsExchange(SwimRace race, int wall)
        {
            return race.Relay && race.Distance % 4 == 0 && wall % (race.Distance / 4) == 0;
        }

        /// <summary>
        /// Race: the last wall already touched at a timestamp (0 before the first one). Tolerance: a wall up to this much later is
        /// touched too (half a frame: the walls of the auto pilot fall between two frames).
        /// </summary>
        public static int LastWallBefore(SwimRace race, long timestamp, long tolerance = 0)
        {
            int wall = 0;
            foreach (int w in TurnWalls(race))
            {
                SwimMark split = race.GetSplit(w);
                if (split != null && split.Timestamp <= timestamp + tolerance)
                    wall = w;
            }

            return wall;
        }

        /// <summary>Race: the technical distance is a checkpoint of the race (marked with the split key, not with T).</summary>
        public static bool IsCheckpoint(SwimRace race, int wall, int offset)
        {
            int d = wall + offset;
            return !race.IsTraining && d > 0 && d < race.Distance && race.Checkpoints.Contains(d);
        }

        /// <summary>
        /// Events of the phase from the marks of its event window. In a race the technical splits say their wall,
        /// in a training video the offset decides (−5 before the contact, the others after it).
        /// </summary>
        private static void Fill(SwimRace race, SwimTiming timing, SwimTechPhase p, bool wallMarks)
        {
            List<SwimMark> marks = race.Marks.Where(m => m.Timestamp >= p.EventStart && m.Timestamp < p.EventEnd).OrderBy(m => m.Timestamp).ToList();
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

            IEnumerable<SwimMark> techSplits = wallMarks
                ? race.Marks.Where(m => m.Type == SwimMarkType.TechSplit && (m.Wall == p.Wall || (m.Wall < 0 && m.Timestamp >= p.EventStart && m.Timestamp < p.EventEnd)))
                    .OrderBy(m => m.Timestamp)
                : marks.Where(m => m.Type == SwimMarkType.TechSplit);
            foreach (SwimMark m in techSplits)
            {
                double t = rel(m);
                if ((m.Distance < 0 && t <= 0 && p.Kind == SwimPhaseKind.Turn) || (m.Distance > 0 && t > 0))
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
                p.LastStroke = last(SwimMarkType.Stroke, t => t < 0 && t >= -LastStrokeSeconds);
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
                // A stroke much later is not the first one (it was not marked).
                double b = p.Breakout.Value;
                p.FirstStroke = first(SwimMarkType.Stroke, t => t > b && t <= b + FirstStrokeSeconds);
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
                // Only in the order they happen (a mark on a wrong frame would give a negative time).
                if (p.Block.HasValue && p.Reaction.HasValue && p.Block.Value > p.Reaction.Value)
                    set("blockPhase", p.Block.Value - p.Reaction.Value);
                if (p.Entry.HasValue && p.Block.HasValue && p.Entry.Value > p.Block.Value)
                    set("flight", p.Entry.Value - p.Block.Value);
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
                // Distances are in pool units (μ. or yd), velocities in m/s.
                if (p.EntryDist.HasValue && p.BreakoutDist.HasValue && p.Kicks.Count > 0 && p.BreakoutDist.Value > p.EntryDist.Value)
                    set("kickDist", (p.BreakoutDist.Value - p.EntryDist.Value) / p.Kicks.Count);
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
                    set("kickDist", p.BreakoutDist.Value / p.Kicks.Count);
                if (p.BreakoutDist.HasValue && p.Breakout.HasValue && p.Pushoff.HasValue && p.Breakout.Value > p.Pushoff.Value)
                    set("uwVel", p.BreakoutDist.Value * k / (p.Breakout.Value - p.Pushoff.Value));
            }

            // 0 kicks only when kicks are marked in this video (otherwise they were not counted).
            if (p.Kicks.Count > 0 || (p.Breakout.HasValue && race.Marks.Any(m => m.Type == SwimMarkType.Kick)))
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
        /// Offset of the next technical split for a press of T at this timestamp: the next distance of the phase not marked yet
        /// (5, 10, 15 after a signal; +5, +10, +15 after a contact), then −5 before the next wall. The checkpoints of a race
        /// are skipped (they are marked with the split key). Null when there is nothing left to mark with T.
        /// </summary>
        public static int? NextOffset(SwimRace race, SwimTiming timing, List<SwimTechPhase> phases, long timestamp, out SwimTechPhase phase)
        {
            phase = null;
            if (race == null || timing == null || !timing.IsValid || phases == null)
                return null;

            SwimTechPhase current = OwnerOf(phases, timestamp);
            if (current != null && current.Contains(timestamp))
            {
                double t = timing.Seconds(timestamp, current.Origin);
                if (t > 0 && t <= PositiveSeconds)
                {
                    foreach (int offset in current.Kind == SwimPhaseKind.Start ? StartOffsets : TurnOffsets)
                    {
                        if (offset <= 0 || current.Splits.ContainsKey(offset) || IsCheckpoint(race, current.Wall, offset))
                            continue;

                        // Passed already: a farther distance is marked before this press.
                        if (current.Splits.Any(s => s.Key > offset && s.Value <= t))
                            continue;

                        // Too late for this distance at the pace of the previous ones (e.g. +15 not visible on the video):
                        // the press is for the next wall.
                        if (t > Latest(race, current, offset))
                            break;

                        phase = current;
                        return offset;
                    }
                }
            }

            if (race.IsTraining)
            {
                // The approach of the next contact (if it is marked already).
                phase = phases.FirstOrDefault(p => p.Kind == SwimPhaseKind.Turn && p.Origin >= timestamp && p.Contains(timestamp));
                return -5;
            }

            int pool = race.Pool <= 0 ? 50 : race.Pool;
            int wall = LastWallBefore(race, timestamp) + pool;
            if (wall >= race.Distance || IsExchange(race, wall) || IsCheckpoint(race, wall, -5))
                return null;

            phase = phases.FirstOrDefault(p => p.Kind == SwimPhaseKind.Turn && p.Wall == wall);
            return -5;
        }

        /// <summary>
        /// Latest plausible time of the head at a distance, from the times at the previous distances of the phase.
        /// In a 25 pool the −5 of the next wall comes only two intervals of 5 later, so the margin is smaller.
        /// </summary>
        private static double Latest(SwimRace race, SwimTechPhase p, int offset)
        {
            bool shortPool = race.Pool > 0 && race.Pool <= 25;
            double tp, tpp;
            bool hasPrevious = p.Splits.TryGetValue(offset - 5, out tp);
            bool hasBefore = p.Splits.TryGetValue(offset - 10, out tpp);
            if (hasPrevious && hasBefore && tp > tpp)
            {
                double interval = tp - tpp;
                if (shortPool)
                    return tp + Math.Max(1.5, (p.Kind == SwimPhaseKind.Start ? 2.0 : 1.5) * interval);
                return tp + Math.Max(3.0, 3 * interval);
            }

            if (hasPrevious && tp > 0)
                return tp + Math.Max(3.0, 2.5 * tp);
            return PositiveSeconds;
        }

        /// <summary>The phase whose origin (signal or contact) is the last one before a timestamp, while its events last.</summary>
        public static SwimTechPhase OwnerOf(List<SwimTechPhase> phases, long timestamp)
        {
            if (phases == null)
                return null;

            return phases.Where(p => p.Origin <= timestamp && timestamp < p.EventEnd).OrderBy(p => p.Origin).LastOrDefault();
        }
        #endregion
    }
}
