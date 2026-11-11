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
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Kinovea.ScreenManager
{
    /// <summary>(Swim 2.21.0) The video of one camera of a recording, in the video of all its cameras.</summary>
    public sealed class SwimCompositeView
    {
        public string Camera = "";
        public string Path = "";

        /// <summary>Length of the video (s), its images per second, whether it has sound.</summary>
        public double Duration;
        public double Fps;
        public bool Sound;

        /// <summary>When the video started on the clock of the camera (from the file; MinValue when not written).</summary>
        public DateTime Created = DateTime.MinValue;

        /// <summary>Seconds from the start command to the camera recording, as measured (NaN: not measured).</summary>
        public double StartOffset = double.NaN;

        /// <summary>The second of this video at second 0 of the first video (a moment: t = t of the first + Offset).</summary>
        public double Offset;

        /// <summary>How it was lined up: "first" (the one the others are lined up with), "sound", "start", "file", "none".</summary>
        public string Way = "none";
    }

    /// <summary>
    /// (Swim 2.21.0) One video of a recording with the views of all its cameras, made when their videos are on the computer: lined up by
    /// their sound (the cameras that heard the same: the start signal, whistles, voices, splashes), else by the moments the cameras said
    /// they record (measured by the link with each camera, from the command they shared: a camera under the water rarely hears what the
    /// others hear), else by the starts written in the files (to the second). In a grid of 1920 × 1080: the cameras in the order of the
    /// recording, the details of the athlete in the free place, each view with the name of its camera and how it was lined up. The part
    /// that all the cameras recorded, each from its image nearest to the common moment; H.264 at the images per second of the fastest
    /// camera (halved, or more, above 60), the sound of the first camera that has sound; written by ffmpeg (next to the program, below
    /// the normal priority) under a temporary name and renamed when whole, beside the video of the first camera.
    /// </summary>
    public static class SwimTakeComposite
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public const int Width = 1920;
        public const int Height = 1080;
        public const int CellWidth = 960;
        public const int CellHeight = 540;

        /// <summary>At most this many cameras (the grid of four).</summary>
        public const int MaxViews = 4;

        /// <summary>At most this many images per second in the video of all the cameras (a camera at 100 or 120 gives 50 or 60).</summary>
        public const double MaxFps = 60;

        /// <summary>The camera label of the video of all the cameras in the names of the files.</summary>
        public const string FileLabel = "Ενοποιημένο";

        // The ffmpeg that make videos now and their temporary files: stopped and removed with the program.
        private static readonly object runningLocker = new object();
        private static readonly Dictionary<Process, string> running = new Dictionary<Process, string>();

        static SwimTakeComposite()
        {
            System.Windows.Forms.Application.ApplicationExit += (s, e) => StopAll();
        }

        private static readonly Regex fpsPattern = new Regex(@"(\d+(?:\.\d+)?)(k?) (?:fps|tbr)", RegexOptions.CultureInvariant);
        private static readonly Regex durationPattern = new Regex(@"Duration: (\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.CultureInvariant);

        /// <summary>ffmpeg next to the program.</summary>
        public static string Ffmpeg
        {
            get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"); }
        }

        #region Views
        /// <summary>The cameras of a recording whose video is on the computer (its first part), in the order of the recording (at most four).</summary>
        public static List<SwimCompositeView> Views(SwimTake take)
        {
            List<SwimCompositeView> views = new List<SwimCompositeView>();
            foreach (SwimTakeCamera c in take.Cameras)
            {
                string path = c.Targets != null ? c.Targets.FirstOrDefault(File.Exists) : null;
                if (path == null)
                    continue;

                views.Add(new SwimCompositeView
                {
                    Camera = c.Alias,
                    Path = path,
                    StartOffset = c.StartOffset.HasValue ? c.StartOffset.Value : double.NaN
                });

                if (views.Count == MaxViews)
                    break;
            }

            return views;
        }

        /// <summary>The video of all the cameras of the recording was made and is there.</summary>
        public static bool HasComposite(SwimTake take)
        {
            return take != null && !string.IsNullOrEmpty(take.Composite) && File.Exists(take.Composite);
        }

        /// <summary>
        /// A recording whose video of all the cameras can be made by itself: the videos of all its cameras came (at least two of them on
        /// the computer) and it was not made yet.
        /// </summary>
        public static bool Ready(SwimTake take)
        {
            return take != null && take.AllDone && take.Cameras.Count >= 2 && !HasComposite(take) && Views(take).Count >= 2;
        }

        /// <summary>
        /// Length, images per second and sound of each video (asked to ffmpeg; the length and the start also from the file of a GoPro).
        /// False when a video cannot be read.
        /// </summary>
        public static bool Probe(string ffmpeg, SwimCompositeView view, out string error)
        {
            error = null;
            SwimGoProFile file = SwimGoProFiles.Read(view.Path);
            view.Created = file.Created;
            view.Duration = file.Duration;
            string text = RunForText(ffmpeg, "-hide_banner -nostdin -i \"" + view.Path + "\"", 20000);
            if (text == null)
            {
                error = SwimLang.T("το ffmpeg δεν απάντησε");
                return false;
            }

            foreach (string line in text.Split('\n'))
            {
                if (!line.Contains("Stream #"))
                    continue;

                if (line.Contains("Video:") && view.Fps <= 0)
                {
                    Match m = fpsPattern.Match(line);
                    double fps;
                    if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out fps))
                    {
                        if (m.Groups[2].Value == "k")
                            fps *= 1000;

                        if (fps > 0 && fps < 1000)
                            view.Fps = fps;
                    }
                }
                else if (line.Contains("Audio:"))
                {
                    view.Sound = true;
                }
            }

            if (view.Duration <= 0)
            {
                Match d = durationPattern.Match(text);
                if (d.Success)
                {
                    view.Duration = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 +
                        int.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture) * 60 +
                        double.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture);
                }
            }

            if (view.Fps <= 0 || view.Duration <= 0)
            {
                error = SwimLang.T("δεν διαβάστηκε το βίντεο");
                return false;
            }

            return true;
        }
        #endregion

        #region Lining up
        /// <summary>
        /// Lines up the views (Offset: the second of each video at second 0 of the first one). The anchor is the first view with sound:
        /// the others with sound that match it clearly are lined up by it; the rest by the measured starts of the cameras (when the
        /// anchor's and theirs are known), else by the starts written in the files. The offsets are then from the first view.
        /// </summary>
        public static void LineUp(string ffmpeg, List<SwimCompositeView> views, CancellationToken token)
        {
            if (views.Count == 0)
                return;

            int anchor = views.FindIndex(v => v.Sound);
            if (anchor < 0)
                anchor = 0;

            SwimCompositeView a = views[anchor];
            a.Offset = 0;
            a.Way = "first";
            float[] anchorSound = null;
            if (a.Sound)
            {
                string error;
                anchorSound = SwimSoundSync.Half(SwimStartSound.Read(ffmpeg, a.Path, 0, Math.Min(a.Duration, SwimSoundSync.MaxSeconds), token, out error));
            }

            for (int i = 0; i < views.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (i == anchor)
                    continue;

                SwimCompositeView v = views[i];
                v.Way = "none";
                if (anchorSound != null && v.Sound)
                {
                    string error;
                    float[] sound = SwimSoundSync.Half(SwimStartSound.Read(ffmpeg, v.Path, 0, Math.Min(v.Duration, SwimSoundSync.MaxSeconds), token, out error));
                    SwimSoundMatch m = sound != null ? SwimSoundSync.Match(anchorSound, sound, Math.Min(SwimSoundSync.MaxSeconds, Math.Max(a.Duration, v.Duration))) : null;
                    if (m != null && m.Reliable)
                    {
                        v.Offset = m.LagSeconds;
                        v.Way = "sound";
                        continue;
                    }
                }

                v.Offset = ByClocks(a, v, out v.Way);
            }

            // From the first view.
            double first = views[0].Offset;
            foreach (SwimCompositeView v in views)
                v.Offset -= first;
        }

        /// <summary>
        /// (2.22.0) As LineUp, for the views of a recording: what its sound says of the measured starts of the cameras is kept with it
        /// (SwimTake.Clock); in a part of a continuous recording, a camera lined up by its measured start (its sound did not match:
        /// a part without the horn) is lined up by what the sound said in the other parts of the same recording (the median), as the
        /// starts of the cameras are those of the whole recording: «clock». The measured starts can be a tenth of a second off.
        /// </summary>
        public static void LineUp(string ffmpeg, List<SwimCompositeView> views, CancellationToken token, SwimTake take)
        {
            LineUp(ffmpeg, views, token);
            if (take == null || views.Count < 2)
                return;

            SwimCompositeView anchor = views.FirstOrDefault(v => v.Way == "first");
            if (anchor == null || double.IsNaN(anchor.StartOffset))
                return;

            // What the sound says: the offset it found less the one of the measured starts is the anchor's correction less the view's.
            Dictionary<string, double> clock = new Dictionary<string, double>();
            foreach (SwimCompositeView v in views)
            {
                if (v == anchor || v.Way != "sound" || double.IsNaN(v.StartOffset))
                    continue;

                clock[anchor.Camera] = 0;
                clock[v.Camera] = (anchor.StartOffset - v.StartOffset) - (v.Offset - anchor.Offset);
            }

            if (clock.Count > 0)
                SwimGoProTakes.SetClock(take.Id, clock);

            if (!take.IsPart)
                return;

            List<SwimTake> others = SwimGoProTakes.Parts(take.ParentId).Where(p => p.Id != take.Id && p.Clock != null).ToList();
            bool changed = false;
            foreach (SwimCompositeView v in views)
            {
                if (v == anchor || v.Way != "start")
                    continue;

                List<double> d = others.Where(p => p.Clock.ContainsKey(anchor.Camera) && p.Clock.ContainsKey(v.Camera))
                    .Select(p => p.Clock[anchor.Camera] - p.Clock[v.Camera]).OrderBy(x => x).ToList();
                if (d.Count == 0)
                    continue;

                double median = d.Count % 2 == 1 ? d[d.Count / 2] : 0.5 * (d[d.Count / 2 - 1] + d[d.Count / 2]);
                v.Offset += median;
                v.Way = "clock";
                changed = true;
            }

            if (!changed)
                return;

            double first = views[0].Offset;
            foreach (SwimCompositeView v in views)
                v.Offset -= first;
        }

        /// <summary>
        /// The offset of a view from the anchor by the measured starts of the cameras (from the same command: the one that started later
        /// is behind by the difference), else by the starts written in the files (to the second), else 0.
        /// </summary>
        public static double ByClocks(SwimCompositeView anchor, SwimCompositeView view, out string way)
        {
            if (!double.IsNaN(anchor.StartOffset) && !double.IsNaN(view.StartOffset))
            {
                way = "start";
                return anchor.StartOffset - view.StartOffset;
            }

            if (anchor.Created != DateTime.MinValue && view.Created != DateTime.MinValue && Math.Abs((anchor.Created - view.Created).TotalSeconds) < 60)
            {
                way = "file";
                return (anchor.Created - view.Created).TotalSeconds;
            }

            way = "none";
            return 0;
        }

        /// <summary>The part that all the videos have, on the clock of the first one (seconds); false when they have less than a second together.</summary>
        public static bool Common(List<SwimCompositeView> views, out double from, out double to)
        {
            from = 0;
            to = double.MaxValue;
            foreach (SwimCompositeView v in views)
            {
                from = Math.Max(from, -v.Offset);
                to = Math.Min(to, v.Duration - v.Offset);
            }

            return to - from >= 1;
        }

        /// <summary>The images per second of the video of all the cameras: of the fastest camera, divided by 2, 3… above MaxFps.</summary>
        public static double OutputFps(List<SwimCompositeView> views)
        {
            double fps = views.Count > 0 ? views.Max(v => v.Fps) : 30;
            if (fps <= 0)
                fps = 30;

            return fps / Math.Ceiling(fps / MaxFps - 1e-9);
        }
        #endregion

        /// <summary>
        /// Where the video of all the cameras of a recording goes: the one made before for it, else beside the video of its first camera,
        /// named as its videos with the camera «Ενοποιημένο» (free of the files already there). Null without a video.
        /// </summary>
        public static string OutputPath(SwimTake take)
        {
            if (!string.IsNullOrEmpty(take.Composite))
                return take.Composite;

            SwimCompositeView first = Views(take).FirstOrDefault();
            if (first == null)
                return null;

            string folder = System.IO.Path.GetDirectoryName(first.Path);
            string root = SwimGoProTakes.Root.Length > 0 ? SwimGoProTakes.Root : folder;
            string name = System.IO.Path.GetFileName(SwimGoProTakes.TargetPath(root, take, FileLabel, 1));
            return SwimGoProTakes.FreePath(System.IO.Path.Combine(folder, name), null);
        }

        #region Making
        /// <summary>
        /// The video of all the cameras of a recording (at least two with their video on the computer) to `output`, lined up by their sound,
        /// the starts of the cameras or of the files. Null when made, else what went wrong (in the language of the interface). progress: the
        /// part done (0 – 1).
        /// </summary>
        public static string Make(string ffmpeg, SwimTake take, string output, Action<double> progress, CancellationToken token, out List<SwimCompositeView> views)
        {
            views = Views(take);
            if (views.Count < 2)
                return SwimLang.T("χρειάζονται τα βίντεο τουλάχιστον δύο καμερών");

            if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg))
                return SwimLang.T("λείπει το ffmpeg του προγράμματος");

            foreach (SwimCompositeView v in views)
            {
                string error;
                if (!Probe(ffmpeg, v, out error))
                    return v.Camera + ": " + error;
            }

            if (progress != null)
                progress(0.02);

            LineUp(ffmpeg, views, token, take);
            return MakeFromViews(ffmpeg, take, views, output, progress, token);
        }

        /// <summary>The video of all the cameras from views read and lined up (Offset from the first view).</summary>
        public static string MakeFromViews(string ffmpeg, SwimTake take, List<SwimCompositeView> views, string output, Action<double> progress, CancellationToken token)
        {
            if (views.Count < 2)
                return SwimLang.T("χρειάζονται τα βίντεο τουλάχιστον δύο καμερών");

            if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg))
                return SwimLang.T("λείπει το ffmpeg του προγράμματος");

            double from, to;
            if (!Common(views, out from, out to))
                return SwimLang.T("τα βίντεο των καμερών δεν έχουν κοινό κομμάτι (τουλάχιστον 1 s)");

            string folder = System.IO.Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            string overlay = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kinovea_swim_composite_" + Guid.NewGuid().ToString("N") + ".png");
            string partial = output + ".part";
            try
            {
                using (Bitmap bmp = DrawOverlay(take, views))
                    bmp.Save(overlay, ImageFormat.Png);

                double fps = OutputFps(views);
                string arguments = Arguments(views, from, to - from, fps, overlay, partial);
                log.DebugFormat("Video of all the cameras: {0}", string.Join(", ", views.Select(v => string.Format(CultureInfo.InvariantCulture, "{0} {1:0.000} s ({2})", v.Camera, v.Offset, v.Way))));
                string error = Run(ffmpeg, arguments, partial, to - from, progress, token);
                if (error != null)
                {
                    TryDelete(partial);
                    return error;
                }

                if (!File.Exists(partial) || new FileInfo(partial).Length == 0)
                {
                    TryDelete(partial);
                    return SwimLang.T("το ffmpeg δεν έγραψε το βίντεο");
                }

                TryDelete(output);
                File.Move(partial, output);
                return null;
            }
            catch (OperationCanceledException)
            {
                TryDelete(partial);
                throw;
            }
            finally
            {
                TryDelete(overlay);
            }
        }

        /// <summary>The places of the views in the grid: 0 top left, 1 top right, 2 bottom left, 3 bottom right; the details at the free place.</summary>
        public static int[] Cells(int count)
        {
            switch (count)
            {
                case 1: return new[] { 0 };
                case 2: return new[] { 0, 2 };
                case 3: return new[] { 0, 2, 3 };
                default: return new[] { 0, 1, 2, 3 };
            }
        }

        /// <summary>
        /// The arguments of ffmpeg: each video from its image nearest to the start of the common part (read from half an image before it,
        /// its clock from 0), in its place of the grid, the drawing over all. The seek is on the clock of the streams, as the sound that lined
        /// them up (-seek_timestamp: else ffmpeg adds the start of the file to it, before 0 when the sound starts before the picture, and
        /// keeps an image too early).
        /// </summary>
        public static string Arguments(List<SwimCompositeView> views, double from, double length, double fps, string overlay, string output)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder a = new StringBuilder("-hide_banner -nostdin -nostats -loglevel error -y -progress pipe:1 ");
            foreach (SwimCompositeView v in views)
            {
                double wanted = Math.Max(0, from + v.Offset);

                // (2.22.0) Not held at 0: the first image of a video can be a little before 0 on the clock of its streams (the videos of
                // the athletes of a continuous recording, cut at a key image, start 0.5 ms before it) and a seek at 0 left it out: that
                // camera one image ahead of the others.
                double seek = wanted - 0.5 / v.Fps;
                a.AppendFormat(ci, "-seek_timestamp 1 -ss {0:0.0000} -t {1:0.000} -i \"{2}\" ", seek, length + 1 / v.Fps, v.Path);
            }

            a.AppendFormat(ci, "-loop 1 -t {0:0.000} -i \"{1}\" ", length, overlay);

            int[] cells = Cells(views.Count);
            StringBuilder f = new StringBuilder();
            f.AppendFormat(ci, "color=c=black:s={0}x{1}:r={2:0.###}:d={3:0.000}[bg];", Width, Height, fps, length);
            for (int i = 0; i < views.Count; i++)
            {
                f.AppendFormat(ci, "[{0}:v]setpts=PTS-STARTPTS,scale={1}:{2}:force_original_aspect_ratio=decrease,pad={1}:{2}:(ow-iw)/2:(oh-ih)/2,setsar=1[c{0}];",
                    i, CellWidth, CellHeight);
            }

            string last = "bg";
            for (int i = 0; i < views.Count; i++)
            {
                int x = (cells[i] % 2) * CellWidth, y = (cells[i] / 2) * CellHeight;
                f.AppendFormat(ci, "[{0}][c{1}]overlay={2}:{3}:shortest=1[o{1}];", last, i, x, y);
                last = "o" + i.ToString(ci);
            }

            f.AppendFormat(ci, "[{0}][{1}:v]overlay=0:0:shortest=1[out]", last, views.Count);
            a.Append("-filter_complex \"" + f + "\" -map \"[out]\" ");

            // The sound of the first camera that has sound.
            int sound = views.FindIndex(v => v.Sound);
            if (sound >= 0)
                a.AppendFormat(ci, "-map {0}:a:0 -c:a aac -strict -2 -b:a 128k -ac 2 ", sound);

            a.AppendFormat(ci, "-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -r {0:0.###} -g {1} ", fps, Math.Max(1, (int)Math.Round(fps)));
            a.Append("-movflags +faststart -f mp4 \"" + output + "\"");
            return a.ToString();
        }

        /// <summary>The videos being made stop now and their temporary files go (the program closes).</summary>
        public static void StopAll()
        {
            List<KeyValuePair<Process, string>> list;
            lock (runningLocker)
                list = running.ToList();

            foreach (KeyValuePair<Process, string> r in list)
            {
                try
                {
                    if (!r.Key.HasExited)
                    {
                        r.Key.Kill();
                        r.Key.WaitForExit(3000);
                    }
                }
                catch (Exception) { }

                TryDelete(r.Value);
            }
        }

        /// <summary>
        /// ffmpeg with its progress (out_time_ms of -progress) and stop; null when it went, else its last error lines. `partial`: the
        /// temporary file it writes (removed if the program closes meanwhile). Also for the cuts of the continuous recordings (2.22.0).
        /// </summary>
        internal static string Run(string ffmpeg, string arguments, string partial, double length, Action<double> progress, CancellationToken token)
        {
            ProcessStartInfo info = new ProcessStartInfo(ffmpeg, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            };

            StringBuilder errors = new StringBuilder();
            using (Process p = new Process { StartInfo = info })
            {
                p.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (errors)
                            errors.AppendLine(e.Data);
                    }
                };
                p.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null || progress == null || length <= 0 || !e.Data.StartsWith("out_time_ms=", StringComparison.Ordinal))
                        return;

                    long microseconds;
                    if (long.TryParse(e.Data.Substring(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out microseconds))
                        progress(Math.Max(0.05, Math.Min(0.99, microseconds / 1e6 / length)));
                };

                p.Start();
                lock (runningLocker)
                    running[p] = partial;

                try
                {
                    try
                    {
                        // The program stays quick while the video is made.
                        p.PriorityClass = ProcessPriorityClass.BelowNormal;
                    }
                    catch (Exception) { }

                    p.BeginErrorReadLine();
                    p.BeginOutputReadLine();
                    while (!p.WaitForExit(200))
                    {
                        if (token.IsCancellationRequested)
                        {
                            try { p.Kill(); } catch (Exception) { }
                            p.WaitForExit(5000);
                            token.ThrowIfCancellationRequested();
                        }
                    }

                    p.WaitForExit();
                }
                finally
                {
                    lock (runningLocker)
                        running.Remove(p);
                }

                if (p.ExitCode == 0)
                    return null;

                string text;
                lock (errors)
                    text = errors.ToString();

                string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
                log.ErrorFormat("ffmpeg failed for the video of all the cameras ({0}): {1}", p.ExitCode, text);
                return "ffmpeg: " + string.Join(" ", lines.Skip(Math.Max(0, lines.Length - 2)));
            }
        }

        internal static string RunForText(string ffmpeg, string arguments, int timeoutMs)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(ffmpeg, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process p = Process.Start(info))
                {
                    Task<string> err = p.StandardError.ReadToEndAsync();
                    Task<string> outText = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        return null;
                    }

                    return err.Result + outText.Result;
                }
            }
            catch (Exception e)
            {
                log.Error("ffmpeg could not be asked about a video.", e);
                return null;
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

        #region Drawing
        /// <summary>
        /// The drawing over the grid (transparent elsewhere): the name of each camera and how it was lined up, the details of the
        /// recording in the free place (or a band at the bottom with four cameras), thin lines between the places.
        /// </summary>
        public static Bitmap DrawOverlay(SwimTake take, List<SwimCompositeView> views)
        {
            Bitmap bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font label = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font small = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush text = new SolidBrush(Color.White))
            using (SolidBrush muted = new SolidBrush(Color.FromArgb(200, 210, 220)))
            using (SolidBrush back = new SolidBrush(Color.FromArgb(170, 10, 12, 16)))
            using (Pen line = new Pen(Color.FromArgb(255, 24, 26, 30), 4))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                int[] cells = Cells(views.Count);
                for (int i = 0; i < views.Count; i++)
                {
                    int x = (cells[i] % 2) * CellWidth, y = (cells[i] / 2) * CellHeight;
                    string name = views[i].Camera;
                    string way = WayText(views[i].Way);
                    SizeF size = g.MeasureString(name, label);
                    SizeF wsize = way.Length > 0 ? g.MeasureString(way, small) : SizeF.Empty;
                    float w = Math.Max(size.Width, wsize.Width) + 16;
                    float h = size.Height + (way.Length > 0 ? wsize.Height : 0) + 8;
                    FillRounded(g, back, x + 10, y + 10, w, h, 6);
                    g.DrawString(name, label, text, x + 18, y + 13);
                    if (way.Length > 0)
                        g.DrawString(way, small, muted, x + 18, y + 13 + size.Height - 2);
                }

                // The lines between the places.
                g.DrawLine(line, CellWidth, 0, CellWidth, Height);
                g.DrawLine(line, 0, CellHeight, Width, CellHeight);

                // The details of the recording.
                if (views.Count < 4)
                    DrawDetails(g, take, new RectangleF(CellWidth, 0, CellWidth, CellHeight), false);
                else
                    DrawDetails(g, take, new RectangleF(0, Height - 64, Width, 64), true);
            }

            return bmp;
        }

        private static string WayText(string way)
        {
            switch (way)
            {
                case "sound": return SwimLang.T("συγχρονισμός: ήχος");
                case "start": return SwimLang.T("συγχρονισμός: έναρξη κάμερας");
                case "clock": return SwimLang.T("συγχρονισμός: ήχος της εγγραφής");
                case "file": return SwimLang.T("συγχρονισμός: ώρα αρχείου (±1 s)");
                case "none": return SwimLang.T("χωρίς συγχρονισμό");
                default: return "";
            }
        }

        private static void DrawDetails(Graphics g, SwimTake take, RectangleF area, bool band)
        {
            SwimTakeDetails d = take.Details ?? new SwimTakeDetails();
            using (SolidBrush fill = new SolidBrush(band ? Color.FromArgb(200, 14, 16, 20) : Color.FromArgb(255, 20, 22, 26)))
                g.FillRectangle(fill, area);

            string who = d.SwimmerName;
            if (string.IsNullOrWhiteSpace(who))
                who = SwimLang.T("χωρίς αθλητή");

            string id = (d.AthleteId ?? "").Trim();
            string title = (id.Length > 0 ? id + "  " : "") + who;
            string club = d.SwimmerClub;
            string what = d.Kind == SwimTakeDetails.KindRace ?
                string.Join(" · ", new[] { d.Distance.ToString(CultureInfo.InvariantCulture) + " " + SwimPresets.StyleName(d.Style), d.Round ?? "",
                    d.Heat > 0 ? SwimLang.T("Σειρά") + " " + d.Heat.ToString(CultureInfo.InvariantCulture) : "",
                    d.Lane > 0 ? SwimLang.T("Διάδρομος") + " " + d.Lane.ToString(CultureInfo.InvariantCulture) : "" }.Where(s => !string.IsNullOrWhiteSpace(s))) :
                d.KindName;
            string when = string.Join(" · ", new[] { take.Start.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture), d.Location ?? "", d.Competition ?? "" }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            using (SolidBrush text = new SolidBrush(Color.White))
            using (SolidBrush accent = new SolidBrush(Color.FromArgb(120, 200, 255)))
            using (SolidBrush muted = new SolidBrush(Color.FromArgb(170, 180, 190)))
            using (StringFormat clip = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                if (band)
                {
                    using (Font f = new Font("Segoe UI", 24f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Font s = new Font("Segoe UI", 18f, FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        g.DrawString(title, f, text, new RectangleF(area.X + 20, area.Y + 14, area.Width * 0.45f, 36), clip);
                        g.DrawString(what + "   " + when, s, muted, new RectangleF(area.X + area.Width * 0.47f, area.Y + 20, area.Width * 0.52f, 30), clip);
                    }

                    return;
                }

                float x = area.X + 48, y = area.Y + 70, w = area.Width - 96;
                using (Font kindFont = new Font("Segoe UI", 26f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font titleFont = new Font("Segoe UI", 44f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font clubFont = new Font("Segoe UI", 30f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (Font whenFont = new Font("Segoe UI", 24f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (Font signFont = new Font("Segoe UI", 18f, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    g.DrawString(what, kindFont, accent, new RectangleF(x, y, w, 40), clip);
                    y += 56;
                    g.DrawString(title, titleFont, text, new RectangleF(x, y, w, 64), clip);
                    y += 72;
                    if (!string.IsNullOrWhiteSpace(club))
                    {
                        g.DrawString(club, clubFont, text, new RectangleF(x, y, w, 44), clip);
                        y += 56;
                    }

                    g.DrawString(when, whenFont, muted, new RectangleF(x, y, w, 36), clip);
                    g.DrawString("Kinovea Swim " + SwimHudPainter.AppVersion, signFont, muted, new RectangleF(x, area.Bottom - 56, w, 28), clip);
                }
            }
        }

        private static void FillRounded(Graphics g, Brush brush, float x, float y, float w, float h, float r)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(x, y, 2 * r, 2 * r, 180, 90);
                path.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
                path.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
                path.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }
        #endregion
    }
}
