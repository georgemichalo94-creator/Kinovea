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
    /// The comparisons with the final of the event (the 8 of an Olympic final) and the graphic of the Δ with the reference.
    /// </summary>
    public partial class FormSwimAnalysis
    {
        private SwimPage tabFinals;
        private SwimFinalsView finalsView;
        private Label lblFinals;
        private SwimDeltaView deltaView;
        private CheckBox chkDeltaChart;
        private CheckBox chkFinalsBoard;

        /// <summary>The Δ with the reference at each checkpoint, under the events of the swimming page.</summary>
        private Control BuildDeltaView()
        {
            deltaView = new SwimDeltaView { Dock = DockStyle.Top, Height = 118, Visible = false, Margin = new Padding(0, 4, 0, 4) };
            toolTip.SetToolTip(deltaView, SwimLang.T("Δ από την αναφορά σε κάθε πέρασμα: πάνω και πράσινο = πιο γρήγορα, κάτω και κόκκινο = πιο αργά."));
            return deltaView;
        }

        /// <summary>Button of the swimming page: the file of the finals (once, kept on this computer).</summary>
        private Button BuildFinalsImportButton()
        {
            Button button = new SwimNoFocusButton { Text = SwimLang.T("🏅 Τελικοί…"), AutoSize = true, Height = 25, Margin = new Padding(4, 2, 0, 2) };
            button.Click += (s, e) => ImportFinals();
            toolTip.SetToolTip(button, SwimLang.T("Φόρτωση του αρχείου με τους τελικούς (π.χ. οι 8 του ολυμπιακού τελικού σε κάθε αγώνισμα).\n") +
                SwimLang.T("Μία φορά: κρατιούνται σε αυτόν τον υπολογιστή. Κάθε φιναλίστ γίνεται αναφορά για το Δ,\n") +
                SwimLang.T("και η καρτέλα «Τελικός ΟΑ» και ο πίνακας στο βίντεο συγκρίνουν τον αθλητή με τους 8 (ίδιο αγώνισμα, στυλ, απόσταση, φύλο)."));
            return button;
        }

        /// <summary>Choices of the video: the graphic of the Δ, the board of the final at each checkpoint.</summary>
        private void AddFinalsOverlayOptions(FlowLayoutPanel overlay)
        {
            chkDeltaChart = new CheckBox { Text = SwimLang.T("Γράφημα Δ"), AutoSize = true, Margin = new Padding(6, 5, 3, 3) };
            chkFinalsBoard = new CheckBox { Text = SwimLang.T("Πίνακας τελικού"), AutoSize = true, Margin = new Padding(6, 5, 3, 3) };
            chkDeltaChart.CheckedChanged += (s, e) => CommitOverlayOptions();
            chkFinalsBoard.CheckedChanged += (s, e) => CommitOverlayOptions();
            toolTip.SetToolTip(chkDeltaChart, SwimLang.T("Γράφημα με το Δ από την αναφορά σε κάθε πέρασμα, κάτω από το γράφημα ταχύτητας στο βίντεο."));
            toolTip.SetToolTip(chkFinalsBoard, SwimLang.T("Σε κάθε σημείο (15μ., 25μ., 50μ., στροφές, τερματισμός) πίνακας με τους φιναλίστ του τελικού\n") +
                SwimLang.T("και τον αθλητή στη θέση που θα είχε, για λίγα δευτερόλεπτα, στην άλλη πλευρά του βίντεο."));
            overlay.Controls.Add(chkDeltaChart);
            overlay.Controls.Add(chkFinalsBoard);
        }

        private SwimPage BuildFinalsTab()
        {
            SwimPage page = new SwimPage(SwimLang.T("🏅 Τελικός ΟΑ"));
            page.Padding = new Padding(6);
            lblFinals = new Label { Dock = DockStyle.Top, Height = 38, ForeColor = SwimTheme.Dim, AutoEllipsis = true, Padding = new Padding(2, 2, 2, 0) };
            Panel scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = SwimTheme.Back };
            finalsView = new SwimFinalsView { Dock = DockStyle.Top };
            scroll.Controls.Add(finalsView);
            page.Controls.Add(scroll);
            page.Controls.Add(lblFinals);
            return page;
        }

        private void ImportFinals()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = SwimLang.T("Αρχείο τελικών");
                dialog.Filter = SwimLang.T("Τελικοί (*.json)|*.json|Όλα τα αρχεία (*.*)|*.*");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                manager.ImportFinals(dialog.FileName);
            }
        }

        /// <summary>The graphic of the Δ and the page of the final for the race.</summary>
        private void RefreshFinals()
        {
            SwimRace race = manager.Race;
            SwimComputed computed = manager.Computed;
            if (race == null || computed == null || deltaView == null || finalsView == null)
                return;

            // Δ with the reference.
            List<SwimDeltaPoint> points = computed.Rows.Where(r => r.T.HasValue && r.Delta.HasValue)
                .Select(r => new SwimDeltaPoint { D = r.D, Delta = r.Delta.Value }).ToList();
            deltaView.Visible = computed.Reference != null && !race.IsTraining;
            deltaView.SetData(points, race.Distance, race.Pool, computed.Reference != null ? computed.Reference.Name : null);

            // The final of the event.
            SwimFinals finals = manager.Finals;
            SwimFinal final = manager.FinalForRace();
            if (finals == null || finals.Finals.Count == 0)
            {
                lblFinals.Text = SwimLang.T("Δεν έχουν φορτωθεί τελικοί: κουμπί «🏅 Τελικοί…» στην καρτέλα 2 · Κολύμβηση (αρχείο με τους 8 κάθε τελικού).");
                finalsView.SetData(null, "", "");
                return;
            }

            if (final == null)
            {
                string gender = SwimReference.NormalizeGender(race.AthleteGender);
                lblFinals.Text = gender.Length == 0
                    ? SwimLang.T("Για τη σύγκριση χρειάζεται το φύλο του αθλητή (Στοιχεία αγώνα → αθλητής από το μητρώο).")
                    : race.Pool != 50 || race.IsYards || race.Relay
                        ? SwimLang.T("Οι τελικοί είναι σε πισίνα 50μ., ατομικά αγωνίσματα.")
                        : SwimLang.T("Δεν υπάρχει τελικός για αυτό το αγώνισμα στο αρχείο (") + finals.Source + SwimLang.T("): π.χ. τα 50μ. πεταλούδα, ύπτιο και πρόσθιο δεν είναι ολυμπιακά.");
                finalsView.SetData(null, "", "");
                return;
            }

            List<SwimFinalsMetric> metrics = SwimFinalsCompare.Metrics(race, computed, final);
            lblFinals.Text = string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}: {1}. Κάθε τιμή του αγώνα ανάμεσα στους {2} του τελικού · η θέση που θα είχε ο αθλητής."),
                final.Source, string.Join(", ", final.Swimmers.Take(3).Select(s => s.Rank.ToString(CultureInfo.InvariantCulture) + ". " + s.Name + " " + SwimReference.FormatTime(s.Final)).ToArray()) +
                (final.Swimmers.Count > 3 ? " …" : ""), final.Swimmers.Count);
            finalsView.SetData(metrics, final.Label, race.Athlete);
        }

        private void CommitFinalsOverlayOptions(SwimSettings s)
        {
            if (chkDeltaChart != null)
                s.ShowDeltaChart = chkDeltaChart.Checked;
            if (chkFinalsBoard != null)
                s.ShowFinalsBoard = chkFinalsBoard.Checked;
        }

        private void LoadFinalsOverlayOptions(SwimSettings s)
        {
            if (chkDeltaChart != null)
                chkDeltaChart.Checked = s.ShowDeltaChart;
            if (chkFinalsBoard != null)
                chkFinalsBoard.Checked = s.ShowFinalsBoard;
        }
    }

    /// <summary>The Δ with the reference at each checkpoint (panel).</summary>
    internal sealed class SwimDeltaView : Control
    {
        private List<SwimDeltaPoint> points = new List<SwimDeltaPoint>();
        private int distance = 100, pool = 50;
        private string reference;

        public SwimDeltaView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = SwimTheme.Surface;
        }

        public void SetData(List<SwimDeltaPoint> list, int raceDistance, int racePool, string referenceName)
        {
            points = list ?? new List<SwimDeltaPoint>();
            distance = raceDistance;
            pool = racePool;
            reference = referenceName;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            SwimFinalsPainter.PaintDelta(e.Graphics, ClientRectangle, points, distance, pool, reference, false, 1.25f);
        }
    }

    /// <summary>The values of the race against the swimmers of the final (panel), as tall as its lines.</summary>
    internal sealed class SwimFinalsView : Control
    {
        private List<SwimFinalsMetric> metrics = new List<SwimFinalsMetric>();
        private string title = "", athlete = "";

        public SwimFinalsView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = SwimTheme.Back;
            Height = 60;
        }

        public void SetData(List<SwimFinalsMetric> list, string finalTitle, string athleteName)
        {
            metrics = list ?? new List<SwimFinalsMetric>();
            title = finalTitle ?? "";
            athlete = athleteName ?? "";
            Height = SwimFinalsPainter.MetricsHeight(metrics);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            SwimFinalsPainter.PaintMetrics(e.Graphics, ClientRectangle, metrics, title, athlete);
        }
    }
}
