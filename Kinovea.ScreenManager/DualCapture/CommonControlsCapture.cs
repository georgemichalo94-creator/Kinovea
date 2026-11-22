#region License
/*
Copyright © Joan Charmant 2013.
jcharmant@gmail.com 
 
This file is part of Kinovea.

Kinovea is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License version 2 
as published by the Free Software Foundation.

Kinovea is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with Kinovea. If not, see http://www.gnu.org/licenses/.

*/
#endregion

using System;
using System.Drawing;
using System.Windows.Forms;
using Kinovea.ScreenManager.Languages;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    public partial class CommonControlsCapture : KinoveaControl
    {
        #region Events
        public event EventHandler SwapAsked;
        public event EventHandler<EventArgs<bool>> GrabbingChanged;
        public event EventHandler SnapshotAsked;
        public event EventHandler<EventArgs<bool>> RecordingChanged;

        /// <summary>(Swim 2.27.0) The wall of cameras on or off (its button on the right of the row).</summary>
        public event EventHandler WallAsked;

        /// <summary>(Swim 2.27.0) The menu of the capture folder of all the cameras is asked for (its button in the row).</summary>
        public event EventHandler FolderAsked;

        /// <summary>(Swim 2.27.0) The start signal is asked for (its button in the row): the cameras record, the words, the horn.</summary>
        public event EventHandler SignalAsked;

        /// <summary>(Swim 2.28.0) The report of the attempts shown or put away; the automatic stop after the last line on or off.</summary>
        public event EventHandler ReportAsked;
        public event EventHandler AutoStopAsked;
        #endregion

        #region Properties
        public bool Grabbing
        {
            get { return grabbing; }
            set { grabbing = value; RefreshGrabbingButton(); }
        }

        public bool Recording
        {
            get { return recording; }
            set { recording = value; RefreshRecordingButton(); }
        }
        #endregion

        #region Members
        private bool grabbing = true;
        private bool recording;

        // (Swim 2.27.0) The row drawn in the colors of Kinovea Swim (the buttons of the designer stay, hidden).
        private SwimCaptureBar bar;
        #endregion

        public CommonControlsCapture()
        {
            InitializeComponent();
            this.Hotkeys = HotkeySettingsManager.LoadHotkeys("DualCapture");

            // (Swim 2.27.0) The row of Kinovea Swim: record, pause, snapshot, swap, and the wall of cameras on the right.
            BackColor = SwimTheme.Surface;
            foreach (Control c in new Control[] { btnGrab, btnRecord, btnSnapshot, btnSwap, lblInfo })
                c.Visible = false;

            bar = new SwimCaptureBar();
            bar.Dock = DockStyle.Fill;
            bar.PartClicked += Bar_PartClicked;
            Controls.Add(bar);
            bar.BringToFront();
        }

        public void RefreshUICulture()
        {
            // Labels
            lblInfo.Text = ScreenManagerLang.lblInfo_Text;

            // ToolTips
            if (bar != null)
                bar.Invalidate();
        }

        public void UpdateRecordingStatus(bool status)
        {
            recording = status;
            RefreshRecordingButton();
        }

        /// <summary>
        /// (Swim 2.27.0) The button of the wall of cameras: asked for or not, and shown only when the screens can make a wall (two capture
        /// screens or more); the swap only with two screens.
        /// </summary>
        public void SetWall(bool on, bool available, bool swap)
        {
            if (bar == null)
                return;

            bar.WallOn = on;
            bar.WallAvailable = available;
            bar.SwapAvailable = swap;
            bar.Invalidate();
        }

        /// <summary>(Swim 2.27.0) Where the row reads the capture folder of all the cameras (its text, and whether a camera has none).</summary>
        public void SetFolderProvider(Func<string> text, Func<bool> missing)
        {
            if (bar == null)
                return;

            bar.FolderText = text;
            bar.FolderMissing = missing;
            bar.Invalidate();
        }

        /// <summary>(Swim 2.27.0) The menu of the capture folder, above its button.</summary>
        public void ShowFolderMenu(ContextMenuStrip menu)
        {
            if (bar == null || menu == null)
                return;

            RectangleF place = bar.PartPlace(SwimCaptureBar.PartFolder);
            menu.Show(bar, new Point((int)place.Left, (int)place.Top), ToolStripDropDownDirection.AboveRight);
        }

        /// <summary>(Swim 2.27.0) The capture folder of the cameras changed: the row shows it again.</summary>
        public void RefreshFolder()
        {
            if (bar != null)
                bar.Invalidate();
        }

        /// <summary>(Swim 2.27.0) The cameras record, as the record button does (nothing when they already do).</summary>
        public void SwimRecordAll()
        {
            if (!recording)
                ToggleRecording();
        }

        /// <summary>(Swim 2.28.0) The cameras stop recording, as the stop button does (the automatic stop after the last line).</summary>
        public void SwimStopAll()
        {
            if (recording)
                ToggleRecording();
        }

        /// <summary>(Swim 2.28.0) The button of the report: there is a report, it is shown.</summary>
        public void SetReport(bool available, bool on)
        {
            if (bar == null)
                return;

            bar.ReportAvailable = available;
            bar.ReportOn = on && available;
            bar.Invalidate();
        }

        /// <summary>(Swim 2.28.0) The automatic stop on or off.</summary>
        public void SetAutoStop(bool on)
        {
            if (bar == null)
                return;

            bar.AutoStopOn = on;
            bar.Invalidate();
        }

        private void Bar_PartClicked(int part)
        {
            switch (part)
            {
                case SwimCaptureBar.PartSignal:
                    if (SignalAsked != null)
                        SignalAsked(this, EventArgs.Empty);
                    break;
                case SwimCaptureBar.PartFolder:
                    if (FolderAsked != null)
                        FolderAsked(this, EventArgs.Empty);
                    break;
                case SwimCaptureBar.PartRecord:
                    ToggleRecording();
                    break;
                case SwimCaptureBar.PartGrab:
                    ToggleGrabbing();
                    break;
                case SwimCaptureBar.PartSnapshot:
                    TakeSnapshot();
                    break;
                case SwimCaptureBar.PartSwap:
                    if (SwapAsked != null)
                        SwapAsked(this, EventArgs.Empty);
                    break;
                case SwimCaptureBar.PartWall:
                    if (WallAsked != null)
                        WallAsked(this, EventArgs.Empty);
                    break;
                case SwimCaptureBar.PartReport:
                    if (ReportAsked != null)
                        ReportAsked(this, EventArgs.Empty);
                    break;
                case SwimCaptureBar.PartAutoStop:
                    if (AutoStopAsked != null)
                        AutoStopAsked(this, EventArgs.Empty);
                    break;
            }
        }

        #region Commands

        protected override bool ExecuteCommand(int cmd)
        {
            DualCaptureCommands command = (DualCaptureCommands)cmd;

            switch (command)
            {
                case DualCaptureCommands.ToggleGrabbing:
                    ToggleGrabbing();
                    break;
                case DualCaptureCommands.ToggleRecording:
                    ToggleRecording();
                    break;
                case DualCaptureCommands.TakeSnapshot:
                    TakeSnapshot();
                    break;
                default:
                    return base.ExecuteCommand(cmd);
            }

            return true;
        }

        public void ExecuteDualCommand(int cmd)
        {
            ExecuteCommand(cmd);
        }

        #endregion

        private void ToggleGrabbing()
        {
            grabbing = !grabbing;
            RefreshGrabbingButton();

            if (GrabbingChanged != null)
                GrabbingChanged(this, new EventArgs<bool>(grabbing));
        }

        private void ToggleRecording()
        {
            recording = !recording;
            RefreshRecordingButton();

            if (RecordingChanged != null)
                RecordingChanged(this, new EventArgs<bool>(recording));
        }

        private void TakeSnapshot()
        {
            if (SnapshotAsked != null)
                SnapshotAsked(this, EventArgs.Empty);
        }

        #region UI Handlers
        private void btnGrab_Click(object sender, EventArgs e)
        {
            ToggleGrabbing();
        }

        private void btnSnapshot_Click(object sender, EventArgs e)
        {
            TakeSnapshot();
        }

        private void btnRecord_Click(object sender, EventArgs e)
        {
            ToggleRecording();
        }
        
        private void btnSwap_Click(object sender, EventArgs e)
        {
            if (SwapAsked != null)
                SwapAsked(this, EventArgs.Empty);
        }
        #endregion

        #region Lower level helpers
        private void RefreshGrabbingButton()
        {
            btnGrab.Image = grabbing ? Properties.Capture.pause_16 : Properties.Capture.circled_play_16;
            btnRecord.Enabled = grabbing;
            if (bar != null)
            {
                bar.Grabbing = grabbing;
                bar.Invalidate();
            }
        }

        private void RefreshRecordingButton()
        {
            btnRecord.Image = recording ? Properties.Capture.record_stop : Properties.Capture.circle_16;
            if (bar != null)
                bar.SetRecording(recording);
        }
        #endregion
    }
}
