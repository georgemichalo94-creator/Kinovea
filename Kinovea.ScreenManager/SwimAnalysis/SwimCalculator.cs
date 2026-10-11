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
    /// Converts video timestamps to race time (seconds after the start signal),
    /// using the same arithmetic as the player time code (frame based, high speed factor aware).
    /// </summary>
    public class SwimTiming
    {
        public double AverageTimestampsPerFrame;
        public double FrameIntervalMilliseconds;
        public double HighSpeedFactor = 1.0;

        public bool IsValid
        {
            get { return AverageTimestampsPerFrame > 0 && FrameIntervalMilliseconds > 0; }
        }

        public double Seconds(long timestamp, long origin)
        {
            if (!IsValid)
                return 0;

            double frames = Math.Round((timestamp - origin) / AverageTimestampsPerFrame);
            double hsf = HighSpeedFactor <= 0 ? 1.0 : HighSpeedFactor;
            return frames * FrameIntervalMilliseconds / hsf / 1000.0;
        }
    }

    public class SwimRow
    {
        public int Index;
        public int D;
        public int PrevD;
        public double? T;
        public double? SegTime;

        /// <summary>
        /// Average velocity of the segment. Outside the clean swimming (start, turns, last 5 m before a wall) it is
        /// conventional: the checkpoint at a wall is the touch (hands or feet), not the head, and the segment
        /// contains the dive, the glide, the turn or the underwater phase.
        /// </summary>
        public double? Vel;

        /// <summary>25 yard pools: velocity in yd/s (Vel is always in m/s).</summary>
        public double? VelYd;
        public double? Strokes;

        /// <summary>Stroke rate (cycles/min), distance per stroke press (m) and cycle length (m): clean swimming only.</summary>
        public double? Sr;
        public double? Dps;
        public double? Sl;

        /// <summary>Stroke rate from the intervals between the presses inside the segment (cycles/min).</summary>
        public double? SrExact;
        public string Style;

        /// <summary>Starts at least 15 m after the last wall (no start or turn underwater phase).</summary>
        public bool IsSwim;

        /// <summary>
        /// Clean swimming: starts at least 15 m after the last wall and ends at least 5 m before the next one,
        /// like the "Ειδικοί τομείς" of the race sheets. Only these segments give stroke rate and length.
        /// </summary>
        public bool IsFree;

        /// <summary>Time of the reference at this checkpoint and difference (athlete − reference, negative = faster).</summary>
        public double? RefT;
        public double? Delta;
    }

    /// <summary>
    /// Average velocity between two distances (for the velocity chart).
    /// </summary>
    public class SwimSegment
    {
        public int D0;
        public int D1;
        public double V;

        /// <summary>Clean swimming (see SwimRow.IsFree). The other segments have a conventional velocity.</summary>
        public bool Free = true;
    }

    /// <summary>
    /// One 50 of the race (in pool units) with the ΔΑΚ (Δείκτης Απόδοσης Κολύμβησης) and the clean swimming averages.
    /// ΔΑΚ = (strokes + time from +15 to the wall) / (meters / 10), for every length of the 50: 50 m pool /3.5 as in the Excel,
    /// 25 m pool /1.0 per length, 25 yd pool /0.9144 per length (DakYd: per 10 yd).
    /// </summary>
    public class SwimBlock
    {
        public int From;
        public int To;
        public double? Time;
        public double? Strokes;
        public double? Swolf;
        public double? Dak;
        public double? DakYd;
        public double? Sr;
        public double? Dps;
        public double? Sl;
        public double? Vel;
        public double? VelYd;
        public string Style;
    }

    public class SwimTurnComputed
    {
        public int Wall;

        /// <summary>Style before and after the wall (they differ at a change of style in the medley).</summary>
        public string StyleIn;
        public string StyleOut;

        /// <summary>−5 → +15 (20 units around the wall), wall → +15 (head) and −5 → wall.</summary>
        public double? InOut;
        public double? Out15;
        public double? In5;

        /// <summary>Relay: end of a leg, the "turn" is the exchange (the next swimmer leaves the block).</summary>
        public bool Exchange;

        /// <summary>Flip turn: freestyle or backstroke, same style before and after the wall.</summary>
        public bool Flip;

        /// <summary>
        /// Flip turn: last hand entry → feet off. Open turn (butterfly, breaststroke, medley change of style): touch → feet off.
        /// Relay exchange: touch → feet of the next swimmer leave the block.
        /// </summary>
        public double? TurnTime;
        public double? UnderwaterTime;
        public int? Kicks;
        public double? LastStrokeGap;

        /// <summary>Times after the start signal: feet leave the wall (or the block, at an exchange), head breaks the surface.</summary>
        public double? Pushoff;
        public double? Breakout;
    }

    public class SwimComputed
    {
        public List<SwimRow> Rows = new List<SwimRow>();
        public List<SwimTurnComputed> Turns = new List<SwimTurnComputed>();

        /// <summary>The 50s of the race with the ΔΑΚ.</summary>
        public List<SwimBlock> Blocks = new List<SwimBlock>();

        /// <summary>Meters per pool unit (1 or 0.9144) and the label of the unit ("μ." or "yd").</summary>
        public double K = 1.0;
        public string Unit = "μ.";
        /// <summary>First movement after the signal (marked on the video). The official reaction time of the
        /// touch pads corresponds to the block release (BlockTime).</summary>
        public double? ReactionTime;
        public double? BlockTime;
        public double? EntryTime;

        /// <summary>Flight: feet leave the block → hands enter the water.</summary>
        public double? FlightTime;
        public double? StartUnderwaterTime;
        public int? StartKicks;
        public double? FinishLastStrokeGap;

        /// <summary>Last 5 and last 15 units of the race.</summary>
        public double? FinishLast5;
        public double? FinishLast15;
        public int TotalPresses;
        public double? FinalTime;
        public double? AvgVel;

        /// <summary>Signal → head at 15 units.</summary>
        public double? Start15;

        /// <summary>Mean of the ΔΑΚ of the 50s.</summary>
        public double? AvgDak;

        /// <summary>Clean swimming averages, weighted by time and distance: Σcycles·60/Σtime, Σd/Σstrokes, Σd/Σcycles.</summary>
        public double? AvgSr;
        public double? AvgDps;
        public double? AvgSl;

        /// <summary>Marking problems and implausible values, to check on the video.</summary>
        public List<string> Warnings = new List<string>();

        /// <summary>Lists of event times (seconds after the start), sorted.</summary>
        public List<double> Strokes = new List<double>();
        public List<double> Kicks = new List<double>();
        public List<double> Breakouts = new List<double>();
        public List<double> FeetOff = new List<double>();
        public List<double> Entries = new List<double>();
        public List<double> Reactions = new List<double>();

        /// <summary>Reference used for the deltas (null if none or not the same event).</summary>
        public SwimReference Reference;
        public List<SwimSegment> ReferenceSegments = new List<SwimSegment>();
    }

    /// <summary>
    /// Local computation of the analysis, mirroring the Google Sheets backend (swimAnalyze_),
    /// so the panel shows the same numbers even when offline.
    /// </summary>
    public static class SwimCalculator
    {
        public static SwimComputed Compute(SwimRace race, SwimTiming timing, double strokeRounding)
        {
            SwimComputed result = new SwimComputed();
            if (race == null)
                return result;

            double k = race.K;
            result.K = k;
            result.Unit = race.Unit;

            if (race.Reference != null && race.Reference.Matches(race))
            {
                result.Reference = race.Reference;
                result.ReferenceSegments = Segments(race.Reference);
            }

            if (race.StartTimestamp == null || timing == null || !timing.IsValid)
            {
                foreach (int d in race.Checkpoints)
                {
                    SwimRow empty = new SwimRow { D = d, Style = SwimPresets.StyleAt(race, d) };
                    if (result.Reference != null)
                        empty.RefT = result.Reference.TimeAt(d);
                    result.Rows.Add(empty);
                }

                return result;
            }

            long origin = race.StartTimestamp.Value;
            Func<SwimMarkType, List<double>> times = type => race.Marks
                .Where(m => m.Type == type)
                .Select(m => timing.Seconds(m.Timestamp, origin))
                .OrderBy(t => t)
                .ToList();

            result.Strokes = times(SwimMarkType.Stroke);
            result.Kicks = times(SwimMarkType.Kick);
            result.Breakouts = times(SwimMarkType.Breakout);
            result.FeetOff = times(SwimMarkType.FeetOff);
            result.Entries = times(SwimMarkType.Entry);
            result.Reactions = times(SwimMarkType.Reaction);
            result.TotalPresses = result.Strokes.Count;

            int pool = race.Pool <= 0 ? 50 : race.Pool;
            int distance = race.Distance;

            // Checkpoint times.
            Dictionary<int, double> timeAt = new Dictionary<int, double>();
            for (int i = 0; i < race.Checkpoints.Count; i++)
            {
                SwimMark split = race.GetSplit(race.Checkpoints[i]);
                SwimRow row = new SwimRow();
                row.Index = i;
                row.D = race.Checkpoints[i];
                row.PrevD = i == 0 ? 0 : race.Checkpoints[i - 1];
                row.Style = SwimPresets.StyleAt(race, row.D);
                if (split != null)
                {
                    row.T = timing.Seconds(split.Timestamp, origin);
                    timeAt[row.D] = row.T.Value;
                }

                if (result.Reference != null)
                {
                    row.RefT = result.Reference.TimeAt(row.D);
                    if (row.T.HasValue && row.RefT.HasValue)
                        row.Delta = row.T.Value - row.RefT.Value;
                }

                result.Rows.Add(row);
            }

            List<int> turnWalls = new List<int>();
            for (int w = pool; w < distance; w += pool)
                turnWalls.Add(w);

            List<double> barriers = turnWalls.Where(w => timeAt.ContainsKey(w)).Select(w => timeAt[w]).OrderBy(t => t).ToList();
            bool hasStrokes = result.Strokes.Count > 0;

            double? lastT = null;
            foreach (SwimRow row in result.Rows)
            {
                double? tPrev = row.Index == 0 ? 0.0 : result.Rows[row.Index - 1].T;
                int ppc = SwimPresets.PressesPerCycle(row.Style);
                int lastWall = (int)Math.Floor((row.PrevD + 1e-9) / pool) * pool;
                row.IsSwim = row.PrevD >= lastWall + 15;
                row.IsFree = row.IsSwim && row.D <= lastWall + pool - 5;

                if (row.T.HasValue && tPrev.HasValue)
                {
                    row.SegTime = row.T.Value - tPrev.Value;
                    if (row.SegTime.Value > 0)
                    {
                        // Velocities in m/s (in 25 yd pools also in yd/s).
                        row.Vel = (row.D - row.PrevD) * k / row.SegTime.Value;
                        if (race.IsYards)
                            row.VelYd = (row.D - row.PrevD) / row.SegTime.Value;
                    }
                }

                if (row.T.HasValue)
                {
                    if (lastT.HasValue && row.T.Value <= lastT.Value)
                        result.Warnings.Add(string.Format("Ο χρόνος στα {0}{1} δεν είναι μεγαλύτερος από τον προηγούμενο.", row.D, race.Unit));
                    lastT = row.T;
                }

                if (hasStrokes && row.T.HasValue && tPrev.HasValue)
                {
                    double nA = Round(Cumulative(result.Strokes, barriers, tPrev.Value), strokeRounding);
                    double nB = Round(Cumulative(result.Strokes, barriers, row.T.Value), strokeRounding);
                    row.Strokes = Math.Max(0, Round(nB - nA, strokeRounding > 0 ? strokeRounding : 0));

                    double sum = 0;
                    int count = 0;
                    for (int j = 0; j + 1 < result.Strokes.Count; j++)
                    {
                        double a = result.Strokes[j];
                        double b = result.Strokes[j + 1];
                        if (a < tPrev.Value - 1e-9)
                            continue;
                        if (b > row.T.Value + 1e-9)
                            break;
                        if (HasBarrier(barriers, a, b))
                            continue;
                        sum += b - a;
                        count++;
                    }

                    if (count > 0 && sum > 0)
                        row.SrExact = 60.0 / ((sum / count) * ppc);
                }

                // Stroke rate and length only in the clean swimming (15 m after the wall to 5 m before the next one),
                // like the race sheets of the Excel: in the last 5 m there is the glide and the start of the turn or of
                // the finish, and in the first 15 m the underwater phase, so the values there are not those of the swimming.
                if (row.IsFree && row.Strokes.HasValue && row.Strokes.Value > 0 && row.SegTime.HasValue && row.SegTime.Value > 0)
                {
                    double cycles = row.Strokes.Value / ppc;
                    row.Sr = cycles * 60.0 / row.SegTime.Value;
                    row.Dps = (row.D - row.PrevD) * k / row.Strokes.Value;
                    row.Sl = (row.D - row.PrevD) * k / cycles;
                }
            }

            // Clean swimming averages, weighted (a mean of the segment values would give the same weight to a 5 m and a 30 m segment).
            double sumCycles = 0, sumTime = 0, sumStrokes = 0, sumDistance = 0;
            foreach (SwimRow row in result.Rows.Where(r => r.Sr.HasValue))
            {
                sumCycles += row.Strokes.Value / SwimPresets.PressesPerCycle(row.Style);
                sumTime += row.SegTime.Value;
                sumStrokes += row.Strokes.Value;
                sumDistance += (row.D - row.PrevD) * k;
            }

            if (sumTime > 0)
                result.AvgSr = sumCycles * 60.0 / sumTime;
            if (sumStrokes > 0)
                result.AvgDps = sumDistance / sumStrokes;
            if (sumCycles > 0)
                result.AvgSl = sumDistance / sumCycles;

            // Start.
            double firstWallT = barriers.Count > 0 ? barriers[0] : double.MaxValue;
            result.ReactionTime = First(result.Reactions, t => t > 0 && t < firstWallT);
            result.BlockTime = First(result.FeetOff, t => t > 0 && t < firstWallT);
            result.EntryTime = First(result.Entries, t => t > 0 && t < firstWallT);
            if (result.BlockTime.HasValue && result.EntryTime.HasValue && result.EntryTime.Value > result.BlockTime.Value)
                result.FlightTime = result.EntryTime.Value - result.BlockTime.Value;
            result.StartUnderwaterTime = First(result.Breakouts, t => t > 0 && t < firstWallT);
            if (result.Kicks.Count > 0)
            {
                double fromT = result.EntryTime ?? 0;
                double to = result.StartUnderwaterTime ?? firstWallT;
                result.StartKicks = result.Kicks.Count(t => t > fromT && t <= to);
            }

            // Turns.
            List<double> wallTimes = result.Rows
                .Where(r => r.T.HasValue && IsWall(r.D, pool))
                .Select(r => r.T.Value)
                .OrderBy(t => t)
                .ToList();

            foreach (int wall in turnWalls)
            {
                SwimTurnComputed turn = new SwimTurnComputed();
                turn.Wall = wall;
                result.Turns.Add(turn);

                // Relays: at the end of each leg the "turn" is the exchange, and the next swimmer may leave the block
                // a little before the touch (negative exchange time, allowed down to −0.03 s).
                turn.Exchange = race.Relay && distance % 4 == 0 && wall % (distance / 4) == 0;

                // Flip turn: freestyle or backstroke, same style before and after the wall. The others (butterfly,
                // breaststroke, medley change of style) are open turns, with a touch of the hands.
                string styleIn = SwimPresets.StyleAt(race, wall);
                string styleOut = SwimPresets.StyleAt(race, wall + pool);
                turn.StyleIn = styleIn;
                turn.StyleOut = styleOut;
                turn.Flip = !turn.Exchange && (styleIn == "fs" || styleIn == "bk") && styleOut == styleIn;

                double tIn, tOut;
                bool hasIn = timeAt.TryGetValue(wall - 5, out tIn);
                bool hasOut = timeAt.TryGetValue(wall + 15, out tOut);
                if (hasIn && hasOut)
                    turn.InOut = tOut - tIn;

                double tw;
                if (!timeAt.TryGetValue(wall, out tw))
                    continue;

                if (hasOut)
                    turn.Out15 = tOut - tw;
                if (hasIn)
                    turn.In5 = tw - tIn;

                double nextT = First(wallTimes, t => t > tw + 1e-9) ?? double.MaxValue;
                double prevT = Last(wallTimes, t => t < tw - 1e-9) ?? 0;
                double fromT = turn.Exchange ? tw - 0.5 : tw;
                double? pushoff = First(result.FeetOff, t => t > fromT && t < nextT);
                double? breakout = First(result.Breakouts, t => t > tw && t < nextT);
                double? lastStroke = Last(result.Strokes, t => t < tw && t > prevT);
                turn.Pushoff = pushoff;
                turn.Breakout = breakout;
                if (lastStroke.HasValue)
                    turn.LastStrokeGap = tw - lastStroke.Value;

                // Turn time: flip turn from the last hand entry, open turn and exchange from the touch, to the feet off.
                if (pushoff.HasValue)
                {
                    if (!turn.Flip)
                        turn.TurnTime = pushoff.Value - tw;
                    else if (lastStroke.HasValue)
                        turn.TurnTime = pushoff.Value - lastStroke.Value;
                }

                if (breakout.HasValue)
                    turn.UnderwaterTime = breakout.Value - (pushoff ?? tw);
                if (result.Kicks.Count > 0)
                {
                    double to = breakout ?? nextT;
                    turn.Kicks = result.Kicks.Count(t => t > tw && t <= to);
                }
            }

            // Finish.
            double tFinish;
            if (timeAt.TryGetValue(distance, out tFinish))
            {
                result.FinalTime = tFinish;
                double? prevWall = Last(wallTimes, t => t < tFinish - 1e-9);
                double? lastStroke = Last(result.Strokes, t => t < tFinish && (!prevWall.HasValue || t > prevWall.Value));
                if (lastStroke.HasValue)
                    result.FinishLastStrokeGap = tFinish - lastStroke.Value;

                double t5, t15;
                if (timeAt.TryGetValue(distance - 5, out t5))
                    result.FinishLast5 = tFinish - t5;
                if (timeAt.TryGetValue(distance - 15, out t15))
                    result.FinishLast15 = tFinish - t15;
                if (tFinish > 0)
                    result.AvgVel = distance * k / tFinish;
            }

            double tStart15;
            if (timeAt.TryGetValue(15, out tStart15))
                result.Start15 = tStart15;

            result.Blocks = Blocks50(race, result, pool, k);
            List<double> daks = result.Blocks.Where(b => b.Dak.HasValue).Select(b => b.Dak.Value).ToList();
            if (daks.Count > 0)
                result.AvgDak = daks.Average();

            CheckPlausibility(race, result, pool);
            return result;
        }

        /// <summary>
        /// The 50s of the race with the ΔΑΚ, like swimBlocks50_ of the Google Sheet backend.
        /// </summary>
        private static List<SwimBlock> Blocks50(SwimRace race, SwimComputed result, int pool, double k)
        {
            List<SwimBlock> blocks = new List<SwimBlock>();
            Dictionary<int, SwimRow> byD = new Dictionary<int, SwimRow>();
            foreach (SwimRow r in result.Rows)
                byD[r.D] = r;

            for (int b = 0; b + 50 <= race.Distance; b += 50)
            {
                int e = b + 50;
                SwimRow re;
                if (!byD.TryGetValue(e, out re))
                    continue;

                SwimRow rs = null;
                if (b > 0)
                    byD.TryGetValue(b, out rs);

                SwimBlock block = new SwimBlock { From = b, To = e, Style = SwimPresets.StyleAt(race, e) };
                if (re.T.HasValue && (b == 0 || (rs != null && rs.T.HasValue)))
                    block.Time = re.T.Value - (b == 0 ? 0 : rs.T.Value);

                int from = b, to = e;
                List<SwimRow> inBlock = result.Rows.Where(r => r.PrevD >= from && r.D <= to).ToList();
                if (inBlock.Any(r => r.Strokes.HasValue))
                    block.Strokes = inBlock.Sum(r => r.Strokes ?? 0);
                if (block.Time.HasValue && block.Strokes.HasValue)
                    block.Swolf = block.Time.Value + block.Strokes.Value;
                if (block.Time.HasValue && block.Time.Value > 0)
                {
                    block.Vel = 50 * k / block.Time.Value;
                    if (race.IsYards)
                        block.VelYd = 50 / block.Time.Value;
                }

                // ΔΑΚ: for every length of the 50, from +15 to the wall.
                double sumStrokes = 0, sumTime = 0;
                int sumWidth = 0;
                bool ok = true, any = false;
                for (int w = b; w < e; w += pool)
                {
                    SwimRow a15, aw;
                    if (!byD.TryGetValue(w + 15, out a15) || !byD.TryGetValue(Math.Min(w + pool, e), out aw) ||
                        !a15.T.HasValue || !aw.T.HasValue || aw.D <= a15.D)
                    {
                        ok = false;
                        break;
                    }

                    int d0 = a15.D, d1 = aw.D;
                    foreach (SwimRow r in result.Rows.Where(x => x.PrevD >= d0 && x.D <= d1))
                    {
                        if (r.Strokes.HasValue)
                        {
                            any = true;
                            sumStrokes += r.Strokes.Value;
                        }
                    }

                    sumTime += aw.T.Value - a15.T.Value;
                    sumWidth += d1 - d0;
                }

                if (ok && any && sumWidth > 0)
                {
                    block.Dak = (sumStrokes + sumTime) / (sumWidth * k / 10.0);
                    if (race.IsYards)
                        block.DakYd = (sumStrokes + sumTime) / (sumWidth / 10.0);
                }

                // Clean swimming of the 50, weighted.
                double cycles = 0, time = 0, strokes = 0, meters = 0;
                foreach (SwimRow r in inBlock.Where(x => x.Sr.HasValue))
                {
                    cycles += r.Strokes.Value / SwimPresets.PressesPerCycle(r.Style);
                    time += r.SegTime.Value;
                    strokes += r.Strokes.Value;
                    meters += (r.D - r.PrevD) * k;
                }

                if (time > 0)
                    block.Sr = cycles * 60.0 / time;
                if (strokes > 0)
                    block.Dps = meters / strokes;
                if (cycles > 0)
                    block.Sl = meters / cycles;

                blocks.Add(block);
            }

            return blocks;
        }

        /// <summary>
        /// Average velocity of the reference between its consecutive known times (from the start signal at 0 m).
        /// </summary>
        public static List<SwimSegment> Segments(SwimReference reference)
        {
            List<SwimSegment> segments = new List<SwimSegment>();
            int d0 = 0;
            double t0 = 0;
            int pool = reference.Pool <= 0 ? 50 : reference.Pool;
            double k = reference.PoolUnit == SwimRace.UnitYards ? SwimRace.Yard : 1.0;
            foreach (SwimReferenceSplit s in reference.Times.OrderBy(x => x.D))
            {
                if (s.D > d0 && s.T > t0)
                {
                    segments.Add(new SwimSegment { D0 = d0, D1 = s.D, V = (s.D - d0) * k / (s.T - t0), Free = IsFreeSegment(d0, s.D, pool) });
                    d0 = s.D;
                    t0 = s.T;
                }
            }

            return segments;
        }

        /// <summary>
        /// Clean swimming segment: from at least 15 m after a wall to at least 5 m before the next one.
        /// </summary>
        public static bool IsFreeSegment(int d0, int d1, int pool)
        {
            if (pool <= 0 || d1 <= d0)
                return false;

            int lastWall = (int)Math.Floor((d0 + 1e-9) / pool) * pool;
            return d0 >= lastWall + 15 && d1 <= lastWall + pool - 5;
        }

        /// <summary>
        /// Number of stroke presses since the last marked checkpoint (live counter for the current segment).
        /// </summary>
        public static int PressesSinceLastSplit(SwimComputed computed)
        {
            double lastSplit = 0;
            foreach (SwimRow row in computed.Rows)
            {
                if (row.T.HasValue && row.T.Value > lastSplit)
                    lastSplit = row.T.Value;
            }

            return computed.Strokes.Count(t => t > lastSplit);
        }

        public static bool IsWall(int d, int pool)
        {
            return pool > 0 && d % pool == 0;
        }

        #region Plausibility checks
        // Wide limits on purpose: they catch marking errors (a missed or a double press, a split on the wrong
        // checkpoint, a wrong slow motion factor), not a slow or a fast swimmer.
        private const double MinVelocity = 0.4;
        private const double MaxCleanVelocity = 2.7;
        private const double MaxVelocity = 3.4;
        private const double MinSr = 15;
        private const double MaxSr = 80;

        private class StrokeInterval
        {
            public string Style;
            public double A;
            public double B;
        }

        private static void CheckPlausibility(SwimRace race, SwimComputed result, int pool)
        {
            // A wrong slow motion factor scales all the times: one warning instead of one per value.
            if (CheckTimeScale(result))
                return;

            CheckStart(result);
            CheckVelocities(result);
            CheckStrokeRates(result);
            CheckStrokeIntervals(result);
            CheckTurns(result);
            CheckBreakouts(result, pool);
            CheckCleanSegments(race, result, pool);
        }

        /// <summary>
        /// Start events in their natural order: first movement → block release → hands entry → breakout.
        /// The block release is what the touch pads give as the official reaction time (about 0.55–0.95 s).
        /// </summary>
        private static void CheckStart(SwimComputed result)
        {
            string[] names = { "η αντίδραση", "η αποδέσμευση από τον βατήρα", "η είσοδος στο νερό", "η έξοδος από το νερό" };
            string[] after = { "την αντίδραση", "την αποδέσμευση από τον βατήρα", "την είσοδο στο νερό", "την έξοδο από το νερό" };
            double?[] times = { result.ReactionTime, result.BlockTime, result.EntryTime, result.StartUnderwaterTime };
            int previous = -1;
            for (int i = 0; i < times.Length; i++)
            {
                if (!times[i].HasValue)
                    continue;

                if (previous >= 0 && times[i].Value <= times[previous].Value)
                {
                    result.Warnings.Add(Format("Εκκίνηση: {0} ({1:0.00} s) πρέπει να είναι μετά {2} ({3:0.00} s). Έλεγξε τα σημάδια.",
                        names[i], times[i].Value, after[previous], times[previous].Value));
                }

                previous = i;
            }

            if (result.BlockTime.HasValue && (result.BlockTime.Value < 0.35 || result.BlockTime.Value > 1.3))
            {
                result.Warnings.Add(Format("Εκκίνηση: αποδέσμευση από τον βατήρα στα {0:0.00} s από το σήμα (συνήθως 0.55–0.95 s). Έλεγξε το καρέ του σήματος εκκίνησης.",
                    result.BlockTime.Value));
            }
        }

        private static bool CheckTimeScale(SwimComputed result)
        {
            List<double> clean = result.Rows.Where(r => r.IsFree && r.Vel.HasValue).Select(r => r.Vel.Value).ToList();
            if (clean.Count < 2)
                return false;

            double median = Median(clean);
            if (median >= 0.6 && median <= 2.6)
                return false;

            result.Warnings.Add(Format("Η ταχύτητα κολύμβησης βγαίνει {0:0.00} m/s (διάμεσος): δεν είναι ρεαλιστική. Έλεγξε τον συντελεστή αργής κίνησης (high speed) του βίντεο και ότι τα περάσματα είναι στα σωστά σημεία.", median));
            return true;
        }

        private static void CheckVelocities(SwimComputed result)
        {
            List<string> found = new List<string>();
            foreach (SwimRow row in result.Rows)
            {
                double max = row.IsFree ? MaxCleanVelocity : MaxVelocity;
                if (row.Vel.HasValue && (row.Vel.Value < MinVelocity || row.Vel.Value > max))
                    found.Add(Format("{0}–{1}{2} ({3:0.00} m/s)", row.PrevD, row.D, result.Unit, row.Vel.Value));
            }

            if (found.Count > 0)
                result.Warnings.Add("Μη ρεαλιστική ταχύτητα στο τμήμα " + Join(found) + ": έλεγξε τα δύο περάσματα του τμήματος.");
        }

        private static void CheckStrokeRates(SwimComputed result)
        {
            List<string> found = result.Rows
                .Where(r => r.Sr.HasValue && (r.Sr.Value < MinSr || r.Sr.Value > MaxSr))
                .Select(r => Format("{0}–{1}{2} ({3:0.0})", r.PrevD, r.D, result.Unit, r.Sr.Value))
                .ToList();
            if (found.Count > 0)
                result.Warnings.Add("Συχνότητα εκτός ορίων (15–80 κύκλοι/λεπτό) στο τμήμα " + Join(found) + ": έλεγξε τα πατήματα χεριάς και τα περάσματα.");

            // Cycle length of each style. In freestyle and backstroke every hand entry is one press (two per cycle):
            // one press per cycle doubles the length.
            foreach (IGrouping<string, SwimRow> group in result.Rows.Where(r => r.Sl.HasValue).GroupBy(r => r.Style))
            {
                int ppc = SwimPresets.PressesPerCycle(group.Key);
                double cycles = group.Sum(r => r.Strokes.Value / ppc);
                double distance = group.Sum(r => (double)(r.D - r.PrevD) * result.K);
                if (cycles <= 0)
                    continue;

                double sl = distance / cycles;
                if (sl > 3.4)
                {
                    result.Warnings.Add(Format("Μήκος κύκλου {0:0.00} m ({1}): πολύ μεγάλο. {2}", sl, StyleName(group.Key),
                        ppc == 2 ? "Στο ελεύθερο και στο ύπτιο πατάς σε κάθε είσοδο χεριού (δύο πατήματα ανά κύκλο)." : "Μήπως λείπουν πατήματα χεριάς;"));
                }
                else if (sl < 0.9)
                {
                    result.Warnings.Add(Format("Μήκος κύκλου {0:0.00} m ({1}): πολύ μικρό. Μήπως υπάρχουν διπλά πατήματα χεριάς;", sl, StyleName(group.Key)));
                }
            }
        }

        /// <summary>
        /// A missed stroke press gives an interval about twice the usual one and a double press a very short one.
        /// Checked in the clean swimming only (no underwater phase, no glide into the wall), against the median
        /// interval of the same style.
        /// </summary>
        private static void CheckStrokeIntervals(SwimComputed result)
        {
            // Windows of clean swimming (contiguous clean segments of the same style are merged).
            List<double> windowStart = new List<double>();
            List<double> windowEnd = new List<double>();
            List<string> windowStyle = new List<string>();
            foreach (SwimRow row in result.Rows)
            {
                if (!row.IsFree || !row.T.HasValue || row.Index == 0)
                    continue;

                double? t0 = result.Rows[row.Index - 1].T;
                if (!t0.HasValue || row.T.Value <= t0.Value)
                    continue;

                int n = windowEnd.Count;
                if (n > 0 && Math.Abs(windowEnd[n - 1] - t0.Value) < 1e-9 && windowStyle[n - 1] == row.Style)
                {
                    windowEnd[n - 1] = row.T.Value;
                }
                else
                {
                    windowStart.Add(t0.Value);
                    windowEnd.Add(row.T.Value);
                    windowStyle.Add(row.Style);
                }
            }

            List<StrokeInterval> intervals = new List<StrokeInterval>();
            List<double> s = result.Strokes;
            for (int j = 0; j + 1 < s.Count; j++)
            {
                for (int k = 0; k < windowStart.Count; k++)
                {
                    if (s[j] >= windowStart[k] - 1e-9 && s[j + 1] <= windowEnd[k] + 1e-9)
                    {
                        intervals.Add(new StrokeInterval { Style = windowStyle[k], A = s[j], B = s[j + 1] });
                        break;
                    }
                }
            }

            List<string> missed = new List<string>();
            List<string> doubled = new List<string>();
            foreach (IGrouping<string, StrokeInterval> group in intervals.GroupBy(x => x.Style))
            {
                List<double> durations = group.Select(x => x.B - x.A).ToList();
                if (durations.Count < 6)
                    continue;

                double median = Median(durations);
                if (median <= 0)
                    continue;

                foreach (StrokeInterval x in group)
                {
                    double d = x.B - x.A;
                    if (d > 1.75 * median)
                        missed.Add(Format("{0:0.00}–{1:0.00} s", x.A, x.B));
                    else if (d < 0.4 * median)
                        doubled.Add(Format("{0:0.00} s", x.B));
                }
            }

            if (missed.Count > 0)
                result.Warnings.Add("Χεριές: μεγάλο κενό ανάμεσα σε δύο πατήματα στα " + Join(missed) + " (σχεδόν διπλάσιο από το συνηθισμένο): μήπως λείπει ένα πάτημα;");
            if (doubled.Count > 0)
                result.Warnings.Add("Χεριές: πολύ κοντινά πατήματα στα " + Join(doubled) + ": μήπως διπλό πάτημα;");
        }

        /// <summary>
        /// Turn time is about 1.0–2.1 s in a flip turn (from the last hand entry) and 0.9–1.5 s in an open turn (from the
        /// touch); an exchange is usually 0.1–0.5 s. A much longer time means a split on the wrong checkpoint (the wall
        /// time is not the wall), a missing last stroke press before a flip turn or a missing feet off mark at this wall.
        /// </summary>
        private static void CheckTurns(SwimComputed result)
        {
            List<string> found = new List<string>();
            foreach (SwimTurnComputed turn in result.Turns)
            {
                string name = (turn.Exchange ? "αλλαγή " : "στροφή ") + turn.Wall.ToString(CultureInfo.InvariantCulture) + result.Unit;
                if (turn.TurnTime.HasValue && turn.TurnTime.Value > (turn.Exchange ? 1.5 : (turn.Flip ? 3.0 : 2.5)))
                    found.Add(Format("{0}: χρόνος {1:0.00} s", name, turn.TurnTime.Value));
                else if (turn.UnderwaterTime.HasValue && turn.UnderwaterTime.Value > 12)
                    found.Add(Format("{0}: υποβρύχια {1:0.00} s", name, turn.UnderwaterTime.Value));
            }

            if (found.Count > 0)
                result.Warnings.Add("Στροφές: " + Join(found) + ". Έλεγξε την επαφή στον τοίχο (μήπως το πέρασμα γράφτηκε σε άλλο σημείο), την τελευταία χεριά πριν την ανάποδη στροφή, την αποκόλληση και την έξοδο.");

            // Flip turns are timed from the last hand entry: without the last press the gap to the wall is one stroke longer.
            List<SwimTurnComputed> flips = result.Turns.Where(x => x.Flip && x.LastStrokeGap.HasValue).ToList();
            if (flips.Count < 3)
                return;

            double usual = Median(flips.Select(x => x.LastStrokeGap.Value).ToList());
            List<string> gaps = flips
                .Where(x => x.LastStrokeGap.Value > 1.6 * usual && x.LastStrokeGap.Value - usual > 0.35)
                .Select(x => Format("{0}{1} ({2:0.00} s)", x.Wall, result.Unit, x.LastStrokeGap.Value))
                .ToList();
            if (gaps.Count > 0)
            {
                result.Warnings.Add(Format("Ανάποδες στροφές: η τελευταία χεριά είναι πολύ πριν τον τοίχο στα {0}, ενώ συνήθως {1:0.00} s. Μήπως λείπει το πάτημα της τελευταίας χεριάς; Από αυτή μετράει ο χρόνος στροφής.",
                    Join(gaps), usual));
            }
        }

        /// <summary>
        /// The head must break the surface within 15 m of the start and of each turn. A breakout after the checkpoint
        /// of +15 m is a marking error (or a disqualification), and the next segment would not be clean swimming.
        /// </summary>
        private static void CheckBreakouts(SwimComputed result, int pool)
        {
            List<int> walls = new List<int> { 0 };
            List<double?> breakouts = new List<double?> { result.StartUnderwaterTime };
            List<string> names = new List<string> { "εκκίνηση" };
            foreach (SwimTurnComputed turn in result.Turns)
            {
                walls.Add(turn.Wall);
                breakouts.Add(turn.Breakout);
                names.Add((turn.Exchange ? "αλλαγή " : "στροφή ") + turn.Wall.ToString(CultureInfo.InvariantCulture) + result.Unit);
            }

            List<string> found = new List<string>();
            for (int i = 0; i < walls.Count; i++)
            {
                if (!breakouts[i].HasValue)
                    continue;

                int wall = walls[i];
                SwimRow row = result.Rows.FirstOrDefault(r => r.D >= wall + 15 && r.D < wall + pool && r.T.HasValue);
                if (row != null && breakouts[i].Value > row.T.Value + 0.1)
                    found.Add(Format("{0}: {1:0.00} s, μετά το πέρασμα των {2}{3} ({4:0.00} s)", names[i], breakouts[i].Value, row.D, result.Unit, row.T.Value));
            }

            if (found.Count > 0)
                result.Warnings.Add("Έξοδος από το νερό μετά τα 15" + result.Unit + " (" + Join(found) + "). Έλεγξε το σημάδι εξόδου: το επόμενο τμήμα μετράει ως καθαρή κολύμβηση.");
        }

        /// <summary>
        /// Stroke rate and length need at least one segment between +15 m and −5 m of the walls in every length.
        /// </summary>
        private static void CheckCleanSegments(SwimRace race, SwimComputed result, int pool)
        {
            if (result.Strokes.Count == 0 || pool < 25 || race.Distance <= 0)
                return;

            List<string> lengths = new List<string>();
            for (int wall = 0; wall < race.Distance; wall += pool)
            {
                int end = Math.Min(wall + pool, race.Distance);
                if (!result.Rows.Any(r => r.IsFree && r.PrevD >= wall && r.D <= end))
                    lengths.Add(Format("{0}–{1}{2}", wall, end, result.Unit));
            }

            if (lengths.Count > 0)
            {
                result.Warnings.Add(Format("Δεν υπάρχει τμήμα καθαρής κολύμβησης {0} {1}, οπότε εκεί δεν βγαίνουν συχνότητα και μήκος χεριάς. Χρειάζονται δύο σημεία ελέγχου ανάμεσα στα +15 και −5 από τους τοίχους (π.χ. {2} και {3}{4}).",
                    lengths.Count == 1 ? "στο μήκος" : "στα μήκη", Join(lengths), 15, pool - 5, result.Unit));
            }
        }

        private static string Format(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }

        /// <summary>
        /// The first items of a list, so a warning stays short.
        /// </summary>
        private static string Join(List<string> items)
        {
            const int max = 3;
            string text = string.Join(", ", items.Take(max).ToArray());
            if (items.Count > max)
                text += Format(" και {0} ακόμη", items.Count - max);

            return text;
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0)
                return 0;

            List<double> sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        private static string StyleName(string style)
        {
            int i = Array.IndexOf(SwimPresets.Styles, style);
            if (i < 0 || i >= SwimPresets.StyleNames.Length)
                return style;

            string name = SwimPresets.StyleNames[i];
            int p = name.IndexOf(" (", StringComparison.Ordinal);
            return (p > 0 ? name.Substring(0, p) : name).ToLowerInvariant();
        }
        #endregion

        private static double Round(double x, double step)
        {
            // Same rounding as the sheet (JavaScript Math.round: halves away from zero for these positive values).
            if (step <= 0)
                return Math.Round(x * 100, MidpointRounding.AwayFromZero) / 100;

            return Math.Round(x / step + 1e-9, MidpointRounding.AwayFromZero) * step;
        }

        private static int UpperBound(List<double> sorted, double t)
        {
            int lo = 0;
            int hi = sorted.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (sorted[mid] <= t)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return lo;
        }

        private static bool HasBarrier(List<double> barriers, double a, double b)
        {
            foreach (double x in barriers)
            {
                if (x > a && x < b)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Cumulative stroke count at time t, interpolated between two presses (gives the half strokes at
        /// checkpoints). No interpolation across a wall: the swimmer is turning / underwater there.
        /// </summary>
        private static double Cumulative(List<double> strokes, List<double> barriers, double t)
        {
            int k = UpperBound(strokes, t);
            if (k == 0 || k == strokes.Count)
                return k;

            double a = strokes[k - 1];
            double b = strokes[k];
            if (b <= a || HasBarrier(barriers, a, b))
                return k;

            return k + (t - a) / (b - a);
        }

        private static double? First(List<double> list, Func<double, bool> predicate)
        {
            foreach (double x in list)
            {
                if (predicate(x))
                    return x;
            }

            return null;
        }

        private static double? Last(List<double> list, Func<double, bool> predicate)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (predicate(list[i]))
                    return list[i];
            }

            return null;
        }
    }
}
