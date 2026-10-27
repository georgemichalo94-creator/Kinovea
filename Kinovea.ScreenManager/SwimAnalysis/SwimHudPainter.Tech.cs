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

namespace Kinovea.ScreenManager
{
    /// <summary>What the technical HUD of a start or a turn of a training video draws at one frame (2.9.9).</summary>
    public sealed class SwimTechHudScene
    {
        public SwimTechRun Run;

        /// <summary>Seconds from the origin of the phase (the start signal or the contact with the wall).</summary>
        public double Time;

        /// <summary>The title of the phase («Στροφή 1»), in the language of the analysis.</summary>
        public string Title;

        /// <summary>Point of the video image (reference pixels) → point of the drawing; null without it.</summary>
        public Func<PointF, PointF> FromImage;

        /// <summary>The head now (reference pixels), null where the reading has no point.</summary>
        public PointF? Head;

        /// <summary>The camera's pan of the reading now (reference pixels): the strokes' places on the water follow it; NaN: none.</summary>
        public double Pan = double.NaN;

        /// <summary>The image of the video (reference pixels).</summary>
        public Size VideoSize;

        /// <summary>After the last stroke measured: the card of the phase.</summary>
        public bool Card;
        public float CardFade = 1f;
    }

    /// <summary>The technical HUD of a training video (2.9.9): drawn with the panels, colours and letters of the HUD of the race.</summary>
    public static partial class SwimHudPainter
    {
        private static readonly Color GlideFull = Color.FromArgb(96, 232, 150);
        private static readonly Color GlideShort = Color.FromArgb(255, 190, 92);
        private static readonly Color GlideQuick = Color.FromArgb(255, 104, 104);

        private static Color GlideColor(SwimGlideClass c)
        {
            switch (c)
            {
                case SwimGlideClass.Full: return GlideFull;
                case SwimGlideClass.Short: return GlideShort;
                case SwimGlideClass.Quick: return GlideQuick;
                default: return C2;
            }
        }

        private static string GlideName(SwimGlideClass c)
        {
            switch (c)
            {
                case SwimGlideClass.Full: return SwimLang.T("ΠΛΗΡΕΣ ΓΛΙΣΤΡΗΜΑ");
                case SwimGlideClass.Short: return SwimLang.T("ΣΥΝΤΟΜΟ ΓΛΙΣΤΡΗΜΑ");
                case SwimGlideClass.Quick: return SwimLang.T("ΓΡΗΓΟΡΟ ΓΛΙΣΤΡΗΜΑ");
                default: return SwimLang.T("ΓΛΙΣΤΡΗΜΑ");
            }
        }

        private static string StyleName(string style)
        {
            switch (style)
            {
                case "fs": return SwimLang.T("ΕΛΕΥΘΕΡΟ");
                case "bk": return SwimLang.T("ΥΠΤΙΟ");
                case "br": return SwimLang.T("ΠΡΟΣΘΙΟ");
                case "fly": return SwimLang.T("ΠΕΤΑΛΟΥΔΑ");
                default: return SwimLang.T("ΜΙΚΤΟ");
            }
        }

        private static string Seconds2(double? s)
        {
            return s.HasValue ? s.Value.ToString("0.00", CultureInfo.InvariantCulture) : "–";
        }

        /// <summary>The technical HUD at a frame: the clock and the values of the phase, the strokes on the water, the timeline, the card.</summary>
        public static void PaintTech(Graphics g, Size size, SwimTechHudScene s)
        {
            if (s == null || s.Run == null || s.Run.Phase == null || !CanPaint(size))
                return;

            Layout L = MakeLayout(size);
            GraphicsState saved = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.ScaleTransform(L.K, L.K);
                Func<PointF, PointF> fromImage = s.FromImage == null ? null : (Func<PointF, PointF>)(p =>
                {
                    PointF q = s.FromImage(p);
                    return new PointF(q.X / L.K, q.Y / L.K);
                });

                float card = s.Card ? s.CardFade : 0f;
                if (card < 0.999f)
                {
                    TechVideoLayer(g, s, L, fromImage);
                    TechClockPanel(g, s, L);
                    TechCounters(g, s, L);
                    TechCyclesPanel(g, s, L);
                    TechTimeline(g, s, L);
                }

                if (card > 0.001f)
                    TechCard(g, s, L, card);
            }
            finally
            {
                g.Restore(saved);
            }
        }

        #region On the video
        private static void TechVideoLayer(Graphics g, SwimTechHudScene s, Layout L, Func<PointF, PointF> fromImage)
        {
            SwimTechRun run = s.Run;
            if (fromImage == null || s.VideoSize.Width <= 0)
                return;

            PointF top = fromImage(new PointF(0, 0)), bottom = fromImage(new PointF(s.VideoSize.Width, s.VideoSize.Height));
            RectangleF video = RectangleF.FromLTRB(top.X, top.Y, bottom.X, bottom.Y);

            // The strokes on the water: where the head was at each stroke, in the frame of the pool (the camera's pan of the reading).
            if (!double.IsNaN(s.Pan))
            {
                Font tag = Ui(13, true);
                foreach (SwimTechCycle c in run.Cycles.Concat(run.Approach))
                {
                    if (c.Start > s.Time + 1e-6)
                        continue;
                    double u = run.PoolX(c.Start);
                    if (double.IsNaN(u))
                        continue;
                    float x = fromImage(new PointF((float)(u + s.Pan), 0)).X;
                    if (x < Math.Max(video.Left + 4, 520) || x > video.Right - 4)
                        continue;

                    float age = (float)Math.Max(0, s.Time - c.Start);
                    float alpha = Clamp(1.0 - Math.Max(0, age - 3.0) / 4.0, 0.35, 1.0);
                    using (Pen glow = new Pen(A(Color.FromArgb(60, 56, 225, 255), alpha), 7f))
                    using (Pen line = new Pen(A(Color.FromArgb(220, 143, 243, 255), alpha), 2f))
                    {
                        g.DrawLine(glow, x, video.Top + video.Height * 0.12f, x, video.Bottom - video.Height * 0.08f);
                        g.DrawLine(line, x, video.Top + video.Height * 0.12f, x, video.Bottom - video.Height * 0.08f);
                    }

                    string label = (run.Approach.Contains(c) ? SwimLang.T("Π") : SwimLang.T("Χ")) + c.Number.ToString(CultureInfo.InvariantCulture);
                    if (c.Distance.HasValue)
                        label += " · " + c.Distance.Value.ToString("0.00", CultureInfo.InvariantCulture) + " " + SwimLang.T("μ.");
                    float w = Width(g, label, tag) + 18;
                    RectangleF chip = new RectangleF(x - w / 2, video.Top + video.Height * 0.12f - 32, w, 26);
                    using (SolidBrush back = new SolidBrush(A(Color.FromArgb(200, 4, 30, 48), alpha)))
                        g.FillRectangle(back, chip);
                    using (Pen border = new Pen(A(Color.FromArgb(160, 56, 225, 255), alpha), 1f))
                        g.DrawRectangle(border, chip.X, chip.Y, chip.Width, chip.Height);
                    Text(g, label, tag, A(Ice, alpha), x, chip.Top + 5, 0);
                }
            }

            if (!s.Head.HasValue)
                return;

            // The head: a ring, and the glide of the cycle beside it while it goes on (and a little after).
            PointF head = fromImage(s.Head.Value);
            using (Pen glow = new Pen(Color.FromArgb(70, 56, 225, 255), 7f))
            using (Pen ring = new Pen(C2, 2f))
            {
                g.DrawEllipse(glow, head.X - 34, head.Y - 22, 68, 44);
                g.DrawEllipse(ring, head.X - 34, head.Y - 22, 68, 44);
            }

            SwimTechCycle glideCycle = run.Cycles.Concat(run.Approach).LastOrDefault(c => c.GlideStart.HasValue && s.Time >= c.GlideStart.Value && s.Time < c.End + 1.0);
            if (glideCycle != null && glideCycle.Glide.HasValue)
            {
                bool going = s.Time < glideCycle.End;
                double shown = going ? s.Time - glideCycle.GlideStart.Value : glideCycle.Glide.Value;
                SwimGlideClass cls = going ? SwimGlideClass.None : SwimTechRun.Classify(glideCycle.Glide);
                Color tone = going ? C2 : GlideColor(cls);
                RectangleF bubble = new RectangleF(head.X - 96, head.Y - 118, 192, 76);
                using (SolidBrush back = new SolidBrush(Color.FromArgb(214, 4, 24, 38)))
                    g.FillRectangle(back, bubble);
                using (Pen border = new Pen(Color.FromArgb(200, tone), 1.5f))
                    g.DrawRectangle(border, bubble.X, bubble.Y, bubble.Width, bubble.Height);
                Caps(g, going ? SwimLang.T("ΓΛΙΣΤΡΗΜΑ") : GlideName(cls), Ui(11, true), tone, bubble.Left + bubble.Width / 2, bubble.Top + 8, 0.16f, -1);
                Glow(g, shown.ToString("0.00", CultureInfo.InvariantCulture) + " s", Thin(34), Color.White, bubble.Left + bubble.Width / 2, bubble.Top + 26, 0, 0.8f, tone);
                using (Pen stem = new Pen(Color.FromArgb(160, tone), 1.5f))
                    g.DrawLine(stem, head.X, bubble.Bottom, head.X, head.Y - 22);
            }
        }
        #endregion

        #region Panels
        /// <summary>The clock from the origin, the title and the part of the phase now.</summary>
        private static void TechClockPanel(Graphics g, SwimTechHudScene s, Layout L)
        {
            SwimTechRun run = s.Run;
            RectangleF r = new RectangleF(36, 76, 470, 232);
            bool turn = run.Phase.Kind == SwimPhaseKind.Turn;
            Panel(g, r, SwimLang.Upper(s.Title ?? "") + " · " + StyleName(run.Style),
                turn ? SwimLang.T("ΑΠΟ ΤΟΝ ΤΟΙΧΟ") : SwimLang.T("ΑΠΟ ΤΟ ΣΗΜΑ"));
            double t = s.Time;
            string clock = (t < 0 ? "−" : "") + Math.Abs(t).ToString("0.00", CultureInfo.InvariantCulture);
            Glow(g, clock, Thin(112), Ice, r.Left + 22, r.Top + 40, -1, 1f);
            Caps(g, "s", Ui(22, true), C2, r.Left + 30 + Width(g, clock, Thin(112)), r.Top + 118, 0.1f);
            string part = run.PartAt(t);
            if (!string.IsNullOrEmpty(part))
                Caps(g, SwimLang.Upper(part), Ui(17, true), Amb, r.Left + 24, r.Bottom - 46, 0.18f);
        }

        /// <summary>Strokes, tempo, metres and glide of the last cycle done.</summary>
        private static void TechCounters(Graphics g, SwimTechHudScene s, Layout L)
        {
            SwimTechRun run = s.Run;
            RectangleF r = new RectangleF(36, 320, 470, 104);
            Panel(g, r, null, null);
            double t = s.Time;
            SwimTechCycle last = run.LastDone(t);
            int strokes = t < 0 ? 0 : run.StrokesAt(t);
            string[] labels = { SwimLang.T("ΧΕΡΙΕΣ"), SwimLang.T("ΡΥΘΜΟΣ (s)"), SwimLang.T("Μ./ΧΕΡΙΑ"), SwimLang.T("ΓΛΙΣΤΡΗΜΑ (s)") };
            string[] values =
            {
                strokes.ToString(CultureInfo.InvariantCulture),
                last != null ? Seconds2(last.Tempo) : "–",
                last != null && last.Distance.HasValue ? last.Distance.Value.ToString("0.00", CultureInfo.InvariantCulture) : "–",
                last != null && last.Glide.HasValue ? Seconds2(last.Glide) : "–"
            };
            Color[] tones = { Ice, Ice, Amb, last != null ? GlideColor(SwimTechRun.Classify(last.Glide)) : Ice };
            float cell = r.Width / 4f;
            for (int i = 0; i < 4; i++)
            {
                float x = r.Left + 18 + i * cell;
                using (Pen bar = new Pen(Color.FromArgb(150, i == 2 ? Amb : C), 2f))
                    g.DrawLine(bar, x - 6, r.Top + 18, x - 6, r.Bottom - 16);
                Caps(g, labels[i], Ui(10, true), Lbl, x, r.Top + 16, 0.14f);
                Glow(g, values[i], Thin(40), tones[i], x, r.Top + 36, -1, 0.7f);
            }
        }

        /// <summary>The cycles measured so far: tempo and metres as bars, the glides coloured.</summary>
        private static void TechCyclesPanel(Graphics g, SwimTechHudScene s, Layout L)
        {
            SwimTechRun run = s.Run;
            List<SwimTechCycle> list = (s.Time < 0 ? run.Approach : run.Cycles).Where(c => c.End <= s.Time + 1e-6).ToList();
            RectangleF r = new RectangleF(36, L.H - 290, 470, 168);
            if (r.Top < 440)
                return;

            Panel(g, r, s.Time < 0 ? SwimLang.T("ΚΥΚΛΟΙ ΠΡΟΣΕΓΓΙΣΗΣ") : SwimLang.T("ΚΥΚΛΟΙ ΜΕΤΑ ΤΗΝ ΕΞΟΔΟ"), SwimLang.T("ΡΥΘΜΟΣ · ΓΛΙΣΤΡΗΜΑ"));
            if (list.Count == 0)
            {
                Caps(g, SwimLang.T("ΠΕΡΙΜΕΝΕΙ ΤΗΝ ΠΡΩΤΗ ΧΕΡΙΑ"), Ui(12, true), Lbl, r.Left + 22, r.Top + 70, 0.16f);
                return;
            }

            RectangleF plot = new RectangleF(r.Left + 22, r.Top + 46, r.Width - 44, r.Height - 70);
            double maxTempo = Math.Max(1.5, list.Max(c => c.Tempo));
            int n = Math.Max(list.Count, 5);
            float w = plot.Width / n;
            for (int i = 0; i < list.Count; i++)
            {
                SwimTechCycle c = list[i];
                float h = (float)(c.Tempo / maxTempo * plot.Height);
                RectangleF bar = new RectangleF(plot.Left + i * w + w * 0.18f, plot.Bottom - h, w * 0.64f, h);
                Color tone = c.Glide.HasValue ? GlideColor(SwimTechRun.Classify(c.Glide)) : C;
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(150, tone)))
                    g.FillRectangle(fill, bar);
                using (Pen edge = new Pen(Color.FromArgb(230, tone), 1f))
                    g.DrawRectangle(edge, bar.X, bar.Y, bar.Width, bar.Height);
                Text(g, Seconds2(c.Tempo), Ui(11, true), Ice, bar.Left + bar.Width / 2, bar.Top - 17, 0);
                Text(g, (s.Time < 0 ? SwimLang.T("Π") : SwimLang.T("Χ")) + c.Number.ToString(CultureInfo.InvariantCulture), Ui(10), Lbl, bar.Left + bar.Width / 2, plot.Bottom + 4, 0);
            }
        }

        /// <summary>The phase along its time: approach, wall, underwater, each cycle (its glide coloured), and where the video is.</summary>
        private static void TechTimeline(Graphics g, SwimTechHudScene s, Layout L)
        {
            SwimTechRun run = s.Run;
            SwimTechPhase p = run.Phase;
            double t0 = Math.Min(run.First, p.Kind == SwimPhaseKind.Turn ? -1.0 : 0), t1 = Math.Max(run.Last, (p.Breakout ?? 0) + 1.0);
            if (t1 - t0 < 1)
                return;

            RectangleF r = new RectangleF(540, L.H - 92, Math.Max(200, L.W - 576), 40);
            using (SolidBrush back = new SolidBrush(Color.FromArgb(150, 2, 16, 28)))
                g.FillRectangle(back, r);
            using (Pen border = new Pen(Color.FromArgb(64, 56, 225, 255), 1f))
                g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height);
            Func<double, float> X = t => r.Left + (float)((t - t0) / (t1 - t0) * r.Width);
            Action<double, double, Color, string> seg = (a, b, color, label) =>
            {
                if (b <= a)
                    return;
                RectangleF q = RectangleF.FromLTRB(X(Math.Max(t0, a)), r.Top + 6, X(Math.Min(t1, b)), r.Bottom - 6);
                using (SolidBrush fill = new SolidBrush(color))
                    g.FillRectangle(fill, q);
                if (!string.IsNullOrEmpty(label) && q.Width > Width(g, label, Ui(10, true)) + 8)
                    Text(g, label, Ui(10, true), Color.FromArgb(230, 234, 252, 255), q.Left + q.Width / 2, q.Top + 6, 0);
            };

            foreach (SwimTechCycle c in run.Approach)
                seg(c.Start, c.End, Color.FromArgb(70, 56, 225, 255), SwimLang.T("Π") + c.Number);
            if (p.Kind == SwimPhaseKind.Turn)
                seg(p.RotationStart ?? -0.6, p.Pushoff ?? 0.4, Color.FromArgb(110, 143, 243, 255), SwimLang.T("ΤΟΙΧΟΣ"));
            double uw0 = p.Kind == SwimPhaseKind.Turn ? p.Pushoff ?? 0 : p.Entry ?? p.Block ?? 0;
            if (p.Breakout.HasValue)
                seg(uw0, p.Breakout.Value, Color.FromArgb(90, 20, 90, 160), SwimLang.T("ΥΠΟΒΡΥΧΙΑ"));
            foreach (SwimTechCycle c in run.Cycles)
            {
                seg(c.Start, c.GlideStart ?? c.End, Color.FromArgb(70, 56, 225, 255), SwimLang.T("Χ") + c.Number);
                if (c.GlideStart.HasValue)
                    seg(c.GlideStart.Value, c.End, Color.FromArgb(150, GlideColor(SwimTechRun.Classify(c.Glide))), "");
            }

            float now = X(Math.Max(t0, Math.Min(t1, s.Time)));
            using (Pen glow = new Pen(Color.FromArgb(90, 56, 225, 255), 6f))
            using (Pen line = new Pen(Color.White, 2f))
            {
                g.DrawLine(glow, now, r.Top - 4, now, r.Bottom + 4);
                g.DrawLine(line, now, r.Top - 4, now, r.Bottom + 4);
            }
        }
        #endregion

        #region The card
        /// <summary>
        /// After the last stroke: the phase at a glance. The time, the strokes, the tempo, the metres per stroke and the full glides; the
        /// table of the underwater phase and of each cycle; bars of the metres and of the glides (with the line of a full glide).
        /// </summary>
        private static void TechCard(Graphics g, SwimTechHudScene s, Layout L, float a)
        {
            SwimTechRun run = s.Run;
            SwimTechPhase p = run.Phase;
            List<SwimTechCycle> cycles = run.Cycles;
            float w = 900, h = Math.Min(L.H - 120, 900);
            RectangleF r = new RectangleF((L.W - w) / 2, (L.H - h) / 2, w, h);
            using (SolidBrush dim = new SolidBrush(Color.FromArgb((int)(120 * a), 0, 8, 16)))
                g.FillRectangle(dim, 0, 0, L.W, L.H);
            Panel(g, r, SwimLang.Upper(s.Title ?? "") + " · " + StyleName(run.Style), SwimLang.T("Η ΦΑΣΗ ΜΕ ΜΙΑ ΜΑΤΙΑ"), a);

            double total = run.Last;
            string totalText = total.ToString("0.00", CultureInfo.InvariantCulture);
            Glow(g, totalText, Thin(96), A(Amb, a), r.Left + 30, r.Top + 44, -1, 1f, Amb);
            float after = r.Left + 40 + Width(g, totalText, Thin(96));
            Caps(g, "s", Ui(20, true), A(C2, a), after, r.Top + 110, 0.1f);
            Caps(g, p.Kind == SwimPhaseKind.Turn ? SwimLang.T("ΑΠΟ ΤΟΝ ΤΟΙΧΟ ΩΣ ΤΗΝ ΤΕΛΕΥΤΑΙΑ ΧΕΡΙΑ") : SwimLang.T("ΑΠΟ ΤΟ ΣΗΜΑ ΩΣ ΤΗΝ ΤΕΛΕΥΤΑΙΑ ΧΕΡΙΑ"),
                Ui(11, true), A(Lbl, a), after + 34, r.Top + 116, 0.16f);

            int strokes = run.Strokes.Count;
            double? tempo = cycles.Count > 0 ? cycles.Average(c => c.Tempo) : (double?)null;
            List<double> metres = cycles.Where(c => c.Distance.HasValue).Select(c => c.Distance.Value).ToList();
            int full = cycles.Count(c => SwimTechRun.Classify(c.Glide) == SwimGlideClass.Full);
            int glides = cycles.Count(c => c.Glide.HasValue);
            string line1 = string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} ΧΕΡΙΕΣ · ΡΥΘΜΟΣ {1} s"), strokes, Seconds2(tempo));
            string line2 = (metres.Count > 0 ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} Μ. ΑΝΑ ΧΕΡΙΑ"), metres.Average().ToString("0.00", CultureInfo.InvariantCulture)) : SwimLang.T("Μ. ΑΝΑ ΧΕΡΙΑ: ΧΩΡΙΣ ΓΝΩΣΤΕΣ ΑΠΟΣΤΑΣΕΙΣ")) +
                (glides > 0 ? string.Format(CultureInfo.InvariantCulture, SwimLang.T(" · ΠΛΗΡΗ ΓΛΙΣΤΡΗΜΑΤΑ {0} ΑΠΟ {1}"), full, glides) : "");
            Caps(g, line1, Ui(15, true), A(Ice, a), r.Left + 32, r.Top + 160, 0.14f);
            Caps(g, line2, Ui(15, true), A(Ice, a), r.Left + 32, r.Top + 186, 0.14f);

            // The table.
            float y = r.Top + 230;
            float[] cols = { r.Left + 32, r.Left + 380, r.Left + 560, r.Right - 40 };
            string[] heads = { SwimLang.T("ΧΕΡΙΑ"), SwimLang.T("ΡΥΘΜΟΣ"), SwimLang.T("ΑΠΟΣΤΑΣΗ"), SwimLang.T("ΓΛΙΣΤΡΗΜΑ") };
            for (int i = 0; i < 4; i++)
                Caps(g, heads[i], Ui(11, true), A(Lbl, a), cols[i], y, 0.16f, i == 0 ? 0 : 1);
            y += 24;
            Action<string, string, string, string, Color> row = (c0, c1, c2, c3, glideTone) =>
            {
                using (Pen rule = new Pen(A(Color.FromArgb(50, 56, 225, 255), a), 1f))
                    g.DrawLine(rule, r.Left + 30, y - 4, r.Right - 30, y - 4);
                Text(g, c0, Ui(19, true), A(Ice, a), cols[0], y, -1);
                Text(g, c1, Ui(19, true), A(Ice, a), cols[1], y, 1);
                Text(g, c2, Ui(19, true), A(Amb, a), cols[2], y, 1);
                Text(g, c3, Ui(19, true), A(glideTone, a), cols[3], y, 1);
                y += 32;
            };

            double uw0 = p.Kind == SwimPhaseKind.Turn ? p.Pushoff ?? 0 : p.Entry ?? p.Block ?? 0;
            double? uwTime = p.Breakout.HasValue ? p.Breakout.Value - uw0 : (double?)null;
            double? uwMetres = p.Breakout.HasValue ? run.PositionAt(p.Breakout.Value) : null;
            row(SwimLang.T("Υποβρύχια φάση") + (p.Kicks.Count > 0 ? " · " + p.Kicks.Count + " " + SwimLang.T("δελφ.") : ""),
                uwTime.HasValue ? Seconds2(uwTime) + " s" : "–", uwMetres.HasValue ? uwMetres.Value.ToString("0.0", CultureInfo.InvariantCulture) + " " + SwimLang.T("μ.") : "–", "–", Ice);
            foreach (SwimTechCycle c in cycles.Take(8))
                row(c.Number.ToString(CultureInfo.InvariantCulture), Seconds2(c.Tempo) + " s",
                    c.Distance.HasValue ? c.Distance.Value.ToString("0.00", CultureInfo.InvariantCulture) + " " + SwimLang.T("μ.") : "–",
                    c.Glide.HasValue ? Seconds2(c.Glide) + " s" : "–", GlideColor(SwimTechRun.Classify(c.Glide)));

            // Bars: metres per stroke (when known) and glides (breaststroke, butterfly) with the line of a full glide.
            float bottom = r.Bottom - 30;
            RectangleF bars = new RectangleF(r.Left + 32, Math.Max(y + 30, bottom - 190), r.Width - 64, Math.Min(170, bottom - Math.Max(y + 30, bottom - 190)));
            if (bars.Height < 60 || cycles.Count == 0)
                return;

            // Two charts side by side: the metres per stroke (else the tempo) and the glides (else the tempo).
            bool glide = cycles.Any(c => c.Glide.HasValue);
            bool dist = cycles.Any(c => c.Distance.HasValue);
            RectangleF left = new RectangleF(bars.Left, bars.Top, bars.Width / 2 - 16, bars.Height);
            RectangleF right = new RectangleF(bars.Left + bars.Width / 2 + 16, bars.Top, bars.Width / 2 - 16, bars.Height);
            if (dist)
                CardBars(g, left, SwimLang.T("ΜΕΤΡΑ ΑΝΑ ΧΕΡΙΑ"), cycles.Select(c => c.Distance).ToList(), c => Amb, a, null);
            else
                CardBars(g, left, SwimLang.T("ΡΥΘΜΟΣ ΑΝΑ ΚΥΚΛΟ (s)"), cycles.Select(c => (double?)c.Tempo).ToList(), v => C, a, null);
            if (glide)
                CardBars(g, right, SwimLang.T("ΓΛΙΣΤΡΗΜΑ ΑΝΑ ΧΕΡΙΑ (s)"), cycles.Select(c => c.Glide).ToList(),
                    v => GlideColor(SwimTechRun.Classify(v)), a, SwimTechRun.FullGlide);
            else if (dist)
                CardBars(g, right, SwimLang.T("ΡΥΘΜΟΣ ΑΝΑ ΚΥΚΛΟ (s)"), cycles.Select(c => (double?)c.Tempo).ToList(), v => C, a, null);
        }

        private static void CardBars(Graphics g, RectangleF r, string title, List<double?> values, Func<double?, Color> tone, float a, double? line)
        {
            Caps(g, title, Ui(11, true), A(Lbl, a), r.Left, r.Top, 0.16f);
            RectangleF plot = new RectangleF(r.Left, r.Top + 40, r.Width, r.Height - 64);
            List<double> known = values.Where(v => v.HasValue).Select(v => v.Value).ToList();
            if (known.Count == 0 || plot.Height < 20)
                return;

            double max = Math.Max(known.Max(), line ?? 0) * 1.15;
            int n = Math.Max(values.Count, 4);
            float w = plot.Width / n;
            for (int i = 0; i < values.Count; i++)
            {
                if (!values[i].HasValue)
                    continue;
                float h = (float)(values[i].Value / max * plot.Height);
                RectangleF bar = new RectangleF(plot.Left + i * w + w * 0.12f, plot.Bottom - h, w * 0.76f, h);
                Color c = tone(values[i]);
                using (SolidBrush fill = new SolidBrush(A(Color.FromArgb(185, c), a)))
                    g.FillRectangle(fill, bar);
                Text(g, values[i].Value.ToString("0.00", CultureInfo.InvariantCulture), Ui(12, true), A(Ice, a), bar.Left + bar.Width / 2, bar.Top - 18, 0);
                Text(g, SwimLang.T("Χ") + (i + 1).ToString(CultureInfo.InvariantCulture), Ui(10), A(Lbl, a), bar.Left + bar.Width / 2, plot.Bottom + 4, 0);
            }

            if (line.HasValue)
            {
                float y = plot.Bottom - (float)(line.Value / max * plot.Height);
                using (Pen dash = new Pen(A(Color.FromArgb(200, 234, 252, 255), a), 1.2f) { DashPattern = new float[] { 4, 3 } })
                    g.DrawLine(dash, plot.Left, y, plot.Right, y);
                Caps(g, string.Format(CultureInfo.InvariantCulture, SwimLang.T("ΠΛΗΡΕΣ {0} s"), line.Value.ToString("0.0", CultureInfo.InvariantCulture)), Ui(9, true), A(Lbl, a), plot.Right, y - 14, 0.12f, 1);
            }
        }
        #endregion
    }
}
