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

        // The final of the event: its board at the checkpoints and the finish, the result in the summary.
        public SwimFinal Final;
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

        List<Frame> frames = Frames(fly, free);
        try
        {
            // The 100 m backstroke against the fictional final: the board and the Δ of the technical view.
            string error;
            SwimFinals finals = SwimFinals.Parse(FinalsJson(), out error);
            SwimRace back = Backstroke100();
            SwimFinal final = finals != null ? finals.For(back) : null;
            if (final == null)
                throw new InvalidOperationException("no final for the backstroke: " + error);
            back.Reference = SwimFinals.Reference(final, final.Swimmers[0]);
            computed[back] = SwimCalculator.Compute(back, timing, 0.5);
            reels[back] = SwimReel.Build(back, computed[back]);
            frames.Add(new Frame { Name = "bk-26.5-final", Race = back, Time = 26.5, Width = 960, Height = 540, Final = final });
            frames.Add(new Frame { Name = "bk-26.5-final-export", Race = back, Time = 26.5, Width = 960, Height = 540, ExportWidth = 1280, Final = final });
            frames.Add(new Frame { Name = "bk-54.0-finish", Race = back, Time = 54.0, Width = 960, Height = 540, Final = final });
            frames.Add(new Frame { Name = "bk-58.6-summary", Race = back, Time = 58.6, Width = 960, Height = 540, Final = final });
            frames.Add(new Frame { Name = "bk-26.5-portrait", Race = back, Time = 26.5, Width = 405, Height = 720, Final = final });
            frames.Add(new Frame { Name = "bk-26.5-small", Race = back, Time = 26.5, Width = 640, Height = 360, Final = final });
            RenderReport(folder, names, back, computed[back], final, timing);
        }
        catch (Exception e)
        {
            errors.AppendLine("backstroke against the final: " + e);
        }

        foreach (Frame frame in frames)
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

        // The skeleton (fictional, drawn) and a check of the pose models of the program.
        try
        {
            RenderSkeleton(folder, names);
        }
        catch (Exception e)
        {
            errors.AppendLine("skeleton: " + e);
        }

        try
        {
            RenderProposal(folder, names);
        }
        catch (Exception e)
        {
            errors.AppendLine("proposal: " + e);
        }

        try
        {
            RenderFinals(folder, names);
        }
        catch (Exception e)
        {
            errors.AppendLine("finals: " + e);
        }

        try
        {
            File.WriteAllText(Path.Combine(folder, "pose.txt"), PoseCheck() + ProposalsCheck() + StartLightCheck() + FinalsCheck(), Encoding.UTF8);
        }
        catch (Exception e)
        {
            errors.AppendLine("pose: " + e);
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
            scene.Final = frame.Final;
            scene.BoardName = "ΔΟΚΙΜΑΣΤΙΚΟΣ";
            scene.Delta = true;
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

    /// <summary>A fictional skeleton of a track start seen from the side: set position, drive, flight, entry (reference pixels).</summary>
    private static SwimSkeleton StartSkeleton()
    {
        // Joints (SwimJoints order) at four moments; the left side is the near side.
        float[][] keys =
        {
            new float[] { 885,560, 935,515, 945,522, 915,565, 925,568, 905,605, 912,606, 1000,470, 1008,474, 960,520, 1045,512, 952,598, 1085,592, 975,600, 1105,595, 935,602, 1070,600 },
            new float[] { 820,545, 880,500, 888,505, 830,520, 838,525, 780,515, 788,518, 960,470, 968,474, 920,525, 1010,505, 948,598, 1060,560, 970,600, 1080,560, 930,604, 1046,566 },
            new float[] { 640,500, 700,480, 706,484, 640,470, 646,474, 580,462, 586,466, 820,470, 826,474, 900,470, 906,476, 980,462, 986,468, 1000,470, 1006,476, 990,450, 996,456 },
            new float[] { 520,600, 570,565, 576,568, 520,590, 526,594, 470,625, 476,628, 680,520, 686,524, 760,490, 766,494, 840,465, 846,470, 860,470, 866,476, 850,450, 856,455 }
        };
        double[] times = { 0, 0.45, 0.95, 1.35 };
        SwimSkeleton s = new SwimSkeleton();
        s.Start = Ts(0);
        s.Step = PerFrame;
        int n = (int)Math.Round(times[times.Length - 1] * Fps) + 1;
        for (int i = 0; i < n; i++)
        {
            double t = i / Fps;
            int k = 0;
            while (k < times.Length - 2 && t > times[k + 1])
                k++;
            double f = Math.Max(0, Math.Min(1, (t - times[k]) / (times[k + 1] - times[k])));
            SwimPose pose = new SwimPose();
            for (int j = 0; j < SwimJoints.Count; j++)
            {
                pose.X[j] = (float)(keys[k][2 * j] * (1 - f) + keys[k + 1][2 * j] * f + 1.5 * Math.Sin(i * 0.7 + j));
                pose.Y[j] = (float)(keys[k][2 * j + 1] * (1 - f) + keys[k + 1][2 * j + 1] * f + 1.5 * Math.Cos(i * 0.9 + j));
                pose.Visibility[j] = SwimJoints.IsLeft(j) || j == SwimJoints.Nose ? 0.95f : 0.7f;
            }

            s.Add(pose);
        }

        return s;
    }

    private static void RenderSkeleton(string folder, List<string> names)
    {
        SwimSkeleton skeleton = StartSkeleton();
        foreach (double t in new double[] { 0.1, 0.9 })
        {
            string name = "skeleton-" + t.ToString("0.0", ci);
            using (Bitmap image = new Bitmap(960, 540, PixelFormat.Format24bppRgb))
            using (Graphics g = Graphics.FromImage(image))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                float sx = 960f / ReferenceWidth, sy = 540f / ReferenceHeight;
                using (LinearGradientBrush water = new LinearGradientBrush(new Rectangle(0, 0, 960, 540), Color.FromArgb(150, 190, 215), Color.FromArgb(18, 92, 140), LinearGradientMode.Vertical))
                    g.FillRectangle(water, 0, 0, 960, 540);
                using (SolidBrush block = new SolidBrush(Color.FromArgb(235, 235, 240)))
                    g.FillRectangle(block, 900 * sx, 600 * sy, 260 * sx, 40 * sy);
                SwimSkeletonPainter.Paint(g, skeleton, Ts(t), p => new PointF(p.X * sx, p.Y * sy), 1f, true);
                SaveJpeg(image, Path.Combine(folder, name + ".jpg"), 65);
            }

            names.Add(name);
        }
    }

    /// <summary>The box of a proposal being checked, on the fictional start.</summary>
    /// <summary>100 m backstroke, long course: the race compared with a fictional final.</summary>
    public static SwimRace Backstroke100()
    {
        SwimRace race = new SwimRace();
        race.Athlete = "ΔΟΚΙΜΑΣΤΙΚΟΣ ΑΘΛΗΤΗΣ";
        race.AthleteGender = "Ανδρών";
        race.Lane = 3;
        race.Style = "bk";
        race.Distance = 100;
        race.Pool = 50;
        race.Competition = "Δοκιμή γραφικών";
        race.Checkpoints = SwimPresets.DefaultCheckpoints(100, 50, false);
        race.StartTimestamp = Ts(0);
        Add(race, SwimMarkType.Reaction, 0.20, 0);
        Add(race, SwimMarkType.FeetOff, 0.58, 0);
        Add(race, SwimMarkType.Entry, 0.67, 0);
        Add(race, SwimMarkType.Breakout, 5.40, 0);
        int[] distances = { 15, 25, 35, 45, 50, 65, 75, 85, 95, 100 };
        double[] times = { 6.20, 11.62, 17.10, 22.66, 25.62, 32.80, 38.25, 43.90, 49.62, 52.95 };
        Splits(race, distances, times);
        Add(race, SwimMarkType.FeetOff, 25.92, 0);
        Add(race, SwimMarkType.Breakout, 31.12, 0);
        for (double t = 5.62; t < 25.0; t += 0.64)
            Add(race, SwimMarkType.Stroke, t, 0);
        for (double t = 31.30; t < 52.4; t += 0.66)
            Add(race, SwimMarkType.Stroke, t, 0);
        race.UnderwaterDist = 14.1;
        race.GetTurn(50, true).UnderwaterDist = 12.4;
        return race;
    }

    /// <summary>A fictional final of the 100 m backstroke (eight swimmers, the format of the files of the finals).</summary>
    public static string FinalsJson()
    {
        StringBuilder b = new StringBuilder();
        b.Append("{\"format\":\"kinovea-swim-finals\",\"version\":1,\"source\":\"Δοκιμαστικός τελικός\",\"short\":\"ΔΟΚΙΜΗ\",\"pool\":50,\"finals\":[");
        b.Append("{\"gender\":\"M\",\"style\":\"bk\",\"distance\":100,\"swimmers\":[");
        string[] names = { "Alpha ONE", "Beta TWO", "Gamma THREE", "Delta FOUR", "Epsilon FIVE", "Zeta SIX", "Eta SEVEN", "Theta EIGHT" };
        for (int i = 0; i < names.Length; i++)
        {
            double k = 1 + 0.004 * i;
            double t15 = 5.9 + 0.04 * i, t25 = 11.2 * k, t45 = 22.0 * k, t50 = 24.9 * k, t65 = 32.0 * k, t75 = 37.4 * k, t95 = 49.1 * k, t100 = 52.0 * k;
            if (i > 0)
                b.Append(",");
            b.Append(string.Format(ci,
                "{{\"rank\":{0},\"name\":\"{1}\",\"final\":{2:0.00},\"checkpoints\":{{\"15\":{3:0.00},\"25\":{4:0.00},\"45\":{5:0.00},\"50\":{6:0.00},\"65\":{7:0.00},\"75\":{8:0.00},\"95\":{9:0.00},\"100\":{2:0.00}}}," +
                "\"blockTime\":{10:0.00},\"start15\":{3:0.00},\"startBreakoutTime\":{11:0.00},\"startBreakoutDist\":{12:0.00},\"finishIn5\":{13:0.00}," +
                "\"turns\":[{{\"wall\":50,\"in5\":{14:0.00},\"out15\":{15:0.00},\"breakoutTime\":{16:0.00},\"breakoutDist\":{17:0.00}}}]," +
                "\"strokes\":{{\"50\":{18},\"100\":{19}}},\"rate\":{{\"25\":{20:0.0},\"50\":{20:0.0},\"75\":{21:0.0},\"100\":{21:0.0}}},\"cycle\":{{\"25\":{22:0.00},\"50\":{22:0.00},\"75\":{23:0.00},\"100\":{23:0.00}}}}}",
                i + 1, names[i], t100, t15, t25, t45, t50, t65, t75, t95,
                0.52 + 0.02 * (i % 3), 5.0 + 0.05 * i, 14.5 - 0.1 * i, t100 - t95, t50 - t45, t65 - t50, 5.6 + 0.1 * (i % 4), 12.8 - 0.15 * i,
                28 + i % 3, 33 + i % 3, 46.0 + 0.5 * i, 47.0 + 0.4 * i, 2.40 - 0.02 * i, 2.30 - 0.02 * i));
        }

        b.Append("]}]}");
        return b.ToString();
    }

    /// <summary>The finals: the fictional final read, the comparison of the fictional race with it, the board of a checkpoint.</summary>
    public static string FinalsCheck()
    {
        StringBuilder b = new StringBuilder();
        string error;
        SwimFinals finals = SwimFinals.Parse(FinalsJson(), out error);
        if (finals == null)
            return "finals: not read: " + error + "\n";

        SwimRace race = Backstroke100();
        SwimFinal final = finals.For(race);
        b.AppendLine(string.Format(ci, "finals: {0} finals, {1} swimmers; for the race: {2}", finals.Finals.Count, finals.SwimmerCount, final != null ? final.Label : "none"));
        if (final == null)
            return b.ToString();

        race.Reference = SwimFinals.Reference(final, final.Swimmers[0]);
        SwimComputed computed = SwimCalculator.Compute(race, Timing(), 0.5);
        foreach (SwimFinalsMetric m in SwimFinalsCompare.Metrics(race, computed, final))
            b.AppendLine(string.Format(ci, "  {0} · {1}: athlete {2} rank {3}/{4} best {5} mean {6}", m.Group, m.Label, m.Format(m.Athlete), m.Rank, m.Count + 1, m.Format(m.Best), m.Format(m.Mean)));
        foreach (SwimFinalsBoardRow row in SwimFinalsCompare.Board(final, 50, 25.62, "ΔΟΚΙΜΑΣΤΙΚΟΣ"))
            b.AppendLine(string.Format(ci, "  board 50: {0}. {1} {2:0.00} +{3:0.00}{4}", row.Place, row.Name, row.Time, row.Behind, row.IsAthlete ? " (athlete)" : ""));
        b.AppendLine("  deltas: " + string.Join(" ", computed.Rows.Where(r => r.Delta.HasValue).Select(r => r.D + ":" + r.Delta.Value.ToString("+0.00;-0.00", ci)).ToArray()));
        return b.ToString();
    }

    /// <summary>The graphics of the comparisons with the fictional final: board, Δ on the video and on the panel, the page of the final.</summary>
    private static void RenderFinals(string folder, List<string> names)
    {
        string error;
        SwimFinals finals = SwimFinals.Parse(FinalsJson(), out error);
        if (finals == null)
            throw new InvalidOperationException(error);
        SwimRace race = Backstroke100();
        SwimFinal final = finals.For(race);
        if (final == null)
            throw new InvalidOperationException("no final for the race");
        race.Reference = SwimFinals.Reference(final, final.Swimmers[0]);
        SwimComputed computed = SwimCalculator.Compute(race, Timing(), 0.5);
        List<SwimDeltaPoint> deltas = computed.Rows.Where(r => r.Delta.HasValue).Select(r => new SwimDeltaPoint { D = r.D, Delta = r.Delta.Value }).ToList();

        // The board of the 50 m and the Δ on the video.
        using (Bitmap image = new Bitmap(960, 540, PixelFormat.Format24bppRgb))
        using (Graphics g = Graphics.FromImage(image))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (LinearGradientBrush water = new LinearGradientBrush(new Rectangle(0, 0, 960, 540), Color.FromArgb(60, 140, 180), Color.FromArgb(18, 70, 110), LinearGradientMode.Vertical))
                g.FillRectangle(water, 0, 0, 960, 540);
            List<SwimFinalsBoardRow> rows = SwimFinalsCompare.Board(final, 50, 25.62, "ΔΟΚΙΜΑΣΤΙΚΟΣ");
            int width = 260;
            float scale = width / 260f;
            SwimFinalsPainter.PaintBoard(g, new Rectangle(10, 10, width, SwimFinalsPainter.BoardHeight(rows.Count, scale)), "50 M · ΔΟΚΙΜΗ", "Τελικός 100μ. ύπτιο Ανδρών", rows, scale);
            SwimFinalsPainter.PaintDelta(g, new Rectangle(600, 10, 350, 105), deltas, 100, 50, race.Reference.Name, true, 1f);
            SwimFinalsPainter.PaintDelta(g, new Rectangle(600, 130, 350, 105), deltas.Where(d => d.D <= 50).ToList(), 100, 50, race.Reference.Name, true, 1f);
            SaveJpeg(image, Path.Combine(folder, "finals-board.jpg"), 70);
        }

        names.Add("finals-board");

        // The Δ of the panel.
        using (Bitmap image = new Bitmap(640, 118, PixelFormat.Format24bppRgb))
        using (Graphics g = Graphics.FromImage(image))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            SwimFinalsPainter.PaintDelta(g, new Rectangle(0, 0, 640, 118), deltas, 100, 50, race.Reference.Name, false, 1.25f);
            SaveJpeg(image, Path.Combine(folder, "finals-delta-panel.jpg"), 75);
        }

        names.Add("finals-delta-panel");

        // The page of the final.
        List<SwimFinalsMetric> metrics = SwimFinalsCompare.Metrics(race, computed, final);
        int height = Math.Min(1400, SwimFinalsPainter.MetricsHeight(metrics));
        using (Bitmap image = new Bitmap(720, height, PixelFormat.Format24bppRgb))
        using (Graphics g = Graphics.FromImage(image))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            SwimFinalsPainter.PaintMetrics(g, new Rectangle(0, 0, 720, height), metrics, final.Label, race.Athlete);
            SaveJpeg(image, Path.Combine(folder, "finals-metrics.jpg"), 70);
        }

        names.Add("finals-metrics");
    }

    private static void RenderProposal(string folder, List<string> names)
    {
        SwimSkeleton skeleton = StartSkeleton();
        string name = "proposal-0.9";
        using (Bitmap image = new Bitmap(960, 540, PixelFormat.Format24bppRgb))
        using (Graphics g = Graphics.FromImage(image))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float sx = 960f / ReferenceWidth, sy = 540f / ReferenceHeight;
            using (LinearGradientBrush water = new LinearGradientBrush(new Rectangle(0, 0, 960, 540), Color.FromArgb(150, 190, 215), Color.FromArgb(18, 92, 140), LinearGradientMode.Vertical))
                g.FillRectangle(water, 0, 0, 960, 540);
            using (SolidBrush block = new SolidBrush(Color.FromArgb(235, 235, 240)))
                g.FillRectangle(block, 900 * sx, 600 * sy, 260 * sx, 40 * sy);
            SwimSkeletonPainter.Paint(g, skeleton, Ts(0.9), p => new PointF(p.X * sx, p.Y * sy), 1f, true);
            SwimProposal proposal = new SwimProposal { Type = SwimMarkType.FeetOff, Kind = SwimEventDetector.StartFeet, Confidence = 0.82 };
            SwimAnalysisManager.PaintProposalPrompt(g, "✨ 4/5  " + SwimAnalysisManager.ProposalLabel(proposal), "+2 καρέ από την πρόταση",
                "Enter σωστό · ← → καρέ · Delete λάθος · Esc τέλος", false, new Size(960, 540));
            SaveJpeg(image, Path.Combine(folder, name + ".jpg"), 65);
        }

        names.Add(name);
    }

    /// <summary>Fictional skeleton under the water to the right: dolphin kicks at 2.2 Hz from 0.2 s, first stroke and up to the surface at 2.2 s.</summary>
    private static List<int> Glide(double fps, double seconds, double strokeAt)
    {
        double L = 200, speed = 260, f = 2.2;
        List<int> d = new List<int>();
        for (int i = 0; i < (int)(seconds * fps); i++)
        {
            double t = i / fps;
            double x = 300 + speed * t;
            double depth = t < strokeAt ? 420 : Math.Max(300, 420 - (t - strokeAt) * 450);
            double kick = t < strokeAt + 0.1 ? 1 : 0.2;
            double pull = t < strokeAt ? 0 : Math.Min(1, (t - strokeAt) / 0.35);
            double ankle = depth + 6 + 0.2 * L * Math.Sin(2 * Math.PI * f * (t - 0.2) - 0.6) * kick;
            double[] p =
            {
                x + 0.15 * L, depth - 5, x, depth, x - 3, depth + 2,
                x + 0.3 * L - pull * 0.35 * L, depth - 4 + pull * 0.2 * L, x + 0.3 * L, depth - 3,
                x + 0.55 * L - pull * 0.75 * L, depth - 6 + pull * 0.3 * L, x + 0.55 * L, depth - 5,
                x - 0.45 * L, depth + 6, x - 0.45 * L - 3, depth + 8,
                x - 0.75 * L, depth + 6, x - 0.75 * L - 2, depth + 8,
                x - L, ankle, x - L - 2, ankle + 2,
                x - L - 8, ankle - 6, x - L - 10, ankle - 4,
                x - L - 25, ankle + 10, x - L - 27, ankle + 12
            };
            for (int j = 0; j < SwimJoints.Count; j++)
            {
                d.Add((int)Math.Round(p[2 * j] + 1.5 * Math.Sin(i * 1.3 + j)));
                d.Add((int)Math.Round(p[2 * j + 1] + 1.5 * Math.Cos(i * 1.7 + j)));
                d.Add(90);
            }
        }

        return d;
    }

    /// <summary>
    /// The proposals of fictional readings (dolphin kicks and breakout under the water) and the learning from fictional answers
    /// (in a temporary file, not the settings of the program).
    /// </summary>
    public static string ProposalsCheck()
    {
        StringBuilder b = new StringBuilder();
        double fps = 60;
        List<int> glide = Glide(fps, 3.0, 2.2);
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        List<SwimDetection> found = SwimEventDetector.Detect(SwimPoseSeries.FromData(glide, fps), null, new SwimDetectorContext { Style = "fly" });
        b.AppendLine("proposals of a fictional glide (kicks expected near 0.36 0.81 1.27 1.72 2.18 s, breakout near 2.45 s), " + watch.ElapsedMilliseconds + " ms:");
        string path = Path.Combine(Path.GetTempPath(), "swim-learning-preview.json");
        if (File.Exists(path))
            File.Delete(path);
        SwimLearning learning = SwimLearning.Load(path);
        foreach (SwimDetection d in found)
        {
            double confidence;
            double sample = SwimProposalMath.Combine(d, learning, "preview", fps, out confidence);
            b.AppendLine(string.Format(ci, "  {0}{1} {2:0.000} s ({3:0}%)", d.Kind, d.Index > 0 ? " " + d.Index : "", sample / fps, 100 * confidence));
        }

        // Three answers 2 frames after the kicks: the next proposals move.
        for (int i = 0; i < 3; i++)
        {
            SwimLearningRecord r = new SwimLearningRecord { Date = DateTime.Now, Kind = SwimEventDetector.UnderKick, Video = "preview", Right = true, Error = 2 / fps };
            r.Cues["feet"] = 2 / fps;
            learning.Add(r);
        }

        learning.Save();
        SwimLearning back = SwimLearning.Load(path);
        SwimDetection first = found.FirstOrDefault(d => d.Kind == SwimEventDetector.UnderKick);
        if (first != null)
        {
            double c0, c1;
            double before = SwimProposalMath.Combine(first, null, "preview", fps, out c0);
            double after = SwimProposalMath.Combine(first, back, "preview", fps, out c1);
            b.AppendLine(string.Format(ci, "learning: {0} answers saved and read back, first kick {1:0.000} s → {2:0.000} s after 3 answers of +2 frames",
                back.Records.Count, before / fps, after / fps));
        }

        File.Delete(path);
        return b.ToString();
    }

    /// <summary>
    /// The start light detector on a fictional video (480×270, 50 frames/s): a still image with glints on the water, three lights
    /// at the blocks that flash at 6.00 s (frame 300), then the camera follows the race (pan); and the same video without the lights.
    /// </summary>
    public static string StartLightCheck()
    {
        StringBuilder b = new StringBuilder();
        int w = 480, h = 270, n = 640, wide = w + 2400;
        double fps = 50;
        byte[] scene = new byte[wide * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < wide; x++)
            {
                double value = y < h / 2 ? 90 + 35 * Math.Sin(x / 11.0) + 25 * Math.Cos(y / 7.0) : 120 + 20 * Math.Sin(x / 5.0 + y / 9.0);
                scene[y * wide + x] = (byte)Math.Max(0, Math.Min(255, value));
            }
        }

        int[] lightX = { 100, 240, 380 };
        double[] pulse = { 230, 215, 190, 150, 110, 80 };
        for (int withLights = 1; withLights >= 0; withLights--)
        {
            Random random = new Random(7);
            SwimStartLight detector = new SwimStartLight(w, h, fps);
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            for (int f = 0; f < n; f++)
            {
                int shift = f < 330 ? 0 : (f - 330) * 6;
                byte[] v = new byte[w * h];
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(scene, y * wide + shift, v, y * w, w);

                // Glints on the water (lower half): small bright dots at random places in every frame.
                for (int k = 0; k < 120; k++)
                {
                    int gx = random.Next(w - 2), gy = h / 2 + random.Next(h / 2 - 2);
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                            v[(gy + dy) * w + gx + dx] = 225;
                }

                // The start lights at the blocks: off (dark), a flash at frame 300 that fades.
                if (withLights == 1)
                {
                    foreach (int lx in lightX)
                    {
                        int x0 = lx - shift;
                        int k = f - 300;
                        byte level = (byte)(k >= 0 && k < pulse.Length ? pulse[k] : 55);
                        for (int dy = 0; dy < 4; dy++)
                        {
                            for (int dx = 0; dx < 4; dx++)
                            {
                                int x = x0 + dx;
                                if (x >= 0 && x < w)
                                    v[(60 + dy) * w + x] = level;
                            }
                        }
                    }
                }

                detector.Add(v);
            }

            List<SwimStartCandidate> found = detector.Find();
            string what = withLights == 1 ? "with three start lights flashing at 6.00 s (frame 300)" : "without lights";
            if (found.Count == 0)
            {
                b.AppendLine(string.Format(ci, "start light, fictional video {0}: no start found, {1} frames in {2} ms", what, n, watch.ElapsedMilliseconds));
                continue;
            }

            SwimStartCandidate c = found[0];
            b.AppendLine(string.Format(ci, "start light, fictional video {0}: start at frame {1} ({2:0.00} s), {3} lights, flash {4:0}, follow {5:0.00} s, confidence {6:0.00}; {7} frames in {8} ms",
                what, c.Frame, c.Frame / fps, c.Lights, c.Flash, c.Follow / fps, SwimStartLight.Confidence(found), n, watch.ElapsedMilliseconds));
        }

        return b.ToString();
    }

    /// <summary>The pose models of the program: files, reading, one detection and one skeleton on a drawn image, times.</summary>
    public static string PoseCheck()
    {
        StringBuilder b = new StringBuilder();
        b.AppendLine("models folder " + SwimPoseNet.ModelFolder + " available " + SwimPoseNet.Available);
        if (!SwimPoseNet.Available)
            return b.ToString();

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        using (SwimPoseNet net = new SwimPoseNet())
        {
            b.AppendLine("read in " + watch.ElapsedMilliseconds + " ms");
            using (OpenCvSharp.Mat image = new OpenCvSharp.Mat(1080, 1920, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(140, 110, 40)))
            {
                // A rough figure: head, body, arms and legs.
                OpenCvSharp.Scalar skin = new OpenCvSharp.Scalar(150, 180, 225);
                OpenCvSharp.Cv2.Circle(image, new OpenCvSharp.Point(960, 300), 45, skin, -1);
                OpenCvSharp.Cv2.Line(image, new OpenCvSharp.Point(960, 350), new OpenCvSharp.Point(960, 620), skin, 70);
                OpenCvSharp.Cv2.Line(image, new OpenCvSharp.Point(960, 400), new OpenCvSharp.Point(820, 560), skin, 28);
                OpenCvSharp.Cv2.Line(image, new OpenCvSharp.Point(960, 400), new OpenCvSharp.Point(1100, 560), skin, 28);
                OpenCvSharp.Cv2.Line(image, new OpenCvSharp.Point(940, 620), new OpenCvSharp.Point(900, 880), skin, 34);
                OpenCvSharp.Cv2.Line(image, new OpenCvSharp.Point(980, 620), new OpenCvSharp.Point(1020, 880), skin, 34);

                watch.Restart();
                List<SwimPersonCandidate> found = net.Detect(image, new Rectangle(560, 100, 800, 900), 0.1f);
                b.AppendLine("detector: " + found.Count + " candidates in " + watch.ElapsedMilliseconds + " ms" +
                    (found.Count > 0 ? string.Format(ci, ", best {0:0.00} at {1:0},{2:0}", found[0].Score, found[0].Hip.X, found[0].Hip.Y) : ""));

                watch.Restart();
                SwimPose pose = null;
                for (int i = 0; i < 5; i++)
                    pose = net.Landmarks(image, new PointF(960, 600), new PointF(960, 250), 0f);
                b.AppendLine("landmarks: 5 runs in " + watch.ElapsedMilliseconds + " ms" +
                    (pose != null ? string.Format(ci, ", confidence {0:0.000}, hip {1:0},{2:0}, nose {3:0},{4:0}", pose.Confidence, pose.Hip.X, pose.Hip.Y, pose.X[0], pose.Y[0]) : ", none"));

                // The person mask, kept on request and placed in a crop of the image (as for the image of a start).
                net.KeepMask = true;
                watch.Restart();
                pose = net.Landmarks(image, new PointF(960, 600), new PointF(960, 250), 0f);
                byte[] mask = SwimPoseNet.MaskInCrop(net.LastMask, net.LastTransform, new Rectangle(660, 150, 600, 800), 150, 200);
                int on = mask == null ? -1 : mask.Count(v => v != 0);
                b.AppendLine("mask: " + (net.LastMask == null ? "none" : net.LastMask.Length + " values") + ", in a 150 × 200 crop " + on + " pixels on the person, " +
                    watch.ElapsedMilliseconds + " ms");
                net.KeepMask = false;
            }
        }

        SwimSkeleton s = StartSkeleton();
        b.AppendLine("fictional skeleton: " + s.Count + " frames, found " + s.Found);
        return b.ToString();
    }

    /// <summary>
    /// The graphics of the PDF report drawn by the program (the summary card, the race against the final) for the fictional backstroke,
    /// through the swim analysis of the program as in a session (the race of the panel set for the drawing).
    /// </summary>
    private static void RenderReport(string folder, List<string> names, SwimRace race, SwimComputed computed, SwimFinal final, SwimTiming timing)
    {
        StringBuilder info = new StringBuilder();
        try
        {
            Kinovea.Services.Software.Initialize(new Version(2025, 2, 0));
            SwimAnalysisManager manager = SwimAnalysisManager.Instance;
            System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            Type type = typeof(SwimAnalysisManager);
            type.GetField("currentRace", flags).SetValue(manager, race);
            type.GetField("currentTiming", flags).SetValue(manager, timing);
            type.GetProperty("Computed", flags).GetSetMethod(true).Invoke(manager, new object[] { computed });
            string error;
            SwimFinals finals = SwimFinals.Parse(FinalsJson(), out error);
            type.GetProperty("Finals", flags).GetSetMethod(true).Invoke(manager, new object[] { finals });
            Dictionary<string, byte[]> images = (Dictionary<string, byte[]>)type.GetMethod("RenderReportGraphics", flags).Invoke(manager, new object[0]);
            foreach (KeyValuePair<string, byte[]> kv in images)
            {
                using (MemoryStream stream = new MemoryStream(kv.Value))
                using (Bitmap image = new Bitmap(stream))
                {
                    info.AppendLine(string.Format(ci, "report graphic {0}: {1}x{2} PNG {3} bytes", kv.Key, image.Width, image.Height, kv.Value.Length));
                    SaveJpeg(image, Path.Combine(folder, "report-" + kv.Key + ".jpg"), 80);
                }

                names.Add("report-" + kv.Key);
            }

            if (images.Count == 0)
                info.AppendLine("report graphics: none drawn");
        }
        catch (Exception e)
        {
            Exception inner = e.InnerException ?? e;
            info.AppendLine("report graphics: " + inner);
        }

        File.AppendAllText(Path.Combine(folder, "report.txt"), info.ToString(), Encoding.UTF8);
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
