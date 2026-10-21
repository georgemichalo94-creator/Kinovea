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
    /// The analysis in full screen (2.9.5). The full screen of the videos (VideoFullScreen) keeps no room beside the image for the bars,
    /// so a round button over the video takes their place (SwimHudDial). The window of the analysis, open, waits for the end of the full
    /// screen (the round button opens it when needed).
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Round button in full screen
        private SwimHudDial dial;
        private PlayerScreenUserInterface dialPlayer;
        private FrameServerPlayer dialFrameServer;
        private Timer dialTimer;

        /// <summary>The window of the analysis was open when the full screen started: it opens again after it.</summary>
        private bool panelBeforeFullScreen;

        /// <summary>The round button is on the video of the analysis (full screen).</summary>
        public bool DialShown
        {
            get { return dial != null && !dial.IsDisposed && dialPlayer != null && !dialPlayer.IsDisposed && dialPlayer == player; }
        }

        /// <summary>
        /// The full screen starts or ends (raised by VideoFullScreen.Set, on the UI thread, before the players place their images again):
        /// the round button comes or goes; the window of the analysis, open, closes for the full screen and opens again after it.
        /// </summary>
        private void VideoFullScreen_ActiveChanged(object sender, EventArgs e)
        {
            try
            {
                UpdateDial();
                if (VideoFullScreen.Active)
                {
                    panelBeforeFullScreen = PanelOpen && DialShown;
                    if (panelBeforeFullScreen)
                        form.Close();
                }
                else if (panelBeforeFullScreen)
                {
                    panelBeforeFullScreen = false;
                    if (analysisOn && HasVideo && !PanelOpen)
                        EnsureForm(false);
                }
            }
            catch (Exception ex)
            {
                log.Error("Error while following the full screen of the videos.", ex);
            }
        }

        /// <summary>
        /// LayoutVideoBars in full screen: no room beside the image (0), the bars of this screen hidden (they come back with their room
        /// after the full screen), the round button placed in the video area.
        /// </summary>
        private int LayoutFullScreen(PlayerScreenUserInterface screen, Size viewport)
        {
            if (screen == null || screen.InvokeRequired)
                return 0;

            if (screen == barsPlayer)
            {
                foreach (SwimVideoBar bar in new SwimVideoBar[] { leftBar, rightBar })
                {
                    if (bar != null && !bar.IsDisposed && bar.Visible)
                        bar.Visible = false;
                }
            }

            if (screen == dialPlayer && dial != null && !dial.IsDisposed)
                dial.PlaceIn(viewport);

            return 0;
        }

        /// <summary>Puts the round button on the video of the analysis in full screen, moves it to another screen, or removes it.</summary>
        private void UpdateDial()
        {
            bool want = VideoFullScreen.Active && analysisOn && HasVideo && Race != null;
            PlayerScreenUserInterface target = want ? player : null;
            bool present = dial != null && !dial.IsDisposed && dialPlayer != null && !dialPlayer.IsDisposed;
            if (present && dialPlayer == target)
            {
                dial.RefreshNow();
                return;
            }

            RemoveDial();
            if (target == null)
                return;

            dial = new SwimHudDial(this);
            dialPlayer = target;
            dialFrameServer = frameServer;
            target.panelCenter.Controls.Add(dial);
            dial.PlaceIn(target.panelCenter.ClientSize);
            dial.BringToFront();

            if (dialTimer == null)
            {
                dialTimer = new Timer();
                dialTimer.Interval = 150;
                dialTimer.Tick += DialTimer_Tick;
            }

            dialTimer.Start();
        }

        private void RemoveDial()
        {
            if (dial != null && !dial.IsDisposed)
            {
                if (dial.Parent != null)
                    dial.Parent.Controls.Remove(dial);
                dial.Dispose();
            }

            dial = null;
            dialPlayer = null;
            dialFrameServer = null;
            if (dialTimer != null)
                dialTimer.Stop();
        }

        private void DialTimer_Tick(object sender, EventArgs e)
        {
            if (dial == null || dial.IsDisposed || dialPlayer == null || dialPlayer.IsDisposed)
            {
                // The screen was closed with its round button.
                RemoveDial();
                return;
            }

            if (dialPlayer != player || !HasVideo || !VideoFullScreen.Active)
            {
                UpdateDial();
                return;
            }

            dial.RefreshIfChanged();
        }

        /// <summary>A click on the round button: its screen becomes the active one and gets the keys, the analysis follows it.</summary>
        internal void DialClicked()
        {
            if (dialPlayer == null || dialPlayer.IsDisposed)
                return;

            dialPlayer.SwimBarClicked();
            if (dialPlayer != player && dialFrameServer != null && dialFrameServer.Loaded)
                Attach(dialPlayer, dialFrameServer);
        }

        /// <summary>The round button hidden in its dot, or shown again («Απόκρυψη», a click on the dot, the key of SwimDial).</summary>
        internal void SetDialHidden(bool hidden)
        {
            Settings.DialHidden = hidden;
            Settings.Save();
            if (dial != null && !dial.IsDisposed)
                dial.RefreshNow();
        }

        /// <summary>The key of SwimDial (D): in full screen the round button hidden or shown again; it also brings the analysis back.</summary>
        private void ToggleDial()
        {
            if (!VideoFullScreen.Active)
                return;

            if (!analysisOn || !DialShown)
            {
                Settings.DialHidden = false;
                Settings.Save();
                ShowAnalysis(false);
                return;
            }

            SetDialHidden(!Settings.DialHidden);
        }
        #endregion
    }
}
