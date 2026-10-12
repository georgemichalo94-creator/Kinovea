// Preview images of the technical view of a race (SwimReelPainter) for the CI, drawn by the program just built,
// with two fictional races on a drawn pool. C# 5: compiled by Windows PowerShell (reel_preview.ps1).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Kinovea.ScreenManager;

public static class ReelPreview
{
    private const double Fps = 50;
    private const long PerFrame = 1800;
    private const long Origin = 90000;
    private const int ReferenceWidth = 1920;
    private const int ReferenceHeight = 1080;
    private static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    private sealed class Frame
    {
        public string Name;
        public SwimRace Race;
        public double Time;
        public int Width;
        public int Height;

        // Drawn as in a video export: on a bigger image, graphics of a screen of this width, then made smaller.
        public int ExportWidth;
    }

    private static long Ts(double seconds)
    {
        return Origin + (long)Math.Round(seconds * Fps) * PerFrame;
    }

    public static SwimTiming Timing()
    {
        SwimTiming timing = new SwimTiming();
        timing.AverageTimestampsPerFrame = PerFrame;
        timing.FrameIntervalMilliseconds = 1000.0 / Fps;
        return timing;
    }

    private static void Add(SwimRace race, SwimMarkType type, double t, int distance)
    {
        SwimMark mark = new SwimMark();
        mark.Type = type;
        mark.Timestamp = Ts(t);
        mark.Distance = distance;
        race.Marks.Add(mark);
    }

    private static void Splits(SwimRace race, int[] distances, double[] times)
    {
        for (int i = 0; i < distances.Length; i++)
            Add(race, SwimMarkType.Split, times[i], distances[i]);
    }

    /// <summary>Place of the swimmer on the drawn pool (reference pixels): the camera follows, not exactly.</summary>
    private static SwimTrack Track(double from, double to, double x0, double x1, double y, double lane)
    {
        SwimTrack track = new SwimTrack();
        track.Start = Ts(from);
        track.Step = PerFrame;
        int n = (int)Math.Round((to - from) * Fps) + 1;
        for (int i = 0; i < n; i++)
        {
            double f = i / (double)Math.Max(1, n - 1);
            double x = x0 + (x1 - x0) * f + 60 * Math.Sin(f * 9);
            double yy = y + 5 * Math.Sin(f * 23);
            track.X.Add((int)Math.Round(x));
            track.Y.Add((int)Math.Round(yy));
            track.Top.Add((int)Math.Round(yy - lane / 2));
            track.Bottom.Add((int)Math.Round(yy + lane / 2));
        }

        return track;
    }

    /// <summary>50 m butterfly, long course, with one double stroke mark.</summary>
    public static SwimRace Butterfly50()
    {
        SwimRace race = new SwimRace();
        race.Athlete = "ΔΟΚΙΜΑΣΤΙΚΗ ΑΘΛΗΤΡΙΑ";
        race.AthleteGender = "Γυναικών";
        race.Lane = 4;
        race.Style = "fly";
        race.Distance = 50;
        race.Pool = 50;
        race.Competition = "Δοκιμή γραφικών";
        race.Checkpoints = SwimPresets.DefaultCheckpoints(50, 50, false);
        race.StartTimestamp = Ts(0);

        Add(race, SwimMarkType.Reaction, 0.58, 0);
        Add(race, SwimMarkType.FeetOff, 0.70, 0);
        Add(race, SwimMarkType.Entry, 1.02, 0);
        foreach (double k in new double[] { 1.46, 1.88, 2.30, 2.72, 3.14, 3.56, 3.98 })
            Add(race, SwimMarkType.Kick, k, 0);
        Add(race, SwimMarkType.Breakout, 4.62, 0);

        List<double> strokes = new List<double>();
        for (double t = 4.86; t < 26.0; t += 0.92 + 0.10 * (t - 4.86) / 21.0)
            strokes.Add(t);
        foreach (double t in strokes)
            Add(race, SwimMarkType.Stroke, t, 0);
        Add(race, SwimMarkType.Stroke, strokes[9] + 0.27, 0);

        Splits(race, new int[] { 15, 25, 35, 45, 50 }, new double[] { 6.21, 11.92, 17.80, 23.81, 26.91 });
        race.Tracks.Add(Track(4.82, 27.5, 560, 1420, 610, 84));
        return race;
    }

    /// <summary>100 m freestyle, short course: three turns, the swimmer followed in the second length.</summary>
    public static SwimRace Freestyle100()
    {
        SwimRace race = new SwimRace();
        race.Athlete = "ΔΟΚΙΜΑΣΤΙΚΟΣ ΑΘΛΗΤΗΣ";
        race.AthleteGender = "Ανδρών";
        race.Lane = 5;
        race.Style = "fs";
        race.Distance = 100;
        race.Pool = 25;
        race.Competition = "Δοκιμή γραφικών";
        race.Checkpoints = SwimPresets.DefaultCheckpoints(100, 25, false);
        race.StartTimestamp = Ts(0);

        Add(race, SwimMarkType.Reaction, 0.55, 0);
        Add(race, SwimMarkType.FeetOff, 0.66, 0);
        Add(race, SwimMarkType.Entry, 0.98, 0);
        foreach (double k in new double[] { 1.40, 1.80, 2.20, 2.60, 3.00 })
            Add(race, SwimMarkType.Kick, k, 0);
        Add(race, SwimMarkType.Breakout, 3.82, 0);

        int[] distances = { 15, 20, 25, 40, 45, 50, 65, 70, 75, 90, 95, 100 };
        double[] times = { 5.95, 8.45, 11.05, 18.10, 20.70, 23.40, 30.65, 33.35, 36.15, 43.55, 46.35, 49.30 };
        Splits(race, distances, times);

        double[] walls = { 11.05, 23.40, 36.15 };
        List<double> breakouts = new List<double> { 3.82 };
        foreach (double w in walls)
        {
            Add(race, SwimMarkType.FeetOff, w + 0.28, 0);
            foreach (double k in new double[] { 0.80, 1.25, 1.70, 2.15, 2.60 })
                Add(race, SwimMarkType.Kick, w + k, 0);
            Add(race, SwimMarkType.Breakout, w + 3.15, 0);
            breakouts.Add(w + 3.15);
        }

        double[] ends = { 11.05, 23.40, 36.15, 49.30 };
        for (int lap = 0; lap < 4; lap++)
        {
            double step = 0.60 + 0.012 * lap;
            for (double t = breakouts[lap] + 0.18; t < ends[lap] - 0.75; t += step)
                Add(race, SwimMarkType.Stroke, t, 0);
        }

        race.Tracks.Add(Track(14.30, 23.80, 520, 1460, 610, 84));
        return race;
    }

    private static List<Frame> Frames(SwimRace fly, SwimRace free)
    {
        List<Frame> frames = new List<Frame>();
        Action<string, SwimRace, double, int, int, int> add = (name, race, t, w, h, export) =>
            frames.Add(new Frame { Name = name, Race = race, Time = t, Width = w, Height = h, ExportWidth = export });
        add("fly-06.4", fly, 6.4, 960, 540, 0);
        add("fly-14.2", fly, 14.2, 960, 540, 0);
        add("fly-summary", fly, 28.6, 960, 540, 0);
        add("free-15.0", free, 15.0, 960, 540, 0);
        add("free-summary", free, 51.0, 960, 540, 0);
        add("fly-14.2-export", fly, 14.2, 960, 540, 1280);
        add("fly-14.2-small", fly, 14.2, 640, 360, 0);
        add("fly-14.2-portrait", fly, 14.2, 405, 720, 0);
        return frames;
    }

    /// <summary>The values of the technical view of both races, as text.</summary>
    public static string Describe()
    {
        StringBuilder b = new StringBuilder();
        foreach (SwimRace race in new SwimRace[] { Butterfly50(), Freestyle100() })
        {
            SwimTiming timing = Timing();
            SwimComputed computed = SwimCalculator.Compute(race, timing, 0.5);
            SwimReel reel = SwimReel.Build(race, computed);
            b.AppendLine(race.Distance + " " + race.Style + " pool " + race.Pool + ": finish " + F(reel.Finish) + ", walls " +
                string.Join(" ", reel.Walls.Select(w => w.ToString("0.00", ci))) + ", breakout " + F(reel.FirstBreakout) +
                ", double marks " + string.Join(" ", reel.DoubleMarks.Select(d => d.ToString("0.00", ci))));
            foreach (KeyValuePair<int, double> kv in reel.BreakoutDistances)
                b.AppendLine(string.Format(ci, "  breakout lap {0}: {1:0.0} m {2}", kv.Key + 1, kv.Value, reel.BreakoutMeasured.Contains(kv.Key) ? "measured" : "estimated"));
            b.AppendLine("  cycles " + reel.Cycles.Count + ": " + string.Join(" ", reel.Cycles.Take(6).Select(c =>
                string.Format(ci, "{0:0.00}/{1:0.00}/{2:0.00}/{3:0.00}", c.Time, c.Tempo, c.Dps, c.Speed))) + " …");
            foreach (SwimReelPart p in reel.Parts)
                b.AppendLine(string.Format(ci, "  part {0}-{1}: {2} s, {3} strokes, tempo {4}, dps {5}, v {6}", p.From, p.To, F(p.Time), p.Strokes, F(p.Tempo), F(p.Dps), F(p.Speed)));
            foreach (SwimReelBadge badge in reel.Badges)
                b.AppendLine(string.Format(ci, "  badge {0:0.00} {1} | {2} | {3}", badge.Time, badge.Title, badge.Value, badge.Detail));
        }

        return b.ToString();
    }

    private static string F(double? v)
    {
        return v.HasValue ? v.Value.ToString("0.00", ci) : "-";
    }

    /// <summary>Draws the images in the folder (JPEG), with frames.txt (their names), model.txt and errors.txt.</summary>
    public static string Render(string folder)
    {
        Directory.CreateDirectory(folder);
        StringBuilder errors = new StringBuilder();
        List<string> names = new List<string>();
        try
        {
            File.WriteAllText(Path.Combine(folder, "model.txt"), Describe(), Encoding.UTF8);
        }
        catch (Exception e)
        {
            errors.AppendLine("model: " + e);
        }

        SwimRace fly = Butterfly50();
        SwimRace free = Freestyle100();
        Dictionary<SwimRace, SwimComputed> computed = new Dictionary<SwimRace, SwimComputed>();
        Dictionary<SwimRace, SwimReel> reels = new Dictionary<SwimRace, SwimReel>();
        SwimTiming timing = Timing();
        foreach (SwimRace race in new SwimRace[] { fly, free })
        {
            computed[race] = SwimCalculator.Compute(race, timing, 0.5);
            reels[race] = SwimReel.Build(race, computed[race]);
        }

        foreach (Frame frame in Frames(fly, free))
        {
            try
            {
                using (Bitmap image = Draw(frame, computed[frame.Race], reels[frame.Race], timing))
                    SaveJpeg(image, Path.Combine(folder, frame.Name + ".jpg"), 65);
                names.Add(frame.Name);
            }
            catch (Exception e)
            {
                errors.AppendLine(frame.Name + ": " + e);
            }
        }

        File.WriteAllLines(Path.Combine(folder, "frames.txt"), names.ToArray());
        if (errors.Length > 0)
            File.WriteAllText(Path.Combine(folder, "errors.txt"), errors.ToString(), Encoding.UTF8);
        return names.Count + " images, " + (errors.Length > 0 ? "errors:\n" + errors : "no errors");
    }

    private static Bitmap Draw(Frame frame, SwimComputed computed, SwimReel reel, SwimTiming timing)
    {
        // As a video export: the image of the video size, the graphics of a screen of ExportWidth.
        int w = frame.ExportWidth > 0 ? ReferenceWidth : frame.Width;
        int h = frame.ExportWidth > 0 ? ReferenceHeight : frame.Height;
        Bitmap image = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(image))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            long timestamp = Ts(frame.Time);
            float sx = w / (float)ReferenceWidth, sy = h / (float)ReferenceHeight;
            DrawPool(g, w, h, frame.Race, timestamp, sx, sy);

            SwimReelScene scene = new SwimReelScene();
            scene.Race = frame.Race;
            scene.Computed = computed;
            scene.Reel = reel;
            scene.Timing = timing;
            scene.Timestamp = timestamp;
            scene.Time = timing.Seconds(timestamp, frame.Race.StartTimestamp.Value);
            scene.Summary = true;
            if (frame.ExportWidth > 0)
            {
                float scale = Math.Max(0.25f, w / (float)frame.ExportWidth);
                Size logical = new Size((int)Math.Round(w / scale), (int)Math.Round(h / scale));
                float lx = sx / scale, ly = sy / scale;
                scene.FromImage = p => new PointF(p.X * lx, p.Y * ly);
                GraphicsState state = g.Save();
                g.ScaleTransform(scale, scale);
                SwimReelPainter.Paint(g, logical, scene);
                g.Restore(state);
            }
            else
            {
                scene.FromImage = p => new PointF(p.X * sx, p.Y * sy);
                SwimReelPainter.Paint(g, new Size(w, h), scene);
            }
        }

        if (frame.ExportWidth <= 0)
            return image;

        Bitmap small = new Bitmap(frame.Width, frame.Height, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(image, new Rectangle(0, 0, small.Width, small.Height));
        }

        image.Dispose();
        return small;
    }

    /// <summary>Water, lane ropes and the swimmer (a dark shape with white water) where the track says.</summary>
    private static void DrawPool(Graphics g, int w, int h, SwimRace race, long timestamp, float sx, float sy)
    {
        using (LinearGradientBrush water = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(28, 112, 158), Color.FromArgb(8, 52, 88), LinearGradientMode.Vertical))
            g.FillRectangle(water, 0, 0, w, h);

        for (int k = -7; k <= 7; k++)
        {
            float y = (568 + 84 * k) * sy;
            if (y < -10 || y > h + 10)
                continue;

            float dash = 26 * sx;
            int i = 0;
            for (float x = -dash; x < w; x += dash, i++)
            {
                using (SolidBrush b = new SolidBrush(i % 2 == 0 ? Color.FromArgb(220, 205, 40, 40) : Color.FromArgb(220, 235, 235, 235)))
                    g.FillEllipse(b, x, y - 4 * sy, dash - 2 * sx, 8 * sy);
            }
        }

        SwimTrack track = race.TrackAt(timestamp);
        float px, py, top, bottom;
        if (track == null || !track.TryGet(timestamp, 0, out px, out py, out top, out bottom))
            return;

        float cx = px * sx, cy = py * sy, len = 150 * sx, thick = 26 * sy;
        using (SolidBrush splash = new SolidBrush(Color.FromArgb(150, 240, 248, 255)))
            g.FillEllipse(splash, cx - len * 0.75f, cy - thick, len * 1.2f, thick * 2);
        using (SolidBrush body = new SolidBrush(Color.FromArgb(230, 20, 26, 34)))
            g.FillEllipse(body, cx - len / 2, cy - thick / 3, len, thick * 0.66f);
        using (SolidBrush cap = new SolidBrush(Color.FromArgb(255, 240, 200, 20)))
            g.FillEllipse(cap, cx + len / 2 - 22 * sx, cy - 10 * sy, 22 * sx, 20 * sy);
    }

    private static void SaveJpeg(Bitmap image, string path, long quality)
    {
        ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using (EncoderParameters parameters = new EncoderParameters(1))
        {
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            image.Save(path, codec, parameters);
        }
    }
}
