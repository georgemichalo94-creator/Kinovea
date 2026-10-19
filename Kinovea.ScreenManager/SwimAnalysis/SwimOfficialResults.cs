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
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>One swimmer of a heat of the official results of a competition (since 2026-10-19).</summary>
    public class SwimOfficialEntry
    {
        /// <summary>The heat (1, 2…; a final: 1) and the lane.</summary>
        public int Heat;
        public int Lane;

        /// <summary>The source's code of the athlete (the KOE live results and Microplus: the federation's number of a Greek athlete).</summary>
        public string Code = "";

        /// <summary>The athlete's code of the international federation when published (World Aquatics).</summary>
        public string IfId = "";

        /// <summary>The names as published (Greek in the KOE results) and in Latin letters (empty when the source has one form only).</summary>
        public string FamilyName = "";
        public string GivenName = "";
        public string FamilyNameLatin = "";
        public string GivenNameLatin = "";

        /// <summary>The country (IOC code: GRE, CYP…), the club as published, the year of birth (0: unknown), "M" or "F".</summary>
        public string Nation = "";
        public string Club = "";
        public int BirthYear;
        public string Gender = "";

        /// <summary>The final time (s), the cumulative times at every 50 m before it (s), the reaction time (s) when published.</summary>
        public double? Time;
        public List<double> Splits = new List<double>();
        public double? Reaction;

        /// <summary>Not a time (DSQ, DNS, DNF…): what the results say.</summary>
        public string Status = "";

        public string Name
        {
            get { return (FamilyName + " " + GivenName).Trim(); }
        }

        public string NameLatin
        {
            get { return (FamilyNameLatin + " " + GivenNameLatin).Trim(); }
        }

        /// <summary>The official times as SwimRace.Official keeps them: the splits and the final time (null without a final time).</summary>
        public List<double> OfficialTimes()
        {
            if (!Time.HasValue)
                return null;
            List<double> times = new List<double>(Splits);
            times.Add(Time.Value);
            return times;
        }
    }

    /// <summary>A heat (or a final) of an event of the official results.</summary>
    public class SwimOfficialUnit
    {
        /// <summary>"koe" (live.koe.org.gr), "microplus" (results.microplustimingservices.com), "worldaquatics".</summary>
        public string Source = "";
        public string CompetitionCode = "";
        public string UnitCode = "";

        /// <summary>The event as published ("100m FREESTYLE") and read: distance (m), style ("fs", "bk", "br", "fly", "im"), relay.</summary>
        public string Event = "";
        public int Distance;
        public string Style = "";
        public bool Relay;

        /// <summary>"M", "F" or "X" (mixed, as SwimReference.NormalizeGender); the category of age as published ("MEN - GENERAL").</summary>
        public string Gender = "";
        public string Category = "";

        /// <summary>The round as published ("QUALIFIERS", "FINALS") and read: "heats", "semi", "final", "timed" (timed finals).</summary>
        public string Phase = "";
        public string Round = "";

        /// <summary>The heat of the round (1, 2…) and the day.</summary>
        public int Heat;
        public DateTime? Date;

        public List<SwimOfficialEntry> Entries = new List<SwimOfficialEntry>();

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} · {1} · {2} {3}", Category, Event, Phase, Heat);
        }
    }

    /// <summary>A competition of the KOE live results.</summary>
    public class SwimKoeCompetition
    {
        public int Code;
        public string OrganizationId = "";
        public string Name = "";
        public string NameLatin = "";
        public string Place = "";
        public DateTime? From;
        public DateTime? To;
        public int Season;

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} · {1:d/M/yyyy} · {2}", Name, From, Place);
        }
    }

    /// <summary>A competition of a Microplus results site: its address and the folder of its files.</summary>
    public class SwimMicroplusCompetition
    {
        public string Site = "";
        public string Files = "";
        public string Name = "";

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// The official results of the competitions on the web (since 2026-10-19): the KOE live results (live.koe.org.gr, the
    /// competitions of the federation and the international ones of the Greek athletes), read-only and public. Calls block: run
    /// them off the UI thread.
    /// </summary>
    public static class SwimOfficialResults
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public const string KoeLive = "https://live.koe.org.gr";

        /// <summary>The swimming competitions of a season of the KOE live results (the season of a competition: the year it ends in).</summary>
        public static List<SwimKoeCompetition> KoeCompetitions(int season)
        {
            JObject o = GetJson(KoeLive + "/api-gen/Organizations?RacingSeason=" + season.ToString(CultureInfo.InvariantCulture) + "&SportCode=1");
            List<SwimKoeCompetition> list = new List<SwimKoeCompetition>();
            JArray array = o == null ? null : o["Organizations"] as JArray;
            if (array == null)
                return list;

            foreach (JToken t in array)
            {
                if (Int(t["SportCode"]) != 1)
                    continue;
                list.Add(new SwimKoeCompetition
                {
                    Code = Int(t["CompetitionCode"]),
                    OrganizationId = Str(t["OrganizationID"]),
                    Name = Str(t["Description"]),
                    NameLatin = Str(t["Description_ENG"]),
                    Place = Str(t["Place_Short"]),
                    From = Date(t["DateOfStart"]),
                    To = Date(t["DateOfEnd"]),
                    Season = Int(t["RacingSeason"])
                });
            }

            return list;
        }

        /// <summary>The heats and finals of a competition of the KOE live results, without their results.</summary>
        public static List<SwimOfficialUnit> KoeSchedule(int competition)
        {
            JObject o = GetJson(KoeLive + "/api/Schedules?OrganizationID=&CompetitionCode=" + competition.ToString(CultureInfo.InvariantCulture));
            List<SwimOfficialUnit> units = new List<SwimOfficialUnit>();
            JArray schedules = o == null ? null : o["Schedules"] as JArray;
            if (schedules == null)
                return units;

            foreach (JToken s in schedules)
            {
                JArray list = s.SelectToken("Schedules.Schedule") as JArray;
                if (list == null)
                    continue;

                foreach (JToken t in list)
                {
                    SwimOfficialUnit u = new SwimOfficialUnit
                    {
                        Source = "koe",
                        CompetitionCode = competition.ToString(CultureInfo.InvariantCulture),
                        UnitCode = Str(t["UnitCode"]),
                        Event = Str(t["Event_Description_ENG"]),
                        Gender = Gender(Str(t["GenderCode_ENG"])),
                        Category = Str(t["Category_Description_ENG"]),
                        Phase = Str(t["PhaseDescription_ENG"]),
                        Round = Round(Str(t["PhaseType_ENG"]), Str(t["PhaseDescription_ENG"])),
                        Heat = Int(t["UnitNum"]),
                        Date = Date(t["StartDate"])
                    };
                    ParseEvent(u.Event, out u.Distance, out u.Style, out u.Relay);
                    if (!string.IsNullOrEmpty(u.UnitCode) && u.Distance > 0)
                        units.Add(u);
                }
            }

            return units;
        }

        /// <summary>The results of a heat of the KOE live results (its entries; false when they could not be read).</summary>
        public static bool KoeResults(SwimOfficialUnit unit)
        {
            JObject o = GetJson(KoeLive + "/api/Results?OrganizationID=&CompetitionCode=" + Uri.EscapeDataString(unit.CompetitionCode) +
                "&UnitCode=" + Uri.EscapeDataString(unit.UnitCode));
            JArray outer = o == null ? null : o["Results"] as JArray;
            if (outer == null)
                return false;

            unit.Entries.Clear();
            foreach (JToken r in outer)
            {
                JArray list = r.SelectToken("Results.Results") as JArray;
                if (list == null)
                    continue;

                foreach (JToken t in list)
                {
                    JToken a = t.SelectToken("Composition.Athlete[0].Description[0]");
                    SwimOfficialEntry e = new SwimOfficialEntry
                    {
                        Heat = unit.Heat,
                        Lane = Int(t["StartOrder"]),
                        Code = Str(t["CompetitorCode"]),
                        Club = Str(t["OrganisationCode"]),
                        Status = Str(t["IRM_ENG"]),
                        Time = Time(Str(t["Result"]))
                    };

                    if (a != null)
                    {
                        e.FamilyName = Str(a["FamilyName"]);
                        e.GivenName = Str(a["GivenName"]);
                        e.FamilyNameLatin = Str(a["FamilyName_ENG"]);
                        e.GivenNameLatin = Str(a["GivenName_ENG"]);
                        e.Gender = Gender(Str(a["GenderCode_ENG"]));
                        DateTime? born = Date(a["BirthDate"]);
                        e.BirthYear = born.HasValue ? born.Value.Year : 0;
                        e.Nation = Str(a["NationCode"]);
                        e.IfId = Str(a["IFId"]);
                    }

                    if (string.IsNullOrEmpty(e.Nation))
                        e.Nation = Str(t["NationCode"]);

                    // The cumulative times at the walls ("PROGRESS"/"INTERMEDIATE" at "50.00", "100.00"…) and the reaction time.
                    JArray ex = t.SelectToken("ExtendedResults.ExtendedResult") as JArray;
                    SortedDictionary<double, double> at = new SortedDictionary<double, double>();
                    if (ex != null)
                    {
                        foreach (JToken x in ex)
                        {
                            string code = Str(x["Code"]).ToUpperInvariant();
                            double? value = Time(Str(x["Value"]));
                            if (!value.HasValue)
                                continue;
                            if (code == "INTERMEDIATE")
                            {
                                double pos;
                                if (double.TryParse(Str(x["Pos"]), NumberStyles.Float, CultureInfo.InvariantCulture, out pos) && pos > 0)
                                    at[pos] = value.Value;
                            }
                            else if (code == "RT" || code == "REACTION" || code == "REACTION_TIME")
                            {
                                e.Reaction = value;
                            }
                        }
                    }

                    e.Splits = SplitsEvery50(at, unit.Distance);
                    unit.Entries.Add(e);
                }
            }

            return true;
        }

        /// <summary>
        /// The cumulative times at every 50 m before the finish (the official splits SwimRace.Official keeps) from the times by distance;
        /// empty when one is missing (25 m splits are left out).
        /// </summary>
        public static List<double> SplitsEvery50(IDictionary<double, double> at, int distance)
        {
            List<double> splits = new List<double>();
            for (int d = 50; d < distance; d += 50)
            {
                double t;
                if (!at.TryGetValue(d, out t))
                    return new List<double>();
                splits.Add(t);
            }

            return splits;
        }

        /// <summary>"100m FREESTYLE", "200m INDIVIDUAL MEDLEY", "4x100m FREESTYLE" (and the Greek forms) read.</summary>
        public static void ParseEvent(string text, out int distance, out string style, out bool relay)
        {
            distance = 0;
            style = "";
            relay = false;
            if (string.IsNullOrWhiteSpace(text))
                return;

            string s = text.ToUpperInvariant();
            Match m = Regex.Match(s, @"(\d+)\s*[XΧ]\s*(\d+)\s*M");
            if (m.Success)
            {
                relay = true;
                distance = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                m = Regex.Match(s, @"(\d+)\s*M");
                if (m.Success)
                    distance = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            }

            if (s.Contains("MEDLEY") || s.Contains("ΜΙΚΤΗ"))
                style = "im";
            else if (s.Contains("BACK") || s.Contains("ΥΠΤΙΟ"))
                style = "bk";
            else if (s.Contains("BREAST") || s.Contains("ΠΡΟΣΘΙΟ"))
                style = "br";
            else if (s.Contains("BUTTERFLY") || s.Contains("FLY") || s.Contains("ΠΕΤΑΛΟΥΔΑ"))
                style = "fly";
            else if (s.Contains("FREE") || s.Contains("ΕΛΕΥΘΕΡΟ"))
                style = "fs";
        }

        /// <summary>The round of a phase: "heats", "semi", "final", "timed" (a final in heats, the long events).</summary>
        public static string Round(string type, string description)
        {
            string s = ((type ?? "") + " " + (description ?? "")).ToUpperInvariant();
            if (s.Contains("TIM") || s.Contains("SERIES"))
                return "timed";
            if (s.Contains("SEMI"))
                return "semi";
            if (s.Contains("PRE") || s.Contains("QUALIF") || s.Contains("HEAT"))
                return "heats";
            if (s.Contains("FIN"))
                return "final";
            return "";
        }

        private static string Gender(string code)
        {
            string s = (code ?? "").Trim().ToUpperInvariant();
            return s == "M" ? "M" : (s == "W" || s == "F") ? "F" : s == "X" ? "X" : "";
        }

        private static double? Time(string text)
        {
            double t = SwimRace.ParseRaceTime(text);
            return double.IsNaN(t) || t <= 0 ? (double?)null : t;
        }

        private static string Str(JToken t)
        {
            return t == null || t.Type == JTokenType.Null ? "" : ((string)t ?? "").Trim();
        }

        private static int Int(JToken t)
        {
            int i;
            return int.TryParse(Str(t), NumberStyles.Integer, CultureInfo.InvariantCulture, out i) ? i : 0;
        }

        private static DateTime? Date(JToken t)
        {
            DateTime d;
            return DateTime.TryParse(Str(t), CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : (DateTime?)null;
        }

        public const string MicroplusHost = "results.microplustimingservices.com";

        /// <summary>
        /// The competition of a link of a Microplus results site (results.microplustimingservices.com/ACROPOLIS2024/, any page of it):
        /// the folder of its files, from its home page (its "pathManifestazione"). Null when it is not one or it cannot be read.
        /// </summary>
        public static SwimMicroplusCompetition MicroplusCompetition(string link)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(link) || !Uri.TryCreate(link.Trim(), UriKind.Absolute, out uri) ||
                !uri.Host.EndsWith(MicroplusHost, StringComparison.OrdinalIgnoreCase))
                return null;

            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return null;

            // The home page of the site, after its redirections (location = "….php").
            string root = "https://" + uri.Host + "/" + segments[0] + "/";
            string page = root;
            for (int i = 0; i < 3; i++)
            {
                string html = GetText(page);
                if (html == null)
                    return null;

                Match path = Regex.Match(html, @"pathManifestazione\s*=\s*[""']([^""']*)[""']");
                if (path.Success)
                {
                    Match title = Regex.Match(html, @"<title>\s*([^<]*?)\s*</title>", RegexOptions.IgnoreCase);
                    return new SwimMicroplusCompetition
                    {
                        Site = root,
                        Files = root + "export/" + path.Groups[1].Value.Trim('/') + (path.Groups[1].Value.Trim('/').Length > 0 ? "/" : "") + "NU/",
                        Name = title.Success ? WebUtility.HtmlDecode(title.Groups[1].Value) : segments[0]
                    };
                }

                Match next = Regex.Match(html, @"location(?:\.href)?\s*=\s*[""']([^""']+)[""']");
                if (!next.Success)
                    return null;
                page = new Uri(new Uri(page), next.Groups[1].Value).AbsoluteUri;
            }

            return null;
        }

        /// <summary>
        /// The rounds of the events of a Microplus competition with their results published (one unit per round: its results file
        /// has all the heats, each swimmer with his heat), without their results.
        /// </summary>
        public static List<SwimOfficialUnit> MicroplusSchedule(SwimMicroplusCompetition c)
        {
            List<SwimOfficialUnit> units = new List<SwimOfficialUnit>();
            JObject counters = c == null ? null : GetJson(c.Files + "Contatori.json");
            JArray files = counters == null ? null : counters["contatori"] as JArray;
            if (files == null)
                return units;

            HashSet<string> results = new HashSet<string>(files.Select(f => Str(f["nomefile"])).Where(n => n.Contains("CLAS")), StringComparer.OrdinalIgnoreCase);
            foreach (string category in files.Select(f => Str(f["c0"])).Where(x => x.Length > 0).Distinct())
            {
                JObject schedule = GetJson(c.Files + "ScheduleByEvent_" + category + ".JSON");
                JArray events = schedule == null ? null : schedule["e"] as JArray;
                if (events == null)
                    continue;

                foreach (JToken e in events)
                {
                    string ev = Str(e["c1"]), round = Str(e["c2"]), part = Str(e["c3"]);
                    string file = "NU" + category + ev + "CLAS" + (round.Length == 3 ? round.Substring(1) : round) + " " + part + ".JSON";
                    if (!results.Contains(file))
                        continue;

                    SwimOfficialUnit u = new SwimOfficialUnit
                    {
                        Source = "microplus",
                        CompetitionCode = c.Files,
                        UnitCode = file,
                        Event = Str(e["d_en"]),
                        Category = Str(e["d3_en"]),
                        Phase = Str(e["d1_en"]),
                        Round = Round("", Str(e["d1_en"])),
                        Gender = CategoryGender(Str(e["d3_en"]), category),
                        Date = DayMonthYear(Str(e["gi"]))
                    };
                    ParseEvent(u.Event, out u.Distance, out u.Style, out u.Relay);
                    if (u.Distance > 0)
                        units.Add(u);
                }
            }

            return units;
        }

        /// <summary>The results of a round of a Microplus competition (its entries, each with its heat; false when not read).</summary>
        public static bool MicroplusResults(SwimOfficialUnit unit)
        {
            JObject o = GetJson(unit.CompetitionCode + Uri.EscapeDataString(unit.UnitCode));
            JArray data = o == null ? null : o["data"] as JArray;
            if (data == null)
                return false;

            // The columns of the times: "R.T.", "50m", "100m"…, "FINISH".
            List<string> columns = new List<string>();
            JArray details = o.SelectToken("ExtraDetails.Eng") as JArray;
            if (details != null)
                columns.AddRange(details.Select(d => Str(d["Field"]).ToUpperInvariant()));

            unit.Entries.Clear();
            foreach (JToken d in data)
            {
                SwimOfficialEntry e = new SwimOfficialEntry
                {
                    Heat = Int(d["b"]),
                    Lane = Int(d["PlaLane"]),
                    Code = Str(d["PlaCod"]),
                    Nation = Str(d["PlaNat"]),
                    BirthYear = Int(d["PlaBirth"]),
                    Club = Str(d["TeamDescrIta"]).Length > 0 ? Str(d["TeamDescrIta"]) : Str(d["PlaTeamCod"]),
                    Gender = unit.Gender == "X" ? "" : unit.Gender,
                    Time = Time(Str(d["MemPrest"]))
                };
                if (!e.Time.HasValue)
                    e.Status = Str(d["MemPrest"]);

                // The names as published: in Greek letters, or in Latin ones.
                string family = Str(d["PlaSurname"]), given = Str(d["PlaName"]);
                if (SwimNameMatch.HasGreek(family + given))
                {
                    e.FamilyName = family;
                    e.GivenName = given;
                }
                else
                {
                    e.FamilyNameLatin = family;
                    e.GivenNameLatin = given;
                }

                SortedDictionary<double, double> at = new SortedDictionary<double, double>();
                JArray fields = d["MemFields"] as JArray;
                for (int i = 0; fields != null && i < fields.Count && i < columns.Count; i++)
                {
                    double? value = Time(Str(fields[i]["V"]));
                    if (!value.HasValue)
                        continue;

                    Match m = Regex.Match(columns[i], @"^(\d+)\s*M$");
                    if (columns[i].Replace(".", "").Replace(" ", "") == "RT")
                        e.Reaction = value;
                    else if (m.Success)
                        at[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)] = value.Value;
                }

                e.Splits = SplitsEvery50(at, unit.Distance);
                unit.Entries.Add(e);
            }

            return true;
        }

        /// <summary>"M", "F", "X" of a category of Microplus ("WOMEN - GENERAL", "MEN - JUNIORS", its code "ASF").</summary>
        private static string CategoryGender(string name, string code)
        {
            string s = (name ?? "").ToUpperInvariant();
            if (s.Contains("MIX"))
                return "X";
            if (s.Contains("WOMEN") || s.Contains("GIRL") || s.Contains("FEMALE") || s.Contains("ΓΥΝΑΙΚ") || s.Contains("ΚΟΡΑΣ"))
                return "F";
            if (s.Contains("MEN") || s.Contains("BOY") || s.Contains("MALE") || s.Contains("ΑΝΔΡ") || s.Contains("ΑΓΟΡ"))
                return "M";
            string c = (code ?? "").ToUpperInvariant();
            return c.EndsWith("F", StringComparison.Ordinal) ? "F" : c.EndsWith("M", StringComparison.Ordinal) ? "M" : "";
        }

        /// <summary>"26/04/2024".</summary>
        private static DateTime? DayMonthYear(string text)
        {
            DateTime d;
            return DateTime.TryParseExact((text ?? "").Trim(), new[] { "d/M/yyyy", "dd/MM/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
                ? d : (DateTime?)null;
        }

        /// <summary>A GET of a page (null when it fails; the reason in the log). TLS 1.2, 20 s.</summary>
        private static string GetText(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 20000;
                request.ReadWriteTimeout = 20000;
                request.UserAgent = "Kinovea-SwimAnalysis";
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception e)
            {
                log.Error("Could not read " + url + ".", e);
                return null;
            }
        }

        /// <summary>A GET of JSON (null when it fails; the reason in the log). TLS 1.2, 20 s.</summary>
        private static JObject GetJson(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Accept = "application/json";
                request.Timeout = 20000;
                request.ReadWriteTimeout = 20000;
                request.UserAgent = "Kinovea-SwimAnalysis";
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return JObject.Parse(reader.ReadToEnd());
                }
            }
            catch (Exception e)
            {
                log.Error("Could not read the official results at " + url + ".", e);
                return null;
            }
        }
    }
}
