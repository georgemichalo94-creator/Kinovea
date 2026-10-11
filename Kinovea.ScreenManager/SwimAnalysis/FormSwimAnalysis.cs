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
    /// Button that never takes the keyboard focus, so the hotkeys keep working after a click (themed, see SwimButton).
    /// </summary>
    internal class SwimNoFocusButton : SwimButton
    {
    }

    /// <summary>
    /// Tool window of the swim race analysis.
    /// The tabs follow the order of the race (start, swimming, turns, finish) and inside each tab the events
    /// are listed in the order they happen, each with its button, its key and its value.
    /// The window forwards the player hotkeys (play, frame step, swim marks…) to the player screen,
    /// except while the user is typing in a text field.
    /// </summary>
    public partial class FormSwimAnalysis : Form
    {
        private static readonly Color colorNext = SwimTheme.NextRow;
        private static readonly Color colorDone = Color.Empty;
        private static readonly Color colorOk = SwimTheme.Green;
        private static readonly Color colorWarn = SwimTheme.Warn;
        private static readonly Color colorError = SwimTheme.Red;
        private static readonly Color colorBusy = SwimTheme.Accent;
        private static readonly Color colorValue = SwimTheme.Accent;
        private static readonly Color colorPick = SwimTheme.Amber;

        private readonly SwimAnalysisManager manager;
        private bool updating;
        private Timer liveTimer;
        private readonly ToolTip toolTip = new ToolTip();
        private Font boldGridFont;
        private Font buttonFont;
        private Font valueFont;

        // Header
        private ComboBox cmbRaces;
        private Button btnNewRace;
        private Button btnDeleteRace;
        private SwimLiveStrip lblLive;
        private Label lblStatus;
        private Button btnUndo;
        private SwimSegmented modeSwitch;
        private Label lblWordmark;
        private Label lblBrandSub;

        // Tabs, in race order.
        private SwimTabs tabs;
        private SwimPage tabRace;
        private SwimPage tabStart;
        private SwimPage tabSwim;
        private SwimPage tabTurns;
        private SwimPage tabFinish;
        private SwimPage tabConnection;

        private readonly List<KeyValuePair<PlayerScreenCommands, Button>> commandButtons = new List<KeyValuePair<PlayerScreenCommands, Button>>();
        private readonly Dictionary<Button, string> buttonTexts = new Dictionary<Button, string>();
        private readonly Dictionary<SwimPickTarget, Button> pickButtons = new Dictionary<SwimPickTarget, Button>();

        // Tab: race info
        private TextBox tbAthleteSearch;
        private ListBox lbAthletes;
        private Label lblAthlete;
        private Label lblAthleteList;
        private TextBox tbAthlete;
        private NumericUpDown nudLane;
        private ComboBox cmbStyle;
        private ComboBox cmbDistance;
        private ComboBox cmbPool;
        private ComboBox cmbKind;
        private TextBox tbCompetition;
        private TextBox tbLocation;
        private ComboBox cmbRound;
        private TextBox tbDate;
        private TextBox tbCheckpoints;

        // Tab: start
        private Label lblStartSignal;
        private Label lblReactionAuto;
        private TextBox tbReaction;
        private Label lblBlock;
        private Label lblEntry;
        private TextBox tbEntryDistHands;
        private TextBox tbEntryDistHips;
        private TextBox tbEntryVelHands;
        private TextBox tbEntryVelHips;
        private TextBox tbEntryVelFeet;
        private Label lblStartKicks;
        private Label lblBreakout;
        private TextBox tbBreakoutDist;
        private TextBox tbUnderwaterDist;
        private Label lblFirstSplitTitle;
        private Label lblFirstSplit;

        // Tab: swimming
        private DataGridView grid;
        private ComboBox cmbReference;
        private Label lblReferences;
        private ComboBox cmbPb;
        private Label lblPb;
        private Button btnSaveReference;
        private CheckBox chkSpeedChart;
        private CheckBox chkSplitBoard;
        private ComboBox cmbCorner;
        private static readonly string[] corners = { "TopRight", "TopLeft", "BottomRight", "BottomLeft" };
        private static readonly Color colorFaster = SwimTheme.Green;
        private static readonly Color colorSlower = SwimTheme.Red;

        /// <summary>Segments outside the clean swimming (conventional velocity, no stroke rate / length), as in the sheet.</summary>
        private static readonly Color colorPhase = SwimTheme.PhaseCell;
        private LinkLabel lnkChecks;

        // Tab: turns
        private Label lblTurnCurrent;
        private Label lblTurnGap;
        private Label lblTurnWall;
        private Label lblTurnTime;
        private Label lblTurnKicks;
        private Label lblTurnUnderwater;
        private DataGridView gridTurns;
        private int currentTurnWall;

        // Tab: finish
        private Label lblFinishGap;
        private Label lblFinalTime;
        private TextBox tbFinishDist;
        private Label lblFinishSummary;
        private TextBox tbChecks;
        private SwimTile tileDak;
        private SwimTile tileFinal;
        private SwimTile tileVel;
        private SwimTile tileSr;
        private SwimTile tileDps;
        private SwimTile tileStart;
        private SwimTile tileTurns;
        private DataGridView grid50;

        // Calibration (start, turns and finish tabs)
        private SwimCard grpCalibration;
        private ComboBox cmbLine1;
        private ComboBox cmbLine2;
        private TextBox tbLaneWidth;
        private CheckBox chkFixedCamera;
        private Button btnCalibrate;
        private Button btnDeleteCalibration;
        private Label lblCalibration;
        private int calibrationPool = -1;

        // Tab: connection
        private TextBox tbUrl;
        private TextBox tbToken;
        private ComboBox cmbRounding;
        private CheckBox chkAutoSend;
        private Label lblConnection;

        // Footer
        private Label lblSync;
        private Button btnSend;
        private Button btnOpenSheet;
        private Button btnPdf;

        public FormSwimAnalysis(SwimAnalysisManager manager)
        {
            this.manager = manager;
            BuildUI();
            RefreshAll();

            liveTimer = new Timer();
            liveTimer.Interval = 200;
            liveTimer.Tick += (s, e) => RefreshLive();
            liveTimer.Start();
        }

        /// <summary>
        /// Show the window next to the right edge of the Kinovea main window.
        /// </summary>
        public void ShowNear(Form owner)
        {
            StartPosition = FormStartPosition.Manual;
            Rectangle area = owner != null ? owner.Bounds : Screen.PrimaryScreen.WorkingArea;
            Rectangle screen = Screen.FromRectangle(area).WorkingArea;
            int x = Math.Max(screen.Left, Math.Min(area.Right - Width - 16, screen.Right - Width));
            int y = Math.Max(screen.Top, Math.Min(area.Top + 90, screen.Bottom - Height));
            Location = new Point(x, y);

            if (owner != null)
                Show(owner);
            else
                Show();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SwimTheme.DarkTitleBar(Handle);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            liveTimer.Stop();
            liveTimer.Dispose();
            toolTip.Dispose();
            if (boldGridFont != null)
                boldGridFont.Dispose();
            base.OnFormClosed(e);
        }

        #region UI construction
        private void BuildUI()
        {
            Text = "Swim Analytics · Ανάλυση αγώνα κολύμβησης";
            Font = new Font("Segoe UI", 9f);
            buttonFont = SwimTheme.Ui(8.5f, FontStyle.Bold);
            valueFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            BackColor = SwimTheme.Back;
            ForeColor = SwimTheme.Text;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            Size = new Size(690, 900);
            MinimumSize = new Size(580, 640);

            // Brand line: wordmark and the switch between the race analysis and the technical analysis.
            Panel brand = new Panel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 6, 8, 4) };
            lblWordmark = new Label { Text = "SWIM ANALYTICS", AutoSize = true, Font = SwimTheme.Display(13.5f), ForeColor = SwimTheme.Text, Location = new Point(8, 9) };
            lblBrandSub = new Label { Text = "Κ.Ο.Ε. · KINOVEA", AutoSize = true, Font = SwimTheme.Ui(7f, FontStyle.Bold), ForeColor = SwimTheme.Accent, Location = new Point(170, 16) };
            modeSwitch = new SwimSegmented("RACE ANALYSIS", "ΤΕΧΝΙΚΗ ΑΝΑΛΥΣΗ") { Size = new Size(300, 28), Location = new Point(360, 7), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            modeSwitch.SelectedIndexChanged += (s, e) => SetTechnicalView(modeSwitch.SelectedIndex == 1, true);
            toolTip.SetToolTip(modeSwitch, "Race analysis: περάσματα, χεριές, στροφές και τερματισμός του αγώνα.\n" +
                "Τεχνική ανάλυση: φάσεις της εκκίνησης και των στροφών, χρόνοι 5/10/15, γωνίες, στιγμιότυπα με σχόλια και elite καρέ.");
            brand.Controls.Add(lblWordmark);
            brand.Controls.Add(lblBrandSub);
            brand.Controls.Add(modeSwitch);
            brand.Resize += (s, e) =>
            {
                modeSwitch.Left = brand.ClientSize.Width - modeSwitch.Width - 8;
                lblBrandSub.Left = lblWordmark.Right + 6;
            };
            brand.Paint += (s, e) =>
            {
                using (System.Drawing.Drawing2D.LinearGradientBrush line = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(1, brand.Width), 2), SwimTheme.Accent, SwimTheme.Back, System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(line, 0, brand.Height - 2, brand.Width, 2);
            };

            // Header: race selector.
            Panel header = new Panel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 6, 4) };
            Label lblRace = new Label { Text = "Ανάλυση:", AutoSize = true, Location = new Point(6, 11), ForeColor = SwimTheme.Dim };
            cmbRaces = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(72, 7), Width = 330, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            cmbRaces.SelectedIndexChanged += CmbRaces_SelectedIndexChanged;
            btnNewRace = new SwimNoFocusButton { Text = "+ Νέος αθλητής", Width = 120, Height = 26, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnNewRace.Location = new Point(header.Width - 160, 6);
            btnNewRace.Click += (s, e) => manager.NewRace();
            btnDeleteRace = new SwimNoFocusButton { Text = "✕", Width = 30, Height = 26, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnDeleteRace.Location = new Point(header.Width - 46, 6);
            btnDeleteRace.Click += BtnDeleteRace_Click;
            toolTip.SetToolTip(btnDeleteRace, "Διαγραφή αυτής της ανάλυσης");
            header.Controls.Add(lblRace);
            header.Controls.Add(cmbRaces);
            header.Controls.Add(btnNewRace);
            header.Controls.Add(btnDeleteRace);
            header.Resize += (s, e) =>
            {
                btnDeleteRace.Left = header.ClientSize.Width - btnDeleteRace.Width - 6;
                btnNewRace.Left = btnDeleteRace.Left - btnNewRace.Width - 6;
                cmbRaces.Width = Math.Max(120, btnNewRace.Left - cmbRaces.Left - 8);
            };

            // Live line: race time, next checkpoint, strokes, delta, ΔΑΚ, phase + undo.
            Panel live = new Panel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(6, 4, 6, 4) };
            lblLive = new SwimLiveStrip { Dock = DockStyle.Fill };
            btnUndo = new SwimNoFocusButton { Dock = DockStyle.Right, Width = 150, Font = buttonFont, Accent = SwimTheme.Red };
            btnUndo.Click += (s, e) => RunCommand(PlayerScreenCommands.SwimUndo);
            toolTip.SetToolTip(btnUndo, "Αναίρεση του τελευταίου σημαδιού");
            RegisterCommandButton(PlayerScreenCommands.SwimUndo, btnUndo, "Αναίρεση");
            live.Controls.Add(lblLive);
            live.Controls.Add(btnUndo);

            lblStatus = new Label { Dock = DockStyle.Top, Height = 34, AutoEllipsis = true, ForeColor = SwimTheme.Dim, Padding = new Padding(8, 2, 8, 0) };

            // Footer: sync status.
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(6), BackColor = SwimTheme.Surface };
            lblSync = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Font = SwimTheme.Ui(8.5f, FontStyle.Bold) };
            FlowLayoutPanel footerButtons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            btnSend = new SwimNoFocusButton { Text = "Αποστολή τώρα", AutoSize = true, Height = 28, Primary = true };
            btnSend.Click += (s, e) => manager.SendNow(false);
            btnOpenSheet = new SwimNoFocusButton { Text = "Άνοιγμα φύλλου", AutoSize = true, Height = 28 };
            btnOpenSheet.Click += (s, e) => manager.OpenSheet();
            btnPdf = new SwimNoFocusButton { Text = "PDF report", AutoSize = true, Height = 28 };
            btnPdf.Click += (s, e) => manager.SendNow(true);
            footerButtons.Controls.Add(btnSend);
            footerButtons.Controls.Add(btnOpenSheet);
            footerButtons.Controls.Add(btnPdf);
            footer.Controls.Add(lblSync);
            footer.Controls.Add(footerButtons);

            grpCalibration = BuildCalibrationGroup();

            tabRace = BuildRaceTab();
            tabStart = BuildStartTab();
            tabSwim = BuildSwimTab();
            tabTurns = BuildTurnsTab();
            tabFinish = BuildFinishTab();
            tabConnection = BuildConnectionTab();
            BuildTechPages();

            tabs = new SwimTabs { Dock = DockStyle.Fill };
            tabs.AddPage(tabRace);
            tabs.AddPage(tabStart);
            tabs.AddPage(tabSwim);
            tabs.AddPage(tabTurns);
            tabs.AddPage(tabFinish);
            tabs.AddPage(tabTechStart);
            tabs.AddPage(tabTechTurns);
            tabs.AddPage(tabTechMedia);
            tabs.AddPage(tabConnection);
            tabs.SelectedTab = tabStart;
            tabs.SelectedIndexChanged += (s, e) => { UpdateCalibrationVisibility(); RefreshTech(); };

            Controls.Add(tabs);
            Controls.Add(grpCalibration);
            Controls.Add(footer);
            Controls.Add(lblStatus);
            Controls.Add(live);
            Controls.Add(header);
            Controls.Add(brand);
            SwimTheme.Apply(this);
            SetTechnicalView(false, false);
            UpdateCalibrationVisibility();
        }

        /// <summary>
        /// Race analysis or technical analysis: the pages of the mode, the overlay of the video and the hotkeys stay the same.
        /// A training video has only the technical analysis.
        /// </summary>
        private void SetTechnicalView(bool technical, bool fromUser)
        {
            SwimRace race = manager.Race;
            if (!technical && race != null && race.IsTraining)
            {
                technical = true;
                if (fromUser)
                    ShowStatus("Βίντεο προπόνησης: μόνο τεχνική ανάλυση. Για ανάλυση αγώνα άλλαξε το «Είδος βίντεο» στα Στοιχεία.", true);
            }

            modeSwitch.SetSelected(technical ? 1 : 0);
            manager.TechnicalView = technical;
            SwimPage selected = tabs.SelectedTab;
            List<SwimPage> pages = technical
                ? new List<SwimPage> { tabRace, tabTechStart, tabTechTurns, tabTechMedia, tabConnection }
                : new List<SwimPage> { tabRace, tabStart, tabSwim, tabTurns, tabFinish, tabConnection };
            tabs.ShowPages(pages);
            if (fromUser || !pages.Contains(selected))
            {
                if (technical)
                    tabs.SelectedTab = tabTechStart;
                else
                    tabs.SelectedTab = race != null && race.StartTimestamp.HasValue ? tabSwim : tabStart;
            }

            UpdateCalibrationVisibility();
            RefreshTech();
            manager.RefreshOverlay();
        }

        private SwimPage BuildStartTab()
        {
            SwimPage page = new SwimPage("1 · Εκκίνηση");
            page.Padding = new Padding(6);
            page.AutoScroll = true;
            TableLayoutPanel t = NewEventTable();

            lblStartSignal = ValueLabel();
            AddEventRow(t, "Σήμα εκκίνησης", PlayerScreenCommands.SwimStart, "Εκκίνηση",
                "Στο καρέ του σήματος εκκίνησης (καπνός/φλας/ήχος): εκεί γίνεται t = 0.", lblStartSignal);

            lblReactionAuto = ValueLabel();
            tbReaction = NumberBox();
            toolTip.SetToolTip(tbReaction, "Χρόνος από το σήμα έως την πρώτη κίνηση, αν τον μετράς αλλιώς. Αν συμπληρωθεί, υπερισχύει του σημαδιού.\n" +
                "Μην γράφεις εδώ τον επίσημο χρόνο αντίδρασης των αποτελεσμάτων (≈0.6 s): αυτός αντιστοιχεί στην αποδέσμευση από τον βατήρα.");
            AddEventRow(t, "Αντίδραση (πρώτη κίνηση)", PlayerScreenCommands.SwimReaction, "Αντίδραση",
                "Η πρώτη κίνηση του αθλητή μετά το σήμα (όχι ο επίσημος χρόνος αντίδρασης, που είναι η αποδέσμευση από τον βατήρα).",
                lblReactionAuto, Caption("ή γράψε:"), tbReaction, Unit("s"));

            lblBlock = ValueLabel();
            AddEventRow(t, "Αποδέσμευση από τον βατήρα", PlayerScreenCommands.SwimFeetOff, "Αποδέσμευση",
                "Τα πόδια φεύγουν από τον βατήρα (χρόνος πάνω στον βατήρα). Αντιστοιχεί στον επίσημο χρόνο αντίδρασης των αποτελεσμάτων.", lblBlock);

            lblEntry = ValueLabel();
            tbEntryDistHands = NumberBox();
            AddEventRow(t, "Είσοδος χεριών στο νερό", PlayerScreenCommands.SwimEntry, "Είσοδος χεριών",
                "Τα χέρια μπαίνουν στο νερό: χρόνος από το σήμα και χρόνος πτήσης (αποδέσμευση → είσοδος). Η απόσταση μετριέται από τον τοίχο εκκίνησης.",
                lblEntry, Caption("απόσταση:"), tbEntryDistHands, Unit("m"),
                PickButton(SwimPickTarget.EntryHands, "Κλικ στο βίντεο εκεί που τα χέρια μπαίνουν στο νερό: υπολογίζεται η απόσταση από τον τοίχο εκκίνησης."));

            tbEntryDistHips = NumberBox();
            AddEventRow(t, "Είσοδος ισχίων", null, null,
                "Τα ισχία μπαίνουν στο νερό. Η απόσταση μετριέται από τον τοίχο εκκίνησης.",
                Caption("απόσταση:"), tbEntryDistHips, Unit("m"),
                PickButton(SwimPickTarget.EntryHips, "Κλικ στο βίντεο εκεί που τα ισχία μπαίνουν στο νερό."));

            tbEntryVelHands = NumberBox();
            tbEntryVelHips = NumberBox();
            tbEntryVelFeet = NumberBox();
            AddEventRow(t, "Ταχύτητες εισόδου (m/s)", null, null,
                "Ταχύτητα των χεριών, των ισχίων και των ποδιών τη στιγμή της εισόδου.",
                Caption("χέρια"), tbEntryVelHands, Caption("ισχία"), tbEntryVelHips, Caption("πόδια"), tbEntryVelFeet);

            lblStartKicks = ValueLabel();
            AddEventRow(t, "Υποβρύχιοι δελφινισμοί", PlayerScreenCommands.SwimKick, "Δελφινισμός",
                "Ένα πάτημα για κάθε υποβρύχιο δελφινισμό.", lblStartKicks);

            lblBreakout = ValueLabel();
            tbBreakoutDist = NumberBox();
            AddEventRow(t, "Έξοδος από το νερό (κεφάλι)", PlayerScreenCommands.SwimBreakout, "Έξοδος",
                "Το κεφάλι βγαίνει από το νερό (χρόνος κάτω από το νερό). Η απόσταση μετριέται από τον τοίχο εκκίνησης.",
                lblBreakout, Caption("απόσταση:"), tbBreakoutDist, Unit("m"),
                PickButton(SwimPickTarget.StartBreakout, "Κλικ στο κεφάλι εκεί που βγαίνει από το νερό."));

            tbUnderwaterDist = NumberBox();
            AddEventRow(t, "Απόσταση κάτω από το νερό", null, null,
                "Μέχρι πού έμεινε κάτω από το νερό, από τον τοίχο εκκίνησης.",
                tbUnderwaterDist, Unit("m"),
                PickButton(SwimPickTarget.StartUnderwater, "Κλικ στο κεφάλι εκεί που φτάνει στην επιφάνεια."));

            lblFirstSplit = ValueLabel();
            lblFirstSplitTitle = AddEventRow(t, "Πέρασμα 15μ.", PlayerScreenCommands.SwimSplit, "Πέρασμα",
                "Το κεφάλι περνά το πρώτο σημείο ελέγχου. Μετά συνεχίζεις στην καρτέλα Κολύμβηση.", lblFirstSplit);

            page.Controls.Add(t);
            return page;
        }

        private SwimPage BuildSwimTab()
        {
            SwimPage page = new SwimPage("2 · Κολύμβηση");
            page.Padding = new Padding(6);

            // Comparison with an elite reference and what is drawn on the video.
            TableLayoutPanel compare = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = new Padding(0), Padding = new Padding(0, 0, 0, 2) };
            compare.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            compare.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            compare.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            compare.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label lblCompare = new Label { Text = "Σύγκριση με (elite):", AutoSize = true, Font = valueFont, ForeColor = colorValue, Margin = new Padding(0, 7, 4, 3) };
            cmbReference = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 4, 3) };
            cmbReference.SelectedIndexChanged += CmbReference_SelectedIndexChanged;
            toolTip.SetToolTip(cmbReference, "Αναφορά για το Δ σε κάθε πέρασμα (αρνητικό = πιο γρήγορα από την αναφορά). Δείχνει τις αναφορές του ίδιου αγωνίσματος, στυλ και πισίνας.");
            Button btnRefreshReferences = new SwimNoFocusButton { Text = "↻", Width = 30, Height = 25, Margin = new Padding(0, 2, 4, 2) };
            btnRefreshReferences.Click += (s, e) => manager.RefreshReferences();
            toolTip.SetToolTip(btnRefreshReferences, "Ανανέωση των αναφορών από το φύλλο «Elite αναφορές» του Google Sheet");
            btnSaveReference = new SwimNoFocusButton { Text = "Ως αναφορά…", AutoSize = true, Height = 25, Margin = new Padding(0, 2, 0, 2) };
            btnSaveReference.Click += (s, e) => SaveAsReference();
            toolTip.SetToolTip(btnSaveReference, "Αποθήκευση αυτής της ανάλυσης ως elite αναφοράς (π.χ. ανάλυση ενός τελικού), για σύγκριση σε επόμενες αναλύσεις.");
            lblReferences = new Label { AutoSize = false, Height = 18, Dock = DockStyle.Fill, ForeColor = SwimTheme.Dim, AutoEllipsis = true, Margin = new Padding(0, 0, 0, 2) };
            compare.Controls.Add(lblCompare, 0, 0);
            compare.Controls.Add(cmbReference, 1, 0);
            compare.Controls.Add(btnRefreshReferences, 2, 0);
            compare.Controls.Add(btnSaveReference, 3, 0);
            compare.Controls.Add(lblReferences, 0, 1);
            compare.SetColumnSpan(lblReferences, 4);

            // Previous race of the athlete (PB) for the comparison tables and charts of the report.
            Label lblPbTitle = new Label { Text = "Σύγκριση στο report (PB):", AutoSize = true, Font = valueFont, ForeColor = colorValue, Margin = new Padding(0, 7, 4, 3) };
            cmbPb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 4, 3) };
            cmbPb.SelectedIndexChanged += CmbPb_SelectedIndexChanged;
            toolTip.SetToolTip(cmbPb, "Προηγούμενη ανάλυση του αθλητή στο ίδιο αγώνισμα για τους πίνακες και τα γραφήματα PB – αγώνας – elite του report.\n" +
                "Αυτόματα: ο καλύτερος χρόνος του αθλητή πριν από αυτόν τον αγώνα.");
            Button btnRefreshPb = new SwimNoFocusButton { Text = "↻", Width = 30, Height = 25, Margin = new Padding(0, 2, 4, 2) };
            btnRefreshPb.Click += (s, e) => manager.RefreshPbCandidates();
            toolTip.SetToolTip(btnRefreshPb, "Ανανέωση της λίστας των αναλύσεων του αθλητή από το Google Sheet");
            lblPb = new Label { AutoSize = false, Height = 18, Dock = DockStyle.Fill, ForeColor = SwimTheme.Dim, AutoEllipsis = true, Margin = new Padding(0, 0, 0, 2) };
            compare.Controls.Add(lblPbTitle, 0, 2);
            compare.Controls.Add(cmbPb, 1, 2);
            compare.Controls.Add(btnRefreshPb, 2, 2);
            compare.Controls.Add(lblPb, 0, 3);
            compare.SetColumnSpan(lblPb, 4);

            FlowLayoutPanel overlay = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0, 0, 0, 4) };
            chkSpeedChart = new CheckBox { Text = "Γράφημα ταχύτητας", AutoSize = true, Margin = new Padding(6, 5, 3, 3) };
            chkSplitBoard = new CheckBox { Text = "Χρόνος, πέρασμα, Δ", AutoSize = true, Margin = new Padding(6, 5, 3, 3) };
            cmbCorner = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 112, Margin = new Padding(3, 3, 3, 3) };
            cmbCorner.Items.AddRange(new object[] { "Πάνω δεξιά", "Πάνω αριστερά", "Κάτω δεξιά", "Κάτω αριστερά" });
            chkSpeedChart.CheckedChanged += (s, e) => CommitOverlayOptions();
            chkSplitBoard.CheckedChanged += (s, e) => CommitOverlayOptions();
            cmbCorner.SelectedIndexChanged += (s, e) => CommitOverlayOptions();
            toolTip.SetToolTip(chkSpeedChart, "Γράφημα με την ταχύτητα κάθε τμήματος πάνω στο βίντεο (αθλητής και αναφορά). Ακολουθεί τον χρόνο του βίντεο.\n" +
                "Συνεχής γραμμή: καθαρή κολύμβηση · διάστικτη: εκκίνηση, στροφές και τελευταία 5μ. (συμβατική ταχύτητα).");
            toolTip.SetToolTip(chkSplitBoard, "Χρόνος αγώνα, τελευταίο πέρασμα και Δ με την αναφορά πάνω στο βίντεο.");
            overlay.Controls.Add(Caption("Στο βίντεο:"));
            overlay.Controls.Add(chkSpeedChart);
            overlay.Controls.Add(chkSplitBoard);
            overlay.Controls.Add(Caption("θέση"));
            overlay.Controls.Add(cmbCorner);

            TableLayoutPanel t = NewEventTable();
            AddEventRow(t, "Χεριά", PlayerScreenCommands.SwimStroke, "Χεριά",
                "Σε κάθε είσοδο χεριού. Ελεύθερο/ύπτιο: κάθε χέρι · πεταλούδα/πρόσθιο: κάθε κύκλος.", Caption("Οι μισές χεριές στα σημεία ελέγχου βγαίνουν αυτόματα."));
            AddEventRow(t, "Πέρασμα σημείου ελέγχου", PlayerScreenCommands.SwimSplit, "Πέρασμα",
                "Το κεφάλι περνά το επόμενο σημείο ελέγχου (25μ., 35μ., …). Στους τοίχους: το χέρι ακουμπά τον τοίχο.", Caption("Η κίτρινη γραμμή είναι το επόμενο σημείο."));

            grid = CreateGrid();
            grid.Columns.Add(Column("d", "Απόσταση", 62));
            grid.Columns.Add(Column("t", "Χρόνος", 62));
            grid.Columns.Add(Column("seg", "Τμήμα", 56));
            grid.Columns.Add(Column("vel", "Ταχ. m/s", 58));
            grid.Columns.Add(Column("strokes", "Χεριές", 52));
            grid.Columns.Add(Column("sr", "SR", 50));
            grid.Columns.Add(Column("dps", "DPS", 50));
            grid.Columns.Add(Column("sl", "SL", 46));
            grid.Columns.Add(Column("srx", "SR χρόνων", 64));
            grid.Columns.Add(Column("ref", "Αναφορά", 62));
            grid.Columns.Add(Column("delta", "Δ", 54));
            grid.Columns["vel"].ToolTipText = "Μέση ταχύτητα του τμήματος. Πορτοκαλί: εκκίνηση, στροφή ή τελευταία 5μ. πριν τον τοίχο, όπου η ταχύτητα είναι συμβατική (στον τοίχο μετράει η επαφή, όχι το κεφάλι).";
            grid.Columns["sr"].ToolTipText = "Συχνότητα (κύκλοι/λεπτό), μόνο στην καθαρή κολύμβηση: από 15μ. μετά τον τοίχο έως 5μ. πριν τον επόμενο.";
            grid.Columns["dps"].ToolTipText = "Μέτρα ανά χεριά (ελεύθερο/ύπτιο: ανά χέρι), μόνο στην καθαρή κολύμβηση.";
            grid.Columns["sl"].ToolTipText = "Μήκος κύκλου (m/κύκλο), μόνο στην καθαρή κολύμβηση. Ελεύθερο/ύπτιο: 2 × DPS.";
            grid.Columns["srx"].ToolTipText = "Συχνότητα από τα διαστήματα ανάμεσα στα πατήματα μέσα στο τμήμα (δεν επηρεάζεται από το γλίστρημα). Με λίγες χεριές είναι ενδεικτική.";
            grid.CellDoubleClick += Grid_CellDoubleClick;
            grid.KeyDown += Grid_KeyDown;

            Label hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 32,
                ForeColor = SwimTheme.Dim,
                Text = "Διπλό κλικ: μετάβαση του βίντεο στο σημείο · Delete: διαγραφή του χρόνου · SR / DPS / SL μόνο στην καθαρή κολύμβηση (+15μ. έως −5μ.) · πορτοκαλί: συμβατική ταχύτητα"
            };

            lnkChecks = new LinkLabel { Dock = DockStyle.Bottom, Height = 34, AutoEllipsis = true, Visible = false, LinkColor = colorWarn, ActiveLinkColor = colorError, Padding = new Padding(0, 2, 0, 0) };
            lnkChecks.LinkClicked += (s, e) => tabs.SelectedTab = tabFinish;
            toolTip.SetToolTip(lnkChecks, "Έλεγχοι των δεδομένων: όλοι στην καρτέλα Τερματισμός.");

            page.Controls.Add(grid);
            page.Controls.Add(lnkChecks);
            page.Controls.Add(hint);
            page.Controls.Add(t);
            page.Controls.Add(overlay);
            page.Controls.Add(compare);
            return page;
        }

        private SwimPage BuildTurnsTab()
        {
            SwimPage page = new SwimPage("3 · Στροφές");
            page.Padding = new Padding(6);

            lblTurnCurrent = new Label { Dock = DockStyle.Top, Height = 24, Font = valueFont, ForeColor = colorBusy, TextAlign = ContentAlignment.MiddleLeft };

            TableLayoutPanel t = NewEventTable();
            lblTurnGap = ValueLabel();
            AddEventRow(t, "Τελευταία χεριά πριν τον τοίχο", PlayerScreenCommands.SwimStroke, "Χεριά",
                "Η τελευταία είσοδος χεριού πριν τη στροφή. Στις ανάποδες στροφές (ελεύθερο/ύπτιο) από εδώ μετράει ο χρόνος στροφής.", lblTurnGap);
            lblTurnWall = ValueLabel();
            AddEventRow(t, "Επαφή στον τοίχο", PlayerScreenCommands.SwimSplit, "Πέρασμα",
                "Το πέρασμα στον τοίχο (το χέρι ή τα πόδια ακουμπούν τον τοίχο).", lblTurnWall);
            lblTurnTime = ValueLabel();
            AddEventRow(t, "Αποκόλληση από τον τοίχο", PlayerScreenCommands.SwimFeetOff, "Αποκόλληση",
                "Τα πόδια φεύγουν από τον τοίχο: δίνει τον χρόνο στροφής. Ανάποδη στροφή (ελεύθερο/ύπτιο): από την τελευταία είσοδο χεριού. " +
                "Ανοιχτή (πεταλούδα, πρόσθιο, αλλαγή στυλ): από την επαφή στον τοίχο.", lblTurnTime);
            lblTurnKicks = ValueLabel();
            AddEventRow(t, "Υποβρύχιοι δελφινισμοί", PlayerScreenCommands.SwimKick, "Δελφινισμός",
                "Ένα πάτημα για κάθε υποβρύχιο δελφινισμό μετά τη στροφή.", lblTurnKicks);
            lblTurnUnderwater = ValueLabel();
            AddEventRow(t, "Έξοδος από το νερό (κεφάλι)", PlayerScreenCommands.SwimBreakout, "Έξοδος",
                "Το κεφάλι βγαίνει από το νερό. Τα μέτρα κάτω από το νερό μετριούνται από τον τοίχο της στροφής.",
                lblTurnUnderwater,
                PickButton(SwimPickTarget.TurnUnderwater, "Κλικ στο κεφάλι εκεί που βγαίνει από το νερό: μέτρα κάτω από το νερό από τον τοίχο της στροφής."));

            gridTurns = CreateGrid();
            gridTurns.ReadOnly = false;
            gridTurns.Columns.Add(Column("wall", "Τοίχος", 70));
            gridTurns.Columns.Add(Column("uwDist", "Απόσταση κάτω από το νερό", 92));
            gridTurns.Columns.Add(Column("turnTime", "Χρόνος στροφής (s)", 82));
            gridTurns.Columns["turnTime"].ToolTipText = "Ανάποδη στροφή (ελεύθερο/ύπτιο): τελευταία είσοδος χεριού → αποκόλληση. Ανοιχτή (πεταλούδα, πρόσθιο, αλλαγή στυλ): επαφή → αποκόλληση. Σκυταλοδρομία: χρόνος αλλαγής. Γράφεται και με το χέρι.";
            gridTurns.Columns.Add(Column("uwTime", "Υποβρύχιος χρόνος (s)", 86));
            gridTurns.Columns.Add(Column("kicks", "Δελφινισμοί", 72));
            gridTurns.Columns.Add(Column("gap", "Τελ. χεριά → τοίχος (s)", 90));
            foreach (DataGridViewColumn c in gridTurns.Columns)
            {
                c.ReadOnly = c.Name != "uwDist" && c.Name != "turnTime";
                c.DefaultCellStyle.BackColor = c.ReadOnly && c.Name != "wall" ? SwimTheme.RowAlt : SwimTheme.Input;
            }
            gridTurns.CellEndEdit += GridTurns_CellEndEdit;
            gridTurns.CellDoubleClick += GridTurns_CellDoubleClick;

            Label lblTurns = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Text = "Όλες οι στροφές: τα λευκά κελιά γράφονται και με το χέρι (το χειροκίνητο υπερισχύει). Διπλό κλικ: μετάβαση του βίντεο στον τοίχο.",
                ForeColor = SwimTheme.Dim,
                Padding = new Padding(0, 6, 0, 0)
            };

            page.Controls.Add(gridTurns);
            page.Controls.Add(lblTurns);
            page.Controls.Add(t);
            page.Controls.Add(lblTurnCurrent);
            return page;
        }

        private SwimPage BuildFinishTab()
        {
            SwimPage page = new SwimPage("4 · Τερματισμός & ΔΑΚ");
            page.Padding = new Padding(6);
            TableLayoutPanel t = NewEventTable();

            lblFinishGap = ValueLabel();
            AddEventRow(t, "Τελευταία χεριά", PlayerScreenCommands.SwimStroke, "Χεριά",
                "Η τελευταία είσοδος χεριού πριν τον τερματισμό.", lblFinishGap);

            lblFinalTime = ValueLabel();
            AddEventRow(t, "Τερματισμός (επαφή στον τοίχο)", PlayerScreenCommands.SwimSplit, "Τερματισμός",
                "Το χέρι ακουμπά τον τοίχο: τελικός χρόνος (το τελευταίο σημείο ελέγχου).", lblFinalTime);

            tbFinishDist = NumberBox();
            AddEventRow(t, "Απόσταση τελευταίας χεριάς", null, null,
                "Απόσταση από τον τοίχο τερματισμού τη στιγμή της τελευταίας χεριάς.",
                tbFinishDist, Unit("m"),
                PickButton(SwimPickTarget.FinishLastStroke, "Κλικ στο σημείο εισόδου του χεριού στην τελευταία χεριά: απόσταση από τον τοίχο τερματισμού."));

            lblFinishSummary = new Label { Dock = DockStyle.Top, Height = 40, ForeColor = SwimTheme.Dim, Padding = new Padding(0, 6, 0, 0) };

            // Summary tiles: ΔΑΚ first.
            FlowLayoutPanel tiles = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 132, WrapContents = true, Padding = new Padding(0, 6, 0, 0) };
            tileDak = new SwimTile();
            tileFinal = new SwimTile();
            tileVel = new SwimTile();
            tileSr = new SwimTile();
            tileDps = new SwimTile();
            tileStart = new SwimTile();
            tileTurns = new SwimTile();
            foreach (SwimTile tile in new SwimTile[] { tileDak, tileFinal, tileVel, tileSr, tileDps, tileStart, tileTurns })
                tiles.Controls.Add(tile);
            tileDak.Size = new Size(130, 58);
            toolTip.SetToolTip(tileDak, "ΔΑΚ (Δείκτης Απόδοσης Κολύμβησης): (χεριές + χρόνος από τα 15 έως τον τοίχο) / (μέτρα / 10), για κάθε μήκος. Μέσος όρος των 50άρι.");

            Label lbl50 = new Label { Text = "Ανά 50 · ΔΑΚ", Dock = DockStyle.Top, Height = 22, Font = valueFont, ForeColor = SwimTheme.Amber, TextAlign = ContentAlignment.BottomLeft };
            grid50 = CreateGrid();
            grid50.Dock = DockStyle.Top;
            grid50.Height = 150;
            grid50.Columns.Add(Column("b", "50", 70));
            grid50.Columns.Add(Column("time", "Χρόνος", 56));
            grid50.Columns.Add(Column("strokes", "Χεριές", 50));
            grid50.Columns.Add(Column("swolf", "Swolf", 50));
            grid50.Columns.Add(Column("dak", "ΔΑΚ", 54));
            grid50.Columns.Add(Column("sr", "SR", 46));
            grid50.Columns.Add(Column("dps", "DPS", 46));
            grid50.Columns.Add(Column("vel", "Ταχ. m/s", 56));
            grid50.Columns["dak"].DefaultCellStyle.ForeColor = SwimTheme.Amber;
            grid50.Columns["dak"].DefaultCellStyle.Font = SwimTheme.Ui(9f, FontStyle.Bold);
            grid50.Columns["dak"].ToolTipText = "ΔΑΚ: (χεριές + χρόνος από τα 15 έως τον τοίχο) / (μέτρα / 10), για κάθε μήκος του 50άρι (πισίνα 50μ. /3,5 όπως στο Excel).";
            grid50.Columns["sr"].ToolTipText = "Συχνότητα της καθαρής κολύμβησης του 50άρι (σταθμισμένη).";
            grid50.Columns["dps"].ToolTipText = "Μήκος χεριάς της καθαρής κολύμβησης του 50άρι (σταθμισμένο).";

            Label lblChecks = new Label { Text = "Έλεγχος δεδομένων", Dock = DockStyle.Top, Height = 24, Font = valueFont, ForeColor = colorValue, TextAlign = ContentAlignment.BottomLeft };
            tbChecks = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle };
            toolTip.SetToolTip(tbChecks, "Πιθανά λάθη στα σημάδια (χαμένο ή διπλό πάτημα, πέρασμα σε λάθος σημείο, σειρά της εκκίνησης) και τιμές έξω από τα ρεαλιστικά όρια.");

            page.Controls.Add(tbChecks);
            page.Controls.Add(lblChecks);
            page.Controls.Add(grid50);
            page.Controls.Add(lbl50);
            page.Controls.Add(lblFinishSummary);
            page.Controls.Add(tiles);
            page.Controls.Add(t);
            return page;
        }

        private SwimCard BuildCalibrationGroup()
        {
            SwimCard group = new SwimCard { Title = "Μέτρηση αποστάσεων στο βίντεο (βαθμονόμηση πισίνας)", Dock = DockStyle.Bottom, Height = 112 };

            FlowLayoutPanel row1 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            cmbLine1 = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 50 };
            cmbLine2 = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 50 };
            tbLaneWidth = new TextBox { Width = 40 };
            toolTip.SetToolTip(cmbLine1, "Απόσταση της πρώτης γραμμής από τον τοίχο εκκίνησης (π.χ. 0 = ο τοίχος, 5 = αλλαγή χρώματος στα σχοινιά).");
            toolTip.SetToolTip(cmbLine2, "Απόσταση της δεύτερης γραμμής από τον τοίχο εκκίνησης (π.χ. 5 ή 15).");
            toolTip.SetToolTip(tbLaneWidth, "Απόσταση ανάμεσα στα δύο σχοινιά του διαδρόμου (2.5 μ. σε ολυμπιακή πισίνα).");
            tbLaneWidth.TextChanged += (s, e) => CommitLaneWidth();
            row1.Controls.Add(Caption("Γραμμές στα"));
            row1.Controls.Add(cmbLine1);
            row1.Controls.Add(Caption("και"));
            row1.Controls.Add(cmbLine2);
            row1.Controls.Add(Caption("από τον τοίχο εκκίνησης (μονάδες πισίνας) · διάδρομος"));
            row1.Controls.Add(tbLaneWidth);
            row1.Controls.Add(Unit("μ."));

            FlowLayoutPanel row2 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            btnCalibrate = new SwimNoFocusButton { Text = "Βαθμονόμηση (4 κλικ)", AutoSize = true, Height = 26 };
            btnCalibrate.Click += (s, e) => manager.BeginCalibration(ParseLine(cmbLine1, 0), ParseLine(cmbLine2, 5));
            toolTip.SetToolTip(btnCalibrate, "Σε σταματημένο καρέ: κλικ στις δύο γραμμές πάνω στα δύο σχοινιά του διαδρόμου (Α, Β, Α, Β).");
            btnDeleteCalibration = new SwimNoFocusButton { Text = "Διαγραφή", AutoSize = true, Height = 26 };
            btnDeleteCalibration.Click += (s, e) => manager.DeleteCalibration();
            toolTip.SetToolTip(btnDeleteCalibration, "Διαγραφή της βαθμονόμησης αυτού του καρέ (ή της πιο κοντινής).");
            chkFixedCamera = new CheckBox { Text = "Σταθερή κάμερα", AutoSize = true, Margin = new Padding(10, 6, 3, 3) };
            chkFixedCamera.CheckedChanged += (s, e) =>
            {
                if (updating)
                    return;
                manager.Settings.FixedCamera = chkFixedCamera.Checked;
                manager.Settings.Save();
                RefreshCalibrationLabel();
            };
            toolTip.SetToolTip(chkFixedCamera, "Σταθερή κάμερα: μία βαθμονόμηση για όλο το βίντεο. Αν η κάμερα ακολουθεί τον αθλητή, άφησέ το κενό: κάθε απόσταση μετριέται σε καρέ με δική του βαθμονόμηση.");
            row2.Controls.Add(btnCalibrate);
            row2.Controls.Add(btnDeleteCalibration);
            row2.Controls.Add(chkFixedCamera);

            lblCalibration = new Label { Dock = DockStyle.Fill, ForeColor = SwimTheme.Dim, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };

            group.Controls.Add(lblCalibration);
            group.Controls.Add(row2);
            group.Controls.Add(row1);
            return group;
        }

        private SwimPage BuildRaceTab()
        {
            SwimPage page = new SwimPage("Στοιχεία αγώνα");
            page.Padding = new Padding(8);
            TableLayoutPanel table = NewTable();

            // Athlete from the registry of the main Google Sheet (KOE App).
            TableLayoutPanel searchRow = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tbAthleteSearch = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0) };
            Button btnReloadAthletes = new SwimNoFocusButton { Text = "↻", Width = 30, Height = 24, Margin = new Padding(0) };
            btnReloadAthletes.Click += (s, e) => manager.RefreshAthletes();
            toolTip.SetToolTip(btnReloadAthletes, "Ανανέωση της λίστας αθλητών από το Google Sheet");
            toolTip.SetToolTip(tbAthleteSearch, "Γράψε μέρος του ονόματος, το μητρώο ή το σωματείο. Enter: επιλογή του πρώτου.");
            searchRow.Controls.Add(tbAthleteSearch, 0, 0);
            searchRow.Controls.Add(btnReloadAthletes, 1, 0);
            lbAthletes = new ListBox { Height = 110, IntegralHeight = false };
            lblAthlete = new Label { AutoSize = false, Height = 34, ForeColor = SwimTheme.Accent };
            lblAthleteList = new Label { AutoSize = false, Height = 20, ForeColor = SwimTheme.Dim };
            tbAthleteSearch.TextChanged += (s, e) => FilterAthletes();
            tbAthleteSearch.KeyDown += TbAthleteSearch_KeyDown;
            lbAthletes.Click += (s, e) => ChooseAthlete(lbAthletes.SelectedItem as SwimAthlete);
            lbAthletes.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    ChooseAthlete(lbAthletes.SelectedItem as SwimAthlete);
                    e.Handled = true;
                }
            };

            tbAthlete = new TextBox();
            nudLane = new NumericUpDown { Minimum = 0, Maximum = 10, Width = 60 };
            cmbStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbStyle.Items.AddRange(SwimPresets.StyleNames);
            cmbDistance = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbDistance.Items.AddRange(SwimPresets.DistanceChoices);
            cmbPool = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbPool.Items.AddRange(SwimPresets.PoolNames);
            toolTip.SetToolTip(cmbPool, "Πισίνα 50μ., 25μ. ή 25 γιαρδών. Στις γιάρδες οι αποστάσεις είναι σε yd και οι ταχύτητες / μήκη χεριάς σε m (με τις τιμές σε yd στο report).");
            cmbKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbKind.Items.AddRange(new object[] { "Αγώνας (race analysis + τεχνική εκκίνησης/στροφών)", "Προπόνηση: προσπάθειες εκκινήσεων / στροφών (τεχνική ανάλυση)" });
            toolTip.SetToolTip(cmbKind, "Βίντεο προπόνησης: κάθε σήμα εκκίνησης (G) ξεκινά μια προσπάθεια εκκίνησης και κάθε επαφή στον τοίχο (W) μια προσπάθεια στροφής.");
            tbCompetition = new TextBox();
            tbLocation = new TextBox();
            cmbRound = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
            cmbRound.Items.AddRange(new object[] { "Heats", "Semi-final", "Final", "Προκριματικός", "Ημιτελικός", "Τελικός" });
            tbDate = new TextBox();
            tbCheckpoints = new TextBox { Multiline = true, Height = 64, ScrollBars = ScrollBars.Vertical };
            Button btnDefaults = new SwimNoFocusButton { Text = "Προεπιλεγμένα σημεία", AutoSize = true, Height = 26 };
            btnDefaults.Click += (s, e) => ApplyDefaultCheckpoints();

            AddRow(table, "Είδος βίντεο", cmbKind);
            AddRow(table, "Αθλητής (από KOE App)", searchRow);
            AddRow(table, "", lbAthletes);
            AddRow(table, "", lblAthlete);
            AddRow(table, "", lblAthleteList);
            AddRow(table, "Όνομα στο report", tbAthlete);
            AddRow(table, "Διάδρομος", nudLane);
            AddRow(table, "Στυλ", cmbStyle);
            AddRow(table, "Απόσταση", cmbDistance);
            AddRow(table, "Πισίνα", cmbPool);
            AddRow(table, "Διοργάνωση", tbCompetition);
            AddRow(table, "Τόπος", tbLocation);
            AddRow(table, "Φάση", cmbRound);
            AddRow(table, "Ημερομηνία", tbDate);
            AddRow(table, "Σημεία ελέγχου", tbCheckpoints);
            AddRow(table, "", btnDefaults);

            Label note = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 70,
                ForeColor = SwimTheme.Dim,
                Text = "Τα σημεία ελέγχου είναι οι αποστάσεις όπου πατάς «Πέρασμα», με τη σειρά (σε μονάδες της πισίνας: μ. ή yd). Με την αλλαγή απόστασης ή πισίνας μπαίνουν τα προεπιλεγμένα (όπως στο Excel: 15/25/35/45/50 ανά μήκος έως 200μ., 15/25/45/50 στα 400μ., 15/45/50 στα 800/1500μ., 15/20/25 ανά μήκος στις πισίνες 25μ. και 25yd)."
            };
            table.Controls.Add(note, 0, table.RowCount);
            table.SetColumnSpan(note, 2);
            table.RowCount++;

            tbAthlete.TextChanged += (s, e) => CommitRaceInfo(false);
            nudLane.ValueChanged += (s, e) => CommitRaceInfo(false);
            cmbStyle.SelectedIndexChanged += (s, e) => CommitRaceInfo(false);
            cmbDistance.SelectedIndexChanged += (s, e) => CommitDistance();
            cmbPool.SelectedIndexChanged += (s, e) => CommitDistance();
            cmbKind.SelectedIndexChanged += (s, e) => CommitKind();
            tbCompetition.TextChanged += (s, e) => CommitRaceInfo(false);
            tbLocation.TextChanged += (s, e) => CommitRaceInfo(false);
            cmbRound.TextChanged += (s, e) => CommitRaceInfo(false);
            tbDate.TextChanged += (s, e) => CommitRaceInfo(false);
            tbCheckpoints.Leave += (s, e) => CommitCheckpoints();

            page.Controls.Add(table);
            return page;
        }

        private SwimPage BuildConnectionTab()
        {
            SwimPage page = new SwimPage("Σύνδεση");
            page.Padding = new Padding(8);
            TableLayoutPanel table = NewTable();

            tbUrl = new TextBox();
            tbToken = new TextBox();
            cmbRounding = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbRounding.Items.AddRange(new object[] { "Μισές χεριές (0.5) – όπως στο Excel", "Ακέραιες χεριές", "Ακριβές (με δεκαδικά)" });
            chkAutoSend = new CheckBox { Text = "Αποστολή στο Google Sheet σε κάθε πάτημα", AutoSize = true };
            Button btnSave = new SwimNoFocusButton { Text = "Αποθήκευση", AutoSize = true, Height = 28, Primary = true };
            btnSave.Click += (s, e) => SaveConnection();
            Button btnTest = new SwimNoFocusButton { Text = "Δοκιμή σύνδεσης", AutoSize = true, Height = 28 };
            btnTest.Click += (s, e) => TestConnection();
            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            buttons.Controls.Add(btnSave);
            buttons.Controls.Add(btnTest);
            lblConnection = new Label { AutoSize = false, Height = 60, Dock = DockStyle.Fill };

            AddRow(table, "Διεύθυνση Web App (…/exec)", tbUrl);
            AddRow(table, "Token", tbToken);
            AddRow(table, "Χεριές στα σημεία ελέγχου", cmbRounding);
            AddRow(table, "", chkAutoSend);
            AddRow(table, "", buttons);
            AddRow(table, "", lblConnection);

            Label help = new Label
            {
                AutoSize = false,
                Height = 150,
                Dock = DockStyle.Fill,
                ForeColor = SwimTheme.Dim,
                Text = "Ρύθμιση μία φορά:\n" +
                       "1. Στο Google Sheet: Επεκτάσεις → Apps Script, επικόλληση του SwimRaceAnalysis.gs, αποθήκευση.\n" +
                       "2. Στο φύλλο: μενού Swim Analysis → 1. Αρχική ρύθμιση (δίνει το token).\n" +
                       "3. Apps Script: Ανάπτυξη → Νέα ανάπτυξη → Εφαρμογή Ιστού, Εκτέλεση ως: Εγώ, Πρόσβαση: Οποιοσδήποτε.\n" +
                       "4. Αντέγραψε εδώ τη διεύθυνση (…/exec) και το token, πάτησε Αποθήκευση και Δοκιμή σύνδεσης."
            };
            table.Controls.Add(help, 0, table.RowCount);
            table.SetColumnSpan(help, 2);
            table.RowCount++;

            page.Controls.Add(table);
            return page;
        }

        /// <summary>
        /// Table of events: number, event, button with its key, values.
        /// </summary>
        private static TableLayoutPanel NewEventTable()
        {
            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = new Padding(0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return t;
        }

        /// <summary>
        /// Add an event of the race (in the order it happens). Returns the title label.
        /// </summary>
        private Label AddEventRow(TableLayoutPanel t, string title, PlayerScreenCommands? command, string buttonText, string description, params Control[] values)
        {
            int row = t.RowCount;
            t.RowCount = row + 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label number = new Label
            {
                Text = (row + 1).ToString(CultureInfo.InvariantCulture),
                AutoSize = false,
                Width = 22,
                Height = 22,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = SwimTheme.Ui(8f, FontStyle.Bold),
                ForeColor = SwimTheme.Accent,
                BackColor = SwimTheme.Raised,
                Margin = new Padding(0, 8, 2, 3)
            };

            Label label = new Label { Text = title, AutoSize = false, Width = 148, Height = 38, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(2, 2, 2, 2) };
            toolTip.SetToolTip(label, description);

            Control button;
            if (command.HasValue)
            {
                PlayerScreenCommands c = command.Value;
                Button b = new SwimNoFocusButton { Width = 140, Height = 34, Font = buttonFont, Margin = new Padding(2, 3, 2, 3) };
                b.Click += (s, e) => RunCommand(c);
                toolTip.SetToolTip(b, description);
                RegisterCommandButton(c, b, buttonText);
                button = b;
            }
            else
            {
                button = new Label { Text = "", AutoSize = false, Width = 140, Height = 34, Margin = new Padding(2) };
            }

            FlowLayoutPanel flow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(2, 4, 2, 2) };
            foreach (Control v in values)
                flow.Controls.Add(v);

            t.Controls.Add(number, 0, row);
            t.Controls.Add(label, 1, row);
            t.Controls.Add(button, 2, row);
            t.Controls.Add(flow, 3, row);
            return label;
        }

        private void RegisterCommandButton(PlayerScreenCommands command, Button button, string text)
        {
            button.Text = text;
            buttonTexts[button] = text;
            commandButtons.Add(new KeyValuePair<PlayerScreenCommands, Button>(command, button));
        }

        private void RunCommand(PlayerScreenCommands command)
        {
            if (!manager.ExecutePlayerCommand(command))
                ShowStatus("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής.", true);
        }

        private Button PickButton(SwimPickTarget target, string tooltip)
        {
            Button b = new SwimNoFocusButton { Text = "📍 Μέτρηση", AutoSize = true, Height = 26, Margin = new Padding(6, 2, 2, 2) };
            b.Click += (s, e) => manager.BeginMeasure(target, SelectedTurnWall());
            toolTip.SetToolTip(b, tooltip + " (χρειάζεται βαθμονόμηση: δες κάτω)");
            pickButtons[target] = b;
            return b;
        }

        private Label ValueLabel()
        {
            return new Label { AutoSize = true, Font = valueFont, ForeColor = colorValue, Margin = new Padding(3, 7, 8, 3), Text = "–" };
        }

        private static Label Caption(string text)
        {
            return new Label { AutoSize = true, Text = text, ForeColor = SwimTheme.Dim, Margin = new Padding(3, 7, 0, 3) };
        }

        private static Label Unit(string text)
        {
            return new Label { AutoSize = true, Text = text, ForeColor = SwimTheme.Dim, Margin = new Padding(0, 7, 3, 3) };
        }

        private static TableLayoutPanel NewTable()
        {
            TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }

        private static void AddRow(TableLayoutPanel table, string label, Control control)
        {
            int row = table.RowCount;
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };
            if (!(control is CheckBox) && !(control is Button) && !(control is FlowLayoutPanel) && !(control is NumericUpDown))
                control.Dock = DockStyle.Fill;
            control.Margin = new Padding(3);
            table.Controls.Add(l, 0, row);
            table.Controls.Add(control, 1, row);
        }

        private TextBox NumberBox()
        {
            TextBox tb = new TextBox { Width = 52, Margin = new Padding(3, 3, 2, 3), TextAlign = HorizontalAlignment.Right };
            tb.TextChanged += (s, e) => CommitManualValues();
            return tb;
        }

        private static DataGridView CreateGrid()
        {
            DataGridView g = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            g.RowTemplate.Height = 22;
            SwimTheme.StyleGrid(g);
            return g;
        }

        private static DataGridViewTextBoxColumn Column(string name, string header, int minWidth)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name;
            c.HeaderText = header;
            c.MinimumWidth = minWidth;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            return c;
        }
        #endregion

        #region Refresh from the manager
        public void RefreshAll()
        {
            updating = true;
            try
            {
                cmbRaces.Items.Clear();
                if (manager.VideoFile != null)
                {
                    foreach (SwimRace race in manager.VideoFile.Races)
                        cmbRaces.Items.Add(new RaceItem(race));

                    for (int i = 0; i < cmbRaces.Items.Count; i++)
                    {
                        if (((RaceItem)cmbRaces.Items[i]).Race == manager.Race)
                            cmbRaces.SelectedIndex = i;
                    }
                }

                SwimRace r = manager.Race;
                bool hasRace = r != null;
                tabs.Enabled = hasRace;
                if (hasRace)
                {
                    tbAthlete.Text = r.Athlete;
                    nudLane.Value = Math.Max(0, Math.Min(10, r.Lane));
                    cmbStyle.SelectedIndex = Math.Max(0, Array.IndexOf(SwimPresets.Styles, r.Style));
                    cmbKind.SelectedIndex = r.IsTraining ? 1 : 0;
                    FillDistances(r, r.DistanceLabel);
                    cmbPool.SelectedIndex = SwimPresets.PoolIndex(r);
                    tbCompetition.Text = r.Competition;
                    tbLocation.Text = r.Location;
                    cmbRound.Text = r.Round;
                    tbDate.Text = r.Date;
                    tbCheckpoints.Text = SwimPresets.FormatCheckpoints(r.Checkpoints);
                }

                SwimSettings settings = manager.Settings;
                tbUrl.Text = settings.WebAppUrl;
                tbToken.Text = settings.Token;
                cmbRounding.SelectedIndex = settings.StrokeRounding >= 1 ? 1 : (settings.StrokeRounding > 0 ? 0 : 2);
                chkAutoSend.Checked = settings.AutoSend;
                chkSpeedChart.Checked = settings.ShowSpeedChart;
                chkSplitBoard.Checked = settings.ShowSplitBoard;
                cmbCorner.SelectedIndex = Math.Max(0, Array.IndexOf(corners, settings.OverlayCorner));
            }
            finally
            {
                updating = false;
            }

            if (manager.Race != null)
                SetTechnicalView(manager.Race.IsTraining || manager.TechnicalView, false);

            RefreshManualFields();
            RefreshButtons();
            RefreshAthletes();
            RefreshReferences();
            RefreshPb();
            RefreshData();
            RefreshSync();
            RefreshCalibration();
            RefreshPick();
            if (!string.IsNullOrEmpty(manager.LastStatus))
                ShowStatus(manager.LastStatus, manager.LastStatusIsWarning);
        }

        /// <summary>
        /// Distances of the pool unit (meters or yards), with the distance of the race selected.
        /// </summary>
        private void FillDistances(SwimRace race, string select)
        {
            string[] choices = SwimPresets.DistancesFor(race);
            cmbDistance.BeginUpdate();
            cmbDistance.Items.Clear();
            cmbDistance.Items.AddRange(choices);
            int index = Array.IndexOf(choices, select);
            if (index < 0 && !string.IsNullOrEmpty(select))
            {
                cmbDistance.Items.Add(select);
                index = cmbDistance.Items.Count - 1;
            }

            cmbDistance.SelectedIndex = Math.Max(0, index);
            cmbDistance.EndUpdate();
        }

        /// <summary>
        /// Previous analyses of the athlete for the PB of the report.
        /// </summary>
        public void RefreshPb()
        {
            if (IsDisposed || cmbPb == null)
                return;

            SwimRace race = manager.Race;
            updating = true;
            try
            {
                cmbPb.BeginUpdate();
                cmbPb.Items.Clear();
                cmbPb.Items.Add(new SwimAnalysisItem { Id = "auto", Label = "Αυτόματα: ο καλύτερος προηγούμενος χρόνος" });
                cmbPb.Items.Add(new SwimAnalysisItem { Id = "", Label = "Χωρίς σύγκριση με προηγούμενο αγώνα" });
                int selected = 0;
                if (race != null)
                {
                    if (race.ComparePb == "")
                        selected = 1;

                    foreach (SwimAnalysisItem item in manager.PbCandidates)
                    {
                        cmbPb.Items.Add(item);
                        if (item.Id == race.ComparePb)
                            selected = cmbPb.Items.Count - 1;
                    }

                    if (selected == 0 && race.ComparePb != "auto" && !string.IsNullOrEmpty(race.ComparePb))
                    {
                        cmbPb.Items.Add(new SwimAnalysisItem { Id = race.ComparePb, Label = string.IsNullOrEmpty(race.ComparePbLabel) ? race.ComparePb : race.ComparePbLabel });
                        selected = cmbPb.Items.Count - 1;
                    }
                }

                cmbPb.SelectedIndex = selected;
                cmbPb.EndUpdate();
            }
            finally
            {
                updating = false;
            }

            lblPb.Text = manager.PbMessage ?? "";
            cmbPb.Enabled = race != null && !race.IsTraining;
        }

        private void CmbPb_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (updating)
                return;

            SwimAnalysisItem item = cmbPb.SelectedItem as SwimAnalysisItem;
            if (item != null)
                manager.SetComparePb(item.Id, item.Id == "auto" || item.Id == "" ? "" : item.Label);
        }

        /// <summary>
        /// Values typed (or measured on the video) for the current race.
        /// </summary>
        public void RefreshManualFields()
        {
            SwimRace r = manager.Race;
            if (r == null || IsDisposed)
                return;

            updating = true;
            try
            {
                SetText(tbReaction, Format(r.Reaction));
                SetText(tbEntryDistHands, Format(r.EntryDistHands));
                SetText(tbEntryDistHips, Format(r.EntryDistHips));
                SetText(tbEntryVelHands, Format(r.EntryVelHands));
                SetText(tbEntryVelHips, Format(r.EntryVelHips));
                SetText(tbEntryVelFeet, Format(r.EntryVelFeet));
                SetText(tbUnderwaterDist, Format(r.UnderwaterDist));
                SetText(tbBreakoutDist, Format(r.BreakoutDist));
                SetText(tbFinishDist, Format(r.FinishLastStrokeDist));
            }
            finally
            {
                updating = false;
            }
        }

        private static void SetText(TextBox tb, string text)
        {
            // Keep what the user is typing ("0." or "3,4") when the value is the same.
            if (tb.Focused && ParseText(tb.Text) == ParseText(text))
                return;

            if (tb.Text != text)
                tb.Text = text;
        }

        /// <summary>
        /// Show the current key bindings on the command buttons.
        /// </summary>
        private void RefreshButtons()
        {
            HotkeyCommand[] hotkeys = HotkeySettingsManager.LoadHotkeys("PlayerScreen") ?? new HotkeyCommand[0];
            foreach (KeyValuePair<PlayerScreenCommands, Button> pair in commandButtons)
            {
                HotkeyCommand command = hotkeys.FirstOrDefault(h => h != null && h.CommandCode == (int)pair.Key);
                string key = command != null && command.KeyData != Keys.None ? KeyName(command.KeyData) : "";
                string baseText = buttonTexts[pair.Value];
                SwimButton themed = pair.Value as SwimButton;
                if (themed != null)
                {
                    themed.Text = baseText;
                    themed.Key = key;
                }
                else
                {
                    pair.Value.Text = string.IsNullOrEmpty(key) ? baseText : baseText + " [" + key + "]";
                }
            }
        }

        private static string KeyName(Keys keys)
        {
            if (keys == Keys.Back)
                return "Backspace";

            return new KeysConverter().ConvertToString(keys);
        }

        public void RefreshData()
        {
            SwimRace race = manager.Race;
            SwimComputed computed = manager.Computed;
            if (race == null || computed == null || IsDisposed)
                return;

            // Race list label may have changed (athlete name).
            for (int i = 0; i < cmbRaces.Items.Count; i++)
            {
                RaceItem item = (RaceItem)cmbRaces.Items[i];
                if (item.Race == race && item.Label != race.DisplayName)
                {
                    updating = true;
                    cmbRaces.Items[i] = new RaceItem(race);
                    cmbRaces.SelectedIndex = i;
                    updating = false;
                }
            }

            int next = race.NextCheckpoint();
            int firstVisible = grid.FirstDisplayedScrollingRowIndex;
            int selected = grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Index : -1;

            if (grid.Rows.Count != computed.Rows.Count)
            {
                grid.Rows.Clear();
                foreach (SwimRow row in computed.Rows)
                    grid.Rows.Add();
            }

            int nextRowIndex = -1;
            for (int i = 0; i < computed.Rows.Count; i++)
            {
                SwimRow row = computed.Rows[i];
                DataGridViewRow gr = grid.Rows[i];
                gr.Tag = row.D;
                gr.Cells["d"].Value = row.D + race.Unit + (row.Style != race.Style && race.Style == "im" ? " " + row.Style : "");
                gr.Cells["t"].Value = Format(row.T);
                gr.Cells["seg"].Value = Format(row.SegTime);
                gr.Cells["vel"].Value = Format(row.Vel);
                gr.Cells["vel"].Style.BackColor = row.IsFree ? Color.Empty : colorPhase;
                gr.Cells["vel"].ToolTipText = row.VelYd.HasValue ? row.VelYd.Value.ToString("0.00", CultureInfo.InvariantCulture) + " yd/s" : "";
                gr.Cells["strokes"].Value = FormatStrokes(row.Strokes);
                gr.Cells["sr"].Value = Format(row.Sr, "0.0");
                gr.Cells["dps"].Value = Format(row.Dps);
                gr.Cells["sl"].Value = Format(row.Sl);
                gr.Cells["srx"].Value = Format(row.SrExact, "0.0");
                gr.Cells["ref"].Value = Format(row.RefT);
                DataGridViewCell deltaCell = gr.Cells["delta"];
                deltaCell.Value = row.Delta.HasValue ? SwimReference.FormatDelta(row.Delta.Value) : "";
                deltaCell.Style.ForeColor = row.Delta.HasValue && row.Delta.Value > 0.0049 ? colorSlower : colorFaster;
                bool isNext = row.D == next;
                gr.DefaultCellStyle.BackColor = isNext ? colorNext : colorDone;
                if (boldGridFont == null)
                    boldGridFont = new Font(grid.Font, FontStyle.Bold);
                gr.DefaultCellStyle.Font = isNext ? boldGridFont : grid.Font;
                if (isNext)
                    nextRowIndex = i;
            }

            bool hasReference = computed.Reference != null;
            if (grid.Columns["ref"].Visible != hasReference)
            {
                grid.Columns["ref"].Visible = hasReference;
                grid.Columns["delta"].Visible = hasReference;
            }

            if (selected >= 0 && selected < grid.Rows.Count)
                grid.Rows[selected].Selected = true;
            else
                grid.ClearSelection();

            // Keep the next checkpoint visible.
            if (nextRowIndex >= 0 && grid.DisplayedRowCount(false) > 0)
            {
                int visible = grid.DisplayedRowCount(false);
                if (nextRowIndex < firstVisible || nextRowIndex >= firstVisible + visible)
                    grid.FirstDisplayedScrollingRowIndex = Math.Max(0, nextRowIndex - visible / 2);
            }

            RefreshPhases(race, computed);
            RefreshSummary(race, computed);
            RefreshTech();
            RefreshLive();
        }

        /// <summary>
        /// Tiles (ΔΑΚ first) and the 50s of the race with the ΔΑΚ.
        /// </summary>
        private void RefreshSummary(SwimRace race, SwimComputed computed)
        {
            string u = race.IsYards ? "yd" : "m";
            tileDak.Set("ΔΑΚ", Format(computed.AvgDak), "μέσος όρος 50άρι", SwimTheme.Amber, true);
            tileFinal.Set("Τελικός χρόνος", computed.FinalTime.HasValue ? SwimReference.FormatTime(computed.FinalTime.Value) : "", "", SwimTheme.Text, false);
            tileVel.Set("Μέση ταχύτητα", Format(computed.AvgVel), race.IsYards && computed.AvgVel.HasValue ? (computed.AvgVel.Value / SwimRace.Yard).ToString("0.00", CultureInfo.InvariantCulture) + " yd/s" : "m/s", SwimTheme.Text, false);
            tileSr.Set("Συχνότητα", Format(computed.AvgSr, "0.0"), "κύκλοι/λεπτό", SwimTheme.Accent, false);
            tileDps.Set("Μήκος χεριάς", Format(computed.AvgDps), "m/χεριά", SwimTheme.Accent, false);
            tileStart.Set("Εκκίνηση 0-15" + u, Format(computed.Start15), "s", SwimTheme.Text, false);
            double turns = computed.Turns.Where(t => t.InOut.HasValue).Sum(t => t.InOut.Value);
            bool anyTurn = computed.Turns.Any(t => t.InOut.HasValue);
            tileTurns.Set("Στροφές −5/+15", anyTurn ? turns.ToString("0.00", CultureInfo.InvariantCulture) : "",
                anyTurn && computed.FinalTime.HasValue && computed.FinalTime.Value > 0 ? (turns / computed.FinalTime.Value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "% του χρόνου" : "s", SwimTheme.Text, false);

            if (grid50.Rows.Count != computed.Blocks.Count)
            {
                grid50.Rows.Clear();
                foreach (SwimBlock b in computed.Blocks)
                    grid50.Rows.Add();
            }

            for (int i = 0; i < computed.Blocks.Count; i++)
            {
                SwimBlock b = computed.Blocks[i];
                DataGridViewRow gr = grid50.Rows[i];
                gr.Cells["b"].Value = b.From.ToString(CultureInfo.InvariantCulture) + "–" + b.To.ToString(CultureInfo.InvariantCulture) + race.Unit;
                gr.Cells["time"].Value = Format(b.Time);
                gr.Cells["strokes"].Value = FormatStrokes(b.Strokes);
                gr.Cells["swolf"].Value = Format(b.Swolf, "0.0");
                gr.Cells["dak"].Value = Format(b.Dak);
                gr.Cells["dak"].ToolTipText = b.DakYd.HasValue ? "ανά 10yd: " + b.DakYd.Value.ToString("0.00", CultureInfo.InvariantCulture) : "";
                gr.Cells["sr"].Value = Format(b.Sr, "0.0");
                gr.Cells["dps"].Value = Format(b.Dps);
                gr.Cells["vel"].Value = Format(b.Vel);
            }
        }

        private void RefreshPhases(SwimRace race, SwimComputed computed)
        {
            // Start, in the order of the events.
            lblStartSignal.Text = race.StartTimestamp.HasValue ? "✔ t = 0" : "δεν έχει οριστεί";
            lblStartSignal.ForeColor = race.StartTimestamp.HasValue ? colorOk : colorWarn;
            lblReactionAuto.Text = Seconds(computed.ReactionTime);
            lblBlock.Text = Seconds(computed.BlockTime);
            lblEntry.Text = Seconds(computed.EntryTime) + (computed.FlightTime.HasValue ? " · πτήση " + Seconds(computed.FlightTime) : "");
            lblStartKicks.Text = computed.StartKicks.HasValue ? computed.StartKicks.Value.ToString(CultureInfo.InvariantCulture) : "–";
            lblBreakout.Text = Seconds(computed.StartUnderwaterTime);

            SwimRow first = computed.Rows.FirstOrDefault();
            lblFirstSplitTitle.Text = first != null ? "Πέρασμα " + first.D + race.Unit : "Πρώτο πέρασμα";
            lblFirstSplit.Text = first != null ? Seconds(first.T) : "–";

            // Finish.
            lblFinishGap.Text = computed.FinishLastStrokeGap.HasValue ? "τελ. χεριά → τοίχος " + Seconds(computed.FinishLastStrokeGap) : "–";
            lblFinalTime.Text = computed.FinalTime.HasValue ? "τελικός χρόνος " + FormatRaceTime(computed.FinalTime.Value) : "–";
            SwimRow finalRow = computed.Rows.LastOrDefault();
            string finalDelta = finalRow != null && finalRow.Delta.HasValue && computed.Reference != null
                ? string.Format(" · Δ τελικού {0} από «{1}»", SwimReference.FormatDelta(finalRow.Delta.Value), computed.Reference.Name)
                : "";
            string clean = computed.AvgSr.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "\nΚαθαρή κολύμβηση: SR {0:0.0} κύκλοι/λεπτό · DPS {1:0.00} m/χεριά · SL {2:0.00} m/κύκλο",
                    computed.AvgSr.Value, computed.AvgDps ?? 0, computed.AvgSl ?? 0)
                : "";
            lblFinishSummary.Text = string.Format(CultureInfo.InvariantCulture,
                "Συνολικές χεριές (πατήματα): {0}{1}{2}",
                computed.TotalPresses,
                finalDelta,
                clean);

            // Data checks.
            string checks = computed.Warnings.Count == 0
                ? (computed.Rows.Any(r => r.T.HasValue) ? "✔ Δεν βρέθηκε κάποιο πρόβλημα στα σημάδια." : "")
                : string.Join(Environment.NewLine, computed.Warnings.Select(w => "⚠ " + w).ToArray());
            if (tbChecks.Text != checks)
                tbChecks.Text = checks;
            tbChecks.ForeColor = computed.Warnings.Count > 0 ? colorWarn : colorOk;

            lnkChecks.Visible = computed.Warnings.Count > 0;
            if (computed.Warnings.Count > 0)
            {
                string link = string.Format(CultureInfo.InvariantCulture, "⚠ {0}: {1}",
                    computed.Warnings.Count == 1 ? "1 έλεγχος" : computed.Warnings.Count + " έλεγχοι",
                    computed.Warnings[0]);
                if (lnkChecks.Text != link)
                {
                    lnkChecks.Text = link;
                    lnkChecks.LinkArea = new LinkArea(0, link.Length);
                }
            }

            // Turns.
            if (!gridTurns.IsCurrentCellInEditMode)
            {
                if (gridTurns.Rows.Count != computed.Turns.Count)
                {
                    gridTurns.Rows.Clear();
                    foreach (SwimTurnComputed t in computed.Turns)
                        gridTurns.Rows.Add();
                }

                for (int i = 0; i < computed.Turns.Count; i++)
                {
                    SwimTurnComputed t = computed.Turns[i];
                    SwimTurnManual manual = race.GetTurn(t.Wall, false);
                    DataGridViewRow gr = gridTurns.Rows[i];
                    gr.Tag = t.Wall;
                    gr.Cells["wall"].Value = t.Exchange ? t.Wall.ToString(CultureInfo.InvariantCulture) + " αλλαγή" : t.Wall.ToString(CultureInfo.InvariantCulture);
                    gr.Cells["uwDist"].Value = manual != null ? Format(manual.UnderwaterDist) : "";
                    gr.Cells["turnTime"].Value = manual != null && manual.TurnTime.HasValue ? Format(manual.TurnTime) : Format(t.TurnTime);
                    gr.Cells["uwTime"].Value = Format(t.UnderwaterTime);
                    gr.Cells["kicks"].Value = t.Kicks.HasValue ? t.Kicks.Value.ToString(CultureInfo.InvariantCulture) : "";
                    gr.Cells["gap"].Value = Format(t.LastStrokeGap);
                }
            }

            currentTurnWall = -1;
            RefreshTurnLabels();
        }

        /// <summary>
        /// Values of the turn the video is at (or the selected one).
        /// </summary>
        private void RefreshTurnLabels()
        {
            SwimRace race = manager.Race;
            SwimComputed computed = manager.Computed;
            if (race == null || computed == null)
                return;

            int wall = manager.CurrentTurnWall;
            if (wall <= 0)
                wall = SelectedTurnWall();

            if (wall == currentTurnWall)
                return;

            currentTurnWall = wall;
            SwimTurnComputed t = computed.Turns.FirstOrDefault(x => x.Wall == wall);
            if (computed.Turns.Count == 0)
            {
                lblTurnCurrent.Text = "Αυτός ο αγώνας δεν έχει στροφές.";
                wall = 0;
            }
            else if (t == null)
            {
                lblTurnCurrent.Text = "Στροφή: –";
            }
            else
            {
                lblTurnCurrent.Text = string.Format("{0} στα {1}{2}{3}", t.Exchange ? "Αλλαγή" : "Στροφή", wall, race.Unit, manager.CurrentTurnWall == wall ? " (η θέση του βίντεο)" : " (επιλεγμένη)");
            }

            SwimTurnManual manual = wall > 0 ? race.GetTurn(wall, false) : null;
            SwimRow wallRow = computed.Rows.FirstOrDefault(r => r.D == wall);
            lblTurnGap.Text = t != null && t.LastStrokeGap.HasValue ? "→ τοίχος " + Seconds(t.LastStrokeGap) : "–";
            lblTurnWall.Text = wallRow != null && wallRow.T.HasValue ? wall + race.Unit + " στα " + FormatRaceTime(wallRow.T.Value) : "–";
            double? turnTime = manual != null && manual.TurnTime.HasValue ? manual.TurnTime : (t != null ? t.TurnTime : null);
            if (turnTime.HasValue)
                lblTurnTime.Text = (t != null && t.Exchange ? "χρόνος αλλαγής " : (t != null && t.Flip ? "χρόνος στροφής (από την τελευταία χεριά) " : "χρόνος στροφής ")) + Seconds(turnTime);
            else if (t != null && t.Flip && t.Pushoff.HasValue)
                lblTurnTime.Text = "χρόνος στροφής: λείπει η τελευταία χεριά πριν τον τοίχο (C)";
            else
                lblTurnTime.Text = "–";
            lblTurnKicks.Text = t != null && t.Kicks.HasValue ? t.Kicks.Value.ToString(CultureInfo.InvariantCulture) : "–";
            string uwTime = t != null && t.UnderwaterTime.HasValue ? "υποβρύχια " + Seconds(t.UnderwaterTime) : "–";
            string uwDist = manual != null && manual.UnderwaterDist.HasValue ? " · " + Format(manual.UnderwaterDist) + " μ." : "";
            lblTurnUnderwater.Text = uwTime + uwDist;

            foreach (DataGridViewRow gr in gridTurns.Rows)
            {
                bool current = gr.Tag is int && (int)gr.Tag == wall;
                gr.DefaultCellStyle.Font = current ? (boldGridFont ?? (boldGridFont = new Font(gridTurns.Font, FontStyle.Bold))) : gridTurns.Font;
            }
        }

        private int SelectedTurnWall()
        {
            if (gridTurns != null && gridTurns.CurrentRow != null && gridTurns.CurrentRow.Tag is int)
                return (int)gridTurns.CurrentRow.Tag;

            List<int> walls = manager.TurnWalls;
            return walls.Count > 0 ? walls[0] : 0;
        }

        private void RefreshLive()
        {
            if (IsDisposed || manager.Race == null)
                return;

            SwimRace race = manager.Race;
            if (race.IsTraining || manager.TechnicalView)
            {
                RefreshTechLive();
            }
            else
            {
                double? now = manager.CurrentRaceTime;
                string time = manager.HasVideo && now.HasValue ? (now.Value >= 60 ? SwimReference.FormatTime(now.Value) : now.Value.ToString("0.00", CultureInfo.InvariantCulture)) : "–";
                int next = race.NextCheckpoint();
                List<SwimChip> chips = new List<SwimChip>();
                chips.Add(new SwimChip("ΕΠΟΜΕΝΟ", next > 0 ? next + race.Unit : "τέλος", SwimTheme.Amber));
                chips.Add(new SwimChip("ΧΕΡΙΕΣ", SwimCalculator.PressesSinceLastSplit(manager.Computed).ToString(CultureInfo.InvariantCulture), SwimTheme.Text));
                if (now.HasValue && manager.Computed.Reference != null)
                {
                    SwimRow last = manager.Computed.Rows.Where(r => r.T.HasValue && r.Delta.HasValue && r.T.Value <= now.Value + 1e-6).OrderBy(r => r.T.Value).LastOrDefault();
                    if (last != null)
                        chips.Add(new SwimChip("Δ " + last.D + race.Unit, SwimReference.FormatDelta(last.Delta.Value), last.Delta.Value > 0.0049 ? SwimTheme.Red : SwimTheme.Green));
                }

                if (now.HasValue)
                {
                    SwimBlock block = manager.Computed.Blocks.Where(b => b.Dak.HasValue && manager.Computed.Rows.Any(r => r.D == b.To && r.T.HasValue && r.T.Value <= now.Value + 1e-6))
                        .OrderBy(b => b.To).LastOrDefault();
                    if (block != null)
                        chips.Add(new SwimChip("ΔΑΚ " + block.From + "-" + block.To, block.Dak.Value.ToString("0.00", CultureInfo.InvariantCulture), SwimTheme.Amber));
                }

                chips.Add(new SwimChip("ΦΑΣΗ", PhaseName(manager.CurrentPhase), SwimTheme.Accent));
                if (!race.StartTimestamp.HasValue)
                    chips.Add(new SwimChip("ΣΗΜΑ", "δεν έχει οριστεί", SwimTheme.Warn));
                lblLive.SetValues("ΧΡΟΝΟΣ ΑΓΩΝΑ", time, chips);
            }

            if (tabs.SelectedTab == tabTurns)
                RefreshTurnLabels();

            if (grpCalibration.Visible)
                RefreshCalibrationLabel();
        }

        private string PhaseName(SwimPhase phase)
        {
            switch (phase)
            {
                case SwimPhase.Start: return "Εκκίνηση";
                case SwimPhase.Swim: return "Κολύμβηση";
                case SwimPhase.Turn: return "Στροφή " + manager.CurrentTurnWall + (manager.Race != null ? manager.Race.Unit : "");
                case SwimPhase.Finish: return "Τερματισμός";
                default: return "";
            }
        }

        /// <summary>
        /// Show the tab of the phase of the event just marked (only when a phase tab is shown).
        /// </summary>
        public void FollowPhase(SwimPhase phase)
        {
            if (IsDisposed)
                return;

            SwimPage current = tabs.SelectedTab;
            if (current != tabStart && current != tabSwim && current != tabTurns && current != tabFinish)
                return;

            SwimPage target;
            switch (phase)
            {
                case SwimPhase.Start: target = tabStart; break;
                case SwimPhase.Swim: target = tabSwim; break;
                case SwimPhase.Turn: target = tabTurns; break;
                default: target = tabFinish; break;
            }

            if (target != current)
                tabs.SelectedTab = target;
        }

        /// <summary>
        /// Calibration lines and options.
        /// </summary>
        public void RefreshCalibration()
        {
            if (IsDisposed)
                return;

            updating = true;
            try
            {
                int pool = manager.PoolLength;
                if (pool != calibrationPool)
                {
                    calibrationPool = pool;
                    foreach (ComboBox combo in new ComboBox[] { cmbLine1, cmbLine2 })
                    {
                        combo.Items.Clear();
                        for (int d = 0; d <= pool; d += 5)
                            combo.Items.Add(d.ToString(CultureInfo.InvariantCulture));
                    }
                }

                cmbLine1.Text = manager.CalibrationLine1.ToString("0.##", CultureInfo.InvariantCulture);
                cmbLine2.Text = manager.CalibrationLine2.ToString("0.##", CultureInfo.InvariantCulture);
                if (!tbLaneWidth.Focused)
                    tbLaneWidth.Text = manager.Settings.LaneWidth.ToString("0.##", CultureInfo.InvariantCulture);
                chkFixedCamera.Checked = manager.Settings.FixedCamera;
            }
            finally
            {
                updating = false;
            }

            RefreshCalibrationLabel();
        }

        private void RefreshCalibrationLabel()
        {
            string text = manager.CalibrationSummary();
            if (lblCalibration.Text != text)
            {
                lblCalibration.Text = text;
                toolTip.SetToolTip(lblCalibration, text);
            }

            bool hasCalibration = manager.VideoFile != null && manager.VideoFile.Calibrations.Count > 0;
            btnDeleteCalibration.Enabled = hasCalibration;
        }

        /// <summary>
        /// Highlight the button of the pending click on the video.
        /// </summary>
        public void RefreshPick()
        {
            if (IsDisposed)
                return;

            SwimPickTarget active = manager.PickTarget == SwimPickTarget.Calibration ? manager.PendingMeasure : manager.PickTarget;
            foreach (KeyValuePair<SwimPickTarget, Button> pair in pickButtons)
                Highlight(pair.Value, pair.Key == active && manager.PickTarget != SwimPickTarget.None);

            bool calibrating = manager.PickTarget == SwimPickTarget.Calibration;
            btnCalibrate.Text = calibrating ? "Ακύρωση βαθμονόμησης" : "Βαθμονόμηση (4 κλικ)";
            Highlight(btnCalibrate, calibrating);
            RefreshTechPick(active);
            RefreshCalibrationLabel();
        }

        private static void Highlight(Button b, bool on)
        {
            SwimButton themed = b as SwimButton;
            if (themed != null)
            {
                themed.Highlighted = on;
                return;
            }

            if (on)
            {
                b.BackColor = colorPick;
            }
            else
            {
                b.BackColor = SystemColors.Control;
                b.UseVisualStyleBackColor = true;
            }
        }

        private void UpdateCalibrationVisibility()
        {
            SwimPage page = tabs != null ? tabs.SelectedTab : null;
            bool visible = page == tabStart || page == tabTurns || page == tabFinish || page == tabTechStart || page == tabTechTurns;
            grpCalibration.Visible = visible;

            if (visible)
                RefreshCalibrationLabel();
            if (page == tabTurns)
            {
                currentTurnWall = -1;
                RefreshTurnLabels();
            }
        }

        /// <summary>
        /// References of the event of the race and the one selected.
        /// </summary>
        public void RefreshReferences()
        {
            if (IsDisposed)
                return;

            SwimRace race = manager.Race;
            int count = 0;
            updating = true;
            try
            {
                cmbReference.BeginUpdate();
                cmbReference.Items.Clear();
                cmbReference.Items.Add(new ReferenceItem(null));
                int selected = 0;
                if (race != null)
                {
                    List<SwimReference> list = manager.ReferencesForRace();
                    count = list.Count;
                    if (race.Reference != null && !list.Any(r => r.Id == race.Reference.Id))
                        list.Insert(0, race.Reference);

                    foreach (SwimReference reference in list)
                    {
                        cmbReference.Items.Add(new ReferenceItem(reference));
                        if (race.Reference != null && reference.Id == race.Reference.Id)
                            selected = cmbReference.Items.Count - 1;
                    }
                }

                cmbReference.SelectedIndex = selected;
                cmbReference.EndUpdate();
            }
            finally
            {
                updating = false;
            }

            string info = manager.ReferencesMessage ?? "";
            if (race != null)
            {
                string ev = string.Format("{0} {1} ({2})", race.DistanceLabel, SwimPresets.StyleShort(race.Style), race.PoolLabel);
                info = (count > 0 ? string.Format("{0} για {1}. ", count, ev) : string.Format("Καμία αναφορά για {0} στο φύλλο «{1}». ", ev, SwimAnalysisManager.ReferenceSheetName)) + info;
            }

            lblReferences.Text = info;
            toolTip.SetToolTip(lblReferences, info);
            btnSaveReference.Enabled = race != null;
        }

        private void CmbReference_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (updating)
                return;

            ReferenceItem item = cmbReference.SelectedItem as ReferenceItem;
            manager.SetReference(item != null ? item.Reference : null);
        }

        private void SaveAsReference()
        {
            SwimRace race = manager.Race;
            if (race == null)
                return;

            string name = AskText("Νέα elite αναφορά",
                "Όνομα της αναφοράς, όπως θα φαίνεται στη λίστα σύγκρισης (π.χ. «Muratory – EJC 2025 Final»):",
                SwimReference.DefaultName(race));
            if (name != null)
                manager.SaveAsReference(name);
        }

        private string AskText(string title, string prompt, string value)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = title;
                dialog.Font = Font;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(440, 124);
                dialog.BackColor = SwimTheme.Back;
                dialog.ForeColor = SwimTheme.Text;
                dialog.HandleCreated += (s, e) => SwimTheme.DarkTitleBar(dialog.Handle);

                Label label = new Label { Text = prompt, Left = 12, Top = 10, Width = 416, Height = 36 };
                TextBox box = new TextBox { Text = value ?? "", Left = 12, Top = 50, Width = 416 };
                Button ok = new SwimButton { Text = "OK", DialogResult = DialogResult.OK, Left = 272, Top = 86, Width = 75, Height = 28, Primary = true };
                Button cancel = new SwimButton { Text = "Άκυρο", DialogResult = DialogResult.Cancel, Left = 353, Top = 86, Width = 75, Height = 28 };
                dialog.Controls.Add(label);
                dialog.Controls.Add(box);
                dialog.Controls.Add(ok);
                dialog.Controls.Add(cancel);
                SwimTheme.Apply(dialog);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return null;

                string text = box.Text.Trim();
                return text.Length > 0 ? text : null;
            }
        }

        private void CommitOverlayOptions()
        {
            if (updating)
                return;

            SwimSettings s = manager.Settings;
            s.ShowSpeedChart = chkSpeedChart.Checked;
            s.ShowSplitBoard = chkSplitBoard.Checked;
            if (cmbCorner.SelectedIndex >= 0)
                s.OverlayCorner = corners[cmbCorner.SelectedIndex];
            s.Save();
            manager.RefreshOverlay();
        }

        /// <summary>
        /// Update the selected athlete and the state of the athlete list.
        /// </summary>
        public void RefreshAthletes()
        {
            if (IsDisposed)
                return;

            SwimRace r = manager.Race;
            if (r != null && !string.IsNullOrEmpty(r.AthleteId))
            {
                List<string> parts = new List<string> { "✔ " + r.Athlete, "μητρώο " + r.AthleteId };
                if (!string.IsNullOrEmpty(r.AthleteClub)) parts.Add(r.AthleteClub);
                if (!string.IsNullOrEmpty(r.AthleteCategory)) parts.Add(r.AthleteCategory);
                if (!string.IsNullOrEmpty(r.AthleteBirthYear)) parts.Add(r.AthleteBirthYear);
                lblAthlete.ForeColor = SwimTheme.Accent;
                lblAthlete.Text = string.Join(" · ", parts.ToArray());
            }
            else
            {
                lblAthlete.ForeColor = colorWarn;
                lblAthlete.Text = "Δεν έχει επιλεγεί αθλητής από τη λίστα: η ανάλυση δεν θα συνδεθεί με μητρώο.";
            }

            lblAthleteList.Text = manager.AthletesMessage ?? "";
            FilterAthletes();
        }

        private void FilterAthletes()
        {
            if (lbAthletes == null)
                return;

            string query = tbAthleteSearch.Text.Trim();
            lbAthletes.BeginUpdate();
            lbAthletes.Items.Clear();
            if (query.Length >= 2)
            {
                foreach (SwimAthlete a in manager.Athletes.Search(query, 60))
                    lbAthletes.Items.Add(a);
            }

            lbAthletes.EndUpdate();
        }

        private void ChooseAthlete(SwimAthlete athlete)
        {
            if (athlete == null)
                return;

            manager.SetAthlete(athlete);
            tbAthleteSearch.Text = "";
        }

        private void TbAthleteSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (lbAthletes.Items.Count > 0)
                    ChooseAthlete(lbAthletes.Items[0] as SwimAthlete);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Down && lbAthletes.Items.Count > 0)
            {
                lbAthletes.Focus();
                lbAthletes.SelectedIndex = 0;
                e.Handled = true;
            }
        }

        public void RefreshSync()
        {
            if (IsDisposed)
                return;

            Color color;
            switch (manager.SyncState)
            {
                case SwimSyncState.Synced: color = colorOk; break;
                case SwimSyncState.Sending: color = colorBusy; break;
                case SwimSyncState.Offline: color = colorWarn; break;
                case SwimSyncState.Error: color = colorError; break;
                case SwimSyncState.NotConfigured: color = colorWarn; break;
                default: color = SwimTheme.Dim; break;
            }

            string sheet = manager.Race != null && !string.IsNullOrEmpty(manager.Race.SheetName) ? "  ·  " + manager.Race.SheetName : "";
            lblSync.ForeColor = color;
            lblSync.Text = "● " + (manager.SyncMessage ?? "") + sheet;
            toolTip.SetToolTip(lblSync, lblSync.Text);
            btnOpenSheet.Enabled = manager.Race != null && !string.IsNullOrEmpty(manager.Race.SheetUrl);
        }

        public void ShowStatus(string message, bool warning)
        {
            if (IsDisposed)
                return;

            lblStatus.ForeColor = warning ? colorWarn : SwimTheme.Dim;
            lblStatus.Text = message;
            toolTip.SetToolTip(lblStatus, message);
        }
        #endregion

        #region Edits
        private void CmbRaces_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (updating || cmbRaces.SelectedItem == null)
                return;

            manager.SelectRace(((RaceItem)cmbRaces.SelectedItem).Race.Id);
        }

        private void BtnDeleteRace_Click(object sender, EventArgs e)
        {
            if (manager.Race == null)
                return;

            DialogResult answer = MessageBox.Show(this,
                "Διαγραφή της ανάλυσης \"" + manager.Race.DisplayName + "\" από αυτό το βίντεο;\nΤο φύλλο στο Google Sheet δεν διαγράφεται.",
                "Διαγραφή ανάλυσης", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes)
                manager.DeleteRace();
        }

        private void CommitRaceInfo(bool structureChanged)
        {
            if (updating || manager.Race == null)
                return;

            SwimRace r = manager.Race;
            r.Athlete = tbAthlete.Text.Trim();
            r.Lane = (int)nudLane.Value;
            if (cmbStyle.SelectedIndex >= 0)
                r.Style = SwimPresets.Styles[cmbStyle.SelectedIndex];
            r.Competition = tbCompetition.Text.Trim();
            r.Location = tbLocation.Text.Trim();
            r.Round = cmbRound.Text.Trim();
            r.Date = tbDate.Text.Trim();
            manager.RaceInfoChanged(structureChanged);
        }

        private void CommitDistance()
        {
            if (updating || manager.Race == null || cmbDistance.SelectedItem == null)
                return;

            int distance;
            bool relay;
            if (!SwimPresets.TryParseDistance(cmbDistance.SelectedItem.ToString(), out distance, out relay))
                return;

            SwimRace r = manager.Race;
            int oldPool = r.Pool;
            string oldUnit = r.PoolUnit;
            SwimPresets.ApplyPool(r, cmbPool.SelectedIndex);
            bool poolChanged = r.Pool != oldPool || r.PoolUnit != oldUnit;
            if (r.PoolUnit != oldUnit)
            {
                // The events of a 25 yard pool are not those of a meter pool (500, 1000, 1650).
                updating = true;
                FillDistances(r, cmbDistance.SelectedItem.ToString());
                updating = false;
            }

            if (r.Distance == distance && r.Relay == relay && !poolChanged)
                return;

            r.Distance = distance;
            r.Relay = relay;
            r.Checkpoints = SwimPresets.DefaultCheckpoints(distance, r.Pool, relay);
            updating = true;
            tbCheckpoints.Text = SwimPresets.FormatCheckpoints(r.Checkpoints);
            updating = false;
            CommitRaceInfo(true);
            RefreshCalibration();
        }

        private void CommitKind()
        {
            if (updating || manager.Race == null || cmbKind.SelectedIndex < 0)
                return;

            SwimRace r = manager.Race;
            bool training = cmbKind.SelectedIndex == 1;
            if (r.IsTraining == training)
                return;

            if (training && r.Marks.Any(m => m.Type == SwimMarkType.Split))
            {
                DialogResult answer = MessageBox.Show(this,
                    "Η ανάλυση έχει περάσματα αγώνα. Σε βίντεο προπόνησης δεν χρησιμοποιούνται (μένουν αποθηκευμένα αν το γυρίσεις πίσω σε αγώνα). Συνέχεια;",
                    "Είδος βίντεο", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    updating = true;
                    cmbKind.SelectedIndex = 0;
                    updating = false;
                    return;
                }
            }

            r.Kind = training ? SwimRace.KindTraining : SwimRace.KindRace;
            manager.RaceInfoChanged(false);
            SetTechnicalView(training, true);
            ShowStatus(training
                ? "Βίντεο προπόνησης: G σήμα (νέα εκκίνηση), W επαφή στον τοίχο (νέα στροφή), T για 5/10/15, H/J χέρια/πίσω πόδι, O αρχή περιστροφής, P στιγμιότυπο, A γωνία."
                : "Βίντεο αγώνα: race analysis και τεχνική ανάλυση της εκκίνησης και των στροφών.", false);
        }

        private void ApplyDefaultCheckpoints()
        {
            if (manager.Race == null)
                return;

            SwimRace r = manager.Race;
            r.Checkpoints = SwimPresets.DefaultCheckpoints(r.Distance, r.Pool, r.Relay);
            updating = true;
            tbCheckpoints.Text = SwimPresets.FormatCheckpoints(r.Checkpoints);
            updating = false;
            manager.RaceInfoChanged(true);
        }

        private void CommitCheckpoints()
        {
            if (updating || manager.Race == null)
                return;

            List<int> parsed = SwimPresets.ParseCheckpoints(tbCheckpoints.Text);
            SwimRace r = manager.Race;
            if (parsed.Count == 0 || parsed.SequenceEqual(r.Checkpoints))
            {
                tbCheckpoints.Text = SwimPresets.FormatCheckpoints(r.Checkpoints);
                return;
            }

            if (parsed[parsed.Count - 1] != r.Distance)
                ShowStatus("Προσοχή: το τελευταίο σημείο ελέγχου δεν είναι ίσο με την απόσταση του αγώνα (" + r.Distance + r.Unit + ").", true);

            r.Checkpoints = parsed;
            tbCheckpoints.Text = SwimPresets.FormatCheckpoints(parsed);
            manager.RaceInfoChanged(true);
        }

        private void CommitManualValues()
        {
            if (updating || manager.Race == null)
                return;

            SwimRace r = manager.Race;
            r.Reaction = Parse(tbReaction);
            r.EntryDistHands = Parse(tbEntryDistHands);
            r.EntryDistHips = Parse(tbEntryDistHips);
            r.EntryVelHands = Parse(tbEntryVelHands);
            r.EntryVelHips = Parse(tbEntryVelHips);
            r.EntryVelFeet = Parse(tbEntryVelFeet);
            r.UnderwaterDist = Parse(tbUnderwaterDist);
            r.BreakoutDist = Parse(tbBreakoutDist);
            r.FinishLastStrokeDist = Parse(tbFinishDist);
            manager.ManualValuesChanged();
        }

        private void CommitLaneWidth()
        {
            if (updating)
                return;

            double? width = ParseText(tbLaneWidth.Text);
            if (width.HasValue && width.Value >= 0.5 && width.Value <= 5)
                manager.Settings.LaneWidth = width.Value;
        }

        private static double ParseLine(ComboBox combo, double fallback)
        {
            double? value = ParseText(combo.Text);
            return value.HasValue ? value.Value : fallback;
        }

        private void GridTurns_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (manager.Race == null || e.RowIndex < 0)
                return;

            DataGridViewRow row = gridTurns.Rows[e.RowIndex];
            if (!(row.Tag is int))
                return;

            int wall = (int)row.Tag;
            string column = gridTurns.Columns[e.ColumnIndex].Name;
            double? value = ParseText(Convert.ToString(row.Cells[e.ColumnIndex].Value, CultureInfo.InvariantCulture));
            SwimTurnManual manual = manager.Race.GetTurn(wall, true);
            if (column == "uwDist")
                manual.UnderwaterDist = value;
            else if (column == "turnTime")
                manual.TurnTime = value;

            BeginInvoke((Action)(() => manager.ManualValuesChanged()));
        }

        private void GridTurns_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || !(gridTurns.Rows[e.RowIndex].Tag is int))
                return;

            string column = gridTurns.Columns[e.ColumnIndex].Name;
            if (column == "uwDist" || column == "turnTime")
                return;

            manager.Seek((int)gridTurns.Rows[e.RowIndex].Tag);
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || !(grid.Rows[e.RowIndex].Tag is int))
                return;

            manager.Seek((int)grid.Rows[e.RowIndex].Tag);
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete || grid.SelectedRows.Count == 0)
                return;

            object tag = grid.SelectedRows[0].Tag;
            if (tag is int)
            {
                manager.ClearSplit((int)tag);
                e.Handled = true;
            }
        }

        private void SaveConnection()
        {
            SwimSettings s = manager.Settings;
            s.WebAppUrl = tbUrl.Text.Trim();
            s.Token = tbToken.Text.Trim();
            s.StrokeRounding = cmbRounding.SelectedIndex == 1 ? 1.0 : (cmbRounding.SelectedIndex == 2 ? 0.0 : 0.5);
            s.AutoSend = chkAutoSend.Checked;
            manager.SaveSettings();
            lblConnection.ForeColor = SwimTheme.Dim;
            lblConnection.Text = "Οι ρυθμίσεις αποθηκεύτηκαν.";
        }

        private void TestConnection()
        {
            SaveConnection();
            lblConnection.ForeColor = colorBusy;
            lblConnection.Text = "Δοκιμή…";
            manager.TestConnection(result =>
            {
                if (IsDisposed)
                    return;

                if (result.Ok)
                {
                    lblConnection.ForeColor = colorOk;
                    lblConnection.Text = "✔ Συνδέθηκε με το Google Sheet \"" + result.SpreadsheetName + "\".";
                }
                else
                {
                    lblConnection.ForeColor = colorError;
                    lblConnection.Text = "✖ " + result.Error;
                }
            });
        }
        #endregion

        #region Keyboard
        /// <summary>
        /// Forward the player hotkeys to the player screen unless the user is typing in a field.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Control focused = FocusedControl();
            bool typing = IsTextInput(focused);

            if (keyData == Keys.Escape && manager.CancelPick())
                return true;

            if (!typing && (focused == grid || focused == gridAngles) && keyData == Keys.Delete)
                return base.ProcessCmdKey(ref msg, keyData);

            if (!typing || (keyData & (Keys.Control | Keys.Alt)) != 0 || (keyData >= Keys.F1 && keyData <= Keys.F24))
            {
                HotkeyCommand command = HotkeySettingsManager.FindCommand("PlayerScreen", keyData);
                if (command != null && (!typing || command.Name.StartsWith("Swim")))
                {
                    if (manager.ExecutePlayerCommand(command.CommandCode))
                        return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private Control FocusedControl()
        {
            Control c = ActiveControl;
            while (c is ContainerControl && ((ContainerControl)c).ActiveControl != null)
                c = ((ContainerControl)c).ActiveControl;
            return c;
        }

        private static bool IsTextInput(Control c)
        {
            if (c == null)
                return false;
            if (c is TextBoxBase)
                return true;
            if (c is UpDownBase || (c.Parent != null && c.Parent is UpDownBase))
                return true;
            ComboBox combo = c as ComboBox;
            if (combo != null && combo.DropDownStyle != ComboBoxStyle.DropDownList)
                return true;
            DataGridView dgv = c as DataGridView;
            if (dgv != null && dgv.IsCurrentCellInEditMode)
                return true;
            if (c is DataGridViewTextBoxEditingControl)
                return true;
            if (c is ListBox)
                return true;
            return false;
        }
        #endregion

        #region Helpers
        private class ReferenceItem
        {
            public readonly SwimReference Reference;

            public ReferenceItem(SwimReference reference)
            {
                Reference = reference;
            }

            public override string ToString()
            {
                return Reference == null ? "(χωρίς σύγκριση)" : Reference.Label;
            }
        }

        private class RaceItem
        {
            public readonly SwimRace Race;
            public readonly string Label;

            public RaceItem(SwimRace race)
            {
                Race = race;
                Label = race.DisplayName;
            }

            public override string ToString()
            {
                return Label;
            }
        }

        private static string Format(double? value)
        {
            return Format(value, "0.00");
        }

        private static string Format(double? value, string format)
        {
            return value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "";
        }

        private static string Seconds(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) + " s" : "–";
        }

        private static string FormatRaceTime(double seconds)
        {
            if (seconds < 60)
                return seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";

            int minutes = (int)(seconds / 60);
            double rest = seconds - minutes * 60;
            return minutes.ToString(CultureInfo.InvariantCulture) + ":" + rest.ToString("00.00", CultureInfo.InvariantCulture);
        }

        private static string FormatStrokes(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "";
        }

        private static double? Parse(TextBox tb)
        {
            return ParseText(tb.Text);
        }

        private static double? ParseText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            double value;
            string normalized = text.Trim().Replace(',', '.');
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return value;

            return null;
        }
        #endregion
    }
}
