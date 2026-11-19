#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.26.1) The digital assistant and the buttons of the bars next to the video that are not in the menu (the automatic readings:
    /// the autopilot, the whole race, the start from the light, the technique autopilot, the skeleton): what the video is, why a button
    /// cannot be shown or pressed now, «Δείξε μου» (the bars shown, the tab of the button, the button lit) and «Ξεκίνα το» (the button
    /// pressed, as a click on it).
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>The video of the analysis for the assistant: "race", "race-tech" (in the technical view), "training", or null.</summary>
        internal string AssistantVideoKind()
        {
            SwimRace race = Race;
            if (race == null || !HasVideo)
                return null;
            if (race.IsTraining)
                return "training";
            return TechnicalView ? "race-tech" : "race";
        }

        /// <summary>
        /// Why the button cannot be pressed now (null: it can): "video" (no video with its analysis), "race" (it is for a race video),
        /// "raceview" (a race in the technical view), "training" (it is for a training video), "tech" (it is in the technical view),
        /// "busy" (a reading is running).
        /// </summary>
        internal string BarButtonNeeds(string id)
        {
            SwimRace race = Race;
            if (race == null || !HasVideo)
                return "video";

            switch (id)
            {
                case "auto-pilot":
                case "auto-race":
                case "start-light":
                    if (race.IsTraining)
                        return "race";
                    if (TechnicalView)
                        return "raceview";
                    break;
                case "tech-pilot":
                    if (!race.IsTraining)
                        return "training";
                    break;
                case "skeleton":
                    if (!race.IsTraining && !TechnicalView)
                        return "tech";
                    break;
                default:
                    return "video";
            }

            if (AutoStrokesRunning || StartLightRunning || TechPilotRunning || SkeletonRunning)
                return "busy";
            return null;
        }

        /// <summary>
        /// The analysis of a video taken up for the assistant, as a key of the analysis takes it up (on a screen of a group of screens:
        /// the analysis of the group): the video of the active screen when the analysis is not on a video yet. «Δείξε μου» and «Ξεκίνα
        /// το» then bring the bars, or the window when the bars do not fit.
        /// </summary>
        internal void TakeUp(PlayerScreenUserInterface player, FrameServerPlayer frameServer)
        {
            long unused = 0;
            ToGroup(ref player, ref frameServer, ref unused);
            Attach(player, frameServer);
        }

        /// <summary>
        /// «Δείξε μου»: the bars on the video (as a key of the analysis brings them), the tab of the button, the button lit (null). When
        /// the bars do not fit next to the video (two or more screens, the assistant open, a narrow window) or the button does not fit
        /// in its bar, the window of the analysis at the page of the button, the button lit there ("panel"). Else why not (as
        /// BarButtonNeeds, and "bars": in full screen or the big screens, where the round button has the buttons, "hidden": the window
        /// does not show the button now).
        /// </summary>
        internal string SpotBarButton(string id)
        {
            string why = BarButtonNeeds(id);
            if (why != null && why != "busy")
                return why;

            if (!BarsVisible && Settings.VideoBars && !DialShown)
                ShowAnalysis(false);
            if (BarsVisible && leftBar.Spot(id))
                return null;
            if (DialShown)
                return "bars";

            if (leftBar != null && !leftBar.IsDisposed)
                leftBar.EndSpot();
            ShowAnalysis(true);
            return form != null && !form.IsDisposed && form.SpotButton(id, SwimVideoBar.SpotSeconds) ? "panel" : "hidden";
        }

        /// <summary>«Ξεκίνα το»: the button of the bars pressed, as a click on it (its reading says what it does and what it needs).</summary>
        internal void RunBarButton(string id)
        {
            if (BarButtonNeeds(id) != null)
                return;

            if (leftBar != null && !leftBar.IsDisposed)
                leftBar.EndSpot();
            ShowAnalysis(false);
            switch (id)
            {
                case "auto-pilot":
                    RunAutoPilot();
                    break;
                case "auto-race":
                    BeginAutoRace();
                    break;
                case "start-light":
                    FindStartLight();
                    break;
                case "tech-pilot":
                    RunTechPilot();
                    break;
                case "skeleton":
                    BeginSkeleton();
                    break;
            }
        }
    }
}
