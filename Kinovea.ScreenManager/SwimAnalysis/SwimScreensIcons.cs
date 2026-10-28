#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.14.0) The icons and the tips of the buttons of the common controls of the screens added for the swim analysis: the big
    /// screens (the screens show only their images, as big as they can be, the common controls drive them) and the group of screens
    /// (videos of the same race from several cameras, analysed as one).
    /// </summary>
    internal static class SwimScreensIcons
    {
        /// <summary>Three screens (two above, one below) with the corners of a full image; filled with the accent when on.</summary>
        internal static Bitmap BigScreens(bool on)
        {
            Bitmap b = new Bitmap(22, 18);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                Color ink = on ? Color.FromArgb(0, 150, 170) : Color.FromArgb(85, 85, 85);
                using (SolidBrush fill = new SolidBrush(on ? Color.FromArgb(0, 150, 170) : Color.FromArgb(120, 120, 120)))
                {
                    g.FillRectangle(fill, 4, 4, 6, 4);
                    g.FillRectangle(fill, 12, 4, 6, 4);
                    g.FillRectangle(fill, 8, 10, 6, 4);
                }

                using (Pen pen = new Pen(ink, 1.6f))
                {
                    // The four corners of the image.
                    g.DrawLines(pen, new[] { new PointF(1, 5), new PointF(1, 1), new PointF(5, 1) });
                    g.DrawLines(pen, new[] { new PointF(17, 1), new PointF(21, 1), new PointF(21, 5) });
                    g.DrawLines(pen, new[] { new PointF(1, 13), new PointF(1, 17), new PointF(5, 17) });
                    g.DrawLines(pen, new[] { new PointF(17, 17), new PointF(21, 17), new PointF(21, 13) });
                }
            }

            return b;
        }

        /// <summary>Three screens joined by a ring (the group); filled with the accent when there is a group.</summary>
        internal static Bitmap Group(bool on)
        {
            Bitmap b = new Bitmap(22, 18);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                Color ink = on ? Color.FromArgb(0, 150, 170) : Color.FromArgb(85, 85, 85);
                using (Pen ring = new Pen(ink, 1.6f))
                    g.DrawEllipse(ring, 2.5f, 1.5f, 17, 15);
                using (SolidBrush fill = new SolidBrush(on ? Color.FromArgb(0, 150, 170) : Color.FromArgb(120, 120, 120)))
                {
                    g.FillRectangle(fill, 5, 5, 5, 4);
                    g.FillRectangle(fill, 12, 5, 5, 4);
                    g.FillRectangle(fill, 8.5f, 10.5f, 5, 4);
                }
            }

            return b;
        }

        /// <summary>The tip of the button of the group: what it does, or the group there is (its screens and the screen of its analysis).</summary>
        internal static string GroupTip(string screens, int lead)
        {
            if (string.IsNullOrEmpty(screens))
                return SwimLang.T("Ομάδα οθονών: διάλεξε τις οθόνες του ίδιου αγώνα από άλλες κάμερες (ίδιος κολυμβητής). Αναλύονται ως μία, με ένα στρογγυλό κουμπί στη μέση τους· συγχρόνισε πρώτα τις οθόνες στο σήμα εκκίνησης.");

            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                SwimLang.T("Ομάδα: οθόνες {0} · η ανάλυση στο βίντεο της οθόνης {1}. Κλικ για να αλλάξει."), screens, lead);
        }

        internal static string BigScreensTip(bool on)
        {
            return on
                ? SwimLang.T("Μεγάλες οθόνες: ενεργές. Κλικ για να ξαναφανούν τα χειριστήρια κάθε οθόνης.")
                : SwimLang.T("Μεγάλες οθόνες: μόνο οι εικόνες των βίντεο, όσο πιο μεγάλες γίνεται (τα χειριστήρια κάθε οθόνης κρύβονται, τα κοινά μένουν· η ανάλυση κολύμβησης με το στρογγυλό κουμπί σε κάθε βίντεο).");
        }
    }
}
