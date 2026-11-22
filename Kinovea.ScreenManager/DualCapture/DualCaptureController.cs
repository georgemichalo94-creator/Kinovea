using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    public class DualCaptureController
    {
        #region Properties
        public CommonControlsCapture View
        {
            get { return view; }
        }
        public bool Active
        {
            get { return active; }
        }
        #endregion

        #region Members
        bool active;
        private CommonControlsCapture view = new CommonControlsCapture();
        private List<CaptureScreen> screens = new List<CaptureScreen>();

        // (Swim 2.27.0) The menu of the capture folder of all the cameras (its items are made again each time it opens).
        private ContextMenuStrip folderMenu = new ContextMenuStrip();

        // (Swim 2.28.0) The automatic stop of the recording of an attempt after its last line.
        private Timer swimAutoStop = new Timer { Interval = 200 };
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        
        #endregion

        #region Constructor
        public DualCaptureController()
        {
            view.Dock = DockStyle.Fill;

            view.GrabbingChanged += CCtrl_GrabbingChanged;
            view.SnapshotAsked += CCtrl_SnapshotAsked;
            view.RecordingChanged += CCtrl_RecordingChanged;

            // (Swim 2.27.0) The capture folder of all the cameras, in the common row.
            view.FolderAsked += CCtrl_FolderAsked;
            view.SetFolderProvider(SwimFolderText, SwimFolderMissing);
            view.SignalAsked += CCtrl_SignalAsked;

            // (Swim 2.28.0) The attempts measured live: the report after an attempt and the automatic stop after the last line.
            view.ReportAsked += (s, e) => SwimLiveAttempts.ShowReport(!SwimLiveAttempts.Report);
            view.AutoStopAsked += (s, e) =>
            {
                SwimLiveAttempts.AutoStop = !SwimLiveAttempts.AutoStop;
                view.SetAutoStop(SwimLiveAttempts.AutoStop);
                log.DebugFormat("Automatic stop after the last line: {0}.", SwimLiveAttempts.AutoStop ? "on" : "off");
            };
            view.SetAutoStop(SwimLiveAttempts.AutoStop);
            SwimLiveAttempts.Changed += () => view.SetReport(SwimLiveAttempts.List.Count > 0, SwimLiveAttempts.Report);
            swimAutoStop.Tick += SwimAutoStop_Tick;
        }
        #endregion

        #region Public methods
        public void ScreenListChanged(List<AbstractScreen> screenList)
        {
            // (Swim 2.20.0) Two capture screens or more (up to five in the grid): the common controls drive all of them.
            if (screenList.Count >= 2 && screenList.All(s => s is CaptureScreen))
                Enter(screenList);
            else
                Exit();
        }
        public void RefreshUICulture()
        {
            view.RefreshUICulture();
        }
        public void ExecuteDualCommand(HotkeyCommand cmd)
        {
            view.ExecuteDualCommand(cmd.CommandCode);
        }
        #endregion

        #region Screens event handlers
        private void Screen_RecordingStarted(object sender, EventArgs e)
        {
            view.UpdateRecordingStatus(true);
        }
        private void Screen_RecordingStopped(object sender, EventArgs e)
        {
            if (screens.All(s => !s.Recording))
            {
                view.UpdateRecordingStatus(false);
                SwimEndAttempt();
            }
        }
        #endregion

        #region Entering/Exiting dual capture management
        private void Enter(List<AbstractScreen> screenList)
        {
            Exit();

            screens.Clear();
            screens.AddRange(screenList.Cast<CaptureScreen>());

            foreach (CaptureScreen screen in screens)
            {
                AddEventHandlers(screen);
                screen.Synched = true;
            }

            active = true;
        }
        private void Exit()
        {
            if (active)
            {
                foreach (CaptureScreen screen in screens)
                {
                    RemoveEventHandlers(screen);
                    screen.Synched = false;
                }

                screens.Clear();
            }

            active = false;
        }
        private void AddEventHandlers(CaptureScreen screen)
        {
            screen.RecordingStarted += Screen_RecordingStarted;
            screen.RecordingStopped += Screen_RecordingStopped;
        }
        private void RemoveEventHandlers(CaptureScreen screen)
        {
            screen.RecordingStarted -= Screen_RecordingStarted;
            screen.RecordingStopped -= Screen_RecordingStopped;
        }
        #endregion

        #region View event handlers
        private void CCtrl_GrabbingChanged(object sender, EventArgs<bool> e)
        {
            foreach (CaptureScreen screen in screens)
                screen.ForceGrabbingStatus(e.Value);
        }
        private void CCtrl_SnapshotAsked(object sender, EventArgs e)
        {
            foreach (CaptureScreen screen in screens)
                screen.PerformSnapshot();
        }
        private void CCtrl_RecordingChanged(object sender, EventArgs<bool> e)
        {
            // (Swim 2.27.0) Before a start: cameras that write on the computer without a capture folder stop it, with one message that
            // points to the folder button of the common row (on the wall the row of each screen is hidden).
            if (e.Value)
            {
                List<CaptureScreen> without = screens.Where(s => s.SwimNeedsCaptureFolder).ToList();
                if (without.Count > 0)
                {
                    log.DebugFormat("Recording of all the cameras not started: {0} without a capture folder.", without.Count);
                    view.UpdateRecordingStatus(false);
                    view.RefreshFolder();
                    SwimCaptureFolders.AlertNoFolder(without.Select(s => s.CameraAlias).ToList());
                    return;
                }
            }

            // (Swim 2.20.0) One moment for all the screens: the cameras that record on their own card (GoPro) get their command at
            // the same time and measure their start from it.
            DateTime commandTime = DateTime.Now;
            foreach (CaptureScreen screen in screens)
                screen.ForceRecordingStatus(e.Value, commandTime);

            // (Swim 2.27.0) The common button shows a recording only while a screen records.
            if (e.Value && screens.All(s => !s.Recording))
                view.UpdateRecordingStatus(false);

            // (Swim 2.28.0) The stop of the cameras ends the attempt going on.
            if (!e.Value && screens.All(s => !s.Recording))
                SwimEndAttempt();
        }

        #region Swim: the start signal of the common row (2.27.0)
        /// <summary>
        /// The start signal: the cameras record (started now when they do not), «Take your marks», 1 s, the horn. The horn is the zero
        /// of the time of the attempt: in the companion KVA of each video (its time origin) and in the live panel. The cameras that
        /// record on their own card (GoPro) keep it with their recording, as their own button does.
        /// </summary>
        private void CCtrl_SignalAsked(object sender, EventArgs e)
        {
            if (SwimStartSignal.Playing)
                return;

            if (!screens.Any(s => s.Recording))
            {
                view.SwimRecordAll();
                if (!screens.Any(s => s.Recording))
                {
                    log.DebugFormat("Start signal not played: no camera records.");
                    return;
                }
            }

            DateTime asked = DateTime.Now;
            DateTime horn = SwimStartSignal.Play();
            if (horn == DateTime.MinValue)
                return;

            SwimCaptureWall.SignalHorn = horn;
            foreach (CaptureScreen screen in screens.Where(s => s.Recording))
                screen.SwimAddHorn(horn);

            if (screens.Any(s => s.Recording && s.RecordsOnCamera))
                SwimGoProTakes.AddSignal(horn);

            // (Swim 2.28.0) A new attempt from this horn: the lines of the cameras watch for the swimmer again.
            SwimLiveAttempts.Begin(horn);
            foreach (CaptureScreen screen in screens)
                screen.SwimLineReset();

            swimAutoStop.Start();

            log.DebugFormat("Start signal of the common row: the horn in {0:0.00} s, {1} cameras recording.", (horn - asked).TotalSeconds, screens.Count(s => s.Recording));
            view.RefreshFolder();
        }
        #endregion

        #region Swim: the attempts measured live (2.28.0)
        /// <summary>The recording of the attempt going on stopped: what each camera saw is kept, and the report is asked for.</summary>
        private void SwimEndAttempt()
        {
            swimAutoStop.Stop();
            if (SwimLiveAttempts.Current == null)
                return;

            try
            {
                List<SwimLiveTrack> tracks = screens.Select(s => s.SwimTrack()).Where(t => t != null).ToList();
                SwimLiveAttempt attempt = SwimLiveAttempts.End(tracks);
                if (attempt != null)
                {
                    log.DebugFormat("Attempt {0} ended: {1} cameras with a place, lines seen {2}/{3}, {4} places of the head.", attempt.Number, tracks.Count,
                        tracks.Count(t => SwimWallPlaces.Under(t.Distance) && !double.IsNaN(t.Crossed)), tracks.Count(t => SwimWallPlaces.Under(t.Distance)),
                        tracks.Sum(t => t.Samples.Count));
                }
            }
            catch (Exception e)
            {
                log.Error("Error while keeping the attempt.", e);
            }
        }

        /// <summary>
        /// The recording of the attempt stops by itself AutoStopAfter seconds after the time of its farthest line, else AutoStopLatest
        /// seconds after its horn.
        /// </summary>
        private void SwimAutoStop_Tick(object sender, EventArgs e)
        {
            SwimLiveAttempt attempt = SwimLiveAttempts.Current;
            if (attempt == null || !screens.Any(s => s.Recording))
            {
                swimAutoStop.Stop();
                return;
            }

            if (!SwimLiveAttempts.AutoStop)
                return;

            double since = (DateTime.Now - attempt.Horn).TotalSeconds;
            List<SwimWallCamera> lines = screens.Select(s => s.SwimWallState()).Where(c => SwimWallPlaces.Under(c.Distance)).ToList();
            double farthest = lines.Count > 0 ? lines.Max(c => c.Distance) : double.NaN;
            double crossed = lines.Where(c => Math.Abs(c.Distance - farthest) < 0.01).Select(c => c.Crossed).Where(t => !double.IsNaN(t)).DefaultIfEmpty(double.NaN).Min();
            bool after = !double.IsNaN(crossed) && since >= crossed + SwimLiveAttempts.AutoStopAfter;
            if (after || since >= SwimLiveAttempts.AutoStopLatest)
            {
                swimAutoStop.Stop();
                log.DebugFormat("Automatic stop {0:0.0} s after the horn ({1}).", since, after ? "after the last line" : "the last line not seen");
                view.SwimStopAll();
            }
        }
        #endregion

        #region Swim: the capture folder of all the cameras (2.27.0)
        private List<CaptureScreen> SwimWritingScreens()
        {
            return screens.Where(s => !s.RecordsOnCamera).ToList();
        }

        private string SwimFolderText()
        {
            return SwimCaptureFolders.Text(SwimWritingScreens().Select(s => s.SwimCaptureFolderId).ToList());
        }

        private bool SwimFolderMissing()
        {
            return SwimWritingScreens().Any(s => s.SwimCaptureFolderId == Guid.Empty);
        }

        private void CCtrl_FolderAsked(object sender, EventArgs e)
        {
            List<Guid> ids = SwimWritingScreens().Select(s => s.SwimCaptureFolderId).Distinct().ToList();
            Guid current = ids.Count == 1 ? ids[0] : Guid.Empty;
            SwimCaptureFolders.Fill(folderMenu, current, SwimApplyFolder, () =>
            {
                if (screens.Count > 0)
                    screens[0].View_OpenPreferences(PreferenceTab.Capture_Paths);

                view.RefreshFolder();
            });

            view.ShowFolderMenu(folderMenu);
        }

        /// <summary>
        /// The capture folder for all the cameras that write on the computer; those that now share a folder and a file name pattern
        /// get the name of their camera at the end of it, so that the files of one start are never the same.
        /// </summary>
        private void SwimApplyFolder(CaptureFolder folder)
        {
            if (folder == null)
                return;

            List<CaptureScreen> writing = SwimWritingScreens();
            foreach (CaptureScreen screen in writing)
                screen.SwimSetCaptureFolder(folder.Id);

            string[] patterns = SwimCaptureFolders.DistinctPatterns(writing.Select(s => s.SwimFilenamePattern).ToList(), writing.Select(s => s.SwimCaptureFolderId).ToList());
            int renamed = 0;
            for (int i = 0; i < writing.Count && i < patterns.Length; i++)
            {
                if (patterns[i] == writing[i].SwimFilenamePattern)
                    continue;

                writing[i].SwimSetFilenamePattern(patterns[i]);
                renamed++;
            }

            log.DebugFormat("Capture folder chosen for {0} cameras ({1} file names made different).", writing.Count, renamed);
            view.RefreshFolder();
        }
        #endregion
        #endregion
        
        #region Private methods

        #endregion
    }
}
