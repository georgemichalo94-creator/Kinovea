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
using System.Runtime.InteropServices;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Joints of the skeleton kept from the 33 points of the pose model (MediaPipe BlazePose), in this order.
    /// </summary>
    public static class SwimJoints
    {
        public const int Count = 17;
        public const int Nose = 0;
        public const int LeftShoulder = 1, RightShoulder = 2, LeftElbow = 3, RightElbow = 4, LeftWrist = 5, RightWrist = 6;
        public const int LeftHip = 7, RightHip = 8, LeftKnee = 9, RightKnee = 10, LeftAnkle = 11, RightAnkle = 12;
        public const int LeftHeel = 13, RightHeel = 14, LeftToe = 15, RightToe = 16;

        /// <summary>Index of each joint in the 33 points of the model.</summary>
        public static readonly int[] FromModel = { 0, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };

        /// <summary>Lines of the skeleton.</summary>
        public static readonly int[][] Bones =
        {
            new[] { LeftShoulder, RightShoulder }, new[] { LeftHip, RightHip },
            new[] { LeftShoulder, LeftHip }, new[] { RightShoulder, RightHip },
            new[] { LeftShoulder, LeftElbow }, new[] { LeftElbow, LeftWrist },
            new[] { RightShoulder, RightElbow }, new[] { RightElbow, RightWrist },
            new[] { LeftHip, LeftKnee }, new[] { LeftKnee, LeftAnkle }, new[] { LeftAnkle, LeftHeel }, new[] { LeftHeel, LeftToe }, new[] { LeftAnkle, LeftToe },
            new[] { RightHip, RightKnee }, new[] { RightKnee, RightAnkle }, new[] { RightAnkle, RightHeel }, new[] { RightHeel, RightToe }, new[] { RightAnkle, RightToe },
            new[] { Nose, LeftShoulder }, new[] { Nose, RightShoulder }
        };

        /// <summary>Left and right joints of the arms and of the legs (the model sometimes mixes the sides in a side view).</summary>
        public static readonly int[][] ArmPairs = { new[] { LeftShoulder, RightShoulder }, new[] { LeftElbow, RightElbow }, new[] { LeftWrist, RightWrist } };
        public static readonly int[][] LegPairs =
        {
            new[] { LeftHip, RightHip }, new[] { LeftKnee, RightKnee }, new[] { LeftAnkle, RightAnkle }, new[] { LeftHeel, RightHeel }, new[] { LeftToe, RightToe }
        };

        public static bool IsLeft(int joint)
        {
            return joint != Nose && joint % 2 == 1;
        }
    }

    /// <summary>The skeleton in one image (pixels of that image) and the region of the swimmer for the next image.</summary>
    public sealed class SwimPose
    {
        public readonly float[] X = new float[SwimJoints.Count];
        public readonly float[] Y = new float[SwimJoints.Count];

        /// <summary>0–1: the joint is in the image and not hidden.</summary>
        public readonly float[] Visibility = new float[SwimJoints.Count];

        public float Confidence;

        /// <summary>Hip centre and full body point of the model: the region followed in the next image.</summary>
        public PointF Hip, Body;

        public float BodySize
        {
            get { return (float)Math.Sqrt((Body.X - Hip.X) * (Body.X - Hip.X) + (Body.Y - Hip.Y) * (Body.Y - Hip.Y)); }
        }

        public void Swap(int a, int b)
        {
            float t = X[a]; X[a] = X[b]; X[b] = t;
            t = Y[a]; Y[a] = Y[b]; Y[b] = t;
            t = Visibility[a]; Visibility[a] = Visibility[b]; Visibility[b] = t;
        }

        public void Scale(float sx, float sy)
        {
            for (int i = 0; i < SwimJoints.Count; i++)
            {
                X[i] *= sx;
                Y[i] *= sy;
            }

            Hip = new PointF(Hip.X * sx, Hip.Y * sy);
            Body = new PointF(Body.X * sx, Body.Y * sy);
        }
    }

    /// <summary>A person found by the detector: box, hip centre and full body point (pixels of the image).</summary>
    public sealed class SwimPersonCandidate
    {
        public RectangleF Box;
        public PointF Hip, Body;
        public float Score;
    }

    /// <summary>
    /// Arithmetic of the two MediaPipe pose models (person detector 224 × 224, landmarks 256 × 256), as in the OpenCV model zoo
    /// but with the region of the landmarks cut directly from the whole image (rotated), as MediaPipe does.
    /// </summary>
    public static class SwimPoseMath
    {
        public const int DetectorSize = 224;
        public const int LandmarkSize = 256;
        public const int Anchors = 2254;

        private static float[] anchors;

        /// <summary>Centres of the 2254 anchors of the detector (x, y in 0–1): strides 8, 16, 32 (×3), 2 anchors per layer and cell.</summary>
        public static float[] AnchorCentres
        {
            get
            {
                if (anchors == null)
                    anchors = GenerateAnchors();
                return anchors;
            }
        }

        public static float[] GenerateAnchors()
        {
            int[] strides = { 8, 16, 32, 32, 32 };
            List<float> result = new List<float>();
            int layer = 0;
            while (layer < strides.Length)
            {
                int last = layer;
                while (last + 1 < strides.Length && strides[last + 1] == strides[layer])
                    last++;

                int perCell = 2 * (last - layer + 1);
                int size = (int)Math.Ceiling(DetectorSize / (double)strides[layer]);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        for (int a = 0; a < perCell; a++)
                        {
                            result.Add((float)((x + 0.5) / size));
                            result.Add((float)((y + 0.5) / size));
                        }
                    }
                }

                layer = last + 1;
            }

            return result.ToArray();
        }

        public static float Sigmoid(float v)
        {
            v = Math.Max(-100f, Math.Min(100f, v));
            return (float)(1.0 / (1.0 + Math.Exp(-v)));
        }

        /// <summary>Size and place of an image of w × h in the square input of the detector (aspect kept, centred).</summary>
        public static void Letterbox(int w, int h, out double ratio, out int newW, out int newH, out int padLeft, out int padTop)
        {
            ratio = Math.Min(DetectorSize / (double)h, DetectorSize / (double)w);
            newW = Math.Max(1, (int)(w * ratio));
            newH = Math.Max(1, (int)(h * ratio));
            padLeft = (DetectorSize - newW) / 2;
            padTop = (DetectorSize - newH) / 2;
        }

        /// <summary>
        /// The persons of the detector outputs (boxes: 2254 × 12, scores: 2254 logits) in the pixels of the image given to it
        /// (w × h, letterboxed), best first.
        /// </summary>
        public static List<SwimPersonCandidate> DecodeDetections(float[] boxes, float[] scores, int w, int h, float scoreThreshold, float nmsThreshold)
        {
            float[] a = AnchorCentres;
            double ratio;
            int newW, newH, padLeft, padTop;
            Letterbox(w, h, out ratio, out newW, out newH, out padLeft, out padTop);
            double scale = Math.Max(w, h);
            double biasX = (int)(padLeft / ratio), biasY = (int)(padTop / ratio);

            List<SwimPersonCandidate> found = new List<SwimPersonCandidate>();
            int n = Math.Min(Anchors, Math.Min(scores.Length, boxes.Length / 12));
            for (int i = 0; i < n; i++)
            {
                float score = Sigmoid(scores[i]);
                if (score < scoreThreshold)
                    continue;

                int o = i * 12;
                double cx = (boxes[o] / DetectorSize + a[2 * i]) * scale - biasX;
                double cy = (boxes[o + 1] / DetectorSize + a[2 * i + 1]) * scale - biasY;
                double bw = boxes[o + 2] / DetectorSize * scale;
                double bh = boxes[o + 3] / DetectorSize * scale;
                SwimPersonCandidate c = new SwimPersonCandidate
                {
                    Box = new RectangleF((float)(cx - bw / 2), (float)(cy - bh / 2), (float)bw, (float)bh),
                    Hip = new PointF((float)((boxes[o + 4] / DetectorSize + a[2 * i]) * scale - biasX), (float)((boxes[o + 5] / DetectorSize + a[2 * i + 1]) * scale - biasY)),
                    Body = new PointF((float)((boxes[o + 6] / DetectorSize + a[2 * i]) * scale - biasX), (float)((boxes[o + 7] / DetectorSize + a[2 * i + 1]) * scale - biasY)),
                    Score = score
                };
                found.Add(c);
            }

            // Greedy non-maximum suppression.
            found.Sort((x, y) => y.Score.CompareTo(x.Score));
            List<SwimPersonCandidate> kept = new List<SwimPersonCandidate>();
            foreach (SwimPersonCandidate c in found)
            {
                if (kept.All(k => Iou(k.Box, c.Box) <= nmsThreshold))
                    kept.Add(c);
                if (kept.Count >= 10)
                    break;
            }

            return kept;
        }

        public static double Iou(RectangleF a, RectangleF b)
        {
            float x1 = Math.Max(a.Left, b.Left), y1 = Math.Max(a.Top, b.Top);
            float x2 = Math.Min(a.Right, b.Right), y2 = Math.Min(a.Bottom, b.Bottom);
            double inter = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
            double union = a.Width * a.Height + b.Width * b.Height - inter;
            return union <= 0 ? 0 : inter / union;
        }

        /// <summary>
        /// Rotation of the region of a person: the body upright in the input of the landmark model (as the model zoo), radians.
        /// </summary>
        public static double RegionAngle(PointF hip, PointF body)
        {
            double radians = Math.PI / 2 - Math.Atan2(-(body.Y - hip.Y), body.X - hip.X);
            return radians - 2 * Math.PI * Math.Floor((radians + Math.PI) / (2 * Math.PI));
        }

        /// <summary>
        /// Affine transform (2 × 3, rows) from an image to the 256 × 256 input of the landmark model: the square of side twice the
        /// distance hip – full body point, centred on the hip and rotated. originX, originY, k: the image is a part of the
        /// whole image (from that origin) resized by k.
        /// </summary>
        public static double[] RegionTransform(PointF hip, PointF body, double originX, double originY, double k)
        {
            double full = Math.Max(1e-3, Math.Sqrt((body.X - hip.X) * (body.X - hip.X) + (body.Y - hip.Y) * (body.Y - hip.Y)));
            double s = LandmarkSize / (2 * full);
            double angle = RegionAngle(hip, body);
            double a = Math.Cos(angle), b = Math.Sin(angle);
            double g = s / k;
            double hx = (hip.X - originX) * k, hy = (hip.Y - originY) * k;
            double half = LandmarkSize / 2.0;
            return new double[]
            {
                g * a, g * b, half - g * (a * hx + b * hy),
                -g * b, g * a, half - g * (-b * hx + a * hy)
            };
        }

        /// <summary>The part of the whole image needed for the region (axis aligned, may go outside the image) and the shrink factor.</summary>
        public static void RegionCrop(PointF hip, PointF body, out RectangleF crop, out double k)
        {
            double full = Math.Max(1e-3, Math.Sqrt((body.X - hip.X) * (body.X - hip.X) + (body.Y - hip.Y) * (body.Y - hip.Y)));
            double half = full * Math.Sqrt(2) + 2;
            crop = RectangleF.FromLTRB((float)Math.Floor(hip.X - half), (float)Math.Floor(hip.Y - half), (float)Math.Ceiling(hip.X + half), (float)Math.Ceiling(hip.Y + half));
            k = Math.Min(1.0, LandmarkSize / (2 * full));
        }

        /// <summary>
        /// The skeleton from the outputs of the landmark model (39 × 5: x, y, z, visibility, presence in the 256 input) in the
        /// pixels of the whole image.
        /// </summary>
        public static SwimPose DecodeLandmarks(float[] landmarks, float confidence, PointF hip, PointF body)
        {
            if (landmarks == null || landmarks.Length < 39 * 5)
                return null;

            double full = Math.Max(1e-3, Math.Sqrt((body.X - hip.X) * (body.X - hip.X) + (body.Y - hip.Y) * (body.Y - hip.Y)));
            double s = LandmarkSize / (2 * full);
            double angle = RegionAngle(hip, body);
            double a = Math.Cos(angle), b = Math.Sin(angle);
            double half = LandmarkSize / 2.0;

            Func<int, PointF> point = i =>
            {
                double qx = landmarks[i * 5] - half, qy = landmarks[i * 5 + 1] - half;
                // Inverse of the rotation [[a, b], [-b, a]] is its transpose.
                return new PointF((float)(hip.X + (a * qx - b * qy) / s), (float)(hip.Y + (b * qx + a * qy) / s));
            };

            SwimPose pose = new SwimPose { Confidence = confidence };
            for (int j = 0; j < SwimJoints.Count; j++)
            {
                int m = SwimJoints.FromModel[j];
                PointF p = point(m);
                pose.X[j] = p.X;
                pose.Y[j] = p.Y;
                pose.Visibility[j] = Sigmoid(landmarks[m * 5 + 3]);
            }

            pose.Hip = point(33);
            pose.Body = point(34);
            return pose;
        }

        /// <summary>Left and right of the arms and of the legs as in the previous image, when the model mixed them.</summary>
        public static void KeepSides(SwimPose pose, SwimPose previous)
        {
            if (pose == null || previous == null)
                return;

            foreach (int[][] pairs in new[] { SwimJoints.ArmPairs, SwimJoints.LegPairs })
            {
                double same = 0, swapped = 0;
                foreach (int[] p in pairs)
                {
                    same += Distance(pose, p[0], previous, p[0]) + Distance(pose, p[1], previous, p[1]);
                    swapped += Distance(pose, p[1], previous, p[0]) + Distance(pose, p[0], previous, p[1]);
                }

                if (swapped < 0.7 * same)
                {
                    foreach (int[] p in pairs)
                        pose.Swap(p[0], p[1]);
                }
            }
        }

        private static double Distance(SwimPose a, int i, SwimPose b, int j)
        {
            double dx = a.X[i] - b.X[j], dy = a.Y[i] - b.Y[j];
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// The two pose models with OpenCV (CPU): person detector in a part of the image, skeleton in the region of a person.
    /// The model files (MediaPipe, Apache 2.0, from the OpenCV model zoo) are in SwimAnalysis\Models next to the program.
    /// </summary>
    public sealed class SwimPoseNet : IDisposable
    {
        public const string DetectorFile = "person_detection_mediapipe_2023mar.onnx";
        public const string LandmarkFile = "pose_estimation_mediapipe_2023mar.onnx";

        private readonly OpenCvSharp.Dnn.Net detector;
        private readonly OpenCvSharp.Dnn.Net landmarker;
        private readonly string[] detectorOutputs;
        private readonly string[] landmarkOutputs;

        /// <summary>SwimAnalysis\Models next to this assembly (the program folder).</summary>
        public static string ModelFolder
        {
            get
            {
                string folder = null;
                try
                {
                    folder = Path.GetDirectoryName(typeof(SwimPoseNet).Assembly.Location);
                }
                catch (Exception)
                {
                    // Assembly without a location: the program folder.
                }

                if (string.IsNullOrEmpty(folder))
                    folder = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(folder, "SwimAnalysis", "Models");
            }
        }

        public static bool Available
        {
            get { return File.Exists(Path.Combine(ModelFolder, DetectorFile)) && File.Exists(Path.Combine(ModelFolder, LandmarkFile)); }
        }

        public SwimPoseNet()
        {
            detector = OpenCvSharp.Dnn.Net.ReadNetFromONNX(Path.Combine(ModelFolder, DetectorFile));
            landmarker = OpenCvSharp.Dnn.Net.ReadNetFromONNX(Path.Combine(ModelFolder, LandmarkFile));
            if (detector == null || landmarker == null)
                throw new InvalidOperationException("The pose models could not be read.");

            foreach (OpenCvSharp.Dnn.Net net in new[] { detector, landmarker })
            {
                net.SetPreferableBackend(OpenCvSharp.Dnn.Backend.OPENCV);
                net.SetPreferableTarget(OpenCvSharp.Dnn.Target.CPU);
            }

            detectorOutputs = detector.GetUnconnectedOutLayersNames().Where(n => n != null).ToArray();
            landmarkOutputs = landmarker.GetUnconnectedOutLayersNames().Where(n => n != null).ToArray();
        }

        /// <summary>Persons in a part of the image (pixels of the whole image), best first.</summary>
        public List<SwimPersonCandidate> Detect(OpenCvSharp.Mat bgr, Rectangle part, float scoreThreshold = 0.4f)
        {
            Rectangle r = Rectangle.Intersect(part, new Rectangle(0, 0, bgr.Width, bgr.Height));
            if (r.Width < 32 || r.Height < 32)
                return new List<SwimPersonCandidate>();

            double ratio;
            int newW, newH, padLeft, padTop;
            SwimPoseMath.Letterbox(r.Width, r.Height, out ratio, out newW, out newH, out padLeft, out padTop);
            float[] boxes = null, scores = null;
            using (OpenCvSharp.Mat crop = new OpenCvSharp.Mat(bgr, new OpenCvSharp.Rect(r.X, r.Y, r.Width, r.Height)))
            using (OpenCvSharp.Mat resized = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat input = new OpenCvSharp.Mat(SwimPoseMath.DetectorSize, SwimPoseMath.DetectorSize, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.All(128)))
            {
                // Grey around the image: 0 after the normalisation, as the model zoo pads.
                OpenCvSharp.Cv2.Resize(crop, resized, new OpenCvSharp.Size(newW, newH), 0, 0, OpenCvSharp.InterpolationFlags.Linear);
                using (OpenCvSharp.Mat target = new OpenCvSharp.Mat(input, new OpenCvSharp.Rect(padLeft, padTop, newW, newH)))
                    resized.CopyTo(target);

                // RGB in [-1, 1], NCHW.
                using (OpenCvSharp.Mat blob = OpenCvSharp.Dnn.CvDnn.BlobFromImage(input, 1.0 / 127.5, new OpenCvSharp.Size(SwimPoseMath.DetectorSize, SwimPoseMath.DetectorSize),
                    new OpenCvSharp.Scalar(127.5, 127.5, 127.5), true, false))
                {
                    detector.SetInput(blob);
                    OpenCvSharp.Mat[] outs = detectorOutputs.Select(n => new OpenCvSharp.Mat()).ToArray();
                    try
                    {
                        detector.Forward(outs, detectorOutputs);
                        foreach (OpenCvSharp.Mat o in outs)
                        {
                            float[] values = Floats(o);
                            if (values.Length == SwimPoseMath.Anchors * 12)
                                boxes = values;
                            else if (values.Length == SwimPoseMath.Anchors)
                                scores = values;
                        }
                    }
                    finally
                    {
                        foreach (OpenCvSharp.Mat o in outs)
                            o.Dispose();
                    }
                }
            }

            if (boxes == null || scores == null)
                throw new InvalidOperationException("Unexpected outputs of the person detector.");

            List<SwimPersonCandidate> found = SwimPoseMath.DecodeDetections(boxes, scores, r.Width, r.Height, scoreThreshold, 0.3f);
            foreach (SwimPersonCandidate c in found)
            {
                c.Box.Offset(r.X, r.Y);
                c.Hip = new PointF(c.Hip.X + r.X, c.Hip.Y + r.Y);
                c.Body = new PointF(c.Body.X + r.X, c.Body.Y + r.Y);
            }

            return found;
        }

        /// <summary>The skeleton in the region of a person (pixels of the whole image), null if the model does not see a person.</summary>
        public SwimPose Landmarks(OpenCvSharp.Mat bgr, PointF hip, PointF body, float confidenceThreshold = 0.5f)
        {
            RectangleF want;
            double k;
            SwimPoseMath.RegionCrop(hip, body, out want, out k);
            Rectangle r = Rectangle.Intersect(Rectangle.Round(want), new Rectangle(0, 0, bgr.Width, bgr.Height));
            if (r.Width < 4 || r.Height < 4)
                return null;

            float[] landmarks = null;
            float confidence = 0;
            using (OpenCvSharp.Mat crop = new OpenCvSharp.Mat(bgr, new OpenCvSharp.Rect(r.X, r.Y, r.Width, r.Height)))
            using (OpenCvSharp.Mat small = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat warped = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat rgb = new OpenCvSharp.Mat())
            using (OpenCvSharp.Mat floats = new OpenCvSharp.Mat())
            {
                // Shrink with area interpolation first (as the model zoo), then one rotation into the 256 × 256 input.
                OpenCvSharp.Mat source = crop;
                double kx = 1, ky = 1;
                if (k < 0.999)
                {
                    int sw = Math.Max(1, (int)Math.Round(r.Width * k)), sh = Math.Max(1, (int)Math.Round(r.Height * k));
                    OpenCvSharp.Cv2.Resize(crop, small, new OpenCvSharp.Size(sw, sh), 0, 0, OpenCvSharp.InterpolationFlags.Area);
                    kx = sw / (double)r.Width;
                    ky = sh / (double)r.Height;
                    source = small;
                }

                double[] m = SwimPoseMath.RegionTransform(hip, body, r.X, r.Y, 0.5 * (kx + ky));
                using (OpenCvSharp.Mat matrix = new OpenCvSharp.Mat(2, 3, OpenCvSharp.MatType.CV_64FC1))
                {
                    for (int i = 0; i < 6; i++)
                        matrix.Set(i / 3, i % 3, m[i]);
                    OpenCvSharp.Cv2.WarpAffine(source, warped, matrix, new OpenCvSharp.Size(SwimPoseMath.LandmarkSize, SwimPoseMath.LandmarkSize),
                        OpenCvSharp.InterpolationFlags.Linear, OpenCvSharp.BorderTypes.Constant, OpenCvSharp.Scalar.All(0));
                }

                OpenCvSharp.Cv2.CvtColor(warped, rgb, OpenCvSharp.ColorConversionCodes.BGR2RGB);
                rgb.ConvertTo(floats, OpenCvSharp.MatType.CV_32FC3, 1.0 / 255.0);

                // NHWC input: the interleaved pixels of the image are already in that order.
                using (OpenCvSharp.Mat blob = floats.Reshape(1, new[] { 1, SwimPoseMath.LandmarkSize, SwimPoseMath.LandmarkSize, 3 }))
                {
                    landmarker.SetInput(blob);
                    OpenCvSharp.Mat[] outs = landmarkOutputs.Select(n => new OpenCvSharp.Mat()).ToArray();
                    try
                    {
                        landmarker.Forward(outs, landmarkOutputs);
                        foreach (OpenCvSharp.Mat o in outs)
                        {
                            float[] values = Floats(o);
                            if (values.Length == 39 * 5)
                                landmarks = values;
                            else if (values.Length == 1)
                                confidence = values[0];
                        }
                    }
                    finally
                    {
                        foreach (OpenCvSharp.Mat o in outs)
                            o.Dispose();
                    }
                }
            }

            if (landmarks == null)
                throw new InvalidOperationException("Unexpected outputs of the pose model.");
            if (confidence < confidenceThreshold)
                return null;

            return SwimPoseMath.DecodeLandmarks(landmarks, confidence, hip, body);
        }

        private static float[] Floats(OpenCvSharp.Mat m)
        {
            long total = m.Total() * m.Channels();
            float[] values = new float[total];
            if (total == 0)
                return values;

            if (m.IsContinuous())
            {
                Marshal.Copy(m.Data, values, 0, values.Length);
                return values;
            }

            using (OpenCvSharp.Mat copy = m.Clone())
                Marshal.Copy(copy.Data, values, 0, values.Length);
            return values;
        }

        public void Dispose()
        {
            detector.Dispose();
            landmarker.Dispose();
        }
    }

    /// <summary>
    /// Follows one swimmer from image to image: the region of the previous skeleton, the detector near the expected place
    /// when the swimmer is lost, no jump to another person, left and right kept.
    /// </summary>
    public sealed class SwimPoseTracker
    {
        private readonly float firstCrop;
        private PointF center;
        private PointF velocity;
        private float size;
        private SwimPose previous;
        private bool hasRegion;
        private PointF regionHip, regionBody;

        public SwimPoseTracker(PointF click, float firstCrop)
        {
            center = click;
            this.firstCrop = firstCrop;
            size = firstCrop;
        }

        /// <summary>Images in a row without the swimmer, after he was found.</summary>
        public int Lost { get; private set; }

        public bool Found
        {
            get { return previous != null; }
        }

        /// <summary>
        /// The swimmer in the next image. detect: persons in a part of the image; landmarks: skeleton in a region (hip, full
        /// body point); both in the pixels of the image.
        /// </summary>
        public SwimPose Next(Func<Rectangle, List<SwimPersonCandidate>> detect, Func<PointF, PointF, SwimPose> landmarks)
        {
            PointF predicted = new PointF(center.X + velocity.X, center.Y + velocity.Y);
            float body = Math.Max(40f, size / 3f);
            SwimPose pose = hasRegion ? landmarks(regionHip, regionBody) : null;
            if (pose != null && Dist(pose.Hip, predicted) > 0.8 * body)
                pose = null;

            if (pose == null)
            {
                float searched = previous != null ? size : firstCrop;
                Rectangle part = new Rectangle((int)(predicted.X - searched / 2), (int)(predicted.Y - searched / 2), (int)searched, (int)searched);
                SwimPersonCandidate best = null;
                double bestDistance = double.MaxValue;
                foreach (SwimPersonCandidate c in detect(part))
                {
                    double d = Dist(c.Hip, predicted);
                    if (previous != null && d > 1.5 * body)
                        continue;
                    if (d < bestDistance)
                    {
                        best = c;
                        bestDistance = d;
                    }
                }

                if (best != null)
                    pose = landmarks(best.Hip, best.Body);
                if (pose != null && previous != null && Dist(pose.Hip, predicted) > 1.5 * body)
                    pose = null;
            }

            if (pose == null)
            {
                hasRegion = false;
                if (previous != null)
                    Lost++;
                return null;
            }

            SwimPoseMath.KeepSides(pose, previous);
            if (previous != null)
                velocity = new PointF(0.6f * velocity.X + 0.4f * (pose.Hip.X - center.X), 0.6f * velocity.Y + 0.4f * (pose.Hip.Y - center.Y));
            center = pose.Hip;
            size = Math.Max(300f, Math.Min(1800f, 6f * pose.BodySize));
            regionHip = pose.Hip;
            regionBody = pose.Body;
            hasRegion = true;
            previous = pose;
            Lost = 0;
            return pose;
        }

        private static double Dist(PointF a, PointF b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }
    }
}
