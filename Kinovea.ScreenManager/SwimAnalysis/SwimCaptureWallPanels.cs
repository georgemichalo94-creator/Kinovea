#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.27.0) The live panel of the wall of cameras, in the free place of the wall (five cameras: the sixth place): the state of all
    /// the cameras (the recording and its time, ready, paused) and a line per camera (its number, its name, images per second, lost
    /// images). It reads the cameras four times a second while it is shown.
    /// </summary>
    public sealed class SwimLivePanel : Control
    {
        public const int TickMilliseconds = 250;

        private readonly Timer timer = new Timer();
        private Func<IList<SwimWallCamera>> provider;
        private IList<SwimWallCamera> cameras = new List<SwimWallCamera>();
        private bool failedLogged;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public SwimLivePanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = SwimTheme.Surface;
            timer.Interval = TickMilliseconds;
            timer.Tick += (s, e) => Pull();
        }

        /// <summary>Where the panel reads the cameras of the wall, in their order.</summary>
        public void SetProvider(Func<IList<SwimWallCamera>> provider)
        {
            this.provider = provider;
            Pull();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            timer.Enabled = Visible;
            if (Visible)
                Pull();
        }

        private void Pull()
        {
            if (provider == null || IsDisposed)
                return;

            try
            {
                cameras = provider() ?? new List<SwimWallCamera>();
            }
            catch (Exception e)
            {
                cameras = new List<SwimWallCamera>();
                if (!failedLogged)
                {
                    failedLogged = true;
                    log.Error("Error while reading the cameras for the live panel of the wall.", e);
                }
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Draw(e.Graphics, ClientRectangle, cameras, Math.Max(1f, DeviceDpi / 96f));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Stop();
                timer.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// The panel in 'r': its title, the state of all the cameras and the line of each camera; wide (below the images) the state on
        /// the left and the cameras on the right, else one under the other.
        /// </summary>
        public static void Draw(Graphics g, Rectangle r, IList<SwimWallCamera> cameras, float scale)
        {
            if (g == null || r.Width < 40 || r.Height < 40)
                return;

            List<SwimWallCamera> list = cameras == null ? new List<SwimWallCamera>() : cameras.Where(c => c != null).ToList();
            SmoothingMode smoothing = g.SmoothingMode;
            TextRenderingHint hint = g.TextRenderingHint;
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                using (LinearGradientBrush back = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2), SwimTheme.Raised, SwimTheme.Surface, LinearGradientMode.Vertical))
                    g.FillRectangle(back, r);

                using (Pen scan = new Pen(Color.FromArgb(9, 143, 243, 255), 1f))
                {
                    for (int y = r.Top + 2; y < r.Bottom - 1; y += 4)
                        g.DrawLine(scan, r.Left + 1, y, r.Right - 1, y);
                }

                bool wide = r.Width > r.Height * 2.3f;
                float s = Math.Max(1f, scale) * Math.Max(0.8f, Math.Min(1.45f, wide ? r.Height / 230f : Math.Min(r.Width / 380f, r.Height / 300f)));
                float pad = 18 * s;

                // The fonts (in points, which already follow the DPI) only with the size of the panel.
                float fs = s / Math.Max(1f, scale);
                SwimTheme.Brackets(g, RectangleF.Inflate(r, -6 * s, -6 * s), SwimTheme.Accent, 0.55f);

                RectangleF state, rows;
                if (wide)
                {
                    float split = Math.Max(250 * s, r.Width * 0.36f);
                    state = new RectangleF(r.Left + pad, r.Top + pad, split - pad * 1.5f, r.Height - 2 * pad);
                    rows = new RectangleF(r.Left + split + pad * 0.5f, r.Top + pad, r.Width - split - pad * 1.5f, r.Height - 2 * pad);
                    using (Pen line = new Pen(SwimTheme.GridLine, 1f))
                        g.DrawLine(line, r.Left + split, r.Top + pad, r.Left + split, r.Bottom - pad);
                }
                else
                {
                    state = new RectangleF(r.Left + pad, r.Top + pad, r.Width - 2 * pad, 0);
                    rows = RectangleF.Empty;
                }

                float bottom = DrawState(g, state, list, wide, s, fs);
                if (!wide)
                {
                    float hintHeight = 22 * s;
                    bool withHint = r.Height - (bottom - r.Top) > list.Count * 24 * s + hintHeight + pad * 2;
                    rows = new RectangleF(r.Left + pad, bottom + 12 * s, r.Width - 2 * pad, r.Bottom - pad - (bottom + 12 * s) - (withHint ? hintHeight : 0));
                    using (Pen line = new Pen(SwimTheme.GridLine, 1f))
                        g.DrawLine(line, r.Left + pad, bottom + 5 * s, r.Right - pad, bottom + 5 * s);

                    if (withHint)
                        DrawHint(g, new RectangleF(r.Left + pad, r.Bottom - pad - hintHeight + 4 * s, r.Width - 2 * pad, hintHeight), fs);
                }

                DrawRows(g, rows, list, s, fs);
            }
            catch (Exception e)
            {
                log.Error("Error while drawing the live panel of the wall of cameras.", e);
            }
            finally
            {
                g.SmoothingMode = smoothing;
                g.TextRenderingHint = hint;
            }
        }

        /// <summary>The title and the state of all the cameras; returns the bottom of what was drawn.</summary>
        private static float DrawState(Graphics g, RectangleF r, List<SwimWallCamera> list, bool wide, float s, float fs)
        {
            int loaded = list.Count(c => c.Loaded);
            int streaming = list.Count(c => c.Loaded && c.Connected && c.Grabbing && c.Fps > 0);
            bool recording = list.Any(c => c.Recording);
            double seconds = recording ? list.Where(c => c.Recording).Max(c => c.RecordingSeconds) : 0;
            bool paused = loaded > 0 && list.Where(c => c.Loaded).All(c => !c.Grabbing);

            float y = r.Top;
            Font title = SwimCaptureWall.Display(9.5f * fs);
            using (SolidBrush accent = new SolidBrush(SwimTheme.Accent))
                SwimTheme.Tracked(g, SwimLang.Upper(SwimLang.T("Ζωντανή εικόνα")), title, accent, r.Left, y);

            string count = SwimLang.Upper(list.Count == 1 ? SwimLang.T("1 κάμερα") : SwimLang.F($"{list.Count} κάμερες"));
            float countWidth = SwimTheme.TrackedWidth(g, count, title);
            if (countWidth < r.Width * 0.45f)
            {
                using (SolidBrush faint = new SolidBrush(SwimTheme.Faint))
                    SwimTheme.Tracked(g, count, title, faint, r.Right - countWidth, y);
            }

            y += title.GetHeight(g) + 14 * s;

            string label, value, sub;
            Color labelColor, valueColor, glow;
            if (recording)
            {
                label = SwimLang.T("Εγγραφή");
                labelColor = SwimTheme.Red;
                value = SwimCaptureWall.Clock(seconds, true);
                valueColor = SwimTheme.Text;
                glow = SwimTheme.Red;
                sub = streaming == loaded ? SwimLang.T("όλες οι κάμερες γράφουν") : SwimLang.F($"{streaming} από {loaded} κάμερες με εικόνα");
            }
            else if (loaded == 0)
            {
                label = SwimLang.T("Χωρίς κάμερες");
                labelColor = SwimTheme.Dim;
                value = "—";
                valueColor = SwimTheme.Faint;
                glow = SwimTheme.Faint;
                sub = SwimLang.T("καμία κάμερα στις οθόνες");
            }
            else if (paused)
            {
                label = SwimLang.T("Σε παύση");
                labelColor = SwimTheme.Amber;
                value = SwimCaptureWall.Clock(0, true);
                valueColor = SwimTheme.Faint;
                glow = SwimTheme.Amber;
                sub = SwimLang.T("οι κάμερες είναι σε παύση");
            }
            else
            {
                label = SwimLang.T("Έτοιμο για εγγραφή");
                labelColor = streaming == loaded ? SwimTheme.Green : SwimTheme.Amber;
                value = SwimCaptureWall.Clock(0, true);
                valueColor = SwimTheme.Faint;
                glow = SwimTheme.Accent;
                sub = SwimLang.F($"{streaming} από {loaded} κάμερες με εικόνα");
            }

            Font labelFont = SwimCaptureWall.Display(10f * fs);
            float dot = 9 * s;
            float labelHeight = labelFont.GetHeight(g);
            using (SolidBrush brush = new SolidBrush(labelColor))
            {
                if (recording)
                {
                    double phase = (Environment.TickCount & int.MaxValue) % 1000 / 1000.0;
                    int alpha = (int)Math.Round(150 + 105 * Math.Cos(phase * 2 * Math.PI));
                    using (SolidBrush beat = new SolidBrush(Color.FromArgb(Math.Max(60, Math.Min(255, alpha)), labelColor)))
                        g.FillEllipse(beat, r.Left, y + (labelHeight - dot) / 2f, dot, dot);
                }
                else
                {
                    g.FillEllipse(brush, r.Left, y + (labelHeight - dot) / 2f, dot, dot);
                }

                SwimTheme.Tracked(g, SwimLang.Upper(label), labelFont, brush, r.Left + dot + 8 * s, y);
            }

            y += labelHeight + 2 * s;
            Font big = SwimCaptureWall.FontOf(SwimTheme.LightFamily, (wide ? 30f : 32f) * fs);
            SwimTheme.GlowText(g, value, big, valueColor, glow, new PointF(r.Left - 3 * s, y), recording ? 1f : 0.35f);
            y += big.GetHeight(g) + 2 * s;

            Font subFont = SwimCaptureWall.Display(9.5f * fs);
            using (SolidBrush brush = new SolidBrush(!recording && loaded > 0 && streaming < loaded ? SwimTheme.Amber : SwimTheme.Dim))
            using (StringFormat format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(sub, subFont, brush, new RectangleF(r.Left, y, Math.Max(10, r.Width), subFont.GetHeight(g) + 2), format);

            y += subFont.GetHeight(g);
            if (wide && r.Bottom - y > 30 * s)
                DrawHint(g, new RectangleF(r.Left, r.Bottom - 20 * s, r.Width, 20 * s), fs);

            return y;
        }

        /// <summary>One line per camera: its number, its name and its state, in one or two columns.</summary>
        private static void DrawRows(Graphics g, RectangleF r, List<SwimWallCamera> list, float s, float fs)
        {
            if (list.Count == 0 || r.Width < 60 || r.Height < 14)
                return;

            float rowHeight = Math.Min(30 * s, Math.Max(18 * s, r.Height / list.Count));
            int perColumn = Math.Max(1, (int)Math.Floor(r.Height / rowHeight));
            int columns = Math.Min(2, (list.Count + perColumn - 1) / perColumn);
            if (columns > 1 && r.Width / columns < 170 * s)
                columns = 1;

            float columnWidth = r.Width / columns;
            float rs = Math.Min(s, rowHeight / (24f));
            for (int i = 0; i < list.Count; i++)
            {
                int column = i / perColumn;
                if (column >= columns)
                    break;

                int line = i % perColumn;
                RectangleF row = new RectangleF(r.Left + column * columnWidth + (column > 0 ? 10 * s : 0), r.Top + line * rowHeight, columnWidth - (column > 0 ? 10 * s : 0), rowHeight);
                DrawRow(g, row, list[i], rs, fs * rs / s);
            }
        }

        private static void DrawRow(Graphics g, RectangleF row, SwimWallCamera camera, float s, float fs)
        {
            float box = Math.Min(row.Height - 4 * s, 20 * s);
            float cy = row.Top + row.Height / 2f;
            RectangleF square = new RectangleF(row.Left, cy - box / 2f, box, box);
            Font numberFont = SwimCaptureWall.Display(9.5f * fs);
            Font font = SwimCaptureWall.Display(10f * fs);
            Font small = SwimCaptureWall.Display(9f * fs);

            SwimTheme.FillRounded(g, camera.Loaded ? SwimTheme.AccentDark : SwimTheme.Raised, square, 3 * s);
            using (SolidBrush brush = new SolidBrush(camera.Loaded ? SwimTheme.AccentPale : SwimTheme.Faint))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(camera.Number.ToString(CultureInfo.InvariantCulture), numberFont, brush, new RectangleF(square.Left, square.Top + 0.5f * s, square.Width, square.Height), center);

            string state;
            Color stateColor, dot;
            if (!camera.Loaded)
            {
                state = SwimLang.T("χωρίς κάμερα");
                stateColor = SwimTheme.Faint;
                dot = SwimTheme.Faint;
            }
            else if (camera.Recording)
            {
                state = camera.Fps > 0 ? string.Format(CultureInfo.CurrentCulture, "REC · {0:0} fps", camera.Fps) : "REC";
                stateColor = SwimTheme.Red;
                dot = SwimTheme.Red;
            }
            else if (camera.OnCamera)
            {
                state = SwimLang.T("γράφει στην κάρτα της");
                stateColor = SwimTheme.Dim;
                dot = SwimTheme.Accent;
            }
            else if (!camera.Grabbing)
            {
                state = SwimLang.T("παύση");
                stateColor = SwimTheme.Amber;
                dot = SwimTheme.Amber;
            }
            else if (!camera.Connected || camera.Fps <= 0)
            {
                state = SwimLang.T("χωρίς εικόνα");
                stateColor = SwimTheme.Red;
                dot = SwimTheme.Red;
            }
            else
            {
                state = string.Format(CultureInfo.CurrentCulture, "{0:0} fps", camera.Fps);
                stateColor = SwimTheme.Dim;
                dot = camera.Drops > 0 || camera.Load >= 85 ? SwimTheme.Amber : SwimTheme.Green;
            }

            float dotSize = 7 * s;
            float x = row.Right - dotSize;
            using (SolidBrush brush = new SolidBrush(dot))
                g.FillEllipse(brush, x, cy - dotSize / 2f, dotSize, dotSize);

            x -= 8 * s;
            using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            {
                float stateWidth = g.MeasureString(state, small).Width;
                using (SolidBrush brush = new SolidBrush(stateColor))
                    g.DrawString(state, small, brush, new RectangleF(x - stateWidth - 4 * s, row.Top, stateWidth + 4 * s, row.Height), right);
                x -= stateWidth + 4 * s;

                if (camera.Loaded && camera.Drops > 0 && !camera.OnCamera)
                {
                    string lost = string.Format(CultureInfo.CurrentCulture, "-{0}", camera.Drops);
                    float lostWidth = g.MeasureString(lost, small).Width;
                    using (SolidBrush brush = new SolidBrush(SwimTheme.Amber))
                        g.DrawString(lost, small, brush, new RectangleF(x - lostWidth - 8 * s, row.Top, lostWidth + 4 * s, row.Height), right);
                    x -= lostWidth + 8 * s;
                }
            }

            string name = string.IsNullOrEmpty(camera.Name) ? SwimLang.T("Κενή οθόνη") : camera.Name;
            float nameLeft = square.Right + 8 * s;
            using (SolidBrush brush = new SolidBrush(camera.Loaded ? SwimTheme.Text : SwimTheme.Faint))
            using (StringFormat format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(name, font, brush, new RectangleF(nameLeft, row.Top, Math.Max(10, x - nameLeft - 6 * s), row.Height), format);
        }

        private static void DrawHint(Graphics g, RectangleF r, float fs)
        {
            Font font = SwimCaptureWall.Display(8.5f * fs);
            using (SolidBrush brush = new SolidBrush(SwimTheme.Faint))
            using (StringFormat format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(SwimLang.T("Διπλό κλικ σε μια κάμερα: μόνη της σε όλη την οθόνη"), font, brush, r, format);
        }
    }

    /// <summary>
    /// (2.27.0) The row of the common controls of the capture screens in the colors of Kinovea Swim: the recording of all the cameras
    /// (with its time), pause / start of all the cameras, a snapshot of all, the swap of two screens, and on the right the wall of
    /// cameras on or off. It never takes the keyboard focus.
    /// </summary>
    public sealed class SwimCaptureBar : Control
    {
        public const int PartRecord = 0, PartGrab = 1, PartSnapshot = 2, PartSwap = 3, PartWall = 4, PartCount = 5;

        /// <summary>A part of the bar was clicked (one of the Part constants).</summary>
        public event Action<int> PartClicked;

        /// <summary>The cameras give their images (else they are paused: no recording).</summary>
        public bool Grabbing = true;

        /// <summary>Two screens: they can change places.</summary>
        public bool SwapAvailable;

        /// <summary>The wall of cameras is asked for, and the screens can make one.</summary>
        public bool WallOn, WallAvailable;

        private bool recording;
        private readonly Stopwatch watch = new Stopwatch();
        private readonly Timer timer = new Timer();
        private readonly ToolTip toolTip = new ToolTip { InitialDelay = 500, ReshowDelay = 100, AutoPopDelay = 9000 };
        private RectangleF[] places = new RectangleF[PartCount];
        private int hover = -1;
        private string tip;

        public SwimCaptureBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = SwimTheme.Surface;
            timer.Interval = 100;
            timer.Tick += (s, e) => Invalidate();
        }

        public bool Recording
        {
            get { return recording; }
        }

        /// <summary>The time of the recording going on.</summary>
        public double RecordingSeconds
        {
            get { return watch.Elapsed.TotalSeconds; }
        }

        /// <summary>The recording of the cameras started or stopped (its time starts at the first start).</summary>
        public void SetRecording(bool on)
        {
            if (on && !recording)
                watch.Restart();
            else if (!on)
                watch.Reset();

            recording = on;
            timer.Enabled = on;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Draw(e.Graphics, ClientRectangle);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int part = Hit(e.Location);
            if (part != hover)
            {
                hover = part;
                Cursor = part >= 0 && PartEnabled(part) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            string text = part >= 0 ? Tip(part) : null;
            if (text != tip)
            {
                tip = text;
                toolTip.SetToolTip(this, text ?? "");
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

            int part = Hit(e.Location);
            if (part >= 0 && PartEnabled(part) && PartClicked != null)
                PartClicked(part);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Stop();
                timer.Dispose();
                toolTip.Dispose();
            }

            base.Dispose(disposing);
        }

        private int Hit(Point point)
        {
            for (int i = 0; i < places.Length; i++)
            {
                if (!places[i].IsEmpty && places[i].Contains(point))
                    return i;
            }

            return -1;
        }

        private bool PartEnabled(int part)
        {
            switch (part)
            {
                case PartRecord: return Grabbing || recording;
                case PartSwap: return SwapAvailable;
                case PartWall: return WallAvailable;
                default: return true;
            }
        }

        private string Tip(int part)
        {
            switch (part)
            {
                case PartRecord: return recording ? SwimLang.T("Σταματά την εγγραφή σε όλες τις κάμερες μαζί.") : SwimLang.T("Ξεκινά την εγγραφή σε όλες τις κάμερες μαζί.");
                case PartGrab: return Grabbing ? SwimLang.T("Παύση της εικόνας όλων των καμερών.") : SwimLang.T("Η εικόνα όλων των καμερών συνεχίζει.");
                case PartSnapshot: return SwimLang.T("Στιγμιότυπο από όλες τις κάμερες.");
                case PartSwap: return SwimLang.T("Οι δύο οθόνες αλλάζουν θέση.");
                case PartWall: return WallOn ? SwimLang.T("Τοίχος καμερών: μόνο οι εικόνες, ενωμένες. Πάτησε για τις κλασικές οθόνες με τα κουμπιά τους.") :
                    SwimLang.T("Κλασικές οθόνες. Πάτησε για τον τοίχο καμερών: μόνο οι εικόνες, ενωμένες, με τα στοιχεία τους πάνω τους.");
                default: return null;
            }
        }

        /// <summary>The bar in 'r' (its parts are kept for the mouse).</summary>
        public void Draw(Graphics g, Rectangle r)
        {
            if (g == null || r.Width < 40 || r.Height < 16)
                return;

            SmoothingMode smoothing = g.SmoothingMode;
            TextRenderingHint hint = g.TextRenderingHint;
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                float s = Math.Max(1f, DeviceDpi / 96f);
                using (SolidBrush back = new SolidBrush(SwimTheme.Surface))
                    g.FillRectangle(back, r);
                using (Pen line = new Pen(SwimTheme.GridLine, 1f))
                    g.DrawLine(line, r.Left, r.Top, r.Right, r.Top);

                RectangleF[] next = new RectangleF[PartCount];
                float height = Math.Min(32 * s, r.Height - 10 * s);
                float top = r.Top + (r.Height - height) / 2f;
                float x = r.Left + 14 * s;
                Font font = SwimCaptureWall.Display(10f);
                Font caption = SwimCaptureWall.Display(8.5f);

                string all = SwimLang.Upper(SwimLang.T("Όλες οι κάμερες"));
                float captionWidth = SwimTheme.TrackedWidth(g, all, caption);
                if (r.Width > 700 * s)
                {
                    using (SolidBrush faint = new SolidBrush(SwimTheme.Faint))
                        SwimTheme.Tracked(g, all, caption, faint, x, r.Top + (r.Height - caption.GetHeight(g)) / 2f);
                    x += captionWidth + 16 * s;
                }

                string recordText = recording ? SwimLang.Upper(SwimLang.T("Στοπ")) + "  " + SwimCaptureWall.Clock(watch.Elapsed.TotalSeconds, true) : SwimLang.Upper(SwimLang.T("Εγγραφή"));
                float recordWidth = Math.Max(SwimTheme.TrackedWidth(g, SwimLang.Upper(SwimLang.T("Εγγραφή")), font, 0.08f),
                    SwimTheme.TrackedWidth(g, SwimLang.Upper(SwimLang.T("Στοπ")) + "  00:00.0", font, 0.08f)) + 52 * s;
                next[PartRecord] = new RectangleF(x, top, recordWidth, height);
                x += recordWidth + 8 * s;

                string grabText = SwimLang.Upper(Grabbing ? SwimLang.T("Παύση") : SwimLang.T("Έναρξη"));
                float grabWidth = Math.Max(SwimTheme.TrackedWidth(g, SwimLang.Upper(SwimLang.T("Παύση")), font, 0.08f), SwimTheme.TrackedWidth(g, SwimLang.Upper(SwimLang.T("Έναρξη")), font, 0.08f)) + 48 * s;
                next[PartGrab] = new RectangleF(x, top, grabWidth, height);
                x += grabWidth + 8 * s;

                string snapshotText = SwimLang.Upper(SwimLang.T("Στιγμιότυπο"));
                float snapshotWidth = SwimTheme.TrackedWidth(g, snapshotText, font, 0.08f) + 48 * s;
                next[PartSnapshot] = new RectangleF(x, top, snapshotWidth, height);
                x += snapshotWidth + 8 * s;

                string swapText = SwimLang.Upper(SwimLang.T("Εναλλαγή"));
                if (SwapAvailable)
                {
                    float swapWidth = SwimTheme.TrackedWidth(g, swapText, font, 0.08f) + 48 * s;
                    next[PartSwap] = new RectangleF(x, top, swapWidth, height);
                    x += swapWidth + 8 * s;
                }

                string wallText = SwimLang.Upper(SwimLang.T("Τοίχος καμερών"));
                if (WallAvailable)
                {
                    float wallWidth = SwimTheme.TrackedWidth(g, wallText, font, 0.08f) + 48 * s;
                    float wallLeft = Math.Max(x + 8 * s, r.Right - 14 * s - wallWidth);
                    next[PartWall] = new RectangleF(wallLeft, top, wallWidth, height);
                }

                places = next;

                // The recording: red with its time while it goes on.
                RectangleF rec = next[PartRecord];
                bool recordOn = PartEnabled(PartRecord);
                if (recording)
                    DrawButton(g, rec, Color.FromArgb(196, 38, 46), Color.FromArgb(255, 120, 120), Color.White, recordText, font, s, (gg, icon, ink) => { using (SolidBrush b = new SolidBrush(ink)) gg.FillRectangle(b, RectangleF.Inflate(icon, -icon.Width * 0.12f, -icon.Height * 0.12f)); }, hover == PartRecord);
                else
                    DrawButton(g, rec, recordOn ? Color.FromArgb(70, 120, 20, 28) : SwimTheme.Raised, recordOn ? SwimTheme.Red : SwimTheme.Border, recordOn ? SwimTheme.Text : SwimTheme.Faint, recordText, font, s,
                        (gg, icon, ink) => { using (SolidBrush b = new SolidBrush(recordOn ? SwimTheme.Red : SwimTheme.Faint)) gg.FillEllipse(b, icon); }, hover == PartRecord && recordOn);

                DrawButton(g, next[PartGrab], SwimTheme.Raised, SwimTheme.Border, Grabbing ? SwimTheme.Text : SwimTheme.Amber, grabText, font, s, (gg, icon, ink) =>
                {
                    using (SolidBrush b = new SolidBrush(ink))
                    {
                        if (Grabbing)
                        {
                            gg.FillRectangle(b, icon.Left + icon.Width * 0.12f, icon.Top, icon.Width * 0.28f, icon.Height);
                            gg.FillRectangle(b, icon.Left + icon.Width * 0.60f, icon.Top, icon.Width * 0.28f, icon.Height);
                        }
                        else
                        {
                            gg.FillPolygon(b, new[] { new PointF(icon.Left + icon.Width * 0.15f, icon.Top), new PointF(icon.Right, icon.Top + icon.Height / 2f), new PointF(icon.Left + icon.Width * 0.15f, icon.Bottom) });
                        }
                    }
                }, hover == PartGrab);

                DrawButton(g, next[PartSnapshot], SwimTheme.Raised, SwimTheme.Border, SwimTheme.Text, snapshotText, font, s, (gg, icon, ink) => SwimCaptureWall.IconCamera(gg, icon, ink, s), hover == PartSnapshot);

                if (SwapAvailable)
                {
                    DrawButton(g, next[PartSwap], SwimTheme.Raised, SwimTheme.Border, SwimTheme.Text, swapText, font, s, (gg, icon, ink) =>
                    {
                        using (Pen p = new Pen(ink, Math.Max(1.4f, 1.6f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        {
                            float y1 = icon.Top + icon.Height * 0.3f, y2 = icon.Top + icon.Height * 0.72f, a = icon.Width * 0.22f;
                            gg.DrawLine(p, icon.Left, y1, icon.Right, y1);
                            gg.DrawLines(p, new[] { new PointF(icon.Right - a, y1 - a), new PointF(icon.Right, y1), new PointF(icon.Right - a, y1 + a) });
                            gg.DrawLine(p, icon.Right, y2, icon.Left, y2);
                            gg.DrawLines(p, new[] { new PointF(icon.Left + a, y2 - a), new PointF(icon.Left, y2), new PointF(icon.Left + a, y2 + a) });
                        }
                    }, hover == PartSwap);
                }

                if (WallAvailable)
                {
                    DrawButton(g, next[PartWall], WallOn ? SwimTheme.AccentDark : SwimTheme.Raised, WallOn ? SwimTheme.Accent : SwimTheme.Border, WallOn ? SwimTheme.AccentPale : SwimTheme.Text, wallText, font, s, (gg, icon, ink) =>
                    {
                        using (SolidBrush b = new SolidBrush(ink))
                        {
                            float w = icon.Width / 3f - 1 * s, h = icon.Height / 2f - 1 * s;
                            for (int i = 0; i < 6; i++)
                            {
                                if (i == 5)
                                {
                                    using (Pen p = new Pen(ink, 1f))
                                        gg.DrawRectangle(p, icon.Left + 2 * (w + 1.5f * s), icon.Top + (h + 1.5f * s), w, h);
                                    continue;
                                }

                                gg.FillRectangle(b, icon.Left + (i % 3) * (w + 1.5f * s), icon.Top + (i / 3) * (h + 1.5f * s), w, h);
                            }
                        }
                    }, hover == PartWall);
                }
            }
            catch (Exception e)
            {
                log.Error("Error while drawing the common controls of the capture screens.", e);
            }
            finally
            {
                g.SmoothingMode = smoothing;
                g.TextRenderingHint = hint;
            }
        }

        /// <summary>A button of the bar: its shape with the corners cut, an icon on the left and its text in spaced capitals.</summary>
        private static void DrawButton(Graphics g, RectangleF r, Color fill, Color border, Color ink, string text, Font font, float s, Action<Graphics, RectangleF, Color> icon, bool hot)
        {
            if (r.IsEmpty)
                return;

            SwimTheme.FillRounded(g, hot ? SwimTheme.Mix(fill, SwimTheme.Accent, 0.18) : fill, r, 6 * s);
            SwimTheme.DrawRounded(g, hot ? SwimTheme.Accent : border, r, 6 * s);
            float size = 13 * s;
            RectangleF place = new RectangleF(r.Left + 14 * s, r.Top + (r.Height - size) / 2f, size, size);
            if (icon != null)
                icon(g, place, ink);

            using (SolidBrush brush = new SolidBrush(ink))
                SwimTheme.Tracked(g, text, font, brush, place.Right + 9 * s, r.Top + (r.Height - font.GetHeight(g)) / 2f + 0.5f * s, 0.08f);
        }

        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
    }
}
