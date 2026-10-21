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
        public int Strokes;
        public double? Tempo;
        public double? Dps;
        public double? Speed;
    }

    /// <summary>
    /// Technical view of a race (as the swimming reels): distance-time model through the checkpoints, the stroke cycles
    /// with their tempo, distance and speed, the phase at each moment, the badges and the summary of the race.
    /// Times from the marks of the analysis (the automatic strokes included); the distances between the checkpoints
    /// come from a smooth monotone curve through them (an estimate, shown with ≈).
    /// </summary>
    public sealed class SwimReel
    {
        private readonly List<double> knotT = new List<double>();
        private readonly List<double> knotD = new List<double>();

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

        /// <summary>Stroke marks closer to their neighbours than a cycle (a second press on the same stroke): left out.</summary>
        public readonly List<double> DoubleMarks = new List<double>();

        /// <summary>Stroke marks used for the cycles, by lap.</summary>
        private readonly List<List<double>> lapStrokes = new List<List<double>>();
        private readonly List<string> lapStyles = new List<string>();
        private readonly List<double> lapBreakouts = new List<double>();
        private readonly List<double> lapStarts = new List<double>();
        private readonly List<double> lapFeetOff = new List<double>();

        public double? Entry;
        public double? FirstBreakout;

        /// <summary>Breakout distances from the wall (pool units) by lap, measured by the analyst or estimated (≈).</summary>
        public readonly Dictionary<int, double> BreakoutDistances = new Dictionary<int, double>();
        public readonly HashSet<int> BreakoutMeasured = new HashSet<int>();

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

            // Distance-time model: the start and the checkpoints with a time, both increasing.
            reel.knotT.Add(0);
            reel.knotD.Add(0);
            // (On the video: the frame of a mark when its time is the official one beside it.)
            foreach (SwimRow row in computed.Rows.Where(r => r.T.HasValue).OrderBy(r => r.OnVideo))
            {
                if (row.OnVideo <= reel.knotT[reel.knotT.Count - 1] + 1e-6 || row.D <= reel.knotD[reel.knotD.Count - 1])
                    continue;

                reel.knotT.Add(row.OnVideo);
                reel.knotD.Add(row.D);
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
            reel.BuildCycles();
            reel.BuildBreakouts(race, computed);
            reel.BuildBadges(race, computed);
            reel.BuildParts(race);
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

                lapStrokes.Add(RemoveDoubleMarks(strokes));
            }
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

        private void BuildCycles()
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
                    number++;
                    Cycles.Add(new SwimReelCycle { Start = t0, Time = t1, Tempo = tempo, Dps = dps, Speed = dps / tempo, Lap = lap, Number = number });
                }
            }
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
                if (lap == 0)
                {
                    measured = race.BreakoutDist ?? race.UnderwaterDist;
                }
                else
                {
                    SwimTurnManual t;
                    if (race.Turns != null && race.Turns.TryGetValue(wall, out t) && t != null)
                        measured = t.UnderwaterDist;
                }

                if (measured.HasValue && measured.Value > 0)
                {
                    BreakoutDistances[lap] = measured.Value;
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
                if (d > 0 && d < Pool)
                    BreakoutDistances[lap] = d;
            }
        }

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
                string detail = race.EntryDistHands.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("χέρια στο νερό στα {0:0.0} {1}"), race.EntryDistHands.Value, UnitShort)
                    : SwimLang.T("χέρια στο νερό");
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
        private void BuildParts(SwimRace race)
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
                if (t0.HasValue && t1.HasValue && t1.Value > t0.Value)
                {
                    part.Time = t1.Value - t0.Value;
                    part.Speed = (part.To - part.From) * K / part.Time.Value;
                }

                double a = t0 ?? double.NaN, b = t1 ?? double.NaN;
                if (!double.IsNaN(a) && !double.IsNaN(b))
                {
                    part.Strokes = lapStrokes.SelectMany(s => s).Count(s => s > a && s <= b);
                    List<SwimReelCycle> inside = Cycles.Where(c => c.Time > a && c.Time <= b).ToList();
                    if (inside.Count > 0)
                    {
                        part.Tempo = inside.Average(c => c.Tempo);
                        part.Dps = inside.Average(c => c.Dps);
                    }
                }

                Parts.Add(part);
            }
        }

        private double? TimeAtCheckpoint(int distance)
        {
            if (distance == 0)
                return 0;

            int i = knotD.IndexOf(distance);
            return i >= 0 ? (double?)knotT[i] : null;
        }
        #endregion

        #region Model
        /// <summary>
        /// Distance of the race (pool units) at a time after the start, from the curve through the checkpoints; after the last
        /// checkpoint marked, on with the speed of the last segment up to the next checkpoint.
        /// </summary>
        public double DistanceAt(double t)
        {
            return Position(t, true);
        }

        /// <summary>Time (s after the start) at a distance up to the last checkpoint marked, from the curve.</summary>
        public double TimeAtDistance(double d)
        {
            int n = knotT.Count;
            if (n < 2 || d <= 0)
                return 0;
            if (d >= knotD[n - 1])
                return knotT[n - 1];

            double lo = 0, hi = knotT[n - 1];
            for (int i = 0; i < 50; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Position(mid, true) < d)
                    lo = mid;
                else
                    hi = mid;
            }

            return 0.5 * (lo + hi);
        }

        /// <summary>stopAtCheckpoint: not beyond the next checkpoint (where the swimmer is shown), else up to the finish (cycles).</summary>
        private double Position(double t, bool stopAtCheckpoint)
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
                bool wall = knotD[i] < Distance && Math.Abs(knotD[i] / Pool - Math.Round(knotD[i] / Pool)) < 1e-9;
                if (!wall && i < n - 1)
                    continue;

                // The knots first..i are one length (or what is marked of it).
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

        /// <summary>Speed (m/s) at a time after the start, from the curve through the checkpoints.</summary>
        public double SpeedAt(double t)
        {
            const double dt = 0.02;
            return (DistanceAt(t + dt) - DistanceAt(t - dt)) / (2 * dt) * K;
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

        private static double EndSlope(double h0, double h1, double m0, double m1)
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

        /// <summary>Stroke marks of the current length up to this time.</summary>
        public int StrokesInLength(double t)
        {
            int lap = LapAt(t);
            return lap < lapStrokes.Count ? lapStrokes[lap].Count(s => s <= t + 1e-6) : 0;
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
            if (lap == 0)
            {
                if (Entry.HasValue && t < Entry.Value)
                    return "ΕΚΚΙΝΗΣΗ";
                if (!Entry.HasValue && !double.IsNaN(breakout) && t < breakout)
                    return "ΕΚΚΙΝΗΣΗ";
                if (!double.IsNaN(breakout) && t < breakout)
                    return "ΥΠΟΒΡΥΧΙΑ";
            }
            else if (!double.IsNaN(breakout) && t < breakout)
            {
                double feetOff = lapFeetOff[lap];
                if (double.IsNaN(feetOff) || t < feetOff)
                    return "ΣΤΡΟΦΗ";
                return "ΥΠΟΒΡΥΧΙΑ";
            }

            return SwimAthleteList.Normalize(SwimPresets.StyleName(StyleOfLap(lap)));
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
