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
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The buttons of the analysis over the video (2.9.5, the "multibutton"). The full screen keeps no room beside the image for the
    /// bars (SwimVideoBar), so a round button over the video takes their place. A click opens around it the buttons of the bars.
    /// A drag moves it anywhere on the video (its place is kept); hidden, a small dot in the corner brings it back (so does the key of
    /// SwimDial). Since 2.14.0 every screen with a video has one (several cameras of the same swimmer, also with the big screens): the
    /// one of the screen of the analysis has the race time and the buttons; the others wait with the number of their screen, and a
    /// click takes the analysis to their video and opens its buttons. It never takes the keyboard focus: the keys of the analysis keep
    /// going to the video.
    /// (2.17.0) Open, it is a wheel of the HUD: a hub in the middle with the name, the value and the key of the sector under the mouse
    /// (else ✕ and the race time); an inner ring with the commands (the check of the proposals, the phase shown, the strokes made by
    /// the program, undo, the window of the analysis, «Απόκρυψη»); one outer ring or more with the marks of the phase and their tools
    /// in the order of the events from the top, clockwise, each a sector with its sign, its short name, the value already measured
    /// and its key. Near an edge of the video it opens as a half wheel turned away from the edge (the hub stays where it was); in a
    /// corner the whole wheel moves into the video. The video shows between the sectors. An own design.
    /// </summary>
    internal sealed class SwimHudDial : Control, IMessageFilter
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // Sizes at 96 dpi.
        private const float CenterSize = 56;
        private const float HubSize = 96;
        private const float InnerBand = 28;
        private const float OuterBand = 64;
        private const float NextBand = 56;
        private const float BandGap = 6;
        private const float SectorGap = 3;
        private const float InnerArc = 36;
        private const float OuterArc = 56;
        private const float DotSize = 14;
        private const float Margin = 6;

        /// <summary>
        /// The place of the centre at first, a fraction of the video area: bottom middle, under the map of the HUD (the wheel opens
        /// upwards as a half wheel, without moving it).
        /// </summary>
        private static readonly PointF DefaultPlace = new PointF(0.5f, 0.95f);

        private const string CenterId = "dial";
        private const string DotId = "dial-dot";

        /// <summary>A sector of the open wheel: the button of the bars, its short name, its ring (radii) and its angles (radians).</summary>
        private sealed class Item
        {
            public SwimBarButton Button;
            public string Label;
            public bool Inner;
            public float R0;
            public float R1;
            public double A0;
            public double A1;
        }

        /// <summary>The open wheel: its centre, its scale, its span (a whole wheel or a half one turned away from an edge), its sectors.</summary>
        private sealed class Wheel
        {
            public PointF Center;
            public float Scale;
            public double Span;
            public double Start;
            public float Radius;
            public bool Shifted;
            public float BandFrom;
            public float BandTo;
            public List<Item> Items = new List<Item>();
        }

        private readonly SwimAnalysisManager manager;

        /// <summary>(2.14.0) The round button of a group of screens: in the middle of the group, over its screens.</summary>
        private readonly bool group;

        /// <summary>(2.14.0) Where the area is in the control the button is on (the group: the screens of the group in their common parent).</summary>
        private Point origin;

        /// <summary>A left bar that is never shown: the buttons of the bars (DialButtons) and the phase chosen on the round button.</summary>
        private readonly SwimVideoBar source;
        private readonly ToolTip toolTip = new ToolTip { InitialDelay = 500, ReshowDelay = 100, AutoPopDelay = 15000 };
        private Wheel wheel;
        private Region shape;
        private Size area;

        /// <summary>The centre in the video area when closed (where the analyst put it) and the centre in the control.</summary>
        private PointF place;
        private PointF center;
        private bool expanded;
        private bool filtering;
        private string hoverId;
        private string pressedId;
        private string tipId;
        private string signature = "";
        private string clockShown = "";
        private float scale = 1f;

        /// <summary>The scale of the drawing: the one of the screen, smaller when the open wheel needs it in a small area.</summary>
        private float drawScale = 1f;

        /// <summary>(2.17.0) When the wheel opened moved into the video: a second click of a double click is not taken by a sector.</summary>
        private DateTime openedAt = DateTime.MinValue;

        // Moving it: pressed on the centre, then dragged (the mouse on the screen and the place when it was pressed).
        private bool downOnCenter;
        private bool dragging;
        private Point downMouse;
        private PointF downPlace;

        public SwimHudDial(SwimAnalysisManager manager, bool group = false)
        {
            this.manager = manager;
            this.group = group;
            source = new SwimVideoBar(manager, true);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = SwimTheme.Back;
            ForeColor = SwimTheme.Text;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopFilter();
                toolTip.Dispose();
                source.Dispose();
            }

            base.Dispose(disposing);
            if (disposing && shape != null)
            {
                shape.Dispose();
                shape = null;
            }
        }

        protected override void WndProc(ref Message m)
        {
            // No context menu of the video on it (the right click would go up to the viewport).
            const int WM_CONTEXTMENU = 0x007B;
            if (m.Msg == WM_CONTEXTMENU)
                return;

            base.WndProc(ref m);
        }

        private bool Hidden
        {
            get { return manager.Settings.DialHidden; }
        }

        /// <summary>(2.14.0) On another screen than the one of the analysis: it waits, with the number of its screen.</summary>
        private bool Standby
        {
            get { return !manager.IsAnalysisScreen(this); }
        }

        private List<Item> Items
        {
            get { return wheel != null ? wheel.Items : new List<Item>(); }
        }

        private float S(float value)
        {
            return value * scale;
        }

        #region Place and shape
        /// <summary>The video area (its size) changed, or the full screen started: the button at its place in it.</summary>
        internal void PlaceIn(Size viewport)
        {
            PlaceIn(new Rectangle(Point.Empty, viewport));
        }

        /// <summary>(2.14.0) The area of the button in the control it is on (the screens of a group in their common parent).</summary>
        internal void PlaceIn(Rectangle where)
        {
            if (where.Size == area && where.Location == origin && area.Width > 0 && !IsDisposed && Parent != null)
            {
                // Same place: only on top again (the screens may have been put over it).
                if (Parent.Controls.GetChildIndex(this) != 0)
                    BringToFront();
                return;
            }

            origin = where.Location;
            area = where.Size;
            scale = Math.Max(1f, DeviceDpi / 96f);
            place = Clamp(SavedPlace());
            Relayout();
        }

        /// <summary>Lays it out again now (hidden or shown, its buttons).</summary>
        internal void RefreshNow()
        {
            if ((Hidden || Standby) && expanded)
                SetExpanded(false);

            signature = Signature();
            clockShown = Clock();
            Relayout();
        }

        /// <summary>
        /// Places the buttons again when they changed, redraws the centre when the race time changed (timer of the analysis, 150 ms).
        /// </summary>
        internal void RefreshIfChanged()
        {
            if (Standby && expanded)
                SetExpanded(false);

            string current = Signature();
            string clock = Clock();
            if (current != signature)
            {
                signature = current;
                clockShown = clock;
                Relayout();
            }
            else if (clock != clockShown)
            {
                // The centre shows the time (open: small, under the ✕, while no sector is under the mouse).
                clockShown = clock;
                if (!Hidden)
                    Invalidate();
            }
        }

        /// <summary>Opens or closes the buttons around the centre.</summary>
        internal void SetExpanded(bool open)
        {
            if (open && (Hidden || Standby))
                open = false;
            if (expanded == open)
                return;

            expanded = open;
            if (open)
                StartFilter();
            else
                StopFilter();

            hoverId = null;
            tipId = null;
            toolTip.SetToolTip(this, "");
            signature = Signature();
            Relayout();
            openedAt = open && wheel != null && wheel.Shifted ? DateTime.UtcNow : DateTime.MinValue;
            if (open)
                BringToFront();
        }

        private PointF SavedPlace()
        {
            float fx = DefaultPlace.X;
            float fy = DefaultPlace.Y;
            if (group)
            {
                fx = 0.5f;
                fy = 0.5f;
            }

            string text = group ? manager.Settings.GroupDialPlace : manager.Settings.DialPlace;
            if (!string.IsNullOrEmpty(text))
            {
                string[] parts = text.Split(',');
                float x, y;
                if (parts.Length == 2 &&
                    float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
                    x >= 0 && x <= 1 && y >= 0 && y <= 1)
                {
                    fx = x;
                    fy = y;
                }
            }

            return new PointF(fx * area.Width, fy * area.Height);
        }

        private void SavePlace()
        {
            if (area.Width < 1 || area.Height < 1)
                return;

            string value = string.Format(CultureInfo.InvariantCulture, "{0:0.####},{1:0.####}", place.X / area.Width, place.Y / area.Height);
            if (group)
                manager.Settings.GroupDialPlace = value;
            else
                manager.Settings.DialPlace = value;
            manager.Settings.Save();
        }

        /// <summary>A place of the centre with the whole closed button inside the area.</summary>
        private PointF Clamp(PointF p)
        {
            float r = S(CenterSize) / 2 + S(Margin);
            return new PointF(Between(p.X, r, area.Width - r), Between(p.Y, r, area.Height - r));
        }

        private static float Between(float v, float lo, float hi)
        {
            return lo > hi ? (lo + hi) / 2 : Math.Max(lo, Math.Min(hi, v));
        }

        /// <summary>
        /// The bounds and the shape of the control: the centre (or the dot when hidden), and the wheel when open. The shape is the
        /// hub and the sectors only, so the video shows between and around them.
        /// </summary>
        private void Relayout()
        {
            if (area.Width < 1 || area.Height < 1 || IsDisposed)
                return;

            drawScale = scale;
            Wheel next = null;
            PointF c;
            RectangleF box;
            if (Hidden)
            {
                float d = S(DotSize);
                c = new PointF(area.Width - S(Margin) - d / 2, area.Height - S(Margin) - d / 2);
                box = Circle(c, d);
            }
            else if (expanded)
            {
                next = Layout(Buttons(), place, new RectangleF(0, 0, area.Width, area.Height), scale);
                c = next.Center;
                drawScale = next.Scale;
                box = BoundsOf(next);
            }
            else
            {
                c = place;
                box = Circle(c, S(CenterSize));
            }

            // Whole pixels around it, one more for its edge.
            Rectangle bounds = Rectangle.FromLTRB(
                (int)Math.Floor(box.Left) - 1,
                (int)Math.Floor(box.Top) - 1,
                (int)Math.Ceiling(box.Right) + 1,
                (int)Math.Ceiling(box.Bottom) + 1);

            float dx = -bounds.Left;
            float dy = -bounds.Top;
            center = new PointF(c.X + dx, c.Y + dy);
            if (next != null)
                next.Center = center;
            wheel = next;

            Region region = ShapeOf(center, next, Hidden ? S(DotSize) : S(CenterSize));

            // In the control it is on (the area of a group is not at its corner).
            bounds.Offset(origin);
            if (Bounds != bounds)
                Bounds = bounds;
            Region = region;
            if (shape != null)
                shape.Dispose();
            shape = region;
            Invalidate();
        }

        /// <summary>The shape: a circle closed; open, the hub, the sectors and the dashed band between the inner and the outer rings.</summary>
        private static Region ShapeOf(PointF c, Wheel w, float closed)
        {
            if (w == null)
            {
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(c.X - closed / 2 - 1, c.Y - closed / 2 - 1, closed + 2, closed + 2);
                    return new Region(path);
                }
            }

            Region region;
            float hub = HubSize * w.Scale;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(c.X - hub / 2 - 1, c.Y - hub / 2 - 1, hub + 2, hub + 2);
                region = new Region(path);
            }

            foreach (Item it in w.Items)
            {
                using (GraphicsPath path = SectorPath(c, it.R0 - 0.5f, it.R1 + 0.5f, it.A0, it.A1, Math.Max(0f, SectorGap * w.Scale - 1f)))
                    region.Union(path);
            }

            if (w.BandTo > w.BandFrom)
            {
                using (GraphicsPath band = SectorPath(c, w.BandFrom - 0.5f, w.BandTo + 0.5f, w.Start, w.Start + w.Span, 0f))
                    region.Union(band);
            }

            return region;
        }

        /// <summary>
        /// The open wheel of the buttons at a place in an area: a whole wheel around the place when it fits; else a half wheel turned
        /// away from the nearest edge (the centre stays at its place); else the whole wheel moved into the area. In a small area
        /// (five screens in full screen) everything is smaller, so that it fits.
        /// </summary>
        private static Wheel Layout(List<SwimBarButton> buttons, PointF place, RectangleF area, float scale)
        {
            List<SwimBarButton> inner = buttons.Where(b => IsCommand(b.Id)).ToList();
            List<SwimBarButton> outer = buttons.Where(b => !IsCommand(b.Id)).ToList();

            // Away from the nearest edge.
            float left = place.X - area.Left, right = area.Right - place.X, top = place.Y - area.Top, bottom = area.Bottom - place.Y;
            float nearest = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
            double away = nearest == bottom ? -Math.PI / 2 : nearest == top ? Math.PI / 2 : nearest == left ? 0 : Math.PI;

            float s = scale;
            Wheel w = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                w = Build(inner, outer, place, 2 * Math.PI, -Math.PI / 2, s);
                if (Inside(w, area))
                    return w;

                w = Build(inner, outer, place, Math.PI, away - Math.PI / 2, s);
                if (Inside(w, area))
                    return w;

                w = Build(inner, outer, place, 2 * Math.PI, -Math.PI / 2, s);
                float room = w.Radius + Margin * s;
                if (area.Width >= 2 * room && area.Height >= 2 * room)
                {
                    PointF moved = new PointF(Between(place.X, area.Left + room, area.Right - room), Between(place.Y, area.Top + room, area.Bottom - room));
                    w = Build(inner, outer, moved, 2 * Math.PI, -Math.PI / 2, s);
                    w.Shifted = Math.Abs(moved.X - place.X) > 0.5f || Math.Abs(moved.Y - place.Y) > 0.5f;
                    return w;
                }

                s = Math.Max(scale * 0.45f, s * 0.85f);
            }

            PointF middle = new PointF(area.Left + area.Width / 2, area.Top + area.Height / 2);
            w = Build(inner, outer, middle, 2 * Math.PI, -Math.PI / 2, s);
            w.Shifted = true;
            return w;
        }

        /// <summary>
        /// The sectors of a wheel: the commands on the inner ring (as many as fit, the others after the marks), the marks on outer rings
        /// from the inside out, each ring with a share of them as of its length. A whole wheel starts at the top (its first sector
        /// there); a half one from its start angle, clockwise.
        /// </summary>
        private static Wheel Build(List<SwimBarButton> inner, List<SwimBarButton> outer, PointF c, double span, double start, float s)
        {
            bool whole = span >= 2 * Math.PI - 1e-6;
            Wheel w = new Wheel { Center = c, Scale = s, Span = span, Start = start };
            float hub = HubSize * s / 2;
            float r0 = hub + BandGap * s / 2;
            float r1 = r0 + InnerBand * s;
            double mid = (r0 + r1) / 2.0;
            int capacity = Math.Max(1, (int)Math.Floor(span * mid / (InnerArc * s)));
            List<SwimBarButton> ring = inner.Take(capacity).ToList();
            List<SwimBarButton> rest = outer.Concat(inner.Skip(capacity)).ToList();
            Place(w, ring, r0, r1, span, start, whole, true);
            float to = ring.Count > 0 ? r1 : hub;

            // The outer rings: how many, then a share of the marks to each as of its capacity.
            List<float[]> rings = new List<float[]>();
            List<int> capacities = new List<int>();
            float from = to + BandGap * s;
            int total = 0;
            while (total < rest.Count)
            {
                float thick = (rings.Count == 0 ? OuterBand : NextBand) * s;
                float a = rings.Count == 0 ? from : rings[rings.Count - 1][1] + BandGap * s / 2;
                float b = a + thick;
                int cap = Math.Max(1, (int)Math.Floor(span * ((a + b) / 2.0) / (OuterArc * s)));
                rings.Add(new float[] { a, b });
                capacities.Add(cap);
                total += cap;
            }

            int done = 0;
            for (int k = 0; k < rings.Count; k++)
            {
                int share = k == rings.Count - 1 ? rest.Count - done : Math.Min(capacities[k], (int)Math.Round((double)rest.Count * capacities[k] / total));
                share = Math.Max(0, Math.Min(share, rest.Count - done));
                Place(w, rest.Skip(done).Take(share).ToList(), rings[k][0], rings[k][1], span, start, whole, false);
                done += share;
            }

            if (ring.Count > 0 && rings.Count > 0)
            {
                w.BandFrom = r1;
                w.BandTo = rings[0][0];
            }

            w.Radius = w.Items.Count > 0 ? w.Items.Max(i => i.R1) : hub;
            return w;
        }

        private static void Place(Wheel w, List<SwimBarButton> list, float r0, float r1, double span, double start, bool whole, bool inner)
        {
            int n = list.Count;
            if (n == 0)
                return;

            double step = span / n;
            double first = whole ? -Math.PI / 2 - step / 2 : start;
            for (int i = 0; i < n; i++)
                w.Items.Add(new Item { Button = list[i], Label = Label(list[i]), Inner = inner, R0 = r0, R1 = r1, A0 = first + i * step, A1 = first + (i + 1) * step });
        }

        /// <summary>The commands of the inner ring (the others are marks and their tools, on the outer rings).</summary>
        private static bool IsCommand(string id)
        {
            switch (id)
            {
                case "phase":
                case "undo":
                case "panel":
                case "hide":
                case "auto-prev":
                case "auto-next":
                case "auto-delete":
                case "proposal-delete":
                case "skeleton-delete":
                    return true;
                default:
                    return IsCheck(id);
            }
        }

        private static bool Inside(Wheel w, RectangleF area)
        {
            RectangleF b = BoundsOf(w);
            float m = Margin * w.Scale;
            return b.Left >= area.Left + m && b.Top >= area.Top + m && b.Right <= area.Right - m && b.Bottom <= area.Bottom - m;
        }

        /// <summary>The box of the hub and of the sectors (a half wheel: its half only).</summary>
        private static RectangleF BoundsOf(Wheel w)
        {
            float hub = HubSize * w.Scale / 2;
            float x0 = w.Center.X - hub, y0 = w.Center.Y - hub, x1 = w.Center.X + hub, y1 = w.Center.Y + hub;
            foreach (Item it in w.Items)
            {
                int steps = Math.Max(2, (int)Math.Ceiling((it.A1 - it.A0) / 0.05));
                for (int i = 0; i <= steps; i++)
                {
                    double a = it.A0 + (it.A1 - it.A0) * i / steps;
                    foreach (float r in new[] { it.R0, it.R1 })
                    {
                        PointF p = Polar(w.Center, r, a);
                        x0 = Math.Min(x0, p.X);
                        y0 = Math.Min(y0, p.Y);
                        x1 = Math.Max(x1, p.X);
                        y1 = Math.Max(y1, p.Y);
                    }
                }
            }

            return RectangleF.FromLTRB(x0, y0, x1, y1);
        }

        private static PointF Polar(PointF c, double r, double a)
        {
            return new PointF(c.X + (float)(r * Math.Cos(a)), c.Y + (float)(r * Math.Sin(a)));
        }

        /// <summary>
        /// A ring sector from r0 to r1 and from a0 to a1 (radians, clockwise from the right), with a gap of the same width all along
        /// its sides (the angles a little inside at each radius).
        /// </summary>
        private static GraphicsPath SectorPath(PointF c, float r0, float r1, double a0, double a1, float gap)
        {
            GraphicsPath path = new GraphicsPath();
            double i1 = gap > 0 && r1 > 0 ? gap / (2.0 * r1) : 0;
            double i0 = gap > 0 && r0 > 0 ? gap / (2.0 * r0) : 0;
            double s1 = a0 + i1, e1 = a1 - i1, s0 = a0 + i0, e0 = a1 - i0;
            if (e1 - s1 < 0.002)
            {
                s1 = (a0 + a1) / 2 - 0.001;
                e1 = s1 + 0.002;
            }

            path.AddArc(c.X - r1, c.Y - r1, 2 * r1, 2 * r1, Deg(s1), Deg(e1 - s1));
            if (r0 > 0.5f && e0 - s0 > 0.002)
                path.AddArc(c.X - r0, c.Y - r0, 2 * r0, 2 * r0, Deg(e0), -Deg(e0 - s0));
            else
                path.AddLine(Polar(c, Math.Max(0f, r0), (a0 + a1) / 2), Polar(c, Math.Max(0f, r0), (a0 + a1) / 2));
            path.CloseFigure();
            return path;
        }

        private static float Deg(double radians)
        {
            return (float)(radians * 180 / Math.PI);
        }

        private static RectangleF Circle(PointF c, float d)
        {
            return new RectangleF(c.X - d / 2, c.Y - d / 2, d, d);
        }
        #endregion

        #region Buttons
        /// <summary>
        /// The buttons in their order: the check of the proposals while it runs (the next to press), the phase shown, the marks of
        /// the phase, their tools and the other buttons of the bars, «Απόκρυψη».
        /// </summary>
        private List<SwimBarButton> Buttons()
        {
            List<SwimBarButton> all;
            try
            {
                all = source.DialButtons();
            }
            catch (Exception e)
            {
                log.Error("Could not list the buttons of the swim analysis for the round button.", e);
                all = new List<SwimBarButton>();
            }

            List<SwimBarButton> list = new List<SwimBarButton>();
            list.AddRange(all.Where(b => IsCheck(b.Id)));
            list.Add(source.DialPhaseButton());
            list.AddRange(all.Where(b => !IsCheck(b.Id)));
            list.Add(HideButton());
            return list;
        }

        private static bool IsCheck(string id)
        {
            return id == "race-right" || id == "race-wrong" || id == "race-end" || id == "proposal-right" || id == "proposal-wrong" || id == "proposal-end";
        }

        private SwimBarButton HideButton()
        {
            return new SwimBarButton
            {
                Id = "hide",
                Text = SwimLang.T("Απόκρυψη"),
                Key = source.KeyOf(PlayerScreenCommands.SwimDial),
                Tip = SwimLang.T("Κρύβει το στρογγυλό κουμπί της ανάλυσης: μια μικρή τελεία στη γωνία το ξαναφέρνει."),
                Click = () => manager.SetDialHidden(true)
            };
        }

        /// <summary>The short name of a button on its sector (the whole name is in the hub and in its tip).</summary>
        internal static string Label(SwimBarButton b)
        {
            switch (b.Id)
            {
                case "phase":
                case "skeleton":
                case "auto-pick":
                    return b.Text;
                case "start": return SwimLang.T("Εκκίν.");
                case "start-light": return "💡";
                case "auto-pilot": return "✈";
                case "reaction":
                case "t-reaction": return SwimLang.T("Αντίδρ.");
                case "block":
                case "t-feetoff": return SwimLang.T("Αποδ.");
                case "entry":
                case "t-entry": return SwimLang.T("Είσοδος");
                case "kick":
                case "t-kick": return SwimLang.T("Δελφίν.");
                case "breakout":
                case "t-breakout": return SwimLang.T("Έξοδος");
                case "stroke": return SwimLang.T("Χεριά");
                case "pushoff":
                case "t-pushoff": return SwimLang.T("Αποκόλλ.");
                case "auto-race": return "🤖";
                case "split": return SplitLabel(b.Text);
                case "t-signal": return SwimLang.T("Σήμα");
                case "t-hands": return SwimLang.T("Χέρια");
                case "t-backfoot": return SwimLang.T("Πίσω\nπόδι");
                case "t-split": return "5/10/15";
                case "t-stroke":
                case "t-first": return SwimLang.T("1η\nχεριά");
                case "t-in5": return "−5";
                case "t-last": return SwimLang.T("Τελ.\nχεριά");
                case "t-rotation": return SwimLang.T("Περιστρ.");
                case "t-wall": return SwimLang.T("Επαφή");
                case "t-out": return "+5/10/15";
                case "t-snapshot": return "📷";
                case "t-angle": return "∠";
                case "skeleton-angles": return SwimLang.T("Γωνίες");
                case "skeleton-delete": return SwimLang.T("✕ Σκελ.");
                case "proposal-right":
                case "race-right": return "✓";
                case "proposal-wrong":
                case "race-wrong": return "✕";
                case "proposal-end":
                case "race-end": return SwimLang.T("Τέλος");
                case "proposal-review":
                case "race-review": return "✨ " + FirstNumber(b.Text);
                case "proposal-delete": return SwimLang.T("✕ Προτ.");
                case "proposal-make": return "✨";
                case "auto-prev": return "◀";
                case "auto-next": return "▶";
                case "auto-delete": return SwimLang.T("✕ Χεριά");
                case "undo": return SwimLang.T("Αναίρ.");
                case "panel": return SwimLang.T("Πίνακας");
                case "hide": return SwimLang.T("Απόκρυψη");
                default: return Shorten(b.Text);
            }
        }

        /// <summary>(2.17.0) The word under the sign of a sector whose short name is a sign only (💡, ✈, 🤖, 📷, ∠, ✨).</summary>
        private static string Caption(SwimBarButton b)
        {
            switch (b.Id)
            {
                case "start-light": return SwimLang.T("Φως");
                case "auto-pilot": return SwimLang.T("Πιλότος");
                case "auto-race": return SwimLang.T("Αγώνας");
                case "t-snapshot": return SwimLang.T("Καρέ");
                case "t-angle": return SwimLang.T("Γωνία");
                case "proposal-make": return SwimLang.T("Πρόταση");
                case "proposal-review":
                case "race-review": return SwimLang.T("Έλεγχος");
                case "split":
                    string mark = SplitLabel(b.Text);
                    return mark.Length > 0 && char.IsDigit(mark[0]) ? SwimLang.T("Πέρασμα") : null;
                default: return null;
            }
        }

        /// <summary>
        /// The checkpoint that the next press marks (the text of the bar: «Πέρασμα 15m», «Επαφή 50m», «Τερματισμός», «Όλα τα
        /// περάσματα ✓»): its distance, «Τέρμα», or ✓.
        /// </summary>
        private static string SplitLabel(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            if (text.EndsWith("✓", StringComparison.Ordinal))
                return "✓";

            string[] words = text.Trim().Split(' ');
            string last = words[words.Length - 1];
            if (last.Length > 0 && char.IsDigit(last[0]))
                return last;

            return SwimLang.T("Τέρμα");
        }

        private static string FirstNumber(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            StringBuilder b = new StringBuilder();
            foreach (char ch in text)
            {
                if (char.IsDigit(ch))
                    b.Append(ch);
                else if (b.Length > 0)
                    break;
            }

            return b.ToString();
        }

        /// <summary>A name made short: its first word, at most 7 letters.</summary>
        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            string word = text.Trim().Split(' ')[0];
            return word.Length <= 7 ? word : word.Substring(0, 6) + ".";
        }

        /// <summary>The key of a button as it fits in the small badge of its sector.</summary>
        private static string KeyBadge(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            switch (key)
            {
                case "Backspace": return "⌫";
                case "Enter":
                case "Return": return "↵";
                case "Space": return "␣";
                case "Left": return "←";
                case "Right": return "→";
                case "Up": return "↑";
                case "Down": return "↓";
            }

            return key.Length <= 3 ? key : key.Substring(0, 3);
        }

        /// <summary>The tip of a sector: its whole name, its value and its key, then the help of the bar.</summary>
        private static string TipOf(SwimBarButton b)
        {
            bool keyInTip = !string.IsNullOrEmpty(b.Key) && b.Tip != null && b.Tip.Contains("(" + b.Key + ")");
            string head = b.Text + (string.IsNullOrEmpty(b.Value) ? "" : " · " + b.Value) + (string.IsNullOrEmpty(b.Key) || keyInTip ? "" : " (" + b.Key + ")");
            return string.IsNullOrEmpty(b.Tip) ? head : head + "\n" + b.Tip;
        }

        private string TipAt(string id)
        {
            if (id == null)
                return "";

            if (id == DotId)
            {
                string key = source.KeyOf(PlayerScreenCommands.SwimDial);
                return SwimLang.T("Το στρογγυλό κουμπί της ανάλυσης: κλικ για να ξαναφανεί.") + (string.IsNullOrEmpty(key) ? "" : " (" + key + ")");
            }

            if (id == CenterId && Standby)
            {
                string head = manager.DialName(this);
                return (head.Length > 0 ? head + ": " : "") + (manager.AnalysisBusy ?
                    SwimLang.T("η ανάλυση μένει στο βίντεο της ανάγνωσης ή του αυτόματου πιλότου μέχρι να τελειώσει.") :
                    group ? SwimLang.T("κλικ για την ανάλυση της ομάδας (τα κουμπιά ανοίγουν γύρω του).") :
                    SwimLang.T("κλικ για την ανάλυση αυτού του βίντεο (τα κουμπιά ανοίγουν γύρω του)."));
            }

            if (id == CenterId)
            {
                string head = group ? manager.DialName(this) + "\n" : "";
                return head + (expanded ? SwimLang.T("Κλείνει τα κουμπιά.") : SwimLang.T("Τα κουμπιά της ανάλυσης: κλικ για να ανοίξουν γύρω του, σύρσιμο για να μετακινηθεί."));
            }

            Item item = Items.FirstOrDefault(x => x.Button.Id == id);
            return item != null ? TipOf(item.Button) : "";
        }

        /// <summary>The race time in the middle (as the bar: seconds, minutes after the first one).</summary>
        private string Clock()
        {
            if (Standby)
                return "";

            double? now = manager.CurrentRaceTime;
            if (!now.HasValue)
                return "–";

            return now.Value >= 60 ? SwimReference.FormatTime(now.Value) : now.Value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>What decides the shape: hidden or not, open or not, and the buttons when open.</summary>
        private string Signature()
        {
            StringBuilder b = new StringBuilder();
            b.Append(Hidden).Append('|').Append(expanded).Append('|').Append(Standby).Append('|').Append(manager.StandbyText(this));
            if (expanded && !Hidden)
            {
                foreach (SwimBarButton x in Buttons())
                    b.Append('|').Append(x.Id).Append(':').Append(Label(x)).Append(':').Append(x.Dim).Append(':').Append((int)x.Look).Append(':').Append(x.Value).Append(':').Append(x.Key);
            }

            return b.ToString();
        }
        #endregion

        #region Mouse
        private string HitTest(Point p)
        {
            if (Hidden)
                return Within(p, center, S(DotSize) + 2) ? DotId : null;
            if (!expanded || wheel == null)
                return Within(p, center, CenterSize * drawScale) ? CenterId : null;

            float dx = p.X - center.X;
            float dy = p.Y - center.Y;
            double r = Math.Sqrt(dx * dx + dy * dy);
            if (r <= HubSize * drawScale / 2 + 1)
                return CenterId;

            double a = Math.Atan2(dy, dx);
            foreach (Item it in wheel.Items)
            {
                if (r < it.R0 - 1 || r > it.R1 + 1)
                    continue;
                if (Normalize(a - it.A0) <= it.A1 - it.A0)
                    return it.Button.Id;
            }

            return null;
        }

        private static double Normalize(double a)
        {
            a %= 2 * Math.PI;
            return a < 0 ? a + 2 * Math.PI : a;
        }

        private static bool Within(Point p, PointF c, float diameter)
        {
            float dx = p.X - c.X;
            float dy = p.Y - c.Y;
            return dx * dx + dy * dy <= diameter * diameter / 4;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            // As a click on the video: this screen is the active one and gets the keys.
            manager.DialClicked(this);
            string id = HitTest(e.Location);

            // The wheel moved into the video when it opened: the second click of a double click is not for the sector now under it.
            if (id != null && id != CenterId && id != DotId && (DateTime.UtcNow - openedAt).TotalMilliseconds < SystemInformation.DoubleClickTime)
                id = null;

            pressedId = id;
            downOnCenter = id == CenterId;
            dragging = false;
            downMouse = Control.MousePosition;
            downPlace = place;
            if (downOnCenter)
                Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (downOnCenter && (Control.MouseButtons & MouseButtons.Left) != 0)
            {
                Point now = Control.MousePosition;
                if (!dragging && (Math.Abs(now.X - downMouse.X) > S(4) || Math.Abs(now.Y - downMouse.Y) > S(4)))
                {
                    dragging = true;
                    tipId = null;
                    toolTip.SetToolTip(this, "");
                    Cursor = Cursors.SizeAll;
                    if (expanded)
                        SetExpanded(false);
                }

                if (dragging)
                {
                    place = Clamp(new PointF(downPlace.X + now.X - downMouse.X, downPlace.Y + now.Y - downMouse.Y));
                    Relayout();
                    return;
                }
            }

            string id = HitTest(e.Location);
            if (id != hoverId)
            {
                hoverId = id;
                Cursor = id != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            if (id != tipId)
            {
                tipId = id;
                toolTip.SetToolTip(this, TipAt(id));
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (dragging)
                return;

            hoverId = null;
            pressedId = null;
            tipId = null;
            toolTip.SetToolTip(this, "");
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;

            string pressed = pressedId;
            bool moved = dragging;
            pressedId = null;
            downOnCenter = false;
            dragging = false;
            Capture = false;
            if (moved)
            {
                Cursor = Cursors.Hand;
                SavePlace();
                Invalidate();
                return;
            }

            string id = HitTest(e.Location);
            Invalidate();
            if (id == null || id != pressed || !IsHandleCreated)
                return;

            if (id == DotId)
            {
                manager.SetDialHidden(false);
                return;
            }

            if (id == CenterId)
            {
                SetExpanded(!expanded);
                return;
            }

            Item item = Items.FirstOrDefault(x => x.Button.Id == id);
            if (item == null || item.Button.Click == null)
                return;

            // After the mouse up is over (as the bar): the action may open a dialog, hide the button or end the full screen.
            Action click = item.Button.Click;
            bool repeat = item.Button.Repeat;
            if (!repeat)
                SetExpanded(false);

            BeginInvoke(click);
            if (repeat)
                BeginInvoke((MethodInvoker)RefreshIfChanged);
        }

        private void StartFilter()
        {
            if (filtering)
                return;

            Application.AddMessageFilter(this);
            filtering = true;
        }

        private void StopFilter()
        {
            if (!filtering)
                return;

            Application.RemoveMessageFilter(this);
            filtering = false;
        }

        /// <summary>Open, a click anywhere else closes the buttons (the click still goes where it was made).</summary>
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201;
            const int WM_RBUTTONDOWN = 0x0204;
            const int WM_MBUTTONDOWN = 0x0207;
            const int WM_NCLBUTTONDOWN = 0x00A1;
            const int WM_NCRBUTTONDOWN = 0x00A4;
            if (!expanded || IsDisposed || !IsHandleCreated)
                return false;

            int msg = m.Msg;
            bool click = msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN || msg == WM_NCLBUTTONDOWN || msg == WM_NCRBUTTONDOWN;
            if (click && m.HWnd != Handle)
                BeginInvoke((MethodInvoker)(() => SetExpanded(false)));

            return false;
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
                bool standby = Standby;
                Draw(e.Graphics, Hidden, center, expanded, wheel, Clock(), hoverId, pressedId, Hidden ? scale : drawScale, standby, standby ? manager.StandbyText(this) : null);
            }
            catch (Exception ex)
            {
                log.Error("Error while drawing the round button of the swim analysis.", ex);
            }
        }

        /// <summary>
        /// The button: the dot when hidden; open, the wheel and its hub; closed, the round button (the race time; on another screen
        /// than the one of the analysis the number of its screen, waiting for a click).
        /// </summary>
        private static void Draw(Graphics g, bool hidden, PointF center, bool expanded, Wheel wheel, string clock, string hoverId, string pressedId, float scale,
            bool standby = false, string standbyText = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // The sizes of the fonts follow the drawing (the screen's own dpi is already in the points).
            float fs = scale / Math.Max(0.5f, g.DpiY / 96f);
            if (hidden)
            {
                RectangleF dot = Circle(center, DotSize * scale);
                Rim(g, dot, 0.22f);
                Face(g, Inset(dot, 0.22f), hoverId == DotId ? SwimTheme.Mix(SwimTheme.AccentDark, SwimTheme.Accent, 0.5) : SwimTheme.AccentDark, SwimTheme.Back);
                return;
            }

            bool hover = hoverId == CenterId;
            bool pressed = hover && pressedId == CenterId;
            if (expanded && wheel != null && !standby)
            {
                if (wheel.BandTo > wheel.BandFrom)
                    DrawBand(g, wheel, scale);
                foreach (Item it in wheel.Items)
                    DrawSector(g, center, it, it.Button.Id == hoverId, it.Button.Id == hoverId && it.Button.Id == pressedId, scale, fs);

                Item under = wheel.Items.FirstOrDefault(i => i.Button.Id == hoverId);
                RectangleF disk = Circle(center, HubSize * scale);
                Hub(g, disk, hover, pressed, false, scale);
                if (under != null)
                    Readout(g, disk, under.Button, fs);
                else
                {
                    RectangleF inner = Inset(disk, 0.2f);
                    RectangleF top = new RectangleF(inner.X, inner.Y, inner.Width, inner.Height * 0.62f);
                    RectangleF bottom = new RectangleF(inner.X, inner.Y + inner.Height * 0.58f, inner.Width, inner.Height * 0.36f);
                    Write(g, "✕", SwimTheme.Ui(15f * fs, FontStyle.Bold), top, hover ? Color.White : SwimTheme.Text, true);
                    Write(g, clock, SwimTheme.DisplayLight(9f * fs), bottom, SwimTheme.AccentLight, false);
                }

                return;
            }

            RectangleF closed = Circle(center, CenterSize * scale);
            Hub(g, closed, hover, pressed, standby, scale);
            RectangleF face = Inset(closed, 0.2f);
            if (standby)
                Write(g, string.IsNullOrEmpty(standbyText) ? "◉" : standbyText,
                    string.IsNullOrEmpty(standbyText) ? SwimTheme.Ui(13f * fs, FontStyle.Bold) : Fit(g, standbyText, face.Width, false, fs), face, hover ? SwimTheme.Text : SwimTheme.Dim, hover);
            else
                Write(g, clock, Fit(g, clock, face.Width, true, fs), face, Color.White, true);
        }

        /// <summary>
        /// The hub (and the closed button): a dark face with a fine hexagon grid, a thick light ring, a thin ring of the accent outside
        /// and four marks. Under the mouse the ring glows, pressed the face lights up; waiting (another screen) everything is quieter.
        /// </summary>
        private static void Hub(Graphics g, RectangleF r, bool hover, bool pressed, bool quiet, float scale)
        {
            Color middle = pressed ? SwimTheme.Mix(SwimTheme.AccentDark, SwimTheme.Accent, 0.35) :
                hover ? SwimTheme.Mix(SwimTheme.Raised, SwimTheme.AccentDark, 0.85) : SwimTheme.Mix(SwimTheme.Raised, SwimTheme.AccentDark, 0.4);
            Face(g, Inset(r, 0.04f), middle, SwimTheme.Back);
            HexGrid(g, Inset(r, 0.1f), Color.FromArgb(quiet ? 18 : 34, SwimTheme.AccentLight), r.Width / 9f);

            float w = r.Width;
            float thick = Math.Max(2.2f, w * 0.06f);
            Color ring = quiet ? SwimTheme.Dim : hover ? Color.White : SwimTheme.Mix(SwimTheme.Text, SwimTheme.Accent, 0.12);
            using (Pen halo = new Pen(Color.FromArgb(quiet ? 30 : hover ? 120 : 70, SwimTheme.Accent), thick * 2.2f))
                g.DrawEllipse(halo, Inset(r, 0.07f));
            using (Pen pen = new Pen(Color.FromArgb(quiet ? 150 : 240, ring), thick))
                g.DrawEllipse(pen, Inset(r, 0.07f));
            using (Pen thin = new Pen(Color.FromArgb(quiet ? 70 : 150, SwimTheme.Accent), Math.Max(1f, 1.1f * scale)))
                g.DrawEllipse(thin, Inset(r, 0.012f));

            // Four short marks on the thin ring (top, right, bottom, left).
            PointF c = new PointF(r.X + w / 2, r.Y + w / 2);
            using (Pen mark = new Pen(Color.FromArgb(quiet ? 90 : 200, SwimTheme.AccentLight), Math.Max(1.2f, 1.6f * scale)))
            {
                for (int i = 0; i < 4; i++)
                {
                    double a = i * Math.PI / 2;
                    g.DrawLine(mark, Polar(c, w * 0.43f, a), Polar(c, w * 0.5f, a));
                }
            }
        }

        /// <summary>A fine grid of hexagons inside a circle.</summary>
        private static void HexGrid(Graphics g, RectangleF r, Color color, float size)
        {
            if (size < 3 || r.Width < 6)
                return;

            GraphicsState state = g.Save();
            using (GraphicsPath clip = new GraphicsPath())
            using (Pen pen = new Pen(color, 1f))
            {
                clip.AddEllipse(r);
                g.SetClip(clip, CombineMode.Intersect);
                float h = (float)(Math.Sqrt(3) * size / 2);
                int col = 0;
                for (float x = r.Left - size; x <= r.Right + size; x += size * 1.5f, col++)
                {
                    float y0 = r.Top - h + (col % 2 == 0 ? 0 : h);
                    for (float y = y0; y <= r.Bottom + h; y += 2 * h)
                    {
                        PointF[] hex = new PointF[6];
                        for (int k = 0; k < 6; k++)
                            hex[k] = Polar(new PointF(x, y), size / 2, k * Math.PI / 3);
                        g.DrawPolygon(pen, hex);
                    }
                }
            }

            g.Restore(state);
        }

        /// <summary>The dashed band between the inner and the outer rings (the gap of the rings, over the span).</summary>
        private static void DrawBand(Graphics g, Wheel w, float scale)
        {
            float r = (w.BandFrom + w.BandTo) / 2;
            using (GraphicsPath band = SectorPath(w.Center, w.BandFrom, w.BandTo, w.Start, w.Start + w.Span, 0f))
            using (SolidBrush back = new SolidBrush(SwimTheme.Back))
                g.FillPath(back, band);
            using (Pen dash = new Pen(Color.FromArgb(150, SwimTheme.Accent), Math.Max(1f, 1.2f * scale)) { DashStyle = DashStyle.Dash })
                g.DrawArc(dash, w.Center.X - r, w.Center.Y - r, 2 * r, 2 * r, Deg(w.Start), Deg(w.Span));
        }

        /// <summary>
        /// A sector: a dark field lighter at its outer edge, a fine edge, the accent along its inner edge for the next to press (the
        /// danger colour for undo, brighter under the mouse), a thin line along its outer edge for a value already measured; its sign,
        /// its short name and its value in the middle, its key in a small badge at its outer corner.
        /// </summary>
        private static void DrawSector(Graphics g, PointF c, Item it, bool hover, bool pressed, float scale, float fs)
        {
            SwimBarButton b = it.Button;
            bool primary = b.Look == SwimBarLook.Primary;
            bool danger = b.Look == SwimBarLook.Danger;
            bool dim = b.Dim || b.Look == SwimBarLook.Quiet;
            float gap = SectorGap * scale;
            double mid = (it.A0 + it.A1) / 2;

            Color field = pressed ? SwimTheme.Mix(SwimTheme.AccentDark, SwimTheme.Accent, 0.3) :
                hover ? SwimTheme.Mix(SwimTheme.RaisedHover, SwimTheme.AccentDark, 0.7) :
                primary ? SwimTheme.Mix(SwimTheme.Raised, SwimTheme.AccentDark, 0.6) :
                danger ? SwimTheme.Mix(SwimTheme.Raised, SwimTheme.AmberDark, 0.35) :
                dim ? SwimTheme.Surface : SwimTheme.Raised;
            Color deep = SwimTheme.Mix(field, SwimTheme.Back, 0.55);
            using (GraphicsPath path = SectorPath(c, it.R0, it.R1, it.A0, it.A1, gap))
            {
                using (LinearGradientBrush fill = new LinearGradientBrush(Polar(c, it.R0 - 1, mid), Polar(c, it.R1 + 1, mid), deep, field))
                    g.FillPath(fill, path);
                using (Pen edge = new Pen(Color.FromArgb(hover ? 210 : dim ? 70 : 130, hover ? SwimTheme.Accent : SwimTheme.Border), 1f))
                    g.DrawPath(edge, path);
            }

            // The accent along the inner edge.
            if (primary || danger || hover)
            {
                Color accent = danger ? SwimTheme.Amber : hover ? SwimTheme.AccentLight : SwimTheme.Accent;
                float t = Math.Max(2f, 3f * scale);
                float r = it.R0 + t / 2 + 1;
                double inset = (gap + 2) / (2.0 * r);
                if (it.A1 - it.A0 > 2 * inset)
                {
                    using (Pen pen = new Pen(Color.FromArgb(hover || primary ? 235 : 190, accent), t))
                        g.DrawArc(pen, c.X - r, c.Y - r, 2 * r, 2 * r, Deg(it.A0 + inset), Deg(it.A1 - it.A0 - 2 * inset));
                }
            }

            // A value already measured: a thin line along the outer edge.
            if (!string.IsNullOrEmpty(b.Value))
            {
                float r = it.R1 - Math.Max(1.5f, 2f * scale);
                double inset = (gap + 6) / (2.0 * r);
                if (it.A1 - it.A0 > 2 * inset)
                {
                    using (Pen pen = new Pen(Color.FromArgb(dim ? 70 : 170, SwimTheme.AccentPale), Math.Max(1f, 1.4f * scale)))
                        g.DrawArc(pen, c.X - r, c.Y - r, 2 * r, 2 * r, Deg(it.A0 + inset), Deg(it.A1 - it.A0 - 2 * inset));
                }
            }

            Color text = dim && !hover ? SwimTheme.Faint : SwimTheme.Text;
            Color sign = dim && !hover ? SwimTheme.Faint : hover ? Color.White : primary ? SwimTheme.AccentPale : SwimTheme.AccentLight;
            float rm = (it.R0 + it.R1) / 2;
            PointF m = Polar(c, rm, mid);
            float width = Math.Min((float)((it.A1 - it.A0) * rm) - gap - 6 * scale, 2.2f * (it.R1 - it.R0));
            string label = it.Label ?? "";

            if (it.Inner)
            {
                RectangleF box = new RectangleF(m.X - width / 2, m.Y - (it.R1 - it.R0) / 2, width, it.R1 - it.R0);
                Font font = label.Length <= 2 ? SwimTheme.Ui(11f * fs, FontStyle.Bold) : FitSize(g, label, width, new float[] { 7.5f, 7f, 6.5f, 6f }, fs, true);
                Write(g, label, font, box, text, !dim || hover);
                return;
            }

            // The sign: drawn; else the short name when it is a sign (💡 ✈ 🤖 📷 ✨, the distance of a checkpoint) with a word under
            // it; else a short name of one or two letters alone (✓, −5); else none, the name only.
            string caption = Caption(b);
            bool drawn = HasIcon(b.Id);
            string signText = null;
            string name = label;
            if (caption != null)
            {
                signText = label;
                name = caption;
            }
            else if (!drawn && label.Length > 0 && label.Length <= 2)
            {
                signText = label;
                name = null;
            }

            float icon = 18f * scale;
            string value = string.IsNullOrEmpty(b.Value) ? null : b.Value;
            Font nameFont = name != null ? FitSize(g, name, width, new float[] { 8f, 7.5f, 7f, 6.5f, 6f }, fs, true) : null;
            Font valueFont = SwimTheme.Ui(7f * fs, FontStyle.Regular);
            int nameHeight = name != null ? TextRenderer.MeasureText(g, name.Length > 0 ? name : " ", nameFont, Size.Empty, TextFormatFlags.NoPadding).Height : 0;
            int valueHeight = value != null ? TextRenderer.MeasureText(g, value, valueFont, Size.Empty, TextFormatFlags.NoPadding).Height : 0;
            bool withSign = drawn || signText != null;
            float total = (withSign ? icon + 3 * scale : 0) + nameHeight + (value != null ? valueHeight + 1 : 0);
            float y = m.Y - total / 2;
            if (withSign)
            {
                if (drawn)
                    Icon(g, b.Id, new RectangleF(m.X - icon / 2, y, icon, icon), sign, Math.Max(1.3f, 1.7f * scale));
                else
                {
                    Font signFont = signText.Length <= 2 ? SwimTheme.Ui(13f * fs, FontStyle.Bold) : FitSize(g, signText, width, new float[] { 11f, 10f, 9f, 8f }, fs, true);
                    Write(g, signText, signFont, new RectangleF(m.X - width / 2, y, width, icon), sign, !dim || hover);
                }

                y += icon + 3 * scale;
            }

            if (name != null)
            {
                Write(g, name, nameFont, new RectangleF(m.X - width / 2, y, width, nameHeight), text, !dim || hover);
                y += nameHeight;
            }

            if (value != null)
                Write(g, value, valueFont, new RectangleF(m.X - width / 2, y + 1, width, valueHeight), dim ? SwimTheme.Faint : SwimTheme.AccentPale, false);

            // The key in a small badge at the outer corner (the end of the sector, clockwise).
            string key = KeyBadge(b.Key);
            if (key != null && (it.A1 - it.A0) * it.R1 > 40 * scale)
            {
                float bw = Math.Max(11f * scale, TextRenderer.MeasureText(g, key, SwimTheme.Ui(6.5f * fs, FontStyle.Bold), Size.Empty, TextFormatFlags.NoPadding).Width + 5 * scale);
                float bh = 11f * scale;
                float rb = it.R1 - bh * 0.9f;
                double ab = it.A1 - (bw * 0.75f + gap) / rb;
                PointF pb = Polar(c, rb, ab);
                RectangleF badge = new RectangleF(pb.X - bw / 2, pb.Y - bh / 2, bw, bh);
                using (GraphicsPath pill = Rounded(badge, bh / 2))
                {
                    using (SolidBrush back = new SolidBrush(SwimTheme.Mix(SwimTheme.Back, SwimTheme.AccentDark, 0.5)))
                        g.FillPath(back, pill);
                    using (Pen pen = new Pen(Color.FromArgb(dim ? 60 : 150, SwimTheme.Accent), 1f))
                        g.DrawPath(pen, pill);
                }

                Write(g, key, SwimTheme.Ui(6.5f * fs, FontStyle.Bold), badge, dim ? SwimTheme.Faint : SwimTheme.AccentLight, false);
            }
        }

        /// <summary>The whole name, the value and the key of the sector under the mouse, in the hub.</summary>
        private static void Readout(Graphics g, RectangleF disk, SwimBarButton b, float fs)
        {
            RectangleF inner = Inset(disk, 0.17f);
            Font font = SwimTheme.Ui(8f * fs, FontStyle.Bold);
            List<string> lines = Wrap(g, b.Text ?? "", font, inner.Width);
            if (lines.Count > 3)
            {
                font = SwimTheme.Ui(7f * fs, FontStyle.Bold);
                lines = Wrap(g, b.Text ?? "", font, inner.Width);
                if (lines.Count > 3)
                    lines = lines.Take(3).ToList();
            }

            string extra = (string.IsNullOrEmpty(b.Value) ? "" : b.Value) + (string.IsNullOrEmpty(b.Key) ? "" : (string.IsNullOrEmpty(b.Value) ? "" : "  ·  ") + KeyBadge(b.Key));
            Font extraFont = SwimTheme.Ui(7.5f * fs, FontStyle.Regular);
            int lh = TextRenderer.MeasureText(g, "Ag", font, Size.Empty, TextFormatFlags.NoPadding).Height;
            int eh = extra.Length > 0 ? TextRenderer.MeasureText(g, extra, extraFont, Size.Empty, TextFormatFlags.NoPadding).Height : 0;
            float total = lines.Count * lh + (eh > 0 ? eh + 2 : 0);
            float y = disk.Y + disk.Height / 2 - total / 2;
            foreach (string line in lines)
            {
                Write(g, line, font, new RectangleF(inner.X, y, inner.Width, lh), Color.White, true);
                y += lh;
            }

            if (eh > 0)
                Write(g, extra, extraFont, new RectangleF(inner.X, y + 2, inner.Width, eh), SwimTheme.AccentLight, false);
        }

        /// <summary>Words on lines that fit a width.</summary>
        private static List<string> Wrap(Graphics g, string text, Font font, float width)
        {
            List<string> lines = new List<string>();
            foreach (string part in text.Replace("\r", "").Split('\n'))
            {
                string line = "";
                foreach (string word in part.Split(' '))
                {
                    if (word.Length == 0)
                        continue;
                    string next = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && TextRenderer.MeasureText(g, next, font, Size.Empty, TextFormatFlags.NoPadding).Width > width)
                    {
                        lines.Add(line);
                        line = word;
                    }
                    else
                        line = next;
                }

                if (line.Length > 0)
                    lines.Add(line);
            }

            return lines;
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Min(2 * radius, Math.Min(r.Width, r.Height));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>(2.17.0) The marks and the tools with a drawn sign.</summary>
        private static bool HasIcon(string id)
        {
            switch (id)
            {
                case "start":
                case "reaction":
                case "t-reaction":
                case "block":
                case "t-feetoff":
                case "entry":
                case "t-entry":
                case "kick":
                case "t-kick":
                case "breakout":
                case "t-breakout":
                case "stroke":
                case "t-stroke":
                case "t-first":
                case "t-last":
                case "pushoff":
                case "t-pushoff":
                case "auto-pick":
                case "t-rotation":
                case "t-wall":
                case "t-signal":
                case "skeleton":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The sign of a mark, drawn with lines in a square: a flag (the start), a stopwatch (the reaction), the block with an arrow
        /// (leaving it), an arrow into the water (the entry), a wave (the dolphin kicks), an arrow out of the water (the breakout), an
        /// arc (a stroke), the wall with an arrow away (the push off), a swimmer (the athlete), a turning arrow (the rotation), the
        /// wall and a hand (the touch), the sound (the signal), a skeleton.
        /// </summary>
        private static void Icon(Graphics g, string id, RectangleF r, Color color, float width)
        {
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            Func<float, float, PointF> P = (u, v) => new PointF(x + u * w, y + v * h);
            using (Pen pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (SolidBrush brush = new SolidBrush(color))
            {
                switch (id)
                {
                    case "start":
                        g.DrawLine(pen, P(0.28f, 0.08f), P(0.28f, 0.95f));
                        g.FillPolygon(brush, new[] { P(0.32f, 0.1f), P(0.85f, 0.3f), P(0.32f, 0.52f) });
                        break;
                    case "reaction":
                    case "t-reaction":
                        g.DrawEllipse(pen, x + 0.14f * w, y + 0.2f * h, 0.72f * w, 0.72f * h);
                        g.DrawLine(pen, P(0.5f, 0.06f), P(0.5f, 0.2f));
                        g.DrawLine(pen, P(0.4f, 0.06f), P(0.6f, 0.06f));
                        g.DrawLine(pen, P(0.5f, 0.56f), P(0.68f, 0.4f));
                        break;
                    case "block":
                    case "t-feetoff":
                        g.DrawPolygon(pen, new[] { P(0.08f, 0.92f), P(0.08f, 0.62f), P(0.5f, 0.48f), P(0.5f, 0.92f) });
                        g.DrawLine(pen, P(0.56f, 0.32f), P(0.94f, 0.32f));
                        g.DrawLines(pen, new[] { P(0.8f, 0.18f), P(0.94f, 0.32f), P(0.8f, 0.46f) });
                        break;
                    case "entry":
                    case "t-entry":
                        g.DrawLine(pen, P(0.5f, 0.04f), P(0.5f, 0.6f));
                        g.DrawLines(pen, new[] { P(0.32f, 0.42f), P(0.5f, 0.6f), P(0.68f, 0.42f) });
                        Wave(g, pen, x, y + 0.78f * h, w, 0.12f * h);
                        break;
                    case "kick":
                    case "t-kick":
                        Wave(g, pen, x, y + 0.36f * h, w, 0.16f * h);
                        Wave(g, pen, x, y + 0.7f * h, w, 0.16f * h);
                        break;
                    case "breakout":
                    case "t-breakout":
                        g.DrawLine(pen, P(0.5f, 0.66f), P(0.5f, 0.06f));
                        g.DrawLines(pen, new[] { P(0.32f, 0.24f), P(0.5f, 0.06f), P(0.68f, 0.24f) });
                        Wave(g, pen, x, y + 0.8f * h, w, 0.12f * h);
                        break;
                    case "stroke":
                    case "t-stroke":
                    case "t-first":
                    case "t-last":
                        g.DrawArc(pen, x + 0.1f * w, y + 0.16f * h, 0.8f * w, 0.8f * h, 200, 250);
                        g.DrawLines(pen, new[] { P(0.62f, 0.86f), P(0.84f, 0.84f), P(0.86f, 0.62f) });
                        break;
                    case "pushoff":
                    case "t-pushoff":
                        g.DrawLine(pen, P(0.12f, 0.06f), P(0.12f, 0.94f));
                        g.DrawLine(pen, P(0.3f, 0.5f), P(0.92f, 0.5f));
                        g.DrawLines(pen, new[] { P(0.76f, 0.34f), P(0.92f, 0.5f), P(0.76f, 0.66f) });
                        break;
                    case "auto-pick":
                        g.DrawEllipse(pen, x + 0.36f * w, y + 0.06f * h, 0.28f * w, 0.28f * h);
                        g.DrawArc(pen, x + 0.16f * w, y + 0.44f * h, 0.68f * w, 0.8f * h, 180, 180);
                        break;
                    case "t-rotation":
                        g.DrawArc(pen, x + 0.12f * w, y + 0.12f * h, 0.76f * w, 0.76f * h, -60, 290);
                        g.DrawLines(pen, new[] { P(0.62f, 0.06f), P(0.72f, 0.2f), P(0.56f, 0.3f) });
                        break;
                    case "t-wall":
                        g.DrawLine(pen, P(0.86f, 0.04f), P(0.86f, 0.96f));
                        g.DrawLine(pen, P(0.1f, 0.5f), P(0.62f, 0.5f));
                        g.FillEllipse(brush, x + 0.6f * w, y + 0.38f * h, 0.2f * w, 0.24f * h);
                        break;
                    case "t-signal":
                        g.DrawPolygon(pen, new[] { P(0.08f, 0.38f), P(0.26f, 0.38f), P(0.46f, 0.18f), P(0.46f, 0.82f), P(0.26f, 0.62f), P(0.08f, 0.62f) });
                        g.DrawArc(pen, x + 0.4f * w, y + 0.3f * h, 0.3f * w, 0.4f * h, -60, 120);
                        g.DrawArc(pen, x + 0.4f * w, y + 0.14f * h, 0.5f * w, 0.72f * h, -60, 120);
                        break;
                    case "skeleton":
                        g.DrawEllipse(pen, x + 0.4f * w, y + 0.02f * h, 0.2f * w, 0.2f * h);
                        g.DrawLine(pen, P(0.5f, 0.24f), P(0.5f, 0.6f));
                        g.DrawLines(pen, new[] { P(0.2f, 0.44f), P(0.5f, 0.32f), P(0.8f, 0.44f) });
                        g.DrawLines(pen, new[] { P(0.28f, 0.96f), P(0.5f, 0.6f), P(0.72f, 0.96f) });
                        break;
                }
            }
        }

        /// <summary>A small wave over a width (two crests).</summary>
        private static void Wave(Graphics g, Pen pen, float x, float y, float w, float amplitude)
        {
            PointF[] points = new PointF[17];
            for (int i = 0; i < points.Length; i++)
            {
                double t = (double)i / (points.Length - 1);
                points[i] = new PointF(x + (float)(0.06 + 0.88 * t) * w, y + (float)(amplitude * Math.Sin(t * 4 * Math.PI)));
            }

            g.DrawLines(pen, points);
        }

        /// <summary>The metal rim of a round button: light at the top left, dark at the bottom right; 'share' of the width.</summary>
        internal static void Rim(Graphics g, RectangleF r, float share)
        {
            float t = Math.Max(1f, r.Width * share);
            RectangleF band = new RectangleF(r.X + t / 2, r.Y + t / 2, r.Width - t, r.Height - t);
            using (LinearGradientBrush metal = new LinearGradientBrush(r, Color.FromArgb(178, 196, 206), Color.FromArgb(24, 38, 48), LinearGradientMode.ForwardDiagonal))
            using (Pen pen = new Pen(metal, t))
                g.DrawEllipse(pen, band);
            using (Pen edge = new Pen(Color.FromArgb(120, 0, 0, 0), 1f))
                g.DrawEllipse(edge, r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
        }

        /// <summary>The face of a round button: its middle colour fading to the edge colour.</summary>
        internal static void Face(Graphics g, RectangleF r, Color middle, Color edge)
        {
            if (r.Width < 1 || r.Height < 1)
                return;

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(r);
                using (PathGradientBrush light = new PathGradientBrush(path))
                {
                    light.CenterColor = middle;
                    light.SurroundColors = new[] { edge };
                    light.CenterPoint = new PointF(r.X + r.Width * 0.45f, r.Y + r.Height * 0.4f);
                    g.FillPath(light, path);
                }
            }
        }

        private static RectangleF Inset(RectangleF r, float share)
        {
            float dx = r.Width * share;
            float dy = r.Height * share;
            return new RectangleF(r.X + dx, r.Y + dy, r.Width - 2 * dx, r.Height - 2 * dy);
        }

        /// <summary>The biggest of a few sizes with which each line of the text fits the width (the clock: the thin figures).</summary>
        private static Font Fit(Graphics g, string text, float width, bool clock, float fs)
        {
            float[] sizes = clock ? new float[] { 10.5f, 9.5f, 8.5f, 7.5f } : new float[] { 8f, 7.5f, 7f, 6.5f, 6f };
            Font font = null;
            foreach (float size in sizes)
            {
                font = clock ? SwimTheme.DisplayLight(size * fs) : SwimTheme.Ui(size * fs, FontStyle.Bold);
                if (Widest(g, text, font) <= width)
                    return font;
            }

            return font;
        }

        private static Font FitSize(Graphics g, string text, float width, float[] sizes, float fs, bool bold)
        {
            Font font = null;
            foreach (float size in sizes)
            {
                font = SwimTheme.Ui(size * fs, bold ? FontStyle.Bold : FontStyle.Regular);
                if (Widest(g, text, font) <= width)
                    return font;
            }

            return font;
        }

        private static int Widest(Graphics g, string text, Font font)
        {
            int widest = 0;
            foreach (string line in (text ?? "").Split('\n'))
                widest = Math.Max(widest, TextRenderer.MeasureText(g, line, font, Size.Empty, TextFormatFlags.NoPadding).Width);
            return widest;
        }

        /// <summary>Text in the middle of a box, on one line or more; with 'glow' a faint light of the accent around it.</summary>
        private static void Write(Graphics g, string text, Font font, RectangleF r, Color color, bool glow = false)
        {
            if (string.IsNullOrEmpty(text) || font == null)
                return;

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping;
            Size size = TextRenderer.MeasureText(g, text, font, Size.Empty, flags);
            Rectangle box = new Rectangle((int)Math.Round(r.Left), (int)Math.Round(r.Top + (r.Height - size.Height) / 2), (int)Math.Round(r.Width), size.Height);
            if (glow)
            {
                Color light = SwimTheme.Mix(SwimTheme.Back, SwimTheme.Accent, 0.45);
                foreach (Point o in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
                {
                    Rectangle moved = box;
                    moved.Offset(o);
                    TextRenderer.DrawText(g, text, font, moved, light, flags);
                }
            }

            TextRenderer.DrawText(g, text, font, box, color, flags);
        }
        #endregion

        #region Preview
        /// <summary>
        /// For the previews of the CI (tests/reel_preview, by reflection): the round button with fictional buttons over a drawn pool.
        /// 0: closed (left), pressed, waiting on screens 2 and 3 (the second under the mouse) and the dot of the hidden one (bottom
        /// right); 1: open in the middle (a sector under the mouse: its name in the hub); 2: open at its first place (bottom middle,
        /// a half wheel upwards); 3: open in the bottom right corner (moved into the video); 4: a turn of a training, open in the
        /// middle; 5: the check of the proposals of a race (✓ ✕ Τέλος, the strokes of the program).
        /// </summary>
        internal static Bitmap Preview(int variant)
        {
            const int w = 1280;
            const int h = 720;
            const float scale = 1.25f;
            Bitmap image = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(image))
            {
                DrawPool(g, w, h);
                if (variant == 0)
                {
                    Draw(g, false, new PointF(w * 0.12f, h * 0.5f), false, null, "23.45", null, null, scale);
                    Draw(g, false, new PointF(w * 0.30f, h * 0.5f), false, null, "1:02.37", CenterId, CenterId, scale);
                    Draw(g, false, new PointF(w * 0.50f, h * 0.5f), false, null, "", null, null, scale, true, "2\n" + SwimLang.T("οθόνη"));
                    Draw(g, false, new PointF(w * 0.70f, h * 0.5f), false, null, "", CenterId, null, scale, true, SwimLang.T("ομάδα") + "\n1·2·3");
                    Draw(g, false, new PointF(w * 0.88f, h * 0.5f), false, null, "–", CenterId, null, 1.8f);
                    Draw(g, true, new PointF(w - Margin * scale - DotSize * scale / 2, h - Margin * scale - DotSize * scale / 2), false, null, "", null, null, scale);
                }
                else
                {
                    List<SwimBarButton> buttons = variant == 4 ? PreviewTurnButtons() : variant == 5 ? PreviewCheckButtons() : PreviewButtons();
                    PointF place = variant == 2 ? new PointF(DefaultPlace.X * w, DefaultPlace.Y * h) :
                        variant == 3 ? new PointF(w * 0.95f, h * 0.92f) : new PointF(w / 2f, h / 2f);
                    Wheel wheel = Layout(buttons, place, new RectangleF(0, 0, w, h), scale);
                    string hover = variant == 1 ? "block" : variant == 4 ? "t-rotation" : variant == 5 ? "race-right" : variant == 2 ? "entry" : null;
                    Draw(g, false, wheel.Center, true, wheel, "23.45", hover, null, wheel.Scale);
                }
            }

            return image;
        }

        /// <summary>Fictional buttons of the start of a race, as the bars would give them (the order of Buttons()).</summary>
        private static List<SwimBarButton> PreviewButtons()
        {
            return new List<SwimBarButton>
            {
                new SwimBarButton { Id = "phase", Text = SwimLang.T("ΕΚΚ."), Tip = "" },
                new SwimBarButton { Id = "start", Text = SwimLang.T("Εκκίνηση"), Value = "✓", Key = "G" },
                new SwimBarButton { Id = "start-light", Text = SwimLang.T("💡 Εκκίνηση από το φως") },
                new SwimBarButton { Id = "auto-pilot", Text = SwimLang.T("✈ Αυτόματος πιλότος") },
                new SwimBarButton { Id = "reaction", Text = SwimLang.T("Αντίδραση"), Value = "0.62", Key = "R" },
                new SwimBarButton { Id = "block", Text = SwimLang.T("Αποδέσμευση"), Value = "0.71", Key = "F" },
                new SwimBarButton { Id = "entry", Text = SwimLang.T("Είσοδος χεριών"), Key = "E" },
                new SwimBarButton { Id = "kick", Text = SwimLang.T("Δελφινισμός"), Key = "K", Dim = true },
                new SwimBarButton { Id = "breakout", Text = SwimLang.T("Έξοδος"), Key = "B" },
                new SwimBarButton { Id = "stroke", Text = SwimLang.T("Χεριά"), Key = "C" },
                new SwimBarButton { Id = "split", Text = SwimLang.T("Πέρασμα ") + "15m", Key = "S", Look = SwimBarLook.Primary },
                new SwimBarButton { Id = "auto-pick", Text = SwimLang.T("Αθλητής") },
                new SwimBarButton { Id = "race-review", Text = SwimLang.T("✨ Έλεγχος ") + "12", Look = SwimBarLook.Primary },
                new SwimBarButton { Id = "undo", Text = SwimLang.T("Αναίρεση"), Key = "Backspace", Look = SwimBarLook.Danger },
                new SwimBarButton { Id = "panel", Text = SwimLang.T("Πίνακας ανάλυσης"), Look = SwimBarLook.Primary },
                new SwimBarButton { Id = "hide", Text = SwimLang.T("Απόκρυψη") }
            };
        }

        /// <summary>Fictional buttons of a turn of a training (the technical view).</summary>
        private static List<SwimBarButton> PreviewTurnButtons()
        {
            return new List<SwimBarButton>
            {
                new SwimBarButton { Id = "phase", Text = "ΣΤΡ." },
                new SwimBarButton { Id = "t-in5", Text = "Στα 5 μ. πριν τον τοίχο", Value = "2.41", Key = "1" },
                new SwimBarButton { Id = "t-last", Text = "Τελευταία χεριά", Value = "1.12", Key = "2" },
                new SwimBarButton { Id = "t-rotation", Text = "Αρχή της περιστροφής", Key = "3" },
                new SwimBarButton { Id = "t-wall", Text = "Επαφή στον τοίχο", Key = "4" },
                new SwimBarButton { Id = "t-pushoff", Text = "Αποκόλληση από τον τοίχο", Key = "5" },
                new SwimBarButton { Id = "t-kick", Text = "Δελφινισμός", Key = "K" },
                new SwimBarButton { Id = "t-breakout", Text = "Έξοδος", Key = "B" },
                new SwimBarButton { Id = "t-out", Text = "Στα 5/10/15 μ. μετά τον τοίχο", Key = "6" },
                new SwimBarButton { Id = "t-snapshot", Text = "📷 Καρέ", Key = "P" },
                new SwimBarButton { Id = "t-angle", Text = "∠ Γωνία" },
                new SwimBarButton { Id = "skeleton", Text = "Σκελετός" },
                new SwimBarButton { Id = "proposal-make", Text = "✨ Προτάσεις από τον σκελετό", Look = SwimBarLook.Primary },
                new SwimBarButton { Id = "undo", Text = "Αναίρεση", Key = "Backspace", Look = SwimBarLook.Danger },
                new SwimBarButton { Id = "panel", Text = "Πίνακας ανάλυσης" },
                new SwimBarButton { Id = "hide", Text = "Απόκρυψη" }
            };
        }

        /// <summary>Fictional buttons while the proposals of a race are checked, with the strokes made by the program.</summary>
        private static List<SwimBarButton> PreviewCheckButtons()
        {
            return new List<SwimBarButton>
            {
                new SwimBarButton { Id = "race-right", Text = "Σωστό", Key = "Enter", Look = SwimBarLook.Primary },
                new SwimBarButton { Id = "race-wrong", Text = "Λάθος: διόρθωσέ το", Key = "X" },
                new SwimBarButton { Id = "race-end", Text = "Τέλος ελέγχου" },
                new SwimBarButton { Id = "phase", Text = "ΚΟΛ." },
                new SwimBarButton { Id = "stroke", Text = "Χεριά", Value = "18", Key = "C" },
                new SwimBarButton { Id = "split", Text = "Πέρασμα " + "25m", Value = "12.84", Key = "S", Look = SwimBarLook.Primary },
                new SwimBarButton { Id = "race-review", Text = "✨ Έλεγχος " + "7", Look = SwimBarLook.Primary },
                new SwimBarButton { Id = "auto-race", Text = "🤖 Όλος ο αγώνας" },
                new SwimBarButton { Id = "auto-pick", Text = "Αθλητής" },
                new SwimBarButton { Id = "auto-prev", Text = "◀ " + "Προηγούμενη χεριά" },
                new SwimBarButton { Id = "auto-next", Text = "▶ " + "Επόμενη χεριά" },
                new SwimBarButton { Id = "auto-delete", Text = "✕ Σβήσιμο της χεριάς" },
                new SwimBarButton { Id = "undo", Text = "Αναίρεση", Key = "Backspace", Look = SwimBarLook.Danger },
                new SwimBarButton { Id = "panel", Text = "Πίνακας ανάλυσης" },
                new SwimBarButton { Id = "hide", Text = "Απόκρυψη" }
            };
        }

        /// <summary>A pool drawn for the previews: water and lane ropes.</summary>
        private static void DrawPool(Graphics g, int w, int h)
        {
            using (LinearGradientBrush water = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(18, 92, 132), Color.FromArgb(8, 54, 86), LinearGradientMode.Vertical))
                g.FillRectangle(water, 0, 0, w, h);

            using (Pen rope = new Pen(Color.FromArgb(200, 225, 235, 240), 3f))
            {
                for (int y = h / 8; y < h; y += h / 8)
                    g.DrawLine(rope, 0, y, w, y + 6);
            }
        }
        #endregion
    }
}
