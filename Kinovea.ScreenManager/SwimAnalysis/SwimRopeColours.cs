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
    /// (2.39.1) The lanes of a race video from the colours of its ropes. World Aquatics asks for three yellow ropes around lanes 4 and 5
    /// (green for lanes 1 and 8, blue for the others) and for lane 0 or 1 on the right hand when facing the course from the starting end
    /// (Facilities Rules, recommended; 50 m events may start from the other end). The yellow ropes of a working image are found as long
    /// straight lines of yellow pixels; three in a row, a lane apart, are the ropes of lanes 4 and 5, and a lane is told by its place
    /// against them: −1 the lane just above the middle yellow rope, 0 the lane just below it, −2 and 1 the next ones, and so on. The
    /// other ropes follow from the three by the perspective of the camera (Rope): the lanes narrower towards the far side, as on video.
    /// The swimmer the camera follows (the middle of the image) is checked against the lane of the race with it: the first length of a
    /// race of the analyst was read with the swimmer of the next lane, in the middle of the image.
    /// Lab (the video of that package, a frame every 0.5 s, 960 px wide): the three ropes in 93 of 103 frames (not at the turn, where the
    /// wall end is in view and the ropes run out of it); the swimmer followed in the first length one lane off the second one, as seen
    /// on the video.
    /// </summary>
    public static class SwimRopeColours
    {
        /// <summary>A rope: y = A x + B in working pixels; Cover: the share of the width where it was seen (0..1).</summary>
        public struct Line
        {
            public double A, B, Cover;

            public double Y(double x)
            {
                return A * x + B;
            }
        }

        // A yellow pixel: hue 30°–76°, saturation from 0.35, value from 120, more green than blue (lab: the yellow ropes, not the water,
        // the blue or the green ropes, the white of the splashes).
        private const double HueLow = 30, HueHigh = 76, SaturationMin = 0.35;
        private const int ValueMin = 120;

        // The lines: up to 5000 yellow points (every k-th), 400 tries per line, at most 7 lines, a slope up to 0.6, two points of a try at
        // least 15% of the width apart, a line seen along at least 40% of the width (in 64 columns).
        private const int MaxPoints = 5000, Tries = 400, MaxLines = 7, Columns = 64;
        private const double MaxSlope = 0.6, MinSpread = 0.15, MinCover = 0.40;

        // The rows looked at: not the top 6% (the stands, the banners) nor the bottom 3%.
        private const double TopShare = 0.06, BottomShare = 0.97;

        /// <summary>A pixel of a rope of lanes 4 and 5.</summary>
        public static bool IsYellow(byte b, byte g, byte r)
        {
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            int d = max - min;
            if (max < ValueMin || d <= 0 || b >= g)
                return false;
            if ((double)d / max < SaturationMin)
                return false;
            double hue;
            if (max == r)
            {
                hue = 60.0 * (g - b) / d;
                if (hue < 0)
                    hue += 360;
            }
            else if (max == g)
            {
                hue = 60.0 * (2 + (double)(b - r) / d);
            }
            else
            {
                return false;
            }

            return hue >= HueLow && hue <= HueHigh;
        }

        /// <summary>The yellow ropes of a working image (24-bit BGR rows): long straight lines of yellow pixels, the best seen first.</summary>
        public static List<Line> Yellow(SwimStrokeImage image)
        {
            List<Line> lines = new List<Line>();
            if (image == null || image.Data == null || image.Width < 64 || image.Height < 36)
                return lines;

            int w = image.Width, h = image.Height;
            int top = (int)(TopShare * h), bottom = (int)(BottomShare * h);
            List<int> xs = new List<int>(), ys = new List<int>();
            byte[] data = image.Data;
            for (int y = top; y < bottom; y++)
            {
                int row = y * w * 3;
                for (int x = 0; x < w; x++)
                {
                    int i = row + 3 * x;
                    if (IsYellow(data[i], data[i + 1], data[i + 2]))
                    {
                        xs.Add(x);
                        ys.Add(y);
                    }
                }
            }

            if (xs.Count < 50)
                return lines;

            int step = Math.Max(1, xs.Count / MaxPoints);
            List<double> px = new List<double>(), py = new List<double>();
            for (int i = 0; i < xs.Count; i += step)
            {
                px.Add(xs[i]);
                py.Add(ys[i]);
            }

            int n = px.Count;
            bool[] alive = Enumerable.Repeat(true, n).ToArray();
            double tolerance = Math.Max(1.5, w / 384.0);
            double column = w / (double)Columns;
            uint seed = 12345;
            Func<int, int> next = count =>
            {
                seed = (1103515245u * seed + 12345u) & 0x7fffffffu;
                return (int)(seed % (uint)count);
            };

            for (int found = 0; found < MaxLines; found++)
            {
                List<int> idx = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    if (alive[i])
                        idx.Add(i);
                }

                if (idx.Count < 40)
                    break;

                int bestCover = -1;
                double bestA = 0, bestB = 0;
                bool[] seen = new bool[Columns + 1];
                for (int t = 0; t < Tries; t++)
                {
                    int i = idx[next(idx.Count)], j = idx[next(idx.Count)];
                    double dx = px[j] - px[i];
                    if (Math.Abs(dx) < MinSpread * w)
                        continue;
                    double a = (py[j] - py[i]) / dx;
                    if (Math.Abs(a) > MaxSlope)
                        continue;
                    double b = py[i] - a * px[i];
                    Array.Clear(seen, 0, seen.Length);
                    int cover = 0;
                    foreach (int k in idx)
                    {
                        if (Math.Abs(py[k] - (a * px[k] + b)) > tolerance)
                            continue;
                        int c = Math.Min(Columns, (int)(px[k] / column));
                        if (!seen[c])
                        {
                            seen[c] = true;
                            cover++;
                        }
                    }

                    if (cover > bestCover)
                    {
                        bestCover = cover;
                        bestA = a;
                        bestB = b;
                    }
                }

                if (bestCover < MinCover * Columns)
                    break;

                // The line again through its points (least squares), then its points and their neighbours out.
                double sx = 0, sy = 0, sxx = 0, sxy = 0;
                int m = 0;
                foreach (int k in idx)
                {
                    if (Math.Abs(py[k] - (bestA * px[k] + bestB)) > tolerance)
                        continue;
                    sx += px[k];
                    sy += py[k];
                    sxx += px[k] * px[k];
                    sxy += px[k] * py[k];
                    m++;
                }

                double den = m * sxx - sx * sx;
                if (m >= 2 && Math.Abs(den) > 1e-9)
                {
                    bestA = (m * sxy - sx * sy) / den;
                    bestB = (sy - bestA * sx) / m;
                }

                lines.Add(new Line { A = bestA, B = bestB, Cover = bestCover / (double)Columns });
                foreach (int k in idx)
                {
                    if (Math.Abs(py[k] - (bestA * px[k] + bestB)) <= 3 * tolerance)
                        alive[k] = false;
                }
            }

            return lines;
        }

        /// <summary>
        /// The three yellow ropes of lanes 4 and 5 at the column x (their y, top to bottom): three yellow lines in a row whose gaps are
        /// alike (the nearer lane up to 1.6 times the farther) and a lane wide (laneWidth: the lane of the swimmer there, 0 if not
        /// known), and no other yellow line a lane beyond them (more yellow ropes: not the scheme of World Aquatics). Null if none.
        /// </summary>
        public static double[] Triple(IList<Line> lines, double x, double laneWidth)
        {
            if (lines == null || lines.Count < 3)
                return null;

            List<double> ys = lines.Select(l => l.Y(x)).OrderBy(y => y).ToList();
            double[] best = null;
            double bestScore = double.MaxValue;
            for (int i = 0; i + 2 < ys.Count; i++)
            {
                double y0 = ys[i], y1 = ys[i + 1], y2 = ys[i + 2];
                double g1 = y1 - y0, g2 = y2 - y1;
                if (g1 <= 3 || g2 <= 3)
                    continue;
                double ratio = g2 / g1;
                if (ratio < 0.7 || ratio > 1.6)
                    continue;
                double mean = 0.5 * (g1 + g2);
                if (laneWidth > 0 && (mean < 0.55 * laneWidth || mean > 1.8 * laneWidth))
                    continue;

                // Exactly three in a row: no yellow rope a lane before the first or after the last.
                bool more = ys.Any(y => Math.Abs(y - (y0 - g1)) <= 0.35 * g1 || Math.Abs(y - (y2 + g2)) <= 0.35 * g2);
                if (more)
                    continue;

                double score = Math.Abs(Math.Log(ratio)) + (laneWidth > 0 ? Math.Abs(Math.Log(mean / laneWidth)) : 0);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = new[] { y0, y1, y2 };
                }
            }

            return best;
        }

        /// <summary>
        /// The perspective of the pool along the column of the triple: rope d (0, 1, 2 the yellow ropes, −1, −2 … the farther ones above
        /// them, 3, 4 … the nearer ones below) at y = (a d + b) / (c d + 1). Ropes a lane apart on flat water are equal steps along a line,
        /// and a camera sees equal steps along a line as such a function of the step, fixed by three of them: the three yellow ropes.
        /// </summary>
        private static void Perspective(double[] triple, out double a, out double b, out double c)
        {
            double g1 = triple[1] - triple[0], g2 = triple[2] - triple[1];
            c = (g1 - g2) / (2 * g2);
            b = triple[0];
            a = g1 + triple[1] * c;
        }

        /// <summary>
        /// The y of rope d at the column of the triple (Perspective); NaN beyond the horizon of the water. Near the yellow ropes to a
        /// pixel or so; three or four lanes away within a quarter of a lane or so (the perspective from three ropes found to a pixel:
        /// CI 26.11), which Place allows for (15% of a lane at each rope) and LaneByColour checks (the lane found on the ropes of its place).
        /// </summary>
        public static double Rope(double[] triple, double d)
        {
            if (triple == null || triple.Length != 3 || triple[2] <= triple[1] || triple[1] <= triple[0])
                return double.NaN;

            double a, b, c;
            Perspective(triple, out a, out b, out c);
            double den = c * d + 1;
            return den > 1e-6 ? (a * d + b) / den : double.NaN;
        }

        /// <summary>The rope at y, as a fraction of its lane (Rope the other way); NaN above the horizon of the water.</summary>
        private static double RopeAt(double[] triple, double y)
        {
            double a, b, c;
            Perspective(triple, out a, out b, out c);
            double den = a - c * y;
            return den > 1e-6 ? (y - b) / den : double.NaN;
        }

        /// <summary>
        /// The place of a lane, its middle at y, against the three yellow ropes: −1 the lane just above the middle rope, 0 just below it,
        /// −2, −3 the next ones above, 1, 2 below (lanes 0 to 9: −5 to 4), the other ropes by the perspective (Rope). Null too near a rope
        /// to say (within 15% of its lane), or off the lanes.
        /// </summary>
        public static int? Place(double[] triple, double y)
        {
            if (triple == null || triple.Length != 3 || triple[2] <= triple[1] || triple[1] <= triple[0])
                return null;

            double d = RopeAt(triple, y);
            if (double.IsNaN(d) || double.IsInfinity(d))
                return null;

            const double edge = 0.15;
            double f = d - Math.Floor(d);
            if (f < edge || f > 1 - edge)
                return null;

            int place = (int)Math.Floor(d) - 1;
            return place < -5 || place > 4 ? (int?)null : place;
        }

        /// <summary>The middle of the lane at a place (as Place counts them) at the column of the triple: between its two ropes; NaN off the water.</summary>
        public static double Middle(double[] triple, int place)
        {
            return 0.5 * (Rope(triple, place + 1) + Rope(triple, place + 2));
        }

        /// <summary>The lane number at a place: lane 1 at the far side of the pool (the top of the image), or at the near side.</summary>
        public static int LaneAt(int place, bool oneFar)
        {
            return oneFar ? place + 5 : 4 - place;
        }

        /// <summary>The place of a lane number (LaneAt the other way).</summary>
        public static int PlaceOf(int lane, bool oneFar)
        {
            return oneFar ? lane - 5 : 4 - lane;
        }
    }
}
