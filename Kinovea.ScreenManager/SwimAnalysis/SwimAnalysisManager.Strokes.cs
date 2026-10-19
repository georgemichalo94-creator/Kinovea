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
using System.Media;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Kinovea.Video;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Strokes counted at each checkpoint.
    ///
    /// With "Χεριές σε κάθε πέρασμα" on, the analyst clicks the head and the feet of the swimmer after each breakout:
    /// that starts the follow of the lap (stroke session). At each checkpoint marked afterwards the video of the lap is
    /// read up to the checkpoint (and a little after it, for the fraction of the stroke at the checkpoint), the strokes
    /// are found (SwimStrokeDetector) and placed as stroke marks, and the count of the segment is shown: Enter confirms
    /// it, or the analyst types the right number. The confirmed or typed counts are kept in SwimRace.StrokeCounts and
    /// have priority over the count from the marks (in the panel, the sheet and the PDF). The session ends at the wall.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Strokes at each checkpoint
        /// <summary>Width of the frames given to the stroke detector (pixels).</summary>
        private const int StrokeWorkWidth = 960;

        /// <summary>Video read after a checkpoint (s): the next stroke gives the fraction of the stroke at the checkpoint.</summary>
        private const double StrokeLookahead = 1.5;

        /// <summary>Video read from the start of the lap before the first count (s): enough strokes for the stroke cycle.</summary>
        private const double StrokeMinSpan = 4.5;

        private const string StrokeBatchPrefix = "auto:";

        private PointF autoStrokeHead;
        private long autoStrokeFrame;
        private StrokeSession strokeSession;

        /// <summary>True while the video is read for the strokes.</summary>
        public bool AutoStrokesRunning { get; private set; }

        /// <summary>Stroke cycle (s) of the lap followed or of the last one, 0 if none.</summary>
        public double LastAutoCycle { get; private set; }

        /// <summary>Strokes of the race placed by the automatic detection.</summary>
        public int AutoStrokeCount
        {
            get { return Race == null ? 0 : Race.Marks.Count(m => m.Type == SwimMarkType.Stroke && m.Batch != null); }
        }

        public bool PickingStrokes
        {
            get { return PickTarget == SwimPickTarget.StrokeHead || PickTarget == SwimPickTarget.StrokeFeet; }
        }

        /// <summary>A lap of this race is followed: its strokes are counted at each checkpoint up to the wall.</summary>
        public bool StrokeSessionActive
        {
            get { return SessionFor(Race) != null; }
        }

        /// <summary>Wall at the end of the lap followed, 0 if none.</summary>
        public int StrokeSessionWall
        {
            get
            {
                StrokeSession s = SessionFor(Race);
                return s == null ? 0 : s.Wall;
            }
        }

        private sealed class StrokeSession : IDisposable
        {
            public string RaceId;
            public string VideoPath;
            public string Batch;
            public SwimStrokeDetector Detector;
            public readonly List<long> Timestamps = new List<long>();
            public long Start;
            public int Width;
            public int Height;
            public string Style;
            public int Step;
            public double Fps;
            public int Wall;

            /// <summary>The wall marked before the lap followed (or the start of the race).</summary>
            public long LapStart;

            /// <summary>Shortest time of the lap from the length before it (s, 0: no limit): no wall before it.</summary>
            public double Shortest;

            /// <summary>Time of the length before it read with it (s, 0: none).</summary>
            public double PreviousSeconds;

            /// <summary>Strokes deleted by the analyst in this lap (✕): no automatic stroke there again.</summary>
            public readonly List<long> Removed = new List<long>();

            public OpenCvSharp.Mat PreviousGray;
            public double Cycle;
            public bool Ropes;

            public long LastRead
            {
                get { return Timestamps.Count > 0 ? Timestamps[Timestamps.Count - 1] : long.MinValue; }
            }

            public void Dispose()
            {
                if (PreviousGray != null)
                {
                    PreviousGray.Dispose();
                    PreviousGray = null;
                }
            }
        }

        private StrokeSession SessionFor(SwimRace race)
        {
            StrokeSession s = strokeSession;
            if (s == null)
                return null;

            if (race == null || s.RaceId != race.Id || s.VideoPath != videoPath)
            {
                // Another race or another video: the lap followed is over.
                EndStrokeSession();
                return null;
            }

            return s;
        }

        /// <summary>Strokes at each checkpoint on or off (setting of the panel).</summary>
        public void SetAutoStrokes(bool on)
        {
            Settings.AutoStrokes = on;
            Settings.Save();
            if (!on)
            {
                if (PickingStrokes && !PickingTrack)
                    CancelPick();
                EndStrokeSession();
                Status("Χεριές σε κάθε πέρασμα: κλειστό.", false);
            }
            else
            {
                Status("Χεριές σε κάθε πέρασμα: μετά την έξοδο από το νερό (B) κάνε κλικ στο κεφάλι και στα πόδια του αθλητή· " +
                    "σε κάθε πέρασμα (S) θα δεις τις χεριές του τμήματος για επιβεβαίωση.", false);
            }

            if (form != null && !form.IsDisposed)
                form.RefreshAutoStrokes();
        }

        private void EndStrokeSession()
        {
            if (strokeSession == null)
                return;

            strokeSession.Dispose();
            strokeSession = null;
            if (form != null && !form.IsDisposed)
                form.RefreshAutoStrokes();
        }

        #region Choice of the swimmer
        /// <summary>
        /// The next two clicks on the video are the head and the feet of the swimmer: the lap is followed from this frame.
        /// Called by the panel button and, with the counting on, after the breakout.
        /// </summary>
        public void BeginAutoStrokes()
        {
            // Again while choosing: cancel. While choosing the swimmer to follow: the strokes instead (they keep his place too).
            if (PickingStrokes && !PickingTrack)
            {
                CancelPick();
                return;
            }

            if (AutoStrokesRunning)
                return;

            if (!HasVideo || Race == null)
            {
                Status("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής.", true);
                return;
            }

            if (Race.IsTraining)
            {
                Status("Οι χεριές σε κάθε πέρασμα είναι για την ανάλυση αγώνα (με σημεία ελέγχου).", true);
                return;
            }

            pendingMeasure = SwimPickTarget.None;
            calibrationClicks.Clear();
            angleClicks.Clear();
            pickForTrack = false;
            pickForRace = false;
            pickForQuiet = false;
            SetPick(SwimPickTarget.StrokeHead);
            Status("Χεριές: κάνε κλικ στο κεφάλι του αθλητή (Esc: χωρίς αυτόματη μέτρηση σε αυτό το μήκος).", false);
            InvalidatePlayer();
        }

        private void AutoStrokeClick(PointF point, long timestamp)
        {
            // A click instead of the swimmer proposed for the whole race: this one (a swimmer of a lane proposed: the lane clicked
            // teaches the numbering of the pool once the reading of the race starts from it).
            if (PickTarget == SwimPickTarget.StrokeHead)
                lanePickClicked = pickForRace ? autoPick : null;
            autoPick = null;
            if (PickTarget == SwimPickTarget.StrokeHead || !SameFrame(autoStrokeFrame, timestamp))
            {
                bool moved = PickTarget == SwimPickTarget.StrokeFeet;
                autoStrokeHead = point;
                autoStrokeFrame = timestamp;
                SetPick(SwimPickTarget.StrokeFeet);
                Status(moved ? "Το καρέ άλλαξε: αυτό το κλικ είναι το κεφάλι. Τώρα κλικ στα πόδια του αθλητή, στο ίδιο καρέ." :
                    (PickingTrack ? "Ακολούθηση" : "Χεριές") + ": τώρα κλικ στα πόδια του αθλητή, στο ίδιο καρέ.", moved);
                InvalidatePlayer();
                return;
            }

            Size reference = frameServer.VideoReader.Info.ReferenceSize;
            double dx = point.X - autoStrokeHead.X;
            double dy = point.Y - autoStrokeHead.Y;
            if (Math.Abs(dx) < Math.Max(8, reference.Width * 0.02) || Math.Abs(dy) > 0.6 * Math.Abs(dx))
            {
                SystemSounds.Exclamation.Play();
                Status("Τα πόδια πρέπει να είναι πίσω από το κεφάλι, κατά μήκος της διαδρομής (αριστερά ή δεξιά). Κάνε ξανά κλικ στα πόδια.", true);
                return;
            }

            bool track = pickForTrack;
            bool race = pickForRace;
            bool quiet = pickForQuiet;
            pickForTrack = false;
            pickForRace = false;
            pickForQuiet = false;
            SetPick(SwimPickTarget.None);
            InvalidatePlayer();
            if (track && quiet)
            {
                StartQuietReading(autoStrokeHead, point, autoStrokeFrame);
            }
            else if (track && race)
            {
                LearnLaneFromClick(lanePickClicked, autoStrokeHead, autoStrokeFrame);
                lanePickClicked = null;
                AutoRace(autoStrokeHead, point, autoStrokeFrame);
            }
            else if (track)
            {
                TrackSwimmer(autoStrokeHead, point, autoStrokeFrame);
            }
            else
            {
                // The swimmer clicked for the strokes is also the one of the reading in the background (no other click).
                PointF head = autoStrokeHead;
                long frame = autoStrokeFrame;
                StartStrokeSession(head, point, frame);
                if (CanReadQuietly())
                {
                    quietAsked.Add(Race.Id);
                    StartQuietReading(head, point, frame);
                }
            }
        }

        private void StartStrokeSession(PointF head, PointF feet, long from)
        {
            RefreshTiming();
            if (!Timing.IsValid || Race == null)
            {
                Status("Δεν είναι γνωστός ο ρυθμός καρέ του βίντεο: η αυτόματη μέτρηση δεν μπορεί να γίνει.", true);
                return;
            }

            EndStrokeSession();

            StrokeSession s = CreateSession(head, feet, from);
            strokeSession = s;
            LastAutoCycle = 0;

            Status(string.Format(CultureInfo.InvariantCulture,
                "Χεριές: ο αθλητής επιλέχθηκε. Σε κάθε πέρασμα μέχρι τον τοίχο των {0}{1} θα δεις τις χεριές του τμήματος.",
                s.Wall, UnitLabel), false);
            if (form != null && !form.IsDisposed)
                form.RefreshAutoStrokes();

            // Checkpoints of the lap already marked after this frame: their strokes now, one after the other.
            List<SwimMark> marked = Race.Marks
                .Where(m => m.Type == SwimMarkType.Split && m.Timestamp > from && m.Distance <= s.Wall)
                .OrderBy(m => m.Timestamp)
                .ToList();
            foreach (SwimMark split in marked)
            {
                if (SessionFor(Race) == null)
                    break;
                if (!PromptSegmentStrokes(split, true))
                    break;
            }
        }

        /// <summary>A reading of the lap from this frame, with the detector set on the swimmer clicked (head, feet).</summary>
        private StrokeSession CreateSession(PointF head, PointF feet, long from)
        {
            return CreateSession(head, feet, from, null, 0);
        }

        /// <summary>
        /// The same, with the lane already known (working coordinates of the first frame), as after a wall. wall: the wall at the
        /// end of the length read (0: the next one after the checkpoints marked), whose style the detector takes (each length of
        /// the medley in its own style also when the reading of the whole race has not marked the walls before it yet).
        /// </summary>
        private StrokeSession CreateSession(PointF head, PointF feet, long from, SwimStrokeLane? lane, int virtualRope, int wall = 0)
        {
            double hsf = Timing.HighSpeedFactor <= 0 ? 1.0 : Timing.HighSpeedFactor;
            double realFps = 1000.0 * hsf / Timing.FrameIntervalMilliseconds;
            int step = Math.Max(1, (int)Math.Round(realFps / 60.0));

            Size reference = ReadingReader.Info.ReferenceSize;
            int width = StrokeWorkWidth;
            int height = Math.Max(2, (int)Math.Round(width * (double)reference.Height / reference.Width / 2.0) * 2);
            double sx = (double)width / reference.Width;
            double sy = (double)height / reference.Height;

            StrokeSession s = new StrokeSession();
            s.RaceId = Race.Id;
            s.VideoPath = videoPath;
            s.Batch = StrokeBatchPrefix + Guid.NewGuid().ToString("N");
            s.Start = from;
            s.Width = width;
            s.Height = height;
            s.Step = step;
            s.Fps = realFps / step;
            s.Wall = wall > 0 ? wall : NextWallDistance(from);
            s.LapStart = LapStartTimestamp(from);
            s.Style = SwimPresets.StyleAt(Race, s.Wall);
            if (s.Style == "im")
                s.Style = "fs";
            s.Detector = lane.HasValue
                ? new SwimStrokeDetector(width, height, head.X * sx, head.Y * sy, feet.X * sx, feet.Y * sy, s.Style, s.Fps, lane.Value, virtualRope)
                : new SwimStrokeDetector(width, height, head.X * sx, head.Y * sy, feet.X * sx, feet.Y * sy, s.Style, s.Fps);
            return s;
        }
        #endregion

        #region Counting at a checkpoint
        /// <summary>
        /// A checkpoint has just been marked (counting on): strokes of the segment that ends there.
        /// </summary>
        private void StrokesAfterSplit(long timestamp)
        {
            if (Race == null || Race.IsTraining)
                return;

            SwimMark split = Race.Marks
                .Where(m => m.Type == SwimMarkType.Split && SameFrame(m.Timestamp, timestamp))
                .OrderBy(m => m.Distance)
                .LastOrDefault();
            if (split == null)
                return;

            PromptSegmentStrokes(split, false);
        }

        /// <summary>
        /// Strokes of the segment that ends at this checkpoint: with a lap followed, reads the video of the lap up to the
        /// checkpoint and places the strokes, then asks the analyst to confirm or correct the count.
        /// Returns false if the analyst stopped (Esc) or the reading was cancelled.
        /// </summary>
        private bool PromptSegmentStrokes(SwimMark split, bool sequence)
        {
            StrokeSession s = SessionFor(Race);
            int pool = Math.Max(1, Race.Pool);
            bool wall = split.Distance % pool == 0;

            if (s != null && split.Distance > s.Wall)
            {
                // The wall of the lap followed was not marked: this checkpoint is in another lap.
                EndStrokeSession();
                s = null;
            }

            bool detected = false;
            if (s != null && split.Timestamp > s.Start)
            {
                long target = wall ? split.Timestamp : Math.Max(split.Timestamp + SessionSpan(s, StrokeLookahead), s.Start + SessionSpan(s, StrokeMinSpan));
                target = Math.Min(target, frameServer.VideoReader.WorkingZone.End);
                if (target > s.LastRead && !ReadStrokeFrames(s, target))
                    return false;

                PlaceSessionStrokes(s, wall ? split.Timestamp : target, wall);
                detected = true;
            }

            bool answered = AskSegmentStrokes(split.Distance, detected ? s : null, sequence);

            if (s != null && wall && split.Distance == s.Wall)
                EndStrokeSession();

            return answered;
        }

        /// <summary>Strokes of the lap followed up to 'until' (the wall: a little before it), as the marks of the session.</summary>
        private void PlaceSessionStrokes(StrokeSession s, long until, bool wall)
        {
            if (s.Timestamps.Count < 10)
                return;

            int last = s.Timestamps.Count - 1;
            while (last > 0 && s.Timestamps[last] > until)
                last--;
            if (wall)
                last -= (int)Math.Round(SwimStrokeDetector.WallMargin(s.Style) * s.Fps);
            if (last < 5)
                return;

            SwimStrokeResult result = s.Detector.Finish(0, last);
            s.Cycle = result.Cycle;
            s.Ropes = result.Ropes;
            LastAutoCycle = result.Cycle;

            // The place of the swimmer, for the athlete label of the technical view.
            StoreTrack(s, result);

            // The automatic strokes of the lap are replaced: those of this follow and of an earlier one (swimmer chosen again).
            SwimMark wallSplit = Race.GetSplit(s.Wall);
            long lapEnd = wallSplit != null && wallSplit.Timestamp > s.LapStart ? wallSplit.Timestamp : Math.Max(until, s.LastRead);
            Race.Marks.RemoveAll(m => m.Type == SwimMarkType.Stroke && m.Batch != null &&
                (m.Batch == s.Batch || (m.Timestamp > s.LapStart && m.Timestamp <= lapEnd)));

            // A stroke marked by hand (C) is the stroke found within half a stroke of it, and none is placed again where
            // the analyst deleted one (✕).
            long near = Math.Max(1, SessionSpan(s, 0.5 * (result.Period > 0 ? result.Period : 0.5)));
            List<long> kept = Race.Marks.Where(m => m.Type == SwimMarkType.Stroke).Select(m => m.Timestamp).ToList();
            foreach (int i in result.Strokes)
            {
                long t = s.Timestamps[i];
                if (kept.Any(m => Math.Abs(m - t) < near) || s.Removed.Any(r => Math.Abs(r - t) < near))
                    continue;

                Race.Marks.Add(new SwimMark { Type = SwimMarkType.Stroke, Timestamp = t, Batch = s.Batch });
            }
        }

        /// <summary>Correction of the strokes of a segment (double click on the strokes of the panel).</summary>
        public void EditSegmentStrokes(int distance)
        {
            if (Race == null || Race.IsTraining)
                return;

            if (Race.GetSplit(distance) == null)
            {
                Status("Το σημείο δεν έχει ακόμη χρόνο: μάρκαρε πρώτα το πέρασμα.", true);
                return;
            }

            AskSegmentStrokes(distance, null, false);
        }

        /// <summary>
        /// The count of the segment from the marks, in a small window: Enter keeps it, a number replaces it,
        /// an empty box removes the typed value. Returns false on Esc.
        /// </summary>
        private bool AskSegmentStrokes(int distance, StrokeSession detection, bool sequence)
        {
            double previous;
            bool hadTyped = Race.StrokeCounts.TryGetValue(distance, out previous);
            Race.StrokeCounts.Remove(distance);
            Recompute();

            SwimRow row = Computed.Rows.FirstOrDefault(r => r.D == distance);
            if (row == null)
            {
                if (hadTyped)
                    Race.StrokeCounts[distance] = previous;
                Recompute();
                return true;
            }

            double? found = row.Strokes;
            string title = string.Format(CultureInfo.InvariantCulture, "Χεριές {0}→{1}{2}", row.PrevD, row.D, UnitLabel);
            string detail;
            if (detection != null)
            {
                detail = string.Format(CultureInfo.InvariantCulture, "Αυτόματη μέτρηση{0}{1}.",
                    detection.Cycle > 0 ? " · κύκλος " + detection.Cycle.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',') + " s" : "",
                    detection.Ropes ? "" : " · χωρίς σχοινιά διαδρομής");
            }
            else if (found.HasValue)
            {
                detail = "Από τα σημάδια χεριών του βίντεο: " + FormatCount(found.Value) + ".";
            }
            else
            {
                detail = "Δεν υπάρχουν σημάδια χεριών σε αυτό το τμήμα.";
            }

            if (hadTyped)
                detail += string.Format(CultureInfo.InvariantCulture, " Γραμμένο πριν: {0}.", FormatCount(previous));

            // A new count shows what was found; a correction from the panel starts from the number written before.
            double? shown = detection == null && hadTyped ? previous : found;
            DialogResult answer;
            double? value;
            bool cleared;
            using (FormSwimStrokeCount dialog = new FormSwimStrokeCount(title, detail, shown, sequence))
            {
                answer = dialog.ShowDialog(DialogOwner);
                value = dialog.Value;
                cleared = dialog.Cleared;
            }

            if (answer != DialogResult.OK)
            {
                if (hadTyped)
                    Race.StrokeCounts[distance] = previous;
                Changed(true);
                Status(sequence ? "Σταμάτησε η μέτρηση των χεριών." : "Οι χεριές του τμήματος δεν άλλαξαν.", false);
                return false;
            }

            if (cleared)
            {
                Status(string.Format(CultureInfo.InvariantCulture, "{0}: χωρίς γραμμένο αριθμό (μετράνε τα σημάδια χεριών).", title), false);
            }
            else if (value.HasValue)
            {
                Race.StrokeCounts[distance] = value.Value;
                bool same = found.HasValue && Math.Abs(found.Value - value.Value) < 1e-6;
                Status(string.Format(CultureInfo.InvariantCulture, "{0}: {1}{2}", title, FormatCount(value.Value),
                    same ? " ✓" : (found.HasValue ? " (διόρθωση από " + FormatCount(found.Value) + ")" : "")), false);
            }

            Changed(true);
            return true;
        }

        private static string FormatCount(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');
        }

        /// <summary>
        /// Reads the frames of the lap followed up to 'target', with the progress window (the UI thread waits there,
        /// a background thread reads the video like the video export). Returns false if cancelled or failed.
        /// </summary>
        private bool ReadStrokeFrames(StrokeSession s, long target)
        {
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
                worker.DoWork += (o, e) => { e.Result = ReadFrames(s, target, (BackgroundWorker)o); };
                worker.RunWorkerCompleted += (o, e) =>
                {
                    error = e.Error;
                    cancelled = e.Error == null && (cancel.Asked || e.Result is bool && !(bool)e.Result);
                    progress.Close();
                };

                worker.RunWorkerAsync();
                progress.ShowDialog();
            }

            // As after a video export: back to the decoding mode of the player, then to the frame of the checkpoint.
            AutoStrokesRunning = false;
            frameServer.VideoReader.AfterFrameEnumeration();
            if (player != null && !player.IsDisposed)
                player.AfterExportVideo();
            if (form != null && !form.IsDisposed)
                form.RefreshAutoStrokes();

            if (error != null)
            {
                log.Error("Reading of the video for the swimmer failed.", error);
                if (s == strokeSession)
                {
                    Status("Η αυτόματη μέτρηση των χεριών απέτυχε: " + error.Message, true);
                    EndStrokeSession();
                }
                else
                {
                    Status("Η ακολούθηση του αθλητή απέτυχε: " + error.Message, true);
                }

                return false;
            }

            if (cancelled)
            {
                Status(s == strokeSession ? "Η ανάγνωση του βίντεο για τις χεριές ακυρώθηκε." : "Η ακολούθηση του αθλητή ακυρώθηκε.", false);
                return false;
            }

            return true;
        }

        /// <summary>A frame for the detector: pixels at the working size and camera motion from the previous frame.</summary>
        private sealed class StrokeFrame
        {
            public byte[] Pixels;
            public double[] Motion;
            public long Timestamp;

            /// <summary>The frame at its own size, for the lane searched in it (the first frame of a session without a lane).</summary>
            public SwimStrokeImage Detail;
        }

        /// <summary>Cancel of a progress window: asked by the analyst (its reading may still end as if complete).</summary>
        private sealed class ProgressCancel
        {
            public bool Asked;
        }

        /// <summary>
        /// Cancel of the progress window of a reading: the worker is asked to stop, and the window stays open until it has. Its Cancel
        /// button closed the window at once (DialogResult) while the worker still decoded the video: the code after ShowDialog then
        /// used the reader and the results of a reading still going on (the result of a start search not there yet, a partial race
        /// taken as read). A reading may also end complete after a Cancel (it was in a part that does not look): the answer tells.
        /// </summary>
        private static ProgressCancel KeepOpenWhileBusy(FormProgressBar progress, BackgroundWorker worker)
        {
            ProgressCancel cancel = new ProgressCancel();

            // No button of the window takes the focus: a reading often starts with Enter (a proposal taken, a start accepted), and a
            // second Enter (the key held, a double press) went to the focused Cancel and stopped the reading at once (seen in the
            // test of 2.7.2: the swimmer proposed taken with Enter, "0 lengths"). Cancel stays a click.
            foreach (Control c in progress.Controls)
            {
                if (c is IButtonControl)
                    c.TabStop = false;
            }

            progress.CancelAsked += (o, e) =>
            {
                cancel.Asked = true;
                worker.CancelAsync();
            };
            progress.FormClosing += (o, e) =>
            {
                if (worker.IsBusy)
                    e.Cancel = true;
            };
            return cancel;
        }

        /// <summary>
        /// Background thread: the frames after the last one read, up to 'target'. Returns false if cancelled.
        /// This thread decodes the frames and computes the camera motion (OpenCV) while a second thread runs the detector
        /// on the previous frames.
        /// </summary>
        private bool ReadFrames(StrokeSession s, long target, BackgroundWorker worker)
        {
            VideoReader reader = ReadingReader;
            long from = s.Timestamps.Count == 0 ? s.Start : s.LastRead;
            long lastQueued = s.LastRead;
            double span = Math.Max(1, target - from);
            bool completed = true;
            Exception detectorError = null;

            // A reading in the background stays below the player and the panel (and tells its progress less often).
            bool background = quietThread != null;
            int frames = 0;

            // The lane is searched in the first frame: that frame at its own size too (the floats of the ropes of far lanes).
            bool wantDetail = s.Timestamps.Count == 0 && s.Detector.SearchesLane;
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
                                if (f.Detail != null)
                                    s.Detector.Detail = f.Detail;
                                s.Detector.Add(new SwimStrokeImage(s.Width, s.Height, f.Pixels), f.Motion);
                                s.Timestamps.Add(f.Timestamp);
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

                    // Continue after the last frame read (that one is already in the detector).
                    if (s.Timestamps.Count > 0 && hasMore)
                        hasMore = reader.MoveNext(s.Step - 1, true);

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

                        if (frame.Timestamp > lastQueued)
                        {
                            StrokeFrame item = new StrokeFrame { Timestamp = frame.Timestamp };
                            using (OpenCvSharp.Mat bgr = SwimStrokeVideo.ToWorking(frame.Image, s.Width, s.Height))
                            {
                                OpenCvSharp.Mat gray = SwimStrokeVideo.HalfGray(bgr);
                                item.Motion = s.PreviousGray == null ? null : SwimStrokeVideo.Motion(s.PreviousGray, gray);
                                if (s.PreviousGray != null)
                                    s.PreviousGray.Dispose();
                                s.PreviousGray = gray;
                                item.Pixels = SwimStrokeVideo.Bytes(bgr);
                            }

                            if (wantDetail)
                            {
                                wantDetail = false;
                                item.Detail = SwimStrokeVideo.Detail(frame.Image, s.Width);
                            }

                            queue.Add(item);
                            lastQueued = frame.Timestamp;
                            if (!background || ++frames % 25 == 0)
                                worker.ReportProgress((int)Math.Max(0, Math.Min(100, 100.0 * (frame.Timestamp - from) / span)));
                        }

                        if (!hasMore)
                            break;

                        hasMore = reader.MoveNext(s.Step - 1, true);
                    }
                }
                finally
                {
                    queue.CompleteAdding();
                    detection.Wait();
                }
            }

            if (detectorError != null)
                throw new InvalidOperationException("Stroke detector: " + detectorError.Message, detectorError);

            return completed;
        }

        /// <summary>Video timestamps in this time of the lap followed (s).</summary>
        private long SessionSpan(StrokeSession s, double seconds)
        {
            return (long)Math.Round(seconds * s.Fps * s.Step * Timing.AverageTimestampsPerFrame);
        }

        /// <summary>Start of the lap of this frame: the wall marked before it, or the start of the race.</summary>
        private long LapStartTimestamp(long timestamp)
        {
            int pool = Math.Max(1, Race.Pool);
            SwimMark wall = Race.Marks
                .Where(m => m.Type == SwimMarkType.Split && m.Distance % pool == 0 && m.Timestamp <= timestamp + HalfFrame)
                .OrderBy(m => m.Timestamp)
                .LastOrDefault();
            if (wall != null)
                return wall.Timestamp;

            return Race.StartTimestamp.HasValue && Race.StartTimestamp.Value <= timestamp ? Race.StartTimestamp.Value : long.MinValue;
        }

        private int NextWallDistance(long timestamp)
        {
            int pool = Math.Max(1, Race.Pool);
            SwimMark before = Race.Marks
                .Where(m => m.Type == SwimMarkType.Split && m.Timestamp <= timestamp + HalfFrame)
                .OrderBy(m => m.Timestamp)
                .LastOrDefault();
            int d = before == null ? 0 : before.Distance;
            return Math.Max(pool, Math.Min(Race.Distance, (d / pool + 1) * pool));
        }
        #endregion

        #region Review
        /// <summary>Move the video to the previous (−1) or next (+1) stroke mark.</summary>
        public void SeekStroke(int direction)
        {
            if (Race == null || !HasVideo)
                return;

            long now = player.CurrentTimestamp;
            double half = Math.Max(1, Timing.AverageTimestampsPerFrame * 0.5);
            List<SwimMark> strokes = SortedStrokes();
            SwimMark target = direction > 0
                ? strokes.FirstOrDefault(m => m.Timestamp > now + half)
                : strokes.LastOrDefault(m => m.Timestamp < now - half);
            if (target == null)
            {
                SystemSounds.Beep.Play();
                Status(direction > 0 ? "Δεν υπάρχει άλλη χεριά μετά από αυτό το καρέ." : "Δεν υπάρχει χεριά πριν από αυτό το καρέ.", true);
                return;
            }

            player.ForcePosition(target.Timestamp, true);
            Status(StrokeDescription(target, strokes), false);
        }

        /// <summary>Delete the stroke mark of the current frame (C puts it back).</summary>
        public void DeleteStrokeHere()
        {
            if (Race == null || !HasVideo)
                return;

            long now = player.CurrentTimestamp;
            double tolerance = Math.Max(1, Timing.AverageTimestampsPerFrame * 1.5);
            SwimMark mark = Race.Marks
                .Where(m => m.Type == SwimMarkType.Stroke)
                .OrderBy(m => Math.Abs(m.Timestamp - now))
                .FirstOrDefault();
            if (mark == null || Math.Abs(mark.Timestamp - now) > tolerance)
            {
                SystemSounds.Beep.Play();
                Status("Δεν υπάρχει χεριά σε αυτό το καρέ: πήγαινε σε μια χεριά με ◀ ▶.", true);
                return;
            }

            Race.Marks.Remove(mark);
            StrokeSession s = SessionFor(Race);
            if (s != null && mark.Timestamp > s.LapStart)
                s.Removed.Add(mark.Timestamp);

            Changed(true);
            InvalidatePlayer();
            Status("Διαγράφηκε η χεριά αυτού του καρέ (C για να την ξαναβάλεις).", false);
        }

        /// <summary>Delete the strokes placed by the automatic detection in this race (the typed counts stay).</summary>
        public int DeleteAutoStrokes()
        {
            if (Race == null)
                return 0;

            EndStrokeSession();
            int removed = Race.Marks.RemoveAll(m => m.Type == SwimMarkType.Stroke && m.Batch != null);
            if (removed > 0)
            {
                Changed(true);
                InvalidatePlayer();
                Status(string.Format(CultureInfo.InvariantCulture, "Διαγράφηκαν {0} αυτόματες χεριές (οι αριθμοί που επιβεβαίωσες μένουν).", removed), false);
            }

            return removed;
        }

        private List<SwimMark> SortedStrokes()
        {
            return Race.Marks.Where(m => m.Type == SwimMarkType.Stroke).OrderBy(m => m.Timestamp).ToList();
        }

        private string StrokeDescription(SwimMark mark, List<SwimMark> strokes)
        {
            int index = strokes.IndexOf(mark) + 1;
            string time = Race.StartTimestamp.HasValue && !Race.IsTraining
                ? string.Format(CultureInfo.InvariantCulture, " · {0:0.00} s", Timing.Seconds(mark.Timestamp, Race.StartTimestamp.Value))
                : "";
            return string.Format(CultureInfo.InvariantCulture, "Χεριά {0}/{1}{2}{3}", index, strokes.Count, time, mark.Batch != null ? " · αυτόματη" : "");
        }

        /// <summary>
        /// Screen only: what to click while choosing the swimmer, and the number of the stroke on the frames of the strokes.
        /// </summary>
        private void DrawStrokeOverlay(Graphics g, ImageTransform transform, long timestamp, Size renderingSize)
        {
            if (Race == null)
                return;

            if (PickTarget == SwimPickTarget.StrokeHead)
            {
                // The swimmer proposed for the whole race (Enter takes him).
                if (autoPick != null && pickForRace)
                {
                    DrawAutoPick(g, transform, timestamp);
                    return;
                }

                DrawLabel(g, PickingTrack ? "Ακολούθηση: κλικ στο κεφάλι του αθλητή (Esc: ακύρωση)" : "Χεριές: κλικ στο κεφάλι του αθλητή (Esc: χωρίς)",
                    new Point(12, 12), Color.FromArgb(0, 220, 255));
                return;
            }

            if (PickTarget == SwimPickTarget.StrokeFeet)
            {
                if (SameFrame(autoStrokeFrame, timestamp))
                {
                    Point p = transform.Transform(autoStrokeHead);
                    using (Pen pen = new Pen(Color.FromArgb(240, 0, 220, 255), 2f))
                        g.DrawEllipse(pen, p.X - 8, p.Y - 8, 16, 16);
                    DrawLabel(g, "κεφάλι", new Point(p.X + 10, p.Y - 20), Color.FromArgb(0, 220, 255));
                }

                DrawLabel(g, (PickingTrack ? "Ακολούθηση" : "Χεριές") + ": κλικ στα πόδια του αθλητή", new Point(12, 12), Color.FromArgb(0, 220, 255));
                return;
            }

            if (AutoStrokeCount == 0 || renderingSize.Height < 60)
                return;

            SwimMark mark = Race.Marks.FirstOrDefault(m => m.Type == SwimMarkType.Stroke && SameFrame(m.Timestamp, timestamp));
            if (mark == null)
                return;

            DrawLabel(g, StrokeDescription(mark, SortedStrokes()), new Point(12, renderingSize.Height - 28),
                mark.Batch != null ? Color.FromArgb(255, 214, 0) : Color.White);
        }
        #endregion
        #endregion
    }

    /// <summary>
    /// OpenCV part of the automatic strokes: frames at the working size and camera motion between frames.
    /// </summary>
    internal static class SwimStrokeVideo
    {
        /// <summary>Copy of the frame at the working size, 24-bit BGR.</summary>
        public static OpenCvSharp.Mat ToWorking(Bitmap image, int width, int height)
        {
            using (OpenCvSharp.Mat source = OpenCvSharp.Extensions.BitmapConverter.ToMat(image))
            {
                OpenCvSharp.Mat bgr = source;
                OpenCvSharp.Mat converted = null;
                int channels = source.Channels();
                if (channels == 4)
                {
                    converted = new OpenCvSharp.Mat();
                    OpenCvSharp.Cv2.CvtColor(source, converted, OpenCvSharp.ColorConversionCodes.BGRA2BGR);
                    bgr = converted;
                }
                else if (channels == 1)
                {
                    converted = new OpenCvSharp.Mat();
                    OpenCvSharp.Cv2.CvtColor(source, converted, OpenCvSharp.ColorConversionCodes.GRAY2BGR);
                    bgr = converted;
                }

                OpenCvSharp.Mat result = new OpenCvSharp.Mat();
                OpenCvSharp.Cv2.Resize(bgr, result, new OpenCvSharp.Size(width, height), 0, 0, OpenCvSharp.InterpolationFlags.Area);
                if (converted != null)
                    converted.Dispose();
                return result;
            }
        }

        /// <summary>
        /// The frame at its own size (at most 1920 wide), 24-bit BGR, when clearly larger than the working size (null otherwise).
        /// </summary>
        public static SwimStrokeImage Detail(Bitmap image, int workingWidth)
        {
            if (image == null || image.Width < 1.5 * workingWidth)
                return null;

            int width = Math.Min(image.Width, 1920);
            int height = Math.Max(2, (int)Math.Round((double)width * image.Height / image.Width));
            using (OpenCvSharp.Mat bgr = ToWorking(image, width, height))
                return new SwimStrokeImage(width, height, Bytes(bgr));
        }

        /// <summary>Grayscale image at half the working size (for the camera motion).</summary>
        public static OpenCvSharp.Mat HalfGray(OpenCvSharp.Mat bgr)
        {
            using (OpenCvSharp.Mat gray = new OpenCvSharp.Mat())
            {
                OpenCvSharp.Cv2.CvtColor(bgr, gray, OpenCvSharp.ColorConversionCodes.BGR2GRAY);
                OpenCvSharp.Mat half = new OpenCvSharp.Mat();
                OpenCvSharp.Cv2.Resize(gray, half, new OpenCvSharp.Size(bgr.Width / 2, bgr.Height / 2), 0, 0, OpenCvSharp.InterpolationFlags.Area);
                return half;
            }
        }

        /// <summary>Pixels of a BGR image, rows without padding.</summary>
        public static byte[] Bytes(OpenCvSharp.Mat bgr)
        {
            int length = bgr.Width * bgr.Height * 3;
            byte[] data = new byte[length];
            if (bgr.IsContinuous())
            {
                Marshal.Copy(bgr.Data, data, 0, length);
                return data;
            }

            using (OpenCvSharp.Mat copy = bgr.Clone())
                Marshal.Copy(copy.Data, data, 0, length);
            return data;
        }

        /// <summary>
        /// Homography (row-major, working coordinates) from the previous frame to this one: corners tracked with
        /// optical flow, RANSAC. Identity when it can't be estimated.
        /// </summary>
        public static double[] Motion(OpenCvSharp.Mat previousHalf, OpenCvSharp.Mat currentHalf)
        {
            try
            {
                OpenCvSharp.Point2f[] corners = OpenCvSharp.Cv2.GoodFeaturesToTrack(previousHalf, 400, 0.01, 8, null, 7, false, 0.04);
                if (corners == null || corners.Length < 12)
                    return SwimStrokeDetector.Identity();

                OpenCvSharp.Point2f[] next = new OpenCvSharp.Point2f[0];
                byte[] status;
                float[] error;
                OpenCvSharp.Cv2.CalcOpticalFlowPyrLK(previousHalf, currentHalf, corners, ref next, out status, out error,
                    new OpenCvSharp.Size(21, 21), 3);

                List<OpenCvSharp.Point2d> from = new List<OpenCvSharp.Point2d>();
                List<OpenCvSharp.Point2d> to = new List<OpenCvSharp.Point2d>();
                int n = Math.Min(corners.Length, Math.Min(next.Length, status.Length));
                for (int i = 0; i < n; i++)
                {
                    if (status[i] != 1)
                        continue;
                    from.Add(new OpenCvSharp.Point2d(corners[i].X, corners[i].Y));
                    to.Add(new OpenCvSharp.Point2d(next[i].X, next[i].Y));
                }

                if (from.Count < 12)
                    return SwimStrokeDetector.Identity();

                using (OpenCvSharp.Mat h = OpenCvSharp.Cv2.FindHomography(from, to, OpenCvSharp.HomographyMethods.Ransac, 1.5))
                {
                    if (h == null || h.Empty() || h.Rows != 3 || h.Cols != 3)
                        return SwimStrokeDetector.Identity();

                    double[] m = new double[9];
                    for (int r = 0; r < 3; r++)
                    {
                        for (int c = 0; c < 3; c++)
                            m[r * 3 + c] = h.At<double>(r, c);
                    }

                    // Half size -> working size: S⁻¹·H·S with S = diag(½, ½, 1).
                    return new double[] { m[0], m[1], 2 * m[2], m[3], m[4], 2 * m[5], m[6] / 2, m[7] / 2, m[8] };
                }
            }
            catch (Exception)
            {
                return SwimStrokeDetector.Identity();
            }
        }
    }
}
