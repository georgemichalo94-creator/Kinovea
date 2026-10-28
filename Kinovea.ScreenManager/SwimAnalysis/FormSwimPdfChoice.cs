#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.11.0) The parts of the PDF of the report, at the PDF button: the short analysis (three pages for the coach), the full one (the
    /// report with all its tables and comments at its end), or both (one PDF, the short first; 2.12.0: or two separate files). The
    /// choice stays for the next PDF and for the package for the coaches.
    /// </summary>
    public class FormSwimPdfChoice : Form
    {
        private readonly CheckBox shortBox;
        private readonly CheckBox fullBox;
        private readonly CheckBox separateBox;
        private readonly SwimButton ok;
        private readonly Label hint;

        public bool Short
        {
            get { return shortBox.Checked; }
        }

        public bool Full
        {
            get { return fullBox.Checked; }
        }

        /// <summary>(2.12.0) Both parts, each in its own file (the short analysis and the full one with its comments).</summary>
        public bool Separate
        {
            get { return separateBox.Checked && Short && Full; }
        }

        public FormSwimPdfChoice(bool shortPart, bool fullPart)
            : this(shortPart, fullPart, false)
        {
        }

        public FormSwimPdfChoice(bool shortPart, bool fullPart, bool separate)
        {
            Text = SwimLang.T("PDF αναφοράς");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(500, 340);
            BackColor = SwimTheme.Back;
            ForeColor = SwimTheme.Text;
            Font = SwimTheme.Ui(9.5f, FontStyle.Regular);
            KeyPreview = true;

            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16, 12, 16, 12), BackColor = SwimTheme.Back };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            t.Controls.Add(new Label
            {
                Text = SwimLang.T("Τι θα έχει το PDF;"),
                AutoSize = true,
                Font = SwimTheme.Ui(11f, FontStyle.Bold),
                ForeColor = SwimTheme.AccentLight,
                Margin = new Padding(0, 0, 0, 8)
            });

            shortBox = Box(SwimLang.T("Σύντομη ανάλυση"), shortPart || !fullPart);
            t.Controls.Add(shortBox);
            t.Controls.Add(Note(SwimLang.T("3 σελίδες για τον προπονητή: ο αγώνας με μια ματιά, εκκίνηση, στροφές, τερματισμός, κολύμβηση, με το ρεκόρ και την αναφορά.")));
            fullBox = Box(SwimLang.T("Πλήρης ανάλυση"), fullPart);
            t.Controls.Add(fullBox);
            t.Controls.Add(Note(SwimLang.T("Όλοι οι πίνακες και τα γραφήματα του αγώνα, με σχόλια στο τέλος (τι έγινε καλά, τι να προσέξουμε, επεξήγηση).")));
            separateBox = Box(SwimLang.T("Σε δύο ξεχωριστά αρχεία"), separate);
            separateBox.Font = SwimTheme.Ui(9.5f, FontStyle.Regular);
            separateBox.Margin = new Padding(0, 6, 0, 0);
            t.Controls.Add(separateBox);

            hint = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(0, 6, 0, 0), MaximumSize = new Size(460, 0) };
            t.Controls.Add(hint);

            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 12, 0, 0) };
            ok = new SwimButton(true) { Text = SwimLang.T("📄 Δημιουργία PDF"), AutoSize = true, Height = 32, Primary = true };
            ok.Click += (s, e) =>
            {
                if (!Short && !Full)
                    return;
                DialogResult = DialogResult.OK;
                Close();
            };
            SwimButton cancel = new SwimButton(true) { Text = SwimLang.T("Άκυρο"), AutoSize = true, Height = 32 };
            cancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            t.Controls.Add(buttons);
            Controls.Add(t);

            shortBox.CheckedChanged += (s, e) => UpdateState();
            fullBox.CheckedChanged += (s, e) => UpdateState();
            separateBox.CheckedChanged += (s, e) => UpdateState();
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
                else if (e.KeyCode == Keys.Enter && (Short || Full))
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };
            UpdateState();
        }

        private static CheckBox Box(string text, bool value)
        {
            return new CheckBox
            {
                Text = text,
                Checked = value,
                AutoSize = true,
                Font = SwimTheme.Ui(10.5f, FontStyle.Bold),
                ForeColor = SwimTheme.Text,
                BackColor = SwimTheme.Back,
                Margin = new Padding(0, 4, 0, 0),
                Cursor = Cursors.Hand
            };
        }

        private static Label Note(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(20, 0, 0, 6), MaximumSize = new Size(440, 0) };
        }

        private void UpdateState()
        {
            ok.Enabled = Short || Full;
            separateBox.Enabled = Short && Full;
            if (Short && Full && separateBox.Checked)
                hint.Text = SwimLang.T("Δύο PDF: η σύντομη ανάλυση και η πλήρης ανάλυση (με σχόλια στο τέλος), το καθένα σε δικό του αρχείο.");
            else if (Short && Full)
                hint.Text = SwimLang.T("Ένα PDF: πρώτα η σύντομη και μετά η πλήρης ανάλυση.");
            else if (Short)
                hint.Text = SwimLang.T("Ένα PDF με τις 3 σελίδες της σύντομης ανάλυσης.");
            else if (Full)
                hint.Text = SwimLang.T("Ένα PDF με την πλήρη ανάλυση, όπως ως τώρα, με σχόλια στο τέλος.");
            else
                hint.Text = SwimLang.T("Διάλεξε τουλάχιστον ένα.");
        }
    }
}
