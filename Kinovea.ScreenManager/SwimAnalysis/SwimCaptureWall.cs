#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.27.0) The state of the camera of one capture screen of the wall of cameras, as its image and the live panel show it.
    /// </summary>
    public sealed class SwimWallCamera
    {
        /// <summary>The place of the screen in the wall, 1 to 5 (0: not in the wall).</summary>
        public int Number;

        /// <summary>The name of the camera ("" without one).</summary>
        public string Name = "";

        /// <summary>A camera is in the screen.</summary>
        public bool Loaded;

        /// <summary>Its images come (the stream is open).</summary>
        public bool Connected;

        public bool Grabbing;
        public bool Recording;

        /// <summary>The camera records on its own card (a GoPro).</summary>
        public bool OnCamera;

        /// <summary>Images per second that reach the program.</summary>
        public double Fps;

        /// <summary>Images dropped by the program or lost on the way, since the start of the recording (or of the connection).</summary>
        public long Drops;

        /// <summary>The time of the work on an image, in percent of the time between two images.</summary>
        public float Load;

        /// <summary>The duration of the recording going on (0 without one).</summary>
        public double RecordingSeconds;

        /// <summary>(2.28.0) Its distance in the pool from the wall of the start (NaN: not said; 0: out of the water at the blocks).</summary>
        public double Distance = double.NaN;

        /// <summary>(2.28.0) Its line, where its distance is: a part of the width of the image.</summary>
        public float Line = 0.5f;

        /// <summary>(2.28.0) The time of its line in the attempt going on, seconds from the horn (NaN: not yet).</summary>
        public double Crossed = double.NaN;

        /// <summary>(2.29.0) The metres of the lane across its image, measured with a pole (0: not measured).</summary>
        public double Width;

        /// <summary>(2.29.0) The mouse is on its line (it can be dragged) or drags it.</summary>
        public bool LineActive;
    }

    /// <summary>(2.27.0) Where the images of the wall of cameras and its live panel go.</summary>
    public sealed class SwimWallLayout
    {
        /// <summary>The place of each screen, in its order; empty: not shown (another screen alone on the wall).</summary>
        public Rectangle[] Cells = new Rectangle[0];

        /// <summary>The place of the live panel; empty: no room for it.</summary>
        public Rectangle Info = Rectangle.Empty;

        /// <summary>The number of images in each row.</summary>
        public int[] Rows = new int[0];

        /// <summary>The screen alone on the wall, or -1.</summary>
        public int Solo = -1;

        /// <summary>Where the live panel went: "slot" (the free place of the last row), "below", "side" or "" (none).</summary>
        public string InfoPlace = "";
    }

    /// <summary>
    /// (2.27.0) The wall of cameras: two capture screens or more show only their images, whole and joined edge to edge without frames,
    /// in the rows that make them biggest; the live panel takes the free place of the last row (five cameras: three above, two and
    /// the panel below), else the room below or beside the images. Over each image its number, the name of the camera and its state
    /// (images per second, lost images, the recording), and when the mouse is over it three buttons: alone on the wall, snapshot,
    /// settings of the camera. A double click puts a camera alone on the wall, another puts all of them back.
    /// </summary>
    public static class SwimCaptureWall
    {
        /// <summary>The buttons over an image of the wall, shown while the mouse is over it (right to left: settings, snapshot, alone).</summary>
        public const int ButtonSolo = 0, ButtonSnapshot = 1, ButtonConfigure = 2, ButtonCount = 3;

        /// <summary>A layout with more rows is taken only if its images are this much higher (the images side by side read better).</summary>
        public const double MoreRowsGain = 1.12;

        /// <summary>The least room for the live panel at 96 DPI: below the images (height) and beside them (width).</summary>
        public const int InfoBelowMinimum = 110, InfoSideMinimum = 220;

        /// <summary>The highest the live panel goes below the images, as a share of the height of an image, and at most (96 DPI).</summary>
        public const double InfoBelowShare = 0.6;
        public const int InfoBelowMaximum = 240;

        /// <summary>The state of a camera is read again this often for its image (the stream draws it many times per second).</summary>
        public const int StateMilliseconds = 250;

        /// <summary>
        /// Set by the screen manager: a screen asks it to look again whether the screens make a wall (a camera that records on its own
        /// card, a GoPro, keeps its own buttons and so the classic screens).
        /// </summary>
        public static Action RecheckAsked;

        public static void Recheck()
        {
            Action recheck = RecheckAsked;
            if (recheck != null)
                recheck();
        }

        /// <summary>
        /// The start signal of the common row («Take your marks», 1 s, the horn): the horn on the clock of the computer (MinValue: none
        /// yet), the zero of the time of the attempt for the live panel (the videos keep it in their companion KVA).
        /// </summary>
        public static DateTime SignalHorn = DateTime.MinValue;

        #region Layout
        /// <summary>
        /// The places of the images of the wall in 'area': the rows (one to three, the extra images in the first rows) whose images are
        /// biggest, all images of the same height and whole, the rows left aligned in a block in the middle of the area, nothing between
        /// them. 'aspects': width over height of each image (0: not known yet, 16:9). 'solo': the screen alone on the wall (-1: none).
        /// 'scale': the DPI over 96.
        /// </summary>
        public static SwimWallLayout Layout(Rectangle area, IList<double> aspects, bool withInfo, int solo, float scale)
        {
            SwimWallLayout layout = new SwimWallLayout();
            int count = aspects == null ? 0 : aspects.Count;
            layout.Cells = new Rectangle[count];
            if (count == 0 || area.Width < 20 || area.Height < 20)
                return layout;

            double[] a = new double[count];
            for (int i = 0; i < count; i++)
                a[i] = aspects[i] > 0.2 && aspects[i] < 6 ? aspects[i] : 16.0 / 9;

            if (solo >= 0 && solo < count)
            {
                layout.Solo = solo;
                layout.Rows = new int[] { 1 };
                layout.Cells[solo] = Fit(a[solo], area);
                return layout;
            }

            int[] bestRows = null;
            double bestHeight = 0;
            for (int rowCount = 1; rowCount <= Math.Min(count, 3); rowCount++)
            {
                int[] rows = new int[rowCount];
                for (int r = 0; r < rowCount; r++)
                    rows[r] = count / rowCount + (r < count % rowCount ? 1 : 0);

                double height = area.Height / (double)rowCount;
                int cell = 0;
                for (int r = 0; r < rowCount; r++)
                {
                    double sum = 0;
                    for (int c = 0; c < rows[r]; c++, cell++)
                        sum += a[cell];

                    height = Math.Min(height, area.Width / Math.Max(0.1, sum));
                }

                if (height < 8)
                    continue;

                if (bestRows == null || height > bestHeight * MoreRowsGain)
                {
                    bestRows = rows;
                    bestHeight = height;
                }
            }

            if (bestRows == null)
                return layout;

            layout.Rows = bestRows;
            int image = Math.Max(1, (int)Math.Floor(bestHeight));
            int rowsCount = bestRows.Length;

            // The widths of the images of each row (the edges rounded once, so that the images touch).
            int[][] widths = new int[rowsCount][];
            int[] rowWidths = new int[rowsCount];
            int index = 0;
            for (int r = 0; r < rowsCount; r++)
            {
                widths[r] = new int[bestRows[r]];
                double x = 0;
                int previous = 0;
                for (int c = 0; c < bestRows[r]; c++, index++)
                {
                    x += image * a[index];
                    int edge = (int)Math.Round(x);
                    widths[r][c] = Math.Max(1, edge - previous);
                    previous = edge;
                }

                rowWidths[r] = previous;
            }

            int blockWidth = 0;
            foreach (int w in rowWidths)
                blockWidth = Math.Max(blockWidth, w);

            blockWidth = Math.Min(blockWidth, area.Width);
            int blockHeight = Math.Min(image * rowsCount, area.Height);

            // The live panel: the free place of the last row, else below the images, else beside them, else none.
            Size info = Size.Empty;
            int last = rowsCount - 1;
            int below = (int)Math.Round(InfoBelowMinimum * scale);
            int side = (int)Math.Round(InfoSideMinimum * scale);
            int groupWidth = blockWidth, groupHeight = blockHeight;
            if (withInfo)
            {
                int free = blockWidth - rowWidths[last];
                if (bestRows[last] < bestRows[0] && free >= Math.Min(side, image))
                {
                    layout.InfoPlace = "slot";
                    info = new Size(free, image);
                }
                else if (area.Height - blockHeight >= below)
                {
                    layout.InfoPlace = "below";
                    int height = Math.Min(area.Height - blockHeight, Math.Max(below, Math.Min((int)Math.Round(InfoBelowMaximum * scale), (int)Math.Round(image * InfoBelowShare))));
                    info = new Size(blockWidth, height);
                    groupHeight += height;
                }
                else if (area.Width - blockWidth >= side)
                {
                    layout.InfoPlace = "side";
                    int width = Math.Min(area.Width - blockWidth, Math.Max(side, (int)Math.Round(image * a[0] * 0.75)));
                    info = new Size(width, blockHeight);
                    groupWidth += width;
                }
            }

            int left = area.Left + Math.Max(0, (area.Width - groupWidth) / 2);
            int top = area.Top + Math.Max(0, (area.Height - groupHeight) / 2);
            index = 0;
            for (int r = 0; r < rowsCount; r++)
            {
                int x = left;
                for (int c = 0; c < bestRows[r]; c++, index++)
                {
                    layout.Cells[index] = new Rectangle(x, top + r * image, widths[r][c], image);
                    x += widths[r][c];
                }
            }

            switch (layout.InfoPlace)
            {
                case "slot":
                    layout.Info = new Rectangle(left + rowWidths[last], top + last * image, info.Width, info.Height);
                    break;
                case "below":
                    layout.Info = new Rectangle(left, top + blockHeight, info.Width, info.Height);
                    break;
                case "side":
                    layout.Info = new Rectangle(left + blockWidth, top, info.Width, info.Height);
                    break;
            }

            return layout;
        }

        /// <summary>An image of this aspect as big as it fits in 'area', in its middle.</summary>
        public static Rectangle Fit(double aspect, Rectangle area)
        {
            if (aspect <= 0 || area.Width <= 0 || area.Height <= 0)
                return area;

            int width = area.Width;
            int height = (int)Math.Round(width / aspect);
            if (height > area.Height)
            {
                height = area.Height;
                width = (int)Math.Round(height * aspect);
            }

            return new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, Math.Max(1, width), Math.Max(1, height));
        }
        #endregion

        #region Over the images
        /// <summary>The size of the things drawn over an image: the DPI, a little smaller on small images and bigger on big ones.</summary>
        public static float OverlayScale(Rectangle image, float scale)
        {
            return Math.Max(1f, scale) * Math.Max(0.75f, Math.Min(1.3f, image.Width / 640f));
        }

        /// <summary>The places of the buttons over an image (bottom right, right to left: settings, snapshot, alone on the wall).</summary>
        public static Rectangle[] Buttons(Rectangle image, float scale)
        {
            float s = OverlayScale(image, scale);
            int size = (int)Math.Round(30 * s);
            int gap = (int)Math.Round(6 * s);
            int margin = (int)Math.Round(12 * s);
            Rectangle[] places = new Rectangle[ButtonCount];
            int x = image.Right - margin - size;
            int y = image.Bottom - margin - size;
            for (int i = ButtonCount - 1; i >= 0; i--)
            {
                places[i] = new Rectangle(x, y, size, size);
                x -= size + gap;
            }

            return places;
        }

        /// <summary>The button under a point of the viewport (-1: none).</summary>
        public static int HitButton(Rectangle image, float scale, Point point)
        {
            Rectangle[] places = Buttons(image, scale);
            for (int i = 0; i < places.Length; i++)
            {
                Rectangle r = places[i];
                r.Inflate(2, 2);
                if (r.Contains(point))
                    return i;
            }

            return -1;
        }

        /// <summary>What a button does, as its label says it.</summary>
        public static string ButtonLabel(int button, bool solo, bool recording)
        {
            switch (button)
            {
                case ButtonSolo:
                    return solo ? SwimLang.T("Πίσω σε όλες τις κάμερες") : SwimLang.T("Μόνη της σε όλη την οθόνη");
                case ButtonSnapshot:
                    return SwimLang.T("Στιγμιότυπο");
                case ButtonConfigure:
                    return recording ? SwimLang.T("Ρυθμίσεις κάμερας (όχι στην εγγραφή)") : SwimLang.T("Ρυθμίσεις κάμερας");
                default:
                    return "";
            }
        }

        /// <summary>
        /// What goes over an image of the wall: its number and the name of its camera (top left), its state (top right: images per
        /// second and a dot green / amber / red, the recording in red with its time, the lost images in amber), thin corner brackets
        /// (red with a red frame while it records), and while the mouse is over it its three buttons. Without an image: the number and
        /// what the screen waits for in its middle. 'image': the place of the image in the viewport (the whole viewport without one).
        /// </summary>
        public static void PaintOverlay(Graphics g, Rectangle image, SwimWallCamera camera, RecordingStatus status, float progress, bool hasImage,
            bool hover, int hoverButton, bool solo, float scale)
        {
            if (g == null || camera == null || image.Width < 24 || image.Height < 24)
                return;

            SmoothingMode smoothing = g.SmoothingMode;
            TextRenderingHint hint = g.TextRenderingHint;
            PixelOffsetMode offset = g.PixelOffsetMode;
            CompositingQuality compositing = g.CompositingQuality;
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                // Sizes in pixels follow the DPI and the size of the image; the sizes of the fonts (in points) only the size of the image,
                // the points already follow the DPI.
                float s = OverlayScale(image, scale);
                float fs = s / Math.Max(1f, scale);
                bool recording = status == RecordingStatus.Recording || camera.Recording;
                bool paused = !recording && status == RecordingStatus.Paused;
                bool empty = !camera.Loaded || status == RecordingStatus.Disconnected;

                if (!hasImage)
                    PaintWaiting(g, image, camera, empty, paused, s, fs);

                if (hasImage && recording)
                    PaintRecordingFrame(g, image, progress, s);

                if (hasImage && SwimWallPlaces.Under(camera.Distance))
                    PaintLine(g, image, camera, s, fs);

                RectangleF inner = RectangleF.Inflate(image, -7 * s, -7 * s);
                Color bracket = recording ? SwimTheme.Red : SwimTheme.Accent;
                SwimTheme.Brackets(g, inner, bracket, hover ? 0.95f : (recording ? 0.85f : 0.45f));

                PaintBadge(g, image, camera, s, fs);
                if (!empty)
                    PaintState(g, image, camera, recording, paused, hasImage, s, fs);

                if (hover && !empty)
                    PaintButtons(g, image, hoverButton, solo, recording, scale, s, fs);
            }
            catch (Exception e)
            {
                log.Error("Error while drawing over an image of the wall of cameras.", e);
            }
            finally
            {
                g.SmoothingMode = smoothing;
                g.TextRenderingHint = hint;
                g.PixelOffsetMode = offset;
                g.CompositingQuality = compositing;
            }
        }

        private static void PaintWaiting(Graphics g, Rectangle image, SwimWallCamera camera, bool empty, bool paused, float s, float fs)
        {
            using (LinearGradientBrush back = new LinearGradientBrush(image, SwimTheme.Surface, SwimTheme.Back, LinearGradientMode.Vertical))
                g.FillRectangle(back, image);

            using (Pen grid = new Pen(Color.FromArgb(40, SwimTheme.GridLine), 1f))
            {
                float step = 32 * s;
                for (float y = image.Top + step; y < image.Bottom; y += step)
                    g.DrawLine(grid, image.Left, y, image.Right, y);
            }

            string number = camera.Number > 0 ? camera.Number.ToString(CultureInfo.InvariantCulture) : "";
            string title = empty ? SwimLang.T("Χωρίς κάμερα") : (paused ? SwimLang.T("Σε παύση") : SwimLang.T("Σύνδεση με την κάμερα"));
            Font big = FontOf(SwimTheme.LightFamily, Math.Min(48f, Math.Max(18f, image.Height / 9f / Math.Max(1f, s / fs))));
            Font label = Display(10f * fs);
            SizeF numberSize = string.IsNullOrEmpty(number) ? SizeF.Empty : g.MeasureString(number, big);
            float labelWidth = SwimTheme.TrackedWidth(g, SwimLang.Upper(title), label);
            float total = numberSize.Height * 0.9f + 8 * s + label.GetHeight(g);
            float y0 = image.Top + (image.Height - total) / 2f;
            if (!string.IsNullOrEmpty(number))
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(120, SwimTheme.Faint)))
                    g.DrawString(number, big, brush, image.Left + (image.Width - numberSize.Width) / 2f, y0 - numberSize.Height * 0.08f);
            }

            using (SolidBrush brush = new SolidBrush(empty ? SwimTheme.Dim : (paused ? SwimTheme.Amber : SwimTheme.Accent)))
                SwimTheme.Tracked(g, SwimLang.Upper(title), label, brush, image.Left + (image.Width - labelWidth) / 2f, y0 + numberSize.Height * 0.9f + 8 * s);
        }

        /// <summary>
        /// (2.28.0) The line of a camera under the water at its distance (dashed, its distance on top) and, once the head of the swimmer
        /// passed it in the attempt going on, its time from the horn (≈: from the live image) at its foot.
        /// </summary>
        private static void PaintLine(Graphics g, Rectangle image, SwimWallCamera camera, float s, float fs)
        {
            float x = image.Left + Math.Max(0.02f, Math.Min(0.98f, camera.Line)) * image.Width;
            float top = image.Top + 70 * s, bottom = image.Bottom - 20 * s;
            if (bottom - top < 30 * s)
                return;

            // (2.29.0) Solid and thicker while the mouse is on it (it can be dragged) or drags it.
            bool active = camera.LineActive;
            using (Pen shade = new Pen(Color.FromArgb(110, SwimTheme.Back), (active ? 5f : 3.6f) * s))
                g.DrawLine(shade, x, top, x, bottom);
            using (Pen pen = new Pen(Color.FromArgb(235, SwimTheme.Accent), (active ? 2.4f : 1.6f) * s))
            {
                if (!active)
                    pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, x, top, x, bottom);
            }

            Font font = Display(10f * fs);
            string label = SwimWallPlaces.Label(camera.Distance);
            string chipText = LineLabel(camera);
            SizeF size = g.MeasureString(chipText, font);
            RectangleF chip = new RectangleF(x - size.Width / 2f - 7 * s, top - size.Height - 6 * s, size.Width + 14 * s, size.Height + 4 * s);
            chip.X = Math.Max(image.Left + 4 * s, Math.Min(image.Right - 4 * s - chip.Width, chip.X));
            SwimTheme.FillRounded(g, Color.FromArgb(active ? 235 : 200, SwimTheme.Back), chip, 3 * s);
            SwimTheme.DrawRounded(g, SwimTheme.Accent, chip, 3 * s);
            using (SolidBrush brush = new SolidBrush(SwimTheme.Accent))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(chipText, font, brush, chip, center);

            if (double.IsNaN(camera.Crossed))
                return;

            Font big = Display(11.5f * fs);
            string time = label + " · ≈" + camera.Crossed.ToString("0.00", CultureInfo.InvariantCulture) + " s";
            SizeF timeSize = g.MeasureString(time, big);
            float left = x + 8 * s;
            if (left + timeSize.Width + 16 * s > image.Right - 8 * s)
                left = x - 8 * s - timeSize.Width - 16 * s;

            RectangleF badge = new RectangleF(left, bottom - timeSize.Height - 14 * s, timeSize.Width + 16 * s, timeSize.Height + 8 * s);
            SwimTheme.FillRounded(g, SwimTheme.Accent, badge, 3 * s);
            using (SolidBrush brush = new SolidBrush(SwimTheme.Back))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(time, big, brush, badge, center);
        }

        /// <summary>
        /// (2.29.0) The chip over a line: its distance, the metres its image spans once measured with a pole, how to move it while the
        /// mouse is on it.
        /// </summary>
        public static string LineLabel(SwimWallCamera camera)
        {
            string label = SwimWallPlaces.Label(camera.Distance);
            if (camera.LineActive)
                return label + " · " + SwimLang.T("σύρε τη γραμμή στο σημάδι της πισίνας");
            if (camera.Width > 0)
                return label + " · " + string.Format(CultureInfo.InvariantCulture, SwimLang.T("εικόνα {0:0.00} m"), camera.Width);
            return label;
        }

        /// <summary>
        /// (2.29.0) The measure of the scale of an image with a pole: what to click (over the image, near its top), the marks clicked and
        /// the line from the first one to the mouse. 'points' and 'mouse' in the viewport.
        /// </summary>
        public static void PaintPole(Graphics g, Rectangle image, IList<PointF> points, Point? mouse, float scale)
        {
            if (g == null || points == null || image.Width < 24 || image.Height < 24)
                return;

            SmoothingMode smoothing = g.SmoothingMode;
            TextRenderingHint hint = g.TextRenderingHint;
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                float s = OverlayScale(image, scale);
                float fs = s / Math.Max(1f, scale);
                string text = points.Count == 0
                    ? SwimLang.T("Κλίμακα: κλικ στο ένα σημάδι του κονταριού (δεξί κλικ: ακύρωση)")
                    : SwimLang.T("Κλίμακα: κλικ στο άλλο σημάδι του κονταριού");
                Font font = Display(11f * fs);
                SizeF size = g.MeasureString(text, font);
                if (size.Width + 20 * s > image.Width - 8 * s)
                {
                    font = Display(9f * fs);
                    size = g.MeasureString(text, font);
                }

                float width = Math.Min(size.Width + 20 * s, image.Width - 8 * s);
                RectangleF chip = new RectangleF(image.Left + (image.Width - width) / 2f, image.Top + 44 * s, width, size.Height + 10 * s);
                SwimTheme.FillRounded(g, SwimTheme.Amber, chip, 4 * s);
                using (SolidBrush brush = new SolidBrush(SwimTheme.Back))
                using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                    g.DrawString(text, font, brush, chip, center);

                using (Pen shade = new Pen(Color.FromArgb(150, SwimTheme.Back), 4.5f * s))
                using (Pen pen = new Pen(SwimTheme.Amber, 2f * s))
                {
                    if (points.Count > 0 && mouse.HasValue)
                    {
                        g.DrawLine(shade, points[0], mouse.Value);
                        using (Pen dash = new Pen(SwimTheme.Amber, 2f * s) { DashStyle = DashStyle.Dash })
                            g.DrawLine(dash, points[0], mouse.Value);
                    }

                    foreach (PointF p in points)
                    {
                        float r = 7 * s, arm = 12 * s;
                        g.DrawEllipse(shade, p.X - r, p.Y - r, 2 * r, 2 * r);
                        g.DrawEllipse(pen, p.X - r, p.Y - r, 2 * r, 2 * r);
                        g.DrawLine(pen, p.X - arm, p.Y, p.X + arm, p.Y);
                        g.DrawLine(pen, p.X, p.Y - arm, p.X, p.Y + arm);
                    }
                }
            }
            catch (Exception e)
            {
                log.Error("Error while drawing the measure of a pole over an image of the wall of cameras.", e);
            }
            finally
            {
                g.SmoothingMode = smoothing;
                g.TextRenderingHint = hint;
            }
        }

        private static void PaintRecordingFrame(Graphics g, Rectangle image, float progress, float s)
        {
            float width = Math.Max(2f, 2.5f * s);
            using (Pen pen = new Pen(Color.FromArgb(230, SwimTheme.Red), width))
                g.DrawRectangle(pen, image.Left + width / 2f, image.Top + width / 2f, image.Width - width, image.Height - width);

            // The time left of a recording with a highest duration, along the bottom.
            if (progress > 0f && progress < 1f)
            {
                float height = Math.Max(3f, 4f * s);
                using (SolidBrush rest = new SolidBrush(Color.FromArgb(150, 40, 6, 10)))
                    g.FillRectangle(rest, image.Left, image.Bottom - height, image.Width, height);
                using (SolidBrush left = new SolidBrush(SwimTheme.Red))
                    g.FillRectangle(left, image.Left, image.Bottom - height, image.Width * progress, height);
            }
        }

        private static void PaintBadge(Graphics g, Rectangle image, SwimWallCamera camera, float s, float fs)
        {
            float x = image.Left + 12 * s, y = image.Top + 12 * s;
            float box = 26 * s;
            string number = camera.Number > 0 ? camera.Number.ToString(CultureInfo.InvariantCulture) : "·";
            string name = string.IsNullOrEmpty(camera.Name) ? "" : camera.Name;
            if (!double.IsNaN(camera.Distance))
                name = (name.Length > 0 ? name + " · " : "") + SwimWallPlaces.Label(camera.Distance);
            Font numberFont = Display(12.5f * fs);
            Font nameFont = Display(10.5f * fs);

            float nameWidth = 0;
            if (name.Length > 0)
            {
                nameWidth = g.MeasureString(name, nameFont).Width;
                nameWidth = Math.Min(nameWidth, Math.Max(0, image.Width * 0.55f - box));
            }

            if (nameWidth > 0)
            {
                RectangleF plate = new RectangleF(x + box - 2 * s, y, nameWidth + 18 * s, box);
                SwimTheme.FillRounded(g, Color.FromArgb(190, SwimTheme.Back), plate, 5 * s);
                using (SolidBrush brush = new SolidBrush(SwimTheme.Text))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(name, nameFont, brush, new RectangleF(plate.Left + 9 * s, plate.Top, nameWidth + 4 * s, plate.Height), format);
            }

            RectangleF square = new RectangleF(x, y, box, box);
            SwimTheme.FillRounded(g, SwimTheme.Accent, square, 5 * s);
            using (SolidBrush brush = new SolidBrush(SwimTheme.Back))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(number, numberFont, brush, new RectangleF(square.Left, square.Top + 0.5f * s, square.Width, square.Height), format);
        }

        private static void PaintState(Graphics g, Rectangle image, SwimWallCamera camera, bool recording, bool paused, bool hasImage, float s, float fs)
        {
            float right = image.Right - 12 * s, y = image.Top + 12 * s;
            float height = 26 * s;
            Font font = Display(10.5f * fs);
            string text;
            Color fill, ink, dot;
            if (recording)
            {
                text = "REC  " + Clock(camera.RecordingSeconds, false);
                fill = Color.FromArgb(235, 196, 38, 46);
                ink = Color.White;
                dot = Color.White;
            }
            else if (paused)
            {
                text = SwimLang.Upper(SwimLang.T("Παύση"));
                fill = Color.FromArgb(225, SwimTheme.AmberDark);
                ink = SwimTheme.Amber;
                dot = SwimTheme.Amber;
            }
            else if (camera.OnCamera)
            {
                text = SwimLang.Upper(SwimLang.T("Γράφει στην κάρτα της"));
                fill = Color.FromArgb(190, SwimTheme.Back);
                ink = SwimTheme.Text;
                dot = SwimTheme.Accent;
            }
            else
            {
                text = camera.Fps > 0 ? string.Format(CultureInfo.CurrentCulture, "{0:0} fps", camera.Fps) : SwimLang.T("χωρίς εικόνα");
                fill = Color.FromArgb(190, SwimTheme.Back);
                ink = SwimTheme.Text;
                dot = !hasImage || camera.Fps <= 0 || camera.Load >= 100 ? SwimTheme.Red : (camera.Drops > 0 || camera.Load >= 85 ? SwimTheme.Amber : SwimTheme.Green);
            }

            float textWidth = g.MeasureString(text, font).Width;
            float dotSize = 9 * s;
            RectangleF chip = new RectangleF(right - (textWidth + dotSize + 24 * s), y, textWidth + dotSize + 24 * s, height);
            SwimTheme.FillRounded(g, fill, chip, 5 * s);
            float dotX = chip.Left + 10 * s, dotY = chip.Top + (height - dotSize) / 2f;
            if (recording)
            {
                // The dot of the recording beats once a second.
                double phase = (Environment.TickCount & int.MaxValue) % 1000 / 1000.0;
                int alpha = (int)Math.Round(150 + 105 * Math.Cos(phase * 2 * Math.PI));
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(Math.Max(60, Math.Min(255, alpha)), dot)))
                    g.FillEllipse(brush, dotX, dotY, dotSize, dotSize);
            }
            else
            {
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(70, dot)))
                    g.FillEllipse(glow, dotX - 2.5f * s, dotY - 2.5f * s, dotSize + 5 * s, dotSize + 5 * s);
                using (SolidBrush brush = new SolidBrush(dot))
                    g.FillEllipse(brush, dotX, dotY, dotSize, dotSize);
            }

            using (SolidBrush brush = new SolidBrush(ink))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text, font, brush, new RectangleF(dotX + dotSize + 6 * s, chip.Top + 0.5f * s, textWidth + 8 * s, height), format);

            // The images lost since the start of the recording (or of the connection), under it.
            if (camera.Drops > 0 && !camera.OnCamera)
            {
                string lost = string.Format(CultureInfo.CurrentCulture, "{0} {1}", camera.Drops, SwimLang.Upper(SwimLang.T(camera.Drops == 1 ? "χαμένο καρέ" : "χαμένα καρέ")));
                Font small = Display(9f * fs);
                float width = g.MeasureString(lost, small).Width + 16 * s;
                RectangleF line = new RectangleF(right - width, chip.Bottom + 5 * s, width, 21 * s);
                SwimTheme.FillRounded(g, Color.FromArgb(225, SwimTheme.AmberDark), line, 4 * s);
                using (SolidBrush brush = new SolidBrush(SwimTheme.Amber))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(lost, small, brush, line, format);
            }
        }

        private static void PaintButtons(Graphics g, Rectangle image, int hoverButton, bool solo, bool recording, float scale, float s, float fs)
        {
            Rectangle[] places = Buttons(image, scale);
            for (int i = 0; i < places.Length; i++)
            {
                bool disabled = i == ButtonConfigure && recording;
                bool hot = i == hoverButton && !disabled;
                RectangleF r = places[i];
                SwimTheme.FillRounded(g, hot ? Color.FromArgb(240, SwimTheme.AccentDark) : Color.FromArgb(205, SwimTheme.Back), r, 6 * s);
                SwimTheme.DrawRounded(g, hot ? SwimTheme.Accent : Color.FromArgb(150, SwimTheme.Border), r, 6 * s);
                Color ink = disabled ? SwimTheme.Faint : (hot ? SwimTheme.AccentPale : SwimTheme.Text);
                RectangleF icon = RectangleF.Inflate(r, -r.Width * 0.27f, -r.Height * 0.27f);
                switch (i)
                {
                    case ButtonSolo: IconSolo(g, icon, ink, solo, s); break;
                    case ButtonSnapshot: IconCamera(g, icon, ink, s); break;
                    case ButtonConfigure: IconGear(g, icon, ink, s); break;
                }
            }

            if (hoverButton < 0 || hoverButton >= places.Length)
                return;

            string label = ButtonLabel(hoverButton, solo, recording);
            Font font = Display(10f * fs);
            float width = g.MeasureString(label, font).Width + 18 * s;
            RectangleF plate = new RectangleF(places[0].Left - 8 * s - width, places[0].Top + (places[0].Height - 24 * s) / 2f, width, 24 * s);
            if (plate.Left < image.Left + 4)
                return;

            SwimTheme.FillRounded(g, Color.FromArgb(215, SwimTheme.Back), plate, 4 * s);
            using (SolidBrush brush = new SolidBrush(SwimTheme.Text))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(label, font, brush, plate, format);
        }
        #endregion

        #region Icons
        /// <summary>Four corners pointing out (alone on the wall), or in (back to all the cameras).</summary>
        internal static void IconSolo(Graphics g, RectangleF r, Color ink, bool inward, float s)
        {
            float l = r.Width * 0.38f;
            using (Pen pen = new Pen(ink, Math.Max(1.4f, 1.7f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (!inward)
                {
                    g.DrawLines(pen, new[] { new PointF(r.Left, r.Top + l), new PointF(r.Left, r.Top), new PointF(r.Left + l, r.Top) });
                    g.DrawLines(pen, new[] { new PointF(r.Right - l, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Top + l) });
                    g.DrawLines(pen, new[] { new PointF(r.Left, r.Bottom - l), new PointF(r.Left, r.Bottom), new PointF(r.Left + l, r.Bottom) });
                    g.DrawLines(pen, new[] { new PointF(r.Right - l, r.Bottom), new PointF(r.Right, r.Bottom), new PointF(r.Right, r.Bottom - l) });
                }
                else
                {
                    float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, d = r.Width * 0.12f;
                    g.DrawLines(pen, new[] { new PointF(cx - d - l, cy - d), new PointF(cx - d, cy - d), new PointF(cx - d, cy - d - l) });
                    g.DrawLines(pen, new[] { new PointF(cx + d, cy - d - l), new PointF(cx + d, cy - d), new PointF(cx + d + l, cy - d) });
                    g.DrawLines(pen, new[] { new PointF(cx - d - l, cy + d), new PointF(cx - d, cy + d), new PointF(cx - d, cy + d + l) });
                    g.DrawLines(pen, new[] { new PointF(cx + d, cy + d + l), new PointF(cx + d, cy + d), new PointF(cx + d + l, cy + d) });
                }
            }
        }

        /// <summary>A megaphone (the start signal): its mouth opening to the right, its handle and two waves of sound.</summary>
        internal static void IconHorn(Graphics g, RectangleF r, Color ink, float s)
        {
            using (SolidBrush brush = new SolidBrush(ink))
            using (Pen pen = new Pen(ink, Math.Max(1.2f, 1.4f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float cy = r.Top + r.Height / 2f;
                g.FillPolygon(brush, new[] { new PointF(r.Left, cy - r.Height * 0.16f), new PointF(r.Left + r.Width * 0.22f, cy - r.Height * 0.16f),
                    new PointF(r.Left + r.Width * 0.62f, r.Top + r.Height * 0.05f), new PointF(r.Left + r.Width * 0.62f, r.Bottom - r.Height * 0.05f),
                    new PointF(r.Left + r.Width * 0.22f, cy + r.Height * 0.16f), new PointF(r.Left, cy + r.Height * 0.16f) });
                g.DrawArc(pen, r.Left + r.Width * 0.52f, cy - r.Height * 0.25f, r.Width * 0.3f, r.Height * 0.5f, -50, 100);
                g.DrawArc(pen, r.Left + r.Width * 0.56f, cy - r.Height * 0.42f, r.Width * 0.44f, r.Height * 0.84f, -50, 100);
            }
        }

        /// <summary>A camera: its body, the bump on top and the lens.</summary>
        internal static void IconCamera(Graphics g, RectangleF r, Color ink, float s)
        {
            using (Pen pen = new Pen(ink, Math.Max(1.3f, 1.6f * s)) { LineJoin = LineJoin.Round })
            {
                RectangleF body = new RectangleF(r.Left, r.Top + r.Height * 0.22f, r.Width, r.Height * 0.72f);
                g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
                g.DrawLines(pen, new[] { new PointF(r.Left + r.Width * 0.30f, body.Top), new PointF(r.Left + r.Width * 0.38f, r.Top + r.Height * 0.06f),
                    new PointF(r.Left + r.Width * 0.62f, r.Top + r.Height * 0.06f), new PointF(r.Left + r.Width * 0.70f, body.Top) });
                float d = body.Height * 0.62f;
                g.DrawEllipse(pen, body.Left + (body.Width - d) / 2f, body.Top + (body.Height - d) / 2f, d, d);
            }
        }

        /// <summary>A gear: a ring with eight teeth and its hole.</summary>
        internal static void IconGear(Graphics g, RectangleF r, Color ink, float s)
        {
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            float outer = r.Width / 2f, ring = outer * 0.66f, hole = outer * 0.28f;
            using (Pen teeth = new Pen(ink, Math.Max(2f, r.Width * 0.17f)) { StartCap = LineCap.Flat, EndCap = LineCap.Flat })
            {
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * Math.PI / 4;
                    float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
                    g.DrawLine(teeth, cx + cos * ring * 0.9f, cy + sin * ring * 0.9f, cx + cos * outer, cy + sin * outer);
                }
            }

            using (Pen pen = new Pen(ink, Math.Max(1.6f, r.Width * 0.16f)))
                g.DrawEllipse(pen, cx - ring + r.Width * 0.08f, cy - ring + r.Width * 0.08f, 2 * ring - r.Width * 0.16f, 2 * ring - r.Width * 0.16f);
            using (Pen pen = new Pen(ink, Math.Max(1.2f, 1.3f * s)))
                g.DrawEllipse(pen, cx - hole, cy - hole, 2 * hole, 2 * hole);
        }
        #endregion

        #region Helpers
        /// <summary>A duration as minutes:seconds (with tenths when asked): 00:12.4.</summary>
        public static string Clock(double seconds, bool tenths)
        {
            if (double.IsNaN(seconds) || seconds < 0)
                seconds = 0;

            if (tenths)
            {
                long t = (long)Math.Floor(seconds * 10);
                return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}.{2}", t / 600, t / 10 % 60, t % 10);
            }

            long whole = (long)Math.Floor(seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", whole / 60, whole % 60);
        }

        /// <summary>The display font in half points (a few sizes kept, whatever the size of the images).</summary>
        internal static Font Display(float points)
        {
            return SwimTheme.Display(Half(points));
        }

        internal static Font FontOf(string family, float points)
        {
            return family == SwimTheme.LightFamily ? SwimTheme.DisplayLight(Half(points)) : SwimTheme.Display(Half(points));
        }

        private static float Half(float points)
        {
            return Math.Max(6f, Math.Min(96f, (float)Math.Round(points * 2) / 2f));
        }
        #endregion

        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
    }

    /// <summary>(Swim 2.29.0) The length between the two marks of the pole clicked on an image (metres; null: cancelled).</summary>
    public static class SwimPoleDialog
    {
        public static double? Ask(IWin32Window owner, double initial)
        {
            using (Form f = new Form())
            {
                f.Text = SwimLang.T("Κλίμακα με κοντάρι");
                f.StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.AutoScaleMode = AutoScaleMode.Font;
                f.ClientSize = new Size(430, 150);
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                Label question = new Label { Text = SwimLang.T("Πόσα μέτρα απέχουν τα δύο σημάδια του κονταριού;"), AutoSize = false, Location = new Point(12, 14), Size = new Size(406, 40) };
                NumericUpDown value = new NumericUpDown { DecimalPlaces = 2, Increment = 0.05m, Minimum = 0.20m, Maximum = 10m, Location = new Point(12, 58), Width = 120 };
                value.Value = (decimal)Math.Max(0.2, Math.Min(10.0, initial));
                Label unit = new Label { Text = "m", AutoSize = true, Location = new Point(138, 61) };
                Button ok = new Button { Text = SwimLang.T("Εντάξει"), DialogResult = DialogResult.OK, Location = new Point(242, 108), Width = 85 };
                Button cancel = new Button { Text = SwimLang.T("Άκυρο"), DialogResult = DialogResult.Cancel, Location = new Point(333, 108), Width = 85 };
                f.Controls.AddRange(new Control[] { question, value, unit, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Shown += (s, e) => value.Select(0, value.Text.Length);
                return f.ShowDialog(owner) == DialogResult.OK ? (double)value.Value : (double?)null;
            }
        }
    }
}
