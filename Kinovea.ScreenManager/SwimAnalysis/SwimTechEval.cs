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
    /// (2.13.0) What the program measured for a check of the catalogue in one start or turn: the value in the unit of the check, a short
    /// text, whether it is in the good range (null: the check has no range or the value is too near its limit to say), how sure the
    /// measurement is (1 for the times of the marks, the visibility of the joints for the skeleton) and the frame it was measured at.
    /// </summary>
    public class SwimTechMeasure
    {
        public string Check = "";
        public double Value;
        public string Text = "";
        public bool? Good;
        public double Confidence = 1.0;
        public long? Timestamp;
    }

    /// <summary>
    /// (2.13.0) The automatic part of the technique evaluation: the moment of the video of each criterion (anchor), the measurements of
    /// the checks from the marks of the phase and from the skeleton, the proposals they give and the totals of a sheet.
    /// The times come from the marks the analyst (or the automatic reading) put; the skeleton measurements only where the skeleton
    /// of the swimmer was read and its joints are seen well. A proposal is never a score: the coach confirms it.
    /// </summary>
    public static class SwimTechEvaluator
    {
        /// <summary>A measurement nearer to the limit of its range than this part of the limit gives no proposal.</summary>
        public const double Margin = 0.06;

        /// <summary>Joints seen less than this (0–1) are not measured.</summary>
        public const float MinVisibility = 0.5f;

        /// <summary>
        /// The checks of the catalogue this version measures: a criterion of one of them is measured (🤖); the other checks of the
        /// catalogue are what the coach looks at for now.
        /// </summary>
        public static readonly HashSet<string> MeasuredChecks = new HashSet<string>
        {
            // From the marks.
            "start_15", "turn_5_10", "turn_segments", "push_time", "rotation_flip", "kick_count_rot", "rotation_open", "rate_speed_5m",
            "kick_rate", "rule_15m", "rule_one_dolphin",
            // From the skeleton: turns.
            "knee_plant", "head_feet_depth", "elbow_touch", "streamline_plant", "body_line_plant", "streamline", "body_line", "body_line_glide",
            "push_dir", "push_dir_back", "push_dir_breast", "hips_to_wall", "legs_together", "kick_amp_growth",
            // From the skeleton: starts.
            "hips_vs_shoulders", "set_elbow", "front_knee_set", "set_hips_front", "rear_leg_height", "shoulders_takeoff", "arms_flex_takeoff",
            "lead_leg_hip", "legs_split", "head_spine_flight", "head_drop_flight", "flight_line", "arms_flight", "back_arch", "entry_streamline",
            "legs_split_entry", "head_lift_entry", "entry_arch", "hips_peak",
        };

        /// <summary>
        /// (2.13.1) Checks of the skeleton under the water, in the bubbles of the wall or of the dolphin kicks: proposed only once the
        /// coach's answers agree with them (★, as a criterion measured in part). At the hand-marked push-offs of the lab's underwater
        /// turns the line of the body read 50° off the straight line (median) even on the side views: the skeleton is not reliable
        /// there yet. Their measurement is still shown.
        /// </summary>
        public static readonly HashSet<string> NeedsAgreement = new HashSet<string>
        {
            "knee_plant", "head_feet_depth", "elbow_touch", "streamline_plant", "body_line_plant", "streamline", "body_line", "body_line_glide",
            "push_dir", "push_dir_back", "push_dir_breast", "hips_to_wall", "legs_together", "kick_amp_growth",
        };

        #region Anchors
        /// <summary>The time of a moment of the video in a phase, in seconds from its origin (null when it cannot be placed).</summary>
        public static double? AnchorSeconds(SwimTechPhase p, string anchor)
        {
            if (p == null || string.IsNullOrEmpty(anchor))
                return null;

            double? v;
            if (p.Kind == SwimPhaseKind.Start)
            {
                switch (anchor)
                {
                    case "set": return -0.1;
                    case "signal": return 0;
                    case "drive":
                        if (p.Reaction.HasValue && p.Block.HasValue) return 0.5 * (p.Reaction.Value + p.Block.Value);
                        return p.HandsOff ?? p.Reaction ?? (p.Block.HasValue ? p.Block * 0.5 : 0.35);
                    case "hands_off": return p.HandsOff ?? (p.Reaction.HasValue && p.Block.HasValue ? 0.5 * (p.Reaction + p.Block) : null);
                    case "rear_off": return p.BackFoot;
                    case "arms_vertical":
                        if (p.HandsOff.HasValue && p.Block.HasValue) return p.HandsOff + 0.5 * (p.Block - p.HandsOff);
                        return p.Block.HasValue ? p.Block * 0.7 : null;
                    case "takeoff": return p.Block;
                    case "midflight": return p.Block.HasValue && p.Entry.HasValue ? 0.5 * (p.Block + p.Entry) : null;
                    case "pre_entry": return p.Entry.HasValue ? p.Entry - 0.06 : null;
                    case "hand_entry": return p.Entry;
                    case "hip_entry": return p.Entry.HasValue ? p.Entry + 0.12 : null;
                    case "feet_entry": return p.Entry.HasValue ? p.Entry + 0.25 : null;
                    case "glide": return p.Entry.HasValue ? p.Entry + 0.4 : null;
                    case "underwater":
                        if (p.Kicks.Count > 0) return p.Kicks[0];
                        return p.Entry.HasValue ? p.Entry + 1.0 : null;
                    case "pullout": return p.Entry.HasValue ? p.Entry + 1.5 : null;
                    case "breakout": return p.Breakout;
                    case "first_stroke": return p.FirstStroke;
                    case "t15": return Split(p, 15);
                    default: return null;
                }
            }

            switch (anchor)
            {
                case "in5": return Split(p, -5);
                case "last_stroke": return p.LastStroke;
                case "rotation":
                    if (p.RotationStart.HasValue) return p.RotationStart;
                    if (p.Flip) return p.LastStroke.HasValue ? 0.5 * p.LastStroke : -0.4;
                    return 0.3;
                case "touch": return 0;
                case "release": return p.Flip ? (double?)null : 0.25;
                case "plant":
                    if (p.Flip) return 0;
                    return p.Pushoff.HasValue ? Math.Max(0.4, p.Pushoff.Value - 0.3) : (double?)null;
                case "pushoff": return p.Pushoff;
                case "glide": return p.Pushoff.HasValue ? p.Pushoff + 0.4 : null;
                case "underwater":
                    if (p.Kicks.Count > 0) return p.Kicks[0];
                    return p.Pushoff.HasValue ? p.Pushoff + 1.0 : null;
                case "pullout": return p.Pushoff.HasValue ? p.Pushoff + 1.5 : null;
                case "breakout": return p.Breakout;
                case "first_stroke": return p.FirstStroke;
                case "out10": return Split(p, 10);
                case "t15": return Split(p, 15);
                default:
                    v = null;
                    return v;
            }
        }

        public static long? AnchorTimestamp(SwimTechPhase p, string anchor, SwimTiming timing)
        {
            double? s = AnchorSeconds(p, anchor);
            if (!s.HasValue || timing == null || !timing.IsValid)
                return null;
            return timing.Timestamp(s.Value, p.Origin);
        }

        private static double? Split(SwimTechPhase p, int offset)
        {
            double t;
            return p != null && p.Splits.TryGetValue(offset, out t) ? (double?)t : null;
        }
        #endregion

        #region Measurements
        /// <summary>
        /// Everything the program can measure in this phase now, by check id. The times need their marks (the program does not guess a
        /// missing event); the skeleton measurements need the skeleton of the swimmer at their moment.
        /// </summary>
        public static Dictionary<string, SwimTechMeasure> Measure(SwimRace race, SwimTechPhase p, SwimTiming timing, SwimTechSheetCatalog catalog)
        {
            Dictionary<string, SwimTechMeasure> m = new Dictionary<string, SwimTechMeasure>();
            if (race == null || p == null || catalog == null)
                return m;

            Action<string, double, string, double, long?> put = (check, value, text, confidence, ts) =>
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                    return;
                SwimTechCheck c;
                catalog.Checks.TryGetValue(check, out c);
                SwimTechMeasure x = new SwimTechMeasure { Check = check, Value = value, Text = text, Confidence = confidence, Timestamp = ts };
                x.Good = c != null ? Judge(c, value) : null;
                m[check] = x;
            };

            try
            {
                MeasureTimes(race, p, timing, catalog, put);
                MeasurePose(race, p, timing, put);
            }
            catch (Exception)
            {
                // A measurement that fails leaves the others (and the coach's scores) as they are.
            }

            return m;
        }

        /// <summary>
        /// In the good range (true), out of it (false), or too near its limit to say (null). The range is the one learned from the coach's
        /// answers when there is one (see SwimTechLearning), else the catalogue's.
        /// </summary>
        public static bool? Judge(SwimTechCheck check, double value)
        {
            SwimTechCheck c = SwimTechLearning.Effective(check);
            bool? good = c.Good(value);
            if (!good.HasValue || c.Eq.HasValue)
                return good;

            // Too near the limit to say: 2 points of a percentage, 3 degrees, else 6% of the limit (at least 0.02). A limit of the skeleton
            // not learned yet from the coach's answers is wider (3 points, 5 degrees): the joints read on the video vary by that much
            // (on the underwater turns of the lab, 2026-10-28).
            bool learned = !ReferenceEquals(c, check);
            bool pose = c.Kind == "pose";
            double pct = pose && !learned ? 3.0 : 2.0, deg = pose && !learned ? 5.0 : 3.0;
            if (c.Min.HasValue && c.Max.HasValue)
            {
                // A narrow range keeps at least half of it to say "good".
                double quarter = (c.Max.Value - c.Min.Value) / 4;
                pct = Math.Min(pct, quarter);
                deg = Math.Min(deg, quarter);
            }
            // (2.15.0) A count (the dolphin kicks) is exact: one kick with at most one is good, not "too near to say".
            Func<double, bool> whole = v => Math.Abs(v - Math.Round(v)) < 1e-9;
            if (string.IsNullOrEmpty(c.Unit) && whole(value) && (!c.Min.HasValue || whole(c.Min.Value)) && (!c.Max.HasValue || whole(c.Max.Value)))
                return good;
            Func<double, bool> near = limit => Math.Abs(value - limit) <= (c.Unit == "%" ? pct : c.Unit == "°" ? deg : Math.Max(Math.Abs(limit) * Margin, 0.02));
            if ((c.Min.HasValue && near(c.Min.Value)) || (c.Max.HasValue && near(c.Max.Value)))
                return null;
            return good;
        }

        private static string Sec(double v)
        {
            return v.ToString("0.00", CultureInfo.InvariantCulture) + " s";
        }

        private static string Deg(double v)
        {
            return v.ToString("0", CultureInfo.InvariantCulture) + "°";
        }

        private static string Pct(double v)
        {
            return v.ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        private static void MeasureTimes(SwimRace race, SwimTechPhase p, SwimTiming timing, SwimTechSheetCatalog catalog, Action<string, double, string, double, long?> put)
        {
            string style = race.Style ?? "";
            Func<double, long?> at = s => timing != null && timing.IsValid ? (long?)timing.Timestamp(s, p.Origin) : null;

            if (p.Kind == SwimPhaseKind.Start)
            {
                double? t15 = Split(p, 15);
                if (t15.HasValue)
                {
                    // The level tables time the start to the head at 15 m from the signal (the coach, 2026-10-28).
                    double value = t15.Value;
                    string text = Sec(value) + SwimLang.T(" από το σήμα");
                    int? level = LevelOf(catalog, "start_15", style, race.AthleteGender, value);
                    if (level.HasValue)
                        text += " · " + SwimLang.T("επίπεδο ") + level.Value.ToString(CultureInfo.InvariantCulture);
                    put("start_15", value, text, 1.0, at(t15.Value));
                }
            }
            else
            {
                double? in5 = Split(p, -5);
                double? out10 = Split(p, 10);
                double? inOut10 = p.Value("inOut10");
                if (inOut10.HasValue)
                {
                    string text = Sec(inOut10.Value);
                    int? level = LevelOf(catalog, "turn_5_10", style, race.AthleteGender, inOut10.Value);
                    if (level.HasValue)
                        text += " · " + SwimLang.T("επίπεδο ") + level.Value.ToString(CultureInfo.InvariantCulture);
                    put("turn_5_10", inOut10.Value, text, 1.0, out10.HasValue ? at(out10.Value) : null);
                }

                // The segments of the turn: 5 m → wall → feet leave → 10 m.
                if (in5.HasValue && p.Pushoff.HasValue)
                {
                    List<string> parts = new List<string> { SwimLang.T("5 μ. → τοίχος ") + Sec(-in5.Value), SwimLang.T("τοίχος → πόδια φεύγουν ") + Sec(p.Pushoff.Value) };
                    if (out10.HasValue && out10.Value > p.Pushoff.Value)
                        parts.Add(SwimLang.T("πόδια → 10 μ. ") + Sec(out10.Value - p.Pushoff.Value));
                    put("turn_segments", -in5.Value + p.Pushoff.Value + (out10.HasValue ? Math.Max(0, out10.Value - p.Pushoff.Value) : 0), string.Join(" · ", parts.ToArray()), 1.0, at(in5.Value));
                }

                List<double> strokes = ApproachStrokes(race, p, timing);
                if (p.Flip)
                {
                    // Contact time of a flip turn: the feet on the wall (its origin) to the push-off.
                    if (p.Pushoff.HasValue && p.Pushoff.Value > 0 && p.Pushoff.Value < 1.5)
                        put("push_time", p.Pushoff.Value, Sec(p.Pushoff.Value), 1.0, at(p.Pushoff.Value));

                    // Speed of rotation: last hand entry → feet on the wall, against half a stroke cycle of the approach (one stroke of
                    // an arm to the next of the other).
                    if (p.LastStroke.HasValue && strokes.Count >= 3)
                    {
                        List<double> gaps = new List<double>();
                        for (int i = Math.Max(1, strokes.Count - 4); i < strokes.Count; i++)
                            gaps.Add(strokes[i] - strokes[i - 1]);
                        double half = Median(gaps);
                        if (half > 0.2 && half < 2.0)
                        {
                            double rot = -p.LastStroke.Value;
                            double ratio = 100.0 * rot / half;
                            put("rotation_flip", ratio, Sec(rot) + SwimLang.T(" · μισός κύκλος ") + Sec(half) + " · " + Pct(ratio), 1.0, at(p.LastStroke.Value));
                        }
                    }

                    // Dolphin kicks marked in the rotation (from the last stroke to the wall): only when some are marked there.
                    if (p.LastStroke.HasValue)
                    {
                        long from = at(p.LastStroke.Value - 0.2) ?? p.Origin, to = p.Origin;
                        int kicks = race.Marks.Count(k => k.Type == SwimMarkType.Kick && k.Timestamp >= from && k.Timestamp <= to);
                        if (kicks > 0)
                            put("kick_count_rot", kicks, kicks.ToString(CultureInfo.InvariantCulture), 1.0, null);
                    }
                }
                else if (p.Pushoff.HasValue && p.Pushoff.Value > 0 && p.Pushoff.Value < 4)
                {
                    // Open turn: the hands touch (its origin) to the feet leaving.
                    put("rotation_open", p.Pushoff.Value, Sec(p.Pushoff.Value), 1.0, at(p.Pushoff.Value));
                }

                // Stroke rate in the last 5 m against the strokes before them.
                if (in5.HasValue && strokes.Count >= 5)
                {
                    List<double> before = strokes.Where(s => s < in5.Value).ToList();
                    List<double> after = strokes.Where(s => s >= in5.Value).ToList();
                    if (before.Count >= 3 && after.Count >= 2)
                    {
                        double rb = (before[before.Count - 1] - before[0]) / (before.Count - 1);
                        double ra = (after[after.Count - 1] - after[0]) / Math.Max(1, after.Count - 1);
                        if (rb > 0 && ra > 0)
                        {
                            double ratio = 100.0 * rb / ra;
                            put("rate_speed_5m", ratio, Pct(ratio) + SwimLang.T(" της συχνότητας πριν"), 1.0, at(in5.Value));
                        }
                    }
                }
            }

            // Both: dolphin kick rate, the 15 m rule, one dolphin kick in the breaststroke pull-out.
            double? kickRate = p.Value("kickRate");
            if (kickRate.HasValue && p.Kicks.Count >= 3)
                put("kick_rate", kickRate.Value, kickRate.Value.ToString("0.0", CultureInfo.InvariantCulture) + SwimLang.T(" /δλ"), 1.0, at(p.Kicks[0]));

            if (p.BreakoutDist.HasValue && p.BreakoutDist.Value > 0)
            {
                double meters = p.BreakoutDist.Value * race.K;
                put("rule_15m", meters, meters.ToString("0.0", CultureInfo.InvariantCulture) + " m", 1.0, p.Breakout.HasValue ? at(p.Breakout.Value) : null);
            }

            if (style == "br" && p.Kicks.Count > 0)
                put("rule_one_dolphin", p.Kicks.Count, p.Kicks.Count.ToString(CultureInfo.InvariantCulture), 1.0, at(p.Kicks[0]));
        }

        /// <summary>The strokes marked in the approach of a turn (seconds from the contact, before it), in order.</summary>
        private static List<double> ApproachStrokes(SwimRace race, SwimTechPhase p, SwimTiming timing)
        {
            if (timing == null || !timing.IsValid)
                return new List<double>();

            return race.Marks.Where(k => k.Type == SwimMarkType.Stroke && k.Timestamp >= p.EventStart && k.Timestamp < p.Origin)
                .Select(k => timing.Seconds(k.Timestamp, p.Origin))
                .Where(s => s < 0 && s >= -12)
                .OrderBy(s => s)
                .ToList();
        }

        private static double Median(List<double> values)
        {
            if (values == null || values.Count == 0)
                return double.NaN;
            List<double> v = values.OrderBy(x => x).ToList();
            int n = v.Count;
            return n % 2 == 1 ? v[n / 2] : 0.5 * (v[n / 2 - 1] + v[n / 2]);
        }
        #endregion

        #region Skeleton
        /// <summary>The joints at a timestamp (median of ±2 images of the skeleton), null without a skeleton there.</summary>
        private sealed class Body
        {
            public readonly PointF[] P = new PointF[SwimJoints.Count];
            public readonly float[] V = new float[SwimJoints.Count];

            public bool Seen(int j)
            {
                return V[j] >= MinVisibility;
            }

            /// <summary>The joint of the side seen better (the left joint is given; the right one follows it).</summary>
            public int Best(int left)
            {
                return V[left] >= V[left + 1] ? left : left + 1;
            }

            /// <summary>
            /// (2.15.0) One side for a chain of joints (0: left, 1: right), the one seen better over all of them: chosen joint by joint, a
            /// shoulder–hip–ankle line could take the ankle of the other leg (legs apart at the entry: +20° for +2°).
            /// </summary>
            public int Side(params int[] lefts)
            {
                double left = 0, right = 0;
                foreach (int j in lefts)
                {
                    left += V[j];
                    right += V[j + 1];
                }

                return left >= right ? 0 : 1;
            }

            public double Confidence(params int[] joints)
            {
                return joints.Length == 0 ? 0 : joints.Min(j => V[j]);
            }

            public double? Angle(int a, int b, int c)
            {
                if (!Seen(a) || !Seen(b) || !Seen(c))
                    return null;
                double angle = SwimSignal.Angle(P[a].X, P[a].Y, P[b].X, P[b].Y, P[c].X, P[c].Y);
                return double.IsNaN(angle) ? (double?)null : angle;
            }

            /// <summary>Length of the body (shoulder to ankle of the side seen better), 0 when not seen.</summary>
            public double Length
            {
                get
                {
                    int s = Best(SwimJoints.LeftShoulder), a = Best(SwimJoints.LeftAnkle);
                    if (!Seen(s) || !Seen(a))
                        return 0;
                    return Dist(P[s], P[a]);
                }
            }

            /// <summary>The middle of two joints (left and right) where both are seen, else the one seen.</summary>
            public PointF? Mid(int left)
            {
                bool l = Seen(left), r = Seen(left + 1);
                if (l && r) return new PointF(0.5f * (P[left].X + P[left + 1].X), 0.5f * (P[left].Y + P[left + 1].Y));
                if (l) return P[left];
                if (r) return P[left + 1];
                return null;
            }
        }

        private static double Dist(PointF a, PointF b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static Body BodyAt(SwimRace race, long? ts)
        {
            if (!ts.HasValue)
                return null;

            SwimSkeleton s = race.SkeletonAt(ts.Value);
            if (s == null)
                return null;

            // (2.13.1) Only the images where the skeleton is a side view of the swimmer.
            Body b = new Body();
            if (!s.TryGet(ts.Value, 2, b.P, b.V, true))
                return null;

            // (2.15.0) In the frame of the pool: the camera's pan of a reading of the pilot (its skeletons are in image pixels) taken off,
            // as the event reader and the technical HUD do (a camera following the push-off made it read −15° for −6°). Angles in one
            // image are the same either way.
            double pan = PanAt(race, ts.Value);
            if (!double.IsNaN(pan) && pan != 0)
            {
                for (int i = 0; i < b.P.Length; i++)
                    b.P[i] = new PointF((float)(b.P[i].X - pan), b.P[i].Y);
            }

            return b;
        }

        /// <summary>(2.15.0) The camera's pan at a timestamp from the tracks of the readings with a pan; NaN when none covers it.</summary>
        private static double PanAt(SwimRace race, long timestamp)
        {
            if (race.Tracks == null)
                return double.NaN;

            foreach (SwimTrack t in race.Tracks)
            {
                if (t == null || !t.HasPan)
                    continue;
                double q = SwimTechRun.TrackPan(t, timestamp);
                if (!double.IsNaN(q))
                    return q;
            }

            return double.NaN;
        }

        /// <summary>
        /// The direction the swimmer moves along x in the image (+1 to the right, -1 to the left) between two timestamps, from the middle
        /// of the hips (0 when it cannot be told).
        /// </summary>
        private static int Direction(SwimRace race, long? from, long? to)
        {
            Body a = BodyAt(race, from), b = BodyAt(race, to);
            if (a == null || b == null)
                return 0;
            PointF? ha = a.Mid(SwimJoints.LeftHip), hb = b.Mid(SwimJoints.LeftHip);
            if (!ha.HasValue || !hb.HasValue)
                return 0;
            double dx = hb.Value.X - ha.Value.X;
            double len = Math.Max(a.Length, b.Length);
            if (len <= 0 || Math.Abs(dx) < 0.1 * len)
                return 0;
            return dx > 0 ? 1 : -1;
        }

        private static void MeasurePose(SwimRace race, SwimTechPhase p, SwimTiming timing, Action<string, double, string, double, long?> put)
        {
            if (race.Skeletons == null || race.Skeletons.Count == 0 || timing == null || !timing.IsValid)
                return;

            Func<string, long?> at = anchor => AnchorTimestamp(p, anchor, timing);
            if (p.Kind == SwimPhaseKind.Turn)
                MeasureTurnPose(race, p, timing, at, put);
            else
                MeasureStartPose(race, p, timing, at, put);
        }

        private static void MeasureTurnPose(SwimRace race, SwimTechPhase p, SwimTiming timing, Func<string, long?> at, Action<string, double, string, double, long?> put)
        {
            long? plant = at("plant");
            long? push = at("pushoff");

            // The feet on the wall (flip turn: the origin): the knee angle and the depth of the head against the feet.
            if (p.Flip && plant.HasValue)
            {
                Body b = BodyAt(race, plant);
                if (b != null)
                {
                    int side1 = b.Side(SwimJoints.LeftHip, SwimJoints.LeftKnee, SwimJoints.LeftAnkle); int hip = SwimJoints.LeftHip + side1, knee = SwimJoints.LeftKnee + side1, ankle = SwimJoints.LeftAnkle + side1;
                    double? kneeAngle = b.Angle(hip, knee, ankle);
                    if (kneeAngle.HasValue)
                        put("knee_plant", kneeAngle.Value, Deg(kneeAngle.Value), b.Confidence(hip, knee, ankle), plant);

                    double len = b.Length;
                    PointF? feet = b.Mid(SwimJoints.LeftAnkle);
                    if (len > 0 && feet.HasValue && b.Seen(SwimJoints.Nose))
                    {
                        double diff = 100.0 * Math.Abs(b.P[SwimJoints.Nose].Y - feet.Value.Y) / len;
                        put("head_feet_depth", diff, Pct(diff), b.Confidence(SwimJoints.Nose, b.Best(SwimJoints.LeftAnkle)), plant);
                    }
                }
            }

            // Open turn: the elbows at the touch.
            if (!p.Flip)
            {
                long? touch = at("touch");
                Body b = BodyAt(race, touch);
                if (b != null)
                {
                    int side2 = b.Side(SwimJoints.LeftShoulder, SwimJoints.LeftElbow, SwimJoints.LeftWrist); int s = SwimJoints.LeftShoulder + side2, e = SwimJoints.LeftElbow + side2, w = SwimJoints.LeftWrist + side2;
                    double? elbow = b.Angle(s, e, w);
                    if (elbow.HasValue)
                        put("elbow_touch", elbow.Value, Deg(elbow.Value), b.Confidence(s, e, w), touch);
                }
            }

            // The push: the streamline at the plant and at the push-off, the line of the body, the direction of the glide.
            if (push.HasValue)
            {
                long? later = timing.Timestamp((p.Pushoff ?? 0) + 0.35, p.Origin);
                int dir = Direction(race, push, later);
                Body pl = BodyAt(race, plant);
                if (pl != null)
                {
                    Streamline(pl, dir, plant, "streamline_plant", put);
                    ArmTrunkLine(pl, plant, "body_line_plant", put);
                }

                Body b = BodyAt(race, push);
                if (b != null)
                {
                    Streamline(b, dir, push, "streamline", put);
                    BodyLine(b, push, "body_line", put);
                }

                long? glide = at("glide");
                Body g = BodyAt(race, glide);
                if (g != null)
                    BodyLine(g, glide, "body_line_glide", put);

                Body c = BodyAt(race, later);
                if (b != null && c != null && dir != 0)
                {
                    PointF? h0 = b.Mid(SwimJoints.LeftHip), h1 = c.Mid(SwimJoints.LeftHip);
                    if (h0.HasValue && h1.HasValue)
                    {
                        double dx = (h1.Value.X - h0.Value.X) * dir, dy = h1.Value.Y - h0.Value.Y;
                        if (dx > 0.1 * Math.Max(1, b.Length))
                        {
                            double angle = Math.Atan2(-dy, dx) * 180 / Math.PI;
                            double conf = Math.Min(b.Confidence(b.Best(SwimJoints.LeftHip)), c.Confidence(c.Best(SwimJoints.LeftHip)));
                            string text = Deg(angle) + (angle > 2 ? SwimLang.T(" προς τα πάνω") : angle < -2 ? SwimLang.T(" προς τα κάτω") : SwimLang.T(" οριζόντια"));
                            put("push_dir", angle, text, conf, push);
                            put("push_dir_back", angle, text, conf, push);
                            put("push_dir_breast", angle, text, conf, push);
                        }
                    }
                }
            }

            // Flip turn: the hips keep going to the wall from the last stroke to the feet on the wall.
            if (p.Flip && p.LastStroke.HasValue)
            {
                long a = timing.Timestamp(p.LastStroke.Value, p.Origin);
                int dir = Direction(race, timing.Timestamp(p.LastStroke.Value - 0.8, p.Origin), a);
                if (dir != 0)
                {
                    double worst = double.MaxValue, len = 0;
                    int seen = 0;
                    PointF? prev = null;
                    for (double s = p.LastStroke.Value; s <= 0.001; s += 0.1)
                    {
                        Body b = BodyAt(race, timing.Timestamp(s, p.Origin));
                        PointF? h = b != null ? b.Mid(SwimJoints.LeftHip) : null;
                        if (!h.HasValue)
                            continue;
                        len = Math.Max(len, b.Length);
                        if (prev.HasValue)
                        {
                            worst = Math.Min(worst, (h.Value.X - prev.Value.X) * dir);
                            seen++;
                        }
                        prev = h;
                    }

                    if (seen >= 3 && len > 0)
                    {
                        // Backwards by more than a twentieth of the body in a tenth of a second: the hips stalled or went back.
                        double back = 100.0 * worst / len;
                        put("hips_to_wall", back, back >= -5 ? SwimLang.T("προς τον τοίχο") : SwimLang.T("πίσω ") + Pct(-back), 0.6, a);
                    }
                }
            }

            UnderwaterKicks(race, p, timing, put);
        }

        /// <summary>The streamline at a moment: elbows straight, the hands in front of the head, the head between the arms.</summary>
        private static void Streamline(Body b, int dir, long? ts, string check, Action<string, double, string, double, long?> put)
        {
            int side3 = b.Side(SwimJoints.LeftShoulder, SwimJoints.LeftElbow, SwimJoints.LeftWrist); int s = SwimJoints.LeftShoulder + side3, e = SwimJoints.LeftElbow + side3, w = SwimJoints.LeftWrist + side3;
            double? elbow = b.Angle(s, e, w);
            if (!elbow.HasValue)
                return;

            bool ahead = true;
            if (dir != 0 && b.Seen(SwimJoints.Nose))
                ahead = (b.P[w].X - b.P[SwimJoints.Nose].X) * dir > 0;

            // Head between the arms: the nose near the line shoulder → wrist, within one upper arm (a neutral head sits about 0.7 of it
            // to the chest side: 0.69 median, 0.85 for 9 in 10 frames in a demonstration of underwater dolphin kicks; 0.45 failed 4 in
            // 10 frames of the glides of the lab's underwater turns, 2026-10-28). A lifted head is the head angle's (HeadBelow).
            bool between = true;
            if (b.Seen(SwimJoints.Nose))
            {
                double arm = Dist(b.P[s], b.P[e]);
                double d = LineDistance(b.P[SwimJoints.Nose], b.P[s], b.P[w]);
                between = arm > 0 && d <= arm * 1.0;
            }

            bool good = elbow.Value >= 150 && ahead && between;
            string text = SwimLang.T("αγκώνας ") + Deg(elbow.Value) + (ahead ? "" : SwimLang.T(" · χέρια όχι μπροστά")) + (between ? "" : SwimLang.T(" · κεφάλι έξω από τα χέρια"));
            put(check, good ? 1 : 0, text, b.Confidence(s, e, w), ts);
        }

        /// <summary>
        /// The line shoulders–hips–ankles at a moment: its bend from a straight line. (2.14.1) Without the hands: at the hand-marked
        /// push-offs of the lab's underwater turns (side views) the trunk and the legs read 7–11° from a straight line (median), the arms
        /// 47–128°; the arms are judged by the streamline's checks.
        /// </summary>
        private static void BodyLine(Body b, long? ts, string check, Action<string, double, string, double, long?> put)
        {
            int side4 = b.Side(SwimJoints.LeftShoulder, SwimJoints.LeftHip, SwimJoints.LeftAnkle); int s = SwimJoints.LeftShoulder + side4, h = SwimJoints.LeftHip + side4, a = SwimJoints.LeftAnkle + side4;
            double? atHip = b.Angle(s, h, a);
            if (!atHip.HasValue)
                return;
            double dev = 180 - atHip.Value;
            put(check, dev, Deg(dev) + SwimLang.T(" από την ευθεία"), b.Confidence(s, h, a), ts);
        }

        /// <summary>The line hand–shoulder–hip at a moment (the legs are bent on the wall): its bend from a straight line.</summary>
        private static void ArmTrunkLine(Body b, long? ts, string check, Action<string, double, string, double, long?> put)
        {
            int side5 = b.Side(SwimJoints.LeftWrist, SwimJoints.LeftShoulder, SwimJoints.LeftHip); int w = SwimJoints.LeftWrist + side5, s = SwimJoints.LeftShoulder + side5, h = SwimJoints.LeftHip + side5;
            double? atShoulder = b.Angle(w, s, h);
            if (!atShoulder.HasValue)
                return;
            double dev = 180 - atShoulder.Value;
            put(check, dev, Deg(dev) + SwimLang.T(" από την ευθεία"), b.Confidence(w, s, h), ts);
        }

        private static double LineDistance(PointF p, PointF a, PointF b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6)
                return Dist(p, a);
            return Math.Abs((p.X - a.X) * dy - (p.Y - a.Y) * dx) / len;
        }

        /// <summary>The dolphin kicks marked after the push (or the entry): the feet together, the size of the kick from the first to the last.</summary>
        private static void UnderwaterKicks(SwimRace race, SwimTechPhase p, SwimTiming timing, Action<string, double, string, double, long?> put)
        {
            if (p.Kicks.Count < 3)
                return;

            List<double> gaps = new List<double>(), sizes = new List<double>();
            double conf = 1;
            int counted = 0;
            // (2.15.0) Every kick over a whole cycle (from the kick before it): the first one had 0.25 s only (the first upbeat out of the
            // glide), so the growth late / early read 160–280% with kicks of the same size (and "good" with kicks getting smaller).
            for (int i = 1; i < p.Kicks.Count; i++)
            {
                double from = p.Kicks[i - 1], to = p.Kicks[i];
                double top = double.MinValue, bottom = double.MaxValue, len = 0;
                for (double s = from; s <= to + 1e-6; s += 0.04)
                {
                    Body b = BodyAt(race, timing.Timestamp(s, p.Origin));
                    if (b == null)
                        continue;
                    int ankle = b.Best(SwimJoints.LeftAnkle);
                    PointF? hip = b.Mid(SwimJoints.LeftHip);
                    if (!b.Seen(ankle) || !hip.HasValue)
                        continue;
                    double rel = b.P[ankle].Y - hip.Value.Y;
                    top = Math.Max(top, rel);
                    bottom = Math.Min(bottom, rel);
                    len = Math.Max(len, b.Length);
                    conf = Math.Min(conf, b.V[ankle]);
                    if (b.Seen(SwimJoints.LeftAnkle) && b.Seen(SwimJoints.RightAnkle) && b.Length > 0)
                        gaps.Add(100.0 * Dist(b.P[SwimJoints.LeftAnkle], b.P[SwimJoints.RightAnkle]) / b.Length);
                }

                if (len > 0 && top > bottom)
                {
                    sizes.Add(100.0 * (top - bottom) / len);
                    counted++;
                }
            }

            long? first = timing.Timestamp(p.Kicks[0], p.Origin);
            if (gaps.Count >= 5)
            {
                double together = Median(gaps);
                put("legs_together", together, Pct(together) + SwimLang.T(" του σώματος"), conf, first);
            }

            if (counted >= 3)
            {
                int k = Math.Max(1, counted / 3);
                double early = sizes.Take(k).Average(), late = sizes.Skip(counted - k).Average();
                if (early > 0)
                {
                    double growth = 100.0 * late / early;
                    put("kick_amp_growth", growth, Pct(growth) + SwimLang.T(" (τελευταίες / πρώτες)"), conf, first);
                }
            }
        }

        private static void MeasureStartPose(SwimRace race, SwimTechPhase p, SwimTiming timing, Func<string, long?> at, Action<string, double, string, double, long?> put)
        {
            long? set = at("set"), takeoff = at("takeoff"), entry = at("hand_entry"), mid = at("midflight");
            int dir = Direction(race, set, takeoff.HasValue ? takeoff : entry);
            if (dir == 0)
                dir = Direction(race, set, entry);
            bool back = race.Style == "bk";

            // Set position: hips above the shoulders, the front knee, the arms long, the hips in front of the back foot.
            Body s = BodyAt(race, set);
            if (s != null && s.Length > 0 && !back)
            {
                PointF? hip = s.Mid(SwimJoints.LeftHip), sh = s.Mid(SwimJoints.LeftShoulder);
                if (hip.HasValue && sh.HasValue)
                {
                    double up = 100.0 * (sh.Value.Y - hip.Value.Y) / s.Length;
                    put("hips_vs_shoulders", up, Pct(up), s.Confidence(s.Best(SwimJoints.LeftHip), s.Best(SwimJoints.LeftShoulder)), set);
                }

                int side6 = s.Side(SwimJoints.LeftElbow, SwimJoints.LeftWrist, SwimJoints.LeftShoulder); int e = SwimJoints.LeftElbow + side6, w = SwimJoints.LeftWrist + side6, sj = SwimJoints.LeftShoulder + side6;
                double? elbow = s.Angle(sj, e, w);
                if (elbow.HasValue)
                    put("set_elbow", elbow.Value, Deg(elbow.Value), s.Confidence(sj, e, w), set);

                if (dir != 0 && s.Seen(SwimJoints.LeftAnkle) && s.Seen(SwimJoints.RightAnkle))
                {
                    // The front leg: its ankle further in the direction of the dive.
                    bool leftFront = (s.P[SwimJoints.LeftAnkle].X - s.P[SwimJoints.RightAnkle].X) * dir > 0;
                    int hipF = leftFront ? SwimJoints.LeftHip : SwimJoints.RightHip, kneeF = leftFront ? SwimJoints.LeftKnee : SwimJoints.RightKnee,
                        ankleF = leftFront ? SwimJoints.LeftAnkle : SwimJoints.RightAnkle, ankleB = leftFront ? SwimJoints.RightAnkle : SwimJoints.LeftAnkle;
                    double? knee = s.Angle(hipF, kneeF, ankleF);
                    if (knee.HasValue)
                        put("front_knee_set", knee.Value, Deg(knee.Value), s.Confidence(hipF, kneeF, ankleF), set);

                    if (hip.HasValue)
                    {
                        double ahead = 100.0 * (hip.Value.X - s.P[ankleB].X) * dir / s.Length;
                        bool front = ahead > 0;
                        put("set_hips_front", front ? 1 : 0, front ? SwimLang.T("ισχία μπροστά από το πίσω πόδι") : SwimLang.T("ισχία πίσω από το πίσω πόδι"),
                            Math.Min(s.Confidence(s.Best(SwimJoints.LeftHip)), s.V[ankleB]), set);
                    }
                }
            }

            // Take-off: the rear leg up to the hip line, the shoulders horizontal.
            Body t = BodyAt(race, takeoff);
            if (t != null && t.Length > 0 && !back)
            {
                PointF? hip = t.Mid(SwimJoints.LeftHip), sh = t.Mid(SwimJoints.LeftShoulder);
                if (dir != 0 && t.Seen(SwimJoints.LeftAnkle) && t.Seen(SwimJoints.RightAnkle) && hip.HasValue)
                {
                    bool leftRear = (t.P[SwimJoints.LeftAnkle].X - t.P[SwimJoints.RightAnkle].X) * dir < 0;
                    int ankleR = leftRear ? SwimJoints.LeftAnkle : SwimJoints.RightAnkle;
                    double up = 100.0 * (hip.Value.Y - t.P[ankleR].Y) / t.Length;
                    put("rear_leg_height", up, Pct(up) + (up >= 0 ? SwimLang.T(" πάνω από τα ισχία") : SwimLang.T(" κάτω από τα ισχία")), t.V[ankleR], takeoff);
                }

                if (dir != 0 && hip.HasValue && sh.HasValue)
                {
                    double dx = (sh.Value.X - hip.Value.X) * dir, dy = hip.Value.Y - sh.Value.Y;
                    if (dx > 0)
                    {
                        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
                        put("shoulders_takeoff", angle, Deg(angle), t.Confidence(t.Best(SwimJoints.LeftHip), t.Best(SwimJoints.LeftShoulder)), takeoff);
                    }
                }
            }

            // The arms bend before the take-off (the smallest elbow angle from the signal to the take-off).
            if (p.Block.HasValue && p.Block.Value > 0.2 && !back)
            {
                double best = double.MaxValue, conf = 0;
                long? when = null;
                for (double x = 0.05; x <= p.Block.Value + 1e-6; x += 0.04)
                {
                    long ts = timing.Timestamp(x, p.Origin);
                    Body b = BodyAt(race, ts);
                    if (b == null)
                        continue;
                    int side7 = b.Side(SwimJoints.LeftShoulder, SwimJoints.LeftElbow, SwimJoints.LeftWrist); int sj = SwimJoints.LeftShoulder + side7, e = SwimJoints.LeftElbow + side7, w = SwimJoints.LeftWrist + side7;
                    double? a = b.Angle(sj, e, w);
                    if (a.HasValue && a.Value < best)
                    {
                        best = a.Value;
                        conf = b.Confidence(sj, e, w);
                        when = ts;
                    }
                }

                if (best < double.MaxValue)
                    put("arms_flex_takeoff", best, Deg(best), conf, when);
            }

            // The lead leg: from the take-off until the lower ankle reaches the height of the hips (before the hands enter).
            if (!back && p.Block.HasValue && p.Entry.HasValue && p.Entry.Value > p.Block.Value)
                LeadLeg(race, p, timing, put);

            // Flight: the legs together, the line of the body, the head, the arms ahead; before the entry the streamline.
            Body m = BodyAt(race, mid);
            if (m != null)
            {
                double? split = ThighAngle(m);
                if (split.HasValue)
                    put("legs_split", split.Value, Deg(split.Value), m.Confidence(SwimJoints.LeftKnee, SwimJoints.RightKnee), mid);

                if (!back && dir != 0)
                {
                    double? head = HeadBelow(m, dir);
                    if (head.HasValue)
                    {
                        double conf = m.Confidence(SwimJoints.Nose, m.Best(SwimJoints.LeftShoulder), m.Best(SwimJoints.LeftHip));
                        put("head_spine_flight", head.Value, HeadText(head.Value), conf, mid);
                        put("head_drop_flight", head.Value, HeadText(head.Value), conf, mid);
                    }

                    int side8 = m.Side(SwimJoints.LeftShoulder, SwimJoints.LeftHip, SwimJoints.LeftAnkle); int sj = SwimJoints.LeftShoulder + side8, h = SwimJoints.LeftHip + side8, a = SwimJoints.LeftAnkle + side8;
                    double? line = m.Angle(sj, h, a);
                    if (line.HasValue)
                        put("flight_line", 180 - line.Value, Deg(180 - line.Value) + SwimLang.T(" από την ευθεία"), m.Confidence(sj, h, a), mid);

                    ArmsAhead(m, dir, mid, "arms_flight", put);
                }

                if (back && dir != 0)
                {
                    // Backstroke: the arch of the back, the legs below the line of the trunk (up in the image is the back of the swimmer).
                    double? legs = LegsUp(m, dir);
                    if (legs.HasValue)
                        put("back_arch", -legs.Value, Deg(-legs.Value), m.Confidence(m.Best(SwimJoints.LeftShoulder), m.Best(SwimJoints.LeftHip), m.Best(SwimJoints.LeftAnkle)), mid);
                }
            }

            long? pre = at("pre_entry");
            Body pe = BodyAt(race, pre);
            if (pe != null)
                Streamline(pe, dir, pre, "entry_streamline", put);

            // The legs as the hands enter.
            Body he = BodyAt(race, entry);
            if (he != null)
            {
                double? split = ThighAngle(he);
                if (split.HasValue)
                    put("legs_split_entry", split.Value, Deg(split.Value), he.Confidence(SwimJoints.LeftKnee, SwimJoints.RightKnee), entry);
            }

            if (p.Entry.HasValue && dir != 0)
            {
                if (!back)
                    EntryHeadAndArch(race, p, timing, dir, put);
                HipsPeak(race, p, timing, put);
            }
        }

        /// <summary>
        /// The head as a turn around the shoulder: the angle (degrees) of shoulder → nose from the line of the trunk (hip → shoulder → on),
        /// positive down in the image (towards the chest of a swimmer facing down). With the arms in front the shoulder sits beside the head
        /// and the nose about level with it: a neutral head is about 75° (73° median, 52°–88° for 8 in 10 frames, in a demonstration of
        /// underwater dolphin kicks on the front; 85° on the back), the head lifted to look forward turns it down towards 0°, the chin to
        /// the chest turns it past 90°. Null when not seen.
        /// </summary>
        private static double? HeadBelow(Body b, int dir)
        {
            int side9 = b.Side(SwimJoints.LeftShoulder, SwimJoints.LeftHip); int s = SwimJoints.LeftShoulder + side9, h = SwimJoints.LeftHip + side9;
            if (dir == 0 || !b.Seen(s) || !b.Seen(h) || !b.Seen(SwimJoints.Nose))
                return null;
            return SideAngle(b.P[h], b.P[s], b.P[s], b.P[SwimJoints.Nose], dir);
        }

        /// <summary>The head angle as a text: degrees, and lifted / neutral / to the chest (neutral about 75°).</summary>
        private static string HeadText(double v)
        {
            if (v < 40)
                return Deg(v) + SwimLang.T(" · σηκωμένο") + SwimLang.T(" (ουδέτερο ~75°)");
            if (v > 110)
                return Deg(v) + SwimLang.T(" · προς το στήθος") + SwimLang.T(" (ουδέτερο ~75°)");
            return Deg(v) + SwimLang.T(" · ουδέτερο");
        }

        /// <summary>The angle (degrees) of the legs (hip → ankle) from the line of the trunk (shoulder → hip → on), positive up in the image.</summary>
        private static double? LegsUp(Body b, int dir)
        {
            int side10 = b.Side(SwimJoints.LeftShoulder, SwimJoints.LeftHip, SwimJoints.LeftAnkle); int s = SwimJoints.LeftShoulder + side10, h = SwimJoints.LeftHip + side10, a = SwimJoints.LeftAnkle + side10;
            if (dir == 0 || !b.Seen(s) || !b.Seen(h) || !b.Seen(a))
                return null;
            return SideAngle(b.P[s], b.P[h], b.P[h], b.P[a], dir);
        }

        /// <summary>
        /// The signed angle (degrees) of the direction a → b from the direction from → to: positive when it turns clockwise on the screen
        /// for a swimmer moving right (dir 1), mirrored for one moving left (dir -1). From a line pointing the way the swimmer moves
        /// (hip → shoulder) positive is down in the image; from a line pointing back (shoulder → hip) positive is up.
        /// </summary>
        private static double SideAngle(PointF from, PointF to, PointF a, PointF b, int dir)
        {
            double ux = to.X - from.X, uy = to.Y - from.Y, vx = b.X - a.X, vy = b.Y - a.Y;
            double cross = ux * vy - uy * vx, dot = ux * vx + uy * vy;
            return Math.Atan2(cross, dot) * 180 / Math.PI * dir;
        }

        /// <summary>The arms straight and ahead of the head (in the direction of the swimmer): 1 yes, 0 no.</summary>
        private static void ArmsAhead(Body b, int dir, long? ts, string check, Action<string, double, string, double, long?> put)
        {
            int side11 = b.Side(SwimJoints.LeftShoulder, SwimJoints.LeftElbow, SwimJoints.LeftWrist); int s = SwimJoints.LeftShoulder + side11, e = SwimJoints.LeftElbow + side11, w = SwimJoints.LeftWrist + side11;
            double? elbow = b.Angle(s, e, w);
            if (!elbow.HasValue || dir == 0)
                return;
            bool ahead = !b.Seen(SwimJoints.Nose) || (b.P[w].X - b.P[SwimJoints.Nose].X) * dir > 0;
            bool good = elbow.Value >= 150 && ahead;
            put(check, good ? 1 : 0, SwimLang.T("αγκώνας ") + Deg(elbow.Value) + (ahead ? "" : SwimLang.T(" · χέρια όχι μπροστά")), b.Confidence(s, e, w), ts);
        }

        /// <summary>Time from the take-off until the lower ankle reaches the height of the hips (not before the hands enter: 1 s).</summary>
        private static void LeadLeg(SwimRace race, SwimTechPhase p, SwimTiming timing, Action<string, double, string, double, long?> put)
        {
            double conf = 1;
            int seen = 0;
            for (double x = p.Block.Value; x <= p.Entry.Value + 1e-6; x += 0.02)
            {
                long ts = timing.Timestamp(x, p.Origin);
                Body b = BodyAt(race, ts);
                PointF? hip = b != null ? b.Mid(SwimJoints.LeftHip) : null;
                if (b == null || !hip.HasValue || !b.Seen(SwimJoints.LeftAnkle) || !b.Seen(SwimJoints.RightAnkle) || b.Length <= 0)
                    continue;
                seen++;
                conf = Math.Min(conf, Math.Min(b.V[SwimJoints.LeftAnkle], b.V[SwimJoints.RightAnkle]));
                double lower = Math.Max(b.P[SwimJoints.LeftAnkle].Y, b.P[SwimJoints.RightAnkle].Y);
                if (lower <= hip.Value.Y + 0.03 * b.Length)
                {
                    double after = x - p.Block.Value;
                    put("lead_leg_hip", after, Sec(after) + SwimLang.T(" από την απογείωση"), conf, ts);
                    return;
                }
            }

            // Seen through the flight and never there: as late as can be.
            if (seen >= 5)
                put("lead_leg_hip", 1.0, SwimLang.T("δεν φτάνει ως την είσοδο"), conf, timing.Timestamp(p.Entry.Value, p.Origin));
        }

        /// <summary>
        /// The entry, from the hands in to the feet in: the head (head_lift_entry: the middle head angle of the images of the entry where
        /// the trunk and the head are read in proportion; neutral about 75°) and, as the hips enter, the legs above the line of the trunk
        /// (entry_arch, the "banana"). In a demonstration of good underwater dolphin kicks, single images and the images of the swimmer
        /// coming into the view gave "lifted" heads; with these two rules 2 in 100 windows of 0.4 s were still called lifted (5 in 100
        /// without them), all where the swimmer came into the view.
        /// </summary>
        private static void EntryHeadAndArch(SwimRace race, SwimTechPhase p, SwimTiming timing, int dir, Action<string, double, string, double, long?> put)
        {
            List<double[]> heads = new List<double[]>();
            double conf = 1;
            for (double x = p.Entry.Value; x <= p.Entry.Value + 0.4 + 1e-6; x += 0.02)
            {
                long ts = timing.Timestamp(x, p.Origin);
                Body b = BodyAt(race, ts);
                if (b == null)
                    continue;
                double? head = HeadBelow(b, dir);
                if (!head.HasValue)
                    continue;
                int side12 = b.Side(SwimJoints.LeftShoulder, SwimJoints.LeftHip); int sj = SwimJoints.LeftShoulder + side12, hj = SwimJoints.LeftHip + side12;
                heads.Add(new double[] { head.Value, Dist(b.P[sj], b.P[hj]), Dist(b.P[sj], b.P[SwimJoints.Nose]), ts,
                    b.Confidence(SwimJoints.Nose, sj, hj) });
            }

            if (heads.Count >= 6)
            {
                // The trunk about as long as in the other images, the nose at a third of the trunk from the shoulder (0.15–0.6).
                double trunk = Median(heads.Select(v => v[1]).ToList());
                List<double[]> ok = heads.Where(v => v[1] >= 0.6 * trunk && v[1] <= 1.5 * trunk && v[2] >= 0.15 * v[1] && v[2] <= 0.6 * v[1])
                    .OrderBy(v => v[0]).ToList();
                if (ok.Count >= 6)
                {
                    double[] middle = ok[(ok.Count - 1) / 2];
                    conf = ok.Min(v => v[4]);
                    put("head_lift_entry", middle[0], HeadText(middle[0]), conf, (long)middle[3]);
                }
            }

            long hips = timing.Timestamp(p.Entry.Value + 0.12, p.Origin);
            Body h = BodyAt(race, hips);
            double? legs = h != null ? LegsUp(h, dir) : null;
            if (legs.HasValue)
                put("entry_arch", legs.Value, Deg(legs.Value) + (legs.Value > 0 ? SwimLang.T(" πάνω από τη γραμμή") : SwimLang.T(" κάτω από τη γραμμή")),
                    h.Confidence(h.Best(SwimJoints.LeftShoulder), h.Best(SwimJoints.LeftHip), h.Best(SwimJoints.LeftAnkle)), hips);
        }

        /// <summary>When the hips are at their highest in the flight, from the hand entry (negative: before it).</summary>
        private static void HipsPeak(SwimRace race, SwimTechPhase p, SwimTiming timing, Action<string, double, string, double, long?> put)
        {
            double from = p.Block.HasValue ? p.Block.Value : Math.Max(0, p.Entry.Value - 0.6);
            double top = double.MaxValue, conf = 1;
            double? when = null;
            int seen = 0;
            for (double x = from; x <= p.Entry.Value + 0.2 + 1e-6; x += 0.02)
            {
                Body b = BodyAt(race, timing.Timestamp(x, p.Origin));
                PointF? hip = b != null ? b.Mid(SwimJoints.LeftHip) : null;
                if (!hip.HasValue)
                    continue;
                seen++;
                conf = Math.Min(conf, b.Confidence(b.Best(SwimJoints.LeftHip)));
                if (hip.Value.Y < top)
                {
                    top = hip.Value.Y;
                    when = x;
                }
            }

            if (seen >= 5 && when.HasValue)
            {
                double rel = when.Value - p.Entry.Value;
                put("hips_peak", rel, (rel >= 0 ? "+" : "") + Sec(rel) + SwimLang.T(" από την είσοδο των χεριών"), conf, timing.Timestamp(when.Value, p.Origin));
            }
        }

        /// <summary>The angle between the two thighs (hip → knee), null when both are not seen.</summary>
        private static double? ThighAngle(Body b)
        {
            if (!b.Seen(SwimJoints.LeftHip) || !b.Seen(SwimJoints.RightHip) || !b.Seen(SwimJoints.LeftKnee) || !b.Seen(SwimJoints.RightKnee))
                return null;
            double ax = b.P[SwimJoints.LeftKnee].X - b.P[SwimJoints.LeftHip].X, ay = b.P[SwimJoints.LeftKnee].Y - b.P[SwimJoints.LeftHip].Y;
            double bx = b.P[SwimJoints.RightKnee].X - b.P[SwimJoints.RightHip].X, by = b.P[SwimJoints.RightKnee].Y - b.P[SwimJoints.RightHip].Y;
            double na = Math.Sqrt(ax * ax + ay * ay), nb = Math.Sqrt(bx * bx + by * by);
            if (na < 1 || nb < 1)
                return null;
            double c = Math.Max(-1, Math.Min(1, (ax * bx + ay * by) / (na * nb)));
            return Math.Acos(c) * 180 / Math.PI;
        }
        #endregion

        #region Proposals, totals, levels
        /// <summary>
        /// The score the program proposes for a criterion from its measurement: 1 (done well), 0 (not), null (no proposal: no
        /// measurement, no range, or too near the limit, or not seen well). On a list of errors 1 means the error is there.
        /// A criterion the program measures (A) is proposed unless the coach's answers disagree with it (fewer than 7 in 10); one it
        /// measures in part (P) only once they agree (9 in 10 of at least 10 answers); one the coach judges (X) never.
        /// </summary>
        public static int? Proposal(SwimTechCriterion criterion, SwimTechMeasure measure, bool errorList, SwimTechAgreement agreement = null)
        {
            if (criterion == null || measure == null || !measure.Good.HasValue || criterion.Level == "X")
                return null;
            if (measure.Confidence < MinVisibility)
                return null;
            if ((criterion.Level == "P" || NeedsAgreement.Contains(criterion.Check ?? "")) && (agreement == null || !agreement.Reliable))
                return null;
            if (criterion.Level == "A" && agreement != null && agreement.Disagrees)
                return null;

            bool good = measure.Good.Value;
            return errorList ? (good ? 0 : 1) : (good ? 1 : 0);
        }

        /// <summary>The totals of a sheet (all its phases): done well, scored, criteria.</summary>
        public static void Totals(SwimTechSheet sheet, SwimTechEval eval, out int good, out int scored, out int total)
        {
            good = scored = total = 0;
            if (sheet == null)
                return;
            foreach (SwimTechSheetPhase ph in sheet.Phases)
            {
                int g, s, t;
                PhaseTotals(ph, eval, out g, out s, out t);
                good += g;
                scored += s;
                total += t;
            }
        }

        public static void PhaseTotals(SwimTechSheetPhase phase, SwimTechEval eval, out int good, out int scored, out int total)
        {
            good = scored = 0;
            total = phase != null ? phase.Items.Count : 0;
            if (phase == null || eval == null)
                return;
            foreach (SwimTechSheetItem it in phase.Items)
            {
                SwimTechScore sc;
                if (!eval.Scores.TryGetValue(it.Key, out sc) || sc.Value < 0)
                    continue;
                scored++;
                if (sc.Value == 1)
                    good++;
            }
        }

        /// <summary>"M" or "F" from the gender of the athlete ("Ανδρών", "Γυναικών", "Αγόρια", "Κορίτσια", "M", "F"), null when unknown.</summary>
        public static string GenderCode(string gender)
        {
            string g = (gender ?? "").Trim().ToLowerInvariant();
            if (g.Length == 0)
                return null;
            if (g.StartsWith("γυν") || g.StartsWith("κορ") || g.StartsWith("θηλ") || g == "f" || g.StartsWith("fem") || g.StartsWith("wom") || g == "w" || g.StartsWith("girl"))
                return "F";
            if (g.StartsWith("ανδ") || g.StartsWith("αγ") || g.StartsWith("αρρ") || g == "m" || g.StartsWith("mal") || g.StartsWith("men") || g.StartsWith("boy"))
                return "M";
            return null;
        }

        /// <summary>The level (1 = best) of a time in a table of the catalogue for the style and the gender of the athlete.</summary>
        public static int? LevelOf(SwimTechSheetCatalog catalog, string table, string style, string gender, double value)
        {
            SwimTechLevelTable t;
            if (catalog == null || !catalog.Levels.TryGetValue(table, out t))
                return null;
            string g = GenderCode(gender);
            return g != null ? t.Level(style, g, value) : null;
        }
        #endregion
    }
}
