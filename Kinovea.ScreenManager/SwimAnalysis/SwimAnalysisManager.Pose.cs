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
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using Kinovea.Video;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Skeleton of the swimmer in the technical analysis (starts and turns from a fixed camera, above or under the water):
    /// one click on the swimmer, the video is read from that frame while the swimmer is seen, the skeleton of each frame is
    /// kept (pose model, see SwimPose), drawn on the video with the angles of the knee, the hip and the elbow, and its angles
    /// can be added to the analysis at any frame.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Skeleton
        /// <summary>Width of the frames given to the pose models (pixels).</summary>
        private const int PoseWorkWidth = 1920;

        /// <summary>At most this much video is read for a skeleton (s).</summary>
        private const double SkeletonMaxSeconds = 20;

        /// <summary>The reading stops when the swimmer is not seen for this long (s).</summary>
        private const double SkeletonLostSeconds = 1.0;

        private static SwimPoseNet poseNet;
        private static readonly object poseNetLock = new object();

        public bool SkeletonRunning { get; private set; }

        /// <summary>The skeleton is drawn on the video (when there is one).</summary>
        public bool SkeletonShown
        {
            get { return Settings != null && Settings.SkeletonOverlay && Race != null && Race.Skeletons.Count > 0; }
        }

        /// <summary>Parts of the video with a skeleton, for the panel ("1.2–6.0 s"), empty if none.</summary>
        public string SkeletonDescription
        {
            get
            {
                if (Race == null || Race.Skeletons.Count == 0 || !Timing.IsValid)
                    return "";

                long origin = Race.StartTimestamp ?? frameServer.Metadata.TimeOrigin;
                return string.Join(", ", Race.Skeletons.OrderBy(s => s.Start).Select(s => string.Format(CultureInfo.InvariantCulture, "{0:0.0}–{1:0.0} s",
                    Timing.Seconds(s.Start, origin), Timing.Seconds(s.End, origin))));
            }
        }

        public void SetSkeletonOverlay(bool on)
        {
            Settings.SkeletonOverlay = on;
            Settings.Save();
            InvalidatePlayer();
            if (form != null && !form.IsDisposed)
                form.RefreshSkeleton();
        }

        public void SetSkeletonAngles(bool on)
        {
            Settings.SkeletonAngles = on;
            Settings.Save();
            InvalidatePlayer();
        }

        /// <summary>The pose models, read once (from the reading thread).</summary>
        private static SwimPoseNet PoseNet()
        {
            lock (poseNetLock)
            {
                if (poseNet == null)
                    poseNet = new SwimPoseNet();
                return poseNet;
            }
        }

        /// <summary>The next click on the video chooses the swimmer whose skeleton is found from this frame on.</summary>
        public void BeginSkeleton()
        {
            if (PickTarget == SwimPickTarget.Skeleton)
            {
                CancelPick();
                return;
            }

            if (SkeletonRunning || AutoStrokesRunning)
                return;

            if (!HasVideo || Race == null)
            {
                Status("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής.", true);
                return;
            }

            if (!SwimPoseNet.Available)
            {
                Status("Λείπουν τα αρχεία του σκελετού (φάκελος SwimAnalysis\\Models δίπλα στο Kinovea.exe).", true);
                return;
            }

            pendingMeasure = SwimPickTarget.None;
            calibrationClicks.Clear();
            angleClicks.Clear();
            pickForTrack = false;
            SetPick(SwimPickTarget.Skeleton);
            Status("Σκελετός: σε ένα καρέ όπου φαίνεται καθαρά ο αθλητής, κλικ πάνω του (π.χ. στη μέση του σώματος). " +
                "Το βίντεο διαβάζεται από εκεί και μετά, όσο φαίνεται ο αθλητής (Esc: ακύρωση).", false);
            InvalidatePlayer();
            if (form != null && !form.IsDisposed)
                form.RefreshSkeleton();
        }

        private void SkeletonClick(PointF point, long timestamp)
        {
            SetPick(SwimPickTarget.None);
            InvalidatePlayer();
            ReadSkeleton(point, timestamp);
        }

        /// <summary>Reads the video from the clicked frame while the swimmer is seen and keeps his skeleton.</summary>
        private void ReadSkeleton(PointF click, long from)
        {
            RefreshTiming();
            if (!Timing.IsValid || Race == null)
            {
                Status("Δεν είναι γνωστός ο ρυθμός καρέ του βίντεο: ο σκελετός δεν μπορεί να βρεθεί.", true);
                return;
            }

            long end = Math.Min(frameServer.VideoReader.WorkingZone.End, from + RealSpan(SkeletonMaxSeconds));
            if (end <= from)
            {
                Status("Δεν υπάρχει βίντεο μετά από αυτό το καρέ.", true);
                return;
            }

            double hsf = Timing.HighSpeedFactor <= 0 ? 1.0 : Timing.HighSpeedFactor;
            double realFps = 1000.0 * hsf / Timing.FrameIntervalMilliseconds;
            int step = Math.Max(1, (int)Math.Round(realFps / 60.0));
            Size reference = frameServer.VideoReader.Info.ReferenceSize;
            int width = Math.Min(PoseWorkWidth, Math.Max(64, reference.Width));
            int height = Math.Max(2, (int)Math.Round(width * (double)reference.Height / Math.Max(1, reference.Width)));
            int lostLimit = Math.Max(5, (int)Math.Round(SkeletonLostSeconds * realFps / step));
            SwimSkeleton skeleton = new SwimSkeleton { Start = from, Step = Timing.AverageTimestampsPerFrame * step };

            Exception error = null;
            bool cancelled = false;
            SkeletonRunning = true;
            if (form != null && !form.IsDisposed)
                form.RefreshSkeleton();

            player.BeforeExportVideo();
            using (BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true })
            using (FormProgressBar progress = new FormProgressBar(true))
            {
                progress.CancelAsked += (o, e) => worker.CancelAsync();
                worker.ProgressChanged += (o, e) => progress.Update(e.ProgressPercentage, 100, true);
                worker.DoWork += (o, e) =>
                {
                    e.Result = ReadSkeletonFrames(skeleton, click, from, end, step, width, height, reference, lostLimit, (BackgroundWorker)o);
                };
                worker.RunWorkerCompleted += (o, e) =>
                {
                    error = e.Error;
                    cancelled = e.Error == null && e.Result is bool && !(bool)e.Result;
                    progress.Close();
                };

                worker.RunWorkerAsync();
                progress.ShowDialog();
            }

            SkeletonRunning = false;
            frameServer.VideoReader.AfterFrameEnumeration();
            if (player != null && !player.IsDisposed)
                player.AfterExportVideo();

            try
            {
                if (error != null)
                {
                    log.Error("Skeleton reading failed.", error);
                    Status("Ο σκελετός δεν βρέθηκε: " + error.Message, true);
                    return;
                }

                // Without the frames after the swimmer was last seen.
                SwimSkeleton kept = TrimSkeleton(skeleton);
                if (kept == null || kept.Found < 5)
                {
                    Status(cancelled ? "Η ανάγνωση για τον σκελετό ακυρώθηκε." :
                        "Ο αθλητής δεν βρέθηκε. Κλικ πάνω στο σώμα του σε ένα καρέ όπου φαίνεται ολόκληρος και καθαρά.", !cancelled);
                    return;
                }

                StoreSkeleton(kept);
                Changed(false);
                long origin = Race.StartTimestamp ?? frameServer.Metadata.TimeOrigin;
                Status(string.Format(CultureInfo.InvariantCulture,
                    "Σκελετός: βρέθηκε σε {0} από {1} καρέ ({2:0.00}–{3:0.00} s){4}. «📐 Γωνίες σκελετού» βάζει τις γωνίες του καρέ στην ανάλυση.",
                    kept.Found, kept.Count, Timing.Seconds(kept.Start, origin), Timing.Seconds(kept.End, origin), cancelled ? ", ανάγνωση σταμάτησε" : ""), false);
            }
            finally
            {
                InvalidatePlayer();
                if (form != null && !form.IsDisposed)
                    form.RefreshSkeleton();
            }
        }

        /// <summary>Background thread: the frames from 'from' to 'end' (or until the swimmer is lost). False if cancelled.</summary>
        private bool ReadSkeletonFrames(SwimSkeleton skeleton, PointF click, long from, long end, int step, int width, int height, Size reference,
            int lostLimit, BackgroundWorker worker)
        {
            SwimPoseNet net = PoseNet();
            VideoReader reader = frameServer.VideoReader;
            double span = Math.Max(1, end - from);
            float sx = reference.Width / (float)width, sy = reference.Height / (float)height;
            SwimPoseTracker tracker = new SwimPoseTracker(new PointF(click.X / sx, click.Y / sy), Math.Max(300f, 0.33f * Math.Max(width, height)));

            reader.BeforeFrameEnumeration();
            long current = reader.Current != null ? reader.Current.Timestamp : from;
            bool hasMore = reader.MoveTo(current, from);
            bool first = true;
            while (true)
            {
                if (worker.CancellationPending)
                    return false;

                VideoFrame frame = reader.Current;
                if (frame == null || frame.Image == null || frame.Timestamp > end)
                    break;

                if (first)
                {
                    skeleton.Start = frame.Timestamp;
                    first = false;
                }

                using (OpenCvSharp.Mat bgr = SwimStrokeVideo.ToWorking(frame.Image, width, height))
                {
                    SwimPose pose = tracker.Next(part => net.Detect(bgr, part), (hip, body) => net.Landmarks(bgr, hip, body));
                    if (pose != null)
                        pose.Scale(sx, sy);
                    skeleton.Add(pose);
                }

                worker.ReportProgress((int)Math.Max(0, Math.Min(100, 100.0 * (frame.Timestamp - from) / span)));
                if (tracker.Found && tracker.Lost > lostLimit)
                    break;
                if (!tracker.Found && skeleton.Count > 3 * lostLimit)
                    break;
                if (!hasMore)
                    break;

                hasMore = reader.MoveNext(step - 1, true);
            }

            return true;
        }

        /// <summary>The skeleton without the frames before the swimmer was first seen and after he was last seen.</summary>
        private static SwimSkeleton TrimSkeleton(SwimSkeleton s)
        {
            int n = s.Count;
            int firstSeen = -1, lastSeen = -1;
            for (int i = 0; i < n; i++)
            {
                bool seen = false;
                int o = i * SwimSkeleton.Values;
                for (int j = 0; j < SwimJoints.Count && !seen; j++)
                    seen = s.Data[o + j * 3 + 2] > 0;
                if (!seen)
                    continue;

                if (firstSeen < 0)
                    firstSeen = i;
                lastSeen = i;
            }

            if (firstSeen < 0)
                return null;

            return s.Slice(s.Start + (long)Math.Round(firstSeen * s.Step), s.Start + (long)Math.Round(lastSeen * s.Step));
        }

        /// <summary>The new skeleton replaces what the older parts have in its time.</summary>
        private void StoreSkeleton(SwimSkeleton skeleton)
        {
            long start = skeleton.Start, end = skeleton.End;
            List<SwimSkeleton> kept = new List<SwimSkeleton>();
            foreach (SwimSkeleton old in Race.Skeletons.Where(s => s != null && s.Count > 0))
            {
                if (old.End < start || old.Start > end)
                {
                    kept.Add(old);
                    continue;
                }

                SwimSkeleton before = old.Slice(old.Start, start - 1);
                SwimSkeleton after = old.Slice(end + 1, old.End);
                if (before != null)
                    kept.Add(before);
                if (after != null)
                    kept.Add(after);
            }

            kept.Add(skeleton);
            kept.Sort((a, b) => a.Start.CompareTo(b.Start));
            Race.Skeletons.Clear();
            Race.Skeletons.AddRange(kept);
        }

        /// <summary>Delete the skeleton of the current frame (or all of them when the frame has none).</summary>
        public void DeleteSkeleton()
        {
            if (Race == null || Race.Skeletons.Count == 0)
                return;

            long? now = CurrentVideoTimestamp;
            SwimSkeleton here = now.HasValue ? Race.SkeletonAt(now.Value) : null;
            if (here != null)
            {
                Race.Skeletons.Remove(here);
                Status("Διαγράφηκε ο σκελετός αυτού του σημείου του βίντεο.", false);
            }
            else
            {
                Race.Skeletons.Clear();
                Status("Διαγράφηκαν όλοι οι σκελετοί του βίντεο.", false);
            }

            Changed(false);
            InvalidatePlayer();
            if (form != null && !form.IsDisposed)
                form.RefreshSkeleton();
        }

        /// <summary>Adds the angles of the skeleton at the current frame (knee, hip, elbow, shoulder of the nearer side) to the analysis.</summary>
        public void AddSkeletonAngles()
        {
            long? now = CurrentVideoTimestamp;
            SwimSkeleton s = now.HasValue && Race != null ? Race.SkeletonAt(now.Value) : null;
            PointF[] points = new PointF[SwimJoints.Count];
            float[] visibility = new float[SwimJoints.Count];
            if (s == null || !s.TryGet(now.Value, 2, points, visibility))
            {
                Status("Δεν υπάρχει σκελετός σε αυτό το καρέ: πρώτα «🦴 Σκελετός» και κλικ στον αθλητή.", true);
                return;
            }

            bool left = SwimSkeletonPainter.LeftSideNearer(visibility);
            List<string> added = new List<string>();
            foreach (KeyValuePair<string, int[]> a in SwimSkeletonPainter.Angles)
            {
                int[] j = SwimSkeletonPainter.SideJoints(a.Value, left);
                if (j.Any(k => visibility[k] < 0.5f))
                    continue;

                SwimAngle angle = new SwimAngle { Name = a.Key, Timestamp = now.Value };
                angle.Points = j.Select(k => new SwimPoint(points[k])).ToList();
                double? deg = angle.Degrees;
                if (!deg.HasValue)
                    continue;

                Race.Angles.Add(angle);
                added.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:0}°", a.Key, deg.Value));
            }

            if (added.Count == 0)
            {
                Status("Οι αρθρώσεις δεν φαίνονται καθαρά σε αυτό το καρέ: δοκίμασε ένα κοντινό καρέ.", true);
                return;
            }

            SwimTechPhase phase = SwimTechCalculator.PhaseAt(TechPhases, now.Value);
            Status("📐 Από τον σκελετό: " + string.Join(", ", added) + (phase != null ? " (" + phase.Title + ")" : "") + ". Διαγράφονται και μετονομάζονται όπως οι άλλες γωνίες.", false);
            Changed(true);
        }

        /// <summary>The skeleton of the frame (see SwimSkeletonPainter). map: reference image pixels → drawing.</summary>
        private void DrawSkeleton(Graphics g, long timestamp, Func<PointF, PointF> map, float scale)
        {
            if (Race == null)
                return;

            SwimSkeletonPainter.Paint(g, Race.SkeletonAt(timestamp), timestamp, map, scale, Settings == null || Settings.SkeletonAngles);
        }

        /// <summary>The prompt while the swimmer is chosen.</summary>
        private void DrawSkeletonPrompt(Graphics g)
        {
            if (PickTarget == SwimPickTarget.Skeleton)
                DrawLabel(g, "Σκελετός: κλικ πάνω στον αθλητή (Esc: ακύρωση)", new Point(12, 12), Color.FromArgb(0, 220, 255));
        }
        #endregion
    }
}
