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

    /// <summary>What was read from the text: the swimmers, the one of the race and why him.</summary>
    public class SwimPasteResult
    {
        public List<SwimPasteRecord> Records = new List<SwimPasteRecord>();

        /// <summary>The record of the race's swimmer (by his name, his lane, or the only one with a time); −1 when the analyst chooses.</summary>
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
        private enum Kind { Word, Nation, Int, Time, Distance, Status, Paren, Dash }

        private class Token
        {
            public Kind Kind;
            public string Text;
            public double Value;

            /// <summary>A time with a sign: the gap to the winner (+0.35), not a time of the swimmer.</summary>
            public bool Signed;
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

        /// <summary>Words of the column titles and the pages (English, Greek), and of the icons copied with a table: never a name.</summary>
        private static readonly string[] HeaderWords =
        {
            "rank", "lane", "ctry", "country", "nation", "nat", "noc", "name", "athlete", "athletes", "swimmer", "time", "gap", "distance",
            "dist", "splits", "split", "rk", "ln", "rt", "r.t.", "reaction", "tb", "behind", "pts", "points", "yob", "yb", "born", "year",
            "club", "team", "pos", "place", "result", "results", "official", "unofficial", "heat", "heats", "final", "finals", "semifinal",
            "semifinals", "summary", "men's", "women's", "men", "women", "boys", "girls", "mixed", "freestyle", "backstroke",
            "breaststroke", "butterfly", "medley", "individual", "relay", "of", "the", "and", "by", "powered", "scroll", "to", "top",
            "start", "list", "records", "reports", "entries", "q", "qso", "so", "wr", "er", "cr", "nr", "or", "wj", "ej", "ejr", "wjr",
            "θέση", "θεση", "διαδρ", "διάδρ", "διαδρομος", "διάδρομος", "ονοματεπωνυμο", "ονοματεπώνυμο", "ονομα", "όνομα", "επωνυμο",
            "επώνυμο", "ετος", "έτος", "γεννησης", "γέννησης", "σωματειο", "σωματείο", "συλλογος", "σύλλογος", "ομαδα", "ομάδα", "χρονος",
            "χρόνος", "αντιδραση", "αντίδραση", "βαθμοι", "βαθμοί", "ενδιαμεσοι", "ενδιάμεσοι", "χωρα", "χώρα", "κατ", "κατηγορια"
        };

        private static readonly string[] IconWords =
        {
            "add_circle_outline", "remove_circle_outline", "add_circle", "remove_circle", "keyboard_arrow_up", "keyboard_arrow_down",
            "expand_more", "expand_less", "arrow_drop_down", "arrow_drop_up", "chevron_right", "chevron_left", "arrow_forward", "arrow_back"
        };

        private static readonly string[] StatusWords = { "DSQ", "DNS", "DNF", "DQ", "DNC", "DISQ", "ΑΚΥΡ", "ΑΚ", "ΔΕΝ" };

        /// <summary>
        /// Reads the swimmers of the text and finds the race's one: by the name of the race's athlete (Latin or Greek letters, either
        /// order, an initial for the given name), else by the race's lane (−1/0: not known… 0 counts only when written), else the only
        /// swimmer with a time.
        /// </summary>
        public static SwimPasteResult Parse(string text, int distance, string athlete, int lane)
        {
            SwimPasteResult result = new SwimPasteResult();
            if (string.IsNullOrWhiteSpace(text) || distance <= 0)
                return result;

            List<Token> tokens = Tokenize(text);
            List<int> starts = RecordStarts(tokens);
            if (starts.Count == 0)
            {
                SwimPasteRecord only = ReadRecord(tokens, 0, tokens.Count, false, distance);
                if (only != null)
                    result.Records.Add(only);
            }
            else
            {
                for (int i = 0; i < starts.Count; i++)
                {
                    int to = i + 1 < starts.Count ? starts[i + 1] : tokens.Count;
                    SwimPasteRecord r = ReadRecord(tokens, starts[i], to, true, distance);
                    if (r != null)
                        result.Records.Add(r);
                }
            }

            Choose(result, athlete, lane);
            return result;
        }

        /// <summary>The text in tokens: icons and column titles out, the country written onto the name split ("ITAMarco").</summary>
        private static List<Token> Tokenize(string text)
        {
            string s = text.Replace(' ', ' ').Replace(' ', ' ').Replace(' ', ' ');
            foreach (string icon in IconWords)
                s = Regex.Replace(s, Regex.Escape(icon), " ", RegexOptions.IgnoreCase);

            // A letter written onto a number ("Gap1": a column title onto the rank).
            s = Regex.Replace(s, @"(?<=[A-Za-zΑ-Ωα-ωάέήίόύώΆΈΉΊΌΎΏ])(?=\d)", " ");

            // The country onto the given name ("ITAMarco", the tables of some sites).
            s = Regex.Replace(s, @"\b([A-Z]{3})(?=[A-Z][a-z])", m => Nations.Contains(m.Groups[1].Value) ? m.Groups[1].Value + " " : m.Value);

            // A unit apart ("50 m") onto its distance; parentheses with spaces inside closed up.
            s = Regex.Replace(s, @"\b(\d{2,4})\s+(m|μ|M|Μ)\b(?![a-zA-Zα-ωΑ-Ω])", "$1$2");
            s = Regex.Replace(s, @"\(\s+", "(");
            s = Regex.Replace(s, @"\s+\)", ")");

            List<Token> tokens = new List<Token>();
            foreach (string raw in Regex.Split(s, @"[\s\|;]+"))
            {
                string t = raw.Trim().Trim(',');
                if (t.Length == 0)
                    continue;
                Token token = Classify(t);
                if (token != null)
                    tokens.Add(token);
            }

            return tokens;
        }

        private static Token Classify(string t)
        {
            string inner = t.Trim('(', ')');
            if (inner.Length == 3 && inner != t && Nations.Contains(inner))
                return new Token { Kind = Kind.Nation, Text = inner };

            if (t.StartsWith("(", StringComparison.Ordinal) || t.EndsWith(")", StringComparison.Ordinal))
            {
                // "(27.48)": a time by length; "(+2)", "(=)": a change of rank.
                Match pt = Regex.Match(t, @"^\(?(\d{1,2}:)?\d{1,3}[.,]\d{1,2}\)?$");
                return new Token { Kind = Kind.Paren, Text = t, Value = pt.Success ? ParseTime(t.Trim('(', ')')) : double.NaN };
            }

            if (t == "-" || t == "–" || t == "—" || t == "=")
                return new Token { Kind = Kind.Dash, Text = t };

            string upper = t.ToUpperInvariant().TrimEnd('.');
            if (StatusWords.Contains(upper))
                return new Token { Kind = Kind.Status, Text = upper };

            Match dm = Regex.Match(t, @"^(\d{2,4})(m|μ|M|Μ)\.?:?$");
            if (dm.Success)
                return new Token { Kind = Kind.Distance, Text = t, Value = int.Parse(dm.Groups[1].Value, CultureInfo.InvariantCulture) };

            Match tm = Regex.Match(t, @"^([+\-])?((?:\d{1,2}:)?\d{1,3}[.,]\d{1,2})([A-Za-z=*]{0,4})$");
            if (tm.Success)
            {
                double v = ParseTime(tm.Groups[2].Value);
                if (!double.IsNaN(v))
                    return new Token { Kind = Kind.Time, Text = t, Value = v, Signed = tm.Groups[1].Success };
            }

            Match im = Regex.Match(t, @"^(\d{1,4})\.?$");
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
            string s = t.ToLowerInvariant().TrimEnd('.', ':');
            if (HeaderWords.Contains(s))
                return true;

            // Written together: every part a title.
            string rest = s;
            bool any = false;
            while (rest.Length > 0)
            {
                string w = HeaderWords.Where(h => h.Length > 1 && rest.StartsWith(h, StringComparison.Ordinal)).OrderByDescending(h => h.Length).FirstOrDefault();
                if (w == null)
                    return false;
                rest = rest.Substring(w.Length);
                any = true;
            }

            return any && t.Length > 4;
        }

        /// <summary>
        /// Where the swimmers' rows start: a rank (or "-") then a lane (0-10) then a name or a country. Rows of splits ("50 3 27.48")
        /// are not starts: a time follows the second number.
        /// </summary>
        private static List<int> RecordStarts(List<Token> tokens)
        {
            List<int> starts = new List<int>();
            for (int i = 0; i + 2 < tokens.Count; i++)
            {
                Token a = tokens[i], b = tokens[i + 1], c = tokens[i + 2];
                bool rank = (a.Kind == Kind.Int && a.Value >= 1 && a.Value <= 99) || a.Kind == Kind.Dash;
                bool laneHere = b.Kind == Kind.Int && b.Value >= 0 && b.Value <= 10 && !b.Text.EndsWith(".", StringComparison.Ordinal);
                bool named = c.Kind == Kind.Word || c.Kind == Kind.Nation ||
                    (c.Kind == Kind.Time && c.Value >= 0.30 && c.Value <= 1.20 && i + 3 < tokens.Count && (tokens[i + 3].Kind == Kind.Word || tokens[i + 3].Kind == Kind.Nation));
                if (rank && laneHere && named)
                {
                    starts.Add(i);
                    i += 2;
                }
            }

            return starts;
        }

        /// <summary>One swimmer from his tokens [from, to).</summary>
        private static SwimPasteRecord ReadRecord(List<Token> tokens, int from, int to, bool rankLane, int distance)
        {
            SwimPasteRecord r = new SwimPasteRecord();
            r.Text = string.Join(" ", tokens.Skip(from).Take(to - from).Select(t => t.Text).ToArray());
            int i = from;
            if (rankLane)
            {
                r.Rank = tokens[i].Kind == Kind.Int ? (int)tokens[i].Value : 0;
                r.Lane = (int)tokens[i + 1].Value;
                i += 2;
            }

            // The name: the first run of words (a country before or after it); then the year of birth and the club (the words after
            // the year or the country, before the times).
            List<string> name = new List<string>(), club = new List<string>();
            int firstNumber = -1;
            bool numberAfterName = false;
            for (int k = i; k < to; k++)
            {
                Token t = tokens[k];
                if (t.Kind == Kind.Nation)
                {
                    if (r.Nation.Length == 0)
                        r.Nation = t.Text;
                    continue;
                }

                if (t.Kind == Kind.Status)
                {
                    r.Status = t.Text;
                    continue;
                }

                if (t.Kind == Kind.Word)
                {
                    if (numberAfterName)
                        break;
                    if (club.Count > 0 || (name.Count > 0 && (r.BirthYear > 0 || (r.Nation.Length > 0 && NationAfterName(tokens, i, k)))))
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

            // The status and the country anywhere in the row.
            for (int k = i; k < to; k++)
            {
                if (tokens[k].Kind == Kind.Status && r.Status.Length == 0)
                    r.Status = tokens[k].Text;
                if (tokens[k].Kind == Kind.Nation && r.Nation.Length == 0)
                    r.Nation = tokens[k].Text;
            }

            ReadTimes(tokens, Math.Max(i, firstNumber < 0 ? to : firstNumber), to, distance, r);
            if (r.Time.HasValue && r.Status.Length > 0 && (r.Status == "DSQ" || r.Status == "DQ" || r.Status == "DISQ"))
                r.Time = null;
            if (!r.Time.HasValue && r.Status.Length == 0 && name.Count == 0)
                return null;
            return r;
        }

        /// <summary>The country is written after the name (the words after it are the club), not before it (EA: "ITA Marco ROSSI").</summary>
        private static bool NationAfterName(List<Token> tokens, int from, int k)
        {
            bool word = false;
            for (int j = from; j < k; j++)
            {
                if (tokens[j].Kind == Kind.Word)
                    word = true;
                else if (tokens[j].Kind == Kind.Nation)
                    return word;
            }

            return false;
        }

        /// <summary>
        /// The times of a row: the reaction time (0.30-1.20 before the first time of the race), the times by distance ("50m 27.48", "50 3
        /// 27.48"), else the times in order (cumulative, or by length when they add up to the final time); the final time by its distance,
        /// else the longest time that fits the distance.
        /// </summary>
        private static void ReadTimes(List<Token> tokens, int from, int to, int distance, SwimPasteRecord r)
        {
            SortedDictionary<int, double> at = new SortedDictionary<int, double>();
            List<double> free = new List<double>();
            HashSet<int> used = new HashSet<int>();
            const double min = 9.0;

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
                    if (bare && u.Kind == Kind.Time && (u.Signed || u.Value < Math.Max(min, 0.38 * d) || u.Value > 1.3 * d + 5))
                        break;
                    if (u.Kind == Kind.Time && !u.Signed && u.Value >= Math.Max(min, 0.38 * d) && u.Value <= 1.3 * d + 5 && !used.Contains(j))
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
                if (t.Value >= min)
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
                    r.Time = fits.Max();
            }

            if (!r.Time.HasValue)
                return;

            // The splits every 50 m: by distance, else the times in order before the final time (25 m ones every other), else lengths.
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
                List<double> before = free.TakeWhile(v => v < r.Time.Value - 1e-6).ToList();
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
                    List<double> laps = free.Where(v => v < r.Time.Value - 1e-6).ToList();
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

        /// <summary>The race's swimmer among the records: his name, else his lane, else the only one with a time.</summary>
        private static void Choose(SwimPasteResult result, string athlete, int lane)
        {
            if (result.Records.Count == 0)
                return;

            if (!string.IsNullOrWhiteSpace(athlete))
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

                if (at >= 0 && best >= SwimNameMatch.SameName - 0.03 && !tie)
                {
                    result.Best = at;
                    result.Why = "name";
                    return;
                }
            }

            if (lane > 0)
            {
                List<int> same = Enumerable.Range(0, result.Records.Count).Where(i => result.Records[i].Lane == lane).ToList();
                if (same.Count == 1)
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
        /// How alike the race's athlete (as typed, either order) and a record's swimmer are (0-1): SwimNameMatch, an initial for the
        /// given name ("M. ROSSI") counting when the family name is the same and the initial the given name's first letter.
        /// </summary>
        public static double NameScore(string athlete, SwimPasteRecord r)
        {
            string a1, a2;
            SwimNameMatch.Split(athlete, out a1, out a2);
            double s = Math.Max(SwimNameMatch.Similarity(a1, a2, r.FamilyName, r.GivenName), SwimNameMatch.Similarity(a2, a1, r.FamilyName, r.GivenName));

            string initial = new string(r.GivenName.Where(char.IsLetter).ToArray());
            if (initial.Length >= 1 && initial.Length <= 3 && r.GivenName.Contains("."))
            {
                foreach (string[] pair in new[] { new[] { a1, a2 }, new[] { a2, a1 } })
                {
                    string g = SwimNameMatch.Latin(pair[1]);
                    if (SwimNameMatch.FamilySimilarity(pair[0], r.FamilyName) >= 0.95 && g.Length > 0 &&
                        g[0] == SwimNameMatch.Latin(initial.Substring(0, 1))[0])
                        s = Math.Max(s, 0.95);
                }
            }

            return s;
        }

        /// <summary>"58.58", "1:01.23", "58,58" (s); NaN when it is not a time.</summary>
        public static double ParseTime(string text)
        {
            Match m = Regex.Match((text ?? "").Trim(), @"^(?:(\d{1,2}):)?(\d{1,3})[.,](\d{1,2})$");
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
