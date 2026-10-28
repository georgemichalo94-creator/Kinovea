#region License
/*
Copyright © Joan Charmant 2008.
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
using Kinovea.ScreenManager.Properties;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    public partial class CommonControlsPlayers : KinoveaControl
    {
        #region Events
        public event EventHandler PlayToggled;
        public event EventHandler GotoFirst;
        public event EventHandler GotoPrev;
        public event EventHandler GotoPrevKeyframe;
        public event EventHandler GotoNext;
        public event EventHandler GotoLast;
        public event EventHandler GotoNextKeyframe;
        public event EventHandler GotoSync;
        public event EventHandler<TimeEventArgs> PositionChanged;
        public event EventHandler AddKeyframe;
        public event EventHandler SwapAsked;
        public event EventHandler SyncAsked;
        public event EventHandler MergeAsked;
        public event EventHandler ExportImageAsked;
        public event EventHandler ExportvideoAsked;
        /// <summary>(Swim 2.14.0) The big screens on or off: the screens show only their images, these controls drive them.</summary>
        public event EventHandler BigScreensAsked;
        /// <summary>(Swim 2.14.0) The menu of the group of screens (the sender is the button, to show it under).</summary>
        public event EventHandler GroupAsked;
        #endregion

        #region Properties
        public bool Playing
        {
            get { return playing;  }
        }
        public bool Merging
        {
            get { return merging; }
        }
        #endregion

        #region Members
        private bool playing;
        private bool merging;
        private int playerCount = 2;
        private long oldPosition;
        private Button btnBigScreens;
        private bool bigScreens;
        private Button btnGroup;
        private string groupTip;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        #endregion

        #region Construction
        public CommonControlsPlayers()
        {
            InitializeComponent();
            PostInit();
            this.Hotkeys = HotkeySettingsManager.LoadHotkeys("DualPlayer");
        }

        private void PostInit()
        {
            BackColor = Color.White;
            trkFrame.SetAsCommonTimeline(true);

            btnSnapshot.Click += (s, e) => ExportImageAsked?.Invoke(s, e);
            btnDualSave.Click += (s, e) => ExportvideoAsked?.Invoke(s, e);
            btnSwap.Click += (s, e) => SwapAsked?.Invoke(s, e);
            btnSync.Click += (s, e) => SyncAsked?.Invoke(s, e);
            AddBigScreensButton();
        }

        /// <summary>
        /// (Swim 2.14.0) The buttons of the group of screens and of the big screens, at the right end: the timeline and the side by side
        /// exports move left to make room for them.
        /// </summary>
        private void AddBigScreensButton()
        {
            const int room = 68;
            trkFrame.Width -= room;
            btnSnapshot.Left -= room;
            btnDualSave.Left -= room;

            btnGroup = new Button();
            btnGroup.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnGroup.Cursor = Cursors.Hand;
            btnGroup.FlatStyle = FlatStyle.Flat;
            btnGroup.FlatAppearance.BorderSize = 0;
            btnGroup.FlatAppearance.MouseOverBackColor = Color.WhiteSmoke;
            btnGroup.BackColor = Color.White;
            btnGroup.Margin = new Padding(0);
            btnGroup.Size = new Size(30, 25);
            btnGroup.Location = new Point(btnDualSave.Right + 6, 10);
            btnGroup.TabStop = false;
            btnGroup.Image = SwimScreensIcons.Group(false);
            btnGroup.Click += (s, e) => GroupAsked?.Invoke(btnGroup, EventArgs.Empty);
            Controls.Add(btnGroup);
            groupTip = SwimScreensIcons.GroupTip(null, 0);
            toolTips.SetToolTip(btnGroup, groupTip);

            btnBigScreens = new Button();
            btnBigScreens.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBigScreens.Cursor = Cursors.Hand;
            btnBigScreens.FlatStyle = FlatStyle.Flat;
            btnBigScreens.FlatAppearance.BorderSize = 0;
            btnBigScreens.FlatAppearance.MouseOverBackColor = Color.WhiteSmoke;
            btnBigScreens.BackColor = Color.White;
            btnBigScreens.Margin = new Padding(0);
            btnBigScreens.Size = new Size(30, 25);
            btnBigScreens.Location = new Point(btnGroup.Right + 4, 10);
            btnBigScreens.TabStop = false;
            btnBigScreens.Image = SwimScreensIcons.BigScreens(false);
            btnBigScreens.Click += (s, e) => BigScreensAsked?.Invoke(this, EventArgs.Empty);
            Controls.Add(btnBigScreens);
            toolTips.SetToolTip(btnBigScreens, SwimScreensIcons.BigScreensTip(false));
        }

        /// <summary>(Swim 2.14.0) There is a group of screens or not: the icon and the tip of its button follow.</summary>
        public void SetGroup(bool on, string tip)
        {
            if (btnGroup == null)
                return;

            Image old = btnGroup.Image;
            btnGroup.Image = SwimScreensIcons.Group(on);
            if (old != null)
                old.Dispose();
            groupTip = tip;
            toolTips.SetToolTip(btnGroup, tip);
        }

        /// <summary>(Swim 2.14.0) The big screens are on or off: the icon and the tip of the button follow.</summary>
        public void SetBigScreens(bool on)
        {
            bigScreens = on;
            if (btnBigScreens == null)
                return;

            Image old = btnBigScreens.Image;
            btnBigScreens.Image = SwimScreensIcons.BigScreens(on);
            if (old != null)
                old.Dispose();
            toolTips.SetToolTip(btnBigScreens, SwimScreensIcons.BigScreensTip(on));
        }
        #endregion
        
        #region Public methods
        public void RefreshUICulture()
        {
            // Labels
            lblInfo.Text = ScreenManagerLang.lblInfo_Text;

            // ToolTips
            toolTips.SetToolTip(buttonGotoFirst, ScreenManagerLang.buttonGotoFirst_ToolTip);
            toolTips.SetToolTip(buttonGotoLast, ScreenManagerLang.buttonGotoLast_ToolTip);
            toolTips.SetToolTip(buttonGotoNext, ScreenManagerLang.buttonGotoNext_ToolTip);
            toolTips.SetToolTip(buttonGotoPrevious, ScreenManagerLang.buttonGotoPrevious_ToolTip);
            toolTips.SetToolTip(buttonPlay, ScreenManagerLang.buttonPlay_ToolTip);
            toolTips.SetToolTip(btnSwap, ScreenManagerLang.mnuSwapScreens);
            toolTips.SetToolTip(btnSync, ScreenManagerLang.btnSync_ToolTip);
            toolTips.SetToolTip(btnSnapshot, ScreenManagerLang.ToolTip_SideBySideSnapshot);
            toolTips.SetToolTip(btnDualSave, ScreenManagerLang.ToolTip_SideBySideVideo);
            if (btnBigScreens != null)
                toolTips.SetToolTip(btnBigScreens, SwimScreensIcons.BigScreensTip(bigScreens));
            if (btnGroup != null)
                toolTips.SetToolTip(btnGroup, groupTip);
            
            RefreshMergeTooltip();
        }

        public void SetupTrkFrame(long min, long max, long pos)
        {
            trkFrame.Minimum = min;
            trkFrame.Maximum = max;
            trkFrame.Position = pos;
            trkFrame.Invalidate();
            UpdateDebug();
        }

        public void UpdateSyncPosition(long position)
        {
            trkFrame.UpdateSyncPointMarker(position);
            trkFrame.Invalidate();
            UpdateDebug();
        }
        
        public void UpdateCurrentPosition(long position)
        {
            trkFrame.Position = position;
            trkFrame.Invalidate();
            UpdateDebug();
        }

        public void UpdateHairline(long position, bool left)
        {
            if (left)
                trkFrame.LeftHairline = position;
            else
                trkFrame.RightHairline = position;

            trkFrame.UpdatePlayHeadMarkers();
            trkFrame.Invalidate();
        }

        private void UpdateDebug()
        {
            //lblInfo.Text = string.Format("{0}/{1}, s:{2}", trkFrame.Position, trkFrame.Maximum, trkFrame.SyncPosition);
        }
        
        public void Pause()
        {
            playing = false;
            RefreshPlayButton();
        }

        public void Play()
        {
            playing = true;
            RefreshPlayButton();
        }

        public void StopMerge()
        {
            merging = false;
            RefreshMergeTooltip();
        }

        /// <summary>
        /// Number of player screens under the common controls.
        /// Superposition, swap and side by side export only exist for two screens.
        /// </summary>
        public void SetPlayerCount(int count)
        {
            playerCount = count;
            bool dual = count <= 2;
            btnMerge.Visible = dual;
            btnSwap.Visible = dual;
            btnSnapshot.Visible = dual;
            btnDualSave.Visible = dual;

            // (Swim 2.14.0) The timeline up to the next button shown (the side by side exports, or the group and the big screens).
            if (btnBigScreens != null && btnGroup != null)
            {
                int end = dual ? btnSnapshot.Left - 2 : btnGroup.Left - 4;
                if (end - trkFrame.Left >= trkFrame.MinimumSize.Width)
                    trkFrame.Width = end - trkFrame.Left;
            }

            if (!dual && merging)
                StopMerge();
        }
        #endregion

        #region Commands

        protected override bool ExecuteCommand(int cmd)
        {
            DualPlayerCommands command = (DualPlayerCommands)cmd;

            switch (command)
            {
                case DualPlayerCommands.TogglePlay:
                    PlayPause();
                    break;
                case DualPlayerCommands.GotoPreviousImage:
                    Previous();
                    break;
                case DualPlayerCommands.GotoFirstImage:
                    First();
                    break;
                case DualPlayerCommands.GotoPreviousKeyframe:
                    if (GotoPrevKeyframe != null)
                        GotoPrevKeyframe(this, EventArgs.Empty);
                    break;
                case DualPlayerCommands.GotoNextImage:
                    Next();
                    break;
                case DualPlayerCommands.GotoLastImage:
                    Last();
                    break;
                case DualPlayerCommands.GotoNextKeyframe:
                    if (GotoNextKeyframe != null)
                        GotoNextKeyframe(this, EventArgs.Empty);
                    break;
                case DualPlayerCommands.GotoSyncPoint:
                    if (GotoSync != null)
                        GotoSync(this, EventArgs.Empty);
                    break;
                case DualPlayerCommands.ToggleSyncMerge:
                    ToggleMerge();
                    break;
                case DualPlayerCommands.AddKeyframe:
                    if (AddKeyframe != null)
                        AddKeyframe(this, EventArgs.Empty);
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

        private void First()
        {
            if (GotoFirst != null)
                GotoFirst(this, EventArgs.Empty);
        }
        private void Previous()
        {
            if (GotoPrev != null)
                GotoPrev(this, EventArgs.Empty);
        }
        private void PlayPause()
        {
            playing = !playing;
            RefreshPlayButton();

            if (PlayToggled != null)
                PlayToggled(this, EventArgs.Empty);
        }
        private void Next()
        {
            if (GotoNext != null)
                GotoNext(this, EventArgs.Empty);
        }
        private void Last()
        {
            if (GotoLast != null)
                GotoLast(this, EventArgs.Empty);
        }
        private void ToggleMerge()
        {
            if (playerCount > 2)
                return;

            merging = !merging;

            if (MergeAsked != null)
                MergeAsked(this, EventArgs.Empty);

            RefreshMergeTooltip();
        }
        #region UI Handlers
        private void buttonGotoFirst_Click(object sender, EventArgs e)
        {
            First();
        }
        private void buttonGotoPrevious_Click(object sender, EventArgs e)
        {
            Previous();
        }
        private void buttonPlay_Click(object sender, EventArgs e)
        {
            PlayPause();
        }
        private void buttonGotoNext_Click(object sender, EventArgs e)
        {
            Next();
        }
        private void buttonGotoLast_Click(object sender, EventArgs e)
        {
            Last();
        }
        private void btnMerge_Click(object sender, EventArgs e)
        {
            ToggleMerge();
        }
        private void trkFrame_PositionChanged(object sender, TimeEventArgs e)
        {
            if (e.Time == oldPosition)
                return;
            
            oldPosition = e.Time;

            if (PositionChanged != null)
                PositionChanged(this, new TimeEventArgs(e.Time));
        }
        #endregion
        
        #region Lower level helpers
        private void RefreshPlayButton()
        {
            buttonPlay.Image = playing ? Resources.flatpause3b : Resources.flatplay;
        }
        private void RefreshMergeTooltip()
        {
            string text = merging ? ScreenManagerLang.ToolTip_CommonCtrl_DisableMerge : ScreenManagerLang.ToolTip_CommonCtrl_EnableMerge;
            toolTips.SetToolTip(btnMerge, text);
        }
        #endregion
    }
}
