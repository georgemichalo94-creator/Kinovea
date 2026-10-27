#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>A box of a swimmer in an image (pixels of that image) and the score of the detector.</summary>
    public sealed class SwimBox
    {
        public float X0, Y0, X1, Y1, Score;

        public SwimBox()
        {
        }

        public SwimBox(float x0, float y0, float x1, float y1, float score)
        {
            X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; Score = score;
        }

        public float Width { get { return X1 - X0; } }
        public float Height { get { return Y1 - Y0; } }
        public float CenterX { get { return (X0 + X1) / 2; } }
        public float CenterY { get { return (Y0 + Y1) / 2; } }
        public float Diagonal { get { return (float)Math.Sqrt(Width * Width + Height * Height); } }

        public static SwimBox Lerp(SwimBox a, SwimBox b, float k)
        {
            return new SwimBox(a.X0 + (b.X0 - a.X0) * k, a.Y0 + (b.Y0 - a.Y0) * k, a.X1 + (b.X1 - a.X1) * k, a.Y1 + (b.Y1 - a.Y1) * k, 0);
        }
    }

    /// <summary>
    /// (2.9.9) The swimmer detector of the technical analysis: a YOLOX detector of persons (Megvii, Apache License 2.0), in
    /// all the views (above the water, under the water, from the front), where the person detector of the skeleton (made for
    /// people standing, with the face visible) does not see a swimmer. Input 640 × 640 BGR 0–255 with grey borders (114),
    /// output 8400 × 85 (box offsets on the grid of strides 8, 16, 32, objectness, 80 classes; class 0 = person).
    /// </summary>
    public sealed class SwimSwimmerDetector : IDisposable
    {
        public const string ModelFile = "swimmer_yolox_s.onnx";
        public const int InputSize = 640;
        private static readonly int[] Strides = { 8, 16, 32 };

        private readonly OpenCvSharp.Dnn.Net net;
        private readonly string[] outputs;
        private readonly float[] gridX, gridY, gridStride;

        public static bool Available
        {
            get { return File.Exists(Path.Combine(SwimPoseNet.ModelFolder, ModelFile)); }
        }

        public SwimSwimmerDetector() : this(Path.Combine(SwimPoseNet.ModelFolder, ModelFile))
        {
        }

        public SwimSwimmerDetector(string path)
        {
            net = OpenCvSharp.Dnn.Net.ReadNetFromONNX(path);
            if (net == null)
                throw new InvalidOperationException("The swimmer detector could not be read.");
            net.SetPreferableBackend(OpenCvSharp.Dnn.Backend.OPENCV);
            net.SetPreferableTarget(OpenCvSharp.Dnn.Target.CPU);
            outputs = net.GetUnconnectedOutLayersNames().Where(n => n != null).ToArray();
            List<float> gx = new List<float>(), gy = new List<float>(), gs = new List<float>();
            foreach (int s in Strides)
            {
                int n = InputSize / s;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        gx.Add(x);
                        gy.Add(y);
                        gs.Add(s);
                    }
                }
            }

            gridX = gx.ToArray();
            gridY = gy.ToArray();
            gridStride = gs.ToArray();
        }

        /// <summary>Persons in the image (pixels of the image), best first, at most max, above the threshold.</summary>
        public List<SwimBox> Detect(OpenCvSharp.Mat bgr, float threshold = 0.05f, int max = 8)
        {
            double r = Math.Min(InputSize / (double)bgr.Height, InputSize / (double)bgr.Width);
            int w = Math.Max(1, (int)(bgr.Width * r)), h = Math.Max(1, (int)(bgr.Height * r));
            float[] o;
            using (OpenCvSharp.Mat input = new OpenCvSharp.Mat(InputSize, InputSize, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.All(114)))
            using (OpenCvSharp.Mat resized = new OpenCvSharp.Mat())
            {
                OpenCvSharp.Cv2.Resize(bgr, resized, new OpenCvSharp.Size(w, h), 0, 0, OpenCvSharp.InterpolationFlags.Linear);
                using (OpenCvSharp.Mat target = new OpenCvSharp.Mat(input, new OpenCvSharp.Rect(0, 0, w, h)))
                    resized.CopyTo(target);
                using (OpenCvSharp.Mat blob = OpenCvSharp.Dnn.CvDnn.BlobFromImage(input, 1.0, new OpenCvSharp.Size(InputSize, InputSize), new OpenCvSharp.Scalar(0, 0, 0), false, false))
                {
                    net.SetInput(blob);
                    using (OpenCvSharp.Mat res = net.Forward(outputs.Length > 0 ? outputs[0] : null))
                        o = Floats(res);
                }
            }

            int anchors = gridX.Length;
            if (o.Length < anchors * 85)
                throw new InvalidOperationException("Unexpected output of the swimmer detector.");

            List<SwimBox> found = new List<SwimBox>();
            for (int i = 0; i < anchors; i++)
            {
                int b = i * 85;
                float score = o[b + 4] * o[b + 5];
                if (score < threshold)
                    continue;
                float s = gridStride[i];
                float cx = (o[b] + gridX[i]) * s, cy = (o[b + 1] + gridY[i]) * s;
                float bw = (float)Math.Exp(o[b + 2]) * s, bh = (float)Math.Exp(o[b + 3]) * s;
                found.Add(new SwimBox((float)((cx - bw / 2) / r), (float)((cy - bh / 2) / r), (float)((cx + bw / 2) / r), (float)((cy + bh / 2) / r), score));
            }

            // Greedy NMS at IoU 0.45.
            List<SwimBox> kept = new List<SwimBox>();
            foreach (SwimBox c in found.OrderByDescending(x => x.Score))
            {
                if (kept.Any(k => Iou(k, c) > 0.45))
                    continue;
                kept.Add(c);
                if (kept.Count >= max)
                    break;
            }

            return kept;
        }

        public static double Iou(SwimBox a, SwimBox b)
        {
            float x0 = Math.Max(a.X0, b.X0), y0 = Math.Max(a.Y0, b.Y0), x1 = Math.Min(a.X1, b.X1), y1 = Math.Min(a.Y1, b.Y1);
            float inter = Math.Max(0, x1 - x0) * Math.Max(0, y1 - y0);
            float union = a.Width * a.Height + b.Width * b.Height - inter;
            return union > 0 ? inter / union : 0;
        }

        private static float[] Floats(OpenCvSharp.Mat m)
        {
            using (OpenCvSharp.Mat c = m.IsContinuous() ? m.Clone() : m.Clone())
            {
                int n = (int)c.Total();
                float[] v = new float[n];
                System.Runtime.InteropServices.Marshal.Copy(c.Data, v, 0, n);
                return v;
            }
        }

        public void Dispose()
        {
            net.Dispose();
        }
    }

    /// <summary>
    /// (2.9.9) The path of the swimmer through the detections of a reading: the chain of boxes (one per image at most, gaps
    /// of up to MaxGap images allowed) that best agrees with the scores, a smooth motion and a steady size (dynamic
    /// programming over the candidates of each image). The images of the gaps get the boxes in between (Observed false).
    /// Every: the detector ran on one image in Every; a gap costs per detection missed (not per image), so that a chain
    /// read on one image in three is worth as much as one read on every image.
    /// </summary>
    public static class SwimTrackPath
    {
        public static List<SwimBox> Find(List<List<SwimBox>> detections, out bool[] observed, int candidates = 6, int maxGap = 12,
            double motion = 20.0, double gap = 0.6, double size = 1.5, double start = 2.0, double reward = 2.5, float minScore = 0.05f, int every = 1)
        {
            every = Math.Max(1, every);
            int n = detections.Count;
            List<SwimBox>[] cand = new List<SwimBox>[n];
            for (int t = 0; t < n; t++)
                cand[t] = detections[t].Where(b => b.Score >= minScore).Take(candidates).ToList();

            double[][] cost = new double[n][];
            int[][] prevT = new int[n][], prevI = new int[n][];
            double bestCost = double.MaxValue;
            int bestT = -1, bestI = -1;
            for (int t = 0; t < n; t++)
            {
                int m = cand[t].Count;
                cost[t] = new double[m];
                prevT[t] = new int[m];
                prevI[t] = new int[m];
                for (int j = 0; j < m; j++)
                {
                    SwimBox b = cand[t][j];
                    double node = -Math.Log(Math.Max(b.Score, 1e-3)) - reward;
                    double c0 = start + node;
                    int pt = -1, pi = -1;
                    for (int g = 1; g <= maxGap && t - g >= 0; g++)
                    {
                        int tp = t - g;
                        for (int i = 0; i < cand[tp].Count; i++)
                        {
                            double c = cost[tp][i] + Transition(cand[tp][i], b, g, every, motion, gap, size) + node;
                            if (c < c0)
                            {
                                c0 = c;
                                pt = tp;
                                pi = i;
                            }
                        }
                    }

                    cost[t][j] = c0;
                    prevT[t][j] = pt;
                    prevI[t][j] = pi;
                    if (c0 < bestCost)
                    {
                        bestCost = c0;
                        bestT = t;
                        bestI = j;
                    }
                }
            }

            List<SwimBox> path = new List<SwimBox>(new SwimBox[n]);
            observed = new bool[n];
            if (bestT < 0)
                return path;

            SortedDictionary<int, SwimBox> chosen = new SortedDictionary<int, SwimBox>();
            int ct = bestT, ci = bestI;
            while (ct >= 0)
            {
                chosen[ct] = cand[ct][ci];
                int nt = prevT[ct][ci], ni = prevI[ct][ci];
                ct = nt;
                ci = ni;
            }

            int[] keys = chosen.Keys.ToArray();
            for (int k = 0; k < keys.Length; k++)
            {
                int t = keys[k];
                path[t] = chosen[t];
                observed[t] = true;
                if (k + 1 < keys.Length)
                {
                    int t2 = keys[k + 1];
                    for (int u = t + 1; u < t2; u++)
                        path[u] = SwimBox.Lerp(chosen[t], chosen[t2], (u - t) / (float)(t2 - t));
                }
            }

            return path;
        }

        private static double Transition(SwimBox a, SwimBox b, int g, int every, double motion, double gap, double size)
        {
            double s = 0.5 * (a.Diagonal + b.Diagonal) + 1e-6;
            double dx = b.CenterX - a.CenterX, dy = b.CenterY - a.CenterY;
            double d = (dx * dx + dy * dy) / (s * s) / g;
            double r = Math.Abs(Math.Log((b.Width * b.Height + 1) / (a.Width * a.Height + 1)));
            double missed = Math.Max(0.0, g / (double)every - 1);
            return motion * d + gap * missed + size * r / Math.Sqrt(g);
        }
    }

    /// <summary>(2.9.9) One image of a technical reading: the swimmer's box, the skeleton, the motion of the camera.</summary>
    public sealed class SwimTechFrame
    {
        /// <summary>Seconds from the first image of the video.</summary>
        public double T;
        public SwimBox Box;
        public bool Observed;

        /// <summary>
        /// The images where the detector ran: its boxes (at most 6, best first) as x0, y0, x1, y1, score each, empty when it
        /// found none; null on the other images. The path can be found again from them (and the detector taught).
        /// </summary>
        public float[] Detections;

        /// <summary>17 joints (SwimJoints): x, y, visibility 0–1, in the pixels of the reading; null without a skeleton.</summary>
        public float[] Pose;
        public float PoseConfidence;

        /// <summary>Direction of the head used for the skeleton region: 0 left, 1 right, 2 up, 3 down.</summary>
        public int Direction = -1;

        /// <summary>Motion of the background since the image before (pixels): the camera's pan; Features: points used.</summary>
        public float CameraDx, CameraDy;
        public int Features;

        /// <summary>
        /// Parts of the image (0–1): Water (blue-green, saturated), Bubbles (white: low saturation, bright), Body (the rest:
        /// skin, suit, cap, ropes, walls); Motion: mean absolute difference of the brightness from the image before (0–255).
        /// For a camera fixed on the wall (the front view of a turn): the swimmer close to the camera, the cloud of the push-off.
        /// </summary>
        public float Water, Bubbles, Body, Motion;

        /// <summary>
        /// The mean horizontal change of the brightness in RowBins bands of the height of the image (top first, 0–255): the
        /// lane rope at the surface makes the strongest band of a side view under water (the level of the breakout).
        /// </summary>
        public float[] Rows;
    }

    /// <summary>(2.9.9) A technical reading of a video: one frame per image read, at Fps, in images of Width × Height.</summary>
    public sealed class SwimTechReading
    {
        public string Video;
        public double Fps, VideoFps;
        public int Width, Height;
        public double Start, End;
        public List<SwimTechFrame> Frames = new List<SwimTechFrame>();
    }

    /// <summary>
    /// (2.9.9) The reading of the swimmer in a part of a video for the technical analysis, in two passes over the images:
    /// 1. the swimmer detector on each image and the motion of the background (the camera that follows the swimmer);
    /// 2. the path of the swimmer through the detections (SwimTrackPath) and the skeleton in each box of the path (the
    /// landmark model of SwimPoseNet, the region turned to the direction of the head: the one of the image before first,
    /// the others when the model does not see a body there).
    /// The images come from a source that can be read twice (pass 1 and pass 2), at the same times.
    /// </summary>
    public sealed class SwimTechReader
    {
        public delegate void ImageSink(OpenCvSharp.Mat bgr, double t);
        public delegate void ImageSource(ImageSink sink);

        public float DetectionThreshold = 0.05f;
        public float PoseThreshold = 0.3f;

        /// <summary>The detector on one image in DetectEvery (the path fills the others; the camera motion and the skeleton on all).</summary>
        public int DetectEvery = 1;

        /// <summary>The path crosses at most this many detections in a row without the swimmer (the bubbles of a turn, a breakout).</summary>
        public int MaxGapDetections = 20;

        /// <summary>Boxes as x0, y0, x1, y1, score (at most max, in the order given).</summary>
        public static float[] Flatten(List<SwimBox> boxes, int max)
        {
            int n = Math.Min(max, boxes.Count);
            float[] a = new float[n * 5];
            for (int i = 0; i < n; i++)
            {
                SwimBox b = boxes[i];
                a[i * 5] = (float)Math.Round(b.X0, 1);
                a[i * 5 + 1] = (float)Math.Round(b.Y0, 1);
                a[i * 5 + 2] = (float)Math.Round(b.X1, 1);
                a[i * 5 + 3] = (float)Math.Round(b.Y1, 1);
                a[i * 5 + 4] = (float)Math.Round(b.Score, 4);
            }

            return a;
        }

        public SwimTechReading Read(ImageSource source, SwimSwimmerDetector detector, SwimPoseNet pose, Action<string, double> progress = null)
        {
            SwimTechReading reading = new SwimTechReading();
            List<List<SwimBox>> detections = new List<List<SwimBox>>();
            List<SwimTechFrame> frames = reading.Frames;
            OpenCvSharp.Mat previousGray = null;
            SwimBox previousBest = null;
            int count = 0;
            source((bgr, t) =>
            {
                if (count == 0)
                {
                    reading.Width = bgr.Width;
                    reading.Height = bgr.Height;
                    reading.Start = t;
                }

                reading.End = t;
                bool detect = count % Math.Max(1, DetectEvery) == 0;
                List<SwimBox> found = detect ? detector.Detect(bgr, DetectionThreshold) : new List<SwimBox>();
                detections.Add(found);
                SwimTechFrame f = new SwimTechFrame { T = t };
                if (detect)
                    f.Detections = Flatten(found, 6);
                OpenCvSharp.Mat gray = SmallGray(bgr);
                ImageParts(bgr, f);
                if (previousGray != null)
                {
                    using (OpenCvSharp.Mat diff = new OpenCvSharp.Mat())
                    {
                        OpenCvSharp.Cv2.Absdiff(previousGray, gray, diff);
                        f.Motion = (float)OpenCvSharp.Cv2.Mean(diff).Val0;
                    }

                    float dx, dy;
                    int n;
                    BackgroundMotion(previousGray, gray, previousBest, bgr.Width / (float)gray.Width, out dx, out dy, out n);
                    f.CameraDx = dx;
                    f.CameraDy = dy;
                    f.Features = n;
                    previousGray.Dispose();
                }

                previousGray = gray;
                previousBest = found.Count > 0 ? found[0] : previousBest;
                frames.Add(f);
                count++;
                if (progress != null && count % 10 == 0)
                    progress("detect", t);
            });
            if (previousGray != null)
                previousGray.Dispose();

            bool[] observed;
            int every = Math.Max(1, DetectEvery);
            List<SwimBox> path = SwimTrackPath.Find(detections, out observed, maxGap: MaxGapDetections * every, every: every);
            for (int i = 0; i < frames.Count; i++)
            {
                frames[i].Box = path[i];
                frames[i].Observed = observed[i];
            }

            if (pose != null)
            {
                int k = 0, direction = -1;
                source((bgr, t) =>
                {
                    if (k >= frames.Count)
                        return;
                    SwimTechFrame f = frames[k++];
                    if (f.Box == null)
                        return;
                    SwimPose best = null;
                    int bestDirection = -1;
                    foreach (int d in Directions(direction))
                    {
                        SwimPose p = Landmarks(pose, bgr, f.Box, d);
                        if (p != null && (best == null || p.Confidence > best.Confidence))
                        {
                            best = p;
                            bestDirection = d;
                        }

                        if (best != null && best.Confidence >= 0.8f)
                            break;
                    }

                    if (best != null)
                    {
                        f.Pose = new float[SwimJoints.Count * 3];
                        for (int j = 0; j < SwimJoints.Count; j++)
                        {
                            f.Pose[j * 3] = best.X[j];
                            f.Pose[j * 3 + 1] = best.Y[j];
                            f.Pose[j * 3 + 2] = best.Visibility[j];
                        }

                        f.PoseConfidence = best.Confidence;
                        f.Direction = bestDirection;
                        direction = bestDirection;
                    }

                    if (progress != null && k % 10 == 0)
                        progress("pose", t);
                });
            }

            return reading;
        }

        private static IEnumerable<int> Directions(int first)
        {
            if (first >= 0)
                yield return first;
            foreach (int d in new[] { 0, 1, 2, 3 })
            {
                if (d != first)
                    yield return d;
            }
        }

        /// <summary>The skeleton in a box, the region turned to the head in that direction (0 left, 1 right, 2 up, 3 down).</summary>
        public SwimPose Landmarks(SwimPoseNet pose, OpenCvSharp.Mat bgr, SwimBox box, int direction)
        {
            float l = Math.Max(box.Width, box.Height) * 0.55f;
            PointF hip = new PointF(box.CenterX, box.CenterY);
            float dx = direction == 0 ? -1 : direction == 1 ? 1 : 0;
            float dy = direction == 2 ? -1 : direction == 3 ? 1 : 0;
            PointF body = new PointF(hip.X + dx * l, hip.Y + dy * l);
            return pose.Landmarks(bgr, hip, body, PoseThreshold);
        }

        /// <summary>The parts of water, bubbles and the rest in the image (at most 320 pixels wide).</summary>
        public static void ImageParts(OpenCvSharp.Mat bgr, SwimTechFrame f)
        {
            int w = Math.Min(320, bgr.Width);
            int h = Math.Max(2, (int)Math.Round(bgr.Height * w / (double)bgr.Width));
            using (OpenCvSharp.Mat small = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat hsv = new OpenCvSharp.Mat())
            {
                OpenCvSharp.Cv2.Resize(bgr, small, new OpenCvSharp.Size(w, h), 0, 0, OpenCvSharp.InterpolationFlags.Area);
                OpenCvSharp.Cv2.CvtColor(small, hsv, OpenCvSharp.ColorConversionCodes.BGR2HSV);
                byte[] px = new byte[w * h * 3];
                System.Runtime.InteropServices.Marshal.Copy(hsv.Data, px, 0, px.Length);
                int water = 0, bubbles = 0, n = w * h;
                for (int i = 0; i < px.Length; i += 3)
                {
                    int hue = px[i], sat = px[i + 1], val = px[i + 2];
                    if (hue >= 80 && hue <= 110 && sat >= 70)
                        water++;
                    else if (sat < 60 && val > 170)
                        bubbles++;
                }

                f.Water = water / (float)n;
                f.Bubbles = bubbles / (float)n;
                f.Body = 1f - f.Water - f.Bubbles;

                // The horizontal change of the brightness along the height of the image, in RowBins bands: the lane rope
                // and the water surface (the blocks of the rope) make the strongest band of a side view under water.
                float[] rows = new float[RowBins];
                int[] counts = new int[RowBins];
                for (int y = 0; y < h; y++)
                {
                    int band = Math.Min(RowBins - 1, y * RowBins / h);
                    int o = y * w * 3;
                    double sum = 0;
                    for (int x = 1; x < w; x++)
                        sum += Math.Abs(px[o + x * 3 + 2] - px[o + (x - 1) * 3 + 2]);
                    rows[band] += (float)(sum / Math.Max(1, w - 1));
                    counts[band]++;
                }

                for (int k = 0; k < RowBins; k++)
                    rows[k] = (float)Math.Round(rows[k] / Math.Max(1, counts[k]), 1);
                f.Rows = rows;
            }
        }

        /// <summary>Bands of the height of the image in SwimTechFrame.Rows.</summary>
        public const int RowBins = 48;

        private static OpenCvSharp.Mat SmallGray(OpenCvSharp.Mat bgr)
        {
            OpenCvSharp.Mat gray = new OpenCvSharp.Mat();
            using (OpenCvSharp.Mat g = new OpenCvSharp.Mat())
            {
                OpenCvSharp.Cv2.CvtColor(bgr, g, OpenCvSharp.ColorConversionCodes.BGR2GRAY);
                int w = Math.Min(640, bgr.Width);
                int h = Math.Max(2, (int)Math.Round(bgr.Height * w / (double)bgr.Width));
                OpenCvSharp.Cv2.Resize(g, gray, new OpenCvSharp.Size(w, h), 0, 0, OpenCvSharp.InterpolationFlags.Area);
            }

            return gray;
        }

        /// <summary>
        /// The median motion of the background between two grey images (pixels of the reading): features outside the
        /// swimmer's box (with a margin), followed by the pyramidal Lucas–Kanade flow.
        /// </summary>
        public static void BackgroundMotion(OpenCvSharp.Mat a, OpenCvSharp.Mat b, SwimBox swimmer, float scale, out float dx, out float dy, out int count)
        {
            dx = dy = float.NaN;
            count = 0;
            using (OpenCvSharp.Mat mask = new OpenCvSharp.Mat(a.Rows, a.Cols, OpenCvSharp.MatType.CV_8UC1, OpenCvSharp.Scalar.All(255)))
            {
                if (swimmer != null)
                {
                    float pad = 0.15f * Math.Max(swimmer.Width, swimmer.Height);
                    int x0 = (int)Math.Max(0, (swimmer.X0 - pad) / scale), y0 = (int)Math.Max(0, (swimmer.Y0 - pad) / scale);
                    int x1 = (int)Math.Min(a.Cols, (swimmer.X1 + pad) / scale), y1 = (int)Math.Min(a.Rows, (swimmer.Y1 + pad) / scale);
                    if (x1 > x0 && y1 > y0)
                        mask[new OpenCvSharp.Rect(x0, y0, x1 - x0, y1 - y0)].SetTo(OpenCvSharp.Scalar.All(0));
                }

                OpenCvSharp.Point2f[] p0 = OpenCvSharp.Cv2.GoodFeaturesToTrack(a, 400, 0.01, 7, mask, 3, false, 0.04);
                if (p0 == null || p0.Length < 10)
                    return;
                OpenCvSharp.Point2f[] p1 = new OpenCvSharp.Point2f[0];
                byte[] status;
                float[] err;
                OpenCvSharp.Cv2.CalcOpticalFlowPyrLK(a, b, p0, ref p1, out status, out err, new OpenCvSharp.Size(21, 21), 3);
                List<float> xs = new List<float>(), ys = new List<float>();
                for (int i = 0; i < p0.Length; i++)
                {
                    if (status[i] == 1)
                    {
                        xs.Add(p1[i].X - p0[i].X);
                        ys.Add(p1[i].Y - p0[i].Y);
                    }
                }

                count = xs.Count;
                if (count < 10)
                    return;
                xs.Sort();
                ys.Sort();
                dx = xs[count / 2] * scale;
                dy = ys[count / 2] * scale;
            }
        }
    }
}
