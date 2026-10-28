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
    /// (2.11.0) The charts of the short analysis of the PDF: white as the page, the race in blue with its values on it, the PB grey and
    /// dashed, the reference dark and dotted, a dashed line at each wall (the lines of the series cut there) and the lengths named
    /// under the categories. Big letters: the chart is printed at about half of its size.
    /// </summary>
    public static partial class SwimChartPainter
    {
        private static readonly Color LightInk = Color.FromArgb(0x13, 0x17, 0x1C);
        private static readonly Color LightText = Color.FromArgb(0x3F, 0x47, 0x52);
        private static readonly Color LightDim = Color.FromArgb(0x5F, 0x68, 0x74);
        private static readonly Color LightGrid = Color.FromArgb(0xE6, 0xEA, 0xEF);

        private static void PaintLight(Graphics g, SizeF size, SwimChartData c)
        {
            float W = size.Width, H = size.Height;
            using (SolidBrush white = new SolidBrush(Color.White))
                g.FillRectangle(white, 0, 0, W, H);

            int n = c.Categories.Count;
            List<SwimChartSeries> series = c.Series.Where(s => s.Values.Count == n && !s.Bars).ToList();
            Font title = SwimTheme.Ui(15f, FontStyle.Bold);
            Font legend = SwimTheme.Ui(12.5f, FontStyle.Regular);
            Font tick = SwimTheme.Ui(12f, FontStyle.Regular);
            Font groupFont = SwimTheme.Ui(12f, FontStyle.Bold);
            Font valueFont = SwimTheme.Ui(12f, FontStyle.Bold);
            float left = 6, right = W - 6, y = 4;

            // The title on the left and the legend on the right (under the title when it does not fit beside it).
            string text = c.Title ?? "";
            float titleH = 0, titleW = 0;
            if (text.Length > 0)
            {
                SizeF ts = g.MeasureString(text, title);
                titleH = ts.Height;
                titleW = ts.Width;
                using (SolidBrush b = new SolidBrush(LightInk))
                    g.DrawString(text, title, b, left, y);
            }

            float legendW = c.Legend && series.Count > 1 ? LegendWidth(g, series, legend) : 0;
            if (legendW > 0)
            {
                float lx = right - legendW;
                float ly = y;
                if (titleW > 0 && lx < left + titleW + 16)
                {
                    ly = y + titleH + 2;
                    lx = left;
                }

                float bottom = DrawLightLegend(g, series, legend, lx, ly);
                y = Math.Max(y + titleH, bottom) + 6;
            }
            else
            {
                y += titleH + (titleH > 0 ? 6 : 0);
            }

            if (n == 0 || series.Count == 0 || !series.Any(s => s.Values.Any(v => v.HasValue)))
            {
                using (SolidBrush dim = new SolidBrush(LightDim))
                    g.DrawString(SwimLang.T("Δεν υπάρχουν ακόμη τιμές"), tick, dim, left, y + 10);
                return;
            }

            // The axis of the values (the padding of the video charts) and the labels of the categories.
            Axis a = MakeAxis(series, n, false);
            float tickH = tick.GetHeight(g);
            float axisW = 0;
            for (double v = a.Min; v <= a.Max + a.Step * 1e-6; v += a.Step)
                axisW = Math.Max(axisW, g.MeasureString(a.Label(v), tick).Width);
            float plotLeft = left + axisW + 8, plotRight = right - 4;
            float band = (plotRight - plotLeft) / n;
            CategoryLabels labels = CategoryLines(g, c, band, tick);
            int lines = Math.Max(1, labels.Lines.Where(l => l != null).Select(l => l.Count).DefaultIfEmpty(1).Max());
            float groupsH = c.Groups.Count > 0 ? groupFont.GetHeight(g) + 4 : 0;
            Font wallFont = SwimTheme.Ui(11.5f, FontStyle.Bold);
            float wallH = c.Breaks.Count > 0 && band >= 40 ? wallFont.GetHeight(g) + 2 : 0;
            float plotTop = y + wallH + valueFont.GetHeight(g) * 0.6f + 4;
            float plotBottom = H - 4 - groupsH - lines * labels.Font.GetHeight(g) - 6;
            if (plotBottom - plotTop < 40)
                return;

            a.Top = plotTop;
            a.Bottom = plotBottom;
            Func<int, float> X = i => plotLeft + (i + 0.5f) * band;

            using (Pen grid = new Pen(LightGrid, 1f))
            using (SolidBrush dim = new SolidBrush(LightDim))
            {
                for (double v = a.Min; v <= a.Max + a.Step * 1e-6; v += a.Step)
                {
                    float yy = a.Y(v);
                    g.DrawLine(grid, plotLeft, yy, plotRight, yy);
                    string label = a.Label(v);
                    SizeF ls = g.MeasureString(label, tick);
                    g.DrawString(label, tick, dim, plotLeft - 6 - ls.Width, yy - ls.Height / 2);
                }
            }

            // The categories and, under them, the names of their groups (the lengths).
            float lineH = labels.Font.GetHeight(g);
            using (SolidBrush text2 = new SolidBrush(LightText))
            {
                for (int i = 0; i < n; i++)
                {
                    if (labels.Lines[i] == null)
                        continue;
                    for (int j = 0; j < labels.Lines[i].Count; j++)
                    {
                        SizeF s = g.MeasureString(labels.Lines[i][j], labels.Font);
                        float x = Math.Max(plotLeft - 6, Math.Min(plotRight + 6 - s.Width, X(i) - s.Width / 2));
                        g.DrawString(labels.Lines[i][j], labels.Font, text2, x, plotBottom + 5 + j * lineH);
                    }
                }
            }

            if (c.Groups.Count > 0)
            {
                float gy = plotBottom + 5 + lines * lineH + 2;
                using (SolidBrush dim = new SolidBrush(LightDim))
                {
                    foreach (SwimChartGroup gr in c.Groups)
                    {
                        float cx = (X(gr.From) + X(gr.To)) / 2;
                        SizeF s = g.MeasureString(gr.Text ?? "", groupFont);
                        g.DrawString(gr.Text ?? "", groupFont, dim, Math.Max(plotLeft, Math.Min(plotRight - s.Width, cx - s.Width / 2)), gy);
                    }
                }
            }

            // The walls.
            if (c.Breaks.Count > 0)
            {
                // The line of each wall from above the plot, its name above the values (over the plot).
                using (Pen wall = new Pen(LightInk, 1.2f) { DashPattern = new float[] { 4, 4 } })
                using (SolidBrush ink = new SolidBrush(LightInk))
                {
                    foreach (int i in c.Breaks)
                    {
                        float x = plotLeft + i * band;
                        g.DrawLine(wall, x, plotTop - wallH - valueFont.GetHeight(g) * 0.6f, x, plotBottom);
                        if (wallH > 0)
                            g.DrawString(SwimLang.T("τοίχος"), wallFont, ink, x + 4, plotTop - wallH - valueFont.GetHeight(g) * 0.6f - 2);
                    }
                }
            }

            // The series: the others first, the race (the series with its values written) over them.
            List<SwimChartSeries> order = series.OrderBy(s => c.LabelSeries.Contains(c.Series.IndexOf(s)) ? 1 : 0).ToList();
            foreach (SwimChartSeries s in order)
            {
                bool race = c.LabelSeries.Contains(c.Series.IndexOf(s)) || c.Series.IndexOf(s) == 0;
                float width = s.Width > 0 ? s.Width : (race ? 3f : 2f);
                using (Pen pen = LightPen(s, width))
                {
                    foreach (List<PointF> part in LightParts(s, c.Breaks, n, X, a))
                    {
                        if (part.Count > 1)
                            g.DrawLines(pen, part.ToArray());
                    }
                }

                if (n > 40)
                    continue;
                float r = race ? 5f : 3.5f;
                using (SolidBrush fill = new SolidBrush(s.Color))
                using (Pen ring = new Pen(Color.White, 1.5f))
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (!s.Values[i].HasValue)
                            continue;
                        RectangleF dot = new RectangleF(X(i) - r, a.Y(s.Values[i].Value) - r, 2 * r, 2 * r);
                        g.FillEllipse(fill, dot);
                        if (race)
                            g.DrawEllipse(ring, dot);
                    }
                }
            }

            // The values of the race over its points (one that would cover another is left out).
            List<RectangleF> taken = new List<RectangleF>();
            foreach (int k in c.LabelSeries.Where(k => k >= 0 && k < c.Series.Count))
            {
                SwimChartSeries s = c.Series[k];
                if (!series.Contains(s))
                    continue;
                using (SolidBrush b = new SolidBrush(SwimTheme.Mix(s.Color, Color.Black, 0.25)))
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (!s.Values[i].HasValue)
                            continue;
                        string v = i < s.Texts.Count && !string.IsNullOrEmpty(s.Texts[i]) ? s.Texts[i] : FormatValue(s.Values[i].Value, a.Decimals);
                        SizeF vs = g.MeasureString(v, valueFont);
                        float yy = a.Y(s.Values[i].Value) - 8 - vs.Height;
                        if (yy < plotTop - vs.Height * 0.6f)
                            yy = a.Y(s.Values[i].Value) + 8;
                        RectangleF box = new RectangleF(X(i) - vs.Width / 2, yy, vs.Width, vs.Height);
                        if (taken.Any(t => t.IntersectsWith(RectangleF.Inflate(box, 1, 0))))
                            continue;
                        taken.Add(box);
                        g.DrawString(v, valueFont, b, box.Left, box.Top);
                    }
                }
            }
        }

        /// <summary>A value with the decimals of its axis (one when the axis has none, at most two).</summary>
        private static string FormatValue(double v, int axisDecimals)
        {
            int d = axisDecimals == 0 ? 1 : Math.Min(2, axisDecimals);
            return v.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        /// <summary>The points of a series in parts, cut at the walls (and where values are missing).</summary>
        private static List<List<PointF>> LightParts(SwimChartSeries s, List<int> breaks, int n, Func<int, float> X, Axis a)
        {
            List<List<PointF>> parts = new List<List<PointF>>();
            List<PointF> part = new List<PointF>();
            for (int i = 0; i < n; i++)
            {
                if (breaks.Contains(i) && part.Count > 0)
                {
                    parts.Add(part);
                    part = new List<PointF>();
                }

                if (s.Values[i].HasValue)
                    part.Add(new PointF(X(i), a.Y(s.Values[i].Value)));
            }

            if (part.Count > 0)
                parts.Add(part);
            return parts;
        }

        private static Pen LightPen(SwimChartSeries s, float width)
        {
            Pen pen = new Pen(s.Color, width) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (s.Dash == "dash")
                pen.DashPattern = new float[] { 3.2f, 2.2f };
            else if (s.Dash == "dot")
            {
                pen.DashCap = DashCap.Round;
                pen.DashPattern = new float[] { 0.8f, 1.8f };
            }

            return pen;
        }

        private static float LegendWidth(Graphics g, List<SwimChartSeries> series, Font font)
        {
            float w = 0;
            foreach (SwimChartSeries s in series)
                w += 30 + g.MeasureString(s.Name ?? "", font).Width + 14;
            return w;
        }

        /// <summary>The legend on one line from x: a sample of each line and its name. Returns its bottom.</summary>
        private static float DrawLightLegend(Graphics g, List<SwimChartSeries> series, Font font, float x, float y)
        {
            float h = font.GetHeight(g);
            using (SolidBrush text = new SolidBrush(LightText))
            {
                foreach (SwimChartSeries s in series)
                {
                    float cy = y + h / 2;
                    using (Pen pen = LightPen(s, s.Width > 0 ? s.Width : 2.5f))
                        g.DrawLine(pen, x, cy, x + 24, cy);
                    g.DrawString(s.Name ?? "", font, text, x + 30, y);
                    x += 30 + g.MeasureString(s.Name ?? "", font).Width + 14;
                }
            }

            return y + h;
        }
    }
}
