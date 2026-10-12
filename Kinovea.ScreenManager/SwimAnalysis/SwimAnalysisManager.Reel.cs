#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Technical view of the race on the video, as the swimming reels: athlete label that follows the swimmer, race time
    /// with the strokes, tempo, distance per cycle and speed, phase, badges at the start, the breakouts and the checkpoints,
    /// chart of the cycles of the length and a summary card after the finish. On the screen and in the video exports.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Technical view (reel)
        private SwimReel reelCache;
        private int reelVersion = -1;
        private string reelRaceId;
        private bool pickForTrack;

        /// <summary>The technical view is drawn on the video of a race (instead of the race board and the velocity chart).</summary>
        public bool ReelShown
        {
            get { return Settings != null && Settings.ReelOverlay && Race != null && !Race.IsTraining; }
        }

        /// <summary>The next two clicks choose the swimmer that the athlete label follows (not the automatic strokes).</summary>
        public bool PickingTrack
        {
            get { return pickForTrack && PickingStrokes; }
        }

        public void SetReel(bool on)
        {
            Settings.ReelOverlay = on;
            Settings.Save();
            if (on)
            {
                bool tracked = Race != null && Race.Tracks.Count > 0;
                Status(tracked
                    ? "Τεχνική ανάλυση στο βίντεο: ανοιχτή."
                    : "Τεχνική ανάλυση στο βίντεο: ανοιχτή. Για την ετικέτα πάνω στον αθλητή: «Ακολούθηση αθλητή» και κλικ στο κεφάλι και στα πόδια του.", false);
            }
            else
            {
                Status("Τεχνική ανάλυση στο βίντεο: κλειστή.", false);
            }

            InvalidatePlayer();
            if (form != null && !form.IsDisposed)
                form.RefreshReel();
        }

        public void SetReelSummary(bool on)
        {
            Settings.ReelSummary = on;
            Settings.Save();
            InvalidatePlayer();
        }

        private SwimReel Reel
        {
            get
            {
                if (Race == null || Computed == null)
                    return null;

                if (reelCache == null || reelVersion != DataVersion || reelRaceId != Race.Id)
                {
                    reelCache = SwimReel.Build(Race, Computed);
                    reelVersion = DataVersion;
                    reelRaceId = Race.Id;
                }

                return reelCache;
            }
        }

        /// <summary>Parts of the race where the swimmer is followed, for the panel ("6.4–26.7 s"), empty if none.</summary>
        public string TrackDescription
        {
            get
            {
                if (Race == null || Race.Tracks.Count == 0 || !Race.StartTimestamp.HasValue || !Timing.IsValid)
                    return "";

                return string.Join(", ", Race.Tracks.OrderBy(t => t.Start).Select(t => string.Format(CultureInfo.InvariantCulture, "{0:0.0}–{1:0.0} s",
                    Timing.Seconds(t.Start, Race.StartTimestamp.Value), Timing.Seconds(t.End, Race.StartTimestamp.Value))));
            }
        }

        #region Following the swimmer
        /// <summary>
        /// The next two clicks are the head and the feet of the swimmer: the length is read from this frame up to its wall and the
        /// place of the swimmer is kept for the label of the technical view. The marks of the race do not change.
        /// </summary>
        public void BeginTrackSwimmer()
        {
            if (PickingStrokes)
            {
                bool wasTrack = pickForTrack;
                CancelPick();
                if (wasTrack)
                    return;
            }

            if (AutoStrokesRunning)
                return;

            if (!HasVideo || Race == null)
            {
                Status("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής.", true);
                return;
            }

            if (Race.IsTraining || !Race.StartTimestamp.HasValue)
            {
                Status("Η ακολούθηση του αθλητή είναι για την ανάλυση αγώνα, μετά το σήμα εκκίνησης (G).", true);
                return;
            }

            pendingMeasure = SwimPickTarget.None;
            calibrationClicks.Clear();
            angleClicks.Clear();
            pickForTrack = true;
            SetPick(SwimPickTarget.StrokeHead);
            Status("Ακολούθηση: σε ένα καρέ μετά την έξοδο από το νερό, κλικ στο κεφάλι του αθλητή και μετά στα πόδια του (Esc: ακύρωση).", false);
            InvalidatePlayer();
            if (form != null && !form.IsDisposed)
                form.RefreshReel();
        }

        /// <summary>Reads the length from the clicked frame to its wall and keeps the place of the swimmer.</summary>
        private void TrackSwimmer(PointF head, PointF feet, long from)
        {
            RefreshTiming();
            if (!Timing.IsValid || Race == null)
            {
                Status("Δεν είναι γνωστός ο ρυθμός καρέ του βίντεο: η ακολούθηση δεν μπορεί να γίνει.", true);
                return;
            }

            long target = TrackEnd(from);
            if (target <= from)
            {
                Status("Δεν υπάρχει βίντεο μετά από αυτό το καρέ.", true);
                return;
            }

            StrokeSession s = CreateSession(head, feet, from);
            try
            {
                if (!ReadStrokeFrames(s, target))
                    return;

                if (s.Timestamps.Count < 10)
                {
                    Status("Πολύ λίγα καρέ για την ακολούθηση του αθλητή.", true);
                    return;
                }

                SwimStrokeResult result = s.Detector.Finish(0, s.Timestamps.Count - 1);
                StoreTrack(s, result);
                Changed(true);
                Status(string.Format(CultureInfo.InvariantCulture, "Ακολούθηση: ο αθλητής βρέθηκε μέχρι τα {0:0.00} s{1}. Η ετικέτα του φαίνεται στην τεχνική ανάλυση.",
                    Timing.Seconds(s.LastRead, Race.StartTimestamp.Value), result.Ropes ? "" : " (χωρίς σχοινιά διαδρομής)"), false);
            }
            finally
            {
                s.Dispose();
                InvalidatePlayer();
                if (form != null && !form.IsDisposed)
                    form.RefreshReel();
            }
        }

        /// <summary>
        /// End of the reading for the swimmer: the wall of the length (a little after the touch) when it is marked, else the end of
        /// the race or of the working zone, at most one minute of video.
        /// </summary>
        private long TrackEnd(long from)
        {
            long end = frameServer.VideoReader.WorkingZone.End;
            long minute = from + (long)Math.Round(60.0 * 1000.0 / Timing.FrameIntervalMilliseconds * Timing.AverageTimestampsPerFrame);
            end = Math.Min(end, minute);

            int wall = NextWallDistance(from);
            SwimMark split = Race.GetSplit(wall);
            if (split != null && split.Timestamp > from)
            {
                long after = split.Timestamp + (long)Math.Round(0.6 * 1000.0 / Timing.FrameIntervalMilliseconds * Timing.AverageTimestampsPerFrame);
                end = Math.Min(end, after);
            }

            return end;
        }

        /// <summary>
        /// The place of the swimmer of a reading (automatic strokes or follow) in reference image pixels: it replaces the parts of
        /// the track in the same time.
        /// </summary>
        private void StoreTrack(StrokeSession s, SwimStrokeResult result)
        {
            if (Race == null || result == null || result.Path == null || result.Lanes == null)
                return;

            int n = Math.Min(s.Timestamps.Count, Math.Min(result.Path.Length, result.Lanes.Count));
            if (n < 10)
                return;

            Size reference = frameServer.VideoReader.Info.ReferenceSize;
            double sx = reference.Width / (double)Math.Max(1, s.Width);
            double sy = reference.Height / (double)Math.Max(1, s.Height);

            SwimTrack track = new SwimTrack
            {
                Start = s.Timestamps[0],
                Step = (s.Timestamps[n - 1] - s.Timestamps[0]) / (double)Math.Max(1, n - 1)
            };

            for (int i = 0; i < n; i++)
            {
                double x = result.Path[i];
                SwimStrokeLane lane = result.Lanes[i];
                double y1 = lane.Y1(x), y2 = lane.Y2(x);
                track.X.Add((int)Math.Round(x * sx));
                track.Y.Add((int)Math.Round(0.5 * (y1 + y2) * sy));
                track.Top.Add((int)Math.Round(Math.Min(y1, y2) * sy));
                track.Bottom.Add((int)Math.Round(Math.Max(y1, y2) * sy));
            }

            long start = track.Start, end = track.End;
            Race.Tracks.RemoveAll(t => t == null || (t.Start <= end && t.End >= start));
            Race.Tracks.Add(track);
            Race.Tracks.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        /// <summary>Delete the place of the swimmer kept for the label (the marks do not change).</summary>
        public void DeleteTrack()
        {
            if (Race == null || Race.Tracks.Count == 0)
                return;

            Race.Tracks.Clear();
            Changed(true);
            InvalidatePlayer();
            Status("Διαγράφηκε η ακολούθηση του αθλητή (η ετικέτα δεν φαίνεται πια).", false);
            if (form != null && !form.IsDisposed)
                form.RefreshReel();
        }
        #endregion

        /// <summary>
        /// The technical view at this frame. size: the image (screen pixels, or logical units of an export);
        /// fromImage: point of the video image (reference pixels) → point in these units, null without the athlete label.
        /// </summary>
        private void DrawReel(Graphics g, long timestamp, Size size, Func<PointF, PointF> fromImage)
        {
            SwimReel reel = Reel;
            if (reel == null || Race == null || !Race.StartTimestamp.HasValue || !Timing.IsValid)
                return;

            SwimReelScene scene = new SwimReelScene
            {
                Race = Race,
                Computed = Computed,
                Reel = reel,
                Timing = Timing,
                Timestamp = timestamp,
                Time = Timing.Seconds(timestamp, Race.StartTimestamp.Value),
                FromImage = fromImage,
                Summary = Settings.ReelSummary
            };
            SwimReelPainter.Paint(g, size, scene);
        }
        #endregion
    }
}
