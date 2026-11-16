#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (Swim 2.24.0) The panel of a start or a turn: on the panel of a group of screens, at the end of the video of the group (big) and as
    /// the picture of the report. Its title, the stage of the moment and its clock; the events of the phase on a line of time; all the
    /// values measured, in groups, big, each from its moment (a dash before it); the speed as a continuous line drawn from the measured
    /// points (the distance through them, smooth and without overshoot: the mean of each segment is the one measured), the mean of each
    /// segment with its value, the speed of the moment and the highest so far.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Data of the panel
        private sealed class PanelValue
        {
            public string Label;
            public string Text;
            public string Unit;

            /// <summary>When it is known (s from the origin of the phase).</summary>
            public double At;

            /// <summary>The kicks: counted as they pass.</summary>
            public bool Kicks;
        }

        private sealed class PanelGroup
        {
            public string Title;
            public readonly List<PanelValue> Values = new List<PanelValue>();
        }

        private sealed class PanelMark
        {
            public readonly double T;
            public readonly string Label;

            public PanelMark(double t, string label)
            {
                T = t;
                Label = label;
            }
        }

        private sealed class PanelSegment
        {
            public double From, To, Speed;
        }

        private sealed class PanelData
        {
            public SwimTechPhase Phase;

            /// <summary>The part of the phase drawn (s from its origin): its window.</summary>
            public double T0, T1;

            public readonly List<PanelMark> Events = new List<PanelMark>();
            public readonly List<PanelMark> Lines = new List<PanelMark>();
            public readonly List<PanelMark> Measured = new List<PanelMark>();
            public readonly List<double> Kicks = new List<double>();
            public readonly List<PanelGroup> Groups = new List<PanelGroup>();
            public readonly List<PanelSegment> Segments = new List<PanelSegment>();

            /// <summary>The speed (m/s) at a time from SpeedFrom to SpeedTo; null: no line (too few points).</summary>
            public Func<double, double> Speed;
            public double SpeedFrom, SpeedTo;

            /// <summary>The highest speed of the line and of the segments (the top of the chart).</summary>
            public double SpeedTop;

            /// <summary>
            /// (2.24.1) The end of the time of the chart of the speed: where its line ends (the 15, or the last distance measured), else the
            /// end of the part drawn. Nothing of the speed after it.
            /// </summary>
            public double ChartTo;

            /// <summary>The main time of the phase for the picture of its end: the 15 (or the last distance), its label.</summary>
            public double? MainTime;
            public string MainLabel;

            /// <summary>The time of the last thing of the phase (its end: all the values shown).</summary>
            public double Last;
        }

        /// <summary>
        /// A monotone cubic through points (Fritsch–Carlson, the end slopes from three points as SciPy's PCHIP, or the first slope given): it
        /// does not overshoot between the points. Its derivative is the speed when the points are the distance in time. (2.24.1) flatEnd:
        /// the speed levels off at the last point (no acceleration there), instead of going on as the last segments go.
        /// </summary>
        private sealed class Pchip
        {
            private readonly double[] x, y, m;

            public Pchip(IList<double> xs, IList<double> ys, double? first, bool flatEnd = false)
            {
                int n = xs.Count;
                x = xs.ToArray();
                y = ys.ToArray();
                m = new double[n];
                double[] h = new double[n - 1], dl = new double[n - 1];
                for (int i = 0; i < n - 1; i++)
                {
                    h[i] = x[i + 1] - x[i];
                    dl[i] = (y[i + 1] - y[i]) / h[i];
                }

                for (int i = 1; i < n - 1; i++)
                {
                    if (dl[i - 1] * dl[i] <= 0)
                    {
                        m[i] = 0;
                    }
                    else
                    {
                        double w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1];
                        m[i] = (w1 + w2) / (w1 / dl[i - 1] + w2 / dl[i]);
                    }
                }

                m[0] = first ?? End(n, h[0], n > 2 ? h[1] : h[0], dl[0], n > 2 ? dl[1] : dl[0]);
                if (flatEnd)
                {
                    // No acceleration at the last point (the second derivative of the cubic is zero there): with the slope at the point
                    // before it, (3 d − m) / 2. Inside 0 … 3 d, so the last piece stays monotone.
                    double d = dl[n - 2];
                    m[n - 1] = d > 0 ? Math.Max(0, Math.Min(3 * d, (3 * d - m[n - 2]) / 2)) : 0;
                }
                else
                {
                    m[n - 1] = End(n, h[n - 2], n > 2 ? h[n - 3] : h[n - 2], dl[n - 2], n > 2 ? dl[n - 3] : dl[n - 2]);
                }
            }

            private static double End(int n, double h0, double h1, double d0, double d1)
            {
                if (n < 3)
                    return d0;
                double v = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
                if (Math.Sign(v) != Math.Sign(d0))
                    return 0;
                if (Math.Sign(d0) != Math.Sign(d1) && Math.Abs(v) > Math.Abs(3 * d0))
                    return 3 * d0;
                return v;
            }

            public double StartSlope
            {
                get { return m[0]; }
            }

            public double Last
            {
                get { return x[x.Length - 1]; }
            }

            public double Slope(double t)
            {
                int i = 0;
                while (i < x.Length - 2 && t > x[i + 1])
                    i++;
                double hi = x[i + 1] - x[i];
                double s = Math.Max(0, Math.Min(1, (t - x[i]) / hi));
                double h00 = 6 * s * s - 6 * s, h10 = 3 * s * s - 4 * s + 1, h01 = -6 * s * s + 6 * s, h11 = 3 * s * s - 2 * s;
                return (h00 * y[i] + h01 * y[i + 1]) / hi + h10 * m[i] + h11 * m[i + 1];
            }
        }

        private static void AddValue(PanelGroup group, string label, double? value, string format, string unit, double? at, double fallback)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                return;
            group.Values.Add(new PanelValue
            {
                Label = label,
                Text = value.Value.ToString(format, CultureInfo.InvariantCulture),
                Unit = unit,
                At = at ?? fallback
            });
        }

        private static void AddMark(List<PanelMark> list, double? t, string label)
        {
            if (t.HasValue && !double.IsNaN(t.Value))
                list.Add(new PanelMark(t.Value, label));
        }

        /// <summary>The points (time, metres) in the order of time, each later and farther than the one before it (a wrong mark leaves).</summary>
        private static void MonotonePoints(List<KeyValuePair<double, double>> points, out List<double> xs, out List<double> ys)
        {
            xs = new List<double>();
            ys = new List<double>();
            foreach (KeyValuePair<double, double> p in points.OrderBy(q => q.Key))
            {
                if (xs.Count > 0 && (p.Key < xs[xs.Count - 1] + 0.05 || p.Value < ys[ys.Count - 1] + 0.05))
                    continue;
                xs.Add(p.Key);
                ys.Add(p.Value);
            }
        }

        /// <summary>The data of the panel of a phase: its values in groups, its events, its segments and the line of its speed.</summary>
        private PanelData BuildPanel(SwimTechPhase p)
        {
            PanelData d = new PanelData { Phase = p };
            d.T0 = Timing.Seconds(p.WindowStart, p.Origin);
            d.T1 = Math.Max(d.T0 + 1, Timing.Seconds(p.WindowEnd, p.Origin));
            string u = UnitShort;
            double k = Race != null ? Race.K : 1;
            Func<int, double?> split = offset =>
            {
                double t;
                return p.Splits.TryGetValue(offset, out t) ? (double?)t : null;
            };
            d.Kicks.AddRange(p.Kicks);
            double? breakoutDist = p.BreakoutDist;
            List<KeyValuePair<double, double>> points = new List<KeyValuePair<double, double>>();

            if (p.Kind == SwimPhaseKind.Turn)
            {
                double? tIn = split(-5), o5 = split(5), o10 = split(10), o15 = split(15);
                double? vIn = tIn.HasValue && tIn.Value < 0 ? 5 * k / -tIn.Value : (double?)null;

                PanelGroup approach = new PanelGroup { Title = SwimLang.T("ΠΡΟΣΕΓΓΙΣΗ") };
                AddValue(approach, string.Format(SwimLang.T("−5 {0} → ΤΟΙΧΟΣ"), u), tIn.HasValue ? -tIn.Value : (double?)null, "0.00", "s", tIn, 0);
                AddValue(approach, SwimLang.T("ΤΑΧΥΤΗΤΑ"), vIn, "0.00", "m/s", 0, 0);
                AddValue(approach, SwimLang.T("ΧΕΡΙΑ → ΤΟΙΧΟΣ"), p.LastStroke.HasValue ? -p.LastStroke.Value : (double?)null, "0.00", "s", p.LastStroke, 0);

                PanelGroup wall = new PanelGroup { Title = SwimLang.T("ΣΤΡΟΦΗ") };
                AddValue(wall, SwimLang.T("ΠΕΡΙΣΤΡΟΦΗ"), p.Value("rotation"), "0.00", "s", p.RotationStart, 0);
                AddValue(wall, SwimLang.T("ΕΠΑΦΗ"), p.Value("contact"), "0.00", "s", p.Pushoff, 0);
                AddValue(wall, SwimLang.T("ΧΡΟΝΟΣ ΣΤΡΟΦΗΣ"), p.Value("turnTime"), "0.00", "s", p.Pushoff, 0);

                PanelGroup under = UnderwaterGroup(p, u);

                PanelGroup after = new PanelGroup { Title = SwimLang.T("ΜΕΤΑ ΤΟΝ ΤΟΙΧΟ") };
                AddValue(after, "+5 " + u, o5, "0.00", "s", o5, 0);
                AddValue(after, "+10 " + u, o10, "0.00", "s", o10, 0);
                AddValue(after, "+15 " + u, o15, "0.00", "s", o15, 0);
                AddValue(after, SwimLang.T("1η ΧΕΡΙΑ"), p.FirstStroke, "0.00", "s", p.FirstStroke, 0);
                d.Groups.AddRange(new[] { approach, wall, under, after }.Where(x => x.Values.Count > 0));

                AddMark(d.Events, tIn, "−5 " + u);
                AddMark(d.Events, p.LastStroke, SwimLang.T("τελ. χεριά"));
                AddMark(d.Events, p.RotationStart, SwimLang.T("περιστροφή"));
                AddMark(d.Events, 0, SwimLang.T("επαφή"));
                AddMark(d.Events, p.Pushoff, SwimLang.T("αποκόλληση"));
                AddMark(d.Events, o5, "+5");
                AddMark(d.Events, p.Breakout, SwimLang.T("έξοδος"));
                AddMark(d.Events, p.FirstStroke, SwimLang.T("1η χεριά"));
                AddMark(d.Events, o10, "+10");
                AddMark(d.Events, o15, "+15");
                AddMark(d.Lines, 0, SwimLang.T("ΕΠΑΦΗ"));
                AddMark(d.Lines, p.Breakout, SwimLang.T("ΕΞΟΔΟΣ"));
                AddMark(d.Measured, tIn, "−5 " + u);
                AddMark(d.Measured, o5, "+5 " + u);
                AddMark(d.Measured, o10, "+10 " + u);
                AddMark(d.Measured, o15, "+15 " + u);
                if (vIn.HasValue)
                    d.Segments.Add(new PanelSegment { From = tIn.Value, To = 0, Speed = vIn.Value });
                AddSegment(d, 0, o5, p.Value("v0_5"));
                AddSegment(d, o5, o10, p.Value("v5_10"));
                AddSegment(d, o10, o15, p.Value("v10_15"));
                d.MainTime = o15 ?? o10 ?? o5;
                d.MainLabel = string.Format(SwimLang.T("ΤΟΙΧΟΣ → +{0} {1}"), o15.HasValue ? 15 : o10.HasValue ? 10 : 5, u);

                // The speed: to the wall at the mean of the approach, slowing down from the rotation (or the last 0.3 s) to the contact
                // (the mean of −5 → wall as measured); on the wall from rest to the push-off; after it the line through the distances.
                if (o5.HasValue)
                    points.Add(new KeyValuePair<double, double>(o5.Value, 5 * k));
                if (p.Breakout.HasValue && breakoutDist.HasValue)
                    points.Add(new KeyValuePair<double, double>(p.Breakout.Value, breakoutDist.Value * k));
                if (o10.HasValue)
                    points.Add(new KeyValuePair<double, double>(o10.Value, 10 * k));
                if (o15.HasValue)
                    points.Add(new KeyValuePair<double, double>(o15.Value, 15 * k));
                TurnSpeed(d, p, tIn, k, points);
            }
            else
            {
                double? t5 = split(5), t10 = split(10), t15 = split(15);
                double? release = p.Block ?? p.OfficialRelease;
                PanelGroup block = new PanelGroup { Title = SwimLang.T("ΒΑΤΗΡΑΣ") };
                AddValue(block, SwimLang.T("ΑΝΤΙΔΡΑΣΗ"), p.Reaction, "0.00", "s", p.Reaction, 0);
                AddValue(block, SwimLang.T("ΧΕΡΙΑ"), p.HandsOff, "0.00", "s", p.HandsOff, 0);
                AddValue(block, SwimLang.T("ΠΙΣΩ ΠΟΔΙ"), p.BackFoot, "0.00", "s", p.BackFoot, 0);
                AddValue(block, SwimLang.T("ΑΠΟΔΕΣΜΕΥΣΗ"), release, "0.00", "s", release, 0);

                PanelGroup flight = new PanelGroup { Title = SwimLang.T("ΠΤΗΣΗ") };
                AddValue(flight, SwimLang.T("ΧΡΟΝΟΣ ΠΤΗΣΗΣ"), p.Value("flight"), "0.00", "s", p.Entry, 0);
                AddValue(flight, SwimLang.T("ΕΙΣΟΔΟΣ"), p.Entry, "0.00", "s", p.Entry, 0);
                AddValue(flight, SwimLang.T("ΑΠΟΣΤΑΣΗ"), p.EntryDist, "0.0", u, p.Entry, 0);

                PanelGroup under = UnderwaterGroup(p, u);

                PanelGroup dist = new PanelGroup { Title = SwimLang.T("ΑΠΟΣΤΑΣΕΙΣ") };
                AddValue(dist, "5 " + u, t5, "0.00", "s", t5, 0);
                AddValue(dist, "10 " + u, t10, "0.00", "s", t10, 0);
                AddValue(dist, "15 " + u, t15, "0.00", "s", t15, 0);
                AddValue(dist, SwimLang.T("1η ΧΕΡΙΑ"), p.FirstStroke, "0.00", "s", p.FirstStroke, 0);
                d.Groups.AddRange(new[] { block, flight, under, dist }.Where(x => x.Values.Count > 0));

                AddMark(d.Events, 0, SwimLang.T("σήμα"));
                AddMark(d.Events, p.Reaction, SwimLang.T("αντίδραση"));
                AddMark(d.Events, p.HandsOff, SwimLang.T("χέρια"));
                AddMark(d.Events, p.BackFoot, SwimLang.T("π. πόδι"));
                AddMark(d.Events, release, SwimLang.T("αποδέσμευση"));
                AddMark(d.Events, p.Entry, SwimLang.T("είσοδος"));
                AddMark(d.Events, t5, "5 " + u);
                AddMark(d.Events, t10, "10 " + u);
                AddMark(d.Events, p.Breakout, SwimLang.T("έξοδος"));
                AddMark(d.Events, p.FirstStroke, SwimLang.T("1η χεριά"));
                AddMark(d.Events, t15, "15 " + u);
                AddMark(d.Lines, release, SwimLang.T("ΑΠΟΔΕΣΜΕΥΣΗ"));
                AddMark(d.Lines, p.Entry, SwimLang.T("ΕΙΣΟΔΟΣ"));
                AddMark(d.Lines, p.Breakout, SwimLang.T("ΕΞΟΔΟΣ"));
                if (p.EntryDist.HasValue)
                    AddMark(d.Measured, p.Entry, SwimLang.T("είσοδος"));
                AddMark(d.Measured, t5, "5 " + u);
                AddMark(d.Measured, t10, "10 " + u);
                AddMark(d.Measured, t15, "15 " + u);
                AddSegment(d, 0, t5, p.Value("v0_5"));
                AddSegment(d, t5, t10, p.Value("v5_10"));
                AddSegment(d, t10, t15, p.Value("v10_15"));
                d.MainTime = t15 ?? t10 ?? t5;
                d.MainLabel = string.Format(SwimLang.T("ΣΗΜΑ → {0} {1}"), t15.HasValue ? 15 : t10.HasValue ? 10 : 5, u);

                // The speed: from rest at the first movement on the block, the line through the distances (the entry, 5, 10, the
                // breakout, 15).
                double move = p.Reaction ?? p.HandsOff ?? p.BackFoot ?? 0;
                if (move < 0 || move > 1.5)
                    move = 0;
                points.Add(new KeyValuePair<double, double>(move, 0));
                if (p.Entry.HasValue && p.EntryDist.HasValue)
                    points.Add(new KeyValuePair<double, double>(p.Entry.Value, p.EntryDist.Value * k));
                if (t5.HasValue)
                    points.Add(new KeyValuePair<double, double>(t5.Value, 5 * k));
                if (t10.HasValue)
                    points.Add(new KeyValuePair<double, double>(t10.Value, 10 * k));
                if (p.Breakout.HasValue && breakoutDist.HasValue)
                    points.Add(new KeyValuePair<double, double>(p.Breakout.Value, breakoutDist.Value * k));
                if (t15.HasValue)
                    points.Add(new KeyValuePair<double, double>(t15.Value, 15 * k));
                List<double> xs, ys;
                MonotonePoints(points, out xs, out ys);
                if (xs.Count >= 2 && xs[0] == move)
                {
                    // (2.24.1) The speed levels off at the 15 (or the last distance): it stops there, it does not go on falling.
                    Pchip curve = new Pchip(xs, ys, 0.0, true);
                    d.Speed = t => t < move ? 0 : curve.Slope(Math.Min(t, curve.Last));
                    d.SpeedFrom = Math.Min(0, move);
                    d.SpeedTo = curve.Last;
                }
            }

            // The part drawn: from a little before the first event to a little after the last one, inside the window of the phase (the
            // window of a training attempt lasts 25 s after its origin: its events would be squeezed into a corner).
            double firstEvent = 0, lastEvent = 0;
            foreach (PanelMark m in d.Events)
            {
                firstEvent = Math.Min(firstEvent, m.T);
                lastEvent = Math.Max(lastEvent, m.T);
            }

            foreach (double kick in d.Kicks)
                lastEvent = Math.Max(lastEvent, kick);
            if (d.Speed != null)
                lastEvent = Math.Max(lastEvent, d.SpeedTo);
            double window0 = d.T0, window1 = d.T1;
            d.T0 = Math.Max(window0, firstEvent - 0.6);
            d.T1 = Math.Min(window1, Math.Max(lastEvent + 0.8, d.T0 + 2));
            if (d.T1 <= d.T0 + 0.5)
            {
                d.T0 = window0;
                d.T1 = window1;
            }

            d.Last = Math.Max(d.T1, lastEvent);

            d.SpeedTop = d.Segments.Count > 0 ? d.Segments.Max(s => s.Speed) : 0;
            if (d.Speed != null)
            {
                for (int i = 0; i <= 200; i++)
                {
                    double t = d.SpeedFrom + (d.SpeedTo - d.SpeedFrom) * i / 200.0;
                    d.SpeedTop = Math.Max(d.SpeedTop, Math.Abs(d.Speed(t)));
                }
            }

            // (2.24.1) The chart of the speed ends where its line ends (the 15, or the last distance measured): nothing after it.
            double chartEnd = d.Speed != null ? d.SpeedTo : d.Segments.Count > 0 ? d.Segments.Max(s => s.To) : d.T1;
            d.ChartTo = chartEnd > d.T0 + 0.5 ? Math.Min(d.T1, chartEnd) : d.T1;
            return d;
        }

        /// <summary>The underwater values of a start or a turn: the kicks as they pass, their rate, the breakout and its distance.</summary>
        private static PanelGroup UnderwaterGroup(SwimTechPhase p, string u)
        {
            PanelGroup under = new PanelGroup { Title = SwimLang.T("ΥΠΟΒΡΥΧΙΑ") };
            double? kicks = p.Value("kicks");
            if (kicks.HasValue)
            {
                double first = p.Kicks.Count > 0 ? p.Kicks[0] : (p.Breakout ?? 0);
                under.Values.Add(new PanelValue { Label = SwimLang.T("ΔΕΛΦΙΝΙΣΜΟΙ"), Text = kicks.Value.ToString("0", CultureInfo.InvariantCulture), At = first, Kicks = true });
            }

            AddValue(under, SwimLang.T("ΣΥΧΝΟΤΗΤΑ"), p.Value("kickRate"), "0.00", "/s", p.Kicks.Count > 0 ? p.Kicks[p.Kicks.Count - 1] : (double?)null, 0);
            AddValue(under, SwimLang.T("ΕΞΟΔΟΣ"), p.Breakout, "0.00", "s", p.Breakout, 0);
            AddValue(under, SwimLang.T("ΑΠΟΣΤΑΣΗ"), p.BreakoutDist, "0.0", u, p.Breakout, 0);
            return under;
        }

        private static void AddSegment(PanelData d, double? from, double? to, double? speed)
        {
            if (from.HasValue && to.HasValue && speed.HasValue && to.Value > from.Value && speed.Value > 0)
                d.Segments.Add(new PanelSegment { From = from.Value, To = to.Value, Speed = speed.Value });
        }

        /// <summary>
        /// The line of the speed of a turn. To the wall: the mean speed of the approach until the rotation (or the last 0.3 s), then slowing
        /// down to rest at the contact, so that −5 → wall takes the time measured. On the wall: from rest to the speed of the push-off. After
        /// it: the line through the distances (+5, the breakout, +10, +15), from where the push on the wall brought the head (the distance its
        /// speed covers there), so that wall → +5 takes the time measured too.
        /// </summary>
        private static void TurnSpeed(PanelData d, SwimTechPhase p, double? tIn, double k, List<KeyValuePair<double, double>> points)
        {
            bool approach = tIn.HasValue && tIn.Value < -0.2;
            double t5 = approach ? tIn.Value : 0;
            double slow = 0, va = 0;
            if (approach)
            {
                slow = p.RotationStart.HasValue && p.RotationStart.Value > t5 + 0.1 && p.RotationStart.Value < 0 ? p.RotationStart.Value : Math.Max(t5 + 0.1, -0.3);
                va = 5 * k / ((slow - t5) + (-slow) / 2);
            }

            double push = p.Pushoff.HasValue && p.Pushoff.Value > 0 && p.Pushoff.Value < 2 ? p.Pushoff.Value : 0;
            Pchip after = null;
            double d0 = 0;
            for (int i = 0; i < 6; i++)
            {
                List<KeyValuePair<double, double>> all = new List<KeyValuePair<double, double>>(points);
                all.Add(new KeyValuePair<double, double>(push, d0));
                List<double> xs, ys;
                MonotonePoints(all, out xs, out ys);
                if (xs.Count < 2 || xs[0] != push)
                {
                    after = null;
                    break;
                }

                // (2.24.1) The speed levels off at the +15 (or the last distance): it stops there, it does not go on falling.
                after = new Pchip(xs, ys, null, true);
                double next = push > 0 ? Math.Max(0, after.StartSlope) * push / 2 : 0;
                if (Math.Abs(next - d0) < 1e-4)
                    break;
                d0 = next;
            }

            if (!approach && after == null)
                return;

            Pchip line = after;
            double top = after != null ? Math.Max(0, after.StartSlope) : 0;
            d.Speed = t =>
            {
                if (t < 0)
                {
                    if (!approach)
                        return 0;
                    if (t < slow)
                        return va;
                    return va * 0.5 * (1 + Math.Cos(Math.PI * (t - slow) / (0 - slow)));
                }

                if (line == null)
                    return 0;
                if (t < push)
                    return top * 0.5 * (1 - Math.Cos(Math.PI * t / push));
                return line.Slope(Math.Min(t, line.Last));
            };
            d.SpeedFrom = t5;
            d.SpeedTo = line != null ? line.Last : 0;
        }

        /// <summary>The stage of the movement at a time of a phase (s from its origin), as the snapshots get it (Greek).</summary>
        private static string StageAt(SwimTechPhase phase, double t)
        {
            if (phase == null)
                return "";

            const double near = 0.04;
            Func<double?, bool> at = x => x.HasValue && Math.Abs(t - x.Value) <= near;
            Func<double?, bool> before = x => x.HasValue && t < x.Value - near;
            if (phase.Kind == SwimPhaseKind.Start)
            {
                if (t < 0.02 || before(phase.Reaction))
                    return "Θέση ετοιμότητας";
                if (at(phase.Block))
                    return "Αποδέσμευση";
                if (before(phase.Block))
                    return "Φάση βατήρα";
                if (at(phase.Entry))
                    return "Είσοδος στο νερό";
                if (before(phase.Entry))
                    return "Πτήση";
                if (at(phase.Breakout))
                    return "Έξοδος από το νερό";
                if (before(phase.Breakout))
                    return "Υποβρύχια φάση";
                return "Κολύμβηση";
            }

            if (Math.Abs(t) <= near)
                return "Επαφή στον τοίχο";
            if (t < 0)
                return phase.RotationStart.HasValue && t >= phase.RotationStart.Value - near ? "Περιστροφή" : "Προσέγγιση";
            if (at(phase.Pushoff))
                return "Αποκόλληση";
            if (before(phase.Pushoff))
                return "Επαφή στον τοίχο";
            if (at(phase.Breakout))
                return "Έξοδος από το νερό";
            if (before(phase.Breakout))
                return "Υποβρύχια φάση";
            return "Κολύμβηση";
        }
        #endregion

        #region Painting
        /// <summary>
        /// The size the panel is drawn at in a place of this shape (units of the drawing): beside the videos side by side (1280 wide, at
        /// least 720 high), in a narrow place one under the other (900 wide, at least 1100 high).
        /// </summary>
        private static SizeF PanelCanvas(Size size)
        {
            float aspect = size.Width / (float)Math.Max(1, size.Height);
            if (aspect >= 1.2f)
                return new SizeF(1280f, Math.Max(720f, 1280f / aspect));
            return new SizeF(900f, Math.Max(1100f, 900f / aspect));
        }

        /// <summary>
        /// (2.24.0) The panel of a phase at its end, fitted in a size (the end of the video of the group, the picture of the report): all
        /// its values, the whole line of its speed, its main time in place of the clock. The analysis is read: on the thread of the screens.
        /// </summary>
        private void PaintFinalPanel(Graphics g, Size size, SwimTechPhase phase)
        {
            using (SolidBrush back = new SolidBrush(SwimTheme.Back))
                g.FillRectangle(back, 0, 0, size.Width, size.Height);
            if (phase == null || size.Width < 40 || size.Height < 30 || Timing == null || !Timing.IsValid)
                return;

            lock (overlayLock)
            {
                GraphicsState state = g.Save();
                try
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    SizeF canvas = PanelCanvas(size);
                    float scale = Math.Min(size.Width / canvas.Width, size.Height / canvas.Height);
                    g.TranslateTransform((size.Width - canvas.Width * scale) / 2f, (size.Height - canvas.Height * scale) / 2f);
                    g.ScaleTransform(scale, scale);
                    PanelData d = BuildPanel(phase);
                    PaintPanel(g, d, d.Last + 1e-3, canvas.Width, canvas.Height, true);
                }
                catch (Exception e)
                {
                    log.Error("Error while drawing the panel of a phase at its end.", e);
                }
                finally
                {
                    g.Restore(state);
                }
            }
        }

        /// <summary>
        /// The panel of a phase at a time of it (s from its origin), in units of a panel width x height. final: the picture of its end (no
        /// stage, its main time in place of the clock).
        /// </summary>
        private void PaintPanel(Graphics g, PanelData d, double now, float width, float height, bool final)
        {
            SwimTechPhase phase = d.Phase;
            bool wide = width >= 1100;
            RectangleF frame = new RectangleF(16, 16, width - 32, height - 32);
            SwimTheme.HudPanel(g, frame, SwimTheme.Accent, 1f, false);
            SwimTheme.CapsIn(g, "SWIM ANALYTICS", SwimTheme.DisplayLight(15f), SwimTheme.AccentPale, new RectangleF(40, 22, 400, 26), StringAlignment.Near, 0.2f);

            // The clock (the main time at the end) at the right, the title and the stage of the moment at the left.
            string clock, clockLabel;
            if (final && d.MainTime.HasValue)
            {
                clock = d.MainTime.Value.ToString("0.00", CultureInfo.InvariantCulture) + " s";
                clockLabel = d.MainLabel;
            }
            else
            {
                double shown = final ? d.Last : now;
                clock = (shown < 0 ? "−" : "") + Math.Abs(shown).ToString("0.00", CultureInfo.InvariantCulture) + " s";
                clockLabel = phase.Kind == SwimPhaseKind.Turn ? SwimLang.T("ΑΠΟ ΤΗΝ ΕΠΑΦΗ") : SwimLang.T("ΑΠΟ ΤΟ ΣΗΜΑ");
            }

            Font clockFont = SwimTheme.DisplayLight(wide ? 60f : 48f);
            SizeF cs = g.MeasureString(clock, clockFont);
            float clockLeft = frame.Right - cs.Width - 10;
            SwimTheme.GlowText(g, clock, clockFont, SwimTheme.AccentPale, SwimTheme.Accent, new PointF(clockLeft, wide ? 8 : 16), 0.45f);
            SwimTheme.CapsIn(g, clockLabel, SwimTheme.Ui(10.5f, FontStyle.Bold), SwimTheme.Dim, new RectangleF(frame.Right - 520, 98, 504, 22), StringAlignment.Far, 0.12f);

            Font titleFont = SwimTheme.Ui(wide ? 27f : 24f, FontStyle.Bold);
            string title = SwimLang.Upper(phase.Title ?? "");
            float titleRoom = Math.Max(60, clockLeft - 60);
            float titleW = Math.Min(titleRoom, SwimTheme.TrackedWidth(g, title, titleFont, 0.1f));
            SwimTheme.CapsIn(g, title, titleFont, SwimTheme.Amber, new RectangleF(40, 48, titleRoom, 56), StringAlignment.Near, 0.1f);
            if (!final)
            {
                string stage = StageAt(phase, now);
                if (!string.IsNullOrEmpty(stage))
                {
                    string text = SwimLang.Upper(SwimLang.Label(stage));
                    Font sf = SwimTheme.Ui(13.5f, FontStyle.Bold);
                    float tw = SwimTheme.TrackedWidth(g, text, sf, 0.08f);
                    RectangleF chip = new RectangleF(40 + titleW + 26, 55, tw + 32, 42);
                    if (chip.Right < clockLeft - 12)
                    {
                        using (GraphicsPath path = SwimTheme.Rounded(chip, 8))
                        using (SolidBrush fill = new SolidBrush(Color.FromArgb(14, 50, 64)))
                        using (Pen pen = new Pen(SwimTheme.Accent, 1.6f))
                        {
                            g.FillPath(fill, path);
                            g.DrawPath(pen, path);
                        }

                        SwimTheme.CapsIn(g, text, sf, SwimTheme.AccentPale, new RectangleF(chip.X + 16, chip.Y, tw + 6, chip.Height), StringAlignment.Near, 0.08f);
                    }
                }
            }

            using (Pen line = new Pen(Color.FromArgb(90, SwimTheme.Accent), 1.5f))
                g.DrawLine(line, 40, 126, frame.Right - 24, 126);

            PaintPanelTimeline(g, d, now, width);

            // The values (at the left, or at the top in a narrow panel) and the chart of the speed (at the right, or under them).
            bool chart = d.Speed != null || d.Segments.Count > 0;
            const float top = 222;
            int groups = Math.Max(1, d.Groups.Count);
            float cellW, step;
            RectangleF chartBox = RectangleF.Empty;
            if (!chart)
            {
                cellW = (width - 80) / 4f;
                step = Math.Max(118f, Math.Min(160f, (height - top - 40) / groups));
            }
            else if (wide)
            {
                cellW = 146;
                step = Math.Max(118f, Math.Min(150f, (height - top - 30) / groups));
                chartBox = new RectangleF(642, 212, width - 26 - 642, height - 28 - 212);
            }
            else
            {
                cellW = (width - 80) / 4f;
                step = 118;
                float chartTop = top + groups * step + 4;
                chartBox = new RectangleF(26, chartTop, width - 52, height - 28 - chartTop);
            }

            PaintPanelValues(g, d, now, cellW, step, top);
            if (d.Groups.Count == 0)
            {
                using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
                    g.DrawString(SwimLang.T("Οι χρόνοι μπαίνουν εδώ καθώς περνούν."), SwimTheme.Ui(13f, FontStyle.Regular), dim, 40, top);
            }

            if (chart && chartBox.Width > 200 && chartBox.Height > 160)
                PaintPanelSpeed(g, d, now, chartBox);
        }

        /// <summary>The line of time of the phase: the events passed (their names above it, or under it where they would touch), the kicks.</summary>
        private static void PaintPanelTimeline(Graphics g, PanelData d, double now, float width)
        {
            float x0 = 52, x1 = width - 52, y = 176;
            Func<double, float> tx = t => x0 + (float)((Math.Max(d.T0, Math.Min(d.T1, t)) - d.T0) / (d.T1 - d.T0)) * (x1 - x0);
            using (Pen faint = new Pen(SwimTheme.Faint, 3f))
                g.DrawLine(faint, x0, y, x1, y);
            using (Pen done = new Pen(SwimTheme.Accent, 4f))
                g.DrawLine(done, x0, y, tx(Math.Min(now, d.T1)), y);

            Font lf = SwimTheme.Ui(12f, FontStyle.Regular);
            float[] taken = { -999f, -999f };
            using (SolidBrush amber = new SolidBrush(SwimTheme.Amber))
            using (SolidBrush text = new SolidBrush(SwimTheme.Text))
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            {
                foreach (PanelMark m in d.Events.Where(q => q.T <= now + 1e-6 && q.T >= d.T0 - 1e-6 && q.T <= d.T1 + 1e-6).OrderBy(q => q.T))
                {
                    float x = tx(m.T);
                    g.FillPolygon(amber, new[] { new PointF(x, y - 8), new PointF(x + 7, y), new PointF(x, y + 8), new PointF(x - 7, y) });
                    float w = g.MeasureString(m.Label, lf).Width;
                    for (int row = 0; row < 2; row++)
                    {
                        if (x - w / 2 > taken[row] + 6)
                        {
                            g.DrawString(m.Label, lf, row == 0 ? text : dim, x - w / 2, row == 0 ? y - 36 : y + 11);
                            taken[row] = x + w / 2;
                            break;
                        }
                    }
                }
            }

            using (Pen tick = new Pen(SwimTheme.AccentLight, 2f))
            {
                foreach (double k in d.Kicks.Where(t => t <= now + 1e-6))
                    g.DrawLine(tick, tx(k), y - 6, tx(k), y + 6);
            }

            float cx = tx(now);
            using (SolidBrush dot = new SolidBrush(SwimTheme.AccentPale))
                g.FillEllipse(dot, cx - 7, y - 7, 14, 14);
        }

        /// <summary>The values in their groups, big: each from its moment, a dash before it (the places stay as they are).</summary>
        private static void PaintPanelValues(Graphics g, PanelData d, double now, float cellW, float step, float top)
        {
            Font groupFont = SwimTheme.Ui(13f, FontStyle.Bold);
            Font labelFont = SwimTheme.Ui(10.5f, FontStyle.Bold);
            Font valueFont = SwimTheme.DisplayLight(37.5f);
            Font unitFont = SwimTheme.Ui(13.5f, FontStyle.Regular);
            float vem = valueFont.SizeInPoints * g.DpiY / 72f, uem = unitFont.SizeInPoints * g.DpiY / 72f;
            float vasc = vem * valueFont.FontFamily.GetCellAscent(valueFont.Style) / (float)valueFont.FontFamily.GetEmHeight(valueFont.Style);
            float uasc = uem * unitFont.FontFamily.GetCellAscent(unitFont.Style) / (float)unitFont.FontFamily.GetEmHeight(unitFont.Style);
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            using (SolidBrush faint = new SolidBrush(SwimTheme.Faint))
            using (StringFormat tight = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces })
            {
                for (int gi = 0; gi < d.Groups.Count; gi++)
                {
                    PanelGroup group = d.Groups[gi];
                    float gy = top + gi * step;
                    SwimTheme.CapsIn(g, group.Title, groupFont, SwimTheme.Amber, new RectangleF(40, gy - 2, cellW * 4, 24), StringAlignment.Near, 0.16f);
                    for (int j = 0; j < group.Values.Count && j < 4; j++)
                    {
                        PanelValue v = group.Values[j];
                        float x = 40 + j * cellW;
                        SwimTheme.CapsIn(g, v.Label, labelFont, SwimTheme.Dim, new RectangleF(x, gy + 24, cellW - 8, 20), StringAlignment.Near, 0.04f);
                        string text = v.Text;
                        bool known = now >= v.At - 1e-6;
                        if (v.Kicks)
                        {
                            int n = d.Kicks.Count(t => t <= now + 1e-6);
                            known = n > 0 || known;
                            text = (d.Kicks.Count > 0 ? n : 0).ToString(CultureInfo.InvariantCulture);
                        }

                        float vy = gy + 42;
                        if (!known)
                        {
                            g.DrawString("–", valueFont, faint, x - 4, vy);
                            continue;
                        }

                        SwimTheme.GlowText(g, text, valueFont, SwimTheme.Text, SwimTheme.Accent, new PointF(x - 6, vy), 0.3f);
                        if (!string.IsNullOrEmpty(v.Unit))
                        {
                            float vw = g.MeasureString(text, valueFont, PointF.Empty, tight).Width;
                            float pad = Math.Max(0, (g.MeasureString(text, valueFont).Width - vw) / 2);
                            g.DrawString(v.Unit, unitFont, dim, x - 6 + pad + vw + 4, vy + vasc - uasc);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The chart of the speed (m/s): the line from the measured points as far as now, the mean of each segment passed with its value, the
        /// lines of the events, the speed of the moment big and the highest so far.
        /// </summary>
        private static void PaintPanelSpeed(Graphics g, PanelData d, double now, RectangleF box)
        {
            using (GraphicsPath path = SwimTheme.Rounded(box, 8))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(9, 17, 29)))
            using (Pen border = new Pen(Color.FromArgb(30, 60, 80), 1.2f))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            Font titleFont = SwimTheme.Ui(13f, FontStyle.Bold);
            float tw = SwimTheme.TrackedWidth(g, SwimLang.T("ΤΑΧΥΤΗΤΑ"), titleFont, 0.16f);
            SwimTheme.CapsIn(g, SwimLang.T("ΤΑΧΥΤΗΤΑ"), titleFont, SwimTheme.Amber, new RectangleF(box.X + 20, box.Y + 10, tw + 8, 26), StringAlignment.Near, 0.16f);
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
                g.DrawString("m/s", SwimTheme.Ui(13f, FontStyle.Regular), dim, box.X + 20 + tw + 16, box.Y + 11);

            RectangleF plot = RectangleF.FromLTRB(box.X + 56, box.Y + 56, box.Right - 24, box.Bottom - 40);
            double top = Math.Max(1.0, d.SpeedTop);
            double stepV = top <= 3.2 ? 0.5 : 1.0;
            double vmax = Math.Max(2 * stepV, Math.Ceiling(top * 1.04 / stepV) * stepV);
            Func<double, float> px = t => plot.Left + (float)((t - d.T0) / (d.ChartTo - d.T0)) * plot.Width;
            Func<double, float> py = v => plot.Bottom - (float)(Math.Max(0, Math.Min(vmax, v)) / vmax) * plot.Height;

            Font axis = SwimTheme.Ui(11f, FontStyle.Regular);
            Font small = SwimTheme.Ui(10.5f, FontStyle.Bold);
            using (Pen grid = new Pen(Color.FromArgb(22, 40, 54), 1f))
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
            {
                for (double v = 0; v <= vmax + 1e-6; v += stepV)
                {
                    g.DrawLine(grid, plot.Left, py(v), plot.Right, py(v));
                    g.DrawString(v.ToString(stepV < 1 ? "0.0" : "0", CultureInfo.InvariantCulture), axis, dim, new RectangleF(plot.Left - 54, py(v) - 10, 46, 20), right);
                }

                // The lines of the events (dashed), their names at the top (one under the other where they would touch).
                float lastRight = -999f;
                int row = 0;
                using (Pen dash = new Pen(Color.FromArgb(70, 100, 120), 1f) { DashStyle = DashStyle.Dash })
                {
                    foreach (PanelMark m in d.Lines.Where(q => q.T <= now + 1e-6 && q.T >= d.T0 && q.T <= d.ChartTo).OrderBy(q => q.T))
                    {
                        float x = px(m.T);
                        g.DrawLine(dash, x, plot.Top, x, plot.Bottom);
                        float w = g.MeasureString(m.Label, small).Width;
                        row = x + 4 < lastRight + 6 ? row + 1 : 0;
                        g.DrawString(m.Label, small, dim, x + 4, plot.Top + 2 + 18 * row);
                        lastRight = Math.Max(lastRight, x + 4 + w);
                    }

                    // (2.24.1) Where the line of the speed ends (the 15): its line at the right, its distance at the top, left of it.
                    PanelMark endMark = d.Speed != null ? d.Measured.FirstOrDefault(q => Math.Abs(q.T - d.ChartTo) < 1e-6) : null;
                    if (endMark != null && endMark.T <= now + 1e-6)
                    {
                        float x = px(endMark.T);
                        g.DrawLine(dash, x, plot.Top, x, plot.Bottom);
                        float w = g.MeasureString(endMark.Label, small).Width;
                        float left = x - 4 - w;
                        int endRow = left < lastRight + 6 ? row + 1 : 0;
                        g.DrawString(endMark.Label, small, dim, left, plot.Top + 2 + 18 * endRow);
                    }
                }
            }

            // The mean of each segment passed, with its value under it (where each value is written: the speed of the moment keeps off it).
            Font segFont = SwimTheme.Ui(13.5f, FontStyle.Regular);
            List<RectangleF> segmentLabels = new List<RectangleF>();
            using (Pen seg = new Pen(Color.FromArgb(150, 112, 40), 3f))
            using (SolidBrush amber = new SolidBrush(SwimTheme.Amber))
            {
                foreach (PanelSegment s in d.Segments.Where(q => q.To <= now + 1e-6 && q.From < d.ChartTo))
                {
                    float a = px(Math.Max(d.T0, s.From)), b = px(Math.Min(d.ChartTo, s.To)), y = py(s.Speed);
                    if (b - a < 4)
                        continue;
                    g.DrawLine(seg, a, y, b, y);
                    string text = s.Speed.ToString("0.00", CultureInfo.InvariantCulture);
                    SizeF ts = g.MeasureString(text, segFont);
                    double mid = (s.From + s.To) / 2;
                    bool lineUnder = d.Speed != null && Math.Abs(d.Speed(mid)) < s.Speed;
                    float ty = Math.Max(plot.Top + 2, Math.Min(plot.Bottom - ts.Height, lineUnder ? y - ts.Height - 2 : y + 5));
                    float tx = (a + b) / 2 - ts.Width / 2;
                    g.DrawString(text, segFont, amber, tx, ty);
                    segmentLabels.Add(new RectangleF(tx, ty, ts.Width, ts.Height));
                }
            }

            // The line of the speed as far as now, glowing, filled under it.
            if (d.Speed != null)
            {
                double lo = Math.Max(d.T0, d.SpeedFrom), hi = Math.Min(d.ChartTo, d.SpeedTo), end = Math.Min(now, hi);
                List<PointF> pts = new List<PointF>();
                double best = -1, bestT = lo;
                const int samples = 240;
                for (int i = 0; i <= samples; i++)
                {
                    double t = lo + (hi - lo) * i / samples;
                    if (t > end)
                        break;
                    double v = Math.Abs(d.Speed(t));
                    pts.Add(new PointF(px(t), py(v)));
                    if (v > best)
                    {
                        best = v;
                        bestT = t;
                    }
                }

                if (end > lo && (pts.Count == 0 || px(end) > pts[pts.Count - 1].X + 0.5f))
                    pts.Add(new PointF(px(end), py(Math.Abs(d.Speed(end)))));

                if (pts.Count >= 2)
                {
                    List<PointF> area = new List<PointF>(pts) { new PointF(pts[pts.Count - 1].X, plot.Bottom), new PointF(pts[0].X, plot.Bottom) };
                    using (SolidBrush under = new SolidBrush(Color.FromArgb(34, SwimTheme.Accent)))
                        g.FillPolygon(under, area.ToArray());
                    using (Pen glow = new Pen(Color.FromArgb(60, SwimTheme.Accent), 10f) { LineJoin = LineJoin.Round })
                    using (Pen pen = new Pen(SwimTheme.AccentPale, 3.5f) { LineJoin = LineJoin.Round })
                    {
                        g.DrawLines(glow, pts.ToArray());
                        g.DrawLines(pen, pts.ToArray());
                    }

                    double vnow = Math.Abs(d.Speed(end));
                    PointF last = pts[pts.Count - 1];
                    using (SolidBrush white = new SolidBrush(SwimTheme.Text))
                    using (Pen ring = new Pen(SwimTheme.Accent, 2f))
                    {
                        g.FillEllipse(white, last.X - 8, last.Y - 8, 16, 16);
                        g.DrawEllipse(ring, last.X - 8, last.Y - 8, 16, 16);
                    }

                    Font big = SwimTheme.DisplayLight(34.5f);
                    string vt = vnow.ToString("0.00", CultureInfo.InvariantCulture);
                    SizeF vs = g.MeasureString(vt, big);
                    float bx = Math.Min(last.X + 12, plot.Right - vs.Width - 8), by = Math.Max(plot.Top + 22, last.Y - vs.Height - 14);

                    // (2.24.1) Over the value of a segment it would touch (the line levels off at the 15, close to the mean of the last
                    // segment): the lowest fifth of the box of the big digits is empty.
                    foreach (RectangleF label in segmentLabels.OrderByDescending(r => r.Top))
                    {
                        if (new RectangleF(bx, by, vs.Width, vs.Height * 0.8f).IntersectsWith(label))
                            by = Math.Max(plot.Top + 22, label.Top - vs.Height * 0.8f - 2);
                    }
                    SwimTheme.GlowText(g, vt, big, SwimTheme.Text, SwimTheme.Accent, new PointF(bx, by), 0.4f);
                    RectangleF nowBox = new RectangleF(bx, by, vs.Width, vs.Height);

                    if (best > vnow + 0.05)
                    {
                        float mx = px(bestT), my = py(best);
                        Font mf = SwimTheme.Ui(13.5f, FontStyle.Bold);
                        string mt = string.Format(SwimLang.T("μέγιστη {0}"), best.ToString("0.00", CultureInfo.InvariantCulture));
                        SizeF ms = g.MeasureString(mt, mf);
                        using (Pen ring = new Pen(SwimTheme.Amber, 2f))
                            g.DrawEllipse(ring, mx - 5, my - 5, 10, 10);
                        // At the right of the point, else under it, else at its left; not over the speed of the moment.
                        RectangleF[] places =
                        {
                            new RectangleF(Math.Min(mx + 12, plot.Right - ms.Width), Math.Max(my - 12, plot.Top + 40), ms.Width, ms.Height),
                            new RectangleF(Math.Min(mx + 12, plot.Right - ms.Width), my + 8, ms.Width, ms.Height),
                            new RectangleF(Math.Max(plot.Left, mx - 12 - ms.Width), Math.Max(my - 12, plot.Top + 40), ms.Width, ms.Height)
                        };
                        RectangleF place = places.FirstOrDefault(r => !r.IntersectsWith(nowBox) && r.Bottom <= plot.Bottom);
                        if (!place.IsEmpty)
                        {
                            using (SolidBrush amber = new SolidBrush(SwimTheme.Amber))
                                g.DrawString(mt, mf, amber, place.X, place.Y);
                        }
                    }
                }

                // The measured points on the line, their distances under the axis.
                using (SolidBrush amber = new SolidBrush(SwimTheme.Amber))
                using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
                {
                    foreach (PanelMark m in d.Measured.Where(q => q.T <= now + 1e-6 && q.T >= lo - 1e-6 && q.T <= hi + 1e-6))
                    {
                        float x = px(m.T), y = py(Math.Abs(d.Speed(m.T)));
                        g.FillEllipse(amber, x - 6, y - 6, 12, 12);
                        float w = g.MeasureString(m.Label, small).Width;
                        g.DrawString(m.Label, small, dim, Math.Max(plot.Left, Math.Min(plot.Right - w, x - w / 2)), plot.Bottom + 8);
                    }
                }
            }

            using (Pen axisLine = new Pen(Color.FromArgb(60, 90, 110), 1f))
                g.DrawLine(axisLine, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
        }
        #endregion

        #region The panel at the end of the video and in the report
        /// <summary>
        /// (2.24.0) The attempt of a kind whose panel goes to the report: the only one, else the best one (a turn: −5 → +15, else wall → +15,
        /// −5 → +10, wall → +10, the turn time; a start: the 15, else the 10, the 5, the release). Null: none with values.
        /// </summary>
        private SwimTechPhase BestPhase(SwimPhaseKind kind)
        {
            if (TechPhases == null)
                return null;

            List<SwimTechPhase> list = TechPhases.Where(p => p.Kind == kind && p.Values.Count > 0).ToList();
            if (list.Count <= 1)
                return list.FirstOrDefault();

            string[] keys = kind == SwimPhaseKind.Turn
                ? new[] { "inOut15", "out15", "inOut10", "out10", "turnTime" }
                : new[] { "t15", "t10", "t5", "blockTime" };
            foreach (string key in keys)
            {
                List<SwimTechPhase> with = list.Where(p => p.Value(key).HasValue).ToList();
                if (with.Count > 0)
                    return with.OrderBy(p => p.Value(key).Value).First();
            }

            return list[0];
        }

        /// <summary>(2.24.0) The start or the turn whose panel closes a video of the part [from, to] of a training video: the last one in it.</summary>
        private SwimTechPhase ClosingPhase(long from, long to)
        {
            if (Race == null || !Race.IsTraining || TechPhases == null || Timing == null || !Timing.IsValid)
                return null;
            return TechPhases.Where(p => p.WindowStart <= to && p.WindowEnd >= from && p.Values.Count > 0).OrderBy(p => p.Origin).LastOrDefault();
        }

        /// <summary>
        /// (2.24.0) The panels of a technique analysis for its report (PNG at the size of the graphics of the report): "panel-start" and
        /// "panel-turn", each of its best attempt.
        /// </summary>
        private Dictionary<string, byte[]> RenderPanelGraphics()
        {
            Dictionary<string, byte[]> images = new Dictionary<string, byte[]>();
            if (TechPhases == null || Timing == null || !Timing.IsValid)
                return images;

            foreach (SwimPhaseKind kind in new[] { SwimPhaseKind.Start, SwimPhaseKind.Turn })
            {
                SwimTechPhase best = BestPhase(kind);
                if (best == null)
                    continue;
                try
                {
                    images[kind == SwimPhaseKind.Start ? "panel-start" : "panel-turn"] = RenderGraphic(g => PaintFinalPanel(g, ReportGraphicSize, best));
                }
                catch (Exception e)
                {
                    log.Error("Could not draw the panel of the report.", e);
                }
            }

            return images;
        }

        private static float Ease(float u)
        {
            u = Math.Max(0f, Math.Min(1f, u));
            return u * u * (3 - 2 * u);
        }
        #endregion
    }
}
