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

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// "📋 Επικόλληση αποτελεσμάτων" (since 2.8.1): the analyst pastes the swimmer's row (and his splits) copied from a results page
    /// that the program cannot read itself; what was read is shown as he types, the swimmer chosen when the text has several.
    /// </summary>
    public class SwimPasteDialog : Form
    {
        private readonly TextBox text;
        private readonly ComboBox who;
        private readonly Label whoLabel;
        private readonly Label preview;
        private readonly Button ok;
        private readonly int distance;
        private readonly string athlete;
        private readonly int lane;
        private readonly Timer readTimer;
        private SwimPasteResult result;
        private bool filling;

        /// <summary>The swimmer chosen (null: none) and whether by the race's athlete's name.</summary>
        public SwimPasteRecord Chosen { get; private set; }
        public bool ChosenByName { get; private set; }

        public SwimPasteDialog(string intro, string initial, int distance, string athlete, int lane)
        {
            this.distance = distance;
            this.athlete = athlete ?? "";
            this.lane = lane;

            Text = SwimLang.T("📋 Επικόλληση αποτελεσμάτων");
            StartPosition = FormStartPosition.CenterParent;
            Width = 780;
            Height = 560;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            Label help = new Label
            {
                Dock = DockStyle.Top,
                Height = string.IsNullOrEmpty(intro) ? 62 : 122,
                Padding = new Padding(8, 8, 8, 0),
                Text = (string.IsNullOrEmpty(intro) ? "" : intro + "\n") +
                    SwimLang.T("Στη σελίδα των αποτελεσμάτων μαρκάρισε τη γραμμή του αθλητή (και τους ενδιάμεσους, αν τους ανοίξεις), Ctrl+C, και επικόλλησέ τη εδώ (Ctrl+V). ") +
                    SwimLang.T("Μπορείς να επικολλήσεις και όλο τον πίνακα: ο αθλητής βρίσκεται από το όνομα ή τον διάδρομο του αγώνα.")
            };

            text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                MaxLength = 0,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                AcceptsReturn = true,
                AcceptsTab = true,
                Font = new Font(FontFamily.GenericMonospace, 9f)
            };

            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(8, 4, 8, 0) };
            TableLayoutPanel whoRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 30, ColumnCount = 2 };
            whoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            whoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            whoLabel = new Label { Text = SwimLang.T("Αθλητής:"), AutoSize = true, Margin = new Padding(0, 7, 6, 0) };
            who = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            whoRow.Controls.Add(whoLabel, 0, 0);
            whoRow.Controls.Add(who, 1, 0);
            preview = new Label { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(0, 4, 0, 0) };

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            ok = new Button { Text = SwimLang.T("Συνέχεια"), DialogResult = DialogResult.OK, AutoSize = true, Enabled = false };
            Button cancel = new Button { Text = SwimLang.T("Άκυρο"), DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            bottom.Controls.Add(preview);
            bottom.Controls.Add(whoRow);
            Controls.Add(text);
            Controls.Add(help);
            Controls.Add(bottom);
            Controls.Add(buttons);
            CancelButton = cancel;

            // Read again a moment after the typing stops (a whole page pasted is read once).
            readTimer = new Timer { Interval = 250 };
            readTimer.Tick += (s, e) =>
            {
                readTimer.Stop();
                Read();
            };
            text.TextChanged += (s, e) =>
            {
                readTimer.Stop();
                readTimer.Start();
            };
            who.SelectedIndexChanged += (s, e) =>
            {
                if (!filling)
                    ShowRecord(Selected(), false);
            };
            ok.Click += (s, e) =>
            {
                if (readTimer.Enabled)
                {
                    readTimer.Stop();
                    Read();
                }

                if (Chosen == null || !Chosen.Time.HasValue)
                    DialogResult = DialogResult.None;
            };

            if (!string.IsNullOrEmpty(initial))
                text.Text = initial;
            readTimer.Stop();
            Read();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                readTimer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            text.Focus();
            text.SelectionStart = text.TextLength;
        }

        /// <summary>The text read again: the swimmers in the list, the race's one chosen when found.</summary>
        private void Read()
        {
            result = SwimResultsPaste.Parse(text.Text, distance, athlete, lane);
            filling = true;
            who.Items.Clear();
            bool ask = result.Best < 0 && result.Records.Count > 1;
            if (ask)
                who.Items.Add(SwimLang.T("— διάλεξε τον αθλητή του βίντεο —"));
            foreach (SwimPasteRecord r in result.Records)
                who.Items.Add(r.ToString());
            if (who.Items.Count > 0)
                who.SelectedIndex = ask ? 0 : Math.Max(0, result.Best);
            filling = false;
            who.Visible = whoLabel.Visible = result.Records.Count > 1;
            ShowRecord(Selected(), result.Best >= 0 && result.Why == "name");
        }

        /// <summary>The swimmer of the list (null: none chosen).</summary>
        private SwimPasteRecord Selected()
        {
            if (result == null || result.Records.Count == 0)
                return null;
            int i = who.SelectedIndex - (result.Best < 0 && result.Records.Count > 1 ? 1 : 0);
            if (result.Records.Count == 1)
                i = 0;
            return i >= 0 && i < result.Records.Count ? result.Records[i] : null;
        }

        private void ShowRecord(SwimPasteRecord r, bool byName)
        {
            Chosen = r;
            ChosenByName = r != null && (byName || (athlete.Trim().Length > 0 && SwimResultsPaste.NameScore(athlete, r) >= SwimNameMatch.SameName));
            ok.Enabled = r != null && r.Time.HasValue;
            preview.ForeColor = ok.Enabled ? SystemColors.ControlText : Color.FromArgb(170, 70, 30);

            if (result == null || result.Records.Count == 0)
            {
                preview.Text = text.Text.Trim().Length == 0 ? SwimLang.T("Επικόλλησε εδώ τα αποτελέσματα.")
                    : SwimLang.T("Δεν βρέθηκε αθλητής με χρόνο: αντίγραψε τη γραμμή του αθλητή (όνομα, χρόνος) από τη σελίδα των αποτελεσμάτων.");
                return;
            }

            if (r == null)
            {
                preview.Text = string.Format(CultureInfo.InvariantCulture, SwimLang.T("Βρέθηκαν {0} αθλητές: διάλεξε ποιος είναι ο αθλητής του βίντεο."), result.Records.Count);
                return;
            }

            List<string> lines = new List<string>();
            string about = string.Join(" · ", new[] { r.Name, r.Nation, r.BirthYear > 0 ? r.BirthYear.ToString(CultureInfo.InvariantCulture) : "", r.Club }
                .Where(x => !string.IsNullOrEmpty(x)).ToArray());
            lines.Add(about);
            List<string> second = new List<string>();
            if (r.Lane >= 0)
                second.Add(SwimLang.T("Διάδρομος ") + r.Lane.ToString(CultureInfo.InvariantCulture));
            if (r.Reaction.HasValue)
                second.Add(SwimLang.T("αντίδραση ") + r.Reaction.Value.ToString("0.00", CultureInfo.InvariantCulture));
            if (second.Count > 0)
                lines.Add(string.Join(" · ", second.ToArray()));

            if (r.Time.HasValue)
            {
                string times = string.Join(" ", r.Splits.Select(SwimResultsPaste.FormatTime).Concat(new[] { SwimResultsPaste.FormatTime(r.Time.Value) }).ToArray());
                lines.Add(SwimLang.T("Επίσημοι χρόνοι: ") + times);
                if (r.SplitsMissing)
                    lines.Add(SwimLang.T("Δεν βρέθηκαν όλοι οι ενδιάμεσοι των 50μ.: μπαίνει μόνο ο τελικός χρόνος (άνοιξε τους ενδιάμεσους στη σελίδα και αντίγραψέ τους μαζί, αν θέλεις και αυτούς)."));
            }
            else if (r.TimeChoices.Count > 1)
                lines.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Βρέθηκαν {0} χρόνοι που μπορεί να είναι ο τελικός ({1}): σβήσε από το κείμενο όποιον δεν είναι ο χρόνος του αγώνα (π.χ. χρόνο συμμετοχής)."),
                    r.TimeChoices.Count, string.Join(", ", r.TimeChoices.Select(SwimResultsPaste.FormatTime).ToArray())));
            else
                lines.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Δεν βρέθηκε τελικός χρόνος για τα {0}μ."), distance) +
                    (r.Status.Length > 0 ? " (" + r.Status + ")" : ""));

            // Another swimmer than the race's athlete (a row of the wrong line, the only one pasted).
            if (athlete.Trim().Length > 0 && SwimResultsPaste.NameScore(athlete, r) < SwimResultsPaste.OtherName)
                lines.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("⚠ Δεν μοιάζει με τον αθλητή του αγώνα («{0}»): έλεγξε ότι είναι η σωστή γραμμή."), athlete.Trim()));

            if (result.Best >= 0 && result.Records.Count > 1)
                lines.Add(result.Why == "name" ? SwimLang.T("(βρέθηκε από το όνομα του αθλητή του αγώνα)") :
                    result.Why == "lane" ? SwimLang.T("(βρέθηκε από τον διάδρομο του αγώνα)") : "");
            preview.Text = string.Join("\n", lines.Where(x => x.Length > 0).ToArray());
        }
    }
}
