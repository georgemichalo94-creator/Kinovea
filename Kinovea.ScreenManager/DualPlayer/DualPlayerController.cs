using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.IO;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Synchronization logic.
    /// Works with two or more player screens: the loaded ones are synchronized together on their time origins.
    /// Superposition (merge) and side by side export are only for two screens.
    /// </summary>
    public class DualPlayerController
    {
        #region Events
        public event EventHandler ExportImageAsked;
        public event EventHandler ExportVideoAsked;
        #endregion

        #region Properties
        public CommonControlsPlayers View
        {
            get { return view; }
        }
        public bool Active
        {
            get { return active; }
        }
        public bool DualSaveInProgress
        {
            get { return dualSaveInProgress; }
            set { dualSaveInProgress = value; }
        }

        public CommonTimeline CommonTimeline
        {
            get { return commonTimeline; }
        }
        #endregion

        #region Members
        private bool active;
        private bool synching;
        private bool dynamicSynching;
        private bool dualSaveInProgress;

        private CommonTimeline commonTimeline = new CommonTimeline();   
        private long currentTime;   // current time in common timeline, in microseconds.
        private CommonControlsPlayers view = new CommonControlsPlayers();
        private List<PlayerScreen> players = new List<PlayerScreen>();  // All the player screens, in screen order.
        private List<PlayerScreen> synced = new List<PlayerScreen>();   // The loaded ones, synchronized together.
        private int resyncOperations = 0;
        private int maxResyncOperations = 1;
        private HotkeyCommand[] hotkeys;

        // If two videos have creation dates within this many seconds we consider them part 
        // of the same dual recording and start them together in the replay watcher context.
        private const int spanDualRecording = 5;
                                                 
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        #endregion

        #region Constructor
        public DualPlayerController()
        {
            view.Dock = DockStyle.Fill;

            view.PlayToggled += CCtrl_PlayToggled;
            view.GotoFirst += CCtrl_GotoFirst;
            view.GotoPrev += CCtrl_GotoPrev;
            view.GotoPrevKeyframe += CCtrl_GotoPrevKeyframe;
            view.GotoNext += CCtrl_GotoNext;
            view.GotoLast += CCtrl_GotoLast;
            view.GotoNextKeyframe += CCtrl_GotoNextKeyframe;
            view.GotoSync += CCtrl_GotoSync;
            view.AddKeyframe += CCtrl_AddKeyframe;
            view.SyncAsked += CCtrl_SyncAsked;
            view.MergeAsked += CCtrl_MergeAsked;
            view.PositionChanged += CCtrl_PositionChanged;
            view.ExportImageAsked += (s, e) => ExportImageAsked?.Invoke(s, e);
            view.ExportvideoAsked += (s, e) => ExportVideoAsked?.Invoke(s, e);

            hotkeys = HotkeySettingsManager.LoadHotkeys("DualPlayer");
        }
        #endregion

        #region Public methods
        public void ScreenListChanged(List<AbstractScreen> screenList)
        {
            if (screenList.Count >= 2 && screenList.All(s => s is PlayerScreen))
                Enter(screenList);
            else
                Exit();

            view.SetPlayerCount(players.Count);
        }
        public void RefreshUICulture()
        {
            view.RefreshUICulture();

            if (synching)
                InitializeSync();
        }
        public void UpdateTrkFrame(long position)
        {
            view.UpdateCurrentPosition(position);
        }
        public void Pause()
        {
            if (!active || !synching)
                return;

            dynamicSynching = false;

            if (view.Playing)
                view.Pause();

            foreach (PlayerScreen player in synced)
            {
                if (player.IsPlaying)
                    player.view.OnButtonPlay();
            }
        }
        public void StopMerge()
        {
            view.StopMerge();
        }
        /// <summary>
        /// (Swim 2.14.0) Screens changed places (one dragged onto another): the players follow the order of the screens, their
        /// synchronisation is kept (it is by player).
        /// </summary>
        public void ScreenOrderChanged(List<AbstractScreen> screenList)
        {
            if (!active)
                return;

            List<PlayerScreen> order = screenList.OfType<PlayerScreen>().ToList();
            if (order.Count != players.Count || order.Any(p => !players.Contains(p)))
                return;

            players.Clear();
            players.AddRange(order);
            List<PlayerScreen> syncedInOrder = order.Where(p => synced.Contains(p)).ToList();
            synced.Clear();
            synced.AddRange(syncedInOrder);
            UpdateHairLines();
        }

        public void SwapSync()
        {
            if (!synching || players.Count != 2)
                return;

            players.Reverse();
            synced.Reverse();

            UpdateHairLines();
        }
        public void CommitLaunchSettings()
        {
            if (!active)
                return;

            List<PlayerScreen> loaded = players.Where(p => p.Full).ToList();
            if (loaded.Count < 2)
                return;

            synced = loaded;
            synching = true;
            foreach (PlayerScreen player in synced)
                player.Synched = true;

            InitializeSync();

            // Handle dual replay.
            // The screens know they are in the context of a dual replay and have ignored the auto-replay flag,
            // to avoid one video starting before the other. They wait to be started by the dual playback.
            if (synced.Any(p => !p.IsReplayWatcher || !File.Exists(p.FilePath)))
                return;

            // Check that the creation dates are close enough to be considered part of the same recording.
            List<DateTime> creations = synced.Select(p => File.GetCreationTime(p.FilePath)).ToList();
            double span = (creations.Max() - creations.Min()).TotalSeconds;
            if (span < spanDualRecording)
            {
                resyncOperations = 0;
                view.Play();
                dynamicSynching = true;
                EnsureAllPlaying();
            }
            else
            {
                log.DebugFormat("Dual replay detected but the videos are not considered part of the same recording. {0}s offset", span);
            }
        }
        #endregion

        #region Players event handlers
        private void Player_PauseAsked(object sender, EventArgs e)
        {
            if (active && synching && view.Playing)
                Pause();
        }

        private void Player_PlayStarted(object sender, EventArgs e)
        {
            // If all the players are playing, we must activate the dynamic synching, even if they were started independently. 
            // This way they will continue playing on the next loop, this time in sync.
            if (!active || !synching)
                return;

            PlayerScreen player = sender as PlayerScreen;
            if (player == null || !synced.Contains(player))
                return;

            if (synced.All(p => p.IsPlaying) && !view.Playing)
            {
                // Immediately force synchronization.
                // This may not fully register for automatically started players, see `resyncOperations`.
                commonTimeline.Initialize(synced);
                view.UpdateSyncPosition(commonTimeline.GetCommonTime(synced[0], synced[0].LocalTimeOriginPhysical));

                AlignPlayers(false);

                resyncOperations = 0;
                view.Play();
                dynamicSynching = true;
                EnsureAllPlaying();
            }
        }

        private void Player_SpeedChanged(object sender, EventArgs e)
        {
            if (!active || !synching)
                return;

            PlayerScreen player = sender as PlayerScreen;
            if (player == null || !synced.Contains(player) || !PreferencesManager.PlayerPreferences.SyncLockSpeed)
                return;

            foreach (PlayerScreen other in synced)
            {
                if (other != player)
                    other.RealtimePercentage = player.RealtimePercentage;
            }
        }

        private void Player_TimeOriginChanged(object sender, EventArgs e)
        {
            if (!active || !synching)
                return;

            PlayerScreen player = sender as PlayerScreen;
            if (player == null || !synced.Contains(player))
                return;

            // Reinit synchronization.
            commonTimeline.Initialize(synced);
            currentTime = commonTimeline.GetCommonTime(player, player.LocalTime);
            view.SetupTrkFrame(0, commonTimeline.LastTime, currentTime);
            view.UpdateSyncPosition(currentTime);
            UpdateHairLines();
        }

        private void Player_HighSpeedFactorChanged(object sender, EventArgs e)
        {
            if (!active || !synching)
                return;

            if (PreferencesManager.PlayerPreferences.SyncLockSpeed)
                LockSpeed();

            // Synchronization must be reinitialized.
            commonTimeline.Initialize(synced);

            // TODO: Check if current time is still in bounds.
            currentTime = Math.Min(currentTime, commonTimeline.GetCommonTime(synced[0], synced[0].LocalTime));

            view.SetupTrkFrame(0, commonTimeline.LastTime, currentTime);
            view.UpdateSyncPosition(currentTime); 
        }

        private void Player_ImageChanged(object sender, EventArgs<Bitmap> e)
        {
            if (!active || !synching)
                return;

            PlayerScreen player = sender as PlayerScreen;
            if (player == null || !synced.Contains(player))
                return;

            if (dynamicSynching)
            {
                if (player.IsPlaying)
                {
                    //log.DebugFormat("Received image from [{0}] ({1}).", GetPlayerIndex(player), player.LocalTime / 1000);
                    currentTime = commonTimeline.GetCommonTime(player, player.LocalTime);

                    if (resyncOperations < maxResyncOperations)
                    {
                        //----------------------------------------------------------------------------------
                        // Test for desync. 
                        // This is not the primary synchronization mechanism, videos should synchronize naturally from being started
                        // at the right time, based on their time origin and the fact that their playback rate relative to real time should match.
                        //
                        // However, for videos started automatically, we can't guarantee that one isn't ahead of the other.
                        // We allow ourselves one attempt at resync here.
                        // This kind of resync can easily misfire and cause judder so it's only used very sparingly.
                        //----------------------------------------------------------------------------------
                        foreach (PlayerScreen otherPlayer in synced)
                        {
                            if (otherPlayer == player || !otherPlayer.IsPlaying)
                                continue;

                            long otherTime = commonTimeline.GetCommonTime(otherPlayer, otherPlayer.LocalTime);
                            long divergence = Math.Abs(currentTime - otherTime);
                            long frameTime = Math.Max(player.LocalFrameTime, otherPlayer.LocalFrameTime);
                            if (divergence > frameTime)
                            {
                                resyncOperations++;

                                log.WarnFormat("Synchronization divergence: [{0}]@{1} vs [{2}]@{3}. Resynchronizing.", 
                                    GetPlayerIndex(player), currentTime / 1000, GetPlayerIndex(otherPlayer), otherTime / 1000);

                                AlignPlayers(true);
                                break;
                            }
                        }
                    }

                    EnsureAllPlaying();
                }
                else if (synced.All(p => !p.IsPlaying))
                {
                    // All the players have completed a loop and are waiting.
                    currentTime = 0;
                    EnsureAllPlaying();
                }
            }

            UpdateHairLines();
            
            if (!view.Merging || e.Value == null || synced.Count != 2)
                return;

            PlayerScreen mergeTarget = player == synced[0] ? synced[1] : synced[0];
            mergeTarget.SetSyncMergeImage(e.Value, !dualSaveInProgress);
        }
        #endregion

        #region View event handlers
        private void CCtrl_PlayToggled(object sender, EventArgs e)
        {
            if (synching)
            {
                AlignPlayers(false);

                dynamicSynching = view.Playing;
                if (dynamicSynching)
                {
                    resyncOperations = 0;
                    EnsureAllPlaying();
                }
            }

            // Propagate the stop call to screens.
            if (!view.Playing)
                Pause();
        }
        private void CCtrl_GotoFirst(object sender, EventArgs e)
        {
            Pause();

            if (synching)
            {
                currentTime = 0;
                GotoTime(currentTime, true);
                UpdateTrkFrame(currentTime);
            }
            else
            {
                foreach (PlayerScreen player in players)
                    player.view.buttonGotoFirst_Click(this, EventArgs.Empty);
            }
        }
        private void CCtrl_GotoPrev(object sender, EventArgs e)
        {
            Pause();

            if (synching)
            {
                if (currentTime > 0)
                {
                    currentTime -= commonTimeline.FrameTime;

                    GotoTime(currentTime, true);
                    UpdateTrkFrame(currentTime);
                }
            }
            else
            {
                foreach (PlayerScreen screen in players)
                    screen.view.buttonGotoPrevious_Click(this, EventArgs.Empty);
            }
        }
        private void CCtrl_GotoNext(object sender, EventArgs e)
        {
            Pause();

            if (synching)
            {
                if (currentTime < commonTimeline.LastTime)
                {
                    currentTime += commonTimeline.FrameTime;
                    
                    GotoTime(currentTime, true);
                    UpdateTrkFrame(currentTime);
                }
            }
            else
            {
                foreach (PlayerScreen player in players)
                    player.view.buttonGotoNext_Click(this, EventArgs.Empty);
            }
        }
        private void CCtrl_GotoLast(object sender, EventArgs e)
        {
            Pause();

            if (synching)
            {
                currentTime = commonTimeline.LastTime;
                GotoTime(currentTime, true);
                UpdateTrkFrame(currentTime);
            }
            else
            {
                foreach (PlayerScreen player in players)
                    player.view.buttonGotoLast_Click(this, EventArgs.Empty);
            }
        }
        private void CCtrl_SyncAsked(object sender, EventArgs e)
        {
            if (!synching)
                return;

            SetSyncPoint(false);
            GotoTime(currentTime, true);
        }
        private void CCtrl_MergeAsked(object sender, EventArgs e)
        {
            if (!synching || synced.Count != 2)
                return;

            log.Debug(String.Format("SyncMerge videos is now {0}", view.Merging.ToString()));

            // This will also do a full refresh, and trigger back Player_ImageChanged().
            foreach (PlayerScreen player in synced)
                player.SyncMerge = view.Merging;
        }
        private void CCtrl_PositionChanged(object sender, TimeEventArgs e)
        {
            if (!synching)
                return;

            Pause();
            
            currentTime = e.Time;
            GotoTime(currentTime, true);
        }
        
        private void CCtrl_GotoPrevKeyframe(object sender, EventArgs e)
        {
            Pause();

            if (!synching)
                return;

            foreach (PlayerScreen player in synced)
                player.GotoPrevKeyframe();
        }
        private void CCtrl_GotoNextKeyframe(object sender, EventArgs e)
        {
            Pause();

            if (!synching)
                return;

            foreach (PlayerScreen player in synced)
                player.GotoNextKeyframe();
        }
        private void CCtrl_AddKeyframe(object sender, EventArgs e)
        {
            Pause();

            if (!synching)
                return;

            foreach (PlayerScreen player in synced)
                player.AddKeyframe();
        }
        private void CCtrl_GotoSync(object sender, EventArgs e)
        {
            Pause();

            if (!synching)
                return;
            
            currentTime = commonTimeline.GetCommonTime(synced[0], synced[0].LocalTimeOriginPhysical);
            GotoTime(currentTime, true);
            UpdateTrkFrame(currentTime);
        }
        
        #endregion

        #region Entering/Exiting dual player management
        private void Enter(List<AbstractScreen> screenList)
        {
            Exit();

            players.Clear();
            foreach (AbstractScreen screen in screenList)
                players.Add((PlayerScreen)screen);

            foreach (PlayerScreen player in players)
                AddEventHandlers(player);

            active = true;
        }
        private void Exit()
        {
            synching = false;
            dynamicSynching = false;

            if (active)
            {
                foreach (PlayerScreen player in players)
                    RemoveEventHandlers(player);

                players.Clear();
            }

            synced.Clear();
            active = false;
        }
        private void AddEventHandlers(PlayerScreen player)
        {
            player.PlayStarted += Player_PlayStarted;
            player.PauseAsked += Player_PauseAsked;
            player.SpeedChanged += Player_SpeedChanged;
            player.HighSpeedFactorChanged += Player_HighSpeedFactorChanged;
            player.TimeOriginChanged += Player_TimeOriginChanged;
            player.ImageChanged += Player_ImageChanged;
        }
        private void RemoveEventHandlers(PlayerScreen player)
        {
            player.PlayStarted -= Player_PlayStarted;
            player.PauseAsked -= Player_PauseAsked;
            player.SpeedChanged -= Player_SpeedChanged;
            player.HighSpeedFactorChanged -= Player_HighSpeedFactorChanged;
            player.TimeOriginChanged -= Player_TimeOriginChanged;
            player.ImageChanged -= Player_ImageChanged;
        }
        #endregion

        public void ExecuteDualCommand(HotkeyCommand playerCommand)
        {
            // A player has detected that a hotkey it received should actually be handled at the dual player level.
            // At that point there is still two options, either it's a true dual player command,
            // something normally bound to controls in the common controls,
            // or it's a multiplexed command, a command that should simply be forwarded to each player.

            HotkeyCommand dualCommand = hotkeys.FirstOrDefault(hk => hk != null && hk.KeyData == playerCommand.KeyData);
            if (dualCommand == null)
                return;

            DualPlayerCommands command = (DualPlayerCommands)dualCommand.CommandCode;

            switch(command)
            {
                case DualPlayerCommands.GotoPreviousKeyframe:
                case DualPlayerCommands.GotoNextKeyframe:
                case DualPlayerCommands.GotoSyncPoint:
                case DualPlayerCommands.AddKeyframe:
                    foreach (PlayerScreen player in (synching ? synced : players))
                        player.ExecuteScreenCommand(playerCommand.CommandCode);
                    break;

                default:
                    view.ExecuteDualCommand(dualCommand.CommandCode);
                    break;
            }
        }


        #region Synchronization
        public void ResetSync()
        {
            if (!active)
                return;
            
            log.DebugFormat("Re-initializing dual player synchronization");

            synching = false;
            dynamicSynching = false;
            Pause();

            // Synchronize the screens that have a video (at least two).
            List<PlayerScreen> loaded = players.Where(p => p.Full).ToList();
            if (loaded.Count < 2)
                return;

            synced = loaded;
            synching = true;
            foreach (PlayerScreen player in synced)
                player.Synched = true;

            if (PreferencesManager.PlayerPreferences.SyncLockSpeed)
                LockSpeed();

            InitializeSync();

            foreach (PlayerScreen player in synced)
                player.SyncMerge = false;
            
            StopMerge();

            GotoTime(currentTime, true);
        }

        private void LockSpeed()
        {
            double percentage = synced.Min(p => p.RealtimePercentage);
            foreach (PlayerScreen player in synced)
                player.RealtimePercentage = percentage;
        }

        private void InitializeSync()
        {
            if (synced.Count < 2)
                return;

            commonTimeline.Initialize(synced);
            currentTime = 0;
            view.SetupTrkFrame(0, commonTimeline.LastTime, currentTime);
            view.UpdateSyncPosition(commonTimeline.GetCommonTime(synced[0], synced[0].LocalTimeOriginPhysical));
            UpdateHairLines();

            log.Debug("Synchronization initialized.");
        }

        private void SetSyncPoint(bool intervalOnly)
        {
            log.DebugFormat("Resetting time origins. {0}", string.Join(", ", synced.Select((p, i) => string.Format("[{0}]:{1}", i, p.LocalTime))));
            foreach (PlayerScreen player in synced)
                player.LocalTimeOriginPhysical = player.LocalTime;

            commonTimeline.Initialize(synced);
            currentTime = commonTimeline.GetCommonTime(synced[0], synced[0].LocalTime);
            
            view.SetupTrkFrame(0, commonTimeline.LastTime, currentTime);
            view.UpdateSyncPosition(currentTime); 
        }

        /// <summary>
        /// (Swim 2.14.1) The time origins of some screens set at once (the same moment in every video, found from their sound; local real
        /// time in microseconds as LocalTimeOriginPhysical), then the synchronisation set up on them as with the sync button: every screen
        /// goes to the moment the given one shows.
        /// </summary>
        public void SyncAt(IDictionary<PlayerScreen, long> origins, PlayerScreen shown)
        {
            if (!active || origins == null || origins.Count == 0)
                return;

            if (!synching)
                ResetSync();
            if (!synching)
                return;

            Pause();
            foreach (PlayerScreen player in synced)
            {
                long origin;
                if (origins.TryGetValue(player, out origin))
                    player.LocalTimeOriginPhysical = origin;
            }

            commonTimeline.Initialize(synced);
            PlayerScreen at = shown != null && synced.Contains(shown) ? shown : synced[0];
            currentTime = commonTimeline.GetCommonTime(at, at.LocalTime);
            view.SetupTrkFrame(0, commonTimeline.LastTime, currentTime);
            view.UpdateSyncPosition(currentTime);
            GotoTime(currentTime, true);
            UpdateTrkFrame(currentTime);
        }

        private void GotoTime(long commonTime, bool allowUIUpdate)
        {
            foreach (PlayerScreen player in synced)
                GotoTime(player, commonTime, allowUIUpdate);

            UpdateHairLines();
        }
        
        private void GotoTime(PlayerScreen player, long commonTime, bool allowUIUpdate)
        {
            long localTime = commonTimeline.GetLocalTime(player, commonTime);
            localTime = Math.Max(0, localTime);

            if (player.LocalTime != localTime)
                player.GotoTime(localTime, allowUIUpdate);
        }

        private void UpdateHairLines()
        {
            if (synced.Count < 2)
                return;

            // Two screens: one marker per screen. More screens: the earliest and the latest position.
            List<long> times = synced.Select(p => commonTimeline.GetCommonTime(p, p.LocalTime)).ToList();
            long leftTime = synced.Count == 2 ? times[0] : times.Min();
            long rightTime = synced.Count == 2 ? times[1] : times.Max();

            view.UpdateHairline(leftTime, true);
            view.UpdateHairline(rightTime, false);
        }
        
        /// <summary>
        /// Force all players to align on a common time.
        /// Used if players may have moved independently from the common tracker.
        /// Should not be used while playback is active.
        /// </summary>
        private void AlignPlayers(bool catchup)
        {
            if (synced.Count < 2)
                return;

            List<long> times = synced.Select(p => commonTimeline.GetCommonTime(p, p.LocalTime)).ToList();
            currentTime = catchup ? times.Max() : times.Min();

            log.DebugFormat("Aligning players to {0}.", currentTime / 1000);
            GotoTime(currentTime, true);
        }

        private void EnsureAllPlaying()
        {
            foreach (PlayerScreen player in synced)
                EnsurePlaying(player);
        }

        /// <summary>
        /// Make sure a player is playing if it needs to but does not start it if it shouldn't.
        /// </summary>
        private void EnsurePlaying(PlayerScreen player)
        {
            if (!player.IsPlaying && !commonTimeline.IsOutOfBounds(player, currentTime))
                player.EnsurePlaying();
        }

        #endregion

        private int GetPlayerIndex(PlayerScreen player)
        {
            return players.IndexOf(player);
        }
    }
}
