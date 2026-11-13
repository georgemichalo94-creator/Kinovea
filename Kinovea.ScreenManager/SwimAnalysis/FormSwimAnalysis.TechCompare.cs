#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.22.0) The comparison of the report of a technique analysis (training), at the top of the pages Εκκινήσεις and Στροφές (the same
    /// choice in both): the same athlete (none, the previous analysis, the best one, or one of the list) and elite (none, a final of this
    /// computer as the mean of its swimmers or its winner, or an elite technique analysis), each alone or both together.
    /// </summary>
    public partial class FormSwimAnalysis
    {
        private sealed class TechCompareRow
        {
            public Control Panel;
            public Label PrevCaption;
            public ComboBox Prev;
            public ComboBox Elite;
            public Label Info;
            public Button Save;
        }

        private sealed class TechEliteItem
        {
            public readonly SwimTechElite Elite;
            public readonly string Text;

            public TechEliteItem(SwimTechElite elite, string text)
            {
                Elite = elite;
                Text = text;
            }

            public override string ToString()
            {
                return Text;
            }
        }

        private readonly List<TechCompareRow> techCompareRows = new List<TechCompareRow>();
        private bool techCompareRefreshQueued;

        /// <summary>The row of the comparison of a technique page (shown in a technique analysis only).</summary>
        private Control BuildTechCompareRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0, 0, 0, 6) };
            Label title = new Label { Text = SwimLang.T("Σύγκριση στο report:"), AutoSize = true, Font = valueFont, ForeColor = SwimTheme.Accent, Margin = new Padding(0, 7, 6, 3) };
            Label prevCaption = Caption(SwimLang.T("Ίδιος αθλητής:"));
            ComboBox prev = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270, DropDownWidth = 360, Margin = new Padding(3, 3, 8, 3) };
            ComboBox elite = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, DropDownWidth = 420, Margin = new Padding(3, 3, 6, 3) };
            Button refresh = new SwimNoFocusButton { Text = "↻", Width = 30, Height = 26, Margin = new Padding(0, 2, 6, 2) };
            Button save = new SwimNoFocusButton { Text = SwimLang.T("Ως elite αναφορά…"), AutoSize = true, Height = 26, Margin = new Padding(0, 2, 6, 2) };
            Label info = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(4, 7, 0, 3), Text = "" };

            prev.SelectedIndexChanged += (s, e) =>
            {
                if (updating)
                    return;
                SwimAnalysisItem item = prev.SelectedItem as SwimAnalysisItem;
                if (item != null)
                    manager.SetTechPrev(item.Id, item.Label);
            };
            elite.SelectedIndexChanged += (s, e) =>
            {
                if (updating)
                    return;
                TechEliteItem item = elite.SelectedItem as TechEliteItem;
                if (item != null)
                    manager.SetTechElite(item.Elite);
            };
            refresh.Click += (s, e) => manager.RefreshTechCandidates();
            save.Click += (s, e) => SaveAsTechElite();

            toolTip.SetToolTip(prev, SwimLang.T("Με ποια προηγούμενη ανάλυση τεχνικής του αθλητή (ίδιο στυλ και πισίνα) συγκρίνεται το report: στα νούμερα, στο γράφημα,\n") +
                SwimLang.T("στην αξιολόγηση και στις φάσεις δίπλα-δίπλα. «Αυτόματα: η προηγούμενη» βρίσκει μόνη της την πιο πρόσφατη πριν από αυτήν."));
            toolTip.SetToolTip(elite, SwimLang.T("Σύγκριση με elite: ένας τελικός αυτού του υπολογιστή (ο μέσος όρος των κολυμβητών ή ο νικητής: −5 μ., +15 μ., έξοδος,\n") +
                SwimLang.T("15 μ. εκκίνησης) ή μια elite ανάλυση τεχνικής του Google Sheet (όλα τα νούμερα και τα στιγμιότυπά της)."));
            toolTip.SetToolTip(refresh, SwimLang.T("Ξαναφέρνει τις αναλύσεις τεχνικής του αθλητή και τις elite αναλύσεις από το Google Sheet."));
            toolTip.SetToolTip(save, SwimLang.T("Αυτή η ανάλυση γίνεται elite αναφορά τεχνικής (φύλλο «Elite τεχνική» του Google Sheet):\n") +
                SwimLang.T("μετά διαλέγεται στη λίστα Elite άλλων αναλύσεων, για σύγκριση με όλα τα νούμερα και τα στιγμιότυπά της."));

            // Each list with its caption, the buttons together: on a narrow panel they go to the next line as one.
            row.Controls.Add(title);
            row.Controls.Add(Together(prevCaption, prev));
            row.Controls.Add(Together(Caption("Elite:"), elite));
            row.Controls.Add(Together(refresh, save));
            row.Controls.Add(info);
            row.Visible = false;
            techCompareRows.Add(new TechCompareRow { Panel = row, PrevCaption = prevCaption, Prev = prev, Elite = elite, Info = info, Save = save });
            return row;
        }

        /// <summary>Controls that stay on one line of a row that wraps.</summary>
        private static Control Together(params Control[] controls)
        {
            FlowLayoutPanel group = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0) };
            group.Controls.AddRange(controls);
            return group;
        }

        /// <summary>After the event of a list (its items are filled again outside of it).</summary>
        public void RefreshTechCompareLater()
        {
            if (IsDisposed || techCompareRefreshQueued || !IsHandleCreated)
                return;

            techCompareRefreshQueued = true;
            BeginInvoke((Action)(() =>
            {
                techCompareRefreshQueued = false;
                RefreshTechCompare();
            }));
        }

        /// <summary>The lists of the comparison, with the choice of the analysis (in both pages).</summary>
        public void RefreshTechCompare()
        {
            if (IsDisposed || techCompareRows.Count == 0)
                return;

            SwimRace race = manager.Race;
            bool show = race != null && race.IsTraining;
            bool female = race != null && SwimReference.NormalizeGender(race.AthleteGender) == "F";
            List<SwimAnalysisItem> prevItems = show ? TechPrevItems(race) : new List<SwimAnalysisItem>();
            List<TechEliteItem> eliteItems = show ? TechEliteItems(race) : new List<TechEliteItem>();
            int prevIndex = Math.Max(0, prevItems.FindIndex(i => i.Id == (race != null ? race.TechPrev ?? "" : "")));
            int eliteIndex = Math.Max(0, eliteItems.FindIndex(i => (i.Elite == null && (race == null || race.TechElite == null)) ||
                (i.Elite != null && race != null && i.Elite.Same(race.TechElite))));

            updating = true;
            try
            {
                foreach (TechCompareRow row in techCompareRows)
                {
                    row.Panel.Visible = show;
                    if (!show)
                        continue;

                    row.PrevCaption.Text = female ? SwimLang.T("Ίδια αθλήτρια:") : SwimLang.T("Ίδιος αθλητής:");
                    Fill(row.Prev, prevItems.Cast<object>().ToList(), prevIndex);
                    Fill(row.Elite, eliteItems.Cast<object>().ToList(), eliteIndex);
                    row.Info.Text = manager.TechCompareMessage ?? "";
                    toolTip.SetToolTip(row.Info, row.Info.Text);
                    row.Save.Enabled = manager.Settings.IsConfigured;
                }
            }
            finally
            {
                updating = false;
            }
        }

        private static void Fill(ComboBox combo, List<object> items, int selected)
        {
            bool same = combo.Items.Count == items.Count;
            for (int i = 0; same && i < items.Count; i++)
                same = combo.Items[i].ToString() == items[i].ToString();

            combo.BeginUpdate();
            if (!same)
            {
                combo.Items.Clear();
                combo.Items.AddRange(items.ToArray());
            }

            if (combo.Items.Count > 0 && combo.SelectedIndex != selected)
                combo.SelectedIndex = Math.Min(selected, combo.Items.Count - 1);
            combo.EndUpdate();
        }

        /// <summary>Χωρίς, the two automatic choices (with the analysis they give now), the analyses of the list, the one chosen if gone.</summary>
        private List<SwimAnalysisItem> TechPrevItems(SwimRace race)
        {
            List<SwimAnalysisItem> items = new List<SwimAnalysisItem>();
            items.Add(new SwimAnalysisItem { Id = "", Label = SwimLang.T("Χωρίς") });
            SwimTechAnalysisItem auto = manager.TechAutoPrev(false), best = manager.TechAutoPrev(true);
            items.Add(new SwimAnalysisItem { Id = "auto", Label = SwimLang.T("Αυτόματα: η προηγούμενη") + (auto != null ? " (" + auto.Date + ")" : "") });
            items.Add(new SwimAnalysisItem { Id = "best", Label = SwimLang.T("Αυτόματα: η καλύτερη") + (best != null ? " (" + best.Date + ")" : "") });
            foreach (SwimTechAnalysisItem a in manager.TechCandidates)
                items.Add(new SwimAnalysisItem { Id = a.Id, Label = a.Label });

            string chosen = race.TechPrev ?? "";
            if (chosen.Length > 0 && !items.Any(i => i.Id == chosen))
                items.Add(new SwimAnalysisItem { Id = chosen, Label = string.IsNullOrEmpty(race.TechPrevLabel) ? chosen : race.TechPrevLabel });
            return items;
        }

        /// <summary>Χωρίς, the finals of this computer (mean and winner), the elite technique analyses, the one chosen if gone.</summary>
        private List<TechEliteItem> TechEliteItems(SwimRace race)
        {
            List<TechEliteItem> items = new List<TechEliteItem>();
            items.Add(new TechEliteItem(null, SwimLang.T("Χωρίς")));
            foreach (SwimTechElite f in manager.TechEliteFinals())
                items.Add(new TechEliteItem(f, f.Label));
            foreach (SwimTechElite r in manager.TechEliteRefs)
                items.Add(new TechEliteItem(r, SwimLang.T("Elite ανάλυση τεχνικής") + " · «" + r.Label + "»"));

            SwimTechElite chosen = race.TechElite;
            if (chosen != null && chosen.IsSet && !items.Any(i => i.Elite != null && i.Elite.Same(chosen)))
                items.Add(new TechEliteItem(chosen, chosen.Type == SwimTechElite.TypeRef ? SwimLang.T("Elite ανάλυση τεχνικής") + " · «" + chosen.Label + "»" : chosen.Label));
            return items;
        }

        private void SaveAsTechElite()
        {
            SwimRace race = manager.Race;
            if (race == null || !race.IsTraining)
                return;

            string suggested = string.Join(" · ", new[] { race.Athlete, race.Date }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray());
            string name = AskText(SwimLang.T("Elite αναφορά τεχνικής"),
                SwimLang.T("Όνομα στη λίστα Elite των αναλύσεων τεχνικής (π.χ. «Στροφή ελεύθερο – elite 1»):"), suggested);
            if (name != null)
                manager.SaveAsTechElite(name);
        }
    }
}
