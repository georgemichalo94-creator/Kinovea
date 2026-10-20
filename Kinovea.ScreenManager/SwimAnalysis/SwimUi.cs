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
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Colors and fonts of the swim analysis panel and of the video overlay. Since 2.9.0 a holographic "HUD" style: deep navy glass,
    /// thin cyan lines with a glow, corner brackets, cut corners and spaced capitals; the swimmer amber, the reference cyan, the
    /// ΔΑΚ amber, faster green, slower red (as before).
    /// </summary>
    internal static class SwimTheme
    {
        public static readonly Color Back = Color.FromArgb(3, 14, 23);
        public static readonly Color Surface = Color.FromArgb(5, 22, 34);
        public static readonly Color Raised = Color.FromArgb(8, 32, 47);
        public static readonly Color RaisedHover = Color.FromArgb(12, 47, 66);
        public static readonly Color Pressed = Color.FromArgb(4, 17, 27);
        public static readonly Color Border = Color.FromArgb(19, 74, 92);
        public static readonly Color Input = Color.FromArgb(2, 10, 17);
        public static readonly Color Text = Color.FromArgb(234, 252, 255);
        public static readonly Color Dim = Color.FromArgb(124, 200, 214);
        public static readonly Color Faint = Color.FromArgb(62, 112, 128);
        public static readonly Color Accent = Color.FromArgb(56, 225, 255);
        public static readonly Color AccentLight = Color.FromArgb(143, 243, 255);
        public static readonly Color AccentPale = Color.FromArgb(201, 251, 255);
        public static readonly Color AccentDark = Color.FromArgb(8, 76, 99);
        public static readonly Color Amber = Color.FromArgb(255, 190, 92);
        public static readonly Color AmberDark = Color.FromArgb(70, 48, 16);
        public static readonly Color Green = Color.FromArgb(61, 235, 160);
        public static readonly Color Red = Color.FromArgb(255, 104, 104);
        public static readonly Color Warn = Color.FromArgb(255, 186, 60);
        public static readonly Color GridLine = Color.FromArgb(14, 50, 64);
        public static readonly Color RowAlt = Color.FromArgb(4, 19, 30);
        public static readonly Color NextRow = Color.FromArgb(58, 46, 18);
        public static readonly Color PhaseCell = Color.FromArgb(58, 40, 20);

        private static string displayFamily;
        private static string lightFamily;
        private static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        /// <summary>Bahnschrift (Windows 10 and later) for the numbers and the titles, Segoe UI otherwise.</summary>
        public static string DisplayFamily
        {
            get
            {
                if (displayFamily == null)
                {
                    displayFamily = "Segoe UI Semibold";
                    foreach (string name in new string[] { "Bahnschrift SemiBold", "Bahnschrift" })
                    {
                        try
                        {
                            using (FontFamily family = new FontFamily(name))
                            {
                                if (!family.IsStyleAvailable(FontStyle.Regular))
                                    continue;

                                displayFamily = name;
                                break;
                            }
                        }
                        catch (ArgumentException)
                        {
                            // Not installed.
                        }
                    }
                }

                return displayFamily;
            }
        }

        public static Font Display(float size)
        {
            return Get(DisplayFamily, size, FontStyle.Regular);
        }

        /// <summary>Thin figures of the big values of the HUD (2.9.0): Bahnschrift Light, else Segoe UI Light.</summary>
        public static string LightFamily
        {
            get
            {
                if (lightFamily == null)
                {
                    lightFamily = DisplayFamily;
                    foreach (string name in new string[] { "Bahnschrift Light", "Bahnschrift SemiLight", "Segoe UI Light" })
                    {
                        try
                        {
                            using (FontFamily family = new FontFamily(name))
                            {
                                if (!family.IsStyleAvailable(FontStyle.Regular))
                                    continue;

                                lightFamily = name;
                                break;
                            }
                        }
                        catch (ArgumentException)
                        {
                            // Not installed.
                        }
                    }
                }

                return lightFamily;
            }
        }

        public static Font DisplayLight(float size)
        {
            return Get(LightFamily, size, FontStyle.Regular);
        }

        public static Font Ui(float size, FontStyle style)
        {
            return Get("Segoe UI", size, style);
        }

        private static Font Get(string family, float size, FontStyle style)
        {
            string key = family + "|" + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (int)style;
            Font font;
            if (!fonts.TryGetValue(key, out font))
            {
                try
                {
                    font = new Font(family, size, style);
                }
                catch (ArgumentException)
                {
                    // The family does not have this style.
                    font = new Font(FontFamily.GenericSansSerif, size, style);
                }

                fonts[key] = font;
            }

            return font;
        }

        /// <summary>
        /// The shape of the cards, buttons and chips. Since 2.9.0 (HUD) the top left and bottom right corners are cut, by about the
        /// radius asked (at most a third of the width, less than half the height); 2 px or less: a rectangle.
        /// </summary>
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float c = Math.Min(radius * 1.15f, Math.Min(r.Width / 3f, r.Height / 2.2f));
            if (c <= 2f)
            {
                path.AddRectangle(r);
                return path;
            }

            path.AddPolygon(new[]
            {
                new PointF(r.Left + c, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom - c),
                new PointF(r.Right - c, r.Bottom), new PointF(r.Left, r.Bottom), new PointF(r.Left, r.Top + c)
            });
            return path;
        }

        public static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
        {
            using (GraphicsPath path = Rounded(r, radius))
            using (SolidBrush brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public static void DrawRounded(Graphics g, Color color, RectangleF r, float radius)
        {
            using (GraphicsPath path = Rounded(r, radius))
            using (Pen pen = new Pen(color))
                g.DrawPath(pen, path);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        #region HUD (2.9.0)
        /// <summary>The color with its alpha times 'alpha' (0–1).</summary>
        public static Color Fade(Color c, float alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, (int)Math.Round(c.A * Math.Max(0f, Math.Min(1f, alpha))))), c.R, c.G, c.B);
        }

        /// <summary>
        /// A glass panel of the HUD: deep navy and see-through (the video under it), a thin cyan border, faint scan lines and glowing
        /// corner brackets in the accent. alpha: 1 as designed, less when the swimmer is under it (he stays in front).
        /// </summary>
        public static void HudPanel(Graphics g, RectangleF r, Color accent, float alpha = 1f, bool scanLines = true)
        {
            if (r.Width < 6 || r.Height < 6 || alpha <= 0.01f)
                return;

            SmoothingMode mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            using (LinearGradientBrush back = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                Fade(Color.FromArgb(158, 4, 30, 48), alpha), Fade(Color.FromArgb(184, 2, 16, 28), alpha), LinearGradientMode.Vertical))
                g.FillRectangle(back, r);

            if (scanLines)
            {
                using (Pen scan = new Pen(Fade(Color.FromArgb(11, 143, 243, 255), alpha), 1f))
                {
                    for (float y = r.Top + 2; y < r.Bottom - 1; y += 4)
                        g.DrawLine(scan, r.Left + 1, y, r.Right - 1, y);
                }
            }

            using (Pen border = new Pen(Fade(Color.FromArgb(72, 56, 225, 255), alpha), 1f))
                g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height);

            g.SmoothingMode = mode;
            Brackets(g, r, accent, alpha);
        }

        /// <summary>The corner brackets of a panel of the HUD, glowing, in a color.</summary>
        public static void Brackets(Graphics g, RectangleF r, Color color, float alpha = 1f)
        {
            float l = Math.Max(6f, Math.Min(22f, Math.Min(r.Width, r.Height) / 5f));
            using (Pen glow = new Pen(Fade(Color.FromArgb(70, color), alpha), 4.5f))
            using (Pen pen = new Pen(Fade(Color.FromArgb(240, color), alpha), 1.7f))
            {
                foreach (Pen p in new[] { glow, pen })
                {
                    g.DrawLines(p, new[] { new PointF(r.Left, r.Top + l), new PointF(r.Left, r.Top), new PointF(r.Left + l, r.Top) });
                    g.DrawLines(p, new[] { new PointF(r.Right - l, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Top + l) });
                    g.DrawLines(p, new[] { new PointF(r.Left, r.Bottom - l), new PointF(r.Left, r.Bottom), new PointF(r.Left + l, r.Bottom) });
                    g.DrawLines(p, new[] { new PointF(r.Right - l, r.Bottom), new PointF(r.Right, r.Bottom), new PointF(r.Right, r.Bottom - l) });
                }
            }
        }

        /// <summary>A line with a soft glow under it (the lines of the charts of the HUD).</summary>
        public static void GlowLine(Graphics g, Color color, float width, PointF a, PointF b, float alpha = 1f)
        {
            using (Pen glow = new Pen(Fade(Color.FromArgb(55, color), alpha), width * 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen pen = new Pen(Fade(color, alpha), width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(glow, a, b);
                g.DrawLine(pen, a, b);
            }
        }

        /// <summary>
        /// Text with a glow (the big values of the HUD), placed as DrawString places it at a point. strength: of the glow (0: none).
        /// </summary>
        public static void GlowText(Graphics g, string text, Font font, Color color, Color glow, PointF at, float strength = 1f)
        {
            if (string.IsNullOrEmpty(text))
                return;

            SmoothingMode mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float em = font.SizeInPoints * g.DpiY / 72f;
            using (GraphicsPath path = new GraphicsPath())
            using (StringFormat format = new StringFormat(StringFormat.GenericDefault))
            {
                path.AddString(text, font.FontFamily, (int)font.Style, em, at, format);
                if (strength > 0.01f)
                {
                    using (Pen wide = new Pen(Fade(Color.FromArgb(34, glow), strength), Math.Max(2f, em * 0.20f)) { LineJoin = LineJoin.Round })
                    using (Pen near = new Pen(Fade(Color.FromArgb(70, glow), strength), Math.Max(1f, em * 0.08f)) { LineJoin = LineJoin.Round })
                    {
                        g.DrawPath(wide, path);
                        g.DrawPath(near, path);
                    }
                }

                using (SolidBrush brush = new SolidBrush(color))
                    g.FillPath(brush, path);
            }

            g.SmoothingMode = mode;
        }

        /// <summary>
        /// Spaced capitals (the labels of the HUD) from a point, 'tracking' of the size of the font between the letters. Returns the
        /// right end of the text.
        /// </summary>
        public static float Tracked(Graphics g, string text, Font font, Brush brush, float x, float y, float tracking = 0.14f)
        {
            if (string.IsNullOrEmpty(text))
                return x;

            float em = font.SizeInPoints * g.DpiY / 72f;
            float cx = x;
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces })
            {
                foreach (char ch in text)
                {
                    string s = ch.ToString();
                    g.DrawString(s, font, brush, cx, y, format);
                    cx += g.MeasureString(s, font, PointF.Empty, format).Width + tracking * em;
                }
            }

            return cx - tracking * em;
        }

        /// <summary>Width of spaced capitals (see Tracked).</summary>
        public static float TrackedWidth(Graphics g, string text, Font font, float tracking = 0.14f)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            float em = font.SizeInPoints * g.DpiY / 72f;
            float w = 0;
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces })
            {
                foreach (char ch in text)
                    w += g.MeasureString(ch.ToString(), font, PointF.Empty, format).Width + tracking * em;
            }

            return w - tracking * em;
        }

        /// <summary>
        /// Spaced capitals with the text of the controls (TextRenderer, sharp on an opaque background) in a box, from its left (or its
        /// right), vertically centred; cut with "…" when they do not fit. Returns the width drawn.
        /// </summary>
        public static int TrackedText(Graphics g, string text, Font font, Color color, Rectangle box, float tracking = 0.12f, bool right = false)
        {
            if (string.IsNullOrEmpty(text) || box.Width < 2 || box.Height < 2)
                return 0;

            int space = Math.Max(1, (int)Math.Round(font.SizeInPoints * g.DpiY / 72f * tracking));
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            List<int> widths = text.Select(ch => TextRenderer.MeasureText(g, ch.ToString(), font, Size.Empty, flags).Width).ToList();
            int total = widths.Sum() + space * (text.Length - 1);
            if (total > box.Width)
            {
                // Too long: as one string, cut.
                TextRenderer.DrawText(g, text, font, box, color, flags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    (right ? TextFormatFlags.Right : TextFormatFlags.Left));
                return box.Width;
            }

            int height = TextRenderer.MeasureText(g, text, font, Size.Empty, flags).Height;
            int x = right ? box.Right - total : box.Left;
            int y = box.Top + (box.Height - height) / 2;
            for (int i = 0; i < text.Length; i++)
            {
                TextRenderer.DrawText(g, text[i].ToString(), font, new Point(x, y), color, flags);
                x += widths[i] + space;
            }

            return total;
        }
        #endregion

        /// <summary>Opaque background behind a control (the first ancestor with a solid color).</summary>
        public static Color BackOf(Control c)
        {
            Control p = c.Parent;
            while (p != null)
            {
                if (p.BackColor.A == 255)
                    return p.BackColor;
                p = p.Parent;
            }

            return Back;
        }

        /// <summary>
        /// Dark colors for the standard controls of a container (text boxes, lists, combo boxes, check boxes, grids).
        /// </summary>
        public static void Apply(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is TextBoxBase)
                {
                    c.BackColor = Input;
                    c.ForeColor = Text;
                    TextBox tb = c as TextBox;
                    if (tb != null)
                        tb.BorderStyle = BorderStyle.FixedSingle;
                    RichTextBox rtb = c as RichTextBox;
                    if (rtb != null)
                        rtb.BorderStyle = BorderStyle.None;
                }
                else if (c is ComboBox)
                {
                    ComboBox combo = (ComboBox)c;
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.BackColor = Input;
                    combo.ForeColor = Text;
                }
                else if (c is ListBox)
                {
                    c.BackColor = Input;
                    c.ForeColor = Text;
                    ((ListBox)c).BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is NumericUpDown)
                {
                    c.BackColor = Input;
                    c.ForeColor = Text;
                    ((NumericUpDown)c).BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is CheckBox)
                {
                    ((CheckBox)c).FlatStyle = FlatStyle.Flat;
                    ((CheckBox)c).FlatAppearance.BorderColor = Border;
                    ((CheckBox)c).FlatAppearance.CheckedBackColor = AccentDark;
                }
                else if (c is DataGridView)
                {
                    StyleGrid((DataGridView)c);
                }

                if (c.HasChildren && !(c is DataGridView) && !(c is NumericUpDown))
                    Apply(c);
            }
        }

        public static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Surface;
            g.BorderStyle = BorderStyle.None;
            g.GridColor = GridLine;
            g.EnableHeadersVisualStyles = false;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersDefaultCellStyle.BackColor = Raised;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Dim;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Raised;
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Dim;
            g.ColumnHeadersDefaultCellStyle.Font = Ui(8f, FontStyle.Bold);
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(2, 4, 2, 4);
            g.DefaultCellStyle.BackColor = Surface;
            g.DefaultCellStyle.ForeColor = Text;
            g.DefaultCellStyle.SelectionBackColor = AccentDark;
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.AlternatingRowsDefaultCellStyle.BackColor = RowAlt;
            g.RowHeadersVisible = false;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Dark title bar on Windows 10 (2004) and Windows 11. Ignored elsewhere.</summary>
        public static void DarkTitleBar(IntPtr handle)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref on, sizeof(int));
            }
            catch (Exception)
            {
                // Older Windows: the standard title bar stays.
            }
        }
    }

    /// <summary>
    /// Flat button with rounded corners and the key of its command in a small badge. It never takes the keyboard
    /// focus, so the hotkeys keep working after a click.
    /// </summary>
    internal class SwimButton : Button
    {
        private bool hover;
        private bool pressed;
        private string key = "";
        private bool highlighted;
        private Color accent = SwimTheme.Accent;
        private bool primary;

        public SwimButton() : this(false)
        {
        }

        /// <summary>
        /// The buttons of the panel never take the focus (the keys stay with the video); those of a dialog do,
        /// so that Enter and Esc click them.
        /// </summary>
        public SwimButton(bool selectable)
        {
            SetStyle(ControlStyles.Selectable, selectable);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = selectable;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = SwimTheme.Raised;
            ForeColor = SwimTheme.Text;
            Font = SwimTheme.Ui(8.5f, FontStyle.Bold);
            Cursor = Cursors.Hand;
        }

        /// <summary>A click puts in what is being typed in the window first (see OnMouseDown); not for «Αναίρεση».</summary>
        public bool CommitsTyping = true;

        /// <summary>Hotkey shown in the badge ("S", "Backspace"), empty for none.</summary>
        public string Key
        {
            get { return key; }
            set { key = value ?? ""; Invalidate(); }
        }

        /// <summary>Waiting for this button's action (a click on the video, a calibration).</summary>
        public bool Highlighted
        {
            get { return highlighted; }
            set { if (highlighted != value) { highlighted = value; Invalidate(); } }
        }

        public Color Accent
        {
            get { return accent; }
            set { accent = value; Invalidate(); }
        }

        /// <summary>Filled with the accent color (main action).</summary>
        public bool Primary
        {
            get { return primary; }
            set { primary = value; Invalidate(); }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text ?? "", Font);
            int badge = string.IsNullOrEmpty(key) ? 0 : TextRenderer.MeasureText(key, SwimTheme.Ui(7f, FontStyle.Bold)).Width + 12;
            return new Size(text.Width + badge + 22, Math.Max(26, text.Height + 10));
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();

                // A button of the panel does not take the focus: what is being typed in the window (the official times) counts before
                // it acts (since 2026-10-19), except for the buttons that would undo it (CommitsTyping false).
                FormSwimAnalysis form = GetStyle(ControlStyles.Selectable) || !CommitsTyping ? null : FindForm() as FormSwimAnalysis;
                if (form != null)
                    form.CommitTyping();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Invalidate();
            base.OnTextChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(SwimTheme.BackOf(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill = primary ? SwimTheme.AccentDark : SwimTheme.Raised;
            if (highlighted)
                fill = SwimTheme.AmberDark;
            else if (pressed)
                fill = SwimTheme.Pressed;
            else if (hover && Enabled)
                fill = primary ? SwimTheme.Mix(SwimTheme.AccentDark, accent, 0.25) : SwimTheme.RaisedHover;

            // HUD (2.9.0): cut corners, a thin line in the accent that glows on the main and the hovered buttons.
            Color border = highlighted ? SwimTheme.Amber : (hover && Enabled ? SwimTheme.Mix(SwimTheme.Border, accent, 0.75) :
                primary && Enabled ? SwimTheme.Mix(SwimTheme.Border, accent, 0.55) : SwimTheme.Border);
            RectangleF r = new RectangleF(1.5f, 1.5f, Width - 3.5f, Height - 3.5f);
            SwimTheme.FillRounded(g, fill, r, 6);
            if (Enabled && (highlighted || primary || hover))
            {
                using (GraphicsPath glowPath = SwimTheme.Rounded(r, 6))
                using (Pen glow = new Pen(Color.FromArgb(hover || highlighted ? 70 : 45, highlighted ? SwimTheme.Amber : accent), 3f))
                    g.DrawPath(glow, glowPath);
            }

            SwimTheme.DrawRounded(g, border, r, 6);

            // Accent on the left edge: the command buttons look like the buttons of a timing console.
            if (highlighted || primary)
            {
                using (SolidBrush b = new SolidBrush(highlighted ? SwimTheme.Amber : accent))
                    g.FillRectangle(b, 3, 7, 2, Math.Max(4, Height - 14));
            }

            Color text = Enabled ? (highlighted ? SwimTheme.Amber : ForeColor) : SwimTheme.Faint;
            Rectangle area = new Rectangle(8, 0, Width - 14, Height);
            if (!string.IsNullOrEmpty(key))
            {
                Font badgeFont = SwimTheme.Ui(7f, FontStyle.Bold);
                Size ks = TextRenderer.MeasureText(key, badgeFont, Size.Empty, TextFormatFlags.NoPadding);
                int bw = ks.Width + 8, bh = ks.Height + 4;
                Rectangle badge = new Rectangle(Width - bw - 6, (Height - bh) / 2, bw, bh);
                SwimTheme.FillRounded(g, SwimTheme.Back, badge, 3);
                SwimTheme.DrawRounded(g, Enabled ? SwimTheme.Mix(SwimTheme.Border, accent, 0.35) : SwimTheme.Border, badge, 3);
                TextRenderer.DrawText(g, key, badgeFont, badge, Enabled ? accent : SwimTheme.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                area.Width = badge.Left - area.Left - 4;
            }

            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak;
            flags |= string.IsNullOrEmpty(key) ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
            TextRenderer.DrawText(g, Text ?? "", Font, area, text, flags);
        }
    }

    /// <summary>
    /// Page of the panel (one tab of SwimTabs).
    /// </summary>
    internal class SwimPage : Panel
    {
        public SwimPage(string title)
        {
            Text = title;
            Dock = DockStyle.Fill;
            Visible = false;
            BackColor = SwimTheme.Back;
            Padding = new Padding(6);
        }
    }

    /// <summary>
    /// Tabs drawn as a flat strip with an accent line under the active one, and the pages below.
    /// The strip shows only the pages of the current mode (race analysis or technical analysis).
    /// </summary>
    internal class SwimTabs : Panel
    {
        private readonly SwimTabStrip strip;
        private readonly Panel host;
        private readonly List<SwimPage> pages = new List<SwimPage>();
        private List<SwimPage> visible = new List<SwimPage>();
        private SwimPage selected;

        public event EventHandler SelectedIndexChanged;

        public SwimTabs()
        {
            BackColor = SwimTheme.Back;
            host = new Panel { Dock = DockStyle.Fill, BackColor = SwimTheme.Back };
            strip = new SwimTabStrip(this) { Dock = DockStyle.Top, Height = 34 };
            Controls.Add(host);
            Controls.Add(strip);
        }

        public List<SwimPage> VisiblePages
        {
            get { return visible; }
        }

        public void AddPage(SwimPage page)
        {
            pages.Add(page);
            visible.Add(page);
            host.Controls.Add(page);
            if (selected == null)
                SelectedTab = page;
            strip.Invalidate();
        }

        /// <summary>Pages shown in the strip, in this order.</summary>
        public void ShowPages(IEnumerable<SwimPage> list)
        {
            visible = list.Where(p => pages.Contains(p)).ToList();
            if (!visible.Contains(selected))
                SelectedTab = visible.FirstOrDefault();
            strip.Invalidate();
        }

        public SwimPage SelectedTab
        {
            get { return selected; }
            set
            {
                if (value == selected || value == null || !pages.Contains(value))
                    return;

                selected = value;
                SuspendLayout();
                foreach (SwimPage p in pages)
                    p.Visible = p == selected;
                ResumeLayout(true);
                strip.Invalidate();
                EventHandler handler = SelectedIndexChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }

        private class SwimTabStrip : Control
        {
            private readonly SwimTabs owner;
            private int hover = -1;
            private readonly List<Rectangle> bounds = new List<Rectangle>();

            public SwimTabStrip(SwimTabs owner)
            {
                this.owner = owner;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                Cursor = Cursors.Hand;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(SwimTheme.Back);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                ComputeBounds(g);
                using (SolidBrush line = new SolidBrush(SwimTheme.Border))
                    g.FillRectangle(line, 0, Height - 1, Width, 1);

                for (int i = 0; i < owner.visible.Count; i++)
                {
                    SwimPage page = owner.visible[i];
                    Rectangle r = bounds[i];
                    bool active = page == owner.selected;
                    if (i == hover && !active)
                        SwimTheme.FillRounded(g, SwimTheme.Surface, new RectangleF(r.X, r.Y + 3, r.Width, r.Height - 6), 4);

                    // HUD (2.9.0): the active tab bright with a glowing line under it.
                    if (active)
                    {
                        using (LinearGradientBrush under = new LinearGradientBrush(new Rectangle(r.X, 0, Math.Max(1, r.Width), Height),
                            Color.FromArgb(0, SwimTheme.Accent), Color.FromArgb(46, SwimTheme.Accent), LinearGradientMode.Vertical))
                            g.FillRectangle(under, r.X + 2, 2, r.Width - 4, Height - 3);
                    }

                    Font font = SwimTheme.Ui(8.5f, active ? FontStyle.Bold : FontStyle.Regular);
                    TextRenderer.DrawText(g, page.Text, font, r, active ? Color.White : SwimTheme.Dim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                    if (active)
                    {
                        using (SolidBrush glow = new SolidBrush(Color.FromArgb(70, SwimTheme.Accent)))
                            g.FillRectangle(glow, r.X + 4, Height - 5, r.Width - 8, 5);
                        using (SolidBrush accent = new SolidBrush(SwimTheme.Accent))
                            g.FillRectangle(accent, r.X + 6, Height - 3, r.Width - 12, 2);
                    }
                }
            }

            private void ComputeBounds(Graphics g)
            {
                bounds.Clear();
                int n = owner.visible.Count;
                if (n == 0)
                    return;

                List<int> widths = owner.visible.Select(p => TextRenderer.MeasureText(g, p.Text, SwimTheme.Ui(8.5f, FontStyle.Bold)).Width + 20).ToList();
                int total = widths.Sum();
                double scale = total > Width ? (double)Width / total : 1.0;
                int x = 0;
                for (int i = 0; i < n; i++)
                {
                    int w = (int)Math.Floor(widths[i] * scale);
                    bounds.Add(new Rectangle(x, 0, w, Height - 1));
                    x += w;
                }
            }

            private int HitTest(Point p)
            {
                for (int i = 0; i < bounds.Count; i++)
                {
                    if (bounds[i].Contains(p))
                        return i;
                }

                return -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int h = HitTest(e.Location);
                if (h != hover)
                {
                    hover = h;
                    Invalidate();
                }

                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                hover = -1;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                int i = HitTest(e.Location);
                if (e.Button == MouseButtons.Left && i >= 0 && i < owner.visible.Count)
                    owner.SelectedTab = owner.visible[i];

                base.OnMouseDown(e);
            }
        }
    }

    /// <summary>
    /// Two (or more) options in a pill, the selected one filled with the accent.
    /// </summary>
    internal class SwimSegmented : Control
    {
        private readonly List<string> items = new List<string>();
        private int selectedIndex;
        private int hover = -1;

        public event EventHandler SelectedIndexChanged;

        public SwimSegmented(params string[] options)
        {
            items.AddRange(options);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            Font = SwimTheme.Ui(8f, FontStyle.Bold);
        }

        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                int v = Math.Max(0, Math.Min(items.Count - 1, value));
                if (v == selectedIndex)
                    return;

                selectedIndex = v;
                Invalidate();
                EventHandler handler = SelectedIndexChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }

        /// <summary>Change the selection without raising the event.</summary>
        public void SetSelected(int index)
        {
            selectedIndex = Math.Max(0, Math.Min(items.Count - 1, index));
            Invalidate();
        }

        private Rectangle Segment(int i)
        {
            int w = (Width - 4) / Math.Max(1, items.Count);
            return new Rectangle(2 + i * w, 2, i == items.Count - 1 ? Width - 4 - i * w : w, Height - 4);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(SwimTheme.BackOf(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            SwimTheme.FillRounded(g, SwimTheme.Input, outer, Height / 2f);
            SwimTheme.DrawRounded(g, SwimTheme.Border, outer, Height / 2f);
            for (int i = 0; i < items.Count; i++)
            {
                Rectangle r = Segment(i);
                bool active = i == selectedIndex;
                if (active && r.Width > 0 && r.Height > 0)
                {
                    using (LinearGradientBrush brush = new LinearGradientBrush(r, SwimTheme.AccentDark, SwimTheme.Mix(SwimTheme.AccentDark, SwimTheme.Accent, 0.45), LinearGradientMode.Horizontal))
                    using (GraphicsPath path = SwimTheme.Rounded(r, r.Height / 2f))
                        g.FillPath(brush, path);
                }
                else if (i == hover)
                {
                    SwimTheme.FillRounded(g, SwimTheme.Raised, r, r.Height / 2f);
                }

                TextRenderer.DrawText(g, items[i], Font, r, active ? Color.White : SwimTheme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (Segment(i).Contains(e.Location))
                    h = i;
            }

            if (h != hover)
            {
                hover = h;
                Invalidate();
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (Segment(i).Contains(e.Location) && e.Button == MouseButtons.Left)
                    SelectedIndex = i;
            }

            base.OnMouseDown(e);
        }
    }

    /// <summary>
    /// Card with a title, for groups of controls (replaces the GroupBox, whose border cannot be themed).
    /// </summary>
    internal class SwimCard : Panel
    {
        private string title = "";

        public SwimCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = SwimTheme.Surface;
            Padding = new Padding(8, 24, 8, 6);
        }

        public string Title
        {
            get { return title; }
            set { title = value ?? ""; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(SwimTheme.BackOf(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // HUD (2.9.0): a glass card with glowing corner brackets and the title in spaced capitals.
            RectangleF r = new RectangleF(1.5f, 1.5f, Width - 3.5f, Height - 3.5f);
            SwimTheme.FillRounded(g, SwimTheme.Surface, r, 2);
            SwimTheme.DrawRounded(g, SwimTheme.Border, r, 2);
            SwimTheme.Brackets(g, r, SwimTheme.Accent, 0.8f);
            using (SolidBrush accent = new SolidBrush(SwimTheme.Accent))
                g.FillRectangle(accent, 9, 10, 2, 9);
            SwimTheme.TrackedText(g, SwimLang.Upper(title), SwimTheme.Ui(7.5f, FontStyle.Bold), SwimTheme.AccentLight, new Rectangle(15, 6, Width - 22, 16), 0.12f);
        }
    }

    /// <summary>
    /// Value tile: caption, big value, unit or comment. The ΔΑΚ tile is amber.
    /// </summary>
    internal class SwimTile : Control
    {
        private string caption = "";
        private string value = "–";
        private string sub = "";
        private Color color = SwimTheme.Text;
        private bool accent;

        public SwimTile()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Size = new Size(96, 58);
            Margin = new Padding(0, 0, 6, 6);
        }

        public void Set(string caption, string value, string sub, Color color, bool accent)
        {
            if (this.caption == caption && this.value == value && this.sub == sub && this.color == color && this.accent == accent)
                return;

            this.caption = caption ?? "";
            this.value = string.IsNullOrEmpty(value) ? "–" : value;
            this.sub = sub ?? "";
            this.color = color;
            this.accent = accent;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(SwimTheme.BackOf(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            SwimTheme.FillRounded(g, accent ? SwimTheme.AmberDark : SwimTheme.Surface, r, 6);
            SwimTheme.DrawRounded(g, accent ? SwimTheme.Mix(SwimTheme.AmberDark, SwimTheme.Amber, 0.6) : SwimTheme.Border, r, 6);
            TextRenderer.DrawText(g, caption.ToUpperInvariant(), SwimTheme.Ui(6.75f, FontStyle.Bold), new Rectangle(8, 5, Width - 12, 14),
                accent ? SwimTheme.Amber : SwimTheme.Dim, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, value, SwimTheme.Display(15f), new Rectangle(6, 17, Width - 10, 26),
                accent ? SwimTheme.Amber : color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (sub.Length > 0)
                TextRenderer.DrawText(g, sub, SwimTheme.Ui(6.75f, FontStyle.Regular), new Rectangle(8, Height - 16, Width - 12, 13),
                    SwimTheme.Dim, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// One value of the live strip.
    /// </summary>
    internal class SwimChip
    {
        public string Caption;
        public string Value;
        public Color Color;

        public SwimChip(string caption, string value, Color color)
        {
            Caption = caption;
            Value = value;
            Color = color;
        }
    }

    /// <summary>
    /// Live line of the panel: race clock and chips (next checkpoint, strokes, delta, ΔΑΚ, phase).
    /// </summary>
    internal class SwimLiveStrip : Control
    {
        private string clock = "–";
        private string clockCaption = SwimLang.T("ΧΡΟΝΟΣ");
        private List<SwimChip> chips = new List<SwimChip>();
        private string signature = "";

        public SwimLiveStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        public void SetValues(string clockCaption, string clock, List<SwimChip> list)
        {
            string sig = clockCaption + "|" + clock + "|" + string.Join("|", list.Select(c => c.Caption + "=" + c.Value + "#" + c.Color.ToArgb()).ToArray());
            if (sig == signature)
                return;

            signature = sig;
            this.clockCaption = clockCaption;
            this.clock = clock;
            chips = list;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(SwimTheme.BackOf(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (LinearGradientBrush back = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)), SwimTheme.Surface, SwimTheme.Back, LinearGradientMode.Horizontal))
            using (GraphicsPath path = SwimTheme.Rounded(r, 6))
                g.FillPath(back, path);
            SwimTheme.DrawRounded(g, SwimTheme.Border, r, 6);
            using (SolidBrush accent = new SolidBrush(SwimTheme.Accent))
                g.FillRectangle(accent, 1, 6, 3, Height - 12);

            // Clock.
            Font clockFont = SwimTheme.Display(19f);
            TextRenderer.DrawText(g, clockCaption, SwimTheme.Ui(6.5f, FontStyle.Bold), new Point(12, 5), SwimTheme.Dim, TextFormatFlags.NoPadding);
            Size cs = TextRenderer.MeasureText(g, clock, clockFont);
            TextRenderer.DrawText(g, clock, clockFont, new Rectangle(8, 14, cs.Width + 4, Height - 16), SwimTheme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

            int x = 14 + Math.Max(cs.Width, 96);
            Font capFont = SwimTheme.Ui(6.5f, FontStyle.Bold);
            Font valFont = SwimTheme.Ui(10f, FontStyle.Bold);
            foreach (SwimChip chip in chips)
            {
                Size vs = TextRenderer.MeasureText(g, chip.Value ?? "", valFont);
                Size ks = TextRenderer.MeasureText(g, chip.Caption ?? "", capFont);
                int w = Math.Max(vs.Width, ks.Width) + 12;
                if (x + w > Width - 4)
                    break;

                RectangleF box = new RectangleF(x, 6, w, Height - 12);
                SwimTheme.FillRounded(g, SwimTheme.Raised, box, 4);
                TextRenderer.DrawText(g, chip.Caption ?? "", capFont, new Point(x + 6, 9), SwimTheme.Dim, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, chip.Value ?? "", valFont, new Rectangle(x + 2, 19, w - 4, Height - 26), chip.Color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                x += w + 6;
            }
        }
    }
}
