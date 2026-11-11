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
using System.Threading;

namespace Kinovea.ScreenManager
{
    /// <summary>(Swim 2.22.0) A piece of a file of a camera for a part: from second From to second To of that file.</summary>
    public class SwimCutPiece
    {
        public SwimGoProFile File;
        public double From;
        public double To;
    }

    /// <summary>(Swim 2.22.0) The video of a camera for a part of a continuous recording (an athlete): its pieces and its place.</summary>
    public class SwimCutJob
    {
        public SwimTake Recording;
        public SwimTake Part;
        public string Alias = "";
        public string Target = "";
        public List<SwimCutPiece> Pieces = new List<SwimCutPiece>();

        /// <summary>Seconds from the command of the recording to the first image of the part's video (its key image).</summary>
        public double StartFromCommand;

        public double Length
        {
            get { return Pieces.Sum(p => Math.Max(0, p.To - p.From)); }
        }
    }

    /// <summary>
    /// (Swim 2.22.0) The videos of a continuous recording (the athletes of an order one after the other, the button «⏭ Επόμενος» at the end
    /// of the attempt of each) cut for each athlete: from each camera's files (a long recording is in several files, one after the other),
    /// each part from the press before it to its press, a second more on each side; without coding again (the same images, quickly), so
    /// from the key image at or before its start. The start of each part's video is kept on the clock of the recording's command: the videos
    /// of the cameras of a part are lined up by it when their sounds do not match (under the water). Each part's video goes to the place of
    /// the athlete's videos (his folder and name), under a temporary name until whole; the files of the card are never changed.
    /// </summary>
    public static class SwimGoProCut
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Seconds kept before and after each part (the press is not exactly at the end of an attempt).</summary>
        public const double Margin = 1.0;

        /// <summary>A part shorter than this in a camera's video has no video of that camera (it had stopped).</summary>
        public const double MinLength = 0.5;

        #region Plan
        /// <summary>
        /// The videos of the parts of a continuous recording from the files of one of its cameras (the parts whose video of this camera did
        /// not come yet); a part this camera has no video of gets a job without pieces (it comes without a file). `taken`: the paths given
        /// already in this copy.
        /// </summary>
        public static List<SwimCutJob> Plan(string root, SwimGoProMatch m, List<SwimTake> parts, HashSet<string> taken)
        {
            List<SwimCutJob> jobs = new List<SwimCutJob>();
            SwimTake recording = m.Take;
            string alias = m.Camera.Alias;
            List<SwimGoProFile> files = m.Files;
            if (files.Count == 0)
                return jobs;

            // The first image of the camera's video after the command: as measured by the camera's link, else from the start written in the
            // file (the clock of the camera is the one of the computer since the camera took it, to the second).
            double start;
            if (m.Camera.StartOffset.HasValue)
            {
                start = m.Camera.StartOffset.Value;
            }
            else
            {
                double written = files[0].Created == DateTime.MinValue ? double.NaN : (files[0].Created.AddHours(m.Hours) - recording.Start).TotalSeconds;
                start = !double.IsNaN(written) && Math.Abs(written) < 60 ? Math.Max(0, written) : 1.0;
            }

            // The files one after the other by their pictures: where each starts in the camera's video, its key images.
            double[] offsets = new double[files.Count];
            double[] lengths = new double[files.Count];
            Dictionary<string, double[]> keys = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            double total = 0;
            for (int i = 0; i < files.Count; i++)
            {
                double video;
                keys[files[i].Path] = SwimGoProFiles.KeyframeTimes(files[i].Path, out video);
                lengths[i] = video > 0 ? video : Math.Max(0, files[i].Duration);
                offsets[i] = total;
                total += lengths[i];
            }

            if (total <= 0)
                return jobs;

            foreach (SwimTake part in parts.OrderBy(p => p.Index))
            {
                SwimTakeCamera camera = part.Cameras.FirstOrDefault(c => c.Alias == alias);
                if (camera == null || camera.Done)
                    continue;

                double from = part.Index <= 1 ? 0 : (part.Start - recording.Start).TotalSeconds - start - Margin;
                double to = part.EndedByButton && part.Stop != DateTime.MinValue ? (part.Stop - recording.Start).TotalSeconds - start + Margin : total;
                from = Math.Max(0, from);
                to = Math.Min(total, to);

                SwimCutJob job = new SwimCutJob { Recording = recording, Part = part, Alias = alias };
                if (to - from >= MinLength)
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        double a = Math.Max(from, offsets[i]);
                        double b = Math.Min(to, offsets[i] + lengths[i]);
                        if (b - a > 0.01)
                            job.Pieces.Add(new SwimCutPiece { File = files[i], From = a - offsets[i], To = b - offsets[i] });
                    }
                }

                if (job.Pieces.Count > 0)
                {
                    // A cut without coding starts at a key image: the one at or before the start of the part.
                    SwimCutPiece first = job.Pieces[0];
                    first.From = KeyBefore(keys[first.File.Path], first.From);
                    job.StartFromCommand = start + offsets[files.IndexOf(first.File)] + first.From;
                    job.Target = SwimGoProTakes.FreePath(SwimGoProTakes.TargetPath(root, part, alias, 1), taken);
                }

                jobs.Add(job);
            }

            return jobs;
        }

        /// <summary>The key image at or before a second (the second itself when every image is a key one or the images were not read).</summary>
        public static double KeyBefore(double[] keys, double seconds)
        {
            if (keys == null || keys.Length == 0)
                return seconds;

            double best = keys[0];
            foreach (double k in keys)
            {
                if (k <= seconds + 1e-6)
                    best = k;
                else
                    break;
            }

            return Math.Max(0, best);
        }
        #endregion

        #region Cut
        /// <summary>
        /// The videos of the parts cut (each to a temporary name, renamed when whole): each part gets its video and the start of it. Returns
        /// the number of videos cut; `error` says the first problem. progress: the name of the video, the part done of all (0 – 1).
        /// </summary>
        public static int Run(string ffmpeg, List<SwimCutJob> jobs, Action<string, double> progress, CancellationToken token, out string error)
        {
            error = null;
            int cut = 0;
            double total = Math.Max(1e-6, jobs.Sum(j => j.Length));
            double done = 0;
            List<KeyValuePair<string, string>> lines = new List<KeyValuePair<string, string>>();
            Dictionary<string, bool> sounds = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (SwimCutJob job in jobs)
                {
                    token.ThrowIfCancellationRequested();
                    if (job.Pieces.Count == 0)
                    {
                        // This camera had stopped: the part has no video of it.
                        SwimGoProTakes.MarkDone(job.Part.Id, job.Alias, "", new List<string>());
                        continue;
                    }

                    if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg))
                    {
                        error = error ?? SwimLang.T("λείπει το ffmpeg του προγράμματος");
                        break;
                    }

                    string name = Path.GetFileName(job.Target);
                    string partial = job.Target + ".part";
                    List<string> temps = new List<string>();
                    double before = done;
                    double length = job.Length;
                    Action<double> report = f =>
                    {
                        if (progress != null)
                            progress(name, Math.Min(1, (before + Math.Max(0, Math.Min(1, f)) * length) / total));
                    };

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(job.Target));
                        string source = job.Pieces[0].File.Path;
                        bool sound;
                        if (!sounds.TryGetValue(source, out sound))
                        {
                            sound = HasSound(ffmpeg, source);
                            sounds[source] = sound;
                        }

                        string problem;
                        if (job.Pieces.Count == 1)
                        {
                            problem = CutPiece(ffmpeg, job.Pieces[0], sound, partial, report, token);
                        }
                        else
                        {
                            // A part over two files of the camera: each piece, then one after the other.
                            problem = null;
                            double share = 0;
                            for (int i = 0; i < job.Pieces.Count && problem == null; i++)
                            {
                                string temp = job.Target + ".piece" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".part";
                                temps.Add(temp);
                                double weight = (job.Pieces[i].To - job.Pieces[i].From) / Math.Max(1e-6, length);
                                double offset = share;
                                problem = CutPiece(ffmpeg, job.Pieces[i], sound, temp, f => report(offset + weight * f), token);
                                share += weight;
                            }

                            if (problem == null)
                                problem = Join(ffmpeg, temps, sound, partial, token);
                        }

                        if (problem == null && (!File.Exists(partial) || new FileInfo(partial).Length == 0))
                            problem = SwimLang.T("το ffmpeg δεν έγραψε το βίντεο");

                        if (problem != null)
                        {
                            TryDelete(partial);
                            error = error ?? (name + ": " + problem);
                            log.ErrorFormat("A part of a continuous recording was not cut: {0}", problem);
                        }
                        else
                        {
                            string target = File.Exists(job.Target) ? SwimGoProTakes.FreePath(job.Target, null) : job.Target;
                            File.Move(partial, target);
                            SwimGoProTakes.MarkDone(job.Part.Id, job.Alias, SourceText(job), new List<string> { target });
                            SwimGoProTakes.SetPartStart(job.Part.Id, job.Alias, job.StartFromCommand);
                            lines.Add(new KeyValuePair<string, string>(SourceText(job), target));
                            cut++;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        TryDelete(partial);
                        throw;
                    }
                    catch (Exception e)
                    {
                        TryDelete(partial);
                        log.Error("A part of a continuous recording was not cut.", e);
                        error = error ?? (name + ": " + e.Message);
                    }
                    finally
                    {
                        foreach (string temp in temps)
                            TryDelete(temp);
                    }

                    done += length;
                    report(1);
                }
            }
            finally
            {
                SwimGoProFiles.LogCuts(lines);
            }

            return cut;
        }

        /// <summary>
        /// A camera of a continuous recording whose parts all came: its files came (the recording is done when all its cameras are). The
        /// files of the card are named in it.
        /// </summary>
        public static void Finish(SwimGoProMatch m)
        {
            List<SwimTake> parts = SwimGoProTakes.Parts(m.Take.Id);
            string alias = m.Camera.Alias;
            if (parts.Count == 0 || !parts.All(p => p.Cameras.All(c => c.Alias != alias || c.Done)))
                return;

            List<string> targets = parts.SelectMany(p => p.Cameras.Where(c => c.Alias == alias).SelectMany(c => c.Targets ?? new List<string>())).ToList();
            SwimGoProTakes.MarkDone(m.Take.Id, alias, string.Join(", ", m.Files.Select(f => f.Name)), targets);
        }

        /// <summary>What a part's video was cut from: "GX010123.MP4 12.0 – 45.3 s" (and the next pieces).</summary>
        private static string SourceText(SwimCutJob job)
        {
            return string.Join(" + ", job.Pieces.Select(p => string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0} – {2:0.0} s", p.File.Name, p.From, p.To)));
        }

        /// <summary>A piece of a file, without coding: from its key image, the picture and the sound as they are.</summary>
        private static string CutPiece(string ffmpeg, SwimCutPiece piece, bool sound, string output, Action<double> progress, CancellationToken token)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            double length = Math.Max(0.1, piece.To - piece.From);

            // A little after the key image, so that the seek does not take the one before it; on the clock of the streams (else ffmpeg adds
            // the start of the file to it, before 0 when the sound starts before the picture).
            string arguments = string.Format(ci,
                "-hide_banner -nostdin -nostats -loglevel error -y -progress pipe:1 -seek_timestamp 1 -ss {0:0.0000} -i \"{1}\" -t {2:0.000} -map 0:v:0 {3}-c copy -f mp4 \"{4}\"",
                piece.From + 0.0005, piece.File.Path, length, sound ? "-map 0:a:0 " : "", output);
            return SwimTakeComposite.Run(ffmpeg, arguments, output, length, progress, token);
        }

        /// <summary>The pieces one after the other in one video (the concat of ffmpeg, without coding).</summary>
        private static string Join(string ffmpeg, List<string> pieces, bool sound, string output, CancellationToken token)
        {
            string list = output + ".txt";
            try
            {
                StringBuilder b = new StringBuilder();
                foreach (string p in pieces)
                    b.Append("file '").Append(p.Replace("'", "'\\''")).Append("'\n");

                File.WriteAllText(list, b.ToString(), new UTF8Encoding(false));
                string arguments = "-hide_banner -nostdin -nostats -loglevel error -y -progress pipe:1 -f concat -safe 0 -i \"" + list + "\" -map 0:v:0 " +
                    (sound ? "-map 0:a:0 " : "") + "-c copy -f mp4 \"" + output + "\"";
                return SwimTakeComposite.Run(ffmpeg, arguments, output, 0, null, token);
            }
            finally
            {
                TryDelete(list);
            }
        }

        /// <summary>The file has sound (ffmpeg says an audio stream).</summary>
        private static bool HasSound(string ffmpeg, string path)
        {
            string text = SwimTakeComposite.RunForText(ffmpeg, "-hide_banner -nostdin -i \"" + path + "\"", 20000);
            return text != null && text.Split('\n').Any(l => l.Contains("Stream #") && l.Contains("Audio:"));
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
    }
}
