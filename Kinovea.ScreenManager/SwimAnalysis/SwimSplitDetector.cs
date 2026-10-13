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
            int[] assigned = Assign(times, lapStart, fps, distances);
            for (int i = 0; i < marks.Count; i++)
            {
                if (assigned[i] <= 0)
                    continue;

                SwimSplitFound f = new SwimSplitFound { LapDistance = assigned[i], Confidence = marks[i].ContainsKey(CueMiddle) ? 0.8 : 0.55 };
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

            return found.OrderBy(f => f.LapDistance).ToList();
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
        /// </summary>
        public static void AddPace(List<SwimSplitFound> found, Dictionary<string, double> wall, int pool, double lastFiveRatio, int frames)
        {
            if (found == null || pool <= 0)
                return;

            List<SwimSplitFound> seen = found.Where(x => x.Cues.Count > 0 && !(x.Cues.Count == 1 && x.Cues.ContainsKey(CuePace))).ToList();
            if (seen.Count < 2)
                return;

            // Time per metre and time at the wall of the lap (straight line through the checkpoints: medians of the slopes of
            // each pair and of the intercepts, so that one wrong checkpoint does not move it).
            double slope, intercept;
            if (!PaceLine(seen, out slope, out intercept))
                return;

            // A checkpoint far from the pace of the others (more than a fifth of the time of 10 m: a mark crossed at another moment,
            // the swimmer hidden) becomes the pace of the others (the analyst checks it); with four checkpoints the pace stands
            // without it.
            double tolerance = 0.2 * 10 * slope;
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
                        tolerance = 0.2 * 10 * slope;
                        foreach (SwimSplitFound f in far)
                        {
                            f.Cues.Clear();
                            f.Cues[CuePace] = intercept + slope * f.LapDistance;
                            f.Confidence = 0.3;
                        }

                        seen = kept;
                    }
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

                SwimSplitFound added = new SwimSplitFound { LapDistance = d, Confidence = 0.3 };
                added.Cues[CuePace] = t;
                found.Add(added);
            }

            found.Sort((a, b) => a.LapDistance.CompareTo(b.LapDistance));
            SwimSplitFound end = found.FirstOrDefault(x => x.LapDistance == pool - 5);
            double endOnLine = intercept + slope * (pool - 5);
            double tEnd = end != null && seen.Contains(end) && Math.Abs(Time(end.Cues) - endOnLine) <= tolerance ? Time(end.Cues) : endOnLine;
            double tWall = tEnd + lastFiveRatio * 10 * slope;
            if (wall != null && tWall >= 0 && tWall <= frames - 1)
                wall[CuePace] = tWall;
        }

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

            // The steepest rise of the motion in the window (a rise of at least a tenth of the usual level).
            int h = Math.Max(1, (int)Math.Round(0.3 * fps));
            double steepest = 0.1 * usual;
            int tRise = -1;
            for (int t = Math.Max(from, h); t <= Math.Min(to, T - 1 - h); t++)
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
        /// the fastest swimmer to it.
        /// </summary>
        public static Dictionary<string, double> WallCues(SwimStrokeResult result, int direction, double fps, double after, double lapStart, int pool)
        {
            if (!double.IsNaN(lapStart) && pool > 0)
            {
                double earliest = lapStart + pool * FastestPace * fps;
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
        /// (a mark 10 m further takes between 0.5 and 1.6 times the time of the previous 10 m).
        /// </summary>
        private static int[] Assign(List<double> times, double lapStart, double fps, int[] distances)
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

                List<double> pace = new List<double>();
                for (int k = 1; k < d.Count; k++)
                {
                    if (t[k] <= t[k - 1])
                        return double.MaxValue;
                    double p = (t[k] - t[k - 1]) / (d[k] - d[k - 1]);
                    if (k == 1)
                        p *= 1.15;
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
