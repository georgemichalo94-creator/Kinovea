using System;
using System.Collections.Generic;
using System.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>One image of the external (surface) side camera: the backstroke flags' string and the roll of the camera.</summary>
    public sealed class SwimFlagFrame
    {
        /// <summary>Seconds in the video.</summary>
        public double T;

        /// <summary>Angle of the flags' string in the image (degrees in [0, 180) from the +x axis, 90 = vertical); NaN when not seen.</summary>
        public float Angle = float.NaN;

        /// <summary>Column of the string at a quarter of the height (pixels of the image the reader was given); NaN when not seen.</summary>
        public float X25 = float.NaN;

        /// <summary>Pixels of the difference image on the fitted string.</summary>
        public int Inliers;

        /// <summary>Roll of the camera from the long near-horizontal lines of the far side (degrees); NaN when unknown.</summary>
        public float Roll = float.NaN;
    }

    /// <summary>A pass of the camera through the plane of the flags (5 m from the wall).</summary>
    public sealed class SwimFlagPass
    {
        /// <summary>Seconds in the video when the string is vertical (the camera is in the plane of the flags).</summary>
        public double T;

        /// <summary>Column of the string at that moment (pixels of the reader's image): the column of the camera's own plane.</summary>
        public double X;

        /// <summary>+1 when the angle grows through the vertical, -1 when it falls (the direction the camera moves).</summary>
        public int Direction;

        /// <summary>Degrees the angle turns within 0.75 s of the pass (a clean pass turns 60-100 degrees).</summary>
        public double Range;
    }

    /// <summary>
    /// A line of coloured pennants against the sky in one image of the external camera (outdoor pools, where the string of
    /// triangles is often too busy for the motion of SwimFlagReader.Frame): from SwimFlagReader.ColourCandidates.
    /// </summary>
    public sealed class SwimFlagCandidate
    {
        /// <summary>Angle in the image (degrees in [0, 180), 90 = vertical).</summary>
        public float Angle;

        /// <summary>Column at a quarter of the image height.</summary>
        public float X25;

        /// <summary>Coloured pixels within 4 px of the line.</summary>
        public int Support;

        /// <summary>Highest and lowest rows of those pixels.</summary>
        public int Top, Bottom;

        /// <summary>Those pixels by colour: red/pink, yellow/orange, green.</summary>
        public int Red, Yellow, Green;

        /// <summary>Two colours or more along the line (each at least 12% of the pixels): a string of pennants, not one pennant, a roof or a row of seats.</summary>
        public bool Diverse
        {
            get
            {
                int all = Red + Yellow + Green;
                if (all <= 0)
                    return false;
                int n = (Red >= 0.12 * all ? 1 : 0) + (Yellow >= 0.12 * all ? 1 : 0) + (Green >= 0.12 * all ? 1 : 0);
                return n >= 2;
            }
        }
    }

    /// <summary>The head at the flags (5 m) before and after the wall, in seconds of the underwater reading, and the passes used.</summary>
    public sealed class SwimFlagHead
    {
        /// <summary>The head 5 m before the wall (null: not found).</summary>
        public double? In;

        /// <summary>The head 5 m after the wall (null: not found).</summary>
        public double? Out;

        public SwimFlagPass InPass;
        public SwimFlagPass OutPass;

        /// <summary>
        /// Degrees of the string from the vertical (the roll taken off) at the crossing of each side, NaN when unknown. Far from 0
        /// the camera was away from the flags' plane when the head crossed it: the column of the string at a quarter of the height
        /// is then the plane at one depth only, not at the swimmer's lane (about ±0.1-0.3 s off when 25 degrees or more).
        /// </summary>
        public double InOffPlane = double.NaN, OutOffPlane = double.NaN;

        /// <summary>Passes of the camera through the flags found in the external video (0: no flags seen).</summary>
        public int Passes;

        /// <summary>What was found for each side (for the log): the pass used, the images near it, the head against the plane.</summary>
        public string Trace = "";
    }

    /// <summary>
    /// The backstroke flags (5 m from each wall) in the external side camera that follows the swimmer. The string of flags
    /// is near the camera, so once the far scene of two images is aligned (phase correlation of the upper part) it is what
    /// still moves: the strongest steep line of the difference. When the camera passes the 5 m plane, the string (all of
    /// whose points are then in the camera's vertical plane) turns through the vertical: that is the moment the camera is
    /// at 5 m, and the string's column is where that plane is in the image.
    /// </summary>
    public static class SwimFlagReader
    {
        /// <summary>Part of the height (from the top) where the string is looked for.</summary>
        public const double Band = 0.5;

        /// <summary>Grey levels of change (after the alignment) that count as moving.</summary>
        public const double MoveThreshold = 35;

        /// <summary>Shortest string segment as a part of the band height.</summary>
        public const double MinLength = 0.2;

        /// <summary>Largest tilt from the vertical (degrees) of a string segment.</summary>
        public const double MaxTilt = 60;

        /// <summary>The string in an image from the change since the previous image (both BGR, the same size).</summary>
        public static SwimFlagFrame Frame(OpenCvSharp.Mat prev, OpenCvSharp.Mat cur, double t)
        {
            SwimFlagFrame f = new SwimFlagFrame { T = t };
            int w = cur.Width, h = Math.Max(8, (int)(Band * cur.Height));
            using (OpenCvSharp.Mat a = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat b = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat af = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat bf = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat warped = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat diff = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat blur = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat maskF = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat mask = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat prevTop = new OpenCvSharp.Mat(prev, new OpenCvSharp.Rect(0, 0, w, h)))
            using (OpenCvSharp.Mat curTop = new OpenCvSharp.Mat(cur, new OpenCvSharp.Rect(0, 0, w, h)))
            using (OpenCvSharp.Mat noWindow = new OpenCvSharp.Mat())
            {
                OpenCvSharp.Cv2.CvtColor(prevTop, a, OpenCvSharp.ColorConversionCodes.BGR2GRAY);
                OpenCvSharp.Cv2.CvtColor(curTop, b, OpenCvSharp.ColorConversionCodes.BGR2GRAY);
                a.ConvertTo(af, OpenCvSharp.MatType.CV_32F);
                b.ConvertTo(bf, OpenCvSharp.MatType.CV_32F);
                double response;
                OpenCvSharp.Point2d shift = OpenCvSharp.Cv2.PhaseCorrelate(af, bf, noWindow, out response);
                using (OpenCvSharp.Mat m = new OpenCvSharp.Mat(2, 3, OpenCvSharp.MatType.CV_64F))
                {
                    m.Set(0, 0, 1.0); m.Set(0, 1, 0.0); m.Set(0, 2, shift.X);
                    m.Set(1, 0, 0.0); m.Set(1, 1, 1.0); m.Set(1, 2, shift.Y);
                    OpenCvSharp.Cv2.WarpAffine(af, warped, m, new OpenCvSharp.Size(w, h), OpenCvSharp.InterpolationFlags.Linear, OpenCvSharp.BorderTypes.Replicate);
                }

                OpenCvSharp.Cv2.Absdiff(bf, warped, diff);
                OpenCvSharp.Cv2.GaussianBlur(diff, blur, new OpenCvSharp.Size(3, 3), 0);
                OpenCvSharp.Cv2.Threshold(blur, maskF, MoveThreshold, 255, OpenCvSharp.ThresholdTypes.Binary);
                maskF.ConvertTo(mask, OpenCvSharp.MatType.CV_8U);
                int edge = (int)Math.Abs(shift.X) + 3;
                if (edge * 2 < w)
                {
                    using (OpenCvSharp.Mat left = new OpenCvSharp.Mat(mask, new OpenCvSharp.Rect(0, 0, edge, h)))
                        left.SetTo(OpenCvSharp.Scalar.All(0));
                    using (OpenCvSharp.Mat right = new OpenCvSharp.Mat(mask, new OpenCvSharp.Rect(w - edge, 0, edge, h)))
                        right.SetTo(OpenCvSharp.Scalar.All(0));
                }

                OpenCvSharp.LineSegmentPoint[] segs = OpenCvSharp.Cv2.HoughLinesP(mask, 1, Math.PI / 180, 30, MinLength * h, 15);
                OpenCvSharp.LineSegmentPoint best = default(OpenCvSharp.LineSegmentPoint);
                double bestLen = -1;
                foreach (OpenCvSharp.LineSegmentPoint s in segs)
                {
                    double ang = Angle(s.P1.X, s.P1.Y, s.P2.X, s.P2.Y);
                    if (Math.Abs(ang - 90) > MaxTilt)
                        continue;
                    double len = Math.Sqrt((double)(s.P2.X - s.P1.X) * (s.P2.X - s.P1.X) + (double)(s.P2.Y - s.P1.Y) * (s.P2.Y - s.P1.Y));
                    if (len > bestLen)
                    {
                        bestLen = len;
                        best = s;
                    }
                }

                if (bestLen > 0 && best.P2.Y != best.P1.Y)
                {
                    // Least squares x = c + k y on the moving pixels within 6 px of the segment.
                    byte[] px = new byte[w * h];
                    System.Runtime.InteropServices.Marshal.Copy(mask.Data, px, 0, px.Length);
                    double x1 = best.P1.X, y1 = best.P1.Y, x2 = best.P2.X, y2 = best.P2.Y;
                    double sy = 0, sx = 0, syy = 0, sxy = 0;
                    int n = 0;
                    for (int y = 0; y < h; y++)
                    {
                        double xl = x1 + (y - y1) * (x2 - x1) / (y2 - y1);
                        int xa = Math.Max(0, (int)Math.Floor(xl - 6)), xb = Math.Min(w - 1, (int)Math.Ceiling(xl + 6));
                        for (int x = xa; x <= xb; x++)
                        {
                            if (px[y * w + x] == 0 || Math.Abs(x - xl) >= 6)
                                continue;
                            sy += y; sx += x; syy += (double)y * y; sxy += (double)x * y;
                            n++;
                        }
                    }

                    if (n > 20 && n * syy - sy * sy > 1e-6)
                    {
                        double k = (n * sxy - sx * sy) / (n * syy - sy * sy);
                        double c = (sx - k * sy) / n;
                        double ang = Math.Atan2(1, k) * 180 / Math.PI;
                        f.Angle = (float)(((ang % 180) + 180) % 180);
                        f.X25 = (float)(c + k * 0.25 * cur.Height);
                        f.Inliers = n;
                    }
                    else
                    {
                        f.Angle = (float)Angle(x1, y1, x2, y2);
                        f.X25 = (float)(x1 + (0.25 * cur.Height - y1) * (x2 - x1) / (y2 - y1));
                    }
                }
            }

            f.Roll = (float)Roll(cur);
            return f;
        }

        /// <summary>Angle in [0, 180) degrees of the segment from (x1, y1) to (x2, y2) in image coordinates.</summary>
        private static double Angle(double x1, double y1, double x2, double y2)
        {
            double a = Math.Atan2(y2 - y1, x2 - x1) * 180 / Math.PI;
            return ((a % 180) + 180) % 180;
        }

        /// <summary>Median angle (degrees) of the long near-horizontal lines of the upper half (the far side); NaN with fewer than 3.</summary>
        public static double Roll(OpenCvSharp.Mat bgr)
        {
            int h = Math.Max(8, bgr.Height / 2);
            using (OpenCvSharp.Mat top = new OpenCvSharp.Mat(bgr, new OpenCvSharp.Rect(0, 0, bgr.Width, h)))
            using (OpenCvSharp.Mat g = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat e = new OpenCvSharp.Mat())
            {
                OpenCvSharp.Cv2.CvtColor(top, g, OpenCvSharp.ColorConversionCodes.BGR2GRAY);
                OpenCvSharp.Cv2.Canny(g, e, 50, 150);
                OpenCvSharp.LineSegmentPoint[] segs = OpenCvSharp.Cv2.HoughLinesP(e, 1, Math.PI / 360, 60, 0.25 * bgr.Width, 8);
                List<double> a = new List<double>();
                foreach (OpenCvSharp.LineSegmentPoint s in segs)
                {
                    double ang = Math.Atan2(s.P2.Y - s.P1.Y, s.P2.X - s.P1.X) * 180 / Math.PI;
                    if (ang > 90)
                        ang -= 180;
                    if (ang < -90)
                        ang += 180;
                    if (Math.Abs(ang) < 12)
                        a.Add(ang);
                }

                if (a.Count < 3)
                    return double.NaN;
                a.Sort();
                return a.Count % 2 == 1 ? a[a.Count / 2] : 0.5 * (a[a.Count / 2 - 1] + a[a.Count / 2]);
            }
        }

        /// <summary>
        /// The passes of the camera through the flags' plane: the angle crosses the vertical (90 degrees plus the median roll)
        /// between two images at most 0.35 s apart, both within 40 degrees of it, and the angle turns at least minRange
        /// degrees steadily (|correlation with time| > 0.8) within 0.75 s of the crossing.
        /// </summary>
        public static List<SwimFlagPass> Passes(IList<SwimFlagFrame> frames, double minRange = 25)
        {
            List<SwimFlagPass> passes = new List<SwimFlagPass>();
            List<float> rolls = frames.Where(q => !float.IsNaN(q.Roll)).Select(q => q.Roll).OrderBy(q => q).ToList();
            double roll = rolls.Count > 0 ? rolls[rolls.Count / 2] : 0;
            List<SwimFlagFrame> seen = frames.Where(q => !float.IsNaN(q.Angle)).ToList();
            for (int i = 1; i < seen.Count; i++)
            {
                SwimFlagFrame a = seen[i - 1], b = seen[i];
                if (b.T - a.T > 0.35)
                    continue;
                double da = a.Angle - (90 + roll), db = b.Angle - (90 + roll);
                if (Math.Abs(da) > 40 || Math.Abs(db) > 40 || Math.Sign(da) == Math.Sign(db) || da == 0)
                    continue;
                double w = Math.Abs(da) / (Math.Abs(da) + Math.Abs(db));
                double t = a.T + (b.T - a.T) * w;
                List<SwimFlagFrame> near = seen.Where(q => Math.Abs(q.T - t) <= 0.75).ToList();
                if (near.Count < 5)
                    continue;
                double range = near.Max(q => q.Angle) - near.Min(q => q.Angle);
                double corr = Correlation(near.Select(q => q.T).ToArray(), near.Select(q => (double)q.Angle).ToArray());
                if (range < minRange || Math.Abs(corr) <= 0.8)
                    continue;
                passes.Add(new SwimFlagPass { T = t, X = a.X25 + (b.X25 - a.X25) * w, Direction = corr > 0 ? 1 : -1, Range = range });
            }

            return passes;
        }

        /// <summary>
        /// Image columns from the centre are this many times wider under water than above it (flat port of the underwater
        /// camera, the same lens as the external one just above it on the pole).
        /// </summary>
        public const double Refraction = 1.33;

        /// <summary>
        /// The pass for the head at 5 m: on the approach the last one 1 to 12 s before the wall, after the push-off the first one
        /// within 10 s; the camera moving with the swimmer (side: +1 when the swimmer leaves the wall towards the right of the
        /// image, so the angle grows on the approach and falls after the push-off); the string in the middle 80% of the image.
        /// </summary>
        public static SwimFlagPass Choose(IList<SwimFlagPass> passes, bool approach, double wall, double pushoff, int side, int width)
        {
            int want = approach ? side : -side;
            IEnumerable<SwimFlagPass> ok = passes.Where(p => p.Direction == want && p.X >= 0.1 * width && p.X <= 0.9 * width);
            if (approach)
                return ok.Where(p => p.T >= wall - 12 && p.T <= wall - 1.0).OrderByDescending(p => p.T).FirstOrDefault();
            return ok.Where(p => p.T >= pushoff && p.T <= pushoff + 10).OrderBy(p => p.T).FirstOrDefault();
        }

        private static double Correlation(double[] x, double[] y)
        {
            int n = x.Length;
            double mx = x.Average(), my = y.Average(), sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < n; i++)
            {
                sxy += (x[i] - mx) * (y[i] - my);
                sxx += (x[i] - mx) * (x[i] - mx);
                syy += (y[i] - my) * (y[i] - my);
            }

            return sxx > 0 && syy > 0 ? sxy / Math.Sqrt(sxx * syy) : 0;
        }

        #region Coloured pennants against the sky
        /// <summary>Part of the height (from the top) where the sky and the pennants are looked for.</summary>
        public const double SkyBand = 0.7;

        /// <summary>
        /// The candidate lines of a string of coloured pennants in one image (BGR, 720 px wide or so), the strongest first (at most k):
        /// the sky is the run of blue or white pixels down from the top of each column (gaps of up to 20 px bridged), its lower end
        /// smoothed across columns (running median of 61, never below the 70th percentile + 6% of the height: gaps between buildings
        /// open onto the blue water); the saturated red, yellow and green pixels down to 6 px below it are the pennants and the steep
        /// lines through them (probabilistic Hough, segments of the same line merged) the candidates, refitted on their pixels.
        /// No sky (indoor pools): none. Checked on the 39 turns of an outdoor pool (Heraklion): the string is among them as the
        /// camera passes the flags; SwimFlagReader.ColourTrack picks it over time.
        /// </summary>
        public static List<SwimFlagCandidate> ColourCandidates(OpenCvSharp.Mat bgr, int k = 8)
        {
            List<SwimFlagCandidate> result = new List<SwimFlagCandidate>();
            int width = bgr.Width, height = bgr.Height;
            int h = (int)(SkyBand * height);
            if (width < 64 || h < 16)
                return result;

            byte[] hsv = new byte[width * h * 3];
            using (OpenCvSharp.Mat band = new OpenCvSharp.Mat(bgr, new OpenCvSharp.Rect(0, 0, width, h)))
            using (OpenCvSharp.Mat converted = new OpenCvSharp.Mat())
            {
                OpenCvSharp.Cv2.CvtColor(band, converted, OpenCvSharp.ColorConversionCodes.BGR2HSV);
                for (int y = 0; y < h; y++)
                    System.Runtime.InteropServices.Marshal.Copy(converted.Ptr(y), hsv, y * width * 3, width * 3);
            }

            // The skyline: per column the end of the sky down from the top.
            int[] ends = new int[width];
            for (int x = 0; x < width; x++)
            {
                if (!Sky(hsv, (0 * width + x) * 3))
                    continue;
                int end = 0, miss = 0;
                for (int y = 0; y < h; y++)
                {
                    if (Sky(hsv, (y * width + x) * 3))
                    {
                        end = y;
                        miss = 0;
                    }
                    else if (++miss > 20)
                    {
                        break;
                    }
                }

                ends[x] = end;
            }

            int[] line = new int[width];
            int[] window = new int[61];
            for (int x = 0; x < width; x++)
            {
                for (int j = 0; j < 61; j++)
                    window[j] = ends[Math.Max(0, Math.Min(width - 1, x - 30 + j))];
                Array.Sort(window);
                line[x] = window[30];
            }

            int[] sorted = (int[])line.Clone();
            Array.Sort(sorted);
            double at = 0.7 * (width - 1);
            int lo = (int)Math.Floor(at);
            double p70 = sorted[lo] + (lo + 1 < width ? (at - lo) * (sorted[lo + 1] - sorted[lo]) : 0);
            int cap = (int)p70 + (int)(0.06 * height);
            for (int x = 0; x < width; x++)
                line[x] = Math.Min(line[x], cap);

            // The coloured pixels above the skyline.
            byte[] mask = new byte[width * h];
            List<int> xs = new List<int>(), ys = new List<int>();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (y > line[x] + 6)
                        continue;
                    int i = (y * width + x) * 3;
                    int hue = hsv[i], sat = hsv[i + 1], val = hsv[i + 2];
                    bool colour = hue <= 14 || hue >= 158 || (hue >= 17 && hue <= 88);
                    if (!colour || sat < 90 || val < 80)
                        continue;
                    mask[y * width + x] = 255;
                    xs.Add(x);
                    ys.Add(y);
                }
            }

            if (xs.Count < 20)
                return result;

            OpenCvSharp.LineSegmentPoint[] segs;
            using (OpenCvSharp.Mat m = new OpenCvSharp.Mat(h, width, OpenCvSharp.MatType.CV_8UC1))
            using (OpenCvSharp.Mat dilated = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat kernel = new OpenCvSharp.Mat(3, 3, OpenCvSharp.MatType.CV_8UC1, OpenCvSharp.Scalar.All(1)))
            {
                for (int y = 0; y < h; y++)
                    System.Runtime.InteropServices.Marshal.Copy(mask, y * width, m.Ptr(y), width);
                OpenCvSharp.Cv2.Dilate(m, dilated, kernel);
                segs = OpenCvSharp.Cv2.HoughLinesP(dilated, 1, Math.PI / 180, 15, (int)(0.08 * h), 40);
            }

            // Segments of the same line merged (the longest kept): within 6 degrees and 20 px at a quarter of the height.
            double y25 = 0.25 * height;
            List<double[]> raw = new List<double[]>();
            foreach (OpenCvSharp.LineSegmentPoint s in segs)
            {
                if (s.P1.Y == s.P2.Y)
                    continue;
                double ang = Angle(s.P1.X, s.P1.Y, s.P2.X, s.P2.Y);
                if (Math.Abs(ang - 90) > 65)
                    continue;
                double len = Math.Sqrt((double)(s.P2.X - s.P1.X) * (s.P2.X - s.P1.X) + (double)(s.P2.Y - s.P1.Y) * (s.P2.Y - s.P1.Y));
                double x25 = s.P1.X + (y25 - s.P1.Y) * (s.P2.X - s.P1.X) / (double)(s.P2.Y - s.P1.Y);
                raw.Add(new[] { len, ang, x25 });
            }

            raw.Sort((p, q) => q[0].CompareTo(p[0]));
            List<double[]> groups = new List<double[]>();
            foreach (double[] r in raw)
            {
                if (!groups.Any(g => Math.Abs(g[1] - r[1]) <= 6 && Math.Abs(g[2] - r[2]) <= 20))
                    groups.Add(r);
            }

            foreach (double[] g in groups.Take(2 * k))
            {
                double ang = g[1], x25 = g[2];
                double rad = ang * Math.PI / 180, dx = Math.Cos(rad), dy = Math.Sin(rad);
                double sy = 0, sx = 0, syy = 0, sxy = 0;
                int n = 0, top = int.MaxValue, bottom = int.MinValue, red = 0, yellow = 0, green = 0;
                for (int j = 0; j < xs.Count; j++)
                {
                    if (Math.Abs((xs[j] - x25) * dy - (ys[j] - y25) * dx) > 4)
                        continue;
                    n++;
                    sy += ys[j]; sx += xs[j]; syy += (double)ys[j] * ys[j]; sxy += (double)xs[j] * ys[j];
                    top = Math.Min(top, ys[j]);
                    bottom = Math.Max(bottom, ys[j]);
                    int hue = hsv[(ys[j] * width + xs[j]) * 3];
                    if (hue <= 14 || hue >= 158)
                        red++;
                    else if (hue >= 15 && hue <= 35)
                        yellow++;
                    else if (hue >= 36 && hue <= 88)
                        green++;
                }

                if (n < 20)
                    continue;

                // Refitted x = c + kk y on its pixels, kept unless it moves away from the segment.
                double det = n * syy - sy * sy;
                if (bottom - top >= 10 && det > 1e-6)
                {
                    double kk = (n * sxy - sx * sy) / det;
                    double c = (sx - kk * sy) / n;
                    double angF = ((Math.Atan2(1, kk) * 180 / Math.PI) % 180 + 180) % 180;
                    double x25F = c + kk * y25;
                    if (Math.Abs(angF - ang) <= 8 && Math.Abs(x25F - x25) <= 30)
                    {
                        ang = angF;
                        x25 = x25F;
                    }
                }

                result.Add(new SwimFlagCandidate { Angle = (float)ang, X25 = (float)x25, Support = n, Top = top, Bottom = bottom, Red = red, Yellow = yellow, Green = green });
            }

            return result.OrderByDescending(c => c.Support).Take(k).ToList();
        }

        /// <summary>Blue sky (hue 85-135, bright) or white clouds (grey, very bright) at this HSV pixel.</summary>
        private static bool Sky(byte[] hsv, int i)
        {
            int hue = hsv[i], sat = hsv[i + 1], val = hsv[i + 2];
            return (hue >= 85 && hue <= 135 && val >= 120) || (sat < 45 && val >= 165);
        }

        /// <summary>
        /// The string of coloured pennants over time from the candidates of each image (times in seconds, same order): chains of
        /// candidates with a smooth angle and column (dynamic programming over gaps of up to 5 images and 0.55 s, the best chain
        /// first, up to 6), a candidate worth more when it has many pixels, reaches the top of the image and has several colours
        /// (the string, not a roof or one big pennant), steeper than 35 degrees from the horizontal. The chains of 8 images or
        /// more with several colours on at least half of them give the string's images (the best chain where two meet); roll from
        /// the given values (the motion reading of the same images), NaN without them.
        /// </summary>
        public static List<SwimFlagFrame> ColourTrack(IList<double> times, IList<List<SwimFlagCandidate>> candidates, IList<float> rolls = null, int imageHeight = 540)
        {
            int n = Math.Min(times.Count, candidates.Count);
            List<SwimFlagCandidate>[] c = new List<SwimFlagCandidate>[n];
            for (int i = 0; i < n; i++)
                c[i] = candidates[i] ?? new List<SwimFlagCandidate>();

            HashSet<long> used = new HashSet<long>();
            SwimFlagCandidate[] chosen = new SwimFlagCandidate[n];
            for (int round = 0; round < 6; round++)
            {
                double[][] cost = new double[n][];
                int[][] prevI = new int[n][], prevK = new int[n][];
                double best = 0;
                int bi = -1, bk = -1;
                for (int i = 0; i < n; i++)
                {
                    int m = c[i].Count;
                    cost[i] = new double[m];
                    prevI[i] = new int[m];
                    prevK[i] = new int[m];
                    for (int k = 0; k < m; k++)
                    {
                        cost[i][k] = double.NaN;
                        prevI[i][k] = -1;
                        if (used.Contains(((long)i << 8) | (uint)k))
                            continue;
                        double reward;
                        if (!Reward(c[i][k], imageHeight, out reward))
                            continue;
                        double v0 = 0.5 - reward;
                        for (int g = 1; g <= 5; g++)
                        {
                            int j = i - g;
                            if (j < 0 || times[i] - times[j] > 0.55)
                                break;
                            for (int l = 0; l < c[j].Count; l++)
                            {
                                if (double.IsNaN(cost[j][l]))
                                    continue;
                                double da = c[i][k].Angle - c[j][l].Angle, dx = c[i][k].X25 - c[j][l].X25;
                                if (Math.Abs(da) > 14 * g || Math.Abs(dx) > 90 * g)
                                    continue;
                                double v = cost[j][l] + (da / (5.0 * g)) * (da / (5.0 * g)) + (dx / (30.0 * g)) * (dx / (30.0 * g)) + 0.4 * (g - 1) - reward;
                                if (v < v0)
                                {
                                    v0 = v;
                                    prevI[i][k] = j;
                                    prevK[i][k] = l;
                                }
                            }
                        }

                        cost[i][k] = v0;
                        if (v0 < best)
                        {
                            best = v0;
                            bi = i;
                            bk = k;
                        }
                    }
                }

                if (bi < 0)
                    break;

                List<int[]> chain = new List<int[]>();
                for (int i = bi, k = bk; i >= 0; )
                {
                    chain.Add(new[] { i, k });
                    int pi = prevI[i][k], pk = prevK[i][k];
                    i = pi;
                    k = pk;
                }

                foreach (int[] node in chain)
                    used.Add(((long)node[0] << 8) | (uint)node[1]);

                if (chain.Count < 8 || chain.Count(node => c[node[0]][node[1]].Diverse) < 0.5 * chain.Count)
                    continue;
                foreach (int[] node in chain)
                {
                    if (chosen[node[0]] == null)
                        chosen[node[0]] = c[node[0]][node[1]];
                }
            }

            List<SwimFlagFrame> frames = new List<SwimFlagFrame>();
            for (int i = 0; i < n; i++)
            {
                if (chosen[i] == null)
                    continue;
                frames.Add(new SwimFlagFrame
                {
                    T = times[i],
                    Angle = chosen[i].Angle,
                    X25 = chosen[i].X25,
                    Inliers = chosen[i].Support,
                    Roll = rolls != null && i < rolls.Count ? rolls[i] : float.NaN
                });
            }

            return frames;
        }

        /// <summary>What a candidate adds to a chain (false: too far from the vertical to be the string near a pass).</summary>
        private static bool Reward(SwimFlagCandidate c, int imageHeight, out double reward)
        {
            reward = 0;
            if (Math.Abs(c.Angle - 90) > 55)
                return false;
            reward = Math.Log(1 + c.Support / 40.0) + (c.Top <= 0.04 * imageHeight ? 0.5 : 0) + (c.Diverse ? 1.5 : -0.5) - 1.2;
            return true;
        }

        /// <summary>Images of the string of coloured pennants from which a video counts as one with pennants (SwimFlagReader.Merge).</summary>
        public const int PennantImages = 20;

        /// <summary>
        /// The head at the flags of a video with coloured pennants against the sky (outdoor pools): from the pennants alone when
        /// their string was followed in PennantImages images or more, else from the motion of the string. In such videos the motion
        /// picks posts, floodlight masts and the flapping of the pennants (Heraklion, 39 turns: 17 −5 and 8 +5 from the motion, most
        /// off by 0.4-1.6 s or on a mast, against 21 and 29 from the pennants): no value is better than one of those.
        /// </summary>
        public static SwimFlagHead Merge(SwimFlagHead motion, SwimFlagHead colour, int pennantImages)
        {
            if (colour == null || pennantImages < PennantImages)
                return motion;

            colour.Passes = Math.Max(colour.Passes, motion != null ? motion.Passes : 0);
            colour.Trace = "pennants in " + pennantImages + " images: " + colour.Trace + (motion != null ? "(motion, not used: " + motion.Trace + ")" : "");
            return colour;
        }
        #endregion
    }
}
