#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>A start found by the start light: frame (index of the frames given) and how clear it is.</summary>
    public sealed class SwimStartCandidate
    {
        /// <summary>Frame of the flash (first frame with the light on).</summary>
        public int Frame;

        /// <summary>Flash of that frame: the lights that came on together (sum of their strengths).</summary>
        public double Flash;

        /// <summary>Flash × the race that follows (camera following the swimmers): the candidates are sorted by it.</summary>
        public double Strength;

        /// <summary>Lights at different places of the image that came on in that frame.</summary>
        public int Lights;

        /// <summary>Frame where the camera starts to follow the swimmers after this start.</summary>
        public int Follow;

        /// <summary>Places of the lights (working image), for the check.</summary>
        public List<double[]> Places = new List<double[]>();

        /// <summary>
        /// Where it comes from: "light" (a flash with the swimmers starting to move just after it), "light?" (a flash without that
        /// check: the dive was not seen), "weak light" (a weaker flash with the dive after it, the strong ones failed the check),
        /// "dive" (no flash fits: the start put 0.30 s before the swimmers start to move), "line" (no start that fits the race: LineDelay
        /// before the swimmers leave the blocks together, LineStarts), "light+sound" (the light seen just before
        /// the start signal heard), "sound" (no light seen: the signal heard less its usual delay).
        /// </summary>
        public string Source = "light";

        /// <summary>Frame where the swimmers start to move after it (−1: not seen).</summary>
        public int Dive = -1;

        /// <summary>Frame (fraction) of the start signal heard just after it (NaN: none heard).</summary>
        public double Beep = double.NaN;

        /// <summary>How clear the signal heard is (SwimBeep.Score, dB).</summary>
        public double BeepScore;
    }

    /// <summary>
    /// Start signal from the start lights: the strobes next to the blocks (or at the starter) flash when the start signal sounds.
    ///
    /// Frame by frame (brightness = max of the three colours, working size):
    /// 1. what came on: brightness above the brighter of the two previous frames (3×3 maximum), in units of the usual flicker of
    ///    each pixel (its mean frame difference over ~0.5 s): small bright spots that came on where the image was still;
    /// 2. each spot is followed for ~0.3 s: a light starts bright (≥ 140) from a still background and goes down again; a moving
    ///    object darkens next to where it brightens, water glints come back at the same place (flicker map of the last seconds);
    /// 3. the flash of a frame: its lights at different places (≥ 60 px apart, the strongest of each place), spots that already came on
    ///    there in the previous frames do not count, nor the sparkles of the water (less than 3 pixels at 960 wide);
    /// 4. the race: the camera starts to follow the swimmers (frame difference ≥ 2.5 × the still level for most of 1.5 s, then most
    ///    of the next 6 s); the start is the first strong flash (≥ 60% of the strongest) 0.3–1.6 s before, with a still camera;
    /// 5. the dive: the swimmers start to move 0.10–0.55 s after the signal (the area of the spots that come on rises to 3× its usual
    ///    level and stays); a flash without it, or with the swimmers already moving just before it (the flashes of the photographers
    ///    during the dive), is not the start. Then a weaker flash with the dive after it, else the start 0.30 s before the dive.
    /// Reference implementation and evaluation: Python prototype of the same algorithm (Paris 2026 and Šamorín 2025 races,
    /// 50 frames/s: the frame of the analyst in all of them).
    /// </summary>
    public sealed class SwimStartLight
    {
        private sealed class Blob
        {
            public int Frame;
            public int X0, Y0, W, H;
            public bool[] Mask;
            public int Area;
            public double Cx, Cy;
            public double ZMax;
            public double Dark, Bright;
            public double Rate;
            public double[] Prev;
            public List<double> Values = new List<double>();
        }

        private const double OnLevel = 25;
        private const double OnZ = 6;
        private const double NoiseFloor = 3;
        private const int CellSize = 8;

        /// <summary>Weaker flashes than this are not a start light (glints that came on at a new place).</summary>
        private const double MinFlash = 25;

        /// <summary>A spot lit for one frame only (a camera flash) counts little (tests).</summary>
        internal static bool Persistence = false;

        /// <summary>The flashes are checked with the dive of the swimmers after them (tests).</summary>
        internal static bool DiveCheck = true;

        /// <summary>Seconds from the start signal to the first movement of the swimmers seen by the dive check (median of the races).</summary>
        internal static double DiveDelay = 0.30;

        /// <summary>
        /// A light: the jump of its brightest pixel at least this many times the noise of the place (10 before the dive check: the
        /// strobes of the blocks of a sunny outdoor pool, 9.4–9.5, were missed; tests).
        /// </summary>
        internal static double LightZ = 9;

        private readonly int width;
        private readonly int height;
        private readonly double fps;
        private readonly int prevCount;
        private readonly int followCount;
        private readonly int repeatCount;
        private readonly int warmup;
        private readonly double noiseRate;
        private readonly double rateDecay;
        private readonly int cellsX;
        private readonly int cellsY;
        private readonly int minLightArea;

        private readonly List<byte[]> history = new List<byte[]>();
        private float[] noise;
        private float[] rate;
        private readonly List<Blob> following = new List<Blob>();
        private readonly List<List<Blob>> finished = new List<List<Blob>>();
        private readonly List<double> motion = new List<double>();
        private readonly List<Tuple<int, double>> follows = new List<Tuple<int, double>>();
        private int followChecked;
        private int frames;

        /// <summary>
        /// The motion of the swimmers at the blocks (since 2.8.0): cells of LineCell px at 960 wide, the mean change of the brightness
        /// of each cell from the frame before, each frame (zeros for the first).
        /// </summary>
        private readonly int lineCell;
        private readonly int lineCellsX;
        private readonly int lineCellsY;
        private readonly List<float[]> lineMotion = new List<float[]>();

        // Work buffers.
        private readonly byte[] reference;
        private readonly byte[] referenceMin;
        private readonly byte[] tmp;
        private readonly byte[] pass;
        private readonly short[] onset;
        private readonly int[] labels;
        private readonly int[] stack;

        /// <summary>Debug output (null: none).</summary>
        public static Action<string> Trace;

        public SwimStartLight(int width, int height, double fps)
        {
            this.width = width;
            this.height = height;
            this.fps = fps > 0 ? fps : 25;
            prevCount = Math.Max(3, (int)Math.Round(0.12 * this.fps));
            followCount = Math.Max(8, (int)Math.Round(0.32 * this.fps));
            repeatCount = Math.Max(2, (int)Math.Round(0.08 * this.fps));
            warmup = Math.Max(10, (int)Math.Round(0.6 * this.fps));
            noiseRate = Math.Min(0.5, 2.0 / this.fps);
            rateDecay = Math.Pow(0.5, 1.0 / (2.0 * this.fps));
            cellsX = (width + CellSize - 1) / CellSize;
            cellsY = (height + CellSize - 1) / CellSize;
            minLightArea = Math.Max(2, (int)Math.Round(3.0 * width * width / (960.0 * 960.0)));
            lineCell = Math.Max(8, (int)Math.Round(LineCell * width / 960.0));
            lineCellsX = (width + lineCell - 1) / lineCell;
            lineCellsY = (height + lineCell - 1) / lineCell;
            int n = width * height;
            reference = new byte[n];
            referenceMin = new byte[n];
            tmp = new byte[n];
            pass = new byte[n];
            onset = new short[n];
            labels = new int[n];
            stack = new int[n];
            rate = new float[cellsX * cellsY];
        }

        public int Count
        {
            get { return frames; }
        }

        /// <summary>Real frames per second of the frames given (high speed videos: of the capture).</summary>
        public double FramesPerSecond
        {
            get { return fps; }
        }

        /// <summary>Camera and image motion of each frame (median frame difference + % of pixels changed by more than 25).</summary>
        public IList<double> Motion
        {
            get { return motion; }
        }

        /// <summary>
        /// Add the next frame: brightness (max of B, G, R) at the working size, one byte per pixel, rows without padding.
        /// </summary>
        public void Add(byte[] brightness)
        {
            if (brightness == null || brightness.Length < width * height)
                throw new ArgumentException("Brightness image of the wrong size.");

            int n = width * height;
            byte[] v = history.Count > prevCount ? history[0] : new byte[n];
            if (history.Count > prevCount)
                history.RemoveAt(0);
            Buffer.BlockCopy(brightness, 0, v, 0, n);
            int index = frames;
            finished.Add(new List<Blob>());

            // The spots followed get the value of this frame.
            for (int k = following.Count - 1; k >= 0; k--)
            {
                Blob b = following[k];
                b.Values.Add(MaskMean(v, b));
                if (b.Values.Count >= followCount)
                {
                    finished[b.Frame].Add(b);
                    following.RemoveAt(k);
                }
            }

            if (history.Count == 0)
            {
                motion.Add(0);
                lineMotion.Add(new float[lineCellsX * lineCellsY]);
                history.Add(v);
                frames++;
                return;
            }

            byte[] v1 = history[history.Count - 1];
            lineMotion.Add(CellMotion(v, v1));
            byte[] v2 = history.Count >= 2 ? history[history.Count - 2] : null;
            bool haveReference = v2 != null;
            if (haveReference)
            {
                Rows((y0, y1) =>
                {
                    for (int p = y0 * width; p < y1 * width; p++)
                        tmp[p] = v1[p] > v2[p] ? v1[p] : v2[p];
                });
                Filter3(tmp, reference, true);
                Rows((y0, y1) =>
                {
                    for (int p = y0 * width; p < y1 * width; p++)
                    {
                        int o = v[p] - reference[p];
                        onset[p] = (short)(o > 0 ? o : 0);
                    }
                });
            }

            // Frame difference: the motion of the frame (and the usual flicker of each pixel, after the spots).
            int[] histogram = new int[256];
            int over = 0;
            object sync = new object();
            Rows((y0, y1) =>
            {
                int[] h = new int[256];
                int o = 0;
                for (int p = y0 * width; p < y1 * width; p++)
                {
                    int d = v[p] - v1[p];
                    if (d < 0)
                        d = -d;
                    h[d]++;
                    if (d > 25)
                        o++;
                }

                lock (sync)
                {
                    for (int k = 0; k < 256; k++)
                        histogram[k] += h[k];
                    over += o;
                }
            });

            double median = 0;
            int half = n / 2, run = 0;
            for (int d = 0; d < 256; d++)
            {
                run += histogram[d];
                if (run > half)
                {
                    median = d;
                    break;
                }
            }

            double frameMotion = median + 100.0 * over / n;
            motion.Add(frameMotion);

            // Spots that came on (with the flicker of the previous frames).
            if (haveReference && noise != null && index > warmup && !MovingCamera(frameMotion))
                FindSpots(v, v1, v2, index);

            // Usual flicker of each pixel and the places that came on lately.
            if (noise == null)
            {
                noise = new float[n];
                for (int p = 0; p < n; p++)
                    noise[p] = Math.Abs(v[p] - v1[p]);
            }
            else
            {
                float a = (float)noiseRate, b1 = 1 - a;
                Rows((y0, y1) =>
                {
                    for (int p = y0 * width; p < y1 * width; p++)
                    {
                        int d = v[p] - v1[p];
                        noise[p] = noise[p] * b1 + (d < 0 ? -d : d) * a;
                    }
                });
            }

            if (haveReference)
            {
                float decay = (float)rateDecay;
                for (int c = 0; c < rate.Length; c++)
                    rate[c] *= decay;

                // By rows of cells (no two threads on the same cell).
                System.Threading.Tasks.Parallel.For(0, cellsY, cy =>
                {
                    int yEnd = Math.Min(height, (cy + 1) * CellSize), cellRow = cy * cellsX;
                    for (int y = cy * CellSize; y < yEnd; y++)
                    {
                        int row = y * width;
                        for (int x = 0; x < width; x++)
                        {
                            if (onset[row + x] > OnLevel)
                                rate[cellRow + x / CellSize] += 1;
                        }
                    }
                });
            }

            history.Add(v);
            frames++;
        }

        /// <summary>Runs the body on bands of rows in parallel (rows y0 to y1 − 1).</summary>
        private void Rows(Action<int, int> body)
        {
            int bands = Math.Max(1, Math.Min(Environment.ProcessorCount * 2, height / 16));
            System.Threading.Tasks.Parallel.For(0, bands, b =>
            {
                int y0 = height * b / bands, y1 = height * (b + 1) / bands;
                body(y0, y1);
            });
        }

        /// <summary>3×3 maximum (or minimum) of the image, edges replicated: rows then columns.</summary>
        private void Filter3(byte[] src, byte[] dst, bool maximum)
        {
            Rows((y0, y1) =>
            {
                for (int y = y0; y < y1; y++)
                {
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        byte m = src[row + x];
                        if (x > 0) { byte q = src[row + x - 1]; if (maximum ? q > m : q < m) m = q; }
                        if (x < width - 1) { byte q = src[row + x + 1]; if (maximum ? q > m : q < m) m = q; }
                        pass[row + x] = m;
                    }
                }
            });

            Rows((y0, y1) =>
            {
                for (int y = y0; y < y1; y++)
                {
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        int p = row + x;
                        byte m = pass[p];
                        if (y > 0) { byte q = pass[p - width]; if (maximum ? q > m : q < m) m = q; }
                        if (y < height - 1) { byte q = pass[p + width]; if (maximum ? q > m : q < m) m = q; }
                        dst[p] = m;
                    }
                }
            });
        }

        /// <summary>The camera moves too much in this frame for a light to be seen (and the spots would be thousands).</summary>
        private bool MovingCamera(double frameMotion)
        {
            // motion already has this frame: the usual level is the second before it.
            int count = motion.Count;
            if (count < 10)
                return false;

            int from = Math.Max(0, count - 1 - (int)Math.Round(fps));
            List<double> recent = new List<double>();
            for (int k = from; k < count - 1; k++)
                recent.Add(motion[k]);
            double usual = Median(recent);
            return frameMotion > Math.Max(14, 3 * usual);
        }

        private void FindSpots(byte[] v, byte[] v1, byte[] v2, int index)
        {
            int n = width * height;
            int candidates = 0;
            object sync = new object();
            Rows((y0, y1) =>
            {
                int c = 0;
                for (int p = y0 * width; p < y1 * width; p++)
                {
                    float o = onset[p];
                    bool on = o > OnLevel && o > OnZ * (noise[p] + NoiseFloor);
                    labels[p] = on ? -1 : 0;
                    if (on)
                        c++;
                }

                lock (sync)
                    candidates += c;
            });

            // A change of the whole image (exposure, a flash of a camera in front): not lights.
            if (candidates == 0 || candidates > n / 20)
                return;

            Rows((y0, y1) =>
            {
                for (int p = y0 * width; p < y1 * width; p++)
                    tmp[p] = v1[p] < v2[p] ? v1[p] : v2[p];
            });
            Filter3(tmp, referenceMin, false);

            int label = 0;
            for (int start = 0; start < n; start++)
            {
                if (labels[start] != -1)
                    continue;

                // 8-connected spot.
                label++;
                int top = 0;
                stack[top++] = start;
                labels[start] = label;
                int count = 0, minX = width, minY = height, maxX = 0, maxY = 0;
                double sx = 0, sy = 0;
                List<int> pixels = new List<int>();
                while (top > 0)
                {
                    int p = stack[--top];
                    pixels.Add(p);
                    int x = p % width, y = p / width;
                    count++;
                    sx += x;
                    sy += y;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= height)
                            continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= width || (dx == 0 && dy == 0))
                                continue;
                            int q = yy * width + xx;
                            if (labels[q] == -1)
                            {
                                labels[q] = label;
                                stack[top++] = q;
                            }
                        }
                    }
                }

                if (count < 2 || count > 3000)
                    continue;

                Blob b = new Blob { Frame = index, Area = count, Cx = sx / count, Cy = sy / count };
                b.X0 = Math.Max(0, minX - 1);
                b.Y0 = Math.Max(0, minY - 1);
                b.W = Math.Min(width, maxX + 2) - b.X0;
                b.H = Math.Min(height, maxY + 2) - b.Y0;
                b.Mask = new bool[b.W * b.H];
                double zmax = 0, bright = 0;
                foreach (int p in pixels)
                {
                    int x = p % width, y = p / width;
                    b.Mask[(y - b.Y0) * b.W + (x - b.X0)] = true;
                    double z = onset[p] / (noise[p] + NoiseFloor);
                    if (z > zmax)
                        zmax = z;
                    bright += onset[p];
                }

                b.ZMax = zmax;
                b.Bright = bright;

                // Darkening next to the spot (8 px around): a moving object, not a light.
                double dark = 0;
                int ex0 = Math.Max(0, minX - 8), ey0 = Math.Max(0, minY - 8), ex1 = Math.Min(width - 1, maxX + 8), ey1 = Math.Min(height - 1, maxY + 8);
                for (int y = ey0; y <= ey1; y++)
                {
                    int row = y * width;
                    for (int x = ex0; x <= ex1; x++)
                    {
                        int d = referenceMin[row + x] - v[row + x];
                        if (d >= 20)
                            dark += d;
                    }
                }

                b.Dark = dark;

                // Places that came on lately around the spot (3×3 cells).
                int cx = Math.Min(cellsX - 1, (int)(b.Cx / CellSize)), cy = Math.Min(cellsY - 1, (int)(b.Cy / CellSize));
                double r = 0;
                for (int j = Math.Max(0, cy - 1); j <= Math.Min(cellsY - 1, cy + 1); j++)
                {
                    for (int i = Math.Max(0, cx - 1); i <= Math.Min(cellsX - 1, cx + 1); i++)
                        r += rate[j * cellsX + i];
                }

                b.Rate = r;

                // The spot in the previous frames.
                int prev = Math.Min(prevCount, history.Count);
                b.Prev = new double[prev];
                for (int k = 0; k < prev; k++)
                    b.Prev[k] = MaskMean(history[history.Count - prev + k], b);

                b.Values.Add(MaskMean(v, b));
                following.Add(b);
            }
        }

        private double MaskMean(byte[] image, Blob b)
        {
            double sum = 0;
            int count = 0;
            for (int y = 0; y < b.H; y++)
            {
                int row = (b.Y0 + y) * width + b.X0, mrow = y * b.W;
                for (int x = 0; x < b.W; x++)
                {
                    if (!b.Mask[mrow + x])
                        continue;
                    sum += image[row + x];
                    count++;
                }
            }

            return count > 0 ? sum / count : 0;
        }

        private static double Median(List<double> values)
        {
            if (values == null || values.Count == 0)
                return 0;

            List<double> v = values.OrderBy(x => x).ToList();
            int n = v.Count;
            return n % 2 == 1 ? v[n / 2] : 0.5 * (v[n / 2 - 1] + v[n / 2]);
        }

        #region Result
        /// <summary>Strength of the flash of each frame (0: no light came on) and the lights of it.</summary>
        private void Flashes(out double[] flash, out int[] lights, out List<double[]>[] places)
        {
            int n = frames;
            flash = new double[n];
            lights = new int[n];
            places = new List<double[]>[n];

            // The spots still followed (end of the frames given) count with what they have.
            List<Blob>[] all = new List<Blob>[n];
            for (int i = 0; i < n; i++)
                all[i] = new List<Blob>(finished[i]);
            foreach (Blob b in following)
                all[b.Frame].Add(b);

            // A light: bright from a still background, then down again; not a moving object.
            List<Blob>[] lit = new List<Blob>[n];
            for (int i = 0; i < n; i++)
            {
                lit[i] = new List<Blob>();
                foreach (Blob b in all[i])
                {
                    if (b.Prev == null || b.Prev.Length == 0 || b.Values.Count == 0)
                        continue;

                    double baseLevel = b.Prev.Average();
                    double sd = Math.Sqrt(b.Prev.Select(x => (x - baseLevel) * (x - baseLevel)).Average());
                    double first = b.Values[0];
                    double jump = first - baseLevel;
                    if (jump < 30 || b.ZMax < LightZ || first < 140)
                        continue;
                    if (sd > Math.Max(3.0, 0.15 * jump))
                        continue;
                    if (b.Values.Count > 2)
                    {
                        double low = b.Values.Skip(1).Take(11).Min();
                        if (low > baseLevel + 0.6 * jump)
                            continue;
                    }

                    if (b.Dark > 0.25 * Math.Max(b.Bright, 1))
                        continue;

                    lit[i].Add(b);
                }
            }

            for (int i = 0; i < n; i++)
            {
                List<Tuple<double, Blob>> scored = new List<Tuple<double, Blob>>();
                foreach (Blob b in lit[i])
                {
                    // Already came on there just before: not the start of a light.
                    bool again = false;
                    for (int j = Math.Max(0, i - repeatCount); j < i && !again; j++)
                        again = lit[j].Any(c => Math.Abs(c.Cx - b.Cx) < 15 && Math.Abs(c.Cy - b.Cy) < 15);
                    if (again)
                        continue;

                    // The glints of the water are sparkles of a pixel or two (several at once can look like a flash of lights).
                    if (b.Area < minLightArea)
                        continue;

                    double s = Math.Min(b.ZMax, 40) * Math.Sqrt(Math.Min(b.Area, 100)) / (1.0 + b.Rate / 25.0);

                    // A start light stays on for a few frames; the flash of a photographer's camera on the wet bodies (backstroke
                    // starts) lights a single frame.
                    if (Persistence && b.Values.Count > 1 && b.Prev != null && b.Prev.Length > 0)
                    {
                        double baseLevel = b.Prev.Average();
                        double jump = Math.Max(1e-6, b.Values[0] - baseLevel);
                        double still = (b.Values[1] - baseLevel) / jump;
                        s *= still <= 0.1 ? 0.15 : still >= 0.4 ? 1.0 : 0.15 + 0.85 * (still - 0.1) / 0.3;
                    }

                    scored.Add(Tuple.Create(s, b));
                }

                // One light per place (the strongest), 60 px apart.
                List<Blob> kept = new List<Blob>();
                foreach (Tuple<double, Blob> t in scored.OrderByDescending(t => t.Item1))
                {
                    if (kept.All(k => (k.Cx - t.Item2.Cx) * (k.Cx - t.Item2.Cx) + (k.Cy - t.Item2.Cy) * (k.Cy - t.Item2.Cy) > 60 * 60))
                    {
                        kept.Add(t.Item2);
                        flash[i] += t.Item1;
                    }
                }

                lights[i] = kept.Count;
                places[i] = kept.Select(k => new[] { k.Cx, k.Cy }).ToList();
            }
        }

        /// <summary>Frames where the camera starts to follow the race (with the still level before it).</summary>
        private List<Tuple<int, double>> FollowStarts()
        {
            UpdateFollows();
            List<Tuple<int, double>> result = new List<Tuple<int, double>>(follows);
            result.AddRange(ScanFollows(followChecked, motion.Count - (int)Math.Round(1.0 * fps)));
            return result;
        }

        /// <summary>The follow starts of the frames with their 1.5 s after them read (kept, not checked again).</summary>
        private void UpdateFollows()
        {
            int n = motion.Count;
            int last = Math.Min(n - (int)Math.Round(1.0 * fps), n - (int)Math.Round(1.5 * fps) - SmoothRadius);
            if (last <= followChecked)
                return;

            follows.AddRange(ScanFollows(followChecked, last));
            followChecked = last;
        }

        private int SmoothRadius
        {
            get { return Math.Max(1, (int)Math.Round(0.05 * fps)); }
        }

        /// <summary>Centred mean of the motion over 5 frames at 50 frames/s (zeros outside, as the reference).</summary>
        private double SmoothMotion(int i)
        {
            int r = SmoothRadius, n = motion.Count;
            double s = 0;
            for (int k = i - r; k <= i + r; k++)
            {
                if (k >= 0 && k < n)
                    s += motion[k];
            }

            return s / (2 * r + 1);
        }

        private List<Tuple<int, double>> ScanFollows(int from, int to)
        {
            List<Tuple<int, double>> result = new List<Tuple<int, double>>();
            int n = motion.Count;
            int baseLength = (int)Math.Round(1.0 * fps);
            int hold = (int)Math.Round(1.5 * fps);
            for (int i = Math.Max((int)Math.Round(1.5 * fps), from); i < to; i++)
            {
                double still = Median(motion.Skip(i - baseLength).Take(baseLength).ToList());
                double threshold = Math.Max(12, 2.5 * still);
                if (SmoothMotion(i) < threshold || SmoothMotion(i - 1) >= threshold)
                    continue;

                int end = Math.Min(n, i + hold), above = 0;
                for (int k = i; k < end; k++)
                {
                    if (SmoothMotion(k) >= threshold)
                        above++;
                }

                if (above < 0.8 * (end - i))
                    continue;

                result.Add(Tuple.Create(i, still));
            }

            return result;
        }

        /// <summary>
        /// The starts found in the frames given, the most likely first (empty: no light seen before a race).
        /// </summary>
        public List<SwimStartCandidate> Find()
        {
            double[] flash;
            int[] lights;
            List<double[]>[] places;
            Flashes(out flash, out lights, out places);
            List<SwimStartCandidate> result = new List<SwimStartCandidate>();
            int n = motion.Count;
            foreach (Tuple<int, double> follow in FollowStarts().Take(4))
            {
                int f = follow.Item1;
                double still = follow.Item2;
                int lo = (int)Math.Ceiling(f - 1.6 * fps - 1e-6), hi = (int)Math.Floor(f - 0.3 * fps + 1e-6);
                List<int> window = new List<int>();
                for (int j = Math.Max(0, lo); j <= Math.Min(n - 1, hi); j++)
                {
                    if (flash[j] > 0 && motion[j] <= Math.Max(10, 2.2 * still))
                        window.Add(j);
                }

                if (window.Count == 0)
                    continue;

                double max = window.Max(j => flash[j]);
                if (max < MinFlash)
                    continue;

                int first = window.Where(j => flash[j] >= 0.6 * max).Min();

                // The race after it: the camera keeps moving (most of the next 6 s).
                int end = Math.Min(n, f + (int)Math.Round(6 * fps)), moving = 0;
                for (int k = f; k < end; k++)
                {
                    if (motion[k] > Math.Max(10, 2 * still))
                        moving++;
                }

                double race = end > f ? (double)moving / (end - f) : 0;
                if (Trace != null)
                    Trace(string.Format("follow {0} still {1:0.0} -> flash {2} ({3:0} max {4:0}) race {5:0.00}", f, still, first, flash[first], max, race));

                if (result.Any(c => c.Frame == first))
                    continue;

                result.Add(new SwimStartCandidate
                {
                    Frame = first,
                    Flash = flash[first],
                    Strength = max * (0.5 + race),
                    Lights = lights[first],
                    Follow = f,
                    Places = places[first] ?? new List<double[]>()
                });
            }

            result = result.OrderByDescending(c => c.Strength).ToList();
            if (!DiveCheck)
                return result;

            // The dive of the swimmers after the flash: the flashes with it first.
            double[] spots;
            List<int> dives = DiveOnsets(out spots);
            foreach (SwimStartCandidate c in result)
            {
                int dive;
                c.Source = DiveAfter(c.Frame, dives, spots, out dive) ? "light" : "light?";
                c.Dive = dive;
            }

            List<SwimStartCandidate> checkedLights = result.Where(c => c.Source == "light").ToList();
            if (checkedLights.Count > 0)
                return checkedLights.Concat(result.Where(c => c.Source != "light")).ToList();

            // None: a weaker flash of the same windows with the dive after it, else the start just before the dive.
            List<Tuple<int, double>> follows4 = FollowStarts().Take(4).ToList();
            SwimStartCandidate weak = null;
            foreach (Tuple<int, double> follow in follows4)
            {
                int f = follow.Item1;
                double still = follow.Item2;
                int lo = (int)Math.Ceiling(f - 1.6 * fps - 1e-6), hi = (int)Math.Floor(f - 0.3 * fps + 1e-6);
                for (int j = Math.Max(0, lo); j <= Math.Min(n - 1, hi); j++)
                {
                    if (flash[j] < MinFlash || motion[j] > Math.Max(10, 2.2 * still) || (weak != null && flash[j] <= weak.Flash))
                        continue;

                    int dive;
                    if (!DiveAfter(j, dives, spots, out dive))
                        continue;

                    weak = new SwimStartCandidate
                    {
                        Frame = j, Flash = flash[j], Strength = flash[j], Lights = lights[j], Follow = f, Places = places[j] ?? new List<double[]>(),
                        Source = "weak light", Dive = dive
                    };
                }
            }

            if (weak != null)
            {
                result.Insert(0, weak);
                return result;
            }

            foreach (Tuple<int, double> follow in follows4)
            {
                int f = follow.Item1;
                int from = (int)Math.Round(f - 1.4 * fps), to = (int)Math.Round(f - 0.05 * fps);
                List<int> inWindow = dives.Where(x => x >= from && x <= to).ToList();
                if (inWindow.Count == 0)
                    continue;

                int dive = inWindow[0];
                result.Insert(0, new SwimStartCandidate
                {
                    Frame = Math.Max(0, dive - (int)Math.Round(DiveDelay * fps)), Flash = 0, Strength = 0, Lights = 0, Follow = f,
                    Source = "dive", Dive = dive
                });
                break;
            }

            return result;
        }

        /// <summary>
        /// Frames where the swimmers start to move: the area of the spots that came on (two frames) rises to 3× its usual level of
        /// the second before (lowest quarter of the frames with spots, at least 250 px at 960 wide) and stays there 0.12 s (frames
        /// without spots, the camera moving, do not count). spots: that area of each frame.
        /// </summary>
        private List<int> DiveOnsets(out double[] spots)
        {
            int n = frames;
            double[] area = new double[n];
            Action<Blob> add = b =>
            {
                if (b.Frame < 0 || b.Frame >= n || b.Prev == null || b.Prev.Length == 0 || b.Values.Count == 0)
                    return;
                double first = b.Values[0];
                if (first < 100 || first - b.Prev.Average() < 15)
                    return;
                area[b.Frame] += b.Area;
            };
            foreach (List<Blob> list in finished)
            {
                foreach (Blob b in list)
                    add(b);
            }

            foreach (Blob b in following)
                add(b);

            spots = new double[n];
            for (int i = 0; i < n; i++)
                spots[i] = area[i] + (i + 1 < n ? area[i + 1] : 0);

            double floor = 250.0 * width * width / (960.0 * 960.0);
            int baseFrom = (int)(1.5 * fps), baseTo = (int)(0.3 * fps), hold = (int)Math.Round(0.12 * fps);
            List<int> result = new List<int>();
            List<double> usual = new List<double>();
            for (int i = baseFrom; i < n - hold - 1; i++)
            {
                usual.Clear();
                for (int j = i - baseFrom; j < i - baseTo; j++)
                {
                    if (spots[j] > 0)
                        usual.Add(spots[j]);
                }

                if (usual.Count < 10)
                    continue;

                usual.Sort();
                double level = Math.Max(floor, 3 * usual[Math.Min(usual.Count - 1, (int)(0.25 * usual.Count))]);
                if (spots[i] < level || spots[i - 1] >= level)
                    continue;

                bool stays = true;
                for (int j = i; j < i + hold && stays; j++)
                    stays = spots[j] <= 0 || spots[j] >= 0.6 * level;
                if (stays)
                    result.Add(i);
            }

            return result;
        }

        /// <summary>
        /// The dive after a flash (frame): the swimmers start to move 0.10–0.55 s after it, and they were not already moving from
        /// 0.40 s before it into that dive without a pause (the flashes of the photographers on the swimmers diving).
        /// </summary>
        private bool DiveAfter(int frame, List<int> dives, double[] spots, out int dive)
        {
            dive = -1;
            int lo = frame + (int)Math.Round(0.10 * fps), hi = frame + (int)Math.Round(0.55 * fps);
            foreach (int x in dives)
            {
                if (x >= lo && x <= hi)
                {
                    dive = x;
                    break;
                }
            }

            if (dive < 0)
                return false;

            int before0 = frame - (int)Math.Round(0.40 * fps), before1 = frame - (int)Math.Round(0.04 * fps);
            foreach (int x in dives)
            {
                if (x < before0 || x >= before1)
                    continue;

                double min = double.MaxValue;
                bool any = false;
                for (int j = x; j <= dive && j < spots.Length; j++)
                {
                    if (spots[j] > 0)
                    {
                        any = true;
                        min = Math.Min(min, spots[j]);
                    }
                }

                if (any && min >= 0.3 * spots[x])
                    return false;
            }

            return true;
        }

        #region The swimmers leaving the blocks together
        /// <summary>Size of the cells of the motion at the blocks at 960 wide (px).</summary>
        internal const int LineCell = 32;

        /// <summary>
        /// Seconds from the start signal to the first frame of the swimmers' motion along the blocks found by LineStarts (median of
        /// the 42 videos of the lab where it was found within 0.15 s of the verified start, 2026-10-19).
        /// </summary>
        internal static double LineDelay = 0.18;

        /// <summary>The mean change of the brightness of each cell from the frame before (rows of cells).</summary>
        private float[] CellMotion(byte[] v, byte[] v1)
        {
            int cx = lineCellsX, cy = lineCellsY, size = lineCell, w = width, h = height;
            float[] cells = new float[cx * cy];
            System.Threading.Tasks.Parallel.For(0, cy, r =>
            {
                double[] sum = new double[cx];
                int[] count = new int[cx];
                int y0 = r * size, y1 = Math.Min(h, y0 + size);
                for (int y = y0; y < y1; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        int d = v[row + x] - v1[row + x];
                        int c = x / size;
                        sum[c] += d < 0 ? -d : d;
                        count[c]++;
                    }
                }

                for (int c = 0; c < cx; c++)
                    cells[r * cx + c] = count[c] > 0 ? (float)(sum[c] / count[c]) : 0;
            });
            return cells;
        }

        /// <summary>
        /// The starts seen from the swimmers leaving the blocks together (no light seen, no signal heard: an outdoor pool, the wind),
        /// in order of time: the frames where cells along a straight line (the blocks, the wall of a backstroke start) start to move
        /// together, more than they did over the second before (mean + 1 sd, ×1.5 + 1.5 levels of brightness over the next 0.2 s),
        /// with the camera still before (the median cell moved at most 4 levels) and the whole image moving at least twice as much over
        /// the second after (the swimmers in the air and in the water, the camera starting to follow): at least 10 cells on the line;
        /// the first frame of each such run of frames, the start LineDelay before it. Only runs with at least 15 cells on the line for
        /// at least 8 frames are kept. Lab 2026-10-19, 110 videos with verified starts: within 0.15 s of the start in 42 (the camera
        /// and the start end in view), the only run of the first 40 s of 7 of the 8 videos of an outdoor meet without signal.
        /// </summary>
        public List<SwimStartCandidate> LineStarts()
        {
            List<SwimStartCandidate> result = new List<SwimStartCandidate>();
            int n = lineMotion.Count, C = lineCellsX * lineCellsY;
            int b0 = Math.Max(2, (int)Math.Round(1.0 * fps)), b1 = Math.Max(1, (int)Math.Round(0.1 * fps));
            int a1 = Math.Max(1, (int)Math.Round(0.2 * fps)), a3 = Math.Max(a1 + 1, (int)Math.Round(1.0 * fps));
            if (n < b0 + a3 + 2 || C < 4)
                return result;

            // The median cell of each frame: the camera moving (all cells) or still.
            double[] g = new double[n];
            float[] sorted = new float[C];
            for (int t = 0; t < n; t++)
            {
                Array.Copy(lineMotion[t], sorted, C);
                Array.Sort(sorted);
                g[t] = C % 2 == 1 ? sorted[C / 2] : 0.5 * (sorted[C / 2 - 1] + sorted[C / 2]);
            }

            double[] x = new double[C], y = new double[C];
            for (int c = 0; c < C; c++)
            {
                x[c] = c % lineCellsX;
                y[c] = c / lineCellsX;
            }

            // Running sums of each cell over the second before [t - b0, t - b1) and over the next 0.2 s [t, t + a1).
            double[] s1 = new double[C], s2 = new double[C], ahead = new double[C];
            for (int k = 0; k < b0 - b1; k++)
            {
                float[] m = lineMotion[k];
                for (int c = 0; c < C; c++)
                {
                    s1[c] += m[c];
                    s2[c] += m[c] * (double)m[c];
                }
            }

            for (int k = b0; k < b0 + a1; k++)
            {
                float[] m = lineMotion[k];
                for (int c = 0; c < C; c++)
                    ahead[c] += m[c];
            }

            int span = b0 - b1;
            List<double> before = new List<double>(span);
            List<int> on = new List<int>(C);
            Random random = new Random(0);
            int[] line = new int[n];
            double[] scene = new double[n];
            for (int t = b0; t < n - a3; t++)
            {
                if (t > b0)
                {
                    float[] add = lineMotion[t - b1 - 1], drop = lineMotion[t - b0 - 1], come = lineMotion[t + a1 - 1], go = lineMotion[t - 1];
                    for (int c = 0; c < C; c++)
                    {
                        s1[c] += add[c] - drop[c];
                        s2[c] += add[c] * (double)add[c] - drop[c] * (double)drop[c];
                        ahead[c] += come[c] - go[c];
                    }
                }

                before.Clear();
                for (int k = t - b0; k < t - b1; k++)
                    before.Add(g[k]);
                double still = Median(before);
                if (still > LineStill)
                    continue;

                on.Clear();
                for (int c = 0; c < C; c++)
                {
                    double mean = s1[c] / span, sd = Math.Sqrt(Math.Max(0, s2[c] / span - mean * mean));
                    if (ahead[c] / a1 > 1.5 * (mean + sd) + 1.5)
                        on.Add(c);
                }

                if (on.Count < 4 || on.Count > C / 2)
                    continue;

                double after = 0;
                for (int k = t + a1; k < t + a3; k++)
                    after += g[k];
                after /= a3 - a1;
                scene[t] = after / (still + 0.3);
                if (scene[t] < LineScene)
                    continue;

                line[t] = BestLine(on, x, y, random);
            }

            // Runs of frames with enough cells on the line (gaps of up to two frames).
            int first = -1, last = -1, most = 0, runFrames = 0;
            Action close = () =>
            {
                if (first >= 0 && most >= LineStrongCells && runFrames >= LineStrongFrames)
                {
                    result.Add(new SwimStartCandidate
                    {
                        Frame = Math.Max(0, first - (int)Math.Round(LineDelay * fps)), Flash = 0, Strength = most, Lights = 0, Follow = -1,
                        Source = "line", Dive = first
                    });
                }

                first = -1;
                most = 0;
                runFrames = 0;
            };

            for (int t = 0; t < n; t++)
            {
                if (line[t] < LineCells)
                    continue;

                if (first >= 0 && t - last > 2)
                    close();
                if (first < 0)
                    first = t;
                last = t;
                most = Math.Max(most, line[t]);
                runFrames++;
            }

            close();
            if (Trace != null)
            {
                foreach (SwimStartCandidate c in result)
                    Trace(string.Format(System.Globalization.CultureInfo.InvariantCulture, "line start: frame {0} (motion from {1}, {2} cells)", c.Frame, c.Dive, c.Strength));
            }

            return result;
        }

        /// <summary>The cells moving together must be on a straight line: at least this many (LineStarts).</summary>
        internal const int LineCells = 10;

        /// <summary>Kept: at least this many cells on the line in one frame of the run, and so many frames (LineStarts).</summary>
        internal const int LineStrongCells = 15, LineStrongFrames = 8;

        /// <summary>The camera still before (the median cell, levels of brightness) and the image moving after (times) (LineStarts).</summary>
        internal const double LineStill = 4.0, LineScene = 2.0;

        /// <summary>
        /// The most cells of the list within 0.75 cell of one straight line through two of them at least 3 cells apart (all the pairs
        /// up to 300, else 300 pairs drawn).
        /// </summary>
        private static int BestLine(List<int> cells, double[] x, double[] y, Random random)
        {
            int m = cells.Count, best = 0;
            bool all = m * (m - 1) / 2 <= 300;
            int pairs = all ? m * (m - 1) / 2 : 300;
            int i = 0, j = 1;
            for (int p = 0; p < pairs; p++)
            {
                int a, b;
                if (all)
                {
                    a = i;
                    b = j;
                    j++;
                    if (j >= m)
                    {
                        i++;
                        j = i + 1;
                    }
                }
                else
                {
                    a = random.Next(m);
                    b = random.Next(m);
                    if (a == b)
                        continue;
                }

                double ax = x[cells[a]], ay = y[cells[a]], dx = x[cells[b]] - ax, dy = y[cells[b]] - ay;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 3)
                    continue;

                int count = 0;
                for (int k = 0; k < m; k++)
                {
                    double d = Math.Abs((x[cells[k]] - ax) * dy - (y[cells[k]] - ay) * dx) / length;
                    if (d <= 0.75)
                        count++;
                }

                if (count > best)
                    best = count;
            }

            return best;
        }
        #endregion

        #region Start signal heard
        /// <summary>
        /// The light of the start just before a start signal heard (frames from..to of the frames given): the clearest small flash
        /// that came on from a still background, at a place without glints lately, and went down again within 0.1 s (the strobe
        /// next to the starter: a few pixels in a phone video from the stands, too weak for the start lights), the nearer to the
        /// frame expected from the signal the better. A place that flashes again within 0.5 s before or after is not it (a screen,
        /// glints). Null: none.
        /// </summary>
        public SwimStartCandidate FlashBefore(int from, int to, double expected)
        {
            from = Math.Max(0, from);
            to = Math.Min(frames - 1, to);
            if (to < from)
                return null;

            List<Blob> blobs = new List<Blob>();
            for (int i = from; i <= to; i++)
                blobs.AddRange(finished[i]);
            blobs.AddRange(following.Where(b => b.Frame >= from && b.Frame <= to));

            int down = Math.Max(1, (int)Math.Round(0.1 * fps));
            double maxArea = 400.0 * width * width / (960.0 * 960.0);
            Blob best = null;
            double bestScore = 0;
            foreach (Blob b in blobs)
            {
                if (b.Prev == null || b.Prev.Length == 0 || b.Values.Count == 0 || b.Area > maxArea)
                    continue;

                double baseLevel = b.Prev.Average();
                double sd = Math.Sqrt(b.Prev.Select(x => (x - baseLevel) * (x - baseLevel)).Average());
                double first = b.Values[0];
                double jump = first - baseLevel;
                if (jump < 35 || first < 140 || b.ZMax < 7 || sd > Math.Max(3.0, 0.15 * jump))
                    continue;

                // Glints come back at the same place; a moving object darkens next to where it brightens.
                if (b.Rate > 5 || b.Dark > 0.1 * Math.Max(b.Bright, 1))
                    continue;

                if (b.Values.Count > 1 && b.Values.Skip(1).Take(down).Min() > baseLevel + 0.75 * jump)
                    continue;

                double score = Math.Min(b.ZMax, 40) * Math.Min(jump, 150) / (1.0 + b.Rate);
                if (!double.IsNaN(expected))
                    score *= SwimStartSound.Nearness(b.Frame, expected, fps);
                if (score > bestScore && !FlashesAgain(b, (int)Math.Round(0.5 * fps)))
                {
                    bestScore = score;
                    best = b;
                }
            }

            if (best == null)
                return null;

            if (Trace != null)
                Trace(string.Format("flash before the signal: frame {0} at {1:0},{2:0} area {3} z {4:0.0} score {5:0}", best.Frame, best.Cx, best.Cy, best.Area, best.ZMax, bestScore));

            return new SwimStartCandidate
            {
                Frame = best.Frame, Flash = Math.Round(bestScore / 10, 1), Strength = bestScore / 10, Lights = 1, Follow = -1,
                Places = new List<double[]> { new[] { best.Cx, best.Cy } }, Source = "light+sound"
            };
        }

        /// <summary>Another spot came on at the place of this one (15 px at 960 wide) within 'span' frames before or after it (not the next).</summary>
        private bool FlashesAgain(Blob b, int span)
        {
            double near = 15.0 * width / 960.0;
            Func<Blob, bool> same = c => c != b && Math.Abs(c.Frame - b.Frame) > 1 && Math.Abs(c.Frame - b.Frame) <= span &&
                Math.Abs(c.Cx - b.Cx) <= near && Math.Abs(c.Cy - b.Cy) <= near;
            for (int i = Math.Max(0, b.Frame - span); i <= Math.Min(frames - 1, b.Frame + span); i++)
            {
                if (finished[i].Any(same))
                    return true;
            }

            return following.Any(same);
        }

        /// <summary>Frame where the swimmers start to move 0.10–0.55 s after this frame (−1: not seen, or already moving before it).</summary>
        public int DiveAfterFrame(int frame)
        {
            double[] spots;
            List<int> dives = DiveOnsets(out spots);
            int dive;
            return DiveAfter(frame, dives, spots, out dive) ? dive : -1;
        }

        /// <summary>The first frame 0.10 s to 'seconds' after this frame where the swimmers start to move (−1: none).</summary>
        public int FirstDiveAfter(int frame, double seconds)
        {
            double[] spots;
            List<int> dives = DiveOnsets(out spots);
            int lo = frame + (int)Math.Round(0.10 * fps), hi = frame + (int)Math.Round(seconds * fps);
            foreach (int x in dives)
            {
                if (x >= lo && x <= hi)
                    return x;
            }

            return -1;
        }

        /// <summary>Frames where the camera starts to follow the race.</summary>
        public List<int> FollowFrames()
        {
            return FollowStarts().Select(f => f.Item1).ToList();
        }

        /// <summary>
        /// Median motion of the frames from 'from' to 'to' seconds before this frame (a start: the camera still and the swimmers
        /// set, low; a signal heard in the middle of a race: the camera following the swimmers, high). NaN: no frames there.
        /// </summary>
        public double MotionBefore(double frame, double from = 1.5, double to = 0.2)
        {
            int lo = Math.Max(1, (int)Math.Round(frame - from * fps)), hi = Math.Min(motion.Count - 1, (int)Math.Round(frame - to * fps));
            if (hi < lo)
                return double.NaN;

            return Median(motion.Skip(lo).Take(hi - lo + 1).ToList());
        }

        /// <summary>
        /// The camera was already following a race before this frame (a signal heard in the middle of a race: not a start): the motion
        /// of the 1.5 s before it (the last 0.2 s left out) is that of a camera moving (more than 8) and at least 0.6 of the motion of
        /// the 3 s after it. Before a start the camera is still on the swimmers set, and it follows them after it.
        /// </summary>
        public bool InRace(double frame)
        {
            double before = MotionBefore(frame), after = MotionBefore(frame + 3.2 * fps, 3.0, 0.2);
            return !double.IsNaN(before) && before > 8 && (double.IsNaN(after) || before > 0.6 * after);
        }

        /// <summary>The motion of a still camera in these frames: a low quantile (20%) of the motion of the frames read.</summary>
        public double StillMotion()
        {
            if (motion.Count < 2)
                return double.NaN;

            List<double> v = motion.Skip(1).OrderBy(x => x).ToList();
            return v[(int)(0.2 * (v.Count - 1))];
        }

        /// <summary>
        /// The light of the start in frames at full size (brightness, rows without padding) when it is too small for the frames of
        /// the reading: the frame (from 'first' to 'last', −1: the end) where the clearest tiny flash comes on: a few pixels (at
        /// most 60 at 1920 wide) 50 brighter than in the two frames before (and their neighbours), at least 170, gone again two
        /// frames later (a digit of a clock on the image stays); a place that comes on again in the six frames before or after
        /// counts less (glints, screens). Frames with many pixels coming on (the camera moving, a change of exposure) are left out.
        /// −1: none (score below 60). weight: of each image (the nearer to the light expected, the better), null: none.
        /// </summary>
        public static int TinyFlash(IList<byte[]> images, int width, int height, int first, out double score, out double[] place,
            Func<int, double> weight = null, int last = -1)
        {
            score = 0;
            place = null;
            int n = width * height, best = -1;
            if (images == null || images.Count < 3 || width < 3 || height < 3)
                return -1;

            byte[] reference = new byte[n], pass = new byte[n];
            int[] labels = new int[n];
            int[] stack = new int[n];
            double maxArea = Math.Max(4, 60.0 * width * width / (1920.0 * 1920.0));
            int end = last >= 0 ? Math.Min(images.Count - 1, last) : images.Count - 1;
            for (int k = Math.Max(2, first); k <= end; k++)
            {
                byte[] v = images[k], v1 = images[k - 1], v2 = images[k - 2];
                if (v == null || v1 == null || v2 == null)
                    continue;

                // The two frames before, 3×3 maximum: a small move of the camera does not make a flash.
                for (int p = 0; p < n; p++)
                    pass[p] = v1[p] > v2[p] ? v1[p] : v2[p];
                Dilate3(pass, reference, width, height);

                List<int> candidates = new List<int>();
                for (int p = 0; p < n; p++)
                {
                    labels[p] = 0;
                    if (v[p] >= 170 && v[p] - reference[p] >= 50)
                    {
                        candidates.Add(p);
                        if (candidates.Count > n / 500)
                            break;
                    }
                }

                if (candidates.Count == 0 || candidates.Count > n / 500)
                    continue;

                foreach (int p in candidates)
                    labels[p] = -1;

                int label = 0;
                foreach (int start in candidates)
                {
                    if (labels[start] != -1)
                        continue;

                    label++;
                    int top = 0, area = 0, minX = width, minY = height, maxX = 0, maxY = 0, peak = 0;
                    double sx = 0, sy = 0, now = 0, before = 0;
                    List<int> pixels = new List<int>();
                    stack[top++] = start;
                    labels[start] = label;
                    while (top > 0)
                    {
                        int p = stack[--top];
                        pixels.Add(p);
                        int x = p % width, y = p / width;
                        area++;
                        sx += x;
                        sy += y;
                        now += v[p];
                        before += reference[p];
                        peak = Math.Max(peak, v[p] - reference[p]);
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= width || yy >= height)
                                    continue;
                                int q = yy * width + xx;
                                if (labels[q] == -1)
                                {
                                    labels[q] = label;
                                    stack[top++] = q;
                                }
                            }
                        }
                    }

                    if (area > maxArea)
                        continue;

                    now /= area;
                    before /= area;

                    // The same place in the six frames before and after (not the next): glints of the water and screens come on
                    // again and again, the light of the start once.
                    int flicker = 0;
                    for (int j = Math.Max(2, k - 6); j <= Math.Min(images.Count - 1, k + 6); j++)
                    {
                        if (j >= k && j <= k + 1)
                            continue;

                        byte[] w = images[j], w1 = images[j - 1], w2 = images[j - 2];
                        if (w == null || w1 == null || w2 == null)
                            continue;
                        bool on = false;
                        for (int y = Math.Max(0, minY - 1); y <= Math.Min(height - 1, maxY + 1) && !on; y++)
                        {
                            for (int x = Math.Max(0, minX - 1); x <= Math.Min(width - 1, maxX + 1) && !on; x++)
                            {
                                int p = y * width + x;
                                on = w[p] - Math.Max(w1[p], w2[p]) >= 30;
                            }
                        }

                        if (on)
                            flicker++;
                    }

                    // Gone two frames later (a flash caught by two frames is still on in the next one).
                    int later = Math.Min(images.Count - 1, k + 2);
                    if (later > k && images[later] != null)
                    {
                        double after = 0;
                        foreach (int p in pixels)
                            after += images[later][p];
                        after /= area;
                        if (after > before + 0.5 * (now - before))
                            continue;
                    }

                    double s = peak * Math.Sqrt(Math.Min(area, 50)) / (1 + flicker) * (weight != null ? weight(k) : 1.0);
                    if (s > score)
                    {
                        score = s;
                        best = k;
                        place = new[] { sx / area, sy / area };
                    }
                }
            }

            if (score < 60)
            {
                place = null;
                return -1;
            }

            return best;
        }

        /// <summary>3×3 maximum, edges replicated.</summary>
        private static void Dilate3(byte[] src, byte[] dst, int width, int height)
        {
            byte[] rows = new byte[src.Length];
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    byte m = src[row + x];
                    if (x > 0 && src[row + x - 1] > m) m = src[row + x - 1];
                    if (x < width - 1 && src[row + x + 1] > m) m = src[row + x + 1];
                    rows[row + x] = m;
                }
            }

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    byte m = rows[row + x];
                    if (y > 0 && rows[row - width + x] > m) m = rows[row - width + x];
                    if (y < height - 1 && rows[row + width + x] > m) m = rows[row + width + x];
                    dst[row + x] = m;
                }
            }
        }
        #endregion

        /// <summary>
        /// True when a start was found and the race after it has been read long enough (6 s): no need to read further. With the dive
        /// check, a flash with the dive after it ends the reading; a start put before the dive, or a flash without the dive, can be
        /// a camera move before the real start: the reading goes on, until 20 s of that race. The seconds are counted from the
        /// camera following the race of the start found (one seen at the end of the frames read is checked again with its race).
        /// </summary>
        public bool Done()
        {
            int n = motion.Count;
            int race = (int)Math.Round(6 * fps);
            UpdateFollows();
            if (follows.Count == 0 || n - follows[0].Item1 < race)
                return false;

            List<SwimStartCandidate> found = Find();
            if (found.Count == 0 || n - found[0].Follow < race)
                return false;

            return !DiveCheck || found[0].Source == "light" || found[0].Source == "weak light" || n - found[0].Follow >= (int)Math.Round(20 * fps);
        }

        /// <summary>Confidence shown with the proposal (0–1): a clear flash of several lights, far ahead of the next candidate.</summary>
        public static double Confidence(List<SwimStartCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return 0;

            SwimStartCandidate best = candidates[0];
            if (best.Source == "dive")
                return 0.35;
            if (best.Source == "line")
                return 0.4;
            if (best.Source == "weak light")
                return 0.55;
            if (best.Source == "light+sound")
                return best.Follow >= 0 || best.Dive >= 0 ? 0.95 : 0.9;
            if (best.Source == "sound")
                return best.Follow >= 0 || best.Dive >= 0 ? 0.8 : 0.7;

            double c = best.Flash >= 200 ? 0.9 : best.Flash >= 80 ? 0.8 : best.Flash >= 30 ? 0.65 : 0.45;
            if (best.Lights >= 2)
                c += 0.05;
            if (candidates.Count > 1 && candidates[1].Source == best.Source && candidates[1].Strength > 0.7 * best.Strength)
                c -= 0.2;
            if (best.Source == "light?")
                c -= 0.2;
            return Math.Max(0.25, Math.Min(0.95, c));
        }
        #endregion
    }
}
