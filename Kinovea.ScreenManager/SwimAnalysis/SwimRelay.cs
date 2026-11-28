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
    /// (2.42.0) An exchange of a relay: the touch of the swimmer who arrives (the end of his leg: the official time where given) and the
    /// moment the feet of the next one leave the block (F), the time between them, the official one of the results, and the two
    /// snapshots of it.
    /// </summary>
    public sealed class SwimExchange
    {
        /// <summary>1–3: the exchange from the swimmer of leg Number to the one of leg Number + 1.</summary>
        public int Number;

        /// <summary>Its wall (pool units from the start).</summary>
        public int Wall;

        /// <summary>Race time of the touch (s): the time of the checkpoint at the wall; null when it is not marked.</summary>
        public double? Touch;

        /// <summary>Race time the feet of the next swimmer leave the block; null when F is not marked.</summary>
        public double? FeetOff;

        /// <summary>FeetOff − Touch (s; negative: he left before the touch). Null when one of them is missing.</summary>
        public double? Time;

        /// <summary>The official time of the exchange (the results), null when not given.</summary>
        public double? Official;

        /// <summary>The family names of the two swimmers ("" when not known).</summary>
        public string From = "";
        public string To = "";

        /// <summary>The ids of its snapshots (the touch, the feet off the block), null when not taken.</summary>
        public string TouchSnapshot;
        public string OffSnapshot;

        public int Snapshots
        {
            get { return (TouchSnapshot != null ? 1 : 0) + (OffSnapshot != null ? 1 : 0); }
        }
    }

    /// <summary>
    /// (2.42.0) The exchanges of a relay as the analysis measures them (the touch: S at the wall of the end of a leg; the feet off the
    /// block: F), their zones and what is written about them on the video, the bar and the report. Nothing of the screen: also in the CI.
    /// </summary>
    public static class SwimRelay
    {
        /// <summary>
        /// The zones of the time of an exchange (s): before −0.03 the exchange is not valid (the touch pad and the block allow −0.03),
        /// from −0.03 to 0.10 a risk, from 0.10 to 0.20 the target, up to 0.35 slow, after it very slow.
        /// </summary>
        public const double Invalid = -0.03;
        public const double TargetFrom = 0.10;
        public const double TargetTo = 0.20;
        public const double Slow = 0.35;

        /// <summary>The ends of the scale of the graphic (s).</summary>
        public const double ScaleFrom = -0.08;
        public const double ScaleTo = 0.50;

        /// <summary>
        /// While F is not marked, the time runs from the touch for this long on the video (a longer exchange is not one: the analyst
        /// looks elsewhere); the result stays this long after the feet leave the block.
        /// </summary>
        public const double RunningSeconds = 1.5;
        public const double HoldSeconds = 2.0;

        public const string TouchEvent = "Επαφή · αλλαγή {0} → {1}";
        public const string OffEvent = "Αποκόλληση βατήρα · αλλαγή {0} → {1}";

        /// <summary>The name of the snapshot of the touch (from leg n to leg n + 1), in Greek as the events of the snapshots.</summary>
        public static string TouchLabel(int n)
        {
            return string.Format(CultureInfo.InvariantCulture, TouchEvent, n, n + 1);
        }

        public static string OffLabel(int n)
        {
            return string.Format(CultureInfo.InvariantCulture, OffEvent, n, n + 1);
        }

        /// <summary>A snapshot of an exchange (its touch or its feet off the block): kept for the PDF only, not uploaded.</summary>
        public static bool IsExchangeSnapshot(SwimSnapshot s)
        {
            if (s == null || string.IsNullOrEmpty(s.Event))
                return false;
            return s.Event.StartsWith(TouchEvent.Substring(0, TouchEvent.IndexOf('{')), StringComparison.Ordinal) ||
                s.Event.StartsWith(OffEvent.Substring(0, OffEvent.IndexOf('{')), StringComparison.Ordinal);
        }

        /// <summary>The exchange of a wall (1–3), 0 when the wall is not the end of a leg of a relay of four.</summary>
        public static int ExchangeAt(SwimRace race, int wall)
        {
            int leg = race != null ? race.LegDistance : 0;
            if (leg <= 0 || wall <= 0 || wall % leg != 0)
                return 0;
            int n = wall / leg;
            return n >= 1 && n <= 3 ? n : 0;
        }

        /// <summary>The three exchanges of a relay of four (none for another race), with what is measured of each.</summary>
        public static List<SwimExchange> Exchanges(SwimRace race, SwimComputed c)
        {
            List<SwimExchange> list = new List<SwimExchange>();
            if (race == null || !race.IsRelayOfFour)
                return list;

            int leg = race.LegDistance;
            for (int n = 1; n <= 3; n++)
            {
                SwimExchange x = new SwimExchange { Number = n, Wall = n * leg };
                SwimRow row = c != null && c.Rows != null ? c.Rows.FirstOrDefault(r => r.D == x.Wall) : null;
                if (row != null && row.T.HasValue && (row.Marked || row.Official))
                    x.Touch = row.T;

                SwimTurnComputed turn = c != null && c.Turns != null ? c.Turns.FirstOrDefault(t => t.Wall == x.Wall && t.Exchange) : null;
                if (turn != null && x.Touch.HasValue && turn.Pushoff.HasValue)
                {
                    x.FeetOff = turn.Pushoff;
                    x.Time = turn.TurnTime ?? turn.Pushoff.Value - x.Touch.Value;
                }

                SwimRelayLeg from = race.RelayLeg(n), to = race.RelayLeg(n + 1);
                x.From = from != null ? from.FamilyName : "";
                x.To = to != null ? to.FamilyName : "";
                x.Official = to != null ? to.OfficialExchange : null;

                if (race.Snapshots != null)
                {
                    SwimSnapshot touch = race.Snapshots.FirstOrDefault(s => s.Event == TouchLabel(n));
                    SwimSnapshot off = race.Snapshots.FirstOrDefault(s => s.Event == OffLabel(n));
                    x.TouchSnapshot = touch != null ? touch.Id : null;
                    x.OffSnapshot = off != null ? off.Id : null;
                }

                list.Add(x);
            }

            return list;
        }

        /// <summary>The zone of a time: 0 not valid, 1 risk, 2 the target, 3 slow, 4 very slow.</summary>
        public static int Zone(double t)
        {
            if (t < Invalid - 1e-9)
                return 0;
            if (t < TargetFrom - 1e-9)
                return 1;
            if (t <= TargetTo + 1e-9)
                return 2;
            return t <= Slow + 1e-9 ? 3 : 4;
        }

        /// <summary>
        /// What the time says, as written under the graphic: "στη ζώνη 0.10–0.20", "+0.02 πάνω από τη ζώνη 0.10–0.20", "−0.06 κάτω από
        /// τη ζώνη 0.10–0.20 · ρίσκο", "άκυρη: πριν από το −0.03".
        /// </summary>
        public static string Verdict(double t)
        {
            string zone = string.Format(CultureInfo.InvariantCulture, "{0:0.00}–{1:0.00}", TargetFrom, TargetTo);
            switch (Zone(t))
            {
                case 0:
                    return SwimLang.T("άκυρη: πριν από το −0.03");
                case 1:
                    return Signed(t - TargetFrom) + SwimLang.T(" κάτω από τη ζώνη ") + zone + SwimLang.T(" · ρίσκο");
                case 2:
                    return SwimLang.T("στη ζώνη ") + zone;
                case 3:
                    return Signed(t - TargetTo) + SwimLang.T(" πάνω από τη ζώνη ") + zone;
                default:
                    return Signed(t - TargetTo) + SwimLang.T(" πάνω από τη ζώνη ") + zone + SwimLang.T(" · αργή");
            }
        }

        /// <summary>A time of an exchange with its sign: "+0.02", "−0.06" (the minus sign, as the graphics write it), "0.00".</summary>
        public static string Signed(double t)
        {
            double r = Math.Round(t, 2);
            if (Math.Abs(r) < 0.005)
                return "0.00";
            return (r < 0 ? "−" : "+") + Math.Abs(r).ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>A time of an exchange as the big figures write it: "0.22", "−0.02".</summary>
        public static string Figure(double t)
        {
            double r = Math.Round(t, 2);
            return (r < -0.004 ? "−" : "") + Math.Abs(r).ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The exchange shown at a race time and the time it shows: from the touch, the time running frame by frame until the feet leave
        /// the block (F; without F for RunningSeconds), then its time for HoldSeconds. Null: none at this time. 'running': still counting.
        /// </summary>
        public static SwimExchange ShownAt(List<SwimExchange> exchanges, double now, out double shown, out bool running)
        {
            shown = 0;
            running = false;
            if (exchanges == null || double.IsNaN(now))
                return null;

            foreach (SwimExchange x in exchanges)
            {
                if (!x.Touch.HasValue || now < x.Touch.Value - 1e-6)
                    continue;

                double since = now - x.Touch.Value;
                if (x.Time.HasValue && x.FeetOff.HasValue)
                {
                    double end = Math.Max(x.Touch.Value, x.FeetOff.Value);
                    if (now > end + HoldSeconds)
                        continue;
                    running = now < x.FeetOff.Value - 1e-6;
                    shown = running ? since : x.Time.Value;
                    return x;
                }

                if (since <= RunningSeconds)
                {
                    running = true;
                    shown = since;
                    return x;
                }
            }

            return null;
        }
    }
}
