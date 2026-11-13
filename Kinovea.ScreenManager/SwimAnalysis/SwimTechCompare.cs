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
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.22.0) The elite of the comparison of a technique analysis (training) in the report: the mean of the swimmers of a final kept on
    /// this computer, or its winner (the values are kept here, so the report is the same from any computer), or an elite technique
    /// analysis of the Google Sheet (sheet «Elite τεχνική»: all its numbers and its snapshots).
    /// </summary>
    public sealed class SwimTechElite
    {
        public const string TypeFinal = "final";
        public const string TypeRef = "ref";

        /// <summary>"final" or "ref".</summary>
        public string Type = "";

        /// <summary>final: "set|gender|style|distance|mean" (or "|first"); ref: the Race ID of the elite analysis.</summary>
        public string Id = "";

        /// <summary>As the report shows it: "OG2024 · 100μ. fs Γυναικών · μέσος όρος 8" (no names of the finalists).</summary>
        public string Label = "";

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Gender;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Style;

        /// <summary>The event of the final (for its «Επίπεδα» in the report), 0 for an elite analysis.</summary>
        public int Distance;
        public int Pool;

        /// <summary>final: the values of the turns and of the start, with the keys of the report (in5, out15, inOut15, …).</summary>
        public Dictionary<string, double> Turn = new Dictionary<string, double>();
        public Dictionary<string, double> Start = new Dictionary<string, double>();

        [JsonIgnore]
        public bool IsSet
        {
            get { return (Type == TypeFinal || Type == TypeRef) && !string.IsNullOrEmpty(Id); }
        }

        public bool Same(SwimTechElite other)
        {
            return other != null && other.Type == Type && other.Id == Id;
        }

        /// <summary>What the report gets (race.compare.techElite).</summary>
        public JObject ToJson()
        {
            JObject o = new JObject();
            o["type"] = Type ?? "";
            o["id"] = Id ?? "";
            o["label"] = Label ?? "";
            if (Type == TypeFinal)
            {
                o["gender"] = Gender ?? "";
                o["style"] = Style ?? "";
                o["distance"] = Distance;
                o["pool"] = Pool > 0 ? Pool : 50;
                JObject values = new JObject();
                values["turn"] = Values(Turn);
                values["start"] = Values(Start);
                o["values"] = values;
            }

            return o;
        }

        private static JObject Values(Dictionary<string, double> map)
        {
            JObject o = new JObject();
            if (map == null)
                return o;
            foreach (KeyValuePair<string, double> pair in map.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!double.IsNaN(pair.Value) && !double.IsInfinity(pair.Value))
                    o[pair.Key] = Math.Round(pair.Value, 3);
            }

            return o;
        }

        public void Normalize()
        {
            if (Type == null) Type = "";
            if (Id == null) Id = "";
            if (Label == null) Label = "";
            if (Turn == null) Turn = new Dictionary<string, double>();
            if (Start == null) Start = new Dictionary<string, double>();
        }
    }

    /// <summary>(2.22.0) A technique analysis of the athlete in the Google Sheet (same style and pool), for the comparison in the report.</summary>
    public sealed class SwimTechAnalysisItem
    {
        public string Id = "";
        public string Label = "";
        public string Date = "";

        /// <summary>The date as yyyymmdd (0 if unknown).</summary>
        public int Key;
        public int Turns;
        public int Starts;

        /// <summary>The mean of −5 → +15 of its turns (else wall → +15) and of the 15 m of its starts, for "the best".</summary>
        public double? Turn15;
        public double? Start15;

        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>(2.22.0) The finals kept on this computer as the elite of a technique analysis.</summary>
    public static class SwimTechEliteFinals
    {
        /// <summary>
        /// The finals of the style of the analysis (of the gender of the athlete when it is known), each as the mean of its swimmers and
        /// as its winner.
        /// </summary>
        public static List<SwimTechElite> For(SwimFinals finals, SwimRace race)
        {
            List<SwimTechElite> list = new List<SwimTechElite>();
            if (finals == null || race == null)
                return list;

            string gender = SwimReference.NormalizeGender(race.AthleteGender);
            bool known = gender == "M" || gender == "F";
            IEnumerable<SwimFinal> fit = finals.Finals.Where(f => f.Style == race.Style && (!known || f.Gender == gender))
                .OrderBy(f => f.Short, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Gender == gender ? 0 : 1).ThenBy(f => f.Distance);
            foreach (SwimFinal f in fit)
            {
                SwimTechElite mean = Of(f, false);
                SwimTechElite first = Of(f, true);
                if (mean != null)
                    list.Add(mean);
                if (first != null)
                    list.Add(first);
            }

            return list;
        }

        /// <summary>
        /// The values of a final for the report: the turns (−5, wall → +15, −5 → +15, the breakout: the mean over the swimmers and their
        /// walls) and the start (release, 15 m, breakout). first: only the winner. Null if the final has none of them.
        /// </summary>
        public static SwimTechElite Of(SwimFinal f, bool first)
        {
            if (f == null || f.Swimmers.Count == 0)
                return null;

            List<SwimFinalist> swimmers = first ? f.Swimmers.OrderBy(s => s.Rank).Take(1).ToList() : f.Swimmers;
            if (!first && swimmers.Count < 2)
                return null;

            SwimTechElite e = new SwimTechElite
            {
                Type = SwimTechElite.TypeFinal,
                Id = string.Join("|", f.Short, f.Gender, f.Style, f.Distance.ToString(CultureInfo.InvariantCulture), first ? "first" : "mean"),
                Gender = f.Gender,
                Style = f.Style,
                Distance = f.Distance,
                Pool = f.Pool,
                Label = f.Label + " · " + (first ? SwimLang.T("1η θέση") : string.Format(CultureInfo.InvariantCulture, SwimLang.T("μέσος όρος {0}"), swimmers.Count))
            };

            List<SwimFinalistTurn> turns = swimmers.SelectMany(s => s.Turns).ToList();
            Put(e.Turn, "in5", turns.Select(t => t.In5));
            Put(e.Turn, "out15", turns.Select(t => t.Out15));
            Put(e.Turn, "inOut15", turns.Select(t => t.In5.HasValue && t.Out15.HasValue ? t.In5 + t.Out15 : null));
            Put(e.Turn, "breakoutTime", turns.Select(t => t.BreakoutTime));
            Put(e.Turn, "breakoutDist", turns.Select(t => t.BreakoutDist));
            Put(e.Start, "blockTime", swimmers.Select(s => s.BlockTime));
            Put(e.Start, "t15", swimmers.Select(s => s.Start15));
            Put(e.Start, "breakoutTime", swimmers.Select(s => s.StartBreakoutTime));
            Put(e.Start, "breakoutDist", swimmers.Select(s => s.StartBreakoutDist));
            return e.Turn.Count + e.Start.Count > 0 ? e : null;
        }

        private static void Put(Dictionary<string, double> map, string key, IEnumerable<double?> values)
        {
            List<double> v = values.Where(x => x.HasValue && !double.IsNaN(x.Value) && x.Value > 0).Select(x => x.Value).ToList();
            if (v.Count > 0)
                map[key] = v.Average();
        }

        /// <summary>"10.10.2026", "10/10/2026", "2026-10-10" → 20261010 (0 if it is not a date), as the Apps Script compares them.</summary>
        public static int DateKey(string date)
        {
            if (string.IsNullOrWhiteSpace(date))
                return 0;

            string[] formats = { "d.M.yyyy", "d/M/yyyy", "d-M-yyyy", "yyyy-M-d", "d.M.yy", "d/M/yy" };
            string text = date.Trim();
            int space = text.IndexOf(' ');
            if (space > 0)
                text = text.Substring(0, space);

            DateTime d;
            if (!DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return 0;
            return d.Year * 10000 + d.Month * 100 + d.Day;
        }

        /// <summary>
        /// The previous analysis that "auto" (the newest one not after this one) or "best" (the fastest: −5 → +15 of the turns, else the
        /// 15 m of the starts) gives, as the Apps Script picks it: of the same kind as this analysis (turns, else starts). Null: none.
        /// </summary>
        public static SwimTechAnalysisItem Pick(List<SwimTechAnalysisItem> candidates, SwimRace race, bool turns, bool best)
        {
            if (candidates == null || race == null)
                return null;

            int day = DateKey(race.Date);
            List<SwimTechAnalysisItem> fit = candidates.Where(a => a.Id != race.Id && (turns ? a.Turns > 0 : a.Starts > 0) &&
                (day == 0 || a.Key == 0 || a.Key <= day)).ToList();
            if (fit.Count == 0)
                return null;
            if (!best)
                return fit[0];

            return fit.OrderBy(a =>
            {
                double? v = turns ? a.Turn15 : a.Start15;
                return v.HasValue ? v.Value : 1e9;
            }).First();
        }
    }
}
