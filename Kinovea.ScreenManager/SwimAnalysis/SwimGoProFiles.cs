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
using System.Text.RegularExpressions;
using System.Threading;
using Kinovea.Camera;

namespace Kinovea.ScreenManager
{
    /// <summary>(Swim 2.21.0) A video of a GoPro card: its number and part, when it started, and the camera that made it.</summary>
    public class SwimGoProFile
    {
        public string Path = "";
        public string Name = "";
        public string Folder = "";
        public int Number;
        public int Part = 1;
        public long Size;

        /// <summary>When the recording started (the clock of the camera: the one of the computer since it took it).</summary>
        public DateTime Created = DateTime.MinValue;

        /// <summary>The serial number and model written by the camera in the file ("" when not found).</summary>
        public string Serial = "";
        public string Model = "";

        /// <summary>The time zone of the camera written in the file, in minutes (int.MinValue when not written).</summary>
        public int ZoneMinutes = int.MinValue;

        /// <summary>(2.21.0) Length of the video in seconds (0 when not read).</summary>
        public double Duration;
    }

    /// <summary>(Swim 2.21.0) A recording of a camera and the files found for it (its parts).</summary>
    public class SwimGoProMatch
    {
        public SwimTake Take;
        public SwimTakeCamera Camera;
        public List<SwimGoProFile> Files = new List<SwimGoProFile>();
        public List<string> Targets = new List<string>();

        /// <summary>The file is already at its place, whole (an earlier copy or download that stopped later): not copied again.</summary>
        public List<bool> Present = new List<bool>();

        /// <summary>(2.22.0) The hours the clock of the camera is off the computer (0 almost always).</summary>
        public int Hours;
    }

    /// <summary>
    /// (Swim 2.21.0) The videos of the GoPro cameras on a card (in a card reader, or a folder they were copied in) or in
    /// the list of the card by Wi-Fi, found for the recordings started from Kinovea: same camera (its serial number in the file) and the
    /// same start (within 20 s; the clock of a camera written as universal time is also found, a whole number of hours away). The files
    /// are copied, never moved or deleted; a CSV in the folder of the day says what went where.
    /// </summary>
    public static class SwimGoProFiles
    {
        private static readonly Regex goProName = new Regex(@"^G[XHPL](\d{2})(\d{4})\.MP4$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex oldName = new Regex(@"^GOPR(\d{4})\.MP4$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex goProFolder = new Regex(@"^\d{3}GOPRO$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly DateTime mp4Epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        #region Reading
        /// <summary>The number and part of a GoPro file from its name (GX020123.MP4: number 123, part 2), false when it is not one.</summary>
        public static bool ParseName(string name, out int number, out int part)
        {
            number = 0;
            part = 1;
            System.Text.RegularExpressions.Match m = goProName.Match(name ?? "");
            if (m.Success)
            {
                part = Math.Max(1, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                number = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                return true;
            }

            m = oldName.Match(name ?? "");
            if (m.Success)
            {
                number = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        /// <summary>
        /// The GoPro cards in the computer now (in a card reader): the drives other than the one of Windows with a DCIM\100GOPRO…
        /// folder. (A GoPro by USB is a device of Windows, not a drive: its files are copied to a folder first.)
        /// </summary>
        public static List<string> FindCards()
        {
            List<string> result = new List<string>();
            string system = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "";
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Removable && drive.DriveType != DriveType.Fixed)
                        continue;

                    if (!drive.IsReady)
                        continue;

                    string root = drive.RootDirectory.FullName;
                    if (string.Equals(root, system, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string dcim = System.IO.Path.Combine(root, "DCIM");
                    if (Directory.Exists(dcim) && Directory.EnumerateDirectories(dcim).Any(d => goProFolder.IsMatch(System.IO.Path.GetFileName(d))))
                        result.Add(root);
                }
                catch (Exception e)
                {
                    log.Debug("A drive could not be looked at for a GoPro card.", e);
                }
            }

            return result;
        }

        /// <summary>The GoPro videos under a folder (a card: DCIM\100GOPRO…), read; the others are left.</summary>
        public static List<SwimGoProFile> Scan(string folder, CancellationToken token)
        {
            List<SwimGoProFile> result = new List<SwimGoProFile>();
            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(folder, "*.MP4", SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                log.Error("Could not list the folder of the videos.", e);
                return result;
            }

            foreach (string path in paths)
            {
                token.ThrowIfCancellationRequested();
                int number, part;
                string name = System.IO.Path.GetFileName(path);
                if (!ParseName(name, out number, out part))
                    continue;

                SwimGoProFile f = Read(path);
                f.Number = number;
                f.Part = part;
                result.Add(f);
            }

            return result;
        }

        /// <summary>A GoPro MP4: the start of its recording (mvhd), the serial number and model (the GPMF of its udta).</summary>
        public static SwimGoProFile Read(string path)
        {
            SwimGoProFile f = new SwimGoProFile { Path = path, Name = System.IO.Path.GetFileName(path), Folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? "") };
            try
            {
                FileInfo info = new FileInfo(path);
                f.Size = info.Length;
                using (FileStream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536))
                using (BinaryReader r = new BinaryReader(s))
                {
                    ReadBoxes(r, 0, s.Length, f, 0);
                }

                if (f.Created == DateTime.MinValue)
                    f.Created = info.LastWriteTime;
                else if (f.ZoneMinutes != int.MinValue && Math.Abs(f.ZoneMinutes) <= 14 * 60)
                    f.Created = f.Created.AddMinutes(f.ZoneMinutes); // universal time in the file (HERO12…): the clock of the camera
            }
            catch (Exception e)
            {
                log.Debug("Could not read a GoPro file.", e);
            }

            return f;
        }

        private static void ReadBoxes(BinaryReader r, long start, long end, SwimGoProFile f, int depth)
        {
            Stream s = r.BaseStream;
            long position = start;
            while (position + 8 <= end && depth < 6)
            {
                s.Position = position;
                long size = ReadUInt32(r);
                string type = Encoding.ASCII.GetString(r.ReadBytes(4));
                long header = 8;
                if (size == 1)
                {
                    size = (long)ReadUInt64(r);
                    header = 16;
                }
                else if (size == 0)
                {
                    size = end - position;
                }

                if (size < header || position + size > end)
                    break;

                long content = position + header;
                long contentEnd = position + size;
                switch (type)
                {
                    case "moov":
                    case "udta":
                        ReadBoxes(r, content, contentEnd, f, depth + 1);
                        break;
                    case "mvhd":
                        s.Position = content;
                        int version = r.ReadByte();
                        r.ReadBytes(3);
                        double seconds = version == 1 ? (double)ReadUInt64(r) : ReadUInt32(r);
                        if (seconds > 0)
                            f.Created = mp4Epoch.AddSeconds(seconds);

                        // Modification time, then the time scale and the duration.
                        if (version == 1)
                            ReadUInt64(r);
                        else
                            ReadUInt32(r);

                        double scale = ReadUInt32(r);
                        double units = version == 1 ? (double)ReadUInt64(r) : ReadUInt32(r);
                        if (scale > 0 && units > 0)
                            f.Duration = units / scale;
                        break;
                    case "GPMF":
                        int length = (int)Math.Min(contentEnd - content, 1 << 20);
                        s.Position = content;
                        ReadGpmf(r.ReadBytes(length), 0, length, f, 0);
                        break;
                }

                position += size;
            }
        }

        /// <summary>The keys of the GPMF of the camera: CASN (serial number), MINF (model).</summary>
        private static void ReadGpmf(byte[] data, int start, int end, SwimGoProFile f, int depth)
        {
            int p = start;
            while (p + 8 <= end && depth < 6)
            {
                string key = Encoding.ASCII.GetString(data, p, 4);
                byte type = data[p + 4];
                int structSize = data[p + 5];
                int repeat = (data[p + 6] << 8) | data[p + 7];
                int length = structSize * repeat;
                int padded = (length + 3) & ~3;
                int content = p + 8;
                if (content + length > end)
                    break;

                if (type == 0)
                {
                    ReadGpmf(data, content, content + length, f, depth + 1);
                }
                else if (key == "TZON" && type == (byte)'s' && length >= 2)
                {
                    f.ZoneMinutes = (short)((data[content] << 8) | data[content + 1]);
                }
                else if (type == (byte)'c' && (key == "CASN" || key == "MINF"))
                {
                    string text = Encoding.ASCII.GetString(data, content, length).Trim('\0', ' ');
                    if (key == "CASN")
                        f.Serial = text;
                    else
                        f.Model = text;
                }

                p = content + padded;
            }
        }

        #region Key images (2.22.0)
        /// <summary>The tables of a track of an MP4 that give the times of its images.</summary>
        private sealed class Mp4Track
        {
            public string Handler = "";
            public long Timescale;
            public List<long[]> Stts = new List<long[]>();
            public List<long> Stss;
            public List<long[]> Ctts;
            public long MediaTime;

            /// <summary>The empty edits before the first one (in the time scale of the movie): the track starts that much later.</summary>
            public long EmptyEdits;
        }

        /// <summary>The time scale of the movie (mvhd), for the empty edits.</summary>
        private sealed class Mp4Movie
        {
            public long Timescale;
        }

        /// <summary>
        /// (2.22.0) The times (s, on the clock of the file) of the key images of the first video track of an MP4: the images a cut without
        /// coding starts at. From its tables (stts, stss, ctts and the first edit). Null when not read, empty when every image is a key one.
        /// </summary>
        public static double[] KeyframeTimes(string path)
        {
            double seconds;
            return KeyframeTimes(path, out seconds);
        }

        /// <summary>
        /// (2.22.0) The same, and the length of the picture (s, 0 when not read): the files of a long recording follow each other by their
        /// pictures (the sound of a file may go a little longer).
        /// </summary>
        public static double[] KeyframeTimes(string path, out double videoSeconds)
        {
            videoSeconds = 0;
            try
            {
                using (FileStream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536))
                using (BinaryReader r = new BinaryReader(s))
                {
                    List<Mp4Track> tracks = new List<Mp4Track>();
                    Mp4Movie movie = new Mp4Movie();
                    ReadTracks(r, 0, s.Length, tracks, null, movie, 0);
                    Mp4Track video = tracks.FirstOrDefault(t => t.Handler == "vide" && t.Timescale > 0 && t.Stts.Count > 0);
                    if (video == null)
                        return null;

                    double delay = movie.Timescale > 0 ? video.EmptyEdits / (double)movie.Timescale : 0;
                    videoSeconds = delay + video.Stts.Sum(e => (double)e[0] * e[1]) / video.Timescale;
                    if (video.Stss == null)
                        return new double[0];

                    // The decode time of each key image (sample numbers from 1), then its presentation time.
                    List<double> times = new List<double>(video.Stss.Count);
                    long sample = 1, time = 0;
                    int entry = 0;
                    long leftInEntry = video.Stts[0][0];
                    List<long> sync = video.Stss.OrderBy(n => n).ToList();
                    foreach (long n in sync)
                    {
                        while (sample < n && entry < video.Stts.Count)
                        {
                            long step = Math.Min(n - sample, leftInEntry);
                            time += step * video.Stts[entry][1];
                            sample += step;
                            leftInEntry -= step;
                            if (leftInEntry == 0)
                            {
                                entry++;
                                if (entry < video.Stts.Count)
                                    leftInEntry = video.Stts[entry][0];
                            }
                        }

                        long pts = time + CompositionOffset(video.Ctts, n);
                        times.Add(delay + (pts - video.MediaTime) / (double)video.Timescale);
                    }

                    return times.OrderBy(t => t).ToArray();
                }
            }
            catch (Exception e)
            {
                log.Debug("Could not read the key images of a video.", e);
                return null;
            }
        }

        private static long CompositionOffset(List<long[]> ctts, long sample)
        {
            if (ctts == null)
                return 0;

            long first = 1;
            foreach (long[] e in ctts)
            {
                if (sample < first + e[0])
                    return e[1];

                first += e[0];
            }

            return 0;
        }

        private static void ReadTracks(BinaryReader r, long start, long end, List<Mp4Track> tracks, Mp4Track track, Mp4Movie movie, int depth)
        {
            Stream s = r.BaseStream;
            long position = start;
            while (position + 8 <= end && depth < 8)
            {
                s.Position = position;
                long size = ReadUInt32(r);
                string type = Encoding.ASCII.GetString(r.ReadBytes(4));
                long header = 8;
                if (size == 1)
                {
                    size = (long)ReadUInt64(r);
                    header = 16;
                }
                else if (size == 0)
                {
                    size = end - position;
                }

                if (size < header || position + size > end)
                    break;

                long content = position + header;
                long contentEnd = position + size;
                switch (type)
                {
                    case "moov":
                    case "mdia":
                    case "minf":
                    case "stbl":
                    case "edts":
                        ReadTracks(r, content, contentEnd, tracks, track, movie, depth + 1);
                        break;
                    case "trak":
                        Mp4Track t = new Mp4Track();
                        tracks.Add(t);
                        ReadTracks(r, content, contentEnd, tracks, t, movie, depth + 1);
                        break;
                    case "mvhd":
                        {
                            s.Position = content;
                            int version = r.ReadByte();
                            r.ReadBytes(3);
                            if (version == 1)
                            {
                                ReadUInt64(r);
                                ReadUInt64(r);
                            }
                            else
                            {
                                ReadUInt32(r);
                                ReadUInt32(r);
                            }

                            movie.Timescale = ReadUInt32(r);
                        }

                        break;
                    case "hdlr":
                        if (track != null)
                        {
                            s.Position = content + 8;
                            track.Handler = Encoding.ASCII.GetString(r.ReadBytes(4));
                        }

                        break;
                    case "mdhd":
                        if (track != null)
                        {
                            s.Position = content;
                            int version = r.ReadByte();
                            r.ReadBytes(3);
                            if (version == 1)
                            {
                                ReadUInt64(r);
                                ReadUInt64(r);
                            }
                            else
                            {
                                ReadUInt32(r);
                                ReadUInt32(r);
                            }

                            track.Timescale = ReadUInt32(r);
                        }

                        break;
                    case "stts":
                    case "ctts":
                        if (track != null)
                        {
                            s.Position = content;
                            int version = r.ReadByte();
                            r.ReadBytes(3);
                            long count = ReadUInt32(r);
                            List<long[]> entries = new List<long[]>();
                            for (long i = 0; i < count && s.Position + 8 <= contentEnd; i++)
                            {
                                long n = ReadUInt32(r);
                                long v = ReadUInt32(r);
                                if (type == "ctts" && (version == 1 || v > int.MaxValue))
                                    v = (int)(uint)v;

                                entries.Add(new[] { n, v });
                            }

                            if (type == "stts")
                                track.Stts = entries;
                            else
                                track.Ctts = entries;
                        }

                        break;
                    case "stss":
                        if (track != null)
                        {
                            s.Position = content + 4;
                            long count = ReadUInt32(r);
                            track.Stss = new List<long>();
                            for (long i = 0; i < count && s.Position + 4 <= contentEnd; i++)
                                track.Stss.Add(ReadUInt32(r));
                        }

                        break;
                    case "elst":
                        if (track != null)
                        {
                            s.Position = content;
                            int version = r.ReadByte();
                            r.ReadBytes(3);
                            long count = ReadUInt32(r);
                            for (long i = 0; i < count && s.Position + 12 <= contentEnd; i++)
                            {
                                // The duration of the edit, where it starts in the track (-1: an empty edit, the track starts later), its rate.
                                long duration = version == 1 ? (long)ReadUInt64(r) : ReadUInt32(r);
                                long media = version == 1 ? (long)ReadUInt64(r) : (int)ReadUInt32(r);
                                r.ReadBytes(4);
                                if (media >= 0)
                                {
                                    track.MediaTime = media;
                                    break;
                                }

                                track.EmptyEdits += duration;
                            }
                        }

                        break;
                }

                position += size;
            }
        }
        #endregion

        private static uint ReadUInt32(BinaryReader r)
        {
            byte[] b = r.ReadBytes(4);
            return b.Length < 4 ? 0u : (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
        }

        private static ulong ReadUInt64(BinaryReader r)
        {
            ulong high = ReadUInt32(r);
            ulong low = ReadUInt32(r);
            return (high << 32) | low;
        }
        #endregion

        #region Matching
        /// <summary>
        /// The files of one camera for its recordings (not on the computer yet): a recording takes the file (and its parts) that started
        /// within 20 s of its start, nearest first. The clock of the camera is the same for all its files: the clock of the computer
        /// first; another clock a whole number of hours off (universal time, another time zone) only when it finds more recordings and at
        /// least two (a single file of another hour proves nothing: a recording without a file must not take the video of another one).
        /// </summary>
        public static List<SwimGoProMatch> Match(List<SwimTake> takes, string alias, List<SwimGoProFile> files)
        {
            // (2.22.0) The parts of a continuous recording are cut from the files of their recording: not matched themselves.
            takes = takes.Where(t => !t.IsPart).ToList();

            // The clock of the computer first; another one only with more recordings found, and at least two.
            int pending = takes.Count(t => t.Cameras.Any(c => c.Alias == alias && !c.Done));
            double error;
            List<SwimGoProMatch> best = MatchAt(takes, alias, files, 0, out error);
            int bestHours = 0;
            foreach (int hours in HourOffsets())
            {
                if (best.Count == pending)
                    break;

                if (hours == 0)
                    continue;

                List<SwimGoProMatch> found = MatchAt(takes, alias, files, hours, out error);
                if (found.Count >= 2 && found.Count > best.Count)
                {
                    best = found;
                    bestHours = hours;
                }
            }

            if (bestHours != 0 && best.Count > 0)
                log.DebugFormat("The clock of the camera \"{0}\" is {1} h off the computer.", alias, bestHours);

            return best;
        }

        /// <summary>0 first, then ±1 … ±14 hours (all the time zones).</summary>
        private static IEnumerable<int> HourOffsets()
        {
            yield return 0;
            for (int h = 1; h <= 14; h++)
            {
                yield return h;
                yield return -h;
            }
        }

        /// <summary>The recordings of a camera and their files with its clock `hours` off; `error` the seconds off in all.</summary>
        private static List<SwimGoProMatch> MatchAt(List<SwimTake> takes, string alias, List<SwimGoProFile> files, int hours, out double error)
        {
            error = 0;
            List<SwimGoProMatch> result = new List<SwimGoProMatch>();
            List<IGrouping<string, SwimGoProFile>> groups = files.GroupBy(f => f.Folder + "/" + f.Number.ToString(CultureInfo.InvariantCulture)).ToList();
            HashSet<string> used = new HashSet<string>();

            foreach (SwimTake take in takes.OrderBy(t => t.Start))
            {
                SwimTakeCamera camera = take.Cameras.FirstOrDefault(c => c.Alias == alias);
                if (camera == null || camera.Done)
                    continue;

                IGrouping<string, SwimGoProFile> best = null;
                double bestError = double.MaxValue;
                foreach (IGrouping<string, SwimGoProFile> g in groups)
                {
                    if (used.Contains(g.Key))
                        continue;

                    SwimGoProFile first = g.OrderBy(x => x.Part).First();
                    double e = TimeError(first.Created, take.Start, hours);
                    if (e < bestError)
                    {
                        bestError = e;
                        best = g;
                    }
                }

                if (best == null || bestError > 20)
                    continue;

                used.Add(best.Key);
                error += bestError;
                result.Add(new SwimGoProMatch { Take = take, Camera = camera, Files = best.OrderBy(x => x.Part).ToList(), Hours = hours });
            }

            return result;
        }

        /// <summary>Seconds between the start of a file and of a recording, the clock of the camera `hours` off.</summary>
        private static double TimeError(DateTime fileStart, DateTime takeStart, int hours)
        {
            if (fileStart == DateTime.MinValue)
                return double.MaxValue;

            // The camera starts a little after the command (up to 2 s): its file starts at or after it.
            double seconds = (fileStart.AddHours(hours) - takeStart).TotalSeconds;
            return seconds >= -2 ? Math.Abs(seconds - 1) : Math.Abs(seconds) + 5;
        }

        /// <summary>
        /// Which camera (by its name) made the files of a card: by the serial numbers written in them, else by the model when only one
        /// camera of the recordings is of that model; null when the files do not say.
        /// </summary>
        public static string CameraOf(List<SwimTake> takes, List<SwimGoProFile> files)
        {
            List<SwimTakeCamera> cameras = takes.SelectMany(t => t.Cameras).ToList();
            foreach (SwimGoProFile f in files)
            {
                if (string.IsNullOrEmpty(f.Serial))
                    continue;

                string serial = f.Serial.Trim();
                SwimTakeCamera c = cameras.LastOrDefault(x => !string.IsNullOrEmpty(x.Serial) &&
                    (string.Equals(x.Serial.Trim(), serial, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(SwimGoProTakes.FileSerialOf(x.Serial), serial, StringComparison.OrdinalIgnoreCase)));
                if (c != null)
                    return c.Alias;
            }

            string model = files.Select(f => f.Model).FirstOrDefault(m => !string.IsNullOrEmpty(m));
            if (!string.IsNullOrEmpty(model))
            {
                List<string> aliases = cameras.Where(c => SameModel(c.Model, model)).Select(c => c.Alias).Distinct().ToList();
                if (aliases.Count == 1)
                    return aliases[0];
            }

            return null;
        }

        private static bool SameModel(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;

            string x = a.ToUpperInvariant().Replace(" ", "").Replace("BLACK", "");
            string y = b.ToUpperInvariant().Replace(" ", "").Replace("BLACK", "");
            return x == y || x.Contains(y) || y.Contains(x);
        }

        /// <summary>
        /// The paths of the files of the matches (the parts after the first get "_μέρος2"…), free of the files already there; a file
        /// already at its place with the same size (a copy or download that stopped later) keeps its place and is not copied again.
        /// </summary>
        public static void PlanTargets(string root, List<SwimGoProMatch> matches)
        {
            HashSet<string> taken = new HashSet<string>();
            foreach (SwimGoProMatch m in matches)
            {
                m.Targets.Clear();
                m.Present.Clear();
                string first = null;
                for (int i = 0; i < m.Files.Count; i++)
                {
                    string wanted;
                    if (i == 0)
                    {
                        wanted = SwimGoProTakes.TargetPath(root, m.Take, m.Camera.Alias, 1);
                    }
                    else
                    {
                        // The parts follow the name of the first one.
                        string baseName = System.IO.Path.GetFileNameWithoutExtension(first);
                        wanted = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(first), baseName + "_μέρος" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".MP4");
                    }

                    string already = FindPresent(wanted, m.Files[i], taken);
                    bool present = already != null;
                    string target = present ? already : SwimGoProTakes.FreePath(wanted, taken);
                    if (present)
                        taken.Add(already.ToLowerInvariant());

                    if (i == 0)
                        first = target;

                    m.Targets.Add(target);
                    m.Present.Add(present);
                }
            }
        }

        /// <summary>
        /// The place of a file already copied whole (a copy or download that stopped later): the wanted path or the next names
        /// ("…_2.MP4"…) while they are taken, the one with the same size (and the same start and end when the file of the card is
        /// here to compare); null when none.
        /// </summary>
        private static string FindPresent(string wanted, SwimGoProFile f, HashSet<string> taken)
        {
            string folder = System.IO.Path.GetDirectoryName(wanted);
            string name = System.IO.Path.GetFileNameWithoutExtension(wanted);
            string extension = System.IO.Path.GetExtension(wanted);
            for (int n = 1; n < 100; n++)
            {
                string candidate = n == 1 ? wanted : System.IO.Path.Combine(folder, name + "_" + n.ToString(CultureInfo.InvariantCulture) + extension);
                if (!File.Exists(candidate))
                    return null;

                if (!taken.Contains(candidate.ToLowerInvariant()) && IsWhole(candidate, f.Size) && SameEnds(candidate, f.Path))
                    return candidate;
            }

            return null;
        }

        /// <summary>The first and last 64 KB of two files are the same (true when the source is not a file here: a list by Wi-Fi).</summary>
        private static bool SameEnds(string copy, string source)
        {
            try
            {
                if (string.IsNullOrEmpty(source) || !File.Exists(source))
                    return true;

                using (FileStream a = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (FileStream b = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (a.Length != b.Length)
                        return false;

                    int chunk = (int)Math.Min(65536, a.Length);
                    foreach (long position in new[] { 0L, a.Length - chunk })
                    {
                        byte[] x = new byte[chunk];
                        byte[] y = new byte[chunk];
                        a.Position = position;
                        b.Position = position;
                        if (ReadAll(a, x) != chunk || ReadAll(b, y) != chunk)
                            return false;

                        for (int i = 0; i < chunk; i++)
                        {
                            if (x[i] != y[i])
                                return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int ReadAll(Stream s, byte[] buffer)
        {
            int total = 0;
            int read;
            while (total < buffer.Length && (read = s.Read(buffer, total, buffer.Length - total)) > 0)
                total += read;

            return total;
        }

        /// <summary>A file is at this path with this size (a size not known never matches).</summary>
        public static bool IsWhole(string path, long size)
        {
            try
            {
                return size > 0 && File.Exists(path) && new FileInfo(path).Length == size;
            }
            catch (Exception)
            {
                return false;
            }
        }
        #endregion

        #region Copy
        /// <summary>
        /// Copies the files of the matches (to a temporary name, then renamed), keeps them as done and writes what went where in the CSV
        /// of each folder of the day. Returns the number of files copied; `error` says the first problem.
        /// </summary>
        public static int Copy(List<SwimGoProMatch> matches, Action<string, long, long> progress, CancellationToken token, out string error)
        {
            error = null;
            long total = matches.SelectMany(m => m.Files).Sum(f => Math.Max(0, f.Size));
            long done = 0;
            int copied = 0;
            Dictionary<string, List<string>> csv = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (SwimGoProMatch m in matches)
            {
                List<string> targets = new List<string>();
                bool ok = true;
                for (int i = 0; i < m.Files.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    SwimGoProFile f = m.Files[i];
                    string target = m.Targets[i];
                    string partial = target + ".part";
                    if (i < m.Present.Count && m.Present[i])
                    {
                        done += Math.Max(0, f.Size);
                        targets.Add(target);
                        continue;
                    }

                    try
                    {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                        long before = done;
                        CopyFile(f.Path, partial, token, bytes => progress(System.IO.Path.GetFileName(target), before + bytes, total));
                        File.Move(partial, target);
                        done += Math.Max(0, f.Size);
                        targets.Add(target);
                        copied++;

                        string session = SessionOf(target);
                        if (!csv.ContainsKey(session))
                            csv[session] = new List<string>();

                        csv[session].Add("Αντιγραφή;" + f.Path + ";" + target);
                    }
                    catch (OperationCanceledException)
                    {
                        TryDelete(partial);
                        throw;
                    }
                    catch (Exception e)
                    {
                        TryDelete(partial);
                        log.Error("Could not copy a video of a GoPro.", e);
                        error = error ?? (f.Name + ": " + e.Message);
                        ok = false;
                        break;
                    }
                }

                if (ok)
                    SwimGoProTakes.MarkDone(m.Take.Id, m.Camera.Alias, string.Join(", ", m.Files.Select(x => x.Name)), targets);
            }

            WriteCsv(csv);
            return copied;
        }

        /// <summary>A file of a download by Wi-Fi is on the computer: the same CSV line as a copy.</summary>
        public static void LogDownloads(IEnumerable<KeyValuePair<string, string>> downloads)
        {
            Dictionary<string, List<string>> csv = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> d in downloads)
            {
                string session = SessionOf(d.Value);
                if (!csv.ContainsKey(session))
                    csv[session] = new List<string>();

                csv[session].Add("Κατέβασμα Wi-Fi;" + d.Key + ";" + d.Value);
            }

            WriteCsv(csv);
        }

        /// <summary>(2.22.0) The videos of the parts of a continuous recording, cut: «Κοπή;GX010123.MP4 12.0 – 45.3 s;…» in the CSV of the day.</summary>
        public static void LogCuts(IEnumerable<KeyValuePair<string, string>> cuts)
        {
            Dictionary<string, List<string>> csv = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> c in cuts)
            {
                string session = SessionOf(c.Value);
                if (!csv.ContainsKey(session))
                    csv[session] = new List<string>();

                csv[session].Add("Κοπή;" + c.Key + ";" + c.Value);
            }

            WriteCsv(csv);
        }

        private static void CopyFile(string source, string target, CancellationToken token, Action<long> onBytes)
        {
            byte[] buffer = new byte[4 << 20];
            using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20))
            using (FileStream output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                long written = 0;
                DateTime last = DateTime.MinValue;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    written += read;
                    if ((DateTime.UtcNow - last).TotalMilliseconds > 300)
                    {
                        last = DateTime.UtcNow;
                        onBytes(written);
                    }
                }

                onBytes(written);
                if (written != input.Length)
                    throw new IOException("The copy is not whole.");
            }
        }

        /// <summary>The folder of the day of a target (the one named …video, else its folder).</summary>
        private static string SessionOf(string target)
        {
            DirectoryInfo d = new DirectoryInfo(System.IO.Path.GetDirectoryName(target));
            DirectoryInfo walk = d;
            while (walk != null)
            {
                if (walk.Name.EndsWith("video", StringComparison.OrdinalIgnoreCase))
                    return walk.FullName;

                walk = walk.Parent;
            }

            return d.FullName;
        }

        private static void WriteCsv(Dictionary<string, List<string>> csv)
        {
            // One list per day: the copies and downloads of a day are added to it.
            string stamp = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            foreach (KeyValuePair<string, List<string>> entry in csv)
            {
                try
                {
                    string path = System.IO.Path.Combine(entry.Key, "_Kinovea_βίντεο_ιστορικό_" + stamp + ".csv");
                    List<string> lines = new List<string>();
                    if (!File.Exists(path))
                        lines.Add("Ενέργεια;Από;Προς");

                    lines.AddRange(entry.Value);
                    File.AppendAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(true));
                }
                catch (Exception e)
                {
                    log.Error("Could not write the list of the copied videos.", e);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception) { }
        }
        #endregion

        #region Wi-Fi
        /// <summary>The files of the list of a card by Wi-Fi, as files to match (no serial: the camera is the one asked).</summary>
        public static List<SwimGoProFile> FromRemote(IList<RemoteMediaFile> files)
        {
            List<SwimGoProFile> result = new List<SwimGoProFile>();
            foreach (RemoteMediaFile r in files)
            {
                int number, part;
                if (!ParseName(r.Name, out number, out part))
                    continue;

                result.Add(new SwimGoProFile { Path = r.Folder + "/" + r.Name, Name = r.Name, Folder = r.Folder, Number = number, Part = part, Size = r.Size, Created = r.Created });
            }

            return result;
        }
        #endregion
    }
}
