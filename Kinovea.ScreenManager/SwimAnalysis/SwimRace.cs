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
    }

    public class SwimTurnManual
    {
        public double? UnderwaterDist;
        public double? TurnTime;
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

        /// <summary>JPEG image next to the video (folder "video.ext.swim").</summary>
        public string File = "";

        /// <summary>Google Drive file of the uploaded image (empty until it is uploaded).</summary>
        public string FileId = "";
        public string EliteFileId = "";
        public string EliteLabel = "";
        public string EliteComment = "";
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

            SwimTrack part = new SwimTrack { Start = Start + (long)Math.Round(first * Step), Step = Step };
            part.X.AddRange(X.GetRange(first, last - first + 1));
            part.Y.AddRange(Y.GetRange(first, last - first + 1));
            part.Top.AddRange(Top.GetRange(first, last - first + 1));
            part.Bottom.AddRange(Bottom.GetRange(first, last - first + 1));
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
        /// The joints at a timestamp: each the median of the images around it (±smooth) where it is seen (visibility at least
        /// 30), its visibility the best of them. False outside the part or where the swimmer was not found.
        /// </summary>
        public bool TryGet(long timestamp, int smooth, PointF[] points, float[] visibility)
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

            List<float> xs = new List<float>(), ys = new List<float>();
            bool any = false;
            for (int j = 0; j < SwimJoints.Count; j++)
            {
                xs.Clear();
                ys.Clear();
                float best = 0;
                for (int k = Math.Max(0, i - smooth); k <= Math.Min(n - 1, i + smooth); k++)
                {
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

        public List<SwimMark> Marks = new List<SwimMark>();

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

        /// <summary>Frame of the finish with the graphics of the video (final time, velocity chart), for the race report.</summary>
        public SwimSnapshot FinalFrame;

        /// <summary>
        /// Place of the swimmer in the video, found with the automatic strokes or with "Ακολούθηση αθλητή": the athlete label of
        /// the technical view follows it. One part per lap followed.
        /// </summary>
        public List<SwimTrack> Tracks = new List<SwimTrack>();

        /// <summary>Skeleton of the swimmer found with "Σκελετός" (technical analysis): one part per start or turn followed.</summary>
        public List<SwimSkeleton> Skeletons = new List<SwimSkeleton>();

        // Last synchronization with the Google Sheet.
        public string SheetName;
        public string SheetUrl;
        public string LastPdfUrl;

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
            get { return IsYards ? "yd" : "μ."; }
        }

        [JsonIgnore]
        public string DistanceLabel
        {
            get { return Relay ? "4x" + (Distance / 4).ToString(CultureInfo.InvariantCulture) : Distance.ToString(CultureInfo.InvariantCulture); }
        }

        [JsonIgnore]
        public string PoolLabel
        {
            get { return Pool.ToString(CultureInfo.InvariantCulture) + (IsYards ? "yd" : "μ."); }
        }

        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                string name = string.IsNullOrWhiteSpace(Athlete) ? "(χωρίς όνομα)" : Athlete.Trim();
                if (IsTraining)
                    return string.Format("{0} – τεχνική {1} ({2})", name, SwimPresets.StyleShort(Style), PoolLabel);

                string lane = Lane > 0 ? " · Δ" + Lane.ToString(CultureInfo.InvariantCulture) : "";
                return string.Format("{0} – {1}{2} {3}{4}", name, DistanceLabel, IsYards ? "yd" : "", SwimPresets.StyleShort(Style), lane);
            }
        }

        public SwimMark GetSplit(int distance)
        {
            return Marks.FirstOrDefault(m => m.Type == SwimMarkType.Split && m.Distance == distance);
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
            if (Skeletons == null) Skeletons = new List<SwimSkeleton>();
            Skeletons.RemoveAll(s => s == null || s.Data == null || s.Step <= 0 || s.Data.Count % SwimSkeleton.Values != 0);
            if (string.IsNullOrEmpty(Kind)) Kind = KindRace;
            if (string.IsNullOrEmpty(PoolUnit)) PoolUnit = UnitMeters;
            if (ComparePb == null) ComparePb = "auto";
            if (TechNotes == null) TechNotes = "";
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
