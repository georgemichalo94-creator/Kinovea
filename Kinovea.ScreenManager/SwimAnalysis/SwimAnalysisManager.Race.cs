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
            LengthRead l = new LengthRead { Session = s, Result = result, LapFrom = s.Wall - Race.Pool };
            try
            {
                l.Found = SwimSplitDetector.Detect(result, s.Detector.Direction, s.Fps, lapStart, Race.Pool);
                double last = l.Found.Count > 0 ? l.Found.Max(f => f.Cues.Values.Max()) : double.NaN;
                l.Wall = SwimSplitDetector.WallCues(result, s.Detector.Direction, s.Fps, last, lapStart, Race.Pool);
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
        private List<SwimProposal> SplitProposals(LengthRead l, string batch)
        {
            List<SwimProposal> made = new List<SwimProposal>();
            StrokeSession s = l.Session;
            string video = SwimLearning.VideoCode(videoPath);
            SwimLearning learning = Learning;

            List<Tuple<int, Dictionary<string, double>, double>> items = new List<Tuple<int, Dictionary<string, double>, double>>();
            foreach (SwimSplitFound f in l.Found)
                items.Add(Tuple.Create(l.LapFrom + f.LapDistance, f.Cues, f.Confidence));
            if (l.Wall.Count > 0)
            {
                // The wall: the cues of its kind (finish, open turn, flip turn), each with its usual delay before any learning.
                string kind = WallKind(s.Wall);
                Dictionary<string, double> wall = new Dictionary<string, double>();
                foreach (KeyValuePair<string, double> c in l.Wall)
                    wall[c.Key + "." + kind] = c.Value + WallDelay(c.Key, kind) * s.Fps;
                items.Add(Tuple.Create(s.Wall, wall, 0.5));
            }

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
                SwimMark marked = Race.GetSplit(distance);
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

        /// <summary>Checkpoints already marked that taught the learning in the last automatic reading.</summary>
        private int learnedFromMarks;

        /// <summary>The wall at this distance: "finish", "open" (hand touch: butterfly, breaststroke) or "flip" (freestyle, backstroke).</summary>
        private string WallKind(int wall)
        {
            if (wall >= Race.Distance)
                return "finish";
            string style = SwimPresets.StyleAt(Race, wall);
            return style == "fs" || style == "bk" ? "flip" : "open";
        }

        /// <summary>
        /// Usual time from a wall cue to the analyst's frame (s), before any learning: the swimmer slows down and the head reaches the
        /// end of the ropes before the touch, much before the feet touch in a flip turn.
        /// </summary>
        private static double WallDelay(string cue, string kind)
        {
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
            int dot = cue.IndexOf('.');
            if (dot > 0)
                cue = cue.Substring(0, dot);

            switch (cue)
            {
                case SwimSplitDetector.CueFar: weight = 1.0; spread = 0.06; break;
                case SwimSplitDetector.CueMiddle: weight = 0.6; spread = 0.12; break;
                case SwimSplitDetector.CueNear: weight = 0.4; spread = 0.15; break;
                case SwimSplitDetector.CueWallBody: weight = 1.0; spread = 0.15; break;
                case SwimSplitDetector.CueWallHead: weight = 0.8; spread = 0.2; break;
                default: weight = 0.5; spread = 0.3; break;
            }
        }

        /// <summary>The proposals made replace the waiting ones of the same distances and the check starts.</summary>
        private void ShowSplitProposals(List<SwimProposal> made, int lengths)
        {
            Race.Proposals.RemoveAll(p => p.Kind == KindRaceSplit && made.Any(m => m.Index == p.Index));
            int learned = learnedFromMarks;
            learnedFromMarks = 0;
            if (made.Count == 0)
            {
                Status(learned > 0 ?
                    string.Format(CultureInfo.InvariantCulture, "🤖 Τα περάσματα είχαν ήδη χρόνους: {0} από αυτά κρατήθηκαν για τη μάθηση (δεν χρειάζεται έλεγχος).", learned) :
                    "Αυτόματα περάσματα: δεν βρέθηκαν σημάδια των σχοινιών (κόκκινοι φελλοί στα 15/25/35 και τα τελευταία 5μ.).", false);
                Changed(false);
                RefreshProposalsUi();
                return;
            }

            Race.Proposals.AddRange(made);
            Changed(false);
            BeginReview(made.OrderBy(p => p.Timestamp).First());
            if (lengths > 1)
                Status(string.Format(CultureInfo.InvariantCulture, "🤖 {0} μήκη, {1} προτάσεις περασμάτων. ", lengths, made.Count) + LastStatus, false);
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
            }
        }

        /// <summary>
        /// The whole race from one click: each length is followed to its wall, its checkpoints and its wall are found, then the
        /// next length is followed from the wall, the other way (the swimmer is where the previous length ended). Then all of them
        /// are proposed for the check.
        /// </summary>
        private void AutoRace(PointF head, PointF feet, long from)
        {
            RefreshTiming();
            if (!Timing.IsValid || Race == null || !Race.StartTimestamp.HasValue)
                return;

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
                    Status("Η αυτόματη ανάλυση του αγώνα απέτυχε: " + error.Message, true);
                    return;
                }

                // What was read (also when cancelled) is kept and proposed.
                string batch = Guid.NewGuid().ToString("N");
                List<SwimProposal> made = new List<SwimProposal>();
                foreach (LengthRead l in lengths)
                {
                    StoreTrack(l.Session, l.Result);
                    made.AddRange(SplitProposals(l, batch));
                }

                Changed(false);
                ShowSplitProposals(made, lengths.Count);
                if (cancelled)
                    Status(string.Format(CultureInfo.InvariantCulture, "Η ανάγνωση σταμάτησε μετά από {0} μήκη: προτείνονται όσα βρέθηκαν.", lengths.Count), true);
                else if (resumeWall > 0)
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
        }

        /// <summary>Background thread: the lengths one after the other. False if cancelled.</summary>
        private bool ReadRace(PointF head, PointF feet, long from, List<LengthRead> lengths, BackgroundWorker worker)
        {
            long lapStart = LapStartTimestamp(from);
            int wall = NextWallDistance(from);

            // Going on after a stop (the swimmer lost after a wall): the length of the click is the one after that wall.
            if (resumeWall > 0 && resumeAt != long.MinValue && from >= resumeAt - RealSpan(1.0) && from <= resumeAt + RealSpan(40))
            {
                wall = resumeWall;
                lapStart = resumeLapStart;
            }

            resumeWall = -1;
            resumeLapStart = long.MinValue;
            resumeAt = long.MinValue;
            double previousSeconds = 0;
            Size reference = frameServer.VideoReader.Info.ReferenceSize;
            SwimStrokeLane? nextLane = null;
            int nextVirtual = 0;
            while (wall <= Race.Distance)
            {
                StrokeSession s = CreateSession(head, feet, from, nextLane, nextVirtual);
                s.Wall = wall;
                s.LapStart = lapStart;
                long target = lengths.Count == 0 ? TrackEnd(from) :
                    Math.Min(frameServer.VideoReader.WorkingZone.End, from + RealSpan(Math.Min(120, previousSeconds * 1.35 + 3)));
                if (target <= from || !ReadFrames(s, target, worker))
                {
                    s.Dispose();
                    return target <= from;
                }

                if (s.Timestamps.Count < 10)
                {
                    s.Dispose();
                    break;
                }

                SwimStrokeResult result = s.Detector.Finish(0, s.Timestamps.Count - 1);
                LengthRead l = AnalyseLength(s, result, lapStart);
                if (l == null)
                {
                    s.Dispose();
                    break;
                }

                // After a wall, a length without any mark of the ropes: the swimmer was lost (camera turning fast, lane at the
                // far end). The analyst clicks him again a little later and the reading goes on from there.
                if (lengths.Count > 0 && l.Found.Count == 0)
                {
                    resumeWall = wall;
                    resumeLapStart = lapStart;
                    resumeAt = from + RealSpan(2.5);
                    s.Dispose();
                    break;
                }

                lengths.Add(l);
                if (wall >= Race.Distance || worker.CancellationPending)
                    break;

                // The wall: the swimmer turns there; the next length starts from it, the other way (its usual delay added, a little after).
                double w = double.NaN;
                string kind = WallKind(wall);
                foreach (string cue in new[] { SwimSplitDetector.CueStop, SwimSplitDetector.CueWallBody, SwimSplitDetector.CueWallHead })
                {
                    if (l.Wall.ContainsKey(cue))
                    {
                        w = l.Wall[cue] + WallDelay(cue, kind) * s.Fps;
                        break;
                    }
                }

                if (double.IsNaN(w) || result.Path == null || result.Path.Length == 0)
                    break;

                int wi = (int)Math.Round(Math.Max(0, Math.Min(result.Path.Length - 1, w)));
                double x = result.Path[wi];
                SwimStrokeLane lane = result.Lanes[Math.Min(wi, result.Lanes.Count - 1)];
                double y = 0.5 * (lane.Y1(x) + lane.Y2(x));
                SwimStrokeLane lane0 = result.Lanes[0];
                double sx = (double)s.Width / reference.Width, sy = (double)s.Height / reference.Height;
                double hx0 = head.X * sx, hy0 = head.Y * sy, fx0 = feet.X * sx, fy0 = feet.Y * sy;
                double cx0 = 0.5 * (hx0 + fx0);
                double lw0 = Math.Max(5, Math.Abs(lane0.Y2(cx0) - lane0.Y1(cx0))), lw = Math.Max(5, Math.Abs(lane.Y2(x) - lane.Y1(x)));
                double length = Math.Sqrt((hx0 - fx0) * (hx0 - fx0) + (hy0 - fy0) * (hy0 - fy0)) * lw / lw0;
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
                lapStart = wallTs;
                wall += Race.Pool;
                if (from >= frameServer.VideoReader.WorkingZone.End)
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
            Changed(true);
            NextProposal(timestamp, done);
        }
        #endregion
    }
}
