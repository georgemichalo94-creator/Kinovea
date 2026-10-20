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
        #endregion
    }
}
