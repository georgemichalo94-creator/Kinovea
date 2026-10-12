#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Skeleton of a reading for the detectors: the joints of each sample smoothed as on the video (median of the samples
    /// around where the joint is seen), NaN where the swimmer or the joint is not seen.
    /// </summary>
    public sealed class SwimPoseSeries
    {
        public readonly int Count;
        public readonly double SamplesPerSecond;
        public readonly float[,] X;
        public readonly float[,] Y;

        /// <summary>0–1, the best of the sample and its neighbours.</summary>
        public readonly float[,] V;

        public SwimPoseSeries(int count, double samplesPerSecond)
        {
            Count = count;
            SamplesPerSecond = samplesPerSecond;
            X = new float[count, SwimJoints.Count];
            Y = new float[count, SwimJoints.Count];
            V = new float[count, SwimJoints.Count];
        }

        /// <summary>From the values of a skeleton part (x, y, visibility 0–100 per joint and sample).</summary>
        public static SwimPoseSeries FromData(IList<int> data, double samplesPerSecond, int smooth = 2)
        {
            int n = data.Count / SwimSkeleton.Values;
            SwimPoseSeries s = new SwimPoseSeries(n, samplesPerSecond);
            Func<int, bool> seen = i =>
            {
                int o = i * SwimSkeleton.Values;
                for (int j = 0; j < SwimJoints.Count; j++)
                {
                    if (data[o + j * 3 + 2] > 0)
                        return true;
                }

                return false;
            };

            List<float> xs = new List<float>(), ys = new List<float>();
            for (int i = 0; i < n; i++)
            {
                bool usable = seen(i) || (i > 0 && i + 1 < n && seen(i - 1) && seen(i + 1));
                for (int j = 0; j < SwimJoints.Count; j++)
                {
                    s.X[i, j] = float.NaN;
                    s.Y[i, j] = float.NaN;
                    s.V[i, j] = 0;
                    if (!usable)
                        continue;

                    xs.Clear();
                    ys.Clear();
                    float best = 0;
                    for (int k = Math.Max(0, i - smooth); k <= Math.Min(n - 1, i + smooth); k++)
                    {
                        int o = k * SwimSkeleton.Values + j * 3;
                        int v = data[o + 2];
                        if (v < 30)
                            continue;

                        xs.Add(data[o]);
                        ys.Add(data[o + 1]);
                        if (Math.Abs(k - i) <= 1)
                            best = Math.Max(best, v / 100f);
                    }

                    if (xs.Count == 0)
                        continue;

                    xs.Sort();
                    ys.Sort();
                    s.X[i, j] = xs[xs.Count / 2];
                    s.Y[i, j] = ys[ys.Count / 2];
                    s.V[i, j] = best;
                }
            }

            return s;
        }

        public bool Has(int i, int j, float visibility = 0)
        {
            return i >= 0 && i < Count && !float.IsNaN(X[i, j]) && V[i, j] >= visibility;
        }

        public PointF P(int i, int j)
        {
            return new PointF(X[i, j], Y[i, j]);
        }
    }

    /// <summary>
    /// The image around the swimmer at the start (block and swimmer), kept while the skeleton is read: a few seconds of
    /// small colour crops, and the person mask of the pose model in the same crop.
    /// </summary>
    public sealed class SwimRegionFrames
    {
        /// <summary>Part of the reference image (pixels).</summary>
        public RectangleF Region;

        /// <summary>Pixels of the crop per reference pixel.</summary>
        public float Scale;

        public int Width;
        public int Height;

        /// <summary>Video timestamp of each crop (increasing).</summary>
        public readonly List<long> Timestamps = new List<long>();

        /// <summary>Sample of the skeleton of each crop (see Align).</summary>
        public readonly List<int> Samples = new List<int>();

        /// <summary>BGR, rows of Width × 3 bytes.</summary>
        public readonly List<byte[]> Pixels = new List<byte[]>();

        /// <summary>1 where the pose model sees the person, null when it did not see him in that image.</summary>
        public readonly List<byte[]> Masks = new List<byte[]>();

        public int Count
        {
            get { return Samples.Count; }
        }

        /// <summary>Index of the crop of a sample, −1 if none.</summary>
        public int IndexOf(int sample)
        {
            int i = Samples.BinarySearch(sample);
            return i >= 0 ? i : -1;
        }

        public PointF ToCrop(float x, float y)
        {
            return new PointF((x - Region.X) * Scale, (y - Region.Y) * Scale);
        }

        public float ToReferenceY(float cropY)
        {
            return Region.Y + cropY / Scale;
        }

        public void Add(long timestamp, byte[] pixels, byte[] mask)
        {
            Timestamps.Add(timestamp);
            Samples.Add(Samples.Count);
            Pixels.Add(pixels);
            Masks.Add(mask);
        }

        /// <summary>The samples of the crops in a skeleton part (first timestamp, timestamps per sample).</summary>
        public void Align(long start, double step)
        {
            for (int i = 0; i < Timestamps.Count; i++)
                Samples[i] = step > 0 ? (int)Math.Round((Timestamps[i] - start) / step) : i;
        }

        /// <summary>Drops the oldest crops, keeping at most 'keep' of them.</summary>
        public void KeepLast(int keep)
        {
            int n = Timestamps.Count - keep;
            if (n <= 0)
                return;

            Timestamps.RemoveRange(0, n);
            Samples.RemoveRange(0, n);
            Pixels.RemoveRange(0, n);
            Masks.RemoveRange(0, n);
        }
    }

    /// <summary>A value learned at the frames kept by the analyst (kind, name, value without answers; answers of the same video).</summary>
    public delegate double SwimMeasure(string kind, string name, double fallback, out int sameVideo);

    /// <summary>What the detectors know besides the video: the stroke and what was learned from the analyst.</summary>
    public sealed class SwimDetectorContext
    {
        /// <summary>fs, bk, br, fly, im.</summary>
        public string Style = "fs";

        public SwimMeasure Measure = Fallback;

        private static double Fallback(string kind, string name, double fallback, out int sameVideo)
        {
            sameVideo = 0;
            return fallback;
        }
    }

    /// <summary>
    /// Events of the technical analysis found from the skeleton (and the image of the start): the start on the block (first
    /// movement, hands, back foot, feet, entry), the dolphin kicks and the breakout under the water, the contact and the
    /// push off of a turn. Each event has cues (frames found in different ways) that the learning weighs (see SwimProposalMath).
    /// </summary>
    public static class SwimEventDetector
    {
        public const string StartReaction = "start.reaction";
        public const string StartHands = "start.hands";
        public const string StartBack = "start.back";
        public const string StartFeet = "start.feet";
        public const string StartEntry = "start.entry";
        public const string UnderKick = "under.kick";
        public const string UnderBreakout = "under.breakout";
        public const string TurnContact = "turn.contact";
        public const string TurnFeet = "turn.feet";

        /// <summary>Water level of the entry below the block (body sizes) without any answer of the analyst.</summary>
        public const double DefaultEntryLevel = 0.5;

        /// <summary>Lines describing the steps of the detectors (for the tests), null in the program.</summary>
        public static Action<string> Trace;

        private static readonly int[] Feet = { SwimJoints.LeftAnkle, SwimJoints.RightAnkle, SwimJoints.LeftHeel, SwimJoints.RightHeel, SwimJoints.LeftToe, SwimJoints.RightToe };
        private static readonly int[] Wrists = { SwimJoints.LeftWrist, SwimJoints.RightWrist };

        /// <summary>All the events found in a reading.</summary>
        public static List<SwimDetection> Detect(SwimPoseSeries pose, SwimRegionFrames region, SwimDetectorContext context)
        {
            List<SwimDetection> found = new List<SwimDetection>();
            if (pose == null || pose.Count < 8 || pose.SamplesPerSecond <= 0)
                return found;

            context = context ?? new SwimDetectorContext();
            StartInfo start = DetectStart(pose, region, context, found);
            int from = start != null ? start.EndSample : 0;
            List<int> kicks = context.Style == "br" ? new List<int>() : DetectKicks(pose, from, found);
            DetectBreakout(pose, kicks, found);
            if (start == null)
                DetectTurn(pose, context, found);
            return found;
        }

        #region Start
        /// <summary>What the start detector found about the reading (the other detectors go on after it).</summary>
        public sealed class StartInfo
        {
            public double L;
            public int Direction;
            public int Set0, Set1, Half;
            public int EndSample;

            /// <summary>Level of the block contacts (reference pixels).</summary>
            public float Block = float.NaN;
        }

        /// <summary>Middle of the body: nose, middle of the shoulders, middle of the hips (those seen).</summary>
        public static PointF Core(SwimPoseSeries s, int i)
        {
            double x = 0, y = 0;
            int n = 0;
            Action<int, int> pair = (a, b) =>
            {
                bool ha = s.Has(i, a), hb = s.Has(i, b);
                if (!ha && !hb)
                    return;

                double px = ha && hb ? 0.5 * (s.X[i, a] + s.X[i, b]) : ha ? s.X[i, a] : s.X[i, b];
                double py = ha && hb ? 0.5 * (s.Y[i, a] + s.Y[i, b]) : ha ? s.Y[i, a] : s.Y[i, b];
                x += px;
                y += py;
                n++;
            };

            pair(SwimJoints.LeftHip, SwimJoints.RightHip);
            pair(SwimJoints.LeftShoulder, SwimJoints.RightShoulder);
            if (s.Has(i, SwimJoints.Nose))
            {
                x += s.X[i, SwimJoints.Nose];
                y += s.Y[i, SwimJoints.Nose];
                n++;
            }

            return n == 0 ? new PointF(float.NaN, float.NaN) : new PointF((float)(x / n), (float)(y / n));
        }

        /// <summary>Shoulder – hip – knee – ankle length of the better side (pixels), median over samples [a, b).</summary>
        public static double BodySize(SwimPoseSeries s, int a, int b)
        {
            Func<int, int, double> seg = (p, q) =>
            {
                List<double> d = new List<double>();
                for (int i = Math.Max(0, a); i < Math.Min(s.Count, b); i++)
                {
                    if (s.Has(i, p) && s.Has(i, q))
                        d.Add(Dist(s.P(i, p), s.P(i, q)));
                }

                return d.Count == 0 ? double.NaN : SwimLearning.Median(d);
            };

            double left = seg(SwimJoints.LeftShoulder, SwimJoints.LeftHip) + seg(SwimJoints.LeftHip, SwimJoints.LeftKnee) + seg(SwimJoints.LeftKnee, SwimJoints.LeftAnkle);
            double right = seg(SwimJoints.RightShoulder, SwimJoints.RightHip) + seg(SwimJoints.RightHip, SwimJoints.RightKnee) + seg(SwimJoints.RightKnee, SwimJoints.RightAnkle);
            if (double.IsNaN(left))
                return right;
            if (double.IsNaN(right))
                return left;
            return Math.Max(left, right);
        }

        /// <summary>
        /// The start on the block: the reading begins with the swimmer still (set position, hips above the feet) and the body
        /// then goes more than one body size away. Null if the reading is not a start.
        /// </summary>
        public static StartInfo DetectStart(SwimPoseSeries s, SwimRegionFrames region, SwimDetectorContext context, List<SwimDetection> found)
        {
            double sps = s.SamplesPerSecond;
            int n = s.Count;
            PointF[] core = new PointF[n];
            List<int> seen = new List<int>();
            for (int i = 0; i < n; i++)
            {
                core[i] = Core(s, i);
                if (!float.IsNaN(core[i].X))
                    seen.Add(i);
            }

            if (seen.Count < 10)
                return null;

            int first = seen[0];
            double l0 = BodySize(s, first, first + (int)(0.5 * sps));
            if (double.IsNaN(l0) || l0 < 8)
                return null;

            PointF reference = MedianPoint(core, seen.Take(5));
            int big = seen.FirstOrDefault(i => Dist(core[i], reference) > 1.0 * l0);
            if (big <= first)
                return null;

            // Set position: back from the big motion, the first samples where the core is still (3 in a row slower than
            // half a body per second over ±0.067 s); the 0.6 s before them.
            int still = -1, slow = 0;
            int k2 = Math.Max(2, (int)Math.Round(0.067 * sps));
            for (int i = big; i >= first + k2 && still < 0; i--)
            {
                int i0 = i - k2, i1 = Math.Min(n - 1, i + k2);
                double speed = float.IsNaN(core[i0].X) || float.IsNaN(core[i1].X) ? double.NaN : Dist(core[i0], core[i1]) / (i1 - i0) * sps / l0;
                slow = !double.IsNaN(speed) && speed < 0.5 ? slow + 1 : 0;
                if (slow >= 3)
                    still = i + 2;
            }

            if (still < 0)
                return null;

            int set1 = still - (int)Math.Round(0.05 * sps);
            int set0 = Math.Max(first, set1 - (int)Math.Round(0.6 * sps));
            if (set1 - set0 < Math.Max(5, (int)(0.2 * sps)))
                return null;

            List<int> setSamples = seen.Where(i => i >= set0 && i < set1).ToList();
            if (setSamples.Count < 5)
                return null;

            double L = BodySize(s, set0, set1);
            if (double.IsNaN(L) || L < 8)
                L = l0;

            // Still and upright: the core does not move in the set position and the hips are above the feet.
            PointF cbase = MedianPoint(core, setSamples);
            if (setSamples.Max(i => Dist(core[i], cbase)) > 0.25 * L)
                return null;

            double hipY = MedianOf(setSamples, i => MeanOf(s, i, SwimJoints.LeftHip, SwimJoints.RightHip, false));
            double feetY = MedianOf(setSamples, i => MeanOf(s, i, SwimJoints.LeftAnkle, SwimJoints.RightAnkle, false));
            if (double.IsNaN(hipY) || double.IsNaN(feetY) || feetY - hipY < 0.2 * L)
                return null;

            int direction = core[big].X >= cbase.X ? 1 : -1;
            double[] disp = new double[n];
            for (int i = 0; i < n; i++)
                disp[i] = float.IsNaN(core[i].X) ? double.NaN : Dist(core[i], cbase) / L;

            int half = -1;
            for (int i = set1; i < n; i++)
            {
                if (!double.IsNaN(disp[i]) && disp[i] > 0.5)
                {
                    half = i;
                    break;
                }
            }

            if (half < 0)
                return null;

            StartInfo info = new StartInfo { L = L, Direction = direction, Set0 = set0, Set1 = set1, Half = half };
            if (Trace != null)
                Trace(string.Format("start: L {0:0} dir {1} first {2} set {3}-{4} still {5} half {6} big {7}", L, direction, first, set0, set1, still, half, big));
            int quiet = Math.Max(3, (int)Math.Round(0.067 * sps));

            // First movement: back from the half body motion while the core is moving.
            List<double> setDisp = setSamples.Select(i => disp[i]).Where(v => !double.IsNaN(v)).ToList();
            double b = SwimLearning.Median(setDisp);
            double mad = SwimLearning.Median(setDisp.Select(v => Math.Abs(v - b)).ToList());
            int reactionSkeleton = OnsetBack(disp, half, set1, b + Math.Max(0.03, 6 * mad), quiet);
            SwimDetection reaction = new SwimDetection(StartReaction, SwimMarkType.Reaction) { Confidence = 0.75 };
            reaction.Cue("skeleton", reactionSkeleton, 0.6, 0.06);

            // Image of the block: region of the swimmer, motion and release of the contacts.
            BlockImage block = region != null ? BlockImage.Build(s, region, info, setSamples, big) : null;
            if (block != null)
            {
                int reactionImage = block.MotionOnset(info, quiet);
                if (reactionImage >= 0)
                {
                    reaction.Cue("image", reactionImage, 1.0, 0.04);
                    reaction.Confidence = 0.85;
                }
            }

            found.Add(reaction);
            int reactionSample = (int)Math.Round(reaction.Cues.Values.Min());

            // Hands, back foot, feet: release of the contacts with the block, else the skeleton.
            double hands = double.NaN, back = double.NaN, feet = double.NaN;
            if (block != null)
                block.Releases(info, reactionSample, out hands, out back, out feet);

            SwimDetection handsOff = new SwimDetection(StartHands, SwimMarkType.HandsOff) { Confidence = double.IsNaN(hands) ? 0.45 : 0.75 };
            handsOff.Cue("image", hands, 1.0, 0.04);
            int handsSkeleton = WristsAway(s, info, reactionSample);
            if (handsSkeleton >= 0)
                handsOff.Cue("skeleton", handsSkeleton, 0.3, 0.08);
            if (handsOff.Cues.Count > 0)
                found.Add(handsOff);

            if (!double.IsNaN(back))
            {
                SwimDetection backFoot = new SwimDetection(StartBack, SwimMarkType.BackFoot) { Confidence = 0.65 };
                backFoot.Cue("image", back, 1.0, 0.04);
                found.Add(backFoot);
            }

            SwimDetection feetOff = new SwimDetection(StartFeet, SwimMarkType.FeetOff) { Confidence = double.IsNaN(feet) ? 0.4 : 0.8 };
            feetOff.Cue("image", feet, 1.0, 0.035);
            int feetSkeleton = FeetAway(s, info, double.IsNaN(hands) ? reactionSample : (int)Math.Round(hands));
            if (feetSkeleton >= 0)
                feetOff.Cue("skeleton", feetSkeleton, 0.25, 0.1);
            if (feetOff.Cues.Count > 0)
                found.Add(feetOff);

            // Entry: the hands at the water, under the block by the level learned (body sizes).
            info.Block = block != null && !float.IsNaN(block.BlockLevel) ? block.BlockLevel : FeetLevel(s, setSamples);
            info.EndSample = feetOff.Cues.Count > 0 ? (int)Math.Round(feetOff.Cues.Values.Max()) : half;
            if (!float.IsNaN(info.Block))
            {
                // With the level learned in this video (same camera) the cues are other cues: their usual delay is learned apart.
                // The level of other videos is not used (other cameras): there the cues keep the default level and learn their delay.
                int sameVideo;
                double level = context.Measure(StartEntry, "level", DefaultEntryLevel, out sameVideo);
                if (sameVideo == 0)
                    level = DefaultEntryLevel;
                string suffix = sameVideo > 0 ? "-video" : "";
                double water = info.Block + level * L;
                int entryWrist = -1, entryHead = -1;
                for (int i = half; i < n && (entryWrist < 0 || entryHead < 0); i++)
                {
                    double wy = MaxOf(s, i, Wrists, 0.3f);
                    if (entryWrist < 0 && !double.IsNaN(wy) && wy >= water)
                        entryWrist = i;
                    if (entryHead < 0 && s.Has(i, SwimJoints.Nose, 0.3f) && s.Y[i, SwimJoints.Nose] >= water)
                        entryHead = i;
                }

                SwimDetection entry = new SwimDetection(StartEntry, SwimMarkType.Entry) { Confidence = 0.6 };
                if (entryWrist >= 0)
                    entry.Cue("wrist" + suffix, entryWrist, sameVideo > 0 ? 1.0 : 0.7, sameVideo > 0 ? 0.035 : 0.05);
                if (entryHead >= 0)
                    entry.Cue("head" + suffix, entryHead - 0.08 * sps, sameVideo > 0 ? 0.6 : 1.0, 0.05);
                entry.Measures["block"] = info.Block;
                entry.Measures["L"] = L;
                if (entry.Cues.Count > 0)
                {
                    found.Add(entry);
                    info.EndSample = (int)Math.Round(entry.Cues.Values.Max());
                }
            }

            return info;
        }

        /// <summary>From 'from' back to 'min': the first sample of the run above the level that reaches 'from' ('quiet' samples below end it).</summary>
        public static int OnsetBack(double[] signal, int from, int min, double level, int quiet)
        {
            int i = from, below = 0, lastAbove = from;
            while (i > min)
            {
                i--;
                if (!double.IsNaN(signal[i]) && signal[i] > level)
                {
                    below = 0;
                    lastAbove = i;
                }
                else if (++below >= quiet)
                {
                    break;
                }
            }

            return lastAbove;
        }

        /// <summary>The forward wrist leaves its place of the set position by a quarter of the body (3 samples in a row).</summary>
        private static int WristsAway(SwimPoseSeries s, StartInfo info, int from)
        {
            PointF[] base0 = new PointF[2];
            for (int k = 0; k < 2; k++)
            {
                int j = Wrists[k];
                List<int> samples = Enumerable.Range(info.Set0, info.Set1 - info.Set0).Where(i => s.Has(i, j, 0.5f)).ToList();
                base0[k] = samples.Count < 3 ? new PointF(float.NaN, float.NaN) : new PointF((float)MedianOf(samples, i => s.X[i, j]), (float)MedianOf(samples, i => s.Y[i, j]));
            }

            int run = 0;
            for (int i = Math.Max(0, from); i < s.Count; i++)
            {
                bool away = true, any = false;
                for (int k = 0; k < 2; k++)
                {
                    int j = Wrists[k];
                    if (float.IsNaN(base0[k].X) || !s.Has(i, j, 0.5f))
                        continue;

                    any = true;
                    away &= Dist(s.P(i, j), base0[k]) > 0.25 * info.L;
                }

                run = any && away ? run + 1 : 0;
                if (run >= 3)
                    return i - 2;
            }

            return -1;
        }

        /// <summary>All the feet points seen leave their place of the set position by 15% of the body (3 samples in a row).</summary>
        private static int FeetAway(SwimPoseSeries s, StartInfo info, int from)
        {
            PointF[] base0 = new PointF[Feet.Length];
            for (int k = 0; k < Feet.Length; k++)
            {
                int j = Feet[k];
                List<int> samples = Enumerable.Range(info.Set0, info.Set1 - info.Set0).Where(i => s.Has(i, j, 0.5f)).ToList();
                base0[k] = samples.Count < 3 ? new PointF(float.NaN, float.NaN) : new PointF((float)MedianOf(samples, i => s.X[i, j]), (float)MedianOf(samples, i => s.Y[i, j]));
            }

            int run = 0;
            for (int i = Math.Max(0, from); i < s.Count; i++)
            {
                double nearest = double.MaxValue;
                for (int k = 0; k < Feet.Length; k++)
                {
                    int j = Feet[k];
                    if (float.IsNaN(base0[k].X) || !s.Has(i, j, 0.5f))
                        continue;

                    nearest = Math.Min(nearest, Dist(s.P(i, j), base0[k]));
                }

                run = nearest < double.MaxValue && nearest > 0.15 * info.L ? run + 1 : 0;
                if (run >= 3)
                    return i - 2;
            }

            return -1;
        }

        /// <summary>Level of the feet in the set position (the lowest foot point, reference pixels).</summary>
        private static float FeetLevel(SwimPoseSeries s, List<int> setSamples)
        {
            double y = MedianOf(setSamples, i => MaxOf(s, i, Feet, 0.3f));
            return double.IsNaN(y) ? float.NaN : (float)y;
        }

        /// <summary>
        /// The image of the block: the swimmer in the set position (person masks of the pose model, else around the skeleton),
        /// the block empty after the swimmer left, the time when each pixel of the swimmer becomes the empty block again.
        /// </summary>
        private sealed class BlockImage
        {
            private const int Threshold = 26;
            private const int Hold = 3;

            private SwimRegionFrames region;
            private bool[] area;
            private float lp;
            private int direction;
            private int set0, set1, gone;

            /// <summary>Level of the contacts with the block (reference pixels), NaN if unknown.</summary>
            public float BlockLevel = float.NaN;

            public static BlockImage Build(SwimPoseSeries s, SwimRegionFrames r, StartInfo info, List<int> setSamples, int big)
            {
                if (r == null || r.Count < 10 || r.Width < 16 || r.Height < 16)
                    return null;

                BlockImage b = new BlockImage { region = r, lp = (float)(info.L * r.Scale), direction = info.Direction };
                b.set0 = Math.Max(0, LowerIndex(r, info.Set0));
                b.set1 = LowerIndex(r, info.Set1);
                if (b.set1 - b.set0 < 4)
                    return null;

                // Area of the swimmer: the person masks of the set position (union, widened), else thick lines on the skeleton.
                int w = r.Width, h = r.Height;
                bool[] union = new bool[w * h];
                int masks = 0;
                for (int k = b.set0; k < b.set1; k++)
                {
                    byte[] m = r.Masks[k];
                    if (m == null)
                        continue;

                    masks++;
                    for (int p = 0; p < union.Length; p++)
                        union[p] |= m[p] != 0;
                }

                if (masks >= 3)
                    b.area = Dilate(union, w, h, Math.Max(1, (int)Math.Round(0.06 * b.lp)));
                else
                    b.area = SkeletonArea(s, r, info, setSamples, b.lp);

                // Front of the area, and the first sample where the whole skeleton is beyond it (the block is empty).
                float front = float.MinValue;
                for (int p = 0; p < b.area.Length; p++)
                {
                    if (b.area[p])
                        front = Math.Max(front, b.direction * (p % w));
                }

                b.gone = -1;
                for (int i = big; i < s.Count && b.gone < 0; i++)
                {
                    int count = 0;
                    float least = float.MaxValue;
                    for (int j = 0; j < SwimJoints.Count; j++)
                    {
                        if (!s.Has(i, j, 0.3f))
                            continue;

                        count++;
                        least = Math.Min(least, b.direction * r.ToCrop(s.X[i, j], s.Y[i, j]).X);
                    }

                    if (count >= 8 && least > front + 0.3f * b.lp)
                        b.gone = r.IndexOf(i) >= 0 ? r.IndexOf(i) : LowerIndex(r, i);
                }

                if (b.gone < 0)
                {
                    // The swimmer still in the region at the end of the crops: the last ones, if well after the half body motion.
                    int last = r.Count - 4;
                    if (last <= LowerIndex(r, info.Half) + (int)(0.3 * s.SamplesPerSecond))
                        return null;
                    b.gone = last;
                }

                if (b.gone + 4 > r.Count || b.gone <= b.set1)
                    return null;

                if (Trace != null)
                    Trace(string.Format("block: crops {0} lp {1:0} set {2}-{3} gone {4} (sample {5}) area {6} masks {7} front {8:0}",
                        r.Count, b.lp, b.set0, b.set1, b.gone, r.Samples[b.gone], b.area.Count(x => x), masks, front));

                return b;
            }

            /// <summary>Index of the last crop at or before a sample (0 if none).</summary>
            private static int LowerIndex(SwimRegionFrames r, int sample)
            {
                int i = r.Samples.BinarySearch(sample);
                if (i >= 0)
                    return i;
                i = ~i - 1;
                return Math.Max(0, i);
            }

            private static bool[] SkeletonArea(SwimPoseSeries s, SwimRegionFrames r, StartInfo info, List<int> setSamples, float lp)
            {
                int w = r.Width, h = r.Height;
                bool[] a = new bool[w * h];
                PointF[] p = new PointF[SwimJoints.Count];
                for (int j = 0; j < SwimJoints.Count; j++)
                {
                    List<int> samples = setSamples.Where(i => s.Has(i, j, 0.3f)).ToList();
                    p[j] = samples.Count == 0 ? new PointF(float.NaN, float.NaN) :
                        r.ToCrop((float)MedianOf(samples, i => s.X[i, j]), (float)MedianOf(samples, i => s.Y[i, j]));
                }

                foreach (int[] bone in SwimJoints.Bones)
                {
                    if (!float.IsNaN(p[bone[0]].X) && !float.IsNaN(p[bone[1]].X))
                        FillCapsule(a, w, h, p[bone[0]], p[bone[1]], 0.18f * lp);
                }

                foreach (int j in Feet.Concat(Wrists))
                {
                    if (!float.IsNaN(p[j].X))
                        FillCapsule(a, w, h, p[j], p[j], 0.25f * lp);
                }

                return a;
            }

            /// <summary>Part of the swimmer area that changes between two crops 2 samples apart: back from the half body motion while it moves.</summary>
            public int MotionOnset(StartInfo info, int quiet)
            {
                int lag = 2;
                int w = region.Width, h = region.Height;
                int count = region.Count;
                double[] motion = new double[count];
                int areaCount = area.Count(x => x);
                if (areaCount < 20)
                    return -1;

                for (int k = lag; k < count; k++)
                {
                    byte[] a = region.Pixels[k], b = region.Pixels[k - lag];
                    int changed = 0;
                    for (int p = 0; p < w * h; p++)
                    {
                        if (!area[p])
                            continue;

                        int o = p * 3;
                        int ga = (a[o] + 2 * a[o + 1] + a[o + 2]) >> 2, gb = (b[o] + 2 * b[o + 1] + b[o + 2]) >> 2;
                        if (Math.Abs(ga - gb) > 14)
                            changed++;
                    }

                    motion[k] = changed / (double)areaCount;
                }

                List<double> quietPart = new List<double>();
                for (int k = Math.Max(lag, set0); k < set1; k++)
                    quietPart.Add(motion[k]);
                if (quietPart.Count < 3)
                    return -1;

                double baseLevel = SwimLearning.Median(quietPart);
                double mad = SwimLearning.Median(quietPart.Select(v => Math.Abs(v - baseLevel)).ToList());
                int half = LowerIndex(region, info.Half);

                // The level: above the noise of the set position and a tenth of the motion of the start.
                double peak = 0;
                for (int k = set1; k <= Math.Min(count - 1, half); k++)
                    peak = Math.Max(peak, motion[k]);
                double level = baseLevel + Math.Max(Math.Max(0.03, 6 * mad), 0.1 * (peak - baseLevel));
                int onset = OnsetBack(motion, half, set1, level, quiet);
                return region.Samples[Math.Max(0, onset - lag / 2)];
            }

            /// <summary>
            /// Release of the contacts along the bottom of the swimmer in the set position: the front contacts (hands, then the
            /// front foot, which leaves last) and the back end (the back foot of a track start). Samples, NaN if not found.
            /// </summary>
            public void Releases(StartInfo info, int reactionSample, out double hands, out double back, out double feet)
            {
                hands = back = feet = double.NaN;
                int w = region.Width, h = region.Height, n = region.Count;
                int[] setMedian = MedianImage(set0, set1);
                int[] empty = MedianImage(gone, gone + 4);

                // The swimmer: differs from the empty block, in his area.
                bool[] swimmer = new bool[w * h];
                for (int p = 0; p < w * h; p++)
                {
                    if (!area[p])
                        continue;

                    int o = p * 3;
                    int d = Math.Max(Math.Abs(setMedian[o] - empty[o]), Math.Max(Math.Abs(setMedian[o + 1] - empty[o + 1]), Math.Abs(setMedian[o + 2] - empty[o + 2])));
                    swimmer[p] = d > Threshold;
                }

                swimmer = Open(swimmer, w, h);

                // Release: the first crop after the set position from which the pixel is the empty block for 'Hold' crops.
                int[] release = new int[w * h];
                int[] run = new int[w * h];
                for (int p = 0; p < release.Length; p++)
                    release[p] = -1;
                for (int k = set1; k < n; k++)
                {
                    byte[] img = region.Pixels[k];
                    for (int p = 0; p < w * h; p++)
                    {
                        if (!swimmer[p] || release[p] >= 0)
                            continue;

                        int o = p * 3;
                        int d = Math.Max(Math.Abs(img[o] - empty[o]), Math.Max(Math.Abs(img[o + 1] - empty[o + 1]), Math.Abs(img[o + 2] - empty[o + 2])));
                        run[p] = d <= Threshold ? run[p] + 1 : 0;
                        if (run[p] >= Hold)
                            release[p] = k - Hold + 1;
                    }
                }

                // Bottom of the swimmer, column by column: its lowest pixels and when they were released.
                List<double[]> contour = new List<double[]>(); // forward, y, release (crop index)
                for (int x = 0; x < w; x++)
                {
                    int bottom = -1;
                    for (int y = h - 1; y >= 0; y--)
                    {
                        if (swimmer[y * w + x])
                        {
                            bottom = y;
                            break;
                        }
                    }

                    if (bottom < 0)
                        continue;

                    List<double> times = new List<double>();
                    for (int y = bottom; y >= 0 && times.Count < 4; y--)
                    {
                        int p = y * w + x;
                        if (swimmer[p] && release[p] >= 0)
                            times.Add(release[p]);
                    }

                    if (times.Count > 0)
                        contour.Add(new double[] { direction * x, bottom, SwimLearning.Median(times) });
                }

                if (contour.Count < 6)
                    return;

                contour.Sort((a, b) => a[0].CompareTo(b[0]));
                if (Trace != null)
                    Trace("contour: " + string.Join(" ", contour.Select(c => string.Format("{0:0}/{1:0}/{2:0.0}", c[0], c[1], c[2]))));
                double lowest = Percentile(contour.Select(c => c[1]).ToList(), 97);
                BlockLevel = region.ToReferenceY((float)lowest);
                List<double[]> band = contour.Where(c => c[1] >= lowest - 0.14 * lp).ToList();
                if (Trace != null)
                    Trace(string.Format("band (lowest {0:0}): ", lowest) + string.Join(" ", band.Select(c => string.Format("{0:0}/{1:0}/{2:0.0}", c[0], c[1], c[2]))));
                int reaction = LowerIndex(region, reactionSample);
                List<double[]> usable = band.Where(c => c[2] < gone - 2 && c[2] >= reaction).ToList();
                if (usable.Count > 0)
                    feet = Sample(usable.Max(c => c[2]));

                // Hands: the first contacts to leave the block, the first quarter of the releases of the bottom.
                List<double> early = usable.Select(c => c[2]).ToList();
                if (early.Count > 0)
                    hands = Sample(Percentile(early, 25));

                // Back foot (track start): the back end of the bottom, apart from the front contacts, released between hands and feet.
                double rearLimit = Percentile(contour.Select(c => c[0]).ToList(), 8);
                List<double[]> rear = contour.Where(c => c[0] <= rearLimit).ToList();
                if (rear.Count > 0 && band.Count > 0 && !double.IsNaN(hands) && !double.IsNaN(feet))
                {
                    double gap = band.Min(c => c[0]) - rear.Max(c => c[0]);
                    double r = Sample(rear.Max(c => c[2]));
                    if (gap > 0.3 * lp && r >= hands + 2 && r <= feet - 1)
                        back = r;
                }
            }

            /// <summary>Sample of the reading of a crop index (fractions between crops).</summary>
            private double Sample(double index)
            {
                int i = (int)Math.Floor(index);
                if (i < 0)
                    return region.Samples[0];
                if (i >= region.Count - 1)
                    return region.Samples[region.Count - 1];
                double f = index - i;
                return region.Samples[i] + f * (region.Samples[i + 1] - region.Samples[i]);
            }

            private int[] MedianImage(int from, int to)
            {
                int w = region.Width, h = region.Height;
                from = Math.Max(0, from);
                to = Math.Min(region.Count, to);
                List<int> picks = new List<int>();
                int count = to - from;
                int stepK = Math.Max(1, count / 15);
                for (int k = from; k < to; k += stepK)
                    picks.Add(k);

                int[] result = new int[w * h * 3];
                int[] values = new int[picks.Count];
                for (int p = 0; p < w * h * 3; p++)
                {
                    for (int q = 0; q < picks.Count; q++)
                        values[q] = region.Pixels[picks[q]][p];
                    Array.Sort(values);
                    result[p] = values[values.Length / 2];
                }

                return result;
            }
        }
        #endregion

        #region Under water
        /// <summary>
        /// Dolphin kicks: the body level, the arms forward (streamline), the feet going up and down across the line of the body.
        /// One kick at each lowest point of the feet. Returns the samples of the kicks.
        /// </summary>
        public static List<int> DetectKicks(SwimPoseSeries s, int from, List<SwimDetection> found)
        {
            List<int> kicks = new List<int>();
            double sps = s.SamplesPerSecond;
            int n = s.Count;
            double L = BodySize(s, from, n);
            if (double.IsNaN(L) || L < 8)
                return kicks;

            double[] offset = new double[n];
            bool[] glide = new bool[n];
            int around = Math.Max(2, (int)Math.Round(0.3 * sps));
            for (int i = 0; i < n; i++)
            {
                offset[i] = double.NaN;
                if (i < from)
                    continue;

                // Direction of the swimmer around this sample.
                int direction = MotionDirection(s, Math.Max(from, i - around), Math.Min(n, i + around + 1));

                PointF sh = MidPoint(s, i, SwimJoints.LeftShoulder, SwimJoints.RightShoulder);
                PointF hip = MidPoint(s, i, SwimJoints.LeftHip, SwimJoints.RightHip);
                PointF feet = MeanPoint(s, i, new[] { SwimJoints.LeftAnkle, SwimJoints.RightAnkle, SwimJoints.LeftToe, SwimJoints.RightToe }, 0.3f);
                PointF nose = s.Has(i, SwimJoints.Nose, 0.3f) ? s.P(i, SwimJoints.Nose) : sh;
                if (float.IsNaN(sh.X) || float.IsNaN(hip.X) || float.IsNaN(feet.X))
                    continue;

                double ux = hip.X - sh.X, uy = hip.Y - sh.Y;
                double len = Math.Sqrt(ux * ux + uy * uy);
                if (len < 1)
                    continue;

                ux /= len;
                uy /= len;

                // Level body: the line shoulders – hips within 35° of the horizontal.
                bool level = Math.Abs(uy) < Math.Sin(35 * Math.PI / 180);

                // Streamline: the wrists seen are in front of the head.
                bool forward = true, anyWrist = false;
                foreach (int j in Wrists)
                {
                    if (!s.Has(i, j, 0.3f))
                        continue;

                    anyWrist = true;
                    forward &= direction * (s.X[i, j] - nose.X) > 0.05 * L;
                }

                glide[i] = level && anyWrist && forward;

                // Distance of the feet from the line of the body, positive towards the bottom of the image.
                double nx = -uy, ny = ux;
                if (ny < 0)
                {
                    nx = -nx;
                    ny = -ny;
                }

                offset[i] = ((feet.X - hip.X) * nx + (feet.Y - hip.Y) * ny) / L;
            }

            // Smoothed, without its slow part (±0.3 s).
            double[] smooth = Smooth(offset, 1);
            int halfWindow = Math.Max(2, (int)Math.Round(0.3 * sps));
            double[] wave = new double[n];
            for (int i = 0; i < n; i++)
            {
                wave[i] = double.NaN;
                if (double.IsNaN(smooth[i]))
                    continue;

                double sum = 0;
                int count = 0;
                for (int k = Math.Max(0, i - halfWindow); k <= Math.Min(n - 1, i + halfWindow); k++)
                {
                    if (!double.IsNaN(smooth[k]))
                    {
                        sum += smooth[k];
                        count++;
                    }
                }

                wave[i] = smooth[i] - sum / count;
            }

            // The underwater part: the glide (arms forward) for 0.6 s at least, gaps of 2 samples allowed, and 0.12 s after it
            // (the last kick comes with the first pull). Short moments of arms forward while swimming (butterfly) do not count.
            bool[] underwater = new bool[n];
            int gapLimit = 2, minRun = Math.Max(4, (int)Math.Round(0.6 * sps)), tail = (int)Math.Round(0.12 * sps);
            for (int i = 0; i < n; )
            {
                if (!glide[i])
                {
                    i++;
                    continue;
                }

                int j = i, last = i, gap = 0;
                while (j + 1 < n && gap <= gapLimit)
                {
                    j++;
                    if (glide[j])
                    {
                        last = j;
                        gap = 0;
                    }
                    else
                    {
                        gap++;
                    }
                }

                if (last - i + 1 >= minRun)
                {
                    for (int k = i; k <= Math.Min(n - 1, last + tail); k++)
                        underwater[k] = true;
                }

                i = last + 1;
            }

            // Lowest points of the feet in the glide: local maxima, apart by 0.25 s at least, clear against the points around.
            int minGap = Math.Max(2, (int)Math.Round(0.25 * sps));
            List<int> peaks = new List<int>();
            List<double> heights = new List<double>();
            for (int i = 1; i < n - 1; i++)
            {
                if (!underwater[i] || double.IsNaN(wave[i]))
                    continue;

                bool top = true;
                for (int k = Math.Max(0, i - minGap); k <= Math.Min(n - 1, i + minGap) && top; k++)
                {
                    if (k != i && !double.IsNaN(wave[k]) && (wave[k] > wave[i] || (wave[k] == wave[i] && k < i)))
                        top = false;
                }

                if (!top || wave[i] < 0.03)
                    continue;

                // Prominence: above the lowest values on both sides within a kick.
                double left = MinOf(wave, i - 2 * minGap, i), right = MinOf(wave, i, i + 2 * minGap);
                double prominence = wave[i] - Math.Max(left, right);
                if (double.IsNaN(prominence) || prominence < 0.06)
                    continue;

                peaks.Add(i);
                heights.Add(prominence);
            }

            if (peaks.Count < 2)
                return kicks;

            for (int k = 0; k < peaks.Count; k++)
            {
                SwimDetection kick = new SwimDetection(UnderKick, SwimMarkType.Kick, k + 1) { Confidence = Math.Min(0.85, 0.45 + 0.4 * Math.Min(1, heights[k] / 0.25)) };
                kick.Cue("feet", peaks[k], 1.0, 0.05);
                found.Add(kick);
                kicks.Add(peaks[k]);
            }

            return kicks;
        }

        /// <summary>
        /// Breakout after the dolphin kicks: the head reaches the level where the swimmer then swims (the surface), and the first
        /// arm stroke begins (a wrist goes back from the streamline).
        /// </summary>
        public static void DetectBreakout(SwimPoseSeries s, List<int> kicks, List<SwimDetection> found)
        {
            if (kicks == null || kicks.Count < 2)
                return;

            double sps = s.SamplesPerSecond;
            int n = s.Count;
            int lastKick = kicks[kicks.Count - 1];
            double L = BodySize(s, kicks[0], n);
            if (double.IsNaN(L))
                return;

            int direction = MotionDirection(s, kicks[0], n);

            // First stroke: a wrist goes back from where it was in the glide (relative to the shoulders) by a third of the body.
            Func<int, int, double> reach = (i, j) =>
            {
                PointF sh = MidPoint(s, i, SwimJoints.LeftShoulder, SwimJoints.RightShoulder);
                return float.IsNaN(sh.X) || !s.Has(i, j, 0.3f) ? double.NaN : direction * (s.X[i, j] - sh.X);
            };

            double glideReach = double.NaN;
            List<double> reaches = new List<double>();
            for (int i = kicks[0]; i <= lastKick; i++)
            {
                foreach (int j in Wrists)
                {
                    double r = reach(i, j);
                    if (!double.IsNaN(r))
                        reaches.Add(r);
                }
            }

            if (reaches.Count > 0)
                glideReach = SwimLearning.Median(reaches);

            int stroke = -1, run = 0;
            for (int i = lastKick; i < n && stroke < 0 && !double.IsNaN(glideReach); i++)
            {
                bool back = false;
                foreach (int j in Wrists)
                {
                    double r = reach(i, j);
                    if (!double.IsNaN(r) && r < glideReach - 0.33 * L)
                        back = true;
                }

                run = back ? run + 1 : 0;
                if (run >= 2)
                    stroke = i - 1;
            }

            SwimDetection breakout = new SwimDetection(UnderBreakout, SwimMarkType.Breakout) { Confidence = 0.5 };
            if (stroke >= 0)
                breakout.Cue("stroke", stroke + 0.12 * sps, 0.5, 0.1);

            // Surface: the highest level of the head in the swimming after the first stroke.
            int end = stroke >= 0 ? Math.Min(n, stroke + (int)(1.2 * sps)) : n;
            List<double> heads = new List<double>();
            for (int i = stroke >= 0 ? stroke : lastKick; i < end; i++)
            {
                if (s.Has(i, SwimJoints.Nose, 0.3f))
                    heads.Add(s.Y[i, SwimJoints.Nose]);
            }

            if (heads.Count >= 5 && (stroke < 0 || end - stroke >= (int)(0.4 * sps)))
            {
                double surface = Percentile(heads, 10);
                for (int i = lastKick; i < n; i++)
                {
                    if (s.Has(i, SwimJoints.Nose, 0.3f) && s.Y[i, SwimJoints.Nose] <= surface + 0.05 * L)
                    {
                        breakout.Cue("surface", i, 1.0, 0.06);
                        breakout.Confidence = 0.6;
                        break;
                    }
                }
            }

            if (breakout.Cues.Count > 0)
                found.Add(breakout);
        }
        #endregion

        #region Turn
        /// <summary>
        /// A turn at a wall: the swimmer comes (the body moves one way), stops and goes back the other way. Contact: the hands
        /// at the wall (breaststroke, butterfly) or the feet at the wall after the somersault (freestyle, backstroke). Push off:
        /// the feet leave their place at the wall.
        /// </summary>
        public static void DetectTurn(SwimPoseSeries s, SwimDetectorContext context, List<SwimDetection> found)
        {
            double sps = s.SamplesPerSecond;
            int n = s.Count;
            double L = BodySize(s, 0, n);
            if (double.IsNaN(L) || L < 8)
                return;

            // Velocity of the core along x (body sizes per second), over ±3 samples.
            PointF[] core = new PointF[n];
            for (int i = 0; i < n; i++)
                core[i] = Core(s, i);

            double[] v = new double[n];
            int k3 = Math.Max(2, (int)Math.Round(0.05 * sps));
            for (int i = 0; i < n; i++)
            {
                v[i] = double.NaN;
                int a = i - k3, b = i + k3;
                if (a < 0 || b >= n || float.IsNaN(core[a].X) || float.IsNaN(core[b].X))
                    continue;
                v[i] = (core[b].X - core[a].X) / (b - a) * sps / L;
            }

            // The approach and the push off: 0.3 s faster than 0.4 body/s one way, then the other way, within 2.5 s.
            int need = Math.Max(3, (int)Math.Round(0.3 * sps));
            int approachEnd = -1, approachDir = 0;
            int runPos = 0, runNeg = 0;
            for (int i = 0; i < n; i++)
            {
                runPos = !double.IsNaN(v[i]) && v[i] > 0.4 ? runPos + 1 : 0;
                runNeg = !double.IsNaN(v[i]) && v[i] < -0.4 ? runNeg + 1 : 0;
                int dir = runPos >= need ? 1 : runNeg >= need ? -1 : 0;
                if (dir == 0)
                    continue;

                if (approachDir == 0 || approachDir == dir)
                {
                    approachDir = dir;
                    approachEnd = i;
                }
                else if (i - approachEnd <= (int)(2.5 * sps))
                {
                    TurnAt(s, context, L, approachDir, approachEnd, i, found);
                    return;
                }
                else
                {
                    approachDir = dir;
                    approachEnd = i;
                }
            }
        }

        private static void TurnAt(SwimPoseSeries s, SwimDetectorContext context, double L, int towardWall, int approachEnd, int pushSeen, List<SwimDetection> found)
        {
            double sps = s.SamplesPerSecond;
            bool open = context.Style == "br" || context.Style == "fly";
            int from = Math.Max(0, approachEnd - (int)(1.5 * sps));
            int to = Math.Min(s.Count - 1, pushSeen);

            // Contact: the hands (open turn) or the feet (somersault) at their place nearest to the wall.
            int[] joints = open ? Wrists : Feet;
            double best = double.MinValue;
            int bestAt = -1;
            for (int i = from; i <= to; i++)
            {
                double x = MaxForward(s, i, joints, towardWall, 0.3f);
                if (!double.IsNaN(x) && x > best)
                {
                    best = x;
                    bestAt = i;
                }
            }

            if (bestAt < 0)
                return;

            int contact = bestAt;
            for (int i = bestAt; i >= from; i--)
            {
                double x = MaxForward(s, i, joints, towardWall, 0.3f);
                if (double.IsNaN(x) || x < best - 0.02 * L)
                    break;
                contact = i;
            }

            SwimDetection wall = new SwimDetection(TurnContact, SwimMarkType.WallContact) { Confidence = 0.5 };
            wall.Cue(open ? "hands" : "feet", contact, 1.0, 0.06);
            found.Add(wall);

            // Push off: the feet come to their place nearest to the wall, then leave it (a tenth of the body, 3 samples in a row);
            // the body is then at its fastest away from the wall.
            double wallFeet = double.MinValue;
            int wallFeetAt = -1;
            for (int i = contact; i <= Math.Min(s.Count - 1, pushSeen); i++)
            {
                double x = MaxForward(s, i, Feet, towardWall, 0.3f);
                if (!double.IsNaN(x) && x > wallFeet)
                {
                    wallFeet = x;
                    wallFeetAt = i;
                }
            }

            SwimDetection push = new SwimDetection(TurnFeet, SwimMarkType.FeetOff) { Confidence = 0.5 };
            int run = 0;
            for (int i = wallFeetAt; i >= 0 && i < s.Count; i++)
            {
                double x = MaxForward(s, i, Feet, towardWall, 0.3f);
                run = !double.IsNaN(x) && x < wallFeet - 0.1 * L ? run + 1 : 0;
                if (run >= 3)
                {
                    push.Cue("feet", i - 2, 1.0, 0.06);
                    break;
                }
            }

            double fastest = 0;
            int fastestAt = -1;
            int k3 = Math.Max(2, (int)Math.Round(0.05 * sps));
            int pushFrom = Math.Max(k3, wallFeetAt >= 0 ? wallFeetAt : contact);
            for (int i = pushFrom; i < Math.Min(s.Count - k3, pushFrom + (int)(0.8 * sps)); i++)
            {
                PointF a = Core(s, i - k3), b = Core(s, i + k3);
                if (float.IsNaN(a.X) || float.IsNaN(b.X))
                    continue;

                double away = -towardWall * (b.X - a.X);
                if (away > fastest)
                {
                    fastest = away;
                    fastestAt = i;
                }
            }

            if (fastestAt >= 0)
                push.Cue("speed", fastestAt, 0.4, 0.08);
            if (push.Cues.Count > 0)
                found.Add(push);
        }
        #endregion

        #region Helpers
        private static double Dist(PointF a, PointF b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        private static PointF MedianPoint(PointF[] points, IEnumerable<int> samples)
        {
            List<int> list = samples.Where(i => !float.IsNaN(points[i].X)).ToList();
            if (list.Count == 0)
                return new PointF(float.NaN, float.NaN);
            return new PointF((float)SwimLearning.Median(list.Select(i => (double)points[i].X).ToList()), (float)SwimLearning.Median(list.Select(i => (double)points[i].Y).ToList()));
        }

        private static double MedianOf(IEnumerable<int> samples, Func<int, double> value)
        {
            List<double> v = samples.Select(value).Where(x => !double.IsNaN(x)).ToList();
            return v.Count == 0 ? double.NaN : SwimLearning.Median(v);
        }

        /// <summary>Mean of the y of two joints (or the one seen); x when 'x' is true.</summary>
        private static double MeanOf(SwimPoseSeries s, int i, int a, int b, bool x)
        {
            bool ha = s.Has(i, a, 0.3f), hb = s.Has(i, b, 0.3f);
            Func<int, double> c = j => x ? s.X[i, j] : s.Y[i, j];
            if (ha && hb)
                return 0.5 * (c(a) + c(b));
            if (ha)
                return c(a);
            if (hb)
                return c(b);
            return double.NaN;
        }

        private static PointF MidPoint(SwimPoseSeries s, int i, int a, int b)
        {
            double x = MeanOf(s, i, a, b, true), y = MeanOf(s, i, a, b, false);
            return double.IsNaN(x) ? new PointF(float.NaN, float.NaN) : new PointF((float)x, (float)y);
        }

        private static PointF MeanPoint(SwimPoseSeries s, int i, int[] joints, float visibility)
        {
            double x = 0, y = 0;
            int n = 0;
            foreach (int j in joints)
            {
                if (!s.Has(i, j, visibility))
                    continue;
                x += s.X[i, j];
                y += s.Y[i, j];
                n++;
            }

            return n == 0 ? new PointF(float.NaN, float.NaN) : new PointF((float)(x / n), (float)(y / n));
        }

        /// <summary>Largest y of the joints seen (the lowest in the image).</summary>
        private static double MaxOf(SwimPoseSeries s, int i, int[] joints, float visibility)
        {
            double best = double.NaN;
            foreach (int j in joints)
            {
                if (s.Has(i, j, visibility) && (double.IsNaN(best) || s.Y[i, j] > best))
                    best = s.Y[i, j];
            }

            return best;
        }

        /// <summary>The joint furthest in a direction of x.</summary>
        private static double MaxForward(SwimPoseSeries s, int i, int[] joints, int direction, float visibility)
        {
            double best = double.NaN;
            foreach (int j in joints)
            {
                if (s.Has(i, j, visibility) && (double.IsNaN(best) || direction * s.X[i, j] > best))
                    best = direction * s.X[i, j];
            }

            return best;
        }

        /// <summary>Direction of the motion of the body along x in a part (+1 or −1).</summary>
        private static int MotionDirection(SwimPoseSeries s, int from, int to)
        {
            PointF a = new PointF(float.NaN, float.NaN), b = a;
            for (int i = Math.Max(0, from); i < to && float.IsNaN(a.X); i++)
                a = Core(s, i);
            for (int i = to - 1; i >= Math.Max(0, from) && float.IsNaN(b.X); i--)
                b = Core(s, i);
            return float.IsNaN(a.X) || float.IsNaN(b.X) || b.X >= a.X ? 1 : -1;
        }

        private static double[] Smooth(double[] values, int half)
        {
            int n = values.Length;
            double[] median = new double[n];
            List<double> w = new List<double>();
            for (int i = 0; i < n; i++)
            {
                median[i] = double.NaN;
                if (double.IsNaN(values[i]))
                    continue;

                w.Clear();
                for (int k = Math.Max(0, i - half); k <= Math.Min(n - 1, i + half); k++)
                {
                    if (!double.IsNaN(values[k]))
                        w.Add(values[k]);
                }

                median[i] = SwimLearning.Median(w);
            }

            double[] mean = new double[n];
            for (int i = 0; i < n; i++)
            {
                mean[i] = double.NaN;
                if (double.IsNaN(median[i]))
                    continue;

                double sum = 0;
                int count = 0;
                for (int k = Math.Max(0, i - half); k <= Math.Min(n - 1, i + half); k++)
                {
                    if (!double.IsNaN(median[k]))
                    {
                        sum += median[k];
                        count++;
                    }
                }

                mean[i] = sum / count;
            }

            return mean;
        }

        private static double MinOf(double[] values, int from, int to)
        {
            double best = double.NaN;
            for (int i = Math.Max(0, from); i <= Math.Min(values.Length - 1, to); i++)
            {
                if (!double.IsNaN(values[i]) && (double.IsNaN(best) || values[i] < best))
                    best = values[i];
            }

            return best;
        }

        public static double Percentile(List<double> values, double p)
        {
            if (values == null || values.Count == 0)
                return double.NaN;

            List<double> v = values.OrderBy(x => x).ToList();
            double pos = (v.Count - 1) * p / 100.0;
            int i = (int)Math.Floor(pos);
            if (i >= v.Count - 1)
                return v[v.Count - 1];
            return v[i] + (pos - i) * (v[i + 1] - v[i]);
        }

        private static bool[] Dilate(bool[] a, int w, int h, int r)
        {
            bool[] result = new bool[a.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!a[y * w + x])
                        continue;

                    for (int dy = -r; dy <= r; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h)
                            continue;
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int xx = x + dx;
                            if (xx >= 0 && xx < w)
                                result[yy * w + xx] = true;
                        }
                    }
                }
            }

            return result;
        }

        private static bool[] Erode(bool[] a, int w, int h, int r)
        {
            bool[] result = new bool[a.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool all = true;
                    for (int dy = -r; dy <= r && all; dy++)
                    {
                        int yy = y + dy;
                        for (int dx = -r; dx <= r && all; dx++)
                        {
                            int xx = x + dx;
                            all = yy >= 0 && yy < h && xx >= 0 && xx < w && a[yy * w + xx];
                        }
                    }

                    result[y * w + x] = all;
                }
            }

            return result;
        }

        /// <summary>Opening with a 3 × 3 square (removes isolated pixels).</summary>
        private static bool[] Open(bool[] a, int w, int h)
        {
            return Dilate(Erode(a, w, h, 1), w, h, 1);
        }

        private static void FillCapsule(bool[] a, int w, int h, PointF p, PointF q, float radius)
        {
            int x0 = (int)Math.Floor(Math.Min(p.X, q.X) - radius), x1 = (int)Math.Ceiling(Math.Max(p.X, q.X) + radius);
            int y0 = (int)Math.Floor(Math.Min(p.Y, q.Y) - radius), y1 = (int)Math.Ceiling(Math.Max(p.Y, q.Y) + radius);
            double dx = q.X - p.X, dy = q.Y - p.Y, len2 = dx * dx + dy * dy;
            for (int y = Math.Max(0, y0); y <= Math.Min(h - 1, y1); y++)
            {
                for (int x = Math.Max(0, x0); x <= Math.Min(w - 1, x1); x++)
                {
                    double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((x - p.X) * dx + (y - p.Y) * dy) / len2)) : 0;
                    double cx = p.X + t * dx - x, cy = p.Y + t * dy - y;
                    if (cx * cx + cy * cy <= radius * radius)
                        a[y * w + x] = true;
                }
            }
        }
        #endregion
    }
}
