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
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>(2.19.0) The password of AI pro, once in a session of Kinovea.</summary>
    public class FormSwimAiPassword : Form
    {
        private readonly TextBox box;
        private readonly Label hint;

        public FormSwimAiPassword()
        {
            Text = "AI pro";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(420, 190);
            BackColor = SwimTheme.Back;
            ForeColor = SwimTheme.Text;
            Font = SwimTheme.Ui(9.5f, FontStyle.Regular);

            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16, 12, 16, 12), BackColor = SwimTheme.Back };
            t.Controls.Add(new Label { Text = SwimLang.T("AI pro · ανάλυση με τον Claude"), AutoSize = true, Font = SwimTheme.Ui(11f, FontStyle.Bold), ForeColor = SwimTheme.AccentLight, Margin = new Padding(0, 0, 0, 8) });
            t.Controls.Add(new Label { Text = SwimLang.T("Κωδικός:"), AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(0, 0, 0, 2) });
            box = new TextBox { UseSystemPasswordChar = true, Width = 380, BackColor = SwimTheme.Input, ForeColor = SwimTheme.Text, BorderStyle = BorderStyle.FixedSingle };
            t.Controls.Add(box);
            hint = new Label { AutoSize = true, ForeColor = SwimTheme.Red, Margin = new Padding(0, 4, 0, 0), MaximumSize = new Size(380, 0) };
            t.Controls.Add(hint);

            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 10, 0, 0) };
            SwimButton ok = new SwimButton(true) { Text = SwimLang.T("Είσοδος"), AutoSize = true, Height = 32, Primary = true };
            SwimButton cancel = new SwimButton(true) { Text = SwimLang.T("Άκυρο"), AutoSize = true, Height = 32 };
            ok.Click += (s, e) => TryUnlock();
            cancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            t.Controls.Add(buttons);
            Controls.Add(t);

            AcceptButton = null;
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    TryUnlock();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
            Shown += (s, e) => box.Focus();
        }

        private void TryUnlock()
        {
            int wait = SwimAiGate.WaitSeconds;
            if (wait > 0)
            {
                hint.Text = string.Format(CultureInfo.InvariantCulture, SwimLang.T("Πολλές λάθος προσπάθειες: περίμενε {0} δευτ."), wait);
                return;
            }

            Cursor = Cursors.WaitCursor;
            bool ok = SwimAiGate.Unlock(box.Text);
            Cursor = Cursors.Default;
            if (ok)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            box.Clear();
            box.Focus();
            wait = SwimAiGate.WaitSeconds;
            hint.Text = wait > 0
                ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("Πολλές λάθος προσπάθειες: περίμενε {0} δευτ."), wait)
                : SwimLang.T("Λάθος κωδικός.");
        }
    }

    /// <summary>(2.19.0) The key of the Claude API: typed (pasted) by the owner, kept protected for his Windows user.</summary>
    public class FormSwimAiKey : Form
    {
        private readonly TextBox box;
        private readonly Label hint;

        /// <summary>The key typed (empty: take the key away).</summary>
        public string Key { get; private set; }

        public FormSwimAiKey(bool hasKey)
        {
            Text = SwimLang.T("Κλειδί του Claude API");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520, 250);
            BackColor = SwimTheme.Back;
            ForeColor = SwimTheme.Text;
            Font = SwimTheme.Ui(9.5f, FontStyle.Regular);

            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16, 12, 16, 12), BackColor = SwimTheme.Back };
            t.Controls.Add(new Label { Text = SwimLang.T("Κλειδί του Claude API"), AutoSize = true, Font = SwimTheme.Ui(11f, FontStyle.Bold), ForeColor = SwimTheme.AccentLight, Margin = new Padding(0, 0, 0, 6) });
            t.Controls.Add(new Label
            {
                Text = SwimLang.T("Επικόλλησε το κλειδί από το Claude Console (API keys, αρχίζει με sk-ant-). Μένει μόνο σε αυτόν τον υπολογιστή, κλειδωμένο για τον λογαριασμό σου στα Windows· δεν φαίνεται ποτέ ολόκληρο."),
                AutoSize = true,
                ForeColor = SwimTheme.Dim,
                MaximumSize = new Size(480, 0),
                Margin = new Padding(0, 0, 0, 8)
            });
            box = new TextBox { UseSystemPasswordChar = true, Width = 480, BackColor = SwimTheme.Input, ForeColor = SwimTheme.Text, BorderStyle = BorderStyle.FixedSingle };
            t.Controls.Add(box);
            hint = new Label { AutoSize = true, ForeColor = SwimTheme.Red, Margin = new Padding(0, 4, 0, 0), MaximumSize = new Size(480, 0) };
            t.Controls.Add(hint);

            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 10, 0, 0) };
            SwimButton ok = new SwimButton(true) { Text = SwimLang.T("Αποθήκευση"), AutoSize = true, Height = 32, Primary = true };
            ok.Click += (s, e) => Save();
            buttons.Controls.Add(ok);
            if (hasKey)
            {
                SwimButton remove = new SwimButton(true) { Text = SwimLang.T("Αφαίρεση κλειδιού"), AutoSize = true, Height = 32, Accent = SwimTheme.Red };
                remove.Click += (s, e) =>
                {
                    Key = "";
                    DialogResult = DialogResult.OK;
                    Close();
                };
                buttons.Controls.Add(remove);
            }

            SwimButton cancel = new SwimButton(true) { Text = SwimLang.T("Άκυρο"), AutoSize = true, Height = 32 };
            cancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            buttons.Controls.Add(cancel);
            t.Controls.Add(buttons);
            Controls.Add(t);

            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Save();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
            Shown += (s, e) => box.Focus();
        }

        private void Save()
        {
            string key = (box.Text ?? "").Trim();
            if (!SwimAiKey.LooksValid(key))
            {
                hint.Text = SwimLang.T("Δεν μοιάζει με κλειδί του Claude API (αρχίζει με sk-ant-, χωρίς κενά).");
                return;
            }

            Key = key;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>
    /// (2.19.0) AI pro: the window where the coach chooses what Claude analyses (a start or a turn with its list, all of them, or the
    /// comments of the race), the screens whose frames go, what he gives about the athlete (never the name), the model; it shows the
    /// cost before and after, what was spent in the month, Claude's answers and how often they agree with the coach's own scores.
    /// </summary>
    public class FormSwimAiPro : Form
    {
        /// <summary>Requests at the same time (one per start or turn).</summary>
        private const int Parallel = 3;

        /// <summary>The text of a request without its frames, about (for the estimate).</summary>
        private const int TextPerRequest = 7000;

        private const int AnswerTokens = 3000;

        private readonly SwimAnalysisManager manager;
        private readonly SwimRace race;
        private readonly ComboBox cmbWhat;
        private readonly FlowLayoutPanel screensPanel;
        private readonly List<KeyValuePair<CheckBox, PlayerScreenUserInterface>> screenBoxes = new List<KeyValuePair<CheckBox, PlayerScreenUserInterface>>();
        private readonly ComboBox cmbGender;
        private readonly TextBox txtCategory;
        private readonly TextBox txtAge;
        private readonly TextBox txtHeight;
        private readonly TextBox txtBest;
        private readonly TextBox txtLevel;
        private readonly TextBox txtFocus;
        private readonly ComboBox cmbModel;
        private readonly CheckBox chkHigh;
        private readonly Label lblKey;
        private readonly SwimButton btnKey;
        private readonly Label lblEstimate;
        private readonly Label lblMonth;
        private readonly SwimButton btnRun;
        private readonly Label lblProgress;
        private readonly TextBox txtResult;
        private readonly Label lblAgree;
        private readonly List<Job> jobs = new List<Job>();
        private readonly List<SwimAiCall> calls = new List<SwimAiCall>();
        private bool running;
        private bool cancelling;
        private bool filling;

        private class WhatItem
        {
            public string Kind;
            public SwimTechPhase Phase;
            public SwimTechSheet Sheet;
            public string Text;

            public override string ToString()
            {
                return Text;
            }
        }

        private class Job
        {
            public bool IsRace;
            public SwimTechPhase Phase;
            public SwimTechSheet Sheet;
            public string Title;
            public List<SwimAiMoment> Moments;
            public JObject Body;
            public string Error;
            public bool Flying;
            public bool Done;
            public bool Ok;
        }

        private class GenderItem
        {
            public string Code;
            public string Text;

            public override string ToString()
            {
                return Text;
            }
        }

        public FormSwimAiPro(SwimAnalysisManager manager)
        {
            this.manager = manager;
            race = manager.Race;

            Text = SwimLang.T("AI pro · ανάλυση με τον Claude");
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            // (The laptop of the analysis has about 820 points of height: the window fits, its answers below.)
            ClientSize = new Size(720, 690);
            MinimumSize = new Size(600, 560);
            BackColor = SwimTheme.Back;
            ForeColor = SwimTheme.Text;
            Font = SwimTheme.Ui(9.5f, FontStyle.Regular);

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16, 10, 16, 10), BackColor = SwimTheme.Back };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            root.Controls.Add(new Label { Text = SwimLang.T("AI pro · ανάλυση με τον Claude"), AutoSize = true, Font = SwimTheme.Ui(12f, FontStyle.Bold), ForeColor = SwimTheme.AccentLight, Margin = new Padding(0, 0, 0, 2) });
            root.Controls.Add(new Label { Text = KindText(), AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(0, 0, 0, 4) });

            // Four columns: a label and its field, twice on the rows of the athlete.
            TableLayoutPanel grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            cmbWhat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            AddRow(grid, SwimLang.T("Τι να αναλύσει:"), cmbWhat);
            screensPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 1, 0, 1) };
            AddRow(grid, SwimLang.T("Οθόνες:"), screensPanel);

            Title(grid, SwimLang.T("Ο αθλητής (το όνομα δεν στέλνεται ποτέ)"));
            cmbGender = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 12, 3) };
            cmbGender.Items.Add(new GenderItem { Code = "", Text = "—" });
            cmbGender.Items.Add(new GenderItem { Code = "M", Text = SwimLang.T("Αγόρι – Άνδρας") });
            cmbGender.Items.Add(new GenderItem { Code = "F", Text = SwimLang.T("Κορίτσι – Γυναίκα") });
            txtCategory = Field(false);
            AddPair(grid, SwimLang.T("Φύλο:"), cmbGender, SwimLang.T("Κατηγορία:"), txtCategory);
            txtAge = Field(false);
            txtHeight = Field(false);
            AddPair(grid, SwimLang.T("Ηλικία:"), txtAge, SwimLang.T("Ύψος (εκ.):"), txtHeight);
            txtBest = Field(false);
            txtLevel = Field(false);
            AddPair(grid, SwimLang.T("Ατομικό ρεκόρ:"), txtBest, SwimLang.T("Επίπεδο:"), txtLevel);
            txtFocus = Field(false);
            AddRow(grid, SwimLang.T("Τι να προσέξει:"), txtFocus);

            Title(grid, SwimLang.T("Ο Claude"));

            cmbModel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            foreach (string m in SwimAiModels.All)
                cmbModel.Items.Add(SwimAiModels.Label(m));
            AddRow(grid, SwimLang.T("Μοντέλο:"), cmbModel);
            chkHigh = new CheckBox { Text = SwimLang.T("Υψηλή λεπτομέρεια καρέ (περίπου διπλό κόστος)"), AutoSize = true, ForeColor = SwimTheme.Text, Margin = new Padding(0, 4, 0, 2) };
            AddRow(grid, "", chkHigh);
            FlowLayoutPanel keyRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            lblKey = new Label { AutoSize = true, ForeColor = SwimTheme.Text, Margin = new Padding(0, 8, 8, 0) };
            btnKey = new SwimButton(true) { AutoSize = true, Height = 28 };
            btnKey.Click += (s, e) => AskKey();
            keyRow.Controls.Add(lblKey);
            keyRow.Controls.Add(btnKey);
            AddRow(grid, SwimLang.T("Κλειδί API:"), keyRow);
            root.Controls.Add(grid);

            root.Controls.Add(new Label
            {
                Text = SwimLang.T("Στέλνονται μόνο τα καρέ των οθονών, οι μετρήσεις του Kinovea, η λίστα σου και τα στοιχεία από πάνω: κανένα όνομα. Η Anthropic δεν τα χρησιμοποιεί για εκπαίδευση και τα σβήνει σε 30 ημέρες."),
                AutoSize = true,
                ForeColor = SwimTheme.Faint,
                MaximumSize = new Size(680, 0),
                Margin = new Padding(0, 6, 0, 2)
            });
            lblEstimate = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(0, 2, 0, 0) };
            root.Controls.Add(lblEstimate);
            lblMonth = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(0, 2, 0, 6) };
            root.Controls.Add(lblMonth);

            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 2, 0, 2) };
            btnRun = new SwimButton(true) { Text = SwimLang.T("✨ Ανάλυση με τον Claude"), AutoSize = true, Height = 34, Primary = true };
            btnRun.Click += (s, e) =>
            {
                if (running)
                    CancelAll();
                else
                    Run();
            };
            SwimButton close = new SwimButton(true) { Text = SwimLang.T("Κλείσιμο"), AutoSize = true, Height = 34 };
            close.Click += (s, e) => Close();
            buttons.Controls.Add(btnRun);
            buttons.Controls.Add(close);
            root.Controls.Add(buttons);
            lblProgress = new Label { AutoSize = true, ForeColor = SwimTheme.Amber, Margin = new Padding(0, 2, 0, 4), MaximumSize = new Size(660, 0) };
            root.Controls.Add(lblProgress);

            txtResult = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = SwimTheme.Input,
                ForeColor = SwimTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                WordWrap = true,
                Margin = new Padding(0, 2, 0, 4)
            };
            root.RowCount = root.Controls.Count + 2;
            root.Controls.Add(txtResult);
            lblAgree = new Label { AutoSize = true, ForeColor = SwimTheme.Green, Margin = new Padding(0, 2, 0, 0), MaximumSize = new Size(660, 0) };
            root.Controls.Add(lblAgree);
            for (int i = 0; i < root.Controls.Count; i++)
                root.RowStyles.Add(root.Controls[i] == txtResult ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            Controls.Add(root);
            SwimTheme.Apply(this);

            Fill();
            cmbWhat.SelectedIndexChanged += (s, e) => RefreshEstimate();
            cmbModel.SelectedIndexChanged += (s, e) =>
            {
                if (!filling)
                    RefreshEstimate();
            };
            chkHigh.CheckedChanged += (s, e) =>
            {
                if (!filling)
                    RefreshEstimate();
            };
            FormClosing += (s, e) =>
            {
                if (running)
                    CancelAll();
                SaveInputs();
            };
        }

        #region Filling
        private string KindText()
        {
            if (race == null)
                return "";
            string style = SwimPresets.StyleName(race.Style);
            if (race.IsTraining)
                return string.Format(CultureInfo.InvariantCulture, SwimLang.T("Τεχνική ανάλυση (προπόνηση) · {0} · πισίνα {1}"), style, race.PoolLabel);
            return string.Format(CultureInfo.InvariantCulture, SwimLang.T("Αγώνας · {0}{1} {2} · πισίνα {3}"), race.DistanceLabel, race.IsYards ? "yd" : SwimLang.T("μ."), style, race.PoolLabel);
        }

        private void Fill()
        {
            filling = true;
            try
            {
                FillWhat(null);

                screensPanel.Controls.Clear();
                screenBoxes.Clear();
                List<PlayerScreenUserInterface> screens = manager.AiScreens();
                foreach (PlayerScreenUserInterface s in screens)
                {
                    CheckBox box = new CheckBox
                    {
                        Text = SwimLang.T("Οθόνη ") + SwimAnalysisManager.AiScreenNumber(s, screens).ToString(CultureInfo.InvariantCulture),
                        Checked = true,
                        AutoSize = true,
                        ForeColor = SwimTheme.Text,
                        Margin = new Padding(0, 4, 14, 2)
                    };
                    box.CheckedChanged += (o, e) => RefreshEstimate();
                    screenBoxes.Add(new KeyValuePair<CheckBox, PlayerScreenUserInterface>(box, s));
                    screensPanel.Controls.Add(box);
                }

                SwimAiAthlete a = race != null ? race.AiAthlete : null;
                if (a == null && race != null)
                    a = new SwimAiAthlete { Gender = SwimTechEvaluator.GenderCode(race.AthleteGender) ?? "", Category = race.AthleteCategory ?? "" };
                if (a == null)
                    a = new SwimAiAthlete();
                string g = SwimTechEvaluator.GenderCode(a.Gender) ?? "";
                cmbGender.SelectedItem = cmbGender.Items.Cast<GenderItem>().FirstOrDefault(x => x.Code == g) ?? cmbGender.Items[0];
                txtCategory.Text = a.Category ?? "";
                txtAge.Text = a.Age ?? "";
                txtHeight.Text = a.Height ?? "";
                txtBest.Text = a.Best ?? "";
                txtLevel.Text = a.Level ?? "";
                txtFocus.Text = a.Focus ?? "";

                SwimSettings settings = manager.Settings;
                int model = Array.IndexOf(SwimAiModels.All, settings != null ? settings.AiProModel : SwimAiModels.Opus);
                cmbModel.SelectedIndex = model >= 0 ? model : 0;
                chkHigh.Checked = settings != null && settings.AiProHighDetail;
            }
            finally
            {
                filling = false;
            }

            RefreshKey();
            RefreshEstimate();
            RefreshMonth();
            RefreshAgreement();
            if (cmbWhat.Items.Count == 0)
            {
                btnRun.Enabled = false;
                lblProgress.Text = race != null && race.IsTraining
                    ? SwimLang.T("Δεν υπάρχει ακόμη εκκίνηση ή στροφή με λίστα: σημάδεψε πρώτα το σήμα της εκκίνησης ή την επαφή με τον τοίχο.")
                    : SwimLang.T("Δεν υπάρχει ακόμη τίποτα να αναλυθεί: σημάδεψε πρώτα την εκκίνηση του αγώνα.");
            }
        }

        /// <summary>What can be analysed: each start and turn with its list (marked when Claude answered it), all of them, the race. The choice stays.</summary>
        private void FillWhat(WhatItem was)
        {
            cmbWhat.Items.Clear();
            List<SwimTechPhase> phases = manager.AiPhases();
            SwimTechPhase now = manager.AiCurrentPhase();
            WhatItem select = null;
            foreach (SwimTechPhase p in phases)
            {
                SwimTechSheet sheet = manager.TechSheetFor(p);
                SwimTechEval eval = manager.TechEval(p, sheet, false);
                WhatItem item = new WhatItem
                {
                    Kind = "phase",
                    Phase = p,
                    Sheet = sheet,
                    Text = p.Title + " · " + sheet.Title.Text + (eval != null && eval.Ai != null ? "  " + SwimLang.T("(έχει ήδη απάντηση AI)") : "")
                };
                cmbWhat.Items.Add(item);
                if (now != null && p.Id == now.Id)
                    select = item;
            }

            if (phases.Count >= 2)
                cmbWhat.Items.Add(new WhatItem { Kind = "all", Text = string.Format(CultureInfo.InvariantCulture, SwimLang.T("Όλες οι εκκινήσεις και οι στροφές ({0})"), phases.Count) });
            if (race != null && !race.IsTraining && race.StartTimestamp.HasValue)
                cmbWhat.Items.Add(new WhatItem { Kind = "race", Text = SwimLang.T("Σχόλια του αγώνα για την αναφορά (όλος ο αγώνας)") });

            WhatItem same = was == null ? null : cmbWhat.Items.Cast<WhatItem>().FirstOrDefault(x => x.Kind == was.Kind && (x.Phase == null ? was.Phase == null : was.Phase != null && x.Phase.Id == was.Phase.Id));
            if (cmbWhat.Items.Count > 0)
                cmbWhat.SelectedItem = same ?? select ?? cmbWhat.Items[0];
        }

        private static TextBox Field(bool multiline)
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = multiline,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
                BackColor = SwimTheme.Input,
                ForeColor = SwimTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 3, 0, 3)
            };
        }

        /// <summary>A row: its label and a field over the other three columns.</summary>
        private static void AddRow(TableLayoutPanel grid, string label, Control control)
        {
            int row = grid.RowCount;
            grid.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(0, 7, 10, 3) }, 0, row);
            grid.Controls.Add(control, 1, row);
            grid.SetColumnSpan(control, 3);
            grid.RowCount = row + 1;
        }

        /// <summary>A row of two labels and their fields.</summary>
        private static void AddPair(TableLayoutPanel grid, string label1, Control control1, string label2, Control control2)
        {
            int row = grid.RowCount;
            grid.Controls.Add(new Label { Text = label1, AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(0, 7, 10, 3) }, 0, row);
            grid.Controls.Add(control1, 1, row);
            grid.Controls.Add(new Label { Text = label2, AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(0, 7, 10, 3) }, 2, row);
            grid.Controls.Add(control2, 3, row);
            grid.RowCount = row + 1;
        }

        /// <summary>The title of a part, over the four columns.</summary>
        private static void Title(TableLayoutPanel grid, string text)
        {
            Label title = new Label { Text = text, AutoSize = true, Font = SwimTheme.Ui(9.5f, FontStyle.Bold), ForeColor = SwimTheme.Accent, Margin = new Padding(0, 8, 0, 1) };
            int row = grid.RowCount;
            grid.Controls.Add(title, 0, row);
            grid.SetColumnSpan(title, 4);
            grid.RowCount = row + 1;
        }

        private string SelectedModel
        {
            get
            {
                int i = cmbModel.SelectedIndex;
                return i >= 0 && i < SwimAiModels.All.Length ? SwimAiModels.All[i] : SwimAiModels.Opus;
            }
        }

        private int Edge
        {
            get { return chkHigh.Checked ? SwimAnalysisManager.AiEdgeHigh : SwimAnalysisManager.AiEdgeNormal; }
        }

        private List<PlayerScreenUserInterface> ChosenScreens()
        {
            return screenBoxes.Where(p => p.Key.Checked && p.Value != null && !p.Value.IsDisposed).Select(p => p.Value).ToList();
        }

        private SwimAiAthlete Athlete()
        {
            GenderItem g = cmbGender.SelectedItem as GenderItem;
            return new SwimAiAthlete
            {
                Gender = g != null ? g.Code : "",
                Category = txtCategory.Text.Trim(),
                Age = txtAge.Text.Trim(),
                Height = txtHeight.Text.Trim(),
                Best = txtBest.Text.Trim(),
                Level = txtLevel.Text.Trim(),
                Focus = txtFocus.Text.Trim(),
            };
        }

        private void SaveInputs()
        {
            if (race != null && race == manager.Race)
                manager.SetAiAthlete(Athlete());
            SwimSettings settings = manager.Settings;
            if (settings == null)
                return;
            string model = SelectedModel;
            if (settings.AiProModel != model || settings.AiProHighDetail != chkHigh.Checked)
            {
                settings.AiProModel = model;
                settings.AiProHighDetail = chkHigh.Checked;
                settings.Save();
            }
        }
        #endregion

        #region State
        private void RefreshKey()
        {
            string key = manager.AiKey;
            lblKey.Text = string.IsNullOrEmpty(key) ? SwimLang.T("δεν υπάρχει ακόμη") : SwimAiKey.Masked(key);
            lblKey.ForeColor = string.IsNullOrEmpty(key) ? SwimTheme.Amber : SwimTheme.Text;
            btnKey.Text = string.IsNullOrEmpty(key) ? SwimLang.T("Βάλε το κλειδί…") : SwimLang.T("Αλλαγή…");
        }

        private bool AskKey()
        {
            bool has = !string.IsNullOrEmpty(manager.AiKey);
            using (FormSwimAiKey f = new FormSwimAiKey(has))
            {
                if (f.ShowDialog(this) != DialogResult.OK)
                    return false;
                manager.SetAiKey(f.Key);
            }

            RefreshKey();
            return !string.IsNullOrEmpty(manager.AiKey);
        }

        /// <summary>The cost of what is chosen, before it goes: its frames (moments × screens) and its texts.</summary>
        private void RefreshEstimate()
        {
            if (filling)
                return;
            WhatItem what = cmbWhat.SelectedItem as WhatItem;
            int screens = ChosenScreens().Count;
            if (what == null || screens == 0)
            {
                lblEstimate.Text = screens == 0 && what != null ? SwimLang.T("Διάλεξε τουλάχιστον μία οθόνη.") : "";
                return;
            }

            int requests, frames;
            Count(what, screens, out requests, out frames);
            double usd = Estimate(requests, frames);
            lblEstimate.Text = string.Format(CultureInfo.InvariantCulture, SwimLang.T("Εκτίμηση: {0} καρέ σε {1} αίτηση(-εις) · ≈ {2}"), frames, requests, SwimAiModels.Dollars(usd));
        }

        private void Count(WhatItem what, int screens, out int requests, out int frames)
        {
            requests = 0;
            frames = 0;
            int max = manager.Settings != null ? manager.Settings.AiProMoments : 16;
            if (what.Kind == "race")
            {
                requests = 1;
                frames = manager.AiRaceMoments(14).Count * screens;
                return;
            }

            List<SwimTechPhase> phases = what.Kind == "all" ? manager.AiPhases() : new List<SwimTechPhase> { what.Phase };
            foreach (SwimTechPhase p in phases)
            {
                SwimTechSheet sheet = what.Kind == "all" ? manager.TechSheetFor(p) : what.Sheet;
                if (sheet == null)
                    continue;
                requests++;
                frames += manager.AiTechMoments(p, sheet, max).Count * screens;
            }
        }

        private double Estimate(int requests, int frames)
        {
            int edge = Edge;
            int w = edge, h = (int)Math.Round(edge * 9 / 16.0);
            List<SwimAiImage> images = Enumerable.Range(0, frames).Select(i => new SwimAiImage { Width = w, Height = h }).ToList();
            long tokens;
            double usd = SwimAiModels.Estimate(SelectedModel, images, TextPerRequest * requests, AnswerTokens * requests, out tokens);
            return usd + SwimAiModels.Usd(SelectedModel, 1800L * Math.Max(0, requests - 1), 0);
        }

        private void RefreshMonth()
        {
            SwimSettings settings = manager.Settings;
            SwimAiUsage month = SwimAiBook.Month(settings != null ? settings.AiProUsage : null, DateTime.Now);
            double limit = settings != null ? settings.AiProMonthLimit : 95;
            lblMonth.Text = string.Format(CultureInfo.InvariantCulture, SwimLang.T("Αυτόν τον μήνα σε αυτό το Kinovea: {0} αιτήσεις · {1} από τα {2} του Max (όριο {3})"),
                month.Requests, SwimAiModels.Dollars(month.Usd), SwimAiModels.Dollars(SwimAiModels.MonthlyCredits), SwimAiModels.Dollars(limit));
        }

        private void RefreshAgreement()
        {
            int same, total;
            SwimAiApply.Agreement(manager.Race, out same, out total);
            int allSame = 0, all = 0;
            SwimAiCalibration calibration = SwimAnalysisManager.AiCalibration();
            if (calibration != null)
                calibration.Totals(out allSame, out all);
            if (total == 0 && all == 0)
            {
                lblAgree.Text = SwimLang.T("Η συμφωνία του Claude με σένα θα φανεί όταν βαθμολογήσεις σημεία που έκρινε κι εκείνος (επιβεβαίωση ή αλλαγή των AI).");
                lblAgree.ForeColor = SwimTheme.Dim;
                return;
            }

            lblAgree.ForeColor = SwimTheme.Green;
            lblAgree.Text = string.Format(CultureInfo.InvariantCulture, SwimLang.T("Συμφωνία του Claude με τις βαθμολογίες σου: σε αυτό το βίντεο {0} · σε όλα τα βίντεο {1}"),
                Ratio(same, total), Ratio(allSame, all));
        }

        private static string Ratio(int same, int total)
        {
            if (total == 0)
                return "—";
            return string.Format(CultureInfo.InvariantCulture, "{0}/{1} ({2:0}%)", same, total, 100.0 * same / total);
        }

        private void SetRunning(bool on)
        {
            running = on;
            btnRun.Text = on ? SwimLang.T("Ακύρωση") : SwimLang.T("✨ Ανάλυση με τον Claude");
            btnRun.Primary = !on;
            cmbWhat.Enabled = !on;
            screensPanel.Enabled = !on;
            cmbModel.Enabled = !on;
            chkHigh.Enabled = !on;
            btnKey.Enabled = !on;
            Cursor = on ? Cursors.AppStarting : Cursors.Default;
        }

        private void Progress(string text)
        {
            lblProgress.Text = text;
            lblProgress.Refresh();
        }

        private void Append(string text)
        {
            if (txtResult.TextLength > 0)
                txtResult.AppendText(Environment.NewLine + Environment.NewLine);
            txtResult.AppendText((text ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
        }
        #endregion

        #region The analysis
        private void Run()
        {
            WhatItem what = cmbWhat.SelectedItem as WhatItem;
            if (what == null || race == null || race != manager.Race)
                return;

            List<PlayerScreenUserInterface> screens = ChosenScreens();
            if (screens.Count == 0)
            {
                lblProgress.Text = SwimLang.T("Διάλεξε τουλάχιστον μία οθόνη.");
                return;
            }

            string key = manager.AiKey;
            if (string.IsNullOrEmpty(key))
            {
                if (!AskKey())
                    return;
                key = manager.AiKey;
                if (string.IsNullOrEmpty(key))
                    return;
            }

            SaveInputs();
            string model = SelectedModel;
            SwimAiAthlete athlete = Athlete();

            // The month's limit: nothing goes when this would pass it.
            int requests, frames;
            Count(what, screens.Count, out requests, out frames);
            double estimate = Estimate(requests, frames);
            SwimSettings settings = manager.Settings;
            SwimAiUsage month = SwimAiBook.Month(settings.AiProUsage, DateTime.Now);
            if (month.Usd + estimate > settings.AiProMonthLimit)
            {
                MessageBox.Show(this, string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("Με αυτή την ανάλυση (≈ {0}) θα περνούσες το όριο του μήνα ({1}· ξοδεύτηκαν {2}). Δεν στάλθηκε τίποτα."),
                    SwimAiModels.Dollars(estimate), SwimAiModels.Dollars(settings.AiProMonthLimit), SwimAiModels.Dollars(month.Usd)),
                    "AI pro", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            jobs.Clear();
            if (what.Kind == "race")
            {
                jobs.Add(new Job { IsRace = true, Title = SwimLang.T("Σχόλια του αγώνα") });
            }
            else
            {
                List<SwimTechPhase> phases = what.Kind == "all" ? manager.AiPhases() : new List<SwimTechPhase> { what.Phase };
                foreach (SwimTechPhase p in phases)
                {
                    SwimTechSheet sheet = what.Kind == "all" ? manager.TechSheetFor(p) : what.Sheet;
                    if (sheet != null)
                        jobs.Add(new Job { Phase = p, Sheet = sheet, Title = p.Title + " · " + sheet.Title.Text });
                }
            }

            if (jobs.Count == 0)
                return;

            SetRunning(true);
            txtResult.Clear();
            int edge = Edge;
            int max = settings.AiProMoments;

            // The frames first (the screens move to each moment and go back after), then the requests.
            for (int i = 0; i < jobs.Count; i++)
            {
                Job j = jobs[i];
                string head = jobs.Count > 1 ? string.Format(CultureInfo.InvariantCulture, "({0}/{1}) ", i + 1, jobs.Count) : "";
                Progress(head + SwimLang.T("Λήψη καρέ: ") + j.Title + "…");
                try
                {
                    j.Moments = j.IsRace ? manager.AiRaceMoments(14) : manager.AiTechMoments(j.Phase, j.Sheet, max);
                    if (j.Moments.Count == 0)
                    {
                        j.Error = SwimLang.T("Δεν βρέθηκαν στιγμές στο βίντεο (λείπουν τα σημάδια).");
                        continue;
                    }

                    List<SwimAiImage> images = manager.AiCapture(j.Moments, screens, edge, (done, total) =>
                        Progress(head + SwimLang.T("Λήψη καρέ: ") + j.Title + string.Format(CultureInfo.InvariantCulture, " · {0}/{1}", done, total)));
                    if (images.Count == 0)
                    {
                        j.Error = SwimLang.T("Δεν πάρθηκαν καρέ από τις οθόνες.");
                        continue;
                    }

                    j.Body = j.IsRace
                        ? manager.AiRaceRequest(athlete, j.Moments, images, screens.Count, model)
                        : manager.AiTechRequest(j.Phase, j.Sheet, athlete, j.Moments, images, screens.Count, model);
                }
                catch (Exception e)
                {
                    j.Error = e.Message;
                }
            }

            foreach (Job j in jobs.Where(x => x.Body == null))
            {
                j.Done = true;
                Append("━━ " + j.Title + " ━━" + Environment.NewLine + "⚠ " + (j.Error ?? SwimLang.T("Δεν στάλθηκε.")));
            }

            cancelling = false;
            StartNext(key);
            if (calls.Count == 0)
                Finish();
        }

        private void StartNext(string key)
        {
            int flying = jobs.Count(j => j.Flying);
            foreach (Job j in jobs.Where(x => x.Body != null && !x.Done && !x.Flying).ToList())
            {
                if (flying >= Parallel || cancelling)
                    break;
                flying++;
                j.Flying = true;
                SwimAiCall call = new SwimAiCall();
                calls.Add(call);
                Job job = j;
                call.Start(key, job.Body, reply => Answered(key, call, job, reply));
            }

            ShowWaiting();
        }

        private void ShowWaiting()
        {
            int waiting = jobs.Count(j => !j.Done);
            if (waiting > 0 && !cancelling)
                Progress(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Ο Claude αναλύει… (απομένουν {0} · συνήθως 30–90 δευτ. η καθεμιά)"), waiting));
        }

        private void Answered(string key, SwimAiCall call, Job job, SwimAiReply reply)
        {
            // The cost first, even when the window was closed.
            manager.AiRecord(reply);
            calls.Remove(call);
            job.Flying = false;
            job.Done = true;
            job.Ok = reply.Ok;
            job.Body = null;
            if (IsDisposed)
                return;

            List<string> lines = new List<string>();
            lines.Add("━━ " + job.Title + " ━━");
            if (!reply.Ok)
            {
                lines.Add("⚠ " + (reply.Error ?? SwimLang.T("Χωρίς απάντηση.")));
            }
            else if (job.IsRace)
            {
                SwimAiRaceAnswer answer = SwimAiApply.ParseRace(reply.Input);
                bool applied = manager.AiApplyRace(race, answer, reply.Model);
                lines.Add(applied ? SwimLang.T("Τα σχόλια μπήκαν στις σημειώσεις της ανάλυσης (για την αναφορά).") : SwimLang.T("⚠ Η ανάλυση άλλαξε: τα σχόλια δεν μπήκαν."));
                lines.Add(Body(SwimAiApply.RaceBlock(answer, reply.Model, "")));
            }
            else
            {
                SwimAiTechAnswer answer = SwimAiApply.ParseTech(reply.Input, job.Sheet, job.Moments != null ? job.Moments.Count : 0);
                SwimAiApplied applied = manager.AiApplyTech(race, job.Phase.Id, job.Sheet.Id, answer, reply.Model);
                if (applied == null)
                {
                    lines.Add(SwimLang.T("⚠ Η ανάλυση άλλαξε: οι απαντήσεις δεν μπήκαν στη λίστα."));
                }
                else
                {
                    List<string> parts = new List<string>();
                    parts.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} προτάσεις AI στη λίστα (περιμένουν την επιβεβαίωσή σου)"), applied.Proposed));
                    parts.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} λάθη"), applied.Errors));
                    if (applied.NotSeen > 0)
                        parts.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} δεν φαίνονται"), applied.NotSeen));
                    if (applied.Unsure > 0)
                        parts.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} αβέβαια"), applied.Unsure));
                    if (applied.Coach > 0)
                        parts.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} με δική σου βαθμολογία (έμεινε)"), applied.Coach));
                    if (applied.Measured > 0)
                        parts.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} μετρημένα 🤖 (έμειναν)"), applied.Measured));
                    lines.Add(string.Join(" · ", parts.ToArray()));
                }

                lines.Add(Body(SwimAiApply.TechBlock(job.Sheet, answer, reply.Model, "")));
            }

            if (reply.InputTokens > 0 || reply.OutputTokens > 0)
                lines.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Κόστος: {0:N0} + {1:N0} tokens ≈ {2} ({3})"), reply.InputTokens, reply.OutputTokens,
                    SwimAiModels.Dollars(reply.Usd), SwimAiModels.Short(reply.Model)));
            Append(string.Join(Environment.NewLine, lines.ToArray()));
            RefreshMonth();
            RefreshAgreement();

            if (!cancelling && jobs.Any(j => !j.Done && !j.Flying))
                StartNext(key);
            else
                ShowWaiting();
            if (calls.Count == 0)
                Finish();
        }

        /// <summary>A block of AI pro without its first line (the model and the date).</summary>
        private static string Body(string block)
        {
            string[] lines = (block ?? "").Replace("\r\n", "\n").Split('\n');
            return string.Join(Environment.NewLine, lines.Skip(1).ToArray());
        }

        private void Finish()
        {
            bool wasCancelling = cancelling;
            cancelling = false;
            SetRunning(false);
            int ok = jobs.Count(j => j.Ok);
            if (wasCancelling)
                Progress(SwimLang.T("Ακυρώθηκε."));
            else if (ok == 0 && jobs.Count > 0)
                Progress(SwimLang.T("Δεν ήρθε απάντηση: δες τα μηνύματα παρακάτω."));
            else if (jobs.Count > 0)
                Progress(SwimLang.T("Έτοιμο. Οι προτάσεις AI είναι στη λίστα της αξιολόγησης: επιβεβαίωσέ τες ή άλλαξέ τες (Τεχνική › Αξιολόγηση)."));
            filling = true;
            try
            {
                FillWhat(cmbWhat.SelectedItem as WhatItem);
            }
            finally
            {
                filling = false;
            }

            RefreshEstimate();
        }

        private void CancelAll()
        {
            cancelling = true;
            foreach (Job j in jobs.Where(x => !x.Done && !x.Flying))
            {
                j.Done = true;
                j.Body = null;
            }

            foreach (SwimAiCall c in calls.ToList())
                c.Cancel();
            Progress(SwimLang.T("Ακύρωση…"));
            if (calls.Count == 0)
                Finish();
        }
        #endregion
    }
}
