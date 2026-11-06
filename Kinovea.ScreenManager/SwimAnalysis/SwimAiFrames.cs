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
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>(2.19.1) The frames of one request of AI pro: what it is, its moments and its frames (one per screen at each moment).</summary>
    public class SwimAiFramesSet
    {
        public string Title = "";
        public List<SwimAiMoment> Moments = new List<SwimAiMoment>();
        public List<SwimAiImage> Images = new List<SwimAiImage>();

        /// <summary>The screens with a frame, in their order.</summary>
        public List<int> Screens
        {
            get { return Images == null ? new List<int>() : Images.Where(i => i != null).Select(i => i.Screen).Distinct().OrderBy(x => x).ToList(); }
        }
    }

    /// <summary>
    /// (2.19.1) The frames AI pro sends, in one picture: a row for each moment (its number, what it is, its time), a column for each
    /// screen. With two screens or more the coach sees at once that all the cameras are at the same moment of the attempt; a screen
    /// without a frame at a moment (its video starts later or ends before) is a grey box.
    /// </summary>
    public static class SwimAiContact
    {
        public const int Pad = 10;
        public const int LabelHeight = 22;
        public const int HeadHeight = 36;

        /// <summary>The width of a frame in the picture for this many screens in this width (its window): between 200 and 480 pixels.</summary>
        public static int Thumb(int screens, int width)
        {
            int s = Math.Max(1, screens);
            return Math.Max(200, Math.Min(480, (width - 40) / s - Pad));
        }

        /// <summary>The picture of the frames of a request (null without any frame).</summary>
        public static Bitmap Compose(SwimAiFramesSet set, int thumb)
        {
            if (set == null || set.Moments == null || set.Moments.Count == 0 || set.Images == null || set.Images.Count == 0)
                return null;

            List<int> screens = set.Screens;
            Dictionary<int, Size> sizes = new Dictionary<int, Size>();
            foreach (int s in screens)
            {
                SwimAiImage first = set.Images.First(i => i.Screen == s);
                int w = Math.Max(1, first.Width), h = Math.Max(1, first.Height);
                sizes[s] = new Size(thumb, Math.Max(1, (int)Math.Round(thumb * (double)h / w)));
            }

            int row = sizes.Values.Max(z => z.Height);
            int width = Pad + screens.Count * (thumb + Pad);
            int height = HeadHeight + set.Moments.Count * (LabelHeight + row + Pad) + Pad;
            Bitmap picture = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(picture))
            using (Font big = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font small = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point))
            using (SolidBrush back = new SolidBrush(SwimTheme.Back))
            using (SolidBrush ink = new SolidBrush(SwimTheme.Text))
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            using (SolidBrush box = new SolidBrush(SwimTheme.Raised))
            using (SolidBrush shade = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
            {
                g.FillRectangle(back, 0, 0, width, height);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                string head = (set.Title ?? "") + "  ·  " + string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} στιγμές × {1} οθόνες"), set.Moments.Count, screens.Count);
                g.DrawString(head, big, ink, Pad, 9);

                int y = HeadHeight;
                foreach (SwimAiMoment m in set.Moments.OrderBy(x => x.N))
                {
                    string text = m.N.ToString(CultureInfo.InvariantCulture) + ". " + (m.Label ?? "") + "  (" + SwimAiPrompt.Signed(m.Seconds) + " s)";
                    g.DrawString(text, small, ink, Pad, y + 3);
                    y += LabelHeight;
                    for (int c = 0; c < screens.Count; c++)
                    {
                        Size size = sizes[screens[c]];
                        Rectangle r = new Rectangle(Pad + c * (thumb + Pad), y, size.Width, size.Height);
                        SwimAiImage image = set.Images.FirstOrDefault(i => i.Moment == m.N && i.Screen == screens[c] && i.Jpeg != null && i.Jpeg.Length > 0);
                        bool drawn = false;
                        if (image != null)
                        {
                            try
                            {
                                using (MemoryStream ms = new MemoryStream(image.Jpeg))
                                using (Image frame = Image.FromStream(ms))
                                    g.DrawImage(frame, r);
                                drawn = true;
                            }
                            catch (Exception)
                            {
                                drawn = false;
                            }
                        }

                        if (!drawn)
                        {
                            g.FillRectangle(box, r);
                            g.DrawString(SwimLang.T("χωρίς καρέ (εκτός του βίντεο αυτής της κάμερας)"), small, dim, new RectangleF(r.X + 8, r.Y + 8, r.Width - 16, r.Height - 16));
                        }

                        string name = SwimLang.T("Οθόνη ") + screens[c].ToString(CultureInfo.InvariantCulture);
                        SizeF measured = g.MeasureString(name, small);
                        g.FillRectangle(shade, r.X, r.Bottom - measured.Height - 4, measured.Width + 10, measured.Height + 4);
                        g.DrawString(name, small, ink, r.X + 5, r.Bottom - measured.Height - 2);
                    }

                    y += row + Pad;
                }
            }

            return picture;
        }
    }

    /// <summary>
    /// (2.19.1) The frames of AI pro as Claude sees them (before a request, nothing spent; or those of the last analysis), one request at a
    /// time when there were several, with the saving of the picture where the coach chooses.
    /// </summary>
    public class FormSwimAiFrames : Form
    {
        private readonly List<SwimAiFramesSet> sets;
        private readonly PictureBox box;
        private readonly Panel panel;
        private readonly ComboBox cmbSet;
        private readonly int areaWidth;
        private Bitmap picture;

        public FormSwimAiFrames(IList<SwimAiFramesSet> sets, string note)
        {
            this.sets = sets != null ? sets.Where(s => s != null).ToList() : new List<SwimAiFramesSet>();
            Text = SwimLang.T("AI pro · τα καρέ");
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            // (The picture is in the pixels of the screen: the window too.)
            AutoScaleMode = AutoScaleMode.None;
            BackColor = SwimTheme.Back;
            ForeColor = SwimTheme.Text;
            Font = SwimTheme.Ui(9.5f, FontStyle.Regular);

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            try
            {
                Form owner = Form.ActiveForm;
                if (owner != null)
                    area = Screen.FromControl(owner).WorkingArea;
            }
            catch (Exception)
            {
                // The screen of the main one.
            }

            areaWidth = area.Width;
            panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = SwimTheme.Back };
            box = new PictureBox { SizeMode = PictureBoxSizeMode.AutoSize, Location = new Point(0, 0), BackColor = SwimTheme.Back };
            panel.Controls.Add(box);

            FlowLayoutPanel top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(10, 8, 10, 4), BackColor = SwimTheme.Back };
            top.Controls.Add(new Label { Text = note ?? "", AutoSize = true, ForeColor = SwimTheme.Dim, MaximumSize = new Size(Math.Max(400, (int)(area.Width * 0.85) - 40), 0), Margin = new Padding(0, 0, 0, 4) });
            cmbSet = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 520, Margin = new Padding(0, 2, 0, 2) };
            foreach (SwimAiFramesSet s in this.sets)
                cmbSet.Items.Add(string.Format(CultureInfo.InvariantCulture, "{0}  ({1} × {2})", s.Title, s.Moments.Count, s.Screens.Count));
            if (this.sets.Count > 1)
                top.Controls.Add(cmbSet);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 6, 8, 6), BackColor = SwimTheme.Back };
            SwimButton close = new SwimButton(true) { Text = SwimLang.T("Κλείσιμο"), AutoSize = true, Height = 30, Primary = true };
            SwimButton save = new SwimButton(true) { Text = SwimLang.T("Αποθήκευση εικόνας…"), AutoSize = true, Height = 30 };
            close.Click += (s, e) => Close();
            save.Click += (s, e) => Save();
            buttons.Controls.Add(close);
            buttons.Controls.Add(save);

            Controls.Add(panel);
            Controls.Add(top);
            Controls.Add(buttons);
            SwimTheme.Apply(this);

            if (this.sets.Count > 0)
            {
                cmbSet.SelectedIndex = 0;
                ShowSet(0);
            }

            cmbSet.SelectedIndexChanged += (s, e) => ShowSet(cmbSet.SelectedIndex);

            int w = picture != null ? picture.Width + 40 : 600;
            int h = picture != null ? picture.Height + 150 : 400;
            ClientSize = new Size(Math.Max(520, Math.Min(w, (int)(area.Width * 0.92))), Math.Max(360, Math.Min(h, (int)(area.Height * 0.9))));
            MinimumSize = new Size(480, 320);
            AcceptButton = close;
            CancelButton = close;
            FormClosed += (s, e) =>
            {
                box.Image = null;
                if (picture != null)
                    picture.Dispose();
                picture = null;
            };
        }

        /// <summary>The picture of the frames that is shown (for the checks).</summary>
        internal Bitmap Picture
        {
            get { return picture; }
        }

        private void ShowSet(int index)
        {
            if (index < 0 || index >= sets.Count)
                return;
            SwimAiFramesSet set = sets[index];
            Bitmap next = SwimAiContact.Compose(set, SwimAiContact.Thumb(set.Screens.Count, (int)(areaWidth * 0.92)));
            box.Image = next;
            if (picture != null)
                picture.Dispose();
            picture = next;
            panel.AutoScrollPosition = new Point(0, 0);
        }

        private void Save()
        {
            if (picture == null)
                return;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = SwimLang.T("Αποθήκευση εικόνας…");
                dialog.Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg";
                dialog.FileName = "AI pro.png";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                try
                {
                    picture.Save(dialog.FileName, dialog.FilterIndex == 2 ? ImageFormat.Jpeg : ImageFormat.Png);
                }
                catch (Exception e)
                {
                    MessageBox.Show(this, e.Message, "AI pro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
    }
}
