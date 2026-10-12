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

namespace Kinovea.ScreenManager
{
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

        private static readonly string[] raceTabs = { "ΕΚΚ.", "ΚΟΛ.", "ΣΤΡ.", "ΤΕΡ." };
        private static readonly string[] raceTabTips = { "Εκκίνηση", "Κολύμβηση", "Στροφή", "Τερματισμός" };
        private static readonly string[] techTabs = { "ΕΚΚΙΝ.", "ΣΤΡΟΦΗ", "ΕΡΓΑΛ." };
        private static readonly string[] techTabTips = { "Φάσεις της εκκίνησης", "Φάσεις της στροφής", "Στιγμιότυπα και γωνίες" };

        private const string StrokeTip = "Σε κάθε είσοδο χεριού. Ελεύθερο/ύπτιο: κάθε χέρι · πεταλούδα/πρόσθιο: κάθε κύκλος.";

        private enum Look
        {
            Normal,
            Primary,
            Danger,
            Quiet
        }

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
            b.Append(manager.SkeletonRunning).Append('|').Append(manager.SkeletonDescription).Append('|');
            b.Append(race.Proposals.Count).Append('|').Append(manager.Reviewing).Append('|');
            b.Append(manager.CurrentProposal != null ? manager.CurrentProposal.Id : "");
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
            DrawButton(g, list, "undo", undo, "Αναίρεση", null, KeyOf(PlayerScreenCommands.SwimUndo), Look.Danger, false,
                "Αναίρεση του τελευταίου σημαδιού.", () => Run(PlayerScreenCommands.SwimUndo));

            float top = PaintLive(g, area, race, tech) + S(8);

            // The tabs follow the phase of the video; a click shows another phase until the video is in another phase.
            string[] names = tech ? techTabs : raceTabs;
            string[] tips = tech ? techTabTips : raceTabTips;
            int automatic = tech ? AutomaticTechTab() : AutomaticRaceTab();
            if (manualTab >= 0 && (manualFrom != automatic || manualTab >= names.Length))
                manualTab = -1;
            int tab = manualTab >= 0 ? manualTab : automatic;

            RectangleF strip = new RectangleF(area.Left, top, area.Width, S(26));
            DrawTabs(g, list, strip, names, tips, tab, automatic);
            top = strip.Bottom + S(6);

            string title;
            List<Btn> buttons = tech ? TechButtons(race, tab, out title) : RaceButtons(race, tab, out title);
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
                caption = "ΤΕΧΝΙΚΗ ΑΝΑΛΥΣΗ";
                if (phase != null && now.HasValue)
                {
                    double t = manager.Timing.Seconds(now.Value, phase.Origin);
                    clock = (t < 0 ? "−" : "") + Math.Abs(t).ToString("0.00", CultureInfo.InvariantCulture);
                    caption = phase.Kind == SwimPhaseKind.Start ? "ΑΠΟ ΤΟ ΣΗΜΑ" : "ΑΠΟ ΤΗΝ ΕΠΑΦΗ";
                    SwimTechPhase owner;
                    int? next = SwimTechCalculator.NextOffset(race, manager.Timing, manager.TechPhases, now.Value, out owner);
                    if (next.HasValue)
                    {
                        string text = (next.Value < 0 ? "−5" : (owner != null && owner.Kind == SwimPhaseKind.Turn ? "+" : "") + next.Value.ToString(CultureInfo.InvariantCulture)) + race.Unit;
                        chips.Add(new SwimChip("T ΕΠΟΜΕΝΟ", text, SwimTheme.Accent));
                    }

                    int kicks = phase.Kicks.Count(k => k <= t + 1e-6);
                    chips.Add(new SwimChip("ΔΕΛΦΙΝΙΣΜΟΙ", kicks.ToString(CultureInfo.InvariantCulture), SwimTheme.Text));
                }
            }
            else
            {
                double? now = manager.CurrentRaceTime;
                if (now.HasValue)
                    clock = now.Value >= 60 ? SwimReference.FormatTime(now.Value) : now.Value.ToString("0.00", CultureInfo.InvariantCulture);

                warn = !race.StartTimestamp.HasValue;
                caption = warn ? "ΧΩΡΙΣ ΣΗΜΑ ΕΚΚΙΝΗΣΗΣ" : "ΧΡΟΝΟΣ ΑΓΩΝΑ";
                int next = race.NextCheckpoint();
                chips.Add(new SwimChip("ΕΠΟΜΕΝΟ", next > 0 ? next.ToString(CultureInfo.InvariantCulture) + race.Unit : "τέλος", SwimTheme.Amber));
                chips.Add(new SwimChip("ΧΕΡΙΕΣ", SwimCalculator.PressesSinceLastSplit(manager.Computed).ToString(CultureInfo.InvariantCulture), SwimTheme.Text));
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
            Write(g, caption, captionFont, new RectangleF(card.Left + inner, card.Top + S(7), Math.Max(10, evLeft - card.Left - inner - S(6)), es.Height + 2),
                warn ? SwimTheme.Warn : SwimTheme.Dim, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Write(g, clock, SwimTheme.Display(22f), new RectangleF(card.Left + S(8), card.Top + S(18), card.Width - S(14), S(36)), Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (chips.Count > 0)
                ChipRows(g, chips, card.Left + inner, card.Top + chipsTop, card.Right - S(8), chipHeight, true);

            return card.Bottom;
        }

        /// <summary>Card with the buttons of the marks of a tab, one under the other (two columns if they don't fit).</summary>
        private void PaintButtons(Graphics g, List<Item> list, RectangleF space, string title, List<Btn> buttons, bool autoRow, bool skeletonRow = false)
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
            Write(g, title, SwimTheme.Ui(7f, FontStyle.Bold), new RectangleF(card.Left + inner, card.Top + S(8), card.Width - inner - S(8), S(14)),
                SwimTheme.Accent, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

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

                Btn b = buttons[i];
                PlayerScreenCommands command = b.Command;
                DrawButton(g, list, b.Id, r, b.Text, columns == 1 ? b.Value : null, KeyOf(command), b.Primary ? Look.Primary : Look.Normal, b.Dim, b.Tip,
                    () => Run(command));
            }

            float ay = y0 + rowCount * (bh + spacing) - spacing + S(8);
            if (skeletonRow && ay + S(26) <= card.Bottom)
            {
                // Skeleton of the swimmer (one click) and its angles at the frame.
                float sq = S(28);
                bool picking = manager.PickTarget == SwimPickTarget.Skeleton;
                RectangleF skel = new RectangleF(x0, ay, (width - sq - S(8)) / 2, S(26));
                DrawButton(g, list, "skeleton", skel, picking ? "Κλικ…" : "Σκελετός", null, null, picking ? Look.Primary : Look.Normal, manager.SkeletonRunning,
                    "Σκελετός του αθλητή: κλικ πάνω του σε ένα καθαρό καρέ. Το βίντεο διαβάζεται από εκεί, όσο φαίνεται,\n" +
                    "και ο σκελετός σχεδιάζεται με τις γωνίες γονάτου, ισχίου και αγκώνα.", () => manager.BeginSkeleton());
                RectangleF angles = new RectangleF(skel.Right + S(4), ay, skel.Width, S(26));
                DrawButton(g, list, "skeleton-angles", angles, "Γωνίες", null, null, Look.Normal, manager.SkeletonDescription.Length == 0,
                    "Οι γωνίες του σκελετού σε αυτό το καρέ μπαίνουν στην ανάλυση (γόνατο, ισχίο, αγκώνας, ώμος).", () => manager.AddSkeletonAngles());
                RectangleF skeletonDelete = new RectangleF(angles.Right + S(4), ay, sq, S(26));
                DrawButton(g, list, "skeleton-delete", skeletonDelete, "✕", null, null, Look.Normal, manager.SkeletonDescription.Length == 0,
                    "Διαγραφή του σκελετού αυτού του σημείου του βίντεο", () => manager.DeleteSkeleton());

                // Automatic proposals: check them one by one (Enter, Delete, Esc on the video), or propose from the skeleton.
                float py = ay + S(30);
                if (py + S(26) > card.Bottom)
                    return;

                int pending = manager.Race != null ? manager.Race.Proposals.Count : 0;
                if (manager.Reviewing)
                {
                    float third = (width - 2 * S(4)) / 3;
                    DrawButton(g, list, "proposal-right", new RectangleF(x0, py, third, S(26)), "✓ Σωστό", null, null, Look.Primary, false,
                        "Η πρόταση μπαίνει ως σημάδι στο καρέ που βλέπεις (Enter). Με τα βελάκια μετακινείς πρώτα το καρέ αν χρειάζεται.",
                        () => manager.AcceptCurrentProposal());
                    DrawButton(g, list, "proposal-wrong", new RectangleF(x0 + third + S(4), py, third, S(26)), "✕ Λάθος", null, null, Look.Normal, false,
                        "Δεν υπάρχει τέτοιο γεγονός εδώ (Delete).", () => manager.RejectCurrentProposal());
                    DrawButton(g, list, "proposal-end", new RectangleF(x0 + 2 * (third + S(4)), py, third, S(26)), "Τέλος", null, null, Look.Normal, false,
                        "Τέλος του ελέγχου (Esc): οι υπόλοιπες προτάσεις περιμένουν.", () => manager.EndReview());
                }
                else if (pending > 0)
                {
                    RectangleF review = new RectangleF(x0, py, width - sq - S(4), S(26));
                    DrawButton(g, list, "proposal-review", review, "✨ Έλεγχος " + pending, null, null, Look.Primary, false,
                        "Οι προτάσεις μία μία: Enter σωστό, ←/→ και Enter διόρθωση, Delete λάθος, Esc τέλος.", () => manager.BeginReview());
                    DrawButton(g, list, "proposal-delete", new RectangleF(review.Right + S(4), py, sq, S(26)), "✕", null, null, Look.Normal, false,
                        "Διαγραφή των προτάσεων που περιμένουν", () => manager.DeleteProposals());
                }
                else
                {
                    DrawButton(g, list, "proposal-make", new RectangleF(x0, py, width, S(26)), "✨ Προτάσεις", null, null, Look.Normal,
                        manager.SkeletonDescription.Length == 0 || manager.SkeletonRunning,
                        "Προτάσεις από τον σκελετό: πρώτη κίνηση, χέρια, πόδια, είσοδος, δελφινισμοί, έξοδος, στροφή.", () => manager.ProposeHere());
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
            bool pickingStrokes = manager.PickingStrokes && !manager.PickingTrack;
            string pickText = pickingStrokes ? "Κλικ στον αθλητή…" : "Επιλογή αθλητή";
            if (TextRenderer.MeasureText(g, pickText, SwimTheme.Ui(8.25f, FontStyle.Bold), Size.Empty, TextFormatFlags.NoPadding).Width > pick.Width - S(16))
                pickText = pickingStrokes ? "Κλικ…" : "Αθλητής";
            DrawButton(g, list, "auto-pick", pick, pickText, null, null, Look.Normal, false,
                "Αυτόματες χεριές: κλικ στο κεφάλι και στα πόδια του αθλητή σε αυτό το καρέ.\nΟι χεριές μετριούνται σε κάθε πέρασμα μέχρι τον τοίχο.",
                () => manager.BeginAutoStrokes());
            RectangleF prev = new RectangleF(pick.Right + S(4), ay, small, S(26));
            DrawButton(g, list, "auto-prev", prev, "◀", null, null, Look.Normal, false, "Προηγούμενη χεριά", () => manager.SeekStroke(-1));
            RectangleF next = new RectangleF(prev.Right + S(4), ay, small, S(26));
            DrawButton(g, list, "auto-next", next, "▶", null, null, Look.Normal, false, "Επόμενη χεριά", () => manager.SeekStroke(1));
            RectangleF delete = new RectangleF(next.Right + S(4), ay, small, S(26));
            DrawButton(g, list, "auto-delete", delete, "✕", null, null, Look.Normal, false, "Διαγραφή της χεριάς σε αυτό το καρέ (C για να την ξαναβάλεις)",
                () => manager.DeleteStrokeHere());
        }

        /// <summary>Phase of the race and its values: Δ with the reference, ΔΑΚ, automatic strokes (or the values of the technical analysis).</summary>
        private float PaintInfo(Graphics g, RectangleF area, SwimRace race, bool tech)
        {
            string value;
            List<SwimChip> chips = new List<SwimChip>();
            if (tech)
            {
                SwimTechPhase phase = manager.CurrentTechPhase;
                value = phase != null ? phase.Title : (race.IsTraining ? "G: νέα εκκίνηση · W: νέα στροφή" : "εκτός εκκίνησης / στροφής");
                if (phase != null)
                {
                    string key = phase.Kind == SwimPhaseKind.Start ? "t15" : "out15";
                    double? v15 = phase.Value(key);
                    if (v15.HasValue)
                        chips.Add(new SwimChip(phase.Kind == SwimPhaseKind.Start ? "15" + race.Unit : "→ +15" + race.Unit, v15.Value.ToString("0.00", CultureInfo.InvariantCulture), SwimTheme.Green));
                }

                chips.Add(new SwimChip("ΣΤΙΓΜΙΟΤΥΠΑ", race.Snapshots.Count.ToString(CultureInfo.InvariantCulture), SwimTheme.Text));
                chips.Add(new SwimChip("ΓΩΝΙΕΣ", race.Angles.Count.ToString(CultureInfo.InvariantCulture), SwimTheme.Text));
            }
            else
            {
                value = PhaseName(race, manager.CurrentPhase);
                double? now = manager.CurrentRaceTime;
                SwimComputed c = manager.Computed;
                if (now.HasValue && c.Reference != null)
                {
                    SwimRow last = c.Rows.Where(r => r.T.HasValue && r.Delta.HasValue && r.T.Value <= now.Value + 1e-6).OrderBy(r => r.T.Value).LastOrDefault();
                    if (last != null)
                        chips.Add(new SwimChip("Δ " + last.D + race.Unit, SwimReference.FormatDelta(last.Delta.Value), last.Delta.Value > 0.0049 ? SwimTheme.Red : SwimTheme.Green));
                }

                if (now.HasValue)
                {
                    SwimBlock block = c.Blocks
                        .Where(b => b.Dak.HasValue && c.Rows.Any(r => r.D == b.To && r.T.HasValue && r.T.Value <= now.Value + 1e-6))
                        .OrderBy(b => b.To)
                        .LastOrDefault();
                    if (block != null)
                        chips.Add(new SwimChip("ΔΑΚ " + block.From + "-" + block.To, block.Dak.Value.ToString("0.00", CultureInfo.InvariantCulture), SwimTheme.Amber));
                }

                string auto = AutoStrokesState(race);
                if (auto != null)
                    chips.Add(new SwimChip("ΑΥΤΟΜΑΤΕΣ ΧΕΡΙΕΣ", auto, SwimTheme.Accent));
            }

            float inner = S(10);
            float chipsTop = S(48);
            float chipHeight = S(28);
            float height = chips.Count > 0 ? ChipRows(g, chips, area.Left + inner, area.Top + chipsTop, area.Right - S(8), chipHeight, false) - area.Top + S(8) : S(48);
            RectangleF card = new RectangleF(area.Left, area.Top, area.Width, height);
            DrawCard(g, card, SwimTheme.Amber);
            Write(g, tech ? "ΦΑΣΗ ΤΕΧΝΙΚΗΣ ΑΝΑΛΥΣΗΣ" : "ΦΑΣΗ", SwimTheme.Ui(6.75f, FontStyle.Bold), new RectangleF(card.Left + inner, card.Top + S(7), card.Width - inner - S(8), S(13)),
                SwimTheme.Dim, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
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
            Write(g, "ΠΕΡΑΣΜΑΤΑ", SwimTheme.Ui(6.75f, FontStyle.Bold), new RectangleF(card.Left + inner, card.Top + S(7), card.Width - inner - S(8), S(13)),
                SwimTheme.Dim, TextFormatFlags.Left | TextFormatFlags.SingleLine);

            float rx = card.Left + S(7);
            float rw = card.Right - S(5) - rx;
            float colD = rx + S(5);
            float colT = rx + rw * 0.57f;
            float colV = rx + rw * 0.78f;
            float colS = rx + rw - S(5);
            RectangleF head = new RectangleF(rx, card.Top + S(21), rw, S(14));
            Font headFont = SwimTheme.Ui(6.5f, FontStyle.Bold);
            CellRight(g, "ΧΡΟΝΟΣ", headFont, colT, head, SwimTheme.Faint);
            CellRight(g, "M/S", headFont, colV, head, SwimTheme.Faint);
            CellRight(g, "ΧΕΡ.", headFont, colS, head, SwimTheme.Faint);

            float rowHeight = S(21);
            float rowsTop = card.Top + S(37);
            int capacity = Math.Max(0, (int)((card.Bottom - S(6) - rowsTop) / rowHeight));
            if (rows.Count == 0 || capacity == 0)
                return;

            int next = rows.FindIndex(r => !r.T.HasValue);
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
                    SwimTheme.FillRounded(g, SwimTheme.NextRow, r, 4);
                else if (hoverId == id && row.T.HasValue)
                    SwimTheme.FillRounded(g, SwimTheme.RaisedHover, r, 4);

                Color distanceColor = i == next ? SwimTheme.Amber : (row.T.HasValue ? SwimTheme.Text : SwimTheme.Faint);
                Write(g, row.D.ToString(CultureInfo.InvariantCulture) + race.Unit, cellFont, new RectangleF(colD, r.Top, rw * 0.3f, r.Height), distanceColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                CellRight(g, row.T.HasValue ? SwimReference.FormatTime(row.T.Value) : "–", timeFont, colT, r,
                    warn ? SwimTheme.Red : (row.T.HasValue ? Color.White : SwimTheme.Faint));
                if (row.Vel.HasValue)
                    CellRight(g, row.Vel.Value.ToString("0.00", CultureInfo.InvariantCulture), cellFont, colV, r, row.IsFree ? SwimTheme.Text : SwimTheme.Dim);
                if (row.Strokes.HasValue)
                    CellRight(g, row.Strokes.Value.ToString("0.#", CultureInfo.InvariantCulture) + (row.StrokesTyped ? " ✓" : ""), cellFont, colS, r,
                        row.StrokesTyped ? SwimTheme.Green : SwimTheme.Text);

                if (row.T.HasValue)
                {
                    int distance = row.D;
                    string tip = "Μετάβαση του βίντεο στο πέρασμα των " + distance.ToString(CultureInfo.InvariantCulture) + race.Unit + "." +
                        (warn ? "\n⚠ Ο χρόνος δεν είναι μεγαλύτερος από τον προηγούμενο." : "");
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
            DrawButton(g, list, "close", close, "✕  Κλείσιμο ανάλυσης", null, null, Look.Quiet, false,
                "Κρύβει τα κουμπιά και το παράθυρο της ανάλυσης (η ανάλυση είναι αποθηκευμένη).\n" +
                "Εμφανίζονται ξανά με ένα πλήκτρο της ανάλυσης (π.χ. S) ή με το Ctrl+Shift+W.",
                () => manager.CloseAnalysis());

            string sheet = !string.IsNullOrEmpty(race.SheetName) ? " · " + race.SheetName : "";
            string sync = "● " + (manager.SyncMessage ?? "") + sheet;
            RectangleF line = new RectangleF(area.Left + S(4), close.Top - S(6) - S(16), area.Width - S(8), S(16));
            Write(g, sync, SwimTheme.Ui(7.5f, FontStyle.Regular), line, SyncColor(manager.SyncState),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            list.Add(new Item { Id = "sync", Bounds = line, Tip = (manager.SyncMessage ?? "") + sheet });

            bool configured = manager.Settings.IsConfigured;
            RectangleF send = new RectangleF(area.Left, line.Top - S(3) - S(30), area.Width, S(30));
            DrawButton(g, list, "send", send, "Αποστολή τώρα", null, null, Look.Normal, !configured,
                configured ? "Στέλνει την ανάλυση στο Google Sheet τώρα." : "Δεν έχει ρυθμιστεί η σύνδεση με το Google Sheet: καρτέλα «Σύνδεση» του πίνακα.",
                () => manager.SendByUser(false));

            bool open = manager.PanelOpen;
            RectangleF panel = new RectangleF(area.Left, send.Top - S(6) - S(34), area.Width, S(34));
            // The key of the window only when the bar is wide enough for it next to the text.
            string panelText = open ? "Κλείσιμο πίνακα" : "Πίνακας ανάλυσης";
            string panelKey = KeyOf(PlayerScreenCommands.SwimShowPanel);
            Font badgeFont = SwimTheme.Ui(7f, FontStyle.Bold);
            int needed = TextRenderer.MeasureText(g, panelText, SwimTheme.Ui(9f, FontStyle.Bold), Size.Empty, TextFormatFlags.NoPadding).Width +
                TextRenderer.MeasureText(g, panelKey, badgeFont, Size.Empty, TextFormatFlags.NoPadding).Width + (int)S(40);
            if (needed > panel.Width)
                panelKey = null;
            DrawButton(g, list, "panel", panel, panelText, null, panelKey, Look.Primary, false,
                (open ? "Κλείνει το παράθυρο της ανάλυσης· τα κουμπιά μένουν εδώ."
                      : "Ανοίγει το παράθυρο της ανάλυσης: στοιχεία αγώνα, πίνακας περασμάτων, σύγκριση, report και σύνδεση.") +
                (string.IsNullOrEmpty(KeyOf(PlayerScreenCommands.SwimShowPanel)) ? "" : " (" + KeyOf(PlayerScreenCommands.SwimShowPanel) + ")"),
                () => manager.TogglePanel());
            if (race.IsTraining)
                return panel.Top;

            // Technical view on the video (as the swimming reels) and the swimmer that its label follows.
            RectangleF row = new RectangleF(area.Left, panel.Top - S(6) - S(28), area.Width, S(28));
            float half = (row.Width - S(4)) / 2;
            RectangleF reel = new RectangleF(row.Left, row.Top, half, row.Height);
            RectangleF follow = new RectangleF(reel.Right + S(4), row.Top, row.Right - reel.Right - S(4), row.Height);
            bool reelOn = manager.Settings.ReelOverlay;
            DrawButton(g, list, "reel", reel, reelOn ? "✓ Τεχνική" : "Τεχνική", null, null, reelOn ? Look.Primary : Look.Normal, false,
                reelOn ? "Κλείνει την τεχνική ανάλυση στο βίντεο (ξανά ο πίνακας του αγώνα και το γράφημα ταχύτητας)."
                       : "Τεχνική ανάλυση στο βίντεο, όπως στα reels: χρόνος, χεριές, συχνότητα, απόσταση ανά κύκλο, ταχύτητα,\n" +
                         "φάση, γράφημα ανά κύκλο και σύνοψη μετά τον τερματισμό. Φαίνεται και στην εξαγωγή βίντεο.",
                () => manager.SetReel(!manager.Settings.ReelOverlay));
            bool picking = manager.PickingTrack;
            string tracked = manager.TrackDescription;
            DrawButton(g, list, "follow", follow, picking ? "Κλικ…" : "Ακολούθηση", null, null, picking ? Look.Primary : Look.Normal, !race.StartTimestamp.HasValue,
                "Ακολούθηση αθλητή: σε ένα καρέ μετά την έξοδο από το νερό, κλικ στο κεφάλι και μετά στα πόδια του.\n" +
                "Η ετικέτα με το όνομα ακολουθεί τον αθλητή μέχρι τον τοίχο του μήκους (ένα μήκος κάθε φορά)." +
                (tracked.Length > 0 ? "\nΑκολουθείται: " + tracked : ""),
                () => manager.BeginTrackSwimmer());
            return row.Top;
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
                    title = "ΕΚΚΙΝΗΣΗ";
                    list.Add(B("start", PlayerScreenCommands.SwimStart, "Εκκίνηση", race.StartTimestamp.HasValue ? "✓" : null,
                        "Στο καρέ του σήματος εκκίνησης (καπνός, φλας, ήχος): εκεί γίνεται t = 0."));
                    list.Add(B("reaction", PlayerScreenCommands.SwimReaction, "Αντίδραση", Seconds(c.ReactionTime),
                        "Η πρώτη κίνηση του αθλητή μετά το σήμα (όχι ο επίσημος χρόνος αντίδρασης)."));
                    list.Add(B("block", PlayerScreenCommands.SwimFeetOff, "Αποδέσμευση", Seconds(c.BlockTime),
                        "Τα πόδια φεύγουν από τον βατήρα: αντιστοιχεί στον επίσημο χρόνο αντίδρασης."));
                    list.Add(B("entry", PlayerScreenCommands.SwimEntry, "Είσοδος χεριών", Seconds(c.EntryTime),
                        "Τα χέρια μπαίνουν στο νερό."));
                    list.Add(B("kick", PlayerScreenCommands.SwimKick, "Δελφινισμός", Count(c.StartKicks),
                        "Κάθε υποβρύχιος δελφινισμός."));
                    list.Add(B("breakout", PlayerScreenCommands.SwimBreakout, "Έξοδος", Seconds(c.StartUnderwaterTime),
                        "Το κεφάλι βγαίνει από το νερό."));
                    list.Add(B("stroke", PlayerScreenCommands.SwimStroke, "Χεριά", null, StrokeTip));
                    break;
                case 1:
                    title = "ΚΟΛΥΜΒΗΣΗ";
                    list.Add(B("stroke", PlayerScreenCommands.SwimStroke, "Χεριά", null, StrokeTip));
                    break;
                case 2:
                    int wall = manager.CurrentTurnWall;
                    title = wall > 0 ? "ΣΤΡΟΦΗ " + wall.ToString(CultureInfo.InvariantCulture) + race.Unit.ToUpperInvariant() : "ΣΤΡΟΦΗ";
                    SwimTurnComputed turn = c.Turns.FirstOrDefault(t => t.Wall == wall);
                    list.Add(B("pushoff", PlayerScreenCommands.SwimFeetOff, "Αποκόλληση", turn != null ? Seconds(turn.TurnTime) : null,
                        "Τα πόδια φεύγουν από τον τοίχο."));
                    list.Add(B("kick", PlayerScreenCommands.SwimKick, "Δελφινισμός", turn != null ? Count(turn.Kicks) : null,
                        "Κάθε υποβρύχιος δελφινισμός."));
                    list.Add(B("breakout", PlayerScreenCommands.SwimBreakout, "Έξοδος", turn != null ? Seconds(turn.UnderwaterTime) : null,
                        "Το κεφάλι βγαίνει από το νερό."));
                    list.Add(B("stroke", PlayerScreenCommands.SwimStroke, "Χεριά", null, StrokeTip));
                    break;
                default:
                    title = "ΤΕΡΜΑΤΙΣΜΟΣ";
                    list.Add(B("stroke", PlayerScreenCommands.SwimStroke, "Χεριά", null, "Οι χεριές μέχρι την τελευταία πριν τον τερματισμό."));
                    break;
            }

            list.Add(SplitButton(race));
            return list;
        }

        /// <summary>The checkpoint button says which checkpoint the next press marks.</summary>
        private Btn SplitButton(SwimRace race)
        {
            int next = race.NextCheckpoint();
            string distance = next.ToString(CultureInfo.InvariantCulture) + race.Unit;
            Btn b = new Btn { Id = "split", Command = PlayerScreenCommands.SwimSplit, Primary = next >= 0, Dim = next < 0 };
            if (next < 0)
            {
                b.Text = "Όλα τα περάσματα ✓";
                b.Tip = "Όλα τα σημεία ελέγχου έχουν χρόνο. Για να ξαναμαρκάρεις ένα, διάγραψε τον χρόνο του στον πίνακα (επιλογή γραμμής + Delete).";
            }
            else if (next >= race.Distance)
            {
                b.Text = "Τερματισμός";
                b.Tip = "Το χέρι ακουμπά τον τοίχο του τερματισμού (" + distance + ").";
            }
            else if (SwimCalculator.IsWall(next, manager.PoolLength))
            {
                b.Text = "Επαφή " + distance;
                b.Tip = "Επαφή με τον τοίχο των " + distance + ".";
            }
            else
            {
                b.Text = "Πέρασμα " + distance;
                b.Tip = "Το κεφάλι περνά το σημείο των " + distance + ".";
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
                    title = race.IsTraining ? "ΕΚΚΙΝΗΣΗ · G: ΝΕΑ ΠΡΟΣΠΑΘΕΙΑ" : "ΕΚΚΙΝΗΣΗ";
                    list.Add(B("t-signal", PlayerScreenCommands.SwimStart, "Σήμα", null, race.IsTraining ? "Σήμα εκκίνησης: νέα προσπάθεια εκκίνησης." : "Σήμα εκκίνησης."));
                    list.Add(B("t-reaction", PlayerScreenCommands.SwimReaction, "Αντίδραση", null, "Η πρώτη κίνηση μετά το σήμα."));
                    list.Add(B("t-hands", PlayerScreenCommands.SwimHandsOff, "Χέρια", null, "Τα χέρια φεύγουν από τον βατήρα."));
                    list.Add(B("t-backfoot", PlayerScreenCommands.SwimBackFoot, "Πίσω πόδι", null, "Το πίσω πόδι φεύγει από τον βατήρα."));
                    list.Add(B("t-feetoff", PlayerScreenCommands.SwimFeetOff, "Αποδέσμευση", null, "Το μπροστινό πόδι φεύγει από τον βατήρα."));
                    list.Add(B("t-entry", PlayerScreenCommands.SwimEntry, "Είσοδος", null, "Τα χέρια μπαίνουν στο νερό."));
                    list.Add(B("t-kick", PlayerScreenCommands.SwimKick, "Δελφινισμός", null, "Κάθε υποβρύχιος δελφινισμός."));
                    list.Add(B("t-breakout", PlayerScreenCommands.SwimBreakout, "Έξοδος", null, "Το κεφάλι βγαίνει από το νερό."));
                    list.Add(B("t-split", PlayerScreenCommands.SwimTechSplit, "Κεφάλι 5 / 10 / 15", null, "Το κεφάλι περνά τα 5, 10 και 15 μέτρα."));
                    list.Add(B("t-stroke", PlayerScreenCommands.SwimStroke, "Πρώτη χεριά", null, "Η πρώτη χεριά μετά την έξοδο."));
                    break;
                case 1:
                    title = race.IsTraining ? "ΣΤΡΟΦΗ · W: ΝΕΑ ΣΤΡΟΦΗ" : "ΣΤΡΟΦΗ";
                    list.Add(B("t-in5", PlayerScreenCommands.SwimTechSplit, "Κεφάλι −5", null, "Το κεφάλι 5 μέτρα πριν τον τοίχο."));
                    list.Add(B("t-last", PlayerScreenCommands.SwimStroke, "Τελευταία χεριά", null, "Η τελευταία χεριά πριν τον τοίχο."));
                    list.Add(B("t-rotation", PlayerScreenCommands.SwimRotation, "Περιστροφή", null, "Η αρχή της περιστροφής."));
                    list.Add(B("t-wall", PlayerScreenCommands.SwimWallContact, "Επαφή", null, race.IsTraining ? "Επαφή με τον τοίχο: νέα στροφή." : "Επαφή με τον τοίχο."));
                    list.Add(B("t-pushoff", PlayerScreenCommands.SwimFeetOff, "Αποκόλληση", null, "Τα πόδια φεύγουν από τον τοίχο."));
                    list.Add(B("t-kick", PlayerScreenCommands.SwimKick, "Δελφινισμός", null, "Κάθε υποβρύχιος δελφινισμός."));
                    list.Add(B("t-breakout", PlayerScreenCommands.SwimBreakout, "Έξοδος", null, "Το κεφάλι βγαίνει από το νερό."));
                    list.Add(B("t-out", PlayerScreenCommands.SwimTechSplit, "Κεφάλι +5 / +10 / +15", null, "Το κεφάλι περνά τα 5, 10 και 15 μέτρα μετά τον τοίχο."));
                    list.Add(B("t-first", PlayerScreenCommands.SwimStroke, "Πρώτη χεριά", null, "Η πρώτη χεριά μετά την έξοδο."));
                    break;
                default:
                    title = "ΣΤΙΓΜΙΟΤΥΠΑ & ΓΩΝΙΕΣ";
                    list.Add(B("t-snapshot", PlayerScreenCommands.SwimSnapshot, "Στιγμιότυπο", Count(race.Snapshots.Count), "Στιγμιότυπο του καρέ για το report."));
                    list.Add(B("t-angle", PlayerScreenCommands.SwimAngle, "Γωνία", Count(race.Angles.Count), "Μέτρηση γωνίας με κλικ στο βίντεο."));
                    break;
            }

            return list;
        }

        private string PhaseName(SwimRace race, SwimPhase phase)
        {
            switch (phase)
            {
                case SwimPhase.Start: return "Εκκίνηση";
                case SwimPhase.Swim: return "Κολύμβηση";
                case SwimPhase.Turn: return "Στροφή " + manager.CurrentTurnWall.ToString(CultureInfo.InvariantCulture) + race.Unit;
                default: return "Τερματισμός";
            }
        }

        private string AutoStrokesState(SwimRace race)
        {
            if (!manager.Settings.AutoStrokes || race.IsTraining)
                return null;
            if (manager.AutoStrokesRunning)
                return "ανάγνωση…";
            if (manager.PickTarget == SwimPickTarget.StrokeHead && !manager.PickingTrack)
                return "κλικ στο κεφάλι";
            if (manager.PickTarget == SwimPickTarget.StrokeFeet && !manager.PickingTrack)
                return "κλικ στα πόδια";

            int wall = manager.StrokeSessionWall;
            return wall > 0 ? "μέχρι " + wall.ToString(CultureInfo.InvariantCulture) + race.Unit : "μετά την έξοδο (B)";
        }

        private static string EventText(SwimRace race)
        {
            string style = SwimPresets.StyleShort(race.Style).ToUpperInvariant();
            if (race.IsTraining)
                return style + " · " + race.PoolLabel;

            string lane = race.Lane > 0 ? " · Δ" + race.Lane.ToString(CultureInfo.InvariantCulture) : "";
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
        private string KeyOf(PlayerScreenCommands command)
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
        /// <summary>Translucent dark card with a colored left edge, as the graphics on the video.</summary>
        private static void DrawCard(Graphics g, RectangleF r, Color accent)
        {
            if (r.Width < 8 || r.Height < 8)
                return;

            using (GraphicsPath path = SwimTheme.Rounded(r, 7))
            using (LinearGradientBrush back = new LinearGradientBrush(r, Color.FromArgb(232, 9, 14, 22), Color.FromArgb(205, 18, 30, 45), LinearGradientMode.Horizontal))
            using (Pen border = new Pen(Color.FromArgb(90, 255, 255, 255), 1f))
            {
                g.FillPath(back, path);
                g.DrawPath(border, path);
            }

            using (SolidBrush a = new SolidBrush(accent))
                g.FillRectangle(a, r.Left + 1, r.Top + 7, 3, Math.Max(2, r.Height - 14));
        }

        /// <summary>Button as those of the window: rounded, key in a badge, the value already measured before the key.</summary>
        private void DrawButton(Graphics g, List<Item> list, string id, RectangleF r, string text, string value, string key, Look look, bool dim, string tip, Action click)
        {
            bool hover = hoverId == id;
            bool pressed = hover && pressedId == id;
            Color accent = look == Look.Danger ? SwimTheme.Red : SwimTheme.Accent;

            if (look != Look.Quiet || hover)
            {
                Color fill = look == Look.Primary ? SwimTheme.AccentDark : SwimTheme.Raised;
                if (pressed)
                    fill = SwimTheme.Pressed;
                else if (hover)
                    fill = look == Look.Primary ? SwimTheme.Mix(SwimTheme.AccentDark, accent, 0.25) : SwimTheme.RaisedHover;
                else if (dim)
                    fill = SwimTheme.Mix(fill, SwimTheme.Back, 0.6);

                Color border = hover ? SwimTheme.Mix(SwimTheme.Border, accent, 0.6) : SwimTheme.Border;
                SwimTheme.FillRounded(g, fill, r, 5);
                SwimTheme.DrawRounded(g, border, r, 5);
                if (look == Look.Primary || look == Look.Danger)
                {
                    using (SolidBrush b = new SolidBrush(dim ? SwimTheme.Mix(accent, SwimTheme.Back, 0.5) : accent))
                        g.FillRectangle(b, r.Left + 2, r.Top + 5, 3, Math.Max(4, r.Height - 10));
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

        /// <summary>Tabs of the phases. The tab of the phase of the video is in the accent color when another one is shown.</summary>
        private void DrawTabs(Graphics g, List<Item> list, RectangleF r, string[] names, string[] tips, int selected, int automatic)
        {
            SwimTheme.FillRounded(g, SwimTheme.Input, r, 6);
            SwimTheme.DrawRounded(g, SwimTheme.Border, r, 6);
            float w = r.Width / names.Length;
            Font font = SwimTheme.Ui(7.5f, FontStyle.Bold);
            for (int i = 0; i < names.Length; i++)
            {
                string id = "tab" + i.ToString(CultureInfo.InvariantCulture);
                RectangleF cell = new RectangleF(r.Left + i * w + 2, r.Top + 2, w - 4, r.Height - 4);
                if (i == selected)
                    SwimTheme.FillRounded(g, SwimTheme.AccentDark, cell, 4);
                else if (hoverId == id)
                    SwimTheme.FillRounded(g, SwimTheme.RaisedHover, cell, 4);

                Color color = i == selected ? Color.White : (i == automatic ? SwimTheme.Accent : SwimTheme.Dim);
                Write(g, names[i], font, cell, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                int index = i;
                string tip = tips[i] + (i == automatic ? " (η φάση του βίντεο)" : "");
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
                    RectangleF box = new RectangleF(x, y, w, height);
                    SwimTheme.FillRounded(g, SwimTheme.Raised, box, 4);
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
