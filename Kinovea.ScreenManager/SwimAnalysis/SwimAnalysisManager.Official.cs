#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The race linked with the official results on the web (since 2026-10-19): the competition (the link, or the date and the
    /// name), the event and the round, the athlete (his registry number, else his name, else the lane and the time read), then his
    /// official times (SwimRace.Official), his lane and, from the registry, the athlete.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>What the race needs from the official results (a copy for the background).</summary>
        private class OfficialQuery
        {
            public string Link = "";

            /// <summary>The link is the one kept for the competition (another race of it), not typed for this race.</summary>
            public bool LinkRemembered;
            public int Distance;
            public string Style = "";
            public bool Relay;
            public DateTime? Date;

            /// <summary>The day of the video file: tried for the KOE live results when the race's date finds no competition.</summary>
            public DateTime? VideoDate;
            public string Competition = "";
            public string Round = "";
            public int Lane;
            public string AthleteId = "";
            public string Athlete = "";

            /// <summary>The nation of the race's athlete in the registry: "GRE" for ours, his own for an international one; "" unknown.</summary>
            public string Nation = "";
            public string Gender = "";
            public int BirthYear;
            public double? Final;
            public SwimAthleteList Registry;
        }

        /// <summary>What was found: the swimmer's result (or the swimmers to choose from), and why not.</summary>
        private class OfficialFound
        {
            public SwimKoeCompetition Competition;
            public List<SwimKoeCompetition> Competitions = new List<SwimKoeCompetition>();

            /// <summary>The only KOE competition of the day, its name not the one typed: the analyst says whether it is it.</summary>
            public bool AskCompetition;
            public SwimOfficialUnit Unit;
            public SwimOfficialEntry Entry;
            public string How = "";

            /// <summary>Found by the registry number or the name (not by the lane): the swimmer is the race's athlete.</summary>
            public bool ByAthlete;

            /// <summary>The race's athlete was given (number or name) and is not in the results: the lane only offers swimmers.</summary>
            public bool AthleteNotFound;

            /// <summary>The competition as the source names it (Microplus, World Aquatics, the KOE live results).</summary>
            public string Source = "";

            /// <summary>Heats whose results could not be read; more heats than read: the lane alone does not decide then.</summary>
            public int Missing;
            public bool Truncated;
            public List<KeyValuePair<SwimOfficialUnit, SwimOfficialEntry>> Choices = new List<KeyValuePair<SwimOfficialUnit, SwimOfficialEntry>>();
            public string Error;

            /// <summary>The results of the link cannot be read by the program (another site, a page that does not open, no results of the event
            /// published): the analyst pastes them (since 2.8.1).</summary>
            public bool Locked;

            /// <summary>The competition of a results page whose results are not read (its title; since 2.8.2), null when not read.</summary>
            public SwimEventInfo PageEvent;
        }

        private BackgroundWorker officialWorker;

        /// <summary>Results found while the video is read, waiting to be offered (one at a time: the last found).</summary>
        private Timer officialWait;

        /// <summary>
        /// The button "🔗 Επίσημα αποτελέσματα" of the race tab: the race found in the official results (the link typed, else the one
        /// kept for the same competition in the same days, else the KOE live results by the date and the name of the competition)
        /// and, once the analyst agrees, its official times in the analysis.
        /// </summary>
        public void LinkOfficialResults(string link)
        {
            if (Race == null)
                return;
            if (officialWorker != null && officialWorker.IsBusy)
            {
                Status(SwimLang.T("Η αναζήτηση στα επίσημα αποτελέσματα τρέχει ήδη."), false);
                return;
            }

            // No link: the one used for another race of the same competition in the same days (as typed in the race details).
            Race.ResultsLink = (link ?? "").Trim();
            bool remembered = false;
            if (Race.ResultsLink.Length == 0)
            {
                string known = RememberedResultsLink(Race);
                if (!string.IsNullOrEmpty(known))
                {
                    Race.ResultsLink = known;
                    remembered = true;
                }
            }

            // A link that names the event (the results of the European Aquatics championships): the race's details from it first
            // (since 2.8.2), so that its results are read for that event (a new action: asked again if refused before).
            declinedEvent = null;
            ApplyEvent(SwimResultsPaste.EventFromLink(Race.ResultsLink), SwimLang.T("τον σύνδεσμο"));

            OfficialQuery q = NewOfficialQuery(Race.ResultsLink, remembered);

            Status(SwimLang.T("🔗 Αναζήτηση του αγώνα στα επίσημα αποτελέσματα…"), false);
            string raceId = Race.Id;
            officialWorker = new BackgroundWorker();
            officialWorker.DoWork += (s, e) => e.Result = FindOfficial(q);
            officialWorker.RunWorkerCompleted += (s, e) =>
            {
                if (Race == null || Race.Id != raceId)
                    return;
                if (e.Error != null)
                {
                    log.Error("The official results could not be read.", e.Error);
                    Status(SwimLang.T("🔗 Τα επίσημα αποτελέσματα δεν διαβάστηκαν: ") + e.Error.Message, true);
                    return;
                }

                OfferOfficialWhenFree((OfficialFound)e.Result, q, raceId);
            };
            officialWorker.RunWorkerAsync();
        }

        /// <summary>What the race needs from the official results, from its details (a copy for the background).</summary>
        private OfficialQuery NewOfficialQuery(string link, bool remembered)
        {
            SwimAthlete own = string.IsNullOrEmpty(Race.AthleteId) || Athletes == null ? null : Athletes.Find(Race.AthleteId);
            OfficialQuery q = new OfficialQuery
            {
                Link = (link ?? "").Trim(),
                LinkRemembered = remembered,
                Distance = Race.Distance,
                Style = Race.Style,
                Relay = Race.Relay,
                Date = ParseRaceDate(Race.Date),
                VideoDate = VideoFileDate(),
                Competition = Race.Competition ?? "",
                Round = Race.Round ?? "",
                Lane = RaceLaneNumber(),
                AthleteId = (Race.AthleteId ?? "").Trim(),
                Athlete = (Race.Athlete ?? "").Trim(),
                Nation = own == null ? "" : AthleteNation(own),
                Gender = SwimReference.NormalizeGender(Race.AthleteGender),
                Final = Computed != null ? Computed.FinalTime : null,
                Registry = Athletes
            };
            int born;
            if (int.TryParse(Race.AthleteBirthYear, NumberStyles.Integer, CultureInfo.InvariantCulture, out born))
                q.BirthYear = born;
            return q;
        }

        /// <summary>
        /// What was found offered now, or once the reading of the video and the auto pilot are over (the window of a reading lets the
        /// answer come meanwhile, and the official times would move the marks it reads).
        /// </summary>
        private void OfferOfficialWhenFree(OfficialFound found, OfficialQuery q, string raceId)
        {
            if (!ReadingBusy && !PilotActive)
            {
                OfferOfficial(found, q);
                return;
            }

            Status(SwimLang.T("🔗 Τα επίσημα αποτελέσματα βρέθηκαν· εμφανίζονται μόλις τελειώσει η ανάγνωση του βίντεο."), false);
            CancelPendingOffer();
            Timer wait = new Timer { Interval = 700 };
            officialWait = wait;
            wait.Tick += (s, e) =>
            {
                if (ReadingBusy || PilotActive)
                    return;
                wait.Stop();
                wait.Dispose();
                if (officialWait == wait)
                    officialWait = null;
                if (Race != null && Race.Id == raceId)
                    OfferOfficial(found, q);
            };
            wait.Start();
        }

        /// <summary>Results waiting for the end of the reading forgotten (others found, or pasted).</summary>
        private void CancelPendingOffer()
        {
            if (officialWait == null)
                return;
            officialWait.Stop();
            officialWait.Dispose();
            officialWait = null;
        }

        /// <summary>The date of the race as typed ("2.5.2025", "02/05/2025").</summary>
        private static DateTime? ParseRaceDate(string text)
        {
            DateTime d;
            string[] formats = { "d.M.yyyy", "d/M/yyyy", "d-M-yyyy", "yyyy-M-d", "d.M.yy" };
            if (!string.IsNullOrWhiteSpace(text) && DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return d;
            return null;
        }

        /// <summary>The day of the video file (its last change: a copy from the camera keeps it); null if unknown.</summary>
        private DateTime? VideoFileDate()
        {
            try
            {
                if (string.IsNullOrEmpty(videoPath) || !System.IO.File.Exists(videoPath))
                    return null;
                DateTime d = System.IO.File.GetLastWriteTime(videoPath);
                return d.Year >= 2000 ? d.Date : (DateTime?)null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The nation of an athlete of the registry: "GRE" for the federation's, his own for an international one.</summary>
        private static string AthleteNation(SwimAthlete a)
        {
            return a.International ? (a.Nation ?? "").Trim().ToUpperInvariant() : "GRE";
        }

        /// <summary>The round of the race as typed: "heats", "semi", "final" ("" when not typed).</summary>
        private static string RaceRound(string text)
        {
            string s = SwimAthleteList.Normalize(text);
            if (s.Length == 0)
                return "";
            if (s.Contains("ΗΜΙΤΕΛ") || s.Contains("SEMI"))
                return "semi";
            if (s.Contains("ΤΕΛ") || s.Contains("FINAL"))
                return "final";
            if (s.Contains("ΠΡΟΚΡ") || s.Contains("HEAT") || s.Contains("ΣΕΙΡ"))
                return "heats";
            return "";
        }

        /// <summary>The round of a unit as the race details write it ("" for none).</summary>
        private static string RoundName(string round)
        {
            switch (round)
            {
                case "heats": return "Προκριματικός";
                case "semi": return "Ημιτελικός";
                case "final": case "timed": return "Τελικός";
                default: return "";
            }
        }

        /// <summary>The link of a competition of the KOE live results (its page of events).</summary>
        private static string KoeLink(int code)
        {
            return SwimOfficialResults.KoeLive + "/#/athletic-sports-schedule/1/" + code.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The link kept for the competition of the race (its name as typed) from another race of it in the same days (four days
        /// apart at most: another meet of the same name has its own link); null if none.
        /// </summary>
        private string RememberedResultsLink(SwimRace race)
        {
            string name = SwimAthleteList.Normalize(race.Competition ?? "");
            DateTime? day = ParseRaceDate(race.Date);
            if (name.Length == 0 || !day.HasValue || Settings == null || Settings.ResultsLinks == null)
                return null;

            string best = null;
            double bestDays = double.MaxValue;
            foreach (KeyValuePair<string, string> pair in Settings.ResultsLinks)
            {
                int bar = pair.Key.LastIndexOf('|');
                DateTime kept;
                if (bar <= 0 || pair.Key.Substring(0, bar) != name || string.IsNullOrWhiteSpace(pair.Value) ||
                    !DateTime.TryParseExact(pair.Key.Substring(bar + 1), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out kept))
                    continue;

                double days = Math.Abs((kept - day.Value.Date).TotalDays);
                if (days <= 4 && days < bestDays)
                {
                    best = pair.Value;
                    bestDays = days;
                }
            }

            return best;
        }

        /// <summary>The link of a competition kept for its next races: by its name as typed and the day of the race.</summary>
        private void RememberResultsLink(string competition, DateTime? day, string link)
        {
            string name = SwimAthleteList.Normalize(competition ?? "");
            if (name.Length == 0 || !day.HasValue || string.IsNullOrWhiteSpace(link) || Settings == null)
                return;

            if (Settings.ResultsLinks == null)
                Settings.ResultsLinks = new Dictionary<string, string>();
            Settings.ResultsLinks[name + "|" + day.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] = link.Trim();

            // The last 300 kept (one per competition and day).
            if (Settings.ResultsLinks.Count > 300)
            {
                foreach (string old in Settings.ResultsLinks.Keys.OrderBy(k => k.Substring(k.LastIndexOf('|') + 1), StringComparer.Ordinal)
                    .Take(Settings.ResultsLinks.Count - 300).ToList())
                    Settings.ResultsLinks.Remove(old);
            }

            Settings.Save();
        }

        /// <summary>Background: the competition, the units of the event, their results, the swimmer.</summary>
        private static OfficialFound FindOfficial(OfficialQuery q)
        {
            OfficialFound found = new OfficialFound();

            // A Microplus results site (results.microplustimingservices.com/ACROPOLIS2024/…): its own files.
            List<SwimOfficialUnit> units = null;
            if (Regex.IsMatch(q.Link ?? "", SwimOfficialResults.MicroplusHost.Replace(".", "\\."), RegexOptions.IgnoreCase))
            {
                SwimMicroplusCompetition mp = SwimOfficialResults.MicroplusCompetition(q.Link);
                if (mp == null)
                {
                    found.Error = SwimLang.T("η σελίδα της Microplus δεν διαβάστηκε (έλεγξε τον σύνδεσμο και τη σύνδεση στο διαδίκτυο)");
                    found.Locked = true;
                    return found;
                }

                found.Source = mp.Name;
                units = SwimOfficialResults.MicroplusSchedule(mp);
            }

            // A World Aquatics competition (worldaquatics.com/competitions/4725/…): the event, then its heats with their results.
            int worldAquatics = SwimOfficialResults.WorldAquaticsCompetition(q.Link);
            if (units == null && worldAquatics > 0)
            {
                found.Source = "World Aquatics (" + worldAquatics.ToString(CultureInfo.InvariantCulture) + ")";
                units = new List<SwimOfficialUnit>();
                foreach (SwimOfficialUnit ev in SwimOfficialResults.WorldAquaticsEvents(worldAquatics)
                    .Where(u => u.Distance == q.Distance && u.Style == q.Style && u.Relay == q.Relay && GenderFits(u.Gender, q.Gender)))
                    units.AddRange(SwimOfficialResults.WorldAquaticsHeats(ev));
            }

            // The competition of the KOE live results: from the link (…/#/athletic-sports-schedule/1/8729, …/#/swimming-result/1/8729/<unit>).
            int code = 0;
            string unitCode = "";
            Match m = Regex.Match(q.Link ?? "", @"#/[a-z\-]+/\d+/(\d+)(?:/([0-9A-Fa-f\-]{36}))?");
            if (units == null && m.Success)
            {
                code = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                unitCode = m.Groups[2].Success ? m.Groups[2].Value : "";
            }
            else if (units == null && !string.IsNullOrWhiteSpace(q.Link) && !Regex.IsMatch(q.Link.Trim(), @"^\d+$"))
            {
                found.Error = SwimLang.T("ο σύνδεσμος δεν είναι των live αποτελεσμάτων της ΚΟΕ (live.koe.org.gr), σελίδα αποτελεσμάτων της Microplus ή ") +
                    SwimLang.T("διοργάνωση της World Aquatics (worldaquatics.com/competitions/…)· άφησέ τον κενό για αναζήτηση με την ημερομηνία");
                found.Locked = true;

                // The competition and its city from the title of its page (a link that names the event; since 2.8.2).
                if (SwimResultsPaste.EventFromLink(q.Link) != null)
                    found.PageEvent = SwimResultsPaste.EventFromPageTitle(SwimOfficialResults.PageTitle(q.Link));
                return found;
            }
            else if (units == null && !string.IsNullOrWhiteSpace(q.Link) && !int.TryParse(q.Link.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out code))
            {
                found.Error = SwimLang.T("ο κωδικός της διοργάνωσης δεν διαβάζεται");
                return found;
            }

            if (units == null && code == 0)
            {
                // By the date (of the race, else of the video file: a date left as the day the analysis began finds nothing): the
                // competitions of the KOE that day (a day before or after too).
                if (!q.Date.HasValue && !q.VideoDate.HasValue)
                {
                    found.Error = SwimLang.T("γράψε την ημερομηνία του αγώνα (στοιχεία αγώνα) ή επικόλλησε τον σύνδεσμο της διοργάνωσης από το live.koe.org.gr");
                    return found;
                }

                Dictionary<int, List<SwimKoeCompetition>> seasons = new Dictionary<int, List<SwimKoeCompetition>>();
                List<SwimKoeCompetition> on = new List<SwimKoeCompetition>();
                foreach (DateTime? d in new[] { q.Date, q.VideoDate })
                {
                    if (!d.HasValue)
                        continue;
                    on = KoeCompetitionsAround(d.Value.Date, seasons);
                    if (on.Count > 0)
                        break;
                }

                if (on.Count == 0)
                {
                    found.Error = string.Format(CultureInfo.InvariantCulture, SwimLang.T("δεν βρέθηκε διοργάνωση της ΚΟΕ στις {0:d/M/yyyy}· επικόλλησε τον σύνδεσμό της"),
                        (q.Date ?? q.VideoDate).Value);
                    return found;
                }

                // The name typed decides between them; the only one of the day, unlike the name typed, is asked about.
                if (!string.IsNullOrWhiteSpace(q.Competition))
                {
                    on = on.OrderByDescending(c => CompetitionLike(q.Competition, c)).ToList();
                    double best = CompetitionLike(q.Competition, on[0]);
                    double second = on.Count > 1 ? CompetitionLike(q.Competition, on[1]) : 0;
                    if (on.Count > 1 && best >= 0.75 && best - second >= 0.1)
                        on = on.Take(1).ToList();
                    else if (on.Count == 1 && best < 0.5)
                        found.AskCompetition = true;
                }

                found.Competitions = on;
                if (on.Count > 1 || found.AskCompetition)
                    return found;
                found.Competition = on[0];
                code = on[0].Code;
            }

            // The units of the event, then the round (when typed), the day of the race and the unit of the link.
            if (units == null)
            {
                units = SwimOfficialResults.KoeSchedule(code);
                if (found.Source.Length == 0)
                    found.Source = found.Competition != null ? found.Competition.Name : SwimLang.T("ΚΟΕ live (") + code.ToString(CultureInfo.InvariantCulture) + ")";
            }

            units = units.Where(u => u.Distance == q.Distance && u.Style == q.Style && u.Relay == q.Relay && GenderFits(u.Gender, q.Gender)).ToList();
            if (!string.IsNullOrEmpty(unitCode) && units.Any(u => string.Equals(u.UnitCode, unitCode, StringComparison.OrdinalIgnoreCase)))
                units = units.Where(u => string.Equals(u.UnitCode, unitCode, StringComparison.OrdinalIgnoreCase)).ToList();
            string round = RaceRound(q.Round);
            if (round.Length > 0 && units.Any(u => u.Round == round || (round == "final" && u.Round == "timed")))
                units = units.Where(u => u.Round == round || (round == "final" && u.Round == "timed")).ToList();

            // The day of the race: a link kept from another race of the competition must have races that day (else it is another
            // meet of the same name); a link typed for the race is taken even on another day (the date typed can be wrong: the
            // analyst sees the date of the results before they go in).
            if (q.Date.HasValue && units.Any(u => u.Date.HasValue && u.Date.Value.Date == q.Date.Value.Date))
                units = units.Where(u => !u.Date.HasValue || u.Date.Value.Date == q.Date.Value.Date).ToList();
            else if (q.LinkRemembered && q.Date.HasValue && units.Any(u => u.Date.HasValue))
            {
                found.Error = string.Format(CultureInfo.InvariantCulture, SwimLang.T("η διοργάνωση του συνδέσμου που κρατήθηκε για «{0}» δεν έχει αυτό το αγώνισμα ") +
                    SwimLang.T("στις {1:d/M/yyyy}· επικόλλησε τον σύνδεσμο αυτής της διοργάνωσης"), q.Competition, q.Date.Value);
                return found;
            }

            if (units.Count == 0)
            {
                found.Error = SwimLang.T("η διοργάνωση δεν έχει το αγώνισμα ") + q.Distance.ToString(CultureInfo.InvariantCulture) + SwimLang.T("μ. ") + SwimPresets.StyleShort(q.Style) +
                    (q.Gender.Length > 0 ? " (" + (q.Gender == "F" ? SwimLang.T("γυναικών") : SwimLang.T("ανδρών")) + ")" : "");
                // A competition of World Aquatics whose events are not published with their names and results (the national
                // meets it lists) can still be pasted from the page that shows them.
                found.Locked = worldAquatics > 0;
                return found;
            }

            if (units.Count > 60)
            {
                found.Truncated = true;
                units = units.Take(60).ToList();
            }

            foreach (SwimOfficialUnit u in units)
            {
                bool read = true;
                if (u.Source == "microplus")
                    read = SwimOfficialResults.MicroplusResults(u);
                else if (u.Source == "koe")
                    read = SwimOfficialResults.KoeResults(u);
                if (!read)
                    found.Missing++;
            }

            List<KeyValuePair<SwimOfficialUnit, SwimOfficialEntry>> all = units
                .SelectMany(u => u.Entries.Select(e => new KeyValuePair<SwimOfficialUnit, SwimOfficialEntry>(u, e))).ToList();
            List<KeyValuePair<SwimOfficialUnit, SwimOfficialEntry>> chosen = null;
            string family, given;
            SwimNameMatch.Split(q.Athlete, out family, out given);

            // 1. The registry number (the KOE results carry the federation's number of the Greek athletes; an international athlete of
            // the registry has "WA" and his World Aquatics code), with his family name when the race has his name (another
            // federation's number can be the same) and his nation.
            if (q.AthleteId.Length > 0)
            {
                chosen = all.Where(x => (x.Value.Code == q.AthleteId || (x.Value.IfId.Length > 0 && WorldAquaticsId(x.Value.IfId) == q.AthleteId)) &&
                    NationFits(q.Nation, x.Value.Nation) && (q.Athlete.Length == 0 || SameFamily(family, given, x.Value))).ToList();
                found.How = SwimLang.T("με τον αριθμό μητρώου");
            }

            // 2. The name (Greek or Latin letters, family name first or last), the year of birth and the nation.
            if ((chosen == null || chosen.Count == 0) && q.Athlete.Length > 0)
            {
                chosen = all.Where(x =>
                    (q.BirthYear == 0 || x.Value.BirthYear == 0 || x.Value.BirthYear == q.BirthYear) && NationFits(q.Nation, x.Value.Nation) &&
                    NameLike(family, given, x.Value) >= SwimNameMatch.SameName).ToList();
                found.How = SwimLang.T("με το όνομα");
            }

            if (chosen != null && chosen.Count > 0)
            {
                // The swimmer of the race: his result, or his results to choose from (heats and final, the round not typed); none
                // with a time (DSQ, DNS): said, nothing goes in.
                found.ByAthlete = true;
                List<KeyValuePair<SwimOfficialUnit, SwimOfficialEntry>> timed = chosen.Where(x => x.Value.Time.HasValue).ToList();
                if (timed.Count == 0)
                {
                    SwimOfficialEntry none = chosen[0].Value;
                    found.Error = SwimLang.T("ο αθλητής βρέθηκε (") + chosen[0].Key.Phase + SwimLang.T(") χωρίς χρόνο") + (none.Status.Length > 0 ? ": " + none.Status : "");
                    return found;
                }

                if (timed.Count > 1)
                {
                    found.Choices = timed;
                    return found;
                }

                found.Unit = timed[0].Key;
                found.Entry = timed[0].Value;
                return found;
            }

            // 3. The lane (of the race, or "L4" in the name of the video): with the final time read, its swimmer within half a second
            // of it when no other is within a second, all results read and the race's athlete not given; else its swimmers to
            // choose from, the nearest to the time read first.
            found.AthleteNotFound = q.AthleteId.Length > 0 || q.Athlete.Length > 0;
            if (q.Lane <= 0)
            {
                found.Error = found.AthleteNotFound ? SwimLang.T("ο αθλητής δεν βρέθηκε στο αγώνισμα (έλεγξε τη φάση και το όνομα, ή γράψε τον διάδρομο)")
                    : SwimLang.T("διάλεξε πρώτα τον αθλητή ή γράψε τον διάδρομο");
                return found;
            }

            List<KeyValuePair<SwimOfficialUnit, SwimOfficialEntry>> lane = all.Where(x => x.Value.Lane == q.Lane && x.Value.Time.HasValue).ToList();
            if (lane.Count == 0)
            {
                found.Error = SwimLang.T("κανείς με χρόνο στον διάδρομο ") + q.Lane.ToString(CultureInfo.InvariantCulture) + SwimLang.T(" του αγωνίσματος") +
                    (found.AthleteNotFound ? SwimLang.T(", και ο αθλητής δεν βρέθηκε") : "");
                return found;
            }

            if (q.Final.HasValue)
                lane = lane.OrderBy(x => Math.Abs(x.Value.Time.Value - q.Final.Value)).ToList();
            found.How = q.Final.HasValue ? SwimLang.T("με τον διάδρομο και τον χρόνο που διαβάστηκε") : SwimLang.T("με τον διάδρομο");
            bool sure = !found.AthleteNotFound && found.Missing == 0 && !found.Truncated && q.Final.HasValue &&
                Math.Abs(lane[0].Value.Time.Value - q.Final.Value) <= 0.5 &&
                (lane.Count == 1 || Math.Abs(lane[1].Value.Time.Value - q.Final.Value) > 1.0);
            if (!sure)
            {
                found.Choices = lane;
                return found;
            }

            found.Unit = lane[0].Key;
            found.Entry = lane[0].Value;
            return found;
        }

        /// <summary>The competitions of the KOE live results a day before to a day after (seasons read once).</summary>
        private static List<SwimKoeCompetition> KoeCompetitionsAround(DateTime day, Dictionary<int, List<SwimKoeCompetition>> seasons)
        {
            List<SwimKoeCompetition> all = new List<SwimKoeCompetition>();
            foreach (int season in new[] { day.Year, day.Year + 1 })
            {
                List<SwimKoeCompetition> list;
                if (!seasons.TryGetValue(season, out list))
                {
                    list = SwimOfficialResults.KoeCompetitions(season);
                    seasons[season] = list;
                }

                all.AddRange(list);
            }

            return all.Where(c => c.From.HasValue && c.From.Value.Date <= day.AddDays(1) && (c.To ?? c.From).Value.Date >= day.AddDays(-1))
                .GroupBy(c => c.Code).Select(g => g.First()).ToList();
        }

        /// <summary>How alike the name typed and a competition's names (Greek, Latin) are (0-1).</summary>
        private static double CompetitionLike(string typed, SwimKoeCompetition c)
        {
            return Math.Max(NameLike(typed, c.Name), NameLike(typed, c.NameLatin));
        }

        /// <summary>The gender of an event fits the race's: the same, or either unknown, or a mixed event.</summary>
        private static bool GenderFits(string unit, string race)
        {
            return string.IsNullOrEmpty(unit) || string.IsNullOrEmpty(race) || unit == "X" || unit == race;
        }

        /// <summary>The nation of a result fits the race's athlete: the same, or either unknown.</summary>
        private static bool NationFits(string athlete, string entry)
        {
            return string.IsNullOrEmpty(athlete) || string.IsNullOrWhiteSpace(entry) || string.Equals(athlete.Trim(), entry.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>How alike the race's athlete (family and given name as typed, either order) and a result's swimmer are (0-1).</summary>
        private static double NameLike(string family, string given, SwimOfficialEntry e)
        {
            return Math.Max(
                Math.Max(SwimNameMatch.Similarity(family, given, e.FamilyName, e.GivenName), SwimNameMatch.Similarity(family, given, e.FamilyNameLatin, e.GivenNameLatin)),
                Math.Max(SwimNameMatch.Similarity(given, family, e.FamilyName, e.GivenName), SwimNameMatch.Similarity(given, family, e.FamilyNameLatin, e.GivenNameLatin)));
        }

        /// <summary>The race's athlete has the family name of a result's swimmer (the athlete's number found there: the same person).</summary>
        private static bool SameFamily(string family, string given, SwimOfficialEntry e)
        {
            foreach (string name in new[] { family, given })
            {
                if (SwimNameMatch.FamilySimilarity(name, e.FamilyName) >= 0.95 || SwimNameMatch.FamilySimilarity(name, e.FamilyNameLatin) >= 0.95)
                    return true;
            }

            return false;
        }

        /// <summary>The registry's code of an international athlete from his World Aquatics code ("WA123456").</summary>
        public static string WorldAquaticsId(string ifId)
        {
            return string.IsNullOrWhiteSpace(ifId) ? "" : "WA" + ifId.Trim();
        }

        /// <summary>How alike two names of competitions are (0-1), word by word in Latin letters.</summary>
        private static double NameLike(string a, string b)
        {
            string ka = SwimNameMatch.Key(a), kb = SwimNameMatch.Key(b);
            if (ka.Length == 0 || kb.Length == 0)
                return 0;
            string[] wa = ka.Split(' '), wb = kb.Split(' ');
            int common = wa.Count(w => w.Length > 2 && wb.Any(x => SwimNameMatch.JaroWinkler(w, x) >= 0.9));
            return (double)common / Math.Max(1, Math.Min(wa.Count(w => w.Length > 2), wb.Count(w => w.Length > 2)));
        }

        /// <summary>UI thread: what was found shown, the analyst agrees (or chooses), then it goes in the analysis.</summary>
        private void OfferOfficial(OfficialFound found, OfficialQuery q)
        {
            string title = SwimLang.T("🔗 Επίσημα αποτελέσματα");
            if (found.AskCompetition && found.Competitions.Count == 1)
            {
                SwimKoeCompetition only = found.Competitions[0];
                if (MessageBox.Show(DialogOwner, SwimLang.T("Τη μέρα του αγώνα η ΚΟΕ έχει μία διοργάνωση, με άλλο όνομα από αυτό των στοιχείων του αγώνα:\n\n") +
                    only + SwimLang.T("\n\nΕίναι αυτή;"), title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    Status(SwimLang.T("🔗 Δεν μπήκαν επίσημοι χρόνοι: επικόλλησε τον σύνδεσμο της διοργάνωσης στα στοιχεία του αγώνα."), false);
                    return;
                }

                LinkOfficialResults(KoeLink(only.Code));
                return;
            }

            if (found.Competitions.Count > 1)
            {
                Status(SwimLang.T("🔗 Την ίδια μέρα έγιναν ") + found.Competitions.Count.ToString(CultureInfo.InvariantCulture) + SwimLang.T(" διοργανώσεις: διάλεξε."), false);
                int i = SwimChoiceDialog.Choose(DialogOwner, title, SwimLang.T("Ποια διοργάνωση είναι;"), found.Competitions.Select(c => c.ToString()).ToList());
                if (i < 0)
                    return;
                LinkOfficialResults(KoeLink(found.Competitions[i].Code));
                return;
            }

            if (found.Entry == null && found.Choices.Count > 0)
            {
                List<string> items = found.Choices.Select(x => OfficialLine(x.Key, x.Value, q.Final)).ToList();
                string question = found.AthleteNotFound
                    ? SwimLang.T("Ο αθλητής του αγώνα δεν βρέθηκε στα αποτελέσματα") + MissingNote(found) + SwimLang.T(". Είναι κάποιος από αυτούς ο αθλητής του βίντεο;")
                    : SwimLang.T("Ποιος είναι ο αθλητής του βίντεο;") + MissingNote(found);
                int i = SwimChoiceDialog.Choose(DialogOwner, title, question, items);
                if (i < 0)
                {
                    Status(SwimLang.T("🔗 Δεν μπήκαν επίσημοι χρόνοι."), false);
                    return;
                }

                found.Unit = found.Choices[i].Key;
                found.Entry = found.Choices[i].Value;
                found.How = SwimLang.T("από τη λίστα");
            }

            if (found.Entry == null)
            {
                Status("🔗 " + (found.Error ?? SwimLang.T("δεν βρέθηκε ο αγώνας")) + ".", true);
                if (found.Locked)
                {
                    ApplyEvent(found.PageEvent, SwimLang.T("τη σελίδα των αποτελεσμάτων"));
                    PasteOfficialResults(SwimLang.T("Τα αποτελέσματα αυτής της σελίδας δεν διαβάζονται αυτόματα: ") + (found.Error ?? "") + ".");
                }
                return;
            }

            // The race's athlete stays his own when the swimmer was not found by him (the lane, the list) and the race has one.
            SwimOfficialEntry e = found.Entry;
            SwimAthlete athlete = RegistryAthlete(e, q.Registry);
            bool keepAthlete = !found.ByAthlete && !string.IsNullOrWhiteSpace(Race.Athlete) && (athlete == null || athlete.Id != (Race.AthleteId ?? ""));
            List<double> times = e.OfficialTimes();
            StringBuilder b = new StringBuilder();
            b.Append(SwimLang.T("Βρέθηκε ")).Append(found.How).Append(":\n\n");
            b.Append(found.Source.Length > 0 ? found.Source : SwimLang.T("Επίσημα αποτελέσματα"));
            if (found.Unit.Date.HasValue)
                b.Append(" · ").Append(found.Unit.Date.Value.ToString("d/M/yyyy", CultureInfo.InvariantCulture));
            b.Append('\n').Append(OfficialLine(found.Unit, e, null)).Append("\n\n");
            b.Append(SwimLang.T("Επίσημοι χρόνοι: ")).Append(SwimRace.FormatOfficial(times));
            if (e.Reaction.HasValue)
                b.Append(SwimLang.T(" · αντίδραση ")).Append(e.Reaction.Value.ToString("0.00", CultureInfo.InvariantCulture));
            b.Append('\n');
            if (keepAthlete)
                b.Append(SwimLang.T("Ο αθλητής του αγώνα μένει ο ίδιος: ")).Append(Race.Athlete).Append('\n');
            else if (athlete != null && athlete.Id != (Race.AthleteId ?? ""))
                b.Append(SwimLang.T("Αθλητής από το μητρώο: ")).Append(athlete.Name).Append(" (").Append(athlete.Id).Append(")\n");
            if (e.Lane > 0 && (Race.Lane == 0 || Race.Lane != e.Lane))
                b.Append(SwimLang.T("Διάδρομος: ")).Append(e.Lane.ToString(CultureInfo.InvariantCulture)).Append('\n');
            DateTime? raceDay = ParseRaceDate(Race.Date);
            if (found.Unit.Date.HasValue && (!raceDay.HasValue || raceDay.Value.Date != found.Unit.Date.Value.Date))
                b.Append(SwimLang.T("Ημερομηνία του αγώνα: ")).Append(found.Unit.Date.Value.ToString("d/M/yyyy", CultureInfo.InvariantCulture))
                    .Append(raceDay.HasValue ? SwimLang.T(" (στα στοιχεία: ") + raceDay.Value.ToString("d/M/yyyy", CultureInfo.InvariantCulture) + ")" : "").Append('\n');
            if (found.Missing > 0)
                b.Append(SwimLang.T("(Δεν διαβάστηκαν τα αποτελέσματα ")).Append(found.Missing.ToString(CultureInfo.InvariantCulture)).Append(SwimLang.T(" σειρών.)\n"));
            b.Append(SwimLang.T("\nΝα μπουν στην ανάλυση; (Ο τερματισμός και οι τοίχοι στους επίσημους χρόνους, όσα σημάδια δεν έχεις ελέγξει μετακινούνται.)"));
            if (MessageBox.Show(DialogOwner, b.ToString(), title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                Status(SwimLang.T("🔗 Δεν μπήκαν επίσημοι χρόνοι."), false);
                return;
            }

            // The race's details as they are: «Αναίρεση» of the change puts them back with the times.
            SwimRace race = Race;
            string oldAthlete = race.Athlete, oldId = race.AthleteId, oldGender = race.AthleteGender, oldBorn = race.AthleteBirthYear,
                oldCategory = race.AthleteCategory, oldClub = race.AthleteClub, oldDate = race.Date, oldRound = race.Round,
                oldCompetition = race.Competition, oldLink = race.ResultsLink, oldLocation = race.Location;
            int oldLane = race.Lane;
            Action detailsBack = () =>
            {
                race.Athlete = oldAthlete;
                race.AthleteId = oldId;
                race.AthleteGender = oldGender;
                race.AthleteBirthYear = oldBorn;
                race.AthleteCategory = oldCategory;
                race.AthleteClub = oldClub;
                race.Lane = oldLane;
                race.Date = oldDate;
                race.Round = oldRound;
                race.Competition = oldCompetition;
                race.ResultsLink = oldLink;
                race.Location = oldLocation;
            };

            if (!keepAthlete && athlete != null && athlete.Id != (Race.AthleteId ?? ""))
                SetAthlete(athlete);
            if (e.Lane > 0)
                Race.Lane = e.Lane;
            if (string.IsNullOrWhiteSpace(Race.Athlete))
                Race.Athlete = e.Name.Length > 0 ? e.Name : e.NameLatin;

            // The race's details from the results: the day, and the round and the competition when not typed.
            if (found.Unit.Date.HasValue)
                Race.Date = found.Unit.Date.Value.ToString("d.M.yyyy", CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(Race.Round))
                Race.Round = RoundName(found.Unit.Round);
            if (string.IsNullOrWhiteSpace(Race.Competition) && found.Competition != null)
                Race.Competition = found.Competition.Name;
            else if ((Race.Competition ?? "").Trim().Length < 3 && found.Unit.Source == "microplus" && found.Source.Length > 0)
            {
                // A Microplus results site: its name (since 2.8.2), and its city when the name has one ("... Belgrade 2024").
                Race.Competition = found.Source;
                if ((Race.Location ?? "").Trim().Length < 3 && SwimResultsPaste.CityOf(found.Source).Length > 0)
                    Race.Location = SwimResultsPaste.CityOf(found.Source);
            }
            OfficialChanged(times, e.Reaction, detailsBack);

            // The link of the competition, for its next races in the same days.
            string link = !string.IsNullOrWhiteSpace(q.Link) ? q.Link.Trim() : found.Competition != null ? KoeLink(found.Competition.Code) : "";
            if (link.Length > 0)
            {
                Race.ResultsLink = link;
                RememberResultsLink(Race.Competition, found.Unit.Date ?? ParseRaceDate(Race.Date), link);
            }

            if (form != null)
                form.RefreshAll();

            // The registry learns from it (a sure match only): a Latin spelling of our athlete, an international athlete's code.
            if (!keepAthlete)
                RememberOfficialAthlete(e, athlete, found.ByAthlete, found.Unit.Source == "paste");
        }

        /// <summary>" (δεν διαβάστηκαν όλα τα αποτελέσματα)" when some heats were not read or left out.</summary>
        private static string MissingNote(OfficialFound found)
        {
            return found.Missing > 0 || found.Truncated ? SwimLang.T(" (δεν διαβάστηκαν όλα τα αποτελέσματα)") : "";
        }

        /// <summary>"100μ. fs · QUALIFIERS 3 · διάδρομος 4 · ΟΝΟΜΑ (σωματείο, 2009) · 54.54" (and its difference from the time read).</summary>
        private static string OfficialLine(SwimOfficialUnit u, SwimOfficialEntry e, double? read)
        {
            string name = e.Name.Length > 0 ? e.Name : e.NameLatin;
            string about = string.Join(", ", new[] { e.Club, e.Nation, e.BirthYear > 0 ? e.BirthYear.ToString(CultureInfo.InvariantCulture) : "" }
                .Where(x => !string.IsNullOrEmpty(x)).ToArray());
            int heat = e.Heat > 0 ? e.Heat : u.Heat;

            // What is not known is left out (since 2.8.1: a pasted row without its phase or lane).
            string phase = ((u.Phase ?? "") + (heat > 0 ? " " + heat.ToString(CultureInfo.InvariantCulture) : "")).Trim();
            string[] parts =
            {
                u.Event ?? "", phase, e.Lane >= 0 ? SwimLang.T("διάδρομος ") + e.Lane.ToString(CultureInfo.InvariantCulture) : "",
                name + (about.Length > 0 ? " (" + about + ")" : ""), e.Time.HasValue ? SwimReference.FormatTime(e.Time.Value) : (e.Status ?? "")
            };
            string line = string.Join(" · ", parts.Where(x => x.Length > 0).ToArray());
            if (read.HasValue && e.Time.HasValue)
                line += string.Format(CultureInfo.InvariantCulture, SwimLang.T(" (Δ {0:+0.00;-0.00} από τον χρόνο του βίντεο)"), e.Time.Value - read.Value);
            return line;
        }

        /// <summary>
        /// The registry's athlete of a result: his number (the KOE results) with his family name and nation, else his name, gender,
        /// year of birth and nation (one athlete only).
        /// </summary>
        private static SwimAthlete RegistryAthlete(SwimOfficialEntry e, SwimAthleteList registry)
        {
            if (registry == null || e == null)
                return null;

            // His code (the federation's number of a Greek athlete, the World Aquatics one of an international athlete) with his family
            // name (another spelling of the given name, "Giorgos" for "Georgios", stays his).
            SwimAthlete byCode = (e.Code.Length > 0 ? registry.Find(e.Code) : null) ?? (e.IfId.Length > 0 ? registry.Find(WorldAquaticsId(e.IfId)) : null);
            if (byCode != null && NationFits(AthleteNation(byCode), e.Nation) &&
                (LikeAthlete(byCode, e, false) >= SwimNameMatch.SameName || LikeAthlete(byCode, e, true) >= FamilyByCode))
                return byCode;

            // Else his name or another spelling of it, his year of birth, gender and nation, one athlete only.
            List<SwimAthlete> like = registry.Athletes.Where(a =>
            {
                int born;
                if (e.BirthYear > 0 && int.TryParse(a.BirthYear, NumberStyles.Integer, CultureInfo.InvariantCulture, out born) && born != e.BirthYear)
                    return false;
                string gender = SwimReference.NormalizeGender(a.Gender);
                if (e.Gender.Length > 0 && gender.Length > 0 && gender != e.Gender)
                    return false;
                if (!NationFits(AthleteNation(a), e.Nation))
                    return false;
                return LikeAthlete(a, e, false) >= SwimNameMatch.SameName;
            }).Take(2).ToList();
            return like.Count == 1 ? like[0] : null;
        }

        /// <summary>The family name alike this much with the code of the athlete: the same athlete.</summary>
        private const double FamilyByCode = 0.85;

        /// <summary>How alike an athlete of the registry (his name and its other spellings) and the athlete of a result are (0-1).</summary>
        private static double LikeAthlete(SwimAthlete a, SwimOfficialEntry e, bool familyOnly)
        {
            double best = 0;
            foreach (string name in new[] { a.Name }.Concat(a.Aliases ?? new List<string>()))
            {
                string f, g;
                SwimNameMatch.Split(name, out f, out g);
                if (familyOnly)
                    g = "";
                best = Math.Max(best, Math.Max(SwimNameMatch.Similarity(f, g, e.FamilyName, e.GivenName),
                    SwimNameMatch.Similarity(f, g, e.FamilyNameLatin, e.GivenNameLatin)));
            }

            return best;
        }

        /// <summary>
        /// The registry from the official results (since 2026-10-19): the Latin spelling of the name of an athlete of it that it does
        /// not know yet kept (results abroad have no registry number) when the swimmer was found by him (his number or his name,
        /// the same year of birth), an international athlete not in it added with his unique code (the analyst says whether he is
        /// one already there under another spelling).
        /// </summary>
        private void RememberOfficialAthlete(SwimOfficialEntry e, SwimAthlete athlete, bool byAthlete, bool pasted)
        {
            if (e == null || Settings == null || !Settings.IsConfigured)
                return;

            // Pasted results (since 2.8.1): no spelling learned (the text can be shortened, "M. ROSSI"), and an international athlete
            // only with his whole name (a family and a given name, not an initial).
            if (pasted && (athlete != null || !WholeName(e.FamilyNameLatin, e.GivenNameLatin)))
                return;

            if (athlete != null)
            {
                int born;
                bool otherYear = e.BirthYear > 0 && int.TryParse(athlete.BirthYear, NumberStyles.Integer, CultureInfo.InvariantCulture, out born) && born != e.BirthYear;
                string latin = e.NameLatin;
                if (!byAthlete || otherYear || latin.Length == 0 || KnownSpelling(athlete, latin))
                    return;

                client.AddAlias(Settings.WebAppUrl, Settings.Token, athlete.Id, latin, result =>
                {
                    if (!result.Ok)
                        return;
                    if (athlete.Aliases == null)
                        athlete.Aliases = new List<string>();
                    athlete.Aliases.Add(latin);
                    Athletes.Save();
                });
                return;
            }

            // A Greek athlete not found in the registry stays as he was typed (the registry is the federation's).
            string nation = (e.Nation ?? "").Trim().ToUpperInvariant();
            if (nation.Length == 3 && nation != "GRE")
                AddInternationalAthlete(e, false);
        }

        /// <summary>A family and a given name of two letters or more each, no initial ("M.", "M").</summary>
        private static bool WholeName(string family, string given)
        {
            Func<string, bool> whole = n =>
            {
                string letters = new string((n ?? "").Where(char.IsLetter).ToArray());
                return letters.Length >= 2 && !(n ?? "").Trim().EndsWith(".", StringComparison.Ordinal);
            };
            return whole(family) && whole(given);
        }

        /// <summary>The spelling is the athlete's name or one of its other spellings (letter for letter in Latin, in any order).</summary>
        private static bool KnownSpelling(SwimAthlete a, string name)
        {
            string key = SortedKey(name);
            return SortedKey(a.Name) == key || (a.Aliases != null && a.Aliases.Any(x => SortedKey(x) == key));
        }

        private static string SortedKey(string name)
        {
            return string.Join(" ", SwimNameMatch.Key(name).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).OrderBy(w => w, StringComparer.Ordinal).ToArray());
        }

        /// <summary>
        /// An international athlete of a result in the sheet "Διεθνείς αθλητές": his unique code back (WA and his World Aquatics code,
        /// else X-ΧΩΡΑ-αύξων) as the athlete of the race. One who may be there already (same name and country, a year of birth
        /// unknown): the analyst chooses (force: a new one).
        /// </summary>
        private void AddInternationalAthlete(SwimOfficialEntry e, bool force)
        {
            JObject a = new JObject();
            a["name"] = e.NameLatin.Length > 0 ? e.NameLatin : e.Name;
            a["nation"] = e.Nation.Trim().ToUpperInvariant();
            a["gender"] = e.Gender == "F" ? "Γυναικών" : e.Gender == "M" ? "Ανδρών" : "";
            a["birthYear"] = e.BirthYear > 0 ? e.BirthYear.ToString(CultureInfo.InvariantCulture) : "";
            a["club"] = e.Club;
            a["waId"] = e.IfId;
            if (e.Name.Length > 0 && e.NameLatin.Length > 0)
                a["aliases"] = new JArray(e.Name);

            string raceId = Race.Id;
            Status(SwimLang.T("Καταχώρηση του διεθνούς αθλητή…"), false);
            client.AddAthlete(Settings.WebAppUrl, Settings.Token, a, force, result =>
            {
                if (Race == null || Race.Id != raceId)
                    return;

                JArray duplicates = result.Raw == null ? null : result.Raw["duplicates"] as JArray;
                if (!result.Ok && duplicates != null && duplicates.Count > 0)
                {
                    List<string> items = duplicates.Select(d => string.Format(CultureInfo.InvariantCulture, "{0} ({1}, {2}) · {3}", (string)d["name"], (string)d["nation"],
                        string.IsNullOrEmpty((string)d["birthYear"]) ? SwimLang.T("έτος γέννησης άγνωστο") : (string)d["birthYear"], (string)d["id"])).ToList();
                    items.Add(SwimLang.T("Άλλος αθλητής: νέος κωδικός"));
                    int i = SwimChoiceDialog.Choose(DialogOwner, SwimLang.T("Διεθνής αθλητής"), SwimLang.T("Ο αθλητής των αποτελεσμάτων μοιάζει με κάποιον που υπάρχει ήδη στους διεθνείς. Είναι ο ίδιος;"), items);
                    if (i < 0)
                    {
                        Status(SwimLang.T("Ο διεθνής αθλητής δεν καταχωρήθηκε (ο αγώνας κράτησε το όνομα των αποτελεσμάτων)."), false);
                        return;
                    }

                    if (i == items.Count - 1)
                    {
                        AddInternationalAthlete(e, true);
                        return;
                    }

                    UseInternationalAthlete((string)duplicates[i]["id"], a, e, false);
                    return;
                }

                if (!result.Ok || result.Raw == null || string.IsNullOrEmpty(result.Raw.Value<string>("id")))
                {
                    Status(SwimLang.T("Ο διεθνής αθλητής δεν καταχωρήθηκε: ") + (result.Error ?? SwimLang.T("χωρίς απάντηση")) + SwimLang.T(" (χρειάζεται το script 2.7.6 στο Google Sheet)."), true);
                    return;
                }

                UseInternationalAthlete(result.Raw.Value<string>("id"), a, e, result.Raw.Value<bool?>("created") ?? false);
            });
        }

        /// <summary>The international athlete of this code as the athlete of the race (in the local copy of the registry too).</summary>
        private void UseInternationalAthlete(string id, JObject a, SwimOfficialEntry e, bool created)
        {
            SwimAthlete athlete = Athletes.Find(id);
            if (athlete == null)
            {
                athlete = new SwimAthlete
                {
                    Id = id,
                    Name = (string)a["name"],
                    Gender = (string)a["gender"],
                    BirthYear = (string)a["birthYear"],
                    Club = (string)a["club"],
                    International = true,
                    Nation = (string)a["nation"],
                    WaId = e.IfId.Length > 0 ? e.IfId : null
                };
                Athletes.Athletes.Add(athlete);
                Athletes.BuildIndex();
                Athletes.Save();
            }

            SetAthlete(athlete);
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Διεθνής αθλητής: {0} ({1}) {2}."), athlete.Name, id,
                created ? SwimLang.T("καταχωρήθηκε στο φύλλο «Διεθνείς αθλητές»") : SwimLang.T("υπήρχε ήδη")), false);
        }
    }

    /// <summary>A small list to choose one item from (−1: none).</summary>
    public static class SwimChoiceDialog
    {
        public static int Choose(IWin32Window owner, string title, string question, IList<string> items)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                f.StartPosition = FormStartPosition.CenterParent;
                f.Width = 760;
                f.Height = 420;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                Label label = new Label { Text = question, Dock = DockStyle.Top, Height = 32, Padding = new Padding(8, 8, 8, 0) };
                ListBox list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
                foreach (string item in items)
                    list.Items.Add(item);
                if (list.Items.Count > 0)
                    list.SelectedIndex = 0;
                FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
                Button ok = new Button { Text = SwimLang.T("Επιλογή"), DialogResult = DialogResult.OK, AutoSize = true };
                Button cancel = new Button { Text = SwimLang.T("Άκυρο"), DialogResult = DialogResult.Cancel, AutoSize = true };
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);
                f.Controls.Add(list);
                f.Controls.Add(label);
                f.Controls.Add(buttons);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                list.DoubleClick += (s, e) => { f.DialogResult = DialogResult.OK; f.Close(); };
                return f.ShowDialog(owner) == DialogResult.OK ? list.SelectedIndex : -1;
            }
        }
    }
}
