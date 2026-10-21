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
using System.Media;
using System.Text;
using System.Windows.Forms;
using Kinovea.Services;
using Look = Kinovea.ScreenManager.SwimBarLook;

namespace Kinovea.ScreenManager
{
    /// <summary>The look of a button of the analysis on the video (SwimBarButton).</summary>
    internal enum SwimBarLook
    {
        Normal,
        Primary,
        Danger,
        Quiet
    }

    /// <summary>
    /// A button of the analysis on the video, without its place (2.9.5): drawn in the bars beside the image (SwimVideoBar, which
    /// places it) and around the round button of the full screen (SwimHudDial). One list of buttons for both.
    /// </summary>
    internal sealed class SwimBarButton
    {
        public string Id;
        public string Text;

        /// <summary>The value already measured (in the bar, before the key).</summary>
        public string Value;

        /// <summary>The key of its command (a badge in the bar); empty or null for a button that is not a key of the player.</summary>
        public string Key;
        public SwimBarLook Look;
        public bool Dim;
        public string Tip;
        public Action Click;

        /// <summary>The round button stays open after it (from stroke to stroke, from phase to phase).</summary>
        public bool Repeat;
    }

    /// <summary>
    /// A bar of the swim analysis on one side of the video, in the black area next to the image (the player screen keeps
    /// that room free, see SwimAnalysisManager.LayoutVideoBars).
    /// Left: the race time and the buttons of the marks of the phase, in the order of the events, as the tabs of the window.
    /// Right: the phase, the checkpoints, the last message and the window of the analysis.
    /// Drawn like the graphics on the video: dark cards with a colored edge. The bar never takes the keyboard focus,
    /// so the keys of the analysis keep going to the video.
    /// </summary>
    internal sealed class SwimVideoBar : Control
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Edge of the checkpoints card: the color of the athlete in the velocity chart of the video.</summary>
        private static readonly Color colorSplits = Color.FromArgb(255, 184, 0);

        // The tabs of the bars in the language of the analysis.
        private static string[] raceTabs
        {
            get { return new string[] { SwimLang.T("ΕΚΚ."), SwimLang.T("ΚΟΛ."), SwimLang.T("ΣΤΡ."), SwimLang.T("ΤΕΡ.") }; }
        }

        private static string[] raceTabTips
        {
            get { return new string[] { SwimLang.T("Εκκίνηση"), SwimLang.T("Κολύμβηση"), SwimLang.T("Στροφή"), SwimLang.T("Τερματισμός") }; }
        }

        private static string[] techTabs
        {
            get { return new string[] { SwimLang.T("ΕΚΚΙΝ."), SwimLang.T("ΣΤΡΟΦΗ"), SwimLang.T("ΕΡΓΑΛ.") }; }
        }

        private static string[] techTabTips
        {
            get { return new string[] { SwimLang.T("Φάσεις της εκκίνησης"), SwimLang.T("Φάσεις της στροφής"), SwimLang.T("Στιγμιότυπα και γωνίες") }; }
        }

        private static string StrokeTip { get { return SwimLang.T("Σε κάθε είσοδο χεριού. Ελεύθερο/ύπτιο: κάθε χέρι · πεταλούδα/πρόσθιο: κάθε κύκλος."); } }

        /// <summary>A part of the bar that reacts to the mouse: a button, a tab, a checkpoint.</summary>
        private sealed class Item
        {
            public string Id;
            public RectangleF Bounds;
            public string Tip;
            public Action Click;
        }

        /// <summary>A button of a mark: its command, its text and the value already measured.</summary>
        private sealed class Btn
        {
            public string Id;
            public PlayerScreenCommands Command;
            public string Text;
            public string Value;
            public bool Primary;
            public bool Dim;
            public string Tip;

            /// <summary>Instead of the command (a button that is not a key of the player).</summary>
            public Action Action;
        }

        private readonly SwimAnalysisManager manager;
        private readonly bool left;
        private readonly ToolTip toolTip = new ToolTip { InitialDelay = 600, ReshowDelay = 150, AutoPopDelay = 15000 };
        private List<Item> items = new List<Item>();
        private string hoverId;
        private string pressedId;
        private string tipId;
        private string signature = "";
        private Dictionary<PlayerScreenCommands, string> keys;
        private float scale = 1f;

        // Tab chosen with the mouse (-1: the tab follows the phase of the video) and the phase of the video then:
        // the choice holds until the video is in another phase.
        private int manualTab = -1;
        private int manualFrom = -1;

        public SwimVideoBar(SwimAnalysisManager manager, bool left)
        {
            this.manager = manager;
            this.left = left;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Black;
            ForeColor = SwimTheme.Text;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                toolTip.Dispose();

            base.Dispose(disposing);
        }

        protected override void WndProc(ref Message m)
        {
            // No context menu of the video on the bars (the right click would go up to the viewport).
            const int WM_CONTEXTMENU = 0x007B;
            if (m.Msg == WM_CONTEXTMENU)
                return;

            base.WndProc(ref m);
        }

        #region Refresh
        /// <summary>Redraw if what the bar shows has changed (timer of the analysis).</summary>
        public void RefreshIfChanged()
        {
            string current = Signature();
            if (current == signature)
                return;

            signature = current;
            Invalidate();
        }

        public void RefreshNow()
        {
            signature = Signature();
            Invalidate();
        }

        private string Signature()
        {
            SwimRace race = manager.Race;
            if (race == null)
                return "";

            StringBuilder b = new StringBuilder();
            b.Append(race.Id).Append('|').Append(manager.DataVersion).Append('|').Append(manager.CurrentVideoTimestamp).Append('|');
            b.Append(manager.LastStatus).Append('|').Append(manager.LastStatusIsWarning).Append('|');
            b.Append((int)manager.SyncState).Append('|').Append(manager.SyncMessage).Append('|').Append(race.SheetName).Append('|');
            b.Append(manager.PanelOpen).Append('|').Append(manager.TechnicalView).Append('|').Append((int)manager.PickTarget).Append('|');
            b.Append(manager.Settings.AutoStrokes).Append('|').Append(manager.Settings.IsConfigured).Append('|');
            b.Append(manager.AutoStrokesRunning).Append('|').Append(manager.StrokeSessionWall).Append('|');
            b.Append(race.Athlete).Append('|').Append(race.Lane).Append('|').Append(race.Style).Append('|');
            b.Append(race.Distance).Append('|').Append(race.Pool).Append('|').Append(race.Kind).Append('|');
            b.Append(manager.Settings.ReelOverlay).Append('|').Append(manager.PickingTrack).Append('|').Append(manager.TrackDescription).Append('|');
            b.Append(manager.Settings.VideoGraphics).Append('|').Append(manager.Settings.HudLayout).Append('|');
            b.Append(manager.SkeletonRunning).Append('|').Append(manager.SkeletonDescription).Append('|');
            b.Append(race.Proposals.Count).Append('|').Append(manager.Reviewing).Append('|');
            b.Append(manager.CurrentProposal != null ? manager.CurrentProposal.Id : "").Append('|');
            b.Append(manager.PilotWaiting).Append('|').Append(manager.PilotDoubtful).Append('|').Append(manager.StartLightRunning);
            return b.ToString();
        }
        #endregion

        #region Mouse
        private Item HitTest(Point p)
        {
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].Bounds.Contains(p))
                    return items[i];
            }

            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Item item = HitTest(e.Location);
            string id = item != null ? item.Id : null;
            if (id != hoverId)
            {
                hoverId = id;
                Cursor = item != null && item.Click != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            if (id != tipId)
            {
                tipId = id;
                toolTip.SetToolTip(this, item != null && item.Tip != null ? item.Tip : "");
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverId = null;
            pressedId = null;
            tipId = null;
            toolTip.SetToolTip(this, "");
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            // As a click on the video: this screen is the active one and gets the keys.
            manager.BarClicked();
            Item item = HitTest(e.Location);
            pressedId = item != null && item.Click != null ? item.Id : null;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;

            Item item = HitTest(e.Location);
            string pressed = pressedId;
            pressedId = null;
            Invalidate();

            // After the mouse up is over (capture released): the action may open a dialog or remove the bars.
            if (item != null && pressed != null && item.Id == pressed && item.Click != null && IsHandleCreated)
                BeginInvoke(item.Click);
        }

        private void Run(PlayerScreenCommands command)
        {
            if (!manager.ExecuteBarCommand(command))
                SystemSounds.Beep.Play();
        }

        private void ChooseTab(int tab, int automatic)
        {
            manualTab = tab == automatic ? -1 : tab;
            manualFrom = automatic;
            Invalidate();
        }
        #endregion

        #region Painting
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.Black);
            List<Item> list = new List<Item>();
            try
            {
                scale = Math.Max(1f, DeviceDpi / 96f);
                SwimRace race = manager.Race;
                if (race != null && Width >= S(120) && Height >= S(200))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    bool tech = race.IsTraining || manager.TechnicalView;
                    if (left)
                        PaintLeft(g, list, race, tech);
                    else
                        PaintRight(g, list, race, tech);
                }
            }
            catch (Exception ex)
            {
                log.Error("Error while drawing a bar of the swim analysis.", ex);
            }

            items = list;
        }

        private float S(float value)
        {
            return value * scale;
        }

        /// <summary>Left: time, tabs of the phases, buttons of the marks of the phase, undo.</summary>
        private void PaintLeft(Graphics g, List<Item> list, SwimRace race, bool tech)
        {
            float pad = S(8);
            RectangleF area = new RectangleF(pad, pad, Width - 2 * pad, Height - 2 * pad);

            RectangleF undo = new RectangleF(area.Left, area.Bottom - S(32), area.Width, S(32));
            DrawButton(g, list, UndoButton(), undo);

            float top = PaintLive(g, area, race, tech) + S(8);

            // The tabs follow the phase of the video; a click shows another phase until the video is in another phase.
            string[] names = tech ? techTabs : raceTabs;
            string[] tips = tech ? techTabTips : raceTabTips;
            int automatic;
            int tab = ShownTab(tech, out automatic);

            RectangleF strip = new RectangleF(area.Left, top, area.Width, S(26));
            DrawTabs(g, list, strip, names, tips, tab, automatic);
            top = strip.Bottom + S(6);

            string title;
            List<SwimBarButton> buttons = MarkButtons(race, tech, tab, out title);
            bool autoRow = !tech && manager.Settings.AutoStrokes && tab <= 2;
            PaintButtons(g, list, new RectangleF(area.Left, top, area.Width, undo.Top - S(8) - top), title, buttons, autoRow, tech);
        }

        /// <summary>Right: phase and values, checkpoints, last message, window of the analysis and sending.</summary>
        private void PaintRight(Graphics g, List<Item> list, SwimRace race, bool tech)
        {
            float pad = S(8);
            RectangleF area = new RectangleF(pad, pad, Width - 2 * pad, Height - 2 * pad);
            float bottom = PaintActions(g, list, area, race) - S(8);
            float top = PaintInfo(g, area, race, tech) + S(8);
            bottom = PaintStatus(g, list, area, top, bottom);
            if (!tech)
                PaintSplits(g, list, new RectangleF(area.Left, top, area.Width, bottom - top), race);
        }

        /// <summary>Card of the time: race time (or time of the start / turn in the technical analysis) and chips.</summary>
        private float PaintLive(Graphics g, RectangleF area, SwimRace race, bool tech)
        {
            string caption;
            string clock = "–";
            bool warn = false;
            List<SwimChip> chips = new List<SwimChip>();
            if (tech)
            {
                SwimTechPhase phase = manager.CurrentTechPhase;
                long? now = manager.CurrentVideoTimestamp;
                caption = SwimLang.T("ΤΕΧΝΙΚΗ ΑΝΑΛΥΣΗ");
                if (phase != null && now.HasValue)
                {
                    double t = manager.Timing.Seconds(now.Value, phase.Origin);
                    clock = (t < 0 ? "−" : "") + Math.Abs(t).ToString("0.00", CultureInfo.InvariantCulture);
                    caption = phase.Kind == SwimPhaseKind.Start ? SwimLang.T("ΑΠΟ ΤΟ ΣΗΜΑ") : SwimLang.T("ΑΠΟ ΤΗΝ ΕΠΑΦΗ");
                    SwimTechPhase owner;
                    int? next = SwimTechCalculator.NextOffset(race, manager.Timing, manager.TechPhases, now.Value, out owner);
                    if (next.HasValue)
                    {
                        string text = (next.Value < 0 ? "−5" : (owner != null && owner.Kind == SwimPhaseKind.Turn ? "+" : "") + next.Value.ToString(CultureInfo.InvariantCulture)) + race.Unit;
                        chips.Add(new SwimChip(SwimLang.T("T ΕΠΟΜΕΝΟ"), text, SwimTheme.Accent));
                    }

                    int kicks = phase.Kicks.Count(k => k <= t + 1e-6);
                    chips.Add(new SwimChip(SwimLang.T("ΔΕΛΦΙΝΙΣΜΟΙ"), kicks.ToString(CultureInfo.InvariantCulture), SwimTheme.Text));
                }
            }
            else
            {
                double? now = manager.CurrentRaceTime;
                if (now.HasValue)
                    clock = now.Value >= 60 ? SwimReference.FormatTime(now.Value) : now.Value.ToString("0.00", CultureInfo.InvariantCulture);

                warn = !race.StartTimestamp.HasValue;
                caption = warn ? SwimLang.T("ΧΩΡΙΣ ΣΗΜΑ ΕΚΚΙΝΗΣΗΣ") : SwimLang.T("ΧΡΟΝΟΣ ΑΓΩΝΑ");
                int next = race.NextCheckpoint();
                chips.Add(new SwimChip(SwimLang.T("ΕΠΟΜΕΝΟ"), next > 0 ? next.ToString(CultureInfo.InvariantCulture) + race.Unit : SwimLang.T("τέλος"), SwimTheme.Amber));
                chips.Add(new SwimChip(SwimLang.T("ΧΕΡΙΕΣ"), SwimCalculator.PressesSinceLastSplit(manager.Computed).ToString(CultureInfo.InvariantCulture), SwimTheme.Text));
            }

            float inner = S(10);
            float chipsTop = S(56);
            float chipHeight = S(28);
            float height = chips.Count > 0 ? ChipRows(g, chips, area.Left + inner, area.Top + chipsTop, area.Right - S(8), chipHeight, false) - area.Top + S(8) : S(58);
            RectangleF card = new RectangleF(area.Left, area.Top, area.Width, height);
            DrawCard(g, card, warn ? SwimTheme.Warn : SwimTheme.Accent);

            Font captionFont = SwimTheme.Ui(6.75f, FontStyle.Bold);
            string ev = EventText(race);
            Size es = TextRenderer.MeasureText(g, ev, captionFont, Size.Empty, TextFormatFlags.NoPadding);
            float evLeft = card.Right - S(8) - es.Width;
            Write(g, ev, captionFont, new RectangleF(evLeft, card.Top + S(7), es.Width + 2, es.Height + 2), SwimTheme.Dim, TextFormatFlags.Left | TextFormatFlags.SingleLine);
            Caption(g, caption, new RectangleF(card.Left + inner, card.Top + S(6), Math.Max(10, evLeft - card.Left - inner - S(6)), es.Height + 3),
                warn ? SwimTheme.Warn : SwimTheme.AccentLight);

            // The clock in thin glowing figures (HUD, 2.9.0).
            Font clockFont = SwimTheme.DisplayLight(23f);
            SizeF clockSize = g.MeasureString(clock, clockFont);
            float clockTop = card.Top + S(18) + (S(36) - clockSize.Height) / 2;
            SwimTheme.GlowText(g, clock, clockFont, Color.White, SwimTheme.Accent, new PointF(card.Left + S(5), clockTop), 1f);
            if (chips.Count > 0)
                ChipRows(g, chips, card.Left + inner, card.Top + chipsTop, card.Right - S(8), chipHeight, true);

            return card.Bottom;
        }

        /// <summary>Card with the buttons of the marks of a tab, one under the other (two columns if they don't fit).</summary>
        private void PaintButtons(Graphics g, List<Item> list, RectangleF space, string title, List<SwimBarButton> buttons, bool autoRow, bool skeletonRow = false)
        {
            if (buttons.Count == 0 || space.Height < S(70))
                return;

            float inner = S(10);
            float header = S(26);
            float spacing = S(5);
            float autoHeight = skeletonRow ? 2 * S(26) + S(4) + S(8) : autoRow ? S(26) + S(8) : 0;
            int n = buttons.Count;
            float room = space.Height - header - S(8) - autoHeight;
            int columns = 1;
            float bh = (room - (n - 1) * spacing) / n;
            if (bh < S(24))
            {
                columns = 2;
                int rows = (n + 1) / 2;
                bh = (room - (rows - 1) * spacing) / rows;
            }

            // Few buttons (swimming, finish): bigger, as the keys of a timing console.
            bh = Math.Max(S(20), Math.Min(S(n <= 3 ? 50 : 38), bh));
            int rowCount = (n + columns - 1) / columns;
            float height = header + rowCount * bh + (rowCount - 1) * spacing + autoHeight + S(8);
            RectangleF card = new RectangleF(space.Left, space.Top, space.Width, Math.Min(space.Height, height));
            DrawCard(g, card, SwimTheme.Accent);
            Caption(g, title, new RectangleF(card.Left + inner, card.Top + S(7), card.Width - inner - S(8), S(15)), SwimTheme.AccentLight);

            float x0 = card.Left + inner;
            float width = card.Right - S(8) - x0;
            float columnWidth = columns == 1 ? width : (width - spacing) / 2;
            float y0 = card.Top + header;
            for (int i = 0; i < n; i++)
            {
                int row = i / columns;
                int column = i % columns;
                RectangleF r = new RectangleF(x0 + column * (columnWidth + spacing), y0 + row * (bh + spacing), columnWidth, bh);
                if (r.Bottom > card.Bottom - S(2))
                    break;

                SwimBarButton b = buttons[i];
                DrawButton(g, list, b.Id, r, b.Text, columns == 1 ? b.Value : null, b.Key, b.Look, b.Dim, b.Tip, b.Click);
            }

            float ay = y0 + rowCount * (bh + spacing) - spacing + S(8);
            if (skeletonRow && ay + S(26) <= card.Bottom)
            {
                // Skeleton of the swimmer (one click) and its angles at the frame.
                float sq = S(28);
                List<SwimBarButton> skeleton = SkeletonButtons();
                RectangleF skel = new RectangleF(x0, ay, (width - sq - S(8)) / 2, S(26));
                DrawButton(g, list, skeleton[0], skel);
                RectangleF angles = new RectangleF(skel.Right + S(4), ay, skel.Width, S(26));
                DrawButton(g, list, skeleton[1], angles);
                DrawButton(g, list, skeleton[2], new RectangleF(angles.Right + S(4), ay, sq, S(26)));

                // Automatic proposals: check them one by one (Enter, Delete, Esc on the video), or propose from the skeleton.
                float py = ay + S(30);
                if (py + S(26) > card.Bottom)
                    return;

                List<SwimBarButton> proposals = ProposalButtons();
                if (proposals.Count == 3)
                {
                    float third = (width - 2 * S(4)) / 3;
                    for (int i = 0; i < 3; i++)
                        DrawButton(g, list, proposals[i], new RectangleF(x0 + i * (third + S(4)), py, third, S(26)));
                }
                else if (proposals.Count == 2)
                {
                    RectangleF review = new RectangleF(x0, py, width - sq - S(4), S(26));
                    DrawButton(g, list, proposals[0], review);
                    DrawButton(g, list, proposals[1], new RectangleF(review.Right + S(4), py, sq, S(26)));
                }
                else
                {
                    DrawButton(g, list, proposals[0], new RectangleF(x0, py, width, S(26)));
                }

                return;
            }

            if (!autoRow)
                return;

            // Automatic strokes: choose the swimmer again, go from stroke to stroke, delete the stroke of the frame.
            if (ay + S(26) > card.Bottom)
                return;

            float small = S(28);
            RectangleF pick = new RectangleF(x0, ay, width - 3 * (small + S(4)), S(26));
            // The long text when the bar is wide enough for it.
            List<SwimBarButton> auto = AutoStrokeButtons(false);
            if (TextRenderer.MeasureText(g, auto[0].Text, SwimTheme.Ui(8.25f, FontStyle.Bold), Size.Empty, TextFormatFlags.NoPadding).Width > pick.Width - S(16))
                auto[0].Text = AutoStrokeButtons(true)[0].Text;
            DrawButton(g, list, auto[0], pick);
            RectangleF prev = new RectangleF(pick.Right + S(4), ay, small, S(26));
            DrawButton(g, list, auto[1], prev);
            RectangleF next = new RectangleF(prev.Right + S(4), ay, small, S(26));
            DrawButton(g, list, auto[2], next);
            DrawButton(g, list, auto[3], new RectangleF(next.Right + S(4), ay, small, S(26)));
        }

        /// <summary>Phase of the race and its values: Δ with the reference, ΔΑΚ, automatic strokes (or the values of the technical analysis).</summary>
        private float PaintInfo(Graphics g, RectangleF area, SwimRace race, bool tech)
        {
            string value;
            List<SwimChip> chips = new List<SwimChip>();
            if (tech)
            {
                SwimTechPhase phase = manager.CurrentTechPhase;
                value = phase != null ? phase.Title : (race.IsTraining ? SwimLang.T("G: νέα εκκίνηση · W: νέα στροφή") : SwimLang.T("εκτός εκκίνησης / στροφής"));
                if (phase != null)
                {
                    string key = phase.Kind == SwimPhaseKind.Start ? "t15" : "out15";
                    double? v15 = phase.Value(key);
                    if (v15.HasValue)
                        chips.Add(new SwimChip(phase.Kind == SwimPhaseKind.Start ? "15" + race.Unit : "→ +15" + race.Unit, v15.Value.ToString("0.00", CultureInfo.InvariantCulture), SwimTheme.Green));
                }

                chips.Add(new SwimChip(SwimLang.T("ΣΤΙΓΜΙΟΤΥΠΑ"), race.Snapshots.Count.ToString(CultureInfo.InvariantCulture), SwimTheme.Text));
                chips.Add(new SwimChip(SwimLang.T("ΓΩΝΙΕΣ"), race.Angles.Count.ToString(CultureInfo.InvariantCulture), SwimTheme.Text));
            }
            else
            {
                value = PhaseName(race, manager.CurrentPhase);
                double? now = manager.CurrentRaceTime;
                SwimComputed c = manager.Computed;
                if (now.HasValue && c.Reference != null)
                {
                    SwimRow last = c.Rows.Where(r => r.Delta.HasValue && r.PassedAt(now.Value)).OrderBy(r => r.OnVideo).LastOrDefault();
                    if (last != null)
                        chips.Add(new SwimChip(SwimLang.T("Δ ") + last.D + race.Unit, SwimReference.FormatDelta(last.Delta.Value), last.Delta.Value > 0.0049 ? SwimTheme.Red : SwimTheme.Green));
                }

                if (now.HasValue)
                {
                    SwimBlock block = c.Blocks
                        .Where(b => b.Dak.HasValue && c.Rows.Any(r => r.D == b.To && r.PassedAt(now.Value)))
                        .OrderBy(b => b.To)
                        .LastOrDefault();
                    if (block != null)
                        chips.Add(new SwimChip(SwimLang.T("ΔΑΚ ") + block.From + "-" + block.To, block.Dak.Value.ToString("0.00", CultureInfo.InvariantCulture), SwimTheme.Amber));
                }

                string auto = AutoStrokesState(race);
                if (auto != null)
                    chips.Add(new SwimChip(SwimLang.T("ΑΥΤΟΜΑΤΕΣ ΧΕΡΙΕΣ"), auto, SwimTheme.Accent));
            }

            float inner = S(10);
            float chipsTop = S(48);
            float chipHeight = S(28);
            float height = chips.Count > 0 ? ChipRows(g, chips, area.Left + inner, area.Top + chipsTop, area.Right - S(8), chipHeight, false) - area.Top + S(8) : S(48);
            RectangleF card = new RectangleF(area.Left, area.Top, area.Width, height);
            DrawCard(g, card, SwimTheme.Amber);
            Caption(g, tech ? SwimLang.T("ΦΑΣΗ ΤΕΧΝΙΚΗΣ ΑΝΑΛΥΣΗΣ") : SwimLang.T("ΦΑΣΗ"), new RectangleF(card.Left + inner, card.Top + S(6), card.Width - inner - S(8), S(14)),
                SwimTheme.AccentLight);
            Write(g, value, SwimTheme.Display(13f), new RectangleF(card.Left + inner, card.Top + S(19), card.Width - inner - S(8), S(26)), SwimTheme.Accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (chips.Count > 0)
                ChipRows(g, chips, card.Left + inner, card.Top + chipsTop, card.Right - S(8), chipHeight, true);

            return card.Bottom;
        }

        /// <summary>
        /// The checkpoints: time, velocity and strokes, the next one in amber. A click on a checkpoint shows its frame.
        /// With many checkpoints, the ones around the next one.
        /// </summary>
        private void PaintSplits(Graphics g, List<Item> list, RectangleF card, SwimRace race)
        {
            if (card.Height < S(90))
                return;

            List<SwimRow> rows = manager.Computed.Rows;
            DrawCard(g, card, colorSplits);
            float inner = S(10);
            Caption(g, SwimLang.T("ΠΕΡΑΣΜΑΤΑ"), new RectangleF(card.Left + inner, card.Top + S(6), card.Width - inner - S(8), S(14)), SwimTheme.AccentLight);

            float rx = card.Left + S(7);
            float rw = card.Right - S(5) - rx;
            float colD = rx + S(5);
            float colT = rx + rw * 0.57f;
            float colV = rx + rw * 0.78f;
            float colS = rx + rw - S(5);
            RectangleF head = new RectangleF(rx, card.Top + S(21), rw, S(14));
            Font headFont = SwimTheme.Ui(6.5f, FontStyle.Bold);
            CellRight(g, SwimLang.T("ΧΡΟΝΟΣ"), headFont, colT, head, SwimTheme.Faint);
            CellRight(g, "M/S", headFont, colV, head, SwimTheme.Faint);
            CellRight(g, SwimLang.T("ΧΕΡ."), headFont, colS, head, SwimTheme.Faint);

            float rowHeight = S(21);
            float rowsTop = card.Top + S(37);
            int capacity = Math.Max(0, (int)((card.Bottom - S(6) - rowsTop) / rowHeight));
            if (rows.Count == 0 || capacity == 0)
                return;

            // (A checkpoint with an official time and no mark is still to mark: its time shows dimmed.)
            int next = rows.FindIndex(r => !r.Marked);
            int first = 0;
            if (rows.Count > capacity)
            {
                // The next checkpoint third from the bottom: the ones done before it, two still to mark after it.
                int anchor = next >= 0 ? next : rows.Count - 1;
                first = Math.Max(0, Math.Min(rows.Count - capacity, anchor - (capacity - Math.Min(3, capacity))));
            }

            Font timeFont = SwimTheme.Display(9.5f);
            Font cellFont = SwimTheme.Ui(8.25f, FontStyle.Regular);
            double? lastT = null;
            for (int i = 0; i < rows.Count; i++)
            {
                SwimRow row = rows[i];
                bool warn = row.T.HasValue && lastT.HasValue && row.T.Value <= lastT.Value + 1e-9;
                if (row.T.HasValue)
                    lastT = row.T.Value;
                if (i < first || i >= first + capacity)
                    continue;

                string id = "row" + row.D.ToString(CultureInfo.InvariantCulture);
                RectangleF r = new RectangleF(rx, rowsTop + (i - first) * rowHeight, rw, rowHeight - S(2));
                if (i == next)
                {
                    // HUD (2.9.0): the next checkpoint amber, with a glowing bar on its left.
                    SwimTheme.FillRounded(g, SwimTheme.NextRow, r, 3);
                    using (SolidBrush glow = new SolidBrush(Color.FromArgb(70, SwimTheme.Amber)))
                        g.FillRectangle(glow, r.Left, r.Top + S(2), S(4), r.Height - S(4));
                    using (SolidBrush bar = new SolidBrush(SwimTheme.Amber))
                        g.FillRectangle(bar, r.Left + S(1), r.Top + S(3), S(2), r.Height - S(6));
                }
                else if (hoverId == id && row.Marked)
                {
                    SwimTheme.FillRounded(g, SwimTheme.RaisedHover, r, 3);
                }
                else
                {
                    using (Pen line = new Pen(Color.FromArgb(40, SwimTheme.Accent), 1f))
                        g.DrawLine(line, r.Left + S(4), r.Bottom + S(1), r.Right - S(4), r.Bottom + S(1));
                }

                Color distanceColor = i == next ? SwimTheme.Amber : (row.Marked ? SwimTheme.Text : SwimTheme.Faint);
                Write(g, row.D.ToString(CultureInfo.InvariantCulture) + race.Unit, cellFont, new RectangleF(colD, r.Top, rw * 0.3f, r.Height), distanceColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                CellRight(g, row.T.HasValue ? SwimReference.FormatTime(row.T.Value) : "–", timeFont, colT, r,
                    warn ? SwimTheme.Red : (row.Marked ? Color.White : row.T.HasValue ? SwimTheme.Dim : SwimTheme.Faint));
                if (row.Vel.HasValue)
                    CellRight(g, row.Vel.Value.ToString("0.00", CultureInfo.InvariantCulture), cellFont, colV, r, row.IsFree ? SwimTheme.Text : SwimTheme.Dim);
                if (row.Strokes.HasValue)
                    CellRight(g, row.Strokes.Value.ToString("0.#", CultureInfo.InvariantCulture) + (row.StrokesTyped ? " ✓" : ""), cellFont, colS, r,
                        row.StrokesTyped ? SwimTheme.Green : SwimTheme.Text);

                if (row.Marked)
                {
                    int distance = row.D;
                    string tip = SwimLang.T("Μετάβαση του βίντεο στο πέρασμα των ") + distance.ToString(CultureInfo.InvariantCulture) + race.Unit + "." +
                        (row.Official ? SwimLang.T("\nΟ χρόνος είναι ο επίσημος των αποτελεσμάτων.") +
                            (row.MarkApart && row.MarkT.HasValue ? SwimLang.T("\n⚠ Το σημάδι στο βίντεο είναι στα ") + SwimReference.FormatTime(row.MarkT.Value) +
                                SwimLang.T(": έλεγξε το καρέ ή την εκκίνηση.") : "") : "") +
                        (warn ? SwimLang.T("\n⚠ Ο χρόνος δεν είναι μεγαλύτερος από τον προηγούμενο.") : "");
                    list.Add(new Item { Id = id, Bounds = r, Tip = tip, Click = () => manager.Seek(distance) });
                }
            }
        }

        /// <summary>Last message of the analysis, above the buttons. Returns the new bottom of the free space.</summary>
        private float PaintStatus(Graphics g, List<Item> list, RectangleF area, float top, float bottom)
        {
            string text = manager.LastStatus;
            if (string.IsNullOrEmpty(text))
                return bottom;

            bool warn = manager.LastStatusIsWarning;
            Font font = SwimTheme.Ui(8.25f, FontStyle.Regular);
            float inner = S(10);
            int width = (int)(area.Width - inner - S(8));
            if (width < 40)
                return bottom;

            Size size = TextRenderer.MeasureText(g, text, font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            int line = TextRenderer.MeasureText(g, "Ág", font, Size.Empty, TextFormatFlags.NoPadding).Height;
            float textHeight = Math.Min(size.Height, line * 5);
            float height = textHeight + S(16);
            if (bottom - height < top + S(40))
                return bottom;

            RectangleF card = new RectangleF(area.Left, bottom - height, area.Width, height);
            DrawCard(g, card, warn ? SwimTheme.Warn : SwimTheme.Faint);
            Write(g, text, font, new RectangleF(card.Left + inner, card.Top + S(8), width, textHeight), warn ? SwimTheme.Warn : SwimTheme.Dim,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            list.Add(new Item { Id = "status", Bounds = card, Tip = text });
            return card.Top - S(8);
        }

        /// <summary>Window of the analysis, sending to the Google Sheet and closing, at the bottom. Returns their top.</summary>
        private float PaintActions(Graphics g, List<Item> list, RectangleF area, SwimRace race)
        {
            RectangleF close = new RectangleF(area.Left, area.Bottom - S(24), area.Width, S(24));
            DrawButton(g, list, "close", close, SwimLang.T("✕  Κλείσιμο ανάλυσης"), null, null, Look.Quiet, false,
                SwimLang.T("Κρύβει τα κουμπιά και το παράθυρο της ανάλυσης (η ανάλυση είναι αποθηκευμένη).\n") +
                SwimLang.T("Εμφανίζονται ξανά με ένα πλήκτρο της ανάλυσης (π.χ. S) ή με το Ctrl+Shift+W."),
                () => manager.CloseAnalysis());

            string sheet = !string.IsNullOrEmpty(race.SheetName) ? " · " + race.SheetName : "";
            string sync = "● " + (manager.SyncMessage ?? "") + sheet;
            RectangleF line = new RectangleF(area.Left + S(4), close.Top - S(6) - S(16), area.Width - S(8), S(16));
            Write(g, sync, SwimTheme.Ui(7.5f, FontStyle.Regular), line, SyncColor(manager.SyncState),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            list.Add(new Item { Id = "sync", Bounds = line, Tip = (manager.SyncMessage ?? "") + sheet });

            bool configured = manager.Settings.IsConfigured;
            RectangleF sendRow = new RectangleF(area.Left, line.Top - S(3) - S(30), area.Width, S(30));
            RectangleF send = new RectangleF(sendRow.Left, sendRow.Top, (sendRow.Width - S(4)) * 0.62f, sendRow.Height);
            DrawButton(g, list, "send", send, SwimLang.T("Αποστολή τώρα"), null, null, Look.Normal, !configured,
                configured ? SwimLang.T("Στέλνει την ανάλυση στο Google Sheet τώρα.") : SwimLang.T("Δεν έχει ρυθμιστεί η σύνδεση με το Google Sheet: καρτέλα «Σύνδεση» του πίνακα."),
                () => manager.SendByUser(false));
            RectangleF pdf = new RectangleF(send.Right + S(4), sendRow.Top, sendRow.Right - send.Right - S(4), sendRow.Height);
            DrawButton(g, list, "pdf", pdf, "PDF", null, null, Look.Normal, !configured,
                configured ? SwimLang.T("Το PDF του report: ανοίγει μόνο του όταν είναι έτοιμο (όπως το «PDF report» του πίνακα).") :
                    SwimLang.T("Δεν έχει ρυθμιστεί η σύνδεση με το Google Sheet: καρτέλα «Σύνδεση» του πίνακα."),
                () => manager.SendByUser(true));

            RectangleF panel = new RectangleF(area.Left, send.Top - S(6) - S(34), area.Width, S(34));
            // The key of the window only when the bar is wide enough for it next to the text.
            SwimBarButton panelButton = PanelButton();
            Font badgeFont = SwimTheme.Ui(7f, FontStyle.Bold);
            int needed = TextRenderer.MeasureText(g, panelButton.Text, SwimTheme.Ui(9f, FontStyle.Bold), Size.Empty, TextFormatFlags.NoPadding).Width +
                TextRenderer.MeasureText(g, panelButton.Key, badgeFont, Size.Empty, TextFormatFlags.NoPadding).Width + (int)S(40);
            if (needed > panel.Width)
                panelButton.Key = null;
            DrawButton(g, list, panelButton, panel);

            // The graphics of the analysis on the video on and off (2.9.0, key V): the swimmer and the image alone. Beside it (2.9.1, a
            // race) the look of the graphics: the HUD of the demo or the boards of before.
            bool graphicsOn = manager.Settings.VideoGraphics;
            RectangleF graphicsRow = new RectangleF(area.Left, panel.Top - S(6) - S(28), area.Width, S(28));
            RectangleF graphics = graphicsRow;
            if (!race.IsTraining)
            {
                float split = (graphicsRow.Width - S(4)) * 0.58f;
                graphics = new RectangleF(graphicsRow.Left, graphicsRow.Top, split, graphicsRow.Height);
                RectangleF look = new RectangleF(graphics.Right + S(4), graphicsRow.Top, graphicsRow.Right - graphics.Right - S(4), graphicsRow.Height);
                bool hudOn = manager.Settings.HudLayout;
                DrawButton(g, list, "hud", look, hudOn ? "HUD" : SwimLang.T("Πινακάκια"), null, null, hudOn ? Look.Primary : Look.Normal, !graphicsOn,
                    hudOn ? SwimLang.T("Τα γραφικά του αγώνα ως HUD, όπως το demo: πίνακας ανάλυσης αριστερά, περάσματα και καταγραφή δεξιά, χάρτης πισίνας κάτω.\n") +
                            SwimLang.T("Τα πινακάκια (Δ, γράφημα Δ, τελικός, ταχύτητα) προστίθενται με τα κουτάκια «Στο βίντεο» του πίνακα. Κλικ: τα πινακάκια όπως πριν.")
                          : SwimLang.T("Τα γραφικά του αγώνα ως πινακάκια, όπως πριν. Κλικ: το HUD, όπως το demo."),
                    () => manager.SetHud(!manager.Settings.HudLayout));
            }

            string graphicsText = race.IsTraining ? SwimLang.T("Γραφικά στο βίντεο") : SwimLang.T("Γραφικά");
            DrawButton(g, list, "graphics", graphics, (graphicsOn ? "◉ " : "○ ") + graphicsText, null,
                KeyOf(PlayerScreenCommands.SwimGraphics), graphicsOn ? Look.Normal : Look.Quiet, false,
                graphicsOn ? SwimLang.T("Κρύβει τα γραφικά της ανάλυσης από το βίντεο (πίνακες, γραφήματα, σύγκριση, τεχνική), και από τις εξαγωγές του βίντεο.")
                           : SwimLang.T("Ξαναδείχνει τα γραφικά της ανάλυσης στο βίντεο."),
                () => manager.SetGraphics(!manager.Settings.VideoGraphics));
            if (race.IsTraining)
                return graphicsRow.Top;

            // Technical view on the video (as the swimming reels) and the swimmer that its label follows.
            RectangleF row = new RectangleF(area.Left, graphicsRow.Top - S(6) - S(28), area.Width, S(28));
            float half = (row.Width - S(4)) / 2;
            RectangleF reel = new RectangleF(row.Left, row.Top, half, row.Height);
            RectangleF follow = new RectangleF(reel.Right + S(4), row.Top, row.Right - reel.Right - S(4), row.Height);
            bool reelOn = manager.Settings.ReelOverlay;
            DrawButton(g, list, "reel", reel, reelOn ? SwimLang.T("✓ Τεχνική") : SwimLang.T("Τεχνική"), null, null, reelOn ? Look.Primary : Look.Normal, false,
                reelOn ? SwimLang.T("Κλείνει την τεχνική ανάλυση στο βίντεο (ξανά ο πίνακας του αγώνα και το γράφημα ταχύτητας).")
                       : SwimLang.T("Τεχνική ανάλυση στο βίντεο, όπως στα reels: χρόνος, χεριές, συχνότητα, απόσταση ανά κύκλο, ταχύτητα,\n") +
                         SwimLang.T("φάση, γράφημα ανά κύκλο και σύνοψη μετά τον τερματισμό. Φαίνεται και στην εξαγωγή βίντεο."),
                () => manager.SetReel(!manager.Settings.ReelOverlay));
            bool picking = manager.PickingTrack;
            string tracked = manager.TrackDescription;
            DrawButton(g, list, "follow", follow, picking ? SwimLang.T("Κλικ…") : SwimLang.T("Ακολούθηση"), null, null, picking ? Look.Primary : Look.Normal, !race.StartTimestamp.HasValue,
                SwimLang.T("Ακολούθηση αθλητή: σε ένα καρέ μετά την έξοδο από το νερό, κλικ στο κεφάλι και μετά στα πόδια του.\n") +
                SwimLang.T("Η ετικέτα με το όνομα ακολουθεί τον αθλητή μέχρι τον τοίχο του μήκους (ένα μήκος κάθε φορά).") +
                (tracked.Length > 0 ? SwimLang.T("\nΑκολουθείται: ") + tracked : ""),
                () => manager.BeginTrackSwimmer());

            // The check of the proposals of the race (whole race, auto pilot), also from here (since 2026-10-18).
            List<SwimBarButton> checks = RaceCheckButtons(race);
            if (checks.Count == 0)
                return row.Top;

            RectangleF check = new RectangleF(area.Left, row.Top - S(6) - S(28), area.Width, S(28));
            if (checks.Count == 3)
            {
                float third = (check.Width - 2 * S(4)) / 3;
                DrawButton(g, list, checks[0], new RectangleF(check.Left, check.Top, third, check.Height));
                DrawButton(g, list, checks[1], new RectangleF(check.Left + third + S(4), check.Top, third, check.Height));
                DrawButton(g, list, checks[2], new RectangleF(check.Left + 2 * (third + S(4)), check.Top, check.Right - check.Left - 2 * (third + S(4)), check.Height));
            }
            else
            {
                DrawButton(g, list, checks[0], check);
            }

            return check.Top;
        }
        #endregion

        #region Buttons
        /// <summary>Undo of the last mark (at the bottom of the left bar).</summary>
        private SwimBarButton UndoButton()
        {
            return new SwimBarButton
            {
                Id = "undo",
                Text = SwimLang.T("Αναίρεση"),
                Key = KeyOf(PlayerScreenCommands.SwimUndo),
                Look = Look.Danger,
                Tip = SwimLang.T("Αναίρεση του τελευταίου σημαδιού."),
                Click = () => Run(PlayerScreenCommands.SwimUndo)
            };
        }

        /// <summary>The marks of a tab (RaceButtons, TechButtons) with their keys and what they do.</summary>
        private List<SwimBarButton> MarkButtons(SwimRace race, bool tech, int tab, out string title)
        {
            List<Btn> buttons = tech ? TechButtons(race, tab, out title) : RaceButtons(race, tab, out title);
            List<SwimBarButton> list = new List<SwimBarButton>();
            foreach (Btn b in buttons)
            {
                PlayerScreenCommands command = b.Command;
                list.Add(new SwimBarButton
                {
                    Id = b.Id,
                    Text = b.Text,
                    Value = b.Value,
                    Key = b.Action != null ? null : KeyOf(command),
                    Look = b.Primary ? Look.Primary : Look.Normal,
                    Dim = b.Dim,
                    Tip = b.Tip,
                    Click = b.Action ?? (() => Run(command))
                });
            }

            return list;
        }

        /// <summary>Technical analysis: the skeleton of the swimmer (one click), its angles at the frame, its deletion.</summary>
        private List<SwimBarButton> SkeletonButtons()
        {
            bool picking = manager.PickTarget == SwimPickTarget.Skeleton;
            bool none = manager.SkeletonDescription.Length == 0;
            return new List<SwimBarButton>
            {
                new SwimBarButton
                {
                    Id = "skeleton",
                    Text = picking ? SwimLang.T("Κλικ…") : SwimLang.T("Σκελετός"),
                    Look = picking ? Look.Primary : Look.Normal,
                    Dim = manager.SkeletonRunning,
                    Tip = SwimLang.T("Σκελετός του αθλητή: κλικ πάνω του σε ένα καθαρό καρέ. Το βίντεο διαβάζεται από εκεί, όσο φαίνεται,\n") +
                        SwimLang.T("και ο σκελετός σχεδιάζεται με τις γωνίες γονάτου, ισχίου και αγκώνα."),
                    Click = () => manager.BeginSkeleton()
                },
                new SwimBarButton
                {
                    Id = "skeleton-angles",
                    Text = SwimLang.T("Γωνίες"),
                    Dim = none,
                    Tip = SwimLang.T("Οι γωνίες του σκελετού σε αυτό το καρέ μπαίνουν στην ανάλυση (γόνατο, ισχίο, αγκώνας, ώμος)."),
                    Click = () => manager.AddSkeletonAngles()
                },
                new SwimBarButton
                {
                    Id = "skeleton-delete",
                    Text = "✕",
                    Dim = none,
                    Tip = SwimLang.T("Διαγραφή του σκελετού αυτού του σημείου του βίντεο"),
                    Click = () => manager.DeleteSkeleton()
                }
            };
        }

        /// <summary>
        /// Technical analysis: the automatic proposals, checked one by one (✓ ✕ Τέλος while it runs), the check of the ones that wait
        /// and their deletion, or proposals made from the skeleton.
        /// </summary>
        private List<SwimBarButton> ProposalButtons()
        {
            List<SwimBarButton> list = new List<SwimBarButton>();
            int pending = manager.Race != null ? manager.Race.Proposals.Count : 0;
            if (manager.Reviewing)
            {
                list.Add(new SwimBarButton
                {
                    Id = "proposal-right",
                    Text = SwimLang.T("✓ Σωστό"),
                    Look = Look.Primary,
                    Tip = SwimLang.T("Η πρόταση μπαίνει ως σημάδι στο καρέ που βλέπεις (Enter). Με τα βελάκια μετακινείς πρώτα το καρέ αν χρειάζεται."),
                    Click = () => manager.AcceptCurrentProposal()
                });
                list.Add(new SwimBarButton
                {
                    Id = "proposal-wrong",
                    Text = SwimLang.T("✕ Λάθος"),
                    Tip = SwimLang.T("Δεν υπάρχει τέτοιο γεγονός εδώ (Delete)."),
                    Click = () => manager.RejectCurrentProposal()
                });
                list.Add(new SwimBarButton
                {
                    Id = "proposal-end",
                    Text = SwimLang.T("Τέλος"),
                    Tip = SwimLang.T("Τέλος του ελέγχου (Esc): οι υπόλοιπες προτάσεις περιμένουν."),
                    Click = () => manager.EndReview()
                });
            }
            else if (pending > 0)
            {
                list.Add(new SwimBarButton
                {
                    Id = "proposal-review",
                    Text = SwimLang.T("✨ Έλεγχος ") + pending,
                    Look = Look.Primary,
                    Tip = SwimLang.T("Οι προτάσεις μία μία: Enter σωστό, ←/→ και Enter διόρθωση, Delete λάθος, Esc τέλος."),
                    Click = () => manager.BeginReview()
                });
                list.Add(new SwimBarButton
                {
                    Id = "proposal-delete",
                    Text = "✕",
                    Tip = SwimLang.T("Διαγραφή των προτάσεων που περιμένουν"),
                    Click = () => manager.DeleteProposals()
                });
            }
            else
            {
                list.Add(new SwimBarButton
                {
                    Id = "proposal-make",
                    Text = SwimLang.T("✨ Προτάσεις"),
                    Dim = manager.SkeletonDescription.Length == 0 || manager.SkeletonRunning,
                    Tip = SwimLang.T("Προτάσεις από τον σκελετό: πρώτη κίνηση, χέρια, πόδια, είσοδος, δελφινισμοί, έξοδος, στροφή."),
                    Click = () => manager.ProposeHere()
                });
            }

            return list;
        }

        /// <summary>
        /// Automatic strokes: choose the swimmer again (the short text for a narrow place), go from stroke to stroke, delete the stroke
        /// of the frame.
        /// </summary>
        private List<SwimBarButton> AutoStrokeButtons(bool shortText)
        {
            bool pickingStrokes = manager.PickingStrokes && !manager.PickingTrack;
            string pickText = shortText ? (pickingStrokes ? SwimLang.T("Κλικ…") : SwimLang.T("Αθλητής"))
                                        : (pickingStrokes ? SwimLang.T("Κλικ στον αθλητή…") : SwimLang.T("Επιλογή αθλητή"));
            return new List<SwimBarButton>
            {
                new SwimBarButton
                {
                    Id = "auto-pick",
                    Text = pickText,
                    Tip = SwimLang.T("Αυτόματες χεριές: κλικ στο κεφάλι και στα πόδια του αθλητή σε αυτό το καρέ.\nΟι χεριές μετριούνται σε κάθε πέρασμα μέχρι τον τοίχο."),
                    Click = () => manager.BeginAutoStrokes()
                },
                new SwimBarButton { Id = "auto-prev", Text = "◀", Tip = SwimLang.T("Προηγούμενη χεριά"), Click = () => manager.SeekStroke(-1), Repeat = true },
                new SwimBarButton { Id = "auto-next", Text = "▶", Tip = SwimLang.T("Επόμενη χεριά"), Click = () => manager.SeekStroke(1), Repeat = true },
                new SwimBarButton
                {
                    Id = "auto-delete",
                    Text = "✕",
                    Tip = SwimLang.T("Διαγραφή της χεριάς σε αυτό το καρέ (C για να την ξαναβάλεις)"),
                    Click = () => manager.DeleteStrokeHere()
                }
            };
        }

        /// <summary>
        /// The check of the proposals of the race (whole race, auto pilot): ✓ ✕ Τέλος while it runs, else the check of the ones that wait
        /// (none without proposals).
        /// </summary>
        private List<SwimBarButton> RaceCheckButtons(SwimRace race)
        {
            List<SwimBarButton> list = new List<SwimBarButton>();
            int pending = race.Proposals.Count;
            if (manager.Reviewing)
            {
                list.Add(new SwimBarButton
                {
                    Id = "race-right",
                    Text = SwimLang.T("✓ Σωστό"),
                    Look = Look.Primary,
                    Tip = SwimLang.T("Η πρόταση μπαίνει ως σημάδι στο καρέ που βλέπεις (Enter). Με τα βελάκια μετακινείς πρώτα το καρέ αν χρειάζεται."),
                    Click = () => manager.AcceptCurrentProposal()
                });
                list.Add(new SwimBarButton
                {
                    Id = "race-wrong",
                    Text = SwimLang.T("✕ Λάθος"),
                    Tip = SwimLang.T("Δεν υπάρχει τέτοιο γεγονός εδώ (Delete)."),
                    Click = () => manager.RejectCurrentProposal()
                });
                list.Add(new SwimBarButton
                {
                    Id = "race-end",
                    Text = SwimLang.T("Τέλος"),
                    Tip = SwimLang.T("Τέλος του ελέγχου (Esc): οι υπόλοιπες προτάσεις περιμένουν."),
                    Click = () => manager.EndReview()
                });
            }
            else if (pending > 0)
            {
                int doubtful = manager.PilotDoubtful;
                list.Add(new SwimBarButton
                {
                    Id = "race-review",
                    Text = SwimLang.T("✨ Έλεγχος ") + pending.ToString(CultureInfo.InvariantCulture) +
                        (doubtful > 0 ? "  ·  ⚠ " + doubtful.ToString(CultureInfo.InvariantCulture) : ""),
                    Look = Look.Primary,
                    Tip = SwimLang.T("Οι προτάσεις μία μία στο βίντεο: Enter σωστό, ←/→ και Enter διόρθωση, Delete λάθος, Esc τέλος. Από τις απαντήσεις σου μαθαίνει.") +
                        (doubtful > 0 ? SwimLang.T("\nΠρώτα τα αβέβαια του αυτόματου πιλότου (⚠: κάτω από 50%).") : ""),
                    Click = () =>
                    {
                        if (manager.PilotDoubtful > 0)
                            manager.BeginPilotReview();
                        else
                            manager.BeginReview();
                    }
                });
            }

            return list;
        }

        /// <summary>The window of the analysis, opened or closed (its key also in the tip, the badge may not fit).</summary>
        private SwimBarButton PanelButton()
        {
            bool open = manager.PanelOpen;
            string key = KeyOf(PlayerScreenCommands.SwimShowPanel);
            return new SwimBarButton
            {
                Id = "panel",
                Text = open ? SwimLang.T("Κλείσιμο πίνακα") : SwimLang.T("Πίνακας ανάλυσης"),
                Key = key,
                Look = Look.Primary,
                Tip = (open ? SwimLang.T("Κλείνει το παράθυρο της ανάλυσης· τα κουμπιά μένουν εδώ.")
                            : SwimLang.T("Ανοίγει το παράθυρο της ανάλυσης: στοιχεία αγώνα, πίνακας περασμάτων, σύγκριση, report και σύνδεση.")) +
                    (string.IsNullOrEmpty(key) ? "" : " (" + key + ")"),
                Click = () => manager.TogglePanel()
            };
        }

        /// <summary>The tab shown: the phase of the video, or the one chosen with the mouse until the video is in another phase.</summary>
        private int ShownTab(bool tech, out int automatic)
        {
            string[] names = tech ? techTabs : raceTabs;
            automatic = tech ? AutomaticTechTab() : AutomaticRaceTab();
            if (manualTab >= 0 && (manualFrom != automatic || manualTab >= names.Length))
                manualTab = -1;
            return manualTab >= 0 ? manualTab : automatic;
        }

        /// <summary>
        /// The buttons of the bars now, without their places, for the round button of the full screen (SwimHudDial): the marks of the
        /// phase shown, their tools (the skeleton and its proposals, or the automatic strokes), the check of the proposals of the race,
        /// undo and the window of the analysis. Asked of a left bar (its phase).
        /// </summary>
        internal List<SwimBarButton> DialButtons()
        {
            List<SwimBarButton> list = new List<SwimBarButton>();
            SwimRace race = manager.Race;
            if (race == null)
                return list;

            bool tech = race.IsTraining || manager.TechnicalView;
            int automatic;
            int tab = ShownTab(tech, out automatic);
            string title;
            list.AddRange(MarkButtons(race, tech, tab, out title));
            if (tech)
            {
                list.AddRange(SkeletonButtons());
                list.AddRange(ProposalButtons());
            }
            else
            {
                if (manager.Settings.AutoStrokes && tab <= 2)
                    list.AddRange(AutoStrokeButtons(true));
                list.AddRange(RaceCheckButtons(race));
            }

            list.Add(UndoButton());
            list.Add(PanelButton());
            return list;
        }

        /// <summary>
        /// For the round button of the full screen: the phase shown (the short name of its tab) and, on a click, the next one (as the
        /// tabs of the bar: until the video is in another phase).
        /// </summary>
        internal SwimBarButton DialPhaseButton()
        {
            SwimRace race = manager.Race;
            bool tech = race != null && (race.IsTraining || manager.TechnicalView);
            string[] names = tech ? techTabs : raceTabs;
            string[] tips = tech ? techTabTips : raceTabTips;
            int automatic;
            int tab = ShownTab(tech, out automatic);
            int next = (tab + 1) % names.Length;
            return new SwimBarButton
            {
                Id = "phase",
                Text = names[tab],
                Look = tab == automatic ? Look.Normal : Look.Primary,
                Tip = tips[tab] + (tab == automatic ? SwimLang.T(" (η φάση του βίντεο)") : "") + SwimLang.T("\nΚλικ: ") + tips[next] + ".",
                Click = () => ChooseTab(next, automatic),
                Repeat = true
            };
        }
        #endregion

        #region Contents
        private int AutomaticRaceTab()
        {
            switch (manager.CurrentPhase)
            {
                case SwimPhase.Start: return 0;
                case SwimPhase.Swim: return 1;
                case SwimPhase.Turn: return 2;
                default: return 3;
            }
        }

        private int AutomaticTechTab()
        {
            SwimTechPhase phase = manager.CurrentTechPhase;
            return phase != null && phase.Kind == SwimPhaseKind.Turn ? 1 : 0;
        }

        /// <summary>The marks of a phase of the race, as in the tabs of the window, and the checkpoint at the end.</summary>
        private List<Btn> RaceButtons(SwimRace race, int tab, out string title)
        {
            SwimComputed c = manager.Computed;
            List<Btn> list = new List<Btn>();
            switch (tab)
            {
                case 0:
                    title = SwimLang.T("ΕΚΚΙΝΗΣΗ");
                    list.Add(B("start", PlayerScreenCommands.SwimStart, SwimLang.T("Εκκίνηση"), race.StartTimestamp.HasValue ? (race.PilotStart ? "✈?" : "✓") : null,
                        SwimLang.T("Στο καρέ του σήματος εκκίνησης (φως, καπνός, ήχος): εκεί γίνεται t = 0.") +
                        (race.PilotStart ? SwimLang.T("\n✈ Την έβαλε ο αυτόματος πιλότος: έλεγξέ τη (Enter στον «Έλεγχο», ή G στο σωστό καρέ).") : "")));
                    list.Add(new Btn
                    {
                        Id = "start-light",
                        Text = SwimLang.T("💡 Εκκίνηση από το φως"),
                        Dim = manager.StartLightRunning,
                        Tip = SwimLang.T("Αυτόματα: το καρέ που ανάβουν τα φώτα εκκίνησης, για έλεγχο (Enter σωστό, Delete λάθος)."),
                        Action = () => manager.FindStartLight()
                    });
                    list.Add(PilotButton());
                    list.Add(B("reaction", PlayerScreenCommands.SwimReaction, SwimLang.T("Αντίδραση"), Seconds(c.ReactionTime),
                        SwimLang.T("Η πρώτη κίνηση του αθλητή μετά το σήμα (όχι ο επίσημος χρόνος αντίδρασης).")));
                    list.Add(B("block", PlayerScreenCommands.SwimFeetOff, SwimLang.T("Αποδέσμευση"), Seconds(c.BlockTime),
                        SwimLang.T("Τα πόδια φεύγουν από τον βατήρα: αντιστοιχεί στον επίσημο χρόνο αντίδρασης.")));
                    list.Add(B("entry", PlayerScreenCommands.SwimEntry, SwimLang.T("Είσοδος χεριών"), Seconds(c.EntryTime),
                        SwimLang.T("Τα χέρια μπαίνουν στο νερό.")));
                    list.Add(B("kick", PlayerScreenCommands.SwimKick, SwimLang.T("Δελφινισμός"), Count(c.StartKicks),
                        SwimLang.T("Κάθε υποβρύχιος δελφινισμός.")));
                    list.Add(B("breakout", PlayerScreenCommands.SwimBreakout, SwimLang.T("Έξοδος"), Seconds(c.StartUnderwaterTime),
                        SwimLang.T("Το κεφάλι βγαίνει από το νερό.")));
                    list.Add(B("stroke", PlayerScreenCommands.SwimStroke, SwimLang.T("Χεριά"), null, StrokeTip));
                    break;
                case 1:
                    title = SwimLang.T("ΚΟΛΥΜΒΗΣΗ");
                    list.Add(B("stroke", PlayerScreenCommands.SwimStroke, SwimLang.T("Χεριά"), null, StrokeTip));
                    list.Add(new Btn
                    {
                        Id = "auto-race",
                        Text = manager.PickingTrack ? SwimLang.T("Κλικ κεφάλι, πόδια…") : SwimLang.T("🤖 Όλος ο αγώνας"),
                        Dim = manager.AutoStrokesRunning,
                        Tip = SwimLang.T("Αυτόματη ανάλυση όλου του αγώνα: κλικ στο κεφάλι και στα πόδια του αθλητή μετά την έξοδο από το νερό.\n") +
                            SwimLang.T("Ακολουθείται σε όλα τα μήκη και προτείνονται τα περάσματα (15/25/35/45 και οι τοίχοι) για έλεγχο\n") +
                            SwimLang.T("(Enter σωστό, ←/→ και Enter διόρθωση, Delete λάθος)."),
                        Action = () => manager.BeginAutoRace()
                    });
                    list.Add(PilotButton());
                    break;
                case 2:
                    int wall = manager.CurrentTurnWall;
                    title = wall > 0 ? SwimLang.T("ΣΤΡΟΦΗ ") + wall.ToString(CultureInfo.InvariantCulture) + race.Unit.ToUpperInvariant() : SwimLang.T("ΣΤΡΟΦΗ");
                    SwimTurnComputed turn = c.Turns.FirstOrDefault(t => t.Wall == wall);
                    list.Add(B("pushoff", PlayerScreenCommands.SwimFeetOff, SwimLang.T("Αποκόλληση"), turn != null ? Seconds(turn.TurnTime) : null,
                        SwimLang.T("Τα πόδια φεύγουν από τον τοίχο.")));
                    list.Add(B("kick", PlayerScreenCommands.SwimKick, SwimLang.T("Δελφινισμός"), turn != null ? Count(turn.Kicks) : null,
                        SwimLang.T("Κάθε υποβρύχιος δελφινισμός.")));
                    list.Add(B("breakout", PlayerScreenCommands.SwimBreakout, SwimLang.T("Έξοδος"), turn != null ? Seconds(turn.UnderwaterTime) : null,
                        SwimLang.T("Το κεφάλι βγαίνει από το νερό.")));
                    list.Add(B("stroke", PlayerScreenCommands.SwimStroke, SwimLang.T("Χεριά"), null, StrokeTip));
                    break;
                default:
                    title = SwimLang.T("ΤΕΡΜΑΤΙΣΜΟΣ");
                    list.Add(B("stroke", PlayerScreenCommands.SwimStroke, SwimLang.T("Χεριά"), null, SwimLang.T("Οι χεριές μέχρι την τελευταία πριν τον τερματισμό.")));
                    break;
            }

            list.Add(SplitButton(race));
            return list;
        }

        /// <summary>The auto pilot: the whole analysis of the race without the analyst, then the PDF (since 2026-10-18).</summary>
        private Btn PilotButton()
        {
            string waiting = manager.PilotWaiting;
            return new Btn
            {
                Id = "auto-pilot",
                Text = waiting.Length > 0 ? "✈ " + waiting : SwimLang.T("✈ Αυτόματος πιλότος"),
                Primary = waiting.Length > 0,
                Dim = manager.AutoStrokesRunning || manager.StartLightRunning,
                Tip = SwimLang.T("Όλη η ανάλυση του αγώνα μόνη της: η εκκίνηση από το φως ή τον ήχο, ο αθλητής που ακολουθεί η κάμερα, όλα τα μήκη\n") +
                    SwimLang.T("(περάσματα, τοίχοι, έξοδοι από το νερό, χεριές) και στο τέλος το PDF. Ρωτά μόνο όταν δεν είναι σίγουρος (εκκίνηση, αθλητής).\n") +
                    SwimLang.T("Ό,τι έβαλε περιμένει στον «✨ Έλεγχο»: Enter σωστό, ←/→ και Enter διόρθωση, Delete λάθος. Από τις απαντήσεις σου\n") +
                    SwimLang.T("μαθαίνει· ό,τι δεν ελέγξεις δεν διδάσκει."),
                Action = () => manager.RunAutoPilot()
            };
        }

        /// <summary>The checkpoint button says which checkpoint the next press marks at this frame (2.9.5: or moves, SplitTargetAt).</summary>
        private Btn SplitButton(SwimRace race)
        {
            bool move = false;
            long? now = race == manager.Race ? manager.CurrentVideoTimestamp : null;
            int next = now.HasValue ? manager.SplitTargetAt(now.Value, out move) : race.NextCheckpoint();
            string distance = next.ToString(CultureInfo.InvariantCulture) + race.Unit;
            Btn b = new Btn { Id = "split", Command = PlayerScreenCommands.SwimSplit, Primary = next >= 0, Dim = next < 0 };
            if (next < 0)
            {
                b.Text = SwimLang.T("Όλα τα περάσματα ✓");
                b.Tip = SwimLang.T("Όλα τα σημεία ελέγχου έχουν χρόνο. Για να αλλάξεις έναν: κλικ στη γραμμή του (το βίντεο πάει εκεί), το σωστό καρέ με ← → και «Πέρασμα» (S).");
            }
            else if (move)
            {
                // (2.9.5) A checkpoint with a time already (also one of the auto pilot): it moves to this frame.
                b.Text = SwimLang.T("Αλλαγή ") + distance;
                b.Tip = SwimLang.T("Ο χρόνος των ") + distance + SwimLang.T(" μετακινείται σε αυτό το καρέ (Backspace: η προηγούμενη τιμή). Για άλλο σημείο: κλικ στη γραμμή του στα περάσματα.");
            }
            else if (next >= race.Distance)
            {
                b.Text = SwimLang.T("Τερματισμός");
                b.Tip = SwimLang.T("Το χέρι ακουμπά τον τοίχο του τερματισμού (") + distance + ").";
            }
            else if (SwimCalculator.IsWall(next, manager.PoolLength))
            {
                b.Text = SwimLang.T("Επαφή ") + distance;
                b.Tip = SwimLang.T("Επαφή με τον τοίχο των ") + distance + ".";
            }
            else
            {
                b.Text = SwimLang.T("Πέρασμα ") + distance;
                b.Tip = SwimLang.T("Το κεφάλι περνά το σημείο των ") + distance + ".";
            }

            return b;
        }

        /// <summary>The marks of the technical analysis, as in its tabs.</summary>
        private List<Btn> TechButtons(SwimRace race, int tab, out string title)
        {
            List<Btn> list = new List<Btn>();
            switch (tab)
            {
                case 0:
                    title = race.IsTraining ? SwimLang.T("ΕΚΚΙΝΗΣΗ · G: ΝΕΑ ΠΡΟΣΠΑΘΕΙΑ") : SwimLang.T("ΕΚΚΙΝΗΣΗ");
                    list.Add(B("t-signal", PlayerScreenCommands.SwimStart, SwimLang.T("Σήμα"), null, race.IsTraining ? SwimLang.T("Σήμα εκκίνησης: νέα προσπάθεια εκκίνησης.") : SwimLang.T("Σήμα εκκίνησης.")));
                    list.Add(B("t-reaction", PlayerScreenCommands.SwimReaction, SwimLang.T("Αντίδραση"), null, SwimLang.T("Η πρώτη κίνηση μετά το σήμα.")));
                    list.Add(B("t-hands", PlayerScreenCommands.SwimHandsOff, SwimLang.T("Χέρια"), null, SwimLang.T("Τα χέρια φεύγουν από τον βατήρα.")));
                    list.Add(B("t-backfoot", PlayerScreenCommands.SwimBackFoot, SwimLang.T("Πίσω πόδι"), null, SwimLang.T("Το πίσω πόδι φεύγει από τον βατήρα.")));
                    list.Add(B("t-feetoff", PlayerScreenCommands.SwimFeetOff, SwimLang.T("Αποδέσμευση"), null, SwimLang.T("Το μπροστινό πόδι φεύγει από τον βατήρα.")));
                    list.Add(B("t-entry", PlayerScreenCommands.SwimEntry, SwimLang.T("Είσοδος"), null, SwimLang.T("Τα χέρια μπαίνουν στο νερό.")));
                    list.Add(B("t-kick", PlayerScreenCommands.SwimKick, SwimLang.T("Δελφινισμός"), null, SwimLang.T("Κάθε υποβρύχιος δελφινισμός.")));
                    list.Add(B("t-breakout", PlayerScreenCommands.SwimBreakout, SwimLang.T("Έξοδος"), null, SwimLang.T("Το κεφάλι βγαίνει από το νερό.")));
                    list.Add(B("t-split", PlayerScreenCommands.SwimTechSplit, SwimLang.T("Κεφάλι 5 / 10 / 15"), null, SwimLang.T("Το κεφάλι περνά τα 5, 10 και 15 μέτρα.")));
                    list.Add(B("t-stroke", PlayerScreenCommands.SwimStroke, SwimLang.T("Πρώτη χεριά"), null, SwimLang.T("Η πρώτη χεριά μετά την έξοδο.")));
                    break;
                case 1:
                    title = race.IsTraining ? SwimLang.T("ΣΤΡΟΦΗ · W: ΝΕΑ ΣΤΡΟΦΗ") : SwimLang.T("ΣΤΡΟΦΗ");
                    list.Add(B("t-in5", PlayerScreenCommands.SwimTechSplit, SwimLang.T("Κεφάλι −5"), null, SwimLang.T("Το κεφάλι 5 μέτρα πριν τον τοίχο.")));
                    list.Add(B("t-last", PlayerScreenCommands.SwimStroke, SwimLang.T("Τελευταία χεριά"), null, SwimLang.T("Η τελευταία χεριά πριν τον τοίχο.")));
                    list.Add(B("t-rotation", PlayerScreenCommands.SwimRotation, SwimLang.T("Περιστροφή"), null, SwimLang.T("Η αρχή της περιστροφής.")));
                    list.Add(B("t-wall", PlayerScreenCommands.SwimWallContact, SwimLang.T("Επαφή"), null, race.IsTraining ? SwimLang.T("Επαφή με τον τοίχο: νέα στροφή.") : SwimLang.T("Επαφή με τον τοίχο.")));
                    list.Add(B("t-pushoff", PlayerScreenCommands.SwimFeetOff, SwimLang.T("Αποκόλληση"), null, SwimLang.T("Τα πόδια φεύγουν από τον τοίχο.")));
                    list.Add(B("t-kick", PlayerScreenCommands.SwimKick, SwimLang.T("Δελφινισμός"), null, SwimLang.T("Κάθε υποβρύχιος δελφινισμός.")));
                    list.Add(B("t-breakout", PlayerScreenCommands.SwimBreakout, SwimLang.T("Έξοδος"), null, SwimLang.T("Το κεφάλι βγαίνει από το νερό.")));
                    list.Add(B("t-out", PlayerScreenCommands.SwimTechSplit, SwimLang.T("Κεφάλι +5 / +10 / +15"), null, SwimLang.T("Το κεφάλι περνά τα 5, 10 και 15 μέτρα μετά τον τοίχο.")));
                    list.Add(B("t-first", PlayerScreenCommands.SwimStroke, SwimLang.T("Πρώτη χεριά"), null, SwimLang.T("Η πρώτη χεριά μετά την έξοδο.")));
                    break;
                default:
                    title = SwimLang.T("ΣΤΙΓΜΙΟΤΥΠΑ & ΓΩΝΙΕΣ");
                    list.Add(B("t-snapshot", PlayerScreenCommands.SwimSnapshot, SwimLang.T("Στιγμιότυπο"), Count(race.Snapshots.Count), SwimLang.T("Στιγμιότυπο του καρέ για το report.")));
                    list.Add(B("t-angle", PlayerScreenCommands.SwimAngle, SwimLang.T("Γωνία"), Count(race.Angles.Count), SwimLang.T("Μέτρηση γωνίας με κλικ στο βίντεο.")));
                    break;
            }

            return list;
        }

        private string PhaseName(SwimRace race, SwimPhase phase)
        {
            switch (phase)
            {
                case SwimPhase.Start: return SwimLang.T("Εκκίνηση");
                case SwimPhase.Swim: return SwimLang.T("Κολύμβηση");
                case SwimPhase.Turn: return SwimLang.T("Στροφή ") + manager.CurrentTurnWall.ToString(CultureInfo.InvariantCulture) + race.Unit;
                default: return SwimLang.T("Τερματισμός");
            }
        }

        private string AutoStrokesState(SwimRace race)
        {
            if (!manager.Settings.AutoStrokes || race.IsTraining)
                return null;
            if (manager.AutoStrokesRunning)
                return SwimLang.T("ανάγνωση…");
            if (manager.PickTarget == SwimPickTarget.StrokeHead && !manager.PickingTrack)
                return SwimLang.T("κλικ στο κεφάλι");
            if (manager.PickTarget == SwimPickTarget.StrokeFeet && !manager.PickingTrack)
                return SwimLang.T("κλικ στα πόδια");

            int wall = manager.StrokeSessionWall;
            return wall > 0 ? SwimLang.T("μέχρι ") + wall.ToString(CultureInfo.InvariantCulture) + race.Unit : SwimLang.T("μετά την έξοδο (B)");
        }

        private static string EventText(SwimRace race)
        {
            string style = SwimPresets.StyleShort(race.Style).ToUpperInvariant();
            if (race.IsTraining)
                return style + " · " + race.PoolLabel;

            string lane = race.Lane > 0 ? SwimLang.T(" · Δ") + race.Lane.ToString(CultureInfo.InvariantCulture) : "";
            return race.DistanceLabel + (race.IsYards ? "YD " : " ") + style + lane;
        }

        private static Btn B(string id, PlayerScreenCommands command, string text, string value, string tip)
        {
            return new Btn { Id = id, Command = command, Text = text, Value = value, Tip = tip };
        }

        private static string Seconds(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) : null;
        }

        private static string Count(int? value)
        {
            return value.HasValue && value.Value > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
        }

        private static Color SyncColor(SwimSyncState state)
        {
            switch (state)
            {
                case SwimSyncState.Synced: return SwimTheme.Green;
                case SwimSyncState.Sending: return SwimTheme.Accent;
                case SwimSyncState.Offline: return SwimTheme.Warn;
                case SwimSyncState.Error: return SwimTheme.Red;
                case SwimSyncState.NotConfigured: return SwimTheme.Warn;
                default: return SwimTheme.Dim;
            }
        }

        /// <summary>Key of a command in the settings of Kinovea, as in the badges of the window ("S", "Backspace").</summary>
        internal string KeyOf(PlayerScreenCommands command)
        {
            if (keys == null)
            {
                keys = new Dictionary<PlayerScreenCommands, string>();
                try
                {
                    HotkeyCommand[] hotkeys = HotkeySettingsManager.LoadHotkeys("PlayerScreen") ?? new HotkeyCommand[0];
                    KeysConverter converter = new KeysConverter();
                    foreach (HotkeyCommand h in hotkeys)
                    {
                        if (h == null || h.KeyData == Keys.None)
                            continue;

                        keys[(PlayerScreenCommands)h.CommandCode] = h.KeyData == Keys.Back ? "Backspace" : converter.ConvertToString(h.KeyData);
                    }
                }
                catch (Exception e)
                {
                    log.Error("Could not read the keys of the player screen.", e);
                }
            }

            string key;
            return keys.TryGetValue(command, out key) ? key : "";
        }
        #endregion

        #region Drawing
        /// <summary>A glass panel of the HUD (2.9.0) with glowing corner brackets in the accent, as the graphics on the video.</summary>
        private static void DrawCard(Graphics g, RectangleF r, Color accent)
        {
            if (r.Width < 8 || r.Height < 8)
                return;

            SwimTheme.HudPanel(g, new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), accent);
        }

        /// <summary>The caption of a card in spaced capitals (HUD).</summary>
        private void Caption(Graphics g, string text, RectangleF r, Color color)
        {
            SwimTheme.TrackedText(g, SwimLang.Upper(text), SwimTheme.Ui(6.75f, FontStyle.Bold), color, Rectangle.Round(r), 0.16f);
        }

        /// <summary>Button as those of the window: rounded, key in a badge, the value already measured before the key.</summary>
        private void DrawButton(Graphics g, List<Item> list, string id, RectangleF r, string text, string value, string key, Look look, bool dim, string tip, Action click)
        {
            bool hover = hoverId == id;
            bool pressed = hover && pressedId == id;
            Color accent = look == Look.Danger ? SwimTheme.Red : SwimTheme.Accent;

            if (look != Look.Quiet || hover)
            {
                // HUD (2.9.0): cut corners and a thin line in the accent, glowing on the main and the hovered buttons.
                Color fill = look == Look.Primary ? SwimTheme.AccentDark : SwimTheme.Raised;
                if (pressed)
                    fill = SwimTheme.Pressed;
                else if (hover)
                    fill = look == Look.Primary ? SwimTheme.Mix(SwimTheme.AccentDark, accent, 0.25) : SwimTheme.RaisedHover;
                else if (dim)
                    fill = SwimTheme.Mix(fill, SwimTheme.Back, 0.6);

                Color border = hover ? SwimTheme.Mix(SwimTheme.Border, accent, 0.8) :
                    (look == Look.Primary || look == Look.Danger) && !dim ? SwimTheme.Mix(SwimTheme.Border, accent, 0.5) : SwimTheme.Border;
                SwimTheme.FillRounded(g, fill, r, 6);
                if (!dim && (hover || look == Look.Primary))
                {
                    using (GraphicsPath glowPath = SwimTheme.Rounded(r, 6))
                    using (Pen glow = new Pen(Color.FromArgb(hover ? 70 : 42, accent), 3f))
                        g.DrawPath(glow, glowPath);
                }

                SwimTheme.DrawRounded(g, border, r, 6);
                if (look == Look.Primary || look == Look.Danger)
                {
                    using (SolidBrush b = new SolidBrush(dim ? SwimTheme.Mix(accent, SwimTheme.Back, 0.5) : accent))
                        g.FillRectangle(b, r.Left + 3, r.Top + 7, 2, Math.Max(4, r.Height - 14));
                }
            }

            Color textColor = dim && !hover ? SwimTheme.Faint : (look == Look.Quiet ? SwimTheme.Dim : SwimTheme.Text);
            bool icon = string.IsNullOrEmpty(key) && string.IsNullOrEmpty(value) && text != null && text.Length <= 2;
            float right = r.Right - S(6);
            if (!string.IsNullOrEmpty(key))
            {
                Font badgeFont = SwimTheme.Ui(7f, FontStyle.Bold);
                Size ks = TextRenderer.MeasureText(g, key, badgeFont, Size.Empty, TextFormatFlags.NoPadding);
                float bw = ks.Width + S(8);
                float bh = ks.Height + S(4);
                RectangleF badge = new RectangleF(right - bw, r.Top + (r.Height - bh) / 2, bw, bh);
                SwimTheme.FillRounded(g, SwimTheme.Back, badge, 3);
                SwimTheme.DrawRounded(g, SwimTheme.Mix(SwimTheme.Border, accent, dim ? 0.15 : 0.35), badge, 3);
                Write(g, key, badgeFont, badge, dim && !hover ? SwimTheme.Faint : accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                right = badge.Left - S(6);
            }

            if (!string.IsNullOrEmpty(value))
            {
                Font valueFont = SwimTheme.Display(9.5f);
                Size vs = TextRenderer.MeasureText(g, value, valueFont, Size.Empty, TextFormatFlags.NoPadding);
                RectangleF vr = new RectangleF(right - vs.Width - 1, r.Top, vs.Width + 2, r.Height);
                Write(g, value, valueFont, vr, dim && !hover ? SwimTheme.Faint : SwimTheme.Accent,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                right = vr.Left - S(6);
            }

            Font font = SwimTheme.Ui(r.Height >= S(44) ? 10f : (r.Height >= S(30) ? 9f : 8.25f), FontStyle.Bold);
            if (icon)
            {
                Write(g, text, font, r, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            else
            {
                float textLeft = r.Left + S(look == Look.Primary || look == Look.Danger ? 12 : 9);
                RectangleF tr = new RectangleF(textLeft, r.Top, Math.Max(4, right - textLeft), r.Height);
                Write(g, text, font, tr, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }

            list.Add(new Item { Id = id, Bounds = r, Tip = tip, Click = click });
        }

        /// <summary>A button of the list (SwimBarButton) at its place in the bar.</summary>
        private void DrawButton(Graphics g, List<Item> list, SwimBarButton b, RectangleF r)
        {
            DrawButton(g, list, b.Id, r, b.Text, b.Value, b.Key, b.Look, b.Dim, b.Tip, b.Click);
        }

        /// <summary>Tabs of the phases. The tab of the phase of the video is in the accent color when another one is shown.</summary>
        private void DrawTabs(Graphics g, List<Item> list, RectangleF r, string[] names, string[] tips, int selected, int automatic)
        {
            // HUD (2.9.0): a dark strip, the shown phase bright with a glowing line under it.
            SwimTheme.FillRounded(g, SwimTheme.Input, r, 4);
            SwimTheme.DrawRounded(g, SwimTheme.Border, r, 4);
            float w = r.Width / names.Length;
            Font font = SwimTheme.Ui(7.5f, FontStyle.Bold);
            for (int i = 0; i < names.Length; i++)
            {
                string id = "tab" + i.ToString(CultureInfo.InvariantCulture);
                RectangleF cell = new RectangleF(r.Left + i * w + 2, r.Top + 2, w - 4, r.Height - 4);
                if (i == selected)
                {
                    using (LinearGradientBrush under = new LinearGradientBrush(new RectangleF(cell.X, cell.Y - 1, cell.Width, cell.Height + 2),
                        Color.FromArgb(10, SwimTheme.Accent), Color.FromArgb(70, SwimTheme.Accent), LinearGradientMode.Vertical))
                        g.FillRectangle(under, cell);
                    using (SolidBrush glow = new SolidBrush(Color.FromArgb(80, SwimTheme.Accent)))
                        g.FillRectangle(glow, cell.Left + 3, cell.Bottom - 4, cell.Width - 6, 4);
                    using (SolidBrush line = new SolidBrush(SwimTheme.Accent))
                        g.FillRectangle(line, cell.Left + 5, cell.Bottom - 2, cell.Width - 10, 2);
                }
                else if (hoverId == id)
                {
                    SwimTheme.FillRounded(g, SwimTheme.RaisedHover, cell, 3);
                }

                Color color = i == selected ? Color.White : (i == automatic ? SwimTheme.Accent : SwimTheme.Dim);
                Write(g, names[i], font, cell, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                int index = i;
                string tip = tips[i] + (i == automatic ? SwimLang.T(" (η φάση του βίντεο)") : "");
                list.Add(new Item { Id = id, Bounds = cell, Tip = tip, Click = () => ChooseTab(index, automatic) });
            }
        }

        /// <summary>Chips (caption and value), from left to right on as many lines as needed. Returns the bottom of the last line.</summary>
        private float ChipRows(Graphics g, List<SwimChip> chips, float left, float top, float right, float height, bool draw)
        {
            Font captionFont = SwimTheme.Ui(6.5f, FontStyle.Bold);
            Font valueFont = SwimTheme.Ui(9.5f, FontStyle.Bold);
            float x = left;
            float y = top;
            float bottom = top;
            foreach (SwimChip chip in chips)
            {
                Size vs = TextRenderer.MeasureText(g, chip.Value ?? "", valueFont, Size.Empty, TextFormatFlags.NoPadding);
                Size cs = TextRenderer.MeasureText(g, chip.Caption ?? "", captionFont, Size.Empty, TextFormatFlags.NoPadding);
                float w = Math.Min(right - left, Math.Max(vs.Width, cs.Width) + S(13));
                if (x > left && x + w > right)
                {
                    x = left;
                    y += height + S(5);
                }

                if (draw)
                {
                    // HUD (2.9.0): an outlined chip, its caption spaced.
                    RectangleF box = new RectangleF(x, y, w, height);
                    SwimTheme.FillRounded(g, SwimTheme.Raised, box, 4);
                    SwimTheme.DrawRounded(g, SwimTheme.Mix(SwimTheme.Border, chip.Color, 0.25), box, 4);
                    Write(g, chip.Caption, captionFont, new RectangleF(x + S(6), y + S(3), w - S(9), cs.Height + 2), SwimTheme.Dim,
                        TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    Write(g, chip.Value, valueFont, new RectangleF(x + S(6), y + S(3) + cs.Height, w - S(9), height - S(5) - cs.Height), chip.Color,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                }

                x += w + S(6);
                bottom = y + height;
            }

            return bottom;
        }

        private static void CellRight(Graphics g, string text, Font font, float right, RectangleF row, Color color)
        {
            if (string.IsNullOrEmpty(text))
                return;

            Size size = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);
            Write(g, text, font, new RectangleF(right - size.Width - 1, row.Top, size.Width + 2, row.Height), color,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private static void Write(Graphics g, string text, Font font, RectangleF r, Color color, TextFormatFlags flags)
        {
            if (string.IsNullOrEmpty(text) || r.Width < 1 || r.Height < 1)
                return;

            TextRenderer.DrawText(g, text, font, Rectangle.Round(r), color, flags | TextFormatFlags.NoPadding);
        }
        #endregion
    }
}
