#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The lanes of a view of the whole pool from the stands (phones): the water, its far and near edges (the side walls of the
    /// pool), their vanishing point, and the lanes between the two walls as lines through that point, lanes of equal width with
    /// the perspective across the pool fitted to the ropes. A lane is numbered by its place from the near wall.
    /// (Lab, the analysts' phone races of one venue where both walls are in view: 8 lanes and the analyst's lane within a few
    /// pixels in 51 of 51; its place from the near wall + 1 = the lane of the race in all 29 races with a known lane.)
    /// </summary>
    public sealed class SwimPoolGrid
    {
        /// <summary>Lanes between the two walls.</summary>
        public int Lanes { get; private set; }

        /// <summary>Perspective across the pool (0: the far lanes as wide as the near ones).</summary>
        public double Perspective { get; private set; }

        /// <summary>The far wall (top of the water): y = FarA x + FarB, working pixels.</summary>
        public double FarA { get; private set; }
        public double FarB { get; private set; }

        /// <summary>The near wall (bottom of the water).</summary>
        public double NearA { get; private set; }
        public double NearB { get; private set; }

        /// <summary>Parts of the columns of the water whose edge is on the wall line (occluded or out of view otherwise).</summary>
        public double FarShare { get; private set; }
        public double NearShare { get; private set; }

        /// <summary>The rope evidence at the ropes of the grid (0..1) and at the middles of the lanes (the floor lines).</summary>
        public double Score { get; private set; }
        public double Middles { get; private set; }

        /// <summary>The vanishing point of the walls and the ropes.</summary>
        public double Vx { get; private set; }
        public double Vy { get; private set; }

        /// <summary>The column where the grid was fitted.</summary>
        public double X { get; private set; }

        private double yNear, yFar;

        /// <summary>The water of the pool (working pixels), for the motion of the swimmers.</summary>
        private bool[] pool;

        /// <summary>
        /// Both walls found along most of the pool, a clear grid of ropes, the near wall in the image: the lane numbers can be
        /// counted. Lab: all the views where the analysts' lanes matched; none of the views with a wall out of the image.
        /// </summary>
        public bool Sure
        {
            get
            {
                return Lanes >= 6 && FarShare >= 0.55 && NearShare >= 0.45 && Score >= 0.5 && Score - Middles >= 0.25 &&
                    yNear < height - 8 && yFar > 4 && yNear - yFar > 60;
            }
        }

        private readonly int width, height;

        private SwimPoolGrid(int width, int height)
        {
            this.width = width;
            this.height = height;
        }

        /// <summary>The y of rope k (0: the near wall, Lanes: the far wall) at the column x.</summary>
        public double RopeY(double k, double x)
        {
            double u = k / Lanes;
            double c = Perspective;
            double yc = (yNear + (yFar * (1 + c) - yNear) * u) / (1 + c * u);
            return LineY(yc, x);
        }

        /// <summary>
        /// Lane k as the reading knows a lane (working pixels): its far rope (top of the image) and its near rope as straight lines
        /// (the ropes are lines through the vanishing point).
        /// </summary>
        public SwimStrokeLane Lane(int k)
        {
            double a1, b1, a2, b2;
            RopeLine(k + 1, out a1, out b1);
            RopeLine(k, out a2, out b2);
            if (a1 * X + b1 > a2 * X + b2)
                return new SwimStrokeLane(a2, b2, a1, b1);
            return new SwimStrokeLane(a1, b1, a2, b2);
        }

        /// <summary>Rope k as the line y = a x + b.</summary>
        private void RopeLine(int k, out double a, out double b)
        {
            double yc = RopeY(k, X);
            double dx = X - Vx;
            a = Math.Abs(dx) < 1e-6 ? 0 : (yc - Vy) / dx;
            b = yc - a * X;
        }

        /// <summary>The y at x of the line through the vanishing point and (X, yc).</summary>
        private double LineY(double yc, double x)
        {
            double dx = X - Vx;
            if (Math.Abs(dx) < 1e-6)
                return yc;
            double s = (yc - Vy) / dx;
            return yc + s * (x - X);
        }

        /// <summary>The lane of the point (0: the lane at the near wall), -1 outside the pool.</summary>
        public int LaneAt(double x, double y)
        {
            double dx = x - Vx;
            if (Math.Abs(dx) < 1e-6)
                return -1;

            // The line through the vanishing point and the point, at the column of the grid.
            double yc = Vy + (y - Vy) * (X - Vx) / dx;
            for (int k = 0; k < Lanes; k++)
            {
                double a = RopeY(k, X), b = RopeY(k + 1, X);
                if (yc <= Math.Max(a, b) && yc >= Math.Min(a, b))
                    return k;
            }

            return -1;
        }

        /// <summary>The lanes a 50 m pool has between its walls (lab: 8 in all 51 views of the national pool; 6 to 10 left free, 7 or
        /// 9 came out on 4 of them, with the far lanes wrong).</summary>
        public static readonly int[] LongCourseLanes = { 8, 10 };

        /// <summary>The lanes of a 25 m pool.</summary>
        public static readonly int[] ShortCourseLanes = { 6, 8, 10 };

        /// <summary>
        /// The pool of the image (24-bit BGR, working size), the grid fitted at the column x (where the swimmer is looked for) with
        /// one of these numbers of lanes (LongCourseLanes when null). Null when the water or one of the walls is not found.
        /// </summary>
        public static SwimPoolGrid Find(SwimStrokeImage image, double x, IList<int> laneCounts = null)
        {
            if (laneCounts == null || laneCounts.Count == 0)
                laneCounts = LongCourseLanes;
            int w = image.Width, h = image.Height;
            SwimPoolGrid g = new SwimPoolGrid(w, h);
            bool[] water;
            float[] evidence;
            Classify(image, out water, out evidence);
            bool[] pool = PoolMask(water, w, h);
            if (pool == null)
                return null;

            // The top and the bottom of the water in every second column.
            List<double> xs = new List<double>(), tops = new List<double>(), bottoms = new List<double>();
            for (int cx = 0; cx < w; cx += 2)
            {
                int first = -1, last = -1, count = 0;
                for (int y = 0; y < h; y++)
                {
                    if (!pool[y * w + cx])
                        continue;
                    if (first < 0)
                        first = y;
                    last = y;
                    count++;
                }

                if (count > 30)
                {
                    xs.Add(cx);
                    tops.Add(first);
                    bottoms.Add(last);
                }
            }

            double fa, fb, fs, na, nb, ns;
            if (!FitLine(xs, tops, out fa, out fb, out fs) || !FitLine(xs, bottoms, out na, out nb, out ns) || Math.Abs(fa - na) < 1e-4)
                return null;

            g.FarA = fa;
            g.FarB = fb;
            g.FarShare = fs;
            g.NearA = na;
            g.NearB = nb;
            g.NearShare = ns;
            g.pool = pool;
            g.Vx = (nb - fb) / (fa - na);
            g.Vy = fa * g.Vx + fb;
            g.X = x;
            g.yFar = fa * x + fb;
            g.yNear = na * x + nb;
            if (g.yNear - g.yFar < 20)
                return null;

            // The rope evidence of the lines through the vanishing point, at each y of the column between the walls.
            bool[] around = Dilate(pool, w, h, 3);
            int y0 = (int)Math.Floor(Math.Min(g.yFar, g.yNear)), y1 = (int)Math.Floor(Math.Max(g.yFar, g.yNear));
            double[] profile = new double[y1 - y0 + 1];
            for (int i = 0; i < profile.Length; i++)
                profile[i] = LineScore(evidence, around, w, h, g, y0 + i);

            double best = double.NegativeInfinity;
            foreach (int n in laneCounts)
            {
                if (n < 2)
                    continue;
                for (int ci = 0; ci <= 150; ci++)
                {
                    double c = ci * 0.02;
                    double ropes = 0, middles = 0;
                    for (int k = 1; k < n; k++)
                    {
                        int i = ProfileIndex(g.yNear, g.yFar, c, (double)k / n, y0, profile.Length);
                        double m = profile[i];
                        if (i > 0)
                            m = Math.Max(m, profile[i - 1]);
                        if (i < profile.Length - 1)
                            m = Math.Max(m, profile[i + 1]);
                        ropes += m;
                    }

                    for (int k = 0; k < n; k++)
                        middles += profile[ProfileIndex(g.yNear, g.yFar, c, (k + 0.5) / n, y0, profile.Length)];

                    ropes /= n - 1;
                    middles /= n;
                    double value = ropes - 0.5 * middles;
                    if (value > best)
                    {
                        best = value;
                        g.Lanes = n;
                        g.Perspective = c;
                        g.Score = ropes;
                        g.Middles = middles;
                    }
                }
            }

            return g;
        }

        /// <summary>
        /// The swimmer along lane k (0: the lane at the near wall): where most moves in the middle part of the lane across (its
        /// ropes and splashes left out), smoothed over half a body; NaN when nothing moves. clear: that motion over the strongest
        /// motion more than two bodies away (lab: the analyst's swimmer within a body in 44 of 50 phone races).
        /// </summary>
        public double SwimmerAlong(int k, float[] motion, double body, out double clear)
        {
            clear = 0;
            if (k < 0 || k >= Lanes || motion == null || motion.Length != width * height)
                return double.NaN;

            double[] smooth = Smooth(Profile(k, motion), Math.Max(3, 0.5 * body));
            int peak = 0;
            for (int x = 1; x < width; x++)
                if (smooth[x] > smooth[peak])
                    peak = x;

            // Nothing that moves (a grey level of difference, on average across the lane): no swimmer.
            if (smooth[peak] < SwimmerMotion)
                return double.NaN;

            double second = 0;
            bool any = false;
            for (int x = 0; x < width; x++)
            {
                if (Math.Abs(x - peak) <= 2 * body)
                    continue;
                any = true;
                second = Math.Max(second, smooth[x]);
            }

            clear = any ? smooth[peak] / Math.Max(1e-6, second) : 9.9;
            if (AlongEstimate == 1)
            {
                // The middle of what moves around the peak (above half of it, within 1.5 bodies).
                double sw = 0, sxw = 0, half = 0.5 * smooth[peak];
                int lo = Math.Max(0, (int)(peak - 1.5 * body)), hi = Math.Min(width - 1, (int)(peak + 1.5 * body));
                for (int x = lo; x <= hi; x++)
                {
                    if (smooth[x] < half)
                        continue;
                    sw += smooth[x];
                    sxw += x * smooth[x];
                }

                if (sw > 0)
                    return sxw / sw;
            }

            return peak;
        }

        /// <summary>The swimmer's place from the profile: 0 the peak, 1 the middle of what moves around it (tests).</summary>
        internal static int AlongEstimate = 0;

        /// <summary>The motion across the inner 60% of lane k (in the water), column by column.</summary>
        private double[] Profile(int k, float[] motion)
        {
            double[] profile = new double[width];
            for (int x = 0; x < width; x++)
            {
                double a = RopeY(k, x), b = RopeY(k + 1, x);
                double lo = Math.Min(a, b), hi = Math.Max(a, b), w = hi - lo;
                lo += 0.2 * w;
                hi -= 0.2 * w;
                int i0 = Math.Max(0, (int)Math.Ceiling(lo)), i1 = Math.Min(height - 1, (int)Math.Floor(hi));
                if (i1 < i0)
                    continue;
                double sum = 0;
                for (int y = i0; y <= i1; y++)
                    sum += pool[y * width + x] && Inside(x, y) ? motion[y * width + x] : 0;
                profile[x] = sum / (i1 - i0 + 1);
            }

            return profile;
        }

        /// <summary>
        /// The head of the swimmer of lane k swimming that way (+1: to the right): the front of what moves around him (where the
        /// motion, smoothed by a quarter body, falls under half of its peak near the swimmer of SwimmerAlong), less the reach of
        /// his hands ahead of the head (HandReach). NaN when there is no swimmer.
        /// </summary>
        public double SwimmerHead(int k, float[] motion, double body, int direction, string style, out double clear)
        {
            double along = SwimmerAlong(k, motion, body, out clear);
            if (double.IsNaN(along) || direction == 0)
                return double.NaN;

            double[] fine = Smooth(Profile(k, motion), Math.Max(2, 0.25 * body));
            int lo = Math.Max(0, (int)Math.Floor(along - body)), hi = Math.Min(width - 1, (int)Math.Ceiling(along + body));
            int p = lo;
            for (int x = lo; x <= hi; x++)
                if (fine[x] > fine[p])
                    p = x;
            int front = p;
            while (front + direction >= 0 && front + direction < width && fine[front + direction] >= 0.5 * fine[p])
                front += direction;
            return front - direction * HandReach(style) * body;
        }

        /// <summary>
        /// How far ahead of the head the hands enter the water (swimmer lengths): the front of the motion is the hands. From the
        /// analysts' clicks on 50 phone races (front of the motion against the head clicked): backstroke 0.2 (the most regular),
        /// breaststroke and freestyle 0.4, butterfly 0.5.
        /// </summary>
        public static double HandReach(string style)
        {
            switch (style)
            {
                case "bk": return 0.2;
                case "fly": return 0.5;
                default: return 0.4;
            }
        }

        /// <summary>The least motion of a swimmer, mean grey difference across his lane (the frames 0.1 s apart).</summary>
        private const double SwimmerMotion = 1.0;

        /// <summary>The middle of lane k at the column x.</summary>
        public double LaneMiddle(int k, double x)
        {
            return 0.5 * (RopeY(k, x) + RopeY(k + 1, x));
        }

        /// <summary>The width of lane k across at the column x.</summary>
        public double LaneWidth(int k, double x)
        {
            return Math.Abs(RopeY(k, x) - RopeY(k + 1, x));
        }

        /// <summary>
        /// The way the swimmers swim the first length, from the motion of the dive (frames a little after the start): the start
        /// end is where it moves, between the walls (the blocks a little above the far wall's line included). +1: the start is at
        /// the left of the water (they swim to the right), -1: at the right, 0: not clear (the motion near the middle of the water).
        /// Lab: right in 72 of 75 phone races, the 3 others within 0.08 of the middle.
        /// </summary>
        public int StartDirection(float[] motion)
        {
            if (motion == null || motion.Length != width * height)
                return 0;

            int x0 = width, x1 = -1;
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (pool[y * width + x])
                    {
                        x0 = Math.Min(x0, x);
                        x1 = Math.Max(x1, x);
                        break;
                    }

            if (x1 - x0 < width / 4)
                return 0;

            double sum = 0, moment = 0;
            for (int x = 0; x < width; x++)
            {
                double top = FarA * x + FarB - 30, bottom = NearA * x + NearB + 10;
                int i0 = Math.Max(0, (int)Math.Ceiling(top)), i1 = Math.Min(height - 1, (int)Math.Floor(bottom));
                for (int y = i0; y <= i1; y++)
                {
                    float m = motion[y * width + x];
                    if (m < 12)
                        continue;
                    sum += m;
                    moment += m * x;
                }
            }

            if (sum <= 0)
                return 0;

            double place = (moment / sum - x0) / (x1 - x0);
            if (Math.Abs(place - 0.5) < 0.08)
                return 0;
            return place < 0.5 ? 1 : -1;
        }

        /// <summary>Between the walls (the pool mask can leak along the deck).</summary>
        private bool Inside(int x, int y)
        {
            return y >= FarA * x + FarB && y <= NearA * x + NearB;
        }

        /// <summary>
        /// The motion between frames a tenth of a second apart (24-bit BGR, working size): the grey difference after the
        /// camera's shift between them is taken out (the shift of least difference within 12 pixels across and 6 up and down),
        /// the least of the 3x3 pixels around (a camera that moved by part of a pixel does not light up every edge of the image:
        /// lab: with it the start end was right in 73 of 75 phone races), the strongest of the consecutive pairs; zero at the
        /// borders the shift uncovers.
        /// </summary>
        public static float[] Motion(IList<SwimStrokeImage> frames)
        {
            if (frames == null || frames.Count < 2)
                return null;

            int w = frames[0].Width, h = frames[0].Height;
            float[][] grey = new float[frames.Count][];
            for (int i = 0; i < frames.Count; i++)
                grey[i] = Blur(Grey(frames[i]), w, h);

            float[] result = new float[w * h];
            for (int i = 0; i + 1 < frames.Count; i++)
            {
                float[] a = grey[i], b = grey[i + 1];
                int dx, dy;
                Shift(a, b, w, h, out dx, out dy);
                int bx = Math.Abs(dx) + 8, by = Math.Abs(dy) + 8;
                for (int y = by; y < h - by; y++)
                {
                    for (int x = bx; x < w - bx; x++)
                    {
                        // b(x, y) ~ a(x - dx, y - dy), give or take a pixel
                        float v = b[y * w + x];
                        float d = float.MaxValue;
                        for (int j = -1; j <= 1; j++)
                        {
                            int row = (y - dy + j) * w + (x - dx);
                            for (int k = -1; k <= 1; k++)
                                d = Math.Min(d, Math.Abs(v - a[row + k]));
                        }

                        if (d > result[y * w + x])
                            result[y * w + x] = d;
                    }
                }
            }

            return result;
        }

        private static float[] Grey(SwimStrokeImage image)
        {
            int n = image.Width * image.Height;
            float[] g = new float[n];
            byte[] d = image.Data;
            for (int i = 0; i < n; i++)
                g[i] = (float)(0.114 * d[3 * i] + 0.587 * d[3 * i + 1] + 0.299 * d[3 * i + 2]);
            return g;
        }

        /// <summary>Gaussian blur, sigma 1 (radius 3), edges replicated.</summary>
        private static float[] Blur(float[] g, int w, int h)
        {
            double[] k = new double[7];
            double s = 0;
            for (int i = -3; i <= 3; i++)
            {
                k[i + 3] = Math.Exp(-0.5 * i * i);
                s += k[i + 3];
            }

            for (int i = 0; i < 7; i++)
                k[i] /= s;

            float[] t = new float[w * h], o = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double v = 0;
                    for (int i = -3; i <= 3; i++)
                        v += k[i + 3] * g[y * w + Math.Max(0, Math.Min(w - 1, x + i))];
                    t[y * w + x] = (float)v;
                }

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double v = 0;
                    for (int i = -3; i <= 3; i++)
                        v += k[i + 3] * t[Math.Max(0, Math.Min(h - 1, y + i)) * w + x];
                    o[y * w + x] = (float)v;
                }

            return o;
        }

        /// <summary>The camera's shift from a to b (b(x, y) ~ a(x - dx, y - dy)): least mean difference on every fourth pixel.</summary>
        private static void Shift(float[] a, float[] b, int w, int h, out int dx, out int dy)
        {
            dx = dy = 0;
            double best = double.MaxValue;
            for (int sy = -6; sy <= 6; sy++)
            {
                for (int sx = -12; sx <= 12; sx++)
                {
                    double sum = 0;
                    int count = 0;
                    for (int y = 16; y < h - 16; y += 4)
                        for (int x = 24; x < w - 24; x += 4)
                        {
                            sum += Math.Abs(b[y * w + x] - a[(y - sy) * w + (x - sx)]);
                            count++;
                        }

                    double mean = sum / Math.Max(1, count);
                    if (mean < best)
                    {
                        best = mean;
                        dx = sx;
                        dy = sy;
                    }
                }
            }
        }

        /// <summary>Gaussian smoothing of a profile (sigma in samples, edges replicated).</summary>
        private static double[] Smooth(double[] p, double sigma)
        {
            int r = (int)Math.Ceiling(3 * sigma);
            double[] k = new double[2 * r + 1];
            double s = 0;
            for (int i = -r; i <= r; i++)
            {
                k[i + r] = Math.Exp(-0.5 * i * i / (sigma * sigma));
                s += k[i + r];
            }

            double[] o = new double[p.Length];
            for (int x = 0; x < p.Length; x++)
            {
                double v = 0;
                for (int i = -r; i <= r; i++)
                    v += k[i + r] * p[Math.Max(0, Math.Min(p.Length - 1, x + i))];
                o[x] = v / s;
            }

            return o;
        }

        private static int ProfileIndex(double yn, double yf, double c, double u, int y0, int count)
        {
            double y = (yn + (yf * (1 + c) - yn) * u) / (1 + c * u);
            int i = (int)Math.Round(y - y0);
            return Math.Max(0, Math.Min(count - 1, i));
        }

        /// <summary>The mean evidence along the line through the vanishing point and (X, yc), on the pool, every third column.</summary>
        private static double LineScore(float[] evidence, bool[] pool, int w, int h, SwimPoolGrid g, double yc)
        {
            double dx = g.X - g.Vx;
            double s = Math.Abs(dx) < 1e-6 ? 0 : (yc - g.Vy) / dx;
            double sum = 0;
            int count = 0;
            for (int x = 0; x < w; x += 3)
            {
                int y = (int)Math.Round(yc + s * (x - g.X), MidpointRounding.ToEven);
                if (y < 1 || y >= h - 1 || !pool[y * w + x])
                    continue;
                float e = Math.Max(evidence[y * w + x], Math.Max(evidence[(y - 1) * w + x], evidence[(y + 1) * w + x]));
                sum += e;
                count++;
            }

            return count >= 30 ? sum / count : 0;
        }

        /// <summary>
        /// Per pixel: water (the hue of the pool, saturated enough, not dark) and the evidence of a rope (rope colours: yellow, red,
        /// green, white floats; a thin line brighter or darker than 2-4 pixels above and below).
        /// </summary>
        private static void Classify(SwimStrokeImage image, out bool[] water, out float[] evidence)
        {
            int w = image.Width, h = image.Height;
            byte[] d = image.Data;
            water = new bool[w * h];
            evidence = new float[w * h];
            float[] value = new float[w * h];
            bool[] colour = new bool[w * h];
            for (int i = 0; i < w * h; i++)
            {
                double b = d[3 * i], gr = d[3 * i + 1], r = d[3 * i + 2];
                double max = Math.Max(r, Math.Max(gr, b)), min = Math.Min(r, Math.Min(gr, b));
                double delta = max - min + 1e-6;
                double hue;
                if (max == r)
                    hue = (((gr - b) / delta) % 6 + 6) % 6 * 60;
                else if (max == gr)
                    hue = ((b - r) / delta + 2) * 60;
                else
                    hue = ((r - gr) / delta + 4) * 60;
                double sat = delta / (max + 1e-6);
                value[i] = (float)max;
                water[i] = hue > 165 && hue < 215 && sat > 0.25 && max > 60;
                bool yellow = hue >= 35 && hue <= 75 && sat > 0.35 && max > 110;
                bool red = (hue <= 22 || hue >= 330) && sat > 0.35 && max > 90;
                bool green = hue > 75 && hue < 165 && sat > 0.25 && max > 50;
                bool white = sat < 0.22 && max > 165;
                colour[i] = yellow || red || green || white;
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    double ridge = 0;
                    for (int dd = 2; dd <= 4; dd++)
                    {
                        if (y - dd < 0 || y + dd >= h)
                            continue;
                        double up = value[i - dd * w], down = value[i + dd * w];
                        double rr = Math.Abs(value[i] - 0.5 * (up + down)) - 0.5 * Math.Abs(up - down);
                        ridge = Math.Max(ridge, Math.Min(1, Math.Max(0, rr / 40.0)));
                    }

                    evidence[i] = (float)Math.Min(1, (colour[i] ? 0.7 : 0) + 0.5 * ridge);
                }
            }
        }

        /// <summary>The water closed across the ropes (15 pixels up and down), opened (5x5), its largest part with the holes filled.</summary>
        private static bool[] PoolMask(bool[] water, int w, int h)
        {
            bool[] closed = ErodeVertical(DilateVertical(water, w, h, 7), w, h, 7);
            bool[] opened = DilateSquare(ErodeSquare(closed, w, h, 2), w, h, 2);

            // The largest connected part (4-connected).
            int[] label = new int[w * h];
            int bestLabel = 0, bestSize = 0, next = 0;
            Stack<int> stack = new Stack<int>();
            for (int i = 0; i < w * h; i++)
            {
                if (!opened[i] || label[i] != 0)
                    continue;
                next++;
                int size = 0;
                label[i] = next;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int p = stack.Pop();
                    size++;
                    int px = p % w, py = p / w;
                    if (px > 0 && opened[p - 1] && label[p - 1] == 0) { label[p - 1] = next; stack.Push(p - 1); }
                    if (px < w - 1 && opened[p + 1] && label[p + 1] == 0) { label[p + 1] = next; stack.Push(p + 1); }
                    if (py > 0 && opened[p - w] && label[p - w] == 0) { label[p - w] = next; stack.Push(p - w); }
                    if (py < h - 1 && opened[p + w] && label[p + w] == 0) { label[p + w] = next; stack.Push(p + w); }
                }

                if (size > bestSize)
                {
                    bestSize = size;
                    bestLabel = next;
                }
            }

            if (bestSize < w * h / 50)
                return null;

            // Holes filled: what the outside (background reached from the border) does not reach.
            bool[] outside = new bool[w * h];
            for (int x = 0; x < w; x++)
            {
                Seed(outside, label, bestLabel, x, stack);
                Seed(outside, label, bestLabel, (h - 1) * w + x, stack);
            }

            for (int y = 0; y < h; y++)
            {
                Seed(outside, label, bestLabel, y * w, stack);
                Seed(outside, label, bestLabel, y * w + w - 1, stack);
            }

            while (stack.Count > 0)
            {
                int p = stack.Pop();
                int px = p % w, py = p / w;
                if (px > 0) Seed(outside, label, bestLabel, p - 1, stack);
                if (px < w - 1) Seed(outside, label, bestLabel, p + 1, stack);
                if (py > 0) Seed(outside, label, bestLabel, p - w, stack);
                if (py < h - 1) Seed(outside, label, bestLabel, p + w, stack);
            }

            bool[] pool = new bool[w * h];
            for (int i = 0; i < w * h; i++)
                pool[i] = !outside[i];
            return pool;
        }

        private static void Seed(bool[] outside, int[] label, int pool, int p, Stack<int> stack)
        {
            if (outside[p] || label[p] == pool)
                return;
            outside[p] = true;
            stack.Push(p);
        }

        private static bool[] DilateVertical(bool[] m, int w, int h, int r)
        {
            bool[] o = new bool[w * h];
            for (int x = 0; x < w; x++)
            {
                int last = -1000000;
                for (int y = 0; y < h; y++)
                {
                    if (m[y * w + x])
                        last = y;
                    if (y - last <= r)
                        o[y * w + x] = true;
                }

                last = 1000000;
                for (int y = h - 1; y >= 0; y--)
                {
                    if (m[y * w + x])
                        last = y;
                    if (last - y <= r)
                        o[y * w + x] = true;
                }
            }

            return o;
        }

        private static bool[] ErodeVertical(bool[] m, int w, int h, int r)
        {
            // Outside the image counts as not water (as the closing of the lab).
            bool[] o = new bool[w * h];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    bool all = true;
                    for (int dy = -r; dy <= r && all; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h || !m[yy * w + x])
                            all = false;
                    }

                    o[y * w + x] = all;
                }
            }

            return o;
        }

        private static bool[] ErodeSquare(bool[] m, int w, int h, int r)
        {
            bool[] row = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool all = true;
                    for (int dx = -r; dx <= r && all; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= w || !m[y * w + xx])
                            all = false;
                    }

                    row[y * w + x] = all;
                }

            bool[] o = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool all = true;
                    for (int dy = -r; dy <= r && all; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h || !row[yy * w + x])
                            all = false;
                    }

                    o[y * w + x] = all;
                }

            return o;
        }

        private static bool[] DilateSquare(bool[] m, int w, int h, int r)
        {
            bool[] row = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool any = false;
                    for (int dx = -r; dx <= r && !any; dx++)
                    {
                        int xx = x + dx;
                        if (xx >= 0 && xx < w && m[y * w + xx])
                            any = true;
                    }

                    row[y * w + x] = any;
                }

            bool[] o = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool any = false;
                    for (int dy = -r; dy <= r && !any; dy++)
                    {
                        int yy = y + dy;
                        if (yy >= 0 && yy < h && row[yy * w + x])
                            any = true;
                    }

                    o[y * w + x] = any;
                }

            return o;
        }

        /// <summary>Dilation by a diamond of radius r (r steps of the 4 neighbours).</summary>
        private static bool[] Dilate(bool[] m, int w, int h, int r)
        {
            bool[] cur = (bool[])m.Clone();
            for (int k = 0; k < r; k++)
            {
                bool[] o = (bool[])cur.Clone();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (cur[i])
                            continue;
                        if ((x > 0 && cur[i - 1]) || (x < w - 1 && cur[i + 1]) || (y > 0 && cur[i - w]) || (y < h - 1 && cur[i + w]))
                            o[i] = true;
                    }

                cur = o;
            }

            return cur;
        }

        /// <summary>
        /// The line of most points within 2.5 pixels (pairs of points at least 40 apart, a fixed sequence of pairs), refitted on
        /// them; share: the part of the points on it.
        /// </summary>
        private static bool FitLine(List<double> xs, List<double> ys, out double a, out double b, out double share)
        {
            a = b = share = 0;
            int n = xs.Count;
            if (n < 10)
                return false;

            Random random = new Random(1);
            int bestCount = -1;
            double bestA = 0, bestB = 0;
            for (int it = 0; it < 600; it++)
            {
                int i = random.Next(n), j = random.Next(n);
                if (Math.Abs(xs[j] - xs[i]) < 40)
                    continue;
                double s = (ys[j] - ys[i]) / (xs[j] - xs[i]);
                double c = ys[i] - s * xs[i];
                int count = 0;
                for (int k = 0; k < n; k++)
                    if (Math.Abs(ys[k] - (s * xs[k] + c)) < 2.5)
                        count++;
                if (count > bestCount)
                {
                    bestCount = count;
                    bestA = s;
                    bestB = c;
                }
            }

            if (bestCount < 2)
                return false;

            // Least squares on the points of the line.
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            int m = 0;
            for (int k = 0; k < n; k++)
            {
                if (Math.Abs(ys[k] - (bestA * xs[k] + bestB)) >= 2.5)
                    continue;
                sx += xs[k];
                sy += ys[k];
                sxx += xs[k] * xs[k];
                sxy += xs[k] * ys[k];
                m++;
            }

            double den = m * sxx - sx * sx;
            if (m < 2 || Math.Abs(den) < 1e-9)
                return false;
            a = (m * sxy - sx * sy) / den;
            b = (sy - a * sx) / m;
            share = (double)m / n;
            return true;
        }
    }
}
