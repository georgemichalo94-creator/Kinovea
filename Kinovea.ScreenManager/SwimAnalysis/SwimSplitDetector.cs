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

        /// <summary>Debug output (null: none).</summary>
        public static Action<string> Trace;

        private struct Crossing
        {
            public double Frame;
            public bool Far;
            public bool Long;
        }

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
            List<Crossing> crossings = new List<Crossing>();
            crossings.AddRange(Crossings(result, head, T, direction, fps, true));
            crossings.AddRange(Crossings(result, head, T, direction, fps, false));
            if (Trace != null)
            {
                foreach (Crossing c in crossings.OrderBy(c => c.Frame))
                    Trace(string.Format(System.Globalization.CultureInfo.InvariantCulture, "crossing {0:0.0} {1} {2}", c.Frame, c.Far ? "far" : "near", c.Long ? "long" : "mark"));
            }

            // Marks: crossings of the far and the near rope less than 1 s apart are the same checkpoint. After the start of the
            // red last 5 m, the short red parts are pieces of it.
            List<Dictionary<string, double>> ends = Group(crossings.Where(c => c.Long).ToList(), fps);
            double firstEnd = ends.Count > 0 ? ends.Min(e => Time(e)) : double.MaxValue;
            List<Dictionary<string, double>> marks = Group(crossings.Where(c => !c.Long && c.Frame < firstEnd - 0.3 * fps).ToList(), fps);

            // Distances of the marks passed: 15, 25, 35 from the wall of the lap (pool of 50 m).
            int[] distances = pool == 50 ? new[] { 15, 25, 35 } : new[] { 15 };
            List<double> times = marks.Select(m => Time(m)).ToList();
            int[] assigned = Assign(times, lapStart, fps, distances);
            for (int i = 0; i < marks.Count; i++)
            {
                if (assigned[i] <= 0)
                    continue;

                SwimSplitFound f = new SwimSplitFound { LapDistance = assigned[i], Confidence = marks[i].ContainsKey(CueFar) && marks[i].ContainsKey(CueNear) ? 0.8 : 0.6 };
                foreach (KeyValuePair<string, double> c in marks[i])
                    f.Cues[c.Key] = c.Value;
                found.Add(f);
            }

            // The start of the red last 5 m (45 in a 50 m pool, 20 in a 25 m pool): the first one after the last mark.
            double after = found.Count > 0 ? found.Max(f => Time(f.Cues)) : lapStart;
            Dictionary<string, double> end = ends.Where(e => Time(e) > after + 0.5 * fps).OrderBy(e => Time(e)).FirstOrDefault();
            if (end != null)
            {
                SwimSplitFound f = new SwimSplitFound { LapDistance = pool - 5, Confidence = end.ContainsKey(CueFar) && end.ContainsKey(CueNear) ? 0.75 : 0.55 };
                foreach (KeyValuePair<string, double> c in end)
                    f.Cues[c.Key] = c.Value;
                found.Add(f);
            }

            return found.OrderBy(f => f.LapDistance).ToList();
        }

        /// <summary>Main time of a checkpoint: the far rope if it has it, else the near one.</summary>
        private static double Time(Dictionary<string, double> cues)
        {
            double t;
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
            values.Sort();
            int n = values.Count;
            return n % 2 == 1 ? values[n / 2] : 0.5 * (values[n / 2 - 1] + values[n / 2]);
        }

        private static double LaneWidth(SwimStrokeLane lane, double x)
        {
            return Math.Max(5.0, Math.Abs(lane.Y2(x) - lane.Y1(x)));
        }

        /// <summary>
        /// Crossings of the head with the marks of one rope: a mark (short red part) or the start of the red last 5 m
        /// (long part: its edge towards the swimmer), followed from frame to frame with the camera motion.
        /// </summary>
        private static List<Crossing> Crossings(SwimStrokeResult result, double[] head, int T, int direction, double fps, bool far)
        {
            List<Crossing> raw = new List<Crossing>();
            List<int[]> red = far ? result.RedFar : result.RedNear;
            int settle = Math.Max(1, (int)Math.Round(0.3 * fps));
            for (int t = settle; t < T; t++)
            {
                if (double.IsNaN(head[t]) || double.IsNaN(head[t - 1]))
                    continue;

                int[] now = Joined(red[t], result.Lanes[t]), before = Joined(red[t - 1], result.Lanes[t - 1]);
                if (now.Length == 0 || before.Length == 0)
                    continue;

                double[] shift = t < result.Shifts.Count ? result.Shifts[t] : null;
                for (int i = 0; i + 1 < now.Length; i += 2)
                {
                    bool isLong;
                    double x = Point(result.Lanes[t], now[i], now[i + 1], direction, out isLong);

                    // The same part in the previous frame, moved by the camera.
                    double best = double.MaxValue, xb = double.NaN;
                    bool longBefore = false;
                    for (int j = 0; j + 1 < before.Length; j += 2)
                    {
                        bool lb;
                        double xp = Point(result.Lanes[t - 1], before[j], before[j + 1], direction, out lb);
                        if (lb != isLong)
                            continue;

                        double moved = xp;
                        if (shift != null)
                            moved += shift[Math.Max(0, Math.Min(shift.Length - 1, (int)Math.Round(xp)))];
                        double e = Math.Abs(moved - x);
                        if (e < best)
                        {
                            best = e;
                            xb = xp;
                            longBefore = lb;
                        }
                    }

                    double lw = LaneWidth(result.Lanes[t], x);
                    if (double.IsNaN(xb) || best > Math.Max(6.0, 0.5 * lw))
                        continue;

                    double s0 = direction * (head[t - 1] - xb);
                    double s1 = direction * (head[t] - x);
                    if (s0 < 0 && s1 >= 0)
                        raw.Add(new Crossing { Frame = t - 1 + (-s0) / Math.Max(1e-9, s1 - s0), Far = far, Long = isLong });
                }
            }

            // The head goes back and forth over a mark (splashes): one crossing per mark, the middle of the ones within 0.6 s.
            List<Crossing> merged = new List<Crossing>();
            foreach (bool isLong in new[] { false, true })
            {
                List<Crossing> list = raw.Where(c => c.Long == isLong).OrderBy(c => c.Frame).ToList();
                int i = 0;
                while (i < list.Count)
                {
                    int j = i;
                    while (j + 1 < list.Count && list[j + 1].Frame - list[i].Frame <= 0.6 * fps)
                        j++;
                    List<double> frames = list.Skip(i).Take(j - i + 1).Select(c => c.Frame).ToList();
                    merged.Add(new Crossing { Frame = SwimLearningMedian(frames), Far = far, Long = isLong });
                    i = j + 1;
                }
            }

            return merged;
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

        /// <summary>Crossings of the far and the near rope less than 1 s apart: one checkpoint (cues far, near, and their middle).</summary>
        private static List<Dictionary<string, double>> Group(List<Crossing> crossings, double fps)
        {
            List<Dictionary<string, double>> result = new List<Dictionary<string, double>>();
            List<Crossing> far = crossings.Where(c => c.Far).OrderBy(c => c.Frame).ToList();
            List<Crossing> near = crossings.Where(c => !c.Far).OrderBy(c => c.Frame).ToList();
            bool[] used = new bool[near.Count];
            foreach (Crossing f in far)
            {
                int best = -1;
                double e = double.MaxValue;
                for (int k = 0; k < near.Count; k++)
                {
                    double d = Math.Abs(near[k].Frame - f.Frame);
                    if (!used[k] && d <= 1.0 * fps && d < e)
                    {
                        e = d;
                        best = k;
                    }
                }

                Dictionary<string, double> cues = new Dictionary<string, double> { { CueFar, f.Frame } };
                if (best >= 0)
                {
                    used[best] = true;
                    cues[CueNear] = near[best].Frame;
                    cues[CueMiddle] = 0.5 * (f.Frame + near[best].Frame);
                }

                result.Add(cues);
            }

            for (int k = 0; k < near.Count; k++)
            {
                if (!used[k])
                    result.Add(new Dictionary<string, double> { { CueNear, near[k].Frame } });
            }

            return result.OrderBy(c => Time(c)).ToList();
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
