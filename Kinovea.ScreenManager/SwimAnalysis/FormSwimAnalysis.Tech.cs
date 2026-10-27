#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Pages of the technical analysis: starts, turns, snapshots and angles.
    /// In a race they show the start and the turns of the race, in a training video the attempts.
    /// </summary>
    public partial class FormSwimAnalysis
    {
        // The values kept in the files and the sheet (Greek), shown with SwimLang.Label.
        private static readonly string[] stagesStart = { "Θέση ετοιμότητας", "Φάση βατήρα", "Αποδέσμευση", "Πτήση", "Είσοδος στο νερό", "Υποβρύχια φάση", "Έξοδος από το νερό", "Κολύμβηση" };
        private static readonly string[] stagesTurn = { "Προσέγγιση", "Περιστροφή", "Επαφή στον τοίχο", "Αποκόλληση", "Υποβρύχια φάση", "Έξοδος από το νερό", "Κολύμβηση" };
        private static readonly string[] angleNames = { "Γόνατο", "Ισχίο", "Κορμός", "Αγκώνας", "Ώμος", "Αστράγαλος", "Κεφάλι", "Γωνία εισόδου στο νερό", "Γωνία ώθησης", "Γωνία πτήσης" };

        private SwimPage tabTechStart;
        private SwimPage tabTechTurns;
        private SwimPage tabTechMedia;

        // Starts
        private ComboBox cmbStartPhase;
        private Label lblStartPhaseInfo;
        private Label lblTsSignal, lblTsReaction, lblTsHands, lblTsBackFoot, lblTsBlock, lblTsEntry, lblTsKicks, lblTsBreakout, lblTsSplits, lblTsFirstStroke;
        private DataGridView gridTechStart;
        private string selectedStartId;

        // Turns
        private ComboBox cmbTurnPhase;
        private Label lblTurnPhaseInfo;
        private Label lblTtIn, lblTtLastStroke, lblTtRotation, lblTtContact, lblTtPushoff, lblTtKicks, lblTtBreakout, lblTtSplits, lblTtFirstStroke;
        private DataGridView gridTechTurns;
        private string selectedTurnId;

        // Phase of the video at the last refresh of each page: the page follows the video only when it enters another phase.
        private string videoStartId;
        private string videoTurnId;

        // Snapshots and angles
        private ListBox lbSnapshots;
        private PictureBox picSnapshot;
        private PictureBox picElite;
        private ComboBox cmbStage;
        private TextBox tbComment;
        private ComboBox cmbElite;
        private Label lblElite;
        private ComboBox cmbAngleName;
        private Button btnSkeleton;
        private Button btnSkeletonAngles;
        private CheckBox chkSkeleton;
        private CheckBox chkSkeletonAngles;
        private Label lblSkeleton;
        private Button btnPropose;
        private Button btnTechPilot;
        private Button btnReview;
        private Button btnProposalRight;
        private Button btnProposalWrong;
        private Button btnDeleteProposals;
        private CheckBox chkProposeAuto;
        private CheckBox chkAutoSnapshots;
        private Label lblProposals;
        private DataGridView gridAngles;
        private TextBox tbNotes;
        private Label lblSnapshotInfo;
        private string selectedSnapshotId;
        private string shownEliteId;
        private string shownEliteList;

        private readonly Dictionary<Button, SwimPickTarget[]> techPickButtons = new Dictionary<Button, SwimPickTarget[]>();

        private class PhaseItem
        {
            public readonly string Id;
            public readonly string Title;

            public PhaseItem(SwimTechPhase phase)
            {
                Id = phase.Id;
                Title = phase.Title;
            }

            public override string ToString()
            {
                return Title;
            }
        }

        private class SnapshotItem
        {
            public readonly SwimSnapshot Snapshot;
            public readonly string Text;

            public SnapshotItem(SwimSnapshot snapshot, string text)
            {
                Snapshot = snapshot;
                Text = text;
            }

            public override string ToString()
            {
                return Text;
            }
        }

        private class EliteItem
        {
            public readonly SwimEliteFrame Frame;

            public EliteItem(SwimEliteFrame frame)
            {
                Frame = frame;
            }

            public override string ToString()
            {
                return Frame == null ? SwimLang.T("(χωρίς elite καρέ)") : Frame.Display;
            }
        }

        #region Construction
        private void BuildTechPages()
        {
            tabTechStart = BuildTechStartTab();
            tabTechTurns = BuildTechTurnsTab();
            tabTechMedia = BuildTechMediaTab();
        }

        /// <summary>Selector of the phase: combo, go to the video, delete (training attempts).</summary>
        private TableLayoutPanel PhaseBar(string caption, out ComboBox combo, out Label info, Action<SwimTechPhase> seek, Action<SwimTechPhase> delete)
        {
            TableLayoutPanel bar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = new Padding(0), Padding = new Padding(0, 0, 0, 4) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label title = new Label { Text = caption, AutoSize = true, Font = valueFont, ForeColor = SwimTheme.Accent, Margin = new Padding(0, 7, 6, 3) };
            ComboBox c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 4, 3) };
            Button go = new SwimNoFocusButton { Text = SwimLang.T("▶ βίντεο"), Width = 76, Height = 26, Margin = new Padding(0, 2, 4, 2) };
            Button del = new SwimNoFocusButton { Text = SwimLang.T("Διαγραφή"), Width = 76, Height = 26, Margin = new Padding(0, 2, 0, 2), Accent = SwimTheme.Red };
            ComboBox local = c;
            go.Click += (s, e) => { SwimTechPhase p = SelectedPhase(local); if (p != null) seek(p); };
            del.Click += (s, e) => { SwimTechPhase p = SelectedPhase(local); if (p != null) delete(p); };
            toolTip.SetToolTip(go, SwimLang.T("Μετάβαση του βίντεο στο σήμα (εκκίνηση) ή στην επαφή στον τοίχο (στροφή)."));
            toolTip.SetToolTip(del, SwimLang.T("Διαγραφή των σημαδιών αυτής της προσπάθειας."));
            Label i = new Label { AutoSize = false, Height = 18, Dock = DockStyle.Fill, ForeColor = SwimTheme.Dim, AutoEllipsis = true, Margin = new Padding(0, 0, 0, 2) };
            bar.Controls.Add(title, 0, 0);
            bar.Controls.Add(c, 1, 0);
            bar.Controls.Add(go, 2, 0);
            bar.Controls.Add(del, 3, 0);
            bar.Controls.Add(i, 0, 1);
            bar.SetColumnSpan(i, 4);
            combo = c;
            info = i;
            return bar;
        }

        private Button TechPickButton(string text, string tooltip, Func<SwimTechPhase> phase, bool entry, params SwimPickTarget[] targets)
        {
            Button b = new SwimNoFocusButton { Text = text, AutoSize = true, Height = 26, Margin = new Padding(6, 2, 2, 2) };
            b.Click += (s, e) => manager.BeginPhaseMeasure(phase(), entry);
            toolTip.SetToolTip(b, tooltip + SwimLang.T(" (χρειάζεται βαθμονόμηση: δες κάτω)"));
            techPickButtons[b] = targets;
            return b;
        }

        private Button OffsetButton(string text, int offset, SwimPhaseKind kind)
        {
            Button b = new SwimNoFocusButton { Text = text, Width = 44, Height = 26, Margin = new Padding(3, 2, 0, 2) };
            b.Click += (s, e) => manager.MarkTechSplit(offset, kind);
            toolTip.SetToolTip(b, SwimLang.T("Τεχνικό πέρασμα σε αυτό το καρέ: το κεφάλι στα ") + text + SwimLang.T(" (αντί για το επόμενο της σειράς του πλήκτρου)."));
            return b;
        }

        private SwimPage BuildTechStartTab()
        {
            SwimPage page = new SwimPage(SwimLang.T("Εκκινήσεις"));
            page.Padding = new Padding(6);
            TableLayoutPanel bar = PhaseBar(SwimLang.T("Εκκίνηση:"), out cmbStartPhase, out lblStartPhaseInfo, p => manager.SeekTo(p.Origin), p => DeletePhase(p));
            cmbStartPhase.SelectedIndexChanged += (s, e) =>
            {
                if (updating)
                    return;
                PhaseItem item = cmbStartPhase.SelectedItem as PhaseItem;
                selectedStartId = item != null ? item.Id : null;
                RefreshTech();
            };

            TableLayoutPanel t = NewEventTable();
            lblTsSignal = ValueLabel();
            AddEventRow(t, SwimLang.T("Σήμα εκκίνησης"), PlayerScreenCommands.SwimStart, SwimLang.T("Σήμα"),
                SwimLang.T("Στο καρέ του σήματος. Στον αγώνα: t = 0. Στην προπόνηση: κάθε πάτημα ξεκινά νέα προσπάθεια εκκίνησης."), lblTsSignal);
            lblTsReaction = ValueLabel();
            AddEventRow(t, SwimLang.T("Αντίδραση (πρώτη κίνηση)"), PlayerScreenCommands.SwimReaction, SwimLang.T("Αντίδραση"),
                SwimLang.T("Η πρώτη κίνηση του αθλητή μετά το σήμα."), lblTsReaction);
            lblTsHands = ValueLabel();
            AddEventRow(t, SwimLang.T("Χέρια φεύγουν από τον βατήρα"), PlayerScreenCommands.SwimHandsOff, SwimLang.T("Χέρια"),
                SwimLang.T("Τα χέρια αφήνουν τον βατήρα."), lblTsHands);
            lblTsBackFoot = ValueLabel();
            AddEventRow(t, SwimLang.T("Πίσω πόδι φεύγει"), PlayerScreenCommands.SwimBackFoot, SwimLang.T("Πίσω πόδι"),
                SwimLang.T("Εκκίνηση track: το πίσω πόδι αφήνει τον βατήρα / τη σφήνα."), lblTsBackFoot);
            lblTsBlock = ValueLabel();
            AddEventRow(t, SwimLang.T("Αποδέσμευση (μπροστινό πόδι)"), PlayerScreenCommands.SwimFeetOff, SwimLang.T("Αποδέσμευση"),
                SwimLang.T("Το μπροστινό πόδι αφήνει τον βατήρα: τέλος της φάσης βατήρα (ο επίσημος χρόνος αντίδρασης)."), lblTsBlock);
            lblTsEntry = ValueLabel();
            AddEventRow(t, SwimLang.T("Είσοδος χεριών στο νερό"), PlayerScreenCommands.SwimEntry, SwimLang.T("Είσοδος"),
                SwimLang.T("Τα χέρια μπαίνουν στο νερό: τέλος της πτήσης."), lblTsEntry,
                TechPickButton(SwimLang.T("📍 Απόσταση"), SwimLang.T("Κλικ εκεί που τα χέρια μπαίνουν στο νερό: απόσταση εισόδου."), () => SelectedPhase(cmbStartPhase), true,
                    SwimPickTarget.EntryHands, SwimPickTarget.PhaseEntry));
            lblTsKicks = ValueLabel();
            AddEventRow(t, SwimLang.T("Υποβρύχιοι δελφινισμοί"), PlayerScreenCommands.SwimKick, SwimLang.T("Δελφινισμός"),
                SwimLang.T("Ένα πάτημα για κάθε δελφινισμό: πλήθος και συχνότητα."), lblTsKicks);
            lblTsBreakout = ValueLabel();
            AddEventRow(t, SwimLang.T("Έξοδος από το νερό (κεφάλι)"), PlayerScreenCommands.SwimBreakout, SwimLang.T("Έξοδος"),
                SwimLang.T("Το κεφάλι βγαίνει από το νερό: τέλος της υποβρύχιας φάσης."), lblTsBreakout,
                TechPickButton(SwimLang.T("📍 Απόσταση"), SwimLang.T("Κλικ στο κεφάλι εκεί που βγαίνει από το νερό: απόσταση εξόδου."), () => SelectedPhase(cmbStartPhase), false,
                    SwimPickTarget.StartBreakout, SwimPickTarget.PhaseBreakout));
            lblTsSplits = ValueLabel();
            AddEventRow(t, SwimLang.T("Κεφάλι στα 5 / 10 / 15"), PlayerScreenCommands.SwimTechSplit, "5 / 10 / 15",
                SwimLang.T("Το κεφάλι περνά τα 5, 10 και 15 (με τη σειρά, ή με τα κουμπιά για συγκεκριμένη απόσταση)."), lblTsSplits,
                OffsetButton("5", 5, SwimPhaseKind.Start), OffsetButton("10", 10, SwimPhaseKind.Start), OffsetButton("15", 15, SwimPhaseKind.Start));
            lblTsFirstStroke = ValueLabel();
            AddEventRow(t, SwimLang.T("Πρώτη χεριά"), PlayerScreenCommands.SwimStroke, SwimLang.T("Χεριά"),
                SwimLang.T("Η πρώτη είσοδος χεριού μετά την έξοδο."), lblTsFirstStroke);

            gridTechStart = CreateTechGrid();
            Label hint = new Label { Dock = DockStyle.Bottom, Height = 30, ForeColor = SwimTheme.Dim,
                Text = SwimLang.T("Χρόνοι από το σήμα εκκίνησης · Στην προπόνηση κάθε στήλη είναι μια προσπάθεια, με την καλύτερη και τον μέσο όρο.") };

            page.Controls.Add(gridTechStart);
            page.Controls.Add(hint);
            page.Controls.Add(t);
            page.Controls.Add(bar);
            return page;
        }

        private SwimPage BuildTechTurnsTab()
        {
            SwimPage page = new SwimPage(SwimLang.T("Στροφές"));
            page.Padding = new Padding(6);
            TableLayoutPanel bar = PhaseBar(SwimLang.T("Στροφή:"), out cmbTurnPhase, out lblTurnPhaseInfo, p => manager.SeekTo(p.Origin), p => DeletePhase(p));
            cmbTurnPhase.SelectedIndexChanged += (s, e) =>
            {
                if (updating)
                    return;
                PhaseItem item = cmbTurnPhase.SelectedItem as PhaseItem;
                selectedTurnId = item != null ? item.Id : null;
                RefreshTech();
            };

            TableLayoutPanel t = NewEventTable();
            lblTtIn = ValueLabel();
            AddEventRow(t, SwimLang.T("Κεφάλι στα −5 πριν τον τοίχο"), PlayerScreenCommands.SwimTechSplit, "−5",
                SwimLang.T("Το κεφάλι στα 5 πριν τον τοίχο: χρόνος προσέγγισης."), lblTtIn, OffsetButton("−5", -5, SwimPhaseKind.Turn));
            lblTtLastStroke = ValueLabel();
            AddEventRow(t, SwimLang.T("Τελευταία χεριά"), PlayerScreenCommands.SwimStroke, SwimLang.T("Χεριά"),
                SwimLang.T("Η τελευταία είσοδος χεριού πριν τον τοίχο (από εκεί μετράει ο χρόνος της ανάποδης στροφής)."), lblTtLastStroke);
            lblTtRotation = ValueLabel();
            AddEventRow(t, SwimLang.T("Αρχή περιστροφής"), PlayerScreenCommands.SwimRotation, SwimLang.T("Περιστροφή"),
                SwimLang.T("Ανάποδη στροφή: το κεφάλι αρχίζει να κατεβαίνει (αρχή της τούμπας)."), lblTtRotation);
            lblTtContact = ValueLabel();
            AddEventRow(t, SwimLang.T("Επαφή στον τοίχο"), PlayerScreenCommands.SwimWallContact, SwimLang.T("Επαφή"),
                SwimLang.T("Πόδια (ανάποδη) ή χέρια (ανοιχτή) στον τοίχο. Στον αγώνα είναι το πέρασμα του τοίχου, στην προπόνηση ξεκινά νέα προσπάθεια στροφής."), lblTtContact);
            lblTtPushoff = ValueLabel();
            AddEventRow(t, SwimLang.T("Αποκόλληση"), PlayerScreenCommands.SwimFeetOff, SwimLang.T("Αποκόλληση"),
                SwimLang.T("Τα πόδια φεύγουν από τον τοίχο: χρόνος επαφής και χρόνος στροφής."), lblTtPushoff);
            lblTtKicks = ValueLabel();
            AddEventRow(t, SwimLang.T("Υποβρύχιοι δελφινισμοί"), PlayerScreenCommands.SwimKick, SwimLang.T("Δελφινισμός"),
                SwimLang.T("Ένα πάτημα για κάθε δελφινισμό μετά την αποκόλληση."), lblTtKicks);
            lblTtBreakout = ValueLabel();
            AddEventRow(t, SwimLang.T("Έξοδος από το νερό (κεφάλι)"), PlayerScreenCommands.SwimBreakout, SwimLang.T("Έξοδος"),
                SwimLang.T("Το κεφάλι βγαίνει από το νερό."), lblTtBreakout,
                TechPickButton(SwimLang.T("📍 Απόσταση"), SwimLang.T("Κλικ στο κεφάλι εκεί που βγαίνει από το νερό: απόσταση από τον τοίχο."), () => SelectedPhase(cmbTurnPhase), false,
                    SwimPickTarget.TurnUnderwater, SwimPickTarget.PhaseBreakout));
            lblTtSplits = ValueLabel();
            AddEventRow(t, SwimLang.T("Κεφάλι στα +5 / +10 / +15"), PlayerScreenCommands.SwimTechSplit, "+5 / +10 / +15",
                SwimLang.T("Το κεφάλι περνά τα 5, 10 και 15 από τον τοίχο."), lblTtSplits,
                OffsetButton("+5", 5, SwimPhaseKind.Turn), OffsetButton("+10", 10, SwimPhaseKind.Turn), OffsetButton("+15", 15, SwimPhaseKind.Turn));
            lblTtFirstStroke = ValueLabel();
            AddEventRow(t, SwimLang.T("Πρώτη χεριά"), PlayerScreenCommands.SwimStroke, SwimLang.T("Χεριά"),
                SwimLang.T("Η πρώτη είσοδος χεριού μετά την έξοδο."), lblTtFirstStroke);

            gridTechTurns = CreateTechGrid();
            Label hint = new Label { Dock = DockStyle.Bottom, Height = 30, ForeColor = SwimTheme.Dim,
                Text = SwimLang.T("Χρόνοι από την επαφή στον τοίχο (αρνητικοί πριν την επαφή) · Χρόνος στροφής: ανάποδη από την τελευταία χεριά, ανοιχτή από την επαφή.") };

            page.Controls.Add(gridTechTurns);
            page.Controls.Add(hint);
            page.Controls.Add(t);
            page.Controls.Add(bar);
            return page;
        }

        private DataGridView CreateTechGrid()
        {
            DataGridView g = CreateGrid();
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            g.ScrollBars = ScrollBars.Both;
            g.CellDoubleClick += (s, e) =>
            {
                DataGridView grid = (DataGridView)s;
                if (e.ColumnIndex <= 0 || e.ColumnIndex >= grid.Columns.Count)
                    return;
                SwimTechPhase phase = grid.Columns[e.ColumnIndex].Tag as SwimTechPhase;
                if (phase != null)
                    manager.SeekTo(phase.Origin);
            };
            return g;
        }

        private SwimPage BuildTechMediaTab()
        {
            SwimPage page = new SwimPage(SwimLang.T("Στιγμιότυπα & γωνίες"));
            page.Padding = new Padding(6);

            // Tools: snapshot, angle (with its name).
            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 4) };
            Button btnSnapshot = new SwimNoFocusButton { Width = 150, Height = 32, Primary = true };
            btnSnapshot.Click += (s, e) => RunCommand(PlayerScreenCommands.SwimSnapshot);
            RegisterCommandButton(PlayerScreenCommands.SwimSnapshot, btnSnapshot, SwimLang.T("📷 Στιγμιότυπο"));
            toolTip.SetToolTip(btnSnapshot, SwimLang.T("Το καρέ του βίντεο (με τα σχέδια και τις γωνίες του) μπαίνει στο report με τα σχόλιά σου."));
            Button btnAngle = new SwimNoFocusButton { Width = 120, Height = 32 };
            btnAngle.Click += (s, e) => RunCommand(PlayerScreenCommands.SwimAngle);
            RegisterCommandButton(PlayerScreenCommands.SwimAngle, btnAngle, SwimLang.T("📐 Γωνία"));
            toolTip.SetToolTip(btnAngle, SwimLang.T("Τρία κλικ στο σταματημένο βίντεο: άκρο, κορυφή, άκρο. Η γωνία μπαίνει στις τιμές της φάσης."));
            techPickButtons[btnAngle] = new SwimPickTarget[] { SwimPickTarget.Angle };
            cmbAngleName = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 170, Margin = new Padding(6, 6, 3, 3) };
            cmbAngleName.Items.AddRange(angleNames.Select(SwimLang.Label).ToArray());
            cmbAngleName.Text = SwimLang.Label(manager.AngleName);
            cmbAngleName.TextChanged += (s, e) => manager.AngleName = SwimLang.Greek(cmbAngleName.Text, angleNames);
            toolTip.SetToolTip(cmbAngleName, SwimLang.T("Όνομα της επόμενης γωνίας (π.χ. γόνατο στη θέση ετοιμότητας, γωνία εισόδου στο νερό)."));
            chkAutoSnapshots = new CheckBox { Text = SwimLang.T("αυτόματα σε κάθε γεγονός"), AutoSize = true, Margin = new Padding(4, 9, 8, 3) };
            chkAutoSnapshots.CheckedChanged += (s, e) =>
            {
                if (!updating)
                    manager.SetAutoSnapshots(chkAutoSnapshots.Checked);
            };
            toolTip.SetToolTip(chkAutoSnapshots, SwimLang.T("Όταν μαρκάρεις ένα γεγονός της εκκίνησης ή της στροφής (σήμα, χέρια, αποδέσμευση, είσοδο, πρώτο δελφινισμό,\n") +
                SwimLang.T("έξοδο, πρώτη και τελευταία χεριά, περιστροφή, επαφή στον τοίχο, αποκόλληση) το καρέ του γίνεται στιγμιότυπο με το όνομά του.\n") +
                SwimLang.T("Ξανά στο ίδιο γεγονός σε άλλο καρέ: αλλάζει το στιγμιότυπο. Αναίρεση του σημαδιού: φεύγει και το στιγμιότυπο.\n") +
                SwimLang.T("Με το «📷 Στιγμιότυπο» (P) τραβάς όποιο άλλο καρέ θέλεις."));
            tools.Controls.Add(btnSnapshot);
            tools.Controls.Add(chkAutoSnapshots);
            tools.Controls.Add(btnAngle);
            tools.Controls.Add(Caption(SwimLang.T("όνομα γωνίας:")));
            tools.Controls.Add(cmbAngleName);

            // Skeleton of the swimmer: one click, then the angles of any frame.
            btnSkeleton = new SwimNoFocusButton { Text = SwimLang.T("🦴 Σκελετός"), Width = 120, Height = 32 };
            btnSkeleton.Click += (s, e) => manager.BeginSkeleton();
            toolTip.SetToolTip(btnSkeleton, SwimLang.T("Σε ένα καρέ όπου ο αθλητής φαίνεται καθαρά (π.χ. στη θέση ετοιμότητας ή πριν την επαφή), κλικ πάνω του.\n") +
                SwimLang.T("Το βίντεο διαβάζεται από εκεί και μετά, όσο τον βλέπει, και ο σκελετός του σχεδιάζεται στο βίντεο με τις γωνίες\n") +
                SwimLang.T("του γονάτου, του ισχίου και του αγκώνα. Για κάμερες σε σταθερή θέση, πάνω ή κάτω από το νερό."));
            techPickButtons[btnSkeleton] = new SwimPickTarget[] { SwimPickTarget.Skeleton };
            btnSkeletonAngles = new SwimNoFocusButton { Text = SwimLang.T("📐 Γωνίες σκελετού"), Width = 150, Height = 32 };
            btnSkeletonAngles.Click += (s, e) => manager.AddSkeletonAngles();
            toolTip.SetToolTip(btnSkeletonAngles, SwimLang.T("Οι γωνίες του σκελετού σε αυτό το καρέ (γόνατο, ισχίο, αγκώνας, ώμος της πλευράς που φαίνεται)\n") +
                SwimLang.T("μπαίνουν στην ανάλυση όπως οι γωνίες με κλικ: στις τιμές της φάσης, στο Google Sheet και στο report."));
            Button btnDeleteSkeleton = new SwimNoFocusButton { Text = "✕", Width = 32, Height = 32 };
            btnDeleteSkeleton.Click += (s, e) => manager.DeleteSkeleton();
            toolTip.SetToolTip(btnDeleteSkeleton, SwimLang.T("Διαγραφή του σκελετού αυτού του σημείου του βίντεο (ή όλων, αν δεν υπάρχει εδώ)"));
            chkSkeleton = new CheckBox { Text = SwimLang.T("στο βίντεο"), AutoSize = true, Margin = new Padding(8, 9, 3, 3) };
            chkSkeleton.CheckedChanged += (s, e) =>
            {
                if (!updating)
                    manager.SetSkeletonOverlay(chkSkeleton.Checked);
            };
            chkSkeletonAngles = new CheckBox { Text = SwimLang.T("με γωνίες"), AutoSize = true, Margin = new Padding(4, 9, 3, 3) };
            chkSkeletonAngles.CheckedChanged += (s, e) =>
            {
                if (!updating)
                    manager.SetSkeletonAngles(chkSkeletonAngles.Checked);
            };
            lblSkeleton = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(6, 10, 0, 3), Text = "–" };
            tools.SetFlowBreak(cmbAngleName, true);
            tools.Controls.Add(btnSkeleton);
            tools.Controls.Add(btnSkeletonAngles);
            tools.Controls.Add(btnDeleteSkeleton);
            tools.Controls.Add(chkSkeleton);
            tools.Controls.Add(chkSkeletonAngles);
            tools.Controls.Add(lblSkeleton);
            tools.SetFlowBreak(lblSkeleton, true);

            // (2.9.9) The turn read on its own (side camera under the water): its events as proposals, checked as the others.
            btnTechPilot = new SwimNoFocusButton { Text = SwimLang.T("🤖 Αυτόματα"), Width = 150, Height = 32, Primary = true };
            btnTechPilot.Click += (s, e) => manager.RunTechPilot();
            toolTip.SetToolTip(btnTechPilot, SwimLang.T("Βίντεο προπόνησης από την υποβρύχια πλάγια κάμερα: διαβάζεται όλο (ή η ζώνη εργασίας).\n") +
                SwimLang.T("Στροφή: η τελευταία χεριά, η αρχή της περιστροφής, η επαφή, η αποκόλληση, οι δελφινισμοί, η έξοδος και κάθε χεριά μετά.\n") +
                SwimLang.T("Εκκίνηση (με το σήμα G μαρκαρισμένο): η είσοδος, οι δελφινισμοί, η έξοδος και κάθε χεριά μετά.\n") +
                SwimLang.T("Με το βίντεο της εξωτερικής κάμερας στον ίδιο φάκελο: το κεφάλι στα 5 μ. από τις σημαίες του ύπτιου.\n") +
                SwimLang.T("Το στυλ της ανάλυσης λέει το είδος της στροφής (ελεύθερο, ύπτιο: ανάποδη· πρόσθιο, πεταλούδα: ανοιχτή).\n") +
                SwimLang.T("Μετά ελέγχεις μία μία: Enter σωστό, ←/→ και Enter διόρθωση, Delete λάθος, Esc τέλος. Με το HUD: οι μετρήσεις της φάσης στο βίντεο."));

            // Automatic proposals from the skeleton, checked one by one on the video; the learning of this computer.
            btnPropose = new SwimNoFocusButton { Text = SwimLang.T("✨ Προτάσεις"), Width = 120, Height = 32 };
            btnPropose.Click += (s, e) => manager.ProposeHere();
            toolTip.SetToolTip(btnPropose, SwimLang.T("Από τον σκελετό αυτού του σημείου: προτάσεις για πρώτη κίνηση, χέρια, πίσω πόδι, αποκόλληση, είσοδο\n") +
                SwimLang.T("(εκκίνηση από τη θέση ετοιμότητας), δελφινισμούς και έξοδο (κάτω από το νερό), επαφή και αποκόλληση (στροφή).\n") +
                SwimLang.T("Μετά ελέγχεις μία μία: Enter σωστό, ←/→ και Enter διόρθωση, Delete λάθος, Esc τέλος."));
            btnReview = new SwimNoFocusButton { Text = SwimLang.T("▶ Έλεγχος"), Width = 104, Height = 32, Primary = true };
            btnReview.Click += (s, e) => manager.BeginReview();
            toolTip.SetToolTip(btnReview, SwimLang.T("Το βίντεο πηγαίνει στην πρώτη πρόταση που περιμένει απάντηση."));
            btnProposalRight = new SwimNoFocusButton { Text = SwimLang.T("✓ Σωστό"), Width = 90, Height = 32 };
            btnProposalRight.Click += (s, e) => manager.AcceptCurrentProposal();
            toolTip.SetToolTip(btnProposalRight, SwimLang.T("Η πρόταση μπαίνει ως σημάδι στο καρέ που βλέπεις (Enter). Αν μετακίνησες το βίντεο με τα βελάκια, κρατιέται η διόρθωσή σου."));
            btnProposalWrong = new SwimNoFocusButton { Text = SwimLang.T("✕ Λάθος"), Width = 86, Height = 32 };
            btnProposalWrong.Click += (s, e) => manager.RejectCurrentProposal();
            toolTip.SetToolTip(btnProposalWrong, SwimLang.T("Δεν υπάρχει τέτοιο γεγονός εδώ (Delete)."));
            btnDeleteProposals = new SwimNoFocusButton { Text = "🗑", Width = 34, Height = 32 };
            btnDeleteProposals.Click += (s, e) => manager.DeleteProposals();
            toolTip.SetToolTip(btnDeleteProposals, SwimLang.T("Διαγραφή των προτάσεων που περιμένουν (χωρίς απάντηση: η μάθηση δεν αλλάζει)."));
            chkProposeAuto = new CheckBox { Text = SwimLang.T("αμέσως μετά τον σκελετό"), AutoSize = true, Margin = new Padding(8, 9, 3, 3) };
            chkProposeAuto.CheckedChanged += (s, e) =>
            {
                if (!updating)
                    manager.SetProposeAfterSkeleton(chkProposeAuto.Checked);
            };
            lblProposals = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(6, 10, 0, 3), Text = "–" };
            LinkLabel resetLearning = new LinkLabel { Text = SwimLang.T("↺ μάθηση"), AutoSize = true, Margin = new Padding(8, 10, 0, 3), LinkColor = SwimTheme.Accent };
            resetLearning.LinkClicked += (s, e) =>
            {
                if (MessageBox.Show(this, SwimLang.T("Να ξεχάσει ό,τι έμαθε από τις απαντήσεις στις προτάσεις;\n(Οι αναλύσεις και τα σημάδια δεν αλλάζουν. Τα άλλα Kinovea κρατούν τη μάθησή τους.)"),
                    SwimLang.T("Μάθηση των προτάσεων"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    manager.ResetLearning();
            };
            toolTip.SetToolTip(resetLearning, SwimLang.T("Η μάθηση μοιράζεται με τα άλλα Kinovea του ίδιου Google Sheet (μόνο αριθμοί, χωρίς ονόματα ή εικόνες). Χωρίς σύνδεση μένει σε αυτόν τον υπολογιστή.\n") +
                SwimLang.T("«↺ μάθηση»: ξεκινά από την αρχή μόνο σε αυτό το Kinovea."));
            tools.Controls.Add(btnTechPilot);
            tools.Controls.Add(btnPropose);
            tools.Controls.Add(btnReview);
            tools.Controls.Add(btnProposalRight);
            tools.Controls.Add(btnProposalWrong);
            tools.Controls.Add(btnDeleteProposals);
            tools.Controls.Add(chkProposeAuto);
            tools.Controls.Add(lblProposals);
            tools.Controls.Add(resetLearning);

            // Snapshots: list on the left, the picture, the comments and the elite frame on the right.
            TableLayoutPanel snaps = new TableLayoutPanel { Dock = DockStyle.Top, Height = 330, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            snaps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            snaps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
            lbSnapshots = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Margin = new Padding(0, 0, 6, 0) };
            lbSnapshots.SelectedIndexChanged += (s, e) =>
            {
                if (updating)
                    return;
                SnapshotItem item = lbSnapshots.SelectedItem as SnapshotItem;
                selectedSnapshotId = item != null ? item.Snapshot.Id : null;
                RefreshSnapshotDetails();
            };
            lbSnapshots.DoubleClick += (s, e) =>
            {
                SnapshotItem item = lbSnapshots.SelectedItem as SnapshotItem;
                if (item != null)
                    manager.SeekTo(item.Snapshot.Timestamp);
            };

            TableLayoutPanel details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Margin = new Padding(0) };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            details.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            picSnapshot = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = SwimTheme.Input, Margin = new Padding(0, 0, 3, 3) };
            picElite = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = SwimTheme.Input, Margin = new Padding(3, 0, 0, 3) };
            toolTip.SetToolTip(picSnapshot, SwimLang.T("Το στιγμιότυπο του αθλητή (διπλό κλικ στη λίστα: μετάβαση στο καρέ)."));
            toolTip.SetToolTip(picElite, SwimLang.T("Το elite καρέ για σύγκριση (μπαίνει δίπλα στο στιγμιότυπο στο report)."));

            FlowLayoutPanel stageRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            cmbStage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 190, Margin = new Padding(3, 3, 3, 3) };
            cmbStage.TextChanged += (s, e) => CommitSnapshotText();
            lblSnapshotInfo = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(6, 7, 3, 3) };
            stageRow.Controls.Add(Caption(SwimLang.T("Στάδιο:")));
            stageRow.Controls.Add(cmbStage);
            stageRow.Controls.Add(lblSnapshotInfo);

            tbComment = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Margin = new Padding(0, 3, 0, 3) };
            tbComment.TextChanged += (s, e) => CommitSnapshotText();
            toolTip.SetToolTip(tbComment, SwimLang.T("Σχόλια για αυτή την εικόνα: μπαίνουν κάτω από την εικόνα στο report."));

            TableLayoutPanel eliteRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Margin = new Padding(0) };
            eliteRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            eliteRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            eliteRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            eliteRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cmbElite = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 4, 3) };
            cmbElite.SelectedIndexChanged += CmbElite_SelectedIndexChanged;
            toolTip.SetToolTip(cmbElite, SwimLang.T("Καρέ elite κολυμβητή στο ίδιο στάδιο, από το φύλλο «Elite καρέ» του Google Sheet."));
            Button btnEliteRefresh = new SwimNoFocusButton { Text = "↻", Width = 30, Height = 25, Margin = new Padding(0, 2, 4, 2) };
            btnEliteRefresh.Click += (s, e) => manager.RefreshEliteFrames();
            toolTip.SetToolTip(btnEliteRefresh, SwimLang.T("Ανανέωση της βιβλιοθήκης elite καρέ"));
            Button btnSaveElite = new SwimNoFocusButton { Text = SwimLang.T("Ως elite καρέ…"), AutoSize = true, Height = 25, Margin = new Padding(0, 2, 0, 2) };
            btnSaveElite.Click += (s, e) => SaveSnapshotAsElite();
            toolTip.SetToolTip(btnSaveElite, SwimLang.T("Το στιγμιότυπο μπαίνει στη βιβλιοθήκη elite καρέ (π.χ. από βίντεο elite κολυμβητή), για σύγκριση σε επόμενες αναλύσεις."));
            eliteRow.Controls.Add(Caption("Elite:"), 0, 0);
            eliteRow.Controls.Add(cmbElite, 1, 0);
            eliteRow.Controls.Add(btnEliteRefresh, 2, 0);
            eliteRow.Controls.Add(btnSaveElite, 3, 0);

            FlowLayoutPanel snapButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            Button btnSnapGo = new SwimNoFocusButton { Text = SwimLang.T("▶ βίντεο"), Width = 76, Height = 26, Margin = new Padding(0, 2, 4, 2) };
            btnSnapGo.Click += (s, e) => { SwimSnapshot snap = SelectedSnapshot(); if (snap != null) manager.SeekTo(snap.Timestamp); };
            Button btnSnapDelete = new SwimNoFocusButton { Text = SwimLang.T("Διαγραφή"), Width = 80, Height = 26, Margin = new Padding(0, 2, 4, 2), Accent = SwimTheme.Red };
            btnSnapDelete.Click += (s, e) => DeleteSnapshot();
            lblElite = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(6, 7, 3, 3) };
            snapButtons.Controls.Add(btnSnapGo);
            snapButtons.Controls.Add(btnSnapDelete);
            snapButtons.Controls.Add(lblElite);

            details.Controls.Add(picSnapshot, 0, 0);
            details.Controls.Add(picElite, 1, 0);
            details.Controls.Add(stageRow, 0, 1);
            details.SetColumnSpan(stageRow, 2);
            details.Controls.Add(tbComment, 0, 2);
            details.SetColumnSpan(tbComment, 2);
            details.Controls.Add(eliteRow, 0, 3);
            details.SetColumnSpan(eliteRow, 2);
            details.Controls.Add(snapButtons, 0, 4);
            details.SetColumnSpan(snapButtons, 2);
            snaps.Controls.Add(lbSnapshots, 0, 0);
            snaps.Controls.Add(details, 1, 0);

            // Angles of the analysis.
            Label lblAngles = new Label { Text = SwimLang.T("Γωνίες"), Dock = DockStyle.Top, Height = 24, Font = valueFont, ForeColor = SwimTheme.Accent, TextAlign = ContentAlignment.BottomLeft };
            gridAngles = CreateGrid();
            gridAngles.ReadOnly = false;
            gridAngles.Columns.Add(Column("name", SwimLang.T("Γωνία"), 120));
            gridAngles.Columns.Add(Column("deg", SwimLang.T("Μοίρες"), 60));
            gridAngles.Columns.Add(Column("phase", SwimLang.T("Φάση"), 90));
            gridAngles.Columns.Add(Column("t", SwimLang.T("Χρόνος (s)"), 64));
            foreach (DataGridViewColumn c in gridAngles.Columns)
            {
                c.ReadOnly = c.Name != "name";
                c.DefaultCellStyle.BackColor = c.ReadOnly ? SwimTheme.RowAlt : SwimTheme.Input;
            }

            gridAngles.Columns["deg"].DefaultCellStyle.ForeColor = SwimTheme.Amber;
            gridAngles.CellEndEdit += GridAngles_CellEndEdit;
            gridAngles.KeyDown += GridAngles_KeyDown;
            gridAngles.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex == 0)
                    return;
                SwimAngle a = gridAngles.Rows[e.RowIndex].Tag as SwimAngle;
                if (a != null)
                    manager.SeekTo(a.Timestamp);
            };

            // Notes of the training (in the report).
            Label lblNotes = new Label { Text = SwimLang.T("Σημειώσεις (μπαίνουν στην αρχή του report τεχνικής ανάλυσης)"), Dock = DockStyle.Bottom, Height = 22, ForeColor = SwimTheme.Dim, TextAlign = ContentAlignment.BottomLeft };
            tbNotes = new TextBox { Dock = DockStyle.Bottom, Height = 52, Multiline = true, ScrollBars = ScrollBars.Vertical };
            tbNotes.TextChanged += (s, e) =>
            {
                if (updating || manager.Race == null)
                    return;
                manager.Race.TechNotes = tbNotes.Text;
                manager.ManualValuesChanged();
            };

            page.Controls.Add(gridAngles);
            page.Controls.Add(lblAngles);
            page.Controls.Add(lblNotes);
            page.Controls.Add(tbNotes);
            page.Controls.Add(snaps);
            page.Controls.Add(tools);
            return page;
        }
        #endregion

        #region Refresh
        private SwimTechPhase SelectedPhase(ComboBox combo)
        {
            PhaseItem item = combo != null ? combo.SelectedItem as PhaseItem : null;
            return item != null ? manager.TechPhases.FirstOrDefault(p => p.Id == item.Id) : null;
        }

        private SwimSnapshot SelectedSnapshot()
        {
            if (manager.Race == null || string.IsNullOrEmpty(selectedSnapshotId))
                return null;

            return manager.Race.Snapshots.FirstOrDefault(s => s.Id == selectedSnapshotId);
        }

        /// <summary>
        /// Pages of the technical analysis (only the one shown is refreshed).
        /// </summary>
        public void RefreshTech()
        {
            if (IsDisposed || tabs == null || tabTechStart == null || manager.Race == null)
                return;

            SwimPage page = tabs.SelectedTab;
            if (page == tabTechStart)
                RefreshTechPhasePage(SwimPhaseKind.Start);
            else if (page == tabTechTurns)
                RefreshTechPhasePage(SwimPhaseKind.Turn);
            else if (page == tabTechMedia)
                RefreshTechMedia();
            RefreshSkeleton();
        }

        /// <summary>The skeleton row: reading, choice of the swimmer, parts of the video with a skeleton.</summary>
        public void RefreshSkeleton()
        {
            if (IsDisposed || lblSkeleton == null)
                return;

            bool picking = manager.PickTarget == SwimPickTarget.Skeleton;
            btnSkeleton.Text = picking ? SwimLang.T("Ακύρωση") : SwimLang.T("🦴 Σκελετός");
            btnSkeleton.Enabled = !manager.SkeletonRunning;
            string parts = manager.SkeletonDescription;
            if (manager.SkeletonRunning)
            {
                lblSkeleton.Text = SwimLang.T("ανάγνωση του βίντεο…");
                lblSkeleton.ForeColor = SwimTheme.Amber;
            }
            else if (picking)
            {
                lblSkeleton.Text = SwimLang.T("κλικ πάνω στον αθλητή");
                lblSkeleton.ForeColor = SwimTheme.Amber;
            }
            else if (parts.Length > 0)
            {
                lblSkeleton.Text = SwimLang.T("σκελετός: ") + parts;
                lblSkeleton.ForeColor = SwimTheme.Green;
            }
            else
            {
                lblSkeleton.Text = SwimLang.T("χωρίς σκελετό");
                lblSkeleton.ForeColor = SwimTheme.Dim;
            }

            btnSkeletonAngles.Enabled = parts.Length > 0 && !manager.SkeletonRunning;
            if (manager.Settings != null)
            {
                bool was = updating;
                updating = true;
                chkSkeleton.Checked = manager.Settings.SkeletonOverlay;
                chkSkeletonAngles.Checked = manager.Settings.SkeletonAngles;
                chkProposeAuto.Checked = manager.Settings.ProposeAfterSkeleton;
                chkAutoSnapshots.Checked = manager.Settings.AutoTechSnapshots;
                updating = was;
            }

            // Proposals: waiting, being checked, what was learned.
            int pending = manager.Race != null ? manager.Race.Proposals.Count : 0;
            bool reviewing = manager.Reviewing;
            btnPropose.Enabled = parts.Length > 0 && !manager.SkeletonRunning;
            btnTechPilot.Enabled = manager.HasVideo && manager.Race != null && manager.Race.IsTraining && !manager.SkeletonRunning && !manager.TechPilotRunning;
            btnReview.Enabled = pending > 0 && !reviewing;
            btnReview.Text = pending > 0 ? SwimLang.T("▶ Έλεγχος ") + pending : SwimLang.T("▶ Έλεγχος");
            btnProposalRight.Enabled = reviewing;
            btnProposalWrong.Enabled = reviewing;
            btnDeleteProposals.Enabled = pending > 0;
            lblProposals.Text = reviewing && manager.CurrentProposal != null ? SwimLang.T("ελέγχεις: ") + SwimAnalysisManager.ProposalLabel(manager.CurrentProposal) : manager.ProposalsDescription;
            lblProposals.ForeColor = reviewing ? SwimTheme.Amber : pending > 0 ? SwimTheme.Accent : SwimTheme.Dim;
            RefreshPilot();
        }

        private void RefreshTechPhasePage(SwimPhaseKind kind)
        {
            SwimRace race = manager.Race;
            List<SwimTechPhase> phases = manager.TechPhases.Where(p => p.Kind == kind).ToList();
            ComboBox combo = kind == SwimPhaseKind.Start ? cmbStartPhase : cmbTurnPhase;
            string selectedId = kind == SwimPhaseKind.Start ? selectedStartId : selectedTurnId;

            // The selected phase follows the video when the video enters another phase of this kind
            // (a phase chosen in the list stays while the video does not move to another one).
            SwimTechPhase current = manager.CurrentTechPhase;
            string videoId = current != null && current.Kind == kind && manager.HasVideo ? current.Id : null;
            string lastVideoId = kind == SwimPhaseKind.Start ? videoStartId : videoTurnId;
            if (videoId != null && videoId != lastVideoId)
                selectedId = videoId;
            if (kind == SwimPhaseKind.Start)
                videoStartId = videoId;
            else
                videoTurnId = videoId;

            updating = true;
            try
            {
                string itemsSig = string.Join("|", phases.Select(p => p.Id + ":" + p.Title).ToArray());
                string comboSig = string.Join("|", combo.Items.Cast<object>().OfType<PhaseItem>().Select(p => p.Id + ":" + p.Title).ToArray());
                if (itemsSig != comboSig)
                {
                    combo.BeginUpdate();
                    combo.Items.Clear();
                    foreach (SwimTechPhase p in phases)
                        combo.Items.Add(new PhaseItem(p));
                    combo.EndUpdate();
                }

                int index = phases.FindIndex(p => p.Id == selectedId);
                if (index < 0 && phases.Count > 0)
                    index = phases.Count - 1;
                if (combo.SelectedIndex != index)
                    combo.SelectedIndex = index;
            }
            finally
            {
                updating = false;
            }

            SwimTechPhase phase = SelectedPhase(combo);
            if (kind == SwimPhaseKind.Start)
                selectedStartId = phase != null ? phase.Id : null;
            else
                selectedTurnId = phase != null ? phase.Id : null;

            string unit = race.Unit;
            Label info = kind == SwimPhaseKind.Start ? lblStartPhaseInfo : lblTurnPhaseInfo;
            if (phases.Count == 0)
            {
                info.Text = race.IsTraining
                    ? (kind == SwimPhaseKind.Start ? SwimLang.T("Καμία εκκίνηση: πάτησε G στο καρέ του σήματος για να ξεκινήσει η πρώτη προσπάθεια.") : SwimLang.T("Καμία στροφή: πάτησε W στο καρέ της επαφής στον τοίχο."))
                    : (kind == SwimPhaseKind.Start ? SwimLang.T("Ορίσε πρώτα το σήμα εκκίνησης (G).") : SwimLang.T("Οι στροφές εμφανίζονται με το πέρασμα κάθε τοίχου (S)."));
            }
            else if (phase != null)
            {
                info.Text = string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} · {1} τιμές{2}"), phase.Title, phase.Values.Count,
                    kind == SwimPhaseKind.Turn ? (phase.Flip ? SwimLang.T(" · ανάποδη στροφή") : SwimLang.T(" · ανοιχτή στροφή")) : "");
            }

            Func<double?, string> sec = v => v.HasValue ? v.Value.ToString("0.00", CultureInfo.InvariantCulture) + " s" : "–";
            Func<string, string> val = key => { double? v = phase != null ? phase.Value(key) : null; return v.HasValue ? v.Value.ToString("0.00", CultureInfo.InvariantCulture) : null; };
            Func<int[], string> splits = offsets =>
            {
                if (phase == null)
                    return "–";
                List<string> parts = new List<string>();
                foreach (int o in offsets)
                {
                    double t;
                    if (phase.Splits.TryGetValue(o, out t))
                        parts.Add(string.Format(CultureInfo.InvariantCulture, "{0}{1}: {2:0.00}", o > 0 && kind == SwimPhaseKind.Turn ? "+" : "", o, t));
                }

                return parts.Count > 0 ? string.Join(" · ", parts.ToArray()) + " s" : "–";
            };
            Func<string> kicks = () =>
            {
                if (phase == null || phase.Kicks.Count == 0)
                    return "–";
                string rate = val("kickRate");
                return phase.Kicks.Count.ToString(CultureInfo.InvariantCulture) + (rate != null ? " · " + rate + " /s" : "");
            };
            Func<double?, string, string> dist = (d, label) => d.HasValue ? " · " + label + " " + d.Value.ToString("0.00", CultureInfo.InvariantCulture) + " " + unit : "";

            if (kind == SwimPhaseKind.Start)
            {
                lblTsSignal.Text = phase != null ? "✔ t = 0" : "–";
                lblTsReaction.Text = sec(phase != null ? phase.Reaction : null);
                lblTsHands.Text = sec(phase != null ? phase.HandsOff : null);
                lblTsBackFoot.Text = sec(phase != null ? phase.BackFoot : null);
                string blockPhase = val("blockPhase");
                lblTsBlock.Text = sec(phase != null ? phase.Block : null) + (blockPhase != null ? SwimLang.T(" · φάση βατήρα ") + blockPhase + " s" : "");
                string flight = val("flight");
                lblTsEntry.Text = sec(phase != null ? phase.Entry : null) + (flight != null ? SwimLang.T(" · πτήση ") + flight + " s" : "") + dist(phase != null ? phase.EntryDist : null, SwimLang.T("απόσταση"));
                lblTsKicks.Text = kicks();
                string uw = val("underwater");
                lblTsBreakout.Text = sec(phase != null ? phase.Breakout : null) + (uw != null ? SwimLang.T(" · υποβρύχια ") + uw + " s" : "") + dist(phase != null ? phase.BreakoutDist : null, SwimLang.T("στα"));
                lblTsSplits.Text = splits(SwimTechCalculator.StartOffsets);
                lblTsFirstStroke.Text = sec(phase != null ? phase.FirstStroke : null);
            }
            else
            {
                double tIn = 0;
                bool hasIn = phase != null && phase.Splits.TryGetValue(-5, out tIn);
                lblTtIn.Text = hasIn ? (-tIn).ToString("0.00", CultureInfo.InvariantCulture) + SwimLang.T(" s πριν την επαφή") : "–";
                lblTtLastStroke.Text = phase != null && phase.LastStroke.HasValue ? SwimLang.T("→ τοίχος ") + (-phase.LastStroke.Value).ToString("0.00", CultureInfo.InvariantCulture) + " s" : "–";
                string rotation = val("rotation");
                lblTtRotation.Text = rotation != null ? SwimLang.T("περιστροφή ") + rotation + " s" : (phase != null && phase.RotationStart.HasValue ? sec(phase.RotationStart) : "–");
                lblTtContact.Text = phase != null ? "✔ t = 0" : "–";
                string turnTime = val("turnTime");
                lblTtPushoff.Text = sec(phase != null ? phase.Pushoff : null) + (turnTime != null ? SwimLang.T(" · χρόνος στροφής ") + turnTime + " s" : "");
                lblTtKicks.Text = kicks();
                string uw = val("underwater");
                lblTtBreakout.Text = sec(phase != null ? phase.Breakout : null) + (uw != null ? SwimLang.T(" · υποβρύχια ") + uw + " s" : "") + dist(phase != null ? phase.BreakoutDist : null, SwimLang.T("στα"));
                lblTtSplits.Text = splits(new int[] { 5, 10, 15 });
                lblTtFirstStroke.Text = sec(phase != null ? phase.FirstStroke : null);
            }

            RefreshTechGrid(kind == SwimPhaseKind.Start ? gridTechStart : gridTechTurns, kind, phases, phase);
        }

        /// <summary>
        /// Values of all the phases of a kind: one row per value, one column per phase (+ best and mean for several attempts).
        /// </summary>
        private void RefreshTechGrid(DataGridView grid, SwimPhaseKind kind, List<SwimTechPhase> phases, SwimTechPhase selected)
        {
            SwimRace race = manager.Race;
            List<SwimTechMetric> metrics = SwimTechCalculator.Metrics
                .Where(m => (!m.Kind.HasValue || m.Kind.Value == kind) && phases.Any(p => p.Values.ContainsKey(m.Key)))
                .ToList();
            List<string> angles = race.Angles
                .Select(a => new { a, p = SwimTechCalculator.PhaseAt(manager.TechPhases, a.Timestamp) })
                .Where(x => x.p != null && x.p.Kind == kind && x.a.Degrees.HasValue)
                .Select(x => x.a.Name)
                .Distinct()
                .ToList();
            bool many = phases.Count > 1;

            string sig = kind + "|" + string.Join(",", phases.Select(p => p.Id + p.Title).ToArray()) + "|" + string.Join(",", metrics.Select(m => m.Key).ToArray()) + "|" + string.Join(",", angles.ToArray());
            if (grid.Tag as string != sig)
            {
                grid.Tag = sig;
                grid.Rows.Clear();
                grid.Columns.Clear();
                DataGridViewTextBoxColumn first = Column("metric", SwimLang.T("Τιμή"), 190);
                first.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                first.Width = 210;
                grid.Columns.Add(first);
                foreach (SwimTechPhase p in phases)
                {
                    DataGridViewTextBoxColumn c = Column("p" + grid.Columns.Count, p.Title, 64);
                    c.Width = 74;
                    c.Tag = p;
                    grid.Columns.Add(c);
                }

                if (many)
                {
                    DataGridViewTextBoxColumn best = Column("best", SwimLang.T("Καλύτερη"), 64);
                    best.Width = 74;
                    best.DefaultCellStyle.ForeColor = SwimTheme.Green;
                    grid.Columns.Add(best);
                    DataGridViewTextBoxColumn mean = Column("mean", SwimLang.T("Μ.ό."), 60);
                    mean.Width = 64;
                    grid.Columns.Add(mean);
                }

                foreach (SwimTechMetric m in metrics)
                    grid.Rows[grid.Rows.Add()].Cells[0].Value = SwimTechCalculator.Label(m, race.Unit);
                foreach (string name in angles)
                    grid.Rows[grid.Rows.Add()].Cells[0].Value = (name.StartsWith("Γωνία", StringComparison.OrdinalIgnoreCase) ? SwimLang.Label(name) : SwimLang.T("Γωνία: ") + SwimLang.Label(name)) + " (°)";
            }
            else
            {
                // Same structure: the phases may carry new values.
                for (int i = 0; i < phases.Count; i++)
                    grid.Columns[i + 1].Tag = phases[i];
            }

            string[] lowerIsBetter = { "reaction", "handsOff", "backFoot", "blockTime", "blockPhase", "entryTime", "in5", "rotation", "contact", "turnTime", "t5", "t10", "t15", "out5", "out10", "out15", "inOut5", "inOut10", "inOut15" };
            string[] higherIsBetter = { "entryDist", "v0_5", "v5_10", "v10_15", "uwVel", "pushVel", "bo15Vel" };
            for (int r = 0; r < metrics.Count; r++)
            {
                SwimTechMetric m = metrics[r];
                List<double> values = new List<double>();
                for (int i = 0; i < phases.Count; i++)
                {
                    double? v = phases[i].Value(m.Key);
                    grid.Rows[r].Cells[i + 1].Value = v.HasValue ? m.Format(v.Value) : "";
                    if (v.HasValue)
                        values.Add(v.Value);
                }

                if (many)
                {
                    string best = "";
                    if (values.Count > 0 && lowerIsBetter.Contains(m.Key))
                        best = m.Format(values.Min());
                    else if (values.Count > 0 && higherIsBetter.Contains(m.Key))
                        best = m.Format(values.Max());
                    grid.Rows[r].Cells[phases.Count + 1].Value = best;
                    grid.Rows[r].Cells[phases.Count + 2].Value = values.Count > 0 ? m.Format(values.Average()) : "";
                }
            }

            for (int j = 0; j < angles.Count; j++)
            {
                int r = metrics.Count + j;
                List<double> values = new List<double>();
                for (int i = 0; i < phases.Count; i++)
                {
                    SwimTechPhase p = phases[i];
                    SwimAngle a = race.Angles.FirstOrDefault(x => x.Name == angles[j] && x.Degrees.HasValue && p.Contains(x.Timestamp));
                    grid.Rows[r].Cells[i + 1].Value = a != null ? a.Degrees.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
                    if (a != null)
                        values.Add(a.Degrees.Value);
                }

                if (many)
                {
                    grid.Rows[r].Cells[phases.Count + 1].Value = "";
                    grid.Rows[r].Cells[phases.Count + 2].Value = values.Count > 0 ? values.Average().ToString("0.0", CultureInfo.InvariantCulture) : "";
                }
            }

            // The selected phase is highlighted.
            for (int i = 0; i < phases.Count; i++)
            {
                DataGridViewColumn c = grid.Columns[i + 1];
                bool on = selected != null && phases[i].Id == selected.Id;
                c.HeaderCell.Style.ForeColor = on ? SwimTheme.Amber : SwimTheme.Dim;
                c.DefaultCellStyle.ForeColor = on ? SwimTheme.Text : SwimTheme.Dim;
            }
        }

        private void RefreshTechMedia()
        {
            SwimRace race = manager.Race;
            updating = true;
            try
            {
                // Snapshots.
                List<SnapshotItem> items = race.Snapshots.OrderBy(s => s.Timestamp).Select(s =>
                {
                    SwimTechPhase p = SwimTechCalculator.PhaseAt(manager.TechPhases, s.Timestamp);
                    string text = string.Format("{0}{1}{2}{3}", p != null ? p.Title : SwimLang.T("Στιγμιότυπο"), string.IsNullOrEmpty(s.Event) ? "" : " · 📷 " + SwimLang.Label(s.Event),
                        string.IsNullOrEmpty(s.Stage) || s.Stage == s.Event ? "" : " · " + SwimLang.Label(s.Stage), string.IsNullOrEmpty(s.FileId) ? "  ⏳" : "  ✔");
                    return new SnapshotItem(s, text);
                }).ToList();
                List<SnapshotItem> shown = lbSnapshots.Items.Cast<object>().OfType<SnapshotItem>().ToList();
                bool sameList = shown.Count == items.Count && shown.Select(x => x.Snapshot.Id).SequenceEqual(items.Select(x => x.Snapshot.Id));
                if (!sameList)
                {
                    lbSnapshots.BeginUpdate();
                    lbSnapshots.Items.Clear();
                    foreach (SnapshotItem item in items)
                        lbSnapshots.Items.Add(item);
                    lbSnapshots.EndUpdate();
                }
                else
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (shown[i].Text != items[i].Text)
                            lbSnapshots.Items[i] = items[i];
                    }
                }

                int index = items.FindIndex(x => x.Snapshot.Id == selectedSnapshotId);
                if (index < 0 && items.Count > 0)
                    index = items.Count - 1;
                if (lbSnapshots.SelectedIndex != index)
                    lbSnapshots.SelectedIndex = index;
                selectedSnapshotId = index >= 0 ? items[index].Snapshot.Id : null;

                // Angles.
                List<SwimAngle> angles = race.Angles.OrderBy(a => a.Timestamp).ToList();
                if (!gridAngles.IsCurrentCellInEditMode)
                {
                    if (gridAngles.Rows.Count != angles.Count)
                    {
                        gridAngles.Rows.Clear();
                        foreach (SwimAngle a in angles)
                            gridAngles.Rows.Add();
                    }

                    for (int i = 0; i < angles.Count; i++)
                    {
                        SwimAngle a = angles[i];
                        SwimTechPhase p = SwimTechCalculator.PhaseAt(manager.TechPhases, a.Timestamp);
                        DataGridViewRow row = gridAngles.Rows[i];
                        row.Tag = a;
                        row.Cells["name"].Value = SwimLang.Label(a.Name);
                        row.Cells["deg"].Value = a.Degrees.HasValue ? a.Degrees.Value.ToString("0.0", CultureInfo.InvariantCulture) + "°" : "";
                        row.Cells["phase"].Value = p != null ? p.Title : "";
                        row.Cells["t"].Value = p != null ? manager.Timing.Seconds(a.Timestamp, p.Origin).ToString("0.00", CultureInfo.InvariantCulture) : "";
                    }
                }

                if (!tbNotes.Focused && tbNotes.Text != (race.TechNotes ?? ""))
                    tbNotes.Text = race.TechNotes ?? "";
            }
            finally
            {
                updating = false;
            }

            RefreshSnapshotDetails();
        }

        private void RefreshSnapshotDetails()
        {
            SwimSnapshot snap = SelectedSnapshot();
            updating = true;
            try
            {
                bool has = snap != null;
                cmbStage.Enabled = has;
                tbComment.Enabled = has;
                cmbElite.Enabled = has;
                SwimTechPhase phase = has ? SwimTechCalculator.PhaseAt(manager.TechPhases, snap.Timestamp) : null;
                string[] stages = (phase != null && phase.Kind == SwimPhaseKind.Turn ? stagesTurn : stagesStart).Select(SwimLang.Label).ToArray();
                if (cmbStage.Items.Count != stages.Length || (cmbStage.Items.Count > 0 && (string)cmbStage.Items[0] != stages[0]))
                {
                    cmbStage.Items.Clear();
                    cmbStage.Items.AddRange(stages);
                }

                if (!cmbStage.Focused)
                    cmbStage.Text = has ? SwimLang.Label(snap.Stage) : "";
                if (!tbComment.Focused && tbComment.Text != (has ? snap.Comment : ""))
                    tbComment.Text = has ? snap.Comment : "";
                lblSnapshotInfo.Text = has && phase != null
                    ? string.Format(CultureInfo.InvariantCulture, "t = {0:0.00} s · {1}", manager.Timing.Seconds(snap.Timestamp, phase.Origin), string.IsNullOrEmpty(snap.FileId) ? SwimLang.T("δεν έχει ανέβει ακόμη") : SwimLang.T("στο Drive"))
                    : (has ? (string.IsNullOrEmpty(snap.FileId) ? SwimLang.T("δεν έχει ανέβει ακόμη") : SwimLang.T("στο Drive")) : SwimLang.T("Πάτησε «📷 Στιγμιότυπο» (P) σε ένα καρέ του βίντεο."));

                // Elite frames: those of the kind of the phase first.
                List<SwimEliteFrame> frames = manager.EliteLibrary.For(manager.Race.Style, phase != null ? (SwimPhaseKind?)phase.Kind : null);
                List<EliteItem> eliteItems = new List<EliteItem> { new EliteItem(null) };
                int selected = 0;
                foreach (SwimEliteFrame f in frames)
                {
                    eliteItems.Add(new EliteItem(f));
                    if (has && f.FileId == snap.EliteFileId)
                        selected = eliteItems.Count - 1;
                }

                if (has && selected == 0 && !string.IsNullOrEmpty(snap.EliteFileId))
                {
                    eliteItems.Add(new EliteItem(new SwimEliteFrame { FileId = snap.EliteFileId, Label = snap.EliteLabel, Comment = snap.EliteComment }));
                    selected = eliteItems.Count - 1;
                }

                string eliteSig = string.Join("|", eliteItems.Select(x => x.Frame == null ? "" :
                    x.Frame.FileId + ":" + x.ToString() + ":" + x.Frame.Athlete + ":" + x.Frame.Comment).ToArray());
                if (eliteSig != shownEliteList)
                {
                    shownEliteList = eliteSig;
                    cmbElite.BeginUpdate();
                    cmbElite.Items.Clear();
                    foreach (EliteItem item in eliteItems)
                        cmbElite.Items.Add(item);
                    cmbElite.EndUpdate();
                }

                if (cmbElite.SelectedIndex != selected)
                    cmbElite.SelectedIndex = selected;
                lblElite.Text = manager.EliteMessage ?? "";
            }
            finally
            {
                updating = false;
            }

            ShowSnapshotImage(snap);
            ShowEliteImage(snap != null ? snap.EliteFileId : null);
        }

        private string shownSnapshotFile;

        private void ShowSnapshotImage(SwimSnapshot snap)
        {
            string file = snap != null ? manager.SnapshotPath(snap) : null;
            if (file == shownSnapshotFile)
                return;

            shownSnapshotFile = file;
            Image old = picSnapshot.Image;
            picSnapshot.Image = SwimAnalysisManager.LoadImage(file);
            if (old != null)
                old.Dispose();
        }

        private void ShowEliteImage(string fileId)
        {
            if (fileId == shownEliteId)
                return;

            shownEliteId = fileId;
            Image old = picElite.Image;
            picElite.Image = null;
            if (old != null)
                old.Dispose();

            if (string.IsNullOrEmpty(fileId))
                return;

            string wanted = fileId;
            manager.GetEliteImage(fileId, image =>
            {
                if (IsDisposed || shownEliteId != wanted)
                {
                    if (image != null)
                        image.Dispose();
                    return;
                }

                Image previous = picElite.Image;
                picElite.Image = image;
                if (previous != null)
                    previous.Dispose();
            });
        }

        /// <summary>
        /// Live strip of the technical analysis: time from the origin of the phase and its values.
        /// </summary>
        private void RefreshTechLive()
        {
            SwimRace race = manager.Race;
            SwimTechPhase phase = manager.CurrentTechPhase;
            List<SwimChip> chips = new List<SwimChip>();
            string clock = "–";
            if (phase != null && manager.HasVideo && manager.CurrentVideoTimestamp.HasValue)
            {
                double t = manager.Timing.Seconds(manager.CurrentVideoTimestamp.Value, phase.Origin);
                clock = (t < 0 ? "−" : "") + Math.Abs(t).ToString("0.00", CultureInfo.InvariantCulture);
                chips.Add(new SwimChip(SwimLang.T("ΦΑΣΗ"), phase.Title, SwimTheme.Amber));
                SwimTechPhase owner;
                int? next = SwimTechCalculator.NextOffset(race, manager.Timing, manager.TechPhases, manager.CurrentVideoTimestamp.Value, out owner);
                string nextText = !next.HasValue ? "– (S)"
                    : (next.Value < 0 ? "−5" : (owner != null && owner.Kind == SwimPhaseKind.Turn ? "+" : "") + next.Value.ToString(CultureInfo.InvariantCulture)) + race.Unit;
                chips.Add(new SwimChip(SwimLang.T("T ΕΠΟΜΕΝΟ"), nextText, next.HasValue ? SwimTheme.Accent : SwimTheme.Dim));
                int kicks = phase.Kicks.Count(k => k <= t + 1e-6);
                if (kicks > 0)
                    chips.Add(new SwimChip(SwimLang.T("ΔΕΛΦΙΝΙΣΜΟΙ"), kicks.ToString(CultureInfo.InvariantCulture), SwimTheme.Text));
                string key = phase.Kind == SwimPhaseKind.Start ? "t15" : "out15";
                double? v15 = phase.Value(key);
                if (v15.HasValue)
                    chips.Add(new SwimChip(phase.Kind == SwimPhaseKind.Start ? "15" + race.Unit : "→ +15" + race.Unit, v15.Value.ToString("0.00", CultureInfo.InvariantCulture), SwimTheme.Green));
            }
            else
            {
                chips.Add(new SwimChip(SwimLang.T("ΦΑΣΗ"), race.IsTraining ? SwimLang.T("G: νέα εκκίνηση · W: νέα στροφή") : SwimLang.T("εκτός εκκίνησης / στροφής"), SwimTheme.Dim));
            }

            chips.Add(new SwimChip(SwimLang.T("ΣΤΙΓΜΙΟΤΥΠΑ"), race.Snapshots.Count.ToString(CultureInfo.InvariantCulture), SwimTheme.Text));
            chips.Add(new SwimChip(SwimLang.T("ΓΩΝΙΕΣ"), race.Angles.Count.ToString(CultureInfo.InvariantCulture), SwimTheme.Text));
            lblLive.SetValues(phase != null ? (phase.Kind == SwimPhaseKind.Start ? SwimLang.T("ΑΠΟ ΤΟ ΣΗΜΑ") : SwimLang.T("ΑΠΟ ΤΗΝ ΕΠΑΦΗ")) : SwimLang.T("ΤΕΧΝΙΚΗ"), clock, chips);
        }

        /// <summary>
        /// The start or turn page follows the event just marked.
        /// </summary>
        public void FollowTechPhase(SwimTechPhase phase)
        {
            if (IsDisposed || phase == null)
                return;

            SwimPage current = tabs.SelectedTab;
            if (current != tabTechStart && current != tabTechTurns)
                return;

            SwimPage target = phase.Kind == SwimPhaseKind.Start ? tabTechStart : tabTechTurns;
            if (phase.Kind == SwimPhaseKind.Start)
                selectedStartId = phase.Id;
            else
                selectedTurnId = phase.Id;

            if (target != current)
                tabs.SelectedTab = target;
            else
                RefreshTech();
        }

        public void SnapshotAdded(SwimSnapshot snapshot)
        {
            if (IsDisposed || snapshot == null)
                return;

            selectedSnapshotId = snapshot.Id;
            RefreshTech();
        }

        /// <summary>
        /// Highlight the measure and angle buttons of the technical pages while their click is pending.
        /// </summary>
        private void RefreshTechPick(SwimPickTarget active)
        {
            foreach (KeyValuePair<Button, SwimPickTarget[]> pair in techPickButtons)
                Highlight(pair.Key, manager.PickTarget != SwimPickTarget.None && pair.Value.Contains(active));
        }
        #endregion

        #region Edits
        private void DeletePhase(SwimTechPhase phase)
        {
            if (phase == null)
                return;

            string what = manager.Race.IsTraining ? SwimLang.T("όλα τα σημάδια της προσπάθειας") : SwimLang.T("τα σημάδια της τεχνικής ανάλυσης (5/10/15, χέρια, πίσω πόδι, περιστροφή)");
            DialogResult answer = MessageBox.Show(this, SwimLang.T("Διαγραφή: ") + phase.Title + ", " + what + SwimLang.Question, SwimLang.T("Διαγραφή"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes)
                manager.DeletePhase(phase);
        }

        private void CommitSnapshotText()
        {
            if (updating)
                return;

            SwimSnapshot snap = SelectedSnapshot();
            if (snap == null)
                return;

            // The stage as picked or typed; unchanged (a comment typed), the value kept stays as it is (in Greek).
            string stageShown = cmbStage.Text.Trim();
            if (stageShown != (SwimLang.Label(snap.Stage) ?? "").Trim())
                snap.Stage = SwimLang.Greek(stageShown, stagesStart.Concat(stagesTurn));
            snap.Comment = tbComment.Text;
            manager.SnapshotChanged(snap);
        }

        private void CmbElite_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (updating)
                return;

            SwimSnapshot snap = SelectedSnapshot();
            EliteItem item = cmbElite.SelectedItem as EliteItem;
            if (snap == null || item == null)
                return;

            manager.SetSnapshotElite(snap, item.Frame);
            ShowEliteImage(snap.EliteFileId);
        }

        private void SaveSnapshotAsElite()
        {
            SwimSnapshot snap = SelectedSnapshot();
            if (snap == null)
            {
                ShowStatus(SwimLang.T("Επίλεξε πρώτα ένα στιγμιότυπο."), true);
                return;
            }

            SwimRace race = manager.Race;
            string athlete = string.IsNullOrWhiteSpace(race.Athlete) ? "" : race.Athlete.Trim();
            string name = AskText(SwimLang.T("Νέο elite καρέ"), SwimLang.T("Τίτλος του καρέ στη βιβλιοθήκη (π.χ. «Pan Zhanle – είσοδος στο νερό»):"),
                (athlete.Length > 0 ? athlete + " – " : "") + (string.IsNullOrEmpty(snap.Stage) ? SwimLang.T("καρέ") : SwimLang.Label(snap.Stage).ToLowerInvariant()));
            if (name == null)
                return;

            string source = string.Join(" · ", new string[] { race.Competition, race.Round, race.Date }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray());
            manager.SaveSnapshotAsElite(snap, name, athlete, source, snap.Comment);
        }

        private void DeleteSnapshot()
        {
            SwimSnapshot snap = SelectedSnapshot();
            if (snap == null)
                return;

            DialogResult answer = MessageBox.Show(this, SwimLang.T("Διαγραφή του στιγμιότυπου από την ανάλυση (και από το report);"), SwimLang.T("Στιγμιότυπο"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;

            selectedSnapshotId = null;
            manager.DeleteSnapshot(snap);
        }

        private void GridAngles_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || gridAngles.Columns[e.ColumnIndex].Name != "name")
                return;

            SwimAngle angle = gridAngles.Rows[e.RowIndex].Tag as SwimAngle;
            string name = Convert.ToString(gridAngles.Rows[e.RowIndex].Cells[e.ColumnIndex].Value, CultureInfo.InvariantCulture);
            // Only a name changed by the analyst (leaving the cell unchanged keeps the name as it is kept, in Greek).
            if (angle != null && !string.IsNullOrWhiteSpace(name) && name.Trim() != (SwimLang.Label(angle.Name) ?? "").Trim())
                BeginInvoke((Action)(() => manager.RenameAngle(angle, SwimLang.Greek(name.Trim(), angleNames))));
        }

        private void GridAngles_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete || gridAngles.IsCurrentCellInEditMode || gridAngles.CurrentRow == null)
                return;

            SwimAngle angle = gridAngles.CurrentRow.Tag as SwimAngle;
            if (angle != null)
            {
                manager.DeleteAngle(angle);
                e.Handled = true;
            }
        }
        #endregion
    }
}
