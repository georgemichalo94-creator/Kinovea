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
using Kinovea.Services;
using Newtonsoft.Json;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (Swim 2.28.0) Where a camera of the wall is in the pool: its distance from the wall of the start (NaN: not said; 0: the camera
    /// out of the water at the blocks), its line (a part of the width of its image: where its distance is) and the metres of the lane
    /// across its image (0: not measured, the speeds use an estimate). Kept by the name of the camera.
    /// </summary>
    public sealed class SwimWallPlace
    {
        public string Camera = "";
        public double Distance = double.NaN;
        public float Line = 0.5f;
        public double Width;

        /// <summary>The camera is under the water: it has a line at its distance.</summary>
        [JsonIgnore]
        public bool Under
        {
            get { return SwimWallPlaces.Under(Distance); }
        }
    }

    /// <summary>
    /// (Swim 2.28.0) The places of the cameras of the wall in the pool, said once for a pool (SwimWall.json in the settings folder).
    /// Five cameras on the wall are, in their order, out of the water at the blocks and under the water at 2.5, 5, 10 and 15 m until
    /// said otherwise (the right click on a camera).
    /// </summary>
    public static class SwimWallPlaces
    {
        /// <summary>The places of five cameras in their order on the wall.</summary>
        public static readonly double[] Five = { 0, 2.5, 5, 10, 15 };

        /// <summary>The distances of the menu of a camera.</summary>
        public static readonly double[] Choices = { 0, 2.5, 5, 7.5, 10, 12.5, 15, 20, 25 };

        /// <summary>The metres of the lane across an image not measured (a camera on the side wall of the pool, near the lane).</summary>
        public const double EstimatedWidth = 3.0;

        /// <summary>(2.29.0) The length of the pole said last (metres between its two marks), the first answer of the next measure.</summary>
        public static double PoleLength = 2.0;

        /// <summary>A place of a camera changed (its distance, its line or its width).</summary>
        public static event Action Changed;

        private static List<SwimWallPlace> places;
        private static readonly object locker = new object();
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static string FilePath
        {
            get { return Path.Combine(Software.SettingsDirectory, "SwimWall.json"); }
        }

        /// <summary>A distance under the water (a camera with a line).</summary>
        public static bool Under(double distance)
        {
            return !double.IsNaN(distance) && distance > 0.01;
        }

        /// <summary>A distance as the wall writes it ("2.5 m", "10 m").</summary>
        public static string Label(double distance)
        {
            return double.IsNaN(distance) ? "" : distance.ToString("0.##", CultureInfo.InvariantCulture) + " m";
        }

        /// <summary>The place of a camera: the one said for it, else (five cameras on the wall) the place of its number on the wall.</summary>
        public static SwimWallPlace Of(string camera, int number, int count)
        {
            SwimWallPlace place = new SwimWallPlace { Camera = camera ?? "" };
            lock (locker)
            {
                SwimWallPlace saved = Find(camera);
                if (saved != null)
                {
                    place.Distance = saved.Distance;
                    place.Line = saved.Line;
                    place.Width = saved.Width;
                    return place;
                }
            }

            if (count == Five.Length && number >= 1 && number <= Five.Length)
                place.Distance = Five[number - 1];

            return place;
        }

        /// <summary>The distance of a camera (NaN: no place).</summary>
        public static void SetDistance(string camera, double distance)
        {
            Change(camera, p => p.Distance = distance);
        }

        /// <summary>The line of a camera, a part of the width of its image.</summary>
        public static void SetLine(string camera, float line)
        {
            Change(camera, p => p.Line = Math.Max(0.02f, Math.Min(0.98f, line)));
        }

        /// <summary>The metres of the lane across the image of a camera (0: not measured).</summary>
        public static void SetWidth(string camera, double width)
        {
            Change(camera, p => p.Width = width > 0.2 && width < 50 ? width : 0);
        }

        private static void Change(string camera, Action<SwimWallPlace> change)
        {
            if (string.IsNullOrEmpty(camera))
                return;

            lock (locker)
            {
                SwimWallPlace place = Find(camera);
                if (place == null)
                {
                    place = new SwimWallPlace { Camera = camera };
                    places.Add(place);
                }

                change(place);
                Save();
            }

            Action changed = Changed;
            if (changed != null)
                changed();
        }

        private static SwimWallPlace Find(string camera)
        {
            if (places == null)
                Load();

            return string.IsNullOrEmpty(camera) ? null : places.FirstOrDefault(p => p != null && p.Camera == camera);
        }

        private static void Load()
        {
            places = new List<SwimWallPlace>();
            try
            {
                if (File.Exists(FilePath))
                {
                    List<SwimWallPlace> read = JsonConvert.DeserializeObject<List<SwimWallPlace>>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (read != null)
                        places = read.Where(p => p != null && !string.IsNullOrEmpty(p.Camera)).ToList();
                }
            }
            catch (Exception e)
            {
                log.Error("Could not read the places of the cameras of the wall.", e);
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(places, Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception e)
            {
                log.Error("Could not save the places of the cameras of the wall.", e);
            }
        }
    }

    /// <summary>A point of the speed of the swimmer along the pool: metres from the wall of the start, metres per second.</summary>
    public struct SwimSpeedPoint
    {
        public double Metres;
        public double Speed;

        public SwimSpeedPoint(double metres, double speed)
        {
            Metres = metres;
            Speed = speed;
        }
    }

    /// <summary>
    /// (Swim 2.28.0) What one camera of the wall saw of an attempt: its place, the way the swimmer went in its image, the time of its
    /// line and the places of his head (the seconds from the horn).
    /// </summary>
    public sealed class SwimLiveTrack
    {
        public int Number;
        public string Camera = "";
        public double Distance = double.NaN;
        public float Line = 0.5f;

        /// <summary>The metres of the lane across the image (0: not measured, EstimatedWidth is used).</summary>
        public double Width;

        public int Way;
        public double Crossed = double.NaN;
        public List<SwimLineSample> Samples = new List<SwimLineSample>();

        /// <summary>The metres from the wall of the start of a place of the head in the image.</summary>
        public double Metres(float head)
        {
            double width = Width > 0 ? Width : SwimWallPlaces.EstimatedWidth;
            return Distance + (head - Line) * (Way == 0 ? 1 : Way) * width;
        }

        /// <summary>
        /// The speed of the head along this camera: at each place, the slope of the places within 'window' seconds around it (a line
        /// fitted to them, at least four places); in metres from the wall and metres per second, in the order of the places.
        /// </summary>
        public List<SwimSpeedPoint> Speed(double window = 0.12)
        {
            List<SwimSpeedPoint> points = new List<SwimSpeedPoint>();
            if (Way == 0 || double.IsNaN(Distance) || Samples == null || Samples.Count < 4)
                return points;

            List<SwimLineSample> samples = Samples.Where(s => !float.IsNaN(s.Head) && !double.IsNaN(s.Seconds)).OrderBy(s => s.Seconds).ToList();
            double[] t = samples.Select(s => s.Seconds).ToArray();
            double[] m = samples.Select(s => Metres(s.Head)).ToArray();
            int from = 0;
            for (int i = 0; i < t.Length; i++)
            {
                while (from < i && t[i] - t[from] > window)
                    from++;

                int to = i;
                while (to + 1 < t.Length && t[to + 1] - t[i] <= window)
                    to++;

                int n = to - from + 1;
                if (n < 4)
                    continue;

                double st = 0, sm = 0, stt = 0, stm = 0;
                for (int k = from; k <= to; k++)
                {
                    st += t[k];
                    sm += m[k];
                    stt += t[k] * t[k];
                    stm += t[k] * m[k];
                }

                double d = n * stt - st * st;
                if (Math.Abs(d) < 1e-9)
                    continue;

                double slope = (n * stm - st * sm) / d;
                double at = (sm - slope * st) / n + slope * t[i];
                if (slope > 0.2 && slope < 6.5)
                    points.Add(new SwimSpeedPoint(at, slope));
            }

            return points;
        }
    }

    /// <summary>(Swim 2.28.0) One attempt measured live from the horn: the times of the lines of the cameras and what each one saw.</summary>
    public sealed class SwimLiveAttempt
    {
        /// <summary>1, 2, … in the order of the attempts since the last «new swimmer».</summary>
        public int Number;

        public DateTime Horn = DateTime.MinValue;

        /// <summary>From the horn to the feet off the block (NaN: not measured).</summary>
        public double Reaction = double.NaN;

        public List<SwimLiveTrack> Tracks = new List<SwimLiveTrack>();

        private List<List<SwimSpeedPoint>> speed;

        /// <summary>The time of the line at this distance, seconds from the horn (NaN: not seen).</summary>
        public double TimeAt(double distance)
        {
            SwimLiveTrack track = Tracks.FirstOrDefault(t => Math.Abs(t.Distance - distance) < 0.01);
            return track == null ? double.NaN : track.Crossed;
        }

        /// <summary>The distances of the lines (the cameras under the water), from the nearest.</summary>
        public List<double> Lines
        {
            get { return Tracks.Where(t => SwimWallPlaces.Under(t.Distance)).Select(t => t.Distance).Distinct().OrderBy(d => d).ToList(); }
        }

        /// <summary>A camera of the attempt has no measured width: its speeds are an estimate.</summary>
        public bool Estimated
        {
            get { return Tracks.Any(t => SwimWallPlaces.Under(t.Distance) && t.Width <= 0); }
        }

        /// <summary>The speed of the head along each camera, from the nearest camera (computed once).</summary>
        public List<List<SwimSpeedPoint>> Speed
        {
            get
            {
                if (speed == null)
                {
                    speed = Tracks.Where(t => SwimWallPlaces.Under(t.Distance)).OrderBy(t => t.Distance)
                        .Select(t => t.Speed()).Where(p => p.Count > 0).ToList();
                }

                return speed;
            }
        }

        /// <summary>The highest speed of the head and where (NaN: none).</summary>
        public SwimSpeedPoint Top
        {
            get
            {
                SwimSpeedPoint top = new SwimSpeedPoint(double.NaN, double.NaN);
                foreach (List<SwimSpeedPoint> part in Speed)
                {
                    foreach (SwimSpeedPoint p in part)
                    {
                        if (double.IsNaN(top.Speed) || p.Speed > top.Speed)
                            top = p;
                    }
                }

                return top;
            }
        }

        /// <summary>The time of the farthest line seen (NaN: none) and its distance.</summary>
        public double Final(out double distance)
        {
            distance = double.NaN;
            foreach (double d in Lines.OrderByDescending(x => x))
            {
                double t = TimeAt(d);
                if (!double.IsNaN(t))
                {
                    distance = d;
                    return t;
                }
            }

            return double.NaN;
        }
    }

    /// <summary>
    /// (Swim 2.28.0) The attempts of the swimmer of the moment, as the report after an attempt compares them (five at most, the last
    /// ones): the attempt going on from its horn to the stop of the recording, and whether the report is asked for.
    /// </summary>
    public static class SwimLiveAttempts
    {
        public const int Max = 5;

        /// <summary>The recording stops by itself this long after the time of the farthest line.</summary>
        public const double AutoStopAfter = 2.0;

        /// <summary>…and at the latest this long after the horn, when the farthest line was not seen.</summary>
        public const double AutoStopLatest = 25.0;

        /// <summary>The recording stops by itself after the farthest line (the button of the common row).</summary>
        public static bool AutoStop = true;

        /// <summary>The attempts, their colours in the report: the green, cyan, purple, white and yellow of the five swims of a session.</summary>
        public static readonly System.Drawing.Color[] Colors =
        {
            System.Drawing.Color.FromArgb(61, 235, 160), System.Drawing.Color.FromArgb(56, 225, 255), System.Drawing.Color.FromArgb(199, 146, 255),
            System.Drawing.Color.FromArgb(234, 252, 255), System.Drawing.Color.FromArgb(255, 209, 102)
        };

        /// <summary>The attempts changed, or the report is asked for or put away.</summary>
        public static event Action Changed;

        private static readonly List<SwimLiveAttempt> attempts = new List<SwimLiveAttempt>();
        private static readonly object locker = new object();
        private static int next = 1;
        private static SwimLiveAttempt current;
        private static bool report;

        /// <summary>The attempt going on (from its horn), null without one.</summary>
        public static SwimLiveAttempt Current
        {
            get { lock (locker) return current; }
        }

        /// <summary>The attempts ended, the oldest first (five at most).</summary>
        public static List<SwimLiveAttempt> List
        {
            get { lock (locker) return attempts.ToList(); }
        }

        /// <summary>The last attempt ended (null: none).</summary>
        public static SwimLiveAttempt Last
        {
            get { lock (locker) return attempts.Count > 0 ? attempts[attempts.Count - 1] : null; }
        }

        /// <summary>The report is asked for (shown over the wall of cameras).</summary>
        public static bool Report
        {
            get { lock (locker) return report && attempts.Count > 0; }
        }

        /// <summary>The colour of an attempt (by its number: it keeps it while it is shown).</summary>
        public static System.Drawing.Color ColorOf(SwimLiveAttempt attempt)
        {
            int n = attempt == null ? 0 : Math.Max(0, attempt.Number - 1);
            return Colors[n % Colors.Length];
        }

        /// <summary>The horn of a new attempt (the attempt going on ends without being kept).</summary>
        public static void Begin(DateTime horn)
        {
            lock (locker)
            {
                current = new SwimLiveAttempt { Number = next, Horn = horn };
                report = false;
            }

            Raise();
        }

        /// <summary>The recording of the attempt going on stopped: what the cameras saw is kept, and the report is asked for.</summary>
        public static SwimLiveAttempt End(IEnumerable<SwimLiveTrack> tracks)
        {
            SwimLiveAttempt ended;
            lock (locker)
            {
                ended = current;
                current = null;
                if (ended == null)
                    return null;

                ended.Tracks = tracks == null ? new List<SwimLiveTrack>() : tracks.Where(t => t != null).ToList();
                attempts.Add(ended);
                next = ended.Number + 1;
                while (attempts.Count > Max)
                    attempts.RemoveAt(0);

                report = true;
            }

            Raise();
            return ended;
        }

        /// <summary>A new swimmer: the attempts go.</summary>
        public static void Clear()
        {
            lock (locker)
            {
                attempts.Clear();
                current = null;
                next = 1;
                report = false;
            }

            Raise();
        }

        /// <summary>The report shown or put away.</summary>
        public static void ShowReport(bool show)
        {
            lock (locker)
            {
                if (report == show)
                    return;

                report = show;
            }

            Raise();
        }

        /// <summary>(For the checks and the pictures) attempts as given, the report shown.</summary>
        public static void Replace(IList<SwimLiveAttempt> list)
        {
            lock (locker)
            {
                attempts.Clear();
                if (list != null)
                    attempts.AddRange(list.Where(a => a != null));

                next = attempts.Count > 0 ? attempts.Max(a => a.Number) + 1 : 1;
                current = null;
                report = attempts.Count > 0;
            }

            Raise();
        }

        /// <summary>The best time at a distance among the attempts (NaN: none) and its attempt.</summary>
        public static double Best(double distance, out SwimLiveAttempt best)
        {
            best = null;
            double time = double.NaN;
            foreach (SwimLiveAttempt a in List)
            {
                double t = a.TimeAt(distance);
                if (!double.IsNaN(t) && (double.IsNaN(time) || t < time))
                {
                    time = t;
                    best = a;
                }
            }

            return time;
        }

        private static void Raise()
        {
            Action changed = Changed;
            if (changed != null)
                changed();
        }
    }
}
