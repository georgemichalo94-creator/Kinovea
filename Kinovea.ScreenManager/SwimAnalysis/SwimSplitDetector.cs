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
    /// <summary>A checkpoint found on a lap followed: the frame (index of the frames read, fractions allowed) of each cue.</summary>
    public sealed class SwimSplitFound
    {
        /// <summary>Distance from the wall where the lap started (15, 25, 35 or 45 in a 50 m pool).</summary>
        public int LapDistance;

        /// <summary>Head over the mark of the far rope (the one above the swimmer in the image), of the near rope, and their middle.</summary>
        public readonly Dictionary<string, double> Cues = new Dictionary<string, double>();

        /// <summary>0–1.</summary>
        public double Confidence;
    }

    /// <summary>
    /// Checkpoints of a lap followed by the camera (stands), from the marks of the lane ropes: the red floats at 15 m and 25 m
    /// from each wall and the red last 5 m before each wall.
    ///
    /// 1. head: the front of the moving part of the swimmer along the band (band difference above 35% of its maximum near the
    ///    swimmer, towards where he swims), median over ±0.12 s;
    /// 2. the red parts of the two ropes of the lane in each frame (SwimStrokeDetector.RedRuns): short ones are marks, long ones the
    ///    last 5 m; each is matched to the previous frame with the camera motion;
    /// 3. a crossing: the head passes the mark (or the start of the last 5 m) between two frames; the crossings of the same mark
    ///    on the far and the near rope (less than 1 s apart) are one checkpoint with two cues;
    /// 4. distances: the marks passed in order are 15, 25, 35 from the wall of the lap, the start of the long red part is 45
    ///    (pool of 50 m), with the time from the start of the lap to choose when one is missing.
    /// The analyst marks the head at the mark of the far rope: that cue counts most, the learning moves and weighs each cue.
    /// </summary>
    public static class SwimSplitDetector
    {
        public const string CueFar = "far";
        public const string CueNear = "near";
        public const string CueMiddle = "mid";

        /// <summary>
        /// The line between the two marks a quarter of the lane from the near rope, and three quarters (the head seen higher in
        /// the lane: above the water, camera high). The learning finds which one the analyst follows in each video.
        /// </summary>
        public const string CueQuarterNear = "l25";
        public const string CueQuarterFar = "l75";

        /// <summary>Debug output (null: none).</summary>
        public static Action<string> Trace;

        /// <summary>Fastest time per metre of a lap (s/m), start or push included: no checkpoint before it.</summary>
        private const double FastestPace = 0.33;

        /// <summary>
        /// Checkpoints of the lap. direction: +1 if the swimmer goes to larger x in the image, −1 if to smaller x.
        /// fps: frames per second of the reading. lapStart: frame index where the lap started (start signal or wall), can be
        /// negative (before the frames read). pool: length of the pool (only 50 has the 15/25/35/45 marks used).
        /// </summary>
        public static List<SwimSplitFound> Detect(SwimStrokeResult result, int direction, double fps, double lapStart, int pool)
        {
            List<SwimSplitFound> found = new List<SwimSplitFound>();
            if (result == null || result.Path == null || result.Lanes.Count == 0 || fps <= 0)
                return found;

            int T = Math.Min(result.Path.Length, Math.Min(result.Lanes.Count, Math.Min(result.Motion.Count, Math.Min(result.RedFar.Count, result.RedNear.Count))));
            if (T < 10)
                return found;

            double[] head = Heads(result, T, direction, fps);
            double earliestEnd = lapStart + (pool - 5) * FastestPace * fps;
            List<Checkpoint> points = Checkpoints(result, head, T, direction, fps, earliestEnd);
            if (Trace != null)
            {
                foreach (Checkpoint c in points.OrderBy(c => Time(c.Cues)))
                    Trace(string.Format(System.Globalization.CultureInfo.InvariantCulture, "checkpoint {0}: {1}", c.Long ? "long" : "mark",
                        string.Join(" ", c.Cues.Select(k => k.Key + "=" + k.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))));
            }

            // After the start of the red last 5 m, the short red parts are pieces of it. The red 5 m of the wall the lap started
            // from (the swimmer leaves them after a turn) are not the end: the end comes after the time of the fastest swimmer to it.
            List<Dictionary<string, double>> ends = points.Where(c => c.Long && Time(c.Cues) >= earliestEnd).Select(c => c.Cues).OrderBy(c => Time(c)).ToList();
            double firstEnd = ends.Count > 0 ? ends.Min(e => Time(e)) : double.MaxValue;
            List<Dictionary<string, double>> marks = points.Where(c => !c.Long && Time(c.Cues) < firstEnd - 0.3 * fps).Select(c => c.Cues).OrderBy(c => Time(c)).ToList();

            // Distances of the marks passed from the wall of the lap: 15, 25, 35 in a pool of 50 (the marks 15 from each wall and
            // the middle); 10 and 15 in a pool of 25 (the marks 15 from each wall).
            int[] distances = pool == 50 ? new[] { 15, 25, 35 } : new[] { 10, 15 };
            List<double> times = marks.Select(m => Time(m)).ToList();

            // The start of the red last 5 m, when seen, is on the pace of the swimmer too: the marks before it get the distances
            // that bring him there at that pace (a reading that starts late, from the click, sees the 25 m mark first, not the 15).
            // Without a choice that fits it (the red seen at another moment), the marks alone.
            int[] assigned = null;
            if (EndInAssign && ends.Count > 0)
            {
                assigned = Assign(times, lapStart, fps, distances, Time(ends[0]), pool - 5);
                if (assigned.All(a => a <= 0))
                    assigned = null;
            }

            if (assigned == null)
                assigned = Assign(times, lapStart, fps, distances, double.NaN, 0);
            for (int i = 0; i < marks.Count; i++)
            {
                if (assigned[i] <= 0)
                    continue;

                SwimSplitFound f = new SwimSplitFound { LapDistance = assigned[i], Confidence = MarkConfidence(marks[i].Keys) };
                foreach (KeyValuePair<string, double> c in marks[i])
                    f.Cues[c.Key] = c.Value;
                found.Add(f);
            }

            // The start of the red last 5 m (45 in a 50 m pool, 20 in a 25 m pool): the first one after the last mark.
            double after = found.Count > 0 ? found.Max(f => Time(f.Cues)) : lapStart;
            Dictionary<string, double> end = ends.Where(e => Time(e) > after + 0.5 * fps).OrderBy(e => Time(e)).FirstOrDefault();
            if (end != null)
            {
                SwimSplitFound f = new SwimSplitFound { LapDistance = pool - 5, Confidence = end.ContainsKey(CueMiddle) ? 0.75 : 0.5 };
                foreach (KeyValuePair<string, double> c in end)
                    f.Cues[c.Key] = c.Value;
                found.Add(f);
            }

            // The marks of one rope, and the quarter lines, as the line between the marks of the two ropes (MarkDelay).
            if (pool == 50 && MarkDelays)
            {
                foreach (SwimSplitFound f in found)
                {
                    foreach (string k in f.Cues.Keys.ToList())
                        f.Cues[k] += MarkDelay(k, f.LapDistance) * fps;
                }
            }

            return found.OrderBy(f => f.LapDistance).ToList();
        }

        /// <summary>
        /// Delay (s) to add to the crossing of a line of the lane at a checkpoint of a 50 m pool (lap distance 15, 25, 35, 45) for
        /// the crossing of the line between the marks of the two ropes, which the analysts see: the far rope's mark alone is crossed
        /// later than that line at 15 m and earlier at 35 and 45 m, the near rope's mark the other way round, the quarter lines in
        /// between (the lines of the marks lean in the image the more the further the swimmer is along the lap, the same in both
        /// directions of the pool). Medians of the analysts' races of all the folders (2026-10-16, 883 checkpoints, the same in every
        /// style): far +0.14 / +0.03 / -0.17 / -0.46 s off at 15 / 25 / 35 / 45 m, near -0.34 / +0.03 / +0.34 / +0.58 s. Fitted on
        /// one half of the races and tried on the other: checkpoints more than 0.5 s off 22 -> 17 and 42 -> 35, those with one rope
        /// only median 0.25 -> 0.12 s. The line between the marks itself stays.
        /// </summary>
        public static double MarkDelay(string cue, int lapDistance)
        {
            int i = Array.IndexOf(new[] { 15, 25, 35, 45 }, lapDistance);
            if (i < 0)
                return 0;

            switch (cue)
            {
                case CueFar: return new[] { -0.14, -0.03, 0.17, 0.46 }[i];
                case CueQuarterFar: return new[] { -0.02, -0.04, 0.05, 0.19 }[i];
                case CueQuarterNear: return new[] { 0.21, -0.03, -0.20, -0.33 }[i];
                case CueNear: return new[] { 0.34, -0.03, -0.34, -0.58 }[i];
                default: return 0;
            }
        }

        /// <summary>The marks of one rope and the quarter lines with their delay by distance (tests).</summary>
        internal static bool MarkDelays = true;

        /// <summary>
        /// Delay (s) to add, on top of MarkDelay, to a cue of the 15 m of a lap after a turn in a 50 m pool (not in backstroke): there
        /// the swimmer comes up near the line and the arms of the first strokes and their splash reach it before the head the analysts
        /// mark, while after the start the 15 m is read on time. Analysts' races of all the folders (2026-10): after a turn the 15 m
        /// came 0.12-0.16 s early in freestyle, butterfly and breaststroke (the middle line 0.17 s), on time in backstroke.
        /// </summary>
        public static double FifteenDelay(string cue, bool afterTurn, string style)
        {
            if (!FifteenDelays || !afterTurn || style == "bk" || cue == CuePace || cue == CuePaceOne)
                return 0;

            return cue == CueMiddle ? FifteenMiddle : FifteenOther;
        }

        /// <summary>The 15 m of a lap after a turn with its delay (tests and lab variants).</summary>
        internal static bool FifteenDelays = true;
        internal static double FifteenMiddle = 0.17, FifteenOther = 0.07;

        /// <summary>
        /// Frame where the search of the breakout of a length ends: the main time of its 15 m (MainFrame) before the FifteenDelay of
        /// that cue (50 m pools). The delay makes the checkpoint right, but the steepest rise of the first strokes after the
        /// breakout must stay outside the search (2026-10-18: with the window up to the delayed 15 m some turn breakouts went to
        /// the first stroke, e.g. +0.26 -> +1.44 s).
        /// </summary>
        public static double BreakoutSearchEnd(Dictionary<string, double> cues15, bool afterTurn, string style, double fps, int pool)
        {
            if (cues15 == null || cues15.Count == 0)
                return double.NaN;

            string key = cues15.ContainsKey(CueMiddle) ? CueMiddle : cues15.ContainsKey(CueFar) ? CueFar : cues15.ContainsKey(CueNear) ? CueNear : cues15.Keys.First();
            return cues15[key] - (pool == 50 ? FifteenDelay(key, afterTurn, style) * fps : 0);
        }

        /// <summary>FifteenDelay on the checkpoints of a length just read (50 m pools); afterTurn: the length starts at a wall, not with a dive.</summary>
        public static void AddFifteenDelay(List<SwimSplitFound> found, double fps, int pool, bool afterTurn, string style)
        {
            if (found == null || pool != 50)
                return;

            foreach (SwimSplitFound f in found)
            {
                if (f.LapDistance != 15)
                    continue;

                foreach (string k in f.Cues.Keys.ToList())
                    f.Cues[k] += FifteenDelay(k, afterTurn, style) * fps;
            }
        }

        /// <summary>The cues of a checkpoint that MarkDelay moves (their learning is apart from the answers kept before it).</summary>
        public static bool HasMarkDelay(string cue)
        {
            return MarkDelays && (cue == CueFar || cue == CueNear || cue == CueQuarterNear || cue == CueQuarterFar);
        }

        /// <summary>The start of the red last 5 m names the marks before it too (tests).</summary>
        internal static bool EndInAssign = true;

        // The confidence shown with a proposal is about the share of such proposals within 0.2 s of the analysts' time (test
        // races of camcorders and phones, 2026-10-15): the analyst sees which ones to look at closely.

        /// <summary>A checkpoint from the marks: crossed with the line between them 77 %, the far mark only 65 %, the near one only 26 %.</summary>
        public static double MarkConfidence(ICollection<string> cues)
        {
            if (cues.Contains(CueMiddle))
                return 0.8;
            if (cues.Count == 1 && cues.Contains(CueFar))
                return 0.65;
            if (cues.Count == 1 && cues.Contains(CueNear))
                return 0.3;
            return 0.55;
        }

        /// <summary>A checkpoint from the pace of the others (its mark not seen): 59 %.</summary>
        public const double PaceConfidence = 0.5;

        /// <summary>A wall from its cues: the head or the body seen at the wall 50-66 %, the swimmer stopped and the pace 43 %, the pace alone 29 %.</summary>
        public static double WallConfidence(IEnumerable<string> cues)
        {
            HashSet<string> seen = new HashSet<string>(cues.Select(k => { int dot = k.IndexOf('.'); return dot > 0 ? k.Substring(0, dot) : k; }));
            if (seen.Contains(CueWallHead) || seen.Contains(CueWallBody))
                return 0.6;
            return seen.Contains(CueStop) ? 0.45 : 0.3;
        }

        /// <summary>The checkpoints far from the time of the length go (tests).</summary>
        internal static bool LengthMarks = true;

        /// <summary>
        /// Seconds after the wall found from which a fast turn of the camera ends the reading of a length (0: never): the frames
        /// after it (the camera turning to the scoreboard, a cut) do not move the path before the wall.
        /// </summary>
        public static double PathAfterWall = 1.0;

        /// <summary>
        /// The frame (index of a reading) where the camera starts turning fast after 'after' (more than 3% of the width a frame,
        /// three frames in a row: to the scoreboard, a cut), 0.2 s before it; −1 if it does not.
        /// </summary>
        public static int CameraBreak(SwimStrokeResult result, double after, double fps)
        {
            if (result == null || result.Shifts == null || double.IsNaN(after) || fps <= 0)
                return -1;

            int run = 0;
            for (int t = Math.Max(1, (int)Math.Ceiling(after)); t < result.Shifts.Count; t++)
            {
                double[] s = result.Shifts[t];
                bool fast = false;
                if (s != null && s.Length > 0)
                {
                    foreach (double v in s)
                    {
                        if (double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > 0.03 * s.Length)
                        {
                            fast = true;
                            break;
                        }
                    }
                }

                run = fast ? run + 1 : 0;
                if (run >= 3)
                    return Math.Max((int)Math.Ceiling(after), t - 2 - (int)Math.Round(0.2 * fps));
            }

            return -1;
        }

        /// <summary>
        /// The checkpoints found of a length after the first one (frames; before AddPace) far from where a swimmer is at their
        /// distance in a length of its time go (the path left him: ahead of him after a turn, behind him at the end, a mark of
        /// another length): in the analysed races the mark at d comes at (d − h)/(pool − h) of the time of the length (h: 4.8 m
        /// after a dive, −1.4 m after a turn), within 2% of it. Off by more than 10% of the length the time of the length before
        /// gives (previousSeconds × 1.1 after the length of a dive, × 1.0 after a turn; 20% in another style) and, when the wall
        /// is seen (the swimmer stopping, the ends of the ropes), by more than 6% of the length it gives: the checkpoint goes.
        /// With two checkpoints or more, what they are off by together (the start of the lap off) is not counted.
        /// </summary>
        public static void KeepWithLength(List<SwimSplitFound> found, Dictionary<string, double> wall, double lapStart, double fps, int pool,
            bool dive, double previousSeconds, bool previousDive, bool sameStyle)
        {
            if (!LengthMarks || found == null || found.Count == 0 || pool <= 0 || fps <= 0 || double.IsNaN(lapStart) || previousSeconds <= 0)
                return;

            double h = dive ? 4.8 : -1.4;
            double expected = previousSeconds * (previousDive ? 1.1 : 1.0) * fps;
            double tolerance = (sameStyle ? 0.10 : 0.20) * expected;
            double seenWall = double.NaN;
            if (wall != null)
            {
                List<double> seen = wall.Where(c => c.Key == CueStop || c.Key == CueWallHead || c.Key == CueWallBody).Select(c => c.Value).ToList();
                if (seen.Count > 0)
                    seenWall = SwimLearningMedian(seen) - lapStart;
            }

            // Two checkpoints or more: their common offset (the start of the lap a little off, a wall found late) does not count
            // (up to 8% of the length).
            Func<double, double> offset = length =>
            {
                if (found.Count < 2 || length <= 0)
                    return 0;
                List<double> r = found.Select(f => Time(f.Cues) - lapStart - (f.LapDistance - h) / (pool - h) * length).ToList();
                double m = SwimLearningMedian(r);
                return Math.Max(-0.08 * length, Math.Min(0.08 * length, m));
            };

            double offPrevious0 = offset(expected);
            double offWall0 = double.IsNaN(seenWall) || seenWall <= 0 ? 0 : offset(seenWall);
            found.RemoveAll(f =>
            {
                double t = Time(f.Cues) - lapStart;
                double share = (f.LapDistance - h) / (pool - h);
                bool offPrevious = Math.Abs(t - share * expected - offPrevious0) > tolerance;
                bool offWall = double.IsNaN(seenWall) || seenWall <= 0 || Math.Abs(t - share * seenWall - offWall0) > 0.06 * seenWall;
                return offPrevious && offWall;
            });
        }

        /// <summary>The time from the pace of the swimmer in the lap (the checkpoints found before and after).</summary>
        public const string CuePace = "pace";

        /// <summary>
        /// Usual time of the last 5 m to the wall against the 10 m before it, as the analysts mark the wall (analyses of 50 m
        /// pools): the finish and the open turns (hand touch) about half, the flip turn of freestyle (feet touch) a little more.
        /// </summary>
        public static double LastFiveRatio(string kind, string style)
        {
            if (kind == "flip")
                return style == "fs" ? 0.55 : 0.51;
            return kind == "open" ? 0.505 : 0.50;
        }

        /// <summary>
        /// The pace of the swimmer in the lap, from the checkpoints found (frames of the reading): the swimmers keep their speed
        /// between the marks (the next 10 m take 0.96–1.04 of the time of the 10 m before), the last 5 m to the wall take
        /// 'lastFiveRatio' of the 10 m before. The checkpoints not found are added with the cue "pace" only (low confidence: the
        /// analyst checks them), the wall gets the cue "pace". Needs two checkpoints found; only in the frames read (0 to frames − 1).
        /// Returns the wall of the pace (frames, also after the frames read: the reading goes on to it; NaN: no pace).
        /// </summary>
        public static double AddPace(List<SwimSplitFound> found, Dictionary<string, double> wall, int pool, double lastFiveRatio, int frames)
        {
            if (found == null || pool <= 0)
                return double.NaN;

            List<SwimSplitFound> seen = found.Where(x => x.Cues.Count > 0 && !(x.Cues.Count == 1 && x.Cues.ContainsKey(CuePace))).ToList();
            if (seen.Count < 2)
                return double.NaN;

            // Time per metre and time at the wall of the lap (straight line through the checkpoints: medians of the slopes of
            // each pair and of the intercepts, so that one wrong checkpoint does not move it).
            double slope, intercept;
            if (!PaceLine(seen, out slope, out intercept))
                return double.NaN;

            // A checkpoint far from the pace of the others (more than PaceTolerance of the time of 10 m: a mark crossed at another
            // moment, the swimmer hidden) becomes the pace of the others (the analyst checks it); with four checkpoints the pace
            // stands without it. The analysts' own checkpoints of a length are within 0.05 of it; 0.1 tried: a wrong checkpoint
            // then moves the line and a right one goes instead (test races).
            double tolerance = PaceTolerance * 10 * slope;
            if (seen.Count >= 4)
            {
                List<SwimSplitFound> far = seen.Where(f => Math.Abs(Time(f.Cues) - (intercept + slope * f.LapDistance)) > tolerance).ToList();
                if (far.Count > 0 && seen.Count - far.Count >= 2)
                {
                    List<SwimSplitFound> kept = seen.Where(f => !far.Contains(f)).ToList();
                    double s2, i2;
                    if (PaceLine(kept, out s2, out i2))
                    {
                        slope = s2;
                        intercept = i2;
                        tolerance = PaceTolerance * 10 * slope;
                        foreach (SwimSplitFound f in far)
                        {
                            f.Cues.Clear();
                            f.Cues[CuePace] = intercept + slope * f.LapDistance;
                            f.Confidence = PaceConfidence;
                        }

                        seen = kept;
                    }
                }
            }

            // Four checkpoints and one of them wrong: it also moves the line of the others (half of the pairs have it), enough to
            // stay within it. Each one against the line of the other three, the farthest goes when it is off by more than
            // PaceLooTolerance of the time of 10 m (wider than for the line of all four: the line of three that leaves out the 45 m is
            // that of the faster first part of a sprint; 0.2 moved a right 45 m of a 50 m backstroke). Test races, 192 lengths
            // with four checkpoints: two such (45 m -2.59 s, 95 m +1.31 s), then within 0.6 s; no other.
            if (seen.Count == 4 && PaceLeaveOneOut)
            {
                SwimSplitFound worst = null;
                double worstRatio = 1.0, worstSlope = 0, worstIntercept = 0;
                foreach (SwimSplitFound f in seen)
                {
                    double s3, i3;
                    if (!PaceLine(seen.Where(x => x != f).ToList(), out s3, out i3))
                        continue;

                    double ratio = Math.Abs(Time(f.Cues) - (i3 + s3 * f.LapDistance)) / (PaceLooTolerance * 10 * s3);
                    if (ratio > worstRatio)
                    {
                        worst = f;
                        worstRatio = ratio;
                        worstSlope = s3;
                        worstIntercept = i3;
                    }
                }

                if (worst != null)
                {
                    slope = worstSlope;
                    intercept = worstIntercept;
                    tolerance = PaceTolerance * 10 * slope;
                    worst.Cues.Clear();
                    worst.Cues[CuePace] = intercept + slope * worst.LapDistance;
                    worst.Confidence = PaceConfidence;
                    seen = seen.Where(x => x != worst).ToList();
                }
            }

            int[] marks = pool == 50 ? new[] { 15, 25, 35, 45 } : new[] { 10, 15, 20 };
            foreach (int d in marks)
            {
                if (found.Any(x => x.LapDistance == d))
                    continue;

                double t = intercept + slope * d;
                if (t < 0 || t > frames - 1)
                    continue;

                SwimSplitFound added = new SwimSplitFound { LapDistance = d, Confidence = PaceConfidence };
                added.Cues[CuePace] = t;
                found.Add(added);
            }

            found.Sort((a, b) => a.LapDistance.CompareTo(b.LapDistance));
            SwimSplitFound end = found.FirstOrDefault(x => x.LapDistance == pool - 5);
            double endOnLine = intercept + slope * (pool - 5);
            double tEnd = end != null && seen.Contains(end) && Math.Abs(Time(end.Cues) - endOnLine) <= tolerance ? Time(end.Cues) : endOnLine;
            double tWall = tEnd + lastFiveRatio * 10 * slope;

            // The swimmer does not stop far before the wall his pace gets him to: such a stop (the path left him for the still
            // water) and the ends of the ropes it let in are not the wall; the reading goes on until the frames reach it.
            if (StopPaceCheck && wall != null && tWall > 0)
            {
                double early = Math.Max(lastFiveRatio * 10 * slope, 0.3 * 10 * slope);
                foreach (string k in new[] { CueStop, CueWallHead, CueWallBody })
                {
                    double t;
                    if (wall.TryGetValue(k, out t) && t < tWall - early)
                        wall.Remove(k);
                }
            }

            if (wall != null && tWall >= 0 && tWall <= frames - 1)
                wall[CuePace] = tWall;
            return tWall;
        }

        /// <summary>A stop or an end of the ropes far before the wall the pace of the swimmer gets him to is not the wall (tests).</summary>
        internal static bool StopPaceCheck = true;

        /// <summary>Part of the time of 10 m a checkpoint can be off the pace of the others (tests).</summary>
        internal static double PaceTolerance = 0.2;

        /// <summary>With four checkpoints each one against the pace of the other three (tests).</summary>
        internal static bool PaceLeaveOneOut = true;

        /// <summary>Part of the time of 10 m a checkpoint can be off the pace of the other three (tests).</summary>
        internal static double PaceLooTolerance = 0.3;

        /// <summary>The time of the wall from the one checkpoint seen in the length and the start of the length.</summary>
        public const string CuePaceOne = "pace1";

        /// <summary>
        /// The wall of a length where one checkpoint only was seen (a swimmer far or hidden), when it is the 35 or the 45 m of a pool
        /// of 50: from it and the start of the length (the start signal, the wall before). After a checkpoint the swimmer is slower
        /// than on average from the start of the length (the dive or the push and the gliding are faster): the time per metre to the
        /// wall is SinglePaceRatio times the one before (analyses of 69 races: the wall within 0.07–0.24 s of the analyst's,
        /// median). Cue "pace1" in the frames read (0 to frames − 1, lapStart in the same frames); returns its wall (frames, also
        /// after the frames read; NaN: none).
        /// </summary>
        public static double AddSinglePace(List<SwimSplitFound> found, Dictionary<string, double> wall, int pool, double lapStart, bool dive, string kind, int frames)
        {
            if (!SinglePace || found == null || pool != 50 || double.IsNaN(lapStart))
                return double.NaN;

            List<SwimSplitFound> seen = found.Where(x => x.Cues.Count > 0 && !(x.Cues.Count == 1 && x.Cues.ContainsKey(CuePace))).ToList();
            if (seen.Count != 1 || (seen[0].LapDistance != 35 && seen[0].LapDistance != 45))
                return double.NaN;

            int d = seen[0].LapDistance;
            double t = Time(seen[0].Cues);
            if (t <= lapStart)
                return double.NaN;

            double tWall = t + (pool - d) * SinglePaceRatio(dive, d, kind) * (t - lapStart) / d;
            if (wall != null && tWall >= 0 && tWall <= frames - 1)
                wall[CuePaceOne] = tWall;
            return tWall;
        }

        /// <summary>
        /// Time per metre from the checkpoint at d (35 or 45) to the wall against the one from the start of the length to it (medians
        /// of the analysed races: after the dive more than after a push; the analysts mark the feet in a flip turn).
        /// </summary>
        internal static double SinglePaceRatio(bool dive, int d, string kind)
        {
            if (dive)
            {
                if (d == 45)
                    return kind == "flip" ? 1.17 : kind == "open" ? 1.12 : 1.11;
                return kind == "flip" ? 1.18 : 1.15;
            }

            if (d == 45)
                return kind == "flip" ? 1.11 : kind == "open" ? 1.04 : 1.06;
            return kind == "flip" ? 1.11 : kind == "open" ? 1.05 : 1.08;
        }

        /// <summary>The wall from one checkpoint when only one was seen (tests).</summary>
        internal static bool SinglePace = true;

        /// <summary>
        /// The wall cues of a length with their usual delays (frames, keys "cue.kind…"): when the cues seen at the wall (the swimmer
        /// stopping, the ends of the ropes reached) agree with each other and not with the pace (a guess from the checkpoints
        /// before, wrong when one of them is), the pace goes.
        /// </summary>
        public static void PreferSeenWall(Dictionary<string, double> wall, double fps)
        {
            if (!WallConsensus || wall == null || fps <= 0)
                return;

            Func<string, string> cue = k => { int dot = k.IndexOf('.'); return dot > 0 ? k.Substring(0, dot) : k; };
            List<double> seen = wall.Where(c => cue(c.Key) == CueStop || cue(c.Key) == CueWallHead || cue(c.Key) == CueWallBody).Select(c => c.Value).ToList();
            List<string> pace = wall.Keys.Where(k => cue(k) == CuePace).ToList();
            if (seen.Count < 2 || pace.Count == 0)
                return;

            double median = SwimLearningMedian(new List<double>(seen));
            if (seen.Any(v => Math.Abs(v - median) > 0.3 * fps))
                return;

            foreach (string k in pace)
            {
                if (Math.Abs(wall[k] - median) > 0.4 * fps)
                    wall.Remove(k);
            }
        }

        /// <summary>The seen wall cues in agreement win over the pace (tests).</summary>
        internal static bool WallConsensus = false;

        /// <summary>Straight line time = intercept + slope × distance through checkpoints (medians of the pair slopes and of the intercepts).</summary>
        private static bool PaceLine(List<SwimSplitFound> seen, out double slope, out double intercept)
        {
            slope = intercept = 0;
            List<double> slopes = new List<double>();
            for (int i = 0; i < seen.Count; i++)
            {
                for (int j = i + 1; j < seen.Count; j++)
                {
                    int dd = seen[j].LapDistance - seen[i].LapDistance;
                    if (dd != 0)
                        slopes.Add((Time(seen[j].Cues) - Time(seen[i].Cues)) / dd);
                }
            }

            if (slopes.Count == 0)
                return false;

            double s = SwimLearningMedian(slopes);
            if (s <= 0)
                return false;

            slope = s;
            intercept = SwimLearningMedian(seen.Select(x => Time(x.Cues) - s * x.LapDistance).ToList());
            return true;
        }

        /// <summary>The motion around the swimmer becomes that of the swimming (breakout).</summary>
        public const string CueEnergy = "energy";

        /// <summary>The first strong splash of the swimming (breakout).</summary>
        public const string CueBurst = "burst";

        /// <summary>The steepest rise of the motion around the swimmer (breakout).</summary>
        public const string CueRise = "rise";

        /// <summary>The breakout after a turn with its own cues and delays (tests).</summary>
        public static bool TurnBreakout = true;

        /// <summary>
        /// After a turn in freestyle the motion of the swimming starting (cue "energy") counts as much as the steepest rise: the
        /// rise comes later with a breath or a stroke out of time (test races, 32 turns: energy within 0.3 s 84 %, rise 62 %, the
        /// rise 2-6.5 s late in 6). Weight 1 and spread 0.2 s as the rise instead of 0.6 and 0.5 s: phone breakouts more than
        /// 1 s off 8 -> 4 of 76 (q90 0.90 -> 0.59 s), the camcorders' three within 0.12 s of before (tests).
        /// </summary>
        public static bool TurnEnergyFreestyle = true;

        /// <summary>
        /// Usual delay (s) of the steepest rise of the motion after a turn from the breakout the analysts mark (analysed races:
        /// the first strokes after the head is out, about a second later in butterfly, 0.3 s in backstroke; in breaststroke the
        /// pull-out and the kick under the water come first, the rise 2.7 s after the head is out (median of 6 turns, camcorders
        /// and phones; 1.2 s from one race before 2026-10-15).
        /// </summary>
        public static double TurnRiseDelay(string style)
        {
            return TurnRiseDelay(style, false);
        }

        /// <summary>
        /// The same after a turn into another style ('change': the open turns of the medley). Butterfly 1.0 s: the camcorders
        /// of the races analysed before 1.2 s, the phones in the stands 0.25 s (22 turns: more than a second off 8 -> 5). From
        /// butterfly to backstroke the rise comes later than after a backstroke turn: 0.6 s after the head is out instead of 0.3 s
        /// (8 turns of 200 and 400 m medley: median error 0.67 -> 0.43 s, none more than a second off).
        /// </summary>
        public static double TurnRiseDelay(string style, bool change)
        {
            return TurnRiseDelay(style, change, 1.0);
        }

        /// <summary>
        /// The same for a swimmer seen from far by the share 'far' (FarShare): the delays above are those of the phones in the stands;
        /// a camcorder near the swimmer sees the head out sooner after the steepest rise: butterfly 1.25 s, from butterfly to
        /// backstroke 0.3 s (2026-10-18, 115 analysts' races: the camcorders' butterfly turns +0.30 s late with 1.0 s, +0.06 with
        /// 1.25; the phones' -0.26 with 1.25, -0.01 with 1.0).
        /// </summary>
        public static double TurnRiseDelay(string style, bool change, double far)
        {
            if (!TurnBreakout)
                return 0;
            far = Math.Max(0, Math.Min(1, far));
            switch (style)
            {
                case "fly": return TurnRiseFlyNear + far * (TurnRiseFly - TurnRiseFlyNear);
                case "br": return TurnRiseBreast;
                case "bk": return change ? TurnRiseBackChangeNear + far * (TurnRiseBackChange - TurnRiseBackChangeNear) : -0.3;
                default: return 0;
            }
        }

        /// <summary>See TurnRiseDelay (tests).</summary>
        internal static double TurnRiseBreast = -2.6, TurnRiseFly = -1.0, TurnRiseBackChange = -0.6;
        internal static double TurnRiseFlyNear = -1.25, TurnRiseBackChangeNear = -0.3;

        /// <summary>
        /// Share (0-1) of a swimmer seen from far (a phone in the stands) by his length at the working size, as FarCheckpointDelay:
        /// all of it up to FarSwimmerFull px, nothing from FarSwimmerNone px on; an unknown length counts as far (the delays used
        /// before 2026-10-18).
        /// </summary>
        public static double FarShare(double swimmerLength)
        {
            if (!ViewBreakout || double.IsNaN(swimmerLength) || swimmerLength <= 0 || swimmerLength <= FarSwimmerFull)
                return 1;
            if (swimmerLength >= FarSwimmerNone)
                return 0;
            return (FarSwimmerNone - swimmerLength) / (FarSwimmerNone - FarSwimmerFull);
        }

        /// <summary>The breakout delays by the view of the swimmer (tests and lab variants).</summary>
        internal static bool ViewBreakout = true;

        /// <summary>
        /// Usual delay (s) of the steepest rise after the start from the breakout the analysts mark: in breaststroke the first
        /// stroke at the surface comes 0.3 s after the head is out (10 starts, camcorders and phones: median error 0.53 -> 0.34 s,
        /// more than a second 2 -> 1); in the other styles the arms of the first stroke come 0.15 s after the head (2026-10-18:
        /// 86 starts in freestyle, butterfly and backstroke, the delay fitted on one half of the races and tried on the other:
        /// median error 0.25 -> 0.12 s and 0.17 -> 0.11 s, camcorders and phones alike).
        /// </summary>
        public static double StartRiseDelay(string style)
        {
            return StartRiseDelay(style, 1.0);
        }

        /// <summary>
        /// The same for a swimmer seen from far by the share 'far' (FarShare): the 0.3 s of breaststroke are those of the phones in
        /// the stands; a camcorder near the swimmer had the breaststroke starts on time without it (2026-10-18: 4 of 5 worse with it).
        /// </summary>
        public static double StartRiseDelay(string style, double far)
        {
            far = Math.Max(0, Math.Min(1, far));
            return style == "br" ? StartRiseBreastNear + far * (StartRiseBreast - StartRiseBreastNear) : StartRiseOther;
        }

        /// <summary>See StartRiseDelay (tests).</summary>
        internal static double StartRiseBreast = -0.3, StartRiseOther = -0.15, StartRiseBreastNear = 0;

        /// <summary>
        /// Delay (s) of the steepest rise after a turn from the first stroke counted after it (the strokes of the length start
        /// there): the delays of the breakout before 2026-10-15 (the strokes after a breaststroke turn are counted from the
        /// first one at the surface, already a little early).
        /// </summary>
        public static double TurnRiseStrokeDelay(string style)
        {
            if (!TurnBreakout)
                return 0;
            switch (style)
            {
                case "fly": return -1.25;
                case "br": return -1.1;
                case "bk": return -0.3;
                default: return 0;
            }
        }

        /// <summary>
        /// Delay (s) to add to the checkpoints of a swimmer seen from far (a phone in the stands: the swimmer a few dozen pixels
        /// long at the working size): there the arms and the splashes above the water ahead of his head are the front of the
        /// moving part. Analysed phone races (OAKA 2025 and 2026, 285 checkpoints, both years alike): backstroke 0.35 s early,
        /// breaststroke 0.25, butterfly and freestyle 0.1–0.15; camcorder views (swimmers of 80 px and longer) on time. All of it
        /// up to FarSwimmerFull px, nothing from FarSwimmerNone px on.
        /// </summary>
        public static double FarCheckpointDelay(string style, double swimmerLength)
        {
            if (double.IsNaN(swimmerLength) || swimmerLength <= 0 || swimmerLength >= FarSwimmerNone)
                return 0;
            double share = swimmerLength <= FarSwimmerFull ? 1.0 : (FarSwimmerNone - swimmerLength) / (FarSwimmerNone - FarSwimmerFull);
            double delay = style == "bk" ? FarDelayBack : style == "br" ? FarDelayBreast : FarDelayOther;
            return share * delay;
        }

        /// <summary>See FarCheckpointDelay (tests).</summary>
        internal static double FarSwimmerFull = 55, FarSwimmerNone = 85;
        internal static double FarDelayBack = 0.3, FarDelayBreast = 0.15, FarDelayOther = 0.1;

        /// <summary>
        /// Delay (s) of a cue of a turn ("open", "flip") from the pace of the length ("pace", "pace1") for a swimmer seen from far:
        /// the pace runs through the checkpoints as read, before their FarCheckpointDelay, so its turn came that much early in the
        /// phone videos (2026-10-18, 115 analysts' races, the turns of the phones: backstroke flip -0.40 s, butterfly -0.18,
        /// breaststroke -0.17, freestyle -0.04; the camcorders' near 0). A hand at the wall first was on time already and keeps
        /// none: the finish (the phones' backstroke +0.05, freestyle +0.10) and the medley turn from backstroke to breaststroke (a
        /// hand on the back: -0.12; with the delay 4 better, 7 worse). With it, the phones' walls: median 0.23 -> 0.20 s, more than
        /// 0.5 s 40 -> 32 of 223. The other cues: 0.
        /// </summary>
        public static double FarWallDelay(string cue, string kind, string style, double swimmerLength)
        {
            if (!FarWallPace || (cue != CuePace && cue != CuePaceOne) || (kind == "finish" && !FarWallFinish) || (kind == "open" && style == "bk"))
                return 0;
            return FarCheckpointDelay(style, swimmerLength);
        }

        /// <summary>The turn of the pace with the delay of the swimmer seen from far; the finish too (tests and lab variants).</summary>
        internal static bool FarWallPace = true, FarWallFinish = false;

        /// <summary>
        /// Usual delay (s) of a cue of the breakout ("energy", "burst") from the breakout the analysts mark (the head out). In
        /// breaststroke and backstroke the motion starts with the head out (0.1 s) and the first strong splash just after (0.2 s).
        /// In butterfly and freestyle too (analysed races, 2026-10: phone videos from the stands and most camcorder races within
        /// ±0.3 s; the second of delay used before came from one camcorder race, phone breakouts came 1.2 s late with it).
        /// </summary>
        public static double BreakoutDelay(string cue, string style)
        {
            bool breast = style == "br" || style == "bk";
            if (cue == CueEnergy)
                return breast ? 0.1 : 0.0;

            return breast ? 0.2 : 0.0;
        }

        /// <summary>
        /// Usual delay (s) of a cue of the breakout from the first stroke the analysts count after it, where the counting of the
        /// strokes of the length starts: in butterfly and freestyle about a second after the head is out (the delays of the
        /// breakout before 2026-10-15, kept for the strokes: the steepest rise does not count in freestyle); breaststroke and
        /// backstroke as the breakout.
        /// </summary>
        public static double StrokeStartDelay(string cue, string style)
        {
            bool breast = style == "br" || style == "bk";
            if (cue == CueEnergy)
                return breast ? 0.1 : 1.4;

            return breast ? 0.2 : 1.0;
        }

        /// <summary>The steepest rise of the breakout measured inside the window only (tests and lab variants).</summary>
        internal static bool RiseInsideWindow = true;

        /// <summary>
        /// The breakout after a wall (or the start): the head breaks the surface and the swimming starts, between the frames
        /// 'from' and 'to' (the 15 m mark: the rules want the head out before it). Underwater the water around the swimmer
        /// hardly moves; at the breakout the motion of the swimming starts (cue "energy": the motion within 1.2 lane widths of
        /// the swimmer, over 0.4 s, reaches 60% of its usual level in the swimming after 'to' and stays) with a strong splash
        /// (cue "burst": the first peak of the stroke signal as strong as the strokes); the motion rises fastest when the head and
        /// the arms come out (cue "rise": the steepest rise over 0.6 s; the kicks under the surface move the water before it,
        /// in backstroke for seconds). Frames of the reading, empty if not found.
        /// </summary>
        public static Dictionary<string, double> BreakoutCues(SwimStrokeResult result, double fps, int from, int to)
        {
            Dictionary<string, double> cues = new Dictionary<string, double>();
            if (result == null || result.Path == null || fps <= 0)
                return cues;

            int T = Math.Min(result.Path.Length, Math.Min(result.Lanes.Count, result.Motion.Count));
            from = Math.Max(1, from);
            to = Math.Min(T - 1, to);
            if (T < 10 || to <= from)
                return cues;

            double[] e = new double[T];
            for (int t = 0; t < T; t++)
            {
                double[] d = result.Motion[t];
                if (d == null || d.Length == 0)
                    continue;
                int p = Math.Max(0, Math.Min(d.Length - 1, result.Path[t]));
                double lw = LaneWidth(result.Lanes[t], p);
                int r = (int)Math.Round(1.2 * lw);
                double sum = 0;
                int count = 0;
                for (int x = Math.Max(0, p - r); x <= Math.Min(d.Length - 1, p + r); x++)
                {
                    sum += d[x];
                    count++;
                }

                e[t] = count > 0 ? sum / count : 0;
            }

            int half = Math.Max(1, (int)Math.Round(0.2 * fps));
            double[] es = new double[T];
            for (int t = 0; t < T; t++)
            {
                double sum = 0;
                int count = 0;
                for (int k = Math.Max(0, t - half); k <= Math.Min(T - 1, t + half); k++)
                {
                    sum += e[k];
                    count++;
                }

                es[t] = sum / count;
            }

            // Usual level of the swimming: after the 15 m (else the end of the reading).
            int refFrom = to, refTo = Math.Min(T - 1, to + (int)Math.Round(4 * fps));
            if (refTo - refFrom < (int)Math.Round(1.5 * fps))
            {
                refFrom = Math.Max(from, T - 1 - (int)Math.Round(4 * fps));
                refTo = T - 1;
            }

            List<double> level = new List<double>();
            for (int t = refFrom; t <= refTo; t++)
                level.Add(es[t]);
            if (level.Count < 5)
                return cues;
            double usual = SwimLearningMedian(level);
            if (usual <= 1e-6)
                return cues;

            // The quietest moment of the underwater phase (a reading that starts in the swimming has none), then the rise to the
            // level of the swimming: half way from the quiet level, staying above a third of the way for a second.
            int tMin = from;
            for (int t = from; t <= to; t++)
            {
                if (es[t] < es[tMin])
                    tMin = t;
            }

            double floor = es[tMin];
            if (floor > 0.7 * usual)
                return cues;

            double rise = floor + 0.5 * (usual - floor), keep = floor + 0.3 * (usual - floor);
            int hold = (int)Math.Round(1.0 * fps);
            for (int t = Math.Max(1, tMin); t <= to; t++)
            {
                if (es[t] < rise)
                    continue;

                bool stays = true;
                for (int k = t; k < Math.Min(T, t + hold) && stays; k++)
                    stays = es[k] >= keep;
                if (!stays)
                    continue;

                double a = es[t - 1], b = es[t];
                double fraction = b - a > 1e-9 ? (rise - a) / (b - a) : 1.0;
                cues[CueEnergy] = t - 1 + Math.Max(0, Math.Min(1, fraction));
                break;
            }

            // The steepest rise of the motion in the window (a rise of at least a tenth of the usual level), measured inside the
            // window only (2026-10-18: at its very first frame the rise took the motion of the turn itself before the window, 1.5 s
            // before the head was out; after the quietest moment instead lost a medley breakout).
            int h = Math.Max(1, (int)Math.Round(0.3 * fps));
            double steepest = 0.1 * usual;
            int tRise = -1;
            for (int t = Math.Max(h, from + (RiseInsideWindow ? h : 0)); t <= Math.Min(to, T - 1 - h); t++)
            {
                double up = es[t + h] - es[t - h];
                if (up > steepest)
                {
                    steepest = up;
                    tRise = t;
                }
            }

            if (tRise >= 0)
                cues[CueRise] = tRise;

            // The splash: the first peak of the stroke signal after the quiet moment as strong as the strokes of the swimming.
            double[] o = result.Onset;
            if (o != null && o.Length >= T)
            {
                List<double> peaks = new List<double>();
                for (int t = Math.Max(1, refFrom); t < Math.Min(refTo, o.Length - 1); t++)
                {
                    if (o[t] > o[t - 1] && o[t] >= o[t + 1] && o[t] > 0)
                        peaks.Add(o[t]);
                }

                if (peaks.Count >= 3)
                {
                    double strong = 0.6 * SwimLearningMedian(peaks);
                    for (int t = Math.Max(1, tMin); t <= Math.Min(to, o.Length - 2); t++)
                    {
                        if (o[t] > o[t - 1] && o[t] >= o[t + 1] && o[t] >= strong)
                        {
                            cues[CueBurst] = t;
                            break;
                        }
                    }
                }
            }

            return cues;
        }

        public const string CueStop = "stop";
        public const string CueWallHead = "wallhead";
        public const string CueWallBody = "wallbody";

        /// <summary>
        /// The wall at the end of the length followed (turn or finish): the cues of its frame (index of the frames read, fractions
        /// allowed). The wall is where the red last 5 m of the ropes end; the head (front of the moving part) and the body
        /// (middle of it) reach it. Without the end of the ropes in the image: the swimmer slowing down at the wall (stop).
        /// </summary>
        public static Dictionary<string, double> WallCues(SwimStrokeResult result, int direction, double fps, double after)
        {
            return WallCues(result, direction, fps, after, double.NaN, 0);
        }

        /// <summary>
        /// The same, knowing the start of the lap (frame index, can be negative) and the pool: the wall comes after the time of
        /// the fastest swimmer to it, and not before 'shortest' seconds from the start of the lap (see ShortestLength; 0: none).
        /// </summary>
        public static Dictionary<string, double> WallCues(SwimStrokeResult result, int direction, double fps, double after, double lapStart, int pool, double shortest = 0)
        {
            if (!double.IsNaN(lapStart) && pool > 0)
            {
                double earliest = lapStart + Math.Max(pool * FastestPace, LengthCheck ? shortest : 0) * fps;
                if (double.IsNaN(after) || after < earliest)
                    after = earliest;
            }

            Dictionary<string, double> cues = new Dictionary<string, double>();
            if (result == null || result.Path == null || fps <= 0)
                return cues;

            int T = Math.Min(result.Path.Length, Math.Min(result.Lanes.Count, Math.Min(result.Motion.Count, Math.Min(result.RedFar.Count, result.RedNear.Count))));
            if (T < 10)
                return cues;

            double[] head = Heads(result, T, direction, fps);
            double[] body = new double[T];
            for (int t = 0; t < T; t++)
                body[t] = result.Path[t];

            int start = double.IsNaN(after) ? (int)(0.5 * T) : Math.Max(1, (int)Math.Ceiling(after));
            double wallHead = WallCrossing(result, head, T, direction, start);
            double wallBody = WallCrossing(result, body, T, direction, start);
            double stop = Wall(result, direction, fps, after);
            if (!double.IsNaN(stop))
                cues[CueStop] = stop;

            // The end of the ropes is the wall only near the moment the swimmer stops there (else a red part beyond the wall).
            if (!double.IsNaN(wallHead) && (double.IsNaN(stop) || Math.Abs(wallHead - stop) <= 1.0 * fps))
                cues[CueWallHead] = wallHead;
            if (!double.IsNaN(wallBody) && (double.IsNaN(stop) || Math.Abs(wallBody - stop) <= 1.0 * fps))
                cues[CueWallBody] = wallBody;
            return cues;
        }

        /// <summary>
        /// First frame (from start) where the position x passes the end of the red last 5 m of a rope (the far one, else the near
        /// one) in the direction of the swimmer: the end of a long red part towards where he swims.
        /// </summary>
        private static double WallCrossing(SwimStrokeResult result, double[] x, int T, int direction, int start)
        {
            for (int t = Math.Max(1, start); t < T; t++)
            {
                double e0 = WallEnd(result, t - 1, direction), e1 = WallEnd(result, t, direction);
                if (double.IsNaN(e0) || double.IsNaN(e1) || double.IsNaN(x[t - 1]) || double.IsNaN(x[t]))
                    continue;

                // The end must be the same in the two frames (less than half a lane width apart after the camera moved).
                double lw = LaneWidth(result.Lanes[t], e1);
                double[] shift = t < result.Shifts.Count ? result.Shifts[t] : null;
                double moved = e0 + (shift != null && shift.Length > 0 ? shift[Math.Max(0, Math.Min(shift.Length - 1, (int)Math.Round(e0)))] : 0);
                if (Math.Abs(moved - e1) > Math.Max(6.0, 0.5 * lw))
                    continue;

                double s0 = direction * (x[t - 1] - e0), s1 = direction * (x[t] - e1);
                if (s0 < 0 && s1 >= 0)
                    return t - 1 + (-s0) / Math.Max(1e-9, s1 - s0);
            }

            return double.NaN;
        }

        /// <summary>End of the red last 5 m in a frame (working x, NaN if not seen): far rope first, else near rope.</summary>
        private static double WallEnd(SwimStrokeResult result, int t, int direction)
        {
            foreach (List<int[]> red in new[] { result.RedFar, result.RedNear })
            {
                int[] runs = Joined(red[t], result.Lanes[t]);
                double best = double.NaN;
                for (int i = 0; i + 1 < runs.Length; i += 2)
                {
                    double middle = 0.5 * (runs[i] + runs[i + 1]);
                    if (runs[i + 1] - runs[i] + 1 <= 3.2 * LaneWidth(result.Lanes[t], middle))
                        continue;

                    double end = direction > 0 ? runs[i + 1] : runs[i];
                    if (double.IsNaN(best) || direction * (end - best) > 0)
                        best = end;
                }

                // A red part that reaches the border of the image: its end is not the wall.
                int width = result.Motion[t] != null ? result.Motion[t].Length : 0;
                if (!double.IsNaN(best) && (best <= 1 || (width > 0 && best >= width - 2)))
                    best = double.NaN;
                if (!double.IsNaN(best))
                    return best;
            }

            return double.NaN;
        }

        /// <summary>The wall of a length not before ShortestLength of the length before it (tests).</summary>
        internal static bool LengthCheck = true;

        /// <summary>
        /// Shortest time a length can take (s) from the time of the length before it, so that a wall seen much earlier (the path
        /// left the swimmer and stopped with the water) is not taken: after the length of the start, its time (the dive is worth
        /// more than a push off: the analysed races 1.05–1.20 times it); after a turn in the same style 0.9 of it (0.98–1.03);
        /// in another style (medley, the leg of a relay) 0.75 (freestyle after breaststroke 0.83). previousSeconds 0: no limit.
        /// </summary>
        public static double ShortestLength(double previousSeconds, bool previousFromStart, bool sameStyle)
        {
            if (previousSeconds <= 0)
                return 0;
            if (!sameStyle)
                return 0.75 * previousSeconds;
            return (previousFromStart ? 1.0 : 0.9) * previousSeconds;
        }

        /// <summary>The place at the wall from the end of the ropes when the path is far from it (tests).</summary>
        internal static bool WallPlace = true;

        /// <summary>
        /// Place of the swimmer's body (working x) at the wall of a length (frame index 'wall'), where the next length starts: the
        /// path there, unless it is more than a body away from the wall itself (the end of the red last 5 m of the ropes, seen
        /// within 0.5 s of that frame): then half a body before the wall (the path left the swimmer for the water behind him, the
        /// camera turning towards the wall). bodyPerLane: the length of the swimmer in lane widths.
        /// </summary>
        public static double PlaceAtWall(SwimStrokeResult result, double wall, int direction, double fps, double bodyPerLane)
        {
            int n = result != null && result.Path != null ? result.Path.Length : 0;
            if (n == 0)
                return double.NaN;

            int wi = (int)Math.Round(Math.Max(0, Math.Min(n - 1, wall)));
            double x = result.Path[wi];
            if (!WallPlace || double.IsNaN(bodyPerLane) || bodyPerLane <= 0)
                return x;

            int T = Math.Min(n, Math.Min(result.Lanes.Count, Math.Min(result.RedFar.Count, result.RedNear.Count)));
            int reach = (int)Math.Round(0.5 * fps);
            for (int k = 0; k <= reach; k++)
            {
                foreach (int t in new[] { wi - k, wi + k })
                {
                    if (t < 0 || t >= T || (k == 0 && t != wi))
                        continue;

                    double end = WallEnd(result, t, direction);
                    if (double.IsNaN(end))
                        continue;

                    // The end moved with the camera to the frame of the wall.
                    end = MoveWithCamera(result, end, t, wi);

                    double body = bodyPerLane * LaneWidth(result.Lanes[wi], end);
                    if (Math.Abs(x - end) <= body)
                        return x;
                    return end - direction * 0.5 * body;
                }
            }

            return x;
        }

        /// <summary>
        /// Usual speed of the swimmer in a length read: the median of the middle 60% of its frames (lane widths a frame towards
        /// where he swims, camera motion removed), NaN when not known.
        /// </summary>
        public static double UsualSpeed(SwimStrokeResult result, int direction)
        {
            if (result == null || result.Path == null)
                return double.NaN;

            int T = Math.Min(result.Path.Length, Math.Min(result.Lanes.Count, result.Shifts.Count));
            if (T < 20)
                return double.NaN;

            double[] u = Speeds(result, T, direction);
            List<double> middle = new List<double>();
            for (int t = Math.Max(1, (int)(0.2 * T)); t < (int)(0.8 * T); t++)
                middle.Add(u[t]);
            if (middle.Count < 5)
                return double.NaN;

            double usual = SwimLearningMedian(middle);
            return usual > 1e-6 ? usual : double.NaN;
        }

        /// <summary>
        /// Usual time (s) from the wall of a turn to the breakout (the head out) by the style of the length after it: the median
        /// of the analysed races (freestyle 2.45, backstroke 4.98, breaststroke 4.92, butterfly 4.70).
        /// </summary>
        public static double TurnUnderwater(string style)
        {
            switch (style)
            {
                case "bk": return 4.98;
                case "br": return 4.92;
                case "fly": return 4.70;
                default: return 2.45;
            }
        }

        /// <summary>
        /// The place x (working x) at frame 'from' moved with the camera to frame 'to' (indices of the frames read, either order).
        /// </summary>
        public static double MoveWithCamera(SwimStrokeResult result, double x, int from, int to)
        {
            if (result == null || result.Shifts == null)
                return x;
            for (int u = from; u < to && u + 1 < result.Shifts.Count; u++)
                x += ShiftAt(result.Shifts[u + 1], x);
            for (int u = from; u > to && u < result.Shifts.Count; u--)
                x -= ShiftAt(result.Shifts[u], x);
            return x;
        }

        private static double ShiftAt(double[] shift, double x)
        {
            if (shift == null || shift.Length == 0)
                return 0;
            return shift[Math.Max(0, Math.Min(shift.Length - 1, (int)Math.Round(x)))];
        }

        /// <summary>
        /// The wall at the end of the length followed (turn or finish), frame index (NaN if not found): the swimmer, who swam
        /// at his usual speed along the lane (camera motion removed, in lane widths so that the zoom does not count), slows
        /// below a quarter of it after the last checkpoint of the length (after: frame of that checkpoint, or NaN).
        /// </summary>
        public static double Wall(SwimStrokeResult result, int direction, double fps, double after)
        {
            if (result == null || result.Path == null || fps <= 0)
                return double.NaN;

            int T = Math.Min(result.Path.Length, Math.Min(result.Lanes.Count, result.Shifts.Count));
            if (T < (int)(2 * fps))
                return double.NaN;

            double[] u = Speeds(result, T, direction);
            int half = Math.Max(1, (int)Math.Round(0.15 * fps));
            double[] smooth = new double[T];
            for (int t = 0; t < T; t++)
            {
                double sum = 0;
                int count = 0;
                for (int k = Math.Max(1, t - half); k <= Math.Min(T - 1, t + half); k++)
                {
                    sum += u[k];
                    count++;
                }

                smooth[t] = count > 0 ? sum / count : 0;
            }

            // Usual speed: the middle of the length followed.
            int from = (int)(0.2 * T), to = (int)(0.8 * T);
            List<double> middle = new List<double>();
            for (int t = Math.Max(1, from); t < to; t++)
                middle.Add(u[t]);
            if (middle.Count < 5)
                return double.NaN;

            double usual = SwimLearningMedian(middle);
            if (usual <= 1e-6)
                return double.NaN;

            int start = double.IsNaN(after) ? (int)(0.6 * T) : Math.Max(1, (int)Math.Ceiling(after));
            int hold = Math.Max(2, (int)Math.Round(0.3 * fps));
            if (Trace != null)
            {
                System.Text.StringBuilder b = new System.Text.StringBuilder();
                for (int t = Math.Max(1, start - 50); t < T; t += 5)
                    b.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1:0.00} ", t, smooth[t] / usual));
                Trace("speed/usual after " + start + ": " + b);
            }
            for (int t = start; t < T; t++)
            {
                if (smooth[t] >= 0.25 * usual)
                    continue;

                bool stays = true;
                for (int k = t; k < Math.Min(T, t + hold) && stays; k++)
                    stays = smooth[k] < 0.4 * usual;
                if (!stays)
                    continue;

                // The moment it went below: between the frame before (above) and this one.
                double a = smooth[t - 1], b = smooth[t];
                double fraction = a - b > 1e-9 ? (a - 0.25 * usual) / (a - b) : 1.0;
                return t - 1 + Math.Max(0, Math.Min(1, fraction));
            }

            return double.NaN;
        }

        /// <summary>Speed of the swimmer along the lane in each frame (lane widths per frame, towards where he swims).</summary>
        private static double[] Speeds(SwimStrokeResult result, int T, int direction)
        {
            double[] u = new double[T];
            for (int t = 1; t < T; t++)
            {
                int p0 = result.Path[t - 1], p1 = result.Path[t];
                double[] shift = result.Shifts[t];
                double moved = shift != null && shift.Length > 0 ? shift[Math.Max(0, Math.Min(shift.Length - 1, p0))] : 0;
                u[t] = direction * (p1 - p0 - moved) / LaneWidth(result.Lanes[t], p1);
            }

            return u;
        }

        /// <summary>Main frame of a checkpoint found (its cues): see Time.</summary>
        public static double MainFrame(Dictionary<string, double> cues)
        {
            return cues == null || cues.Count == 0 ? double.NaN : Time(cues);
        }

        /// <summary>Main time of a checkpoint: the line between the marks of the two ropes if it has it, else the far rope, else the near one.</summary>
        private static double Time(Dictionary<string, double> cues)
        {
            double t;
            if (cues.TryGetValue(CueMiddle, out t))
                return t;
            if (cues.TryGetValue(CueFar, out t))
                return t;
            if (cues.TryGetValue(CueNear, out t))
                return t;
            return cues.Values.First();
        }

        /// <summary>Front of the moving part of the swimmer in each frame (working x, NaN if not seen), median over ±0.12 s.</summary>
        private static double[] Heads(SwimStrokeResult result, int T, int direction, double fps)
        {
            double[] raw = new double[T];
            for (int t = 0; t < T; t++)
            {
                raw[t] = double.NaN;
                double[] d = result.Motion[t];
                if (d == null || d.Length == 0)
                    continue;

                int W = d.Length;
                double[] ds = SwimStrokeDetector.Gauss1D(d, 3.0);
                int p = Math.Max(0, Math.Min(W - 1, result.Path[t]));
                double lw = LaneWidth(result.Lanes[t], p);
                int r = (int)Math.Round(0.8 * lw);
                double max = 0;
                for (int x = Math.Max(0, p - r); x <= Math.Min(W - 1, p + r); x++)
                    max = Math.Max(max, ds[x]);
                if (max <= 1e-6)
                    continue;

                double threshold = 0.35 * max;
                int last = p, gap = 0;
                int reach = (int)Math.Round(1.6 * lw);
                for (int k = 1; k <= reach; k++)
                {
                    int x = p + direction * k;
                    if (x < 0 || x >= W)
                        break;
                    if (ds[x] >= threshold)
                    {
                        last = x;
                        gap = 0;
                    }
                    else if (++gap > 3)
                    {
                        break;
                    }
                }

                raw[t] = last;
            }

            int half = Math.Max(1, (int)Math.Round(0.12 * fps));
            double[] head = new double[T];
            for (int t = 0; t < T; t++)
            {
                List<double> v = new List<double>();
                for (int k = Math.Max(0, t - half); k <= Math.Min(T - 1, t + half); k++)
                {
                    if (!double.IsNaN(raw[k]))
                        v.Add(raw[k]);
                }

                head[t] = v.Count == 0 ? double.NaN : SwimLearningMedian(v);
            }

            return head;
        }

        private static double SwimLearningMedian(List<double> values)
        {
            if (values == null || values.Count == 0)
                return double.NaN;

            values.Sort();
            int n = values.Count;
            return n % 2 == 1 ? values[n / 2] : 0.5 * (values[n / 2 - 1] + values[n / 2]);
        }

        private static double LaneWidth(SwimStrokeLane lane, double x)
        {
            return Math.Max(5.0, Math.Abs(lane.Y2(x) - lane.Y1(x)));
        }

        /// <summary>A mark of a rope followed over the frames (the camera moves it; the swimmer and the splashes hide it).</summary>
        private sealed class MarkTrack
        {
            public bool Long;
            public double X;
            public int First;
            public int LastSeen;
            public int Seen;
            public bool Done;

            /// <summary>Place in each frame from First (seen or moved with the camera).</summary>
            public readonly List<double> Places = new List<double>();

            public int Last
            {
                get { return First + Places.Count - 1; }
            }

            public double At(int t)
            {
                return Places[Math.Max(0, Math.Min(Places.Count - 1, t - First))];
            }
        }

        /// <summary>A checkpoint passed: a mark (or the start of the red last 5 m) and the frames of its cues.</summary>
        private sealed class Checkpoint
        {
            public bool Long;
            public readonly Dictionary<string, double> Cues = new Dictionary<string, double>();
        }

        /// <summary>
        /// The checkpoints passed by the head: the marks of the two ropes are followed over the frames (Track); the mark of the
        /// far rope and the one of the near rope of the same checkpoint are paired (the closest in the image when both are
        /// followed); the head crosses each of them and the line between them (its middle: the middle of the lane, cue "mid").
        /// The perspective puts the two marks of a checkpoint far apart near the ends of the pool (more than a second of
        /// swimming): the line between them is what the analyst sees.
        /// </summary>
        private static List<Checkpoint> Checkpoints(SwimStrokeResult result, double[] head, int T, int direction, double fps, double earliestEnd)
        {
            List<MarkTrack> far = Track(result, result.RedFar, true, head, T, direction, fps).Where(m => m.Seen >= 3).ToList();
            List<MarkTrack> near = Track(result, result.RedNear, false, head, T, direction, fps).Where(m => m.Seen >= 3).ToList();
            if (Trace != null)
            {
                foreach (MarkTrack m in far.Concat(near))
                    Trace(string.Format(System.Globalization.CultureInfo.InvariantCulture, "track {0} {1} frames {2}-{3} seen {4} last seen {5} x {6:0}->{7:0}",
                        far.Contains(m) ? "far" : "near", m.Long ? "long" : "mark", m.First, m.Last, m.Seen, m.LastSeen, m.Places[0], m.Places[m.Places.Count - 1]));
            }

            // Pairs: the far and the near mark of the same checkpoint. The head crosses them less than 2 s apart (two checkpoints
            // are 10 m apart: more than 4 s); a mark the head did not cross (hidden too long) pairs with the closest mark of the
            // other rope followed at the crossing of that one (less than 6 lane widths: two checkpoints are most of the image apart).
            Dictionary<MarkTrack, double> crossing = new Dictionary<MarkTrack, double>();
            foreach (MarkTrack m in far.Concat(near))
                crossing[m] = CrossingOf(m.Places, m.First, head, T, direction, fps);

            // The start of the red last 5 m is crossed once on each rope: the first time (the swimmer hides the rest of the red
            // part as he goes, its edge moves with him); before the time of the fastest swimmer it is the red of the wall left.
            foreach (List<MarkTrack> rope in new[] { far, near })
            {
                MarkTrack first = rope.Where(m => m.Long && !double.IsNaN(crossing[m]) && crossing[m] >= earliestEnd).OrderBy(m => crossing[m]).FirstOrDefault();
                foreach (MarkTrack m in rope.Where(m => m.Long && m != first))
                    crossing[m] = double.NaN;
            }

            List<Tuple<MarkTrack, MarkTrack, double>> candidates = new List<Tuple<MarkTrack, MarkTrack, double>>();
            foreach (MarkTrack f in far)
            {
                foreach (MarkTrack n in near)
                {
                    if (f.Long != n.Long)
                        continue;

                    double cf = crossing[f], cn = crossing[n];
                    if (!double.IsNaN(cf) && !double.IsNaN(cn))
                    {
                        if (Math.Abs(cf - cn) <= 2.0 * fps)
                            candidates.Add(Tuple.Create(f, n, Math.Abs(cf - cn) / fps));
                        continue;
                    }

                    if (double.IsNaN(cf) && double.IsNaN(cn))
                        continue;

                    // One crossed: the other one followed then, close to it in the image.
                    int t = (int)Math.Round(double.IsNaN(cf) ? cn : cf);
                    MarkTrack other = double.IsNaN(cf) ? f : n;
                    if (t < other.First || t > other.Last)
                        continue;
                    double lw = LaneWidth(result.Lanes[Math.Max(0, Math.Min(T - 1, t))], n.At(t));
                    double dx = Math.Abs(f.At(t) - n.At(t));
                    if (dx <= 6 * lw)
                        candidates.Add(Tuple.Create(f, n, 2.0 + dx / lw));
                }
            }

            List<Checkpoint> points = new List<Checkpoint>();
            HashSet<MarkTrack> paired = new HashSet<MarkTrack>();
            foreach (Tuple<MarkTrack, MarkTrack, double> c in candidates.OrderBy(c => c.Item3))
            {
                MarkTrack f = c.Item1, n = c.Item2;
                if (paired.Contains(f) || paired.Contains(n))
                    continue;
                paired.Add(f);
                paired.Add(n);

                // The line between them, at a quarter, half and three quarters of the lane from the near rope, in each frame where
                // one of the two is followed (the other one at the distance it had in the closest frame where both were).
                int from = Math.Max(f.First, n.First), to = Math.Min(f.Last, n.Last);
                int first = Math.Min(f.First, n.First), last = Math.Max(f.Last, n.Last);
                Checkpoint p = new Checkpoint { Long = f.Long };
                double x;
                if (!double.IsNaN(x = crossing[f]))
                    p.Cues[CueFar] = x;
                if (!double.IsNaN(x = crossing[n]))
                    p.Cues[CueNear] = x;
                foreach (KeyValuePair<string, double> line in new[] {
                    new KeyValuePair<string, double>(CueQuarterNear, 0.25), new KeyValuePair<string, double>(CueMiddle, 0.5), new KeyValuePair<string, double>(CueQuarterFar, 0.75) })
                {
                    List<double> places = new List<double>();
                    for (int t = first; t <= last; t++)
                    {
                        bool hasF = t >= f.First && t <= f.Last, hasN = t >= n.First && t <= n.Last;
                        int r = Math.Max(from, Math.Min(to, t));
                        double dx = f.At(r) - n.At(r);
                        double xn = hasN ? n.At(t) : f.At(t) - dx;
                        double xf = hasF ? f.At(t) : n.At(t) + dx;
                        places.Add(xn + line.Value * (xf - xn));
                    }

                    if (!double.IsNaN(x = CrossingOf(places, first, head, T, direction, fps)))
                        p.Cues[line.Key] = x;
                }

                DropOddCrossings(p.Cues, fps);
                if (p.Cues.Count > 0)
                    points.Add(p);
            }

            foreach (MarkTrack m in far.Concat(near))
            {
                if (paired.Contains(m))
                    continue;
                double x = crossing[m];
                if (double.IsNaN(x))
                    continue;
                Checkpoint p = new Checkpoint { Long = m.Long };
                p.Cues[far.Contains(m) ? CueFar : CueNear] = x;
                points.Add(p);
            }

            // Pieces of the same mark (followed twice: hidden, then seen again): one checkpoint, the one with the most cues.
            List<Checkpoint> merged = new List<Checkpoint>();
            foreach (Checkpoint p in points.OrderByDescending(p => p.Cues.Count))
            {
                Checkpoint same = merged.FirstOrDefault(m => m.Long == p.Long && Math.Abs(Time(m.Cues) - Time(p.Cues)) <= 0.6 * fps);
                if (same == null)
                {
                    merged.Add(p);
                    continue;
                }

                foreach (KeyValuePair<string, double> c in p.Cues)
                {
                    if (!same.Cues.ContainsKey(c.Key))
                        same.Cues[c.Key] = c.Value;
                }
            }

            return merged;
        }

        /// <summary>
        /// The crossings of one checkpoint are of lines close together, crossed within a second: one more than OddCrossing s
        /// from the median of the others (three or more) is the head of another moment (the path ahead of the swimmer on the
        /// glints of the water) and goes: its main time (the middle line) also names the marks and gives the pace. Test races
        /// (40 camcorder, 29 phone): a 200 IM last length 1 -> 4 checkpoints (165 m -2.78 -> -0.49 s, 175-195 within 0.14 s;
        /// its breakout 2.5 s late no longer proposed), a 50 m wall +0.92 -> +0.22 s, the other races unchanged.
        /// </summary>
        private static void DropOddCrossings(Dictionary<string, double> cues, double fps)
        {
            if (OddCrossing <= 0 || cues.Count < 3)
                return;

            double median = SwimLearningMedian(cues.Values.ToList());
            foreach (string k in cues.Where(c => Math.Abs(c.Value - median) > OddCrossing * fps).Select(c => c.Key).ToList())
                cues.Remove(k);
        }

        /// <summary>See DropOddCrossings (s; 0: none go; tests).</summary>
        internal static double OddCrossing = 1.0;

        /// <summary>
        /// Frame where the head crosses a point followed from frame 'first' (NaN if it does not): the head minus the point,
        /// fitted by a straight line over half a second on each side of the first crossing (the head goes back and forth over
        /// the mark with the arms and the splashes), where the line is zero. Not on the first frames of the point (the head was
        /// already over it when it was first seen).
        /// </summary>
        private static double CrossingOf(List<double> places, int first, double[] head, int T, int direction, double fps)
        {
            int settle = Math.Max(1, (int)Math.Round(0.3 * fps));
            int t0 = -1;
            for (int k = 2; k < places.Count && t0 < 0; k++)
            {
                int t = first + k;
                if (t < settle || t >= T || double.IsNaN(head[t]) || double.IsNaN(head[t - 1]))
                    continue;

                double s0 = direction * (head[t - 1] - places[k - 1]);
                double s1 = direction * (head[t] - places[k]);
                if (s0 < 0 && s1 >= 0)
                    t0 = t;
            }

            if (t0 < 0)
                return double.NaN;

            // Least squares of s(t) = a + b (t - t0) on the frames of the window where both are known.
            int w = Math.Max(2, (int)Math.Round(0.5 * fps));
            double n = 0, st = 0, ss = 0, stt = 0, sts = 0;
            for (int t = Math.Max(first, t0 - w); t <= Math.Min(Math.Min(T - 1, first + places.Count - 1), t0 + w); t++)
            {
                if (double.IsNaN(head[t]))
                    continue;
                double u = t - t0, v = direction * (head[t] - places[t - first]);
                n++;
                st += u;
                ss += v;
                stt += u * u;
                sts += u * v;
            }

            double fallback;
            {
                double s0 = direction * (head[t0 - 1] - places[t0 - 1 - first]);
                double s1 = direction * (head[t0] - places[t0 - first]);
                fallback = t0 - 1 + (-s0) / Math.Max(1e-9, s1 - s0);
            }

            double det = n * stt - st * st;
            if (n < 5 || Math.Abs(det) < 1e-9)
                return fallback;

            double b = (n * sts - st * ss) / det;
            double a = (ss - b * st) / n;
            if (b <= 1e-6)
                return fallback;

            double crossing = t0 - a / b;
            return Math.Abs(crossing - t0) <= w ? crossing : fallback;
        }

        /// <summary>
        /// The marks of a rope followed over the frames read: each red part (its point: middle of a mark, edge of the last 5 m
        /// towards the swimmer) continues the mark it falls on after the camera motion (the point on the rope moved by the
        /// homography of the frame), else starts a new one. A mark is not looked at within 1.5 lane widths of the head; a mark not
        /// seen for 1.5 s, or out of the image, ends. A mark found again after the swimmer passed it continues the one it was.
        /// </summary>
        private static List<MarkTrack> Track(SwimStrokeResult result, List<int[]> red, bool far, double[] head, int T, int direction, double fps)
        {
            List<MarkTrack> all = new List<MarkTrack>();
            List<MarkTrack> active = new List<MarkTrack>();
            int maxGap = (int)Math.Round(1.5 * fps);
            for (int t = 0; t < T; t++)
            {
                SwimStrokeLane lane = result.Lanes[t];
                double[] shift = t > 0 && t < result.Shifts.Count ? result.Shifts[t] : null;
                double[] motion = t > 0 && t < result.Motions.Count ? result.Motions[t] : null;
                int width = result.Motion[t] != null && result.Motion[t].Length > 0 ? result.Motion[t].Length : (shift != null ? shift.Length : 0);

                // The camera moved: the marks with it (on their rope of the frame before).
                foreach (MarkTrack m in active)
                {
                    if (motion != null)
                    {
                        SwimStrokeLane before = result.Lanes[t - 1];
                        double y = far ? before.Y1(m.X) : before.Y2(m.X), X, Y;
                        SwimStrokeDetector.MapPoint(motion, m.X, y, out X, out Y);
                        if (!double.IsNaN(X) && !double.IsInfinity(X))
                            m.X = X;
                    }
                    else if (shift != null && shift.Length > 0)
                    {
                        m.X += shift[Math.Max(0, Math.Min(shift.Length - 1, (int)Math.Round(m.X)))];
                    }
                }

                // The red parts of this frame.
                int[] runs = Joined(red[t], lane);
                bool[] used = new bool[active.Count];
                for (int i = 0; i + 1 < runs.Length; i += 2)
                {
                    bool isLong;
                    double x = Point(lane, runs[i], runs[i + 1], direction, out isLong);

                    // The edge of a red part cut by the border of the image is not the end of the red 5 m.
                    if (isLong && width > 0 && (x <= 2 || x >= width - 3))
                        continue;

                    double lw = LaneWidth(lane, x);
                    bool nearHead = !double.IsNaN(head[t]) && Math.Abs(head[t] - x) < 1.5 * lw;
                    double tolerance = Math.Max(6.0, 0.5 * lw);
                    int best = -1;
                    double e = double.MaxValue;
                    for (int k = 0; k < active.Count; k++)
                    {
                        if (used[k] || active[k].Long != isLong)
                            continue;

                        // A mark hidden for a while drifts a little more with the camera.
                        double d = Math.Abs(active[k].X - x);
                        double allowed = t - active[k].LastSeen > 0.3 * fps ? Math.Max(tolerance, 1.0 * lw) : tolerance;
                        if (d <= allowed && d < e)
                        {
                            e = d;
                            best = k;
                        }
                    }

                    if (best >= 0)
                    {
                        used[best] = true;
                        MarkTrack m = active[best];

                        // Near the head the red part can be a piece of the mark (the rest hidden): the place comes from the camera.
                        if (!nearHead)
                        {
                            m.X += (t - m.LastSeen > 0.3 * fps ? 1.0 : 0.6) * (x - m.X);
                            m.LastSeen = t;
                            m.Seen++;
                        }

                        continue;
                    }

                    if (nearHead)
                        continue;

                    MarkTrack track = new MarkTrack { Long = isLong, X = x, First = t, LastSeen = t, Seen = 1 };
                    active.Add(track);
                    all.Add(track);
                    Array.Resize(ref used, active.Count);
                    used[active.Count - 1] = true;
                }

                foreach (MarkTrack m in active)
                {
                    if (t - m.LastSeen > maxGap || (width > 0 && (m.X < -width * 0.1 || m.X > width * 1.1)))
                        m.Done = true;
                    else
                        m.Places.Add(m.X);
                }

                active.RemoveAll(m => m.Done);
            }

            return all;
        }

        /// <summary>
        /// Red parts of a rope with the gaps up to 1.5 lane widths closed: the last 5 m are red floats one after the other,
        /// cut where the swimmer or a splash hides them (the marks are 10 m apart).
        /// </summary>
        private static int[] Joined(int[] runs, SwimStrokeLane lane)
        {
            if (runs == null || runs.Length < 2)
                return new int[0];

            List<int> joined = new List<int> { runs[0], runs[1] };
            for (int i = 2; i + 1 < runs.Length; i += 2)
            {
                int last = joined[joined.Count - 1];
                if (runs[i] - last <= 1.5 * LaneWidth(lane, 0.5 * (last + runs[i])))
                    joined[joined.Count - 1] = runs[i + 1];
                else
                {
                    joined.Add(runs[i]);
                    joined.Add(runs[i + 1]);
                }
            }

            return joined.ToArray();
        }

        /// <summary>
        /// Point of a red part that the head crosses: the middle of a mark, or for a long part (the last 5 m) its edge towards
        /// the swimmer. Long: more than 3.2 lane widths.
        /// </summary>
        private static double Point(SwimStrokeLane lane, int from, int to, int direction, out bool isLong)
        {
            double middle = 0.5 * (from + to);
            double lw = LaneWidth(lane, middle);
            isLong = to - from + 1 > 3.2 * lw;
            if (!isLong)
                return middle;
            return direction > 0 ? from : to;
        }

        /// <summary>
        /// Distance of each mark passed (0: none): in order, the first ones that fit the times from the start of the lap
        /// (a mark 10 m further takes between 0.5 and 1.6 times the time of the previous 10 m) and, when 'endTime' is a number
        /// (frames), the time at 'endDistance' (the start of the red last 5 m) after them.
        /// </summary>
        private static int[] Assign(List<double> times, double lapStart, double fps, int[] distances, double endTime, int endDistance)
        {
            int n = times.Count;
            int[] result = new int[n];
            if (n == 0)
                return result;

            // Every increasing choice of distances for the marks (a mark can be skipped: a reflection or another lane),
            // scored by the regularity of the speed between them and from the start of the lap.
            int m = distances.Length;
            double bestScore = double.MaxValue;
            int[] best = null;
            int[] choice = new int[n];
            Search(0, -1, 0);
            if (best != null)
            {
                for (int i = 0; i < n; i++)
                    result[i] = best[i] >= 0 ? distances[best[i]] : 0;
            }

            return result;

            // Local recursive search (n and m are small).
            void Search(int i, int lastIndex, int count)
            {
                if (i == n)
                {
                    if (count == 0)
                        return;
                    double score = Score(choice, count);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = (int[])choice.Clone();
                    }

                    return;
                }

                choice[i] = -1;
                Search(i + 1, lastIndex, count);
                for (int k = lastIndex + 1; k < m; k++)
                {
                    choice[i] = k;
                    Search(i + 1, k, count + 1);
                }

                choice[i] = -1;
            }

            double Score(int[] c, int count)
            {
                // Pace (s per m) between consecutive points from the start of the lap; a lap starts faster (dive or push).
                List<double> d = new List<double> { 0 };
                List<double> t = new List<double> { lapStart / fps };
                for (int i = 0; i < n; i++)
                {
                    if (c[i] < 0)
                        continue;
                    d.Add(distances[c[i]]);
                    t.Add(times[i] / fps);
                }

                if (!double.IsNaN(endTime))
                {
                    d.Add(endDistance);
                    t.Add(endTime / fps);
                }

                List<double> pace = new List<double>();
                for (int k = 1; k < d.Count; k++)
                {
                    if (t[k] <= t[k - 1])
                        return double.MaxValue;
                    double p = (t[k] - t[k - 1]) / (d[k] - d[k - 1]);
                    if (k == 1)
                    {
                        // No swimmer is at the first mark faster than the fastest start or turn (a mark of the red of the
                        // wall left behind, a reflection).
                        if (p < FastestPace)
                            return double.MaxValue;
                        p *= 1.15;
                    }

                    pace.Add(p);
                }

                // Plausible pace: 0.35–1.6 s per m.
                if (pace.Any(p => p < 0.35 || p > 1.6))
                    return double.MaxValue;

                double mean = pace.Average();
                double spread = pace.Count > 1 ? Math.Sqrt(pace.Select(p => (p - mean) * (p - mean)).Average()) / mean : 0.3;
                return spread + 0.1 * (n - count);
            }
        }
    }
}
