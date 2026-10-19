#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Kinovea.Services;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>A turn of a finalist: the wall, 5 m before it, 15 m (or 5 m in the long events) after it, the breakout from the wall.</summary>
    public sealed class SwimFinalistTurn
    {
        public int Wall;
        public double? In5;
        public double? Out15;
        public double? Out5;
        public double? BreakoutTime;
        public double? BreakoutDist;
    }

    /// <summary>A swimmer of a final: checkpoints (s after the start signal) and the phases of the analysis of the final.</summary>
    public sealed class SwimFinalist
    {
        public int Rank;
        public string Name = "";
        public double Final;
        public SortedDictionary<int, double> Checkpoints = new SortedDictionary<int, double>();
        public double? BlockTime;
        public double? Start15;
        public double? StartBreakoutTime;
        public double? StartBreakoutDist;
        public double? FinishIn5;
        public List<SwimFinalistTurn> Turns = new List<SwimFinalistTurn>();

        /// <summary>Strokes of each 50 (key: the end of the 50).</summary>
        public SortedDictionary<int, int> Strokes = new SortedDictionary<int, int>();

        /// <summary>Stroke rate (cycles/min) and length of the cycle (m) of each 25 (key: the end of the 25).</summary>
        public SortedDictionary<int, double> Rate = new SortedDictionary<int, double>();
        public SortedDictionary<int, double> Cycle = new SortedDictionary<int, double>();

        public double? TimeAt(int d)
        {
            double t;
            return Checkpoints.TryGetValue(d, out t) ? t : (double?)null;
        }

        public SwimFinalistTurn TurnAt(int wall)
        {
            return Turns.FirstOrDefault(t => t.Wall == wall);
        }

        /// <summary>"C. DRESSEL" for the boards, "Caeleb DRESSEL" else.</summary>
        public string ShortName
        {
            get
            {
                string[] parts = (Name ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    return Name ?? "";

                // The family name is written in capitals in the analyses of the final.
                int family = Array.FindIndex(parts, p => p.Length > 1 && p == p.ToUpperInvariant() && p.Any(char.IsLetter));
                if (family <= 0)
                    family = parts.Length - 1;
                return parts[0].Substring(0, 1) + ". " + string.Join(" ", parts.Skip(family).ToArray());
            }
        }
    }

    /// <summary>A final: event (gender, style, distance, 50 m pool) and its swimmers in the order of the result.</summary>
    public sealed class SwimFinal
    {
        public string Gender = "";
        public string Style = "fs";
        public int Distance;
        public int Pool = 50;
        public string Source = "";
        public string Short = "";
        public List<SwimFinalist> Swimmers = new List<SwimFinalist>();

        /// <summary>"OG2024 · 100m bk Men" in the language of the analysis.</summary>
        public string Label
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} · {1}μ. {2} {3}"), Short, Distance, SwimPresets.StyleShort(Style),
                    SwimLang.Label(Gender == "F" ? "Γυναικών" : "Ανδρών"));
            }
        }

        /// <summary>"OG2024 · 100μ. bk Ανδρών", as the sheet gets it.</summary>
        public string LabelGreek
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} · {1}μ. {2} {3}", Short, Distance, SwimPresets.StyleShort(Style),
                    Gender == "F" ? "Γυναικών" : "Ανδρών");
            }
        }

        /// <summary>Mean of the swimmers at a checkpoint (null if fewer than two have it).</summary>
        public double? MeanAt(int d)
        {
            List<double> v = Swimmers.Select(s => s.TimeAt(d)).Where(t => t.HasValue).Select(t => t.Value).ToList();
            return v.Count >= 2 ? v.Average() : (double?)null;
        }
    }

    /// <summary>
    /// The finals for the comparisons (the 8 of an Olympic final), loaded once from a file of the analyses of the finals and kept
    /// on this computer (not in the program: the data stay with the analyst). Each finalist is also a reference for the Δ.
    /// </summary>
    public sealed class SwimFinals
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public const string Format = "kinovea-swim-finals";

        public string Source = "";
        public string Short = "";
        public List<SwimFinal> Finals = new List<SwimFinal>();

        public static string FilePath
        {
            get { return Path.Combine(Software.SettingsDirectory, "SwimFinals.json"); }
        }

        public int SwimmerCount
        {
            get { return Finals.Sum(f => f.Swimmers.Count); }
        }

        public static SwimFinals Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string error;
                    SwimFinals finals = Parse(File.ReadAllText(FilePath, Encoding.UTF8), out error);
                    if (finals != null)
                        return finals;
                    log.Error("Could not read the finals: " + error);
                }
            }
            catch (Exception e)
            {
                log.Error("Could not read the finals.", e);
            }

            return new SwimFinals();
        }

        /// <summary>
        /// The first time on a computer: a file of the finals next to the program (in its folder or in one of the five above it, e.g.
        /// "ai\OlympicFinalsParis2024.json" beside the portable folder) is imported as with the button. Null if there is none.
        /// </summary>
        public static SwimFinals ImportNearby()
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                for (int level = 0; dir != null && level < 6; level++, dir = dir.Parent)
                {
                    FileInfo[] files;
                    try
                    {
                        files = dir.GetFiles("*.json");
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    foreach (FileInfo f in files.Where(x => x.Name.IndexOf("final", StringComparison.OrdinalIgnoreCase) >= 0 && x.Length < 20 * 1024 * 1024)
                        .OrderByDescending(x => x.LastWriteTimeUtc))
                    {
                        if (string.Equals(f.FullName, FilePath, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string error;
                        SwimFinals finals = Import(f.FullName, out error);
                        if (finals != null)
                        {
                            log.InfoFormat("Finals imported from {0}: {1} finals.", f.FullName, finals.Finals.Count);
                            return finals;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                log.Error("Could not look for a file of the finals next to the program.", e);
            }

            return null;
        }

        /// <summary>The finals of a file (kept on this computer for the next times); null with the reason if it is not such a file.</summary>
        public static SwimFinals Import(string path, out string error)
        {
            error = null;
            string text;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }

            SwimFinals finals = Parse(text, out error);
            if (finals == null)
                return null;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, text, Encoding.UTF8);
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }

            return finals;
        }

        /// <summary>The finals of the text of a file (format "kinovea-swim-finals").</summary>
        public static SwimFinals Parse(string text, out string error)
        {
            try
            {
                return ParseText(text, out error);
            }
            catch (Exception e)
            {
                error = SwimLang.T("λάθος τιμή στο αρχείο (") + e.Message + ")";
                return null;
            }
        }

        private static SwimFinals ParseText(string text, out string error)
        {
            error = null;
            JObject o;
            try
            {
                o = JObject.Parse(text);
            }
            catch (Exception e)
            {
                error = SwimLang.T("δεν είναι αρχείο JSON (") + e.Message + ")";
                return null;
            }

            if ((string)o["format"] != Format)
            {
                error = SwimLang.T("δεν είναι αρχείο τελικών (format ") + Format + ")";
                return null;
            }

            SwimFinals finals = new SwimFinals { Source = (string)o["source"] ?? "", Short = (string)o["short"] ?? "" };
            int pool = (int?)o["pool"] ?? 50;
            JArray list = o["finals"] as JArray;
            if (list == null)
            {
                error = SwimLang.T("δεν έχει τελικούς");
                return null;
            }

            foreach (JObject f in list.OfType<JObject>())
            {
                SwimFinal final = new SwimFinal
                {
                    Gender = SwimReference.NormalizeGender((string)f["gender"] ?? ""),
                    Style = (string)f["style"] ?? "fs",
                    Distance = (int?)f["distance"] ?? 0,
                    Pool = (int?)f["pool"] ?? pool,
                    Source = finals.Source,
                    Short = finals.Short
                };

                JArray swimmers = f["swimmers"] as JArray;
                if (final.Distance <= 0 || swimmers == null)
                    continue;

                foreach (JObject s in swimmers.OfType<JObject>())
                {
                    SwimFinalist sw = new SwimFinalist
                    {
                        Rank = (int?)s["rank"] ?? final.Swimmers.Count + 1,
                        Name = (string)s["name"] ?? "",
                        Final = (double?)s["final"] ?? 0,
                        BlockTime = (double?)s["blockTime"],
                        Start15 = (double?)s["start15"],
                        StartBreakoutTime = (double?)s["startBreakoutTime"],
                        StartBreakoutDist = (double?)s["startBreakoutDist"],
                        FinishIn5 = (double?)s["finishIn5"]
                    };

                    ReadMap(s["checkpoints"] as JObject, sw.Checkpoints);
                    if (sw.Final > 0)
                        sw.Checkpoints[final.Distance] = sw.Final;
                    ReadMap(s["rate"] as JObject, sw.Rate);
                    ReadMap(s["cycle"] as JObject, sw.Cycle);
                    JObject strokes = s["strokes"] as JObject;
                    if (strokes != null)
                    {
                        foreach (JProperty p in strokes.Properties())
                        {
                            int d;
                            if (int.TryParse(p.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out d) && p.Value.Type == JTokenType.Integer)
                                sw.Strokes[d] = (int)p.Value;
                        }
                    }

                    JArray turns = s["turns"] as JArray;
                    if (turns != null)
                    {
                        foreach (JObject t in turns.OfType<JObject>())
                        {
                            sw.Turns.Add(new SwimFinalistTurn
                            {
                                Wall = (int?)t["wall"] ?? 0,
                                In5 = (double?)t["in5"],
                                Out15 = (double?)t["out15"],
                                Out5 = (double?)t["out5"],
                                BreakoutTime = (double?)t["breakoutTime"],
                                BreakoutDist = (double?)t["breakoutDist"]
                            });
                        }
                    }

                    if (sw.Final > 0)
                        final.Swimmers.Add(sw);
                }

                final.Swimmers = final.Swimmers.OrderBy(x => x.Rank).ToList();
                if (final.Swimmers.Count > 0)
                    finals.Finals.Add(final);
            }

            if (finals.Finals.Count == 0)
            {
                error = SwimLang.T("δεν έχει τελικούς με κολυμβητές");
                return null;
            }

            return finals;
        }

        private static void ReadMap(JObject o, SortedDictionary<int, double> map)
        {
            if (o == null)
                return;

            foreach (JProperty p in o.Properties())
            {
                int d;
                if (!int.TryParse(p.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out d))
                    continue;
                if (p.Value.Type == JTokenType.Float || p.Value.Type == JTokenType.Integer)
                    map[d] = (double)p.Value;
            }
        }

        /// <summary>
        /// The final of the event of the race: same style, distance and gender, a 50 m pool, not a relay (null if there is none or
        /// the gender of the athlete is not known).
        /// </summary>
        public SwimFinal For(SwimRace race)
        {
            if (race == null || race.Relay || race.IsYards || race.Pool != 50)
                return null;

            string gender = SwimReference.NormalizeGender(race.AthleteGender);
            if (gender != "M" && gender != "F")
                return null;

            return Finals.FirstOrDefault(f => f.Style == race.Style && f.Distance == race.Distance && f.Pool == race.Pool && f.Gender == gender);
        }

        /// <summary>The swimmers of the final of the race as references (for the Δ): fastest first.</summary>
        public List<SwimReference> ReferencesFor(SwimRace race)
        {
            SwimFinal final = For(race);
            return final == null ? new List<SwimReference>() : final.Swimmers.Select(s => Reference(final, s)).ToList();
        }

        /// <summary>A reference by its id (one of the finalists), null if it is not one of them.</summary>
        public SwimReference FindReference(string id)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith(IdPrefix, StringComparison.Ordinal))
                return null;

            foreach (SwimFinal f in Finals)
            {
                foreach (SwimFinalist s in f.Swimmers)
                {
                    if (ReferenceId(f, s) == id)
                        return Reference(f, s);
                }
            }

            return null;
        }

        private const string IdPrefix = "finals|";

        private static string ReferenceId(SwimFinal f, SwimFinalist s)
        {
            return IdPrefix + f.Short + "|" + f.Gender + "|" + f.Style + "|" + f.Distance.ToString(CultureInfo.InvariantCulture) + "|" +
                s.Rank.ToString(CultureInfo.InvariantCulture) + "|" + s.Name;
        }

        public static SwimReference Reference(SwimFinal f, SwimFinalist s)
        {
            SwimReference r = new SwimReference
            {
                Id = ReferenceId(f, s),
                Name = string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} · {1} ({2}ος)"), s.Name, f.Short, s.Rank),
                Gender = f.Gender,
                Style = f.Style,
                Distance = f.Distance,
                Pool = f.Pool,
                PoolUnit = SwimRace.UnitMeters,
                Source = f.Source
            };

            foreach (KeyValuePair<int, double> c in s.Checkpoints)
                r.Times.Add(new SwimReferenceSplit { D = c.Key, T = c.Value });
            return r;
        }
    }

    /// <summary>One value of the race against the swimmers of the final (the comparison of the panel and of the report).</summary>
    public sealed class SwimFinalsMetric
    {
        /// <summary>Phase of the race ("Εκκίνηση", "Στροφή 50μ.", "Πέρασμα", "Τερματισμός", "Χεριές", "Ρυθμός").</summary>
        public string Group = "";
        public string Label = "";
        public string Unit = "s";
        public int Decimals = 2;

        /// <summary>False for the values that are not better when lower (strokes, rate, cycle, distance under the water).</summary>
        public bool LowerIsBetter = true;
        public bool Neutral;
        public double? Athlete;

        /// <summary>The value of each swimmer of the final (null where the analysis of the final has none).</summary>
        public List<double?> Finalists = new List<double?>();

        /// <summary>Place of the athlete among the swimmers of the final with a value (1 = best), 0 if not comparable.</summary>
        public int Rank
        {
            get
            {
                if (!Athlete.HasValue || Neutral)
                    return 0;
                List<double> v = Finalists.Where(x => x.HasValue).Select(x => x.Value).ToList();
                if (v.Count == 0)
                    return 0;
                double a = Athlete.Value;
                return 1 + v.Count(x => LowerIsBetter ? x < a - 1e-9 : x > a + 1e-9);
            }
        }

        public int Count
        {
            get { return Finalists.Count(x => x.HasValue); }
        }

        public double? Best
        {
            get
            {
                List<double> v = Finalists.Where(x => x.HasValue).Select(x => x.Value).ToList();
                if (v.Count == 0)
                    return null;
                return LowerIsBetter ? v.Min() : v.Max();
            }
        }

        public double? Mean
        {
            get
            {
                List<double> v = Finalists.Where(x => x.HasValue).Select(x => x.Value).ToList();
                return v.Count == 0 ? (double?)null : v.Average();
            }
        }

        public string Format(double? value)
        {
            if (!value.HasValue)
                return "–";
            if (Unit == "s" && value.Value >= 60)
                return SwimReference.FormatTime(value.Value);
            return value.Value.ToString(Decimals == 0 ? "0" : Decimals == 1 ? "0.0" : "0.00", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The values of a race against the swimmers of a final, phase by phase.</summary>
    public static class SwimFinalsCompare
    {
        public static List<SwimFinalsMetric> Metrics(SwimRace race, SwimComputed computed, SwimFinal final)
        {
            List<SwimFinalsMetric> list = new List<SwimFinalsMetric>();
            if (race == null || computed == null || final == null)
                return list;

            List<SwimFinalist> sw = final.Swimmers;
            Func<string, string, double?, Func<SwimFinalist, double?>, SwimFinalsMetric> add = (group, label, athlete, of) =>
            {
                SwimFinalsMetric m = new SwimFinalsMetric { Group = group, Label = label, Athlete = athlete, Finalists = sw.Select(of).ToList() };
                if (m.Count > 0)
                    list.Add(m);
                return m;
            };

            // Start.
            add("Εκκίνηση", "Αποκόλληση από τον βατήρα", computed.BlockTime, s => s.BlockTime);
            add("Εκκίνηση", "15μ. εκκίνηση", computed.Start15, s => s.Start15);
            add("Εκκίνηση", "Έξοδος: χρόνος", computed.StartUnderwaterTime, s => s.StartBreakoutTime).Neutral = true;
            SwimFinalsMetric startDist = add("Εκκίνηση", "Έξοδος: απόσταση", race.UnderwaterDist, s => s.StartBreakoutDist);
            startDist.Unit = "μ.";
            startDist.Neutral = true;

            // Checkpoints: each 50 (each 100 in the long events) and the end of each length.
            int step = final.Distance >= 800 ? 100 : 50;
            for (int d = step; d <= final.Distance; d += step)
            {
                SwimRow row = computed.Rows.FirstOrDefault(r => r.D == d && r.T.HasValue);
                int at = d;
                add("Περάσματα", d.ToString(CultureInfo.InvariantCulture) + "μ.", row != null ? row.T : null, s => s.TimeAt(at));
            }

            // Turns: 5 m before the wall + 15 m after it (the long events: the 5 m before), the breakout from the wall.
            foreach (SwimTurnComputed turn in computed.Turns.Where(t => !t.Exchange).OrderBy(t => t.Wall))
            {
                int wall = turn.Wall;
                string group = "Στροφή " + wall.ToString(CultureInfo.InvariantCulture) + "μ.";
                bool longEvent = sw.Any(s => s.TurnAt(wall) != null && s.TurnAt(wall).Out5.HasValue && !s.TurnAt(wall).Out15.HasValue);
                if (!longEvent)
                {
                    add(group, "5μ. πριν + 15μ. μετά", turn.InOut, s =>
                    {
                        SwimFinalistTurn t = s.TurnAt(wall);
                        return t != null && t.In5.HasValue && t.Out15.HasValue ? t.In5 + t.Out15 : null;
                    });
                    add(group, "15μ. μετά τον τοίχο", turn.Out15, s => s.TurnAt(wall) != null ? s.TurnAt(wall).Out15 : null);
                }

                add(group, "5μ. πριν τον τοίχο", turn.In5, s => s.TurnAt(wall) != null ? s.TurnAt(wall).In5 : null);
                SwimRow wallRow = computed.Rows.FirstOrDefault(r => r.D == wall && r.T.HasValue);
                double? breakout = turn.Breakout.HasValue && wallRow != null ? turn.Breakout - wallRow.T : null;
                add(group, "Έξοδος: χρόνος από τον τοίχο", breakout, s => s.TurnAt(wall) != null ? s.TurnAt(wall).BreakoutTime : null).Neutral = true;
                SwimTurnManual manual = race.GetTurn(wall, false);
                SwimFinalsMetric dist = add(group, "Έξοδος: απόσταση", manual != null ? manual.UnderwaterDist : null,
                    s => s.TurnAt(wall) != null ? s.TurnAt(wall).BreakoutDist : null);
                dist.Unit = "μ.";
                dist.Neutral = true;
            }

            add("Τερματισμός", "Τελευταία 5μ.", computed.FinishLast5, s => s.FinishIn5);

            // Strokes, rate and length of the cycle of each 50 (the final has them by 25: the mean of the two), each kind together.
            List<SwimBlock> blocks = computed.Blocks.Where(b => b.To - b.From == 50).OrderBy(b => b.To).ToList();
            Func<SwimBlock, string> blockLabel = b => b.From.ToString(CultureInfo.InvariantCulture) + "–" + b.To.ToString(CultureInfo.InvariantCulture) + "μ.";
            foreach (SwimBlock block in blocks)
            {
                int to = block.To;
                SwimFinalsMetric strokes = add("Χεριές", blockLabel(block), block.Strokes, s =>
                {
                    int n;
                    return s.Strokes.TryGetValue(to, out n) ? n : (double?)null;
                });
                strokes.Unit = "χεριές";
                strokes.Decimals = 1;
                strokes.Neutral = true;
            }

            foreach (SwimBlock block in blocks)
            {
                int to = block.To;
                SwimFinalsMetric rate = add("Συχνότητα", blockLabel(block), block.Sr, s => Mean25(s.Rate, to));
                rate.Unit = "κύκλοι/λ.";
                rate.Decimals = 1;
                rate.Neutral = true;
            }

            foreach (SwimBlock block in blocks)
            {
                int to = block.To;
                SwimFinalsMetric cycle = add("Μήκος κύκλου", blockLabel(block), block.Sl, s => Mean25(s.Cycle, to));
                cycle.Unit = "μ./κύκλο";
                cycle.Neutral = true;
            }

            return list;
        }

        private static double? Mean25(SortedDictionary<int, double> map, int to)
        {
            double a, b;
            bool ha = map.TryGetValue(to - 25, out a), hb = map.TryGetValue(to, out b);
            if (ha && hb)
                return (a + b) / 2;
            if (hb)
                return b;
            return ha ? a : (double?)null;
        }

        /// <summary>
        /// The board of a checkpoint: the swimmers of the final at that distance and the athlete among them (place by time),
        /// null if the final has no time there.
        /// </summary>
        public static List<SwimFinalsBoardRow> Board(SwimFinal final, int distance, double athleteTime, string athlete)
        {
            if (final == null)
                return null;

            List<SwimFinalsBoardRow> rows = final.Swimmers.Where(s => s.TimeAt(distance).HasValue)
                .Select(s => new SwimFinalsBoardRow { Name = s.ShortName, Time = s.TimeAt(distance).Value, Rank = s.Rank }).ToList();
            if (rows.Count < 2)
                return null;

            rows.Add(new SwimFinalsBoardRow { Name = string.IsNullOrWhiteSpace(athlete) ? SwimLang.T("ΑΘΛΗΤΗΣ") : athlete.Trim(), Time = athleteTime, IsAthlete = true });
            rows = rows.OrderBy(r => r.Time).ThenBy(r => r.IsAthlete ? 1 : 0).ToList();

            // The gap to the first line; the athlete in front of the whole final: his lead on its winner (negative).
            double first = rows[0].Time;
            double leader = rows.Where(r => !r.IsAthlete).Min(r => r.Time);
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Place = i + 1;
                rows[i].Behind = rows[i].IsAthlete && i == 0 ? rows[i].Time - leader : rows[i].Time - first;
            }

            return rows;
        }
    }

    /// <summary>A line of the board of a checkpoint.</summary>
    public sealed class SwimFinalsBoardRow
    {
        public int Place;

        /// <summary>Place in the result of the final (0 for the athlete).</summary>
        public int Rank;
        public string Name = "";
        public double Time;

        /// <summary>Behind the first of the final at this checkpoint (s).</summary>
        public double Behind;
        public bool IsAthlete;
    }
}
