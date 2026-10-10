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
using Newtonsoft.Json;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Kinds of events the analyst marks on the video during a swim race analysis.
    /// </summary>
    public enum SwimMarkType
    {
        /// <summary>Head (or hand at the wall) crossing the next checkpoint distance.</summary>
        Split,
        /// <summary>One stroke (hand entry). Free/back: every arm. Fly/breast: every cycle.</summary>
        Stroke,
        /// <summary>One underwater dolphin kick.</summary>
        Kick,
        /// <summary>Head breaks the surface after the start or a turn.</summary>
        Breakout,
        /// <summary>Feet leave the block (start) or the wall (turn).</summary>
        FeetOff,
        /// <summary>Hands enter the water after the start.</summary>
        Entry,
        /// <summary>First movement of the swimmer after the start signal (reaction).</summary>
        Reaction
    }

    /// <summary>
    /// A single event marked on the video.
    /// </summary>
    public class SwimMark
    {
        public string Id = Guid.NewGuid().ToString("N");
        public SwimMarkType Type;

        /// <summary>Absolute video timestamp of the frame where the event was marked.</summary>
        public long Timestamp;

        /// <summary>For splits: checkpoint distance in meters.</summary>
        public int Distance;
    }

    public class SwimTurnManual
    {
        public double? UnderwaterDist;
        public double? TurnTime;
    }

    /// <summary>
    /// One analysed swimmer in one race.
    /// Times are not stored as seconds but as video timestamps, and converted with the video timing
    /// so that a change of the capture framerate (high speed) is honored.
    /// </summary>
    public class SwimRace
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Athlete = "";

        // Athlete from the federation registry (unique_id in the main Google Sheet). Empty for free text names.
        public string AthleteId = "";
        public string AthleteGender = "";
        public string AthleteBirthYear = "";
        public string AthleteCategory = "";
        public string AthleteClub = "";

        public int Lane;
        public string Style = "fs";
        public bool Relay;
        public int Distance = 100;
        public int Pool = 50;
        public string Competition = "";
        public string Location = "";
        public string Round = "";
        public string Date = DateTime.Now.ToString("d.M.yyyy", CultureInfo.InvariantCulture);
        public List<int> Checkpoints = new List<int>();

        /// <summary>Video timestamp of the start signal (race time zero).</summary>
        public long? StartTimestamp;

        public List<SwimMark> Marks = new List<SwimMark>();

        // Manual values (typed by the analyst).
        public double? Reaction;
        public double? EntryDistHands;
        public double? EntryDistHips;
        public double? EntryVelHands;
        public double? EntryVelHips;
        public double? EntryVelFeet;
        public double? UnderwaterDist;
        public double? BreakoutDist;
        public double? FinishLastStrokeDist;
        public Dictionary<int, SwimTurnManual> Turns = new Dictionary<int, SwimTurnManual>();

        /// <summary>Elite reference for the deltas at the checkpoints (a copy, so it works offline).</summary>
        public SwimReference Reference;

        // Last synchronization with the Google Sheet.
        public string SheetName;
        public string SheetUrl;
        public string LastPdfUrl;

        [JsonIgnore]
        public string DistanceLabel
        {
            get { return Relay ? "4x" + (Distance / 4).ToString(CultureInfo.InvariantCulture) : Distance.ToString(CultureInfo.InvariantCulture); }
        }

        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                string name = string.IsNullOrWhiteSpace(Athlete) ? "(χωρίς όνομα)" : Athlete.Trim();
                string lane = Lane > 0 ? " · Δ" + Lane.ToString(CultureInfo.InvariantCulture) : "";
                return string.Format("{0} – {1} {2}{3}", name, DistanceLabel, SwimPresets.StyleShort(Style), lane);
            }
        }

        public SwimMark GetSplit(int distance)
        {
            return Marks.FirstOrDefault(m => m.Type == SwimMarkType.Split && m.Distance == distance);
        }

        /// <summary>
        /// Distance of the first checkpoint without a split time, or -1 if all are filled.
        /// </summary>
        public int NextCheckpoint()
        {
            foreach (int d in Checkpoints)
            {
                if (GetSplit(d) == null)
                    return d;
            }

            return -1;
        }

        /// <summary>
        /// Remove split marks whose distance is no longer a checkpoint.
        /// </summary>
        public void RemoveOrphanSplits()
        {
            Marks.RemoveAll(m => m.Type == SwimMarkType.Split && !Checkpoints.Contains(m.Distance));
        }

        public SwimTurnManual GetTurn(int wall, bool create)
        {
            SwimTurnManual turn;
            if (Turns.TryGetValue(wall, out turn))
                return turn;

            if (!create)
                return null;

            turn = new SwimTurnManual();
            Turns[wall] = turn;
            return turn;
        }

        /// <summary>
        /// Copy of the race information for another swimmer in the same video (same start signal).
        /// </summary>
        public SwimRace CloneForNewSwimmer()
        {
            SwimRace r = new SwimRace();
            r.Style = Style;
            r.Relay = Relay;
            r.Distance = Distance;
            r.Pool = Pool;
            r.Competition = Competition;
            r.Location = Location;
            r.Round = Round;
            r.Date = Date;
            r.Checkpoints = new List<int>(Checkpoints);
            r.StartTimestamp = StartTimestamp;
            r.Reference = Reference != null ? Reference.Copy() : null;
            return r;
        }
    }

    /// <summary>
    /// All the races analysed on one video. Saved next to the video as "video.ext.swim.json".
    /// </summary>
    public class SwimVideoFile
    {
        public int Version = 1;
        public string VideoPath;
        public string ActiveRaceId;
        public List<SwimRace> Races = new List<SwimRace>();

        /// <summary>Perspective calibrations of the water surface (shared by all the swimmers of the video).</summary>
        public List<SwimCalibration> Calibrations = new List<SwimCalibration>();
    }
}
