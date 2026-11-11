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
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (Swim 2.21.0) The details of the next recordings of the GoPro cameras: race, start or turn; the athlete from the list of the
    /// registry (or a name), his club; the competition, place and day; for a race the event, round, heat and lane (the swimmer of the
    /// lane from pasted results or a start list). Also the folder of the videos, and the path the next video will get. (2.22.0) On the
    /// right, the order of the athletes of a continuous recording: they swim one after the other in the same video, «⏭ Επόμενος» at the
    /// end of each attempt, and the videos of the cards are cut for each of them.
    /// </summary>
    public class FormGoProTakeDetails : Form
    {
        private readonly SwimTakeDetails details;
        private readonly string sampleCamera;
        private readonly RadioButton rbRace = new RadioButton();
        private readonly RadioButton rbStart = new RadioButton();
        private readonly RadioButton rbTurn = new RadioButton();
        private readonly TextBox tbSearch = new TextBox();
        private readonly ListBox lbAthletes = new ListBox();
        private readonly TextBox tbAthleteId = new TextBox();
        private readonly TextBox tbAthlete = new TextBox();
        private readonly TextBox tbClub = new TextBox();
        private readonly TextBox tbCompetition = new TextBox();
        private readonly TextBox tbLocation = new TextBox();
        private readonly TextBox tbDate = new TextBox();
        private readonly ComboBox cmbDistance = new ComboBox();
        private readonly ComboBox cmbStyle = new ComboBox();
        private readonly ComboBox cmbRound = new ComboBox();
        private readonly NumericUpDown nudHeat = new NumericUpDown();
        private readonly NumericUpDown nudLane = new NumericUpDown();
        private readonly Label lblSwimmers = new Label();
        private readonly TextBox tbRoot = new TextBox();
        private readonly Label lblPreview = new Label();
        private readonly List<Control> raceOnly = new List<Control>();
        private readonly ListBox lbQueue = new ListBox();
        private readonly CheckBox cbQueueEnter = new CheckBox();
        private readonly Label lblQueueCount = new Label();
        private readonly Panel queuePanel = new Panel();
        private readonly ToolTip queueTips = new ToolTip();
        private readonly bool withOrder;
        private bool updating;

        /// <summary>The details chosen (null when cancelled) and the folder of the videos.</summary>
        public SwimTakeDetails Result { get; private set; }
        public string RootFolder { get; private set; }

        /// <summary>
        /// The details of the next recordings (with the order of the athletes of a continuous recording), or of one recording afterwards
        /// (`order` false: no order).
        /// </summary>
        public FormGoProTakeDetails(SwimTakeDetails current, string root, string sampleCamera)
            : this(current, root, sampleCamera, true)
        {
        }

        public FormGoProTakeDetails(SwimTakeDetails current, string root, string sampleCamera, bool order)
        {
            details = (current ?? new SwimTakeDetails()).Clone();
            withOrder = order;
            this.sampleCamera = string.IsNullOrEmpty(sampleCamera) ? SwimLang.T("Κάμερα") : sampleCamera;

            Text = SwimLang.T("📝 Στοιχεία εγγραφής GoPro");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(1090, 740);
            KeyPreview = true;

            TableLayoutPanel table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(12, 10, 12, 6),
                AutoScroll = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // What the recordings are.
            FlowLayoutPanel kinds = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            rbTurn.Text = SwimLang.T("Στροφή");
            rbStart.Text = SwimLang.T("Εκκίνηση");
            rbRace.Text = SwimLang.T("Αγώνας");
            foreach (RadioButton rb in new[] { rbTurn, rbStart, rbRace })
            {
                rb.AutoSize = true;
                rb.Margin = new Padding(0, 3, 18, 3);
                rb.CheckedChanged += (s, e) => KindChanged();
                kinds.Controls.Add(rb);
            }

            AddRow(table, SwimLang.T("Τι γράφεις"), kinds);

            // The athlete: the list of the registry (KOE App), or a name.
            tbSearch.Width = 440;
            tbSearch.TextChanged += (s, e) => Search();
            tbSearch.KeyDown += TbSearch_KeyDown;
            AddRow(table, SwimLang.T("Αναζήτηση αθλητή"), tbSearch);

            lbAthletes.Width = 440;
            lbAthletes.Height = 90;
            lbAthletes.IntegralHeight = false;
            lbAthletes.DoubleClick += (s, e) =>
            {
                if (QueueEnter)
                    QueueSelected();
                else
                    ChooseSelected(false);
            };
            lbAthletes.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    if (QueueEnter)
                        QueueSelected();
                    else
                        ChooseSelected(true);

                    e.Handled = true;
                }
            };
            AddRow(table, "", lbAthletes);

            Label searchHelp = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                MaximumSize = new Size(440, 0),
                Text = SwimLang.T("Γράψε μέρος του ονόματος ή τον ΑΜ. Enter: ο πρώτος της λίστας και «Εντάξει». Διπλό κλικ: διαλέγεις άλλον.")
            };
            AddRow(table, "", searchHelp);

            tbAthleteId.Width = 120;
            tbAthlete.Width = 440;
            tbClub.Width = 440;
            AddRow(table, SwimLang.T("ΑΜ"), tbAthleteId);
            AddRow(table, SwimLang.T("Ονοματεπώνυμο"), tbAthlete);
            AddRow(table, SwimLang.T("Σωματείο"), tbClub);
            foreach (TextBox tb in new[] { tbAthleteId, tbAthlete, tbClub })
                tb.TextChanged += (s, e) => FieldsChanged();

            // The competition, place and day.
            tbCompetition.Width = 440;
            tbLocation.Width = 440;
            tbDate.Width = 120;
            AddRow(table, SwimLang.T("Διοργάνωση"), tbCompetition);
            AddRow(table, SwimLang.T("Τόπος"), tbLocation);
            AddRow(table, SwimLang.T("Ημερομηνία"), tbDate);
            foreach (TextBox tb in new[] { tbCompetition, tbLocation, tbDate })
                tb.TextChanged += (s, e) => FieldsChanged();

            // The race: event, round, heat, lane; the swimmers of pasted results.
            FlowLayoutPanel eventPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            cmbDistance.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbDistance.Width = 80;
            cmbDistance.Items.AddRange(SwimPresets.DistanceChoices);
            cmbStyle.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbStyle.Width = 170;
            cmbStyle.Items.AddRange(SwimPresets.StyleNames);
            cmbRound.Width = 150;
            cmbRound.Items.AddRange(new object[] { "", SwimLang.T("Προκριματικός"), SwimLang.T("Ημιτελικός"), SwimLang.T("Τελικός"), SwimLang.T("Τελικός Β") });
            eventPanel.Controls.Add(cmbDistance);
            eventPanel.Controls.Add(cmbStyle);
            eventPanel.Controls.Add(cmbRound);
            cmbDistance.SelectedIndexChanged += (s, e) => FieldsChanged();
            cmbStyle.SelectedIndexChanged += (s, e) => FieldsChanged();
            cmbRound.TextChanged += (s, e) => FieldsChanged();
            raceOnly.Add(AddRow(table, SwimLang.T("Αγώνισμα · φάση"), eventPanel));

            FlowLayoutPanel heatLane = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            nudHeat.Width = 60;
            nudHeat.Maximum = 99;
            nudLane.Width = 60;
            nudLane.Maximum = 10;
            heatLane.Controls.Add(new Label { Text = SwimLang.T("Σειρά"), AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            heatLane.Controls.Add(nudHeat);
            heatLane.Controls.Add(new Label { Text = SwimLang.T("Διάδρομος"), AutoSize = true, Margin = new Padding(18, 6, 6, 0) });
            heatLane.Controls.Add(nudLane);
            nudHeat.ValueChanged += (s, e) => FieldsChanged();
            nudLane.ValueChanged += (s, e) => FieldsChanged();
            raceOnly.Add(AddRow(table, "", heatLane));

            FlowLayoutPanel pastePanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            Button btnPaste = new Button { Text = SwimLang.T("📋 Επικόλληση αποτελεσμάτων ή λίστας εκκίνησης…"), AutoSize = true };
            btnPaste.Click += (s, e) => PasteList();
            lblSwimmers.AutoSize = true;
            lblSwimmers.MaximumSize = new Size(440, 0);
            lblSwimmers.ForeColor = Color.DimGray;
            pastePanel.Controls.Add(btnPaste);
            raceOnly.Add(AddRow(table, "", pastePanel));
            AddRow(table, "", lblSwimmers);

            Button btnFromAnalysis = new Button { Text = SwimLang.T("↙ Από τα στοιχεία της ανάλυσης"), AutoSize = true };
            btnFromAnalysis.Click += (s, e) => FromAnalysis();
            AddRow(table, "", btnFromAnalysis);

            // The folder of the videos and the path of the next one.
            FlowLayoutPanel rootPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            tbRoot.Width = 380;
            tbRoot.Text = root ?? "";
            tbRoot.TextChanged += (s, e) => FieldsChanged();
            Button btnRoot = new Button { Text = "…", Width = 40 };
            btnRoot.Click += (s, e) => ChooseRoot();
            rootPanel.Controls.Add(tbRoot);
            rootPanel.Controls.Add(btnRoot);
            AddRow(table, SwimLang.T("Φάκελος βίντεο"), rootPanel);

            lblPreview.AutoSize = true;
            lblPreview.MaximumSize = new Size(460, 0);
            lblPreview.ForeColor = Color.FromArgb(30, 90, 140);
            AddRow(table, SwimLang.T("Το επόμενο βίντεο"), lblPreview);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
            Button ok = new Button { Text = SwimLang.T("Εντάξει"), DialogResult = DialogResult.OK, Width = 100 };
            Button cancel = new Button { Text = SwimLang.T("Άκυρο"), DialogResult = DialogResult.Cancel, Width = 100 };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            CancelButton = cancel;

            BuildQueuePanel();
            Controls.Add(table);
            Controls.Add(buttons);
            if (withOrder)
                Controls.Add(queuePanel);
            else
                ClientSize = new Size(660, 740);

            ShowDetails();
            FormClosing += (s, e) =>
            {
                if (DialogResult == DialogResult.OK)
                {
                    ReadFields();
                    Result = details.Clone();
                    RootFolder = tbRoot.Text.Trim();
                }
            };

            Shown += (s, e) => tbSearch.Focus();
        }

        private static Control AddRow(TableLayoutPanel table, string label, Control control)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label l = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 6, 0) };
            control.Margin = new Padding(0, 3, 0, 3);
            table.Controls.Add(l, 0, row);
            table.Controls.Add(control, 1, row);
            return control;
        }

        #region Fields
        private void ShowDetails()
        {
            updating = true;
            try
            {
                rbRace.Checked = details.Kind == SwimTakeDetails.KindRace;
                rbStart.Checked = details.Kind == SwimTakeDetails.KindStart;
                rbTurn.Checked = !rbRace.Checked && !rbStart.Checked;
                tbAthleteId.Text = details.AthleteId ?? "";
                tbAthlete.Text = details.Athlete ?? "";
                tbClub.Text = details.Club ?? "";
                tbCompetition.Text = details.Competition ?? "";
                tbLocation.Text = details.Location ?? "";
                tbDate.Text = details.Date ?? "";
                string distance = details.Relay && details.Distance % 4 == 0 ? "4x" + (details.Distance / 4).ToString(CultureInfo.InvariantCulture) : details.Distance.ToString(CultureInfo.InvariantCulture);
                cmbDistance.SelectedItem = cmbDistance.Items.Cast<object>().FirstOrDefault(i => (string)i == distance) ?? cmbDistance.Items[1];
                cmbStyle.SelectedIndex = Math.Max(0, Array.IndexOf(SwimPresets.Styles, details.Style));
                cmbRound.Text = details.Round ?? "";
                nudHeat.Value = Math.Max(0, Math.Min(99, details.Heat));
                nudLane.Value = Math.Max(0, Math.Min(10, details.Lane));
            }
            finally
            {
                updating = false;
            }

            KindChanged();
            ShowSwimmers();
            ShowQueue();
            ShowPreview();
        }

        private void ReadFields()
        {
            details.Kind = rbRace.Checked ? SwimTakeDetails.KindRace : (rbStart.Checked ? SwimTakeDetails.KindStart : SwimTakeDetails.KindTurn);
            details.AthleteId = tbAthleteId.Text.Trim();
            details.Athlete = tbAthlete.Text.Trim();
            details.Club = tbClub.Text.Trim();
            details.Competition = tbCompetition.Text.Trim();
            details.Location = tbLocation.Text.Trim();
            details.Date = tbDate.Text.Trim();
            int distance;
            bool relay;
            if (cmbDistance.SelectedItem != null && SwimPresets.TryParseDistance((string)cmbDistance.SelectedItem, out distance, out relay))
            {
                details.Distance = distance;
                details.Relay = relay;
            }

            if (cmbStyle.SelectedIndex >= 0 && cmbStyle.SelectedIndex < SwimPresets.Styles.Length)
                details.Style = SwimPresets.Styles[cmbStyle.SelectedIndex];

            details.Round = cmbRound.Text.Trim();
            details.Heat = (int)nudHeat.Value;
            details.Lane = (int)nudLane.Value;
        }

        private void FieldsChanged()
        {
            if (updating)
                return;

            ReadFields();
            ShowPreview();
        }

        private void KindChanged()
        {
            bool race = rbRace.Checked;
            foreach (Control c in raceOnly)
                c.Enabled = race;

            // (2.22.0) A race is never a continuous recording.
            queuePanel.Enabled = !race;

            if (!updating)
                FieldsChanged();
        }

        private void ShowPreview()
        {
            string root = tbRoot.Text.Trim();
            if (root.Length == 0)
            {
                lblPreview.Text = SwimLang.T("Διάλεξε τον φάκελο των βίντεο (π.χ. E:\\).");
                return;
            }

            if (details.IsContinuous)
            {
                // (2.22.0) The video of the first athlete of the order; one for each of them.
                SwimTake part = new SwimTake { Start = DateTime.Now, Details = details.ForAthlete(details.Queue[0]) };
                lblPreview.Text = SwimGoProTakes.TargetPath(root, part, sampleCamera, 1) + "\n" +
                    string.Format(CultureInfo.InvariantCulture, SwimLang.T("(συνεχόμενη εγγραφή: ένα βίντεο για κάθε έναν από τους {0} αθλητές της σειράς)"), details.Queue.Count);
                return;
            }

            SwimTake sample = new SwimTake { Start = DateTime.Now, Details = details.Clone() };
            lblPreview.Text = SwimGoProTakes.TargetPath(root, sample, sampleCamera, 1);
        }

        private void ShowSwimmers()
        {
            int n = details.Swimmers != null ? details.Swimmers.Count : 0;
            if (n == 0)
            {
                lblSwimmers.Text = "";
                return;
            }

            SwimTakeSwimmer lane = details.SwimmerOfLane();
            lblSwimmers.Text = string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} κολυμβητές· ο διάδρομος δίνει τον αθλητή"), n) +
                (lane != null ? ": " + lane.Name : "");
        }
        #endregion

        #region Athletes
        private void Search()
        {
            lbAthletes.BeginUpdate();
            try
            {
                lbAthletes.Items.Clear();
                string q = tbSearch.Text.Trim();
                SwimAthleteList list = SwimAnalysisManager.Instance.Athletes;
                if (q.Length == 0 || list == null)
                    return;

                foreach (SwimAthlete a in list.Search(q, 40))
                    lbAthletes.Items.Add(a);

                if (lbAthletes.Items.Count > 0)
                    lbAthletes.SelectedIndex = 0;
            }
            finally
            {
                lbAthletes.EndUpdate();
            }
        }

        private void TbSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && lbAthletes.Items.Count > 0)
            {
                lbAthletes.Focus();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (lbAthletes.Items.Count > 0)
                    lbAthletes.SelectedIndex = Math.Max(0, lbAthletes.SelectedIndex);

                if (QueueEnter)
                    QueueSelected();
                else
                    ChooseSelected(true);
            }
        }

        /// <summary>The athlete of the list in the fields; with `close`, the details are taken at once (the next athlete in two keys).</summary>
        private void ChooseSelected(bool close)
        {
            SwimAthlete a = lbAthletes.SelectedItem as SwimAthlete;
            if (a == null)
                return;

            updating = true;
            try
            {
                tbAthleteId.Text = a.Id ?? "";
                tbAthlete.Text = a.Name ?? "";
                tbClub.Text = a.Club ?? "";
            }
            finally
            {
                updating = false;
            }

            FieldsChanged();
            if (close)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        #endregion

        #region Order of the athletes (2.22.0)
        /// <summary>Enter (or a double click) in the search puts the athlete in the order (the form stays open for the next one).</summary>
        private bool QueueEnter
        {
            get { return withOrder && cbQueueEnter.Checked && !rbRace.Checked; }
        }

        private void BuildQueuePanel()
        {
            queuePanel.Dock = DockStyle.Right;
            queuePanel.Width = 420;
            queuePanel.Padding = new Padding(10, 10, 12, 8);

            Label title = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Font = new Font(Font, FontStyle.Bold),
                Text = SwimLang.T("👥 Σειρά αθλητών (συνεχόμενη εγγραφή)")
            };
            Label help = new Label
            {
                Dock = DockStyle.Top,
                Height = 96,
                ForeColor = Color.DimGray,
                Text = SwimLang.T("Πολλοί αθλητές στο ίδιο βίντεο, ο ένας μετά τον άλλον: βάλε τους με τη σειρά που θα κολυμπήσουν. Στην εγγραφή πατάς «⏭ Επόμενος» στο τέλος κάθε προσπάθειας· από τις κάρτες, το βίντεο κάθε κάμερας κόβεται για κάθε αθλητή. Άδεια σειρά: ένα βίντεο για τον αθλητή των πεδίων.")
            };
            cbQueueEnter.Dock = DockStyle.Top;
            cbQueueEnter.Height = 26;
            cbQueueEnter.Text = SwimLang.T("Enter στην αναζήτηση: ο αθλητής μπαίνει στη σειρά");
            cbQueueEnter.Checked = details.IsContinuous;

            lbQueue.Dock = DockStyle.Fill;
            lbQueue.IntegralHeight = false;
            lbQueue.HorizontalScrollbar = true;
            lbQueue.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete)
                {
                    RemoveQueued();
                    e.Handled = true;
                }
            };

            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 70, Padding = new Padding(0, 6, 0, 0), WrapContents = true };
            Button add = QueueButton(actions, SwimLang.T("➕ Ο αθλητής των πεδίων"), SwimLang.T("Ο αθλητής των πεδίων (ΑΜ, όνομα, σωματείο) στο τέλος της σειράς."));
            Button paste = QueueButton(actions, SwimLang.T("📋 Επικόλληση λίστας…"), SwimLang.T("Μία γραμμή για κάθε αθλητή (από Excel: ΑΜ, ονοματεπώνυμο, σωματείο· ή μόνο ΑΜ ή όνομα: τα υπόλοιπα από τη λίστα των αθλητών)."));
            Button up = QueueButton(actions, "▲", SwimLang.T("Πιο νωρίς στη σειρά"));
            Button down = QueueButton(actions, "▼", SwimLang.T("Πιο αργά στη σειρά"));
            Button remove = QueueButton(actions, "✖", SwimLang.T("Έξω από τη σειρά"));
            Button clear = QueueButton(actions, SwimLang.T("🗑 Όλοι"), SwimLang.T("Άδεια σειρά: ένα βίντεο για τον αθλητή των πεδίων."));
            add.Click += (s, e) => QueueFields();
            paste.Click += (s, e) => PasteQueue();
            up.Click += (s, e) => MoveQueued(-1);
            down.Click += (s, e) => MoveQueued(1);
            remove.Click += (s, e) => RemoveQueued();
            clear.Click += (s, e) =>
            {
                if (details.Queue.Count == 0)
                    return;

                if (MessageBox.Show(this, SwimLang.T("Να αδειάσει η σειρά των αθλητών;"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                details.Queue = new List<SwimTakeAthlete>();
                ShowQueue();
                ShowPreview();
            };

            lblQueueCount.Dock = DockStyle.Bottom;
            lblQueueCount.Height = 22;
            lblQueueCount.ForeColor = Color.FromArgb(30, 90, 140);

            queuePanel.Controls.Add(lbQueue);
            queuePanel.Controls.Add(cbQueueEnter);
            queuePanel.Controls.Add(help);
            queuePanel.Controls.Add(title);
            queuePanel.Controls.Add(actions);
            queuePanel.Controls.Add(lblQueueCount);
        }

        private Button QueueButton(FlowLayoutPanel panel, string text, string tip)
        {
            Button b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 6, 4) };
            queueTips.SetToolTip(b, tip);
            panel.Controls.Add(b);
            return b;
        }

        private void ShowQueue()
        {
            if (details.Queue == null)
                details.Queue = new List<SwimTakeAthlete>();

            int selected = lbQueue.SelectedIndex;
            lbQueue.BeginUpdate();
            lbQueue.Items.Clear();
            for (int i = 0; i < details.Queue.Count; i++)
                lbQueue.Items.Add((i + 1).ToString(CultureInfo.InvariantCulture) + ".  " + details.Queue[i]);

            lbQueue.EndUpdate();
            if (lbQueue.Items.Count > 0)
                lbQueue.SelectedIndex = Math.Max(0, Math.Min(lbQueue.Items.Count - 1, selected));

            lblQueueCount.Text = details.Queue.Count == 0 ?
                SwimLang.T("Χωρίς σειρά: ένα βίντεο για τον αθλητή των πεδίων.") :
                string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} αθλητές · συνεχόμενη εγγραφή"), details.Queue.Count);
        }

        private void Enqueue(SwimTakeAthlete athlete)
        {
            if (athlete == null || string.IsNullOrWhiteSpace(athlete.Athlete))
                return;

            details.Queue.Add(athlete);
            cbQueueEnter.Checked = true;
            ShowQueue();
            lbQueue.SelectedIndex = lbQueue.Items.Count - 1;
            ShowPreview();
        }

        /// <summary>The athlete chosen in the search into the order; the search is ready for the next one.</summary>
        private void QueueSelected()
        {
            SwimAthlete a = lbAthletes.SelectedItem as SwimAthlete;
            if (a == null)
                return;

            Enqueue(new SwimTakeAthlete { AthleteId = a.Id ?? "", Athlete = a.Name ?? "", Club = a.Club ?? "" });
            tbSearch.Text = "";
            tbSearch.Focus();
        }

        private void QueueFields()
        {
            ReadFields();
            if (string.IsNullOrWhiteSpace(details.Athlete))
            {
                MessageBox.Show(this, SwimLang.T("Γράψε πρώτα τον αθλητή (αναζήτηση ή ονοματεπώνυμο)."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Enqueue(new SwimTakeAthlete { AthleteId = details.AthleteId, Athlete = details.Athlete, Club = details.Club });
            tbSearch.Text = "";
            tbSearch.Focus();
        }

        private void MoveQueued(int step)
        {
            int i = lbQueue.SelectedIndex;
            int j = i + step;
            if (i < 0 || j < 0 || j >= details.Queue.Count)
                return;

            SwimTakeAthlete a = details.Queue[i];
            details.Queue.RemoveAt(i);
            details.Queue.Insert(j, a);
            ShowQueue();
            lbQueue.SelectedIndex = j;
            ShowPreview();
        }

        private void RemoveQueued()
        {
            int i = lbQueue.SelectedIndex;
            if (i < 0 || i >= details.Queue.Count)
                return;

            details.Queue.RemoveAt(i);
            ShowQueue();
            ShowPreview();
        }

        private void PasteQueue()
        {
            string clip = "";
            try
            {
                if (Clipboard.ContainsText())
                    clip = Clipboard.GetText();
            }
            catch (Exception) { }

            using (Form paste = new Form())
            {
                paste.Text = SwimLang.T("📋 Επικόλληση λίστας αθλητών");
                paste.StartPosition = FormStartPosition.CenterParent;
                paste.ClientSize = new Size(560, 420);
                paste.MinimizeBox = false;
                paste.MaximizeBox = false;
                paste.ShowInTaskbar = false;
                Label help = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 44,
                    Padding = new Padding(8, 6, 8, 0),
                    Text = SwimLang.T("Μία γραμμή για κάθε αθλητή, με τη σειρά που θα κολυμπήσουν (από Excel: ΑΜ, ονοματεπώνυμο, σωματείο· ή μόνο ΑΜ ή μόνο όνομα). Μπαίνουν στο τέλος της σειράς.")
                };
                TextBox text = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = false, Text = clip, Font = new Font("Consolas", 9f) };
                FlowLayoutPanel bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(6) };
                Button ok = new Button { Text = SwimLang.T("Στη σειρά"), DialogResult = DialogResult.OK, Width = 100 };
                Button cancel = new Button { Text = SwimLang.T("Άκυρο"), DialogResult = DialogResult.Cancel, Width = 100 };
                bar.Controls.Add(cancel);
                bar.Controls.Add(ok);
                paste.Controls.Add(text);
                paste.Controls.Add(bar);
                paste.Controls.Add(help);
                paste.CancelButton = cancel;
                if (paste.ShowDialog(this) != DialogResult.OK)
                    return;

                List<SwimTakeAthlete> read = ReadAthleteLines(text.Text, SwimAnalysisManager.Instance.Athletes);
                foreach (SwimTakeAthlete a in read)
                    details.Queue.Add(a);

                ShowQueue();
                ShowPreview();
                if (read.Count == 0)
                    MessageBox.Show(this, SwimLang.T("Δεν διαβάστηκαν αθλητές από το κείμενο."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// The athletes of a pasted list, one per line: a number of the registry (digits), the name, the club (the columns of Excel, or
        /// ";"); what the line does not say is taken from the list of the athletes (by the number, or a name that only one has).
        /// </summary>
        public static List<SwimTakeAthlete> ReadAthleteLines(string text, SwimAthleteList list)
        {
            List<SwimTakeAthlete> result = new List<SwimTakeAthlete>();
            foreach (string raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;

                string[] cells = line.Split(new[] { '\t', ';' }, StringSplitOptions.None).Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
                string id = "";
                List<string> rest = new List<string>();
                foreach (string c in cells)
                {
                    if (id.Length == 0 && c.All(char.IsDigit) && c.Length >= 3)
                        id = c;
                    else
                        rest.Add(c);
                }

                // "123456 ΕΠΩΝΥΜΟ ΟΝΟΜΑ" in one cell: the number first.
                if (id.Length == 0 && rest.Count > 0)
                {
                    string[] words = rest[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length > 1 && words[0].All(char.IsDigit) && words[0].Length >= 3)
                    {
                        id = words[0];
                        rest[0] = string.Join(" ", words.Skip(1));
                    }
                }

                string name = rest.Count > 0 ? rest[0] : "";
                string club = rest.Count > 1 ? rest[1] : "";
                SwimAthlete found = null;
                if (list != null)
                {
                    if (id.Length > 0)
                        found = list.Find(id);

                    if (found == null && name.Length > 0)
                    {
                        List<SwimAthlete> matches = list.Search(name, 2);
                        if (matches.Count == 1)
                            found = matches[0];
                    }
                }

                if (found != null)
                    result.Add(new SwimTakeAthlete { AthleteId = found.Id ?? id, Athlete = found.Name ?? name, Club = club.Length > 0 ? club : (found.Club ?? "") });
                else if (name.Length > 0)
                    result.Add(new SwimTakeAthlete { AthleteId = id, Athlete = name, Club = club });
            }

            return result;
        }
        #endregion

        #region Pasted list, analysis, folder
        private void PasteList()
        {
            string clip = "";
            try
            {
                if (Clipboard.ContainsText())
                    clip = Clipboard.GetText();
            }
            catch (Exception) { }

            using (Form paste = new Form())
            {
                paste.Text = SwimLang.T("📋 Επικόλληση αποτελεσμάτων ή λίστας εκκίνησης");
                paste.StartPosition = FormStartPosition.CenterParent;
                paste.ClientSize = new Size(640, 420);
                paste.MinimizeBox = false;
                paste.MaximizeBox = false;
                paste.ShowInTaskbar = false;
                Label help = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 40,
                    Padding = new Padding(8, 6, 8, 0),
                    Text = SwimLang.T("Αντίγραψε τα αποτελέσματα ή τη λίστα εκκίνησης της σειράς (από τη σελίδα ή το PDF) και επικόλλησέ τα εδώ. Ο διάδρομος δίνει τον αθλητή κάθε βίντεο.")
                };
                TextBox text = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = false, Text = clip, Font = new Font("Consolas", 9f) };
                FlowLayoutPanel bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(6) };
                Button ok = new Button { Text = SwimLang.T("Ανάγνωση"), DialogResult = DialogResult.OK, Width = 100 };
                Button cancel = new Button { Text = SwimLang.T("Άκυρο"), DialogResult = DialogResult.Cancel, Width = 100 };
                bar.Controls.Add(cancel);
                bar.Controls.Add(ok);
                paste.Controls.Add(text);
                paste.Controls.Add(bar);
                paste.Controls.Add(help);
                paste.AcceptButton = null;
                paste.CancelButton = cancel;
                if (paste.ShowDialog(this) != DialogResult.OK)
                    return;

                ReadPasted(text.Text);
            }
        }

        private void ReadPasted(string text)
        {
            ReadFields();
            try
            {
                SwimEventInfo ev = SwimResultsPaste.EventFromText(text);
                if (ev != null)
                {
                    if (ev.HasEvent)
                    {
                        details.Distance = ev.Distance;
                        details.Relay = ev.Relay;
                        details.Style = ev.Style;
                    }

                    string round = RoundName(ev.Round);
                    if (round.Length > 0)
                        details.Round = round;

                    if (!string.IsNullOrWhiteSpace(ev.Competition) && string.IsNullOrWhiteSpace(details.Competition))
                        details.Competition = ev.Competition.Trim();

                    if (!string.IsNullOrWhiteSpace(ev.Location) && string.IsNullOrWhiteSpace(details.Location))
                        details.Location = ev.Location.Trim();
                }

                SwimPasteResult result = SwimResultsPaste.Parse(text, Math.Max(50, details.Distance), "", 0);
                details.Swimmers = result.Records
                    .Where(r => r.Lane > 0)
                    .GroupBy(r => r.Lane)
                    .Select(g => g.First())
                    .Select(r => new SwimTakeSwimmer { Lane = r.Lane, Name = (r.FamilyName + " " + r.GivenName).Trim(), Club = r.Club ?? "" })
                    .OrderBy(s => s.Lane)
                    .ToList();
            }
            catch (Exception)
            {
                details.Swimmers = new List<SwimTakeSwimmer>();
            }

            // The swimmers come from the list: the athlete of the registry is left for the lane to choose.
            if (details.Swimmers.Count > 0)
            {
                details.Athlete = "";
                details.AthleteId = "";
                details.Club = "";
            }

            ShowDetails();
            if (details.Swimmers.Count == 0)
                MessageBox.Show(this, SwimLang.T("Δεν διαβάστηκαν κολυμβητές με διάδρομο από το κείμενο."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string RoundName(string round)
        {
            switch (round ?? "")
            {
                case "heats": return SwimLang.T("Προκριματικός");
                case "semi": return SwimLang.T("Ημιτελικός");
                case "final":
                case "timed": return SwimLang.T("Τελικός");
                default: return "";
            }
        }

        /// <summary>The details of the analysis open now (its race): the athlete, the competition, the event.</summary>
        private void FromAnalysis()
        {
            SwimAnalysisManager manager = SwimAnalysisManager.Current;
            SwimRace race = manager != null ? manager.Race : null;
            if (race == null)
            {
                MessageBox.Show(this, SwimLang.T("Δεν υπάρχει ανοιχτή ανάλυση: άνοιξε ένα βίντεο με τα στοιχεία του αγώνα."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ReadFields();
            details.Kind = race.IsTraining ? details.Kind : SwimTakeDetails.KindRace;
            details.AthleteId = race.AthleteId ?? "";
            details.Athlete = race.Athlete ?? "";
            details.Club = race.AthleteClub ?? "";
            details.Competition = race.Competition ?? "";
            details.Location = race.Location ?? "";
            details.Date = race.Date ?? details.Date;
            details.Style = race.Style ?? "fs";
            details.Distance = race.Distance;
            details.Relay = race.Relay;
            details.Round = race.Round ?? "";
            details.Lane = Math.Max(0, Math.Min(10, race.Lane));
            ShowDetails();
        }

        private void ChooseRoot()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = SwimLang.T("Ο φάκελος όπου μπαίνουν οι φάκελοι της κάθε μέρας (π.χ. E:\\).");
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(tbRoot.Text.Trim()))
                    dialog.SelectedPath = tbRoot.Text.Trim();

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    tbRoot.Text = dialog.SelectedPath;
            }
        }
        #endregion
    }
}
