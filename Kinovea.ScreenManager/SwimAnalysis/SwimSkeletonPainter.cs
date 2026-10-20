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
    /// Drawing of the skeleton of the swimmer on the video or on an exported image: bones (left side cyan, right side amber,
    /// across the body white), joints, and the angles of the knee, the hip and the elbow of the side nearer to the camera.
    /// </summary>
    public static class SwimSkeletonPainter
    {
        /// <summary>Angles of the skeleton: name (kept in Greek, shown with SwimLang.Label) and the three joints (end, vertex, end) on the left side.</summary>
        public static readonly KeyValuePair<string, int[]>[] Angles =
        {
            new KeyValuePair<string, int[]>("Γόνατο", new[] { SwimJoints.LeftHip, SwimJoints.LeftKnee, SwimJoints.LeftAnkle }),
            new KeyValuePair<string, int[]>("Ισχίο", new[] { SwimJoints.LeftShoulder, SwimJoints.LeftHip, SwimJoints.LeftKnee }),
            new KeyValuePair<string, int[]>("Αγκώνας", new[] { SwimJoints.LeftShoulder, SwimJoints.LeftElbow, SwimJoints.LeftWrist }),
            new KeyValuePair<string, int[]>("Ώμος", new[] { SwimJoints.LeftElbow, SwimJoints.LeftShoulder, SwimJoints.LeftHip })
        };

        /// <summary>The angles drawn live on the skeleton (knee, hip, elbow).</summary>
        private const int DrawnAngles = 3;

        /// <summary>The side of the body nearer to the camera (its joints are seen better): true for the left.</summary>
        public static bool LeftSideNearer(float[] visibility)
        {
            int[] left = { SwimJoints.LeftShoulder, SwimJoints.LeftElbow, SwimJoints.LeftWrist, SwimJoints.LeftHip, SwimJoints.LeftKnee, SwimJoints.LeftAnkle };
            float l = left.Sum(j => visibility[j]), r = left.Sum(j => visibility[j + 1]);
            return l >= r;
        }

        /// <summary>The joints of an angle on the chosen side (the right joint follows its left one).</summary>
        public static int[] SideJoints(int[] left, bool leftSide)
        {
            return leftSide ? left : left.Select(j => j + 1).ToArray();
        }

        /// <summary>The skeleton at a timestamp, if the part has the swimmer there. map: reference image pixels → drawing.</summary>
        public static void Paint(Graphics g, SwimSkeleton skeleton, long timestamp, Func<PointF, PointF> map, float scale, bool angles)
        {
            if (skeleton == null || map == null)
                return;

            PointF[] points = new PointF[SwimJoints.Count];
            float[] visibility = new float[SwimJoints.Count];
            if (!skeleton.TryGet(timestamp, 2, points, visibility))
                return;

            Paint(g, points.Select(map).ToArray(), visibility, scale, angles);
        }

        /// <summary>The skeleton from its joints in drawing coordinates.</summary>
        public static void Paint(Graphics g, PointF[] p, float[] visibility, float scale, bool angles)
        {
            using (Pen shadow = new Pen(Color.FromArgb(120, 0, 0, 0), 5.5f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen leftPen = new Pen(Color.FromArgb(235, SwimTheme.Accent), 3f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen rightPen = new Pen(Color.FromArgb(235, SwimTheme.Amber), 3f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen middlePen = new Pen(Color.FromArgb(220, 240, 244, 248), 3f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                foreach (int[] bone in SwimJoints.Bones)
                {
                    if (visibility[bone[0]] >= 0.4f && visibility[bone[1]] >= 0.4f)
                        g.DrawLine(shadow, p[bone[0]], p[bone[1]]);
                }

                foreach (int[] bone in SwimJoints.Bones)
                {
                    if (visibility[bone[0]] < 0.4f || visibility[bone[1]] < 0.4f)
                        continue;

                    bool across = SwimJoints.IsLeft(bone[0]) != SwimJoints.IsLeft(bone[1]) || bone[0] == SwimJoints.Nose;
                    Pen pen = across ? middlePen : (SwimJoints.IsLeft(bone[0]) ? leftPen : rightPen);
                    g.DrawLine(pen, p[bone[0]], p[bone[1]]);
                }
            }

            float r = 3.5f * scale;
            using (SolidBrush joint = new SolidBrush(Color.FromArgb(250, 255, 255, 255)))
            {
                for (int j = 0; j < SwimJoints.Count; j++)
                {
                    if (visibility[j] >= 0.4f)
                        g.FillEllipse(joint, p[j].X - r, p[j].Y - r, 2 * r, 2 * r);
                }
            }

            if (!angles)
                return;

            bool left = LeftSideNearer(visibility);
            for (int i = 0; i < DrawnAngles; i++)
            {
                int[] j = SideJoints(Angles[i].Value, left);
                if (j.Any(k => visibility[k] < 0.5f))
                    continue;

                DrawJointAngle(g, p[j[0]], p[j[1]], p[j[2]], scale);
            }
        }

        /// <summary>A small arc at the vertex and the value of the angle on the outer side.</summary>
        private static void DrawJointAngle(Graphics g, PointF a, PointF v, PointF b, float scale)
        {
            double ax = a.X - v.X, ay = a.Y - v.Y, bx = b.X - v.X, by = b.Y - v.Y;
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            if (la < 4 || lb < 4)
                return;

            double degrees = Math.Acos(Math.Max(-1, Math.Min(1, (ax * bx + ay * by) / (la * lb)))) * 180 / Math.PI;
            double a0 = Math.Atan2(ay, ax) * 180 / Math.PI;
            double a2 = Math.Atan2(by, bx) * 180 / Math.PI;
            double sweep = a2 - a0;
            while (sweep > 180) sweep -= 360;
            while (sweep < -180) sweep += 360;

            float radius = (float)Math.Max(8 * scale, Math.Min(22 * scale, 0.35 * Math.Min(la, lb)));
            using (Pen arc = new Pen(Color.FromArgb(230, 255, 255, 255), 1.6f * scale))
                g.DrawArc(arc, v.X - radius, v.Y - radius, 2 * radius, 2 * radius, (float)a0, (float)sweep);

            // The value away from the inside of the angle.
            double mid = (a0 + sweep / 2) * Math.PI / 180;
            float distance = radius + 13 * scale;
            PointF centre = new PointF(v.X - (float)Math.Cos(mid) * distance, v.Y - (float)Math.Sin(mid) * distance);
            string text = string.Format(CultureInfo.InvariantCulture, "{0:0}°", degrees);
            Font font = SwimTheme.Display(9.5f * scale);
            SizeF size = g.MeasureString(text, font);
            RectangleF box = new RectangleF(centre.X - size.Width / 2 - 3 * scale, centre.Y - size.Height / 2, size.Width + 6 * scale, size.Height);
            SwimTheme.HudChip(g, box, SwimTheme.Accent, 1f, 4 * scale, 205);
            using (SolidBrush brush = new SolidBrush(SwimTheme.Text))
                g.DrawString(text, font, brush, box.X + 3 * scale, box.Y);
        }
    }
}
