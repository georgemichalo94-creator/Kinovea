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
using System.Text;
using System.Text.RegularExpressions;

namespace Kinovea.ScreenManager
{
    /// <summary>One swimmer read from results text pasted by the analyst (since 2.8.1).</summary>
    public class SwimPasteRecord
    {
        /// <summary>The rank (0: not given or none, a disqualification) and the lane (-1: not given; 0 is a lane of ten-lane pools).</summary>
        public int Rank;
        public int Lane = -1;

        /// <summary>The names as written (Latin or Greek letters), the country (IOC code), the club, the year of birth (0: unknown).</summary>
        public string FamilyName = "";
        public string GivenName = "";
        public string Nation = "";
        public string Club = "";
        public int BirthYear;

        /// <summary>The reaction time, the final time, the cumulative times at every 50 m before the finish (s).</summary>
        public double? Reaction;
        public double? Time;
        public List<double> Splits = new List<double>();

        /// <summary>
        /// Times of the row that could each be the final one (an entry time beside it, a few seconds apart): none is taken, the analyst
        /// removes the other from the text.
        /// </summary>
        public List<double> TimeChoices = new List<double>();

        /// <summary>Not a time (DSQ, DNS, DNF…) as written.</summary>
        public string Status = "";

        /// <summary>The text of the swimmer's part of what was pasted (one line).</summary>
        public string Text = "";

        /// <summary>The splits could not all be read (only the final time goes in).</summary>
        public bool SplitsMissing;

        public string Name
        {
            get { return (GivenName + " " + FamilyName).Trim(); }
        }

        public override string ToString()
        {
            List<string> parts = new List<string>();
            if (Lane >= 0)
                parts.Add("L" + Lane.ToString(CultureInfo.InvariantCulture));
            parts.Add(Name.Length > 0 ? Name : "?");
            if (Nation.Length > 0)
                parts.Add(Nation);
            parts.Add(Time.HasValue ? SwimResultsPaste.FormatTime(Time.Value) : Status.Length > 0 ? Status : "–");
            return string.Join(" · ", parts.ToArray());
        }
    }

    /// <summary>
    /// The event of a race read from its results (since 2.8.2): from a results link that names it (".../athletic-result/SWM/ST/M/
    /// 100MBA.../SFNL/..."), the title of pasted results ("Men's 100m Backstroke Semifinal") or the results page's title (the
    /// competition and its city). What is not known stays empty (0).
    /// </summary>
    public class SwimEventInfo
    {
        /// <summary>The whole distance (a relay: legs x leg), the relay, the style ("fs", "bk", "br", "fly", "im").</summary>
        public int Distance;
        public bool Relay;
        public string Style = "";

        /// <summary>"M", "F", "X" (mixed); the round: "heats", "semi", "final".</summary>
        public string Gender = "";
        public string Round = "";

        /// <summary>The competition (the results page's title), its city, the pool of a short course competition (25).</summary>
        public string Competition = "";
        public string Location = "";
        public int Pool;

        public bool Any
        {
            get { return Distance > 0 || Style.Length > 0 || Gender.Length > 0 || Round.Length > 0 || Competition.Length > 0 || Location.Length > 0 || Pool > 0; }
        }

        /// <summary>The event itself (distance and style) is known.</summary>
        public bool HasEvent
        {
            get { return Distance > 0 && Style.Length > 0; }
        }
    }

    /// <summary>What was read from the text: the swimmers, the one of the race and why him.</summary>
    public class SwimPasteResult
    {
        public List<SwimPasteRecord> Records = new List<SwimPasteRecord>();

        /// <summary>The record of the race's swimmer (by his name, his lane, or the only one); −1 when the analyst chooses.</summary>
        public int Best = -1;

        /// <summary>"name", "lane", "only" (how Best was chosen), "" when not.</summary>
        public string Why = "";
    }

    /// <summary>
    /// The official results of a race from text copied from any results page (a site without an open source of results, a PDF): the
    /// row of the swimmer (rank, lane, name, country, year, club, reaction time, final time) and his splits (by distance, "50m 27.48",
    /// or in order, cumulative or by length) read in the usual layouts (since 2.8.1). Nothing is fetched: the analyst copies what he
    /// sees and checks what was read before it goes in.
    /// </summary>
    public static class SwimResultsPaste
    {
        /// <summary>A swimmer whose name is this unlike the race's athlete's is another one (NameScore).</summary>
        public const double OtherName = 0.5;

        /// <summary>The shortest time of a race (s): shorter ones are reaction times, gaps, tenths.</summary>
        private const double MinRaceTime = 9.0;

        private enum Kind { Word, Nation, Int, Time, Distance, Status, Paren, Dash }

        private class Token
        {
            public Kind Kind;
            public string Text;
            public double Value;

            /// <summary>A time with a sign: the gap to the winner (+0.35), not a time of the swimmer.</summary>
            public bool Signed;

            /// <summary>A country written in parentheses or onto the name ("(ITA)", "ITAMarco"): the country, not a name.</summary>
            public bool Sure;

            /// <summary>The first word of its line.</summary>
            public bool LineStart;
        }

        private struct Start
        {
            public int At;
            public bool Lane;
        }

        /// <summary>The countries of the results (IOC codes, the neutral and refugee teams).</summary>
        private static readonly HashSet<string> Nations = new HashSet<string>(
            ("AFG ALB ALG AND ANG ANT ARG ARM ARU ASA AUS AUT AZE BAH BAN BAR BDI BEL BEN BER BHU BIH BIZ BLR BOL BOT BRA BRN BRU BUL BUR " +
             "CAF CAM CAN CAY CGO CHA CHI CHN CIV CMR COD COK COL COM CPV CRC CRO CUB CYP CZE DEN DJI DMA DOM ECU EGY ERI ESA ESP EST ETH " +
             "FIJ FIN FRA FSM GAB GAM GBR GBS GEO GEQ GER GHA GRE GRN GUA GUI GUM GUY HAI HKG HON HUN INA IND IRI IRL IRQ ISL ISR ISV ITA " +
             "IVB JAM JOR JPN KAZ KEN KGZ KIR KOR KOS KSA KUW LAO LAT LBA LBN LBR LCA LES LIE LTU LUX MAD MAR MAS MAW MDA MDV MEX MGL MHL " +
             "MKD MLI MLT MNE MON MOZ MRI MTN MYA NAM NCA NED NEP NGR NIG NOR NRU NZL OMA PAK PAN PAR PER PHI PLE PLW PNG POL POR PRK PUR " +
             "QAT ROU RSA RUS RWA SAM SEN SEY SGP SKN SLE SLO SMR SOL SOM SRB SRI SSD STP SUD SUI SUR SVK SWE SWZ SYR TAN TGA THA TJK TKM " +
             "TLS TOG TPE TTO TUN TUR TUV UAE UGA UKR URU USA UZB VAN VEN VIE VIN YEM ZAM ZIM NAB AIN ROC EOR IOA FAR MAC GIB NCL TAH")
            .Split(' '));

        /// <summary>
        /// Words of the column titles, the events and the pages (English, Greek, a few other languages), and of the icons copied with a
        /// table: never a name. Compared in lower case without accents.
        /// </summary>
        private static readonly string[] HeaderWords =
        {
            "rank", "lane", "ctry", "country", "nation", "nat", "noc", "name", "athlete", "athletes", "swimmer", "time", "gap", "distance",
            "dist", "splits", "split", "rk", "ln", "rt", "r.t.", "r.t", "reaction", "tb", "behind", "pts", "points", "yob", "yb", "born", "year",
            "club", "team", "pos", "place", "result", "results", "official", "unofficial", "heat", "heats", "final", "finals", "semifinal",
            "semifinals", "summary", "men's", "women's", "men", "women", "boys", "girls", "mixed", "freestyle", "backstroke",
            "breaststroke", "butterfly", "medley", "individual", "relay", "of", "the", "and", "by", "powered", "scroll", "to", "top",
            "start", "list", "records", "reports", "entries", "q", "qso", "so", "wr", "er", "cr", "nr", "or", "wj", "ej", "ejr", "wjr",
            "event", "seed", "prelims", "age", "entry", "pb", "sb",
            "θεση", "διαδρ", "διαδρομος", "ονοματεπωνυμο", "ονομα", "επωνυμο", "ετος", "γεννησης", "γεν", "ετ", "ετ.γεν", "σωματειο",
            "συλλογος", "ομαδα", "χρονος", "αντιδραση", "βαθμοι", "ενδιαμεσοι", "χωρα", "κατ", "κατηγορια", "ελευθερο", "υπτιο",
            "προσθιο", "πεταλουδα", "μικτη", "ατομικη", "ανδρων", "γυναικων", "αγοριων", "κοριτσιων", "κορασιδων", "παιδων", "νεανιδων",
            "εφηβων", "τελικος", "τελικοι", "προκριματικος", "προκριματικοι", "ημιτελικος", "σειρα", "σειρες", "αποτελεσματα",
            "αγωνισμα", "καταταξη",
            "nom", "prenom", "temps", "rang", "couloir", "nazione", "corsia", "tempo", "nome", "societa", "anno", "zeit", "jahrgang",
            "verein", "platz", "tiempo", "nombre", "nacion", "equipo", "pais"
        };

        private static readonly HashSet<string> Headers = new HashSet<string>(HeaderWords.Select(Plain));
        private static readonly List<string> HeaderList = Headers.Where(h => h.Length > 1).OrderByDescending(h => h.Length).ToList();

        private static readonly string[] IconWords =
        {
            "add_circle_outline", "remove_circle_outline", "add_circle", "remove_circle", "keyboard_arrow_up", "keyboard_arrow_down",
            "expand_more", "expand_less", "arrow_drop_down", "arrow_drop_up", "chevron_right", "chevron_left", "arrow_forward", "arrow_back"
        };

        private static readonly string[] StatusWords = { "DSQ", "DNS", "DNF", "DQ", "DNC", "DISQ", "ΑΚΥΡ", "ΑΚ", "ΔΕΝ" };

        /// <summary>
        /// Reads the swimmers of the text and finds the race's one: by the name of the race's athlete (Latin or Greek letters, either
        /// order, an initial for the given name), else by the race's lane (−1/0: not known… 0 counts only when written) when the
        /// swimmer there is not plainly another one, else the only swimmer.
        /// </summary>
        public static SwimPasteResult Parse(string text, int distance, string athlete, int lane)
        {
            SwimPasteResult result = new SwimPasteResult();
            if (string.IsNullOrWhiteSpace(text) || distance <= 0)
                return result;

            // The printouts of Hy-Tek Meet Manager (results and heat sheets of many international junior meets) have their own reader.
            SwimPasteResult hyTek = ParseHyTek(text, distance);
            if (hyTek != null)
            {
                Choose(hyTek, athlete, lane);
                return hyTek;
            }

            List<Token> tokens = Tokenize(text);
            List<Start> starts = RecordStarts(tokens);
            if (starts.Count == 0)
            {
                // No ranks: one swimmer, or one per name that starts a line after the times of the one before.
                int from = 0;
                bool timed = false;
                for (int k = 0; k <= tokens.Count; k++)
                {
                    if (k == tokens.Count || (timed && tokens[k].LineStart && tokens[k].Kind == Kind.Word))
                    {
                        SwimPasteRecord r = k > from ? ReadRecord(tokens, from, k, false, false, distance) : null;
                        if (r != null)
                            result.Records.Add(r);
                        from = k;
                        timed = false;
                    }

                    if (k < tokens.Count && tokens[k].Kind == Kind.Time && !tokens[k].Signed && tokens[k].Value >= MinRaceTime)
                        timed = true;
                }
            }
            else
            {
                for (int i = 0; i < starts.Count; i++)
                {
                    int to = i + 1 < starts.Count ? starts[i + 1].At : tokens.Count;
                    SwimPasteRecord r = ReadRecord(tokens, starts[i].At, to, true, starts[i].Lane, distance);
                    if (r != null)
                        result.Records.Add(r);
                }
            }

            Choose(result, athlete, lane);
            return result;
        }

        // A time of Hy-Tek: "51.55", "1:54.85", an exhibition swim with its "X" ("X52.36"); not a time: "NT", "DQ", "NS", "SCR"…
        private const string HyTime = @"X?(?:[0-9]{1,2}:)?[0-9]{1,2}\.[0-9]{2}";
        private const string HyStatus = @"X?(?:DQ|DSQ|NS|DNS|DNF|SCR|DFS|NT)";

        // A row of the results: the place ("8", "*2" a tie, "---" none), the competitor's number, "Surname, Given", the age (or the year), the team
        // (a country or a club, several words), the entry time, the final time or a status, the points.
        private static readonly Regex HyResultRow = new Regex(
            @"^\s*(?<rank>\*?[0-9]{1,3}|-{2,3})\s+(?:(?<no>[0-9]{1,5})\s+)?(?<family>[^,\d\t]+?),\s*(?<given>[^,\d\t]+?)\s+(?<age>[0-9]{1,2}|(?:19|20)[0-9]{2})\s+" +
            @"(?<team>[^\d\t]*?\p{L}[^\d\t]*?)\s+(?:(?<seed>" + HyTime + "|" + HyStatus + @")\s+)?(?<time>" + HyTime + "|" + HyStatus +
            @")(?:\s+[0-9]{1,3}(?:\.[0-9])?)?\s*$", RegexOptions.IgnoreCase);

        // A row of a heat sheet: the lane, "Surname, Given", the age, the team, the entry time (no result yet).
        private static readonly Regex HyStartRow = new Regex(
            @"^\s*(?<lane>[0-9]{1,2})\s+(?<family>[^,\d\t]+?),\s*(?<given>[^,\d\t]+?)\s+(?<age>[0-9]{1,2}|(?:19|20)[0-9]{2})\s+(?<team>[^\d\t]*?\p{L}[^\d\t]*?)\s+" +
            @"(?<seed>" + HyTime + "|" + HyStatus + @")\s*$", RegexOptions.IgnoreCase);

        // The splits under a row: the reaction time ("r:+0.65") and the cumulative times, each after the first with the length's own
        // time in parentheses ("24.73   51.55 (26.82)"); a long race goes on over more lines ("3:19.90 (34.58)  3:54.23 (34.33)").
        private static readonly Regex HySplitRow = new Regex(
            @"^\s*(?:r:\s*[+-]?[0-9]\.[0-9]{2}\s+)?(?:[0-9]{1,2}:)?[0-9]{1,2}\.[0-9]{2}(?:\s*\((?:[0-9]{1,2}:)?[0-9]{1,2}\.[0-9]{2}\))?" +
            @"(?:\s+(?:[0-9]{1,2}:)?[0-9]{1,2}\.[0-9]{2}(?:\s*\((?:[0-9]{1,2}:)?[0-9]{1,2}\.[0-9]{2}\))?)*\s*$");

        // The countries of the teams written in full (English) -> IOC code.
        private static readonly Dictionary<string, string> CountryNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Greece", "GRE" }, { "Cyprus", "CYP" }, { "Croatia", "CRO" }, { "Austria", "AUT" }, { "Slovenia", "SLO" }, { "Poland", "POL" },
            { "Portugal", "POR" }, { "Israel", "ISR" }, { "Turkey", "TUR" }, { "Türkiye", "TUR" }, { "Turkiye", "TUR" }, { "Ukraine", "UKR" },
            { "Czech Republic", "CZE" }, { "Czechia", "CZE" }, { "Slovakia", "SVK" }, { "Serbia", "SRB" }, { "Bulgaria", "BUL" },
            { "Romania", "ROU" }, { "Hungary", "HUN" }, { "Italy", "ITA" }, { "Spain", "ESP" }, { "France", "FRA" }, { "Germany", "GER" },
            { "Great Britain", "GBR" }, { "Switzerland", "SUI" }, { "Belgium", "BEL" }, { "Netherlands", "NED" }, { "Lithuania", "LTU" },
            { "Latvia", "LAT" }, { "Estonia", "EST" }, { "Finland", "FIN" }, { "Sweden", "SWE" }, { "Norway", "NOR" }, { "Denmark", "DEN" },
            { "Iceland", "ISL" }, { "Ireland", "IRL" }, { "Luxembourg", "LUX" }, { "Malta", "MLT" }, { "Montenegro", "MNE" },
            { "North Macedonia", "MKD" }, { "Albania", "ALB" }, { "Bosnia and Herzegovina", "BIH" }, { "Kosovo", "KOS" },
            { "Georgia", "GEO" }, { "Armenia", "ARM" }, { "Azerbaijan", "AZE" }, { "Moldova", "MDA" }, { "Egypt", "EGY" },
            { "Lebanon", "LBN" }, { "Jordan", "JOR" }, { "Kazakhstan", "KAZ" }, { "Liechtenstein", "LIE" }, { "Monaco", "MON" },
            { "Andorra", "AND" }, { "San Marino", "SMR" }, { "Faroe Islands", "FAR" }, { "Gibraltar", "GIB" }, { "Tunisia", "TUN" },
            { "Algeria", "ALG" }, { "Morocco", "MAR" }, { "USA", "USA" }, { "United States", "USA" }, { "Canada", "CAN" }
        };

        /// <summary>
        /// The text of a Hy-Tek Meet Manager printout (copied from its PDF or page; since 2.9.2): results rows "8  144 Surname, Given
        /// 18 Greece  51.55  1" with the splits on the next line ("24.73  51.55 (26.82)"), or heat sheet rows "3 Surname, Given 18 Greece
        /// X51.77" (the lane, no time yet). An exhibition swim ("---", "X52.36") is a time like the others. Null when the text is not of
        /// this layout (the other readers then).
        /// </summary>
        private static SwimPasteResult ParseHyTek(string text, int distance)
        {
            string[] lines = Normalize(text).Split('\n');

            // A heat sheet ("Heat 4 of 6", a "Lane" column): its rows are lanes and entry times, not places and results.
            bool sheet = lines.Any(l => Regex.IsMatch(l, @"^\s*Heat\s+[0-9]+\s+of\s+[0-9]+", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(l, @"^\s*Lane\s+Name\b", RegexOptions.IgnoreCase));

            // The column names of the results ("Seed Time  Finals Time"): of two times in a row the second is the result.
            bool seedFirst = lines.Any(l => Regex.IsMatch(l, @"\bseed\s+time\b.*\bfinals?\s+time\b", RegexOptions.IgnoreCase));
            SwimPasteResult result = new SwimPasteResult();
            SwimPasteRecord last = null;
            List<double> cumulative = new List<double>();
            foreach (string raw in lines.Concat(new[] { "" }))
            {
                string line = raw.TrimEnd();
                bool splitLine = last != null && last.Time.HasValue && line.Trim().Length > 0 && HySplitRow.IsMatch(line);
                if (splitLine)
                {
                    Match rt = Regex.Match(line, @"r:\s*([+-]?[0-9]\.[0-9]{2})");
                    if (rt.Success)
                    {
                        double reaction = double.Parse(rt.Groups[1].Value, CultureInfo.InvariantCulture);
                        if (reaction > 0 && reaction < 1.5)
                            last.Reaction = reaction;
                    }

                    string body = Regex.Replace(line, @"r:\s*[+-]?[0-9]\.[0-9]{2}", " ");
                    body = Regex.Replace(body, @"\([^)]*\)", " ");
                    foreach (Match tm in Regex.Matches(body, @"(?:[0-9]{1,2}:)?[0-9]{1,2}\.[0-9]{2}"))
                    {
                        double v = ParseTime(tm.Value);
                        if (!double.IsNaN(v))
                            cumulative.Add(v);
                    }

                    continue;
                }

                // The splits of the row before end here (a long race prints them on several lines).
                if (last != null && cumulative.Count > 0)
                    SetHyTekSplits(last, cumulative, distance);
                cumulative.Clear();
                if (line.Trim().Length == 0)
                    continue;

                Match m = sheet ? HyStartRow.Match(line) : HyResultRow.Match(line);
                if (!m.Success)
                {
                    // Titles, column names, "Heat 4 of 6", "====", page heads: a row's splits come right after it.
                    if (!Regex.IsMatch(line, @"^\s*(=+|-+)\s*$"))
                        last = null;
                    continue;
                }

                SwimPasteRecord r = new SwimPasteRecord { Text = line.Trim() };
                if (m.Groups["age"].Value.Length == 4)
                    r.BirthYear = int.Parse(m.Groups["age"].Value, CultureInfo.InvariantCulture);
                r.FamilyName = Regex.Replace(m.Groups["family"].Value.Trim(), @"\s+", " ");
                r.GivenName = Regex.Replace(m.Groups["given"].Value.Trim(), @"\s+", " ");
                string team = Regex.Replace(m.Groups["team"].Value.Trim(), @"\s+", " ");
                string code;
                if (CountryNames.TryGetValue(team, out code))
                    r.Nation = code;
                else if (team.Length == 3 && Nations.Contains(team.ToUpperInvariant()))
                    r.Nation = team.ToUpperInvariant();
                else
                    r.Club = team;

                if (sheet)
                {
                    r.Lane = int.Parse(m.Groups["lane"].Value, CultureInfo.InvariantCulture);
                }
                else
                {
                    int place;
                    r.Rank = int.TryParse(m.Groups["rank"].Value.TrimStart('*'), NumberStyles.Integer, CultureInfo.InvariantCulture, out place) ? place : 0;
                    string time = m.Groups["time"].Value.TrimStart('X', 'x');
                    double t = ParseTime(time);
                    double seed = m.Groups["seed"].Success ? ParseTime(m.Groups["seed"].Value.TrimStart('X', 'x')) : double.NaN;
                    if (!double.IsNaN(t) && t >= MinRaceTime && !double.IsNaN(seed) && !seedFirst)
                    {
                        // Two times and no column names saying which is the entry time: none taken (as the other layouts).
                        r.TimeChoices.Add(t);
                        r.TimeChoices.Add(seed);
                    }
                    else if (!double.IsNaN(t) && t >= MinRaceTime)
                        r.Time = t;
                    else if (double.IsNaN(t) && !Regex.IsMatch(time, "^NT$", RegexOptions.IgnoreCase))
                        r.Status = time.ToUpperInvariant();
                }

                result.Records.Add(r);
                last = r;
            }

            return result.Records.Count > 0 ? result : null;
        }

        /// <summary>
        /// The splits of a Hy-Tek row from its cumulative times (the last one the final time): one every 50 m before the finish (a 25 m
        /// pool prints every 25 m: the 50 m ones kept). Not all of them: none, and SplitsMissing (the final time alone goes in).
        /// </summary>
        private static void SetHyTekSplits(SwimPasteRecord r, List<double> cumulative, int distance)
        {
            List<double> c = new List<double>(cumulative);
            bool rising = c.Zip(c.Skip(1), (a, b) => b > a).All(x => x);
            if (rising && c.Count > 0 && Math.Abs(c[c.Count - 1] - r.Time.Value) < 0.005)
                c.RemoveAt(c.Count - 1);
            int every = c.Count > 0 && distance % (c.Count + 1) == 0 && distance / (c.Count + 1) == 25 ? 2 : 1;
            List<double> splits = c.Where((v, i) => (i + 1) % every == 0).ToList();
            if (rising && distance >= 100 && splits.Count == distance / 50 - 1 && splits.All(v => v < r.Time.Value))
                r.Splits = splits;
            else if (distance >= 100)
                r.SplitsMissing = true;
        }

        /// <summary>The text in tokens: icons and column titles out, the country written onto the name split ("ITAMarco").</summary>
        private static List<Token> Tokenize(string text)
        {
            string s = Normalize(text);
            foreach (string icon in IconWords)
                s = Regex.Replace(s, Regex.Escape(icon), " ", RegexOptions.IgnoreCase);

            // A tie of the rank ("=2", "2=", "*2", "T2") is the rank.
            s = Regex.Replace(s, @"(?<![\p{L}0-9.,:+\-])(?:[=*]|T)([0-9]{1,3})(?![0-9.,:\p{L}])", "$1");
            s = Regex.Replace(s, @"(?<![\p{L}0-9.,:+\-])([0-9]{1,3})[=*T](?![0-9.,:\p{L}])", "$1");

            // A letter written onto a number ("Gap1": a column title onto the rank).
            s = Regex.Replace(s, @"(?<=\p{L})(?=[0-9])", " ");

            // The country onto the given name ("ITAMarco", "HUNÁdám": the tables of some sites), kept as the sure country.
            s = Regex.Replace(s, @"\b([A-Z]{3})(?=\p{Lu}\p{Ll})", m => Nations.Contains(m.Groups[1].Value) ? "(" + m.Groups[1].Value + ") " : m.Value);

            // A unit apart ("50 m") onto its distance (25, 50, 75…; not an initial: "10 M. ROSSI"); parentheses with spaces inside closed up.
            s = Regex.Replace(s, @"\b([0-9]{2,4})[ \t]+(m|μ|M|Μ)\b(?!\p{L})(?!\.[ \t]*\p{Lu})", m =>
            {
                int d = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                return d >= 25 && d % 25 == 0 ? m.Groups[1].Value + m.Groups[2].Value : m.Value;
            });
            s = Regex.Replace(s, @"\([ \t]+", "(");
            s = Regex.Replace(s, @"[ \t]+\)", ")");

            List<Token> tokens = new List<Token>();
            foreach (string line in s.Split('\n'))
            {
                bool first = true;
                foreach (string raw in Regex.Split(line, @"[\s\|;]+"))
                {
                    string t = raw.Trim().Trim(',');
                    if (t.Length == 0)
                        continue;
                    Token token = Classify(t);
                    if (token != null)
                    {
                        token.LineStart = first;
                        tokens.Add(token);
                    }

                    first = false;
                }
            }

            return tokens;
        }

        /// <summary>
        /// The text with its digits in ASCII (full-width, Arabic-Indic…), full-width punctuation as usual, the spaces as spaces and the
        /// line ends as "\n".
        /// </summary>
        private static string Normalize(string text)
        {
            StringBuilder b = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '0' && c <= '9')
                {
                    b.Append(c);
                    continue;
                }

                if (char.IsDigit(c))
                {
                    double v = char.GetNumericValue(c);
                    b.Append(v >= 0 && v <= 9 ? (char)('0' + (int)v) : ' ');
                    continue;
                }

                switch (c)
                {
                    case '\r':
                        if (i + 1 >= text.Length || text[i + 1] != '\n')
                            b.Append('\n');
                        break;
                    case '\n':
                    case '\u2028':
                    case '\u2029':
                        b.Append('\n');
                        break;
                    case '\uFF0E':
                    case '\u066B':
                        b.Append('.');
                        break;
                    case '\uFF0C':
                        b.Append(',');
                        break;
                    case '\uFF1A':
                        b.Append(':');
                        break;
                    default:
                        b.Append(char.IsWhiteSpace(c) && c != '\t' ? ' ' : c);
                        break;
                }
            }

            return b.ToString();
        }

        private static Token Classify(string t)
        {
            string inner = t.Trim('(', ')');
            if (inner.Length == 3 && inner != t && Nations.Contains(inner))
                return new Token { Kind = Kind.Nation, Text = inner, Sure = true };

            if (t.StartsWith("(", StringComparison.Ordinal) || t.EndsWith(")", StringComparison.Ordinal))
            {
                // "(27.48)": a time by length; "(+2)", "(=)": a change of rank.
                Match pt = Regex.Match(t, @"^\(?([0-9]{1,2}:)?[0-9]{1,3}[.,][0-9]{1,2}\)?$");
                return new Token { Kind = Kind.Paren, Text = t, Value = pt.Success ? ParseTime(t.Trim('(', ')')) : double.NaN };
            }

            if (t == "-" || t == "–" || t == "—" || t == "=")
                return new Token { Kind = Kind.Dash, Text = t };

            string upper = t.ToUpperInvariant().TrimEnd('.');
            if (StatusWords.Contains(upper))
                return new Token { Kind = Kind.Status, Text = upper };

            Match dm = Regex.Match(t, @"^([0-9]{2,4})(m|μ|M|Μ)\.?:?$");
            if (dm.Success)
            {
                int d = int.Parse(dm.Groups[1].Value, CultureInfo.InvariantCulture);
                return d >= 25 && d % 25 == 0 ? new Token { Kind = Kind.Distance, Text = t, Value = d } : null;
            }

            Match tm = Regex.Match(t, @"^([+\-])?((?:[0-9]{1,2}:)?[0-9]{1,3}[.,][0-9]{1,2})([A-Za-z=*]{0,4})$");
            if (tm.Success)
            {
                double v = ParseTime(tm.Groups[2].Value);
                if (!double.IsNaN(v))
                    return new Token { Kind = Kind.Time, Text = t, Value = v, Signed = tm.Groups[1].Success };
            }

            Match im = Regex.Match(t, @"^([0-9]{1,4})\.?$");
            if (im.Success)
                return new Token { Kind = Kind.Int, Text = t, Value = int.Parse(im.Groups[1].Value, CultureInfo.InvariantCulture) };

            if (t.Length == 3 && Nations.Contains(t))
                return new Token { Kind = Kind.Nation, Text = t };

            // A word of letters (names, clubs: "N.", "RAMSDEN-POOLE", "D'ANGELO", "Ν.Ο."); column titles and icons are left out.
            if (Regex.IsMatch(t, @"^[\p{L}][\p{L}\.\-'’]*$"))
            {
                if (IsHeader(t))
                    return null;
                return new Token { Kind = Kind.Word, Text = t };
            }

            return null;
        }

        /// <summary>A column title (or several written together: "RankLaneCtryNameTimeGap").</summary>
        private static bool IsHeader(string t)
        {
            string full = Plain(t);
            string s = full.TrimEnd('.', ':');
            if (Headers.Contains(full) || Headers.Contains(s))
                return true;

            // Written together: every part a title, three parts or more, or two of three letters or more (not "And-or").
            List<int> parts = new List<int>();
            string rest = s;
            while (rest.Length > 0)
            {
                string w = HeaderList.FirstOrDefault(h => rest.StartsWith(h, StringComparison.Ordinal));
                if (w == null)
                    return false;
                parts.Add(w.Length);
                rest = rest.Substring(w.Length);
            }

            return t.Length > 4 && (parts.Count >= 3 || (parts.Count == 2 && parts.All(n => n >= 3)));
        }

        /// <summary>Lower case without accents (ό: ο, final ς: σ), for the titles.</summary>
        private static string Plain(string text)
        {
            string d = (text ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder b = new StringBuilder(d.Length);
            foreach (char c in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;
                b.Append(c == 'ς' ? 'σ' : c);
            }

            return b.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Where the swimmers' rows start: a rank (1-999, or "-") then a lane (0-10) then a name or a country; or, at the start of a
        /// line, a rank then the name (tables without lanes). Rows of splits ("50 3 27.48") are not starts: a time follows the number.
        /// </summary>
        private static List<Start> RecordStarts(List<Token> tokens)
        {
            List<Start> starts = new List<Start>();
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                Token a = tokens[i], b = tokens[i + 1];
                bool rank = (a.Kind == Kind.Int && a.Value >= 1 && a.Value <= 999) || a.Kind == Kind.Dash;
                if (!rank)
                    continue;

                bool laneHere = b.Kind == Kind.Int && b.Value >= 0 && b.Value <= 10 && !b.Text.EndsWith(".", StringComparison.Ordinal);
                if (laneHere && i + 2 < tokens.Count && Named(tokens, i + 2))
                {
                    starts.Add(new Start { At = i, Lane = true });
                    i += 2;
                }
                else if (a.LineStart && Named(tokens, i + 1))
                {
                    starts.Add(new Start { At = i, Lane = false });
                    i += 1;
                }
            }

            return starts;
        }

        /// <summary>A name (or a country) at k, or a reaction time then a name.</summary>
        private static bool Named(List<Token> tokens, int k)
        {
            Token c = tokens[k];
            return c.Kind == Kind.Word || c.Kind == Kind.Nation ||
                (c.Kind == Kind.Time && !c.Signed && c.Value >= 0.30 && c.Value <= 1.20 && k + 1 < tokens.Count &&
                 (tokens[k + 1].Kind == Kind.Word || tokens[k + 1].Kind == Kind.Nation));
        }

        /// <summary>One swimmer from his tokens [from, to).</summary>
        private static SwimPasteRecord ReadRecord(List<Token> tokens, int from, int to, bool ranked, bool laned, int distance)
        {
            SwimPasteRecord r = new SwimPasteRecord();
            r.Text = string.Join(" ", tokens.GetRange(from, to - from).Select(t => t.Text).ToArray());
            int i = from;
            if (ranked)
            {
                r.Rank = tokens[i].Kind == Kind.Int ? (int)tokens[i].Value : 0;
                i++;
                if (laned)
                {
                    r.Lane = (int)tokens[i].Value;
                    i++;
                }
            }

            // The country (the other codes before the times are names: "PAN Xiaolong CHN"); the name: the first run of words; then the
            // year of birth and the club (the words after the year or the country, before the times).
            int nation = NationOf(tokens, i, to);
            if (nation >= 0)
                r.Nation = tokens[nation].Text;

            List<string> name = new List<string>(), club = new List<string>();
            int firstNumber = -1;
            bool numberAfterName = false;
            for (int k = i; k < to; k++)
            {
                Token t = tokens[k];
                if (k == nation || (t.Kind == Kind.Nation && t.Sure))
                    continue;

                if (t.Kind == Kind.Status)
                {
                    r.Status = t.Text;
                    continue;
                }

                if (t.Kind == Kind.Word || t.Kind == Kind.Nation)
                {
                    if (numberAfterName)
                        break;
                    if (club.Count > 0 || (name.Count > 0 && (r.BirthYear > 0 || (nation >= 0 && nation < k && WordBefore(tokens, i, nation)))))
                        club.Add(t.Text);
                    else
                        name.Add(t.Text);
                    continue;
                }

                if (t.Kind == Kind.Int && t.Value >= 1930 && t.Value <= DateTime.Now.Year && r.BirthYear == 0 && name.Count > 0)
                {
                    r.BirthYear = (int)t.Value;
                    continue;
                }

                if (t.Kind == Kind.Time || t.Kind == Kind.Distance || t.Kind == Kind.Int)
                {
                    // A number before the name (a reaction time written first) does not end the search for it.
                    if (firstNumber < 0)
                        firstNumber = k;
                    if (name.Count > 0)
                    {
                        numberAfterName = true;
                        break;
                    }
                }
            }

            if (name.Count == 0 && firstNumber < 0 && r.Status.Length == 0)
                return null;
            SplitName(name, out r.FamilyName, out r.GivenName);
            r.Club = string.Join(" ", club.ToArray());

            // The status anywhere in the row; the country after the times when none before them.
            for (int k = i; k < to; k++)
            {
                if (tokens[k].Kind == Kind.Status && r.Status.Length == 0)
                    r.Status = tokens[k].Text;
                if (nation < 0 && tokens[k].Kind == Kind.Nation && r.Nation.Length == 0)
                    r.Nation = tokens[k].Text;
            }

            ReadTimes(tokens, Math.Max(i, firstNumber < 0 ? to : firstNumber), to, distance, r);
            if (r.Time.HasValue && r.Status.Length > 0 && (r.Status == "DSQ" || r.Status == "DQ" || r.Status == "DISQ"))
                r.Time = null;
            if (!r.Time.HasValue && r.TimeChoices.Count == 0 && r.Status.Length == 0 && name.Count == 0)
                return null;
            return r;
        }

        /// <summary>
        /// The country of a row: the one written in parentheses or onto the name, else the last code before the times (names can be
        /// codes too: PAN, TAN, VAN DEN…, BEN); −1 when none.
        /// </summary>
        private static int NationOf(List<Token> tokens, int from, int to)
        {
            int last = -1;
            for (int k = from; k < to; k++)
            {
                Token t = tokens[k];
                if ((t.Kind == Kind.Time && !t.Signed && t.Value >= MinRaceTime) || t.Kind == Kind.Distance)
                    break;
                if (t.Kind != Kind.Nation)
                    continue;
                if (t.Sure)
                    return k;
                last = k;
            }

            return last;
        }

        /// <summary>A word of the name before the country (the country is written after the name: the words after it are the club).</summary>
        private static bool WordBefore(List<Token> tokens, int from, int nation)
        {
            for (int j = from; j < nation; j++)
            {
                if (tokens[j].Kind == Kind.Word || (tokens[j].Kind == Kind.Nation && !tokens[j].Sure))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The times of a row: the reaction time (0.30-1.20 before the first time of the race), the times by distance ("50m 27.48", "50 3
        /// 27.48"), else the times in order (cumulative, or by length when they add up to the final time); the final time by its distance,
        /// else the longest time that fits the distance when the others are splits of it (a length or more before it).
        /// </summary>
        private static void ReadTimes(List<Token> tokens, int from, int to, int distance, SwimPasteRecord r)
        {
            SortedDictionary<int, double> at = new SortedDictionary<int, double>();
            List<double> free = new List<double>();
            HashSet<int> used = new HashSet<int>();

            for (int k = from; k < to; k++)
            {
                Token t = tokens[k];
                int d = 0;
                if (t.Kind == Kind.Distance)
                    d = (int)t.Value;
                else if (t.Kind == Kind.Int && t.Value >= 25 && t.Value <= distance && (int)t.Value % 25 == 0)
                    d = (int)t.Value;
                if (d <= 0)
                    continue;

                // The first time of the swimmer after the distance (its rank, a change of rank between them; a distance without its
                // unit, "50", only with those: an age, "25 ITA 0.63 27.48", is not one).
                bool bare = t.Kind == Kind.Int;
                for (int j = k + 1; j < Math.Min(to, k + 5); j++)
                {
                    Token u = tokens[j];
                    if (u.Kind == Kind.Distance || (u.Kind == Kind.Int && u.Value >= 25 && (int)u.Value % 25 == 0 && u.Value > 25 && j > k + 1))
                        break;
                    if (u.Kind == Kind.Word || u.Kind == Kind.Nation || u.Kind == Kind.Status)
                        break;
                    if (bare && u.Kind == Kind.Time && (u.Signed || u.Value < Math.Max(MinRaceTime, 0.38 * d) || u.Value > 1.3 * d + 5))
                        break;
                    if (u.Kind == Kind.Time && !u.Signed && u.Value >= Math.Max(MinRaceTime, 0.38 * d) && u.Value <= 1.3 * d + 5 && !used.Contains(j))
                    {
                        if (!at.ContainsKey(d))
                            at[d] = u.Value;
                        used.Add(j);
                        used.Add(k);
                        break;
                    }
                }
            }

            int firstRace = -1;
            for (int k = from; k < to; k++)
            {
                Token t = tokens[k];
                if (t.Kind != Kind.Time || t.Signed)
                    continue;
                if (t.Value >= MinRaceTime)
                {
                    if (firstRace < 0)
                        firstRace = k;
                    if (!used.Contains(k))
                        free.Add(t.Value);
                }
                else if (firstRace < 0 && !r.Reaction.HasValue && t.Value >= 0.30 && t.Value <= 1.20 && !used.Contains(k))
                    r.Reaction = t.Value;
            }

            double lo = 0.40 * distance, hi = 1.25 * distance;
            double final;
            if (at.TryGetValue(distance, out final))
                r.Time = final;
            else
            {
                List<double> fits = free.Where(v => v >= lo && v <= hi).ToList();
                if (fits.Count > 0)
                {
                    // The others splits of it (a length or more before it, 10 s at least); one within 10 s of it (an entry time, a
                    // best time beside it) leaves the choice to the analyst. (2.15.0) Nearer than a 50 m lap can be (0.8 of the mean lap
                    // of the race) it is no split either: an entry time 19.5 s slower than the result of a 1500 m was taken as the final
                    // time. A 50 m race has no split: any other time is a choice.
                    double max = fits.Max();
                    double near = distance > 50 ? Math.Max(10, 0.8 * max * 50.0 / distance) : double.MaxValue;
                    if (fits.Any(v => max - v > 0.005 && max - v < near))
                    {
                        r.TimeChoices = fits.Where(v => max - v < near).Distinct().OrderBy(v => v).ToList();
                        return;
                    }

                    r.Time = max;
                }
            }

            if (!r.Time.HasValue)
                return;

            // The splits every 50 m: by distance, else the times in order before the final time (25 m ones every other; listed after
            // the final time too), else lengths.
            List<double> splits = new List<double>();
            bool ok = true;
            for (int d = 50; d < distance; d += 50)
            {
                double v;
                if (at.TryGetValue(d, out v))
                    splits.Add(v);
                else
                {
                    ok = false;
                    break;
                }
            }

            if (!ok || distance < 100)
            {
                splits.Clear();
                ok = distance <= 50;
                double end = r.Time.Value - 1e-6;
                List<double> before = free.TakeWhile(v => v < end).ToList();
                if (before.Count == 0)
                    before = free.SkipWhile(v => v >= end).TakeWhile(v => v < end).ToList();
                int n50 = distance / 50 - 1, n25 = distance / 25 - 1;
                if (distance > 50 && Increasing(before) && before.Count == n50)
                {
                    splits = before;
                    ok = true;
                }
                else if (distance > 50 && Increasing(before) && before.Count == n25)
                {
                    for (int k = 1; k < before.Count; k += 2)
                        splits.Add(before[k]);
                    ok = true;
                }
                else if (distance > 50)
                {
                    // By length (each a fraction of the final time) adding up to it.
                    List<double> laps = free.Where(v => v < end).ToList();
                    if (laps.Count == n50 + 1 && Math.Abs(laps.Sum() - r.Time.Value) <= 0.03)
                    {
                        double run = 0;
                        for (int k = 0; k < n50; k++)
                        {
                            run += laps[k];
                            splits.Add(Math.Round(run, 2));
                        }

                        ok = true;
                    }
                }
            }

            if (ok && (distance <= 50 || Plausible(splits, r.Time.Value)))
                r.Splits = splits;
            else
            {
                r.Splits = new List<double>();
                r.SplitsMissing = distance > 50;
            }
        }

        private static bool Increasing(List<double> v)
        {
            for (int i = 1; i < v.Count; i++)
            {
                if (v[i] <= v[i - 1])
                    return false;
            }

            return true;
        }

        /// <summary>Each 50 m between 18 and 90 s, before the final time.</summary>
        private static bool Plausible(List<double> splits, double final)
        {
            double prev = 0;
            foreach (double s in splits.Concat(new[] { final }))
            {
                double lap = s - prev;
                if (lap < 18 || lap > 90)
                    return false;
                prev = s;
            }

            return true;
        }

        /// <summary>
        /// The family name: the words in capitals ("Marco ROSSI", "ROSSI Marco"); all in capitals or none: the first word is the
        /// family name when all are capitals (the results publish it first), else the last word.
        /// </summary>
        private static void SplitName(List<string> words, out string family, out string given)
        {
            family = given = "";
            if (words.Count == 0)
                return;
            Func<string, bool> caps = w =>
            {
                string letters = new string(w.Where(char.IsLetter).ToArray());
                return letters.Length >= 2 && letters == letters.ToUpperInvariant();
            };

            List<string> upper = words.Where(caps).ToList();
            if (upper.Count > 0 && upper.Count < words.Count)
            {
                family = string.Join(" ", upper.ToArray());
                given = string.Join(" ", words.Where(w => !caps(w)).ToArray());
            }
            else if (upper.Count == words.Count)
            {
                family = words[0];
                given = string.Join(" ", words.Skip(1).ToArray());
            }
            else
            {
                family = words[words.Count - 1];
                given = string.Join(" ", words.Take(words.Count - 1).ToArray());
            }
        }

        /// <summary>
        /// The race's swimmer among the records: his name (the same person, SameName), else his lane when the swimmer there is not
        /// plainly another one, else the only record.
        /// </summary>
        private static void Choose(SwimPasteResult result, string athlete, int lane)
        {
            if (result.Records.Count == 0)
                return;

            bool named = !string.IsNullOrWhiteSpace(athlete);
            if (named)
            {
                double best = 0;
                int at = -1;
                bool tie = false;
                for (int i = 0; i < result.Records.Count; i++)
                {
                    double s = NameScore(athlete, result.Records[i]);
                    if (s > best + 1e-9)
                    {
                        best = s;
                        at = i;
                        tie = false;
                    }
                    else if (Math.Abs(s - best) <= 1e-9 && s > 0)
                        tie = true;
                }

                if (at >= 0 && best >= SwimNameMatch.SameName && !tie)
                {
                    result.Best = at;
                    result.Why = "name";
                    return;
                }
            }

            if (lane > 0)
            {
                List<int> same = Enumerable.Range(0, result.Records.Count).Where(i => result.Records[i].Lane == lane).ToList();
                if (same.Count == 1 && (!named || NameScore(athlete, result.Records[same[0]]) >= OtherName))
                {
                    result.Best = same[0];
                    result.Why = "lane";
                    return;
                }
            }

            if (result.Records.Count == 1)
            {
                result.Best = 0;
                result.Why = "only";
            }
        }

        /// <summary>
        /// The event of a results link of the widgets that name it in the link (the results of the European Aquatics championships:
        /// "#/athletic-result/SWM/ST/M/100MBA------------/SFNL/000200--": men, 100 m backstroke, semi-final) and the city of the
        /// competition's page ("europeanaquatics.org/paris-2026/..."); null when the link names no event.
        /// </summary>
        public static SwimEventInfo EventFromLink(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
                return null;

            Match m = Regex.Match(link, @"#/athletic-result/SWM/[A-Z]*/([MWX])/((?:4X)?[0-9]{2,4}M[A-Z]{2,3})-*/([A-Z0-9]+)", RegexOptions.IgnoreCase);
            if (!m.Success)
                return null;

            SwimEventInfo ev = new SwimEventInfo();
            string g = m.Groups[1].Value.ToUpperInvariant();
            ev.Gender = g == "W" ? "F" : g;

            Match e = Regex.Match(m.Groups[2].Value.ToUpperInvariant(), @"^(4X)?([0-9]{2,4})M([A-Z]{2,3})$");
            if (e.Success)
            {
                int leg = int.Parse(e.Groups[2].Value, CultureInfo.InvariantCulture);
                ev.Relay = e.Groups[1].Success;
                ev.Distance = ev.Relay ? 4 * leg : leg;
                ev.Style = StyleCode(e.Groups[3].Value);
                if (ev.Style.Length == 0)
                    ev.Distance = 0;
            }

            string phase = m.Groups[3].Value.ToUpperInvariant();
            ev.Round = phase.Contains("SF") || phase.Contains("SEM") ? "semi" :
                phase.StartsWith("FN", StringComparison.Ordinal) || phase.Contains("FIN") ? "final" :
                phase.Contains("HEAT") || phase.StartsWith("HT", StringComparison.Ordinal) || phase.StartsWith("PR", StringComparison.Ordinal) ? "heats" : "";

            Match site = Regex.Match(link, @"^https?://[^/#?]+/([^/#?]+)/schedule-and-results", RegexOptions.IgnoreCase);
            if (site.Success)
                ev.Location = CityOf(site.Groups[1].Value.Replace('-', ' '));
            return ev;
        }

        /// <summary>The style of an event code of the results (FR, BA, BR, BF, IM, ...); "" when unknown.</summary>
        private static string StyleCode(string code)
        {
            switch (code)
            {
                case "FR": case "FS": return "fs";
                case "BA": case "BK": return "bk";
                case "BR": case "BS": return "br";
                case "BF": case "FL": case "BU": case "FLY": return "fly";
                case "IM": case "ME": case "MR": case "MED": return "im";
                default: return "";
            }
        }

        /// <summary>
        /// The event in the title of pasted results (its first lines): "Men's 100m Backstroke Semifinal", "4x100m Medley Relay Women
        /// Final", "100μ. Ύπτιο Ανδρών Ημιτελικός"; a distance and a style on the same line, else null.
        /// </summary>
        public static SwimEventInfo EventFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string head = Normalize(text.Length > 4000 ? text.Substring(0, 4000) : text);
            int lines = 0;
            foreach (string raw in head.Split('\n'))
            {
                if (raw.Trim().Length == 0)
                    continue;
                if (++lines > 6)
                    break;

                string line = Plain(raw);
                // Hy-Tek writes the course between them: "100 LC Meter", "50 SC Meter" (since 2.9.2).
                Match d = Regex.Match(line, @"(?<![0-9])(4\s*[x×χ]\s*)?([0-9]{2,4})\s*(?:(lc|sc)\s*)?(?:m|μ|μετρ|meter|metre)(?![a-zα-ω])", RegexOptions.IgnoreCase);
                string style = StyleWord(line);
                if (!d.Success || style.Length == 0)
                    continue;

                int n = int.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture);
                if (n < 25 || n > 1500)
                    continue;

                SwimEventInfo ev = new SwimEventInfo { Style = style, Relay = d.Groups[1].Success || Regex.IsMatch(line, @"\brelay\b|σκυταλ") };
                ev.Distance = d.Groups[1].Success ? 4 * n : n;
                if (d.Groups[3].Success)
                    ev.Pool = d.Groups[3].Value.ToLowerInvariant() == "sc" ? 25 : 50;
                ev.Gender = Regex.IsMatch(line, @"\b(women'?s?|girls|ladies)\b|γυναικ|κοριτσ|κορασιδ|νεανιδ") ? "F" :
                    Regex.IsMatch(line, @"\b(men'?s?|boys)\b|ανδρ|αγορι|παιδων|εφηβ") ? "M" :
                    Regex.IsMatch(line, @"\bmixed\b") || (style != "im" && Regex.IsMatch(line, @"μικτ|μεικτ")) ? "X" : "";
                ev.Round = Regex.IsMatch(line, @"semi|ημιτελικ") ? "semi" :
                    Regex.IsMatch(line, @"\bfinals?\b|τελικ") ? "final" :
                    Regex.IsMatch(line, @"\bheats?\b|prelim|προκριματικ|σειρ") ? "heats" : "";
                return ev;
            }

            return null;
        }

        /// <summary>The style named in a line (lower case without accents); "" when none.</summary>
        private static string StyleWord(string line)
        {
            // (2.15.0) «Μικτή» is the medley with «ατομική»/«ομαδική» or alone; with another style it is a mixed relay (men and women):
            // «4x50μ. Ελεύθερο Μεικτή» was read as a medley (legs 2–3 breaststroke and butterfly).
            bool mixedWord = Regex.IsMatch(line, @"μικτ|μεικτ");
            if (Regex.IsMatch(line, @"medley|\bim\b") || (mixedWord && Regex.IsMatch(line, @"ατομικ|ομαδικ")))
                return "im";
            if (Regex.IsMatch(line, @"backstroke|\bback\b|υπτιο"))
                return "bk";
            if (Regex.IsMatch(line, @"breaststroke|\bbreast\b|προσθιο"))
                return "br";
            if (Regex.IsMatch(line, @"butterfly|\bfly\b|πεταλουδα"))
                return "fly";
            if (Regex.IsMatch(line, @"freestyle|\bfree\b|ελευθερο"))
                return "fs";
            return mixedWord ? "im" : "";
        }

        /// <summary>
        /// The competition from the title of its results page ("SCHEDULE & RESULTS – European Aquatics Championships Paris 2026": the
        /// part that is not the page's own name), its city (the word before the year) and a short course pool (25).
        /// </summary>
        public static SwimEventInfo EventFromPageTitle(string title)
        {
            SwimEventInfo ev = new SwimEventInfo();
            if (string.IsNullOrWhiteSpace(title))
                return ev;

            string best = "";
            foreach (string part in Regex.Split(System.Net.WebUtility.HtmlDecode(title), @"\s+[–—|\-]\s+"))
            {
                string p = part.Trim();
                string low = p.ToLowerInvariant();
                if (low.Contains("schedule") || low.Contains("results") || low.Contains("αποτελέσματα") || low.Contains("πρόγραμμα"))
                    continue;
                if (p.Length > best.Length)
                    best = p;
            }

            ev.Competition = best.Length > 120 ? best.Substring(0, 120) : best;
            ev.Location = CityOf(best);
            string all = title.ToLowerInvariant();
            if (all.Contains("short course") || Regex.IsMatch(all, @"\(25\s*m\)|\b25\s*m\b"))
                ev.Pool = 25;
            return ev;
        }

        /// <summary>The city of a competition's name or page ("paris 2026", "European Championships Belgrade 2024"): the word before
        /// the year, with a capital; "" when none.</summary>
        public static string CityOf(string name)
        {
            Match m = Regex.Match(name ?? "", @"(\p{L}[\p{L}'’\-]+)\s+(?:19|20)[0-9]{2}\b");
            if (!m.Success)
                return "";
            string w = m.Groups[1].Value;
            string low = w.ToLowerInvariant();
            if (low == "championships" || low == "championship" || low == "cup" || low == "games" || low == "open" || low == "meet")
                return "";
            return char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant();
        }

        /// <summary>
        /// How alike the race's athlete (as typed: family and given names split at any space, either order) and a record's swimmer are
        /// (0-1): SwimNameMatch, an initial for the given name ("M. ROSSI") counting when the family name is the same and the initial the
        /// given name's first letter.
        /// </summary>
        public static double NameScore(string athlete, SwimPasteRecord r)
        {
            string letters = new string(r.GivenName.Where(char.IsLetter).ToArray());
            bool initial = letters.Length >= 1 && letters.Length <= 3 && r.GivenName.Contains(".");
            double s = 0;
            foreach (string[] pair in NameSplits(athlete))
            {
                s = Math.Max(s, SwimNameMatch.Similarity(pair[0], pair[1], r.FamilyName, r.GivenName));
                if (!initial)
                    continue;
                string g = SwimNameMatch.Latin(pair[1]);
                if (g.Length > 0 && SwimNameMatch.FamilySimilarity(pair[0], r.FamilyName) >= 0.95 &&
                    g[0] == SwimNameMatch.Latin(letters.Substring(0, 1))[0])
                    s = Math.Max(s, 0.95);
            }

            return s;
        }

        /// <summary>
        /// A typed name as family and given names, split at every space in both orders ("DE LA CRUZ Maria", "Maria de la Cruz"); one
        /// word: a family name alone.
        /// </summary>
        public static List<string[]> NameSplits(string athlete)
        {
            List<string[]> pairs = new List<string[]>();
            string[] words = (athlete ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 1)
                pairs.Add(new[] { words[0], "" });
            for (int i = 1; i < words.Length; i++)
            {
                string a = string.Join(" ", words.Take(i).ToArray()), b = string.Join(" ", words.Skip(i).ToArray());
                pairs.Add(new[] { a, b });
                pairs.Add(new[] { b, a });
            }

            return pairs;
        }

        /// <summary>"58.58", "1:01.23", "58,58" (s); NaN when it is not a time.</summary>
        public static double ParseTime(string text)
        {
            Match m = Regex.Match((text ?? "").Trim(), @"^(?:([0-9]{1,2}):)?([0-9]{1,3})[.,]([0-9]{1,2})$");
            if (!m.Success)
                return double.NaN;
            double minutes = m.Groups[1].Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            double seconds = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            string frac = m.Groups[3].Value;
            double f = int.Parse(frac, CultureInfo.InvariantCulture) / (frac.Length == 1 ? 10.0 : 100.0);
            if (m.Groups[1].Success && seconds >= 60)
                return double.NaN;
            return Math.Round(minutes * 60 + seconds + f, 2);
        }

        /// <summary>"58.58", "1:01.23".</summary>
        public static string FormatTime(double t)
        {
            int minutes = (int)Math.Floor(t / 60 + 1e-9);
            double rest = t - minutes * 60;
            return minutes > 0 ? minutes.ToString(CultureInfo.InvariantCulture) + ":" + rest.ToString("00.00", CultureInfo.InvariantCulture)
                : rest.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
