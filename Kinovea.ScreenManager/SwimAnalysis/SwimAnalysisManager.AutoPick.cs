#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using Kinovea.Services;
using Kinovea.Video;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The swimmer of the whole race without a click. The camera of a race follows its swimmer: he is in the lane at the middle of
    /// the image and swims the way the camera turns. "🤖 Όλος ο αγώνας" proposes him on a frame a little after the start (shown on
    /// the video); Enter starts the reading with him, ↑/↓ move the proposal to the lane above / below, a click on another swimmer
    /// starts it with that one, as before. A camera that does not move follows nobody, and without the ropes of the lane there is
    /// no lane: then the click is asked.
    /// (Lab, 33 races of the analysts: the right swimmer in about 5 of 6 races with a moving camera; the analyst checks it here.)
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>Where the swimmer is looked for, after the start (the analysts click about then: after the breakout).</summary>
        private const double AutoPickSeconds = 6.0;

        /// <summary>Length of the swimmer in widths of his lane (the analysts' clicks of the test races: median 1.7).</summary>
        private const double AutoPickLength = 1.7;

        /// <summary>
        /// Camera motion (pixels of the working image a frame) below which the camera does not follow a swimmer: fixed cameras of
        /// the test races 0.09–0.21, the slowest pan 0.75.
        /// </summary>
        private const double AutoPickMinShift = 0.4;

        /// <summary>
        /// Probes of the lane on the middle row of the image (place and length, parts of its width), the first one the lane proposed:
        /// that lane is surely a lane when enough of the probes find it too (a line of the floor, a splash or a glint taken for a
        /// rope gives a lane of another width at another place). Lab, 35 races of the analysts' camcorders: with 4 of the 6
        /// agreeing, 27 of the 28 races kept had the lane width of the analyst's click (3 of the 4 wrong widths left out); 25 of
        /// the 28 views of the whole pool from the stands (phones: no swimmer followed, lanes 13–38 px) left out.
        /// </summary>
        private static readonly double[][] AutoPickProbes =
        {
            new[] { 0.5, 0.17 }, new[] { 0.3, 0.17 }, new[] { 0.7, 0.17 }, new[] { 0.5, 0.3 }, new[] { 0.3, 0.3 }, new[] { 0.7, 0.3 }
        };

        private const int AutoPickAgree = 4;

        private sealed class AutoPickResult
        {
            public PointF Head;
            public PointF Feet;
            public long Frame;

            /// <summary>Width of the lane at the swimmer, in pixels of the image.</summary>
            public float LaneWidth;
        }

        /// <summary>The swimmer proposed for the reading of the whole race, null if none.</summary>
        private AutoPickResult autoPick;

        /// <summary>
        /// While the click on the swimmer of the whole race is waited for: the swimmer the camera follows, on a frame of the first
        /// length (the frame shown when it is after the breakout of the start, else 6 s after the start). The video goes there.
        /// </summary>
        private void ProposeAutoPick()
        {
            // Whatever goes wrong here, the click on the swimmer works as before.
            try
            {
                FindAutoPick();
            }
            catch (Exception e)
            {
                log.Error("Automatic choice of the swimmer failed.", e);
                autoPick = null;
            }
        }

        private void FindAutoPick()
        {
            autoPick = null;
            RefreshTiming();
            if (Race == null || Race.IsTraining || !Race.StartTimestamp.HasValue || !Timing.IsValid || !HasVideo || AutoStrokesRunning)
                return;

            long start = Race.StartTimestamp.Value;
            long? now = CurrentVideoTimestamp;
            double since = now.HasValue ? Timing.Seconds(now.Value, start) : -1;
            long at = since >= 3.5 && since <= 10 ? now.Value : start + RealSpan(AutoPickSeconds);
            VideoSection zone = frameServer.VideoReader.WorkingZone;
            if (at > zone.End || at <= zone.Start)
                return;

            AutoPickResult found = null;
            string reason = null;
            Exception error = null;
            AutoStrokesRunning = true;
            if (form != null && !form.IsDisposed)
                form.RefreshAutoStrokes();

            player.BeforeExportVideo();
            try
            {
                using (BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true })
                using (FormProgressBar progress = new FormProgressBar(true))
                {
                    progress.CancelAsked += (o, e) => worker.CancelAsync();
                    worker.ProgressChanged += (o, e) => progress.Update(e.ProgressPercentage, 100, true);
                    worker.DoWork += (o, e) =>
                    {
                        string why;
                        e.Result = AutoPickProbe(at, zone.Start, (BackgroundWorker)o, out why);
                        reason = why;
                    };
                    worker.RunWorkerCompleted += (o, e) =>
                    {
                        error = e.Error;
                        if (e.Error == null)
                            found = e.Result as AutoPickResult;
                        progress.Close();
                    };

                    worker.RunWorkerAsync();
                    progress.ShowDialog();
                }
            }
            finally
            {
                AutoStrokesRunning = false;
                frameServer.VideoReader.AfterFrameEnumeration();
                if (player != null && !player.IsDisposed)
                    player.AfterExportVideo();
                if (form != null && !form.IsDisposed)
                    form.RefreshAutoStrokes();
            }

            // The click may have been cancelled meanwhile (Esc).
            if (!pickForRace || !PickingTrack)
                return;

            if (error != null)
            {
                log.Error("Automatic choice of the swimmer failed.", error);
                return;
            }

            if (found == null)
            {
                if (reason == "static")
                    Status("🤖 Αυτόματη ανάλυση αγώνα: η κάμερα δεν κινείται, οπότε δεν ακολουθεί κάποιον αθλητή. Σε ένα καρέ μετά την έξοδο από το νερό, " +
                        "κλικ στο κεφάλι του αθλητή και μετά στα πόδια του (Esc: ακύρωση).", false);
                else if (reason == "ropes")
                    Status("🤖 Αυτόματη ανάλυση αγώνα: δεν βρέθηκαν τα σχοινιά της διαδρομής στη μέση της εικόνας. Σε ένα καρέ μετά την έξοδο από το νερό, " +
                        "κλικ στο κεφάλι του αθλητή και μετά στα πόδια του (Esc: ακύρωση).", false);
                else if (reason == "unsure")
                    Status("🤖 Αυτόματη ανάλυση αγώνα: η διαδρομή στη μέση της εικόνας δεν είναι σίγουρη (π.χ. πλάνο όλης της πισίνας ή πολλά νερά). " +
                        "Σε ένα καρέ μετά την έξοδο από το νερό, κλικ στο κεφάλι του αθλητή και μετά στα πόδια του (Esc: ακύρωση).", false);
                return;
            }

            autoPick = found;
            SeekTo(found.Frame);
            InvalidatePlayer();
            Status("🤖 Ο αθλητής που ακολουθεί η κάμερα σημειώθηκε στο βίντεο: Enter και η ανάγνωση όλου του αγώνα ξεκινά με αυτόν · " +
                "↑/↓: ο αθλητής της διαδρομής από πάνω / από κάτω · για άλλον αθλητή: κλικ στο κεφάλι του και μετά στα πόδια του (Esc: ακύρωση).", false);
        }

        /// <summary>
        /// Background thread: the camera motion over the 1.5 s before 'at' (its direction is the way the swimmer swims), then the
        /// lane at the middle of the image at 'at', checked by the other probes. Null with the reason ("static", "ropes", "unsure")
        /// when there is no swimmer to propose, or when cancelled (no reason).
        /// </summary>
        private AutoPickResult AutoPickProbe(long at, long first, BackgroundWorker worker, out string reason)
        {
            reason = null;
            Size reference = ReadingReader.Info.ReferenceSize;
            int width = StrokeWorkWidth;
            int height = Math.Max(2, (int)Math.Round(width * (double)reference.Height / reference.Width / 2.0) * 2);
            double sx = (double)width / reference.Width, sy = (double)height / reference.Height;
            double hsf = Timing.HighSpeedFactor <= 0 ? 1.0 : Timing.HighSpeedFactor;
            double realFps = 1000.0 * hsf / Timing.FrameIntervalMilliseconds;
            int step = Math.Max(1, (int)Math.Round(realFps / 60.0));
            double xm = width / 2.0, ym = height / 2.0;

            // The middle of the image moves with the camera: the mean of the motion at the middle, as the lab measures it.
            double sum = 0;
            int n = 0;
            long from = Math.Max(first, at - RealSpan(1.5));
            bool read = ReadShared(new List<StrokeSession>(), width, height, step, from, at, worker, h =>
            {
                double x, y;
                SwimStrokeDetector.MapPoint(h, xm, ym, out x, out y);
                if (double.IsNaN(x) || Math.Abs(x - xm) > 0.25 * width)
                    return;
                sum += x - xm;
                n++;
            }, null);
            if (!read)
                return null;

            double shift = n > 0 ? sum / n : 0;
            if (Math.Abs(shift) < AutoPickMinShift)
            {
                reason = "static";
                return null;
            }

            // The pool moves the other way of the swimmer in the image: he swims to the side the camera turns to.
            int direction = shift < 0 ? 1 : -1;
            SwimStrokeLane lane = new SwimStrokeLane();
            long frame = at;
            double laneWidth = 0, middle = 0;
            int agree = 0;
            for (int k = 0; k < AutoPickProbes.Length; k++)
            {
                double px = AutoPickProbes[k][0] * width, probeLength = AutoPickProbes[k][1] * width;
                PointF head = new PointF((float)((px + direction * 0.5 * probeLength) / sx), (float)(ym / sy));
                PointF feet = new PointF((float)((px - direction * 0.5 * probeLength) / sx), (float)(ym / sy));
                using (StrokeSession probe = CreateSession(head, feet, at))
                {
                    if (!ReadFrames(probe, at, worker))
                        return null;

                    bool found = probe.Timestamps.Count > 0 && probe.Detector.FoundRopes;
                    if (k == 0)
                    {
                        if (!found)
                        {
                            reason = "ropes";
                            return null;
                        }

                        lane = probe.Detector.CurrentLane;
                        frame = probe.Timestamps[0];
                        laneWidth = Math.Max(5, Math.Abs(lane.Y2(xm) - lane.Y1(xm)));
                        middle = 0.5 * (lane.Y1(xm) + lane.Y2(xm));
                        agree++;
                        continue;
                    }

                    if (!found)
                        continue;

                    // The same lane: its middle near the one of the lane proposed, the same width (lines at the middle of the image).
                    SwimStrokeLane other = probe.Detector.CurrentLane;
                    double w = Math.Abs(other.Y2(xm) - other.Y1(xm)), y = 0.5 * (other.Y1(xm) + other.Y2(xm));
                    if (Math.Abs(y - middle) <= 0.25 * laneWidth && w >= 0.8 * laneWidth && w <= 1.25 * laneWidth)
                        agree++;
                }

                // Not enough of them left to agree.
                if (agree + (AutoPickProbes.Length - 1 - k) < AutoPickAgree)
                    break;
            }

            if (agree < AutoPickAgree)
            {
                log.DebugFormat("Automatic choice of the swimmer: {0} of {1} probes on the lane at the middle, not proposed.", agree, AutoPickProbes.Length);
                reason = "unsure";
                return null;
            }

            double length = AutoPickLength * laneWidth;
            return new AutoPickResult
            {
                Head = new PointF((float)((xm + direction * 0.5 * length) / sx), (float)(middle / sy)),
                Feet = new PointF((float)((xm - direction * 0.5 * length) / sx), (float)(middle / sy)),
                Frame = frame,
                LaneWidth = (float)(laneWidth / sy)
            };
        }

        /// <summary>
        /// ↑/↓ while the swimmer of the whole race is proposed: the swimmer of the lane above (step −1) or below (+1), a lane width
        /// away across the lane (the reading finds the ropes of the lane at the click, so near enough is right). False when not used.
        /// </summary>
        private bool MoveAutoPick(PlayerScreenUserInterface screen, int step)
        {
            AutoPickResult p = autoPick;
            if (p == null || screen != player || !HasVideo || !pickForRace || !PickingTrack || PickTarget != SwimPickTarget.StrokeHead)
                return false;

            float dx = p.Head.X - p.Feet.X, dy = p.Head.Y - p.Feet.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 1 || p.LaneWidth <= 0)
                return true;

            // Across the lane, downwards in the image for +1.
            float nx = -dy / length, ny = dx / length;
            if (ny < 0)
            {
                nx = -nx;
                ny = -ny;
            }

            float d = step * p.LaneWidth;
            PointF head = new PointF(p.Head.X + nx * d, p.Head.Y + ny * d), feet = new PointF(p.Feet.X + nx * d, p.Feet.Y + ny * d);
            Size reference = ReadingReader.Info.ReferenceSize;
            float middle = 0.5f * (head.Y + feet.Y);
            if (middle < 0.25f * p.LaneWidth || middle > reference.Height - 0.25f * p.LaneWidth)
                return true;

            autoPick = new AutoPickResult { Head = head, Feet = feet, Frame = p.Frame, LaneWidth = p.LaneWidth };
            SeekTo(p.Frame);
            InvalidatePlayer();
            Status("🤖 Η πρόταση άλλαξε διαδρομή: Enter και η ανάγνωση όλου του αγώνα ξεκινά με αυτόν τον αθλητή · ↑/↓ για άλλη διαδρομή · " +
                "ή κλικ στο κεφάλι του αθλητή και μετά στα πόδια του (Esc: ακύρωση).", false);

            // The auto pilot does not take a swimmer the analyst is choosing: it waits for his Enter.
            if (PilotActive && pilotStep == PilotStep.Swimmer)
            {
                StopPilotCountdown();
                PilotPrompt();
            }
            return true;
        }

        /// <summary>Enter while the swimmer of the whole race is proposed: the reading starts with him. False when not used.</summary>
        private bool AcceptAutoPick(PlayerScreenUserInterface screen)
        {
            AutoPickResult p = autoPick;
            if (p == null || screen != player || !HasVideo || !pickForRace || !PickingTrack || PickTarget != SwimPickTarget.StrokeHead)
                return false;

            autoPick = null;
            pickForTrack = false;
            pickForRace = false;
            pickForQuiet = false;
            SetPick(SwimPickTarget.None);
            InvalidatePlayer();
            AutoRace(p.Head, p.Feet, p.Frame);
            return true;
        }

        /// <summary>
        /// Screen only: the swimmer proposed, on his frame (a band along his lane from the feet to the head, an arrow the way he
        /// swims); on the other frames where to find him.
        /// </summary>
        private void DrawAutoPick(Graphics g, ImageTransform transform, long timestamp)
        {
            AutoPickResult p = autoPick;
            if (p == null || Race == null || !Race.StartTimestamp.HasValue)
                return;

            Color amber = Color.FromArgb(255, 255, 184, 0);
            if (!SameFrame(p.Frame, timestamp))
            {
                DrawLabel(g, string.Format(CultureInfo.InvariantCulture,
                    "🤖 Όλος ο αγώνας: Enter = ο αθλητής που ακολουθεί η κάμερα (σημειωμένος στο καρέ των {0:0.00} s) · ↑/↓ άλλη διαδρομή · ή κλικ στο κεφάλι άλλου αθλητή (Esc: ακύρωση)",
                    Timing.Seconds(p.Frame, Race.StartTimestamp.Value)), new Point(12, 12), amber);
                return;
            }

            Point head = transform.Transform(p.Head), feet = transform.Transform(p.Feet);
            float half = Math.Max(6, transform.Transform(p.LaneWidth) * 0.42f);
            float dx = head.X - feet.X, dy = head.Y - feet.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 4)
                return;

            float ux = dx / length, uy = dy / length;
            PointF[] band =
            {
                new PointF(feet.X - uy * half, feet.Y + ux * half),
                new PointF(head.X - uy * half, head.Y + ux * half),
                new PointF(head.X + uy * half, head.Y - ux * half),
                new PointF(feet.X + uy * half, feet.Y - ux * half)
            };

            using (SolidBrush fill = new SolidBrush(Color.FromArgb(46, amber)))
            using (Pen edge = new Pen(Color.FromArgb(235, amber), 2f) { DashPattern = new float[] { 5, 3 } })
            using (Pen arrow = new Pen(amber, 3f) { EndCap = LineCap.ArrowAnchor })
            {
                g.FillPolygon(fill, band);
                g.DrawPolygon(edge, band);
                g.DrawLine(arrow, head.X - ux * half * 1.2f, head.Y - uy * half * 1.2f, head.X + ux * half * 1.4f, head.Y + uy * half * 1.4f);
            }

            float top = Math.Min(Math.Min(band[0].Y, band[1].Y), Math.Min(band[2].Y, band[3].Y));
            float left = Math.Min(Math.Min(band[0].X, band[1].X), Math.Min(band[2].X, band[3].X));
            DrawLabel(g, "🤖 Enter: αυτός ο αθλητής · ↑/↓: άλλη διαδρομή · ή κλικ σε άλλον", new Point((int)left, (int)Math.Max(4, top - 26)), amber);
            DrawLabel(g, "🤖 Όλος ο αγώνας: Enter = ο αθλητής που ακολουθεί η κάμερα · ↑/↓ άλλη διαδρομή · κλικ στο κεφάλι για άλλον (Esc: ακύρωση)", new Point(12, 12), amber);
        }
    }
}
