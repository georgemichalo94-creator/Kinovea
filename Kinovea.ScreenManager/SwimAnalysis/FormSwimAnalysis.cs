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

        /// <summary>Unit labels of the distances of the race tabs ("m" or "yd").</summary>
        private readonly List<Label> distanceUnits = new List<Label>();

        /// <summary>A cell of the turns grid being edited: its race and its text before the edit.</summary>
        private SwimRace turnEditRace;
        private string turnEditText;
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
        private SwimSegmented langSwitch;
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
        private TextBox tbResultsLink;
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
        private CheckBox chkAutoStrokes;
        private CheckBox chkLearnMarks;
        private Label lblLearn;
        private Button btnAutoStrokes;
        private Label lblAutoStrokes;
        private CheckBox chkReel;
        private CheckBox chkReelSummary;
        private Button btnTrack;
        private Label lblTrack;
        private Button btnPilot;
        private Button btnRaceReview;
        private ComboBox cmbReference;
        private Label lblReferences;
        private ComboBox cmbPb;
        private Label lblPb;
        private Button btnSaveReference;
        private CheckBox chkSpeedChart;
        private CheckBox chkGraphics;
        private CheckBox chkHud;
        private CheckBox chkSplitBoard;
        private CheckBox chkOverlayExports;
        private CheckBox chkVideoBars;
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
        private TextBox tbOfficial;
        private Label lblOfficial;

        /// <summary>
        /// The race whose official times are being typed and not put in yet (null: the box shows the race's own). Another race
        /// selected meanwhile drops what was typed: it was not for it.
        /// </summary>
        private SwimRace officialEditRace;

        /// <summary>Why the official times typed could not be put in (shown until they are corrected), null: none.</summary>
        private string officialError;
        private Button btnFinalFrame;
        private Label lblFinalFrame;
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

            // The first control that takes the focus does not take the letters of the hotkeys (not the reaction time box).
            ActiveControl = cmbRaces;

            liveTimer = new Timer();
            liveTimer.Interval = 200;
            liveTimer.Tick += (s, e) => RefreshLive();
            liveTimer.Start();
        }

        /// <summary>
        /// Show the window where it was last closed (e.g. on a second monitor), or else next to the right edge of the
        /// Kinovea main window.
        /// </summary>
        public void ShowNear(Form owner)
        {
            StartPosition = FormStartPosition.Manual;
            Rectangle area = owner != null ? owner.Bounds : Screen.PrimaryScreen.WorkingArea;
            Rectangle screen = Screen.FromRectangle(area).WorkingArea;

            // Small or scaled screens (1366 x 768, 125 %): the whole window, with its footer, stays visible.
            MinimumSize = new Size(Math.Min(MinimumSize.Width, screen.Width), Math.Min(MinimumSize.Height, screen.Height));
            Size = new Size(Math.Min(Width, screen.Width), Math.Min(Height, screen.Height));
            int x = Math.Max(screen.Left, Math.Min(area.Right - Width - 16, screen.Right - Width));
            int y = Math.Max(screen.Top, Math.Min(area.Top + 90, screen.Bottom - Height));
            Location = new Point(x, y);

            Rectangle saved;
            if (TryParseBounds(manager.Settings.PanelBounds, out saved) && TitleOnScreen(saved))
                Bounds = saved;

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

        /// <summary>The page shown (its place among all the pages): the panel opens again on it in the other language.</summary>
        public int PageIndex
        {
            get { return Array.IndexOf(AllPages, tabs.SelectedTab); }
            set
            {
                SwimPage[] all = AllPages;
                if (value >= 0 && value < all.Length && tabs.VisiblePages.Contains(all[value]))
                    tabs.SelectedTab = all[value];
            }
        }

        private SwimPage[] AllPages
        {
            get { return new SwimPage[] { tabRace, tabStart, tabSwim, tabTurns, tabFinish, tabFinals, tabTechStart, tabTechTurns, tabTechMedia, tabConnection }; }
        }

        /// <summary>The switch of the language as the analysis is (a change refused during a reading).</summary>
        public void RefreshLanguageSwitch()
        {
            langSwitch.SetSelected(SwimLang.English ? 1 : 0);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            // Checkpoints typed and not committed yet (the user goes back to the video to mark them),
            // unless the list would drop a checkpoint that has a time (it may be half typed). The official times too.
            CommitCheckpoints(true);
            CommitOfficial();
            base.OnDeactivate(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            CommitCheckpoints(true);
            CommitOfficial();

            // The window opens again where the analyst left it.
            if (WindowState == FormWindowState.Normal)
                manager.Settings.PanelBounds = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", Left, Top, Width, Height);

            base.OnFormClosing(e);
        }

        private static bool TryParseBounds(string text, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (string.IsNullOrEmpty(text))
                return false;

            string[] parts = text.Split(',');
            int[] v = new int[4];
            if (parts.Length != 4)
                return false;

            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v[i]))
                    return false;
            }

            if (v[2] < 200 || v[3] < 200)
                return false;

            bounds = new Rectangle(v[0], v[1], v[2], v[3]);
            return true;
        }

        /// <summary>The title bar of the window is on a screen (it can be dragged): a monitor may have been removed since.</summary>
        private static bool TitleOnScreen(Rectangle bounds)
        {
            Rectangle title = new Rectangle(bounds.Left, bounds.Top, bounds.Width, 30);
            foreach (Screen s in Screen.AllScreens)
            {
                Rectangle common = Rectangle.Intersect(s.WorkingArea, title);
                if (common.Width >= 120 && common.Height >= 20)
                    return true;
            }

            return false;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            liveTimer.Stop();
            liveTimer.Dispose();
            toolTip.Dispose();
            if (boldGridFont != null)
                boldGridFont.Dispose();
            foreach (PictureBox box in new PictureBox[] { picSnapshot, picElite })
            {
                if (box != null && box.Image != null)
                {
                    Image image = box.Image;
                    box.Image = null;
                    image.Dispose();
                }
            }

            base.OnFormClosed(e);
            if (valueFont != null)
                valueFont.Dispose();
        }

        #region UI construction
        private void BuildUI()
        {
            Text = SwimLang.T("Swim Analytics · Ανάλυση αγώνα κολύμβησης");
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
            lblWordmark = new Label { Text = "SWIM ANALYTICS", AutoSize = true, Font = SwimTheme.DisplayLight(14f), ForeColor = SwimTheme.AccentPale, Location = new Point(8, 10) };
            lblBrandSub = new Label { Text = SwimLang.T("Κ.Ο.Ε. · KINOVEA"), AutoSize = true, Font = SwimTheme.Ui(7f, FontStyle.Bold), ForeColor = SwimTheme.Accent, Location = new Point(170, 16) };
            modeSwitch = new SwimSegmented("RACE ANALYSIS", SwimLang.T("ΤΕΧΝΙΚΗ ΑΝΑΛΥΣΗ")) { Size = new Size(300, 28), Location = new Point(360, 7), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            modeSwitch.SelectedIndexChanged += (s, e) => SetTechnicalView(modeSwitch.SelectedIndex == 1, true);

            // The language of the whole analysis: the panel, the graphics of the video, the PDF and the menus of Kinovea.
            langSwitch = new SwimSegmented("ΕΛ", "EN") { Size = new Size(76, 28), Location = new Point(276, 7), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            langSwitch.SetSelected(SwimLang.English ? 1 : 0);
            langSwitch.SelectedIndexChanged += (s, e) => BeginInvoke((Action)(() => manager.SetLanguage(langSwitch.SelectedIndex == 1)));
            toolTip.SetToolTip(langSwitch, "Ελληνικά / English: το πάνελ, τα γραφικά του βίντεο, το PDF και τα μενού του Kinovea.\n" +
                "Greek / English: the panel, the graphics of the video, the PDF and the menus of Kinovea.");
            toolTip.SetToolTip(modeSwitch, SwimLang.T("Race analysis: περάσματα, χεριές, στροφές και τερματισμός του αγώνα.\n") +
                SwimLang.T("Τεχνική ανάλυση: φάσεις της εκκίνησης και των στροφών, χρόνοι 5/10/15, γωνίες, στιγμιότυπα με σχόλια και elite καρέ."));
            brand.Controls.Add(lblWordmark);
            brand.Controls.Add(lblBrandSub);
            brand.Controls.Add(modeSwitch);
            brand.Controls.Add(langSwitch);
            brand.Resize += (s, e) =>
            {
                modeSwitch.Left = brand.ClientSize.Width - modeSwitch.Width - 8;
                langSwitch.Left = modeSwitch.Left - langSwitch.Width - 8;
                lblBrandSub.Left = lblWordmark.Right + 6;
                lblBrandSub.Visible = lblBrandSub.Right <= langSwitch.Left - 6;
            };
            brand.Paint += (s, e) =>
            {
                // HUD (2.9.0): a glowing line under the wordmark.
                using (System.Drawing.Drawing2D.LinearGradientBrush glow = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(1, brand.Width), 6), Color.FromArgb(70, SwimTheme.Accent), Color.FromArgb(0, SwimTheme.Accent), System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(glow, 0, brand.Height - 5, brand.Width, 5);
                using (System.Drawing.Drawing2D.LinearGradientBrush line = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(1, brand.Width), 2), SwimTheme.Accent, SwimTheme.Back, System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(line, 0, brand.Height - 2, brand.Width, 2);
            };

            // Header: race selector.
            Panel header = new Panel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 6, 4) };
            Label lblRace = new Label { Text = SwimLang.T("Ανάλυση:"), AutoSize = true, Location = new Point(6, 11), ForeColor = SwimTheme.Dim };
            cmbRaces = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(72, 7), Width = 330, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            cmbRaces.SelectedIndexChanged += CmbRaces_SelectedIndexChanged;
            btnNewRace = new SwimNoFocusButton { Text = SwimLang.T("+ Νέος αθλητής"), Width = 120, Height = 26, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnNewRace.Location = new Point(header.Width - 160, 6);
            btnNewRace.Click += (s, e) => manager.NewRace();
            btnDeleteRace = new SwimNoFocusButton { Text = "✕", Width = 30, Height = 26, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnDeleteRace.Location = new Point(header.Width - 46, 6);
            btnDeleteRace.Click += BtnDeleteRace_Click;
            toolTip.SetToolTip(btnDeleteRace, SwimLang.T("Διαγραφή αυτής της ανάλυσης"));
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
            // («Αναίρεση» with official times being typed drops them, and only them, as from the bar of the video: CommitsTyping.)
            btnUndo = new SwimNoFocusButton { Dock = DockStyle.Right, Width = 150, Font = buttonFont, Accent = SwimTheme.Red, CommitsTyping = false };
            btnUndo.Click += (s, e) =>
            {
                if (!DropTypedOfficial())
                    RunCommand(PlayerScreenCommands.SwimUndo);
            };
            toolTip.SetToolTip(btnUndo, SwimLang.T("Αναίρεση του τελευταίου σημαδιού"));
            RegisterCommandButton(PlayerScreenCommands.SwimUndo, btnUndo, SwimLang.T("Αναίρεση"));
            live.Controls.Add(lblLive);
            live.Controls.Add(btnUndo);

            lblStatus = new Label { Dock = DockStyle.Top, Height = 34, AutoEllipsis = true, ForeColor = SwimTheme.Dim, Padding = new Padding(8, 2, 8, 0) };

            // Footer: sync status.
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(6), BackColor = SwimTheme.Surface };
            lblSync = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Font = SwimTheme.Ui(8.5f, FontStyle.Bold) };
            FlowLayoutPanel footerButtons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            btnSend = new SwimNoFocusButton { Text = SwimLang.T("Αποστολή τώρα"), AutoSize = true, Height = 28, Primary = true };
            btnSend.Click += (s, e) => manager.SendByUser(false);
            btnOpenSheet = new SwimNoFocusButton { Text = SwimLang.T("Άνοιγμα φύλλου"), AutoSize = true, Height = 28 };
            btnOpenSheet.Click += (s, e) => manager.OpenSheet();
            btnPdf = new SwimNoFocusButton { Text = "PDF report", AutoSize = true, Height = 28 };
            btnPdf.Click += (s, e) => manager.SendByUser(true);
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
            tabFinals = BuildFinalsTab();
            tabConnection = BuildConnectionTab();
            BuildTechPages();

            tabs = new SwimTabs { Dock = DockStyle.Fill };
            tabs.AddPage(tabRace);
            tabs.AddPage(tabStart);
            tabs.AddPage(tabSwim);
            tabs.AddPage(tabTurns);
            tabs.AddPage(tabFinish);
            tabs.AddPage(tabFinals);
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
            SetTechnicalView(manager.TechnicalView, false);
            UpdateCalibrationVisibility();
        }

        /// <summary>
        /// Race analysis or technical analysis: the pages of the mode, the overlay of the video and the hotkeys stay the same.
        /// A training video has only the technical analysis.
        /// </summary>
        private void SetTechnicalView(bool technical, bool fromUser)
        {
            SwimRace race = manager.Race;

            // The choice of the user stays for the next videos; a training video shows the technical analysis anyway.
            if (fromUser)
                manager.TechnicalView = technical;
            if (!technical && race != null && race.IsTraining)
            {
                technical = true;
                if (fromUser)
                    ShowStatus(SwimLang.T("Βίντεο προπόνησης: μόνο τεχνική ανάλυση. Για ανάλυση αγώνα άλλαξε το «Είδος βίντεο» στα Στοιχεία."), true);
            }

            modeSwitch.SetSelected(technical ? 1 : 0);
            SwimPage selected = tabs.SelectedTab;
            List<SwimPage> pages = technical
                ? new List<SwimPage> { tabRace, tabTechStart, tabTechTurns, tabTechMedia, tabConnection }
                : new List<SwimPage> { tabRace, tabStart, tabSwim, tabTurns, tabFinish, tabFinals, tabConnection };
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
            SwimPage page = new SwimPage(SwimLang.T("1 · Εκκίνηση"));
            page.Padding = new Padding(6);
            page.AutoScroll = true;
            TableLayoutPanel t = NewEventTable();

            lblStartSignal = ValueLabel();
            Button startLight = new SwimNoFocusButton { Text = SwimLang.T("💡 Αυτόματα (φως)"), AutoSize = true, Height = 26, Margin = new Padding(6, 2, 2, 2) };
            startLight.Click += (s, e) => manager.FindStartLight();
            toolTip.SetToolTip(startLight, SwimLang.T("Βρίσκει το καρέ που ανάβουν τα φώτα εκκίνησης (δίπλα στους βατήρες ή στον αφέτη), διαβάζοντας το βίντεο\n") +
                SwimLang.T("από την αρχή μέχρι να ξεκινήσει η κάμερα να ακολουθεί τους αθλητές. Το προτείνει για έλεγχο: Enter σωστό,\n") +
                SwimLang.T("βελάκια και Enter διόρθωση, Delete λάθος (δείχνει την επόμενη υποψήφια). Μαθαίνει από τις απαντήσεις σου."));
            Button autoRace = new SwimNoFocusButton { Text = SwimLang.T("🤖 Όλος ο αγώνας"), AutoSize = true, Height = 26, Margin = new Padding(6, 2, 2, 2) };
            autoRace.Click += (s, e) => manager.BeginAutoRace();
            toolTip.SetToolTip(autoRace, SwimLang.T("Μετά την εκκίνηση: κλικ στο κεφάλι και στα πόδια του αθλητή μετά την έξοδο από το νερό. Το Kinovea τον ακολουθεί\n") +
                SwimLang.T("σε όλα τα μήκη και προτείνει τα περάσματα (15/25/35/45 και τους τοίχους) από τα σημάδια των σχοινιών, για έλεγχο."));
            // The auto pilot (since 2026-10-18): the whole analysis without the analyst, then the PDF; its marks wait for the check.
            btnPilot = new SwimNoFocusButton { Text = SwimLang.T("✈ Αυτόματος πιλότος"), AutoSize = true, Height = 26, Margin = new Padding(6, 2, 2, 2) };
            btnPilot.Click += (s, e) => manager.RunAutoPilot();
            toolTip.SetToolTip(btnPilot, SwimLang.T("Όλη η ανάλυση του αγώνα μόνη της: η εκκίνηση από το φως ή τον ήχο, ο αθλητής που ακολουθεί η κάμερα, όλα\n") +
                SwimLang.T("τα μήκη (περάσματα, τοίχοι, έξοδοι από το νερό, χεριές) και στο τέλος το PDF. Ρωτά μόνο όταν δεν είναι σίγουρος (εκκίνηση,\n") +
                SwimLang.T("αθλητής). Ό,τι έβαλε περιμένει στον «Έλεγχο»: Enter σωστό, ←/→ και Enter διόρθωση, Delete λάθος. Από τις απαντήσεις σου\n") +
                SwimLang.T("μαθαίνει· ό,τι δεν ελέγξεις δεν διδάσκει. Η πρώτη κίνηση, η αποδέσμευση, η είσοδος και η αποκόλληση από τους τοίχους δεν\n") +
                SwimLang.T("φαίνονται καθαρά από την κερκίδα: μένουν προτάσεις στους συνήθεις χρόνους και μπαίνουν μόνο αν τις επιβεβαιώσεις."));
            btnRaceReview = new SwimNoFocusButton { Text = SwimLang.T("▶ Έλεγχος"), AutoSize = true, Height = 26, Margin = new Padding(6, 2, 2, 2) };
            btnRaceReview.Click += (s, e) =>
            {
                if (manager.PilotDoubtful > 0)
                    manager.BeginPilotReview();
                else
                    manager.BeginReview();
            };
            toolTip.SetToolTip(btnRaceReview, SwimLang.T("Οι προτάσεις του αγώνα μία μία στο βίντεο: Enter σωστό, ←/→ και Enter διόρθωση, Delete λάθος, Esc τέλος.\n") +
                SwimLang.T("Πρώτα τα αβέβαια του αυτόματου πιλότου (⚠: κάτω από 50%). Από τις απαντήσεις σου μαθαίνει."));
            AddEventRow(t, SwimLang.T("Σήμα εκκίνησης"), PlayerScreenCommands.SwimStart, SwimLang.T("Εκκίνηση"),
                SwimLang.T("Στο καρέ του σήματος εκκίνησης (φως/καπνός/ήχος): εκεί γίνεται t = 0."), lblStartSignal, startLight, autoRace, btnPilot, btnRaceReview);

            // Learning from the marks of a manual analysis.
            string learnHelp = SwimLang.T("Ανάλυση με το χέρι (χωρίς «🤖 Όλος ο αγώνας»): κάθε σημάδι σου διδάσκει το πρόγραμμα, όπως οι απαντήσεις στις προτάσεις.\n") +
                SwimLang.T("Η πρώτη κίνηση, η αποδέσμευση, η είσοδος και η αποκόλληση από κάθε τοίχο διδάσκουν αμέσως (από τους συνηθισμένους χρόνους).\n") +
                SwimLang.T("Για τα περάσματα, τους τοίχους και τις εξόδους: μία φορά σε κάθε αγώνα κλικ στο κεφάλι και στα πόδια του αθλητή (ζητείται μετά το\n") +
                SwimLang.T("πρώτο πέρασμα· αρκεί και το κλικ των αυτόματων χεριών). Ο αγώνας διαβάζεται στο παρασκήνιο, χωρίς να φαίνεται τίποτα, κι εσύ συνεχίζεις.\n") +
                SwimLang.T("Μια διόρθωση αντικαθιστά την παλιά απάντηση, μια αναίρεση την παίρνει πίσω. Η μάθηση μένει σε αυτόν τον υπολογιστή (μόνο αριθμοί).");
            chkLearnMarks = new CheckBox { Text = SwimLang.T("από τα πατήματά μου"), AutoSize = true, Margin = new Padding(6, 7, 3, 3) };
            chkLearnMarks.CheckedChanged += (s, e) =>
            {
                if (!updating)
                    manager.SetLearnFromMarks(chkLearnMarks.Checked);
            };
            toolTip.SetToolTip(chkLearnMarks, learnHelp);
            Button btnLearn = new SwimNoFocusButton { Text = SwimLang.T("🎓 Κλικ στον αθλητή"), AutoSize = true, Height = 26, Margin = new Padding(6, 2, 2, 2) };
            btnLearn.Click += (s, e) => manager.BeginQuietLearning();
            toolTip.SetToolTip(btnLearn, SwimLang.T("Μετά την εκκίνηση, σε ένα καρέ μετά την έξοδο από το νερό: κλικ στο κεφάλι και μετά στα πόδια του αθλητή.\n") +
                SwimLang.T("Ο αγώνας διαβάζεται στο παρασκήνιο και κάθε πέρασμα, τοίχος και έξοδος που μαρκάρεις διδάσκει το πρόγραμμα."));
            lblLearn = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(6, 7, 0, 3), Text = "–" };
            AddEventRow(t, SwimLang.T("Μάθηση"), null, null, learnHelp, chkLearnMarks, btnLearn, lblLearn);

            lblReactionAuto = ValueLabel();
            tbReaction = NumberBox();
            toolTip.SetToolTip(tbReaction, SwimLang.T("Χρόνος από το σήμα έως την πρώτη κίνηση, αν τον μετράς αλλιώς. Αν συμπληρωθεί, υπερισχύει του σημαδιού.\n") +
                SwimLang.T("Μην γράφεις εδώ τον επίσημο χρόνο αντίδρασης των αποτελεσμάτων (≈0.6 s): αυτός αντιστοιχεί στην αποδέσμευση από τον βατήρα."));
            AddEventRow(t, SwimLang.T("Αντίδραση (πρώτη κίνηση)"), PlayerScreenCommands.SwimReaction, SwimLang.T("Αντίδραση"),
                SwimLang.T("Η πρώτη κίνηση του αθλητή μετά το σήμα (όχι ο επίσημος χρόνος αντίδρασης, που είναι η αποδέσμευση από τον βατήρα)."),
                lblReactionAuto, Caption(SwimLang.T("ή γράψε:")), tbReaction, Unit("s"));

            lblBlock = ValueLabel();
            AddEventRow(t, SwimLang.T("Αποδέσμευση από τον βατήρα"), PlayerScreenCommands.SwimFeetOff, SwimLang.T("Αποδέσμευση"),
                SwimLang.T("Τα πόδια φεύγουν από τον βατήρα (χρόνος πάνω στον βατήρα). Αντιστοιχεί στον επίσημο χρόνο αντίδρασης των αποτελεσμάτων."), lblBlock);

            lblEntry = ValueLabel();
            tbEntryDistHands = NumberBox();
            AddEventRow(t, SwimLang.T("Είσοδος χεριών στο νερό"), PlayerScreenCommands.SwimEntry, SwimLang.T("Είσοδος χεριών"),
                SwimLang.T("Τα χέρια μπαίνουν στο νερό: χρόνος από το σήμα και χρόνος πτήσης (αποδέσμευση → είσοδος). Η απόσταση μετριέται από τον τοίχο εκκίνησης."),
                lblEntry, Caption(SwimLang.T("απόσταση:")), tbEntryDistHands, DistanceUnit(),
                PickButton(SwimPickTarget.EntryHands, SwimLang.T("Κλικ στο βίντεο εκεί που τα χέρια μπαίνουν στο νερό: υπολογίζεται η απόσταση από τον τοίχο εκκίνησης.")));

            tbEntryDistHips = NumberBox();
            AddEventRow(t, SwimLang.T("Είσοδος ισχίων"), null, null,
                SwimLang.T("Τα ισχία μπαίνουν στο νερό. Η απόσταση μετριέται από τον τοίχο εκκίνησης."),
                Caption(SwimLang.T("απόσταση:")), tbEntryDistHips, DistanceUnit(),
                PickButton(SwimPickTarget.EntryHips, SwimLang.T("Κλικ στο βίντεο εκεί που τα ισχία μπαίνουν στο νερό.")));

            tbEntryVelHands = NumberBox();
            tbEntryVelHips = NumberBox();
            tbEntryVelFeet = NumberBox();
            AddEventRow(t, SwimLang.T("Ταχύτητες εισόδου (m/s)"), null, null,
                SwimLang.T("Ταχύτητα των χεριών, των ισχίων και των ποδιών τη στιγμή της εισόδου."),
                Caption(SwimLang.T("χέρια")), tbEntryVelHands, Caption(SwimLang.T("ισχία")), tbEntryVelHips, Caption(SwimLang.T("πόδια")), tbEntryVelFeet);

            lblStartKicks = ValueLabel();
            AddEventRow(t, SwimLang.T("Υποβρύχιοι δελφινισμοί"), PlayerScreenCommands.SwimKick, SwimLang.T("Δελφινισμός"),
                SwimLang.T("Ένα πάτημα για κάθε υποβρύχιο δελφινισμό."), lblStartKicks);

            lblBreakout = ValueLabel();
            tbBreakoutDist = NumberBox();
            AddEventRow(t, SwimLang.T("Έξοδος από το νερό (κεφάλι)"), PlayerScreenCommands.SwimBreakout, SwimLang.T("Έξοδος"),
                SwimLang.T("Το κεφάλι βγαίνει από το νερό (χρόνος κάτω από το νερό). Η απόσταση μετριέται από τον τοίχο εκκίνησης."),
                lblBreakout, Caption(SwimLang.T("απόσταση:")), tbBreakoutDist, DistanceUnit(),
                PickButton(SwimPickTarget.StartBreakout, SwimLang.T("Κλικ στο κεφάλι εκεί που βγαίνει από το νερό.")));

            tbUnderwaterDist = NumberBox();
            AddEventRow(t, SwimLang.T("Απόσταση κάτω από το νερό"), null, null,
                SwimLang.T("Μέχρι πού έμεινε κάτω από το νερό, από τον τοίχο εκκίνησης."),
                tbUnderwaterDist, DistanceUnit(),
                PickButton(SwimPickTarget.StartUnderwater, SwimLang.T("Κλικ στο κεφάλι εκεί που φτάνει στην επιφάνεια.")));

            lblFirstSplit = ValueLabel();
            lblFirstSplitTitle = AddEventRow(t, SwimLang.T("Πέρασμα 15μ."), PlayerScreenCommands.SwimSplit, SwimLang.T("Πέρασμα"),
                SwimLang.T("Το κεφάλι περνά το πρώτο σημείο ελέγχου. Μετά συνεχίζεις στην καρτέλα Κολύμβηση."), lblFirstSplit);

            page.Controls.Add(t);
            return page;
        }

        private SwimPage BuildSwimTab()
        {
            SwimPage page = new SwimPage(SwimLang.T("2 · Κολύμβηση"));
            page.Padding = new Padding(6);

            // Comparison with an elite reference and what is drawn on the video.
            TableLayoutPanel compare = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, Margin = new Padding(0), Padding = new Padding(0, 0, 0, 2) };
            compare.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            compare.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            compare.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            compare.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            compare.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label lblCompare = new Label { Text = SwimLang.T("Σύγκριση με:"), AutoSize = true, Font = valueFont, ForeColor = colorValue, Margin = new Padding(0, 7, 4, 3) };
            cmbReference = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 4, 3) };
            cmbReference.SelectedIndexChanged += CmbReference_SelectedIndexChanged;
            toolTip.SetToolTip(cmbReference, SwimLang.T("Αναφορά για το Δ σε κάθε πέρασμα (αρνητικό = πιο γρήγορα από την αναφορά), το ghost και τη γραμμή στο βίντεο.\n") +
                SwimLang.T("Ίδιο αγώνισμα, στυλ και πισίνα: οι elite αναφορές, οι φιναλίστ, οι άλλοι κολυμβητές αυτού του βίντεο,\n") +
                SwimLang.T("οι άλλοι αγώνες του αθλητή και άλλων αθλητών που έχουν αναλυθεί στο Google Sheet (με το Apps Script 2.8.0)."));
            Button btnRefreshReferences = new SwimNoFocusButton { Text = "↻", Width = 30, Height = 25, Margin = new Padding(0, 2, 4, 2) };
            btnRefreshReferences.Click += (s, e) => manager.RefreshReferences();
            toolTip.SetToolTip(btnRefreshReferences, SwimLang.T("Ανανέωση των αναφορών από το φύλλο «Elite αναφορές» του Google Sheet"));
            btnSaveReference = new SwimNoFocusButton { Text = SwimLang.T("Ως αναφορά…"), AutoSize = true, Height = 25, Margin = new Padding(0, 2, 0, 2) };
            btnSaveReference.Click += (s, e) => SaveAsReference();
            toolTip.SetToolTip(btnSaveReference, SwimLang.T("Αποθήκευση αυτής της ανάλυσης ως elite αναφοράς (π.χ. ανάλυση ενός τελικού), για σύγκριση σε επόμενες αναλύσεις."));
            lblReferences = new Label { AutoSize = false, Height = 18, Dock = DockStyle.Fill, ForeColor = SwimTheme.Dim, AutoEllipsis = true, Margin = new Padding(0, 0, 0, 2) };
            compare.Controls.Add(lblCompare, 0, 0);
            compare.Controls.Add(cmbReference, 1, 0);
            compare.Controls.Add(btnRefreshReferences, 2, 0);
            compare.Controls.Add(btnSaveReference, 3, 0);
            compare.Controls.Add(BuildFinalsImportButton(), 4, 0);
            compare.Controls.Add(lblReferences, 0, 1);
            compare.SetColumnSpan(lblReferences, 5);

            // Previous race of the athlete (PB) for the comparison tables and charts of the report.
            Label lblPbTitle = new Label { Text = SwimLang.T("Σύγκριση στο report (PB):"), AutoSize = true, Font = valueFont, ForeColor = colorValue, Margin = new Padding(0, 7, 4, 3) };
            cmbPb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 4, 3) };
            cmbPb.SelectedIndexChanged += CmbPb_SelectedIndexChanged;
            toolTip.SetToolTip(cmbPb, SwimLang.T("Προηγούμενη ανάλυση του αθλητή στο ίδιο αγώνισμα για τους πίνακες και τα γραφήματα PB – αγώνας – elite του report.\n") +
                SwimLang.T("Αυτόματα: ο καλύτερος χρόνος του αθλητή πριν από αυτόν τον αγώνα."));
            Button btnRefreshPb = new SwimNoFocusButton { Text = "↻", Width = 30, Height = 25, Margin = new Padding(0, 2, 4, 2) };
            btnRefreshPb.Click += (s, e) => manager.RefreshPbCandidates();
            toolTip.SetToolTip(btnRefreshPb, SwimLang.T("Ανανέωση της λίστας των αναλύσεων του αθλητή από το Google Sheet"));
            lblPb = new Label { AutoSize = false, Height = 18, Dock = DockStyle.Fill, ForeColor = SwimTheme.Dim, AutoEllipsis = true, Margin = new Padding(0, 0, 0, 2) };
            compare.Controls.Add(lblPbTitle, 0, 2);
            compare.Controls.Add(cmbPb, 1, 2);
            compare.Controls.Add(btnRefreshPb, 2, 2);
            compare.Controls.Add(lblPb, 0, 3);
            compare.SetColumnSpan(lblPb, 5);

            FlowLayoutPanel overlay = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0, 0, 0, 4) };
            chkSpeedChart = new CheckBox { Text = SwimLang.T("Γράφημα ταχύτητας"), AutoSize = true, Margin = new Padding(6, 5, 3, 3) };
            // All the graphics of the analysis on the video on and off at once (2.9.0, key V, button of the bar).
            chkGraphics = new CheckBox { Text = SwimLang.T("Γραφικά (V)"), AutoSize = true, Margin = new Padding(6, 5, 3, 3), Font = valueFont };
            chkGraphics.CheckedChanged += (s, e) =>
            {
                if (!updating)
                    manager.SetGraphics(chkGraphics.Checked);
            };
            toolTip.SetToolTip(chkGraphics, SwimLang.T("Όλα τα γραφικά της ανάλυσης στο βίντεο μαζί (πίνακες, γραφήματα, σύγκριση, τεχνική): ναι ή όχι.\n") +
                SwimLang.T("Και με το πλήκτρο V ή το κουμπί «Γραφικά στο βίντεο» δίπλα στο βίντεο. Όχι: ούτε στις εξαγωγές του βίντεο."));
            // The graphics of a race as the HUD of the demo, or the boards of before (2.9.1).
            chkHud = new CheckBox { Text = SwimLang.T("HUD (όπως το demo)"), AutoSize = true, Margin = new Padding(6, 5, 3, 3) };
            chkHud.CheckedChanged += (s, e) =>
            {
                if (!updating)
                    manager.SetHud(chkHud.Checked);
            };
            toolTip.SetToolTip(chkHud, SwimLang.T("Τα γραφικά του αγώνα ως HUD, όπως το demo: πίνακας ανάλυσης αριστερά (χρόνος, φάση, ταχύτητα, συχνότητα),\n") +
                SwimLang.T("περάσματα και καταγραφή δεξιά, χάρτης πισίνας κάτω, ενδείξεις πάνω στο βίντεο και «Η ανάλυση με μια ματιά» μετά τον τερματισμό.\n") +
                SwimLang.T("Τα κουτάκια δίπλα προσθέτουν τα πινακάκια: Δ και ΔΑΚ, γράφημα Δ, πίνακας τελικού, ταχύτητα. Χωρίς αυτό: τα πινακάκια όπως πριν.\n") +
                SwimLang.T("Σε μικρή εικόνα (κάτω από 900×500) φαίνονται τα πινακάκια."));
            chkSplitBoard = new CheckBox { Text = SwimLang.T("Χρόνος, πέρασμα, Δ"), AutoSize = true, Margin = new Padding(6, 5, 3, 3) };
            cmbCorner = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 112, Margin = new Padding(3, 3, 3, 3) };
            cmbCorner.Items.AddRange(new object[] { SwimLang.T("Πάνω δεξιά"), SwimLang.T("Πάνω αριστερά"), SwimLang.T("Κάτω δεξιά"), SwimLang.T("Κάτω αριστερά") });
            chkOverlayExports = new CheckBox { Text = SwimLang.T("και στις εξαγωγές"), AutoSize = true, Margin = new Padding(10, 5, 3, 3) };
            chkVideoBars = new CheckBox { Text = SwimLang.T("Κουμπιά δεξιά-αριστερά"), AutoSize = true, Margin = new Padding(10, 5, 3, 3) };
            chkVideoBars.CheckedChanged += (s, e) =>
            {
                if (!updating)
                    manager.SetVideoBars(chkVideoBars.Checked);
            };
            toolTip.SetToolTip(chkVideoBars, SwimLang.T("Τα κουμπιά της ανάλυσης, ο χρόνος και τα περάσματα σε δύο στήλες αριστερά και δεξιά της εικόνας,\n") +
                SwimLang.T("στο μαύρο του βίντεο. Αυτό το παράθυρο ανοίγει μόνο όταν το θέλεις: κουμπί «Πίνακας ανάλυσης» ή Ctrl+Shift+W.\n") +
                SwimLang.T("Χωρίς αυτή την επιλογή, κάθε πλήκτρο της ανάλυσης ανοίγει το παράθυρο όπως πριν."));
            chkSpeedChart.CheckedChanged += (s, e) => CommitOverlayOptions();
            chkSplitBoard.CheckedChanged += (s, e) => CommitOverlayOptions();
            chkOverlayExports.CheckedChanged += (s, e) => CommitOverlayOptions();
            cmbCorner.SelectedIndexChanged += (s, e) => CommitOverlayOptions();
            toolTip.SetToolTip(chkOverlayExports, SwimLang.T("Τα γραφικά του βίντεο (χρόνος, γράφημα ταχύτητας, τιμές εκκίνησης και στροφών, γωνίες)\n") +
                SwimLang.T("μπαίνουν και στις εξαγωγές του Kinovea (βίντεο, εικόνα, αντιγραφή καρέ) και στα στιγμιότυπα του report,\n") +
                SwimLang.T("με τις ίδιες αναλογίες που έχουν στην οθόνη. Ισχύει όσο είναι ανοιχτή η ανάλυση (παράθυρο ή κουμπιά στο βίντεο)."));
            toolTip.SetToolTip(chkSpeedChart, SwimLang.T("Γράφημα με την ταχύτητα κάθε τμήματος πάνω στο βίντεο (αθλητής και αναφορά). Ακολουθεί τον χρόνο του βίντεο.\n") +
                SwimLang.T("Συνεχής γραμμή: καθαρή κολύμβηση · διάστικτη: εκκίνηση, στροφές και τελευταία 5μ. (συμβατική ταχύτητα)."));
            toolTip.SetToolTip(chkSplitBoard, SwimLang.T("Χρόνος αγώνα, τελευταίο πέρασμα και Δ με την αναφορά πάνω στο βίντεο."));
            overlay.Controls.Add(Caption(SwimLang.T("Στο βίντεο:")));
            overlay.Controls.Add(chkGraphics);
            overlay.Controls.Add(chkHud);
            overlay.Controls.Add(chkSpeedChart);
            overlay.Controls.Add(chkSplitBoard);
            AddFinalsOverlayOptions(overlay);
            overlay.Controls.Add(Caption(SwimLang.T("θέση")));
            overlay.Controls.Add(cmbCorner);
            overlay.Controls.Add(chkOverlayExports);
            overlay.Controls.Add(chkVideoBars);
            Control compareOptions = BuildCompareOptions();

            TableLayoutPanel t = NewEventTable();
            AddEventRow(t, SwimLang.T("Χεριά"), PlayerScreenCommands.SwimStroke, SwimLang.T("Χεριά"),
                SwimLang.T("Σε κάθε είσοδο χεριού. Ελεύθερο/ύπτιο: κάθε χέρι · πεταλούδα/πρόσθιο: κάθε κύκλος."), Caption(SwimLang.T("Οι μισές χεριές στα σημεία ελέγχου βγαίνουν αυτόματα.")));

            string autoHelp = SwimLang.T("Οι χεριές κάθε τμήματος μετριούνται από το βίντεο, όταν η κάμερα ακολουθεί τον αθλητή (κερκίδα ή πλάγια λήψη).\n") +
                SwimLang.T("1. Μετά την έξοδο από το νερό (B), κάνε κλικ στο κεφάλι και μετά στα πόδια του αθλητή (μία φορά σε κάθε μήκος).\n") +
                SwimLang.T("2. Σε κάθε πέρασμα (S) βλέπεις τις χεριές από το προηγούμενο πέρασμα: Enter αν είναι σωστές, αλλιώς γράψε τον σωστό αριθμό.\n") +
                SwimLang.T("Ό,τι επιβεβαιώνεις ή γράφεις μένει (✓ στη στήλη Χεριές) και πάει στο Sheet· διπλό κλικ στη στήλη Χεριές για διόρθωση.\n") +
                SwimLang.T("Οι χεριές μπαίνουν και ως σημάδια στο βίντεο: ◀ ▶ πηγαίνουν από χεριά σε χεριά, ✕ διαγράφει τη χεριά του καρέ.\n") +
                SwimLang.T("«Επιλογή αθλητή» ξεκινά το μήκος από το καρέ που βλέπεις, χωρίς το B.");
            chkAutoStrokes = new CheckBox { Text = SwimLang.T("σε κάθε πέρασμα"), AutoSize = true, Margin = new Padding(6, 7, 3, 3) };
            chkAutoStrokes.CheckedChanged += (s, e) =>
            {
                if (!updating)
                    manager.SetAutoStrokes(chkAutoStrokes.Checked);
            };
            toolTip.SetToolTip(chkAutoStrokes, autoHelp);
            btnAutoStrokes = new SwimNoFocusButton { Text = SwimLang.T("Επιλογή αθλητή"), AutoSize = true, Height = 26, Margin = new Padding(6, 2, 2, 2) };
            btnAutoStrokes.Click += (s, e) => manager.BeginAutoStrokes();
            toolTip.SetToolTip(btnAutoStrokes, SwimLang.T("Κλικ στο κεφάλι και στα πόδια του αθλητή σε αυτό το καρέ: οι χεριές μετριούνται σε κάθε πέρασμα μέχρι τον τοίχο.\n") +
                SwimLang.T("Αν τα περάσματα του μήκους είναι ήδη μαρκαρισμένα, οι χεριές τους εμφανίζονται αμέσως, ένα τμήμα τη φορά."));
            Button btnDeleteAuto = new SwimNoFocusButton { Text = SwimLang.T("Διαγραφή αυτόματων"), AutoSize = true, Height = 26, Margin = new Padding(6, 2, 2, 2) };
            btnDeleteAuto.Click += (s, e) => AskDeleteAutoStrokes();
            toolTip.SetToolTip(btnDeleteAuto, SwimLang.T("Διαγράφει τα σημάδια χεριών της αυτόματης μέτρησης σε αυτόν τον αγώνα. Οι χεριές που έβαλες εσύ και οι αριθμοί που επιβεβαίωσες μένουν."));
            lblAutoStrokes = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(6, 7, 0, 3), Text = "–" };
            AddEventRow(t, SwimLang.T("Αυτόματες χεριές"), null, null, autoHelp,
                chkAutoStrokes,
                btnAutoStrokes,
                StrokeButton("◀", SwimLang.T("Προηγούμενη χεριά"), (s, e) => manager.SeekStroke(-1)),
                StrokeButton("▶", SwimLang.T("Επόμενη χεριά"), (s, e) => manager.SeekStroke(1)),
                StrokeButton("✕", SwimLang.T("Διαγραφή της χεριάς σε αυτό το καρέ (C για να την ξαναβάλεις)"), (s, e) => manager.DeleteStrokeHere()),
                btnDeleteAuto, lblAutoStrokes);

            string reelHelp = SwimLang.T("Τεχνική ανάλυση πάνω στο βίντεο, όπως στα reels κολύμβησης: ετικέτα που ακολουθεί τον αθλητή, χρόνος αγώνα με\n") +
                SwimLang.T("χεριές του μήκους, συχνότητα και απόσταση του τελευταίου κύκλου, ταχύτητα, φάση (εκκίνηση, υποβρύχια, στυλ, στροφή),\n") +
                SwimLang.T("σημάδια στην αντίδραση, την είσοδο, το breakout και τα περάσματα, γράφημα των κύκλων του μήκους και κάρτα σύνοψης μετά τον τερματισμό.\n") +
                SwimLang.T("Οι χρόνοι και οι χεριές είναι από τα σημάδια της ανάλυσης· η απόσταση και η ταχύτητα ανά κύκλο εκτιμώνται από τα περάσματα (≈).\n") +
                SwimLang.T("Αντικαθιστά τον χρόνο και το γράφημα ταχύτητας στο βίντεο, και μπαίνει και στην εξαγωγή βίντεο («και στις εξαγωγές»).");
            chkReel = new CheckBox { Text = SwimLang.T("στο βίντεο"), AutoSize = true, Margin = new Padding(6, 7, 3, 3) };
            chkReel.CheckedChanged += (s, e) =>
            {
                if (!updating)
                    manager.SetReel(chkReel.Checked);
            };
            toolTip.SetToolTip(chkReel, reelHelp);
            btnTrack = new SwimNoFocusButton { Text = SwimLang.T("Ακολούθηση αθλητή"), AutoSize = true, Height = 26, Margin = new Padding(6, 2, 2, 2) };
            btnTrack.Click += (s, e) => manager.BeginTrackSwimmer();
            toolTip.SetToolTip(btnTrack, SwimLang.T("Για την ετικέτα πάνω στον αθλητή: σε ένα καρέ μετά την έξοδο από το νερό, κλικ στο κεφάλι και μετά στα πόδια του.\n") +
                SwimLang.T("Το μήκος διαβάζεται μέχρι τον τοίχο του (μία φορά σε κάθε μήκος). Τα σημάδια της ανάλυσης δεν αλλάζουν.\n") +
                SwimLang.T("Οι αυτόματες χεριές βρίσκουν και αυτές τη θέση του αθλητή στα μήκη που μετράνε."));
            Button btnDeleteTrack = new SwimNoFocusButton { Text = "✕", Width = 32, Height = 26, Margin = new Padding(2, 2, 0, 2) };
            btnDeleteTrack.Click += (s, e) => manager.DeleteTrack();
            toolTip.SetToolTip(btnDeleteTrack, SwimLang.T("Διαγραφή της ακολούθησης του αθλητή (η ετικέτα δεν φαίνεται πια)"));
            chkReelSummary = new CheckBox { Text = SwimLang.T("σύνοψη στο τέλος"), AutoSize = true, Margin = new Padding(10, 7, 3, 3) };
            chkReelSummary.CheckedChanged += (s, e) =>
            {
                if (!updating)
                    manager.SetReelSummary(chkReelSummary.Checked);
            };
            toolTip.SetToolTip(chkReelSummary, SwimLang.T("Κάρτα με τα στοιχεία του αγώνα στο τέλος του βίντεο, με όλα τα γραφικά (και τους πίνακες) και στις εξαγωγές: τελικός χρόνος,\n") +
                SwimLang.T("αντίδραση, 15μ., breakout, ενδιάμεσοι, χεριές, συχνότητα, απόσταση ανά κύκλο και ταχύτητα ανά τμήμα, ΔΑΚ κάθε 50άρι, στροφές −5/+15,\n") +
                SwimLang.T("breakout στροφών, τελευταία 5μ. και η απόσταση κάθε κύκλου. Έρχεται ένα δευτερόλεπτο μετά τον τερματισμό (μετά τον πίνακα του τελικού, όταν υπάρχει).\n") +
                SwimLang.T("Στο HUD είναι «Η ανάλυση με μια ματιά»."));
            lblTrack = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(6, 7, 0, 3), Text = "–" };
            AddEventRow(t, SwimLang.T("Τεχνική ανάλυση (reel)"), null, null, reelHelp, chkReel, btnTrack, btnDeleteTrack, chkReelSummary, lblTrack);

            AddEventRow(t, SwimLang.T("Πέρασμα σημείου ελέγχου"), PlayerScreenCommands.SwimSplit, SwimLang.T("Πέρασμα"),
                SwimLang.T("Το κεφάλι περνά το επόμενο σημείο ελέγχου (25μ., 35μ., …). Στους τοίχους: το χέρι ακουμπά τον τοίχο."), Caption(SwimLang.T("Η κίτρινη γραμμή είναι το επόμενο σημείο.")));

            grid = CreateGrid();
            grid.Columns.Add(Column("d", SwimLang.T("Απόσταση"), 62));
            grid.Columns.Add(Column("t", SwimLang.T("Χρόνος"), 62));
            grid.Columns.Add(Column("seg", SwimLang.T("Τμήμα"), 56));
            grid.Columns.Add(Column("vel", SwimLang.T("Ταχ. m/s"), 58));
            grid.Columns.Add(Column("strokes", SwimLang.T("Χεριές"), 52));
            grid.Columns.Add(Column("sr", "SR", 50));
            grid.Columns.Add(Column("dps", "DPS", 50));
            grid.Columns.Add(Column("sl", "SL", 46));
            grid.Columns.Add(Column("srx", SwimLang.T("SR χρόνων"), 64));
            grid.Columns.Add(Column("ref", SwimLang.T("Αναφορά"), 62));
            grid.Columns.Add(Column("delta", SwimLang.T("Δ"), 54));
            grid.Columns["vel"].ToolTipText = SwimLang.T("Μέση ταχύτητα του τμήματος. Πορτοκαλί: εκκίνηση, στροφή ή τελευταία 5μ. πριν τον τοίχο, όπου η ταχύτητα είναι συμβατική (στον τοίχο μετράει η επαφή, όχι το κεφάλι).");
            grid.Columns["strokes"].ToolTipText = SwimLang.T("Χεριές του τμήματος. ✓: επιβεβαιωμένο ή γραμμένο από εσένα. Διπλό κλικ: διόρθωση.");
            grid.Columns["sr"].ToolTipText = SwimLang.T("Συχνότητα (κύκλοι/λεπτό), μόνο στην καθαρή κολύμβηση: από 15μ. μετά τον τοίχο έως 5μ. πριν τον επόμενο.");
            grid.Columns["dps"].ToolTipText = SwimLang.T("Μέτρα ανά χεριά (ελεύθερο/ύπτιο: ανά χέρι), μόνο στην καθαρή κολύμβηση.");
            grid.Columns["sl"].ToolTipText = SwimLang.T("Μήκος κύκλου (m/κύκλο), μόνο στην καθαρή κολύμβηση. Ελεύθερο/ύπτιο: 2 × DPS.");
            grid.Columns["srx"].ToolTipText = SwimLang.T("Συχνότητα από τα διαστήματα ανάμεσα στα πατήματα μέσα στο τμήμα (δεν επηρεάζεται από το γλίστρημα). Με λίγες χεριές είναι ενδεικτική.");
            grid.CellDoubleClick += Grid_CellDoubleClick;
            grid.KeyDown += Grid_KeyDown;

            Label hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 32,
                ForeColor = SwimTheme.Dim,
                Text = SwimLang.T("Διπλό κλικ: μετάβαση του βίντεο στο σημείο (μετά σωστό καρέ με ← → και S: ο χρόνος του αλλάζει· στη στήλη Χεριές: διόρθωση) · Delete: διαγραφή του χρόνου · SR / DPS / SL μόνο στην καθαρή κολύμβηση (+15μ. έως −5μ.) · πορτοκαλί: συμβατική ταχύτητα")
            };

            lnkChecks = new LinkLabel { Dock = DockStyle.Bottom, Height = 34, AutoEllipsis = true, Visible = false, LinkColor = colorWarn, ActiveLinkColor = colorError, Padding = new Padding(0, 2, 0, 0) };
            lnkChecks.LinkClicked += (s, e) => tabs.SelectedTab = tabFinish;
            toolTip.SetToolTip(lnkChecks, SwimLang.T("Έλεγχοι των δεδομένων: όλοι στην καρτέλα Τερματισμός."));

            page.Controls.Add(grid);
            page.Controls.Add(lnkChecks);
            page.Controls.Add(hint);
            page.Controls.Add(BuildDeltaView());
            page.Controls.Add(t);
            page.Controls.Add(compareOptions);
            page.Controls.Add(overlay);
            page.Controls.Add(compare);
            return page;
        }

        private SwimPage BuildTurnsTab()
        {
            SwimPage page = new SwimPage(SwimLang.T("3 · Στροφές"));
            page.Padding = new Padding(6);

            lblTurnCurrent = new Label { Dock = DockStyle.Top, Height = 24, Font = valueFont, ForeColor = colorBusy, TextAlign = ContentAlignment.MiddleLeft };

            TableLayoutPanel t = NewEventTable();
            lblTurnGap = ValueLabel();
            AddEventRow(t, SwimLang.T("Τελευταία χεριά πριν τον τοίχο"), PlayerScreenCommands.SwimStroke, SwimLang.T("Χεριά"),
                SwimLang.T("Η τελευταία είσοδος χεριού πριν τη στροφή. Στις ανάποδες στροφές (ελεύθερο/ύπτιο) από εδώ μετράει ο χρόνος στροφής."), lblTurnGap);
            lblTurnWall = ValueLabel();
            AddEventRow(t, SwimLang.T("Επαφή στον τοίχο"), PlayerScreenCommands.SwimSplit, SwimLang.T("Πέρασμα"),
                SwimLang.T("Το πέρασμα στον τοίχο (το χέρι ή τα πόδια ακουμπούν τον τοίχο)."), lblTurnWall);
            lblTurnTime = ValueLabel();
            AddEventRow(t, SwimLang.T("Αποκόλληση από τον τοίχο"), PlayerScreenCommands.SwimFeetOff, SwimLang.T("Αποκόλληση"),
                SwimLang.T("Τα πόδια φεύγουν από τον τοίχο: δίνει τον χρόνο στροφής. Ανάποδη στροφή (ελεύθερο/ύπτιο): από την τελευταία είσοδο χεριού. ") +
                SwimLang.T("Ανοιχτή (πεταλούδα, πρόσθιο, αλλαγή στυλ): από την επαφή στον τοίχο."), lblTurnTime);
            lblTurnKicks = ValueLabel();
            AddEventRow(t, SwimLang.T("Υποβρύχιοι δελφινισμοί"), PlayerScreenCommands.SwimKick, SwimLang.T("Δελφινισμός"),
                SwimLang.T("Ένα πάτημα για κάθε υποβρύχιο δελφινισμό μετά τη στροφή."), lblTurnKicks);
            lblTurnUnderwater = ValueLabel();
            AddEventRow(t, SwimLang.T("Έξοδος από το νερό (κεφάλι)"), PlayerScreenCommands.SwimBreakout, SwimLang.T("Έξοδος"),
                SwimLang.T("Το κεφάλι βγαίνει από το νερό. Τα μέτρα κάτω από το νερό μετριούνται από τον τοίχο της στροφής."),
                lblTurnUnderwater,
                PickButton(SwimPickTarget.TurnUnderwater, SwimLang.T("Κλικ στο κεφάλι εκεί που βγαίνει από το νερό: μέτρα κάτω από το νερό από τον τοίχο της στροφής.")));

            gridTurns = CreateGrid();
            gridTurns.ReadOnly = false;
            gridTurns.Columns.Add(Column("wall", SwimLang.T("Τοίχος"), 70));
            gridTurns.Columns.Add(Column("uwDist", SwimLang.T("Απόσταση κάτω από το νερό"), 92));
            gridTurns.Columns.Add(Column("turnTime", SwimLang.T("Χρόνος στροφής (s)"), 82));
            gridTurns.Columns["turnTime"].ToolTipText = SwimLang.T("Ανάποδη στροφή (ελεύθερο/ύπτιο): τελευταία είσοδος χεριού → αποκόλληση. Ανοιχτή (πεταλούδα, πρόσθιο, αλλαγή στυλ): επαφή → αποκόλληση. Σκυταλοδρομία: χρόνος αλλαγής. Γράφεται και με το χέρι.");
            gridTurns.Columns.Add(Column("uwTime", SwimLang.T("Υποβρύχιος χρόνος (s)"), 86));
            gridTurns.Columns.Add(Column("kicks", SwimLang.T("Δελφινισμοί"), 72));
            gridTurns.Columns.Add(Column("gap", SwimLang.T("Τελ. χεριά → τοίχος (s)"), 90));
            foreach (DataGridViewColumn c in gridTurns.Columns)
            {
                c.ReadOnly = c.Name != "uwDist" && c.Name != "turnTime";
                c.DefaultCellStyle.BackColor = c.ReadOnly && c.Name != "wall" ? SwimTheme.RowAlt : SwimTheme.Input;
            }
            gridTurns.CellBeginEdit += (s, e) =>
            {
                turnEditRace = manager.Race;
                turnEditText = Convert.ToString(gridTurns.Rows[e.RowIndex].Cells[e.ColumnIndex].Value, CultureInfo.InvariantCulture);
            };
            gridTurns.CellEndEdit += GridTurns_CellEndEdit;
            gridTurns.CellDoubleClick += GridTurns_CellDoubleClick;

            Label lblTurns = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Text = SwimLang.T("Όλες οι στροφές: τα λευκά κελιά γράφονται και με το χέρι (το χειροκίνητο υπερισχύει). Διπλό κλικ: μετάβαση του βίντεο στον τοίχο."),
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
            SwimPage page = new SwimPage(SwimLang.T("4 · Τερματισμός & ΔΑΚ"));
            page.Padding = new Padding(6);
            TableLayoutPanel t = NewEventTable();

            lblFinishGap = ValueLabel();
            AddEventRow(t, SwimLang.T("Τελευταία χεριά"), PlayerScreenCommands.SwimStroke, SwimLang.T("Χεριά"),
                SwimLang.T("Η τελευταία είσοδος χεριού πριν τον τερματισμό."), lblFinishGap);

            lblFinalTime = ValueLabel();
            AddEventRow(t, SwimLang.T("Τερματισμός (επαφή στον τοίχο)"), PlayerScreenCommands.SwimSplit, SwimLang.T("Τερματισμός"),
                SwimLang.T("Το χέρι ακουμπά τον τοίχο: τελικός χρόνος (το τελευταίο σημείο ελέγχου)."), lblFinalTime);

            string officialHelp = SwimLang.T("Ο επίσημος χρόνος από τα αποτελέσματα, π.χ. 1:01.23. Μπορείς να γράψεις και τους επίσημους ενδιάμεσους ") +
                SwimLang.T("των 50 πριν από αυτόν (αθροιστικά, όπως στα αποτελέσματα): 28.45 1:01.23.\n") +
                SwimLang.T("Το «Όλος ο αγώνας» και ο αυτόματος πιλότος βάζουν τον τερματισμό και τους τοίχους αυτών των αποστάσεων στους επίσημους χρόνους ") +
                SwimLang.T("και τα 45 μ. πριν από αυτούς από αυτούς· η ανάλυση και το PDF δείχνουν τους επίσημους χρόνους.\n") +
                SwimLang.T("Γραμμένο μετά την ανάγνωση, μετακινεί τα σημάδια του πιλότου που δεν έχεις ελέγξει ακόμη.");
            tbOfficial = new TextBox { Width = 150, Margin = new Padding(3, 3, 2, 3) };
            tbOfficial.TextChanged += (s, e) =>
            {
                if (!updating && officialEditRace == null)
                    officialEditRace = manager.Race;
            };
            tbOfficial.Leave += (s, e) => CommitOfficial();
            tbOfficial.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    CommitOfficial();
                }
            };
            toolTip.SetToolTip(tbOfficial, officialHelp);
            lblOfficial = ValueLabel();
            AddEventRow(t, SwimLang.T("Επίσημος χρόνος (αποτελέσματα)"), null, null, officialHelp, tbOfficial, lblOfficial);

            tbFinishDist = NumberBox();
            AddEventRow(t, SwimLang.T("Απόσταση τελευταίας χεριάς"), null, null,
                SwimLang.T("Απόσταση από τον τοίχο τερματισμού τη στιγμή της τελευταίας χεριάς."),
                tbFinishDist, DistanceUnit(),
                PickButton(SwimPickTarget.FinishLastStroke, SwimLang.T("Κλικ στο σημείο εισόδου του χεριού στην τελευταία χεριά: απόσταση από τον τοίχο τερματισμού.")));

            string finalFrameHelp = SwimLang.T("Το καρέ του τερματισμού με τα γραφικά του βίντεο (τελικός χρόνος, πέρασμα, γράφημα ταχύτητας) μπαίνει στο report και στο PDF του αγώνα.\n") +
                SwimLang.T("Λαμβάνεται αυτόματα όταν μαρκάρεις τον τερματισμό. Με το κουμπί το ξαναπαίρνεις από το καρέ που βλέπεις (αντικαθιστά το προηγούμενο).");
            btnFinalFrame = new SwimNoFocusButton { Text = SwimLang.T("📷 Από αυτό το καρέ"), AutoSize = true, Height = 26, Margin = new Padding(6, 2, 2, 2) };
            btnFinalFrame.Click += (s, e) => manager.CaptureFinalFrameNow();
            toolTip.SetToolTip(btnFinalFrame, finalFrameHelp);
            lblFinalFrame = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(3, 7, 0, 3), Text = "–" };
            AddEventRow(t, SwimLang.T("Τελικό καρέ για το report"), null, null, finalFrameHelp, btnFinalFrame, lblFinalFrame);

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
            toolTip.SetToolTip(tileDak, SwimLang.T("ΔΑΚ (Δείκτης Απόδοσης Κολύμβησης): (χεριές + χρόνος από τα 15 έως τον τοίχο) / (μέτρα / 10), για κάθε μήκος. Μέσος όρος των 50άρι."));

            Label lbl50 = new Label { Text = SwimLang.T("Ανά 50 · ΔΑΚ"), Dock = DockStyle.Top, Height = 22, Font = valueFont, ForeColor = SwimTheme.Amber, TextAlign = ContentAlignment.BottomLeft };
            grid50 = CreateGrid();
            grid50.Dock = DockStyle.Top;
            grid50.Height = 150;
            grid50.Columns.Add(Column("b", "50", 70));
            grid50.Columns.Add(Column("time", SwimLang.T("Χρόνος"), 56));
            grid50.Columns.Add(Column("strokes", SwimLang.T("Χεριές"), 50));
            grid50.Columns.Add(Column("swolf", "Swolf", 50));
            grid50.Columns.Add(Column("dak", SwimLang.T("ΔΑΚ"), 54));
            grid50.Columns.Add(Column("sr", "SR", 46));
            grid50.Columns.Add(Column("dps", "DPS", 46));
            grid50.Columns.Add(Column("vel", SwimLang.T("Ταχ. m/s"), 56));
            grid50.Columns["dak"].DefaultCellStyle.ForeColor = SwimTheme.Amber;
            grid50.Columns["dak"].DefaultCellStyle.Font = SwimTheme.Ui(9f, FontStyle.Bold);
            grid50.Columns["dak"].ToolTipText = SwimLang.T("ΔΑΚ: (χεριές + χρόνος από τα 15 έως τον τοίχο) / (μέτρα / 10), για κάθε μήκος του 50άρι (πισίνα 50μ. /3,5 όπως στο Excel).");
            grid50.Columns["sr"].ToolTipText = SwimLang.T("Συχνότητα της καθαρής κολύμβησης του 50άρι (σταθμισμένη).");
            grid50.Columns["dps"].ToolTipText = SwimLang.T("Μήκος χεριάς της καθαρής κολύμβησης του 50άρι (σταθμισμένο).");

            Label lblChecks = new Label { Text = SwimLang.T("Έλεγχος δεδομένων"), Dock = DockStyle.Top, Height = 24, Font = valueFont, ForeColor = colorValue, TextAlign = ContentAlignment.BottomLeft };
            tbChecks = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle };
            toolTip.SetToolTip(tbChecks, SwimLang.T("Πιθανά λάθη στα σημάδια (χαμένο ή διπλό πάτημα, πέρασμα σε λάθος σημείο, σειρά της εκκίνησης) και τιμές έξω από τα ρεαλιστικά όρια."));

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
            SwimCard group = new SwimCard { Title = SwimLang.T("Μέτρηση αποστάσεων στο βίντεο (βαθμονόμηση πισίνας)"), Dock = DockStyle.Bottom, Height = 112 };

            FlowLayoutPanel row1 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            cmbLine1 = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 50 };
            cmbLine2 = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 50 };
            tbLaneWidth = new TextBox { Width = 40 };
            toolTip.SetToolTip(cmbLine1, SwimLang.T("Απόσταση της πρώτης γραμμής από τον τοίχο εκκίνησης (π.χ. 0 = ο τοίχος, 5 = αλλαγή χρώματος στα σχοινιά)."));
            toolTip.SetToolTip(cmbLine2, SwimLang.T("Απόσταση της δεύτερης γραμμής από τον τοίχο εκκίνησης (π.χ. 5 ή 15)."));
            toolTip.SetToolTip(tbLaneWidth, SwimLang.T("Απόσταση ανάμεσα στα δύο σχοινιά του διαδρόμου (2.5 μ. σε ολυμπιακή πισίνα)."));
            tbLaneWidth.TextChanged += (s, e) => CommitLaneWidth();
            row1.Controls.Add(Caption(SwimLang.T("Γραμμές στα")));
            row1.Controls.Add(cmbLine1);
            row1.Controls.Add(Caption(SwimLang.T("και")));
            row1.Controls.Add(cmbLine2);
            row1.Controls.Add(Caption(SwimLang.T("από τον τοίχο εκκίνησης (μονάδες πισίνας) · διάδρομος")));
            row1.Controls.Add(tbLaneWidth);
            row1.Controls.Add(Unit(SwimLang.T("μ.")));

            FlowLayoutPanel row2 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            btnCalibrate = new SwimNoFocusButton { Text = SwimLang.T("Βαθμονόμηση (4 κλικ)"), AutoSize = true, Height = 26 };
            btnCalibrate.Click += (s, e) => manager.BeginCalibration(ParseLine(cmbLine1, 0), ParseLine(cmbLine2, 5));
            toolTip.SetToolTip(btnCalibrate, SwimLang.T("Σε σταματημένο καρέ: κλικ στις δύο γραμμές πάνω στα δύο σχοινιά του διαδρόμου (Α, Β, Α, Β)."));
            btnDeleteCalibration = new SwimNoFocusButton { Text = SwimLang.T("Διαγραφή"), AutoSize = true, Height = 26 };
            btnDeleteCalibration.Click += (s, e) => manager.DeleteCalibration();
            toolTip.SetToolTip(btnDeleteCalibration, SwimLang.T("Διαγραφή της βαθμονόμησης αυτού του καρέ (ή της πιο κοντινής)."));
            chkFixedCamera = new CheckBox { Text = SwimLang.T("Σταθερή κάμερα"), AutoSize = true, Margin = new Padding(10, 6, 3, 3) };
            chkFixedCamera.CheckedChanged += (s, e) =>
            {
                if (updating)
                    return;
                manager.Settings.FixedCamera = chkFixedCamera.Checked;
                manager.Settings.Save();
                RefreshCalibrationLabel();
            };
            toolTip.SetToolTip(chkFixedCamera, SwimLang.T("Σταθερή κάμερα: μία βαθμονόμηση για όλο το βίντεο. Αν η κάμερα ακολουθεί τον αθλητή, άφησέ το κενό: κάθε απόσταση μετριέται σε καρέ με δική του βαθμονόμηση."));
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
            SwimPage page = new SwimPage(SwimLang.T("Στοιχεία αγώνα"));
            page.Padding = new Padding(8);
            TableLayoutPanel table = NewTable();

            // Athlete from the registry of the main Google Sheet (KOE App).
            TableLayoutPanel searchRow = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tbAthleteSearch = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0) };
            Button btnReloadAthletes = new SwimNoFocusButton { Text = "↻", Width = 30, Height = 24, Margin = new Padding(0) };
            btnReloadAthletes.Click += (s, e) => manager.RefreshAthletes();
            toolTip.SetToolTip(btnReloadAthletes, SwimLang.T("Ανανέωση της λίστας αθλητών από το Google Sheet"));
            toolTip.SetToolTip(tbAthleteSearch, SwimLang.T("Γράψε μέρος του ονόματος, το μητρώο ή το σωματείο. Enter: επιλογή του πρώτου."));
            searchRow.Controls.Add(tbAthleteSearch, 0, 0);
            searchRow.Controls.Add(btnReloadAthletes, 1, 0);

            // An international athlete not in the registry (since 2.8.1): found in the public search of World Aquatics by the name
            // typed in the search (else the name in the report), registered with his code.
            Button btnWorldAquatics = new SwimNoFocusButton { Text = "🌐", Width = 30, Height = 24, Margin = new Padding(4, 0, 0, 0) };
            btnWorldAquatics.Click += (s, e) =>
                manager.SearchWorldAquatics(tbAthleteSearch.Text.Trim().Length > 0 ? tbAthleteSearch.Text : tbAthlete.Text);
            toolTip.SetToolTip(btnWorldAquatics, SwimLang.T("Αναζήτηση διεθνούς αθλητή στη World Aquatics, με το όνομα που έγραψες στην αναζήτηση ") +
                SwimLang.T("(αλλιώς με το «Όνομα στο report»): διαλέγεις τον αθλητή και καταχωρείται στους διεθνείς αθλητές με τον κωδικό του."));
            searchRow.Controls.Add(btnWorldAquatics, 2, 0);
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
            toolTip.SetToolTip(cmbPool, SwimLang.T("Πισίνα 50μ., 25μ. ή 25 γιαρδών. Στις γιάρδες οι αποστάσεις είναι σε yd και οι ταχύτητες / μήκη χεριάς σε m (με τις τιμές σε yd στο report)."));
            cmbKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbKind.Items.AddRange(new object[] { SwimLang.T("Αγώνας (race analysis + τεχνική εκκίνησης/στροφών)"), SwimLang.T("Προπόνηση: προσπάθειες εκκινήσεων / στροφών (τεχνική ανάλυση)") });
            toolTip.SetToolTip(cmbKind, SwimLang.T("Βίντεο προπόνησης: κάθε σήμα εκκίνησης (G) ξεκινά μια προσπάθεια εκκίνησης και κάθε επαφή στον τοίχο (W) μια προσπάθεια στροφής."));
            tbCompetition = new TextBox();
            tbLocation = new TextBox();
            cmbRound = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
            cmbRound.Items.AddRange(new object[] { "Heats", "Semi-final", "Final", "Προκριματικός", "Ημιτελικός", "Τελικός" });
            tbDate = new TextBox();
            tbCheckpoints = new TextBox { Multiline = true, Height = 64, ScrollBars = ScrollBars.Vertical };
            Button btnDefaults = new SwimNoFocusButton { Text = SwimLang.T("Προεπιλεγμένα σημεία"), AutoSize = true, Height = 26 };
            btnDefaults.Click += (s, e) => ApplyDefaultCheckpoints();

            AddRow(table, SwimLang.T("Είδος βίντεο"), cmbKind);
            AddRow(table, SwimLang.T("Αθλητής (από KOE App)"), searchRow);
            AddRow(table, "", lbAthletes);
            AddRow(table, "", lblAthlete);
            AddRow(table, "", lblAthleteList);
            AddRow(table, SwimLang.T("Όνομα στο report"), tbAthlete);
            AddRow(table, SwimLang.T("Διάδρομος"), nudLane);
            AddRow(table, SwimLang.T("Στυλ"), cmbStyle);
            AddRow(table, SwimLang.T("Απόσταση"), cmbDistance);
            AddRow(table, SwimLang.T("Πισίνα"), cmbPool);
            AddRow(table, SwimLang.T("Διοργάνωση"), tbCompetition);
            AddRow(table, SwimLang.T("Τόπος"), tbLocation);
            // (Φάση: the round of the competition here, the phase of the technical analysis elsewhere.)
            AddRow(table, SwimLang.English ? "Round" : "Φάση", cmbRound);
            AddRow(table, SwimLang.T("Ημερομηνία"), tbDate);

            // The official results on the web (since 2026-10-19): the competition's link (KOE live results, a Microplus results site,
            // a World Aquatics competition), or none (the KOE live results by the date). What was typed in the official times box and not put in goes: these replace it.
            tbResultsLink = new TextBox { Dock = DockStyle.Fill };
            tbResultsLink.TextChanged += (s, e) => CommitRaceInfo(false);
            Button btnResults = new SwimNoFocusButton { Text = SwimLang.T("🔗 Επίσημα αποτελέσματα"), AutoSize = true, Height = 26 };
            btnResults.Click += (s, e) =>
            {
                officialEditRace = null;
                officialError = null;
                manager.LinkOfficialResults(tbResultsLink.Text);
            };
            string resultsHelp = SwimLang.T("Βρίσκει τον αγώνα στα επίσημα αποτελέσματα: της ΚΟΕ (live.koe.org.gr), μιας σελίδας αποτελεσμάτων της ") +
                SwimLang.T("Microplus (results.microplustimingservices.com) ή μιας διοργάνωσης της World Aquatics (worldaquatics.com/competitions/…). Τη ") +
                SwimLang.T("διοργάνωση από τον σύνδεσμο που επικολλάς εδώ (ή, αν τον αφήσεις κενό, από ") +
                SwimLang.T("την ημερομηνία και το όνομα της διοργάνωσης στα live της ΚΟΕ), το αγώνισμα από το στυλ και την απόσταση, τη φάση από τη φάση ") +
                SwimLang.T("και την ημέρα, τον αθλητή από το μητρώο του (αλλιώς από το όνομα ή τον διάδρομο).\nΠαίρνει τους επίσημους ενδιάμεσους, τον ") +
                SwimLang.T("τελικό χρόνο, τον χρόνο αντίδρασης όπου δημοσιεύεται και τον διάδρομο, και τον αθλητή από το μητρώο· πριν μπουν, σου δείχνει τι βρήκε.");
            toolTip.SetToolTip(tbResultsLink, resultsHelp);
            toolTip.SetToolTip(btnResults, resultsHelp);

            // Results the program cannot read (since 2.8.1: a site with locked results, as the European Aquatics one): the swimmer's
            // row pasted. The window opens by itself when the link is not read.
            Button btnPaste = new SwimNoFocusButton { Text = SwimLang.T("📋 Επικόλληση"), AutoSize = true, Height = 26 };
            btnPaste.Click += (s, e) =>
            {
                officialEditRace = null;
                officialError = null;
                manager.PasteOfficialResults();
            };
            toolTip.SetToolTip(btnPaste, SwimLang.T("Για αποτελέσματα που δεν διαβάζονται αυτόματα (π.χ. European Aquatics): στη σελίδα τους μαρκάρισε ") +
                SwimLang.T("τη γραμμή του αθλητή (και τους ενδιάμεσους), Ctrl+C, και επικόλλησέ τη εδώ. Ανοίγει και μόνο του όταν ο σύνδεσμος δεν διαβάζεται."));
            FlowLayoutPanel resultsButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            resultsButtons.Controls.Add(btnResults);
            resultsButtons.Controls.Add(btnPaste);
            AddRow(table, SwimLang.T("Αποτελέσματα (σύνδεσμος)"), tbResultsLink);
            AddRow(table, "", resultsButtons);

            AddRow(table, SwimLang.T("Σημεία ελέγχου"), tbCheckpoints);
            AddRow(table, "", btnDefaults);

            Label note = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 70,
                ForeColor = SwimTheme.Dim,
                Text = SwimLang.T("Τα σημεία ελέγχου είναι οι αποστάσεις όπου πατάς «Πέρασμα», με τη σειρά (σε μονάδες της πισίνας: μ. ή yd). Με την αλλαγή απόστασης ή πισίνας μπαίνουν τα προεπιλεγμένα (όπως στο Excel: 15/25/35/45/50 ανά μήκος έως 200μ., 15/25/45/50 στα 400μ., 15/45/50 στα 800/1500μ., 15/20/25 ανά μήκος στις πισίνες 25μ. και 25yd).")
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
            SwimPage page = new SwimPage(SwimLang.T("Σύνδεση"));
            page.Padding = new Padding(8);
            TableLayoutPanel table = NewTable();

            tbUrl = new TextBox();
            tbToken = new TextBox();
            cmbRounding = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbRounding.Items.AddRange(new object[] { SwimLang.T("Μισές χεριές (0.5) – όπως στο Excel"), SwimLang.T("Ακέραιες χεριές"), SwimLang.T("Ακριβές (με δεκαδικά)") });
            chkAutoSend = new CheckBox { Text = SwimLang.T("Αποστολή στο Google Sheet σε κάθε πάτημα"), AutoSize = true };
            Button btnSave = new SwimNoFocusButton { Text = SwimLang.T("Αποθήκευση"), AutoSize = true, Height = 28, Primary = true };
            btnSave.Click += (s, e) => SaveConnection();
            Button btnTest = new SwimNoFocusButton { Text = SwimLang.T("Δοκιμή σύνδεσης"), AutoSize = true, Height = 28 };
            btnTest.Click += (s, e) => TestConnection();
            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            buttons.Controls.Add(btnSave);
            buttons.Controls.Add(btnTest);
            lblConnection = new Label { AutoSize = false, Height = 60, Dock = DockStyle.Fill };

            AddRow(table, SwimLang.T("Διεύθυνση Web App (…/exec)"), tbUrl);
            AddRow(table, "Token", tbToken);
            AddRow(table, SwimLang.T("Χεριές στα σημεία ελέγχου"), cmbRounding);
            AddRow(table, "", chkAutoSend);
            AddRow(table, "", buttons);
            AddRow(table, "", lblConnection);

            Label help = new Label
            {
                AutoSize = false,
                Height = 150,
                Dock = DockStyle.Fill,
                ForeColor = SwimTheme.Dim,
                Text = SwimLang.T("Ρύθμιση μία φορά:\n") +
                       SwimLang.T("1. Στο Google Sheet: Επεκτάσεις → Apps Script, επικόλληση του SwimRaceAnalysis.gs, αποθήκευση.\n") +
                       SwimLang.T("2. Στο φύλλο: μενού Swim Analysis → 1. Αρχική ρύθμιση (δίνει το token).\n") +
                       SwimLang.T("3. Apps Script: Ανάπτυξη → Νέα ανάπτυξη → Εφαρμογή Ιστού, Εκτέλεση ως: Εγώ, Πρόσβαση: Οποιοσδήποτε.\n") +
                       SwimLang.T("4. Αντέγραψε εδώ τη διεύθυνση (…/exec) και το token, πάτησε Αποθήκευση και Δοκιμή σύνδεσης.")
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
                ShowStatus(SwimLang.T("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής."), true);
        }

        private Button PickButton(SwimPickTarget target, string tooltip)
        {
            Button b = new SwimNoFocusButton { Text = SwimLang.T("📍 Μέτρηση"), AutoSize = true, Height = 26, Margin = new Padding(6, 2, 2, 2) };
            b.Click += (s, e) => manager.BeginMeasure(target, SelectedTurnWall());
            toolTip.SetToolTip(b, tooltip + SwimLang.T(" (χρειάζεται βαθμονόμηση: δες κάτω)"));
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

        /// <summary>Unit of a distance of the pool: "m" or "yd" with the pool of the race (see RefreshAll).</summary>
        private Label DistanceUnit()
        {
            Label label = Unit("m");
            distanceUnits.Add(label);
            return label;
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
                    if (!tbResultsLink.Focused)
                        tbResultsLink.Text = r.ResultsLink ?? "";
                    tbCheckpoints.Text = SwimPresets.FormatCheckpoints(r.Checkpoints);
                }

                SwimSettings settings = manager.Settings;
                tbUrl.Text = settings.WebAppUrl;
                tbToken.Text = settings.Token;
                cmbRounding.SelectedIndex = settings.StrokeRounding >= 1 ? 1 : (settings.StrokeRounding > 0 ? 0 : 2);
                chkAutoSend.Checked = settings.AutoSend;
                chkGraphics.Checked = settings.VideoGraphics;
                chkHud.Checked = settings.HudLayout;
                chkSpeedChart.Checked = settings.ShowSpeedChart;
                chkSplitBoard.Checked = settings.ShowSplitBoard;
                LoadFinalsOverlayOptions(settings);
                LoadCompareOptions(settings);
                chkOverlayExports.Checked = settings.OverlayInExports;
                chkVideoBars.Checked = settings.VideoBars;
                chkAutoStrokes.Checked = settings.AutoStrokes;
                chkLearnMarks.Checked = settings.LearnFromMarks;
                chkReel.Checked = settings.ReelOverlay;
                chkReelSummary.Checked = settings.ReelSummary;
                cmbCorner.SelectedIndex = Math.Max(0, Array.IndexOf(corners, settings.OverlayCorner));
            }
            finally
            {
                updating = false;
            }

            if (manager.Race != null)
                SetTechnicalView(manager.Race.IsTraining || manager.TechnicalView, false);

            // Another race or video: the technical pages follow the video again.
            ResetTechSelection();

            RefreshManualFields();
            RefreshButtons();
            RefreshAthletes();
            RefreshReferences();
            RefreshPb();
            RefreshData();
            RefreshSync();
            RefreshCalibration();
            RefreshPick();
            RefreshPilot();
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
                cmbPb.Items.Add(new SwimAnalysisItem { Id = "auto", Label = SwimLang.T("Αυτόματα: ο καλύτερος προηγούμενος χρόνος") });
                cmbPb.Items.Add(new SwimAnalysisItem { Id = "", Label = SwimLang.T("Χωρίς σύγκριση με προηγούμενο αγώνα") });
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
                // The official times: as typed while the analyst types them for this race (and could not be put in), else the race's.
                if (officialEditRace != r)
                {
                    officialEditRace = null;
                    officialError = null;
                    string official = SwimRace.FormatOfficial(r.Official);
                    if (tbOfficial.Text != official)
                        tbOfficial.Text = official;
                }
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

            foreach (Label unit in distanceUnits)
                unit.Text = race.IsYards ? "yd" : "m";

            RefreshAutoStrokes();

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
                gr.Cells["t"].ToolTipText = row.Official
                    ? SwimLang.T("Ο επίσημος χρόνος των αποτελεσμάτων") + (row.MarkT.HasValue ? (row.MarkApart ? SwimLang.T(" · ⚠ το σημάδι στο βίντεο είναι στα ") : SwimLang.T(" (το σημάδι στο ")) +
                        FormatRaceTime(row.MarkT.Value) + (row.MarkApart ? SwimLang.T(": η ανάλυση κρατά τον επίσημο· έλεγξε το καρέ του σημαδιού ή την εκκίνηση.") : ").") :
                        SwimLang.T(" (δεν έχει μαρκαριστεί ακόμη)."))
                    : "";
                gr.Cells["seg"].Value = Format(row.SegTime);
                gr.Cells["vel"].Value = Format(row.Vel);
                gr.Cells["vel"].Style.BackColor = row.IsFree ? Color.Empty : colorPhase;
                gr.Cells["vel"].ToolTipText = row.VelYd.HasValue ? row.VelYd.Value.ToString("0.00", CultureInfo.InvariantCulture) + " yd/s" : "";
                gr.Cells["strokes"].Value = FormatStrokes(row.Strokes) + (row.StrokesTyped && row.Strokes.HasValue ? " ✓" : "");
                gr.Cells["strokes"].ToolTipText = row.StrokesTyped
                    ? SwimLang.T("Επιβεβαιωμένο ή γραμμένο από εσένα. Διπλό κλικ για αλλαγή.")
                    : SwimLang.T("Από τα σημάδια χεριών. Διπλό κλικ για να το επιβεβαιώσεις ή να το διορθώσεις.");
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
            tileDak.Set(SwimLang.T("ΔΑΚ"), Format(computed.AvgDak), SwimLang.T("μέσος όρος 50άρι"), SwimTheme.Amber, true);
            tileFinal.Set(SwimLang.T("Τελικός χρόνος"), computed.FinalTime.HasValue ? SwimReference.FormatTime(computed.FinalTime.Value) : "", "", SwimTheme.Text, false);
            tileVel.Set(SwimLang.T("Μέση ταχύτητα"), Format(computed.AvgVel), race.IsYards && computed.AvgVel.HasValue ? (computed.AvgVel.Value / SwimRace.Yard).ToString("0.00", CultureInfo.InvariantCulture) + " yd/s" : "m/s", SwimTheme.Text, false);
            tileSr.Set(SwimLang.T("Συχνότητα"), Format(computed.AvgSr, "0.0"), SwimLang.T("κύκλοι/λεπτό"), SwimTheme.Accent, false);
            tileDps.Set(SwimLang.T("Μήκος χεριάς"), Format(computed.AvgDps), SwimLang.T("m/χεριά"), SwimTheme.Accent, false);
            tileStart.Set(SwimLang.T("Εκκίνηση 0-15") + u, Format(computed.Start15), "s", SwimTheme.Text, false);
            double turns = computed.Turns.Where(t => t.InOut.HasValue).Sum(t => t.InOut.Value);
            bool anyTurn = computed.Turns.Any(t => t.InOut.HasValue);
            tileTurns.Set(SwimLang.T("Στροφές −5/+15"), anyTurn ? turns.ToString("0.00", CultureInfo.InvariantCulture) : "",
                anyTurn && computed.FinalTime.HasValue && computed.FinalTime.Value > 0 ? (turns / computed.FinalTime.Value * 100).ToString("0.0", CultureInfo.InvariantCulture) + SwimLang.T("% του χρόνου") : "s", SwimTheme.Text, false);

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
                gr.Cells["dak"].ToolTipText = b.DakYd.HasValue ? SwimLang.T("ανά 10yd: ") + b.DakYd.Value.ToString("0.00", CultureInfo.InvariantCulture) : "";
                gr.Cells["sr"].Value = Format(b.Sr, "0.0");
                gr.Cells["dps"].Value = Format(b.Dps);
                gr.Cells["vel"].Value = Format(b.Vel);
            }
        }

        private void RefreshPhases(SwimRace race, SwimComputed computed)
        {
            // Start, in the order of the events.
            lblStartSignal.Text = !race.StartTimestamp.HasValue ? SwimLang.T("δεν έχει οριστεί") : race.PilotStart ? SwimLang.T("✈ t = 0 · έλεγξέ τη") : "✔ t = 0";
            lblStartSignal.ForeColor = race.StartTimestamp.HasValue && !race.PilotStart ? colorOk : colorWarn;
            lblReactionAuto.Text = Seconds(computed.ReactionTime);
            lblBlock.Text = Seconds(computed.BlockTime) + (computed.BlockOfficial ? SwimLang.T(" (επίσημος χρόνος αντίδρασης)") : "");
            lblEntry.Text = Seconds(computed.EntryTime) + (computed.FlightTime.HasValue ? SwimLang.T(" · πτήση ") + Seconds(computed.FlightTime) : "");
            lblStartKicks.Text = computed.StartKicks.HasValue ? computed.StartKicks.Value.ToString(CultureInfo.InvariantCulture) : "–";
            lblBreakout.Text = Seconds(computed.StartUnderwaterTime);

            SwimRow first = computed.Rows.FirstOrDefault();
            lblFirstSplitTitle.Text = first != null ? SwimLang.T("Πέρασμα ") + first.D + race.Unit : SwimLang.T("Πρώτο πέρασμα");
            lblFirstSplit.Text = first != null ? Seconds(first.T) : "–";

            // Finish.
            lblFinishGap.Text = computed.FinishLastStrokeGap.HasValue ? SwimLang.T("τελ. χεριά → τοίχος ") + Seconds(computed.FinishLastStrokeGap) : "–";
            SwimRow finalRow = computed.Rows.LastOrDefault();
            lblFinalTime.Text = computed.FinalTime.HasValue ? SwimLang.T("τελικός χρόνος ") + FormatRaceTime(computed.FinalTime.Value) +
                (finalRow != null && finalRow.Official ? SwimLang.T(" (επίσημος)") : "") : "–";
            RefreshFinalFrame();
            RefreshOfficial(race, computed);
            string finalDelta = finalRow != null && finalRow.Delta.HasValue && computed.Reference != null
                ? string.Format(SwimLang.T(" · Δ τελικού {0} από «{1}»"), SwimReference.FormatDelta(finalRow.Delta.Value), computed.Reference.Name)
                : "";
            string clean = computed.AvgSr.HasValue
                ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("\nΚαθαρή κολύμβηση: SR {0:0.0} κύκλοι/λεπτό · DPS {1:0.00} m/χεριά · SL {2:0.00} m/κύκλο"),
                    computed.AvgSr.Value, computed.AvgDps ?? 0, computed.AvgSl ?? 0)
                : "";
            lblFinishSummary.Text = string.Format(CultureInfo.InvariantCulture,
                SwimLang.T("Συνολικές χεριές (πατήματα): {0}{1}{2}"),
                computed.TotalPresses,
                finalDelta,
                clean);

            // Data checks.
            string checks = computed.Warnings.Count == 0
                ? (computed.Rows.Any(r => r.T.HasValue) ? SwimLang.T("✔ Δεν βρέθηκε κάποιο πρόβλημα στα σημάδια.") : "")
                : string.Join(Environment.NewLine, computed.Warnings.Select(w => "⚠ " + w).ToArray());
            if (tbChecks.Text != checks)
                tbChecks.Text = checks;
            tbChecks.ForeColor = computed.Warnings.Count > 0 ? colorWarn : colorOk;

            lnkChecks.Visible = computed.Warnings.Count > 0;
            if (computed.Warnings.Count > 0)
            {
                string link = string.Format(CultureInfo.InvariantCulture, "⚠ {0}: {1}",
                    computed.Warnings.Count == 1 ? SwimLang.T("1 έλεγχος") : computed.Warnings.Count + SwimLang.T(" έλεγχοι"),
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
                    gr.Cells["wall"].Value = t.Exchange ? t.Wall.ToString(CultureInfo.InvariantCulture) + SwimLang.T(" αλλαγή") : t.Wall.ToString(CultureInfo.InvariantCulture);
                    gr.Cells["uwDist"].Value = manual != null ? Format(manual.UnderwaterDist) : "";
                    gr.Cells["turnTime"].Value = manual != null && manual.TurnTime.HasValue ? Format(manual.TurnTime) : Format(t.TurnTime);
                    gr.Cells["uwTime"].Value = Format(t.UnderwaterTime);
                    gr.Cells["kicks"].Value = t.Kicks.HasValue ? t.Kicks.Value.ToString(CultureInfo.InvariantCulture) : "";
                    gr.Cells["gap"].Value = Format(t.LastStrokeGap);
                }
            }

            currentTurnWall = -1;
            RefreshTurnLabels();
            RefreshFinals();
            RefreshQuiet();
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
                lblTurnCurrent.Text = SwimLang.T("Αυτός ο αγώνας δεν έχει στροφές.");
                wall = 0;
            }
            else if (t == null)
            {
                lblTurnCurrent.Text = SwimLang.T("Στροφή: –");
            }
            else
            {
                lblTurnCurrent.Text = string.Format(SwimLang.T("{0} στα {1}{2}{3}"), t.Exchange ? SwimLang.T("Αλλαγή") : SwimLang.T("Στροφή"), wall, race.Unit, manager.CurrentTurnWall == wall ? SwimLang.T(" (η θέση του βίντεο)") : SwimLang.T(" (επιλεγμένη)"));
            }

            SwimTurnManual manual = wall > 0 ? race.GetTurn(wall, false) : null;
            SwimRow wallRow = computed.Rows.FirstOrDefault(r => r.D == wall);
            lblTurnGap.Text = t != null && t.LastStrokeGap.HasValue ? SwimLang.T("→ τοίχος ") + Seconds(t.LastStrokeGap) : "–";
            lblTurnWall.Text = wallRow != null && wallRow.T.HasValue ? wall + race.Unit + SwimLang.T(" στα ") + FormatRaceTime(wallRow.T.Value) : "–";
            double? turnTime = manual != null && manual.TurnTime.HasValue ? manual.TurnTime : (t != null ? t.TurnTime : null);
            if (turnTime.HasValue)
                lblTurnTime.Text = (t != null && t.Exchange ? SwimLang.T("χρόνος αλλαγής ") : (t != null && t.Flip ? SwimLang.T("χρόνος στροφής (από την τελευταία χεριά) ") : SwimLang.T("χρόνος στροφής "))) + Seconds(turnTime);
            else if (t != null && t.Flip && t.Pushoff.HasValue)
                lblTurnTime.Text = SwimLang.T("χρόνος στροφής: λείπει η τελευταία χεριά πριν τον τοίχο (C)");
            else
                lblTurnTime.Text = "–";
            lblTurnKicks.Text = t != null && t.Kicks.HasValue ? t.Kicks.Value.ToString(CultureInfo.InvariantCulture) : "–";
            string uwTime = t != null && t.UnderwaterTime.HasValue ? SwimLang.T("υποβρύχια ") + Seconds(t.UnderwaterTime) : "–";
            string uwDist = manual != null && manual.UnderwaterDist.HasValue ? " · " + Format(manual.UnderwaterDist) + " " + race.Unit : "";
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
                chips.Add(new SwimChip(SwimLang.T("ΕΠΟΜΕΝΟ"), next > 0 ? next + race.Unit : SwimLang.T("τέλος"), SwimTheme.Amber));
                chips.Add(new SwimChip(SwimLang.T("ΧΕΡΙΕΣ"), SwimCalculator.PressesSinceLastSplit(manager.Computed).ToString(CultureInfo.InvariantCulture), SwimTheme.Text));
                if (now.HasValue && manager.Computed.Reference != null)
                {
                    SwimRow last = manager.Computed.Rows.Where(r => r.Delta.HasValue && r.PassedAt(now.Value)).OrderBy(r => r.OnVideo).LastOrDefault();
                    if (last != null)
                        chips.Add(new SwimChip(SwimLang.T("Δ ") + last.D + race.Unit, SwimReference.FormatDelta(last.Delta.Value), last.Delta.Value > 0.0049 ? SwimTheme.Red : SwimTheme.Green));
                }

                if (now.HasValue)
                {
                    SwimBlock block = manager.Computed.Blocks.Where(b => b.Dak.HasValue && manager.Computed.Rows.Any(r => r.D == b.To && r.PassedAt(now.Value)))
                        .OrderBy(b => b.To).LastOrDefault();
                    if (block != null)
                        chips.Add(new SwimChip(SwimLang.T("ΔΑΚ ") + block.From + "-" + block.To, block.Dak.Value.ToString("0.00", CultureInfo.InvariantCulture), SwimTheme.Amber));
                }

                chips.Add(new SwimChip(SwimLang.T("ΦΑΣΗ"), PhaseName(manager.CurrentPhase), SwimTheme.Accent));
                if (!race.StartTimestamp.HasValue)
                    chips.Add(new SwimChip(SwimLang.T("ΣΗΜΑ"), SwimLang.T("δεν έχει οριστεί"), SwimTheme.Warn));
                lblLive.SetValues(SwimLang.T("ΧΡΟΝΟΣ ΑΓΩΝΑ"), time, chips);
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
                case SwimPhase.Start: return SwimLang.T("Εκκίνηση");
                case SwimPhase.Swim: return SwimLang.T("Κολύμβηση");
                case SwimPhase.Turn: return SwimLang.T("Στροφή ") + manager.CurrentTurnWall + (manager.Race != null ? manager.Race.Unit : "");
                case SwimPhase.Finish: return SwimLang.T("Τερματισμός");
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
            btnCalibrate.Text = calibrating ? SwimLang.T("Ακύρωση βαθμονόμησης") : SwimLang.T("Βαθμονόμηση (4 κλικ)");
            Highlight(btnCalibrate, calibrating);
            if (btnAutoStrokes != null)
            {
                bool strokes = manager.PickingStrokes && !manager.PickingTrack;
                btnAutoStrokes.Text = strokes ? SwimLang.T("Ακύρωση επιλογής") : SwimLang.T("Επιλογή αθλητή");
                Highlight(btnAutoStrokes, strokes);
            }
            RefreshReel();
            RefreshTechPick(active);
            RefreshSkeleton();
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
                info = (count > 0 ? string.Format(SwimLang.T("{0} για {1}. "), count, ev) : string.Format(SwimLang.T("Καμία αναφορά για {0} στο φύλλο «{1}». "), ev, SwimAnalysisManager.ReferenceSheetName)) + info;
            }

            lblReferences.Text = info;
            toolTip.SetToolTip(lblReferences, info);
            btnSaveReference.Enabled = race != null;
            RefreshCompareHint();
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

            string name = AskText(SwimLang.T("Νέα elite αναφορά"),
                SwimLang.T("Όνομα της αναφοράς, όπως θα φαίνεται στη λίστα σύγκρισης (π.χ. «Tester – EJC 2025 Final»):"),
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
                Button ok = new SwimButton(true) { Text = "OK", DialogResult = DialogResult.OK, Left = 272, Top = 86, Width = 75, Height = 28, Primary = true };
                Button cancel = new SwimButton(true) { Text = SwimLang.T("Άκυρο"), DialogResult = DialogResult.Cancel, Left = 353, Top = 86, Width = 75, Height = 28 };
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
            CommitFinalsOverlayOptions(s);
            CommitCompareOptions(s);
            s.OverlayInExports = chkOverlayExports.Checked;
            if (cmbCorner.SelectedIndex >= 0)
                s.OverlayCorner = corners[cmbCorner.SelectedIndex];
            s.Save();
            manager.RefreshOverlay();
            RefreshCompareHint();
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
                List<string> parts = new List<string> { "✔ " + r.Athlete, SwimLang.T("μητρώο ") + r.AthleteId };
                if (!string.IsNullOrEmpty(r.AthleteClub)) parts.Add(r.AthleteClub);
                if (!string.IsNullOrEmpty(r.AthleteCategory)) parts.Add(r.AthleteCategory);
                if (!string.IsNullOrEmpty(r.AthleteBirthYear)) parts.Add(r.AthleteBirthYear);
                lblAthlete.ForeColor = SwimTheme.Accent;
                lblAthlete.Text = string.Join(" · ", parts.ToArray());
            }
            else
            {
                lblAthlete.ForeColor = colorWarn;
                lblAthlete.Text = SwimLang.T("Δεν έχει επιλεγεί αθλητής από τη λίστα: η ανάλυση δεν θα συνδεθεί με μητρώο.");
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
                SwimLang.T("Διαγραφή της ανάλυσης \"") + manager.Race.DisplayName + SwimLang.T("\" από αυτό το βίντεο;\nΤο φύλλο στο Google Sheet δεν διαγράφεται."),
                SwimLang.T("Διαγραφή ανάλυσης"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes)
                manager.DeleteRace();
        }

        private void CommitRaceInfo(bool structureChanged)
        {
            if (updating || manager.Race == null)
                return;

            SwimRace r = manager.Race;
            string oldStyle = r.Style;
            r.Athlete = tbAthlete.Text.Trim();
            r.Lane = (int)nudLane.Value;
            if (cmbStyle.SelectedIndex >= 0)
                r.Style = SwimPresets.Styles[cmbStyle.SelectedIndex];
            r.Competition = tbCompetition.Text.Trim();
            r.Location = tbLocation.Text.Trim();
            r.Round = cmbRound.Text.Trim();
            r.Date = tbDate.Text.Trim();
            if (tbResultsLink != null)
                r.ResultsLink = tbResultsLink.Text.Trim();
            manager.RaceInfoChanged(structureChanged);

            // The previous races of the PB are those of the event.
            if (!structureChanged && r.Style != oldStyle)
                manager.RefreshPbCandidates();
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
                    SwimLang.T("Η ανάλυση έχει περάσματα αγώνα. Σε βίντεο προπόνησης δεν χρησιμοποιούνται (μένουν αποθηκευμένα αν το γυρίσεις πίσω σε αγώνα). Συνέχεια;"),
                    SwimLang.T("Είδος βίντεο"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
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
            RefreshPb();
            if (!training)
                manager.RefreshPbCandidates();
            ShowStatus(training
                ? SwimLang.T("Βίντεο προπόνησης: G σήμα (νέα εκκίνηση), W επαφή στον τοίχο (νέα στροφή), T για 5/10/15, H/J χέρια/πίσω πόδι, O αρχή περιστροφής, P στιγμιότυπο, A γωνία.")
                : SwimLang.T("Βίντεο αγώνα: race analysis και τεχνική ανάλυση της εκκίνησης και των στροφών."), false);
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
            CommitCheckpoints(false);
        }

        /// <summary>After another race or video the start and turn pages follow the video again.</summary>
        private void ResetTechSelection()
        {
            selectedStartId = null;
            selectedTurnId = null;
            videoStartId = null;
            videoTurnId = null;
        }

        private void CommitCheckpoints(bool keepMarked)
        {
            if (updating || manager.Race == null || IsDisposed)
                return;

            List<int> parsed = SwimPresets.ParseCheckpoints(tbCheckpoints.Text);
            SwimRace r = manager.Race;
            if (parsed.Count == 0 || parsed.SequenceEqual(r.Checkpoints))
            {
                if (!keepMarked)
                    tbCheckpoints.Text = SwimPresets.FormatCheckpoints(r.Checkpoints);
                return;
            }

            if (keepMarked && r.Marks.Any(m => m.Type == SwimMarkType.Split && !parsed.Contains(m.Distance)))
                return;

            if (parsed[parsed.Count - 1] != r.Distance)
                ShowStatus(SwimLang.T("Προσοχή: το τελευταίο σημείο ελέγχου δεν είναι ίσο με την απόσταση του αγώνα (") + r.Distance + r.Unit + ").", true);

            r.Checkpoints = parsed;
            tbCheckpoints.Text = SwimPresets.FormatCheckpoints(parsed);
            manager.RaceInfoChanged(true);
        }

        /// <summary>
        /// The official times typed (Enter or leaving the box): read and checked against the race, then the manager puts them in the
        /// analysis (and moves the pilot's marks not checked yet to them).
        /// </summary>
        private void CommitOfficial()
        {
            // Only what was typed for this race (another one selected meanwhile: dropped, see RefreshManualFields). Not while a reading
            // of the video runs (it reads the marks and the frames): it waits, typed, for Enter or the next click after it.
            SwimRace r = manager.Race;
            if (updating || r == null || officialEditRace == null || manager.ReadingBusy)
                return;
            if (officialEditRace != r)
            {
                officialEditRace = null;
                RefreshManualFields();
                return;
            }

            string error;
            List<double> official = SwimRace.ParseOfficial(tbOfficial.Text, out error);
            if (error == null)
                error = SwimRace.OfficialProblem(official, r.Distance, r.Pool, r.K, r.Unit);
            if (error != null)
            {
                // Shown until corrected (the typed text stays); the same again is not said again.
                bool again = error == officialError;
                officialError = error;
                lblOfficial.Text = "⚠ " + error;
                lblOfficial.ForeColor = colorWarn;
                if (!again)
                    ShowStatus(SwimLang.T("Επίσημος χρόνος: ") + error + ".", true);
                return;
            }

            officialEditRace = null;
            officialError = null;
            if (SwimRace.FormatOfficial(official) == SwimRace.FormatOfficial(r.Official))
            {
                RefreshManualFields();
                RefreshOfficial(r, manager.Computed);
                return;
            }

            manager.OfficialChanged(official);
        }

        /// <summary>
        /// What is being typed in the window counts before a button of the panel acts (they never take the focus, so the box does not
        /// lose it): the official times.
        /// </summary>
        internal void CommitTyping()
        {
            CommitOfficial();
        }

        /// <summary>
        /// «Αναίρεση» while official times were being typed: what was typed goes (the box shows the race's own again). False: nothing
        /// was being typed.
        /// </summary>
        private bool DropTypedOfficial()
        {
            if (officialEditRace == null)
                return false;
            officialEditRace = null;
            officialError = null;
            RefreshManualFields();
            if (manager.Race != null)
                RefreshOfficial(manager.Race, manager.Computed);
            ShowStatus(SwimLang.T("Αναιρέθηκε ό,τι έγραφες στον επίσημο χρόνο (δεν μπήκε στην ανάλυση)."), false);
            return true;
        }

        /// <summary>
        /// Official times typed for this race that could not be put in: the reading waits for them to be corrected (it would place the
        /// walls without them). Says so.
        /// </summary>
        internal bool OfficialTypedWrong()
        {
            if (officialEditRace == null || officialEditRace != manager.Race || officialError == null)
                return false;

            tabs.SelectedTab = tabFinish;
            tbOfficial.Focus();
            ShowStatus(SwimLang.T("Διόρθωσε πρώτα τον επίσημο χρόνο (ή σβήσ' τον): ") + officialError + ".", true);
            return true;
        }

        /// <summary>What the official times give in the analysis: the final time and the splits typed, and a mark elsewhere.</summary>
        private void RefreshOfficial(SwimRace race, SwimComputed computed)
        {
            if (race == null || computed == null)
                return;

            // Typed and not put in (it could not be read): the warning stays until it is corrected.
            if (officialEditRace == race && officialError != null)
            {
                lblOfficial.Text = "⚠ " + officialError;
                lblOfficial.ForeColor = colorWarn;
                return;
            }

            Dictionary<int, double> official = race.OfficialTimes();
            if (official.Count == 0)
            {
                // Kept but not fitting the race any more (its distance or pool changed): said, not hidden.
                string problem = race.Official == null ? null : SwimRace.OfficialProblem(race.Official, race.Distance, race.Pool, race.K, race.Unit);
                lblOfficial.Text = problem != null ? SwimLang.T("⚠ δεν μπαίνουν στην ανάλυση: ") + problem : "–";
                lblOfficial.ForeColor = problem != null ? colorWarn : colorValue;
                return;
            }

            if (!race.StartTimestamp.HasValue)
            {
                lblOfficial.Text = SwimLang.T("μπαίνουν στην ανάλυση όταν οριστεί η εκκίνηση");
                lblOfficial.ForeColor = colorValue;
                return;
            }

            List<string> apart = new List<string>();
            // (2.9.5b) The official times stand; a mark farther from its official time than a frame and a half is named (its frame, or the
            // start, is off: the speeds next to it mix the two).
            foreach (SwimRow row in computed.Rows.Where(x => x.MarkApart && x.MarkT.HasValue && x.OfficialTime.HasValue))
                apart.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}{1}: σημάδι {2} (Δ {3} s)"), row.D, race.Unit,
                    FormatRaceTime(row.MarkT.Value), SwimReference.FormatDelta(row.MarkT.Value - row.OfficialTime.Value)));
            if (computed.BlockMarkApart && computed.BlockMarkTime.HasValue && race.OfficialReaction.HasValue)
                apart.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("αποκόλληση: σημάδι {0} (Δ {1} s)"),
                    computed.BlockMarkTime.Value.ToString("0.00", CultureInfo.InvariantCulture), SwimReference.FormatDelta(computed.BlockMarkTime.Value - race.OfficialReaction.Value)));
            if (apart.Count > 0)
                apart.Add(SwimLang.T("η ανάλυση κρατά τους επίσημους· έλεγξε τα καρέ ή την εκκίνηση"));

            // How the times typed are read: each at its distance (the first ones, the final one).
            List<string> at = official.Where(o => o.Key != race.Distance).OrderBy(o => o.Key)
                .Select(o => o.Key.ToString(CultureInfo.InvariantCulture) + race.Unit + " " + SwimReference.FormatTime(o.Value)).ToList();
            if (at.Count > 3)
                at = new List<string> { at[0], at[1], "…" };
            double final;
            string text = (at.Count > 0 ? string.Join(" · ", at.ToArray()) + " · " : "") +
                (official.TryGetValue(race.Distance, out final) ? SwimLang.T("τελικός ") + SwimReference.FormatTime(final) : "");
            string warning = SwimRace.OfficialWarning(race.Official, race.Distance, race.K, race.Unit);
            if (warning != null)
                apart.Add(warning);
            lblOfficial.Text = apart.Count == 0 ? "✔ " + text + SwimLang.T(" στην ανάλυση") : text + " · ⚠ " + string.Join(" · ", apart.ToArray());
            lblOfficial.ForeColor = apart.Count == 0 ? colorOk : colorWarn;
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

            // Esc, or Enter without a change, does not turn the computed value into a manual one;
            // another race selected during the edit does not receive it either.
            string text = Convert.ToString(row.Cells[e.ColumnIndex].Value, CultureInfo.InvariantCulture);
            SwimRace editRace = turnEditRace;
            string before = turnEditText;
            turnEditRace = null;
            turnEditText = null;
            if (editRace != manager.Race || text == before)
                return;

            int wall = (int)row.Tag;
            string column = gridTurns.Columns[e.ColumnIndex].Name;
            double? value = ParseText(text);
            if (value == null && !string.IsNullOrWhiteSpace(text))
            {
                // Not a number (e.g. a hotkey typed in the cell): the value stays as it was.
                BeginInvoke((Action)(() => RefreshData()));
                return;
            }

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

            if (e.ColumnIndex >= 0 && grid.Columns[e.ColumnIndex].Name == "strokes")
            {
                manager.EditSegmentStrokes((int)grid.Rows[e.RowIndex].Tag);
                return;
            }

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
            lblConnection.Text = SwimLang.T("Οι ρυθμίσεις αποθηκεύτηκαν.");
        }

        private void TestConnection()
        {
            SaveConnection();
            lblConnection.ForeColor = colorBusy;
            lblConnection.Text = SwimLang.T("Δοκιμή…");
            manager.TestConnection(result =>
            {
                if (IsDisposed)
                    return;

                if (result.Ok)
                {
                    // The version the web app runs (the code saved in Apps Script runs only after "New version" of the deployment).
                    string version = result.Raw != null && result.Raw["version"] != null ? (string)result.Raw["version"] : null;
                    Version backend;
                    bool old = !string.IsNullOrEmpty(version) && Version.TryParse(version, out backend) && backend < SwimAnalysisManager.RequiredBackend;
                    lblConnection.ForeColor = old ? colorWarn : colorOk;
                    lblConnection.Text = SwimLang.T("✔ Συνδέθηκε με το Google Sheet \"") + result.SpreadsheetName + "\"" + (string.IsNullOrEmpty(version) ? "." :
                        " · Apps Script " + version + (old ? SwimLang.T(": χρειάζεται η ") + SwimAnalysisManager.RequiredBackend.ToString(3) +
                        SwimLang.T(" (Ανάπτυξη → Διαχείριση αναπτύξεων → ✏️ → Έκδοση: Νέα έκδοση → Ανάπτυξη).") : "."));
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

            Keys code = keyData & Keys.KeyCode;
            bool plain = (keyData & (Keys.Control | Keys.Alt)) == 0;

            // An open list, an editable grid cell, the rows of a grid or a list keep their own keys.
            if (!typing && KeepsKey(focused, keyData, code, plain))
                return base.ProcessCmdKey(ref msg, keyData);

            if (!typing || !plain || (code >= Keys.F1 && code <= Keys.F24))
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

            // A control of a page hidden meanwhile (the panel followed the race) no longer has the focus.
            return c != null && c.ContainsFocus ? c : null;
        }

        private bool IsTextInput(Control c)
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

            // The athletes list is searched by typing; the other lists only take the arrows (see KeepsKey).
            return c == lbAthletes;
        }

        /// <summary>
        /// Keys that stay with the control that has the focus instead of going to the video:
        /// an open drop-down list, typing in an editable grid cell, Delete and Ctrl+C in a grid, the arrows of a list.
        /// </summary>
        private bool KeepsKey(Control focused, Keys keyData, Keys code, bool plain)
        {
            if (focused == null)
                return false;

            ComboBox combo = focused as ComboBox;
            if (combo != null && combo.DroppedDown)
                return true;

            DataGridView dgv = focused as DataGridView;
            if (dgv != null)
            {
                if (plain && code == Keys.Delete)
                    return true;
                if (keyData == (Keys.Control | Keys.C))
                    return true;

                // Typing in an editable cell starts editing it (Excel style): a number in the cells of numbers
                // (the letters stay hotkeys there), any text in the names of the angles.
                DataGridViewCell cell = dgv.CurrentCell;
                bool editable = cell != null && !cell.ReadOnly && !dgv.ReadOnly;
                if (!editable || !plain)
                    return false;

                bool number = (code >= Keys.D0 && code <= Keys.D9) || (code >= Keys.NumPad0 && code <= Keys.NumPad9) ||
                    code == Keys.Decimal || code == Keys.OemPeriod || code == Keys.Oemcomma || code == Keys.OemMinus || code == Keys.Subtract;
                bool letter = (code >= Keys.A && code <= Keys.Z) || (code >= Keys.OemSemicolon && code <= Keys.Oem102);
                return number || (dgv == gridAngles && letter);
            }

            if (focused is ListBox && plain)
                return code == Keys.Up || code == Keys.Down || code == Keys.PageUp || code == Keys.PageDown || code == Keys.Home || code == Keys.End;

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
                return Reference == null ? SwimLang.T("(χωρίς σύγκριση)") : Reference.Label;
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

        /// <summary>Automatic strokes of the race (swimming page).</summary>
        /// <summary>What the learning from the marks of the race is doing (start page).</summary>
        public void RefreshQuiet()
        {
            if (IsDisposed || lblLearn == null)
                return;

            string text = manager.QuietDescription;
            lblLearn.Text = string.IsNullOrEmpty(text) ? "–" : text;
            lblLearn.ForeColor = manager.QuietReadingRunning ? SwimTheme.Amber : SwimTheme.Dim;
        }

        public void RefreshAutoStrokes()
        {
            if (IsDisposed || lblAutoStrokes == null)
                return;

            int count = manager.AutoStrokeCount;
            string cycle = manager.LastAutoCycle > 0
                ? SwimLang.T(" · κύκλος ") + manager.LastAutoCycle.ToString("0.00", CultureInfo.InvariantCulture) + " s"
                : "";
            string text;
            Color color = SwimTheme.Dim;
            if (manager.AutoStrokesRunning)
            {
                text = SwimLang.T("ανάγνωση του βίντεο…");
                color = SwimTheme.Amber;
            }
            else if (manager.PickingStrokes && !manager.PickingTrack)
            {
                text = SwimLang.T("κλικ στο κεφάλι και στα πόδια του αθλητή");
                color = SwimTheme.Amber;
            }
            else if (manager.StrokeSessionActive)
            {
                text = string.Format(CultureInfo.InvariantCulture, SwimLang.T("μήκος μέχρι τα {0}{1}{2}"), manager.StrokeSessionWall, manager.Race != null ? manager.Race.Unit : "", cycle);
                color = SwimTheme.Green;
            }
            else if (!manager.Settings.AutoStrokes)
            {
                text = count > 0 ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} αυτόματες χεριές στο βίντεο"), count) : SwimLang.T("κλειστό");
            }
            else
            {
                text = SwimLang.T("μετά το B: κλικ στο κεφάλι και στα πόδια");
            }

            lblAutoStrokes.Text = text;
            lblAutoStrokes.ForeColor = color;
            btnAutoStrokes.Enabled = !manager.AutoStrokesRunning;
            RefreshReel();
            RefreshPilot();
        }

        /// <summary>The auto pilot (what it waits for) and the check of the proposals of the race, on the start page.</summary>
        public void RefreshPilot()
        {
            if (IsDisposed || btnPilot == null || btnRaceReview == null)
                return;

            string waiting = manager.PilotWaiting;
            btnPilot.Text = waiting.Length > 0 ? "✈ " + waiting : SwimLang.T("✈ Αυτόματος πιλότος");
            Highlight(btnPilot, waiting.Length > 0);
            btnPilot.Enabled = !manager.AutoStrokesRunning && !manager.StartLightRunning;

            int pending = manager.Race != null ? manager.Race.Proposals.Count : 0;
            int doubtful = manager.PilotDoubtful;
            btnRaceReview.Text = pending > 0
                ? SwimLang.T("▶ Έλεγχος ") + pending.ToString(CultureInfo.InvariantCulture) + (doubtful > 0 ? " · ⚠ " + doubtful.ToString(CultureInfo.InvariantCulture) : "")
                : SwimLang.T("▶ Έλεγχος");
            btnRaceReview.Enabled = pending > 0 && !manager.Reviewing;
        }

        /// <summary>Technical view of the race: on/off, the swimmer followed for the label.</summary>
        public void RefreshReel()
        {
            if (IsDisposed || lblTrack == null)
                return;

            bool picking = manager.PickingTrack;
            btnTrack.Text = picking ? SwimLang.T("Ακύρωση επιλογής") : SwimLang.T("Ακολούθηση αθλητή");
            Highlight(btnTrack, picking);
            btnTrack.Enabled = !manager.AutoStrokesRunning;

            string followed = manager.TrackDescription;
            if (manager.AutoStrokesRunning)
            {
                lblTrack.Text = SwimLang.T("ανάγνωση του βίντεο…");
                lblTrack.ForeColor = SwimTheme.Amber;
            }
            else if (picking)
            {
                lblTrack.Text = SwimLang.T("κλικ στο κεφάλι και στα πόδια του αθλητή");
                lblTrack.ForeColor = SwimTheme.Amber;
            }
            else if (followed.Length > 0)
            {
                lblTrack.Text = SwimLang.T("ετικέτα: ") + followed;
                lblTrack.ForeColor = SwimTheme.Green;
            }
            else
            {
                lblTrack.Text = SwimLang.T("χωρίς ετικέτα αθλητή");
                lblTrack.ForeColor = SwimTheme.Dim;
            }

            if (manager.Settings != null && chkReel.Checked != manager.Settings.ReelOverlay)
            {
                bool was = updating;
                updating = true;
                chkReel.Checked = manager.Settings.ReelOverlay;
                updating = was;
            }

            if (manager.Settings != null && chkGraphics != null && chkGraphics.Checked != manager.Settings.VideoGraphics)
            {
                bool was = updating;
                updating = true;
                chkGraphics.Checked = manager.Settings.VideoGraphics;
                updating = was;
            }

            if (manager.Settings != null && chkHud != null && chkHud.Checked != manager.Settings.HudLayout)
            {
                bool was = updating;
                updating = true;
                chkHud.Checked = manager.Settings.HudLayout;
                updating = was;
            }

            RefreshCompareHint();
        }

        private Button StrokeButton(string text, string tooltip, EventHandler click)
        {
            Button b = new SwimNoFocusButton { Text = text, Width = 32, Height = 26, Margin = new Padding(2, 2, 0, 2) };
            b.Click += click;
            toolTip.SetToolTip(b, tooltip);
            return b;
        }

        private void AskDeleteAutoStrokes()
        {
            int count = manager.AutoStrokeCount;
            if (count == 0)
            {
                ShowStatus(SwimLang.T("Δεν υπάρχουν αυτόματες χεριές σε αυτόν τον αγώνα."), false);
                return;
            }

            string question = string.Format(CultureInfo.InvariantCulture, SwimLang.T("Διαγραφή των {0} σημαδιών χεριών της αυτόματης μέτρησης σε αυτόν τον αγώνα;\nΟι χεριές που έβαλες εσύ και οι αριθμοί που επιβεβαίωσες μένουν."), count);
            if (MessageBox.Show(this, question, SwimLang.T("Αυτόματες χεριές"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                manager.DeleteAutoStrokes();
        }

        /// <summary>State of the final frame of the race for the report (finish page).</summary>
        public void RefreshFinalFrame()
        {
            if (IsDisposed || lblFinalFrame == null)
                return;

            SwimRace race = manager.Race;
            string text;
            Color color = SwimTheme.Dim;
            if (race == null || race.IsTraining)
            {
                text = "–";
            }
            else if (race.FinalFrame == null)
            {
                text = SwimLang.T("μπαίνει αυτόματα όταν μαρκάρεις τον τερματισμό");
            }
            else
            {
                string at = race.StartTimestamp.HasValue && manager.Timing != null
                    ? SwimLang.T(" · καρέ στο ") + FormatRaceTime(manager.Timing.Seconds(race.FinalFrame.Timestamp, race.StartTimestamp.Value))
                    : "";
                if (race.GetSplit(race.Distance) == null)
                {
                    text = SwimLang.T("μπαίνει στο report μόλις μαρκάρεις τον τερματισμό");
                }
                else if (!string.IsNullOrEmpty(race.FinalFrame.FileId))
                {
                    text = SwimLang.T("✓ στο report") + at;
                    color = SwimTheme.Green;
                }
                else if (manager.FinalFrameUploading)
                {
                    text = SwimLang.T("⏳ ανεβαίνει στο Drive…") + at;
                }
                else if (!manager.FinalFrameOnDisk)
                {
                    text = SwimLang.T("λείπει το αρχείο: πάτα 📷 στο καρέ του τερματισμού");
                    color = SwimTheme.Warn;
                }
                else
                {
                    text = SwimLang.T("στον υπολογιστή · ανεβαίνει με την επόμενη Αποστολή") + at;
                    color = SwimTheme.Warn;
                }
            }

            lblFinalFrame.Text = text;
            lblFinalFrame.ForeColor = color;
            btnFinalFrame.Enabled = race != null && !race.IsTraining;
        }

        private static string FormatRaceTime(double seconds)
        {
            // (As SwimReference.FormatTime, rounded to the hundredth first: 59.996 is 1:00.00, not 60.00 s.)
            string time = SwimReference.FormatTime(seconds);
            return time.IndexOf(':') >= 0 ? time : time + " s";
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
