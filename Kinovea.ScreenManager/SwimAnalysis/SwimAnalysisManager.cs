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
        public SwimRace Race { get; private set; }
        public SwimComputed Computed { get; private set; }
        public SwimTiming Timing { get; private set; }
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
        private PlayerScreenUserInterface player;
        private FrameServerPlayer frameServer;
        private string videoPath;
        private FormSwimAnalysis form;
        private SwimSheetClient client;

        /// <summary>Oldest Apps Script with the reports of this version (pools in yards, ΔΑΚ, comparisons, technical analysis).</summary>
        private static readonly Version minimumBackend = new Version(2, 0, 0);
        private bool backendWarned;
        private List<string> undoStack = new List<string>();
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
            Athletes = SwimAthleteList.Load();
            References = SwimReferenceList.Load();
            if (Settings.ReferenceByEvent == null)
                Settings.ReferenceByEvent = new Dictionary<string, string>();
            ReferencesMessage = ReferencesSummary();
            AthletesMessage = AthletesSummary();
            Timing = new SwimTiming();
            Computed = new SwimComputed();
            CalibrationLine1 = 0;
            CalibrationLine2 = 5;
            SyncState = Settings.IsConfigured ? SwimSyncState.Idle : SwimSyncState.NotConfigured;
            SyncMessage = Settings.IsConfigured ? "Έτοιμο" : "Δεν έχει ρυθμιστεί η σύνδεση με το Google Sheet";

            client = new SwimSheetClient();
            client.StateChanged += Client_StateChanged;

            saveTimer = new Timer();
            saveTimer.Interval = 800;
            saveTimer.Tick += SaveTimer_Tick;

            // Edits of text fields are sent after a short pause instead of at every keystroke.
            sendTimer = new Timer();
            sendTimer.Interval = 1500;
            sendTimer.Tick += SendTimer_Tick;
        }

        #region Entry points from the player screen
        public void ShowWindow(PlayerScreenUserInterface player, FrameServerPlayer frameServer)
        {
            Attach(player, frameServer);
            EnsureForm(true);
        }

        /// <summary>
        /// Handles a Swim* command from the player screen at the given video timestamp.
        /// </summary>
        public bool HandleCommand(PlayerScreenUserInterface player, FrameServerPlayer frameServer, PlayerScreenCommands command, long timestamp)
        {
            Attach(player, frameServer);
            if (Race == null)
                return false;

            switch (command)
            {
                case PlayerScreenCommands.SwimShowPanel:
                    EnsureForm(true);
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
                        Status("Βίντεο προπόνησης: W για την επαφή στον τοίχο (στροφή), T για τα 5/10/15 και G για νέα εκκίνηση.", true);
                        return true;
                    }

                    AddSplit(timestamp);
                    break;
                case PlayerScreenCommands.SwimStroke:
                    AddMark(SwimMarkType.Stroke, timestamp, "Χεριά");
                    break;
                case PlayerScreenCommands.SwimKick:
                    AddMark(SwimMarkType.Kick, timestamp, "Δελφινισμός");
                    break;
                case PlayerScreenCommands.SwimBreakout:
                    AddMark(SwimMarkType.Breakout, timestamp, "Έξοδος από το νερό");
                    break;
                case PlayerScreenCommands.SwimFeetOff:
                    AddMark(SwimMarkType.FeetOff, timestamp, "Αποκόλληση ποδιών");
                    break;
                case PlayerScreenCommands.SwimEntry:
                    AddMark(SwimMarkType.Entry, timestamp, "Είσοδος χεριών στο νερό");
                    break;
                case PlayerScreenCommands.SwimReaction:
                    AddMark(SwimMarkType.Reaction, timestamp, "Αντίδραση");
                    break;
                case PlayerScreenCommands.SwimUndo:
                    Undo();
                    break;
                default:
                    if (!HandleTechCommand(command, timestamp))
                        return false;
                    break;
            }

            EnsureForm(false);
            if (command != PlayerScreenCommands.SwimSnapshot && command != PlayerScreenCommands.SwimAngle)
                Changed(true);

            // The panel follows the race: start, swimming, turn, finish (or the start and turn pages of the technical analysis).
            if (command != PlayerScreenCommands.SwimUndo && command != PlayerScreenCommands.SwimSnapshot && command != PlayerScreenCommands.SwimAngle && form != null)
            {
                if (Race.IsTraining || TechnicalView)
                    form.FollowTechPhase(SwimTechCalculator.PhaseAt(TechPhases, timestamp));
                else
                    form.FollowPhase(PhaseAt(timestamp));
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

            // Other swimmers analysed on the same video share the same start signal.
            foreach (SwimRace other in VideoFile.Races)
            {
                if (other != Race && (other.StartTimestamp == null || other.StartTimestamp == old))
                    other.StartTimestamp = timestamp;
            }

            Status(old == null ? "Σήμα εκκίνησης: t = 0 σε αυτό το καρέ." : "Το σήμα εκκίνησης άλλαξε. Όλοι οι χρόνοι υπολογίστηκαν ξανά.", false);
        }

        private bool EnsureStart()
        {
            if (Race.StartTimestamp != null)
                return true;

            Race.StartTimestamp = frameServer.Metadata.TimeOrigin;
            Status("Δεν είχε οριστεί σήμα εκκίνησης: χρησιμοποιήθηκε το σημείο μηδέν του Kinovea. Πάτησε Εκκίνηση στο καρέ του σήματος για να το αλλάξεις.", true);
            return false;
        }

        private void AddSplit(long timestamp)
        {
            bool hadStart = EnsureStart();
            int distance = Race.NextCheckpoint();
            string unit = Race.Unit;
            if (distance < 0)
            {
                SystemSounds.Beep.Play();
                Status("Όλα τα σημεία έχουν χρόνο. Διάγραψε έναν χρόνο (επιλογή γραμμής + Delete) για να τον ξαναμαρκάρεις.", true);
                return;
            }

            SwimMark mark = new SwimMark { Type = SwimMarkType.Split, Timestamp = timestamp, Distance = distance };
            Race.Marks.Add(mark);
            undoStack.Add(mark.Id);

            double t = Timing.Seconds(timestamp, Race.StartTimestamp.Value);
            string message = string.Format(CultureInfo.InvariantCulture, "Πέρασμα {0}{1}: {2:0.00} s", distance, unit, t);

            // Sanity check against the neighbour checkpoints.
            SwimMark previous = Race.Checkpoints.Where(d => d < distance).Select(d => Race.GetSplit(d)).LastOrDefault(m => m != null);
            SwimMark next = Race.Checkpoints.Where(d => d > distance).Select(d => Race.GetSplit(d)).FirstOrDefault(m => m != null);
            bool warn = (previous != null && previous.Timestamp >= timestamp) || (next != null && next.Timestamp <= timestamp);
            if (warn)
            {
                SystemSounds.Exclamation.Play();
                message += "  ⚠ ο χρόνος δεν είναι ανάμεσα στα γειτονικά σημεία.";
            }

            if (hadStart || warn)
                Status(message, warn);
        }

        private void AddMark(SwimMarkType type, long timestamp, string label)
        {
            // Two presses on the same frame (paused video, key repeat) are the same event, not two.
            if (Race.Marks.Any(m => m.Type == type && SameFrame(m.Timestamp, timestamp)))
            {
                SystemSounds.Beep.Play();
                Status(label + ": υπάρχει ήδη σε αυτό το καρέ (δεν καταχωρήθηκε δεύτερη φορά).", true);
                return;
            }

            bool hadStart = Race.IsTraining || EnsureStart();
            SwimMark mark = new SwimMark { Type = type, Timestamp = timestamp };
            Race.Marks.Add(mark);
            undoStack.Add(mark.Id);

            if (Race.IsTraining)
            {
                // Times of a training video are from the origin of the attempt (signal or contact with the wall).
                List<SwimTechPhase> phases = SwimTechCalculator.Compute(Race, Computed, Timing);
                SwimTechPhase phase = SwimTechCalculator.PhaseAt(phases, timestamp);
                if (type == SwimMarkType.WallContact)
                    Status(string.Format(CultureInfo.InvariantCulture, "{0}: Στροφή {1}.", label, phases.Count(p => p.Kind == SwimPhaseKind.Turn && p.Origin <= timestamp)), false);
                else if (phase != null)
                    Status(string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.00} s ({2})", label, Timing.Seconds(timestamp, phase.Origin), phase.Title), false);
                else
                    Status(label + ": πριν από την πρώτη προσπάθεια. Πάτησε το σήμα εκκίνησης (G) ή την επαφή στον τοίχο (W).", true);
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
                mark = Race.Marks.FirstOrDefault(m => m.Id == id);
            }

            if (mark == null)
                mark = Race.Marks.OrderBy(m => m.Timestamp).LastOrDefault();

            if (mark == null)
            {
                Status("Δεν υπάρχει κάτι για αναίρεση.", true);
                return;
            }

            Race.Marks.Remove(mark);
            Status("Αναιρέθηκε: " + MarkLabel(mark), false);
        }

        public void ClearSplit(int distance)
        {
            if (Race == null)
                return;

            int removed = Race.Marks.RemoveAll(m => m.Type == SwimMarkType.Split && m.Distance == distance);
            if (removed > 0)
            {
                Status(string.Format("Διαγράφηκε ο χρόνος στα {0}μ. Το επόμενο Πέρασμα θα συμπληρώσει αυτό το σημείο.", distance), false);
                Changed(true);
            }
        }

        public void ClearMarks(SwimMarkType type)
        {
            if (Race == null)
                return;

            Race.Marks.RemoveAll(m => m.Type == type);
            Changed(true);
        }

        public void Seek(int distance)
        {
            if (Race == null || !HasVideo)
                return;

            SwimMark split = Race.GetSplit(distance);
            if (split != null)
                player.ForcePosition(split.Timestamp, true);
        }

        private static string MarkLabel(SwimMark mark)
        {
            switch (mark.Type)
            {
                case SwimMarkType.Split: return "πέρασμα " + mark.Distance;
                case SwimMarkType.Stroke: return "χεριά";
                case SwimMarkType.Kick: return "δελφινισμός";
                case SwimMarkType.Breakout: return "έξοδος από το νερό";
                case SwimMarkType.FeetOff: return "αποκόλληση ποδιών";
                case SwimMarkType.Entry: return "είσοδος χεριών στο νερό";
                case SwimMarkType.Reaction: return "αντίδραση";
                case SwimMarkType.HandsOff: return "χέρια από τον βατήρα";
                case SwimMarkType.BackFoot: return "πίσω πόδι";
                case SwimMarkType.TechSplit: return "τεχνικό πέρασμα " + (mark.Distance > 0 && mark.Wall > 0 ? "+" : "") + mark.Distance;
                case SwimMarkType.WallContact: return "επαφή στον τοίχο";
                case SwimMarkType.RotationStart: return "αρχή περιστροφής";
                case SwimMarkType.Signal: return "σήμα εκκίνησης";
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
                if (split != null && split.Timestamp <= timestamp && d > last)
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
                    if (split != null && split.Timestamp <= now)
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
            return PickTarget != SwimPickTarget.None && screen == player && HasVideo && form != null && !form.IsDisposed;
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
                Status("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής.", true);
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
                Status("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής.", true);
                return;
            }

            int wall = 0;
            if (target == SwimPickTarget.TurnUnderwater)
            {
                wall = TurnWalls.Contains(wallHint) && TechnicalView ? wallHint : CurrentTurnWall;
                if (wall <= 0)
                    wall = TurnWalls.Contains(wallHint) ? wallHint : TurnWalls.FirstOrDefault();

                if (wall <= 0)
                {
                    Status("Αυτός ο αγώνας δεν έχει στροφές (απόσταση ίση με το μήκος της πισίνας).", true);
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
                    ? "Πρώτα βαθμονόμηση της πισίνας. "
                    : "Δεν υπάρχει βαθμονόμηση σε αυτό το καρέ (κινούμενη κάμερα). ";
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

            pendingMeasure = SwimPickTarget.None;
            calibrationClicks.Clear();
            SetPick(SwimPickTarget.None);
            Status("Ακυρώθηκε.", false);
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

            SwimCalibration calibration = CalibrationFor(timestamp);
            if (calibration == null)
            {
                double l1, l2;
                SuggestLines(pendingMeasure, pendingWall, out l1, out l2);
                StartCalibration(l1, l2);
                Status("Το καρέ άλλαξε: χρειάζεται βαθμονόμηση σε αυτό το καρέ. " + CalibrationPrompt(), true);
                return;
            }

            double x, y;
            if (!calibration.TryPool(point, out x, out y) || double.IsNaN(x) || double.IsInfinity(x))
            {
                SystemSounds.Exclamation.Play();
                Status("Το σημείο δεν μπορεί να μετρηθεί με αυτή τη βαθμονόμηση. Κάνε κλικ πάνω στο νερό.", true);
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
                Label = string.Format(CultureInfo.InvariantCulture, "{0:0.00} m", value),
                Calibration = calibration
            };

            pendingMeasure = SwimPickTarget.None;
            SetPick(SwimPickTarget.None);

            string message = string.Format(CultureInfo.InvariantCulture, "📍 {0}: {1:0.00} μ.", label, value);
            bool warn = false;
            if (x < -1 || x > PoolLength + 1)
            {
                message += "  ⚠ το σημείο είναι έξω από την πισίνα: έλεγξε τη βαθμονόμηση.";
                warn = true;
            }
            else if (y < -1.5 || y > calibration.LaneWidth + 1.5)
            {
                message += "  ⚠ το σημείο είναι έξω από τον διάδρομο της βαθμονόμησης.";
                warn = true;
            }

            if (Settings.FixedCamera && Math.Abs(Timing.Seconds(timestamp, calibration.Timestamp)) > 0.05)
                message += string.Format(CultureInfo.InvariantCulture, "  (βαθμονόμηση {0} από άλλο καρέ: σωστό μόνο αν η κάμερα δεν κινήθηκε)", calibration.Label);

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
            Status("Η βαθμονόμηση " + calibration.Label + " διαγράφηκε.", false);
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
                return string.Format("Βαθμονόμηση σε εξέλιξη: σημείο {0}/4.", calibrationClicks.Count + 1);

            SwimCalibration current = CalibrationFor(player.CurrentTimestamp);
            if (current != null)
            {
                string location = SameFrame(current.Timestamp, player.CurrentTimestamp) ? "σε αυτό το καρέ" : string.Format(CultureInfo.InvariantCulture, "από το καρέ {0:0.00} s", RaceSeconds(current.Timestamp));
                return string.Format("✔ Βαθμονόμηση {0} {1}. Πάτησε «📍 Μέτρηση» δίπλα σε μια απόσταση.", current.Label, location);
            }

            if (count == 0)
                return "Καμία βαθμονόμηση: πάτησε «📍 Μέτρηση» (ή «Βαθμονόμηση») σε ένα σταματημένο καρέ όπου φαίνονται οι δύο γραμμές.";

            return string.Format("{0} βαθμονομήσεις σε άλλα καρέ. Με κινούμενη κάμερα χρειάζεται βαθμονόμηση σε κάθε καρέ μέτρησης.", count);
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
                Status("Το καρέ άλλαξε κατά τη βαθμονόμηση: ξεκινάμε ξανά από το σημείο 1 σε αυτό το καρέ.", true);
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

            string done = string.Format(CultureInfo.InvariantCulture, "✔ Βαθμονόμηση {0} στο {1:0.00} s.", calibration.Label, RaceSeconds(calibration.Timestamp));
            if (pendingMeasure != SwimPickTarget.None)
            {
                SetPick(pendingMeasure);
                Status(done + " " + MeasurePrompt(pendingMeasure, pendingWall), false);
            }
            else
            {
                SetPick(SwimPickTarget.None);
                Status(done + " Τώρα πάτησε «📍 Μέτρηση» δίπλα σε μια απόσταση και κάνε κλικ στο βίντεο.", false);
            }

            if (form != null)
                form.RefreshCalibration();
        }

        private string CalibrationPrompt()
        {
            int i = calibrationClicks.Count;
            double line = i < 2 ? CalibrationLine1 : CalibrationLine2;
            string rope = i % 2 == 0 ? "στο σχοινί Α (π.χ. το κοντινό στην κάμερα)" : "στο σχοινί Β (το άλλο σχοινί του διαδρόμου)";
            if (i >= 2)
                rope = i % 2 == 0 ? "στο σχοινί Α" : "στο σχοινί Β";

            return string.Format(CultureInfo.InvariantCulture,
                "📐 Κλικ {0}/4: η γραμμή των {1:0.##} μ. {2}, στην επιφάνεια του νερού. Esc: ακύρωση.", i + 1, line, rope);
        }

        private string MeasurePrompt(SwimPickTarget target, int wall)
        {
            string what;
            switch (target)
            {
                case SwimPickTarget.EntryHands: what = "Κάνε κλικ στο σημείο όπου τα χέρια μπαίνουν στο νερό."; break;
                case SwimPickTarget.EntryHips: what = "Κάνε κλικ στο σημείο όπου τα ισχία μπαίνουν στο νερό."; break;
                case SwimPickTarget.StartUnderwater: what = "Κάνε κλικ στο κεφάλι, εκεί που φτάνει στην επιφάνεια."; break;
                case SwimPickTarget.StartBreakout: what = "Κάνε κλικ στο κεφάλι, εκεί που βγαίνει από το νερό."; break;
                case SwimPickTarget.TurnUnderwater: what = string.Format("Στροφή {0}μ.: κάνε κλικ στο κεφάλι, εκεί που βγαίνει από το νερό.", wall); break;
                case SwimPickTarget.FinishLastStroke: what = "Κάνε κλικ στο σημείο εισόδου του χεριού στην τελευταία χεριά."; break;
                case SwimPickTarget.PhaseEntry: what = "Κάνε κλικ στο σημείο όπου τα χέρια μπαίνουν στο νερό (απόσταση από τη γραμμή 0)."; break;
                case SwimPickTarget.PhaseBreakout: what = "Κάνε κλικ στο κεφάλι εκεί που βγαίνει από το νερό (απόσταση από τη γραμμή 0)."; break;
                default: what = "Κάνε κλικ στο βίντεο."; break;
            }

            return "📍 " + what + " Esc: ακύρωση.";
        }

        private string MeasureLabel(SwimPickTarget target, int wall)
        {
            switch (target)
            {
                case SwimPickTarget.EntryHands: return "Απόσταση εισόδου (χέρια)";
                case SwimPickTarget.EntryHips: return "Απόσταση εισόδου (ισχία)";
                case SwimPickTarget.StartUnderwater: return "Απόσταση κάτω από το νερό";
                case SwimPickTarget.StartBreakout: return "Απόσταση εξόδου (κεφάλι)";
                case SwimPickTarget.TurnUnderwater: return string.Format("Στροφή {0}μ.: μέτρα κάτω από το νερό", wall);
                case SwimPickTarget.FinishLastStroke: return "Τερματισμός: απόσταση τελευταίας χεριάς";
                case SwimPickTarget.PhaseEntry: return PendingPhaseTitle() + ": απόσταση εισόδου";
                case SwimPickTarget.PhaseBreakout: return PendingPhaseTitle() + ": απόσταση εξόδου";
                default: return "Απόσταση";
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
                case SwimPickTarget.StartUnderwater: Race.UnderwaterDist = value; break;
                case SwimPickTarget.StartBreakout: Race.BreakoutDist = value; break;
                case SwimPickTarget.TurnUnderwater: Race.GetTurn(wall, true).UnderwaterDist = value; break;
                case SwimPickTarget.FinishLastStroke: Race.FinishLastStrokeDist = value; break;
                case SwimPickTarget.PhaseEntry: Race.GetPhaseValues(pendingPhaseId, true).EntryDist = value; break;
                case SwimPickTarget.PhaseBreakout: Race.GetPhaseValues(pendingPhaseId, true).BreakoutDist = value; break;
            }
        }

        private string PendingPhaseTitle()
        {
            SwimTechPhase phase = TechPhases.FirstOrDefault(p => p.Id == pendingPhaseId);
            return phase != null ? phase.Title : "Προσπάθεια";
        }

        /// <summary>
        /// Distance of a start or a turn of the technical analysis: in a race the values of the race analysis
        /// (entry and breakout of the start, underwater distance of the turn), in a training video the values of the attempt.
        /// </summary>
        public void BeginPhaseMeasure(SwimTechPhase phase, bool entry)
        {
            if (phase == null)
            {
                Status("Επίλεξε πρώτα μια εκκίνηση ή μια στροφή.", true);
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
        private static readonly Pen penLine = new Pen(Color.FromArgb(200, 255, 214, 0), 1.0f);
        private static readonly Pen penMajor = new Pen(Color.FromArgb(230, 255, 160, 0), 2.0f);
        private static readonly Pen penRope = new Pen(Color.FromArgb(160, 255, 214, 0), 1.0f) { DashStyle = DashStyle.Dash };
        private static readonly Pen penMeasure = new Pen(Color.FromArgb(240, 0, 220, 255), 2.0f);
        private static readonly SolidBrush brushPoint = new SolidBrush(Color.FromArgb(240, 255, 214, 0));
        private static readonly SolidBrush brushMeasure = new SolidBrush(Color.FromArgb(240, 0, 220, 255));
        private static readonly SolidBrush brushLabelBack = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
        private static readonly Font fontLabel = new Font("Segoe UI", 8.5f, FontStyle.Bold);

        /// <summary>
        /// Draws the calibration and the last measured distance on the player screen (screen only).
        /// </summary>
        public void DrawOverlay(PlayerScreenUserInterface screen, Graphics g, ImageTransform transform, long timestamp, Size renderingSize)
        {
            if (screen != player || form == null || form.IsDisposed || VideoFile == null || transform == null)
                return;

            try
            {
                SmoothingMode smoothing = g.SmoothingMode;
                TextRenderingHint textHint = g.TextRenderingHint;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

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

                DrawAngles(g, transform, timestamp);
                if (Race != null && (Race.IsTraining || TechnicalView))
                    DrawTechOverlay(g, timestamp, renderingSize);
                if (Race != null && !Race.IsTraining)
                    DrawRaceOverlay(g, timestamp, renderingSize);

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
                string text = string.Format(CultureInfo.InvariantCulture, "{0} · {1:0.##} m {2}", i + 1, line, i % 2 == 0 ? "A" : "B");
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
                        DrawLabel(g, string.Format(CultureInfo.InvariantCulture, "{0:0.##} m", x), transform.Transform(label), Color.FromArgb(255, 230, 120));
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
            if (m.Calibration != null)
                DrawPoolSegment(g, transform, m.Calibration, penMeasure, m.PoolX, 0, m.PoolX, m.Calibration.LaneWidth);

            g.FillEllipse(brushMeasure, p.X - 5, p.Y - 5, 10, 10);
            DrawLabel(g, m.Label, new Point(p.X + 10, p.Y + 6), Color.FromArgb(160, 240, 255));
        }

        private static void DrawPoolSegment(Graphics g, ImageTransform transform, SwimCalibration c, Pen pen, double x1, double y1, double x2, double y2)
        {
            PointF a, b;
            if (!c.TryImage(x1, y1, out a) || !c.TryImage(x2, y2, out b))
                return;

            g.DrawLine(pen, transform.Transform(a), transform.Transform(b));
        }

        private static void DrawLabel(Graphics g, string text, Point location, Color color)
        {
            SizeF size = g.MeasureString(text, fontLabel);
            Rectangle back = new Rectangle(location.X - 2, location.Y - 1, (int)size.Width + 4, (int)size.Height + 2);
            g.FillRectangle(brushLabelBack, back);
            using (SolidBrush brush = new SolidBrush(color))
                g.DrawString(text, fontLabel, brush, location);
        }
        #endregion

        #region Race overlay: race time, last split, delta, ΔΑΚ and velocity chart
        private static readonly Color colorAthlete = Color.FromArgb(255, 255, 184, 0);
        private static readonly Color colorReference = Color.FromArgb(255, 0, 200, 255);
        private static readonly Pen penAthlete = new Pen(colorAthlete, 2.4f) { LineJoin = LineJoin.Round };
        private static readonly Pen penAthleteLast = new Pen(colorAthlete, 4f);
        private static readonly Pen penReference = new Pen(colorReference, 1.6f) { DashStyle = DashStyle.Dash };

        // Start, turns and last 5 m before a wall: conventional velocity (touch at the wall, dive, glide, underwater), drawn faint.
        private static readonly Pen penAthletePhase = new Pen(Color.FromArgb(150, colorAthlete), 1.6f) { DashStyle = DashStyle.Dot };
        private static readonly Pen penAthleteLastPhase = new Pen(Color.FromArgb(170, colorAthlete), 3f) { DashStyle = DashStyle.Dot };
        private static readonly Pen penReferencePhase = new Pen(Color.FromArgb(120, colorReference), 1.2f) { DashStyle = DashStyle.Dot };
        private static readonly Pen penChartGrid = new Pen(Color.FromArgb(38, 255, 255, 255), 1f);
        private static readonly Pen penChartWall = new Pen(Color.FromArgb(90, 0, 200, 255), 1f) { DashStyle = DashStyle.Dot };
        private static readonly SolidBrush brushText = new SolidBrush(Color.White);
        private static readonly SolidBrush brushDim = new SolidBrush(Color.FromArgb(225, 150, 170, 192));
        private static readonly SolidBrush brushFaster = new SolidBrush(Color.FromArgb(255, 32, 168, 110));
        private static readonly SolidBrush brushSlower = new SolidBrush(Color.FromArgb(255, 214, 64, 64));
        private static readonly SolidBrush brushAthlete = new SolidBrush(colorAthlete);
        private static readonly SolidBrush brushReference = new SolidBrush(colorReference);

        /// <summary>
        /// Panel in a corner of the video, synchronized with the video time: only the splits already
        /// passed at the current frame are shown, so it plays "live" during the playback.
        /// </summary>
        private void DrawRaceOverlay(Graphics g, long timestamp, Size size)
        {
            if (Race == null || Computed == null || !Race.StartTimestamp.HasValue || Race.Distance <= 0)
                return;
            if (!Settings.ShowSpeedChart && !Settings.ShowSplitBoard)
                return;
            if (size.Width < 300 || size.Height < 200)
                return;

            double now = Timing.Seconds(timestamp, Race.StartTimestamp.Value);
            if (now < -1e-6)
                return;

            int width = Math.Max(280, Math.Min(540, (int)(size.Width * 0.36)));
            int boardHeight = Settings.ShowSplitBoard ? 70 : 0;
            int chartHeight = Settings.ShowSpeedChart ? (int)(width * 0.5) : 0;
            int gap = boardHeight > 0 && chartHeight > 0 ? 4 : 0;
            int height = boardHeight + gap + chartHeight;
            if (height > size.Height - 20)
            {
                chartHeight = 0;
                height = boardHeight;
                if (height == 0)
                    return;
            }

            const int margin = 10;
            string corner = Settings.OverlayCorner ?? "TopRight";
            int x = corner.EndsWith("Left", StringComparison.Ordinal) ? margin : size.Width - width - margin;
            int y = corner.StartsWith("Bottom", StringComparison.Ordinal) ? size.Height - height - margin : margin;

            // Splits reached at this moment of the video.
            List<SwimRow> done = Computed.Rows.Where(r => r.T.HasValue && r.T.Value <= now + 1e-6).ToList();
            SwimRow last = done.OrderBy(r => r.T.Value).LastOrDefault();

            int top = y;
            if (boardHeight > 0)
            {
                Rectangle board = new Rectangle(x, top, width, boardHeight);
                DrawCard(g, board, SwimTheme.Accent);
                DrawBoard(g, board, now, last);
                top += boardHeight + gap;
            }

            if (chartHeight > 0)
            {
                Rectangle chart = new Rectangle(x, top, width, chartHeight);
                DrawCard(g, chart, colorAthlete);
                DrawSpeedChart(g, chart, done, last);
            }
        }

        /// <summary>Translucent dark card with an accent on the left edge (broadcast style).</summary>
        private static void DrawCard(Graphics g, Rectangle r, Color accent)
        {
            using (GraphicsPath path = SwimTheme.Rounded(r, 7))
            using (LinearGradientBrush back = new LinearGradientBrush(r, Color.FromArgb(232, 9, 14, 22), Color.FromArgb(205, 18, 30, 45), LinearGradientMode.Horizontal))
            using (Pen border = new Pen(Color.FromArgb(90, 255, 255, 255), 1f))
            {
                g.FillPath(back, path);
                g.DrawPath(border, path);
            }

            using (SolidBrush a = new SolidBrush(accent))
                g.FillRectangle(a, r.Left + 1, r.Top + 7, 3, r.Height - 14);
        }

        private void DrawBoard(Graphics g, Rectangle r, double now, SwimRow last)
        {
            bool finished = last != null && last.D >= Race.Distance;
            Font caption = SwimTheme.Ui(6.75f, FontStyle.Bold);
            Font clockFont = SwimTheme.Display(21f);
            Font splitFont = SwimTheme.Display(11f);

            // Name and event.
            string names = string.IsNullOrWhiteSpace(Race.Athlete) ? "" : Race.Athlete.Trim().ToUpperInvariant();
            string ev = string.Format(CultureInfo.InvariantCulture, "{0}{1} {2}", Race.DistanceLabel, Race.IsYards ? "YD" : "M", SwimPresets.StyleShort(Race.Style).ToUpperInvariant());
            using (StringFormat format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
                g.DrawString(names.Length > 0 ? names + "  ·  " + ev : ev, caption, brushDim, new RectangleF(r.Left + 10, r.Top + 6, r.Width * 0.62f, 13), format);

            string clock = SwimReference.FormatTime(finished ? last.T.Value : Math.Max(0, now));
            g.DrawString(clock, clockFont, brushText, r.Left + 8, r.Top + 18);

            if (last != null)
            {
                string label = finished ? "ΤΕΛΙΚΟΣ" : last.D.ToString(CultureInfo.InvariantCulture) + (Race.IsYards ? " YD" : " M");
                string split = SwimReference.FormatTime(last.T.Value);
                SizeF ls = g.MeasureString(label, caption);
                SizeF ss = g.MeasureString(split, splitFont);
                float right = r.Right - 10;
                g.DrawString(label, caption, brushDim, right - ls.Width, r.Top + 6);
                g.DrawString(split, splitFont, brushText, right - ss.Width, r.Top + 18);

                if (last.Delta.HasValue)
                {
                    string delta = FormatDelta(last.Delta.Value);
                    Font deltaFont = SwimTheme.Display(10.5f);
                    SizeF ds = g.MeasureString(delta, deltaFont);
                    RectangleF pill = new RectangleF(right - ds.Width - 10, r.Top + 40, ds.Width + 10, ds.Height + 1);
                    using (GraphicsPath path = SwimTheme.Rounded(pill, pill.Height / 2))
                        g.FillPath(last.Delta.Value <= 0 ? brushFaster : brushSlower, path);
                    g.DrawString(delta, deltaFont, brushText, pill.Left + 5, pill.Top + 1);
                }
            }

            // ΔΑΚ of the last 50 completed, and the reference.
            SwimBlock block = Computed.Blocks.Where(b => b.Dak.HasValue && Computed.Rows.Any(row => row.D == b.To && row.T.HasValue && row.T.Value <= now + 1e-6))
                .OrderBy(b => b.To).LastOrDefault();
            float bx = r.Left + 10;
            if (block != null)
            {
                string dak = string.Format(CultureInfo.InvariantCulture, "ΔΑΚ {0}-{1}  {2:0.00}", block.From, block.To, block.Dak.Value);
                Font dakFont = SwimTheme.Ui(7.5f, FontStyle.Bold);
                SizeF ks = g.MeasureString(dak, dakFont);
                RectangleF chip = new RectangleF(bx, r.Bottom - ks.Height - 7, ks.Width + 8, ks.Height + 2);
                SwimTheme.FillRounded(g, Color.FromArgb(200, 74, 52, 6), chip, 4);
                using (SolidBrush amber = new SolidBrush(SwimTheme.Amber))
                    g.DrawString(dak, dakFont, amber, chip.Left + 4, chip.Top + 1);
                bx = chip.Right + 8;
            }

            if (Computed.Reference != null)
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
            g.DrawString("ΤΑΧΥΤΗΤΑ  M/S", caption, brushDim, r.Left + 10, r.Top + 6);

            // Legend.
            float lx = r.Right - 8;
            if (Computed.Reference != null)
            {
                string refText = "ΑΝΑΦΟΡΑ";
                SizeF s = g.MeasureString(refText, caption);
                lx -= s.Width;
                g.DrawString(refText, caption, brushReference, lx, r.Top + 6);
                lx -= 20;
                g.DrawLine(penReference, lx, r.Top + 12, lx + 16, r.Top + 12);
                lx -= 10;
            }

            string athleteText = "ΑΘΛΗΤΗΣ";
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
            DrawSteps(g, penReference, penReferencePhase, reference, X, Y);
            List<SwimSegment> athleteDone = done.Where(row => row.Vel.HasValue).Select(row => new SwimSegment { D0 = row.PrevD, D1 = row.D, V = row.Vel.Value, Free = row.IsFree }).ToList();
            foreach (SwimSegment s in athleteDone.Where(x => x.Free))
            {
                RectangleF area = new RectangleF(X(s.D0), Y(s.V), Math.Max(1, X(s.D1) - X(s.D0)), Math.Max(1, plot.Bottom - Y(s.V)));
                using (LinearGradientBrush glow = new LinearGradientBrush(new RectangleF(area.X, area.Y - 1, area.Width, area.Height + 2),
                    Color.FromArgb(80, colorAthlete), Color.FromArgb(0, colorAthlete), LinearGradientMode.Vertical))
                    g.FillRectangle(glow, area);
            }

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
                SwimTheme.FillRounded(g, Color.FromArgb(225, 9, 14, 22), pill, 4);
                g.DrawString(value, valueFont, last.IsFree ? brushAthlete : brushDim, vx, vy);
            }

            if (athleteAll.Count == 0 && reference.Count == 0)
            {
                string hint = "η ταχύτητα εμφανίζεται με τα περάσματα";
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
            undoStack.Clear();
            PbCandidates = new List<SwimAnalysisItem>();
            Changed(false);
            RefreshPbCandidates();
            if (form != null)
                form.RefreshAll();
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
            undoStack.Clear();
            Status("Νέα ανάλυση στο ίδιο βίντεο (ίδιο σήμα εκκίνησης). Συμπλήρωσε το όνομα του αθλητή.", false);
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
            undoStack.Clear();
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
                AthletesMessage = "Ρύθμισε πρώτα τη σύνδεση (καρτέλα Σύνδεση) για να φορτωθεί η λίστα αθλητών.";
                if (form != null)
                    form.RefreshAthletes();
                return;
            }

            if (AthletesLoading)
                return;

            AthletesLoading = true;
            AthletesMessage = "Φόρτωση λίστας αθλητών…";
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
                            list.Athletes.Add(a);
                        }
                    }

                    list.BuildIndex();
                    Athletes = list;
                    Athletes.Save();
                    AthletesMessage = AthletesSummary();
                    if (list.Athletes.Count == 0)
                        AthletesMessage = "Δεν βρέθηκαν αθλητές: το Google Sheet χρειάζεται φύλλο με στήλες unique_id και ΟΝΟΜΑΤΕΠΩΝΥΜΟ.";
                }
                else
                {
                    AthletesMessage = "Η λίστα αθλητών δεν ενημερώθηκε: " + result.Error + " " + AthletesSummary();
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
                return "Η λίστα αθλητών δεν έχει φορτωθεί.";

            return string.Format("Λίστα: {0} αθλητές (ενημέρωση {1:dd/MM HH:mm}).", Athletes.Athletes.Count, Athletes.Updated);
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
            Status(string.Format("Αθλητής: {0} (μητρώο {1}).", athlete.Name, athlete.Id), false);
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
        /// References of the event of the current race (and of the gender of the athlete when known).
        /// </summary>
        public List<SwimReference> ReferencesForRace()
        {
            return References.For(Race);
        }

        /// <summary>
        /// Download the references of the sheet "Elite αναφορές" (in the background).
        /// </summary>
        public void RefreshReferences()
        {
            if (!Settings.IsConfigured)
            {
                ReferencesMessage = "Ρύθμισε πρώτα τη σύνδεση (καρτέλα Σύνδεση) για να φορτωθούν οι elite αναφορές.";
                if (form != null)
                    form.RefreshReferences();
                return;
            }

            if (ReferencesLoading)
                return;

            ReferencesLoading = true;
            ReferencesMessage = "Φόρτωση elite αναφορών…";
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
                        ReferencesMessage += string.Format(" ⚠ {0} γραμμές του φύλλου δεν διαβάστηκαν (π.χ. {1}).", skipped.Count, skipped[0]);

                    UploadLocalReferences();
                }
                else if (IsUnknownAction(result))
                {
                    ReferencesMessage = "Για τις elite αναφορές χρειάζεται η νέα έκδοση του Apps Script (1.2.0): δες τον οδηγό. " + ReferencesSummary();
                }
                else
                {
                    ReferencesMessage = "Οι elite αναφορές δεν ενημερώθηκαν: " + result.Error + " " + ReferencesSummary();
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
            if (reference != null)
                Settings.ReferenceByEvent[key] = reference.Id;
            else
                Settings.ReferenceByEvent.Remove(key);

            Settings.Save();
            Status(reference != null ? "Σύγκριση με: " + reference.Label : "Χωρίς σύγκριση με αναφορά.", false);
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
                Status("Η ανάλυση δεν έχει ακόμη χρόνους περασμάτων για να γίνει αναφορά.", true);
                return;
            }

            reference.Local = true;
            References.AddOrReplace(reference);
            References.Save();
            if (form != null)
                form.RefreshReferences();

            if (!Settings.IsConfigured)
            {
                Status("Η αναφορά «" + reference.Name + "» αποθηκεύτηκε σε αυτόν τον υπολογιστή.", false);
                return;
            }

            Status("Αποστολή της αναφοράς «" + reference.Name + "» στο Google Sheet…", false);
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
                        Status("✔ Η αναφορά «" + reference.Name + "» μπήκε στο φύλλο «" + ReferenceSheetName + "» του Google Sheet.", false);
                }
                else if (report)
                {
                    if (IsUnknownAction(result))
                        Status("Η αναφορά κρατήθηκε σε αυτόν τον υπολογιστή. Για να μπει και στο Google Sheet χρειάζεται η νέα έκδοση του Apps Script (1.2.0).", true);
                    else
                        Status("Η αναφορά κρατήθηκε σε αυτόν τον υπολογιστή: " + result.Error, true);
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

                SwimReference fresh = References.Find(race.Reference.Id);
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
        /// When the event of the race changes, use the reference chosen last time for this event (if any).
        /// Returns true if the reference of the race changed.
        /// </summary>
        private bool EnsureReference()
        {
            if (Race == null)
                return false;

            if (Race.Reference != null && Race.Reference.Matches(Race) && Race.Reference.GenderMatches(Race.AthleteGender))
                return false;

            SwimReference remembered = null;
            string id;
            if (Settings.ReferenceByEvent.TryGetValue(EventKey(Race), out id))
                remembered = References.Find(id);

            if (remembered != null && (!remembered.Matches(Race) || !remembered.GenderMatches(Race.AthleteGender)))
                remembered = null;

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
                return "Δεν υπάρχουν ακόμη elite αναφορές.";

            int local = References.References.Count(r => r.Local);
            string text = string.Format("Elite αναφορές: {0}", References.References.Count);
            if (References.Updated > DateTime.MinValue)
                text += string.Format(" (ενημέρωση {0:dd/MM HH:mm})", References.Updated);
            if (local > 0)
                text += string.Format(", {0} μόνο σε αυτόν τον υπολογιστή", local);
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
                    SyncMessage = "Έτοιμο";
                }
            }
            else
            {
                SyncState = SwimSyncState.NotConfigured;
                SyncMessage = "Δεν έχει ρυθμιστεί η σύνδεση με το Google Sheet";
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
                callback(new SwimSheetResult { Ok = false, Error = "Συμπλήρωσε τη διεύθυνση της Εφαρμογής Ιστού." });
                return;
            }

            client.Ping(Settings.WebAppUrl, Settings.Token, callback);
        }

        public void SendNow(bool pdf)
        {
            if (Race == null)
                return;

            if (!Settings.IsConfigured)
            {
                SyncState = SwimSyncState.NotConfigured;
                SyncMessage = "Συμπλήρωσε διεύθυνση και token στην καρτέλα Σύνδεση.";
                if (form != null)
                    form.RefreshSync();
                return;
            }

            long origin = frameServer != null && frameServer.Loaded ? frameServer.Metadata.TimeOrigin : 0;
            string body = SwimPayload.Build(Race, Computed, TechPhases, Timing, origin, Settings, VideoName, pdf ? "pdf" : "update");
            client.Enqueue(Settings.WebAppUrl, body, Race.Id, pdf);
        }

        /// <summary>
        /// Send another analysis of the video (e.g. after the upload of one of its snapshots).
        /// </summary>
        private void SendRace(SwimRace race, bool pdf)
        {
            if (race == null || !Settings.IsConfigured)
                return;

            if (race == Race)
            {
                SendNow(pdf);
                return;
            }

            SwimComputed computed = SwimCalculator.Compute(race, Timing, Settings.StrokeRounding);
            List<SwimTechPhase> tech = SwimTechCalculator.Compute(race, computed, Timing);
            long origin = frameServer != null && frameServer.Loaded ? frameServer.Metadata.TimeOrigin : 0;
            client.Enqueue(Settings.WebAppUrl, SwimPayload.Build(race, computed, tech, Timing, origin, Settings, VideoName, pdf ? "pdf" : "update"), race.Id, pdf);
        }

        public void OpenSheet()
        {
            if (Race == null)
                return;

            if (string.IsNullOrEmpty(Race.SheetUrl))
            {
                Status("Το φύλλο δεν έχει δημιουργηθεί ακόμη. Πάτησε Αποστολή τώρα.", true);
                return;
            }

            OpenUrl(Race.SheetUrl);
        }

        private void Client_StateChanged(object sender, SwimSyncEventArgs e)
        {
            SyncState = e.State;
            SyncMessage = e.Message;

            // An older Apps Script still computes the stroke rate and length in the last 5 m and over whole 50s.
            Version backend;
            string version = e.Result != null && e.Result.Ok && e.Result.Raw != null ? e.Result.Raw.Value<string>("version") : null;
            if (!backendWarned && !string.IsNullOrEmpty(version) && Version.TryParse(version, out backend) && backend < minimumBackend)
            {
                backendWarned = true;
                Status(string.Format(CultureInfo.InvariantCulture,
                    "Το Apps Script του KOE App είναι η έκδοση {0}: το report δεν έχει ακόμη τις πισίνες σε γιάρδες, τον ΔΑΚ, τις συγκρίσεις και την τεχνική ανάλυση. Βάλε την έκδοση {1} (δες τον οδηγό).",
                    version, minimumBackend.ToString(3)), true);
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
                        OpenUrl(e.Result.PdfUrl);
                    }

                    MarkDirty();
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
            undoStack.Clear();
            Recompute();
            if (form != null)
                form.RefreshAll();
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
                Status("Δεν ήταν δυνατή η αποθήκευση της ανάλυσης στον δίσκο: " + e.Message, true);
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
                        file.VideoPath = path;
                        if (file.Calibrations == null)
                            file.Calibrations = new List<SwimCalibration>();
                        file.Calibrations.RemoveAll(c => c == null);
                        Status(string.Format("Φορτώθηκαν {0} αναλύσεις για αυτό το βίντεο.", file.Races.Count), false);
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
            if (PickTarget != SwimPickTarget.None)
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
