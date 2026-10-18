#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Kinovea.Services;
using Kinovea.Video;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Learning from a manual analysis (without "Όλος ο αγώνας"): each mark of the analyst teaches the learning as his answer to a
    /// proposal of its event would. The first movement, the feet off the block, the entry and the feet off each wall teach at once,
    /// from their usual times. The checkpoints, the walls and the breakouts need the video: after one click on the swimmer (head,
    /// feet; the click of the automatic strokes is enough) the race is read in the background on a second reader of the video file,
    /// the analyst going on meanwhile, and what it finds waits hidden until the analyst marks its event. A mark corrected later
    /// replaces its answer, a mark undone or deleted takes it back: each event of a race counts once.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Reading in the background
        /// <summary>A reading of the race in the background and what it reads with (copies: the analyst goes on meanwhile).</summary>
        private sealed class QuietReading
        {
            public VideoReader Reader;
            public VideoSection Zone;
            public SwimRace Race;
            public SwimTiming Timing;
            public SwimLearning Learning;
            public string RaceId;
            public SwimVideoFile Video;
            public PointF Head;
            public PointF Feet;
            public long Click;
            public readonly List<LengthRead> Lengths = new List<LengthRead>();
            public BackgroundWorker Worker;
            public volatile bool Cancelled;
        }

        /// <summary>The reading in the background on this thread (null on the others): the race, timing, learning and reader it reads with.</summary>
        [ThreadStatic]
        private static QuietReading quietThread;

        /// <summary>The reading in the background running (UI thread), null if none.</summary>
        private QuietReading quietJob;

        /// <summary>The next two clicks on the swimmer start the reading in the background.</summary>
        private bool pickForQuiet;

        /// <summary>Races where the click was asked in this session (asked once).</summary>
        private readonly HashSet<string> quietAsked = new HashSet<string>();

        /// <summary>A mark of the last command taught the learning (a 🎓 after its message).</summary>
        private bool markTaught;

        private DateTime quietUiRefreshed = DateTime.MinValue;

        /// <summary>The reader of a reading: the one of the player, or the second one of the reading in the background.</summary>
        private VideoReader ReadingReader
        {
            get
            {
                QuietReading q = quietThread;
                return q != null ? q.Reader : frameServer.VideoReader;
            }
        }

        /// <summary>The part of the video a reading may read.</summary>
        private VideoSection ReadingZone
        {
            get
            {
                QuietReading q = quietThread;
                return q != null ? q.Zone : frameServer.VideoReader.WorkingZone;
            }
        }

        public bool QuietReadingRunning
        {
            get { return quietJob != null; }
        }

        /// <summary>For the panel: what the learning from the marks of this race is doing.</summary>
        public string QuietDescription
        {
            get
            {
                if (Race == null || Race.IsTraining)
                    return "";
                if (Settings == null || !Settings.LearnFromMarks)
                    return "κλειστή";
                if (quietJob != null)
                    return string.Format(CultureInfo.InvariantCulture, "🎓 διαβάζει τον αγώνα στο παρασκήνιο (μήκος {0})…", quietJob.Lengths.Count + 1);

                int taught = Race.Marks.Count(m => m.Taught != null);
                string marks = taught == 1 ? "1 σημάδι σου έγινε μάθηση" : taught + " σημάδια σου έγιναν μάθηση";
                if (Race.QuietRead)
                {
                    int waiting = Race.QuietProposals.Count(p => MarkOfQuietEvent(p) == null);
                    return string.Format(CultureInfo.InvariantCulture, "🎓 ο αγώνας διαβάστηκε · {0} · {1} σημεία περιμένουν τα πατήματά σου", marks, waiting);
                }

                return taught > 0 ? "🎓 " + marks + " · κλικ στον αθλητή για τα περάσματα και τις εξόδους" : "κλικ στον αθλητή: μαθαίνει και από τα περάσματα, τους τοίχους, τις εξόδους";
            }
        }

        public void SetLearnFromMarks(bool on)
        {
            Settings.LearnFromMarks = on;
            Settings.Save();
            if (!on)
                CancelQuietReading();
            if (form != null && !form.IsDisposed)
                form.RefreshQuiet();
        }

        /// <summary>The race can be read in the background now (once per race; not after a reading of the whole race).</summary>
        private bool CanReadQuietly()
        {
            return Settings != null && Settings.LearnFromMarks && HasVideo && Race != null && !Race.IsTraining && Race.StartTimestamp.HasValue &&
                PoolHasMarks && !Race.QuietRead && quietJob == null && !AutoStrokesRunning &&
                !Race.Proposals.Any(p => p.Kind == KindRaceSplit || p.Kind == KindRaceBreakout);
        }

        /// <summary>
        /// Manual analysis: after the first checkpoint the analyst is asked once for a click on the swimmer, for the reading in the
        /// background (Esc: not in this race).
        /// </summary>
        private void OfferQuietLearning()
        {
            if (Race == null || quietAsked.Contains(Race.Id) || !CanReadQuietly() || PickTarget != SwimPickTarget.None || reviewId != null)
                return;

            quietAsked.Add(Race.Id);
            BeginQuietLearning();
        }

        /// <summary>The next two clicks (head and feet of the swimmer) start the reading of the race in the background.</summary>
        public void BeginQuietLearning()
        {
            if (Race != null && Race.QuietRead && quietJob == null && !Race.IsTraining)
            {
                Status("🎓 Ο αγώνας έχει ήδη διαβαστεί για τη μάθηση: κάθε πέρασμα, τοίχος και έξοδος που μαρκάρεις τη διδάσκει.", false);
                return;
            }

            if (!CanReadQuietly())
            {
                if (Settings != null && !Settings.LearnFromMarks)
                    Status("Η μάθηση από τα πατήματα είναι κλειστή (καρτέλα 1 · Εκκίνηση).", true);
                else if (quietJob != null)
                    Status("🎓 Ο αγώνας διαβάζεται ήδη στο παρασκήνιο.", false);
                else if (Race != null && !Race.IsTraining && Race.StartTimestamp.HasValue && PoolHasMarks)
                    Status("Ο αγώνας έχει προτάσεις του «🤖 Όλος ο αγώνας»: οι απαντήσεις σου σε αυτές είναι η μάθηση.", false);
                else
                    Status("Μάθηση από τα πατήματα: πρώτα το σήμα εκκίνησης (G), σε πισίνα 50 ή 25 με σημάδια στα σχοινιά.", true);
                return;
            }

            BeginTrackSwimmer();
            if (!PickingTrack)
                return;

            pickForQuiet = true;
            Status("🎓 Για να μαθαίνει από τα πατήματά σου: σε ένα καρέ μετά την έξοδο από το νερό, κλικ στο κεφάλι του αθλητή και μετά " +
                "στα πόδια του (μία φορά σε αυτόν τον αγώνα). Ο αγώνας διαβάζεται στο παρασκήνιο, εσύ συνεχίζεις. Esc: όχι τώρα.", false);
        }

        /// <summary>The race read in the background from the swimmer clicked, on a second reader of the video file.</summary>
        private void StartQuietReading(PointF head, PointF feet, long click)
        {
            RefreshTiming();
            if (!CanReadQuietly() || !Timing.IsValid)
                return;

            VideoReader reader = OpenSecondReader();
            if (reader == null)
            {
                Status("🎓 Το βίντεο δεν άνοιξε δεύτερη φορά για την ανάγνωση στο παρασκήνιο: η μάθηση γίνεται μόνο από την εκκίνηση και τις αποκολλήσεις.", true);
                return;
            }

            QuietReading job = new QuietReading
            {
                Reader = reader,
                Zone = frameServer.VideoReader.WorkingZone,
                Race = Race.CopyForReading(),
                Timing = new SwimTiming
                {
                    AverageTimestampsPerFrame = Timing.AverageTimestampsPerFrame,
                    FrameIntervalMilliseconds = Timing.FrameIntervalMilliseconds,
                    HighSpeedFactor = Timing.HighSpeedFactor
                },
                Learning = SwimLearning.Load(),
                RaceId = Race.Id,
                Video = VideoFile,
                Head = head,
                Feet = feet,
                Click = click
            };

            BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            job.Worker = worker;
            worker.DoWork += (o, e) =>
            {
                quietThread = job;
                Thread thread = Thread.CurrentThread;
                ThreadPriority priority = thread.Priority;
                try
                {
                    // Below the player and the panel: the analyst goes on working.
                    thread.Priority = ThreadPriority.BelowNormal;
                    e.Result = ReadRace(job.Head, job.Feet, job.Click, job.Lengths, (BackgroundWorker)o);
                }
                finally
                {
                    thread.Priority = priority;
                    quietThread = null;
                }
            };
            worker.ProgressChanged += (o, e) =>
            {
                // The panel says which length is read, once a second at most.
                if ((DateTime.Now - quietUiRefreshed).TotalSeconds < 1 || form == null || form.IsDisposed)
                    return;
                quietUiRefreshed = DateTime.Now;
                form.RefreshQuiet();
            };
            worker.RunWorkerCompleted += (o, e) => QuietReadingDone(job, e);

            quietJob = job;
            worker.RunWorkerAsync();
            Status("🎓 Ο αγώνας διαβάζεται στο παρασκήνιο: συνέχισε την ανάλυση. Κάθε πέρασμα, τοίχος και έξοδος που μαρκάρεις θα διδάσκει το πρόγραμμα.", false);
            if (form != null && !form.IsDisposed)
                form.RefreshQuiet();
        }

        /// <summary>The video file opened again (frames decoded on demand), null if it can't be.</summary>
        private VideoReader OpenSecondReader()
        {
            VideoReader reader = null;
            try
            {
                VideoReader player = frameServer.VideoReader;
                string path = player.FilePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return null;

                reader = VideoTypeManager.GetVideoReader(Path.GetExtension(path));
                if (reader == null)
                    return null;

                VideoOptions o = player.Options;
                if (o != null)
                    reader.Options = new VideoOptions(o.ImageAspectRatio, o.ImageRotation, o.Demosaicing, o.Deinterlace);
                if (reader.Open(path) != OpenVideoResult.Success)
                {
                    CloseReader(reader);
                    return null;
                }

                // The file sets its own rotation when it opens: the frames as the player shows them (the clicks are on those).
                if (o != null)
                {
                    if (reader.CanChangeImageRotation && reader.Options.ImageRotation != o.ImageRotation)
                        reader.ChangeImageRotation(o.ImageRotation);
                    if (reader.CanChangeAspectRatio && reader.Options.ImageAspectRatio != o.ImageAspectRatio)
                        reader.ChangeAspectRatio(o.ImageAspectRatio);
                    if (reader.CanChangeDeinterlacing && reader.Options.Deinterlace != o.Deinterlace)
                        reader.ChangeDeinterlace(o.Deinterlace);
                }

                if (reader.Info.ReferenceSize != player.Info.ReferenceSize)
                {
                    CloseReader(reader);
                    return null;
                }

                return reader;
            }
            catch (Exception e)
            {
                log.Error("Could not open the video again for the reading in the background.", e);
                CloseReader(reader);
                return null;
            }
        }

        private static void CloseReader(VideoReader reader)
        {
            if (reader == null)
                return;

            try
            {
                if (reader.Loaded)
                    reader.Close();
            }
            catch (Exception e)
            {
                log.Error("Could not close the second reader of the video.", e);
            }
        }

        /// <summary>The reading in the background stops (another race or video, the learning turned off); what it read goes.</summary>
        private void CancelQuietReading()
        {
            QuietReading job = quietJob;
            if (job == null)
                return;

            job.Cancelled = true;
            quietJob = null;
            if (job.Worker != null && job.Worker.IsBusy)
                job.Worker.CancelAsync();
        }

        /// <summary>
        /// The reading in the background has finished (UI thread): what it found waits hidden for the marks of the analyst; the
        /// events he marked meanwhile teach now.
        /// </summary>
        private void QuietReadingDone(QuietReading job, RunWorkerCompletedEventArgs e)
        {
            // A reading with its progress window is running (its thread reads the learning, the answers would change it under it):
            // what was read waits for its end.
            if (!job.Cancelled && (AutoStrokesRunning || SkeletonRunning || StartLightRunning))
            {
                System.Windows.Forms.Timer wait = new System.Windows.Forms.Timer { Interval = 500 };
                wait.Tick += (o, a) =>
                {
                    if (!job.Cancelled && (AutoStrokesRunning || SkeletonRunning || StartLightRunning))
                        return;
                    wait.Stop();
                    wait.Dispose();
                    QuietReadingDone(job, e);
                };
                wait.Start();
                return;
            }

            if (quietJob == job)
                quietJob = null;

            try
            {
                if (job.Cancelled)
                    return;

                if (e.Error != null)
                {
                    log.Error("The reading of the race in the background failed.", e.Error);
                    Status("🎓 Η ανάγνωση του αγώνα στο παρασκήνιο σταμάτησε (" + e.Error.Message + "): η μάθηση γίνεται από την εκκίνηση και τις αποκολλήσεις.", false);
                    return;
                }

                // Another race or another video meanwhile: not this one's.
                if (Race == null || Race.Id != job.RaceId || VideoFile != job.Video)
                    return;

                if (job.Lengths.Count == 0)
                {
                    Status("🎓 Στο παρασκήνιο δεν βρέθηκε ο αθλητής από το κλικ (σημάδια σχοινιών): η μάθηση γίνεται από την εκκίνηση και τις αποκολλήσεις. " +
                        "Ξανά με «🎓 Κλικ στον αθλητή» σε ένα καθαρό καρέ.", false);
                    return;
                }

                string batch = "quiet-" + Guid.NewGuid().ToString("N");
                List<SwimProposal> made = new List<SwimProposal>();
                foreach (LengthRead l in job.Lengths)
                {
                    made.AddRange(SplitProposals(l, batch, true));
                    SwimProposal breakout = BreakoutProposal(l, batch, true);
                    if (breakout != null)
                        made.Add(breakout);
                }

                Race.QuietProposals.RemoveAll(x => made.Any(p => p.Kind == x.Kind && p.Index == x.Index));
                Race.QuietProposals.AddRange(made);

                // Read to the finish: once per race. Else (the swimmer lost after a wall, a click in a later length) another click
                // reads the rest.
                int lastWall = job.Lengths[job.Lengths.Count - 1].Session.Wall;
                Race.QuietRead = lastWall >= Race.Distance;

                // What the analyst marked meanwhile teaches now.
                int taught = 0;
                foreach (SwimProposal p in made)
                {
                    SwimMark mark = MarkOfQuietEvent(p);
                    if (mark != null && TeachQuiet(p, mark))
                        taught++;
                }

                MarkDirty();
                int waiting = made.Count(p => MarkOfQuietEvent(p) == null);
                Status(string.Format(CultureInfo.InvariantCulture,
                    "🎓 Ο αγώνας διαβάστηκε στο παρασκήνιο ({0} μήκη): {1} σημεία που είχες ήδη μαρκάρει έγιναν μάθηση, {2} περιμένουν τα επόμενα πατήματά σου.{3}",
                    job.Lengths.Count, taught, waiting, Race.QuietRead ? "" : string.Format(CultureInfo.InvariantCulture,
                        " Διαβάστηκε μέχρι τα {0}{1}: για τα υπόλοιπα, «🎓 Κλικ στον αθλητή» σε ένα καρέ μετά.", lastWall, Race.Unit)), false);
                RefreshProposalsUi();
                if (form != null && !form.IsDisposed)
                    form.RefreshQuiet();
            }
            finally
            {
                foreach (LengthRead l in job.Lengths)
                    l.Session.Dispose();
                CloseReader(job.Reader);
            }
        }
        #endregion

        #region Learning from the marks
        /// <summary>
        /// A mark of the analyst teaches the learning as his answer to the proposal of its event would (also when nothing was
        /// proposed): the hidden proposal of the reading in the background (checkpoint, wall, breakout), the usual time of the first
        /// movement, the feet off, the entry and the feet off a wall. An event with a proposal shown is taught by the answer to it.
        /// </summary>
        private void LearnFromMark(SwimMarkType type, long timestamp)
        {
            if (Race == null || Race.IsTraining || !Race.StartTimestamp.HasValue || !Timing.IsValid || Settings == null || !Settings.LearnFromMarks)
                return;

            // (A mark of the auto pilot not checked yet does not teach.)
            SwimMarkType markType = type == SwimMarkType.WallContact ? SwimMarkType.Split : type;
            SwimMark mark = Race.Marks.Where(m => m.Type == markType && SameFrame(m.Timestamp, timestamp)).OrderBy(m => Math.Abs(m.Timestamp - timestamp)).FirstOrDefault();
            if (mark == null || mark.Pilot != null)
                return;

            bool taught = false;
            SwimProposal quiet = QuietProposalFor(mark);
            if (quiet != null)
                taught = TeachQuiet(quiet, mark);

            taught |= TeachPrior(mark);

            // The event after it that the analyst marked before (the feet off before the first movement, the push-off before the
            // wall) is taught again with it, as the whole race would propose it.
            foreach (SwimMark next in PriorDependents(mark, false))
                Reteach(next);

            if (taught)
                markTaught = true;
        }

        /// <summary>The hidden proposal of the event of a mark: the checkpoint of its distance, the nearest breakout.</summary>
        private SwimProposal QuietProposalFor(SwimMark mark)
        {
            if (Race.QuietProposals.Count == 0)
                return null;
            if (mark.Type == SwimMarkType.Split)
                return Race.QuietProposals.FirstOrDefault(p => p.Kind == KindRaceSplit && p.Index == mark.Distance);
            if (mark.Type == SwimMarkType.Breakout)
                return Race.QuietProposals.Where(p => p.Kind == KindRaceBreakout).OrderBy(p => Math.Abs(p.Timestamp - mark.Timestamp)).FirstOrDefault();
            return null;
        }

        /// <summary>The mark of the event of a hidden proposal, null if not marked yet.</summary>
        private SwimMark MarkOfQuietEvent(SwimProposal p)
        {
            if (p.Kind == KindRaceSplit)
                return Race.GetSplit(p.Index);
            if (p.Kind == KindRaceBreakout)
            {
                SwimMark nearest = Race.Marks.Where(m => m.Type == SwimMarkType.Breakout).OrderBy(m => Math.Abs(m.Timestamp - p.Timestamp)).FirstOrDefault();
                return nearest != null && Math.Abs(Timing.Seconds(nearest.Timestamp, p.Timestamp)) <= QuietWindow ? nearest : null;
            }

            return null;
        }

        /// <summary>A hidden proposal and the mark of its event further apart than this (s) are not the same event (the swimmer was lost).</summary>
        private const double QuietWindow = 1.5;

        private bool TeachQuiet(SwimProposal p, SwimMark mark)
        {
            if (mark.Pilot != null || Race.Proposals.Any(x => x.Kind == p.Kind && x.Index == p.Index) || Math.Abs(Timing.Seconds(mark.Timestamp, p.Timestamp)) > QuietWindow)
                return false;

            Answer(p, mark.Timestamp, true);
            mark.Taught = AnswerKey(p);
            return true;
        }

        /// <summary>A mark of an event with a usual time teaches it (within half a second of it: further, it is another event).</summary>
        private bool TeachPrior(SwimMark mark)
        {
            if (mark.Pilot != null)
                return false;

            SwimProposal p = PriorEventFor(mark);
            if (p == null || Race.Proposals.Any(x => x.Kind == p.Kind && x.Index == p.Index) || Math.Abs(Timing.Seconds(mark.Timestamp, p.Timestamp)) > 0.5)
                return false;

            Answer(p, mark.Timestamp, true);
            mark.Taught = AnswerKey(p);
            return true;
        }

        /// <summary>
        /// The proposal its event would have had in the whole race (its usual time moved by the learning): the first movement, the
        /// feet off and the entry from the start signal and from the events kept before them, the feet off a wall from the wall.
        /// Null for the other marks and for a second mark of the same event (the first one is the event).
        /// </summary>
        private SwimProposal PriorEventFor(SwimMark mark)
        {
            long start = Race.StartTimestamp.Value;
            long to = start + RealSpan(StartEventsSeconds);
            if ((mark.Type == SwimMarkType.Reaction || mark.Type == SwimMarkType.FeetOff || mark.Type == SwimMarkType.Entry) && mark.Timestamp > start && mark.Timestamp <= to)
            {
                if (FirstStartMark(mark.Type) != mark)
                    return null;

                string kind = mark.Type == SwimMarkType.Reaction ? KindRaceReaction : mark.Type == SwimMarkType.FeetOff ? KindRaceBlock : KindRaceEntry;
                SwimProposal p = PriorEventProposal(kind, mark.Type, 0, CueFromStart, start, SwimPresets.StyleAt(Race, Race.Pool), "", "marks");
                if (p == null)
                    return null;

                List<SwimProposal> one = new List<SwimProposal> { p };
                SwimMark reaction = FirstStartMark(SwimMarkType.Reaction);
                SwimMark block = FirstStartMark(SwimMarkType.FeetOff);
                if (kind == KindRaceBlock && reaction != null)
                    MoveEventProposals(KindRaceReaction, 0, reaction.Timestamp, one);
                else if (kind == KindRaceEntry && block != null)
                    MoveEventProposals(KindRaceBlock, 0, block.Timestamp, one);
                return p;
            }

            if (mark.Type == SwimMarkType.FeetOff)
            {
                SwimMark wall = WallBefore(mark);
                if (wall == null || FirstPushoff(wall) != mark)
                    return null;

                string wallKind = WallKind(wall.Distance);
                if (wallKind == "finish")
                    return null;

                return PriorEventProposal(KindRacePushoff, SwimMarkType.FeetOff, wall.Distance, CueFromWall, wall.Timestamp,
                    SwimPresets.StyleAt(Race, wall.Distance), wallKind, "marks");
            }

            return null;
        }

        /// <summary>
        /// The marks taught again after this one: the feet off after the first movement, the entry after the feet off, the push-off
        /// after its wall. 'removed': the mark is gone (its event is taught again without it).
        /// </summary>
        private List<SwimMark> PriorDependents(SwimMark mark, bool removed)
        {
            List<SwimMark> next = new List<SwimMark>();
            if (Race == null || !Race.StartTimestamp.HasValue)
                return next;

            long start = Race.StartTimestamp.Value;
            bool inStart = mark.Timestamp > start && mark.Timestamp <= start + RealSpan(StartEventsSeconds);
            SwimMark dependent = null;
            if (mark.Type == SwimMarkType.Reaction && inStart && (removed || FirstStartMark(SwimMarkType.Reaction) == mark))
                dependent = FirstStartMark(SwimMarkType.FeetOff);
            else if (mark.Type == SwimMarkType.FeetOff && inStart && (removed || FirstStartMark(SwimMarkType.FeetOff) == mark))
                dependent = FirstStartMark(SwimMarkType.Entry);
            else if (mark.Type == SwimMarkType.Split && IsTurnWall(mark.Distance))
                dependent = FirstPushoff(mark);

            if (dependent != null && dependent != mark)
                next.Add(dependent);
            return next;
        }

        /// <summary>A mark taught again from the marks now (or its answer forgotten when it is not that event any more).</summary>
        private void Reteach(SwimMark mark)
        {
            if (TeachPrior(mark) || string.IsNullOrEmpty(mark.Taught))
                return;

            string key = mark.Taught;
            mark.Taught = null;
            if (!Race.Marks.Any(m => m.Taught == key))
            {
                Learning.Forget(key);
                Learning.Save();
            }
        }

        /// <summary>The start moved: the first movement, the feet off and the entry are taught again from the new signal.</summary>
        private void RecheckStartLessons()
        {
            if (Race == null || Race.IsTraining || !Race.StartTimestamp.HasValue || !Timing.IsValid || Settings == null || !Settings.LearnFromMarks)
                return;

            foreach (SwimMark m in Race.Marks.Where(x => x.Type == SwimMarkType.Reaction || x.Type == SwimMarkType.Entry ||
                (x.Type == SwimMarkType.FeetOff && WallBefore(x) == null)).OrderBy(x => x.Timestamp).ToList())
            {
                if (!string.IsNullOrEmpty(m.Taught) || FirstStartMark(m.Type) == m)
                    Reteach(m);
            }
        }

        private SwimMark FirstStartMark(SwimMarkType type)
        {
            long start = Race.StartTimestamp.Value;
            long to = start + RealSpan(StartEventsSeconds);
            return Race.Marks.Where(m => m.Type == type && m.Timestamp > start && m.Timestamp <= to).OrderBy(m => m.Timestamp).FirstOrDefault();
        }

        private bool IsTurnWall(int distance)
        {
            return distance > 0 && distance < Race.Distance && Race.Pool > 0 && distance % Race.Pool == 0;
        }

        /// <summary>The wall of the turn a feet off belongs to (contact at most 0.2 s after it, 2.5 s before it), null if none.</summary>
        private SwimMark WallBefore(SwimMark feetOff)
        {
            return Race.Marks.Where(m => m.Type == SwimMarkType.Split && IsTurnWall(m.Distance) &&
                    feetOff.Timestamp >= m.Timestamp - RealSpan(0.2) && feetOff.Timestamp <= m.Timestamp + RealSpan(2.5))
                .OrderByDescending(m => m.Timestamp).FirstOrDefault();
        }

        /// <summary>The first feet off after a wall (as the push-off of the whole race is searched).</summary>
        private SwimMark FirstPushoff(SwimMark wall)
        {
            return Race.Marks.Where(m => m.Type == SwimMarkType.FeetOff && m.Timestamp >= wall.Timestamp - RealSpan(0.2) && m.Timestamp <= wall.Timestamp + RealSpan(2.5))
                .OrderBy(m => m.Timestamp).FirstOrDefault();
        }

        /// <summary>
        /// The event of the race a proposal is about, for its answer (see SwimLearningRecord.Key): the same event answered again replaces
        /// the answer. Null for the proposals that are not one event of a race (the start light, the technical analysis).
        /// </summary>
        private string AnswerKey(SwimProposal p)
        {
            if (Race == null || p == null || !IsWholeRaceKind(p.Kind))
                return null;

            string race = Race.Id.Length > 12 ? Race.Id.Substring(0, 12) : Race.Id;
            return SwimLearning.VideoCode(videoPath) + "|" + race + "|" + p.Kind + "|" + p.Index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A mark is gone (undone, deleted, replaced): the answer it gave goes too, another mark of the same event teaches it again,
        /// and the events taught from it (the feet off from the first movement, the push-off from the wall) are taught again without it.
        /// </summary>
        private void ForgetTaught(SwimMark mark)
        {
            if (mark == null || Race == null)
                return;

            string key = mark.Taught;
            mark.Taught = null;
            if (!string.IsNullOrEmpty(key))
            {
                Learning.Forget(key);
                Learning.Save();
                foreach (SwimMark other in Race.Marks.Where(m => m.Taught == key).ToList())
                {
                    other.Taught = null;
                    LearnFromMark(other.Type, other.Timestamp);
                }
            }

            if (Settings != null && Settings.LearnFromMarks && !Race.IsTraining && Race.StartTimestamp.HasValue && Timing.IsValid)
            {
                foreach (SwimMark next in PriorDependents(mark, true))
                    Reteach(next);
            }
        }
        #endregion
    }
}
