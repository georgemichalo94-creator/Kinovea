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
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (Swim 2.28.0) The report after an attempt, over the wall of cameras (as the timing systems of starts with a line on the swimmer
    /// show it): the speed of the head along the pool for the last five attempts in their colours, the lines 5, 10, 15 m with their
    /// times, the highest speed; beside it the times from the horn of the five attempts, the parts of the last one with their mean
    /// speed and its difference from the best. Back to the cameras, or a new swimmer (the attempts go). The cameras see the swimmer
    /// only in their images: between two cameras the curve is a dotted line, the times of the lines stay exact.
    /// </summary>
    public sealed class SwimAttemptReport : Control
    {
        public const int PartBack = 0, PartClear = 1, PartCount = 2;

        /// <summary>«Back to the cameras» or «new swimmer» was clicked.</summary>
        public event Action BackAsked, ClearAsked;

        private RectangleF[] parts = new RectangleF[PartCount];
        private int hover = -1;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public SwimAttemptReport()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = SwimTheme.Back;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            parts = Draw(e.Graphics, ClientRectangle, SwimLiveAttempts.List, Math.Max(1f, DeviceDpi / 96f), hover);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int part = Hit(e.Location);
            if (part != hover)
            {
                hover = part;
                Cursor = part >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != -1)
            {
                hover = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
                return;

            switch (Hit(e.Location))
            {
                case PartBack:
                    if (BackAsked != null)
                        BackAsked();
                    break;
                case PartClear:
                    if (ClearAsked != null)
                        ClearAsked();
                    break;
            }
        }

        private int Hit(Point point)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                if (!parts[i].IsEmpty && parts[i].Contains(point))
                    return i;
            }

            return -1;
        }

        /// <summary>The report in 'r' for these attempts (the last one is the attempt of the report); the places of its buttons.</summary>
        public static RectangleF[] Draw(Graphics g, Rectangle r, IList<SwimLiveAttempt> attempts, float scale, int hover = -1)
        {
            RectangleF[] buttons = new RectangleF[PartCount];
            if (g == null || r.Width < 200 || r.Height < 160)
                return buttons;

            List<SwimLiveAttempt> list = attempts == null ? new List<SwimLiveAttempt>() : attempts.Where(a => a != null).ToList();
            SmoothingMode smoothing = g.SmoothingMode;
            TextRenderingHint hint = g.TextRenderingHint;
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (SolidBrush back = new SolidBrush(SwimTheme.Back))
                    g.FillRectangle(back, r);

                float s = Math.Max(1f, scale) * Math.Max(0.75f, Math.Min(1.35f, Math.Min(r.Width / 1400f, r.Height / 780f)));
                float fs = s / Math.Max(1f, scale);
                float pad = 20 * s;
                SwimLiveAttempt attempt = list.Count > 0 ? list[list.Count - 1] : null;

                // The title and the main numbers.
                RectangleF head = new RectangleF(r.Left + pad, r.Top + pad * 0.8f, r.Width - 2 * pad, 62 * s);
                DrawHead(g, head, attempt, list, s, fs);

                // The buttons, along the bottom.
                float buttonsHeight = 40 * s;
                RectangleF bottom = new RectangleF(r.Left + pad, r.Bottom - pad * 0.7f - buttonsHeight, r.Width - 2 * pad, buttonsHeight);
                buttons = DrawButtons(g, bottom, hover, s, fs);

                // The graph of the speed and the table of the times.
                float top = head.Bottom + 12 * s;
                RectangleF content = new RectangleF(r.Left + pad, top, r.Width - 2 * pad, bottom.Top - 12 * s - top);
                float tableWidth = Math.Max(330 * s, Math.Min(480 * s, content.Width * 0.34f));
                RectangleF graph = new RectangleF(content.Left, content.Top, content.Width - tableWidth - 16 * s, content.Height);
                RectangleF table = new RectangleF(graph.Right + 16 * s, content.Top, tableWidth, content.Height);
                DrawGraph(g, graph, list, attempt, s, fs);
                DrawTable(g, table, list, attempt, s, fs);
            }
            catch (Exception e)
            {
                log.Error("Error while drawing the report of an attempt.", e);
            }
            finally
            {
                g.SmoothingMode = smoothing;
                g.TextRenderingHint = hint;
            }

            return buttons;
        }

        private static string Seconds(double t)
        {
            return double.IsNaN(t) ? "–" : t.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static void DrawHead(Graphics g, RectangleF r, SwimLiveAttempt attempt, List<SwimLiveAttempt> list, float s, float fs)
        {
            Font caption = SwimCaptureWall.Display(9.5f * fs);
            Font title = SwimCaptureWall.Display(19f * fs);
            using (SolidBrush accent = new SolidBrush(SwimTheme.Accent))
                SwimTheme.Tracked(g, SwimLang.Upper(SwimLang.T("Αναφορά μετά την προσπάθεια")), caption, accent, r.Left, r.Top);

            double far = double.NaN;
            double final = attempt == null ? double.NaN : attempt.Final(out far);
            List<double> lines = attempt == null ? new List<double>() : attempt.Lines.Where(d => d >= 4.99).ToList();
            string what = attempt == null ? SwimLang.T("Καμία προσπάθεια ακόμα") :
                SwimLang.F($"Προσπάθεια {attempt.Number}") + (lines.Count > 0 ? "  ·  " + SwimLang.F($"εκκίνηση έως τα {SwimWallPlaces.Label(lines.Max())}") : "");
            using (SolidBrush text = new SolidBrush(SwimTheme.Text))
                g.DrawString(what, title, text, r.Left - 2 * s, r.Top + caption.GetHeight(g) + 4 * s);

            if (attempt == null)
                return;

            // From the right: the time of the farthest line (green when the best), the highest speed, the reaction.
            float x = r.Right;
            SwimLiveAttempt best;
            double bestTime = double.IsNaN(far) ? double.NaN : SwimLiveAttempts.Best(far, out best);
            bool isBest = !double.IsNaN(final) && !double.IsNaN(bestTime) && Math.Abs(final - bestTime) < 1e-9 && list.Count > 1;
            x = Number(g, x, r.Top, double.IsNaN(far) ? SwimLang.T("Χρόνος") : SwimWallPlaces.Label(far) + (isBest ? " · " + SwimLang.T("καλύτερη") : ""),
                Seconds(final), "s", isBest ? SwimTheme.Green : SwimTheme.Dim, 30f, s, fs, false);
            SwimSpeedPoint topSpeed = attempt.Top;
            string at = double.IsNaN(topSpeed.Metres) ? "" : " · " + topSpeed.Metres.ToString("0.0", CultureInfo.InvariantCulture) + " m";
            x = Number(g, x, r.Top, SwimLang.T("Μέγιστη ταχύτητα") + at, double.IsNaN(topSpeed.Speed) ? "–" : (attempt.Estimated ? "≈" : "") + topSpeed.Speed.ToString("0.00", CultureInfo.InvariantCulture),
                "m/s", SwimTheme.Dim, 24f, s, fs, true);
            Number(g, x, r.Top, SwimLang.T("Αντίδραση"), Seconds(attempt.Reaction), "s", SwimTheme.Dim, 24f, s, fs, true);
        }

        /// <summary>A number of the head, its right edge at 'right'; returns the left edge (with the line before it).</summary>
        private static float Number(Graphics g, float right, float top, string label, string value, string unit, Color labelColor, float size, float s, float fs, bool line)
        {
            Font labelFont = SwimCaptureWall.Display(8.5f * fs);
            Font valueFont = SwimCaptureWall.FontOf(SwimTheme.LightFamily, size * fs);
            Font unitFont = SwimCaptureWall.Display(10f * fs);
            string caps = SwimLang.Upper(label);
            float labelWidth = SwimTheme.TrackedWidth(g, caps, labelFont);
            SizeF valueSize = g.MeasureString(value, valueFont);
            SizeF unitSize = g.MeasureString(unit, unitFont);
            float width = Math.Max(labelWidth, valueSize.Width + unitSize.Width);
            float left = right - width;
            using (SolidBrush brush = new SolidBrush(labelColor))
                SwimTheme.Tracked(g, caps, labelFont, brush, right - labelWidth, top);

            float valueTop = top + labelFont.GetHeight(g) + 2 * s;
            using (SolidBrush brush = new SolidBrush(SwimTheme.Text))
                g.DrawString(value, valueFont, brush, right - unitSize.Width - valueSize.Width, valueTop);
            using (SolidBrush brush = new SolidBrush(SwimTheme.Dim))
                g.DrawString(unit, unitFont, brush, right - unitSize.Width, valueTop + valueSize.Height - unitSize.Height - 4 * s);

            left -= 18 * s;
            if (line)
            {
                using (Pen pen = new Pen(SwimTheme.Border, 1f))
                    g.DrawLine(pen, left + 9 * s, top + 2 * s, left + 9 * s, top + 54 * s);
            }

            return left;
        }

        private static RectangleF[] DrawButtons(Graphics g, RectangleF r, int hover, float s, float fs)
        {
            RectangleF[] places = new RectangleF[PartCount];
            Font font = SwimCaptureWall.Display(10.5f * fs);
            Font note = SwimCaptureWall.Display(9.5f * fs);
            string back = SwimLang.Upper(SwimLang.T("Πίσω στις κάμερες"));
            string clear = SwimLang.T("Νέος κολυμβητής");
            float backWidth = SwimTheme.TrackedWidth(g, back, font, 0.08f) + 40 * s;
            float clearWidth = g.MeasureString(clear, font).Width + 36 * s;
            places[PartBack] = new RectangleF(r.Right - backWidth, r.Top, backWidth, r.Height);
            places[PartClear] = new RectangleF(places[PartBack].Left - 10 * s - clearWidth, r.Top, clearWidth, r.Height);

            SwimTheme.FillRounded(g, hover == PartBack ? SwimTheme.AccentLight : SwimTheme.Accent, places[PartBack], 4 * s);
            using (SolidBrush brush = new SolidBrush(SwimTheme.Back))
            {
                float w = SwimTheme.TrackedWidth(g, back, font, 0.08f);
                SwimTheme.Tracked(g, back, font, brush, places[PartBack].Left + (places[PartBack].Width - w) / 2f, places[PartBack].Top + (places[PartBack].Height - font.GetHeight(g)) / 2f, 0.08f);
            }

            SwimTheme.FillRounded(g, hover == PartClear ? SwimTheme.RaisedHover : SwimTheme.Raised, places[PartClear], 4 * s);
            SwimTheme.DrawRounded(g, hover == PartClear ? SwimTheme.Accent : SwimTheme.Border, places[PartClear], 4 * s);
            using (SolidBrush brush = new SolidBrush(SwimTheme.Text))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(clear, font, brush, places[PartClear], center);

            using (SolidBrush brush = new SolidBrush(SwimTheme.Dim))
            using (StringFormat left = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(SwimLang.T("Το επόμενο «Σήμα εκκίνησης» γυρίζει μόνο του στις κάμερες."), note, brush,
                    new RectangleF(r.Left, r.Top, Math.Max(10, places[PartClear].Left - r.Left - 16 * s), r.Height), left);

            return places;
        }

        #region The graph
        private static void DrawGraph(Graphics g, RectangleF r, List<SwimLiveAttempt> list, SwimLiveAttempt attempt, float s, float fs)
        {
            SwimTheme.FillRounded(g, SwimTheme.Surface, r, 4 * s);
            SwimTheme.DrawRounded(g, SwimTheme.GridLine, r, 4 * s);
            Font caption = SwimCaptureWall.Display(9.5f * fs);
            Font small = SwimCaptureWall.Display(8.5f * fs);
            Font label = SwimCaptureWall.Display(9.5f * fs);
            using (SolidBrush text = new SolidBrush(SwimTheme.Text))
                SwimTheme.Tracked(g, SwimLang.Upper(SwimLang.T("Ταχύτητα κατά μήκος της πισίνας")), caption, text, r.Left + 14 * s, r.Top + 12 * s);

            // The legend: one chip per attempt (the last one thicker), right aligned.
            float lx = r.Right - 14 * s;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                SwimLiveAttempt a = list[i];
                bool last = a == attempt;
                string name = a.Number.ToString(CultureInfo.InvariantCulture) + (last ? " · " + SwimLang.T("τώρα") : "");
                float w = g.MeasureString(name, small).Width;
                using (SolidBrush brush = new SolidBrush(last ? SwimTheme.Text : SwimTheme.Dim))
                    g.DrawString(name, small, brush, lx - w, r.Top + 11 * s);
                using (Pen pen = new Pen(SwimLiveAttempts.ColorOf(a), last ? 3.5f * s : 2.2f * s))
                    g.DrawLine(pen, lx - w - 22 * s, r.Top + 11 * s + small.GetHeight(g) / 2f, lx - w - 6 * s, r.Top + 11 * s + small.GetHeight(g) / 2f);
                lx -= w + 36 * s;
            }

            // The plot: metres from the wall of the start (to the farthest camera, 15 m at least), metres per second.
            List<double> lines = attempt == null ? new List<double>() : attempt.Lines.Where(d => d >= 4.99).ToList();
            double farthest = list.SelectMany(a => a.Tracks).Where(t => SwimWallPlaces.Under(t.Distance)).Select(t => t.Distance).DefaultIfEmpty(15).Max();
            double xMax = Math.Max(15, Math.Ceiling(farthest + 0.01));
            double topSpeed = list.SelectMany(a => a.Speed).SelectMany(p => p).Select(p => p.Speed).DefaultIfEmpty(0).Max();
            double yMax = Math.Max(4, Math.Min(7, Math.Ceiling(topSpeed + 0.4)));
            RectangleF plot = new RectangleF(r.Left + 50 * s, r.Top + 56 * s, r.Width - 66 * s, r.Height - 56 * s - 58 * s);
            if (plot.Width < 60 || plot.Height < 40)
                return;

            Func<double, float> X = m => plot.Left + (float)(m / xMax * plot.Width);
            Func<double, float> Y = v => plot.Bottom - (float)(Math.Max(0, Math.Min(yMax, v)) / yMax * plot.Height);

            // Where no camera sees the swimmer (between the cameras of the last attempt): hatched.
            List<double[]> seen = attempt == null ? new List<double[]>() : attempt.Speed.Select(p => new[] { p.Min(q => q.Metres), p.Max(q => q.Metres) }).OrderBy(a => a[0]).ToList();
            using (HatchBrush hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(28, SwimTheme.Dim), Color.Transparent))
            {
                for (int i = 0; i + 1 < seen.Count; i++)
                {
                    if (seen[i + 1][0] - seen[i][1] < 0.3)
                        continue;

                    RectangleF gap = RectangleF.FromLTRB(X(seen[i][1]), plot.Top, X(seen[i + 1][0]), plot.Bottom);
                    g.FillRectangle(hatch, gap);
                    using (SolidBrush brush = new SolidBrush(SwimTheme.Faint))
                    using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                    {
                        if (gap.Width > 60 * s)
                            g.DrawString(SwimLang.T("χωρίς κάμερα"), small, brush, new RectangleF(gap.Left, plot.Bottom - 20 * s, gap.Width, 18 * s), center);
                    }
                }
            }

            using (Pen grid = new Pen(SwimTheme.GridLine, 1f))
            using (SolidBrush ink = new SolidBrush(SwimTheme.Dim))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center })
            using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
            {
                for (int m = 0; m <= (int)xMax; m++)
                {
                    g.DrawLine(grid, X(m), plot.Top, X(m), plot.Bottom);
                    g.DrawString(m.ToString(CultureInfo.InvariantCulture), label, ink, new RectangleF(X(m) - 20 * s, plot.Bottom + 6 * s, 40 * s, 20 * s), center);
                }

                for (int v = 0; v <= (int)yMax; v++)
                {
                    g.DrawLine(grid, plot.Left, Y(v), plot.Right, Y(v));
                    g.DrawString(v.ToString(CultureInfo.InvariantCulture), label, ink, new RectangleF(plot.Left - 40 * s, Y(v) - 10 * s, 32 * s, 20 * s), right);
                }

                g.DrawString("m/s", small, ink, plot.Left - 44 * s, plot.Top - 22 * s);
                using (StringFormat far = new StringFormat { Alignment = StringAlignment.Far })
                    g.DrawString(SwimLang.T("Απόσταση από τον τοίχο (m)"), small, ink, new RectangleF(plot.Left, plot.Bottom + 26 * s, plot.Width, 18 * s), far);
            }

            using (Pen axis = new Pen(SwimTheme.Faint, 1.4f * s))
            {
                g.DrawLine(axis, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
                g.DrawLine(axis, plot.Left, plot.Top, plot.Left, plot.Bottom);
            }

            // The lines 5, 10, 15 m with the times of the attempt.
            using (Pen dashed = new Pen(SwimTheme.Red, 1.6f * s) { DashStyle = DashStyle.Dash })
            using (SolidBrush text = new SolidBrush(SwimTheme.Text))
            {
                foreach (double d in lines)
                {
                    float x = X(d);
                    g.DrawLine(dashed, x, plot.Top, x, plot.Bottom);
                    string tag = SwimWallPlaces.Label(d) + " · " + Seconds(attempt.TimeAt(d)) + " s";
                    float w = g.MeasureString(tag, label).Width;
                    float tx = Math.Min(plot.Right - w, Math.Max(plot.Left, x - w / 2f));
                    g.DrawString(tag, label, text, tx, plot.Top - 22 * s);
                }
            }

            // The curves: the older attempts thin, the last one thick; dotted between two cameras.
            Region clip = g.Clip;
            g.SetClip(RectangleF.Inflate(plot, 0, 2 * s));
            foreach (SwimLiveAttempt a in list)
            {
                bool last = a == attempt;
                Color color = SwimLiveAttempts.ColorOf(a);
                List<List<SwimSpeedPoint>> parts = a.Speed;
                using (Pen pen = new Pen(last ? color : Color.FromArgb(185, color), last ? 3f * s : 1.6f * s) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (Pen dotted = new Pen(Color.FromArgb(last ? 170 : 110, color), 1.4f * s) { DashStyle = DashStyle.Dot })
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        PointF[] points = parts[i].Select(p => new PointF(X(p.Metres), Y(p.Speed))).ToArray();
                        if (points.Length >= 2)
                            g.DrawLines(pen, points);

                        if (i + 1 < parts.Count && parts[i].Count > 0 && parts[i + 1].Count > 0)
                        {
                            SwimSpeedPoint end = parts[i][parts[i].Count - 1], begin = parts[i + 1][0];
                            g.DrawLine(dotted, X(end.Metres), Y(end.Speed), X(begin.Metres), Y(begin.Speed));
                        }
                    }
                }
            }

            g.Clip = clip;

            // The highest speed of the attempt.
            if (attempt != null)
            {
                SwimSpeedPoint top = attempt.Top;
                if (!double.IsNaN(top.Speed))
                {
                    Color color = SwimLiveAttempts.ColorOf(attempt);
                    float cx = X(top.Metres), cy = Y(top.Speed), radius = 5.5f * s;
                    using (SolidBrush fill = new SolidBrush(SwimTheme.Back))
                        g.FillEllipse(fill, cx - radius, cy - radius, 2 * radius, 2 * radius);
                    using (Pen pen = new Pen(color, 2.4f * s))
                        g.DrawEllipse(pen, cx - radius, cy - radius, 2 * radius, 2 * radius);

                    string tag = SwimLang.T("μέγιστη") + " " + (attempt.Estimated ? "≈" : "") + top.Speed.ToString("0.00", CultureInfo.InvariantCulture) + " m/s · " +
                        top.Metres.ToString("0.0", CultureInfo.InvariantCulture) + " m";
                    using (SolidBrush brush = new SolidBrush(color))
                        g.DrawString(tag, label, brush, Math.Min(plot.Right - g.MeasureString(tag, label).Width, cx + 10 * s), Math.Max(plot.Top + 2 * s, cy - 24 * s));
                }

                // The cameras along the bottom: where each one saw the swimmer.
                int index = 0;
                foreach (SwimLiveTrack t in attempt.Tracks.Where(t => SwimWallPlaces.Under(t.Distance)).OrderBy(t => t.Distance))
                {
                    List<SwimSpeedPoint> part = t.Speed();
                    if (part.Count == 0)
                        continue;

                    float x0 = X(part.Min(p => p.Metres)), x1 = X(part.Max(p => p.Metres));
                    float y = plot.Bottom + 30 * s + (index++ % 2) * 6 * s;
                    using (SolidBrush brush = new SolidBrush(SwimTheme.Border))
                        g.FillRectangle(brush, x0, y, Math.Max(2, x1 - x0), 3 * s);
                }
            }

            if (attempt == null || attempt.Speed.Count == 0)
            {
                using (SolidBrush brush = new SolidBrush(SwimTheme.Dim))
                using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(SwimLang.T("Δεν υπάρχουν θέσεις του κολυμβητή από τις κάμερες κάτω από το νερό."), label, brush, plot, center);
            }
        }
        #endregion

        #region The table
        private static void DrawTable(Graphics g, RectangleF r, List<SwimLiveAttempt> list, SwimLiveAttempt attempt, float s, float fs)
        {
            SwimTheme.FillRounded(g, SwimTheme.Surface, r, 4 * s);
            SwimTheme.DrawRounded(g, SwimTheme.GridLine, r, 4 * s);
            Font caption = SwimCaptureWall.Display(9.5f * fs);
            Font head = SwimCaptureWall.Display(8.5f * fs);
            Font cell = SwimCaptureWall.Display(11f * fs);
            Font bold = SwimCaptureWall.Display(12f * fs);
            float x0 = r.Left + 14 * s, x1 = r.Right - 14 * s;
            float y = r.Top + 12 * s;
            using (SolidBrush text = new SolidBrush(SwimTheme.Text))
                SwimTheme.Tracked(g, SwimLang.Upper(SwimLang.T("Χρόνοι από την κόρνα")), caption, text, x0, y);

            y += caption.GetHeight(g) + 12 * s;
            List<double> lines = attempt == null ? new List<double>() : attempt.Lines.Where(d => d >= 4.99).Take(4).ToList();
            List<string> columns = new List<string> { "#", SwimLang.T("Αντίδρ.") };
            columns.AddRange(lines.Select(d => SwimWallPlaces.Label(d)));
            columns.Add(SwimLang.T("Μέγ. m/s"));
            int count = columns.Count;
            float first = 34 * s;
            float width = (x1 - x0 - first) / Math.Max(1, count - 1);
            Func<int, RectangleF> Column = (i) => i == 0 ? new RectangleF(x0, 0, first, 0) : new RectangleF(x0 + first + (i - 1) * width, 0, width, 0);

            using (SolidBrush brush = new SolidBrush(SwimTheme.Dim))
            using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap })
            {
                for (int i = 0; i < count; i++)
                {
                    RectangleF c = Column(i);
                    if (i == 0)
                        g.DrawString(columns[i], head, brush, c.Left, y);
                    else
                        g.DrawString(columns[i], head, brush, new RectangleF(c.Left, y, c.Width, 20 * s), right);
                }
            }

            y += head.GetHeight(g) + 6 * s;
            float rowHeight = 34 * s;
            Dictionary<double, double> best = new Dictionary<double, double>();
            foreach (double d in lines)
            {
                SwimLiveAttempt b;
                best[d] = SwimLiveAttempts.Best(d, out b);
            }

            for (int row = 0; row < SwimLiveAttempts.Max; row++)
            {
                SwimLiveAttempt a = row < list.Count ? list[row] : null;
                RectangleF line = new RectangleF(x0 - 6 * s, y, x1 - x0 + 12 * s, rowHeight);
                if (a != null && a == attempt)
                    SwimTheme.FillRounded(g, Color.FromArgb(34, SwimLiveAttempts.ColorOf(a)), line, 3 * s);

                using (Pen sep = new Pen(SwimTheme.GridLine, 1f))
                    g.DrawLine(sep, x0, y, x1, y);

                float cy = y + rowHeight / 2f;
                Color dot = a != null ? SwimLiveAttempts.ColorOf(a) : SwimTheme.Faint;
                using (SolidBrush brush = new SolidBrush(dot))
                    g.FillEllipse(brush, x0, cy - 5 * s, 10 * s, 10 * s);

                using (SolidBrush brush = new SolidBrush(a != null ? SwimTheme.Text : SwimTheme.Faint))
                using (StringFormat left = new StringFormat { LineAlignment = StringAlignment.Center })
                    g.DrawString(a != null ? a.Number.ToString(CultureInfo.InvariantCulture) : "·", bold, brush, new RectangleF(x0 + 14 * s, y, first, rowHeight), left);

                List<string> values = new List<string>();
                List<Color> colors = new List<Color>();
                values.Add(a == null ? "–" : Seconds(a.Reaction));
                colors.Add(SwimTheme.Text);
                foreach (double d in lines)
                {
                    double t = a == null ? double.NaN : a.TimeAt(d);
                    bool isBest = !double.IsNaN(t) && list.Count > 1 && Math.Abs(t - best[d]) < 1e-9;
                    values.Add(Seconds(t) + (isBest && d == lines[lines.Count - 1] ? " ★" : ""));
                    colors.Add(isBest ? SwimTheme.Green : SwimTheme.Text);
                }

                SwimSpeedPoint top = a == null ? new SwimSpeedPoint(double.NaN, double.NaN) : a.Top;
                values.Add(double.IsNaN(top.Speed) ? "–" : top.Speed.ToString("0.00", CultureInfo.InvariantCulture));
                colors.Add(SwimTheme.Text);

                using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                {
                    for (int i = 0; i < values.Count; i++)
                    {
                        RectangleF c = Column(i + 1);
                        using (SolidBrush brush = new SolidBrush(a == null ? SwimTheme.Faint : colors[i]))
                            g.DrawString(values[i], a == attempt ? bold : cell, brush, new RectangleF(c.Left, y, c.Width, rowHeight), right);
                    }
                }

                y += rowHeight;
            }

            if (attempt == null)
                return;

            // The parts of the attempt: from the horn to the first line, then from line to line, with their mean speed.
            y += 16 * s;
            using (SolidBrush brush = new SolidBrush(SwimTheme.Dim))
                SwimTheme.Tracked(g, SwimLang.Upper(SwimLang.F($"Τμήματα · προσπάθεια {attempt.Number}")), head, brush, x0, y);

            y += head.GetHeight(g) + 6 * s;
            double fromDistance = 0, fromTime = 0;
            foreach (double d in lines)
            {
                double t = attempt.TimeAt(d);
                string name = SwimWallPlaces.Label(fromDistance).Replace(" m", "") + " – " + SwimWallPlaces.Label(d);
                string time = double.IsNaN(t) || double.IsNaN(fromTime) ? "–" : (t - fromTime).ToString("0.00", CultureInfo.InvariantCulture) + " s";
                string speed = double.IsNaN(t) || double.IsNaN(fromTime) || t - fromTime <= 0 ? "" : ((d - fromDistance) / (t - fromTime)).ToString("0.00", CultureInfo.InvariantCulture) + " m/s";
                using (SolidBrush ink = new SolidBrush(SwimTheme.Text))
                using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
                using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap })
                {
                    g.DrawString(name, cell, ink, x0, y);
                    g.DrawString(time, cell, ink, new RectangleF(x0, y, (x1 - x0) * 0.66f, 24 * s), right);
                    g.DrawString(speed, cell, dim, new RectangleF(x0, y, x1 - x0, 24 * s), right);
                }

                y += 26 * s;
                fromDistance = d;
                fromTime = t;
            }

            // The difference from the best other attempt at the farthest line.
            double far;
            double final = attempt.Final(out far);
            if (!double.IsNaN(final))
            {
                double other = double.NaN;
                SwimLiveAttempt otherAttempt = null;
                foreach (SwimLiveAttempt a in list)
                {
                    if (a == attempt)
                        continue;

                    double t = a.TimeAt(far);
                    if (!double.IsNaN(t) && (double.IsNaN(other) || t < other))
                    {
                        other = t;
                        otherAttempt = a;
                    }
                }

                if (otherAttempt != null)
                {
                    y += 8 * s;
                    double delta = final - other;
                    Color color = delta <= 0 ? SwimTheme.Green : SwimTheme.Amber;
                    RectangleF box = new RectangleF(x0, y, x1 - x0, 44 * s);
                    SwimTheme.FillRounded(g, Color.FromArgb(28, color), box, 3 * s);
                    SwimTheme.DrawRounded(g, Color.FromArgb(120, color), box, 3 * s);
                    string number = (delta <= 0 ? "−" : "+") + Math.Abs(delta).ToString("0.00", CultureInfo.InvariantCulture) + " s";
                    Font big = SwimCaptureWall.Display(14f * fs);
                    float w = g.MeasureString(number, big).Width;
                    using (SolidBrush brush = new SolidBrush(color))
                    using (StringFormat middle = new StringFormat { LineAlignment = StringAlignment.Center })
                        g.DrawString(number, big, brush, new RectangleF(box.Left + 8 * s, box.Top, w + 4 * s, box.Height), middle);
                    using (SolidBrush brush = new SolidBrush(SwimTheme.Text))
                    using (StringFormat middle = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                        g.DrawString(SwimLang.F($"στα {SwimWallPlaces.Label(far)} από την προσπάθεια {otherAttempt.Number}"), cell, brush,
                            new RectangleF(box.Left + 16 * s + w, box.Top, box.Width - w - 22 * s, box.Height), middle);
                    y = box.Bottom;
                }
            }

            // How it was measured.
            Font note = SwimCaptureWall.Display(8.5f * fs);
            // (2.30.0) Once every line was read again in its recorded file, the times are to the frame.
            string noteText = attempt.Exact ? SwimLang.T("Οι χρόνοι από τα αρχεία των καμερών, καρέ καρέ.")
                : SwimLang.T("≈ από τη ζωντανή εικόνα. Ο ακριβής χρόνος ελέγχεται καρέ καρέ στην ανάλυση.");
            if (attempt.Estimated)
                noteText += " " + SwimLang.T("Η ταχύτητα με εκτίμηση της κλίμακας των καμερών.");

            float noteHeight = 40 * s;
            using (SolidBrush brush = new SolidBrush(SwimTheme.Dim))
                g.DrawString(noteText, note, brush, new RectangleF(x0, Math.Max(y + 10 * s, r.Bottom - 12 * s - noteHeight), x1 - x0, noteHeight));
        }
        #endregion
    }
}
