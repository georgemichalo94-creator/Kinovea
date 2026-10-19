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
using System.Text;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>A series of a chart of the report, with its values as the sheet has them.</summary>
    public sealed class SwimChartSeries
    {
        public string Name = "";
        public Color Color = Color.FromArgb(242, 169, 0);
        public bool Bars;
        public int Axis;
        public List<double?> Values = new List<double?>();

        /// <summary>The values as the sheet shows them (its number format), for the labels on the chart.</summary>
        public List<string> Texts = new List<string>();
    }

    /// <summary>
    /// A chart of the PDF report and its values, read by the Apps Script from the sheet of the race (action "pdf" with drawCharts).
    /// The PDF of Google draws only the title of most of the charts of a sheet: Kinovea draws them, with the graphics of the video.
    /// </summary>
    public sealed class SwimChartData
    {
        public string Key = "";
        public string Type = "line";
        public string Title = "";
        public string VTitle = "";
        public string V2Title = "";
        public string HTitle = "";
        public bool Stacked;
        public int Width = 546;
        public int Height = 300;

        /// <summary>Hash of the values and of the settings, from the Apps Script: the image is used while they do not change.</summary>
        public string Hash = "";

        /// <summary>All the colours of the chart (a pie has one per slice).</summary>
        public List<Color> Colors = new List<Color>();
        public List<string> Categories = new List<string>();
        public List<SwimChartSeries> Series = new List<SwimChartSeries>();

        /// <summary>The series with their values written on the chart (the race, with few points).</summary>
        public List<int> LabelSeries = new List<int>();

        public static SwimChartData FromJson(JObject o)
        {
            SwimChartData c = new SwimChartData();
            if (o == null)
                return c;

            c.Key = Text(o["key"]);
            c.Type = Text(o["type"]);
            c.Title = Text(o["title"]);
            c.VTitle = Text(o["vTitle"]);
            c.V2Title = Text(o["v2Title"]);
            c.HTitle = Text(o["hTitle"]);
            c.Hash = Text(o["hash"]);
            c.Stacked = o["stacked"] != null && o["stacked"].Type == JTokenType.Boolean && (bool)o["stacked"];
            c.Width = Math.Max(120, Math.Min(1200, Integer(o["width"], 546)));
            c.Height = Math.Max(80, Math.Min(900, Integer(o["height"], 300)));

            foreach (JToken t in Array(o["colors"]))
                c.Colors.Add(ParseColor(Text(t), SwimTheme.Amber));
            foreach (JToken t in Array(o["categories"]))
                c.Categories.Add(Text(t));

            foreach (JObject s in Array(o["series"]).OfType<JObject>())
            {
                SwimChartSeries x = new SwimChartSeries();
                x.Name = Text(s["name"]);
                x.Color = ParseColor(Text(s["color"]), SwimTheme.Amber);
                x.Bars = Text(s["type"]) == "bars";
                x.Axis = Integer(s["axis"], 0) == 1 ? 1 : 0;
                foreach (JToken v in Array(s["values"]))
                    x.Values.Add(v.Type == JTokenType.Float || v.Type == JTokenType.Integer ? (double?)v.Value<double>() : null);
                foreach (JToken t in Array(s["texts"]))
                    x.Texts.Add(Text(t));

                // As many values as categories.
                while (x.Values.Count < c.Categories.Count)
                    x.Values.Add(null);
                if (x.Values.Count > c.Categories.Count)
                    x.Values.RemoveRange(c.Categories.Count, x.Values.Count - c.Categories.Count);
                while (x.Texts.Count < x.Values.Count)
                    x.Texts.Add("");
                c.Series.Add(x);
            }

            foreach (JToken t in Array(o["labelSeries"]))
            {
                int i = Integer(t, -1);
                if (i >= 0 && i < c.Series.Count && !c.LabelSeries.Contains(i))
                    c.LabelSeries.Add(i);
            }

            return c;
        }

        public static Color ParseColor(string s, Color fallback)
        {
            s = (s ?? "").Trim().TrimStart('#');
            int rgb;
            if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
                return fallback;
            return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        }

        private static IEnumerable<JToken> Array(JToken t)
        {
            JArray a = t as JArray;
            return a != null ? (IEnumerable<JToken>)a : new JToken[0];
        }

        private static string Text(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined)
                return "";
            JValue v = t as JValue;
            if (v == null)
                return "";
            return Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "";
        }

        private static int Integer(JToken t, int fallback)
        {
            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float))
                return fallback;
            double d = t.Value<double>();
            return double.IsNaN(d) || Math.Abs(d) > 1e6 ? fallback : (int)Math.Round(d);
        }
    }

    /// <summary>
    /// The charts of the PDF report with the graphics of the video: a dark card with the cyan accent, the title in capitals, the race
    /// in amber with its values written on it, the reference (elite) in cyan, the PB grey and dashed, a grid that prints well.
    /// The image is twice the size of the chart on the sheet, so that it is as sharp as the text of the PDF.
    /// </summary>
    public static class SwimChartPainter
    {
        /// <summary>Changes when the drawing changes: the charts already on the Drive are drawn again.</summary>
        public const string Version = "1";

        /// <summary>Pixels of the image per pixel of the sheet.</summary>
        public const float Scale = 2f;

        private static readonly Color CardTop = Color.FromArgb(14, 21, 32);
        private static readonly Color CardBottom = Color.FromArgb(21, 32, 47);
        private static readonly Color CardBorder = Color.FromArgb(48, 68, 92);
        private static readonly Color Grid = Color.FromArgb(40, 57, 78);
        private static readonly Color ZeroLine = Color.FromArgb(120, 140, 160);
        private static readonly Color PbGrey = Color.FromArgb(140, 160, 182);
        private const float Margin = 16;

        /// <summary>The chart as a PNG (at most 1 million pixels: the limit of an image over the cells of a Google Sheet).</summary>
        public static byte[] RenderPng(SwimChartData chart)
        {
            float scale = Scale;
            while (chart.Width * scale * chart.Height * scale > 1000000 && scale > 1)
                scale -= 0.25f;
            int w = (int)Math.Round(chart.Width * scale), h = (int)Math.Round(chart.Height * scale);
            using (Bitmap image = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.White);
                    g.ScaleTransform(scale, scale);
                    Paint(g, new SizeF(chart.Width, chart.Height), chart);
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    image.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }

        public static void Paint(Graphics g, SizeF size, SwimChartData chart)
        {
            float W = size.Width, H = size.Height;
            RectangleF card = new RectangleF(0.5f, 0.5f, W - 1, H - 1);
            using (GraphicsPath path = SwimTheme.Rounded(card, 10))
            using (LinearGradientBrush back = new LinearGradientBrush(new RectangleF(0, 0, W, H), CardTop, CardBottom, LinearGradientMode.Vertical))
            using (Pen border = new Pen(CardBorder, 1f))
            {
                g.FillPath(back, path);
                g.DrawPath(border, path);
            }

            using (SolidBrush accent = new SolidBrush(SwimTheme.Accent))
                g.FillRectangle(accent, 1.5f, 14, 3, H - 28);

            float top = DrawTitle(g, chart, W);
            RectangleF area = new RectangleF(Margin, top, W - 2 * Margin, H - top - 10);
            if (area.Width < 60 || area.Height < 40)
                return;

            try
            {
                if (chart.Type == "pie")
                    PaintPie(g, chart, area);
                else if (chart.Type == "radar")
                    PaintRadar(g, chart, area);
                else
                    PaintXY(g, chart, area);
            }
            catch (Exception)
            {
                // A chart that cannot be drawn says so on its card: the report goes on.
                Empty(g, area, SwimLang.T("Το γράφημα δεν σχεδιάστηκε"));
            }
        }

        #region Title, legend, empty
        /// <summary>The title in capitals and its unit (the part in brackets) beside it, dim. Returns the top of the content.</summary>
        private static float DrawTitle(Graphics g, SwimChartData chart, float W)
        {
            string main = chart.Title ?? "", unit = TitleUnit(main);
            if (unit.Length > 0)
                main = main.Substring(0, main.Length - unit.Length - 3);

            Font font = SwimTheme.Ui(12f, FontStyle.Bold);
            Font unitFont = SwimTheme.Ui(10f, FontStyle.Regular);
            string title = SwimAthleteList.Normalize(main);
            using (SolidBrush text = new SolidBrush(SwimTheme.Text))
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            using (StringFormat noWrap = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
            {
                float y = 11;
                SizeF ts = g.MeasureString(title, font);
                float unitWidth = unit.Length > 0 ? g.MeasureString(unit, unitFont).Width + 8 : 0;
                float max = W - 2 * Margin - unitWidth;
                g.DrawString(title, font, text, new RectangleF(Margin, y, Math.Max(20, max), ts.Height + 1), noWrap);
                if (unit.Length > 0)
                {
                    float x = Margin + Math.Min(ts.Width, max) + 4;
                    g.DrawString(unit, unitFont, dim, x, y + (ts.Height - unitFont.GetHeight(g)) / 2 + 1);
                }

                return y + ts.Height + 6;
            }
        }

        /// <summary>The series in a line (or more) from the left: a mark in their colour and their name in capitals. Returns the bottom.</summary>
        private static float DrawLegend(Graphics g, IList<SwimChartSeries> series, float left, float right, float y)
        {
            Font font = SwimTheme.Ui(9f, FontStyle.Bold);
            float lineH = font.GetHeight(g) + 3;
            float x = left;
            foreach (SwimChartSeries s in series)
            {
                string name = SwimAthleteList.Normalize(string.IsNullOrEmpty(s.Name) ? "—" : s.Name);
                float w = 18 + g.MeasureString(name, font).Width + 12;
                if (x > left && x + w > right)
                {
                    x = left;
                    y += lineH;
                }

                Color color = Vivid(s.Color);
                float cy = y + lineH / 2 - 1;
                if (s.Bars)
                {
                    SwimTheme.FillRounded(g, color, new RectangleF(x + 2, cy - 4.5f, 10, 9), 2);
                }
                else
                {
                    using (Pen pen = LinePen(s, color, 2.2f))
                        g.DrawLine(pen, x, cy, x + 14, cy);
                    using (SolidBrush dot = new SolidBrush(color))
                        g.FillEllipse(dot, x + 4.5f, cy - 2.5f, 5, 5);
                }

                using (SolidBrush text = new SolidBrush(color))
                    g.DrawString(name, font, text, x + 18, y + 1);
                x += w;
            }

            return y + lineH;
        }

        private static void Empty(Graphics g, RectangleF area, string text)
        {
            Font font = SwimTheme.Ui(9.5f, FontStyle.Regular);
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            {
                SizeF s = g.MeasureString(text, font);
                g.DrawString(text, font, dim, area.Left + (area.Width - s.Width) / 2, area.Top + (area.Height - s.Height) / 2);
            }
        }
        #endregion

        #region Lines and columns
        private sealed class Axis
        {
            public double Min;
            public double Max;
            public double Step;
            public int Decimals;
            public float Top;
            public float Bottom;

            public float Y(double v)
            {
                double t = (v - Min) / (Max - Min);
                return Bottom - (float)(t * (Bottom - Top));
            }

            public string Label(double v)
            {
                return Math.Round(v, Decimals).ToString("F" + Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            }
        }

        private static void PaintXY(Graphics g, SwimChartData c, RectangleF area)
        {
            int n = c.Categories.Count;
            List<SwimChartSeries> series = c.Series.Where(s => s.Values.Count == n).ToList();
            if (n == 0 || series.Count == 0 || !series.Any(s => s.Values.Any(v => v.HasValue)))
            {
                Empty(g, area, SwimLang.T("Δεν υπάρχουν ακόμη τιμές"));
                return;
            }

            bool twoAxes = series.Any(s => s.Axis == 1) && series.Any(s => s.Axis == 0);
            Func<SwimChartSeries, int> axisOf = s => twoAxes ? s.Axis : 0;

            float y = area.Top;
            if (series.Count > 1)
                y = DrawLegend(g, series, area.Left, area.Right, y) + 3;

            // The units of the axes above them (in the colour of their first series when there are two), unless the title has it.
            Font small = SwimTheme.Ui(9f, FontStyle.Regular);
            float unitH = small.GetHeight(g);
            string leftUnit = c.VTitle ?? "", rightUnit = twoAxes ? (c.V2Title ?? "") : "";
            if (!twoAxes && SameUnit(leftUnit, TitleUnit(c.Title)))
                leftUnit = "";
            if (leftUnit.Length > 0 || rightUnit.Length > 0)
            {
                Color left = twoAxes ? Vivid(series.First(s => s.Axis == 0).Color) : SwimTheme.Dim;
                using (SolidBrush b = new SolidBrush(left))
                    g.DrawString(leftUnit, small, b, area.Left - 2, y);
                if (rightUnit.Length > 0)
                {
                    using (SolidBrush b = new SolidBrush(Vivid(series.First(s => s.Axis == 1).Color)))
                    {
                        SizeF us = g.MeasureString(rightUnit, small);
                        g.DrawString(rightUnit, small, b, area.Right - us.Width + 2, y);
                    }
                }

                y += unitH + 2;
            }

            Font tick = SwimTheme.Ui(9f, FontStyle.Regular);
            float tickH = tick.GetHeight(g);
            float plotTop = y + tickH / 2 + 2;

            // The axes first (their labels decide the width of the plot), then the categories (their lines decide its height).
            Axis a0 = MakeAxis(series.Where(s => axisOf(s) == 0).ToList(), n, c.Stacked);
            Axis a1 = twoAxes ? MakeAxis(series.Where(s => axisOf(s) == 1).ToList(), n, c.Stacked) : null;
            float leftWidth = AxisWidth(g, a0, tick) + 6;
            float rightWidth = a1 != null ? AxisWidth(g, a1, tick) + 6 : 4;
            float plotLeft = area.Left + leftWidth, plotWidth = area.Width - leftWidth - rightWidth;
            float band = plotWidth / n;
            CategoryLabels labels = CategoryLines(g, c, band, tick);
            int lines = Math.Max(1, labels.Lines.Where(l => l != null).Select(l => l.Count).DefaultIfEmpty(1).Max());
            float plotBottom = area.Bottom - lines * labels.Font.GetHeight(g) - 6;
            if (plotBottom - plotTop < 30)
            {
                Empty(g, area, SwimLang.T("Δεν χωράει το γράφημα"));
                return;
            }

            RectangleF plot = new RectangleF(plotLeft, plotTop, plotWidth, plotBottom - plotTop);
            foreach (Axis a in new[] { a0, a1 })
            {
                if (a == null)
                    continue;
                a.Top = plot.Top;
                a.Bottom = plot.Bottom;
            }

            // Grid with the labels of the axes.
            using (Pen grid = new Pen(Grid, 1f))
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            {
                for (double v = a0.Min; v <= a0.Max + a0.Step * 1e-6; v += a0.Step)
                {
                    float yy = a0.Y(v);
                    g.DrawLine(grid, plot.Left, yy, plot.Right, yy);
                    string label = a0.Label(v);
                    SizeF ls = g.MeasureString(label, tick);
                    g.DrawString(label, tick, dim, plot.Left - 4 - ls.Width, yy - ls.Height / 2);
                }

                if (a1 != null)
                {
                    for (double v = a1.Min; v <= a1.Max + a1.Step * 1e-6; v += a1.Step)
                    {
                        string label = a1.Label(v);
                        SizeF ls = g.MeasureString(label, tick);
                        g.DrawString(label, tick, dim, plot.Right + 4, a1.Y(v) - ls.Height / 2);
                    }
                }
            }

            // The zero of an axis that crosses it: of the left one solid, of the right one dashed in the colour of its series.
            if (a0.Min < 0 && a0.Max > 0)
            {
                using (Pen zero = new Pen(ZeroLine, 1.2f))
                    g.DrawLine(zero, plot.Left, a0.Y(0), plot.Right, a0.Y(0));
            }
            else if (a1 != null && a1.Min < 0 && a1.Max > 0)
            {
                using (Pen zero = new Pen(Color.FromArgb(150, Vivid(series.First(s => s.Axis == 1).Color)), 1.2f) { DashPattern = new float[] { 4, 3 } })
                    g.DrawLine(zero, plot.Left, a1.Y(0), plot.Right, a1.Y(0));
            }

            Func<int, float> X = i => plot.Left + (i + 0.5f) * band;
            DrawCategories(g, labels.Lines, plot, X, labels.Font);

            List<RectangleF> taken = new List<RectangleF>();
            List<SwimChartSeries> bars = series.Where(s => s.Bars).ToList();
            if (bars.Count > 0)
                DrawBars(g, c, bars, series, axisOf, a0, a1, n, band, X, taken);

            // Lines: the race last, over the others.
            List<SwimChartSeries> lineSeries = series.Where(s => !s.Bars).OrderBy(s => c.LabelSeries.Contains(c.Series.IndexOf(s)) ? 1 : 0).ToList();
            foreach (SwimChartSeries s in lineSeries)
            {
                Axis a = axisOf(s) == 1 ? a1 : a0;
                DrawLine(g, s, a, plot, X, n, c.LabelSeries.Contains(c.Series.IndexOf(s)));
            }

            // The values of the race on top of everything; with few categories, the values of the other series too (where there is room).
            List<SwimChartSeries> labelled = c.LabelSeries.Where(i => i >= 0 && i < c.Series.Count).Select(i => c.Series[i]).Where(s => series.Contains(s)).ToList();
            if (n <= 6)
                labelled.AddRange(series.Where(s => !labelled.Contains(s)));
            foreach (SwimChartSeries s in labelled)
            {
                if (c.Stacked && s.Bars)
                    continue;
                Axis a = axisOf(s) == 1 ? a1 : a0;
                DrawValueLabels(g, c, s, series, axisOf, a, plot, X, band, taken);
            }
        }

        /// <summary>The unit in the brackets at the end of a title ("Ταχύτητα ανά τμήμα (m/s)" → "m/s"), else empty.</summary>
        private static string TitleUnit(string title)
        {
            title = title ?? "";
            int open = title.LastIndexOf(" (", StringComparison.Ordinal);
            return open > 0 && title.EndsWith(")", StringComparison.Ordinal) ? title.Substring(open + 2, title.Length - open - 3) : "";
        }

        private static bool SameUnit(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && string.Equals(a.Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The axis of these series: nice steps (4 to 7 of them, as tight as possible), from zero when there are columns.</summary>
        private static Axis MakeAxis(List<SwimChartSeries> series, int n, bool stacked)
        {
            List<double> values = new List<double>();
            bool bars = series.Any(s => s.Bars);
            if (stacked && bars)
            {
                for (int i = 0; i < n; i++)
                {
                    double up = 0, down = 0;
                    foreach (SwimChartSeries s in series.Where(x => x.Bars))
                    {
                        double v = s.Values[i] ?? 0;
                        if (v >= 0) up += v; else down += v;
                    }

                    values.Add(up);
                    values.Add(down);
                }

                values.AddRange(series.Where(s => !s.Bars).SelectMany(s => s.Values).Where(v => v.HasValue).Select(v => v.Value));
            }
            else
            {
                values.AddRange(series.SelectMany(s => s.Values).Where(v => v.HasValue).Select(v => v.Value));
            }

            values = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
            double lo = values.Count > 0 ? values.Min() : 0, hi = values.Count > 0 ? values.Max() : 1;
            if (bars)
            {
                lo = Math.Min(0, lo);
                hi = Math.Max(0, hi);
            }

            if (hi - lo < 1e-9)
            {
                double pad = Math.Abs(hi) > 1e-9 ? Math.Abs(hi) * 0.1 : 1;
                if (!(bars && lo == 0))
                    lo -= pad;
                hi += pad;
            }
            else if (bars)
            {
                // Room for the values over the columns.
                double span = hi - lo;
                if (hi > 0) hi += span * 0.12;
                if (lo < 0) lo -= span * 0.12;
            }
            else
            {
                double span = hi - lo;
                lo -= span * 0.12;
                hi += span * 0.16;
            }

            // The step among 1, 2, 2.5, 5 x 10^k that covers [lo, hi] with 4 to 7 steps and the least empty room.
            double mag = Math.Pow(10, Math.Floor(Math.Log10((hi - lo) / 5)));
            Axis best = null;
            foreach (double m in new[] { mag / 10, mag, mag * 10 })
            {
                foreach (double f in new[] { 1, 2, 2.5, 5 })
                {
                    double step = f * m;
                    double min = Math.Floor(lo / step + 1e-9) * step, max = Math.Ceiling(hi / step - 1e-9) * step;
                    int count = (int)Math.Round((max - min) / step);
                    if (count < 4 || count > 7)
                        continue;
                    if (best == null || max - min < best.Max - best.Min - 1e-9 || (Math.Abs(max - min - (best.Max - best.Min)) < 1e-9 && step > best.Step))
                        best = new Axis { Min = min, Max = max, Step = step };
                }
            }

            if (best == null)
            {
                double step = Math.Max(1e-9, (hi - lo) / 5);
                best = new Axis { Min = lo, Max = lo + 5 * step, Step = step };
            }

            int d = 0;
            while (d < 6 && Math.Abs(best.Step * Math.Pow(10, d) - Math.Round(best.Step * Math.Pow(10, d))) > 1e-6)
                d++;
            best.Decimals = d;
            return best;
        }

        private static float AxisWidth(Graphics g, Axis a, Font font)
        {
            float w = 0;
            for (double v = a.Min; v <= a.Max + a.Step * 1e-6; v += a.Step)
                w = Math.Max(w, g.MeasureString(a.Label(v), font).Width);
            return w;
        }

        private sealed class CategoryLabels
        {
            public Font Font;

            /// <summary>The lines of each category (null: not written).</summary>
            public List<List<string>> Lines;
        }

        /// <summary>
        /// The labels of the categories: on one line when they fit, else on up to three lines (at the spaces and after a "/"), in
        /// smaller letters if needed, else every k-th on one line. The unit of the axis goes after the last one.
        /// </summary>
        private static CategoryLabels CategoryLines(Graphics g, SwimChartData c, float band, Font font)
        {
            int n = c.Categories.Count;
            string unit = (c.HTitle ?? "").Trim();
            List<string> labels = Enumerable.Range(0, n).Select(i => i == n - 1 && unit.Length > 0 && unit.Length <= 4 ? c.Categories[i] + unit : c.Categories[i]).ToList();
            float room = Math.Max(8, band - 4);
            if (labels.All(t => g.MeasureString(t, font).Width <= room))
                return new CategoryLabels { Font = font, Lines = labels.Select(t => new List<string> { t }).ToList() };

            foreach (Font f in new[] { font, SwimTheme.Ui(8f, FontStyle.Regular) })
            {
                List<List<string>> wrapped = labels.Select(t => Wrap(g, t, f, room)).ToList();
                if (wrapped.All(w => w != null && w.Count <= 3))
                    return new CategoryLabels { Font = f, Lines = wrapped };
            }

            float widest = labels.Max(t => g.MeasureString(t, font).Width);
            int every = Math.Max(1, (int)Math.Ceiling((widest + 6) / Math.Max(1, band)));
            List<List<string>> some = new List<List<string>>();
            for (int i = 0; i < n; i++)
            {
                bool show = i == n - 1 || (i % every == 0 && !(n - 1 - i < every && every > 1));
                some.Add(show ? new List<string> { labels[i] } : null);
            }

            return new CategoryLabels { Font = font, Lines = some };
        }

        /// <summary>A label in lines no wider than 'room' (at the spaces and after a "/"); null when a word alone is wider.</summary>
        private static List<string> Wrap(Graphics g, string text, Font font, float room)
        {
            List<string> words = new List<string>();
            StringBuilder word = new StringBuilder();
            foreach (char ch in text ?? "")
            {
                if (ch == ' ')
                {
                    if (word.Length > 0)
                        words.Add(word.ToString());
                    word.Clear();
                    continue;
                }

                word.Append(ch);
                if (ch == '/')
                {
                    words.Add(word.ToString());
                    word.Clear();
                }
            }

            if (word.Length > 0)
                words.Add(word.ToString());

            List<string> lines = new List<string>();
            string line = "";
            foreach (string w in words)
            {
                string joined = line.Length == 0 ? w : (line.EndsWith("/", StringComparison.Ordinal) ? line + w : line + " " + w);
                if (g.MeasureString(joined, font).Width <= room)
                {
                    line = joined;
                    continue;
                }

                if (g.MeasureString(w, font).Width > room)
                    return null;
                if (line.Length > 0)
                    lines.Add(line);
                line = w;
            }

            if (line.Length > 0)
                lines.Add(line);
            return lines;
        }

        private static void DrawCategories(Graphics g, List<List<string>> labels, RectangleF plot, Func<int, float> X, Font font)
        {
            float lineH = font.GetHeight(g);
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            using (Pen tick = new Pen(Grid, 1f))
            {
                for (int i = 0; i < labels.Count; i++)
                {
                    if (labels[i] == null)
                        continue;
                    g.DrawLine(tick, X(i), plot.Bottom, X(i), plot.Bottom + 3);
                    for (int j = 0; j < labels[i].Count; j++)
                    {
                        SizeF s = g.MeasureString(labels[i][j], font);
                        float x = Math.Max(plot.Left - 6, Math.Min(plot.Right + 6 - s.Width, X(i) - s.Width / 2));
                        g.DrawString(labels[i][j], font, dim, x, plot.Bottom + 4 + j * lineH);
                    }
                }
            }
        }

        private static void DrawBars(Graphics g, SwimChartData c, List<SwimChartSeries> bars, List<SwimChartSeries> all, Func<SwimChartSeries, int> axisOf,
            Axis a0, Axis a1, int n, float band, Func<int, float> X, List<RectangleF> taken)
        {
            if (c.Stacked)
            {
                float width = Math.Max(2, band * 0.56f);
                for (int i = 0; i < n; i++)
                {
                    double up = 0, down = 0;
                    foreach (SwimChartSeries s in bars)
                    {
                        Axis a = axisOf(s) == 1 ? a1 : a0;
                        double v = s.Values[i] ?? 0;
                        if (Math.Abs(v) < 1e-12)
                            continue;
                        double from = v >= 0 ? up : down;
                        double to = from + v;
                        if (v >= 0) up = to; else down = to;
                        float y0 = a.Y(from), y1 = a.Y(to);
                        RectangleF r = new RectangleF(X(i) - width / 2, Math.Min(y0, y1), width, Math.Max(1, Math.Abs(y1 - y0)));
                        FillBar(g, r, Vivid(s.Color), false);
                    }

                    // The total over the column.
                    double total = 0;
                    bool any = false;
                    foreach (SwimChartSeries s in bars)
                    {
                        if (s.Values[i].HasValue)
                        {
                            total += s.Values[i].Value;
                            any = true;
                        }
                    }

                    if (any)
                    {
                        Axis a = axisOf(bars[0]) == 1 ? a1 : a0;
                        string text = total.ToString("0.00", CultureInfo.InvariantCulture);
                        PointF at = new PointF(X(i), a.Y(up) - 2);
                        Pill(g, text, at, true, SwimTheme.Text, taken, a.Top, a.Bottom);
                    }
                }

                return;
            }

            float group = band * (bars.Count == 1 ? 0.56f : 0.8f);
            float each = group / bars.Count;
            for (int k = 0; k < bars.Count; k++)
            {
                SwimChartSeries s = bars[k];
                Axis a = axisOf(s) == 1 ? a1 : a0;
                double baseValue = a.Min > 0 ? a.Min : (a.Max < 0 ? a.Max : 0);
                for (int i = 0; i < n; i++)
                {
                    if (!s.Values[i].HasValue)
                        continue;
                    float left = X(i) - group / 2 + k * each;
                    float y0 = a.Y(baseValue), y1 = a.Y(s.Values[i].Value);
                    RectangleF r = new RectangleF(left + 0.75f, Math.Min(y0, y1), Math.Max(1, each - 1.5f), Math.Max(1, Math.Abs(y1 - y0)));
                    FillBar(g, r, Vivid(s.Color), s.Values[i].Value < baseValue);
                }
            }
        }

        private static void FillBar(Graphics g, RectangleF r, Color color, bool down)
        {
            Color light = SwimTheme.Mix(color, Color.White, 0.12);
            Color dark = SwimTheme.Mix(color, Color.Black, 0.22);
            using (LinearGradientBrush brush = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), down ? dark : light, down ? light : dark, LinearGradientMode.Vertical))
            using (GraphicsPath path = TopRounded(r, Math.Min(2.5f, r.Width / 3), down))
                g.FillPath(brush, path);
        }

        /// <summary>A column with its end rounded (the top, or the bottom for a negative value).</summary>
        private static GraphicsPath TopRounded(RectangleF r, float radius, bool down)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2;
            if (d < 0.5f || r.Height < d)
            {
                path.AddRectangle(r);
                return path;
            }

            if (!down)
            {
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            }
            else
            {
                path.AddLine(r.X, r.Y, r.Right, r.Y);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            }

            path.CloseFigure();
            return path;
        }

        private static void DrawLine(Graphics g, SwimChartSeries s, Axis a, RectangleF plot, Func<int, float> X, int n, bool race)
        {
            List<PointF> points = new List<PointF>();
            for (int i = 0; i < n; i++)
            {
                if (s.Values[i].HasValue)
                    points.Add(new PointF(X(i), a.Y(s.Values[i].Value)));
            }

            if (points.Count == 0)
                return;

            Color color = Vivid(s.Color);
            if (race && points.Count > 1)
            {
                // A glow under the race, as on the speed chart of the video.
                using (GraphicsPath area = new GraphicsPath())
                {
                    area.AddLines(points.ToArray());
                    area.AddLine(points[points.Count - 1], new PointF(points[points.Count - 1].X, plot.Bottom));
                    area.AddLine(new PointF(points[points.Count - 1].X, plot.Bottom), new PointF(points[0].X, plot.Bottom));
                    area.CloseFigure();
                    float top = points.Min(p => p.Y);
                    using (LinearGradientBrush glow = new LinearGradientBrush(new RectangleF(plot.Left, top - 1, plot.Width, plot.Bottom - top + 2),
                        Color.FromArgb(70, color), Color.FromArgb(0, color), LinearGradientMode.Vertical))
                        g.FillPath(glow, area);
                }
            }

            float width = race ? 2.8f : 2.1f;
            if (points.Count > 1)
            {
                using (Pen pen = LinePen(s, color, width))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawLines(pen, points.ToArray());
                }
            }

            if (n > 40)
                return;
            float radius = race ? 3.6f : (IsPb(s) ? 2.4f : 2.9f);
            using (SolidBrush fill = new SolidBrush(color))
            using (Pen ring = new Pen(CardTop, 1.4f))
            {
                foreach (PointF p in points)
                {
                    RectangleF dot = new RectangleF(p.X - radius, p.Y - radius, 2 * radius, 2 * radius);
                    g.FillEllipse(fill, dot);
                    g.DrawEllipse(ring, dot);
                }
            }
        }

        /// <summary>
        /// The values of a series written on the chart, in dark pills (as the value on the speed chart of the video): over the points
        /// or the columns, the last, the first, the highest and the lowest first; a value that would cover another is left out.
        /// </summary>
        private static void DrawValueLabels(Graphics g, SwimChartData c, SwimChartSeries s, List<SwimChartSeries> series, Func<SwimChartSeries, int> axisOf,
            Axis a, RectangleF plot, Func<int, float> X, float band, List<RectangleF> taken)
        {
            int n = c.Categories.Count;
            List<int> order = Enumerable.Range(0, n).Where(i => s.Values[i].HasValue).ToList();
            if (order.Count == 0)
                return;

            int max = order.OrderByDescending(i => s.Values[i].Value).First();
            int min = order.OrderBy(i => s.Values[i].Value).First();
            List<int> first = new List<int> { order[order.Count - 1], order[0], max, min };
            List<int> sequence = first.Distinct().Concat(order.Where(i => !first.Contains(i))).ToList();

            float barOffset = 0;
            bool bars = s.Bars;
            if (bars)
            {
                List<SwimChartSeries> barSeries = series.Where(x => x.Bars).ToList();
                float group = band * (barSeries.Count == 1 ? 0.56f : 0.8f);
                float each = group / barSeries.Count;
                barOffset = -group / 2 + barSeries.IndexOf(s) * each + each / 2;
            }

            Color color = Vivid(s.Color);
            foreach (int i in sequence)
            {
                double v = s.Values[i].Value;
                string text = !string.IsNullOrEmpty(s.Texts[i]) ? s.Texts[i] : v.ToString("0.##", CultureInfo.InvariantCulture);
                bool down = bars ? v < 0 : false;
                PointF at = new PointF(X(i) + barOffset, a.Y(v) + (down ? 2 : (bars ? -2 : -6)));
                Pill(g, text, at, !down, color, taken, plot.Top, plot.Bottom);
            }
        }

        /// <summary>A value in a dark pill, centred on 'at', above it (or below). False when it would cover another.</summary>
        private static bool Pill(Graphics g, string text, PointF at, bool above, Color color, List<RectangleF> taken, float top, float bottom)
        {
            Font font = SwimTheme.Display(9.25f);
            SizeF s = g.MeasureString(text, font);
            float w = s.Width + 4, h = s.Height - 1;
            float y = above ? at.Y - h : at.Y;
            if (above && y < top - h * 0.6f)
                y = at.Y + 10;
            if (!above && y + h > bottom + 2)
                y = at.Y - h - 10;
            RectangleF r = new RectangleF(at.X - w / 2, y, w, h);
            RectangleF test = RectangleF.Inflate(r, 1, 0.5f);
            if (taken.Any(t => t.IntersectsWith(test)))
                return false;
            taken.Add(r);
            SwimTheme.FillRounded(g, Color.FromArgb(232, 9, 14, 22), r, 4);
            using (Pen edge = new Pen(Color.FromArgb(110, color), 0.8f))
            using (GraphicsPath path = SwimTheme.Rounded(r, 4))
                g.DrawPath(edge, path);
            using (SolidBrush brush = new SolidBrush(color))
                g.DrawString(text, font, brush, r.Left + 2, r.Top - 0.5f);
            return true;
        }
        #endregion

        #region Pie
        /// <summary>A ring (as the video draws its parts), the total in the middle, the parts with their values and shares beside it.</summary>
        private static void PaintPie(Graphics g, SwimChartData c, RectangleF area)
        {
            SwimChartSeries s = c.Series.FirstOrDefault();
            int n = c.Categories.Count;
            List<int> parts = s == null ? new List<int>() : Enumerable.Range(0, n).Where(i => i < s.Values.Count && s.Values[i].HasValue && s.Values[i].Value > 0).ToList();
            double total = parts.Sum(i => s.Values[i].Value);
            if (parts.Count == 0 || total <= 0)
            {
                Empty(g, area, SwimLang.T("Δεν υπάρχουν ακόμη τιμές"));
                return;
            }

            float d = Math.Min(area.Height - 8, area.Width * 0.44f);
            RectangleF ring = new RectangleF(area.Left + 8, area.Top + (area.Height - d) / 2, d, d);
            float thickness = d * 0.2f;
            Func<int, Color> colorOf = i => Vivid(c.Colors.Count > 0 ? c.Colors[i % c.Colors.Count] : s.Color);

            float angle = -90;
            using (Pen gap = new Pen(CardTop, 2.2f))
            {
                foreach (int i in parts)
                {
                    float sweep = (float)(s.Values[i].Value / total * 360);
                    using (GraphicsPath path = new GraphicsPath())
                    {
                        path.AddArc(ring, angle, sweep);
                        RectangleF inner = RectangleF.Inflate(ring, -thickness, -thickness);
                        path.AddArc(inner, angle + sweep, -sweep);
                        path.CloseFigure();
                        using (SolidBrush brush = new SolidBrush(colorOf(i)))
                            g.FillPath(brush, path);
                        g.DrawPath(gap, path);
                    }

                    angle += sweep;
                }
            }

            // The total in the middle.
            Font big = SwimTheme.Display(15f);
            Font caption = SwimTheme.Ui(7.5f, FontStyle.Bold);
            string totalText = total.ToString("0.00", CultureInfo.InvariantCulture);
            using (SolidBrush text = new SolidBrush(SwimTheme.Text))
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            {
                SizeF ts = g.MeasureString(totalText, big);
                SizeF cs = g.MeasureString(SwimLang.T("ΣΥΝΟΛΟ"), caption);
                float cx = ring.Left + ring.Width / 2, cy = ring.Top + ring.Height / 2;
                g.DrawString(totalText, big, text, cx - ts.Width / 2, cy - (ts.Height + cs.Height) / 2);
                g.DrawString(SwimLang.T("ΣΥΝΟΛΟ"), caption, dim, cx - cs.Width / 2, cy - (ts.Height + cs.Height) / 2 + ts.Height - 2);
            }

            // The parts: name, value and share.
            Font name = SwimTheme.Ui(9.5f, FontStyle.Bold);
            Font value = SwimTheme.Display(11f);
            float left = ring.Right + 22, right = area.Right - 4;
            float rowH = Math.Min(34, (area.Height - 4) / parts.Count);
            float y = area.Top + (area.Height - rowH * parts.Count) / 2;
            using (SolidBrush text = new SolidBrush(SwimTheme.Text))
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            using (Pen line = new Pen(Grid, 1f))
            using (StringFormat noWrap = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
            {
                foreach (int i in parts)
                {
                    Color color = colorOf(i);
                    float cy = y + rowH / 2;
                    SwimTheme.FillRounded(g, color, new RectangleF(left, cy - 5, 10, 10), 2.5f);
                    string share = (s.Values[i].Value / total * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
                    string val = !string.IsNullOrEmpty(s.Texts[i]) ? s.Texts[i] : s.Values[i].Value.ToString("0.00", CultureInfo.InvariantCulture);
                    SizeF shs = g.MeasureString(share, value);
                    using (SolidBrush b = new SolidBrush(color))
                        g.DrawString(share, value, b, right - shs.Width, cy - shs.Height / 2);
                    SizeF vs = g.MeasureString(val, value);
                    float valRight = right - shs.Width - 10;
                    g.DrawString(val, value, text, valRight - vs.Width, cy - vs.Height / 2);
                    float nameLeft = left + 16;
                    g.DrawString(SwimAthleteList.Normalize(c.Categories[i]), name, dim,
                        new RectangleF(nameLeft, cy - name.GetHeight(g) / 2, Math.Max(10, valRight - vs.Width - 6 - nameLeft), name.GetHeight(g) + 1), noWrap);
                    if (i != parts[parts.Count - 1])
                        g.DrawLine(line, left, y + rowH, right, y + rowH);
                    y += rowH;
                }
            }
        }
        #endregion

        #region Radar
        /// <summary>One axis per category, the series as polygons, a scale that shows their differences.</summary>
        private static void PaintRadar(Graphics g, SwimChartData c, RectangleF area)
        {
            int n = c.Categories.Count;
            List<SwimChartSeries> series = c.Series.Where(s => s.Values.Count == n && s.Values.Any(v => v.HasValue)).ToList();
            if (n < 3 || series.Count == 0)
            {
                PaintXY(g, c, area);
                return;
            }

            float y = DrawLegend(g, series, area.Left, area.Right, area.Top) + 4;
            RectangleF room = new RectangleF(area.Left, y, area.Width, area.Bottom - y);
            Font label = SwimTheme.Ui(9f, FontStyle.Bold);
            float radius = Math.Min(room.Height / 2 - label.GetHeight(g) - 2, room.Width / 2 - 70);
            if (radius < 30)
            {
                PaintXY(g, c, area);
                return;
            }

            PointF center = new PointF(room.Left + room.Width / 2, room.Top + room.Height / 2);
            List<double> values = series.SelectMany(s => s.Values).Where(v => v.HasValue).Select(v => v.Value).ToList();
            double lo = values.Min(), hi = values.Max();
            double span = Math.Max(1e-6, hi - lo);
            double min = Math.Max(0, lo - span * 0.6), max = hi + span * 0.15;
            if (max - min < 1e-6)
                max = min + 1;
            Func<int, double, PointF> P = (i, v) =>
            {
                double angle = -Math.PI / 2 + i * 2 * Math.PI / n;
                double r = Math.Max(0, Math.Min(1, (v - min) / (max - min))) * radius;
                return new PointF(center.X + (float)(Math.Cos(angle) * r), center.Y + (float)(Math.Sin(angle) * r));
            };

            using (Pen grid = new Pen(Grid, 1f))
            {
                for (int level = 1; level <= 4; level++)
                {
                    double v = min + (max - min) * level / 4;
                    g.DrawPolygon(grid, Enumerable.Range(0, n).Select(i => P(i, v)).ToArray());
                }

                for (int i = 0; i < n; i++)
                    g.DrawLine(grid, center, P(i, max));
            }

            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            {
                for (int i = 0; i < n; i++)
                {
                    string text = SwimAthleteList.Normalize(c.Categories[i]);
                    SizeF s = g.MeasureString(text, label);
                    PointF end = P(i, max);
                    double angle = -Math.PI / 2 + i * 2 * Math.PI / n;
                    float x = end.X + (float)Math.Cos(angle) * 8 - s.Width / 2 + (float)Math.Cos(angle) * s.Width / 2;
                    float yy = end.Y + (float)Math.Sin(angle) * 8 - s.Height / 2 + (float)Math.Sin(angle) * s.Height / 2;
                    g.DrawString(text, label, dim, x, yy);
                }
            }

            foreach (SwimChartSeries s in series)
            {
                Color color = Vivid(s.Color);
                PointF[] points = Enumerable.Range(0, n).Where(i => s.Values[i].HasValue).Select(i => P(i, s.Values[i].Value)).ToArray();
                if (points.Length < 2)
                    continue;
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(46, color)))
                using (Pen pen = LinePen(s, color, 2.2f))
                {
                    if (points.Length >= 3)
                        g.FillPolygon(fill, points);
                    g.DrawPolygon(pen, points);
                }

                using (SolidBrush dot = new SolidBrush(color))
                    foreach (PointF p in points)
                        g.FillEllipse(dot, p.X - 2.6f, p.Y - 2.6f, 5.2f, 5.2f);
            }
        }
        #endregion

        #region Colours
        /// <summary>A colour bright enough on the dark card (the dark ones are lightened).</summary>
        private static Color Vivid(Color c)
        {
            double luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
            return luminance < 0.36 ? SwimTheme.Mix(c, Color.White, 0.3) : c;
        }

        private static bool IsPb(SwimChartSeries s)
        {
            string name = SwimAthleteList.Normalize(s.Name);
            return name == "PB" || name.StartsWith("PB ", StringComparison.Ordinal) || name.EndsWith(" PB", StringComparison.Ordinal) ||
                (s.Color.R == PbGrey.R && s.Color.G == PbGrey.G && s.Color.B == PbGrey.B);
        }

        /// <summary>The pen of a line: the PB dashed (it prints apart from the others).</summary>
        private static Pen LinePen(SwimChartSeries s, Color color, float width)
        {
            Pen pen = new Pen(color, IsPb(s) ? Math.Max(1.6f, width - 0.4f) : width);
            if (IsPb(s))
                pen.DashPattern = new float[] { 3.2f, 2.2f };
            return pen;
        }
        #endregion
    }
}
