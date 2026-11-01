#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.16.0) A small window with the progress of a long work done on a worker thread (the video of a group of screens): the line the
    /// work reports, its bar and a button to stop it. It closes by itself when the work ends.
    /// </summary>
    internal sealed class SwimProgressDialog : Form
    {
        private readonly BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
        private readonly Label line;
        private readonly ProgressBar bar;
        private readonly Button stop;

        /// <summary>The work was stopped with the button.</summary>
        public bool Cancelled { get; private set; }

        /// <summary>The work ended with an exception (null otherwise).</summary>
        public Exception Failure { get; private set; }

        public SwimProgressDialog(string title, Action<BackgroundWorker> work)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 118);
            BackColor = SwimTheme.Back;
            ForeColor = SwimTheme.Text;
            Font = new Font("Segoe UI", 9f);

            line = new Label { Text = SwimLang.T("Ξεκινά…"), AutoSize = false, Bounds = new Rectangle(14, 12, 432, 36), ForeColor = SwimTheme.Text };
            bar = new ProgressBar { Bounds = new Rectangle(14, 52, 432, 18), Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous };
            stop = new SwimNoFocusButton { Text = SwimLang.T("Διακοπή"), Bounds = new Rectangle(346, 80, 100, 28) };
            stop.Click += (s, e) =>
            {
                stop.Enabled = false;
                line.Text = SwimLang.T("Σταματά…");
                worker.CancelAsync();
            };
            Controls.Add(line);
            Controls.Add(bar);
            Controls.Add(stop);

            worker.DoWork += (s, e) => work(worker);
            worker.ProgressChanged += (s, e) =>
            {
                bar.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
                string text = e.UserState as string;
                if (!string.IsNullOrEmpty(text))
                    line.Text = text;
            };
            worker.RunWorkerCompleted += (s, e) =>
            {
                Cancelled = worker.CancellationPending;
                Failure = e.Error;
                Close();
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SwimTheme.DarkTitleBar(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            worker.RunWorkerAsync();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Not closed while the work runs: it is asked to stop and the window closes when it has.
            if (worker.IsBusy)
            {
                e.Cancel = true;
                stop.Enabled = false;
                worker.CancelAsync();
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                worker.Dispose();
            base.Dispose(disposing);
        }
    }
}
