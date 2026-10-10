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
    /// 15/25/45/50 for 400m and 4x100m, 15/45/50 for 800m and longer.
    /// </summary>
    public static class SwimPresets
    {
        public static readonly string[] Styles = new string[] { "fs", "bk", "br", "fly", "im" };
        public static readonly string[] StyleNames = new string[] { "Ελεύθερο (fs)", "Ύπτιο (bk)", "Πρόσθιο (br)", "Πεταλούδα (fly)", "Μικτή (medley)" };

        /// <summary>Distance choices shown in the UI. "4x100" means a 400m relay.</summary>
        public static readonly string[] DistanceChoices = new string[] { "50", "100", "200", "400", "800", "1500", "4x50", "4x100", "4x200" };

        private static readonly string[] imOrder = new string[] { "fly", "bk", "br", "fs" };
        private static readonly string[] medleyRelayOrder = new string[] { "bk", "br", "fly", "fs" };

        public static string StyleShort(string style)
        {
            return style == "im" ? "medley" : style;
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
                pattern = leg <= 200 ? new int[] { 15, 20, 25 } : new int[] { 15, 25 };
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
    }
}
