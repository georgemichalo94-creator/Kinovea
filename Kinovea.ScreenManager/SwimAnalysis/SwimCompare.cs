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
    /// Where a reference race (another race of the athlete, another athlete, an elite race) is at the time of the video, in the lane
    /// of the swimmer (since 2.8.0): its distance from its times at the checkpoints (a monotone curve through them, as the swimmer's),
    /// its place in the pool, and the place of the image of that place of the pool. With the swimmer followed (a part of the race read
    /// since 2.8.0): where the swimmer is, will be or was at that place of the pool in the same length, moved with the camera to this
    /// frame. With a fixed camera: the calibration of the water. The ghost and the line of the reference are drawn there.
    /// </summary>
    public static class SwimCompare
    {
        /// <summary>The swimmer's body centre (the place followed) is this share of his length behind the head.</summary>
        public const double HeadLead = 0.5;

        /// <summary>A place of the pool is found past the ends of the frames of a length up to this share of the pool.</summary>
        public const double ExtendShare = 0.25;

        /// <summary>With the calibration of a fixed camera: the body centre behind the head and the length of a swimmer (pool units).</summary>
        public const double CalibratedLead = 0.9, CalibratedBody = 2.0;

        /// <summary>A monotone curve of the distance over the time (PCHIP, as the swimmer's in SwimReel) through (0, 0) and the checkpoints.</summary>
        public sealed class Curve
        {
            private readonly List<double> times = new List<double>();
            private readonly List<double> distances = new List<double>();
            private double[] slopes = new double[0];
            private double total;

            public bool Valid
            {
                get { return times.Count >= 2; }
            }

            /// <summary>Time of the finish: the last time when it is at the whole distance, else when it gets there at its last speed.</summary>
            public double FinishTime
            {
                get
                {
                    int n = times.Count;
                    if (n < 2)
                        return double.PositiveInfinity;
                    double last = distances[n - 1];
                    if (last >= total - 1e-9)
                        return times[n - 1];
                    double v = (distances[n - 1] - distances[n - 2]) / (times[n - 1] - times[n - 2]);
                    return v > 1e-9 ? times[n - 1] + (total - last) / v : double.PositiveInfinity;
                }
            }

            /// <summary>
            /// The curve of a reference: (0, 0) and its times, both increasing (a time out of order is left out). total: the distance
            /// of the race (after the last time of a reference without its final time, on at its last speed up to it).
            /// </summary>
            public static Curve Of(SwimReference reference, double total)
            {
                Curve c = new Curve();
                c.total = total;
                c.times.Add(0);
                c.distances.Add(0);
                if (reference == null || reference.Times == null)
                    return c;

                foreach (SwimReferenceSplit s in reference.Times.Where(x => x != null && x.D > 0 && x.T > 0 && !double.IsNaN(x.T) && !double.IsInfinity(x.T)).OrderBy(x => x.D))
                {
                    if (s.T <= c.times[c.times.Count - 1] + 1e-6 || s.D <= c.distances[c.distances.Count - 1])
                        continue;

                    c.times.Add(s.T);
                    c.distances.Add(s.D);
                }

                if (c.total <= 0)
                    c.total = c.distances[c.distances.Count - 1];
                c.slopes = SwimReel.PchipSlopes(c.times, c.distances);
                return c;
            }

            /// <summary>Distance at a time after the start: 0 before it, the whole distance after the finish (at its wall).</summary>
            public double DistanceAt(double t)
            {
                int n = times.Count;
                if (n < 2 || t <= 0)
                    return 0;
                if (t >= times[n - 1])
                {
                    double last = distances[n - 1];
                    if (last >= total - 1e-9)
                        return last;
                    double v = (distances[n - 1] - distances[n - 2]) / (times[n - 1] - times[n - 2]);
                    return Math.Min(total, last + Math.Max(0, v) * (t - times[n - 1]));
                }

                int k = 0;
                while (k < n - 2 && t > times[k + 1])
                    k++;

                double h = times[k + 1] - times[k];
                double s = (t - times[k]) / h;
                double h00 = (1 + 2 * s) * (1 - s) * (1 - s);
                double h10 = s * (1 - s) * (1 - s);
                double h01 = s * s * (3 - 2 * s);
                double h11 = s * s * (s - 1);
                return h00 * distances[k] + h10 * h * slopes[k] + h01 * distances[k + 1] + h11 * h * slopes[k + 1];
            }
        }

        /// <summary>Where the reference is drawn in this frame (reference image pixels).</summary>
        public sealed class Placement
        {
            /// <summary>The place of the reference in the image is known (else only on which side of the swimmer it is).</summary>
            public bool Located;

            /// <summary>Centre of the ghost of the reference (its body, as the swimmer's place followed).</summary>
            public float X, Y;

            /// <summary>The line of the reference across the lane, at its head (the water at its place in the pool).</summary>
            public PointF LineA, LineB;

            /// <summary>The swimmer in this frame (centre of his body, the source of the ghost), half the height of his lane.</summary>
            public float SwimmerX, SwimmerY, HalfHeight;

            /// <summary>Length of the swimmer (pixels).</summary>
            public float Body;

            /// <summary>The swimmer swims to the right of the image (+1) or to the left (−1), 0 if not known.</summary>
            public int Direction;

            /// <summary>The reference is to the right (+1) or to the left (−1) of the swimmer in the image, 0 at the same place.</summary>
            public int Side;

            /// <summary>Distance of the reference ahead of the swimmer (pool units; negative: behind).</summary>
            public double Ahead;

            /// <summary>The reference swims the other way (it turned, or not yet): the ghost is drawn mirrored.</summary>
            public bool Mirror;

            /// <summary>The reference has finished (it is at its wall), and since when (s).</summary>
            public bool Finished;
            public double FinishedFor;

            /// <summary>The place comes from frames on both sides of it, or the calibration (not extended past the frames of the length).</summary>
            public bool Inside;
        }

        /// <summary>Lap (0: first length) of a distance of the race; a wall belongs to the length that ends there.</summary>
        public static int Lap(double distance, int pool)
        {
            if (pool <= 0 || distance <= 0)
                return 0;

            return Math.Max(0, (int)Math.Ceiling(distance / pool - 1e-9) - 1);
        }

        /// <summary>Place in the pool (0 at the wall of the start, 'pool' at the other end) of a distance of the race.</summary>
        public static double PoolPlace(double distance, int pool)
        {
            if (pool <= 0)
                return distance;

            int lap = Lap(distance, pool);
            double within = distance - lap * (double)pool;
            return lap % 2 == 0 ? within : pool - within;
        }

        /// <summary>Direction of a length along the places of the pool: +1 away from the wall of the start, −1 towards it.</summary>
        public static int PoolDirection(double distance, int pool)
        {
            return Lap(distance, pool) % 2 == 0 ? 1 : -1;
        }

        /// <summary>
        /// The reference in this frame of a part of the race followed (track with the camera's displacement): its place in the pool
        /// from its curve at the time of the race; the frames of the part in the same length as the swimmer now, with the place of the
        /// swimmer in the pool (his curve, at his head) and in the image (X − Pan, his body); the image of the reference's place:
        /// between the two frames around it, or past the ends of the part along the line of the last pool units there.
        /// Null when the reference cannot be compared here: no camera displacement, too few frames of this length, before the start.
        /// raceSeconds: time of the race of a timestamp; swimmerDistance: the swimmer's distance at a time of the race (his curve).
        /// </summary>
        public static Placement Place(SwimTrack track, long timestamp, Func<long, double> raceSeconds, Func<double, double> swimmerDistance,
            Curve reference, int pool)
        {
            if (track == null || !track.HasPan || reference == null || !reference.Valid || pool <= 0 || raceSeconds == null || swimmerDistance == null)
                return null;

            int n = Math.Min(Math.Min(track.X.Count, track.Y.Count), Math.Min(Math.Min(track.Top.Count, track.Bottom.Count), track.Pan.Count));
            if (n < 3 || track.Step <= 0)
                return null;

            double f = (timestamp - track.Start) / track.Step;
            if (f < -0.5 || f > n - 0.5)
                return null;

            int now = (int)Math.Round(Math.Max(0, Math.Min(n - 1, f)));
            double tNow = raceSeconds(timestamp);
            if (double.IsNaN(tNow) || tNow <= 0)
                return null;

            double dSelf = swimmerDistance(tNow);
            if (double.IsNaN(dSelf) || dSelf <= 0)
                return null;

            double dRef = reference.DistanceAt(tNow);
            int lap = Lap(dSelf, pool);

            // The frames of this part in the same length: the place of the swimmer in the pool and in the image (X − Pan).
            List<double> places = new List<double>(), images = new List<double>();
            for (int i = 0; i < n; i++)
            {
                double t = raceSeconds(track.Start + (long)Math.Round(i * track.Step));
                double d = t > 0 ? swimmerDistance(t) : double.NaN;
                if (double.IsNaN(d) || d <= 0 || Lap(d, pool) != lap)
                    continue;

                places.Add(PoolPlace(d, pool));
                images.Add(track.X[i] - track.Pan[i]);
            }

            if (places.Count < 3)
                return null;

            // Image pixels per pool unit along the places of the length (its sign: the direction in the image).
            double scale = Slope(places, images);
            if (double.IsNaN(scale) || Math.Abs(scale) < 1e-3)
                return null;

            int poolDirection = lap % 2 == 0 ? 1 : -1;
            float halfHeight = Math.Max(2f, 0.5f * (track.Bottom[now] - track.Top[now]));
            float body = track.Body > 0 ? track.Body : 3.4f * halfHeight;
            Placement p = new Placement
            {
                SwimmerX = track.X[now],
                SwimmerY = track.Y[now],
                HalfHeight = halfHeight,
                Body = body,
                Direction = Math.Sign(scale) * poolDirection,
                Ahead = dRef - dSelf,
                Mirror = dRef > 0 && PoolDirection(dRef, pool) != poolDirection,
                Finished = tNow >= reference.FinishTime,
                FinishedFor = Math.Max(0, tNow - reference.FinishTime)
            };

            float slope = track.Slope != null && track.Slope.Count == track.X.Count ? track.Slope[now] / 1000f : 0f;
            double target = PoolPlace(dRef, pool);
            double selfPlace = PoolPlace(dSelf, pool);
            p.Side = Math.Sign((target - selfPlace) * scale);

            bool inside;
            double u = ImageAt(places, images, target, pool, out inside);
            if (double.IsNaN(u))
                return p;

            // u: the image of the body of the swimmer when his head was at the reference's place, so the water of that place (the head of
            // the reference) is half a body ahead of it; the body of a reference swimming the other way is half a body on the other side.
            float lead = (float)(HeadLead * body);
            float centre = (float)(u + track.Pan[now]);
            float head = centre + p.Direction * lead;
            p.X = p.Mirror ? head + p.Direction * lead : centre;
            p.Y = p.SwimmerY + slope * (p.X - p.SwimmerX);
            float headY = p.SwimmerY + slope * (head - p.SwimmerX);
            p.LineA = new PointF(head, headY - halfHeight);
            p.LineB = new PointF(head, headY + halfHeight);
            p.Side = Math.Abs(head - (p.SwimmerX + p.Direction * lead)) < 0.5f ? 0 : Math.Sign(head - (p.SwimmerX + p.Direction * lead));
            p.Inside = inside;
            p.Located = true;
            return p;
        }

        /// <summary>
        /// The reference with the calibration of the water of a fixed camera (pool units: place along the pool from the wall of the
        /// start, across the lane from its first rope): the line exactly across the lane at its head, the ghost and the swimmer at a
        /// body centre behind the head. Null if the calibration does not reach these places.
        /// </summary>
        public static Placement PlaceCalibrated(SwimCalibration calibration, double tNow, double dSelf, Curve reference, int pool)
        {
            if (calibration == null || reference == null || !reference.Valid || pool <= 0 || tNow <= 0 || double.IsNaN(dSelf) || dSelf <= 0)
                return null;

            double dRef = reference.DistanceAt(tNow);
            double width = calibration.LaneWidth > 0 ? calibration.LaneWidth : 2.5, mid = 0.5 * width;
            int poolDirection = PoolDirection(dSelf, pool);
            int refDirection = dRef > 0 ? PoolDirection(dRef, pool) : poolDirection;
            double self = PoolPlace(dSelf, pool), target = PoolPlace(dRef, pool);

            PointF swimmer, ahead, behind, top, bottom, head, ghost, a, b;
            if (!calibration.TryImage(self - poolDirection * CalibratedLead, mid, out swimmer) ||
                !calibration.TryImage(self + 0.5 * CalibratedBody, mid, out ahead) || !calibration.TryImage(self - 0.5 * CalibratedBody, mid, out behind) ||
                !calibration.TryImage(self, 0, out top) || !calibration.TryImage(self, width, out bottom) ||
                !calibration.TryImage(target, mid, out head) || !calibration.TryImage(target - refDirection * CalibratedLead, mid, out ghost) ||
                !calibration.TryImage(target, 0, out a) || !calibration.TryImage(target, width, out b))
                return null;

            PointF forward;
            if (!calibration.TryImage(self + poolDirection, mid, out forward))
                return null;

            PointF selfHead;
            if (!calibration.TryImage(self, mid, out selfHead))
                return null;

            return new Placement
            {
                Located = true,
                Inside = true,
                X = ghost.X,
                Y = ghost.Y,
                LineA = a,
                LineB = b,
                SwimmerX = swimmer.X,
                SwimmerY = swimmer.Y,
                HalfHeight = Math.Max(2f, 0.5f * Distance(top, bottom)),
                Body = Math.Max(8f, Distance(ahead, behind)),
                Direction = Math.Sign(forward.X - selfHead.X),
                Side = Math.Abs(head.X - selfHead.X) < 0.5f ? 0 : Math.Sign(head.X - selfHead.X),
                Ahead = dRef - dSelf,
                Mirror = refDirection != poolDirection,
                Finished = tNow >= reference.FinishTime,
                FinishedFor = Math.Max(0, tNow - reference.FinishTime)
            };
        }

        /// <summary>
        /// The transparency of the ghost (BGRA pixels, alpha 0 outside the frame): the colour of the water from the border of the part;
        /// what differs from it (the swimmer, the splash) is seen, the water barely; all fading towards the edges; tinted.
        /// </summary>
        public static void GhostAlpha(byte[] px, int stride, int pw, int ph, float alpha, Color tint)
        {
            // The water: the median colour of the pixels of the border.
            List<int> rs = new List<int>(), gs = new List<int>(), bs = new List<int>();
            for (int y = 0; y < ph; y++)
            {
                bool rowEdge = y == 0 || y == ph - 1;
                for (int x = 0; x < pw; x++)
                {
                    if (!rowEdge && x != 0 && x != pw - 1)
                        continue;
                    int i = y * stride + x * 4;
                    if (px[i + 3] == 0)
                        continue;
                    bs.Add(px[i]);
                    gs.Add(px[i + 1]);
                    rs.Add(px[i + 2]);
                }
            }

            int wr = Median(rs), wg = Median(gs), wb = Median(bs);
            const float share = 0.36f, strength = 0.82f;
            float cx = pw / 2f, cy = ph / 2f;
            for (int y = 0; y < ph; y++)
            {
                float fy = 1f - SmoothStep(0.5f, 1f, Math.Abs(y + 0.5f - cy) / cy);
                for (int x = 0; x < pw; x++)
                {
                    int i = y * stride + x * 4;
                    if (px[i + 3] == 0)
                        continue;

                    int b = px[i], g = px[i + 1], r = px[i + 2];
                    float fx = 1f - SmoothStep(0.62f, 1f, Math.Abs(x + 0.5f - cx) / cx);
                    float diff = Math.Abs(r - wr) + Math.Abs(g - wg) + Math.Abs(b - wb);
                    float s = Math.Max(0f, Math.Min(1f, (diff - 24f) / 90f));
                    float a = alpha * strength * fx * fy * (0.16f + 0.84f * s);

                    px[i] = (byte)Math.Round(b * (1 - share) + tint.B * share);
                    px[i + 1] = (byte)Math.Round(g * (1 - share) + tint.G * share);
                    px[i + 2] = (byte)Math.Round(r * (1 - share) + tint.R * share);
                    px[i + 3] = (byte)Math.Round(Math.Max(0f, Math.Min(1f, a)) * 255);
                }
            }
        }

        private static int Median(List<int> values)
        {
            if (values.Count == 0)
                return 0;
            values.Sort();
            return values[values.Count / 2];
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Math.Max(0f, Math.Min(1f, (x - edge0) / (edge1 - edge0)));
            return t * t * (3 - 2 * t);
        }

        private static float Distance(PointF a, PointF b)
        {
            return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        /// <summary>Least squares slope of the images over the places (NaN without spread).</summary>
        internal static double Slope(List<double> places, List<double> images)
        {
            int n = Math.Min(places.Count, images.Count);
            if (n < 2)
                return double.NaN;

            double mx = places.Take(n).Average(), my = images.Take(n).Average();
            double sxx = 0, sxy = 0;
            for (int i = 0; i < n; i++)
            {
                sxx += (places[i] - mx) * (places[i] - mx);
                sxy += (places[i] - mx) * (images[i] - my);
            }

            return sxx < 1e-9 ? double.NaN : sxy / sxx;
        }

        /// <summary>
        /// Place of the image (X − Pan) of a place of the pool from the frames of one length (places and images in the same order):
        /// interpolated between the two frames on each side of it, else along the line fitted to the frames of the last 3 pool units
        /// at the nearer end, at most a share of the pool past the ends (NaN if further, or the line has no slope).
        /// </summary>
        internal static double ImageAt(List<double> places, List<double> images, double target, int pool, out bool inside)
        {
            inside = false;
            int n = Math.Min(places.Count, images.Count);
            if (n < 2)
                return double.NaN;

            List<int> order = Enumerable.Range(0, n).OrderBy(i => places[i]).ToList();
            double lo = places[order[0]], hi = places[order[n - 1]];
            if (hi - lo < 1e-6)
                return double.NaN;

            if (target >= lo && target <= hi)
            {
                for (int k = 0; k < n - 1; k++)
                {
                    double p0 = places[order[k]], p1 = places[order[k + 1]];
                    if (target < p0 || target > p1)
                        continue;

                    inside = true;
                    if (p1 - p0 < 1e-9)
                        return 0.5 * (images[order[k]] + images[order[k + 1]]);
                    double s = (target - p0) / (p1 - p0);
                    return images[order[k]] + s * (images[order[k + 1]] - images[order[k]]);
                }
            }

            bool low = target < lo;
            double edge = low ? lo : hi;
            if (Math.Abs(target - edge) > ExtendShare * pool)
                return double.NaN;

            // The line of the last pool units at the nearer end (least squares), else of the whole length.
            List<int> near = order.Where(i => Math.Abs(places[i] - edge) <= 3.0).ToList();
            if (near.Count < 2)
                near = low ? order.Take(Math.Min(n, 5)).ToList() : order.Skip(Math.Max(0, n - 5)).ToList();

            double slope = Slope(near.Select(i => places[i]).ToList(), near.Select(i => images[i]).ToList());
            List<int> used = near;
            if (double.IsNaN(slope))
            {
                slope = Slope(places, images);
                used = order;
            }

            if (double.IsNaN(slope) || Math.Abs(slope) < 1e-6)
                return double.NaN;

            double mx = used.Average(i => places[i]), my = used.Average(i => images[i]);
            return my + slope * (target - mx);
        }
    }
}
