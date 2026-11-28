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
using System.Linq;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.25.0) The box of the packages made in the background, at the bottom right of the window of Kinovea (over it, not over the other
    /// programs, without taking the keys): each package with its progress (its video, its PDF, the KOE App); a click opens its window.
    /// When one is ready (or its video was not made), a notice above it: «Άνοιγμα αποστολής», «Αργότερα». It goes where the analyst drags
    /// it (kept in the settings, from the bottom right corner of Kinovea). It shows itself while there are packages, hides when none.
    /// </summary>
    internal sealed class FormSwimPackageTray : Form
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static FormSwimPackageTray tray;

        private const int RowWidth = 392;
        private const int RowHeight = 58;
        private const int NoticeWidth = 416;
        private const int NoticeHeight = 132;
        private const int Gap = 8;
        private const int DefaultRight = 16;
        private const int DefaultBottom = 96;

        private readonly Form owner;
        private readonly Timer timer = new Timer { Interval = 400 };
        private readonly List<KeyValuePair<Rectangle, Action>> targets = new List<KeyValuePair<Rectangle, Action>>();
        private List<SwimAnalysisManager.BackgroundRow> rows = new List<SwimAnalysisManager.BackgroundRow>();
        private SwimAnalysisManager.BackgroundRow notice;
        private string shape = "";
        private bool pressed;
        private bool dragging;
        private Point dragFrom;
        private Point locationFrom;

        /// <summary>The box for the window of Kinovea (made when a package is in the background), its packages as they are now.</summary>
        public static void UpdateFor(Form owner)
        {
            try
            {
                if (tray == null || tray.IsDisposed)
                {
                    if (owner == null || owner.IsDisposed || SwimAnalysisManager.BackgroundRows().Count == 0)
                        return;
                    tray = new FormSwimPackageTray(owner);
                }

                tray.RefreshRows();
            }
            catch (Exception e)
            {
                log.Error("Could not show the box of the packages in the background.", e);
            }
        }

        private FormSwimPackageTray(Form owner)
        {
            this.owner = owner;
            Text = SwimLang.T("📦 Αποστολές στο παρασκήνιο");
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = SwimTheme.Surface;
            ForeColor = SwimTheme.Text;
            Font = SwimTheme.Ui(9f, FontStyle.Regular);
            DoubleBuffered = true;
            Size = new Size(RowWidth, RowHeight);
            Owner = owner;

            owner.LocationChanged += Owner_Moved;
            owner.SizeChanged += Owner_Moved;
            owner.FormClosed += (s, e) => Close();
            timer.Tick += (s, e) => RefreshRows();
            timer.Start();
            Disposed += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                owner.LocationChanged -= Owner_Moved;
                owner.SizeChanged -= Owner_Moved;
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                // A tool window (not in Alt+Tab) that does not take the keys from Kinovea when clicked.
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 | 0x08000000;
                return cp;
            }
        }

        private void Owner_Moved(object sender, EventArgs e)
        {
            if (Visible)
                Place();
        }

        /// <summary>The packages as they are now: the size of the box, its notice, its place; hidden when there is none.</summary>
        private void RefreshRows()
        {
            if (IsDisposed)
                return;

            rows = SwimAnalysisManager.BackgroundRows();
            if (rows.Count == 0 || owner == null || owner.IsDisposed)
            {
                notice = null;
                if (Visible)
                    Hide();
                return;
            }

            notice = rows.FirstOrDefault(r => !r.Noticed && (r.State == SwimAnalysisManager.BackgroundState.Ready || r.State == SwimAnalysisManager.BackgroundState.Failed));
            int width = notice != null ? NoticeWidth : RowWidth;
            int height = (notice != null ? NoticeHeight + Gap : 0) + rows.Count * RowHeight + (rows.Count - 1) * Gap;
            string now = width + "x" + height + (notice != null ? "n" : "");
            if (now != shape)
            {
                shape = now;
                Size = new Size(width, height);
                Region before = Region;
                using (GraphicsPath outline = Outline())
                    Region = new Region(outline);
                if (before != null)
                    before.Dispose();
            }

            Place();
            if (!Visible && owner.Visible && owner.WindowState != FormWindowState.Minimized)
                Show(owner);
            Invalidate();
        }

        /// <summary>The cards of the box (the notice above the rows), each rounded.</summary>
        private GraphicsPath Outline()
        {
            GraphicsPath path = new GraphicsPath();
            foreach (Rectangle r in Cards())
                using (GraphicsPath card = Rounded(r, 10))
                    path.AddPath(card, false);
            return path;
        }

        private IEnumerable<Rectangle> Cards()
        {
            int y = 0;
            if (notice != null)
            {
                yield return new Rectangle(Width - NoticeWidth, 0, NoticeWidth, NoticeHeight);
                y = NoticeHeight + Gap;
            }

            for (int i = 0; i < rows.Count; i++)
                yield return new Rectangle(Width - RowWidth, y + i * (RowHeight + Gap), RowWidth, RowHeight);
        }

        /// <summary>From the bottom right corner of the window of Kinovea, as the analyst left it, always inside it.</summary>
        private void Place()
        {
            if (owner == null || owner.IsDisposed)
                return;

            Rectangle client = owner.RectangleToScreen(owner.ClientRectangle);
            Point offset = Offset();
            int x = client.Right - Width - offset.X, y = client.Bottom - Height - offset.Y;
            x = Math.Max(client.Left, Math.Min(x, client.Right - Width));
            y = Math.Max(client.Top, Math.Min(y, client.Bottom - Height));
            if (Location.X != x || Location.Y != y)
                Location = new Point(x, y);
        }

        private static Point Offset()
        {
            SwimAnalysisManager m = SwimAnalysisManager.Current;
            string text = m != null && m.Settings != null ? m.Settings.PackageTray : null;
            if (!string.IsNullOrEmpty(text))
            {
                string[] parts = text.Split(',');
                int dx, dy;
                if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out dx) &&
                    int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out dy))
                    return new Point(Math.Max(0, dx), Math.Max(0, dy));
            }

            return new Point(DefaultRight, DefaultBottom);
        }

        private void KeepOffset()
        {
            SwimAnalysisManager m = SwimAnalysisManager.Current;
            if (m == null || m.Settings == null || owner == null || owner.IsDisposed)
                return;

            Rectangle client = owner.RectangleToScreen(owner.ClientRectangle);
            int dx = Math.Max(0, client.Right - Right), dy = Math.Max(0, client.Bottom - Bottom);
            m.Settings.PackageTray = dx.ToString(CultureInfo.InvariantCulture) + "," + dy.ToString(CultureInfo.InvariantCulture);
            m.Settings.Save();
        }

        #region Painting
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(SwimTheme.Back);
            targets.Clear();

            List<Rectangle> cards = Cards().ToList();
            int i = 0;
            if (notice != null)
                PaintNotice(g, cards[i++], notice);
            foreach (SwimAnalysisManager.BackgroundRow row in rows)
            {
                if (i < cards.Count)
                    PaintRow(g, cards[i++], row);
            }
        }

        private void PaintRow(Graphics g, Rectangle r, SwimAnalysisManager.BackgroundRow row)
        {
            bool ready = row.State == SwimAnalysisManager.BackgroundState.Ready;
            bool failed = row.State == SwimAnalysisManager.BackgroundState.Failed;
            Color edge = ready ? SwimTheme.Green : failed ? SwimTheme.Warn : Color.FromArgb(42, 159, 187);
            using (GraphicsPath path = Rounded(Rectangle.Inflate(r, -1, -1), 10))
            using (SolidBrush back = new SolidBrush(ready ? Color.FromArgb(6, 35, 26) : SwimTheme.Surface))
            using (Pen pen = new Pen(edge, 1.2f))
            {
                g.FillPath(back, path);
                g.DrawPath(pen, path);
            }

            DrawBox(g, new RectangleF(r.Left + 12, r.Top + 18, 20, 20), ready ? SwimTheme.Green : failed ? SwimTheme.Warn : SwimTheme.AccentLight);
            string title = ready ? SwimLang.T("Έτοιμη αποστολή") + " · " + row.Title
                : failed ? SwimLang.T("Η αποστολή δεν έγινε") + " · " + row.Title
                : row.State == SwimAnalysisManager.BackgroundState.Waiting ? row.Title + " · " + SwimLang.T("αποστολή στη σειρά")
                : row.Title + " · " + SwimLang.T("αποστολή στο παρασκήνιο");
            Rectangle text = new Rectangle(r.Left + 42, r.Top + 8, r.Width - 42 - 30, 18);
            TextRenderer.DrawText(g, title, SwimTheme.Ui(9f, FontStyle.Bold), text, SwimTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            Rectangle line = new Rectangle(r.Left + 42, r.Top + 26, r.Width - 42 - 30, 16);
            TextRenderer.DrawText(g, failed ? (row.Error ?? "") : (row.Line ?? ""), SwimTheme.Ui(8.5f, FontStyle.Regular), line,
                ready ? Color.FromArgb(155, 232, 198) : failed ? SwimTheme.Warn : SwimTheme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (row.State == SwimAnalysisManager.BackgroundState.Making || row.State == SwimAnalysisManager.BackgroundState.Waiting)
            {
                Rectangle bar = new Rectangle(r.Left + 42, r.Bottom - 12, r.Width - 42 - 30, 4);
                using (SolidBrush track = new SolidBrush(Color.FromArgb(18, 56, 74)))
                    g.FillRectangle(track, bar);
                int done = (int)Math.Round(bar.Width * Math.Max(0, Math.Min(100, row.Percent)) / 100.0);
                if (done > 0)
                {
                    using (SolidBrush fill = new SolidBrush(SwimTheme.Accent))
                        g.FillRectangle(fill, new Rectangle(bar.Left, bar.Top, done, bar.Height));
                }
            }

            // ↗: its window; a package ended can also leave the box («×»: its window closed, the package stays in its folder).
            object key = row.Key;
            Rectangle open = new Rectangle(r.Right - 28, r.Top + 6, 22, 22);
            TextRenderer.DrawText(g, "↗", SwimTheme.Ui(10f, FontStyle.Bold), open, SwimTheme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (ready || failed)
            {
                Rectangle close = new Rectangle(r.Right - 28, r.Bottom - 28, 22, 22);
                TextRenderer.DrawText(g, "×", SwimTheme.Ui(11f, FontStyle.Regular), close, SwimTheme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                targets.Add(new KeyValuePair<Rectangle, Action>(close, () => SwimAnalysisManager.CloseBackground(key)));
            }

            targets.Add(new KeyValuePair<Rectangle, Action>(r, () => SwimAnalysisManager.OpenBackground(key)));
        }

        private void PaintNotice(Graphics g, Rectangle r, SwimAnalysisManager.BackgroundRow row)
        {
            bool ready = row.State == SwimAnalysisManager.BackgroundState.Ready;
            Color accent = ready ? SwimTheme.Green : SwimTheme.Warn;
            using (GraphicsPath path = Rounded(Rectangle.Inflate(r, -1, -1), 10))
            using (SolidBrush back = new SolidBrush(SwimTheme.Surface))
            using (Pen pen = new Pen(ready ? Color.FromArgb(31, 111, 90) : SwimTheme.AmberDark, 1.2f))
            {
                g.FillPath(back, path);
                g.DrawPath(pen, path);
            }

            using (SolidBrush stripe = new SolidBrush(accent))
                g.FillRectangle(stripe, new Rectangle(r.Left + 1, r.Top + 10, 4, r.Height - 20));

            string head = ready ? SwimLang.T("✓ Η αποστολή είναι έτοιμη") : SwimLang.T("⚠ Το βίντεο της αποστολής δεν έγινε");
            TextRenderer.DrawText(g, head, SwimTheme.Ui(10.5f, FontStyle.Bold), new Rectangle(r.Left + 16, r.Top + 10, r.Width - 30, 22), accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, row.Title, SwimTheme.Ui(9f, FontStyle.Regular), new Rectangle(r.Left + 16, r.Top + 36, r.Width - 30, 18), SwimTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, ready ? (row.Line ?? "") : (row.Error ?? ""), SwimTheme.Ui(8.5f, FontStyle.Regular), new Rectangle(r.Left + 16, r.Top + 54, r.Width - 30, 18),
                ready ? Color.FromArgb(207, 233, 240) : SwimTheme.Warn, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            object key = row.Key;
            Rectangle open = PaintButton(g, new Point(r.Left + 16, r.Bottom - 44), ready ? SwimLang.T("📦 Άνοιγμα αποστολής") : SwimLang.T("Άνοιγμα"), true);
            Rectangle later = PaintButton(g, new Point(open.Right + 8, r.Bottom - 44), SwimLang.T("Αργότερα"), false);
            targets.Add(new KeyValuePair<Rectangle, Action>(open, () => SwimAnalysisManager.OpenBackground(key)));
            targets.Add(new KeyValuePair<Rectangle, Action>(later, () => SwimAnalysisManager.NoticedBackground(key)));
            targets.Add(new KeyValuePair<Rectangle, Action>(r, null));
        }

        private static Rectangle PaintButton(Graphics g, Point at, string text, bool primary)
        {
            Font font = SwimTheme.Ui(9f, FontStyle.Regular);
            Size size = TextRenderer.MeasureText(g, text, font, new Size(400, 30), TextFormatFlags.NoPrefix);
            Rectangle r = new Rectangle(at.X, at.Y, size.Width + 24, 30);
            using (GraphicsPath path = Rounded(r, 6))
            using (SolidBrush back = new SolidBrush(primary ? SwimTheme.AccentDark : SwimTheme.Raised))
            using (Pen pen = new Pen(primary ? Color.FromArgb(42, 159, 187) : SwimTheme.Border, 1f))
            {
                g.FillPath(back, path);
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, text, font, r, SwimTheme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return r;
        }

        /// <summary>The box of a package (its outline, as the icon of the window).</summary>
        private static void DrawBox(Graphics g, RectangleF r, Color color)
        {
            float k = r.Width / 24f;
            Func<float, float, PointF> p = (x, y) => new PointF(r.Left + x * k, r.Top + y * k);
            using (Pen pen = new Pen(color, 1.8f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawPolygon(pen, new[] { p(12, 2.5f), p(3.5f, 6.8f), p(3.5f, 17.2f), p(12, 21.5f), p(20.5f, 17.2f), p(20.5f, 6.8f) });
                g.DrawLines(pen, new[] { p(3.5f, 6.8f), p(12, 11.1f), p(20.5f, 6.8f) });
                g.DrawLine(pen, p(12, 11.1f), p(12, 21.5f));
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
        #endregion

        #region Mouse
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            pressed = true;
            dragging = false;
            dragFrom = Cursor.Position;
            locationFrom = Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (pressed && (e.Button & MouseButtons.Left) != 0)
            {
                Point now = Cursor.Position;
                if (!dragging && (Math.Abs(now.X - dragFrom.X) > 4 || Math.Abs(now.Y - dragFrom.Y) > 4))
                    dragging = true;
                if (dragging)
                    Location = new Point(locationFrom.X + now.X - dragFrom.X, locationFrom.Y + now.Y - dragFrom.Y);
                return;
            }

            Cursor = targets.Any(t => t.Value != null && t.Key.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!pressed || e.Button != MouseButtons.Left)
                return;
            pressed = false;
            if (dragging)
            {
                dragging = false;
                KeepOffset();
                Place();
                return;
            }

            // The first target that holds the click (the buttons before their card).
            foreach (KeyValuePair<Rectangle, Action> t in targets)
            {
                if (!t.Key.Contains(e.Location))
                    continue;
                if (t.Value != null)
                {
                    try
                    {
                        t.Value();
                    }
                    catch (Exception ex)
                    {
                        log.Error("Error on a click in the box of the packages.", ex);
                    }
                }

                break;
            }

            RefreshRows();
        }
        #endregion
    }
}
