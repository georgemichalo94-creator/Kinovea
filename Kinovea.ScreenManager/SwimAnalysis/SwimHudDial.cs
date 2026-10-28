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
    /// The buttons of the analysis in full screen (2.9.5). The full screen keeps no room beside the image for the bars (SwimVideoBar),
    /// so a round button over the video takes their place. A click opens around it the buttons of the bars, each a small round button
    /// with a short name (the tip has its whole name, its key and its help): the phase shown, the check of the proposals, the marks of
    /// the phase and their tools, undo, the window of the analysis and «Απόκρυψη». A drag moves it anywhere on the video (its place is
    /// kept); hidden, a small dot in the corner brings it back (so does the key of SwimDial).
    /// Since 2.14.0 every screen with a video has one (several cameras of the same swimmer, also with the big screens): the one of the
    /// screen of the analysis has the race time and the buttons; the others wait with the number of their screen, and a click takes
    /// the analysis to their video and opens its buttons. Drawn as a lens: a metal rim, a dark face, fine marks and a glowing ring
    /// (an own design). It never takes the keyboard focus: the keys of the analysis keep going to the video.
    /// </summary>
    internal sealed class SwimHudDial : Control, IMessageFilter
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // Sizes at 96 dpi.
        private const float CenterSize = 56;
        private const float ItemSize = 50;
        private const float Gap = 6;
        private const float DotSize = 14;
        private const float Margin = 6;

        /// <summary>
        /// The place of the centre at first, a fraction of the video area: bottom middle, under the map of the HUD (the buttons open
        /// upwards, without moving it).
        /// </summary>
        private static readonly PointF DefaultPlace = new PointF(0.5f, 0.95f);

        private const string CenterId = "dial";
        private const string DotId = "dial-dot";

        /// <summary>A small round button around the centre: the button of the bars, its short name, its centre.</summary>
        private sealed class Item
        {
            public SwimBarButton Button;
            public string Label;
            public PointF Center;
        }

        private readonly SwimAnalysisManager manager;

        /// <summary>(2.14.0) The round button of a group of screens: in the middle of the group, over its screens.</summary>
        private readonly bool group;

        /// <summary>(2.14.0) Where the area is in the control the button is on (the group: the screens of the group in their common parent).</summary>
        private Point origin;

        /// <summary>A left bar that is never shown: the buttons of the bars (DialButtons) and the phase chosen on the round button.</summary>
        private readonly SwimVideoBar source;
        private readonly ToolTip toolTip = new ToolTip { InitialDelay = 500, ReshowDelay = 100, AutoPopDelay = 15000 };
        private List<Item> items = new List<Item>();
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

        /// <summary>The scale of the drawing: the one of the screen, smaller when the open buttons need it in a small area.</summary>
        private float drawScale = 1f;

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
                // The centre shows the time only when closed (a cross when open).
                clockShown = clock;
                if (!expanded && !Hidden)
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
        /// The bounds and the shape of the control: the centre (or the dot when hidden), and the small buttons around it when open.
        /// The shape is the circles only, so the video shows between and around them.
        /// </summary>
        private void Relayout()
        {
            if (area.Width < 1 || area.Height < 1 || IsDisposed)
                return;

            List<RectangleF> circles = new List<RectangleF>();
            List<Item> list = new List<Item>();
            PointF c;
            drawScale = scale;
            if (Hidden)
            {
                float d = S(DotSize);
                c = new PointF(area.Width - S(Margin) - d / 2, area.Height - S(Margin) - d / 2);
                circles.Add(Circle(c, d));
            }
            else
            {
                c = place;
                if (expanded)
                {
                    List<SwimBarButton> buttons = Buttons();
                    PointF shifted;
                    PointF[] spots = RingLayout(buttons.Count, place, new RectangleF(0, 0, area.Width, area.Height), scale, out shifted, out drawScale);
                    c = shifted;
                    for (int i = 0; i < buttons.Count; i++)
                        list.Add(new Item { Button = buttons[i], Label = Label(buttons[i]), Center = spots[i] });
                }

                circles.Add(Circle(c, CenterSize * drawScale));
                foreach (Item it in list)
                    circles.Add(Circle(it.Center, ItemSize * drawScale));
            }

            // Whole pixels around the circles, one more for their edge.
            Rectangle bounds = Rectangle.FromLTRB(
                (int)Math.Floor(circles.Min(r => r.Left)) - 1,
                (int)Math.Floor(circles.Min(r => r.Top)) - 1,
                (int)Math.Ceiling(circles.Max(r => r.Right)) + 1,
                (int)Math.Ceiling(circles.Max(r => r.Bottom)) + 1);

            center = new PointF(c.X - bounds.Left, c.Y - bounds.Top);
            foreach (Item it in list)
                it.Center = new PointF(it.Center.X - bounds.Left, it.Center.Y - bounds.Top);
            items = list;

            Region next;
            using (GraphicsPath path = new GraphicsPath(FillMode.Winding))
            {
                foreach (RectangleF r in circles)
                    path.AddEllipse(r.X - bounds.Left - 1, r.Y - bounds.Top - 1, r.Width + 2, r.Height + 2);
                next = new Region(path);
            }

            // In the control it is on (the area of a group is not at its corner).
            bounds.Offset(origin);
            if (Bounds != bounds)
                Bounds = bounds;
            Region = next;
            if (shape != null)
                shape.Dispose();
            shape = next;
            Invalidate();
        }

        /// <summary>
        /// The centres of n round buttons around a centre in an area: rings from the inside out, the first button at the top and the
        /// others clockwise. Near an edge the rings become arcs turned to the middle of the area, or the whole moves into the area
        /// (its centre in 'shifted'): of these the one whose buttons are nearest to the centre. In a small area (five screens in full
        /// screen) the buttons are smaller ('layoutScale'), so that all of them fit.
        /// </summary>
        internal static PointF[] RingLayout(int n, PointF center, RectangleF area, float scale, out PointF shifted, out float layoutScale)
        {
            shifted = center;
            layoutScale = scale;
            if (n <= 0)
                return new PointF[0];

            float s = scale;
            float room = Math.Min(area.Width, area.Height) / 2 - Margin * scale;
            float needed = OuterRadius(n, First(s), Step(s)) + ItemSize * s / 2;
            if (needed > room)
                s = Math.Max(scale * 0.6f, scale * room / needed);
            layoutScale = s;

            float d = ItemSize * s;
            float step = Step(s);
            float first = First(s);
            float margin = Margin * s;
            RectangleF inside = RectangleF.FromLTRB(area.Left + margin + d / 2, area.Top + margin + d / 2, area.Right - margin - d / 2, area.Bottom - margin - d / 2);
            double toMiddle = Math.Atan2(area.Top + area.Height / 2 - center.Y, area.Left + area.Width / 2 - center.X);
            PointF[] best = null;
            double bestReach = double.MaxValue;
            foreach (double span in new double[] { 2 * Math.PI, 1.5 * Math.PI, Math.PI, 0.75 * Math.PI, 0.6 * Math.PI })
            {
                PointF[] spots = Rings(n, center, span, toMiddle, first, step);
                if (!spots.All(p => p.X >= inside.Left && p.X <= inside.Right && p.Y >= inside.Top && p.Y <= inside.Bottom))
                    continue;

                double reach = Reach(spots, center);
                if (reach < bestReach - 0.5)
                {
                    best = spots;
                    bestReach = reach;
                }
            }

            // Whole rings around a centre moved into the area, when their buttons are nearer (a corner: a long fan otherwise).
            float outer = OuterRadius(n, first, step) + d / 2 + margin;
            PointF moved = new PointF(Between(center.X, area.Left + outer, area.Right - outer), Between(center.Y, area.Top + outer, area.Bottom - outer));
            PointF[] around = Rings(n, moved, 2 * Math.PI, 0, first, step);
            if (best == null || Reach(around, center) < bestReach - 0.5)
            {
                shifted = moved;
                return around;
            }

            return best;
        }

        private static float First(float s)
        {
            return (CenterSize / 2 + Gap + ItemSize / 2 + 2) * s;
        }

        private static float Step(float s)
        {
            return (ItemSize + Gap) * s;
        }

        /// <summary>How far the farthest button is from the centre.</summary>
        private static double Reach(PointF[] spots, PointF center)
        {
            double reach = 0;
            foreach (PointF p in spots)
                reach = Math.Max(reach, Math.Sqrt((p.X - center.X) * (p.X - center.X) + (p.Y - center.Y) * (p.Y - center.Y)));
            return reach;
        }

        /// <summary>The buttons on rings (a whole circle) or on arcs of 'span' turned to 'direction', the inner ring filled first.</summary>
        private static PointF[] Rings(int n, PointF c, double span, double direction, float first, float step)
        {
            PointF[] spots = new PointF[n];
            bool whole = span >= 2 * Math.PI - 1e-6;
            int done = 0;
            for (int ring = 0; done < n; ring++)
            {
                double r = first + ring * step;
                int capacity = whole ? (int)Math.Floor(2 * Math.PI * r / step) : (int)Math.Floor(span * r / step) + 1;
                int k = Math.Min(n - done, Math.Max(1, capacity));
                double used = k > 1 ? (k - 1) * step / r : 0;
                for (int i = 0; i < k; i++)
                {
                    double a = whole ? -Math.PI / 2 + i * 2 * Math.PI / k : direction - used / 2 + (k > 1 ? i * used / (k - 1) : 0);
                    spots[done + i] = new PointF(c.X + (float)(r * Math.Cos(a)), c.Y + (float)(r * Math.Sin(a)));
                }

                done += k;
            }

            return spots;
        }

        /// <summary>The radius of the last ring of n buttons on whole rings.</summary>
        private static float OuterRadius(int n, float first, float step)
        {
            int done = 0;
            float r = first;
            for (int ring = 0; done < n; ring++)
            {
                r = first + ring * step;
                done += Math.Max(1, (int)Math.Floor(2 * Math.PI * r / step));
            }

            return r;
        }

        private static RectangleF Circle(PointF c, float d)
        {
            return new RectangleF(c.X - d / 2, c.Y - d / 2, d, d);
        }
        #endregion

        #region Buttons
        /// <summary>
        /// The small buttons around the centre in their order: the phase shown; the check of the proposals while it runs (the next to
        /// press); the marks of the phase, their tools and the other buttons of the bars; «Απόκρυψη».
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
            list.Add(source.DialPhaseButton());
            list.AddRange(all.Where(b => IsCheck(b.Id)));
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

        /// <summary>The short name of a button on its small round button (the whole name is in its tip).</summary>
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

        /// <summary>The tip of a small button: its whole name, its value and its key, then the help of the bar.</summary>
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

            Item item = items.FirstOrDefault(x => x.Button.Id == id);
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
                    b.Append('|').Append(x.Id).Append(':').Append(Label(x)).Append(':').Append(x.Dim).Append(':').Append((int)x.Look).Append(':').Append(x.Value);
            }

            return b.ToString();
        }
        #endregion

        #region Mouse
        private string HitTest(Point p)
        {
            if (Hidden)
                return Within(p, center, S(DotSize) + 2) ? DotId : null;
            if (Within(p, center, CenterSize * drawScale))
                return CenterId;

            foreach (Item it in items)
            {
                if (Within(p, it.Center, ItemSize * drawScale))
                    return it.Button.Id;
            }

            return null;
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

            Item item = items.FirstOrDefault(x => x.Button.Id == id);
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
                Draw(e.Graphics, Hidden, center, expanded, items, Clock(), hoverId, pressedId, Hidden ? scale : drawScale, standby, standby ? manager.StandbyText(this) : null);
            }
            catch (Exception ex)
            {
                log.Error("Error while drawing the round button of the swim analysis.", ex);
            }
        }

        /// <summary>
        /// The button: the dot when hidden; else the small buttons and the centre (the race time, a cross when open; on another screen
        /// than the one of the analysis the number of its screen, waiting for a click).
        /// </summary>
        private static void Draw(Graphics g, bool hidden, PointF center, bool expanded, List<Item> items, string clock, string hoverId, string pressedId, float scale,
            bool standby = false, string standbyText = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (hidden)
            {
                RectangleF dot = Circle(center, DotSize * scale);
                Rim(g, dot, 0.22f);
                Face(g, Inset(dot, 0.22f), hoverId == DotId ? SwimTheme.Mix(SwimTheme.AccentDark, SwimTheme.Accent, 0.5) : SwimTheme.AccentDark, SwimTheme.Back);
                return;
            }

            foreach (Item it in items)
                DrawItem(g, it, it.Button.Id == hoverId, it.Button.Id == hoverId && it.Button.Id == pressedId, scale);

            bool hover = hoverId == CenterId;
            bool pressed = hover && pressedId == CenterId;
            RectangleF disk = Circle(center, CenterSize * scale);
            Ring ring = standby ? Ring.Quiet : Ring.Primary;
            Lens(g, disk, ring, hover, pressed, true);

            RectangleF inner = Inset(disk, 0.22f);
            if (expanded && !standby)
                Write(g, "✕", SwimTheme.Ui(13f, FontStyle.Bold), inner, SwimTheme.Text, true);
            else if (standby)
                Write(g, string.IsNullOrEmpty(standbyText) ? "◉" : standbyText,
                    string.IsNullOrEmpty(standbyText) ? SwimTheme.Ui(13f, FontStyle.Bold) : Fit(g, standbyText, inner.Width, false), inner, hover ? SwimTheme.Text : SwimTheme.Dim, hover);
            else
                Write(g, clock, Fit(g, clock, inner.Width, true), inner, Color.White, true);
        }

        /// <summary>How bright the rings of a round button are.</summary>
        private enum Ring
        {
            Normal,
            Primary,
            Danger,
            Quiet,
            Dim
        }

        /// <summary>A small round button: the lens of the centre, smaller; its ring after its look; lighter under the mouse.</summary>
        private static void DrawItem(Graphics g, Item it, bool hover, bool pressed, float scale)
        {
            SwimBarButton b = it.Button;
            Ring ring = b.Dim ? Ring.Dim : b.Look == SwimBarLook.Primary ? Ring.Primary : b.Look == SwimBarLook.Danger ? Ring.Danger : Ring.Normal;
            RectangleF r = Circle(it.Center, ItemSize * scale);
            Lens(g, r, ring, hover, pressed, false);

            // A value already measured: a small light at the top, inside the ring.
            if (!string.IsNullOrEmpty(b.Value))
            {
                float d = 4f * scale;
                float y = r.Top + r.Height * 0.17f;
                using (SolidBrush halo = new SolidBrush(Color.FromArgb(b.Dim ? 30 : 90, SwimTheme.Accent)))
                    g.FillEllipse(halo, it.Center.X - d, y - d / 2, 2 * d, 2 * d);
                using (SolidBrush mark = new SolidBrush(b.Dim ? SwimTheme.Faint : SwimTheme.AccentPale))
                    g.FillEllipse(mark, it.Center.X - d / 2, y, d, d);
            }

            string label = it.Label ?? "";
            Color color = b.Dim && !hover ? SwimTheme.Faint : SwimTheme.Text;
            RectangleF inner = Inset(r, 0.19f);
            Font font = label.Length <= 2 ? SwimTheme.Ui(13f, FontStyle.Bold) : Fit(g, label, inner.Width, false);
            Write(g, label, font, inner, color, !b.Dim || hover);
        }

        /// <summary>
        /// A round button drawn as a lens: a metal rim, a dark face lit from the middle, a ring of fine marks, a glowing ring and a thin
        /// inner ring. Under the mouse the glow is stronger, pressed the face lights up.
        /// </summary>
        private static void Lens(Graphics g, RectangleF r, Ring ring, bool hover, bool pressed, bool centre)
        {
            Color glow = ring == Ring.Danger ? SwimTheme.Amber : ring == Ring.Primary ? SwimTheme.AccentLight : SwimTheme.Accent;
            int strength = ring == Ring.Dim ? 40 : ring == Ring.Quiet ? 70 : ring == Ring.Normal ? 120 : 170;
            if (hover)
                strength = Math.Min(255, strength + 70);

            Rim(g, r, 0.09f);
            RectangleF face = Inset(r, 0.09f);
            Color middle = pressed ? SwimTheme.Mix(SwimTheme.AccentDark, SwimTheme.Accent, 0.35) :
                hover ? SwimTheme.Mix(SwimTheme.Raised, SwimTheme.AccentDark, 0.8) : SwimTheme.Mix(SwimTheme.Raised, SwimTheme.AccentDark, 0.35);
            Face(g, face, middle, SwimTheme.Back);

            float w = r.Width;
            PointF c = new PointF(r.X + w / 2, r.Y + w / 2);

            // The fine marks, every 10° (every 30° longer), between the rim and the glowing ring.
            int marks = centre ? 36 : 24;
            using (Pen fine = new Pen(Color.FromArgb(Math.Min(255, strength / 2 + 20), glow), Math.Max(0.8f, w * 0.012f)))
            using (Pen bold = new Pen(Color.FromArgb(Math.Min(255, strength), glow), Math.Max(1f, w * 0.02f)))
            {
                for (int i = 0; i < marks; i++)
                {
                    double a = 2 * Math.PI * i / marks;
                    bool major = i % (marks / 12) == 0;
                    float outer = w * 0.41f, innerR = major ? w * 0.37f : w * 0.385f;
                    g.DrawLine(major ? bold : fine,
                        c.X + (float)(innerR * Math.Cos(a)), c.Y + (float)(innerR * Math.Sin(a)),
                        c.X + (float)(outer * Math.Cos(a)), c.Y + (float)(outer * Math.Sin(a)));
                }
            }

            // The glowing ring: wide and faint, then narrow and bright.
            RectangleF glowRing = Circle(c, w * 0.70f);
            using (Pen halo = new Pen(Color.FromArgb(Math.Min(255, strength / 3), glow), w * 0.07f))
                g.DrawEllipse(halo, glowRing);
            using (Pen line = new Pen(Color.FromArgb(Math.Min(255, strength + 40), glow), Math.Max(1.2f, w * 0.03f)))
                g.DrawEllipse(line, glowRing);

            // The thin inner ring of the centre.
            if (centre)
            {
                using (Pen thin = new Pen(Color.FromArgb(Math.Min(255, strength / 2), glow), Math.Max(0.8f, w * 0.012f)))
                    g.DrawEllipse(thin, Circle(c, w * 0.58f));
            }
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

        /// <summary>The biggest of a few sizes with which each line of the text fits the width.</summary>
        private static Font Fit(Graphics g, string text, float width, bool clock)
        {
            float[] sizes = clock ? new float[] { 10.5f, 9.5f, 8.5f, 7.5f } : new float[] { 8f, 7.5f, 7f, 6.5f, 6f };
            Font font = null;
            foreach (float size in sizes)
            {
                font = clock ? SwimTheme.DisplayLight(size) : SwimTheme.Ui(size, FontStyle.Bold);
                int widest = 0;
                foreach (string line in text.Split('\n'))
                    widest = Math.Max(widest, TextRenderer.MeasureText(g, line, font, Size.Empty, TextFormatFlags.NoPadding).Width);
                if (widest <= width)
                    return font;
            }

            return font;
        }

        /// <summary>Text in the middle of a square, on one line or more; with 'glow' a faint light of the accent around it.</summary>
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
        /// right); 1: open in the middle; 2: open at its first place (bottom middle, an arc upwards); 3: open in the bottom right corner
        /// (moved into the video).
        /// </summary>
        internal static Bitmap Preview(int variant)
        {
            const int w = 960;
            const int h = 540;
            Bitmap image = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(image))
            {
                DrawPool(g, w, h);
                List<Item> none = new List<Item>();
                if (variant == 0)
                {
                    Draw(g, false, new PointF(w * 0.12f, h * 0.5f), false, none, "23.45", null, null, 1f);
                    Draw(g, false, new PointF(w * 0.30f, h * 0.5f), false, none, "1:02.37", CenterId, CenterId, 1f);
                    Draw(g, false, new PointF(w * 0.50f, h * 0.5f), false, none, "", null, null, 1f, true, "2\n" + SwimLang.T("οθόνη"));
                    Draw(g, false, new PointF(w * 0.70f, h * 0.5f), false, none, "", CenterId, null, 1f, true, SwimLang.T("ομάδα") + "\n1·2·3");
                    Draw(g, false, new PointF(w * 0.88f, h * 0.5f), false, none, "–", CenterId, null, 1.5f);
                    Draw(g, true, new PointF(w - Margin - DotSize / 2, h - Margin - DotSize / 2), false, none, "", null, null, 1f);
                }
                else
                {
                    List<SwimBarButton> buttons = PreviewButtons();
                    PointF place = variant == 1 ? new PointF(w / 2f, h / 2f) :
                        variant == 2 ? new PointF(DefaultPlace.X * w, DefaultPlace.Y * h) : new PointF(w * 0.95f, h * 0.92f);
                    PointF shifted;
                    float layoutScale;
                    PointF[] spots = RingLayout(buttons.Count, place, new RectangleF(0, 0, w, h), 1f, out shifted, out layoutScale);
                    List<Item> items = new List<Item>();
                    for (int i = 0; i < buttons.Count; i++)
                        items.Add(new Item { Button = buttons[i], Label = Label(buttons[i]), Center = spots[i] });
                    Draw(g, false, shifted, true, items, "23.45", items.Count > 4 ? items[4].Button.Id : null, null, layoutScale);
                }
            }

            return image;
        }

        /// <summary>Fictional buttons of the start of a race, as the bars would give them.</summary>
        private static List<SwimBarButton> PreviewButtons()
        {
            return new List<SwimBarButton>
            {
                new SwimBarButton { Id = "phase", Text = SwimLang.T("ΕΚΚ.") },
                new SwimBarButton { Id = "race-review", Text = SwimLang.T("✨ Έλεγχος ") + "12", Look = SwimBarLook.Primary },
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
                new SwimBarButton { Id = "auto-prev", Text = "◀" },
                new SwimBarButton { Id = "auto-next", Text = "▶" },
                new SwimBarButton { Id = "auto-delete", Text = "✕" },
                new SwimBarButton { Id = "undo", Text = SwimLang.T("Αναίρεση"), Key = "Backspace", Look = SwimBarLook.Danger },
                new SwimBarButton { Id = "panel", Text = SwimLang.T("Πίνακας ανάλυσης"), Look = SwimBarLook.Primary },
                new SwimBarButton { Id = "hide", Text = SwimLang.T("Απόκρυψη") }
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
