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
using Newtonsoft.Json;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Kinds of events the analyst marks on the video during a swim race analysis.
    /// New values are added at the end: the saved analyses store the number.
    /// </summary>
    public enum SwimMarkType
    {
        /// <summary>Head (or hand at the wall) crossing the next checkpoint distance.</summary>
        Split,
        /// <summary>One stroke (hand entry). Free/back: every arm. Fly/breast: every cycle.</summary>
        Stroke,
        /// <summary>One underwater dolphin kick.</summary>
        Kick,
        /// <summary>Head breaks the surface after the start or a turn.</summary>
        Breakout,
        /// <summary>Feet leave the block (start) or the wall (turn).</summary>
        FeetOff,
        /// <summary>Hands enter the water after the start.</summary>
        Entry,
        /// <summary>First movement of the swimmer after the start signal (reaction).</summary>
        Reaction,
        /// <summary>Hands leave the block (start).</summary>
        HandsOff,
        /// <summary>Back foot leaves the block (track start).</summary>
        BackFoot,
        /// <summary>
        /// Head passes a technical distance: 5, 10 or 15 from the start wall, or −5, +5, +10, +15 around a turn wall
        /// (SwimMark.Distance is the offset, SwimMark.Wall the wall of the race, −1 in a training video).
        /// </summary>
        TechSplit,
        /// <summary>Training video: contact with the wall, beginning of a turn attempt.</summary>
        WallContact,
        /// <summary>Beginning of the rotation (somersault) before a flip turn.</summary>
        RotationStart,
        /// <summary>Training video: start signal of a start attempt.</summary>
        Signal
    }

    /// <summary>
    /// A single event marked on the video.
    /// </summary>
    public class SwimMark
    {
        public string Id = Guid.NewGuid().ToString("N");
        public SwimMarkType Type;

        /// <summary>Absolute video timestamp of the frame where the event was marked.</summary>
        public long Timestamp;

        /// <summary>For splits: checkpoint distance in pool units. For technical splits: offset from the wall (−5, 5, 10, 15).</summary>
        public int Distance;

        /// <summary>Technical splits of a race: wall of the offset (0 = start). −1 in a training video (the attempt is found by time).</summary>
        public int Wall = -1;

        /// <summary>
        /// Strokes placed by the automatic detection: id of the lap followed (its strokes are placed again at each checkpoint of the lap).
        /// Null for the marks of the analyst.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Batch;

        /// <summary>The answer of the learning this mark gave (its event, SwimLearningRecord.Key): it goes if the mark goes.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Taught;

        /// <summary>
        /// Put by the auto pilot and not checked yet (since 2026-10-18): it does not teach the learning. The analyst's answer to its
        /// proposal makes it his (Enter at its frame) or replaces it (another frame). Null for the other marks.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public SwimPilotMark Pilot;
    }

    /// <summary>A mark of the auto pilot: what putting it changed, undone when the analyst moves or removes it.</summary>
    public class SwimPilotMark
    {
        /// <summary>The wall of a breakout (0: the start), the distance of a checkpoint.</summary>
        public int Wall;

        /// <summary>The automatic strokes before the breakout that went with it (as Enter on it removes them).</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<SwimMark> Strokes;

        /// <summary>The underwater distance estimated with the breakout (null: none).</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public double? Underwater;

        /// <summary>
        /// A checkpoint at an official time of the results (since 2026-10-19: put there, or moved there). Read: the frame where the
        /// reading itself put it, where it goes back when the official time is taken away (null: it goes with it).
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Official;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public long? Read;
    }

    public class SwimTurnManual
    {
        public double? UnderwaterDist;
        public double? TurnTime;

        /// <summary>
        /// (2.9.8) UnderwaterDist is the estimate from the times (written with a breakout, SwimAnalysisManager.EstimateUnderwaterDistance),
        /// not measured on the video or typed: the speeds of the breakout are not computed from it (SwimTechCalculator).
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool UnderwaterEstimated;
    }

    /// <summary>
    /// Distances measured for one attempt of a training video (start or turn).
    /// </summary>
    public class SwimPhaseManual
    {
        public double? EntryDist;
        public double? BreakoutDist;
    }

    /// <summary>
    /// (2.13.0) The evaluation of one start or turn (an attempt, or the start / a turn of a race) with one sheet of criteria of the
    /// catalogue (SwimTechSheets): the score of each criterion, what the program measured for it, the frames of the errors.
    /// </summary>
    public class SwimTechEval
    {
        public string Id = Guid.NewGuid().ToString("N");

        /// <summary>The phase (SwimTechPhase.Id): "start"/"turn-50" in a race, the id of the mark of the attempt in a training video.</summary>
        public string PhaseId = "";

        /// <summary>The sheet of the catalogue (SwimTechSheet.Id).</summary>
        public string SheetId = "";

        /// <summary>When it was last changed (d/M/yyyy HH:mm).</summary>
        public string Date = "";
        public string Notes = "";

        /// <summary>The scores by criterion key (SwimTechCriterion).</summary>
        public Dictionary<string, SwimTechScore> Scores = new Dictionary<string, SwimTechScore>();
    }

    /// <summary>
    /// (2.13.0) The score of one criterion: 1 done as described (✓), 0 not (✗), -1 not visible / not judged (–); on a list of errors 1
    /// means the error is there. Auto: the program put it from its measurement and the coach has not confirmed it yet.
    /// </summary>
    public class SwimTechScore
    {
        public int Value;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Auto;

        /// <summary>The value the program measured when it was scored (for the learning of the limits), with its unit.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public double? Measured;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Unit;

        /// <summary>The snapshot of the frame of an error (SwimSnapshot.Id), for the report.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string SnapshotId;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Comment;
    }

    /// <summary>
    /// Angle measured on the video with three clicks (end, vertex, end), in image coordinates.
    /// </summary>
    public class SwimAngle
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public long Timestamp;
        public List<SwimPoint> Points = new List<SwimPoint>();

        /// <summary>Angle at the vertex, 0–180°.</summary>
        [JsonIgnore]
        public double? Degrees
        {
            get
            {
                if (Points == null || Points.Count != 3)
                    return null;

                double ax = Points[0].X - Points[1].X, ay = Points[0].Y - Points[1].Y;
                double bx = Points[2].X - Points[1].X, by = Points[2].Y - Points[1].Y;
                double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
                if (la < 1e-6 || lb < 1e-6)
                    return null;

                double cos = Math.Max(-1, Math.Min(1, (ax * bx + ay * by) / (la * lb)));
                return Math.Acos(cos) * 180.0 / Math.PI;
            }
        }
    }

    /// <summary>
    /// Frame of the video kept for the technical report, with the comments of the analyst and, optionally,
    /// an elite frame of the same moment for comparison.
    /// </summary>
    public class SwimSnapshot
    {
        public string Id = Guid.NewGuid().ToString("N");
        public long Timestamp;
        public string Stage = "";
        public string Comment = "";

        /// <summary>Taken automatically when this event of the technical analysis was marked (empty: taken by the analyst).</summary>
        public string Event = "";

        /// <summary>The mark of that event: the snapshot goes when the mark is undone.</summary>
        public string MarkId = "";

        /// <summary>JPEG image next to the video (folder "video.ext.swim").</summary>
        public string File = "";

        /// <summary>Google Drive file of the uploaded image (empty until it is uploaded).</summary>
        public string FileId = "";
        public string EliteFileId = "";
        public string EliteLabel = "";
        public string EliteComment = "";

        /// <summary>
        /// A final frame the analyst chose («📷 Από αυτό το καρέ»), not the one of the finish: taken again there when the official
        /// times change (since 2026-10-19).
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Chosen;

        /// <summary>The language of the graphics of the final frame: "en", or none for Greek (since 2.7.7).</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Lang;
    }

    /// <summary>A chart of the PDF report drawn by Kinovea: its key in the report, its image on the Drive, the hash of its values.</summary>
    public class SwimChartImage
    {
        public string Id = "";
        public string FileId = "";

        /// <summary>The hash of the values the image was drawn from (from the Apps Script).</summary>
        public string Hash = "";

        /// <summary>The version of the drawing (SwimChartPainter.Version): an older drawing is drawn again.</summary>
        public string Painter = "";
    }

    /// <summary>A graphic of the video in the report ("summary", "versus"): its image on the Drive and the hash of what was drawn.</summary>
    public class SwimReportGraphic
    {
        public string Id = "";
        public string FileId = "";
        public string Hash = "";
    }

    /// <summary>
    /// Place of the swimmer in a part of the video (one lap followed): every Step timestamps from Start, the centre of the
    /// moving part of the swimmer along the lane and the ropes of the lane on each side of it (reference image pixels).
    /// </summary>
    public class SwimTrack
    {
        public long Start;
        public double Step;

        // One line each in the analysis file (one number per frame).
        [JsonConverter(typeof(SwimNumbersOnOneLine))]
        public List<int> X = new List<int>();
        [JsonConverter(typeof(SwimNumbersOnOneLine))]
        public List<int> Y = new List<int>();
        [JsonConverter(typeof(SwimNumbersOnOneLine))]
        public List<int> Top = new List<int>();
        [JsonConverter(typeof(SwimNumbersOnOneLine))]
        public List<int> Bottom = new List<int>();

        /// <summary>
        /// Since 2.8.0: the displacement of the camera along the lane at the swimmer, added up from the first point (reference image
        /// pixels). X − Pan is the place of the swimmer in the pool in pixels of the image, the same in every frame of the part: where
        /// the swimmer is, will be or was at a place of the pool (the ghost and the line of a reference). Empty in older analyses.
        /// </summary>
        [JsonConverter(typeof(SwimNumbersOnOneLine))]
        public List<int> Pan = new List<int>();

        /// <summary>Since 2.8.0: the slope of the lane at the swimmer (dy/dx × 1000), for the line of a reference.</summary>
        [JsonConverter(typeof(SwimNumbersOnOneLine))]
        public List<int> Slope = new List<int>();

        /// <summary>
        /// Since 2.8.0: the length of the swimmer clicked for the reading (head to feet) in thousandths of the width of the lane there
        /// (in pixels both follow the zoom and the perspective), 0: unknown.
        /// </summary>
        public int Body;

        /// <summary>
        /// Since 2.8.0: how far his head (the front of the moving swimmer, where the checkpoints are read) is ahead of the place
        /// followed, along the swim, in thousandths of the width of the lane (the median of the part), 0: unknown.
        /// </summary>
        public int Lead;

        /// <summary>The camera's displacement is known for every point (a part read since 2.8.0).</summary>
        [JsonIgnore]
        public bool HasPan
        {
            get { return Pan != null && Pan.Count == X.Count && X.Count > 0; }
        }

        [JsonIgnore]
        public long End
        {
            get { return Start + (long)Math.Round(Step * Math.Max(0, X.Count - 1)); }
        }

        /// <summary>
        /// Place at a timestamp, averaged over 'smooth' points on each side (the path moves by whole pixels).
        /// False outside the part.
        /// </summary>
        public bool TryGet(long timestamp, int smooth, out float x, out float y, out float top, out float bottom)
        {
            x = y = top = bottom = 0;
            int n = Math.Min(Math.Min(X.Count, Y.Count), Math.Min(Top.Count, Bottom.Count));
            if (n == 0 || Step <= 0)
                return false;

            double f = (timestamp - Start) / Step;
            if (f < -0.5 || f > n - 0.5)
                return false;

            int i = (int)Math.Round(Math.Max(0, Math.Min(n - 1, f)));
            double sx = 0, sy = 0, st = 0, sb = 0, sw = 0;
            for (int k = Math.Max(0, i - smooth); k <= Math.Min(n - 1, i + smooth); k++)
            {
                double w = smooth + 1 - Math.Abs(k - i);
                sx += w * X[k];
                sy += w * Y[k];
                st += w * Top[k];
                sb += w * Bottom[k];
                sw += w;
            }

            x = (float)(sx / sw);
            y = (float)(sy / sw);
            top = (float)(st / sw);
            bottom = (float)(sb / sw);
            return true;
        }

        /// <summary>The points of the part between two timestamps (included), null if there are fewer than two.</summary>
        public SwimTrack Slice(long from, long to)
        {
            int n = Math.Min(Math.Min(X.Count, Y.Count), Math.Min(Top.Count, Bottom.Count));
            if (n == 0 || Step <= 0 || to < from)
                return null;

            int first = (int)Math.Max(0, Math.Ceiling(((double)from - Start) / Step - 1e-9));
            int last = (int)Math.Min(n - 1, Math.Floor(((double)to - Start) / Step + 1e-9));
            if (last - first + 1 < 2)
                return null;

            SwimTrack part = new SwimTrack { Start = Start + (long)Math.Round(first * Step), Step = Step, Body = Body, Lead = Lead };
            part.X.AddRange(X.GetRange(first, last - first + 1));
            part.Y.AddRange(Y.GetRange(first, last - first + 1));
            part.Top.AddRange(Top.GetRange(first, last - first + 1));
            part.Bottom.AddRange(Bottom.GetRange(first, last - first + 1));
            if (Pan != null && Pan.Count == X.Count)
                part.Pan.AddRange(Pan.GetRange(first, last - first + 1));
            if (Slope != null && Slope.Count == X.Count)
                part.Slope.AddRange(Slope.GetRange(first, last - first + 1));
            return part;
        }
    }

    /// <summary>
    /// Skeleton of the swimmer in a part of the video (a start or a turn followed): every Step timestamps from Start, the
    /// joints of SwimJoints as x, y (reference image pixels) and visibility 0–100, all 0 where the swimmer was not found.
    /// </summary>
    public class SwimSkeleton
    {
        public const int Values = SwimJoints.Count * 3;
        public long Start;
        public double Step;

        [JsonConverter(typeof(SwimNumbersOnOneLine))]
        public List<int> Data = new List<int>();

        [JsonIgnore]
        public int Count
        {
            get { return Data == null ? 0 : Data.Count / Values; }
        }

        [JsonIgnore]
        public long End
        {
            get { return Start + (long)Math.Round(Step * Math.Max(0, Count - 1)); }
        }

        /// <summary>Adds the next image: the skeleton in reference pixels, null if the swimmer was not found.</summary>
        public void Add(SwimPose pose)
        {
            for (int j = 0; j < SwimJoints.Count; j++)
            {
                bool seen = pose != null && !float.IsNaN(pose.X[j]) && !float.IsNaN(pose.Y[j]);
                Data.Add(seen ? (int)Math.Round(pose.X[j]) : 0);
                Data.Add(seen ? (int)Math.Round(pose.Y[j]) : 0);
                Data.Add(seen ? Math.Max(1, Math.Min(100, (int)Math.Round(pose.Visibility[j] * 100))) : 0);
            }
        }

        /// <summary>Images with the swimmer.</summary>
        [JsonIgnore]
        public int Found
        {
            get
            {
                int found = 0;
                for (int i = 0; i < Count; i++)
                {
                    if (Seen(i))
                        found++;
                }

                return found;
            }
        }

        private bool Seen(int i)
        {
            int o = i * Values;
            for (int j = 0; j < SwimJoints.Count; j++)
            {
                if (Data[o + j * 3 + 2] > 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// (2.13.1) An image whose skeleton is a side view of the swimmer: the trunk (shoulders → hips) at least a fifth of the extent
        /// of the joints seen, the two shoulders and the two hips together (within 0.6 of the trunk). In the lab's underwater turns
        /// 4 in 10 skeletons were not (the swimmer read as standing or from the front: the trunk folded to a point, the shoulders
        /// apart); in a demonstration of underwater kicks 97 in 100 were.
        /// </summary>
        public bool SideView(int i)
        {
            if (i < 0 || i >= Count)
                return false;

            int o = i * Values;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            int seen = 0;
            for (int j = 0; j < SwimJoints.Count; j++)
            {
                if (Data[o + j * 3 + 2] < 30)
                    continue;
                float x = Data[o + j * 3], y = Data[o + j * 3 + 1];
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                seen++;
            }

            if (seen < 4)
                return false;

            PointF? s = Mid(o, SwimJoints.LeftShoulder), h = Mid(o, SwimJoints.LeftHip);
            double extent = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
            if (!s.HasValue || !h.HasValue || extent < 1)
                return false;

            double trunk = Math.Sqrt((s.Value.X - h.Value.X) * (s.Value.X - h.Value.X) + (s.Value.Y - h.Value.Y) * (s.Value.Y - h.Value.Y));
            if (trunk < 0.2 * extent)
                return false;

            foreach (int left in new[] { SwimJoints.LeftShoulder, SwimJoints.LeftHip })
            {
                int a = o + left * 3, b = o + (left + 1) * 3;
                if (Data[a + 2] >= 30 && Data[b + 2] >= 30)
                {
                    double dx = Data[a] - Data[b], dy = Data[a + 1] - Data[b + 1];
                    if (Math.Sqrt(dx * dx + dy * dy) > 0.6 * trunk)
                        return false;
                }
            }

            return true;
        }

        private PointF? Mid(int o, int left)
        {
            int a = o + left * 3, b = o + (left + 1) * 3;
            bool l = Data[a + 2] >= 30, r = Data[b + 2] >= 30;
            if (l && r) return new PointF(0.5f * (Data[a] + Data[b]), 0.5f * (Data[a + 1] + Data[b + 1]));
            if (l) return new PointF(Data[a], Data[a + 1]);
            if (r) return new PointF(Data[b], Data[b + 1]);
            return null;
        }

        /// <summary>
        /// The joints at a timestamp: each the median of the images around it (±smooth) where it is seen (visibility at least
        /// 30), its visibility the best of them. False outside the part or where the swimmer was not found. sideView: only the
        /// images whose skeleton is a side view of the swimmer (SideView), at least two of them, one next to the timestamp.
        /// </summary>
        public bool TryGet(long timestamp, int smooth, PointF[] points, float[] visibility, bool sideView = false)
        {
            int n = Count;
            if (n == 0 || Step <= 0 || points == null || visibility == null)
                return false;

            double f = (timestamp - Start) / Step;
            if (f < -0.5 || f > n - 0.5)
                return false;

            int i = (int)Math.Round(Math.Max(0, Math.Min(n - 1, f)));
            if (!Seen(i) && !(i > 0 && Seen(i - 1) && i + 1 < n && Seen(i + 1)))
                return false;

            bool[] use = null;
            if (sideView)
            {
                use = new bool[2 * smooth + 1];
                int good = 0;
                bool near = false;
                for (int k = Math.Max(0, i - smooth); k <= Math.Min(n - 1, i + smooth); k++)
                {
                    bool ok = SideView(k);
                    use[k - i + smooth] = ok;
                    if (ok)
                    {
                        good++;
                        near = near || Math.Abs(k - i) <= 1;
                    }
                }

                if (good < 2 || !near)
                    return false;
            }

            List<float> xs = new List<float>(), ys = new List<float>();
            bool any = false;
            for (int j = 0; j < SwimJoints.Count; j++)
            {
                xs.Clear();
                ys.Clear();
                float best = 0;
                for (int k = Math.Max(0, i - smooth); k <= Math.Min(n - 1, i + smooth); k++)
                {
                    if (use != null && !use[k - i + smooth])
                        continue;
                    int o = k * Values + j * 3;
                    int v = Data[o + 2];
                    if (v < 30)
                        continue;

                    xs.Add(Data[o]);
                    ys.Add(Data[o + 1]);
                    if (Math.Abs(k - i) <= 1)
                        best = Math.Max(best, v / 100f);
                }

                if (xs.Count == 0)
                {
                    points[j] = PointF.Empty;
                    visibility[j] = 0;
                    continue;
                }

                xs.Sort();
                ys.Sort();
                points[j] = new PointF(xs[xs.Count / 2], ys[ys.Count / 2]);
                visibility[j] = best;
                any = any || best > 0;
            }

            return any;
        }

        /// <summary>The points of the part between two timestamps (included), null if there are fewer than two.</summary>
        public SwimSkeleton Slice(long from, long to)
        {
            int n = Count;
            if (n == 0 || Step <= 0 || to < from)
                return null;

            int first = (int)Math.Max(0, Math.Ceiling(((double)from - Start) / Step - 1e-9));
            int last = (int)Math.Min(n - 1, Math.Floor(((double)to - Start) / Step + 1e-9));
            if (last - first + 1 < 2)
                return null;

            SwimSkeleton part = new SwimSkeleton { Start = Start + (long)Math.Round(first * Step), Step = Step };
            part.Data.AddRange(Data.GetRange(first * Values, (last - first + 1) * Values));
            return part;
        }
    }

    /// <summary>A list of numbers written on one line of the indented analysis file (read as usual).</summary>
    public sealed class SwimNumbersOnOneLine : JsonConverter
    {
        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(List<int>);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            List<int> numbers = value as List<int>;
            if (numbers == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteRawValue("[" + string.Join(",", numbers.Select(n => n.ToString(CultureInfo.InvariantCulture))) + "]");
        }
    }

    /// <summary>
    /// One analysed swimmer in one race (or in a training video for the technical analysis).
    /// Times are not stored as seconds but as video timestamps, and converted with the video timing
    /// so that a change of the capture framerate (high speed) is honored.
    /// </summary>
    public class SwimRace
    {
        public const string KindRace = "race";
        public const string KindTraining = "training";
        public const string UnitMeters = "m";
        public const string UnitYards = "yd";
        public const double Yard = 0.9144;

        public string Id = Guid.NewGuid().ToString("N");
        public string Athlete = "";

        // Athlete from the federation registry (unique_id in the main Google Sheet). Empty for free text names.
        public string AthleteId = "";
        public string AthleteGender = "";
        public string AthleteBirthYear = "";
        public string AthleteCategory = "";
        public string AthleteClub = "";

        /// <summary>"race" (race analysis and technical analysis of its start and turns) or "training" (start and turn attempts).</summary>
        public string Kind = KindRace;

        public int Lane;
        public string Style = "fs";
        public bool Relay;
        public int Distance = 100;
        public int Pool = 50;

        /// <summary>"m" or "yd" (25 yard pools): distances in pool units, velocities and lengths always in meters.</summary>
        public string PoolUnit = UnitMeters;
        public string Competition = "";
        public string Location = "";
        public string Round = "";
        public string Date = DateTime.Now.ToString("d.M.yyyy", CultureInfo.InvariantCulture);
        public List<int> Checkpoints = new List<int>();

        /// <summary>Video timestamp of the start signal (race time zero).</summary>
        public long? StartTimestamp;

        /// <summary>
        /// The start was put by the auto pilot and the analyst has not checked it yet (since 2026-10-18): shown with ✈, and the light
        /// found again is proposed for the check instead of being taken as his start (which would teach the light from itself).
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool PilotStart;

        public List<SwimMark> Marks = new List<SwimMark>();

        /// <summary>
        /// (2.15.0) Where the analyst deleted an automatic stroke (✕): no automatic stroke is placed there again, by any reading of this race
        /// (before, the list was of the reading in progress only, and a second «Όλος ο αγώνας» put the deleted strokes back).
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<long> RemovedStrokes;

        /// <summary>(2.15.0) An automatic stroke was deleted near this timestamp (within 'near').</summary>
        public bool StrokeRemovedNear(long timestamp, long near)
        {
            return RemovedStrokes != null && RemovedStrokes.Any(r => Math.Abs(r - timestamp) < near);
        }

        /// <summary>
        /// Strokes of the segments confirmed or typed by the analyst (key: distance of the checkpoint at the end of the segment).
        /// They have priority over the count from the stroke marks.
        /// </summary>
        public Dictionary<int, double> StrokeCounts = new Dictionary<int, double>();

        // Manual values (typed by the analyst).
        public double? Reaction;
        public double? EntryDistHands;
        public double? EntryDistHips;
        public double? EntryVelHands;
        public double? EntryVelHips;
        public double? EntryVelFeet;
        public double? UnderwaterDist;
        public double? BreakoutDist;
        public double? FinishLastStrokeDist;

        /// <summary>(2.9.8) UnderwaterDist of the start is the estimate from the times (see SwimTurnManual.UnderwaterEstimated).</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool UnderwaterEstimated;

        /// <summary>
        /// (2.15.0) The swimmer straight, from the toes to the top of the head (pool units): measured on the video (📍, a click on the
        /// toes and one on the head in a frame of the glide; both under the water at the same depth, so the shift of the water cancels)
        /// or typed. At the push-off from a wall the toes are still on it and the body is straight: the head is this far from the wall,
        /// the start of the underwater phase of every turn of the race (SwimTechCalculator.UnderwaterSpeed). Null: not measured.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public double? BodyLength;

        /// <summary>
        /// The official times of the results, typed by the analyst (since 2026-10-19): the final time alone, or the times at every
        /// Distance / Official.Count (the 50 m splits of the results, the last one the final time). The walls at those distances
        /// are read at them and the analysis shows them (OfficialTimes). Null: none.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<double> Official;

        /// <summary>
        /// The competition in the official results the race was found with (since 2026-10-19): the link of the KOE live results, or
        /// the code of the competition there. Null: not linked.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ResultsLink;

        /// <summary>The official reaction time of the results (the release from the block, s), when they publish it.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public double? OfficialReaction;

        public Dictionary<int, SwimTurnManual> Turns = new Dictionary<int, SwimTurnManual>();

        /// <summary>Distances measured for the attempts of a training video, by attempt id.</summary>
        public Dictionary<string, SwimPhaseManual> PhaseValues = new Dictionary<string, SwimPhaseManual>();

        /// <summary>Elite reference for the deltas at the checkpoints (a copy, so it works offline).</summary>
        public SwimReference Reference;

        /// <summary>Comparison with a previous analysis of the athlete in the report: "auto" (best earlier time), "" (none) or its id.</summary>
        public string ComparePb = "auto";
        public string ComparePbLabel = "";

        // Technical analysis.
        public List<SwimAngle> Angles = new List<SwimAngle>();
        public List<SwimSnapshot> Snapshots = new List<SwimSnapshot>();
        public string TechNotes = "";

        /// <summary>(2.13.0) Evaluations of the starts and turns with the sheets of criteria.</summary>
        public List<SwimTechEval> TechEvals = new List<SwimTechEval>();

        /// <summary>Frame of the finish with the graphics of the video (final time, velocity chart), for the race report.</summary>
        public SwimSnapshot FinalFrame;

        /// <summary>Graphics of the video drawn for the PDF report (the summary card, the race against the final), on the Drive.</summary>
        public List<SwimReportGraphic> Graphics = new List<SwimReportGraphic>();

        /// <summary>The charts of the PDF report drawn by Kinovea with the graphics of the video, on the Drive.</summary>
        public List<SwimChartImage> ChartImages = new List<SwimChartImage>();

        /// <summary>
        /// Place of the swimmer in the video, found with the automatic strokes or with "Ακολούθηση αθλητή": the athlete label of
        /// the technical view follows it. One part per lap followed.
        /// </summary>
        public List<SwimTrack> Tracks = new List<SwimTrack>();

        /// <summary>Skeleton of the swimmer found with "Σκελετός" (technical analysis): one part per start or turn followed.</summary>
        public List<SwimSkeleton> Skeletons = new List<SwimSkeleton>();

        /// <summary>Marks proposed by the automatic analysis and not answered yet (see SwimProposal).</summary>
        public List<SwimProposal> Proposals = new List<SwimProposal>();

        /// <summary>
        /// Manual analysis: what the reading of the race in the background found (one click on the swimmer), never shown. The mark of
        /// the analyst at each of these events answers it, for the learning, as the check of a proposal would.
        /// </summary>
        public List<SwimProposal> QuietProposals = new List<SwimProposal>();

        /// <summary>The race was read in the background for the learning (once per race).</summary>
        public bool QuietRead;

        // Last synchronization with the Google Sheet.
        public string SheetName;
        public string SheetUrl;
        public string LastPdfUrl;

        /// <summary>
        /// (2.10.0) The package for the coaches («📦 Δημιουργία αποστολής»): its folder and the name of its files (made again in place), when it
        /// was made and when the analyst said it was sent (dd/MM/yyyy HH:mm).
        /// </summary>
        public string PackageFolder;
        public string PackageBase;
        public string PackageMade;
        public string PackageSent;

        [JsonIgnore]
        public bool IsTraining
        {
            get { return Kind == KindTraining; }
        }

        [JsonIgnore]
        public bool IsYards
        {
            get { return PoolUnit == UnitYards; }
        }

        /// <summary>Meters per pool unit (1 or 0.9144).</summary>
        [JsonIgnore]
        public double K
        {
            get { return IsYards ? Yard : 1.0; }
        }

        /// <summary>"μ." or "yd" after a distance.</summary>
        [JsonIgnore]
        public string Unit
        {
            get { return IsYards ? "yd" : SwimLang.T("μ."); }
        }

        [JsonIgnore]
        public string DistanceLabel
        {
            get { return Relay ? "4x" + (Distance / 4).ToString(CultureInfo.InvariantCulture) : Distance.ToString(CultureInfo.InvariantCulture); }
        }

        [JsonIgnore]
        public string PoolLabel
        {
            get { return Pool.ToString(CultureInfo.InvariantCulture) + (IsYards ? "yd" : SwimLang.T("μ.")); }
        }

        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                string name = string.IsNullOrWhiteSpace(Athlete) ? SwimLang.T("(χωρίς όνομα)") : Athlete.Trim();
                if (IsTraining)
                    return string.Format(SwimLang.T("{0} – τεχνική {1} ({2})"), name, SwimPresets.StyleShort(Style), PoolLabel);

                string lane = Lane > 0 ? SwimLang.T(" · Δ") + Lane.ToString(CultureInfo.InvariantCulture) : "";
                return string.Format("{0} – {1}{2} {3}{4}", name, DistanceLabel, IsYards ? "yd" : "", SwimPresets.StyleShort(Style), lane);
            }
        }

        public SwimMark GetSplit(int distance)
        {
            return Marks.FirstOrDefault(m => m.Type == SwimMarkType.Split && m.Distance == distance);
        }

        /// <summary>
        /// The official times by distance (Official): the last one at the distance of the race, the others every Distance / count
        /// when that falls on the walls of the pool (the 50 m splits of the results in a 50 m or a 25 m pool). Empty when none was
        /// typed or they do not fit the race (see OfficialProblem).
        /// </summary>
        public Dictionary<int, double> OfficialTimes()
        {
            Dictionary<int, double> times = new Dictionary<int, double>();
            List<double> official = Official == null ? null : Official.ToList();
            if (official == null || official.Count == 0 || OfficialProblem(official, Distance, Pool, K, Unit) != null)
                return times;

            int step = Distance / official.Count;
            for (int i = 0; i < official.Count; i++)
                times[step * (i + 1)] = official[i];
            return times;
        }

        /// <summary>
        /// Fastest average speed of a part of a race in official times (m/s): faster is a typo (the fastest 50 yd freestyle, start
        /// and turn in it, is about 2.6 m/s). A split typed as the final time, or the times of each 50 instead of the cumulative
        /// ones, give such a speed.
        /// </summary>
        public const double OfficialFastest = 2.75;

        /// <summary>Slowest average speed of a part of a race in official times that is not questioned (m/s; see OfficialWarning).</summary>
        public const double OfficialSlowest = 0.5;

        /// <summary>Slower than this (m/s) is a typo ("6123" for 1:01.23): an error, the times are not taken.</summary>
        public const double OfficialSlowestError = 0.2;

        /// <summary>
        /// Why official times do not fit a race (null: they do): each one after the one before, as many as the walls they fall on
        /// (one: the final time; else every distance / count, a multiple of the pool), and no part faster than a swimmer can be
        /// (OfficialFastest). metersPerUnit: 1, or 0.9144 for yards; unit: "μ." or "yd".
        /// </summary>
        public static string OfficialProblem(List<double> official, int distance, int pool, double metersPerUnit = 1.0, string unit = null)
        {
            if (official == null || official.Count == 0)
                return null;
            unit = unit ?? SwimLang.T("μ.");
            if (official.Any(t => double.IsNaN(t) || double.IsInfinity(t) || t <= 0))
                return SwimLang.T("οι χρόνοι πρέπει να είναι θετικοί");
            for (int i = 1; i < official.Count; i++)
            {
                if (official[i] <= official[i - 1])
                    return SwimLang.T("κάθε χρόνος πρέπει να είναι μετά τον προηγούμενο (οι ενδιάμεσοι αθροιστικά, όπως στα αποτελέσματα)");
            }

            if (official.Count > 1 && (distance <= 0 || pool <= 0 || distance % official.Count != 0 || (distance / official.Count) % pool != 0))
                return string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} χρόνοι δεν μοιράζουν τα {1} σε τοίχους της πισίνας: γράψε τον τελικό, ") +
                    SwimLang.T("ή όλους τους ενδιάμεσους των 50 (ή των 100) και τον τελικό"), official.Count, distance);

            foreach (OfficialPart part in OfficialParts(official, distance, metersPerUnit))
            {
                if (part.Speed > OfficialFastest)
                    return string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} για τα {1}-{2}{3} είναι πιο γρήγορο απ' ό,τι κολυμπά άνθρωπος: έλεγξε ") +
                        SwimLang.T("ότι είναι ο τελικός χρόνος (όχι ενδιάμεσος) και οι ενδιάμεσοι αθροιστικά, όπως στα αποτελέσματα"),
                        SwimReference.FormatTime(part.Time), part.From, part.To, unit);
                if (part.Speed < OfficialSlowestError)
                    return string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} για τα {1}-{2}{3} είναι πολύ αργό για χρόνος αγώνα: έλεγξε τα λεπτά ") +
                        SwimLang.T("και τα δευτερόλεπτα (π.χ. 1:01.23)"), SwimReference.FormatTime(part.Time), part.From, part.To, unit);
            }

            return null;
        }

        /// <summary>
        /// A part of the race slower than OfficialSlowest in official times that fit it (null: none, or they do not fit): shown, the
        /// times stand.
        /// </summary>
        public static string OfficialWarning(List<double> official, int distance, double metersPerUnit = 1.0, string unit = null)
        {
            if (official == null || official.Count == 0)
                return null;
            unit = unit ?? SwimLang.T("μ.");

            foreach (OfficialPart part in OfficialParts(official, distance, metersPerUnit))
            {
                if (part.Speed > 0 && part.Speed < OfficialSlowest)
                    return string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} για τα {1}-{2}{3} είναι πολύ αργό: έλεγξέ το"),
                        SwimReference.FormatTime(part.Time), part.From, part.To, unit);
            }

            return null;
        }

        private struct OfficialPart
        {
            public int From;
            public int To;
            public double Time;
            public double Speed;
        }

        /// <summary>The parts of the race between the official times (from the start to the first one, then from each to the next).</summary>
        private static IEnumerable<OfficialPart> OfficialParts(List<double> official, int distance, double metersPerUnit)
        {
            if (distance <= 0 || official.Count == 0 || distance % official.Count != 0)
                yield break;

            int step = distance / official.Count;
            double k = metersPerUnit > 0 ? metersPerUnit : 1.0;
            for (int i = 0; i < official.Count; i++)
            {
                double time = official[i] - (i == 0 ? 0 : official[i - 1]);
                if (time <= 0)
                    continue;
                yield return new OfficialPart { From = step * i, To = step * (i + 1), Time = time, Speed = step * k / time };
            }
        }

        /// <summary>
        /// Official times as typed: "1:01.23", or the splits and the final time ("28.45 1:01.23", "28,45 / 1:01,23"). Minutes
        /// with ':', the decimals after '.' or ','. Null when nothing was typed; error: what could not be read.
        /// </summary>
        public static List<double> ParseOfficial(string text, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(text))
                return null;

            List<double> times = new List<double>();
            foreach (string token in text.Split(new[] { ' ', '\t', '/', ';', '|', '–', '-' }, StringSplitOptions.RemoveEmptyEntries))
            {
                double t = ParseRaceTime(token);
                if (double.IsNaN(t))
                {
                    error = SwimLang.T("δεν διαβάζεται το «") + token + SwimLang.T("» (γράψε π.χ. 1:01.23)");
                    return null;
                }

                times.Add(t);
            }

            return times.Count == 0 ? null : times;
        }

        /// <summary>"1:01.23", "61.23", "61,23", "1:01,23" in seconds; NaN when it is not a time.</summary>
        public static double ParseRaceTime(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return double.NaN;

            string s = token.Trim().Replace(',', '.');
            int minutes = 0;
            int colon = s.IndexOf(':');
            if (colon >= 0)
            {
                if (colon == 0 || s.IndexOf(':', colon + 1) >= 0 || !int.TryParse(s.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
                    return double.NaN;
                s = s.Substring(colon + 1);
            }

            // (Digits and a point only: not "Infinity" or "NaN", which the parsing of numbers takes.)
            double seconds;
            if (s.Length == 0 || s.Any(c => c != '.' && (c < '0' || c > '9')) ||
                !double.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out seconds) || double.IsInfinity(seconds) || double.IsNaN(seconds))
                return double.NaN;
            if (colon >= 0 && seconds >= 60)
                return double.NaN;
            return minutes * 60 + seconds;
        }

        /// <summary>Official times as the analyst types them ("28.45 1:01.23"), "" for none.</summary>
        public static string FormatOfficial(List<double> official)
        {
            if (official == null || official.Count == 0)
                return "";
            return string.Join(" ", official.Select(SwimReference.FormatTime).ToArray());
        }

        /// <summary>The part of the skeleton that contains this timestamp, null if none.</summary>
        public SwimSkeleton SkeletonAt(long timestamp)
        {
            if (Skeletons == null)
                return null;

            return Skeletons.FirstOrDefault(s => s != null && s.Count > 0 &&
                timestamp >= s.Start - s.Step * 0.5 && timestamp <= s.End + s.Step * 0.5);
        }

        /// <summary>The part of the swimmer's track that contains this timestamp, null if none.</summary>
        public SwimTrack TrackAt(long timestamp)
        {
            if (Tracks == null)
                return null;

            return Tracks.FirstOrDefault(t => t != null && t.X.Count > 0 &&
                timestamp >= t.Start - t.Step * 0.5 && timestamp <= t.End + t.Step * 0.5);
        }

        /// <summary>
        /// Distance of the first checkpoint without a split time, or -1 if all are filled.
        /// </summary>
        public int NextCheckpoint()
        {
            foreach (int d in Checkpoints)
            {
                if (GetSplit(d) == null)
                    return d;
            }

            return -1;
        }

        /// <summary>
        /// Remove split marks whose distance is no longer a checkpoint.
        /// </summary>
        public void RemoveOrphanSplits()
        {
            Marks.RemoveAll(m => m.Type == SwimMarkType.Split && !Checkpoints.Contains(m.Distance));
        }

        public SwimTurnManual GetTurn(int wall, bool create)
        {
            SwimTurnManual turn;
            if (Turns.TryGetValue(wall, out turn))
                return turn;

            if (!create)
                return null;

            turn = new SwimTurnManual();
            Turns[wall] = turn;
            return turn;
        }

        public SwimPhaseManual GetPhaseValues(string phaseId, bool create)
        {
            if (string.IsNullOrEmpty(phaseId))
                return null;

            if (PhaseValues == null)
                PhaseValues = new Dictionary<string, SwimPhaseManual>();

            SwimPhaseManual values;
            if (PhaseValues.TryGetValue(phaseId, out values))
                return values;

            if (!create)
                return null;

            values = new SwimPhaseManual();
            PhaseValues[phaseId] = values;
            return values;
        }

        /// <summary>
        /// Lists that may be missing in analyses saved by an older version.
        /// </summary>
        public void EnsureLists()
        {
            if (Marks == null) Marks = new List<SwimMark>();
            if (StrokeCounts == null) StrokeCounts = new Dictionary<int, double>();
            if (Checkpoints == null) Checkpoints = new List<int>();
            if (Turns == null) Turns = new Dictionary<int, SwimTurnManual>();
            if (PhaseValues == null) PhaseValues = new Dictionary<string, SwimPhaseManual>();
            if (Angles == null) Angles = new List<SwimAngle>();
            if (Snapshots == null) Snapshots = new List<SwimSnapshot>();
            if (Tracks == null) Tracks = new List<SwimTrack>();
            Tracks.RemoveAll(t => t == null || t.X == null || t.Y == null || t.Top == null || t.Bottom == null || t.Step <= 0);
            foreach (SwimTrack t in Tracks)
            {
                if (t.Pan == null) t.Pan = new List<int>();
                if (t.Slope == null) t.Slope = new List<int>();
            }
            if (Skeletons == null) Skeletons = new List<SwimSkeleton>();
            Skeletons.RemoveAll(s => s == null || s.Data == null || s.Step <= 0 || s.Data.Count % SwimSkeleton.Values != 0);
            if (Proposals == null) Proposals = new List<SwimProposal>();
            Proposals.RemoveAll(p => p == null || string.IsNullOrEmpty(p.Kind));
            if (QuietProposals == null) QuietProposals = new List<SwimProposal>();
            QuietProposals.RemoveAll(p => p == null || string.IsNullOrEmpty(p.Kind));
            foreach (SwimProposal p in Proposals.Concat(QuietProposals))
            {
                if (p.Cues == null)
                    p.Cues = new Dictionary<string, long>();
            }
            if (string.IsNullOrEmpty(Kind)) Kind = KindRace;
            if (string.IsNullOrEmpty(PoolUnit)) PoolUnit = UnitMeters;
            if (ComparePb == null) ComparePb = "auto";
            if (TechNotes == null) TechNotes = "";
            if (TechEvals == null) TechEvals = new List<SwimTechEval>();
            TechEvals.RemoveAll(e => e == null || string.IsNullOrEmpty(e.PhaseId) || string.IsNullOrEmpty(e.SheetId));
            foreach (SwimTechEval e in TechEvals)
            {
                if (e.Scores == null) e.Scores = new Dictionary<string, SwimTechScore>();
                if (e.Notes == null) e.Notes = "";
                if (e.Date == null) e.Date = "";
            }
        }

        public bool ShouldSerializeTechEvals()
        {
            return TechEvals != null && TechEvals.Count > 0;
        }

        public bool ShouldSerializeQuietProposals()
        {
            return QuietProposals != null && QuietProposals.Count > 0;
        }

        /// <summary>
        /// What a reading of the video in the background needs, as it is now (the marks, the checkpoints): the edits of the analyst
        /// during the reading do not reach it. The images of the analysis (skeletons, tracks, snapshots) are not copied.
        /// </summary>
        public SwimRace CopyForReading()
        {
            SwimRace r = (SwimRace)MemberwiseClone();
            r.Marks = Marks.Select(m => new SwimMark { Id = m.Id, Type = m.Type, Timestamp = m.Timestamp, Distance = m.Distance, Wall = m.Wall, Batch = m.Batch,
                Pilot = m.Pilot }).ToList();
            r.Checkpoints = new List<int>(Checkpoints);
            r.StrokeCounts = new Dictionary<int, double>(StrokeCounts);
            r.RemovedStrokes = RemovedStrokes != null ? new List<long>(RemovedStrokes) : null;
            r.Turns = new Dictionary<int, SwimTurnManual>();
            r.PhaseValues = new Dictionary<string, SwimPhaseManual>();
            r.Reference = Reference != null ? Reference.Copy() : null;
            r.Angles = new List<SwimAngle>();
            r.Snapshots = new List<SwimSnapshot>();
            r.TechEvals = new List<SwimTechEval>();
            r.FinalFrame = null;
            r.Graphics = new List<SwimReportGraphic>();
            r.ChartImages = new List<SwimChartImage>();
            r.Tracks = new List<SwimTrack>();
            r.Skeletons = new List<SwimSkeleton>();
            r.Proposals = new List<SwimProposal>();
            r.QuietProposals = new List<SwimProposal>();
            return r;
        }

        /// <summary>
        /// Copy of the race information for another swimmer in the same video (same start signal).
        /// </summary>
        public SwimRace CloneForNewSwimmer()
        {
            SwimRace r = new SwimRace();
            r.Kind = Kind;
            r.Style = Style;
            r.Relay = Relay;
            r.Distance = Distance;
            r.Pool = Pool;
            r.PoolUnit = PoolUnit;
            r.Competition = Competition;
            r.Location = Location;
            r.Round = Round;
            r.Date = Date;
            r.Checkpoints = new List<int>(Checkpoints);
            r.StartTimestamp = StartTimestamp;
            r.PilotStart = PilotStart;
            r.Reference = Reference != null ? Reference.Copy() : null;
            return r;
        }
    }

    /// <summary>
    /// All the races analysed on one video. Saved next to the video as "video.ext.swim.json".
    /// </summary>
    public class SwimVideoFile
    {
        /// <summary>2: pools in yards, technical analysis (training videos, angles, snapshots). Files of version 1 load as they are.</summary>
        public const int CurrentVersion = 2;
        public int Version = CurrentVersion;
        public string VideoPath;
        public string ActiveRaceId;
        public List<SwimRace> Races = new List<SwimRace>();

        /// <summary>Perspective calibrations of the water surface (shared by all the swimmers of the video).</summary>
        public List<SwimCalibration> Calibrations = new List<SwimCalibration>();
    }
}
