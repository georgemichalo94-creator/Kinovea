#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using Kinovea.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// A frame of an elite swimmer for the comparison with the snapshots (sheet "Elite καρέ" of the Google Sheet).
    /// </summary>
    public class SwimEliteFrame
    {
        public string FileId = "";
        public string Label = "";
        public string Style = "";

        /// <summary>"start" or "turn".</summary>
        public string Kind = "start";
        public string Stage = "";
        public string Athlete = "";
        public string Source = "";
        public string Comment = "";

        [JsonIgnore]
        public string Display
        {
            get
            {
                List<string> parts = new List<string> { Label };
                if (!string.IsNullOrWhiteSpace(Stage))
                    parts.Add(Stage);
                if (!string.IsNullOrWhiteSpace(Style))
                    parts.Add(Style);
                return string.Join(" · ", parts.ToArray());
            }
        }

        public override string ToString()
        {
            return Display;
        }

        public static SwimEliteFrame FromJson(JObject o)
        {
            if (o == null)
                return null;

            SwimEliteFrame f = new SwimEliteFrame();
            f.FileId = o.Value<string>("fileId") ?? "";
            f.Label = o.Value<string>("label") ?? "";
            f.Style = o.Value<string>("style") ?? "";
            f.Kind = o.Value<string>("kind") == "turn" ? "turn" : "start";
            f.Stage = o.Value<string>("stage") ?? "";
            f.Athlete = o.Value<string>("athlete") ?? "";
            f.Source = o.Value<string>("source") ?? "";
            f.Comment = o.Value<string>("comment") ?? "";
            return f.FileId.Length > 0 ? f : null;
        }
    }

    /// <summary>
    /// Local copy of the elite frames and of their images (settings folder), so they can be chosen offline.
    /// </summary>
    public class SwimEliteLibrary
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public DateTime Updated;
        public List<SwimEliteFrame> Frames = new List<SwimEliteFrame>();

        private static string FilePath
        {
            get { return Path.Combine(Software.SettingsDirectory, "SwimEliteFrames.json"); }
        }

        public static string ImageFolder
        {
            get { return Path.Combine(Software.SettingsDirectory, "SwimAnalysis", "EliteFrames"); }
        }

        public static string ImagePath(string fileId)
        {
            string safe = new string((fileId ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
            return Path.Combine(ImageFolder, safe + ".jpg");
        }

        public static SwimEliteLibrary Load()
        {
            SwimEliteLibrary library = null;
            try
            {
                if (File.Exists(FilePath))
                    library = JsonConvert.DeserializeObject<SwimEliteLibrary>(File.ReadAllText(FilePath, System.Text.Encoding.UTF8));
            }
            catch (Exception e)
            {
                log.Error("Could not read the elite frames.", e);
            }

            if (library == null || library.Frames == null)
                library = new SwimEliteLibrary();

            library.Frames.RemoveAll(f => f == null || string.IsNullOrEmpty(f.FileId));
            return library;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.None), System.Text.Encoding.UTF8);
            }
            catch (Exception e)
            {
                log.Error("Could not save the elite frames.", e);
            }
        }

        /// <summary>Frames of the style and kind of a phase first.</summary>
        public List<SwimEliteFrame> For(string style, SwimPhaseKind? kind)
        {
            string k = kind.HasValue ? (kind.Value == SwimPhaseKind.Turn ? "turn" : "start") : null;
            return Frames
                .OrderBy(f => (k == null || f.Kind == k) ? 0 : 1)
                .ThenBy(f => (string.IsNullOrEmpty(style) || string.IsNullOrEmpty(f.Style) || f.Style == style) ? 0 : 1)
                .ThenBy(f => f.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    /// <summary>
    /// Another analysis of the athlete in the same event, for the comparison in the report (PB).
    /// </summary>
    public class SwimAnalysisItem
    {
        public string Id = "";
        public string Label = "";
        public double? FinalTime;

        public override string ToString()
        {
            return Label;
        }
    }

    public partial class SwimAnalysisManager
    {
        #region Properties
        /// <summary>Starts and turns of the technical analysis (race) or attempts (training video).</summary>
        public List<SwimTechPhase> TechPhases { get; private set; } = new List<SwimTechPhase>();

        /// <summary>The panel shows the technical analysis (the overlay shows the phase values).</summary>
        public bool TechnicalView { get; set; }

        public SwimEliteLibrary EliteLibrary { get; private set; } = SwimEliteLibrary.Load();
        public bool EliteLoading { get; private set; }
        public string EliteMessage { get; private set; } = "";

        /// <summary>Other analyses of the athlete in the event (for the PB of the report).</summary>
        public List<SwimAnalysisItem> PbCandidates { get; private set; } = new List<SwimAnalysisItem>();
        public string PbMessage { get; private set; } = "";

        /// <summary>Name given to the next angle measured on the video.</summary>
        public string AngleName { get; set; } = "Γόνατο";

        /// <summary>Current position of the video (or null without a video).</summary>
        public long? CurrentVideoTimestamp
        {
            get { return HasVideo ? (long?)player.CurrentTimestamp : null; }
        }

        /// <summary>Phase at the current position of the video (or null).</summary>
        public SwimTechPhase CurrentTechPhase
        {
            get { return HasVideo ? SwimTechCalculator.PhaseAt(TechPhases, player.CurrentTimestamp) : null; }
        }
        #endregion

        #region Members
        private readonly List<PointF> angleClicks = new List<PointF>();
        private long angleFrame;
        private readonly HashSet<string> uploading = new HashSet<string>();

        /// <summary>
        /// Frames of marks of the technical analysis that are not (yet) an event of their phase, the newest last (see AutoSnapshots).
        /// </summary>
        private readonly List<KeyValuePair<string, Bitmap>> waitingFrames = new List<KeyValuePair<string, Bitmap>>();
        private const int WaitingFrames = 6;

        /// <summary>Snapshots of an event replaced by the one of a new mark (by the id of that mark): Undo of the mark puts them back.</summary>
        private readonly Dictionary<string, List<SwimSnapshot>> replacedSnapshots = new Dictionary<string, List<SwimSnapshot>>();

        /// <summary>Snapshots taken by the analyst that got the name of an event.</summary>
        private readonly HashSet<string> namedSnapshots = new HashSet<string>();

        /// <summary>Snapshots whose upload failed: not retried automatically until the next manual send.</summary>
        private readonly HashSet<string> uploadFailed = new HashSet<string>();
        private readonly Dictionary<string, List<Action<Image>>> eliteImageRequests = new Dictionary<string, List<Action<Image>>>();
        #endregion

        #region Commands of the technical analysis
        /// <summary>
        /// Swim commands of the technical analysis and of the training videos. Returns false for the other commands.
        /// </summary>
        private bool HandleTechCommand(PlayerScreenCommands command, long timestamp)
        {
            switch (command)
            {
                case PlayerScreenCommands.SwimHandsOff:
                    AddMark(SwimMarkType.HandsOff, timestamp, SwimLang.T("Χέρια φεύγουν από τον βατήρα"));
                    return true;
                case PlayerScreenCommands.SwimBackFoot:
                    AddMark(SwimMarkType.BackFoot, timestamp, SwimLang.T("Πίσω πόδι φεύγει"));
                    return true;
                case PlayerScreenCommands.SwimRotation:
                    AddMark(SwimMarkType.RotationStart, timestamp, SwimLang.T("Αρχή περιστροφής"));
                    return true;
                case PlayerScreenCommands.SwimWallContact:
                    if (Race.IsTraining)
                        AddMark(SwimMarkType.WallContact, timestamp, SwimLang.T("Επαφή στον τοίχο (νέα στροφή)"));
                    else
                        AddWallSplit(timestamp);
                    return true;
                case PlayerScreenCommands.SwimTechSplit:
                    AddTechSplit(timestamp, null);
                    return true;
                case PlayerScreenCommands.SwimSnapshot:
                    TakeSnapshot(timestamp);
                    return true;
                case PlayerScreenCommands.SwimAngle:
                    BeginAngle();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Training video: every press of the start key begins a new start attempt (its own t = 0).
        /// </summary>
        private void AddSignal(long timestamp)
        {
            if (Race.Marks.Any(m => m.Type == SwimMarkType.Signal && SameFrame(m.Timestamp, timestamp)))
            {
                SystemSounds.Beep.Play();
                Status(SwimLang.T("Υπάρχει ήδη σήμα εκκίνησης σε αυτό το καρέ."), true);
                return;
            }

            SwimMark mark = new SwimMark { Type = SwimMarkType.Signal, Timestamp = timestamp };
            Race.Marks.Add(mark);
            undoStack.Add(mark.Id);
            int n = Race.Marks.Count(m => m.Type == SwimMarkType.Signal && m.Timestamp <= timestamp);
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Εκκίνηση {0}: σήμα (t = 0 της προσπάθειας)."), n), false);
        }

        /// <summary>
        /// Race: W is the contact with the next wall (turn or finish), whatever checkpoints are still empty before it.
        /// A press shortly after a wall already marked marks that wall again (correction).
        /// </summary>
        private void AddWallSplit(long timestamp)
        {
            const double correction = 2.0;
            bool hadStart = EnsureStart();
            int last = SwimTechCalculator.LastWallBefore(Race, timestamp, HalfFrame);
            SwimMark lastSplit = last > 0 ? Race.GetSplit(last) : null;
            int wall = lastSplit != null && Timing.Seconds(timestamp, lastSplit.Timestamp) < correction ? last : last + PoolLength;
            string unit = Race.Unit;
            if (wall > Race.Distance || !Race.Checkpoints.Contains(wall))
            {
                SystemSounds.Beep.Play();
                Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Τα {0}{1} δεν είναι τοίχος αυτού του αγώνα (δες τα σημεία ελέγχου)."), wall, unit), true);
                return;
            }

            // A wall that has a time already is corrected only near that time (a press in the middle of a length is a slip).
            SwimMark existing = Race.GetSplit(wall);
            if (existing != null && Math.Abs(Timing.Seconds(timestamp, existing.Timestamp)) >= correction)
            {
                SystemSounds.Beep.Play();
                Status(string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("Ο τοίχος των {0}{1} έχει ήδη χρόνο ({2:0.00} s). Για διόρθωση πάτα W κοντά στο σωστό καρέ ή σβήσε τον χρόνο (Delete στον πίνακα)."),
                    wall, unit, Timing.Seconds(existing.Timestamp, Race.StartTimestamp.Value)), true);
                return;
            }

            SwimMark mark = new SwimMark { Type = SwimMarkType.Split, Timestamp = timestamp, Distance = wall };
            AddReplacing(mark, m => m.Type == SwimMarkType.Split && m.Distance == wall);
            ResolveProposalsByMark(SwimMarkType.WallContact, timestamp);

            double t = Timing.Seconds(timestamp, Race.StartTimestamp.Value);
            string message = string.Format(CultureInfo.InvariantCulture, "{0} {1}{2}: {3:0.00} s", wall >= Race.Distance ? SwimLang.T("Τερματισμός") : SwimLang.T("Τοίχος"), wall, unit, t);
            SwimMark previous = Race.Checkpoints.Where(d => d < wall).Select(d => Race.GetSplit(d)).LastOrDefault(m => m != null);
            SwimMark next = Race.Checkpoints.Where(d => d > wall).Select(d => Race.GetSplit(d)).FirstOrDefault(m => m != null);
            bool warn = (previous != null && previous.Timestamp >= timestamp) || (next != null && next.Timestamp <= timestamp);
            if (warn)
            {
                SystemSounds.Exclamation.Play();
                message += SwimLang.T("  ⚠ ο χρόνος δεν είναι ανάμεσα στα γειτονικά σημεία.");
            }

            if (hadStart || warn)
                Status(message, warn);
        }

        /// <summary>
        /// Head at a technical distance (T): the next one of the phase (5, 10, 15 after a start, +5, +10, +15 after a contact,
        /// otherwise −5 before the next wall), or the distance of a panel button (forced, on the page of its kind).
        /// </summary>
        public void AddTechSplit(long timestamp, int? forced, SwimPhaseKind? forcedKind = null)
        {
            if (Race == null)
                return;

            if (!Race.IsTraining)
                EnsureStart();

            List<SwimTechPhase> phases = SwimTechCalculator.Compute(Race, Computed, Timing);
            SwimTechPhase phase = null;
            int offset;
            if (forced.HasValue)
            {
                offset = forced.Value;
                if (offset > 0)
                {
                    // After the signal or the contact of the phase: never before it, never for a wall not marked yet.
                    phase = SwimTechCalculator.OwnerOf(phases, timestamp);
                    SwimPhaseKind kind = forcedKind ?? (phase != null ? phase.Kind : SwimPhaseKind.Start);
                    if (phase == null || phase.Kind != kind || Timing.Seconds(timestamp, phase.Origin) <= 0)
                    {
                        SystemSounds.Beep.Play();
                        if (kind == SwimPhaseKind.Start)
                            Status(SwimLang.T("Τα 5/10/15 μετριούνται μετά το σήμα εκκίνησης: πήγαινε το βίντεο στο καρέ που το κεφάλι περνά την απόσταση."), true);
                        else if (Race.IsTraining)
                            Status(SwimLang.T("Τα +5/+10/+15 μετριούνται μετά την επαφή στον τοίχο: μάρκαρε πρώτα την επαφή (W) αυτής της στροφής."), true);
                        else
                            Status(SwimLang.T("Τα +5/+10/+15 μετριούνται μετά την επαφή στον τοίχο: μάρκαρε πρώτα τον τοίχο της στροφής (W ή S)."), true);
                        return;
                    }
                }
                else if (Race.IsTraining)
                {
                    phase = phases.FirstOrDefault(p => p.Kind == SwimPhaseKind.Turn && p.Origin >= timestamp && p.Contains(timestamp));
                }
            }
            else
            {
                int? next = SwimTechCalculator.NextOffset(Race, Timing, phases, timestamp, out phase);
                if (!next.HasValue)
                {
                    SystemSounds.Beep.Play();
                    Status(SwimLang.T("Δεν μένει τεχνική απόσταση για το T εδώ: τα σημεία ελέγχου (π.χ. 15 και −5 πριν τον τοίχο) μαρκάρονται με το Πέρασμα (S) ") +
                        SwimLang.T("και μετράνε και στην τεχνική ανάλυση. Για άλλη απόσταση: τα κουμπιά 5/10/15 στην καρτέλα της φάσης."), true);
                    return;
                }

                offset = next.Value;
            }

            int wall = -1;
            string unit = Race.Unit;
            if (!Race.IsTraining)
            {
                wall = offset < 0 ? SwimTechCalculator.LastWallBefore(Race, timestamp, HalfFrame) + PoolLength : phase.Wall;
                if (offset < 0 && (wall >= Race.Distance || SwimTechCalculator.IsExchange(Race, wall)))
                {
                    SystemSounds.Beep.Play();
                    Status(SwimLang.T("Δεν ακολουθεί στροφή: το −5 μετριέται μόνο πριν από στροφή (ο τερματισμός μαρκάρεται με το Πέρασμα)."), true);
                    return;
                }

                if (SwimTechCalculator.IsCheckpoint(Race, wall, offset))
                {
                    SystemSounds.Beep.Play();
                    Status(string.Format(CultureInfo.InvariantCulture,
                        SwimLang.T("Τα {0}{1} είναι σημείο ελέγχου: μαρκάρεται με το Πέρασμα (S) και μετράει και στην τεχνική ανάλυση."), wall + offset, unit), true);
                    return;
                }
            }

            // A new press for the same distance of the same phase replaces the old one (Undo puts it back).
            Predicate<SwimMark> replaces;
            if (!Race.IsTraining)
            {
                int markWall = wall;
                replaces = m => m.Type == SwimMarkType.TechSplit && m.Distance == offset && m.Wall == markWall;
            }
            else if (offset < 0)
            {
                // Training: −5 belongs to the approach of the next contact (marked or not yet), near the press.
                List<long> origins = Race.Marks.Where(m => m.Type == SwimMarkType.Signal || m.Type == SwimMarkType.WallContact).Select(m => m.Timestamp).ToList();
                long span = SwimTechCalculator.Ticks(Timing, 10);
                long low = Math.Max(origins.Where(o => o < timestamp).DefaultIfEmpty(long.MinValue).Max(), timestamp - span);
                long high = Math.Min(origins.Where(o => o >= timestamp).DefaultIfEmpty(long.MaxValue).Min(), timestamp + span);
                replaces = m => m.Type == SwimMarkType.TechSplit && m.Distance == offset && m.Timestamp > low && m.Timestamp <= high;
            }
            else
            {
                // Training: +5/+10/+15 (or 5/10/15) belong to the attempt of the press.
                long low = phase.Origin;
                long high = phase.EventEnd;
                replaces = m => m.Type == SwimMarkType.TechSplit && m.Distance == offset && m.Timestamp > low && m.Timestamp < high;
            }

            SwimMark mark = new SwimMark { Type = SwimMarkType.TechSplit, Timestamp = timestamp, Distance = offset, Wall = wall };
            AddReplacing(mark, replaces);

            string where = offset < 0
                ? (Race.IsTraining ? "−5" + unit + SwimLang.T(" πριν τον τοίχο") : string.Format(CultureInfo.InvariantCulture, SwimLang.T("−5{0} πριν τον τοίχο των {1}{0}"), unit, wall))
                : (phase != null && phase.Kind == SwimPhaseKind.Turn ? "+" : "") + offset.ToString(CultureInfo.InvariantCulture) + unit;
            double? t = offset > 0 && phase != null ? Timing.Seconds(timestamp, phase.Origin) : (double?)null;
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Τεχνικό πέρασμα {0}{1}"), where,
                t.HasValue ? string.Format(CultureInfo.InvariantCulture, SwimLang.T(": {0:0.00} s από {1}"), t.Value, phase.Kind == SwimPhaseKind.Start ? SwimLang.T("το σήμα") : SwimLang.T("την επαφή")) : "."), false);
        }

        /// <summary>
        /// Panel buttons: technical split at the current frame of the video, for a distance of the page (start or turn).
        /// </summary>
        public void MarkTechSplit(int offset, SwimPhaseKind kind)
        {
            if (!HasVideo || Race == null)
            {
                Status(SwimLang.T("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής."), true);
                return;
            }

            AddTechSplit(player.CurrentTimestamp, offset, kind);
            Changed(true);
        }

        /// <summary>
        /// Delete the marks of a phase: all the events of a training attempt, or the technical marks of a start or turn of a race
        /// (the race marks stay: they are part of the race analysis).
        /// </summary>
        public void DeletePhase(SwimTechPhase phase)
        {
            if (Race == null || phase == null)
                return;

            int removed;
            if (Race.IsTraining)
            {
                removed = Race.Marks.RemoveAll(m => m.Id == phase.Id ||
                    (m.Type != SwimMarkType.Signal && m.Type != SwimMarkType.WallContact && phase.Contains(m.Timestamp)));
            }
            else if (phase.Kind == SwimPhaseKind.Start)
            {
                removed = Race.Marks.RemoveAll(m => (m.Type == SwimMarkType.TechSplit && (m.Wall == 0 || (m.Wall < 0 && phase.Contains(m.Timestamp)))) ||
                    m.Type == SwimMarkType.HandsOff || m.Type == SwimMarkType.BackFoot);
            }
            else
            {
                removed = Race.Marks.RemoveAll(m => (m.Type == SwimMarkType.TechSplit && (m.Wall == phase.Wall || (m.Wall < 0 && phase.Contains(m.Timestamp)))) ||
                    (m.Type == SwimMarkType.RotationStart && m.Timestamp >= phase.EventStart && m.Timestamp <= phase.Origin));
            }

            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}: διαγράφηκαν {1} σημάδια."), phase.Title, removed), false);
            Changed(true);
        }

        public void SeekTo(long timestamp)
        {
            if (HasVideo)
                player.ForcePosition(timestamp, true);
        }

        /// <summary>
        /// Shows the frame nearest to this time. The checkpoints and the breakouts read from the video fall between two frames, and
        /// the player shows the first frame at or after the time asked: half a frame earlier then. Since 2.7.1 (the auto pilot tried
        /// on the analysis computer, 2026-10-18): the check showed the frame after such a time, so Enter there moved a mark of the
        /// pilot by up to a frame and counted it as corrected, and the answers taught half a frame late on average.
        /// </summary>
        public void SeekNearest(long timestamp)
        {
            if (!HasVideo)
                return;

            SeekTo(Math.Max(frameServer.VideoReader.WorkingZone.Start, timestamp - HalfFrame));
        }
        #endregion

        #region Angles
        /// <summary>
        /// Measure an angle with three clicks on the paused video: one end, the vertex, the other end.
        /// </summary>
        public void BeginAngle()
        {
            if (PickTarget == SwimPickTarget.Angle)
            {
                CancelPick();
                return;
            }

            if (!HasVideo || Race == null)
            {
                Status(SwimLang.T("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής."), true);
                return;
            }

            angleClicks.Clear();
            pendingMeasure = SwimPickTarget.None;
            SetPick(SwimPickTarget.Angle);
            Status(AnglePrompt(), false);
        }

        private string AnglePrompt()
        {
            string[] what = { SwimLang.T("το ένα άκρο (π.χ. ο αστράγαλος)"), SwimLang.T("την κορυφή της γωνίας (π.χ. το γόνατο)"), SwimLang.T("το άλλο άκρο (π.χ. το ισχίο)") };
            int i = Math.Min(angleClicks.Count, 2);
            return string.Format(CultureInfo.InvariantCulture, SwimLang.T("📐 Γωνία «{0}» – κλικ {1}/3 στο {2}. Esc: ακύρωση."), AngleName, i + 1, what[i]);
        }

        private void AngleClick(PointF point, long timestamp)
        {
            if (angleClicks.Count > 0 && !SameFrame(angleFrame, timestamp))
            {
                angleClicks.Clear();
                Status(SwimLang.T("Το καρέ άλλαξε: η γωνία ξεκινά ξανά σε αυτό το καρέ."), true);
            }

            if (angleClicks.Count == 0)
                angleFrame = timestamp;

            angleClicks.Add(point);
            if (angleClicks.Count < 3)
            {
                Status(AnglePrompt(), false);
                InvalidatePlayer();
                return;
            }

            SwimAngle angle = new SwimAngle { Name = string.IsNullOrWhiteSpace(AngleName) ? "Γωνία" : AngleName.Trim(), Timestamp = angleFrame };
            angle.Points = angleClicks.Select(p => new SwimPoint(p)).ToList();
            angleClicks.Clear();
            SetPick(SwimPickTarget.None);

            double? deg = angle.Degrees;
            if (!deg.HasValue)
            {
                SystemSounds.Exclamation.Play();
                Status(SwimLang.T("Τα σημεία είναι πολύ κοντά: η γωνία δεν μετρήθηκε."), true);
                InvalidatePlayer();
                return;
            }

            Race.Angles.Add(angle);
            SwimTechPhase phase = SwimTechCalculator.PhaseAt(TechPhases, angle.Timestamp);
            Status(string.Format(CultureInfo.InvariantCulture, "📐 {0}: {1:0.0}°{2}", SwimLang.Label(angle.Name), deg.Value, phase != null ? " (" + phase.Title + ")" : ""), false);
            Changed(true);
        }

        public void DeleteAngle(SwimAngle angle)
        {
            if (Race == null || angle == null)
                return;

            Race.Angles.Remove(angle);
            Changed(true);
        }

        public void RenameAngle(SwimAngle angle, string name)
        {
            if (angle == null || string.IsNullOrWhiteSpace(name))
                return;

            angle.Name = name.Trim();
            Changed(false);
            ScheduleSend();
        }
        #endregion

        #region Snapshots
        /// <summary>
        /// Keep the current frame (with the drawings and the angles of the frame) for the technical report.
        /// </summary>
        public void TakeSnapshot(long timestamp)
        {
            if (!HasVideo || Race == null)
            {
                Status(SwimLang.T("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής."), true);
                return;
            }

            // One snapshot per frame: the automatic one of an event can be there already (it gets the comments).
            SwimSnapshot same = Race.Snapshots.FirstOrDefault(x => SameFrame(x.Timestamp, timestamp));
            if (same != null)
            {
                Status(SwimLang.T("📷 Υπάρχει ήδη στιγμιότυπο σε αυτό το καρέ") + (string.IsNullOrEmpty(same.Event) ? "" : " (" + SwimLang.Label(same.Event) + ")") +
                    SwimLang.T(": γράψε τα σχόλιά σου στην καρτέλα Στιγμιότυπα."), false);
                if (form != null)
                    form.SnapshotAdded(same);
                return;
            }

            Bitmap image = CaptureFrame(timestamp, Settings.OverlayInExports, false);
            if (image == null)
                return;

            SwimSnapshot snapshot;
            try
            {
                snapshot = AddSnapshot(image, timestamp, "", "");
            }
            finally
            {
                image.Dispose();
            }

            if (snapshot == null)
                return;

            SwimTechPhase phase = PhaseOf(timestamp);
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("📷 Στιγμιότυπο{0}{1}. Γράψε τα σχόλιά σου στην καρτέλα Στιγμιότυπα."),
                phase != null ? " · " + phase.Title : "", string.IsNullOrEmpty(snapshot.Stage) ? "" : " · " + SwimLang.Label(snapshot.Stage)), false);
            Changed(false);
            UploadSnapshot(snapshot);
            if (form != null)
                form.SnapshotAdded(snapshot);
        }

        /// <summary>The image as a snapshot of the race (saved next to the video), null if it could not be saved.</summary>
        private SwimSnapshot AddSnapshot(Bitmap image, long timestamp, string autoEvent, string markId)
        {
            SwimSnapshot snapshot = new SwimSnapshot { Timestamp = timestamp, Event = autoEvent ?? "", MarkId = markId ?? "" };
            snapshot.Stage = SuggestStage(PhaseOf(timestamp), timestamp);
            try
            {
                snapshot.File = Path.GetFileName(SaveSnapshotImage(image, snapshot.Id));
            }
            catch (Exception e)
            {
                log.Error("Could not save the snapshot.", e);
                Status(SwimLang.T("Δεν αποθηκεύτηκε το στιγμιότυπο: ") + e.Message, true);
                return null;
            }

            Race.Snapshots.Add(snapshot);
            return snapshot;
        }

        private SwimTechPhase PhaseOf(long timestamp)
        {
            return SwimTechCalculator.PhaseAt(TechPhases, timestamp);
        }

        public void SetAutoSnapshots(bool on)
        {
            Settings.AutoTechSnapshots = on;
            Settings.Save();
            if (!on)
                ClearWaitingFrames();
        }

        /// <summary>
        /// Technical analysis: the frame of each event of a start or a turn becomes a snapshot when the event is marked (or pressed
        /// again on its mark), with the stage of the phase and the event written on it, as the analyst would take it: the signal, the
        /// first movement, the hands and the back foot off the block, the release, the entry, the first dolphin kick, the breakout, the
        /// first stroke; the last stroke before the wall, the start of the rotation, the contact with the wall, the push-off. The same
        /// event marked again at another frame replaces its snapshot, undoing the mark removes it; the snapshot key still takes any
        /// other frame. A stroke or a rotation marked before the contact with the wall is kept until the wall is marked (then it is
        /// known to be the last stroke of the turn): the video has left that frame by then.
        /// </summary>
        private void AutoSnapshots(PlayerScreenCommands command, long timestamp, string newMarkId, bool commandStatus)
        {
            if (Race == null || !HasVideo || Settings == null || !Settings.AutoTechSnapshots)
                return;

            List<SwimSnapshot> taken = new List<SwimSnapshot>();

            // The start signal of a race is not a mark: its frame at once.
            if (command == PlayerScreenCommands.SwimStart && !Race.IsTraining && Race.StartTimestamp.HasValue && SameFrame(Race.StartTimestamp.Value, timestamp))
            {
                SwimTechPhase phase = PhaseOf(timestamp);
                SwimSnapshot here = Race.Snapshots.FirstOrDefault(x => SameFrame(x.Timestamp, timestamp));
                if (phase != null && phase.Kind == SwimPhaseKind.Start && here != null)
                {
                    if (string.IsNullOrEmpty(here.Event))
                    {
                        NameSnapshot(here, "Σήμα εκκίνησης", "", phase);
                        taken.Add(here);
                    }
                }
                else if (phase != null && phase.Kind == SwimPhaseKind.Start)
                {
                    Bitmap image = CaptureFrame(timestamp, Settings.OverlayInExports, false, true);
                    if (image != null)
                    {
                        try
                        {
                            SwimSnapshot snapshot = SaveAutoSnapshot(image, timestamp, "Σήμα εκκίνησης", "", phase);
                            if (snapshot != null)
                                taken.Add(snapshot);
                        }
                        finally
                        {
                            image.Dispose();
                        }
                    }
                }
            }

            // The mark of this frame: just put, or there already (the key pressed on a stroke found by the program, for example).
            SwimMark mark = newMarkId != null ? Race.Marks.FirstOrDefault(m => m.Id == newMarkId) : null;
            if (mark == null)
            {
                SwimMarkType type;
                if (MarkTypeOf(command, out type))
                    mark = Race.Marks.Where(m => m.Type == type && SameFrame(m.Timestamp, timestamp)).OrderBy(m => Math.Abs(m.Timestamp - timestamp)).FirstOrDefault();
            }

            if (mark != null && SameFrame(mark.Timestamp, timestamp) && !waitingFrames.Any(w => w.Key == mark.Id))
            {
                // One snapshot per frame: one there already (the analyst's) gets the name of the event instead.
                SwimSnapshot here = Race.Snapshots.FirstOrDefault(x => SameFrame(x.Timestamp, mark.Timestamp));
                if (here != null)
                {
                    SwimTechPhase phase;
                    string label = EventLabel(mark, out phase);
                    if (label != null && string.IsNullOrEmpty(here.Event))
                    {
                        NameSnapshot(here, label, mark.Id, phase);
                        taken.Add(here);
                    }
                }
                else if (MayBecomeEvent(mark))
                {
                    Bitmap image = CaptureFrame(timestamp, Settings.OverlayInExports, false, true);
                    if (image != null)
                    {
                        waitingFrames.Add(new KeyValuePair<string, Bitmap>(mark.Id, image));
                        while (waitingFrames.Count > WaitingFrames)
                        {
                            waitingFrames[0].Value.Dispose();
                            waitingFrames.RemoveAt(0);
                        }
                    }
                }
            }

            // Every frame kept whose mark is now an event of its phase becomes its snapshot (the others wait, a mark undone drops its frame).
            for (int i = waitingFrames.Count - 1; i >= 0; i--)
            {
                KeyValuePair<string, Bitmap> w = waitingFrames[i];
                SwimMark m = Race.Marks.FirstOrDefault(x => x.Id == w.Key);
                SwimTechPhase phase = null;
                string label = m != null ? EventLabel(m, out phase) : null;
                if (m != null && label == null)
                    continue;

                waitingFrames.RemoveAt(i);
                try
                {
                    if (m != null)
                    {
                        SwimSnapshot snapshot = SaveAutoSnapshot(w.Value, m.Timestamp, label, m.Id, phase);
                        if (snapshot != null)
                            taken.Add(snapshot);
                    }
                }
                finally
                {
                    w.Value.Dispose();
                }
            }

            if (taken.Count == 0)
                return;

            // The status of the mark stays, the snapshot is said after it.
            string said = "📷 " + string.Join(", ", taken.OrderBy(x => x.Timestamp).Select(x => SwimLang.Label(x.Event)).ToArray());
            if (commandStatus && !string.IsNullOrEmpty(LastStatus))
                Status(LastStatus + "   " + said, LastStatusIsWarning);
            else
                Status(said + SwimLang.T(" (στιγμιότυπο στην καρτέλα Στιγμιότυπα)"), false);

            MarkDirty();
            foreach (SwimSnapshot snapshot in taken.OrderBy(x => x.Timestamp))
                UploadSnapshot(snapshot);
            if (form != null)
                form.SnapshotAdded(taken[taken.Count - 1]);
        }

        /// <summary>
        /// The snapshot of an event of a phase from the frame kept (one taken by the analyst at that frame meanwhile gets the name
        /// instead): the one of the same event before (another frame) goes, its comments and elite frame stay with the new one.
        /// </summary>
        private SwimSnapshot SaveAutoSnapshot(Bitmap image, long timestamp, string label, string markId, SwimTechPhase phase)
        {
            SwimSnapshot here = Race.Snapshots.FirstOrDefault(x => string.IsNullOrEmpty(x.Event) && SameFrame(x.Timestamp, timestamp));
            if (here != null)
            {
                NameSnapshot(here, label, markId, phase);
                return here;
            }

            SwimSnapshot snapshot = AddSnapshot(image, timestamp, label, markId);
            if (snapshot == null)
                return null;

            ReplaceEventSnapshots(snapshot, phase, markId);
            return snapshot;
        }

        /// <summary>A snapshot taken by the analyst at the frame of an event gets its name (Undo of the mark takes the name back only).</summary>
        private void NameSnapshot(SwimSnapshot snapshot, string label, string markId, SwimTechPhase phase)
        {
            snapshot.Event = label;
            snapshot.MarkId = markId ?? "";
            namedSnapshots.Add(snapshot.Id);
            ReplaceEventSnapshots(snapshot, phase, markId);
        }

        /// <summary>
        /// The other snapshots of the same event of the phase go (kept for the Undo of the new mark); their comments and elite frame
        /// stay with this one when it has none.
        /// </summary>
        private void ReplaceEventSnapshots(SwimSnapshot snapshot, SwimTechPhase phase, string markId)
        {
            List<SwimSnapshot> old = Race.Snapshots.Where(x => x != snapshot && x.Event == snapshot.Event && PhaseOf(x.Timestamp) == phase).ToList();
            foreach (SwimSnapshot before in old)
            {
                if (string.IsNullOrEmpty(snapshot.Comment))
                    snapshot.Comment = before.Comment ?? "";
                if (string.IsNullOrEmpty(snapshot.EliteFileId) && !string.IsNullOrEmpty(before.EliteFileId))
                {
                    snapshot.EliteFileId = before.EliteFileId;
                    snapshot.EliteLabel = before.EliteLabel;
                    snapshot.EliteComment = before.EliteComment;
                }

                Race.Snapshots.Remove(before);
            }

            if (old.Count > 0 && !string.IsNullOrEmpty(markId))
                replacedSnapshots[markId] = old;
        }

        /// <summary>
        /// The mark of an event is undone: its snapshot goes (one taken by the analyst only loses the name, one he wrote on stays) and
        /// the snapshots it replaced come back.
        /// </summary>
        private void UndoAutoSnapshots(SwimMark mark)
        {
            List<SwimSnapshot> back;
            bool restore = replacedSnapshots.TryGetValue(mark.Id, out back);
            if (restore)
                replacedSnapshots.Remove(mark.Id);

            foreach (SwimSnapshot x in Race.Snapshots.Where(s => !string.IsNullOrEmpty(s.MarkId) && s.MarkId == mark.Id).ToList())
            {
                if (namedSnapshots.Remove(x.Id))
                {
                    x.Event = "";
                    x.MarkId = "";
                    continue;
                }

                // What it carries from the one it replaced goes back with that one.
                bool written = !restore && (!string.IsNullOrEmpty(x.Comment) || !string.IsNullOrEmpty(x.EliteFileId));
                if (!written)
                    Race.Snapshots.Remove(x);
            }

            if (restore)
            {
                foreach (SwimSnapshot x in back)
                {
                    if (!Race.Snapshots.Contains(x))
                        Race.Snapshots.Add(x);
                }
            }
        }

        /// <summary>
        /// The event of its phase a mark is (the name of its snapshot), or null: the first movement, the hands, the back foot, the
        /// release and the entry of a start, the push-off of a turn, the first dolphin kick and the breakout of each, the last stroke
        /// before the wall and the first after the breakout, the rotation, the contact with the wall.
        /// </summary>
        private string EventLabel(SwimMark m, out SwimTechPhase phase)
        {
            phase = PhaseOf(m.Timestamp);
            SwimTechPhase p = phase;
            if (p == null)
                return null;

            double t = Timing.Seconds(m.Timestamp, p.Origin);
            double near = Math.Max(0.011, 0.55 * Timing.Seconds(m.Timestamp + (long)Math.Round(Math.Max(1, Timing.AverageTimestampsPerFrame)), m.Timestamp));
            Func<double?, bool> at = x => x.HasValue && Math.Abs(t - x.Value) <= near;
            bool start = p.Kind == SwimPhaseKind.Start;
            switch (m.Type)
            {
                case SwimMarkType.Signal:
                    return start && at(0) ? "Σήμα εκκίνησης" : null;
                case SwimMarkType.Reaction:
                    return start && t > 0 ? "Αντίδραση" : null;
                case SwimMarkType.HandsOff:
                    return start && t > 0 ? "Χέρια φεύγουν από τον βατήρα" : null;
                case SwimMarkType.BackFoot:
                    return start && t > 0 ? "Πίσω πόδι φεύγει" : null;
                case SwimMarkType.FeetOff:
                    return start ? (at(p.Block) ? "Αποδέσμευση από τον βατήρα" : null) : (at(p.Pushoff) ? "Αποκόλληση από τον τοίχο" : null);
                case SwimMarkType.Entry:
                    return start && at(p.Entry) ? "Είσοδος στο νερό" : null;
                case SwimMarkType.Kick:
                    return p.Kicks.Count > 0 && at(p.Kicks[0]) ? "Πρώτος δελφινισμός" : null;
                case SwimMarkType.Breakout:
                    return at(p.Breakout) ? "Έξοδος από το νερό" : null;
                case SwimMarkType.Stroke:
                    return !start && t < 0 && at(p.LastStroke) ? "Τελευταία χεριά" : at(p.FirstStroke) ? "Πρώτη χεριά" : null;
                case SwimMarkType.RotationStart:
                    return !start && at(p.RotationStart) ? "Αρχή περιστροφής" : null;
                case SwimMarkType.WallContact:
                    return !start && at(0) ? "Επαφή στον τοίχο" : null;
                case SwimMarkType.Split:
                    return !start && m.Distance == p.Wall && at(0) ? "Επαφή στον τοίχο" : null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// A mark whose frame is kept: an event of its phase now, or a stroke or a rotation that becomes one when the wall after it is
        /// marked (a kick, when the entry or the push-off before it is marked).
        /// </summary>
        private bool MayBecomeEvent(SwimMark m)
        {
            SwimTechPhase phase;
            if (EventLabel(m, out phase) != null)
                return true;

            switch (m.Type)
            {
                case SwimMarkType.Stroke:
                case SwimMarkType.RotationStart:
                case SwimMarkType.Kick:
                case SwimMarkType.FeetOff:
                case SwimMarkType.Breakout:
                case SwimMarkType.Entry:
                    return true;
                default:
                    return false;
            }
        }

        private static bool MarkTypeOf(PlayerScreenCommands command, out SwimMarkType type)
        {
            switch (command)
            {
                case PlayerScreenCommands.SwimStroke: type = SwimMarkType.Stroke; return true;
                case PlayerScreenCommands.SwimKick: type = SwimMarkType.Kick; return true;
                case PlayerScreenCommands.SwimBreakout: type = SwimMarkType.Breakout; return true;
                case PlayerScreenCommands.SwimFeetOff: type = SwimMarkType.FeetOff; return true;
                case PlayerScreenCommands.SwimEntry: type = SwimMarkType.Entry; return true;
                case PlayerScreenCommands.SwimReaction: type = SwimMarkType.Reaction; return true;
                case PlayerScreenCommands.SwimHandsOff: type = SwimMarkType.HandsOff; return true;
                case PlayerScreenCommands.SwimBackFoot: type = SwimMarkType.BackFoot; return true;
                case PlayerScreenCommands.SwimRotation: type = SwimMarkType.RotationStart; return true;
                case PlayerScreenCommands.SwimSplit: type = SwimMarkType.Split; return true;
                default: type = SwimMarkType.Split; return false;
            }
        }

        private void ClearWaitingFrames()
        {
            foreach (KeyValuePair<string, Bitmap> w in waitingFrames)
                w.Value.Dispose();
            waitingFrames.Clear();
            replacedSnapshots.Clear();
            namedSnapshots.Clear();
        }

        /// <summary>
        /// The current frame with the drawings, the angles of the frame and, if asked, the graphics of the video
        /// (race board, velocity chart, values of the start or turn). At most 1280 x 720.
        /// finalFrame: the whole race board and chart, even if they are hidden on the screen.
        /// </summary>
        private Bitmap CaptureFrame(long timestamp, bool graphics, bool finalFrame, bool live = false)
        {
            Size size = frameServer.VideoReader.Info.ReferenceSize;
            if (size.Width <= 0 || size.Height <= 0)
                return null;

            // A snapshot taken while the video plays (an event marked on the run) does not stop it: the frame shown, as decoded.
            bool playing = live && player.IsCurrentlyPlaying;
            Bitmap full = null;
            try
            {
                // The graphics are drawn below, once, with the choices of this capture.
                suppressExportOverlay = true;
                if (!playing)
                    player.BeforeExportVideo();
                full = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
                player.PaintFlushedImage(full);
            }
            catch (Exception e)
            {
                log.Error("Could not capture the frame.", e);
                Status(SwimLang.T("Δεν ήταν δυνατή η λήψη του καρέ: ") + e.Message, true);
                if (full != null)
                    full.Dispose();
                return null;
            }
            finally
            {
                if (!playing)
                    player.AfterExportVideo();
                suppressExportOverlay = false;
            }

            // The images of the report are at most 1280 wide: the graphics are drawn as on a screen of that width.
            using (Graphics g = Graphics.FromImage(full))
                DrawGraphicsOnImage(g, size, timestamp, graphics, finalFrame, ReportImageWidth);

            // At most 1280 x 720 (Google Sheets accepts images up to 1 million pixels and 2 MB).
            double scale = Math.Min(1.0, Math.Min(1280.0 / size.Width, 720.0 / size.Height));
            if (scale >= 0.999)
                return full;

            Bitmap small = new Bitmap(Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)), PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(full, new Rectangle(0, 0, small.Width, small.Height));
            }

            full.Dispose();
            return small;
        }

        private static string SnapshotFallbackFolder
        {
            get { return Path.Combine(Software.SettingsDirectory, "SwimAnalysis", "Snapshots"); }
        }

        #region Graphics of the video on images and the final frame
        /// <summary>
        /// The graphics of the video on an image exported by Kinovea (video, image, clipboard), when enabled in the panel:
        /// angles of the frame, race board, velocity chart, values of the start or turn, with the proportions of the screen.
        /// The video export calls this from a background thread: the drawing is done on the UI thread.
        /// </summary>
        public void DrawExportOverlay(PlayerScreenUserInterface screen, Graphics g, Size outputSize, long timestamp)
        {
            // As on the screen: only for the video of the analysis, while the analysis is on the screen (window or bars).
            if (suppressExportOverlay || screen == null || screen != player || player.IsDisposed || !Active ||
                VideoFile == null || Race == null || Settings == null || !Settings.OverlayInExports)
                return;

            // The proportions of the graphics on the screen (or of a screen 1280 wide if the video was not shown).
            Action draw = () => DrawGraphicsOnImage(g, outputSize, timestamp, true, false,
                lastRenderingSize.Width > 0 ? lastRenderingSize.Width : ReportImageWidth, true);
            try
            {
                if (player.InvokeRequired)
                    player.Invoke(draw);
                else
                    draw();
            }
            catch (Exception e)
            {
                log.Error("Could not draw the swim graphics on the exported image.", e);
            }
        }

        private const float ReportImageWidth = 1280f;

        /// <summary>
        /// Angles of the frame (image coordinates) and, with panels, the board, chart and values of the start or turn,
        /// drawn on the image as they are drawn on a screen of the reference width.
        /// allRace: the race board and chart of the final frame (even if hidden on the screen), without the values of the start or turn.
        /// reel: the technical view instead of the board and the chart when it is on the screen (the video exports, not the report).
        /// </summary>
        private void DrawGraphicsOnImage(Graphics g, Size size, long timestamp, bool panels, bool allRace, float referenceWidth, bool reel = false)
        {
            if (Race == null)
                return;

            lock (overlayLock)
            {
                GraphicsState state = g.Save();
                try
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    foreach (SwimAngle a in Race.Angles.Where(x => SameFrame(x.Timestamp, timestamp)))
                        DrawAngle(g, a, p => p, Math.Max(1f, size.Width / 900f));

                    if (SkeletonShown)
                    {
                        // Reference image pixels → pixels of the exported image.
                        Size reference = frameServer.VideoReader.Info.ReferenceSize;
                        float kx = size.Width / (float)Math.Max(1, reference.Width), ky = size.Height / (float)Math.Max(1, reference.Height);
                        DrawSkeleton(g, timestamp, p => new PointF(p.X * kx, p.Y * ky), Math.Max(1f, size.Width / 900f));
                    }

                    if (panels && size.Width > 0 && size.Height > 0)
                    {
                        float scale = Math.Max(0.25f, size.Width / Math.Max(1f, referenceWidth));
                        Size logical = new Size((int)Math.Round(size.Width / scale), (int)Math.Round(size.Height / scale));
                        g.ScaleTransform(scale, scale);
                        if (reel && !allRace && ReelReady(logical))
                        {
                            // Video image (reference pixels) → logical units of the exported image.
                            Size reference = frameServer.VideoReader.Info.ReferenceSize;
                            float sx = size.Width / (float)Math.Max(1, reference.Width) / scale;
                            float sy = size.Height / (float)Math.Max(1, reference.Height) / scale;
                            DrawReel(g, timestamp, logical, p => new PointF(p.X * sx, p.Y * sy));
                        }
                        else
                        {
                            if (!allRace && (Race.IsTraining || TechnicalView))
                                DrawTechOverlay(g, timestamp, logical);
                            if (!Race.IsTraining)
                                DrawRaceOverlay(g, timestamp, logical, allRace);
                        }
                    }
                }
                catch (Exception e)
                {
                    log.Error("Error while drawing the swim graphics on an image.", e);
                }
                finally
                {
                    g.Restore(state);
                }
            }
        }

        /// <summary>
        /// Frame of the finish with the graphics of the video (final time, velocity chart) for the race report.
        /// Taken when the finish is marked, or from the panel at the current frame. Replaces the previous one.
        /// </summary>
        public void CaptureFinalFrame(long timestamp, bool chosen = false)
        {
            if (!HasVideo || Race == null || Race.IsTraining || !Race.StartTimestamp.HasValue)
                return;

            Bitmap image = CaptureFrame(timestamp, true, true);
            if (image == null)
                return;

            SwimSnapshot frame = new SwimSnapshot { Id = "final", Timestamp = timestamp, Stage = SwimLang.T("Τερματισμός"), Chosen = chosen };
            try
            {
                frame.File = Path.GetFileName(SaveSnapshotImage(image, frame.Id));
            }
            catch (Exception e)
            {
                log.Error("Could not save the final frame.", e);
                Status(SwimLang.T("Δεν αποθηκεύτηκε το τελικό καρέ: ") + e.Message, true);
                return;
            }
            finally
            {
                image.Dispose();
            }

            Race.FinalFrame = frame;
            uploadFailed.Remove(FinalUploadKey(Race));
            MarkDirty();
            UploadFinalFrame(Race);
            if (form != null)
                form.RefreshFinalFrame();
        }

        /// <summary>Final frame from the frame on the screen (button of the panel).</summary>
        public void CaptureFinalFrameNow()
        {
            if (!HasVideo || Race == null)
                return;

            if (Race.IsTraining)
            {
                Status(SwimLang.T("Το τελικό καρέ είναι για την ανάλυση αγώνα."), true);
                return;
            }

            if (!Race.StartTimestamp.HasValue)
            {
                Status(SwimLang.T("Μαρκάρισε πρώτα το σήμα εκκίνησης."), true);
                return;
            }

            CaptureFinalFrame(player.CurrentTimestamp, true);
            Status(SwimLang.T("📷 Τελικό καρέ για το report: από αυτό το καρέ."), false);
        }

        private static string FinalUploadKey(SwimRace race)
        {
            return "final:" + race.Id;
        }

        /// <summary>The final frame of the current race is being uploaded.</summary>
        public bool FinalFrameUploading
        {
            get { return Race != null && uploading.Contains(FinalUploadKey(Race)); }
        }

        /// <summary>The image of the final frame of the current race is on this computer.</summary>
        public bool FinalFrameOnDisk
        {
            get { return Race != null && Race.FinalFrame != null && SnapshotPath(Race.FinalFrame) != null; }
        }

        /// <summary>Upload the final frame to the Drive (in the background); the report shows it after the next update.</summary>
        private void UploadFinalFrame(SwimRace race)
        {
            if (!Settings.IsConfigured || race == null || race.FinalFrame == null || !string.IsNullOrEmpty(race.FinalFrame.FileId))
                return;

            string key = FinalUploadKey(race);
            if (uploading.Contains(key) || uploadFailed.Contains(key))
                return;

            byte[] bytes;
            try
            {
                string path = SnapshotPath(race.FinalFrame);
                if (path == null)
                    return;
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                log.Error("Could not read the final frame.", e);
                uploadFailed.Add(key);
                return;
            }

            SwimSnapshot frame = race.FinalFrame;
            SwimVideoFile video = VideoFile;
            uploading.Add(key);
            client.UploadSnapshot(Settings.WebAppUrl, Settings.Token, race.Id, frame.Id, bytes, result =>
            {
                uploading.Remove(key);

                // Another video was opened meanwhile (uploaded again next time).
                if (VideoFile != video || video == null || !video.Races.Contains(race))
                    return;

                if (race.FinalFrame != frame)
                {
                    // A newer final frame was taken meanwhile: it goes up on its own.
                    UploadFinalFrame(race);
                    return;
                }

                if (result.Ok && !string.IsNullOrEmpty(result.FileId))
                {
                    frame.FileId = result.FileId;
                    MarkDirty();
                    if (race == Race)
                        Changed(true);
                    else
                        SendRace(race, false);
                }
                else
                {
                    uploadFailed.Add(key);
                    Status(SwimLang.T("Το τελικό καρέ κρατήθηκε στον υπολογιστή και θα ανέβει με την επόμενη Αποστολή: ") + result.Error, true);
                }

                if (form != null)
                    form.RefreshFinalFrame();
                UploadPendingSnapshots();
            });
        }
        #endregion

        /// <summary>Folder of the snapshots next to the video ("video.ext.swim"), or in the settings folder.</summary>
        private string SnapshotFolder()
        {
            string next = videoPath + ".swim";
            try
            {
                Directory.CreateDirectory(next);
                return next;
            }
            catch (Exception)
            {
                Directory.CreateDirectory(SnapshotFallbackFolder);
                return SnapshotFallbackFolder;
            }
        }

        /// <summary>
        /// Image file of a snapshot (only its name is kept in the analysis: next to the video or in the settings folder),
        /// or null when it is missing.
        /// </summary>
        public string SnapshotPath(SwimSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.File))
                return null;

            try
            {
                if (Path.IsPathRooted(snapshot.File) && File.Exists(snapshot.File))
                    return snapshot.File;

                string name = Path.GetFileName(snapshot.File);
                List<string> folders = new List<string>();
                if (!string.IsNullOrEmpty(videoPath))
                    folders.Add(videoPath + ".swim");
                folders.Add(SnapshotFallbackFolder);
                foreach (string folder in folders)
                {
                    string path = Path.Combine(folder, name);
                    if (File.Exists(path))
                        return path;
                }
            }
            catch (Exception e)
            {
                log.Error("Could not find the snapshot image.", e);
            }

            return null;
        }

        private string SaveSnapshotImage(Bitmap image, string id)
        {
            string path = Path.Combine(SnapshotFolder(), Race.Id + "_" + id + ".jpg");
            ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.MimeType == "image/jpeg");
            if (codec == null)
            {
                image.Save(path, System.Drawing.Imaging.ImageFormat.Jpeg);
                return path;
            }

            using (EncoderParameters parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                image.Save(path, codec, parameters);
            }

            return path;
        }

        /// <summary>
        /// Stage of the movement at a timestamp, from the events of the phase (the analyst can change it).
        /// </summary>
        private string SuggestStage(SwimTechPhase phase, long timestamp)
        {
            if (phase == null)
                return "";

            double t = Timing.Seconds(timestamp, phase.Origin);
            const double near = 0.04;
            Func<double?, bool> at = x => x.HasValue && Math.Abs(t - x.Value) <= near;
            Func<double?, bool> before = x => x.HasValue && t < x.Value - near;
            if (phase.Kind == SwimPhaseKind.Start)
            {
                if (t < 0.02 || before(phase.Reaction))
                    return "Θέση ετοιμότητας";
                if (at(phase.Block))
                    return "Αποδέσμευση";
                if (before(phase.Block))
                    return "Φάση βατήρα";
                if (at(phase.Entry))
                    return "Είσοδος στο νερό";
                if (before(phase.Entry))
                    return "Πτήση";
                if (at(phase.Breakout))
                    return "Έξοδος από το νερό";
                if (before(phase.Breakout))
                    return "Υποβρύχια φάση";
                return "Κολύμβηση";
            }

            if (Math.Abs(t) <= near)
                return "Επαφή στον τοίχο";
            if (t < 0)
                return phase.RotationStart.HasValue && t >= phase.RotationStart.Value - near ? "Περιστροφή" : "Προσέγγιση";
            if (at(phase.Pushoff))
                return "Αποκόλληση";
            if (before(phase.Pushoff))
                return "Επαφή στον τοίχο";
            if (at(phase.Breakout))
                return "Έξοδος από το νερό";
            if (before(phase.Breakout))
                return "Υποβρύχια φάση";
            return "Κολύμβηση";
        }

        public void SnapshotChanged(SwimSnapshot snapshot)
        {
            Changed(false);
            ScheduleSend();
        }

        public void DeleteSnapshot(SwimSnapshot snapshot)
        {
            if (Race == null || snapshot == null)
                return;

            Race.Snapshots.Remove(snapshot);
            Changed(true);
        }

        /// <summary>
        /// Upload the image of a snapshot to the Drive (in the background, one at a time); the report shows it after the next update.
        /// </summary>
        private void UploadSnapshot(SwimSnapshot snapshot)
        {
            if (!Settings.IsConfigured || snapshot == null || Race == null || !string.IsNullOrEmpty(snapshot.FileId) ||
                uploading.Count > 0 || uploadFailed.Contains(snapshot.Id))
                return;

            byte[] bytes;
            try
            {
                string path = SnapshotPath(snapshot);
                if (path == null)
                    return;
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                log.Error("Could not read the snapshot.", e);
                uploadFailed.Add(snapshot.Id);
                return;
            }

            SwimRace race = Race;
            SwimVideoFile video = VideoFile;
            uploading.Add(snapshot.Id);
            client.UploadSnapshot(Settings.WebAppUrl, Settings.Token, race.Id, snapshot.Id, bytes, result =>
            {
                uploading.Remove(snapshot.Id);

                // Another video was opened meanwhile: its timing and file are not those of this race (uploaded again next time).
                if (VideoFile != video || video == null || !video.Races.Contains(race))
                    return;

                if (result.Ok && !string.IsNullOrEmpty(result.FileId))
                {
                    snapshot.FileId = result.FileId;
                    MarkDirty();
                    if (race == Race)
                        Changed(true);
                    else
                        SendRace(race, false);
                    UploadPendingSnapshots();
                }
                else
                {
                    uploadFailed.Add(snapshot.Id);
                    if (IsUnknownAction(result))
                        Status(SwimLang.T("Για τα στιγμιότυπα στο report χρειάζεται η νέα έκδοση του Apps Script (2.0.0): δες τον οδηγό."), true);
                    else
                        Status(SwimLang.T("Το στιγμιότυπο κρατήθηκε στον υπολογιστή και θα ανέβει με την επόμενη Αποστολή: ") + result.Error, true);
                }

                if (form != null)
                    form.RefreshTech();
            });
        }

        /// <summary>Snapshots of the current analysis that did not reach the Drive yet (offline, no connection settings).</summary>
        private void UploadPendingSnapshots()
        {
            if (VideoFile == null || Race == null || !Settings.IsConfigured)
                return;

            UploadFinalFrame(Race);
            if (uploading.Count > 0)
                return;

            SwimSnapshot next = Race.Snapshots.FirstOrDefault(x => string.IsNullOrEmpty(x.FileId) && !uploadFailed.Contains(x.Id) && SnapshotPath(x) != null);
            if (next != null)
                UploadSnapshot(next);
        }

        /// <summary>A manual send also retries the snapshots that could not be uploaded.</summary>
        private void RetryFailedUploads()
        {
            uploadFailed.Clear();
            UploadPendingSnapshots();
        }

        /// <summary>Image of a snapshot from its file next to the video (null if missing).</summary>
        public static Image LoadImage(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return null;

                using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(path)))
                using (Image image = Image.FromStream(stream))
                    return new Bitmap(image);
            }
            catch (Exception)
            {
                return null;
            }
        }
        #endregion

        #region Elite frames
        public void RefreshEliteFrames()
        {
            if (!Settings.IsConfigured)
            {
                EliteMessage = SwimLang.T("Ρύθμισε πρώτα τη σύνδεση για τη βιβλιοθήκη elite καρέ.");
                if (form != null)
                    form.RefreshTech();
                return;
            }

            if (EliteLoading)
                return;

            EliteLoading = true;
            EliteMessage = SwimLang.T("Φόρτωση elite καρέ…");
            client.GetEliteFrames(Settings.WebAppUrl, Settings.Token, result =>
            {
                EliteLoading = false;
                if (result.Ok && result.Raw != null)
                {
                    List<SwimEliteFrame> frames = new List<SwimEliteFrame>();
                    JArray array = result.Raw["frames"] as JArray;
                    if (array != null)
                    {
                        foreach (JToken token in array)
                        {
                            SwimEliteFrame f = SwimEliteFrame.FromJson(token as JObject);
                            if (f != null)
                                frames.Add(f);
                        }
                    }

                    EliteLibrary.Frames = frames;
                    EliteLibrary.Updated = DateTime.Now;
                    EliteLibrary.Save();
                    EliteMessage = string.Format(SwimLang.T("Elite καρέ: {0} (ενημέρωση {1:dd/MM HH:mm})."), frames.Count, EliteLibrary.Updated);
                }
                else if (IsUnknownAction(result))
                {
                    EliteMessage = SwimLang.T("Για τα elite καρέ χρειάζεται η νέα έκδοση του Apps Script (2.0.0).");
                }
                else
                {
                    EliteMessage = SwimLang.T("Τα elite καρέ δεν ενημερώθηκαν: ") + result.Error;
                }

                if (form != null)
                    form.RefreshTech();
            });
        }

        public void RefreshEliteFramesIfStale()
        {
            if (Settings.IsConfigured && (EliteLibrary.Frames.Count == 0 || (DateTime.Now - EliteLibrary.Updated).TotalHours > 6))
                RefreshEliteFrames();
        }

        /// <summary>
        /// Image of an elite frame: from the local cache, otherwise downloaded once. The callback runs on the UI thread (null if not available).
        /// </summary>
        public void GetEliteImage(string fileId, Action<Image> callback)
        {
            if (string.IsNullOrEmpty(fileId))
            {
                callback(null);
                return;
            }

            string path = SwimEliteLibrary.ImagePath(fileId);
            Image cached = LoadImage(path);
            if (cached != null || !Settings.IsConfigured)
            {
                callback(cached);
                return;
            }

            List<Action<Image>> waiting;
            if (eliteImageRequests.TryGetValue(fileId, out waiting))
            {
                waiting.Add(callback);
                return;
            }

            eliteImageRequests[fileId] = new List<Action<Image>> { callback };
            client.GetImage(Settings.WebAppUrl, Settings.Token, fileId, result =>
            {
                Image image = null;
                if (result.Ok && result.Raw != null)
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(result.Raw.Value<string>("data") ?? "");
                        Directory.CreateDirectory(SwimEliteLibrary.ImageFolder);
                        File.WriteAllBytes(path, bytes);
                        image = LoadImage(path);
                    }
                    catch (Exception e)
                    {
                        log.Error("Could not store the elite frame.", e);
                    }
                }

                List<Action<Image>> callbacks;
                if (eliteImageRequests.TryGetValue(fileId, out callbacks))
                {
                    eliteImageRequests.Remove(fileId);
                    foreach (Action<Image> c in callbacks)
                        c(image == null ? null : new Bitmap(image));
                }

                if (image != null)
                    image.Dispose();
            });
        }

        /// <summary>Compare a snapshot with an elite frame in the report (null: no comparison).</summary>
        public void SetSnapshotElite(SwimSnapshot snapshot, SwimEliteFrame frame)
        {
            if (snapshot == null)
                return;

            snapshot.EliteFileId = frame != null ? frame.FileId : "";
            snapshot.EliteLabel = frame != null ? frame.Label + (string.IsNullOrWhiteSpace(frame.Athlete) ? "" : " (" + frame.Athlete + ")") : "";
            snapshot.EliteComment = frame != null ? frame.Comment : "";
            Changed(true);
        }

        /// <summary>
        /// Keep a snapshot as an elite frame of the library (e.g. from the video of an elite swimmer).
        /// </summary>
        public void SaveSnapshotAsElite(SwimSnapshot snapshot, string label, string athlete, string source, string comment)
        {
            if (snapshot == null || !Settings.IsConfigured)
            {
                Status(SwimLang.T("Ρύθμισε πρώτα τη σύνδεση με το Google Sheet."), true);
                return;
            }

            byte[] bytes;
            try
            {
                string path = SnapshotPath(snapshot);
                if (path == null)
                {
                    Status(SwimLang.T("Δεν βρέθηκε η εικόνα του στιγμιότυπου δίπλα στο βίντεο."), true);
                    return;
                }

                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                Status(SwimLang.T("Δεν βρέθηκε η εικόνα του στιγμιότυπου: ") + e.Message, true);
                return;
            }

            SwimTechPhase phase = SwimTechCalculator.PhaseAt(TechPhases, snapshot.Timestamp);
            JObject frame = new JObject();
            frame["label"] = label ?? "";
            frame["style"] = Race.Style == "im" ? "" : Race.Style;
            frame["kind"] = phase != null && phase.Kind == SwimPhaseKind.Turn ? "turn" : "start";
            frame["stage"] = snapshot.Stage ?? "";
            frame["athlete"] = athlete ?? "";
            frame["source"] = source ?? "";
            frame["comment"] = comment ?? "";
            Status(SwimLang.T("Αποστολή του elite καρέ…"), false);
            client.SaveEliteFrame(Settings.WebAppUrl, Settings.Token, frame, bytes, result =>
            {
                if (result.Ok && result.Raw != null)
                {
                    SwimEliteFrame saved = SwimEliteFrame.FromJson(result.Raw["frame"] as JObject);
                    if (saved != null)
                    {
                        EliteLibrary.Frames.Add(saved);
                        EliteLibrary.Save();
                        try
                        {
                            Directory.CreateDirectory(SwimEliteLibrary.ImageFolder);
                            File.WriteAllBytes(SwimEliteLibrary.ImagePath(saved.FileId), bytes);
                        }
                        catch (Exception e)
                        {
                            log.Error("Could not cache the elite frame.", e);
                        }
                    }

                    Status(SwimLang.T("✔ Το καρέ μπήκε στη βιβλιοθήκη elite καρέ (φύλλο «Elite καρέ»)."), false);
                }
                else
                {
                    Status(IsUnknownAction(result) ? SwimLang.T("Για τα elite καρέ χρειάζεται η νέα έκδοση του Apps Script (2.0.0).") : SwimLang.T("Το elite καρέ δεν αποθηκεύτηκε: ") + result.Error, true);
                }

                if (form != null)
                    form.RefreshTech();
            });
        }
        #endregion

        #region Comparison with a previous analysis (PB)
        /// <summary>
        /// Other analyses of the athlete in the same event, from the Google Sheet (for the PB of the report).
        /// </summary>
        public void RefreshPbCandidates()
        {
            if (Race == null || !Settings.IsConfigured || Race.IsTraining)
                return;

            if (string.IsNullOrWhiteSpace(Race.AthleteId) && string.IsNullOrWhiteSpace(Race.Athlete))
            {
                PbCandidates = new List<SwimAnalysisItem>();
                PbMessage = SwimLang.T("Επίλεξε αθλητή για τη σύγκριση με προηγούμενο αγώνα.");
                if (form != null)
                    form.RefreshPb();
                return;
            }

            JObject query = new JObject();
            query["athleteId"] = Race.AthleteId ?? "";
            query["athlete"] = Race.Athlete ?? "";
            query["style"] = Race.Style;
            query["distance"] = Race.Distance;
            query["relay"] = Race.Relay;
            query["pool"] = Race.Pool;
            query["poolUnit"] = Race.IsYards ? SwimRace.UnitYards : SwimRace.UnitMeters;
            query["raceId"] = Race.Id;
            string raceId = Race.Id;
            PbMessage = SwimLang.T("Φόρτωση προηγούμενων αναλύσεων…");
            client.GetAnalyses(Settings.WebAppUrl, Settings.Token, query, result =>
            {
                if (Race == null || Race.Id != raceId)
                    return;

                List<SwimAnalysisItem> list = new List<SwimAnalysisItem>();
                if (result.Ok && result.Raw != null)
                {
                    JArray array = result.Raw["analyses"] as JArray;
                    if (array != null)
                    {
                        foreach (JToken token in array)
                        {
                            JObject o = token as JObject;
                            if (o == null)
                                continue;
                            list.Add(new SwimAnalysisItem { Id = o.Value<string>("id") ?? "", Label = o.Value<string>("label") ?? "", FinalTime = o.Value<double?>("finalTime") });
                        }
                    }

                    PbMessage = list.Count == 0 ? SwimLang.T("Δεν υπάρχει άλλη ανάλυση του αθλητή σε αυτό το αγώνισμα.") : string.Format(SwimLang.T("{0} προηγούμενες αναλύσεις."), list.Count);
                }
                else
                {
                    PbMessage = IsUnknownAction(result) ? SwimLang.T("Για τη σύγκριση με PB χρειάζεται η νέα έκδοση του Apps Script (2.0.0).") : SwimLang.T("Δεν φορτώθηκαν οι αναλύσεις: ") + result.Error;
                }

                PbCandidates = list;
                if (form != null)
                    form.RefreshPb();
            });
        }

        /// <summary>"auto" (best earlier time), "" (none) or the id of another analysis.</summary>
        public void SetComparePb(string id, string label)
        {
            if (Race == null)
                return;

            Race.ComparePb = id ?? "";
            Race.ComparePbLabel = label ?? "";
            Changed(true);
        }
        #endregion

        #region Overlay of the technical analysis
        private static readonly Pen penAngle = new Pen(Color.FromArgb(240, 0, 200, 255), 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        private static readonly Pen penAngleArc = new Pen(Color.FromArgb(230, 255, 176, 0), 2f);
        private static readonly SolidBrush brushAnglePoint = new SolidBrush(Color.FromArgb(250, 255, 255, 255));

        /// <summary>
        /// Angles of this frame and the clicks of the angle in progress.
        /// </summary>
        private void DrawAngles(Graphics g, ImageTransform transform, long timestamp)
        {
            if (Race == null)
                return;

            float scale = 1.0f;
            foreach (SwimAngle a in Race.Angles.Where(x => SameFrame(x.Timestamp, timestamp)))
                DrawAngle(g, a, p => transform.Transform(p), scale);

            if (PickTarget == SwimPickTarget.Angle && angleClicks.Count > 0 && SameFrame(angleFrame, timestamp))
            {
                List<Point> pts = angleClicks.Select(p => transform.Transform(p)).ToList();
                for (int i = 0; i + 1 < pts.Count; i++)
                    g.DrawLine(penAngle, pts[i], pts[i + 1]);
                foreach (Point p in pts)
                    g.FillEllipse(brushAnglePoint, p.X - 4, p.Y - 4, 8, 8);
            }
        }

        /// <summary>
        /// One angle: the two arms, the arc at the vertex and the value.
        /// </summary>
        private static void DrawAngle(Graphics g, SwimAngle a, Func<PointF, PointF> map, float scale)
        {
            if (a.Points == null || a.Points.Count != 3)
                return;

            PointF p0 = map(a.Points[0].PointF), v = map(a.Points[1].PointF), p2 = map(a.Points[2].PointF);
            using (Pen arm = new Pen(penAngle.Color, penAngle.Width * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(arm, v, p0);
                g.DrawLine(arm, v, p2);
            }

            double a0 = Math.Atan2(p0.Y - v.Y, p0.X - v.X) * 180 / Math.PI;
            double a2 = Math.Atan2(p2.Y - v.Y, p2.X - v.X) * 180 / Math.PI;
            double sweep = a2 - a0;
            while (sweep > 180) sweep -= 360;
            while (sweep < -180) sweep += 360;
            float radius = 26 * scale;
            using (Pen arc = new Pen(penAngleArc.Color, penAngleArc.Width * scale))
                g.DrawArc(arc, v.X - radius, v.Y - radius, radius * 2, radius * 2, (float)a0, (float)sweep);

            float r = 4 * scale;
            foreach (PointF p in new PointF[] { p0, v, p2 })
                g.FillEllipse(brushAnglePoint, p.X - r, p.Y - r, r * 2, r * 2);

            double? deg = a.Degrees;
            if (!deg.HasValue)
                return;

            double mid = (a0 + sweep / 2) * Math.PI / 180;
            PointF label = new PointF(v.X + (float)Math.Cos(mid) * (radius + 14 * scale), v.Y + (float)Math.Sin(mid) * (radius + 14 * scale));
            string text = string.Format(CultureInfo.InvariantCulture, "{0:0.0}°", deg.Value);
            Font font = SwimTheme.Display(10f * scale);
            SizeF size = g.MeasureString(text, font);
            RectangleF box = new RectangleF(label.X - size.Width / 2 - 4, label.Y - size.Height / 2 - 1, size.Width + 8, size.Height + 2);
            SwimTheme.FillRounded(g, Color.FromArgb(215, 7, 12, 19), box, 4 * scale);
            using (SolidBrush brush = new SolidBrush(SwimTheme.Amber))
                g.DrawString(text, font, brush, box.X + 4, box.Y + 1);
            if (!string.IsNullOrWhiteSpace(a.Name))
            {
                Font small = SwimTheme.Ui(7.5f * scale, FontStyle.Bold);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(230, 230, 237, 243)))
                    g.DrawString(SwimLang.Label(a.Name), small, brush, box.X, box.Bottom + 1);
            }
        }

        /// <summary>
        /// Values of the start or turn at the current frame, in the order they happen, up to the current time.
        /// </summary>
        private void DrawTechOverlay(Graphics g, long timestamp, Size size)
        {
            SwimTechPhase phase = SwimTechCalculator.PhaseAt(TechPhases, timestamp);
            if (phase == null || size.Width < 300 || size.Height < 200)
                return;

            double now = Timing.Seconds(timestamp, phase.Origin);
            List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
            Action<string, double?> add = (label, t) =>
            {
                if (t.HasValue && t.Value <= now + 1e-6)
                    items.Add(new KeyValuePair<string, string>(label, t.Value.ToString("0.00", CultureInfo.InvariantCulture)));
            };

            string unit = Race.IsYards ? "yd" : "m";
            if (phase.Kind == SwimPhaseKind.Start)
            {
                add(SwimLang.T("ΑΝΤΙΔΡΑΣΗ"), phase.Reaction);
                add(SwimLang.T("ΧΕΡΙΑ"), phase.HandsOff);
                add(SwimLang.T("ΠΙΣΩ ΠΟΔΙ"), phase.BackFoot);
                add(SwimLang.T("ΑΠΟΔΕΣΜΕΥΣΗ"), phase.Block);
                add(SwimLang.T("ΕΙΣΟΔΟΣ"), phase.Entry);
                foreach (KeyValuePair<int, double> s in phase.Splits)
                    add(s.Key.ToString(CultureInfo.InvariantCulture) + " " + unit, s.Value);
                add(SwimLang.T("ΕΞΟΔΟΣ"), phase.Breakout);
            }
            else
            {
                double tIn;
                if (phase.Splits.TryGetValue(-5, out tIn) && tIn <= now)
                    items.Add(new KeyValuePair<string, string>("−5 " + unit, (-tIn).ToString("0.00", CultureInfo.InvariantCulture)));
                if (phase.LastStroke.HasValue && phase.LastStroke.Value <= now)
                    items.Add(new KeyValuePair<string, string>(SwimLang.T("ΧΕΡΙΑ→ΤΟΙΧΟΣ"), (-phase.LastStroke.Value).ToString("0.00", CultureInfo.InvariantCulture)));
                add(SwimLang.T("ΑΠΟΚΟΛΛΗΣΗ"), phase.Pushoff);
                foreach (KeyValuePair<int, double> s in phase.Splits.Where(x => x.Key > 0))
                    add("+" + s.Key.ToString(CultureInfo.InvariantCulture) + " " + unit, s.Value);
                add(SwimLang.T("ΕΞΟΔΟΣ"), phase.Breakout);
            }

            int kicks = phase.Kicks.Count(k => k <= now + 1e-6);
            if (kicks > 0)
                items.Add(new KeyValuePair<string, string>(SwimLang.T("ΔΕΛΦΙΝΙΣΜΟΙ"), kicks.ToString(CultureInfo.InvariantCulture)));

            // Card at the bottom left of the video.
            Font title = SwimTheme.Display(11f);
            Font caption = SwimTheme.Ui(6.5f, FontStyle.Bold);
            Font value = SwimTheme.Display(12f);
            int cellW = 74, cellH = 36, cols = Math.Max(1, Math.Min(items.Count, Math.Max(3, (int)(size.Width * 0.62) / cellW)));
            int rows = items.Count == 0 ? 0 : (items.Count + cols - 1) / cols;
            int width = Math.Max(200, cols * cellW + 16);
            int height = 34 + rows * cellH + 8;
            Rectangle card = new Rectangle(10, size.Height - height - 10, width, height);
            using (GraphicsPath path = SwimTheme.Rounded(card, 6))
            using (SolidBrush back = new SolidBrush(Color.FromArgb(215, 11, 17, 26)))
                g.FillPath(back, path);
            using (SolidBrush accent = new SolidBrush(SwimTheme.Amber))
                g.FillRectangle(accent, card.Left, card.Top + 6, 3, card.Height - 12);

            string clock = (now < 0 ? "−" : "") + Math.Abs(now).ToString("0.00", CultureInfo.InvariantCulture) + " s";
            using (SolidBrush text = new SolidBrush(SwimTheme.Text))
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            using (SolidBrush amber = new SolidBrush(SwimTheme.Amber))
            {
                g.DrawString(phase.Title.ToUpperInvariant(), title, amber, card.Left + 10, card.Top + 7);
                SizeF cs = g.MeasureString(clock, title);
                g.DrawString(clock, title, text, card.Right - cs.Width - 8, card.Top + 7);
                for (int i = 0; i < items.Count; i++)
                {
                    int cx = card.Left + 10 + (i % cols) * cellW;
                    int cy = card.Top + 32 + (i / cols) * cellH;
                    g.DrawString(items[i].Key, caption, dim, cx, cy);
                    g.DrawString(items[i].Value, value, text, cx, cy + 11);
                }
            }
        }
        #endregion
    }
}
