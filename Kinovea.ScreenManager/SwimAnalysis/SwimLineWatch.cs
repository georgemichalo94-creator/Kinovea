#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using Kinovea.Video;

namespace Kinovea.ScreenManager
{
    /// <summary>(Swim 2.28.0) Where the head of the swimmer was in one image of a camera: seconds from the horn, part of the width.</summary>
    public struct SwimLineSample
    {
        public double Seconds;
        public float Head;

        public SwimLineSample(double seconds, float head)
        {
            Seconds = seconds;
            Head = head;
        }
    }

    /// <summary>
    /// (Swim 2.28.0) The line of one camera of the wall at its distance in the pool (5, 10 or 15 m from the wall of the start) and the
    /// moment the head of the swimmer reaches it, from the live images. The camera does not move: the swimmer is what is not as before
    /// (the water, the ropes and the bottom stay the same, learned while nobody passes). Seen from the side in a streamline, the arms
    /// go thin in front of the head: the head is where the body becomes thick (HeadShare of its thickest part, the shoulders), and the
    /// time of the line is when it reaches it, in proportion between two images. The swimmer is darker or lighter than the water: the
    /// changes of the other kind (the bubbles, mostly) do not count once his kind is clear. The head cannot go ahead faster than a
    /// swimmer (a cloud of bubbles in front of him is not his head). Live, from the images shown (≈): the exact frame is checked on the
    /// video in the analysis. The places of the head after the horn stay (Samples), for his speed.
    /// </summary>
    public sealed class SwimLineWatch
    {
        /// <summary>The width of the small grey image the watch works on (its height keeps the shape of the image).</summary>
        public const int Columns = 160;

        /// <summary>A column is of the swimmer when its longest run of changed pixels reaches this part of the height of the image.</summary>
        public const float BodyMinimum = 0.035f;

        /// <summary>The head: from the front, the first place where the body is this part of its thickest part.</summary>
        public const float HeadShare = 0.55f;

        /// <summary>The swimmer is at least this part of the width of the image (less: bubbles, a reflection, the noise).</summary>
        public const float SwimmerMinimum = 0.08f;

        /// <summary>The smallest change of grey (0–255) that counts, whatever the noise of the camera.</summary>
        public const float ChangeMinimum = 14f;

        /// <summary>The changes count from this many times the usual change of the image (its noise, the light on the water).</summary>
        public const float NoiseFactor = 3.5f;

        /// <summary>How fast the water is learned where nobody passes (part of the new image in each image).</summary>
        public const float Learning = 0.08f;

        /// <summary>Images of the head past the line before its time is kept (a single image past it can be a mistake).</summary>
        public const int Confirm = 2;

        /// <summary>Seconds without the head (bubbles over him, an image lost) after which his last place is not used any more.</summary>
        public const double HeadGap = 0.3;

        /// <summary>The fastest the head goes ahead, in widths of the image per second (a swimmer of 5 m/s in a view of 2.3 m).</summary>
        public const float MaxSpeed = 2.2f;

        /// <summary>The changes of one kind (darker or lighter than the water) are the swimmer when they are this many times the others.</summary>
        public const float KindShare = 1.5f;

        /// <summary>The places of the head kept for one attempt at most.</summary>
        public const int MaxSamples = 4000;

        /// <summary>
        /// The way the swimmers go in the images of all the cameras of the wall (they are all on the same side of the pool), as the
        /// first watch that saw it found it: +1 to the right, -1 to the left, 0 not known.
        /// </summary>
        public static int SharedDirection;

        /// <summary>The line, a part of the width of the image (0.5: the middle).</summary>
        public float Line = 0.5f;

        /// <summary>+1: the swimmer goes to the right of the image, -1: to the left, 0: from his motion (or SharedDirection).</summary>
        public int Direction;

        /// <summary>The time the head reached the line, in the seconds given with the images (after 0, the horn); NaN before.</summary>
        public double Crossed = double.NaN;

        /// <summary>Where the head is in the last image (part of the width), NaN without a swimmer.</summary>
        public float Head = float.NaN;

        /// <summary>The swimmer is in the last image.</summary>
        public bool Swimmer;

        /// <summary>The places of the head after the horn, in the order of the images.</summary>
        public readonly List<SwimLineSample> Samples = new List<SwimLineSample>();

        /// <summary>The way he goes, as known now (Direction, else what the motion said, else SharedDirection).</summary>
        public int Way
        {
            get { return Direction != 0 ? Direction : (found != 0 ? found : SharedDirection); }
        }

        private float[] background;
        private float[] gray;
        private float[] change;
        private sbyte[] kind;
        private readonly float[] thickness = new float[Columns];
        private readonly int[] histogram = new int[256];
        private int rows;
        private int found;
        private float firstCenter = float.NaN;
        private float lastHead = float.NaN;
        private double lastHeadSeconds = double.NaN;
        private double pending = double.NaN;
        private int pendingImages;

        /// <summary>A new attempt: the time of the line and the places of the head go, the water learned stays.</summary>
        public void Reset()
        {
            Crossed = double.NaN;
            pending = double.NaN;
            pendingImages = 0;
            lastHead = float.NaN;
            lastHeadSeconds = double.NaN;
            firstCenter = float.NaN;
            Samples.Clear();
        }

        /// <summary>The camera or its image changed: everything is learned again.</summary>
        public void Forget()
        {
            Reset();
            background = null;
            found = 0;
            Head = float.NaN;
            Swimmer = false;
        }

        /// <summary>An image of the camera and its time (seconds from the horn; before the horn negative or NaN: only learned).</summary>
        public void Feed(Bitmap image, double seconds)
        {
            if (image == null || image.Width < 16 || image.Height < 16)
                return;

            int height = Math.Max(8, (int)Math.Round(Columns * (double)image.Height / image.Width));
            if (gray == null || gray.Length != Columns * height)
            {
                gray = new float[Columns * height];
                background = null;
            }

            if (!Small(image, gray, Columns, height))
                return;

            FeedGray(gray, height, seconds);
        }

        /// <summary>A small grey image (Columns wide, 'height' rows, 0–255, the rows from the top) and its time.</summary>
        public void FeedGray(float[] image, int height, double seconds)
        {
            if (image == null || height < 4 || image.Length != Columns * height)
                return;

            if (background == null || background.Length != image.Length || rows != height)
            {
                rows = height;
                background = (float[])image.Clone();
                change = new float[image.Length];
                kind = new sbyte[image.Length];
                Head = float.NaN;
                Swimmer = false;
                return;
            }

            // The changes since the water learned, counted from the usual change of this image (its noise).
            Array.Clear(histogram, 0, histogram.Length);
            for (int i = 0; i < image.Length; i++)
            {
                float d = image[i] - background[i];
                kind[i] = (sbyte)(d < 0 ? -1 : 1);
                d = Math.Abs(d);
                change[i] = d;
                histogram[Math.Min(255, (int)d)]++;
            }

            int half = image.Length / 2, count = 0, median = 0;
            for (; median < 255; median++)
            {
                count += histogram[median];
                if (count >= half)
                    break;
            }

            float threshold = Math.Max(ChangeMinimum, NoiseFactor * Math.Max(1, median));

            int from, to;
            Thickness(threshold, 0);
            Stretch(out from, out to);

            // Darker or lighter than the water: once clear, only his kind of change counts (not the bubbles of the other kind).
            if (from >= 0 && to - from + 1 >= SwimmerMinimum * Columns)
            {
                int dark = 0, light = 0;
                for (int y = 0; y < rows; y++)
                {
                    for (int x = from; x <= to; x++)
                    {
                        int i = y * Columns + x;
                        if (change[i] < threshold)
                            continue;

                        if (kind[i] < 0)
                            dark++;
                        else
                            light++;
                    }
                }

                int only = dark >= KindShare * light ? -1 : (light >= KindShare * dark ? 1 : 0);
                if (only != 0)
                {
                    Thickness(threshold, only);
                    Stretch(out from, out to);
                }
            }

            Swimmer = from >= 0 && to - from + 1 >= SwimmerMinimum * Columns;
            if (!Swimmer)
            {
                Learn(image, -1, -1);
                Head = float.NaN;
                if (ForgetHead(seconds))
                    firstCenter = float.NaN;

                return;
            }

            // The water is learned away from him.
            Learn(image, from - 3, to + 3);

            // The way he goes: from his motion since he came (or the way of the other cameras).
            float center = (from + to + 1) / 2f / Columns;
            if (float.IsNaN(firstCenter))
                firstCenter = center;

            if (found == 0 && Math.Abs(center - firstCenter) > 0.06f)
            {
                found = center > firstCenter ? 1 : -1;
                if (SharedDirection == 0)
                    SharedDirection = found;
            }

            int way = Way;
            if (way == 0)
            {
                Head = float.NaN;
                ForgetHead(seconds);
                return;
            }

            // The thickest part of the body (the shoulders), robust to one wrong column: the second largest.
            float max1 = 0, max2 = 0;
            for (int x = from; x <= to; x++)
            {
                float t = thickness[x];
                if (t > max1)
                {
                    max2 = max1;
                    max1 = t;
                }
                else if (t > max2)
                {
                    max2 = t;
                }
            }

            float target = HeadShare * Math.Max(max2, BodyMinimum);
            Head = HeadPlace(from, to, way, target);

            // Not further than a swimmer can go since the last place of the head.
            if (!float.IsNaN(Head) && !float.IsNaN(lastHead) && !double.IsNaN(seconds) && !double.IsNaN(lastHeadSeconds) && seconds > lastHeadSeconds)
            {
                float limit = lastHead + way * (float)(MaxSpeed * (seconds - lastHeadSeconds) + 0.03);
                if ((Head - limit) * way > 0)
                {
                    int column = (int)(limit * Columns);
                    if (way > 0)
                        Head = HeadPlace(from, Math.Max(from, Math.Min(to, column)), way, target);
                    else
                        Head = HeadPlace(Math.Min(to, Math.Max(from, column)), to, way, target);
                }
            }

            if (float.IsNaN(Head))
            {
                ForgetHead(seconds);
                return;
            }

            // The line: the head passes it between the last image and this one, in proportion; kept when it stays past it.
            if (double.IsNaN(Crossed))
            {
                float now = (Head - Line) * way;
                if (!double.IsNaN(pending))
                {
                    if (now >= 0)
                    {
                        pendingImages++;
                        if (pendingImages >= Confirm)
                            Crossed = pending;
                    }
                    else if (now < -0.02f)
                    {
                        pending = double.NaN;
                        pendingImages = 0;
                    }
                }
                else if (!float.IsNaN(lastHead) && !double.IsNaN(lastHeadSeconds) && !double.IsNaN(seconds) && seconds > lastHeadSeconds)
                {
                    float before = (lastHead - Line) * way;
                    if (before < 0 && now >= 0)
                    {
                        double t = lastHeadSeconds + (seconds - lastHeadSeconds) * (0 - before) / (now - before);
                        if (t >= 0)
                        {
                            pending = t;
                            pendingImages = 1;
                            if (pendingImages >= Confirm)
                                Crossed = pending;
                        }
                    }
                }
            }

            if (!double.IsNaN(seconds) && seconds >= 0 && Samples.Count < MaxSamples)
                Samples.Add(new SwimLineSample(seconds, Head));

            lastHead = Head;
            lastHeadSeconds = seconds;
        }

        /// <summary>Each column: its longest run of changed pixels of a kind (0: both), a gap of one pixel allowed, a part of the height.</summary>
        private void Thickness(float threshold, int only)
        {
            for (int x = 0; x < Columns; x++)
            {
                int best = 0, run = 0, gap = 0;
                for (int y = 0; y < rows; y++)
                {
                    int i = y * Columns + x;
                    if (change[i] >= threshold && (only == 0 || kind[i] == only))
                    {
                        run += 1 + gap;
                        gap = 0;
                        if (run > best)
                            best = run;
                    }
                    else if (run > 0 && gap == 0)
                    {
                        gap = 1;
                    }
                    else
                    {
                        run = 0;
                        gap = 0;
                    }
                }

                thickness[x] = (float)best / rows;
            }
        }

        /// <summary>The swimmer: the longest stretch of thick columns (gaps of two columns allowed); -1, -1 without one.</summary>
        private void Stretch(out int from, out int to)
        {
            from = -1;
            to = -1;
            int start = -1, last = -1;
            for (int x = 0; x < Columns; x++)
            {
                if (thickness[x] < BodyMinimum)
                    continue;

                if (start < 0 || x - last > 3)
                    start = x;

                last = x;
                if (from < 0 || to - from < last - start)
                {
                    from = start;
                    to = last;
                }
            }
        }

        /// <summary>No head in this image: his last place is forgotten after HeadGap (true when it is).</summary>
        private bool ForgetHead(double seconds)
        {
            if (float.IsNaN(lastHead))
                return true;

            if (double.IsNaN(seconds) || double.IsNaN(lastHeadSeconds) || seconds - lastHeadSeconds > HeadGap || seconds < lastHeadSeconds)
            {
                lastHead = float.NaN;
                lastHeadSeconds = double.NaN;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The head, a part of the width: from the front (the side he goes to), the place where the thickness first reaches 'target',
        /// in proportion between the centres of two columns.
        /// </summary>
        private float HeadPlace(int from, int to, int way, float target)
        {
            if (way > 0)
            {
                for (int x = to; x >= from; x--)
                {
                    if (thickness[x] < target)
                        continue;

                    if (x == to)
                        return (x + 1f) / Columns;

                    float inside = thickness[x], outside = thickness[x + 1];
                    float k = inside - outside > 1e-6f ? (inside - target) / (inside - outside) : 0;
                    return (x + 0.5f + Math.Max(0, Math.Min(1, k))) / Columns;
                }
            }
            else
            {
                for (int x = from; x <= to; x++)
                {
                    if (thickness[x] < target)
                        continue;

                    if (x == from)
                        return (float)x / Columns;

                    float inside = thickness[x], outside = thickness[x - 1];
                    float k = inside - outside > 1e-6f ? (inside - target) / (inside - outside) : 0;
                    return (x + 0.5f - Math.Max(0, Math.Min(1, k))) / Columns;
                }
            }

            return float.NaN;
        }

        /// <summary>The water is learned in the columns outside 'from'–'to' (all of them when both are negative).</summary>
        private void Learn(float[] image, int from, int to)
        {
            for (int x = 0; x < Columns; x++)
            {
                if (x >= from && x <= to)
                    continue;

                for (int y = 0; y < rows; y++)
                {
                    int i = y * Columns + x;
                    background[i] += Learning * (image[i] - background[i]);
                }
            }
        }

        /// <summary>
        /// The small grey image of a picture: each of its pixels the mean of four points of its part of the picture (rows from the
        /// top). False for a picture it cannot read.
        /// </summary>
        public static unsafe bool Small(Bitmap image, float[] target, int columns, int height)
        {
            if (image == null || target == null || target.Length != columns * height)
                return false;

            PixelFormat format = image.PixelFormat;
            int step;
            switch (format)
            {
                case PixelFormat.Format24bppRgb:
                    step = 3;
                    break;
                case PixelFormat.Format32bppRgb:
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                    step = 4;
                    break;
                case PixelFormat.Format8bppIndexed:
                    step = 1;
                    break;
                default:
                    return false;
            }

            int w = image.Width, h = image.Height;
            BitmapData data = null;
            try
            {
                data = image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, format);
                byte* scan0 = (byte*)data.Scan0;
                int stride = data.Stride;
                for (int cy = 0; cy < height; cy++)
                {
                    int y1 = Math.Min(h - 1, (int)((cy + 0.25) * h / height));
                    int y2 = Math.Min(h - 1, (int)((cy + 0.75) * h / height));
                    byte* row1 = scan0 + (long)y1 * stride;
                    byte* row2 = scan0 + (long)y2 * stride;
                    for (int cx = 0; cx < columns; cx++)
                    {
                        int x1 = Math.Min(w - 1, (int)((cx + 0.25) * w / columns)) * step;
                        int x2 = Math.Min(w - 1, (int)((cx + 0.75) * w / columns)) * step;
                        float sum;
                        if (step == 1)
                            sum = row1[x1] + row1[x2] + row2[x1] + row2[x2];
                        else
                            sum = Grey(row1 + x1) + Grey(row1 + x2) + Grey(row2 + x1) + Grey(row2 + x2);

                        target[cy * columns + cx] = sum / 4f;
                    }
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (data != null)
                    image.UnlockBits(data);
            }
        }

        /// <summary>The grey of a pixel stored blue, green, red.</summary>
        private static unsafe float Grey(byte* p)
        {
            return 0.114f * p[0] + 0.587f * p[1] + 0.299f * p[2];
        }
    }

    /// <summary>
    /// (Swim 2.30.0) The line of a camera read again in its recorded file once the recording stopped: every image of the file (the live
    /// watch sees only the images shown), each timed from the horn as the analysis times it (its frame from the horn's frame, at the
    /// images per second of the capture). The time of the line and the places of the head to the frame. One file at a time, below the
    /// priority of the recording, so that the next attempt loses no image.
    /// </summary>
    public static class SwimLineFile
    {
        /// <summary>
        /// Seconds read before the time of the line seen live: the water is learned while nobody passes (a swimmer at 2 m/s enters an
        /// image of 3 m 0.75 s before its middle).
        /// </summary>
        public const double LeadIn = 2.5;

        /// <summary>Seconds read after the time of the line seen live (the head goes on to the edge of the image).</summary>
        public const double After = 1.0;

        /// <summary>Without the line seen live: from this long before the horn to Longest seconds after it.</summary>
        public const double Before = 0.5;
        public const double Longest = 20.0;

        private static readonly object queue = new object();
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// The line of a recorded file read again in the background; 'done' gets the watch that read it (null: the file could not be read)
        /// on the thread of the reading. 'hornFrame': the frame of the horn in the file (the frames from its first image); 'fps': the
        /// images per second of the capture; 'way': the way the swimmer went live (0: not known); 'crossed': the time of the line live.
        /// </summary>
        public static void ReadLater(string path, double hornFrame, double fps, float line, int way, double crossed, Action<SwimLineWatch> done)
        {
            Thread thread = new Thread(() =>
            {
                SwimLineWatch watch = null;
                try
                {
                    lock (queue)
                        watch = Read(path, hornFrame, fps, line, way, crossed);
                }
                catch (Exception e)
                {
                    log.Error("Error while reading the line of a camera in its recorded file.", e);
                    watch = null;
                }

                if (done != null)
                    done(watch);
            });
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.BelowNormal;
            thread.Name = "Swim line file";
            thread.Start();
        }

        /// <summary>The line of a recorded file read again (see ReadLater); null when the file cannot be read.</summary>
        public static SwimLineWatch Read(string path, double hornFrame, double fps, float line, int way, double crossed)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path) || fps <= 0 || double.IsNaN(hornFrame) || hornFrame < 0)
                return null;

            VideoReader reader = null;
            try
            {
                reader = VideoTypeManager.GetVideoReader(System.IO.Path.GetExtension(path));
                if (reader == null)
                    return null;

                reader.Options = VideoOptions.Default;
                if (reader.Open(path) != OpenVideoResult.Success)
                    return null;

                VideoInfo info = reader.Info;
                double perFrame = info.AverageTimeStampsPerFrame;
                if (perFrame <= 0)
                    return null;

                bool seen = !double.IsNaN(crossed);
                double fromFrame = Math.Max(0, hornFrame + (seen ? Math.Max(-Before, crossed - LeadIn) : -Before) * fps);
                double toFrame = hornFrame + (seen ? crossed + After : Longest) * fps;
                long from = info.FirstTimeStamp + (long)Math.Round(fromFrame * perFrame);
                long to = info.FirstTimeStamp + (long)Math.Round(toFrame * perFrame);

                SwimLineWatch watch = new SwimLineWatch { Line = line, Direction = way };
                int images = 0;
                reader.BeforeFrameEnumeration();
                try
                {
                    bool more = reader.MoveTo(from, from);
                    while (true)
                    {
                        VideoFrame frame = reader.Current;
                        if (frame == null || frame.Image == null || frame.Timestamp > to)
                            break;

                        double index = (frame.Timestamp - info.FirstTimeStamp) / perFrame;
                        watch.Feed(frame.Image, (index - hornFrame) / fps);
                        images++;
                        if (!more)
                            break;

                        more = reader.MoveNext(0, true);
                    }
                }
                finally
                {
                    reader.AfterFrameEnumeration();
                }

                log.DebugFormat("Line read again in its file: {0} images, line at {1:0.000} s (live {2:0.000} s), {3} places of the head.",
                    images, watch.Crossed, crossed, watch.Samples.Count);
                return images > 0 ? watch : null;
            }
            finally
            {
                if (reader != null)
                {
                    try
                    {
                        if (reader.Loaded)
                            reader.Close();
                    }
                    catch (Exception e)
                    {
                        log.Error("Could not close the recorded file read for its line.", e);
                    }
                }
            }
        }
    }
}
