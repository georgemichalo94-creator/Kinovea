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
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Technical view of the race on the video, as the swimming reels: athlete label that follows the swimmer, race time
    /// with the strokes, tempo, distance per cycle and speed, phase, badges at the start, the breakouts and the checkpoints,
    /// chart of the cycles of the length and a summary card after the finish. On the screen and in the video exports.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Technical view (reel)
        private SwimReel reelCache;
        private int reelVersion = -1;
        private string reelRaceId;
        private bool pickForTrack;

        /// <summary>The technical view is drawn on the video of a race (instead of the race board and the velocity chart).</summary>
        public bool ReelShown
        {
            get { return Settings != null && Settings.ReelOverlay && Race != null && !Race.IsTraining; }
        }

        /// <summary>
        /// The technical view is drawn on an image of this size (else the race board and the velocity chart are, as before the
        /// start signal or on a very small image).
        /// </summary>
        private bool ReelReady(Size size)
        {
            return ReelShown && SwimReelPainter.CanPaint(size, Reel);
        }

        /// <summary>The next two clicks choose the swimmer that the athlete label follows (not the automatic strokes).</summary>
        public bool PickingTrack
        {
            get { return pickForTrack && PickingStrokes; }
        }

        public void SetReel(bool on)
        {
            Settings.ReelOverlay = on;
            Settings.Save();
            if (on)
            {
                bool tracked = Race != null && Race.Tracks.Count > 0;
                Status(tracked
                    ? SwimLang.T("Τεχνική ανάλυση στο βίντεο: ανοιχτή.")
                    : SwimLang.T("Τεχνική ανάλυση στο βίντεο: ανοιχτή. Για την ετικέτα πάνω στον αθλητή: «Ακολούθηση αθλητή» και κλικ στο κεφάλι και στα πόδια του."), false);
            }
            else
            {
                Status(SwimLang.T("Τεχνική ανάλυση στο βίντεο: κλειστή."), false);
            }

            InvalidatePlayer();
            if (form != null && !form.IsDisposed)
                form.RefreshReel();
        }

        /// <summary>The graphics of the analysis on the video on or off (key V, the button «Γραφικά» of the bar; since 2.9.0).</summary>
        public void SetGraphics(bool on)
        {
            if (Settings == null)
                return;

            Settings.VideoGraphics = on;
            Settings.Save();
            Status(on ? SwimLang.T("Γραφικά στο βίντεο: ναι (V: όχι).") : SwimLang.T("Γραφικά στο βίντεο: όχι, ούτε στις εξαγωγές του βίντεο (V: ναι)."), false);
            InvalidatePlayer();
            if (form != null && !form.IsDisposed)
                form.RefreshReel();
        }

        public void SetReelSummary(bool on)
        {
            Settings.ReelSummary = on;
            Settings.Save();
            InvalidatePlayer();
        }

        private SwimReel Reel
        {
            get
            {
                // A reading in the background reads without it (it is made from the race of the panel).
                if (quietThread != null || Race == null || Computed == null)
                    return null;

                if (reelCache == null || reelVersion != DataVersion || reelRaceId != Race.Id)
                {
                    reelCache = SwimReel.Build(Race, Computed);
                    reelVersion = DataVersion;
                    reelRaceId = Race.Id;
                }

                return reelCache;
            }
        }

        /// <summary>Parts of the race where the swimmer is followed, for the panel ("6.4–26.7 s"), empty if none.</summary>
        public string TrackDescription
        {
            get
            {
                if (Race == null || Race.Tracks.Count == 0 || !Race.StartTimestamp.HasValue || !Timing.IsValid)
                    return "";

                return string.Join(", ", Race.Tracks.OrderBy(t => t.Start).Select(t => string.Format(CultureInfo.InvariantCulture, "{0:0.0}–{1:0.0} s",
                    Timing.Seconds(t.Start, Race.StartTimestamp.Value), Timing.Seconds(t.End, Race.StartTimestamp.Value))));
            }
        }

        #region Following the swimmer
        /// <summary>
        /// The next two clicks are the head and the feet of the swimmer: the length is read from this frame up to its wall and the
        /// place of the swimmer is kept for the label of the technical view. The marks of the race do not change.
        /// </summary>
        public void BeginTrackSwimmer()
        {
            if (PickingStrokes)
            {
                bool wasTrack = PickingTrack;
                CancelPick();
                if (wasTrack)
                    return;
            }

            if (AutoStrokesRunning)
                return;

            if (!HasVideo || Race == null)
            {
                Status(SwimLang.T("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής."), true);
                return;
            }

            if (Race.IsTraining || !Race.StartTimestamp.HasValue)
            {
                Status(SwimLang.T("Η ακολούθηση του αθλητή είναι για την ανάλυση αγώνα, μετά το σήμα εκκίνησης (G)."), true);
                return;
            }

            pendingMeasure = SwimPickTarget.None;
            calibrationClicks.Clear();
            angleClicks.Clear();
            pickForRace = false;
            pickForQuiet = false;
            pickForTrack = true;
            SetPick(SwimPickTarget.StrokeHead);
            Status(SwimLang.T("Ακολούθηση: σε ένα καρέ μετά την έξοδο από το νερό, κλικ στο κεφάλι του αθλητή και μετά στα πόδια του (Esc: ακύρωση)."), false);
            InvalidatePlayer();
            if (form != null && !form.IsDisposed)
                form.RefreshReel();
        }

        /// <summary>Reads the length from the clicked frame to its wall and keeps the place of the swimmer.</summary>
        private void TrackSwimmer(PointF head, PointF feet, long from)
        {
            RefreshTiming();
            if (!Timing.IsValid || Race == null)
            {
                Status(SwimLang.T("Δεν είναι γνωστός ο ρυθμός καρέ του βίντεο: η ακολούθηση δεν μπορεί να γίνει."), true);
                return;
            }

            long target = TrackEnd(from);
            if (target <= from)
            {
                Status(SwimLang.T("Δεν υπάρχει βίντεο μετά από αυτό το καρέ."), true);
                return;
            }

            StrokeSession s = CreateSession(head, feet, from);
            try
            {
                if (!ReadStrokeFrames(s, target))
                    return;

                if (s.Timestamps.Count < 10)
                {
                    Status(SwimLang.T("Πολύ λίγα καρέ για την ακολούθηση του αθλητή."), true);
                    return;
                }

                SwimStrokeResult result = s.Detector.Finish(0, s.Timestamps.Count - 1);
                StoreTrack(s, result);
                Changed(false);
                Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Ακολούθηση: ο αθλητής βρέθηκε μέχρι τα {0:0.00} s{1}. Η ετικέτα του φαίνεται στην τεχνική ανάλυση."),
                    Timing.Seconds(s.LastRead, Race.StartTimestamp.Value), result.Ropes ? "" : SwimLang.T(" (χωρίς σχοινιά διαδρομής)")), false);

                // The checkpoints of the length from the marks of the ropes, for the check.
                if (Settings.ProposeSplitsAfterTrack)
                    ProposeSplits(s, result);
            }
            finally
            {
                s.Dispose();
                InvalidatePlayer();
                if (form != null && !form.IsDisposed)
                    form.RefreshReel();
            }
        }

        /// <summary>
        /// End of the reading for the swimmer, within the working zone: a little after the touch at the wall of the length when it
        /// is marked; else the rest of the length at a slow speed (0.9 m/s) from where the checkpoints put the swimmer, at most
        /// a minute (the label stops at the wall once it is marked).
        /// </summary>
        private long TrackEnd(long from)
        {
            long end = frameServer.VideoReader.WorkingZone.End;
            int wall = NextWallDistance(from);
            SwimMark split = Race.GetSplit(wall);
            if (split != null && split.Timestamp > from)
                return Math.Min(end, split.Timestamp + RealSpan(0.6));

            SwimReel reel = Reel;
            double t = Timing.Seconds(from, Race.StartTimestamp.Value);
            double at = reel != null && reel.HasModel ? reel.DistanceAt(t) : wall - Race.Pool;
            double seconds = Math.Max(0, wall - at) * Race.K / 0.9 + 2.0;
            return Math.Min(end, from + RealSpan(Math.Min(60, seconds)));
        }

        /// <summary>Timestamps of a time of the race (real seconds, also in a high speed video).</summary>
        private long RealSpan(double seconds)
        {
            double hsf = Timing.HighSpeedFactor <= 0 ? 1.0 : Timing.HighSpeedFactor;
            return (long)Math.Round(seconds * 1000.0 * hsf / Timing.FrameIntervalMilliseconds * Timing.AverageTimestampsPerFrame);
        }

        /// <summary>
        /// The place of the swimmer of a reading (automatic strokes or follow) in reference image pixels. It replaces what the
        /// parts kept before have in its time, and stops where a part of a later length begins. bodyIndex: the frame of the reading
        /// where the swimmer's head and feet were given (the click); bodyLanes: the swimmer's length in widths of his lane when known
        /// (the race reading: from the click, for every length).
        /// </summary>
        private void StoreTrack(StrokeSession s, SwimStrokeResult result, int bodyIndex = 0, double bodyLanes = double.NaN)
        {
            if (Race == null || result == null || result.Path == null || result.Lanes == null)
                return;

            int n = Math.Min(s.Timestamps.Count, Math.Min(result.Path.Length, result.Lanes.Count));
            if (n < 10)
                return;

            Size reference = frameServer.VideoReader.Info.ReferenceSize;
            double sx = reference.Width / (double)Math.Max(1, s.Width);
            double sy = reference.Height / (double)Math.Max(1, s.Height);

            // The swimmer's length in widths of the lane where he was clicked (both follow the zoom and the perspective).
            double body = s.Detector != null ? s.Detector.SwimmerLength : double.NaN;
            int bi = Math.Max(0, Math.Min(n - 1, bodyIndex));
            SwimStrokeLane bodyLane = result.Lanes[bi];
            double laneThere = Math.Abs(bodyLane.Y2(result.Path[bi]) - bodyLane.Y1(result.Path[bi]));
            SwimTrack track = new SwimTrack
            {
                Start = s.Timestamps[0],
                Step = (s.Timestamps[n - 1] - s.Timestamps[0]) / (double)Math.Max(1, n - 1),
                Body = !double.IsNaN(bodyLanes) && bodyLanes > 0 ? (int)Math.Round(1000 * bodyLanes)
                    : (double.IsNaN(body) || body <= 0 || double.IsNaN(laneThere) || laneThere < 5 ? 0 : (int)Math.Round(1000 * body / laneThere))
            };

            // Since 2.8.0 also the camera's displacement along the lane at the swimmer (where the swimmer of the frame before is in
            // this frame, minus where he was), added up, and the slope of the lane: the place of the pool where a reference is. A race
            // reading keeps the displacement when the frames of a length go (PathShifts).
            bool shifts = result.HasPathShifts(n);
            double pan = 0;
            for (int i = 0; i < n; i++)
            {
                double x = result.Path[i];
                SwimStrokeLane lane = result.Lanes[i];
                double y1 = lane.Y1(x), y2 = lane.Y2(x);
                track.X.Add((int)Math.Round(x * sx));
                track.Y.Add((int)Math.Round(0.5 * (y1 + y2) * sy));
                track.Top.Add((int)Math.Round(Math.Min(y1, y2) * sy));
                track.Bottom.Add((int)Math.Round(Math.Max(y1, y2) * sy));
                if (shifts && i > 0)
                    pan += result.PathShift(i) * sx;

                track.Pan.Add((int)Math.Round(pan));
                double slope = 0.5 * (lane.A1 + lane.A2) * sy / sx;
                track.Slope.Add((int)Math.Round(Math.Max(-5, Math.Min(5, double.IsNaN(slope) ? 0 : slope)) * 1000));
            }

            if (!shifts)
                track.Pan.Clear();

            // Where the head is ahead of the place followed (the line of a reference is at its head): kept by a race reading, else from
            // the motion of the frames.
            double lead = result.HeadLead;
            if (double.IsNaN(lead) && s.Detector != null)
                lead = SwimSplitDetector.HeadLead(result, s.Detector.Direction, s.Fps);
            track.Lead = double.IsNaN(lead) || lead < 0 ? 0 : Math.Max(1, (int)Math.Round(1000 * lead));

            // Not into the length of a part followed before (a reading can go past an unmarked wall).
            SwimReel reel = Reel;
            Func<long, int> lap = timestamp => reel != null && Race.StartTimestamp.HasValue
                ? reel.LapAt(Timing.Seconds(timestamp, Race.StartTimestamp.Value)) : 0;
            int own = lap(track.Start);
            foreach (SwimTrack old in Race.Tracks.Where(t => t != null && t.Start > track.Start && t.Start <= track.End).OrderBy(t => t.Start))
            {
                if (lap(old.Start) <= own)
                    continue;

                SwimTrack cut = track.Slice(track.Start, old.Start - 1);
                if (cut != null)
                    track = cut;
                break;
            }

            long start = track.Start, end = track.End;
            List<SwimTrack> kept = new List<SwimTrack>();
            foreach (SwimTrack old in Race.Tracks.Where(t => t != null && t.X.Count > 0))
            {
                if (old.End < start || old.Start > end)
                {
                    kept.Add(old);
                    continue;
                }

                SwimTrack before = old.Slice(old.Start, start - 1);
                SwimTrack after = old.Slice(end + 1, old.End);
                if (before != null)
                    kept.Add(before);
                if (after != null)
                    kept.Add(after);
            }

            kept.Add(track);
            kept.Sort((a, b) => a.Start.CompareTo(b.Start));
            Race.Tracks.Clear();
            Race.Tracks.AddRange(kept);
        }

        /// <summary>Delete the place of the swimmer kept for the label (the marks do not change).</summary>
        public void DeleteTrack()
        {
            if (Race == null || Race.Tracks.Count == 0)
                return;

            Race.Tracks.Clear();
            Changed(false);
            InvalidatePlayer();
            Status(SwimLang.T("Διαγράφηκε η ακολούθηση του αθλητή (η ετικέτα δεν φαίνεται πια)."), false);
            if (form != null && !form.IsDisposed)
                form.RefreshReel();
        }
        #endregion

        /// <summary>
        /// The technical view at this frame. size: the image (screen pixels, or logical units of an export);
        /// fromImage: point of the video image (reference pixels) → point in these units, null without the athlete label.
        /// </summary>
        private void DrawReel(Graphics g, long timestamp, Size size, Func<PointF, PointF> fromImage)
        {
            SwimReel reel = Reel;
            if (reel == null || Race == null || !Race.StartTimestamp.HasValue || !Timing.IsValid)
                return;

            SwimReelScene scene = new SwimReelScene
            {
                Race = Race,
                Computed = Computed,
                Reel = reel,
                Timing = Timing,
                Timestamp = timestamp,
                Time = Timing.Seconds(timestamp, Race.StartTimestamp.Value),
                FromImage = fromImage,
                Summary = Settings.ReelSummary,
                Final = Settings.ShowFinalsBoard && PickTarget == SwimPickTarget.None ? FinalForRace() : null,
                BoardName = BoardName(Race.Athlete),
                Delta = Settings.ShowDeltaChart
            };
            SwimReelPainter.Paint(g, size, scene);
        }
        #endregion

        #region Graphics of the report
        /// <summary>Size of the graphics of the report: an image over the cells of a Google Sheet has at most 1 million pixels.</summary>
        private static readonly Size ReportGraphicSize = new Size(1280, 720);

        /// <summary>The graphics of the report are being uploaded before a PDF.</summary>
        private bool reportGraphicsUploading;

        /// <summary>
        /// The graphics of the video for the PDF report (PNG): "summary", the summary card of the race; "versus", the race against the
        /// final of the event (or the reference) after the touch. Empty while the race has no finish.
        /// </summary>
        private Dictionary<string, byte[]> RenderReportGraphics()
        {
            Dictionary<string, byte[]> images = new Dictionary<string, byte[]>();
            SwimReel reel = Reel;
            if (Race == null || Race.IsTraining || Computed == null || reel == null || !reel.Finish.HasValue || !Race.StartTimestamp.HasValue || !Timing.IsValid)
                return images;

            SwimFinal final = FinalForRace();
            try
            {
                SwimReelScene scene = new SwimReelScene
                {
                    Race = Race,
                    Computed = Computed,
                    Reel = reel,
                    Timing = Timing,
                    Time = reel.Finish.Value + 10,
                    Summary = true,
                    SummaryOnly = true,
                    Final = final,
                    BoardName = BoardName(Race.Athlete),
                    Delta = true
                };
                images["summary"] = RenderGraphic(g => SwimReelPainter.Paint(g, ReportGraphicSize, scene));
            }
            catch (Exception e)
            {
                log.Error("Could not draw the summary card of the report.", e);
            }

            if (final != null || Computed.Reference != null)
            {
                try
                {
                    images["versus"] = RenderGraphic(g => DrawVersus(g, final));
                }
                catch (Exception e)
                {
                    log.Error("Could not draw the comparison of the report.", e);
                }
            }

            return images;
        }

        private static byte[] RenderGraphic(Action<Graphics> draw)
        {
            using (Bitmap image = new Bitmap(ReportGraphicSize.Width, ReportGraphicSize.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    using (SolidBrush back = new SolidBrush(SwimTheme.Back))
                        g.FillRectangle(back, 0, 0, image.Width, image.Height);
                    draw(g);
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    image.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }

        /// <summary>
        /// The race against the final after the touch, with the graphics of the video: the board of the final at the finish, the race
        /// board (final time, Δ), the Δ at each checkpoint and the speed of each part. Without a final: against the reference.
        /// </summary>
        private void DrawVersus(Graphics g, SwimFinal final)
        {
            const float scale = 1.6f;
            float W = ReportGraphicSize.Width / scale, H = ReportGraphicSize.Height / scale;
            g.ScaleTransform(scale, scale);
            using (LinearGradientBrush back = new LinearGradientBrush(new RectangleF(0, 0, W, H), SwimTheme.Back, SwimTheme.Surface, LinearGradientMode.Vertical))
                g.FillRectangle(back, 0, 0, W, H);

            SwimRow finish = Computed.Rows.FirstOrDefault(r => r.D >= Race.Distance && r.T.HasValue);
            if (finish == null)
                return;

            List<SwimRow> done = Computed.Rows.Where(r => r.T.HasValue).ToList();
            string title = final != null
                ? SwimLang.T("Ο ΑΓΩΝΑΣ ΑΝΑΜΕΣΑ ΣΤΟΥΣ ΦΙΝΑΛΙΣΤ  ·  ") + SwimAthleteList.Normalize(SwimFinalsPainter.BoardSubtitle(final)) + "  ·  " + SwimAthleteList.Normalize(final.Short)
                : SwimLang.T("Ο ΑΓΩΝΑΣ ΑΠΕΝΑΝΤΙ ΣΤΗΝ ΑΝΑΦΟΡΑ  ·  ") + (Computed.Reference != null ? Computed.Reference.Name : "");
            using (SolidBrush amber = new SolidBrush(SwimTheme.Amber))
            using (StringFormat noWrap = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
                g.DrawString(title, SwimTheme.Ui(8.5f, FontStyle.Bold), amber, new RectangleF(14, 9, W - 28, 16), noWrap);

            const float top = 32, margin = 12, gap = 10;
            float right = W - margin;
            float x = margin;
            if (final != null)
            {
                List<SwimFinalsBoardRow> rows = SwimFinalsCompare.Board(final, finish.D, finish.T.Value, BoardName(Race.Athlete));
                float leftWidth = 320;
                if (rows != null)
                {
                    float bs = Math.Min(leftWidth / 260f, (H - top - margin - 70 - gap) / SwimFinalsPainter.BoardHeight(rows.Count, 1f));
                    int bh = SwimFinalsPainter.BoardHeight(rows.Count, bs);
                    SwimFinalsPainter.PaintBoard(g, new Rectangle((int)x, (int)top, (int)Math.Round(260 * bs), bh), SwimFinalsPainter.BoardTitle(final, finish.D, true),
                        SwimFinalsPainter.BoardSubtitle(final), rows, bs);
                    Rectangle boardCard = new Rectangle((int)x, (int)(H - margin - 70), (int)Math.Round(260 * bs), 70);

                    // Between them: the place in the final, the gap to the first and to the mean of the final.
                    Rectangle result = new Rectangle((int)x, (int)(top + bh + gap), boardCard.Width, (int)(boardCard.Top - gap - (top + bh + gap)));
                    if (result.Height >= 56)
                        DrawFinalResult(g, result, final, rows, finish);
                    DrawCard(g, boardCard, SwimTheme.Accent);
                    DrawBoard(g, boardCard, finish.T.Value, finish);
                    x += 260 * bs + gap + 4;
                }
            }
            else
            {
                Rectangle boardCard = new Rectangle((int)x, (int)top, (int)(right - x), 70);
                DrawCard(g, boardCard, SwimTheme.Accent);
                DrawBoard(g, boardCard, finish.T.Value, finish);
            }

            float y = final != null ? top : top + 70 + gap;
            float width = right - x;
            float bottom = H - margin;
            if (Computed.Reference != null)
            {
                List<SwimDeltaPoint> points = done.Where(r => r.Delta.HasValue).Select(r => new SwimDeltaPoint { D = r.D, Delta = r.Delta.Value }).ToList();
                float h = Math.Min(150, (bottom - y) * 0.42f);
                SwimFinalsPainter.PaintDelta(g, Rectangle.Round(new RectangleF(x, y, width, h)), points, Race.Distance, PoolLength, Computed.Reference.Name, true, 1.2f);
                y += h + gap;
            }

            Rectangle chart = Rectangle.Round(new RectangleF(x, y, width, bottom - y));
            if (chart.Height > 80)
            {
                DrawCard(g, chart, colorAthlete);
                DrawSpeedChart(g, chart, done, finish);
            }
        }

        /// <summary>The place of the athlete in the final at the finish, with the gaps to its first and to its mean.</summary>
        private static void DrawFinalResult(Graphics g, Rectangle r, SwimFinal final, List<SwimFinalsBoardRow> rows, SwimRow finish)
        {
            SwimFinalsBoardRow me = rows.FirstOrDefault(b => b.IsAthlete);
            if (me == null)
                return;

            DrawCard(g, r, SwimTheme.Amber);
            Font caption = SwimTheme.Ui(6.75f, FontStyle.Bold);
            Font place = SwimTheme.Display(24f);
            Font line = SwimTheme.Ui(7.75f, FontStyle.Bold);
            double? mean = final.MeanAt(finish.D);
            double first = rows.Where(b => !b.IsAthlete).Min(b => b.Time);
            string toFirst = SwimReference.FormatDelta(finish.T.Value - first) + SwimLang.T(" s από τον 1ο");
            string toMean = mean.HasValue ? SwimReference.FormatDelta(finish.T.Value - mean.Value) + SwimLang.T(" s από τον μέσο όρο του τελικού") : "";
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            using (SolidBrush text = new SolidBrush(SwimTheme.Text))
            using (SolidBrush amber = new SolidBrush(me.Place <= 3 ? SwimTheme.Green : SwimTheme.Amber))
            using (StringFormat noWrap = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
            {
                g.DrawString(SwimLang.T("ΘΕΣΗ ΣΤΟΝ ΤΕΛΙΚΟ"), caption, dim, r.Left + 12, r.Top + 7);
                string p = SwimLang.Ordinal(me.Place);
                g.DrawString(p, place, amber, r.Left + 10, r.Top + 19);
                float left = r.Left + 14 + g.MeasureString(p, place).Width;
                float lineH = line.GetHeight(g) + 3;
                float y0 = r.Top + 22;
                g.DrawString(toFirst, line, text, new RectangleF(left, y0, r.Right - 8 - left, lineH), noWrap);
                if (toMean.Length > 0)
                    g.DrawString(toMean, line, dim, new RectangleF(left, y0 + lineH, r.Right - 8 - left, lineH), noWrap);
            }
        }

        private static string GraphicHash(byte[] bytes)
        {
            using (SHA1 sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        /// <summary>
        /// Before a PDF: the graphics of the race are drawn, the ones that changed go up to the Drive, then 'then' runs (also after a
        /// failed upload: the PDF is made without that graphic). False when nothing has to go up ('then' has not run).
        /// </summary>
        private bool UploadReportGraphics(Action then)
        {
            if (!Settings.IsConfigured || Race == null || Race.IsTraining || reportGraphicsUploading)
                return false;

            SwimRace race = Race;
            Dictionary<string, byte[]> images = RenderReportGraphics();

            // A graphic that cannot be drawn anymore (e.g. no final for the event now) leaves the report.
            if (race.Graphics.RemoveAll(x => !images.ContainsKey(x.Id)) > 0)
                MarkDirty();

            List<KeyValuePair<string, byte[]>> pending = images.Where(kv =>
            {
                SwimReportGraphic old = race.Graphics.FirstOrDefault(x => x.Id == kv.Key);
                return old == null || string.IsNullOrEmpty(old.FileId) || old.Hash != GraphicHash(kv.Value);
            }).ToList();
            if (pending.Count == 0)
                return false;

            reportGraphicsUploading = true;
            Status(SwimLang.T("🎨 Τα γραφικά του βίντεο ανεβαίνουν για το PDF…"), false);
            UploadReportGraphic(race, pending, 0, then);
            return true;
        }

        private void UploadReportGraphic(SwimRace race, List<KeyValuePair<string, byte[]>> pending, int index, Action then)
        {
            if (index >= pending.Count)
            {
                reportGraphicsUploading = false;
                MarkDirty();
                if (race == Race)
                    then();
                return;
            }

            string id = pending[index].Key;
            byte[] bytes = pending[index].Value;
            client.UploadSnapshot(Settings.WebAppUrl, Settings.Token, race.Id, "report-" + id, bytes, result =>
            {
                if (result.Ok && !string.IsNullOrEmpty(result.FileId))
                {
                    SwimReportGraphic graphic = race.Graphics.FirstOrDefault(x => x.Id == id);
                    if (graphic == null)
                    {
                        graphic = new SwimReportGraphic { Id = id };
                        race.Graphics.Add(graphic);
                    }

                    graphic.FileId = result.FileId;
                    graphic.Hash = GraphicHash(bytes);
                }
                else
                {
                    log.Warn("A graphic of the report was not uploaded (" + id + "): " + result.Error);
                }

                UploadReportGraphic(race, pending, index + 1, then);
            }, "image/png");
        }
        #endregion
    }
}
