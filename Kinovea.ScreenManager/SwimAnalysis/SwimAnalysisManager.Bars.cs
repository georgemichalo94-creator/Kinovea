#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Drawing;
using System.Windows.Forms;
using Kinovea.Services;

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
        private FrameServerPlayer barsFrameServer;
        private Timer barsTimer;

        /// <summary>There is room for the bars next to the image (else they are hidden), and their width.</summary>
        private bool barsRoom;
        private int barsWidth;

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

        /// <summary>The bars are on the video and there is room for them (a very narrow screen hides them).</summary>
        public bool BarsVisible
        {
            get { return BarsShown && barsRoom; }
        }

        /// <summary>
        /// The analysis is on the screen, in its window, in its bars or in its round button of the full screen: graphics on the
        /// video, clicks on the image.
        /// </summary>
        public bool Active
        {
            get { return PanelOpen || BarsShown || DialShown; }
        }

        /// <summary>
        /// Called by the player screen when it places the image: places the bars at the edges of the viewport and returns
        /// the width of each (0 without bars on this screen). The image is placed between them.
        /// </summary>
        public int LayoutVideoBars(PlayerScreenUserInterface screen, Size viewport)
        {
            // Full screen (2.9.5): no room beside the image, the round button over it instead (SwimAnalysisManager.Dial.cs).
            if (VideoFullScreen.Active)
                return LayoutFullScreen(screen, viewport);

            SwimVideoBar left = leftBar;
            SwimVideoBar right = rightBar;
            if (screen == null || screen != barsPlayer || left == null || left.IsDisposed || right == null || right.IsDisposed)
                return 0;

            // The video export places the image from its own thread: the same room as on the screen, the bars untouched.
            if (screen.InvokeRequired)
                return barsRoom ? barsWidth : 0;

            float scale = Math.Max(1f, left.DeviceDpi / 96f);
            int width = (int)Math.Round(Math.Max(176 * scale, Math.Min(248 * scale, viewport.Width * 0.14)));
            bool room = viewport.Width - 2 * width >= 320 * scale && viewport.Height >= 300 * scale;
            barsRoom = room;
            barsWidth = room ? width : 0;
            if (left.Visible != room)
                left.Visible = room;
            if (right.Visible != room)
                right.Visible = room;
            if (!room)
                return 0;

            Rectangle leftBounds = new Rectangle(0, 0, width, viewport.Height);
            Rectangle rightBounds = new Rectangle(viewport.Width - width, 0, width, viewport.Height);
            if (left.Bounds != leftBounds)
                left.Bounds = leftBounds;
            if (right.Bounds != rightBounds)
                right.Bounds = rightBounds;

            return width;
        }

        /// <summary>
        /// The analysis is used on the video: the bars (with the setting), and the window when asked or without the bars.
        /// </summary>
        private void ShowAnalysis(bool panel)
        {
            analysisOn = true;
            UpdateBars();

            // Without visible bars (setting off, or a screen too narrow for them) the window, as before; not over the full screen,
            // where the round button has the buttons.
            if (panel || ((!Settings.VideoBars || !BarsVisible) && !DialShown))
                EnsureForm(panel);
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
            StopPilot();
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
            // In full screen the round button follows the analysis as the bars do (SwimAnalysisManager.Dial.cs).
            UpdateDial();

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
            barsFrameServer = frameServer;
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
            barsFrameServer = null;
            barsRoom = false;
            barsWidth = 0;
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

            // Another video loaded in the screen of the analysis: the analysis of that video (the clicks and the
            // graphics must not go to the analysis of the previous one).
            if (!string.Equals(frameServer.VideoReader.FilePath, videoPath, StringComparison.OrdinalIgnoreCase))
            {
                Attach(player, frameServer);
                leftBar.RefreshNow();
                rightBar.RefreshNow();
                return;
            }

            // The screen became too narrow for the bars while the window is closed: the window, so the analysis stays at hand
            // (not in full screen, where the round button has the buttons).
            if (!barsRoom && !PanelOpen && !DialShown)
                EnsureForm(false);

            leftBar.RefreshIfChanged();
            rightBar.RefreshIfChanged();
        }

        /// <summary>A click on the bars: their screen becomes the active one, the keys go to it and the analysis follows it.</summary>
        internal void BarClicked()
        {
            if (barsPlayer == null || barsPlayer.IsDisposed)
                return;

            barsPlayer.SwimBarClicked();
            if (barsPlayer != player && barsFrameServer != null && barsFrameServer.Loaded)
                Attach(barsPlayer, barsFrameServer);
        }

        /// <summary>
        /// A button of the bars: the command on the screen of the bars, as its key would (in full screen, on the screen of the round
        /// button, which has the same buttons).
        /// </summary>
        internal bool ExecuteBarCommand(PlayerScreenCommands command)
        {
            PlayerScreenUserInterface target = DialShown ? dialPlayer : barsPlayer;
            if (target == null || target.IsDisposed)
                return false;

            return target.ExecuteScreenCommand((int)command);
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
