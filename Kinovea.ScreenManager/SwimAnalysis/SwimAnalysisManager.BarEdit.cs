#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.37.0) What the right bar beside the video changes without the window (SwimVideoBar.List.cs): the distances of the events typed
    /// or turned with the wheel, and the details of the race filled in at the beginning. Each change goes in as the same field of the
    /// window would put it (the same checks and the same consequences: the default checkpoints of a new distance, the previous races of a
    /// new event), and the window, when it is open, shows it.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>The video at a race time (s from the start signal), on the frame nearest to it.</summary>
        internal void SeekRaceTime(double seconds)
        {
            if (Race == null || !Race.StartTimestamp.HasValue || !HasVideo || !Timing.IsValid)
                return;

            SeekNearest(Timing.Timestamp(seconds, Race.StartTimestamp.Value));
        }

        /// <summary>The window of the analysis at one of its pages ("race": the details; athlete: the search of the athlete).</summary>
        internal void ShowPanelPage(string page, bool athlete)
        {
            if (Race == null)
                return;

            ShowAnalysis(true);
            if (form == null || form.IsDisposed)
                return;

            form.ShowView(null, page);
            if (athlete)
                form.FocusAthleteSearch();
        }

        /// <summary>A distance of an event changed in the bar (null: none). As typed in the window: no longer the estimate from the times.</summary>
        internal void SetBarDistance(SwimBarDistance which, int wall, double? value)
        {
            if (Race == null || Race.IsTraining)
                return;

            if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value < 0))
                return;
            if (value.HasValue)
                value = Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);

            switch (which)
            {
                case SwimBarDistance.EntryHands:
                    if (Same(Race.EntryDistHands, value))
                        return;
                    Race.EntryDistHands = value;
                    break;
                case SwimBarDistance.StartBreakout:
                    if (Same(Race.BreakoutDist, value))
                        return;
                    Race.BreakoutDist = value;
                    break;
                case SwimBarDistance.TurnBreakout:
                {
                    if (wall <= 0)
                        return;
                    SwimTurnManual turn = Race.GetTurn(wall, value.HasValue);
                    if (turn == null || (Same(turn.UnderwaterDist, value) && !turn.UnderwaterEstimated))
                        return;
                    turn.UnderwaterDist = value;
                    turn.UnderwaterEstimated = false;
                    break;
                }
                case SwimBarDistance.FinishLastStroke:
                    if (Same(Race.FinishLastStrokeDist, value))
                        return;
                    Race.FinishLastStrokeDist = value;
                    break;
                default:
                    return;
            }

            if (form != null && !form.IsDisposed)
                form.RefreshManualFields();
            ManualValuesChanged();
        }

        private static bool Same(double? a, double? b)
        {
            if (!a.HasValue || !b.HasValue)
                return a.HasValue == b.HasValue;
            return Math.Abs(a.Value - b.Value) < 0.005;
        }

        /// <summary>The lane (0: none).</summary>
        internal void SetBarLane(int lane)
        {
            if (Race == null)
                return;

            lane = Math.Max(0, Math.Min(10, lane));
            if (Race.Lane == lane)
                return;

            Race.Lane = lane;
            BarInfoChanged(false);
        }

        /// <summary>The style: the previous races of the PB are those of the new event.</summary>
        internal void SetBarStyle(string style)
        {
            if (Race == null || string.IsNullOrEmpty(style) || Array.IndexOf(SwimPresets.Styles, style) < 0 || Race.Style == style)
                return;

            Race.Style = style;
            BarInfoChanged(false);
            RefreshPbCandidates();
        }

        /// <summary>The distance of the race ("100", "4x100"): the checkpoints become the default ones of the new distance, as in the window.</summary>
        internal void SetBarEvent(string choice)
        {
            int distance;
            bool relay;
            if (Race == null || !SwimPresets.TryParseDistance(choice, out distance, out relay))
                return;
            if (Race.Distance == distance && Race.Relay == relay)
                return;

            Race.Distance = distance;
            Race.Relay = relay;
            Race.Checkpoints = SwimPresets.DefaultCheckpoints(distance, Race.Pool, relay);
            BarInfoChanged(true);
        }

        /// <summary>The pool (the order of SwimPresets.PoolNames): the default checkpoints of the new pool, as in the window.</summary>
        internal void SetBarPool(int index)
        {
            if (Race == null)
                return;

            int oldPool = Race.Pool;
            string oldUnit = Race.PoolUnit;
            SwimPresets.ApplyPool(Race, index);
            if (Race.Pool == oldPool && Race.PoolUnit == oldUnit)
                return;

            Race.Checkpoints = SwimPresets.DefaultCheckpoints(Race.Distance, Race.Pool, Race.Relay);
            BarInfoChanged(true);
        }

        /// <summary>
        /// A text of the race: "competition", "location", "round", "date", "results" (the link: searched at once, as with «🔗 Επίσημα
        /// αποτελέσματα»), "checkpoints" (the distances in their order, as in the window: the last one should be the distance of the race).
        /// </summary>
        internal void SetBarText(string field, string text)
        {
            if (Race == null)
                return;

            string value = (text ?? "").Trim();
            switch (field)
            {
                case "competition":
                    if (Race.Competition == value)
                        return;
                    Race.Competition = value;
                    break;
                case "location":
                    if (Race.Location == value)
                        return;
                    Race.Location = value;
                    break;
                case "round":
                    if (Race.Round == value)
                        return;
                    Race.Round = value;
                    break;
                case "date":
                    if (Race.Date == value)
                        return;
                    Race.Date = value;
                    break;
                case "results":
                {
                    if ((Race.ResultsLink ?? "") == value)
                        return;

                    Race.ResultsLink = value;
                    BarInfoChanged(false);

                    // A link of results: searched at once, as with the button of the window (the race found in the results, then, when
                    // the analyst agrees, its official times).
                    if (IsResultsLink(value))
                        LinkOfficialResults(value);
                    return;
                }
                case "checkpoints":
                {
                    List<int> parsed = SwimPresets.ParseCheckpoints(value);
                    if (parsed.Count == 0 || parsed.SequenceEqual(Race.Checkpoints))
                        return;

                    Race.Checkpoints = parsed;
                    BarInfoChanged(true);
                    if (parsed[parsed.Count - 1] != Race.Distance)
                        Status(SwimLang.T("Προσοχή: το τελευταίο σημείο ελέγχου δεν είναι ίσο με την απόσταση του αγώνα (") + Race.Distance + Race.Unit + ").", true);
                    return;
                }
                default:
                    return;
            }

            BarInfoChanged(false);
        }

        /// <summary>A detail of the race changed in the bar: as when the window changes it, and the window shows it.</summary>
        private void BarInfoChanged(bool structureChanged)
        {
            RaceInfoChanged(structureChanged);
            if (form != null && !form.IsDisposed)
                form.RefreshRaceFields();
        }
    }
}
