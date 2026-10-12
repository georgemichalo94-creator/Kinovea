#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Bars of the analysis on the sides of the video, in the black area next to the image (SwimVideoBar): the buttons
    /// of the marks on the left, the race time, the checkpoints and the window of the analysis on the right.
    /// With the bars the window of the analysis is opened only when needed: the marks, the graphics on the video and
    /// the clicks on the image work without it.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Bars on the video
        /// <summary>The analysis is in use on the video (window or bars), until it is closed.</summary>
        private bool analysisOn;
        private SwimVideoBar leftBar;
        private SwimVideoBar rightBar;
        private PlayerScreenUserInterface barsPlayer;
        private Timer barsTimer;

        /// <summary>Changes at each computation of the analysis (the bars redraw when it changes).</summary>
        public int DataVersion { get; private set; }

        /// <summary>The window of the analysis is open.</summary>
        public bool PanelOpen
        {
            get { return form != null && !form.IsDisposed; }
        }

        /// <summary>The bars are on the video of the analysis.</summary>
        public bool BarsShown
        {
            get
            {
                return leftBar != null && !leftBar.IsDisposed && rightBar != null && !rightBar.IsDisposed &&
                    barsPlayer != null && !barsPlayer.IsDisposed && barsPlayer == player;
            }
        }

        /// <summary>The analysis is on the screen, in its window or in its bars: graphics on the video, clicks on the image.</summary>
        public bool Active
        {
            get { return PanelOpen || BarsShown; }
        }

        /// <summary>
        /// Called by the player screen when it places the image: places the bars at the edges of the viewport and returns
        /// the width of each (0 without bars on this screen). The image is placed between them.
        /// </summary>
        public int LayoutVideoBars(PlayerScreenUserInterface screen, Size viewport)
        {
            if (screen == null || screen != barsPlayer || leftBar == null || leftBar.IsDisposed || rightBar == null || rightBar.IsDisposed)
                return 0;

            float scale = Math.Max(1f, leftBar.DeviceDpi / 96f);
            int width = (int)Math.Round(Math.Max(176 * scale, Math.Min(248 * scale, viewport.Width * 0.14)));
            bool room = viewport.Width - 2 * width >= 320 * scale && viewport.Height >= 300 * scale;
            if (leftBar.Visible != room)
                leftBar.Visible = room;
            if (rightBar.Visible != room)
                rightBar.Visible = room;
            if (!room)
                return 0;

            Rectangle leftBounds = new Rectangle(0, 0, width, viewport.Height);
            Rectangle rightBounds = new Rectangle(viewport.Width - width, 0, width, viewport.Height);
            if (leftBar.Bounds != leftBounds)
                leftBar.Bounds = leftBounds;
            if (rightBar.Bounds != rightBounds)
                rightBar.Bounds = rightBounds;

            return width;
        }

        /// <summary>
        /// The analysis is used on the video: the bars (with the setting), and the window when asked or without the bars.
        /// </summary>
        private void ShowAnalysis(bool panel)
        {
            analysisOn = true;
            if (panel || !Settings.VideoBars)
                EnsureForm(panel);

            UpdateBars();
        }

        /// <summary>Opens or closes the window of the analysis (button of the bars, Ctrl+Shift+W with the bars).</summary>
        public void TogglePanel()
        {
            if (PanelOpen)
            {
                form.Close();
                return;
            }

            ShowAnalysis(true);
        }

        /// <summary>
        /// Closes the bars and the window (✕ of the bars). The analysis is saved; a key of the analysis brings the bars back.
        /// </summary>
        public void CloseAnalysis()
        {
            analysisOn = false;
            if (PickTarget != SwimPickTarget.None)
                CancelPick();
            if (PanelOpen)
                form.Close();

            UpdateBars();
            InvalidatePlayer();
        }

        /// <summary>Bars on the video on or off (setting of the window).</summary>
        public void SetVideoBars(bool on)
        {
            Settings.VideoBars = on;
            Settings.Save();
            UpdateBars();
            InvalidatePlayer();
        }

        /// <summary>Puts the bars on the video of the analysis, moves them to another screen, or removes them.</summary>
        private void UpdateBars()
        {
            bool want = analysisOn && Settings.VideoBars && HasVideo && Race != null;
            PlayerScreenUserInterface target = want ? player : null;
            bool present = leftBar != null && !leftBar.IsDisposed && rightBar != null && !rightBar.IsDisposed &&
                barsPlayer != null && !barsPlayer.IsDisposed;

            if (present && barsPlayer == target)
            {
                leftBar.RefreshNow();
                rightBar.RefreshNow();
                return;
            }

            RemoveBars();
            if (target == null)
                return;

            leftBar = new SwimVideoBar(this, true);
            rightBar = new SwimVideoBar(this, false);
            barsPlayer = target;
            target.panelCenter.Controls.Add(leftBar);
            target.panelCenter.Controls.Add(rightBar);
            leftBar.BringToFront();
            rightBar.BringToFront();
            target.SwimLayoutChanged();

            if (barsTimer == null)
            {
                barsTimer = new Timer();
                barsTimer.Interval = 150;
                barsTimer.Tick += BarsTimer_Tick;
            }

            barsTimer.Start();
        }

        private void RemoveBars()
        {
            PlayerScreenUserInterface old = barsPlayer;
            foreach (SwimVideoBar bar in new SwimVideoBar[] { leftBar, rightBar })
            {
                if (bar == null || bar.IsDisposed)
                    continue;

                if (bar.Parent != null)
                    bar.Parent.Controls.Remove(bar);
                bar.Dispose();
            }

            leftBar = null;
            rightBar = null;
            barsPlayer = null;
            if (barsTimer != null)
                barsTimer.Stop();

            // The image takes the whole width again.
            if (old != null && !old.IsDisposed)
                old.SwimLayoutChanged();
        }

        private void BarsTimer_Tick(object sender, EventArgs e)
        {
            if (leftBar == null || leftBar.IsDisposed || rightBar == null || rightBar.IsDisposed || barsPlayer == null || barsPlayer.IsDisposed)
            {
                // The screen was closed with its bars.
                RemoveBars();
                return;
            }

            if (barsPlayer != player || !HasVideo)
            {
                UpdateBars();
                return;
            }

            leftBar.RefreshIfChanged();
            rightBar.RefreshIfChanged();
        }

        /// <summary>Owner of the dialogs of the analysis: its window, or the main window when only the bars are shown.</summary>
        private IWin32Window DialogOwner
        {
            get
            {
                if (PanelOpen)
                    return form;

                return player != null && !player.IsDisposed ? player.FindForm() : null;
            }
        }
        #endregion
    }
}
