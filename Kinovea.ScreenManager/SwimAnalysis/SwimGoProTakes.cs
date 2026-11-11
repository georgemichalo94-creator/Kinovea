#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Kinovea.Camera;
using Kinovea.Services;
using Newtonsoft.Json;

namespace Kinovea.ScreenManager
{
    /// <summary>(Swim 2.21.0) A swimmer of a pasted list of results or a start list: his lane, name and club.</summary>
    public class SwimTakeSwimmer
    {
        public int Lane;
        public string Name = "";
        public string Club = "";
    }

    /// <summary>(Swim 2.22.0) An athlete of the order of a continuous recording: his number of the registry, name and club.</summary>
    public class SwimTakeAthlete
    {
        public string AthleteId = "";
        public string Athlete = "";
        public string Club = "";

        public override string ToString()
        {
            string id = (AthleteId ?? "").Trim();
            string club = (Club ?? "").Trim();
            return (id.Length > 0 ? id + "  " : "") + (Athlete ?? "").Trim() + (club.Length > 0 ? "  · " + club : "");
        }
    }

    /// <summary>
    /// (Swim 2.21.0) What the next recordings of the GoPro cameras are: a race (competition, event, round, heat, lane or swimmer) or a
    /// start or turn of technique analysis (the athlete of the registry: his number, name and club), the place and the day. The
    /// videos get their folder and their name from it.
    /// </summary>
    public class SwimTakeDetails
    {
        public const string KindRace = "race";
        public const string KindStart = "start";
        public const string KindTurn = "turn";

        public string Kind = KindTurn;
        public string AthleteId = "";
        public string Athlete = "";
        public string Club = "";
        public string Competition = "";
        public string Location = "";
        public string Date = DateTime.Now.ToString("d.M.yyyy", CultureInfo.InvariantCulture);
        public string Style = "fs";
        public int Distance = 100;
        public bool Relay;
        public string Round = "";
        public int Heat;
        public int Lane;
        public List<SwimTakeSwimmer> Swimmers = new List<SwimTakeSwimmer>();

        /// <summary>
        /// (2.22.0) The athletes of a continuous recording in their order (start or turn): one after the other in the same video, the
        /// button «⏭ Επόμενος» at the end of the attempt of each; the video of each camera is cut for each of them. Empty: a recording
        /// of the athlete of the details.
        /// </summary>
        public List<SwimTakeAthlete> Queue = new List<SwimTakeAthlete>();

        /// <summary>(2.22.0) A continuous recording of the athletes of the order (never a race).</summary>
        public bool IsContinuous
        {
            get { return !IsRace && Queue != null && Queue.Count > 0; }
        }

        /// <summary>(2.22.0) The details of one athlete of the order (null: none, named afterwards), without an order of their own.</summary>
        public SwimTakeDetails ForAthlete(SwimTakeAthlete athlete)
        {
            SwimTakeDetails d = Clone();
            d.Queue = new List<SwimTakeAthlete>();
            d.AthleteId = athlete != null ? (athlete.AthleteId ?? "").Trim() : "";
            d.Athlete = athlete != null ? (athlete.Athlete ?? "").Trim() : "";
            d.Club = athlete != null ? (athlete.Club ?? "").Trim() : "";
            return d;
        }

        public SwimTakeDetails Clone()
        {
            return JsonConvert.DeserializeObject<SwimTakeDetails>(JsonConvert.SerializeObject(this));
        }

        public bool IsRace
        {
            get { return Kind == KindRace; }
        }

        /// <summary>The swimmer of the lane in the pasted list (null when none).</summary>
        public SwimTakeSwimmer SwimmerOfLane()
        {
            if (Lane <= 0 || Swimmers == null)
                return null;

            return Swimmers.FirstOrDefault(s => s.Lane == Lane);
        }

        /// <summary>The swimmer of the videos: the athlete of the registry, else the one of the lane in the pasted list ("" when none).</summary>
        public string SwimmerName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Athlete))
                    return Athlete.Trim();

                SwimTakeSwimmer s = SwimmerOfLane();
                return s != null ? s.Name : "";
            }
        }

        public string SwimmerClub
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Athlete))
                    return (Club ?? "").Trim();

                SwimTakeSwimmer s = SwimmerOfLane();
                return s != null ? s.Club : "";
            }
        }

        /// <summary>"Στροφή", "Εκκίνηση" or "Αγώνας".</summary>
        public string KindName
        {
            get
            {
                switch (Kind)
                {
                    case KindStart: return SwimLang.T("Εκκίνηση");
                    case KindRace: return SwimLang.T("Αγώνας");
                    default: return SwimLang.T("Στροφή");
                }
            }
        }

        /// <summary>The kind in the names of the files, always in Greek (the names stay the same in an English interface).</summary>
        public string KindFileName
        {
            get
            {
                switch (Kind)
                {
                    case KindStart: return "Εκκίνηση";
                    case KindRace: return "Αγώνας";
                    default: return "Στροφή";
                }
            }
        }

        /// <summary>The event in the names of the files, always in Greek: "100Ελεύθερο", "4x100Ελεύθερο".</summary>
        public string EventName
        {
            get
            {
                string distance = Relay && Distance % 4 == 0 ? "4x" + (Distance / 4).ToString(CultureInfo.InvariantCulture) : Distance.ToString(CultureInfo.InvariantCulture);
                return distance + StyleFileName(Style);
            }
        }

        private static string StyleFileName(string style)
        {
            switch (style ?? "")
            {
                case "fs": return "Ελεύθερο";
                case "bk": return "Ύπτιο";
                case "br": return "Πρόσθιο";
                case "fly": return "Πεταλούδα";
                case "im": return "Μικτή";
                default: return style ?? "";
            }
        }

        /// <summary>What the button of the screens says: the athlete and the kind, or the event of the race.</summary>
        public string Summary
        {
            get
            {
                if (IsContinuous)
                    return string.Format(CultureInfo.InvariantCulture, SwimLang.T("👥 {0} αθλητές · {1}"), Queue.Count, KindName);

                string who = SwimmerName;
                if (IsRace)
                {
                    List<string> parts = new List<string> { Distance.ToString(CultureInfo.InvariantCulture) + " " + SwimPresets.StyleName(Style) };
                    if (!string.IsNullOrWhiteSpace(Round))
                        parts.Add(Round.Trim());

                    if (who.Length > 0)
                        parts.Add(who);
                    else if (Lane > 0)
                        parts.Add(SwimLang.T("Δ") + Lane.ToString(CultureInfo.InvariantCulture));

                    return string.Join(" · ", parts);
                }

                if (who.Length == 0)
                    return KindName + " · " + SwimLang.T("χωρίς αθλητή");

                return (string.IsNullOrWhiteSpace(AthleteId) ? "" : AthleteId.Trim() + " ") + who + " · " + KindName;
            }
        }
    }

    /// <summary>(Swim 2.21.0) A camera of a recording, and its file once found (copied or downloaded).</summary>
    public class SwimTakeCamera
    {
        public string Alias = "";
        public string Serial = "";
        public string Model = "";

        /// <summary>
        /// Seconds from the start command to the camera recording, as measured by the camera's link (null: not measured): lines up the
        /// videos of the recording when their sounds do not match (a camera under the water). (2.22.0) For a part of a continuous
        /// recording: from the command of that recording to the first image of the part's video.
        /// </summary>
        public double? StartOffset;

        /// <summary>The name of the file on the card (GX010123.MP4), once found.</summary>
        public string Source = "";
        public List<string> Targets = new List<string>();
        public bool Done;
    }

    /// <summary>(Swim 2.21.0) A recording started from Kinovea: when, with which cameras, of whom.</summary>
    public class SwimTake
    {
        public string Id = Guid.NewGuid().ToString("N");
        public DateTime Start;
        public DateTime Stop = DateTime.MinValue;
        public SwimTakeDetails Details = new SwimTakeDetails();
        public List<SwimTakeCamera> Cameras = new List<SwimTakeCamera>();

        /// <summary>(2.21.0) The video of all its cameras, once made ("" before).</summary>
        public string Composite = "";

        /// <summary>
        /// (2.22.0) A part of a continuous recording (one athlete): the recording it is cut from and its place in it (1, 2…); "" and 0
        /// for a recording of its own. Its Start and Stop are the moments of the button «⏭ Επόμενος» (the start of the recording for the
        /// first part, its end for the last one).
        /// </summary>
        public string ParentId = "";
        public int Index;

        /// <summary>(2.22.0) A part ended by the button (else by the end of the recording: it is cut to the end of the files).</summary>
        public bool EndedByButton;

        /// <summary>(2.22.0) A continuous recording: the moments of the button «⏭ Επόμενος» (the ends of the attempts).</summary>
        public List<DateTime> Marks = new List<DateTime>();

        /// <summary>(2.22.0) The horns of the start signal of the computer during the recording (its clock).</summary>
        public List<DateTime> Signals = new List<DateTime>();

        [JsonIgnore]
        public bool IsPart
        {
            get { return !string.IsNullOrEmpty(ParentId); }
        }

        /// <summary>(2.22.0) A continuous recording, cut in parts (it is not shown: its parts are).</summary>
        [JsonIgnore]
        public bool IsContinuous
        {
            get { return !IsPart && Details != null && Details.IsContinuous; }
        }

        public bool AllDone
        {
            get { return Cameras.Count > 0 && Cameras.All(c => c.Done); }
        }
    }

    /// <summary>(Swim 2.21.0) What is kept between the sessions: the details of the next recordings, the folder, the recordings.</summary>
    public class SwimTakeLog
    {
        public SwimTakeDetails Next = new SwimTakeDetails();
        public string Root = "";
        public List<SwimTake> Takes = new List<SwimTake>();

        /// <summary>The serial number a camera writes in its files when it is not the one it gives by Bluetooth (learned by Wi-Fi).</summary>
        public Dictionary<string, string> FileSerials = new Dictionary<string, string>();
    }

    /// <summary>
    /// (Swim 2.21.0) The recordings of the GoPro cameras started from Kinovea and the names of their videos. Each start (from the common
    /// button: one recording for all the cameras) keeps the details of that moment and the camera (its name, which says where it is,
    /// and its serial number, which is also in its files). The videos are found later on the card (copied from a card reader or a
    /// folder) or downloaded by the Wi-Fi of the camera, and get their folder and name:
    ///   technique: Root\d.M.yyyyΤόποςvideo\ΣΩΜΑΤΕΙΟ\ΑΜ_ΕΠΩΝΥΜΟ_ΟΝΟΜΑ\ΑΜ_ΕΠΩΝΥΜΟ_ΟΝΟΜΑ_Στροφή_yyyyMMdd_Κάμερα.MP4
    ///   race: Root\d.M.yyyyΤόποςvideo\100Ελεύθερο\yyyyMMdd_Διοργάνωση_100Ελεύθερο_Φάση_ΣειράN_ΑΜ_ΕΠΩΝΥΜΟ_ΟΝΟΜΑ_Κάμερα.MP4
    /// Kept in the settings folder (the names of the athletes stay on this computer).
    /// </summary>
    public static class SwimGoProTakes
    {
        private const int MaxTakes = 3000;
        private static readonly object locker = new object();
        private static SwimTakeLog data;
        private static readonly List<CaptureScreen> screens = new List<CaptureScreen>();
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>The details of the next recordings or the list of the recordings changed. Raised on the thread of the change.</summary>
        public static event EventHandler Changed;

        private static string FilePath
        {
            get { return Path.Combine(Software.SettingsDirectory, "SwimGoProTakes.json"); }
        }

        private static SwimTakeLog Data
        {
            get
            {
                lock (locker)
                {
                    if (data == null)
                        data = Load();

                    return data;
                }
            }
        }

        private static SwimTakeLog Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    SwimTakeLog loaded = JsonConvert.DeserializeObject<SwimTakeLog>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (loaded != null)
                    {
                        if (loaded.Next == null)
                            loaded.Next = new SwimTakeDetails();

                        if (loaded.Takes == null)
                            loaded.Takes = new List<SwimTake>();

                        if (loaded.FileSerials == null)
                            loaded.FileSerials = new Dictionary<string, string>();

                        // A new day: the date of the details is today's.
                        loaded.Next.Date = DateTime.Now.ToString("d.M.yyyy", CultureInfo.InvariantCulture);
                        return loaded;
                    }
                }
            }
            catch (Exception e)
            {
                log.Error("Could not read the recordings of the GoPro cameras.", e);
            }

            return new SwimTakeLog();
        }

        public static void Save()
        {
            string json;
            lock (locker)
            {
                if (data == null)
                    return;

                if (data.Takes.Count > MaxTakes)
                    data.Takes.RemoveRange(0, data.Takes.Count - MaxTakes);

                json = JsonConvert.SerializeObject(data, Formatting.Indented);
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, json, Encoding.UTF8);
                if (File.Exists(FilePath))
                    File.Replace(temp, FilePath, null);
                else
                    File.Move(temp, FilePath);
            }
            catch (Exception e)
            {
                log.Error("Could not save the recordings of the GoPro cameras.", e);
            }
        }

        private static void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler != null)
                handler(null, EventArgs.Empty);
        }

        #region Details and folder
        /// <summary>A copy of the details of the next recordings.</summary>
        public static SwimTakeDetails Next
        {
            get { lock (locker) { return Data.Next.Clone(); } }
        }

        public static void SetNext(SwimTakeDetails details)
        {
            if (details == null)
                return;

            lock (locker)
                Data.Next = details.Clone();

            Save();
            RaiseChanged();
        }

        /// <summary>The folder where the videos go (their session folders inside it). "" until chosen.</summary>
        public static string Root
        {
            get { lock (locker) { return Data.Root ?? ""; } }
            set
            {
                lock (locker)
                    Data.Root = value ?? "";

                Save();
                RaiseChanged();
            }
        }
        #endregion

        #region Recordings
        /// <summary>Copies of the recordings, the latest last.</summary>
        public static List<SwimTake> Takes
        {
            get
            {
                lock (locker)
                    return JsonConvert.DeserializeObject<List<SwimTake>>(JsonConvert.SerializeObject(Data.Takes));
            }
        }

        /// <summary>
        /// A camera started recording from Kinovea: the cameras started by the same command (the common button) are one recording, with
        /// the details of now.
        /// </summary>
        public static void Started(string alias, string serial, string model, DateTime commandTime)
        {
            lock (locker)
            {
                SwimTakeLog d = Data;
                SwimTake take = d.Takes.LastOrDefault(t => !t.IsPart && t.Stop == DateTime.MinValue && Math.Abs((t.Start - commandTime).TotalSeconds) < 2 && t.Cameras.All(c => c.Alias != alias));
                bool created = take == null;
                if (created)
                {
                    take = new SwimTake { Start = commandTime, Details = d.Next.Clone() };
                    d.Takes.Add(take);
                }

                take.Cameras.Add(new SwimTakeCamera { Alias = alias ?? "", Serial = serial ?? "", Model = model ?? "" });

                // (2.22.0) A continuous recording: its parts (the first athlete of the order from now), with all its cameras.
                if (take.IsContinuous)
                {
                    if (created)
                        RebuildParts(take);

                    foreach (SwimTake part in d.Takes.Where(t => t.ParentId == take.Id))
                    {
                        if (part.Cameras.All(c => c.Alias != (alias ?? "")))
                            part.Cameras.Add(new SwimTakeCamera { Alias = alias ?? "", Serial = serial ?? "", Model = model ?? "" });
                    }
                }
            }

            log.DebugFormat("Recording of the camera \"{0}\" kept.", alias);
            Save();
            RaiseChanged();
        }

        /// <summary>
        /// A camera did not start at the command (not connected, refused, or taken but not started): it leaves that recording, which goes
        /// when no camera is left.
        /// </summary>
        public static void Failed(string alias, DateTime commandTime)
        {
            bool changed = false;
            lock (locker)
            {
                SwimTake take = Data.Takes.LastOrDefault(t => !t.IsPart && Math.Abs((t.Start - commandTime).TotalSeconds) < 2 && t.Cameras.Any(c => c.Alias == alias && !c.Done));
                if (take != null)
                {
                    take.Cameras.RemoveAll(c => c.Alias == alias && !c.Done);
                    foreach (SwimTake part in Data.Takes.Where(t => t.ParentId == take.Id))
                        part.Cameras.RemoveAll(c => c.Alias == alias && !c.Done);

                    if (take.Cameras.Count == 0)
                    {
                        string id = take.Id;
                        Data.Takes.Remove(take);
                        Data.Takes.RemoveAll(t => t.ParentId == id);
                    }

                    changed = true;
                }
            }

            if (!changed)
                return;

            log.DebugFormat("The camera \"{0}\" did not start: out of the recording.", alias);
            Save();
            RaiseChanged();
        }

        /// <summary>The video of all the cameras of a recording, made.</summary>
        public static void SetComposite(string takeId, string path)
        {
            lock (locker)
            {
                SwimTake take = Data.Takes.FirstOrDefault(t => t.Id == takeId);
                if (take == null)
                    return;

                take.Composite = path ?? "";
            }

            Save();
            RaiseChanged();
        }

        /// <summary>The measured start of a camera after the command of its recording (seconds).</summary>
        public static void SetStartOffset(string alias, DateTime commandTime, double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > 15)
                return;

            bool changed = false;
            lock (locker)
            {
                SwimTake take = Data.Takes.LastOrDefault(t => !t.IsPart && Math.Abs((t.Start - commandTime).TotalSeconds) < 2 && t.Cameras.Any(c => c.Alias == alias));
                SwimTakeCamera camera = take != null ? take.Cameras.First(c => c.Alias == alias) : null;
                if (camera != null && (!camera.StartOffset.HasValue || Math.Abs(camera.StartOffset.Value - seconds) > 1e-6))
                {
                    camera.StartOffset = seconds;
                    changed = true;
                }
            }

            if (changed)
                Save();
        }

        /// <summary>A camera stopped: the end of its recording.</summary>
        public static void Stopped(string alias, DateTime time)
        {
            lock (locker)
            {
                SwimTake take = Data.Takes.LastOrDefault(t => !t.IsPart && t.Cameras.Any(c => c.Alias == alias));
                if (take == null || (take.Stop != DateTime.MinValue && take.Stop > time))
                    return;

                bool first = take.Stop == DateTime.MinValue;
                take.Stop = time;

                // (2.22.0) The part of the athlete of the end: it ends with the first camera that stops (it is cut to the end of the files).
                if (first && take.IsContinuous)
                    RebuildParts(take);
            }

            Save();
            RaiseChanged();
        }

        /// <summary>The details of a recording changed afterwards (a wrong athlete): the names of the videos not found yet follow.</summary>
        public static void SetDetails(string takeId, SwimTakeDetails details)
        {
            lock (locker)
            {
                SwimTake take = Data.Takes.FirstOrDefault(t => t.Id == takeId);
                if (take == null || details == null)
                    return;

                take.Details = details.Clone();
            }

            Save();
            RaiseChanged();
        }

        public static void Remove(IEnumerable<string> takeIds)
        {
            HashSet<string> ids = new HashSet<string>(takeIds);
            lock (locker)
            {
                // (2.22.0) A continuous recording out: its parts too; its last part out: the recording too (once ended).
                HashSet<string> parents = new HashSet<string>(Data.Takes.Where(t => ids.Contains(t.Id) && !t.IsPart).Select(t => t.Id));
                Data.Takes.RemoveAll(t => ids.Contains(t.Id) || parents.Contains(t.ParentId ?? ""));
                HashSet<string> withParts = new HashSet<string>(Data.Takes.Where(t => t.IsPart).Select(t => t.ParentId));
                Data.Takes.RemoveAll(t => t.IsContinuous && t.Stop != DateTime.MinValue && !withParts.Contains(t.Id));
            }

            Save();
            RaiseChanged();
        }

        /// <summary>The file of a camera of a recording is on the computer.</summary>
        public static void MarkDone(string takeId, string alias, string source, List<string> targets)
        {
            lock (locker)
            {
                SwimTake take = Data.Takes.FirstOrDefault(t => t.Id == takeId);
                SwimTakeCamera camera = take != null ? take.Cameras.FirstOrDefault(c => c.Alias == alias) : null;
                if (camera == null)
                    return;

                camera.Source = source ?? "";
                camera.Targets = targets ?? new List<string>();
                camera.Done = true;
            }

            Save();
            RaiseChanged();
        }
        /// <summary>
        /// A file downloaded from a camera says another serial number than the camera by Bluetooth: kept, so that its card is found by
        /// it too.
        /// </summary>
        public static void LearnFileSerial(string serial, string fileSerial)
        {
            if (string.IsNullOrWhiteSpace(serial) || string.IsNullOrWhiteSpace(fileSerial) ||
                string.Equals(serial.Trim(), fileSerial.Trim(), StringComparison.OrdinalIgnoreCase))
                return;

            lock (locker)
                Data.FileSerials[serial.Trim()] = fileSerial.Trim();

            log.WarnFormat("A GoPro writes the serial number {0} in its files and gives {1} by Bluetooth.", fileSerial.Trim(), serial.Trim());
            Save();
        }

        /// <summary>The serial number a camera writes in its files (the one by Bluetooth unless learned otherwise).</summary>
        public static string FileSerialOf(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
                return "";

            lock (locker)
            {
                string learned;
                return Data.FileSerials.TryGetValue(serial.Trim(), out learned) ? learned : serial.Trim();
            }
        }
        #endregion

        #region Continuous recordings (2.22.0)
        /// <summary>A press of «⏭ Επόμενος» closer than this to the one before (or to the start) is the same press (a double click).</summary>
        public const double MinPartSeconds = 2;

        /// <summary>The continuous recording going on: the last recording started, of an order of athletes, not ended. Under the lock.</summary>
        private static SwimTake CurrentUnlocked()
        {
            SwimTake parent = Data.Takes.LastOrDefault(t => !t.IsPart);
            return parent != null && parent.IsContinuous && parent.Stop == DateTime.MinValue ? parent : null;
        }

        /// <summary>
        /// The parts of a continuous recording from its presses (under the lock): part i from the press i - 1 (the start for the first) to
        /// the press i, the athlete i of the order; after the last press the part of the next athlete, open until the end of the
        /// recording, when the order has one more (a press after the last athlete ends the order; one more press is a part of a swimmer
        /// the order does not have, named afterwards). A part already there keeps its details (corrected or not) and its videos; a part of
        /// an undone press goes, unless its videos came.
        /// </summary>
        private static void RebuildParts(SwimTake parent)
        {
            List<SwimTakeAthlete> queue = parent.Details.Queue ?? new List<SwimTakeAthlete>();
            List<DateTime> marks = parent.Marks ?? (parent.Marks = new List<DateTime>());
            int presses = marks.Count;
            int count = presses + (presses < queue.Count ? 1 : 0);
            List<SwimTake> parts = Data.Takes.Where(t => t.ParentId == parent.Id).ToList();
            for (int i = 1; i <= count; i++)
            {
                SwimTake part = parts.FirstOrDefault(p => p.Index == i);
                if (part == null)
                {
                    part = new SwimTake { ParentId = parent.Id, Index = i, Details = parent.Details.ForAthlete(i <= queue.Count ? queue[i - 1] : null) };
                    foreach (SwimTakeCamera c in parent.Cameras)
                        part.Cameras.Add(new SwimTakeCamera { Alias = c.Alias, Serial = c.Serial, Model = c.Model });

                    Data.Takes.Add(part);
                }

                part.Start = i == 1 ? parent.Start : marks[i - 2];
                part.EndedByButton = i <= presses;
                part.Stop = i <= presses ? marks[i - 1] : parent.Stop;
            }

            Data.Takes.RemoveAll(t => t.ParentId == parent.Id && t.Index > count && t.Cameras.All(c => !c.Done));
        }

        /// <summary>
        /// The continuous recording going on (a copy, null when none), its part open now (the athlete in the water: null after the last one)
        /// and the number of its parts so far.
        /// </summary>
        public static SwimTake Continuous(out SwimTake open, out int parts)
        {
            lock (locker)
            {
                open = null;
                parts = 0;
                SwimTake parent = CurrentUnlocked();
                if (parent == null)
                    return null;

                List<SwimTake> mine = Data.Takes.Where(t => t.ParentId == parent.Id).OrderBy(t => t.Index).ToList();
                parts = mine.Count;
                SwimTake current = mine.LastOrDefault(t => t.Stop == DateTime.MinValue);
                if (current != null)
                    open = JsonConvert.DeserializeObject<SwimTake>(JsonConvert.SerializeObject(current));

                return JsonConvert.DeserializeObject<SwimTake>(JsonConvert.SerializeObject(parent));
            }
        }

        /// <summary>
        /// The button «⏭ Επόμενος» (or the key of a presenter): the attempt of the athlete in the water ends now, the next one of the order
        /// starts. False (and why in `message`) when no continuous recording goes on or the press is the same as the one before.
        /// </summary>
        public static bool NextAthlete(DateTime now, out string message)
        {
            lock (locker)
            {
                SwimTake parent = CurrentUnlocked();
                if (parent == null)
                {
                    message = SwimLang.T("Δεν γίνεται συνεχόμενη εγγραφή.");
                    return false;
                }

                List<DateTime> marks = parent.Marks ?? (parent.Marks = new List<DateTime>());
                DateTime last = marks.Count > 0 ? marks[marks.Count - 1] : parent.Start;
                if ((now - last).TotalSeconds < MinPartSeconds)
                {
                    message = SwimLang.T("Το ίδιο πάτημα (λιγότερο από 2 s μετά το προηγούμενο).");
                    return false;
                }

                marks.Add(now);
                RebuildParts(parent);
                int queue = parent.Details.Queue.Count;
                int done = marks.Count;
                SwimTake next = Data.Takes.FirstOrDefault(t => t.ParentId == parent.Id && t.Stop == DateTime.MinValue);
                if (next != null)
                    message = string.Format(CultureInfo.InvariantCulture, SwimLang.T("✓ {0}/{1} · τώρα: {2}"), done, queue, next.Details.Summary);
                else if (done <= queue)
                    message = string.Format(CultureInfo.InvariantCulture, SwimLang.T("✓ {0}/{1} · τέλος της σειράς"), done, queue);
                else
                    message = string.Format(CultureInfo.InvariantCulture, SwimLang.T("✓ +{0} χωρίς όνομα (μετά τη σειρά)"), done - queue);
            }

            log.Debug("The next athlete of the continuous recording.");
            Save();
            RaiseChanged();
            return true;
        }

        /// <summary>The button «↶»: the last press is undone (its part goes, unless its videos came).</summary>
        public static bool UndoNextAthlete(out string message)
        {
            lock (locker)
            {
                SwimTake parent = CurrentUnlocked();
                if (parent == null || parent.Marks == null || parent.Marks.Count == 0)
                {
                    message = SwimLang.T("Δεν υπάρχει πάτημα για αναίρεση.");
                    return false;
                }

                parent.Marks.RemoveAt(parent.Marks.Count - 1);
                RebuildParts(parent);
                message = string.Format(CultureInfo.InvariantCulture, SwimLang.T("↶ Αναιρέθηκε· μέσα στο νερό: {0}/{1}"), parent.Marks.Count + 1, parent.Details.Queue.Count);
            }

            Save();
            RaiseChanged();
            return true;
        }

        /// <summary>The parts of a continuous recording (copies), in their order.</summary>
        public static List<SwimTake> Parts(string parentId)
        {
            lock (locker)
                return JsonConvert.DeserializeObject<List<SwimTake>>(JsonConvert.SerializeObject(Data.Takes.Where(t => t.ParentId == parentId).OrderBy(t => t.Index).ToList()));
        }

        /// <summary>
        /// The start of the video of a camera of a part: seconds from the command of its recording to the first image of the part's video
        /// (lines up the videos of the part when their sounds do not match).
        /// </summary>
        public static void SetPartStart(string partId, string alias, double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
                return;

            lock (locker)
            {
                SwimTake part = Data.Takes.FirstOrDefault(t => t.Id == partId);
                SwimTakeCamera camera = part != null ? part.Cameras.FirstOrDefault(c => c.Alias == alias) : null;
                if (camera == null)
                    return;

                camera.StartOffset = seconds;
            }

            Save();
        }

        /// <summary>The horn of the start signal: kept with the recording going on (none: nothing).</summary>
        public static void AddSignal(DateTime horn)
        {
            lock (locker)
            {
                SwimTake take = Data.Takes.LastOrDefault(t => !t.IsPart);
                if (take == null || take.Stop != DateTime.MinValue)
                    return;

                if (take.Signals == null)
                    take.Signals = new List<DateTime>();

                take.Signals.Add(horn);
            }

            Save();
        }

        /// <summary>«🏁 Ολοκλήρωση συνεδρίας»: the order of the athletes is emptied (the next recordings are of one athlete each).</summary>
        public static void EndSession()
        {
            lock (locker)
                Data.Next.Queue = new List<SwimTakeAthlete>();

            Save();
            RaiseChanged();
        }
        #endregion

        #region Screens
        /// <summary>A capture screen with a camera that gives its files (a GoPro): the Wi-Fi downloads go through it.</summary>
        public static void Register(CaptureScreen screen)
        {
            lock (locker)
            {
                if (!screens.Contains(screen))
                    screens.Add(screen);
            }
        }

        public static void Unregister(CaptureScreen screen)
        {
            lock (locker)
                screens.Remove(screen);
        }

        /// <summary>(2.22.0) A camera of the screens records now.</summary>
        public static bool AnyRecording()
        {
            List<CaptureScreen> copy;
            lock (locker)
                copy = screens.ToList();

            return copy.Any(s => s.Recording);
        }

        /// <summary>The cameras of the screens now: their name and their source.</summary>
        public static List<KeyValuePair<string, IRemoteMediaSource>> MediaSources()
        {
            List<CaptureScreen> copy;
            lock (locker)
                copy = screens.ToList();

            List<KeyValuePair<string, IRemoteMediaSource>> result = new List<KeyValuePair<string, IRemoteMediaSource>>();
            foreach (CaptureScreen s in copy)
            {
                IRemoteMediaSource source = s.RemoteMedia;
                string alias = s.CameraAlias;
                if (source != null && !string.IsNullOrEmpty(alias))
                    result.Add(new KeyValuePair<string, IRemoteMediaSource>(alias, source));
            }

            return result;
        }
        #endregion

        #region Names
        /// <summary>The folder of the day and place: Root\8.11.2026Thessalonikivideo.</summary>
        public static string SessionFolder(string root, SwimTakeDetails d)
        {
            string date = string.IsNullOrWhiteSpace(d.Date) ? DateTime.Now.ToString("d.M.yyyy", CultureInfo.InvariantCulture) : d.Date.Trim();
            string place = Clean(d.Location ?? "", false).Replace(" ", "");
            return Path.Combine(root ?? "", Clean(date + place + "video", false));
        }

        /// <summary>
        /// The path of the video of a camera of a recording (part 1, 2… of a recording the camera cut in parts); the folder of the day and
        /// place, then for technique the club and the athlete, for a race the event.
        /// </summary>
        public static string TargetPath(string root, SwimTake take, string cameraAlias, int part)
        {
            SwimTakeDetails d = take.Details ?? new SwimTakeDetails();
            string date = DayOf(take, d).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string camera = Clean(cameraAlias ?? "", true);
            string folder = SessionFolder(root, d);
            string name;

            string who = Upper(Clean(d.SwimmerName, true));
            string id = Clean(d.AthleteId ?? "", true);
            string person = who.Length == 0 ? "" : (id.Length > 0 ? id + "_" + who : who);

            if (d.IsRace)
            {
                folder = Path.Combine(folder, Clean(d.EventName, true));
                List<string> parts = new List<string> { date };
                if (!string.IsNullOrWhiteSpace(d.Competition))
                    parts.Add(Clean(d.Competition, true));

                parts.Add(Clean(d.EventName, true));
                if (!string.IsNullOrWhiteSpace(d.Round))
                    parts.Add(Clean(d.Round, true));

                if (d.Heat > 0)
                    parts.Add("Σειρά" + d.Heat.ToString(CultureInfo.InvariantCulture));

                if (person.Length > 0)
                    parts.Add(person);
                else if (d.Lane > 0)
                    parts.Add("Διάδρομος" + d.Lane.ToString(CultureInfo.InvariantCulture));
                else
                    parts.Add(take.Start.ToString("HHmmss", CultureInfo.InvariantCulture));

                parts.Add(camera);
                name = string.Join("_", parts.Where(p => p.Length > 0));
            }
            else
            {
                string kind = d.KindFileName;
                if (person.Length > 0)
                {
                    string club = Upper(Clean(d.SwimmerClub, false));
                    folder = Path.Combine(folder, club.Length > 0 ? club : "ΧΩΡΙΣ ΣΩΜΑΤΕΙΟ", person);
                    name = person + "_" + kind + "_" + date + "_" + camera;
                }
                else
                {
                    folder = Path.Combine(folder, "ΧΩΡΙΣ ΑΘΛΗΤΗ");
                    name = kind + "_" + date + "_" + take.Start.ToString("HHmmss", CultureInfo.InvariantCulture) + "_" + camera;
                }
            }

            if (part > 1)
                name += "_μέρος" + part.ToString(CultureInfo.InvariantCulture);

            return Path.Combine(folder, name.Trim('_') + ".MP4");
        }

        /// <summary>A free path next to the wanted one ("…_2.MP4") when a file is there already (a second attempt of the same day).</summary>
        public static string FreePath(string path, HashSet<string> taken)
        {
            string candidate = path;
            int n = 2;
            while (File.Exists(candidate) || File.Exists(candidate + ".part") || (taken != null && taken.Contains(candidate.ToLowerInvariant())))
            {
                candidate = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_" + n.ToString(CultureInfo.InvariantCulture) + Path.GetExtension(path));
                n++;
            }

            if (taken != null)
                taken.Add(candidate.ToLowerInvariant());

            return candidate;
        }

        private static DateTime DayOf(SwimTake take, SwimTakeDetails d)
        {
            DateTime day;
            if (!string.IsNullOrWhiteSpace(d.Date) &&
                DateTime.TryParseExact(d.Date.Trim(), new[] { "d.M.yyyy", "d/M/yyyy", "d-M-yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
                return day;

            return take.Start;
        }

        /// <summary>Capitals without accents, as in the lists of the federation ("ΑΝΤΩΝΙΑΔΟΥ").</summary>
        public static string Upper(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            string decomposed = text.Normalize(NormalizationForm.FormD);
            StringBuilder b = new StringBuilder(decomposed.Length);
            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    b.Append(c);
            }

            return b.ToString().Normalize(NormalizationForm.FormC).ToUpper(CultureInfo.GetCultureInfo("el-GR"));
        }

        /// <summary>A part of a file name: no character Windows refuses; with `underscores`, the spaces become "_".</summary>
        public static string Clean(string text, bool underscores)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            StringBuilder b = new StringBuilder(text.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in text.Trim())
            {
                if (Array.IndexOf(invalid, c) >= 0)
                    b.Append(' ');
                else
                    b.Append(c);
            }

            string s = b.ToString();
            while (s.Contains("  "))
                s = s.Replace("  ", " ");

            s = s.Trim().TrimEnd('.');
            return underscores ? s.Replace(' ', '_') : s;
        }
        #endregion
    }
}
