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
using System.Text.RegularExpressions;
using Kinovea.Video;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The swimmer of the race's lane on a view of the whole pool from the stands (phones), without a click: the lanes of the pool
    /// (SwimPoolGrid), the lane counted from the near wall as the numbering of that pool says, the swimmer where it moves in that
    /// lane, the way he swims from where the dive moved. The numbering of a pool is learned from the lanes the analyst confirms in
    /// the videos of the same folder (Enter on the proposal, ↑/↓ then Enter, a click on another swimmer); until then lane 1 is the
    /// lane at the near wall (the analysts' phone videos of the national pool) and the pilot asks.
    /// (Lab, 51 phone races of the national pool with both walls in view: the lane of the race in 29 of 29 with a known lane, the
    /// swimmer within a body in 44 of 50, the way he swims in 50 of 51.)
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>The swimmer is looked for this long after the start (after the breakout of most strokes), as the camera follows.</summary>
        private const double LanePickSeconds = 6.0;

        /// <summary>The pilot takes the swimmer of a lane only when he stands out of the motion of the lane this much (lab: 1.15).</summary>
        private const double LanePickClear = 1.15;

        /// <summary>Lanes confirmed kept per folder of videos.</summary>
        private const int LaneNumberingKept = 8;

        private static readonly Regex LaneInName = new Regex(@"(?:^|[_\-\s.])L(\d{1,2})(?=$|[_\-\s.])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The lane of the race: the one of the race's data, else "L4" in the name of the video (..._F_L4.mp4); 0 if none.</summary>
        private int RaceLaneNumber()
        {
            if (Race != null && Race.Lane > 0)
                return Race.Lane;
            if (string.IsNullOrEmpty(videoPath))
                return 0;

            Match m = LaneInName.Match(Path.GetFileNameWithoutExtension(videoPath));
            int n;
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 0 && n <= 10 ? n : 0;
        }

        /// <summary>The key of the numbering: the folder of the video (one venue and camera place, usually).</summary>
        private string LaneNumberingKey
        {
            get
            {
                if (string.IsNullOrEmpty(videoPath))
                    return null;
                string folder = Path.GetDirectoryName(videoPath);
                return string.IsNullOrEmpty(folder) ? null : folder.ToLowerInvariant();
            }
        }

        /// <summary>The numbering of a pool: lane n is at the place (n - 1 + Offset) from the near wall, or from the far wall.</summary>
        private struct LaneRule
        {
            public bool FromFar;
            public int Offset;

            /// <summary>The lanes confirmed that agree with the rule.</summary>
            public int Seen;
        }

        /// <summary>The lanes confirmed in the folder of the video (number, place from the near wall, lanes of the pool).</summary>
        private List<int[]> LaneObservations()
        {
            List<int[]> list = new List<int[]>();
            string key = LaneNumberingKey;
            string value;
            if (key == null || Settings.LaneNumbering == null || !Settings.LaneNumbering.TryGetValue(key, out value) || string.IsNullOrEmpty(value))
                return list;

            foreach (string part in value.Split(';'))
            {
                string[] t = part.Split(':');
                int n, k, lanes;
                if (t.Length == 3 && int.TryParse(t[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out n) &&
                    int.TryParse(t[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out k) &&
                    int.TryParse(t[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out lanes) && lanes > 0 && k >= 0 && k < lanes)
                    list.Add(new[] { n, k, lanes });
            }

            return list;
        }

        /// <summary>
        /// The numbering that agrees with most lanes confirmed in this folder (ties: no offset first, then from the near wall); lane
        /// 1 at the near wall when none was confirmed yet.
        /// </summary>
        private LaneRule LaneRuleOf()
        {
            List<int[]> seen = LaneObservations();
            LaneRule best = new LaneRule { FromFar = false, Offset = 0, Seen = 0 };
            int bestRank = int.MaxValue;
            foreach (bool far in new[] { false, true })
            {
                for (int offset = -2; offset <= 2; offset++)
                {
                    LaneRule r = new LaneRule { FromFar = far, Offset = offset };
                    r.Seen = seen.Count(o => LanePlace(r, o[0], o[2]) == o[1]);
                    // More agreeing first, then no offset, then the near wall, then a small offset.
                    int rank = -r.Seen * 100 + (offset == 0 ? 0 : 10) + (far ? 5 : 0) + Math.Abs(offset);
                    if (rank < bestRank)
                    {
                        bestRank = rank;
                        best = r;
                    }
                }
            }

            return best;
        }

        /// <summary>The place of lane 'number' from the near wall (0: the lane at the near wall).</summary>
        private static int LanePlace(LaneRule rule, int number, int lanes)
        {
            int fromItsWall = number - 1 + rule.Offset;
            return rule.FromFar ? lanes - 1 - fromItsWall : fromItsWall;
        }

        /// <summary>A lane the analyst confirmed in this folder (kept with the settings, the last few).</summary>
        private void LearnLane(int number, int place, int lanes)
        {
            string key = LaneNumberingKey;
            if (key == null || number <= 0 || place < 0 || place >= lanes)
                return;

            if (Settings.LaneNumbering == null)
                Settings.LaneNumbering = new Dictionary<string, string>();

            List<int[]> seen = LaneObservations();
            seen.Add(new[] { number, place, lanes });
            if (seen.Count > LaneNumberingKept)
                seen.RemoveRange(0, seen.Count - LaneNumberingKept);
            Settings.LaneNumbering[key] = string.Join(";", seen.Select(o => string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}", o[0], o[1], o[2])));
            try
            {
                Settings.Save();
            }
            catch (Exception e)
            {
                log.Error("The numbering of the lanes could not be saved.", e);
            }
        }

        /// <summary>
        /// A click on a swimmer instead of the swimmer of a lane proposed, on the frame of the proposal: the lane clicked teaches the
        /// numbering of the pool (the lane of the race is there).
        /// </summary>
        private void LearnLaneFromClick(AutoPickResult p, PointF point, long timestamp)
        {
            if (p == null || p.Grid == null || p.LaneNumber <= 0 || !SameFrame(p.Frame, timestamp))
                return;

            int place = p.Grid.LaneAt(point.X * p.Sx, point.Y * p.Sy);
            if (place >= 0)
                LearnLane(p.LaneNumber, place, p.Grid.Lanes);
        }

        /// <summary>
        /// Background thread: the working images (24-bit BGR) of the frames at or just after the given times (ascending), with
        /// their timestamps; null if cancelled or past the end of the video.
        /// </summary>
        private List<KeyValuePair<long, SwimStrokeImage>> ReadImages(IList<long> times, int width, int height, BackgroundWorker worker)
        {
            VideoReader reader = ReadingReader;
            List<KeyValuePair<long, SwimStrokeImage>> images = new List<KeyValuePair<long, SwimStrokeImage>>();
            long tolerance = RealSpan(0.01);
            reader.BeforeFrameEnumeration();
            long current = reader.Current != null ? reader.Current.Timestamp : times[0];
            bool hasMore = reader.MoveTo(current, times[0]);
            int next = 0;
            while (next < times.Count)
            {
                if (worker.CancellationPending)
                    return null;

                VideoFrame frame = reader.Current;
                if (frame == null || frame.Image == null)
                    return null;

                if (frame.Timestamp + tolerance >= times[next])
                {
                    SwimStrokeImage image;
                    using (OpenCvSharp.Mat bgr = SwimStrokeVideo.ToWorking(frame.Image, width, height))
                        image = new SwimStrokeImage(width, height, SwimStrokeVideo.Bytes(bgr));
                    while (next < times.Count && frame.Timestamp + tolerance >= times[next])
                    {
                        images.Add(new KeyValuePair<long, SwimStrokeImage>(frame.Timestamp, image));
                        next++;
                    }
                }

                if (next >= times.Count)
                    break;
                if (!hasMore)
                    return null;
                hasMore = reader.MoveNext(0, true);
            }

            return images;
        }

        /// <summary>
        /// Background thread: the swimmer of the race's lane on a view of the whole pool (phones), at 'at'. Null when the lane of
        /// the race is not known, the view is not one of the whole pool (both walls in view, a clear grid of ropes), the lane is not
        /// in it, nothing moves in it, or the start end is not clear: then the swimmer the camera follows, or the click, as before.
        /// </summary>
        private AutoPickResult LanePick(long at, int width, int height, double sx, double sy, BackgroundWorker worker)
        {
            int number = RaceLaneNumber();
            if (number <= 0 || !Race.StartTimestamp.HasValue)
                return null;

            List<KeyValuePair<long, SwimStrokeImage>> pick = ReadImages(new[] { at, at + RealSpan(0.1), at + RealSpan(0.2) }, width, height, worker);
            if (pick == null)
                return null;

            IList<int> laneCounts = Race.Pool >= 50 ? SwimPoolGrid.LongCourseLanes : SwimPoolGrid.ShortCourseLanes;
            SwimPoolGrid grid = SwimPoolGrid.Find(pick[0].Value, width / 2.0, laneCounts);
            if (grid == null || !grid.Sure)
            {
                log.DebugFormat("Swimmer of lane {0}: not a view of the whole pool{1}.", number, grid == null ? "" : string.Format(CultureInfo.InvariantCulture,
                    " (lanes {0}, ropes {1:0.00}, middles {2:0.00}, walls {3:0}% / {4:0}%)", grid.Lanes, grid.Score, grid.Middles, 100 * grid.FarShare, 100 * grid.NearShare));
                return null;
            }

            LaneRule rule = LaneRuleOf();
            int place = LanePlace(rule, number, grid.Lanes);
            if (place < 0 || place >= grid.Lanes)
            {
                log.DebugFormat("Swimmer of lane {0}: the lane is not in the pool of {1} lanes.", number, grid.Lanes);
                return null;
            }

            long start = Race.StartTimestamp.Value;
            List<KeyValuePair<long, SwimStrokeImage>> dive = ReadImages(new[] { start + RealSpan(0.3), start + RealSpan(0.4) }, width, height, worker);
            if (dive == null)
                return null;

            // The way of the first length from the frames of the dive (their own walls and water: the camera may have turned since),
            // turned at every wall before 'at' (the swimmer found again after a turn).
            SwimPoolGrid atStart = SwimPoolGrid.Find(dive[0].Value, width / 2.0, laneCounts);
            int direction = atStart == null ? 0 : atStart.StartDirection(SwimPoolGrid.Motion(dive.Select(d => d.Value).ToList()));
            int length = NextWallDistance(at) / Math.Max(1, Race.Pool) - 1;
            if (length % 2 == 1)
                direction = -direction;
            float[] motion = SwimPoolGrid.Motion(pick.Select(d => d.Value).ToList());
            double body = AutoPickLength * grid.LaneWidth(place, width / 2.0);
            double clear;
            double x = grid.SwimmerAlong(place, motion, body, out clear);
            log.DebugFormat("Swimmer of lane {0}: place {1} of {2} from the near wall ({3}), at {4:0} (clear {5:0.00}), direction {6}.", number, place,
                grid.Lanes, rule.Seen > 0 ? "numbering seen " + rule.Seen + " times" : "numbering not confirmed", x, clear, direction);
            if (double.IsNaN(x) || direction == 0)
                return null;

            return LaneResult(grid, motion, number, place, x, direction, clear, pick[0].Key, sx, sy, rule.Seen > 0);
        }

        /// <summary>The proposal of the swimmer of lane 'place' of the grid, at x, swimming that way.</summary>
        private static AutoPickResult LaneResult(SwimPoolGrid grid, float[] motion, int number, int place, double x, int direction, double clear,
            long frame, double sx, double sy, bool learned)
        {
            double body = AutoPickLength * grid.LaneWidth(place, x);
            double hx = x + direction * 0.5 * body, fx = x - direction * 0.5 * body;
            return new AutoPickResult
            {
                Head = new PointF((float)(hx / sx), (float)(grid.LaneMiddle(place, hx) / sy)),
                Feet = new PointF((float)(fx / sx), (float)(grid.LaneMiddle(place, fx) / sy)),
                Frame = frame,
                LaneWidth = (float)(grid.LaneWidth(place, x) / sy),
                Agree = 0,
                LaneNumber = number,
                LanePlace = place,
                Grid = grid,
                Motion = motion,
                Direction = direction,
                Clear = clear,
                LaneLearned = learned,
                Sx = sx,
                Sy = sy
            };
        }

        /// <summary>↑/↓ on the swimmer of a lane: the swimmer of the lane further (↑) or nearer (↓) in the grid, where it moves in it.</summary>
        private AutoPickResult MoveLanePick(AutoPickResult p, int step)
        {
            int place = p.LanePlace - step;
            if (place < 0 || place >= p.Grid.Lanes)
                return null;

            double x = 0.5 * (p.Head.X + p.Feet.X) * p.Sx;
            double clear;
            double found = p.Grid.SwimmerAlong(place, p.Motion, AutoPickLength * p.Grid.LaneWidth(place, x), out clear);
            if (!double.IsNaN(found))
                x = found;
            return LaneResult(p.Grid, p.Motion, p.LaneNumber, place, x, p.Direction, double.IsNaN(found) ? 0 : clear, p.Frame, p.Sx, p.Sy, false);
        }

        /// <summary>Who the proposal is (subject), for the texts.</summary>
        private static string PickWhoNominative(AutoPickResult p)
        {
            if (p == null || p.LaneNumber <= 0)
                return "ο αθλητής που ακολουθεί η κάμερα";
            return string.Format(CultureInfo.InvariantCulture, "ο αθλητής του διαδρόμου {0}", p.LaneNumber);
        }

        /// <summary>Who the proposal is (object), for the texts: the swimmer of the lane, or the swimmer the camera follows.</summary>
        private static string PickWho(AutoPickResult p)
        {
            if (p == null || p.LaneNumber <= 0)
                return "τον αθλητή που ακολουθεί η κάμερα";
            return string.Format(CultureInfo.InvariantCulture, "τον αθλητή του διαδρόμου {0}", p.LaneNumber);
        }
    }
}
