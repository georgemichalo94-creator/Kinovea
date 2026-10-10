#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// Coordinates the swim race analysis: receives the hotkey commands from the player screen,
    /// keeps the races of the current video, saves them next to the video, computes the analysis
    /// and pushes it live to the Google Sheet.
    ///
    /// Integration points with the rest of Kinovea are intentionally few:
    /// - PlayerScreenCommands.Swim* (Commands.cs) and their default keys (HotkeySettingsManager.cs),
    /// - PlayerScreenUserInterface.ExecuteScreenCommand forwards those commands here,
    /// - Tools menu entry in ScreenManager.cs opens the panel.
    /// </summary>
    public class SwimAnalysisManager
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
        private List<string> undoStack = new List<string>();
        private Timer saveTimer;
        private Timer sendTimer;
        private bool dirty;
        #endregion

        private SwimAnalysisManager()
        {
            Settings = SwimSettings.Load();
            Athletes = SwimAthleteList.Load();
            AthletesMessage = AthletesSummary();
            Timing = new SwimTiming();
            Computed = new SwimComputed();
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
                    SetStart(timestamp);
                    break;
                case PlayerScreenCommands.SwimSplit:
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
                    AddMark(SwimMarkType.Entry, timestamp, "Είσοδος στο νερό");
                    break;
                case PlayerScreenCommands.SwimUndo:
                    Undo();
                    break;
                default:
                    return false;
            }

            EnsureForm(false);
            Changed(true);
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
            string message = string.Format(CultureInfo.InvariantCulture, "Πέρασμα {0}μ.: {1:0.00} s", distance, t);

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
            bool hadStart = EnsureStart();
            SwimMark mark = new SwimMark { Type = type, Timestamp = timestamp };
            Race.Marks.Add(mark);
            undoStack.Add(mark.Id);

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
                case SwimMarkType.Split: return "πέρασμα " + mark.Distance + "μ.";
                case SwimMarkType.Stroke: return "χεριά";
                case SwimMarkType.Kick: return "δελφινισμός";
                case SwimMarkType.Breakout: return "έξοδος από το νερό";
                case SwimMarkType.FeetOff: return "αποκόλληση ποδιών";
                case SwimMarkType.Entry: return "είσοδος στο νερό";
                default: return mark.Type.ToString();
            }
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

            Race = race;
            VideoFile.ActiveRaceId = race.Id;
            undoStack.Clear();
            Changed(false);
            if (form != null)
                form.RefreshAll();
        }

        public void NewRace()
        {
            if (VideoFile == null)
                return;

            SwimRace race = Race != null ? Race.CloneForNewSwimmer() : CreateDefaultRace();
            VideoFile.Races.Add(race);
            Race = race;
            VideoFile.ActiveRaceId = race.Id;
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

            Changed(false);
            ScheduleSend();
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
            Status(string.Format("Αθλητής: {0} (μητρώο {1}).", athlete.Name, athlete.Id), false);
            Changed(false);
            ScheduleSend();
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

            string body = SwimPayload.Build(Race, Computed, Settings, VideoName, pdf ? "pdf" : "update");
            client.Enqueue(Settings.WebAppUrl, body, Race.Id, pdf);
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

            this.player = player;
            this.frameServer = frameServer;
            RefreshTiming();

            if (sameVideo)
                return;

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
                        file.VideoPath = path;
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
        }
        #endregion
    }
}
