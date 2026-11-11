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
    /// the names of the athletes or of the race. A recording whose details were wrong is corrected here before its videos come.
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
            grid.Columns[0].FillWeight = 16;
            grid.Columns[1].FillWeight = 10;
            grid.Columns[2].FillWeight = 44;
            grid.Columns[3].FillWeight = 30;
            grid.CellDoubleClick += (s, e) => EditSelected();

            // The actions.
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8, 8, 8, 4), WrapContents = false };
            btnCard.Text = SwimLang.T("💾 Από κάρτα ή φάκελο…");
            btnWifi.Text = SwimLang.T("📶 Κατέβασμα με Wi-Fi");
            btnEdit.Text = SwimLang.T("📝 Διόρθωση στοιχείων");
            btnRemove.Text = SwimLang.T("🗑 Έξω από τη λίστα");
            btnStop.Text = SwimLang.T("■ Διακοπή");
            cbPending.Text = SwimLang.T("Μόνο όσα δεν ήρθαν");
            cbPending.Checked = true;
            cbPending.AutoSize = true;
            cbPending.Margin = new Padding(14, 7, 0, 0);
            foreach (Button b in new[] { btnCard, btnWifi, btnEdit, btnRemove, btnStop })
            {
                b.AutoSize = true;
                actions.Controls.Add(b);
            }

            actions.Controls.Add(cbPending);
            btnStop.Enabled = false;
            btnCard.Click += (s, e) => ImportFromFolder();
            btnWifi.Click += (s, e) => DownloadByWifi();
            btnEdit.Click += (s, e) => EditSelected();
            btnRemove.Click += (s, e) => RemoveSelected();
            btnStop.Click += (s, e) =>
            {
                if (cancel != null)
                    cancel.Cancel();
            };
            cbPending.CheckedChanged += (s, e) => Fill();

            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(10, 4, 10, 6) };
            lblStatus.Dock = DockStyle.Top;
            lblStatus.Height = 36;
            progress.Dock = DockStyle.Bottom;
            progress.Height = 14;
            bottom.Controls.Add(lblStatus);
            bottom.Controls.Add(progress);

            Controls.Add(grid);
            Controls.Add(actions);
            Controls.Add(bottom);
            Controls.Add(top);

            SwimGoProTakes.Changed += Takes_Changed;
            FormClosed += (s, e) =>
            {
                SwimGoProTakes.Changed -= Takes_Changed;
                if (cancel != null)
                    cancel.Cancel();
            };

            Fill();
            lblStatus.Text = SwimLang.T("Οι εγγραφές που ξεκίνησαν από το Kinovea. Βάλε τις κάρτες των καμερών σε card reader και πάτα «Από κάρτα» (ή διάλεξε φάκελο με τα αρχεία τους)· ή «Κατέβασμα με Wi-Fi» για λίγες εγγραφές.");
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
            List<SwimTake> takes = SwimGoProTakes.Takes;
            if (cbPending.Checked)
                takes = takes.Where(t => !t.AllDone).ToList();

            takes.Reverse();
            HashSet<string> selected = new HashSet<string>(SelectedTakes().Select(t => t.Id));
            shown = takes;
            grid.SuspendLayout();
            grid.Rows.Clear();
            foreach (SwimTake t in takes)
            {
                string cameras = string.Join(" · ", t.Cameras.Select(c => c.Alias + (c.Done ? " ✓" : " …")));
                int row = grid.Rows.Add(t.Start.ToString("dd/MM HH:mm:ss", CultureInfo.InvariantCulture), t.Details.KindName, t.Details.Summary, cameras);
                grid.Rows[row].Tag = t;
                if (selected.Contains(t.Id))
                    grid.Rows[row].Selected = true;

                if (t.AllDone)
                    grid.Rows[row].DefaultCellStyle.ForeColor = Color.SeaGreen;
            }

            grid.ResumeLayout();
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
            using (FormGoProTakeDetails form = new FormGoProTakeDetails(take.Details, SwimGoProTakes.Root, camera))
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
            btnStop.Enabled = busy;
            if (!busy)
                progress.Value = 0;
        }

        private void Status(string text, bool warning)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = warning ? Color.FromArgb(170, 70, 30) : SystemColors.ControlText;
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

                SwimGoProFiles.PlanTargets(root, matches);
                long bytes = matches.SelectMany(m => m.Files).Sum(f => Math.Max(0, f.Size));
                int count = matches.Sum(m => m.Files.Count);
                int takeCount = matches.Select(m => m.Take.Id).Distinct().Count();
                string question = string.Format(CultureInfo.InvariantCulture,
                    takeCount == 1 ?
                        SwimLang.T("Βρέθηκαν {0} βίντεο για 1 εγγραφή ({2:0.0} GB). Αντιγραφή στον φάκελο {3};\n\nΤα αρχεία της κάρτας μένουν όπως είναι.") :
                        SwimLang.T("Βρέθηκαν {0} βίντεο για {1} εγγραφές ({2:0.0} GB). Αντιγραφή στον φάκελο {3};\n\nΤα αρχεία της κάρτας μένουν όπως είναι."),
                    count, takeCount, bytes / 1073741824.0, root);
                if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    Status("", false);
                    return;
                }

                Progress<KeyValuePair<string, long[]>> reporter = new Progress<KeyValuePair<string, long[]>>(p => ShowProgress(p.Key, p.Value[0], p.Value[1]));
                IProgress<KeyValuePair<string, long[]>> report = reporter;
                string error = null;
                int copied = await Task.Run(() =>
                {
                    string copyError;
                    int n = SwimGoProFiles.Copy(matches, (name, done, total) => report.Report(new KeyValuePair<string, long[]>(name, new[] { done, total })), token, out copyError);
                    error = copyError;
                    return n;
                });

                if (error != null)
                    Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Αντιγράφηκαν {0} βίντεο· πρόβλημα: {1}"), copied, error), true);
                else
                    Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("✓ Αντιγράφηκαν {0} βίντεο στον φάκελο."), copied), false);
            }
            catch (OperationCanceledException)
            {
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
        }
        #endregion
    }
}
