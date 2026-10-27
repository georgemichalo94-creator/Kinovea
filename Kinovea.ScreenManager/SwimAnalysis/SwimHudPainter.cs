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
    /// <summary>What the HUD of the race (2.9.1, as the demo) draws at one frame.</summary>
    public sealed class SwimHudScene
    {
        public SwimRace Race;
        public SwimComputed Computed;

        /// <summary>The technical model of the race (distance, speed, strokes, phases), null before the start signal.</summary>
        public SwimReel Reel;
        public SwimTiming Timing;

        /// <summary>Video timestamp of the frame, race time (s after the start; NaN without a start signal).</summary>
        public long Timestamp;
        public double Time = double.NaN;

        /// <summary>The frame in the video: its number, its time (s), the frames per second and the size of the video.</summary>
        public long FrameNumber;
        public double VideoSeconds;
        public double Fps;
        public Size VideoSize;

        /// <summary>Point of the video image (reference pixels) → point of the drawing; null without the swimmer.</summary>
        public Func<PointF, PointF> FromImage;

        /// <summary>The boards that can be added (the panel's «Στο βίντεο»): Δ and ΔΑΚ, the velocity trace, the Δ chart, the final.</summary>
        public bool DeltaInfo = true;
        public bool SpeedTrace = true;
        public bool DeltaChart;
        public SwimFinal Final;
        public string BoardName = "";

        /// <summary>«Η ανάλυση με μια ματιά» after the finish.</summary>
        public bool Summary = true;

        /// <summary>
        /// (2.9.5) Race time of the last frame of the video (its working zone, as exported), null when not known: a video that ends soon
        /// after the touch shows «Η ανάλυση με μια ματιά» in its last seconds.
        /// </summary>
        public double? VideoEnd;

        /// <summary>The state of the analysis shown on the panel: technical analysis, the Google Sheet, the PDF, the calibration.</summary>
        public bool Technical;
        public SwimSyncState Sync = SwimSyncState.NotConfigured;
        public bool PdfReady;
        public bool Calibrated;

        /// <summary>
        /// The values of the start or the turn of the technical analysis at the frame (the panel on its technical tab), in the place of
        /// the map of the pool: the title of the phase, its clock and the values (label, value). Null: none.
        /// </summary>
        public string TechTitle;
        public string TechClock;
        public List<KeyValuePair<string, string>> TechItems;

        /// <summary>The technical view («Τεχνική» of the bar): the strokes, the rate and the speed beside the swimmer.</summary>
        public bool SwimmerLabel;
    }

    /// <summary>
    /// The HUD of the race on the video, as the demo of 2.9.0 (since 2.9.1): the frame of the HUD with its top bar; on the left the panel of
    /// the analysis (event, race time, phase, progress, next checkpoint, distance, strokes of the length, the tabs of the phases, the speed
    /// and stroke rate gauges with the underwater phase, the speed along the race); on the right the checkpoints and the log of the race;
    /// below the map of the pool; on the video the lock-on of the swimmer, the start, the call-outs of the breakouts, checkpoints and
    /// turns, the final time; after the finish «Η ανάλυση με μια ματιά». Designed for 1920 × 1080 and drawn at the scale of the image;
    /// the boards of before can be added (Δ and ΔΑΚ, the Δ chart, the board of the final). The panels fade where the swimmer is.
    /// </summary>
    public static partial class SwimHudPainter
    {
        /// <summary>The version of the swim analysis shown on the panel.</summary>
        public const string AppVersion = "2.9.9";

        // The colours of the demo.
        private static readonly Color C = Color.FromArgb(56, 225, 255);
        private static readonly Color C2 = Color.FromArgb(143, 243, 255);
        private static readonly Color C3 = Color.FromArgb(201, 251, 255);
        private static readonly Color Ice = Color.FromArgb(234, 252, 255);
        private static readonly Color Amb = Color.FromArgb(255, 190, 92);
        private static readonly Color Lbl = Color.FromArgb(184, 143, 243, 255);

        private const float DesignW = 1920f, DesignH = 1080f;

        /// <summary>
        /// The HUD is drawn on images of at least 900 × 500 that are not narrower than 5:4 (its text readable, the video between its
        /// panels); else the boards of before.
        /// </summary>
        public static bool CanPaint(Size size)
        {
            return size.Width >= 900 && size.Height >= 500 && size.Width >= 1.25f * size.Height;
        }

        /// <summary>Seconds of the call-outs, of the final time before «Η ανάλυση με μια ματιά», of the board of the final.</summary>
        private const double CalloutSeconds = 2.0;
        private const double CalloutMin = 1.2;

        /// <summary>How far around the swimmer the panels are see-through (design units).</summary>
        private const float HoleReach = 70f;
        private const double FinishSeconds = 5.0;
        private const double BoardSeconds = 4.5;
        private const double FinishBoardSeconds = 10.0;

        #region Layout
        private sealed class Layout
        {
            public float K, W, H;
            public RectangleF Left, Splits, Log, Map, Video;
            public float CenterX;
            public RectangleF Swimmer;
        }

        private static Layout MakeLayout(Size size)
        {
            Layout L = new Layout();
            L.K = Math.Min(size.Width / DesignW, size.Height / DesignH);
            L.W = size.Width / L.K;
            L.H = size.Height / L.K;
            // Its contents are laid out on 900 units (taller images: the space below stays free for the video).
            L.Left = new RectangleF(36, 76, 524, Math.Min(L.H - 180, 900));
            float right = L.W - 408;
            float avail = L.H - 196;
            float splits = (float)Math.Round(avail * 0.634);
            L.Splits = new RectangleF(right, 76, 372, splits);
            L.Log = new RectangleF(right, 76 + splits + 16, 372, avail - splits);
            L.Map = new RectangleF(600, L.H - 238, Math.Max(0, L.W - 432 - 600), 134);
            L.Video = RectangleF.FromLTRB(L.Left.Right, 76, right, L.H - 104);
            L.CenterX = (L.Left.Right + right) / 2;
            return L;
        }
        #endregion

        #region Text and shapes
        /// <summary>Fonts in the pixels of the design (as the demo's CSS): labels, numbers, the log.</summary>
        private static Font Ui(float px, bool bold = false)
        {
            return SwimTheme.Ui(Round(px * 0.75f), bold ? FontStyle.Bold : FontStyle.Regular);
        }

        private static Font Thin(float px)
        {
            return SwimTheme.DisplayLight(Round(px * 0.75f));
        }

        private static Font Mono(float px)
        {
            return SwimTheme.Mono(Round(px * 0.75f));
        }

        private static float Round(float pt)
        {
            return (float)(Math.Round(pt * 4) / 4);
        }

        private static Color A(Color c, float alpha)
        {
            return SwimTheme.Fade(c, alpha);
        }

        private static Color A(int a, Color c)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c);
        }

        #region Outlines of the texts
        /// <summary>The outline of a text, its width (advance) and height.</summary>
        private sealed class Shape
        {
            public GraphicsPath Path;
            public float Width;
        }

        /// <summary>The outlines made (UI thread): the labels and most of the values are the same from frame to frame.</summary>
        [ThreadStatic]
        private static Dictionary<string, Shape> shapes;

        private const int ShapesKept = 700;

        /// <summary>
        /// The outline of a text at the origin: spaced capitals ('tracking' of the size of the font between the letters, as
        /// SwimTheme.Tracked draws them), or with NaN as DrawString lays it out at a point. Kept for the next frames.
        /// </summary>
        private static Shape ShapeOf(Graphics g, string text, Font font, float tracking)
        {
            if (shapes == null)
                shapes = new Dictionary<string, Shape>();

            float dpi = g.DpiY;
            string key = font.Name + "|" + font.SizeInPoints.ToString("0.###", CultureInfo.InvariantCulture) + "|" + (int)font.Style + "|" +
                (float.IsNaN(tracking) ? "-" : tracking.ToString("0.###", CultureInfo.InvariantCulture)) + "|" +
                dpi.ToString("0.#", CultureInfo.InvariantCulture) + "|" + text;
            Shape shape;
            if (shapes.TryGetValue(key, out shape))
                return shape;

            if (shapes.Count >= ShapesKept)
            {
                foreach (Shape old in shapes.Values)
                    old.Path.Dispose();
                shapes.Clear();
            }

            float em = font.SizeInPoints * dpi / 72f;
            shape = new Shape { Path = new GraphicsPath() };
            if (float.IsNaN(tracking))
            {
                using (StringFormat format = new StringFormat(StringFormat.GenericDefault))
                    shape.Path.AddString(text, font.FontFamily, (int)font.Style, em, PointF.Empty, format);
                shape.Width = g.MeasureString(text, font).Width;
            }
            else
            {
                using (StringFormat format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces })
                {
                    float x = 0;
                    foreach (char ch in text)
                    {
                        string letter = ch.ToString();
                        if (!char.IsWhiteSpace(ch))
                            shape.Path.AddString(letter, font.FontFamily, (int)font.Style, em, new PointF(x, 0), format);
                        x += g.MeasureString(letter, font, PointF.Empty, format).Width + tracking * em;
                    }

                    shape.Width = Math.Max(0, x - tracking * em);
                }
            }

            shapes[key] = shape;
            return shape;
        }

        /// <summary>An outline filled at a point.</summary>
        private static void Fill(Graphics g, Shape shape, Color color, float x, float y)
        {
            if (color.A == 0 || shape.Path.PointCount == 0)
                return;

            g.TranslateTransform(x, y);
            using (SolidBrush brush = new SolidBrush(color))
                g.FillPath(brush, shape.Path);
            g.TranslateTransform(-x, -y);
        }
        #endregion

        /// <summary>Spaced capitals from a point (align 1: their right end at x; -1: centred on x); returns their width.</summary>
        private static float Caps(Graphics g, string text, Font font, Color color, float x, float y, float tracking = 0.2f, int align = 0)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            Shape shape = ShapeOf(g, text, font, tracking);
            float x0 = align > 0 ? x - shape.Width : (align == 0 ? x : x - shape.Width / 2);
            Fill(g, shape, color, x0, y);
            return shape.Width;
        }

        /// <summary>A text from a point (align 0: centred on x, 1: its right end at x), as DrawString places it.</summary>
        private static void Text(Graphics g, string text, Font font, Color color, float x, float y, int align = -1)
        {
            if (string.IsNullOrEmpty(text))
                return;

            Shape shape = ShapeOf(g, text, font, float.NaN);
            if (align >= 0)
                x -= align == 0 ? shape.Width / 2 : shape.Width;
            Fill(g, shape, color, x, y);
        }

        private static void TextIn(Graphics g, string text, Font font, Color color, RectangleF r, int align = -1)
        {
            if (string.IsNullOrEmpty(text) || r.Width < 4)
                return;

            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center })
            {
                format.Alignment = align < 0 ? StringAlignment.Near : (align == 0 ? StringAlignment.Center : StringAlignment.Far);
                g.DrawString(text, font, brush, r, format);
            }
        }

        /// <summary>
        /// Glowing figures from a point (the left of the text; its right at x when align 1, its centre when 0), as SwimTheme.GlowText
        /// draws them. strength: of the glow (0: none).
        /// </summary>
        private static void Glow(Graphics g, string text, Font font, Color color, float x, float y, int align = -1, float strength = 1f, Color? glow = null)
        {
            if (string.IsNullOrEmpty(text) || color.A == 0)
                return;

            Shape shape = ShapeOf(g, text, font, float.NaN);
            if (align >= 0)
                x -= align == 0 ? shape.Width / 2 : shape.Width;

            if (strength > 0.01f && shape.Path.PointCount > 0)
            {
                float em = font.SizeInPoints * g.DpiY / 72f;
                Color tone = glow ?? C;
                g.TranslateTransform(x, y);
                // One soft pen (stroking the outlines is the cost of the glow); small text has none.
                if (em >= 15)
                {
                    using (Pen soft = new Pen(SwimTheme.Fade(Color.FromArgb(em > 44 ? 48 : 56, tone), strength), Math.Max(2f, em * (em > 44 ? 0.13f : 0.16f))) { LineJoin = LineJoin.Round })
                        g.DrawPath(soft, shape.Path);
                }

                g.TranslateTransform(-x, -y);
            }

            Fill(g, shape, color, x, y);
        }

        private static float Width(Graphics g, string text, Font font)
        {
            return string.IsNullOrEmpty(text) ? 0 : ShapeOf(g, text, font, float.NaN).Width;
        }

        /// <summary>A panel of the demo: see-through navy glass, scan lines, a thin border, glowing corners, its title and a note.</summary>
        private static void Panel(Graphics g, RectangleF r, string title, string sub, float alpha = 1f, float titleSize = 14)
        {
            if (r.Width < 20 || r.Height < 20)
                return;

            using (LinearGradientBrush back = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                A(Color.FromArgb(158, 4, 30, 48), alpha), A(Color.FromArgb(179, 2, 16, 28), alpha), LinearGradientMode.Vertical))
                g.FillRectangle(back, r);
            ScanLines(g, RectangleF.Inflate(r, -1, -1), A(Color.FromArgb(9, 143, 243, 255), alpha));

            using (Pen glow = new Pen(A(Color.FromArgb(22, 56, 225, 255), alpha), 8f))
                g.DrawRectangle(glow, r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8);
            using (Pen border = new Pen(A(Color.FromArgb(64, 56, 225, 255), alpha), 1f))
                g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height);

            Corners(g, r, 26, 2f, alpha);
            if (!string.IsNullOrEmpty(title))
                Caps(g, title, Ui(titleSize, true), A(C2, alpha), r.Left + 18, r.Top + 11, 0.32f);

            if (!string.IsNullOrEmpty(sub))
                Caps(g, sub, Mono(11), A(Color.FromArgb(140, 143, 243, 255), alpha), r.Right - 18, r.Top + 14, 0.12f, 1);
        }

        /// <summary>The see-through scan lines of the panels: every 4 pixels of the image, from a small texture kept.</summary>
        private static void ScanLines(Graphics g, RectangleF r, Color color)
        {
            if (color.A == 0 || r.Width <= 0 || r.Height <= 0)
                return;

            if (scanTextures == null)
                scanTextures = new Dictionary<int, Bitmap>();
            Bitmap texture;
            if (!scanTextures.TryGetValue(color.ToArgb(), out texture))
            {
                if (scanTextures.Count > 64)
                {
                    foreach (Bitmap old in scanTextures.Values)
                        old.Dispose();
                    scanTextures.Clear();
                }

                texture = new Bitmap(4, 4, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                for (int x = 0; x < 4; x++)
                    texture.SetPixel(x, 3, color);
                scanTextures[color.ToArgb()] = texture;
            }

            // One texel per pixel of the image, whatever the scale of the drawing.
            float k;
            using (Matrix m = g.Transform)
                k = Math.Max(0.05f, (float)Math.Sqrt(m.Elements[0] * m.Elements[0] + m.Elements[1] * m.Elements[1]));
            using (TextureBrush brush = new TextureBrush(texture, WrapMode.Tile))
            {
                brush.ScaleTransform(1 / k, 1 / k);
                g.FillRectangle(brush, r);
            }
        }

        [ThreadStatic]
        private static Dictionary<int, Bitmap> scanTextures;

        /// <summary>Glowing corner brackets of length l around a rectangle.</summary>
        private static void Corners(Graphics g, RectangleF r, float l, float width, float alpha = 1f)
        {
            using (Pen glow = new Pen(A(Color.FromArgb(80, 56, 225, 255), alpha), width * 3.5f))
            using (Pen pen = new Pen(A(C, alpha), width))
            {
                foreach (Pen p in new[] { glow, pen })
                {
                    g.DrawLines(p, new[] { new PointF(r.Left, r.Top + l), new PointF(r.Left, r.Top), new PointF(r.Left + l, r.Top) });
                    g.DrawLines(p, new[] { new PointF(r.Right - l, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Top + l) });
                    g.DrawLines(p, new[] { new PointF(r.Left, r.Bottom - l), new PointF(r.Left, r.Bottom), new PointF(r.Left + l, r.Bottom) });
                    g.DrawLines(p, new[] { new PointF(r.Right - l, r.Bottom), new PointF(r.Right, r.Bottom), new PointF(r.Right, r.Bottom - l) });
                }
            }
        }

        /// <summary>A chip of the demo (a lit one filled); returns its width.</summary>
        private static float Chip(Graphics g, float x, float y, string text, bool on, float alpha = 1f)
        {
            Font f = Ui(12, true);
            float w = ShapeOf(g, text, f, 0.16f).Width + 20, h = 26;
            RectangleF r = new RectangleF(x, y, w, h);
            using (SolidBrush back = new SolidBrush(A(on ? Color.FromArgb(56, 56, 225, 255) : Color.FromArgb(15, 56, 225, 255), alpha)))
                g.FillRectangle(back, r);
            if (on)
            {
                using (Pen glow = new Pen(A(Color.FromArgb(70, 56, 225, 255), alpha), 5f))
                    g.DrawRectangle(glow, r.X, r.Y, r.Width, r.Height);
            }

            using (Pen pen = new Pen(A(Color.FromArgb(140, 56, 225, 255), alpha), 1f))
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            Caps(g, text, f, A(on ? Color.White : C2, alpha), x + 10, y + 6, 0.16f);
            return w;
        }

        /// <summary>An arc of a circle: angles in degrees, 0 = up, clockwise (as the demo's SVG).</summary>
        private static void Arc(Graphics g, Pen pen, float cx, float cy, float r, float a0, float a1)
        {
            float sweep = a1 - a0;
            if (Math.Abs(sweep) < 0.05f)
                return;
            g.DrawArc(pen, cx - r, cy - r, 2 * r, 2 * r, a0 - 90, sweep);
        }

        private static PointF Polar(float cx, float cy, float r, float degrees)
        {
            double a = degrees * Math.PI / 180;
            return new PointF(cx + (float)(r * Math.Sin(a)), cy - (float)(r * Math.Cos(a)));
        }

        private static float Clamp(double x, double a, double b)
        {
            return (float)Math.Max(a, Math.Min(b, x));
        }

        private static float Ease(double t)
        {
            t = Clamp(t, 0, 1);
            return (float)(t * t * (3 - 2 * t));
        }

        private static float EaseOut(double t)
        {
            t = Clamp(t, 0, 1);
            return (float)(1 - Math.Pow(1 - t, 3));
        }

        /// <summary>In over fi s from a, out over fo s before b (0–1).</summary>
        private static float Window(double t, double a, double b, double fi, double fo)
        {
            return Clamp(Math.Min((t - a) / fi, (b - t) / fo), 0, 1);
        }
        #endregion

        #region The race at the frame
        /// <summary>The values of the race at the frame (from the marks and the model of the race).</summary>
        private sealed class State
        {
            public double T;
            public bool Started, Finished;

            /// <summary>Before the entry into the water (on the block, in the air): no distance, speed or underwater yet.</summary>
            public bool Early;
            public double Final;
            public double D;
            public string Unit;
            public string Phase, Phase2;
            public int Tab;
            public List<SwimRow> Rows;
            public SwimRow Next, Last;
        }

        private static State Read(SwimHudScene s)
        {
            State st = new State();
            st.T = s.Time;
            st.Unit = s.Race.IsYards ? "yd" : SwimLang.T("μ.");
            st.Rows = s.Computed.Rows.OrderBy(r => r.D).ToList();
            SwimReel reel = s.Reel;
            st.Started = reel != null && !double.IsNaN(st.T) && st.T >= 0;
            st.Finished = reel != null && reel.Finish.HasValue && st.T >= reel.Finish.Value - reel.FrameSlack - 1e-6;
            st.Final = reel != null ? (reel.FinalTime ?? reel.Finish ?? double.NaN) : double.NaN;
            st.Early = st.Started && !st.Finished && (reel.Entry.HasValue ? st.T < reel.Entry.Value : st.T < 1.0);
            st.D = st.Started && !st.Early && reel.HasModel ? reel.DistanceAt(st.T) : 0;
            if (st.Finished)
                st.D = s.Race.Distance;
            double t = double.IsNaN(st.T) ? double.NegativeInfinity : st.T;
            st.Next = st.Rows.FirstOrDefault(r => !r.PassedAt(t));
            st.Last = st.Rows.Where(r => r.PassedAt(t) && r.T.HasValue).OrderBy(r => r.OnVideo).LastOrDefault();
            Phase(s, st);
            return st;
        }

        /// <summary>The phase of the race as the demo shows it, its detail and its tab (1 start, 2 swim, 3 turn, 4 finish).</summary>
        private static void Phase(SwimHudScene s, State st)
        {
            SwimReel reel = s.Reel;
            if (!st.Started)
            {
                st.Phase = SwimLang.T("ΑΝΑΜΟΝΗ");
                st.Phase2 = SwimLang.T("σήμα εκκίνησης");
                st.Tab = 1;
                return;
            }

            if (st.Finished)
            {
                st.Phase = SwimLang.T("ΤΕΛΟΣ");
                st.Phase2 = SwimLang.T("τελικός χρόνος");
                st.Tab = 4;
                return;
            }

            double t = st.T;
            int lap = reel.LapAt(t);
            double breakout;
            bool hasBreakout = TryBreakoutTime(s, lap, out breakout);
            if (lap == 0 && (!hasBreakout || t < breakout) && st.D < 15)
            {
                st.Phase = SwimLang.T("ΕΚΚΙΝΗΣΗ");
                st.Phase2 = reel.Entry.HasValue && t < reel.Entry.Value ? SwimLang.T("βουτιά") : SwimLang.T("υποβρύχια φάση");
                st.Tab = 1;
                return;
            }

            double pool = s.Race.Pool;
            double distance = s.Race.Distance;
            if (st.D >= distance - 5)
            {
                st.Phase = SwimLang.T("ΤΕΡΜΑΤΙΣΜΟΣ");
                st.Phase2 = string.Format(CultureInfo.InvariantCulture, SwimLang.T("τελευταία 5{0}"), st.Unit);
                st.Tab = 4;
                return;
            }

            double intoLength = st.D - Math.Floor(st.D / pool + 1e-9) * pool;
            if (lap > 0 && (!hasBreakout || t < breakout) && intoLength < 15)
            {
                st.Phase = SwimLang.T("ΣΤΡΟΦΗ");
                st.Phase2 = SwimLang.T("υποβρύχια φάση");
                st.Tab = 3;
                return;
            }

            if (pool - intoLength <= 5 && st.D < distance - 5)
            {
                st.Phase = SwimLang.T("ΣΤΡΟΦΗ");
                st.Phase2 = string.Format(CultureInfo.InvariantCulture, SwimLang.T("είσοδος 5{0}"), st.Unit);
                st.Tab = 3;
                return;
            }

            st.Phase = SwimLang.T("ΚΟΛΥΜΒΗΣΗ");
            st.Phase2 = string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}ο μήκος"), lap + 1);
            st.Tab = 2;
        }

        /// <summary>The time of the breakout of a length (s after the start): marked, else none.</summary>
        private static bool TryBreakoutTime(SwimHudScene s, int lap, out double time)
        {
            time = double.NaN;
            List<double> breakouts = s.Computed.Breakouts;
            if (breakouts == null || breakouts.Count == 0 || s.Reel == null)
                return false;

            foreach (double b in breakouts.OrderBy(x => x))
            {
                if (s.Reel.LapAt(b) == lap)
                {
                    time = b;
                    return true;
                }
            }

            return false;
        }

        private static string Clock(double t)
        {
            if (double.IsNaN(t))
                return "—";
            if (t < 0)
                return "−" + Math.Abs(t).ToString("0.00", CultureInfo.InvariantCulture);
            return SwimReference.FormatTime(t);
        }

        /// <summary>A number of strokes as the panel writes it: "64", "64,5" (Greek), "64.5" (English).</summary>
        private static string StrokeNumber(double strokes)
        {
            return SwimLang.Decimals((Math.Round(strokes * 2, MidpointRounding.AwayFromZero) / 2).ToString("0.#", CultureInfo.InvariantCulture));
        }

        /// <summary>Strokes in capitals: "9 ΧΕΡΙΕΣ", "9,5 ΧΕΡΙΕΣ", "1 ΧΕΡΙΑ".</summary>
        private static string StrokesCaps(string number)
        {
            return number == "1" ? SwimLang.T("1 ΧΕΡΙΑ") : string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} ΧΕΡΙΕΣ"), number);
        }

        /// <summary>Strokes at the end of a line of the log: " · 9 χεριές", " · 1 χεριά".</summary>
        private static string StrokesLog(string number)
        {
            return number == "1" ? SwimLang.T(" · 1 χεριά") : string.Format(CultureInfo.InvariantCulture, SwimLang.T(" · {0} χεριές"), number);
        }

        /// <summary>The event in capitals: "100Μ. ΕΛΕΥΘΕΡΟ", "4x100Μ. ΜΙΚΤΗ", "200YD ΥΠΤΙΟ".</summary>
        private static string EventText(SwimRace race)
        {
            return race.DistanceLabel + (race.IsYards ? "YD " : SwimLang.T("Μ. ")) + SwimLang.Upper(SwimPresets.StyleName(race.Style));
        }

        private static string Units(SwimRace race, double d)
        {
            return d.ToString("0", CultureInfo.InvariantCulture) + (race.IsYards ? "yd" : SwimLang.T("μ."));
        }
        #endregion

        public static void Paint(Graphics g, Size size, SwimHudScene s)
        {
            if (s == null || s.Race == null || s.Computed == null || s.Race.IsTraining || !CanPaint(size))
                return;

            Layout L = MakeLayout(size);
            State st = Read(s);
            GraphicsState saved = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.ScaleTransform(L.K, L.K);
                if (s.FromImage != null)
                {
                    RectangleF box = SwimHud.SwimmerBox(s.Race, s.Timestamp, s.FromImage);
                    if (!box.IsEmpty)
                        L.Swimmer = new RectangleF(box.X / L.K, box.Y / L.K, box.Width / L.K, box.Height / L.K);
                }

                // After the finish: the final time, then «Η ανάλυση με μια ματιά» over everything.
                double dashAt = st.Finished ? st.T - DashStart(s) : -1;
                float dash = s.Summary && st.Finished ? Ease(dashAt / 0.6) : 0f;
                if (dash < 0.999f)
                {
                    Timed("frame", () => DrawFrame(g, s, L));
                    Timed("video", () => DrawVideoLayer(g, s, st, L));
                    Timed("left", () => Fade(g, L, L.Left, layer => DrawLeft(layer, s, st, L)));
                    Timed("splits", () => Fade(g, L, L.Splits, layer => DrawSplits(layer, s, st, L)));
                    Timed("log", () =>
                    {
                        if (!DrawFinalsBoard(g, s, st, L))
                            Fade(g, L, L.Log, layer => DrawLog(layer, s, st, L));
                    });
                    Timed("bottom", () => DrawBottom(g, s, st, L));
                }

                if (dash > 0.001f)
                    Timed("dashboard", () => DrawDashboard(g, s, st, L, dash, dashAt));
            }
            finally
            {
                g.Restore(saved);
            }
        }

        /// <summary>
        /// Race time when «Η ανάλυση με μια ματιά» comes: after the final time (and the board of the final); sooner when the video ends
        /// before it could be read, but not before 2 s after the touch (2.9.5).
        /// </summary>
        private static double DashStart(SwimHudScene s)
        {
            double finish = s.Reel.Finish.Value;
            double start = finish + FinishSeconds + (s.Final != null ? FinishBoardSeconds - FinishSeconds : 0);
            if (s.VideoEnd.HasValue && s.VideoEnd.Value < start + DashStill)
                start = Math.Max(finish + 2.0, Math.Min(start, s.VideoEnd.Value - DashStill));
            return start;
        }

        /// <summary>For the CI preview: the milliseconds of each part of the HUD are added up here when it is not null.</summary>
        public static Dictionary<string, double> Timings;

        private static void Timed(string name, Action draw)
        {
            if (Timings == null)
            {
                draw();
                return;
            }

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            draw();
            double ms;
            Timings.TryGetValue(name, out ms);
            Timings[name] = ms + watch.Elapsed.TotalMilliseconds;
        }

        /// <summary>A part of the HUD as it is, with a soft see-through hole where the swimmer passes under it (he stays in front).</summary>
        private static void Fade(Graphics g, Layout L, RectangleF place, Action<Graphics> draw)
        {
            SwimHud.LayerAround(g, place, L.Swimmer, HoleReach, layer =>
            {
                if (!ReferenceEquals(layer, g))
                    layer.TextRenderingHint = TextRenderingHint.AntiAlias;
                draw(layer);
            });
        }

        #region The frame of the HUD
        private static void DrawFrame(Graphics g, SwimHudScene s, Layout L)
        {
            // Corners of the frame.
            float I = 12, l = 40;
            using (Pen glow = new Pen(Color.FromArgb(70, 56, 225, 255), 7f))
            using (Pen pen = new Pen(Color.FromArgb(190, 56, 225, 255), 2f))
            {
                foreach (Pen p in new[] { glow, pen })
                {
                    g.DrawLines(p, new[] { new PointF(I, I + l), new PointF(I, I), new PointF(I + l, I) });
                    g.DrawLines(p, new[] { new PointF(L.W - I - l, I), new PointF(L.W - I, I), new PointF(L.W - I, I + l) });
                    g.DrawLines(p, new[] { new PointF(I, L.H - I - l), new PointF(I, L.H - I), new PointF(I + l, L.H - I) });
                    g.DrawLines(p, new[] { new PointF(L.W - I - l, L.H - I), new PointF(L.W - I, L.H - I), new PointF(L.W - I, L.H - I - l) });
                }
            }

            // The ruler under the top bar.
            float x0 = 600, x1 = L.W - 432;
            if (x1 - x0 > 100)
            {
                using (Pen tick = new Pen(Color.FromArgb(115, 56, 225, 255), 1f))
                using (GraphicsPath ticks = new GraphicsPath())
                {
                    for (int i = 0; i <= 100; i++)
                    {
                        float x = x0 + i * (x1 - x0) / 100f;
                        ticks.StartFigure();
                        ticks.AddLine(x, 66, x, i % 10 == 0 ? 74 : 70);
                    }

                    g.DrawPath(tick, ticks);
                }
            }

            // Top bar: the analysis is live, the frame, the federation.
            float pulse = (float)(0.35 + 0.65 * (0.5 + 0.5 * Math.Sin(s.VideoSeconds * 7)));
            using (SolidBrush halo = new SolidBrush(Color.FromArgb((int)(80 * pulse), C)))
                g.FillEllipse(halo, 36, 32, 15, 15);
            using (SolidBrush dot = new SolidBrush(A((int)(255 * pulse), C)))
                g.FillEllipse(dot, 39, 35, 9, 9);
            Caps(g, SwimLang.T("ΑΝΑΛΥΣΗ ΣΕ ΕΞΕΛΙΞΗ"), Ui(13), Lbl, 58, 33, 0.2f);
            if (s.Fps > 0)
            {
                string frame = string.Format(CultureInfo.InvariantCulture, SwimLang.T("ΚΑΡΕ {0:000000} · {1:0.##} FPS · {2}×{3} · {4:0.00} s"),
                    Math.Max(0, s.FrameNumber), s.Fps, s.VideoSize.Width, s.VideoSize.Height, s.VideoSeconds);
                Caps(g, frame, Mono(13), Color.FromArgb(178, 143, 243, 255), x0, 34, 0.14f);
            }
            Caps(g, SwimLang.T("Κ.Ο.Ε. · ΑΝΑΛΥΣΗ ΑΓΩΝΩΝ · KINOVEA SWIM"), Ui(13), Lbl, L.W - 40, 33, 0.2f, 1);
        }
        #endregion

        #region Left: the panel of the analysis
        private static void DrawLeft(Graphics g, SwimHudScene s, State st, Layout L)
        {
            RectangleF r = L.Left;
            Panel(g, r, null, null);
            float x = r.Left, y = r.Top;

            // Logo, name, version.
            DrawLogo(g, x + 16 + 35, y + 14 + 35, 1f, (float)s.VideoSeconds);
            Glow(g, "SWIM ANALYTICS", Thin(30), Color.White, x + 96, y + 16, -1, 0.9f);
            Caps(g, "K.O.E. · KINOVEA · " + AppVersion, Ui(11), Lbl, x + 100, y + 60, 0.2f);

            // The language and the analysis.
            float cx = x + 18;
            cx += Chip(g, cx, y + 100, "ΕΛ", !SwimLang.English) + 8;
            cx += Chip(g, cx, y + 100, "EN", SwimLang.English) + 22;
            cx += Chip(g, cx, y + 100, "RACE ANALYSIS", !s.Technical) + 8;
            Chip(g, cx, y + 100, SwimLang.T("ΤΕΧΝΙΚΗ ΑΝΑΛΥΣΗ"), s.Technical);

            // The event: distance and style, the round, the pool, the lane.
            RectangleF ev = new RectangleF(x + 18, y + 150, 488, 58);
            using (Pen line = new Pen(Color.FromArgb(64, 56, 225, 255), 1f))
            {
                g.DrawLine(line, ev.Left, ev.Top, ev.Right, ev.Top);
                g.DrawLine(line, ev.Left, ev.Bottom, ev.Right, ev.Bottom);
            }

            string eventText = EventText(s.Race);
            List<KeyValuePair<string, bool>> chips = new List<KeyValuePair<string, bool>>();
            if (!string.IsNullOrWhiteSpace(s.Race.Round))
                chips.Add(new KeyValuePair<string, bool>(SwimLang.Upper(SwimLang.Label(s.Race.Round.Trim())), false));
            chips.Add(new KeyValuePair<string, bool>(Units(s.Race, s.Race.Pool), false));
            if (s.Race.Lane > 0)
                chips.Add(new KeyValuePair<string, bool>(SwimLang.T("ΔΙΑΔΡ. ") + s.Race.Lane.ToString(CultureInfo.InvariantCulture), true));
            float chipsWidth = chips.Sum(c => ShapeOf(g, c.Key, Ui(12, true), 0.16f).Width + 28);
            float chipX = ev.Right - chipsWidth + 8;
            foreach (KeyValuePair<string, bool> c in chips)
                chipX += Chip(g, chipX, ev.Top + 16, c.Key, c.Value) + 8;
            Font eventFont = Thin(30);
            float roomEvent = Math.Max(60, ev.Right - chipsWidth - 12 - ev.Left);
            while (eventFont.Size > 10 && Width(g, eventText, eventFont) > roomEvent)
                eventFont = SwimTheme.DisplayLight(eventFont.Size - 1.5f);
            Text(g, eventText, eventFont, Color.White, ev.Left, ev.Top + 11);

            // The race time and the phase.
            Caps(g, SwimLang.T("ΧΡΟΝΟΣ ΑΓΩΝΑ"), Ui(13), Lbl, x + 20, y + 226, 0.2f);
            double clockTime = st.Finished ? st.Final : st.T;
            string clock = Clock(clockTime);
            Font clockFont = Thin(112);
            while (clockFont.Size > 20 && Width(g, clock, clockFont) > 340)
                clockFont = SwimTheme.DisplayLight(clockFont.Size - 4);
            Color clockColor = !st.Started ? Color.FromArgb(255, 217, 160) : Color.White;
            Glow(g, clock, clockFont, clockColor, x + 8, y + 244, -1, st.Finished ? 1.3f : 1f, !st.Started ? Amb : C);

            Caps(g, SwimLang.T("ΦΑΣΗ"), Ui(13), Lbl, x + 352, y + 226, 0.2f);
            Glow(g, st.Phase, Ui(18, true), C2, x + 350, y + 246, -1, 0.5f);
            Text(g, st.Phase2, Ui(13), Color.FromArgb(178, 201, 251, 255), x + 352, y + 276);
            if (s.DeltaInfo)
                DrawDelta(g, s, st, x + 352, y + 302);

            // Progress of the race, with its checkpoints.
            DrawProgress(g, s, st, new RectangleF(x + 18, y + 378, 488, 22));

            // Next checkpoint, distance, strokes of the length.
            SwimReel reel = s.Reel;
            string next = st.Next != null ? Units(s.Race, st.Next.D) : "—";
            // Between the checkpoints the distance is from the curve through them (≈).
            string dist = st.Started && !st.Early ? (st.Finished ? "" : "≈") + st.D.ToString("0.0", CultureInfo.InvariantCulture) : "—";
            string strokes = st.Started && reel != null ? reel.StrokesInLength(Math.Min(st.T, reel.Finish ?? double.MaxValue)).ToString(CultureInfo.InvariantCulture) : "—";
            string[] names = { SwimLang.T("ΕΠΟΜΕΝΟ"), SwimLang.T("ΑΠΟΣΤΑΣΗ"), SwimLang.T("ΧΕΡΙΕΣ ΜΗΚΟΥΣ") };
            string[] values = { next, dist, strokes };
            for (int i = 0; i < 3; i++)
            {
                RectangleF tile = new RectangleF(x + 18 + i * 166, y + 410, 156, 84);
                using (SolidBrush back = new SolidBrush(Color.FromArgb(10, 56, 225, 255)))
                    g.FillRectangle(back, tile);
                using (Pen pen = new Pen(Color.FromArgb(56, 56, 225, 255), 1f))
                    g.DrawRectangle(pen, tile.X, tile.Y, tile.Width, tile.Height);
                Caps(g, names[i], Ui(10), Lbl, tile.Left + 12, tile.Top + 10, 0.2f);
                Glow(g, values[i], Thin(36), Ice, tile.Left + 8, tile.Top + 28, -1, 0.8f);
            }

            // The tabs of the phases, the current one lit.
            DrawTabs(g, st, new RectangleF(x + 18, y + 508, 488, 36));

            // Gauges: speed, the underwater phase, stroke rate.
            DrawGauges(g, s, st, new PointF(x + 18, y + 556));

            // The speed along the race.
            if (s.SpeedTrace && r.Bottom - (y + 786) >= 60)
                DrawTrace(g, s, st, new RectangleF(x + 18, y + 786, 488, Math.Min(96, r.Bottom - (y + 786) - 14)));
        }

        /// <summary>The emblem: two arcs that turn, a dashed ring, three waves.</summary>
        private static void DrawLogo(Graphics g, float cx, float cy, float scale, float seconds)
        {
            using (Pen ring = new Pen(Color.FromArgb(90, 56, 225, 255), 1f))
                g.DrawEllipse(ring, cx - 30 * scale, cy - 30 * scale, 60 * scale, 60 * scale);
            float turn = seconds * 90 % 360;
            using (Pen glow = new Pen(Color.FromArgb(80, 56, 225, 255), 8f * scale))
            using (Pen arc = new Pen(C, 3f * scale))
            {
                foreach (Pen p in new[] { glow, arc })
                {
                    Arc(g, p, cx, cy, 26 * scale, turn, turn + 110);
                    Arc(g, p, cx, cy, 26 * scale, turn + 180, turn + 290);
                }
            }

            using (Pen dashed = new Pen(Color.FromArgb(204, 143, 243, 255), 1.5f * scale) { DashPattern = new float[] { 2, 2.6f } })
            {
                GraphicsState state = g.Save();
                g.TranslateTransform(cx, cy);
                g.RotateTransform(-seconds * 50 % 360);
                g.DrawEllipse(dashed, -18 * scale, -18 * scale, 36 * scale, 36 * scale);
                g.Restore(state);
            }

            using (Pen wave = new Pen(C3, 1.6f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                for (int i = -1; i <= 1; i++)
                {
                    float yy = cy + i * 6 * scale;
                    PointF[] pts = new PointF[13];
                    for (int k = 0; k <= 12; k++)
                    {
                        float xx = cx - 11 * scale + k * 22f * scale / 12;
                        pts[k] = new PointF(xx, yy - (float)Math.Sin(k * Math.PI / 4) * 1.6f * scale);
                    }

                    g.DrawCurve(wave, pts);
                }
            }
        }

        /// <summary>The Δ with the reference at the last checkpoint, the ΔΑΚ of the last 50 (the boards of before).</summary>
        private static void DrawDelta(Graphics g, SwimHudScene s, State st, float x, float y)
        {
            SwimRow last = st.Rows.Where(r => r.Delta.HasValue && r.PassedAt(double.IsNaN(st.T) ? double.NegativeInfinity : st.T)).OrderBy(r => r.OnVideo).LastOrDefault();
            if (last != null && s.Computed.Reference != null)
            {
                string delta = SwimReference.FormatDelta(last.Delta.Value);
                Font f = Ui(15, true);
                float w = Width(g, delta, f) + 14;
                RectangleF pill = new RectangleF(x, y, w, 26);
                bool faster = last.Delta.Value <= 0;
                Color tone = faster ? SwimTheme.Green : SwimTheme.Red;
                using (SolidBrush fill = new SolidBrush(faster ? SwimFinalsPainter.FasterColor : SwimFinalsPainter.SlowerColor))
                    g.FillRectangle(fill, pill);
                using (Pen glow = new Pen(Color.FromArgb(70, tone), 4f))
                    g.DrawRectangle(glow, pill.X, pill.Y, pill.Width, pill.Height);
                Text(g, delta, f, Color.White, pill.Left + w / 2, pill.Top + 2, 0);
                TextIn(g, "vs " + (s.Computed.Reference.Name ?? ""), Ui(11), C2, new RectangleF(pill.Right + 6, pill.Top, 150 - w, pill.Height));
                y += 32;
            }

            double t = double.IsNaN(st.T) ? double.NegativeInfinity : st.T;
            SwimBlock block = s.Computed.Blocks.Where(b => b.Dak.HasValue && st.Rows.Any(r => r.D == b.To && r.PassedAt(t))).OrderBy(b => b.To).LastOrDefault();
            if (block != null)
            {
                string dak = string.Format(CultureInfo.InvariantCulture, SwimLang.T("ΔΑΚ {0}-{1}  {2:0.00}"), block.From, block.To, block.Dak.Value);
                Font f = Ui(12, true);
                float w = Width(g, dak, f) + 12;
                RectangleF chip = new RectangleF(x, y, w, 22);
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(150, 70, 48, 16)))
                    g.FillRectangle(fill, chip);
                using (Pen pen = new Pen(Color.FromArgb(220, Amb), 1f))
                    g.DrawRectangle(pen, chip.X, chip.Y, chip.Width, chip.Height);
                Text(g, dak, f, Amb, chip.Left + 6, chip.Top + 3);
            }
        }

        private static void DrawProgress(Graphics g, SwimHudScene s, State st, RectangleF r)
        {
            float cy = r.Top + 11;
            float k = r.Width / Math.Max(1, s.Race.Distance);
            using (Pen track = new Pen(Color.FromArgb(64, 56, 225, 255), 3f))
                g.DrawLine(track, r.Left, cy, r.Right, cy);
            float fill = (float)Math.Max(0, Math.Min(s.Race.Distance, st.D)) * k;
            if (fill > 0.5f)
                SwimTheme.GlowLine(g, C, 3f, new PointF(r.Left, cy), new PointF(r.Left + fill, cy));
            using (Pen tick = new Pen(Color.FromArgb(140, 143, 243, 255), 1f))
            {
                foreach (SwimRow row in st.Rows)
                    g.DrawLine(tick, r.Left + row.D * k, r.Top + 5, r.Left + row.D * k, r.Top + 17);
            }
        }

        private static void DrawTabs(Graphics g, State st, RectangleF r)
        {
            string[] names = { SwimLang.T("ΣΤΟΙΧΕΙΑ"), SwimLang.T("1 ΕΚΚΙΝΗΣΗ"), SwimLang.T("2 ΚΟΛΥΜΒΗΣΗ"), SwimLang.T("3 ΣΤΡΟΦΕΣ"), SwimLang.T("4 ΤΕΡΜΑΤΙΣΜΟΣ") };
            float[] widths = { 74, 96, 106, 94, 118 };
            using (Pen line = new Pen(Color.FromArgb(64, 56, 225, 255), 1f))
                g.DrawLine(line, r.Left, r.Bottom, r.Right, r.Bottom);
            float x = r.Left;
            for (int i = 0; i < names.Length; i++)
            {
                bool on = i == st.Tab;
                RectangleF tab = new RectangleF(x, r.Top, widths[i] * r.Width / 488f, 34);
                Font f = Ui(11, true);
                if (on)
                    Glow(g, names[i], f, Color.White, tab.Left + tab.Width / 2, tab.Top + 10, 0, 0.6f);
                else
                    Text(g, names[i], f, Color.FromArgb(140, 143, 243, 255), tab.Left + tab.Width / 2, tab.Top + 10, 0);
                if (on)
                {
                    using (SolidBrush glow = new SolidBrush(Color.FromArgb(80, C)))
                        g.FillRectangle(glow, tab.Left, tab.Bottom - 3, tab.Width, 7);
                    using (SolidBrush ul = new SolidBrush(C))
                        g.FillRectangle(ul, tab.Left, tab.Bottom - 1, tab.Width, 3);
                }

                x = tab.Right;
            }
        }

        private static void DrawGauges(Graphics g, SwimHudScene s, State st, PointF o)
        {
            SwimReel reel = s.Reel;
            double t = st.T;
            double? speed = null, rate = null;
            if (st.Finished)
            {
                speed = s.Computed.AvgVel;
                rate = s.Computed.AvgSr;
            }
            else if (st.Started && !st.Early && reel != null && reel.HasModel)
            {
                speed = Math.Max(0, reel.SpeedAt(t));
                SwimReelCycle cycle = reel.LastCycle(t);
                if (cycle != null && cycle.Tempo > 0.2 && t - cycle.Time <= 2.2 * cycle.Tempo && reel.Swimming(t))
                    rate = 60.0 / cycle.Tempo;
            }

            float spin = (float)s.VideoSeconds;
            // The speed during the race from the curve through the checkpoints (≈), the mean speed of the race after the finish.
            string speedText = speed.HasValue ? (st.Finished ? "" : "≈") + speed.Value.ToString("0.00", CultureInfo.InvariantCulture) : (st.Started ? "—" : "0.00");
            Gauge(g, o.X + 92, o.Y + 92, speed, 3.0, speedText,
                "m/s", SwimLang.T("ΤΑΧΥΤΗΤΑ"), spin * 40, C);
            Gauge(g, o.X + 396, o.Y + 92, rate, 70, rate.HasValue ? rate.Value.ToString("0.0", CultureInfo.InvariantCulture) : "—",
                SwimLang.T("κύκλοι/λ"), SwimLang.T("ΣΥΧΝΟΤΗΤΑ"), -spin * 55, C);

            // The underwater phase of the start or of the turn of this length.
            RectangleF box = new RectangleF(o.X + 188, o.Y + 46, 112, 92);
            using (SolidBrush back = new SolidBrush(Color.FromArgb(10, 56, 225, 255)))
                g.FillRectangle(back, box);
            using (Pen pen = new Pen(Color.FromArgb(56, 56, 225, 255), 1f))
                g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
            Caps(g, SwimLang.T("ΥΠΟΒΡΥΧΙΑ"), Ui(10), Lbl, box.Left + box.Width / 2, box.Top + 12, 0.2f, -1);
            string value, sub;
            Underwater(s, st, out value, out sub);
            Glow(g, value, Thin(26), Color.White, box.Left + box.Width / 2, box.Top + 34, 0, 0.6f);
            TextIn(g, sub, Ui(11), Color.FromArgb(178, 201, 251, 255), new RectangleF(box.Left + 2, box.Top + 68, box.Width - 4, 18), 0);
        }

        private static void Underwater(SwimHudScene s, State st, out string value, out string sub)
        {
            value = "—";
            sub = "";
            SwimReel reel = s.Reel;
            if (!st.Started || st.Early || reel == null)
                return;

            double t = Math.Min(st.T, reel.Finish ?? double.MaxValue);
            int lap = reel.LapAt(t);
            double breakout;
            bool known = TryBreakoutTime(s, lap, out breakout);
            string where = lap == 0 ? SwimLang.T("εκκίνηση") : SwimLang.T("στροφή");
            double bd;
            if (known && t >= breakout && reel.BreakoutDistances.TryGetValue(lap, out bd))
            {
                value = (reel.BreakoutMeasured.Contains(lap) ? "" : "≈") + bd.ToString("0.0", CultureInfo.InvariantCulture) + (s.Race.IsYards ? "yd" : SwimLang.T("μ."));
                double? under = UnderwaterTime(s, lap);
                sub = under.HasValue ? where + " · " + under.Value.ToString("0.00", CultureInfo.InvariantCulture) + " s" : where;
                return;
            }

            if (!known || t < breakout)
            {
                double wall = Math.Floor(st.D / Math.Max(1, s.Race.Pool) + 1e-9) * s.Race.Pool;
                double into = lap == 0 ? st.D : st.D - wall;
                if (into >= 0 && into < 16 && (lap == 0 || !reel.Swimming(t)))
                {
                    value = "≈" + into.ToString("0.0", CultureInfo.InvariantCulture) + (s.Race.IsYards ? "yd" : SwimLang.T("μ."));
                    sub = where + SwimLang.T(" · τώρα");
                    return;
                }
            }

            // Swimming: the breakout of this length, when known.
            if (reel.BreakoutDistances.TryGetValue(lap, out bd))
            {
                value = (reel.BreakoutMeasured.Contains(lap) ? "" : "≈") + bd.ToString("0.0", CultureInfo.InvariantCulture) + (s.Race.IsYards ? "yd" : SwimLang.T("μ."));
                sub = where;
            }
        }

        /// <summary>
        /// The time under the water of a length as the analysis computes it: the start from the signal to the breakout, a turn from
        /// the feet off the wall (else the touch) to the breakout. Null when not marked.
        /// </summary>
        private static double? UnderwaterTime(SwimHudScene s, int lap)
        {
            if (lap == 0)
                return s.Computed.StartUnderwaterTime;

            int wall = lap * Math.Max(1, s.Race.Pool);
            SwimTurnComputed turn = s.Computed.Turns.FirstOrDefault(x => x.Wall == wall);
            return turn != null ? turn.UnderwaterTime : null;
        }

        /// <summary>A gauge of the demo: ticks over 270°, the value as a glowing arc, a turning dashed ring, the value in the middle.</summary>
        private static void Gauge(Graphics g, float cx, float cy, double? value, double max, string text, string unit, string label, float spin, Color color)
        {
            using (SolidBrush face = new SolidBrush(Color.FromArgb(8, 56, 225, 255)))
                g.FillEllipse(face, cx - 80, cy - 80, 160, 160);
            using (Pen ring = new Pen(Color.FromArgb(46, 56, 225, 255), 1f))
                g.DrawEllipse(ring, cx - 80, cy - 80, 160, 160);
            using (Pen tick = new Pen(Color.FromArgb(115, 143, 243, 255), 1f))
            using (GraphicsPath ticks = new GraphicsPath())
            {
                for (int i = 0; i < 48; i++)
                {
                    float a = -135 + i * (270f / 47);
                    ticks.StartFigure();
                    ticks.AddLine(Polar(cx, cy, i % 4 == 0 ? 70 : 73, a), Polar(cx, cy, 77, a));
                }

                g.DrawPath(tick, ticks);
            }

            using (Pen track = new Pen(Color.FromArgb(41, 56, 225, 255), 9f))
                Arc(g, track, cx, cy, 62, -135, 135);
            float k = value.HasValue ? Clamp(value.Value / max, 0.002, 1) : 0;
            if (k > 0.003f)
            {
                using (Pen glow = new Pen(Color.FromArgb(70, color), 16f))
                using (Pen arc = new Pen(color, 9f))
                {
                    Arc(g, glow, cx, cy, 62, -135, -135 + 270 * k);
                    Arc(g, arc, cx, cy, 62, -135, -135 + 270 * k);
                }
            }

            using (Pen dashed = new Pen(Color.FromArgb(115, 143, 243, 255), 1f) { DashPattern = new float[] { 2, 6 } })
            {
                GraphicsState state = g.Save();
                g.TranslateTransform(cx, cy);
                g.RotateTransform(spin % 360);
                g.DrawEllipse(dashed, -48, -48, 96, 96);
                g.Restore(state);
            }

            Glow(g, text, Thin(34), Color.White, cx, cy - 26, 0, 0.9f);
            Caps(g, unit, Ui(11), Color.FromArgb(191, 143, 243, 255), cx, cy + 22, 0.15f, -1);
            Caps(g, label, Ui(11, true), Color.FromArgb(191, 143, 243, 255), cx, cy + 92, 0.25f, -1);
        }

        /// <summary>The speed along the race up to now (from the model through the checkpoints), the reference dashed.</summary>
        private static void DrawTrace(Graphics g, SwimHudScene s, State st, RectangleF r)
        {
            Caps(g, SwimLang.T("ΤΑΧΥΤΗΤΑ ΑΝΑ ΑΠΟΣΤΑΣΗ (m/s)"), Ui(11), Lbl, r.Left + 2, r.Top - 18, 0.2f);
            SwimReel reel = s.Reel;
            List<double> all = s.Computed.Rows.Where(x => x.Vel.HasValue).Select(x => x.Vel.Value).Concat(s.Computed.ReferenceSegments.Select(x => x.V)).Where(v => v > 0.3 && v < 4).ToList();
            double lo = all.Count > 0 ? Math.Floor((all.Min() - 0.1) * 5) / 5 : 1.4;
            double hi = all.Count > 0 ? Math.Ceiling((all.Max() + 0.1) * 5) / 5 : 2.6;
            if (hi - lo < 0.6)
                hi = lo + 0.6;
            Func<double, float> X = d => r.Left + (float)(d / Math.Max(1, s.Race.Distance) * r.Width);
            Func<double, float> Y = v => r.Bottom - 4 - (float)((Math.Max(lo, Math.Min(hi, v)) - lo) / (hi - lo) * (r.Height - 8));
            using (Pen grid = new Pen(Color.FromArgb(31, 56, 225, 255), 1f))
            {
                double step = hi - lo > 1.2 ? 0.4 : 0.2;
                for (double v = Math.Ceiling(lo / step) * step; v <= hi + 1e-9; v += step)
                {
                    g.DrawLine(grid, r.Left, Y(v), r.Right, Y(v));
                    Text(g, v.ToString("0.0", CultureInfo.InvariantCulture), Ui(9), Color.FromArgb(115, 143, 243, 255), r.Right - 2, Y(v) - 14, 1);
                }
            }

            using (Pen wall = new Pen(Color.FromArgb(64, 143, 243, 255), 1f) { DashPattern = new float[] { 3, 3 } })
            {
                for (int w = s.Race.Pool; w < s.Race.Distance; w += s.Race.Pool)
                    g.DrawLine(wall, X(w), r.Top, X(w), r.Bottom);
            }

            // The reference: its segments, dashed.
            if (s.Computed.Reference != null && s.Computed.ReferenceSegments.Count > 0)
            {
                using (Pen dash = new Pen(Color.FromArgb(150, 143, 243, 255), 1.4f) { DashPattern = new float[] { 4, 3 } })
                {
                    foreach (SwimSegment seg in s.Computed.ReferenceSegments)
                        g.DrawLine(dash, X(seg.D0), Y(seg.V), X(seg.D1), Y(seg.V));
                }
            }

            if (!st.Started || reel == null || !reel.HasModel || st.D <= 0.5)
                return;

            List<PointF> pts = new List<PointF>();
            double stepD = Math.Max(0.5, s.Race.Distance / 160.0);
            for (double d = Math.Min(2, st.D); d <= st.D + 1e-6; d += stepD)
            {
                double t = reel.TimeAtDistance(d);
                double v = reel.SpeedAt(Math.Max(0.05, t));
                pts.Add(new PointF(X(d), Y(v)));
            }

            if (pts.Count < 2)
                return;

            using (GraphicsPath area = new GraphicsPath())
            {
                area.AddLines(pts.ToArray());
                area.AddLine(pts[pts.Count - 1], new PointF(pts[pts.Count - 1].X, r.Bottom));
                area.AddLine(new PointF(pts[pts.Count - 1].X, r.Bottom), new PointF(pts[0].X, r.Bottom));
                area.CloseFigure();
                using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(r.Left, r.Top - 1, r.Width, r.Height + 2),
                    Color.FromArgb(90, 56, 225, 255), Color.FromArgb(0, 56, 225, 255), LinearGradientMode.Vertical))
                    g.FillPath(fill, area);
            }

            using (Pen glow = new Pen(Color.FromArgb(70, 56, 225, 255), 6f) { LineJoin = LineJoin.Round })
            using (Pen line = new Pen(C2, 2f) { LineJoin = LineJoin.Round })
            {
                g.DrawLines(glow, pts.ToArray());
                g.DrawLines(line, pts.ToArray());
            }

            PointF end = pts[pts.Count - 1];
            using (SolidBrush halo = new SolidBrush(Color.FromArgb(90, C)))
                g.FillEllipse(halo, end.X - 7, end.Y - 7, 14, 14);
            using (SolidBrush dot = new SolidBrush(Color.White))
                g.FillEllipse(dot, end.X - 4, end.Y - 4, 8, 8);
        }
        #endregion

        #region Right: the checkpoints and the log
        private static void DrawSplits(Graphics g, SwimHudScene s, State st, Layout L)
        {
            RectangleF r = L.Splits;
            Panel(g, r, SwimLang.T("ΠΕΡΑΣΜΑΤΑ"), string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} ΣΗΜΕΙΑ"), st.Rows.Count));
            Font head = Ui(10);
            Caps(g, SwimLang.T("ΑΠΟΣΤ."), head, Lbl, r.Left + 22, r.Top + 46, 0.2f);
            Caps(g, SwimLang.T("ΧΡΟΝΟΣ"), head, Lbl, r.Left + 114, r.Top + 46, 0.2f);
            Caps(g, "M/S", head, Lbl, r.Left + 230, r.Top + 46, 0.2f);
            Caps(g, SwimLang.T("ΧΕΡ."), head, Lbl, r.Left + 312, r.Top + 46, 0.2f);

            // The rows that fit: around the next checkpoint in a long race.
            float rowH = 48;
            int fit = Math.Max(1, (int)((r.Height - 66) / rowH));
            int next = st.Next != null ? st.Rows.IndexOf(st.Next) : st.Rows.Count;
            int first = Math.Max(0, Math.Min(st.Rows.Count - fit, next - fit + 2));
            double t = double.IsNaN(st.T) ? double.NegativeInfinity : st.T;
            for (int i = first; i < Math.Min(st.Rows.Count, first + fit); i++)
            {
                SwimRow row = st.Rows[i];
                RectangleF rr = new RectangleF(r.Left + 14, r.Top + 62 + (i - first) * rowH, r.Width - 28, 44);
                bool done = row.PassedAt(t) && row.T.HasValue;
                bool isNext = row == st.Next && st.Started;
                double age = done ? t - row.OnVideo : double.NaN;
                float hl = done ? Clamp(1 - age / 2.2, 0, 1) : 0;
                if (hl > 0.01f)
                {
                    using (LinearGradientBrush band = new LinearGradientBrush(new RectangleF(rr.X - 1, rr.Y, rr.Width + 2, rr.Height),
                        Color.FromArgb(0, 56, 225, 255), Color.FromArgb((int)(72 * hl), 56, 225, 255), LinearGradientMode.Horizontal))
                    {
                        ColorBlend blend = new ColorBlend(3)
                        {
                            Colors = new[] { Color.FromArgb(0, 56, 225, 255), Color.FromArgb((int)(72 * hl), 56, 225, 255), Color.FromArgb(0, 56, 225, 255) },
                            Positions = new[] { 0f, 0.5f, 1f }
                        };
                        band.InterpolationColors = blend;
                        g.FillRectangle(band, rr);
                    }
                }

                if (isNext)
                {
                    using (SolidBrush nb = new SolidBrush(Color.FromArgb(34, Amb)))
                        g.FillRectangle(nb, rr);
                    using (SolidBrush edge = new SolidBrush(Amb))
                        g.FillRectangle(edge, rr.Left, rr.Top + 6, 2, rr.Height - 12);
                }

                using (Pen line = new Pen(Color.FromArgb(31, 56, 225, 255), 1f))
                    g.DrawLine(line, rr.Left, rr.Bottom, rr.Right, rr.Bottom);
                Text(g, Units(s.Race, row.D), Ui(17), done ? C2 : (isNext ? Amb : Color.FromArgb(102, 143, 243, 255)), rr.Left + 8, rr.Top + 9);
                if (done)
                {
                    Color timeColor = hl > 0.05f ? SwimTheme.Mix(Ice, Color.FromArgb(255, 214, 150), hl) : Ice;
                    float slide = (1 - EaseOut(age / 0.3)) * 24;
                    Glow(g, SwimReference.FormatTime(row.T.Value), Thin(26), timeColor, rr.Left + 96 + slide, rr.Top + 5, -1, 0.5f);
                    double? v = s.Race.IsYards ? row.VelYd : row.Vel;
                    if (v.HasValue)
                        Text(g, v.Value.ToString("0.00", CultureInfo.InvariantCulture), Ui(17), Color.FromArgb(217, 201, 251, 255), rr.Left + 202, rr.Top + 9);
                    if (row.Strokes.HasValue)
                        Text(g, SwimLang.Decimals(row.Strokes.Value.ToString("0.#", CultureInfo.InvariantCulture)), Ui(17), Color.FromArgb(217, 201, 251, 255), rr.Left + 286, rr.Top + 9);
                    if (s.DeltaInfo && row.Delta.HasValue && s.Computed.Reference != null)
                    {
                        string delta = SwimReference.FormatDelta(row.Delta.Value);
                        Text(g, delta, Ui(10, true), row.Delta.Value <= 0 ? SwimTheme.Green : SwimTheme.Red, rr.Left + 8, rr.Top + 29);
                    }
                }
                else
                {
                    Text(g, "—", Thin(26), Color.FromArgb(89, 234, 252, 255), rr.Left + 96, rr.Top + 5);
                }
            }
        }

        /// <summary>The log of the race: what happened up to now, the newest bright, as on a console.</summary>
        private static void DrawLog(Graphics g, SwimHudScene s, State st, Layout L)
        {
            RectangleF r = L.Log;
            Panel(g, r, SwimLang.T("ΚΑΤΑΓΡΑΦΗ"), "AUTO");
            List<KeyValuePair<double, string>> lines = LogLines(s, st);
            double t = double.IsNaN(st.T) ? double.NegativeInfinity : st.T;
            List<KeyValuePair<double, string>> shown = lines.Where(l => l.Key <= t + 1e-6).ToList();
            int fit = Math.Max(1, (int)((r.Height - 54) / 29));
            List<KeyValuePair<double, string>> last = shown.Skip(Math.Max(0, shown.Count - fit)).ToList();
            Font f = Mono(13);
            for (int i = 0; i < last.Count; i++)
            {
                double age = double.IsInfinity(last[i].Key) || double.IsNaN(t) ? 99 : t - last[i].Key;
                // Whole from its frame (the analyst stops there): it slides in and is white for a moment.
                float slide = (1 - EaseOut(age / 0.25)) * 14;
                bool newest = i == last.Count - 1;
                Color color = age < 1.2 ? Color.White : Color.FromArgb(204, 201, 251, 255);
                if (!newest)
                    color = Color.FromArgb((int)(color.A * 0.62f), color);
                TextIn(g, last[i].Value, f, color, new RectangleF(r.Left + 18 + slide, r.Top + 44 + i * 29, r.Width - 30 - slide, 24));
            }
        }

        /// <summary>The lines of the log at their times (−∞: before the race).</summary>
        private static List<KeyValuePair<double, string>> LogLines(SwimHudScene s, State st)
        {
            List<KeyValuePair<double, string>> lines = new List<KeyValuePair<double, string>>();
            Action<double, string> add = (t, text) => lines.Add(new KeyValuePair<double, string>(t, "› " + text));
            double before = double.NegativeInfinity;
            add(before, string.Format(CultureInfo.InvariantCulture, SwimLang.T("Βίντεο {0:0.##} fps · {1}×{2}"), s.Fps, s.VideoSize.Width, s.VideoSize.Height));
            if (s.Calibrated)
                add(before, SwimLang.T("Βαθμονόμηση πισίνας ............ OK"));
            if (s.Race.Tracks != null && s.Race.Tracks.Count > 0 && s.Race.Lane > 0)
                add(before, string.Format(CultureInfo.InvariantCulture, SwimLang.T("Διάδρομος {0}: κλειδώθηκε"), s.Race.Lane));
            if (s.Reel == null)
                return lines;

            SwimReel reel = s.Reel;
            SwimComputed c = s.Computed;
            add(0, SwimLang.T("Σήμα εκκίνησης  0.00"));
            if (c.ReactionTime.HasValue)
                add(c.ReactionTime.Value, string.Format(CultureInfo.InvariantCulture, SwimLang.T("Αντίδραση  {0:0.00}"), c.ReactionTime.Value));
            if (c.BlockTime.HasValue)
                add(c.BlockTime.Value, string.Format(CultureInfo.InvariantCulture, SwimLang.T("Πόδια εκτός βατήρα  {0:0.00}"), c.BlockTime.Value));
            if (reel.Entry.HasValue)
                add(reel.Entry.Value, string.Format(CultureInfo.InvariantCulture, SwimLang.T("Είσοδος στο νερό  {0:0.00}"), reel.Entry.Value));

            string unit = s.Race.IsYards ? "yd" : SwimLang.T("μ.");
            foreach (double b in c.Breakouts.OrderBy(x => x))
            {
                int lap = reel.LapAt(b);
                double bd;
                string dist = reel.BreakoutDistances.TryGetValue(lap, out bd) ? (reel.BreakoutMeasured.Contains(lap) ? "" : "≈") + bd.ToString("0.0", CultureInfo.InvariantCulture) + " " + unit : "";
                add(b, lap == 0 ? SwimLang.T("Έξοδος από το νερό: ") + dist : SwimLang.T("Έξοδος στροφής: ") + dist);
            }

            if (c.Strokes.Count > 0)
                add(c.Strokes[0], string.Format(CultureInfo.InvariantCulture, SwimLang.T("1η χεριά  {0:0.00}"), c.Strokes[0]));

            // The strokes added up as the panel has them (halves at the checkpoints), rounded only when written.
            double total = 0;
            foreach (SwimRow row in st.Rows.Where(r => r.T.HasValue))
            {
                if (row.Strokes.HasValue)
                    total += row.Strokes.Value;
                string time = SwimReference.FormatTime(row.T.Value);
                string text;
                if (row.D >= s.Race.Distance)
                    text = string.Format(CultureInfo.InvariantCulture, SwimLang.T("ΤΕΡΜΑΤΙΣΜΟΣ  {0}"), time) +
                        (total > 0 ? StrokesLog(StrokeNumber(total)) : "");
                else if (row.D % Math.Max(1, s.Race.Pool) == 0)
                    text = string.Format(CultureInfo.InvariantCulture, SwimLang.T("Στροφή {0}  {1}"), Units(s.Race, row.D), time);
                else
                    text = Units(s.Race, row.D) + "  " + time + (row.Strokes.HasValue && row.Strokes.Value > 0
                        ? StrokesLog(SwimLang.Decimals(row.Strokes.Value.ToString("0.#", CultureInfo.InvariantCulture))) : "");
                add(row.OnVideo, text);
            }

            return lines.OrderBy(l => l.Key).ToList();
        }

        /// <summary>The board of the final at a checkpoint (added), in the place of the log for a few seconds. True when drawn.</summary>
        private static bool DrawFinalsBoard(Graphics g, SwimHudScene s, State st, Layout L)
        {
            if (s.Final == null || !st.Started)
                return false;

            double t = st.T;
            SwimRow row = st.Rows.Where(r => r.PassedAt(t) && r.T.HasValue && s.Final.Swimmers.Any(sw => sw.TimeAt(r.D).HasValue)).OrderBy(r => r.OnVideo).LastOrDefault();
            if (row == null)
                return false;

            bool finish = row.D >= s.Race.Distance;
            if (t - row.OnVideo > (finish ? FinishBoardSeconds : BoardSeconds))
                return false;

            List<SwimFinalsBoardRow> rows = SwimFinalsCompare.Board(s.Final, row.D, row.T.Value, s.BoardName);
            if (rows == null)
                return false;

            RectangleF area = L.Log;
            float scale = Math.Min(area.Width / 260f, area.Height / SwimFinalsPainter.BoardHeight(rows.Count, 1f));
            if (scale < 0.6f)
                return false;

            int height = SwimFinalsPainter.BoardHeight(rows.Count, scale);
            Rectangle place = Rectangle.Round(new RectangleF(area.Left, area.Top, Math.Min(area.Width, 260 * scale), height));
            SwimHud.LayerAround(g, place, L.Swimmer, HoleReach, layer =>
                SwimFinalsPainter.PaintBoard(layer, place, SwimFinalsPainter.BoardTitle(s.Final, row.D, finish), SwimFinalsPainter.BoardSubtitle(s.Final), rows, scale));
            return true;
        }
        #endregion

        #region Bottom: the map of the pool (and the Δ chart)
        private static void DrawBottom(Graphics g, SwimHudScene s, State st, Layout L)
        {
            RectangleF area = L.Map;
            if (area.Width < 280)
                return;

            RectangleF map = area;
            if (s.DeltaChart && s.Computed.Reference != null && area.Width >= 620)
            {
                float w = (float)Math.Round(area.Width * 0.56f);
                map = new RectangleF(area.Left, area.Top, w, area.Height);
                RectangleF chart = RectangleF.FromLTRB(map.Right + 16, area.Top, area.Right, area.Bottom);
                double t = double.IsNaN(st.T) ? double.NegativeInfinity : st.T;
                List<SwimDeltaPoint> points = st.Rows.Where(r => r.Delta.HasValue && r.PassedAt(t)).Select(r => new SwimDeltaPoint { D = r.D, Delta = r.Delta.Value }).ToList();
                Rectangle place = Rectangle.Round(chart);
                SwimHud.LayerAround(g, place, L.Swimmer, HoleReach, layer =>
                    SwimFinalsPainter.PaintDelta(layer, place, points, s.Race.Distance, s.Race.Pool, s.Computed.Reference.Name, true, 1.25f));
            }

            // The values of the start or the turn of the technical analysis, in the place of the map while they are shown.
            if (s.TechItems != null && s.TechItems.Count > 0)
                Fade(g, L, map, layer => DrawTech(layer, s, map));
            else
                Fade(g, L, map, layer => DrawMap(layer, s, st, map));
        }

        /// <summary>The values of the start or the turn (the technical analysis) as tiles, up to two rows.</summary>
        private static void DrawTech(Graphics g, SwimHudScene s, RectangleF r)
        {
            Panel(g, r, SwimLang.Upper(SwimLang.T("ΤΕΧΝΙΚΗ ΑΝΑΛΥΣΗ")) + " · " + SwimLang.Upper(s.TechTitle ?? ""), s.TechClock, 1f, 12);
            List<KeyValuePair<string, string>> items = s.TechItems;
            float inner = r.Width - 28;
            int cols = Math.Max(1, (int)((inner + 8) / 120));
            int rows = items.Count > cols ? 2 : 1;
            int shown = Math.Min(items.Count, cols * rows);

            // The last values when they do not all fit: the newest are the ones that matter at the frame.
            List<KeyValuePair<string, string>> list = items.Skip(items.Count - shown).ToList();
            float w = (inner - (cols - 1) * 8) / cols;
            float h = rows == 2 ? (r.Height - 46 - 6) / 2 : Math.Min(64, r.Height - 46);
            for (int i = 0; i < list.Count; i++)
            {
                RectangleF tile = new RectangleF(r.Left + 14 + (i % cols) * (w + 8), r.Top + 38 + (i / cols) * (h + 6), w, h);
                using (SolidBrush back = new SolidBrush(Color.FromArgb(14, 255, 190, 92)))
                    g.FillRectangle(back, tile);
                using (Pen pen = new Pen(Color.FromArgb(70, 255, 190, 92), 1f))
                    g.DrawRectangle(pen, tile.X, tile.Y, tile.Width, tile.Height);
                Font caption = Ui(9.5f, true);
                string label = SwimLang.Upper(list[i].Key);
                while (caption.Size > 5 && ShapeOf(g, label, caption, 0.14f).Width > tile.Width - 12)
                    caption = SwimTheme.Ui(caption.Size - 0.5f, FontStyle.Bold);
                Caps(g, label, caption, Color.FromArgb(220, 255, 214, 150), tile.Left + 6, tile.Top + 4, 0.14f);
                Font value = Thin(Math.Min(24, h - 18));
                Glow(g, list[i].Value, value, Ice, tile.Left + 4, tile.Bottom - value.GetHeight(g) - 1, -1, 0.6f, Amb);
            }
        }

        private static void DrawMap(Graphics g, SwimHudScene s, State st, RectangleF r)
        {
            int pool = Math.Max(1, s.Race.Pool);
            string unit = s.Race.IsYards ? "yd" : SwimLang.T("μ.");
            double d = st.D;
            double pos = PoolPosition(d, pool);
            bool back = ((int)Math.Floor(d / pool + 1e-9)) % 2 == 1;
            string where = st.Early ? "—" : (st.Started && !st.Finished ? "≈" : "") + d.ToString("0.0", CultureInfo.InvariantCulture);
            Panel(g, r, SwimLang.T("ΧΑΡΤΗΣ ΠΙΣΙΝΑΣ · ") + pool.ToString(CultureInfo.InvariantCulture) + unit,
                s.Race.Lane > 0 ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("ΔΙΑΔΡ. {0} · {1} {2} · {3}"), s.Race.Lane, where, unit, back ? "←" : "→") : "", 1f, 12);

            int lanes = Math.Max(8, Math.Min(10, s.Race.Lane));
            RectangleF p = new RectangleF(r.Left + 34, r.Top + 38, r.Width - 54, r.Height - 56);
            float laneH = p.Height / lanes;
            using (SolidBrush water = new SolidBrush(Color.FromArgb(13, 56, 225, 255)))
                g.FillRectangle(water, p);
            if (s.Race.Lane > 0)
            {
                using (SolidBrush mine = new SolidBrush(Color.FromArgb(41, 56, 225, 255)))
                    g.FillRectangle(mine, p.Left, p.Top + (s.Race.Lane - 1) * laneH, p.Width, laneH);
            }

            using (Pen rope = new Pen(Color.FromArgb(56, 143, 243, 255), 1f) { DashPattern = new float[] { 4, 4 } })
            {
                for (int i = 1; i < lanes; i++)
                    g.DrawLine(rope, p.Left, p.Top + i * laneH, p.Right, p.Top + i * laneH);
            }

            using (Pen border = new Pen(Color.FromArgb(140, 56, 225, 255), 1.2f))
                g.DrawRectangle(border, p.X, p.Y, p.Width, p.Height);
            Font small = Ui(8);
            for (int i = 0; i < lanes; i++)
                Text(g, (i + 1).ToString(CultureInfo.InvariantCulture), small, i + 1 == s.Race.Lane ? Color.White : Color.FromArgb(128, 143, 243, 255), p.Left - 10, p.Top + i * laneH + laneH / 2 - 6, 0);

            int[] marks = pool >= 50 ? new[] { 5, 15, 25, 35, 45 } : new[] { 5, 10, 15, 20 };
            using (Pen mark = new Pen(Color.FromArgb(72, 143, 243, 255), 1f) { DashPattern = new float[] { 6, 3 } })
            {
                foreach (int m in marks.Where(m => m < pool))
                {
                    float x = p.Left + m * p.Width / pool;
                    g.DrawLine(mark, x, p.Top, x, p.Bottom);
                    Text(g, m.ToString(CultureInfo.InvariantCulture) + unit, Ui(9), Color.FromArgb(153, 143, 243, 255), x, p.Bottom + 1, 0);
                }
            }

            if (!st.Started || s.Race.Lane <= 0)
                return;

            float cy = p.Top + (s.Race.Lane - 0.5f) * laneH;
            float px = p.Left + (float)(pos * p.Width / pool);

            // The trail of the last 3 s.
            SwimReel reel = s.Reel;
            if (reel != null && reel.HasModel)
            {
                List<PointF> trail = new List<PointF>();
                for (int k = 30; k >= 0; k--)
                {
                    double tk = st.T - k * 0.1;
                    if (tk < 0)
                        continue;
                    double dk = st.Finished ? Math.Min(s.Race.Distance, reel.DistanceAt(Math.Min(tk, reel.Finish.Value))) : reel.DistanceAt(tk);
                    trail.Add(new PointF(p.Left + (float)(PoolPosition(dk, pool) * p.Width / pool), cy));
                }

                if (trail.Count >= 2)
                {
                    using (Pen glow = new Pen(Color.FromArgb(80, C), 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    using (Pen line = new Pen(C, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLines(glow, trail.ToArray());
                        g.DrawLines(line, trail.ToArray());
                    }
                }
            }

            float halo = 8 + 3 * (float)Math.Sin(s.VideoSeconds * 6);
            using (SolidBrush h = new SolidBrush(Color.FromArgb(64, C)))
                g.FillEllipse(h, px - halo, cy - halo, 2 * halo, 2 * halo);
            using (SolidBrush dot = new SolidBrush(Color.White))
                g.FillEllipse(dot, px - 4.2f, cy - 4.2f, 8.4f, 8.4f);
        }

        /// <summary>Where along the pool (0 to the length) a distance of the race is: out and back.</summary>
        private static double PoolPosition(double d, int pool)
        {
            if (d <= 0)
                return 0;
            int length = (int)Math.Floor(d / pool - 1e-9);
            double into = d - length * pool;
            return length % 2 == 0 ? into : pool - into;
        }
        #endregion

        #region On the video: the swimmer, the start, the call-outs, the finish
        private static void DrawVideoLayer(Graphics g, SwimHudScene s, State st, Layout L)
        {
            SwimReel reel = s.Reel;
            float lane = 0;
            PointF swimmer = PointF.Empty;
            bool located = Locate(s, L, out swimmer, out lane);

            // Lock-on of the swimmer: corners that close in around the start signal, then four arcs that follow him (with the strokes,
            // the rate and the speed beside him in the technical view).
            if (located)
            {
                double t = st.T;
                if (!double.IsNaN(t) && t > -1.45 && t < 1.25)
                {
                    DrawReticle(g, s, st, swimmer, lane);
                }
                else if (!st.Finished)
                {
                    DrawLock(g, swimmer, lane);
                    if (s.SwimmerLabel)
                        DrawSwimmerLabel(g, s, st, swimmer, lane, L);
                }
            }

            // The start signal: rings from the swimmer, the time of the signal.
            if (st.Started && st.T < 1.0)
            {
                PointF at = located ? swimmer : new PointF(L.CenterX, L.H * 0.45f);
                for (int i = 0; i < 3; i++)
                {
                    float p = Clamp((st.T - i * 0.12) / 0.9, 0, 1);
                    if (p <= 0)
                        continue;
                    float radius = 20 + EaseOut(p) * (260 + i * 120);
                    using (Pen ring = new Pen(Color.FromArgb((int)(255 * (1 - p)), i == 0 ? Amb : C2), 3f))
                        g.DrawEllipse(ring, at.X - radius, at.Y - radius, 2 * radius, 2 * radius);
                }
            }

            if (st.Started && st.T < 1.6)
            {
                float a = Window(st.T, -0.08, 1.6, 0.08, 0.5);
                PointF at = located ? new PointF(swimmer.X + 110, swimmer.Y + 56) : new PointF(L.CenterX - 60, L.H * 0.45f + 60);
                Font sf = Ui(12, true);
                RectangleF chip = new RectangleF(at.X - 12, at.Y - 9, Math.Max(150, ShapeOf(g, SwimLang.T("ΣΗΜΑ ΕΚΚΙΝΗΣΗΣ"), sf, 0.2f).Width + 24), 78);
                using (SolidBrush back = new SolidBrush(A((int)(190 * a), Color.FromArgb(3, 22, 36))))
                    g.FillRectangle(back, chip);
                using (SolidBrush edge = new SolidBrush(A(Amb, a)))
                    g.FillRectangle(edge, chip.Left, chip.Top, 3, chip.Height);
                Caps(g, SwimLang.T("ΣΗΜΑ ΕΚΚΙΝΗΣΗΣ"), sf, A(Amb, a), at.X, at.Y, 0.2f);
                Glow(g, "0.00", Thin(40), A(Color.White, a), at.X - 4, at.Y + 16, -1, a, Amb);
            }

            // Call-outs of the breakouts, the checkpoints and the turns.
            if (st.Started && !st.Finished)
                DrawCallout(g, s, st, L);

            // The finish: the final time in the middle, rings and rays.
            if (st.Finished && reel != null && reel.Finish.HasValue)
                DrawFinish(g, s, st, L);

            // A scan line slowly over the video.
            if (st.Started && !st.Finished)
            {
                float y = (float)(((s.VideoSeconds * 140) % 1300) - 110);
                if (y > L.Video.Top && y < L.Video.Bottom)
                {
                    using (SolidBrush scan = new SolidBrush(Color.FromArgb(48, 143, 243, 255)))
                        g.FillRectangle(scan, L.Video.Left, y, L.Video.Width, 2);
                }
            }
        }

        /// <summary>Where the swimmer is (design units) and the width of his lane, from the part of the race followed there.</summary>
        private static bool Locate(SwimHudScene s, Layout L, out PointF at, out float lane)
        {
            at = PointF.Empty;
            lane = 0;
            if (s.FromImage == null)
                return false;

            SwimTrack track = s.Race.TrackAt(s.Timestamp);
            float x, y, top, bottom;
            if (track == null || !track.TryGet(s.Timestamp, 5, out x, out y, out top, out bottom))
                return false;

            PointF c = s.FromImage(new PointF(x, y));
            PointF a = s.FromImage(new PointF(x, top)), b = s.FromImage(new PointF(x, bottom));
            at = new PointF(c.X / L.K, c.Y / L.K);
            lane = Math.Abs(b.Y - a.Y) / L.K;
            return true;
        }

        private static void DrawReticle(Graphics g, SwimHudScene s, State st, PointF at, float lane)
        {
            double t = st.T;
            float k = EaseOut((t + 1.4) / 0.7);
            float w = 330 + (150 - 330) * k, h = 280 + (128 - 280) * k, l = 26;
            float a = Window(t, -1.45, 1.25, 0.35, 0.45);
            RectangleF box = new RectangleF(at.X - w / 2, at.Y - h / 2, w, h);
            Corners(g, box, l, 2.5f, a);
            float seconds = (float)s.VideoSeconds;
            using (Pen dashed = new Pen(A(Color.FromArgb(178, 143, 243, 255), a), 1.2f) { DashPattern = new float[] { 10 / 1.2f, 7 / 1.2f } })
            {
                GraphicsState state = g.Save();
                g.TranslateTransform(at.X, at.Y);
                g.RotateTransform(seconds * 60 % 360);
                g.DrawEllipse(dashed, -62, -62, 124, 124);
                g.Restore(state);
            }

            using (SolidBrush dot = new SolidBrush(A(Color.White, a)))
                g.FillEllipse(dot, at.X - 4, at.Y - 4, 8, 8);

            // The label of the lane: locked, then the start.
            float lx = box.Right + 70, ly = box.Top - 20;
            using (Pen lead = new Pen(A(Color.FromArgb(204, 143, 243, 255), a), 1.2f))
                g.DrawLine(lead, box.Right, box.Top, lx, ly);
            RectangleF label = new RectangleF(lx, ly - 34, 214, 64);
            using (SolidBrush back = new SolidBrush(A(Color.FromArgb(184, 3, 22, 36), a)))
                g.FillRectangle(back, label);
            using (Pen border = new Pen(A(Color.FromArgb(153, 56, 225, 255), a), 1f))
                g.DrawRectangle(border, label.X, label.Y, label.Width, label.Height);
            Caps(g, s.Race.Lane > 0 ? SwimLang.T("ΔΙΑΔΡΟΜΟΣ ") + s.Race.Lane.ToString(CultureInfo.InvariantCulture) : SwimLang.T("ΑΘΛΗΤΗΣ"), Ui(12, true), A(C2, a), label.Left + 12, label.Top + 7, 0.25f);
            string state2 = t < -0.85 ? SwimLang.T("ΑΝΙΧΝΕΥΣΗ…") : (t < 0 ? SwimLang.T("ΚΛΕΙΔΩΜΕΝΟΣ") : SwimLang.T("ΕΚΚΙΝΗΣΗ"));
            Text(g, state2, Thin(19), A(st.Started && t < 0.6 ? Amb : Color.White, a), label.Left + 10, label.Top + 23);
            string sub = SwimLang.Upper(SwimPresets.StyleName(s.Race.Style)) + " · " + Units(s.Race, s.Race.Distance) + SwimLang.T(" · ΑΥΤΟΜΑΤΑ");
            Caps(g, sub, Ui(10), A(Color.FromArgb(178, 201, 251, 255), a), label.Left + 12, label.Top + 47, 0.12f);
        }

        /// <summary>The swimmer followed during the race: four arcs around him, he stays clear inside.</summary>
        private static void DrawLock(Graphics g, PointF at, float lane)
        {
            float rx = Math.Max(30, 0.95f * lane), ry = Math.Max(14, 0.36f * lane);
            RectangleF ring = new RectangleF(at.X - rx, at.Y - ry, 2 * rx, 2 * ry);
            using (Pen glow = new Pen(Color.FromArgb(60, C), 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen arc = new Pen(Color.FromArgb(220, C), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                foreach (float start in new[] { 15f, 105f, 195f, 285f })
                {
                    g.DrawArc(glow, ring, start, 60);
                    g.DrawArc(arc, ring, start, 60);
                }
            }
        }

        /// <summary>
        /// Beside the swimmer (the technical view): his lane and the strokes of the length, the rate of the last cycle and the speed
        /// (≈: from the checkpoints).
        /// </summary>
        private static void DrawSwimmerLabel(Graphics g, SwimHudScene s, State st, PointF at, float lane, Layout L)
        {
            SwimReel reel = s.Reel;
            if (reel == null || !st.Started || st.Finished)
                return;

            double t = st.T;
            string head = (s.Race.Lane > 0 ? SwimLang.T("ΔΙΑΔΡ. ") + s.Race.Lane.ToString(CultureInfo.InvariantCulture) + " · " : "") +
                SwimLang.T("ΧΕΡΙΕΣ ") + reel.StrokesInLength(t).ToString(CultureInfo.InvariantCulture);
            List<string> values = new List<string>();
            SwimReelCycle cycle = reel.LastCycle(t);
            if (cycle != null && cycle.Tempo > 0.2 && t - cycle.Time <= 2.2 * cycle.Tempo && reel.Swimming(t))
                values.Add((60.0 / cycle.Tempo).ToString("0.0", CultureInfo.InvariantCulture) + " " + SwimLang.T("κ/λ"));
            if (reel.HasModel)
                values.Add("≈" + Math.Max(0, reel.SpeedAt(t)).ToString("0.00", CultureInfo.InvariantCulture) + " m/s");
            string value = string.Join("  ·  ", values);

            Font hf = Ui(11, true), vf = Thin(22);
            float w = Math.Max(ShapeOf(g, head, hf, 0.2f).Width, Width(g, value, vf)) + 24, h = value.Length > 0 ? 58 : 32;
            float rx = Math.Max(30, 0.95f * lane), ry = Math.Max(14, 0.36f * lane);
            float x = Math.Max(L.Video.Left + 6, Math.Min(L.Video.Right - w - 6, at.X + rx * 0.6f));
            float y = Math.Max(L.Video.Top + 6, at.Y - ry - h - 22);
            RectangleF box = new RectangleF(x, y, w, h);
            using (Pen lead = new Pen(Color.FromArgb(170, 143, 243, 255), 1.2f))
                g.DrawLine(lead, at.X + rx * 0.5f, at.Y - ry * 0.85f, Math.Max(box.Left, Math.Min(box.Right, at.X + rx * 0.6f + 10)), box.Bottom);
            using (SolidBrush back = new SolidBrush(Color.FromArgb(184, 3, 22, 36)))
                g.FillRectangle(back, box);
            using (Pen border = new Pen(Color.FromArgb(153, 56, 225, 255), 1f))
                g.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
            Corners(g, box, 8, 1.5f);
            Caps(g, head, hf, C2, box.Left + 12, box.Top + 8, 0.2f);
            if (value.Length > 0)
                Glow(g, value, vf, Color.White, box.Left + 8, box.Top + 24, -1, 0.6f);
        }

        /// <summary>The call-out of the last event (breakout, checkpoint, turn) for two seconds, at the top of the video.</summary>
        private static void DrawCallout(Graphics g, SwimHudScene s, State st, Layout L)
        {
            double t = st.T;
            List<Callout> list = Callouts(s, st);

            // One after the other: each one at least CalloutMin s, the next waits for it (a breakout just before the 15 m).
            double[] start = new double[list.Count];
            int k = -1;
            for (int i = 0; i < list.Count; i++)
            {
                start[i] = i == 0 ? list[i].Time : Math.Max(list[i].Time, start[i - 1] + CalloutMin);
                if (t >= start[i])
                    k = i;
            }

            if (k < 0)
                return;

            double end = start[k] + CalloutSeconds;
            if (k + 1 < list.Count)
                end = Math.Min(end, start[k + 1]);
            if (t >= end)
                return;

            Callout c = list[k];
            // Whole from its frame (the analyst stops there), out over the last 0.3 s.
            float a = Window(t, start[k] - 0.18, end, 0.18, 0.3);
            if (a <= 0.01f)
                return;

            float drop = (1 - EaseOut((t - start[k]) / 0.3)) * -14;
            float cx = L.CenterX, top = 92 + drop;
            Font kf = Ui(13, true), nf = Thin(54), uf = Ui(15);
            float w = Math.Max(ShapeOf(g, c.Key, kf, 0.34f).Width, Math.Max(Width(g, c.Number, nf), ShapeOf(g, c.Sub, uf, 0.16f).Width)) + 52;
            RectangleF back = new RectangleF(cx - w / 2, top, w, 104);

            // See-through over the swimmer: he stays in front.
            a *= SwimHud.Fade(back, L.Swimmer, 40f);
            using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(back.X - 1, back.Y, back.Width + 2, back.Height), Color.Transparent, Color.Transparent, LinearGradientMode.Horizontal))
            {
                ColorBlend blend = new ColorBlend(4)
                {
                    Colors = new[] { Color.FromArgb(0, 3, 22, 36), A((int)(199 * a), Color.FromArgb(3, 22, 36)), A((int)(199 * a), Color.FromArgb(3, 22, 36)), Color.FromArgb(0, 3, 22, 36) },
                    Positions = new[] { 0f, 0.18f, 0.82f, 1f }
                };
                fill.InterpolationColors = blend;
                g.FillRectangle(fill, back);
            }

            Caps(g, c.Key, kf, A(C2, a), cx, top + 10, 0.34f, -1);
            Glow(g, c.Number, nf, A(Color.White, a), cx, top + 24, 0, a);
            Caps(g, c.Sub, uf, A(Color.FromArgb(204, 201, 251, 255), a), cx, top + 82, 0.16f, -1);
        }

        private sealed class Callout
        {
            public double Time;
            public string Key, Number, Sub;
        }

        private static List<Callout> Callouts(SwimHudScene s, State st)
        {
            List<Callout> list = new List<Callout>();
            SwimReel reel = s.Reel;
            if (reel == null)
                return list;

            string unit = s.Race.IsYards ? "yd" : SwimLang.T("μ.");
            foreach (double b in s.Computed.Breakouts.OrderBy(x => x))
            {
                int lap = reel.LapAt(b);
                double bd;
                if (!reel.BreakoutDistances.TryGetValue(lap, out bd))
                    continue;
                double? under = UnderwaterTime(s, lap);
                string number = (reel.BreakoutMeasured.Contains(lap) ? "" : "≈") + bd.ToString("0.0", CultureInfo.InvariantCulture) + " " + unit;
                string sub = (lap == 0 ? SwimLang.T("ΥΠΟΒΡΥΧΙΑ ΦΑΣΗ") : SwimLang.T("ΥΠΟΒΡΥΧΙΑ ΣΤΡΟΦΗΣ")) +
                    (under.HasValue ? " · " + under.Value.ToString("0.00", CultureInfo.InvariantCulture) + " s" : "");
                list.Add(new Callout { Time = b, Key = lap == 0 ? SwimLang.T("ΕΞΟΔΟΣ ΑΠΟ ΤΟ ΝΕΡΟ") : SwimLang.T("ΕΞΟΔΟΣ ΣΤΡΟΦΗΣ"), Number = number, Sub = sub });
            }

            double lengthStrokes = 0;
            foreach (SwimRow row in st.Rows.Where(r => r.T.HasValue && r.D < s.Race.Distance))
            {
                bool wall = row.D % Math.Max(1, s.Race.Pool) == 0;
                if (row.Strokes.HasValue)
                    lengthStrokes += row.Strokes.Value;
                List<string> sub = new List<string>();
                double? v = s.Race.IsYards ? row.VelYd : row.Vel;
                if (wall)
                {
                    sub.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}ο {1}"), row.D / Math.Max(1, s.Race.Pool), Units(s.Race, s.Race.Pool)));
                    if (lengthStrokes > 0)
                        sub.Add(StrokesCaps(StrokeNumber(lengthStrokes)));
                    lengthStrokes = 0;
                }
                else
                {
                    if (v.HasValue)
                        sub.Add(v.Value.ToString("0.00", CultureInfo.InvariantCulture) + (s.Race.IsYards ? " yd/s" : " m/s"));
                    if (row.Strokes.HasValue && row.Strokes.Value > 0)
                        sub.Add(StrokesCaps(SwimLang.Decimals(row.Strokes.Value.ToString("0.#", CultureInfo.InvariantCulture))));
                }

                if (s.DeltaInfo && row.Delta.HasValue && s.Computed.Reference != null)
                    sub.Add("Δ " + SwimReference.FormatDelta(row.Delta.Value));
                list.Add(new Callout
                {
                    Time = row.OnVideo,
                    Key = wall ? SwimLang.T("ΣΤΡΟΦΗ ") + row.D.ToString(CultureInfo.InvariantCulture) + " " + unit
                               : SwimLang.T("ΠΕΡΑΣΜΑ ") + row.D.ToString(CultureInfo.InvariantCulture) + " " + unit,
                    Number = SwimReference.FormatTime(row.T.Value),
                    Sub = string.Join(" · ", sub)
                });
            }

            return list.OrderBy(x => x.Time).ToList();
        }

        private static void DrawFinish(Graphics g, SwimHudScene s, State st, Layout L)
        {
            // From the frame of the touch (the analyst stops there): without «Η ανάλυση με μια ματιά» it leaves after a few seconds
            // (the clock of the panel keeps the final time). See-through over the swimmer.
            double p0 = Math.Max(0, st.T - s.Reel.Finish.Value);
            float cx = L.CenterX;

            // In the middle of the video, else above the swimmer (below him when there is no room above): he stays in front.
            Func<float, RectangleF> block = y => new RectangleF(cx - 420, y - 10, 840, 280);
            float top = 330 * L.H / DesignH;
            if (!L.Swimmer.IsEmpty && block(top).IntersectsWith(RectangleF.Inflate(L.Swimmer, 40, 40)))
            {
                float above = L.Swimmer.Top - 44 - 270, below = L.Swimmer.Bottom + 54;
                if (above >= L.Video.Top + 10)
                    top = above;
                else if (below + 270 <= L.Video.Bottom)
                    top = below;
                else
                    top = L.Video.Top + 10;
            }

            float cy = top + 140;
            float a = s.Summary ? 1f : Clamp((FinishSeconds + 3 - p0) / 0.6, 0, 1);
            a *= SwimHud.Fade(block(top), L.Swimmer, 40f);
            if (a <= 0.01f)
                return;

            // Rings and rays from the middle of the video.
            for (int i = 0; i < 4; i++)
            {
                float p = Clamp((p0 - i * 0.15) / 1.4, 0, 1);
                if (p <= 0 || p >= 1)
                    continue;
                float radius = 40 + EaseOut(p) * (360 + i * 90);
                using (Pen ring = new Pen(Color.FromArgb((int)(255 * (1 - p) * a), C2), 2.5f))
                    g.DrawEllipse(ring, cx - radius, cy - radius * 0.62f, 2 * radius, 2 * radius * 0.62f);
            }

            float pr = Clamp(p0 / 0.9, 0, 1);
            if (pr < 1)
            {
                using (Pen ray = new Pen(Color.FromArgb((int)(191 * (1 - pr) * a), 143, 243, 255), 1.4f))
                {
                    for (int i = 0; i < 36; i++)
                    {
                        double angle = i * 10 * Math.PI / 180;
                        float r0 = 120 + EaseOut(pr) * 260, r1 = r0 + 30 + (i % 3) * 18;
                        g.DrawLine(ray, cx + r0 * (float)Math.Cos(angle), cy + r0 * (float)Math.Sin(angle) * 0.62f,
                            cx + r1 * (float)Math.Cos(angle), cy + r1 * (float)Math.Sin(angle) * 0.62f);
                    }
                }
            }

            // The final time, scaling in.
            float scale = 1.18f + (1 - 1.18f) * EaseOut(p0 / 0.5);
            GraphicsState state = g.Save();
            g.TranslateTransform(cx, top + 110);
            g.ScaleTransform(scale, scale);
            g.TranslateTransform(-cx, -(top + 110));
            using (LinearGradientBrush back = new LinearGradientBrush(new RectangleF(cx - 420, top - 10, 840, 280), Color.Transparent, Color.Transparent, LinearGradientMode.Horizontal))
            {
                ColorBlend blend = new ColorBlend(4)
                {
                    Colors = new[] { Color.FromArgb(0, 3, 22, 36), A((int)(150 * a), Color.FromArgb(3, 22, 36)), A((int)(150 * a), Color.FromArgb(3, 22, 36)), Color.FromArgb(0, 3, 22, 36) },
                    Positions = new[] { 0f, 0.2f, 0.8f, 1f }
                };
                back.InterpolationColors = blend;
                g.FillRectangle(back, cx - 420, top - 10, 840, 280);
            }

            Caps(g, SwimLang.T("ΤΕΛΙΚΟΣ ΧΡΟΝΟΣ"), Ui(16), A(C2, a), cx, top, 0.5f, -1);
            Glow(g, SwimReference.FormatTime(st.Final), Thin(168), A(Color.White, a), cx, top + 14, 0, 1.4f * a);
            double strokes = st.Rows.Where(r => r.Strokes.HasValue).Sum(r => r.Strokes.Value);
            List<string> sub = new List<string> { EventText(s.Race) };
            if (s.Race.Lane > 0)
                sub.Add(SwimLang.T("ΔΙΑΔΡΟΜΟΣ ") + s.Race.Lane.ToString(CultureInfo.InvariantCulture));
            if (strokes > 0)
                sub.Add(StrokesCaps(StrokeNumber(strokes)));
            if (s.DeltaInfo && s.Computed.Reference != null)
            {
                SwimRow finish = st.Rows.FirstOrDefault(r => r.D >= s.Race.Distance && r.Delta.HasValue);
                if (finish != null)
                    sub.Add("Δ " + SwimReference.FormatDelta(finish.Delta.Value));
            }

            Caps(g, string.Join(" · ", sub), Ui(15), A(C3, a), cx, top + 236, 0.32f, -1);
            g.Restore(state);
        }
        #endregion

        #region «Η ανάλυση με μια ματιά»
        private static void DrawDashboard(Graphics g, SwimHudScene s, State st, Layout L, float a, double t)
        {
            // Still (its animations over): what does not move from a bitmap kept, what moves over it.
            if (a >= 0.999f && t >= DashStill && DrawDashboardKept(g, s, st, L, t))
                return;

            DashPass(g, s, st, L, a, t, 0);
        }

        /// <summary>Seconds after which «Η ανάλυση με μια ματιά» does not move any more (but the swimmer of the pool and a ring).</summary>
        private const double DashStill = 3.0;

        /// <summary>The part of the dashboard drawn: 0 all, 1 what does not move (the bitmap kept), 2 what moves.</summary>
        [ThreadStatic]
        private static int dashPass;

        [ThreadStatic]
        private static Bitmap dashImage;

        [ThreadStatic]
        private static SwimComputed dashComputed;

        [ThreadStatic]
        private static string dashKey;

        /// <summary>
        /// The still dashboard: its unmoving part drawn once at the pixels of the image and kept (until the race, the size, the
        /// language or a value shown changes), the moving part drawn over it. False when the drawing is turned (not kept).
        /// </summary>
        private static bool DrawDashboardKept(Graphics g, SwimHudScene s, State st, Layout L, double t)
        {
            Rectangle device;
            using (Matrix m = g.Transform)
            {
                float[] e = m.Elements;
                if (Math.Abs(e[1]) > 1e-4 || Math.Abs(e[2]) > 1e-4)
                    return false;

                PointF[] corners = { new PointF(0, 0), new PointF(L.W, L.H) };
                m.TransformPoints(corners);
                device = Rectangle.FromLTRB((int)Math.Floor(Math.Min(corners[0].X, corners[1].X)), (int)Math.Floor(Math.Min(corners[0].Y, corners[1].Y)),
                    (int)Math.Ceiling(Math.Max(corners[0].X, corners[1].X)), (int)Math.Ceiling(Math.Max(corners[0].Y, corners[1].Y)));
                if (device.Width <= 0 || device.Height <= 0 || device.Width > 8192 || device.Height > 8192)
                    return false;

                string key = string.Join("|", device.Width.ToString(CultureInfo.InvariantCulture), device.Height.ToString(CultureInfo.InvariantCulture),
                    (corners[0].X - device.X).ToString("0.00", CultureInfo.InvariantCulture), (corners[0].Y - device.Y).ToString("0.00", CultureInfo.InvariantCulture),
                    e[0].ToString("0.0000", CultureInfo.InvariantCulture), SwimLang.English.ToString(), (s.Final != null).ToString(), s.DeltaInfo.ToString(),
                    ((int)s.Sync).ToString(CultureInfo.InvariantCulture), s.PdfReady.ToString(), s.Race.Lane.ToString(CultureInfo.InvariantCulture),
                    s.Race.Round ?? "", s.Race.Style ?? "", s.Race.Distance.ToString(CultureInfo.InvariantCulture), s.Race.Pool.ToString(CultureInfo.InvariantCulture),
                    s.Computed.Reference != null ? s.Computed.Reference.Name ?? "" : "-");
                if (dashImage == null || !ReferenceEquals(dashComputed, s.Computed) || dashKey != key)
                {
                    if (dashImage != null)
                        dashImage.Dispose();

                    dashImage = new Bitmap(device.Width, device.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (Graphics lg = Graphics.FromImage(dashImage))
                    {
                        lg.SmoothingMode = SmoothingMode.AntiAlias;
                        lg.TextRenderingHint = TextRenderingHint.AntiAlias;
                        using (Matrix local = m.Clone())
                        {
                            local.Translate(-device.X, -device.Y, MatrixOrder.Append);
                            lg.Transform = local;
                        }

                        DashPass(lg, s, st, L, 1f, DashStill + 5, 1);
                    }

                    dashComputed = s.Computed;
                    dashKey = key;
                }
            }

            GraphicsState state = g.Save();
            try
            {
                g.ResetTransform();
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(dashImage, device, 0, 0, device.Width, device.Height, GraphicsUnit.Pixel);
            }
            finally
            {
                g.Restore(state);
            }

            DashPass(g, s, st, L, 1f, t, 2);
            return true;
        }

        private static void DashPass(Graphics g, SwimHudScene s, State st, Layout L, float a, double t, int pass)
        {
            int before = dashPass;
            dashPass = pass;
            try
            {
                DashDraw(g, s, st, L, a, t);
            }
            finally
            {
                dashPass = before;
            }
        }

        private static void DashDraw(Graphics g, SwimHudScene s, State st, Layout L, float a, double t)
        {
            // The background over the video, with a fine grid.
            if (dashPass != 2)
            {
                using (LinearGradientBrush back = new LinearGradientBrush(new RectangleF(0, 0, L.W, L.H), Color.FromArgb((int)(204 * a), 4, 26, 40), Color.FromArgb((int)(235 * a), 1, 8, 14), LinearGradientMode.Vertical))
                    g.FillRectangle(back, 0, 0, L.W, L.H);
                using (Pen grid = new Pen(Color.FromArgb((int)(13 * a), 56, 225, 255), 1f))
                using (GraphicsPath lines = new GraphicsPath())
                {
                    for (float x = 0; x < L.W; x += 48)
                    {
                        lines.StartFigure();
                        lines.AddLine(x, 0, x, L.H);
                    }

                    for (float y = 0; y < L.H; y += 48)
                    {
                        lines.StartFigure();
                        lines.AddLine(0, y, L.W, y);
                    }

                    g.DrawPath(grid, lines);
                }
            }

            // Laid out on 1920 × 1080, in the middle of a wider image.
            float ox = (L.W - DesignW) / 2, oy = (L.H - DesignH) / 2;
            GraphicsState state = g.Save();
            g.TranslateTransform(ox, oy);
            if (dashPass != 2)
            {
                float head = EaseOut((t + 0.6) / 0.6) * a;
                Glow(g, SwimLang.T("Η ΑΝΑΛΥΣΗ ΜΕ ΜΙΑ ΜΑΤΙΑ"), Thin(40), A(Color.White, head), 66, 40, -1, head);
                List<string> sub = new List<string> { EventText(s.Race) };
                if (!string.IsNullOrWhiteSpace(s.Race.Round))
                    sub.Add(SwimLang.Upper(SwimLang.Label(s.Race.Round.Trim())));
                sub.Add(SwimLang.T("ΠΙΣΙΝΑ ") + Units(s.Race, s.Race.Pool));
                if (s.Race.Lane > 0)
                    sub.Add(SwimLang.T("ΔΙΑΔΡΟΜΟΣ ") + s.Race.Lane.ToString(CultureInfo.InvariantCulture));
                Caps(g, string.Join(" · ", sub), Ui(13), A(Lbl, head), 70, 104, 0.3f);
            }

            RectangleF[] boxes =
            {
                new RectangleF(70, 150, 520, 520), new RectangleF(70, 690, 520, 330), new RectangleF(620, 150, 720, 440),
                new RectangleF(620, 610, 720, 410), new RectangleF(1370, 150, 480, 520), new RectangleF(1370, 690, 480, 330)
            };

            for (int i = 0; i < boxes.Length; i++)
            {
                float p = EaseOut((t + 0.6 - i * 0.12) / 0.6) * a;
                if (p <= 0.01f)
                    continue;
                RectangleF b = boxes[i];
                b.Offset(0, (1 - p) * 30);

                // What moves: the swimmer of the pool and the ring of the final time.
                if (dashPass == 2 && i != 0 && i != 4)
                    continue;
                switch (i)
                {
                    case 0: DashTime(g, s, st, b, p, t); break;
                    case 1: DashBasics(g, s, st, b, p, t); break;
                    case 2: DashSpeed(g, s, st, b, p, t); break;
                    case 3: DashParts(g, s, st, b, p, t); break;
                    case 4: DashPool(g, s, st, b, p, t); break;
                    case 5: DashExport(g, s, b, p); break;
                }
            }

            g.Restore(state);
        }

        private static void DashTime(Graphics g, SwimHudScene s, State st, RectangleF b, float a, double t)
        {
            int part = s.Race.Distance > 200 ? 100 : 50;
            float cx = b.Left + 260, cy = b.Top + 44 + 215;
            if (dashPass == 2)
            {
                DashRing(g, cx, cy, a, t);
                return;
            }

            Panel(g, b, SwimLang.T("ΤΕΛΙΚΟΣ ΧΡΟΝΟΣ"), string.Format(CultureInfo.InvariantCulture, SwimLang.T("ΑΝΑ {0}"), Units(s.Race, part)), a);
            using (Pen ring = new Pen(A(Color.FromArgb(41, 56, 225, 255), a), 1f))
                g.DrawEllipse(ring, cx - 190, cy - 190, 380, 380);
            using (Pen tick = new Pen(A(Color.FromArgb(115, 143, 243, 255), a), 1f))
            using (GraphicsPath ticks = new GraphicsPath())
            {
                for (int i = 0; i < 90; i++)
                {
                    ticks.StartFigure();
                    ticks.AddLine(Polar(cx, cy, i % 5 == 0 ? 196 : 200, i * 4), Polar(cx, cy, 206, i * 4));
                }

                g.DrawPath(tick, ticks);
            }

            using (Pen track = new Pen(A(Color.FromArgb(33, 56, 225, 255), a), 16f))
                g.DrawEllipse(track, cx - 170, cy - 170, 340, 340);

            // The arc of each part of the race, one after the other.
            float k = Ease((t - 0.2) / 1.6);
            double total = st.Final;
            List<SwimRow> ends = st.Rows.Where(r => r.T.HasValue && (r.D % part == 0 || r.D >= s.Race.Distance)).OrderBy(r => r.D).ToList();
            double prev = 0;
            int n = 0;
            foreach (SwimRow row in ends)
            {
                float a0 = (float)(prev / total * 359.5) + (n > 0 ? 1.2f : 0), a1 = (float)(row.T.Value / total * 359.5);
                float shown = 359.5f * k;
                if (shown > a0 + 0.5f)
                {
                    using (Pen glow = new Pen(A(Color.FromArgb(70, 56, 225, 255), a), 26f))
                    using (Pen arc = new Pen(A(n % 2 == 0 ? C : C3, a), 16f))
                    {
                        Arc(g, glow, cx, cy, 170, a0, Math.Min(a1, shown));
                        Arc(g, arc, cx, cy, 170, a0, Math.Min(a1, shown));
                    }
                }

                prev = row.T.Value;
                n++;
            }

            if (dashPass == 0)
                DashRing(g, cx, cy, a, t);

            // The final time as it is (the ring is the animation).
            Glow(g, SwimReference.FormatTime(total), Thin(92), A(Color.White, a), cx, cy - 64, 0, a);
            Caps(g, EventText(s.Race), Ui(13), A(C2, a), cx, cy + 46, 0.3f, -1);

            // The boards added: the Δ with the reference at the finish, the place among the swimmers of the final.
            List<string> versus = new List<string>();
            if (s.DeltaInfo && s.Computed.Reference != null)
            {
                SwimRow finish = st.Rows.FirstOrDefault(r => r.D >= s.Race.Distance && r.Delta.HasValue);
                if (finish != null)
                    versus.Add("Δ " + SwimReference.FormatDelta(finish.Delta.Value));
            }

            if (s.Final != null && !double.IsNaN(total))
            {
                List<SwimFinalsBoardRow> board = SwimFinalsCompare.Board(s.Final, s.Race.Distance, total, s.BoardName);
                SwimFinalsBoardRow me = board != null ? board.FirstOrDefault(r => r.IsAthlete) : null;
                if (me != null)
                    versus.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} ΣΤΟΝ ΤΕΛΙΚΟ"), SwimLang.Upper(SwimLang.Ordinal(me.Place))));
            }

            if (versus.Count > 0)
                Caps(g, string.Join(" · ", versus), Ui(13, true), A(Amb, a), cx, cy + 74, 0.22f, -1);

            // The parts below the ring.
            prev = 0;
            float y = b.Bottom - 34;
            float lx = b.Left + 40;
            n = 0;
            foreach (SwimRow row in ends.Take(4))
            {
                float pa = Clamp((t - 0.9 - n * 0.4) / 0.4, 0, 1) * a;
                string text = string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}ο {1}  {2}"), n + 1, Units(s.Race, part), (row.T.Value - prev).ToString("0.00", CultureInfo.InvariantCulture));
                lx += Caps(g, text, Ui(14, true), A(n % 2 == 0 ? C : C3, pa), lx, y, 0.12f) + 24;
                prev = row.T.Value;
                n++;
            }
        }

        /// <summary>The dashed ring that turns inside the final time.</summary>
        private static void DashRing(Graphics g, float cx, float cy, float a, double t)
        {
            using (Pen dashed = new Pen(A(Color.FromArgb(128, 143, 243, 255), a), 1.2f) { DashPattern = new float[] { 2 / 1.2f, 9 / 1.2f } })
            {
                GraphicsState state = g.Save();
                g.TranslateTransform(cx, cy);
                g.RotateTransform((float)(t * 20 % 360));
                g.DrawEllipse(dashed, -146, -146, 292, 292);
                g.Restore(state);
            }
        }

        private static void DashBasics(Graphics g, SwimHudScene s, State st, RectangleF b, float a, double t)
        {
            Panel(g, b, SwimLang.T("ΒΑΣΙΚΑ ΣΤΟΙΧΕΙΑ"), SwimLang.T("ΕΚΚΙΝΗΣΗ · ΣΤΡΟΦΗ · ΧΕΡΙΕΣ"), a);
            SwimReel reel = s.Reel;
            SwimComputed c = s.Computed;
            string unit = s.Race.IsYards ? "yd" : SwimLang.T("μ.");
            List<string[]> tiles = new List<string[]>();
            double bd;
            if (reel.BreakoutDistances.TryGetValue(0, out bd))
                tiles.Add(new[] { SwimLang.T("ΥΠΟΒΡΥΧΙΑ ΕΚΚΙΝΗΣΗΣ"), (reel.BreakoutMeasured.Contains(0) ? "" : "≈") + bd.ToString("0.0", CultureInfo.InvariantCulture),
                    unit + (reel.FirstBreakout.HasValue ? " · " + reel.FirstBreakout.Value.ToString("0.00", CultureInfo.InvariantCulture) + " s" : "") });
            else
                tiles.Add(new[] { SwimLang.T("ΑΝΤΙΔΡΑΣΗ"), c.ReactionTime.HasValue ? c.ReactionTime.Value.ToString("0.00", CultureInfo.InvariantCulture) : "–", "s" });
            SwimRow r15 = st.Rows.FirstOrDefault(r => r.D == 15 && r.T.HasValue);
            tiles.Add(new[] { string.Format(CultureInfo.InvariantCulture, SwimLang.T("ΠΕΡΑΣΜΑ 15{0}"), unit), r15 != null ? r15.T.Value.ToString("0.00", CultureInfo.InvariantCulture) : (c.Start15.HasValue ? c.Start15.Value.ToString("0.00", CultureInfo.InvariantCulture) : "–"),
                r15 != null && r15.Vel.HasValue ? "s · " + r15.Vel.Value.ToString("0.00", CultureInfo.InvariantCulture) + " m/s" : "s" });
            if (reel.BreakoutDistances.TryGetValue(1, out bd))
                tiles.Add(new[] { SwimLang.T("ΥΠΟΒΡΥΧΙΑ ΣΤΡΟΦΗΣ"), (reel.BreakoutMeasured.Contains(1) ? "" : "≈") + bd.ToString("0.0", CultureInfo.InvariantCulture),
                    unit + (c.Turns.Count > 0 && c.Turns[0].UnderwaterTime.HasValue ? " · " + c.Turns[0].UnderwaterTime.Value.ToString("0.00", CultureInfo.InvariantCulture) + " s" : "") });
            else
                tiles.Add(new[] { SwimLang.T("ΤΕΛΕΥΤΑΙΑ 5") + unit, c.FinishLast5.HasValue ? c.FinishLast5.Value.ToString("0.00", CultureInfo.InvariantCulture) : "–", "s" });
            List<SwimReelPart> parts = reel.Parts;
            tiles.Add(new[] { SwimLang.T("ΧΕΡΙΕΣ"), parts.Count > 0 ? string.Join(" + ", parts.Select(p => p.Strokes.ToString(CultureInfo.InvariantCulture))) : "–",
                parts.Count > 1 ? SwimLang.T("ανά τμήμα") : "" });
            tiles.Add(new[] { SwimLang.T("ΣΥΧΝΟΤΗΤΑ"), c.AvgSr.HasValue ? c.AvgSr.Value.ToString("0.0", CultureInfo.InvariantCulture) : "–", SwimLang.T("κ/λ") });
            tiles.Add(new[] { SwimLang.T("ΜΕΣΗ ΤΑΧΥΤΗΤΑ"), c.AvgVel.HasValue ? c.AvgVel.Value.ToString("0.00", CultureInfo.InvariantCulture) : "–", "m/s" });
            for (int i = 0; i < tiles.Count; i++)
            {
                float pa = Clamp((t - 0.8 - i * 0.12) / 0.4, 0, 1) * a;
                RectangleF r = new RectangleF(b.Left + 18 + (i % 2) * 246, b.Top + 48 + (i / 2) * 90, 232, 80);
                using (SolidBrush back = new SolidBrush(A(Color.FromArgb(10, 56, 225, 255), pa)))
                    g.FillRectangle(back, r);
                using (Pen pen = new Pen(A(Color.FromArgb(56, 56, 225, 255), pa), 1f))
                    g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                Caps(g, tiles[i][0], Ui(10), A(Lbl, pa), r.Left + 12, r.Top + 9, 0.2f);
                Font vf = Thin(30);
                while (vf.Size > 10 && Width(g, tiles[i][1], vf) > r.Width - 20)
                    vf = SwimTheme.DisplayLight(vf.Size - 1.5f);
                Glow(g, tiles[i][1], vf, A(Ice, pa), r.Left + 8, r.Top + 26, -1, 0.7f * pa);
                Text(g, tiles[i][2], Ui(11), A(Color.FromArgb(178, 201, 251, 255), pa), r.Right - 12, r.Bottom - 22, 1);
            }
        }

        private static void DashSpeed(Graphics g, SwimHudScene s, State st, RectangleF b, float a, double t)
        {
            List<SwimRow> rows = st.Rows.Where(r => r.T.HasValue && (s.Race.IsYards ? r.VelYd : r.Vel).HasValue).ToList();
            Panel(g, b, SwimLang.T("ΤΑΧΥΤΗΤΑ ΑΝΑ ΑΠΟΣΤΑΣΗ"), string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} · {1} ΣΗΜΕΙΑ ΕΛΕΓΧΟΥ"), s.Race.IsYards ? "yd/s" : "m/s", rows.Count), a);
            if (rows.Count == 0)
                return;

            Func<SwimRow, double> V = r => (s.Race.IsYards ? r.VelYd : r.Vel).Value;
            double lo = Math.Floor((rows.Min(V) - 0.1) * 5) / 5, hi = Math.Ceiling((rows.Max(V) + 0.1) * 5) / 5;
            if (hi - lo < 0.6)
                hi = lo + 0.6;
            float left = b.Left + 60, right = b.Right - 40, top = b.Top + 70, bottom = b.Bottom - 70;
            Func<double, float> X = d => left + (float)(d / Math.Max(1, s.Race.Distance) * (right - left));
            Func<double, float> Y = v => bottom - (float)((v - lo) / (hi - lo) * (bottom - top));
            using (Pen grid = new Pen(A(Color.FromArgb(26, 56, 225, 255), a), 1f))
            {
                double step = hi - lo > 1.2 ? 0.4 : 0.2;
                for (double v = Math.Ceiling(lo / step) * step; v <= hi + 1e-9; v += step)
                {
                    g.DrawLine(grid, left, Y(v), right, Y(v));
                    Text(g, v.ToString("0.0", CultureInfo.InvariantCulture), Ui(11), A(Color.FromArgb(140, 143, 243, 255), a), left - 10, Y(v) - 8, 1);
                }
            }

            using (Pen wall = new Pen(A(Color.FromArgb(89, 143, 243, 255), a), 1f) { DashPattern = new float[] { 4, 4 } })
            {
                for (int w = s.Race.Pool; w < s.Race.Distance; w += s.Race.Pool)
                {
                    g.DrawLine(wall, X(w), top - 20, X(w), bottom + 4);
                    Caps(g, SwimLang.T("ΣΤΡΟΦΗ"), Ui(11), A(Color.FromArgb(153, 143, 243, 255), a), X(w) + 6, top - 18, 0.2f);
                }
            }

            int every = Math.Max(1, rows.Count / 12);
            for (int i = 0; i < rows.Count; i++)
            {
                if (i % every == 0 || i == rows.Count - 1)
                    Text(g, rows[i].D.ToString(CultureInfo.InvariantCulture), Ui(11), A(Color.FromArgb(153, 143, 243, 255), a), X(rows[i].D), bottom + 10, 0);
            }

            // Step line: each segment at its mean speed, drawn along the race.
            float dk = Ease((t - 0.6) / 2.2);
            double shownD = s.Race.Distance * dk;
            List<PointF> pts = new List<PointF>();
            foreach (SwimRow r in rows)
            {
                if (r.PrevD > shownD)
                    break;
                pts.Add(new PointF(X(r.PrevD), Y(V(r))));
                pts.Add(new PointF(X(Math.Min(r.D, shownD)), Y(V(r))));
            }

            if (pts.Count >= 2)
            {
                using (GraphicsPath area = new GraphicsPath())
                {
                    area.AddLines(pts.ToArray());
                    area.AddLine(pts[pts.Count - 1], new PointF(pts[pts.Count - 1].X, bottom));
                    area.AddLine(new PointF(pts[pts.Count - 1].X, bottom), new PointF(pts[0].X, bottom));
                    area.CloseFigure();
                    using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(left, top - 1, right - left, bottom - top + 2), A(Color.FromArgb(97, 56, 225, 255), a), A(Color.FromArgb(0, 56, 225, 255), a), LinearGradientMode.Vertical))
                        g.FillPath(fill, area);
                }

                using (Pen glow = new Pen(A(Color.FromArgb(70, 56, 225, 255), a), 8f) { LineJoin = LineJoin.Round })
                using (Pen line = new Pen(A(C2, a), 2.5f) { LineJoin = LineJoin.Round })
                {
                    g.DrawLines(glow, pts.ToArray());
                    g.DrawLines(line, pts.ToArray());
                }
            }

            foreach (SwimRow r in rows)
            {
                float pa = Clamp((shownD - r.D + 4) / 6, 0, 1) * a;
                if (pa <= 0.01f)
                    continue;
                float cx = X((r.PrevD + r.D) / 2.0), cy = Y(V(r));
                using (SolidBrush dot = new SolidBrush(A(Color.White, pa)))
                    g.FillEllipse(dot, cx - 4, cy - 4, 8, 8);
                if (rows.Count <= 16)
                    Text(g, V(r).ToString("0.00", CultureInfo.InvariantCulture), Ui(12, true), A(Ice, pa), cx, cy - 26, 0);
            }
        }

        private static void DashParts(Graphics g, SwimHudScene s, State st, RectangleF b, float a, double t)
        {
            Panel(g, b, SwimLang.T("ΤΜΗΜΑΤΑ ΑΓΩΝΑ"), SwimLang.T("ΧΡΟΝΟΣ ΑΝΑ ΦΑΣΗ"), a);
            List<Tuple<string, string, double>> parts = Phases(s, st);
            if (parts.Count == 0)
                return;

            double max = parts.Max(p => p.Item3);
            int n = Math.Min(parts.Count, 5);
            float rowH = Math.Min(66, (b.Height - 66) / n);
            for (int i = 0; i < n; i++)
            {
                float y = b.Top + 56 + i * rowH;
                float pa = Clamp((t - 1.0 - i * 0.15) / 0.8, 0, 1);
                Caps(g, parts[i].Item1, Ui(11), A(Lbl, a), b.Left + 22, y + 6, 0.2f);
                Text(g, parts[i].Item2, Ui(12), A(Color.FromArgb(153, 201, 251, 255), a), b.Left + 22, y + 24);
                RectangleF track = new RectangleF(b.Left + 180, y + 8, 420, 26);
                using (SolidBrush back = new SolidBrush(A(Color.FromArgb(20, 56, 225, 255), a)))
                    g.FillRectangle(back, track);
                using (Pen pen = new Pen(A(Color.FromArgb(46, 56, 225, 255), a), 1f))
                    g.DrawRectangle(pen, track.X, track.Y, track.Width, track.Height);
                float w = (float)(420 * parts[i].Item3 / Math.Max(1e-6, max)) * EaseOut(pa);
                if (w > 1)
                {
                    RectangleF bar = new RectangleF(track.Left, track.Top, w, track.Height);
                    using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(bar.X - 1, bar.Y, bar.Width + 2, bar.Height), A(Color.FromArgb(89, 56, 225, 255), a), A(Color.FromArgb(217, 143, 243, 255), a), LinearGradientMode.Horizontal))
                        g.FillRectangle(fill, bar);
                    using (Pen glow = new Pen(A(Color.FromArgb(60, 56, 225, 255), a), 6f))
                        g.DrawRectangle(glow, bar.X, bar.Y, bar.Width, bar.Height);
                }

                Glow(g, parts[i].Item3.ToString("0.00", CultureInfo.InvariantCulture), Thin(30), A(Ice, Clamp((t - 1.3 - i * 0.15) / 0.4, 0, 1) * a), b.Left + 612, y + 2, -1, 0.7f * a);
            }
        }

        /// <summary>The parts of the race: the start to 15, the swimming, the turns (from 5 before to 15 after), the last 5.</summary>
        private static List<Tuple<string, string, double>> Phases(SwimHudScene s, State st)
        {
            List<Tuple<string, string, double>> list = new List<Tuple<string, string, double>>();
            Func<int, double?> at = d => d == 0 ? 0 : st.Rows.Where(r => r.D == d && r.T.HasValue).Select(r => (double?)r.T.Value).FirstOrDefault();
            string u = s.Race.IsYards ? "yd" : SwimLang.T("μ.");
            int pool = Math.Max(1, s.Race.Pool), distance = s.Race.Distance;
            Action<string, int, int> add = (name, d0, d1) =>
            {
                double? t0 = at(d0), t1 = at(d1);
                if (t0.HasValue && t1.HasValue && t1.Value > t0.Value)
                    list.Add(Tuple.Create(name, string.Format(CultureInfo.InvariantCulture, "{0}–{1}{2}", d0, d1, u), t1.Value - t0.Value));
            };

            add(SwimLang.T("ΕΚΚΙΝΗΣΗ"), 0, 15);
            int from = 15;
            for (int wall = pool; wall < distance; wall += pool)
            {
                add(SwimLang.T("ΚΟΛΥΜΒΗΣΗ"), from, wall - 5);
                add(SwimLang.T("ΣΤΡΟΦΗ"), wall - 5, wall + 15);
                from = wall + 15;
            }

            add(SwimLang.T("ΚΟΛΥΜΒΗΣΗ"), from, distance - 5);
            add(SwimLang.T("ΤΕΡΜΑΤΙΣΜΟΣ"), distance - 5, distance);

            // A long race: the start, the swimming and the turns added up, the finish.
            if (list.Count > 5)
            {
                Func<string, double> sum = name => list.Where(x => x.Item1 == name).Sum(x => x.Item3);
                list = new List<Tuple<string, string, double>>
                {
                    Tuple.Create(SwimLang.T("ΕΚΚΙΝΗΣΗ"), "0–15" + u, sum(SwimLang.T("ΕΚΚΙΝΗΣΗ"))),
                    Tuple.Create(SwimLang.T("ΚΟΛΥΜΒΗΣΗ"), SwimLang.T("όλα τα μήκη"), sum(SwimLang.T("ΚΟΛΥΜΒΗΣΗ"))),
                    Tuple.Create(SwimLang.T("ΣΤΡΟΦΕΣ"), string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} στροφές"), list.Count(x => x.Item1 == SwimLang.T("ΣΤΡΟΦΗ"))), sum(SwimLang.T("ΣΤΡΟΦΗ"))),
                    Tuple.Create(SwimLang.T("ΤΕΡΜΑΤΙΣΜΟΣ"), string.Format(CultureInfo.InvariantCulture, "{0}–{1}{2}", distance - 5, distance, u), sum(SwimLang.T("ΤΕΡΜΑΤΙΣΜΟΣ")))
                }.Where(x => x.Item3 > 0).ToList();
            }

            return list;
        }

        private static void DashPool(Graphics g, SwimHudScene s, State st, RectangleF b, float a, double t)
        {
            if (dashPass != 2)
                Panel(g, b, SwimLang.T("ΨΗΦΙΑΚΗ ΠΙΣΙΝΑ"), s.Race.Lane > 0 ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("ΔΙΑΔΡΟΜΗ ΔΙΑΔΡ. {0}"), s.Race.Lane) : "", a);
            int pool = Math.Max(1, s.Race.Pool);
            int lanes = Math.Max(8, Math.Min(10, s.Race.Lane));
            string u = s.Race.IsYards ? "yd" : SwimLang.T("μ.");

            // The pool in perspective: a slanted rectangle seen from above and the side.
            PointF o = new PointF(b.Left + 70, b.Top + 330);
            PointF ux = new PointF(340f / pool, -100f / pool), uy = new PointF(68f / lanes, 120f / lanes * 0.55f);
            Func<double, double, PointF> P = (along, across) => new PointF(o.X + (float)(along * ux.X + across * uy.X), o.Y + (float)(along * ux.Y + across * uy.Y));
            PointF[] outline = { P(0, 0), P(pool, 0), P(pool, lanes), P(0, lanes) };
            if (dashPass != 2)
            {
                using (SolidBrush water = new SolidBrush(A(Color.FromArgb(15, 56, 225, 255), a)))
                    g.FillPolygon(water, outline);
                using (Pen glow = new Pen(A(Color.FromArgb(60, 56, 225, 255), a), 8f))
                    g.DrawPolygon(glow, outline);
                using (Pen border = new Pen(A(Color.FromArgb(204, 143, 243, 255), a), 1.5f))
                    g.DrawPolygon(border, outline);
                if (s.Race.Lane > 0)
                {
                    PointF[] mine = { P(0, s.Race.Lane - 1), P(pool, s.Race.Lane - 1), P(pool, s.Race.Lane), P(0, s.Race.Lane) };
                    using (SolidBrush lane = new SolidBrush(A(Color.FromArgb(46, 56, 225, 255), a)))
                        g.FillPolygon(lane, mine);
                }

                using (Pen rope = new Pen(A(Color.FromArgb(89, 143, 243, 255), a), 1f) { DashPattern = new float[] { 4, 3 } })
                {
                    for (int i = 1; i < lanes; i++)
                        g.DrawLine(rope, P(0, i), P(pool, i));
                }

                int[] marks = pool >= 50 ? new[] { 15, 25, 35, 45 } : new[] { 5, 10, 15, 20 };
                using (Pen mark = new Pen(A(Color.FromArgb(115, 143, 243, 255), a), 1f))
                {
                    foreach (int m in marks.Where(m => m < pool))
                    {
                        g.DrawLine(mark, P(m, 0), P(m, lanes));
                        PointF lp = P(m, lanes);
                        Text(g, m.ToString(CultureInfo.InvariantCulture), Ui(13, true), A(C2, a), lp.X, lp.Y + 4, 0);
                    }
                }

                PointF zero = P(0, 0), end = P(pool, 0);
                Caps(g, "0" + u, Ui(12, true), A(C2, a), zero.X - 6, zero.Y - 26, 0.2f);
                Caps(g, pool.ToString(CultureInfo.InvariantCulture) + u, Ui(12, true), A(C2, a), end.X + 6, end.Y - 26, 0.2f, 1);
            }

            // The swimmer runs the race again, every 7 s.
            if (dashPass != 1 && s.Race.Lane > 0 && s.Reel != null && s.Reel.HasModel && !double.IsNaN(st.Final))
            {
                double loop = ((t - 1.0) % 7 + 7) % 7;
                double rt = Clamp(loop / 6, 0, 1) * st.Final;
                double d = Math.Min(s.Race.Distance, s.Reel.DistanceAt(Math.Min(rt, s.Reel.Finish.Value)));
                double pos = PoolPosition(d, pool);
                bool back = ((int)Math.Floor(d / pool - 1e-9)) % 2 == 1;
                double lane = s.Race.Lane - 0.5;
                PointF from = P(back ? pool : 0, lane), dot = P(pos, lane);
                SwimTheme.GlowLine(g, A(C2, a), 3f, from, dot);
                using (SolidBrush h = new SolidBrush(A(Color.FromArgb(120, 56, 225, 255), a)))
                    g.FillEllipse(h, dot.X - 10, dot.Y - 10, 20, 20);
                using (SolidBrush w = new SolidBrush(A(Color.White, a)))
                    g.FillEllipse(w, dot.X - 6, dot.Y - 6, 12, 12);
                Glow(g, "≈" + d.ToString("0.0", CultureInfo.InvariantCulture) + " " + u, Thin(34), A(Color.White, a), b.Left + 24, b.Bottom - 62, -1, a);
                Caps(g, rt.ToString("0.00", CultureInfo.InvariantCulture) + " s", Ui(12), A(Lbl, a), b.Right - 24, b.Bottom - 40, 0.2f, 1);
            }
        }

        private static void DashExport(Graphics g, SwimHudScene s, RectangleF b, float a)
        {
            Panel(g, b, SwimLang.T("ΕΞΑΓΩΓΗ"), "REPORT · SHEET", a);
            string pdf = s.PdfReady ? SwimLang.T("ΕΤΟΙΜΟ") : SwimLang.T("ΟΧΙ ΑΚΟΜΗ");
            string sheet;
            float sheetDone;
            switch (s.Sync)
            {
                case SwimSyncState.Synced: sheet = SwimLang.T("ΣΥΓΧΡΟΝΙΣΜΕΝΟ"); sheetDone = 1f; break;
                case SwimSyncState.Sending: sheet = SwimLang.T("ΑΠΟΣΤΟΛΗ…"); sheetDone = 0.5f; break;
                case SwimSyncState.Idle: sheet = SwimLang.T("ΣΕ ΑΝΑΜΟΝΗ"); sheetDone = 0f; break;
                case SwimSyncState.Error: sheet = SwimLang.T("ΣΦΑΛΜΑ"); sheetDone = 0f; break;
                case SwimSyncState.Offline: sheet = SwimLang.T("ΧΩΡΙΣ ΔΙΚΤΥΟ"); sheetDone = 0f; break;
                default: sheet = SwimLang.T("ΧΩΡΙΣ ΣΥΝΔΕΣΗ"); sheetDone = 0f; break;
            }

            Status(g, b.Left + 80, b.Top + 36 + 78, "PDF REPORT", pdf, s.PdfReady ? 1f : 0f, b.Left + 150, a);
            Status(g, b.Left + 80, b.Top + 36 + 196, "GOOGLE SHEET", sheet, sheetDone, b.Left + 150, a);
        }

        private static void Status(Graphics g, float cx, float cy, string caption, string value, float done, float textX, float a)
        {
            using (Pen track = new Pen(A(Color.FromArgb(46, 56, 225, 255), a), 7f))
                g.DrawEllipse(track, cx - 40, cy - 40, 80, 80);
            if (done > 0.003f)
            {
                using (Pen glow = new Pen(A(Color.FromArgb(70, 56, 225, 255), a), 13f))
                using (Pen arc = new Pen(A(C, a), 7f))
                {
                    Arc(g, glow, cx, cy, 40, 0, 359.5f * done);
                    Arc(g, arc, cx, cy, 40, 0, 359.5f * done);
                }
            }

            if (done >= 1f)
            {
                using (Pen ok = new Pen(A(C3, a), 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(ok, new[] { new PointF(cx - 16, cy), new PointF(cx - 5, cy + 11), new PointF(cx + 16, cy - 12) });
            }

            Caps(g, caption, Ui(12), A(Lbl, a), textX, cy - 38, 0.2f);
            Glow(g, value, Thin(26), A(Ice, a), textX - 2, cy - 20, -1, 0.7f * a);
        }
        #endregion
    }
}
