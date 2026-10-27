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
                ProposeTurn(ev, from, stamps);
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

        /// <summary>The events of the turn as proposals (in the order of the video), then the check from the first.</summary>
        private void ProposeTurn(SwimTurnEvents ev, long from, List<long> stamps)
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
            string notes = ev.Notes.Count > 0 ? " · " + string.Join(" · ", ev.Notes.Select(SwimLang.T)) : "";
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
