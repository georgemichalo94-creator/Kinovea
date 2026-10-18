#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kinovea.Services;
using Kinovea.Video;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Automatic analysis of a race video (camera in the stands following the swimmer): the start signal from the start lights and
    /// the signal heard, proposed for the check of the analyst (Enter right, arrows and Enter corrected, Delete wrong) and learned
    /// from the answers.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Start light
        /// <summary>Proposal of the start signal found from the start lights.</summary>
        public const string KindRaceStart = "race.start";

        /// <summary>At most this much video is read from its beginning for the start (s).</summary>
        private const double StartLightMaxSeconds = 150;

        /// <summary>Width of the frames given to the start light detector (pixels).</summary>
        private const int StartLightWorkWidth = 960;

        public bool StartLightRunning { get; private set; }

        /// <summary>What the reading of the start found (background thread).</summary>
        private sealed class StartFound
        {
            /// <summary>Read to the end (not cancelled).</summary>
            public bool Completed;

            public List<SwimStartCandidate> Candidates = new List<SwimStartCandidate>();

            /// <summary>The start signals heard (empty: no sound, or none heard).</summary>
            public List<SwimBeep> Beeps = new List<SwimBeep>();

            /// <summary>Why the sound could not be read (null: read, or not asked).</summary>
            public string SoundError;
        }

        /// <summary>
        /// Reads the video from its beginning until the start (the start lights flash, the signal sounds, then the camera follows the
        /// swimmers), proposes the frame of the light (the light of the start seen just before the signal heard; without light, the
        /// signal less its usual delay) and starts the check. A start already marked near it only teaches the learning.
        /// </summary>
        public void FindStartLight()
        {
            RefreshTiming();
            startLightCancelled = false;
            StopPilotCountdown();
            if (Race == null || !HasVideo || StartLightRunning)
                return;

            if (Race.IsTraining)
            {
                Status("Η αυτόματη εκκίνηση από το φως είναι για βίντεο αγώνα (κάμερα στην κερκίδα).", true);
                return;
            }

            if (!Timing.IsValid)
            {
                Status("Δεν είναι γνωστός ο ρυθμός καρέ του βίντεο: η εκκίνηση δεν μπορεί να βρεθεί.", true);
                return;
            }

            long from = frameServer.VideoReader.WorkingZone.Start;
            long end = Math.Min(frameServer.VideoReader.WorkingZone.End, from + RealSpan(StartLightMaxSeconds));
            if (end <= from)
                return;

            double hsf = Timing.HighSpeedFactor <= 0 ? 1.0 : Timing.HighSpeedFactor;
            double realFps = 1000.0 * hsf / Timing.FrameIntervalMilliseconds;
            Size reference = frameServer.VideoReader.Info.ReferenceSize;
            int width = Math.Min(StartLightWorkWidth, Math.Max(64, reference.Width));
            int height = Math.Max(2, (int)Math.Round(width * (double)reference.Height / Math.Max(1, reference.Width)));
            SwimStartLight detector = new SwimStartLight(width, height, realFps);
            List<long> stamps = new List<long>();

            // The sound of the video (the start signal heard), read by ffmpeg next to the program while the frames are read. Not for
            // the videos slowed down (high speed): their sound is not in real time.
            double tsPerSecond = frameServer.VideoReader.Info.AverageTimeStampsPerSeconds;
            string ffmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            string path = videoPath;
            bool sound = Math.Abs(hsf - 1) < 0.01 && tsPerSecond > 0 && !string.IsNullOrEmpty(path) && File.Exists(path) && File.Exists(ffmpeg);
            double soundFrom = from / Math.Max(1e-9, tsPerSecond);
            double soundSeconds = (end - from) / Math.Max(1e-9, tsPerSecond) + 1;

            StartFound result = null;
            Exception error = null;
            bool cancelled = false;
            StartLightRunning = true;
            RefreshProposalsUi();
            player.BeforeExportVideo();
            using (BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true })
            using (FormProgressBar progress = new FormProgressBar(true))
            {
                progress.CancelAsked += (o, e) => worker.CancelAsync();
                worker.ProgressChanged += (o, e) => progress.Update(e.ProgressPercentage, 100, true);
                worker.DoWork += (o, e) =>
                {
                    e.Result = ReadStart(detector, stamps, from, end, width, height, (BackgroundWorker)o, sound ? ffmpeg : null, path, soundFrom,
                        soundSeconds, tsPerSecond);
                };
                worker.RunWorkerCompleted += (o, e) =>
                {
                    error = e.Error;
                    result = e.Error == null ? e.Result as StartFound : null;
                    cancelled = e.Error == null && (result == null || !result.Completed);
                    progress.Close();
                };

                worker.RunWorkerAsync();
                progress.ShowDialog();
            }

            StartLightRunning = false;
            frameServer.VideoReader.AfterFrameEnumeration();
            if (player != null && !player.IsDisposed)
                player.AfterExportVideo();

            try
            {
                if (error != null)
                {
                    log.Error("Start light reading failed.", error);
                    startLightCancelled = true;
                    Status("Η αυτόματη εκκίνηση απέτυχε: " + error.Message, true);
                    return;
                }

                if (cancelled)
                {
                    startLightCancelled = true;
                    Status("Η αναζήτηση της εκκίνησης ακυρώθηκε.", false);
                    return;
                }

                if (result.SoundError != null)
                    log.WarnFormat("Start signal: the sound was not read ({0}).", result.SoundError);

                List<SwimStartCandidate> found = result.Candidates;
                if (found.Count == 0 || stamps.Count == 0)
                {
                    Status("Δεν βρέθηκε σήμα εκκίνησης πριν από την κολύμβηση (δεν φαίνεται το φως, δεν ακούγεται το σήμα ή η κάμερα δεν ακολουθεί " +
                        "τον αθλητή). Βάλε την εκκίνηση με το κουμπί «Εκκίνηση» στο καρέ του σήματος.", true);
                    return;
                }

                ProposeStart(PlausibleStarts(found, stamps), stamps, realFps);
            }
            finally
            {
                RefreshProposalsUi();
                InvalidatePlayer();
            }
        }

        /// <summary>
        /// The shortest the race can last (s): 0.97 × the world record of freestyle (the fastest stroke) for the distance in this pool
        /// (long or short course), so that a start leaving less video is surely not the start of this race (the signal of the next race,
        /// the flashes of cameras during the race: a 400 IM of 254 s with a "start" 170 s before the end); relays, yards and other
        /// distances: 0.42 s/m (at least 15 s).
        /// </summary>
        internal static double MinRaceSeconds(int distance, int pool = 50, bool relay = false, bool yards = false)
        {
            int i = Array.IndexOf(new[] { 50, 100, 200, 400, 800, 1500 }, distance);
            if (relay || yards || i < 0)
                return Math.Max(15.0, 0.42 * Math.Max(0, distance));

            double[] records = pool == 25 ? new[] { 19.90, 44.84, 99.37, 212.25, 440.46, 846.88 } : new[] { 20.91, 46.40, 102.00, 219.96, 452.12, 870.67 };
            return 0.97 * records[i];
        }

        /// <summary>The shortest the race of the analysis can last (s), see MinRaceSeconds(distance, pool, relay, yards).</summary>
        private double MinRaceSeconds()
        {
            return Race == null ? 15.0 : MinRaceSeconds(Race.Distance, Race.Pool, Race.Relay, Race.IsYards);
        }

        /// <summary>
        /// The starts after which the video is too short for the race (the signal of the next race near the end, sounds of the stands
        /// in the race) after the others, which keep their order; as found when all are so, or the distance is not known.
        /// </summary>
        private List<SwimStartCandidate> PlausibleStarts(List<SwimStartCandidate> found, List<long> stamps)
        {
            if (found == null || found.Count < 2 || Race == null || Race.Distance <= 0 || stamps.Count == 0)
                return found;

            long end = frameServer.VideoReader.WorkingZone.End;
            double need = MinRaceSeconds();
            List<SwimStartCandidate> fits = new List<SwimStartCandidate>(), late = new List<SwimStartCandidate>();
            foreach (SwimStartCandidate c in found)
            {
                bool ok = c.Frame >= 0 && c.Frame < stamps.Count && Timing.Seconds(end, stamps[c.Frame]) >= need;
                (ok ? fits : late).Add(c);
            }

            if (fits.Count == 0 || late.Count == 0)
                return found;

            log.DebugFormat("Start: {0} candidate(s) leave less than {1:0} s of video for the race, proposed last.", late.Count, need);
            fits.AddRange(late);
            return fits;
        }

        /// <summary>
        /// Background thread: the frames from the beginning until the start is clear (or 'end'), the sound at the same time (ffmpeg:
        /// null, not read), then the starts from the lights and the signal heard.
        /// </summary>
        private StartFound ReadStart(SwimStartLight detector, List<long> stamps, long from, long end, int width, int height, BackgroundWorker worker,
            string ffmpeg, string path, double soundFrom, double soundSeconds, double tsPerSecond)
        {
            StartFound result = new StartFound();
            using (CancellationTokenSource stop = new CancellationTokenSource())
            {
                Task<List<SwimBeep>> heard = null;
                string soundError = null;
                if (ffmpeg != null)
                {
                    heard = Task.Run(() =>
                    {
                        string err;
                        float[] samples = SwimStartSound.Read(ffmpeg, path, soundFrom, soundSeconds, stop.Token, out err);
                        soundError = err;
                        return samples != null ? SwimStartSound.Find(samples) : new List<SwimBeep>();
                    });
                }

                // Once the signal is heard, the frames are read until 8 s after the last clear one (its race, for the checks).
                Func<long, bool> enough = ts =>
                {
                    if (heard == null || !heard.IsCompleted || heard.IsFaulted || heard.IsCanceled)
                        return false;
                    List<SwimBeep> clear = heard.Result.Where(b => b.Score >= SwimStartSound.MinScore).ToList();
                    return clear.Count > 0 && ts > from + (clear.Max(b => b.Onset) + 8) * tsPerSecond;
                };

                result.Completed = ReadStartLightFrames(detector, stamps, from, end, width, height, worker, enough);
                if (heard != null)
                {
                    try
                    {
                        // The sound is usually read long before the frames; cancelled, ffmpeg is stopped.
                        if (!result.Completed)
                            stop.Cancel();
                        while (!heard.Wait(200))
                        {
                            if (worker.CancellationPending && !stop.IsCancellationRequested)
                            {
                                stop.Cancel();
                                result.Completed = false;
                            }
                        }

                        result.Beeps = heard.Result;
                        result.SoundError = soundError;
                    }
                    catch (AggregateException e)
                    {
                        result.SoundError = e.InnerException != null ? e.InnerException.Message : e.Message;
                    }
                }

                if (!result.Completed)
                    return result;
            }

            List<SwimStartCandidate> found = detector.Find();
            if (result.Beeps.Count > 0 && stamps.Count > 1)
            {
                Func<double, double> frameAt = seconds => FrameAt(stamps, from + seconds * tsPerSecond);
                Func<int, int, double, SwimStartCandidate> fullSize = (lo, hi, expected) => TinyFlashAt(stamps, lo, hi, expected, width, detector.FramesPerSecond);
                found = SwimStartSound.Choose(found, result.Beeps, detector, frameAt, fullSize, SwimStartSound.Delay);
            }

            result.Candidates = found;
            return result;
        }

        /// <summary>Frame (fraction) of the frames read at a time stamp (NaN outside them).</summary>
        private static double FrameAt(List<long> stamps, double timestamp)
        {
            if (stamps.Count < 2 || timestamp < stamps[0] || timestamp > stamps[stamps.Count - 1])
                return double.NaN;

            int lo = 0, hi = stamps.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (stamps[mid] <= timestamp)
                    lo = mid;
                else
                    hi = mid;
            }

            return lo + (timestamp - stamps[lo]) / Math.Max(1.0, stamps[hi] - stamps[lo]);
        }

        /// <summary>Time stamp of a frame (fraction) of the frames read.</summary>
        private static long StampAt(List<long> stamps, double frame)
        {
            if (stamps.Count == 0 || double.IsNaN(frame))
                return stamps.Count > 0 ? stamps[0] : 0;

            int k = Math.Max(0, Math.Min(stamps.Count - 1, (int)Math.Floor(frame)));
            if (k >= stamps.Count - 1)
                return stamps[stamps.Count - 1];

            return (long)Math.Round(stamps[k] + (frame - k) * (stamps[k + 1] - stamps[k]));
        }

        /// <summary>
        /// Background thread, after the reading: the tiny flash of the light of the start in the frames at full size (at most 1920
        /// wide) between two frames read, too small for the frames of the reading, the nearer to the frame expected the better.
        /// Null: none.
        /// </summary>
        private SwimStartCandidate TinyFlashAt(List<long> stamps, int lo, int hi, double expected, int workWidth, double fps)
        {
            lo = Math.Max(0, lo);
            hi = Math.Min(stamps.Count - 1, hi);
            if (hi < lo)
                return null;

            VideoReader reader = frameServer.VideoReader;
            Size reference = reader.Info.ReferenceSize;
            int w = Math.Min(1920, Math.Max(64, reference.Width));
            int h = Math.Max(2, (int)Math.Round(w * (double)reference.Height / Math.Max(1, reference.Width)));
            int from = Math.Max(0, lo - 8), to = Math.Min(stamps.Count - 1, hi + 8);
            List<byte[]> images = new List<byte[]>();
            List<int> index = new List<int>();
            long current = reader.Current != null ? reader.Current.Timestamp : stamps[from];
            bool more = reader.MoveTo(current, stamps[from]);
            while (true)
            {
                VideoFrame frame = reader.Current;
                if (frame == null || frame.Image == null || frame.Timestamp > stamps[to])
                    break;

                int k = stamps.BinarySearch(frame.Timestamp);
                if (k >= 0)
                {
                    using (OpenCvSharp.Mat bgr = SwimStrokeVideo.ToWorking(frame.Image, w, h))
                        images.Add(Brightness(bgr));
                    index.Add(k);
                }

                if (!more)
                    break;

                more = reader.MoveNext(0, true);
            }

            int first = index.FindIndex(k => k >= lo), last = index.FindLastIndex(k => k <= hi);
            if (first < 0 || last < first)
                return null;

            double score;
            double[] place;
            int i = SwimStartLight.TinyFlash(images, w, h, first, out score, out place, k => SwimStartSound.Nearness(index[k], expected, fps), last);
            if (i < 0 || index[i] > hi)
                return null;

            double scale = (double)workWidth / w;
            return new SwimStartCandidate
            {
                Frame = index[i], Flash = Math.Round(score / 10, 1), Strength = score / 10, Lights = 1, Follow = -1,
                Places = new List<double[]> { new[] { place[0] * scale, place[1] * scale } }, Source = "light+sound"
            };
        }

        /// <summary>
        /// Background thread: frames from the beginning until the start is clear (or 'end', or 'enough' at a frame). False if
        /// cancelled.
        /// </summary>
        private bool ReadStartLightFrames(SwimStartLight detector, List<long> stamps, long from, long end, int width, int height, BackgroundWorker worker,
            Func<long, bool> enough)
        {
            VideoReader reader = frameServer.VideoReader;
            double span = Math.Max(1, end - from);
            int checkEvery = Math.Max(10, (int)Math.Round(detector.FramesPerSecond));
            bool completed = true;
            System.Threading.CancellationTokenSource done = new System.Threading.CancellationTokenSource();
            Exception detectorError = null;

            using (BlockingCollection<byte[]> queue = new BlockingCollection<byte[]>(8))
            {
                // The detector runs on the previous frames while this thread decodes the next ones.
                Task detection = Task.Run(() =>
                {
                    foreach (byte[] v in queue.GetConsumingEnumerable())
                    {
                        if (detectorError != null || done.IsCancellationRequested)
                            continue;

                        try
                        {
                            detector.Add(v);
                            if (detector.Count % checkEvery == 0 && detector.Done())
                                done.Cancel();
                        }
                        catch (Exception e)
                        {
                            detectorError = e;
                        }
                    }
                });

                try
                {
                    reader.BeforeFrameEnumeration();
                    long current = reader.Current != null ? reader.Current.Timestamp : from;
                    bool hasMore = reader.MoveTo(current, from);
                    while (detectorError == null && !done.IsCancellationRequested)
                    {
                        if (worker.CancellationPending)
                        {
                            completed = false;
                            break;
                        }

                        VideoFrame frame = reader.Current;
                        if (frame == null || frame.Image == null || frame.Timestamp > end || (enough != null && enough(frame.Timestamp)))
                            break;

                        using (OpenCvSharp.Mat bgr = SwimStrokeVideo.ToWorking(frame.Image, width, height))
                            queue.Add(Brightness(bgr));
                        stamps.Add(frame.Timestamp);
                        worker.ReportProgress((int)Math.Max(0, Math.Min(100, 100.0 * (frame.Timestamp - from) / span)));
                        if (!hasMore)
                            break;

                        hasMore = reader.MoveNext(0, true);
                    }
                }
                finally
                {
                    queue.CompleteAdding();
                    detection.Wait();
                    done.Dispose();
                }
            }

            if (detectorError != null)
                throw new InvalidOperationException("Start light: " + detectorError.Message, detectorError);

            // Frames read but not given to the detector (it stopped first) have no place in the result.
            while (stamps.Count > detector.Count)
                stamps.RemoveAt(stamps.Count - 1);

            return completed;
        }

        /// <summary>Brightness of each pixel (maximum of the three colours) of a BGR image.</summary>
        private static byte[] Brightness(OpenCvSharp.Mat bgr)
        {
            byte[] data = SwimStrokeVideo.Bytes(bgr);
            int n = bgr.Width * bgr.Height;
            byte[] v = new byte[n];
            for (int p = 0, q = 0; p < n; p++, q += 3)
            {
                byte m = data[q];
                if (data[q + 1] > m) m = data[q + 1];
                if (data[q + 2] > m) m = data[q + 2];
                v[p] = m;
            }

            return v;
        }

        /// <summary>
        /// The start proposals (the most likely first, the next ones if it is wrong). A start already marked by the analyst near
        /// the flash is an answer: it teaches the learning and nothing is asked.
        /// </summary>
        private void ProposeStart(List<SwimStartCandidate> found, List<long> stamps, double realFps)
        {
            string video = SwimLearning.VideoCode(videoPath);
            SwimLearning l = Learning;
            double confidence0 = SwimStartLight.Confidence(found);
            Race.Proposals.RemoveAll(p => p.Kind == KindRaceStart);
            string batch = Guid.NewGuid().ToString("N");
            List<SwimProposal> made = new List<SwimProposal>();
            for (int k = 0; k < Math.Min(3, found.Count); k++)
            {
                SwimStartCandidate c = found[k];
                if (c.Frame < 0 || c.Frame >= stamps.Count)
                    continue;

                // A start put before the dive of the swimmers (no light fits), or from the signal heard (no light seen before it), is
                // its own cue: its delay is learned apart.
                string cue = c.Source == "dive" ? "dive" : c.Source == "sound" ? "sound" : "light";
                SwimDetection d = new SwimDetection(KindRaceStart, SwimMarkType.Signal, k + 1) { Confidence = k == 0 ? confidence0 : 0.35 * confidence0 };
                double heardStart = double.IsNaN(c.Beep) ? double.NaN : c.Beep - SwimStartSound.Delay * realFps;
                if (cue == "sound")
                    d.Cue(cue, heardStart, 1.0, 0.03);
                else
                    d.Cue(cue, c.Frame, 1.0, c.Source == "dive" ? 0.1 : 0.02);
                double confidence;
                double sample = SwimProposalMath.Combine(d, l, video, realFps, out confidence);
                if (double.IsNaN(sample))
                    continue;

                int index = Math.Max(0, Math.Min(stamps.Count - 1, (int)Math.Round(sample)));
                SwimProposal p = new SwimProposal
                {
                    Type = SwimMarkType.Signal,
                    Kind = KindRaceStart,
                    Timestamp = stamps[index],
                    Confidence = Math.Round(confidence, 2),
                    Index = k + 1,
                    Batch = batch
                };
                p.Cues[cue] = cue == "sound" ? StampAt(stamps, heardStart) : stamps[c.Frame];

                // The signal heard with a light: its delay is learned too (for the starts without light).
                if (cue == "light" && !double.IsNaN(heardStart))
                    p.Cues["sound"] = StampAt(stamps, heardStart);
                p.Measures = new Dictionary<string, double>
                {
                    { "lights", c.Lights },
                    { "flash", Math.Round(c.Flash, 1) }
                };
                if (c.Follow >= 0 && c.Follow < stamps.Count)
                    p.Measures["follow"] = Math.Round(Timing.Seconds(stamps[c.Follow], stamps[c.Frame]), 2);
                if (c.Dive >= 0 && c.Dive < stamps.Count)
                    p.Measures["dive"] = Math.Round(Timing.Seconds(stamps[c.Dive], stamps[c.Frame]), 2);
                if (!double.IsNaN(c.Beep))
                {
                    p.Measures["beep"] = Math.Round((StampAt(stamps, c.Beep) - stamps[c.Frame]) / Math.Max(1e-9, frameServer.VideoReader.Info.AverageTimeStampsPerSeconds), 3);
                    p.Measures["beepScore"] = Math.Round(c.BeepScore, 1);
                }
                made.Add(p);
            }

            if (made.Count == 0)
            {
                Status("Δεν βρέθηκε σήμα εκκίνησης.", true);
                return;
            }

            // The analyst already put the start near the flash: that is his answer (learning), nothing to check (not a start of
            // the auto pilot not checked yet: proposed for the check).
            SwimProposal best = made[0];
            if (Race.StartTimestamp.HasValue && !Race.PilotStart && Math.Abs(Timing.Seconds(Race.StartTimestamp.Value, best.Timestamp)) <= 0.2)
            {
                long marked = Race.StartTimestamp.Value;
                Race.Proposals.Add(best);
                Answer(best, marked, true);
                Changed(false);
                long frames = (long)Math.Round((marked - best.Timestamp) / Math.Max(1, Timing.AverageTimestampsPerFrame));
                string what = found[0].Source == "sound" ? "το σήμα που ακούγεται" : "το φωτεινό σήμα";
                Status(frames == 0 ? "💡 Η εκκίνηση που έχεις βάλει είναι στο καρέ που βρέθηκε από " + what + ": κρατήθηκε για τη μάθηση." :
                    string.Format(CultureInfo.InvariantCulture, "💡 Η εκκίνηση που έχεις βάλει είναι {0:+0;-0} καρέ από αυτήν που βρέθηκε από {1}: κρατήθηκε για τη μάθηση.",
                    frames, what), false);
                return;
            }

            Race.Proposals.AddRange(made);
            Changed(false);
            BeginReview(best);
            if (best.Cues.ContainsKey("dive"))
                Status("⚠ Δεν φάνηκε το φως της εκκίνησης ούτε ακούστηκε το σήμα: η πρόταση είναι λίγο πριν αρχίσουν να κινούνται οι αθλητές. " +
                    "Έλεγξέ τη στο καρέ του σήματος.", true);
            else if (found[0].Source == "sound")
                Status("🔊 Δεν φάνηκε το φως της εκκίνησης: η πρόταση είναι από το σήμα που ακούγεται (λίγο πριν ακουστεί, όσο αργεί ο ήχος να φτάσει " +
                    "στην κάμερα). Έλεγξέ τη στο καρέ του φωτός.", false);
            else if (found[0].Source == "light+sound")
                Status("💡🔊 Η εκκίνηση στο φως του σήματος, που ακούγεται αμέσως μετά. Έλεγξέ τη.", false);

            // A start after which the video is too short for the race: surely not its start (the signal of the next race?).
            double left = Timing.Seconds(frameServer.VideoReader.WorkingZone.End, best.Timestamp);
            double need = MinRaceSeconds();
            if (Race.Distance > 0 && left < need)
                Status(string.Format(CultureInfo.InvariantCulture,
                    "⚠ Μετά την εκκίνηση που βρέθηκε μένουν μόνο {0:0} s βίντεο, ενώ τα {1} m θέλουν τουλάχιστον {2:0} s: μάλλον δεν είναι η εκκίνηση αυτού του αγώνα. " +
                    "Δες τις άλλες προτάσεις (→) ή βάλε την εκκίνηση με το κουμπί «Εκκίνηση» στο καρέ του σήματος.", Math.Max(0, left), Race.Distance, need), true);
        }

        /// <summary>The start proposal is accepted at this frame: it becomes the start of the race (and of the swimmers of the video).</summary>
        private void AcceptStartProposal(SwimProposal p, long timestamp)
        {
            // The other start candidates go (no answer: the learning does not change). The start is the analyst's now (also one
            // the auto pilot had put).
            Race.Proposals.RemoveAll(x => x.Kind == KindRaceStart && x.Id != p.Id);
            Race.PilotStart = false;
            resolvingProposal = true;
            try
            {
                HandleCommand(player, frameServer, PlayerScreenCommands.SwimStart, timestamp);
            }
            finally
            {
                resolvingProposal = false;
            }

            Answer(p, timestamp, true);
            Changed(false);
            string done = "✓ " + ProposalLabel(p) + ": t = 0 σε αυτό το καρέ.";

            // The auto pilot waited for this check of the start: it goes on with the whole race.
            if (PilotActive && pilotStep == PilotStep.Start)
            {
                pilotStep = PilotStep.None;
                EndReview(false);
                pilotStartNote = "την επιβεβαίωσες εσύ";
                PilotLater(PilotRace);
                return;
            }

            // A new analysis (nothing else waiting, no checkpoint yet): straight on to the whole race, a little after the breakout,
            // where the analyst clicks the swimmer (Esc: not now). Not while the auto pilot works (it asks for the swimmer itself).
            if (!PilotActive && PendingProposals.Count == 0 && PoolHasMarks && !Race.IsTraining && !Race.Marks.Any(m => m.Type == SwimMarkType.Split))
            {
                EndReview(false);
                SeekTo(Math.Min(frameServer.VideoReader.WorkingZone.End, timestamp + RealSpan(5.0)));
                BeginAutoRace();
                if (PickingTrack)
                {
                    Status(done + "  →  🤖 Όλος ο αγώνας: κλικ στο κεφάλι του αθλητή και μετά στα πόδια του (σε ένα καρέ μετά την έξοδο από το νερό· " +
                        "Esc: όχι τώρα).", false);
                    return;
                }
            }

            NextProposal(timestamp, done);
        }
        #endregion

        #region Checkpoints
        /// <summary>Proposal of a checkpoint found from the marks of the ropes (Index: the distance).</summary>
        public const string KindRaceSplit = "race.split";

        /// <summary>
        /// The checkpoints of the length just followed (TrackSwimmer), from the marks of the ropes, and its wall: proposed for the
        /// check, the ones not marked yet. The answers teach each cue (head over the mark of the far rope, of the near rope, their
        /// middle; head and body at the end of the ropes for the wall).
        /// </summary>
        private void ProposeSplits(StrokeSession s, SwimStrokeResult result)
        {
            LengthRead l = AnalyseLength(s, result, s.LapStart);
            if (l == null)
                return;

            List<SwimProposal> made = SplitProposals(l, Guid.NewGuid().ToString("N"));
            ShowSplitProposals(made, 1);
        }

        /// <summary>A length followed and what was found in it (background thread of the whole race, or one length).</summary>
        private sealed class LengthRead
        {
            public StrokeSession Session;
            public SwimStrokeResult Result;
            public List<SwimSplitFound> Found = new List<SwimSplitFound>();
            public Dictionary<string, double> Wall = new Dictionary<string, double>();

            /// <summary>Race distance of the wall where the length started.</summary>
            public int LapFrom;

            /// <summary>Cues of the breakout after the wall (or the start) of the length (frames of the reading), empty if not seen.</summary>
            public Dictionary<string, double> Breakout = new Dictionary<string, double>();

            /// <summary>Strokes of the length (frames of the reading): from the breakout to a little before the wall.</summary>
            public readonly List<int> Strokes = new List<int>();
            public double Period;

            /// <summary>Frame of the click on the swimmer in the reading (0: the reading starts there; the first length can start at the start).</summary>
            public int ClickIndex;

            /// <summary>Length of the swimmer clicked (working px, head to feet): a swimmer seen from far has his own delays.</summary>
            public double SwimmerLength = double.NaN;

            /// <summary>Frame of the wall from the pace of the length (also after the frames read; NaN: no pace).</summary>
            public double PaceWall = double.NaN;
        }

        /// <summary>Proposal of a breakout after a wall or the start, found in the whole race (Index: the wall before it, 0 the start).</summary>
        public const string KindRaceBreakout = "race.breakout";

        /// <summary>
        /// Background thread, after a length is read: its breakout (when the reading starts close enough to the wall or the start to
        /// have the underwater phase) and its strokes, from the breakout to a little before the wall.
        /// </summary>
        private void ReadLengthStrokes(LengthRead l)
        {
            StrokeSession s = l.Session;
            int n = s.Timestamps.Count;
            if (n < 10 || s.Fps <= 0)
                return;

            double step = (s.Timestamps[n - 1] - s.Timestamps[0]) / (double)Math.Max(1, n - 1);
            long lapStartTs = s.LapStart == long.MinValue && Race.StartTimestamp.HasValue ? Race.StartTimestamp.Value : s.LapStart;
            double lapStart = step > 0 ? (lapStartTs - s.Timestamps[0]) / step : double.NaN;
            string style = SwimPresets.StyleAt(Race, s.Wall);

            // Without a breakout the strokes start at the click (first length), or after the usual underwater phase of the style.
            int first = l.ClickIndex;
            if (l.LapFrom > 0 && !double.IsNaN(lapStart))
                first = Math.Max(first, (int)Math.Round(lapStart + 0.85 * (style == "fs" ? 2.5 : 4.5) * s.Fps));

            // The breakout, when the reading has the underwater phase (it starts at the wall or the start). The next swimmer of a
            // relay dives from the block as at the start.
            bool dive = IsDive(l.LapFrom);
            if (!double.IsNaN(lapStart) && lapStart > -1.5 * s.Fps)
            {
                int from = (int)Math.Ceiling(Math.Max(0, lapStart + (dive ? 1.5 : 1.0) * s.Fps));
                SwimSplitFound f15 = l.Found.FirstOrDefault(f => f.LapDistance == 15);
                double at15 = f15 != null ? SwimSplitDetector.BreakoutSearchEnd(f15.Cues, !dive, style, s.Fps, Race.Pool) : double.NaN;
                int to = !double.IsNaN(at15) ? (int)Math.Round(at15) : from + (int)Math.Round(8 * s.Fps);
                l.Breakout = SwimSplitDetector.BreakoutCues(l.Result, s.Fps, from, to);

                // After the start not before 3 s (entry and glide of the fastest). Cues earlier than that are the kicks under the
                // surface before the head is out (distance freestyle): the breakout is then that of the first stroke counted. The
                // 3 s are those of the cues before the delay of the steepest rise after the start (StartRiseDelay: with it a breakout
                // at 3.0 s came at 2.87 s and went to the first stroke, without the test on the cues the kicks at 2.9 s stayed).
                double b = BreakoutSample(l), bs = BreakoutSample(l, true);
                double floor = lapStart + 3.0 * s.Fps;
                if (dive && !double.IsNaN(b) && BreakoutSample(l, false, true) < floor)
                {
                    if (!double.IsNaN(bs) && bs >= floor)
                    {
                        foreach (string k in l.Breakout.Keys.ToList())
                            l.Breakout[k] += bs - b;
                        b = bs;
                    }
                    else
                    {
                        l.Breakout.Clear();
                        b = bs = double.NaN;
                    }
                }

                // The strokes are counted from the first stroke the analysts count: after a wall a little before the head is out
                // (the breakout stroke), after the start the first stroke after the breakout.
                if (!double.IsNaN(b))
                    first = Math.Max(0, (int)Math.Round((double.IsNaN(bs) ? b : bs) + (dive ? 0.1 : -0.4) * s.Fps));
            }

            double wall = WallSample(l);
            int last = (int)Math.Round((double.IsNaN(wall) ? n - 1 : wall) - SwimStrokeDetector.WallMargin(s.Style) * s.Fps);
            last = Math.Min(n - 1, last);
            first = Math.Max(0, Math.Min(n - 1, first));
            if (last <= first + 5)
                return;

            SwimStrokeResult r = s.Detector.Finish(first, last);
            l.Strokes.AddRange(r.Strokes);
            l.Period = r.Period;
        }

        /// <summary>
        /// A length done: the band of each frame goes (about 30 MB a length: a 1500 m keeps only what the proposals, the strokes and
        /// the place of the swimmer need).
        /// </summary>
        private static void ReleaseFrames(LengthRead l)
        {
            l.Session.Detector.Release();
            SwimStrokeResult r = l.Result;
            r.Motion.Clear();
            r.Motion.TrimExcess();
            r.Shifts.Clear();
            r.Shifts.TrimExcess();
            r.Motions.Clear();
            r.RedFar.Clear();
            r.RedNear.Clear();
            r.Onset = null;
            if (l.Session.PreviousGray != null)
            {
                l.Session.PreviousGray.Dispose();
                l.Session.PreviousGray = null;
            }
        }

        /// <summary>
        /// Cues of the breakout with their usual delay before any learning (SwimSplitDetector.BreakoutDelay): the motion around
        /// the swimmer and the first strong splash come with the head out; in backstroke the kicks on the back move the water for
        /// seconds before, the cues that come that early do not agree with the others. The steepest rise of the motion is the
        /// head and the arms coming out, in every style (freestyle too: within ±0.3 s after the turns of the phone videos).
        /// Named after the style of the length (the learning keeps each style apart).
        /// </summary>
        private Dictionary<string, double> BreakoutCuesDelayed(LengthRead l, bool strokes = false, bool startUndelayed = false)
        {
            string style = SwimPresets.StyleAt(Race, l.Session.Wall);
            bool turn = !IsDive(l.LapFrom);
            bool change = turn && l.LapFrom > 0 && SwimPresets.StyleAt(Race, l.LapFrom) != style;
            return BreakoutCuesDelayed(l.Breakout, style, l.Session.Fps, turn, strokes, change, startUndelayed, l.SwimmerLength);
        }

        /// <summary>
        /// The same for the cues of a reading (frames) in a style, after the start (or the dive of a relay) or after a turn. After
        /// a turn (analysed races) the steepest rise comes with the first strokes after the head is out: in butterfly and
        /// breaststroke about 1.2 s after it, in backstroke 0.3 s; the first strong splash is not the breakout (freestyle 2.5 s
        /// early, the others ±3 s), nor, in butterfly, the motion starting (±3 s). The cues after a turn are named apart (".t"):
        /// the learning keeps their delays apart from the start's. strokes: the cues of the first stroke counted after the breakout
        /// (SwimSplitDetector.StrokeStartDelay, with the names and the learning of the breakout before 2026-10-15). change: a turn
        /// into another style (the open turns of the medley): the steepest rise in backstroke has its own delay and learning there
        /// (".o"; the motion starting after the turn into freestyle tried as not sure: as many breakouts better as worse).
        /// </summary>
        private static Dictionary<string, double> BreakoutCuesDelayed(Dictionary<string, double> found, string style, double fps, bool turn, bool strokes = false, bool change = false, bool startUndelayed = false, double swimmerLength = double.NaN)
        {
            Dictionary<string, double> cues = new Dictionary<string, double>();

            // By the view of the swimmer since 2026-10-18 (a camcorder near him sees the head out sooner than a phone in the stands).
            double far = SwimSplitDetector.FarShare(swimmerLength);
            foreach (KeyValuePair<string, double> c in found)
            {
                double delay;
                if (c.Key == SwimSplitDetector.CueRise)
                {
                    if (string.IsNullOrEmpty(style) || (strokes && style == "fs"))
                        continue;
                    delay = turn ? (strokes ? SwimSplitDetector.TurnRiseStrokeDelay(style) : SwimSplitDetector.TurnRiseDelay(style, change, far))
                        : (strokes || startUndelayed ? 0 : SwimSplitDetector.StartRiseDelay(style, far));
                }
                else
                {
                    if (turn && SwimSplitDetector.TurnBreakout && (c.Key == SwimSplitDetector.CueBurst || (style == "fly" && c.Key == SwimSplitDetector.CueEnergy)))
                        continue;
                    // After the dive the first strong splash is the breakout of a swimmer seen from far only (since 2026-10-18).
                    if (!turn && !strokes && c.Key == SwimSplitDetector.CueBurst && !SwimSplitDetector.StartBurst(far))
                        continue;
                    delay = strokes ? SwimSplitDetector.StrokeStartDelay(c.Key, style) : SwimSplitDetector.BreakoutDelay(c.Key, style);
                }

                // The rise after a breaststroke turn has its delay since 2026-10-15 (".4": the answers kept with the old one do not move
                // it), after a butterfly turn and after the breaststroke start since 2026-10-16 (".5"), after the start in the other
                // styles since 2026-10-18 (".7"). The rises whose delay depends on the view since 2026-10-18 (butterfly turns, the
                // breaststroke start, the medley turn into backstroke) learn apart near the swimmer (".8": a camcorder).
                string version = "";
                if (!strokes && c.Key == SwimSplitDetector.CueRise)
                {
                    version = turn && style == "br" ? ".4" : (turn && style == "fly") || (!turn && style == "br") ? ".5" : !turn ? ".7" : "";
                    if (far < 0.5 && ((turn && style == "fly") || (!turn && style == "br") || (turn && change && style == "bk")))
                        version += ".8";
                }

                // The open turn of the medley into backstroke: its rise learns apart (".o").
                bool open = turn && change && SwimSplitDetector.TurnBreakout && style == "bk" && c.Key == SwimSplitDetector.CueRise;
                string name = c.Key + (turn && SwimSplitDetector.TurnBreakout ? ".t" : "") + (open ? ".o" : "") + PriorVersion(c.Key, style, strokes) + version;
                cues[StyleCue(name, style)] = c.Value + delay * fps;
            }

            return cues;
        }

        /// <summary>
        /// Part of the name of a cue whose usual delay changed (".2", ".3"): the answers kept with the old delay do not move the
        /// new one (CueWeight reads the name up to the dot). Backstroke: the breakout and the checkpoints since 2026-10;
        /// butterfly and freestyle: the motion and the splash of the breakout since 2026-10-15.
        /// </summary>
        private static string PriorVersion(string cue, string style, bool strokes = false)
        {
            if (style == "bk" && cue != SwimSplitDetector.CueRise)
                return ".2";
            if (!strokes && (style == "fs" || style == "fly") && (cue == SwimSplitDetector.CueEnergy || cue == SwimSplitDetector.CueBurst))
                return ".3";
            return "";
        }

        /// <summary>A cue of a style ("mid@br"): what the learning keeps (each style has its own delays and spreads).</summary>
        private static string StyleCue(string cue, string style)
        {
            return string.IsNullOrEmpty(style) ? cue : cue + "@" + style;
        }

        /// <summary>
        /// Frame of the breakout of a length read (index of its frames, NaN if not seen): its cues as for the proposal; strokes: the
        /// first stroke counted after it.
        /// </summary>
        private double BreakoutSample(LengthRead l, bool strokes = false, bool startUndelayed = false)
        {
            if (l == null || l.Breakout.Count == 0)
                return double.NaN;

            SwimDetection d = new SwimDetection(KindRaceBreakout, SwimMarkType.Breakout, l.LapFrom) { Confidence = 1.0 };
            foreach (KeyValuePair<string, double> c in BreakoutCuesDelayed(l, strokes, startUndelayed))
            {
                double weight, spread;
                CueWeight(c.Key, out weight, out spread);
                d.Cue(c.Key, c.Value, weight, spread);
            }

            double confidence;
            return SwimProposalMath.Combine(d, Learning, SwimLearning.VideoCode(videoPath), l.Session.Fps, out confidence);
        }

        /// <summary>
        /// The breakout of a length proposed (the one not marked yet; one marked by the analyst near it teaches the learning).
        /// </summary>
        private SwimProposal BreakoutProposal(LengthRead l, string batch, bool all = false)
        {
            if (l.Breakout.Count == 0)
                return null;

            StrokeSession s = l.Session;
            Dictionary<string, double> cues = BreakoutCuesDelayed(l);
            // The confidence shown by the kind of breakout as measured on the analysts' races (since 2026-10-18).
            string style = SwimPresets.StyleAt(Race, s.Wall);
            bool turn = !IsDive(l.LapFrom);
            bool change = turn && l.LapFrom > 0 && SwimPresets.StyleAt(Race, l.LapFrom) != style;
            SwimDetection d = new SwimDetection(KindRaceBreakout, SwimMarkType.Breakout, l.LapFrom)
            {
                Confidence = SwimSplitDetector.BreakoutConfidence(style, turn, change, cues.Count)
            };
            foreach (KeyValuePair<string, double> c in cues)
            {
                double weight, spread;
                CueWeight(c.Key, out weight, out spread);
                d.Cue(c.Key, c.Value, weight, spread);
            }

            double confidence;
            double sample = SwimProposalMath.Combine(d, Learning, SwimLearning.VideoCode(videoPath), s.Fps, out confidence);
            if (double.IsNaN(sample))
                return null;

            SwimProposal p = new SwimProposal
            {
                Type = SwimMarkType.Breakout,
                Kind = KindRaceBreakout,
                Timestamp = SessionStamp(s, sample),
                Confidence = Math.Round(confidence, 2),
                Index = l.LapFrom,
                Batch = batch
            };

            foreach (KeyValuePair<string, double> c in cues)
                p.Cues[c.Key] = SessionStamp(s, c.Value);

            // A breakout marked by the analyst after this wall (an analysis made before): his frame teaches without asking (not one
            // of the auto pilot not checked yet: proposed again).
            if (all)
                return p;

            long lapStart = s.LapStart == long.MinValue && Race.StartTimestamp.HasValue ? Race.StartTimestamp.Value : s.LapStart;
            SwimMark marked = Race.Marks.Where(m => m.Type == SwimMarkType.Breakout && m.Pilot == null && m.Timestamp > lapStart && m.Timestamp <= lapStart + RealSpan(12))
                .OrderBy(m => m.Timestamp).FirstOrDefault();
            if (marked != null)
            {
                if (Math.Abs(Timing.Seconds(marked.Timestamp, p.Timestamp)) <= 1.5)
                {
                    Race.Proposals.Add(p);
                    Answer(p, marked.Timestamp, true);
                    learnedFromMarks++;
                }

                return null;
            }

            return p;
        }

        /// <summary>
        /// The breakout after the wall at 'wall' (0: the start) is at this frame: the underwater distance from the time to the next
        /// checkpoint (marked or proposed) at the speed of the swimming after it, 12% faster (the glide of the breakout, as in the
        /// analyses made: within about 0.3 m). Written only where the analyst has no value; returns the text for the status
        /// (empty if nothing was written).
        /// </summary>
        private string EstimateUnderwaterDistance(int wall, long breakout)
        {
            if (Race == null || !Race.StartTimestamp.HasValue || !Timing.IsValid)
                return "";

            bool start = wall == 0;
            SwimTurnManual turn = start ? null : Race.GetTurn(wall, false);
            if ((start && Race.UnderwaterDist.HasValue) || (!start && turn != null && turn.UnderwaterDist.HasValue))
                return "";

            // The first two checkpoints after the breakout in this length, marked or proposed.
            double tb = Timing.Seconds(breakout, Race.StartTimestamp.Value);
            List<Tuple<int, double>> next = new List<Tuple<int, double>>();
            foreach (int d in Race.Checkpoints.Where(c => c > wall && c < wall + Race.Pool).OrderBy(c => c))
            {
                SwimMark mark = Race.GetSplit(d);
                SwimProposal p = Race.Proposals.FirstOrDefault(x => x.Kind == KindRaceSplit && x.Index == d);
                long? ts = mark != null ? mark.Timestamp : p != null ? p.Timestamp : (long?)null;
                if (!ts.HasValue)
                    continue;
                double t = Timing.Seconds(ts.Value, Race.StartTimestamp.Value);
                if (t > tb)
                    next.Add(Tuple.Create(d, t));
                if (next.Count == 2)
                    break;
            }

            if (next.Count < 2 || next[1].Item2 <= next[0].Item2)
                return "";

            double speed = (next[1].Item1 - next[0].Item1) * Race.K / (next[1].Item2 - next[0].Item2);
            double distance = (next[0].Item1 - wall) * Race.K - 1.12 * speed * (next[0].Item2 - tb);
            if (distance < 2 || distance > (next[0].Item1 - wall) * Race.K)
                return "";

            distance = Math.Round(distance / Race.K, 1);
            if (start)
                Race.UnderwaterDist = distance;
            else
                Race.GetTurn(wall, true).UnderwaterDist = distance;
            return string.Format(CultureInfo.InvariantCulture, " · κάτω από το νερό {0:0.0}{1} (εκτίμηση από τον χρόνο ως τα {2}· για ακρίβεια 📍 Μέτρηση)",
                distance, Race.Unit, next[0].Item1);
        }

        /// <summary>
        /// The breakout after the wall at 'wall' (0: the start) is at this frame: the automatic strokes of the length before it (more
        /// than the breakout stroke, 0.4 s) go. Returns them.
        /// </summary>
        private List<SwimMark> TrimAutoStrokesBeforeBreakout(int wall, long breakout)
        {
            if (Race == null || !Race.StartTimestamp.HasValue)
                return new List<SwimMark>();

            SwimMark wallMark = wall > 0 ? Race.GetSplit(wall) : null;
            long from = wall == 0 ? Race.StartTimestamp.Value : wallMark != null ? wallMark.Timestamp : breakout - RealSpan(8);
            long to = breakout - RealSpan(0.4);
            List<SwimMark> removed = Race.Marks.Where(m => m.Type == SwimMarkType.Stroke && m.Batch != null && m.Timestamp > from && m.Timestamp < to).ToList();
            Race.Marks.RemoveAll(m => removed.Contains(m));
            if (removed.Count > 0)
                log.DebugFormat("Breakout after {0}: {1} automatic strokes before it removed.", wall, removed.Count);
            return removed;
        }

        /// <summary>
        /// The strokes of a length as automatic stroke marks (they replace the automatic ones of the length; the analyst's strokes
        /// stay and none is put within half a stroke of them). Returns the number put.
        /// </summary>
        private int PlaceLengthStrokes(LengthRead l)
        {
            StrokeSession s = l.Session;
            int n = s.Timestamps.Count;
            if (l.Strokes.Count == 0 || n < 10)
                return 0;

            long lapStart = s.LapStart == long.MinValue && Race.StartTimestamp.HasValue ? Race.StartTimestamp.Value : s.LapStart;
            double wall = WallSample(l);
            long lapEnd = double.IsNaN(wall) ? s.Timestamps[n - 1] : SessionStamp(s, wall);
            Race.Marks.RemoveAll(m => m.Type == SwimMarkType.Stroke && m.Batch != null && m.Timestamp > lapStart && m.Timestamp < lapEnd);

            long near = Math.Max(1, SessionSpan(s, 0.5 * (l.Period > 0 ? l.Period : 0.5)));
            List<long> kept = Race.Marks.Where(m => m.Type == SwimMarkType.Stroke).Select(m => m.Timestamp).ToList();
            int placed = 0;
            foreach (int i in l.Strokes)
            {
                if (i < 0 || i >= n)
                    continue;
                long t = s.Timestamps[i];
                if (kept.Any(k => Math.Abs(k - t) < near) || s.Removed.Any(r => Math.Abs(r - t) < near))
                    continue;

                Race.Marks.Add(new SwimMark { Type = SwimMarkType.Stroke, Timestamp = t, Batch = s.Batch });
                placed++;
            }

            return placed;
        }

        private bool PoolHasMarks
        {
            get { return Race != null && (Race.Pool == 50 || Race.Pool == 25); }
        }

        /// <summary>The checkpoints and the wall of a length read (null if the race or the reading does not allow it).</summary>
        private LengthRead AnalyseLength(StrokeSession s, SwimStrokeResult result, long lapStartTimestamp)
        {
            if (Race == null || Race.IsTraining || !Race.StartTimestamp.HasValue || s.Timestamps.Count < 10 || s.Fps <= 0 || result == null)
                return null;

            if (!PoolHasMarks)
            {
                Status("Αυτόματα περάσματα: μόνο σε πισίνα 50 ή 25 (σημάδια των σχοινιών).", true);
                return null;
            }

            int n = s.Timestamps.Count;
            double step = (s.Timestamps[n - 1] - s.Timestamps[0]) / (double)Math.Max(1, n - 1);
            if (step <= 0)
                return null;

            // Index of the start of the length in the frames read (before them: negative).
            long lapStartTs = lapStartTimestamp == long.MinValue ? Race.StartTimestamp.Value : lapStartTimestamp;
            double lapStart = (lapStartTs - s.Timestamps[0]) / step;
            LengthRead l = new LengthRead { Session = s, Result = result, LapFrom = s.Wall - Race.Pool, SwimmerLength = s.Detector.SwimmerLength };
            try
            {
                l.Found = SwimSplitDetector.Detect(result, s.Detector.Direction, s.Fps, lapStart, Race.Pool);
                SwimSplitDetector.AddFifteenDelay(l.Found, s.Fps, Race.Pool, l.LapFrom > 0 && !IsDive(l.LapFrom), SwimPresets.StyleAt(Race, s.Wall));
                double last = l.Found.Count > 0 ? l.Found.Max(f => f.Cues.Values.Max()) : double.NaN;
                l.Wall = SwimSplitDetector.WallCues(result, s.Detector.Direction, s.Fps, last, lapStart, Race.Pool, s.Shortest);

                // Checkpoints far from the time of the length (the one before it, the wall seen) go.
                int lapFrom = s.Wall - Race.Pool;
                bool sameStyle = lapFrom > 0 && SwimPresets.StyleAt(Race, s.Wall) == SwimPresets.StyleAt(Race, lapFrom) && !IsDive(lapFrom);
                int foundBefore = l.Found.Count;
                SwimSplitDetector.KeepWithLength(l.Found, l.Wall, lapStart, s.Fps, Race.Pool, IsDive(lapFrom), s.PreviousSeconds,
                    lapFrom > 0 && IsDive(lapFrom - Race.Pool), sameStyle);
                if (l.Found.Count != foundBefore)
                {
                    last = l.Found.Count > 0 ? l.Found.Max(f => f.Cues.Values.Max()) : double.NaN;
                    l.Wall = SwimSplitDetector.WallCues(result, s.Detector.Direction, s.Fps, last, lapStart, Race.Pool, s.Shortest);
                }

                // The pace of the swimmer: the checkpoints not seen and the wall from the ones found.
                double ratio = SwimSplitDetector.LastFiveRatio(WallKind(s.Wall), SwimPresets.StyleAt(Race, s.Wall));
                l.PaceWall = SwimSplitDetector.AddPace(l.Found, l.Wall, Race.Pool, ratio, n);

                // One checkpoint only (the swimmer far or hidden): the wall from it and the start of the length.
                if (double.IsNaN(l.PaceWall))
                    l.PaceWall = SwimSplitDetector.AddSinglePace(l.Found, l.Wall, Race.Pool, lapStart, IsDive(lapFrom), WallKind(s.Wall), n);
            }
            catch (Exception e)
            {
                log.Error("Automatic checkpoints failed.", e);
                return null;
            }

            return l;
        }

        /// <summary>Timestamp at a frame index of a reading (fractions between the frames).</summary>
        private static long SessionStamp(StrokeSession s, double index)
        {
            int n = s.Timestamps.Count;
            double clamped = Math.Max(0, Math.Min(n - 1, index));
            int i0 = (int)Math.Floor(clamped);
            int i1 = Math.Min(n - 1, i0 + 1);
            return (long)Math.Round(s.Timestamps[i0] + (clamped - i0) * (s.Timestamps[i1] - s.Timestamps[i0]));
        }

        /// <summary>Proposals of the checkpoints and of the wall of a length (the ones not marked yet).</summary>
        private List<SwimProposal> SplitProposals(LengthRead l, string batch, bool all = false)
        {
            List<SwimProposal> made = new List<SwimProposal>();
            StrokeSession s = l.Session;
            string video = SwimLearning.VideoCode(videoPath);
            SwimLearning learning = Learning;

            List<Tuple<int, Dictionary<string, double>, double>> items = new List<Tuple<int, Dictionary<string, double>, double>>();
            string style = SwimPresets.StyleAt(Race, s.Wall);
            // A swimmer seen from far (a phone in the stands) has his own delays and his own cues for the learning (".f").
            double far = SwimSplitDetector.FarCheckpointDelay(style, l.SwimmerLength);
            double delay = (CheckpointDelay(style) + far) * s.Fps;
            string view = far > 0 ? ".f" : "";
            foreach (SwimSplitFound f in l.Found)
            {
                Dictionary<string, double> cues = new Dictionary<string, double>();
                // The marks of one rope and the quarter lines have their delay by distance since 2026-10-16 (".6", 50 m pools).
                foreach (KeyValuePair<string, double> c in f.Cues)
                {
                    string version = Race.Pool == 50 && SwimSplitDetector.HasMarkDelay(c.Key) ? ".6" : "";
                    cues[StyleCue(c.Key + PriorVersion(c.Key, style) + version + view, style)] = c.Value + delay;
                }
                // The confidence shown as measured on the analysts' races (cues apart, the 45 m; since 2026-10-18).
                items.Add(Tuple.Create(l.LapFrom + f.LapDistance, cues, SwimSplitDetector.CheckpointConfidence(f, s.Fps, Race.Pool)));
            }

            if (l.Wall.Count > 0)
                items.Add(Tuple.Create(s.Wall, WallCuesOfKind(l, true), SwimSplitDetector.WallConfidence(l.Wall.Keys)));

            foreach (Tuple<int, Dictionary<string, double>, double> item in items)
            {
                int distance = item.Item1;
                if (!Race.Checkpoints.Contains(distance))
                    continue;

                SwimDetection d = new SwimDetection(KindRaceSplit, SwimMarkType.Split, distance) { Confidence = item.Item3 };
                foreach (KeyValuePair<string, double> c in item.Item2)
                {
                    double weight, spread;
                    CueWeight(c.Key, out weight, out spread);
                    d.Cue(c.Key, c.Value, weight, spread);
                }

                double confidence;
                double sample = SwimProposalMath.Combine(d, learning, video, s.Fps, out confidence);
                if (double.IsNaN(sample))
                    continue;

                SwimProposal p = new SwimProposal
                {
                    Type = SwimMarkType.Split,
                    Kind = KindRaceSplit,
                    Timestamp = SessionStamp(s, sample),
                    Confidence = Math.Round(confidence, 2),
                    Index = distance,
                    Batch = batch
                };

                foreach (KeyValuePair<string, double> c in item.Item2)
                    p.Cues[c.Key] = SessionStamp(s, c.Value);

                // Already marked by the analyst (an analysis made before): his frame is the answer, it teaches without asking.
                // (All of them for the reading in the background: its caller teaches with the marks. A checkpoint of the auto pilot
                // not checked yet is proposed again.)
                SwimMark marked = all ? null : Race.GetSplit(distance);
                if (marked != null && marked.Pilot != null)
                    marked = null;
                if (marked != null)
                {
                    if (Math.Abs(Timing.Seconds(marked.Timestamp, p.Timestamp)) <= 1.5)
                    {
                        Race.Proposals.Add(p);
                        Answer(p, marked.Timestamp, true);
                        learnedFromMarks++;
                    }

                    continue;
                }

                made.Add(p);
            }

            return made;
        }

        /// <summary>
        /// Usual delay (s) of the checkpoints from the front of the moving part, before any learning: in backstroke the arms and
        /// the splashes of the recovery are behind the head, the line is crossed a little earlier than seen (races of 50 and 100 m
        /// backstroke: 0.1–0.3 s).
        /// </summary>
        private static double CheckpointDelay(string style)
        {
            return style == "bk" ? -0.15 : 0;
        }

        /// <summary>
        /// The cues of the wall of a length named after its kind (finish, open turn, flip turn), each with its usual delay before
        /// any learning. proposal: the wall proposed to the analyst (the reading goes on from the wall without it).
        /// </summary>
        private Dictionary<string, double> WallCuesOfKind(LengthRead l, bool proposal = false)
        {
            string kind = WallKind(l.Session.Wall);
            string style = SwimPresets.StyleAt(Race, l.Session.Wall);
            Dictionary<string, double> wall = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> c in l.Wall)
            {
                // The turn of the pace of a swimmer seen from far, as proposed, has the delay of his checkpoints since 2026-10-18,
                // and learns apart (".f"); the reading goes on from the wall without it (the next length, its strokes: as before).
                double far = proposal ? SwimSplitDetector.FarWallDelay(c.Key, kind, style, l.SwimmerLength) : 0;
                wall[StyleCue(c.Key + "." + kind + (far > 0 ? ".f" : ""), style)] = c.Value + (WallDelay(c.Key, kind) + far) * l.Session.Fps;
            }

            SwimSplitDetector.PreferSeenWall(wall, l.Session.Fps);
            return wall;
        }

        /// <summary>Frame of the wall of a length read (index of its frames, NaN if not found): its cues as for the proposal.</summary>
        private double WallSample(LengthRead l)
        {
            if (l == null || l.Wall.Count == 0)
                return double.NaN;

            SwimDetection d = new SwimDetection(KindRaceSplit, SwimMarkType.Split, l.Session.Wall) { Confidence = 1.0 };
            foreach (KeyValuePair<string, double> c in WallCuesOfKind(l))
            {
                double weight, spread;
                CueWeight(c.Key, out weight, out spread);
                d.Cue(c.Key, c.Value, weight, spread);
            }

            double confidence;
            return SwimProposalMath.Combine(d, Learning, SwimLearning.VideoCode(videoPath), l.Session.Fps, out confidence);
        }

        /// <summary>Checkpoints already marked that taught the learning in the last automatic reading.</summary>
        private int learnedFromMarks;

        /// <summary>
        /// The checkpoints still waiting take what the last answer taught: each one again from its cues with the learning now
        /// (in the same video the answers count more: same camera, same analyst), so the next ones need fewer corrections.
        /// </summary>
        private void RecombineSplitProposals()
        {
            if (Race == null || !Timing.IsValid)
                return;

            SwimLearning learning = Learning;
            string video = SwimLearning.VideoCode(videoPath);
            foreach (SwimProposal p in Race.Proposals.Where(x => x.Kind == KindRaceSplit && x.Cues.Count > 0))
            {
                // A checkpoint the auto pilot put stays at the frame of its mark (Enter there: right).
                if (IsPilotPut(p))
                    continue;

                long origin = p.Cues.Values.Min();
                SwimDetection d = new SwimDetection(KindRaceSplit, SwimMarkType.Split, p.Index) { Confidence = 1.0 };
                foreach (KeyValuePair<string, long> c in p.Cues)
                {
                    double weight, spread;
                    CueWeight(c.Key, out weight, out spread);
                    d.Cue(c.Key, Timing.Seconds(c.Value, origin), weight, spread);
                }

                double confidence;
                double seconds = SwimProposalMath.Combine(d, learning, video, 1.0, out confidence);
                if (!double.IsNaN(seconds))
                    p.Timestamp = origin + RealSpan(seconds);
            }
        }

        /// <summary>A reading goes on to the wall of the pace of the length when it is more than this after the wall found (s).</summary>
        private const double ReadOnToPace = 0.5;

        /// <summary>The length that starts at this distance starts with a dive: the start, or the next swimmer of a relay.</summary>
        private bool IsDive(int lapFrom)
        {
            return lapFrom == 0 || (Race.Relay && Race.Distance >= 4 && lapFrom > 0 && lapFrom < Race.Distance && lapFrom % (Race.Distance / 4) == 0);
        }

        /// <summary>
        /// Shortest time of the length to 'wall' (s, 0: no limit) from the time of the length before it (previousSeconds, 0: not
        /// read): see SwimSplitDetector.ShortestLength.
        /// </summary>
        private double ShortestOfLength(int wall, double previousSeconds)
        {
            int from = wall - Race.Pool;
            if (previousSeconds <= 0 || from <= 0)
                return 0;

            bool sameStyle = SwimPresets.StyleAt(Race, wall) == SwimPresets.StyleAt(Race, from) && !IsDive(from);
            return SwimSplitDetector.ShortestLength(previousSeconds, IsDive(from - Race.Pool), sameStyle);
        }

        /// <summary>
        /// The wall at this distance: "finish" (also the end of a leg of a relay), "open" (hand touch: butterfly, breaststroke,
        /// the change of stroke of the medley) or "flip" (freestyle, backstroke).
        /// </summary>
        private string WallKind(int wall)
        {
            if (wall >= Race.Distance)
                return "finish";
            if (Race.Relay && Race.Distance >= 4 && wall % (Race.Distance / 4) == 0)
                return "finish";
            string style = SwimPresets.StyleAt(Race, wall);
            if (SwimPresets.StyleAt(Race, wall + 1) != style)
                return "open";
            return style == "fs" || style == "bk" ? "flip" : "open";
        }

        /// <summary>
        /// Usual time from a wall cue to the analyst's frame (s), before any learning: the swimmer slows down and the head reaches the
        /// end of the ropes before the touch, much before the feet touch in a flip turn.
        /// </summary>
        private static double WallDelay(string cue, string kind)
        {
            // The pace gives the analyst's wall already (time of the last 5 m as the analyses have it).
            if (cue == SwimSplitDetector.CuePace || cue == SwimSplitDetector.CuePaceOne)
                return 0;

            switch (kind)
            {
                case "flip":
                    return cue == SwimSplitDetector.CueStop ? 0.7 : cue == SwimSplitDetector.CueWallBody ? 0.9 : 1.0;
                case "open":
                    return cue == SwimSplitDetector.CueStop ? 0.15 : cue == SwimSplitDetector.CueWallBody ? 0.1 : 0.2;
                default:
                    return cue == SwimSplitDetector.CueStop ? 0.1 : cue == SwimSplitDetector.CueWallBody ? 0.0 : 0.2;
            }
        }

        /// <summary>Weight and usual spread (s) of each cue before any learning.</summary>
        private static void CueWeight(string cue, out double weight, out double spread)
        {
            string style = "";
            int at = cue.IndexOf('@');
            if (at > 0)
            {
                style = cue.Substring(at + 1);
                cue = cue.Substring(0, at);
            }

            // A cue of the breakout after a turn (".t").
            bool turn = cue.EndsWith(".t", StringComparison.Ordinal) || cue.Contains(".t.");
            int dot = cue.IndexOf('.');
            if (dot > 0)
                cue = cue.Substring(0, dot);

            // The line between the marks of the two ropes is what the analyst sees (the middle of the lane before any learning;
            // in breaststroke and backstroke the head is high and seen three quarters of the lane towards the far rope); the mark of
            // one rope alone is ahead or behind it by up to a second near the ends of the pool (perspective).
            bool breast = style == "br" || style == "bk";
            switch (cue)
            {
                case SwimSplitDetector.CueMiddle: weight = breast ? 0.6 : 1.0; spread = breast ? 0.12 : 0.08; break;
                case SwimSplitDetector.CueQuarterFar: weight = breast ? 1.0 : 0.6; spread = breast ? 0.08 : 0.12; break;
                case SwimSplitDetector.CueQuarterNear: weight = 0.6; spread = 0.12; break;
                case SwimSplitDetector.CueFar: weight = 0.4; spread = 0.25; break;
                case SwimSplitDetector.CueNear: weight = 0.3; spread = 0.3; break;
                // The wall: the pace of the length (from its checkpoints) before the image (the swimmer hidden in the splashes of
                // the turn, the camera turning).
                case SwimSplitDetector.CuePace: weight = 1.0; spread = 0.15; break;
                case SwimSplitDetector.CuePaceOne: weight = 1.0; spread = 0.25; break;
                case SwimSplitDetector.CueBurst: weight = 1.0; spread = 0.3; break;
                case SwimSplitDetector.CueEnergy:
                    bool sure = turn && style == "fs" && SwimSplitDetector.TurnEnergyFreestyle;
                    weight = sure ? 1.0 : 0.6;
                    spread = sure ? 0.2 : 0.5;
                    break;
                case SwimSplitDetector.CueRise: weight = 1.0; spread = 0.2; break;
                case SwimSplitDetector.CueWallBody: weight = 0.5; spread = 0.3; break;
                case SwimSplitDetector.CueWallHead: weight = 0.4; spread = 0.3; break;
                case SwimSplitDetector.CueStop: weight = 0.3; spread = 0.35; break;
                default: weight = 0.5; spread = 0.3; break;
            }
        }

        /// <summary>
        /// The proposals made replace the waiting ones of the same distances and the check starts (the auto pilot: they are put as
        /// marks and wait for the check).
        /// </summary>
        private void ShowSplitProposals(List<SwimProposal> made, int lengths, int strokes = 0, bool pilot = false)
        {
            Race.Proposals.RemoveAll(p => IsWholeRaceKind(p.Kind) && made.Any(m => m.Kind == p.Kind && m.Index == p.Index));
            Race.QuietProposals.RemoveAll(p => made.Any(m => m.Kind == p.Kind && m.Index == p.Index));
            int learned = learnedFromMarks;
            learnedFromMarks = 0;
            string strokesText = strokes > 0
                ? string.Format(CultureInfo.InvariantCulture, " {0} αυτόματες χεριές (διόρθωση: διπλό κλικ στις χεριές του τμήματος).", strokes)
                : "";
            if (made.Count == 0)
            {
                Status((learned > 0 ?
                    string.Format(CultureInfo.InvariantCulture, "🤖 Τα περάσματα είχαν ήδη χρόνους: {0} από αυτά κρατήθηκαν για τη μάθηση (δεν χρειάζεται έλεγχος).", learned) :
                    "Αυτόματα περάσματα: δεν βρέθηκαν σημάδια των σχοινιών (κόκκινοι φελλοί στα 15/25/35 και τα τελευταία 5μ.).") + strokesText, false);
                Changed(false);
                RefreshProposalsUi();
                return;
            }

            Race.Proposals.AddRange(made);
            if (pilot)
            {
                PilotAccept(made);
                Changed(true);
                RefreshProposalsUi();
                return;
            }

            Changed(false);
            BeginReview(made.OrderBy(p => p.Timestamp).First());
            if (lengths > 1 || strokes > 0)
                Status(string.Format(CultureInfo.InvariantCulture, "🤖 {0} μήκη, {1} προτάσεις.{2} ", lengths, made.Count, strokesText) + LastStatus, false);
        }

        #region Whole race
        private bool pickForRace;

        // Where the automatic reading of the race stopped when the swimmer was lost after a wall: the next click goes on from
        // there (wall at the end of the next length, start of it).
        private int resumeWall = -1;
        private long resumeLapStart = long.MinValue;
        private long resumeAt = long.MinValue;

        /// <summary>The next two clicks (head and feet of the swimmer after the breakout) start the analysis of the whole race.</summary>
        public void BeginAutoRace()
        {
            // The button of the analyst: the auto pilot (if it waited for him) stops, the reading is his; the click on the swimmer
            // it waited for stays armed for it.
            if (PilotActive)
            {
                bool armed = (pilotStep == PilotStep.Swimmer || pilotStep == PilotStep.Lost) && pickForRace && PickingTrack;
                StopPilot();
                if (armed)
                {
                    Status("🤖 Όλος ο αγώνας: ο αυτόματος πιλότος σταμάτησε, η ανάγνωση είναι δική σου. Κλικ στο κεφάλι του αθλητή και μετά στα " +
                        "πόδια του, σε ένα καρέ μετά την έξοδο από το νερό (Esc: ακύρωση).", false);
                    return;
                }
            }

            StartWholeRace();
        }

        /// <summary>The click on the swimmer for the whole race is waited for (the swimmer the camera follows proposed).</summary>
        private void StartWholeRace()
        {
            if (!PoolHasMarks && Race != null)
            {
                Status("Αυτόματη ανάλυση αγώνα: πισίνα 50 ή 25.", true);
                return;
            }

            int keepWall = resumeWall;
            long keepLapStart = resumeLapStart, keepAt = resumeAt;
            BeginTrackSwimmer();
            resumeWall = keepWall;
            resumeLapStart = keepLapStart;
            resumeAt = keepAt;
            if (PickingTrack)
            {
                pickForRace = true;
                Status("🤖 Αυτόματη ανάλυση αγώνα: σε ένα καρέ μετά την έξοδο από το νερό, κλικ στο κεφάλι του αθλητή και μετά στα πόδια του. " +
                    "Το Kinovea τον ακολουθεί σε όλα τα μήκη (Esc: ακύρωση).", false);

                // A new reading of the race (not the next lengths after a lost swimmer, nor from a later length): the swimmer the
                // camera follows is proposed, Enter takes him.
                long? now = CurrentVideoTimestamp;
                bool laterLength = Race != null && now.HasValue && Race.Marks.Any(m => m.Type == SwimMarkType.Split && m.Distance >= Race.Pool && m.Timestamp < now.Value);
                if (resumeWall <= 0 && !laterLength)
                    ProposeAutoPick();
            }
        }

        /// <summary>
        /// The whole race from one click: each length is followed to its wall, its checkpoints and its wall are found, then the
        /// next length is followed from the wall, the other way (the swimmer is where the previous length ended). Then all of them
        /// are proposed for the check.
        /// </summary>
        private void AutoRace(PointF head, PointF feet, long from)
        {
            // The auto pilot reads this race: it puts what is found and makes the report.
            bool pilot = PilotActive && (pilotStep == PilotStep.Swimmer || pilotStep == PilotStep.Lost);
            if (pilot)
            {
                pilotWasLost = pilotStep == PilotStep.Lost;
                StopPilotCountdown();
                pilotStep = PilotStep.Reading;

                // (The question of the swimmer or its countdown stayed on the status line during the reading until 2.7.1.)
                Status(pilotWasLost ? "✈ Ο αυτόματος πιλότος συνεχίζει να διαβάζει τον αγώνα από τον αθλητή που έδειξες… (Cancel: σταμάτημα)" :
                    "✈ Ο αυτόματος πιλότος διαβάζει τον αγώνα… (Cancel: σταμάτημα· ό,τι βρεθεί μένει)", false);
                RefreshPilotUi();
            }

            RefreshTiming();
            if (!Timing.IsValid || Race == null || !Race.StartTimestamp.HasValue)
            {
                if (pilot)
                    StopPilot();
                return;
            }

            List<LengthRead> lengths = new List<LengthRead>();
            Exception error = null;
            bool cancelled = false;
            AutoStrokesRunning = true;
            if (form != null && !form.IsDisposed)
                form.RefreshAutoStrokes();

            player.BeforeExportVideo();
            using (BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true })
            using (FormProgressBar progress = new FormProgressBar(true))
            {
                progress.CancelAsked += (o, e) => worker.CancelAsync();
                worker.ProgressChanged += (o, e) => progress.Update(e.ProgressPercentage, 100, true);
                worker.DoWork += (o, e) => { e.Result = ReadRace(head, feet, from, lengths, (BackgroundWorker)o); };
                worker.RunWorkerCompleted += (o, e) =>
                {
                    error = e.Error;
                    cancelled = e.Error == null && e.Result is bool && !(bool)e.Result;
                    progress.Close();
                };

                worker.RunWorkerAsync();
                progress.ShowDialog();
            }

            AutoStrokesRunning = false;
            frameServer.VideoReader.AfterFrameEnumeration();
            if (player != null && !player.IsDisposed)
                player.AfterExportVideo();
            if (form != null && !form.IsDisposed)
                form.RefreshAutoStrokes();

            try
            {
                if (error != null)
                {
                    log.Error("Automatic race analysis failed.", error);
                    if (pilot)
                        StopPilot();
                    Status("Η αυτόματη ανάλυση του αγώνα απέτυχε: " + error.Message, true);
                    return;
                }

                // What was read (also when cancelled) is kept and proposed.
                string batch = Guid.NewGuid().ToString("N");
                List<SwimProposal> made = new List<SwimProposal>();
                int strokes = 0;
                foreach (LengthRead l in lengths)
                {
                    StoreTrack(l.Session, l.Result);
                    made.AddRange(SplitProposals(l, batch));
                    SwimProposal breakout = BreakoutProposal(l, batch);
                    if (breakout != null)
                        made.Add(breakout);
                    strokes += PlaceLengthStrokes(l);
                }

                // The start events (when the first length was read) and the feet off the wall after each wall read.
                if (lengths.Count > 0 && lengths[0].LapFrom == 0)
                    made.AddRange(StartEventProposals(batch));
                List<SwimProposal> walls = made.Concat(Race.Proposals).ToList();
                foreach (int wall in lengths.Select(l => l.LapFrom).Concat(lengths.Select(l => l.Session.Wall)).Where(w => w > 0).Distinct().OrderBy(w => w))
                {
                    SwimProposal pushoff = PushoffProposal(wall, walls, batch);
                    if (pushoff != null)
                        made.Add(pushoff);
                }

                Changed(strokes > 0);
                ShowSplitProposals(made, lengths.Count, strokes, pilot);

                // (The auto pilot says it itself after the reading: the swimmer lost, or the report.)
                if (!pilot && cancelled)
                    Status(string.Format(CultureInfo.InvariantCulture, "Η ανάγνωση σταμάτησε μετά από {0} μήκη: προτείνονται όσα βρέθηκαν.", lengths.Count), true);
                else if (!pilot && resumeWall > 0)
                    Status(string.Format(CultureInfo.InvariantCulture,
                        "🤖 Μετά τον τοίχο των {0}{1} ο αθλητής χάθηκε (η κάμερα γύρισε γρήγορα). Έλεγξε τις προτάσεις και μετά πάτα ξανά «🤖 Όλος ο αγώνας» " +
                        "σε ένα καρέ μετά την έξοδο από το νερό: η ανάγνωση συνεχίζει από εκεί.", resumeWall - Race.Pool, Race.Unit), true);
            }
            finally
            {
                foreach (LengthRead l in lengths)
                    l.Session.Dispose();
                InvalidatePlayer();
                if (form != null && !form.IsDisposed)
                    form.RefreshReel();
            }

            if (pilot)
                PilotAfterReading(cancelled, lengths.Count);
        }

        /// <summary>
        /// The frames to read for a length: a first part (to the wall at the pace of a fast swimmer), then 3 s more each time the wall
        /// is not found yet, up to 'last' (a slow swimmer). After a length: from its time; the first length: from the distance left.
        /// </summary>
        private void ReadingSpan(long from, long lapStart, int wall, double previousSeconds, out long target, out long last)
        {
            long end = ReadingZone.End;
            SwimMark split = Race.GetSplit(wall);
            if (split != null && split.Pilot == null && split.Timestamp > from)
            {
                // The wall is marked already (an analysis made before): a little after it.
                target = last = Math.Min(end, split.Timestamp + RealSpan(1.2));
                return;
            }

            double first, most;
            if (previousSeconds > 0)
            {
                first = previousSeconds * 1.05 + 1.0;
                most = previousSeconds * 1.6 + 6.0;
            }
            else
            {
                // Distance left: from the curve of the race when it has checkpoints, else from the time since the start of the lap.
                SwimReel reel = Reel;
                double t = Timing.Seconds(from, Race.StartTimestamp.Value);
                double at = reel != null && reel.HasModel ? reel.DistanceAt(t) : wall - Race.Pool + Timing.Seconds(from, lapStart) / 0.6;
                double left = Math.Max(5.0, (wall - Math.Min(wall - 5.0, at)) * Race.K);
                first = left * 0.5 + 2.0;
                most = left * 1.3 + 6.0;
            }

            target = Math.Min(end, from + RealSpan(Math.Min(120, first)));
            last = Math.Min(end, from + RealSpan(Math.Min(180, most)));
        }

        /// <summary>
        /// The first length read from the start signal, so that the click on the swimmer can come later in the length (even after
        /// the 15 m) and the underwater phase of the start is read: the lane clicked is taken back to the start with the motion of
        /// the camera (composed frame to frame), then followed from there again with its two neighbours (the composed motion
        /// drifts a little); the one that arrives on the lane clicked is the session. Null when it can't be (the caller reads from
        /// the click as before); also null when cancelled (cancelled is then true).
        /// </summary>
        private StrokeSession StartSession(PointF head, PointF feet, long click, long start, BackgroundWorker worker, out bool cancelled)
        {
            cancelled = false;
            List<StrokeSession> candidates = new List<StrokeSession>();
            StrokeSession chosen = null;
            StrokeSession clicked = CreateSession(head, feet, click, null, 0, Race.Pool);
            try
            {
                // The lane at the click.
                if (!ReadFrames(clicked, click, worker))
                {
                    cancelled = true;
                    return null;
                }

                if (clicked.Timestamps.Count == 0 || !clicked.Detector.FoundRopes)
                    return null;

                SwimStrokeLane laneC = clicked.Detector.CurrentLane;
                int virtualRope = clicked.Detector.VirtualRope;
                long first = Math.Max(ReadingZone.Start, start - RealSpan(0.3));
                if (first >= click)
                    return null;

                // The camera motion from the start to the click.
                double[] composed = SwimStrokeDetector.Identity();
                bool any = false;
                if (!ReadShared(new List<StrokeSession>(), clicked.Width, clicked.Height, clicked.Step, first, click, worker,
                    h => { composed = SwimStrokeDetector.Multiply(h, composed); any = true; }, null))
                {
                    cancelled = true;
                    return null;
                }

                if (!any)
                    return null;

                double[] back = SwimStrokeDetector.Invert(composed);
                SwimStrokeLane lane0 = MapLaneLines(back, laneC, clicked.Width);

                // The lane taken back and its neighbours, the swimmer in the middle of the image with his size there.
                Size reference = ReadingReader.Info.ReferenceSize;
                double sx = (double)clicked.Width / reference.Width, sy = (double)clicked.Height / reference.Height;
                double hx = head.X * sx, fx = feet.X * sx;
                double xc = 0.5 * (hx + fx);
                double widthC = Math.Max(5, Math.Abs(laneC.Y2(xc) - laneC.Y1(xc)));
                double xm = clicked.Width / 2.0;
                double width0 = Math.Max(5, Math.Abs(lane0.Y2(xm) - lane0.Y1(xm)));
                double length = Math.Abs(hx - fx) * width0 / widthC;
                int direction = hx > fx ? 1 : -1;
                foreach (int k in new[] { 0, -1, 1 })
                {
                    SwimStrokeLane c = lane0;
                    c.B1 += k * width0;
                    c.B2 += k * width0;
                    double y = 0.5 * (c.Y1(xm) + c.Y2(xm));
                    PointF h0 = new PointF((float)((xm + direction * 0.45 * length) / sx), (float)(y / sy));
                    PointF f0 = new PointF((float)((xm - direction * 0.45 * length) / sx), (float)(y / sy));
                    candidates.Add(CreateSession(h0, f0, first, c, virtualRope, Race.Pool));
                }

                OpenCvSharp.Mat lastGray = null;
                if (!ReadShared(candidates, clicked.Width, clicked.Height, clicked.Step, first, click, worker, null, g => lastGray = g))
                {
                    cancelled = true;
                    return null;
                }

                // The one on the lane clicked at the click: close to it, or clearly the closest of the three (the ropes taken back
                // through a zoom of the camera move a little).
                double best = double.MaxValue, second = double.MaxValue;
                foreach (StrokeSession c in candidates)
                {
                    SwimStrokeLane l = c.Detector.CurrentLane;
                    double error = Math.Abs(l.Y1(xc) - laneC.Y1(xc)) + Math.Abs(l.Y2(xc) - laneC.Y2(xc));
                    if (error < best)
                    {
                        second = best;
                        best = error;
                        chosen = c;
                    }
                    else if (error < second)
                    {
                        second = error;
                    }
                }

                bool close = best <= 0.35 * widthC || (best <= 0.5 * widthC && second >= 2 * best);
                if (chosen == null || !close || lastGray == null)
                {
                    if (lastGray != null)
                        lastGray.Dispose();
                    chosen = null;
                    return null;
                }

                // The swimmer is where he was clicked at that frame: the path goes through it.
                int clickIndex = chosen.Timestamps.FindLastIndex(t => t <= click);
                if (clickIndex >= 0)
                    chosen.Detector.Anchor(clickIndex, xc);

                chosen.PreviousGray = lastGray;
                return chosen;
            }
            finally
            {
                clicked.Dispose();
                foreach (StrokeSession c in candidates)
                {
                    if (c != chosen)
                        c.Dispose();
                }
            }
        }

        /// <summary>A lane moved by a homography (each rope line by two of its points).</summary>
        private static SwimStrokeLane MapLaneLines(double[] h, SwimStrokeLane lane, int width)
        {
            SwimStrokeLane mapped = lane;
            MapLine(h, lane.A1, lane.B1, width, out mapped.A1, out mapped.B1);
            MapLine(h, lane.A2, lane.B2, width, out mapped.A2, out mapped.B2);
            return mapped;
        }

        private static void MapLine(double[] h, double a, double b, int width, out double ra, out double rb)
        {
            double x0, y0, x1, y1;
            SwimStrokeDetector.MapPoint(h, 0, b, out x0, out y0);
            SwimStrokeDetector.MapPoint(h, width, a * width + b, out x1, out y1);
            ra = Math.Abs(x1 - x0) > 1e-6 ? (y1 - y0) / (x1 - x0) : a;
            rb = y0 - ra * x0;
        }

        /// <summary>
        /// Background thread: the frames from 'from' to 'target' read once for several sessions that start together (each gets
        /// every frame and its camera motion), or only the camera motion of each frame (no session, 'motion' called with the
        /// homography from the frame before). 'last' gets the grey image of the last frame (for a session that goes on reading).
        /// False if cancelled.
        /// </summary>
        private bool ReadShared(List<StrokeSession> sessions, int width, int height, int step, long from, long target, BackgroundWorker worker,
            Action<double[]> motion, Action<OpenCvSharp.Mat> last)
        {
            VideoReader reader = ReadingReader;
            double span = Math.Max(1, target - from);
            bool completed = true;
            Exception detectorError = null;
            OpenCvSharp.Mat previous = null;

            // A reading in the background stays below the player and the panel (and tells its progress less often).
            bool background = quietThread != null;
            int frames = 0;
            using (BlockingCollection<StrokeFrame> queue = new BlockingCollection<StrokeFrame>(8))
            {
                Task detection = Task.Run(() =>
                {
                    System.Threading.Thread thread = System.Threading.Thread.CurrentThread;
                    System.Threading.ThreadPriority priority = thread.Priority;
                    if (background)
                        thread.Priority = System.Threading.ThreadPriority.BelowNormal;
                    try
                    {
                        foreach (StrokeFrame f in queue.GetConsumingEnumerable())
                        {
                            if (detectorError != null)
                                continue;

                            try
                            {
                                SwimStrokeImage image = new SwimStrokeImage(width, height, f.Pixels);
                                Parallel.ForEach(sessions, s =>
                                {
                                    s.Detector.Add(image, f.Motion);
                                    s.Timestamps.Add(f.Timestamp);
                                });
                            }
                            catch (Exception e)
                            {
                                detectorError = e;
                            }
                        }
                    }
                    finally
                    {
                        thread.Priority = priority;
                    }
                });

                try
                {
                    reader.BeforeFrameEnumeration();
                    long current = reader.Current != null ? reader.Current.Timestamp : from;
                    bool hasMore = reader.MoveTo(current, from);
                    while (detectorError == null)
                    {
                        if (worker.CancellationPending)
                        {
                            completed = false;
                            break;
                        }

                        VideoFrame frame = reader.Current;
                        if (frame == null || frame.Image == null || frame.Timestamp > target)
                            break;

                        using (OpenCvSharp.Mat bgr = SwimStrokeVideo.ToWorking(frame.Image, width, height))
                        {
                            OpenCvSharp.Mat gray = SwimStrokeVideo.HalfGray(bgr);
                            double[] h = previous == null ? null : SwimStrokeVideo.Motion(previous, gray);
                            if (previous != null)
                                previous.Dispose();
                            previous = gray;
                            if (h != null && motion != null)
                                motion(h);
                            if (sessions.Count > 0)
                                queue.Add(new StrokeFrame { Timestamp = frame.Timestamp, Motion = h, Pixels = SwimStrokeVideo.Bytes(bgr) });
                        }

                        if (!background || ++frames % 25 == 0)
                            worker.ReportProgress((int)Math.Max(0, Math.Min(100, 100.0 * (frame.Timestamp - from) / span)));
                        if (!hasMore)
                            break;

                        hasMore = reader.MoveNext(step - 1, true);
                    }
                }
                finally
                {
                    queue.CompleteAdding();
                    detection.Wait();
                }
            }

            if (detectorError != null)
            {
                if (previous != null)
                    previous.Dispose();
                throw new InvalidOperationException("Stroke detector: " + detectorError.Message, detectorError);
            }

            if (completed && last != null && previous != null)
                last(previous);
            else if (previous != null)
                previous.Dispose();
            return completed;
        }

        /// <summary>Background thread: the lengths one after the other. False if cancelled.</summary>
        private bool ReadRace(PointF head, PointF feet, long from, List<LengthRead> lengths, BackgroundWorker worker)
        {
            long lapStart = LapStartTimestamp(from);
            int wall = NextWallDistance(from);

            // Going on after a stop (the swimmer lost after a wall): the length of the click is the one after that wall. A reading
            // in the background neither uses nor changes where the whole race stopped.
            bool background = quietThread != null;
            if (!background && resumeWall > 0 && resumeAt != long.MinValue && from >= resumeAt - RealSpan(1.0) && from <= resumeAt + RealSpan(40))
            {
                wall = resumeWall;
                lapStart = resumeLapStart;
            }

            if (!background)
            {
                resumeWall = -1;
                resumeLapStart = long.MinValue;
                resumeAt = long.MinValue;
            }

            double previousSeconds = 0;
            Size reference = ReadingReader.Info.ReferenceSize;
            SwimStrokeLane? nextLane = null;
            int nextVirtual = 0;

            // After a turn: where the swimmer goes under the water from the wall (first frame of the next reading, px a frame).
            int nextGlideFrames = 0;
            double nextGlideX = double.NaN, nextGlideSpeed = double.NaN;

            // The size of the swimmer clicked and the width of his lane there: his size in the next lengths, where the lane is
            // wider or narrower.
            double clickLength = double.NaN, clickLaneWidth = double.NaN;

            // The swimmer clicked seen from far or close (his length in working px) for the delays of the checkpoints of every length.
            double scale = (double)StrokeWorkWidth / Math.Max(1, reference.Width);
            double clickSwimmer = scale * Math.Sqrt((head.X - feet.X) * (head.X - feet.X) + (head.Y - feet.Y) * (head.Y - feet.Y));
            long clickTimestamp = from;
            while (wall <= Race.Distance)
            {
                // The first length of the race from the start signal (its 15 m and its breakout also when the click comes after
                // them), else from the click.
                StrokeSession s = null;
                if (lengths.Count == 0 && wall == Race.Pool && Race.StartTimestamp.HasValue && lapStart == Race.StartTimestamp.Value &&
                    from > lapStart + RealSpan(1.0))
                {
                    bool cancelled;
                    s = StartSession(head, feet, from, lapStart, worker, out cancelled);
                    if (cancelled)
                        return false;
                }

                if (s == null)
                {
                    s = CreateSession(head, feet, from, nextLane, nextVirtual, wall);
                    if (nextGlideFrames > 0 && !double.IsNaN(nextGlideX) && !double.IsNaN(nextGlideSpeed))
                        s.Detector.Glide(nextGlideFrames, nextGlideX, nextGlideSpeed);
                }

                nextGlideFrames = 0;
                s.Wall = wall;
                s.LapStart = lapStart;
                s.Shortest = ShortestOfLength(wall, previousSeconds);
                s.PreviousSeconds = previousSeconds;

                // The frames up to a little after the wall: a first part (a fast swimmer), then more until the wall is found.
                long target, last;
                ReadingSpan(from, lapStart, wall, previousSeconds, out target, out last);
                SwimStrokeResult result = null;
                LengthRead l = null;
                while (true)
                {
                    if (target <= from || !ReadFrames(s, target, worker))
                    {
                        s.Dispose();
                        return target <= from;
                    }

                    if (s.Timestamps.Count < 10)
                        break;

                    result = s.Detector.Finish(0, s.Timestamps.Count - 1);
                    l = AnalyseLength(s, result, lapStart);
                    if (l == null)
                        break;
                    l.SwimmerLength = clickSwimmer;

                    double found = WallSample(l);
                    bool enough = !double.IsNaN(found) && s.Timestamps.Count - 1 - found >= 1.0 * s.Fps;

                    // The pace of the length gets the swimmer to the wall after the frames read (a stop or the ends of the ropes
                    // seen before the wall: the path left him for still water): the frames up to the wall of the pace first.
                    if (enough && !double.IsNaN(l.PaceWall) && l.PaceWall - found > ReadOnToPace * s.Fps && s.Timestamps.Count - 1 - l.PaceWall < 1.0 * s.Fps)
                        enough = false;
                    if (enough || target >= last || worker.CancellationPending)
                        break;
                    target = Math.Min(last, target + RealSpan(3.0));
                }

                // The reading again without the frames after the camera turns fast a while after the wall (to the scoreboard, a
                // cut): they must not move the path before the wall.
                if (l != null && result != null && SwimSplitDetector.PathAfterWall > 0)
                {
                    double w0 = WallSample(l);
                    int pathEnd = double.IsNaN(w0) ? -1 : SwimSplitDetector.CameraBreak(result, w0 + SwimSplitDetector.PathAfterWall * s.Fps, s.Fps);
                    if (pathEnd > 0 && pathEnd < s.Timestamps.Count - 1)
                    {
                        s.Detector.PathEnd = pathEnd;
                        SwimStrokeResult again = s.Detector.Finish(0, s.Timestamps.Count - 1);
                        LengthRead l2 = AnalyseLength(s, again, lapStart);
                        if (l2 != null)
                        {
                            l2.SwimmerLength = clickSwimmer;
                            result = again;
                            l = l2;
                        }
                    }
                }

                if (l == null || result == null)
                {
                    s.Dispose();
                    break;
                }

                // After a wall, a length without any mark of the ropes: the swimmer was lost (camera turning fast, lane at the
                // far end). The analyst clicks him again a little later and the reading goes on from there.
                if (lengths.Count > 0 && !l.Found.Any(f => f.Cues.Keys.Any(k => k != SwimSplitDetector.CuePace)))
                {
                    if (!background)
                    {
                        resumeWall = wall;
                        resumeLapStart = lapStart;
                        resumeAt = from + RealSpan(2.5);
                    }

                    s.Dispose();
                    break;
                }

                if (lengths.Count == 0)
                    l.ClickIndex = Math.Max(0, s.Timestamps.FindIndex(t => t >= clickTimestamp));
                ReadLengthStrokes(l);
                ReleaseFrames(l);
                lengths.Add(l);
                if (wall >= Race.Distance || worker.CancellationPending)
                    break;

                // The wall: the swimmer turns there; the next length starts from it, the other way, a little after.
                double w = WallSample(l);
                if (double.IsNaN(w) || result.Path == null || result.Path.Length == 0)
                    break;

                int wi = (int)Math.Round(Math.Max(0, Math.Min(result.Path.Length - 1, w)));
                double sx = (double)s.Width / reference.Width, sy = (double)s.Height / reference.Height;
                if (double.IsNaN(clickLength))
                {
                    double hx0 = head.X * sx, hy0 = head.Y * sy, fx0 = feet.X * sx, fy0 = feet.Y * sy;
                    double cx0 = 0.5 * (hx0 + fx0);

                    // The lane at the frame clicked (the reading can start before it, at the start).
                    int clickIndex = Math.Max(0, Math.Min(result.Lanes.Count - 1, s.Timestamps.FindIndex(t => t >= clickTimestamp)));
                    SwimStrokeLane lane0 = result.Lanes[clickIndex];
                    clickLength = Math.Sqrt((hx0 - fx0) * (hx0 - fx0) + (hy0 - fy0) * (hy0 - fy0));
                    clickLaneWidth = Math.Max(5, Math.Abs(lane0.Y2(cx0) - lane0.Y1(cx0)));
                }

                // The swimmer at the wall: the path there, or the wall itself when the path left him before it.
                double x = SwimSplitDetector.PlaceAtWall(result, w, s.Detector.Direction, s.Fps, clickLength / clickLaneWidth);
                SwimStrokeLane lane = result.Lanes[Math.Min(wi, result.Lanes.Count - 1)];
                double y = 0.5 * (lane.Y1(x) + lane.Y2(x));
                double lw = Math.Max(5, Math.Abs(lane.Y2(x) - lane.Y1(x)));
                double length = clickLength * lw / clickLaneWidth;
                int direction = -s.Detector.Direction;
                long wallTs = SessionStamp(s, w);
                previousSeconds = Math.Max(10, Timing.Seconds(wallTs, lapStart));

                head = new PointF((float)((x + direction * 0.45 * length) / sx), (float)(y / sy));
                feet = new PointF((float)((x - direction * 0.45 * length) / sx), (float)(y / sy));
                from = wallTs + RealSpan(0.2);

                // The lane of the next length: the one followed here, at the frame where the next reading starts (the search at
                // the far end of the pool, where the lanes are narrow, could take another one).
                nextLane = null;
                nextVirtual = s.Detector.VirtualRope;
                int startIndex = s.Timestamps.FindIndex(t => t >= from);
                if (startIndex >= 0 && startIndex < result.Lanes.Count)
                    nextLane = result.Lanes[startIndex];

                // Under the water after the turn (not after the dive of the next swimmer of a relay): from the place at the wall
                // moved with the camera to the first frame of the next reading, at the speed of this length.
                double usual = SwimSplitDetector.UsualSpeed(result, s.Detector.Direction);
                if (nextLane.HasValue && !IsDive(wall) && !double.IsNaN(usual) && SwimStrokeDetector.GlideShare > 0)
                {
                    nextGlideX = SwimSplitDetector.MoveWithCamera(result, x, wi, startIndex);
                    double laneWidth = Math.Max(5, Math.Abs(nextLane.Value.Y2(nextGlideX) - nextLane.Value.Y1(nextGlideX)));
                    nextGlideSpeed = direction * usual * laneWidth;
                    double glide = SwimStrokeDetector.GlideShare * SwimSplitDetector.TurnUnderwater(SwimPresets.StyleAt(Race, wall + Race.Pool)) - 0.2;
                    nextGlideFrames = Math.Max(0, (int)Math.Round(glide * s.Fps));
                }

                lapStart = wallTs;
                wall += Race.Pool;
                if (from >= ReadingZone.End)
                    break;
            }

            return true;
        }
        #endregion

        /// <summary>The checkpoint is at this frame: the split of its distance (replacing a different one), the answer to the learning.</summary>
        private void AcceptSplitProposal(SwimProposal p, long timestamp)
        {
            int distance = p.Index;
            SwimMark existing = Race.GetSplit(distance);
            string done;
            if (existing != null && SameFrame(existing.Timestamp, timestamp))
            {
                done = "✓ " + ProposalLabel(p) + ": υπήρχε ήδη σε αυτό το καρέ.";
            }
            else
            {
                if (!Race.StartTimestamp.HasValue)
                    return;

                SwimMark mark = new SwimMark { Type = SwimMarkType.Split, Timestamp = timestamp, Distance = distance };
                if (existing != null)
                    AddReplacing(mark, m => m.Type == SwimMarkType.Split && m.Distance == distance);
                else
                {
                    Race.Marks.Add(mark);
                    undoStack.Add(mark.Id);
                }

                double t = Timing.Seconds(timestamp, Race.StartTimestamp.Value);
                done = string.Format(CultureInfo.InvariantCulture, "✓ Πέρασμα {0}{1}: {2:0.00} s", distance, Race.Unit, t);
                if (distance == Race.Distance)
                    CaptureFinalFrame(timestamp);
            }

            Answer(p, timestamp, true);
            RecombineSplitProposals();
            MoveEventProposals(KindRaceSplit, distance, timestamp);
            Changed(true);
            NextProposal(timestamp, done);
        }
        #endregion
    }
}
