#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Image of the stroke detector at its working resolution: 24-bit BGR, rows of Width * 3 bytes, no padding.
    /// </summary>
    public sealed class SwimStrokeImage
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Data;

        public SwimStrokeImage(int width, int height, byte[] data)
        {
            Width = width;
            Height = height;
            Data = data;
        }

        /// <summary>Bilinear sample of the three channels, edges replicated.</summary>
        public void Sample(double x, double y, out double b, out double g, out double r)
        {
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x > Width - 1) x = Width - 1;
            if (y > Height - 1) y = Height - 1;
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            int x1 = x0 + 1 < Width ? x0 + 1 : Width - 1;
            int y1 = y0 + 1 < Height ? y0 + 1 : Height - 1;
            double fx = x - x0;
            double fy = y - y0;
            int i00 = (y0 * Width + x0) * 3, i01 = (y0 * Width + x1) * 3;
            int i10 = (y1 * Width + x0) * 3, i11 = (y1 * Width + x1) * 3;
            double w00 = (1 - fx) * (1 - fy), w01 = fx * (1 - fy), w10 = (1 - fx) * fy, w11 = fx * fy;
            b = Data[i00] * w00 + Data[i01] * w01 + Data[i10] * w10 + Data[i11] * w11;
            g = Data[i00 + 1] * w00 + Data[i01 + 1] * w01 + Data[i10 + 1] * w10 + Data[i11 + 1] * w11;
            r = Data[i00 + 2] * w00 + Data[i01 + 2] * w01 + Data[i10 + 2] * w10 + Data[i11 + 2] * w11;
        }
    }

    /// <summary>
    /// Lane band of one frame: two lines y = A x + B (working coordinates), the far one first.
    /// </summary>
    public struct SwimStrokeLane
    {
        public double A1, B1, A2, B2;

        public SwimStrokeLane(double a1, double b1, double a2, double b2)
        {
            A1 = a1; B1 = b1; A2 = a2; B2 = b2;
        }

        public double Y1(double x) { return A1 * x + B1; }
        public double Y2(double x) { return A2 * x + B2; }
        public double Width(double x) { return Y2(x) - Y1(x); }
    }

    public sealed class SwimStrokeResult
    {
        /// <summary>Frames (index from the first frame given to the detector) of the strokes, in order.</summary>
        public List<int> Strokes = new List<int>();

        /// <summary>Stroke cycle (s) and time between two stroke marks (s).</summary>
        public double Cycle;
        public double Period;

        /// <summary>True when the lane ropes were found, false for a band along the swimmer.</summary>
        public bool Ropes;

        /// <summary>Lane and position of the swimmer along the band (working x) for each frame.</summary>
        public List<SwimStrokeLane> Lanes = new List<SwimStrokeLane>();
        public int[] Path;

        /// <summary>Stroke signal (onset strength) for each frame.</summary>
        public double[] Onset;
    }

    /// <summary>
    /// Automatic stroke detection on a video where the camera follows the swimmer (stands, side above or under the water).
    ///
    /// The analyst clicks the head and the feet of the swimmer on the first frame. Then, frame by frame:
    /// 1. lane: on the first frame the lane ropes around the swimmer (projection of the image along the dominant
    ///    direction, regularly spaced chain of rope candidates, pair of ropes containing the swimmer). Without ropes
    ///    (side views) a band along the swimmer's axis.
    /// 2. the lane is moved with the camera motion and each rope is refined on the new frame; a rope that leaves the
    ///    image, is hidden or loses its texture follows the other one with the place it had relative to it.
    /// 3. band difference: |band(t) − band(t−2) moved by the camera|, averaged across the band, per column.
    /// At each count (Finish), with the frames given so far:
    /// 4. position of the swimmer along the band: Viterbi on the smoothed band difference, with the camera motion.
    /// 5. stroke signal: band difference ahead of the swimmer; period by autocorrelation; stroke times by dynamic
    ///    programming (beat tracking: one stroke per period, on the peaks of the signal).
    ///
    /// The camera motion (3×3 homography from the previous frame) is computed outside (OpenCV).
    /// Reference implementation and evaluation: Python prototype of the same algorithm.
    /// </summary>
    public sealed class SwimStrokeDetector
    {
        private const int BandRows = 16;
        private const double BandLo = 0.15;
        private const double BandHi = 0.85;
        private const int FrameGap = 2;
        private const double BandBlur = 2.0;
        private const int RopeR = 8;
        private const int RopeMargin = 18;
        private const double RopeSigma = 6.0;
        private const int RopeStep = 4;

        private readonly int width;
        private readonly int height;
        private readonly double headX, headY, feetX, feetY;
        private readonly string style;
        private readonly double fps;
        private readonly double length;
        private readonly int direction;

        private bool initialized;
        private bool ropes;
        private SwimStrokeLane lane;

        // Rope tracking: usual rope-ness of each rope, and the second rope relative to the first one when both were seen
        // (slope difference, vertical offset at the middle of the image).
        private double ropeStrength1;
        private double ropeStrength2;
        private double relativeSlope;
        private double relativeOffset;
        private readonly List<SwimStrokeImage> previous = new List<SwimStrokeImage>();
        private readonly List<double[]> previousMotion = new List<double[]>();
        private readonly List<double[]> differences = new List<double[]>();
        private readonly List<double[]> shifts = new List<double[]>();
        private readonly List<SwimStrokeLane> lanes = new List<SwimStrokeLane>();

        /// <param name="style">Style code of the panel: fs, bk, br, fly.</param>
        public SwimStrokeDetector(int width, int height, double headX, double headY, double feetX, double feetY, string style, double fps)
        {
            this.width = width;
            this.height = height;
            this.headX = headX;
            this.headY = headY;
            this.feetX = feetX;
            this.feetY = feetY;
            this.style = style;
            this.fps = fps > 0 ? fps : 25;
            length = Math.Abs(headX - feetX);
            if (length < 1)
                length = 1;
            direction = headX > feetX ? 1 : -1;
        }

        public int Count
        {
            get { return differences.Count; }
        }

        public bool FoundRopes
        {
            get { return ropes; }
        }

        public SwimStrokeLane CurrentLane
        {
            get { return lane; }
        }

        #region Style
        /// <summary>Plausible stroke cycle (s) of a style.</summary>
        public static void CycleRange(string style, out double lo, out double hi)
        {
            switch (style)
            {
                case "fly": lo = 0.6; hi = 1.6; break;
                case "br": lo = 0.6; hi = 1.8; break;
                case "bk": lo = 0.7; hi = 2.0; break;
                default: lo = 0.7; hi = 1.9; break;
            }
        }

        /// <summary>Stroke marks per cycle: every arm in free and back, every cycle in fly and breast.</summary>
        public static int Presses(string style)
        {
            return (style == "fs" || style == "bk") ? 2 : 1;
        }

        /// <summary>
        /// No stroke is searched in this time before the wall (s): in free and back the turn or the finish make splashes
        /// that look like strokes.
        /// </summary>
        public static double WallMargin(string style)
        {
            return (style == "fs" || style == "bk") ? 0.6 : 0.1;
        }
        #endregion

        /// <summary>
        /// Add the next frame. motion: homography (9 values, row-major) from the previous frame to this one, null for the first frame.
        /// </summary>
        public void Add(SwimStrokeImage image, double[] motion)
        {
            double[] shift = new double[width];
            if (!initialized)
            {
                initialized = true;
                SwimStrokeLane found;
                ropes = FindLane(image, headX, headY, feetX, feetY, out found);
                if (ropes)
                {
                    double a, b;
                    lane = found;
                    if (RefineLine(image, found.A1, found.B1, out a, out b)) { lane.A1 = a; lane.B1 = b; }
                    if (RefineLine(image, found.A2, found.B2, out a, out b)) { lane.A2 = a; lane.B2 = b; }

                    double inside;
                    ropeStrength1 = RopeStrength(image, lane.A1, lane.B1, out inside);
                    ropeStrength2 = RopeStrength(image, lane.A2, lane.B2, out inside);
                    KeepRopeGeometry(lane);
                }
                else
                {
                    lane = AxisBand(headX, headY, feetX, feetY);
                }
            }
            else
            {
                double[] H = motion ?? Identity();
                SwimStrokeLane before = lane;
                SwimStrokeLane predicted;
                MapLine(H, before.A1, before.B1, 0, width, out predicted.A1, out predicted.B1);
                MapLine(H, before.A2, before.B2, 0, width, out predicted.A2, out predicted.B2);
                SwimStrokeLane next = predicted;
                if (ropes)
                    next = TrackRopes(image, predicted);

                // Camera displacement along the band (centre line of the previous band -> this frame).
                for (int x = 0; x < width; x++)
                {
                    double yc = 0.5 * (before.Y1(x) + before.Y2(x));
                    double X, Y;
                    MapPoint(H, x, yc, out X, out Y);
                    shift[x] = X - x;
                }

                lane = next;
            }

            lanes.Add(lane);
            shifts.Add(shift);

            double[] d = new double[width];
            if (previous.Count == FrameGap)
            {
                double[] M = Identity();
                for (int k = 1; k < previousMotion.Count; k++)
                    M = Multiply(previousMotion[k], M);
                M = Multiply(motion ?? Identity(), M);
                double[] Minv = Invert(M);
                BandDifference(image, previous[0], Minv, d);
            }

            differences.Add(d);
            previous.Add(image);
            previousMotion.Add(motion ?? Identity());
            if (previous.Count > FrameGap)
            {
                previous.RemoveAt(0);
                previousMotion.RemoveAt(0);
            }
        }

        private void BandDifference(SwimStrokeImage current, SwimStrokeImage before, double[] Minv, double[] d)
        {
            int n = BandRows * width;
            double[] cur = new double[n * 3];
            double[] prv = new double[n * 3];
            bool[] inside = new bool[n];
            for (int row = 0; row < BandRows; row++)
            {
                double f = BandLo + (BandHi - BandLo) * row / (BandRows - 1);
                for (int x = 0; x < width; x++)
                {
                    double y = lane.Y1(x) * (1 - f) + lane.Y2(x) * f;
                    double px, py;
                    MapPoint(Minv, x, y, out px, out py);
                    int i = row * width + x;
                    double b, g, r;
                    current.Sample(x, y, out b, out g, out r);
                    cur[i * 3] = b; cur[i * 3 + 1] = g; cur[i * 3 + 2] = r;
                    before.Sample(px, py, out b, out g, out r);
                    prv[i * 3] = b; prv[i * 3 + 1] = g; prv[i * 3 + 2] = r;
                    inside[i] = y >= 0 && y <= height - 1 && py >= 0 && py <= height - 1 && px >= 0 && px <= width - 1;
                }
            }

            Parallel.Invoke(() => BlurBand(cur, width), () => BlurBand(prv, width));

            for (int x = 0; x < width; x++)
            {
                double sum = 0;
                int count = 0;
                for (int row = 0; row < BandRows; row++)
                {
                    int i = row * width + x;
                    if (!inside[i])
                        continue;
                    double diff = (Math.Abs(cur[i * 3] - prv[i * 3]) + Math.Abs(cur[i * 3 + 1] - prv[i * 3 + 1]) + Math.Abs(cur[i * 3 + 2] - prv[i * 3 + 2])) / 3.0;
                    sum += diff;
                    count++;
                }

                d[x] = count > 0 ? sum / count : 0;
            }
        }

        /// <summary>Gaussian blur of a band image (BandRows × w × 3) along the rows and along the columns.</summary>
        private static void BlurBand(double[] img, int w)
        {
            int r;
            double[] k = GaussKernel(BandBlur, out r);
            int taps = 2 * r + 1;
            int line = w * 3;
            double[] tmp = new double[img.Length];

            // Across the band (rows), edges replicated. Same order of the sums as the reference.
            int[] rowOffsets = new int[taps];
            for (int row = 0; row < BandRows; row++)
            {
                for (int j = 0; j < taps; j++)
                    rowOffsets[j] = Clamp(row + j - r, 0, BandRows - 1) * line;

                int dst = row * line;
                for (int i = 0; i < line; i++)
                {
                    double s = 0;
                    for (int j = 0; j < taps; j++)
                        s += k[j] * img[rowOffsets[j] + i];
                    tmp[dst + i] = s;
                }
            }

            // Along the band (columns), on a line padded with the edge values.
            double[] padded = new double[(w + 2 * r) * 3];
            for (int row = 0; row < BandRows; row++)
            {
                int src = row * line;
                for (int x = -r; x < w + r; x++)
                {
                    int xx = Clamp(x, 0, w - 1);
                    int p = (x + r) * 3;
                    padded[p] = tmp[src + xx * 3];
                    padded[p + 1] = tmp[src + xx * 3 + 1];
                    padded[p + 2] = tmp[src + xx * 3 + 2];
                }

                for (int x = 0; x < w; x++)
                {
                    int dst = src + x * 3;
                    for (int c = 0; c < 3; c++)
                    {
                        double s = 0;
                        int p = x * 3 + c;
                        for (int j = 0; j < taps; j++)
                            s += k[j] * padded[p + j * 3];
                        img[dst + c] = s;
                    }
                }
            }
        }

        #region Finish
        /// <summary>
        /// Strokes between the frames first and last (indices of the frames given to the detector).
        /// </summary>
        public SwimStrokeResult Finish(int first, int last)
        {
            SwimStrokeResult result = new SwimStrokeResult();
            result.Ropes = ropes;
            result.Lanes.AddRange(lanes);

            int T = differences.Count;
            if (T < FrameGap + 3)
                return result;

            int W = width;
            double[] D = new double[T * W];
            for (int t = 0; t < T; t++)
                Array.Copy(differences[t < FrameGap ? FrameGap : t], 0, D, t * W, W);

            double[] S = new double[T * W];
            for (int t = 0; t < T; t++)
                Array.Copy(shifts[t], 0, S, t * W, W);

            double x0 = 0.5 * (headX + feetX);
            int[] path = Locate(D, S, T, W, x0, Math.Max(12.0, 0.3 * length));
            result.Path = path;

            int ahead = (int)Math.Round(1.6 * length);
            int behind = (int)Math.Round(0.2 * length);
            double[] s = StrokeSignal(D, T, W, path, direction, ahead, behind);
            double[] o = OnsetStrength(s, fps);
            result.Onset = o;

            first = Math.Max(0, first);
            last = Math.Min(T - 1, last);
            if (last - first < 3)
                return result;

            double lo, hi;
            CycleRange(style, out lo, out hi);
            double[] segment = new double[last - first + 1];
            Array.Copy(o, first, segment, 0, segment.Length);
            double cycle = AutocorrelationPeriod(segment, fps, lo, hi);
            if (double.IsNaN(cycle))
                cycle = 0.5 * (lo + hi);

            double period = cycle / Presses(style);
            double[] clipped = new double[o.Length];
            for (int i = 0; i < o.Length; i++)
                clipped[i] = Math.Max(o[i], -1.0);

            result.Cycle = cycle;
            result.Period = period;
            result.Strokes = TrackBeats(clipped, fps, period, first, last, 4.0, 0.5, 2.0);
            return result;
        }

        private static int[] Locate(double[] D, double[] shifts, int T, int W, double x0, double sigmaX)
        {
            double[] tmp = GaussAlong(D, T, W, sigmaX, false);
            double[] smooth = GaussAlong(tmp, T, W, 12.0, true);

            // Each frame to z-scores.
            double[] Z = new double[T * W];
            for (int t = 0; t < T; t++)
            {
                double mean = 0;
                for (int x = 0; x < W; x++)
                    mean += smooth[t * W + x];
                mean /= W;
                double variance = 0;
                for (int x = 0; x < W; x++)
                {
                    double e = smooth[t * W + x] - mean;
                    variance += e * e;
                }

                double std = Math.Sqrt(variance / W) + 1e-6;
                for (int x = 0; x < W; x++)
                    Z[t * W + x] = (smooth[t * W + x] - mean) / std;
            }

            int[] p = Viterbi(Z, shifts, T, W, x0, 0.0, 8);

            // Speed of the swimmer relative to the water (px/frame), then a tighter second pass.
            List<double> rel = new List<double>();
            for (int t = 1; t < T; t++)
                rel.Add(p[t] - p[t - 1] - shifts[t * W + p[t - 1]]);

            double v = rel.Count > 0 ? Median(rel) : 0.0;
            return Viterbi(Z, shifts, T, W, x0, v, 5);
        }

        /// <summary>
        /// Best path of the swimmer along the band: maximises the sum of E minus a penalty on the steps, where a step is
        /// measured after moving the previous position with the camera (shifts) and the swimmer's own speed v.
        /// </summary>
        private static int[] Viterbi(double[] E, double[] shifts, int T, int W, double x0, double v, int maxStep)
        {
            const double lambda = 0.03;
            const double startSigma = 40.0;
            const double negative = -1e18;

            double[] score = new double[W];
            for (int x = 0; x < W; x++)
            {
                double z = (x - x0) / startSigma;
                score[x] = E[x] - z * z;
            }

            int[] back = new int[T * W];
            double[] dest = new double[W];
            double[] sortedDest = new double[W];
            double[] sortedScore = new double[W];
            double[] sortedOrigin = new double[W];
            int[] order = new int[W];
            double[] moved = new double[W];
            double[] origin = new double[W];
            double[] best = new double[W];
            int[] arg = new int[W];

            for (int t = 1; t < T; t++)
            {
                for (int x = 0; x < W; x++)
                {
                    dest[x] = x + shifts[t * W + x] + v;
                    order[x] = x;
                }

                double[] keys = (double[])dest.Clone();
                Array.Sort(keys, order);
                for (int i = 0; i < W; i++)
                {
                    sortedDest[i] = keys[i];
                    sortedScore[i] = score[order[i]];
                    sortedOrigin[i] = order[i];
                }

                Interpolate(sortedDest, sortedScore, negative, negative, moved);
                Interpolate(sortedDest, sortedOrigin, 0, W - 1, origin);

                for (int x = 0; x < W; x++)
                {
                    best[x] = negative;
                    arg[x] = 0;
                }

                for (int st = -maxStep; st <= maxStep; st++)
                {
                    double penalty = lambda * st * st;
                    for (int x = 0; x < W; x++)
                    {
                        int from = x - st;
                        if (from < 0 || from >= W)
                            continue;

                        double candidate = moved[from] - penalty;
                        if (candidate > best[x])
                        {
                            best[x] = candidate;
                            arg[x] = Clamp((int)Math.Floor(origin[from] + 0.5), 0, W - 1);
                        }
                    }
                }

                for (int x = 0; x < W; x++)
                {
                    score[x] = best[x] + E[t * W + x];
                    back[t * W + x] = arg[x];
                }
            }

            int[] path = new int[T];
            int end = 0;
            for (int x = 1; x < W; x++)
            {
                if (score[x] > score[end])
                    end = x;
            }

            path[T - 1] = end;
            for (int t = T - 1; t > 0; t--)
                path[t - 1] = back[t * W + path[t]];

            return path;
        }

        /// <summary>Linear interpolation of (xp, fp) at x = 0, 1, …, out.Length − 1 (xp increasing), like numpy.interp.</summary>
        private static void Interpolate(double[] xp, double[] fp, double left, double right, double[] output)
        {
            int n = xp.Length;
            int j = 0;
            for (int x = 0; x < output.Length; x++)
            {
                if (x < xp[0])
                {
                    output[x] = left;
                    continue;
                }

                if (x > xp[n - 1])
                {
                    output[x] = right;
                    continue;
                }

                while (j < n - 2 && xp[j + 1] <= x)
                    j++;

                double x0 = xp[j], x1 = xp[j + 1];
                if (x >= x1)
                {
                    output[x] = fp[j + 1];
                }
                else if (x1 > x0)
                {
                    double f = (x - x0) / (x1 - x0);
                    output[x] = fp[j] + f * (fp[j + 1] - fp[j]);
                }
                else
                {
                    output[x] = fp[j];
                }
            }
        }

        private static double[] StrokeSignal(double[] D, int T, int W, int[] path, int direction, int ahead, int behind)
        {
            double[] s = new double[T];
            for (int t = 0; t < T; t++)
            {
                int a, b;
                if (direction >= 0)
                {
                    a = path[t] - behind;
                    b = path[t] + ahead;
                }
                else
                {
                    a = path[t] - ahead;
                    b = path[t] + behind;
                }

                a = Math.Max(0, a);
                b = Math.Min(W, b);
                if (b <= a)
                    continue;

                double sum = 0;
                for (int x = a; x < b; x++)
                    sum += D[t * W + x];
                s[t] = sum / (b - a);
            }

            return s;
        }

        private static double[] OnsetStrength(double[] s, double fps)
        {
            double[] x = Gauss1D(s, 2.0);
            int n = Math.Max(3, ((int)(3.0 * fps)) | 1);
            double[] med = MedianFilter(x, n);
            double[] absDev = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
                absDev[i] = Math.Abs(x[i] - med[i]);

            double[] dev = MedianFilter(absDev, n);
            List<double> positive = new List<double>();
            for (int i = 0; i < dev.Length; i++)
            {
                dev[i] *= 1.4826;
                if (dev[i] > 0)
                    positive.Add(dev[i]);
            }

            double floor = positive.Count > 0 ? 0.5 * Median(positive) : 1.0;
            double[] o = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                double value = (x[i] - med[i]) / Math.Max(dev[i], floor);
                o[i] = Math.Max(-3.0, Math.Min(5.0, value));
            }

            return o;
        }

        /// <summary>Stroke cycle (s) from the autocorrelation of the signal, NaN if none in the range.</summary>
        private static double AutocorrelationPeriod(double[] o, double fps, double lo, double hi)
        {
            int n = o.Length;
            double mean = 0;
            for (int i = 0; i < n; i++)
                mean += o[i];
            mean /= n;

            int a = (int)(lo * fps);
            int b = Math.Min(n - 2, (int)(hi * fps));
            if (b <= a + 1)
                return double.NaN;

            double[] ac = new double[b + 2];
            for (int k = 0; k < b + 2; k++)
            {
                double sum = 0;
                for (int i = 0; i + k < n; i++)
                    sum += (o[i] - mean) * (o[i + k] - mean);
                ac[k] = sum / (n - k);
            }

            double zero = ac[0] + 1e-9;
            for (int k = 0; k < ac.Length; k++)
                ac[k] /= zero;

            int count = b - a + 1;
            List<int> candidates = new List<int>();
            for (int i = 1; i < count - 1; i++)
            {
                double y = ac[a + i];
                if (y >= ac[a + i - 1] && y >= ac[a + i + 1])
                    candidates.Add(i);
            }

            if (candidates.Count == 0)
                return double.NaN;

            double top = double.MinValue;
            foreach (int i in candidates)
                top = Math.Max(top, ac[a + i]);

            // The shortest lag close to the best peak (not twice the period).
            int chosen = -1;
            foreach (int i in candidates)
            {
                if (ac[a + i] >= top - 0.15 * Math.Abs(top))
                {
                    chosen = i;
                    break;
                }
            }

            if (chosen < 0)
                return double.NaN;

            double y0 = ac[a + chosen - 1], y1 = ac[a + chosen], y2 = ac[a + chosen + 1];
            double den = y0 - 2 * y1 + y2;
            double d = Math.Abs(den) > 1e-12 ? 0.5 * (y0 - y2) / den : 0.0;
            return (a + chosen + d) / fps;
        }

        /// <summary>
        /// Beat tracking (dynamic programming): the strokes maximise the sum of the signal at the strokes minus
        /// alpha · log²(interval / period) for each interval.
        /// </summary>
        private static List<int> TrackBeats(double[] o, double fps, double period, int first, int last, double alpha, double minRatio, double maxRatio)
        {
            double tau = period * fps;
            int lo = Math.Max(1, (int)Math.Round(minRatio * tau));
            int hi = (int)Math.Round(maxRatio * tau);
            int n = o.Length;
            double[] score = new double[n];
            int[] back = new int[n];
            for (int t = 0; t < n; t++)
                score[t] = double.NegativeInfinity;

            // The first stroke comes within 1.5 periods of the start: later strokes need a previous one.
            int startWindow = first + (int)Math.Round(1.5 * tau);
            for (int t = first; t <= last; t++)
            {
                double best = t <= startWindow ? 0.0 : double.NegativeInfinity;
                int arg = -1;
                for (int d = lo; d <= hi; d++)
                {
                    int p = t - d;
                    if (p < first)
                        break;
                    if (double.IsNegativeInfinity(score[p]))
                        continue;

                    double l = Math.Log(d / tau);
                    double v = score[p] - alpha * l * l;
                    if (v > best)
                    {
                        best = v;
                        arg = p;
                    }
                }

                score[t] = o[t] + best;
                back[t] = arg;
            }

            int tailStart = Math.Max(first, last - (int)Math.Round(1.2 * tau));
            int end = tailStart;
            for (int t = tailStart + 1; t <= last; t++)
            {
                if (score[t] > score[end])
                    end = t;
            }

            List<int> beats = new List<int>();
            int current = end;
            while (current >= 0)
            {
                beats.Add(current);
                current = back[current];
            }

            beats.Reverse();
            return beats;
        }
        #endregion

        #region Lane
        /// <summary>Mean colour along the lines of slope a through (cx, y0) for each y0, over the columns xs.</summary>
        private static double[] LineProfile(SwimStrokeImage img, double cx, double a, double[] y0s, double[] xs)
        {
            double[] profile = new double[y0s.Length * 3];
            for (int i = 0; i < y0s.Length; i++)
            {
                double sb = 0, sg = 0, sr = 0;
                foreach (double x in xs)
                {
                    double b, g, r;
                    img.Sample(x, y0s[i] + a * (x - cx), out b, out g, out r);
                    sb += b; sg += g; sr += r;
                }

                profile[i * 3] = sb / xs.Length;
                profile[i * 3 + 1] = sg / xs.Length;
                profile[i * 3 + 2] = sr / xs.Length;
            }

            return profile;
        }

        /// <summary>Profile minus its Gaussian smoothing (per channel), then the magnitude of the three channels.</summary>
        private static double[] HighPassMagnitude(double[] profile, int n, out double energy)
        {
            double[] magnitude = new double[n];
            energy = 0;
            double[][] channels = new double[3][];
            for (int c = 0; c < 3; c++)
            {
                double[] ch = new double[n];
                for (int i = 0; i < n; i++)
                    ch[i] = profile[i * 3 + c];
                double[] smooth = Gauss1D(ch, RopeSigma);
                for (int i = 0; i < n; i++)
                    ch[i] -= smooth[i];
                channels[c] = ch;
            }

            for (int i = 0; i < n; i++)
            {
                double e = channels[0][i] * channels[0][i] + channels[1][i] * channels[1][i] + channels[2][i] * channels[2][i];
                energy += e;
                magnitude[i] = Math.Sqrt(e);
            }

            energy /= n;
            return magnitude;
        }

        /// <summary>
        /// Ropes of the swimmer's lane on the first frame. Returns false if they can't be found (no ropes in view).
        /// </summary>
        public static bool FindLane(SwimStrokeImage img, double headX, double headY, double feetX, double feetY, out SwimStrokeLane lane)
        {
            lane = new SwimStrokeLane();
            double L = Math.Sqrt((headX - feetX) * (headX - feetX) + (headY - feetY) * (headY - feetY));
            double cx = 0.5 * (headX + feetX);
            double cy = 0.5 * (headY + feetY);
            double halfH = Math.Max(100, Math.Min(0.5 * img.Height, 2.2 * L));

            List<double> columns = new List<double>();
            for (int x = Math.Max(0, (int)(cx - 250)); x < Math.Min(img.Width, (int)(cx + 250)); x += 2)
                columns.Add(x);
            double[] xs = columns.ToArray();
            if (xs.Length == 0)
                return false;

            int n = (int)Math.Ceiling(2 * halfH + 0.5);
            double[] y0s = new double[n];
            for (int i = 0; i < n; i++)
                y0s[i] = cy - halfH + i;

            double bestEnergy = double.MinValue;
            double bestSlope = 0;
            double[] bestMagnitude = null;
            for (int k = 0; k < 161; k++)
            {
                double a = -0.40 + 0.005 * k;
                double[] profile = LineProfile(img, cx, a, y0s, xs);
                double energy;
                double[] magnitude = HighPassMagnitude(profile, n, out energy);
                if (energy > bestEnergy)
                {
                    bestEnergy = energy;
                    bestSlope = a;
                    bestMagnitude = magnitude;
                }
            }

            double[] m = Gauss1D(bestMagnitude, 1.0);
            double max = 0;
            for (int i = 0; i < n; i++)
                max = Math.Max(max, m[i]);
            if (max <= 0)
                return false;

            List<int> peaks = FindPeaks(m, 4, 0.06 * max);
            if (peaks.Count < 3)
                return false;

            double peakMax = 0;
            foreach (int p in peaks)
                peakMax = Math.Max(peakMax, m[p]);

            double[] ys = new double[peaks.Count];
            double[] st = new double[peaks.Count];
            for (int i = 0; i < peaks.Count; i++)
            {
                ys[i] = y0s[peaks[i]];
                st[i] = m[peaks[i]] / peakMax;
            }

            List<int> chain = RopeChain(ys, st, Math.Max(8.0, 0.12 * L), 3.0 * L);
            if (chain.Count < 3)
                return false;

            double slope = bestSlope;
            double bestCoverage = 0;
            double laneY1 = 0, laneY2 = 0;
            bool found = false;
            for (int c = 0; c + 1 < chain.Count; c++)
            {
                double y1 = ys[chain[c]];
                double y2 = ys[chain[c + 1]];
                double coverage = 0;
                for (int k = 0; k <= 10; k++)
                {
                    double f = k / 10.0;
                    double px = feetX + (headX - feetX) * f;
                    double py = feetY + (headY - feetY) * f;
                    if (y1 + slope * (px - cx) <= py && py <= y2 + slope * (px - cx))
                        coverage += f <= 0.7 ? 1.0 : 0.4;
                }

                if (coverage > bestCoverage)
                {
                    bestCoverage = coverage;
                    laneY1 = y1;
                    laneY2 = y2;
                    found = true;
                }
            }

            if (!found || bestCoverage < 2.5)
                return false;

            lane = new SwimStrokeLane(slope, laneY1 - slope * cx, slope, laneY2 - slope * cx);
            return true;
        }

        /// <summary>Longest regularly spaced chain of rope candidates (spacing changes smoothly with the perspective).</summary>
        private static List<int> RopeChain(double[] ys, double[] st, double gmin, double gmax)
        {
            int n = ys.Length;
            double[,] best = new double[n, n];
            int[,] prev = new int[n, n];
            bool[,] valid = new bool[n, n];
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < j; i++)
                {
                    double g = ys[j] - ys[i];
                    if (g < gmin || g > gmax)
                        continue;

                    double sc = st[i] + st[j];
                    int p = -1;
                    for (int h = 0; h < i; h++)
                    {
                        if (!valid[h, i])
                            continue;

                        double r = g / (ys[i] - ys[h]);
                        if (r >= 0.78 && r <= 1.3)
                        {
                            double candidate = best[h, i] + st[j];
                            if (candidate > sc)
                            {
                                sc = candidate;
                                p = h;
                            }
                        }
                    }

                    best[i, j] = sc;
                    prev[i, j] = p;
                    valid[i, j] = true;
                }
            }

            int bi = -1, bj = -1;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (!valid[i, j])
                        continue;
                    if (bi < 0 || best[i, j] > best[bi, bj])
                    {
                        bi = i;
                        bj = j;
                    }
                }
            }

            List<int> chain = new List<int>();
            if (bi < 0)
                return chain;

            chain.Add(bj);
            chain.Add(bi);
            int ci = bi, cj = bj;
            while (prev[ci, cj] >= 0)
            {
                int h = prev[ci, cj];
                chain.Add(h);
                cj = ci;
                ci = h;
            }

            chain.Reverse();
            return chain;
        }

        /// <summary>Band along the swimmer's axis when there are no ropes (side views): ±35 % of the swimmer length.</summary>
        public static SwimStrokeLane AxisBand(double headX, double headY, double feetX, double feetY)
        {
            double dx = headX - feetX, dy = headY - feetY;
            double L = Math.Sqrt(dx * dx + dy * dy);
            double a = Math.Abs(dx) > 1e-6 ? dy / dx : 0.0;
            a = Math.Max(-0.6, Math.Min(0.6, a));
            double mx = 0.5 * (headX + feetX), my = 0.5 * (headY + feetY);
            double off = 0.35 * L * Math.Sqrt(1 + a * a);
            return new SwimStrokeLane(a, my - off - a * mx, a, my + off - a * mx);
        }

        /// <summary>Rope-ness of the lines parallel to (a, b) at the offsets −RopeR … RopeR, null if the line is out of the image.</summary>
        private static double[] Ropeness(SwimStrokeImage img, double a, double b)
        {
            int w = img.Width, h = img.Height;
            int lo = -RopeR - RopeMargin;
            int count = 2 * (RopeR + RopeMargin) + 1;
            List<int> columns = new List<int>();
            for (int x = 0; x < w; x += RopeStep)
            {
                double y = a * x + b;
                if (y + lo >= 0 && y - lo <= h - 1)
                    columns.Add(x);
            }

            if (columns.Count < 12)
                return null;

            double[] profile = new double[count * 3];
            for (int k = 0; k < count; k++)
            {
                double sb = 0, sg = 0, sr = 0;
                foreach (int x in columns)
                {
                    double bb, gg, rr;
                    img.Sample(x, a * x + b + lo + k, out bb, out gg, out rr);
                    sb += bb; sg += gg; sr += rr;
                }

                profile[k * 3] = sb / columns.Count;
                profile[k * 3 + 1] = sg / columns.Count;
                profile[k * 3 + 2] = sr / columns.Count;
            }

            double energy;
            double[] magnitude = HighPassMagnitude(profile, count, out energy);
            double[] m = Gauss1D(magnitude, 1.0);
            double[] result = new double[2 * RopeR + 1];
            Array.Copy(m, RopeMargin, result, 0, result.Length);
            return result;
        }

        /// <summary>
        /// The two ropes of the lane in this frame, from their place predicted with the camera motion. A rope that leaves the
        /// image, is hidden or loses its texture, or that moves away from the other one, follows the good rope with the last
        /// place it had relative to it instead of drifting on its own (a lane along the near wall, a camera in front).
        /// </summary>
        private SwimStrokeLane TrackRopes(SwimStrokeImage image, SwimStrokeLane predicted)
        {
            double a1 = predicted.A1, b1 = predicted.B1, a2 = predicted.A2, b2 = predicted.B2;
            bool ok1 = false, ok2 = false;
            Parallel.Invoke(
                () => ok1 = RefineLine(image, predicted.A1, predicted.B1, out a1, out b1),
                () => ok2 = RefineLine(image, predicted.A2, predicted.B2, out a2, out b2));
            if (!ok1)
            {
                a1 = predicted.A1;
                b1 = predicted.B1;
            }

            if (!ok2)
            {
                a2 = predicted.A2;
                b2 = predicted.B2;
            }

            double inside1, inside2;
            double s1 = RopeStrength(image, a1, b1, out inside1);
            double s2 = RopeStrength(image, a2, b2, out inside2);
            double usual1 = ropeStrength1 > 0 ? ropeStrength1 : s1;
            double usual2 = ropeStrength2 > 0 ? ropeStrength2 : s2;
            bool good1 = inside1 >= 0.25 && s1 >= 0.5 * usual1;
            bool good2 = inside2 >= 0.25 && s2 >= 0.5 * usual2;
            if (good1)
                ropeStrength1 = 0.95 * usual1 + 0.05 * s1;
            if (good2)
                ropeStrength2 = 0.95 * usual2 + 0.05 * s2;

            SwimStrokeLane next = predicted;
            if (good1)
            {
                next.A1 = a1;
                next.B1 = b1;
            }

            if (good2)
            {
                next.A2 = a2;
                next.B2 = b2;
            }

            if (good1 && good2)
            {
                // The two ropes stay nearly parallel and the width of the lane changes slowly: the rope that moved more is wrong.
                double wp = predicted.Width(width / 2.0);
                double wn = next.Width(width / 2.0);
                if (Math.Abs(wn - wp) > 0.06 * Math.Abs(wp) || Math.Abs(next.A1 - next.A2) > Math.Abs(predicted.A1 - predicted.A2) + 0.02)
                {
                    double d1 = Math.Abs(next.B1 - predicted.B1) + 200 * Math.Abs(next.A1 - predicted.A1);
                    double d2 = Math.Abs(next.B2 - predicted.B2) + 200 * Math.Abs(next.A2 - predicted.A2);
                    if (d2 > d1)
                        good2 = false;
                    else
                        good1 = false;
                }
            }

            double xc = width / 2.0;
            if (!good1)
            {
                if (good2)
                {
                    double a = next.A2 - relativeSlope;
                    double y = next.A2 * xc + next.B2 - relativeOffset;
                    next.A1 = a;
                    next.B1 = y - a * xc;
                }
                else
                {
                    next.A1 = predicted.A1;
                    next.B1 = predicted.B1;
                }
            }

            if (!good2)
            {
                if (good1)
                {
                    double a = next.A1 + relativeSlope;
                    double y = next.A1 * xc + next.B1 + relativeOffset;
                    next.A2 = a;
                    next.B2 = y - a * xc;
                }
                else
                {
                    next.A2 = predicted.A2;
                    next.B2 = predicted.B2;
                }
            }

            if (good1 && good2)
                KeepRopeGeometry(next);

            return next;
        }

        /// <summary>The second rope relative to the first one (slope difference, vertical offset at the middle of the image).</summary>
        private void KeepRopeGeometry(SwimStrokeLane l)
        {
            double xc = width / 2.0;
            relativeSlope = l.A2 - l.A1;
            relativeOffset = (l.A2 * xc + l.B2) - (l.A1 * xc + l.B1);
        }

        /// <summary>
        /// Rope-ness at the line itself (peak within two pixels) and the share of the width of the image where the line and its
        /// margins are inside the image.
        /// </summary>
        private static double RopeStrength(SwimStrokeImage img, double a, double b, out double inside)
        {
            int columns = 0, total = 0;
            for (int x = 0; x < img.Width; x += RopeStep)
            {
                double y = a * x + b;
                total++;
                if (y - RopeR - RopeMargin >= 0 && y + RopeR + RopeMargin <= img.Height - 1)
                    columns++;
            }

            inside = total > 0 ? (double)columns / total : 0;
            double[] m = Ropeness(img, a, b);
            if (m == null)
                return 0;

            int c = m.Length / 2;
            double best = 0;
            for (int i = Math.Max(0, c - 2); i <= Math.Min(m.Length - 1, c + 2); i++)
                best = Math.Max(best, m[i]);
            return best;
        }

        /// <summary>Move a rope line to the nearby offset and slope with the strongest rope-ness.</summary>
        private static bool RefineLine(SwimStrokeImage img, double a, double b, out double ra, out double rb)
        {
            ra = a;
            rb = b;
            double xc = 0.5 * img.Width;
            double best = double.MinValue;
            bool found = false;
            double[] slopes = { -0.012, -0.006, 0.0, 0.006, 0.012 };
            foreach (double da in slopes)
            {
                double a2 = a + da;
                double b2 = b - da * xc;
                double[] m = Ropeness(img, a2, b2);
                if (m == null)
                    continue;

                int k = 0;
                double kBest = double.MinValue;
                for (int i = 0; i < m.Length; i++)
                {
                    double sc = m[i] - 0.15 * Math.Abs(i - RopeR) - 20 * Math.Abs(da);
                    if (sc > kBest)
                    {
                        kBest = sc;
                        k = i;
                    }
                }

                if (kBest > best)
                {
                    best = kBest;
                    ra = a2;
                    rb = b2 + (k - RopeR);
                    found = true;
                }
            }

            return found;
        }
        #endregion

        #region Filters
        private static double[] GaussKernel(double sigma, out int r)
        {
            r = (int)Math.Ceiling(3.0 * sigma);
            double[] k = new double[2 * r + 1];
            double sum = 0;
            for (int i = -r; i <= r; i++)
            {
                double z = i / sigma;
                k[i + r] = Math.Exp(-0.5 * z * z);
                sum += k[i + r];
            }

            for (int i = 0; i < k.Length; i++)
                k[i] /= sum;
            return k;
        }

        /// <summary>Gaussian filter of a signal, edges replicated.</summary>
        public static double[] Gauss1D(double[] a, double sigma)
        {
            int n = a.Length;
            double[] output = new double[n];
            if (sigma <= 0)
            {
                Array.Copy(a, output, n);
                return output;
            }

            int r;
            double[] k = GaussKernel(sigma, out r);
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                for (int j = -r; j <= r; j++)
                    s += k[j + r] * a[Clamp(i + j, 0, n - 1)];
                output[i] = s;
            }

            return output;
        }

        /// <summary>Gaussian filter of a T × W array along the frames (alongT) or along the band.</summary>
        private static double[] GaussAlong(double[] a, int T, int W, double sigma, bool alongT)
        {
            int r;
            double[] k = GaussKernel(sigma, out r);
            double[] output = new double[a.Length];
            if (alongT)
            {
                for (int t = 0; t < T; t++)
                {
                    for (int j = -r; j <= r; j++)
                    {
                        int tt = Clamp(t + j, 0, T - 1);
                        double kj = k[j + r];
                        int src = tt * W, dst = t * W;
                        for (int x = 0; x < W; x++)
                            output[dst + x] += kj * a[src + x];
                    }
                }
            }
            else
            {
                for (int t = 0; t < T; t++)
                {
                    int row = t * W;
                    for (int x = 0; x < W; x++)
                    {
                        double s = 0;
                        for (int j = -r; j <= r; j++)
                            s += k[j + r] * a[row + Clamp(x + j, 0, W - 1)];
                        output[row + x] = s;
                    }
                }
            }

            return output;
        }

        /// <summary>Running median over an odd window, edges replicated.</summary>
        private static double[] MedianFilter(double[] x, int n)
        {
            int half = n / 2;
            double[] output = new double[x.Length];
            double[] window = new double[n];
            for (int i = 0; i < x.Length; i++)
            {
                for (int j = 0; j < n; j++)
                    window[j] = x[Clamp(i - half + j, 0, x.Length - 1)];
                Array.Sort(window);
                output[i] = window[half];
            }

            return output;
        }

        private static double Median(List<double> values)
        {
            List<double> sorted = new List<double>(values);
            sorted.Sort();
            int n = sorted.Count;
            if (n == 0)
                return 0;
            return n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
        }

        /// <summary>
        /// Local maxima with at least the given prominence, then the smaller peaks closer than distance to a higher one are removed.
        /// </summary>
        public static List<int> FindPeaks(double[] y, int distance, double prominence)
        {
            int n = y.Length;
            List<int> peaks = new List<int>();
            for (int i = 1; i < n - 1; i++)
            {
                if (!(y[i] > y[i - 1] && y[i] >= y[i + 1]))
                    continue;

                double leftMin = y[i];
                int j = i - 1;
                while (j >= 0 && y[j] <= y[i])
                {
                    leftMin = Math.Min(leftMin, y[j]);
                    j--;
                }

                double rightMin = y[i];
                j = i + 1;
                while (j < n && y[j] <= y[i])
                {
                    rightMin = Math.Min(rightMin, y[j]);
                    j++;
                }

                if (y[i] - Math.Max(leftMin, rightMin) >= prominence)
                    peaks.Add(i);
            }

            if (distance <= 1 || peaks.Count == 0)
                return peaks;

            List<int> byHeight = new List<int>(peaks);
            byHeight.Sort((p, q) => y[q].CompareTo(y[p]));
            HashSet<int> removed = new HashSet<int>();
            foreach (int i in byHeight)
            {
                if (removed.Contains(i))
                    continue;

                foreach (int j in peaks)
                {
                    if (j != i && Math.Abs(j - i) < distance && y[j] <= y[i])
                        removed.Add(j);
                }
            }

            List<int> result = new List<int>();
            foreach (int p in peaks)
            {
                if (!removed.Contains(p))
                    result.Add(p);
            }

            return result;
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
        #endregion

        #region Homography
        public static double[] Identity()
        {
            return new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        }

        public static void MapPoint(double[] H, double x, double y, out double X, out double Y)
        {
            double z = H[6] * x + H[7] * y + H[8];
            X = (H[0] * x + H[1] * y + H[2]) / z;
            Y = (H[3] * x + H[4] * y + H[5]) / z;
        }

        private static void MapLine(double[] H, double a, double b, double x0, double x1, out double ra, out double rb)
        {
            double X0, Y0, X1, Y1;
            MapPoint(H, x0, a * x0 + b, out X0, out Y0);
            MapPoint(H, x1, a * x1 + b, out X1, out Y1);
            ra = (Y1 - Y0) / (X1 - X0);
            rb = Y0 - ra * X0;
        }

        /// <summary>A × B (3×3, row-major).</summary>
        public static double[] Multiply(double[] A, double[] B)
        {
            double[] C = new double[9];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                    C[i * 3 + j] = A[i * 3] * B[j] + A[i * 3 + 1] * B[3 + j] + A[i * 3 + 2] * B[6 + j];
            }

            return C;
        }

        public static double[] Invert(double[] m)
        {
            double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
            double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
            double det = a * A + b * B + c * C;
            if (Math.Abs(det) < 1e-15)
                return Identity();

            double[] inv = new double[9];
            inv[0] = A / det;
            inv[1] = -(b * i - c * h) / det;
            inv[2] = (b * f - c * e) / det;
            inv[3] = B / det;
            inv[4] = (a * i - c * g) / det;
            inv[5] = -(a * f - c * d) / det;
            inv[6] = C / det;
            inv[7] = -(a * h - b * g) / det;
            inv[8] = (a * e - b * d) / det;
            return inv;
        }
        #endregion
    }
}
