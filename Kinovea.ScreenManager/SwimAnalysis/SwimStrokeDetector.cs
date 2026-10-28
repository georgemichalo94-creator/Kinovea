#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Image of the stroke detector at its working resolution: 24-bit BGR, rows of Width * 3 bytes, no padding.
    /// </summary>
    public sealed class SwimStrokeImage
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Data;

        public SwimStrokeImage(int width, int height, byte[] data)
        {
            Width = width;
            Height = height;
            Data = data;
        }

        /// <summary>Bilinear sample of the three channels, edges replicated.</summary>
        public void Sample(double x, double y, out double b, out double g, out double r)
        {
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x > Width - 1) x = Width - 1;
            if (y > Height - 1) y = Height - 1;
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            int x1 = x0 + 1 < Width ? x0 + 1 : Width - 1;
            int y1 = y0 + 1 < Height ? y0 + 1 : Height - 1;
            double fx = x - x0;
            double fy = y - y0;
            int i00 = (y0 * Width + x0) * 3, i01 = (y0 * Width + x1) * 3;
            int i10 = (y1 * Width + x0) * 3, i11 = (y1 * Width + x1) * 3;
            double w00 = (1 - fx) * (1 - fy), w01 = fx * (1 - fy), w10 = (1 - fx) * fy, w11 = fx * fy;
            b = Data[i00] * w00 + Data[i01] * w01 + Data[i10] * w10 + Data[i11] * w11;
            g = Data[i00 + 1] * w00 + Data[i01 + 1] * w01 + Data[i10 + 1] * w10 + Data[i11 + 1] * w11;
            r = Data[i00 + 2] * w00 + Data[i01 + 2] * w01 + Data[i10 + 2] * w10 + Data[i11 + 2] * w11;
        }
    }

    /// <summary>
    /// Lane band of one frame: two lines y = A x + B (working coordinates), the far one first.
    /// </summary>
    public struct SwimStrokeLane
    {
        public double A1, B1, A2, B2;

        /// <summary>
        /// The ropes seen by the texture of their floats (1: the far one, 2: the near one, 3: both): faint ropes of a view from far
        /// (blue and white floats on blue water), followed on their floats, not on the lines of colour next to them (their shade).
        /// </summary>
        public int TextureRopes;

        public SwimStrokeLane(double a1, double b1, double a2, double b2)
        {
            A1 = a1; B1 = b1; A2 = a2; B2 = b2;
            TextureRopes = 0;
        }

        public double Y1(double x) { return A1 * x + B1; }
        public double Y2(double x) { return A2 * x + B2; }
        public double Width(double x) { return Y2(x) - Y1(x); }
    }

    public sealed class SwimStrokeResult
    {
        /// <summary>Frames (index from the first frame given to the detector) of the strokes, in order.</summary>
        public List<int> Strokes = new List<int>();

        /// <summary>Stroke cycle (s) and time between two stroke marks (s).</summary>
        public double Cycle;
        public double Period;

        /// <summary>True when the lane ropes were found, false for a band along the swimmer.</summary>
        public bool Ropes;

        /// <summary>Lane and position of the swimmer along the band (working x) for each frame.</summary>
        public List<SwimStrokeLane> Lanes = new List<SwimStrokeLane>();
        public int[] Path;

        /// <summary>Stroke signal (onset strength) for each frame.</summary>
        public double[] Onset;

        /// <summary>
        /// Red parts of the far and the near rope of the lane in each frame (the marks of the ropes: 15 m, 25 m, the last 5 m):
        /// pairs of working x (from, to), in order.
        /// </summary>
        public List<int[]> RedFar = new List<int[]>();
        public List<int[]> RedNear = new List<int[]>();

        /// <summary>Band difference (motion across the band) of each frame along the band (working x).</summary>
        public List<double[]> Motion = new List<double[]>();

        /// <summary>Camera displacement along the band from the previous frame (working x), per frame.</summary>
        public List<double[]> Shifts = new List<double[]>();

        /// <summary>Camera motion from the previous frame (homography, 9 values row-major, working coordinates), per frame.</summary>
        public List<double[]> Motions = new List<double[]>();

        /// <summary>
        /// The camera did not move during the reading (a fixed view of the pool): Motion is then the band difference without
        /// what stays at the same place of the image all along the reading.
        /// </summary>
        public bool Still;

        /// <summary>
        /// Since 2.8.0: the camera's displacement along the band at the swimmer from the previous frame (working x; Shifts at his place
        /// in the frame before), kept when the frames of a length of a race reading are let go with Shifts: what the ghost and the line
        /// of a reference need. Null until kept.
        /// </summary>
        public double[] PathShifts;

        /// <summary>
        /// Since 2.8.0: how far the head (the front of the moving swimmer, where the checkpoints are read) is ahead of his place in the
        /// path, in widths of the lane (median of the frames), NaN if not known.
        /// </summary>
        public double HeadLead = double.NaN;

        /// <summary>The displacement at the swimmer is known for each of the first n frames (Shifts, or kept in PathShifts).</summary>
        public bool HasPathShifts(int n)
        {
            return (Shifts != null && Shifts.Count >= n) || (PathShifts != null && PathShifts.Length >= n);
        }

        /// <summary>The camera's displacement at the swimmer from frame i − 1 to frame i (working x, 0 if not known).</summary>
        public double PathShift(int i)
        {
            double move = double.NaN;
            if (i <= 0)
                return 0;

            if (Shifts != null && i < Shifts.Count && Path != null && i - 1 < Path.Length)
            {
                double[] shift = Shifts[i];
                if (shift != null && shift.Length > 0)
                    move = shift[Math.Max(0, Math.Min(shift.Length - 1, Path[i - 1]))];
            }
            else if (PathShifts != null && i < PathShifts.Length)
            {
                move = PathShifts[i];
            }

            return double.IsNaN(move) || double.IsInfinity(move) ? 0 : move;
        }

        /// <summary>Keeps the displacement at the swimmer of each frame (PathShifts) before Shifts go.</summary>
        public void KeepPathShifts()
        {
            if (Path == null || Shifts == null || Shifts.Count == 0)
                return;

            int n = Math.Min(Path.Length, Shifts.Count);
            double[] kept = new double[n];
            for (int i = 1; i < n; i++)
                kept[i] = PathShift(i);
            PathShifts = kept;
        }
    }

    /// <summary>
    /// Automatic stroke detection on a video where the camera follows the swimmer (stands, side above or under the water).
    ///
    /// The analyst clicks the head and the feet of the swimmer on the first frame. Then, frame by frame:
    /// 1. lane: on the first frame the lane ropes around the swimmer (projection of the image along the dominant
    ///    direction, regularly spaced chain of rope candidates, pair of ropes containing the swimmer). Without ropes
    ///    (side views) a band along the swimmer's axis.
    /// 2. the lane is moved with the camera motion and each rope is refined on the new frame; a rope that leaves the
    ///    image, is hidden or loses its texture follows the other one with the place it had relative to it.
    /// 3. band difference: |band(t) − band(t−2) moved by the camera|, averaged across the band, per column.
    /// At each count (Finish), with the frames given so far:
    /// 4. position of the swimmer along the band: Viterbi on the smoothed band difference, with the camera motion (a fixed
    ///    camera: without the motion that stays at the same places of the image, the light on the water and the ropes).
    /// 5. stroke signal: band difference ahead of the swimmer; period by autocorrelation; stroke times by dynamic
    ///    programming (beat tracking: one stroke per period, on the peaks of the signal).
    ///
    /// The camera motion (3×3 homography from the previous frame) is computed outside (OpenCV).
    /// Reference implementation and evaluation: Python prototype of the same algorithm.
    /// </summary>
    public sealed class SwimStrokeDetector
    {
        /// <summary>Debug output of the lane search (null: none).</summary>
        public static Action<string> LaneTrace;

        private const int BandRows = 16;
        private const double BandLo = 0.15;
        private const double BandHi = 0.85;
        private const int FrameGap = 2;
        private const double BandBlur = 2.0;
        private const int RopeR = 8;
        private const int RopeMargin = 18;
        private const double RopeSigma = 6.0;
        private const int RopeStep = 4;

        private readonly int width;
        private readonly int height;
        private readonly double headX, headY, feetX, feetY;
        private readonly string style;
        private readonly double fps;
        private readonly double length;
        private readonly int direction;

        private bool initialized;
        private bool ropes;

        // Outer lane: the rope of the wall side is not seen (1: the far one, 2: the near one), its place follows the other rope.
        private int virtualRope;
        private SwimStrokeLane lane;

        // Rope tracking: usual rope-ness of each rope, and the second rope relative to the first one when both were seen
        // (slope difference, vertical offset at the middle of the image).
        private double ropeStrength1;
        private double ropeStrength2;
        private double relativeSlope;
        private double relativeOffset;
        private readonly List<SwimStrokeImage> previous = new List<SwimStrokeImage>();
        private readonly List<double[]> previousMotion = new List<double[]>();
        private readonly List<double[]> differences = new List<double[]>();
        private readonly List<double[]> colourDifferences = new List<double[]>();
        private readonly List<double[]> shifts = new List<double[]>();
        private readonly List<double[]> motions = new List<double[]>();
        private readonly List<SwimStrokeLane> lanes = new List<SwimStrokeLane>();
        private readonly List<int[]> redFar = new List<int[]>();
        private readonly List<int[]> redNear = new List<int[]>();

        // Lane given by the caller (the lane followed in the previous length): no search on the first frame.
        private readonly bool hasLane0;
        private readonly SwimStrokeLane lane0;
        private readonly int virtualRope0;

        /// <summary>
        /// The next length after a wall: the lane is the one followed in the previous length at this frame (the search on the first
        /// frame could take another lane at the far end of the pool, where the lanes are narrow).
        /// </summary>
        public SwimStrokeDetector(int width, int height, double headX, double headY, double feetX, double feetY, string style, double fps,
            SwimStrokeLane lane, int virtualRope)
            : this(width, height, headX, headY, feetX, feetY, style, fps)
        {
            hasLane0 = true;
            lane0 = lane;
            virtualRope0 = virtualRope;
        }

        // The place of the swimmer along the band known at one frame (the click, when the reading starts before it), −1 if none.
        private int anchorFrame = -1;
        private double anchorX;

        /// <summary>
        /// The swimmer is at this x of the band (middle of the head and the feet clicked) at this frame (index of the frames given):
        /// the path goes through it (a reading from the start signal, clicked later: the glints of the water ahead of the swimmer
        /// do not take the path away).
        /// </summary>
        public void Anchor(int frame, double x)
        {
            anchorFrame = frame;
            anchorX = x;
        }

        // The swimmer at the wall of a turn from the first frames: the place there (band x at frame holdFrom), moved with the camera.
        private int holdFrom = -1, holdTo = -1;
        private double holdX;

        /// <summary>
        /// The swimmer stays at this x of the band from the frame 'from' to the frame 'to' (indices of the frames given), the camera
        /// moving it: a turn, where he is at the wall before he pushes off (the reading of the length after a wall starts there).
        /// </summary>
        public void Hold(int from, int to, double x)
        {
            holdFrom = from;
            holdTo = to;
            holdX = x;
        }

        /// <summary>Seconds the swimmer stays at the wall after an open turn's touch for the reading after it (0: no hold; tests).</summary>
        public static double TurnHold = 0.0;

        // The swimmer under the water after a turn: from frame 0 for glideFrames frames, from glideX at glideSpeed (px a frame).
        private int glideFrames;
        private double glideX, glideSpeed;

        /// <summary>
        /// After a turn the swimmer glides and kicks under the water, unseen (the surface hardly moves), while the water he swam
        /// through before is still moving: for these first frames of the reading the path stays near where he goes from the wall
        /// (x at frame 0) at his speed of the length before (px a frame relative to the water, towards where he swims), less sure
        /// of it frame after frame.
        /// </summary>
        public void Glide(int frames, double x, double speed)
        {
            glideFrames = frames;
            glideX = x;
            glideSpeed = speed;
        }

        /// <summary>
        /// Last frame of the reading (−1: all): the frames after it (after the wall, when the camera turns fast to the scoreboard
        /// or cuts) are left out of the result, so that they do not move the path before it.
        /// </summary>
        public int PathEnd = -1;

        /// <summary>Part of the usual time under the water after a turn the path is kept near it (0: never; tests).</summary>
        public static double GlideShare = 0.8;

        /// <summary>Uncertainty of the speed under the water (part of it; tests).</summary>
        internal static double GlideSpread = 0.2;

        /// <summary>Rope of the lane that is not seen (1 far, 2 near) next to the wall of the pool, 0 if both are seen.</summary>
        public int VirtualRope
        {
            get { return virtualRope; }
        }

        /// <summary>Length of the swimmer at the start of the reading (head to feet given, working px).</summary>
        public double SwimmerLength
        {
            get { return Math.Sqrt((headX - feetX) * (headX - feetX) + (headY - feetY) * (headY - feetY)); }
        }

        /// <summary>
        /// The first frame at its own size (larger than the working size): the floats of the ropes of the far lanes, one or two
        /// pixels at the working size, are seen there. Given before the first frame when the lane is to be found; used for the
        /// lane only, then dropped.
        /// </summary>
        public SwimStrokeImage Detail { get; set; }

        /// <summary>The lane will be searched in the next frame (the first one, no lane given): a Detail of it helps.</summary>
        public bool SearchesLane
        {
            get { return !initialized && !hasLane0; }
        }

        /// <summary>
        /// The frames of the reading are no longer needed (a length of the whole race is done): the band of each frame and the
        /// images kept go, the lane and the direction stay. Count is 0 after it.
        /// </summary>
        public void Release()
        {
            previous.Clear();
            previousMotion.Clear();
            differences.Clear();
            colourDifferences.Clear();
            shifts.Clear();
            motions.Clear();
            redFar.Clear();
            redNear.Clear();
            differences.TrimExcess();
            colourDifferences.TrimExcess();
            shifts.TrimExcess();
        }

        /// <param name="style">Style code of the panel: fs, bk, br, fly.</param>
        public SwimStrokeDetector(int width, int height, double headX, double headY, double feetX, double feetY, string style, double fps)
        {
            this.width = width;
            this.height = height;
            this.headX = headX;
            this.headY = headY;
            this.feetX = feetX;
            this.feetY = feetY;
            this.style = style;
            this.fps = fps > 0 ? fps : 25;
            length = Math.Abs(headX - feetX);
            if (length < 1)
                length = 1;
            direction = headX > feetX ? 1 : -1;
        }

        public int Count
        {
            get { return differences.Count; }
        }

        public bool FoundRopes
        {
            get { return ropes; }
        }

        /// <summary>+1 if the swimmer goes to larger x in the image (head right of the feet when clicked), −1 otherwise.</summary>
        public int Direction
        {
            get { return direction; }
        }

        public SwimStrokeLane CurrentLane
        {
            get { return lane; }
        }

        #region Style
        /// <summary>
        /// Shortest stroke cycle (s) of backstroke and freestyle (2.11.1; before 0.7 both): a cycle of 0.7 s took the half strokes
        /// of a length of backstroke (an arm every 0.36-0.43 s, twice the strokes) in 22 of the 72 lengths of backstroke of the
        /// analysts' races (none of their swimmers under 1.2 s, the fastest backstroke 50 m about 1.05 s), in 2 of freestyle.
        /// </summary>
        internal static double CycleLoBack = 1.0;
        internal static double CycleLoFree = 0.85;

        /// <summary>Plausible stroke cycle (s) of a style.</summary>
        public static void CycleRange(string style, out double lo, out double hi)
        {
            switch (style)
            {
                case "fly": lo = 0.6; hi = 1.6; break;
                case "br": lo = 0.6; hi = 1.8; break;
                case "bk": lo = CycleLoBack; hi = 2.0; break;
                default: lo = CycleLoFree; hi = 1.9; break;
            }
        }

        /// <summary>
        /// (2.11.1) After a turn the analysts count the strokes from the one after the breakout stroke (backstroke, breaststroke,
        /// butterfly): the automatic strokes before the breakout + TurnStrokeLead strokes go. 82 analysts' races, the 15 m after
        /// the 107 turns: one stroke and a half too many before (+1.5 on average, 25% within half a stroke), with it within half a
        /// stroke ... (see experiments.md, Phase 16). In freestyle the stroke before the head is out counts: as before.
        /// </summary>
        internal static double TurnStrokeLead = 0.9;

        /// <summary>Without a breakout found after a turn: no stroke counted before the wall + this (s; NaN: as before).</summary>
        internal static double TurnStrokeFloor(string style)
        {
            switch (style)
            {
                case "bk": return TurnFloorBack;
                case "fly": return TurnFloorFly;
                case "br": return TurnFloorBreast;
                default: return double.NaN;
            }
        }

        internal static double TurnFloorBack = 6.0;
        internal static double TurnFloorFly = 7.0;
        internal static double TurnFloorBreast = double.NaN;

        /// <summary>The turn rule of the strokes applies to the style (not freestyle).</summary>
        public static bool TurnStrokeCut(string style)
        {
            return style == "bk" || style == "br" || style == "fly";
        }

        /// <summary>Stroke marks per cycle: every arm in free and back, every cycle in fly and breast.</summary>
        public static int Presses(string style)
        {
            return (style == "fs" || style == "bk") ? 2 : 1;
        }

        /// <summary>
        /// No stroke is searched in this time before the wall (s): in free and back the turn or the finish make splashes
        /// that look like strokes.
        /// </summary>
        public static double WallMargin(string style)
        {
            return (style == "fs" || style == "bk") ? 0.6 : 0.1;
        }
        #endregion

        /// <summary>
        /// Add the next frame. motion: homography (9 values, row-major) from the previous frame to this one, null for the first frame.
        /// </summary>
        public void Add(SwimStrokeImage image, double[] motion)
        {
            double[] shift = new double[width];
            if (!initialized)
            {
                initialized = true;
                SwimStrokeLane found;
                if (hasLane0)
                {
                    found = lane0;
                    virtualRope = virtualRope0;
                    ropes = true;
                }
                else
                {
                    ropes = FindLane(image, headX, headY, feetX, feetY, out found, out virtualRope, Detail);
                    Detail = null;
                }
                if (ropes)
                {
                    double a, b;
                    lane = found;
                    int reach = RefineReach(found.Width(0.5 * (headX + feetX)), RefineShare);
                    bool t1 = (found.TextureRopes & 1) != 0, t2 = (found.TextureRopes & 2) != 0;
                    if (virtualRope != 1 && RefineLine(image, found.A1, found.B1, out a, out b, reach, t1)) { lane.A1 = a; lane.B1 = b; }
                    if (virtualRope != 2 && RefineLine(image, found.A2, found.B2, out a, out b, reach, t2)) { lane.A2 = a; lane.B2 = b; }

                    double inside;
                    ropeStrength1 = RopeStrength(image, lane.A1, lane.B1, out inside, t1);
                    ropeStrength2 = RopeStrength(image, lane.A2, lane.B2, out inside, t2);
                    KeepRopeGeometry(lane);
                }
                else
                {
                    lane = AxisBand(headX, headY, feetX, feetY);
                }
            }
            else
            {
                double[] H = motion ?? Identity();
                SwimStrokeLane before = lane;

                // Camera displacement along the band (centre line of the previous band -> this frame).
                CameraShift(H, before, shift);

                // A motion of the camera no camera makes (a cut, a fast turn to the scoreboard, a motion not found): the camera
                // as in the frame before, this frame without motion of the swimmer; after a few such frames the reading is blind
                // (the lane is lost), so that what comes after does not take the path of the frames before.
                if (MotionCheck && (blind || AbnormalShift(shift)))
                {
                    abnormalRun++;
                    if (abnormalRun >= (int)Math.Max(3, Math.Round(0.2 * fps)))
                        blind = true;
                    H = blind ? Identity() : (lastGoodMotion ?? Identity());
                    CameraShift(H, before, shift);
                    motion = H;
                    quietFrames = FrameGap + 1;
                }
                else
                {
                    abnormalRun = 0;
                    lastGoodMotion = motion;
                }

                SwimStrokeLane predicted;
                MapLine(H, before.A1, before.B1, 0, width, out predicted.A1, out predicted.B1);
                MapLine(H, before.A2, before.B2, 0, width, out predicted.A2, out predicted.B2);
                predicted.TextureRopes = before.TextureRopes;
                SwimStrokeLane next = predicted;
                if (ropes && !blind)
                    next = TrackRopes(image, predicted);

                lane = next;
            }

            lanes.Add(lane);
            shifts.Add(shift);
            motions.Add(motion ?? Identity());
            redFar.Add(ropes && virtualRope != 1 ? RedRuns(image, lane.A1, lane.B1) : new int[0]);
            redNear.Add(ropes && virtualRope != 2 ? RedRuns(image, lane.A2, lane.B2) : new int[0]);

            double[] d = new double[width];
            double[] dc = PathColour > 0 ? new double[width] : null;
            if (previous.Count == FrameGap && quietFrames == 0 && !blind)
            {
                double[] M = Identity();
                for (int k = 1; k < previousMotion.Count; k++)
                    M = Multiply(previousMotion[k], M);
                M = Multiply(motion ?? Identity(), M);
                double[] Minv = Invert(M);
                BandDifference(image, previous[0], Minv, d, dc);
            }

            if (quietFrames > 0)
                quietFrames--;
            differences.Add(d);
            if (dc != null)
                colourDifferences.Add(dc);
            previous.Add(image);
            previousMotion.Add(motion ?? Identity());
            if (previous.Count > FrameGap)
            {
                previous.RemoveAt(0);
                previousMotion.RemoveAt(0);
            }
        }

        /// <summary>Displacement along the band of its centre line from the frame before to this one (motion H).</summary>
        private void CameraShift(double[] H, SwimStrokeLane before, double[] shift)
        {
            for (int x = 0; x < width; x++)
            {
                double yc = 0.5 * (before.Y1(x) + before.Y2(x));
                double X, Y;
                MapPoint(H, x, yc, out X, out Y);
                shift[x] = X - x;
            }
        }

        /// <summary>
        /// A displacement of the band from one frame to the next that no camera makes, the motion of the camera not found (a cut,
        /// a blur): more than a quarter of the width at a place, or 8% more at one end than at the other, or not a number. The
        /// fast turns of the camera after a finish (12–15% a frame, the ends together) are motions.
        /// </summary>
        private bool AbnormalShift(double[] shift)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (double s in shift)
            {
                if (double.IsNaN(s) || double.IsInfinity(s) || Math.Abs(s) > 0.25 * width)
                    return true;
                lo = Math.Min(lo, s);
                hi = Math.Max(hi, s);
            }

            return hi - lo > 0.08 * width;
        }

        /// <summary>The frames after a motion no camera makes do not count (tests).</summary>
        internal static bool MotionCheck = true;

        // The camera motion check: abnormal frames in a row, the reading blind after enough of them, the last normal motion, the
        // frames without band difference still to come (their difference crosses an abnormal frame).
        private int abnormalRun;
        private bool blind;
        private double[] lastGoodMotion;
        private int quietFrames;

        private void BandDifference(SwimStrokeImage current, SwimStrokeImage before, double[] Minv, double[] d, double[] colour = null)
        {
            int n = BandRows * width;
            double[] cur = new double[n * 3];
            double[] prv = new double[n * 3];
            bool[] inside = new bool[n];
            for (int row = 0; row < BandRows; row++)
            {
                double f = BandLo + (BandHi - BandLo) * row / (BandRows - 1);
                for (int x = 0; x < width; x++)
                {
                    double y = lane.Y1(x) * (1 - f) + lane.Y2(x) * f;
                    double px, py;
                    MapPoint(Minv, x, y, out px, out py);
                    int i = row * width + x;
                    double b, g, r;
                    current.Sample(x, y, out b, out g, out r);
                    cur[i * 3] = b; cur[i * 3 + 1] = g; cur[i * 3 + 2] = r;
                    before.Sample(px, py, out b, out g, out r);
                    prv[i * 3] = b; prv[i * 3 + 1] = g; prv[i * 3 + 2] = r;
                    inside[i] = y >= 0 && y <= height - 1 && py >= 0 && py <= height - 1 && px >= 0 && px <= width - 1;
                }
            }

            Parallel.Invoke(() => BlurBand(cur, width), () => BlurBand(prv, width));

            for (int x = 0; x < width; x++)
            {
                double sum = 0, sumColour = 0;
                int count = 0;
                for (int row = 0; row < BandRows; row++)
                {
                    int i = row * width + x;
                    if (!inside[i])
                        continue;
                    double diff = (Math.Abs(cur[i * 3] - prv[i * 3]) + Math.Abs(cur[i * 3 + 1] - prv[i * 3 + 1]) + Math.Abs(cur[i * 3 + 2] - prv[i * 3 + 2])) / 3.0;
                    sum += diff;
                    if (colour != null)
                    {
                        // The colour without the brightness (shares of blue, green, red).
                        double s1 = cur[i * 3] + cur[i * 3 + 1] + cur[i * 3 + 2] + 30, s0 = prv[i * 3] + prv[i * 3 + 1] + prv[i * 3 + 2] + 30;
                        double c = 0;
                        for (int k = 0; k < 3; k++)
                            c += Math.Abs(cur[i * 3 + k] / s1 - prv[i * 3 + k] / s0);
                        sumColour += 255.0 * c / 3.0;
                    }

                    count++;
                }

                d[x] = count > 0 ? sum / count : 0;
                if (colour != null)
                    colour[x] = count > 0 ? sumColour / count : 0;
            }
        }

        /// <summary>Gaussian blur of a band image (BandRows × w × 3) along the rows and along the columns.</summary>
        private static void BlurBand(double[] img, int w)
        {
            int r;
            double[] k = GaussKernel(BandBlur, out r);
            int taps = 2 * r + 1;
            int line = w * 3;
            double[] tmp = new double[img.Length];

            // Across the band (rows), edges replicated. Same order of the sums as the reference.
            int[] rowOffsets = new int[taps];
            for (int row = 0; row < BandRows; row++)
            {
                for (int j = 0; j < taps; j++)
                    rowOffsets[j] = Clamp(row + j - r, 0, BandRows - 1) * line;

                int dst = row * line;
                for (int i = 0; i < line; i++)
                {
                    double s = 0;
                    for (int j = 0; j < taps; j++)
                        s += k[j] * img[rowOffsets[j] + i];
                    tmp[dst + i] = s;
                }
            }

            // Along the band (columns), on a line padded with the edge values.
            double[] padded = new double[(w + 2 * r) * 3];
            for (int row = 0; row < BandRows; row++)
            {
                int src = row * line;
                for (int x = -r; x < w + r; x++)
                {
                    int xx = Clamp(x, 0, w - 1);
                    int p = (x + r) * 3;
                    padded[p] = tmp[src + xx * 3];
                    padded[p + 1] = tmp[src + xx * 3 + 1];
                    padded[p + 2] = tmp[src + xx * 3 + 2];
                }

                for (int x = 0; x < w; x++)
                {
                    int dst = src + x * 3;
                    for (int c = 0; c < 3; c++)
                    {
                        double s = 0;
                        int p = x * 3 + c;
                        for (int j = 0; j < taps; j++)
                            s += k[j] * padded[p + j * 3];
                        img[dst + c] = s;
                    }
                }
            }
        }

        /// <summary>
        /// Red parts of a rope (y = a x + b): a pixel within 3 px of the line that is red (red brightest, saturated, hue within
        /// −30°…+18°), parts with gaps up to 3 px joined, at least 3 px long. Pairs (from, to) of working x.
        /// </summary>
        public static int[] RedRuns(SwimStrokeImage image, double a, double b)
        {
            int w = image.Width, h = image.Height;
            List<int> runs = new List<int>();
            int start = -1, lastRed = -10;
            for (int x = 0; x < w; x++)
            {
                bool red = false;
                double yc = a * x + b;
                for (int dy = -3; dy <= 3 && !red; dy++)
                {
                    int y = (int)Math.Round(yc + dy);
                    if (y < 0 || y >= h)
                        continue;

                    int i = (y * w + x) * 3;
                    red = IsRed(image.Data[i], image.Data[i + 1], image.Data[i + 2]);
                }

                if (red)
                {
                    if (start < 0 || x - lastRed > 4)
                    {
                        if (start >= 0 && lastRed - start + 1 >= 3)
                        {
                            runs.Add(start);
                            runs.Add(lastRed);
                        }

                        start = x;
                    }

                    lastRed = x;
                }
            }

            if (start >= 0 && lastRed - start + 1 >= 3)
            {
                runs.Add(start);
                runs.Add(lastRed);
            }

            return runs.ToArray();
        }

        /// <summary>Red of the floats of a lane rope (as hue below 18° or above 330°, saturation above 0.43, value above 70).</summary>
        public static bool IsRed(byte b, byte g, byte r)
        {
            if (r <= 70 || r < g || r < b)
                return false;

            int min = Math.Min(g, b);
            int range = r - min;
            if (range <= 0.43 * r)
                return false;

            int gb = g - b;
            return gb <= 0.3 * range && gb >= -0.5 * range;
        }

        #region Finish
        /// <summary>
        /// Strokes between the frames first and last (indices of the frames given to the detector).
        /// </summary>
        public SwimStrokeResult Finish(int first, int last)
        {
            // The frames after PathEnd (the camera turning fast after the wall, a cut) are not part of the reading.
            int count = differences.Count;
            int T = PathEnd >= FrameGap + 3 && PathEnd < count - 1 ? PathEnd + 1 : count;

            SwimStrokeResult result = new SwimStrokeResult();
            result.Ropes = ropes;
            result.Lanes.AddRange(lanes.Take(T));
            result.RedFar.AddRange(redFar.Take(T));
            result.RedNear.AddRange(redNear.Take(T));
            result.Motion.AddRange(differences.Take(T));
            result.Shifts.AddRange(shifts.Take(T));
            result.Motions.AddRange(motions.Take(T));

            if (T < FrameGap + 3)
                return result;

            int W = width;
            double[] D = new double[T * W];
            for (int t = 0; t < T; t++)
                Array.Copy(differences[t < FrameGap ? FrameGap : t], 0, D, t * W, W);

            double[] S = new double[T * W];
            for (int t = 0; t < T; t++)
                Array.Copy(shifts[t], 0, S, t * W, W);

            // A fixed camera: the light on the water and the ropes waving move at the same places of the image all along the
            // reading, as strong as the swimmer far from the camera; taken off, the swimmer is what moves.
            if (StillQuantile > 0 && IsStill(S, T, W))
            {
                RemoveStill(D, T, W);
                if (StillLocalQuantile > 0)
                    RemoveStillLocal(D, T, W);
                result.Still = true;
                result.Motion.Clear();
                for (int t = 0; t < T; t++)
                {
                    double[] row = new double[W];
                    Array.Copy(D, t * W, row, 0, W);
                    result.Motion.Add(row);
                }
            }

            double x0 = 0.5 * (headX + feetX);
            double[] scale = null;
            if (ScaledSpeed)
            {
                // The swimmer's own speed in lane widths per frame: in pixels it follows the zoom and the perspective.
                scale = new double[T * W];
                for (int t = 0; t < T; t++)
                {
                    SwimStrokeLane l = lanes[Math.Min(t, lanes.Count - 1)];
                    for (int x = 0; x < W; x++)
                        scale[t * W + x] = Math.Max(5.0, Math.Abs(l.Y2(x) - l.Y1(x)));
                }
            }

            // The path in the colour of the band difference (tests).
            double[] P = D;
            if (PathColour > 0 && !result.Still && colourDifferences.Count >= T)
            {
                P = new double[T * W];
                for (int t = 0; t < T; t++)
                {
                    double[] c = colourDifferences[t < FrameGap ? FrameGap : t];
                    for (int x = 0; x < W; x++)
                        P[t * W + x] = PathColour == 1 ? c[x] : 0.5 * (D[t * W + x] + c[x]);
                }
            }

            int[] path = Locate(P, S, T, W, x0, length, anchorFrame, anchorX, scale, holdFrom, holdTo, holdX, glideFrames, glideX, glideSpeed);
            result.Path = path;

            int ahead = (int)Math.Round(1.6 * length);
            int behind = (int)Math.Round(0.2 * length);
            double[] s = StrokeSignal(D, T, W, path, direction, ahead, behind);
            double[] o = OnsetStrength(s, fps);
            result.Onset = o;

            first = Math.Max(0, first);
            last = Math.Min(T - 1, last);
            if (last - first < 3)
                return result;

            double lo, hi;
            CycleRange(style, out lo, out hi);
            double[] segment = new double[last - first + 1];
            Array.Copy(o, first, segment, 0, segment.Length);
            double cycle = AutocorrelationPeriod(segment, fps, lo, hi);
            if (double.IsNaN(cycle))
                cycle = 0.5 * (lo + hi);

            double period = cycle / Presses(style);
            double[] clipped = new double[o.Length];
            for (int i = 0; i < o.Length; i++)
                clipped[i] = Math.Max(o[i], -1.0);

            result.Cycle = cycle;
            result.Period = period;
            result.Strokes = TrackBeats(clipped, fps, period, first, last, 4.0, 0.5, 2.0);
            return result;
        }

        /// <summary>Mean camera displacement along the band (px a frame) below which the camera is fixed (tests).</summary>
        internal static double StillShift = 0.25;

        /// <summary>
        /// Part of the frames of a fixed camera whose band difference at a place is the motion that stays there (light, ropes),
        /// taken off every frame (tests; 0: off).
        /// </summary>
        internal static double StillQuantile = 0.5;

        /// <summary>
        /// The water the swimmers went through stays rough for a few seconds after them (the light on it moves): the
        /// StillLocalQuantile of each place over the StillLocalSeconds around each frame is taken off too (tests; 0: off).
        /// </summary>
        internal static double StillLocalQuantile = 0.7;
        internal static double StillLocalSeconds = 2.0;

        /// <summary>The camera did not move during the frames (mean displacement along the band below StillShift).</summary>
        private static bool IsStill(double[] S, int T, int W)
        {
            if (T < 25)
                return false;

            double sum = 0;
            long n = 0;
            for (int t = 1; t < T; t++)
            {
                for (int x = 0; x < W; x += 4)
                {
                    sum += Math.Abs(S[t * W + x]);
                    n++;
                }
            }

            return n > 0 && sum / n < StillShift;
        }

        /// <summary>The StillQuantile of each place of the band over the frames taken off the band difference there.</summary>
        private static void RemoveStill(double[] D, int T, int W)
        {
            int from = Math.Min(FrameGap, T - 1);
            int n = T - from;
            int k = Clamp((int)Math.Round(StillQuantile * (n - 1)), 0, n - 1);
            double[] column = new double[n];
            for (int x = 0; x < W; x++)
            {
                for (int t = from; t < T; t++)
                    column[t - from] = D[t * W + x];
                Array.Sort(column);
                double b = column[k];
                for (int t = 0; t < T; t++)
                    D[t * W + x] = Math.Max(0, D[t * W + x] - b);
            }
        }

        /// <summary>The StillLocalQuantile of each place over the frames around (every 0.2 s, linear between) taken off.</summary>
        private void RemoveStillLocal(double[] D, int T, int W)
        {
            int K = Math.Max(5, (int)Math.Round(StillLocalSeconds * fps));
            int step = Math.Max(1, (int)Math.Round(0.2 * fps));
            List<int> centres = new List<int>();
            for (int c = 0; c < T; c += step)
                centres.Add(c);
            if (centres[centres.Count - 1] != T - 1)
                centres.Add(T - 1);

            int m = centres.Count;
            double[] B = new double[m * W];
            double[] buffer = new double[2 * K + 1];
            for (int i = 0; i < m; i++)
            {
                int a = Math.Max(0, centres[i] - K), b = Math.Min(T - 1, centres[i] + K);
                int n = b - a + 1;
                int k = Clamp((int)Math.Round(StillLocalQuantile * (n - 1)), 0, n - 1);
                for (int x = 0; x < W; x++)
                {
                    for (int t = a; t <= b; t++)
                        buffer[t - a] = D[t * W + x];
                    Array.Sort(buffer, 0, n);
                    B[i * W + x] = buffer[k];
                }
            }

            int seg = 0;
            for (int t = 0; t < T; t++)
            {
                while (seg < m - 2 && t > centres[seg + 1])
                    seg++;
                int c0 = centres[seg], c1 = centres[Math.Min(m - 1, seg + 1)];
                double f = c1 > c0 ? Math.Max(0, Math.Min(1, (t - c0) / (double)(c1 - c0))) : 0;
                int i0 = seg * W, i1 = Math.Min(m - 1, seg + 1) * W;
                for (int x = 0; x < W; x++)
                {
                    double bg = (1 - f) * B[i0 + x] + f * B[i1 + x];
                    D[t * W + x] = Math.Max(0, D[t * W + x] - bg);
                }
            }
        }

        /// <summary>
        /// Place of the swimmer along the band in each frame. length: the swimmer (head to feet, px); anchorFrame, anchorX: where he
        /// is known to be at one frame (−1: nowhere).
        /// </summary>
        private static int[] Locate(double[] D, double[] shifts, int T, int W, double x0, double length, int anchorFrame, double anchorX, double[] scale = null,
            int holdFrom = -1, int holdTo = -1, double holdX = 0, int glideFrames = 0, double glideX = 0, double glideSpeed = 0)
        {
            double sigmaX = Math.Max(12.0, 0.3 * length);
            double anchorSigma = Math.Max(12.0, 0.25 * length);
            double[] tmp = GaussAlong(D, T, W, sigmaX, false);
            double[] smooth = GaussAlong(tmp, T, W, 12.0, true);

            // Each frame to z-scores.
            double[] Z = new double[T * W];
            for (int t = 0; t < T; t++)
            {
                double mean = 0;
                for (int x = 0; x < W; x++)
                    mean += smooth[t * W + x];
                mean /= W;
                double variance = 0;
                for (int x = 0; x < W; x++)
                {
                    double e = smooth[t * W + x] - mean;
                    variance += e * e;
                }

                double std = Math.Sqrt(variance / W) + 1e-6;
                for (int x = 0; x < W; x++)
                    Z[t * W + x] = (smooth[t * W + x] - mean) / std;
            }

            // The place known at one frame: far from it the path pays as much as a few frames of the strongest motion.
            if (anchorFrame >= 0 && anchorFrame < T)
            {
                for (int k = Math.Max(0, anchorFrame - AnchorWidth); k <= Math.Min(T - 1, anchorFrame + AnchorWidth); k++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        double z = (x - anchorX) / anchorSigma;
                        Z[k * W + x] -= AnchorWeight * z * z;
                    }
                }
            }

            // At the wall of a turn: the place held from frame to frame with the camera.
            if (holdFrom >= 0 && holdTo > holdFrom)
            {
                double xh = holdX;
                for (int k = Math.Max(0, holdFrom); k <= Math.Min(T - 1, holdTo); k++)
                {
                    if (k > holdFrom)
                    {
                        int xi = Clamp((int)Math.Round(xh), 0, W - 1);
                        xh += shifts[k * W + xi];
                    }

                    for (int x = 0; x < W; x++)
                    {
                        double z = (x - xh) / anchorSigma;
                        Z[k * W + x] -= z * z;
                    }
                }
            }

            // Under the water after a turn: near where he goes from the wall at his speed, less sure frame after frame.
            if (glideFrames > 0 && GlideShare > 0)
            {
                double xg = glideX;
                for (int k = 0; k <= Math.Min(T - 1, glideFrames); k++)
                {
                    if (k > 0)
                    {
                        int xi = Clamp((int)Math.Round(xg), 0, W - 1);
                        xg += shifts[k * W + xi] + glideSpeed;
                    }

                    double sigma = anchorSigma + GlideSpread * Math.Abs(glideSpeed) * k;
                    for (int x = 0; x < W; x++)
                    {
                        double z = (x - xg) / sigma;
                        Z[k * W + x] -= z * z;
                    }
                }
            }

            int[] path = TwoPasses(Z, shifts, T, W, x0, scale);

            // The path does not go where the swimmer is known to be (glints of the water stronger than the swimmer took it away):
            // which moving thing is the swimmer from the motion clipped (glints as strong as the swimmer, the path keeps to the
            // swimmer it goes through), then where he is from the full motion near that path.
            bool missed = anchorFrame >= 0 && anchorFrame < T && Math.Abs(path[anchorFrame] - anchorX) > 0.5 * length;
            if (!missed || double.IsInfinity(ZClip))
                return path;

            double[] clipped = new double[Z.Length];
            for (int i = 0; i < Z.Length; i++)
                clipped[i] = Math.Min(Z[i], ZClip);
            int[] robust = TwoPasses(clipped, shifts, T, W, x0, scale);
            double half = Math.Max(10.0, Corridor * length);
            double[] near = new double[Z.Length];
            for (int t = 0; t < T; t++)
            {
                for (int x = 0; x < W; x++)
                    near[t * W + x] = Math.Abs(x - robust[t]) <= half ? Z[t * W + x] : Z[t * W + x] - 1e6;
            }

            return TwoPasses(near, shifts, T, W, x0, scale);
        }

        /// <summary>
        /// Path with no speed first, then with the swimmer's own speed relative to the water (px/frame), tighter. scale (optional,
        /// T × W): the lane width at each place, the speed then in lane widths per frame (the same speed at another zoom).
        /// </summary>
        private static int[] TwoPasses(double[] Z, double[] shifts, int T, int W, double x0, double[] scale = null)
        {
            int[] p = Viterbi(Z, shifts, T, W, x0, 0.0, 8);
            List<double> rel = new List<double>();
            for (int t = 1; t < T; t++)
            {
                double step = p[t] - p[t - 1] - shifts[t * W + p[t - 1]];
                rel.Add(scale != null ? step / scale[t * W + p[t]] : step);
            }

            double v = rel.Count > 0 ? Median(rel) : 0.0;
            if (scale == null)
                return Viterbi(Z, shifts, T, W, x0, v, 5, SecondLambda);

            double[] field = new double[T * W];
            for (int i = 0; i < field.Length; i++)
                field[i] = v * scale[i];
            return Viterbi(Z, shifts, T, W, x0, 0.0, 5, SecondLambda, field);
        }

        /// <summary>The second pass of the path with the swimmer's speed in lane widths per frame (tests).</summary>
        internal static bool ScaledSpeed = false;

        /// <summary>
        /// The band difference the path follows: 0 the brightness, 1 the colour without the brightness (the glints of the lights
        /// and the floor lines seen through the waves change the brightness, the swimmer's skin and cap the colour), 2 the mean
        /// of both (tests; the strokes, the breakouts and the walls keep the brightness).
        /// </summary>
        internal static int PathColour = 0;

        /// <summary>Frames on each side of the click where the place clicked holds too, and the weight of it (tests).</summary>
        internal static int AnchorWidth = 0;
        internal static double AnchorWeight = 1.0;

        /// <summary>Half width of the band around the path of the clipped motion (swimmer lengths, tests).</summary>
        internal static double Corridor = 0.6;

        /// <summary>Blocks of columns of the lane search: a line counts by its weaker blocks (a rope crosses them all, a swimmer, his splashes or a short line some of them; tests).</summary>
        internal static int LaneBlocks = 10;

        /// <summary>The strongest blocks of LaneBlocks left out (tests).</summary>
        internal static int LaneDrop = 4;

        /// <summary>
        /// The ropes of floats found again by their texture in a lane narrower than TextureMaxLane (px): rows with a texture of
        /// at least TextureZMin (units of its usual level) count as lines, as strong as the strongest line from TextureZRef on
        /// (TextureWeight; 0: off). Texture of the columns cx ± TextureHalfWidth, slopes ± TextureSlopeRange around the lines (tests).
        /// </summary>
        internal static double TextureWeight = 1.0;
        internal static double TextureZMin = 4.0;
        internal static double TextureZRef = 15.0;
        internal static int TextureHalfWidth = 100;
        internal static double TextureSlopeRange = 0.04;
        internal static double TextureMaxLane = 120;

        /// <summary>The lane found with the texture is kept when inside the first one for at least TextureOverlap of its width and
        /// at most TextureSplit times as wide (a part of it), or at most TextureSame times as wide with a rope of floats hardly
        /// seen by its colour (tests).</summary>
        internal static double TextureOverlap = 0.7;
        internal static double TextureSplit = 0.8;
        internal static double TextureSame = 1.05;

        /// <summary>A rope of that lane is followed by its texture when its texture is at least TextureRopeZ and its line of colour
        /// weaker than TextureRopeMagnitude of the strongest line (0: never; tests).</summary>
        internal static double TextureRopeZ = 8.0;
        internal static double TextureRopeMagnitude = 0.25;

        /// <summary>A rope taken for the wall of an outer lane moves to a rope of floats (texture at least TextureRopeZ) at most this
        /// part of the width of the lane from it and at least 1 - this from the other rope (0: never; tests).</summary>
        internal static double TextureVirtual = 0.35;

        /// <summary>A Detail frame counts when at least this many times as wide as the working size (0: never; tests).</summary>
        internal static double DetailMin = 1.5;

        /// <summary>The Detail frame is used for a lane found narrower than this (working px; tests).</summary>
        internal static double DetailMaxLane = 60;

        /// <summary>Smallest ratio of a gap of a rope chain to the gap before it: the lanes get wider towards the camera (tests).</summary>
        internal static double GapMin = 0.9;

        /// <summary>The best chain holding the swimmer between two of its ropes first (tests).</summary>
        internal static bool PreferHolding = true;

        /// <summary>Strengths of the rope candidates capped at this many times their median: the edges of the deck, of the boards (tests; 0: off).</summary>
        internal static double StrengthCap = 2.0;

        /// <summary>Smallest gap of a rope chain (swimmer lengths; tests).</summary>
        internal static double GapLength = 0.2;

        /// <summary>Two ropes holding the swimmer are his lane (a zoomed view: no other rope in the window; tests).</summary>
        internal static bool TwoRopes = true;

        /// <summary>Rope candidates weaker than this part of the LaneReference-th strongest line left out: the waves (tests).</summary>
        internal static double MinStrength = 0.25;

        /// <summary>
        /// The line the weak candidates are measured against: the third strongest (one structure much stronger than the ropes, the
        /// edge of the deck or a stand in front, left the ropes out; 0: the strongest, before 2026-10; tests).
        /// </summary>
        internal static int LaneReference = 3;

        /// <summary>
        /// A lane of a rope chain loses this part of the strongest line inside it (not a line of the floor, not next to its ropes,
        /// at least GapPenaltyMin): a chain of every other rope, one hidden by the splashes, holds such lines (tests; 0: off).
        /// </summary>
        internal static double GapPenalty = 0.5;
        internal static double GapPenaltyMin = 0.5;

        /// <summary>The lane searched again without the lines of the floor only when it fits the old one (dark ropes; tests).</summary>
        internal static bool FloorCheck = true;

        /// <summary>
        /// What the lane holding the swimmer gains in a rope chain when he is in its middle (less towards its ropes, nothing on
        /// them): a chain of other lines can hold him at its edge (a line of the floor, a shade; tests; 0: off).
        /// </summary>
        internal static double LaneCenter = 0.2;

        /// <summary>A cycle at half the lag found is the cycle when its autocorrelation is this part of the other one (tests; 0: off).</summary>
        internal static double OctaveRatio = 0.4;

        /// <summary>Shortest cycle taken by OctaveRatio (s; tests).</summary>
        internal static double OctaveMin = 0.75;

        /// <summary>A rope candidate as strong just inside a rope of the lane (nearer than this part of the lane) is the rope (tests; 0: off).</summary>
        internal static double InnerRope = 0.15;

        /// <summary>Largest evidence of the path in one frame (z-score) when the swimmer is searched again (tests: infinity, never).</summary>
        internal static double ZClip = 1.0;

        /// <summary>
        /// Penalty of the second pass of the path on the steps away from the swimmer's own speed: strong enough that the path does
        /// not leave a swimmer for the still water (glints of the sun, the wake behind him, the turbulence of a turn), which would
        /// mean he suddenly stopped, weak enough that it follows his speed changing (test races, checkpoints more than 1 s off of
        /// 217: 26 with 0.03, 14 with 0.1, 5 with 0.15, 10 with 0.2, 19 with 0.3).
        /// </summary>
        internal static double SecondLambda = 0.15;

        /// <summary>Largest step back against the water of the path with the swimmer's speed (px a frame; negative: any, tests).</summary>
        internal static double Backward = -1;

        /// <summary>
        /// Best path of the swimmer along the band: maximises the sum of E minus a penalty on the steps, where a step is
        /// measured after moving the previous position with the camera (shifts) and the swimmer's own speed v.
        /// </summary>
        private static int[] Viterbi(double[] E, double[] shifts, int T, int W, double x0, double v, int maxStep, double lambda = 0.03, double[] speed = null)
        {
            const double startSigma = 40.0;
            const double negative = -1e18;

            double[] score = new double[W];
            for (int x = 0; x < W; x++)
            {
                double z = (x - x0) / startSigma;
                score[x] = E[x] - z * z;
            }

            int[] back = new int[T * W];
            double[] dest = new double[W];
            double[] sortedDest = new double[W];
            double[] sortedScore = new double[W];
            double[] sortedOrigin = new double[W];
            int[] order = new int[W];
            double[] moved = new double[W];
            double[] origin = new double[W];
            double[] best = new double[W];
            int[] arg = new int[W];

            for (int t = 1; t < T; t++)
            {
                for (int x = 0; x < W; x++)
                {
                    dest[x] = x + shifts[t * W + x] + v + (speed != null ? speed[t * W + x] : 0.0);
                    order[x] = x;
                }

                double[] keys = (double[])dest.Clone();
                Array.Sort(keys, order);
                for (int i = 0; i < W; i++)
                {
                    sortedDest[i] = keys[i];
                    sortedScore[i] = score[order[i]];
                    sortedOrigin[i] = order[i];
                }

                Interpolate(sortedDest, sortedScore, negative, negative, moved);
                Interpolate(sortedDest, sortedOrigin, 0, W - 1, origin);

                for (int x = 0; x < W; x++)
                {
                    best[x] = negative;
                    arg[x] = 0;
                }

                // With the swimmer's speed: no step back against the water (more than Backward px a frame); the swimmer never
                // swims backwards, the wake behind him and the still water do (with the camera following him).
                int stFrom = -maxStep, stTo = maxStep;
                if (Backward >= 0 && v > 0)
                    stFrom = Math.Max(stFrom, (int)Math.Ceiling(-v - Backward));
                else if (Backward >= 0 && v < 0)
                    stTo = Math.Min(stTo, (int)Math.Floor(-v + Backward));

                for (int st = stFrom; st <= stTo; st++)
                {
                    double penalty = lambda * st * st;
                    for (int x = 0; x < W; x++)
                    {
                        int from = x - st;
                        if (from < 0 || from >= W)
                            continue;

                        double candidate = moved[from] - penalty;
                        if (candidate > best[x])
                        {
                            best[x] = candidate;
                            arg[x] = Clamp((int)Math.Floor(origin[from] + 0.5), 0, W - 1);
                        }
                    }
                }

                for (int x = 0; x < W; x++)
                {
                    score[x] = best[x] + E[t * W + x];
                    back[t * W + x] = arg[x];
                }
            }

            int[] path = new int[T];
            int end = 0;
            for (int x = 1; x < W; x++)
            {
                if (score[x] > score[end])
                    end = x;
            }

            path[T - 1] = end;
            for (int t = T - 1; t > 0; t--)
                path[t - 1] = back[t * W + path[t]];

            return path;
        }

        /// <summary>Linear interpolation of (xp, fp) at x = 0, 1, …, out.Length − 1 (xp increasing), like numpy.interp.</summary>
        private static void Interpolate(double[] xp, double[] fp, double left, double right, double[] output)
        {
            int n = xp.Length;
            int j = 0;
            for (int x = 0; x < output.Length; x++)
            {
                if (x < xp[0])
                {
                    output[x] = left;
                    continue;
                }

                if (x > xp[n - 1])
                {
                    output[x] = right;
                    continue;
                }

                while (j < n - 2 && xp[j + 1] <= x)
                    j++;

                double x0 = xp[j], x1 = xp[j + 1];
                if (x >= x1)
                {
                    output[x] = fp[j + 1];
                }
                else if (x1 > x0)
                {
                    double f = (x - x0) / (x1 - x0);
                    output[x] = fp[j] + f * (fp[j + 1] - fp[j]);
                }
                else
                {
                    output[x] = fp[j];
                }
            }
        }

        private static double[] StrokeSignal(double[] D, int T, int W, int[] path, int direction, int ahead, int behind)
        {
            double[] s = new double[T];
            for (int t = 0; t < T; t++)
            {
                int a, b;
                if (direction >= 0)
                {
                    a = path[t] - behind;
                    b = path[t] + ahead;
                }
                else
                {
                    a = path[t] - ahead;
                    b = path[t] + behind;
                }

                a = Math.Max(0, a);
                b = Math.Min(W, b);
                if (b <= a)
                    continue;

                double sum = 0;
                for (int x = a; x < b; x++)
                    sum += D[t * W + x];
                s[t] = sum / (b - a);
            }

            return s;
        }

        private static double[] OnsetStrength(double[] s, double fps)
        {
            double[] x = Gauss1D(s, 2.0);
            int n = Math.Max(3, ((int)(3.0 * fps)) | 1);
            double[] med = MedianFilter(x, n);
            double[] absDev = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
                absDev[i] = Math.Abs(x[i] - med[i]);

            double[] dev = MedianFilter(absDev, n);
            List<double> positive = new List<double>();
            for (int i = 0; i < dev.Length; i++)
            {
                dev[i] *= 1.4826;
                if (dev[i] > 0)
                    positive.Add(dev[i]);
            }

            double floor = positive.Count > 0 ? 0.5 * Median(positive) : 1.0;
            double[] o = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                double value = (x[i] - med[i]) / Math.Max(dev[i], floor);
                o[i] = Math.Max(-3.0, Math.Min(5.0, value));
            }

            return o;
        }

        /// <summary>Stroke cycle (s) from the autocorrelation of the signal, NaN if none in the range.</summary>
        private static double AutocorrelationPeriod(double[] o, double fps, double lo, double hi)
        {
            int n = o.Length;
            double mean = 0;
            for (int i = 0; i < n; i++)
                mean += o[i];
            mean /= n;

            int a = (int)(lo * fps);
            int b = Math.Min(n - 2, (int)(hi * fps));
            if (b <= a + 1)
                return double.NaN;

            double[] ac = new double[b + 2];
            for (int k = 0; k < b + 2; k++)
            {
                double sum = 0;
                for (int i = 0; i + k < n; i++)
                    sum += (o[i] - mean) * (o[i + k] - mean);
                ac[k] = sum / (n - k);
            }

            double zero = ac[0] + 1e-9;
            for (int k = 0; k < ac.Length; k++)
                ac[k] /= zero;

            int count = b - a + 1;
            List<int> candidates = new List<int>();
            for (int i = 1; i < count - 1; i++)
            {
                double y = ac[a + i];
                if (y >= ac[a + i - 1] && y >= ac[a + i + 1])
                    candidates.Add(i);
            }

            if (candidates.Count == 0)
                return double.NaN;

            if (LaneTrace != null)
                LaneTrace("cycle: " + string.Join(" ", candidates.Select(i => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.00}s={1:0.00}", (a + i) / fps, ac[a + i]))));

            double top = double.MinValue;
            foreach (int i in candidates)
                top = Math.Max(top, ac[a + i]);

            // The shortest lag close to the best peak (not twice the period).
            int chosen = -1;
            foreach (int i in candidates)
            {
                if (ac[a + i] >= top - 0.15 * Math.Abs(top))
                {
                    chosen = i;
                    break;
                }
            }

            if (chosen < 0)
                return double.NaN;

            // A peak at half the lag chosen, clearly positive: the cycle is that one, the longer lag repeats every other cycle (a
            // breath every two cycles, one arm stronger than the other).
            if (OctaveRatio > 0)
            {
                double lag = a + chosen;
                foreach (int i in candidates)
                {
                    double half = a + i;
                    if (half / fps >= OctaveMin && Math.Abs(half - 0.5 * lag) <= 0.12 * 0.5 * lag && ac[a + i] >= OctaveRatio * ac[a + chosen] && ac[a + i] > 0.1)
                    {
                        chosen = i;
                        break;
                    }
                }
            }

            double y0 = ac[a + chosen - 1], y1 = ac[a + chosen], y2 = ac[a + chosen + 1];
            double den = y0 - 2 * y1 + y2;
            double d = Math.Abs(den) > 1e-12 ? 0.5 * (y0 - y2) / den : 0.0;
            return (a + chosen + d) / fps;
        }

        /// <summary>
        /// Beat tracking (dynamic programming): the strokes maximise the sum of the signal at the strokes minus
        /// alpha · log²(interval / period) for each interval.
        /// </summary>
        private static List<int> TrackBeats(double[] o, double fps, double period, int first, int last, double alpha, double minRatio, double maxRatio)
        {
            double tau = period * fps;
            int lo = Math.Max(1, (int)Math.Round(minRatio * tau));
            int hi = (int)Math.Round(maxRatio * tau);
            int n = o.Length;
            double[] score = new double[n];
            int[] back = new int[n];
            for (int t = 0; t < n; t++)
                score[t] = double.NegativeInfinity;

            // The first stroke comes within 1.5 periods of the start: later strokes need a previous one.
            int startWindow = first + (int)Math.Round(1.5 * tau);
            for (int t = first; t <= last; t++)
            {
                double best = t <= startWindow ? 0.0 : double.NegativeInfinity;
                int arg = -1;
                for (int d = lo; d <= hi; d++)
                {
                    int p = t - d;
                    if (p < first)
                        break;
                    if (double.IsNegativeInfinity(score[p]))
                        continue;

                    double l = Math.Log(d / tau);
                    double v = score[p] - alpha * l * l;
                    if (v > best)
                    {
                        best = v;
                        arg = p;
                    }
                }

                score[t] = o[t] + best;
                back[t] = arg;
            }

            int tailStart = Math.Max(first, last - (int)Math.Round(1.2 * tau));
            int end = tailStart;
            for (int t = tailStart + 1; t <= last; t++)
            {
                if (score[t] > score[end])
                    end = t;
            }

            List<int> beats = new List<int>();
            int current = end;
            while (current >= 0)
            {
                beats.Add(current);
                current = back[current];
            }

            beats.Reverse();
            return beats;
        }
        #endregion

        #region Lane
        /// <summary>Mean colour along the lines of slope a through (cx, y0) for each y0, over the columns xs.</summary>
        private static double[] LineProfile(SwimStrokeImage img, double cx, double a, double[] y0s, double[] xs)
        {
            double[] profile = new double[y0s.Length * 3];
            for (int i = 0; i < y0s.Length; i++)
            {
                double sb = 0, sg = 0, sr = 0;
                foreach (double x in xs)
                {
                    double b, g, r;
                    img.Sample(x, y0s[i] + a * (x - cx), out b, out g, out r);
                    sb += b; sg += g; sr += r;
                }

                profile[i * 3] = sb / xs.Length;
                profile[i * 3 + 1] = sg / xs.Length;
                profile[i * 3 + 2] = sr / xs.Length;
            }

            return profile;
        }

        /// <summary>Profile minus its Gaussian smoothing (per channel), then the magnitude of the three channels.</summary>
        private static double[] HighPassMagnitude(double[] profile, int n, out double energy)
        {
            double[] magnitude = new double[n];
            energy = 0;
            double[][] channels = new double[3][];
            for (int c = 0; c < 3; c++)
            {
                double[] ch = new double[n];
                for (int i = 0; i < n; i++)
                    ch[i] = profile[i * 3 + c];
                double[] smooth = Gauss1D(ch, RopeSigma);
                for (int i = 0; i < n; i++)
                    ch[i] -= smooth[i];
                channels[c] = ch;
            }

            for (int i = 0; i < n; i++)
            {
                double e = channels[0][i] * channels[0][i] + channels[1][i] * channels[1][i] + channels[2][i] * channels[2][i];
                energy += e;
                magnitude[i] = Math.Sqrt(e);
            }

            energy /= n;
            return magnitude;
        }

        /// <summary>
        /// Magnitude of the lines over blocks of the columns: per line, the mean of the weaker blocks (a rope crosses all of them,
        /// a swimmer, his splashes or a short line only some). profile: the mean colours over all the columns.
        /// </summary>
        private static double[] BlockMagnitude(SwimStrokeImage img, double cx, double a, double[] y0s, double[] xs, int n, out double[] profile, out double energy)
        {
            int blocks = Math.Min(LaneBlocks, Math.Max(1, xs.Length / 10));
            int drop = Math.Min(LaneDrop, blocks - 1);
            double[][] mags = new double[blocks][];
            profile = new double[n * 3];
            for (int b = 0; b < blocks; b++)
            {
                int i0 = b * xs.Length / blocks, i1 = (b + 1) * xs.Length / blocks;
                double[] part = new double[i1 - i0];
                Array.Copy(xs, i0, part, 0, part.Length);
                double[] p = LineProfile(img, cx, a, y0s, part);
                double e;
                mags[b] = HighPassMagnitude(p, n, out e);
                double w = (double)part.Length / xs.Length;
                for (int i = 0; i < p.Length; i++)
                    profile[i] += w * p[i];
            }

            double[] magnitude = new double[n];
            double[] values = new double[blocks];
            energy = 0;
            for (int i = 0; i < n; i++)
            {
                for (int b = 0; b < blocks; b++)
                    values[b] = mags[b][i];
                Array.Sort(values);
                double sum = 0;
                for (int b = 0; b < blocks - drop; b++)
                    sum += values[b];
                magnitude[i] = sum / (blocks - drop);
                energy += magnitude[i] * magnitude[i];
            }

            energy /= n;
            return magnitude;
        }

        /// <summary>
        /// Ropes of the swimmer's lane on the first frame. Returns false if they can't be found (no ropes in view).
        /// </summary>
        public static bool FindLane(SwimStrokeImage img, double headX, double headY, double feetX, double feetY, out SwimStrokeLane lane)
        {
            int virtualRope;
            return FindLane(img, headX, headY, feetX, feetY, out lane, out virtualRope);
        }

        /// <summary>
        /// The lane around the swimmer. virtualRope: 0 when both ropes were seen, 1 or 2 when that rope is the place of the
        /// wall side of an outer lane (one rope only: the next rope of the regular chain, the wall has no rope).
        /// </summary>
        public static bool FindLane(SwimStrokeImage img, double headX, double headY, double feetX, double feetY, out SwimStrokeLane lane, out int virtualRope)
        {
            return FindLane(img, headX, headY, feetX, feetY, out lane, out virtualRope, null);
        }

        /// <summary>The lane around the swimmer, with the frame at its own size (null: none) for the texture of the ropes.</summary>
        public static bool FindLane(SwimStrokeImage img, double headX, double headY, double feetX, double feetY, out SwimStrokeLane lane, out int virtualRope, SwimStrokeImage detail)
        {
            // Around the swimmer (2.2 lengths), then closer to him (1 length) when stronger lines elsewhere (the wall of the pool
            // next to an outer lane, boards) hide the ropes.
            if (detail != null && (DetailMin <= 0 || detail.Width < DetailMin * img.Width))
                detail = null;
            if (FindLane(img, headX, headY, feetX, feetY, 2.2, out lane, out virtualRope, detail))
                return true;
            return FindLane(img, headX, headY, feetX, feetY, 1.0, out lane, out virtualRope, detail);
        }

        private static bool FindLane(SwimStrokeImage img, double headX, double headY, double feetX, double feetY, double reach, out SwimStrokeLane lane, out int virtualRope,
            SwimStrokeImage detail)
        {
            lane = new SwimStrokeLane();
            virtualRope = 0;
            double L = Math.Sqrt((headX - feetX) * (headX - feetX) + (headY - feetY) * (headY - feetY));
            double cx = 0.5 * (headX + feetX);
            double cy = 0.5 * (headY + feetY);
            double halfH = Math.Max(reach < 2 ? 80 : 100, Math.Min(0.5 * img.Height, reach * L));

            List<double> columns = new List<double>();
            for (int x = Math.Max(0, (int)(cx - 250)); x < Math.Min(img.Width, (int)(cx + 250)); x += 2)
                columns.Add(x);
            double[] xs = columns.ToArray();
            if (xs.Length == 0)
                return false;

            int n = (int)Math.Ceiling(2 * halfH + 0.5);
            double[] y0s = new double[n];
            for (int i = 0; i < n; i++)
                y0s[i] = cy - halfH + i;

            double[] energies = new double[161];
            double[][] magnitudes = new double[161][];
            double[][] profiles = new double[161][];
            for (int k = 0; k < 161; k++)
            {
                double a = -0.40 + 0.005 * k;
                if (LaneBlocks > 1)
                {
                    magnitudes[k] = BlockMagnitude(img, cx, a, y0s, xs, n, out profiles[k], out energies[k]);
                    continue;
                }

                profiles[k] = LineProfile(img, cx, a, y0s, xs);
                magnitudes[k] = HighPassMagnitude(profiles[k], n, out energies[k]);
            }

            // The strongest direction first; then the other strong directions (local maxima): next to an outer lane the wall of
            // the pool and its boards can be stronger than the ropes.
            List<int> order = new List<int>();
            for (int k = 0; k < 161; k++)
            {
                bool peak = (k == 0 || energies[k] >= energies[k - 1]) && (k == 160 || energies[k] >= energies[k + 1]);
                if (peak)
                    order.Add(k);
            }

            order.Sort((p, q) => energies[q].CompareTo(energies[p]));
            for (int i = 0; i < Math.Min(4, order.Count); i++)
            {
                if (i > 0 && energies[order[i]] < 0.05 * energies[order[0]])
                    break;
                if (LaneAtSlope(-0.40 + 0.005 * order[i], magnitudes[order[i]], profiles[order[i]], y0s, n, headX, headY, feetX, feetY, L, cx, out lane, out virtualRope))
                {
                    TextureLane(img, detail, cx, -0.40 + 0.005 * order[i], magnitudes[order[i]], profiles[order[i]], y0s, n, headX, headY, feetX, feetY, L, ref lane, ref virtualRope);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Ropes of floats too faint for the lines of colour (blue and white floats on blue water, the far lanes of a view from the
        /// stands): the lane again with the texture of the lines, kept when it is the lane found or a part of it (one rope or more
        /// missed in it, a rope taken at its shade). Its ropes that are hardly lines of colour are followed by their texture.
        /// </summary>
        private static void TextureLane(SwimStrokeImage img, SwimStrokeImage detail, double cx, double slope, double[] magnitude, double[] profile, double[] y0s, int n,
            double headX, double headY, double feetX, double feetY, double L, ref SwimStrokeLane lane, ref int virtualRope)
        {
            double wA = lane.Width(cx);
            if (TextureWeight <= 0 || wA >= TextureMaxLane)
                return;

            double[] texture = TextureProfile(img, 1.0, cx, slope, y0s, n);
            if (texture == null)
                return;

            double[] m = Gauss1D(magnitude, 1.0);
            double max = Math.Max(1e-9, m.Max());
            bool found = TextureLaneB(texture, texture, false, cx, slope, magnitude, profile, y0s, n, headX, headY, feetX, feetY, L, m, max, wA, ref lane, ref virtualRope);

            // Not found: with the texture of the frame at its own size, around a narrow lane (the floats of the ropes of far lanes,
            // a pixel at the working size), for ropes missed in the lane only (in a close view the floats are seen at the working
            // size and the tiles of the floor are lines of texture at its own size). Its ropes are followed by their texture only
            // when it is seen at the working size too.
            double[] search = texture;
            if (!found && detail != null && wA < DetailMaxLane)
            {
                int r0 = Math.Max(0, (int)Math.Floor(Math.Min(lane.Y1(cx), lane.Y2(cx)) - wA - y0s[0]));
                int r1 = Math.Min(n - 1, (int)Math.Ceiling(Math.Max(lane.Y1(cx), lane.Y2(cx)) + wA - y0s[0]));
                double[] fine = TextureProfile(detail, (double)detail.Width / img.Width, cx, slope, y0s, n, r0, r1);
                if (fine != null)
                {
                    search = fine;
                    TextureLaneB(fine, texture, true, cx, slope, magnitude, profile, y0s, n, headX, headY, feetX, feetY, L, m, max, wA, ref lane, ref virtualRope);
                }
            }

            // The rope taken for the wall of an outer lane (at the gap of the ropes next to it) where a faint rope of floats is:
            // that rope (a far lane next to a rope too faint for the lines of colour).
            if (TextureVirtual > 0 && virtualRope != 0)
            {
                double yv = virtualRope == 1 ? lane.Y1(cx) : lane.Y2(cx), yr = virtualRope == 1 ? lane.Y2(cx) : lane.Y1(cx);
                double w = Math.Abs(yr - yv);
                int best = -1;
                for (int r = 1; r < n - 1; r++)
                {
                    double y = y0s[r];
                    if (Math.Abs(y - yv) > TextureVirtual * w || Math.Abs(y - yr) < (1 - TextureVirtual) * w || (y - yr) * (yv - yr) <= 0)
                        continue;
                    if (search[r] < TextureRopeZ || search[r] < search[r - 1] || search[r] < search[r + 1])
                        continue;
                    if (best < 0 || search[r] > search[best])
                        best = r;
                }

                if (best >= 0)
                {
                    if (LaneTrace != null)
                        LaneTrace(string.Format(System.Globalization.CultureInfo.InvariantCulture, "lane: virtual rope {0:0} -> texture {1:0}", yv, y0s[best]));
                    int rope = virtualRope;
                    if (rope == 1)
                        lane.B1 = y0s[best] - lane.A1 * cx;
                    else
                        lane.B2 = y0s[best] - lane.A2 * cx;
                    virtualRope = 0;
                    lane.TextureRopes |= TextureRopeBits(lane, 3 - rope, cx, texture, m, max, y0s, n) & rope;
                }
            }
        }

        /// <summary>
        /// The lane with the texture 'search' (rows of floats), kept when it is a part of the lane found (ropes missed in it) or, but
        /// for 'splitOnly', the same lane on ropes hardly seen by their colour (the lines of colour found were their shade).
        /// </summary>
        private static bool TextureLaneB(double[] search, double[] texture, bool splitOnly, double cx, double slope, double[] magnitude, double[] profile,
            double[] y0s, int n, double headX, double headY, double feetX, double feetY, double L, double[] m, double max, double wA,
            ref SwimStrokeLane lane, ref int virtualRope)
        {
            SwimStrokeLane laneB;
            int virtualB;
            if (!LaneAtSlope(slope, magnitude, profile, y0s, n, headX, headY, feetX, feetY, L, cx, out laneB, out virtualB, search))
                return false;

            int ropes = TextureRopeBits(laneB, virtualB, cx, texture, m, max, y0s, n);
            double wB = laneB.Width(cx);
            double overlap = Math.Min(lane.Y2(cx), laneB.Y2(cx)) - Math.Max(lane.Y1(cx), laneB.Y1(cx));
            if (overlap < TextureOverlap * wB || (wB > TextureSplit * wA && (splitOnly || ropes == 0 || wB > TextureSame * wA)))
                return false;

            if (LaneTrace != null)
                LaneTrace(string.Format(System.Globalization.CultureInfo.InvariantCulture, "lane: texture{5} {0:0}-{1:0} in {2:0}-{3:0} ropes {4}", laneB.Y1(cx), laneB.Y2(cx),
                    lane.Y1(cx), lane.Y2(cx), ropes, splitOnly ? " (own size)" : ""));
            lane = laneB;
            virtualRope = virtualB;
            lane.TextureRopes = ropes;
            return true;
        }

        /// <summary>The ropes of a lane (bits 1, 2; not the virtual one) with the texture of floats and hardly a line of colour.</summary>
        private static int TextureRopeBits(SwimStrokeLane lane, int virtualRope, double cx, double[] texture, double[] m, double max, double[] y0s, int n)
        {
            int bits = 0;
            if (TextureRopeMagnitude <= 0)
                return bits;
            for (int rope = 1; rope <= 2; rope++)
            {
                if (virtualRope == rope)
                    continue;
                int r = (int)Math.Round((rope == 1 ? lane.Y1(cx) : lane.Y2(cx)) - y0s[0]);
                if (r < 1 || r > n - 2)
                    continue;
                double colour = Math.Max(m[r], Math.Max(m[r - 1], m[r + 1])) / max;
                double z = Math.Max(texture[r], Math.Max(texture[r - 1], texture[r + 1]));
                if (z >= TextureRopeZ && colour < TextureRopeMagnitude)
                    bits |= rope;
            }

            return bits;
        }

        /// <summary>
        /// Texture of the lines around the swimmer (columns cx ± TextureHalfWidth): per line the mean of the lower 80 % of the
        /// brightness differences 2 px apart along it (the floats of a rope, light and dark one after the other, change it all
        /// along; the water, a line of the floor or of a deck hardly), minus its smoothing, the best over the slopes around
        /// 'slope', in units of its usual level (median of its size). Null when too few columns.
        /// </summary>
        private static double[] TextureProfile(SwimStrokeImage img, double scale, double cx, double slope, double[] y0s, int n, int r0 = 0, int r1 = int.MaxValue)
        {
            r0 = Math.Max(0, r0);
            r1 = Math.Min(n - 1, r1);
            if (r1 - r0 < 10)
                return null;

            // Working coordinates (x, y) at (x, y) * scale in the image; a line of the working size is 'sub' lines of the image.
            int x0 = Math.Max(1, (int)((cx - TextureHalfWidth) * scale)), x1 = Math.Min(img.Width - 3, (int)((cx + TextureHalfWidth) * scale));
            int m = x1 - x0 + 1;
            if (m < 40 * scale)
                return null;

            int sub = Math.Max(1, (int)Math.Round(scale));
            double[] best = new double[n];
            for (int i = 0; i < n; i++)
                best[i] = double.MinValue;
            double[] g = new double[m], d = new double[m], t = new double[n];
            int steps = (int)Math.Round(TextureSlopeRange / 0.005);
            for (int k = -steps; k <= steps; k++)
            {
                double a = slope + 0.005 * k;
                for (int i = r0; i <= r1; i++)
                {
                    t[i] = 0;
                    for (int q = 0; q < sub; q++)
                    {
                        double shift = q - 0.5 * (sub - 1);
                        for (int j = 0; j < m; j++)
                        {
                            double x = x0 + j, y = y0s[i] * scale + a * (x - cx * scale) + shift;
                            if (y < 1 || y > img.Height - 2)
                            {
                                g[j] = double.NaN;
                                continue;
                            }

                            double b, gg, r;
                            img.Sample(x, y, out b, out gg, out r);
                            g[j] = (b + gg + r) / 3;
                        }

                        int nd = 0;
                        for (int j = 0; j + 2 < m; j++)
                        {
                            if (!double.IsNaN(g[j]) && !double.IsNaN(g[j + 2]))
                                d[nd++] = Math.Abs(g[j + 2] - g[j]);
                        }

                        if (nd < 20)
                            continue;

                        Array.Sort(d, 0, nd);
                        int keep = (int)(0.8 * nd);
                        double sum = 0;
                        for (int j = 0; j < keep; j++)
                            sum += d[j];
                        t[i] = Math.Max(t[i], sum / keep);
                    }
                }

                double[] part = new double[r1 - r0 + 1];
                Array.Copy(t, r0, part, 0, part.Length);
                double[] sm = Gauss1D(part, RopeSigma);
                for (int i = r0; i <= r1; i++)
                    best[i] = Math.Max(best[i], t[i] - sm[i - r0]);
            }

            double[] size = best.Skip(r0).Take(r1 - r0 + 1).Select(v => Math.Abs(v)).OrderBy(v => v).ToArray();
            double usual = Math.Max(1e-6, size[size.Length / 2]);
            for (int i = 0; i < n; i++)
                best[i] = i < r0 || i > r1 ? 0 : best[i] / usual;
            return best;
        }

        private static bool LaneAtSlope(double bestSlope, double[] bestMagnitude, double[] profile, double[] y0s, int n, double headX, double headY,
            double feetX, double feetY, double L, double cx, out SwimStrokeLane lane, out int virtualRope, double[] texture = null)
        {
            lane = new SwimStrokeLane();
            virtualRope = 0;
            double[] m = Gauss1D(bestMagnitude, 1.0);
            double max = 0;
            for (int i = 0; i < n; i++)
                max = Math.Max(max, m[i]);
            if (max <= 0)
                return false;

            // The ropes of floats seen by their texture (blue and white floats on blue water: hardly a line of colour).
            if (texture != null && TextureWeight > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    if (texture[i] >= TextureZMin)
                        m[i] = Math.Max(m[i], TextureWeight * max * Math.Min(1.0, texture[i] / TextureZRef));
                }
            }

            List<int> peaks = FindPeaks(m, 4, 0.06 * max);
            if (LaneTrace != null)
                LaneTrace(string.Format(System.Globalization.CultureInfo.InvariantCulture, "lane: slope {0:0.000}, {1} peaks at {2}", bestSlope, peaks.Count,
                    string.Join(",", peaks.Select(pk => (y0s[pk]).ToString("0", System.Globalization.CultureInfo.InvariantCulture)))));
            if (peaks.Count < 3)
                return false;

            double peakMax = 0;
            foreach (int p in peaks)
                peakMax = Math.Max(peakMax, m[p]);

            double[] ys = new double[peaks.Count];
            double[] st = new double[peaks.Count];
            for (int i = 0; i < peaks.Count; i++)
            {
                ys[i] = y0s[peaks[i]];
                st[i] = m[peaks[i]] / peakMax;
            }

            // The lines of the floor of the pool (clear water, camera high): a shade of the water, half way between the ropes. A
            // chain with them has half the spacing of the ropes: when the lane found has a clearly dark one as a rope, the lane is
            // searched again without any of them.
            bool[] strongAll;
            bool[] floorAll = FloorLines(profile, n, peaks, out strongAll);

            // All the candidates, for the lines inside the lanes of a chain (see GapPenalty).
            double[] allY = (double[])ys.Clone();
            double[] allS = (double[])st.Clone();
            int[] kept = Enumerable.Range(0, ys.Length).ToArray();

            // The waves left out: candidates weaker than a part of the third strongest line (lines 10 px apart), not of the strongest
            // one, which can be something much stronger than the ropes (the edge of the deck, a dark board, a stand in front).
            if (MinStrength > 0)
            {
                double reference = LaneReference > 0 ? DistinctStrength(ys, st, LaneReference, 10) : 1.0;
                int[] keep = Enumerable.Range(0, st.Length).Where(i => st[i] >= MinStrength * reference).ToArray();
                if (keep.Length >= 3)
                {
                    ys = keep.Select(i => ys[i]).ToArray();
                    st = keep.Select(i => st[i]).ToArray();
                    peaks = keep.Select(i => peaks[i]).ToList();
                    kept = keep;
                }
            }

            if (StrengthCap > 0)
            {
                double[] sorted = st.OrderBy(v0 => v0).ToArray();
                double cap = StrengthCap * sorted[sorted.Length / 2];
                for (int i = 0; i < st.Length; i++)
                    st[i] = Math.Min(st[i], cap);
                for (int i = 0; i < allS.Length; i++)
                    allS[i] = Math.Min(allS[i], cap);
            }

            double stMax = st.Max();
            if (stMax > 0)
            {
                for (int i = 0; i < st.Length; i++)
                    st[i] /= stMax;
                for (int i = 0; i < allS.Length; i++)
                    allS[i] /= stMax;
            }

            if (LaneTrace != null)
                LaneTrace("lane: strengths " + string.Join(",", st.Select((v0, i0) => ys[i0].ToString("0", System.Globalization.CultureInfo.InvariantCulture) + ":" + v0.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))));

            bool[] floor = kept.Select(i => floorAll[i]).ToArray();
            bool[] strong = kept.Select(i => strongAll[i]).ToArray();

            // A lane of a chain with a strong line inside it, not a line of the floor nor next to its ropes: a chain of every other
            // rope (one of them weak, hidden by the splashes), not the ropes.
            Func<double, double, double> inside = (a, b) =>
            {
                if (GapPenalty <= 0)
                    return 0;
                double worst = 0, near = 0.2 * (b - a);
                for (int k = 0; k < allY.Length; k++)
                {
                    double y = allY[k];
                    if (y <= a || y >= b || floorAll[k] || y - a <= near || b - y <= near || allS[k] < GapPenaltyMin)
                        continue;
                    worst = Math.Max(worst, allS[k]);
                }

                return GapPenalty * worst;
            };

            int[] all = Enumerable.Range(0, ys.Length).ToArray();
            double y1, y2;
            int r1, r2, v;
            if (!LaneOfCandidates(ys, st, all, bestSlope, headX, headY, feetX, feetY, L, cx, inside, out y1, out y2, out v, out r1, out r2))
                return false;

            if ((r1 >= 0 && strong[r1]) || (r2 >= 0 && strong[r2]))
            {
                int[] ropes = all.Where(i => !floor[i]).ToArray();
                double z1, z2;
                int q1, q2, w;
                if (ropes.Length >= 3 && LaneOfCandidates(ys, st, ropes, bestSlope, headX, headY, feetX, feetY, L, cx, inside, out z1, out z2, out w, out q1, out q2) &&
                    (!FloorCheck || FloorLaneFits(y1, y2, r1, r2, strong, z1, z2)))
                {
                    if (LaneTrace != null)
                        LaneTrace(string.Format(System.Globalization.CultureInfo.InvariantCulture, "lane: floor line {0:0}/{1:0} -> ropes {2:0}/{3:0}", y1, y2, z1, z2));
                    y1 = z1;
                    y2 = z2;
                    v = w;
                    r1 = q1;
                    r2 = q2;
                }
            }

            // A candidate as strong just inside a rope of the lane (the other edge of a thick rope, the rope next to the edge of
            // the wall): the rope is the inner one.
            if (InnerRope > 0)
            {
                double near = InnerRope * (y2 - y1);
                double s1 = r1 >= 0 ? st[r1] : 0, s2 = r2 >= 0 ? st[r2] : 0;
                if (v != 1 && r1 >= 0)
                {
                    int k = Enumerable.Range(0, ys.Length).Where(i => ys[i] > y1 && ys[i] - y1 <= near && st[i] >= 0.8 * s1).DefaultIfEmpty(-1).Last();
                    if (k >= 0)
                        y1 = ys[k];
                }

                if (v != 2 && r2 >= 0)
                {
                    int k = Enumerable.Range(0, ys.Length).Where(i => ys[i] < y2 && y2 - ys[i] <= near && st[i] >= 0.8 * s2).DefaultIfEmpty(-1).First();
                    if (k >= 0)
                        y2 = ys[k];
                }
            }

            virtualRope = v;
            lane = new SwimStrokeLane(bestSlope, y1 - bestSlope * cx, bestSlope, y2 - bestSlope * cx);
            return true;
        }

        /// <summary>
        /// Candidates of a line profile across the lines that are darker than the water by the same part in its colours (the lines
        /// on the floor of the pool seen through the water, as a shade; 'strong': clearly darker): the ropes are bright (white,
        /// yellow floats) or change the colour (blue, purple, red). The colours too dark to measure (the red under the water of a
        /// dim pool) do not count.
        /// </summary>
        private static bool[] FloorLines(double[] profile, int n, List<int> peaks, out bool[] strong)
        {
            bool[] floor = new bool[peaks.Count];
            strong = new bool[peaks.Count];
            if (profile == null || profile.Length < n * 3)
                return floor;

            double[][] high = new double[3][];
            double[][] level = new double[3][];
            for (int c = 0; c < 3; c++)
            {
                double[] ch = new double[n];
                for (int i = 0; i < n; i++)
                    ch[i] = profile[i * 3 + c];
                double[] smooth = Gauss1D(ch, RopeSigma);
                for (int i = 0; i < n; i++)
                    ch[i] -= smooth[i];
                high[c] = ch;
                level[c] = smooth;
            }

            for (int k = 0; k < peaks.Count; k++)
            {
                int p = peaks[k];
                List<double> parts = new List<double>();
                for (int c = 0; c < 3; c++)
                {
                    if (level[c][p] >= 40)
                        parts.Add(high[c][p] / level[c][p]);
                }

                if (parts.Count >= 2 && parts.Max() <= -0.08)
                {
                    double mean = parts.Average();
                    floor[k] = parts.Max() - parts.Min() < 0.35 * Math.Abs(mean);
                    strong[k] = floor[k] && mean <= -0.15;
                }

                if (LaneTrace != null)
                    LaneTrace(string.Format(System.Globalization.CultureInfo.InvariantCulture, "lane: peak {0} rel {1:0.00} {2:0.00} {3:0.00} lvl {4:0} {5:0} {6:0}{7}{8}", p,
                        high[0][p] / Math.Max(1, level[0][p]), high[1][p] / Math.Max(1, level[1][p]), high[2][p] / Math.Max(1, level[2][p]),
                        level[0][p], level[1][p], level[2][p], floor[k] ? " floor" : "", strong[k] ? " strong" : ""));
            }

            return floor;
        }

        /// <summary>
        /// The lane holding the swimmer from some of the rope candidates (their longest regular chain): its two ropes (y at cx) and
        /// the candidates they are (−1 for the place of the missing rope of an outer lane).
        /// </summary>
        private static bool LaneOfCandidates(double[] ys, double[] st, int[] use, double slope, double headX, double headY, double feetX, double feetY,
            double L, double cx, Func<double, double, double> inside, out double laneY1, out double laneY2, out int virtualRope, out int rope1, out int rope2)
        {
            laneY1 = laneY2 = 0;
            virtualRope = 0;
            rope1 = rope2 = -1;
            double[] uy = use.Select(i => ys[i]).ToArray();
            double[] us = use.Select(i => st[i]).ToArray();
            Func<double, double, double> coverageOf = (y1, y2) =>
            {
                double cov = 0;
                for (int k = 0; k <= 10; k++)
                {
                    double f = k / 10.0;
                    double px = feetX + (headX - feetX) * f;
                    double py = feetY + (headY - feetY) * f;
                    if (y1 + slope * (px - cx) <= py && py <= y2 + slope * (px - cx))
                        cov += f <= 0.7 ? 1.0 : 0.4;
                }

                return cov;
            };

            // The lane holding the swimmer gains when he is in its middle (the ropes of a chain of other lines can hold him at an edge).
            double mx = 0.5 * (headX + feetX), my = 0.5 * (headY + feetY);
            Func<double, double, double> centred = (y1, y2) =>
            {
                double rel = (my - (y1 + slope * (mx - cx))) / Math.Max(1e-6, y2 - y1);
                return LaneCenter * Math.Max(0, 1 - 2 * Math.Abs(rel - 0.5));
            };

            List<int> chain = RopeChain(uy, us, Math.Max(8.0, GapLength * L), 3.0 * L, (y1, y2) => coverageOf(y1, y2) >= 2.5, inside, centred);
            if (LaneTrace != null)
                LaneTrace("lane: chain " + string.Join(",", chain.Select(c => uy[c].ToString("0", System.Globalization.CultureInfo.InvariantCulture))));

            if (chain.Count < (TwoRopes ? 2 : 3))
                return false;

            double bestCoverage = 0;
            bool found = false;
            for (int c = 0; c + 1 < chain.Count; c++)
            {
                double coverage = coverageOf(uy[chain[c]], uy[chain[c + 1]]);
                if (coverage > bestCoverage)
                {
                    bestCoverage = coverage;
                    laneY1 = uy[chain[c]];
                    laneY2 = uy[chain[c + 1]];
                    rope1 = use[chain[c]];
                    rope2 = use[chain[c + 1]];
                    found = true;
                }
            }

            // Outer lane: the swimmer is beyond the first or the last rope of the chain; the wall side is the next place of the
            // chain (the spacing changes with the perspective as between the last two ropes).
            if ((!found || bestCoverage < 2.5) && chain.Count >= 3)
            {
                int last = chain.Count - 1;
                double g0 = uy[chain[1]] - uy[chain[0]], g1 = uy[chain[2]] - uy[chain[1]];
                double gl = uy[chain[last]] - uy[chain[last - 1]], gl1 = uy[chain[last - 1]] - uy[chain[last - 2]];
                double before = uy[chain[0]] - g0 * Math.Max(0.7, Math.Min(1.4, g0 / Math.Max(1e-6, g1)));
                double after = uy[chain[last]] + gl * Math.Max(0.7, Math.Min(1.4, gl / Math.Max(1e-6, gl1)));
                double cb = coverageOf(before, uy[chain[0]]), ca = coverageOf(uy[chain[last]], after);
                if (Math.Max(cb, ca) >= 2.5)
                {
                    if (ca >= cb)
                    {
                        laneY1 = uy[chain[last]];
                        laneY2 = after;
                        rope1 = use[chain[last]];
                        rope2 = -1;
                        virtualRope = 2;
                    }
                    else
                    {
                        laneY1 = before;
                        laneY2 = uy[chain[0]];
                        rope1 = -1;
                        rope2 = use[chain[0]];
                        virtualRope = 1;
                    }

                    found = true;
                    bestCoverage = Math.Max(cb, ca);
                }
            }

            return found && bestCoverage >= 2.5;
        }

        /// <summary>
        /// The lane searched again without the lines of the floor fits the old one: it holds it, about twice as wide, with a dark
        /// line of the old one near its middle (dark ropes, navy blue, look like the lines of the floor: the old lane stays).
        /// </summary>
        private static bool FloorLaneFits(double y1, double y2, int r1, int r2, bool[] strong, double z1, double z2)
        {
            double wid = y2 - y1, wid2 = z2 - z1, tol = 0.15 * wid;
            if (z1 > y1 + tol || z2 < y2 - tol || wid2 < 1.5 * wid || wid2 > 2.6 * wid)
                return false;

            Func<double, bool> middle = y => y >= z1 + 0.3 * wid2 && y <= z2 - 0.3 * wid2;
            return (r1 >= 0 && strong[r1] && middle(y1)) || (r2 >= 0 && strong[r2] && middle(y2));
        }

        /// <summary>Strength of the k-th strongest candidate among candidates at least 'apart' px from the stronger ones taken.</summary>
        private static double DistinctStrength(double[] ys, double[] st, int k, double apart)
        {
            List<int> picked = new List<int>();
            foreach (int i in Enumerable.Range(0, st.Length).OrderByDescending(i => st[i]))
            {
                if (picked.All(j => Math.Abs(ys[i] - ys[j]) > apart))
                    picked.Add(i);
                if (picked.Count >= k)
                    break;
            }

            return picked.Count > 0 ? st[picked[picked.Count - 1]] : 0;
        }

        /// <summary>
        /// Longest regularly spaced chain of rope candidates (spacing changes smoothly with the perspective). holds: whether two
        /// next candidates hold the swimmer between them; the best chain with such a pair is preferred when there is one. inside:
        /// what a lane between two candidates loses for a strong line inside it; centred: what the lane holding the swimmer gains
        /// (null: nothing).
        /// </summary>
        private static List<int> RopeChain(double[] ys, double[] st, double gmin, double gmax, Func<double, double, bool> holds = null,
            Func<double, double, double> inside = null, Func<double, double, double> centred = null)
        {
            int n = ys.Length;
            double[,,] best = new double[n, n, 2];
            int[,,] prev = new int[n, n, 2];
            int[,,] prevHeld = new int[n, n, 2];
            bool[,,] valid = new bool[n, n, 2];
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < j; i++)
                {
                    double g = ys[j] - ys[i];
                    if (g < gmin || g > gmax)
                        continue;

                    int held = holds != null && holds(ys[i], ys[j]) ? 1 : 0;
                    double loss = (inside != null ? inside(ys[i], ys[j]) : 0) - (held == 1 && centred != null ? centred(ys[i], ys[j]) : 0);
                    best[i, j, held] = st[i] + st[j] - loss;
                    prev[i, j, held] = -1;
                    valid[i, j, held] = true;
                    for (int h = 0; h < i; h++)
                    {
                        for (int c0 = 0; c0 < 2; c0++)
                        {
                            if (!valid[h, i, c0])
                                continue;

                            double r = g / (ys[i] - ys[h]);
                            if (r < GapMin || r > 1.3)
                                continue;

                            int c = Math.Max(c0, held);
                            double candidate = best[h, i, c0] + st[j] - loss;
                            if (!valid[i, j, c] || candidate > best[i, j, c])
                            {
                                best[i, j, c] = candidate;
                                prev[i, j, c] = h;
                                prevHeld[i, j, c] = c0;
                                valid[i, j, c] = true;
                            }
                        }
                    }
                }
            }

            int bi = -1, bj = -1, bc = 0;
            for (int pass = holds != null && PreferHolding ? 1 : 0; pass >= 0 && bi < 0; pass--)
            {
                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        for (int c = pass; c < 2; c++)
                        {
                            if (!valid[i, j, c])
                                continue;
                            if (bi < 0 || best[i, j, c] > best[bi, bj, bc])
                            {
                                bi = i;
                                bj = j;
                                bc = c;
                            }
                        }
                    }
                }

                // A chain holding the swimmer of two candidates only: the longer chain without him (outer lane).
                if (bi >= 0 && pass == 1 && prev[bi, bj, bc] < 0)
                    bi = -1;
            }

            List<int> chain = new List<int>();
            if (bi < 0)
                return chain;

            chain.Add(bj);
            chain.Add(bi);
            int ci = bi, cj = bj, cc = bc;
            while (prev[ci, cj, cc] >= 0)
            {
                int h = prev[ci, cj, cc];
                int hc = prevHeld[ci, cj, cc];
                chain.Add(h);
                cj = ci;
                ci = h;
                cc = hc;
            }

            chain.Reverse();
            return chain;
        }

        /// <summary>Band along the swimmer's axis when there are no ropes (side views): ±35 % of the swimmer length.</summary>
        public static SwimStrokeLane AxisBand(double headX, double headY, double feetX, double feetY)
        {
            double dx = headX - feetX, dy = headY - feetY;
            double L = Math.Sqrt(dx * dx + dy * dy);
            double a = Math.Abs(dx) > 1e-6 ? dy / dx : 0.0;
            a = Math.Max(-0.6, Math.Min(0.6, a));
            double mx = 0.5 * (headX + feetX), my = 0.5 * (headY + feetY);
            double off = 0.35 * L * Math.Sqrt(1 + a * a);
            return new SwimStrokeLane(a, my - off - a * mx, a, my + off - a * mx);
        }

        /// <summary>Rope-ness of the lines parallel to (a, b) at the offsets −RopeR … RopeR, null if the line is out of the image.</summary>
        private static double[] Ropeness(SwimStrokeImage img, double a, double b)
        {
            int w = img.Width, h = img.Height;
            int lo = -RopeR - RopeMargin;
            int count = 2 * (RopeR + RopeMargin) + 1;
            List<int> columns = new List<int>();
            for (int x = 0; x < w; x += RopeStep)
            {
                double y = a * x + b;
                if (y + lo >= 0 && y - lo <= h - 1)
                    columns.Add(x);
            }

            if (columns.Count < 12)
                return null;

            double[] profile = new double[count * 3];
            for (int k = 0; k < count; k++)
            {
                double sb = 0, sg = 0, sr = 0;
                foreach (int x in columns)
                {
                    double bb, gg, rr;
                    img.Sample(x, a * x + b + lo + k, out bb, out gg, out rr);
                    sb += bb; sg += gg; sr += rr;
                }

                profile[k * 3] = sb / columns.Count;
                profile[k * 3 + 1] = sg / columns.Count;
                profile[k * 3 + 2] = sr / columns.Count;
            }

            double energy;
            double[] magnitude = HighPassMagnitude(profile, count, out energy);
            double[] m = Gauss1D(magnitude, 1.0);
            double[] result = new double[2 * RopeR + 1];
            Array.Copy(m, RopeMargin, result, 0, result.Length);
            return result;
        }

        /// <summary>
        /// Texture of the lines parallel to (a, b) at the offsets −RopeR … RopeR (as in TextureProfile: the mean of the lower 80 % of
        /// the brightness differences 2 px apart along the line), minus its smoothing; null if the line is out of the image.
        /// </summary>
        private static double[] RopeTexture(SwimStrokeImage img, double a, double b)
        {
            int w = img.Width, h = img.Height;
            int lo = -RopeR - RopeMargin;
            int count = 2 * (RopeR + RopeMargin) + 1;
            List<int> columns = new List<int>();
            for (int x = 0; x + 2 < w; x += RopeStep)
            {
                double y1 = Math.Min(a * x + b, a * (x + 2) + b), y2 = Math.Max(a * x + b, a * (x + 2) + b);
                if (y1 + lo >= 0 && y2 - lo <= h - 1)
                    columns.Add(x);
            }

            if (columns.Count < 12)
                return null;

            double[] t = new double[count];
            double[] d = new double[columns.Count];
            int keep = Math.Max(1, (int)(0.8 * columns.Count));
            for (int k = 0; k < count; k++)
            {
                for (int j = 0; j < columns.Count; j++)
                {
                    int x = columns[j];
                    double b1, g1, r1, b2, g2, r2;
                    img.Sample(x, a * x + b + lo + k, out b1, out g1, out r1);
                    img.Sample(x + 2, a * (x + 2) + b + lo + k, out b2, out g2, out r2);
                    d[j] = Math.Abs((b2 + g2 + r2) - (b1 + g1 + r1)) / 3;
                }

                Array.Sort(d);
                double sum = 0;
                for (int j = 0; j < keep; j++)
                    sum += d[j];
                t[k] = sum / keep;
            }

            double[] smooth = Gauss1D(t, RopeSigma);
            for (int k = 0; k < count; k++)
                t[k] -= smooth[k];
            double[] tm = Gauss1D(t, 1.0);
            double[] result = new double[2 * RopeR + 1];
            Array.Copy(tm, RopeMargin, result, 0, result.Length);
            return result;
        }

        /// <summary>
        /// The two ropes of the lane in this frame, from their place predicted with the camera motion. A rope that leaves the
        /// image, is hidden or loses its texture, or that moves away from the other one, follows the good rope with the last
        /// place it had relative to it instead of drifting on its own (a lane along the near wall, a camera in front).
        /// </summary>
        private SwimStrokeLane TrackRopes(SwimStrokeImage image, SwimStrokeLane predicted)
        {
            double a1 = predicted.A1, b1 = predicted.B1, a2 = predicted.A2, b2 = predicted.B2;
            bool ok1 = false, ok2 = false;
            bool t1 = (predicted.TextureRopes & 1) != 0, t2 = (predicted.TextureRopes & 2) != 0;
            int reach = RefineReach(predicted.Width(width / 2.0), TrackShare);
            Parallel.Invoke(
                () => ok1 = RefineLine(image, predicted.A1, predicted.B1, out a1, out b1, reach, t1),
                () => ok2 = RefineLine(image, predicted.A2, predicted.B2, out a2, out b2, reach, t2));
            if (!ok1)
            {
                a1 = predicted.A1;
                b1 = predicted.B1;
            }

            if (!ok2)
            {
                a2 = predicted.A2;
                b2 = predicted.B2;
            }

            double inside1, inside2;
            double s1 = RopeStrength(image, a1, b1, out inside1, t1);
            double s2 = RopeStrength(image, a2, b2, out inside2, t2);
            double usual1 = ropeStrength1 > 0 ? ropeStrength1 : s1;
            double usual2 = ropeStrength2 > 0 ? ropeStrength2 : s2;
            bool good1 = inside1 >= 0.25 && s1 >= 0.5 * usual1;
            bool good2 = inside2 >= 0.25 && s2 >= 0.5 * usual2;
            if (good1)
                ropeStrength1 = 0.95 * usual1 + 0.05 * s1;
            if (good2)
                ropeStrength2 = 0.95 * usual2 + 0.05 * s2;

            SwimStrokeLane next = predicted;
            if (good1)
            {
                next.A1 = a1;
                next.B1 = b1;
            }

            if (good2)
            {
                next.A2 = a2;
                next.B2 = b2;
            }

            if (good1 && good2)
            {
                // The two ropes stay nearly parallel and the width of the lane changes slowly: the rope that moved more is wrong.
                double wp = predicted.Width(width / 2.0);
                double wn = next.Width(width / 2.0);
                if (Math.Abs(wn - wp) > 0.06 * Math.Abs(wp) || Math.Abs(next.A1 - next.A2) > Math.Abs(predicted.A1 - predicted.A2) + 0.02)
                {
                    double d1 = Math.Abs(next.B1 - predicted.B1) + 200 * Math.Abs(next.A1 - predicted.A1);
                    double d2 = Math.Abs(next.B2 - predicted.B2) + 200 * Math.Abs(next.A2 - predicted.A2);
                    if (d2 > d1)
                        good2 = false;
                    else
                        good1 = false;
                }
            }

            double xc = width / 2.0;
            if (!good1)
            {
                if (good2)
                {
                    double a = next.A2 - relativeSlope;
                    double y = next.A2 * xc + next.B2 - relativeOffset;
                    next.A1 = a;
                    next.B1 = y - a * xc;
                }
                else
                {
                    next.A1 = predicted.A1;
                    next.B1 = predicted.B1;
                }
            }

            if (!good2)
            {
                if (good1)
                {
                    double a = next.A1 + relativeSlope;
                    double y = next.A1 * xc + next.B1 + relativeOffset;
                    next.A2 = a;
                    next.B2 = y - a * xc;
                }
                else
                {
                    next.A2 = predicted.A2;
                    next.B2 = predicted.B2;
                }
            }

            if (good1 && good2)
                KeepRopeGeometry(next);

            return next;
        }

        /// <summary>The second rope relative to the first one (slope difference, vertical offset at the middle of the image).</summary>
        private void KeepRopeGeometry(SwimStrokeLane l)
        {
            double xc = width / 2.0;
            relativeSlope = l.A2 - l.A1;
            relativeOffset = (l.A2 * xc + l.B2) - (l.A1 * xc + l.B1);
        }

        /// <summary>
        /// Rope-ness at the line itself (peak within two pixels) and the share of the width of the image where the line and its
        /// margins are inside the image.
        /// </summary>
        private static double RopeStrength(SwimStrokeImage img, double a, double b, out double inside, bool texture = false)
        {
            int columns = 0, total = 0;
            for (int x = 0; x < img.Width; x += RopeStep)
            {
                double y = a * x + b;
                total++;
                if (y - RopeR - RopeMargin >= 0 && y + RopeR + RopeMargin <= img.Height - 1)
                    columns++;
            }

            inside = total > 0 ? (double)columns / total : 0;
            double[] m = texture ? RopeTexture(img, a, b) : Ropeness(img, a, b);
            if (m == null)
                return 0;

            int c = m.Length / 2;
            double best = 0;
            for (int i = Math.Max(0, c - 2); i <= Math.Min(m.Length - 1, c + 2); i++)
                best = Math.Max(best, m[i]);
            return best;
        }

        /// <summary>A rope line moves at most this part of the width of the lane in the refinement of the first frame (RefineShare)
        /// and of the next ones (TrackShare), at most RopeR (0: RopeR; tests).</summary>
        internal static double RefineShare = 0.15;
        internal static double TrackShare = 0.0;

        /// <summary>The reach of the refinement of a rope line in a lane this wide (px): the lines next to a rope (its shade, a line
        /// of the floor) are close to it in a narrow lane.</summary>
        private static int RefineReach(double laneWidth, double share)
        {
            if (share <= 0 || double.IsNaN(laneWidth))
                return RopeR;
            return Math.Max(2, Math.Min(RopeR, (int)Math.Round(share * Math.Abs(laneWidth))));
        }

        /// <summary>Move a rope line to the nearby offset (at most 'reach' px) and slope with the strongest rope-ness (texture: of
        /// its floats).</summary>
        private static bool RefineLine(SwimStrokeImage img, double a, double b, out double ra, out double rb, int reach = RopeR, bool texture = false)
        {
            ra = a;
            rb = b;
            double xc = 0.5 * img.Width;
            double best = double.MinValue;
            bool found = false;
            double[] slopes = { -0.012, -0.006, 0.0, 0.006, 0.012 };
            foreach (double da in slopes)
            {
                double a2 = a + da;
                double b2 = b - da * xc;
                double[] m = texture ? RopeTexture(img, a2, b2) : Ropeness(img, a2, b2);
                if (m == null)
                    continue;

                int k = 0;
                double kBest = double.MinValue;
                for (int i = Math.Max(0, RopeR - reach); i <= Math.Min(m.Length - 1, RopeR + reach); i++)
                {
                    double sc = m[i] - 0.15 * Math.Abs(i - RopeR) - 20 * Math.Abs(da);
                    if (sc > kBest)
                    {
                        kBest = sc;
                        k = i;
                    }
                }

                if (kBest > best)
                {
                    best = kBest;
                    ra = a2;
                    rb = b2 + (k - RopeR);
                    found = true;
                }
            }

            return found;
        }
        #endregion

        #region Filters
        private static double[] GaussKernel(double sigma, out int r)
        {
            r = (int)Math.Ceiling(3.0 * sigma);
            double[] k = new double[2 * r + 1];
            double sum = 0;
            for (int i = -r; i <= r; i++)
            {
                double z = i / sigma;
                k[i + r] = Math.Exp(-0.5 * z * z);
                sum += k[i + r];
            }

            for (int i = 0; i < k.Length; i++)
                k[i] /= sum;
            return k;
        }

        /// <summary>Gaussian filter of a signal, edges replicated.</summary>
        public static double[] Gauss1D(double[] a, double sigma)
        {
            int n = a.Length;
            double[] output = new double[n];
            if (sigma <= 0)
            {
                Array.Copy(a, output, n);
                return output;
            }

            int r;
            double[] k = GaussKernel(sigma, out r);
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                for (int j = -r; j <= r; j++)
                    s += k[j + r] * a[Clamp(i + j, 0, n - 1)];
                output[i] = s;
            }

            return output;
        }

        /// <summary>Gaussian filter of a T × W array along the frames (alongT) or along the band.</summary>
        private static double[] GaussAlong(double[] a, int T, int W, double sigma, bool alongT)
        {
            int r;
            double[] k = GaussKernel(sigma, out r);
            double[] output = new double[a.Length];
            if (alongT)
            {
                for (int t = 0; t < T; t++)
                {
                    for (int j = -r; j <= r; j++)
                    {
                        int tt = Clamp(t + j, 0, T - 1);
                        double kj = k[j + r];
                        int src = tt * W, dst = t * W;
                        for (int x = 0; x < W; x++)
                            output[dst + x] += kj * a[src + x];
                    }
                }
            }
            else
            {
                for (int t = 0; t < T; t++)
                {
                    int row = t * W;
                    for (int x = 0; x < W; x++)
                    {
                        double s = 0;
                        for (int j = -r; j <= r; j++)
                            s += k[j + r] * a[row + Clamp(x + j, 0, W - 1)];
                        output[row + x] = s;
                    }
                }
            }

            return output;
        }

        /// <summary>Running median over an odd window, edges replicated.</summary>
        private static double[] MedianFilter(double[] x, int n)
        {
            int half = n / 2;
            double[] output = new double[x.Length];
            double[] window = new double[n];
            for (int i = 0; i < x.Length; i++)
            {
                for (int j = 0; j < n; j++)
                    window[j] = x[Clamp(i - half + j, 0, x.Length - 1)];
                Array.Sort(window);
                output[i] = window[half];
            }

            return output;
        }

        private static double Median(List<double> values)
        {
            List<double> sorted = new List<double>(values);
            sorted.Sort();
            int n = sorted.Count;
            if (n == 0)
                return 0;
            return n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
        }

        /// <summary>
        /// Local maxima with at least the given prominence, then the smaller peaks closer than distance to a higher one are removed.
        /// </summary>
        public static List<int> FindPeaks(double[] y, int distance, double prominence)
        {
            int n = y.Length;
            List<int> peaks = new List<int>();
            for (int i = 1; i < n - 1; i++)
            {
                if (!(y[i] > y[i - 1] && y[i] >= y[i + 1]))
                    continue;

                double leftMin = y[i];
                int j = i - 1;
                while (j >= 0 && y[j] <= y[i])
                {
                    leftMin = Math.Min(leftMin, y[j]);
                    j--;
                }

                double rightMin = y[i];
                j = i + 1;
                while (j < n && y[j] <= y[i])
                {
                    rightMin = Math.Min(rightMin, y[j]);
                    j++;
                }

                if (y[i] - Math.Max(leftMin, rightMin) >= prominence)
                    peaks.Add(i);
            }

            if (distance <= 1 || peaks.Count == 0)
                return peaks;

            List<int> byHeight = new List<int>(peaks);
            byHeight.Sort((p, q) => y[q].CompareTo(y[p]));
            HashSet<int> removed = new HashSet<int>();
            foreach (int i in byHeight)
            {
                if (removed.Contains(i))
                    continue;

                foreach (int j in peaks)
                {
                    if (j != i && Math.Abs(j - i) < distance && y[j] <= y[i])
                        removed.Add(j);
                }
            }

            List<int> result = new List<int>();
            foreach (int p in peaks)
            {
                if (!removed.Contains(p))
                    result.Add(p);
            }

            return result;
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
        #endregion

        #region Homography
        public static double[] Identity()
        {
            return new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        }

        public static void MapPoint(double[] H, double x, double y, out double X, out double Y)
        {
            double z = H[6] * x + H[7] * y + H[8];
            X = (H[0] * x + H[1] * y + H[2]) / z;
            Y = (H[3] * x + H[4] * y + H[5]) / z;
        }

        private static void MapLine(double[] H, double a, double b, double x0, double x1, out double ra, out double rb)
        {
            double X0, Y0, X1, Y1;
            MapPoint(H, x0, a * x0 + b, out X0, out Y0);
            MapPoint(H, x1, a * x1 + b, out X1, out Y1);
            ra = (Y1 - Y0) / (X1 - X0);
            rb = Y0 - ra * X0;
        }

        /// <summary>A × B (3×3, row-major).</summary>
        public static double[] Multiply(double[] A, double[] B)
        {
            double[] C = new double[9];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                    C[i * 3 + j] = A[i * 3] * B[j] + A[i * 3 + 1] * B[3 + j] + A[i * 3 + 2] * B[6 + j];
            }

            return C;
        }

        public static double[] Invert(double[] m)
        {
            double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
            double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
            double det = a * A + b * B + c * C;
            if (Math.Abs(det) < 1e-15)
                return Identity();

            double[] inv = new double[9];
            inv[0] = A / det;
            inv[1] = -(b * i - c * h) / det;
            inv[2] = (b * f - c * e) / det;
            inv[3] = B / det;
            inv[4] = (a * i - c * g) / det;
            inv[5] = -(a * f - c * d) / det;
            inv[6] = C / det;
            inv[7] = -(a * h - b * g) / det;
            inv[8] = (a * e - b * d) / det;
            return inv;
        }
        #endregion
    }
}
