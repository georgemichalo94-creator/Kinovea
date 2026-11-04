#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Kinovea.Services;
using Kinovea.Video;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.16.0) The technique analysis of a group of screens as one: the graphics of the analysis on the screen of the group chosen (the
    /// panels with the data; what lies on the water of the video of the analysis, as the lines 5/10/15, stays on it) or on a panel in the
    /// place of one more screen (the data of the start or the turn only); the snapshots of all the screens of the group together (each in
    /// the shape of its video, joined, the panel in its place); the video of all the screens together (SwimAnalysisManager.GroupVideo).
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>Settings.GroupGraphics: the graphics of a group on the panel in the place of one more screen.</summary>
        public const int GroupGraphicsPanel = -1;

        /// <summary>Set by the screen manager: the screens placed again (the panel of a group comes or goes).</summary>
        public static Action ScreensRelayoutAsked;

        private SwimGroupDashboard groupDashboard;
        private bool groupDashboardPlaced;
        private bool groupRelayoutPending;

        /// <summary>The screen of the analysis is in a group of screens.</summary>
        internal bool InGroup
        {
            get { return player != null && !player.IsDisposed && VideoScreenGroup.Active && VideoScreenGroup.Contains(player); }
        }

        /// <summary>The screens of the group with a video, in the order of the screens (the screen of the analysis alone without a group).</summary>
        internal List<PlayerScreenUserInterface> GroupScreens
        {
            get
            {
                if (!InGroup)
                    return player != null && !player.IsDisposed ? new List<PlayerScreenUserInterface> { player } : new List<PlayerScreenUserInterface>();
                return VideoScreenGroup.Members.Where(m => m != null && !m.IsDisposed && m.SwimFrameServer != null && m.SwimFrameServer.Loaded).ToList();
            }
        }

        /// <summary>The graphics of the group on its panel.</summary>
        internal bool GroupPanelShown
        {
            get { return InGroup && Settings != null && Settings.GroupGraphics == GroupGraphicsPanel; }
        }

        /// <summary>The screen that shows the panels of the graphics: the screen of the analysis, or the one of the group chosen; null: the panel.</summary>
        internal PlayerScreenUserInterface GroupGraphicsScreen
        {
            get
            {
                if (!InGroup || Settings == null)
                    return player;
                int n = Settings.GroupGraphics;
                if (n == GroupGraphicsPanel)
                    return null;
                if (n > 0)
                {
                    PlayerScreenUserInterface s = GroupScreens.FirstOrDefault(m => m.SwimScreenNumber == n);
                    if (s != null)
                        return s;
                }

                return player;
            }
        }

        /// <summary>The panels of the graphics on the video of the analysis (not when its group shows them on another screen or its panel).</summary>
        private bool PanelsOnAnalysisScreen
        {
            get { return !InGroup || GroupGraphicsScreen == player; }
        }

        /// <summary>
        /// The control of the panel of the group for the grid of the screens (null when it is not shown). The screen manager asks for it
        /// when it places the screens.
        /// </summary>
        public Control GroupDashboardCell
        {
            get
            {
                groupDashboardPlaced = GroupPanelShown;
                if (!groupDashboardPlaced)
                    return null;
                if (groupDashboard == null || groupDashboard.IsDisposed)
                    groupDashboard = new SwimGroupDashboard(this);
                return groupDashboard;
            }
        }

        /// <summary>The way the graphics of a group are shown changed (0 the screen of the analysis, N screen N, -1 the panel).</summary>
        public void SetGroupGraphics(int choice)
        {
            if (Settings == null || Settings.GroupGraphics == choice)
                return;
            Settings.GroupGraphics = choice;
            Settings.Save();
            RequestGroupLayout();
            foreach (PlayerScreenUserInterface s in VideoScreenGroup.Members.Where(m => m != null && !m.IsDisposed))
                s.Invalidate(true);
            InvalidatePlayer();
        }

        /// <summary>The screens placed again when the panel of the group should come or go (later, not inside a layout).</summary>
        private void RequestGroupLayout()
        {
            if (GroupPanelShown == groupDashboardPlaced || groupRelayoutPending || ScreensRelayoutAsked == null)
                return;
            Control host = player != null && !player.IsDisposed && player.IsHandleCreated ? (Control)player : null;
            if (host == null)
                return;

            groupRelayoutPending = true;
            host.BeginInvoke((Action)(() =>
            {
                groupRelayoutPending = false;
                try
                {
                    if (GroupPanelShown != groupDashboardPlaced && ScreensRelayoutAsked != null)
                        ScreensRelayoutAsked();
                }
                catch (Exception e)
                {
                    log.Error("Could not place the screens of the group again.", e);
                }
            }));
        }

        /// <summary>The frame the video of the analysis shows.</summary>
        internal long AnalysisTimestamp
        {
            get { return player != null && !player.IsDisposed ? player.CurrentTimestamp : 0; }
        }

        /// <summary>The video of the analysis was drawn (another frame): the panel of the group follows.</summary>
        private void GroupFramePainted(long timestamp)
        {
            if (groupDashboard != null && !groupDashboard.IsDisposed && groupDashboard.Visible)
                groupDashboard.Follow(timestamp);
        }

        #region The panels on another screen of the group
        /// <summary>
        /// Another screen of the group shows the panels of the graphics: those of the analysis at the same moment (the video of the analysis
        /// at the time of this frame), as on the screen of the analysis but without what lies on its water.
        /// </summary>
        private void DrawGroupPanels(PlayerScreenUserInterface screen, Graphics g, long timestamp, Size size)
        {
            if (Race == null || size.Width <= 0 || size.Height <= 0 || (Settings != null && !Settings.VideoGraphics))
                return;

            long at = VideoScreenGroup.LeadTimestamp(screen, timestamp);
            lock (overlayLock)
            {
                SmoothingMode smoothing = g.SmoothingMode;
                TextRenderingHint hint = g.TextRenderingHint;
                try
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    DrawPanelsAt(g, at, size);
                }
                catch (Exception e)
                {
                    log.Error("Error while drawing the graphics of the group on another screen.", e);
                }
                finally
                {
                    g.SmoothingMode = smoothing;
                    g.TextRenderingHint = hint;
                }
            }
        }

        /// <summary>The panels of the graphics at a moment of the video of the analysis, on a drawing of this size (logical units).</summary>
        private void DrawPanelsAt(Graphics g, long at, Size size)
        {
            if (Race.IsTraining || TechnicalView)
                DrawTechOverlay(g, at, size);
            if (!Race.IsTraining)
                DrawRaceOverlay(g, at, size, false, false, RectangleF.Empty);
            DrawDistanceChart(g, at, size, 1f);
        }

        /// <summary>The panels on an image of a screen of the group (an image of the report or of the video: as on a screen 1280 wide).</summary>
        private void DrawPanelsOnImage(Graphics g, Size size, long at)
        {
            if (Race == null || size.Width <= 0 || size.Height <= 0)
                return;

            lock (overlayLock)
            {
                GraphicsState state = g.Save();
                try
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    float scale = Math.Max(0.25f, size.Width / ReportImageWidth);
                    Size logical = new Size((int)Math.Round(size.Width / scale), (int)Math.Round(size.Height / scale));
                    g.ScaleTransform(scale, scale);
                    DrawPanelsAt(g, at, logical);
                }
                catch (Exception e)
                {
                    log.Error("Error while drawing the graphics of the group on an image.", e);
                }
                finally
                {
                    g.Restore(state);
                }
            }
        }
        #endregion

        #region The panel of the group
        /// <summary>
        /// The panel of the group (in the place of one more screen, on the images and the video of the group): the start or the turn of
        /// this moment of the video of the analysis, its title and its clock, its times as they pass, the speeds of its segments; outside a
        /// start or a turn the race clock. Drawn in units of a 1280 x 720 panel fitted in the size.
        /// </summary>
        internal void PaintGroupDashboard(Graphics g, Size size, long at)
        {
            using (SolidBrush back = new SolidBrush(SwimTheme.Back))
                g.FillRectangle(back, 0, 0, size.Width, size.Height);
            if (size.Width < 40 || size.Height < 30)
                return;

            lock (overlayLock)
            {
                GraphicsState state = g.Save();
                try
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    // In the shape of its place: 16:9 beside 16:9 videos, taller beside 4:3 ones or in a narrow cell (fewer columns).
                    float aspect = size.Width / (float)size.Height;
                    float width = aspect >= 1.5f ? 1280f : aspect >= 1.1f ? 1100f : 860f;
                    float height = Math.Max(560f, width / aspect);
                    float scale = Math.Min(size.Width / width, size.Height / height);
                    g.TranslateTransform((size.Width - width * scale) / 2f, (size.Height - height * scale) / 2f);
                    g.ScaleTransform(scale, scale);
                    PaintDashboard(g, at, width, height);
                }
                catch (Exception e)
                {
                    log.Error("Error while drawing the panel of the group.", e);
                }
                finally
                {
                    g.Restore(state);
                }
            }
        }

        private void PaintDashboard(Graphics g, long at, float width, float height)
        {
            RectangleF frame = new RectangleF(16, 16, width - 32, height - 32);
            SwimTheme.HudPanel(g, frame, SwimTheme.Accent, 1f, false);
            Font wordmark = SwimTheme.DisplayLight(15f);
            Font title = SwimTheme.Ui(17f, FontStyle.Bold);
            Font clockFont = SwimTheme.DisplayLight(44f);
            Font caption = SwimTheme.Ui(11.5f, FontStyle.Bold);
            Font value = SwimTheme.DisplayLight(34f);
            Font small = SwimTheme.Ui(12f, FontStyle.Regular);

            SwimTheme.CapsIn(g, "SWIM ANALYTICS", wordmark, SwimTheme.AccentPale, new RectangleF(40, 34, 400, 30), StringAlignment.Near, 0.2f);
            if (Race == null || TechPhases == null || Timing == null || !Timing.IsValid)
            {
                using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
                    g.DrawString(SwimLang.T("Η ανάλυση της ομάδας δεν είναι ανοιχτή."), small, dim, 40, 100);
                return;
            }

            SwimTechPhase phase;
            double now;
            List<KeyValuePair<string, string>> items = TechValuesAt(at, out phase, out now);
            string head, clock;
            if (phase != null)
            {
                head = SwimLang.Upper(phase.Title);
                clock = (now < 0 ? "−" : "") + Math.Abs(now).ToString("0.00", CultureInfo.InvariantCulture) + " s";
            }
            else
            {
                // Between the start and the turns: the race clock from the start signal.
                SwimTechPhase start = TechPhases.FirstOrDefault(p => p.Kind == SwimPhaseKind.Start);
                double sinceStart = start != null ? Timing.Seconds(at, start.Origin) : 0;
                head = start != null && sinceStart < 0 ? SwimLang.T("ΠΡΙΝ ΤΗΝ ΕΚΚΙΝΗΣΗ") : SwimLang.T("ΚΟΛΥΜΒΗΣΗ");
                clock = start != null ? sinceStart.ToString("0.00", CultureInfo.InvariantCulture) + " s" : "";
                items = new List<KeyValuePair<string, string>>();
            }

            SizeF cs = g.MeasureString(clock, clockFont);
            SwimTheme.GlowText(g, clock, clockFont, SwimTheme.AccentPale, SwimTheme.Accent, new PointF(frame.Right - cs.Width - 24, 30), 1f);
            SwimTheme.CapsIn(g, head, title, SwimTheme.Amber, new RectangleF(40, 72, Math.Max(40, frame.Width - cs.Width - 80), 34), StringAlignment.Near, 0.16f);
            using (Pen line = new Pen(Color.FromArgb(70, SwimTheme.Accent), 1.5f))
                g.DrawLine(line, 40, 118, frame.Right - 24, 118);

            // The values as they pass, in cells of five columns (four or three in a narrower panel), the speeds of the segments below them
            // when there are some.
            bool chart = phase != null && SegmentSpeedsAt(phase, now) > 0;
            float gridBottom = chart ? Math.Max(228f, height * 0.58f) : height - 40;
            int cols = width >= 1200 ? 5 : width >= 1000 ? 4 : 3;
            float cellW = (frame.Width - 48) / cols, cellH = 92;
            int rows = Math.Max(1, (int)((gridBottom - 136) / cellH));
            int shown = Math.Min(items.Count, cols * rows);
            int skip = items.Count - shown;
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            {
                for (int i = 0; i < shown; i++)
                {
                    KeyValuePair<string, string> item = items[skip + i];
                    float cx = 40 + (i % cols) * cellW;
                    float cy = 136 + (i / cols) * cellH;
                    g.DrawString(item.Key, caption, dim, cx, cy);
                    SwimTheme.GlowText(g, item.Value, value, SwimTheme.Text, SwimTheme.Accent, new PointF(cx - 2, cy + 22), 0.7f);
                }

                if (phase != null && items.Count == 0)
                    g.DrawString(SwimLang.T("Οι χρόνοι μπαίνουν εδώ καθώς περνούν."), small, dim, 40, 140);
                else if (phase == null)
                    g.DrawString(SwimLang.T("Τα δεδομένα της εκκίνησης ή της στροφής φαίνονται εδώ, όταν το βίντεο φτάσει σε αυτήν."), small, dim, 40, 140);
            }

            if (chart)
            {
                // The chart of the speeds of the segments across the panel under the values.
                RectangleF place = new RectangleF(40, gridBottom + 4, frame.Width - 48, frame.Bottom - 24 - (gridBottom + 4));
                DrawSegmentSpeedChart(g, phase, now, new Size((int)width, (int)height), 1.9f, place);
            }
        }

        /// <summary>How many segments of the phase have their speed so far (0–5, 5–10, 10–15).</summary>
        private int SegmentSpeedsAt(SwimTechPhase phase, double now)
        {
            int n = 0;
            if (phase == null || (phase.Kind == SwimPhaseKind.Turn && now < 0))
                return 0;
            foreach (int d in DistanceLineOffsets)
            {
                int offset;
                double time;
                double? speed;
                if (LinePassed(phase, now, d, out offset, out time, out speed) && speed.HasValue)
                    n++;
            }

            return n;
        }
        #endregion

        #region Snapshots of the group
        /// <summary>
        /// The images of all the screens of the group at this moment, joined in the shape of their videos (the panel of the group in its
        /// place), with the graphics on the screen chosen: the snapshot of a group (at most 1280 x 720, as the other images of the report).
        /// Null when it could not be taken (the caller takes the screen of the analysis alone then).
        /// </summary>
        private Bitmap CaptureGroupFrame(long timestamp, bool graphics, bool finalFrame, bool live)
        {
            List<PlayerScreenUserInterface> screens = GroupScreens;
            if (screens.Count < 2)
                return null;

            PlayerScreenUserInterface target = graphics ? GroupGraphicsScreen : null;
            bool panel = graphics && GroupPanelShown;
            List<Bitmap> tiles = new List<Bitmap>();
            try
            {
                foreach (PlayerScreenUserInterface s in screens)
                {
                    Bitmap tile = s == player ? CaptureAnalysisTile(timestamp, graphics && target == player, finalFrame, live) : CaptureOtherTile(s, live);
                    if (tile == null)
                        continue;
                    if (graphics && target == s && s != player)
                    {
                        using (Graphics g = Graphics.FromImage(tile))
                            DrawPanelsOnImage(g, tile.Size, timestamp);
                    }

                    tiles.Add(tile);
                }

                if (tiles.Count < 2)
                    return null;

                if (panel)
                {
                    Size ps = tiles[0].Size;
                    Bitmap dash = new Bitmap(ps.Width, ps.Height, PixelFormat.Format24bppRgb);
                    using (Graphics g = Graphics.FromImage(dash))
                        PaintGroupDashboard(g, ps, timestamp);
                    tiles.Add(dash);
                }

                return SwimTiles.Compose(tiles, 1280, 720, Color.Black);
            }
            catch (Exception e)
            {
                log.Error("Could not take the snapshot of the group.", e);
                return null;
            }
            finally
            {
                foreach (Bitmap b in tiles)
                    b.Dispose();
            }
        }

        /// <summary>The image of the video of the analysis at its size, with the tools on its water and the panels when asked.</summary>
        private Bitmap CaptureAnalysisTile(long timestamp, bool panels, bool finalFrame, bool live)
        {
            Size size = frameServer.VideoReader.Info.ReferenceSize;
            if (size.Width <= 0 || size.Height <= 0)
                return null;

            bool playing = live && player.IsCurrentlyPlaying;
            Bitmap full = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
            try
            {
                suppressExportOverlay = true;
                if (!playing)
                    player.BeforeExportVideo();
                player.PaintFlushedImage(full);
            }
            catch
            {
                full.Dispose();
                throw;
            }
            finally
            {
                if (!playing)
                    player.AfterExportVideo();
                suppressExportOverlay = false;
            }

            using (Graphics g = Graphics.FromImage(full))
            {
                DrawGraphicsOnImage(g, size, timestamp, panels, finalFrame, ReportImageWidth);
                if (!panels)
                {
                    // The lines 5/10/15 lie on the water of this video: on it, wherever the panels are.
                    Size reference = frameServer.VideoReader.Info.ReferenceSize;
                    float kx = size.Width / (float)Math.Max(1, reference.Width), ky = size.Height / (float)Math.Max(1, reference.Height);
                    lock (overlayLock)
                        DrawDistanceLines(g, timestamp, p => new PointF(p.X * kx, p.Y * ky), size, Math.Max(0.25f, size.Width / ReportImageWidth));
                }
            }

            return full;
        }

        #region The video of the group
        /// <summary>
        /// (2.16.0) The video of all the screens of the group together (as the snapshots of the group: each in the shape of its video,
        /// joined, the graphics on the screen chosen or on the panel), of the start or the turn the video of the analysis is in (its part of
        /// the video), else of the part of the package (the race, or the marks of a training). Saved where the analyst says.
        /// </summary>
        public void ExportGroupVideo()
        {
            List<PlayerScreenUserInterface> screens = GroupScreens;
            if (!InGroup || screens.Count < 2 || !HasVideo || Race == null)
            {
                Status(SwimLang.T("Το βίντεο όλων των οθονών θέλει μια ομάδα οθονών με βίντεο (κουμπί «Ομάδα»)."), true);
                return;
            }

            string ffmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpeg))
            {
                Status(SwimLang.T("Δεν βρέθηκε το ffmpeg.exe δίπλα στο πρόγραμμα."), true);
                return;
            }

            long from, to;
            SwimTechPhase phase = CurrentTechPhase;
            VideoSection zone = frameServer.VideoReader.WorkingZone;
            if (phase != null)
            {
                from = Math.Max(zone.Start, Math.Min(phase.WindowStart, zone.End));
                to = Math.Min(zone.End, Math.Max(phase.WindowEnd, from));
            }
            else
            {
                PackageRange(out from, out to);
            }

            string name = Path.GetFileNameWithoutExtension(videoPath ?? "video") + (phase != null ? " - " + SafeName(phase.Title) : "") + " - " + SwimLang.T("όλες οι οθόνες") + ".mp4";
            string path;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = SwimLang.T("Βίντεο όλων των οθονών");
                dialog.Filter = "MP4 (*.mp4)|*.mp4";
                dialog.FileName = name;
                try
                {
                    string folder = Path.GetDirectoryName(videoPath ?? "");
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                        dialog.InitialDirectory = folder;
                }
                catch (Exception)
                {
                    // The folder of the video is not known: the last one of the dialog.
                }

                Form owner = player.FindForm();
                if ((owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog()) != DialogResult.OK)
                    return;
                path = dialog.FileName;
            }

            string error = null;
            bool cancelled = false;
            foreach (PlayerScreenUserInterface s in screens)
                s.BeforeExportVideo();
            suppressExportOverlay = true;
            try
            {
                using (SwimProgressDialog progress = new SwimProgressDialog(SwimLang.T("Βίντεο όλων των οθονών"), worker => error = RenderGroupVideo(screens, path, from, to, worker)))
                {
                    Form owner = player.FindForm();
                    if (owner != null)
                        progress.ShowDialog(owner);
                    else
                        progress.ShowDialog();
                    cancelled = progress.Cancelled;
                    if (progress.Failure != null)
                        error = progress.Failure.Message;
                }
            }
            finally
            {
                suppressExportOverlay = false;
                GiveScreensBack(screens);
            }

            if (cancelled)
                Status(SwimLang.T("Το βίντεο όλων των οθονών σταμάτησε."), false);
            else if (!string.IsNullOrEmpty(error))
                Status(SwimLang.T("Το βίντεο όλων των οθονών δεν έγινε: ") + error, true);
            else
                Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("✓ Το βίντεο όλων των οθονών: {0}"), path), false);
        }

        private static string SafeName(string text)
        {
            string s = text ?? "";
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, ' ');
            return s.Trim();
        }

        /// <summary>(2.16.4) How a video of the group is made: by «Βίντεο όλων των οθονών» or for the package for the coaches.</summary>
        private sealed class GroupVideoJob
        {
            /// <summary>The graphics of the analysis on the images (the package: always, as its video of one screen).</summary>
            public bool Graphics;

            /// <summary>The video whose sound goes with the images (the video of the analysis: the joined video follows its frames); null: none.</summary>
            public string Sound;

            /// <summary>The package: the pictures of its preview at these moments of the video of the analysis (null: none).</summary>
            public SwimPackage Package;
            public List<KeyValuePair<long, string>> Moments;

            /// <summary>The text of the progress: frame {0} of {1}, ~{2} s left.</summary>
            public string Progress;

            /// <summary>What came out: the size of the images, the frames written and their rate; stopped by the analyst.</summary>
            public Size Size;
            public int Frames;
            public double Fps;
            public bool Cancelled;
        }

        /// <summary>
        /// The video of «Βίντεο όλων των οθονών» (RenderGroupFrames, without sound) into path. Returns the error, null when done or stopped.
        /// </summary>
        private string RenderGroupVideo(List<PlayerScreenUserInterface> screens, string path, long from, long to, BackgroundWorker worker)
        {
            GroupVideoJob job = new GroupVideoJob
            {
                Graphics = Settings == null || Settings.VideoGraphics,
                Progress = SwimLang.T("Καρέ {0}/{1} · απομένουν ~{2} δλ.")
            };
            string temp = path + ".part";
            string error = RenderGroupFrames(screens, temp, from, to, worker, job);
            if (job.Cancelled || error != null)
            {
                TryDelete(temp);
                return error;
            }

            File.Copy(temp, path, true);
            TryDelete(temp);
            return null;
        }

        /// <summary>
        /// The images of a video of the group into temp (an MP4): each frame of the part of the video of the analysis with the frame of every
        /// other screen of the group at the same moment (their time origins), joined in the shape of their videos as the snapshots of the
        /// group (at most 1920 x 1080: nothing cut, no black between them), the graphics drawn on the thread of the screens, given to ffmpeg
        /// (H.264) with the sound of job.Sound from the first frame. The pictures of the preview of a package at its moments. Returns the error,
        /// null when done (job.Frames) or stopped (job.Cancelled).
        /// </summary>
        private string RenderGroupFrames(List<PlayerScreenUserInterface> screens, string temp, long from, long to, BackgroundWorker worker, GroupVideoJob job)
        {
            string ffmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            VideoReader reader = frameServer.VideoReader;
            double tsPerSecond = reader.Info.AverageTimeStampsPerSeconds;
            double tsPerFrame = Math.Max(1.0, reader.Info.AverageTimeStampsPerFrame);
            double fps = tsPerSecond > 0 ? tsPerSecond / tsPerFrame : 1000.0 / Math.Max(1e-6, Timing.FrameIntervalMilliseconds);
            job.Fps = fps;
            List<PlayerScreenUserInterface> others = screens.Where(s => s != player).ToList();
            PlayerScreenUserInterface target = GroupGraphicsScreen;
            bool panel = GroupPanelShown;
            bool graphics = job.Graphics;

            // The places of the images in the order of the screens (as in the grid and the snapshots of the group), the panel last in the
            // shape of the video of the analysis (it is drawn at its size). The images are read as the video of the analysis, then the
            // others: each goes to its place.
            List<PlayerScreenUserInterface> order = screens.Contains(player) ? screens : new[] { player }.Concat(others).ToList();
            List<double> aspects = order.Select(s => AspectOf(s == player ? reader.Info.ReferenceSize : s.SwimFrameServer.VideoReader.Info.ReferenceSize)).ToList();
            if (panel)
                aspects.Add(AspectOf(reader.Info.ReferenceSize));
            List<int> slots = new List<int> { order.IndexOf(player) };
            slots.AddRange(others.Select(s => order.IndexOf(s)));
            if (panel)
                slots.Add(order.Count);
            double lw, lh;
            List<RectangleF> places = SwimTiles.Layout(aspects, 1920, 1080, out lw, out lh);
            int width = Math.Max(2, (int)Math.Floor(lw) & ~1), height = Math.Max(2, (int)Math.Floor(lh) & ~1);
            double kx = width / Math.Max(1.0, lw), ky = height / Math.Max(1.0, lh);
            List<Rectangle> rects = places.Select(p => Rectangle.FromLTRB((int)Math.Round(p.Left * kx), (int)Math.Round(p.Top * ky), (int)Math.Round(p.Right * kx), (int)Math.Round(p.Bottom * ky))).ToList();
            job.Size = new Size(width, height);

            int total = (int)Math.Max(1, Math.Round((to - from) / tsPerFrame)) + 1;
            byte[] buffer = new byte[width * height * 3];
            List<Bitmap> flushed = new List<Bitmap>();
            for (int i = 0; i < screens.Count + (panel ? 1 : 0); i++)
                flushed.Add(null);
            DateTime began = DateTime.Now;
            int moment = 0;
            using (SwimVideoEncoder encoder = new SwimVideoEncoder { MaxRateK = GroupVideoRate(width, height) })
            using (Bitmap joined = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            {
                try
                {
                    reader.BeforeFrameEnumeration();
                    bool hasMore = reader.MoveTo(reader.Current != null ? reader.Current.Timestamp : from, from);
                    List<bool> otherMore = new List<bool>();
                    foreach (PlayerScreenUserInterface s in others)
                    {
                        VideoReader r = s.SwimFrameServer.VideoReader;
                        r.BeforeFrameEnumeration();
                        long at = s.SwimTimestampFromOrigin(player.SwimTimeFromOrigin(from));
                        otherMore.Add(r.MoveTo(r.Current != null ? r.Current.Timestamp : at, Math.Max(r.WorkingZone.Start, Math.Min(at, r.WorkingZone.End))));
                    }

                    bool started = false;
                    int count = 0;
                    while (true)
                    {
                        if (worker.CancellationPending)
                        {
                            encoder.Abort();
                            job.Cancelled = true;
                            return null;
                        }

                        VideoFrame frame = reader.Current;
                        if (frame == null || frame.Image == null || frame.Timestamp > to)
                            break;

                        if (!started)
                        {
                            // The sound of the video of the analysis from its first frame read, as long as the images (padded, cut at their end).
                            double soundFrom = (frame.Timestamp - reader.Info.FirstTimeStamp) / Math.Max(1e-9, tsPerSecond);
                            double soundSeconds = (to - frame.Timestamp) / Math.Max(1e-9, tsPerSecond) + 2.0 / fps;
                            string startError;
                            if (!encoder.Start(ffmpeg, temp, width, height, fps, job.Sound, soundFrom, soundSeconds, out startError))
                                return startError;
                            started = true;
                        }

                        // The video of the analysis, then each other screen at the same moment (its frame at or just after it).
                        flushed[0] = Flushed(player, frame, flushed[0]);
                        long time = player.SwimTimeFromOrigin(frame.Timestamp);
                        for (int i = 0; i < others.Count; i++)
                        {
                            PlayerScreenUserInterface s = others[i];
                            VideoReader r = s.SwimFrameServer.VideoReader;
                            long at = s.SwimTimestampFromOrigin(time);
                            double half = Math.Max(1.0, r.Info.AverageTimeStampsPerFrame / 2);
                            while (otherMore[i] && r.Current != null && r.Current.Timestamp + half < at)
                                otherMore[i] = r.MoveNext(0, true);
                            VideoFrame of = r.Current;
                            if (of != null && of.Image != null && s.SwimInVideo(at))
                                flushed[i + 1] = Flushed(s, of, flushed[i + 1]);
                            else if (flushed[i + 1] != null)
                            {
                                using (Graphics g = Graphics.FromImage(flushed[i + 1]))
                                    g.Clear(Color.Black);
                            }
                        }

                        // The graphics, on the thread of the screens (the analysis is read there).
                        long ts = frame.Timestamp;
                        Action draw = () => DrawGroupVideoGraphics(flushed, others, target, panel, graphics, ts, frame.Image);
                        if (player.InvokeRequired)
                            player.Invoke(draw);
                        else
                            draw();

                        using (Graphics g = Graphics.FromImage(joined))
                        {
                            g.Clear(Color.Black);
                            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            for (int i = 0; i < flushed.Count && i < slots.Count; i++)
                            {
                                if (flushed[i] != null && slots[i] >= 0 && slots[i] < rects.Count)
                                    g.DrawImage(flushed[i], rects[slots[i]]);
                            }
                        }

                        // The pictures of the preview of the package (the start, the walls… of the video of the analysis): the joined image.
                        if (job.Package != null && job.Moments != null && moment < job.Moments.Count && ts >= job.Moments[moment].Key)
                        {
                            string label = job.Moments[moment].Value;
                            while (moment < job.Moments.Count && ts >= job.Moments[moment].Key)
                                moment++;
                            SwimPackageThumb thumb = new SwimPackageThumb { Label = label, Timestamp = ts, Image = ThumbOf(joined) };
                            job.Package.Thumbs.Add(thumb);
                            worker.ReportProgress(Percent(count, total), thumb);
                        }

                        CopyBgr(joined, buffer);
                        if (!encoder.Write(buffer))
                        {
                            // ffmpeg stopped taking images: a video it finished well is kept (the images to here), else its error.
                            string stopped;
                            if (!encoder.Finish(60000, out stopped))
                                return string.Format(SwimLang.T("Το ffmpeg σταμάτησε: {0}"), string.IsNullOrEmpty(stopped) ? encoder.Errors : stopped);
                            log.WarnFormat("Swim group video: ffmpeg ended after {0} of {1} frames.", count, total);
                            job.Frames = count;
                            return null;
                        }

                        count++;
                        if (count % 5 == 0)
                        {
                            double seconds = (DateTime.Now - began).TotalSeconds;
                            double left = seconds * (total - count) / Math.Max(1, count);
                            worker.ReportProgress(Percent(count, total), string.Format(CultureInfo.InvariantCulture,
                                job.Progress ?? SwimLang.T("Καρέ {0}/{1} · απομένουν ~{2} δλ."), count, total, Math.Max(0, (int)Math.Round(left))));
                        }

                        if (!hasMore)
                            break;
                        hasMore = reader.MoveNext(0, true);
                    }

                    if (count == 0)
                    {
                        encoder.Abort();
                        return SwimLang.T("Δεν διαβάστηκε κανένα καρέ του βίντεο.");
                    }

                    worker.ReportProgress(99, SwimLang.T("Το βίντεο κλείνει…"));
                    string finishError;
                    if (!encoder.Finish(300000, out finishError))
                        return finishError;
                    job.Frames = count;
                    log.InfoFormat("Swim group video: {0} frames of {1} screens in {2:0.0} s ({3}x{4}, sound {5}, {6} kbit/s at most).", count, screens.Count,
                        (DateTime.Now - began).TotalSeconds, width, height, job.Sound != null, encoder.MaxRateK);
                    return null;
                }
                catch (Exception e)
                {
                    log.Error("The video of the group failed.", e);
                    encoder.Abort();
                    return e.Message;
                }
                finally
                {
                    foreach (Bitmap b in flushed)
                    {
                        if (b != null)
                            b.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// (2.16.4) The bitrate cap of a video of the group (kbit/s): the one of the package for 1280 x 720, more for a larger joined image so that
        /// every screen keeps its detail (1920 x 1080: 5000).
        /// </summary>
        private static int GroupVideoRate(int width, int height)
        {
            double k = width * (double)height / (1280.0 * 720.0);
            return (int)Math.Max(SwimVideoEncoder.DefaultMaxRateK, Math.Min(5000, Math.Round(SwimVideoEncoder.DefaultMaxRateK * k)));
        }

        /// <summary>(2.16.4) A picture of the preview of the package (480 wide at most) from a joined image.</summary>
        private static Bitmap ThumbOf(Bitmap image)
        {
            int width = Math.Min(480, image.Width);
            int height = Math.Max(2, (int)Math.Round(width * (double)image.Height / Math.Max(1, image.Width)));
            Bitmap thumb = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(image, new Rectangle(0, 0, width, height));
            }

            return thumb;
        }

        /// <summary>(2.16.4) The screens of a group given back their video after a video of the group (the export or the package).</summary>
        private static void GiveScreensBack(List<PlayerScreenUserInterface> screens)
        {
            foreach (PlayerScreenUserInterface s in screens)
            {
                try
                {
                    if (!s.IsDisposed && s.SwimFrameServer != null && s.SwimFrameServer.Loaded)
                    {
                        s.SwimFrameServer.VideoReader.AfterFrameEnumeration();
                        s.AfterExportVideo();
                    }
                }
                catch (Exception e)
                {
                    log.Error("Could not give a screen back its video after the video of the group.", e);
                }
            }
        }

        /// <summary>The graphics on the images of the group's video: the tools on the video of the analysis, the panels where chosen.</summary>
        private void DrawGroupVideoGraphics(List<Bitmap> images, List<PlayerScreenUserInterface> others, PlayerScreenUserInterface target, bool panel, bool graphics, long ts, Bitmap frame)
        {
            if (images[0] != null)
            {
                Size size = images[0].Size;
                using (Graphics g = Graphics.FromImage(images[0]))
                {
                    bool panels = graphics && target == player;
                    DrawGraphicsOnImage(g, size, ts, panels, false, lastRenderingSize.Width > 0 ? lastRenderingSize.Width : ReportImageWidth, true, true, frame);
                    if (!panels)
                    {
                        Size reference = frameServer.VideoReader.Info.ReferenceSize;
                        float kx = size.Width / (float)Math.Max(1, reference.Width), ky = size.Height / (float)Math.Max(1, reference.Height);
                        lock (overlayLock)
                            DrawDistanceLines(g, ts, p => new PointF(p.X * kx, p.Y * ky), size, Math.Max(0.25f, size.Width / ReportImageWidth));
                    }
                }
            }

            if (graphics && target != null && target != player)
            {
                int i = others.IndexOf(target);
                if (i >= 0 && images[i + 1] != null)
                {
                    using (Graphics g = Graphics.FromImage(images[i + 1]))
                        DrawPanelsOnImage(g, images[i + 1].Size, ts);
                }
            }

            if (panel)
            {
                int last = images.Count - 1;
                Size size = images[0] != null ? images[0].Size : new Size(1280, 720);
                if (images[last] == null || images[last].Size != size)
                {
                    if (images[last] != null)
                        images[last].Dispose();
                    images[last] = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
                }

                using (Graphics g = Graphics.FromImage(images[last]))
                    PaintGroupDashboard(g, size, ts);
            }
        }

        /// <summary>A frame of a screen with its drawings (no graphics of the analysis: they are drawn after), in a bitmap kept from frame to frame.</summary>
        private static Bitmap Flushed(PlayerScreenUserInterface screen, VideoFrame frame, Bitmap keep)
        {
            if (keep == null || keep.Width != frame.Image.Width || keep.Height != frame.Image.Height)
            {
                if (keep != null)
                    keep.Dispose();
                keep = new Bitmap(frame.Image.Width, frame.Image.Height, PixelFormat.Format24bppRgb);
            }

            screen.GetFlushedImage(frame, keep);
            return keep;
        }

        private static double AspectOf(Size size)
        {
            return size.Width > 0 && size.Height > 0 ? (double)size.Width / size.Height : 16.0 / 9;
        }

        /// <summary>The rows of a 24-bit image (BGR) without their padding.</summary>
        private static void CopyBgr(Bitmap image, byte[] buffer)
        {
            BitmapData data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int row = image.Width * 3;
                for (int y = 0; y < image.Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), buffer, y * row, row);
            }
            finally
            {
                image.UnlockBits(data);
            }
        }

        private static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch (Exception)
            {
                // A part left behind is harmless.
            }
        }
        #endregion

        /// <summary>The image another screen of the group shows now (with its own drawings), at the size of its video.</summary>
        private static Bitmap CaptureOtherTile(PlayerScreenUserInterface screen, bool live)
        {
            Size size = screen.SwimFrameServer.VideoReader.Info.ReferenceSize;
            if (size.Width <= 0 || size.Height <= 0)
                return null;

            bool playing = live && screen.IsCurrentlyPlaying;
            Bitmap image = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
            try
            {
                if (!playing)
                    screen.BeforeExportVideo();
                screen.PaintFlushedImage(image);
                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
            finally
            {
                if (!playing)
                    screen.AfterExportVideo();
            }
        }
        #endregion
    }

    /// <summary>
    /// (2.16.0) Images joined in rows, each in the shape of its image, all of them the same height (the rows in the middle, the shorter
    /// ones with black at their sides): nothing cut and no black between them. Of the ways to put them in one, two or three rows (the
    /// extra ones in the first rows) the one whose smallest image is the biggest in the size allowed (as the grid of the screens).
    /// </summary>
    public static class SwimTiles
    {
        /// <summary>The place of each image in the joined image (its size: Width x Height).</summary>
        public static List<RectangleF> Layout(IList<double> aspects, double maxWidth, double maxHeight, out double width, out double height)
        {
            width = height = 0;
            List<RectangleF> best = null;
            double bestMin = -1;
            int count = aspects != null ? aspects.Count : 0;
            if (count == 0 || maxWidth <= 0 || maxHeight <= 0)
                return new List<RectangleF>();

            for (int rowCount = 1; rowCount <= Math.Min(count, 3); rowCount++)
            {
                int[] rows = new int[rowCount];
                for (int r = 0; r < rowCount; r++)
                    rows[r] = count / rowCount + (r < count % rowCount ? 1 : 0);

                // The height of the images: the widest row fits the width, the rows fit the height.
                double h = maxHeight / rowCount;
                int index = 0;
                List<double> sums = new List<double>();
                for (int r = 0; r < rowCount; r++)
                {
                    double sum = 0;
                    for (int c = 0; c < rows[r]; c++)
                        sum += Math.Max(0.1, aspects[index++]);
                    sums.Add(sum);
                    h = Math.Min(h, maxWidth / sum);
                }

                double w = sums.Max() * h, smallest = double.MaxValue;
                List<RectangleF> places = new List<RectangleF>();
                index = 0;
                for (int r = 0; r < rowCount; r++)
                {
                    double x = (w - sums[r] * h) / 2;
                    for (int c = 0; c < rows[r]; c++)
                    {
                        double tw = h * Math.Max(0.1, aspects[index++]);
                        places.Add(new RectangleF((float)x, (float)(r * h), (float)tw, (float)h));
                        smallest = Math.Min(smallest, tw * h);
                        x += tw;
                    }
                }

                if (best == null || smallest > bestMin * 1.02)
                {
                    best = places;
                    bestMin = smallest;
                    width = w;
                    height = rowCount * h;
                }
            }

            return best;
        }

        /// <summary>The images joined (as Layout), at most maxWidth x maxHeight; the edges rounded to whole pixels without gaps.</summary>
        public static Bitmap Compose(IList<Bitmap> images, int maxWidth, int maxHeight, Color back)
        {
            List<double> aspects = images.Select(b => b.Height > 0 ? (double)b.Width / b.Height : 16.0 / 9).ToList();
            double width, height;
            List<RectangleF> places = Layout(aspects, maxWidth, maxHeight, out width, out height);
            int w = Math.Max(2, (int)Math.Round(width)), h = Math.Max(2, (int)Math.Round(height));
            Bitmap result = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(back);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                for (int i = 0; i < images.Count && i < places.Count; i++)
                {
                    RectangleF p = places[i];
                    int left = (int)Math.Round(p.Left), top = (int)Math.Round(p.Top);
                    int right = (int)Math.Round(p.Right), bottom = (int)Math.Round(p.Bottom);
                    using (ImageAttributes clamp = new ImageAttributes())
                    {
                        clamp.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(images[i], new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)), 0, 0, images[i].Width, images[i].Height, GraphicsUnit.Pixel, clamp);
                    }
                }
            }

            return result;
        }
    }
}
