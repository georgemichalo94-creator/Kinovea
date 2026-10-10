#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
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
        public double? Vel;
        public double? Strokes;
        public double? Sr;
        public double? Dps;
        public double? SrExact;
        public string Style;
        public bool IsSwim;
    }

    public class SwimTurnComputed
    {
        public int Wall;
        public double? TurnTime;
        public double? UnderwaterTime;
        public int? Kicks;
        public double? LastStrokeGap;
    }

    public class SwimComputed
    {
        public List<SwimRow> Rows = new List<SwimRow>();
        public List<SwimTurnComputed> Turns = new List<SwimTurnComputed>();
        public double? ReactionTime;
        public double? BlockTime;
        public double? EntryTime;
        public double? StartUnderwaterTime;
        public int? StartKicks;
        public double? FinishLastStrokeGap;
        public int TotalPresses;
        public double? FinalTime;
        public List<string> Warnings = new List<string>();

        /// <summary>Lists of event times (seconds after the start), sorted.</summary>
        public List<double> Strokes = new List<double>();
        public List<double> Kicks = new List<double>();
        public List<double> Breakouts = new List<double>();
        public List<double> FeetOff = new List<double>();
        public List<double> Entries = new List<double>();
        public List<double> Reactions = new List<double>();
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

            if (race.StartTimestamp == null || timing == null || !timing.IsValid)
            {
                foreach (int d in race.Checkpoints)
                    result.Rows.Add(new SwimRow { D = d, Style = SwimPresets.StyleAt(race, d) });
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

                if (row.T.HasValue && tPrev.HasValue)
                {
                    row.SegTime = row.T.Value - tPrev.Value;
                    if (row.SegTime.Value > 0)
                        row.Vel = (row.D - row.PrevD) / row.SegTime.Value;
                }

                if (row.T.HasValue)
                {
                    if (lastT.HasValue && row.T.Value <= lastT.Value)
                        result.Warnings.Add(string.Format("Ο χρόνος στα {0}μ. δεν είναι μεγαλύτερος από τον προηγούμενο.", row.D));
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

                if (row.IsSwim && row.Strokes.HasValue && row.Strokes.Value > 0 && row.SegTime.HasValue && row.SegTime.Value > 0)
                {
                    row.Sr = (row.Strokes.Value / ppc) * 60.0 / row.SegTime.Value;
                    row.Dps = (row.D - row.PrevD) / row.Strokes.Value;
                }
            }

            // Start.
            double firstWallT = barriers.Count > 0 ? barriers[0] : double.MaxValue;
            result.ReactionTime = First(result.Reactions, t => t > 0 && t < firstWallT);
            result.BlockTime = First(result.FeetOff, t => t > 0 && t < firstWallT);
            result.EntryTime = First(result.Entries, t => t > 0 && t < firstWallT);
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

                double tw;
                if (!timeAt.TryGetValue(wall, out tw))
                    continue;

                double nextT = First(wallTimes, t => t > tw + 1e-9) ?? double.MaxValue;
                double prevT = Last(wallTimes, t => t < tw - 1e-9) ?? 0;

                double? pushoff = First(result.FeetOff, t => t > tw && t < nextT);
                double? breakout = First(result.Breakouts, t => t > tw && t < nextT);
                if (pushoff.HasValue)
                    turn.TurnTime = pushoff.Value - tw;
                if (breakout.HasValue)
                    turn.UnderwaterTime = breakout.Value - (pushoff ?? tw);
                if (result.Kicks.Count > 0)
                {
                    double to = breakout ?? nextT;
                    turn.Kicks = result.Kicks.Count(t => t > tw && t <= to);
                }

                double? lastStroke = Last(result.Strokes, t => t < tw && t > prevT);
                if (lastStroke.HasValue)
                    turn.LastStrokeGap = tw - lastStroke.Value;
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
            }

            return result;
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

        private static double Round(double x, double step)
        {
            if (step <= 0)
                return Math.Round(x * 100) / 100;

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
