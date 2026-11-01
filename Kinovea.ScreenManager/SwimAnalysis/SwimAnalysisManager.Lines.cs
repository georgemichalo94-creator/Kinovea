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
using System.Globalization;
using System.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.16.0) A fixed camera with its calibration: the lines 5, 10 and 15 (pool units from the wall of the start or turn) stay on the
    /// lane in every frame (Settings.DistanceLines). When the head has passed a line (key T, the splits of the phase), its label gives the
    /// time and the speed of the segment that ends there, and a chart of the speeds of the segments (0–5, 5–10, 10–15) is drawn: on the
    /// screen, on the snapshots and on the exported videos with the other graphics.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>The distances of the lines from the wall of the start or turn (pool units).</summary>
        private static readonly int[] DistanceLineOffsets = new int[] { 5, 10, 15 };

        /// <summary>The segment that ends at each line: its speed key (SwimTechCalculator, in m/s).</summary>
        private static readonly Dictionary<int, string> SegmentSpeedKeys = new Dictionary<int, string>
        {
            { 5, "v0_5" }, { 10, "v5_10" }, { 15, "v10_15" }
        };

        private static readonly Color LineColor = SwimTheme.Accent;
        private static readonly Color LinePassedColor = SwimTheme.Amber;

        /// <summary>Redraws the video of the analysis (a setting of the graphics changed).</summary>
        public void RedrawVideo()
        {
            InvalidatePlayer();
        }

        /// <summary>
        /// The lines to draw at a frame: (distance from its wall, pool x of the calibration). None without the setting, a fixed camera or a
        /// calibration. A training video counts from the line 0 of its calibration (the wall of the attempt); a race from the wall nearer
        /// the calibration (the start wall, or the turn wall of a camera at the far end).
        /// </summary>
        private List<KeyValuePair<int, double>> DistanceLinesAt(long timestamp, out SwimCalibration calibration)
        {
            calibration = null;
            List<KeyValuePair<int, double>> lines = new List<KeyValuePair<int, double>>();
            if (Settings == null || !Settings.DistanceLines || !Settings.FixedCamera || VideoFile == null || Race == null || Timing == null)
                return lines;

            calibration = CalibrationFor(timestamp);
            if (calibration == null)
                return lines;

            double pool = PoolLength;
            bool fromFar = !Race.IsTraining && pool > 0 && (calibration.Line1 + calibration.Line2) / 2 > pool / 2.0;
            foreach (int d in DistanceLineOffsets)
            {
                double x = fromFar ? pool - d : d;
                if (x < 0 || (pool > 0 && x > pool + 1e-6))
                    continue;
                lines.Add(new KeyValuePair<int, double>(d, x));
            }

            return lines;
        }

        /// <summary>
        /// When the head passed a line in the phase of the frame (null if not yet): its time (s from the origin of the phase) and the
        /// speed of the segment ending there (m/s, null without the previous line). A turn: the approach (−5) before the contact, the
        /// distances after it (+5, +10, +15) from the contact on.
        /// </summary>
        private bool LinePassed(SwimTechPhase phase, double now, int distance, out int offset, out double time, out double? speed)
        {
            offset = distance;
            time = 0;
            speed = null;
            if (phase == null)
                return false;

            double t;
            if (phase.Kind == SwimPhaseKind.Turn && now < 0)
            {
                // Before the contact only the approach: −5 on the line 5.
                if (distance != 5 || !phase.Splits.TryGetValue(-5, out t) || t > now + 1e-6)
                    return false;
                offset = -5;
                time = t;
                return true;
            }

            if (!phase.Splits.TryGetValue(distance, out t) || t > now + 1e-6)
                return false;

            time = t;
            string key;
            if (SegmentSpeedKeys.TryGetValue(distance, out key))
                speed = phase.Value(key);
            return true;
        }

        /// <summary>A font of the lines and the chart at a scale (sizes rounded to half a point: few fonts in the cache of the theme).</summary>
        private static Font LinesFont(float points, float scale, FontStyle style)
        {
            return SwimTheme.Ui((float)Math.Max(5.0, Math.Round(points * Math.Max(0.6f, scale) * 2) / 2), style);
        }

        /// <summary>
        /// The lines 5/10/15 on the lane, with the time and the speed of each segment once passed. map: reference pixels of the video →
        /// the drawing; scale: of the letters and lines (1 on the screen).
        /// </summary>
        private void DrawDistanceLines(Graphics g, long timestamp, Func<PointF, PointF> map, Size size, float scale)
        {
            SwimCalibration calibration;
            List<KeyValuePair<int, double>> lines = DistanceLinesAt(timestamp, out calibration);
            if (lines.Count == 0)
                return;

            SwimTechPhase phase = SwimTechCalculator.PhaseAt(TechPhases, timestamp);
            double now = phase != null ? Timing.Seconds(timestamp, phase.Origin) : 0;
            string unit = UnitShort;
            Font font = LinesFont(8.5f, scale, FontStyle.Bold);
            foreach (KeyValuePair<int, double> line in lines)
            {
                PointF a, b;
                double margin = Math.Min(0.4, calibration.LaneWidth * 0.15);
                if (!calibration.TryImage(line.Value, -margin, out a) || !calibration.TryImage(line.Value, calibration.LaneWidth + margin, out b))
                    continue;

                PointF pa = map(a), pb = map(b);
                int offset;
                double time;
                double? speed;
                bool passed = LinePassed(phase, now, line.Key, out offset, out time, out speed);
                Color color = passed ? LinePassedColor : LineColor;
                using (Pen glow = new Pen(Color.FromArgb(70, color), 7f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (Pen pen = new Pen(Color.FromArgb(235, color), 2.2f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(glow, pa, pb);
                    g.DrawLine(pen, pa, pb);
                }

                // The label at the far rope (the end of the line higher in the image), out of the way of the swimmer.
                PointF at = pa.Y < pb.Y ? pa : pb;
                string text = (passed && offset < 0 ? "−" : passed && phase != null && phase.Kind == SwimPhaseKind.Turn ? "+" : "") +
                    line.Key.ToString(CultureInfo.InvariantCulture) + " " + unit;
                if (passed)
                {
                    text += " · " + (offset < 0 ? "−" : "") + Math.Abs(time).ToString("0.00", CultureInfo.InvariantCulture) + " s";
                    if (speed.HasValue)
                        text += " · " + speed.Value.ToString("0.00", CultureInfo.InvariantCulture) + " m/s";
                }

                SizeF ts = g.MeasureString(text, font);
                RectangleF chip = new RectangleF(at.X - ts.Width / 2 - 4 * scale, at.Y - ts.Height - 8 * scale, ts.Width + 8 * scale, ts.Height + 3 * scale);
                if (chip.Left < 2) chip.X = 2;
                if (chip.Right > size.Width - 2) chip.X = Math.Max(2, size.Width - 2 - chip.Width);
                if (chip.Top < 2) chip.Y = 2;
                SwimTheme.HudChip(g, chip, color, 1f, 3f * scale, 205);
                using (SolidBrush brush = new SolidBrush(passed ? SwimTheme.Text : SwimTheme.AccentPale))
                    g.DrawString(text, font, brush, chip.X + 4 * scale, chip.Y + 1.5f * scale);
            }
        }

        /// <summary>The chart of the speeds of the segments, with the lines 5/10/15 (drawn over the panels of the HUD).</summary>
        private void DrawDistanceChart(Graphics g, long timestamp, Size size, float scale)
        {
            SwimCalibration calibration;
            if (DistanceLinesAt(timestamp, out calibration).Count == 0)
                return;

            SwimTechPhase phase = SwimTechCalculator.PhaseAt(TechPhases, timestamp);
            if (phase == null)
                return;

            DrawSegmentSpeedChart(g, phase, Timing.Seconds(timestamp, phase.Origin), size, scale);
        }

        /// <summary>
        /// The speeds of the segments of the phase passed so far (0–5, 5–10, 10–15: after the contact for a turn): a bar for each, its value
        /// above it and a line through their tops, at the bottom right. Nothing before the first one.
        /// </summary>
        private void DrawSegmentSpeedChart(Graphics g, SwimTechPhase phase, double now, Size size, float scale, RectangleF? place = null)
        {
            if (phase == null || size.Width < 360 || size.Height < 240)
                return;

            List<KeyValuePair<string, double>> bars = new List<KeyValuePair<string, double>>();
            int previous = 0;
            foreach (int d in DistanceLineOffsets)
            {
                int offset;
                double time;
                double? speed;
                if (phase.Kind == SwimPhaseKind.Turn && now < 0)
                    break;
                if (LinePassed(phase, now, d, out offset, out time, out speed) && speed.HasValue)
                    bars.Add(new KeyValuePair<string, double>(previous.ToString(CultureInfo.InvariantCulture) + "–" + d.ToString(CultureInfo.InvariantCulture), speed.Value));
                previous = d;
            }

            if (bars.Count == 0)
                return;

            // (The panel of a group of screens gives its own place: wide under its values.)
            float w = 236 * scale, h = 132 * scale;
            RectangleF panel = place ?? new RectangleF(size.Width - w - 10 * scale, size.Height - h - 10 * scale, w, h);
            SwimTheme.HudPanel(g, panel, SwimTheme.Accent);
            Font title = LinesFont(7f, scale, FontStyle.Bold);
            Font value = LinesFont(8f, scale, FontStyle.Bold);
            Font axis = LinesFont(7f, scale, FontStyle.Regular);
            SwimTheme.CapsIn(g, SwimLang.T("ΤΑΧΥΤΗΤΑ ΑΝΑ ΤΜΗΜΑ (m/s)"), title, SwimTheme.Amber,
                new RectangleF(panel.Left + 10 * scale, panel.Top + 6 * scale, panel.Width - 20 * scale, 16 * scale), StringAlignment.Near, 0.12f);

            // All three places are kept (the bars that come later fill in): the speeds compare at a glance.
            RectangleF plot = new RectangleF(panel.Left + 14 * scale, panel.Top + 40 * scale, panel.Width - 28 * scale, panel.Height - 62 * scale);
            double max = Math.Max(0.5, bars.Max(b => b.Value)), min = Math.Min(max * 0.6, bars.Min(b => b.Value)) * 0.85;
            float slot = plot.Width / DistanceLineOffsets.Length, barW = slot * 0.46f;
            List<PointF> tops = new List<PointF>();
            using (Pen baseLine = new Pen(Color.FromArgb(90, SwimTheme.Accent), 1f))
                g.DrawLine(baseLine, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            using (SolidBrush text = new SolidBrush(SwimTheme.Text))
            {
                for (int i = 0; i < DistanceLineOffsets.Length; i++)
                {
                    int d = DistanceLineOffsets[i];
                    string label = (i == 0 ? 0 : DistanceLineOffsets[i - 1]).ToString(CultureInfo.InvariantCulture) + "–" + d.ToString(CultureInfo.InvariantCulture);
                    float cx = plot.Left + slot * (i + 0.5f);
                    SizeF ls = g.MeasureString(label, axis);
                    g.DrawString(label, axis, dim, cx - ls.Width / 2, plot.Bottom + 3 * scale);
                    KeyValuePair<string, double> bar = bars.FirstOrDefault(b => b.Key == label);
                    if (bar.Key == null)
                        continue;

                    float top = plot.Bottom - (float)((bar.Value - min) / Math.Max(1e-6, max - min)) * plot.Height;
                    top = Math.Min(plot.Bottom - 3 * scale, Math.Max(plot.Top, top));
                    RectangleF r = new RectangleF(cx - barW / 2, top, barW, plot.Bottom - top);
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(150, SwimTheme.Accent)))
                        g.FillRectangle(fill, r);
                    string v = bar.Value.ToString("0.00", CultureInfo.InvariantCulture);
                    SizeF vs = g.MeasureString(v, value);
                    g.DrawString(v, value, text, cx - vs.Width / 2, Math.Max(panel.Top + 22 * scale, top - vs.Height - 1 * scale));
                    tops.Add(new PointF(cx, top));
                }
            }

            if (tops.Count >= 2)
            {
                using (Pen glow = new Pen(Color.FromArgb(60, SwimTheme.Amber), 5f * scale) { LineJoin = LineJoin.Round })
                using (Pen pen = new Pen(SwimTheme.Amber, 1.8f * scale) { LineJoin = LineJoin.Round })
                {
                    g.DrawLines(glow, tops.ToArray());
                    g.DrawLines(pen, tops.ToArray());
                }
            }

            using (SolidBrush dot = new SolidBrush(SwimTheme.Amber))
                foreach (PointF p in tops)
                    g.FillEllipse(dot, p.X - 3 * scale, p.Y - 3 * scale, 6 * scale, 6 * scale);
        }
    }
}
