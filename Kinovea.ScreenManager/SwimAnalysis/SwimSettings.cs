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
        /// (2.9.7) The learning of the proposals shared through the Google Sheet with every Kinovea connected to it (SwimLearningSync):
        /// the answers of all their analysts teach all of them.
        /// </summary>
        public bool SharedLearning = true;

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

        /// <summary>
        /// (2.9.8) The set of the finals the races are compared with (its short name, e.g. "Ευρωπαϊκό 2026"); empty: automatic (the set of
        /// the competition of the race, else the first one).
        /// </summary>
        public string FinalsSet = "";

        /// <summary>Since 2.8.0: the Δ with the reference at the last checkpoint passed on the race board of the video.</summary>
        public bool CompareDelta = true;

        /// <summary>Since 2.8.0: the ghost of the reference on the video, a translucent copy of the swimmer where the reference is.</summary>
        public bool CompareGhost = false;

        /// <summary>Since 2.8.0: the line of the reference across the lane on the video, as the world record line of the television.</summary>
        public bool CompareLine = false;

        /// <summary>Corner of the video for the chart: TopRight, TopLeft, BottomRight, BottomLeft.</summary>
        public string OverlayCorner = "TopRight";

        /// <summary>
        /// Since 2.9.0: the graphics of the analysis on the video (race board, charts, the board of the final, the comparison, the
        /// technical view). The key V and the button «Γραφικά» of the bar put them on and off; off, also off in the exports of the
        /// video (not in the images of the report).
        /// </summary>
        public bool VideoGraphics = true;

        /// <summary>
        /// Since 2.9.1: the graphics of a race as the HUD of the demo (the panel of the analysis on the left, the checkpoints and the
        /// log on the right, the map of the pool below, the call-outs on the video and «Η ανάλυση με μια ματιά» after the finish); the
        /// boards of before are added to it with «Στο βίντεο» (Δ and ΔΑΚ, the Δ chart, the board of the final, the speed). Off: the
        /// boards as before («Πινακάκια»).
        /// </summary>
        public bool HudLayout = true;

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
        /// Since 2.9.5: the place of the round button of the analysis in full screen (SwimHudDial), its centre as a fraction of the video
        /// area ("x,y"), empty for the default place (bottom right).
        /// </summary>
        public string DialPlace = "";

        /// <summary>(2.14.0) The place of the round button of a group of screens, a fraction of the area of the group ("" = its middle).</summary>
        public string GroupDialPlace = "";

        /// <summary>Since 2.9.5: the round button of the full screen is hidden (a small dot in the corner brings it back, or the key D).</summary>
        public bool DialHidden = false;

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

        /// <summary>
        /// Technical analysis: the frame of each event marked (signal, hands, release, entry, first kick, breakout, last stroke,
        /// rotation, contact with the wall, push-off…) becomes a snapshot of its phase.
        /// </summary>
        public bool AutoTechSnapshots = true;

        /// <summary>
        /// Manual analysis: each mark teaches the learning as the answer to a proposal would; after one click on the swimmer the race
        /// is read in the background for the checkpoints, the walls and the breakouts.
        /// </summary>
        public bool LearnFromMarks = true;

        /// <summary>After following the swimmer for a length: the checkpoints of the length from the marks of the ropes, for the check.</summary>
        public bool ProposeSplitsAfterTrack = true;

        /// <summary>Last reference chosen for each event (style|distance|pool|gender → reference id).</summary>
        public Dictionary<string, string> ReferenceByEvent = new Dictionary<string, string>();

        /// <summary>
        /// The numbering of the lanes of the views of the whole pool (phones), per folder of videos: the lanes the analyst confirmed,
        /// "number:place:lanes;..." (the place counted from the near wall, 0 at the near wall).
        /// </summary>
        public Dictionary<string, string> LaneNumbering = new Dictionary<string, string>();

        /// <summary>
        /// The link of the official results last used for each competition (its name as typed, normalized; since 2.7.6): the next race
        /// of the competition finds its results without the link pasted again.
        /// </summary>
        public Dictionary<string, string> ResultsLinks = new Dictionary<string, string>();

        /// <summary>
        /// The language of the analysis (since 2.7.7): "el" or "en", for the panel, the graphics of the video and the PDF (the button
        /// ΕΛ / EN of the panel).
        /// </summary>
        public string Language = "el";

        /// <summary>
        /// (2.10.0) The folder of the packages for the coaches (empty: «Αποστολές προπονητών» on the desktop) and the signature at the end of
        /// their message.
        /// </summary>
        public string PackageFolder = "";
        public string PackageSignature = "";

        /// <summary>
        /// (2.11.0) The parts of the PDF of the report, as chosen at the PDF button: the short analysis (three pages for the coach) and the
        /// full one (the report with all its tables, comments at its end); both: one PDF, the short first.
        /// </summary>
        public bool PdfShort = true;
        public bool PdfFull = true;

        /// <summary>(2.12.0) Both parts in two separate files (Apps Script 2.12.0): the short analysis and the full one with its comments.</summary>
        public bool PdfSeparate;

        /// <summary>(2.13.0) The sheet of criteria last chosen for each kind of phase and style ("start|fs", "turn|br").</summary>
        public Dictionary<string, string> TechSheetChoice = new Dictionary<string, string>();

        /// <summary>(2.13.0) The program fills the criteria it measures (marked 🤖 until the coach confirms them).</summary>
        public bool TechEvalAuto = true;

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
                    {
                        if (settings.TechSheetChoice == null)
                            settings.TechSheetChoice = new Dictionary<string, string>();
                        return settings;
                    }
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
