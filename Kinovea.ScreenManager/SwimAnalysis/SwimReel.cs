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
    public enum SwimReelTone
    {
        Accent,
        Amber,
        Green
    }

    /// <summary>One stroke cycle of the technical view.</summary>
    public sealed class SwimReelCycle
    {
        /// <summary>Stroke marks that begin and end the cycle (s after the start).</summary>
        public double Start;
        public double Time;

        /// <summary>Duration of the cycle (s), distance covered in it (m) and its speed (m/s).</summary>
        public double Tempo;
        public double Dps;
        public double Speed;

        /// <summary>
        /// (2.33.0) What the card and the chart show for the cycle: the median duration and distance of it and the cycles beside it in the
        /// length (three; the first and the last of the length with the two after or before them). A stroke a little before or after its
        /// frame (an automatic one read a few frames off) makes one cycle short and the next long by as much (70 and 42 cycles a minute
        /// around 52): the median is the swimmer's. In the clean swimming of a segment whose strokes the analyst corrected (the automatic
        /// strokes missed one or found one too many), the rate and the cycle length of the segment, as the analysis and the PDF have them.
        /// </summary>
        public double SteadyTempo;
        public double SteadyDps;

        /// <summary>
        /// (2.34.0) The cycle is in a segment of clean swimming: what it shows is the rate and the cycle length of that segment as the
        /// analysis and the PDF have them (every such segment, not only the corrected ones as in 2.33.0).
        /// </summary>
        public bool FromAnalysis;

        /// <summary>(2.33.0) The steady duration, else the cycle's own (s).</summary>
        public double ShownTempo
        {
            get { return SteadyTempo > 0 ? SteadyTempo : Tempo; }
        }

        /// <summary>(2.33.0) The steady distance, else the cycle's own (m).</summary>
        public double ShownDps
        {
            get { return SteadyDps > 0 ? SteadyDps : Dps; }
        }

        /// <summary>Lap (0 = first length) and number of the cycle in the lap (1 = first).</summary>
        public int Lap;
        public int Number;
    }

    /// <summary>Event shown as a badge in the technical view (start, breakouts, checkpoints).</summary>
    public sealed class SwimReelBadge
    {
        public double Time;
        public string Title;
        public string Value;
        public string Detail;
        public SwimReelTone Tone;
    }

    /// <summary>A part of the race for the summary card: half of a single length, a length, or a quarter of a long race.</summary>
    public sealed class SwimReelPart
    {
        public int From;
        public int To;
        public double? Time;

        /// <summary>(2.15.0) The strokes of the part as the analysis counts them (the analyst's counts too), else the marks in it.</summary>
        public double Strokes;
        public double? Tempo;
        public double? Dps;
        public double? Speed;

        /// <summary>
        /// (2.34.0) Tempo and Dps are those of the clean swimming of the part as the analysis and the PDF have them (the rate weighted
        /// with the time, the cycle length of the whole); false: the mean of the cycles marked in the part (no clean swimming in it).
        /// </summary>
        public bool FromAnalysis;
    }

    /// <summary>
    /// (2.34.0) A segment of clean swimming as the analysis and the PDF have it: its checkpoints, their times on the video (s after the
    /// start), the stroke rate (cycles a minute), the length of the cycle (m) and the speed (m/s). What the video shows in it.
    /// </summary>
    public sealed class SwimReelClean
    {
        public int From;
        public int To;
        public double Start;
        public double End;
        public double Rate;
        public double Length;
        public double Speed;
    }

    /// <summary>(2.31.0) A point of the speed of the race on the curve through the checkpoints, for the chart of the whole race.</summary>
    public sealed class SwimReelSample
    {
        /// <summary>Time on the video (s after the start), distance of the race (pool units) and speed (m/s, as SpeedAt).</summary>
        public double Time;
        public double Distance;
        public double Speed;

        /// <summary>Lap (0 = first length): the line of the chart is broken at each wall.</summary>
        public int Lap;

        /// <summary>Under the water (after the start or a turn, before the breakout).</summary>
        public bool Underwater;
    }

    /// <summary>
    /// (2.31.0) An underwater part of the race with its breakout marked: after the start from the entry (marked, else about where the
    /// swimmer enters the water), after a turn from the push off the wall (the feet off marked, else a little after the touch).
    /// </summary>
    public sealed class SwimReelSpan
    {
        public int Lap;

        /// <summary>Times on the video (s after the start): the swimmer in the water, the breakout.</summary>
        public double From;
        public double To;

        /// <summary>Distances of the race (pool units) of the green band of the chart: after the start the entry, after a turn the wall; the breakout.</summary>
        public double FromDistance;
        public double ToDistance;
    }

    /// <summary>
    /// Technical view of a race (as the swimming reels): distance-time model through the checkpoints, the stroke cycles
    /// with their tempo, distance and speed, the phase at each moment, the badges and the summary of the race.
    /// Times from the marks of the analysis (the automatic strokes included); the distances between the checkpoints
    /// come from a smooth monotone curve through them (an estimate, shown with ≈).
    /// </summary>
    public sealed class SwimReel
    {
        // (2.31.0) The curve is on the times of the race (knotT: the analysis' times, the official one where it was typed, as the
        // sheet and the PDF), the swimmer on the frames of the marks (knotV: the video). They differ only where an official time is not
        // on the frame of its mark: the video goes from one to the other in proportion between two checkpoints. Before, the curve was on
        // the frames: a finish marked 0.75 s after the official time made the last 5 m of a 100 m backstroke 1.32 m/s on the video
        // (1.64 in the PDF) and pulled the 85–95 m down with them (the fictional race of reel_speed/rs_ci.ps1).
        private readonly List<double> knotT = new List<double>();
        private readonly List<double> knotV = new List<double>();
        private readonly List<double> knotD = new List<double>();

        // (2.32.0) A new piece of the curve starts at the knot (each wall; the release from the block, the entry, the push off a wall): its
        // speed is not joined to the one before it.
        private readonly List<bool> knotBreak = new List<bool>();

        // Derivative of the curve at the beginning and at the end of each interval between two knots (2.9.4: they differ at a wall,
        // where the curve is broken: the turn).
        private double[] slopeFrom = new double[0];
        private double[] slopeTo = new double[0];

        // The first checkpoint after the last one with a time: the swimmer is not shown beyond it before it is marked.
        private double nextCheckpoint;

        /// <summary>Meters per pool unit.</summary>
        public double K = 1.0;
        public string UnitShort = "m";
        public int Distance;
        public int Pool = 50;

        /// <summary>Final time (s) on the video (the frame of the finish), null until the finish is marked.</summary>
        public double? Finish;

        /// <summary>
        /// The final time shown: the official one of the results when it was typed (since 2026-10-19, up to a frame and a half from
        /// Finish), else Finish.
        /// </summary>
        public double? FinalTime;

        /// <summary>Half a frame (s): the finish between two frames (the pilot's, the official time) is on the frame nearest to it.</summary>
        public double FrameSlack;

        /// <summary>
        /// Time of each wall of the race (the turns, not the finish): marked, or from the curve when a later checkpoint is
        /// marked, else NaN (not reached yet).
        /// </summary>
        public readonly List<double> Walls = new List<double>();
        public readonly List<SwimReelCycle> Cycles = new List<SwimReelCycle>();
        public readonly List<SwimReelBadge> Badges = new List<SwimReelBadge>();
        public readonly List<SwimReelPart> Parts = new List<SwimReelPart>();

        /// <summary>(2.34.0) The segments of clean swimming with the numbers of the analysis, in order (CleanAt).</summary>
        public readonly List<SwimReelClean> Clean = new List<SwimReelClean>();

        /// <summary>Stroke marks closer to their neighbours than a cycle (a second press on the same stroke): left out.</summary>
        public readonly List<double> DoubleMarks = new List<double>();

        /// <summary>Stroke marks used for the cycles, by lap.</summary>
        private readonly List<List<double>> lapStrokes = new List<List<double>>();

        /// <summary>
        /// (2.34.0) The strokes of each lap as the analysis counts them, for the counter of the video: lapStrokes before the double marks
        /// are left out (the analysis counts every mark; the data check says the double ones).
        /// </summary>
        private readonly List<List<double>> lapCounted = new List<List<double>>();
        private readonly List<string> lapStyles = new List<string>();
        private readonly List<double> lapBreakouts = new List<double>();
        private readonly List<double> lapStarts = new List<double>();
        private readonly List<double> lapFeetOff = new List<double>();

        public double? Entry;
        public double? FirstBreakout;

        /// <summary>Breakout distances from the wall (pool units) by lap, measured by the analyst or estimated (≈).</summary>
        public readonly Dictionary<int, double> BreakoutDistances = new Dictionary<int, double>();
        public readonly HashSet<int> BreakoutMeasured = new HashSet<int>();

        /// <summary>
        /// (2.31.0) The speed of the race on the curve while the swimmer is in the water (the chart from the start to the finish): from the
        /// entry of the start and from the push off each wall to the next wall, up to the last checkpoint marked.
        /// </summary>
        public readonly List<SwimReelSample> Samples = new List<SwimReelSample>();

        /// <summary>(2.31.0) The underwater parts of the race whose breakout is marked (the speed shown with ≈, the green bands of the chart).</summary>
        public readonly List<SwimReelSpan> UnderwaterSpans = new List<SwimReelSpan>();

        // (2.31.0) From when the swimmer is in the water in each lap (s on the video, NaN: not known).
        private readonly List<double> lapInWater = new List<double>();

        public bool HasModel
        {
            get { return knotT.Count >= 2; }
        }

        #region Build
        public static SwimReel Build(SwimRace race, SwimComputed computed)
        {
            if (race == null || computed == null || race.IsTraining || !race.StartTimestamp.HasValue || race.Distance <= 0)
                return null;

            SwimReel reel = new SwimReel();
            reel.K = computed.K;
            reel.UnitShort = race.IsYards ? "yd" : "m";
            reel.Distance = race.Distance;
            reel.Pool = Math.Max(1, race.Pool);

            // Distance-time model: the start and the checkpoints with a time, all increasing. The curve on the times of the race (T, the
            // official one where typed), the swimmer on the video at OnVideo (2.32.0: T too, the official time also when the mark is a
            // little before or after it; until 2.31.0 the frame of the mark).
            reel.knotT.Add(0);
            reel.knotV.Add(0);
            reel.knotD.Add(0);
            reel.knotBreak.Add(false);
            foreach (SwimRow row in computed.Rows.Where(r => r.T.HasValue).OrderBy(r => r.OnVideo))
            {
                double raceTime = row.T.Value;
                double video = double.IsNaN(row.OnVideo) ? raceTime : row.OnVideo;
                int last = reel.knotT.Count - 1;
                if (video <= reel.knotV[last] + 1e-6 || raceTime <= reel.knotT[last] + 1e-6 || row.D <= reel.knotD[last])
                    continue;

                reel.knotT.Add(raceTime);
                reel.knotV.Add(video);
                reel.knotD.Add(row.D);
                reel.knotBreak.Add(row.D < race.Distance && row.D % reel.Pool == 0);
            }

            reel.BuildSlopes();
            SwimRow finish = computed.Rows.FirstOrDefault(r => r.D == race.Distance && r.T.HasValue);
            if (finish != null)
            {
                reel.Finish = finish.OnVideo;
                reel.FinalTime = finish.T.Value;
                reel.FrameSlack = finish.FrameSlack;
            }

            double lastD = reel.knotD[reel.knotD.Count - 1];
            reel.nextCheckpoint = computed.Rows.Where(r => r.D > lastD).Select(r => (double)r.D).DefaultIfEmpty(race.Distance).Min();

            // One time for each wall, also for a wall not marked between two checkpoints that are (the laps stay in order).
            int laps = Math.Max(1, (int)Math.Ceiling(reel.Distance / (double)reel.Pool - 1e-9));
            for (int wall = 1; wall < laps; wall++)
            {
                int d = wall * reel.Pool;
                SwimRow row = computed.Rows.FirstOrDefault(r => r.D == d && r.T.HasValue);
                if (row != null)
                    reel.Walls.Add(row.OnVideo);
                else if (reel.HasModel && d < lastD)
                    reel.Walls.Add(reel.TimeAtDistance(d));
                else
                    reel.Walls.Add(double.NaN);
            }

            reel.Entry = computed.Entries.Count > 0 ? (double?)computed.Entries[0] : null;
            reel.FirstBreakout = computed.Breakouts.Count > 0 ? (double?)computed.Breakouts[0] : null;

            reel.BuildLaps(race, computed);
            reel.BuildBreakouts(race, computed);

            // (2.32.0) The start and the turns on the curve from their own marks, then the cycles on it.
            if (reel.AddPhaseKnots(race, computed))
                reel.BuildSlopes();

            reel.BuildCycles(computed);
            reel.BuildBadges(race, computed);
            reel.BuildParts(race, computed);
            reel.BuildSamples();
            return reel;
        }

        /// <summary>Start time of each lap (the start, then each wall), its style, breakout and the strokes after the breakout.</summary>
        private void BuildLaps(SwimRace race, SwimComputed computed)
        {
            int laps = Math.Max(1, (int)Math.Ceiling(Distance / (double)Pool - 1e-9));
            for (int lap = 0; lap < laps; lap++)
            {
                double start = lap == 0 ? 0 : (lap - 1 < Walls.Count ? Walls[lap - 1] : double.NaN);
                lapStarts.Add(start);
                lapStyles.Add(SwimPresets.StyleAt(race, Math.Min(race.Distance, (lap + 1) * Pool)));
            }

            for (int lap = 0; lap < laps; lap++)
            {
                double start = lapStarts[lap];
                double end = LapEnd(lap);
                double breakout = double.NaN;
                double feetOff = double.NaN;
                if (!double.IsNaN(start))
                {
                    breakout = computed.Breakouts.Where(b => b > start && b < end).DefaultIfEmpty(double.NaN).First();
                    if (lap > 0)
                        feetOff = computed.FeetOff.Where(f => f > start && f < end).DefaultIfEmpty(double.NaN).First();
                }

                lapBreakouts.Add(breakout);
                lapFeetOff.Add(feetOff);

                List<double> strokes = new List<double>();
                if (!double.IsNaN(start))
                {
                    double from = !double.IsNaN(breakout) ? breakout - 0.4 : start;
                    strokes.AddRange(computed.Strokes.Where(s => s > from && s <= end + 1e-6));
                }

                AsTheAnalysisCounts(strokes, computed, lap);
                lapCounted.Add(new List<double>(strokes));
                lapStrokes.Add(RemoveDoubleMarks(strokes));
            }
        }

        /// <summary>
        /// (2.34.0) The strokes of the lap as the analysis counts them, so that the counter of the video ends where the PDF does: the
        /// first pull of backstroke after the dive is not one (SwimCalculator), and in a segment whose count the analyst typed (an
        /// automatic stroke missed, or one too many) a stroke is put in the middle of its longest interval, or the one closest to the
        /// stroke before it is left out, for each whole stroke of the difference. The marks of the analysis are not touched.
        /// </summary>
        private void AsTheAnalysisCounts(List<double> strokes, SwimComputed computed, int lap)
        {
            if (computed == null || strokes.Count == 0 || lap >= lapStarts.Count || double.IsNaN(lapStarts[lap]))
                return;

            double lapFrom = lapStarts[lap], lapTo = LapEnd(lap);
            foreach (SwimRow row in computed.Rows)
            {
                if (double.IsNaN(row.OnVideo) || row.Index < 0 || row.Index >= computed.Rows.Count)
                    continue;

                double from = row.Index == 0 ? 0 : computed.Rows[row.Index - 1].OnVideo;
                double to = row.OnVideo;
                if (double.IsNaN(from) || to <= from || from < lapFrom - 1e-6 || to > lapTo + 1e-6)
                    continue;

                if (row.Index == 0 && row.Style == "bk" && SwimCalculator.BackstrokeBreakoutPull && computed.Strokes.Count > 0 &&
                    computed.Strokes[0] <= to + 1e-6 && strokes.Count > 0 && Math.Abs(strokes[0] - computed.Strokes[0]) < 1e-6)
                    strokes.RemoveAt(0);

                if (!row.StrokesTyped || !row.Strokes.HasValue || !row.StrokesAuto.HasValue)
                    continue;

                int more = (int)Math.Truncate(row.Strokes.Value - row.StrokesAuto.Value + (row.Strokes.Value >= row.StrokesAuto.Value ? 1e-6 : -1e-6));
                for (; more > 0; more--)
                    PutStroke(strokes, from, to);
                for (; more < 0; more++)
                    DropStroke(strokes, from, to);
            }
        }

        /// <summary>A stroke in the middle of the longest interval of the strokes in the segment (from, to] (the segment's middle without one).</summary>
        private static void PutStroke(List<double> strokes, double from, double to)
        {
            double at = 0.5 * (from + to), longest = 0;
            for (int i = 1; i < strokes.Count; i++)
            {
                double a = strokes[i - 1], b = strokes[i];
                if (b <= from + 1e-6 || a > to + 1e-6 || b - a <= longest)
                    continue;

                longest = b - a;
                at = Math.Min(to, Math.Max(from + 0.02, 0.5 * (a + b)));
            }

            int index = strokes.FindIndex(s => s > at);
            strokes.Insert(index < 0 ? strokes.Count : index, at);
        }

        /// <summary>The stroke of the segment (from, to] closest to the stroke before it is left out.</summary>
        private static void DropStroke(List<double> strokes, double from, double to)
        {
            int drop = -1;
            double shortest = double.PositiveInfinity;
            for (int i = 0; i < strokes.Count; i++)
            {
                if (strokes[i] <= from + 1e-6 || strokes[i] > to + 1e-6)
                    continue;

                double interval = i > 0 ? strokes[i] - strokes[i - 1] : double.MaxValue;
                if (interval < shortest)
                {
                    shortest = interval;
                    drop = i;
                }
            }

            if (drop >= 0)
                strokes.RemoveAt(drop);
        }

        /// <summary>
        /// Two stroke marks much closer than the usual interval are two presses on the same stroke when leaving one out gives a
        /// usual interval: the one whose removal gives the interval closest to the usual one is left out.
        /// </summary>
        private List<double> RemoveDoubleMarks(List<double> marks)
        {
            if (marks.Count < 4)
                return marks;

            List<double> gaps = new List<double>();
            for (int i = 1; i < marks.Count; i++)
                gaps.Add(marks[i] - marks[i - 1]);
            double median = Median(gaps);

            List<double> kept = new List<double>(marks);
            for (int i = 0; i + 1 < kept.Count; i++)
            {
                if (kept[i + 1] - kept[i] >= 0.7 * median)
                    continue;

                // Without the first of the two: from the mark before to the second; without the second: from the first to the next.
                double withoutFirst = i > 0 ? kept[i + 1] - kept[i - 1] : double.NaN;
                double withoutSecond = i + 2 < kept.Count ? kept[i + 2] - kept[i] : double.NaN;
                double costFirst = double.IsNaN(withoutFirst) ? double.PositiveInfinity : Math.Abs(withoutFirst - median);
                double costSecond = double.IsNaN(withoutSecond) ? double.PositiveInfinity : Math.Abs(withoutSecond - median);
                int drop = costFirst <= costSecond ? i : i + 1;
                double merged = drop == i ? withoutFirst : withoutSecond;
                if (double.IsNaN(merged) || merged < 0.75 * median || merged > 1.3 * median)
                    continue;

                DoubleMarks.Add(kept[drop]);
                kept.RemoveAt(drop);
                i = Math.Max(-1, i - 2);
            }

            return kept;
        }

        private void BuildCycles(SwimComputed computed)
        {
            for (int lap = 0; lap < lapStrokes.Count; lap++)
            {
                List<double> s = lapStrokes[lap];
                int presses = SwimPresets.PressesPerCycle(lapStyles[lap]);
                int number = 0;
                for (int k = presses; k < s.Count; k++)
                {
                    double t0 = s[k - presses], t1 = s[k];
                    double tempo = t1 - t0;
                    if (tempo <= 0.2 || tempo > 4.0)
                        continue;

                    double dps = (Position(t1, false) - Position(t0, false)) * K;
                    double span = RaceTime(t1) - RaceTime(t0);
                    number++;
                    Cycles.Add(new SwimReelCycle { Start = t0, Time = t1, Tempo = tempo, Dps = dps, Speed = dps / (span > 0.05 ? span : tempo), Lap = lap, Number = number });
                }
            }

            // (2.33.0) The steady values of each cycle, inside its length.
            foreach (IGrouping<int, SwimReelCycle> length in Cycles.GroupBy(c => c.Lap))
            {
                List<SwimReelCycle> list = length.ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    int from = Math.Max(0, Math.Min(i - 1, list.Count - 3));
                    int to = Math.Min(list.Count - 1, from + 2);
                    List<SwimReelCycle> around = list.GetRange(from, to - from + 1);
                    list[i].SteadyTempo = Median(around.Select(c => c.Tempo).ToList());
                    list[i].SteadyDps = Median(around.Select(c => c.Dps).ToList());
                }
            }

            // (2.34.0) Every segment of clean swimming as the analysis and the PDF have it: its rate, cycle length and speed are what the
            // video shows in it (the card, the bars of the chart, the summary), so that the video and the PDF never differ. (2.33.0:
            // only the segments whose rate the analysis did not take from the times of the strokes.)
            if (computed == null)
                return;

            foreach (SwimRow row in computed.Rows)
            {
                if (!row.IsFree || !row.Sr.HasValue || row.Sr.Value <= 0 || !row.Sl.HasValue || row.Sl.Value <= 0 ||
                    !row.Vel.HasValue || row.Index <= 0 || row.Index >= computed.Rows.Count)
                    continue;

                double from = computed.Rows[row.Index - 1].OnVideo, to = row.OnVideo;
                if (double.IsNaN(from) || double.IsNaN(to) || to <= from)
                    continue;

                Clean.Add(new SwimReelClean { From = row.PrevD, To = row.D, Start = from, End = to, Rate = row.Sr.Value, Length = row.Sl.Value, Speed = row.Vel.Value });
                foreach (SwimReelCycle cycle in Cycles.Where(x => x.Time > from + 1e-6 && x.Time <= to + 1e-6))
                {
                    cycle.SteadyTempo = 60.0 / row.Sr.Value;
                    cycle.SteadyDps = row.Sl.Value;
                    cycle.FromAnalysis = true;
                }
            }

            Clean.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        private void BuildBreakouts(SwimRace race, SwimComputed computed)
        {
            for (int lap = 0; lap < lapBreakouts.Count; lap++)
            {
                double b = lapBreakouts[lap];
                if (double.IsNaN(b))
                    continue;

                int wall = lap * Pool;
                double? measured = null;
                bool flagged = false;
                if (lap == 0)
                {
                    measured = race.BreakoutDist ?? race.UnderwaterDist;
                    flagged = !race.BreakoutDist.HasValue && race.UnderwaterEstimated;
                }
                else
                {
                    SwimTurnManual t;
                    if (race.Turns != null && race.Turns.TryGetValue(wall, out t) && t != null)
                    {
                        measured = t.UnderwaterDist;
                        flagged = t.UnderwaterEstimated;
                    }
                }

                // (2.9.5b) A value far past what a breakout can be for the style (a breakout of the auto pilot near the 25 m wrote
                // almost 25 m for a breaststroke breakout near 15 m) is not shown: the estimate from the times instead (the
                // value stays in the analysis, the panel warns). A value the pilot estimated (its breakout's record) is shown with ≈.
                double max = MaxBreakout(SwimPresets.StyleAt(race, Math.Min(race.Distance, wall + 15)));
                double limit = (max + MeasuredSlack) / K;
                // (2.15.0) The flag of the analysis (an estimate from the times, of the pilot or of the reading of the race) first: the
                // pilot's record alone missed the estimates kept after its breakout was confirmed.
                bool pilotEstimate = measured.HasValue && (flagged || race.Marks.Any(m => m.Type == SwimMarkType.Breakout && m.Pilot != null &&
                    m.Pilot.Wall == wall && m.Pilot.Underwater.HasValue && Math.Abs(m.Pilot.Underwater.Value - measured.Value) < 1e-6));
                if (measured.HasValue && measured.Value > 0 && measured.Value <= limit)
                {
                    BreakoutDistances[lap] = measured.Value;
                    if (!pilotEstimate)
                        BreakoutMeasured.Add(lap);
                    continue;
                }

                // The swimmer from the breakout to the next checkpoint goes about as fast as in the following segment:
                // back from that checkpoint with that speed (the curve alone is too smooth during the underwater phase).
                SwimRow next = computed.Rows.Where(r => r.T.HasValue && r.OnVideo > b && r.D > wall).OrderBy(r => r.OnVideo).FirstOrDefault();
                SwimRow after = next == null ? null : computed.Rows.Where(r => r.T.HasValue && r.OnVideo > next.OnVideo && r.D > next.D)
                    .OrderBy(r => r.OnVideo).FirstOrDefault();
                if (next == null || after == null || next.D % Pool == 0)
                    continue;

                double v = (after.D - next.D) / (after.OnVideo - next.OnVideo);
                bool dive = lap == 0 || (race.Relay && Distance >= 4 && wall % (Distance / 4) == 0);
                v *= BreakoutSpeedShare(dive, SwimPresets.StyleAt(race, next.D));
                double d = next.D - v * (next.OnVideo - b) - wall;

                // (2.9.5b) Past the most a breakout can be for the style by more than the error of the estimate: none (the breakout
                // marked is not where the swimmer came up); up to it, that most.
                if (d > 0 && d < Pool && d * K <= max + EstimateSlack)
                    BreakoutDistances[lap] = Math.Min(d, max / K);
            }
        }

        /// <summary>
        /// (2.32.0) A distance of the breakout after this wall of the race (0: the start; pool units, from the wall) a swimmer can have: up to
        /// the most for the style of the length with the margin of a measure (a turn's breakout measured at 39.6 m with the calibration of
        /// another frame of a moving camera is not). True for no value.
        /// </summary>
        internal static bool PossibleBreakout(SwimRace race, int wall, double? value)
        {
            if (!value.HasValue || race == null)
                return true;

            double max = MaxBreakout(SwimPresets.StyleAt(race, Math.Min(race.Distance, wall + 15)));
            return value.Value > 0 && value.Value * race.K <= max + MeasuredSlack;
        }

        /// <summary>
        /// (2.9.5b) The farthest a breakout can be from its wall (m): 15 m by the rules in butterfly, backstroke and freestyle (15.5 m for
        /// the head a little past the mark when the body comes up); breaststroke has no such rule and its breakouts can come later (20 m).
        /// </summary>
        internal static double MaxBreakout(string style)
        {
            return style == "br" ? 20.0 : 15.5;
        }

        /// <summary>
        /// (2.9.5b) The estimate of a breakout distance from the times is off by up to 0.83 m in 9 of 10 (BreakoutSpeedShare): past
        /// MaxBreakout by more than this (m) the breakout is not where the swimmer came up (a breakout of the auto pilot there is not put).
        /// </summary>
        internal const double EstimateSlack = 1.0;

        /// <summary>A distance in the analysis past MaxBreakout by up to this (m) is still shown (the analyst's clicks are the reference).</summary>
        internal const double MeasuredSlack = 1.5;

        /// <summary>
        /// The speed from the breakout to the next checkpoint against the speed of the segment after it (2.9.5): the median in the
        /// analysts' races (256 breakouts measured in the 115 races of the lab, by start or turn and style). With it the estimate
        /// of the breakout distance from the times is off by 0.33 m (median, 0.83 m in 9 of 10) instead of 0.38 m (0.96 m); before,
        /// it was short by 0.2–0.5 m in backstroke starts and butterfly turns and long by 0.3–0.6 m in freestyle.
        /// </summary>
        internal static double BreakoutSpeedShare(bool dive, string style)
        {
            switch (style)
            {
                case "bk": return dive ? 0.87 : 1.03;
                case "br": return dive ? 1.05 : 1.00;
                case "fly": return dive ? 0.98 : 0.93;
                case "fs": return dive ? 1.07 : 1.06;
                default: return 1.0;
            }
        }

        private void BuildBadges(SwimRace race, SwimComputed computed)
        {
            if (computed.ReactionTime.HasValue)
                Badges.Add(new SwimReelBadge { Time = computed.ReactionTime.Value, Title = SwimLang.T("ΑΝΤΙΔΡΑΣΗ"), Value = Seconds(computed.ReactionTime.Value), Detail = SwimLang.T("από το σήμα εκκίνησης"), Tone = SwimReelTone.Accent });

            if (Entry.HasValue)
            {
                // (2.41.0) The entry of the head (of the hands in an analysis marked so before 2.41.0).
                bool head = race.HeadEntry();
                double? at = race.EntryDistance();
                string detail = at.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, head ? SwimLang.T("κεφάλι στο νερό στα {0:0.0} {1}") : SwimLang.T("χέρια στο νερό στα {0:0.0} {1}"), at.Value, UnitShort)
                    : head ? SwimLang.T("κεφάλι στο νερό") : SwimLang.T("χέρια στο νερό");
                Badges.Add(new SwimReelBadge { Time = Entry.Value, Title = SwimLang.T("ΕΙΣΟΔΟΣ"), Value = Seconds(Entry.Value), Detail = detail, Tone = SwimReelTone.Accent });
            }

            for (int lap = 0; lap < lapBreakouts.Count; lap++)
            {
                double b = lapBreakouts[lap];
                if (double.IsNaN(b))
                    continue;

                double from = lap == 0 ? (Entry ?? 0) : (!double.IsNaN(lapFeetOff[lap]) ? lapFeetOff[lap] : lapStarts[lap]);
                string under = string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0:0.00} s κάτω από το νερό"), b - from);
                double d;
                string value = BreakoutDistances.TryGetValue(lap, out d)
                    ? (BreakoutMeasured.Contains(lap) ? "" : "≈") + d.ToString("0.0", CultureInfo.InvariantCulture) + " " + UnitShort
                    : Seconds(b);
                Badges.Add(new SwimReelBadge { Time = b, Title = lap == 0 ? "BREAKOUT" : SwimLang.T("BREAKOUT ΣΤΡΟΦΗΣ"), Value = value, Detail = under, Tone = SwimReelTone.Green });
            }

            foreach (SwimRow row in computed.Rows.Where(r => r.T.HasValue).OrderBy(r => r.OnVideo))
            {
                bool wall = row.D % Pool == 0;
                string title, detail;
                SwimReelTone tone;
                if (row.D == Distance)
                {
                    title = SwimLang.T("ΤΕΡΜΑΤΙΣΜΟΣ");
                    detail = string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", Distance, UnitShort, Lower(SwimPresets.StyleName(race.Style)));
                    tone = SwimReelTone.Amber;
                }
                else if (wall)
                {
                    title = row.D.ToString(CultureInfo.InvariantCulture) + " " + UnitShort.ToUpperInvariant();
                    detail = SwimLang.T("στροφή");
                    tone = SwimReelTone.Amber;
                }
                else
                {
                    title = row.D.ToString(CultureInfo.InvariantCulture) + " " + UnitShort.ToUpperInvariant();
                    int fromWall = row.D % Pool;
                    detail = row.D == 15 ? SwimLang.T("από την εκκίνηση") : (fromWall == 15 ? "15 " + UnitShort + SwimLang.T(" μετά τον τοίχο") : SwimLang.T("ενδιάμεσος"));
                    tone = row.D % 25 == 0 ? SwimReelTone.Amber : SwimReelTone.Accent;
                }

                Badges.Add(new SwimReelBadge { Time = row.OnVideo, Title = title, Value = SwimReference.FormatTime(row.T.Value), Detail = detail, Tone = tone });
            }

            Badges.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        /// <summary>Parts of the summary: the halves of a single length, each length up to four, or four parts of a long race.</summary>
        private void BuildParts(SwimRace race, SwimComputed computed)
        {
            List<int> bounds = new List<int> { 0 };
            int lengths = Math.Max(1, Distance / Pool);
            if (lengths == 1)
            {
                if (Distance % 2 == 0 && knotD.Contains(Distance / 2.0))
                    bounds.Add(Distance / 2);
            }
            else if (lengths <= 4)
            {
                for (int i = 1; i < lengths; i++)
                    bounds.Add(i * Pool);
            }
            else
            {
                for (int i = 1; i < 4; i++)
                    bounds.Add((int)Math.Round(Distance * i / 4.0 / Pool) * Pool);
            }

            bounds.Add(Distance);
            bounds = bounds.Distinct().OrderBy(b => b).ToList();

            for (int i = 1; i < bounds.Count; i++)
            {
                SwimReelPart part = new SwimReelPart { From = bounds[i - 1], To = bounds[i] };
                double? t0 = TimeAtCheckpoint(part.From);
                double? t1 = TimeAtCheckpoint(part.To);
                double? r0 = RaceTimeAtCheckpoint(part.From);
                double? r1 = RaceTimeAtCheckpoint(part.To);
                if (r0.HasValue && r1.HasValue && r1.Value > r0.Value)
                {
                    part.Time = r1.Value - r0.Value;
                    part.Speed = (part.To - part.From) * K / part.Time.Value;
                }

                double a = t0 ?? double.NaN, b = t1 ?? double.NaN;
                if (!double.IsNaN(a) && !double.IsNaN(b))
                {
                    part.Strokes = lapCounted.SelectMany(s => s).Count(s => s > a && s <= b);

                    // (2.15.0) The counts of the analysis when every segment of the part has one (those the analyst confirmed or typed, the
                    // first pull of backstroke not counted), as the sheet and the report: the marks alone left his corrections out.
                    List<SwimRow> rows = computed != null ? computed.Rows.Where(r => r.PrevD >= part.From && r.D <= part.To).ToList() : new List<SwimRow>();
                    if (rows.Count > 0 && rows.All(r => r.Strokes.HasValue))
                        part.Strokes = rows.Sum(r => r.Strokes.Value);
                    // (2.34.0) The rate and the cycle length of the clean swimming of the part as the analysis and the PDF have them (of
                    // a 50: the «Ανά 50» of the report): the rate weighted with the time, the length of the whole. Until 2.33.0 the mean
                    // of every cycle marked in the part, the breakout and the strokes into the wall with them.
                    double cyclesOfRate = 0, cyclesOfStrokes = 0, cleanTime = 0, cleanMetres = 0;
                    foreach (SwimRow row in rows.Where(r => r.Sr.HasValue && r.SegTime.HasValue && r.Strokes.HasValue))
                    {
                        cyclesOfRate += row.Sr.Value * row.SegTime.Value / 60.0;
                        cyclesOfStrokes += row.Strokes.Value / SwimPresets.PressesPerCycle(row.Style);
                        cleanTime += row.SegTime.Value;
                        cleanMetres += (row.D - row.PrevD) * K;
                    }

                    double cycles = SwimCalculator.LengthFromRate ? cyclesOfRate : cyclesOfStrokes;
                    List<SwimReelCycle> inside = Cycles.Where(c => c.Time > a && c.Time <= b).ToList();
                    if (cleanTime > 0 && cyclesOfRate > 0 && cycles > 0)
                    {
                        part.Tempo = cleanTime / cyclesOfRate;
                        part.Dps = cleanMetres / cycles;
                        part.FromAnalysis = true;
                    }
                    else if (inside.Count > 0)
                    {
                        part.Tempo = inside.Average(c => c.Tempo);
                        part.Dps = inside.Average(c => c.Dps);
                    }
                }

                Parts.Add(part);
            }
        }

        /// <summary>
        /// (2.32.0) The start and the turns on the curve, from their own marks: on the block (0) up to the release from it, the entry at its
        /// distance measured, the breakout at its distance (measured or estimated as the summary shows it); at a wall up to the push off it
        /// (the feet off marked, else a little after the touch), then the breakout. Before, the curve went only through the checkpoints:
        /// the 50–65 m after a butterfly turn (8.01 s with the 1.10 s on the wall) gave a flat 1.89 m/s under the water and moved the
        /// swimmer off the wall while he was turning; now 2.9 m/s after the push down to the breakout, as the start. A mark out of its
        /// place (a knot that would make the curve go back, or speeds no swimmer has) is left out. True if one was put.
        /// (2.38.6) The previous model again, the analyst's request (2.38.4–2.38.5 from the head): the entry where the hands entered, the
        /// swimmer at the wall up to the push off it, as the speeds of the panel and the PDF (SwimTechCalculator), with the analyst's two
        /// rules on the same marks (SwimSpeedRules): the breakout a few centimetres nearer when the speed from it to the 15 would be a
        /// little below the swimming after it (the distance shown stays the one measured), and the push off a wall a little out from it
        /// when the turn would be faster under the water than the start (its mean, or the highest speed of the curve after the push).
        /// (2.41.0) From the head to the head again, the analyst's request, as the panel and the PDF: the entry where the head went into the
        /// water (in an analysis marked with the hands, the head behind them), the push off a wall where the head is then (the typical height
        /// + 0.10 from it: the swimmer goes out from the wall to there while he pushes), farther only by the rule of the start.
        /// </summary>
        private bool AddPhaseKnots(SwimRace race, SwimComputed computed)
        {
            if (!HasModel || race == null || computed == null)
                return false;

            // The times of the race of these marks as between the checkpoints around them (the knots of the checkpoints alone).
            List<double> cpV = new List<double>(knotV), cpT = new List<double>(knotT);
            List<PhaseKnot> knots = new List<PhaseKnot>();

            // The start.
            double release = computed.BlockMarkTime ?? computed.BlockTime ?? double.NaN;
            if (!double.IsNaN(release) && release > 0.2 && release < 1.5)
                knots.Add(new PhaseKnot { Video = release, Distance = 0, Break = true, Hold = true, What = "release" });

            // (2.41.0) Where the head is at the entry: where it went in, or behind the hands in an analysis marked with them.
            SwimHead head = SwimHead.Of(race);
            double? entryAt = race.EntryDistance();
            double entryDistance = !entryAt.HasValue ? double.NaN : race.HeadEntry() ? entryAt.Value : entryAt.Value - head.BehindHands / K;
            bool entered = Entry.HasValue && !double.IsNaN(entryDistance) && entryDistance * K >= 0.5 && entryDistance * K <= 6.5;
            if (entered)
                knots.Add(new PhaseKnot { Video = Entry.Value, Distance = entryDistance, Break = true, What = "entry" });

            // (2.38.6) The breakout of the start by the rule of the breakout, and its speeds under the water, which the turns are held to
            // (only from a breakout measured, as the panel and the PDF; not in a relay, where another swimmer dived).
            double startMean = double.NaN, startPeak = double.NaN;
            double startDistance;
            if (lapBreakouts.Count > 0 && !double.IsNaN(lapBreakouts[0]) && BreakoutDistances.TryGetValue(0, out startDistance))
            {
                double out0 = startDistance;
                double bo = Map(lapBreakouts[0], cpV, cpT), at15 = RaceTimeAt(15);
                out0 = NearerBreakout(computed, 0, out0, bo, at15,
                    entered ? Math.Max(SwimSpeedRules.LeastOut, entryDistance + SwimTechCalculator.UnderwaterMinSpan) : SwimSpeedRules.LeastOut);
                knots.Add(new PhaseKnot { Video = lapBreakouts[0], Distance = out0, What = "breakout" });

                if (entered && !race.Relay && BreakoutMeasured.Contains(0) && !double.IsNaN(at15))
                {
                    double t0 = Map(Entry.Value, cpV, cpT);
                    double underTime = bo - t0, afterTime = at15 - bo;
                    if (underTime > 0 && afterTime > 0 && out0 > entryDistance && out0 < 15)
                    {
                        startMean = (out0 - entryDistance) * K / underTime;
                        startPeak = SwimSpeedRules.Peak(startMean, (15 - out0) * K / afterTime, underTime, afterTime);
                    }
                }
            }

            // The turns.
            for (int lap = 1; lap < lapStarts.Count; lap++)
            {
                double touch = lapStarts[lap];
                if (double.IsNaN(touch))
                    continue;

                int wall = lap * Pool;
                double breakout = lapBreakouts[lap];
                string before = StyleOfLap(lap - 1), after = StyleOfLap(lap);
                bool tumble = before == after && (after == "fs" || after == "bk");
                double push = !double.IsNaN(lapFeetOff[lap]) ? lapFeetOff[lap] : touch + (tumble ? TumblePush : HandsPush);
                bool change = race.Relay && Distance >= 4 && wall % (Distance / 4) == 0;

                // (2.41.0) Where the head is at the push-off (the typical height + 0.10 from the wall); at the change of a relay the next
                // swimmer dives (from the wall, as the start).
                double headOut = change ? 0 : head.AtPushoff / K;
                double length, out1 = double.NaN, bo = double.NaN, at15 = RaceTimeAt(wall + 15);
                if (!double.IsNaN(breakout) && BreakoutDistances.TryGetValue(lap, out length))
                {
                    bo = Map(breakout, cpV, cpT);
                    out1 = NearerBreakout(computed, wall, length, bo, at15, Math.Max(SwimSpeedRules.LeastOut, headOut + SwimTechCalculator.UnderwaterMinSpan));
                }

                // (2.38.6) The rule of the start: (2.41.0) from the head at the push-off, unless the turn would be faster under the water than
                // the start even from there (on its mean or at the highest speed of the curve after the push); then a little farther.
                double away = headOut;
                if (!change && !race.Relay && !double.IsNaN(startMean) && !double.IsNaN(out1) && !double.IsNaN(at15) &&
                    out1 - headOut >= SwimTechCalculator.UnderwaterMinSpan && out1 < 15)
                {
                    double underTime = bo - Map(push, cpV, cpT), afterTime = at15 - bo;
                    if (underTime >= SwimTechCalculator.SpeedMinTime && afterTime > 0)
                        away = SwimSpeedRules.PushAway(out1, underTime, (15 - out1) * K / afterTime, afterTime, K, startMean, startPeak,
                            out1 - SwimTechCalculator.UnderwaterMinSpan, headOut);
                }

                // The head out from the wall while he pushes (no speed shown there), unless the breakout is not past it.
                if (!double.IsNaN(out1) && out1 - away < SwimTechCalculator.UnderwaterMinSpan)
                    away = 0;
                if (push - touch > 0.05 && push - touch < 2.5 && (double.IsNaN(breakout) || push < breakout - 0.3))
                    knots.Add(away > 1e-6
                        ? new PhaseKnot { Video = push, Distance = wall + away, Break = true, What = "push", After = wall }
                        : new PhaseKnot { Video = push, Distance = wall, Break = true, Hold = true, What = "push" });

                if (!double.IsNaN(out1))
                    knots.Add(new PhaseKnot { Video = breakout, Distance = wall + out1, What = "breakout" });
            }

            bool put = false;
            foreach (PhaseKnot knot in knots.OrderBy(x => x.Video))
                put |= PutKnot(knot, cpV, cpT);

            return put;
        }

        /// <summary>
        /// (2.38.6) The analyst's rule of the breakout (SwimSpeedRules.Breakout) on the times of the race: where the curve takes the breakout
        /// after a wall (0: the start; pool units from it), nearer than 'length' when the speed from there to the 15 (its time of the race
        /// at15) would be below the swimming after it, as the panel and the PDF (2.39.4: however far, not nearer than 'least'; 2.39.3 up
        /// to 0.5 m, before also up to 5% slower).
        /// </summary>
        private double NearerBreakout(SwimComputed computed, int wall, double length, double breakout, double at15, double least)
        {
            double? next = SwimSpeedRules.NextSpeed(computed, wall);
            if (!next.HasValue || double.IsNaN(breakout) || double.IsNaN(at15))
                return length;

            return SwimSpeedRules.Breakout(length, at15 - breakout, next.Value, K, least);
        }

        /// <summary>(2.38.6) The time of the race at a checkpoint (0 at the start), NaN without one.</summary>
        private double RaceTimeAt(int distance)
        {
            double? t = RaceTimeAtCheckpoint(distance);
            return t.HasValue ? t.Value : double.NaN;
        }

        private sealed class PhaseKnot
        {
            public double Video;
            public double Distance;
            public bool Break;
            public bool Hold;
            public string What;

            /// <summary>
            /// (2.38.4) Where the knot before it has to be (the push-off: right after the touch of its wall, as with Hold). (2.38.6) A push
            /// a little out from the wall by the rule of the start.
            /// </summary>
            public double? After;
        }

        /// <summary>
        /// (2.32.0) A knot of the start or a turn between two knots of the curve (never past the last one): on its frame, its time of the
        /// race as between the checkpoints, its distance on from the knot before it (the same on the block and at the wall), speeds a
        /// swimmer can have (the flight up to 15 m/s, under the water 1–3.5 m/s, after the breakout 0.6–2.8 m/s).
        /// </summary>
        private bool PutKnot(PhaseKnot knot, List<double> cpV, List<double> cpT)
        {
            int n = knotV.Count;
            int k = 0;
            while (k < n && knotV[k] < knot.Video)
                k++;
            if (k <= 0 || k >= n)
                return false;

            double race = Map(knot.Video, cpV, cpT);
            if (knot.Video < knotV[k - 1] + 0.05 || knot.Video > knotV[k] - 0.05 || race < knotT[k - 1] + 0.05 || race > knotT[k] - 0.05)
                return false;

            // (2.38.6) A push off a wall a little out from it (the rule of the start) is right after the touch, however near.
            bool push = knot.What == "push";
            double before = knotD[k - 1], next = knotD[k];
            bool near = push && knot.After.HasValue ? knot.Distance <= before + 1e-9 : knot.Distance < before + 0.2 / K;
            if (knot.Hold ? Math.Abs(knot.Distance - before) > 1e-9 : near || knot.Distance > next - 0.2 / K)
                return false;
            if (knot.After.HasValue && Math.Abs(before - knot.After.Value) > 1e-9)
                return false;

            if (!knot.Hold)
            {
                double speedIn = (knot.Distance - before) / (race - knotT[k - 1]) * K;
                double speedOut = (next - knot.Distance) / (knotT[k] - race) * K;
                // Up to the push off a wall the swimmer turns there (no speed shown): any speed; after it under the water, as after the entry.
                double most = knot.What == "entry" || push ? 15.0 : 3.5;
                double least = knot.What == "entry" ? 1.5 : push ? 0.0 : 1.0;
                if (speedIn < least || speedIn > most || speedOut < 0.6 || speedOut > (knot.What == "entry" || push ? 3.5 : 2.8))
                    return false;
            }

            knotT.Insert(k, race);
            knotV.Insert(k, knot.Video);
            knotD.Insert(k, knot.Distance);
            knotBreak.Insert(k, knot.Break);
            return true;
        }

        // (2.31.0) Where the swimmer enters the water after the start when the entry is not marked (m from the wall): about 3 m after a
        // dive, 1.5 m in backstroke. Only so that the block and the flight have no speed.
        internal const double DiveEntry = 3.0;
        internal const double BackstrokeEntry = 1.5;

        // (2.31.0) The push off a wall when the feet off are not marked (s after the touch): 0.3 s after a tumble turn (the touch is the feet
        // on the wall), 0.9 s after a turn with the hands (breaststroke, butterfly, the changes of style of the medley).
        internal const double TumblePush = 0.3;
        internal const double HandsPush = 0.9;

        /// <summary>
        /// (2.31.0) The speed of the race every 0.05 s (more apart in a long race) while the swimmer is in the water, and the underwater
        /// parts with a breakout: the chart from the start to the finish and the speed under the water (before: none, «–»).
        /// </summary>
        private void BuildSamples()
        {
            if (!HasModel)
                return;

            double last = knotV[knotV.Count - 1];
            double step = Math.Max(0.05, last / 2000);
            for (int lap = 0; lap < lapStarts.Count; lap++)
            {
                double from = InWaterFrom(lap);
                lapInWater.Add(from);
                if (double.IsNaN(from))
                    continue;

                double breakout = lapBreakouts[lap];
                if (!double.IsNaN(breakout) && breakout > from)
                {
                    // The band of the chart up to the breakout distance of the summary (measured or estimated), else where the curve is then.
                    double wall = lap * Pool;
                    double length;
                    if (!BreakoutDistances.TryGetValue(lap, out length))
                        length = DistanceAt(breakout) - wall;
                    UnderwaterSpans.Add(new SwimReelSpan
                    {
                        Lap = lap,
                        From = from,
                        To = breakout,
                        FromDistance = lap == 0 ? DistanceAt(from) : wall,
                        ToDistance = Math.Min(Distance, wall + Math.Max(0, length))
                    });
                }

                double to = Math.Min(LapEnd(lap), last);
                if (!(to > from))
                    continue;

                for (double t = from; ; t += step)
                {
                    double at = Math.Min(t, to);
                    Samples.Add(new SwimReelSample
                    {
                        Time = at,
                        Distance = DistanceAt(at),
                        Speed = SpeedAt(at),
                        Lap = lap,
                        Underwater = !double.IsNaN(breakout) && at < breakout
                    });
                    if (at >= to)
                        break;
                }
            }
        }

        /// <summary>
        /// (2.31.0) From when the swimmer is in the water in a lap (s on the video, NaN: its start not known): after the start the entry
        /// marked (else the curve at about the entry), after a wall the feet off marked (else a little after the touch); not after the
        /// breakout of the lap.
        /// </summary>
        private double InWaterFrom(int lap)
        {
            double start = lap < lapStarts.Count ? lapStarts[lap] : double.NaN;
            if (double.IsNaN(start))
                return double.NaN;

            double from;
            if (lap == 0)
            {
                double entry = (StyleOfLap(0) == "bk" ? BackstrokeEntry : DiveEntry) / K;
                from = Entry.HasValue && Entry.Value > 0 ? Entry.Value : TimeAtDistance(entry);
            }
            else
            {
                string before = StyleOfLap(lap - 1), after = StyleOfLap(lap);
                bool tumble = before == after && (after == "fs" || after == "bk");
                from = !double.IsNaN(lapFeetOff[lap]) ? lapFeetOff[lap] : start + (tumble ? TumblePush : HandsPush);
            }

            double breakout = lapBreakouts[lap];
            if (!double.IsNaN(breakout))
                from = Math.Min(from, breakout);

            // (2.32.0) A millisecond after the entry or the push: on their knot the speed is the one of the flight or of the wall before it.
            return Math.Max(start, from) + KnotNudge;
        }

        /// <summary>(2.32.0) The swimmer is in the water this long after the knot of the entry or the push off the wall (s).</summary>
        internal const double KnotNudge = 0.001;

        /// <summary>The frame of a checkpoint on the video (s after the start), null without a time.</summary>
        private double? TimeAtCheckpoint(int distance)
        {
            if (distance == 0)
                return 0;

            int i = knotD.IndexOf(distance);
            return i >= 0 ? (double?)knotV[i] : null;
        }

        /// <summary>(2.31.0) The time of the race at a checkpoint (the official one where typed), null without a time.</summary>
        private double? RaceTimeAtCheckpoint(int distance)
        {
            if (distance == 0)
                return 0;

            int i = knotD.IndexOf(distance);
            return i >= 0 ? (double?)knotT[i] : null;
        }
        #endregion

        #region Model
        /// <summary>
        /// (2.40.0) The strokes as the analysis counts them (lapCounted) after 'from' and up to 'to' (s after the start, the clock of the
        /// curve): the live ΔΑΚ of the 50 the swimmer is in (SwimReelPainter).
        /// </summary>
        public int StrokesBetween(double from, double to)
        {
            int n = 0;
            foreach (List<double> lap in lapCounted)
                n += lap.Count(x => x > from + 1e-6 && x <= to + 1e-6);
            return n;
        }

        /// <summary>
        /// Distance of the race (pool units) at a time after the start, from the curve through the checkpoints; after the last
        /// checkpoint marked, on with the speed of the last segment up to the next checkpoint.
        /// </summary>
        public double DistanceAt(double t)
        {
            return Position(t, true);
        }

        /// <summary>Time on the video (s after the start) at a distance up to the last checkpoint marked, from the curve.</summary>
        public double TimeAtDistance(double d)
        {
            int n = knotT.Count;
            if (n < 2 || d <= 0)
                return 0;
            if (d >= knotD[n - 1])
                return knotV[n - 1];

            double lo = 0, hi = knotT[n - 1];
            for (int i = 0; i < 50; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Curve(mid, true) < d)
                    lo = mid;
                else
                    hi = mid;
            }

            return VideoTime(0.5 * (lo + hi));
        }

        /// <summary>
        /// (2.31.0) The time of the race at a time of the video (both s after the start): the same at the checkpoints whose mark is on
        /// their time, in proportion between two checkpoints, on as the video after the last one.
        /// </summary>
        public double RaceTime(double t)
        {
            return Map(t, knotV, knotT);
        }

        /// <summary>(2.31.0) The time of the video at a time of the race (the inverse of RaceTime).</summary>
        public double VideoTime(double r)
        {
            return Map(r, knotT, knotV);
        }

        private static double Map(double x, List<double> from, List<double> to)
        {
            int n = from.Count;
            if (n < 2 || x <= 0)
                return x;
            if (x >= from[n - 1])
                return to[n - 1] + (x - from[n - 1]);

            int k = 0;
            while (k < n - 2 && x > from[k + 1])
                k++;

            double h = from[k + 1] - from[k];
            return h > 0 ? to[k] + (x - from[k]) / h * (to[k + 1] - to[k]) : to[k];
        }

        /// <summary>stopAtCheckpoint: not beyond the next checkpoint (where the swimmer is shown), else up to the finish (cycles).</summary>
        private double Position(double t, bool stopAtCheckpoint)
        {
            return Curve(RaceTime(t), stopAtCheckpoint);
        }

        /// <summary>(2.31.0) Distance at a time of the race (the curve through the checkpoints, on the times of the race).</summary>
        private double Curve(double t, bool stopAtCheckpoint)
        {
            int n = knotT.Count;
            if (n < 2 || t <= 0)
                return 0;

            if (t >= knotT[n - 1])
            {
                if (Finish.HasValue)
                    return Math.Min(Distance, knotD[n - 1]);

                double v = (knotD[n - 1] - knotD[n - 2]) / (knotT[n - 1] - knotT[n - 2]);
                return Math.Min(stopAtCheckpoint ? nextCheckpoint : Distance, knotD[n - 1] + v * (t - knotT[n - 1]));
            }

            int k = 0;
            while (k < n - 2 && t > knotT[k + 1])
                k++;

            double h = knotT[k + 1] - knotT[k];
            double s = (t - knotT[k]) / h;
            double h00 = (1 + 2 * s) * (1 - s) * (1 - s);
            double h10 = s * (1 - s) * (1 - s);
            double h01 = s * s * (3 - 2 * s);
            double h11 = s * s * (s - 1);
            return h00 * knotD[k] + h10 * h * slopeFrom[k] + h01 * knotD[k + 1] + h11 * h * slopeTo[k];
        }

        /// <summary>
        /// The curve through the checkpoints, one length at a time (2.9.4): broken at each wall marked, so that the turn does not
        /// smooth the speed on both sides of it (before: through the wall, the push-off after it raised the speed of the last metres
        /// before it and the slow turn lowered the start of the next length: 1.81 → 1.62 → 1.83 m/s within 45–50 m of a 100 m
        /// backstroke whose 45–50 m was 1.68 m/s and 50–65 m 2.15 m/s). Each length: the monotone cubic of its checkpoints (as
        /// scipy's PCHIP), its ends from its own segments only.
        /// </summary>
        private void BuildSlopes()
        {
            int n = knotT.Count;
            slopeFrom = new double[Math.Max(0, n - 1)];
            slopeTo = new double[Math.Max(0, n - 1)];
            if (n < 2)
                return;

            int first = 0;
            for (int i = 1; i < n; i++)
            {
                // (2.32.0) Pieces at the walls as before, and at the release, the entry and the push off a wall (knotBreak).
                bool wall = i < knotBreak.Count ? knotBreak[i] : knotD[i] < Distance && Math.Abs(knotD[i] / Pool - Math.Round(knotD[i] / Pool)) < 1e-9;
                if (!wall && i < n - 1)
                    continue;

                // The knots first..i are one length (or what is marked of it); a wall and the push off it, or the start and the release
                // from the block, a piece on the spot (no speed); the release and the entry the flight.
                List<double> x = knotT.GetRange(first, i - first + 1), y = knotD.GetRange(first, i - first + 1);
                double[] m = PchipSlopes(x, y);

                // (2.9.5) The first segment of a length (the dive and the underwater phase after the start, the push-off after a
                // turn) is much faster than the swimming after it: its speed raised the slope at its end (the 15 m), and the next
                // segment then dipped below its own speed and rose again at its end (1.98 → 1.59 → 1.66 m/s within 15–25 m of a
                // 100 m breaststroke whose 15–25 m was 1.68 m/s). At that knot the speed of the swimming that follows.
                if (x.Count >= 3 && x[2] > x[1])
                {
                    m[1] = (y[2] - y[1]) / (x[2] - x[1]);

                    // A length of three knots (the wall, the 15 m, the next wall: a 25 m pool) ends with the same speed: its end
                    // from the first segment too dipped to 1.51 m/s at the wall in a 15–25 m swum at 1.69 m/s.
                    if (x.Count == 3)
                        m[2] = m[1];
                }

                for (int k = first; k < i; k++)
                {
                    slopeFrom[k] = m[k - first];
                    slopeTo[k] = m[k - first + 1];
                }

                first = i;
            }
        }

        /// <summary>
        /// Speed (m/s) at a time of the video after the start, from the curve through the checkpoints: on the times of the race, as the
        /// speeds of the analysis and the PDF (2.31.0).
        /// </summary>
        public double SpeedAt(double t)
        {
            return Slope(RaceTime(t)) * K;
        }

        /// <summary>
        /// (2.31.0) Speed of the curve (pool units/s) at a time of the race: the derivative of the cubic of its segment; at a checkpoint the
        /// one of the segment that ends there (on the frame of a wall the speed of its length: the difference of two points 0.02 s on each
        /// side of it mixed the push-off after it); after the last checkpoint marked as the curve goes on.
        /// </summary>
        private double Slope(double r)
        {
            int n = knotT.Count;
            if (n < 2)
                return 0;

            if (r > knotT[n - 1])
            {
                if (Finish.HasValue)
                    return 0;

                double v = (knotD[n - 1] - knotD[n - 2]) / (knotT[n - 1] - knotT[n - 2]);
                return Curve(r, true) < nextCheckpoint - 1e-9 ? v : 0;
            }

            int k = 0;
            while (k < n - 2 && r > knotT[k + 1])
                k++;

            double h = knotT[k + 1] - knotT[k];
            double s = Math.Max(0, (r - knotT[k]) / h);
            return (6 * s * s - 6 * s) / h * (knotD[k] - knotD[k + 1]) + (3 * s * s - 4 * s + 1) * slopeFrom[k] + (3 * s * s - 2 * s) * slopeTo[k];
        }

        /// <summary>
        /// Derivatives of the monotone cubic through the points (as scipy's PCHIP): weighted harmonic mean inside,
        /// three-point estimate at the ends.
        /// </summary>
        internal static double[] PchipSlopes(List<double> x, List<double> y)
        {
            int n = x.Count;
            double[] m = new double[n];
            if (n < 2)
                return m;

            double[] h = new double[n - 1];
            double[] d = new double[n - 1];
            for (int k = 0; k < n - 1; k++)
            {
                h[k] = x[k + 1] - x[k];
                d[k] = (y[k + 1] - y[k]) / h[k];
            }

            if (n == 2)
            {
                m[0] = m[1] = d[0];
                return m;
            }

            for (int k = 1; k < n - 1; k++)
            {
                if (d[k - 1] * d[k] <= 0)
                {
                    m[k] = 0;
                    continue;
                }

                double w1 = 2 * h[k] + h[k - 1];
                double w2 = h[k] + 2 * h[k - 1];
                m[k] = (w1 + w2) / (w1 / d[k - 1] + w2 / d[k]);
            }

            m[0] = EndSlope(h[0], h[1], d[0], d[1]);
            m[n - 1] = EndSlope(h[n - 2], h[n - 3], d[n - 2], d[n - 3]);
            return m;
        }

        internal static double EndSlope(double h0, double h1, double m0, double m1)
        {
            double d = ((2 * h0 + h1) * m0 - h0 * m1) / (h0 + h1);
            if (Math.Sign(d) != Math.Sign(m0))
                return 0;
            if (Math.Sign(m0) != Math.Sign(m1) && Math.Abs(d) > Math.Abs(3 * m0))
                return 3 * m0;
            return d;
        }
        #endregion

        #region At a moment of the video
        /// <summary>End of a lap (s): its wall, the finish for the last one, infinity while not known.</summary>
        public double LapEnd(int lap)
        {
            if (lap >= 0 && lap < Walls.Count)
                return double.IsNaN(Walls[lap]) ? (Finish ?? double.PositiveInfinity) : Walls[lap];

            return Finish ?? double.PositiveInfinity;
        }

        /// <summary>Lap at a time after the start (0 = first length).</summary>
        public int LapAt(double t)
        {
            int lap = 0;
            foreach (double w in Walls)
            {
                if (t > w)
                    lap++;
            }

            return Math.Min(lap, Math.Max(0, lapStarts.Count - 1));
        }

        public string StyleOfLap(int lap)
        {
            return lap >= 0 && lap < lapStyles.Count ? lapStyles[lap] : "fs";
        }

        /// <summary>Strokes of the current length up to this time, as the analysis counts them (2.34.0, lapCounted).</summary>
        public int StrokesInLength(double t)
        {
            int lap = LapAt(t);
            return lap < lapCounted.Count ? lapCounted[lap].Count(s => s <= t + 1e-6) : 0;
        }

        /// <summary>
        /// (2.34.0) The segment of clean swimming the swimmer is in at this time (from the frame of its first checkpoint to the frame
        /// before its last one), null outside the clean swimming: there the analysis has no rate or stroke length.
        /// </summary>
        public SwimReelClean CleanAt(double t)
        {
            foreach (SwimReelClean c in Clean)
            {
                if (t >= c.Start - 1e-6 && t < c.End - 1e-6)
                    return c;
            }

            return null;
        }

        /// <summary>
        /// (2.34.0) The speed the video shows at this time: in the clean swimming the speed of the segment as the analysis and the PDF
        /// have it (measured: between two checkpoints), elsewhere the speed of the curve (an estimate, shown with ≈).
        /// </summary>
        public double ShownSpeed(double t, out bool measured)
        {
            SwimReelClean c = CleanAt(t);
            measured = c != null;
            return c != null ? c.Speed : Math.Max(0, SpeedAt(t));
        }

        /// <summary>The last complete cycle of the current length at this time, null before the first one.</summary>
        public SwimReelCycle LastCycle(double t)
        {
            int lap = LapAt(t);
            return Cycles.LastOrDefault(c => c.Lap == lap && c.Time <= t + 1e-6);
        }

        /// <summary>Phase of the race at a time after the start (upper case, as on the video; in Greek, shown with SwimLang.T).</summary>
        public string PhaseAt(double t)
        {
            if (t < 0)
                return "ΣΤΟΝ ΒΑΤΗΡΑ";
            if (Finish.HasValue && t >= Finish.Value)
                return "ΤΕΡΜΑΤΙΣΜΟΣ";

            int lap = LapAt(t);
            double breakout = lap < lapBreakouts.Count ? lapBreakouts[lap] : double.NaN;
            // (2.31.0) In the water from the entry or the push off the wall, marked or estimated as for the speed (before, without the
            // marks: the start, the turn, up to the breakout).
            double inWater = lap < lapInWater.Count ? lapInWater[lap] : double.NaN;
            if (lap == 0)
            {
                if (!double.IsNaN(inWater) ? t < inWater : (Entry.HasValue ? t < Entry.Value : !double.IsNaN(breakout) && t < breakout))
                    return "ΕΚΚΙΝΗΣΗ";
                if (!double.IsNaN(breakout) && t < breakout)
                    return "ΥΠΟΒΡΥΧΙΑ";
            }
            else if (!double.IsNaN(breakout) && t < breakout)
            {
                double push = !double.IsNaN(inWater) ? inWater : lapFeetOff[lap];
                if (double.IsNaN(push) || t < push)
                    return "ΣΤΡΟΦΗ";
                return "ΥΠΟΒΡΥΧΙΑ";
            }

            return SwimAthleteList.Normalize(SwimPresets.StyleName(StyleOfLap(lap)));
        }

        /// <summary>
        /// (2.31.0) The swimmer is in the water at this time, its speed shown: from the entry of the start or the push off a wall (marked,
        /// else estimated) to the next wall; not before the start or after the finish.
        /// </summary>
        public bool InWater(double t)
        {
            if (!HasModel || t < 0 || (Finish.HasValue && t >= Finish.Value))
                return false;

            int lap = LapAt(t);
            double from = lap < lapInWater.Count ? lapInWater[lap] : double.NaN;
            return !double.IsNaN(from) && t >= from - 1e-6;
        }

        /// <summary>(2.31.0) Under the water at this time (after the start or a turn, before its breakout): the speed with ≈.</summary>
        public bool Underwater(double t)
        {
            return InWater(t) && UnderwaterSpans.Any(span => t >= span.From - 1e-6 && t < span.To);
        }

        /// <summary>True while the swimmer swims (after the breakout of the lap, before its wall).</summary>
        public bool Swimming(double t)
        {
            if (t < 0 || (Finish.HasValue && t >= Finish.Value))
                return false;

            int lap = LapAt(t);
            double breakout = lap < lapBreakouts.Count ? lapBreakouts[lap] : double.NaN;
            return double.IsNaN(breakout) ? lap > 0 || (Entry.HasValue && t > Entry.Value) : t >= breakout;
        }
        #endregion

        #region Helpers
        public static string Seconds(double v)
        {
            return v.ToString("0.00", CultureInfo.InvariantCulture) + " s";
        }

        private static string Lower(string text)
        {
            return string.IsNullOrEmpty(text) ? "" : text.ToLower(CultureInfo.GetCultureInfo("el-GR"));
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0)
                return 0;

            List<double> s = values.OrderBy(v => v).ToList();
            int n = s.Count;
            return n % 2 == 1 ? s[n / 2] : 0.5 * (s[n / 2 - 1] + s[n / 2]);
        }
        #endregion
    }
}
