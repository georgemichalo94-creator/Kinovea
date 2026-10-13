#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Kinovea.Services;
using Newtonsoft.Json;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Settings of the swim analysis panel, stored in the Kinovea settings folder (SwimAnalysis.json).
    /// </summary>
    public class SwimSettings
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Google Apps Script web app URL (ends with /exec).</summary>
        public string WebAppUrl = "";

        /// <summary>Shared secret shown in the Google Sheet menu "Swim Analysis → Στοιχεία σύνδεσης".</summary>
        public string Token = "";

        /// <summary>Stroke count rounding at checkpoints: 0.5 (half strokes), 1 (whole) or 0 (exact).</summary>
        public double StrokeRounding = 0.5;

        /// <summary>Send every change to the sheet as it happens.</summary>
        public bool AutoSend = true;

        /// <summary>
        /// The camera does not move: one calibration of the water surface is valid for the whole video.
        /// Otherwise a distance can only be measured on the frame of a calibration.
        /// </summary>
        public bool FixedCamera = false;

        /// <summary>Distance between the lane ropes (m) used for new calibrations.</summary>
        public double LaneWidth = 2.5;

        /// <summary>Velocity chart drawn on the video.</summary>
        public bool ShowSpeedChart = true;

        /// <summary>Race time, last split and delta drawn on the video.</summary>
        public bool ShowSplitBoard = true;

        /// <summary>The Δ with the reference at each checkpoint drawn on the video (under the velocity chart).</summary>
        public bool ShowDeltaChart = true;

        /// <summary>The board of the swimmers of the final at each checkpoint drawn on the video.</summary>
        public bool ShowFinalsBoard = true;

        /// <summary>Corner of the video for the chart: TopRight, TopLeft, BottomRight, BottomLeft.</summary>
        public string OverlayCorner = "TopRight";

        /// <summary>
        /// The graphics of the video (race board, velocity chart, values of the start or turn) also in the Kinovea exports
        /// (video, image, clipboard) and in the snapshots of the report, with the proportions they have on the screen.
        /// </summary>
        public bool OverlayInExports = true;

        /// <summary>
        /// Strokes at each checkpoint: after the breakout the analyst clicks the head and the feet of the swimmer, at each
        /// checkpoint the strokes of the segment are counted from the video and shown to confirm or correct.
        /// </summary>
        public bool AutoStrokes = false;

        /// <summary>
        /// Buttons of the analysis, race time and checkpoints in two bars on the left and right of the image (the black
        /// area of the video): the window of the analysis is opened only when needed.
        /// </summary>
        public bool VideoBars = true;

        /// <summary>Place of the window of the analysis when it was last closed ("x,y,width,height"), empty for the default place.</summary>
        public string PanelBounds = "";

        /// <summary>
        /// Technical view of the race on the video (as the swimming reels): athlete label following the swimmer, race time with the
        /// strokes, tempo, distance per cycle and speed, phase, badges at the start, the breakouts and the checkpoints, per cycle chart.
        /// It replaces the race board and the velocity chart.
        /// </summary>
        public bool ReelOverlay = false;

        /// <summary>Technical view: summary card of the race after the finish.</summary>
        public bool ReelSummary = true;

        /// <summary>Technical analysis: the skeleton of the swimmer on the video (where it was found).</summary>
        public bool SkeletonOverlay = true;

        /// <summary>Technical analysis: the angles of the knee, the hip and the elbow on the skeleton.</summary>
        public bool SkeletonAngles = true;

        /// <summary>
        /// Technical analysis: right after a skeleton is read, the marks of the start, the dolphin kicks, the breakout and the
        /// turn are proposed and checked one by one on the video (Enter right, arrows and Enter corrected, Delete wrong).
        /// </summary>
        public bool ProposeAfterSkeleton = true;

        /// <summary>After following the swimmer for a length: the checkpoints of the length from the marks of the ropes, for the check.</summary>
        public bool ProposeSplitsAfterTrack = true;

        /// <summary>Last reference chosen for each event (style|distance|pool|gender → reference id).</summary>
        public Dictionary<string, string> ReferenceByEvent = new Dictionary<string, string>();

        [JsonIgnore]
        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(WebAppUrl) && !string.IsNullOrWhiteSpace(Token); }
        }

        private static string FilePath
        {
            get { return Path.Combine(Software.SettingsDirectory, "SwimAnalysis.json"); }
        }

        public static SwimSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    SwimSettings settings = JsonConvert.DeserializeObject<SwimSettings>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (settings != null)
                        return settings;
                }
            }
            catch (Exception e)
            {
                log.Error("Could not read swim analysis settings.", e);
            }

            return new SwimSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception e)
            {
                log.Error("Could not save swim analysis settings.", e);
            }
        }
    }
}
