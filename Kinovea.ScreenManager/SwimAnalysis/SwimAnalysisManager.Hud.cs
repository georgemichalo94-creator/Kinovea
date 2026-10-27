#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Kinovea.Video;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The HUD of the race on the video (2.9.1, as the demo): it replaces the boards, the velocity chart and the technical view when it
    /// is chosen («HUD» of the bar, the box of the panel); the boards of before are added to it with the boxes «Στο βίντεο». On the
    /// screen and in the video exports (the images of the report keep their board).
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region HUD
        /// <summary>The graphics of a race as the HUD of the demo (else the boards of before).</summary>
        public bool HudChosen
        {
            get { return Settings != null && Settings.HudLayout; }
        }

        /// <summary>The HUD is drawn on an image of this size (a race, the image large enough), else the boards.</summary>
        private bool HudReady(Size size)
        {
            return HudChosen && Race != null && !Race.IsTraining && Computed != null && SwimHudPainter.CanPaint(size);
        }

        /// <summary>The HUD (as the demo) or the boards of before on the video: the button of the bar, the box of the panel.</summary>
        public void SetHud(bool on)
        {
            if (Settings == null)
                return;

            Settings.HudLayout = on;
            Settings.Save();
            Status(on ? SwimLang.T("Γραφικά: HUD, όπως το demo. Τα πινακάκια προστίθενται με τα κουτάκια «Στο βίντεο» του πίνακα.")
                      : SwimLang.T("Γραφικά: πινακάκια, όπως πριν."), false);
            InvalidatePlayer();
            if (form != null && !form.IsDisposed)
                form.RefreshReel();
        }

        /// <summary>
        /// The HUD at this frame. size: the image (screen pixels, or logical units of an export); fromImage: point of the video image
        /// (reference pixels) → point in these units, null without the swimmer.
        /// </summary>
        private void DrawHud(Graphics g, long timestamp, Size size, Func<PointF, PointF> fromImage)
        {
            if (Race == null || Computed == null || Settings == null)
                return;

            bool started = Race.StartTimestamp.HasValue && Timing.IsValid;
            SwimHudScene scene = new SwimHudScene
            {
                Race = Race,
                Computed = Computed,
                Reel = started ? Reel : null,
                Timing = Timing,
                Timestamp = timestamp,
                Time = started ? Timing.Seconds(timestamp, Race.StartTimestamp.Value) : double.NaN,
                FromImage = fromImage,
                DeltaInfo = Settings.ShowSplitBoard,
                SpeedTrace = Settings.ShowSpeedChart,
                DeltaChart = Settings.ShowDeltaChart,
                Final = Settings.ShowFinalsBoard && PickTarget == SwimPickTarget.None ? FinalForRace() : null,
                BoardName = BoardName(Race.Athlete),
                Summary = Settings.ReelSummary,
                VideoEnd = VideoEndSeconds(),
                Technical = TechnicalView || Settings.ReelOverlay,
                SwimmerLabel = Settings.ReelOverlay,
                Sync = SyncState,
                PdfReady = !string.IsNullOrEmpty(Race.LastPdfUrl),
                Calibrated = VideoFile != null && VideoFile.Calibrations.Count > 0
            };

            // The frame of the video, for the top bar.
            if (frameServer != null && frameServer.VideoReader != null)
            {
                VideoInfo info = frameServer.VideoReader.Info;
                double frames = info.AverageTimeStampsPerFrame > 0 ? Math.Round((timestamp - info.FirstTimeStamp) / info.AverageTimeStampsPerFrame) : 0;
                double hsf = Timing.HighSpeedFactor <= 0 ? 1.0 : Timing.HighSpeedFactor;
                scene.FrameNumber = (long)Math.Max(0, frames);
                scene.VideoSeconds = info.FramesPerSeconds > 0 ? Math.Max(0, frames) / info.FramesPerSeconds : 0;
                scene.Fps = info.FramesPerSeconds * hsf;
                scene.VideoSize = info.ReferenceSize;
            }

            // The values of the start or the turn while the panel shows the technical analysis, in the place of the map.
            if (TechnicalView)
            {
                SwimTechPhase phase;
                double clock;
                List<KeyValuePair<string, string>> items = TechValuesAt(timestamp, out phase, out clock);
                if (phase != null && items != null && items.Count > 0)
                {
                    scene.TechTitle = SwimLang.Label(phase.Title);
                    scene.TechClock = (clock < 0 ? "−" : "") + Math.Abs(clock).ToString("0.00", CultureInfo.InvariantCulture) + " s";
                    scene.TechItems = items;
                }
            }

            SwimHudPainter.Paint(g, size, scene);
        }

        /// <summary>The measures of the phase shown last (kept while the analysis does not change).</summary>
        private SwimTechRun techRun;
        private string techRunKey;

        /// <summary>
        /// (2.9.9) The technical HUD of a training video at this frame: the HUD chosen, the image large enough, and a start or a turn with
        /// something measured (strokes, or the underwater phase).
        /// </summary>
        private bool TechHudReady(Size size, long timestamp)
        {
            return HudChosen && Race != null && Race.IsTraining && SwimHudPainter.CanPaintTech(size) && TechRunAt(timestamp) != null;
        }

        /// <summary>The measures of the start or the turn of a frame (SwimTechRun), null without one.</summary>
        private SwimTechRun TechRunAt(long timestamp)
        {
            if (Race == null || !Race.IsTraining || TechPhases == null || !Timing.IsValid)
                return null;

            SwimTechPhase phase = SwimTechCalculator.PhaseAt(TechPhases, timestamp);
            if (phase == null)
                return null;

            string key = phase.Id + "|" + DataVersion.ToString(CultureInfo.InvariantCulture);
            if (techRunKey != key)
            {
                SwimSkeleton skeleton = Race.Skeletons.Where(s => s != null && s.Count > 0 && s.Start <= phase.WindowEnd && s.End >= phase.WindowStart)
                    .OrderByDescending(s => s.Count).FirstOrDefault();
                SwimTrack track = Race.Tracks.Where(t => t != null && t.HasPan && t.Start <= phase.WindowEnd && t.End >= phase.WindowStart)
                    .OrderByDescending(t => t.X.Count).FirstOrDefault();
                try
                {
                    techRun = SwimTechRun.Of(Race, Timing, phase, skeleton, track);
                }
                catch (Exception e)
                {
                    log.Error("Could not measure the phase for the technical HUD.", e);
                    techRun = null;
                }

                techRunKey = key;
            }

            SwimTechRun run = techRun;
            return run != null && (run.Cycles.Count > 0 || run.Approach.Count > 0 || run.Phase.Breakout.HasValue) ? run : null;
        }

        /// <summary>The technical HUD (2.9.9) at this frame: the clock and values of the phase, the strokes on the water, the card at the end.</summary>
        private void DrawTechHud(Graphics g, long timestamp, Size size, Func<PointF, PointF> fromImage)
        {
            SwimTechRun run = TechRunAt(timestamp);
            if (run == null)
                return;

            double t = Timing.Seconds(timestamp, run.Phase.Origin);
            PointF? head = null;
            double pan = double.NaN;
            SwimTrack track = Race.TrackAt(timestamp);
            if (track != null && track.HasPan)
            {
                float x, y, top, bottom;
                if (track.TryGet(timestamp, 1, out x, out y, out top, out bottom))
                    head = new PointF(x, y);
                pan = SwimTechRun.TrackPan(track, timestamp);
            }

            // The card after the last stroke measured, until the end of the phase.
            double cardAt = run.Last + 0.8;
            bool card = run.Cycles.Count > 0 && t > cardAt;
            SwimTechHudScene scene = new SwimTechHudScene
            {
                Run = run,
                Time = t,
                Title = run.Phase.Title,
                FromImage = fromImage,
                Head = head,
                Pan = pan,
                VideoSize = frameServer != null && frameServer.VideoReader != null ? frameServer.VideoReader.Info.ReferenceSize : Size.Empty,
                Card = card,
                CardFade = card ? (float)Math.Min(1.0, (t - cardAt) / 0.6) : 0f
            };
            SwimHudPainter.PaintTech(g, size, scene);
        }
        #endregion
    }
}
