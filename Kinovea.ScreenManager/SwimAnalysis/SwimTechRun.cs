#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.9.9) A stroke cycle of a start or a turn of a training video: from a stroke mark to the next one of the same arm action (one
    /// pull of breaststroke or butterfly, two strokes of freestyle or backstroke). Seconds from the origin of the phase.
    /// </summary>
    public sealed class SwimTechCycle
    {
        /// <summary>1, 2, … in the order of the video.</summary>
        public int Number;
        public double Start, End;

        public double Tempo
        {
            get { return End - Start; }
        }

        /// <summary>
        /// Breaststroke and butterfly: the glide at the end of the cycle (the arms at full reach and the legs extended, until the
        /// next pull), [GlideStart, End). Null: not measured (no skeleton) or another style.
        /// </summary>
        public double? Glide;
        public double? GlideStart;

        /// <summary>Breaststroke: the knees start to bend (the legs recover) and their deepest bend (the kick starts).</summary>
        public double? LegsStart;
        public double? KickStart;

        /// <summary>Metres the head travels in the cycle (null: not measured); the head from the wall at its start.</summary>
        public double? Distance;
        public double? Position;

        /// <summary>The last cycle of an approach: it ends at the wall.</summary>
        public bool ToWall;
    }

    public enum SwimGlideClass
    {
        None,
        Quick,
        Short,
        Full
    }

    /// <summary>
    /// (2.9.9) The measures of a start or a turn of a training video from its origin (G: the start signal; W: the contact with the wall)
    /// for the technical HUD: the underwater phase, each stroke cycle (tempo from the stroke marks; glide of breaststroke and butterfly
    /// from the skeleton read; metres from the technical distances marked: the head at 5 m from the backstroke flags, the T marks, the
    /// wall), the pool position of the head where it is measured, and the part of the phase at any moment.
    /// Distances are given only between two known places (the wall, 5, 10, 15 m): never extrapolated.
    /// </summary>
    public sealed class SwimTechRun
    {
        /// <summary>A glide of at least this long is full (green), of at least ShortGlide short (amber), else quick (red).</summary>
        public const double FullGlide = 0.6;
        public const double ShortGlide = 0.4;

        /// <summary>The reach of the glide is the one of the last GlideEnd seconds of the cycle (before the pull).</summary>
        public const double GlideEnd = 0.35;

        /// <summary>Strokes of the approach counted at most this long before the wall, of the departure after the breakout.</summary>
        public const double ApproachSeconds = 10;
        public const double DepartureSeconds = 20;

        public SwimTechPhase Phase;
        public string Style;

        /// <summary>The parts of the reading measured (null without them) and the timing of the video.</summary>
        public SwimSkeleton Skeleton;
        public SwimTrack Track;
        public SwimTiming Timing;
        public bool Breaststroke;

        /// <summary>A medley: strokes closer than this (seconds, median) are the arms of freestyle or backstroke, else cycles.</summary>
        public const double MedleyCycleGap = 0.9;
        public bool Butterfly;

        /// <summary>Strokes per cycle: 1 (breaststroke, butterfly; a medley: each stroke), 2 (freestyle, backstroke).</summary>
        public int StrokesPerCycle = 1;

        /// <summary>The approach of a turn: strokes per cycle (2 before a flip turn) and its glides (before an open turn).</summary>
        public int ApproachPerCycle = 1;
        public bool ApproachGlides;

        /// <summary>Seconds from the origin of the stroke marks after the breakout and before the wall.</summary>
        public List<double> Strokes = new List<double>();
        public List<double> ApproachStrokes = new List<double>();

        public List<SwimTechCycle> Cycles = new List<SwimTechCycle>();
        public List<SwimTechCycle> Approach = new List<SwimTechCycle>();

        /// <summary>Known places of the head: seconds from the origin → metres from the wall (the marks of the technical distances).</summary>
        public SortedList<double, double> Anchors = new SortedList<double, double>();

        /// <summary>Metres of the head from the wall where they are measured (seconds from the origin → metres), in the order of time.</summary>
        public List<PointF> Positions = new List<PointF>();

        /// <summary>The first and the last second of the phase measured (from the origin).</summary>
        public double First, Last;

        /// <summary>For the tests: the samples of the glides are written here when it is not null.</summary>
        public static System.Text.StringBuilder Debug;

        /// <summary>The head in the frame of the pool (reference pixels: image x minus the camera's pan) at a time, NaN where unknown.</summary>
        private Func<double, double> poolX;

        #region The values at a moment
        public static SwimGlideClass Classify(double? glide)
        {
            if (!glide.HasValue)
                return SwimGlideClass.None;
            return glide.Value >= FullGlide ? SwimGlideClass.Full : glide.Value >= ShortGlide ? SwimGlideClass.Short : SwimGlideClass.Quick;
        }

        /// <summary>Strokes made after the breakout by this time.</summary>
        public int StrokesAt(double t)
        {
            return Strokes.Count(s => s <= t + 1e-6);
        }

        /// <summary>The cycle going on at this time (after the breakout or before the wall), null outside them.</summary>
        public SwimTechCycle CycleAt(double t)
        {
            SwimTechCycle c = Cycles.LastOrDefault(x => x.Start <= t + 1e-6 && t < x.End);
            return c ?? Approach.LastOrDefault(x => x.Start <= t + 1e-6 && t < x.End);
        }

        /// <summary>The last cycle finished by this time (its values are the ones shown).</summary>
        public SwimTechCycle LastDone(double t)
        {
            // After the wall only the cycles after it (none yet before the first one ends).
            return t >= 0 ? Cycles.LastOrDefault(x => x.End <= t + 1e-6) : Approach.LastOrDefault(x => x.End <= t + 1e-6);
        }

        /// <summary>Metres of the head from the wall at a time (null: not measured there).</summary>
        public double? PositionAt(double t)
        {
            if (Positions.Count < 2 || t < Positions[0].X || t > Positions[Positions.Count - 1].X)
                return null;
            int i = Positions.FindIndex(p => p.X >= t);
            if (i <= 0)
                return Positions[0].Y;
            PointF a = Positions[i - 1], b = Positions[i];
            if (b.X - a.X > 0.5f)
                return null;
            return a.Y + (b.Y - a.Y) * (t - a.X) / Math.Max(1e-6, b.X - a.X);
        }

        /// <summary>Speed of the head (m/s) at a time, over ±0.25 s (null: not measured there).</summary>
        public double? SpeedAt(double t)
        {
            double? a = PositionAt(t - 0.25), b = PositionAt(t + 0.25);
            if (!a.HasValue || !b.HasValue)
                return null;
            return Math.Abs(b.Value - a.Value) / 0.5;
        }

        /// <summary>The head in the frame of the pool (reference pixels), NaN where the reading has no point.</summary>
        public double PoolX(double t)
        {
            return poolX != null ? poolX(t) : double.NaN;
        }

        /// <summary>
        /// The part of the phase at a time, in the language of the analysis: «Ώθηση», «Δελφινισμοί», «Έξοδος», «Χεριά 2 · έλξη»…
        /// </summary>
        public string PartAt(double t)
        {
            SwimTechPhase p = Phase;
            if (p == null)
                return "";

            Func<int, string> stroke = n => SwimLang.T("Χεριά") + " " + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (p.Kind == SwimPhaseKind.Turn && t < 0)
            {
                if (p.RotationStart.HasValue && t >= p.RotationStart.Value)
                    return SwimLang.T("Περιστροφή");
                SwimTechCycle a = Approach.LastOrDefault(x => x.Start <= t + 1e-6 && t < x.End);
                if (a != null)
                    return a.ToWall ? SwimLang.T("Τελευταία χεριά · στον τοίχο") : SwimLang.T("Προσέγγιση") + " · " + stroke(a.Number);
                return SwimLang.T("Προσέγγιση");
            }

            if (p.Kind == SwimPhaseKind.Turn)
            {
                if (!p.Pushoff.HasValue || t < p.Pushoff.Value)
                    return SwimLang.T("Στον τοίχο");
            }
            else
            {
                if (t < 0)
                    return SwimLang.T("Εκκίνηση");
                if (p.Entry.HasValue && t < p.Entry.Value)
                    return t < (p.Block ?? 0) ? SwimLang.T("Εκκίνηση") : SwimLang.T("Πτήση");
            }

            if (p.Breakout.HasValue && t < p.Breakout.Value - 0.15)
                return p.Kicks.Count > 0 && t >= p.Kicks[0] - 0.3 ? SwimLang.T("Δελφινισμοί") : SwimLang.T("Υποβρύχια φάση");
            if (p.Breakout.HasValue && (Strokes.Count == 0 ? t < p.Breakout.Value + 0.6 : t < Strokes[0]))
                return SwimLang.T("Έξοδος");

            SwimTechCycle c = Cycles.LastOrDefault(x => x.Start <= t + 1e-6 && t < x.End);
            if (c != null)
            {
                string n = stroke(c.Number);
                if (c.GlideStart.HasValue && t >= c.GlideStart.Value)
                    return n + " · " + SwimLang.T("γλίστρημα");
                if (Breaststroke && c.KickStart.HasValue && t >= c.KickStart.Value)
                    return n + " · " + SwimLang.T("κλωτσιά");
                if (Breaststroke && c.LegsStart.HasValue && t >= c.LegsStart.Value)
                    return n + " · " + SwimLang.T("τα πόδια μαζεύονται");
                return Breaststroke || Butterfly ? n + " · " + SwimLang.T("έλξη") : n;
            }

            if (Strokes.Count > 0 && t >= Strokes[Strokes.Count - 1])
                return stroke(Math.Max(1, (Strokes.Count + StrokesPerCycle - 1) / StrokesPerCycle));
            return "";
        }
        #endregion

        #region Measures
        /// <summary>
        /// The measures of a phase of a training video from its marks, the skeleton read (null: no glides) and the place of the swimmer
        /// read (null: no metres between the known places).
        /// </summary>
        public static SwimTechRun Of(SwimRace race, SwimTiming timing, SwimTechPhase phase, SwimSkeleton skeleton, SwimTrack track)
        {
            if (race == null || timing == null || !timing.IsValid || phase == null)
                return null;

            SwimTechRun run = new SwimTechRun { Phase = phase, Style = race.Style ?? "", Skeleton = skeleton, Track = track, Timing = timing };
            run.Breaststroke = run.Style == "br";
            run.Butterfly = run.Style == "fly";
            run.StrokesPerCycle = run.Style == "fs" || run.Style == "bk" ? 2 : 1;

            // The approach of a turn: a flip turn comes from freestyle or backstroke (two strokes a cycle), an open one from breaststroke
            // or butterfly (one, with its glide) — also in a medley.
            bool flipApproach = phase.RotationStart.HasValue || run.Style == "fs" || run.Style == "bk";
            run.ApproachPerCycle = flipApproach ? 2 : 1;
            run.ApproachGlides = !flipApproach;

            Func<long, double> rel = ts => timing.Seconds(ts, phase.Origin);
            List<double> strokes = race.Marks
                .Where(m => m.Type == SwimMarkType.Stroke && m.Timestamp >= phase.EventStart && m.Timestamp < phase.EventEnd)
                .Select(m => rel(m.Timestamp)).OrderBy(t => t).ToList();
            double after = phase.Breakout.HasValue ? phase.Breakout.Value - 0.35 : (phase.Kind == SwimPhaseKind.Turn ? phase.Pushoff ?? 0 : phase.Entry ?? 0);
            run.Strokes = strokes.Where(t => t >= after && t <= after + DepartureSeconds).ToList();
            if (phase.Kind == SwimPhaseKind.Turn)
                run.ApproachStrokes = strokes.Where(t => t < 0 && t >= -ApproachSeconds && (!phase.RotationStart.HasValue || t < phase.RotationStart.Value)).ToList();

            // A medley: the stroke after the start is butterfly; after a turn, breaststroke when its strokes are a cycle apart (1 s or
            // more), else freestyle or backstroke (an arm each 0.5-0.8 s: two strokes a cycle).
            if (run.Style == "im")
            {
                List<double> gaps = new List<double>();
                for (int i = 1; i < run.Strokes.Count; i++)
                    gaps.Add(run.Strokes[i] - run.Strokes[i - 1]);
                bool arms = gaps.Count >= 2 && SwimSignal.Percentile(gaps, 50) < MedleyCycleGap;
                run.StrokesPerCycle = arms ? 2 : 1;
                run.Breaststroke = !arms && phase.Kind == SwimPhaseKind.Turn;
                run.Butterfly = !arms && phase.Kind != SwimPhaseKind.Turn;
            }

            // The cycles: from a stroke to the stroke of the same arm action.
            int k = run.StrokesPerCycle;
            for (int i = 0; i + k < run.Strokes.Count; i += k)
                run.Cycles.Add(new SwimTechCycle { Number = run.Cycles.Count + 1, Start = run.Strokes[i], End = run.Strokes[i + k] });
            int ka = run.ApproachPerCycle;
            for (int i = 0; i + ka < run.ApproachStrokes.Count; i += ka)
                run.Approach.Add(new SwimTechCycle { Number = run.Approach.Count + 1, Start = run.ApproachStrokes[i], End = run.ApproachStrokes[i + ka] });
            if (run.ApproachStrokes.Count > 0 && run.ApproachGlides)
            {
                // The last pull of the approach goes to the wall (the touch of an open turn).
                double last = run.ApproachStrokes[run.ApproachStrokes.Count - 1];
                double wall = phase.RotationStart ?? 0;
                if (wall - last > 0.3)
                    run.Approach.Add(new SwimTechCycle { Number = run.Approach.Count + 1, Start = last, End = wall, ToWall = true });
            }

            run.First = phase.Kind == SwimPhaseKind.Turn && run.ApproachStrokes.Count > 0 ? run.ApproachStrokes[0] : 0;
            run.Last = run.Strokes.Count > 0 ? run.Strokes[run.Strokes.Count - 1] : (phase.Breakout ?? phase.Pushoff ?? 0);

            if (skeleton != null && skeleton.Count > 10)
                run.MeasureGlides(skeleton, timing);
            run.MeasurePositions(timing, track);
            return run;
        }

        /// <summary>Breaststroke and butterfly: the glide at the end of each cycle, and the legs of breaststroke, from the skeleton.</summary>
        private void MeasureGlides(SwimSkeleton sk, SwimTiming timing)
        {
            PointF[] pts = new PointF[SwimJoints.Count];
            float[] vis = new float[SwimJoints.Count];
            IEnumerable<SwimTechCycle> measured = (Breaststroke || Butterfly ? Cycles : Enumerable.Empty<SwimTechCycle>())
                .Concat(ApproachGlides ? Approach.Where(a => !a.ToWall) : Enumerable.Empty<SwimTechCycle>());
            foreach (SwimTechCycle c in measured)
            {
                List<double> ts = new List<double>(), reach = new List<double>(), knee = new List<double>();
                for (double t = c.Start; t <= c.End + 1e-6; t += 1.0 / 30)
                {
                    long ts0 = Phase.Origin + Ticks(timing, t);
                    if (!sk.TryGet(ts0, 1, pts, vis))
                        continue;
                    double r = Reach(pts, vis), kn = Knee(pts, vis);
                    if (double.IsNaN(r))
                        continue;
                    ts.Add(t);
                    reach.Add(r);
                    knee.Add(kn);
                }

                if (ts.Count < 8)
                    continue;

                // The pose of single images is noisy: a median of 5 images.
                reach = SwimSignal.Median(reach.ToArray(), 5).ToList();
                knee = SwimSignal.Median(knee.ToArray(), 5).ToList();

                // The glide: back from the end of the cycle while the hands stay within 8% of a body length of their reach and the knees
                // are extended.
                int n = ts.Count;
                if (Debug != null)
                {
                    Debug.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "cycle {0} {1:0.00}-{2:0.00}", c.Number, c.Start, c.End));
                    for (int i = 0; i < n; i++)
                        Debug.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  {0:0.00} reach {1:0.00} knee {2:0}", ts[i], reach[i], knee[i]));
                }

                // The reach of the glide: at the end of the cycle (the arms that recover forward can reach further for a moment).
                double top = double.MinValue;
                for (int i = n - 1; i >= 0 && ts[i] >= c.End - GlideEnd; i--)
                    top = Math.Max(top, reach[i]);
                int g = n - 1;
                while (g > 0 && reach[g - 1] >= top - 0.08 && (double.IsNaN(knee[g - 1]) || knee[g - 1] >= 150))
                    g--;
                if (g < n - 1)
                {
                    c.GlideStart = ts[g];
                    c.Glide = c.End - ts[g];
                }

                if (Breaststroke)
                {
                    // The legs: the knees first under 150° after the start of the cycle, their deepest bend before the glide.
                    int legs = -1, deepest = -1;
                    for (int i = 0; i < g; i++)
                    {
                        if (double.IsNaN(knee[i]))
                            continue;
                        if (legs < 0 && knee[i] < 150)
                            legs = i;
                        if (legs >= 0 && (deepest < 0 || knee[i] < knee[deepest]))
                            deepest = i;
                    }

                    if (legs >= 0)
                        c.LegsStart = ts[legs];
                    if (deepest > legs && legs >= 0)
                        c.KickStart = ts[deepest];
                }
            }
        }

        /// <summary>
        /// The head from the wall in metres between the known places (the head at the technical distances marked, from the flags and
        /// the marks of the pool; the contact of a turn): even speed between two of them, not outside. With a reading (its track and
        /// the camera's pan), the head in the frame of the pool for the strokes drawn on the water.
        /// </summary>
        private void MeasurePositions(SwimTiming timing, SwimTrack track)
        {
            SwimTechPhase p = Phase;
            if (track != null && track.HasPan)
            {
                Func<double, double> pan = t => TrackPan(track, p.Origin + Ticks(timing, t));
                Func<double, double> headX = t =>
                {
                    float x, y, top, bottom;
                    long ts = p.Origin + Ticks(timing, t);
                    return track.TryGet(ts, 1, out x, out y, out top, out bottom) ? x : double.NaN;
                };
                poolX = t =>
                {
                    double h = headX(t), q = pan(t);
                    return double.IsNaN(h) || double.IsNaN(q) ? double.NaN : h - q;
                };
            }

            // (2.9.9) The known places in time: (seconds, metres). The head at the splits (±5 from the backstroke flags, ±10/±15 from
            // the marks of the pool) and, before a turn, its contact (0 m: from the head at 5 m to the touch are the 5 m of the
            // analysts). Between two of them the swimmer's speed is taken as even: the camera's pan as a ruler was off by 12-35%
            // (checked on the turns of VOLOS), the times of the known places are what the analysts measure. No metres outside them.
            List<Tuple<double, double>> known = new List<Tuple<double, double>>();
            foreach (KeyValuePair<int, double> s in p.Splits)
            {
                known.Add(Tuple.Create(s.Value, (double)Math.Abs(s.Key)));
                Anchors[s.Value] = Math.Abs(s.Key);
            }

            if (p.Kind == SwimPhaseKind.Turn)
                known.Add(Tuple.Create(0.0, 0.0));

            // Two sides of a turn: before the contact (the approach) and after it; each needs two known places (a start: after it).
            foreach (bool approach in new[] { true, false })
            {
                List<Tuple<double, double>> side = known.Where(q => approach ? q.Item1 <= 0 : q.Item1 > 0).OrderBy(q => q.Item1).ToList();
                if (side.Count < 2)
                    continue;

                double t0 = side[0].Item1, t1 = side[side.Count - 1].Item1;
                for (double t = t0; t <= t1 + 1e-6; t += 1.0 / 30)
                {
                    int i = side.FindIndex(q => q.Item1 >= t);
                    Tuple<double, double> a = side[Math.Max(0, i - 1)], b = side[Math.Max(1, i)];
                    if (b.Item1 - a.Item1 < 0.2)
                        continue;
                    double m = a.Item2 + (b.Item2 - a.Item2) * (t - a.Item1) / (b.Item1 - a.Item1);
                    Positions.Add(new PointF((float)t, (float)m));
                }
            }

            Positions.Sort((a, b) => a.X.CompareTo(b.X));
            foreach (SwimTechCycle c in Cycles.Concat(Approach))
            {
                double? a = PositionAt(c.Start), b = c.ToWall ? 0.0 : PositionAt(c.End);
                c.Position = a;
                if (a.HasValue && b.HasValue)
                    c.Distance = Math.Abs(b.Value - a.Value);
            }
        }

        /// <summary>Hands ahead of the shoulders along the swim (body lengths: shoulders to hips to ankles), NaN without them.</summary>
        private static double Reach(PointF[] p, float[] v)
        {
            Func<int, bool> ok = j => v[j] >= 0.3f;
            double sx = Avg(p, v, SwimJoints.LeftShoulder, SwimJoints.RightShoulder);
            double hx = Avg(p, v, SwimJoints.LeftHip, SwimJoints.RightHip);
            double ax = Avg(p, v, SwimJoints.LeftAnkle, SwimJoints.RightAnkle);
            if (double.IsNaN(sx) || double.IsNaN(hx))
                return double.NaN;
            double sy = AvgY(p, v, SwimJoints.LeftShoulder, SwimJoints.RightShoulder), hy = AvgY(p, v, SwimJoints.LeftHip, SwimJoints.RightHip);
            double ay = AvgY(p, v, SwimJoints.LeftAnkle, SwimJoints.RightAnkle);
            double length = Math.Sqrt((sx - hx) * (sx - hx) + (sy - hy) * (sy - hy)) +
                (double.IsNaN(ax) ? Math.Sqrt((sx - hx) * (sx - hx) + (sy - hy) * (sy - hy)) * 1.4 : Math.Sqrt((hx - ax) * (hx - ax) + (hy - ay) * (hy - ay)));
            if (length < 10)
                return double.NaN;
            double dir = Math.Sign(sx - hx);
            double best = double.NaN;
            foreach (int w in new[] { SwimJoints.LeftWrist, SwimJoints.RightWrist })
            {
                if (!ok(w))
                    continue;
                double r = (p[w].X - sx) * dir / length;
                if (double.IsNaN(best) || r > best)
                    best = r;
            }

            return best;
        }

        /// <summary>Angle of the knees (degrees, the mean of the two sides seen), NaN without them.</summary>
        private static double Knee(PointF[] p, float[] v)
        {
            List<double> a = new List<double>();
            foreach (int[] s in new[] { new[] { SwimJoints.LeftHip, SwimJoints.LeftKnee, SwimJoints.LeftAnkle }, new[] { SwimJoints.RightHip, SwimJoints.RightKnee, SwimJoints.RightAnkle } })
            {
                if (v[s[0]] < 0.3f || v[s[1]] < 0.3f || v[s[2]] < 0.3f)
                    continue;
                double x1 = p[s[0]].X - p[s[1]].X, y1 = p[s[0]].Y - p[s[1]].Y, x2 = p[s[2]].X - p[s[1]].X, y2 = p[s[2]].Y - p[s[1]].Y;
                double n = Math.Sqrt(x1 * x1 + y1 * y1) * Math.Sqrt(x2 * x2 + y2 * y2);
                if (n < 1e-6)
                    continue;
                a.Add(Math.Acos(Math.Max(-1, Math.Min(1, (x1 * x2 + y1 * y2) / n))) * 180 / Math.PI);
            }

            return a.Count == 0 ? double.NaN : a.Average();
        }

        private static double Avg(PointF[] p, float[] v, int a, int b)
        {
            bool va = v[a] >= 0.3f, vb = v[b] >= 0.3f;
            return va && vb ? 0.5 * (p[a].X + p[b].X) : va ? p[a].X : vb ? p[b].X : double.NaN;
        }

        private static double AvgY(PointF[] p, float[] v, int a, int b)
        {
            bool va = v[a] >= 0.3f, vb = v[b] >= 0.3f;
            return va && vb ? 0.5 * (p[a].Y + p[b].Y) : va ? p[a].Y : vb ? p[b].Y : double.NaN;
        }

        /// <summary>
        /// The skeleton and the head with the camera's pan added up (X − Pan: the head in the frame of the pool) of a technical reading,
        /// in reference pixels at the timestamps of its images. False with fewer than 10 images or no swimmer.
        /// </summary>
        public static bool PartsOfReading(SwimTechReading r, IList<long> stamps, Size reference, out SwimSkeleton skeleton, out SwimTrack track)
        {
            skeleton = null;
            track = null;
            int n = Math.Min(r.Frames.Count, stamps.Count);
            if (n < 10 || r.Width <= 0 || r.Height <= 0)
                return false;

            float sx = reference.Width / (float)r.Width, sy = reference.Height / (float)r.Height;
            double step = (stamps[n - 1] - stamps[0]) / (double)Math.Max(1, n - 1);
            if (step <= 0)
                return false;

            skeleton = new SwimSkeleton { Start = stamps[0], Step = step };
            float[] hx = new float[n], hy = new float[n], top = new float[n], bottom = new float[n];
            double[] pan = new double[n];
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                SwimTechFrame f = r.Frames[i];
                if (!float.IsNaN(f.CameraDx))
                    sum += f.CameraDx * sx;
                pan[i] = sum;

                SwimPose pose = null;
                if (f.Box != null && f.Pose != null && f.Pose.Length >= SwimJoints.Count * 3)
                {
                    pose = new SwimPose();
                    for (int j = 0; j < SwimJoints.Count; j++)
                    {
                        pose.X[j] = f.Pose[j * 3] * sx;
                        pose.Y[j] = f.Pose[j * 3 + 1] * sy;
                        pose.Visibility[j] = f.Pose[j * 3 + 2];
                    }
                }

                skeleton.Add(pose);
                hx[i] = hy[i] = top[i] = bottom[i] = float.NaN;
                if (f.Box != null)
                {
                    bool nose = pose != null && pose.Visibility[SwimJoints.Nose] >= 0.3f && !float.IsNaN(pose.X[SwimJoints.Nose]);
                    hx[i] = nose ? pose.X[SwimJoints.Nose] : f.Box.CenterX * sx;
                    hy[i] = nose ? pose.Y[SwimJoints.Nose] : f.Box.CenterY * sy;
                    top[i] = f.Box.Y0 * sy;
                    bottom[i] = f.Box.Y1 * sy;
                }
            }

            // Images without the swimmer keep the nearest place seen (the track has a point for each image).
            int firstSeen = Array.FindIndex(hx, v => !float.IsNaN(v));
            if (firstSeen < 0)
            {
                skeleton = null;
                return false;
            }

            track = new SwimTrack { Start = stamps[0], Step = step };
            float lx = hx[firstSeen], ly = hy[firstSeen], lt = top[firstSeen], lb = bottom[firstSeen];
            for (int i = 0; i < n; i++)
            {
                if (!float.IsNaN(hx[i]))
                {
                    lx = hx[i];
                    ly = hy[i];
                    lt = top[i];
                    lb = bottom[i];
                }

                track.X.Add((int)Math.Round(lx));
                track.Y.Add((int)Math.Round(ly));
                track.Top.Add((int)Math.Round(lt));
                track.Bottom.Add((int)Math.Round(lb));
                track.Pan.Add((int)Math.Round(pan[i]));
                track.Slope.Add(0);
            }

            return true;
        }

        /// <summary>
        /// For the lab and the tests (no video): the measures of a turn read, its events as the marks of a training analysis (1000
        /// timestamps per image of the reading). in5 / out5: the head at −5 / +5 (seconds of the reading), null if unknown.
        /// </summary>
        public static SwimTechRun OfReading(SwimTechReading r, SwimTurnEvents ev, string style, double? in5, double? out5)
        {
            if (r == null || ev == null || !ev.Wall.HasValue || r.Fps <= 0 || r.Frames.Count < 10)
                return null;

            SwimTiming timing = new SwimTiming { AverageTimestampsPerFrame = 1000, FrameIntervalMilliseconds = 1000.0 / r.Fps, HighSpeedFactor = 1 };
            Func<double, long> ts = t => (long)Math.Round(t * r.Fps) * 1000;
            List<long> stamps = r.Frames.Select(f => ts(f.T)).ToList();
            SwimRace race = new SwimRace { Kind = SwimRace.KindTraining, Style = style };
            Action<SwimMarkType, double?, int> mark = (type, t, distance) =>
            {
                if (t.HasValue)
                    race.Marks.Add(new SwimMark { Type = type, Timestamp = ts(t.Value), Distance = distance, Wall = -1 });
            };
            bool pullsIn = ev.Kind == SwimTurnKind.Open;
            bool pullsOut = style == "br" || style == "fly" || (style == "im" && ev.PullDown.HasValue);
            mark(SwimMarkType.WallContact, ev.Wall, 0);
            mark(SwimMarkType.RotationStart, ev.RotationStart, 0);
            mark(SwimMarkType.FeetOff, ev.Pushoff, 0);
            mark(SwimMarkType.Breakout, ev.Breakout, 0);
            foreach (double k in ev.Kicks)
                mark(SwimMarkType.Kick, k, 0);
            foreach (double s in (pullsIn ? ev.PullsIn : ev.StrokesIn).Concat(pullsOut ? ev.PullsOut : ev.StrokesOut))
                mark(SwimMarkType.Stroke, s, 0);
            mark(SwimMarkType.TechSplit, in5, -5);
            mark(SwimMarkType.TechSplit, out5, 5);

            SwimSkeleton skeleton;
            SwimTrack track;
            if (PartsOfReading(r, stamps, new Size(r.Width, r.Height), out skeleton, out track))
            {
                race.Skeletons.Add(skeleton);
                race.Tracks.Add(track);
            }

            List<SwimTechPhase> phases = SwimTechCalculator.Compute(race, null, timing);
            SwimTechPhase phase = phases.FirstOrDefault(p => p.Kind == SwimPhaseKind.Turn);
            return phase == null ? null : Of(race, timing, phase, skeleton, track);
        }

        /// <summary>The camera's pan of a part read at a timestamp (linear between its points), NaN outside it.</summary>
        public static double TrackPan(SwimTrack track, long timestamp)
        {
            if (track == null || !track.HasPan || track.Step <= 0)
                return double.NaN;
            double f = (timestamp - track.Start) / track.Step;
            int n = track.Pan.Count;
            if (f < -0.5 || f > n - 0.5)
                return double.NaN;
            f = Math.Max(0, Math.Min(n - 1, f));
            int i = (int)Math.Floor(f);
            int j = Math.Min(n - 1, i + 1);
            return track.Pan[i] + (track.Pan[j] - track.Pan[i]) * (f - i);
        }

        /// <summary>Timestamps of a number of seconds (the arithmetic of SwimTiming, frame based, high speed factor aware).</summary>
        public static long Ticks(SwimTiming timing, double seconds)
        {
            double hsf = timing.HighSpeedFactor <= 0 ? 1.0 : timing.HighSpeedFactor;
            double frames = seconds * 1000.0 * hsf / timing.FrameIntervalMilliseconds;
            return (long)Math.Round(frames * timing.AverageTimestampsPerFrame);
        }
        #endregion
    }
}
