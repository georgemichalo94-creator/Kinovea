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
    /// card, temperature and settings, and what to do when something is wrong. One painter per screen (the objects of drawing are not
    /// shared between threads).
    /// </summary>
    internal sealed class GoProStatusPainter : IDisposable
    {
        public const int Width = 960;
        public const int Height = 540;

        private static readonly Color Background = Color.FromArgb(14, 26, 34);
        private static readonly Color TileColor = Color.FromArgb(24, 46, 56);
        private static readonly Color Ink = Color.FromArgb(232, 241, 244);
        private static readonly Color Dim = Color.FromArgb(147, 172, 182);
        private static readonly Color Red = Color.FromArgb(255, 82, 72);
        private static readonly Color Green = Color.FromArgb(76, 210, 132);
        private static readonly Color Amber = Color.FromArgb(255, 180, 77);
        private static readonly Color Blue = Color.FromArgb(110, 180, 255);

        private readonly Font fontTitle = new Font("Segoe UI Semibold", 30f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontSub = new Font("Segoe UI", 17f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontPill = new Font("Segoe UI Semibold", 17f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontState = new Font("Segoe UI Semibold", 44f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontTimer = new Font("Segoe UI Semibold", 64f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontTileTitle = new Font("Segoe UI", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontTileValue = new Font("Segoe UI Semibold", 30f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontTileNote = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font fontHint = new Font("Segoe UI Semibold", 17f, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly StringFormat ellipsis = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
        private readonly StringFormat wrap = new StringFormat() { Trimming = StringTrimming.EllipsisWord };

        public void Dispose()
        {
            foreach (Font f in new Font[] { fontTitle, fontSub, fontPill, fontState, fontTimer, fontTileTitle, fontTileValue, fontTileNote, fontHint })
                f.Dispose();

            ellipsis.Dispose();
            wrap.Dispose();
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
        /// and that the image comes with a short delay.
        /// </summary>
        public void PaintOverlay(Graphics g, GoProState s)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using (SolidBrush band = new SolidBrush(Color.FromArgb(170, 8, 16, 22)))
                g.FillRectangle(band, 0, 0, Width, 52);

            string title = !string.IsNullOrEmpty(s.Alias) ? s.Alias : (s.BleName ?? "GoPro");
            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush dim = new SolidBrush(Dim))
            using (SolidBrush red = new SolidBrush(Red))
            {
                g.DrawString(title, fontPill, ink, new RectangleF(14, 13, 220, 26), ellipsis);
                float x = 240;
                if (s.Recording)
                {
                    g.FillEllipse(red, x, 17, 16, 16);
                    string recording = (s.CommandPending ? GoProText.T("ΕΚΚΙΝΗΣΗ…", "STARTING…") : GoProText.T("ΕΓΓΡΑΦΗ ", "REC ") + GoProText.Clock(s.RecordingSeconds));
                    g.DrawString(recording, fontPill, red, x + 22, 13);
                }
                else
                {
                    g.DrawString(GoProText.T("δεν γράφει", "not recording"), fontPill, dim, x, 13);
                }

                List<string> parts = new List<string>();
                if (s.BatteryPercent >= 0)
                    parts.Add("🔋 " + s.BatteryPercent.ToString(CultureInfo.InvariantCulture) + " %");

                if (s.RemainingVideoSeconds >= 0)
                    parts.Add(GoProText.Duration(s.RemainingVideoSeconds));

                if (s.Hot)
                    parts.Add(GoProText.T("⚠ ζέστη", "⚠ hot"));

                parts.Add(GoProText.T("👁 ζωντανά (~0,3 s πίσω)", "👁 live (~0.3 s behind)"));
                StringFormat right = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter };
                using (right)
                    g.DrawString(string.Join("   ", parts), fontSub, ink, new RectangleF(520, 15, Width - 534, 26), right);
            }
        }

        private void PaintHeader(Graphics g, GoProState s)
        {
            string title = !string.IsNullOrEmpty(s.Alias) ? s.Alias : (s.BleName ?? "GoPro");
            string model = s.ModelName ?? "GoPro";
            if (!string.IsNullOrEmpty(s.BleName) && s.BleName != title)
                model += " · " + s.BleName;

            Color linkColor;
            string linkText = LinkText(s, out linkColor);

            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush dim = new SolidBrush(Dim))
            {
                SizeF pill = g.MeasureString(linkText, fontPill);
                float pillWidth = pill.Width + 44;
                RectangleF pillRect = new RectangleF(Width - 32 - pillWidth, 26, pillWidth, 38);
                using (GraphicsPath path = Rounded(pillRect, 19))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(48, linkColor)))
                using (Pen border = new Pen(Color.FromArgb(160, linkColor), 1.5f))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }

                using (SolidBrush dot = new SolidBrush(linkColor))
                    g.FillEllipse(dot, pillRect.X + 14, pillRect.Y + 13, 12, 12);

                g.DrawString(linkText, fontPill, ink, pillRect.X + 32, pillRect.Y + 7);

                RectangleF titleRect = new RectangleF(32, 18, pillRect.X - 48, 44);
                g.DrawString(title, fontTitle, ink, titleRect, ellipsis);
                RectangleF modelRect = new RectangleF(34, 66, Width - 68, 26);
                g.DrawString(model + " · Bluetooth", fontSub, dim, modelRect, ellipsis);
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
            float top = 116;
            Color color;
            string state;
            string detail = null;

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

            if (!double.IsNaN(s.LastStartOffset) && s.LastStartAt != DateTime.MinValue)
            {
                detail = string.Format(CultureInfo.InvariantCulture, GoProText.T("Τελευταία έναρξη {0:HH:mm:ss} · {1} από την εντολή", "Last start {0:HH:mm:ss} · {1} after the command"),
                    s.LastStartAt, GoProText.Seconds(s.LastStartOffset));
            }

            using (SolidBrush brush = new SolidBrush(color))
            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush dim = new SolidBrush(Dim))
            {
                g.FillEllipse(brush, 36, top + 22, 34, 34);
                if (s.Recording && !s.CommandPending)
                {
                    g.DrawString(state, fontState, brush, 88, top + 6);
                    SizeF size = g.MeasureString(state, fontState);
                    g.DrawString(GoProText.Clock(s.RecordingSeconds), fontTimer, ink, 88 + size.Width + 16, top - 8);
                }
                else
                {
                    g.DrawString(state, fontState, brush, new RectangleF(88, top + 6, Width - 120, 64), ellipsis);
                }

                if (detail != null)
                    g.DrawString(detail, fontSub, dim, new RectangleF(90, top + 80, Width - 122, 26), ellipsis);
            }
        }

        private void PaintTiles(Graphics g, GoProState s)
        {
            float top = 250;
            float height = 150;
            float gap = 16;
            float width = (Width - 64 - 3 * gap) / 4;

            // Battery.
            string battery = s.BatteryPercent >= 0 ? s.BatteryPercent.ToString(CultureInfo.InvariantCulture) + " %" : "–";
            Color batteryColor = s.BatteryPercent >= 0 && s.BatteryPercent <= 15 ? Red : (s.BatteryPercent >= 0 && s.BatteryPercent <= 30 ? Amber : Ink);
            string batteryNote = s.BatteryBars == 4 ? GoProText.T("φορτίζει", "charging") : null;
            Tile(g, 32, top, width, height, GoProText.T("Μπαταρία", "Battery"), battery, batteryColor, batteryNote, s.BatteryPercent);

            // Card.
            string card = s.RemainingVideoSeconds >= 0 ? GoProText.Duration(s.RemainingVideoSeconds) : "–";
            Color cardColor = Ink;
            string cardNote = s.SdRemainingKB >= 0 ? string.Format(CultureInfo.InvariantCulture, GoProText.T("{0:0.0} GB ελεύθερα", "{0:0.0} GB free"), s.SdRemainingKB / 1048576.0) : null;
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
                card = GoProText.T("Λάθος κάρτας", "Card error");
                cardColor = Red;
            }
            else if (s.SdSpeedError)
            {
                cardColor = Amber;
                cardNote = GoProText.T("αργή κάρτα", "slow card");
            }
            else if (s.RemainingVideoSeconds >= 0 && s.RemainingVideoSeconds < 15 * 60)
            {
                cardColor = Amber;
            }

            Tile(g, 32 + (width + gap), top, width, height, GoProText.T("Κάρτα (ώρα εγγραφής)", "Card (recording time)"), card, cardColor, cardNote, -1);

            // Temperature.
            string temperature = s.Hot ? GoProText.T("Ζεσταίνεται", "Too hot") : (s.Cold ? GoProText.T("Κρύο", "Too cold") : (s.Link == GoProLink.Connected ? GoProText.T("Κανονική", "Normal") : "–"));
            Color temperatureColor = s.Hot ? Red : (s.Cold ? Blue : Ink);
            string temperatureNote = s.Hot ? GoProText.T("σκιά, πορτάκι ανοιχτό", "shade, door open") : null;
            Tile(g, 32 + 2 * (width + gap), top, width, height, GoProText.T("Θερμοκρασία", "Temperature"), temperature, temperatureColor, temperatureNote, -1);

            // Settings: the race preset wants Linear, 50 Hz and HyperSmooth off.
            string video = "–";
            string note = null;
            Color videoColor = Ink;
            if (s.Resolution >= 0 || s.Fps >= 0)
            {
                int f = GoProIds.Fps(s.Fps);
                video = (GoProIds.ResolutionName(s.Resolution) ?? "?") + " · " + (f > 0 ? f.ToString(CultureInfo.InvariantCulture) : "?");
                List<string> parts = new List<string>();
                string lens = GoProIds.LensName(s.Lens);
                if (lens != null)
                    parts.Add(lens);

                string flicker = GoProIds.AntiFlickerName(s.AntiFlicker);
                if (flicker != null)
                    parts.Add(flicker);

                string hs = GoProIds.HypersmoothName(s.Hypersmooth);
                if (hs != null)
                    parts.Add("HS " + hs);

                note = string.Join(" · ", parts);
                bool linear = s.Lens < 0 || s.Lens == 4;
                bool fifty = s.AntiFlicker < 0 || GoProIds.AntiFlicker50(s.AntiFlicker);
                bool steady = s.Hypersmooth < 0 || s.Hypersmooth == 0;
                if (!linear || !fifty || !steady)
                    videoColor = Amber;
            }

            Tile(g, 32 + 3 * (width + gap), top, width, height, GoProText.T("Ρυθμίσεις", "Settings"), video, videoColor, note, -1);
        }

        private void Tile(Graphics g, float x, float y, float w, float h, string title, string value, Color valueColor, string note, int percent)
        {
            RectangleF rect = new RectangleF(x, y, w, h);
            using (GraphicsPath path = Rounded(rect, 14))
            using (SolidBrush fill = new SolidBrush(TileColor))
                g.FillPath(fill, path);

            using (SolidBrush dim = new SolidBrush(Dim))
            using (SolidBrush brush = new SolidBrush(valueColor))
            {
                g.DrawString(title, fontTileTitle, dim, new RectangleF(x + 16, y + 14, w - 28, 22), ellipsis);
                g.DrawString(value, fontTileValue, brush, new RectangleF(x + 14, y + 44, w - 24, 44), ellipsis);
                if (!string.IsNullOrEmpty(note))
                    g.DrawString(note, fontTileNote, dim, new RectangleF(x + 16, y + 94, w - 28, 44), wrap);
            }

            if (percent >= 0)
            {
                RectangleF bar = new RectangleF(x + 16, y + h - 22, w - 32, 8);
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
            RectangleF rect = new RectangleF(32, 420, Width - 64, 104);
            if (!string.IsNullOrEmpty(s.Problem))
            {
                using (SolidBrush amber = new SolidBrush(Amber))
                    g.DrawString("⚠ " + s.Problem, fontHint, amber, rect, wrap);

                return;
            }

            List<string> lines = new List<string>();
            if (s.ClockSetAt != DateTime.MinValue)
                lines.Add(string.Format(CultureInfo.InvariantCulture, GoProText.T("Ώρα από τον υπολογιστή στις {0:HH:mm:ss}.", "Clock from the computer at {0:HH:mm:ss}."), s.ClockSetAt));

            if (s.Link == GoProLink.Connected)
            {
                lines.Add(s.WifiOn ? GoProText.T("Wi-Fi της κάμερας ανοιχτό.", "Camera Wi-Fi on.") : GoProText.T("Wi-Fi της κάμερας κλειστό (λιγότερη ζέστη και μπαταρία).", "Camera Wi-Fi off (less heat and battery)."));
                lines.Add(GoProText.T("Ο ακριβής συγχρονισμός γίνεται από τον ήχο, όταν ανοίγεις τα βίντεο.", "The exact synchronisation is made by the sound when the videos are opened."));
            }
            else if (s.Rssi != 0)
            {
                string quality = s.Rssi >= -70 ? GoProText.T("καλό", "good") : (s.Rssi >= -85 ? GoProText.T("μέτριο", "fair") : GoProText.T("ασθενές", "weak"));
                lines.Add(string.Format(CultureInfo.InvariantCulture, GoProText.T("Σήμα {0} dBm ({1}).", "Signal {0} dBm ({1})."), s.Rssi, quality));
            }

            using (SolidBrush dim = new SolidBrush(Dim))
                g.DrawString(string.Join("\n", lines), fontSub, dim, rect, wrap);
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
