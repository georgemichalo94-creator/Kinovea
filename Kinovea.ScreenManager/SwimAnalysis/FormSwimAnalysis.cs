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
    /// Button that never takes the keyboard focus, so the hotkeys keep working after a click.
    /// </summary>
    internal class SwimNoFocusButton : Button
    {
        public SwimNoFocusButton()
        {
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }
    }

    /// <summary>
    /// Tool window of the swim race analysis.
    /// The window forwards the player hotkeys (play, frame step, swim marks…) to the player screen,
    /// except while the user is typing in a text field.
    /// </summary>
    public class FormSwimAnalysis : Form
    {
        private static readonly Color colorNext = Color.FromArgb(255, 246, 204);
        private static readonly Color colorDone = Color.White;
        private static readonly Color colorOk = Color.FromArgb(46, 139, 87);
        private static readonly Color colorWarn = Color.FromArgb(200, 110, 0);
        private static readonly Color colorError = Color.FromArgb(192, 0, 0);
        private static readonly Color colorBusy = Color.FromArgb(30, 111, 163);

        private readonly SwimAnalysisManager manager;
        private bool updating;
        private Timer liveTimer;
        private readonly ToolTip toolTip = new ToolTip();
        private Font boldGridFont;

        // Header
        private ComboBox cmbRaces;
        private Button btnNewRace;
        private Button btnDeleteRace;

        // Tabs
        private TabControl tabs;

        // Tab: marking
        private Label lblLive;
        private Label lblStatus;
        private DataGridView grid;
        private FlowLayoutPanel pnlButtons;
        private readonly Dictionary<string, Button> commandButtons = new Dictionary<string, Button>();

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
        private TextBox tbCompetition;
        private TextBox tbLocation;
        private ComboBox cmbRound;
        private TextBox tbDate;
        private TextBox tbCheckpoints;

        // Tab: start / turns / finish
        private TextBox tbReaction;
        private TextBox tbEntryDistHands;
        private TextBox tbEntryDistHips;
        private TextBox tbEntryVelHands;
        private TextBox tbEntryVelHips;
        private TextBox tbEntryVelFeet;
        private TextBox tbUnderwaterDist;
        private TextBox tbBreakoutDist;
        private TextBox tbFinishDist;
        private Label lblStartAuto;
        private DataGridView gridTurns;

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
            Text = "Ανάλυση αγώνα κολύμβησης";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            Size = new Size(600, 780);
            MinimumSize = new Size(500, 560);

            // Header: race selector.
            Panel header = new Panel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 6, 4) };
            Label lblRace = new Label { Text = "Ανάλυση:", AutoSize = true, Location = new Point(6, 11) };
            cmbRaces = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(72, 7), Width = 330, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            cmbRaces.SelectedIndexChanged += CmbRaces_SelectedIndexChanged;
            btnNewRace = new Button { Text = "+ Νέος αθλητής", Width = 110, Height = 25, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnNewRace.Location = new Point(header.Width - 160, 6);
            btnNewRace.Click += (s, e) => manager.NewRace();
            btnDeleteRace = new Button { Text = "✕", Width = 30, Height = 25, Anchor = AnchorStyles.Top | AnchorStyles.Right };
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

            // Footer: sync status.
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(6) };
            lblSync = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            FlowLayoutPanel footerButtons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            btnSend = new SwimNoFocusButton { Text = "Αποστολή τώρα", AutoSize = true, Height = 28 };
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

            tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildMarkingTab());
            tabs.TabPages.Add(BuildRaceTab());
            tabs.TabPages.Add(BuildPhasesTab());
            tabs.TabPages.Add(BuildConnectionTab());

            Controls.Add(tabs);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private TabPage BuildMarkingTab()
        {
            TabPage page = new TabPage("Καταγραφή");
            page.Padding = new Padding(6);

            pnlButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 4) };
            AddCommandButton("SwimStart", "Εκκίνηση", "Σήμα εκκίνησης: t = 0 σε αυτό το καρέ");
            AddCommandButton("SwimSplit", "Πέρασμα", "Χρόνος στο επόμενο σημείο ελέγχου (κεφάλι στα 15μ., 25μ., … / χέρι στον τοίχο)");
            AddCommandButton("SwimStroke", "Χεριά", "Μία χεριά (είσοδος χεριού). Ελεύθερο/ύπτιο: κάθε χέρι. Πεταλούδα/πρόσθιο: κάθε κύκλος");
            AddCommandButton("SwimKick", "Δελφινισμός", "Ένας υποβρύχιος δελφινισμός");
            AddCommandButton("SwimBreakout", "Έξοδος", "Το κεφάλι βγαίνει από το νερό (μετά την εκκίνηση ή τη στροφή)");
            AddCommandButton("SwimFeetOff", "Αποκόλληση", "Τα πόδια φεύγουν από τον βατήρα (εκκίνηση) ή από τον τοίχο (στροφή)");
            AddCommandButton("SwimEntry", "Είσοδος", "Τα χέρια μπαίνουν στο νερό μετά την εκκίνηση");
            AddCommandButton("SwimUndo", "Αναίρεση", "Αναίρεση του τελευταίου σημαδιού");

            lblLive = new Label { Dock = DockStyle.Top, Height = 34, Font = new Font("Segoe UI", 12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(11, 60, 93) };
            lblStatus = new Label { Dock = DockStyle.Top, Height = 36, AutoEllipsis = true, ForeColor = Color.DimGray };

            grid = CreateGrid();
            grid.Columns.Add(Column("d", "Απόσταση", 62));
            grid.Columns.Add(Column("t", "Χρόνος", 62));
            grid.Columns.Add(Column("seg", "Τμήμα", 56));
            grid.Columns.Add(Column("vel", "Ταχ. m/s", 58));
            grid.Columns.Add(Column("strokes", "Χεριές", 52));
            grid.Columns.Add(Column("sr", "SR", 50));
            grid.Columns.Add(Column("dps", "DPS", 50));
            grid.Columns.Add(Column("srx", "SR χρόνων", 64));
            grid.CellDoubleClick += Grid_CellDoubleClick;
            grid.KeyDown += Grid_KeyDown;

            Label hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 32,
                ForeColor = Color.DimGray,
                Text = "Διπλό κλικ: μετάβαση του βίντεο στο σημείο · Delete: διαγραφή του χρόνου (το επόμενο Πέρασμα τον ξαναγράφει) · SR χρόνων: από τα διαστήματα των χεριών"
            };

            page.Controls.Add(grid);
            page.Controls.Add(hint);
            page.Controls.Add(lblStatus);
            page.Controls.Add(lblLive);
            page.Controls.Add(pnlButtons);
            return page;
        }

        private void AddCommandButton(string command, string text, string tooltip)
        {
            Button b = new SwimNoFocusButton
            {
                Width = 98,
                Height = 44,
                Tag = command,
                Text = text,
                Margin = new Padding(2),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            b.Click += (s, e) =>
            {
                if (!manager.ExecutePlayerCommand(command))
                    ShowStatus("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής.", true);
            };
            toolTip.SetToolTip(b, tooltip);
            commandButtons[command] = b;
            pnlButtons.Controls.Add(b);
        }

        private TabPage BuildRaceTab()
        {
            TabPage page = new TabPage("Στοιχεία αγώνα");
            page.Padding = new Padding(8);
            TableLayoutPanel table = NewTable();

            // Athlete from the registry of the main Google Sheet (KOE App).
            TableLayoutPanel searchRow = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tbAthleteSearch = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0) };
            Button btnReloadAthletes = new Button { Text = "↻", Width = 30, Height = 23, Margin = new Padding(0) };
            btnReloadAthletes.Click += (s, e) => manager.RefreshAthletes();
            toolTip.SetToolTip(btnReloadAthletes, "Ανανέωση της λίστας αθλητών από το Google Sheet");
            toolTip.SetToolTip(tbAthleteSearch, "Γράψε μέρος του ονόματος, το μητρώο ή το σωματείο. Enter: επιλογή του πρώτου.");
            searchRow.Controls.Add(tbAthleteSearch, 0, 0);
            searchRow.Controls.Add(btnReloadAthletes, 1, 0);
            lbAthletes = new ListBox { Height = 110, IntegralHeight = false };
            lblAthlete = new Label { AutoSize = false, Height = 34, ForeColor = Color.FromArgb(30, 111, 163) };
            lblAthleteList = new Label { AutoSize = false, Height = 20, ForeColor = Color.DimGray };
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
            cmbPool.Items.AddRange(new object[] { "50μ. (Long Course)", "25μ. (Short Course)" });
            tbCompetition = new TextBox();
            tbLocation = new TextBox();
            cmbRound = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
            cmbRound.Items.AddRange(new object[] { "Heats", "Semi-final", "Final", "Προκριματικός", "Ημιτελικός", "Τελικός" });
            tbDate = new TextBox();
            tbCheckpoints = new TextBox { Multiline = true, Height = 64, ScrollBars = ScrollBars.Vertical };
            Button btnDefaults = new Button { Text = "Προεπιλεγμένα σημεία", AutoSize = true };
            btnDefaults.Click += (s, e) => ApplyDefaultCheckpoints();

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
            AddRow(table, "Σημεία ελέγχου (μ.)", tbCheckpoints);
            AddRow(table, "", btnDefaults);

            Label note = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 70,
                ForeColor = Color.DimGray,
                Text = "Τα σημεία ελέγχου είναι οι αποστάσεις όπου πατάς «Πέρασμα», με τη σειρά. Με την αλλαγή απόστασης ή πισίνας μπαίνουν τα προεπιλεγμένα (όπως στο Excel: 15/25/35/45/50 ανά μήκος έως 200μ., 15/25/45/50 στα 400μ., 15/45/50 στα 800/1500μ.)."
            };
            table.Controls.Add(note, 0, table.RowCount);
            table.SetColumnSpan(note, 2);
            table.RowCount++;

            tbAthlete.TextChanged += (s, e) => CommitRaceInfo(false);
            nudLane.ValueChanged += (s, e) => CommitRaceInfo(false);
            cmbStyle.SelectedIndexChanged += (s, e) => CommitRaceInfo(false);
            cmbDistance.SelectedIndexChanged += (s, e) => CommitDistance();
            cmbPool.SelectedIndexChanged += (s, e) => CommitDistance();
            tbCompetition.TextChanged += (s, e) => CommitRaceInfo(false);
            tbLocation.TextChanged += (s, e) => CommitRaceInfo(false);
            cmbRound.TextChanged += (s, e) => CommitRaceInfo(false);
            tbDate.TextChanged += (s, e) => CommitRaceInfo(false);
            tbCheckpoints.Leave += (s, e) => CommitCheckpoints();

            page.Controls.Add(table);
            return page;
        }

        private TabPage BuildPhasesTab()
        {
            TabPage page = new TabPage("Εκκίνηση / Στροφές");
            page.Padding = new Padding(8);
            TableLayoutPanel table = NewTable();
            table.Dock = DockStyle.Top;
            table.AutoSize = true;

            tbReaction = NumberBox();
            tbEntryDistHands = NumberBox();
            tbEntryDistHips = NumberBox();
            tbEntryVelHands = NumberBox();
            tbEntryVelHips = NumberBox();
            tbEntryVelFeet = NumberBox();
            tbUnderwaterDist = NumberBox();
            tbBreakoutDist = NumberBox();
            tbFinishDist = NumberBox();

            AddRow(table, "Χρόνος αντίδρασης (s)", tbReaction);
            AddRow(table, "Απόσταση εισόδου – χέρια (m)", tbEntryDistHands);
            AddRow(table, "Απόσταση εισόδου – ισχία (m)", tbEntryDistHips);
            AddRow(table, "Ταχύτητα εισόδου – χέρια (m/s)", tbEntryVelHands);
            AddRow(table, "Ταχύτητα εισόδου – ισχία (m/s)", tbEntryVelHips);
            AddRow(table, "Ταχύτητα εισόδου – πόδια (m/s)", tbEntryVelFeet);
            AddRow(table, "Απόσταση κάτω από το νερό (m)", tbUnderwaterDist);
            AddRow(table, "Απόσταση εξόδου – κεφάλι (m)", tbBreakoutDist);
            AddRow(table, "Τερματισμός: απόσταση τελευταίας χεριάς (m)", tbFinishDist);

            lblStartAuto = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 58, ForeColor = Color.FromArgb(30, 111, 163) };

            gridTurns = CreateGrid();
            gridTurns.ReadOnly = false;
            gridTurns.Columns.Add(Column("wall", "Τοίχος (μ.)", 70));
            gridTurns.Columns.Add(Column("uwDist", "Μέτρα κάτω από το νερό", 92));
            gridTurns.Columns.Add(Column("turnTime", "Χρόνος στροφής (s)", 82));
            gridTurns.Columns.Add(Column("uwTime", "Υποβρύχιος χρόνος (s)", 86));
            gridTurns.Columns.Add(Column("kicks", "Δελφινισμοί", 72));
            gridTurns.Columns.Add(Column("gap", "Τελ. χεριά → τοίχος (s)", 90));
            foreach (DataGridViewColumn c in gridTurns.Columns)
            {
                c.ReadOnly = c.Name != "uwDist" && c.Name != "turnTime";
                c.DefaultCellStyle.BackColor = c.ReadOnly && c.Name != "wall" ? Color.FromArgb(227, 241, 251) : Color.White;
            }
            gridTurns.CellEndEdit += GridTurns_CellEndEdit;

            Label lblTurns = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Text = "Στροφές: τα λευκά κελιά συμπληρώνονται με το χέρι. Ο χρόνος στροφής υπολογίζεται αυτόματα όταν μαρκάρεις «Αποκόλληση» μετά τον τοίχο (το χειροκίνητο υπερισχύει).",
                ForeColor = Color.DimGray
            };

            page.Controls.Add(gridTurns);
            page.Controls.Add(lblTurns);
            page.Controls.Add(lblStartAuto);
            page.Controls.Add(table);
            return page;
        }

        private TabPage BuildConnectionTab()
        {
            TabPage page = new TabPage("Σύνδεση");
            page.Padding = new Padding(8);
            TableLayoutPanel table = NewTable();

            tbUrl = new TextBox();
            tbToken = new TextBox();
            cmbRounding = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbRounding.Items.AddRange(new object[] { "Μισές χεριές (0.5) – όπως στο Excel", "Ακέραιες χεριές", "Ακριβές (με δεκαδικά)" });
            chkAutoSend = new CheckBox { Text = "Αποστολή στο Google Sheet σε κάθε πάτημα", AutoSize = true };
            Button btnSave = new Button { Text = "Αποθήκευση", AutoSize = true };
            btnSave.Click += (s, e) => SaveConnection();
            Button btnTest = new Button { Text = "Δοκιμή σύνδεσης", AutoSize = true };
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
                ForeColor = Color.DimGray,
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
            TextBox tb = new TextBox { Width = 90 };
            tb.Leave += (s, e) => CommitManualValues();
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
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(29, 111, 163);
            g.RowTemplate.Height = 22;
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
                    int distIndex = Array.IndexOf(SwimPresets.DistanceChoices, r.DistanceLabel);
                    if (distIndex < 0)
                    {
                        cmbDistance.Items.Add(r.DistanceLabel);
                        distIndex = cmbDistance.Items.Count - 1;
                    }

                    cmbDistance.SelectedIndex = distIndex;
                    cmbPool.SelectedIndex = r.Pool == 25 ? 1 : 0;
                    tbCompetition.Text = r.Competition;
                    tbLocation.Text = r.Location;
                    cmbRound.Text = r.Round;
                    tbDate.Text = r.Date;
                    tbCheckpoints.Text = SwimPresets.FormatCheckpoints(r.Checkpoints);

                    tbReaction.Text = Format(r.Reaction);
                    tbEntryDistHands.Text = Format(r.EntryDistHands);
                    tbEntryDistHips.Text = Format(r.EntryDistHips);
                    tbEntryVelHands.Text = Format(r.EntryVelHands);
                    tbEntryVelHips.Text = Format(r.EntryVelHips);
                    tbEntryVelFeet.Text = Format(r.EntryVelFeet);
                    tbUnderwaterDist.Text = Format(r.UnderwaterDist);
                    tbBreakoutDist.Text = Format(r.BreakoutDist);
                    tbFinishDist.Text = Format(r.FinishLastStrokeDist);
                }

                SwimSettings settings = manager.Settings;
                tbUrl.Text = settings.WebAppUrl;
                tbToken.Text = settings.Token;
                cmbRounding.SelectedIndex = settings.StrokeRounding >= 1 ? 1 : (settings.StrokeRounding > 0 ? 0 : 2);
                chkAutoSend.Checked = settings.AutoSend;
            }
            finally
            {
                updating = false;
            }

            RefreshButtons();
            RefreshAthletes();
            RefreshData();
            RefreshSync();
            if (!string.IsNullOrEmpty(manager.LastStatus))
                ShowStatus(manager.LastStatus, manager.LastStatusIsWarning);
        }

        /// <summary>
        /// Show the current key bindings on the command buttons.
        /// </summary>
        private void RefreshButtons()
        {
            foreach (KeyValuePair<string, Button> pair in commandButtons)
            {
                HotkeyCommand command = HotkeySettingsManager.ActiveBindings.FindByName("PlayerScreen", pair.Key);
                string key = command != null && command.KeyData != Keys.None ? KeyName(command.KeyData) : "";
                string baseText = pair.Value.Text.Split('\n')[0];
                pair.Value.Text = string.IsNullOrEmpty(key) ? baseText : baseText + "\n[" + key + "]";
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
            if (race == null || computed == null)
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
                gr.Cells["d"].Value = row.D + "μ." + (row.Style != race.Style && race.Style == "im" ? " " + row.Style : "");
                gr.Cells["t"].Value = Format(row.T);
                gr.Cells["seg"].Value = Format(row.SegTime);
                gr.Cells["vel"].Value = Format(row.Vel);
                gr.Cells["strokes"].Value = FormatStrokes(row.Strokes);
                gr.Cells["sr"].Value = Format(row.Sr, "0.0");
                gr.Cells["dps"].Value = Format(row.Dps);
                gr.Cells["srx"].Value = Format(row.SrExact, "0.0");
                bool isNext = row.D == next;
                gr.DefaultCellStyle.BackColor = isNext ? colorNext : colorDone;
                if (boldGridFont == null)
                    boldGridFont = new Font(grid.Font, FontStyle.Bold);
                gr.DefaultCellStyle.Font = isNext ? boldGridFont : grid.Font;
                if (isNext)
                    nextRowIndex = i;
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
            RefreshLive();
        }

        private void RefreshPhases(SwimRace race, SwimComputed computed)
        {
            lblStartAuto.Text = string.Format(
                "Αυτόματα από τα σημάδια – Εκκίνηση: βατήρας {0} s · είσοδος {1} s · έξοδος από το νερό {2} s · δελφινισμοί {3}\nΤερματισμός: τελευταία χεριά → τοίχος {4} s · Συνολικές χεριές (πατήματα): {5}",
                Dash(computed.BlockTime), Dash(computed.EntryTime), Dash(computed.StartUnderwaterTime),
                computed.StartKicks.HasValue ? computed.StartKicks.Value.ToString(CultureInfo.InvariantCulture) : "–",
                Dash(computed.FinishLastStrokeGap), computed.TotalPresses);

            if (gridTurns.IsCurrentCellInEditMode)
                return;

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
                gr.Cells["wall"].Value = t.Wall;
                gr.Cells["uwDist"].Value = manual != null ? Format(manual.UnderwaterDist) : "";
                gr.Cells["turnTime"].Value = manual != null && manual.TurnTime.HasValue ? Format(manual.TurnTime) : Format(t.TurnTime);
                gr.Cells["uwTime"].Value = Format(t.UnderwaterTime);
                gr.Cells["kicks"].Value = t.Kicks.HasValue ? t.Kicks.Value.ToString(CultureInfo.InvariantCulture) : "";
                gr.Cells["gap"].Value = Format(t.LastStrokeGap);
            }
        }

        private void RefreshLive()
        {
            if (IsDisposed || manager.Race == null)
                return;

            SwimRace race = manager.Race;
            string time = manager.HasVideo ? Format(manager.CurrentRaceTime) : "–";
            int next = race.NextCheckpoint();
            string nextText = next > 0 ? next + "μ." : "τέλος";
            int presses = SwimCalculator.PressesSinceLastSplit(manager.Computed);
            string start = race.StartTimestamp.HasValue ? "" : "  (χωρίς σήμα εκκίνησης)";
            string text = string.Format("⏱ {0} s   ▸ Επόμενο: {1}   ✋ {2}{3}", time, nextText, presses, start);
            if (lblLive.Text != text)
                lblLive.Text = text;
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
                lblAthlete.ForeColor = Color.FromArgb(30, 111, 163);
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
                default: color = Color.DimGray; break;
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

            lblStatus.ForeColor = warning ? colorWarn : Color.DimGray;
            lblStatus.Text = message;
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
            int pool = cmbPool.SelectedIndex == 1 ? 25 : 50;
            if (r.Distance == distance && r.Relay == relay && r.Pool == pool)
                return;

            r.Distance = distance;
            r.Relay = relay;
            r.Pool = pool;
            r.Checkpoints = SwimPresets.DefaultCheckpoints(distance, pool, relay);
            updating = true;
            tbCheckpoints.Text = SwimPresets.FormatCheckpoints(r.Checkpoints);
            updating = false;
            CommitRaceInfo(true);
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
                ShowStatus("Προσοχή: το τελευταίο σημείο ελέγχου δεν είναι ίσο με την απόσταση του αγώνα (" + r.Distance + "μ.).", true);

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
            lblConnection.ForeColor = Color.DimGray;
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

            if (!typing && focused == grid && keyData == Keys.Delete)
                return base.ProcessCmdKey(ref msg, keyData);

            if (!typing || (keyData & (Keys.Control | Keys.Alt)) != 0 || (keyData >= Keys.F1 && keyData <= Keys.F24))
            {
                HotkeyCommand command = HotkeySettingsManager.ActiveBindings.FindByKeyData("PlayerScreen", keyData);
                if (command != null && (!typing || command.Name.StartsWith("Swim")))
                {
                    if (manager.ExecutePlayerCommand(command.Name))
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

        private static string FormatStrokes(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "";
        }

        private static string Dash(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) : "–";
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
