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
    /// The analysis without room beside the images: the full screen of the videos (2.9.5, VideoFullScreen) and the big screens (2.14.0,
    /// VideoBigScreens). A round button over the video takes the place of the bars (SwimHudDial). Since 2.14.0 every screen with a
    /// video has its own round button (several cameras of the same swimmer): the one of the screen of the analysis has its buttons
    /// and the race time; the others wait, and a click on one takes the analysis to its video. The screens of a group (VideoScreenGroup:
    /// the same race from several cameras) have one round button in the middle of the group instead, and one analysis: the one of the
    /// video of its lead, where the marks made on any screen of the group go at the same moment. The window of the analysis, open,
    /// waits for the end of the full screen (the round button opens it when needed); with the big screens it stays.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Round buttons over the videos
        /// <summary>A round button and the screen and frame server it is for (the group: its lead) and, for the group, its parent.</summary>
        private sealed class DialSlot
        {
            public SwimHudDial Dial;
            public PlayerScreenUserInterface Screen;
            public FrameServerPlayer FrameServer;
            public bool Group;
            public Control Host;
        }

        private readonly List<DialSlot> dials = new List<DialSlot>();
        private Timer dialTimer;

        /// <summary>The window of the analysis was open when the full screen started: it opens again after it.</summary>
        private bool panelBeforeFullScreen;

        /// <summary>(2.14.0) The screen of the group the analyst works on: its frame is the moment of a mark of the bars or the round button.</summary>
        private PlayerScreenUserInterface groupSource;

        /// <summary>No room beside the images (full screen or big screens): round buttons over the videos instead of the bars.</summary>
        public bool DialMode
        {
            get { return VideoFullScreen.Active || VideoBigScreens.Active; }
        }

        /// <summary>The round button of the screen of the analysis (or of its group) is shown.</summary>
        public bool DialShown
        {
            get { return DialOf(player) != null; }
        }

        /// <summary>(2.14.0) The screen of the analysis (null without a video): the lead of a group is chosen with it.</summary>
        internal PlayerScreenUserInterface AnalysisScreen
        {
            get { return HasVideo ? player : null; }
        }

        /// <summary>(2.14.0) The analysis stays with its video: a reading runs on it or the auto pilot works on it.</summary>
        internal bool AnalysisBusy
        {
            get { return ReadingBusy || PilotActive; }
        }

        /// <summary>The round button of a screen: its own, or the one of its group.</summary>
        private DialSlot DialOf(PlayerScreenUserInterface screen)
        {
            if (screen == null || screen.IsDisposed)
                return null;
            if (VideoScreenGroup.Contains(screen))
                return GroupSlot;
            return dials.FirstOrDefault(d => !d.Group && d.Screen == screen && d.Dial != null && !d.Dial.IsDisposed);
        }

        private DialSlot GroupSlot
        {
            get { return dials.FirstOrDefault(d => d.Group && d.Dial != null && !d.Dial.IsDisposed); }
        }

        private DialSlot SlotOf(SwimHudDial dial)
        {
            return dials.FirstOrDefault(d => d.Dial == dial);
        }

        /// <summary>The round button is on the screen of the analysis, or is the one of its group (it has the buttons and the race time).</summary>
        internal bool IsAnalysisScreen(SwimHudDial dial)
        {
            DialSlot slot = SlotOf(dial);
            if (slot == null || !HasVideo)
                return false;
            return slot.Group ? VideoScreenGroup.Contains(player) : slot.Screen == player;
        }

        /// <summary>The number of the screen of a round button (1 for the first), 0 when not known or for a group.</summary>
        internal int ScreenNumberOf(SwimHudDial dial)
        {
            DialSlot slot = SlotOf(dial);
            return slot != null && !slot.Group && slot.Screen != null && !slot.Screen.IsDisposed ? slot.Screen.SwimScreenNumber : 0;
        }

        /// <summary>The name of a round button in its tips: «Οθόνη 2», «Ομάδα: οθόνες 1, 2, 3».</summary>
        internal string DialName(SwimHudDial dial)
        {
            DialSlot slot = SlotOf(dial);
            if (slot == null)
                return "";
            if (slot.Group)
                return string.Format(CultureInfo.InvariantCulture, SwimLang.T("Ομάδα: οθόνες {0}"), GroupNumbers(", "));

            int number = ScreenNumberOf(dial);
            return number > 0 ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("Οθόνη {0}"), number) : "";
        }

        /// <summary>The text of a round button waiting for a click: the number of its screen, or the screens of its group.</summary>
        internal string StandbyText(SwimHudDial dial)
        {
            DialSlot slot = SlotOf(dial);
            if (slot == null)
                return "";
            if (slot.Group)
                return SwimLang.T("ομάδα") + "\n" + GroupNumbers("·");

            int number = ScreenNumberOf(dial);
            return number > 0 ? number.ToString(CultureInfo.InvariantCulture) + "\n" + SwimLang.T("οθόνη") : "";
        }

        /// <summary>The numbers of the screens of the group, in order.</summary>
        private static string GroupNumbers(string separator)
        {
            return string.Join(separator, VideoScreenGroup.Members.Where(m => !m.IsDisposed && m.SwimScreenNumber > 0)
                .Select(m => m.SwimScreenNumber).OrderBy(n => n).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray());
        }

        /// <summary>The screen of the group the analyst works on, else the lead (null without a group).</summary>
        private PlayerScreenUserInterface GroupSourceOrLead()
        {
            PlayerScreenUserInterface s = groupSource;
            if (s != null && !s.IsDisposed && VideoScreenGroup.Contains(s) && s.SwimFrameServer != null && s.SwimFrameServer.Loaded)
                return s;
            return VideoScreenGroup.Lead;
        }

        /// <summary>
        /// (2.14.0) A screen of a group of screens: its lead (the video of the race of the group) and the same moment in it, through the
        /// time origins of the videos (where the screens are synchronised). The screen is kept as the one the analyst works on.
        /// </summary>
        private void ToGroup(ref PlayerScreenUserInterface screen, ref FrameServerPlayer server, ref long timestamp)
        {
            if (!VideoScreenGroup.Contains(screen))
                return;

            groupSource = screen;
            PlayerScreenUserInterface lead = VideoScreenGroup.Lead;
            if (lead == null || lead == screen || lead.IsDisposed || lead.SwimFrameServer == null || !lead.SwimFrameServer.Loaded)
                return;

            timestamp = VideoScreenGroup.LeadTimestamp(screen, timestamp);
            screen = lead;
            server = lead.SwimFrameServer;
        }

        /// <summary>
        /// The full screen starts or ends (raised by VideoFullScreen.Set, on the UI thread, before the players place their images again):
        /// the round buttons come or go; the window of the analysis, open, closes for the full screen and opens again after it.
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

        /// <summary>The big screens start or end (2.14.0): the round buttons instead of the bars, or the bars again.</summary>
        private void VideoBigScreens_ActiveChanged(object sender, EventArgs e)
        {
            try
            {
                UpdateBars();
            }
            catch (Exception ex)
            {
                log.Error("Error while following the big screens.", ex);
            }
        }

        /// <summary>
        /// (2.14.0) The group of screens made, changed or undone: an analysis on a screen of the group goes to its lead (the race of the
        /// group), the round buttons follow; a word on the synchronisation the group needs.
        /// </summary>
        private void VideoScreenGroup_Changed(object sender, EventArgs e)
        {
            try
            {
                if (groupSource != null && !VideoScreenGroup.Contains(groupSource))
                    groupSource = null;

                PlayerScreenUserInterface lead = VideoScreenGroup.Lead;
                if (lead != null && VideoScreenGroup.Contains(player) && player != lead && !AnalysisBusy && lead.SwimFrameServer != null && lead.SwimFrameServer.Loaded)
                    SwitchTo(lead, lead.SwimFrameServer);
                else
                    UpdateBars();

                if (VideoScreenGroup.Active && lead != null && analysisOn)
                {
                    Status(string.Format(CultureInfo.InvariantCulture,
                        SwimLang.T("Ομάδα οθονών {0}: μία ανάλυση, στο βίντεο της οθόνης {1}. Ένα σημάδι από όποια οθόνη της ομάδας μπαίνει στην ίδια στιγμή μέσω του συγχρονισμού: συγχρόνισε πρώτα τις οθόνες στο σήμα εκκίνησης."),
                        GroupNumbers(", "), lead.SwimScreenNumber), false);
                }
            }
            catch (Exception ex)
            {
                log.Error("Error while following the group of screens.", ex);
            }
        }

        /// <summary>
        /// LayoutVideoBars without room beside the images: no room beside the image (0), the bars of this screen hidden (they come back
        /// with their room after), the round button of this screen placed in its video area (made when the analysis is in use and the
        /// screen has a video); a screen of a group: the round button of the group in the middle of its screens.
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

            if (VideoScreenGroup.Contains(screen))
            {
                // Its own round button, from before the group, hidden (it goes at the next update).
                DialSlot own = dials.FirstOrDefault(d => !d.Group && d.Screen == screen && d.Dial != null && !d.Dial.IsDisposed);
                if (own != null && own.Dial.Visible)
                    own.Dial.Visible = false;
                PlaceGroupDial(true);
                return 0;
            }

            DialSlot slot = DialOf(screen);
            if (slot == null && analysisOn && HasVideo && Race != null)
                slot = AddDial(screen, screen.SwimFrameServer);
            if (slot != null)
                slot.Dial.PlaceIn(viewport);

            return 0;
        }

        /// <summary>
        /// The round buttons: one on every screen with a video while the analysis is in use and there is no room beside the images
        /// (the screen of the analysis first, the others as they place their images), one for a group of screens; none otherwise.
        /// </summary>
        private void UpdateDial()
        {
            bool want = DialMode && analysisOn && HasVideo && Race != null;
            if (!want)
            {
                RemoveDials();
                return;
            }

            // Gone: closed screens, screens without a video now, screens now in the group; the group undone.
            foreach (DialSlot gone in dials.Where(Gone).ToList())
                RemoveDial(gone);

            if (VideoScreenGroup.Active)
                PlaceGroupDial(true);
            if (DialOf(player) == null && !VideoScreenGroup.Contains(player))
                AddDial(player, frameServer);

            foreach (DialSlot slot in dials.ToList())
            {
                if (slot.Dial != null && !slot.Dial.IsDisposed)
                    slot.Dial.RefreshNow();
            }
        }

        private static bool Gone(DialSlot d)
        {
            if (d.Dial == null || d.Dial.IsDisposed || d.Screen == null || d.Screen.IsDisposed || d.FrameServer == null || !d.FrameServer.Loaded)
                return true;
            if (d.Group)
                return !VideoScreenGroup.Active || d.Host == null || d.Host.IsDisposed;
            return VideoScreenGroup.Contains(d.Screen);
        }

        private DialSlot AddDial(PlayerScreenUserInterface screen, FrameServerPlayer server)
        {
            if (screen == null || screen.IsDisposed || server == null || !server.Loaded || VideoScreenGroup.Contains(screen))
                return null;

            DialSlot slot = new DialSlot { Dial = new SwimHudDial(this), Screen = screen, FrameServer = server };
            dials.Add(slot);
            screen.panelCenter.Controls.Add(slot.Dial);
            slot.Dial.PlaceIn(screen.panelCenter.ClientSize);
            slot.Dial.BringToFront();
            StartDialTimer();
            return slot;
        }

        private void StartDialTimer()
        {
            if (dialTimer == null)
            {
                dialTimer = new Timer();
                dialTimer.Interval = 150;
                dialTimer.Tick += DialTimer_Tick;
            }

            dialTimer.Start();
        }

        /// <summary>
        /// (2.14.0) The round button of the group in the middle of its screens, on their common parent (made when 'create' and the
        /// analysis is in use); moved to another parent when the screens moved (a screen added, the full screen).
        /// </summary>
        private void PlaceGroupDial(bool create)
        {
            PlayerScreenUserInterface lead = VideoScreenGroup.Lead;
            DialSlot slot = GroupSlot;
            if (!VideoScreenGroup.Active || lead == null || lead.IsDisposed || lead.SwimFrameServer == null || !lead.SwimFrameServer.Loaded)
            {
                if (slot != null)
                    RemoveDial(slot);
                return;
            }

            Rectangle area;
            Control host = GroupHost(out area);
            if (host == null || area.Width < 1 || area.Height < 1)
                return;

            if (slot != null && slot.Host != host)
            {
                RemoveDial(slot);
                slot = null;
            }

            if (slot == null)
            {
                if (!create || !DialMode || !analysisOn || !HasVideo || Race == null)
                    return;

                slot = new DialSlot { Dial = new SwimHudDial(this, true), Screen = lead, FrameServer = lead.SwimFrameServer, Group = true, Host = host };
                dials.Add(slot);
                host.Controls.Add(slot.Dial);
                StartDialTimer();
            }

            if (slot.Screen != lead)
            {
                slot.Screen = lead;
                slot.FrameServer = lead.SwimFrameServer;
                slot.Dial.RefreshNow();
            }

            slot.Dial.PlaceIn(area);
        }

        /// <summary>
        /// The smallest control holding all the screens of the group (not a split container: it takes only its two panels), and the area
        /// of their images in it.
        /// </summary>
        private static Control GroupHost(out Rectangle area)
        {
            area = Rectangle.Empty;
            List<PlayerScreenUserInterface> members = VideoScreenGroup.Members.Where(m => !m.IsDisposed && m.IsHandleCreated && m.Visible).ToList();
            if (members.Count < 2)
                return null;

            Control host = members[0].Parent;
            while (host != null && !members.All(m => host.Contains(m)))
                host = host.Parent;
            while (host is SplitContainer)
                host = host.Parent;
            if (host == null || host.IsDisposed || !host.IsHandleCreated)
                return null;

            Rectangle union = Rectangle.Empty;
            foreach (PlayerScreenUserInterface m in members)
            {
                Rectangle r = host.RectangleToClient(m.panelCenter.RectangleToScreen(m.panelCenter.ClientRectangle));
                union = union.IsEmpty ? r : Rectangle.Union(union, r);
            }

            area = Rectangle.Intersect(union, host.ClientRectangle);
            return host;
        }

        private void RemoveDial(DialSlot slot)
        {
            dials.Remove(slot);
            if (slot.Dial != null && !slot.Dial.IsDisposed)
            {
                if (slot.Dial.Parent != null)
                    slot.Dial.Parent.Controls.Remove(slot.Dial);
                slot.Dial.Dispose();
            }
        }

        private void RemoveDials()
        {
            foreach (DialSlot slot in dials.ToList())
                RemoveDial(slot);
            if (dialTimer != null)
                dialTimer.Stop();
        }

        private void DialTimer_Tick(object sender, EventArgs e)
        {
            if (!DialMode || !analysisOn || !HasVideo)
            {
                RemoveDials();
                return;
            }

            bool changed = false;
            foreach (DialSlot slot in dials.ToList())
            {
                if (Gone(slot))
                {
                    // The screen was closed with its round button, or its video; the group was undone.
                    RemoveDial(slot);
                    changed = true;
                    continue;
                }

                // The round button of the group follows its screens (their places change without a layout of each).
                if (slot.Group)
                    PlaceGroupDial(false);
                if (!slot.Dial.IsDisposed)
                    slot.Dial.RefreshIfChanged();
            }

            if (changed || DialOf(player) == null || (VideoScreenGroup.Active && GroupSlot == null))
                UpdateDial();
        }

        /// <summary>
        /// A click on a round button: its screen becomes the active one and gets the keys, the analysis follows it (another camera of
        /// the same swimmer: its own analysis; the group: the analysis of the group, from the screen of the group the analyst works
        /// on), unless a reading or the auto pilot works on the video of the analysis.
        /// </summary>
        internal void DialClicked(SwimHudDial dial)
        {
            DialSlot slot = SlotOf(dial);
            if (slot == null || slot.Screen == null || slot.Screen.IsDisposed)
                return;

            if (slot.Group)
            {
                PlayerScreenUserInterface source = GroupSourceOrLead();
                if (source != null && !source.IsDisposed)
                    source.SwimBarClicked();

                PlayerScreenUserInterface lead = VideoScreenGroup.Lead;
                if (lead != null && player != lead && lead.SwimFrameServer != null && lead.SwimFrameServer.Loaded && !AnalysisBusy)
                    SwitchTo(lead, lead.SwimFrameServer);
            }
            else
            {
                slot.Screen.SwimBarClicked();
                if (slot.Screen != player && slot.FrameServer != null && slot.FrameServer.Loaded && !AnalysisBusy)
                    SwitchTo(slot.Screen, slot.FrameServer);
            }

            foreach (DialSlot d in dials.ToList())
            {
                if (d.Dial != null && !d.Dial.IsDisposed)
                    d.Dial.RefreshNow();
            }
        }

        /// <summary>
        /// (2.14.0) The analysis to the video of another screen: its own analysis loaded, the bars or the round buttons follow, both
        /// screens drawn again (the graphics of the analysis leave the previous one).
        /// </summary>
        private void SwitchTo(PlayerScreenUserInterface screen, FrameServerPlayer server)
        {
            PlayerScreenUserInterface old = player;
            Attach(screen, server);
            UpdateBars();
            InvalidatePlayer();
            if (old != null && old != player && !old.IsDisposed)
                old.DoInvalidate();
        }

        /// <summary>The round buttons hidden in their dots, or shown again («Απόκρυψη», a click on a dot, the key of SwimDial).</summary>
        internal void SetDialHidden(bool hidden)
        {
            Settings.DialHidden = hidden;
            Settings.Save();
            foreach (DialSlot slot in dials.ToList())
            {
                if (slot.Dial != null && !slot.Dial.IsDisposed)
                    slot.Dial.RefreshNow();
            }
        }

        /// <summary>
        /// The key of SwimDial (D): without room beside the images the round buttons hidden or shown again; it also brings the analysis
        /// back.
        /// </summary>
        private void ToggleDial()
        {
            if (!DialMode)
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

        /// <summary>
        /// (2.14.0) The analyst works on another screen (clicked, or its controls used): when the analysis is in use it follows that
        /// screen's video (several cameras of the same swimmer, each with its own analysis; a screen of a group: the analysis of the
        /// group). Not while a reading runs, a click on the image is awaited or the auto pilot works on the video: they stay with their
        /// video.
        /// </summary>
        public void ScreenActivated(PlayerScreenUserInterface screen, FrameServerPlayer server)
        {
            try
            {
                if (screen == null || screen.IsDisposed || server == null || !server.Loaded)
                    return;

                PlayerScreenUserInterface target = screen;
                FrameServerPlayer targetServer = server;
                if (VideoScreenGroup.Contains(screen))
                {
                    groupSource = screen;
                    target = VideoScreenGroup.Lead;
                    targetServer = target != null ? target.SwimFrameServer : null;
                    if (target == null || target.IsDisposed || targetServer == null || !targetServer.Loaded)
                        return;
                }

                if (!analysisOn || target == player)
                    return;
                if (AnalysisBusy || PickTarget != SwimPickTarget.None)
                    return;

                SwitchTo(target, targetServer);
            }
            catch (Exception ex)
            {
                log.Error("Error while following the active screen.", ex);
            }
        }
        #endregion
    }
}
