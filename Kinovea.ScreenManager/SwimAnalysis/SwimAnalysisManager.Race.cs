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
                Status(SwimLang.T("Η αυτόματη εκκίνηση από το φως είναι για βίντεο αγώνα (κάμερα στην κερκίδα)."), true);
                return;
            }

            if (!Timing.IsValid)
            {
                Status(SwimLang.T("Δεν είναι γνωστός ο ρυθμός καρέ του βίντεο: η εκκίνηση δεν μπορεί να βρεθεί."), true);
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

            // (2.9.3) The start systems the analyst has confirmed (the tones of the signals of the starts he kept).
            List<SwimStartTone> known = Learning.StartTones(KindRaceStart);

            StartFound result = null;
            Exception error = null;
            bool cancelled = false;
            StartLightRunning = true;
            RefreshProposalsUi();
            player.BeforeExportVideo();
            using (BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true })
            using (FormProgressBar progress = new FormProgressBar(true))
            {
                ProgressCancel cancel = KeepOpenWhileBusy(progress, worker);
                worker.ProgressChanged += (o, e) => progress.Update(e.ProgressPercentage, 100, true);
                worker.DoWork += (o, e) =>
                {
                    e.Result = ReadStart(detector, stamps, from, end, width, height, (BackgroundWorker)o, sound ? ffmpeg : null, path, soundFrom,
                        soundSeconds, tsPerSecond, known);
                };
                worker.RunWorkerCompleted += (o, e) =>
                {
                    error = e.Error;
                    result = e.Error == null ? e.Result as StartFound : null;
                    cancelled = e.Error == null && (cancel.Asked || result == null || !result.Completed);
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
                    Status(SwimLang.T("Η αυτόματη εκκίνηση απέτυχε: ") + error.Message, true);
                    return;
                }

                if (cancelled)
                {
                    startLightCancelled = true;
                    Status(SwimLang.T("Η αναζήτηση της εκκίνησης ακυρώθηκε."), false);
                    return;
                }

                if (result.SoundError != null)
                    log.WarnFormat("Start signal: the sound was not read ({0}).", result.SoundError);

                List<SwimStartCandidate> found = WithLineStarts(result.Candidates, detector, stamps);
                if (found.Count == 0 || stamps.Count == 0)
                {
                    Status(SwimLang.T("Δεν βρέθηκε σήμα εκκίνησης πριν από την κολύμβηση (δεν φαίνεται το φως, δεν ακούγεται το σήμα ή η κάμερα δεν ακολουθεί ") +
                        SwimLang.T("τον αθλητή). Βάλε την εκκίνηση με το κουμπί «Εκκίνηση» στο καρέ του σήματος."), true);
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

        /// <summary>The measures of a start proposal with the tones of its signal heard (2.9.3, SwimLearning.StartTones).</summary>
        internal const string ToneLow = "toneLow", ToneHigh = "toneHigh", ToneSeconds = "toneSeconds", TonePurity = "tonePurity";

        /// <summary>"715 Hz" (a single tone) or "890/1015 Hz" (two tones).</summary>
        private static string ToneText(SwimStartCandidate c)
        {
            return new SwimStartTone { Low = c.ToneLow, High = c.ToneHigh }.ToString();
        }

        /// <summary>The starts that leave the video for the race (MinRaceSeconds); null: all (the distance not known).</summary>
        private Func<SwimStartCandidate, bool> StartFits(List<long> stamps)
        {
            if (Race == null || Race.Distance <= 0 || stamps.Count == 0)
                return null;

            long end = frameServer.VideoReader.WorkingZone.End;
            double need = MinRaceSeconds();
            return c => c.Frame >= 0 && c.Frame < stamps.Count && Timing.Seconds(end, stamps[c.Frame]) >= need;
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
        /// No start found, or none after which the video is long enough for the race (an outdoor pool: no light seen, the signal not
        /// heard over the wind, a sound of the race or of the next one taken for it): the starts seen from the swimmers leaving the
        /// blocks together (SwimStartLight.LineStarts, since 2.8.0) come first, the first in time that leaves the video for the race
        /// (lab 2026-10-19: the 8 videos of an outdoor meet with the start known from the analyst's stopwatch: 4 starts found 2-68 s
        /// off before, 0.01-0.31 s with it; the 102 other verified starts unchanged). They are proposals to check: the pilot asks.
        /// </summary>
        private List<SwimStartCandidate> WithLineStarts(List<SwimStartCandidate> found, SwimStartLight detector, List<long> stamps)
        {
            found = found ?? new List<SwimStartCandidate>();
            if (detector == null || stamps.Count == 0)
                return found;

            long end = frameServer.VideoReader.WorkingZone.End;
            bool known = Race != null && Race.Distance > 0;
            double need = known ? MinRaceSeconds() : 0;
            Func<SwimStartCandidate, bool> fits = c => c.Frame >= 0 && c.Frame < stamps.Count && (!known || Timing.Seconds(end, stamps[c.Frame]) >= need);
            if (found.Count > 0 && (!known || found.Any(fits)))
                return found;

            List<SwimStartCandidate> lines;
            try
            {
                lines = detector.LineStarts().Where(fits).ToList();
            }
            catch (Exception e)
            {
                log.Error("The start from the swimmers leaving the blocks could not be looked for.", e);
                return found;
            }

            if (lines.Count == 0)
                return found;

            log.DebugFormat(CultureInfo.InvariantCulture, "Start: {0} start(s) from the swimmers leaving the blocks (no start found that fits the race).", lines.Count);
            List<SwimStartCandidate> result = new List<SwimStartCandidate> { lines[0] };
            result.AddRange(found);
            result.AddRange(lines.Skip(1));
            return result;
        }

        /// <summary>
        /// Background thread: the frames from the beginning until the start is clear (or 'end'), the sound at the same time (ffmpeg:
        /// null, not read), then the starts from the lights and the signal heard.
        /// </summary>
        private StartFound ReadStart(SwimStartLight detector, List<long> stamps, long from, long end, int width, int height, BackgroundWorker worker,
            string ffmpeg, string path, double soundFrom, double soundSeconds, double tsPerSecond, List<SwimStartTone> known)
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
                double lastToCheck = double.NaN;
                bool lastKnown = false;
                Func<long, bool> enough = ts =>
                {
                    if (!lastKnown)
                    {
                        if (heard == null || !heard.IsCompleted || heard.IsFaulted || heard.IsCanceled)
                            return false;
                        lastToCheck = SwimStartSound.LastToCheck(heard.Result, known);
                        lastKnown = true;
                    }

                    return !double.IsNaN(lastToCheck) && ts > from + (lastToCheck + 8) * tsPerSecond;
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
                found = SwimStartSound.Choose(found, result.Beeps, detector, frameAt, fullSize, SwimStartSound.Delay, known);
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
            Func<SwimStartCandidate, bool> fits = StartFits(stamps);
            double confidence0 = SwimStartLight.Confidence(found, realFps, fits);
            bool ambiguous = SwimStartSound.Ambiguous(found, realFps, fits);
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
                string cue = c.Source == "dive" ? "dive" : c.Source == "line" ? "line" : c.Source == "sound" ? "sound" : "light";
                SwimDetection d = new SwimDetection(KindRaceStart, SwimMarkType.Signal, k + 1) { Confidence = k == 0 ? confidence0 : 0.35 * confidence0 };
                double heardStart = double.IsNaN(c.Beep) ? double.NaN : c.Beep - SwimStartSound.Delay * realFps;
                if (cue == "sound")
                    d.Cue(cue, heardStart, 1.0, 0.03);
                else
                    d.Cue(cue, c.Frame, 1.0, c.Source == "dive" || c.Source == "line" ? 0.1 : 0.02);
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

                // (2.9.3) The tones of the signal heard: the start kept teaches the start system of the pool (SwimLearning.StartTones).
                if (!double.IsNaN(c.ToneLow) && !double.IsNaN(c.ToneHigh))
                {
                    p.Measures[ToneLow] = Math.Round(c.ToneLow, 1);
                    p.Measures[ToneHigh] = Math.Round(c.ToneHigh, 1);
                    if (!double.IsNaN(c.ToneSeconds))
                        p.Measures[ToneSeconds] = Math.Round(c.ToneSeconds, 3);
                    if (!double.IsNaN(c.TonePurity))
                        p.Measures[TonePurity] = Math.Round(c.TonePurity, 3);
                    if (c.KnownTone)
                        p.Measures["knownTone"] = 1;
                }
                made.Add(p);
            }

            if (made.Count == 0)
            {
                Status(SwimLang.T("Δεν βρέθηκε σήμα εκκίνησης."), true);
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
                string what = found[0].Source == "sound" ? SwimLang.T("το σήμα που ακούγεται") : SwimLang.T("το φωτεινό σήμα");
                Status(frames == 0 ? SwimLang.T("💡 Η εκκίνηση που έχεις βάλει είναι στο καρέ που βρέθηκε από ") + what + SwimLang.T(": κρατήθηκε για τη μάθηση.") :
                    string.Format(CultureInfo.InvariantCulture, SwimLang.T("💡 Η εκκίνηση που έχεις βάλει είναι {0:+0;-0} καρέ από αυτήν που βρέθηκε από {1}: κρατήθηκε για τη μάθηση."),
                    frames, what), false);
                return;
            }

            Race.Proposals.AddRange(made);
            Changed(false);
            BeginReview(best);
            if (best.Cues.ContainsKey("line"))
                Status(SwimLang.T("⚠ Δεν βρέθηκε σήμα εκκίνησης που να ταιριάζει με τον αγώνα: η πρόταση είναι λίγο πριν φύγουν όλοι μαζί από τους βατήρες. ") +
                    SwimLang.T("Έλεγξέ τη στο καρέ του σήματος."), true);
            else if (best.Cues.ContainsKey("dive"))
                Status(SwimLang.T("⚠ Δεν φάνηκε το φως της εκκίνησης ούτε ακούστηκε το σήμα: η πρόταση είναι λίγο πριν αρχίσουν να κινούνται οι αθλητές. ") +
                    SwimLang.T("Έλεγξέ τη στο καρέ του σήματος."), true);
            else if (ambiguous)
                Status(SwimLang.T("⚠🔊 Ακούστηκαν δύο σήματα σχεδόν το ίδιο καθαρά: η εκκίνηση μπορεί να είναι στο άλλο. Έλεγξέ τη ") +
                    SwimLang.T("(→: η επόμενη πρόταση)."), true);
            else if (found[0].KnownTone)
                Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("🔊 Η εκκίνηση στο σήμα της πισίνας ({0}), το ίδιο με τις εκκινήσεις που έχεις κρατήσει. Έλεγξέ τη."),
                    ToneText(found[0])), false);
            else if (found[0].Source == "sound")
                Status(SwimLang.T("🔊 Δεν φάνηκε το φως της εκκίνησης: η πρόταση είναι από το σήμα που ακούγεται (λίγο πριν ακουστεί, όσο αργεί ο ήχος να φτάσει ") +
                    SwimLang.T("στην κάμερα). Έλεγξέ τη στο καρέ του φωτός."), false);
            else if (found[0].Source == "light+sound")
                Status(SwimLang.T("💡🔊 Η εκκίνηση στο φως του σήματος, που ακούγεται αμέσως μετά. Έλεγξέ τη."), false);

            // A start after which the video is too short for the race: surely not its start (the signal of the next race?).
            double left = Timing.Seconds(frameServer.VideoReader.WorkingZone.End, best.Timestamp);
            double need = MinRaceSeconds();
            if (Race.Distance > 0 && left < need)
                Status(string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("⚠ Μετά την εκκίνηση που βρέθηκε μένουν μόνο {0:0} s βίντεο, ενώ τα {1} m θέλουν τουλάχιστον {2:0} s: μάλλον δεν είναι η εκκίνηση αυτού του αγώνα. ") +
                    SwimLang.T("Δες τις άλλες προτάσεις (→) ή βάλε την εκκίνηση με το κουμπί «Εκκίνηση» στο καρέ του σήματος."), Math.Max(0, left), Race.Distance, need), true);
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
                HandleCommand(player, frameServer, PlayerScreenCommands.SwimStart, timestamp, false);
            }
            finally
            {
                resolvingProposal = false;
            }

            Answer(p, timestamp, true);
            Changed(false);
            string done = "✓ " + ProposalLabel(p) + SwimLang.T(": t = 0 σε αυτό το καρέ.");

            // The auto pilot waited for this check of the start: it goes on with the whole race.
            if (PilotActive && pilotStep == PilotStep.Start)
            {
                pilotStep = PilotStep.None;
                EndReview(false);
                pilotStartNote = SwimLang.T("την επιβεβαίωσες εσύ");
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
                    Status(done + SwimLang.T("  →  🤖 Όλος ο αγώνας: κλικ στο κεφάλι του αθλητή και μετά στα πόδια του (σε ένα καρέ μετά την έξοδο από το νερό· ") +
                        SwimLang.T("Esc: όχι τώρα)."), false);
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

            /// <summary>(2.9.8) Where the search of the breakout ended: the head at 15 m of the reading (frame), NaN if not read.</summary>
            public double BreakoutEnd = double.NaN;

            /// <summary>Strokes of the length (frames of the reading): from the breakout to a little before the wall.</summary>
            public readonly List<int> Strokes = new List<int>();
            public double Period;

            /// <summary>(2.15.0) The strokes of the length are to be checked (the cycle at an end of its range, or none measured; strokes
            /// left out where there was no stroke signal).</summary>
            public bool StrokesDoubtful;

            /// <summary>Frame of the click on the swimmer in the reading (0: the reading starts there; the first length can start at the start).</summary>
            public int ClickIndex;

            /// <summary>Length of the swimmer clicked (working px, head to feet): a swimmer seen from far has his own delays.</summary>
            public double SwimmerLength = double.NaN;

            /// <summary>Frame of the wall from the pace of the length (also after the frames read; NaN: no pace).</summary>
            public double PaceWall = double.NaN;

            /// <summary>The swimmer clicked: his length in widths of his lane there (the same in every length), NaN if not known.</summary>
            public double BodyLanes = double.NaN;

            /// <summary>(2.9.7) The official time of the wall of the length with its start (frames of the reading), null if none.</summary>
            public SwimOfficialLap Official;
        }

        /// <summary>
        /// (2.9.7) The official time of the wall of a length read (typed from the results) and the start of the length (the start
        /// signal, the official time of the wall before it, else where the reading's length started), frames of its reading; null when
        /// the wall has no official time.
        /// </summary>
        private SwimOfficialLap OfficialLap(StrokeSession s, double lapStart)
        {
            long? end = OfficialWallStamp(s.Wall);
            if (!end.HasValue || s.Timestamps.Count < 2)
                return null;

            int lapFrom = s.Wall - Race.Pool;
            double start = lapStart;
            if (lapFrom > 0)
            {
                long? before = OfficialWallStamp(lapFrom);
                if (before.HasValue)
                    start = SessionIndex(s, before.Value);
            }

            string style = SwimPresets.StyleAt(Race, s.Wall);
            string kind = IsDive(lapFrom) ? "dive" : lapFrom > 0 && SwimPresets.StyleAt(Race, lapFrom) != style ? "change" : "turn";
            return new SwimOfficialLap { Start = start, End = SessionIndex(s, end.Value), Style = style, Kind = kind };
        }

        /// <summary>
        /// (2.9.7) The reading's own wall of a length is OfficialWallGap seconds and more from the official time of the wall: it did not
        /// follow the swimmer to it (gap: own − official, s; 0 when there is no such gap).
        /// </summary>
        private bool OffOfficialWall(LengthRead l, out double gap)
        {
            gap = 0;
            if (l == null || l.Session == null || !SwimSplitDetector.OfficialChecks)
                return false;

            long? official = OfficialWallStamp(l.Session.Wall);
            double own = WallSample(l);
            if (!official.HasValue || double.IsNaN(own))
                return false;

            double g = Timing.Seconds(SessionStampOut(l.Session, own), official.Value);
            if (Math.Abs(g) < SwimSplitDetector.OfficialWallGap)
                return false;

            gap = g;
            return true;
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
            double turnBreakout = double.NaN;
            if (!double.IsNaN(lapStart) && lapStart > -1.5 * s.Fps)
            {
                int from = (int)Math.Ceiling(Math.Max(0, lapStart + (dive ? 1.5 : 1.0) * s.Fps));
                SwimSplitFound f15 = l.Found.FirstOrDefault(f => f.LapDistance == 15);
                double at15 = f15 != null ? SwimSplitDetector.BreakoutSearchEnd(f15.Cues, !dive, style, s.Fps, Race.Pool) : double.NaN;
                int to = !double.IsNaN(at15) ? (int)Math.Round(at15) : from + (int)Math.Round(8 * s.Fps);
                l.Breakout = SwimSplitDetector.BreakoutCues(l.Result, s.Fps, from, to, lapStart);
                l.BreakoutEnd = at15;

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
                if (!dive)
                    turnBreakout = b;
            }

            double wall = GoOnSample(l);
            double margin = StrokeMarginAt(s.Wall, s.Style);
            int last = (int)Math.Round((double.IsNaN(wall) ? n - 1 : wall) - margin * s.Fps);
            last = Math.Min(n - 1, last);
            first = Math.Max(0, Math.Min(n - 1, first));
            if (last <= first + 5)
                return;

            SwimStrokeResult r = s.Detector.Finish(first, last);
            double cut = TurnStrokeCut(l, style, dive, turnBreakout, lapStart, r.Period, s.Fps);
            l.Strokes.AddRange(double.IsNaN(cut) ? r.Strokes : r.Strokes.Where(i => i >= cut));
            l.Period = r.Period;
            // (2.15.0) Said after the reading: the count of these lengths is to be checked.
            l.StrokesDoubtful = r.CycleDoubtful || r.Invented > 0;
            if (l.StrokesDoubtful)
                log.DebugFormat("Strokes of the length to {0}: cycle {1:0.00} s{2}, {3} beats without signal left out.", s.Wall, r.Cycle, r.CycleDoubtful ? " (doubtful)" : "", r.Invented);
        }

        /// <summary>
        /// (2.11.1) After a turn in backstroke, breaststroke and butterfly the first stroke counted is the one after the breakout
        /// stroke: the sample from which the strokes of the length count (the breakout + SwimStrokeDetector.TurnStrokeLead
        /// strokes; without a breakout the wall + TurnStrokeFloor), NaN: all count (the start, freestyle, no period).
        /// </summary>
        private static double TurnStrokeCut(LengthRead l, string style, bool dive, double breakout, double lapStart, double period, double fps)
        {
            if (dive || l.LapFrom <= 0 || !SwimStrokeDetector.TurnStrokeCut(style) || !(period > 0) || fps <= 0)
                return double.NaN;
            if (!double.IsNaN(breakout))
                return breakout + SwimStrokeDetector.TurnStrokeLead * period * fps;
            double floor = SwimStrokeDetector.TurnStrokeFloor(style);
            return double.IsNaN(floor) || double.IsNaN(lapStart) ? double.NaN : lapStart + floor * fps;
        }

        /// <summary>
        /// A length done: the band of each frame goes (about 30 MB a length: a 1500 m keeps only what the proposals, the strokes and
        /// the place of the swimmer need; since 2.8.0 also the camera's displacement at the swimmer and where his head is, for the
        /// ghost and the line of a reference).
        /// </summary>
        private static void ReleaseFrames(LengthRead l)
        {
            SwimStrokeResult r = l.Result;
            r.KeepPathShifts();
            if (double.IsNaN(r.HeadLead) && l.Session.Detector != null)
                r.HeadLead = SwimSplitDetector.HeadLead(r, l.Session.Detector.Direction, l.Session.Fps);

            l.Session.Detector.Release();
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
            return BreakoutCuesDelayed(l.Breakout, style, l.Session.Fps, turn, strokes, change, startUndelayed, l.SwimmerLength, l.BreakoutEnd);
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
        /// searchEnd: the head at 15 m of the reading (frame, NaN if not read).
        /// </summary>
        private static Dictionary<string, double> BreakoutCuesDelayed(Dictionary<string, double> found, string style, double fps, bool turn, bool strokes = false, bool change = false, bool startUndelayed = false, double swimmerLength = double.NaN, double searchEnd = double.NaN)
        {
            Dictionary<string, double> cues = new Dictionary<string, double>();

            // (2.9.8) In breaststroke the first peak of the regular strokes alone ("train"): the pull-out and the kick under the water
            // move the water before the head is out, the other cues found them (SwimSplitDetector.BreakoutTrain). Its delay after the
            // start, a turn and the medley turn from backstroke apart, and learned apart (".t", ".c"). Not when it puts the head out
            // after the head at 15 m (TrainAfter15): the strokes before were not found (the lab: the only two breaststroke breakouts
            // after the 15 m were 1.9 and 3.0 s late, all the others more than 1.5 s before it); the other cues then.
            if (style == "br" && !strokes && SwimSplitDetector.BreastTrain && found.ContainsKey(SwimSplitDetector.CueTrain))
            {
                double at = found[SwimSplitDetector.CueTrain] + SwimSplitDetector.TrainDelay(turn, change) * fps;
                if (double.IsNaN(searchEnd) || at <= searchEnd + SwimSplitDetector.TrainAfter15 * fps)
                {
                    string trainName = SwimSplitDetector.CueTrain + (turn ? ".t" : "") + (turn && change ? ".c" : "");
                    cues[StyleCue(trainName, style)] = at;
                    return cues;
                }
            }

            // By the view of the swimmer since 2026-10-18 (a camcorder near him sees the head out sooner than a phone in the stands).
            double far = SwimSplitDetector.FarShare(swimmerLength);
            foreach (KeyValuePair<string, double> c in found)
            {
                double delay;
                if (c.Key == SwimSplitDetector.CueTrain)
                    continue;
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
                Confidence = SwimSplitDetector.BreakoutConfidence(style, turn, change,
                    cues.Keys.Any(k => k.StartsWith(SwimSplitDetector.CueTrain, StringComparison.Ordinal)) ? Math.Max(2, cues.Count) : cues.Count)
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
        /// checkpoint (marked or proposed) at the speed of the swimming after it, times the share of that speed from the breakout
        /// to the checkpoint for the style and the start or turn (SwimReel.BreakoutSpeedShare). Written only where the analyst has
        /// no value; returns the text for the status (empty if nothing was written).
        /// (2.9.5) It was 12% faster for every style: the distance came out short, by 0.25–0.45 m after the start and up to 1 m
        /// after butterfly and breaststroke turns (medians of 689 breakouts of the analysts' library; 14.0 m for a breakout at
        /// 14.2 m). With the shares the median is within ±0.2 m.
        /// </summary>
        private string EstimateUnderwaterDistance(int wall, long breakout)
        {
            if (Race == null || !Race.StartTimestamp.HasValue || !Timing.IsValid)
                return "";

            bool start = wall == 0;
            SwimTurnManual turn = start ? null : Race.GetTurn(wall, false);
            if ((start && Race.UnderwaterDist.HasValue) || (!start && turn != null && turn.UnderwaterDist.HasValue))
                return "";

            int checkpoint;
            double? implied = ImpliedBreakoutDistance(wall, breakout, out checkpoint);
            if (!implied.HasValue)
                return "";

            // (2.9.5b) Past the most a breakout can be for the style by more than the error of the estimate (SwimReel.MaxBreakout): the
            // swimmer did not come up at this frame; no distance is written and the analyst is told (his mark stays; a breakout of the
            // auto pilot there is not put, PilotBreakout). Up to that error: the most it can be.
            double limit = BreakoutLimit(wall);
            if (implied.Value > limit + SwimReel.EstimateSlack)
                return " · ⚠ " + BreakoutPastText(wall, implied.Value) + SwimLang.T(": δεν γράφτηκε απόσταση (έλεγξε το καρέ της εξόδου, ή μέτρησε με 📍)");
            if (checkpoint < 0)
                return "";

            double distance = Math.Min(implied.Value, limit);
            if (distance < 2 || distance > (checkpoint - wall) * Race.K)
                return "";

            distance = Math.Round(distance / Race.K, 1);
            if (start)
            {
                Race.UnderwaterDist = distance;
                Race.UnderwaterEstimated = true;
            }
            else
            {
                SwimTurnManual written = Race.GetTurn(wall, true);
                written.UnderwaterDist = distance;
                written.UnderwaterEstimated = true;
            }

            return string.Format(CultureInfo.InvariantCulture, SwimLang.T(" · κάτω από το νερό {0:0.0}{1} (εκτίμηση από τον χρόνο ως τα {2}· για ακρίβεια 📍 Μέτρηση)"),
                distance, Race.Unit, checkpoint);
        }

        /// <summary>
        /// (2.9.5b) Where the times put a breakout at this frame after the wall at 'wall' (0: the start), in metres from the wall; null when
        /// they do not tell. Back from the first checkpoint after it in its length (marked or proposed) at the speed of the segment after
        /// that checkpoint, times the share of that speed for the style and the start or turn (SwimReel.BreakoutSpeedShare); 'checkpoint':
        /// that checkpoint. When the times after it give no speed (after the last checkpoint with a time in its length): at least at the
        /// last checkpoint before it ('checkpoint' -1: not an estimate).
        /// </summary>
        private double? ImpliedBreakoutDistance(int wall, long breakout, out int checkpoint)
        {
            checkpoint = -1;
            if (Race == null || !Race.StartTimestamp.HasValue || Timing == null || !Timing.IsValid || Race.Pool <= 0)
                return null;

            // The first two checkpoints after the breakout in this length, marked or proposed, and the last one before it.
            double tb = Timing.Seconds(breakout, Race.StartTimestamp.Value);
            List<Tuple<int, double>> next = new List<Tuple<int, double>>();
            int passed = -1;
            foreach (int d in Race.Checkpoints.Where(c => c > wall && c < wall + Race.Pool).Distinct().OrderBy(c => c))
            {
                SwimMark mark = Race.GetSplit(d);
                SwimProposal p = Race.Proposals.FirstOrDefault(x => x.Kind == KindRaceSplit && x.Index == d);
                long? ts = mark != null ? mark.Timestamp : p != null ? p.Timestamp : (long?)null;
                if (!ts.HasValue)
                    continue;
                double t = Timing.Seconds(ts.Value, Race.StartTimestamp.Value);
                if (t > tb)
                    next.Add(Tuple.Create(d, t));
                else if (next.Count == 0)
                    passed = d;
                if (next.Count == 2)
                    break;
            }

            if (next.Count == 2 && next[1].Item2 > next[0].Item2)
            {
                checkpoint = next[0].Item1;
                double speed = (next[1].Item1 - next[0].Item1) * Race.K / (next[1].Item2 - next[0].Item2);
                bool dive = wall == 0 || (Race.Relay && Race.Distance >= 4 && wall % (Race.Distance / 4) == 0);
                double share = SwimReel.BreakoutSpeedShare(dive, SwimPresets.StyleAt(Race, next[0].Item1));
                return (next[0].Item1 - wall) * Race.K - share * speed * (next[0].Item2 - tb);
            }

            return passed >= 0 ? (passed - wall) * Race.K : (double?)null;
        }

        /// <summary>(2.9.5b) The most a breakout after this wall (0: the start) can be from it (m), by the style of its length.</summary>
        private double BreakoutLimit(int wall)
        {
            return SwimReel.MaxBreakout(SwimPresets.StyleAt(Race, Math.Min(Race.Distance, wall + 15)));
        }

        /// <summary>
        /// (2.9.5b) The times put the breakout at this frame past the most a breakout can be for the style, by more than the error of the
        /// estimate: the swimmer did not come up there. 'meters': where they put it (NaN: they do not tell).
        /// </summary>
        private bool BreakoutPastLimit(int wall, long breakout, out double meters)
        {
            int checkpoint;
            double? implied = ImpliedBreakoutDistance(wall, breakout, out checkpoint);
            meters = implied ?? double.NaN;
            return implied.HasValue && implied.Value > BreakoutLimit(wall) + SwimReel.EstimateSlack;
        }

        /// <summary>"από τους χρόνους η έξοδος μετά την εκκίνηση θα ήταν στα 23.5μ. (το πολύ 20μ. στο πρόσθιο)".</summary>
        private string BreakoutPastText(int wall, double meters)
        {
            string style = SwimPresets.StyleAt(Race, Math.Min(Race.Distance, wall + 15));
            return string.Format(CultureInfo.InvariantCulture, SwimLang.T("από τους χρόνους η έξοδος {0} θα ήταν στα {1:0.0}{2} (το πολύ {3:0.#}{2} {4})"),
                wall == 0 ? SwimLang.T("μετά την εκκίνηση") : string.Format(CultureInfo.InvariantCulture, SwimLang.T("μετά τον τοίχο των {0}{1}"), wall, Race.Unit),
                meters / Race.K, Race.Unit, BreakoutLimit(wall) / Race.K, style == "br" ? SwimLang.T("στο πρόσθιο") : SwimLang.T("με τον κανονισμό των 15μ."));
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

            // (2.11.1) After a turn in backstroke, breaststroke and butterfly the first stroke counted is the one after the breakout
            // stroke (as the reading, TurnStrokeCut): the automatic strokes before the breakout + TurnStrokeLead strokes go, the
            // stroke time from the automatic strokes after the breakout.
            if (wall > 0 && !IsDive(wall) && SwimStrokeDetector.TurnStrokeCut(SwimPresets.StyleAt(Race, Math.Min(Race.Distance, wall + 15))))
            {
                List<long> after = Race.Marks.Where(m => m.Type == SwimMarkType.Stroke && m.Batch != null && m.Timestamp > breakout - RealSpan(0.4)
                    && m.Timestamp < breakout + RealSpan(15)).Select(m => m.Timestamp).OrderBy(t => t).ToList();
                List<long> gaps = new List<long>();
                for (int i = 1; i < after.Count; i++)
                    gaps.Add(after[i] - after[i - 1]);
                if (gaps.Count >= 3)
                {
                    gaps.Sort();
                    long period = gaps[gaps.Count / 2];
                    to = Math.Max(to, breakout + (long)Math.Round(SwimStrokeDetector.TurnStrokeLead * period));
                }
            }

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
            double wall = GoOnSample(l);
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
                if (kept.Any(k => Math.Abs(k - t) < near) || s.Removed.Any(r => Math.Abs(r - t) < near) || Race.StrokeRemovedNear(t, near))
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
                Status(SwimLang.T("Αυτόματα περάσματα: μόνο σε πισίνα 50 ή 25 (σημάδια των σχοινιών)."), true);
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
                // (2.9.7) The official time of the wall of the length names and checks its checkpoints.
                l.Official = OfficialLap(s, lapStart);
                bool byOfficial = SwimSplitDetector.OfficialUsable(l.Official, Race.Pool, s.Fps);
                l.Found = SwimSplitDetector.Detect(result, s.Detector.Direction, s.Fps, lapStart, Race.Pool, byOfficial ? l.Official : null);
                SwimSplitDetector.AddFifteenDelay(l.Found, s.Fps, Race.Pool, l.LapFrom > 0 && !IsDive(l.LapFrom), SwimPresets.StyleAt(Race, s.Wall));
                double last = l.Found.Count > 0 ? l.Found.Max(f => f.Cues.Values.Max()) : double.NaN;
                l.Wall = SwimSplitDetector.WallCues(result, s.Detector.Direction, s.Fps, last, lapStart, Race.Pool, s.Shortest);

                // Checkpoints far from the time of the length (the one before it, the wall seen) go (with the official times: these
                // did it already).
                int lapFrom = s.Wall - Race.Pool;
                bool sameStyle = lapFrom > 0 && SwimPresets.StyleAt(Race, s.Wall) == SwimPresets.StyleAt(Race, lapFrom) && !IsDive(lapFrom);
                int foundBefore = l.Found.Count;
                if (!byOfficial)
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

                // The ones of the pace far from the official times go, and the ones still not found are put where the official times
                // put them (the analyst checks them).
                if (byOfficial)
                {
                    l.Found.RemoveAll(f => f.Cues.Count > 0 && f.Cues.Keys.All(k => k == SwimSplitDetector.CuePace) &&
                        !SwimSplitDetector.FitsOfficial(l.Official, s.Fps, f.LapDistance, f.Cues[SwimSplitDetector.CuePace]));
                    SwimSplitDetector.AddOfficialPace(l.Found, l.Official, s.Fps, Race.Pool, n);
                }
            }
            catch (Exception e)
            {
                log.Error("Automatic checkpoints failed.", e);
                return null;
            }

            return l;
        }

        /// <summary>Timestamp at a frame index of a reading, before or after its frames by the average step (see SessionIndex).</summary>
        private static long SessionStampOut(StrokeSession s, double index)
        {
            int n = s.Timestamps.Count;
            if (n < 2 || double.IsNaN(index) || (index >= 0 && index <= n - 1))
                return SessionStamp(s, double.IsNaN(index) ? 0 : index);

            double step = (s.Timestamps[n - 1] - s.Timestamps[0]) / (double)(n - 1);
            return (long)Math.Round(index < 0 ? s.Timestamps[0] + index * step : s.Timestamps[n - 1] + (index - (n - 1)) * step);
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
            // (A swimmer of unknown size is not taken as seen from far here.)
            bool farView = !double.IsNaN(l.SwimmerLength) && l.SwimmerLength > 0 && SwimSplitDetector.FarShare(l.SwimmerLength) >= 0.5;
            HashSet<int> farPace = new HashSet<int>();
            foreach (SwimSplitFound f in l.Found)
            {
                Dictionary<string, double> cues = new Dictionary<string, double>();
                // The marks of one rope and the quarter lines have their delay by distance since 2026-10-16 (".6", 50 m pools).
                foreach (KeyValuePair<string, double> c in f.Cues)
                {
                    string version = Race.Pool == 50 && SwimSplitDetector.HasMarkDelay(c.Key) ? ".6" : "";
                    cues[StyleCue(c.Key + PriorVersion(c.Key, style) + version + view, style)] = c.Value + delay;
                }
                // The confidence shown as measured on the analysts' races (cues apart, the 45 m; since 2026-10-18; the pace alone of
                // a swimmer seen from far as doubtful since 2026-10-19).
                items.Add(Tuple.Create(l.LapFrom + f.LapDistance, cues, SwimSplitDetector.CheckpointConfidence(f, s.Fps, Race.Pool, farView)));
                if (SwimSplitDetector.FarPaceOnly(f, s.Fps, Race.Pool, farView))
                    farPace.Add(l.LapFrom + f.LapDistance);
            }

            if (l.Wall.Count > 0)
                items.Add(Tuple.Create(s.Wall, WallCuesOfKind(l, true), SwimSplitDetector.WallConfidence(l.Wall.Keys)));

            // The official time of the wall (typed from the results, since 2026-10-19): the wall at it and, in a 50 m pool, the 45 m from
            // it and the 35 m (the analyst's, else the one proposed). These have no cues: they teach nothing and the learning does
            // not move them. Not for the learning in the background (all): its answers are those of the cues.
            long? officialWall = all ? null : OfficialWallStamp(s.Wall);
            int lastDistance = officialWall.HasValue && Race.Pool == 50 && s.Wall - 5 > l.LapFrom ? s.Wall - 5 : -1;
            SwimMark analyst35 = lastDistance > 0 ? Race.GetSplit(s.Wall - 15) : null;
            long? before = analyst35 != null && analyst35.Pilot == null ? (long?)analyst35.Timestamp : null;
            if (lastDistance > 0 && !items.Any(x => x.Item1 == lastDistance))
            {
                int at = items.FindIndex(x => x.Item1 > lastDistance);
                items.Insert(at >= 0 ? at : items.Count, Tuple.Create(lastDistance, new Dictionary<string, double>(), 0.0));
            }

            if (officialWall.HasValue && !items.Any(x => x.Item1 == s.Wall))
                items.Add(Tuple.Create(s.Wall, new Dictionary<string, double>(), 0.0));

            foreach (Tuple<int, Dictionary<string, double>, double> item in items)
            {
                int distance = item.Item1;

                // What the reading itself found (null: nothing).
                SwimProposal read = null;
                if (item.Item2.Count > 0)
                {
                    SwimDetection d = new SwimDetection(KindRaceSplit, SwimMarkType.Split, distance) { Confidence = item.Item3 };
                    foreach (KeyValuePair<string, double> c in item.Item2)
                    {
                        double weight, spread;
                        CueWeight(c.Key, out weight, out spread);
                        d.Cue(c.Key, c.Value, weight, spread);
                    }

                    double confidence;
                    double sample = SwimProposalMath.Combine(d, learning, video, s.Fps, out confidence);
                    if (!double.IsNaN(sample))
                    {
                        read = new SwimProposal
                        {
                            Type = SwimMarkType.Split,
                            Kind = KindRaceSplit,
                            Timestamp = SessionStamp(s, sample),
                            Confidence = Math.Round(confidence, 2),
                            Index = distance,
                            Batch = batch,
                            FarPace = farPace.Contains(distance)
                        };

                        foreach (KeyValuePair<string, double> c in item.Item2)
                            read.Cues[c.Key] = SessionStamp(s, c.Value);
                    }
                }

                // At the official time instead (what the reading found kept with it, for the learning and for when it goes).
                SwimProposal p = read;
                if (officialWall.HasValue && distance == s.Wall)
                {
                    p = OfficialSplitProposal(distance, officialWall.Value, SwimSplitDetector.OfficialConfidence, batch, read);
                }
                else if (distance == lastDistance && before.HasValue)
                {
                    string kind = WallKind(s.Wall);
                    long from = (long)Math.Round(SwimSplitDetector.LastFromWall(officialWall.Value, before.Value, kind, style));
                    p = OfficialSplitProposal(distance, from, SwimSplitDetector.OfficialLastConfidence, batch, read);
                }

                if (p == null)
                    continue;
                if (lastDistance > 0 && distance == s.Wall - 15 && !before.HasValue)
                    before = p.Timestamp;
                if (!Race.Checkpoints.Contains(distance))
                    continue;

                // Already marked by the analyst (an analysis made before): his frame is the answer to what the reading found, it
                // teaches without asking. (All of them for the reading in the background: its caller teaches with the marks. A
                // checkpoint of the auto pilot not checked yet is proposed again.)
                SwimMark marked = all ? null : Race.GetSplit(distance);
                if (marked != null && marked.Pilot != null)
                    marked = null;
                if (marked != null)
                {
                    if (read != null && Math.Abs(Timing.Seconds(marked.Timestamp, read.Timestamp)) <= 1.5)
                    {
                        Race.Proposals.Add(read);
                        Answer(read, marked.Timestamp, true);
                        learnedFromMarks++;
                    }

                    continue;
                }

                made.Add(p);
            }

            return made;
        }

        /// <summary>
        /// A checkpoint proposed at an official time (of a wall, the 45 m from it) without cues: the learning does not move it and its
        /// answer teaches what the reading itself found there (read, null: nothing).
        /// </summary>
        private static SwimProposal OfficialSplitProposal(int distance, long timestamp, double confidence, string batch, SwimProposal read)
        {
            return new SwimProposal
            {
                Type = SwimMarkType.Split,
                Kind = KindRaceSplit,
                Timestamp = timestamp,
                Confidence = confidence,
                Index = distance,
                Batch = batch,
                Official = true,
                Read = SwimProposalRead.Of(read)
            };
        }

        /// <summary>The proposal as the reading made it before an official time stood in its place (Read), for the learning.</summary>
        private static SwimProposal ReadOf(SwimProposal p)
        {
            return new SwimProposal
            {
                Id = p.Id,
                Type = p.Type,
                Kind = p.Kind,
                Index = p.Index,
                Batch = p.Batch,
                Measures = p.Measures,
                Timestamp = p.Read.Timestamp,
                Confidence = p.Read.Confidence,
                Cues = new Dictionary<string, long>(p.Read.Cues)
            };
        }

        /// <summary>
        /// The timestamp of the official time of a wall (Race.Official, typed from the results; since 2026-10-19): the start of the
        /// race and the official time after it. Null: none typed for that wall, or no start.
        /// </summary>
        private long? OfficialWallStamp(int wall)
        {
            double seconds;
            if (Race == null || !Race.StartTimestamp.HasValue || !Timing.IsValid || !Race.OfficialTimes().TryGetValue(wall, out seconds))
                return null;
            return Race.StartTimestamp.Value + RealSpan(seconds);
        }

        /// <summary>A change of the official times, for «Αναίρεση»: the times before it and how to put back what it moved.</summary>
        private sealed class OfficialUndo
        {
            public List<double> Official;
            public double? Reaction;
            public readonly List<Action> Back = new List<Action>();

            /// <summary>The race's details as they were before the results of the web filled them (athlete, lane, day…); null: none.</summary>
            public Action Details;

            /// <summary>The checkpoints it moved (their push off follows them back).</summary>
            public readonly HashSet<int> Distances = new HashSet<int>();
        }

        /// <summary>The changes of the official times in the undo stack, by their id there ("official:…"; since 2026-10-19).</summary>
        private readonly Dictionary<string, OfficialUndo> officialUndo = new Dictionary<string, OfficialUndo>();
        private const string OfficialUndoPrefix = "official:";

        /// <summary>
        /// The official times typed by the analyst (SwimRace.Official, since 2026-10-19): the analysis shows them (SwimCalculator), and
        /// the checkpoints he has not made his go to them: the marks of the pilot not checked yet and the proposals still waiting at
        /// the walls with an official time, and before them, in a 50 m pool, the 45 m from it and the 35 m. Where an official time is
        /// taken away they go back to what the reading found; «Αναίρεση» puts all back as it was. Null or empty: none.
        /// </summary>
        public void OfficialChanged(List<double> official)
        {
            // (Times taken away: the official reaction time of the results goes with them; else it stays theirs.)
            if (Race != null)
                OfficialChanged(official, official == null || official.Count == 0 ? null : Race.OfficialReaction);
        }

        /// <summary>
        /// The official times and the official reaction time (of the results on the web, since 2026-10-19; null: none): see
        /// OfficialChanged; the release from the block waiting for the check goes to the reaction time.
        /// </summary>
        public void OfficialChanged(List<double> official, double? reaction, Action detailsBack = null)
        {
            if (Race == null)
                return;

            SwimProposal current = CurrentProposal;
            long? currentBefore = current != null ? (long?)current.Timestamp : null;
            OfficialUndo undo = new OfficialUndo
            {
                Official = Race.Official == null ? null : new List<double>(Race.Official),
                Reaction = Race.OfficialReaction,
                Details = detailsBack
            };
            Race.Official = official == null || official.Count == 0 ? null : new List<double>(official);
            Race.OfficialReaction = reaction.HasValue && reaction.Value > 0 ? reaction : null;
            int moved = ApplyOfficial(undo);
            PushOfficialUndo(undo);
            AfterOfficial(undo.Distances.Contains(Race.Distance), current, currentBefore);

            string movedText = moved == 1 ? SwimLang.T("1 σημάδι που δεν είχες ελέγξει") : moved.ToString(CultureInfo.InvariantCulture) + SwimLang.T(" σημάδια που δεν είχες ελέγξει");
            string warning = Race.Official == null ? null : SwimRace.OfficialWarning(Race.Official, Race.Distance, Race.K, Race.Unit);
            if (Race.Official == null)
                Status(SwimLang.T("Οι επίσημοι χρόνοι αφαιρέθηκαν: η ανάλυση δείχνει ξανά τους χρόνους των σημαδιών") +
                    (moved > 0 ? (SwimLang.English ? "; " : "· ") + movedText + SwimLang.T(" γύρισαν εκεί που τα βρήκε η ανάγνωση.") : ".") + SwimLang.T(" «Αναίρεση»: πίσω."), false);
            else if (!Race.StartTimestamp.HasValue)
                Status(SwimLang.T("Οι επίσημοι χρόνοι κρατήθηκαν· μπαίνουν στα σημάδια όταν οριστεί η εκκίνηση."), false);
            else
                Status((moved > 0
                    ? SwimLang.T("Οι επίσημοι χρόνοι μπήκαν στην ανάλυση· ") + movedText + SwimLang.T(" πήγαν σε αυτούς («Αναίρεση»: πίσω).")
                    : SwimLang.T("Οι επίσημοι χρόνοι μπήκαν στην ανάλυση. Τα σημάδια που έχεις ελέγξει μένουν όπου είναι (η διαφορά φαίνεται στο «Τερματισμός»).")) +
                    (warning != null ? " ⚠ " + warning + "." : ""), warning != null);
        }

        /// <summary>«Αναίρεση» of a change of the official times (its id in the undo stack): the times before it, the checkpoints back.</summary>
        private bool UndoOfficial(string id)
        {
            OfficialUndo undo;
            if (Race == null || id == null || !officialUndo.TryGetValue(id, out undo))
                return false;

            officialUndo.Remove(id);
            SwimProposal current = CurrentProposal;
            long? currentBefore = current != null ? (long?)current.Timestamp : null;
            Race.Official = undo.Official == null ? null : new List<double>(undo.Official);
            Race.OfficialReaction = undo.Reaction;
            for (int i = undo.Back.Count - 1; i >= 0; i--)
                undo.Back[i]();
            foreach (int distance in undo.Distances)
                FollowWall(distance);

            // The race's details the results filled back as they were (athlete, lane, day, round, competition, link).
            if (undo.Details != null)
            {
                undo.Details();
                EnsureReference();
                RefreshPbCandidates();
                if (form != null)
                    form.RefreshAll();
            }

            // The times restored placed again in the race as it is now (a reading since, a start moved since); none: what a reading put
            // at the times since goes back to its own.
            OfficialUndo again = new OfficialUndo();
            if (Race.Official == null || OfficialPlaceable())
                ApplyOfficial(again);
            AfterOfficial(undo.Distances.Contains(Race.Distance) || again.Distances.Contains(Race.Distance), current, currentBefore);
            Status(SwimLang.T("Αναιρέθηκε η αλλαγή των επίσημων χρόνων") + (Race.Official == null ? SwimLang.T(": χωρίς επίσημους χρόνους.") : ": " + SwimRace.FormatOfficial(Race.Official) + "."), false);
            return true;
        }

        /// <summary>
        /// The checkpoints the analyst has not made his at the official times as they are now (see OfficialChanged): back to what the
        /// reading found where there is none any more, at it where there is one. Returns how many moved (undo: how to put them back).
        /// </summary>
        private int ApplyOfficial(OfficialUndo undo)
        {
            if (Race == null)
                return 0;

            // Without the start (or the timing of the video) they cannot be placed: what is at them stays until the start is set (the
            // panel says so). Taken away, the checkpoints at them go back to what the reading found.
            bool placeable = Race.StartTimestamp.HasValue && Timing.IsValid;
            if (Race.Official != null && !placeable)
                return 0;

            Dictionary<int, double> official = placeable ? Race.OfficialTimes() : new Dictionary<int, double>();
            int moved = 0;
            foreach (int distance in OfficialDistances().OrderBy(d => d).ToList())
            {
                if (!official.ContainsKey(OfficialWallOf(distance)) && RestoreSplit(distance, undo))
                    moved++;
            }

            foreach (KeyValuePair<int, double> o in official.OrderBy(x => x.Key))
            {
                long wall = Race.StartTimestamp.Value + RealSpan(o.Value);
                if (MoveSplitTo(o.Key, wall, SwimSplitDetector.OfficialConfidence, undo))
                    moved++;

                long? before = Race.Pool == 50 && o.Key % 50 == 0 ? SplitStamp(o.Key - 15) : null;
                if (before.HasValue)
                {
                    long from = (long)Math.Round(SwimSplitDetector.LastFromWall(wall, before.Value, WallKind(o.Key), SwimPresets.StyleAt(Race, o.Key)));
                    if (MoveSplitTo(o.Key - 5, from, SwimSplitDetector.OfficialLastConfidence, undo))
                        moved++;
                }
            }

            // The official reaction time: the release from the block still waiting at it, or back to its usual time without it (the
            // entry after it follows).
            long? release = OfficialReleaseStamp();
            foreach (SwimProposal p in Race.Proposals.Where(x => x.Kind == KindRaceBlock).ToList())
            {
                if (release.HasValue && (!p.Official || p.Timestamp != release.Value))
                {
                    RememberEntry(undo);
                    SetOfficialProposal(p, release.Value, SwimSplitDetector.OfficialConfidence, undo);
                    MoveEventProposals(KindRaceBlock, 0, release.Value);
                    moved++;
                }
                else if (!release.HasValue && p.Official)
                {
                    RememberEntry(undo);
                    Remember(undo, p);
                    if (p.Read != null)
                    {
                        p.Timestamp = p.Read.Timestamp;
                        p.Confidence = p.Read.Confidence;
                        p.Cues = new Dictionary<string, long>(p.Read.Cues);
                        p.Read = null;
                        p.Official = false;
                        MoveEventProposals(KindRaceBlock, 0, p.Timestamp);
                    }
                    else
                    {
                        SwimProposal gone = p;
                        Race.Proposals.Remove(gone);
                        undo.Back.Add(() =>
                        {
                            if (!Race.Proposals.Any(x => x == gone || x.Kind == KindRaceBlock))
                                Race.Proposals.Add(gone);
                        });
                    }

                    moved++;
                }
            }

            foreach (int distance in undo.Distances)
                FollowWall(distance);
            return moved;
        }

        /// <summary>The release from the block at the official reaction time (Race.OfficialReaction; null: none, or no start).</summary>
        private long? OfficialReleaseStamp()
        {
            if (Race == null || !Race.StartTimestamp.HasValue || !Timing.IsValid || !Race.OfficialReaction.HasValue || Race.OfficialReaction.Value <= 0)
                return null;
            return Race.StartTimestamp.Value + RealSpan(Race.OfficialReaction.Value);
        }

        /// <summary>A proposal at an official time: no cues, what it had kept the first time (Read, null: nothing).</summary>
        private static void SetOfficialProposal(SwimProposal p, long timestamp, double confidence, OfficialUndo undo)
        {
            if (undo != null)
                Remember(undo, p);
            if (!p.Official)
            {
                p.Read = p.Cues.Count > 0 ? SwimProposalRead.Of(p) : null;
                p.Official = true;
            }

            p.Timestamp = timestamp;
            p.Confidence = confidence;
            p.Cues.Clear();
        }

        /// <summary>The entry waiting for the check before the release from the block moves it (for «Αναίρεση»).</summary>
        private void RememberEntry(OfficialUndo undo)
        {
            foreach (SwimProposal e in Race.Proposals.Where(x => x.Kind == KindRaceEntry))
                Remember(undo, e);
        }

        /// <summary>
        /// After the official times changed (typed, taken away, undone): the analysis again; the final frame of the report taken again
        /// (it shows the times; see RecaptureFinalFrame); the check shows its proposal where it is now when it moved; else the video
        /// stays where it was.
        /// </summary>
        private void AfterOfficial(bool finishMoved, SwimProposal current, long? currentBefore)
        {
            Recompute();
            bool currentMoved = current != null && Race.Proposals.Contains(current) && currentBefore.HasValue && current.Timestamp != currentBefore.Value;
            RecaptureFinalFrame(finishMoved, !currentMoved);

            // The check shows its proposal where it is now, when it moved.
            if (currentMoved && HasVideo)
                SeekNearest(current.Timestamp);

            Changed(true);
            RefreshProposalsUi();
            InvalidatePlayer();
            if (form != null)
                form.RefreshManualFields();
        }

        /// <summary>
        /// The final frame of the report taken again (it shows the official times): at the finish, or at the frame the analyst chose
        /// for it («📷 Από αυτό το καρέ») when the finish did not move. back: the video back where it was after it.
        /// </summary>
        private void RecaptureFinalFrame(bool finishMoved, bool back)
        {
            SwimMark end = Race.GetSplit(Race.Distance);
            if (!HasVideo || end == null || Race.IsTraining || !Race.StartTimestamp.HasValue)
                return;

            bool chosen = !finishMoved && Race.FinalFrame != null && Race.FinalFrame.Chosen;
            long at = chosen ? Race.FinalFrame.Timestamp : end.Timestamp;
            long was = player.CurrentTimestamp;
            SeekNearest(at);
            CaptureFinalFrame(at, chosen);
            if (back)
                SeekTo(was);
        }

        /// <summary>A change of the official times on the undo stack (when it changed something or the times).</summary>
        private void PushOfficialUndo(OfficialUndo undo)
        {
            string id = OfficialUndoPrefix + Guid.NewGuid().ToString("N");
            officialUndo[id] = undo;
            undoStack.Add(id);
        }

        /// <summary>
        /// The official times placed again in the race as it is now (a new start, other race details): the checkpoints not checked
        /// move with them. Not when the times do not fit the race any more (the panel says so): nothing moves or goes then.
        /// </summary>
        private void ReapplyOfficial()
        {
            if (!OfficialPlaceable())
                return;

            // The finish moved with them: the final frame of the report there.
            OfficialUndo again = new OfficialUndo();
            ApplyOfficial(again);
            if (again.Distances.Contains(Race.Distance))
            {
                Recompute();
                RecaptureFinalFrame(true, true);
            }
        }

        /// <summary>
        /// The race has official times that can be placed in it as it is now: they fit it (distance, pool) and the start is set. Else
        /// nothing at them moves or goes (a start taken away to be marked again, a distance being changed).
        /// </summary>
        private bool OfficialPlaceable()
        {
            return Race != null && Race.Official != null && Race.StartTimestamp.HasValue && Timing.IsValid &&
                SwimRace.OfficialProblem(Race.Official, Race.Distance, Race.Pool, Race.K, Race.Unit) == null;
        }

        /// <summary>
        /// A reading of the video with its progress window runs (the race, the start light, the skeleton). Not the one in the
        /// background: it reads a copy of the race, and the panel stays in use meanwhile.
        /// </summary>
        public bool ReadingBusy
        {
            get { return AutoStrokesRunning || StartLightRunning || SkeletonRunning; }
        }

        /// <summary>The checkpoints at an official time now: proposals, the pilot's marks.</summary>
        private IEnumerable<int> OfficialDistances()
        {
            return Race.Proposals.Where(p => p.Kind == KindRaceSplit && p.Official).Select(p => p.Index)
                .Concat(Race.Marks.Where(m => m.Type == SwimMarkType.Split && m.Pilot != null && m.Pilot.Official).Select(m => m.Distance))
                .Distinct();
        }

        /// <summary>The wall whose official time places a checkpoint: the wall itself, or the one 5 m after the 45 m of a 50 m pool (-1: none).</summary>
        private int OfficialWallOf(int distance)
        {
            if (Race.Pool > 0 && distance % Race.Pool == 0)
                return distance;
            return Race.Pool == 50 && distance % 50 == 45 ? distance + 5 : -1;
        }

        /// <summary>
        /// A checkpoint the analyst has not made his (the pilot's mark, the proposals waiting) at this frame, with no cues (the learning
        /// does not move it, the answer teaches what the reading found, kept: Read). False when nothing moved (none there, the
        /// analyst's own mark).
        /// </summary>
        private bool MoveSplitTo(int distance, long timestamp, double confidence, OfficialUndo undo)
        {
            SwimMark mark = Race.GetSplit(distance);
            if (mark != null && mark.Pilot == null)
                return false;

            bool moved = false;
            if (mark != null && (mark.Timestamp != timestamp || !mark.Pilot.Official))
            {
                Remember(undo, mark);
                if (!mark.Pilot.Official)
                {
                    mark.Pilot.Read = mark.Timestamp;
                    mark.Pilot.Official = true;
                }

                moved |= mark.Timestamp != timestamp;
                mark.Timestamp = timestamp;
            }

            foreach (SwimProposal p in Race.Proposals.Where(x => x.Kind == KindRaceSplit && x.Index == distance).ToList())
            {
                if (p.Official && p.Timestamp == timestamp && p.Cues.Count == 0 && Math.Abs(p.Confidence - confidence) < 1e-9)
                    continue;

                Remember(undo, p);
                if (!p.Official)
                {
                    p.Read = SwimProposalRead.Of(p);
                    p.Official = true;
                }

                moved |= p.Timestamp != timestamp;
                p.Timestamp = timestamp;
                p.Confidence = confidence;
                p.Cues.Clear();
            }

            if (moved)
                undo.Distances.Add(distance);
            return moved;
        }

        /// <summary>
        /// A checkpoint whose official time was taken away back to what the reading found (Read); one there only from the official time
        /// goes (its proposal, the pilot's mark made from it). True when something changed.
        /// </summary>
        private bool RestoreSplit(int distance, OfficialUndo undo)
        {
            bool changed = false;
            foreach (SwimProposal p in Race.Proposals.Where(x => x.Kind == KindRaceSplit && x.Index == distance && x.Official).ToList())
            {
                Remember(undo, p);
                if (p.Read != null)
                {
                    p.Timestamp = p.Read.Timestamp;
                    p.Confidence = p.Read.Confidence;
                    p.Cues = new Dictionary<string, long>(p.Read.Cues);
                    p.Read = null;
                    p.Official = false;
                }
                else
                {
                    SwimProposal gone = p;
                    Race.Proposals.Remove(gone);
                    undo.Back.Add(() =>
                    {
                        if (!Race.Proposals.Any(x => x == gone || (x.Kind == KindRaceSplit && x.Index == gone.Index)))
                            Race.Proposals.Add(gone);
                    });
                }

                changed = true;
            }

            SwimMark mark = Race.GetSplit(distance);
            if (mark != null && mark.Pilot != null && mark.Pilot.Official)
            {
                Remember(undo, mark);
                if (mark.Pilot.Read.HasValue)
                {
                    mark.Timestamp = mark.Pilot.Read.Value;
                    mark.Pilot.Read = null;
                    mark.Pilot.Official = false;
                }
                else
                {
                    // (The final frame of the report of that finish goes with it, as with Delete on it; taken again with the finish.)
                    SwimMark gone = mark;
                    SwimSnapshot final = Race.FinalFrame;
                    int undoAt = undoStack.IndexOf(gone.Id);
                    double strokes;
                    bool hadStrokes = Race.StrokeCounts.TryGetValue(distance, out strokes);
                    Race.Marks.Remove(gone);
                    undoStack.Remove(gone.Id);
                    Race.StrokeCounts.Remove(distance);
                    if (distance == Race.Distance && final != null && SameFrame(final.Timestamp, gone.Timestamp))
                        Race.FinalFrame = null;
                    undo.Back.Add(() =>
                    {
                        if (Race.GetSplit(gone.Distance) != null)
                            return;
                        Race.Marks.Add(gone);
                        if (undoAt >= 0 && !undoStack.Contains(gone.Id))
                            undoStack.Insert(Math.Min(undoAt, undoStack.Count), gone.Id);
                        if (hadStrokes && !Race.StrokeCounts.ContainsKey(gone.Distance))
                            Race.StrokeCounts[gone.Distance] = strokes;
                        if (Race.FinalFrame == null)
                            Race.FinalFrame = final;
                    });
                }

                changed = true;
            }

            if (changed)
                undo.Distances.Add(distance);
            return changed;
        }

        /// <summary>The state of a proposal before an official time moves it (for «Αναίρεση»).</summary>
        private static void Remember(OfficialUndo undo, SwimProposal p)
        {
            long timestamp = p.Timestamp;
            double confidence = p.Confidence;
            Dictionary<string, long> cues = new Dictionary<string, long>(p.Cues);
            bool official = p.Official;
            SwimProposalRead read = p.Read;
            undo.Back.Add(() =>
            {
                p.Timestamp = timestamp;
                p.Confidence = confidence;
                p.Cues = new Dictionary<string, long>(cues);
                p.Official = official;
                p.Read = read;
            });
        }

        /// <summary>The frame of a pilot's mark before an official time moves it (for «Αναίρεση»: only while it is still the pilot's).</summary>
        private static void Remember(OfficialUndo undo, SwimMark m)
        {
            long timestamp = m.Timestamp;
            SwimPilotMark pilot = m.Pilot;
            long? read = pilot != null ? pilot.Read : null;
            bool official = pilot != null && pilot.Official;
            undo.Back.Add(() =>
            {
                if (pilot == null || m.Pilot != pilot)
                    return;
                m.Timestamp = timestamp;
                pilot.Read = read;
                pilot.Official = official;
            });
        }

        /// <summary>The push off waiting after a turn follows its wall where it is now (as when the wall is kept).</summary>
        private void FollowWall(int distance)
        {
            if (Race == null || Race.Pool <= 0 || distance <= 0 || distance >= Race.Distance || distance % Race.Pool != 0)
                return;

            long? wall = SplitStamp(distance);
            if (wall.HasValue)
                MoveEventProposals(KindRaceSplit, distance, wall.Value);
        }

        /// <summary>The frame of a checkpoint: its mark, else its proposal still waiting (null: none).</summary>
        /// <summary>
        /// (2.15.0) The checkpoint 5 before a wall with an official time (50 m pools: the 45 m) placed again from the wall and the checkpoint
        /// 15 before it as it is now (as ApplyOfficial places it), while it is a proposal or a mark of the pilot not checked. Its confidence
        /// is not above the one of the checkpoint it comes from when that one is a proposal.
        /// </summary>
        private void RederiveOfficialLast(int distance)
        {
            if (Race == null || Race.Pool != 50 || !Race.StartTimestamp.HasValue || Timing == null || !Timing.IsValid)
                return;

            int wallDistance = distance + 15;
            double official;
            if (wallDistance % 50 != 0 || wallDistance > Race.Distance || !Race.OfficialTimes().TryGetValue(wallDistance, out official))
                return;

            long? before = SplitStamp(distance);
            if (!before.HasValue)
                return;

            long wall = Race.StartTimestamp.Value + RealSpan(official);
            long from = (long)Math.Round(SwimSplitDetector.LastFromWall(wall, before.Value, WallKind(wallDistance), SwimPresets.StyleAt(Race, wallDistance)));
            double confidence = SwimSplitDetector.OfficialLastConfidence;
            SwimProposal source = Race.GetSplit(distance) == null ? Race.Proposals.FirstOrDefault(x => x.Kind == KindRaceSplit && x.Index == distance) : null;
            if (source != null)
                confidence = Math.Min(confidence, source.Confidence);
            MoveSplitTo(wallDistance - 5, from, confidence, new OfficialUndo());
        }

        private long? SplitStamp(int distance)
        {
            SwimMark mark = Race.GetSplit(distance);
            if (mark != null)
                return mark.Timestamp;
            SwimProposal p = Race.Proposals.FirstOrDefault(x => x.Kind == KindRaceSplit && x.Index == distance);
            return p != null ? (long?)p.Timestamp : null;
        }

        /// <summary>
        /// Index of a timestamp in the frames of a reading: between two frames read, the fraction between them; before or after them,
        /// by the average step (the reading of a length reads on while the official time of its wall is after its frames; the callers
        /// keep the index within the frames where they need one).
        /// </summary>
        private static double SessionIndex(StrokeSession s, long timestamp)
        {
            List<long> t = s.Timestamps;
            int n = t.Count;
            if (n < 2)
                return double.NaN;

            if (timestamp >= t[0] && timestamp <= t[n - 1])
            {
                int i = t.BinarySearch(timestamp);
                if (i >= 0)
                    return i;
                i = ~i;
                return i - 1 + (timestamp - t[i - 1]) / (double)Math.Max(1, t[i] - t[i - 1]);
            }

            double step = (t[n - 1] - t[0]) / (double)(n - 1);
            if (step <= 0)
                return double.NaN;
            return timestamp < t[0] ? (timestamp - t[0]) / step : n - 1 + (timestamp - t[n - 1]) / step;
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

        /// <summary>
        /// Frame of the wall a length goes on from (index of its frames, NaN if none): the reading's own (WallSample), else the official
        /// time of the wall (typed from the results, since 2026-10-19), so that the reading goes on where it found no wall. Not the
        /// official one where the reading found its own: in the lab (the analysts' wall as the official one, 115 races) going on from
        /// it lost the swimmer after some open turns of the cameras (the place at the wall from the path a little before the touch).
        /// </summary>
        private double GoOnSample(LengthRead l)
        {
            bool official;
            return GoOnSample(l, out official);
        }

        /// <summary>GoOnSample; official: the wall is the official one (the reading found none).</summary>
        private double GoOnSample(LengthRead l, out bool official)
        {
            official = false;
            double own = WallSample(l);
            if (!double.IsNaN(own) || l == null)
                return own;

            long? stamp = OfficialWallStamp(l.Session.Wall);
            official = stamp.HasValue;
            return stamp.HasValue ? SessionIndex(l.Session, stamp.Value) : double.NaN;
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
        /// <summary>
        /// (2.15.0) The time before a wall where no stroke is searched (s): the finish margin at the finish, and at a touch with the hand
        /// that is not the finish (the changeovers of a relay, the backstroke → breaststroke of the medley) not more than it (the turn
        /// margin of a flip, 0.6 s in freestyle and backstroke, left the last stroke of those lengths out: ~1 stroke short).
        /// </summary>
        private double StrokeMarginAt(int wall, string style)
        {
            if (Race == null)
                return SwimStrokeDetector.WallMargin(style);
            if (wall >= Race.Distance)
                return SwimStrokeDetector.FinishMargin(style);
            return WallKind(wall) == "flip" ? SwimStrokeDetector.WallMargin(style) : Math.Min(SwimStrokeDetector.WallMargin(style), SwimStrokeDetector.FinishMargin(style));
        }

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
                case SwimSplitDetector.CueTrain: weight = 1.0; spread = 0.4; break;
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
                ? string.Format(CultureInfo.InvariantCulture, SwimLang.T(" {0} αυτόματες χεριές (διόρθωση: διπλό κλικ στις χεριές του τμήματος)."), strokes)
                : "";
            if (made.Count == 0)
            {
                Status((learned > 0 ?
                    string.Format(CultureInfo.InvariantCulture, SwimLang.T("🤖 Τα περάσματα είχαν ήδη χρόνους: {0} από αυτά κρατήθηκαν για τη μάθηση (δεν χρειάζεται έλεγχος)."), learned) :
                    SwimLang.T("Αυτόματα περάσματα: δεν βρέθηκαν σημάδια των σχοινιών (κόκκινοι φελλοί στα 15/25/35 και τα τελευταία 5μ.).")) + strokesText, false);
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
            int paceOnly = made.Count(IsDoubtfulPace);
            string notes = (paceOnly > 0 ? " ⚠ " + PaceOnlyText(paceOnly) + SwimLang.T(": έλεγξέ ") + (paceOnly == 1 ? SwimLang.T("το") : SwimLang.T("τα")) + SwimLang.T(" προσεκτικά.") : "") +
                OfficialNote(made);
            if (lengths > 1 || strokes > 0 || notes.Length > 0)
                Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("🤖 {0} μήκη, {1} προτάσεις.{2}{3} "), lengths, made.Count, strokesText, notes) + LastStatus,
                    paceOnly > 0);
        }

        /// <summary>
        /// A checkpoint from the pace of the swimmer alone (its line not seen in the video) and shown as doubtful (a swimmer seen from
        /// far, since 2026-10-19: almost half of them more than 0.5 s off in the analysts' phone races).
        /// </summary>
        private bool IsDoubtfulPace(SwimProposal p)
        {
            return p != null && p.Kind == KindRaceSplit && p.FarPace && !p.Official && p.Confidence < DoubtfulConfidence;
        }

        /// <summary>"3 περάσματα βγήκαν μόνο από τον ρυθμό του αθλητή (η γραμμή τους δεν φάνηκε στο βίντεο)".</summary>
        private static string PaceOnlyText(int count)
        {
            return (count == 1 ? SwimLang.T("1 πέρασμα βγήκε") : count.ToString(CultureInfo.InvariantCulture) + SwimLang.T(" περάσματα βγήκαν")) +
                SwimLang.T(" μόνο από τον ρυθμό του αθλητή (η γραμμή ") + (count == 1 ? SwimLang.T("του") : SwimLang.T("τους")) + SwimLang.T(" δεν φάνηκε στο βίντεο)");
        }

        /// <summary>" Από τους επίσημους χρόνους: ο τερματισμός, οι τοίχοι των 50, 100μ. και τα 45 μ. πριν από αυτούς." ("" for none).</summary>
        private string OfficialNote(IEnumerable<SwimProposal> made)
        {
            Dictionary<int, double> official = Race == null ? new Dictionary<int, double>() : Race.OfficialTimes();
            List<int> walls = made.Where(p => p.Kind == KindRaceSplit && p.Official && official.ContainsKey(p.Index)).Select(p => p.Index).Distinct().OrderBy(d => d).ToList();
            if (walls.Count == 0)
                return "";

            bool finish = walls.Contains(Race.Distance);
            List<int> turns = walls.Where(d => d != Race.Distance).ToList();
            List<string> parts = new List<string>();
            if (finish)
                parts.Add(SwimLang.T("ο τερματισμός"));
            if (turns.Count > 0)
                parts.Add((turns.Count == 1 ? SwimLang.T("ο τοίχος των ") : SwimLang.T("οι τοίχοι των ")) + string.Join(", ", turns.Select(d => d.ToString(CultureInfo.InvariantCulture)).ToArray()) + Race.Unit);
            bool last = made.Any(p => p.Kind == KindRaceSplit && p.Official && !official.ContainsKey(p.Index));
            return SwimLang.T(" Από τους επίσημους χρόνους: ") + string.Join(SwimLang.T(" και "), parts.ToArray()) + (last ? SwimLang.T(" (και τα τελευταία 5") + Race.Unit + SwimLang.T(" πριν από αυτούς).") : ".");
        }

        #region Whole race
        private bool pickForRace;

        // Where the automatic reading of the race stopped when the swimmer was lost after a wall: the next click goes on from
        // there (wall at the end of the next length, start of it).
        private int resumeWall = -1;
        private long resumeLapStart = long.MinValue;
        private long resumeAt = long.MinValue;

        /// <summary>(2.15.0) The race and the video where the reading stopped (the resume is only for them).</summary>
        private SwimRace resumeRace;
        private string resumeVideo;

        /// <summary>
        /// (2.15.0) Where the reading stopped is forgotten when the analysis is now of another race or video (it belonged to the manager
        /// of all the screens: a click in another video within 40 s of it read its first length as the next length of the other race).
        /// </summary>
        private void DropForeignResume()
        {
            if (resumeWall > 0 && (resumeRace != Race || resumeVideo != videoPath))
            {
                resumeWall = -1;
                resumeLapStart = long.MinValue;
                resumeAt = long.MinValue;
                resumeGap = 0;
            }
        }

        /// <summary>(2.15.0) The resume belongs to the race and the video read now.</summary>
        private void OwnResume()
        {
            resumeRace = Race;
            resumeVideo = videoPath;
        }

        /// <summary>(2.9.7) The reading stopped because its wall was this far (s) from the official time of the wall (0: the swimmer lost).</summary>
        private double resumeGap;

        /// <summary>The next two clicks (head and feet of the swimmer after the breakout) start the analysis of the whole race.</summary>
        public void BeginAutoRace()
        {
            // Official times typed in the panel that cannot be read: corrected first (the walls would be read without them).
            if (form != null && !form.IsDisposed && form.OfficialTypedWrong())
                return;

            // The button of the analyst: the auto pilot (if it waited for him) stops, the reading is his; the click on the swimmer
            // it waited for stays armed for it.
            if (PilotActive)
            {
                bool armed = (pilotStep == PilotStep.Swimmer || pilotStep == PilotStep.Lost) && pickForRace && PickingTrack;
                StopPilot();
                if (armed)
                {
                    Status(SwimLang.T("🤖 Όλος ο αγώνας: ο αυτόματος πιλότος σταμάτησε, η ανάγνωση είναι δική σου. Κλικ στο κεφάλι του αθλητή και μετά στα ") +
                        SwimLang.T("πόδια του, σε ένα καρέ μετά την έξοδο από το νερό (Esc: ακύρωση)."), false);
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
                Status(SwimLang.T("Αυτόματη ανάλυση αγώνα: πισίνα 50 ή 25."), true);
                return;
            }

            DropForeignResume();
            int keepWall = resumeWall;
            long keepLapStart = resumeLapStart, keepAt = resumeAt;
            double keepGap = resumeGap;
            BeginTrackSwimmer();
            resumeWall = keepWall;
            resumeLapStart = keepLapStart;
            resumeAt = keepAt;
            resumeGap = keepGap;
            if (PickingTrack)
            {
                pickForRace = true;
                Status(SwimLang.T("🤖 Αυτόματη ανάλυση αγώνα: σε ένα καρέ μετά την έξοδο από το νερό, κλικ στο κεφάλι του αθλητή και μετά στα πόδια του. ") +
                    SwimLang.T("Το Kinovea τον ακολουθεί σε όλα τα μήκη (Esc: ακύρωση)."), false);

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
        private void AutoRace(PointF head, PointF feet, long from, SwimStrokeLane? lane = null, int virtualRope = 0, AutoPickResult proposed = null)
        {
            // The auto pilot reads this race: it puts what is found and makes the report.
            bool pilot = PilotActive && (pilotStep == PilotStep.Swimmer || pilotStep == PilotStep.Lost);
            if (pilot)
            {
                pilotWasLost = pilotStep == PilotStep.Lost;
                StopPilotCountdown();
                pilotStep = PilotStep.Reading;

                // (The question of the swimmer or its countdown stayed on the status line during the reading until 2.7.1.)
                Status(pilotWasLost ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("✈ Ο αυτόματος πιλότος συνεχίζει να διαβάζει τον αγώνα μετά τον τοίχο των ") +
                    SwimLang.T("{0}{1}… (Cancel: σταμάτημα· ό,τι βρεθεί μένει)"), pilotLostWall, Race.Unit) :
                    SwimLang.T("✈ Ο αυτόματος πιλότος διαβάζει τον αγώνα… (Cancel: σταμάτημα· ό,τι βρεθεί μένει)"), false);
                RefreshPilotUi();
            }

            RefreshTiming();
            if (!Timing.IsValid || Race == null || !Race.StartTimestamp.HasValue)
            {
                if (pilot)
                    StopPilot();
                return;
            }

            if (proposed != null && !pilot)
                Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("🤖 Ο αγώνας διαβάζεται τρεις φορές, από {0} στα {1} s μετά την εκκίνηση· ") +
                    SwimLang.T("κρατιέται η ανάγνωση που συμφωνεί με τις άλλες δύο… (Cancel: σταμάτημα· ό,τι βρεθεί μένει)"), PickWho(proposed),
                    string.Join(", ", ProposedReadings.Select(t => t.ToString("0", CultureInfo.InvariantCulture)))), false);

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
                ProgressCancel cancel = KeepOpenWhileBusy(progress, worker);
                worker.ProgressChanged += (o, e) => progress.Update(e.ProgressPercentage, 100, true);
                worker.DoWork += (o, e) =>
                {
                    e.Result = proposed != null ? ReadRaceThree(proposed, lengths, (BackgroundWorker)o)
                        : ReadRace(head, feet, from, lengths, (BackgroundWorker)o, lane, virtualRope);
                };
                worker.RunWorkerCompleted += (o, e) =>
                {
                    error = e.Error;
                    cancelled = e.Error == null && (cancel.Asked || e.Result is bool && !(bool)e.Result);
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
                    Status(SwimLang.T("Η αυτόματη ανάλυση του αγώνα απέτυχε: ") + error.Message, true);
                    return;
                }

                // What was read (also when cancelled) is kept and proposed.
                string batch = Guid.NewGuid().ToString("N");
                List<SwimProposal> made = new List<SwimProposal>();
                int strokes = 0;
                foreach (LengthRead l in lengths)
                {
                    StoreTrack(l.Session, l.Result, l.ClickIndex, l.BodyLanes);
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

                // (2.15.0) The lengths whose strokes are to be checked (a stroke rate out of the range searched, the image lost).
                List<int> doubtful = lengths.Where(l => l.StrokesDoubtful && l.Strokes.Count > 0).Select(l => l.Session.Wall).Distinct().OrderBy(w => w).ToList();
                string doubt = doubtful.Count > 0 && !cancelled
                    ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("⚠ Χεριές για έλεγχο στα μήκη έως τα {0}{1}: η συχνότητα έξω από τα συνηθισμένα όρια ή χάθηκε η εικόνα (μέτρησέ τες ή διόρθωσέ τες στον πίνακα)."),
                        string.Join(", ", doubtful), Race.Unit)
                    : null;
                // (The status below or the pilot's report says it: a status of its own was covered by them at once.)
                if (doubt != null && pilot)
                    pilotDoubtful.UnionWith(doubtful);
                string after = doubt != null && !pilot ? " " + doubt : "";

                // (The auto pilot says it itself after the reading: the swimmer lost, or the report.)
                if (!pilot && cancelled)
                    Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Η ανάγνωση σταμάτησε μετά από {0}: προτείνονται όσα βρέθηκαν."),
                        CountOf(lengths.Count, SwimLang.T("μήκος"), SwimLang.T("μήκη"))), true);
                else if (!pilot && resumeWall > 0 && resumeGap != 0)
                    Status(string.Format(CultureInfo.InvariantCulture,
                        SwimLang.T("🤖 Στον τοίχο των {0}{1} η ανάγνωση απείχε {2:0.0} s από τον επίσημο χρόνο: δεν ακολούθησε τον αθλητή ως εκεί. Έλεγξε τις προτάσεις ") +
                        SwimLang.T("και μετά πάτα ξανά «🤖 Όλος ο αγώνας» σε ένα καρέ μετά την έξοδο από το νερό: η ανάγνωση συνεχίζει από εκεί."),
                        resumeWall - Race.Pool, Race.Unit, Math.Abs(resumeGap)) + after, true);
                else if (!pilot && resumeWall > 0)
                    Status(string.Format(CultureInfo.InvariantCulture,
                        SwimLang.T("🤖 Μετά τον τοίχο των {0}{1} ο αθλητής χάθηκε (η κάμερα γύρισε γρήγορα). Έλεγξε τις προτάσεις και μετά πάτα ξανά «🤖 Όλος ο αγώνας» ") +
                        SwimLang.T("σε ένα καρέ μετά την έξοδο από το νερό: η ανάγνωση συνεχίζει από εκεί."), resumeWall - Race.Pool, Race.Unit) + after, true);
                else if (doubt != null && !pilot)
                    Status(doubt, true);
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
        private StrokeSession StartSession(PointF head, PointF feet, long click, long start, BackgroundWorker worker, out bool cancelled,
            SwimStrokeLane? clickLane = null, int clickVirtual = 0)
        {
            cancelled = false;
            List<StrokeSession> candidates = new List<StrokeSession>();
            StrokeSession chosen = null;
            StrokeSession clicked = CreateSession(head, feet, click, clickLane, clickVirtual, Race.Pool);
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

        /// <summary>
        /// The swimmer of a lane proposed at the start (phones) is read three times, from his proposals this long after the start
        /// (s), and the reading in the middle of the three is kept: a phone's reading depends on where it starts (a splash taken
        /// for the swimmer, a turn read a little slower). Lab, 51 phone races of the analysts, races worse than from the analyst's
        /// click by more than 0.3 s (and better): one reading from the proposal at 6 s 5 (2), at 7 s 4 (3), at 8 s 8 (4); the
        /// middle one of the three 2 (3), as the analyst's click moved by a quarter of a body: 2 (1).
        /// </summary>
        private static readonly double[] ProposedReadings = { 6.0, 7.0, 8.0 };

        /// <summary>A proposal this long after the start at most is read three times (later ones: after a turn, once).</summary>
        private const double ProposedReadingsUntil = 12.0;

        /// <summary>
        /// Background thread: the race read from the swimmer of a lane proposed at each of ProposedReadings after the start (the
        /// place in the grid confirmed on the proposal), each reading on its own; the one closest to the other two is kept in
        /// 'lengths' (MiddleReading), the others are disposed. With two readings, the first. None (no proposal at those times):
        /// the proposal once. False if cancelled (the reading with the most lengths so far is kept).
        /// </summary>
        private bool ReadRaceThree(AutoPickResult proposed, List<LengthRead> lengths, BackgroundWorker worker)
        {
            Size reference = ReadingReader.Info.ReferenceSize;
            int width = StrokeWorkWidth;
            int height = Math.Max(2, (int)Math.Round(width * (double)reference.Height / reference.Width / 2.0) * 2);
            double sx = (double)width / reference.Width, sy = (double)height / reference.Height;
            long start = Race.StartTimestamp.Value;
            List<List<LengthRead>> runs = new List<List<LengthRead>>();
            List<Tuple<int, long, long>> states = new List<Tuple<int, long, long>>();
            List<double> gaps = new List<double>();
            bool completed = true;
            int kept = -1;
            try
            {
                foreach (double seconds in ProposedReadings)
                {
                    if (worker.CancellationPending)
                    {
                        completed = false;
                        break;
                    }

                    AutoPickResult p = null;
                    try
                    {
                        p = LanePick(start + RealSpan(seconds), width, height, sx, sy, worker, proposed.LanePlace, proposed.Grid.Lanes);
                    }
                    catch (Exception e)
                    {
                        log.Error("The proposed swimmer could not be found again for a reading.", e);
                    }

                    if (p == null)
                    {
                        log.DebugFormat("Reading of the proposed swimmer from {0:0} s: no proposal there.", seconds);
                        continue;
                    }

                    List<LengthRead> run = new List<LengthRead>();
                    runs.Add(run);
                    bool done = ReadRace(p.Head, p.Feet, p.Frame, run, worker, p.Lane);
                    states.Add(Tuple.Create(resumeWall, resumeLapStart, resumeAt));
                    gaps.Add(resumeGap);
                    if (!done || worker.CancellationPending)
                    {
                        completed = false;
                        break;
                    }
                }

                if (runs.Count == 0)
                    return ReadRace(proposed.Head, proposed.Feet, proposed.Frame, lengths, worker, proposed.Lane);

                // Cancelled: the reading with the most lengths (the first of them), the one under way included.
                kept = completed ? MiddleReading(runs) : runs.Select((r, i) => new { r.Count, i }).OrderByDescending(x => x.Count).ThenBy(x => x.i).First().i;
                lengths.AddRange(runs[kept]);
                resumeWall = states[kept].Item1;
                resumeLapStart = states[kept].Item2;
                resumeAt = states[kept].Item3;
                resumeGap = gaps[kept];
                OwnResume();
                log.DebugFormat("Proposed swimmer read {0} times, reading {1} kept.", runs.Count, kept + 1);
                return completed;
            }
            finally
            {
                for (int i = 0; i < runs.Count; i++)
                {
                    if (i == kept)
                        continue;
                    foreach (LengthRead l in runs[i])
                        l.Session.Dispose();
                }
            }
        }

        /// <summary>
        /// The reading closest to the others (index; the first of two): the smallest sum of its distances to the others (MarksApart);
        /// among those within 0.1 s of it, the one that read the most marks (a reading that lost the swimmer after a turn
        /// agrees with the others as far as it went), then the first.
        /// </summary>
        private int MiddleReading(List<List<LengthRead>> runs)
        {
            if (runs.Count < 3)
                return 0;

            List<Dictionary<int, double>> marks = runs.Select(ReadingMarks).ToList();
            double[] score = new double[marks.Count];
            for (int i = 0; i < marks.Count; i++)
            {
                for (int j = 0; j < marks.Count; j++)
                {
                    if (j != i)
                        score[i] += MarksApart(marks[i], marks[j]);
                }
            }

            double least = score.Min();
            int kept = -1;
            for (int i = 0; i < marks.Count; i++)
            {
                if (score[i] <= least + 0.1 && (kept < 0 || marks[i].Count > marks[kept].Count))
                    kept = i;
            }

            log.DebugFormat(CultureInfo.InvariantCulture, "Three readings: {0} (marks {1}).",
                string.Join(" / ", score.Select(x => x.ToString("0.00", CultureInfo.InvariantCulture))), string.Join(" / ", marks.Select(m => m.Count)));
            return kept;
        }

        /// <summary>Seconds from the start of the checkpoints (distance) and walls (minus the distance) of a reading: the median of their cues.</summary>
        private Dictionary<int, double> ReadingMarks(List<LengthRead> run)
        {
            Dictionary<int, double> marks = new Dictionary<int, double>();
            long start = Race.StartTimestamp.Value;
            foreach (LengthRead l in run)
            {
                foreach (SwimSplitFound f in l.Found)
                {
                    if (f.Cues.Count > 0)
                        marks[l.LapFrom + f.LapDistance] = Timing.Seconds(SessionStamp(l.Session, Median(f.Cues.Values)), start);
                }

                if (l.Wall.Count > 0)
                    marks[-l.Session.Wall] = Timing.Seconds(SessionStamp(l.Session, Median(l.Wall.Values)), start);
            }

            return marks;
        }

        /// <summary>
        /// How far two readings are: the largest difference of their common marks (s; a length read wrong shows in a few marks
        /// only), 99 without any.
        /// </summary>
        private static double MarksApart(Dictionary<int, double> a, Dictionary<int, double> b)
        {
            List<double> d = a.Keys.Where(b.ContainsKey).Select(k => Math.Abs(a[k] - b[k])).ToList();
            return d.Count == 0 ? 99.0 : d.Max();
        }

        private static double Median(IEnumerable<double> values)
        {
            List<double> v = values.OrderBy(x => x).ToList();
            if (v.Count == 0)
                return double.NaN;
            return v.Count % 2 == 1 ? v[v.Count / 2] : 0.5 * (v[v.Count / 2 - 1] + v[v.Count / 2]);
        }

        /// <summary>
        /// Background thread: the lane of a pool's grid checked against the reading's own rope search at the click. Null: the
        /// two agree at the swimmer (middle within 0.35 of the grid's width, width within 10%), the reading searches the ropes
        /// itself as from an analyst's click; else the grid's lane.
        /// </summary>
        private SwimStrokeLane? CheckedLane(PointF head, PointF feet, long click, SwimStrokeLane grid, BackgroundWorker worker, out bool cancelled)
        {
            cancelled = false;
            StrokeSession own = CreateSession(head, feet, click, null, 0);
            try
            {
                if (!ReadFrames(own, click, worker))
                {
                    cancelled = true;
                    return grid;
                }

                if (own.Timestamps.Count == 0 || !own.Detector.FoundRopes)
                    return grid;

                Size reference = ReadingReader.Info.ReferenceSize;
                double xc = 0.5 * (head.X + feet.X) * own.Width / reference.Width;
                SwimStrokeLane l = own.Detector.CurrentLane;
                double wg = Math.Abs(grid.Y2(xc) - grid.Y1(xc)), mg = 0.5 * (grid.Y1(xc) + grid.Y2(xc));
                double wo = Math.Abs(l.Y2(xc) - l.Y1(xc)), mo = 0.5 * (l.Y1(xc) + l.Y2(xc));
                bool agree = Math.Abs(mo - mg) <= LaneCheckMiddle * wg && Math.Abs(wo / Math.Max(1, wg) - 1) <= LaneCheckWidth;
                log.DebugFormat("Lane of the grid {0:0.0} px wide at {1:0.0}, the ropes found {2:0.0} px at {3:0.0}: {4}.", wg, mg, wo, mo,
                    agree ? "the ropes found" : "the grid's");
                return agree ? (SwimStrokeLane?)null : grid;
            }
            finally
            {
                own.Dispose();
            }
        }

        /// <summary>The ropes found at a proposed swimmer agree with the grid's lane: middles this share of its width apart at
        /// most, widths this share apart at most.</summary>
        private const double LaneCheckMiddle = 0.35, LaneCheckWidth = 0.10;

        /// <summary>
        /// Background thread: the lengths one after the other. False if cancelled. clickLane: the lane at the click when it is known
        /// (the swimmer of a lane of a view of the whole pool: its lane on the grid of the pool), else searched at the click.
        /// </summary>
        private bool ReadRace(PointF head, PointF feet, long from, List<LengthRead> lengths, BackgroundWorker worker, SwimStrokeLane? clickLane = null,
            int clickVirtual = 0)
        {
            try
            {
                return ReadRaceLengths(head, feet, from, lengths, worker, clickLane, clickVirtual);
            }
            finally
            {
                // The frames of the lengths go when the reading ends (each one also when the next length starts).
                foreach (LengthRead read in lengths)
                {
                    if (read != null && read.Result != null && read.Session != null)
                        ReleaseFrames(read);
                }
            }
        }

        private bool ReadRaceLengths(PointF head, PointF feet, long from, List<LengthRead> lengths, BackgroundWorker worker, SwimStrokeLane? clickLane,
            int clickVirtual)
        {
            long lapStart = LapStartTimestamp(from);
            int wall = NextWallDistance(from);

            // Going on after a stop (the swimmer lost after a wall): the length of the click is the one after that wall. A reading
            // in the background neither uses nor changes where the whole race stopped.
            bool background = quietThread != null;
            if (!background)
                DropForeignResume();
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
                resumeGap = 0;
            }

            // The lane of the pool's grid (the swimmer proposed by the program) against the ropes found at the click as for an
            // analyst's click: those when the two agree at the swimmer (the ropes of the image follow the lens and the camera
            // better than the straight lines of the grid), the grid's when not (the search took two lanes, or the next one).
            if (clickLane.HasValue)
            {
                bool cancelledCheck;
                clickLane = CheckedLane(head, feet, from, clickLane.Value, worker, out cancelledCheck);
                if (cancelledCheck)
                    return false;
            }

            double previousSeconds = 0;
            Size reference = ReadingReader.Info.ReferenceSize;
            SwimStrokeLane? nextLane = clickLane;
            int nextVirtual = clickLane.HasValue ? clickVirtual : 0;

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
                // The frames of the length before go now: its wall, the place of the swimmer there and his speed under the water
                // after the turn (the red marks of the ropes, the camera's displacement) were taken from them.
                if (lengths.Count > 0 && lengths[lengths.Count - 1] != null && lengths[lengths.Count - 1].Session != null)
                    ReleaseFrames(lengths[lengths.Count - 1]);

                // The first length of the race from the start signal (its 15 m and its breakout also when the click comes after
                // them), else from the click.
                StrokeSession s = null;
                if (lengths.Count == 0 && wall == Race.Pool && Race.StartTimestamp.HasValue && lapStart == Race.StartTimestamp.Value &&
                    from > lapStart + RealSpan(1.0))
                {
                    bool cancelled;
                    s = StartSession(head, feet, from, lapStart, worker, out cancelled, clickLane, clickVirtual);
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
                        resumeGap = 0;
                        OwnResume();
                    }

                    s.Dispose();
                    break;
                }

                if (lengths.Count == 0)
                    l.ClickIndex = Math.Max(0, s.Timestamps.FindIndex(t => t >= clickTimestamp));

                // The size of the swimmer clicked and the width of his lane there (the swimmer's size in widths of his lane, the
                // same in every length: the ghost of a comparison has it).
                if (double.IsNaN(clickLength) && lengths.Count == 0 && result.Lanes.Count > 0)
                {
                    double sx0 = (double)s.Width / reference.Width, sy0 = (double)s.Height / reference.Height;
                    double hx0 = head.X * sx0, hy0 = head.Y * sy0, fx0 = feet.X * sx0, fy0 = feet.Y * sy0;
                    double cx0 = 0.5 * (hx0 + fx0);
                    int clickIndex = Math.Max(0, Math.Min(result.Lanes.Count - 1, s.Timestamps.FindIndex(t => t >= clickTimestamp)));
                    SwimStrokeLane lane0 = result.Lanes[clickIndex];
                    clickLength = Math.Sqrt((hx0 - fx0) * (hx0 - fx0) + (hy0 - fy0) * (hy0 - fy0));
                    clickLaneWidth = Math.Max(5, Math.Abs(lane0.Y2(cx0) - lane0.Y1(cx0)));
                }

                l.BodyLanes = double.IsNaN(clickLength) || double.IsNaN(clickLaneWidth) ? double.NaN : clickLength / clickLaneWidth;
                ReadLengthStrokes(l);
                lengths.Add(l);
                if (wall >= Race.Distance || worker.CancellationPending)
                    break;

                // (2.9.7) The reading's wall far from the official time of the wall: it did not follow the swimmer to it (another
                // swimmer, the path stopped). Its strokes after its last checkpoint seen are not his; the next length is not read from
                // there: the swimmer is looked for again after the wall (the analyst's click, or the pilot's own pick), from the
                // official time of the wall.
                double officialGap;
                if (OffOfficialWall(l, out officialGap))
                {
                    double lastSeen = l.Found.Where(f => f.Cues.Keys.Any(k => k != SwimSplitDetector.CuePace)).Select(f => f.Cues.Values.Max()).DefaultIfEmpty(double.NaN).Max();
                    l.Strokes.RemoveAll(t => double.IsNaN(lastSeen) || t > lastSeen);
                    long? official = OfficialWallStamp(wall);
                    log.DebugFormat("Wall {0}: the reading's own wall {1:+0.00;-0.00} s from the official one, the swimmer looked for again after it.", wall, officialGap);
                    if (!background && official.HasValue)
                    {
                        resumeWall = wall + Race.Pool;
                        resumeLapStart = official.Value;
                        resumeAt = official.Value + RealSpan(2.7);
                        resumeGap = officialGap;
                        OwnResume();
                    }

                    break;
                }

                // The wall: the swimmer turns there; the next length starts from it, the other way, a little after.
                bool officialWall;
                double w = GoOnSample(l, out officialWall);
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
                // (An official wall after the frames read: its own time, not the last frame; the reading's own within its frames.)
                long wallTs = officialWall ? SessionStampOut(s, w) : SessionStamp(s, w);
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
                done = "✓ " + ProposalLabel(p) + SwimLang.T(": υπήρχε ήδη σε αυτό το καρέ.");
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
                done = string.Format(CultureInfo.InvariantCulture, SwimLang.T("✓ Πέρασμα {0}{1}: {2:0.00} s"), distance, Race.Unit, t);

                // (2.9.5) The analyst's frame however far from the proposal: a time out of order is kept with the warning of S.
                if (!SplitInOrder(distance, timestamp))
                {
                    System.Media.SystemSounds.Exclamation.Play();
                    done = "⚠ " + string.Format(CultureInfo.InvariantCulture, SwimLang.T("Πέρασμα {0}{1}: {2:0.00} s"), distance, Race.Unit, t) +
                        SwimLang.T("  ⚠ ο χρόνος δεν είναι ανάμεσα στα γειτονικά σημεία.");
                }

                done += ReestimateAfterSplit(distance);
                if (distance == Race.Distance)
                    CaptureFinalFrame(timestamp);
            }

            Answer(p, timestamp, true);
            RecombineSplitProposals();
            MoveEventProposals(KindRaceSplit, distance, timestamp);
            Changed(true);
            NextProposal(timestamp, done);
        }

        #region Checkpoints marked again (2.9.5)
        /// <summary>The checkpoint chosen with its row (in the table or on the video bar, Seek), -1 none: «Πέρασμα» at the right frame moves it.</summary>
        private int chosenSplit = -1;
        private SwimRace chosenSplitRace;

        /// <summary>The last «Πέρασμα» moved a checkpoint marked before (no strokes to count, no offer of the learning: HandleCommand).</summary>
        private bool splitMoved;

        /// <summary>
        /// (2.9.5) The checkpoint of a row chosen (the video went to it): the next «Πέρασμα» (S) at the right frame moves its time there
        /// (SplitTargetAt), also one the auto pilot put or an official one; Backspace puts the time before back.
        /// </summary>
        private void ChooseSplit(int distance)
        {
            SwimMark split = Race == null ? null : Race.GetSplit(distance);
            if (split == null || !Race.StartTimestamp.HasValue)
                return;

            chosenSplit = distance;
            chosenSplitRace = Race;
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Πέρασμα {0}{1}: {2:0.00} s · για αλλαγή: το σωστό καρέ με ← → και S («Πέρασμα»)· Backspace: η προηγούμενη τιμή."),
                distance, Race.Unit, Timing.Seconds(split.Timestamp, Race.StartTimestamp.Value)), false);
        }

        /// <summary>The checkpoint asked for: the row chosen (the latest choice), else the one of the proposal being checked (-1 none).</summary>
        private int AskedSplit()
        {
            if (chosenSplitRace == Race && chosenSplit >= 0)
                return chosenSplit;
            SwimProposal p = CurrentProposal;
            return p != null && p.Kind == KindRaceSplit ? p.Index : -1;
        }

        /// <summary>
        /// (2.9.5) The checkpoint «Πέρασμα» (S) marks at this frame (-1 none); move: it has a time already, which moves here (after the auto
        /// pilot all of them have one; until 2.9.4 S only filled the next one without a time, the others with Delete first). Only the one
        /// asked for moves (its row chosen, the proposal of a checkpoint being checked), when the frame keeps the order of the times.
        /// Otherwise (2.9.5b) a time is never changed: among the checkpoints without a time that the frame keeps in order, the one whose
        /// time would be nearest to it (expected between the marked ones, or at the speed of the race so far); while the video plays, or
        /// none in order, the next one without a time as before; all marked: none (-1).
        /// </summary>
        public int SplitTargetAt(long timestamp, out bool move)
        {
            move = false;
            if (Race == null)
                return -1;

            int next = Race.NextCheckpoint();
            if (Race.IsTraining || !Timing.IsValid)
                return next;

            // The marked checkpoints: their seconds from this frame (negative: before it).
            List<int> checkpoints = Race.Checkpoints.Distinct().OrderBy(d => d).ToList();
            Dictionary<int, double> marked = new Dictionary<int, double>();
            foreach (int d in checkpoints)
            {
                SwimMark m = Race.GetSplit(d);
                if (m != null)
                    marked[d] = Timing.Seconds(m.Timestamp, timestamp);
            }

            if (marked.Count == 0)
                return next;

            // The frame keeps the order: after the times of the checkpoints before it, before the times of the ones after it.
            Func<int, bool> inOrder = d => marked.All(x => x.Key == d || (x.Key < d ? x.Value < 0 : x.Value > 0));
            int best = -1;
            double bestAway = double.MaxValue;
            foreach (int d in checkpoints)
            {
                if (!inOrder(d))
                    continue;

                double away = Math.Abs(marked.ContainsKey(d) ? marked[d] : ExpectedSplitAway(d, marked, timestamp));
                if (away < bestAway)
                {
                    best = d;
                    bestAway = away;
                }
            }

            // The checkpoint asked for (its row chosen, its proposal being checked): marked at this frame, or its time moved here.
            int asked = AskedSplit();
            if (asked >= 0 && checkpoints.Contains(asked) && inOrder(asked))
            {
                move = marked.ContainsKey(asked);
                return asked;
            }

            // While the video plays, the next checkpoint without a time as always (two presses in a row do not move the last one).
            if (next >= 0 && HasVideo && player.IsCurrentlyPlaying)
                return next;

            // (2.9.5b) Not asked for, a time marked never moves: a press of S on a paused video moved the 15 m of the pilot almost 0.4 s
            // earlier, before the breakout, and the speeds after the start went wrong (15–25 m far too slow, then faster). The nearest
            // checkpoint without a time that the frame keeps in order (one skipped), else the next one without a time (none: all have a
            // time, AddSplit says how to change one).
            if (best >= 0 && marked.ContainsKey(best))
            {
                best = -1;
                bestAway = double.MaxValue;
                foreach (int d in checkpoints)
                {
                    if (marked.ContainsKey(d) || !inOrder(d))
                        continue;

                    double away = Math.Abs(ExpectedSplitAway(d, marked, timestamp));
                    if (away < bestAway)
                    {
                        best = d;
                        bestAway = away;
                    }
                }
            }

            return best >= 0 ? best : next;
        }

        /// <summary>«Πέρασμα» pressed: its checkpoint at this frame (SplitTargetAt); the row chosen counts for this press only.</summary>
        private int TakeSplitTarget(long timestamp, out bool move)
        {
            int distance = SplitTargetAt(timestamp, out move);
            chosenSplit = -1;
            chosenSplitRace = null;
            splitMoved = false;
            return distance;
        }

        /// <summary>
        /// Where a checkpoint without a time is expected (s from this frame): between the marked ones around it in proportion to the
        /// distance, or after (before) the last (first) marked one at the mean speed of the race up to it.
        /// </summary>
        private double ExpectedSplitAway(int distance, Dictionary<int, double> marked, long timestamp)
        {
            int before = marked.Keys.Where(k => k < distance).DefaultIfEmpty(-1).Max();
            int after = marked.Keys.Where(k => k > distance).DefaultIfEmpty(int.MaxValue).Min();
            if (before >= 0 && after != int.MaxValue)
                return marked[before] + (marked[after] - marked[before]) * (distance - before) / (after - before);

            int known = before >= 0 ? before : after;
            double speed = 1.6;
            if (Race.StartTimestamp.HasValue)
            {
                double raceTime = marked[known] - Timing.Seconds(Race.StartTimestamp.Value, timestamp);
                if (raceTime > 0.5)
                    speed = Math.Max(0.5, Math.Min(3.0, known / raceTime));
            }

            return marked[known] + (distance - known) / speed;
        }

        /// <summary>The time keeps the order of the checkpoints marked before and after it.</summary>
        private bool SplitInOrder(int distance, long timestamp)
        {
            SwimMark previous = Race.Checkpoints.Where(d => d < distance).Select(d => Race.GetSplit(d)).LastOrDefault(m => m != null);
            SwimMark next = Race.Checkpoints.Where(d => d > distance).Select(d => Race.GetSplit(d)).FirstOrDefault(m => m != null);
            return (previous == null || previous.Timestamp < timestamp) && (next == null || next.Timestamp > timestamp);
        }

        /// <summary>
        /// (2.9.5) A checkpoint with a time already (the analyst's, the auto pilot's, an official one) moved to this frame with «Πέρασμα»:
        /// the analyst's mark replaces it (Backspace puts it back), the strokes typed for its segment stay, its proposal waiting is answered
        /// (as Enter on it), the underwater distance the pilot estimated from it again; the finish takes its final frame again
        /// (HandleCommand). The times after it follow at the next calculation.
        /// </summary>
        private bool MoveSplit(int distance, long timestamp)
        {
            SwimMark old = Race.GetSplit(distance);
            double was = Timing.Seconds(old.Timestamp, Race.StartTimestamp.Value);
            SwimMark mark = new SwimMark { Type = SwimMarkType.Split, Timestamp = timestamp, Distance = distance };
            AddReplacing(mark, m => m.Type == SwimMarkType.Split && m.Distance == distance);
            splitMoved = true;

            // Its proposal waiting is answered by this frame (the learning), as Enter on it.
            ResolveProposalsByMark(SwimMarkType.Split, timestamp);

            double t = Timing.Seconds(timestamp, Race.StartTimestamp.Value);
            string message = string.Format(CultureInfo.InvariantCulture, SwimLang.T("Πέρασμα {0}{1}: {2:0.00} s (ήταν {3:0.00} s) · Backspace: η προηγούμενη τιμή."),
                distance, Race.Unit, t, was);
            bool warn = !SplitInOrder(distance, timestamp);
            if (warn)
            {
                System.Media.SystemSounds.Exclamation.Play();
                message += SwimLang.T("  ⚠ ο χρόνος δεν είναι ανάμεσα στα γειτονικά σημεία.");
            }

            Status(message + ReestimateAfterSplit(distance), warn);
            return true;
        }

        /// <summary>
        /// (2.9.5) After Backspace on a checkpoint: the underwater distance the pilot estimated from it again; a finish moved and put back
        /// takes its final frame again (the one of the moved finish showed its time). The text for the status.
        /// </summary>
        private string AfterSplitUndone(SwimMark undone, List<SwimMark> restored)
        {
            if (undone == null || undone.Type != SwimMarkType.Split || Race == null)
                return "";

            bool finishBack = restored != null && restored.Any(m => m.Type == SwimMarkType.Split && m.Distance == Race.Distance) &&
                Race.FinalFrame != null && SameFrame(Race.FinalFrame.Timestamp, undone.Timestamp);
            if (finishBack)
                PilotLater(() => RecaptureFinalFrame(true, true));

            return ReestimateAfterSplit(undone.Distance);
        }
        #endregion
        #endregion
    }
}
