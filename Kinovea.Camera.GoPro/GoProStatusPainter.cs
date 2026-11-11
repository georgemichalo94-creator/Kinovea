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

namespace Kinovea.Camera.GoPro
{
    /// <summary>
    /// (Swim 2.20.0) The image of a GoPro screen: the name of the camera, its link, whether it records and for how long, its battery,
    /// card, temperature and settings, and what to do when something is wrong. The texts are large: with three to five screens the
    /// image is shown at less than half of its size. One painter per screen (the objects of drawing are not shared between threads).
    /// </summary>
    internal sealed class GoProStatusPainter : IDisposable
    {
        public const int Width = 960;
        public const int Height = 540;

        private const float Margin = 28;
        private static readonly Color Background = Color.FromArgb(14, 26, 34);
        private static readonly Color TileColor = Color.FromArgb(26, 48, 58);
        private static readonly Color Ink = Color.FromArgb(232, 241, 244);
        private static readonly Color Dim = Color.FromArgb(160, 184, 194);
        private static readonly Color Red = Color.FromArgb(255, 82, 72);
        private static readonly Color Green = Color.FromArgb(76, 210, 132);
        private static readonly Color Amber = Color.FromArgb(255, 180, 77);
        private static readonly Color Blue = Color.FromArgb(110, 180, 255);

        private readonly Font fontTitle = new Font("Segoe UI Semibold", 44f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontSub = new Font("Segoe UI", 26f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontPill = new Font("Segoe UI Semibold", 26f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontState = new Font("Segoe UI Semibold", 54f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontTimer = new Font("Segoe UI Semibold", 70f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontOffset = new Font("Segoe UI Semibold", 32f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontTileTitle = new Font("Segoe UI", 25f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontTileValue = new Font("Segoe UI Semibold", 44f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontTileNote = new Font("Segoe UI", 25f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontFooter = new Font("Segoe UI", 26f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontHint = new Font("Segoe UI Semibold", 27f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontBand = new Font("Segoe UI Semibold", 30f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly StringFormat ellipsis = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
        private readonly StringFormat wrap = new StringFormat() { Trimming = StringTrimming.EllipsisWord };
        private readonly StringFormat right = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter };

        public void Dispose()
        {
            foreach (Font f in new Font[] { fontTitle, fontSub, fontPill, fontState, fontTimer, fontOffset, fontTileTitle, fontTileValue, fontTileNote, fontFooter, fontHint, fontBand })
                f.Dispose();

            ellipsis.Dispose();
            wrap.Dispose();
            right.Dispose();
        }

        public void Paint(Graphics g, GoProState s)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Background);

            PaintHeader(g, s);
            PaintState(g, s);
            PaintTiles(g, s);
            PaintFooter(g, s);
        }

        /// <summary>
        /// The live image with a band over its top: the name of the camera, whether it records and for how long, its battery and card,
        /// and when the live image closes by itself.
        /// </summary>
        public void PaintOverlay(Graphics g, GoProState s)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            const float band = 64;
            using (SolidBrush back = new SolidBrush(Color.FromArgb(185, 8, 16, 22)))
                g.FillRectangle(back, 0, 0, Width, band);

            string title = Title(s);
            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush dim = new SolidBrush(Dim))
            using (SolidBrush red = new SolidBrush(Red))
            {
                g.DrawString(title, fontBand, ink, new RectangleF(18, 12, 250, 44), ellipsis);
                float x = 280;
                if (s.Recording)
                {
                    g.FillEllipse(red, x, 22, 22, 22);
                    string recording = s.CommandPending ? GoProText.T("ΕΚΚΙΝΗΣΗ…", "STARTING…") : GoProText.T("ΕΓΓΡΑΦΗ ", "REC ") + GoProText.Clock(s.RecordingSeconds);
                    g.DrawString(recording, fontBand, red, new RectangleF(x + 30, 12, 290, 44), ellipsis);
                }
                else
                {
                    g.DrawString(GoProText.T("δεν γράφει", "not recording"), fontBand, dim, new RectangleF(x, 12, 290, 44), ellipsis);
                }

                List<string> parts = new List<string>();
                if (s.BatteryPercent >= 0)
                    parts.Add(s.BatteryPercent.ToString(CultureInfo.InvariantCulture) + " %");

                if (s.PreviewSecondsLeft >= 0)
                    parts.Add(string.Format(CultureInfo.InvariantCulture, GoProText.T("κλείνει σε {0}", "closes in {0}"), MinutesSeconds(s.PreviewSecondsLeft)));

                g.DrawString(string.Join("  ·  ", parts), fontPill, ink, new RectangleF(600, 16, Width - 618, 40), right);
            }

            // The image comes with a short delay: said once, at the bottom.
            using (SolidBrush back = new SolidBrush(Color.FromArgb(150, 8, 16, 22)))
                g.FillRectangle(back, 0, Height - 40, Width, 40);

            using (SolidBrush dim = new SolidBrush(Dim))
                g.DrawString(GoProText.T("Ζωντανή εικόνα (Wi-Fi, ~0,3 s πίσω) · μία κάμερα τη φορά", "Live image (Wi-Fi, ~0.3 s behind) · one camera at a time"),
                    fontTileNote, dim, new RectangleF(18, Height - 37, Width - 36, 34), ellipsis);
        }

        private static string Title(GoProState s)
        {
            return !string.IsNullOrEmpty(s.Alias) ? s.Alias : (s.BleName ?? "GoPro");
        }

        private static string MinutesSeconds(int seconds)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", seconds / 60, seconds % 60);
        }

        private void PaintHeader(Graphics g, GoProState s)
        {
            string title = Title(s);
            string model = s.ModelName ?? "GoPro";
            if (!string.IsNullOrEmpty(s.BleName) && s.BleName != title)
                model += " · " + s.BleName;

            Color linkColor;
            string linkText = LinkText(s, out linkColor);

            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush dim = new SolidBrush(Dim))
            {
                SizeF pill = g.MeasureString(linkText, fontPill);
                float pillWidth = Math.Min(380, pill.Width + 52);
                RectangleF pillRect = new RectangleF(Width - Margin - pillWidth, 22, pillWidth, 48);
                using (GraphicsPath path = Rounded(pillRect, 24))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(52, linkColor)))
                using (Pen border = new Pen(Color.FromArgb(170, linkColor), 2f))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }

                using (SolidBrush dot = new SolidBrush(linkColor))
                    g.FillEllipse(dot, pillRect.X + 16, pillRect.Y + 16, 16, 16);

                g.DrawString(linkText, fontPill, ink, new RectangleF(pillRect.X + 38, pillRect.Y + 7, pillRect.Width - 44, 36), ellipsis);

                g.DrawString(title, fontTitle, ink, new RectangleF(Margin - 2, 12, pillRect.X - Margin - 12, 60), ellipsis);
                g.DrawString(model, fontSub, dim, new RectangleF(Margin, 72, Width - 2 * Margin, 36), ellipsis);
            }
        }

        private string LinkText(GoProState s, out Color color)
        {
            switch (s.Link)
            {
                case GoProLink.Connected:
                    color = Green;
                    return GoProText.T("Συνδεδεμένη", "Connected");
                case GoProLink.Connecting:
                    color = Amber;
                    return GoProText.T("Σύνδεση…", "Connecting…");
                case GoProLink.Pairing:
                    color = Amber;
                    return GoProText.T("Σύζευξη…", "Pairing…");
                case GoProLink.NeedsPairing:
                    color = Amber;
                    return GoProText.T("Θέλει σύζευξη", "Needs pairing");
                case GoProLink.NoBluetooth:
                    color = Red;
                    return GoProText.T("Χωρίς Bluetooth", "No Bluetooth");
                case GoProLink.BluetoothOff:
                    color = Red;
                    return GoProText.T("Bluetooth κλειστό", "Bluetooth off");
                case GoProLink.Searching:
                    color = Dim;
                    return GoProText.T("Αναζήτηση…", "Searching…");
                default:
                    color = Dim;
                    return GoProText.T("Σε αναμονή", "Waiting");
            }
        }

        private void PaintState(Graphics g, GoProState s)
        {
            const float top = 116;
            Color color;
            string state;

            if (s.Recording)
            {
                color = Red;
                state = s.CommandPending ? GoProText.T("ΕΚΚΙΝΗΣΗ…", "STARTING…") : GoProText.T("ΕΓΓΡΑΦΗ", "RECORDING");
            }
            else if (s.Link == GoProLink.Connected)
            {
                color = s.CommandPending ? Amber : Green;
                state = s.CommandPending ? GoProText.T("Σταματά…", "Stopping…") : GoProText.T("Έτοιμη για εγγραφή", "Ready to record");
            }
            else
            {
                color = s.Link == GoProLink.NoBluetooth || s.Link == GoProLink.BluetoothOff ? Red : Amber;
                state = GoProText.T("Δεν ελέγχεται ακόμη", "Not under control yet");
            }

            using (SolidBrush brush = new SolidBrush(color))
            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush dim = new SolidBrush(Dim))
            {
                g.FillEllipse(brush, Margin + 2, top + 24, 40, 40);
                float x = Margin + 60;
                if (s.Recording && !s.CommandPending)
                {
                    g.DrawString(state, fontState, brush, x, top + 6);
                    float timerX = x + g.MeasureString(state, fontState).Width + 8;
                    g.DrawString(GoProText.Clock(s.RecordingSeconds), fontTimer, ink, timerX, top - 4);

                    // How long after the command the camera started (the cameras started together are compared by it).
                    if (!double.IsNaN(s.LastStartOffset) && s.LastStartAt != DateTime.MinValue)
                    {
                        string offset = GoProText.Seconds(s.LastStartOffset);
                        g.DrawString(offset, fontOffset, dim, new RectangleF(Width - Margin - 200, top + 24, 200, 44), right);
                    }
                }
                else
                {
                    DrawFit(g, state, fontState, brush, new RectangleF(x, top + 6, Width - x - Margin, 72), 36);
                }
            }
        }

        private void PaintTiles(Graphics g, GoProState s)
        {
            const float top = 214;
            const float height = 186;
            const float gap = 14;
            float width = (Width - 2 * Margin - 3 * gap) / 4;

            // Battery.
            string battery = s.BatteryPercent >= 0 ? s.BatteryPercent.ToString(CultureInfo.InvariantCulture) + " %" : "–";
            Color batteryColor = s.BatteryPercent >= 0 && s.BatteryPercent <= 15 ? Red : (s.BatteryPercent >= 0 && s.BatteryPercent <= 30 ? Amber : Ink);
            string batteryNote = s.BatteryBars == 4 ? GoProText.T("φορτίζει", "charging") : null;
            Tile(g, Margin, top, width, height, GoProText.T("Μπαταρία", "Battery"), battery, batteryColor, batteryNote, Dim, s.BatteryPercent);

            // Card: the time of recording left (and the space).
            string card = s.RemainingVideoSeconds >= 0 ? GoProText.Duration(s.RemainingVideoSeconds) : "–";
            Color cardColor = Ink;
            string cardNote = s.SdRemainingKB >= 0 ? string.Format(CultureInfo.InvariantCulture, GoProText.T("{0:0} GB ελεύθερα", "{0:0} GB free"), s.SdRemainingKB / 1048576.0) : null;
            Color cardNoteColor = Dim;
            if (s.PrimaryStorage == 1)
            {
                card = GoProText.T("Γεμάτη", "Full");
                cardColor = Red;
            }
            else if (s.PrimaryStorage == 2)
            {
                card = GoProText.T("Χωρίς κάρτα", "No card");
                cardColor = Red;
                cardNote = null;
            }
            else if (s.PrimaryStorage == 3)
            {
                card = GoProText.T("Λάθος", "Error");
                cardColor = Red;
                cardNote = GoProText.T("φορμάρισμα στην κάμερα", "format it in the camera");
            }
            else if (s.PrimaryStorage == 4)
            {
                cardColor = Amber;
                cardNote = GoProText.T("απασχολημένη (USB;)", "busy (USB?)");
                cardNoteColor = Amber;
            }
            else if (s.SdSpeedError)
            {
                cardColor = Amber;
                cardNote = GoProText.T("αργή κάρτα", "slow card");
                cardNoteColor = Amber;
            }
            else if (s.RemainingVideoSeconds >= 0 && s.RemainingVideoSeconds < 15 * 60)
            {
                cardColor = Amber;
            }

            Tile(g, Margin + (width + gap), top, width, height, GoProText.T("Κάρτα", "Card"), card, cardColor, cardNote, cardNoteColor, -1);

            // Temperature.
            string temperature = s.Hot ? GoProText.T("Ζέστη", "Too hot") : (s.Cold ? GoProText.T("Κρύο", "Too cold") : (s.Link == GoProLink.Connected ? GoProText.T("Κανονική", "Normal") : "–"));
            Color temperatureColor = s.Hot ? Red : (s.Cold ? Blue : Ink);
            string temperatureNote = s.Hot ? GoProText.T("σκιά, πορτάκι ανοιχτό", "shade, door open") : null;
            Tile(g, Margin + 2 * (width + gap), top, width, height, GoProText.T("Θερμοκρασία", "Temperature"), temperature, temperatureColor, temperatureNote, Amber, -1);

            // Settings: the race wants Linear, 50 Hz and HyperSmooth off; what is not so is written in amber.
            string video = "–";
            string note = null;
            Color videoColor = Ink;
            Color noteColor = Dim;
            if (s.Resolution >= 0 || s.Fps >= 0)
            {
                int f = GoProIds.Fps(s.Fps);
                string resolution = GoProIds.ResolutionName(s.Resolution) ?? "?";
                video = resolution + " · " + (f > 0 ? f.ToString(CultureInfo.InvariantCulture) : "?");

                List<string> wrong = new List<string>();
                if (s.Lens >= 0 && s.Lens != 4)
                    wrong.Add(GoProIds.LensName(s.Lens));

                if (s.AntiFlicker >= 0 && !GoProIds.AntiFlicker50(s.AntiFlicker))
                    wrong.Add(GoProIds.AntiFlickerName(s.AntiFlicker));

                if (s.Hypersmooth >= 0 && s.Hypersmooth != 0)
                    wrong.Add("HS " + GoProIds.HypersmoothName(s.Hypersmooth));

                wrong.RemoveAll(string.IsNullOrEmpty);
                GoProPreset matching = GoProPresets.Matching(s);
                if (wrong.Count > 0)
                {
                    videoColor = Amber;
                    noteColor = Amber;
                    note = string.Join(" · ", wrong);
                }
                else if (matching != null)
                {
                    // One of the sets (race, technique): said in green.
                    note = matching.Short + " ✓";
                    noteColor = Green;
                }
                else
                {
                    note = s.Lens == 4 ? "Linear · 50 Hz" : null;
                }
            }

            Tile(g, Margin + 3 * (width + gap), top, width, height, GoProText.T("Ρυθμίσεις", "Settings"), video, videoColor, note, noteColor, -1);
        }

        private void Tile(Graphics g, float x, float y, float w, float h, string title, string value, Color valueColor, string note, Color noteColor, int percent)
        {
            RectangleF rect = new RectangleF(x, y, w, h);
            using (GraphicsPath path = Rounded(rect, 16))
            using (SolidBrush fill = new SolidBrush(TileColor))
                g.FillPath(fill, path);

            using (SolidBrush dim = new SolidBrush(Dim))
            using (SolidBrush brush = new SolidBrush(valueColor))
            using (SolidBrush noteBrush = new SolidBrush(noteColor))
            {
                g.DrawString(title, fontTileTitle, dim, new RectangleF(x + 16, y + 12, w - 28, 36), ellipsis);
                DrawFit(g, value, fontTileValue, brush, new RectangleF(x + 14, y + 52, w - 24, 62), 26);
                if (!string.IsNullOrEmpty(note))
                    DrawFit(g, note, fontTileNote, noteBrush, new RectangleF(x + 16, y + 120, w - 26, 36), 18);
            }

            if (percent >= 0)
            {
                RectangleF bar = new RectangleF(x + 16, y + h - 24, w - 32, 10);
                using (SolidBrush back = new SolidBrush(Color.FromArgb(60, Dim)))
                using (SolidBrush front = new SolidBrush(valueColor))
                {
                    g.FillRectangle(back, bar);
                    g.FillRectangle(front, bar.X, bar.Y, bar.Width * Math.Min(100, percent) / 100f, bar.Height);
                }
            }
        }

        private void PaintFooter(Graphics g, GoProState s)
        {
            RectangleF rect = new RectangleF(Margin, 412, Width - 2 * Margin, Height - 412 - 10);
            if (!string.IsNullOrEmpty(s.Problem))
            {
                using (SolidBrush amber = new SolidBrush(Amber))
                    g.DrawString("⚠ " + s.Problem, fontHint, amber, rect, wrap);

                return;
            }

            // How an action went (a set of settings): green when it went, amber when something did not.
            if (!string.IsNullOrEmpty(s.Notice))
            {
                using (SolidBrush brush = new SolidBrush(s.NoticeError ? Amber : Green))
                    g.DrawString((s.NoticeError ? "⚠ " : "") + s.Notice, fontHint, brush, rect, wrap);

                return;
            }

            List<string> lines = new List<string>();
            if (s.Link == GoProLink.Connected)
            {
                string clock = s.ClockSetAt != DateTime.MinValue ?
                    string.Format(CultureInfo.InvariantCulture, GoProText.T("Ώρα από τον υπολογιστή {0:HH:mm:ss}", "Clock from the computer {0:HH:mm:ss}"), s.ClockSetAt) : null;
                string wifi = s.WifiOn ? GoProText.T("Wi-Fi ανοιχτό", "Wi-Fi on") : GoProText.T("Wi-Fi κλειστό", "Wi-Fi off");
                lines.Add(clock != null ? clock + " · " + wifi : wifi);

                if (!s.Recording && !double.IsNaN(s.LastStartOffset) && s.LastStartAt != DateTime.MinValue)
                {
                    lines.Add(string.Format(CultureInfo.InvariantCulture, GoProText.T("Τελευταία έναρξη {0:HH:mm:ss}, {1} μετά την εντολή.", "Last start {0:HH:mm:ss}, {1} after the command."),
                        s.LastStartAt, GoProText.Seconds(s.LastStartOffset)));
                }
                else
                {
                    lines.Add(GoProText.T("Ακριβής συγχρονισμός: από τον ήχο, όταν ανοίγεις τα βίντεο.", "Exact synchronisation: by the sound, when the videos are opened."));
                }
            }
            else if (s.Rssi != 0)
            {
                string quality = s.Rssi >= -70 ? GoProText.T("καλό", "good") : (s.Rssi >= -85 ? GoProText.T("μέτριο", "fair") : GoProText.T("ασθενές", "weak"));
                lines.Add(string.Format(CultureInfo.InvariantCulture, GoProText.T("Σήμα Bluetooth {0} dBm ({1}).", "Bluetooth signal {0} dBm ({1})."), s.Rssi, quality));
            }

            using (SolidBrush dim = new SolidBrush(Dim))
                g.DrawString(string.Join("\n", lines), fontFooter, dim, rect, wrap);
        }

        /// <summary>The text in its box, smaller when it does not fit (down to `minSize` pixels, then cut with an ellipsis).</summary>
        private void DrawFit(Graphics g, string text, Font font, Brush brush, RectangleF rect, float minSize)
        {
            SizeF size = g.MeasureString(text, font);
            if (size.Width <= rect.Width)
            {
                g.DrawString(text, font, brush, rect, ellipsis);
                return;
            }

            float target = Math.Max(minSize, (float)Math.Floor(font.Size * rect.Width / size.Width));
            using (Font smaller = new Font(font.FontFamily, target, font.Style, GraphicsUnit.Pixel))
            {
                // Smaller text keeps its middle on the line of the bigger one.
                float shift = (font.Size - target) * 0.45f;
                g.DrawString(text, smaller, brush, new RectangleF(rect.X, rect.Y + shift, rect.Width, rect.Height - shift), ellipsis);
            }
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
