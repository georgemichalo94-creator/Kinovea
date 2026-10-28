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
    /// (2.13.0) The page «Αξιολόγηση»: a start or a turn scored with a sheet of criteria (✓ / ✗ / – for each point, by phase of the
    /// movement), the program's measurements beside each point (🤖 fills the ones it measures until the coach confirms them), the
    /// jump to the frame of each point, the totals and the level.
    /// </summary>
    public partial class FormSwimAnalysis
    {
        private SwimPage tabTechEval;
        private ComboBox cmbEvalPhase;
        private ComboBox cmbEvalSheet;
        private Label lblEvalInfo;
        private Label lblEvalTotals;
        private DataGridView gridEval;
        private TextBox tbEvalNotes;
        private CheckBox chkEvalAuto;
        private Button btnEvalConfirm;
        private string selectedEvalPhaseId;
        private string videoEvalPhaseId;
        private string evalGridSig;
        private bool evalApplying;
        private Dictionary<string, SwimTechMeasure> evalMeasures = new Dictionary<string, SwimTechMeasure>();

        private const int EvalColN = 0, EvalColText = 1, EvalColMeasure = 2, EvalColGood = 3, EvalColBad = 4, EvalColNone = 5, EvalColGo = 6;

        private class SheetItem
        {
            public readonly SwimTechSheet Sheet;

            public SheetItem(SwimTechSheet sheet)
            {
                Sheet = sheet;
            }

            public override string ToString()
            {
                return Sheet.Title.Text;
            }
        }

        private SwimPage BuildTechEvalTab()
        {
            SwimPage page = new SwimPage(SwimLang.T("Αξιολόγηση"));
            page.Padding = new Padding(6);

            TableLayoutPanel bar = PhaseBar(SwimLang.T("Προσπάθεια:"), out cmbEvalPhase, out lblEvalInfo, p => manager.SeekTo(p.Origin), p => DeletePhase(p));
            cmbEvalPhase.SelectedIndexChanged += (s, e) =>
            {
                if (updating)
                    return;
                PhaseItem item = cmbEvalPhase.SelectedItem as PhaseItem;
                selectedEvalPhaseId = item != null ? item.Id : null;
                RefreshTechEval();
            };

            TableLayoutPanel sheetBar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0), Padding = new Padding(0, 0, 0, 4) };
            sheetBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sheetBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Label sheetTitle = new Label { Text = SwimLang.T("Φύλλο:"), AutoSize = true, Font = valueFont, ForeColor = SwimTheme.Accent, Margin = new Padding(0, 7, 6, 3) };
            cmbEvalSheet = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            cmbEvalSheet.SelectedIndexChanged += (s, e) =>
            {
                if (updating)
                    return;
                SheetItem item = cmbEvalSheet.SelectedItem as SheetItem;
                SwimTechPhase phase = SelectedPhase(cmbEvalPhase);
                if (item != null && phase != null)
                    manager.ChooseTechSheet(phase, item.Sheet);
                evalGridSig = null;
                RefreshTechEval();
            };
            sheetBar.Controls.Add(sheetTitle, 0, 0);
            sheetBar.Controls.Add(cmbEvalSheet, 1, 0);

            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 4) };
            Button btnMeasure = new SwimNoFocusButton { Text = SwimLang.T("🤖 Μέτρηση"), AutoSize = true, Height = 28, Margin = new Padding(0, 2, 4, 2) };
            btnMeasure.Click += (s, e) => MeasureTechEval(true);
            toolTip.SetToolTip(btnMeasure, SwimLang.T("Μετρά ό,τι μπορεί από τα σημάδια και τον σκελετό και συμπληρώνει αυτά τα σημεία (🤖) για να τα επιβεβαιώσεις."));
            btnEvalConfirm = new SwimNoFocusButton { Text = SwimLang.T("✓ Επιβεβαίωση 🤖"), AutoSize = true, Height = 28, Margin = new Padding(0, 2, 4, 2) };
            btnEvalConfirm.Click += (s, e) =>
            {
                SwimTechPhase phase = SelectedPhase(cmbEvalPhase);
                SheetItem item = cmbEvalSheet.SelectedItem as SheetItem;
                if (phase != null && item != null)
                    manager.ConfirmTechProposals(phase, item.Sheet);
                RefreshTechEval();
            };
            toolTip.SetToolTip(btnEvalConfirm, SwimLang.T("Κρατά τις αυτόματες βαθμολογίες (🤖) ως δικές σου."));
            chkEvalAuto = new CheckBox { Text = SwimLang.T("Αυτόματη συμπλήρωση"), AutoSize = true, ForeColor = SwimTheme.Text, Margin = new Padding(6, 7, 4, 2) };
            chkEvalAuto.CheckedChanged += (s, e) =>
            {
                if (updating || manager.Settings == null)
                    return;
                manager.Settings.TechEvalAuto = chkEvalAuto.Checked;
                manager.Settings.Save();
                if (chkEvalAuto.Checked)
                    MeasureTechEval(false);
                RefreshTechEval();
            };
            toolTip.SetToolTip(chkEvalAuto, SwimLang.T("Το πρόγραμμα βάζει ✓ ή ✗ στα σημεία που μετρά (🤖) ώσπου να τα επιβεβαιώσεις ή να τα αλλάξεις."));
            Button btnDelete = new SwimNoFocusButton { Text = SwimLang.T("Διαγραφή αξιολόγησης"), AutoSize = true, Height = 28, Margin = new Padding(6, 2, 0, 2), Accent = SwimTheme.Red };
            btnDelete.Click += (s, e) =>
            {
                SwimTechPhase phase = SelectedPhase(cmbEvalPhase);
                SheetItem item = cmbEvalSheet.SelectedItem as SheetItem;
                if (phase == null || item == null || manager.TechEval(phase, item.Sheet, false) == null)
                    return;
                if (MessageBox.Show(this, SwimLang.T("Διαγραφή της αξιολόγησης αυτής της προσπάθειας με αυτό το φύλλο;"), SwimLang.T("Αξιολόγηση"),
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return;
                manager.DeleteTechEval(phase, item.Sheet);
                RefreshTechEval();
            };
            tools.Controls.Add(btnMeasure);
            tools.Controls.Add(btnEvalConfirm);
            tools.Controls.Add(chkEvalAuto);
            tools.Controls.Add(btnDelete);

            gridEval = CreateGrid();
            gridEval.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            gridEval.ScrollBars = ScrollBars.Vertical;
            gridEval.SelectionMode = DataGridViewSelectionMode.CellSelect;
            gridEval.Columns.Add(Column("n", "#", 34));
            DataGridViewTextBoxColumn text = Column("text", SwimLang.T("Σημείο"), 160);
            text.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            text.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            text.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            gridEval.Columns.Add(text);
            DataGridViewTextBoxColumn meas = Column("measure", SwimLang.T("Μέτρηση"), 96);
            meas.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            meas.DefaultCellStyle.Font = new Font("Segoe UI", 8f);
            gridEval.Columns.Add(meas);
            gridEval.Columns.Add(Column("good", "✓", 34));
            gridEval.Columns.Add(Column("bad", "✗", 34));
            gridEval.Columns.Add(Column("none", "–", 34));
            gridEval.Columns.Add(Column("go", "▶", 30));
            gridEval.Columns[EvalColN].Width = 38;
            gridEval.Columns[EvalColMeasure].Width = 118;
            gridEval.Columns[EvalColGood].Width = 36;
            gridEval.Columns[EvalColBad].Width = 36;
            gridEval.Columns[EvalColNone].Width = 34;
            gridEval.Columns[EvalColGo].Width = 30;
            gridEval.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            gridEval.CellClick += GridEval_CellClick;
            gridEval.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == EvalColText)
                    GoToEvalRow(e.RowIndex);
            };
            gridEval.CellToolTipTextNeeded += GridEval_CellToolTipTextNeeded;

            lblEvalTotals = new Label { Dock = DockStyle.Bottom, Height = 40, ForeColor = SwimTheme.AccentLight, Font = valueFont, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            Label lblNotes = new Label { Text = SwimLang.T("Σημειώσεις της αξιολόγησης (στο report)"), Dock = DockStyle.Bottom, Height = 20, ForeColor = SwimTheme.Dim, TextAlign = ContentAlignment.BottomLeft };
            tbEvalNotes = new TextBox { Dock = DockStyle.Bottom, Height = 44, Multiline = true, ScrollBars = ScrollBars.Vertical };
            tbEvalNotes.Leave += (s, e) => SaveEvalNotes();

            page.Controls.Add(gridEval);
            page.Controls.Add(lblEvalTotals);
            page.Controls.Add(lblNotes);
            page.Controls.Add(tbEvalNotes);
            page.Controls.Add(tools);
            page.Controls.Add(sheetBar);
            page.Controls.Add(bar);
            return page;
        }

        private void SaveEvalNotes()
        {
            if (updating || tbEvalNotes == null)
                return;
            SwimTechPhase phase = SelectedPhase(cmbEvalPhase);
            SheetItem item = cmbEvalSheet.SelectedItem as SheetItem;
            if (phase == null || item == null)
                return;
            SwimTechEval eval = manager.TechEval(phase, item.Sheet, false);
            string notes = tbEvalNotes.Text ?? "";
            if ((eval == null && notes.Length == 0) || (eval != null && eval.Notes == notes))
                return;
            manager.SetTechEvalNotes(phase, item.Sheet, notes);
        }

        /// <summary>Measures the phase shown and fills the points the program measures (when the automatic filling is on).</summary>
        private void MeasureTechEval(bool fromUser)
        {
            SwimTechPhase phase = SelectedPhase(cmbEvalPhase);
            SheetItem item = cmbEvalSheet != null ? cmbEvalSheet.SelectedItem as SheetItem : null;
            if (phase == null || item == null)
                return;

            evalMeasures = manager.TechMeasures(phase);
            int changed = manager.ApplyTechProposals(phase, item.Sheet, evalMeasures);
            if (fromUser)
            {
                int measured = item.Sheet.Items.Count(i => i.Criterion.Check != null && evalMeasures.ContainsKey(i.Criterion.Check));
                ShowStatus(string.Format(CultureInfo.InvariantCulture, SwimLang.T("🤖 Μετρήθηκαν {0} από τα {1} σημεία{2}."), measured, item.Sheet.Items.Count(),
                    changed > 0 ? string.Format(CultureInfo.InvariantCulture, SwimLang.T(" · {0} συμπληρώθηκαν (🤖)"), changed) : ""), false);
            }

            RefreshTechEval();
        }

        private SwimTechSheetItem EvalItemAt(int row)
        {
            return row >= 0 && row < gridEval.Rows.Count ? gridEval.Rows[row].Tag as SwimTechSheetItem : null;
        }

        private void GoToEvalRow(int row)
        {
            SwimTechSheetItem item = EvalItemAt(row);
            SwimTechPhase phase = SelectedPhase(cmbEvalPhase);
            if (item != null && phase != null)
                manager.SeekToCriterion(phase, item);
        }

        private void GridEval_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;
            SwimTechSheetItem item = EvalItemAt(e.RowIndex);
            SwimTechPhase phase = SelectedPhase(cmbEvalPhase);
            SheetItem sheet = cmbEvalSheet.SelectedItem as SheetItem;
            if (item == null || phase == null || sheet == null)
                return;

            if (e.ColumnIndex == EvalColGo)
            {
                manager.SeekToCriterion(phase, item);
                return;
            }

            int value;
            if (e.ColumnIndex == EvalColGood) value = 1;
            else if (e.ColumnIndex == EvalColBad) value = 0;
            else if (e.ColumnIndex == EvalColNone) value = -1;
            else return;

            // On a list of errors the columns read "there is" (✗ = 1) and "there is not" (✓ = 0).
            if (!sheet.Sheet.Scored && value >= 0)
                value = value == 1 ? 0 : 1;

            SwimTechEval eval = manager.TechEval(phase, sheet.Sheet, false);
            SwimTechScore old = null;
            if (eval != null)
                eval.Scores.TryGetValue(item.Key, out old);

            // The same answer again clears it (an automatic one is confirmed instead).
            int? set = value;
            if (old != null && old.Value == value)
                set = old.Auto ? (int?)value : null;

            SwimTechMeasure m = null;
            if (item.Criterion.Check != null)
                evalMeasures.TryGetValue(item.Criterion.Check, out m);
            manager.ScoreTechCriterion(phase, sheet.Sheet, item, set, m);
            RefreshTechEval();
        }

        private void GridEval_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            SwimTechSheetItem item = EvalItemAt(e.RowIndex);
            if (item == null)
                return;

            SwimTechSheetCatalog cat = manager.TechCatalog;
            List<string> lines = new List<string> { item.Criterion.Label.Text };
            string anchor = cat.AnchorLabel(item.Criterion.Anchor);
            if (anchor.Length > 0)
                lines.Add(SwimLang.T("Καρέ: ") + anchor + SwimLang.T(" (▶ ή διπλό κλικ)"));
            SwimTechCheck check = cat.CheckOf(item.Criterion);
            bool measured = check != null && SwimTechEvaluator.MeasuredChecks.Contains(check.Id);
            if (check != null)
            {
                SwimTechCheck now = SwimTechLearning.Effective(check);
                string target = now.GoodText;
                if (now != check && check.GoodText.Length > 0)
                    target += SwimLang.T(" (από τις απαντήσεις σου· αρχικά ") + check.GoodText + ")";
                else if (now != check)
                    target += SwimLang.T(" (από τις απαντήσεις σου)");
                lines.Add(SwimLang.T("Μέτρηση: ") + check.Label.Text + (target.Length > 0 ? SwimLang.T(" · στόχος ") + target : ""));
            }

            SwimTechAgreement agreement = manager.TechAgreement(item.Criterion);
            if (agreement != null && agreement.Samples > 0)
                lines.Add(SwimLang.T("Συμφωνία με τις απαντήσεις σου: ") + agreement.Text + (agreement.Reliable ? " ★" : agreement.Disagrees ? SwimLang.T(" ⚠ (δεν προτείνεται πια μόνο του)") : ""));

            if (item.Criterion.Level == "A" && measured && check != null && SwimTechEvaluator.NeedsAgreement.Contains(check.Id))
                lines.Add(SwimLang.T("Μετριέται από τον σκελετό κάτω από το νερό: το βαθμολογεί μόνο του όταν συμφωνεί 9 στις 10 με τις απαντήσεις σου."));
            else if (item.Criterion.Level == "A")
                lines.Add(measured ? SwimLang.T("Το μετρά το πρόγραμμα: 🤖 ώσπου να το επιβεβαιώσεις ή να το αλλάξεις.") : SwimLang.T("Δεν το μετρά ακόμη το πρόγραμμα: το κρίνεις εσύ στο καρέ."));
            else if (item.Criterion.Level == "P")
                lines.Add(measured ? SwimLang.T("Το πρόγραμμα μετρά μέρος του: έλεγξέ το στο καρέ. Το βαθμολογεί μόνο του όταν συμφωνεί 9 στις 10 με τις απαντήσεις σου.") :
                    SwimLang.T("Το κρίνεις εσύ στο καρέ."));
            else
                lines.Add(SwimLang.T("Δεν φαίνεται από πλάγια κάμερα: το κρίνεις εσύ στο καρέ."));
            if (item.Criterion.Cue != null && item.Criterion.Cue.Text.Length > 0)
                lines.Add(SwimLang.T("Διόρθωση: ") + item.Criterion.Cue.Text);
            e.ToolTipText = string.Join(Environment.NewLine, lines.ToArray());
        }

        /// <summary>The page of the evaluation (only when shown).</summary>
        private void RefreshTechEval()
        {
            if (IsDisposed || gridEval == null || manager.Race == null)
                return;

            SwimTechSheetCatalog cat = manager.TechCatalog;
            List<SwimTechPhase> phases = manager.TechPhases.ToList();
            SwimTechPhase current = manager.CurrentTechPhase;
            string videoId = current != null && manager.HasVideo ? current.Id : null;
            if (videoId != null && videoId != videoEvalPhaseId)
                selectedEvalPhaseId = videoId;
            videoEvalPhaseId = videoId;

            updating = true;
            try
            {
                string itemsSig = string.Join("|", phases.Select(p => p.Id + ":" + p.Title).ToArray());
                string comboSig = string.Join("|", cmbEvalPhase.Items.Cast<object>().OfType<PhaseItem>().Select(p => p.Id + ":" + p.Title).ToArray());
                if (itemsSig != comboSig)
                {
                    cmbEvalPhase.BeginUpdate();
                    cmbEvalPhase.Items.Clear();
                    foreach (SwimTechPhase p in phases)
                        cmbEvalPhase.Items.Add(new PhaseItem(p));
                    cmbEvalPhase.EndUpdate();
                }

                int index = phases.FindIndex(p => p.Id == selectedEvalPhaseId);
                if (index < 0 && phases.Count > 0)
                    index = phases.Count - 1;
                if (cmbEvalPhase.SelectedIndex != index)
                    cmbEvalPhase.SelectedIndex = index;
                chkEvalAuto.Checked = manager.Settings == null || manager.Settings.TechEvalAuto;
            }
            finally
            {
                updating = false;
            }

            SwimTechPhase phase = SelectedPhase(cmbEvalPhase);
            selectedEvalPhaseId = phase != null ? phase.Id : null;

            // The sheets of the phase.
            List<SwimTechSheet> sheets = phase != null ? cat.SheetsFor(phase.Kind, manager.Race.Style) : new List<SwimTechSheet>();
            SwimTechSheet sheet = phase != null ? manager.TechSheetFor(phase) : null;
            if (sheet != null && !sheets.Contains(sheet))
                sheets.Insert(0, sheet);
            updating = true;
            try
            {
                string sheetsSig = string.Join("|", sheets.Select(s => s.Id).ToArray());
                string comboSig = string.Join("|", cmbEvalSheet.Items.Cast<object>().OfType<SheetItem>().Select(s => s.Sheet.Id).ToArray());
                if (sheetsSig != comboSig)
                {
                    cmbEvalSheet.BeginUpdate();
                    cmbEvalSheet.Items.Clear();
                    foreach (SwimTechSheet s in sheets)
                        cmbEvalSheet.Items.Add(new SheetItem(s));
                    cmbEvalSheet.EndUpdate();
                }

                SheetItem chosen = cmbEvalSheet.SelectedItem as SheetItem;
                if (chosen == null || sheet == null || chosen.Sheet.Id != sheet.Id)
                {
                    int i = sheet != null ? sheets.FindIndex(s => s.Id == sheet.Id) : -1;
                    cmbEvalSheet.SelectedIndex = i;
                }
            }
            finally
            {
                updating = false;
            }

            SheetItem selected = cmbEvalSheet.SelectedItem as SheetItem;
            sheet = selected != null ? selected.Sheet : null;

            if (cat.Sheets.Count == 0)
                lblEvalInfo.Text = cat.Error.Length > 0 ? cat.Error : SwimLang.T("Ο κατάλογος κριτηρίων είναι άδειος.");
            else if (phase == null)
                lblEvalInfo.Text = manager.Race.IsTraining ? SwimLang.T("Καμία προσπάθεια: σημάδεψε πρώτα το σήμα (G) ή την επαφή στον τοίχο (W).") : SwimLang.T("Ορίσε πρώτα το σήμα εκκίνησης (G).");
            else if (sheet == null)
                lblEvalInfo.Text = SwimLang.T("Κανένα φύλλο για αυτή την προσπάθεια και το στυλ.");
            else
                lblEvalInfo.Text = sheet.Source + (sheet.Note != null && sheet.Note.Text.Length > 0 ? " · " + sheet.Note.Text : "");

            if (phase == null || sheet == null)
            {
                gridEval.Rows.Clear();
                evalGridSig = null;
                lblEvalTotals.Text = "";
                tbEvalNotes.Enabled = false;
                return;
            }

            evalMeasures = manager.TechMeasures(phase);

            // The automatic filling follows the measurements (a new mark, the skeleton read): the page is refreshed by the change.
            if (!evalApplying && manager.Settings != null && manager.Settings.TechEvalAuto)
            {
                evalApplying = true;
                try
                {
                    manager.ApplyTechProposals(phase, sheet, evalMeasures);
                }
                finally
                {
                    evalApplying = false;
                }
            }

            SwimTechEval eval = manager.TechEval(phase, sheet, false);
            string sig = phase.Id + "|" + sheet.Id + "|" + cat.Updated + "|" + SwimLang.Code;
            if (sig != evalGridSig)
            {
                evalGridSig = sig;
                BuildEvalRows(sheet);
                bool was = updating;
                updating = true;
                tbEvalNotes.Text = eval != null ? eval.Notes : "";
                updating = was;
            }

            tbEvalNotes.Enabled = true;
            FillEvalRows(cat, sheet, eval);
            RefreshEvalTotals(cat, phase, sheet, eval);
            btnEvalConfirm.Enabled = eval != null && eval.Scores.Values.Any(s => s.Auto);
        }

        private void BuildEvalRows(SwimTechSheet sheet)
        {
            gridEval.SuspendLayout();
            gridEval.Rows.Clear();
            gridEval.Columns[EvalColGood].HeaderText = sheet.Scored ? "✓" : SwimLang.T("όχι");
            gridEval.Columns[EvalColBad].HeaderText = sheet.Scored ? "✗" : SwimLang.T("ναι");
            Font bold = new Font(gridEval.Font, FontStyle.Bold);
            foreach (SwimTechSheetPhase ph in sheet.Phases)
            {
                int r = gridEval.Rows.Add("", ph.Label.Text, "", "", "", "", "");
                DataGridViewRow head = gridEval.Rows[r];
                head.Tag = ph;
                head.DefaultCellStyle.BackColor = SwimTheme.AccentDark;
                head.DefaultCellStyle.SelectionBackColor = SwimTheme.AccentDark;
                head.DefaultCellStyle.ForeColor = SwimTheme.AccentPale;
                head.DefaultCellStyle.SelectionForeColor = SwimTheme.AccentPale;
                head.DefaultCellStyle.Font = bold;
                foreach (SwimTechSheetItem it in ph.Items)
                {
                    int i = gridEval.Rows.Add(it.N, it.Criterion.Label.Text, "", "", "", "", "▶");
                    gridEval.Rows[i].Tag = it;
                }
            }

            gridEval.ResumeLayout();
        }

        private void FillEvalRows(SwimTechSheetCatalog cat, SwimTechSheet sheet, SwimTechEval eval)
        {
            foreach (DataGridViewRow row in gridEval.Rows)
            {
                SwimTechSheetPhase ph = row.Tag as SwimTechSheetPhase;
                if (ph != null)
                {
                    int g, s, t;
                    SwimTechEvaluator.PhaseTotals(ph, eval, out g, out s, out t);
                    row.Cells[EvalColMeasure].Value = sheet.Scored ? string.Format(CultureInfo.InvariantCulture, "{0}/{1}", g, t) : "";
                    continue;
                }

                SwimTechSheetItem it = row.Tag as SwimTechSheetItem;
                if (it == null)
                    continue;

                // 🤖 the program measures it, ★ and agrees with the coach's answers (9 in 10), ⚠ it does not (no proposal any more),
                // (symbols, not colours: the lists of the program draw the emoji without their colours),
                // ◐ it measures a part of it, 👁 the coach judges it.
                SwimTechAgreement agreement = manager.TechAgreement(it.Criterion);
                bool measured = it.Criterion.Check != null && SwimTechEvaluator.MeasuredChecks.Contains(it.Criterion.Check);
                string mark;
                if (it.Criterion.Level == "X" || it.Criterion.Check == null)
                    mark = "👁 ";
                else if (measured && agreement != null && agreement.Disagrees && it.Criterion.Level == "A")
                    mark = "⚠ ";
                else if (measured && agreement != null && agreement.Reliable)
                    mark = "★ ";
                else if (measured && it.Criterion.Level == "A" && !SwimTechEvaluator.NeedsAgreement.Contains(it.Criterion.Check))
                    mark = "🤖 ";
                else
                    mark = "◐ ";
                string label = mark + it.Criterion.Label.Text;
                if (!label.Equals(row.Cells[EvalColText].Value as string))
                    row.Cells[EvalColText].Value = label;

                SwimTechMeasure m = null;
                if (it.Criterion.Check != null)
                    evalMeasures.TryGetValue(it.Criterion.Check, out m);
                row.Cells[EvalColMeasure].Value = m != null ? m.Text : "";
                row.Cells[EvalColMeasure].Style.ForeColor = m == null ? SwimTheme.Dim : !m.Good.HasValue ? SwimTheme.Text : (m.Good.Value == sheet.Scored ? SwimTheme.Green : SwimTheme.Red);

                SwimTechScore sc = null;
                if (eval != null)
                    eval.Scores.TryGetValue(it.Key, out sc);
                int? v = sc != null ? (int?)sc.Value : null;
                // On a list of errors: "ναι" (the error is there) in the ✗ column.
                int? goodCol = sheet.Scored ? 1 : 0, badCol = sheet.Scored ? 0 : 1;
                SetScoreCell(row.Cells[EvalColGood], v.HasValue && v.Value == goodCol, sc != null && sc.Auto, SwimTheme.Green, "✓");
                SetScoreCell(row.Cells[EvalColBad], v.HasValue && v.Value == badCol, sc != null && sc.Auto, SwimTheme.Red, "✗");
                SetScoreCell(row.Cells[EvalColNone], v.HasValue && v.Value < 0, false, SwimTheme.Faint, "–");
            }
        }

        private static void SetScoreCell(DataGridViewCell cell, bool on, bool auto, Color color, string text)
        {
            cell.Value = on ? (auto ? "🤖" + text : text) : "·";
            cell.Style.BackColor = on ? SwimTheme.Mix(SwimTheme.Back, color, auto ? 0.25 : 0.55) : Color.Empty;
            cell.Style.SelectionBackColor = cell.Style.BackColor.IsEmpty ? Color.Empty : cell.Style.BackColor;
            cell.Style.ForeColor = on ? SwimTheme.Text : SwimTheme.Faint;
        }

        private void RefreshEvalTotals(SwimTechSheetCatalog cat, SwimTechPhase phase, SwimTechSheet sheet, SwimTechEval eval)
        {
            List<string> parts = new List<string>();
            int good, scored, total;
            SwimTechEvaluator.Totals(sheet, eval, out good, out scored, out total);
            if (sheet.Scored)
                parts.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Σύνολο {0}/{1}"), good, total) +
                    (scored < total ? string.Format(CultureInfo.InvariantCulture, SwimLang.T(" · βαθμολογήθηκαν {0}"), scored) : ""));
            else
            {
                int present = eval != null ? eval.Scores.Values.Count(s => s.Value == 1) : 0;
                parts.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Λάθη που υπάρχουν: {0} από {1}"), present, total));
            }

            int auto = eval != null ? eval.Scores.Values.Count(s => s.Auto) : 0;
            if (auto > 0)
                parts.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("🤖 {0} περιμένουν επιβεβαίωση"), auto));

            // The level of the sheet's table (start 15 m or turn 5 + 10 m).
            SwimTechMeasure level;
            if (sheet.Levels == "start_15" && evalMeasures.TryGetValue("start_15", out level))
                parts.Add(SwimLang.T("15 μ.: ") + level.Text);
            else if (sheet.Levels == "turn_5_10" && evalMeasures.TryGetValue("turn_5_10", out level))
                parts.Add(SwimLang.T("5 + 10 μ.: ") + level.Text);
            lblEvalTotals.Text = string.Join("  ·  ", parts.ToArray());
        }
    }
}
