#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Kinovea.Camera;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (Swim 2.21.0) The recordings of the GoPro cameras started from Kinovea and their videos: copied from the cards (in a card reader,
    /// found by themselves, or a folder they were copied in) or downloaded by the Wi-Fi of the cameras, to the folder of the videos with
    /// the names of the athletes or of the race. A recording whose details were wrong is corrected here before its videos come. When the
    /// videos of all the cameras of a recording came, the video of all of them is made by itself (one after the other, the window can be
    /// closed meanwhile: it hides until they are made) and «▶ Προβολή» plays it. (2.22.0) A continuous recording (an order of athletes,
    /// «⏭ Επόμενος» at the end of each attempt) is shown as its parts, one per athlete: its files of a card are cut for each of them.
    /// </summary>
    public class FormGoProTransfer : Form
    {
        private static FormGoProTransfer instance;
        private static bool wifiAccepted;
        private readonly Label lblRoot = new Label();
        private readonly CheckBox cbPending = new CheckBox();
        private readonly DataGridView grid = new DataGridView();
        private readonly Label lblStatus = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Button btnCard = new Button();
        private readonly Button btnWifi = new Button();
        private readonly Button btnEdit = new Button();
        private readonly Button btnRemove = new Button();
        private readonly Button btnStop = new Button();
        private CancellationTokenSource cancel;
        private List<SwimTake> shown = new List<SwimTake>();

        // (2.21.0) The videos of all the cameras: made by themselves when the videos of a recording came (the choice kept while Kinovea runs).
        private static bool autoComposite = true;
        private readonly CheckBox cbAuto = new CheckBox();
        private readonly Button btnView = new Button();
        private readonly Button btnComposite = new Button();
        private readonly Queue<string> compositeQueue = new Queue<string>();
        private readonly HashSet<string> compositeFailed = new HashSet<string>();
        private CancellationTokenSource compositeCancel;
        private string makingTake;
        private bool making;

        #region Entry points
        /// <summary>The details of the next recordings (the button «📝» of the screens).</summary>
        public static void EditDetails(IWin32Window owner, string sampleCamera)
        {
            using (FormGoProTakeDetails form = new FormGoProTakeDetails(SwimGoProTakes.Next, SwimGoProTakes.Root, sampleCamera))
            {
                if (form.ShowDialog(owner) != DialogResult.OK || form.Result == null)
                    return;

                if (form.RootFolder != SwimGoProTakes.Root)
                    SwimGoProTakes.Root = form.RootFolder;

                SwimGoProTakes.SetNext(form.Result);
            }
        }

        /// <summary>The window of the recordings (one).</summary>
        public static void ShowWindow(IWin32Window owner)
        {
            if (instance == null || instance.IsDisposed)
            {
                instance = new FormGoProTransfer();
                instance.Show(owner);
            }
            else
            {
                // Hidden while it makes the videos of all the cameras.
                if (!instance.Visible)
                    instance.Show();

                instance.Activate();
            }
        }
        #endregion

        private FormGoProTransfer()
        {
            Text = SwimLang.T("📥 Βίντεο GoPro στον υπολογιστή");
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(980, 560);
            MinimumSize = new Size(760, 420);
            Font = new Font("Segoe UI", 9f);
            ShowInTaskbar = false;

            // The folder.
            Panel top = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 10, 10, 4) };
            lblRoot.AutoSize = true;
            lblRoot.Location = new Point(10, 14);
            Button btnRoot = new Button { Text = SwimLang.T("Φάκελος…"), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Button btnOpen = new Button { Text = SwimLang.T("📂 Άνοιγμα"), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnRoot.Click += (s, e) => ChooseRoot();
            btnOpen.Click += (s, e) => OpenRoot();
            top.Controls.Add(lblRoot);
            top.Controls.Add(btnRoot);
            top.Controls.Add(btnOpen);
            top.Resize += (s, e) =>
            {
                btnOpen.Location = new Point(top.ClientSize.Width - btnOpen.Width - 10, 8);
                btnRoot.Location = new Point(btnOpen.Left - btnRoot.Width - 6, 8);
            };

            // The recordings.
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.BackgroundColor = Color.White;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add("when", SwimLang.T("Ώρα"));
            grid.Columns.Add("kind", SwimLang.T("Είδος"));
            grid.Columns.Add("who", SwimLang.T("Αθλητής ή αγώνισμα"));
            grid.Columns.Add("cameras", SwimLang.T("Κάμερες (✓ στον υπολογιστή)"));
            grid.Columns.Add("composite", "🎬");
            grid.Columns[0].FillWeight = 15;
            grid.Columns[1].FillWeight = 10;
            grid.Columns[2].FillWeight = 42;
            grid.Columns[3].FillWeight = 27;
            grid.Columns[4].FillWeight = 6;
            grid.Columns[4].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns[4].ToolTipText = SwimLang.T("Το ενοποιημένο βίντεο των καμερών (✓ έγινε · ⏳ γίνεται · … περιμένει · ⚠ δεν έγινε). Διπλό κλικ: προβολή.");
            grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == 4)
                    ViewSelected();
                else
                    EditSelected();
            };

            // The actions: the videos to the computer and the video of all the cameras, then the list.
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 80, Padding = new Padding(8, 6, 8, 2), WrapContents = true };
            btnCard.Text = SwimLang.T("💾 Από κάρτα ή φάκελο…");
            btnWifi.Text = SwimLang.T("📶 Κατέβασμα με Wi-Fi");
            btnEdit.Text = SwimLang.T("📝 Διόρθωση στοιχείων");
            btnRemove.Text = SwimLang.T("🗑 Έξω από τη λίστα");
            btnStop.Text = SwimLang.T("■ Διακοπή");
            btnView.Text = SwimLang.T("▶ Προβολή");
            btnComposite.Text = SwimLang.T("🎬 Ενοποιημένο βίντεο");
            cbPending.Text = SwimLang.T("Σήμερα και όσα περιμένουν");
            cbPending.Checked = true;
            cbPending.AutoSize = true;
            cbPending.Margin = new Padding(14, 7, 0, 0);
            cbAuto.Text = SwimLang.T("🎬 Ενοποιημένο μόνο του");
            cbAuto.Checked = autoComposite;
            cbAuto.AutoSize = true;
            cbAuto.Margin = new Padding(14, 7, 0, 0);
            foreach (Button b in new[] { btnCard, btnWifi, btnStop, btnView, btnComposite, btnEdit, btnRemove })
            {
                b.AutoSize = true;
                actions.Controls.Add(b);
            }

            btnView.Margin = new Padding(18, 3, 3, 3);
            btnView.Font = new Font(Font, FontStyle.Bold);
            actions.SetFlowBreak(btnComposite, true);
            actions.Controls.Add(cbPending);
            actions.Controls.Add(cbAuto);
            btnStop.Enabled = false;
            btnCard.Click += (s, e) => ImportFromFolder();
            btnWifi.Click += (s, e) => DownloadByWifi();
            btnEdit.Click += (s, e) => EditSelected();
            btnRemove.Click += (s, e) => RemoveSelected();
            btnView.Click += (s, e) => ViewSelected();
            btnComposite.Click += (s, e) => MakeSelected();
            btnStop.Click += (s, e) =>
            {
                if (cancel != null)
                    cancel.Cancel();

                if (compositeCancel != null)
                    compositeCancel.Cancel();
            };
            cbPending.CheckedChanged += (s, e) => Fill();
            cbAuto.CheckedChanged += (s, e) => autoComposite = cbAuto.Checked;

            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(10, 4, 10, 6) };
            lblStatus.Dock = DockStyle.Top;
            lblStatus.Height = 44;
            progress.Dock = DockStyle.Bottom;
            progress.Height = 14;
            bottom.Controls.Add(lblStatus);
            bottom.Controls.Add(progress);

            Controls.Add(grid);
            Controls.Add(actions);
            Controls.Add(bottom);
            Controls.Add(top);

            SwimGoProTakes.Changed += Takes_Changed;
            FormClosing += (s, e) =>
            {
                // The videos of all the cameras go on: the window hides until they are made (closed with the program).
                if (making && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
            FormClosed += (s, e) =>
            {
                SwimGoProTakes.Changed -= Takes_Changed;
                if (cancel != null)
                    cancel.Cancel();

                if (compositeCancel != null)
                    compositeCancel.Cancel();

                // The program closes while a video is made: its ffmpeg stops now and its temporary file goes.
                if (making)
                    SwimTakeComposite.StopAll();
            };

            Fill();
            lblStatus.Text = SwimLang.T("Οι εγγραφές που ξεκίνησαν από το Kinovea. Κάρτες σε card reader: «Από κάρτα» · λίγες εγγραφές: «Κατέβασμα με Wi-Fi». Όταν έρθουν τα βίντεο όλων των καμερών μιας εγγραφής, γίνεται μόνο του το ενοποιημένο βίντεο (🎬) · «▶ Προβολή» για να το δεις.");
        }

        private void Takes_Changed(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
                BeginInvoke((Action)Fill);
            else
                Fill();
        }

        #region List
        private void Fill()
        {
            string root = SwimGoProTakes.Root;
            lblRoot.Text = SwimLang.T("Φάκελος βίντεο: ") + (root.Length > 0 ? root : SwimLang.T("(δεν έχει διαλεχτεί)"));
            List<SwimTake> takes = SwimGoProTakes.Takes.Where(t => !t.IsContinuous).ToList();
            if (cbPending.Checked)
            {
                DateTime today = DateTime.Today;
                takes = takes.Where(t => !t.AllDone || t.Start.Date == today || t.Id == makingTake || compositeQueue.Contains(t.Id)).ToList();
            }

            takes.Reverse();
            HashSet<string> selected = new HashSet<string>(SelectedTakes().Select(t => t.Id));
            shown = takes;
            grid.SuspendLayout();
            grid.Rows.Clear();
            foreach (SwimTake t in takes)
            {
                string cameras = string.Join(" · ", t.Cameras.Select(c => c.Alias + (c.Done ? " ✓" : " …")));
                string who = (t.IsPart ? "#" + t.Index.ToString(CultureInfo.InvariantCulture) + "  " : "") + t.Details.Summary;
                int row = grid.Rows.Add(t.Start.ToString("dd/MM HH:mm:ss", CultureInfo.InvariantCulture), t.Details.KindName, who, cameras, CompositeMark(t));
                grid.Rows[row].Tag = t;
                if (selected.Contains(t.Id))
                    grid.Rows[row].Selected = true;

                if (t.AllDone)
                    grid.Rows[row].DefaultCellStyle.ForeColor = Color.SeaGreen;
            }

            grid.ResumeLayout();
        }

        /// <summary>The video of all the cameras of a recording: ✓ made, ⏳ being made, … waiting, ⚠ not made (what went wrong is told).</summary>
        private string CompositeMark(SwimTake t)
        {
            if (t.Id == makingTake)
                return "⏳";

            if (SwimTakeComposite.HasComposite(t))
                return "✓";

            if (compositeQueue.Contains(t.Id))
                return "…";

            return compositeFailed.Contains(t.Id) ? "⚠" : "";
        }

        private void SelectTake(string id)
        {
            if (id == null)
                return;

            foreach (DataGridViewRow row in grid.Rows)
            {
                SwimTake t = row.Tag as SwimTake;
                bool mine = t != null && t.Id == id;
                row.Selected = mine;
                if (mine)
                {
                    try
                    {
                        grid.FirstDisplayedScrollingRowIndex = row.Index;
                    }
                    catch (Exception) { }
                }
            }
        }

        private List<SwimTake> SelectedTakes()
        {
            List<SwimTake> result = new List<SwimTake>();
            foreach (DataGridViewRow row in grid.SelectedRows)
            {
                SwimTake t = row.Tag as SwimTake;
                if (t != null)
                    result.Add(t);
            }

            return result;
        }

        private void EditSelected()
        {
            List<SwimTake> selected = SelectedTakes();
            if (selected.Count != 1)
            {
                Status(SwimLang.T("Διάλεξε μία εγγραφή για να διορθώσεις τα στοιχεία της."), false);
                return;
            }

            SwimTake take = selected[0];
            string camera = take.Cameras.Count > 0 ? take.Cameras[0].Alias : "";
            using (FormGoProTakeDetails form = new FormGoProTakeDetails(take.Details, SwimGoProTakes.Root, camera, false))
            {
                form.Text = SwimLang.T("📝 Στοιχεία της εγγραφής ") + take.Start.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                if (form.ShowDialog(this) != DialogResult.OK || form.Result == null)
                    return;

                if (form.RootFolder != SwimGoProTakes.Root)
                    SwimGoProTakes.Root = form.RootFolder;

                SwimGoProTakes.SetDetails(take.Id, form.Result);
            }
        }

        private void RemoveSelected()
        {
            List<SwimTake> selected = SelectedTakes();
            if (selected.Count == 0)
                return;

            string text = selected.Count == 1 ?
                SwimLang.T("Να βγει η εγγραφή από τη λίστα; Τα βίντεο στις κάρτες και στον υπολογιστή δεν σβήνονται.") :
                string.Format(CultureInfo.InvariantCulture, SwimLang.T("Να βγουν {0} εγγραφές από τη λίστα; Τα βίντεο στις κάρτες και στον υπολογιστή δεν σβήνονται."), selected.Count);
            if (MessageBox.Show(this, text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                SwimGoProTakes.Remove(selected.Select(t => t.Id));
        }

        private void ChooseRoot()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = SwimLang.T("Ο φάκελος όπου μπαίνουν οι φάκελοι της κάθε μέρας (π.χ. E:\\).");
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(SwimGoProTakes.Root))
                    dialog.SelectedPath = SwimGoProTakes.Root;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    SwimGoProTakes.Root = dialog.SelectedPath;
            }
        }

        private void OpenRoot()
        {
            string root = SwimGoProTakes.Root;
            if (root.Length == 0 || !Directory.Exists(root))
                return;

            try
            {
                Process.Start("explorer.exe", "\"" + root + "\"");
            }
            catch (Exception) { }
        }

        /// <summary>The folder of the videos, asked when not chosen yet; null when the user does not choose one.</summary>
        private string EnsureRoot()
        {
            if (SwimGoProTakes.Root.Length == 0 || !Directory.Exists(Path.GetPathRoot(SwimGoProTakes.Root) ?? ""))
                ChooseRoot();

            string root = SwimGoProTakes.Root;
            return root.Length > 0 ? root : null;
        }
        #endregion

        #region Busy
        private void SetBusy(bool busy)
        {
            btnCard.Enabled = !busy;
            btnWifi.Enabled = !busy;
            btnEdit.Enabled = !busy;
            btnRemove.Enabled = !busy;
            btnComposite.Enabled = !busy;
            btnStop.Enabled = busy;
            if (!busy)
                progress.Value = 0;
        }

        private void Status(string text, bool warning)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = warning ? Color.FromArgb(170, 70, 30) : SystemColors.ControlText;
        }

        /// <summary>(2.22.0) The cut of the parts of a continuous recording: the video and the part done of all.</summary>
        private void ShowCutProgress(string text, double done)
        {
            if (IsDisposed)
                return;

            Status("✂ " + text + string.Format(CultureInfo.InvariantCulture, " · {0:0}%", done * 100), false);
            progress.Value = (int)Math.Max(0, Math.Min(100, done * 100));
        }

        private void ShowProgress(string text, long done, long total)
        {
            if (IsDisposed)
                return;

            Status(text + (total > 0 ? string.Format(CultureInfo.InvariantCulture, " · {0:0.0} / {1:0.0} GB", done / 1073741824.0, total / 1073741824.0) : ""), false);
            progress.Value = total > 0 ? (int)Math.Max(0, Math.Min(100, done * 100 / total)) : 0;
        }
        #endregion

        #region From a card or a folder
        private async void ImportFromFolder()
        {
            string root = EnsureRoot();
            if (root == null)
                return;

            List<string> folders = ChooseSources();
            if (folders == null || folders.Count == 0)
                return;

            cancel = new CancellationTokenSource();
            CancellationToken token = cancel.Token;
            SetBusy(true);
            List<string> touched = new List<string>();
            bool stopped = false;
            try
            {
                Status(SwimLang.T("Διαβάζονται τα βίντεο…"), false);
                List<SwimTake> takes = SwimGoProTakes.Takes;
                List<SwimGoProFile> files = await Task.Run(() => folders.SelectMany(f => SwimGoProFiles.Scan(f, token)).ToList());
                if (files.Count == 0)
                {
                    Status(SwimLang.T("Δεν βρέθηκαν βίντεο GoPro σε αυτόν τον φάκελο."), true);
                    return;
                }

                // A card holds the files of one camera; a folder may hold the copies of several: by serial number.
                List<SwimGoProMatch> matches = new List<SwimGoProMatch>();
                foreach (IGrouping<string, SwimGoProFile> group in files.GroupBy(f => f.Serial ?? ""))
                {
                    List<SwimGoProFile> groupFiles = group.ToList();
                    string alias = SwimGoProFiles.CameraOf(takes, groupFiles);
                    if (alias == null)
                        alias = AskCamera(takes, groupFiles.Count);

                    if (alias == null)
                        continue;

                    matches.AddRange(SwimGoProFiles.Match(takes, alias, groupFiles));
                }

                if (matches.Count == 0)
                {
                    Status(SwimLang.T("Τα βίντεο αυτής της κάρτας δεν ταιριάζουν με εγγραφές που δεν ήρθαν ακόμη (ώρα ή κάμερα)."), true);
                    return;
                }

                // (2.22.0) The continuous recordings are cut for their athletes; the others copied whole.
                List<SwimGoProMatch> copies = matches.Where(m => !m.Take.IsContinuous).ToList();
                List<SwimGoProMatch> cuts = matches.Where(m => m.Take.IsContinuous).ToList();
                SwimGoProFiles.PlanTargets(root, copies);
                HashSet<string> taken = new HashSet<string>(copies.SelectMany(m => m.Targets).Select(t => t.ToLowerInvariant()));
                List<SwimCutJob> jobs = new List<SwimCutJob>();
                foreach (SwimGoProMatch m in cuts)
                    jobs.AddRange(SwimGoProCut.Plan(root, m, SwimGoProTakes.Parts(m.Take.Id), taken));

                long bytes = copies.SelectMany(m => m.Files).Sum(f => Math.Max(0, f.Size));
                int count = copies.Sum(m => m.Files.Count);
                int takeCount = copies.Select(m => m.Take.Id).Distinct().Count();
                string question;
                if (cuts.Count == 0)
                {
                    question = string.Format(CultureInfo.InvariantCulture,
                        takeCount == 1 ?
                            SwimLang.T("Βρέθηκαν {0} βίντεο για 1 εγγραφή ({2:0.0} GB). Αντιγραφή στον φάκελο {3};\n\nΤα αρχεία της κάρτας μένουν όπως είναι.") :
                            SwimLang.T("Βρέθηκαν {0} βίντεο για {1} εγγραφές ({2:0.0} GB). Αντιγραφή στον φάκελο {3};\n\nΤα αρχεία της κάρτας μένουν όπως είναι."),
                        count, takeCount, bytes / 1073741824.0, root);
                }
                else
                {
                    double cutBytes = jobs.SelectMany(j => j.Pieces).Sum(x => x.File.Duration > 0 ? x.File.Size * (x.To - x.From) / x.File.Duration : 0);
                    List<string> found = new List<string>();
                    if (copies.Count > 0)
                        found.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("• {0} βίντεο για {1} εγγραφές ({2:0.0} GB): αντιγραφή"), count, takeCount, bytes / 1073741824.0));

                    found.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("• {0} συνεχόμενα βίντεο: κοπή σε {1} κομμάτια, ένα για κάθε αθλητή ({2:0.0} GB)"),
                        cuts.Count, jobs.Count(j => j.Pieces.Count > 0), cutBytes / 1073741824.0));
                    question = SwimLang.T("Βρέθηκαν:") + "\n" + string.Join("\n", found) + "\n\n" +
                        string.Format(CultureInfo.InvariantCulture, SwimLang.T("Στον φάκελο {0};\n\nΤα αρχεία της κάρτας μένουν όπως είναι."), root);
                }

                if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    Status("", false);
                    return;
                }

                Progress<KeyValuePair<string, long[]>> reporter = new Progress<KeyValuePair<string, long[]>>(p => ShowProgress(p.Key, p.Value[0], p.Value[1]));
                IProgress<KeyValuePair<string, long[]>> report = reporter;
                Progress<KeyValuePair<string, double>> cutReporter = new Progress<KeyValuePair<string, double>>(p => ShowCutProgress(p.Key, p.Value));
                IProgress<KeyValuePair<string, double>> cutReport = cutReporter;
                string error = null;
                int cutCount = 0;
                int copied = await Task.Run(() =>
                {
                    string copyError = null, cutError = null;
                    int n = 0;
                    if (copies.Count > 0)
                        n = SwimGoProFiles.Copy(copies, (name, done, total) => report.Report(new KeyValuePair<string, long[]>(name, new[] { done, total })), token, out copyError);

                    if (jobs.Count > 0)
                        cutCount = SwimGoProCut.Run(SwimTakeComposite.Ffmpeg, jobs, (name, done) => cutReport.Report(new KeyValuePair<string, double>(name, done)), token, out cutError);

                    foreach (SwimGoProMatch m in cuts)
                        SwimGoProCut.Finish(m);

                    error = copyError ?? cutError;
                    return n;
                });

                touched.AddRange(copies.Select(m => m.Take.Id));
                touched.AddRange(jobs.Select(j => j.Part.Id));

                string result = jobs.Count == 0 ?
                    string.Format(CultureInfo.InvariantCulture, SwimLang.T("✓ Αντιγράφηκαν {0} βίντεο στον φάκελο."), copied) :
                    string.Format(CultureInfo.InvariantCulture, SwimLang.T("✓ Αντιγράφηκαν {0} βίντεο και κόπηκαν {1} κομμάτια αθλητών."), copied, cutCount);
                if (error != null)
                    Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Αντιγράφηκαν {0} βίντεο· πρόβλημα: {1}"), copied + cutCount, error), true);
                else
                    Status(result, false);
            }
            catch (OperationCanceledException)
            {
                stopped = true;
                Status(SwimLang.T("Σταμάτησε. Ό,τι αντιγράφηκε ολόκληρο έμεινε."), true);
            }
            catch (Exception e)
            {
                Status(SwimLang.T("Η αντιγραφή σταμάτησε από λάθος: ") + e.Message, true);
            }
            finally
            {
                SetBusy(false);
                Fill();
            }

            if (!stopped)
                AutoComposites(touched);
        }

        /// <summary>
        /// Where the videos are: the GoPro cards in the computer (found by themselves), else or instead a folder the user chooses (a card
        /// or the files of a camera copied in a folder). Null when cancelled.
        /// </summary>
        private List<string> ChooseSources()
        {
            List<string> cards = SwimGoProFiles.FindCards();
            if (cards.Count > 0)
            {
                string text = string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("Κάρτες GoPro στον υπολογιστή: {0}\n\nΝαι: αντιγραφή από αυτές · Όχι: διαλέγεις άλλο φάκελο."), string.Join("  ", cards));
                DialogResult answer = MessageBox.Show(this, text, Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Yes)
                    return cards;

                if (answer != DialogResult.No)
                    return null;
            }

            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = SwimLang.T("Η κάρτα της κάμερας (στο card reader) ή ένας φάκελος με τα αρχεία της.");
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return null;

                return new List<string> { dialog.SelectedPath };
            }
        }

        /// <summary>A file just downloaded from a camera says the serial number written in its files: kept when not the Bluetooth one.</summary>
        private static void LearnSerial(IRemoteMediaSource source, List<KeyValuePair<string, string>> downloaded)
        {
            try
            {
                if (downloaded.Count == 0 || string.IsNullOrEmpty(source.CameraSerial))
                    return;

                SwimGoProFile file = SwimGoProFiles.Read(downloaded[0].Value);
                if (!string.IsNullOrEmpty(file.Serial))
                    SwimGoProTakes.LearnFileSerial(source.CameraSerial, file.Serial);
            }
            catch (Exception)
            {
                // Only a help for the cards.
            }
        }

        /// <summary>The camera of files that do not say it: the user chooses among the cameras of the recordings.</summary>
        private string AskCamera(List<SwimTake> takes, int files)
        {
            List<string> aliases = takes.Where(t => !t.AllDone).SelectMany(t => t.Cameras).Where(c => !c.Done).Select(c => c.Alias).Distinct().ToList();
            if (aliases.Count == 0)
                return null;

            if (aliases.Count == 1)
                return aliases[0];

            using (Form ask = new Form())
            {
                ask.Text = Text;
                ask.StartPosition = FormStartPosition.CenterParent;
                ask.FormBorderStyle = FormBorderStyle.FixedDialog;
                ask.MinimizeBox = false;
                ask.MaximizeBox = false;
                ask.ShowInTaskbar = false;
                ask.ClientSize = new Size(440, 150);
                Label l = new Label
                {
                    Text = string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} βίντεο δεν λένε ποια κάμερα τα έγραψε. Από ποια κάμερα είναι αυτή η κάρτα;"), files),
                    Location = new Point(12, 12),
                    Size = new Size(416, 40)
                };
                ComboBox combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(12, 60), Width = 260 };
                combo.Items.AddRange(aliases.Cast<object>().ToArray());
                combo.SelectedIndex = 0;
                Button ok = new Button { Text = SwimLang.T("Εντάξει"), DialogResult = DialogResult.OK, Location = new Point(246, 108), Width = 88 };
                Button skip = new Button { Text = SwimLang.T("Παράλειψη"), DialogResult = DialogResult.Cancel, Location = new Point(340, 108), Width = 88 };
                ask.Controls.AddRange(new Control[] { l, combo, ok, skip });
                ask.AcceptButton = ok;
                ask.CancelButton = skip;
                return ask.ShowDialog(this) == DialogResult.OK ? combo.SelectedItem as string : null;
            }
        }
        #endregion

        #region By Wi-Fi
        private async void DownloadByWifi()
        {
            string root = EnsureRoot();
            if (root == null)
                return;

            List<SwimTake> takes = SelectedTakes();
            if (takes.Count == 0)
                takes = SwimGoProTakes.Takes.Where(t => !t.AllDone).ToList();

            // (2.22.0) A continuous recording is long and cut for its athletes: from the cards.
            bool continuous = takes.Any(t => t.IsPart || t.IsContinuous);
            takes = takes.Where(t => !t.IsPart && !t.IsContinuous).ToList();
            if (takes.Count == 0 && continuous)
            {
                Status(SwimLang.T("Οι συνεχόμενες εγγραφές κόβονται από τις κάρτες: «💾 Από κάρτα ή φάκελο»."), true);
                return;
            }

            List<string> wanted = takes.SelectMany(t => t.Cameras).Where(c => !c.Done).Select(c => c.Alias).Distinct().ToList();
            List<KeyValuePair<string, IRemoteMediaSource>> sources = SwimGoProTakes.MediaSources().Where(s => wanted.Contains(s.Key)).ToList();
            if (wanted.Count == 0)
            {
                Status(SwimLang.T("Όλα τα βίντεο αυτών των εγγραφών είναι ήδη στον υπολογιστή."), false);
                return;
            }

            if (sources.Count == 0)
            {
                Status(SwimLang.T("Οι κάμερες αυτών των εγγραφών δεν είναι σε οθόνες εγγραφής: «GoPro: όλες σε οθόνες» και ξαναδοκίμασε."), true);
                return;
            }

            if (!wifiAccepted)
            {
                string text = SwimLang.T("Για το κατέβασμα ο υπολογιστής μπαίνει με τη σειρά στο Wi-Fi κάθε κάμερας: όσο κρατά δεν έχει ίντερνετ (με ένα Wi-Fi) και μετά ξαναγυρίζει μόνος του στο δίκτυό του.\n\nΤο Wi-Fi είναι αργό για πολλά βίντεο (περίπου 1 – 3 λεπτά ανά εγγραφή και κάμερα): για όλη τη μέρα είναι πιο γρήγορη η κάρτα.\n\nΣυνέχεια;");
                if (MessageBox.Show(this, text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                    return;

                wifiAccepted = true;
            }

            cancel = new CancellationTokenSource();
            CancellationToken token = cancel.Token;
            SetBusy(true);
            int got = 0;
            List<string> problems = new List<string>();
            List<string> touched = new List<string>();
            bool stopped = false;
            try
            {
                foreach (string missing in wanted.Where(w => sources.All(s => s.Key != w)))
                    problems.Add(missing + ": " + SwimLang.T("δεν είναι σε οθόνη"));

                foreach (KeyValuePair<string, IRemoteMediaSource> source in sources)
                {
                    token.ThrowIfCancellationRequested();
                    string alias = source.Key;
                    List<SwimGoProMatch> matched = null;
                    Progress<RemoteMediaProgress> reporter = new Progress<RemoteMediaProgress>(p => ShowProgress(p.Text, p.Done, p.Total));
                    string error = await source.Value.DownloadAsync(list =>
                    {
                        List<SwimGoProFile> files = SwimGoProFiles.FromRemote(list);
                        matched = SwimGoProFiles.Match(takes, alias, files);
                        SwimGoProFiles.PlanTargets(root, matched);
                        List<RemoteMediaDownload> downloads = new List<RemoteMediaDownload>();
                        foreach (SwimGoProMatch m in matched)
                        {
                            for (int i = 0; i < m.Files.Count; i++)
                            {
                                if (m.Present[i])
                                    continue;

                                SwimGoProFile f = m.Files[i];
                                RemoteMediaFile r = list.FirstOrDefault(x => x.Folder == f.Folder && x.Name == f.Name);
                                if (r != null)
                                    downloads.Add(new RemoteMediaDownload { File = r, TargetPath = m.Targets[i] });
                            }
                        }

                        return downloads;
                    }, reporter, token);

                    // What came whole is kept, even when the download stopped later (the next attempt takes only the rest).
                    List<KeyValuePair<string, string>> log = new List<KeyValuePair<string, string>>();
                    foreach (SwimGoProMatch m in matched ?? new List<SwimGoProMatch>())
                    {
                        touched.Add(m.Take.Id);
                        bool whole = true;
                        for (int i = 0; i < m.Files.Count; i++)
                            whole &= SwimGoProFiles.IsWhole(m.Targets[i], m.Files[i].Size) || (m.Files[i].Size <= 0 && File.Exists(m.Targets[i]));

                        if (!whole)
                            continue;

                        SwimGoProTakes.MarkDone(m.Take.Id, alias, string.Join(", ", m.Files.Select(x => x.Name)), m.Targets);
                        for (int i = 0; i < m.Files.Count; i++)
                        {
                            if (m.Present[i])
                                continue;

                            log.Add(new KeyValuePair<string, string>(alias + ":" + m.Files[i].Folder + "/" + m.Files[i].Name, m.Targets[i]));
                            got++;
                        }
                    }

                    // The serial number in the files of this camera (its card is found by it).
                    LearnSerial(source.Value, log);

                    if (error != null)
                        problems.Add(alias + ": " + error);
                    else if (matched == null || matched.Count == 0)
                        problems.Add(alias + ": " + SwimLang.T("δεν βρέθηκαν τα βίντεο αυτών των εγγραφών στην κάρτα"));

                    SwimGoProFiles.LogDownloads(log);
                }

                if (problems.Count == 0)
                    Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("✓ Κατέβηκαν {0} βίντεο στον φάκελο."), got), false);
                else
                    Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Κατέβηκαν {0} βίντεο. "), got) + string.Join(" · ", problems), true);
            }
            catch (OperationCanceledException)
            {
                stopped = true;
                Status(SwimLang.T("Σταμάτησε. Ό,τι κατέβηκε ολόκληρο έμεινε."), true);
            }
            catch (Exception e)
            {
                Status(SwimLang.T("Το κατέβασμα σταμάτησε από λάθος: ") + e.Message, true);
            }
            finally
            {
                SetBusy(false);
                Fill();
            }

            if (!stopped)
                AutoComposites(touched);
        }
        #endregion

        #region The video of all the cameras
        /// <summary>The recordings that just got videos: the video of all their cameras when all came (if chosen).</summary>
        private void AutoComposites(IEnumerable<string> takeIds)
        {
            if (!autoComposite)
                return;

            HashSet<string> wanted = new HashSet<string>(takeIds);
            if (wanted.Count == 0)
                return;

            foreach (SwimTake t in SwimGoProTakes.Takes.Where(x => wanted.Contains(x.Id)))
            {
                if (SwimTakeComposite.Ready(t) && !compositeFailed.Contains(t.Id) && !compositeQueue.Contains(t.Id) && t.Id != makingTake)
                    compositeQueue.Enqueue(t.Id);
            }

            RunComposites();
        }

        /// <summary>The button «🎬»: the video of all the cameras of the chosen recordings (again when made).</summary>
        private void MakeSelected()
        {
            List<SwimTake> selected = SelectedTakes();
            if (selected.Count == 0)
            {
                Status(SwimLang.T("Διάλεξε μια εγγραφή για το ενοποιημένο βίντεο των καμερών της."), false);
                return;
            }

            List<SwimTake> able = selected.Where(t => SwimTakeComposite.Views(t).Count >= 2).ToList();
            if (able.Count == 0)
            {
                Status(SwimLang.T("Για το ενοποιημένο βίντεο χρειάζονται στον υπολογιστή τα βίντεο τουλάχιστον δύο καμερών της εγγραφής."), true);
                return;
            }

            if (able.Any(SwimTakeComposite.HasComposite))
            {
                string text = able.Count == 1 ?
                    SwimLang.T("Υπάρχει ήδη το ενοποιημένο βίντεο αυτής της εγγραφής. Να ξαναγίνει;") :
                    SwimLang.T("Κάποιες από αυτές τις εγγραφές έχουν ήδη ενοποιημένο βίντεο. Να ξαναγίνουν;");
                if (MessageBox.Show(this, text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }

            foreach (SwimTake t in able)
            {
                compositeFailed.Remove(t.Id);
                if (!compositeQueue.Contains(t.Id) && t.Id != makingTake)
                    compositeQueue.Enqueue(t.Id);
            }

            RunComposites();
        }

        /// <summary>The video of all the cameras of the chosen recording, in the player of the computer.</summary>
        private void ViewSelected()
        {
            SwimTake take = SelectedTakes().FirstOrDefault(SwimTakeComposite.HasComposite);
            if (take == null)
            {
                Status(SwimLang.T("Διάλεξε μια εγγραφή με ενοποιημένο βίντεο (🎬 ✓)."), false);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(take.Composite) { UseShellExecute = true });
            }
            catch (Exception e)
            {
                Status(SwimLang.T("Το βίντεο δεν άνοιξε: ") + e.Message, true);
            }
        }

        private static string TakeLabel(SwimTake take)
        {
            return take.Details.Summary + " · " + take.Start.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>The videos waiting, one after the other (the program stays quick: ffmpeg below the normal priority).</summary>
        private async void RunComposites()
        {
            if (making || compositeQueue.Count == 0)
                return;

            making = true;
            compositeCancel = new CancellationTokenSource();
            CancellationToken token = compositeCancel.Token;
            SetBusy(true);
            int made = 0;
            string lastMade = null;
            bool stopped = false;
            List<string> problems = new List<string>();
            try
            {
                while (compositeQueue.Count > 0)
                {
                    token.ThrowIfCancellationRequested();
                    string id = compositeQueue.Dequeue();
                    SwimTake take = SwimGoProTakes.Takes.FirstOrDefault(t => t.Id == id);
                    if (take == null)
                        continue;

                    makingTake = id;
                    Fill();
                    string label = TakeLabel(take);
                    int left = compositeQueue.Count;
                    Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("🎬 Γίνεται το ενοποιημένο βίντεο: {0}"), label) +
                        (left > 0 ? string.Format(CultureInfo.InvariantCulture, SwimLang.T(" · περιμένουν ακόμη {0}"), left) : ""), false);
                    progress.Value = 0;
                    Progress<double> reporter = new Progress<double>(p =>
                    {
                        if (!IsDisposed)
                            progress.Value = (int)Math.Max(0, Math.Min(100, p * 100));
                    });
                    IProgress<double> report = reporter;
                    string output = SwimTakeComposite.OutputPath(take);
                    string error;
                    if (output == null)
                    {
                        error = SwimLang.T("χρειάζονται τα βίντεο τουλάχιστον δύο καμερών");
                    }
                    else
                    {
                        try
                        {
                            error = await Task.Run(() =>
                            {
                                List<SwimCompositeView> views;
                                return SwimTakeComposite.Make(SwimTakeComposite.Ffmpeg, take, output, p => report.Report(p), token, out views);
                            });
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            error = e.Message;
                        }
                    }

                    if (error == null)
                    {
                        SwimGoProTakes.SetComposite(id, output);
                        compositeFailed.Remove(id);
                        made++;
                        lastMade = id;
                    }
                    else
                    {
                        compositeFailed.Add(id);
                        problems.Add(label + ": " + error);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                stopped = true;
            }
            catch (Exception e)
            {
                problems.Add(e.Message);
            }
            finally
            {
                // Stopped (or an error): what waited is not made by itself (the button «🎬» makes it).
                compositeQueue.Clear();
                making = false;
                makingTake = null;
                if (!IsDisposed)
                {
                    SetBusy(false);
                    Fill();
                }
            }

            if (IsDisposed)
                return;

            if (stopped)
            {
                Status(SwimLang.T("Σταμάτησε το ενοποιημένο βίντεο."), true);
            }
            else if (problems.Count > 0)
            {
                Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Ενοποιημένα βίντεο: {0} έγιναν. Δεν έγινε: "), made) + string.Join(" · ", problems), true);
            }
            else if (made > 0)
            {
                Status(made == 1 ?
                    SwimLang.T("✓ Έγινε το ενοποιημένο βίντεο. «▶ Προβολή» για να το δεις.") :
                    string.Format(CultureInfo.InvariantCulture, SwimLang.T("✓ Έγιναν {0} ενοποιημένα βίντεο. «▶ Προβολή» για να τα δεις."), made), false);
                SelectTake(lastMade);
            }
        }
        #endregion
    }
}
