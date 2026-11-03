#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.16.1) What the playback bar of the full screen drives: the screens together (as the common controls, two screens or more) or
    /// the one screen. The times are in microseconds from the start of the timeline, the speed in percent of real time.
    /// </summary>
    public interface ISwimPlayback
    {
        bool Playing { get; }
        long Maximum { get; }
        long Position { get; }

        /// <summary>The time origin (the start signal) on the timeline, or -1.</summary>
        long Origin { get; }

        /// <summary>The time of the frame as the label under the timeline of a screen writes it.</summary>
        string Timecode { get; }

        double Speed { get; }
        double SpeedMax { get; }

        /// <summary>The speed of the video at its own frame rate (the double click on the speed of a screen).</summary>
        double SpeedNormal { get; }

        void TogglePlay();
        void First();
        void Previous();
        void Next();
        void Last();
        void Seek(long time);
        void SetSpeed(double percent);
    }

    /// <summary>
    /// (2.16.1) The playback bar of the full screen, at the bottom over the images: first frame, previous frame, play/pause, next frame,
    /// last frame, the time of the frame, the timeline (click or drag; the time origin in green) and the playback speed (drag or wheel;
    /// double click: the normal speed). It shows when the full screen starts and when the mouse moves, and hides 5 s after the mouse
    /// stopped, never while the mouse is on it or drags in it. It never takes the keyboard focus: the keys stay with the screens.
    /// </summary>
    public sealed class SwimPlaybackBar : Control
    {
        /// <summary>The bar hides this long after the mouse stopped.</summary>
        public const int HideAfterMilliseconds = 5000;

        /// <summary>The height of the bar at 96 DPI.</summary>
        public const float Height96 = 46f;

        /// <summary>The parts of the bar (Places).</summary>
        public const int PartFirst = 0, PartPrevious = 1, PartPlay = 2, PartNext = 3, PartLast = 4, PartTime = 5, PartTimeline = 6,
            PartSpeedLabel = 7, PartSpeed = 8, PartSpeedValue = 9, PartCount = 10;

        private const int TickMilliseconds = 100;
        private const int SeekEveryMilliseconds = 60;
        private static readonly Stopwatch clock = Stopwatch.StartNew();

        private readonly Timer timer = new Timer();
        private readonly ToolTip toolTip = new ToolTip { InitialDelay = 500, ReshowDelay = 100, AutoPopDelay = 10000 };
        private Func<ISwimPlayback> provider;
        private ISwimPlayback source;
        private bool armed;
        private Point lastCursor;
        private long lastMove = long.MinValue / 2;
        private int hover = -1;
        private int dragging = -1;
        private long lastSeek = long.MinValue / 2;
        private long pendingSeek = -1;
        private string tip;

        // What the bar shows (read from what it drives at each tick, and while dragging what the mouse asks).
        private bool playing;
        private long maximum;
        private long position;
        private long origin = -1;
        private string timecode = "";
        private double speed = 100;
        private double speedMax = 200;

        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public SwimPlaybackBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Visible = false;
            BackColor = SwimTheme.Back;
            Cursor = Cursors.Default;
            timer.Interval = TickMilliseconds;
            timer.Tick += Timer_Tick;
        }

        /// <summary>The DPI over 96 (at least 1).</summary>
        public float Scale96
        {
            get { return Math.Max(1f, DeviceDpi / 96f); }
        }

        /// <summary>The height of the bar on this screen.</summary>
        public int BarHeight
        {
            get { return (int)Math.Round(Height96 * Scale96); }
        }

        /// <summary>What the bar drives, asked at each tick (null: nothing to drive, the bar stays hidden).</summary>
        public void SetProvider(Func<ISwimPlayback> provider)
        {
            this.provider = provider;
        }

        /// <summary>
        /// The full screen is on: the bar shows now (for 5 s, as after a move of the mouse) and follows the mouse; off: it hides and
        /// waits.
        /// </summary>
        public bool Armed
        {
            get { return armed; }
            set
            {
                if (armed == value)
                    return;

                armed = value;
                if (armed)
                {
                    lastCursor = Control.MousePosition;
                    lastMove = clock.ElapsedMilliseconds;
                    timer.Start();
                    Tick();
                }
                else
                {
                    timer.Stop();
                    HideBar();
                }
            }
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

        protected override void WndProc(ref Message m)
        {
            // No context menu of the video on it.
            const int WM_CONTEXTMENU = 0x007B;
            if (m.Msg == WM_CONTEXTMENU)
                return;

            base.WndProc(ref m);
        }

        #region The mouse and the timer
        private void Timer_Tick(object sender, EventArgs e)
        {
            Tick();
        }

        /// <summary>The mouse moved over the screens: the bar shows; 5 s without a move: it hides (not while the mouse is on it or drags).</summary>
        private void Tick()
        {
            try
            {
                if (!armed || IsDisposed)
                    return;

                long now = clock.ElapsedMilliseconds;
                Point cursor = Control.MousePosition;
                Control host = Parent;
                bool inside = host != null && host.IsHandleCreated && host.Visible && host.RectangleToScreen(host.ClientRectangle).Contains(cursor);
                if (cursor != lastCursor)
                {
                    lastCursor = cursor;
                    if (inside)
                        lastMove = now;
                }

                source = provider != null ? provider() : null;
                bool over = Visible && IsHandleCreated && RectangleToScreen(ClientRectangle).Contains(cursor);
                bool want = source != null && host != null && (dragging >= 0 || over || now - lastMove < HideAfterMilliseconds);
                if (want && !Visible)
                    ShowBar();
                else if (!want && Visible)
                    HideBar();

                if (!Visible)
                    return;

                if (dragging == PartTimeline && pendingSeek >= 0 && now - lastSeek >= SeekEveryMilliseconds)
                    SeekNow(pendingSeek);

                ReadSource();
            }
            catch (Exception ex)
            {
                log.Error("Error in the playback bar of the full screen.", ex);
            }
        }

        private void ShowBar()
        {
            Visible = true;
            BringToFront();
            ReadSource();
            Invalidate();
        }

        private void HideBar()
        {
            dragging = -1;
            pendingSeek = -1;
            hover = -1;
            SetTip(null);
            if (Capture)
                Capture = false;
            Visible = false;
        }

        /// <summary>What the bar shows, from what it drives (the place dragged stays while it is dragged).</summary>
        private void ReadSource()
        {
            ISwimPlayback s = source;
            if (s == null)
                return;

            bool p = s.Playing;
            long max = Math.Max(0, s.Maximum);
            long pos = dragging == PartTimeline ? position : Math.Max(0, Math.Min(max, s.Position));
            long o = s.Origin;
            string tc = s.Timecode ?? "";
            double sp = dragging == PartSpeed ? speed : s.Speed;
            double sm = s.SpeedMax;
            if (double.IsNaN(sm) || sm <= 0)
                sm = 200;

            bool changed = p != playing || max != maximum || pos != position || o != origin || tc != timecode || Math.Abs(sp - speed) > 1e-6 || Math.Abs(sm - speedMax) > 1e-6;
            playing = p;
            maximum = max;
            position = pos;
            origin = o;
            timecode = tc;
            speed = sp;
            speedMax = sm;
            if (changed)
                Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            lastMove = clock.ElapsedMilliseconds;
            if (dragging == PartTimeline)
            {
                SeekAt(e.X, false);
                return;
            }

            if (dragging == PartSpeed)
            {
                SpeedAt(e.X);
                return;
            }

            int part = HitTest(e.Location);
            if (part != hover)
            {
                hover = part;
                SetTip(TipOf(part));
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (dragging >= 0)
                return;

            hover = -1;
            SetTip(null);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            lastMove = clock.ElapsedMilliseconds;
            ISwimPlayback s = source;
            if (e.Button != MouseButtons.Left || s == null)
                return;

            try
            {
                switch (HitTest(e.Location))
                {
                    case PartFirst:
                        s.First();
                        break;
                    case PartPrevious:
                        s.Previous();
                        break;
                    case PartPlay:
                        s.TogglePlay();
                        break;
                    case PartNext:
                        s.Next();
                        break;
                    case PartLast:
                        s.Last();
                        break;
                    case PartTimeline:
                        dragging = PartTimeline;
                        Capture = true;
                        SeekAt(e.X, true);
                        break;
                    case PartSpeed:
                        dragging = PartSpeed;
                        Capture = true;
                        SpeedAt(e.X);
                        break;
                }
            }
            catch (Exception ex)
            {
                log.Error("Error in a button of the playback bar of the full screen.", ex);
            }

            ReadSource();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            lastMove = clock.ElapsedMilliseconds;
            if (dragging == PartTimeline)
            {
                // The last place dragged, at once.
                SeekAt(e.X, true);
            }

            dragging = -1;
            pendingSeek = -1;
            if (Capture)
                Capture = false;
            hover = HitTest(e.Location);
            ReadSource();
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            ISwimPlayback s = source;
            int part = HitTest(e.Location);
            if (s == null || e.Button != MouseButtons.Left || (part != PartSpeed && part != PartSpeedLabel && part != PartSpeedValue))
                return;

            // The normal speed, as the double click on the speed of a screen.
            dragging = -1;
            s.SetSpeed(s.SpeedNormal);
            ReadSource();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            lastMove = clock.ElapsedMilliseconds;

            // Not to the screen under the bar too.
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (handled != null)
                handled.Handled = true;
            ISwimPlayback s = source;
            if (s == null || e.Delta == 0)
                return;

            int part = HitTest(e.Location);
            if (part == PartSpeed || part == PartSpeedLabel || part == PartSpeedValue)
            {
                double step = SpeedStep(speedMax);
                double target = e.Delta > 0 ? (Math.Floor(speed / step + 1e-6) + 1) * step : (Math.Ceiling(speed / step - 1e-6) - 1) * step;
                s.SetSpeed(Math.Max(speedMax / 1000, Math.Min(speedMax, target)));
            }
            else if (part == PartTimeline || part == PartTime)
            {
                if (e.Delta > 0)
                    s.Next();
                else
                    s.Previous();
            }

            ReadSource();
        }

        /// <summary>The speed steps of the wheel: 5% (videos at their speed), finer for the slow motion cameras.</summary>
        public static double SpeedStep(double speedMax)
        {
            return speedMax >= 100 ? 5 : speedMax >= 20 ? 1 : 0.5;
        }

        private int HitTest(Point p)
        {
            RectangleF[] parts = Places(ClientSize, Scale96);
            for (int i = 0; i < PartCount; i++)
            {
                RectangleF r = parts[i];
                if (r.IsEmpty)
                    continue;

                // The timeline and the speed slider can be caught a little above and below.
                if (i == PartTimeline || i == PartSpeed)
                    r = new RectangleF(r.X - 4, 0, r.Width + 8, Height);
                if (r.Contains(p))
                    return i;
            }

            return -1;
        }

        private void SeekAt(int x, bool now)
        {
            RectangleF line = Places(ClientSize, Scale96)[PartTimeline];
            if (source == null || line.Width <= 1 || maximum <= 0)
                return;

            double fraction = Math.Max(0, Math.Min(1, (x - line.Left) / line.Width));
            long time = (long)Math.Round(fraction * maximum);
            if (time != position)
            {
                position = time;
                Invalidate();
            }

            if (!now && clock.ElapsedMilliseconds - lastSeek < SeekEveryMilliseconds)
            {
                pendingSeek = time;
                return;
            }

            SeekNow(time);
        }

        private void SeekNow(long time)
        {
            lastSeek = clock.ElapsedMilliseconds;
            pendingSeek = -1;
            try
            {
                if (source != null)
                    source.Seek(time);
            }
            catch (Exception ex)
            {
                log.Error("Error while moving the screens from the playback bar of the full screen.", ex);
            }
        }

        private void SpeedAt(int x)
        {
            RectangleF slider = Places(ClientSize, Scale96)[PartSpeed];
            ISwimPlayback s = source;
            if (s == null || slider.Width <= 1 || speedMax <= 0)
                return;

            double fraction = Math.Max(0, Math.Min(1, (x - slider.Left) / slider.Width));
            double value = Math.Max(speedMax / 1000, fraction * speedMax);

            // Near the normal speed: the normal speed (as the slider of a screen sticks to it).
            double normal = s.SpeedNormal;
            if (normal > 0 && Math.Abs(value - normal) < speedMax * 0.025)
                value = normal;

            speed = value;
            s.SetSpeed(value);
            Invalidate();
        }

        private string TipOf(int part)
        {
            switch (part)
            {
                case PartFirst:
                    return SwimLang.T("Πρώτο καρέ");
                case PartPrevious:
                    return SwimLang.T("Προηγούμενο καρέ");
                case PartPlay:
                    return SwimLang.T("Αναπαραγωγή / Παύση");
                case PartNext:
                    return SwimLang.T("Επόμενο καρέ");
                case PartLast:
                    return SwimLang.T("Τελευταίο καρέ");
                case PartTime:
                    return SwimLang.T("Ο χρόνος του καρέ από το μηδέν του χρόνου (το σήμα εκκίνησης).");
                case PartTimeline:
                    return SwimLang.T("Κλικ ή σύρσιμο: άλλη στιγμή του βίντεο. Η πράσινη γραμμή: το μηδέν του χρόνου (το σήμα εκκίνησης).");
                case PartSpeedLabel:
                case PartSpeed:
                case PartSpeedValue:
                    return SwimLang.T("Ταχύτητα αναπαραγωγής: σύρε ή γύρνα τη ροδέλα. Διπλό κλικ: η κανονική ταχύτητα.");
                default:
                    return null;
            }
        }

        private void SetTip(string text)
        {
            if (text == tip)
                return;

            tip = text;
            if (string.IsNullOrEmpty(text) && IsHandleCreated)
                toolTip.Hide(this);
            toolTip.SetToolTip(this, text ?? "");
        }
        #endregion

        #region Painting
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(SwimTheme.Back);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Draw(e.Graphics, ClientSize, Scale96, playing, maximum, position, origin, timecode, speed, speedMax, dragging >= 0 ? dragging : hover);
            }
            catch (Exception ex)
            {
                log.Error("Error while drawing the playback bar of the full screen.", ex);
            }
        }

        /// <summary>
        /// The places of the parts of a bar of this size (scale: the DPI over 96): the five buttons at the left, the time of the frame,
        /// the timeline in the middle, the speed at the right (its label, its slider, its value). A narrow bar loses the label of the
        /// speed first, then its slider gets shorter: the timeline keeps at least 120 px (at 96 DPI). Empty where a part has no place.
        /// </summary>
        public static RectangleF[] Places(Size size, float scale)
        {
            RectangleF[] parts = new RectangleF[PartCount];
            float s = Math.Max(0.5f, scale);
            float h = size.Height;
            float pad = 10 * s, button = 34 * s, gap = 2 * s;
            float top = (h - button) / 2;
            float x = pad;
            for (int i = PartFirst; i <= PartLast; i++)
            {
                parts[i] = new RectangleF(x, top, button, button);
                x += button + gap;
            }

            parts[PartTime] = new RectangleF(x + 8 * s, 0, 112 * s, h);
            float left = parts[PartTime].Right + 10 * s;

            float right = size.Width - pad;
            float value = 58 * s, track = 130 * s, label = 70 * s, minTimeline = 120 * s;
            parts[PartSpeedValue] = new RectangleF(right - value, 0, value, h);
            float speedLeft = parts[PartSpeedValue].Left - 6 * s - track;
            bool withLabel = speedLeft - 6 * s - label - 16 * s - left >= minTimeline;
            if (!withLabel)
            {
                track = Math.Max(60 * s, Math.Min(track, parts[PartSpeedValue].Left - 6 * s - left - minTimeline - 16 * s));
                speedLeft = parts[PartSpeedValue].Left - 6 * s - track;
            }

            parts[PartSpeed] = new RectangleF(speedLeft, top, track, button);
            parts[PartSpeedLabel] = withLabel ? new RectangleF(speedLeft - 6 * s - label, 0, label, h) : RectangleF.Empty;
            float lineRight = (withLabel ? parts[PartSpeedLabel].Left : speedLeft) - 16 * s;
            parts[PartTimeline] = lineRight - left > 4 ? new RectangleF(left, top, lineRight - left, button) : RectangleF.Empty;
            return parts;
        }

        /// <summary>The speed as the speed of a screen writes it: below real time in percent (50%), else as a multiple (1x, 2x).</summary>
        public static string SpeedText(double percent)
        {
            double multiplier = percent / 100;
            return multiplier < 1.0 ? string.Format("{0:0.##}%", multiplier * 100) : string.Format("{0:0.##}x", multiplier);
        }

        /// <summary>
        /// The bar: its background with a line at the top, the buttons (lit under the mouse; the pause bars while it plays), the time of
        /// the frame, the timeline (the part played in the accent, the time origin in green, the knob) and the speed (its label, its
        /// slider, its value). hover: the part under the mouse or dragged (-1: none).
        /// </summary>
        public static void Draw(Graphics g, Size size, float scale, bool playing, long maximum, long position, long origin, string timecode,
            double speed, double speedMax, int hover)
        {
            if (size.Width <= 0 || size.Height <= 0)
                return;

            float s = Math.Max(0.5f, scale);
            RectangleF[] parts = Places(size, s);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (LinearGradientBrush back = new LinearGradientBrush(new Rectangle(0, 0, size.Width, size.Height), SwimTheme.Surface, SwimTheme.Back, 90f))
                g.FillRectangle(back, 0, 0, size.Width, size.Height);
            using (Pen line = new Pen(SwimTheme.Border, Math.Max(1f, s)))
                g.DrawLine(line, 0, 0.5f * s, size.Width, 0.5f * s);

            for (int i = PartFirst; i <= PartLast; i++)
                DrawButton(g, parts[i], i, i == PartPlay && playing, hover == i, s);

            using (StringFormat format = new StringFormat(StringFormatFlags.NoWrap))
            {
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                using (SolidBrush text = new SolidBrush(SwimTheme.Text))
                    g.DrawString(timecode ?? "", SwimTheme.Mono(11f), text, parts[PartTime], format);

                if (!parts[PartSpeedLabel].IsEmpty)
                {
                    format.Alignment = StringAlignment.Far;
                    using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
                        g.DrawString(SwimLang.T("Ταχύτητα"), SwimTheme.Ui(9f, FontStyle.Regular), dim, parts[PartSpeedLabel], format);
                }

                format.Alignment = StringAlignment.Far;
                using (SolidBrush text = new SolidBrush(SwimTheme.Text))
                    g.DrawString(SpeedText(speed), SwimTheme.Ui(10.5f, FontStyle.Bold), text, parts[PartSpeedValue], format);
            }

            double fraction = maximum > 0 ? Math.Max(0, Math.Min(1, (double)position / maximum)) : 0;
            double originFraction = maximum > 0 && origin > 0 && origin <= maximum ? (double)origin / maximum : -1;
            DrawSlider(g, parts[PartTimeline], fraction, originFraction, hover == PartTimeline, 4 * s, 6.5f * s, s);
            double speedFraction = speedMax > 0 ? Math.Max(0, Math.Min(1, speed / speedMax)) : 0;
            DrawSlider(g, parts[PartSpeed], speedFraction, -1, hover == PartSpeed, 3 * s, 5.5f * s, s);
        }

        /// <summary>A track across the place, filled in the accent up to the fraction, with its knob (and a green mark, when asked).</summary>
        private static void DrawSlider(Graphics g, RectangleF place, double fraction, double mark, bool hover, float thickness, float knob, float s)
        {
            if (place.IsEmpty || place.Width < 2)
                return;

            float cy = place.Top + place.Height / 2;
            RectangleF track = new RectangleF(place.Left, cy - thickness / 2, place.Width, thickness);
            using (SolidBrush empty = new SolidBrush(SwimTheme.Mix(SwimTheme.Raised, SwimTheme.Faint, 0.6)))
                g.FillRectangle(empty, track);

            float x = place.Left + (float)(fraction * place.Width);
            using (SolidBrush done = new SolidBrush(SwimTheme.Accent))
                g.FillRectangle(done, track.Left, track.Top, Math.Max(0, x - track.Left), track.Height);

            if (mark >= 0)
            {
                float mx = place.Left + (float)(mark * place.Width);
                using (Pen green = new Pen(SwimTheme.Green, Math.Max(1.5f, 2 * s)))
                    g.DrawLine(green, mx, cy - 8 * s, mx, cy + 8 * s);
            }

            float r = hover ? knob * 1.2f : knob;
            using (SolidBrush face = new SolidBrush(hover ? Color.White : SwimTheme.AccentPale))
                g.FillEllipse(face, x - r, cy - r, 2 * r, 2 * r);
            using (Pen ring = new Pen(SwimTheme.Accent, Math.Max(1f, 1.5f * s)))
                g.DrawEllipse(ring, x - r, cy - r, 2 * r, 2 * r);
        }

        /// <summary>A round button with its sign: |◀◀ first, ◀| previous, ▶ or ❚❚ play, |▶ next, ▶▶| last.</summary>
        private static void DrawButton(Graphics g, RectangleF r, int part, bool pause, bool hover, float s)
        {
            if (r.IsEmpty)
                return;

            if (hover)
            {
                using (SolidBrush lit = new SolidBrush(Color.FromArgb(60, SwimTheme.Accent)))
                    g.FillEllipse(lit, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            }

            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            float u = r.Height * 0.16f;
            using (SolidBrush ink = new SolidBrush(hover ? Color.White : SwimTheme.AccentPale))
            {
                switch (part)
                {
                    case PartPlay:
                        if (pause)
                        {
                            g.FillRectangle(ink, cx - 1.45f * u, cy - 1.7f * u, 1.0f * u, 3.4f * u);
                            g.FillRectangle(ink, cx + 0.45f * u, cy - 1.7f * u, 1.0f * u, 3.4f * u);
                        }
                        else
                        {
                            Triangle(g, ink, cx - 1.2f * u, cx + 1.9f * u, cy, 1.9f * u);
                        }

                        break;
                    case PartFirst:
                        g.FillRectangle(ink, cx - 2.1f * u, cy - 1.4f * u, 0.55f * u, 2.8f * u);
                        Triangle(g, ink, cx + 0.2f * u, cx - 1.4f * u, cy, 1.4f * u);
                        Triangle(g, ink, cx + 1.8f * u, cx + 0.2f * u, cy, 1.4f * u);
                        break;
                    case PartPrevious:
                        Triangle(g, ink, cx + 0.7f * u, cx - 1.3f * u, cy, 1.4f * u);
                        g.FillRectangle(ink, cx + 0.95f * u, cy - 1.4f * u, 0.55f * u, 2.8f * u);
                        break;
                    case PartNext:
                        g.FillRectangle(ink, cx - 1.5f * u, cy - 1.4f * u, 0.55f * u, 2.8f * u);
                        Triangle(g, ink, cx - 0.7f * u, cx + 1.3f * u, cy, 1.4f * u);
                        break;
                    case PartLast:
                        Triangle(g, ink, cx - 1.8f * u, cx - 0.2f * u, cy, 1.4f * u);
                        Triangle(g, ink, cx - 0.2f * u, cx + 1.4f * u, cy, 1.4f * u);
                        g.FillRectangle(ink, cx + 1.55f * u, cy - 1.4f * u, 0.55f * u, 2.8f * u);
                        break;
                }
            }
        }

        /// <summary>A triangle with its base at x 'baseX' (half height 'half') and its tip at x 'tipX', on the line y = cy.</summary>
        private static void Triangle(Graphics g, Brush brush, float baseX, float tipX, float cy, float half)
        {
            g.FillPolygon(brush, new[] { new PointF(baseX, cy - half), new PointF(tipX, cy), new PointF(baseX, cy + half) });
        }
        #endregion
    }

    /// <summary>(2.16.1) The playback bar drives one screen.</summary>
    internal sealed class SwimPlaybackScreen : ISwimPlayback
    {
        public SwimPlaybackScreen(PlayerScreen player)
        {
            Player = player;
        }

        public PlayerScreen Player { get; private set; }

        private PlayerScreenUserInterface View
        {
            get { return Player != null ? Player.view : null; }
        }

        private bool Ready
        {
            get { return Player != null && Player.Full && View != null && !View.IsDisposed; }
        }

        public bool Playing
        {
            get { return Ready && Player.IsPlaying; }
        }

        public long Maximum
        {
            get { return Ready ? Player.LocalLastTime : 0; }
        }

        public long Position
        {
            get { return Ready ? Player.LocalTime : 0; }
        }

        public long Origin
        {
            get { return Ready ? Player.LocalTimeOriginPhysical : -1; }
        }

        public string Timecode
        {
            get { return Ready ? View.SwimTimecode : ""; }
        }

        public double Speed
        {
            get { return Ready ? Player.RealtimePercentage : 100; }
        }

        public double SpeedMax
        {
            get { return Ready ? View.SwimSpeedMax : 200; }
        }

        public double SpeedNormal
        {
            get { return Ready ? View.SwimSpeedNormal : 100; }
        }

        public void TogglePlay()
        {
            if (Ready)
                View.OnButtonPlay();
        }

        public void First()
        {
            if (Ready)
                View.buttonGotoFirst_Click(null, EventArgs.Empty);
        }

        public void Previous()
        {
            if (Ready)
                View.buttonGotoPrevious_Click(null, EventArgs.Empty);
        }

        public void Next()
        {
            if (Ready)
                View.buttonGotoNext_Click(null, EventArgs.Empty);
        }

        public void Last()
        {
            if (Ready)
                View.buttonGotoLast_Click(null, EventArgs.Empty);
        }

        public void Seek(long time)
        {
            if (Ready)
                Player.GotoTime(Math.Max(0, Math.Min(Player.LocalLastTime, time)), true);
        }

        public void SetSpeed(double percent)
        {
            if (!Ready)
                return;

            double max = View.SwimSpeedMax;
            Player.RealtimePercentage = Math.Max(max / 1000, Math.Min(max, percent));
        }
    }

    /// <summary>(2.16.1) The playback bar drives the screens together, as the common controls (two screens or more, synchronised).</summary>
    internal sealed class SwimPlaybackScreens : ISwimPlayback
    {
        private readonly DualPlayerController dual;

        public SwimPlaybackScreens(DualPlayerController dual)
        {
            this.dual = dual;
        }

        public bool Playing
        {
            get { return dual.View.Playing; }
        }

        public long Maximum
        {
            get { return dual.SwimLastTime; }
        }

        public long Position
        {
            get { return dual.SwimShownTime; }
        }

        public long Origin
        {
            get { return dual.SwimOriginTime; }
        }

        public string Timecode
        {
            get { return dual.SwimTimecode; }
        }

        public double Speed
        {
            get { return dual.SwimSpeed; }
        }

        public double SpeedMax
        {
            get { return dual.SwimSpeedMax; }
        }

        public double SpeedNormal
        {
            get { return dual.SwimSpeedNormal; }
        }

        public void TogglePlay()
        {
            dual.View.ExecuteDualCommand((int)DualPlayerCommands.TogglePlay);
        }

        public void First()
        {
            dual.View.ExecuteDualCommand((int)DualPlayerCommands.GotoFirstImage);
        }

        public void Previous()
        {
            dual.View.ExecuteDualCommand((int)DualPlayerCommands.GotoPreviousImage);
        }

        public void Next()
        {
            dual.View.ExecuteDualCommand((int)DualPlayerCommands.GotoNextImage);
        }

        public void Last()
        {
            dual.View.ExecuteDualCommand((int)DualPlayerCommands.GotoLastImage);
        }

        public void Seek(long time)
        {
            dual.SwimSeek(time);
        }

        public void SetSpeed(double percent)
        {
            dual.SwimSetSpeed(percent);
        }
    }

    /// <summary>
    /// (2.16.1) The screens of a group of screens one frame forward or back together (the wheel on one of them). Pure: the plan only, the
    /// dual player moves the screens.
    /// </summary>
    public static class SwimFrameStep
    {
        /// <summary>Within this part of a frame two times are the same frame (the times are rounded to the microsecond).</summary>
        private const double Tolerance = 0.01;

        /// <summary>
        /// The screens of a group one frame forward (direction 1) or back (-1) together. For each screen: the local time of its frame now,
        /// the length of its frames and the time of its last frame (microseconds from the start of its working zone), and its place on
        /// the common timeline of the synchronisation (common = offset + local / scale, as CommonTimeline). The moment they show is the
        /// latest of their frames (a screen behind comes to it); the step goes to the next (previous) moment where one of the videos has
        /// a frame, each screen to its frame at that moment (a slower camera keeps its frame until its next one). Returns for each screen
        /// the local time to ask, half a frame before its frame (the player shows the first frame at or after the time asked), or -1
        /// where the screen stays; null when no video has a frame further (before): the end (the start) of all of them.
        /// </summary>
        public static long[] Plan(long[] now, long[] frame, long[] last, long[] offset, double[] scale, int direction, out long moment)
        {
            moment = 0;
            int n = now != null ? now.Length : 0;
            if (n == 0 || direction == 0 || frame == null || last == null || offset == null || scale == null ||
                frame.Length != n || last.Length != n || offset.Length != n || scale.Length != n)
                return null;

            // The moment they show: the latest of their frames.
            long shown = long.MinValue;
            for (int i = 0; i < n; i++)
                shown = Math.Max(shown, Common(offset[i], scale[i], now[i]));

            // The next (previous) moment where one of the videos has a frame.
            bool found = false;
            long target = 0;
            for (int i = 0; i < n; i++)
            {
                if (frame[i] <= 0)
                    continue;

                double f = frame[i];
                double x = (Local(offset[i], scale[i], shown) - now[i]) / f;
                double j = direction > 0 ? Math.Floor(x + Tolerance) + 1 : Math.Ceiling(x - Tolerance) - 1;
                double t = now[i] + j * f;
                if (t < -f / 2 || t > last[i] + f / 2)
                    continue;

                long c = Common(offset[i], scale[i], (long)Math.Round(Math.Max(0, Math.Min(last[i], t))));
                if (!found || (direction > 0 ? c < target : c > target))
                {
                    target = c;
                    found = true;
                }
            }

            if (!found)
                return null;

            // Each screen to its frame at that moment, within its video.
            moment = target;
            long[] aims = new long[n];
            for (int i = 0; i < n; i++)
            {
                aims[i] = -1;
                if (frame[i] <= 0)
                    continue;

                double f = frame[i];
                double j = Math.Floor((Local(offset[i], scale[i], target) - now[i]) / f + Tolerance);
                double first = Math.Ceiling(-now[i] / f - Tolerance);
                double end = Math.Floor((last[i] - now[i]) / f + Tolerance);
                j = Math.Max(first, Math.Min(end, j));
                if (j != 0)
                    aims[i] = Math.Max(0, now[i] + (long)Math.Round((j - 0.5) * f));
            }

            return aims;
        }

        private static long Common(long offset, double scale, long local)
        {
            return offset + (long)(local / (scale > 0 ? scale : 1));
        }

        private static long Local(long offset, double scale, long common)
        {
            return (long)(common * (scale > 0 ? scale : 1)) - offset;
        }
    }
}
