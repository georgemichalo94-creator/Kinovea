#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.10.0) «📦 Δημιουργία αποστολής»: while the package is made, the progress of the video with the graphics and of the PDF; then the
    /// preview of both (the pages of the PDF, pictures of the video and its player), the recipients, the subject and the message that
    /// can be changed, and OK: the sending with Smash (FormSwimSmash).
    /// </summary>
    public class FormSwimPackage : Form
    {
        private readonly SwimPackage package;
        private readonly Func<BackgroundWorker, string> render;
        private readonly Action beforeRender;
        private readonly Action afterRender;
        private readonly Action retryPdf;
        private readonly Func<bool, string, string> bodyFor;
        private readonly BackgroundWorker worker;
        private readonly ToolTip tips = new ToolTip();

        private readonly Label pdfStatus;
        private readonly FlowLayoutPanel pdfPages;
        private readonly Panel pdfScroll;
        private readonly SwimButton btnOpenPdf;
        private readonly SwimButton btnRetryPdf;
        private readonly Label videoStatus;
        private readonly ProgressBar videoProgress;
        private readonly PictureBox videoPicture;
        private readonly FlowLayoutPanel thumbs;
        private readonly SwimButton btnPlay;
        private readonly TextBox txtTo;
        private readonly TextBox txtSubject;
        private readonly TextBox txtBody;
        private readonly TextBox txtSignature;
        private readonly Label lblInfo;
        private readonly SwimButton btnOk;
        private readonly SwimButton btnClose;
        private readonly SwimButton btnFolder;

        private bool videoRunning;
        private bool videoDone;
        private string videoError;
        private bool cancelled;
        private bool filling;
        private bool toEdited;
        private bool bodyEdited;
        private int pdfRenderSeq;
        private string pdfShown;
        private List<Bitmap> pageImages = new List<Bitmap>();

        /// <summary>The signature as the analyst left it (saved in the settings for the next packages).</summary>
        public string Signature
        {
            get { return txtSignature.Text.Trim(); }
        }

        public bool VideoCancelled
        {
            get { return cancelled; }
        }

        /// <param name="render">Makes the video (on a thread of its own), reports its progress (the pictures as UserState): the error or null.</param>
        /// <param name="bodyFor">The text of the message for (the report is in the KOE App, the signature).</param>
        public FormSwimPackage(SwimPackage package, string title, string signature, Func<BackgroundWorker, string> render, Action beforeRender,
            Action afterRender, Action retryPdf, Func<bool, string, string> bodyFor)
        {
            this.package = package;
            this.render = render;
            this.beforeRender = beforeRender;
            this.afterRender = afterRender;
            this.retryPdf = retryPdf;
            this.bodyFor = bodyFor;

            Text = SwimLang.T("📦 Αποστολή σε προπονητές") + (string.IsNullOrEmpty(title) ? "" : " – " + title);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1240, 860);
            MinimumSize = new Size(940, 680);
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = SwimTheme.Back;
            ForeColor = SwimTheme.Text;
            Font = SwimTheme.Ui(9f, FontStyle.Regular);
            KeyPreview = true;

            // The PDF (left) and the video (right).
            SplitContainer split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                BackColor = SwimTheme.Back,
                SplitterWidth = 8
            };
            split.Panel1.BackColor = SwimTheme.Surface;
            split.Panel2.BackColor = SwimTheme.Surface;
            split.Panel1.Padding = new Padding(10);
            split.Panel2.Padding = new Padding(10);

            Label pdfTitle = Title(SwimLang.T("📄 Αναφορά (PDF)"));
            pdfStatus = new Label { Dock = DockStyle.Top, Height = 40, ForeColor = SwimTheme.Dim, AutoEllipsis = true, Padding = new Padding(0, 2, 0, 0) };
            pdfScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = SwimTheme.Input };
            pdfPages = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(0, 0), BackColor = SwimTheme.Input };
            pdfScroll.Controls.Add(pdfPages);
            pdfScroll.Resize += (s, e) => LayoutPages();
            FlowLayoutPanel pdfButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 6, 0, 0) };
            btnOpenPdf = new SwimButton(true) { Text = SwimLang.T("Άνοιγμα PDF"), AutoSize = true, Height = 30, Enabled = false };
            btnOpenPdf.Click += (s, e) => OpenPdf();
            btnRetryPdf = new SwimButton(true) { Text = SwimLang.T("Ξαναδοκίμασε το PDF"), AutoSize = true, Height = 30, Visible = false };
            btnRetryPdf.Click += (s, e) =>
            {
                btnRetryPdf.Visible = false;
                if (retryPdf != null)
                    retryPdf();
                PdfChanged();
            };
            pdfButtons.Controls.Add(btnOpenPdf);
            pdfButtons.Controls.Add(btnRetryPdf);
            split.Panel1.Controls.Add(pdfScroll);
            split.Panel1.Controls.Add(pdfButtons);
            split.Panel1.Controls.Add(pdfStatus);
            split.Panel1.Controls.Add(pdfTitle);

            Label videoTitle = Title(SwimLang.T("🎬 Βίντεο με τα γραφικά"));
            videoStatus = new Label { Dock = DockStyle.Top, Height = 24, ForeColor = SwimTheme.Dim, AutoEllipsis = true };
            videoProgress = new ProgressBar { Dock = DockStyle.Top, Height = 10, Maximum = 100 };
            videoPicture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Cursor = Cursors.Hand };
            videoPicture.DoubleClick += (s, e) => PlayVideo();
            thumbs = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 104, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 6, 0, 0) };
            FlowLayoutPanel videoButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 6, 0, 0) };
            btnPlay = new SwimButton(true) { Text = SwimLang.T("▶ Αναπαραγωγή"), AutoSize = true, Height = 30, Enabled = false, Primary = true };
            btnPlay.Click += (s, e) => PlayVideo();
            videoButtons.Controls.Add(btnPlay);
            split.Panel2.Controls.Add(videoPicture);
            split.Panel2.Controls.Add(thumbs);
            split.Panel2.Controls.Add(videoButtons);
            split.Panel2.Controls.Add(videoProgress);
            split.Panel2.Controls.Add(videoStatus);
            split.Panel2.Controls.Add(videoTitle);

            // The message.
            TableLayoutPanel message = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 268, ColumnCount = 2, RowCount = 5, Padding = new Padding(10, 8, 10, 0), BackColor = SwimTheme.Back };
            message.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            message.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            message.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            message.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            message.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            message.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            message.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            txtTo = Field(false);
            txtSubject = Field(false);
            txtBody = Field(true);
            txtSignature = Field(false);
            message.Controls.Add(FieldLabel(SwimLang.T("Προς:")), 0, 0);
            message.Controls.Add(txtTo, 1, 0);
            message.Controls.Add(FieldLabel(SwimLang.T("Θέμα:")), 0, 1);
            message.Controls.Add(txtSubject, 1, 1);
            message.Controls.Add(FieldLabel(SwimLang.T("Μήνυμα:")), 0, 2);
            message.Controls.Add(txtBody, 1, 2);
            message.Controls.Add(FieldLabel(SwimLang.T("Υπογραφή:")), 0, 3);
            message.Controls.Add(txtSignature, 1, 3);
            lblInfo = new Label { Dock = DockStyle.Fill, ForeColor = SwimTheme.Dim, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            message.Controls.Add(lblInfo, 0, 4);
            message.SetColumnSpan(lblInfo, 2);

            Panel buttons = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(10, 8, 10, 8), BackColor = SwimTheme.Back };
            btnOk = new SwimButton(true) { Text = SwimLang.T("✓ OK – Αποστολή με Smash"), AutoSize = true, Height = 34, Primary = true, Enabled = false, Dock = DockStyle.Right };
            btnClose = new SwimButton(true) { Text = SwimLang.T("Ακύρωση"), AutoSize = true, Height = 34, Dock = DockStyle.Right };
            btnFolder = new SwimButton(true) { Text = SwimLang.T("Άνοιγμα φακέλου"), AutoSize = true, Height = 34, Dock = DockStyle.Left };
            Panel gap = new Panel { Width = 8, Dock = DockStyle.Right };
            buttons.Controls.Add(btnClose);
            buttons.Controls.Add(gap);
            buttons.Controls.Add(btnOk);
            buttons.Controls.Add(btnFolder);
            btnOk.Click += (s, e) => Ok();
            btnClose.Click += (s, e) => CloseOrCancel();
            btnFolder.Click += (s, e) => OpenFolder();

            Controls.Add(split);
            Controls.Add(message);
            Controls.Add(buttons);
            AcceptButton = null;

            filling = true;
            txtTo.Text = string.Join(", ", package.Recipients.ToArray());
            txtSubject.Text = package.Subject;
            txtSignature.Text = signature ?? "";
            txtBody.Text = (package.Body ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            filling = false;
            txtTo.TextChanged += (s, e) => { if (!filling) toEdited = true; };
            txtBody.TextChanged += (s, e) => { if (!filling) bodyEdited = true; };
            txtSignature.TextChanged += (s, e) =>
            {
                if (!filling && !bodyEdited)
                    SetBody(bodyFor != null ? bodyFor(package.Archived == true, txtSignature.Text) : txtBody.Text);
            };

            worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            worker.DoWork += (s, e) => e.Result = render != null ? render((BackgroundWorker)s) : SwimLang.T("Χωρίς βίντεο.");
            worker.ProgressChanged += Worker_ProgressChanged;
            worker.RunWorkerCompleted += Worker_RunWorkerCompleted;

            Shown += (s, e) =>
            {
                split.SplitterDistance = (int)(split.Width * 0.44);
                StartVideo();
                PdfChanged();
            };
            FormClosing += (s, e) =>
            {
                // The video is being made: it stops first (the reader of the video is given back), then the window closes.
                if (videoRunning)
                {
                    e.Cancel = true;
                    cancelled = true;
                    worker.CancelAsync();
                    btnClose.Enabled = false;
                    videoStatus.Text = SwimLang.T("Σταματά…");
                }
            };
            UpdateState();
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                base.Dispose(false);
                return;
            }

            // The images after the controls that show them.
            worker.Dispose();
            tips.Dispose();
            videoPicture.Image = null;
            List<Image> images = pageImages.Cast<Image>().Concat(package.Thumbs.Where(t => t.Image != null).Select(t => (Image)t.Image)).ToList();
            base.Dispose(true);
            foreach (Image image in images)
                image.Dispose();
            pageImages.Clear();
            foreach (SwimPackageThumb t in package.Thumbs)
                t.Image = null;
        }

        private static Label Title(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Top, Height = 28, Font = SwimTheme.Ui(11f, FontStyle.Bold), ForeColor = SwimTheme.AccentLight };
        }

        private static Label FieldLabel(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = SwimTheme.Dim };
        }

        private static TextBox Field(bool multiline)
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = multiline,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
                AcceptsReturn = multiline,
                BackColor = SwimTheme.Input,
                ForeColor = SwimTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 3, 0, 3)
            };
        }

        private void SetBody(string text)
        {
            filling = true;
            txtBody.Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            filling = false;
        }

        #region Video
        private void StartVideo()
        {
            if (render == null)
                return;
            videoRunning = true;
            videoStatus.Text = SwimLang.T("Το βίντεο γίνεται με τα γραφικά…");
            if (beforeRender != null)
                beforeRender();
            worker.RunWorkerAsync();
            UpdateState();
        }

        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            videoProgress.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
            SwimPackageThumb thumb = e.UserState as SwimPackageThumb;
            if (thumb != null)
                AddThumb(thumb);
            else if (e.UserState is string)
                videoStatus.Text = (string)e.UserState;
        }

        private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            videoRunning = false;
            if (afterRender != null)
                afterRender();
            videoError = e.Error != null ? e.Error.Message : e.Result as string;
            videoDone = !cancelled && string.IsNullOrEmpty(videoError) && package.VideoReady;
            if (cancelled)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            videoProgress.Value = videoDone ? 100 : 0;
            videoProgress.Visible = false;
            UpdateState();
        }

        private void AddThumb(SwimPackageThumb thumb)
        {
            if (thumb.Image == null)
                return;
            PictureBox box = new PictureBox
            {
                Image = thumb.Image,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(144, 81),
                Margin = new Padding(0, 0, 6, 0),
                BackColor = Color.Black,
                Cursor = Cursors.Hand
            };
            tips.SetToolTip(box, thumb.Label);
            Label label = new Label { Text = thumb.Label, AutoSize = false, Size = new Size(144, 16), ForeColor = SwimTheme.Dim, Font = SwimTheme.Ui(7.5f, FontStyle.Regular), TextAlign = ContentAlignment.MiddleCenter };
            Panel cell = new Panel { Size = new Size(150, 98), Margin = new Padding(0) };
            box.Location = new Point(0, 0);
            label.Location = new Point(0, 82);
            cell.Controls.Add(box);
            cell.Controls.Add(label);
            box.Click += (s, e) => videoPicture.Image = thumb.Image;
            box.DoubleClick += (s, e) => PlayVideo();
            thumbs.Controls.Add(cell);
            videoPicture.Image = thumb.Image;
        }

        private void PlayVideo()
        {
            if (!videoDone || !File.Exists(package.VideoPath))
                return;
            Open(package.VideoPath);
        }
        #endregion

        #region PDF
        /// <summary>The PDF arrived, failed or is asked again (called by the analysis, on the window's thread): its state and pages.</summary>
        public void PdfChanged()
        {
            if (IsDisposed)
                return;

            if (package.PdfReady && File.Exists(package.PdfPath))
            {
                pdfStatus.ForeColor = SwimTheme.Green;
                pdfStatus.Text = "✓ " + package.PdfFileName + " · " + SwimPackage.SizeText(new FileInfo(package.PdfPath).Length) +
                    (string.IsNullOrEmpty(package.ArchiveNote) ? "" : "\n" + package.ArchiveNote);
                btnOpenPdf.Enabled = true;
                btnRetryPdf.Visible = false;
                if (pdfShown != package.PdfPath + "|" + File.GetLastWriteTimeUtc(package.PdfPath).Ticks.ToString(CultureInfo.InvariantCulture))
                    RenderPages();
            }
            else if (package.PdfWaiting && string.IsNullOrEmpty(package.PdfError))
            {
                pdfStatus.ForeColor = SwimTheme.Dim;
                pdfStatus.Text = SwimLang.T("Το PDF γίνεται στο Google Sheet…");
                btnOpenPdf.Enabled = false;
            }
            else
            {
                pdfStatus.ForeColor = SwimTheme.Warn;
                string text = !string.IsNullOrEmpty(package.PdfError) ? "⚠ " + package.PdfError : "⚠ " + (package.PdfNote ?? SwimLang.T("Χωρίς PDF."));
                pdfStatus.Text = text;
                btnOpenPdf.Enabled = !string.IsNullOrEmpty(package.PdfUrl);
                btnRetryPdf.Visible = retryPdf != null && !string.IsNullOrEmpty(package.PdfError);
            }

            // The recipients and the line of the KOE App in the message (unless the analyst changed them).
            if (!toEdited)
            {
                filling = true;
                txtTo.Text = string.Join(", ", package.Recipients.ToArray());
                filling = false;
            }

            if (!bodyEdited && bodyFor != null)
                SetBody(bodyFor(package.Archived == true, txtSignature.Text));
            UpdateState();
        }

        private void RenderPages()
        {
            string path = package.PdfPath;
            pdfShown = path + "|" + File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture);
            int seq = ++pdfRenderSeq;
            int width = Math.Max(600, Math.Min(1400, pdfScroll.ClientSize.Width * 2));
            // Drawn on a thread of the pool, shown on the thread of the window (BeginInvoke: whatever the synchronization context).
            Task.Factory.StartNew(() =>
            {
                string error;
                List<Bitmap> pages = SwimPdfPages.Render(path, width, 16, out error);
                Action show = () =>
                {
                    if (IsDisposed || seq != pdfRenderSeq)
                    {
                        if (pages != null)
                            pages.ForEach(b => b.Dispose());
                        return;
                    }

                    ShowPages(pages, error);
                };

                try
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(show);
                    else if (pages != null)
                        pages.ForEach(b => b.Dispose());
                }
                catch (InvalidOperationException)
                {
                    // The window closed meanwhile.
                    if (pages != null)
                        pages.ForEach(b => b.Dispose());
                }
            });
        }

        private void ShowPages(List<Bitmap> pages, string error)
        {
            pdfPages.SuspendLayout();
            foreach (Control c in pdfPages.Controls.Cast<Control>().ToList())
            {
                pdfPages.Controls.Remove(c);
                c.Dispose();
            }

            foreach (Bitmap b in pageImages)
                b.Dispose();
            pageImages = pages ?? new List<Bitmap>();
            if (pages == null || pages.Count == 0)
            {
                Label none = new Label
                {
                    Text = SwimLang.T("Η προεπισκόπηση των σελίδων δεν έγινε σε αυτόν τον υπολογιστή: «Άνοιγμα PDF».") +
                        (string.IsNullOrEmpty(error) ? "" : "\n(" + error + ")"),
                    AutoSize = false,
                    Size = new Size(Math.Max(200, pdfScroll.ClientSize.Width - 24), 80),
                    ForeColor = SwimTheme.Dim,
                    Margin = new Padding(12)
                };
                pdfPages.Controls.Add(none);
            }
            else
            {
                foreach (Bitmap page in pages)
                {
                    PictureBox box = new PictureBox { Image = page, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(12, 12, 12, 0), BackColor = Color.White };
                    box.DoubleClick += (s, e) => OpenPdf();
                    pdfPages.Controls.Add(box);
                }
            }

            pdfPages.ResumeLayout();
            LayoutPages();
        }

        private void LayoutPages()
        {
            int width = Math.Max(120, pdfScroll.ClientSize.Width - 24 - SystemInformation.VerticalScrollBarWidth);
            foreach (Control c in pdfPages.Controls)
            {
                PictureBox box = c as PictureBox;
                if (box == null || box.Image == null)
                    continue;
                box.Size = new Size(width, (int)Math.Round(width * box.Image.Height / (double)Math.Max(1, box.Image.Width)));
            }
        }

        private void OpenPdf()
        {
            if (package.PdfReady && File.Exists(package.PdfPath))
                Open(package.PdfPath);
            else if (!string.IsNullOrEmpty(package.PdfUrl))
                Open(package.PdfUrl);
        }
        #endregion

        private void UpdateState()
        {
            if (IsDisposed)
                return;

            bool pdfWaiting = package.PdfWaiting && !package.PdfReady && string.IsNullOrEmpty(package.PdfError);
            if (videoRunning)
            {
                btnClose.Text = SwimLang.T("Ακύρωση");
            }
            else if (videoDone)
            {
                string size = File.Exists(package.VideoPath) ? SwimPackage.SizeText(new FileInfo(package.VideoPath).Length) : "";
                TimeSpan d = TimeSpan.FromSeconds(Math.Max(0, package.VideoSeconds));
                videoStatus.ForeColor = SwimTheme.Green;
                videoStatus.Text = "✓ " + package.VideoFileName + " · " + string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", (int)d.TotalMinutes, d.Seconds) +
                    " · " + package.VideoSize.Width + "x" + package.VideoSize.Height + " · " + size +
                    (package.VideoHasSound ? SwimLang.T(" · με ήχο") : SwimLang.T(" · χωρίς ήχο"));
                btnPlay.Enabled = true;
                btnClose.Text = SwimLang.T("Κλείσιμο");
            }
            else
            {
                videoStatus.ForeColor = SwimTheme.Warn;
                videoStatus.Text = "⚠ " + (string.IsNullOrEmpty(videoError) ? SwimLang.T("Το βίντεο δεν έγινε.") : videoError);
                btnClose.Text = SwimLang.T("Κλείσιμο");
            }

            btnOk.Enabled = videoDone && !pdfWaiting;
            btnOk.Text = videoDone && pdfWaiting ? SwimLang.T("Περιμένω το PDF…") : SwimLang.T("✓ OK – Αποστολή με Smash");
            string[] files = package.Files;
            long bytes = files.Sum(f => new FileInfo(f).Length);
            lblInfo.Text = SwimLang.T("Φάκελος: ") + package.Folder + (files.Length > 0 ? "   ·   " + files.Length + SwimLang.T(" αρχεία, ") + SwimPackage.SizeText(bytes) : "") +
                (bytes > 25L * 1024 * 1024 ? SwimLang.T("   ·   μεγάλο για email: με Smash") : "");
        }

        private void Ok()
        {
            package.Recipients = SwimPackage.ParseRecipients(txtTo.Text);
            package.Subject = txtSubject.Text.Trim();
            package.Body = txtBody.Text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            try
            {
                package.WriteMessage();
            }
            catch (Exception e)
            {
                MessageBox.Show(this, SwimLang.T("Το μήνυμα δεν γράφτηκε στον φάκελο: ") + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void CloseOrCancel()
        {
            if (videoRunning)
            {
                cancelled = true;
                worker.CancelAsync();
                btnClose.Enabled = false;
                videoStatus.Text = SwimLang.T("Σταματά…");
                return;
            }

            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void OpenFolder()
        {
            string[] files = package.Files;
            if (files.Length > 0)
                Open("explorer.exe", "/select,\"" + files[0] + "\"");
            else if (Directory.Exists(package.Folder))
                Open(package.Folder);
        }

        internal static void Open(string target, string arguments = null)
        {
            try
            {
                if (arguments == null)
                    Process.Start(target);
                else
                    Process.Start(target, arguments);
            }
            catch (Exception)
            {
                // Nothing to open it with: the folder of the package is shown in the window.
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                CloseOrCancel();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    /// <summary>
    /// (2.10.0) After the OK of the package: a small window over the others while the analyst sends with Smash (opened in the browser):
    /// the files dragged from here onto the page, the recipients, the subject and the message copied one by one, and «Στάλθηκε».
    /// </summary>
    public class FormSwimSmash : Form
    {
        private readonly SwimPackage package;
        private readonly Action sent;

        public FormSwimSmash(SwimPackage package, Action sent)
        {
            this.package = package;
            this.sent = sent;

            Text = SwimLang.T("📤 Αποστολή με Smash");
            StartPosition = FormStartPosition.Manual;
            Size = new Size(470, 640);
            MinimumSize = new Size(420, 560);
            TopMost = true;
            ShowInTaskbar = true;
            MaximizeBox = false;
            MinimizeBox = true;
            BackColor = SwimTheme.Back;
            ForeColor = SwimTheme.Text;
            Font = SwimTheme.Ui(9f, FontStyle.Regular);
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Right - Width - 16, area.Top + 40);

            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12), BackColor = SwimTheme.Back };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            string[] files = package.Files;
            long bytes = files.Sum(f => new FileInfo(f).Length);
            t.Controls.Add(Step(SwimLang.T("1. Σύρε τα αρχεία με το ποντίκι από εδώ στη σελίδα του Smash")));
            Label drag = new Label
            {
                Text = "📎  " + string.Join("\n      ", files.Select(Path.GetFileName).ToArray()) + "\n      " + files.Length + SwimLang.T(" αρχεία, ") + SwimPackage.SizeText(bytes),
                AutoSize = false,
                Height = 78,
                Dock = DockStyle.Top,
                BackColor = SwimTheme.AccentDark,
                ForeColor = SwimTheme.Text,
                Padding = new Padding(8),
                Cursor = Cursors.SizeAll,
                Margin = new Padding(0, 2, 0, 4)
            };
            drag.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || files.Length == 0)
                    return;
                DataObject data = new DataObject(DataFormats.FileDrop, files);
                drag.DoDragDrop(data, DragDropEffects.Copy);
            };
            t.Controls.Add(drag);
            FlowLayoutPanel fileButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 8) };
            SwimButton open = new SwimButton(true) { Text = SwimLang.T("Άνοιγμα Smash"), AutoSize = true, Height = 30 };
            open.Click += (s, e) => FormSwimPackage.Open("https://fromsmash.com/");
            SwimButton folder = new SwimButton(true) { Text = SwimLang.T("Άνοιγμα φακέλου"), AutoSize = true, Height = 30 };
            folder.Click += (s, e) =>
            {
                if (files.Length > 0)
                    FormSwimPackage.Open("explorer.exe", "/select,\"" + files[0] + "\"");
            };
            fileButtons.Controls.Add(open);
            fileButtons.Controls.Add(folder);
            t.Controls.Add(fileButtons);

            AddCopy(t, SwimLang.T("2. Παραλήπτες (email)"), string.Join(", ", package.Recipients.ToArray()), false);
            AddCopy(t, SwimLang.T("3. Τίτλος (θέμα)"), package.Subject, false);
            AddCopy(t, SwimLang.T("4. Μήνυμα"), package.Body, true);

            FlowLayoutPanel end = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 10, 0, 0) };
            SwimButton done = new SwimButton(true) { Text = SwimLang.T("✓ Στάλθηκε"), AutoSize = true, Height = 32, Primary = true };
            done.Click += (s, e) =>
            {
                if (this.sent != null)
                    this.sent();
                Close();
            };
            SwimButton close = new SwimButton(true) { Text = SwimLang.T("Κλείσιμο"), AutoSize = true, Height = 32 };
            close.Click += (s, e) => Close();
            end.Controls.Add(done);
            end.Controls.Add(close);
            t.Controls.Add(end);
            Controls.Add(t);
        }

        private static Label Step(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = SwimTheme.AccentLight, Font = SwimTheme.Ui(9f, FontStyle.Bold), Margin = new Padding(0, 6, 0, 2) };
        }

        private static void AddCopy(TableLayoutPanel t, string title, string value, bool multiline)
        {
            t.Controls.Add(Step(title));
            TableLayoutPanel row = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, Height = multiline ? 150 : 32, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            TextBox box = new TextBox
            {
                Text = (value ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
                ReadOnly = true,
                Multiline = multiline,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
                Dock = DockStyle.Fill,
                BackColor = SwimTheme.Input,
                ForeColor = SwimTheme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            SwimButton copy = new SwimButton(true) { Text = SwimLang.T("Αντιγραφή"), AutoSize = true, Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            copy.Click += (s, e) =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(box.Text))
                        Clipboard.SetText(box.Text);
                    copy.Text = SwimLang.T("✓ Αντιγράφηκε");
                }
                catch (Exception)
                {
                    copy.Text = SwimLang.T("Αντιγραφή");
                }
            };
            row.Controls.Add(box, 0, 0);
            row.Controls.Add(copy, 1, 0);
            t.Controls.Add(row);
        }
    }
}
