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
    /// (2.42.0) The exchange of a relay on the HUD: from the touch of the swimmer who arrives, the time runs frame by frame until the feet
    /// of the next one leave the block (F), then its time stays for two seconds, in the colour of its zone, with its place on the scale
    /// of the zones (−0.03 not valid, the target 0.10–0.20), the two swimmers, the official time when the results give it and the
    /// snapshots taken. In the analysis and in every video made with the graphics (exports, the package).
    /// </summary>
    public static partial class SwimHudPainter
    {
        private static readonly Color ExInvalid = Color.FromArgb(255, 84, 92);
        private static readonly Color ExRisk = Color.FromArgb(77, 148, 255);
        private static readonly Color ExTarget = Color.FromArgb(61, 235, 160);
        private static readonly Color ExSlow = Color.FromArgb(255, 190, 92);
        private static readonly Color ExVerySlow = Color.FromArgb(255, 128, 64);

        /// <summary>The colours of the four swimmers of a relay (their dots on the HUD and in the report).</summary>
        private static readonly Color[] LegColors =
        {
            Color.FromArgb(232, 84, 84), Color.FromArgb(242, 194, 48), Color.FromArgb(77, 148, 255), Color.FromArgb(61, 220, 150)
        };

        private static Color ZoneColor(int zone)
        {
            switch (zone)
            {
                case 0: return ExInvalid;
                case 1: return ExRisk;
                case 2: return ExTarget;
                case 3: return ExSlow;
                default: return ExVerySlow;
            }
        }

        private const float ExchangeW = 492, ExchangeH = 292;

        private static void DrawExchange(Graphics g, SwimHudScene s, State st, Layout L)
        {
            if (s.Race == null || !s.Race.IsRelayOfFour || double.IsNaN(st.T))
                return;

            double shown;
            bool running;
            SwimExchange x = SwimRelay.ShownAt(SwimRelay.Exchanges(s.Race, s.Computed), st.T, out shown, out running);
            if (x == null || !x.Touch.HasValue)
                return;

            double start = x.Touch.Value;
            double end = x.FeetOff.HasValue ? Math.Max(x.Touch.Value, x.FeetOff.Value) + SwimRelay.HoldSeconds : x.Touch.Value + SwimRelay.RunningSeconds;
            float a = Window(st.T, start - 0.12, end, 0.12, 0.3);
            if (a <= 0.01f)
                return;

            RectangleF r = new RectangleF(L.Video.Right - ExchangeW - 18, L.Video.Top + 16, ExchangeW, ExchangeH);
            if (r.Left < L.Video.Left + 8)
                r.X = L.Video.Left + 8;
            string unit = s.Race.IsYards ? "yd" : SwimLang.T("μ.");
            Fade(g, L, r, layer => PaintExchange(layer, r, x, shown, running, unit, a));
        }

        private static void PaintExchange(Graphics g, RectangleF r, SwimExchange x, double shown, bool running, string unit, float a)
        {
            Panel(g, r, SwimLang.T("ΑΛΛΑΓΗ ΣΚΥΤΑΛΗΣ"), null, a, 15);

            // The exchange and its wall, at the top right.
            string chip = string.Format(CultureInfo.InvariantCulture, "{0} → {1} · {2}{3}", x.Number, x.Number + 1, x.Wall, unit);
            Font cf = Ui(14, true);
            float cw = Width(g, chip, cf) + 24;
            RectangleF cr = new RectangleF(r.Right - 18 - cw, r.Top + 10, cw, 28);
            using (SolidBrush back = new SolidBrush(A(Color.FromArgb(46, 56, 225, 255), a)))
                g.FillRectangle(back, cr);
            using (Pen pen = new Pen(A(C, a), 1.5f))
                g.DrawRectangle(pen, cr.X, cr.Y, cr.Width, cr.Height);
            Text(g, chip, cf, A(Ice, a), cr.Left + cw / 2, cr.Top + 4, 0);

            // The two swimmers: the one who arrives → the next one, with their colours.
            float nx = r.Left + 20, ny = r.Top + 48;
            Font nf = Ui(18, true);
            string from = x.From.Length > 0 ? x.From : string.Format(CultureInfo.InvariantCulture, SwimLang.T("Σκέλος {0}"), x.Number);
            string to = x.To.Length > 0 ? x.To : string.Format(CultureInfo.InvariantCulture, SwimLang.T("Σκέλος {0}"), x.Number + 1);
            nx = LegDot(g, nx, ny + 13, LegColors[(x.Number - 1) % 4], a);
            Text(g, from, nf, A(Ice, a), nx, ny);
            nx += Width(g, from, nf) + 12;
            Text(g, "→", nf, A(C, a), nx, ny - 1);
            nx += Width(g, "→", nf) + 12;
            nx = LegDot(g, nx, ny + 13, LegColors[x.Number % 4], a);
            Text(g, to, nf, A(Ice, a), nx, ny);

            // The time: running (cyan) from the touch, then in the colour of its zone.
            int zone = SwimRelay.Zone(shown);
            Color tone = running ? C : ZoneColor(zone);
            string figure = SwimRelay.Figure(shown);
            Font big = Thin(112);
            Glow(g, figure, big, A(tone, a), r.Left + 14, r.Top + 70, -1, 1.3f * a, tone);
            Text(g, "s", Ui(28, true), A(tone, a), r.Left + 22 + Width(g, figure, big), r.Top + 140);

            // The scale of the zones and the place of the time on it.
            RectangleF track = new RectangleF(r.Left + 22, r.Top + 200, r.Width - 44, 14);
            Func<double, float> X = v => track.Left + (float)((Math.Max(SwimRelay.ScaleFrom, Math.Min(SwimRelay.ScaleTo, v)) - SwimRelay.ScaleFrom) /
                (SwimRelay.ScaleTo - SwimRelay.ScaleFrom)) * track.Width;
            double[] ends = { SwimRelay.ScaleFrom, SwimRelay.Invalid, SwimRelay.TargetFrom, SwimRelay.TargetTo, SwimRelay.Slow, SwimRelay.ScaleTo };
            for (int i = 0; i < 5; i++)
            {
                RectangleF seg = RectangleF.FromLTRB(X(ends[i]), track.Top, X(ends[i + 1]), track.Bottom);
                using (SolidBrush b = new SolidBrush(A(ZoneColor(i), 0.88f * a)))
                    g.FillRectangle(b, seg);
            }

            RectangleF target = RectangleF.FromLTRB(X(SwimRelay.TargetFrom) - 1, track.Top - 4, X(SwimRelay.TargetTo) + 1, track.Bottom + 4);
            using (Pen box = new Pen(A(Color.White, 0.95f * a), 2.2f))
                g.DrawRectangle(box, target.X, target.Y, target.Width, target.Height);

            float mx = X(shown);
            using (Pen glow = new Pen(A(Color.FromArgb(90, 255, 255, 255), a), 9f))
                g.DrawLine(glow, mx, track.Top - 12, mx, track.Bottom + 6);
            using (Pen marker = new Pen(A(running ? C2 : Color.White, a), 3.5f))
                g.DrawLine(marker, mx, track.Top - 12, mx, track.Bottom + 6);
            using (SolidBrush tip = new SolidBrush(A(running ? C2 : Color.White, a)))
                g.FillPolygon(tip, new[] { new PointF(mx - 7, track.Top - 20), new PointF(mx + 7, track.Top - 20), new PointF(mx, track.Top - 11) });

            // (The 0 beside the −0.03 crowded it: the limits of the zones only.)
            Font tf = Mono(12);
            foreach (double v in new[] { SwimRelay.Invalid, SwimRelay.TargetFrom, SwimRelay.TargetTo, SwimRelay.Slow, SwimRelay.ScaleTo })
            {
                string label = v < 0 ? "−" + Math.Abs(v).ToString("0.00", CultureInfo.InvariantCulture) : v == 0 ? "0" : v.ToString("0.00", CultureInfo.InvariantCulture);
                Text(g, label, tf, A(Lbl, a), X(v), track.Bottom + 7, 0);
            }

            // What the time says (or that it runs), the snapshots taken.
            string verdict = running ? SwimLang.T("● μετράει από την επαφή…") : SwimRelay.Verdict(shown);
            Color vc = running ? C : tone;
            Font vf = Ui(14, true);
            float vw = Width(g, verdict, vf) + 24;
            RectangleF vr = new RectangleF(r.Left + 20, r.Top + 240, Math.Min(vw, r.Width - 120), 28);
            using (SolidBrush back = new SolidBrush(A(Color.FromArgb(40, vc), a)))
                g.FillRectangle(back, vr);
            using (Pen pen = new Pen(A(vc, a), 1.5f))
                g.DrawRectangle(pen, vr.X, vr.Y, vr.Width, vr.Height);
            Text(g, verdict, vf, A(running ? C2 : Ice, a), vr.Left + 12, vr.Top + 4);

            if (x.Snapshots > 0)
            {
                string count = x.Snapshots.ToString(CultureInfo.InvariantCulture);
                Font kf = Ui(16, true);
                float right = r.Right - 20;
                Text(g, count, kf, A(Ice, a), right, vr.Top + 3, 1);
                DrawCamera(g, right - Width(g, count, kf) - 32, vr.Top + 6, a);
            }

            // The moments and the official time.
            List<string> foot = new List<string>();
            foot.Add(SwimLang.T("επαφή ") + SwimReference.FormatTime(x.Touch.Value));
            if (x.FeetOff.HasValue && !running)
                foot.Add(SwimLang.T("πόδια φεύγουν ") + SwimReference.FormatTime(x.FeetOff.Value));
            if (x.Official.HasValue)
                foot.Add(SwimLang.T("επίσημο ") + SwimRelay.Figure(x.Official.Value));
            Text(g, string.Join(" · ", foot.ToArray()), Mono(12), A(Lbl, a), r.Left + 22, r.Bottom - 20);
        }

        /// <summary>
        /// (2.42.0, the test in the program) The exchange on the boards of before (the image too small for the HUD, or the boards chosen):
        /// the same panel in the corner across from the boards (they stand in a top right corner: bottom left; 'right', 'top': where it
        /// goes), a third of the width of the image (180–492 pixels), over them where the image is small. Only from the touch to two
        /// seconds after the feet left the block, as on the HUD.
        /// </summary>
        public static void PaintExchangeBoard(Graphics g, Size size, SwimHudScene s, bool right, bool top)
        {
            if (g == null || s == null || s.Race == null || s.Computed == null || !s.Race.IsRelayOfFour || s.Race.IsTraining || double.IsNaN(s.Time) ||
                size.Width < 300 || size.Height < 200)
                return;

            double shown;
            bool running;
            SwimExchange x = SwimRelay.ShownAt(SwimRelay.Exchanges(s.Race, s.Computed), s.Time, out shown, out running);
            if (x == null || !x.Touch.HasValue)
                return;

            double start = x.Touch.Value;
            double end = x.FeetOff.HasValue ? Math.Max(x.Touch.Value, x.FeetOff.Value) + SwimRelay.HoldSeconds : x.Touch.Value + SwimRelay.RunningSeconds;
            float a = Window(s.Time, start - 0.12, end, 0.12, 0.3);
            if (a <= 0.01f)
                return;

            float width = Math.Max(180f, Math.Min(ExchangeW, size.Width * 0.34f));
            float k = width / ExchangeW;
            float height = ExchangeH * k;
            GraphicsState saved = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.TranslateTransform(right ? size.Width - width - 12 : 12, top ? 12 : Math.Max(12, size.Height - height - 12));
                g.ScaleTransform(k, k);
                string unit = s.Race.IsYards ? "yd" : SwimLang.T("μ.");
                PaintExchange(g, new RectangleF(0, 0, ExchangeW, ExchangeH), x, shown, running, unit, a);
            }
            finally
            {
                g.Restore(saved);
            }
        }

        /// <summary>The dot of a swimmer's colour; returns where his name starts.</summary>
        private static float LegDot(Graphics g, float x, float cy, Color color, float a)
        {
            using (SolidBrush glow = new SolidBrush(A(Color.FromArgb(70, color), a)))
                g.FillEllipse(glow, x - 3, cy - 9, 18, 18);
            using (SolidBrush dot = new SolidBrush(A(color, a)))
                g.FillEllipse(dot, x + 1, cy - 5, 10, 10);
            return x + 22;
        }

        /// <summary>A small camera (the snapshots of the exchange): body, lens, top.</summary>
        private static void DrawCamera(Graphics g, float x, float y, float a)
        {
            using (Pen pen = new Pen(A(C2, a), 1.8f))
            using (SolidBrush fill = new SolidBrush(A(Color.FromArgb(60, 56, 225, 255), a)))
            {
                RectangleF body = new RectangleF(x, y + 4, 26, 16);
                g.FillRectangle(fill, body);
                g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
                g.DrawRectangle(pen, x + 7, y, 10, 4);
                g.DrawEllipse(pen, x + 8, y + 7, 10, 10);
            }
        }
    }
}
