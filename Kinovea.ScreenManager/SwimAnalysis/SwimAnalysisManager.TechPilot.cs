using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Kinovea.Video;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.9.9) The automatic pilot of the technical analysis of a turn (training video from the side camera under the water):
    /// the video is read with the swimmer detector and the skeleton (SwimTechReader), the events of the turn are found
    /// (SwimTurnReader: the wall, the rotation, the push off, the dolphin kicks, the breakout, the last stroke before the wall
    /// and the first after the breakout), and each becomes a proposal checked on the video as the others (Enter, ←/→ and Enter,
    /// Delete). The marks of the analyst stay: no proposal is made where his mark already is.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        public const string KindTechWall = "tech.turn.wall";
        public const string KindTechRotation = "tech.turn.rotation";
        public const string KindTechPushoff = "tech.turn.pushoff";
        public const string KindTechKick = "tech.turn.kick";
        public const string KindTechBreakout = "tech.turn.breakout";
        public const string KindTechLastStroke = "tech.turn.stroke.last";
        public const string KindTechFirstStroke = "tech.turn.stroke.first";
        public const string KindTechHeadIn = "tech.turn.head.in5";
        public const string KindTechHeadOut = "tech.turn.head.out5";

        /// <summary>Images per second and width of the external video for the flags (the flag reader was measured at them).</summary>
        public const double FlagsFps = 10;
        public const int FlagsWidth = 720;

        /// <summary>
        /// Largest difference of length (seconds) between the underwater and the external video taken as one synchronised
        /// export of the same moment (the analysts' exports: within 0.35 s; two separate camera files: much more).
        /// </summary>
        public const double FlagsSyncTolerance = 0.5;

        /// <summary>Seconds of the external video read before the wall and after the push-off (the passes are searched 1–12 s before, 0–10 s after).</summary>
        public const double FlagsBefore = 13;
        public const double FlagsAfter = 11;

        /// <summary>Confidence of the head at −5 / +5 from the flags (checked by eye on 5 turns: within 0.1–0.2 s; the sync of the two files is assumed).</summary>
        public const double FlagsConfidence = 0.6;

        /// <summary>
        /// Words of the underwater camera in a file name and the word of the external camera of the same take (…_Υποβρύχια.MP4 and
        /// …_Εξωτερική.MP4 of the analysts' exports; …_uw / …_ext).
        /// </summary>
        private static readonly string[][] cameraWords =
        {
            new[] { "Υποβρύχια", "Εξωτερική" },
            new[] { "Υποβρυχια", "Εξωτερικη" },
            new[] { "underwater", "external" },
            new[] { "uw", "ext" }
        };

        /// <summary>Images per second of the reading (the times of the events are within half an image of them).</summary>
        public const double TechPilotFps = 30;

        /// <summary>Width of the images of the reading (the detector and the skeleton were measured at it).</summary>
        public const int TechPilotWidth = 1280;

        /// <summary>The longest part of the video read (seconds of the working zone, from its start).</summary>
        public const double TechPilotMaxSeconds = 90;

        /// <summary>The reading of the technical pilot is going on (the buttons wait).</summary>
        public bool TechPilotRunning { get; private set; }

        /// <summary>
        /// "🤖 Στροφή": the turn of this training video read and proposed. The style of the analysis decides the kind of the turn
        /// (freestyle, backstroke: flip; breaststroke, butterfly: open; medley or none: from the video).
        /// </summary>
        public void RunTechPilot()
        {
            if (Race == null || !HasVideo || TechPilotRunning || SkeletonRunning)
                return;

            if (!Race.IsTraining)
            {
                Status(SwimLang.T("🤖 Ο αυτόματος πιλότος της τεχνικής διαβάζει βίντεο προπόνησης από την υποβρύχια πλάγια κάμερα (στροφή). Σε αγώνα: ✈ Πιλότος."), true);
                return;
            }

            if (!SwimPoseNet.Available || !SwimSwimmerDetector.Available)
            {
                Status(string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("🤖 Λείπουν τα μοντέλα της ανάγνωσης στον φάκελο {0} ({1}, {2})."),
                    SwimPoseNet.ModelFolder, SwimSwimmerDetector.ModelFile, SwimPoseNet.LandmarkFile), true);
                return;
            }

            RefreshTiming();
            if (!Timing.IsValid)
            {
                Status(SwimLang.T("Δεν είναι γνωστός ο ρυθμός καρέ του βίντεο: η ανάγνωση δεν μπορεί να γίνει."), true);
                return;
            }

            VideoReader video = frameServer.VideoReader;
            long from = video.WorkingZone.Start;
            long end = Math.Min(video.WorkingZone.End, from + RealSpan(TechPilotMaxSeconds));
            if (end <= from)
            {
                Status(SwimLang.T("Δεν υπάρχει βίντεο για ανάγνωση."), true);
                return;
            }

            double hsf = Timing.HighSpeedFactor <= 0 ? 1.0 : Timing.HighSpeedFactor;
            double realFps = 1000.0 * hsf / Timing.FrameIntervalMilliseconds;
            int step = Math.Max(1, (int)Math.Round(realFps / TechPilotFps));
            Size reference = video.Info.ReferenceSize;
            int width = Math.Min(TechPilotWidth, Math.Max(64, reference.Width));
            int height = Math.Max(2, (int)Math.Round(width * (double)reference.Height / Math.Max(1, reference.Width)) / 2 * 2);
            SwimTurnKind kind = TurnKindOfStyle(Race.Style);
            double seconds = Math.Max(1e-3, Timing.Seconds(end, from));

            List<long> stamps = new List<long>();
            SwimTechReading reading = null;
            Exception error = null;
            bool cancelled = false;
            TechPilotRunning = true;
            RefreshTechPilotUi();
            Status(SwimLang.T("🤖 Ανάγνωση της στροφής: ο κολυμβητής, ο σκελετός και η κίνηση της κάμερας σε κάθε καρέ…"), false);

            player.BeforeExportVideo();
            using (BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true })
            using (FormProgressBar progress = new FormProgressBar(true))
            {
                ProgressCancel cancel = KeepOpenWhileBusy(progress, worker);
                worker.ProgressChanged += (o, e) => progress.Update(e.ProgressPercentage, 100, true);
                worker.DoWork += (o, e) =>
                {
                    BackgroundWorker w = (BackgroundWorker)o;
                    using (SwimSwimmerDetector detector = new SwimSwimmerDetector())
                    using (SwimPoseNet pose = new SwimPoseNet())
                    {
                        SwimTechReader reader = new SwimTechReader { DetectEvery = 3 };
                        int pass = 0;
                        SwimTechReader.ImageSource source = sink =>
                        {
                            pass++;
                            ReadTechPilotFrames(video, from, end, step, width, height, pass == 1 ? stamps : null, sink, w);
                        };
                        SwimTechReading r = reader.Read(source, detector, pose, (stage, t) =>
                        {
                            double f = Math.Max(0, Math.Min(1, t / seconds));
                            w.ReportProgress((int)Math.Round(stage == "pose" ? 50 + 50 * f : 50 * f));
                        });
                        r.Fps = realFps / step;
                        r.VideoFps = realFps;
                        e.Result = w.CancellationPending ? null : r;
                    }
                };
                worker.RunWorkerCompleted += (o, e) =>
                {
                    error = e.Error;
                    cancelled = e.Error == null && (cancel.Asked || e.Result == null);
                    reading = e.Error == null ? e.Result as SwimTechReading : null;
                    progress.Close();
                };

                worker.RunWorkerAsync();
                progress.ShowDialog();
            }

            TechPilotRunning = false;
            video.AfterFrameEnumeration();
            if (player != null && !player.IsDisposed)
                player.AfterExportVideo();

            try
            {
                if (error != null)
                {
                    log.Error("Technical pilot reading failed.", error);
                    Status(SwimLang.T("🤖 Η ανάγνωση δεν έγινε: ") + error.Message, true);
                    return;
                }

                if (cancelled || reading == null)
                {
                    Status(SwimLang.T("🤖 Η ανάγνωση της στροφής ακυρώθηκε."), false);
                    return;
                }

                reading.Video = System.IO.Path.GetFileName(video.FilePath ?? "");
                SwimTurnEvents ev = SwimTurnReader.Find(reading, kind);

                // The head at −5 / +5 from the backstroke flags in the external video of the same take (when there is one).
                SwimFlagHead flags = null;
                List<string> flagNotes = new List<string>();
                if (ev != null && ev.Wall.HasValue && ev.Pushoff.HasValue)
                {
                    string external = SiblingExternalVideo(video.FilePath);
                    if (external == null)
                        flagNotes.Add("±5μ.: δεν βρέθηκε δίπλα το βίντεο της εξωτερικής κάμερας");
                    else
                        flags = ReadTurnFlags(external, reading, ev, from, flagNotes);
                }

                ProposeTurn(ev, from, stamps, flags, flagNotes);
            }
            finally
            {
                RefreshTechPilotUi();
                InvalidatePlayer();
            }
        }

        /// <summary>The kind of the turn from the style of the analysis (medley and none: from the video).</summary>
        public static SwimTurnKind TurnKindOfStyle(string style)
        {
            switch (style)
            {
                case "fs":
                case "bk":
                    return SwimTurnKind.Flip;
                case "br":
                case "fly":
                    return SwimTurnKind.Open;
                default:
                    return SwimTurnKind.Unknown;
            }
        }

        /// <summary>Background thread: the images from 'from' to 'end', one in 'step', to the sink (the timestamps on the first pass).</summary>
        private void ReadTechPilotFrames(VideoReader reader, long from, long end, int step, int width, int height, List<long> stamps,
            SwimTechReader.ImageSink sink, BackgroundWorker worker)
        {
            reader.BeforeFrameEnumeration();
            long current = reader.Current != null ? reader.Current.Timestamp : from;
            bool hasMore = reader.MoveTo(current, from);
            while (!worker.CancellationPending)
            {
                VideoFrame frame = reader.Current;
                if (frame == null || frame.Image == null || frame.Timestamp > end)
                    break;

                if (stamps != null)
                    stamps.Add(frame.Timestamp);
                using (OpenCvSharp.Mat bgr = SwimStrokeVideo.ToWorking(frame.Image, width, height))
                    sink(bgr, Timing.Seconds(frame.Timestamp, from));
                if (!hasMore)
                    break;
                hasMore = reader.MoveNext(step - 1, true);
            }
        }

        /// <summary>
        /// The external side video of the same take: in the same folder, the name with the word of the underwater camera replaced
        /// by the one of the external camera (…_Υποβρύχια.MP4 → …_Εξωτερική.MP4, …_uw.MP4 → …_ext.MP4); null when there is none.
        /// </summary>
        public static string SiblingExternalVideo(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            try
            {
                string folder = System.IO.Path.GetDirectoryName(path);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                string extension = System.IO.Path.GetExtension(path);
                if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
                    return null;

                foreach (string[] words in cameraWords)
                {
                    // A short word only as a whole word (…_uw, uw_…), not inside another one.
                    string pattern = words[0].Length <= 3
                        ? "(?<![\\p{L}\\p{N}])" + System.Text.RegularExpressions.Regex.Escape(words[0]) + "(?![\\p{L}\\p{N}])"
                        : System.Text.RegularExpressions.Regex.Escape(words[0]);
                    System.Text.RegularExpressions.MatchCollection found = System.Text.RegularExpressions.Regex.Matches(name, pattern,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                    if (found.Count == 0)
                        continue;

                    System.Text.RegularExpressions.Match last = found[found.Count - 1];
                    string word = last.Value == last.Value.ToUpperInvariant() && last.Value != last.Value.ToLowerInvariant() ? words[1].ToUpperInvariant() : words[1];
                    string other = name.Substring(0, last.Index) + word + name.Substring(last.Index + last.Length);
                    foreach (string e in new[] { extension, ".MP4", ".mp4", ".MOV", ".mov" })
                    {
                        string candidate = System.IO.Path.Combine(folder, other + e);
                        if (System.IO.File.Exists(candidate) && !string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
                            return candidate;
                    }
                }
            }
            catch (Exception e)
            {
                log.Error("Could not look for the external video of the turn.", e);
            }

            return null;
        }

        /// <summary>
        /// The external video of the take read for the backstroke flags around the turn (SwimFlagReader, 10 images/s, 720 px), then
        /// the head at −5 / +5 in the underwater reading (SwimTurnReader.HeadAtFlags). The two files must be one synchronised
        /// export (the same length); null (and a note) when they are not, when the reading is cancelled or fails.
        /// </summary>
        private SwimFlagHead ReadTurnFlags(string path, SwimTechReading reading, SwimTurnEvents ev, long from, List<string> notes)
        {
            double hsf = Timing.HighSpeedFactor <= 0 ? 1.0 : Timing.HighSpeedFactor;
            if (Math.Abs(hsf - 1) > 1e-3)
            {
                notes.Add("±5μ.: το βίντεο είναι σε αργή κίνηση (η εξωτερική κάμερα δεν διαβάζεται μαζί του)");
                return null;
            }

            VideoReader uw = frameServer.VideoReader;
            VideoReader ext = OpenVideoFile(path);
            if (ext == null)
            {
                notes.Add("±5μ.: το βίντεο της εξωτερικής κάμερας δεν άνοιξε");
                return null;
            }

            try
            {
                double uwRate = uw.Info.AverageTimeStampsPerSeconds, extRate = ext.Info.AverageTimeStampsPerSeconds;
                if (uwRate <= 0 || extRate <= 0 || reading.Frames.Count == 0)
                    return null;

                double uwLength = uw.Info.DurationTimeStamps / uwRate;
                double extLength = ext.Info.DurationTimeStamps / extRate;
                if (Math.Abs(uwLength - extLength) > FlagsSyncTolerance)
                {
                    notes.Add("±5μ.: η εξωτερική κάμερα δεν έχει τη διάρκεια της υποβρύχιας (δεν είναι συγχρονισμένες)");
                    return null;
                }

                // The reading's seconds are from 'from'; the external video's from its first image, the same moment as the
                // underwater video's first image.
                double offset = (from - uw.Info.FirstTimeStamp) / uwRate;
                double start = Math.Max(reading.Frames[0].T, ev.Wall.Value - FlagsBefore) + offset;
                double stop = Math.Min(Math.Min(reading.Frames[reading.Frames.Count - 1].T, ev.Pushoff.Value + FlagsAfter) + offset, extLength);
                if (stop - start < 2)
                    return null;

                long extFrom = ext.Info.FirstTimeStamp + (long)Math.Round(start * extRate);
                long extEnd = ext.Info.FirstTimeStamp + (long)Math.Round(stop * extRate);
                int step = Math.Max(1, (int)Math.Round(ext.Info.FramesPerSeconds / FlagsFps));
                Size size = ext.Info.ReferenceSize;
                int width = Math.Min(FlagsWidth, Math.Max(64, size.Width));
                int height = Math.Max(2, (int)Math.Round(width * (double)size.Height / Math.Max(1, size.Width)) / 2 * 2);

                List<SwimFlagFrame> frames = new List<SwimFlagFrame>();
                Exception error = null;
                bool cancelled = false;
                TechPilotRunning = true;
                Status(SwimLang.T("🤖 Η εξωτερική κάμερα: οι σημαίες του ύπτιου (5μ.) πριν και μετά τον τοίχο…"), false);
                using (BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true })
                using (FormProgressBar progress = new FormProgressBar(true))
                {
                    ProgressCancel cancel = KeepOpenWhileBusy(progress, worker);
                    worker.ProgressChanged += (o, e) => progress.Update(e.ProgressPercentage, 100, true);
                    worker.DoWork += (o, e) =>
                    {
                        BackgroundWorker w = (BackgroundWorker)o;
                        OpenCvSharp.Mat prev = null;
                        try
                        {
                            ext.BeforeFrameEnumeration();
                            bool hasMore = ext.MoveTo(extFrom, extFrom);
                            while (!w.CancellationPending)
                            {
                                VideoFrame frame = ext.Current;
                                if (frame == null || frame.Image == null || frame.Timestamp > extEnd)
                                    break;

                                double t = (frame.Timestamp - ext.Info.FirstTimeStamp) / extRate - offset;
                                OpenCvSharp.Mat cur = SwimStrokeVideo.ToWorking(frame.Image, width, height);
                                if (prev != null)
                                {
                                    frames.Add(SwimFlagReader.Frame(prev, cur, t));
                                    prev.Dispose();
                                }

                                prev = cur;
                                w.ReportProgress((int)Math.Max(0, Math.Min(100, 100.0 * (frame.Timestamp - extFrom) / Math.Max(1, extEnd - extFrom))));
                                if (!hasMore)
                                    break;
                                hasMore = ext.MoveNext(step - 1, true);
                            }
                        }
                        finally
                        {
                            if (prev != null)
                                prev.Dispose();
                            ext.AfterFrameEnumeration();
                        }

                        e.Result = w.CancellationPending ? null : frames;
                    };
                    worker.RunWorkerCompleted += (o, e) =>
                    {
                        error = e.Error;
                        cancelled = e.Error == null && (cancel.Asked || e.Result == null);
                        progress.Close();
                    };

                    worker.RunWorkerAsync();
                    progress.ShowDialog();
                }

                TechPilotRunning = false;
                if (error != null)
                {
                    log.Error("Reading of the flags in the external video failed.", error);
                    notes.Add("±5μ.: η ανάγνωση της εξωτερικής κάμερας δεν έγινε");
                    return null;
                }

                if (cancelled)
                {
                    notes.Add("±5μ.: η ανάγνωση της εξωτερικής κάμερας ακυρώθηκε");
                    return null;
                }

                SwimFlagHead head = SwimTurnReader.HeadAtFlags(reading, ev, frames, width);
                if (head.Passes == 0)
                    notes.Add("±5μ.: δεν φαίνονται οι σημαίες του ύπτιου στην εξωτερική κάμερα");
                else if (!head.In.HasValue && !head.Out.HasValue)
                    notes.Add("±5μ.: το κεφάλι δεν βρέθηκε στο επίπεδο των σημαιών");
                return head;
            }
            finally
            {
                TechPilotRunning = false;
                CloseReader(ext);
            }
        }

        /// <summary>A video file opened for a reading of its own (frames decoded on demand), null if it can't be.</summary>
        private static VideoReader OpenVideoFile(string path)
        {
            VideoReader reader = null;
            try
            {
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                    return null;

                reader = VideoTypeManager.GetVideoReader(System.IO.Path.GetExtension(path));
                if (reader == null)
                    return null;

                reader.Options = VideoOptions.Default;
                if (reader.Open(path) != OpenVideoResult.Success)
                {
                    CloseReader(reader);
                    return null;
                }

                return reader;
            }
            catch (Exception e)
            {
                log.Error("Could not open the external video of the turn.", e);
                CloseReader(reader);
                return null;
            }
        }

        /// <summary>The events of the turn as proposals (in the order of the video), then the check from the first.</summary>
        private void ProposeTurn(SwimTurnEvents ev, long from, List<long> stamps, SwimFlagHead flags, List<string> flagNotes)
        {
            if (ev == null || !ev.Wall.HasValue && !ev.Pushoff.HasValue || stamps.Count == 0)
            {
                string why = ev != null && ev.Notes.Count > 0 ? SwimLang.T(ev.Notes[0]) : SwimLang.T("δεν βρέθηκε η στροφή");
                Status(SwimLang.T("🤖 Δεν βρέθηκε στροφή στο βίντεο: ") + why + SwimLang.T(". Δοκίμασε με τη ζώνη εργασίας γύρω από τη στροφή."), true);
                return;
            }

            string batch = Guid.NewGuid().ToString("N");
            List<SwimProposal> made = new List<SwimProposal>();
            Func<double, long> stampOf = t =>
            {
                long best = stamps[0];
                double bestDiff = double.MaxValue;
                foreach (long s in stamps)
                {
                    double d = Math.Abs(Timing.Seconds(s, from) - t);
                    if (d < bestDiff)
                    {
                        bestDiff = d;
                        best = s;
                    }
                }

                return best;
            };
            Action<SwimMarkType, string, double?, int, double> add = (type, kindName, t, index, confidence) =>
            {
                if (!t.HasValue)
                    return;
                long ts = stampOf(t.Value);
                if (HasMarkNear(type, ts, type == SwimMarkType.Kick ? 0.12 : 0.3))
                    return;
                made.Add(new SwimProposal { Type = type, Timestamp = ts, Kind = kindName, Index = index, Batch = batch, Confidence = confidence });
            };

            bool open = ev.Kind == SwimTurnKind.Open;
            double guess = ev.KindFromVideo ? 0.15 : 0;
            add(SwimMarkType.Stroke, KindTechLastStroke, ev.LastStroke, 0, 0.6 - guess);
            add(SwimMarkType.RotationStart, KindTechRotation, open ? null : ev.RotationStart, 0, 0.8 - guess);
            add(SwimMarkType.WallContact, KindTechWall, ev.Wall, 0, (open ? 0.7 : 0.85) - guess - (ev.PlantFromTurn ? 0.2 : 0));
            add(SwimMarkType.FeetOff, KindTechPushoff, ev.Pushoff, 0, 0.85);
            for (int i = 0; i < ev.Kicks.Count; i++)
                add(SwimMarkType.Kick, KindTechKick, ev.Kicks[i], i + 1, 0.8);
            add(SwimMarkType.Breakout, KindTechBreakout, ev.Breakout, 0, ev.BreakoutBySurface ? 0.75 : ev.BreakoutByLine ? 0.5 : 0.4);
            add(SwimMarkType.Stroke, KindTechFirstStroke, ev.FirstStroke, 0, 0.7);
            if (flags != null)
            {
                add(SwimMarkType.TechSplit, KindTechHeadIn, flags.In, -5, FlagsConfidence);
                add(SwimMarkType.TechSplit, KindTechHeadOut, flags.Out, 5, FlagsConfidence);
            }

            made = made.OrderBy(p => p.Timestamp).ToList();

            if (made.Count == 0)
            {
                Status(SwimLang.T("🤖 Τα γεγονότα της στροφής που βρέθηκαν έχουν ήδη σημάδι."), false);
                return;
            }

            // The proposals of an earlier reading of the pilot (not answered) go: this reading replaces them.
            Race.Proposals.RemoveAll(p => p.Kind != null && p.Kind.StartsWith("tech.turn.", StringComparison.Ordinal));
            Race.Proposals.AddRange(made);
            Changed(false);
            RefreshProposalsUi();

            string kindText = ev.Kind == SwimTurnKind.Open ? SwimLang.T("ανοιχτή") : SwimLang.T("ανάποδη");
            List<string> remarks = ev.Notes.Concat(flagNotes ?? new List<string>()).ToList();
            string notes = remarks.Count > 0 ? " · " + string.Join(" · ", remarks.Select(SwimLang.T)) : "";
            BeginReview(made[0]);
            Status(string.Format(CultureInfo.InvariantCulture,
                SwimLang.T("🤖 Στροφή ({0}): {1} προτάσεις{2}. Enter: σωστό · ←/→ και Enter: διόρθωση · Delete: λάθος."),
                kindText, made.Count, notes), ev.Notes.Count > 0);
        }

        private void RefreshTechPilotUi()
        {
            if (form != null && !form.IsDisposed)
                form.RefreshSkeleton();
        }
    }
}
