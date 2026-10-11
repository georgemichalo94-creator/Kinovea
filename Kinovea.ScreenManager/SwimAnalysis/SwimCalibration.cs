#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Newtonsoft.Json;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// What the next click on the video is used for.
    /// </summary>
    public enum SwimPickTarget
    {
        None,
        Calibration,
        EntryHands,
        EntryHips,
        StartUnderwater,
        StartBreakout,
        TurnUnderwater,
        FinishLastStroke,

        /// <summary>Three clicks: end, vertex, end of an angle.</summary>
        Angle,

        /// <summary>Training video: entry and breakout distance of the selected attempt.</summary>
        PhaseEntry,
        PhaseBreakout
    }

    public class SwimPoint
    {
        public float X;
        public float Y;

        public SwimPoint()
        {
        }

        public SwimPoint(PointF p)
        {
            X = p.X;
            Y = p.Y;
        }

        [JsonIgnore]
        public PointF PointF
        {
            get { return new PointF(X, Y); }
        }
    }

    /// <summary>
    /// Perspective calibration of the water surface for one camera position.
    /// The analyst clicks two lines across the lane (known distances from the start wall, for example the
    /// lane rope colour changes at 5 m and 15 m) on both lane ropes. The 4 points give a plane homography
    /// image → pool coordinates (x: meters from the start wall, y: meters across the lane).
    /// Only points on the water surface are measured correctly (hand entry, head breaking the surface).
    /// </summary>
    public class SwimCalibration
    {
        public string Id = Guid.NewGuid().ToString("N");

        /// <summary>Video timestamp of the frame where the calibration was made.</summary>
        public long Timestamp;

        /// <summary>Distance of the first and second line from the start wall (m).</summary>
        public double Line1;
        public double Line2;

        /// <summary>Distance between the two lane ropes (m).</summary>
        public double LaneWidth = 2.5;

        /// <summary>Image points: line 1 near rope, line 1 far rope, line 2 near rope, line 2 far rope.</summary>
        public List<SwimPoint> Points = new List<SwimPoint>();

        private double[] imageToPool;
        private double[] poolToImage;

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                Prepare();
                return imageToPool != null && poolToImage != null;
            }
        }

        [JsonIgnore]
        public string Label
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0.##}–{1:0.##} μ.", Math.Min(Line1, Line2), Math.Max(Line1, Line2));
            }
        }

        /// <summary>
        /// Pool coordinates of the 4 calibration points, in click order.
        /// </summary>
        public static PointF[] PoolPoints(double line1, double line2, double laneWidth)
        {
            return new PointF[]
            {
                new PointF((float)line1, 0),
                new PointF((float)line1, (float)laneWidth),
                new PointF((float)line2, 0),
                new PointF((float)line2, (float)laneWidth)
            };
        }

        /// <summary>
        /// Computes the homography. Returns an error message, or null if the calibration is usable.
        /// </summary>
        public string Validate()
        {
            imageToPool = null;
            poolToImage = null;

            if (Points == null || Points.Count != 4)
                return "Χρειάζονται 4 σημεία.";
            if (Math.Abs(Line1 - Line2) < 0.99)
                return "Οι δύο γραμμές πρέπει να απέχουν τουλάχιστον 1 μέτρο.";
            if (LaneWidth <= 0.5 || LaneWidth > 5)
                return "Το πλάτος του διαδρόμου πρέπει να είναι μεταξύ 0.5 και 5 μέτρων.";

            PointF[] pool = PoolPoints(Line1, Line2, LaneWidth);
            double[] sx = new double[4], sy = new double[4], dx = new double[4], dy = new double[4];
            for (int i = 0; i < 4; i++)
            {
                sx[i] = Points[i].X;
                sy[i] = Points[i].Y;
                dx[i] = pool[i].X;
                dy[i] = pool[i].Y;
            }

            for (int i = 0; i < 4; i++)
            {
                for (int j = i + 1; j < 4; j++)
                {
                    if (Math.Abs(sx[i] - sx[j]) + Math.Abs(sy[i] - sy[j]) < 4)
                        return "Δύο σημεία είναι σχεδόν στην ίδια θέση.";
                }
            }

            double[] h = SwimHomography.Solve(sx, sy, dx, dy);
            if (h == null)
                return "Τα σημεία είναι σε ευθεία γραμμή: δεν βγαίνει βαθμονόμηση.";

            // All the points must be on the same side of the horizon line, otherwise the clicks were not in
            // the expected order (the quadrilateral is crossed).
            int sign = 0;
            for (int i = 0; i < 4; i++)
            {
                double w = SwimHomography.W(h, sx[i], sy[i]);
                if (double.IsNaN(w) || double.IsInfinity(w))
                    return "Η βαθμονόμηση δεν είναι έγκυρη.";

                int s = Math.Sign(w);
                if (s == 0 || (sign != 0 && s != sign))
                    return "Τα σημεία δεν είναι με τη σωστή σειρά (κοντινό/μακρινό σχοινί). Ξανάκανε τη βαθμονόμηση.";
                sign = s;
            }

            double[] inverse = SwimHomography.Invert(h);
            if (inverse == null)
                return "Η βαθμονόμηση δεν είναι αντιστρέψιμη.";

            imageToPool = h;
            poolToImage = inverse;
            return null;
        }

        private void Prepare()
        {
            if (imageToPool == null && Points != null && Points.Count == 4)
                Validate();
        }

        /// <summary>
        /// Pool coordinates (x: meters from the start wall, y: meters from the near rope) of an image point.
        /// </summary>
        public bool TryPool(PointF image, out double x, out double y)
        {
            x = 0;
            y = 0;
            Prepare();
            if (imageToPool == null)
                return false;

            return SwimHomography.Apply(imageToPool, image.X, image.Y, out x, out y);
        }

        /// <summary>
        /// Image point of a pool position. False if the position is behind the camera horizon.
        /// </summary>
        public bool TryImage(double x, double y, out PointF image)
        {
            image = PointF.Empty;
            Prepare();
            if (poolToImage == null)
                return false;

            // The point must be on the same side of the horizon as the calibration points.
            double w = SwimHomography.W(poolToImage, x, y);
            double wRef = SwimHomography.W(poolToImage, (Line1 + Line2) / 2, LaneWidth / 2);
            if (double.IsNaN(w) || double.IsNaN(wRef) || double.IsInfinity(w) || double.IsInfinity(wRef))
                return false;

            if (Math.Sign(w) != Math.Sign(wRef) || Math.Abs(w) < Math.Abs(wRef) * 0.05)
                return false;

            double ix, iy;
            if (!SwimHomography.Apply(poolToImage, x, y, out ix, out iy))
                return false;

            if (double.IsNaN(ix) || double.IsNaN(iy) || Math.Abs(ix) > 1e5 || Math.Abs(iy) > 1e5)
                return false;

            image = new PointF((float)ix, (float)iy);
            return true;
        }
    }

    /// <summary>
    /// Plane homography from 4 point correspondences (normalized DLT, exact solution).
    /// Matrices are 3x3 row major arrays.
    /// </summary>
    public static class SwimHomography
    {
        public static double[] Solve(double[] sx, double[] sy, double[] dx, double[] dy)
        {
            double[] ts = Normalizer(sx, sy);
            double[] td = Normalizer(dx, dy);
            if (ts == null || td == null)
                return null;

            const int n = 8;
            double[,] a = new double[n, n + 1];
            for (int i = 0; i < 4; i++)
            {
                double u, v, x, y;
                Apply(ts, sx[i], sy[i], out u, out v);
                Apply(td, dx[i], dy[i], out x, out y);

                int r = 2 * i;
                a[r, 0] = u; a[r, 1] = v; a[r, 2] = 1;
                a[r, 3] = 0; a[r, 4] = 0; a[r, 5] = 0;
                a[r, 6] = -u * x; a[r, 7] = -v * x; a[r, 8] = x;

                r++;
                a[r, 0] = 0; a[r, 1] = 0; a[r, 2] = 0;
                a[r, 3] = u; a[r, 4] = v; a[r, 5] = 1;
                a[r, 6] = -u * y; a[r, 7] = -v * y; a[r, 8] = y;
            }

            // Gauss-Jordan elimination with partial pivoting.
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < n; r++)
                {
                    if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                        pivot = r;
                }

                if (Math.Abs(a[pivot, col]) < 1e-10)
                    return null;

                if (pivot != col)
                {
                    for (int k = 0; k <= n; k++)
                    {
                        double tmp = a[col, k];
                        a[col, k] = a[pivot, k];
                        a[pivot, k] = tmp;
                    }
                }

                for (int r = 0; r < n; r++)
                {
                    if (r == col)
                        continue;

                    double f = a[r, col] / a[col, col];
                    if (f == 0)
                        continue;

                    for (int k = col; k <= n; k++)
                        a[r, k] -= f * a[col, k];
                }
            }

            double[] hn = new double[9];
            for (int r = 0; r < n; r++)
                hn[r] = a[r, n] / a[r, r];
            hn[8] = 1;

            double[] tdInverse = Invert(td);
            if (tdInverse == null)
                return null;

            double[] h = Multiply(Multiply(tdInverse, hn), ts);
            if (Math.Abs(h[8]) > 1e-12)
            {
                double s = h[8];
                for (int i = 0; i < 9; i++)
                    h[i] /= s;
            }

            return h;
        }

        public static double W(double[] h, double x, double y)
        {
            return h[6] * x + h[7] * y + h[8];
        }

        public static bool Apply(double[] h, double x, double y, out double ox, out double oy)
        {
            ox = 0;
            oy = 0;
            double w = W(h, x, y);
            if (Math.Abs(w) < 1e-12)
                return false;

            ox = (h[0] * x + h[1] * y + h[2]) / w;
            oy = (h[3] * x + h[4] * y + h[5]) / w;
            return true;
        }

        public static double[] Invert(double[] m)
        {
            double a = m[0], b = m[1], c = m[2];
            double d = m[3], e = m[4], f = m[5];
            double g = m[6], h = m[7], i = m[8];

            double ca = e * i - f * h;
            double cb = -(d * i - f * g);
            double cc = d * h - e * g;
            double det = a * ca + b * cb + c * cc;
            if (Math.Abs(det) < 1e-15)
                return null;

            double cd = -(b * i - c * h);
            double ce = a * i - c * g;
            double cf = -(a * h - b * g);
            double cg = b * f - c * e;
            double ch = -(a * f - c * d);
            double ci = a * e - b * d;

            return new double[]
            {
                ca / det, cd / det, cg / det,
                cb / det, ce / det, ch / det,
                cc / det, cf / det, ci / det
            };
        }

        private static double[] Multiply(double[] a, double[] b)
        {
            double[] r = new double[9];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < 3; k++)
                        sum += a[3 * i + k] * b[3 * k + j];
                    r[3 * i + j] = sum;
                }
            }

            return r;
        }

        /// <summary>
        /// Similarity transform moving the centroid to the origin with a mean distance of √2.
        /// </summary>
        private static double[] Normalizer(double[] xs, double[] ys)
        {
            int count = xs.Length;
            double cx = 0, cy = 0;
            for (int i = 0; i < count; i++)
            {
                cx += xs[i];
                cy += ys[i];
            }

            cx /= count;
            cy /= count;

            double md = 0;
            for (int i = 0; i < count; i++)
                md += Math.Sqrt((xs[i] - cx) * (xs[i] - cx) + (ys[i] - cy) * (ys[i] - cy));
            md /= count;

            if (md < 1e-12)
                return null;

            double s = Math.Sqrt(2) / md;
            return new double[] { s, 0, -s * cx, 0, s, -s * cy, 0, 0, 1 };
        }
    }

    /// <summary>
    /// Last distance measured with a click, drawn on its frame as a visual check.
    /// </summary>
    public class SwimMeasurement
    {
        public long Timestamp;
        public PointF Image;
        public double PoolX;
        public double PoolY;
        public double Value;
        public string Label;
        public SwimCalibration Calibration;
    }
}
