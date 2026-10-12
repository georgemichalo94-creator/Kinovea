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
using System.Linq;
using System.Threading.Tasks;
using Kinovea.Services;
using Kinovea.Video;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Automatic analysis of a race video (camera in the stands following the swimmer): the start signal from the start lights,
    /// proposed for the check of the analyst (Enter right, arrows and Enter corrected, Delete wrong) and learned from the answers.
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

        /// <summary>
        /// Reads the video from its beginning until the start (the start lights flash, then the camera follows the swimmers),
        /// proposes the frame of the flash and starts the check. A start already marked near it only teaches the learning.
        /// </summary>
        public void FindStartLight()
        {
            RefreshTiming();
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
                worker.DoWork += (o, e) => { e.Result = ReadStartLightFrames(detector, stamps, from, end, width, height, (BackgroundWorker)o); };
                worker.RunWorkerCompleted += (o, e) =>
                {
                    error = e.Error;
                    cancelled = e.Error == null && e.Result is bool && !(bool)e.Result;
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
                    Status("Η αυτόματη εκκίνηση απέτυχε: " + error.Message, true);
                    return;
                }

                if (cancelled)
                {
                    Status("Η αναζήτηση της εκκίνησης ακυρώθηκε.", false);
                    return;
                }

                List<SwimStartCandidate> found = detector.Find();
                if (found.Count == 0 || stamps.Count == 0)
                {
                    Status("Δεν βρέθηκε φωτεινό σήμα εκκίνησης πριν από την κολύμβηση (δεν φαίνεται το φως ή η κάμερα δεν ακολουθεί τον αθλητή). " +
                        "Βάλε την εκκίνηση με το κουμπί «Εκκίνηση» στο καρέ του σήματος.", true);
                    return;
                }

                ProposeStart(found, stamps, realFps);
            }
            finally
            {
                RefreshProposalsUi();
                InvalidatePlayer();
            }
        }

        /// <summary>Background thread: frames from the beginning until the start is clear (or 'end'). False if cancelled.</summary>
        private bool ReadStartLightFrames(SwimStartLight detector, List<long> stamps, long from, long end, int width, int height, BackgroundWorker worker)
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
                        if (frame == null || frame.Image == null || frame.Timestamp > end)
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

                SwimDetection d = new SwimDetection(KindRaceStart, SwimMarkType.Signal, k + 1) { Confidence = k == 0 ? confidence0 : 0.35 * confidence0 };
                d.Cue("light", c.Frame, 1.0, 0.02);
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
                p.Cues["light"] = stamps[c.Frame];
                p.Measures = new Dictionary<string, double>
                {
                    { "lights", c.Lights },
                    { "flash", Math.Round(c.Flash, 1) },
                    { "follow", Math.Round(Timing.Seconds(stamps[Math.Min(stamps.Count - 1, c.Follow)], stamps[c.Frame]), 2) }
                };
                made.Add(p);
            }

            if (made.Count == 0)
            {
                Status("Δεν βρέθηκε φωτεινό σήμα εκκίνησης.", true);
                return;
            }

            // The analyst already put the start near the flash: that is his answer (learning), nothing to check.
            SwimProposal best = made[0];
            if (Race.StartTimestamp.HasValue && Math.Abs(Timing.Seconds(Race.StartTimestamp.Value, best.Timestamp)) <= 0.2)
            {
                long marked = Race.StartTimestamp.Value;
                Race.Proposals.Add(best);
                Answer(best, marked, true);
                Changed(false);
                long frames = (long)Math.Round((marked - best.Timestamp) / Math.Max(1, Timing.AverageTimestampsPerFrame));
                Status(frames == 0 ? "💡 Η εκκίνηση που έχεις βάλει είναι στο καρέ του φωτεινού σήματος: κρατήθηκε για τη μάθηση." :
                    string.Format(CultureInfo.InvariantCulture, "💡 Η εκκίνηση που έχεις βάλει είναι {0:+0;-0} καρέ από το φωτεινό σήμα: κρατήθηκε για τη μάθηση.", frames), false);
                return;
            }

            Race.Proposals.AddRange(made);
            Changed(false);
            BeginReview(best);
        }

        /// <summary>The start proposal is accepted at this frame: it becomes the start of the race (and of the swimmers of the video).</summary>
        private void AcceptStartProposal(SwimProposal p, long timestamp)
        {
            // The other start candidates go (no answer: the learning does not change).
            Race.Proposals.RemoveAll(x => x.Kind == KindRaceStart && x.Id != p.Id);
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
            NextProposal(timestamp, "✓ " + ProposalLabel(p) + ": t = 0 σε αυτό το καρέ.");
        }
        #endregion

        #region Checkpoints
        /// <summary>Proposal of a checkpoint found from the marks of the ropes (Index: the distance).</summary>
        public const string KindRaceSplit = "race.split";

        /// <summary>
        /// The checkpoints of the length just followed (TrackSwimmer), from the marks of the ropes: proposed for the check, the ones
        /// not marked yet. The answers teach each cue (head over the mark of the far rope, of the near rope, their middle).
        /// </summary>
        private void ProposeSplits(StrokeSession s, SwimStrokeResult result)
        {
            if (Race == null || Race.IsTraining || !Race.StartTimestamp.HasValue || s.Timestamps.Count < 10 || s.Fps <= 0)
                return;

            int n = s.Timestamps.Count;
            double step = (s.Timestamps[n - 1] - s.Timestamps[0]) / (double)Math.Max(1, n - 1);
            if (step <= 0)
                return;

            // Index of the start of the length in the frames read (before them: negative).
            long lapStartTs = s.LapStart == long.MinValue ? Race.StartTimestamp.Value : s.LapStart;
            double lapStart = (lapStartTs - s.Timestamps[0]) / step;
            int lapFrom = s.Wall - Race.Pool;
            if (Race.PoolUnit != SwimRace.UnitMeters || Race.Pool != 50)
            {
                Status("Αυτόματα περάσματα: μόνο σε πισίνα 50μ. (σημάδια των σχοινιών στα 15/25/35/45).", true);
                return;
            }

            List<SwimSplitFound> found;
            try
            {
                found = SwimSplitDetector.Detect(result, s.Detector.Direction, s.Fps, lapStart, Race.Pool);
            }
            catch (Exception e)
            {
                log.Error("Automatic checkpoints failed.", e);
                Status("Τα αυτόματα περάσματα απέτυχαν: " + e.Message, true);
                return;
            }

            string video = SwimLearning.VideoCode(videoPath);
            SwimLearning l = Learning;
            string batch = Guid.NewGuid().ToString("N");
            List<SwimProposal> made = new List<SwimProposal>();
            Func<double, long> stamp = index =>
            {
                double clamped = Math.Max(0, Math.Min(n - 1, index));
                int i0 = (int)Math.Floor(clamped);
                int i1 = Math.Min(n - 1, i0 + 1);
                return (long)Math.Round(s.Timestamps[i0] + (clamped - i0) * (s.Timestamps[i1] - s.Timestamps[i0]));
            };

            foreach (SwimSplitFound f in found)
            {
                int distance = lapFrom + f.LapDistance;
                if (!Race.Checkpoints.Contains(distance) || Race.GetSplit(distance) != null)
                    continue;

                SwimDetection d = new SwimDetection(KindRaceSplit, SwimMarkType.Split, distance) { Confidence = f.Confidence };
                foreach (KeyValuePair<string, double> c in f.Cues)
                {
                    double weight = c.Key == SwimSplitDetector.CueFar ? 1.0 : c.Key == SwimSplitDetector.CueMiddle ? 0.6 : 0.4;
                    double spread = c.Key == SwimSplitDetector.CueFar ? 0.06 : c.Key == SwimSplitDetector.CueMiddle ? 0.12 : 0.15;
                    d.Cue(c.Key, c.Value, weight, spread);
                }

                double confidence;
                double sample = SwimProposalMath.Combine(d, l, video, s.Fps, out confidence);
                if (double.IsNaN(sample))
                    continue;

                SwimProposal p = new SwimProposal
                {
                    Type = SwimMarkType.Split,
                    Kind = KindRaceSplit,
                    Timestamp = stamp(sample),
                    Confidence = Math.Round(confidence, 2),
                    Index = distance,
                    Batch = batch
                };

                foreach (KeyValuePair<string, double> c in f.Cues)
                    p.Cues[c.Key] = stamp(c.Value);
                made.Add(p);
            }

            // The proposals of an older reading of the same distances go.
            Race.Proposals.RemoveAll(p => p.Kind == KindRaceSplit && made.Any(m => m.Index == p.Index));
            if (made.Count == 0)
            {
                Status("Αυτόματα περάσματα: δεν βρέθηκαν σημάδια των σχοινιών σε αυτό το μήκος (κόκκινοι φελλοί στα 15/25/35 και τα τελευταία 5μ.).", false);
                RefreshProposalsUi();
                return;
            }

            Race.Proposals.AddRange(made);
            Changed(false);
            BeginReview(made.OrderBy(p => p.Timestamp).First());
        }

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
            Changed(true);
            NextProposal(timestamp, done);
        }
        #endregion
    }
}
