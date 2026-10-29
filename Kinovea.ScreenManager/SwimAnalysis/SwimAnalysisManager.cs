#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Windows.Forms;
using Kinovea.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Phases of the race, in the order they happen. The panel shows one tab per phase.
    /// </summary>
    public enum SwimPhase
    {
        Start,
        Swim,
        Turn,
        Finish
    }

    /// <summary>
    /// Coordinates the swim race analysis: receives the hotkey commands from the player screen,
    /// keeps the races of the current video, saves them next to the video, computes the analysis
    /// and pushes it live to the Google Sheet.
    ///
    /// Integration points with the rest of Kinovea are intentionally few:
    /// - PlayerScreenCommands.Swim* (Commands.cs) and their default keys (HotkeySettingsManager.cs),
    /// - PlayerScreenUserInterface.ExecuteScreenCommand forwards those commands here,
    /// - Tools menu entry in ScreenManager.cs opens the panel.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static SwimAnalysisManager instance;

        public static SwimAnalysisManager Instance
        {
            get
            {
                if (instance == null)
                    instance = new SwimAnalysisManager();
                return instance;
            }
        }

        /// <summary>
        /// The manager if the swim analysis was used in this session, otherwise null (does not create it).
        /// Used by the player screen for the per-frame hooks (painting, mouse).
        /// </summary>
        public static SwimAnalysisManager Current
        {
            get { return instance; }
        }

        #region Properties
        public SwimSettings Settings { get; private set; }
        public SwimVideoFile VideoFile { get; private set; }
        /// <summary>The race of the panel (on the thread of a reading in the background: the copy it reads with).</summary>
        public SwimRace Race
        {
            get
            {
                QuietReading q = quietThread;
                return q != null ? q.Race : currentRace;
            }

            private set
            {
                currentRace = value;
            }
        }

        public SwimComputed Computed { get; private set; }

        public SwimTiming Timing
        {
            get
            {
                QuietReading q = quietThread;
                return q != null ? q.Timing : currentTiming;
            }

            private set
            {
                currentTiming = value;
            }
        }

        private SwimRace currentRace;
        private SwimTiming currentTiming;
        public SwimSyncState SyncState { get; private set; }
        public string SyncMessage { get; private set; }
        public string LastStatus { get; private set; }
        public bool LastStatusIsWarning { get; private set; }

        /// <summary>Athletes of the main Google Sheet (cached locally).</summary>
        public SwimAthleteList Athletes { get; private set; }
        public bool AthletesLoading { get; private set; }
        public string AthletesMessage { get; private set; }

        /// <summary>Elite references of the Google Sheet (cached locally) and the ones saved on this computer.</summary>
        public SwimReferenceList References { get; private set; }

        /// <summary>The finals for the comparisons (an Olympic final: its 8 swimmers), loaded from a file once.</summary>
        public SwimFinals Finals { get; private set; }
        public bool ReferencesLoading { get; private set; }
        public string ReferencesMessage { get; private set; }

        /// <summary>What the next click on the video is used for (calibration point, distance).</summary>
        public SwimPickTarget PickTarget { get; private set; }

        /// <summary>Distance waiting for a calibration before it can be measured.</summary>
        public SwimPickTarget PendingMeasure
        {
            get { return pendingMeasure; }
        }

        /// <summary>Line distances of the calibration in progress (or of the last one).</summary>
        public double CalibrationLine1 { get; private set; }
        public double CalibrationLine2 { get; private set; }

        public bool HasVideo
        {
            get { return player != null && !player.IsDisposed && frameServer != null && frameServer.Loaded; }
        }

        public string VideoName
        {
            get { return string.IsNullOrEmpty(videoPath) ? "" : Path.GetFileName(videoPath); }
        }

        /// <summary>
        /// Current position of the video in race time (seconds after the start signal), or null.
        /// </summary>
        public double? CurrentRaceTime
        {
            get
            {
                if (!HasVideo || Race == null)
                    return null;

                long origin = Race.StartTimestamp ?? frameServer.Metadata.TimeOrigin;
                return Timing.Seconds(player.CurrentTimestamp, origin);
            }
        }
        #endregion

        #region Members
        // Graphics of the video on the exported images: size of the screen (proportions), one drawing at a time
        // (the video export draws from a background thread), no graphics while a snapshot paints its frame.
        private Size lastRenderingSize;
        private static readonly object overlayLock = new object();
        private bool suppressExportOverlay;
        private PlayerScreenUserInterface player;
        private FrameServerPlayer frameServer;
        private string videoPath;
        private FormSwimAnalysis form;
        private SwimSheetClient client;

        /// <summary>Oldest Apps Script with the reports of this version (pools in yards, ΔΑΚ, comparisons, technical analysis).</summary>
        private static readonly Version minimumBackend = new Version(2, 0, 0);

        /// <summary>Oldest Apps Script that keeps the strokes confirmed or typed at each checkpoint.</summary>
        private static readonly Version typedStrokesBackend = new Version(2, 3, 0);

        /// <summary>Oldest Apps Script that takes the charts of the PDF drawn by Kinovea (the PDF of Google leaves them empty).</summary>
        private static readonly Version chartsBackend = new Version(2, 6, 0);

        /// <summary>The Apps Script of this version of the panel (shown by the test of the connection when the web app is older).</summary>
        public static Version RequiredBackend
        {
            get { return pdfChartsBackend; }
        }

        /// <summary>
        /// Oldest Apps Script whose PDF prints every chart (2.9.5: before, the charts of Google with the values written on them came out
        /// with their title only, when Kinovea's images were not used) and shows the ΔΑΚ of each 50 (no more «Μέση ταχύτητα από
        /// εκκίνηση»).
        /// </summary>
        private static readonly Version pdfChartsBackend = new Version(2, 9, 5);
        private bool pdfChartsWarned;
        private bool backendWarned;
        private bool typedStrokesWarned;
        private bool chartsWarned;
        private bool languageWarned;

        /// <summary>The race whose PDF waits for its final frame, drawn again in the language of the analysis.</summary>
        private string pdfAfterFinalFrame;

        /// <summary>Oldest Apps Script that makes the report and the PDF in English (the switch ΕΛ / EN).</summary>
        private static readonly Version languageBackend = new Version(2, 7, 7);

        /// <summary>
        /// Oldest Apps Script with the first page "At a glance" of the PDF and the times of the analyses of the sheet (other races to
        /// compare with on the video).
        /// </summary>
        private static readonly Version quickLookBackend = new Version(2, 8, 0);
        private bool quickLookWarned;

        /// <summary>(2.11.0) The short analysis of the PDF (Apps Script 2.11.0): said once when the script is older or it did not fit.</summary>
        private static readonly Version pdfPartsBackend = new Version(2, 11, 0);
        private bool pdfPartsWarned;
        private readonly HashSet<string> pdfShortFailed = new HashSet<string>();

        /// <summary>(2.12.0) The short and the full analysis in two separate files (Apps Script 2.12.0): said once when the script is older.</summary>
        private static readonly Version pdfSeparateBackend = new Version(2, 12, 0);
        private bool pdfSeparateWarned;

        /// <summary>The races whose PDF came out without the first page (said once a race).</summary>
        private readonly HashSet<string> quickLookFailed = new HashSet<string>();
        private List<string> undoStack = new List<string>();

        /// <summary>Marks replaced by a new mark (a wall or a technical distance marked again): Undo puts them back.</summary>
        private readonly Dictionary<string, List<SwimMark>> replacedBy = new Dictionary<string, List<SwimMark>>();
        private Timer saveTimer;
        private Timer sendTimer;
        private bool dirty;

        // Clicks on the video.
        private SwimPickTarget pendingMeasure = SwimPickTarget.None;
        private int pendingWall;
        private string pendingPhaseId;
        private readonly List<PointF> calibrationClicks = new List<PointF>();
        private long calibrationFrame;
        private SwimMeasurement lastMeasurement;
        #endregion

        private SwimAnalysisManager()
        {
            Settings = SwimSettings.Load();

            // The language of the settings (also when it was read before the settings folder was known).
            SwimLang.English = Settings.Language == "en";
            Athletes = SwimAthleteList.Load();
            References = SwimReferenceList.Load();
            Finals = SwimFinals.Load();
            // (2.9.8) Also a set of finals put next to the program after the first one (or a newer file of a set).
            Finals = SwimFinals.ImportNearby(Finals) ?? Finals;
            if (Settings.ReferenceByEvent == null)
                Settings.ReferenceByEvent = new Dictionary<string, string>();
            if (Settings.LaneNumbering == null)
                Settings.LaneNumbering = new Dictionary<string, string>();
            ReferencesMessage = ReferencesSummary();
            AthletesMessage = AthletesSummary();
            Timing = new SwimTiming();
            Computed = new SwimComputed();
            CalibrationLine1 = 0;
            CalibrationLine2 = 5;
            SyncState = Settings.IsConfigured ? SwimSyncState.Idle : SwimSyncState.NotConfigured;
            SyncMessage = Settings.IsConfigured ? SwimLang.T("Έτοιμο") : SwimLang.T("Δεν έχει ρυθμιστεί η σύνδεση με το Google Sheet");

            client = new SwimSheetClient();
            client.StateChanged += Client_StateChanged;
            SwimLang.Changed += SwimLang_Changed;

            // The full screen of the videos: the round button instead of the bars (SwimAnalysisManager.Dial.cs). The manager lives as
            // long as Kinovea, as its handlers.
            VideoFullScreen.ActiveChanged += VideoFullScreen_ActiveChanged;
            VideoBigScreens.ActiveChanged += VideoBigScreens_ActiveChanged;
            VideoScreenGroup.Changed += VideoScreenGroup_Changed;

            saveTimer = new Timer();
            saveTimer.Interval = 800;
            saveTimer.Tick += SaveTimer_Tick;

            // Edits of text fields are sent after a short pause instead of at every keystroke.
            sendTimer = new Timer();
            sendTimer.Interval = 1500;
            sendTimer.Tick += SendTimer_Tick;

            // (2.9.7) The learning shared with the other Kinovea of the Google Sheet.
            StartLearningSync();
        }

        #region Entry points from the player screen
        /// <summary>(2.14.1) A moment of a screen of the group that the video of the group's analysis (the lead's) does not have.</summary>
        private void NotInGroupVideo(PlayerScreenUserInterface lead)
        {
            SystemSounds.Beep.Play();
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Η στιγμή αυτή δεν υπάρχει στο βίντεο της ανάλυσης της ομάδας (οθόνη {0}): διάλεξε στο μενού Ομάδα, «Η ανάλυση της ομάδας στο βίντεο της», ένα βίντεο που έχει όλο τον αγώνα."),
                lead.SwimScreenNumber), true);
        }

        public void ShowWindow(PlayerScreenUserInterface player, FrameServerPlayer frameServer)
        {
            long unused = 0;
            ToGroup(ref player, ref frameServer, ref unused);
            Attach(player, frameServer);
            ShowAnalysis(true);
        }

        /// <summary>
        /// Handles a Swim* command from the player screen at the given video timestamp.
        /// </summary>
        public bool HandleCommand(PlayerScreenUserInterface player, FrameServerPlayer frameServer, PlayerScreenCommands command, long timestamp)
        {
            return HandleCommand(player, frameServer, command, timestamp, true);
        }

        /// <summary>
        /// A command at a timestamp of a video: from a key or a button of a screen (fromScreen: a screen of a group goes to the analysis
        /// of the group), or the analysis's own (the pilot, the start found, an accepted proposal: on the race it works on, 2.14.1).
        /// </summary>
        private bool HandleCommand(PlayerScreenUserInterface player, FrameServerPlayer frameServer, PlayerScreenCommands command, long timestamp, bool fromScreen)
        {
            // (2.14.0) A screen of a group of screens: the analysis of the group (the video of its lead) at the same moment.
            bool inLead = !fromScreen || ToGroup(ref player, ref frameServer, ref timestamp);
            Attach(player, frameServer);
            if (Race == null)
                return false;

            // (2.14.1) A moment the video of the group's analysis does not have: no mark (it would land on its first or last frame).
            if (!inLead && command != PlayerScreenCommands.SwimShowPanel && command != PlayerScreenCommands.SwimGraphics &&
                command != PlayerScreenCommands.SwimDial && command != PlayerScreenCommands.SwimUndo)
            {
                NotInGroupVideo(player);
                return true;
            }

            bool splitAdded = false;
            int undoBefore = undoStack.Count;
            string statusBefore = LastStatus;
            markTaught = false;
            switch (command)
            {
                case PlayerScreenCommands.SwimShowPanel:
                    // With the bars on the video (or the round button of the full screen) the key opens and closes the window.
                    if (PanelOpen && ((Settings.VideoBars && BarsVisible) || DialShown))
                        form.Close();
                    else
                        ShowAnalysis(true);
                    return true;
                case PlayerScreenCommands.SwimGraphics:
                    SetGraphics(!Settings.VideoGraphics);
                    return true;
                case PlayerScreenCommands.SwimDial:
                    // In full screen: the round button hidden or shown again (2.9.5).
                    ToggleDial();
                    return true;
                case PlayerScreenCommands.SwimStart:
                    if (Race.IsTraining)
                        AddSignal(timestamp);
                    else
                        SetStart(timestamp);
                    break;
                case PlayerScreenCommands.SwimSplit:
                    if (Race.IsTraining)
                    {
                        SystemSounds.Beep.Play();
                        Status(SwimLang.T("Βίντεο προπόνησης: W για την επαφή στον τοίχο (στροφή), T για τα 5/10/15 και G για νέα εκκίνηση."), true);
                        return true;
                    }

                    splitAdded = AddSplit(timestamp);
                    break;
                case PlayerScreenCommands.SwimStroke:
                    AddMark(SwimMarkType.Stroke, timestamp, SwimLang.T("Χεριά"));
                    break;
                case PlayerScreenCommands.SwimKick:
                    AddMark(SwimMarkType.Kick, timestamp, SwimLang.T("Δελφινισμός"));
                    break;
                case PlayerScreenCommands.SwimBreakout:
                    AddMark(SwimMarkType.Breakout, timestamp, SwimLang.T("Έξοδος από το νερό"));
                    break;
                case PlayerScreenCommands.SwimFeetOff:
                    AddMark(SwimMarkType.FeetOff, timestamp, SwimLang.T("Αποκόλληση ποδιών"));
                    break;
                case PlayerScreenCommands.SwimEntry:
                    AddMark(SwimMarkType.Entry, timestamp, SwimLang.T("Είσοδος χεριών στο νερό"));
                    break;
                case PlayerScreenCommands.SwimReaction:
                    AddMark(SwimMarkType.Reaction, timestamp, SwimLang.T("Αντίδραση"));
                    break;
                case PlayerScreenCommands.SwimUndo:
                    Undo();
                    break;
                default:
                    if (!HandleTechCommand(command, timestamp))
                        return false;
                    break;
            }

            // The mark taught the learning (manual analysis): said after its message.
            if (markTaught)
            {
                markTaught = false;
                if (!string.IsNullOrEmpty(LastStatus) && !ReferenceEquals(LastStatus, statusBefore))
                    Status(LastStatus + "  🎓", LastStatusIsWarning);
            }

            ShowAnalysis(false);
            if (command != PlayerScreenCommands.SwimSnapshot && command != PlayerScreenCommands.SwimAngle)
                Changed(true);

            // Technical analysis: the frame of the event just marked is kept as a snapshot.
            if ((Race.IsTraining || TechnicalView) && command != PlayerScreenCommands.SwimUndo && command != PlayerScreenCommands.SwimSnapshot &&
                command != PlayerScreenCommands.SwimAngle)
                AutoSnapshots(command, timestamp, undoStack.Count > undoBefore ? undoStack[undoStack.Count - 1] : null, !ReferenceEquals(LastStatus, statusBefore));

            // The finish has just been marked: the frame with the graphics (final time, velocity chart) for the report.
            if (command == PlayerScreenCommands.SwimSplit && splitAdded && !Race.IsTraining)
            {
                SwimMark finish = Race.GetSplit(Race.Distance);
                if (finish != null && SameFrame(finish.Timestamp, timestamp))
                    CaptureFinalFrame(timestamp);
            }

            // The panel follows the race: start, swimming, turn, finish (or the start and turn pages of the technical analysis).
            if (command != PlayerScreenCommands.SwimUndo && command != PlayerScreenCommands.SwimSnapshot && command != PlayerScreenCommands.SwimAngle && form != null)
            {
                if (Race.IsTraining || TechnicalView)
                    form.FollowTechPhase(SwimTechCalculator.PhaseAt(TechPhases, timestamp));
                else
                    form.FollowPhase(PhaseAt(timestamp));
            }

            // Strokes at each checkpoint: the swimmer is chosen after the breakout, the strokes of the segment are shown at each checkpoint
            // (also when a lap is followed from the button of the panel with the setting off).
            if (!Race.IsTraining && HasVideo)
            {
                // A swimmer to follow for the label becomes the swimmer of the strokes (they keep his place too).
                if (command == PlayerScreenCommands.SwimBreakout && Settings.AutoStrokes && (!PickingStrokes || PickingTrack) && !resolvingProposal && reviewId == null)
                    BeginAutoStrokes();
                else if (command == PlayerScreenCommands.SwimSplit && splitAdded && !splitMoved && (Settings.AutoStrokes || StrokeSessionActive))
                    StrokesAfterSplit(timestamp);

                // Manual analysis: after the first checkpoint, one click on the swimmer and the race is read in the background for the learning.
                // (2.9.5: not for a checkpoint moved, splitMoved.)
                if (!TechnicalView && !resolvingProposal && reviewId == null &&
                    ((command == PlayerScreenCommands.SwimSplit && splitAdded && !splitMoved) || command == PlayerScreenCommands.SwimWallContact))
                    OfferQuietLearning();
            }

            return true;
        }

        /// <summary>
        /// Runs a player screen command (used by the panel buttons and to forward keys typed in the panel).
        /// </summary>
        public bool ExecutePlayerCommand(PlayerScreenCommands command)
        {
            return ExecutePlayerCommand((int)command);
        }

        public bool ExecutePlayerCommand(int commandCode)
        {
            if (!HasVideo)
                return false;

            return player.ExecuteScreenCommand(commandCode);
        }
        #endregion

        #region Commands
        private void SetStart(long timestamp)
        {
            long? old = Race.StartTimestamp;
            Race.StartTimestamp = timestamp;

            // The analyst put the start: it is his (one the auto pilot had put too). The pilot waiting for it goes on (below).
            if (!resolvingProposal)
                Race.PilotStart = false;
            bool pilotWaited = !resolvingProposal && PilotActive && pilotStep == PilotStep.Start;
            if (pilotWaited)
                pilotStep = PilotStep.None;

            // Other swimmers analysed on the same video share the same start signal (and whether the pilot put it).
            foreach (SwimRace other in VideoFile.Races)
            {
                if (other != Race && (other.StartTimestamp == null || other.StartTimestamp == old))
                {
                    other.StartTimestamp = timestamp;
                    other.PilotStart = Race.PilotStart;
                }
            }

            Status(old == null ? SwimLang.T("Σήμα εκκίνησης: t = 0 σε αυτό το καρέ.") : SwimLang.T("Το σήμα εκκίνησης άλλαξε. Όλοι οι χρόνοι υπολογίστηκαν ξανά."), false);
            if (old.HasValue && old.Value != timestamp)
                RecheckStartLessons();

            // The official times are after the start (since 2026-10-19): the checkpoints at them not checked yet move with it.
            if (old != timestamp)
                ReapplyOfficial();

            // The start put with its key answers the proposal of the start light near it (the other candidates go).
            if (!resolvingProposal && Race.Proposals.Any(x => x.Kind == KindRaceStart))
            {
                SwimProposal near = Race.Proposals
                    .Where(x => x.Kind == KindRaceStart && Math.Abs(Timing.Seconds(timestamp, x.Timestamp)) <= 1.0)
                    .OrderBy(x => Math.Abs(timestamp - x.Timestamp)).FirstOrDefault();
                bool reviewingStart = CurrentProposal != null && CurrentProposal.Kind == KindRaceStart;
                Race.Proposals.RemoveAll(x => x.Kind == KindRaceStart && (near == null || x.Id != near.Id));
                if (near != null)
                {
                    if (reviewingStart)
                        reviewId = near.Id;
                    ResolveProposalsByMark(SwimMarkType.Signal, timestamp);
                }
                else if (reviewingStart)
                {
                    NextProposal(timestamp);
                }
            }

            // The auto pilot waited for the start: the analyst put it, the pilot goes on with the whole race.
            if (pilotWaited)
            {
                pilotStartNote = SwimLang.T("την έβαλες εσύ");
                PilotLater(PilotRace);
            }
        }

        private bool EnsureStart()
        {
            if (Race.StartTimestamp != null)
                return true;

            Race.StartTimestamp = frameServer.Metadata.TimeOrigin;
            Status(SwimLang.T("Δεν είχε οριστεί σήμα εκκίνησης: χρησιμοποιήθηκε το σημείο μηδέν του Kinovea. Πάτησε Εκκίνηση στο καρέ του σήματος για να το αλλάξεις."), true);
            return false;
        }

        /// <summary>The next checkpoint at this frame. Returns false if nothing was marked.</summary>
        private bool AddSplit(long timestamp)
        {
            // Two presses on the same frame (paused video, key held down) are the same checkpoint, not two.
            SwimMark same = Race.Marks.FirstOrDefault(m => m.Type == SwimMarkType.Split && SameFrame(m.Timestamp, timestamp));
            if (same != null)
            {
                SystemSounds.Beep.Play();
                Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Σε αυτό το καρέ υπάρχει ήδη το πέρασμα των {0}{1}: δεν καταχωρήθηκε δεύτερο πέρασμα."),
                    same.Distance, Race.Unit), true);
                return false;
            }

            bool hadStart = EnsureStart();

            // (2.9.5) The checkpoint of this frame: the next one, or one with a time already that moves here (SplitTargetAt).
            bool move;
            int distance = TakeSplitTarget(timestamp, out move);
            string unit = Race.Unit;
            if (distance < 0)
            {
                SystemSounds.Beep.Play();
                Status(SwimLang.T("Όλα τα σημεία ελέγχου έχουν χρόνο. Για να αλλάξεις έναν: κλικ στη γραμμή του (το βίντεο πάει εκεί), το σωστό καρέ με ← → και «Πέρασμα» (S)."), true);
                return false;
            }

            if (move)
                return MoveSplit(distance, timestamp);

            SwimMark mark = new SwimMark { Type = SwimMarkType.Split, Timestamp = timestamp, Distance = distance };
            Race.Marks.Add(mark);
            undoStack.Add(mark.Id);

            // A proposal of this checkpoint near this frame is answered by the mark (learning).
            ResolveProposalsByMark(SwimMarkType.Split, timestamp);

            double t = Timing.Seconds(timestamp, Race.StartTimestamp.Value);
            string message = string.Format(CultureInfo.InvariantCulture, SwimLang.T("Πέρασμα {0}{1}: {2:0.00} s"), distance, unit, t);

            // Sanity check against the neighbour checkpoints.
            SwimMark previous = Race.Checkpoints.Where(d => d < distance).Select(d => Race.GetSplit(d)).LastOrDefault(m => m != null);
            SwimMark next = Race.Checkpoints.Where(d => d > distance).Select(d => Race.GetSplit(d)).FirstOrDefault(m => m != null);
            bool warn = (previous != null && previous.Timestamp >= timestamp) || (next != null && next.Timestamp <= timestamp);
            if (warn)
            {
                SystemSounds.Exclamation.Play();
                message += SwimLang.T("  ⚠ ο χρόνος δεν είναι ανάμεσα στα γειτονικά σημεία.");
            }

            string estimate = ReestimateAfterSplit(distance);
            if (hadStart || warn)
                Status(message + estimate, warn);

            return true;
        }

        private void AddMark(SwimMarkType type, long timestamp, string label)
        {
            // Two presses on the same frame (paused video, key repeat) are the same event, not two. In the technical analysis the key
            // pressed again on a mark takes its snapshot: no warning then.
            if (Race.Marks.Any(m => m.Type == type && SameFrame(m.Timestamp, timestamp)))
            {
                bool snapshot = (Race.IsTraining || TechnicalView) && Settings.AutoTechSnapshots;
                if (!snapshot)
                    SystemSounds.Beep.Play();
                Status(label + SwimLang.T(": υπάρχει ήδη σε αυτό το καρέ (δεν καταχωρήθηκε δεύτερη φορά)."), !snapshot);
                return;
            }

            bool hadStart = Race.IsTraining || EnsureStart();
            SwimMark mark = new SwimMark { Type = type, Timestamp = timestamp };
            Race.Marks.Add(mark);
            undoStack.Add(mark.Id);

            // A proposal of the same event near this frame is answered by this mark (learning).
            ResolveProposalsByMark(type, timestamp);

            if (Race.IsTraining)
            {
                // Times of a training video are from the origin of the attempt (signal or contact with the wall).
                List<SwimTechPhase> phases = SwimTechCalculator.Compute(Race, Computed, Timing);
                SwimTechPhase phase = SwimTechCalculator.PhaseAt(phases, timestamp);
                if (type == SwimMarkType.WallContact)
                    Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}: Στροφή {1}."), label, phases.Count(p => p.Kind == SwimPhaseKind.Turn && p.Origin <= timestamp)), false);
                else if (phase != null)
                    Status(string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.00} s ({2})", label, Timing.Seconds(timestamp, phase.Origin), phase.Title), false);
                else
                    Status(label + SwimLang.T(": πριν από την πρώτη προσπάθεια. Πάτησε το σήμα εκκίνησης (G) ή την επαφή στον τοίχο (W)."), true);
                return;
            }

            if (hadStart)
            {
                double t = Timing.Seconds(timestamp, Race.StartTimestamp.Value);
                Status(string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.00} s", label, t), false);
            }
        }

        public void Undo()
        {
            if (Race == null)
                return;

            SwimMark mark = null;
            while (mark == null && undoStack.Count > 0)
            {
                string id = undoStack[undoStack.Count - 1];
                undoStack.RemoveAt(undoStack.Count - 1);

                // A change of the official times (since 2026-10-19): the times before it, the checkpoints it moved back.
                if (UndoOfficial(id))
                    return;
                mark = Race.Marks.FirstOrDefault(m => m.Id == id);
            }

            if (mark == null)
                mark = Race.Marks.OrderBy(m => m.Timestamp).LastOrDefault();

            if (mark == null)
            {
                Status(SwimLang.T("Δεν υπάρχει κάτι για αναίρεση."), true);
                return;
            }

            Race.Marks.Remove(mark);
            UndoPilotEffects(mark);
            ForgetTaught(mark);
            List<SwimMark> replaced;
            replacedBy.TryGetValue(mark.Id, out replaced);

            // (2.9.5) A checkpoint moved and put back: the strokes of its segment stay.
            if (mark.Type == SwimMarkType.Split && (replaced == null || !replaced.Any(m => m.Type == SwimMarkType.Split && m.Distance == mark.Distance)))
                Race.StrokeCounts.Remove(mark.Distance);

            // The automatic snapshot of the event goes with it.
            UndoAutoSnapshots(mark);

            if (replaced != null)
            {
                replacedBy.Remove(mark.Id);
                Race.Marks.AddRange(replaced);
                foreach (SwimMark back in replaced)
                    LearnFromMark(back.Type, back.Timestamp);
                Status(SwimLang.T("Αναιρέθηκε: ") + MarkLabel(mark) + SwimLang.T(" (ξαναμπήκε η προηγούμενη τιμή)") + AfterSplitUndone(mark, replaced), false);
                return;
            }

            Status(SwimLang.T("Αναιρέθηκε: ") + MarkLabel(mark) + AfterSplitUndone(mark, null), false);
        }

        /// <summary>Add a mark that replaces others (Undo puts them back).</summary>
        private void AddReplacing(SwimMark mark, Predicate<SwimMark> replaces)
        {
            List<SwimMark> removed = Race.Marks.Where(m => replaces(m)).ToList();
            Race.Marks.RemoveAll(replaces);
            foreach (SwimMark old in removed)
                ForgetTaught(old);
            Race.Marks.Add(mark);
            undoStack.Add(mark.Id);
            if (removed.Count > 0)
                replacedBy[mark.Id] = removed;
        }

        private void ClearUndo()
        {
            StopPilot();
            undoStack.Clear();
            replacedBy.Clear();
            officialUndo.Clear();
            ClearWaitingFrames();
            CancelQuietReading();
        }

        public void ClearSplit(int distance)
        {
            if (Race == null)
                return;

            List<SwimMark> cleared = Race.Marks.Where(m => m.Type == SwimMarkType.Split && m.Distance == distance).ToList();
            int removed = Race.Marks.RemoveAll(m => m.Type == SwimMarkType.Split && m.Distance == distance);
            foreach (SwimMark old in cleared)
                ForgetTaught(old);
            Race.StrokeCounts.Remove(distance);
            if (removed > 0)
            {
                Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Διαγράφηκε ο χρόνος στο σημείο {0}{1} · το επόμενο Πέρασμα θα το συμπληρώσει."), distance, UnitLabel), false);
                Changed(true);
            }
        }

        public void ClearMarks(SwimMarkType type)
        {
            if (Race == null)
                return;

            List<SwimMark> cleared = Race.Marks.Where(m => m.Type == type).ToList();
            Race.Marks.RemoveAll(m => m.Type == type);
            foreach (SwimMark old in cleared)
                ForgetTaught(old);
            Changed(true);
        }

        public void Seek(int distance)
        {
            if (Race == null || !HasVideo)
                return;

            SwimMark split = Race.GetSplit(distance);
            if (split != null)
            {
                SeekNearest(split.Timestamp);

                // (2.9.5) The row chosen: «Πέρασμα» at the right frame moves this checkpoint there.
                ChooseSplit(distance);
            }
        }

        private static string MarkLabel(SwimMark mark)
        {
            switch (mark.Type)
            {
                case SwimMarkType.Split: return SwimLang.T("πέρασμα ") + mark.Distance;
                case SwimMarkType.Stroke: return SwimLang.T("χεριά");
                case SwimMarkType.Kick: return SwimLang.T("δελφινισμός");
                case SwimMarkType.Breakout: return SwimLang.T("έξοδος από το νερό");
                case SwimMarkType.FeetOff: return SwimLang.T("αποκόλληση ποδιών");
                case SwimMarkType.Entry: return SwimLang.T("είσοδος χεριών στο νερό");
                case SwimMarkType.Reaction: return SwimLang.T("αντίδραση");
                case SwimMarkType.HandsOff: return SwimLang.T("χέρια από τον βατήρα");
                case SwimMarkType.BackFoot: return SwimLang.T("πίσω πόδι");
                case SwimMarkType.TechSplit: return SwimLang.T("τεχνικό πέρασμα ") + (mark.Distance > 0 && mark.Wall > 0 ? "+" : "") + mark.Distance;
                case SwimMarkType.WallContact: return SwimLang.T("επαφή στον τοίχο");
                case SwimMarkType.RotationStart: return SwimLang.T("αρχή περιστροφής");
                case SwimMarkType.Signal: return SwimLang.T("σήμα εκκίνησης");
                default: return mark.Type.ToString();
            }
        }
        #endregion

        #region Phases
        /// <summary>
        /// Phase of the race at a video timestamp, from the splits already marked.
        /// </summary>
        public SwimPhase PhaseAt(long timestamp)
        {
            if (Race == null)
                return SwimPhase.Start;

            int last = 0;
            foreach (int d in Race.Checkpoints)
            {
                SwimMark split = Race.GetSplit(d);
                if (split != null && split.Timestamp <= timestamp + HalfFrame && d > last)
                    last = d;
            }

            if (last == 0)
                return SwimPhase.Start;
            if (last >= Race.Distance)
                return SwimPhase.Finish;
            if (SwimCalculator.IsWall(last, PoolLength))
                return SwimPhase.Turn;

            int next = Race.Checkpoints.Where(d => d > last).DefaultIfEmpty(-1).Min();
            return next == Race.Distance ? SwimPhase.Finish : SwimPhase.Swim;
        }

        /// <summary>
        /// Phase at the current video position.
        /// </summary>
        public SwimPhase CurrentPhase
        {
            get { return HasVideo ? PhaseAt(player.CurrentTimestamp) : SwimPhase.Start; }
        }

        public int PoolLength
        {
            get { return Race == null || Race.Pool <= 0 ? 50 : Race.Pool; }
        }

        /// <summary>Unit of the distances of the pool in the texts: "μ." or "yd".</summary>
        private string UnitLabel
        {
            get { return Race != null ? Race.Unit : SwimLang.T("μ."); }
        }

        /// <summary>Short unit drawn on the video: "m" or "yd".</summary>
        private string UnitShort
        {
            get { return Race != null && Race.IsYards ? "yd" : "m"; }
        }

        /// <summary>
        /// Distances of the walls where the swimmer turns.
        /// </summary>
        public List<int> TurnWalls
        {
            get
            {
                List<int> walls = new List<int>();
                if (Race == null)
                    return walls;

                for (int w = PoolLength; w < Race.Distance; w += PoolLength)
                    walls.Add(w);

                return walls;
            }
        }

        /// <summary>
        /// Last turn wall already passed at the current video position, or 0.
        /// </summary>
        public int CurrentTurnWall
        {
            get
            {
                if (!HasVideo || Race == null)
                    return 0;

                long now = player.CurrentTimestamp;
                int wall = 0;
                foreach (int w in TurnWalls)
                {
                    SwimMark split = Race.GetSplit(w);
                    if (split != null && split.Timestamp <= now + HalfFrame)
                        wall = w;
                }

                return wall;
            }
        }

        /// <summary>
        /// Position of a wall in pool coordinates: 0 for the start end, the pool length for the other end.
        /// </summary>
        public double WallX(int distance)
        {
            int pool = PoolLength;
            return ((distance / pool) % 2 == 1) ? pool : 0;
        }
        #endregion

        #region Distances on the video
        /// <summary>
        /// True if the next left click on this player screen image belongs to the swim analysis.
        /// </summary>
        public bool WantsImagePoint(PlayerScreenUserInterface screen)
        {
            return PickTarget != SwimPickTarget.None && screen == player && HasVideo && Active;
        }

        /// <summary>
        /// Start a calibration of the water surface on the current frame (4 clicks).
        /// </summary>
        public void BeginCalibration(double line1, double line2)
        {
            if (PickTarget == SwimPickTarget.Calibration)
            {
                CancelPick();
                return;
            }

            if (!HasVideo || Race == null)
            {
                Status(SwimLang.T("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής."), true);
                return;
            }

            pendingMeasure = SwimPickTarget.None;
            StartCalibration(line1, line2);
            Status(CalibrationPrompt(), false);
        }

        /// <summary>
        /// Measure a distance with a click on the video. Goes to the frame of the corresponding mark
        /// if the video is elsewhere, and asks for a calibration first if needed.
        /// </summary>
        public void BeginMeasure(SwimPickTarget target, int wallHint)
        {
            if (PickTarget == target || (PickTarget == SwimPickTarget.Calibration && pendingMeasure == target))
            {
                CancelPick();
                return;
            }

            if (!HasVideo || Race == null)
            {
                Status(SwimLang.T("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής."), true);
                return;
            }

            int wall = 0;
            bodyFirst = null;
            if (target == SwimPickTarget.BodyLength && !Race.IsTraining)
            {
                // (2.15.0) Near a turn of the race: the frame of the glide after its push-off (any turn: the length is the swimmer's).
                wall = TurnWalls.Contains(wallHint) ? wallHint : CurrentTurnWall;
                if (wall <= 0)
                    wall = TurnWalls.FirstOrDefault();
            }
            else if (target == SwimPickTarget.TurnUnderwater)
            {
                wall = TurnWalls.Contains(wallHint) && TechnicalView ? wallHint : CurrentTurnWall;
                if (wall <= 0)
                    wall = TurnWalls.Contains(wallHint) ? wallHint : TurnWalls.FirstOrDefault();

                if (wall <= 0)
                {
                    Status(SwimLang.T("Αυτός ο αγώνας δεν έχει στροφές (απόσταση ίση με το μήκος της πισίνας)."), true);
                    return;
                }
            }

            long? frame = MeasureFrame(target, wall);
            if (frame.HasValue && Math.Abs(Timing.Seconds(frame.Value, player.CurrentTimestamp)) > 0.5)
                player.ForcePosition(frame.Value, true);

            pendingMeasure = target;
            pendingWall = wall;

            if (CalibrationFor(player.CurrentTimestamp) == null)
            {
                double l1, l2;
                SuggestLines(target, wall, out l1, out l2);
                StartCalibration(l1, l2);
                string why = VideoFile.Calibrations.Count == 0 || Settings.FixedCamera
                    ? SwimLang.T("Πρώτα βαθμονόμηση της πισίνας. ")
                    : SwimLang.T("Δεν υπάρχει βαθμονόμηση σε αυτό το καρέ (κινούμενη κάμερα). ");
                Status(why + CalibrationPrompt(), false);
                return;
            }

            SetPick(target);
            Status(MeasurePrompt(target, wall), false);
        }

        /// <summary>
        /// Stop waiting for a click. Returns false if nothing was pending.
        /// </summary>
        public bool CancelPick()
        {
            if (PickTarget == SwimPickTarget.None)
                return false;

            bool pilotWaited = PilotActive && (pilotStep == PilotStep.Swimmer || pilotStep == PilotStep.Lost);
            autoPick = null;
            pendingMeasure = SwimPickTarget.None;
            bodyFirst = null;
            calibrationClicks.Clear();
            pickForTrack = false;
            pickForRace = false;
            pickForQuiet = false;
            SetPick(SwimPickTarget.None);
            Status(SwimLang.T("Ακυρώθηκε."), false);
            if (pilotWaited)
            {
                StopPilot();
                Status(SwimLang.T("✈ Ο αυτόματος πιλότος σταμάτησε. Ό,τι έβαλε μένει και περιμένει τον «Έλεγχο»."), false);
            }

            return true;
        }

        /// <summary>
        /// Click on the image of the player screen (image coordinates).
        /// </summary>
        public void ImagePointPicked(PlayerScreenUserInterface screen, PointF point, long timestamp)
        {
            if (!WantsImagePoint(screen))
                return;

            if (PickTarget == SwimPickTarget.Calibration)
            {
                CalibrationClick(point, timestamp);
                return;
            }

            if (PickTarget == SwimPickTarget.Angle)
            {
                AngleClick(point, timestamp);
                return;
            }

            if (PickTarget == SwimPickTarget.StrokeHead || PickTarget == SwimPickTarget.StrokeFeet)
            {
                AutoStrokeClick(point, timestamp);
                return;
            }

            if (PickTarget == SwimPickTarget.Skeleton)
            {
                SkeletonClick(point, timestamp);
                return;
            }

            SwimCalibration calibration = CalibrationFor(timestamp);
            if (calibration == null)
            {
                double l1, l2;
                SuggestLines(pendingMeasure, pendingWall, out l1, out l2);
                StartCalibration(l1, l2);
                Status(SwimLang.T("Το καρέ άλλαξε: χρειάζεται βαθμονόμηση σε αυτό το καρέ. ") + CalibrationPrompt(), true);
                return;
            }

            double x, y;
            if (!calibration.TryPool(point, out x, out y) || double.IsNaN(x) || double.IsInfinity(x))
            {
                SystemSounds.Exclamation.Play();
                Status(SwimLang.T("Το σημείο δεν μπορεί να μετρηθεί με αυτή τη βαθμονόμηση. Κάνε κλικ πάνω στο νερό."), true);
                return;
            }

            if (PickTarget == SwimPickTarget.BodyLength)
            {
                BodyLengthClick(point, x, y, timestamp, calibration);
                return;
            }

            SwimPickTarget target = PickTarget;
            int wall = pendingWall;
            double value = MeasuredValue(target, wall, x);
            value = Math.Round(value, 2);
            StoreMeasurement(target, wall, value);

            string label = MeasureLabel(target, wall);
            lastMeasurement = new SwimMeasurement
            {
                Timestamp = timestamp,
                Image = point,
                PoolX = x,
                PoolY = y,
                Value = value,
                Label = string.Format(CultureInfo.InvariantCulture, "{0:0.00} {1}", value, UnitShort),
                Calibration = calibration
            };

            pendingMeasure = SwimPickTarget.None;
            SetPick(SwimPickTarget.None);

            string message = string.Format(CultureInfo.InvariantCulture, "📍 {0}: {1:0.00} {2}", label, value, UnitLabel);
            bool warn = false;
            if (x < -1 || x > PoolLength + 1)
            {
                message += SwimLang.T("  ⚠ το σημείο είναι έξω από την πισίνα: έλεγξε τη βαθμονόμηση.");
                warn = true;
            }
            else if (y < -1.5 || y > calibration.LaneWidth + 1.5)
            {
                message += SwimLang.T("  ⚠ το σημείο είναι έξω από τον διάδρομο της βαθμονόμησης.");
                warn = true;
            }

            if (Settings.FixedCamera && Math.Abs(Timing.Seconds(timestamp, calibration.Timestamp)) > 0.05)
                message += string.Format(CultureInfo.InvariantCulture, SwimLang.T("  (βαθμονόμηση {0} {1} από άλλο καρέ: σωστό μόνο αν η κάμερα δεν κινήθηκε)"), calibration.Label, UnitLabel);

            if (warn)
                SystemSounds.Exclamation.Play();

            Status(message, warn);
            Changed(false);
            ScheduleSend();
            if (form != null)
                form.RefreshManualFields();
        }

        /// <summary>
        /// Delete the calibration used on the current frame (or the closest one).
        /// </summary>
        public void DeleteCalibration()
        {
            if (VideoFile == null || VideoFile.Calibrations.Count == 0)
                return;

            SwimCalibration calibration = null;
            if (HasVideo)
                calibration = NearestCalibration(player.CurrentTimestamp);
            if (calibration == null)
                calibration = VideoFile.Calibrations[VideoFile.Calibrations.Count - 1];

            VideoFile.Calibrations.Remove(calibration);
            if (lastMeasurement != null && lastMeasurement.Calibration == calibration)
                lastMeasurement = null;

            MarkDirty();
            Status(SwimLang.T("Η βαθμονόμηση ") + calibration.Label + " " + UnitLabel + SwimLang.T(" διαγράφηκε."), false);
            InvalidatePlayer();
            if (form != null)
                form.RefreshCalibration();
        }

        /// <summary>
        /// Calibration valid for this frame: the closest one with a fixed camera, otherwise one made on this frame.
        /// </summary>
        public SwimCalibration CalibrationFor(long timestamp)
        {
            SwimCalibration nearest = NearestCalibration(timestamp);
            if (nearest == null)
                return null;

            if (Settings.FixedCamera || SameFrame(nearest.Timestamp, timestamp))
                return nearest;

            return null;
        }

        /// <summary>
        /// One line description of the calibration state for the panel.
        /// </summary>
        public string CalibrationSummary()
        {
            if (VideoFile == null || !HasVideo)
                return "";

            int count = VideoFile.Calibrations.Count;
            if (PickTarget == SwimPickTarget.Calibration)
                return string.Format(SwimLang.T("Βαθμονόμηση σε εξέλιξη: σημείο {0}/4."), calibrationClicks.Count + 1);

            SwimCalibration current = CalibrationFor(player.CurrentTimestamp);
            if (current != null)
            {
                string location = SameFrame(current.Timestamp, player.CurrentTimestamp) ? SwimLang.T("σε αυτό το καρέ") : string.Format(CultureInfo.InvariantCulture, SwimLang.T("από το καρέ {0:0.00} s"), RaceSeconds(current.Timestamp));
                return string.Format(SwimLang.T("✔ Βαθμονόμηση {0} {2} {1}. Πάτησε «📍 Μέτρηση» δίπλα σε μια απόσταση."), current.Label, location, UnitLabel);
            }

            if (count == 0)
                return SwimLang.T("Καμία βαθμονόμηση: πάτησε «📍 Μέτρηση» (ή «Βαθμονόμηση») σε ένα σταματημένο καρέ όπου φαίνονται οι δύο γραμμές.");

            return string.Format(SwimLang.T("{0} βαθμονομήσεις σε άλλα καρέ. Με κινούμενη κάμερα χρειάζεται βαθμονόμηση σε κάθε καρέ μέτρησης."), count);
        }

        private void StartCalibration(double line1, double line2)
        {
            CalibrationLine1 = line1;
            CalibrationLine2 = line2;
            calibrationClicks.Clear();
            SetPick(SwimPickTarget.Calibration);
            if (form != null)
                form.RefreshCalibration();
        }

        private void CalibrationClick(PointF point, long timestamp)
        {
            if (calibrationClicks.Count > 0 && !SameFrame(calibrationFrame, timestamp))
            {
                calibrationClicks.Clear();
                Status(SwimLang.T("Το καρέ άλλαξε κατά τη βαθμονόμηση: ξεκινάμε ξανά από το σημείο 1 σε αυτό το καρέ."), true);
            }

            if (calibrationClicks.Count == 0)
                calibrationFrame = timestamp;

            calibrationClicks.Add(point);
            if (calibrationClicks.Count < 4)
            {
                Status(CalibrationPrompt(), false);
                InvalidatePlayer();
                if (form != null)
                    form.RefreshCalibration();
                return;
            }

            SwimCalibration calibration = new SwimCalibration();
            calibration.Timestamp = calibrationFrame;
            calibration.Line1 = CalibrationLine1;
            calibration.Line2 = CalibrationLine2;
            calibration.LaneWidth = Settings.LaneWidth;
            calibration.Points = calibrationClicks.Select(p => new SwimPoint(p)).ToList();
            string error = calibration.Validate();
            calibrationClicks.Clear();
            if (error != null)
            {
                SystemSounds.Exclamation.Play();
                Status("✖ " + error + " " + CalibrationPrompt(), true);
                InvalidatePlayer();
                if (form != null)
                    form.RefreshCalibration();
                return;
            }

            if (Settings.FixedCamera)
                VideoFile.Calibrations.Clear();
            else
                VideoFile.Calibrations.RemoveAll(c => SameFrame(c.Timestamp, calibration.Timestamp));

            VideoFile.Calibrations.Add(calibration);
            MarkDirty();

            string done = string.Format(CultureInfo.InvariantCulture, SwimLang.T("✔ Βαθμονόμηση {0} {2} στο {1:0.00} s."), calibration.Label, RaceSeconds(calibration.Timestamp), UnitLabel);
            if (pendingMeasure != SwimPickTarget.None)
            {
                SetPick(pendingMeasure);
                Status(done + " " + MeasurePrompt(pendingMeasure, pendingWall), false);
            }
            else
            {
                SetPick(SwimPickTarget.None);
                Status(done + SwimLang.T(" Τώρα πάτησε «📍 Μέτρηση» δίπλα σε μια απόσταση και κάνε κλικ στο βίντεο."), false);
            }

            if (form != null)
                form.RefreshCalibration();
        }

        private string CalibrationPrompt()
        {
            int i = calibrationClicks.Count;
            double line = i < 2 ? CalibrationLine1 : CalibrationLine2;
            string rope = i % 2 == 0 ? SwimLang.T("στο σχοινί Α (π.χ. το κοντινό στην κάμερα)") : SwimLang.T("στο σχοινί Β (το άλλο σχοινί του διαδρόμου)");
            if (i >= 2)
                rope = i % 2 == 0 ? SwimLang.T("στο σχοινί Α") : SwimLang.T("στο σχοινί Β");

            return string.Format(CultureInfo.InvariantCulture,
                SwimLang.T("📐 Κλικ {0}/4: η γραμμή των {1:0.##} {3} {2}, στην επιφάνεια του νερού. Esc: ακύρωση."), i + 1, line, rope, UnitLabel);
        }

        private string MeasurePrompt(SwimPickTarget target, int wall)
        {
            string what;
            switch (target)
            {
                case SwimPickTarget.EntryHands: what = SwimLang.T("Κάνε κλικ στο σημείο όπου τα χέρια μπαίνουν στο νερό."); break;
                case SwimPickTarget.EntryHips: what = SwimLang.T("Κάνε κλικ στο σημείο όπου τα ισχία μπαίνουν στο νερό."); break;
                case SwimPickTarget.StartUnderwater: what = SwimLang.T("Κάνε κλικ στο κεφάλι, εκεί που φτάνει στην επιφάνεια."); break;
                case SwimPickTarget.StartBreakout: what = SwimLang.T("Κάνε κλικ στο κεφάλι, εκεί που βγαίνει από το νερό."); break;
                case SwimPickTarget.TurnUnderwater: what = string.Format(CultureInfo.InvariantCulture, SwimLang.T("Στροφή {0}{1}: κάνε κλικ στο κεφάλι, εκεί που βγαίνει από το νερό."), wall, UnitLabel); break;
                case SwimPickTarget.FinishLastStroke: what = SwimLang.T("Κάνε κλικ στο σημείο εισόδου του χεριού στην τελευταία χεριά."); break;
                case SwimPickTarget.PhaseEntry: what = SwimLang.T("Κάνε κλικ στο σημείο όπου τα χέρια μπαίνουν στο νερό (απόσταση από τη γραμμή 0)."); break;
                case SwimPickTarget.PhaseBreakout: what = SwimLang.T("Κάνε κλικ στο κεφάλι εκεί που βγαίνει από το νερό (απόσταση από τη γραμμή 0)."); break;
                case SwimPickTarget.BodyLength: what = SwimLang.T("Μήκος σώματος: σε ένα καρέ της ολίσθησης (σώμα τεντωμένο), κλικ στα δάχτυλα των ποδιών και μετά στο κεφάλι."); break;
                default: what = SwimLang.T("Κάνε κλικ στο βίντεο."); break;
            }

            return "📍 " + what + SwimLang.T(" Esc: ακύρωση.");
        }

        private string MeasureLabel(SwimPickTarget target, int wall)
        {
            switch (target)
            {
                case SwimPickTarget.EntryHands: return SwimLang.T("Απόσταση εισόδου (χέρια)");
                case SwimPickTarget.EntryHips: return SwimLang.T("Απόσταση εισόδου (ισχία)");
                case SwimPickTarget.StartUnderwater: return SwimLang.T("Απόσταση κάτω από το νερό");
                case SwimPickTarget.StartBreakout: return SwimLang.T("Απόσταση εξόδου (κεφάλι)");
                case SwimPickTarget.TurnUnderwater: return string.Format(CultureInfo.InvariantCulture, SwimLang.T("Στροφή {0}{1}: απόσταση κάτω από το νερό"), wall, UnitLabel);
                case SwimPickTarget.FinishLastStroke: return SwimLang.T("Τερματισμός: απόσταση τελευταίας χεριάς");
                case SwimPickTarget.PhaseEntry: return PendingPhaseTitle() + SwimLang.T(": απόσταση εισόδου");
                case SwimPickTarget.PhaseBreakout: return PendingPhaseTitle() + SwimLang.T(": απόσταση εξόδου");
                case SwimPickTarget.BodyLength: return SwimLang.T("Μήκος σώματος (πόδια → κεφάλι)");
                default: return SwimLang.T("Απόσταση");
            }
        }

        /// <summary>
        /// The distance asked for, from the x position in the pool (meters from the start wall).
        /// Start distances are measured from the start wall, turn and finish distances from their wall.
        /// </summary>
        private double MeasuredValue(SwimPickTarget target, int wall, double x)
        {
            switch (target)
            {
                case SwimPickTarget.TurnUnderwater:
                    return Math.Abs(x - WallX(wall));
                case SwimPickTarget.FinishLastStroke:
                    return Math.Abs(x - WallX(Race.Distance));
                default:
                    return x;
            }
        }

        private void StoreMeasurement(SwimPickTarget target, int wall, double value)
        {
            switch (target)
            {
                case SwimPickTarget.EntryHands: Race.EntryDistHands = value; break;
                case SwimPickTarget.EntryHips: Race.EntryDistHips = value; break;
                // (2.9.8) Measured on the video: no longer the estimate from the times.
                case SwimPickTarget.StartUnderwater: Race.UnderwaterDist = value; Race.UnderwaterEstimated = false; break;
                case SwimPickTarget.StartBreakout: Race.BreakoutDist = value; break;
                case SwimPickTarget.TurnUnderwater:
                    SwimTurnManual turn = Race.GetTurn(wall, true);
                    turn.UnderwaterDist = value;
                    turn.UnderwaterEstimated = false;
                    break;
                case SwimPickTarget.FinishLastStroke: Race.FinishLastStrokeDist = value; break;
                case SwimPickTarget.PhaseEntry: Race.GetPhaseValues(pendingPhaseId, true).EntryDist = value; break;
                case SwimPickTarget.PhaseBreakout: Race.GetPhaseValues(pendingPhaseId, true).BreakoutDist = value; break;
            }
        }

        private string PendingPhaseTitle()
        {
            SwimTechPhase phase = TechPhases.FirstOrDefault(p => p.Id == pendingPhaseId);
            return phase != null ? phase.Title : SwimLang.T("Προσπάθεια");
        }

        /// <summary>
        /// Distance of a start or a turn of the technical analysis: in a race the values of the race analysis
        /// (entry and breakout of the start, underwater distance of the turn), in a training video the values of the attempt.
        /// </summary>
        public void BeginPhaseMeasure(SwimTechPhase phase, bool entry)
        {
            if (phase == null)
            {
                Status(SwimLang.T("Επίλεξε πρώτα μια εκκίνηση ή μια στροφή."), true);
                return;
            }

            if (!Race.IsTraining)
            {
                if (phase.Kind == SwimPhaseKind.Start)
                    BeginMeasure(entry ? SwimPickTarget.EntryHands : SwimPickTarget.StartBreakout, 0);
                else
                    BeginMeasure(SwimPickTarget.TurnUnderwater, phase.Wall);
                return;
            }

            pendingPhaseId = phase.Id;
            BeginMeasure(entry ? SwimPickTarget.PhaseEntry : SwimPickTarget.PhaseBreakout, 0);
        }

        /// <summary>
        /// Lines proposed for a calibration done for a measurement: the rope marks around the expected position.
        /// </summary>
        private void SuggestLines(SwimPickTarget target, int wall, out double line1, out double line2)
        {
            int pool = PoolLength;
            switch (target)
            {
                case SwimPickTarget.EntryHands:
                case SwimPickTarget.EntryHips:
                    line1 = 0;
                    line2 = 5;
                    break;
                case SwimPickTarget.StartUnderwater:
                case SwimPickTarget.StartBreakout:
                    line1 = 5;
                    line2 = Math.Min(15, pool);
                    break;
                case SwimPickTarget.TurnUnderwater:
                    {
                        double wx = WallX(wall);
                        double dir = wx == 0 ? 1 : -1;
                        line1 = wx + dir * 5;
                        line2 = wx + dir * Math.Min(15, pool);
                        break;
                    }
                case SwimPickTarget.FinishLastStroke:
                    {
                        double wx = WallX(Race.Distance);
                        double dir = wx == 0 ? 1 : -1;
                        line1 = wx;
                        line2 = wx + dir * 5;
                        break;
                    }
                case SwimPickTarget.BodyLength:
                    {
                        // (2.15.0) The glide after the push-off: the wall of the turn and its 5 (the end of the colour of the ropes).
                        if (Race.IsTraining || wall <= 0)
                        {
                            line1 = CalibrationLine1;
                            line2 = CalibrationLine2;
                            break;
                        }

                        double wx = WallX(wall);
                        double dir = wx == 0 ? 1 : -1;
                        line1 = wx;
                        line2 = wx + dir * 5;
                        break;
                    }
                default:
                    line1 = CalibrationLine1;
                    line2 = CalibrationLine2;
                    break;
            }

            line1 = Math.Max(0, Math.Min(pool, line1));
            line2 = Math.Max(0, Math.Min(pool, line2));
        }

        /// <summary>
        /// Frame of the mark that goes with a distance (hands entry, breakout, last stroke), if marked.
        /// </summary>
        private long? MeasureFrame(SwimPickTarget target, int wall)
        {
            if (Race == null)
                return null;

            if (target == SwimPickTarget.PhaseEntry || target == SwimPickTarget.PhaseBreakout)
            {
                SwimTechPhase phase = TechPhases.FirstOrDefault(p => p.Id == pendingPhaseId);
                double? t = phase == null ? null : (target == SwimPickTarget.PhaseEntry ? phase.Entry : phase.Breakout);
                if (!t.HasValue)
                    return null;

                return phase.Origin + (long)Math.Round(t.Value * 1000.0 * (Timing.HighSpeedFactor <= 0 ? 1.0 : Timing.HighSpeedFactor) / Timing.FrameIntervalMilliseconds * Timing.AverageTimestampsPerFrame);
            }

            if (!Race.StartTimestamp.HasValue)
                return null;

            long start = Race.StartTimestamp.Value;
            List<long> walls = TurnWalls.Select(w => Race.GetSplit(w)).Where(m => m != null).Select(m => m.Timestamp).OrderBy(t => t).ToList();
            long firstWall = walls.Count > 0 ? walls[0] : long.MaxValue;
            Func<SwimMarkType, long, long, long?> first = (type, after, before) => Race.Marks
                .Where(m => m.Type == type && m.Timestamp > after && m.Timestamp < before)
                .OrderBy(m => m.Timestamp)
                .Select(m => (long?)m.Timestamp)
                .FirstOrDefault();

            switch (target)
            {
                case SwimPickTarget.EntryHands:
                    return first(SwimMarkType.Entry, start, firstWall);
                case SwimPickTarget.StartUnderwater:
                case SwimPickTarget.StartBreakout:
                    return first(SwimMarkType.Breakout, start, firstWall);
                case SwimPickTarget.TurnUnderwater:
                    {
                        SwimMark split = Race.GetSplit(wall);
                        if (split == null)
                            return null;

                        long next = walls.Where(t => t > split.Timestamp).DefaultIfEmpty(long.MaxValue).First();
                        SwimMark finish = Race.GetSplit(Race.Distance);
                        if (finish != null && finish.Timestamp > split.Timestamp)
                            next = Math.Min(next, finish.Timestamp);

                        return first(SwimMarkType.Breakout, split.Timestamp, next);
                    }
                case SwimPickTarget.BodyLength:
                    {
                        // (2.15.0) A little after the push-off of the turn (the glide, the body straight).
                        SwimMark split = Race.GetSplit(wall);
                        if (split == null)
                            return null;

                        long next = walls.Where(t => t > split.Timestamp).DefaultIfEmpty(long.MaxValue).First();
                        long? pushoff = first(SwimMarkType.FeetOff, split.Timestamp, next);
                        if (!pushoff.HasValue)
                            return null;

                        return pushoff.Value + (long)Math.Round(GlideAfterPushoff * 1000.0 * (Timing.HighSpeedFactor <= 0 ? 1.0 : Timing.HighSpeedFactor) /
                            Timing.FrameIntervalMilliseconds * Timing.AverageTimestampsPerFrame);
                    }
                case SwimPickTarget.FinishLastStroke:
                    {
                        SwimMark finish = Race.GetSplit(Race.Distance);
                        if (finish == null)
                            return null;

                        long previousWall = walls.Where(t => t < finish.Timestamp).DefaultIfEmpty(start).Last();
                        return Race.Marks
                            .Where(m => m.Type == SwimMarkType.Stroke && m.Timestamp > previousWall && m.Timestamp < finish.Timestamp)
                            .OrderBy(m => m.Timestamp)
                            .Select(m => (long?)m.Timestamp)
                            .LastOrDefault();
                    }
                default:
                    return null;
            }
        }

        /// <summary>(2.15.0) The frame offered for the body length: this long after the push-off (s), in the glide.</summary>
        private const double GlideAfterPushoff = 0.3;

        /// <summary>(2.15.0) The first click of the body length (the toes), waiting for the head on the same frame; null when none.</summary>
        private SwimMeasurement bodyFirst;

        /// <summary>
        /// (2.15.0) A click of the body length: the toes, then the top of the head on the same frame. The length is along the lane
        /// (the pool x of the calibration): both points are under the water at about the same depth, so what the water shifts seen
        /// from the stands moves both alike and cancels (one click on the head from the wall would be off by it, 0.3–0.5 m).
        /// </summary>
        private void BodyLengthClick(PointF point, double x, double y, long timestamp, SwimCalibration calibration)
        {
            if (bodyFirst == null || !SameFrame(bodyFirst.Timestamp, timestamp))
            {
                bodyFirst = new SwimMeasurement { Timestamp = timestamp, Image = point, PoolX = x, PoolY = y, Calibration = calibration };
                Status(SwimLang.T("📍 Μήκος σώματος: τώρα κλικ στην κορυφή του κεφαλιού (στο ίδιο καρέ). Esc: ακύρωση."), false);
                InvalidatePlayer();
                return;
            }

            SwimMeasurement toes = bodyFirst;
            bodyFirst = null;
            double length = Math.Abs(x - toes.PoolX);
            double meters = length * Race.K;
            if (meters < SwimTechCalculator.BodyLengthMin || meters > SwimTechCalculator.BodyLengthMax)
            {
                SystemSounds.Exclamation.Play();
                Status(string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("📍 Μήκος σώματος {0:0.00} {1}: δεν γίνεται (έλεγξε τα δύο κλικ και τη βαθμονόμηση). Κλικ ξανά στα δάχτυλα των ποδιών."), length, UnitLabel), true);
                InvalidatePlayer();
                return;
            }

            double value = Math.Round(length, 2);
            Race.BodyLength = value;
            lastMeasurement = new SwimMeasurement
            {
                Timestamp = timestamp,
                Image = point,
                PoolX = x,
                PoolY = y,
                Value = value,
                Label = string.Format(CultureInfo.InvariantCulture, "{0:0.00} {1}", value, UnitShort),
                Calibration = calibration,
                From = toes.Image
            };

            pendingMeasure = SwimPickTarget.None;
            SetPick(SwimPickTarget.None);
            string message = string.Format(CultureInfo.InvariantCulture,
                SwimLang.T("📍 Μήκος σώματος (πόδια → κεφάλι): {0:0.00} {1} · για την υποβρύχια ταχύτητα όλων των στροφών"), value, UnitLabel);
            if (Settings.FixedCamera && Math.Abs(Timing.Seconds(timestamp, calibration.Timestamp)) > 0.05)
                message += string.Format(CultureInfo.InvariantCulture, SwimLang.T("  (βαθμονόμηση {0} {1} από άλλο καρέ: σωστό μόνο αν η κάμερα δεν κινήθηκε)"), calibration.Label, UnitLabel);

            Status(message, false);
            Changed(false);
            ScheduleSend();
            if (form != null)
                form.RefreshManualFields();
        }

        private SwimCalibration NearestCalibration(long timestamp)
        {
            if (VideoFile == null)
                return null;

            SwimCalibration best = null;
            long bestDistance = long.MaxValue;
            foreach (SwimCalibration c in VideoFile.Calibrations)
            {
                if (c == null || !c.IsValid)
                    continue;

                long distance = Math.Abs(c.Timestamp - timestamp);
                if (distance < bestDistance)
                {
                    best = c;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private bool SameFrame(long a, long b)
        {
            double tolerance = Timing.AverageTimestampsPerFrame > 0 ? Timing.AverageTimestampsPerFrame * 0.5 : 0;
            return Math.Abs(a - b) <= tolerance;
        }

        /// <summary>
        /// Half a frame (timestamps). A mark up to half a frame after a frame is on that frame: the checkpoints and breakouts the auto
        /// pilot puts keep the time read from the video, between two frames, and the frame shown for them is the nearest one (2.7.1).
        /// </summary>
        private long HalfFrame
        {
            get { return Timing != null && Timing.AverageTimestampsPerFrame > 0 ? (long)Math.Floor(Timing.AverageTimestampsPerFrame * 0.5) : 0; }
        }

        private double RaceSeconds(long timestamp)
        {
            if (Race == null || frameServer == null)
                return 0;

            long origin = Race.StartTimestamp ?? frameServer.Metadata.TimeOrigin;
            return Timing.Seconds(timestamp, origin);
        }

        private void SetPick(SwimPickTarget target)
        {
            PickTarget = target;
            if (player != null && !player.IsDisposed)
                player.SwimPickModeChanged(target != SwimPickTarget.None);

            if (form != null)
                form.RefreshPick();
        }

        private void InvalidatePlayer()
        {
            if (player != null && !player.IsDisposed)
                player.DoInvalidate();
        }
        #endregion

        #region Overlay
        // HUD (2.9.0): the lines of the pool amber, the measures cyan, the labels on glass chips.
        private static readonly Pen penLine = new Pen(Color.FromArgb(200, 255, 206, 110), 1.0f);
        private static readonly Pen penMajor = new Pen(Color.FromArgb(230, 255, 182, 72), 2.0f);
        private static readonly Pen penRope = new Pen(Color.FromArgb(160, 255, 206, 110), 1.0f) { DashStyle = DashStyle.Dash };
        private static readonly Pen penMeasure = new Pen(Color.FromArgb(240, 56, 225, 255), 2.0f);
        private static readonly SolidBrush brushPoint = new SolidBrush(Color.FromArgb(240, 255, 206, 110));
        private static readonly SolidBrush brushMeasure = new SolidBrush(Color.FromArgb(240, 56, 225, 255));
        private static readonly Font fontLabel = new Font("Segoe UI", 8.5f, FontStyle.Bold);

        /// <summary>
        /// Draws the calibration and the last measured distance on the player screen (screen only).
        /// frame: the image of the video on the screen (since 2.8.0, for the ghost of the reference).
        /// </summary>
        public void DrawOverlay(PlayerScreenUserInterface screen, Graphics g, ImageTransform transform, long timestamp, Size renderingSize, Bitmap frame = null)
        {
            if (screen != player || !Active || VideoFile == null || transform == null)
                return;

            // The exports keep the proportions of the graphics on the screen.
            if (renderingSize.Width > 0 && renderingSize.Height > 0)
                lastRenderingSize = renderingSize;

            lock (overlayLock)
            try
            {
                SmoothingMode smoothing = g.SmoothingMode;
                TextRenderingHint textHint = g.TextRenderingHint;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                // The graphics of the analysis can be put off (key V, 2.9.0): the tools (calibration, angles, skeleton, the
                // automatic strokes and the proposal being checked) stay.
                bool hud = Settings == null || Settings.VideoGraphics;

                // The ghost and the line of the reference, on the water under everything else.
                if (hud && Race != null && !Race.IsTraining && CompareShown)
                {
                    Size reference = frameServer.VideoReader.Info.ReferenceSize;
                    PointF origin = transform.Transform(new PointF(0, 0));
                    PointF far = transform.Transform(new PointF(10000, 10000));
                    float sx = (far.X - origin.X) / 10000f, sy = (far.Y - origin.Y) / 10000f;
                    DrawCompare(g, timestamp, frame, reference, p => new PointF(origin.X + p.X * sx, origin.Y + p.Y * sy), renderingSize, 1f);
                }

                if (PickTarget == SwimPickTarget.Calibration)
                {
                    DrawCalibrationClicks(g, transform, timestamp);
                }
                else
                {
                    SwimCalibration calibration = PickTarget != SwimPickTarget.None ? CalibrationFor(timestamp) : null;
                    if (calibration == null)
                    {
                        SwimCalibration nearest = NearestCalibration(timestamp);
                        if (nearest != null && SameFrame(nearest.Timestamp, timestamp))
                            calibration = nearest;
                    }

                    if (calibration != null)
                        DrawCalibration(g, transform, calibration);
                }

                if (lastMeasurement != null && SameFrame(lastMeasurement.Timestamp, timestamp))
                    DrawMeasurement(g, transform, lastMeasurement);

                // (2.15.0) The toes clicked for the body length, waiting for the head.
                if (PickTarget == SwimPickTarget.BodyLength && bodyFirst != null && SameFrame(bodyFirst.Timestamp, timestamp))
                {
                    Point toesPoint = transform.Transform(bodyFirst.Image);
                    g.FillEllipse(brushMeasure, toesPoint.X - 5, toesPoint.Y - 5, 10, 10);
                    DrawLabel(g, SwimLang.T("πόδια"), new Point(toesPoint.X + 10, toesPoint.Y + 6), SwimTheme.AccentLight);
                }

                DrawAngles(g, transform, timestamp);
                if (SkeletonShown)
                    DrawSkeleton(g, timestamp, p => transform.Transform(p), 1f);
                DrawSkeletonPrompt(g);
                if (!hud)
                {
                    // Off: nothing of the graphics.
                }
                else if (HudReady(renderingSize))
                {
                    // The HUD of the race (2.9.1, as the demo): it replaces the boards, the chart and the technical view; the boards
                    // chosen in «Στο βίντεο» are added to it.
                    DrawHud(g, timestamp, renderingSize, p => transform.Transform(p));
                }
                else if (TechHudReady(renderingSize, timestamp))
                {
                    // (2.9.9) The technical HUD of a start or a turn of a training video.
                    DrawTechHud(g, timestamp, renderingSize, p => transform.Transform(p));
                }
                else if (ReelReady(renderingSize))
                {
                    // The technical view replaces the race board, the velocity chart and the values of the start or turn.
                    DrawReel(g, timestamp, renderingSize, p => transform.Transform(p));
                }
                else
                {
                    if (Race != null && (Race.IsTraining || TechnicalView))
                        DrawTechOverlay(g, timestamp, renderingSize, SwimmerOnScreen(timestamp, transform));
                    if (Race != null && !Race.IsTraining)
                        DrawRaceOverlay(g, timestamp, renderingSize, false, false, SwimmerOnScreen(timestamp, transform));
                }

                DrawStrokeOverlay(g, transform, timestamp, renderingSize);

                // The proposal being checked, on top of everything (screen only).
                DrawProposalPrompt(g, timestamp, renderingSize);

                g.SmoothingMode = smoothing;
                g.TextRenderingHint = textHint;
            }
            catch (Exception e)
            {
                log.Error("Error while drawing the swim analysis overlay.", e);
            }
        }

        private void DrawCalibrationClicks(Graphics g, ImageTransform transform, long timestamp)
        {
            if (calibrationClicks.Count == 0 || !SameFrame(calibrationFrame, timestamp))
                return;

            List<Point> points = calibrationClicks.Select(p => transform.Transform(p)).ToList();
            if (points.Count >= 2)
                g.DrawLine(penMajor, points[0], points[1]);
            if (points.Count >= 3)
                g.DrawLine(penRope, points[0], points[2]);

            for (int i = 0; i < points.Count; i++)
            {
                Point p = points[i];
                g.FillEllipse(brushPoint, p.X - 4, p.Y - 4, 8, 8);
                double line = i < 2 ? CalibrationLine1 : CalibrationLine2;
                string text = string.Format(CultureInfo.InvariantCulture, "{0} · {1:0.##} {3} {2}", i + 1, line, i % 2 == 0 ? "A" : "B", UnitShort);
                DrawLabel(g, text, new Point(p.X + 8, p.Y - 18), Color.White);
            }
        }

        private void DrawCalibration(Graphics g, ImageTransform transform, SwimCalibration c)
        {
            double width = c.LaneWidth;
            double min = Math.Min(c.Line1, c.Line2);
            double max = Math.Max(c.Line1, c.Line2);
            double xFrom = Math.Max(0, Math.Floor(min) - 5);
            double xTo = Math.Min(PoolLength, Math.Ceiling(max) + 5);

            // Lane ropes.
            DrawPoolSegment(g, transform, c, penRope, xFrom, 0, xTo, 0);
            DrawPoolSegment(g, transform, c, penRope, xFrom, width, xTo, width);

            // One line across the lane every meter, labelled every 5 meters and at the calibration lines.
            for (double x = xFrom; x <= xTo + 1e-6; x += 1)
            {
                bool calibrated = Math.Abs(x - c.Line1) < 1e-6 || Math.Abs(x - c.Line2) < 1e-6;
                bool major = calibrated || Math.Abs(x % 5) < 1e-6;
                DrawPoolSegment(g, transform, c, major ? penMajor : penLine, x, 0, x, width);
                if (major)
                {
                    PointF label;
                    if (c.TryImage(x, width, out label))
                        DrawLabel(g, string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", x, UnitShort), transform.Transform(label), Color.FromArgb(255, 214, 140));
                }
            }

            // Calibration lines that are not whole meters.
            foreach (double x in new double[] { c.Line1, c.Line2 })
            {
                if (Math.Abs(x - Math.Round(x)) > 1e-6)
                    DrawPoolSegment(g, transform, c, penMajor, x, 0, x, width);
            }
        }

        private void DrawMeasurement(Graphics g, ImageTransform transform, SwimMeasurement m)
        {
            Point p = transform.Transform(m.Image);
            if (m.From.HasValue)
            {
                // (2.15.0) A length of two clicks (the body: toes → head).
                Point a = transform.Transform(m.From.Value);
                g.DrawLine(penMeasure, a, p);
                g.FillEllipse(brushMeasure, a.X - 5, a.Y - 5, 10, 10);
            }
            else if (m.Calibration != null)
            {
                DrawPoolSegment(g, transform, m.Calibration, penMeasure, m.PoolX, 0, m.PoolX, m.Calibration.LaneWidth);
            }

            g.FillEllipse(brushMeasure, p.X - 5, p.Y - 5, 10, 10);
            DrawLabel(g, m.Label, new Point(p.X + 10, p.Y + 6), SwimTheme.AccentLight);
        }

        private static void DrawPoolSegment(Graphics g, ImageTransform transform, SwimCalibration c, Pen pen, double x1, double y1, double x2, double y2)
        {
            PointF a, b;
            if (!c.TryImage(x1, y1, out a) || !c.TryImage(x2, y2, out b))
                return;

            g.DrawLine(pen, transform.Transform(a), transform.Transform(b));
        }

        /// <summary>A label on the video (prompts, the meters of the pool, measures): a glass chip of the HUD, its border in its color.</summary>
        private static void DrawLabel(Graphics g, string text, Point location, Color color)
        {
            SizeF size = g.MeasureString(text, fontLabel);
            RectangleF back = new RectangleF(location.X - 3, location.Y - 2, size.Width + 6, size.Height + 4);
            SwimTheme.HudChip(g, back, color, 1f, 3f, 200);
            using (SolidBrush brush = new SolidBrush(color))
                g.DrawString(text, fontLabel, brush, location);
        }
        #endregion

        #region Race overlay: race time, last split, delta, ΔΑΚ and velocity chart
        // HUD (2.9.0): the swimmer amber, the reference cyan, as before, in the colors of the HUD; the lines glow.
        private static readonly Color colorAthlete = SwimTheme.Amber;
        private static readonly Color colorReference = SwimTheme.Accent;
        private static readonly Pen penAthlete = new Pen(colorAthlete, 2.4f) { LineJoin = LineJoin.Round };
        private static readonly Pen penAthleteGlow = new Pen(Color.FromArgb(60, colorAthlete), 7f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        private static readonly Pen penAthleteLast = new Pen(colorAthlete, 4f);
        private static readonly Pen penReference = new Pen(colorReference, 1.6f) { DashStyle = DashStyle.Dash };
        private static readonly Pen penReferenceGlow = new Pen(Color.FromArgb(38, colorReference), 5f);
        private static readonly SolidBrush brushCaption = new SolidBrush(SwimTheme.AccentLight);

        // Start, turns and last 5 m before a wall: conventional velocity (touch at the wall, dive, glide, underwater), drawn faint.
        private static readonly Pen penAthletePhase = new Pen(Color.FromArgb(150, colorAthlete), 1.6f) { DashStyle = DashStyle.Dot };
        private static readonly Pen penAthleteLastPhase = new Pen(Color.FromArgb(170, colorAthlete), 3f) { DashStyle = DashStyle.Dot };
        private static readonly Pen penReferencePhase = new Pen(Color.FromArgb(120, colorReference), 1.2f) { DashStyle = DashStyle.Dot };
        private static readonly Pen penChartGrid = new Pen(Color.FromArgb(34, 56, 225, 255), 1f);
        private static readonly Pen penChartWall = new Pen(Color.FromArgb(110, 56, 225, 255), 1f) { DashStyle = DashStyle.Dot };
        private static readonly SolidBrush brushText = new SolidBrush(Color.White);
        private static readonly SolidBrush brushDim = new SolidBrush(Color.FromArgb(230, 124, 200, 214));
        private static readonly SolidBrush brushFaster = new SolidBrush(Color.FromArgb(230, 22, 170, 112));
        private static readonly SolidBrush brushSlower = new SolidBrush(Color.FromArgb(230, 210, 60, 66));
        private static readonly SolidBrush brushAthlete = new SolidBrush(colorAthlete);
        private static readonly SolidBrush brushReference = new SolidBrush(colorReference);

        /// <summary>
        /// The graphics of the race on the video without the technical view or the HUD: the boards and, after the finish, the summary card
        /// of the race (2.9.5, as in the technical view), not on the images of the report.
        /// all: the boards of the whole race (the image of the final frame); report: an image of the report.
        /// </summary>
        /// <param name="swimmer">Where the swimmer is on the screen (2.9.0): the graphics fade over him, he stays in front; empty: not known.</param>
        private void DrawRaceOverlay(Graphics g, long timestamp, Size size, bool all = false, bool report = false, RectangleF swimmer = default(RectangleF))
        {
            DrawRaceBoards(g, timestamp, size, all, report, swimmer);
            if (!all && !report)
                DrawRaceSummary(g, timestamp, size);
        }

        /// <summary>
        /// Panel in a corner of the video, synchronized with the video time: only the splits already
        /// passed at the current frame are shown, so it plays "live" during the playback.
        /// report: an image of the report (the Δ and the reference's name on the board whatever is chosen for the video, as before 2.8.0).
        /// </summary>
        /// <param name="swimmer">Where the swimmer is on the screen (2.9.0): the graphics fade over him, he stays in front; empty: not known.</param>
        private void DrawRaceBoards(Graphics g, long timestamp, Size size, bool all, bool report, RectangleF swimmer)
        {
            if (Race == null || Computed == null || !Race.StartTimestamp.HasValue || Race.Distance <= 0)
                return;
            bool showBoard = all || Settings.ShowSplitBoard;
            bool showChart = all || Settings.ShowSpeedChart;
            bool showDelta = (all || Settings.ShowDeltaChart) && Computed.Reference != null;
            if (size.Width < 300 || size.Height < 200)
                return;

            double now = Timing.Seconds(timestamp, Race.StartTimestamp.Value);
            if (now < -1e-6)
                return;

            const int margin = 10;
            string corner = Settings.OverlayCorner ?? "TopRight";
            if (all || Settings.ShowFinalsBoard)
                DrawFinalsBoard(g, now, size, corner, margin, swimmer);
            if (!showChart && !showBoard && !showDelta)
                return;

            int width = Math.Max(280, Math.Min(540, (int)(size.Width * 0.36)));
            int boardHeight = showBoard ? 70 : 0;
            int chartHeight = showChart ? (int)(width * 0.5) : 0;
            int deltaHeight = showDelta ? (int)(width * 0.3) : 0;
            int gap = boardHeight > 0 && chartHeight > 0 ? 4 : 0;
            int gapDelta = deltaHeight > 0 && boardHeight + chartHeight > 0 ? 4 : 0;
            int height = boardHeight + gap + chartHeight + gapDelta + deltaHeight;
            if (height > size.Height - 20)
            {
                deltaHeight = 0;
                gapDelta = 0;
                height = boardHeight + gap + chartHeight;
            }

            if (height > size.Height - 20)
            {
                chartHeight = 0;
                gap = 0;
                height = boardHeight;
                if (height == 0)
                    return;
            }

            int x = corner.EndsWith("Left", StringComparison.Ordinal) ? margin : size.Width - width - margin;
            int y = corner.StartsWith("Bottom", StringComparison.Ordinal) ? size.Height - height - margin : margin;

            // Splits reached at this moment of the video.
            List<SwimRow> done = Computed.Rows.Where(r => r.PassedAt(now)).ToList();
            SwimRow last = done.OrderBy(r => r.T.Value).LastOrDefault();

            // Each card fades by itself where the swimmer is under it (the others stay as they are).
            int top = y;
            if (boardHeight > 0)
            {
                Rectangle board = new Rectangle(x, top, width, boardHeight);
                HudLayer(g, board, SwimmerFade(board, swimmer), layer =>
                {
                    DrawCard(layer, board, SwimTheme.Accent);
                    DrawBoard(layer, board, now, last, all || report || Settings.CompareDelta, all || report || Settings.CompareDelta || CompareShown);
                });
                top += boardHeight + (chartHeight > 0 ? gap : gapDelta);
            }

            if (chartHeight > 0)
            {
                Rectangle chart = new Rectangle(x, top, width, chartHeight);
                HudLayer(g, chart, SwimmerFade(chart, swimmer), layer =>
                {
                    DrawCard(layer, chart, colorAthlete);
                    DrawSpeedChart(layer, chart, done, last);
                });
                top += chartHeight + gapDelta;
            }

            if (deltaHeight > 0)
            {
                List<SwimDeltaPoint> points = done.Where(r => r.Delta.HasValue).Select(r => new SwimDeltaPoint { D = r.D, Delta = r.Delta.Value }).ToList();
                Rectangle delta = new Rectangle(x, top, width, deltaHeight);
                HudLayer(g, delta, SwimmerFade(delta, swimmer), layer =>
                    SwimFinalsPainter.PaintDelta(layer, delta, points, Race.Distance, PoolLength, Computed.Reference.Name, true, 1f));
            }
        }

        /// <summary>
        /// (2.9.5) After the finish, the summary card of the race over the boards, as in the technical view (the choice «σύνοψη στο
        /// τέλος»): on the screen and in the exported video, sooner when the video ends soon after the touch; not while a click on the
        /// video is awaited.
        /// </summary>
        private void DrawRaceSummary(Graphics g, long timestamp, Size size)
        {
            if (Settings == null || !Settings.ReelSummary || PickTarget != SwimPickTarget.None || Race == null || Race.IsTraining ||
                Computed == null || !Race.StartTimestamp.HasValue || !Timing.IsValid)
                return;

            SwimReel reel = Reel;
            if (reel == null || !reel.Finish.HasValue)
                return;

            SwimReelScene scene = new SwimReelScene
            {
                Race = Race,
                Computed = Computed,
                Reel = reel,
                Timing = Timing,
                Timestamp = timestamp,
                Time = Timing.Seconds(timestamp, Race.StartTimestamp.Value),
                Summary = true,
                Final = Settings.ShowFinalsBoard ? FinalForRace() : null,
                BoardName = BoardName(Race.Athlete),
                Delta = Settings.CompareDelta || Settings.ShowDeltaChart,
                VideoEnd = VideoEndSeconds()
            };
            SwimReelPainter.PaintSummary(g, size, scene);
        }

        /// <summary>Race time of the last frame of the video (its working zone, what is exported), null when not known (2.9.5).</summary>
        private double? VideoEndSeconds()
        {
            if (frameServer == null || !frameServer.Loaded || Race == null || !Race.StartTimestamp.HasValue || !Timing.IsValid)
                return null;

            VideoSection zone = frameServer.VideoReader.WorkingZone;
            if (zone.IsEmpty || zone.End <= Race.StartTimestamp.Value)
                return null;

            return Timing.Seconds(zone.End, Race.StartTimestamp.Value);
        }

        /// <summary>
        /// Where the swimmer is on the screen at this frame (2.9.0: the graphics fade over him), from the part of the race followed
        /// there («Ακολούθηση αθλητή», the auto pilot): his body, the water of his arms and the length ahead of his head; empty when
        /// not known.
        /// </summary>
        private RectangleF SwimmerOnScreen(long timestamp, ImageTransform transform)
        {
            return transform == null ? RectangleF.Empty : SwimHud.SwimmerBox(Race, timestamp, p => transform.Transform(p));
        }

        /// <summary>
        /// How much a panel of the boards shows: whole (2.9.4: the boards the analyst chose stay whole and lit until he takes them
        /// away with the boxes «Στο βίντεο» or V; the HUD keeps the swimmer in front). Before: down to 0.25 over the swimmer (2.9.0).
        /// </summary>
        private static float SwimmerFade(Rectangle panel, RectangleF swimmer)
        {
            return 1f;
        }

        /// <summary>
        /// Draws graphics as they are (alpha 1), or through a see-through layer (alpha less than 1: the swimmer under them).
        /// </summary>
        private static void HudLayer(Graphics g, Rectangle place, float alpha, Action<Graphics> draw)
        {
            SwimHud.Layer(g, place, alpha, draw);
        }

        /// <summary>
        /// The swimmers of the final at the last checkpoint passed and the athlete among them, on the other side of the video than the
        /// race board: until the next checkpoint (2.9.4; before: 4.5 s after it, 10 s after the finish).
        /// </summary>
        private void DrawFinalsBoard(Graphics g, double now, Size size, string corner, int margin, RectangleF swimmer = default(RectangleF))
        {
            // Not over the cards of the technical analysis, the prompts of a click, or a race board in a narrow view.
            if (TechnicalView || PickTarget != SwimPickTarget.None || size.Width < 560)
                return;

            SwimFinal final = FinalForRace();
            if (final == null || Computed == null)
                return;

            SwimRow row = Computed.Rows.Where(r => r.PassedAt(now) && final.Swimmers.Any(s => s.TimeAt(r.D).HasValue))
                .OrderBy(r => r.T.Value).LastOrDefault();
            if (row == null)
                return;

            bool finish = row.D >= Race.Distance;
            List<SwimFinalsBoardRow> rows = SwimFinalsCompare.Board(final, row.D, row.T.Value, BoardName(Race.Athlete));
            if (rows == null)
                return;

            // The size in steps (a font of each size is kept).
            float scale = (float)Math.Round(Math.Max(210, Math.Min(380, size.Width * 0.27)) / 260.0 * 10) / 10f;
            int width = (int)Math.Round(260 * scale);
            int height = SwimFinalsPainter.BoardHeight(rows.Count, scale);
            if (height > size.Height - 2 * margin)
                return;

            int x = corner.EndsWith("Left", StringComparison.Ordinal) ? size.Width - width - margin : margin;
            int y = corner.StartsWith("Bottom", StringComparison.Ordinal) ? size.Height - height - margin : margin;
            Rectangle place = new Rectangle(x, y, width, height);
            HudLayer(g, place, SwimmerFade(place, swimmer), layer => SwimFinalsPainter.PaintBoard(layer, place, SwimFinalsPainter.BoardTitle(final, row.D, finish),
                SwimFinalsPainter.BoardSubtitle(final), rows, scale));
        }

        /// <summary>The family name of the athlete for the boards (the registry writes it first).</summary>
        private static string BoardName(string athlete)
        {
            if (string.IsNullOrWhiteSpace(athlete))
                return "";
            string[] parts = athlete.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : athlete.Trim();
        }

        /// <summary>A glass panel of the HUD (2.9.0): see-through navy, thin cyan border, scan lines, corner brackets in the accent.</summary>
        private static void DrawCard(Graphics g, Rectangle r, Color accent)
        {
            SwimTheme.HudPanel(g, r, accent);
        }

        /// <summary>delta: the Δ with the reference at the last checkpoint; versus: the name of the reference.</summary>
        private void DrawBoard(Graphics g, Rectangle r, double now, SwimRow last, bool delta = true, bool versus = true)
        {
            bool finished = last != null && last.D >= Race.Distance;
            Font caption = SwimTheme.Ui(6.75f, FontStyle.Bold);
            Font splitFont = SwimTheme.Display(11f);

            // Name and event, in spaced capitals (HUD).
            string names = string.IsNullOrWhiteSpace(Race.Athlete) ? "" : SwimLang.Upper(Race.Athlete.Trim());
            string ev = string.Format(CultureInfo.InvariantCulture, "{0}{1} {2}", Race.DistanceLabel, Race.IsYards ? "YD" : "M", SwimLang.Upper(SwimPresets.StyleShort(Race.Style)));
            string head = names.Length > 0 ? names + "  ·  " + ev : ev;
            if (SwimTheme.TrackedWidth(g, head, caption, 0.12f) <= r.Width * 0.62f)
            {
                SwimTheme.Tracked(g, head, caption, brushCaption, r.Left + 11, r.Top + 7, 0.12f);
            }
            else
            {
                using (StringFormat format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
                    g.DrawString(head, caption, brushCaption, new RectangleF(r.Left + 10, r.Top + 6, r.Width * 0.62f, 13), format);
            }

            // Before the frame of the finish the time of the frame, never past the final time (the official one can be a little before
            // the frame of the touch).
            SwimRow end = Computed.Rows.FirstOrDefault(row => row.D == Race.Distance && row.T.HasValue);
            double running = end != null ? Math.Min(Math.Max(0, now), end.T.Value) : Math.Max(0, now);
            string clock = SwimReference.FormatTime(finished ? last.T.Value : running);
            SwimTheme.GlowText(g, clock, SwimTheme.DisplayLight(22f), Color.White, SwimTheme.Accent, new PointF(r.Left + 7, r.Top + 16));

            if (last != null)
            {
                string label = finished ? SwimLang.T("ΤΕΛΙΚΟΣ") : last.D.ToString(CultureInfo.InvariantCulture) + (Race.IsYards ? " YD" : " M");
                string split = SwimReference.FormatTime(last.T.Value);
                SizeF ls = g.MeasureString(label, caption);
                SizeF ss = g.MeasureString(split, splitFont);
                float right = r.Right - 10;
                g.DrawString(label, caption, brushDim, right - ls.Width, r.Top + 6);
                g.DrawString(split, splitFont, brushText, right - ss.Width, r.Top + 18);

                if (last.Delta.HasValue && delta)
                {
                    string text = FormatDelta(last.Delta.Value);
                    Font deltaFont = SwimTheme.Display(10.5f);
                    SizeF ds = g.MeasureString(text, deltaFont);
                    RectangleF pill = new RectangleF(right - ds.Width - 10, r.Top + 40, ds.Width + 10, ds.Height + 1);
                    Color tone = last.Delta.Value <= 0 ? SwimTheme.Green : SwimTheme.Red;
                    using (GraphicsPath path = SwimTheme.Rounded(pill, 5))
                    using (Pen glow = new Pen(Color.FromArgb(70, tone), 3f))
                    using (Pen edge = new Pen(tone, 1f))
                    {
                        g.FillPath(last.Delta.Value <= 0 ? brushFaster : brushSlower, path);
                        g.DrawPath(glow, path);
                        g.DrawPath(edge, path);
                    }

                    g.DrawString(text, deltaFont, brushText, pill.Left + 5, pill.Top + 1);
                }
            }

            // ΔΑΚ of the last 50 completed, and the reference.
            SwimBlock block = Computed.Blocks.Where(b => b.Dak.HasValue && Computed.Rows.Any(row => row.D == b.To && row.PassedAt(now)))
                .OrderBy(b => b.To).LastOrDefault();
            float bx = r.Left + 10;
            if (block != null)
            {
                string dak = string.Format(CultureInfo.InvariantCulture, SwimLang.T("ΔΑΚ {0}-{1}  {2:0.00}"), block.From, block.To, block.Dak.Value);
                Font dakFont = SwimTheme.Ui(7.5f, FontStyle.Bold);
                SizeF ks = g.MeasureString(dak, dakFont);
                RectangleF chip = new RectangleF(bx, r.Bottom - ks.Height - 7, ks.Width + 8, ks.Height + 2);
                SwimTheme.FillRounded(g, Color.FromArgb(200, 70, 48, 16), chip, 4);
                SwimTheme.DrawRounded(g, Color.FromArgb(200, SwimTheme.Amber), chip, 4);
                using (SolidBrush amber = new SolidBrush(SwimTheme.Amber))
                    g.DrawString(dak, dakFont, amber, chip.Left + 4, chip.Top + 1);
                bx = chip.Right + 8;
            }

            if (Computed.Reference != null && versus)
            {
                using (StringFormat format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
                    g.DrawString("vs " + Computed.Reference.Name, caption, brushReference, new RectangleF(bx, r.Bottom - 17, Math.Max(10, r.Right - 90 - bx), 13), format);
            }
        }

        private void DrawSpeedChart(Graphics g, Rectangle r, List<SwimRow> done, SwimRow last)
        {
            Font caption = SwimTheme.Ui(6.75f, FontStyle.Bold);
            Font chartFont = SwimTheme.Ui(7f, FontStyle.Regular);
            Font valueFont = SwimTheme.Display(9.5f);
            SwimTheme.Tracked(g, SwimLang.T("ΤΑΧΥΤΗΤΑ  M/S"), caption, brushCaption, r.Left + 11, r.Top + 7, 0.12f);

            // Legend.
            float lx = r.Right - 8;
            if (Computed.Reference != null)
            {
                string refText = SwimLang.T("ΑΝΑΦΟΡΑ");
                SizeF s = g.MeasureString(refText, caption);
                lx -= s.Width;
                g.DrawString(refText, caption, brushReference, lx, r.Top + 6);
                lx -= 20;
                g.DrawLine(penReference, lx, r.Top + 12, lx + 16, r.Top + 12);
                lx -= 10;
            }

            string athleteText = SwimLang.T("ΑΘΛΗΤΗΣ");
            SizeF a = g.MeasureString(athleteText, caption);
            lx -= a.Width;
            g.DrawString(athleteText, caption, brushAthlete, lx, r.Top + 6);
            lx -= 20;
            g.DrawLine(penAthlete, lx, r.Top + 12, lx + 16, r.Top + 12);

            Rectangle plot = new Rectangle(r.Left + 34, r.Top + 24, r.Width - 34 - 12, r.Height - 24 - 18);
            if (plot.Width < 60 || plot.Height < 30)
                return;

            List<SwimSegment> athleteAll = Computed.Rows.Where(row => row.Vel.HasValue && row.T.HasValue).Select(row => new SwimSegment { D0 = row.PrevD, D1 = row.D, V = row.Vel.Value, Free = row.IsFree }).ToList();
            List<SwimSegment> reference = Computed.ReferenceSegments ?? new List<SwimSegment>();
            List<double> values = athleteAll.Select(s => s.V).Concat(reference.Select(s => s.V)).Where(v => v > 0 && v < 4).ToList();

            double vMin = values.Count > 0 ? values.Min() : 1.4;
            double vMax = values.Count > 0 ? values.Max() : 2.2;
            double step = vMax - vMin > 1.2 ? 0.5 : 0.2;
            double yMin = Math.Floor((vMin - 0.05) / step) * step;
            double yMax = Math.Ceiling((vMax + 0.05) / step) * step;
            if (yMax - yMin < 2 * step)
                yMax = yMin + 2 * step;

            double distance = Race.Distance;
            Func<double, float> X = d => plot.Left + (float)(d / distance * plot.Width);
            Func<double, float> Y = v => plot.Bottom - (float)((Math.Max(yMin, Math.Min(yMax, v)) - yMin) / (yMax - yMin) * plot.Height);

            // Horizontal grid with the velocity labels.
            for (double v = yMin; v <= yMax + 1e-9; v += step)
            {
                float yy = Y(v);
                g.DrawLine(penChartGrid, plot.Left, yy, plot.Right, yy);
                string label = v.ToString("0.0", CultureInfo.InvariantCulture);
                SizeF ls = g.MeasureString(label, chartFont);
                g.DrawString(label, chartFont, brushDim, plot.Left - 3 - ls.Width, yy - ls.Height / 2);
            }

            // Walls and distance labels.
            int pool = PoolLength;
            for (int w = pool; w < Race.Distance; w += pool)
                g.DrawLine(penChartWall, X(w), plot.Top, X(w), plot.Bottom);

            int labelStep = new int[] { 25, 50, 100, 200, 250, 500 }.FirstOrDefault(s => Race.Distance / s <= 5);
            if (labelStep <= 0)
                labelStep = 500;
            for (int d = 0; d <= Race.Distance; d += labelStep)
            {
                string label = d.ToString(CultureInfo.InvariantCulture);
                SizeF ls = g.MeasureString(label, chartFont);
                g.DrawString(label, chartFont, brushDim, X(d) - ls.Width / 2, plot.Bottom + 2);
            }

            // Reference profile (the whole race, as a target) and the athlete up to the current frame, with a glow under the line.
            // Clean swimming solid, start / turns / last 5 m dotted.
            DrawSteps(g, penReferenceGlow, penReferenceGlow, reference, X, Y);
            DrawSteps(g, penReference, penReferencePhase, reference, X, Y);
            List<SwimSegment> athleteDone = done.Where(row => row.Vel.HasValue).Select(row => new SwimSegment { D0 = row.PrevD, D1 = row.D, V = row.Vel.Value, Free = row.IsFree }).ToList();
            foreach (SwimSegment s in athleteDone.Where(x => x.Free))
            {
                RectangleF area = new RectangleF(X(s.D0), Y(s.V), Math.Max(1, X(s.D1) - X(s.D0)), Math.Max(1, plot.Bottom - Y(s.V)));
                using (LinearGradientBrush glow = new LinearGradientBrush(new RectangleF(area.X, area.Y - 1, area.Width, area.Height + 2),
                    Color.FromArgb(80, colorAthlete), Color.FromArgb(0, colorAthlete), LinearGradientMode.Vertical))
                    g.FillRectangle(glow, area);
            }

            DrawSteps(g, penAthleteGlow, penAthleteGlow, athleteDone.Where(s => s.Free).ToList(), X, Y);
            DrawSteps(g, penAthlete, penAthletePhase, athleteDone, X, Y);

            if (last != null && last.Vel.HasValue)
            {
                float y0 = Y(last.Vel.Value);
                g.DrawLine(last.IsFree ? penAthleteLast : penAthleteLastPhase, X(last.PrevD), y0, X(last.D), y0);
                string value = last.Vel.Value.ToString("0.00", CultureInfo.InvariantCulture);
                SizeF vs = g.MeasureString(value, valueFont);
                float vx = Math.Min(plot.Right - vs.Width, Math.Max(plot.Left, X(last.D) - vs.Width / 2));
                float vy = y0 - vs.Height - 3 < plot.Top ? y0 + 4 : y0 - vs.Height - 3;
                RectangleF pill = new RectangleF(vx - 3, vy, vs.Width + 6, vs.Height);
                SwimTheme.FillRounded(g, Color.FromArgb(225, 3, 16, 26), pill, 4);
                SwimTheme.DrawRounded(g, Color.FromArgb(150, last.IsFree ? colorAthlete : SwimTheme.Dim), pill, 4);
                g.DrawString(value, valueFont, last.IsFree ? brushAthlete : brushDim, vx, vy);
            }

            if (athleteAll.Count == 0 && reference.Count == 0)
            {
                string hint = SwimLang.T("η ταχύτητα εμφανίζεται με τα περάσματα");
                SizeF hs = g.MeasureString(hint, chartFont);
                g.DrawString(hint, chartFont, brushDim, plot.Left + (plot.Width - hs.Width) / 2, plot.Top + (plot.Height - hs.Height) / 2);
            }
        }

        /// <summary>
        /// Step line: one horizontal line per segment, joined when the segments are contiguous.
        /// The segments outside the clean swimming use the faint pen.
        /// </summary>
        private static void DrawSteps(Graphics g, Pen pen, Pen phasePen, List<SwimSegment> segments, Func<double, float> X, Func<double, float> Y)
        {
            SwimSegment previous = null;
            foreach (SwimSegment s in segments.OrderBy(x => x.D0))
            {
                float y = Y(s.V);
                if (previous != null && previous.D1 == s.D0)
                    g.DrawLine(previous.Free && s.Free ? pen : phasePen, X(s.D0), Y(previous.V), X(s.D0), y);

                g.DrawLine(s.Free ? pen : phasePen, X(s.D0), y, X(s.D1), y);
                previous = s;
            }
        }

        private static string FormatDelta(double delta)
        {
            return SwimReference.FormatDelta(delta);
        }
        #endregion

        #region Races
        public void SelectRace(string id)
        {
            if (VideoFile == null)
                return;

            SwimRace race = VideoFile.Races.FirstOrDefault(r => r.Id == id);
            if (race == null || race == Race)
                return;

            CancelPick();
            Race = race;
            VideoFile.ActiveRaceId = race.Id;
            ClearUndo();

            // Its official times placed in it as it is now (a start moved while another swimmer of the video was shown).
            ReapplyOfficial();
            string check = CheckOnOpen();
            PbCandidates = new List<SwimAnalysisItem>();
            Changed(false);
            RefreshPbCandidates();
            if (form != null)
                form.RefreshAll();
            if (check.Length > 0)
                Status(check, true);
        }

        public void NewRace()
        {
            if (VideoFile == null)
                return;

            CancelPick();
            SwimRace race = Race != null ? Race.CloneForNewSwimmer() : CreateDefaultRace();
            VideoFile.Races.Add(race);
            Race = race;
            VideoFile.ActiveRaceId = race.Id;
            EnsureReference();
            ClearUndo();
            Status(SwimLang.T("Νέα ανάλυση στο ίδιο βίντεο (ίδιο σήμα εκκίνησης). Συμπλήρωσε το όνομα του αθλητή."), false);
            Changed(false);
            if (form != null)
                form.RefreshAll();
        }

        public void DeleteRace()
        {
            if (VideoFile == null || Race == null)
                return;

            CancelPick();
            VideoFile.Races.Remove(Race);
            if (VideoFile.Races.Count == 0)
                VideoFile.Races.Add(CreateDefaultRace());

            Race = VideoFile.Races[VideoFile.Races.Count - 1];
            VideoFile.ActiveRaceId = Race.Id;
            ClearUndo();

            // Its official times placed in it as it is now (as when it is selected).
            ReapplyOfficial();
            Changed(false);
            if (form != null)
                form.RefreshAll();
        }

        /// <summary>
        /// Called by the panel after the race information was edited.
        /// </summary>
        public void RaceInfoChanged(bool structureChanged)
        {
            if (Race == null)
                return;

            if (structureChanged)
                Race.RemoveOrphanSplits();

            // The official times by distance follow the race (since 2026-10-19): the checkpoints at them not checked yet too (not when
            // the times do not fit it: nothing goes then, the panel says so).
            if (structureChanged)
                ReapplyOfficial();

            // The list of references depends on the event (style, distance, pool).
            EnsureReference();
            Changed(false);
            ScheduleSend();
            if (structureChanged)
                RefreshPbCandidates();
            if (form != null)
                form.RefreshReferences();
        }

        public void ManualValuesChanged()
        {
            Changed(false);
            ScheduleSend();
        }

        private void ScheduleSend()
        {
            if (!Settings.AutoSend || !Settings.IsConfigured)
                return;

            sendTimer.Stop();
            sendTimer.Start();
        }

        private void SendTimer_Tick(object sender, EventArgs e)
        {
            sendTimer.Stop();
            SendNow(false);
        }

        private SwimRace CreateDefaultRace()
        {
            SwimRace race = new SwimRace();
            race.Checkpoints = SwimPresets.DefaultCheckpoints(race.Distance, race.Pool, race.Relay);
            return race;
        }
        #endregion

        #region Athletes
        /// <summary>
        /// Download the athlete list from the Google Sheet (in the background).
        /// </summary>
        public void RefreshAthletes()
        {
            if (!Settings.IsConfigured)
            {
                AthletesMessage = SwimLang.T("Ρύθμισε πρώτα τη σύνδεση (καρτέλα Σύνδεση) για να φορτωθεί η λίστα αθλητών.");
                if (form != null)
                    form.RefreshAthletes();
                return;
            }

            if (AthletesLoading)
                return;

            AthletesLoading = true;
            AthletesMessage = SwimLang.T("Φόρτωση λίστας αθλητών…");
            if (form != null)
                form.RefreshAthletes();

            client.GetAthletes(Settings.WebAppUrl, Settings.Token, result =>
            {
                AthletesLoading = false;
                if (result.Ok && result.Raw != null)
                {
                    SwimAthleteList list = new SwimAthleteList();
                    list.Updated = DateTime.Now;
                    list.Sheet = result.Raw.Value<string>("sheet") ?? "";
                    JArray array = result.Raw["athletes"] as JArray;
                    if (array != null)
                    {
                        foreach (JToken token in array)
                        {
                            JObject o = token as JObject;
                            if (o == null)
                                continue;

                            SwimAthlete a = new SwimAthlete();
                            a.Id = o.Value<string>("id") ?? "";
                            a.Name = o.Value<string>("name") ?? "";
                            a.Gender = o.Value<string>("gender") ?? "";
                            a.BirthYear = o.Value<string>("birthYear") ?? "";
                            a.Category = o.Value<string>("category") ?? "";
                            a.Club = o.Value<string>("club") ?? "";
                            a.International = o.Value<bool?>("intl") ?? false;
                            a.Nation = o.Value<string>("nation");
                            a.WaId = o.Value<string>("waId");
                            JArray aliases = o["aliases"] as JArray;
                            if (aliases != null && aliases.Count > 0)
                                a.Aliases = aliases.Select(x => (string)x).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                            list.Athletes.Add(a);
                        }
                    }

                    list.BuildIndex();
                    Athletes = list;
                    Athletes.Save();
                    AthletesMessage = AthletesSummary();
                    if (list.Athletes.Count == 0)
                        AthletesMessage = SwimLang.T("Δεν βρέθηκαν αθλητές: το Google Sheet χρειάζεται φύλλο με στήλες unique_id και ΟΝΟΜΑΤΕΠΩΝΥΜΟ.");
                }
                else
                {
                    AthletesMessage = SwimLang.T("Η λίστα αθλητών δεν ενημερώθηκε: ") + result.Error + " " + AthletesSummary();
                }

                if (form != null)
                    form.RefreshAthletes();
            });
        }

        /// <summary>
        /// Refresh the list when the panel opens if it is older than a few hours.
        /// </summary>
        public void RefreshAthletesIfStale()
        {
            if (Settings.IsConfigured && (Athletes.Athletes.Count == 0 || (DateTime.Now - Athletes.Updated).TotalHours > 6))
                RefreshAthletes();
        }

        private string AthletesSummary()
        {
            if (Athletes == null || Athletes.Athletes.Count == 0)
                return SwimLang.T("Η λίστα αθλητών δεν έχει φορτωθεί.");

            return string.Format(SwimLang.T("Λίστα: {0} αθλητές (ενημέρωση {1:dd/MM HH:mm})."), Athletes.Athletes.Count, Athletes.Updated);
        }

        /// <summary>
        /// Link the current analysis to an athlete of the registry.
        /// </summary>
        public void SetAthlete(SwimAthlete athlete)
        {
            if (Race == null || athlete == null)
                return;

            Race.AthleteId = athlete.Id;
            Race.Athlete = athlete.Name;
            Race.AthleteGender = athlete.Gender;
            Race.AthleteBirthYear = athlete.BirthYear;
            Race.AthleteCategory = athlete.Category;
            Race.AthleteClub = athlete.Club;
            EnsureReference();
            Status(string.Format(SwimLang.T("Αθλητής: {0} (μητρώο {1})."), athlete.Name, athlete.Id), false);
            Changed(false);
            ScheduleSend();
            RefreshPbCandidates();
            if (form != null)
                form.RefreshAll();
        }

        public void ClearAthlete()
        {
            if (Race == null)
                return;

            Race.AthleteId = "";
            Race.AthleteGender = "";
            Race.AthleteBirthYear = "";
            Race.AthleteCategory = "";
            Race.AthleteClub = "";
            Changed(false);
            ScheduleSend();
            if (form != null)
                form.RefreshAll();
        }
        #endregion

        #region Elite references
        /// <summary>
        /// References of the event of the current race (and of the gender of the athlete when known); since 2.8.0 also the other races
        /// of the event: the other swimmers of this video, the athlete's other analyses and other athletes' analyses (Google Sheet).
        /// </summary>
        public List<SwimReference> ReferencesForRace()
        {
            List<SwimReference> list = References.For(Race);
            foreach (SwimReference r in Finals.ReferencesFor(Race, Settings.FinalsSet).Concat(RaceReferences()))
            {
                if (!list.Any(x => x.Id == r.Id))
                    list.Add(r);
            }

            return list;
        }

        /// <summary>The final of the event of the race (same style, distance and gender, 50 m pool), null if none.</summary>
        public SwimFinal FinalForRace()
        {
            return Finals.For(Race, Settings.FinalsSet);
        }

        /// <summary>
        /// (2.9.8) The set of the finals the races are compared with (its Short; empty: automatic, the one of the competition of the
        /// race, else the first set).
        /// </summary>
        public void SetFinalsSet(string shortName)
        {
            shortName = shortName ?? "";
            if (Settings.FinalsSet == shortName)
                return;

            Settings.FinalsSet = shortName;
            Settings.Save();
            if (Race != null && EnsureReference())
                Changed(false);
            if (form != null)
            {
                form.RefreshReferences();
                form.RefreshAll();
            }

            InvalidatePlayer();
        }

        /// <summary>A reference of the sheet, of this computer or one of the swimmers of the finals.</summary>
        private SwimReference FindReference(string id)
        {
            return References.Find(id) ?? Finals.FindReference(id);
        }

        /// <summary>The finals of a file (the analyses of an Olympic final): kept on this computer for the comparisons.</summary>
        public void ImportFinals(string path)
        {
            string error;
            SwimFinals finals = SwimFinals.Import(path, out error);
            if (finals == null)
            {
                Status(SwimLang.T("Οι τελικοί δεν φορτώθηκαν: ") + error, true);
                return;
            }

            Finals = finals;
            if (Race != null && EnsureReference())
                Changed(false);
            SwimFinal final = FinalForRace();
            SwimFinals set = finals.Imported ?? finals;
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("🏅 {0}: {1} τελικοί, {2} κολυμβητές.{3}"), set.Source, set.Finals.Count, set.SwimmerCount,
                final != null ? SwimLang.T(" Αυτός ο αγώνας συγκρίνεται με τον τελικό ") + final.Label + "." :
                Race != null && SwimReference.NormalizeGender(Race.AthleteGender).Length == 0 ? SwimLang.T(" Για τη σύγκριση χρειάζεται το φύλο του αθλητή.") : ""), false);
            if (form != null)
            {
                form.RefreshReferences();
                form.RefreshAll();
            }

            InvalidatePlayer();
        }

        /// <summary>
        /// Download the references of the sheet "Elite αναφορές" (in the background).
        /// </summary>
        public void RefreshReferences()
        {
            if (!Settings.IsConfigured)
            {
                ReferencesMessage = SwimLang.T("Ρύθμισε πρώτα τη σύνδεση (καρτέλα Σύνδεση) για να φορτωθούν οι elite αναφορές.");
                if (form != null)
                    form.RefreshReferences();
                return;
            }

            if (ReferencesLoading)
                return;

            ReferencesLoading = true;
            ReferencesMessage = SwimLang.T("Φόρτωση elite αναφορών…");
            if (form != null)
                form.RefreshReferences();

            client.GetReferences(Settings.WebAppUrl, Settings.Token, result =>
            {
                ReferencesLoading = false;
                if (result.Ok && result.Raw != null)
                {
                    List<SwimReference> list = new List<SwimReference>();
                    JArray array = result.Raw["references"] as JArray;
                    if (array != null)
                    {
                        foreach (JToken token in array)
                        {
                            SwimReference reference = SwimReference.FromJson(token as JObject);
                            if (reference != null && reference.Times.Count > 0)
                                list.Add(reference);
                        }
                    }

                    References.ReplaceFromSheet(list);
                    References.Save();
                    UpdateRaceReferences();

                    JArray skipped = result.Raw["skipped"] as JArray;
                    ReferencesMessage = ReferencesSummary();
                    if (skipped != null && skipped.Count > 0)
                        ReferencesMessage += string.Format(SwimLang.T(" ⚠ {0} γραμμές του φύλλου δεν διαβάστηκαν (π.χ. {1})."), skipped.Count, skipped[0]);

                    UploadLocalReferences();
                }
                else if (IsUnknownAction(result))
                {
                    ReferencesMessage = SwimLang.T("Για τις elite αναφορές χρειάζεται η νέα έκδοση του Apps Script (1.2.0): δες τον οδηγό. ") + ReferencesSummary();
                }
                else
                {
                    ReferencesMessage = SwimLang.T("Οι elite αναφορές δεν ενημερώθηκαν: ") + result.Error + " " + ReferencesSummary();
                }

                if (form != null)
                    form.RefreshReferences();
            });
        }

        public void RefreshReferencesIfStale()
        {
            if (Settings.IsConfigured && (References.References.Count == 0 || (DateTime.Now - References.Updated).TotalHours > 6))
                RefreshReferences();
        }

        /// <summary>
        /// Compare the current race with a reference (null: no comparison). The choice is remembered for the event.
        /// </summary>
        public void SetReference(SwimReference reference)
        {
            if (Race == null)
                return;

            Race.Reference = reference != null ? reference.Copy() : null;
            string key = EventKey(Race);
            // "No comparison" is a choice too (an empty id): the winner of the final does not come back for this event. Another race
            // (of this video, of the athlete or of another athlete) is for this race only.
            if (reference == null || !reference.IsRace)
                Settings.ReferenceByEvent[key] = reference != null ? reference.Id : "";

            Settings.Save();
            Status(reference != null ? SwimLang.T("Σύγκριση με: ") + reference.Label : SwimLang.T("Χωρίς σύγκριση με αναφορά."), false);
            Changed(false);
            ScheduleSend();
            if (form != null)
                form.RefreshReferences();
            InvalidatePlayer();
        }

        /// <summary>
        /// Keep the analysis as an elite reference for future comparisons (this computer + Google Sheet).
        /// </summary>
        public void SaveAsReference(string name)
        {
            if (Race == null)
                return;

            SwimReference reference = SwimReference.FromRace(Race, Computed, name);
            if (reference.Times.Count == 0)
            {
                Status(SwimLang.T("Η ανάλυση δεν έχει ακόμη χρόνους περασμάτων για να γίνει αναφορά."), true);
                return;
            }

            reference.Local = true;
            References.AddOrReplace(reference);
            References.Save();
            if (form != null)
                form.RefreshReferences();

            if (!Settings.IsConfigured)
            {
                Status(SwimLang.T("Η αναφορά «") + reference.Name + SwimLang.T("» αποθηκεύτηκε σε αυτόν τον υπολογιστή."), false);
                return;
            }

            Status(SwimLang.T("Αποστολή της αναφοράς «") + reference.Name + SwimLang.T("» στο Google Sheet…"), false);
            UploadReference(reference, true);
        }

        public static string ReferenceSheetName
        {
            get { return "Elite αναφορές"; }
        }

        private void UploadReference(SwimReference reference, bool report)
        {
            client.SaveReference(Settings.WebAppUrl, Settings.Token, reference, result =>
            {
                if (result.Ok)
                {
                    reference.Local = false;
                    References.Save();
                    if (report)
                        Status(SwimLang.T("✔ Η αναφορά «") + reference.Name + SwimLang.T("» μπήκε στο φύλλο «") + ReferenceSheetName + SwimLang.T("» του Google Sheet."), false);
                }
                else if (report)
                {
                    if (IsUnknownAction(result))
                        Status(SwimLang.T("Η αναφορά κρατήθηκε σε αυτόν τον υπολογιστή. Για να μπει και στο Google Sheet χρειάζεται η νέα έκδοση του Apps Script (1.2.0)."), true);
                    else
                        Status(SwimLang.T("Η αναφορά κρατήθηκε σε αυτόν τον υπολογιστή: ") + result.Error, true);
                }

                if (form != null)
                    form.RefreshReferences();
            });
        }

        /// <summary>
        /// References saved on this computer while the sheet was not reachable.
        /// </summary>
        private void UploadLocalReferences()
        {
            foreach (SwimReference reference in References.References.Where(r => r.Local).ToList())
                UploadReference(reference, false);
        }

        /// <summary>
        /// After a download: refresh the copies of the references used by the races of this video.
        /// </summary>
        private void UpdateRaceReferences()
        {
            if (VideoFile == null)
                return;

            bool changed = false;
            foreach (SwimRace race in VideoFile.Races)
            {
                if (race.Reference == null)
                    continue;

                SwimReference fresh = FindReference(race.Reference.Id);
                if (fresh != null)
                {
                    race.Reference = fresh.Copy();
                    changed = true;
                }
            }

            if (EnsureReference())
                changed = true;

            if (changed)
                Changed(false);
        }

        /// <summary>
        /// When the event of the race changes, use the reference chosen last time for this event (if any); when none was ever chosen
        /// for the event, the winner of its final (e.g. the Olympic champion) once the finals are loaded.
        /// Returns true if the reference of the race changed.
        /// </summary>
        private bool EnsureReference()
        {
            if (Race == null)
                return false;

            // A race chosen to compare with (another swimmer of the video, an analysis of the Google Sheet) stays, of either gender.
            if (Race.Reference != null && Race.Reference.Matches(Race) && (Race.Reference.IsRace || Race.Reference.GenderMatches(Race.AthleteGender)))
                return false;

            SwimReference remembered = null;
            string id;
            bool chosen = Settings.ReferenceByEvent.TryGetValue(EventKey(Race), out id);
            if (chosen && !string.IsNullOrEmpty(id))
                remembered = FindReference(id);

            if (remembered != null && (!remembered.Matches(Race) || !remembered.GenderMatches(Race.AthleteGender)))
                remembered = null;

            if (remembered == null && !chosen && Finals != null)
                remembered = Finals.ReferencesFor(Race, Settings.FinalsSet).FirstOrDefault();

            SwimReference before = Race.Reference;
            Race.Reference = remembered != null ? remembered.Copy() : null;
            return before != null || Race.Reference != null;
        }

        private static string EventKey(SwimRace race)
        {
            return string.Join("|", new string[] { race.Style, race.DistanceLabel, race.Pool.ToString(CultureInfo.InvariantCulture) + (race.IsYards ? "y" : ""), SwimReference.NormalizeGender(race.AthleteGender) });
        }

        private string ReferencesSummary()
        {
            if (References == null || References.References.Count == 0)
                return SwimLang.T("Δεν υπάρχουν ακόμη elite αναφορές.");

            int local = References.References.Count(r => r.Local);
            string text = string.Format(SwimLang.T("Elite αναφορές: {0}"), References.References.Count);
            if (References.Updated > DateTime.MinValue)
                text += string.Format(SwimLang.T(" (ενημέρωση {0:dd/MM HH:mm})"), References.Updated);
            if (local > 0)
                text += string.Format(SwimLang.T(", {0} μόνο σε αυτόν τον υπολογιστή"), local);
            return text + ".";
        }

        private static bool IsUnknownAction(SwimSheetResult result)
        {
            return result != null && !result.Ok && result.Error != null && result.Error.StartsWith("Άγνωστη ενέργεια", StringComparison.Ordinal);
        }

        /// <summary>
        /// Redraw the video (overlay options changed).
        /// </summary>
        public void RefreshOverlay()
        {
            InvalidatePlayer();
        }
        #endregion

        #region Sheet
        public void SaveSettings()
        {
            Settings.Save();
            if (Settings.IsConfigured)
            {
                if (SyncState == SwimSyncState.NotConfigured)
                {
                    SyncState = SwimSyncState.Idle;
                    SyncMessage = SwimLang.T("Έτοιμο");
                }
            }
            else
            {
                SyncState = SwimSyncState.NotConfigured;
                SyncMessage = SwimLang.T("Δεν έχει ρυθμιστεί η σύνδεση με το Google Sheet");
            }

            Changed(false);
            if (form != null)
                form.RefreshSync();

            RefreshAthletesIfStale();
            RefreshReferencesIfStale();
        }

        public void TestConnection(Action<SwimSheetResult> callback)
        {
            if (string.IsNullOrWhiteSpace(Settings.WebAppUrl))
            {
                callback(new SwimSheetResult { Ok = false, Error = SwimLang.T("Συμπλήρωσε τη διεύθυνση της Εφαρμογής Ιστού.") });
                return;
            }

            client.Ping(Settings.WebAppUrl, Settings.Token, callback);
        }

        /// <summary>
        /// The send and PDF buttons: also retry the snapshots that could not be uploaded. The PDF waits for the graphics of the video
        /// (the summary card, the race against the final) that changed to go up first, then for its charts (drawCharts: the Apps
        /// Script sends the values of the charts without an image, Kinovea draws them, uploads them and asks for the PDF again).
        /// </summary>
        public void SendByUser(bool pdf)
        {
            // A final frame drawn in the other language (an analysis made before the switch of the language): drawn again in this
            // one at the same frame, the PDF after its upload.
            if (pdf && pdfAfterFinalFrame == null && Race != null && !Race.IsTraining && Race.FinalFrame != null && HasVideo &&
                Settings.IsConfigured && !ReadingBusy && (Race.FinalFrame.Lang ?? "el") != SwimLang.Code)
            {
                pdfAfterFinalFrame = Race.Id;
                RecaptureFinalFrame(false, true);
                if (FinalFrameUploading)
                    return;
                pdfAfterFinalFrame = null;
            }

            if (!(pdf && UploadReportGraphics(() => SendNow(true, true))))
                SendNow(pdf, pdf);
            RetryFailedUploads();
        }

        public void SendNow(bool pdf, bool drawCharts = false)
        {
            if (Race == null)
                return;

            if (!Settings.IsConfigured)
            {
                SyncState = SwimSyncState.NotConfigured;
                SyncMessage = SwimLang.T("Συμπλήρωσε διεύθυνση και token στην καρτέλα Σύνδεση.");
                if (form != null)
                    form.RefreshSync();
                return;
            }

            // (2.13.0) The program's automatic error lists of a training, up to date in the report.
            if (UpdateAutoEvals() > 0)
            {
                MarkDirty();
                if (form != null)
                    form.RefreshData();
            }

            long origin = frameServer != null && frameServer.Loaded ? frameServer.Metadata.TimeOrigin : 0;
            string body = SwimPayload.Build(Race, Computed, TechPhases, Timing, origin, Settings, VideoName, pdf ? "pdf" : "update", FinalForRace(), pdf && drawCharts,
                PackageFlags(Race));
            client.Enqueue(Settings.WebAppUrl, body, Race.Id, pdf);
        }

        /// <summary>
        /// Send another analysis of the video (e.g. after the upload of one of its snapshots).
        /// </summary>
        private void SendRace(SwimRace race, bool pdf, bool drawCharts = false)
        {
            if (race == null || !Settings.IsConfigured)
                return;

            if (race == Race)
            {
                SendNow(pdf, drawCharts);
                return;
            }

            SwimComputed computed = SwimCalculator.Compute(race, Timing, Settings.StrokeRounding);
            List<SwimTechPhase> tech = SwimTechCalculator.Compute(race, computed, Timing);
            long origin = frameServer != null && frameServer.Loaded ? frameServer.Metadata.TimeOrigin : 0;
            client.Enqueue(Settings.WebAppUrl, SwimPayload.Build(race, computed, tech, Timing, origin, Settings, VideoName, pdf ? "pdf" : "update",
                Finals != null ? Finals.For(race, Settings.FinalsSet) : null, pdf && drawCharts, PackageFlags(race)), race.Id, pdf);
        }

        public void OpenSheet()
        {
            if (Race == null)
                return;

            if (string.IsNullOrEmpty(Race.SheetUrl))
            {
                Status(SwimLang.T("Το φύλλο δεν έχει δημιουργηθεί ακόμη. Πάτησε Αποστολή τώρα."), true);
                return;
            }

            OpenUrl(Race.SheetUrl);
        }

        private void Client_StateChanged(object sender, SwimSyncEventArgs e)
        {
            SyncState = e.State;
            SyncMessage = e.Message;

            // (2.10.0) The package for the coaches waits for the PDF of this race: an error of the sending is said in its window.
            PackageSyncFailed(e);

            // An older Apps Script still computes the stroke rate and length in the last 5 m and over whole 50s.
            Version backend;
            string version = e.Result != null && e.Result.Ok && e.Result.Raw != null ? e.Result.Raw.Value<string>("version") : null;
            if (!backendWarned && !string.IsNullOrEmpty(version) && Version.TryParse(version, out backend) && backend < minimumBackend)
            {
                backendWarned = true;
                Status(string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("Το Apps Script του KOE App είναι η έκδοση {0}: το report δεν έχει ακόμη τις πισίνες σε γιάρδες, τον ΔΑΚ, τις συγκρίσεις και την τεχνική ανάλυση. Βάλε την έκδοση {1} (δες τον οδηγό)."),
                    version, minimumBackend.ToString(3)), true);
            }
            else if (!chartsWarned && !string.IsNullOrEmpty(version) && Version.TryParse(version, out backend) && backend < chartsBackend &&
                Race != null && !Race.IsTraining)
            {
                // The code saved in Apps Script runs only after a new version of the deployment: the web app is still the old one.
                chartsWarned = true;
                Status(string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("Η Εφαρμογή Ιστού του Google τρέχει το Apps Script {0}: τα γραφήματα του PDF βγαίνουν άδεια. Βάλε την έκδοση {1} και κάνε ") +
                    SwimLang.T("«Ανάπτυξη → Διαχείριση αναπτύξεων → ✏️ → Έκδοση: Νέα έκδοση → Ανάπτυξη» (δες τον οδηγό)."),
                    version, chartsBackend.ToString(3)), true);
            }
            else if (!languageWarned && SwimLang.English && !string.IsNullOrEmpty(version) && Version.TryParse(version, out backend) && backend < languageBackend)
            {
                languageWarned = true;
                Status(string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("Η Εφαρμογή Ιστού τρέχει το Apps Script {0}: το report και το PDF βγαίνουν στα ελληνικά. Για τα αγγλικά βάλε την έκδοση {1} (δες τον οδηγό, «Ενημέρωση του script»)."),
                    version, languageBackend.ToString(3)), true);
            }
            else if (!typedStrokesWarned && !string.IsNullOrEmpty(version) && Version.TryParse(version, out backend) && backend < typedStrokesBackend &&
                Race != null && Race.StrokeCounts != null && Race.StrokeCounts.Count > 0)
            {
                typedStrokesWarned = true;
                Status(string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("Το Apps Script του KOE App είναι η έκδοση {0}: στο Sheet οι χεριές βγαίνουν από τα σημάδια, όχι από τους αριθμούς που επιβεβαιώνεις σε κάθε πέρασμα. Βάλε την έκδοση {1} (δες τον οδηγό)."),
                    version, typedStrokesBackend.ToString(3)), true);
            }

            if (e.Result != null && e.Result.Ok && e.State == SwimSyncState.Synced)
                UploadPendingSnapshots();

            if (e.Result != null && e.Result.Ok && VideoFile != null)
            {
                SwimRace race = VideoFile.Races.FirstOrDefault(r => r.Id == e.RaceId);
                if (race != null)
                {
                    if (!string.IsNullOrEmpty(e.Result.SheetName))
                        race.SheetName = e.Result.SheetName;
                    if (!string.IsNullOrEmpty(e.Result.SheetUrl))
                        race.SheetUrl = e.Result.SheetUrl;
                    if (!string.IsNullOrEmpty(e.Result.PdfUrl))
                    {
                        race.LastPdfUrl = e.Result.PdfUrl;

                        // (2.10.0) The PDF of a package goes into its folder (the preview shows it), else it opens as always; the line of
                        // the KOE App («Αρχεία Αθλητών/τριων») in the status.
                        if (!PackageTakesPdf(race, e.Result))
                        {
                            OpenUrl(e.Result.PdfUrl);
                            // (2.12.0) The short analysis in its own file: it opens too.
                            string shortUrl = PdfShortUrl(e.Result);
                            if (!string.IsNullOrEmpty(shortUrl))
                                OpenUrl(shortUrl);
                        }
                        ArchiveStatus(race, e.Result);

                        // An older Apps Script made the PDF with the charts of Google: they come out empty.
                        if (!chartsWarned && !string.IsNullOrEmpty(version) && Version.TryParse(version, out backend) && backend < chartsBackend)
                        {
                            chartsWarned = true;
                            Status(string.Format(CultureInfo.InvariantCulture,
                                SwimLang.T("Το PDF το έφτιαξε το Apps Script {0}: τα γραφήματα βγαίνουν άδεια. Βάλε την έκδοση {1} και κάνε «Διαχείριση αναπτύξεων → Νέα έκδοση» (δες τον οδηγό)."),
                                version, chartsBackend.ToString(3)), true);
                        }
                        else if (!quickLookWarned && !string.IsNullOrEmpty(version) && Version.TryParse(version, out backend) && backend < quickLookBackend)
                        {
                            // Since 2.8.0 the PDF starts with a page to read at a glance (Apps Script 2.8.0).
                            quickLookWarned = true;
                            Status(string.Format(CultureInfo.InvariantCulture,
                                SwimLang.T("Το PDF το έφτιαξε το Apps Script {0}: χωρίς την πρώτη σελίδα «Με μια ματιά». Βάλε την έκδοση {1} (δες τον οδηγό, «Ενημέρωση του script»)."),
                                version, quickLookBackend.ToString(3)), true);
                        }
                        else if (!pdfChartsWarned && !string.IsNullOrEmpty(version) && Version.TryParse(version, out backend) && backend < pdfChartsBackend)
                        {
                            // Since 2.9.5 every chart of the PDF prints and the ΔΑΚ of each 50 shows (Apps Script 2.9.5).
                            pdfChartsWarned = true;
                            Status(string.Format(CultureInfo.InvariantCulture,
                                SwimLang.T("Το PDF το έφτιαξε το Apps Script {0}: κάποια γραφήματα μπορεί να βγουν άδεια και ο ΔΑΚ μόνο ως μέσος όρος. Βάλε την έκδοση {1} (δες τον οδηγό, «Ενημέρωση του script»)."),
                                version, pdfChartsBackend.ToString(3)), true);
                        }
                        else if (Settings.PdfShort && !pdfPartsWarned && !string.IsNullOrEmpty(version) && Version.TryParse(version, out backend) && backend < pdfPartsBackend)
                        {
                            // Since 2.11.0 the PDF can have the short analysis (Apps Script 2.11.0).
                            pdfPartsWarned = true;
                            Status(string.Format(CultureInfo.InvariantCulture,
                                SwimLang.T("Το PDF το έφτιαξε το Apps Script {0}: χωρίς τη σύντομη ανάλυση. Βάλε την έκδοση {1} (δες τον οδηγό, «Ενημέρωση του script»)."),
                                version, pdfPartsBackend.ToString(3)), true);
                        }
                        else if (Settings.PdfSeparate && Settings.PdfShort && Settings.PdfFull && !pdfSeparateWarned && !string.IsNullOrEmpty(version) && Version.TryParse(version, out backend) && backend < pdfSeparateBackend)
                        {
                            // Since 2.12.0 the two parts can come in two files (Apps Script 2.12.0); an older one makes one PDF.
                            pdfSeparateWarned = true;
                            Status(string.Format(CultureInfo.InvariantCulture,
                                SwimLang.T("Το PDF το έφτιαξε το Apps Script {0}: η σύντομη και η πλήρης ανάλυση σε ένα αρχείο. Βάλε την έκδοση {1} (δες τον οδηγό, «Ενημέρωση του script»)."),
                                version, pdfSeparateBackend.ToString(3)), true);
                        }
                        else if (e.Result.Raw != null && e.Result.Raw.Value<bool?>("pdfShortFailed") == true && pdfShortFailed.Add(race.Id ?? ""))
                        {
                            // The three pages did not fit (or the copy of the sheet failed): the full analysis only. Said once a race.
                            Status(SwimLang.T("Η σύντομη ανάλυση δεν έγινε σε δικό της αρχείο: μόνο το PDF της πλήρους ανάλυσης."), true);
                        }
                        else if (Settings.PdfShort && PdfPartMade(e.Result, "short") == false && pdfShortFailed.Add(race.Id ?? ""))
                        {
                            // The three pages could not be made (they did not fit, or the copy of the sheet): the full analysis. Said once a race.
                            Status(SwimLang.T("Η σύντομη ανάλυση δεν έγινε: το PDF έχει την πλήρη ανάλυση."), true);
                        }
                        else if (e.Result.PdfQuick == false && quickLookFailed.Add(race.Id ?? ""))
                        {
                            // The Apps Script could not make the first page (the copy of the sheet): the whole report as before. Said once a race.
                            Status(SwimLang.T("Το PDF βγήκε χωρίς την πρώτη σελίδα «Με μια ματιά»: η πλήρης αναφορά όπως πριν."), true);
                        }
                    }

                    MarkDirty();

                    // The Apps Script waits for the charts of the PDF: drawn here with the graphics of the video, then the PDF.
                    if (e.Result.Raw != null && e.Result.Raw.Value<bool?>("needCharts") == true)
                        DrawChartsForPdf(race, e.Result.Raw["charts"] as JArray);
                }
            }

            if (form != null)
                form.RefreshSync();
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch (Exception e)
            {
                log.Error("Could not open url.", e);
            }
        }
        #endregion

        #region Attachment, persistence, refresh
        private void Attach(PlayerScreenUserInterface player, FrameServerPlayer frameServer)
        {
            if (player == null || frameServer == null || !frameServer.Loaded)
                return;

            string path = frameServer.VideoReader.FilePath;
            bool sameVideo = this.player == player && string.Equals(videoPath, path, StringComparison.OrdinalIgnoreCase) && VideoFile != null;

            // A pending click belongs to the previous video.
            if (!sameVideo && PickTarget != SwimPickTarget.None)
            {
                pendingMeasure = SwimPickTarget.None;
                calibrationClicks.Clear();
                SetPick(SwimPickTarget.None);
            }

            this.player = player;
            this.frameServer = frameServer;
            RefreshTiming();

            if (sameVideo)
                return;

            lastMeasurement = null;
            lastRegion = null;
            SaveNow();
            videoPath = path;
            VideoFile = Load(path);
            Race = VideoFile.Races.FirstOrDefault(r => r.Id == VideoFile.ActiveRaceId) ?? VideoFile.Races.LastOrDefault();
            if (Race == null)
            {
                Race = CreateDefaultRace();
                VideoFile.Races.Add(Race);
            }

            VideoFile.ActiveRaceId = Race.Id;
            EnsureReference();
            ClearUndo();
            Recompute();
            string check = CheckOnOpen();
            if (form != null)
                form.RefreshAll();
            if (check.Length > 0)
                Status(check, true);
        }

        /// <summary>
        /// (2.9.5b) A race opened: the breakouts of the auto pilot not checked yet that the times put past the most a breakout can be
        /// for the style go (an analysis of an earlier version), and the underwater distances past it are named (they stay). The text
        /// for the status ("" if nothing).
        /// </summary>
        private string CheckOnOpen()
        {
            string gone = CheckPilotBreakouts();
            if (gone.Length > 0)
            {
                Recompute();
                MarkDirty();
            }

            string far = UnderwaterWarnings();
            return gone + (gone.Length > 0 && far.Length > 0 ? "  " : "") + far;
        }

        private void RefreshTiming()
        {
            if (frameServer == null || !frameServer.Loaded)
                return;

            Timing.AverageTimestampsPerFrame = frameServer.VideoReader.Info.AverageTimeStampsPerFrame;
            Timing.FrameIntervalMilliseconds = frameServer.VideoReader.Info.FrameIntervalMilliseconds;
            Timing.HighSpeedFactor = frameServer.Metadata.HighSpeedFactor;
        }

        private void Recompute()
        {
            RefreshTiming();
            Computed = SwimCalculator.Compute(Race, Timing, Settings.StrokeRounding);
            TechPhases = SwimTechCalculator.Compute(Race, Computed, Timing);
            DataVersion++;
        }

        private void Changed(bool send)
        {
            Recompute();
            MarkDirty();

            if (form != null)
                form.RefreshData();

            if (send && Settings.AutoSend && Settings.IsConfigured)
            {
                sendTimer.Stop();
                SendNow(false);
            }
        }

        private void Status(string message, bool warning)
        {
            LastStatus = message;
            LastStatusIsWarning = warning;
            if (form != null)
                form.ShowStatus(message, warning);
        }

        private void MarkDirty()
        {
            dirty = true;
            saveTimer.Stop();
            saveTimer.Start();
        }

        private void SaveTimer_Tick(object sender, EventArgs e)
        {
            saveTimer.Stop();
            SaveNow();
        }

        public void SaveNow()
        {
            if (!dirty || VideoFile == null || string.IsNullOrEmpty(videoPath))
                return;

            string json = JsonConvert.SerializeObject(VideoFile, Formatting.Indented);
            string primary = videoPath + ".swim.json";
            try
            {
                File.WriteAllText(primary, json, Encoding.UTF8);
                dirty = false;
                return;
            }
            catch (Exception e)
            {
                log.Warn("Could not save swim analysis next to the video, using the settings folder. " + e.Message);
            }

            try
            {
                string fallback = FallbackPath(videoPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fallback));
                File.WriteAllText(fallback, json, Encoding.UTF8);
                dirty = false;
            }
            catch (Exception e)
            {
                log.Error("Could not save swim analysis.", e);
                Status(SwimLang.T("Δεν ήταν δυνατή η αποθήκευση της ανάλυσης στον δίσκο: ") + e.Message, true);
            }
        }

        private SwimVideoFile Load(string path)
        {
            foreach (string candidate in new string[] { path + ".swim.json", FallbackPath(path) })
            {
                try
                {
                    if (!File.Exists(candidate))
                        continue;

                    SwimVideoFile file = JsonConvert.DeserializeObject<SwimVideoFile>(File.ReadAllText(candidate, Encoding.UTF8));
                    if (file != null && file.Races != null)
                    {
                        file.Races.RemoveAll(r => r == null);
                        foreach (SwimRace race in file.Races)
                            race.EnsureLists();
                        file.Version = SwimVideoFile.CurrentVersion;
                        file.VideoPath = path;
                        if (file.Calibrations == null)
                            file.Calibrations = new List<SwimCalibration>();
                        file.Calibrations.RemoveAll(c => c == null);
                        Status(string.Format(SwimLang.T("Φορτώθηκαν {0} αναλύσεις για αυτό το βίντεο."), file.Races.Count), false);
                        return file;
                    }
                }
                catch (Exception e)
                {
                    log.Error("Could not read swim analysis file " + candidate, e);
                }
            }

            SwimVideoFile created = new SwimVideoFile();
            created.VideoPath = path;
            return created;
        }

        private static string FallbackPath(string path)
        {
            string name = Path.GetFileName(path);
            string hash = ((uint)path.ToLowerInvariant().GetHashCode()).ToString("x8", CultureInfo.InvariantCulture);
            return Path.Combine(Software.SettingsDirectory, "SwimAnalysis", name + "." + hash + ".swim.json");
        }

        private void EnsureForm(bool activate)
        {
            if (form == null || form.IsDisposed)
            {
                form = new FormSwimAnalysis(this);
                form.FormClosed += Form_FormClosed;
                Form owner = player != null && !player.IsDisposed ? player.FindForm() : null;
                form.ShowNear(owner);
                RefreshAthletesIfStale();
                RefreshReferencesIfStale();
                RefreshEliteFramesIfStale();
                RefreshPbCandidates();
                UploadPendingSnapshots();
                InvalidatePlayer();
                return;
            }

            if (activate)
                form.Activate();
        }

        private void Form_FormClosed(object sender, FormClosedEventArgs e)
        {
            SaveNow();
            Settings.Save();
            form = null;

            // Closed to open again in the other language: the analysis stays as it is.
            if (reopeningForm)
                return;

            // Without visible bars on the video, closing the window ends the analysis on the screen (graphics, clicks), and the
            // auto pilot with it; not in full screen, where the round button stays.
            if ((!Settings.VideoBars || !BarsVisible) && !DialShown)
            {
                analysisOn = false;
                StopPilot();
            }

            UpdateBars();

            if (PickTarget != SwimPickTarget.None && !Active)
            {
                pendingMeasure = SwimPickTarget.None;
                calibrationClicks.Clear();
                SetPick(SwimPickTarget.None);
            }

            InvalidatePlayer();
        }
        #endregion
    }
}
