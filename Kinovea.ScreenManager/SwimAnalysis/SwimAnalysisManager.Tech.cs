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
                    AddMark(SwimMarkType.HandsOff, timestamp, "Χέρια φεύγουν από τον βατήρα");
                    return true;
                case PlayerScreenCommands.SwimBackFoot:
                    AddMark(SwimMarkType.BackFoot, timestamp, "Πίσω πόδι φεύγει");
                    return true;
                case PlayerScreenCommands.SwimRotation:
                    AddMark(SwimMarkType.RotationStart, timestamp, "Αρχή περιστροφής");
                    return true;
                case PlayerScreenCommands.SwimWallContact:
                    if (Race.IsTraining)
                        AddMark(SwimMarkType.WallContact, timestamp, "Επαφή στον τοίχο (νέα στροφή)");
                    else
                        AddSplit(timestamp);
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
                Status("Υπάρχει ήδη σήμα εκκίνησης σε αυτό το καρέ.", true);
                return;
            }

            SwimMark mark = new SwimMark { Type = SwimMarkType.Signal, Timestamp = timestamp };
            Race.Marks.Add(mark);
            undoStack.Add(mark.Id);
            int n = Race.Marks.Count(m => m.Type == SwimMarkType.Signal && m.Timestamp <= timestamp);
            Status(string.Format(CultureInfo.InvariantCulture, "Εκκίνηση {0}: σήμα (t = 0 της προσπάθειας).", n), false);
        }

        /// <summary>
        /// Head at a technical distance: the next one of the phase (5, 10, 15 after a start, +5, +10, +15 after a contact,
        /// otherwise −5 before the next wall), or the one asked (buttons of the panel).
        /// </summary>
        public void AddTechSplit(long timestamp, int? forced)
        {
            if (Race == null)
                return;

            if (!Race.IsTraining)
                EnsureStart();

            List<SwimTechPhase> phases = SwimTechCalculator.Compute(Race, Computed, Timing);
            SwimTechPhase phase;
            int offset = SwimTechCalculator.NextOffset(phases, timestamp, out phase);
            if (forced.HasValue)
            {
                offset = forced.Value;
                phase = SwimTechCalculator.PhaseAt(phases, timestamp);
            }

            int wall = -1;
            if (!Race.IsTraining)
            {
                int last = LastWallBefore(timestamp);
                wall = offset < 0 ? last + PoolLength : (phase != null ? phase.Wall : last);
                if (wall >= Race.Distance || (offset < 0 && wall <= 0))
                {
                    SystemSounds.Beep.Play();
                    Status("Δεν υπάρχει άλλη στροφή σε αυτόν τον αγώνα: το −5 δεν μετριέται πριν τον τερματισμό.", true);
                    return;
                }

                if (Race.Checkpoints.Contains(wall + offset) && wall + offset > 0)
                {
                    SystemSounds.Beep.Play();
                    Status(string.Format(CultureInfo.InvariantCulture,
                        "Τα {0}{1} είναι σημείο ελέγχου: μαρκάρεται με το Πέρασμα και μετράει και στην τεχνική ανάλυση.", wall + offset, Race.Unit), true);
                    return;
                }
            }
            else if (offset > 0 && phase == null)
            {
                SystemSounds.Beep.Play();
                Status("Πάτησε πρώτα το σήμα εκκίνησης (εκκίνηση) ή την επαφή στον τοίχο (στροφή) της προσπάθειας.", true);
                return;
            }

            // A new press for the same distance of the same phase replaces the old one.
            SwimTechPhase owner = phase;
            Race.Marks.RemoveAll(m => m.Type == SwimMarkType.TechSplit && m.Distance == offset &&
                (Race.IsTraining ? (owner != null && owner.Contains(m.Timestamp) && (offset < 0 ? m.Timestamp <= owner.Origin : m.Timestamp > owner.Origin)) : m.Wall == wall));

            SwimMark mark = new SwimMark { Type = SwimMarkType.TechSplit, Timestamp = timestamp, Distance = offset, Wall = wall };
            Race.Marks.Add(mark);
            undoStack.Add(mark.Id);

            string where = offset < 0
                ? (Race.IsTraining ? "−5 πριν τον τοίχο" : string.Format(CultureInfo.InvariantCulture, "−5 πριν τον τοίχο των {0}{1}", wall, Race.Unit))
                : (phase != null && phase.Kind == SwimPhaseKind.Turn ? "+" : "") + offset.ToString(CultureInfo.InvariantCulture) + Race.Unit;
            double? t = phase != null ? Timing.Seconds(timestamp, phase.Origin) : (double?)null;
            Status(string.Format(CultureInfo.InvariantCulture, "Τεχνικό πέρασμα {0}{1}", where,
                t.HasValue && offset > 0 ? string.Format(CultureInfo.InvariantCulture, ": {0:0.00} s από {1}", t.Value, phase.Kind == SwimPhaseKind.Start ? "το σήμα" : "την επαφή") : "."), false);
        }

        /// <summary>
        /// Panel buttons: technical split at the current frame of the video.
        /// </summary>
        public void MarkTechSplit(int offset)
        {
            if (!HasVideo || Race == null)
            {
                Status("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής.", true);
                return;
            }

            AddTechSplit(player.CurrentTimestamp, offset);
            Changed(true);
        }

        /// <summary>Wall of the race already passed at a timestamp (0 for the start).</summary>
        private int LastWallBefore(long timestamp)
        {
            int wall = 0;
            foreach (int w in TurnWalls)
            {
                SwimMark split = Race.GetSplit(w);
                if (split != null && split.Timestamp <= timestamp)
                    wall = w;
            }

            return wall;
        }

        /// <summary>
        /// Delete the marks of a phase (all the events of a training attempt, or the technical marks of a race phase).
        /// </summary>
        public void DeletePhase(SwimTechPhase phase)
        {
            if (Race == null || phase == null)
                return;

            int removed;
            if (Race.IsTraining)
                removed = Race.Marks.RemoveAll(m => phase.Contains(m.Timestamp) || m.Id == phase.Id);
            else
                removed = Race.Marks.RemoveAll(m => phase.Contains(m.Timestamp) && (m.Type == SwimMarkType.TechSplit || m.Type == SwimMarkType.HandsOff ||
                    m.Type == SwimMarkType.BackFoot || m.Type == SwimMarkType.RotationStart));

            Status(string.Format(CultureInfo.InvariantCulture, "{0}: διαγράφηκαν {1} σημάδια.", phase.Title, removed), false);
            Changed(true);
        }

        public void SeekTo(long timestamp)
        {
            if (HasVideo)
                player.ForcePosition(timestamp, true);
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
                Status("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής.", true);
                return;
            }

            angleClicks.Clear();
            pendingMeasure = SwimPickTarget.None;
            SetPick(SwimPickTarget.Angle);
            Status(AnglePrompt(), false);
        }

        private string AnglePrompt()
        {
            string[] what = { "το ένα άκρο (π.χ. ο αστράγαλος)", "την κορυφή της γωνίας (π.χ. το γόνατο)", "το άλλο άκρο (π.χ. το ισχίο)" };
            int i = Math.Min(angleClicks.Count, 2);
            return string.Format(CultureInfo.InvariantCulture, "📐 Γωνία «{0}» – κλικ {1}/3 στο {2}. Esc: ακύρωση.", AngleName, i + 1, what[i]);
        }

        private void AngleClick(PointF point, long timestamp)
        {
            if (angleClicks.Count > 0 && !SameFrame(angleFrame, timestamp))
            {
                angleClicks.Clear();
                Status("Το καρέ άλλαξε: η γωνία ξεκινά ξανά σε αυτό το καρέ.", true);
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
                Status("Τα σημεία είναι πολύ κοντά: η γωνία δεν μετρήθηκε.", true);
                InvalidatePlayer();
                return;
            }

            Race.Angles.Add(angle);
            SwimTechPhase phase = SwimTechCalculator.PhaseAt(TechPhases, angle.Timestamp);
            Status(string.Format(CultureInfo.InvariantCulture, "📐 {0}: {1:0.0}°{2}", angle.Name, deg.Value, phase != null ? " (" + phase.Title + ")" : ""), false);
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
                Status("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής.", true);
                return;
            }

            Bitmap image = CaptureFrame(timestamp);
            if (image == null)
                return;

            SwimSnapshot snapshot = new SwimSnapshot { Timestamp = timestamp };
            SwimTechPhase phase = SwimTechCalculator.PhaseAt(TechPhases, timestamp);
            snapshot.Stage = SuggestStage(phase, timestamp);
            try
            {
                snapshot.File = SaveSnapshotImage(image, snapshot.Id);
            }
            catch (Exception e)
            {
                log.Error("Could not save the snapshot.", e);
                Status("Δεν αποθηκεύτηκε το στιγμιότυπο: " + e.Message, true);
                return;
            }
            finally
            {
                image.Dispose();
            }

            Race.Snapshots.Add(snapshot);
            Status(string.Format(CultureInfo.InvariantCulture, "📷 Στιγμιότυπο{0}{1}. Γράψε τα σχόλιά σου στην καρτέλα Στιγμιότυπα.",
                phase != null ? " · " + phase.Title : "", string.IsNullOrEmpty(snapshot.Stage) ? "" : " · " + snapshot.Stage), false);
            Changed(false);
            UploadSnapshot(snapshot);
            if (form != null)
                form.SnapshotAdded(snapshot);
        }

        private Bitmap CaptureFrame(long timestamp)
        {
            Size size = frameServer.VideoReader.Info.ReferenceSize;
            if (size.Width <= 0 || size.Height <= 0)
                return null;

            Bitmap full = null;
            try
            {
                player.BeforeExportVideo();
                full = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
                player.PaintFlushedImage(full);
            }
            catch (Exception e)
            {
                log.Error("Could not capture the frame.", e);
                Status("Δεν ήταν δυνατή η λήψη του καρέ: " + e.Message, true);
                if (full != null)
                    full.Dispose();
                return null;
            }
            finally
            {
                player.AfterExportVideo();
            }

            // The angles of this frame are part of the picture.
            using (Graphics g = Graphics.FromImage(full))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (SwimAngle a in Race.Angles.Where(x => SameFrame(x.Timestamp, timestamp)))
                    DrawAngle(g, a, p => p, Math.Max(1f, size.Width / 900f));
            }

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
                string fallback = Path.Combine(Software.SettingsDirectory, "SwimAnalysis", "Snapshots");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
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
        /// Upload the image of a snapshot to the Drive (in the background); the report shows it after the next update.
        /// </summary>
        private void UploadSnapshot(SwimSnapshot snapshot)
        {
            if (!Settings.IsConfigured || snapshot == null || !string.IsNullOrEmpty(snapshot.FileId) || uploading.Contains(snapshot.Id))
                return;

            byte[] bytes;
            try
            {
                if (string.IsNullOrEmpty(snapshot.File) || !File.Exists(snapshot.File))
                    return;
                bytes = File.ReadAllBytes(snapshot.File);
            }
            catch (Exception e)
            {
                log.Error("Could not read the snapshot.", e);
                return;
            }

            SwimRace race = Race;
            uploading.Add(snapshot.Id);
            client.UploadSnapshot(Settings.WebAppUrl, Settings.Token, race.Id, snapshot.Id, bytes, result =>
            {
                uploading.Remove(snapshot.Id);
                if (result.Ok && !string.IsNullOrEmpty(result.FileId))
                {
                    snapshot.FileId = result.FileId;
                    MarkDirty();
                    if (race == Race)
                        Changed(true);
                    else
                        SendRace(race, false);
                }
                else if (IsUnknownAction(result))
                {
                    Status("Για τα στιγμιότυπα στο report χρειάζεται η νέα έκδοση του Apps Script (2.0.0): δες τον οδηγό.", true);
                }
                else
                {
                    Status("Το στιγμιότυπο κρατήθηκε στον υπολογιστή και θα ανέβει αργότερα: " + result.Error, true);
                }

                if (form != null)
                    form.RefreshTech();
            });
        }

        /// <summary>Snapshots that did not reach the Drive yet (offline, no connection settings).</summary>
        private void UploadPendingSnapshots()
        {
            if (VideoFile == null || !Settings.IsConfigured)
                return;

            foreach (SwimRace race in VideoFile.Races)
            {
                if (race != Race)
                    continue;

                foreach (SwimSnapshot s in race.Snapshots.Where(x => string.IsNullOrEmpty(x.FileId)).ToList())
                    UploadSnapshot(s);
            }
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
                EliteMessage = "Ρύθμισε πρώτα τη σύνδεση για τη βιβλιοθήκη elite καρέ.";
                if (form != null)
                    form.RefreshTech();
                return;
            }

            if (EliteLoading)
                return;

            EliteLoading = true;
            EliteMessage = "Φόρτωση elite καρέ…";
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
                    EliteMessage = string.Format("Elite καρέ: {0} (ενημέρωση {1:dd/MM HH:mm}).", frames.Count, EliteLibrary.Updated);
                }
                else if (IsUnknownAction(result))
                {
                    EliteMessage = "Για τα elite καρέ χρειάζεται η νέα έκδοση του Apps Script (2.0.0).";
                }
                else
                {
                    EliteMessage = "Τα elite καρέ δεν ενημερώθηκαν: " + result.Error;
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
                Status("Ρύθμισε πρώτα τη σύνδεση με το Google Sheet.", true);
                return;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(snapshot.File);
            }
            catch (Exception e)
            {
                Status("Δεν βρέθηκε η εικόνα του στιγμιότυπου: " + e.Message, true);
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
            Status("Αποστολή του elite καρέ…", false);
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

                    Status("✔ Το καρέ μπήκε στη βιβλιοθήκη elite καρέ (φύλλο «Elite καρέ»).", false);
                }
                else
                {
                    Status(IsUnknownAction(result) ? "Για τα elite καρέ χρειάζεται η νέα έκδοση του Apps Script (2.0.0)." : "Το elite καρέ δεν αποθηκεύτηκε: " + result.Error, true);
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
                PbMessage = "Επίλεξε αθλητή για τη σύγκριση με προηγούμενο αγώνα.";
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
            PbMessage = "Φόρτωση προηγούμενων αναλύσεων…";
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

                    PbMessage = list.Count == 0 ? "Δεν υπάρχει άλλη ανάλυση του αθλητή σε αυτό το αγώνισμα." : string.Format("{0} προηγούμενες αναλύσεις.", list.Count);
                }
                else
                {
                    PbMessage = IsUnknownAction(result) ? "Για τη σύγκριση με PB χρειάζεται η νέα έκδοση του Apps Script (2.0.0)." : "Δεν φορτώθηκαν οι αναλύσεις: " + result.Error;
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
                    g.DrawString(a.Name, small, brush, box.X, box.Bottom + 1);
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
                add("ΑΝΤΙΔΡΑΣΗ", phase.Reaction);
                add("ΧΕΡΙΑ", phase.HandsOff);
                add("ΠΙΣΩ ΠΟΔΙ", phase.BackFoot);
                add("ΑΠΟΔΕΣΜΕΥΣΗ", phase.Block);
                add("ΕΙΣΟΔΟΣ", phase.Entry);
                foreach (KeyValuePair<int, double> s in phase.Splits)
                    add(s.Key.ToString(CultureInfo.InvariantCulture) + " " + unit, s.Value);
                add("ΕΞΟΔΟΣ", phase.Breakout);
            }
            else
            {
                double tIn;
                if (phase.Splits.TryGetValue(-5, out tIn) && tIn <= now)
                    items.Add(new KeyValuePair<string, string>("−5 " + unit, (-tIn).ToString("0.00", CultureInfo.InvariantCulture)));
                if (phase.LastStroke.HasValue && phase.LastStroke.Value <= now)
                    items.Add(new KeyValuePair<string, string>("ΧΕΡΙΑ→ΤΟΙΧΟΣ", (-phase.LastStroke.Value).ToString("0.00", CultureInfo.InvariantCulture)));
                add("ΑΠΟΚΟΛΛΗΣΗ", phase.Pushoff);
                foreach (KeyValuePair<int, double> s in phase.Splits.Where(x => x.Key > 0))
                    add("+" + s.Key.ToString(CultureInfo.InvariantCulture) + " " + unit, s.Value);
                add("ΕΞΟΔΟΣ", phase.Breakout);
            }

            int kicks = phase.Kicks.Count(k => k <= now + 1e-6);
            if (kicks > 0)
                items.Add(new KeyValuePair<string, string>("ΔΕΛΦΙΝΙΣΜΟΙ", kicks.ToString(CultureInfo.InvariantCulture)));

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
