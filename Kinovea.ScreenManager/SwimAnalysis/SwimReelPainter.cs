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
    /// <summary>What the technical view draws at one frame.</summary>
    public sealed class SwimReelScene
    {
        public SwimRace Race;
        public SwimComputed Computed;
        public SwimReel Reel;
        public SwimTiming Timing;

        /// <summary>Video timestamp of the frame and race time (s after the start).</summary>
        public long Timestamp;
        public double Time;

        /// <summary>Point of the video image (reference pixels) → point of the drawing, null without the athlete label.</summary>
        public Func<PointF, PointF> FromImage;

        /// <summary>Summary card after the finish.</summary>
        public bool Summary = true;

        /// <summary>The final of the event (its board at the checkpoints and at the finish), null without one.</summary>
        public SwimFinal Final;

        /// <summary>Name of the athlete on the board of the final.</summary>
        public string BoardName = "";

        /// <summary>The Δ with the reference of the race, in the header and as a card.</summary>
        public bool Delta = true;

        /// <summary>Only the summary card, whole, on a dark background (the graphics of the report).</summary>
        public bool SummaryOnly;
    }

    /// <summary>
    /// Drawing of the technical view of a race (as the swimming reels) on the video or on an exported image: athlete label,
    /// header with the race time, strokes, tempo, distance per cycle and speed, phase, badges, chart of the cycles of the
    /// length and, after the finish, the summary card. Sizes follow the image (designed for 1280 × 720).
    /// </summary>
    public static class SwimReelPainter
    {
        private static readonly Color athlete = Color.FromArgb(255, 255, 184, 0);

        /// <summary>Places of the parts, from the size of the image.</summary>
        private sealed class Layout
        {
            public readonly float W, H, K, M;
            public readonly RectangleF Header, Chart;
            public readonly float BadgeX, BadgeY, BadgeW;

            public Layout(Size size)
            {
                W = size.Width;
                H = size.Height;
                bool landscape = W >= 1.2f * H;
                float k = landscape ? Math.Min(W / 1280f, H / 720f) : W / 700f;
                K = Math.Max(0.45f, Math.Min(2.5f, k));
                M = 16 * K;

                float headerW = landscape ? Math.Min(540 * K, W - 2 * M) : W - 2 * M;
                Header = new RectangleF(M, M, headerW, 132 * K);
                float chartW = Math.Min(560 * K, W - 3 * M - headerW);
                BadgeW = Math.Min(250 * K, W - 2 * M);
                BadgeX = W - M - BadgeW;
                if (landscape && chartW >= 300 * K)
                {
                    Chart = new RectangleF(W - M - chartW, M, chartW, 132 * K);
                }
                else
                {
                    // Narrow or portrait images: the chart under the header and its phase.
                    Chart = new RectangleF(M, Header.Bottom + 42 * K, Math.Min(headerW, W - 2 * M), 118 * K);
                }

                BadgeY = Chart.Bottom + 10 * K;
            }
        }

        /// <summary>
        /// The technical view can be drawn on an image of this size (else the race board and the velocity chart are shown):
        /// a race with the start signal, an image of at least 320 × 180.
        /// </summary>
        public static bool CanPaint(Size size, SwimReel reel)
        {
            return reel != null && size.Width >= 320 && size.Height >= 180;
        }

        /// <summary>Seconds the board of the final stays after a checkpoint, and after the touch before the summary card.</summary>
        public const double BoardSeconds = 4.5;
        public const double FinishBoardSeconds = 5.0;

        public static void Paint(Graphics g, Size size, SwimReelScene s)
        {
            if (s == null || s.Race == null || s.Computed == null || !CanPaint(size, s.Reel))
                return;

            Layout layout = new Layout(size);
            if (s.SummaryOnly)
            {
                if (s.Reel.Finish.HasValue)
                    DrawSummary(g, s, layout, true);
                return;
            }

            double t = s.Time;
            bool summary = s.Summary && s.Reel.Finish.HasValue && t >= s.Reel.Finish.Value + SummaryDelay(s);

            if (s.FromImage != null && !summary)
                DrawLabel(g, s, layout);
            DrawHeader(g, s, layout);
            DrawChart(g, s, layout);
            DrawBadges(g, s, layout);
            if (!summary)
            {
                RectangleF board = DrawFinalsBoard(g, s, layout);
                DrawDeltaCard(g, s, layout, board);
            }

            if (summary)
                DrawSummary(g, s, layout, false);
        }

        /// <summary>After the touch the board of the final comes first (when there is one), then the summary card.</summary>
        private static double SummaryDelay(SwimReelScene s)
        {
            return FinishBoard(s) != null ? FinishBoardSeconds : 1.0;
        }

        /// <summary>The board of the final at the finish (the athlete among its swimmers), null without a final or a finish time.</summary>
        private static List<SwimFinalsBoardRow> FinishBoard(SwimReelScene s)
        {
            if (s.Final == null || !s.Reel.Finish.HasValue)
                return null;

            SwimRow finish = s.Computed.Rows.FirstOrDefault(r => r.D >= s.Race.Distance && r.T.HasValue);
            if (finish == null || !s.Final.Swimmers.Any(sw => sw.TimeAt(finish.D).HasValue))
                return null;

            return SwimFinalsCompare.Board(s.Final, finish.D, finish.T.Value, s.BoardName);
        }

        #region Helpers
        private static Font F(float pt, float k, bool display = false, FontStyle style = FontStyle.Regular)
        {
            float size = (float)(Math.Round(pt * k * 4) / 4);
            size = Math.Max(4f, size);
            return display ? SwimTheme.Display(size) : SwimTheme.Ui(size, style);
        }

        private static void Text(Graphics g, string text, Font font, Color color, float x, float y,
            StringAlignment horizontal = StringAlignment.Near, StringAlignment vertical = StringAlignment.Near)
        {
            if (string.IsNullOrEmpty(text))
                return;

            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.NoClip) { Alignment = horizontal, LineAlignment = vertical })
                g.DrawString(text, font, brush, new PointF(x, y), format);
        }

        /// <summary>Text in a width, shortened with … if needed, centred vertically in the rectangle.</summary>
        private static void TextIn(Graphics g, string text, Font font, Color color, RectangleF r, StringAlignment horizontal = StringAlignment.Near)
        {
            if (string.IsNullOrEmpty(text) || r.Width < 4)
                return;

            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat(StringFormatFlags.NoWrap) { Alignment = horizontal, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                g.DrawString(text, font, brush, r, format);
        }

        [ThreadStatic]
        private static StringFormat measureFormat;

        private static float Width(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            if (measureFormat == null)
            {
                using (StringFormat typographic = StringFormat.GenericTypographic)
                    measureFormat = new StringFormat(typographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces };
            }

            return g.MeasureString(text, font, PointF.Empty, measureFormat).Width;
        }

        private static Color Fade(Color c, float a)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, (int)Math.Round(c.A * a))), c.R, c.G, c.B);
        }

        /// <summary>Translucent dark card with a hairline border and an accent on the left edge.</summary>
        private static void Card(Graphics g, RectangleF r, Color accent, float k, float alpha = 1f)
        {
            if (r.Width < 2 || r.Height < 2)
                return;

            int a0 = (int)Math.Round(232 * alpha), a1 = (int)Math.Round(205 * alpha);
            using (GraphicsPath path = SwimTheme.Rounded(r, 9 * k))
            using (LinearGradientBrush back = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y, r.Width + 2, r.Height),
                Color.FromArgb(a0, 9, 14, 22), Color.FromArgb(a1, 18, 30, 45), LinearGradientMode.Horizontal))
            using (Pen border = new Pen(Color.FromArgb((int)Math.Round(80 * alpha), 255, 255, 255), Math.Max(1f, k)))
            {
                g.FillPath(back, path);
                g.DrawPath(border, path);
            }

            using (SolidBrush brush = new SolidBrush(Fade(accent, alpha)))
                g.FillRectangle(brush, r.Left + 1.5f * k, r.Top + 9 * k, 3.2f * k, Math.Max(2, r.Height - 18 * k));
        }

        private static void Pill(Graphics g, RectangleF r, Color fill, Color border, float width)
        {
            float radius = r.Height / 2;
            SwimTheme.FillRounded(g, fill, r, radius);
            using (GraphicsPath path = SwimTheme.Rounded(r, radius))
            using (Pen pen = new Pen(border, width))
                g.DrawPath(pen, path);
        }

        /// <summary>Top of the scale of the distance per cycle: 2.4 m, more for the long cycles of freestyle and backstroke.</summary>
        private static double DpsScale(IEnumerable<double> values)
        {
            double max = values.DefaultIfEmpty(0).Max();
            return Math.Max(2.4, Math.Ceiling(max * 1.05 * 10) / 10);
        }

        private static Color Tone(SwimReelTone tone)
        {
            switch (tone)
            {
                case SwimReelTone.Amber: return SwimTheme.Amber;
                case SwimReelTone.Green: return SwimTheme.Green;
                default: return SwimTheme.Accent;
            }
        }

        private static string UnitCaps(SwimRace race)
        {
            return race.IsYards ? "YD" : "M";
        }

        private static string Unit(SwimRace race)
        {
            return race.IsYards ? "yd" : "m";
        }

        /// <summary>Name, event and lane in upper case: "ΒΛΑΧΟΥ ΑΝΝΑ  ·  50 M ΠΕΤΑΛΟΥΔΑ  ·  ΔΙΑΔΡΟΜΟΣ 8".</summary>
        private static string Title(SwimRace race, bool athleteFirst)
        {
            string name = string.IsNullOrWhiteSpace(race.Athlete) ? "" : SwimAthleteList.Normalize(race.Athlete);
            string ev = string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", race.DistanceLabel, UnitCaps(race), SwimAthleteList.Normalize(SwimPresets.StyleName(race.Style)));
            string lane = race.Lane > 0 ? "ΔΙΑΔΡΟΜΟΣ " + race.Lane.ToString(CultureInfo.InvariantCulture) : "";
            List<string> parts = athleteFirst ? new List<string> { name, ev, lane } : new List<string> { ev, name, lane };
            return string.Join("  ·  ", parts.Where(p => p.Length > 0));
        }
        #endregion

        #region Header
        private static void DrawHeader(Graphics g, SwimReelScene s, Layout L)
        {
            float k = L.K;
            double t = s.Time;
            SwimReel reel = s.Reel;
            RectangleF r = L.Header;
            Card(g, r, SwimTheme.Accent, k);

            // Last checkpoint passed on the right, name and event on the left.
            Font caption = F(9f, k, false, FontStyle.Bold);
            SwimRow last = s.Computed.Rows.Where(row => row.PassedAt(t)).OrderBy(row => row.OnVideo).LastOrDefault();
            float titleRight = r.Right - 14 * k;
            if (last != null)
            {
                bool finished = last.D >= s.Race.Distance;
                string label = finished ? "ΤΕΛΙΚΟΣ" : last.D.ToString(CultureInfo.InvariantCulture) + " " + UnitCaps(s.Race);
                Text(g, label, caption, SwimTheme.Dim, r.Right - 14 * k, r.Top + 9 * k, StringAlignment.Far);
                Text(g, SwimReference.FormatTime(last.T.Value), F(19f, k, true), SwimTheme.Text, r.Right - 12 * k, r.Top + 25 * k, StringAlignment.Far);
                titleRight -= Width(g, label, caption) + 14 * k;

                // The Δ with the reference at that checkpoint, under its time.
                if (s.Delta && last.Delta.HasValue && s.Computed.Reference != null)
                {
                    string delta = SwimReference.FormatDelta(last.Delta.Value);
                    Font deltaFont = F(10f, k, true);
                    float dw = Width(g, delta, deltaFont) + 14 * k;
                    RectangleF deltaPill = new RectangleF(r.Right - 12 * k - dw, r.Top + 54 * k, dw, 20 * k);
                    SwimTheme.FillRounded(g, last.Delta.Value <= 0 ? SwimFinalsPainter.FasterColor : SwimFinalsPainter.SlowerColor, deltaPill, 5 * k);
                    Text(g, delta, deltaFont, Color.White, deltaPill.Left + dw / 2, deltaPill.Top + deltaPill.Height / 2, StringAlignment.Center, StringAlignment.Center);
                    Font vsFont = F(7.5f, k, false, FontStyle.Bold);
                    float vsLeft = r.Left + Math.Min(r.Width * 0.52f, 280 * k);
                    TextIn(g, "Δ vs " + ReferenceShort(s.Computed.Reference), vsFont, SwimTheme.Dim,
                        new RectangleF(vsLeft, deltaPill.Top, Math.Max(10, deltaPill.Left - 6 * k - vsLeft), deltaPill.Height), StringAlignment.Far);
                }
            }

            TextIn(g, Title(s.Race, true), caption, SwimTheme.Dim, new RectangleF(r.Left + 16 * k, r.Top + 6 * k, Math.Max(10, titleRight - r.Left - 16 * k), 18 * k));

            // At the frame of the finish and after it the final time (the official one when typed), before it the time of the frame.
            double final = reel.FinalTime ?? reel.Finish ?? double.PositiveInfinity;
            double clock = reel.Finish.HasValue && t >= reel.Finish.Value - 1e-6 ? final : Math.Min(Math.Max(0, t), final);
            Text(g, SwimReference.FormatTime(clock), F(36f, k, true), Color.White, r.Left + 12 * k, r.Top + 19 * k);

            // Strokes of the length, tempo and distance of the last cycle, speed.
            SwimReelCycle cycle = t >= 0 ? reel.LastCycle(t) : null;
            bool swimming = reel.Swimming(t) && reel.HasModel;
            string[] captions = { "ΧΕΡΙΕΣ", "ΣΥΧΝΟΤΗΤΑ", "ΑΠΟΣΤ./ΚΥΚΛΟ", "ΤΑΧΥΤΗΤΑ" };
            string[] values =
            {
                (t >= 0 ? reel.StrokesInLength(t) : 0).ToString(CultureInfo.InvariantCulture),
                cycle != null ? cycle.Tempo.ToString("0.00", CultureInfo.InvariantCulture) : "–",
                cycle != null && reel.HasModel ? "≈" + cycle.Dps.ToString("0.00", CultureInfo.InvariantCulture) : "–",
                swimming ? reel.SpeedAt(t).ToString("0.00", CultureInfo.InvariantCulture) : "–"
            };
            string[] units = { "", "s/κύκλο", "m", "m/s" };
            Color[] colors = { SwimTheme.Text, SwimTheme.Accent, SwimTheme.Amber, SwimTheme.Green };

            float gap = 8 * k;
            float cw = (r.Width - 32 * k - 3 * gap) / 4;
            float cy = r.Top + 80 * k, ch = 42 * k;
            Font chipCaption = F(7.25f, k, false, FontStyle.Bold);
            Font chipValue = F(14f, k, true);
            Font chipUnit = F(7.25f, k);
            for (int i = 0; i < 4; i++)
            {
                RectangleF chip = new RectangleF(r.Left + 16 * k + i * (cw + gap), cy, cw, ch);
                SwimTheme.FillRounded(g, Color.FromArgb(235, SwimTheme.Raised), chip, 6 * k);
                TextIn(g, captions[i], chipCaption, SwimTheme.Dim, new RectangleF(chip.Left + 8 * k, chip.Top + 3 * k, chip.Width - 10 * k, 14 * k));
                Text(g, values[i], chipValue, colors[i], chip.Left + 7 * k, chip.Top + 15 * k);
                if (units[i].Length > 0 && values[i] != "–")
                {
                    float vw = Width(g, values[i], chipValue);
                    if (chip.Left + 9 * k + vw + Width(g, units[i], chipUnit) < chip.Right - 4 * k)
                        Text(g, units[i], chipUnit, SwimTheme.Dim, chip.Left + 10 * k + vw, chip.Top + 23 * k);
                }
            }

            // Phase of the race under the card.
            string phase = reel.PhaseAt(t);
            Font phaseFont = F(9.5f, k, false, FontStyle.Bold);
            RectangleF pill = new RectangleF(r.Left, r.Bottom + 8 * k, Width(g, phase, phaseFont) + 34 * k, 24 * k);
            Pill(g, pill, Color.FromArgb(220, 9, 14, 22), Color.FromArgb(70, 255, 255, 255), Math.Max(1f, k));
            Color dot = phase == "ΥΠΟΒΡΥΧΙΑ" ? SwimTheme.Green : (phase == "ΤΕΡΜΑΤΙΣΜΟΣ" || phase == "ΣΤΡΟΦΗ" ? SwimTheme.Amber : SwimTheme.Accent);
            using (SolidBrush brush = new SolidBrush(dot))
                g.FillEllipse(brush, pill.Left + 10 * k, pill.Top + 8 * k, 8 * k, 8 * k);
            Text(g, phase, phaseFont, SwimTheme.Text, pill.Left + 23 * k, pill.Top + pill.Height / 2, StringAlignment.Near, StringAlignment.Center);
        }
        #endregion

        #region Chart of the cycles
        private static void DrawChart(Graphics g, SwimReelScene s, Layout L)
        {
            float k = L.K;
            double t = s.Time;
            SwimReel reel = s.Reel;
            RectangleF r = L.Chart;
            if (!reel.HasModel)
                return;

            Card(g, r, athlete, k);

            int laps = Math.Max(1, (int)Math.Ceiling(s.Race.Distance / (double)reel.Pool - 1e-9));
            int lap = t >= 0 ? reel.LapAt(t) : 0;
            Text(g, laps > 1 ? "ΑΝΑ ΚΥΚΛΟ  ·  ΜΗΚΟΣ " + (lap + 1).ToString(CultureInfo.InvariantCulture) : "ΑΝΑ ΚΥΚΛΟ",
                F(8.25f, k, false, FontStyle.Bold), SwimTheme.Dim, r.Left + 16 * k, r.Top + 8 * k);

            // Legend.
            Font legend = F(7.25f, k);
            float lx = r.Right - 14 * k;
            string distanceLegend = "απόσταση/κύκλο m", speedLegend = "ταχύτητα m/s";
            lx -= Width(g, distanceLegend, legend);
            Text(g, distanceLegend, legend, SwimTheme.Amber, lx, r.Top + 9 * k);
            using (SolidBrush b = new SolidBrush(SwimTheme.Amber))
                g.FillRectangle(b, lx - 12 * k, r.Top + 11 * k, 8 * k, 9 * k);
            lx -= 24 * k + Width(g, speedLegend, legend);
            Text(g, speedLegend, legend, SwimTheme.Accent, lx, r.Top + 9 * k);
            using (SolidBrush b = new SolidBrush(SwimTheme.Accent))
                g.FillEllipse(b, lx - 12 * k, r.Top + 12 * k, 8 * k, 8 * k);

            RectangleF plot = new RectangleF(r.Left + 40 * k, r.Top + 30 * k, r.Width - 40 * k - 16 * k, r.Height - 30 * k - 22 * k);
            if (plot.Width < 40 || plot.Height < 30)
                return;

            double d0 = lap * reel.Pool;
            double d1 = Math.Min(s.Race.Distance, (lap + 1) * reel.Pool);
            if (d1 <= d0)
                return;

            Func<double, float> X = d => plot.Left + (float)((Math.Max(d0, Math.Min(d1, d)) - d0) / (d1 - d0) * plot.Width);
            List<double> speeds = reel.Cycles.Select(c => c.Speed).Where(v => v > 0.3 && v < 3.5).ToList();
            double vmin = speeds.Count > 0 ? Math.Floor((speeds.Min() - 0.05) * 10) / 10 : 1.4;
            double vmax = speeds.Count > 0 ? Math.Ceiling((speeds.Max() + 0.05) * 10) / 10 : 2.2;
            if (vmax - vmin < 0.4)
            {
                double mid = 0.5 * (vmax + vmin);
                vmin = mid - 0.2;
                vmax = mid + 0.2;
            }

            float topH = plot.Height * 0.58f;
            Func<double, float> Y = v => plot.Top + (float)((vmax - Math.Max(vmin, Math.Min(vmax, v))) / (vmax - vmin) * topH);
            Font axis = F(6.75f, k);
            using (Pen grid = new Pen(Color.FromArgb(24, 255, 255, 255), Math.Max(1f, 0.8f * k)))
            {
                foreach (double v in new[] { vmin, 0.5 * (vmin + vmax), vmax })
                    g.DrawLine(grid, plot.Left, Y(v), plot.Right, Y(v));
            }

            Text(g, vmax.ToString("0.0", CultureInfo.InvariantCulture), axis, SwimTheme.Faint, plot.Left - 6 * k, Y(vmax), StringAlignment.Far, StringAlignment.Center);
            Text(g, vmin.ToString("0.0", CultureInfo.InvariantCulture), axis, SwimTheme.Faint, plot.Left - 6 * k, Y(vmin), StringAlignment.Far, StringAlignment.Center);

            // Underwater part of the length (start or turn) once its breakout is passed.
            double bd;
            if (reel.BreakoutDistances.TryGetValue(lap, out bd))
            {
                SwimReelBadge breakout = reel.Badges.FirstOrDefault(b => b.Title.StartsWith("BREAKOUT", StringComparison.Ordinal) && reel.LapAt(b.Time) == lap);
                if (breakout != null && breakout.Time <= t)
                {
                    float bx = X(d0 + bd);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(36, SwimTheme.Green)))
                        g.FillRectangle(b, plot.Left, plot.Top, Math.Max(0, bx - plot.Left), plot.Height);
                    if (bx - plot.Left > 70 * k)
                        Text(g, "ΥΠΟΒΡΥΧΙΑ", F(6.75f, k, false, FontStyle.Bold), SwimTheme.Green, plot.Left + 5 * k, plot.Bottom - 3 * k, StringAlignment.Near, StringAlignment.Far);
                }
            }

            // Checkpoints of the length.
            Font tick = F(6.75f, k, false, FontStyle.Bold);
            using (Pen passed = new Pen(Color.FromArgb(220, SwimTheme.Amber), Math.Max(1f, 0.9f * k)) { DashStyle = DashStyle.Dash })
            using (Pen coming = new Pen(Color.FromArgb(150, SwimTheme.Faint), Math.Max(1f, 0.9f * k)) { DashStyle = DashStyle.Dash })
            {
                Text(g, ((int)d0).ToString(CultureInfo.InvariantCulture), axis, SwimTheme.Faint, plot.Left, plot.Bottom + 4 * k, StringAlignment.Center);
                foreach (SwimRow row in s.Computed.Rows.Where(row => row.D > d0 && row.D <= d1))
                {
                    bool done = row.PassedAt(t);
                    float x = X(row.D);
                    g.DrawLine(done ? passed : coming, x, plot.Top, x, plot.Bottom);
                    Text(g, row.D.ToString(CultureInfo.InvariantCulture), tick, done ? SwimTheme.Amber : SwimTheme.Faint, x, plot.Bottom + 4 * k, StringAlignment.Center);
                }
            }

            // Cycles of the length up to now: distance as bars, speed as a line.
            List<SwimReelCycle> cycles = reel.Cycles.Where(c => c.Lap == lap && c.Time <= t + 1e-6).ToList();
            float barH = plot.Height - topH - 6 * k;
            double dpsTop = DpsScale(reel.Cycles.Select(c => c.Dps));
            using (SolidBrush bar = new SolidBrush(Color.FromArgb(150, SwimTheme.Amber)))
            {
                foreach (SwimReelCycle c in cycles)
                {
                    float xa = X(reel.DistanceAt(c.Start)) + 1.5f * k, xb = X(reel.DistanceAt(c.Time)) - 1.5f * k;
                    float h = (float)(Math.Max(0, Math.Min(dpsTop, c.Dps)) / dpsTop * barH);
                    if (xb > xa)
                        g.FillRectangle(bar, xa, plot.Bottom - h, xb - xa, h);
                }
            }

            List<PointF> points = cycles.Select(c => new PointF(X(reel.DistanceAt(c.Time)), Y(c.Speed))).ToList();
            if (points.Count >= 2)
            {
                using (Pen line = new Pen(SwimTheme.Accent, Math.Max(1.5f, 2.2f * k)) { LineJoin = LineJoin.Round })
                    g.DrawLines(line, points.ToArray());
            }

            using (SolidBrush dot = new SolidBrush(SwimTheme.Accent))
            {
                foreach (PointF p in points)
                    g.FillEllipse(dot, p.X - 3 * k, p.Y - 3 * k, 6 * k, 6 * k);
            }

            // Where the swimmer is.
            if (t < 0)
                return;

            double now = reel.DistanceAt(t);
            if (now < d0 - 1e-6 || now > d1 + 1e-6)
                return;

            float xs = X(now);
            string label = now.ToString("0.0", CultureInfo.InvariantCulture) + " " + Unit(s.Race);
            Font tagFont = F(7.5f, k, false, FontStyle.Bold);
            float w = Width(g, label, tagFont) + 12 * k;
            float cx = Math.Max(plot.Left + w / 2, Math.Min(plot.Right - w / 2, xs));
            RectangleF tag = new RectangleF(cx - w / 2, plot.Bottom - 18 * k, w, 16 * k);
            using (Pen pen = new Pen(Color.FromArgb(230, 255, 255, 255), Math.Max(1f, 1.4f * k)))
                g.DrawLine(pen, xs, plot.Top, xs, tag.Top);
            Pill(g, tag, Color.FromArgb(250, 9, 14, 22), Color.FromArgb(170, 255, 255, 255), Math.Max(1f, 0.8f * k));
            Text(g, label, tagFont, Color.White, tag.Left + tag.Width / 2, tag.Top + tag.Height / 2, StringAlignment.Center, StringAlignment.Center);
        }
        #endregion

        #region Badges
        private static void DrawBadges(Graphics g, SwimReelScene s, Layout L)
        {
            const double life = 2.6;
            double t = s.Time;
            List<SwimReelBadge> shown = s.Reel.Badges.Where(b => b.Time <= t + 1e-6 && t < b.Time + life).ToList();
            if (shown.Count > 2)
                shown = shown.Skip(shown.Count - 2).ToList();

            float k = L.K;
            float y = L.BadgeY;
            for (int i = shown.Count - 1; i >= 0; i--)
            {
                SwimReelBadge b = shown[i];
                double age = t - b.Time;
                float fadeIn = (float)Math.Min(1, age / 0.16);
                float fadeOut = (float)Math.Min(1, (life - age) / 0.24);
                float a = Math.Max(0, Math.Min(fadeIn, fadeOut));
                if (a <= 0.01f)
                    continue;

                RectangleF r = new RectangleF(L.BadgeX + (1 - fadeIn) * 26 * k, y, L.BadgeW, 76 * k);
                if (r.Bottom > L.H - L.M)
                    break;

                Color tone = Tone(b.Tone);
                Card(g, r, tone, k, a);
                Text(g, b.Title, F(8.5f, k, false, FontStyle.Bold), Fade(tone, a), r.Left + 15 * k, r.Top + 7 * k);
                Text(g, b.Value, F(21f, k, true), Fade(Color.White, a), r.Left + 13 * k, r.Top + 20 * k);
                TextIn(g, b.Detail, F(8f, k), Fade(SwimTheme.Dim, a), new RectangleF(r.Left + 15 * k, r.Bottom - 22 * k, r.Width - 26 * k, 16 * k));
                y += r.Height + 8 * k;
            }
        }
        #endregion

        #region Athlete label
        private static void DrawLabel(Graphics g, SwimReelScene s, Layout L)
        {
            SwimTrack track = s.Race.TrackAt(s.Timestamp);
            float x, y, top, bottom;
            if (track == null || !track.TryGet(s.Timestamp, 5, out x, out y, out top, out bottom))
                return;

            // In and out over about 0.4 s at the ends of the part, out after the finish.
            double f = (s.Timestamp - track.Start) / Math.Max(1e-6, track.Step);
            double edge = Math.Min(f, track.X.Count - 1 - f);
            double samples = 20;
            if (s.Timing != null && s.Timing.IsValid)
            {
                double hsf = s.Timing.HighSpeedFactor <= 0 ? 1.0 : s.Timing.HighSpeedFactor;
                samples = 0.4 * 1000.0 * hsf / s.Timing.FrameIntervalMilliseconds * s.Timing.AverageTimestampsPerFrame / Math.Max(1e-6, track.Step);
            }

            samples = Math.Max(4, samples);
            float a = (float)Math.Max(0, Math.Min(1, (edge + 1) / samples));
            // Each part followed is one length: out after its wall (or the finish).
            SwimReel reel = s.Reel;
            double end = reel.Finish ?? double.PositiveInfinity;
            if (s.Timing != null && s.Timing.IsValid && s.Race.StartTimestamp.HasValue)
                end = reel.LapEnd(reel.LapAt(s.Timing.Seconds(track.Start, s.Race.StartTimestamp.Value) + 0.05));
            if (s.Time > end + 0.4)
                a *= (float)Math.Max(0, 1 - (s.Time - end - 0.4) / 0.4);
            if (a <= 0.01f)
                return;

            float k = L.K;
            PointF c = s.FromImage(new PointF(x, y));
            PointF pt = s.FromImage(new PointF(x, top));
            PointF pb = s.FromImage(new PointF(x, bottom));
            float lane = Math.Abs(pb.Y - pt.Y);
            float rx = Math.Max(20 * k, 0.95f * lane), ry = Math.Max(8 * k, 0.32f * lane);

            using (Pen glow = new Pen(Color.FromArgb((int)(50 * a), SwimTheme.Accent), 6 * k))
                g.DrawEllipse(glow, c.X - rx, c.Y - ry, 2 * rx, 2 * ry);
            using (Pen ring = new Pen(Color.FromArgb((int)(215 * a), SwimTheme.Accent), Math.Max(1.2f, 2f * k)))
                g.DrawEllipse(ring, c.X - rx, c.Y - ry, 2 * rx, 2 * ry);

            string surname = string.IsNullOrWhiteSpace(s.Race.Athlete) ? "ΑΘΛΗΤΗΣ" : SwimAthleteList.Normalize(s.Race.Athlete).Split(' ')[0];
            string text = s.Race.Lane > 0 ? surname + " · " + s.Race.Lane.ToString(CultureInfo.InvariantCulture) : surname;
            Font font = F(10.5f, k, false, FontStyle.Bold);
            float w = Width(g, text, font) + 22 * k, h = 25 * k;
            float ly = Math.Max(L.M, Math.Min(pt.Y, c.Y - ry) - h - 12 * k);
            float lx = Math.Max(L.M, Math.Min(L.W - L.M - w, c.X - w / 2));
            RectangleF pill = new RectangleF(lx, ly, w, h);

            using (Pen stem = new Pen(Color.FromArgb((int)(180 * a), SwimTheme.Accent), Math.Max(1f, 1.4f * k)))
                g.DrawLine(stem, c.X, pill.Bottom, c.X, c.Y - ry);
            Pill(g, pill, Color.FromArgb((int)(225 * a), 9, 14, 22), Color.FromArgb((int)(255 * a), SwimTheme.Accent), Math.Max(1.2f, 1.6f * k));
            Text(g, text, font, Fade(Color.White, a), pill.Left + w / 2, pill.Top + h / 2, StringAlignment.Center, StringAlignment.Center);
        }
        #endregion

        #region Summary
        /// <summary>The summary card after the finish; whole: alone and complete on a dark background (the graphics of the report).</summary>
        private static void DrawSummary(Graphics g, SwimReelScene s, Layout L, bool whole)
        {
            SwimReel reel = s.Reel;
            float a = whole ? 1f : (float)Math.Max(0, Math.Min(1, (s.Time - reel.Finish.Value - SummaryDelay(s)) / 0.4));
            if (a <= 0.01f)
                return;

            float k = L.K;
            if (whole)
            {
                using (LinearGradientBrush back = new LinearGradientBrush(new RectangleF(0, 0, L.W, L.H), SwimTheme.Back, SwimTheme.Surface, LinearGradientMode.Vertical))
                    g.FillRectangle(back, 0, 0, L.W, L.H);
            }
            else
            {
                using (SolidBrush dim = new SolidBrush(Color.FromArgb((int)(195 * a), 5, 8, 13)))
                    g.FillRectangle(dim, 0, 0, L.W, L.H);
            }

            float mx = whole ? L.M : Math.Max(L.M, L.W * 0.06f), my = whole ? L.M : Math.Max(L.M, L.H * 0.06f);
            RectangleF card = new RectangleF(mx, my, L.W - 2 * mx, L.H - 2 * my);
            Card(g, card, SwimTheme.Amber, k, a);

            float x0 = card.Left + 30 * k;
            float inner = card.Width - 60 * k;
            TextIn(g, Title(s.Race, false), F(11f, k, false, FontStyle.Bold), Fade(SwimTheme.Dim, a), new RectangleF(x0, card.Top + 14 * k, inner, 24 * k));
            Text(g, SwimReference.FormatTime(reel.FinalTime ?? reel.Finish.Value), F(54f, k, true), Fade(SwimTheme.Amber, a), x0 - 4 * k, card.Top + 36 * k);
            DrawResultRibbon(g, s, L, card, a);

            // Values of the race.
            SwimComputed c = s.Computed;
            string unit = Unit(s.Race);
            string partsLabel = string.Join(" / ", reel.Parts.Select(p => p.From.ToString(CultureInfo.InvariantCulture) + "-" + p.To.ToString(CultureInfo.InvariantCulture)));
            Func<Func<SwimReelPart, double?>, string, string> joined = (get, format) =>
                string.Join(" / ", reel.Parts.Select(p => get(p).HasValue ? get(p).Value.ToString(format, CultureInfo.InvariantCulture) : "–"));

            double? reaction = c.ReactionTime ?? s.Race.Reaction;
            double bd;
            bool hasBreakout = reel.BreakoutDistances.TryGetValue(0, out bd);
            string under = reel.FirstBreakout.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.00} s κάτω από το νερό", reel.FirstBreakout.Value - (reel.Entry ?? 0))
                : "";
            List<string[]> tiles = new List<string[]>
            {
                new[] { "ΑΝΤΙΔΡΑΣΗ", reaction.HasValue ? SwimReel.Seconds(reaction.Value) : "–",
                    c.BlockTime.HasValue ? string.Format(CultureInfo.InvariantCulture, "πόδια εκτός βατήρα {0:0.00} s", c.BlockTime.Value) : "" },
                new[] { "15 " + UnitCaps(s.Race) + " ΑΠΟ ΤΗΝ ΕΚΚΙΝΗΣΗ", c.Start15.HasValue ? SwimReel.Seconds(c.Start15.Value) : "–",
                    c.EntryTime.HasValue ? string.Format(CultureInfo.InvariantCulture, "είσοδος στο νερό {0:0.00} s", c.EntryTime.Value) : "" },
                new[] { "BREAKOUT", hasBreakout ? (reel.BreakoutMeasured.Contains(0) ? "" : "≈") + bd.ToString("0.0", CultureInfo.InvariantCulture) + " " + unit : "–", under },
                new[] { "ΧΡΟΝΟΣ ΑΝΑ ΤΜΗΜΑ", string.Join(" / ", reel.Parts.Select(p => p.Time.HasValue ? SwimReference.FormatTime(p.Time.Value) : "–")), partsLabel + " " + unit },
                new[] { "ΧΕΡΙΕΣ", string.Join(" / ", reel.Parts.Select(p => p.Strokes.ToString(CultureInfo.InvariantCulture))), "σημάδια χεριών ανά τμήμα" },
                new[] { "ΣΥΧΝΟΤΗΤΑ", joined(p => p.Tempo, "0.00"), "δευτερόλεπτα ανά κύκλο" },
                new[] { "ΑΠΟΣΤΑΣΗ/ΚΥΚΛΟ", "≈" + joined(p => p.Dps, "0.00"), "μέτρα ανά κύκλο" },
                new[] { "ΤΑΧΥΤΗΤΑ", joined(p => p.Speed, "0.00"), "m/s ανά τμήμα" }
            };

            float gap = 12 * k;
            float tw = (inner - 3 * gap) / 4, th = 70 * k;
            float ty = card.Top + 118 * k;
            Font tileCaption = F(8f, k, false, FontStyle.Bold);
            Font tileDetail = F(7.5f, k);
            for (int i = 0; i < tiles.Count; i++)
            {
                RectangleF r = new RectangleF(x0 + (i % 4) * (tw + gap), ty + (i / 4) * (th + gap), tw, th);
                SwimTheme.FillRounded(g, Color.FromArgb((int)(240 * a), SwimTheme.Raised), r, 8 * k);
                TextIn(g, tiles[i][0], tileCaption, Fade(SwimTheme.Dim, a), new RectangleF(r.Left + 12 * k, r.Top + 4 * k, r.Width - 18 * k, 18 * k));

                // The value in the width of the tile (four parts of a long race take more room).
                float size = 20f;
                Font value = F(size, k, true);
                while (size > 10f && Width(g, tiles[i][1], value) > r.Width - 22 * k)
                {
                    size -= 1.5f;
                    value = F(size, k, true);
                }

                Text(g, tiles[i][1], value, Fade(Color.White, a), r.Left + 10 * k, r.Top + th / 2, StringAlignment.Near, StringAlignment.Center);
                TextIn(g, tiles[i][2], tileDetail, Fade(SwimTheme.Dim, a), new RectangleF(r.Left + 12 * k, r.Bottom - 20 * k, r.Width - 18 * k, 16 * k));
            }

            // Distance of each cycle, the lengths apart (or of each length, in a long race), with the tempo under it.
            float by0 = ty + 2 * th + gap + 36 * k;
            float by1 = card.Bottom - 46 * k;
            List<List<SwimReelCycle>> lengths = reel.Cycles.GroupBy(cy => cy.Lap).OrderBy(gr => gr.Key).Select(gr => gr.ToList()).ToList();
            bool perCycle = reel.Cycles.Count <= 80;
            List<KeyValuePair<double, double>> bars = new List<KeyValuePair<double, double>>();
            List<int> lapOfBar = new List<int>();
            for (int l = 0; l < lengths.Count; l++)
            {
                if (perCycle)
                {
                    bars.AddRange(lengths[l].Select(cy => new KeyValuePair<double, double>(cy.Dps, cy.Tempo)));
                    lapOfBar.AddRange(lengths[l].Select(cy => l));
                }
                else
                {
                    bars.Add(new KeyValuePair<double, double>(lengths[l].Average(cy => cy.Dps), lengths[l].Average(cy => cy.Tempo)));
                    lapOfBar.Add(l);
                }
            }

            if (bars.Count > 0 && by1 - by0 > 40 * k)
            {
                // A gap of half a bar between the lengths.
                float space = perCycle ? 0.6f : 0f;
                float bw = inner / (bars.Count + space * (lengths.Count - 1));
                bool labels = bw >= 24 * k;
                string barsTitle = !perCycle ? "ΑΝΑ ΜΗΚΟΣ  ·  μέση απόσταση ανά κύκλο (m) και, κάτω, συχνότητα (s)"
                    : labels ? "ΑΝΑ ΚΥΚΛΟ  ·  απόσταση (m) και, κάτω, συχνότητα (s)"
                    : "ΑΝΑ ΚΥΚΛΟ  ·  απόσταση ανά κύκλο και, κάτω, μέση συχνότητα κάθε μήκους (s)";
                Text(g, barsTitle, F(8.5f, k, false, FontStyle.Bold), Fade(SwimTheme.Dim, a), x0, by0 - 8 * k, StringAlignment.Near, StringAlignment.Far);
                double median = bars.Select(v => v.Key).OrderBy(v => v).ElementAt(bars.Count / 2);
                double dpsTop = DpsScale(bars.Select(v => v.Key));
                float maxH = by1 - by0 - 20 * k;
                Font valueFont = F(7f, k, false, FontStyle.Bold);
                Font tempoFont = F(6.75f, k);
                float inset = Math.Min(3 * k, bw * 0.15f);
                float x = x0;
                float groupLeft = x0;
                for (int i = 0; i < bars.Count; i++)
                {
                    if (i > 0 && lapOfBar[i] != lapOfBar[i - 1])
                        x += space * bw;
                    if (i == 0 || lapOfBar[i] != lapOfBar[i - 1])
                        groupLeft = x;

                    float h = (float)(Math.Max(0, Math.Min(dpsTop, bars[i].Key)) / dpsTop * maxH);
                    RectangleF r = new RectangleF(x + inset, by1 - h, Math.Max(1, bw - 2 * inset), h);
                    Color color = bars[i].Key >= median ? SwimTheme.Green : SwimTheme.Amber;
                    if (r.Height >= 1)
                        SwimTheme.FillRounded(g, Color.FromArgb((int)(220 * a), color), r, Math.Min(4 * k, r.Width / 3));
                    if (labels)
                    {
                        Text(g, bars[i].Key.ToString("0.00", CultureInfo.InvariantCulture), valueFont, Fade(SwimTheme.Text, a), r.Left + r.Width / 2, r.Top - 2 * k, StringAlignment.Center, StringAlignment.Far);
                        Text(g, bars[i].Value.ToString("0.00", CultureInfo.InvariantCulture), tempoFont, Fade(SwimTheme.Dim, a), r.Left + r.Width / 2, by1 + 3 * k, StringAlignment.Center);
                    }

                    x += bw;

                    // Narrow bars: the length and its tempo under its bars.
                    bool lastOfLength = i == bars.Count - 1 || lapOfBar[i + 1] != lapOfBar[i];
                    if (!labels && lastOfLength)
                    {
                        List<SwimReelCycle> length = lengths[lapOfBar[i]];
                        string caption = string.Format(CultureInfo.InvariantCulture, "ΜΗΚΟΣ {0} · {1:0.00} s", lapOfBar[i] + 1, length.Average(cy => cy.Tempo));
                        if (Width(g, caption, tempoFont) > x - groupLeft - 4 * k)
                            caption = string.Format(CultureInfo.InvariantCulture, "{0} · {1:0.00} s", lapOfBar[i] + 1, length.Average(cy => cy.Tempo));
                        TextIn(g, caption, tempoFont, Fade(SwimTheme.Dim, a), new RectangleF(groupLeft, by1 + 2 * k, x - groupLeft, 14 * k), StringAlignment.Center);
                    }
                }
            }

            string note = "Χρόνοι και χεριές από τα σημάδια της ανάλυσης · απόσταση και ταχύτητα ανά κύκλο εκτιμώνται από τα περάσματα (≈)";
            if (reel.DoubleMarks.Count > 0)
            {
                note += string.Format(CultureInfo.InvariantCulture, " · {0} διπλό χτύπημα χεριάς δεν μετρήθηκε ({1})", reel.DoubleMarks.Count,
                    string.Join(", ", reel.DoubleMarks.Take(3).Select(d => d.ToString("0.00", CultureInfo.InvariantCulture) + " s")));
            }

            TextIn(g, note, F(7.25f, k), Fade(SwimTheme.Dim, a), new RectangleF(x0, card.Bottom - 26 * k, inner, 18 * k));
        }

        /// <summary>
        /// On the right of the final time: the place the athlete would have in the final of the event (and the gap to the first), else
        /// the Δ with the reference at the finish.
        /// </summary>
        private static void DrawResultRibbon(Graphics g, SwimReelScene s, Layout L, RectangleF card, float a)
        {
            float k = L.K;
            string caption = null, value = null;
            Color color = SwimTheme.Amber;
            List<SwimFinalsBoardRow> board = FinishBoard(s);
            SwimFinalsBoardRow me = board != null ? board.FirstOrDefault(b => b.IsAthlete) : null;
            if (me != null)
            {
                caption = "ΘΕΣΗ ΣΤΟΝ ΤΕΛΙΚΟ  ·  " + SwimAthleteList.Normalize(s.Final.Short);
                string gap = me.Place == 1
                    ? SwimReference.FormatDelta(me.Behind) + " ΑΠΟ ΤΟΝ ΝΙΚΗΤΗ"
                    : "+" + me.Behind.ToString("0.00", CultureInfo.InvariantCulture) + " ΑΠΟ ΤΟΝ 1Ο";
                value = me.Place.ToString(CultureInfo.InvariantCulture) + "η  ·  " + gap;
                if (me.Place <= 3)
                    color = SwimTheme.Green;
            }
            else if (s.Delta && s.Computed.Reference != null)
            {
                SwimRow finish = s.Computed.Rows.FirstOrDefault(r => r.D >= s.Race.Distance && r.Delta.HasValue);
                if (finish != null)
                {
                    caption = "Δ ΑΠΟ ΤΗΝ ΑΝΑΦΟΡΑ  ·  " + ReferenceShort(s.Computed.Reference);
                    value = SwimReference.FormatDelta(finish.Delta.Value) + " s";
                    color = finish.Delta.Value <= 0 ? SwimTheme.Green : SwimTheme.Red;
                }
            }

            if (value == null)
                return;

            float right = card.Right - 30 * k;
            float left = card.Left + card.Width * 0.42f;
            TextIn(g, caption, F(8.5f, k, false, FontStyle.Bold), Fade(SwimTheme.Dim, a), new RectangleF(left, card.Top + 46 * k, right - left, 18 * k), StringAlignment.Far);
            Font font = F(19f, k, true);
            float size = 19f;
            while (size > 11f && Width(g, value, font) > right - left)
            {
                size -= 1.5f;
                font = F(size, k, true);
            }

            Text(g, value, font, Fade(color, a), right, card.Top + 66 * k, StringAlignment.Far);
        }

        /// <summary>
        /// The swimmers of the final at the last checkpoint passed and the athlete among them, as on the television, for a few seconds
        /// (longer at the finish), at the bottom on the left. Its place, empty when it is not drawn.
        /// </summary>
        private static RectangleF DrawFinalsBoard(Graphics g, SwimReelScene s, Layout L)
        {
            if (s.Final == null)
                return RectangleF.Empty;

            double t = s.Time;
            SwimRow row = s.Computed.Rows.Where(r => r.PassedAt(t) && s.Final.Swimmers.Any(sw => sw.TimeAt(r.D).HasValue))
                .OrderBy(r => r.OnVideo).LastOrDefault();
            if (row == null)
                return RectangleF.Empty;

            bool finish = row.D >= s.Race.Distance;
            if (t - row.OnVideo > (finish ? FinishBoardSeconds : BoardSeconds))
                return RectangleF.Empty;

            List<SwimFinalsBoardRow> rows = SwimFinalsCompare.Board(s.Final, row.D, row.T.Value, s.BoardName);
            if (rows == null)
                return RectangleF.Empty;

            // Under the header and its phase, or under the chart when it is under the header (narrow images).
            float k = L.K;
            float top = L.Header.Bottom + 42 * k;
            if (L.Chart.Left < L.Header.Right)
                top = Math.Max(top, L.Chart.Bottom + 10 * k);
            float scale = 1.25f * k;
            float room = L.H - L.M - top;
            float needed = SwimFinalsPainter.BoardHeight(rows.Count, scale);
            if (needed > room)
                scale *= room / needed;
            if (scale < 0.55f)
                return RectangleF.Empty;

            int width = (int)Math.Round(Math.Min(260 * scale, L.W - 2 * L.M));
            int height = SwimFinalsPainter.BoardHeight(rows.Count, scale);
            Rectangle r0 = new Rectangle((int)Math.Round(L.M), (int)Math.Round(L.H - L.M - height), width, height);
            SwimFinalsPainter.PaintBoard(g, r0, SwimFinalsPainter.BoardTitle(s.Final, row.D, finish), SwimFinalsPainter.BoardSubtitle(s.Final), rows, scale);
            return r0;
        }

        /// <summary>The Δ with the reference at each checkpoint passed, at the bottom on the right (not over the board of the final).</summary>
        private static void DrawDeltaCard(Graphics g, SwimReelScene s, Layout L, RectangleF board)
        {
            if (!s.Delta || s.Computed.Reference == null)
                return;

            double t = s.Time;
            List<SwimDeltaPoint> points = s.Computed.Rows.Where(r => r.Delta.HasValue && r.PassedAt(t))
                .Select(r => new SwimDeltaPoint { D = r.D, Delta = r.Delta.Value }).ToList();
            if (points.Count == 0)
                return;

            float k = L.K;
            float w = Math.Min(400 * k, L.W - 2 * L.M), h = Math.Max(90 * k, w * 0.3f);
            RectangleF card = new RectangleF(L.W - L.M - w, L.H - L.M - h, w, h);
            if (!board.IsEmpty && card.Left < board.Right + L.M)
                return;
            if (card.Top < L.Header.Bottom + 42 * k)
                return;

            SwimFinalsPainter.PaintDelta(g, Rectangle.Round(card), points, s.Race.Distance, s.Reel.Pool, ReferenceShort(s.Computed.Reference), true, 1.15f * k);
        }

        /// <summary>A short name of the reference: "CECCON · ΠΑΡΙΣΙ 2024 (1ος)" for a swimmer of a final, else its name.</summary>
        private static string ReferenceShort(SwimReference reference)
        {
            if (reference == null)
                return "";
            return string.IsNullOrWhiteSpace(reference.Name) ? reference.Id ?? "" : reference.Name.Trim();
        }
        #endregion
    }
}
