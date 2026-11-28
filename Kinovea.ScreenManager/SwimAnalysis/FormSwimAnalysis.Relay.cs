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
    /// (2.42.0) The swimmers of a relay in the page of the race: the four legs in their order, each with its style, its swimmer (found
    /// in the KOE App from the official results, or chosen: ▾ for the alike ones or the search of the KOE App; or a name only), how
    /// he was found and the official time of his leg. Only for a relay of four.
    /// </summary>
    public partial class FormSwimAnalysis
    {
        private Label relayRowLabel;
        private TableLayoutPanel relayTable;
        private readonly Label[] legStyle = new Label[4];
        private readonly TextBox[] legName = new TextBox[4];
        private readonly Label[] legNote = new Label[4];
        private readonly Button[] legPick = new Button[4];
        private readonly Label[] legTime = new Label[4];
        private Label relayHint;

        /// <summary>The leg whose swimmer the analyst looks for in the list of the KOE App above (0: none, the list is the race's athlete).</summary>
        private int relayTarget;

        /// <summary>The row of the swimmers of the relay in the table of the page (hidden for a race that is not a relay).</summary>
        private void AddRelayRow(TableLayoutPanel table)
        {
            relayTable = new TableLayoutPanel { ColumnCount = 5, RowCount = 5, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0) };
            relayTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            relayTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            relayTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            relayTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            relayTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int k = 0; k < 4; k++)
            {
                int leg = k + 1;
                relayTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                legStyle[k] = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(0, 6, 6, 0) };
                legName[k] = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 4, 2), Font = SwimTheme.Ui(8.5f, FontStyle.Bold) };
                legNote[k] = new Label { AutoSize = true, Margin = new Padding(2, 6, 4, 0), Cursor = Cursors.Hand };
                legPick[k] = new SwimNoFocusButton { Text = "▾", Width = 30, Height = 24, Margin = new Padding(0, 1, 4, 1) };
                legTime[k] = new Label { AutoSize = true, ForeColor = SwimTheme.Text, Margin = new Padding(4, 6, 0, 0), Font = SwimTheme.Ui(8.5f, FontStyle.Bold) };

                TextBox box = legName[k];
                Button pick = legPick[k];
                box.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        CommitLegName(leg);
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                    }
                };
                box.Leave += (s, e) => CommitLegName(leg);
                pick.Click += (s, e) => ShowLegMenu(leg, pick);
                legNote[k].Click += (s, e) => ShowLegMenu(leg, pick);
                toolTip.SetToolTip(box, SwimLang.T("Ο κολυμβητής του σκέλους. Από το KOE App (▾) η ανάλυση του σκέλους του πηγαίνει στην καρτέλα του· ένα όνομα που γράφεις εδώ μένει μόνο στην αναφορά."));
                toolTip.SetToolTip(pick, SwimLang.T("Οι αθλητές του KOE App που μπορεί να είναι αυτός, ή αναζήτηση στη λίστα του KOE App."));

                relayTable.Controls.Add(legStyle[k], 0, k);
                relayTable.Controls.Add(legName[k], 1, k);
                relayTable.Controls.Add(legNote[k], 2, k);
                relayTable.Controls.Add(legPick[k], 3, k);
                relayTable.Controls.Add(legTime[k], 4, k);
            }

            relayTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            relayHint = new Label { AutoSize = false, Dock = DockStyle.Fill, Height = 34, ForeColor = SwimTheme.Dim, Margin = new Padding(0, 2, 0, 0) };
            relayTable.Controls.Add(relayHint, 0, 4);
            relayTable.SetColumnSpan(relayHint, 5);

            int row = table.RowCount;
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            relayRowLabel = new Label { Text = SwimLang.T("Κολυμβητές σκυταλοδρομίας"), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(3, 7, 3, 3) };
            relayTable.Margin = new Padding(3);
            table.Controls.Add(relayRowLabel, 0, row);
            table.Controls.Add(relayTable, 1, row);
        }

        /// <summary>The swimmers of the relay as the race has them (the row hidden for another race).</summary>
        private void RefreshRelayLegs()
        {
            if (relayTable == null)
                return;

            SwimRace r = manager.Race;
            bool relay = r != null && r.IsRelayOfFour;
            relayTable.Visible = relayRowLabel.Visible = relay;
            if (!relay)
            {
                relayTarget = 0;
                return;
            }

            int legDistance = r.LegDistance;
            for (int k = 0; k < 4; k++)
            {
                int leg = k + 1;
                SwimRelayLeg l = r.RelayLeg(leg);
                legStyle[k].Text = string.Format(CultureInfo.InvariantCulture, "{0} · {1} · {2}–{3}", leg, SwimPresets.StyleName(r.LegStyle(leg)),
                    (leg - 1) * legDistance, leg * legDistance);
                if (!legName[k].Focused)
                    legName[k].Text = l != null ? l.Athlete : "";

                string note = SwimAnalysisManager.LegNote(l);
                legNote[k].Text = note;
                legNote[k].ForeColor = l != null && l.HasAthlete ? SwimTheme.Green : note.StartsWith("⚠", StringComparison.Ordinal) ? colorWarn : SwimTheme.Faint;
                string time = l != null && l.OfficialTime.HasValue ? SwimReference.FormatTime(l.OfficialTime.Value) : "";
                if (l != null && l.OfficialExchange.HasValue)
                    time += (time.Length > 0 ? "  " : "") + (leg == 1 ? SwimLang.T("αντ. ") : SwimLang.T("αλλ. ")) +
                        l.OfficialExchange.Value.ToString("0.00", CultureInfo.InvariantCulture);
                legTime[k].Text = time;
                legTime[k].Visible = time.Length > 0;
            }

            RefreshRelayHint();
        }

        private void RefreshRelayHint()
        {
            if (relayHint == null)
                return;

            SwimRace r = manager.Race;
            if (relayTarget > 0)
            {
                relayHint.ForeColor = SwimTheme.Amber;
                relayHint.Text = string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("Σκέλος {0}: γράψε στην αναζήτηση του KOE App (πάνω) και διάλεξε τον κολυμβητή στη λίστα. Esc: ακύρωση."), relayTarget);
                return;
            }

            relayHint.ForeColor = SwimTheme.Dim;
            bool any = r != null && r.RelayLegs != null && r.RelayLegs.Any(l => !string.IsNullOrWhiteSpace(l.Athlete));
            relayHint.Text = any
                ? SwimLang.T("Κάθε κολυμβητής από το KOE App παίρνει την ανάλυση του σκέλους του στην καρτέλα του. ▾: άλλος αθλητής του KOE App.")
                : SwimLang.T("Οι κολυμβητές μπαίνουν μόνοι τους με τα επίσημα αποτελέσματα (🔗 ή 📋 πιο πάνω), ή διάλεξέ τους με ▾.");
        }

        /// <summary>The name typed for a leg: his name only (not the KOE App) when it is not the swimmer's name it had.</summary>
        private void CommitLegName(int leg)
        {
            if (updating || manager.Race == null || !manager.Race.IsRelayOfFour)
                return;

            SwimRelayLeg l = manager.Race.RelayLeg(leg);
            string text = legName[leg - 1].Text.Trim();
            if (text == (l != null ? (l.Athlete ?? "").Trim() : ""))
                return;
            manager.SetRelayLegName(leg, text);
        }

        /// <summary>
        /// The menu of a leg: the athletes of the KOE App who may be its swimmer (from the official results), the search of the KOE App
        /// for it, no swimmer.
        /// </summary>
        private void ShowLegMenu(int leg, Control anchor)
        {
            SwimRace r = manager.Race;
            if (r == null || !r.IsRelayOfFour)
                return;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Closed += (s, e) => BeginInvoke((Action)(() => menu.Dispose()));
            List<SwimAthlete> candidates = manager.RelayLegCandidates(leg);
            foreach (SwimAthlete a in candidates)
            {
                SwimAthlete chosen = a;
                string about = string.Join(" · ", new[] { a.BirthYear, a.Club, SwimLang.T("ΑΜ ") + a.Id }.Where(x => !string.IsNullOrEmpty(x) && x.Trim().Length > 0).ToArray());
                ToolStripMenuItem item = new ToolStripMenuItem(a.Name + "    " + about);
                SwimRelayLeg l = r.RelayLeg(leg);
                item.Checked = l != null && l.AthleteId == a.Id;
                item.Click += (s, e) =>
                {
                    relayTarget = 0;
                    manager.SetRelayLegAthlete(leg, chosen);
                };
                menu.Items.Add(item);
            }

            if (candidates.Count > 0)
                menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem search = new ToolStripMenuItem(SwimLang.T("🔍 Αναζήτηση στο KOE App (όνομα ή ΑΜ)…"));
            search.Click += (s, e) => StartLegSearch(leg);
            menu.Items.Add(search);

            SwimRelayLeg current = r.RelayLeg(leg);
            if (current != null && !string.IsNullOrWhiteSpace(current.Athlete))
            {
                ToolStripMenuItem none = new ToolStripMenuItem(SwimLang.T("✕ Χωρίς κολυμβητή"));
                none.Click += (s, e) =>
                {
                    relayTarget = 0;
                    manager.SetRelayLegName(leg, "");
                };
                menu.Items.Add(none);
            }

            menu.Show(anchor, new Point(0, anchor.Height));
        }

        /// <summary>The search of the KOE App above looks for the leg's swimmer: his family name in it, the list ready.</summary>
        private void StartLegSearch(int leg)
        {
            relayTarget = leg;
            SwimRelayLeg l = manager.Race != null ? manager.Race.RelayLeg(leg) : null;
            string name = l != null ? (l.OfficialName.Length > 0 ? l.OfficialName : l.Athlete) : "";
            string family = (name ?? "").Trim().Split(' ').FirstOrDefault() ?? "";
            tbAthleteSearch.Text = family.Length >= 2 ? family : "";
            tbAthleteSearch.Focus();
            tbAthleteSearch.SelectAll();
            RefreshRelayHint();
        }

        /// <summary>An athlete chosen in the list of the KOE App while it looks for a leg's swimmer: he is that leg's (true).</summary>
        private bool ChooseLegAthlete(SwimAthlete athlete)
        {
            if (relayTarget <= 0 || athlete == null || manager.Race == null || !manager.Race.IsRelayOfFour)
                return false;

            int leg = relayTarget;
            relayTarget = 0;
            manager.SetRelayLegAthlete(leg, athlete);
            tbAthleteSearch.Text = "";
            RefreshRelayHint();
            return true;
        }

        /// <summary>Esc in the search: it is the race's athlete's again.</summary>
        private bool CancelLegSearch()
        {
            if (relayTarget <= 0)
                return false;
            relayTarget = 0;
            tbAthleteSearch.Text = "";
            RefreshRelayHint();
            return true;
        }
    }
}
