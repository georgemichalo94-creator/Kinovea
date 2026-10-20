#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// How the race is compared with the reference on the video (since 2.8.0): the Δ of time on the race board, its graphic, the ghost
    /// and the line of the reference, any of them together.
    /// </summary>
    public partial class FormSwimAnalysis
    {
        private CheckBox chkCompareDelta;
        private CheckBox chkCompareGhost;
        private CheckBox chkCompareLine;
        private Label lblCompareHint;

        /// <summary>The row of the comparison on the video (under the row of the graphics of the video).</summary>
        private Control BuildCompareOptions()
        {
            FlowLayoutPanel row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0, 0, 0, 4) };
            chkCompareDelta = new CheckBox { Text = SwimLang.T("Δ χρόνου"), AutoSize = true, Margin = new Padding(6, 5, 3, 3) };
            chkCompareGhost = new CheckBox { Text = SwimLang.T("Ghost"), AutoSize = true, Margin = new Padding(6, 5, 3, 3) };
            chkCompareLine = new CheckBox { Text = SwimLang.T("Γραμμή"), AutoSize = true, Margin = new Padding(6, 5, 3, 3) };
            lblCompareHint = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(8, 7, 0, 3), Text = "" };

            chkCompareDelta.CheckedChanged += (s, e) => CommitOverlayOptions();
            chkCompareGhost.CheckedChanged += (s, e) => CommitOverlayOptions();
            chkCompareLine.CheckedChanged += (s, e) => CommitOverlayOptions();

            toolTip.SetToolTip(chkCompareDelta, SwimLang.T("Η διαφορά χρόνου από την αναφορά στο τελευταίο πέρασμα, στον πίνακα του χρόνου πάνω στο βίντεο\n") +
                SwimLang.T("(πράσινο: πιο γρήγορα από την αναφορά, κόκκινο: πιο αργά)."));
            toolTip.SetToolTip(chkCompareGhost, SwimLang.T("Ο κολυμβητής της αναφοράς διάφανος, στη θέση που ήταν εκείνη τη στιγμή της κούρσας του,\n") +
                SwimLang.T("στη λωρίδα του αθλητή: ένα αντίγραφο του αθλητή του καρέ, πιο ανοιχτό και γαλάζιο. Πάνω του η απόσταση από τον αθλητή\n") +
                SwimLang.T("(+: η αναφορά μπροστά). Χρειάζεται τα μήκη με «Ακολούθηση αθλητή» ή τον αυτόματο πιλότο (ή σταθερή κάμερα με βαθμονόμηση)."));
            toolTip.SetToolTip(chkCompareLine, SwimLang.T("Γραμμή κατά πλάτος της λωρίδας στο κεφάλι της αναφοράς, όπως η γραμμή του παγκοσμίου ρεκόρ στην τηλεόραση,\n") +
                SwimLang.T("με το όνομα και την απόσταση από τον αθλητή. Χρειάζεται τα μήκη με «Ακολούθηση αθλητή» ή τον αυτόματο πιλότο."));

            row.Controls.Add(Caption(SwimLang.T("Σύγκριση στο βίντεο:")));
            row.Controls.Add(chkCompareDelta);
            if (chkDeltaChart != null)
                row.Controls.Add(chkDeltaChart);
            row.Controls.Add(chkCompareGhost);
            row.Controls.Add(chkCompareLine);
            row.Controls.Add(lblCompareHint);
            return row;
        }

        private void CommitCompareOptions(SwimSettings s)
        {
            if (chkCompareDelta != null)
                s.CompareDelta = chkCompareDelta.Checked;
            if (chkCompareGhost != null)
                s.CompareGhost = chkCompareGhost.Checked;
            if (chkCompareLine != null)
                s.CompareLine = chkCompareLine.Checked;
        }

        private void LoadCompareOptions(SwimSettings s)
        {
            if (chkCompareDelta != null)
                chkCompareDelta.Checked = s.CompareDelta;
            if (chkCompareGhost != null)
                chkCompareGhost.Checked = s.CompareGhost;
            if (chkCompareLine != null)
                chkCompareLine.Checked = s.CompareLine;
        }

        /// <summary>What the ghost and the line need, or where they are drawn.</summary>
        private void RefreshCompareHint()
        {
            if (IsDisposed || lblCompareHint == null)
                return;

            string hint = manager.CompareHint();
            lblCompareHint.Text = hint;
            toolTip.SetToolTip(lblCompareHint, hint);
        }
    }
}
