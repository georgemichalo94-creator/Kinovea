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
    /// Checkpoint presets and swimming style helpers.
    /// Presets follow the analysis templates: 15/25/35/45/50 per length up to 200m,
    /// 15/25/45/50 for 400m and 4x100m, 15/45/50 for 800m and longer, 15/20/25 per length in 25 m and 25 yd pools.
    /// </summary>
    public static class SwimPresets
    {
        public static readonly string[] Styles = new string[] { "fs", "bk", "br", "fly", "im" };

        /// <summary>The names of the styles in the language of the analysis (the order of Styles).</summary>
        public static string[] StyleNames
        {
            get { return new string[] { SwimLang.T("Ελεύθερο (fs)"), SwimLang.T("Ύπτιο (bk)"), SwimLang.T("Πρόσθιο (br)"), SwimLang.T("Πεταλούδα (fly)"), SwimLang.T("Μικτή (medley)") }; }
        }

        /// <summary>Distance choices shown in the UI. "4x100" means a 400m relay.</summary>
        public static readonly string[] DistanceChoices = new string[] { "50", "100", "200", "400", "800", "1500", "4x50", "4x100", "4x200" };

        /// <summary>Events of the 25 yard pools (NCAA / US short course).</summary>
        public static readonly string[] YardDistanceChoices = new string[] { "50", "100", "200", "400", "500", "1000", "1650", "4x50", "4x100", "4x200" };

        /// <summary>Pools: length and unit, in the order of the UI, in the language of the analysis.</summary>
        public static string[] PoolNames
        {
            get { return new string[] { SwimLang.T("50μ. (Long Course)"), SwimLang.T("25μ. (Short Course)"), "25yd (Short Course Yards)" }; }
        }

        private static readonly int[] poolLengths = new int[] { 50, 25, 25 };
        private static readonly string[] poolUnits = new string[] { SwimRace.UnitMeters, SwimRace.UnitMeters, SwimRace.UnitYards };

        private static readonly string[] imOrder = new string[] { "fly", "bk", "br", "fs" };
        private static readonly string[] medleyRelayOrder = new string[] { "bk", "br", "fly", "fs" };

        public static string StyleShort(string style)
        {
            return style == "im" ? "medley" : style;
        }

        public static int PoolIndex(SwimRace race)
        {
            if (race == null)
                return 0;
            if (race.IsYards)
                return 2;
            return race.Pool == 25 ? 1 : 0;
        }

        public static void ApplyPool(SwimRace race, int index)
        {
            int i = Math.Max(0, Math.Min(poolLengths.Length - 1, index));
            race.Pool = poolLengths[i];
            race.PoolUnit = poolUnits[i];
        }

        public static string[] DistancesFor(SwimRace race)
        {
            return race != null && race.IsYards ? YardDistanceChoices : DistanceChoices;
        }

        /// <summary>
        /// Parse a distance choice like "100" or "4x100" into total distance and relay flag.
        /// </summary>
        public static bool TryParseDistance(string text, out int distance, out bool relay)
        {
            distance = 0;
            relay = false;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string t = text.Trim().ToLowerInvariant().Replace("χ", "x").Replace("×", "x");
            int leg;
            if (t.StartsWith("4x") && int.TryParse(t.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out leg) && leg > 0)
            {
                distance = leg * 4;
                relay = true;
                return true;
            }

            return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out distance) && distance > 0;
        }

        public static List<int> DefaultCheckpoints(int distance, int pool, bool relay)
        {
            int[] pattern;
            int leg = relay ? distance / 4 : distance;

            if (pool == 25)
            {
                // 15 and 20: the only clean swimming segment of a short course length (+15 to −5), for stroke rate and length.
                pattern = new int[] { 15, 20, 25 };
            }
            else
            {
                if (leg <= 50 && relay)
                    pattern = new int[] { 15, 25, 35, 45, 50 };
                else if (relay && leg <= 100)
                    pattern = new int[] { 15, 25, 45, 50 };
                else if (relay)
                    pattern = new int[] { 15, 45, 50 };
                else if (distance <= 200)
                    pattern = new int[] { 15, 25, 35, 45, 50 };
                else if (distance <= 400)
                    pattern = new int[] { 15, 25, 45, 50 };
                else
                    pattern = new int[] { 15, 45, 50 };
            }

            List<int> result = new List<int>();
            int length = pool == 25 ? 25 : 50;
            for (int wall = 0; wall < distance; wall += length)
            {
                foreach (int p in pattern)
                {
                    int d = wall + p;
                    if (p < length && d > wall && d <= distance && !result.Contains(d))
                        result.Add(d);
                    else if (p == length && d <= distance && !result.Contains(d))
                        result.Add(d);
                }
            }

            result.Sort();
            return result;
        }

        public static string FormatCheckpoints(List<int> checkpoints)
        {
            return string.Join(", ", checkpoints.Select(c => c.ToString(CultureInfo.InvariantCulture)).ToArray());
        }

        public static List<int> ParseCheckpoints(string text)
        {
            List<int> result = new List<int>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            string[] tokens = text.Split(new char[] { ',', ';', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                int d;
                if (int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out d) && d > 0 && !result.Contains(d))
                    result.Add(d);
            }

            result.Sort();
            return result;
        }

        /// <summary>
        /// Style swum at the end distance of a segment (medley order for IM and medley relays).
        /// </summary>
        public static string StyleAt(SwimRace race, double dEnd)
        {
            if (race.Style != "im")
                return race.Style;

            string[] order = race.Relay ? medleyRelayOrder : imOrder;
            double leg = race.Distance / 4.0;
            if (leg <= 0)
                return "fs";

            int idx = (int)Math.Floor((dEnd - 1e-6) / leg);
            idx = Math.Max(0, Math.Min(3, idx));
            return order[idx];
        }

        /// <summary>
        /// Number of key presses per stroke cycle: free and back count every arm (2), fly and breast once per cycle.
        /// </summary>
        public static int PressesPerCycle(string style)
        {
            return (style == "fs" || style == "bk") ? 2 : 1;
        }

        /// <summary>
        /// Style name without the code: "Ελεύθερο".
        /// </summary>
        public static string StyleName(string style)
        {
            int i = Array.IndexOf(Styles, style);
            if (i < 0 || i >= StyleNames.Length)
                return style;

            string name = StyleNames[i];
            int p = name.IndexOf(" (", StringComparison.Ordinal);
            return p > 0 ? name.Substring(0, p) : name;
        }
    }
}
