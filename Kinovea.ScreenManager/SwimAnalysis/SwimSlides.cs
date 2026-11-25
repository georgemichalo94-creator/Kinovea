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
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.39.0) The slides of a race in its package, as the analyst asked: the pages of the report as slides at the end of the video of
    /// the package (3 s each, from and to the dark of the slides), and the posts for the social media of the federation (4:5) as
    /// pictures in the folder «Instagram» of the package. The slides come with the pages of the report (the Apps Script 2.26.0,
    /// SwimV2.slides: the same numbers as the PDF), their pictures from the Edge of the computer (SwimPdfPrinter.Shoot), the logo of
    /// the federation from the folder of Kinovea (SwimAnalysis\logo.png) and the picture of the cover from the video (the entry of the
    /// start).
    /// </summary>
    public static class SwimSlides
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>The seconds of a slide in the video, and of its fade in and out.</summary>
        public const double SlideSeconds = 3.0;
        public const double FadeSeconds = 0.25;

        /// <summary>The dark of the slides (#0A1322): around a slide in a video of another shape, and the colour of its fades.</summary>
        public static readonly Color Ground = Color.FromArgb(10, 19, 34);

        /// <summary>The folder of the posts in the folder of the package, and the name of each (its number in the set).</summary>
        public const string SocialFolderName = "Instagram";

        public static string SocialName(int number)
        {
            return "Instagram " + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png";
        }

        /// <summary>The logo of the federation on the slides: SwimAnalysis\logo.png next to Kinovea (PNG, white on transparent).</summary>
        public static string LogoPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SwimAnalysis", "logo.png"); }
        }

        /// <summary>The bytes of the logo, null when there is none (the slides without it).</summary>
        public static byte[] Logo()
        {
            try
            {
                string path = LogoPath;
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception e)
            {
                log.Warn("The logo of the slides was not read: " + e.Message);
                return null;
            }
        }

        /// <summary>The kind of a picture for a data URI: PNG, else JPEG.</summary>
        private static string MimeOf(byte[] image)
        {
            return image.Length > 4 && image[0] == 0x89 && image[1] == 'P' && image[2] == 'N' && image[3] == 'G' ? "image/png" : "image/jpeg";
        }

        /// <summary>
        /// The pages of the slides with their pictures: the logo ({{LOGO}}) and the frame of the cover ({{FRAME}}) as data URIs. Without
        /// a picture its place stays as it is (the slides hide a logo they do not have, the cover has the lanes of a pool).
        /// </summary>
        public static string WithImages(string html, byte[] logo, byte[] cover)
        {
            if (string.IsNullOrEmpty(html))
                return html;
            if (logo != null && logo.Length > 4)
                html = html.Replace("{{LOGO}}", "data:" + MimeOf(logo) + ";base64," + Convert.ToBase64String(logo));
            if (cover != null && cover.Length > 4)
                html = html.Replace("{{FRAME}}", "data:" + MimeOf(cover) + ";base64," + Convert.ToBase64String(cover));
            return html;
        }

        /// <summary>The frames of a slide at the rate of the video (3 s).</summary>
        public static int FramesOf(double fps)
        {
            return Math.Max(1, (int)Math.Round(SlideSeconds * Math.Max(1, fps)));
        }

        /// <summary>How much of the slide is seen in frame i of its n: from the dark in its first quarter of a second, to the dark in its last.</summary>
        public static double Opacity(int i, int n, double fps)
        {
            int fade = Math.Max(1, (int)Math.Round(FadeSeconds * Math.Max(1, fps)));
            if (n <= 2 * fade + 1)
                return 1;
            return Math.Max(0, Math.Min(1.0, Math.Min((i + 1) / (double)(fade + 1), (n - i) / (double)(fade + 1))));
        }

        /// <summary>
        /// A slide (PNG) in a video of this size: as large as it fits, in the middle, the dark around it (a video of another shape). The
        /// rows of the image in 24-bit BGR, as the encoder takes them.
        /// </summary>
        public static byte[] Fit(byte[] png, Size size)
        {
            using (MemoryStream m = new MemoryStream(png))
            using (Bitmap slide = new Bitmap(m))
            using (Bitmap frame = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(frame))
                {
                    g.Clear(Ground);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    double k = Math.Min(size.Width / (double)slide.Width, size.Height / (double)slide.Height);
                    int w = Math.Max(1, (int)Math.Round(slide.Width * k)), h = Math.Max(1, (int)Math.Round(slide.Height * k));
                    int x = (size.Width - w) / 2, y = (size.Height - h) / 2;
                    using (ImageAttributes edges = new ImageAttributes())
                    {
                        // The edges of the slide without a seam of the dark around them.
                        edges.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(slide, new Rectangle(x, y, w, h), 0, 0, slide.Width, slide.Height, GraphicsUnit.Pixel, edges);
                    }
                }

                return Bgr(frame);
            }
        }

        /// <summary>The rows of a 24-bit image without their padding.</summary>
        private static byte[] Bgr(Bitmap frame)
        {
            int w = frame.Width, h = frame.Height, row = w * 3;
            byte[] bytes = new byte[row * h];
            BitmapData data = frame.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * row, row);
            }
            finally
            {
                frame.UnlockBits(data);
            }

            return bytes;
        }

        /// <summary>A slide between itself (a = 1) and the dark (a = 0), into the second buffer (BGR, the same size).</summary>
        public static void Fade(byte[] slide, byte[] into, double a)
        {
            int k = (int)Math.Round(Math.Max(0, Math.Min(1, a)) * 256);
            int b = Ground.B, g = Ground.G, r = Ground.R;
            int n = Math.Min(slide.Length, into.Length) / 3 * 3;
            for (int i = 0; i < n; i += 3)
            {
                into[i] = (byte)(b + (((slide[i] - b) * k) >> 8));
                into[i + 1] = (byte)(g + (((slide[i + 1] - g) * k) >> 8));
                into[i + 2] = (byte)(r + (((slide[i + 2] - r) * k) >> 8));
            }
        }

        /// <summary>
        /// The slides at the end of the video, after its last image: each fitted to the size of the video, 3 s with its fades. Returns the
        /// frames written; taken false when ffmpeg stopped taking images (what it took is in the video), cancelled when the analyst
        /// stopped the package.
        /// </summary>
        public static int Write(SwimVideoEncoder encoder, IList<byte[]> slides, Size size, double fps, Func<bool> stop, Action<int, int> progress,
            out bool taken, out bool cancelled)
        {
            taken = true;
            cancelled = false;
            int written = 0;
            if (encoder == null || slides == null || slides.Count == 0 || size.Width < 2 || size.Height < 2)
                return 0;

            int n = FramesOf(fps);
            byte[] faded = new byte[size.Width * size.Height * 3];
            for (int s = 0; s < slides.Count; s++)
            {
                byte[] full;
                try
                {
                    full = Fit(slides[s], size);
                }
                catch (Exception e)
                {
                    log.Warn("A slide was not put in the video: " + e.Message);
                    continue;
                }

                for (int i = 0; i < n; i++)
                {
                    if (stop != null && stop())
                    {
                        cancelled = true;
                        return written;
                    }

                    double a = Opacity(i, n, fps);
                    byte[] frame = full;
                    if (a < 0.999)
                    {
                        Fade(full, faded, a);
                        frame = faded;
                    }

                    if (!encoder.Write(frame))
                    {
                        taken = false;
                        return written;
                    }

                    written++;
                }

                if (progress != null)
                    progress(s + 1, slides.Count);
            }

            return written;
        }

        /// <summary>The picture of the cover from an image of the video (24-bit BGR rows of this size): a JPEG.</summary>
        public static byte[] Jpeg(byte[] bgr, Size size)
        {
            using (Bitmap b = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb))
            {
                BitmapData data = b.LockBits(new Rectangle(0, 0, size.Width, size.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int row = size.Width * 3;
                    for (int y = 0; y < size.Height; y++)
                        Marshal.Copy(bgr, y * row, IntPtr.Add(data.Scan0, y * data.Stride), row);
                }
                finally
                {
                    b.UnlockBits(data);
                }

                ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using (MemoryStream m = new MemoryStream())
                {
                    if (codec != null)
                    {
                        using (EncoderParameters p = new EncoderParameters(1))
                        {
                            p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                            b.Save(m, codec, p);
                        }
                    }
                    else
                    {
                        b.Save(m, ImageFormat.Jpeg);
                    }

                    return m.ToArray();
                }
            }
        }
    }
}
