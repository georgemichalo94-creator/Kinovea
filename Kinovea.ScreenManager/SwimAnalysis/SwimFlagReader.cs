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

    /// <summary>The head at the flags (5 m) before and after the wall, in seconds of the underwater reading, and the passes used.</summary>
    public sealed class SwimFlagHead
    {
        /// <summary>The head 5 m before the wall (null: not found).</summary>
        public double? In;

        /// <summary>The head 5 m after the wall (null: not found).</summary>
        public double? Out;

        public SwimFlagPass InPass;
        public SwimFlagPass OutPass;

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
    }
}
