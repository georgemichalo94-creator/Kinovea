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
    /// (2.9.9) Signals of a technical reading: interpolation of the missing values, filters (median, moving mean,
    /// Savitzky–Golay) and percentiles, with the conventions of the prototypes (edges reflected; Savitzky–Golay fitted
    /// at the edges) so that both give the same events.
    /// </summary>
    public static class SwimSignal
    {
        /// <summary>The missing values (NaN) linearly between the known ones, the first and last known value at the ends.</summary>
        public static double[] Interp(double[] a)
        {
            int n = a.Length;
            double[] r = (double[])a.Clone();
            List<int> known = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (!double.IsNaN(a[i]))
                    known.Add(i);
            }

            if (known.Count < 2)
                return r;

            int k = 0;
            for (int i = 0; i < n; i++)
            {
                if (!double.IsNaN(a[i]))
                    continue;
                if (i < known[0])
                {
                    r[i] = a[known[0]];
                    continue;
                }

                if (i > known[known.Count - 1])
                {
                    r[i] = a[known[known.Count - 1]];
                    continue;
                }

                while (k + 1 < known.Count && known[k + 1] < i)
                    k++;
                int i0 = known[k], i1 = known[k + 1];
                r[i] = a[i0] + (a[i1] - a[i0]) * (i - i0) / (double)(i1 - i0);
            }

            return r;
        }

        /// <summary>Index of the value at i - offset with the edges reflected (d c b a | a b c d | d c b a).</summary>
        private static int Reflect(int i, int n)
        {
            if (n == 1)
                return 0;
            while (i < 0 || i >= n)
            {
                if (i < 0)
                    i = -i - 1;
                if (i >= n)
                    i = 2 * n - i - 1;
            }

            return i;
        }

        /// <summary>Median of a window of the given size around each value (edges reflected). NaN are kept out of the median.</summary>
        public static double[] Median(double[] a, int size)
        {
            int n = a.Length, h = size / 2;
            double[] r = new double[n];
            double[] w = new double[size];
            for (int i = 0; i < n; i++)
            {
                int m = 0;
                for (int j = -h; j < size - h; j++)
                {
                    double v = a[Reflect(i + j, n)];
                    if (!double.IsNaN(v))
                        w[m++] = v;
                }

                if (m == 0)
                {
                    r[i] = double.NaN;
                    continue;
                }

                Array.Sort(w, 0, m);
                r[i] = m % 2 == 1 ? w[m / 2] : 0.5 * (w[m / 2 - 1] + w[m / 2]);
            }

            return r;
        }

        /// <summary>Mean of a window of the given size around each value (edges reflected).</summary>
        public static double[] Mean(double[] a, int size)
        {
            int n = a.Length, h = size / 2;
            double[] r = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                for (int j = -h; j < size - h; j++)
                    s += a[Reflect(i + j, n)];
                r[i] = s / size;
            }

            return r;
        }

        /// <summary>
        /// Savitzky–Golay: the polynomial of the given order fitted to each window (odd length) and its value (deriv 0) or
        /// derivative (per sample); at the edges the polynomial of the first and the last window.
        /// </summary>
        public static double[] SavGol(double[] a, int window, int order, int deriv = 0)
        {
            int n = a.Length;
            double[] r = new double[n];
            if (n < window)
            {
                Array.Copy(a, r, n);
                return r;
            }

            int h = window / 2;
            for (int i = h; i < n - h; i++)
                r[i] = FitAt(a, i - h, window, order, h, deriv);
            for (int i = 0; i < h; i++)
                r[i] = FitAt(a, 0, window, order, i, deriv);
            for (int i = n - h; i < n; i++)
                r[i] = FitAt(a, n - window, window, order, i - (n - window), deriv);
            return r;
        }

        /// <summary>The polynomial fitted to a[start .. start+window) evaluated (or derived) at the position x of the window.</summary>
        private static double FitAt(double[] a, int start, int window, int order, int x, int deriv)
        {
            int p = order + 1;
            double[,] ata = new double[p, p];
            double[] atb = new double[p];
            for (int k = 0; k < window; k++)
            {
                double u = k - x;
                double[] pw = new double[p];
                pw[0] = 1;
                for (int j = 1; j < p; j++)
                    pw[j] = pw[j - 1] * u;
                for (int r = 0; r < p; r++)
                {
                    atb[r] += pw[r] * a[start + k];
                    for (int c = 0; c < p; c++)
                        ata[r, c] += pw[r] * pw[c];
                }
            }

            double[] coef = Solve(ata, atb);
            if (coef == null)
                return deriv == 0 ? a[start + x] : 0;

            // Value or derivative at u = 0.
            double f = 1;
            for (int j = 2; j <= deriv; j++)
                f *= j;
            return deriv < p ? coef[deriv] * f : 0;
        }

        private static double[] Solve(double[,] m, double[] b)
        {
            int n = b.Length;
            double[,] a = (double[,])m.Clone();
            double[] x = (double[])b.Clone();
            for (int c = 0; c < n; c++)
            {
                int piv = c;
                for (int r = c + 1; r < n; r++)
                {
                    if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c]))
                        piv = r;
                }

                if (Math.Abs(a[piv, c]) < 1e-12)
                    return null;
                if (piv != c)
                {
                    for (int k = 0; k < n; k++)
                    {
                        double t = a[c, k];
                        a[c, k] = a[piv, k];
                        a[piv, k] = t;
                    }

                    double tb = x[c];
                    x[c] = x[piv];
                    x[piv] = tb;
                }

                for (int r = 0; r < n; r++)
                {
                    if (r == c)
                        continue;
                    double f = a[r, c] / a[c, c];
                    if (f == 0)
                        continue;
                    for (int k = c; k < n; k++)
                        a[r, k] -= f * a[c, k];
                    x[r] -= f * x[c];
                }
            }

            double[] res = new double[n];
            for (int i = 0; i < n; i++)
                res[i] = x[i] / a[i, i];
            return res;
        }

        /// <summary>Percentile (0–100) with linear interpolation between the sorted values; NaN without values.</summary>
        public static double Percentile(IEnumerable<double> values, double q)
        {
            double[] s = values.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
            if (s.Length == 0)
                return double.NaN;
            double pos = (s.Length - 1) * q / 100.0;
            int lo = (int)Math.Floor(pos), hi = Math.Min(s.Length - 1, lo + 1);
            return s[lo] + (s[hi] - s[lo]) * (pos - lo);
        }

        public static double NanMedian(IEnumerable<double> values)
        {
            return Percentile(values, 50);
        }

        /// <summary>Mean of the known values (NaN when none).</summary>
        public static double NanMean(double a, double b)
        {
            if (double.IsNaN(a))
                return b;
            if (double.IsNaN(b))
                return a;
            return 0.5 * (a + b);
        }

        /// <summary>The angle at b of the segments b→a and b→c, in degrees (NaN when a point is missing).</summary>
        public static double Angle(double ax, double ay, double bx, double by, double cx, double cy)
        {
            double v1x = ax - bx, v1y = ay - by, v2x = cx - bx, v2y = cy - by;
            double n = Math.Sqrt(v1x * v1x + v1y * v1y) * Math.Sqrt(v2x * v2x + v2y * v2y) + 1e-9;
            double c = Math.Max(-1, Math.Min(1, (v1x * v2x + v1y * v2y) / n));
            return Math.Acos(c) * 180 / Math.PI;
        }
    }

    /// <summary>(2.9.9) Flip turn (somersault, the feet touch the wall) or open turn (the hands touch: breaststroke, butterfly, changes of style).</summary>
    public enum SwimTurnKind
    {
        Unknown,
        Flip,
        Open
    }

    /// <summary>(2.9.9) The events of a turn found in the technical readings, in seconds of the video (null when not found).</summary>
    public sealed class SwimTurnEvents
    {
        /// <summary>Closest approach of the body to the wall (pool frame).</summary>
        public double? Extreme;

        /// <summary>+1: the swimmer leaves the wall towards the right of the image, −1: towards the left.</summary>
        public int Sign;

        /// <summary>Flip or open turn; KindFromVideo: found from the video (no style given), to be checked.</summary>
        public SwimTurnKind Kind;
        public bool KindFromVideo;

        /// <summary>Contact with the wall (W): the feet for a flip turn, the hands for an open turn.</summary>
        public double? Wall;

        /// <summary>Feet reach the wall; PlantFromTurn: not seen at the ankles, taken just before the turn of the body.</summary>
        public double? Plant;
        public bool PlantFromTurn;

        /// <summary>Open turn: the hands touch the wall.</summary>
        public double? Touch;

        /// <summary>Flip turn: the head starts to go down (O).</summary>
        public double? RotationStart;

        /// <summary>Feet leave the wall.</summary>
        public double? Pushoff;

        /// <summary>Head breaks the surface; BreakoutBySurface: found with the band of the lane rope (else from the head alone).</summary>
        public double? Breakout;
        public bool BreakoutBySurface;

        /// <summary>The breakout found with the rope line of the long lines (SwimTechFrame.Lines) when the band is not the surface.</summary>
        public bool BreakoutByLine;

        /// <summary>Breaststroke pull-out: the hands sweep back to the thighs under water.</summary>
        public double? PullDown;

        /// <summary>Dolphin kicks (deepest bend of the knees) between the push-off and the breakout (before the pull-down).</summary>
        public List<double> Kicks = new List<double>();

        /// <summary>Kicks after the pull-down of a breaststroke pull-out (the breaststroke kick).</summary>
        public List<double> LegKicks = new List<double>();

        /// <summary>Arm strokes (furthest reach of a hand) before the turn and after the breakout; the last one before and the first one after.</summary>
        public List<double> StrokesIn = new List<double>();
        public List<double> StrokesOut = new List<double>();

        /// <summary>
        /// (2.9.9) The pulls: where each reach of the hands ends (the hands start to pull back), the stroke of a cycle of breaststroke
        /// and butterfly (the glide is before it); pulls closer than PullMinGap merged (the later kept).
        /// </summary>
        public List<double> PullsIn = new List<double>();
        public List<double> PullsOut = new List<double>();
        public double? LastStroke;
        public double? FirstStroke;

        /// <summary>Body length in the image (shoulder–hip–ankle, pixels of the reading).</summary>
        public double BodyLength;

        /// <summary>Remarks for the review (in Greek), empty when all was found.</summary>
        public List<string> Notes = new List<string>();
    }

    /// <summary>
    /// (2.9.9) Events of a turn from the reading of the side underwater camera that follows the swimmer: the turn is the
    /// closest approach to the wall of the body in the frame of the pool (image position minus the pan of the camera);
    /// push-off when the speed away from the wall reaches 90% of its first peak; contact of a flip turn when the ankles
    /// reach the wall, of an open turn when the front of the swimmer stops at the wall; rotation when the hips start to
    /// fold; kicks at the deepest bends of the knees; breakout when the head comes up to the band of the lane rope at the
    /// surface; strokes at the furthest reach of the hands.
    /// </summary>
    public static class SwimTurnReader
    {
        public const double VisibilityMin = 0.3;
        public const double PushoffFraction = 0.9;
        public const double PushoffWindow = 1.6;
        public const double PushoffDrop = 0.03;
        public const double PushoffDropWindow = 0.25;
        public const double PushoffMinSpeed = 1.5;

        /// <summary>
        /// The first peak of a flip turn's push-off can be lower in the camera's frame (bubbles at the wall move the
        /// background): 0.8 body lengths/s. Open turns keep 1.5 (the hips drift ~1 body length/s while the body turns).
        /// </summary>
        public const double PushoffMinSpeedFlip = 0.8;
        public const double PlantDistance = 0.12;
        public const double PlantWindow = 1.2;
        public const double PlantBeforeTurn = 0.4;
        public const double PlantBeforeExtreme = 0.2;
        public const double DolphinMaxAngle = 148;
        public const double KickProminence = 15;
        public const double UprightShoulders = -0.25;
        public const double TouchSpeedFraction = 0.2;
        public const double TouchHold = 0.2;
        public const double TouchLead = 0.1;
        public const double RotationOpenAngle = 160;
        public const double SurfaceMinDepth = 0.3;
        public const double BreakoutLevelWindow = 2.0;
        public const double BreakoutLevelTolerance = 0.03;
        public const double BreakoutRise = 0.25;
        public const double PullDownBack = -0.2;
        public const double PullDownHold = 0.4;
        public const double BreaststrokeDolphinAfter = 1.1;
        public const double FlutterGapFraction = 0.75;

        private sealed class Signals
        {
            public int N;
            public double Fps;
            public double[] T, Pan, Cx, Cy, Water;
            public bool[] Present, InWater;
            public double[] NoseX, NoseY, ShX, ShY, HipX, HipY, KnX, KnY, AnkX, AnkY;
            public double[][] WrX;
            public double[] KneeAngle, HipAngle, BoxX0, BoxX1;
            public double[] X, Xs;
            public double L;
            public int A, B;
        }

        /// <summary>
        /// The events of the turn in an underwater reading. kind: from the style of the race (breaststroke, butterfly and
        /// the changes of style of the medley: open; freestyle, backstroke: flip), Unknown to find it from the video.
        /// </summary>
        public static SwimTurnEvents Find(SwimTechReading uw, SwimTurnKind kind = SwimTurnKind.Unknown, string style = null)
        {
            SwimTurnEvents ev = new SwimTurnEvents();
            if (uw == null || uw.Frames.Count < 60)
            {
                ev.Notes.Add("λίγα καρέ στην υποβρύχια ανάγνωση");
                return ev;
            }

            Signals s = Read(uw);
            ev.BodyLength = s.L;
            if (!Segment(s))
            {
                ev.Notes.Add("ο κολυμβητής δεν φαίνεται αρκετά στην υποβρύχια κάμερα");
                return ev;
            }

            int i0;
            if (!Turn(s, out i0, out ev.Sign))
            {
                ev.Notes.Add("δεν βρέθηκε η αλλαγή κατεύθυνσης στον τοίχο");
                return ev;
            }

            ev.Extreme = s.T[i0];
            int k = Pushoff(s, i0, ev.Sign, kind == SwimTurnKind.Flip ? PushoffMinSpeedFlip : PushoffMinSpeed);
            double pushoff = s.T[k];
            bool fromTurn;
            ev.Plant = Plant(s, ev.Sign, pushoff, i0, out fromTurn);
            if (kind == SwimTurnKind.Unknown)
            {
                kind = Upright(s, i0, pushoff) ? SwimTurnKind.Open : SwimTurnKind.Flip;
                ev.KindFromVideo = true;
                if (kind == SwimTurnKind.Flip)
                {
                    k = Pushoff(s, i0, ev.Sign, PushoffMinSpeedFlip);
                    pushoff = s.T[k];
                    ev.Plant = Plant(s, ev.Sign, pushoff, i0, out fromTurn);
                }
            }

            ev.Pushoff = pushoff;
            ev.PlantFromTurn = fromTurn;

            ev.Kind = kind;
            if (kind == SwimTurnKind.Open)
            {
                ev.Touch = TouchBySpeed(s, i0, ev.Sign, pushoff) ?? Touch(s, i0, ev.Sign);
                ev.Wall = ev.Touch;
                if (!ev.Touch.HasValue)
                    ev.Notes.Add("δεν βρέθηκε η επαφή των χεριών στον τοίχο");
            }
            else
            {
                ev.Wall = ev.Plant;
                if (!ev.Plant.HasValue)
                    ev.Notes.Add("δεν φαίνονται τα πόδια στον τοίχο");
                else if (fromTurn)
                    ev.Notes.Add("η επαφή των ποδιών από την αλλαγή κατεύθυνσης (δεν φαίνονται καθαρά τα πόδια)");
                ev.RotationStart = RotationStart(s, i0, ev.Plant);
                if (!ev.RotationStart.HasValue)
                    ev.Notes.Add("δεν βρέθηκε η αρχή της περιστροφής");
            }

            if (ev.KindFromVideo)
                ev.Notes.Add(kind == SwimTurnKind.Open ? "ανοιχτή στροφή κατά το βίντεο (το στυλ το επιβεβαιώνει)" : "ανάποδη στροφή κατά το βίντεο (το στυλ το επιβεβαιώνει)");

            double? breakout = AfterPushoff(uw, s, ev, pushoff, style);

            double reference = ev.RotationStart ?? ev.Wall ?? s.T[i0];
            ev.StrokesIn = Strokes(s, reference - 6, reference - 0.15, -ev.Sign);
            if (ev.StrokesIn.Count > 0)
                ev.LastStroke = ev.StrokesIn[ev.StrokesIn.Count - 1];
            // The cycles of breaststroke and butterfly: not the reach of the touch (the last 0.5 s before the wall). Breaststroke (and
            // the breaststroke of a medley before an open turn, when its kicks are found): the pulls from the kicks.
            List<double> breastIn = style == "br" || (style == "im" && kind == SwimTurnKind.Open)
                ? BreastPulls(s, reference - 6, reference - PullBeforeWall, -ev.Sign) : null;
            ev.PullsIn = breastIn != null && (style == "br" || breastIn.Count >= 2)
                ? breastIn : CyclePulls(s, reference - 6, reference - PullBeforeWall, -ev.Sign);
            return ev;
        }

        /// <summary>
        /// After the push-off of a turn or the entry of a start: the breakout (the surface band, the rope line, else the head), the
        /// dolphin kicks (and the pull-out of breaststroke), the strokes and the pulls after the breakout. Returns the breakout.
        /// </summary>
        private static double? AfterPushoff(SwimTechReading uw, Signals s, SwimTurnEvents ev, double pushoff, string style)
        {
            double[] surface = Surface(uw, s);
            double? breakout = surface != null ? BreakoutBySurface(s, pushoff, surface) : null;
            ev.BreakoutBySurface = breakout.HasValue;
            if (!breakout.HasValue)
            {
                // The band is not the surface (e.g. a camera that tilts with the swimmer outdoors): the rope line followed
                // over time from the long lines of the images.
                double[] line = LineSurface(uw, s);
                breakout = line != null ? BreakoutBySurface(s, pushoff, line) : null;
                ev.BreakoutByLine = breakout.HasValue;
            }

            if (!breakout.HasValue)
                breakout = BreakoutByHead(s, pushoff);
            ev.Breakout = breakout;
            if (!breakout.HasValue)
                ev.Notes.Add("δεν βρέθηκε η έξοδος από το νερό");
            else if (ev.BreakoutByLine)
                ev.Notes.Add("η έξοδος από τη γραμμή του σχοινιού (η κάμερα γέρνει: έλεγξέ την)");
            else if (!ev.BreakoutBySurface)
                ev.Notes.Add("η έξοδος χωρίς τη γραμμή της επιφάνειας (κατά προσέγγιση: έλεγξέ την)");

            double kickEnd = breakout ?? pushoff + 8;
            ev.PullDown = PullDown(s, pushoff, kickEnd, ev.Sign);
            List<KeyValuePair<double, double>> found = Kicks(s, pushoff + 0.1, kickEnd);
            List<KeyValuePair<double, double>> dolphin = found.Where(q => !ev.PullDown.HasValue || q.Key < ev.PullDown.Value).ToList();
            List<KeyValuePair<double, double>> legs = found.Where(q => ev.PullDown.HasValue && q.Key >= ev.PullDown.Value).ToList();
            if (ev.PullDown.HasValue)
            {
                // Breaststroke pull-out: one dolphin kick (the one nearest 1.1 s after the push-off) and one breaststroke kick
                // (the deepest bend of the knees).
                if (dolphin.Count > 0)
                    dolphin = new List<KeyValuePair<double, double>> { dolphin.OrderBy(q => Math.Abs(q.Key - (pushoff + BreaststrokeDolphinAfter))).First() };
                if (legs.Count > 0)
                    legs = new List<KeyValuePair<double, double>> { legs.OrderBy(q => q.Value).First() };
            }
            else if (dolphin.Count >= 3)
            {
                // The dolphin kicks are regular: a kick much sooner than the usual interval is the flutter of the breakout.
                double[] gaps = new double[dolphin.Count - 1];
                for (int i = 0; i < gaps.Length; i++)
                    gaps[i] = dolphin[i + 1].Key - dolphin[i].Key;
                double usual = SwimSignal.Percentile(gaps.Take(Math.Max(2, gaps.Length - 1)), 50);
                for (int i = 0; i < gaps.Length; i++)
                {
                    if (gaps[i] < FlutterGapFraction * usual)
                    {
                        dolphin = dolphin.Take(i + 1).ToList();
                        break;
                    }
                }
            }

            ev.Kicks.AddRange(dolphin.Select(q => q.Key));
            ev.LegKicks.AddRange(legs.Select(q => q.Key));

            if (breakout.HasValue)
            {
                ev.StrokesOut = Strokes(s, breakout.Value - 0.2, breakout.Value + 10, ev.Sign);
                if (ev.StrokesOut.Count > 0)
                    ev.FirstStroke = ev.StrokesOut[0];
                // Breaststroke (and the breaststroke after the pull-out of a medley): the pulls from the kicks, from the kick of the
                // pull-out on (the first pull is at the breakout, which can be found late: a002 of a second reading 24.04 for 22.7).
                bool breast = style == "br" || (style == "im" && ev.PullDown.HasValue);
                double from = ev.LegKicks.Count > 0 ? Math.Min(breakout.Value - 0.3, ev.LegKicks[ev.LegKicks.Count - 1] + 0.3) : breakout.Value - 0.6;
                ev.PullsOut = breast ? BreastPulls(s, from, breakout.Value + 10, ev.Sign)
                                     : CyclePulls(s, breakout.Value - 0.3, breakout.Value + 10, ev.Sign);
            }

            return breakout;
        }

        /// <summary>
        /// (2.9.9) Events of a start in a reading of the side underwater camera that follows the swimmer (the analyst gives the start
        /// signal): the entry (the swimmer first seen under water for good), then as after the push-off of a turn the dolphin kicks, the
        /// breakout and the strokes. Wall, plant and rotation: none; Pushoff: the entry. Sign from the motion in the frame of the pool.
        /// </summary>
        public static SwimTurnEvents FindStart(SwimTechReading uw, string style = null)
        {
            SwimTurnEvents ev = new SwimTurnEvents();
            if (uw == null || uw.Frames.Count < 60)
            {
                ev.Notes.Add("λίγα καρέ στην υποβρύχια ανάγνωση");
                return ev;
            }

            Signals s = Read(uw);
            ev.BodyLength = s.L;
            if (!Segment(s))
            {
                ev.Notes.Add("ο κολυμβητής δεν φαίνεται αρκετά στην υποβρύχια κάμερα");
                return ev;
            }

            // The direction of the swim: the frame of the pool in the first and the last second of the swimmer under water.
            int k = (int)Math.Max(3, s.Fps);
            Func<int, int, double> mean = (from, to) =>
            {
                double sum = 0;
                int n = 0;
                for (int i = Math.Max(s.A, from); i <= Math.Min(s.B, to); i++)
                {
                    if (double.IsNaN(s.X[i]))
                        continue;
                    sum += s.X[i];
                    n++;
                }

                return n > 0 ? sum / n : double.NaN;
            };
            double x0 = mean(s.A, s.A + k), x1 = mean(s.B - k, s.B);
            if (double.IsNaN(x0) || double.IsNaN(x1))
            {
                ev.Notes.Add("δεν βρέθηκε η κατεύθυνση του κολυμβητή");
                return ev;
            }

            ev.Sign = x1 >= x0 ? 1 : -1;
            double entry = s.T[s.A];
            ev.Pushoff = entry;
            AfterPushoff(uw, s, ev, entry, style);
            return ev;
        }

        /// <summary>
        /// The head at the backstroke flags (5 m) before and after the wall, from the images of the external side camera (on the
        /// same pole, just above the underwater one; their times in seconds of this reading, the two videos synchronised). For
        /// each pass of the camera through the flags' plane (SwimFlagReader.Choose), the column of the string at a quarter of the
        /// height is the column of that plane: mapped into the underwater image (Refraction from the centre), the head crosses it
        /// in the swimming direction within 1.5 s of the pass. Checked by eye on 5 turns: within 0.1-0.2 s.
        /// </summary>
        public static SwimFlagHead HeadAtFlags(SwimTechReading uw, SwimTurnEvents ev, IList<SwimFlagFrame> frames, int extWidth)
        {
            SwimFlagHead head = new SwimFlagHead();
            if (uw == null || ev == null || frames == null || frames.Count < 10 || extWidth <= 0 || uw.Width <= 0 ||
                !ev.Pushoff.HasValue || (ev.Sign != 1 && ev.Sign != -1) || uw.Frames.Count < 60)
                return head;

            List<SwimFlagPass> passes = SwimFlagReader.Passes(frames);
            head.Passes = passes.Count;
            if (passes.Count == 0)
                return head;

            // The head's column where the swimmer is seen under water, the gaps filled, a median of 5 images.
            Signals s = Read(uw);
            double[] hx = new double[s.N];
            for (int i = 0; i < s.N; i++)
                hx[i] = s.Present[i] && s.InWater[i] ? s.NoseX[i] : double.NaN;
            hx = SwimSignal.Median(SwimSignal.Interp(hx), 5);
            if (hx.All(double.IsNaN))
                return head;

            List<SwimFlagFrame> seen = frames.Where(f => !float.IsNaN(f.X25)).OrderBy(f => f.T).ToList();
            // A start (no wall): only the head at 5 m after the entry.
            foreach (bool approach in ev.Wall.HasValue ? new[] { true, false } : new[] { false })
            {
                SwimFlagPass pass = SwimFlagReader.Choose(passes, approach, ev.Wall ?? ev.Pushoff.Value, ev.Pushoff.Value, ev.Sign, extWidth);
                if (pass == null)
                    continue;

                // > 0: the head is past the plane in the swimming direction (towards the wall on the approach).
                int direction = approach ? -ev.Sign : ev.Sign;
                double? cross = null;
                double prevT = double.NaN, prevD = double.NaN;
                foreach (SwimFlagFrame f in seen.Where(f => Math.Abs(f.T - pass.T) <= 1.5))
                {
                    double plane = uw.Width / 2.0 + SwimFlagReader.Refraction * (f.X25 - extWidth / 2.0) * uw.Width / extWidth;
                    double d = (At(s.T, hx, f.T) - plane) * direction;
                    if (!double.IsNaN(prevD) && !double.IsNaN(d) && f.T - prevT <= 0.35 && prevD < 0 && d >= 0)
                    {
                        cross = prevT + (f.T - prevT) * (-prevD) / (d - prevD + 1e-9);
                        break;
                    }

                    prevT = f.T;
                    prevD = d;
                }

                if (approach)
                {
                    head.InPass = pass;
                    head.In = cross;
                }
                else
                {
                    head.OutPass = pass;
                    head.Out = cross;
                }
            }

            return head;
        }

        /// <summary>The value of a signal at time t (linear between the images, the first and last value outside them).</summary>
        private static double At(double[] t, double[] v, double at)
        {
            int n = t.Length;
            if (n == 0)
                return double.NaN;
            if (at <= t[0])
                return v[0];
            if (at >= t[n - 1])
                return v[n - 1];
            int i = Array.BinarySearch(t, at);
            if (i >= 0)
                return v[i];
            i = ~i;
            double w = (at - t[i - 1]) / Math.Max(1e-9, t[i] - t[i - 1]);
            return v[i - 1] + (v[i] - v[i - 1]) * w;
        }

        private static Signals Read(SwimTechReading r)
        {
            Signals s = new Signals();
            int n = r.Frames.Count;
            s.N = n;
            s.Fps = r.Fps > 0 ? r.Fps : 30;
            s.T = new double[n];
            s.Pan = new double[n];
            s.Cx = new double[n];
            s.Cy = new double[n];
            s.Water = new double[n];
            s.Present = new bool[n];
            s.NoseX = Nan(n); s.NoseY = Nan(n); s.ShX = Nan(n); s.ShY = Nan(n); s.HipX = Nan(n); s.HipY = Nan(n);
            s.KnX = Nan(n); s.KnY = Nan(n); s.AnkX = Nan(n); s.AnkY = Nan(n);
            s.WrX = new[] { Nan(n), Nan(n) };
            s.KneeAngle = Nan(n);
            s.HipAngle = Nan(n);
            s.BoxX0 = Nan(n);
            s.BoxX1 = Nan(n);
            double pan = 0;
            bool anyWater = false;
            for (int i = 0; i < n; i++)
            {
                SwimTechFrame f = r.Frames[i];
                s.T[i] = f.T;
                pan += float.IsNaN(f.CameraDx) ? 0 : f.CameraDx;
                s.Pan[i] = pan;
                s.Present[i] = f.Box != null;
                s.Cx[i] = f.Box != null ? f.Box.CenterX : double.NaN;
                s.Cy[i] = f.Box != null ? f.Box.CenterY : double.NaN;
                s.BoxX0[i] = f.Box != null ? f.Box.X0 : double.NaN;
                s.BoxX1[i] = f.Box != null ? f.Box.X1 : double.NaN;
                s.Water[i] = f.Water;
                anyWater |= f.Water > 0 || f.Bubbles > 0 || f.Body > 0;
                float[] p = f.Pose;
                if (p == null || p.Length < SwimJoints.Count * 3)
                    continue;
                Func<int, int, double> J = (j, c) => p[j * 3 + 2] >= VisibilityMin ? p[j * 3 + c] : double.NaN;
                s.NoseX[i] = J(SwimJoints.Nose, 0);
                s.NoseY[i] = J(SwimJoints.Nose, 1);
                s.ShX[i] = SwimSignal.NanMean(J(SwimJoints.LeftShoulder, 0), J(SwimJoints.RightShoulder, 0));
                s.ShY[i] = SwimSignal.NanMean(J(SwimJoints.LeftShoulder, 1), J(SwimJoints.RightShoulder, 1));
                s.HipX[i] = SwimSignal.NanMean(J(SwimJoints.LeftHip, 0), J(SwimJoints.RightHip, 0));
                s.HipY[i] = SwimSignal.NanMean(J(SwimJoints.LeftHip, 1), J(SwimJoints.RightHip, 1));
                s.KnX[i] = SwimSignal.NanMean(J(SwimJoints.LeftKnee, 0), J(SwimJoints.RightKnee, 0));
                s.KnY[i] = SwimSignal.NanMean(J(SwimJoints.LeftKnee, 1), J(SwimJoints.RightKnee, 1));
                s.AnkX[i] = SwimSignal.NanMean(J(SwimJoints.LeftAnkle, 0), J(SwimJoints.RightAnkle, 0));
                s.AnkY[i] = SwimSignal.NanMean(J(SwimJoints.LeftAnkle, 1), J(SwimJoints.RightAnkle, 1));
                s.WrX[0][i] = J(SwimJoints.LeftWrist, 0);
                s.WrX[1][i] = J(SwimJoints.RightWrist, 0);
                double kl = SwimSignal.Angle(J(SwimJoints.LeftHip, 0), J(SwimJoints.LeftHip, 1), J(SwimJoints.LeftKnee, 0), J(SwimJoints.LeftKnee, 1), J(SwimJoints.LeftAnkle, 0), J(SwimJoints.LeftAnkle, 1));
                double kr = SwimSignal.Angle(J(SwimJoints.RightHip, 0), J(SwimJoints.RightHip, 1), J(SwimJoints.RightKnee, 0), J(SwimJoints.RightKnee, 1), J(SwimJoints.RightAnkle, 0), J(SwimJoints.RightAnkle, 1));
                s.KneeAngle[i] = SwimSignal.NanMean(kl, kr);
                double hl = SwimSignal.Angle(J(SwimJoints.LeftShoulder, 0), J(SwimJoints.LeftShoulder, 1), J(SwimJoints.LeftHip, 0), J(SwimJoints.LeftHip, 1), J(SwimJoints.LeftKnee, 0), J(SwimJoints.LeftKnee, 1));
                double hr = SwimSignal.Angle(J(SwimJoints.RightShoulder, 0), J(SwimJoints.RightShoulder, 1), J(SwimJoints.RightHip, 0), J(SwimJoints.RightHip, 1), J(SwimJoints.RightKnee, 0), J(SwimJoints.RightKnee, 1));
                s.HipAngle[i] = SwimSignal.NanMean(hl, hr);
            }

            // The camera under water: mostly water around (out of the water the image is the hall).
            s.InWater = new bool[n];
            if (!anyWater)
            {
                for (int i = 0; i < n; i++)
                    s.InWater[i] = true;
            }
            else
            {
                double[] w = SwimSignal.Median(s.Water, (int)s.Fps + 1);
                for (int i = 0; i < n; i++)
                    s.InWater[i] = w[i] >= 0.85;
            }

            s.X = new double[n];
            for (int i = 0; i < n; i++)
            {
                double hx = double.IsNaN(s.HipX[i]) ? s.Cx[i] : s.HipX[i];
                s.X[i] = s.Present[i] ? hx - s.Pan[i] : double.NaN;
            }

            List<double> lengths = new List<double>(), all = new List<double>();
            for (int i = 0; i < n; i++)
            {
                double l = Dist(s.ShX[i], s.ShY[i], s.HipX[i], s.HipY[i]) + Dist(s.HipX[i], s.HipY[i], s.AnkX[i], s.AnkY[i]);
                if (double.IsNaN(l))
                    continue;
                all.Add(l);
                if (s.Present[i] && s.InWater[i])
                    lengths.Add(l);
            }

            s.L = lengths.Count > 10 ? SwimSignal.NanMedian(lengths) : SwimSignal.NanMedian(all);
            return s;
        }

        private static double[] Nan(int n)
        {
            double[] a = new double[n];
            for (int i = 0; i < n; i++)
                a[i] = double.NaN;
            return a;
        }

        private static double Dist(double ax, double ay, double bx, double by)
        {
            return Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
        }

        /// <summary>The longest part of the reading with the swimmer under water (gaps of up to one second joined).</summary>
        private static bool Segment(Signals s)
        {
            int g = (int)s.Fps;
            List<int[]> runs = new List<int[]>();
            int start = -1;
            for (int i = 0; i <= s.N; i++)
            {
                bool on = i < s.N && s.Present[i] && s.InWater[i];
                if (on && start < 0)
                    start = i;
                if (!on && start >= 0)
                {
                    if (runs.Count > 0 && start - runs[runs.Count - 1][1] <= g)
                        runs[runs.Count - 1][1] = i - 1;
                    else
                        runs.Add(new[] { start, i - 1 });
                    start = -1;
                }
            }

            if (runs.Count == 0)
                return false;
            int[] best = runs.OrderByDescending(r => r[1] - r[0]).First();
            s.A = best[0];
            s.B = best[1];
            return s.B - s.A > 2 * s.Fps;
        }

        private static bool Turn(Signals s, out int i0, out int sign)
        {
            i0 = -1;
            sign = 1;
            s.Xs = SwimSignal.SavGol(SwimSignal.Median(SwimSignal.Interp(s.X), 9), 15, 2);
            int a = s.A, b = s.B;
            int imin = a, imax = a;
            for (int i = a; i <= b; i++)
            {
                if (s.Xs[i] < s.Xs[imin])
                    imin = i;
                if (s.Xs[i] > s.Xs[imax])
                    imax = i;
            }

            Func<int, double> travel = i => Math.Min(Math.Abs(s.Xs[i] - s.Xs[a]), Math.Abs(s.Xs[i] - s.Xs[b]));
            double best = -1;
            foreach (int[] c in new[] { new[] { imin, 1 }, new[] { imax, -1 } })
            {
                if (!(a + s.Fps < c[0] && c[0] < b - s.Fps))
                    continue;
                double tr = travel(c[0]);
                if (tr > best)
                {
                    best = tr;
                    i0 = c[0];
                    sign = c[1];
                }
            }

            return i0 >= 0;
        }

        /// <summary>
        /// Push-off: the first clear peak of the speed away from the wall after the turn (at least 1.5 body lengths per
        /// second: the hips also drift from the wall while an open turn rotates; and the speed drops by 3% within 0.25 s
        /// after it; else the highest speed within 1.6 s), then the first
        /// image with 90% of that speed. The first peak and not the highest: the pan of the camera can add a later one.
        /// </summary>
        private static int Pushoff(Signals s, int i0, int sign, double minSpeed)
        {
            double[] d = SwimSignal.SavGol(s.Xs, 9, 2, 1);
            double[] v = new double[s.N];
            for (int i = 0; i < s.N; i++)
                v[i] = d[i] * s.Fps * sign;
            int j1 = Math.Min(s.N - 1, i0 + (int)(PushoffWindow * s.Fps));
            int nd = (int)(PushoffDropWindow * s.Fps);
            int pk = -1;
            for (int i = i0 + 1; i < j1 && pk < 0; i++)
            {
                if (v[i] < v[i - 1] || v[i] < v[i + 1] || v[i] < minSpeed * s.L)
                    continue;
                double low = v[i];
                for (int q = i; q <= Math.Min(s.N - 1, i + nd); q++)
                    low = Math.Min(low, v[q]);
                if (low <= (1 - PushoffDrop) * v[i])
                    pk = i;
            }

            if (pk < 0)
            {
                pk = i0;
                for (int i = i0; i <= j1; i++)
                {
                    if (v[i] > v[pk])
                        pk = i;
                }
            }

            double vmax = v[pk];
            for (int i = i0; i <= pk; i++)
            {
                if (v[i] >= PushoffFraction * vmax)
                    return i;
            }

            return pk;
        }

        /// <summary>
        /// Contact of the feet: the wall is where the ankles are closest to it (10th percentile in the frame of the pool)
        /// between the turn (from 0.4 s before the closest approach of the body, at most 1.2 s before the push-off) and the
        /// push-off; the feet reach it at the first image within 0.12 body lengths of it. The knees bend on the wall for
        /// about 0.1 s after the contact: an ankle contact long before the turn of the body is noise of the ankles
        /// (bubbles), and then the contact is taken just before the turn (fromTurn).
        /// </summary>
        private static double? Plant(Signals s, int sign, double pushoff, int i0, out bool fromTurn)
        {
            fromTurn = false;
            double turn = s.T[i0];
            double from = Math.Max(pushoff - PlantWindow, turn - PlantBeforeTurn);
            List<int> m = new List<int>();
            for (int i = 0; i < s.N; i++)
            {
                if (s.T[i] > from && s.T[i] < pushoff && !double.IsNaN(s.AnkX[i]))
                    m.Add(i);
            }

            if (m.Count < 5)
                return null;
            double wall = SwimSignal.Percentile(m.Select(i => s.AnkX[i] - s.Pan[i]), sign == 1 ? 10 : 90);
            double? found = null;
            foreach (int i in m)
            {
                if ((s.AnkX[i] - s.Pan[i] - wall) * sign < PlantDistance * s.L)
                {
                    found = s.T[i];
                    break;
                }
            }

            if (!found.HasValue || turn - found.Value > PlantBeforeExtreme)
            {
                fromTurn = true;
                return turn - 0.05;
            }

            return found;
        }

        /// <summary>
        /// Open turn from the video: the shoulders come well above the hips at the wall (the body upright, between 1.2 s
        /// before the turn and the push-off).
        /// </summary>
        private static bool Upright(Signals s, int i0, double pushoff)
        {
            double[] d = new double[s.N];
            for (int i = 0; i < s.N; i++)
                d[i] = (s.ShY[i] - s.HipY[i]) / s.L;
            if (d.All(double.IsNaN))
                return false;
            d = SwimSignal.Median(SwimSignal.Interp(d), 5);
            int count = 0;
            double min = double.MaxValue;
            for (int i = 0; i < s.N; i++)
            {
                if (s.T[i] >= s.T[i0] - 1.2 && s.T[i] <= pushoff)
                {
                    count++;
                    min = Math.Min(min, d[i]);
                }
            }

            return count >= 5 && min < UprightShoulders;
        }

        /// <summary>
        /// Open turn: the hands touch the wall when the body starts its last deceleration. The hips (frame of the pool) stop
        /// (speed towards the wall at most 0.1 body lengths/s) at t0; the approach speed is the median of [t0−2, t0−1] s; the
        /// touch is the later of the last image before t0 still at 80% of it and the last speed peak before t0 (at least half
        /// of it: the final glide of a breaststroke approach is slower than the approach).
        /// </summary>
        private static double? TouchBySpeed(Signals s, int i0, int sign, double pushoff)
        {
            double[] d = SwimSignal.SavGol(s.Xs, 15, 2, 1);
            double[] v = new double[s.N];
            for (int i = 0; i < s.N; i++)
                v[i] = -d[i] * s.Fps * sign / s.L;
            double te = s.T[i0];
            int stop = -1;
            for (int i = 0; i < s.N; i++)
            {
                if (s.T[i] >= te - 2.5 && s.T[i] <= pushoff && v[i] <= 0.1 && s.T[i] > te - 1.5)
                {
                    stop = i;
                    break;
                }
            }

            if (stop < 0)
                return null;
            List<double> approach = new List<double>();
            for (int i = 0; i < s.N; i++)
            {
                if (s.T[i] >= s.T[stop] - 2.0 && s.T[i] <= s.T[stop] - 1.0)
                    approach.Add(v[i]);
            }

            if (approach.Count < 5)
                return null;
            double va = SwimSignal.Percentile(approach, 50);
            if (!(va > 0.3))
                return null;
            int back = (int)(2 * s.Fps), k1 = -1, k2 = -1;
            for (int i = stop; i > Math.Max(0, stop - back) && k1 < 0; i--)
            {
                if (v[i] >= 0.8 * va)
                    k1 = i;
            }

            for (int i = stop - 1; i > Math.Max(1, stop - back) && k2 < 0; i--)
            {
                if (v[i] >= v[i - 1] && v[i] >= v[i + 1] && v[i] >= 0.5 * va)
                    k2 = i;
            }

            int k = Math.Max(k1, k2);
            return k >= 0 ? s.T[k] : (double?)null;
        }

        /// <summary>
        /// Open turn: the front of the swimmer (the edge of the box on the side of the wall: the hands ahead) stops at the
        /// wall. From the fastest approach of that edge (in the 2.5 s before the turn), the first time its speed stays under
        /// 20% of that peak for 0.2 s; the hands touched 0.1 s before it stops.
        /// </summary>
        private static double? Touch(Signals s, int i0, int sign)
        {
            double[] edge = new double[s.N];
            for (int i = 0; i < s.N; i++)
                edge[i] = ((sign == 1 ? s.BoxX0[i] : s.BoxX1[i]) - s.Pan[i]) * sign / s.L;
            if (edge.All(double.IsNaN))
                return null;
            double[] d = SwimSignal.SavGol(SwimSignal.Interp(edge), 9, 2, 1);
            double[] v = new double[s.N];
            for (int i = 0; i < s.N; i++)
                v[i] = -d[i] * s.Fps;
            double te = s.T[i0];
            int ia = -1;
            for (int i = 0; i < s.N; i++)
            {
                if (s.T[i] >= te - 2.5 && s.T[i] <= te && (ia < 0 || v[i] > v[ia]))
                    ia = i;
            }

            if (ia < 0 || !(v[ia] > 0))
                return null;
            int hold = Math.Max(1, (int)(TouchHold * s.Fps));
            for (int i = ia; i < s.N - hold; i++)
            {
                bool stopped = true;
                for (int q = i; q < i + hold && stopped; q++)
                    stopped = v[q] < TouchSpeedFraction * v[ia];
                if (stopped)
                    return s.T[i] - TouchLead;
            }

            return null;
        }

        /// <summary>
        /// Flip turn: the head starts down when the body starts to fold. From the most compact moment of the turn (the
        /// narrowest box, from 1 s before the turn to the contact) back to the last three images with the hips open
        /// (shoulder–hip–knee at least 160°): the first image after them with the hips closing.
        /// </summary>
        private static double? RotationStart(Signals s, int i0, double? plant)
        {
            double hi = plant ?? s.T[i0] + 0.3;
            int tuck = -1;
            for (int i = 0; i < s.N; i++)
            {
                double w = s.BoxX1[i] - s.BoxX0[i];
                if (s.T[i] >= s.T[i0] - 1.0 && s.T[i] <= hi && !double.IsNaN(w) && (tuck < 0 || w < s.BoxX1[tuck] - s.BoxX0[tuck]))
                    tuck = i;
            }

            if (tuck < 0)
                return null;
            int start = -1, run = 0;
            for (int i = tuck; i > Math.Max(0, tuck - (int)(2.0 * s.Fps)); i--)
            {
                if (double.IsNaN(s.HipAngle[i]))
                    continue;
                if (s.HipAngle[i] < RotationOpenAngle)
                {
                    start = i;
                    run = 0;
                }
                else if (++run >= 3)
                {
                    return start >= 0 ? s.T[start] : (double?)null;
                }
            }

            return null;
        }

        /// <summary>
        /// Breaststroke pull-out: the hands sweep back behind the shoulders (0.2 body lengths) and stay there for 0.4 s
        /// (the glide), from 0.3 s after the push-off to 0.5 s before the breakout.
        /// </summary>
        private static double? PullDown(Signals s, double pushoff, double breakout, int sign)
        {
            double[] f = new double[s.N];
            for (int i = 0; i < s.N; i++)
            {
                double a = (s.WrX[0][i] - s.ShX[i]) * sign / s.L, b = (s.WrX[1][i] - s.ShX[i]) * sign / s.L;
                double m = double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Max(a, b);
                f[i] = s.Present[i] && s.InWater[i] ? m : double.NaN;
            }

            if (f.All(double.IsNaN))
                return null;
            f = SwimSignal.Median(SwimSignal.Interp(f), 9);
            int hold = (int)(PullDownHold * s.Fps);
            for (int i = 0; i < s.N; i++)
            {
                if (!(s.T[i] > pushoff + 0.3 && s.T[i] < breakout - 0.5) || f[i] >= PullDownBack || i + hold >= s.N)
                    continue;
                bool stays = true;
                for (int q = i; q < i + hold && stays; q++)
                    stays = f[q] < PullDownBack;
                if (stays)
                    return s.T[i];
            }

            return null;
        }

        /// <summary>The band of the lane rope at the surface (pixels of the reading) per image, from SwimTechFrame.Rows; null without them.</summary>
        private static double[] Surface(SwimTechReading r, Signals s)
        {
            int n = s.N;
            if (r.Frames[0].Rows == null)
                return null;
            int bins = r.Frames[0].Rows.Length;
            int top = (int)(bins * 0.75);
            double[,] score = new double[n, top];
            for (int i = 0; i < n; i++)
            {
                float[] rows = r.Frames[i].Rows;
                double[] v = new double[top];
                for (int b = 0; b < top; b++)
                    v[b] = rows != null && rows.Length == bins ? rows[b] : 0;
                double med = SwimSignal.Percentile(v, 50) + 1e-6;
                for (int b = 0; b < top; b++)
                    score[i, b] = Math.Log(Math.Max(v[b] / med, 1e-3));
            }

            // Viterbi: the band can move slowly (cost 1 per band of change).
            double[] cost = new double[top], next = new double[top];
            int[,] back = new int[n, top];
            for (int b = 0; b < top; b++)
                cost[b] = -score[0, b];
            for (int i = 1; i < n; i++)
            {
                for (int b = 0; b < top; b++)
                {
                    double bestC = double.MaxValue;
                    int bestB = 0;
                    for (int p = 0; p < top; p++)
                    {
                        double c = cost[p] + Math.Abs(b - p);
                        if (c < bestC)
                        {
                            bestC = c;
                            bestB = p;
                        }
                    }

                    next[b] = bestC - score[i, b];
                    back[i, b] = bestB;
                }

                double[] t = cost;
                cost = next;
                next = t;
            }

            int[] path = new int[n];
            int last = 0;
            for (int b = 1; b < top; b++)
            {
                if (cost[b] < cost[last])
                    last = b;
            }

            path[n - 1] = last;
            for (int i = n - 1; i > 0; i--)
                path[i - 1] = back[i, path[i]];
            double[] y = new double[n];
            for (int i = 0; i < n; i++)
                y[i] = Math.Exp(score[i, path[i]]) >= 1.3 ? (path[i] + 0.5) * r.Height / bins : double.NaN;
            return SwimSignal.Interp(y);
        }

        /// <summary>Height bins of the image for the rope line (LineSurface).</summary>
        public const int LineBins = 100;

        /// <summary>Cost of moving the rope line by one bin between images (against the length of a line, 0.35-1).</summary>
        public const double LineJump = 0.6;

        /// <summary>
        /// The rope line at the surface (pixels of the reading) per image from SwimTechFrame.Lines: dynamic programming
        /// through height bins scored by the length of the longest line in the bin (and 70% of it in the next bins: the two
        /// edges of a thin rope), above the nose (+3% of the height: the floor lines are below the swimmer), with a cost
        /// for moving; nan where less than a quarter of the images within 0.5 s have a line on the path. Null without lines.
        /// </summary>
        private static double[] LineSurface(SwimTechReading r, Signals s)
        {
            int n = s.N;
            if (!r.Frames.Any(f => f.Lines != null && f.Lines.Length >= 4))
                return null;
            int bins = LineBins, top = (int)(bins * 0.85);
            double[] nose = SwimSignal.Median(SwimSignal.Interp(s.NoseY), 5);
            double[,] score = new double[n, top];
            bool[,] allowed = new bool[n, top];
            for (int i = 0; i < n; i++)
            {
                double[] v = new double[top];
                float[] lines = r.Frames[i].Lines;
                if (lines != null)
                {
                    for (int q = 0; q + 3 < lines.Length; q += 4)
                    {
                        int b = (int)(lines[q] * bins);
                        if (b >= 0 && b < top)
                            v[b] = Math.Max(v[b], lines[q + 2] - lines[q + 1]);
                    }
                }

                int limit = double.IsNaN(nose[i]) ? top : (int)Math.Min(top, Math.Max(0, (nose[i] / r.Height + 0.03) * bins));
                for (int b = 0; b < top; b++)
                {
                    double up = b > 0 ? v[b - 1] : 0, down = b + 1 < top ? v[b + 1] : 0;
                    score[i, b] = Math.Max(v[b], 0.7 * Math.Max(up, down));
                    allowed[i, b] = b < limit;
                }
            }

            const double big = 1e3;
            double[] cost = new double[top], next = new double[top];
            int[,] back = new int[n, top];
            for (int b = 0; b < top; b++)
                cost[b] = -score[0, b] + (allowed[0, b] ? 0 : big);
            for (int i = 1; i < n; i++)
            {
                for (int b = 0; b < top; b++)
                {
                    double bestC = double.MaxValue;
                    int bestB = 0;
                    for (int p = 0; p < top; p++)
                    {
                        double c = cost[p] + LineJump * Math.Abs(b - p);
                        if (c < bestC)
                        {
                            bestC = c;
                            bestB = p;
                        }
                    }

                    next[b] = bestC - score[i, b] + (allowed[i, b] ? 0 : big);
                    back[i, b] = bestB;
                }

                double[] t = cost;
                cost = next;
                next = t;
            }

            int[] path = new int[n];
            int last = 0;
            for (int b = 1; b < top; b++)
            {
                if (cost[b] < cost[last])
                    last = b;
            }

            path[n - 1] = last;
            for (int i = n - 1; i > 0; i--)
                path[i - 1] = back[i, path[i]];

            // Support: share of the images within 0.5 s with a line on the path.
            int k = Math.Max(1, (int)Math.Round(1.0 * s.Fps));
            double[] hit = new double[n];
            for (int i = 0; i < n; i++)
                hit[i] = score[i, path[i]] > 0 ? 1 : 0;
            double[] y = new double[n];
            int seen = 0;
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - k / 2), z = Math.Min(n - 1, a + k - 1);
                double sum = 0;
                for (int q = a; q <= z; q++)
                    sum += hit[q];
                bool ok = sum / k >= 0.25;
                y[i] = ok ? (path[i] + 0.5) * r.Height / bins : double.NaN;
                if (ok)
                    seen++;
            }

            return seen >= 5 ? SwimSignal.Interp(y) : null;
        }

        private static double? BreakoutBySurface(Signals s, double pushoff, double[] surface)
        {
            double[] ny = SwimSignal.Median(SwimSignal.Interp(s.NoseY), 5);
            double[] d0 = new double[s.N];
            for (int i = 0; i < s.N; i++)
                d0[i] = (ny[i] - surface[i]) / s.L;
            double[] d = SwimSignal.Mean(SwimSignal.Median(d0, 5), 5);
            return Rise(s, pushoff, d, true);
        }

        private static double? BreakoutByHead(Signals s, double pushoff)
        {
            double[] ny = new double[s.N];
            for (int i = 0; i < s.N; i++)
                ny[i] = s.Present[i] && s.InWater[i] ? s.NoseY[i] : double.NaN;
            ny = SwimSignal.Median(SwimSignal.Interp(ny), 7);
            double[] d = new double[s.N];
            for (int i = 0; i < s.N; i++)
                d[i] = ny[i] / s.L;
            return Rise(s, pushoff, d, false);
        }

        /// <summary>
        /// The head comes up from its deepest point after the push-off. With the surface: the head rises to the level it
        /// keeps while swimming at the surface; the breakout is the first image within 0.03 body lengths of the level of the
        /// next 2 s (median), once the head has risen at least 0.25 body lengths (the band must be the surface: the head at
        /// least 0.3 body lengths under it at the deepest point). Without it: within 0.1 body lengths of the highest level
        /// of the head.
        /// </summary>
        private static double? Rise(Signals s, double pushoff, double[] d, bool bySurface)
        {
            List<int> idx = new List<int>();
            for (int i = s.A; i <= s.B; i++)
            {
                if (s.T[i] > pushoff + 0.5 && s.T[i] < pushoff + 10)
                    idx.Add(i);
            }

            if (idx.Count < 30)
                return null;
            int deep = idx[0];
            foreach (int i in idx)
            {
                if (d[i] > d[deep])
                    deep = i;
            }

            if (!bySurface)
            {
                List<int> after = idx.Where(i => i > deep && s.T[i] < s.T[deep] + 6).ToList();
                if (after.Count < 15)
                    return null;
                double level = after.Min(i => d[i]);
                foreach (int i in after)
                {
                    if (d[i] <= level + 0.1)
                        return s.T[i];
                }

                return null;
            }

            // The band must be the surface: the head well under it at the deepest point of the glide.
            if (d[deep] < SurfaceMinDepth)
                return null;
            int n = (int)(BreakoutLevelWindow * s.Fps);
            foreach (int i in idx)
            {
                if (i <= deep)
                    continue;
                if (i + n > s.N)
                    break;
                double[] w = new double[n];
                Array.Copy(d, i, w, 0, n);
                double level = SwimSignal.Percentile(w, 50);
                if (d[deep] - level >= BreakoutRise && d[i] <= level + BreakoutLevelTolerance)
                    return s.T[i];
            }

            return null;
        }

        /// <summary>Deepest bends of the knees between ta and tb: (time, angle).</summary>
        private static List<KeyValuePair<double, double>> Kicks(Signals s, double ta, double tb)
        {
            double[] k = new double[s.N];
            for (int i = 0; i < s.N; i++)
                k[i] = s.Present[i] && s.InWater[i] ? s.KneeAngle[i] : double.NaN;
            k = SwimSignal.Median(SwimSignal.Interp(k), 3);
            double[] neg = k.Select(v => -v).ToArray();
            List<KeyValuePair<double, double>> found = new List<KeyValuePair<double, double>>();
            foreach (int p in SwimStrokeDetector.FindPeaks(neg, (int)(0.25 * s.Fps), KickProminence))
            {
                if (s.T[p] >= ta && s.T[p] <= tb && k[p] <= DolphinMaxAngle)
                    found.Add(new KeyValuePair<double, double>(s.T[p], k[p]));
            }

            return found;
        }

        /// <summary>Furthest reach of a hand along the swimming direction (body lengths ahead of the shoulders) between ta and tb.</summary>
        private static List<double> Strokes(Signals s, double ta, double tb, int direction)
        {
            double[] f = Reach(s, direction);
            List<double> strokes = new List<double>();
            foreach (int p in SwimStrokeDetector.FindPeaks(f, (int)(0.35 * s.Fps), 0.15))
            {
                if (s.T[p] >= ta && s.T[p] <= tb)
                    strokes.Add(s.T[p]);
            }

            return strokes;
        }

        /// <summary>The hands ahead of the shoulders along the swim (body lengths), smoothed; NaN-free where the swimmer was seen.</summary>
        private static double[] Reach(Signals s, int direction)
        {
            double[] f = new double[s.N];
            for (int i = 0; i < s.N; i++)
            {
                double a = (s.WrX[0][i] - s.ShX[i]) * direction / s.L, b = (s.WrX[1][i] - s.ShX[i]) * direction / s.L;
                double m = double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Max(a, b);
                f[i] = s.Present[i] && s.InWater[i] ? m : double.NaN;
            }

            return SwimSignal.SavGol(SwimSignal.Interp(f), 7, 2);
        }

        /// <summary>Breaststroke and butterfly: the reaches of two cycles are at least this far apart (seconds).</summary>
        public const double CycleMinGap = 0.6;

        /// <summary>Two pulls closer than this (seconds) are one cycle (the later kept).</summary>
        public const double PullMinGap = 0.7;

        /// <summary>Where the reach of a cycle ends: forward from its peak while the hands stay within PullDrop body lengths of it.</summary>
        public const double PullDrop = 0.08;

        /// <summary>The reach of the hands at the touch of an open turn is not a cycle: none in this many seconds before the wall.</summary>
        public const double PullBeforeWall = 0.5;

        /// <summary>
        /// Breaststroke and butterfly: one reach of both hands per cycle (peaks of the reach at least CycleMinGap apart, prominence 0.15
        /// body lengths); the pull of each where its reach ends (the glide is before it).
        /// </summary>
        private static List<double> CyclePulls(Signals s, double ta, double tb, int direction)
        {
            List<double> pulls = new List<double>();
            if (s.L <= 0 || tb <= ta)
                return pulls;

            double[] f = Reach(s, direction);
            foreach (int p in SwimStrokeDetector.FindPeaks(f, (int)(CycleMinGap * s.Fps), 0.15))
            {
                if (s.T[p] < ta || s.T[p] > tb)
                    continue;
                int e = p;
                while (e + 1 < s.N && s.T[e + 1] - s.T[p] <= 1.5 && f[e + 1] >= f[p] - PullDrop)
                    e++;
                if (s.T[e] > tb + 0.5)
                    continue;
                if (pulls.Count > 0 && s.T[e] - pulls[pulls.Count - 1] < PullMinGap)
                    pulls[pulls.Count - 1] = s.T[e];
                else
                    pulls.Add(s.T[e]);
            }

            return pulls;
        }

        /// <summary>Breaststroke: a kick bends the knees below this at its deepest (degrees, median of 5 images).</summary>
        public const double BreastKickKnee = 125;

        /// <summary>Breaststroke: the pull of a cycle is this far before its kick at most (seconds), and at least BreastPullBeforeKickMin.</summary>
        public const double BreastPullBeforeKick = 1.1;
        public const double BreastPullBeforeKickMin = 0.1;

        /// <summary>Breaststroke: the hands fall at least this far (body lengths) in BreastFallWindow seconds at the pull.</summary>
        public const double BreastFall = 0.15;
        public const double BreastFallWindow = 0.35;

        /// <summary>
        /// (2.9.9) Breaststroke: one pull a cycle, found from its kick (the deepest bends of the knees, at least 0.7 s apart, below
        /// BreastKickKnee): the largest fall of the hands (the mean of the two wrists, a median of 5 images) in the 1.1 s before the
        /// kick and ending before it, and the pull where that fall leaves the reach of the glide (within PullDrop of the level of the
        /// 0.6 s before it, its 80th percentile). The wrists of the pose jump between the arms and the legs while the legs kick: the
        /// reach alone gave late, doubled or missing cycles. Checked by eye on 30 fps clips (VOLOS): 18 pulls after 4 turns within
        /// 0.1 s (one 0.16 s), one missed at the end of a take (the legs out of the image); before the wall 0.15-0.25 s late.
        /// </summary>
        private static List<double> BreastPulls(Signals s, double ta, double tb, int direction)
        {
            List<double> pulls = new List<double>();
            if (s.L <= 0 || tb <= ta || s.N < 10)
                return pulls;

            int n = s.N;
            double[] hands = new double[n];
            double[] knee = new double[n];
            for (int i = 0; i < n; i++)
            {
                bool seen = s.Present[i] && s.InWater[i];
                double a = (s.WrX[0][i] - s.ShX[i]) * direction / s.L, b = (s.WrX[1][i] - s.ShX[i]) * direction / s.L;
                double v = double.IsNaN(a) ? b : double.IsNaN(b) ? a : 0.5 * (a + b);
                hands[i] = seen ? v : double.NaN;
                knee[i] = seen ? s.KneeAngle[i] : double.NaN;
            }

            double[] f = SwimSignal.SavGol(SwimSignal.Interp(SwimSignal.Median(hands, 5)), 7, 2);
            double[] kneeMedian = SwimSignal.Median(knee, 5);
            double[] k = SwimSignal.Interp(kneeMedian);
            double[] bend = new double[n];
            for (int i = 0; i < n; i++)
                bend[i] = -k[i];

            int w = (int)Math.Round(BreastFallWindow * s.Fps);
            foreach (int kick in SwimStrokeDetector.FindPeaks(bend, (int)(0.7 * s.Fps), 25))
            {
                double tk = s.T[kick];
                if (tk < ta || tk > tb + BreastPullBeforeKick || double.IsNaN(kneeMedian[kick]) || k[kick] > BreastKickKnee)
                    continue;

                // The largest fall of the hands before the kick (its lowest point not after the kick).
                int lo = FirstAtOrAfter(s.T, tk - BreastPullBeforeKick), hi = FirstAtOrAfter(s.T, tk - BreastPullBeforeKickMin);
                double best = -1;
                int start = -1;
                for (int i = lo; i < hi; i++)
                {
                    int end = Math.Min(Math.Min(n - 1, i + w), kick);
                    double low = f[i];
                    for (int j = i; j <= end; j++)
                        low = Math.Min(low, f[j]);
                    if (f[i] - low > best + 1e-9)
                    {
                        best = f[i] - low;
                        start = i;
                    }
                }

                if (start < 0 || best < BreastFall)
                    continue;

                int bottom = start;
                int last = Math.Min(Math.Min(n - 1, start + w), kick);
                for (int j = start; j <= last; j++)
                {
                    if (f[j] < f[bottom])
                        bottom = j;
                }

                // Back from the bottom to the last image still on the reach of the glide.
                int j0 = FirstAtOrAfter(s.T, s.T[start] - 0.6);
                double level = SwimSignal.Percentile(f.Skip(j0).Take(start - j0 + 1), 80);
                int pull = bottom;
                while (pull > j0 && f[pull] < level - PullDrop)
                    pull--;

                double t = s.T[pull];
                if (t < ta || t > tb)
                    continue;

                // Two kicks found for one cycle: one pull.
                if (pulls.Count > 0 && t - pulls[pulls.Count - 1] < 0.3)
                    continue;
                pulls.Add(t);
            }

            if (pulls.Count < 3)
                return pulls;

            // A kick not seen (the legs out of the image, the knees not found bent): a gap of more than 1.6 cycles gets the
            // largest fall of the hands in its middle, and after the last pull one more cycle when the hands fall again a cycle
            // later (both with the hands staying low, seen under water: not the camera leaving the water). Checked on the clips:
            // the last pull of a take found (0.02 s), none added where there was none.
            double[] held = SwimSignal.Median(hands, 5);
            List<double> gaps = new List<double>();
            for (int i = 1; i < pulls.Count; i++)
                gaps.Add(pulls[i] - pulls[i - 1]);
            double cycle = SwimSignal.Percentile(gaps, 50);
            List<double> filled = new List<double> { pulls[0] };
            for (int i = 1; i < pulls.Count; i++)
            {
                double a = pulls[i - 1], b = pulls[i];
                if (b - a > 1.6 * cycle)
                {
                    double? t = FallPull(s, f, hands, held, a + 0.5 * cycle, b - 0.4 * cycle, w);
                    if (t.HasValue && t.Value - a > 0.6 * cycle && b - t.Value > 0.6 * cycle)
                        filled.Add(t.Value);
                }

                filled.Add(b);
            }

            double lastPull = pulls[pulls.Count - 1];
            if (lastPull + 1.4 * cycle <= tb + 0.5)
            {
                double? t = FallPull(s, f, hands, held, lastPull + 0.6 * cycle, Math.Min(tb, lastPull + 1.6 * cycle), w);
                if (t.HasValue && t.Value - lastPull >= 0.75 * cycle)
                    filled.Add(t.Value);
            }

            return filled;
        }

        /// <summary>
        /// Breaststroke without its kick: the largest fall of the hands (at least 0.25 body lengths in BreastFallWindow) in a window, the
        /// hands then low for 3 images seen under water within 0.4 s; the pull where the fall leaves the reach of the glide. Null if none.
        /// </summary>
        private static double? FallPull(Signals s, double[] f, double[] hands, double[] held, double ta, double tb, int w)
        {
            int n = s.N;
            int lo = FirstAtOrAfter(s.T, ta), hi = FirstAtOrAfter(s.T, tb);
            double best = -1;
            int start = -1;
            for (int i = lo; i < hi; i++)
            {
                double bottom = f[i];
                for (int j = i; j <= Math.Min(n - 1, i + w); j++)
                    bottom = Math.Min(bottom, f[j]);
                if (f[i] - bottom > best + 1e-9)
                {
                    best = f[i] - bottom;
                    start = i;
                }
            }

            if (start < 0 || best < 0.25)
                return null;

            int e = start;
            for (int j = start; j <= Math.Min(n - 1, start + w); j++)
            {
                if (f[j] < f[e])
                    e = j;
            }

            int j0 = FirstAtOrAfter(s.T, s.T[start] - 0.6);
            double level = SwimSignal.Percentile(f.Skip(j0).Take(start - j0 + 1), 80);
            int seen = 0;
            for (int q = e; q < n && s.T[q] < s.T[e] + 0.4; q++)
            {
                if (s.Present[q] && s.InWater[q] && !double.IsNaN(hands[q]) && held[q] <= level - 0.25)
                    seen++;
            }

            if (seen < 3)
                return null;

            int pull = e;
            while (pull > j0 && f[pull] < level - PullDrop)
                pull--;
            return s.T[pull];
        }

        /// <summary>The first index with a time at or after t (the length when none).</summary>
        private static int FirstAtOrAfter(double[] times, double t)
        {
            int i = Array.BinarySearch(times, t);
            if (i < 0)
                return ~i;
            while (i > 0 && times[i - 1] >= t)
                i--;
            return i;
        }
    }
}
