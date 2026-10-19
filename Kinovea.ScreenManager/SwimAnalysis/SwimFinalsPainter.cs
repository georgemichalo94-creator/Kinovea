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
    /// <summary>Difference athlete − reference at a checkpoint (s, negative = faster).</summary>
    public sealed class SwimDeltaPoint
    {
        public int D;
        public double Delta;
    }

    /// <summary>
    /// The graphics of the comparisons: the board of a checkpoint with the swimmers of the final (video), the Δ with the reference
    /// at each checkpoint (video and panel) and the values of the race against the final, phase by phase (panel).
    /// </summary>
    public static class SwimFinalsPainter
    {
        private static readonly Color Faster = Color.FromArgb(255, 32, 168, 110);
        private static readonly Color Slower = Color.FromArgb(255, 214, 64, 64);
        private static readonly Color Finalist = Color.FromArgb(255, 0, 200, 255);

        /// <summary>The colours of a Δ: faster (green) and slower (red) than the reference.</summary>
        public static Color FasterColor
        {
            get { return Faster; }
        }

        public static Color SlowerColor
        {
            get { return Slower; }
        }

        /// <summary>Title of the board of a checkpoint: "50 M · ΠΑΡΙΣΙ 2024", at the finish "ΤΕΡΜΑΤΙΣΜΟΣ · ΠΑΡΙΣΙ 2024".</summary>
        public static string BoardTitle(SwimFinal final, int distance, bool finish)
        {
            string source = SwimAthleteList.Normalize(final.Short);
            return finish ? SwimLang.T("ΤΕΡΜΑΤΙΣΜΟΣ · ") + source : distance.ToString(CultureInfo.InvariantCulture) + " M · " + source;
        }

        /// <summary>The event of the final: "Τελικός 100μ. ύπτιο Ανδρών".</summary>
        public static string BoardSubtitle(SwimFinal final)
        {
            return string.Format(CultureInfo.InvariantCulture, SwimLang.T("Τελικός {0}μ. {1} {2}"), final.Distance, SwimPresets.StyleName(final.Style).ToLowerInvariant(),
                SwimLang.Label(final.Gender == "F" ? "Γυναικών" : "Ανδρών"));
        }

        /// <summary>Translucent dark card with an accent on the left edge (the style of the race board of the video).</summary>
        public static void Card(Graphics g, Rectangle r, Color accent)
        {
            using (GraphicsPath path = SwimTheme.Rounded(r, 7))
            using (LinearGradientBrush back = new LinearGradientBrush(r, Color.FromArgb(232, 9, 14, 22), Color.FromArgb(205, 18, 30, 45), LinearGradientMode.Horizontal))
            using (Pen border = new Pen(Color.FromArgb(90, 255, 255, 255), 1f))
            {
                g.FillPath(back, path);
                g.DrawPath(border, path);
            }

            using (SolidBrush a = new SolidBrush(accent))
                g.FillRectangle(a, r.Left + 1, r.Top + 7, 3, r.Height - 14);
        }

        #region Board of a checkpoint (video)
        /// <summary>Height of the board for a number of lines at a scale (1 = a video of about 1000 pixels wide).</summary>
        public static int BoardHeight(int rows, float scale)
        {
            return (int)Math.Ceiling((36 + rows * 15.5f + 6) * scale);
        }

        /// <summary>
        /// The swimmers of the final at a checkpoint and the athlete among them, as on the television: place, name, time and the gap
        /// to the first; the line of the athlete in amber.
        /// </summary>
        public static void PaintBoard(Graphics g, Rectangle r, string title, string subtitle, IList<SwimFinalsBoardRow> rows, float scale)
        {
            if (rows == null || rows.Count == 0 || r.Width < 60 || r.Height < 40)
                return;

            Card(g, r, SwimTheme.Amber);
            Font caption = SwimTheme.Ui(6.75f * scale, FontStyle.Bold);
            Font sub = SwimTheme.Ui(6.5f * scale, FontStyle.Regular);
            Font name = SwimTheme.Ui(7.5f * scale, FontStyle.Bold);
            Font time = SwimTheme.Display(8.5f * scale);
            Font place = SwimTheme.Ui(6.5f * scale, FontStyle.Bold);
            float left = r.Left + 10 * scale, right = r.Right - 8 * scale;
            using (SolidBrush text = new SolidBrush(SwimTheme.Text))
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            using (SolidBrush amber = new SolidBrush(SwimTheme.Amber))
            using (StringFormat noWrap = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
            {
                g.DrawString(title ?? "", caption, amber, new RectangleF(left, r.Top + 5 * scale, right - left, 12 * scale), noWrap);
                g.DrawString(subtitle ?? "", sub, dim, new RectangleF(left, r.Top + 17 * scale, right - left, 12 * scale), noWrap);

                float top = r.Top + 34 * scale;
                float rowH = Math.Max(8, (r.Bottom - 5 * scale - top) / rows.Count);
                float timeWidth = 52 * scale, behindWidth = 40 * scale;
                for (int i = 0; i < rows.Count; i++)
                {
                    SwimFinalsBoardRow row = rows[i];
                    RectangleF line = new RectangleF(r.Left + 5 * scale, top + i * rowH, r.Width - 10 * scale, rowH - 1);
                    if (row.IsAthlete)
                        SwimTheme.FillRounded(g, Color.FromArgb(150, 120, 84, 0), line, 3 * scale);
                    else if (i % 2 == 1)
                        SwimTheme.FillRounded(g, Color.FromArgb(40, 255, 255, 255), line, 3 * scale);

                    // Place in a small circle.
                    float d = Math.Min(rowH - 3, 12 * scale);
                    RectangleF circle = new RectangleF(left, line.Top + (rowH - 1 - d) / 2, d, d);
                    using (SolidBrush disk = new SolidBrush(row.IsAthlete ? SwimTheme.Amber : Color.FromArgb(200, 39, 57, 79)))
                        g.FillEllipse(disk, circle);
                    string p = row.Place.ToString(CultureInfo.InvariantCulture);
                    SizeF ps = g.MeasureString(p, place);
                    g.DrawString(p, place, row.IsAthlete ? Brushes.Black : text, circle.Left + (d - ps.Width) / 2 + 0.5f, circle.Top + (d - ps.Height) / 2);

                    float nameLeft = circle.Right + 5 * scale;
                    float nameRight = right - timeWidth - behindWidth;
                    g.DrawString(row.IsAthlete ? row.Name.ToUpperInvariant() : row.Name, name, row.IsAthlete ? amber : text,
                        new RectangleF(nameLeft, line.Top + (rowH - 1 - name.GetHeight(g)) / 2, Math.Max(10, nameRight - nameLeft), name.GetHeight(g) + 1), noWrap);

                    string t = SwimReference.FormatTime(row.Time);
                    SizeF ts = g.MeasureString(t, time);
                    g.DrawString(t, time, row.IsAthlete ? amber : text, right - behindWidth - ts.Width, line.Top + (rowH - 1 - ts.Height) / 2);

                    string b = row.Place == 1 && Math.Abs(row.Behind) < 0.005 ? "" :
                        (row.Behind < 0 ? "−" : "+") + Math.Abs(row.Behind).ToString("0.00", CultureInfo.InvariantCulture);
                    if (b.Length > 0)
                    {
                        SizeF bs = g.MeasureString(b, sub);
                        g.DrawString(b, sub, row.IsAthlete ? amber : dim, right - bs.Width, line.Top + (rowH - 1 - bs.Height) / 2);
                    }
                }
            }
        }
        #endregion

        #region Δ at each checkpoint (video and panel)
        /// <summary>
        /// Δ with the reference at each checkpoint passed: a bar per checkpoint at its distance, up and green when faster, down and
        /// red when slower, the value of the last one written. 'card': on the video (dark card), else on the panel.
        /// </summary>
        public static void PaintDelta(Graphics g, Rectangle r, IList<SwimDeltaPoint> points, int distance, int pool, string reference, bool card, float scale)
        {
            if (r.Width < 80 || r.Height < 40 || distance <= 0)
                return;

            if (card)
                Card(g, r, SwimTheme.Accent);
            else
                using (SolidBrush back = new SolidBrush(SwimTheme.Surface))
                    g.FillRectangle(back, r);

            Font caption = SwimTheme.Ui(6.75f * scale, FontStyle.Bold);
            Font small = SwimTheme.Ui(6.5f * scale, FontStyle.Regular);
            Font value = SwimTheme.Display(9f * scale);
            float left = r.Left + 10 * scale;
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            using (SolidBrush accent = new SolidBrush(SwimTheme.Accent))
            using (StringFormat noWrap = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
            {
                string head = SwimLang.T("Δ ΑΠΟ ΤΗΝ ΑΝΑΦΟΡΑ (s)");
                g.DrawString(head, caption, dim, left, r.Top + 5 * scale);
                SizeF hs = g.MeasureString(head, caption);
                if (!string.IsNullOrEmpty(reference))
                    g.DrawString("vs " + reference, small, accent, new RectangleF(left + hs.Width + 6 * scale, r.Top + 5.5f * scale, Math.Max(10, r.Right - 8 * scale - left - hs.Width - 6 * scale), 12 * scale), noWrap);

                RectangleF plot = new RectangleF(r.Left + 34 * scale, r.Top + 22 * scale, r.Width - 34 * scale - 10 * scale, r.Height - 22 * scale - 16 * scale);
                if (plot.Width < 40 || plot.Height < 16)
                    return;

                List<SwimDeltaPoint> list = points == null ? new List<SwimDeltaPoint>() : points.Where(p => p.D > 0 && p.D <= distance).OrderBy(p => p.D).ToList();
                double max = list.Count > 0 ? list.Max(p => Math.Abs(p.Delta)) : 0.5;
                double top = new double[] { 0.1, 0.2, 0.5, 1, 2, 5, 10, 20 }.FirstOrDefault(v => v >= max);
                if (top <= 0)
                    top = Math.Ceiling(max);
                float zero = plot.Top + plot.Height / 2;
                Func<double, float> X = d => plot.Left + (float)(d / distance * plot.Width);
                Func<double, float> Y = v => zero - (float)(Math.Max(-top, Math.Min(top, v)) / top * plot.Height / 2);

                // Axis: zero, ± the scale, the walls and the distances.
                using (Pen grid = new Pen(Color.FromArgb(38, 255, 255, 255), 1f))
                using (Pen axis = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                using (Pen wall = new Pen(Color.FromArgb(90, 0, 200, 255), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(grid, plot.Left, plot.Top, plot.Right, plot.Top);
                    g.DrawLine(grid, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
                    for (int w = pool; pool > 0 && w < distance; w += pool)
                        g.DrawLine(wall, X(w), plot.Top, X(w), plot.Bottom);
                    g.DrawLine(axis, plot.Left, zero, plot.Right, zero);
                }

                foreach (KeyValuePair<double, string> label in new[] { new KeyValuePair<double, string>(top, "−"), new KeyValuePair<double, string>(-top, "+") })
                {
                    string text = label.Value + top.ToString(top < 1 ? "0.0" : "0", CultureInfo.InvariantCulture);
                    SizeF ls = g.MeasureString(text, small);
                    g.DrawString(text, small, dim, plot.Left - 3 * scale - ls.Width, Y(label.Key) - ls.Height / 2);
                }

                int step = new int[] { 25, 50, 100, 200, 250, 500 }.FirstOrDefault(s => distance / s <= 5);
                if (step <= 0)
                    step = 500;
                for (int d = 0; d <= distance; d += step)
                {
                    string text = d.ToString(CultureInfo.InvariantCulture);
                    SizeF ls = g.MeasureString(text, small);
                    g.DrawString(text, small, dim, X(d) - ls.Width / 2, plot.Bottom + 1);
                }

                if (list.Count == 0)
                {
                    string hint = string.IsNullOrEmpty(reference) ? SwimLang.T("διάλεξε αναφορά για το Δ") : SwimLang.T("το Δ εμφανίζεται με τα περάσματα");
                    SizeF hs2 = g.MeasureString(hint, small);
                    g.DrawString(hint, small, dim, plot.Left + (plot.Width - hs2.Width) / 2, zero - hs2.Height - 2);
                    return;
                }

                // Bars: as wide as the space to the next checkpoint allows.
                float barWidth = Math.Max(2, Math.Min(10 * scale, plot.Width / Math.Max(4, distance / 10f) * 0.8f));
                for (int i = 0; i < list.Count; i++)
                {
                    SwimDeltaPoint p = list[i];
                    float x = X(p.D);
                    float y = Y(-p.Delta);
                    RectangleF bar = new RectangleF(x - barWidth / 2, Math.Min(y, zero), barWidth, Math.Max(1, Math.Abs(zero - y)));
                    bool last = i == list.Count - 1;
                    Color c = p.Delta <= 0 ? Faster : Slower;
                    using (SolidBrush b = new SolidBrush(last ? c : Color.FromArgb(190, c)))
                        g.FillRectangle(b, bar);
                }

                // The value of the last checkpoint passed.
                SwimDeltaPoint lastPoint = list[list.Count - 1];
                string valueText = SwimReference.FormatDelta(lastPoint.Delta);
                SizeF vs = g.MeasureString(valueText, value);
                float vx = Math.Min(plot.Right - vs.Width - 4, Math.Max(plot.Left, X(lastPoint.D) - vs.Width / 2));
                float vy = lastPoint.Delta <= 0 ? Math.Max(plot.Top, Y(-lastPoint.Delta) - vs.Height - 2) : Math.Min(plot.Bottom - vs.Height, Y(-lastPoint.Delta) + 2);
                RectangleF pill = new RectangleF(vx - 3, vy, vs.Width + 6, vs.Height);
                SwimTheme.FillRounded(g, lastPoint.Delta <= 0 ? Faster : Slower, pill, 4);
                g.DrawString(valueText, value, Brushes.White, vx, vy);
            }
        }
        #endregion

        #region The race against the final (panel)
        private const float GroupHeight = 24;
        private const float RowHeight = 30;
        private const float HeaderHeight = 34;

        /// <summary>Height of the comparison for these values (pixels).</summary>
        public static int MetricsHeight(IList<SwimFinalsMetric> metrics)
        {
            if (metrics == null || metrics.Count == 0)
                return 60;
            // A header at each change of phase, as painted.
            int groups = 0;
            string group = null;
            foreach (SwimFinalsMetric m in metrics)
            {
                if (m.Group != group)
                {
                    group = m.Group;
                    groups++;
                }
            }

            return (int)Math.Ceiling(HeaderHeight + groups * GroupHeight + metrics.Count * RowHeight + 8);
        }

        /// <summary>
        /// The values of the race against the swimmers of the final, phase by phase: each value on a line between the lowest and the
        /// highest (dots: the swimmers of the final, the winner larger; amber: the athlete), with the place the athlete would have
        /// and the difference from the best and the mean.
        /// </summary>
        public static void PaintMetrics(Graphics g, Rectangle r, IList<SwimFinalsMetric> metrics, string title, string athlete)
        {
            using (SolidBrush back = new SolidBrush(SwimTheme.Back))
                g.FillRectangle(back, r);

            Font titleFont = SwimTheme.Ui(9.5f, FontStyle.Bold);
            Font groupFont = SwimTheme.Ui(8.25f, FontStyle.Bold);
            Font labelFont = SwimTheme.Ui(8.25f, FontStyle.Regular);
            Font valueFont = SwimTheme.Display(10f);
            Font small = SwimTheme.Ui(7.25f, FontStyle.Regular);
            Font rankFont = SwimTheme.Display(11f);
            using (SolidBrush text = new SolidBrush(SwimTheme.Text))
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            using (SolidBrush amber = new SolidBrush(SwimTheme.Amber))
            using (SolidBrush accent = new SolidBrush(SwimTheme.Accent))
            using (SolidBrush finalist = new SolidBrush(Color.FromArgb(200, Finalist)))
            using (Pen line = new Pen(SwimTheme.Border, 2f))
            using (Pen tick = new Pen(Color.FromArgb(120, SwimTheme.Dim), 1f))
            using (StringFormat noWrap = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
            {
                g.DrawString(title ?? "", titleFont, accent, new RectangleF(r.Left + 8, r.Top + 6, r.Width - 16, 18), noWrap);
                string legend = SwimLang.T("● τελικός (μεγάλος: ο νικητής)   ● ") + (string.IsNullOrWhiteSpace(athlete) ? SwimLang.T("αθλητής") : athlete.Trim());
                g.DrawString(legend, small, dim, new RectangleF(r.Left + 8, r.Top + 20, r.Width - 16, 14), noWrap);
                if (metrics == null || metrics.Count == 0)
                    return;

                float y = r.Top + HeaderHeight;
                float labelWidth = Math.Min(230, r.Width * 0.34f);
                float rightWidth = Math.Min(150, r.Width * 0.24f);
                float stripLeft = r.Left + 8 + labelWidth + 10, stripRight = r.Right - rightWidth - 14;
                string group = null;
                foreach (SwimFinalsMetric m in metrics)
                {
                    if (m.Group != group)
                    {
                        group = m.Group;
                        using (SolidBrush band = new SolidBrush(SwimTheme.Surface))
                            g.FillRectangle(band, r.Left + 4, y + 2, r.Width - 8, GroupHeight - 4);
                        g.DrawString(SwimLang.Label(group).ToUpperInvariant(), groupFont, text, r.Left + 10, y + 5);
                        y += GroupHeight;
                    }

                    // Label and the value of the athlete.
                    g.DrawString(SwimLang.Label(m.Label), labelFont, text, new RectangleF(r.Left + 10, y + 2, labelWidth - 70, 16), noWrap);
                    string unit = m.Unit == "s" ? "" : " " + SwimLang.T(m.Unit);
                    g.DrawString(m.Athlete.HasValue ? m.Format(m.Athlete) + unit : "–", valueFont, m.Athlete.HasValue ? amber : dim,
                        new RectangleF(r.Left + 10, y + 14, labelWidth, 16), noWrap);

                    // The line of the values.
                    List<double> all = m.Finalists.Where(v => v.HasValue).Select(v => v.Value).ToList();
                    if (m.Athlete.HasValue)
                        all.Add(m.Athlete.Value);
                    float cy = y + RowHeight / 2;
                    if (all.Count > 0 && stripRight - stripLeft > 40)
                    {
                        double lo = all.Min(), hi = all.Max();
                        double pad = Math.Max(1e-6, (hi - lo) * 0.08);
                        if (hi - lo < 1e-6)
                            pad = Math.Max(0.05, Math.Abs(hi) * 0.02);
                        lo -= pad;
                        hi += pad;
                        Func<double, float> X = v => stripLeft + (float)((v - lo) / (hi - lo) * (stripRight - stripLeft));
                        g.DrawLine(line, stripLeft, cy, stripRight, cy);
                        g.DrawString(m.Format(all.Min()), small, dim, stripLeft - 2, cy + 3);
                        string hiText = m.Format(all.Max());
                        SizeF his = g.MeasureString(hiText, small);
                        g.DrawString(hiText, small, dim, stripRight - his.Width + 2, cy + 3);

                        for (int i = m.Finalists.Count - 1; i >= 0; i--)
                        {
                            if (!m.Finalists[i].HasValue)
                                continue;
                            float x = X(m.Finalists[i].Value);
                            float d = i == 0 ? 10 : 7;
                            g.FillEllipse(finalist, x - d / 2, cy - d / 2, d, d);
                            if (i == 0)
                                g.DrawEllipse(Pens.White, x - d / 2, cy - d / 2, d, d);
                        }

                        if (m.Mean.HasValue)
                            g.DrawLine(tick, X(m.Mean.Value), cy - 7, X(m.Mean.Value), cy + 7);

                        if (m.Athlete.HasValue)
                        {
                            float x = X(m.Athlete.Value);
                            g.FillEllipse(amber, x - 6.5f, cy - 6.5f, 13, 13);
                            g.DrawEllipse(Pens.Black, x - 6.5f, cy - 6.5f, 13, 13);
                        }
                    }

                    // Place and differences.
                    float rx = r.Right - rightWidth - 4;
                    if (m.Athlete.HasValue && !m.Neutral && m.Rank > 0)
                    {
                        string rank = m.Rank.ToString(CultureInfo.InvariantCulture) + SwimLang.T("ος/") + (m.Count + 1).ToString(CultureInfo.InvariantCulture);
                        g.DrawString(rank, rankFont, m.Rank <= 3 ? amber : text, rx, y + 3);
                    }

                    if (m.Athlete.HasValue && m.Best.HasValue && m.Mean.HasValue)
                    {
                        double toBest = m.Athlete.Value - m.Best.Value, toMean = m.Athlete.Value - m.Mean.Value;
                        string diffs = m.Neutral
                            ? SwimLang.T("μ.ό. ") + m.Format(m.Mean)
                            : SwimLang.T("1ος ") + Signed(toBest, m.Decimals) + SwimLang.T(" · μ.ό. ") + Signed(toMean, m.Decimals);
                        g.DrawString(diffs, small, dim, new RectangleF(rx, y + 18, rightWidth, 13), noWrap);
                    }

                    y += RowHeight;
                }
            }
        }

        private static string Signed(double v, int decimals)
        {
            string f = decimals == 0 ? "0" : decimals == 1 ? "0.0" : "0.00";
            double limit = decimals == 0 ? 0.5 : decimals == 1 ? 0.05 : 0.005;
            return (v > limit ? "+" : v < -limit ? "−" : "±") + Math.Abs(v).ToString(f, CultureInfo.InvariantCulture);
        }
        #endregion
    }
}
