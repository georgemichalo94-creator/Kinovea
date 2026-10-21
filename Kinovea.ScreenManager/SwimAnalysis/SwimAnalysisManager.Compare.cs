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
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The comparison with the reference on the video (since 2.8.0), each when chosen in the panel: the Δ on the race board, the ghost
    /// (a translucent copy of the swimmer where the reference is) and the line of the reference across the lane (as the world record
    /// line of the television). The reference can be another race: another swimmer of this video, another analysis of the athlete or
    /// another athlete's analysis of the event in the Google Sheet.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        private static readonly Color colorCompare = SwimTheme.Accent;
        private SwimCompare.Curve compareCurve;
        private string compareCurveKey = "";

        /// <summary>The athlete's other analyses of the event in the Google Sheet with their times (Apps Script 2.8.0).</summary>
        public List<SwimReference> OwnAnalyses { get; private set; } = new List<SwimReference>();

        /// <summary>Other athletes' analyses of the event in the Google Sheet with their times (Apps Script 2.8.0).</summary>
        public List<SwimReference> EventAnalyses { get; private set; } = new List<SwimReference>();

        /// <summary>The race these analyses were asked for (another race does not use them).</summary>
        private string analysesRaceId = "";

        private bool CompareShown
        {
            get { return Settings != null && (Settings.CompareGhost || Settings.CompareLine); }
        }

        #region The races to compare with
        /// <summary>The other swimmers of this video in the same event (their times on this video), fastest first.</summary>
        private List<SwimReference> VideoReferences()
        {
            List<SwimReference> list = new List<SwimReference>();
            if (Race == null || VideoFile == null || Race.IsTraining)
                return list;

            foreach (SwimRace other in VideoFile.Races)
            {
                if (other == null || other == Race || other.Id == Race.Id || other.IsTraining || !other.StartTimestamp.HasValue)
                    continue;

                SwimReference reference;
                try
                {
                    SwimComputed computed = SwimCalculator.Compute(other, Timing, Settings.StrokeRounding);
                    string name = string.IsNullOrWhiteSpace(other.Athlete) ? other.DisplayName : other.Athlete.Trim();
                    reference = SwimReference.FromRace(other, computed, name);
                }
                catch (Exception e)
                {
                    log.Error("Could not read another race of the video for the comparison.", e);
                    continue;
                }

                if (reference.Times.Count == 0 || !reference.Matches(Race))
                    continue;

                reference.Kind = SwimReference.KindVideo;
                reference.Source = "";
                list.Add(reference);
            }

            return list.OrderBy(r => r.FinalTime ?? double.MaxValue).ToList();
        }

        /// <summary>The other races of the event (this video, the athlete's analyses, other athletes' analyses) for the list of the comparisons.</summary>
        private IEnumerable<SwimReference> RaceReferences()
        {
            if (Race == null || Race.IsTraining)
                return new List<SwimReference>();

            bool sheet = analysesRaceId == Race.Id;
            return VideoReferences()
                .Concat(OwnAnalyses.Where(r => sheet && r.Matches(Race)).OrderBy(r => r.FinalTime ?? double.MaxValue))
                .Concat(EventAnalyses.Where(r => sheet && r.Matches(Race) && r.GenderMatches(Race.AthleteGender)).OrderBy(r => r.FinalTime ?? double.MaxValue));
        }

        /// <summary>An analysis of the Google Sheet with its times as a reference (null without times: Apps Script before 2.8.0).</summary>
        private SwimReference AnalysisReference(JObject o, string kind)
        {
            JArray times = o != null ? o["times"] as JArray : null;
            if (times == null || times.Count == 0 || Race == null)
                return null;

            SwimReference r = new SwimReference
            {
                Id = o.Value<string>("id") ?? "",
                Kind = kind,
                Gender = o.Value<string>("gender") ?? Race.AthleteGender ?? "",
                Style = Race.Style,
                Distance = Race.Distance,
                Relay = Race.Relay,
                Pool = Race.Pool,
                PoolUnit = Race.IsYards ? SwimRace.UnitYards : SwimRace.UnitMeters,
                Source = ""
            };

            foreach (JToken token in times)
            {
                JObject t = token as JObject;
                double? d = t != null ? t.Value<double?>("d") : null;
                double? time = t != null ? t.Value<double?>("t") : null;
                if (d.HasValue && time.HasValue && d.Value > 0 && time.Value > 0 && !double.IsNaN(time.Value))
                    r.Times.Add(new SwimReferenceSplit { D = (int)Math.Round(d.Value), T = time.Value });
            }

            r.Times = r.Times.GroupBy(s => s.D).Select(g => g.First()).OrderBy(s => s.D).ToList();
            if (r.Times.Count == 0 || string.IsNullOrEmpty(r.Id))
                return null;

            string when = string.Join(" · ", new string[] { o.Value<string>("round"), o.Value<string>("competition"), o.Value<string>("date") }
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray());
            string athlete = (o.Value<string>("athlete") ?? "").Trim();
            r.Name = kind == SwimReference.KindOther && athlete.Length > 0
                ? athlete + (when.Length > 0 ? " · " + when : "")
                : (when.Length > 0 ? when : (o.Value<string>("label") ?? SwimLang.T("Αγώνας")));
            return r;
        }

        /// <summary>The analyses of the answer of the Google Sheet as references of the comparison (with their times).</summary>
        private void ReadAnalysisReferences(JObject raw)
        {
            analysesRaceId = Race != null ? Race.Id : "";
            List<SwimReference> own = new List<SwimReference>(), others = new List<SwimReference>();
            JArray analyses = raw != null ? raw["analyses"] as JArray : null;
            if (analyses != null)
            {
                foreach (JToken token in analyses)
                {
                    SwimReference r = AnalysisReference(token as JObject, SwimReference.KindOwn);
                    if (r != null)
                        own.Add(r);
                }
            }

            JArray list = raw != null ? raw["others"] as JArray : null;
            if (list != null)
            {
                foreach (JToken token in list)
                {
                    SwimReference r = AnalysisReference(token as JObject, SwimReference.KindOther);
                    if (r != null && !own.Any(x => x.Id == r.Id))
                        others.Add(r);
                }
            }

            OwnAnalyses = own;
            EventAnalyses = others;
        }
        #endregion

        #region Panel
        /// <summary>For the panel: where the ghost and the line can be drawn, or what they need.</summary>
        public string CompareHint()
        {
            if (Race == null || Race.IsTraining || Settings == null)
                return "";
            if (Computed == null || Computed.Reference == null)
                return SwimLang.T("Διάλεξε με ποιον γίνεται η σύγκριση: «Σύγκριση με» πιο πάνω.");
            if (!CompareShown)
                return "";

            int parts = Race.Tracks.Count(t => t != null && t.HasPan);
            if (parts > 0)
                return string.Format(CultureInfo.InvariantCulture, SwimLang.T("Ghost / γραμμή όπου ο αθλητής ακολουθείται ({0} μέρη του αγώνα)."), parts);
            if (Settings.FixedCamera && VideoFile != null && VideoFile.Calibrations.Any(c => c != null && c.IsValid))
                return SwimLang.T("Ghost / γραμμή με τη βαθμονόμηση της σταθερής κάμερας.");
            if (Race.Tracks.Count > 0)
                return SwimLang.T("Η ακολούθηση του αθλητή έγινε με παλαιότερη έκδοση: ξανά «Ακολούθηση αθλητή» ή ο αυτόματος πιλότος για το ghost και τη γραμμή.");
            return SwimLang.T("Για το ghost και τη γραμμή: «Ακολούθηση αθλητή» σε κάθε μήκος ή ο αυτόματος πιλότος.");
        }
        #endregion

        #region Place of the reference
        private SwimCompare.Curve CompareCurve()
        {
            SwimReference reference = Computed != null ? Computed.Reference : null;
            if (reference == null || Race == null)
                return null;

            string key = (reference.Id ?? "") + "|" + DataVersion.ToString(CultureInfo.InvariantCulture) + "|" + Race.Distance.ToString(CultureInfo.InvariantCulture);
            if (compareCurve == null || compareCurveKey != key)
            {
                compareCurve = SwimCompare.Curve.Of(reference, Race.Distance);
                compareCurveKey = key;
            }

            return compareCurve.Valid ? compareCurve : null;
        }

        // (2.9.6) The camera of the video from the parts of the race read (the line of the reference in perspective), found once (about
        // 0.1 s) for the times of the checkpoints and the breakouts as they are: they give the places of the frames (a stroke or another
        // mark does not), and for the parts read.
        private SwimCompare.SideCamera compareCamera;
        private string compareCameraKey;

        private SwimCompare.SideCamera CompareCamera(SwimTrack track, long start, SwimReel reel)
        {
            List<SwimTrack> tracks = Race.Tracks == null ? new List<SwimTrack>() : Race.Tracks.Where(t => t != null && t.HasPan).ToList();
            string key = string.Join(",", Computed.Rows.Select(r => r.OnVideo.ToString("0.000", CultureInfo.InvariantCulture))) + "|" +
                string.Join(",", Computed.Breakouts.Select(b => b.ToString("0.000", CultureInfo.InvariantCulture))) + "|" +
                start.ToString(CultureInfo.InvariantCulture) + "|" + PoolLength.ToString(CultureInfo.InvariantCulture) + "|" +
                Race.Distance.ToString(CultureInfo.InvariantCulture) + "|" +
                string.Join(",", tracks.Select(t => t.Start.ToString(CultureInfo.InvariantCulture) + ":" + t.X.Count.ToString(CultureInfo.InvariantCulture))) + "|" +
                (Race.Id ?? "");
            if (key == compareCameraKey)
                return compareCamera;

            compareCameraKey = key;
            try
            {
                compareCamera = SwimCompare.CameraOf(tracks, ts => Timing.Seconds(ts, start), reel.DistanceAt, PoolLength, Race.Distance);
            }
            catch (Exception e)
            {
                log.Debug("No camera for the line of the reference: " + e.Message);
                compareCamera = null;
            }

            return compareCamera;
        }

        /// <summary>The reference at a frame: from the part of the race followed there, else the calibration of a fixed camera.</summary>
        private SwimCompare.Placement ComparePlacement(long timestamp)
        {
            if (Race == null || Race.IsTraining || Computed == null || !Race.StartTimestamp.HasValue || !Timing.IsValid)
                return null;

            SwimCompare.Curve curve = CompareCurve();
            SwimReel reel = Reel;
            if (curve == null || reel == null || !reel.HasModel)
                return null;

            long start = Race.StartTimestamp.Value;
            double t = Timing.Seconds(timestamp, start);
            if (t <= 0)
                return null;

            SwimTrack track = Race.TrackAt(timestamp);
            if (track != null && track.HasPan)
            {
                SwimCompare.Placement p = SwimCompare.Place(track, timestamp, ts => Timing.Seconds(ts, start), reel.DistanceAt, curve, PoolLength,
                    CompareCamera(track, start, reel));
                if (p != null)
                    return p;
            }

            SwimCalibration calibration = Settings.FixedCamera ? CalibrationFor(timestamp) : null;
            return calibration != null ? SwimCompare.PlaceCalibrated(calibration, t, reel.DistanceAt(t), curve, PoolLength) : null;
        }
        #endregion

        #region Drawing
        /// <summary>
        /// The ghost and the line of the reference at a frame. frame: the image of the video (for the ghost); reference: the size of the
        /// reference image (the coordinates of the analysis); toView: reference image pixels → drawing; view: size of the drawing;
        /// textScale: size of the labels (1 on the screen, the scale of the graphics in an export).
        /// </summary>
        private void DrawCompare(Graphics g, long timestamp, Bitmap frame, Size reference, Func<PointF, PointF> toView, Size view, float textScale)
        {
            if (!CompareShown || g == null || toView == null || reference.Width <= 0 || reference.Height <= 0)
                return;

            // Whatever goes wrong here leaves the rest of the drawing of the frame (the board, the chart, the angles) as it is.
            try
            {
                DrawCompareAt(g, timestamp, frame, reference, toView, view, textScale);
            }
            catch (Exception e)
            {
                log.Debug("No comparison on this frame: " + e.Message);
            }
        }

        private void DrawCompareAt(Graphics g, long timestamp, Bitmap frame, Size reference, Func<PointF, PointF> toView, Size view, float textScale)
        {
            SwimCompare.Placement p = ComparePlacement(timestamp);
            if (p == null)
                return;

            // Drawing units per reference pixel.
            PointF o = toView(new PointF(0, 0)), e = toView(new PointF(1000, 0));
            float k = (float)Math.Sqrt((e.X - o.X) * (e.X - o.X) + (e.Y - o.Y) * (e.Y - o.Y)) / 1000f;
            if (k <= 0 || float.IsNaN(k))
                return;

            SwimReference r = Computed.Reference;
            string name = r.ShortName;
            string gap = CompareGap(p);
            Brush gapBrush = p.Ahead > 0.05 ? brushSlower : (p.Ahead < -0.05 ? brushFaster : brushDim);
            if (p.Finished)
            {
                double? final = r.FinalTime;
                gap = final.HasValue ? "✓ " + SwimReference.FormatTime(final.Value) : "✓";
                gapBrush = brushDim;
            }

            PointF swimmer = toView(new PointF(p.SwimmerX, p.SwimmerY));
            if (!p.Located)
            {
                DrawCompareEdge(g, p, swimmer, name, gap, gapBrush, view, textScale, k);
                return;
            }

            PointF headTop = toView(p.LineA), headBottom = toView(p.LineB);
            float headX = 0.5f * (headTop.X + headBottom.X);
            if (headX < -4 || headX > view.Width + 4)
            {
                DrawCompareEdge(g, p, swimmer, name, gap, gapBrush, view, textScale, k);
                return;
            }

            RectangleF ghost = RectangleF.Empty;
            if (Settings.CompareGhost)
                ghost = DrawGhost(g, p, frame, reference, toView);

            PointF anchor;
            if (Settings.CompareLine)
            {
                PointF top = DrawCompareLine(g, p, toView, k);
                anchor = new PointF(top.X, top.Y - 3);
            }
            else if (!ghost.IsEmpty)
            {
                anchor = new PointF(ghost.Left + ghost.Width / 2, ghost.Top + 0.12f * ghost.Height);
            }
            else
            {
                return;
            }

            DrawCompareChip(g, name, gap, gapBrush, anchor, textScale, view, 0);
        }

        /// <summary>How far the reference is ahead of the swimmer ("+1.2 m": the reference ahead, as a Δ above zero).</summary>
        private string CompareGap(SwimCompare.Placement p)
        {
            double ahead = p.Ahead;
            if (Math.Abs(ahead) < 0.05)
                return "±0.0 " + UnitShort;
            return (ahead > 0 ? "+" : "-") + Math.Abs(ahead).ToString("0.0", CultureInfo.InvariantCulture) + " " + UnitShort;
        }

        /// <summary>
        /// The ghost: the swimmer of this frame, copied where the reference is, translucent and tinted, the water clearer than the swimmer
        /// and its splash, faded to the edges; mirrored when the reference swims the other way; fading out after its finish.
        /// Returns where it was drawn (empty if not).
        /// </summary>
        private RectangleF DrawGhost(Graphics g, SwimCompare.Placement p, Bitmap frame, Size reference, Func<PointF, PointF> toView)
        {
            float alpha = p.Finished ? (float)Math.Max(0, 1 - p.FinishedFor / 1.0) : 1f;
            if (frame == null || alpha <= 0.02f)
                return RectangleF.Empty;

            Bitmap patch = null;
            InterpolationMode mode = g.InterpolationMode;
            PixelOffsetMode offset = g.PixelOffsetMode;
            try
            {
                // The image of the video in the proportions of the reference image (else its pixels are not where the analysis is).
                int fw = frame.Width, fh = frame.Height;
                if (fw < 8 || fh < 8)
                    return RectangleF.Empty;
                double aspectFrame = fw / (double)fh, aspectReference = reference.Width / (double)reference.Height;
                if (Math.Abs(aspectFrame - aspectReference) > 0.02 * aspectReference)
                    return RectangleF.Empty;

                float w = Math.Max(24f, Math.Min(0.5f * reference.Width, 1.45f * p.Body));
                float h = Math.Max(16f, Math.Min(0.45f * reference.Height, 2.0f * p.HalfHeight));
                float fx = fw / (float)reference.Width, fy = fh / (float)reference.Height;
                RectangleF source = new RectangleF((p.SwimmerX - w / 2) * fx, (p.SwimmerY - h / 2) * fy, w * fx, h * fy);

                // The ghost is made at most GhostWidth pixels wide (drawn larger): every frame while the video plays.
                float shrink = Math.Min(1f, GhostWidth / Math.Max(1f, source.Width));
                patch = GhostPatch(frame, source, alpha, p.Body * fx * shrink, shrink);
                if (patch == null)
                    return RectangleF.Empty;
                if (p.Mirror)
                    patch.RotateFlip(RotateFlipType.RotateNoneFlipX);

                PointF a = toView(new PointF(p.X - w / 2, p.Y - h / 2)), b = toView(new PointF(p.X + w / 2, p.Y + h / 2));
                RectangleF dest = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                if (dest.Width < 2 || dest.Height < 2)
                    return RectangleF.Empty;

                g.InterpolationMode = InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(patch, dest);
                return dest;
            }
            catch (Exception e)
            {
                // A frame in use elsewhere (decoding): no ghost on this frame.
                log.Debug("No ghost on this frame: " + e.Message);
                return RectangleF.Empty;
            }
            finally
            {
                g.InterpolationMode = mode;
                g.PixelOffsetMode = offset;
                if (patch != null)
                    patch.Dispose();
            }
        }

        /// <summary>Largest width of the ghost's image (pixels).</summary>
        private const float GhostWidth = 360f;

        /// <summary>
        /// The ghost's image: a part of the frame (frame pixels) with its transparency and its tint, at 'scale' of its size. body: the
        /// swimmer's length in pixels of the image. Null if the part is not in the frame.
        /// </summary>
        private static Bitmap GhostPatch(Bitmap frame, RectangleF source, float alpha, float body, float scale = 1f)
        {
            int pw = Math.Max(2, (int)Math.Round(source.Width * scale)), ph = Math.Max(2, (int)Math.Round(source.Height * scale));
            RectangleF inside = RectangleF.Intersect(source, new RectangleF(0, 0, frame.Width, frame.Height));
            if (inside.Width < 2 || inside.Height < 2)
                return null;

            Bitmap patch = new Bitmap(pw, ph, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics pg = Graphics.FromImage(patch))
                {
                    pg.Clear(Color.Transparent);
                    pg.InterpolationMode = InterpolationMode.Bilinear;
                    pg.PixelOffsetMode = PixelOffsetMode.Half;
                    float kx = pw / source.Width, ky = ph / source.Height;
                    pg.DrawImage(frame, new RectangleF((inside.X - source.X) * kx, (inside.Y - source.Y) * ky, inside.Width * kx, inside.Height * ky), inside, GraphicsUnit.Pixel);
                }

                BitmapData data = patch.LockBits(new Rectangle(0, 0, pw, ph), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = data.Stride;
                    byte[] px = new byte[stride * ph];
                    Marshal.Copy(data.Scan0, px, 0, px.Length);
                    SwimCompare.GhostAlpha(px, stride, pw, ph, alpha, colorCompare, body);
                    Marshal.Copy(px, 0, data.Scan0, px.Length);
                }
                finally
                {
                    patch.UnlockBits(data);
                }

                return patch;
            }
            catch
            {
                patch.Dispose();
                throw;
            }
        }

        /// <summary>The line across the lane at the head of the reference, a little past the ropes; returns its top end (drawing).</summary>
        private static PointF DrawCompareLine(Graphics g, SwimCompare.Placement p, Func<PointF, PointF> toView, float k)
        {
            const float past = 0.35f;
            PointF a = p.LineA, b = p.LineB;
            PointF a2 = new PointF(a.X + (a.X - b.X) * past, a.Y + (a.Y - b.Y) * past);
            PointF b2 = new PointF(b.X + (b.X - a.X) * past, b.Y + (b.Y - a.Y) * past);
            PointF va = toView(a2), vb = toView(b2);
            float width = Math.Max(3f, Math.Min(14f, 0.2f * p.HalfHeight * k));
            int fade = p.Finished ? (int)Math.Round(255 * Math.Max(0.35, 1 - p.FinishedFor / 4.0)) : 255;

            using (Pen shadow = new Pen(Color.FromArgb(fade * 110 / 255, 0, 0, 0), width + 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen line = new Pen(Color.FromArgb(fade * 235 / 255, colorCompare), width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen core = new Pen(Color.FromArgb(fade * 230 / 255, 255, 255, 255), Math.Max(1f, 0.3f * width)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(shadow, va, vb);
                g.DrawLine(line, va, vb);
                g.DrawLine(core, va, vb);
            }

            return va.Y <= vb.Y ? va : vb;
        }

        /// <summary>The reference out of the image: its label at the edge on its side, with an arrow.</summary>
        private void DrawCompareEdge(Graphics g, SwimCompare.Placement p, PointF swimmer, string name, string gap, Brush gapBrush, Size view, float textScale, float k)
        {
            if (p.Side == 0 || swimmer.Y < 0 || swimmer.Y > view.Height)
                return;

            float y = swimmer.Y - Math.Max(10f, p.HalfHeight * k) - 4f;
            PointF anchor = new PointF(p.Side > 0 ? view.Width : 0, y);
            DrawCompareChip(g, name, gap, gapBrush, anchor, textScale, view, p.Side);
        }

        /// <summary>
        /// A label of the reference: its name and how far it is, its bottom at the anchor (centred, or at the edge of the side of the
        /// arrow), kept inside the drawing.
        /// </summary>
        private static void DrawCompareChip(Graphics g, string name, string gap, Brush gapBrush, PointF anchor, float textScale, Size view, int arrow)
        {
            GraphicsState state = g.Save();
            try
            {
                float s = Math.Max(0.25f, textScale);
                g.TranslateTransform(anchor.X, anchor.Y);
                g.ScaleTransform(s, s);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                Font font = SwimTheme.Ui(7.5f, FontStyle.Bold);
                Font gapFont = SwimTheme.Display(9f);
                string text = arrow < 0 ? "◀ " + name : (arrow > 0 ? name + " ▶" : name);
                SizeF ns = g.MeasureString(text, font);
                SizeF gs = string.IsNullOrEmpty(gap) ? SizeF.Empty : g.MeasureString(gap, gapFont);
                float h = Math.Max(ns.Height, gs.Height) + 4;
                float w = ns.Width + 10 + (gs.IsEmpty ? 0 : gs.Width + 12);

                // The drawing in the units of the label, around the anchor.
                float left = -anchor.X / s + 2, right = (view.Width - anchor.X) / s - 2, top = -anchor.Y / s + 2, bottom = (view.Height - anchor.Y) / s - 2;
                float x = arrow > 0 ? -w - 6 : (arrow < 0 ? 6 : -w / 2);
                float y = -h;
                x = Math.Max(left, Math.Min(right - w, x));
                y = Math.Max(top, Math.Min(bottom - h, y));

                // HUD (2.9.0): a glass chip with a cyan border.
                RectangleF r = new RectangleF(x, y, w, h);
                SwimTheme.HudChip(g, r, colorCompare, 1f, 4f, 218);
                using (SolidBrush accent = new SolidBrush(colorCompare))
                    g.FillRectangle(accent, r.Left + 2f, r.Top + 4, 2f, r.Height - 8);
                using (SolidBrush white = new SolidBrush(SwimTheme.Text))
                {
                    g.DrawString(text, font, white, r.Left + 6, r.Top + (h - ns.Height) / 2);
                    if (!gs.IsEmpty)
                    {
                        RectangleF pill = new RectangleF(r.Left + ns.Width + 9, r.Top + 2, gs.Width + 8, h - 4);
                        using (GraphicsPath path = SwimTheme.Rounded(pill, Math.Min(pill.Height / 2, 6)))
                            g.FillPath(gapBrush, path);
                        g.DrawString(gap, gapFont, white, pill.Left + 4, pill.Top + (pill.Height - gs.Height) / 2);
                    }
                }
            }
            finally
            {
                g.Restore(state);
            }
        }
        #endregion
    }
}
