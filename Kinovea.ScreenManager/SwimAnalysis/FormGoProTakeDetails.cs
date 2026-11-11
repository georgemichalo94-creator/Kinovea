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
    /// lane from pasted results or a start list). Also the folder of the videos, and the path the next video will get.
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
        private bool updating;

        /// <summary>The details chosen (null when cancelled) and the folder of the videos.</summary>
        public SwimTakeDetails Result { get; private set; }
        public string RootFolder { get; private set; }

        public FormGoProTakeDetails(SwimTakeDetails current, string root, string sampleCamera)
        {
            details = (current ?? new SwimTakeDetails()).Clone();
            this.sampleCamera = string.IsNullOrEmpty(sampleCamera) ? SwimLang.T("Κάμερα") : sampleCamera;

            Text = SwimLang.T("📝 Στοιχεία εγγραφής GoPro");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(660, 740);
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
            lbAthletes.DoubleClick += (s, e) => ChooseSelected(false);
            lbAthletes.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
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

            Controls.Add(table);
            Controls.Add(buttons);

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
