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
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Look = Kinovea.ScreenManager.SwimBarLook;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.37.0) The big card of the right bar, in three tabs (the analyst's request: everything checked from the bars, the window only when
    /// wanted): «ΓΕΓΟΝΟΤΑ», every event of the race in its order, from the start signal to the finish, with its time and its value (a
    /// click shows its frame; the distances are changed in the row, typed or with the wheel); «ΠΕΡΑΣΜΑΤΑ», the checkpoints as before;
    /// «ΑΓΩΝΑΣ», the details of the race filled in at the beginning, changed in place (a list, a text, a number), and the window at the
    /// page of the details. The lists follow the video and scroll with the wheel.
    /// </summary>
    internal sealed partial class SwimVideoBar : IMessageFilter
    {
        #region Tabs
        private static string[] listTabs
        {
            get { return new string[] { SwimLang.T("ΓΕΓΟΝΟΤΑ"), SwimLang.T("ΠΕΡΑΣΜΑΤΑ"), SwimLang.T("ΑΓΩΝΑΣ") }; }
        }

        private static string[] listTabTips
        {
            get
            {
                return new string[]
                {
                    SwimLang.T("Όλα τα γεγονότα του αγώνα με τη σειρά, από το σήμα ως τον τερματισμό: κλικ σε γραμμή, το βίντεο πάει στο καρέ της· ") +
                        SwimLang.T("κλικ σε απόσταση, τη διορθώνεις (γράψιμο ή ροδέλα).") ,
                    SwimLang.T("Τα περάσματα: χρόνος, ταχύτητα, χεριές (με κίτρινο το επόμενο)."),
                    SwimLang.T("Τα στοιχεία του αγώνα που συμπληρώνεις στην αρχή: κλικ σε γραμμή για αλλαγή.")
                };
            }
        }

        /// <summary>The tab of the card shown: 0 the events, 1 the checkpoints, 2 the details of the race (kept in the settings).</summary>
        private int ListTab
        {
            get { return Math.Max(0, Math.Min(2, manager.Settings.BarListTab)); }
        }

        private void ChooseListTab(int tab)
        {
            CloseEditor(true);
            if (manager.Settings.BarListTab == tab)
                return;

            manager.Settings.BarListTab = tab;
            manager.Settings.Save();
            RefreshNow();
        }

        /// <summary>The card in its tabs: the tabs at its top in place of the caption, the list of the tab under them.</summary>
        private void PaintList(Graphics g, List<Item> list, RectangleF card, SwimRace race)
        {
            listArea = RectangleF.Empty;
            if (card.Height < S(90))
                return;

            DrawCard(g, card, colorSplits);
            int tab = ListTab;
            RectangleF strip = new RectangleF(card.Left + S(7), card.Top + S(7), card.Width - S(14), S(22));
            DrawListTabs(g, list, strip, tab);
            RectangleF inner = new RectangleF(card.Left, strip.Bottom + S(5), card.Width, card.Bottom - strip.Bottom - S(5));
            switch (tab)
            {
                case 0:
                    PaintEvents(g, list, inner, race);
                    break;
                case 1:
                    PaintSplits(g, list, inner, race);
                    break;
                default:
                    PaintRaceInfo(g, list, inner, race);
                    break;
            }
        }

        private void DrawListTabs(Graphics g, List<Item> list, RectangleF r, int selected)
        {
            string[] names = listTabs;
            string[] tips = listTabTips;
            SwimTheme.FillRounded(g, SwimTheme.Input, r, 4);
            SwimTheme.DrawRounded(g, SwimTheme.Border, r, 4);
            float w = r.Width / names.Length;
            Font font = SwimTheme.Ui(7f, FontStyle.Bold);
            for (int i = 0; i < names.Length; i++)
            {
                string id = "ltab" + i.ToString(CultureInfo.InvariantCulture);
                RectangleF cell = new RectangleF(r.Left + i * w + 2, r.Top + 2, w - 4, r.Height - 4);
                if (i == selected)
                {
                    using (LinearGradientBrush under = new LinearGradientBrush(new RectangleF(cell.X, cell.Y - 1, cell.Width, cell.Height + 2),
                        Color.FromArgb(10, SwimTheme.Accent), Color.FromArgb(70, SwimTheme.Accent), LinearGradientMode.Vertical))
                        g.FillRectangle(under, cell);
                    using (SolidBrush line = new SolidBrush(SwimTheme.Accent))
                        g.FillRectangle(line, cell.Left + 5, cell.Bottom - 2, cell.Width - 10, 2);
                }
                else if (hoverId == id)
                {
                    SwimTheme.FillRounded(g, SwimTheme.RaisedHover, cell, 3);
                }

                Write(g, names[i], font, cell, i == selected ? Color.White : SwimTheme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                int index = i;
                list.Add(new Item { Id = id, Bounds = cell, Tip = tips[i], Click = () => ChooseListTab(index) });
            }
        }
        #endregion

        #region Events
        // The events of the race, made again when the analysis changes.
        private List<SwimBarEvent> events = new List<SwimBarEvent>();
        private string eventsKey = "";

        // The first line shown of each list, and the analyst's scroll: it holds until the video is at another event (or another next one).
        private int eventsFirst;
        private bool eventsManual;
        private string eventsAnchor;
        private int splitsFirst;
        private bool splitsManual;
        private int splitsAnchor;
        private int infoFirst;
        private int wheelRest;

        /// <summary>Where the wheel scrolls the list of the tab (client coordinates), empty without a list.</summary>
        private RectangleF listArea;

        /// <summary>The race time the list follows when there is no video (the pictures of the CI check); null: the video's.</summary>
        internal double? PreviewTime;

        private List<SwimBarEvent> Events(SwimRace race)
        {
            string key = race.Id + "|" + manager.DataVersion + "|" + SwimLang.English;
            if (key != eventsKey)
            {
                events = SwimBarEvents.Build(race, manager.Computed);
                eventsKey = key;
            }

            return events;
        }

        /// <summary>A line of the list: the header of a group or an event.</summary>
        private sealed class Line
        {
            public string Header;
            public int Event = -1;
        }

        private float RowHeight
        {
            get { return S(20); }
        }

        private float HeaderHeight
        {
            get { return S(16); }
        }

        private float LineHeight(Line l)
        {
            return l.Header != null ? HeaderHeight : RowHeight;
        }

        private static List<Line> Lines(List<SwimBarEvent> list)
        {
            List<Line> lines = new List<Line>();
            for (int i = 0; i < list.Count; i++)
            {
                if (i == 0 || list[i].Group != list[i - 1].Group)
                    lines.Add(new Line { Header = list[i].Group });
                lines.Add(new Line { Event = i });
            }

            return lines;
        }

        /// <summary>The events: a header for each group, the rows with their dot, name, time and value; the event of the moment lit.</summary>
        private void PaintEvents(Graphics g, List<Item> list, RectangleF inner, SwimRace race)
        {
            List<SwimBarEvent> evs = Events(race);
            float rx = inner.Left + S(7);
            float rw = inner.Right - S(9) - rx;
            float colT = rx + rw * 0.62f;
            float colV = rx + rw;

            RectangleF head = new RectangleF(rx, inner.Top, rw, S(13));
            Font headFont = SwimTheme.Ui(6.5f, FontStyle.Bold);
            Write(g, SwimLang.T("ΓΕΓΟΝΟΣ"), headFont, new RectangleF(rx + S(9), head.Top, rw * 0.4f, head.Height), SwimTheme.Faint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            CellRight(g, SwimLang.T("ΧΡΟΝΟΣ"), headFont, colT, head, SwimTheme.Faint);
            CellRight(g, SwimLang.T("ΤΙΜΗ"), headFont, colV, head, SwimTheme.Faint);

            RectangleF area = new RectangleF(inner.Left + S(3), head.Bottom + S(3), inner.Width - S(6), inner.Bottom - head.Bottom - S(9));
            if (area.Height < RowHeight || evs.Count == 0)
                return;
            listArea = area;

            List<Line> lines = Lines(evs);
            int current = SwimBarEvents.CurrentIndex(evs, manager.CurrentRaceTime ?? PreviewTime);
            int next = SwimBarEvents.NextIndex(evs);
            string currentId = current >= 0 ? evs[current].Id : "";

            // The list follows the video: the event of the moment a third from the top (before the start, the top). The analyst's scroll
            // holds until the video is at another event; an open editor keeps its row in place.
            if (eventsManual && currentId != eventsAnchor && editId == null)
                eventsManual = false;
            int target = Math.Max(0, lines.FindIndex(l => l.Event == current));
            if (!eventsManual && editId == null)
                eventsFirst = FirstFor(lines, target, area.Height / 3);
            eventsFirst = Math.Max(0, Math.Min(MaxFirst(lines, area.Height), eventsFirst));

            Font labelFont = SwimTheme.Ui(8.25f, FontStyle.Regular);
            Font timeFont = SwimTheme.Display(9.5f);
            Font valueFont = SwimTheme.Ui(8f, FontStyle.Regular);
            Font groupFont = SwimTheme.Ui(6.5f, FontStyle.Bold);
            float y = area.Top;
            int shown = 0;
            for (int li = eventsFirst; li < lines.Count; li++)
            {
                Line line = lines[li];
                float h = LineHeight(line);
                if (y + h > area.Bottom + 0.5f)
                    break;

                shown++;
                if (line.Header != null)
                {
                    RectangleF hr = new RectangleF(rx, y, rw, h);
                    Size hs = TextRenderer.MeasureText(g, SwimLang.Upper(line.Header), groupFont, Size.Empty, TextFormatFlags.NoPadding);
                    SwimTheme.TrackedText(g, SwimLang.Upper(line.Header), groupFont, SwimTheme.Faint, Rectangle.Round(new RectangleF(hr.Left + S(2), hr.Top + S(3), hr.Width, h - S(2))), 0.14f);
                    float lineLeft = hr.Left + S(8) + hs.Width * 1.18f;
                    if (lineLeft < hr.Right - S(6))
                    {
                        using (Pen p = new Pen(Color.FromArgb(34, SwimTheme.Accent), 1f))
                            g.DrawLine(p, lineLeft, hr.Top + h / 2 + S(1), hr.Right - S(2), hr.Top + h / 2 + S(1));
                    }

                    y += h;
                    continue;
                }

                int i = line.Event;
                SwimBarEvent e = evs[i];
                PaintEventRow(g, list, new RectangleF(rx, y, rw, h - S(2)), e, i == current, i == next, colT, colV, labelFont, timeFont, valueFont, race);
                y += h;
            }

            // The place in the list, when it does not fit.
            if (shown < lines.Count)
                DrawScrollMark(g, area, eventsFirst, shown, lines.Count);
        }

        private void PaintEventRow(Graphics g, List<Item> list, RectangleF r, SwimBarEvent e, bool current, bool next, float colT, float colV,
            Font labelFont, Font timeFont, Font valueFont, SwimRace race)
        {
            string id = "ev:" + e.Id;
            bool hover = hoverId == id;

            // The row: its frame on the video (a checkpoint through the checkpoint, to be marked again there with «Πέρασμα»). Its item
            // first: the hit test takes the last ones first, the value box and the strokes over the row.
            double? at = e.VideoTime;
            Action click = null;
            if (e.Checkpoint > 0 && e.Marked)
            {
                int cp = e.Checkpoint;
                click = () => manager.Seek(cp);
            }
            else if (e.Id == "start" && race.StartTimestamp.HasValue)
            {
                long startStamp = race.StartTimestamp.Value;
                click = () => manager.SeekTo(startStamp);
            }
            else if (at.HasValue)
            {
                double seconds = at.Value;
                click = () => manager.SeekRaceTime(seconds);
            }

            list.Add(new Item { Id = id, Bounds = r, Tip = e.Tip + (click != null ? SwimLang.T("\nΚλικ: το βίντεο στο καρέ του.") : ""), Click = click });

            if (next)
            {
                SwimTheme.FillRounded(g, SwimTheme.NextRow, r, 3);
                using (SolidBrush bar = new SolidBrush(SwimTheme.Amber))
                    g.FillRectangle(bar, r.Left + S(1), r.Top + S(3), S(2), r.Height - S(6));
            }
            else if (current)
            {
                SwimTheme.FillRounded(g, SwimTheme.RaisedHover, r, 3);
                SwimTheme.DrawRounded(g, Color.FromArgb(120, SwimTheme.Accent), r, 3);
            }
            else if (hover && e.VideoTime.HasValue)
            {
                SwimTheme.FillRounded(g, SwimTheme.RaisedHover, r, 3);
            }
            else
            {
                using (Pen line = new Pen(Color.FromArgb(28, SwimTheme.Accent), 1f))
                    g.DrawLine(line, r.Left + S(4), r.Bottom + S(1), r.Right - S(4), r.Bottom + S(1));
            }

            bool timed = e.Time.HasValue;
            Color phase = e.Phase == 0 ? SwimTheme.Accent : e.Phase == 2 ? SwimTheme.Amber : e.Phase == 3 ? SwimTheme.Green : SwimTheme.Text;
            float dot = S(4);
            using (SolidBrush b = new SolidBrush(timed ? phase : Color.FromArgb(110, phase)))
                g.FillEllipse(b, r.Left + S(4), r.Top + (r.Height - dot) / 2, dot, dot);

            // (2.42.0) The time of an exchange running from the touch until F is marked (⏱ 0.11…).
            string value = e.Value;
            bool runningValue = false;
            double? now = manager.CurrentRaceTime ?? PreviewTime;
            if (!timed && e.RunningFrom.HasValue && now.HasValue && now.Value >= e.RunningFrom.Value - 0.0005 &&
                now.Value - e.RunningFrom.Value <= SwimRelay.RunningSeconds)
            {
                value = "⏱ " + Math.Max(0, now.Value - e.RunningFrom.Value).ToString("0.00", CultureInfo.InvariantCulture) + "…";
                runningValue = true;
            }

            // The value: a distance in its box (changed in place), or a text.
            float valueLeft = colV;
            if (e.Distance != SwimBarDistance.None)
            {
                RectangleF box = new RectangleF(colV - S(58), r.Top + S(2), S(58), r.Height - S(4));
                valueLeft = box.Left;
                if (editId != e.Id)
                    DrawValueBox(g, list, e, box, race);
            }
            else if (!string.IsNullOrEmpty(value))
            {
                Color color = runningValue ? SwimTheme.Accent : e.Pilot ? SwimTheme.Amber : e.ValueOk ? SwimTheme.Green : (timed ? SwimTheme.Text : SwimTheme.Faint);
                Size vs = TextRenderer.MeasureText(g, value, valueFont, Size.Empty, TextFormatFlags.NoPadding);
                RectangleF vr = new RectangleF(colV - vs.Width - 1, r.Top, vs.Width + 2, r.Height);
                valueLeft = vr.Left;
                if (e.StrokesEditable && e.StrokesValue && e.Checkpoint > 0)
                {
                    // The strokes of the segment: a click corrects them (the window of the strokes, as at the checkpoint).
                    string sid = "evs:" + e.Id;
                    RectangleF hit = RectangleF.Inflate(vr, S(3), 0);
                    if (hoverId == sid)
                        SwimTheme.FillRounded(g, SwimTheme.Raised, hit, 3);
                    int d = e.Checkpoint;
                    list.Add(new Item { Id = sid, Bounds = hit, Tip = SwimLang.T("Οι χεριές του τμήματος: κλικ για διόρθωση."), Click = () => manager.EditSegmentStrokes(d) });
                }

                Write(g, value, valueFont, vr, color, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            string time = timed ? FormatRaceTime(e.Time.Value) : "–";
            Color timeColor = !timed ? SwimTheme.Faint : next ? SwimTheme.Amber : (e.Checkpoint > 0 && !e.Marked ? SwimTheme.Dim : Color.White);
            Size ts = TextRenderer.MeasureText(g, time, timeFont, Size.Empty, TextFormatFlags.NoPadding);
            float timeRight = Math.Min(colT, valueLeft - S(5));
            Write(g, time, timeFont, new RectangleF(timeRight - ts.Width - 1, r.Top, ts.Width + 2, r.Height), timeColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            float labelLeft = r.Left + S(12);
            float labelRight = timeRight - ts.Width - S(6);
            Color labelColor = next ? SwimTheme.Amber : (timed || (e.Checkpoint > 0 && e.Marked) ? SwimTheme.Text : SwimTheme.Faint);
            Write(g, (e.Pilot ? "✈ " : "") + e.Label, labelFont, new RectangleF(labelLeft, r.Top, Math.Max(4, labelRight - labelLeft), r.Height), labelColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        /// <summary>A distance in its box, as a number box (the arrows drawn): a click opens the editor in its place.</summary>
        private void DrawValueBox(Graphics g, List<Item> list, SwimBarEvent e, RectangleF box, SwimRace race)
        {
            string id = "evd:" + e.Id;
            bool hover = hoverId == id;
            SwimTheme.FillRounded(g, SwimTheme.Input, box, 3);
            SwimTheme.DrawRounded(g, hover ? SwimTheme.Accent : SwimTheme.Mix(SwimTheme.Border, SwimTheme.Accent, 0.35), box, 3);

            // The arrows of the box.
            float aw = S(9);
            float ax = box.Right - aw - S(2);
            float mid = box.Top + box.Height / 2;
            using (SolidBrush arrow = new SolidBrush(hover ? SwimTheme.Accent : SwimTheme.Dim))
            {
                g.FillPolygon(arrow, new[] { new PointF(ax + aw / 2, mid - S(6)), new PointF(ax + aw - S(1), mid - S(2)), new PointF(ax + S(1), mid - S(2)) });
                g.FillPolygon(arrow, new[] { new PointF(ax + S(1), mid + S(2)), new PointF(ax + aw - S(1), mid + S(2)), new PointF(ax + aw / 2, mid + S(6)) });
            }

            string text = e.DistanceValue.HasValue ? (e.DistanceEstimated ? "≈" : "") + SwimDecimalBox.Format(e.DistanceValue) : "–";
            Font font = SwimTheme.Ui(8f, FontStyle.Bold);
            Font unitFont = SwimTheme.Ui(6.5f, FontStyle.Regular);
            string unit = race.IsYards ? "yd" : SwimLang.T("μ.");
            Size us = TextRenderer.MeasureText(g, unit, unitFont, Size.Empty, TextFormatFlags.NoPadding);
            RectangleF ur = new RectangleF(ax - S(2) - us.Width, box.Top, us.Width + 1, box.Height);
            Write(g, unit, unitFont, ur, SwimTheme.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            Color color = !e.DistanceValue.HasValue ? SwimTheme.Faint : e.DistanceEstimated ? SwimTheme.Dim : SwimTheme.AccentLight;
            Write(g, text, font, new RectangleF(box.Left + S(2), box.Top, ur.Left - box.Left - S(4), box.Height), color,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            string tip = SwimLang.T("Κλικ: γράψε την απόσταση ή γύρνα τη ροδέλα (0.01 · Ctrl 0.10 · Shift 1.00), Enter για να μπει, Esc για ακύρωση.") +
                (e.DistanceEstimated ? SwimLang.T("\n≈ εκτίμηση από τους χρόνους, όχι μέτρηση.") : "");
            SwimBarEvent ev = e;
            RectangleF where = box;
            list.Add(new Item { Id = id, Bounds = box, Tip = tip, Click = () => OpenDistanceEditor(ev, where) });
        }

        private static string FormatRaceTime(double t)
        {
            return t >= 60 ? SwimReference.FormatTime(t) : (t < 0 ? "−" : "") + Math.Abs(t).ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>The first line so that line 'target' is 'above' pixels from the top.</summary>
        private int FirstFor(List<Line> lines, int target, float above)
        {
            int first = Math.Max(0, Math.Min(target, lines.Count - 1));
            float h = 0;
            while (first > 0 && h + LineHeight(lines[first - 1]) <= above)
            {
                h += LineHeight(lines[first - 1]);
                first--;
            }

            return first;
        }

        /// <summary>The last first line: the end of the list at the bottom.</summary>
        private int MaxFirst(List<Line> lines, float height)
        {
            float h = 0;
            int first = lines.Count;
            while (first > 0 && h + LineHeight(lines[first - 1]) <= height + 0.5f)
            {
                h += LineHeight(lines[first - 1]);
                first--;
            }

            return Math.Max(0, first);
        }

        /// <summary>A thin bar on the right of the list: the part shown.</summary>
        private void DrawScrollMark(Graphics g, RectangleF area, int first, int shown, int total)
        {
            if (total <= 0)
                return;

            float trackTop = area.Top + S(2);
            float trackHeight = area.Height - S(4);
            float h = Math.Max(S(14), trackHeight * shown / total);
            float y = trackTop + (trackHeight - h) * (total > shown ? (float)first / (total - shown) : 0);
            using (SolidBrush track = new SolidBrush(Color.FromArgb(22, SwimTheme.Accent)))
                g.FillRectangle(track, area.Right - S(2), trackTop, S(2), trackHeight);
            using (SolidBrush thumb = new SolidBrush(Color.FromArgb(150, SwimTheme.Accent)))
                g.FillRectangle(thumb, area.Right - S(2), y, S(2), h);
        }
        #endregion

        #region Details of the race
        private enum InfoEdit
        {
            None,
            Window,
            Athlete,
            Lane,
            Distance,
            Style,
            Pool,
            Round,
            Text
        }

        private sealed class InfoRow
        {
            public string Id;
            public string Label;
            public string Value;
            public Color Color;
            public InfoEdit Edit;

            /// <summary>The text edited (Text), the field it goes to.</summary>
            public string Field;
            public string Text;
            public string Tip;
        }

        private List<InfoRow> InfoRows(SwimRace race)
        {
            List<InfoRow> rows = new List<InfoRow>();
            Func<string, string> orDash = s => string.IsNullOrWhiteSpace(s) ? "–" : s.Trim();
            rows.Add(new InfoRow
            {
                Id = "kind", Label = SwimLang.T("Είδος"), Value = race.IsTraining ? SwimLang.T("Προπόνηση") : SwimLang.T("Αγώνας"), Edit = InfoEdit.Window,
                Tip = SwimLang.T("Αγώνας ή βίντεο προπόνησης: αλλάζει στον πίνακα (κλικ: ανοίγει στα στοιχεία).")
            });
            bool registry = !string.IsNullOrEmpty(race.AthleteId);
            rows.Add(new InfoRow
            {
                Id = "athlete", Label = SwimLang.T("Αθλητής"), Value = orDash(race.Athlete) + (registry ? "  ✓" : ""), Edit = InfoEdit.Athlete,
                Tip = (registry ? SwimLang.T("Από το μητρώο του KOE App (✓).") : SwimLang.T("Όχι από το μητρώο του KOE App.")) +
                    SwimLang.T("\nΚλικ: η αναζήτηση αθλητή στον πίνακα.")
            });
            rows.Add(new InfoRow
            {
                Id = "lane", Label = SwimLang.T("Διάδρομος"), Value = race.Lane > 0 ? race.Lane.ToString(CultureInfo.InvariantCulture) : "–", Edit = InfoEdit.Lane,
                Tip = SwimLang.T("Κλικ: γράψε τον διάδρομο ή γύρνα τη ροδέλα.")
            });
            rows.Add(new InfoRow
            {
                Id = "distance", Label = SwimLang.T("Απόσταση"), Value = race.DistanceLabel + (race.IsYards ? "yd" : SwimLang.T("μ.")), Edit = InfoEdit.Distance,
                Tip = SwimLang.T("Κλικ: διάλεξε την απόσταση (τα σημεία ελέγχου γίνονται τα προεπιλεγμένα).")
            });
            rows.Add(new InfoRow
            {
                Id = "style", Label = SwimLang.T("Στυλ"), Value = SwimPresets.StyleName(race.Style), Edit = InfoEdit.Style,
                Tip = SwimLang.T("Κλικ: διάλεξε το στυλ.")
            });
            rows.Add(new InfoRow
            {
                Id = "pool", Label = SwimLang.T("Πισίνα"), Value = race.PoolLabel, Edit = InfoEdit.Pool,
                Tip = SwimLang.T("Κλικ: διάλεξε την πισίνα (50μ., 25μ., 25yd).")
            });
            rows.Add(new InfoRow
            {
                Id = "round", Label = SwimLang.English ? "Round" : "Φάση", Value = orDash(race.Round), Edit = InfoEdit.Round,
                Tip = SwimLang.T("Κλικ: προκριματικός, ημιτελικός, τελικός ή άλλη.")
            });
            rows.Add(new InfoRow
            {
                Id = "competition", Label = SwimLang.T("Διοργάνωση"), Value = orDash(race.Competition), Edit = InfoEdit.Text, Field = "competition", Text = race.Competition,
                Tip = SwimLang.T("Κλικ: γράψε τη διοργάνωση.")
            });
            rows.Add(new InfoRow
            {
                Id = "location", Label = SwimLang.T("Τόπος"), Value = orDash(race.Location), Edit = InfoEdit.Text, Field = "location", Text = race.Location,
                Tip = SwimLang.T("Κλικ: γράψε τον τόπο.")
            });
            rows.Add(new InfoRow
            {
                Id = "date", Label = SwimLang.T("Ημερομηνία"), Value = orDash(race.Date), Edit = InfoEdit.Text, Field = "date", Text = race.Date,
                Tip = SwimLang.T("Κλικ: γράψε την ημερομηνία (π.χ. 18.12.2026).")
            });

            bool official = race.Official != null && race.Official.Count > 0;
            string link = race.ResultsLink ?? "";
            rows.Add(new InfoRow
            {
                Id = "results",
                Label = SwimLang.T("Αποτελέσματα"),
                Value = official ? SwimLang.T("✓ επίσημοι χρόνοι") : link.Length > 0 ? "🔗 " + link : "–",
                Color = official ? SwimTheme.Green : Color.Empty,
                Edit = InfoEdit.Text,
                Field = "results",
                Text = link,
                Tip = SwimLang.T("Κλικ: επικόλλησε τον σύνδεσμο των αποτελεσμάτων (live της ΚΟΕ, Microplus, World Aquatics): με Enter αναζητούνται ") +
                    SwimLang.T("ο αγώνας και οι επίσημοι χρόνοι, όπως με το «🔗 Επίσημα αποτελέσματα» του πίνακα.")
            });
            string cps = SwimPresets.FormatCheckpoints(race.Checkpoints ?? new List<int>());
            rows.Add(new InfoRow
            {
                Id = "checkpoints", Label = SwimLang.T("Σημεία ελέγχου"), Value = cps.Replace(",", ""), Edit = InfoEdit.Text, Field = "checkpoints", Text = cps,
                Tip = SwimLang.T("Κλικ: γράψε τα σημεία ελέγχου με τη σειρά (π.χ. 15, 25, 35, 45, 50).") + "\n" + cps
            });
            return rows;
        }

        /// <summary>The details of the race, a row each: a click changes it in place; the window at the page of the details at the bottom.</summary>
        private void PaintRaceInfo(Graphics g, List<Item> list, RectangleF inner, SwimRace race)
        {
            List<InfoRow> rows = InfoRows(race);
            float rx = inner.Left + S(7);
            float rw = inner.Right - S(9) - rx;
            RectangleF button = new RectangleF(rx, inner.Bottom - S(8) - S(26), rw, S(26));
            RectangleF area = new RectangleF(inner.Left + S(3), inner.Top, inner.Width - S(6), button.Top - S(6) - inner.Top);
            if (area.Height < RowHeight)
                return;
            listArea = area;

            float rowHeight = S(22);
            int capacity = Math.Max(1, (int)(area.Height / rowHeight));
            infoFirst = Math.Max(0, Math.Min(Math.Max(0, rows.Count - capacity), infoFirst));
            Font labelFont = SwimTheme.Ui(7.5f, FontStyle.Regular);
            Font valueFont = SwimTheme.Ui(8.25f, FontStyle.Regular);
            float labelWidth = Math.Min(S(86), rw * 0.4f);
            for (int i = infoFirst; i < rows.Count && i < infoFirst + capacity; i++)
            {
                InfoRow row = rows[i];
                RectangleF r = new RectangleF(rx, area.Top + (i - infoFirst) * rowHeight, rw, rowHeight - S(2));
                string id = "info:" + row.Id;
                bool hover = hoverId == id;
                if (hover)
                    SwimTheme.FillRounded(g, SwimTheme.RaisedHover, r, 3);
                else
                {
                    using (Pen line = new Pen(Color.FromArgb(28, SwimTheme.Accent), 1f))
                        g.DrawLine(line, r.Left + S(4), r.Bottom + S(1), r.Right - S(4), r.Bottom + S(1));
                }

                Write(g, row.Label, labelFont, new RectangleF(r.Left + S(4), r.Top, labelWidth - S(6), r.Height), SwimTheme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                // What a click does: a list (▾), a text (✎), the window (↗); a number in its box.
                string mark = row.Edit == InfoEdit.Window || row.Edit == InfoEdit.Athlete ? "↗" :
                    row.Edit == InfoEdit.Text ? "✎" : row.Edit == InfoEdit.Lane ? "" : "▾";
                float markWidth = S(12);
                if (mark.Length > 0)
                    Write(g, mark, SwimTheme.Ui(7.5f, FontStyle.Regular), new RectangleF(r.Right - markWidth - S(2), r.Top, markWidth, r.Height), hover ? SwimTheme.Accent : SwimTheme.Faint,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

                RectangleF value = new RectangleF(r.Left + labelWidth, r.Top, r.Width - labelWidth - markWidth - S(4), r.Height);
                if (editId == id)
                {
                    // The editor is over it.
                }
                else if (row.Edit == InfoEdit.Lane)
                {
                    RectangleF box = new RectangleF(value.Left, r.Top + S(2), S(42), r.Height - S(4));
                    SwimTheme.FillRounded(g, SwimTheme.Input, box, 3);
                    SwimTheme.DrawRounded(g, hover ? SwimTheme.Accent : SwimTheme.Mix(SwimTheme.Border, SwimTheme.Accent, 0.35), box, 3);
                    Write(g, row.Value, SwimTheme.Ui(8f, FontStyle.Bold), new RectangleF(box.Left + S(4), box.Top, box.Width - S(14), box.Height), SwimTheme.AccentLight,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    float aw = S(8), ax = box.Right - aw - S(2), mid = box.Top + box.Height / 2;
                    using (SolidBrush arrow = new SolidBrush(hover ? SwimTheme.Accent : SwimTheme.Dim))
                    {
                        g.FillPolygon(arrow, new[] { new PointF(ax + aw / 2, mid - S(5)), new PointF(ax + aw, mid - S(1)), new PointF(ax, mid - S(1)) });
                        g.FillPolygon(arrow, new[] { new PointF(ax, mid + S(1)), new PointF(ax + aw, mid + S(1)), new PointF(ax + aw / 2, mid + S(5)) });
                    }
                }
                else
                {
                    Color color = row.Color != Color.Empty ? row.Color : (row.Value == "–" ? SwimTheme.Faint : SwimTheme.Text);
                    Write(g, row.Value, valueFont, value, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                }

                InfoRow captured = row;
                RectangleF where = value;
                list.Add(new Item { Id = id, Bounds = r, Tip = row.Tip, Click = () => EditInfo(captured, where, race) });
            }

            if (rows.Count > capacity)
                DrawScrollMark(g, area, infoFirst, capacity, rows.Count);

            DrawButton(g, list, "info-window", button, SwimLang.T("Όλα στον πίνακα  ↗"), null, null, Look.Normal, false,
                SwimLang.T("Ανοίγει τον πίνακα της ανάλυσης στα στοιχεία του αγώνα (αθλητής από το μητρώο, επίσημα αποτελέσματα, σημεία ελέγχου)."),
                () => manager.ShowPanelPage("race", false));
        }

        private void EditInfo(InfoRow row, RectangleF where, SwimRace race)
        {
            CloseEditor(true);
            switch (row.Edit)
            {
                case InfoEdit.Window:
                    manager.ShowPanelPage("race", false);
                    break;
                case InfoEdit.Athlete:
                    manager.ShowPanelPage("race", true);
                    break;
                case InfoEdit.Lane:
                    OpenNumberEditor("info:" + row.Id, new RectangleF(where.Left, where.Top + S(1), S(54), where.Height - S(2)), race.Lane > 0 ? (double?)race.Lane : null, 0, 10, 1,
                        v => manager.SetBarLane(v.HasValue ? (int)Math.Round(v.Value) : 0));
                    break;
                case InfoEdit.Distance:
                {
                    string[] choices = SwimPresets.DistancesFor(race);
                    ShowChoices(where, choices.Select(c => c + (race.IsYards ? "yd" : SwimLang.T("μ."))).ToArray(), Array.IndexOf(choices, race.DistanceLabel),
                        i => manager.SetBarEvent(choices[i]));
                    break;
                }
                case InfoEdit.Style:
                {
                    string[] names = SwimPresets.Styles.Select(x => SwimPresets.StyleName(x)).ToArray();
                    ShowChoices(where, names, Array.IndexOf(SwimPresets.Styles, race.Style), i => manager.SetBarStyle(SwimPresets.Styles[i]));
                    break;
                }
                case InfoEdit.Pool:
                    ShowChoices(where, SwimPresets.PoolNames, SwimPresets.PoolIndex(race), i => manager.SetBarPool(i));
                    break;
                case InfoEdit.Round:
                {
                    string[] rounds = SwimLang.English
                        ? new[] { "Heats", "Semi-final", "Final" }
                        : new[] { "Προκριματικός", "Ημιτελικός", "Τελικός", "Heats", "Semi-final", "Final" };
                    string other = SwimLang.T("Άλλη…");
                    string[] items = rounds.Concat(new[] { other }).ToArray();
                    RectangleF at = where;
                    ShowChoices(where, items, Array.IndexOf(rounds, race.Round), i =>
                    {
                        if (i < rounds.Length)
                            manager.SetBarText("round", rounds[i]);
                        else
                            OpenTextEditor("info:round", at, race.Round, text => manager.SetBarText("round", text));
                    });
                    break;
                }
                case InfoEdit.Text:
                {
                    string field = row.Field;
                    OpenTextEditor("info:" + row.Id, where, row.Text ?? "", text => manager.SetBarText(field, text));
                    break;
                }
            }
        }

        /// <summary>A list of choices under a row (the menu of the analysis), the current one checked.</summary>
        private void ShowChoices(RectangleF where, string[] items, int selected, Action<int> chosen)
        {
            ContextMenuStrip menu = new ContextMenuStrip { Renderer = new SwimMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
            menu.Font = SwimTheme.Ui(9f, FontStyle.Regular);
            string raceId = manager.Race != null ? manager.Race.Id : null;
            int picked = -1;
            for (int i = 0; i < items.Length; i++)
            {
                int index = i;
                ToolStripMenuItem item = new ToolStripMenuItem(items[i]) { Checked = i == selected };
                item.Click += (s, e) => picked = index;
                menu.Items.Add(item);
            }

            // The choice once the menu is closed (an editor it opens, «Άλλη…», keeps the keys), for the race it was opened on; else the
            // keys back to the video.
            menu.Closed += (s, e) => Later(() =>
            {
                menu.Dispose();
                try
                {
                    if (picked >= 0 && manager.Race != null && manager.Race.Id == raceId)
                        chosen(picked);
                }
                catch (Exception ex)
                {
                    log.Error("Error while putting in a detail of the race chosen in the bar of the swim analysis.", ex);
                }

                if (editor == null)
                    manager.BarClicked();
                RefreshNow();
            });
            menu.Show(this, new Point((int)where.Left, (int)where.Bottom + 1));
        }
        #endregion

        #region Editors in place
        private Control editor;
        private string editId;

        /// <summary>The race the editor was opened on: its value goes to that race only (another video may be open by then).</summary>
        private string editRaceId;
        private Action<double?> numberCommit;
        private Action<string> textCommit;
        private bool editClosing;

        /// <summary>The distance of an event in its box: typed or with the wheel and the arrows, two decimals.</summary>
        private void OpenDistanceEditor(SwimBarEvent e, RectangleF box)
        {
            SwimBarDistance which = e.Distance;
            int wall = e.Wall;
            OpenNumberEditor(e.Id, box, e.DistanceEstimated ? null : e.DistanceValue, 2, 50, e.Seed, v => manager.SetBarDistance(which, wall, v));
        }

        private void OpenNumberEditor(string id, RectangleF box, double? value, int decimals, int maximum, double seed, Action<double?> commit)
        {
            CloseEditor(true);
            SwimDecimalBox b = new SwimDecimalBox
            {
                Inline = true,
                DecimalPlaces = decimals,
                Increment = decimals == 0 ? 1m : 0.01m,
                Maximum = maximum,
                Seed = (decimal)seed,
                Font = SwimTheme.Ui(8.25f, FontStyle.Bold),
                BackColor = SwimTheme.Input,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            // A whole number (a lane): steps of 1, also when an arrow is held.
            if (decimals == 0)
                b.Accelerations.Clear();
            b.Number = value;
            // Every change of the wheel or the arrows goes in at once (the graphics follow); Enter / Tab ends, Esc drops what was typed.
            b.NumberChanged += (s, e) =>
            {
                if (!editClosing && editor == b && numberCommit != null && EditingThisRace)
                    numberCommit(b.Number);
            };
            b.Committed += (s, e) => CloseEditor(true);
            b.Cancelled += (s, e) => CloseEditor(false);
            b.Leave += (s, e) => Later(() => { if (editor == b) CloseEditor(true); });
            numberCommit = commit;
            textCommit = null;
            ShowEditor(id, b, box);
            b.Select(0, b.Text.Length);
        }

        private void OpenTextEditor(string id, RectangleF box, string text, Action<string> commit)
        {
            CloseEditor(true);
            SwimInlineTextBox t = new SwimInlineTextBox
            {
                Text = text ?? "",
                Font = SwimTheme.Ui(8.25f, FontStyle.Regular),
                BackColor = SwimTheme.Input,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            t.Committed += (s, e) => CloseEditor(true);
            t.Cancelled += (s, e) => CloseEditor(false);
            t.Leave += (s, e) => Later(() => { if (editor == t) CloseEditor(true); });
            textCommit = commit;
            numberCommit = null;
            // The text may be longer than its place: the editor takes the width of the row up to the right edge.
            RectangleF wide = new RectangleF(box.Left, box.Top, Math.Max(box.Width, Width - S(10) - box.Left), box.Height);
            ShowEditor(id, t, wide);
            t.SelectAll();
        }

        /// <summary>After the event that is handled now (a menu closed, a focus lost), when the bar is still there.</summary>
        private void Later(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
                return;

            BeginInvoke(action);
        }

        /// <summary>The editor of a value is open on the race shown (its value goes there).</summary>
        private bool EditingThisRace
        {
            get { return editor != null && manager.Race != null && manager.Race.Id == editRaceId; }
        }

        private void ShowEditor(string id, Control c, RectangleF box)
        {
            editId = id;
            editor = c;
            editRaceId = manager.Race != null ? manager.Race.Id : null;
            int height = Math.Max((int)Math.Round(box.Height), c.PreferredSize.Height);
            c.Bounds = new Rectangle((int)Math.Round(box.Left), (int)Math.Round(box.Top + (box.Height - height) / 2), Math.Max(40, (int)Math.Round(box.Width)), height);
            Controls.Add(c);
            c.BringToFront();
            c.Focus();
            Invalidate();
        }

        /// <summary>Ends the editor: its value in (keep) or dropped, the keys back to the video.</summary>
        private void CloseEditor(bool keep)
        {
            Control c = editor;
            if (c == null || editClosing)
                return;

            editClosing = true;
            try
            {
                if (keep && EditingThisRace)
                {
                    SwimDecimalBox number = c as SwimDecimalBox;
                    if (number != null && numberCommit != null)
                        numberCommit(number.Number);
                    SwimInlineTextBox text = c as SwimInlineTextBox;
                    if (text != null && textCommit != null)
                        textCommit(text.Text);
                }
            }
            catch (Exception ex)
            {
                log.Error("Error while putting in a value changed in the bar of the swim analysis.", ex);
            }
            finally
            {
                editor = null;
                editId = null;
                editRaceId = null;
                numberCommit = null;
                textCommit = null;
                Controls.Remove(c);
                c.Dispose();
                editClosing = false;
            }

            if (!IsDisposed)
            {
                manager.BarClicked();
                RefreshNow();
            }
        }
        #endregion

        #region Wheel
        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);

        /// <summary>
        /// The wheel over the list of the right bar scrolls it (the bar never has the focus: the wheel would go to the video). Not over
        /// another window in front of the bar, nor over the editor of a value (its own wheel).
        /// </summary>
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg != WM_MOUSEWHEEL || left || IsDisposed || !IsHandleCreated || !Visible || listArea.IsEmpty)
                return false;

            long lp = m.LParam.ToInt64();
            Point screen = new Point(unchecked((short)(lp & 0xFFFF)), unchecked((short)((lp >> 16) & 0xFFFF)));
            if (WindowFromPoint(screen) != Handle)
                return false;

            Point p = PointToClient(screen);
            if (!listArea.Contains(p))
                return false;

            long wp = m.WParam.ToInt64();
            int delta = unchecked((short)((wp >> 16) & 0xFFFF));
            ScrollList(delta);
            return true;
        }

        private void ScrollList(int delta)
        {
            CloseEditor(true);
            wheelRest += delta;
            int notches = wheelRest / 120;
            if (notches == 0)
                return;

            wheelRest -= notches * 120;
            switch (ListTab)
            {
                case 0:
                {
                    eventsFirst = Math.Max(0, eventsFirst - notches * 3);
                    eventsManual = true;
                    int current = SwimBarEvents.CurrentIndex(events, manager.CurrentRaceTime ?? PreviewTime);
                    eventsAnchor = current >= 0 && current < events.Count ? events[current].Id : "";
                    break;
                }
                case 1:
                    splitsFirst = Math.Max(0, splitsFirst - notches * 3);
                    splitsManual = true;
                    splitsAnchor = manager.Computed != null && manager.Computed.Rows != null ? manager.Computed.Rows.FindIndex(r => !r.Marked) : -1;
                    break;
                default:
                    infoFirst = Math.Max(0, infoFirst - notches * 2);
                    break;
            }

            Invalidate();
        }
        #endregion
    }
}
