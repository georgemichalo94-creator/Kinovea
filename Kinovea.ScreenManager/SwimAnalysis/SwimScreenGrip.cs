#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.14.0) The grip of a screen when only its image is shown (full screen, big screens) and there are other screens: a small round
    /// button with two arrows at the top left corner of the image. Dragged onto another screen, the two videos change places (the top
    /// row of the screen does the same when it is shown); a click makes the screen the active one. It never takes the keyboard focus.
    /// </summary>
    internal sealed class SwimScreenGrip : Control
    {
        private const float Size96 = 24;
        private readonly ToolTip toolTip = new ToolTip { InitialDelay = 400, ReshowDelay = 100, AutoPopDelay = 12000 };
        private bool hover;
        private bool pressed;
        private Point down;

        /// <summary>The mouse moved, pressed, beyond the size of a drag: the drag of the screen starts.</summary>
        public event EventHandler DragStarted;

        /// <summary>Pressed and released without moving.</summary>
        public event EventHandler Clicked;

        public SwimScreenGrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = SwimTheme.Back;
            Cursor = Cursors.SizeAll;
            int d = (int)Math.Round(Size96 * Math.Max(1f, DeviceDpi / 96f));
            Size = new Size(d, d);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(-1, -1, d + 2, d + 2);
                Region = new Region(path);
            }

            toolTip.SetToolTip(this, SwimLang.T("Σύρε την οθόνη πάνω σε άλλη: τα δύο βίντεο αλλάζουν θέση."));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                toolTip.Dispose();
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

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            pressed = true;
            down = Control.MousePosition;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!pressed || (Control.MouseButtons & MouseButtons.Left) == 0)
                return;

            Point now = Control.MousePosition;
            Size drag = SystemInformation.DragSize;
            if (Math.Abs(now.X - down.X) < drag.Width && Math.Abs(now.Y - down.Y) < drag.Height)
                return;

            pressed = false;
            Invalidate();
            EventHandler handler = DragStarted;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !pressed)
                return;

            pressed = false;
            Invalidate();
            EventHandler handler = Clicked;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(SwimTheme.Back);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Draw(e.Graphics, new RectangleF(0, 0, Width, Height), hover, pressed);
        }

        /// <summary>The grip in a square: a metal rim, a dark face, a glowing ring and two arrows the one over the other.</summary>
        internal static void Draw(Graphics g, RectangleF r, bool hover, bool pressed)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            SwimHudDial.Rim(g, r, 0.1f);
            float inset = r.Width * 0.1f;
            RectangleF face = new RectangleF(r.X + inset, r.Y + inset, r.Width - 2 * inset, r.Height - 2 * inset);
            Color middle = pressed ? SwimTheme.Mix(SwimTheme.AccentDark, SwimTheme.Accent, 0.35) :
                hover ? SwimTheme.Mix(SwimTheme.Raised, SwimTheme.AccentDark, 0.8) : SwimTheme.Mix(SwimTheme.Raised, SwimTheme.AccentDark, 0.35);
            SwimHudDial.Face(g, face, middle, SwimTheme.Back);

            float w = r.Width;
            PointF c = new PointF(r.X + w / 2, r.Y + w / 2);
            Color glow = hover ? SwimTheme.AccentLight : SwimTheme.Accent;
            using (Pen ring = new Pen(Color.FromArgb(hover ? 220 : 150, glow), Math.Max(1f, w * 0.05f)))
                g.DrawEllipse(ring, c.X - w * 0.33f, c.Y - w * 0.33f, w * 0.66f, w * 0.66f);

            // Two arrows: to the right above, to the left below.
            float half = w * 0.2f, gap = w * 0.09f, head = w * 0.08f;
            using (Pen arrow = new Pen(hover ? Color.White : SwimTheme.AccentPale, Math.Max(1.2f, w * 0.065f)))
            {
                arrow.StartCap = LineCap.Round;
                arrow.EndCap = LineCap.Round;
                g.DrawLine(arrow, c.X - half, c.Y - gap, c.X + half, c.Y - gap);
                g.DrawLine(arrow, c.X + half - head, c.Y - gap - head, c.X + half, c.Y - gap);
                g.DrawLine(arrow, c.X + half - head, c.Y - gap + head, c.X + half, c.Y - gap);
                g.DrawLine(arrow, c.X + half, c.Y + gap, c.X - half, c.Y + gap);
                g.DrawLine(arrow, c.X - half + head, c.Y + gap - head, c.X - half, c.Y + gap);
                g.DrawLine(arrow, c.X - half + head, c.Y + gap + head, c.X - half, c.Y + gap);
            }
        }
    }
}
