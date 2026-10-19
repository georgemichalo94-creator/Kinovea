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

        /// <summary>
        /// The start was put by the auto pilot and the analyst has not checked it yet (since 2026-10-18): shown with ✈, and the light
        /// found again is proposed for the check instead of being taken as his start (which would teach the light from itself).
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool PilotStart;

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

        /// <summary>
        /// The official times of the results, typed by the analyst (since 2026-10-19): the final time alone, or the times at every
        /// Distance / Official.Count (the 50 m splits of the results, the last one the final time). The walls at those distances
        /// are read at them and the analysis shows them (OfficialTimes). Null: none.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<double> Official;

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
        public static string OfficialProblem(List<double> official, int distance, int pool, double metersPerUnit = 1.0, string unit = "μ.")
        {
            if (official == null || official.Count == 0)
                return null;
            if (official.Any(t => double.IsNaN(t) || double.IsInfinity(t) || t <= 0))
                return "οι χρόνοι πρέπει να είναι θετικοί";
            for (int i = 1; i < official.Count; i++)
            {
                if (official[i] <= official[i - 1])
                    return "κάθε χρόνος πρέπει να είναι μετά τον προηγούμενο (οι ενδιάμεσοι αθροιστικά, όπως στα αποτελέσματα)";
            }

            if (official.Count > 1 && (distance <= 0 || pool <= 0 || distance % official.Count != 0 || (distance / official.Count) % pool != 0))
                return string.Format(CultureInfo.InvariantCulture, "{0} χρόνοι δεν μοιράζουν τα {1} σε τοίχους της πισίνας: γράψε τον τελικό, " +
                    "ή όλους τους ενδιάμεσους των 50 (ή των 100) και τον τελικό", official.Count, distance);

            foreach (OfficialPart part in OfficialParts(official, distance, metersPerUnit))
            {
                if (part.Speed > OfficialFastest)
                    return string.Format(CultureInfo.InvariantCulture, "{0} για τα {1}-{2}{3} είναι πιο γρήγορο απ' ό,τι κολυμπά άνθρωπος: έλεγξε " +
                        "ότι είναι ο τελικός χρόνος (όχι ενδιάμεσος) και οι ενδιάμεσοι αθροιστικά, όπως στα αποτελέσματα",
                        SwimReference.FormatTime(part.Time), part.From, part.To, unit);
                if (part.Speed < OfficialSlowestError)
                    return string.Format(CultureInfo.InvariantCulture, "{0} για τα {1}-{2}{3} είναι πολύ αργό για χρόνος αγώνα: έλεγξε τα λεπτά " +
                        "και τα δευτερόλεπτα (π.χ. 1:01.23)", SwimReference.FormatTime(part.Time), part.From, part.To, unit);
            }

            return null;
        }

        /// <summary>
        /// A part of the race slower than OfficialSlowest in official times that fit it (null: none, or they do not fit): shown, the
        /// times stand.
        /// </summary>
        public static string OfficialWarning(List<double> official, int distance, double metersPerUnit = 1.0, string unit = "μ.")
        {
            if (official == null || official.Count == 0)
                return null;

            foreach (OfficialPart part in OfficialParts(official, distance, metersPerUnit))
            {
                if (part.Speed > 0 && part.Speed < OfficialSlowest)
                    return string.Format(CultureInfo.InvariantCulture, "{0} για τα {1}-{2}{3} είναι πολύ αργό: έλεγξέ το",
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
                    error = "δεν διαβάζεται το «" + token + "» (γράψε π.χ. 1:01.23)";
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
            r.Turns = new Dictionary<int, SwimTurnManual>();
            r.PhaseValues = new Dictionary<string, SwimPhaseManual>();
            r.Reference = Reference != null ? Reference.Copy() : null;
            r.Angles = new List<SwimAngle>();
            r.Snapshots = new List<SwimSnapshot>();
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
