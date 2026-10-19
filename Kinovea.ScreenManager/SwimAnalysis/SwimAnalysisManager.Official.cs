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
            public int Distance;
            public string Style = "";
            public bool Relay;
            public DateTime? Date;
            public string Competition = "";
            public string Round = "";
            public int Lane;
            public string AthleteId = "";
            public string Athlete = "";
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
            public SwimOfficialUnit Unit;
            public SwimOfficialEntry Entry;
            public string How = "";

            /// <summary>The competition as the source names it (Microplus).</summary>
            public string Source = "";
            public List<KeyValuePair<SwimOfficialUnit, SwimOfficialEntry>> Choices = new List<KeyValuePair<SwimOfficialUnit, SwimOfficialEntry>>();
            public string Error;
        }

        private BackgroundWorker officialWorker;

        /// <summary>
        /// The button "🔗 Επίσημα αποτελέσματα" of the race tab: the race found in the official results (the link typed, else the KOE
        /// live results by the date and the name of the competition) and, once the analyst agrees, its official times in the analysis.
        /// </summary>
        public void LinkOfficialResults(string link)
        {
            if (Race == null)
                return;
            if (officialWorker != null && officialWorker.IsBusy)
            {
                Status("Η αναζήτηση στα επίσημα αποτελέσματα τρέχει ήδη.", false);
                return;
            }

            Race.ResultsLink = (link ?? "").Trim();
            OfficialQuery q = new OfficialQuery
            {
                Link = Race.ResultsLink,
                Distance = Race.Distance,
                Style = Race.Style,
                Relay = Race.Relay,
                Date = ParseRaceDate(Race.Date),
                Competition = Race.Competition ?? "",
                Round = Race.Round ?? "",
                Lane = Race.Lane,
                AthleteId = (Race.AthleteId ?? "").Trim(),
                Athlete = (Race.Athlete ?? "").Trim(),
                Gender = SwimReference.NormalizeGender(Race.AthleteGender),
                Final = Computed != null ? Computed.FinalTime : null,
                Registry = Athletes
            };
            int born;
            if (int.TryParse(Race.AthleteBirthYear, NumberStyles.Integer, CultureInfo.InvariantCulture, out born))
                q.BirthYear = born;

            Status("🔗 Αναζήτηση του αγώνα στα επίσημα αποτελέσματα…", false);
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
                    Status("🔗 Τα επίσημα αποτελέσματα δεν διαβάστηκαν: " + e.Error.Message, true);
                    return;
                }

                OfferOfficial((OfficialFound)e.Result, q);
            };
            officialWorker.RunWorkerAsync();
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
                    found.Error = "η σελίδα της Microplus δεν διαβάστηκε (έλεγξε τον σύνδεσμο και τη σύνδεση στο διαδίκτυο)";
                    return found;
                }

                found.Source = mp.Name;
                units = SwimOfficialResults.MicroplusSchedule(mp);
            }

            // A World Aquatics competition (worldaquatics.com/competitions/4725/…): the event, then its heats with their results.
            int worldAquatics = SwimOfficialResults.WorldAquaticsCompetition(q.Link);
            if (units == null && worldAquatics > 0)
            {
                found.Source = "World Aquatics";
                units = new List<SwimOfficialUnit>();
                foreach (SwimOfficialUnit ev in SwimOfficialResults.WorldAquaticsEvents(worldAquatics)
                    .Where(u => u.Distance == q.Distance && u.Style == q.Style && u.Relay == q.Relay && (q.Gender.Length == 0 || u.Gender == q.Gender || u.Gender == "X")))
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
                found.Error = "ο σύνδεσμος δεν είναι των live αποτελεσμάτων της ΚΟΕ (live.koe.org.gr), σελίδα αποτελεσμάτων της Microplus ή " +
                    "διοργάνωση της World Aquatics (worldaquatics.com/competitions/…)· άφησέ τον κενό για αναζήτηση με την ημερομηνία";
                return found;
            }
            else if (units == null && !string.IsNullOrWhiteSpace(q.Link) && !int.TryParse(q.Link.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out code))
            {
                found.Error = "ο κωδικός της διοργάνωσης δεν διαβάζεται";
                return found;
            }

            if (units == null && code == 0)
            {
                // By the date (the season of a competition: the year it ends in; October to December belong to the next one) and the name.
                if (!q.Date.HasValue)
                {
                    found.Error = "γράψε την ημερομηνία του αγώνα (στοιχεία αγώνα) ή επικόλλησε τον σύνδεσμο της διοργάνωσης από το live.koe.org.gr";
                    return found;
                }

                DateTime day = q.Date.Value.Date;
                List<SwimKoeCompetition> all = new List<SwimKoeCompetition>();
                foreach (int season in new[] { day.Year, day.Year + 1 }.Distinct())
                    all.AddRange(SwimOfficialResults.KoeCompetitions(season));
                List<SwimKoeCompetition> on = all.Where(c => c.From.HasValue && c.From.Value.Date <= day.AddDays(1) &&
                    (c.To ?? c.From).Value.Date >= day.AddDays(-1)).GroupBy(c => c.Code).Select(g => g.First()).ToList();
                if (on.Count == 0)
                {
                    found.Error = string.Format(CultureInfo.InvariantCulture, "δεν βρέθηκε διοργάνωση της ΚΟΕ στις {0:d/M/yyyy}· επικόλλησε τον σύνδεσμό της", day);
                    return found;
                }

                if (on.Count > 1 && !string.IsNullOrWhiteSpace(q.Competition))
                {
                    on = on.OrderByDescending(c => Math.Max(NameLike(q.Competition, c.Name), NameLike(q.Competition, c.NameLatin))).ToList();
                    double best = Math.Max(NameLike(q.Competition, on[0].Name), NameLike(q.Competition, on[0].NameLatin));
                    double second = Math.Max(NameLike(q.Competition, on[1].Name), NameLike(q.Competition, on[1].NameLatin));
                    if (best >= 0.75 && best - second >= 0.1)
                        on = on.Take(1).ToList();
                }

                found.Competitions = on;
                if (on.Count > 1)
                    return found;
                found.Competition = on[0];
                code = on[0].Code;
            }

            // The units of the event, then the round (when typed), the day of the race and the unit of the link.
            if (units == null)
                units = SwimOfficialResults.KoeSchedule(code);
            units = units.Where(u => u.Distance == q.Distance && u.Style == q.Style && u.Relay == q.Relay).ToList();
            if (q.Gender == "M" || q.Gender == "F")
                units = units.Where(u => u.Gender == "" || u.Gender == q.Gender || u.Gender == "X").ToList();
            if (!string.IsNullOrEmpty(unitCode) && units.Any(u => string.Equals(u.UnitCode, unitCode, StringComparison.OrdinalIgnoreCase)))
                units = units.Where(u => string.Equals(u.UnitCode, unitCode, StringComparison.OrdinalIgnoreCase)).ToList();
            string round = RaceRound(q.Round);
            if (round.Length > 0 && units.Any(u => u.Round == round || (round == "final" && u.Round == "timed")))
                units = units.Where(u => u.Round == round || (round == "final" && u.Round == "timed")).ToList();
            if (q.Date.HasValue && units.Any(u => u.Date.HasValue && u.Date.Value.Date == q.Date.Value.Date))
                units = units.Where(u => !u.Date.HasValue || u.Date.Value.Date == q.Date.Value.Date).ToList();
            if (units.Count == 0)
            {
                found.Error = "η διοργάνωση δεν έχει το αγώνισμα " + q.Distance.ToString(CultureInfo.InvariantCulture) + "μ. " + SwimPresets.StyleShort(q.Style) +
                    (q.Gender.Length > 0 ? " (" + (q.Gender == "F" ? "γυναικών" : "ανδρών") + ")" : "");
                return found;
            }

            if (units.Count > 60)
                units = units.Take(60).ToList();
            foreach (SwimOfficialUnit u in units)
            {
                if (u.Source == "microplus")
                    SwimOfficialResults.MicroplusResults(u);
                else if (u.Source == "koe")
                    SwimOfficialResults.KoeResults(u);
            }

            List<KeyValuePair<SwimOfficialUnit, SwimOfficialEntry>> entries = units
                .SelectMany(u => u.Entries.Where(e => e.Time.HasValue).Select(e => new KeyValuePair<SwimOfficialUnit, SwimOfficialEntry>(u, e))).ToList();
            List<KeyValuePair<SwimOfficialUnit, SwimOfficialEntry>> chosen = null;

            // 1. The registry number (the KOE results carry the federation's number of the Greek athletes; an international athlete of
            // the registry has "WA" and his World Aquatics code).
            if (q.AthleteId.Length > 0)
            {
                chosen = entries.Where(x => x.Value.Code == q.AthleteId || (x.Value.IfId.Length > 0 && WorldAquaticsId(x.Value.IfId) == q.AthleteId)).ToList();
                found.How = "με τον αριθμό μητρώου";
            }

            // 2. The name (Greek or Latin letters) and the year of birth.
            if ((chosen == null || chosen.Count == 0) && q.Athlete.Length > 0)
            {
                string family, given;
                SwimNameMatch.Split(q.Athlete, out family, out given);
                chosen = entries.Where(x =>
                    (q.BirthYear == 0 || x.Value.BirthYear == 0 || x.Value.BirthYear == q.BirthYear) &&
                    Math.Max(SwimNameMatch.Similarity(family, given, x.Value.FamilyName, x.Value.GivenName),
                             SwimNameMatch.Similarity(family, given, x.Value.FamilyNameLatin, x.Value.GivenNameLatin)) >= SwimNameMatch.SameName).ToList();
                found.How = "με το όνομα";
            }

            // 3. The lane, and the final time read (the closest within a second).
            if ((chosen == null || chosen.Count == 0) && q.Lane > 0)
            {
                chosen = entries.Where(x => x.Value.Lane == q.Lane).ToList();
                if (q.Final.HasValue && chosen.Count > 1)
                {
                    List<KeyValuePair<SwimOfficialUnit, SwimOfficialEntry>> near = chosen
                        .Where(x => Math.Abs(x.Value.Time.Value - q.Final.Value) <= 1.0)
                        .OrderBy(x => Math.Abs(x.Value.Time.Value - q.Final.Value)).ToList();
                    if (near.Count > 0)
                        chosen = near.Count > 1 && Math.Abs(near[1].Value.Time.Value - q.Final.Value) - Math.Abs(near[0].Value.Time.Value - q.Final.Value) < 0.2
                            ? near : near.Take(1).ToList();
                }

                found.How = q.Final.HasValue ? "με τον διάδρομο και τον χρόνο που διαβάστηκε" : "με τον διάδρομο";
            }

            if (chosen == null || chosen.Count == 0)
            {
                found.Error = q.AthleteId.Length > 0 || q.Athlete.Length > 0
                    ? "ο αθλητής δεν βρέθηκε στο αγώνισμα (έλεγξε τη φάση, ή διάλεξε από τη λίστα με τον διάδρομο)"
                    : "διάλεξε πρώτα τον αθλητή ή γράψε τον διάδρομο";
                found.Choices = q.Lane > 0 ? entries.Where(x => x.Value.Lane == q.Lane).ToList() : new List<KeyValuePair<SwimOfficialUnit, SwimOfficialEntry>>();
                return found;
            }

            if (chosen.Count > 1)
            {
                // Heats and final of the same swimmer: the round typed decides, else the analyst.
                found.Choices = chosen;
                return found;
            }

            found.Unit = chosen[0].Key;
            found.Entry = chosen[0].Value;
            return found;
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
            string title = "🔗 Επίσημα αποτελέσματα";
            if (found.Competitions.Count > 1)
            {
                Status("🔗 Την ίδια μέρα έγιναν " + found.Competitions.Count.ToString(CultureInfo.InvariantCulture) + " διοργανώσεις: διάλεξε.", false);
                int i = SwimChoiceDialog.Choose(DialogOwner, title, "Ποια διοργάνωση είναι;", found.Competitions.Select(c => c.ToString()).ToList());
                if (i < 0)
                    return;
                LinkOfficialResults(found.Competitions[i].Code.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (found.Entry == null && found.Choices.Count > 0)
            {
                List<string> items = found.Choices.Select(x => OfficialLine(x.Key, x.Value)).ToList();
                int i = SwimChoiceDialog.Choose(DialogOwner, title, (found.Error != null ? "Δεν βρέθηκε μόνο ένας: " : "") + "ποιος είναι ο αθλητής του βίντεο;", items);
                if (i < 0)
                {
                    Status("🔗 Δεν μπήκαν επίσημοι χρόνοι.", false);
                    return;
                }

                found.Unit = found.Choices[i].Key;
                found.Entry = found.Choices[i].Value;
                found.How = "από τη λίστα";
            }

            if (found.Entry == null)
            {
                Status("🔗 " + (found.Error ?? "δεν βρέθηκε ο αγώνας") + ".", true);
                return;
            }

            SwimOfficialEntry e = found.Entry;
            SwimAthlete athlete = RegistryAthlete(e, q.Registry);
            List<double> times = e.OfficialTimes();
            StringBuilder b = new StringBuilder();
            b.Append("Βρέθηκε ").Append(found.How).Append(":\n\n").Append(OfficialLine(found.Unit, e)).Append("\n\n");
            b.Append("Επίσημοι χρόνοι: ").Append(SwimRace.FormatOfficial(times));
            if (e.Reaction.HasValue)
                b.Append(" · αντίδραση ").Append(e.Reaction.Value.ToString("0.00", CultureInfo.InvariantCulture));
            b.Append('\n');
            if (athlete != null && athlete.Id != (Race.AthleteId ?? ""))
                b.Append("Αθλητής από το μητρώο: ").Append(athlete.Name).Append(" (").Append(athlete.Id).Append(")\n");
            if (Race.Lane == 0 || Race.Lane != e.Lane)
                b.Append("Διάδρομος: ").Append(e.Lane.ToString(CultureInfo.InvariantCulture)).Append('\n');
            b.Append("\nΝα μπουν στην ανάλυση; (Ο τερματισμός και οι τοίχοι στους επίσημους χρόνους, όσα σημάδια δεν έχεις ελέγξει μετακινούνται.)");
            if (MessageBox.Show(DialogOwner, b.ToString(), title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                Status("🔗 Δεν μπήκαν επίσημοι χρόνοι.", false);
                return;
            }

            if (athlete != null && athlete.Id != (Race.AthleteId ?? ""))
                SetAthlete(athlete);
            if (e.Lane > 0)
                Race.Lane = e.Lane;
            if (string.IsNullOrWhiteSpace(Race.Athlete))
                Race.Athlete = e.Name.Length > 0 ? e.Name : e.NameLatin;
            if (e.Reaction.HasValue)
                Race.OfficialReaction = e.Reaction;
            OfficialChanged(times);
            if (form != null)
                form.RefreshAll();
        }

        /// <summary>"100μ. fs · QUALIFIERS 3 · διάδρομος 4 · ΟΝΟΜΑ (σωματείο, 2009) · 54.54".</summary>
        private static string OfficialLine(SwimOfficialUnit u, SwimOfficialEntry e)
        {
            string name = e.Name.Length > 0 ? e.Name : e.NameLatin;
            string about = string.Join(", ", new[] { e.Club, e.Nation, e.BirthYear > 0 ? e.BirthYear.ToString(CultureInfo.InvariantCulture) : "" }
                .Where(x => !string.IsNullOrEmpty(x)).ToArray());
            int heat = e.Heat > 0 ? e.Heat : u.Heat;
            return string.Format(CultureInfo.InvariantCulture, "{0} · {1}{2} · διάδρομος {3} · {4}{5} · {6}", u.Event, u.Phase,
                heat > 0 ? " " + heat.ToString(CultureInfo.InvariantCulture) : "", e.Lane, name,
                about.Length > 0 ? " (" + about + ")" : "", e.Time.HasValue ? SwimReference.FormatTime(e.Time.Value) : e.Status);
        }

        /// <summary>The registry's athlete of a result: his number (the KOE results), else his name, gender and year of birth.</summary>
        private static SwimAthlete RegistryAthlete(SwimOfficialEntry e, SwimAthleteList registry)
        {
            if (registry == null || e == null)
                return null;

            SwimAthlete byCode = (e.Code.Length > 0 ? registry.Find(e.Code) : null) ?? (e.IfId.Length > 0 ? registry.Find(WorldAquaticsId(e.IfId)) : null);
            if (byCode != null)
            {
                string f, g;
                SwimNameMatch.Split(byCode.Name, out f, out g);
                if (SwimNameMatch.Similarity(f, g, e.FamilyName, e.GivenName) >= SwimNameMatch.SameName ||
                    SwimNameMatch.Similarity(f, g, e.FamilyNameLatin, e.GivenNameLatin) >= SwimNameMatch.SameName)
                    return byCode;
            }

            List<SwimAthlete> like = registry.Athletes.Where(a =>
            {
                int born;
                if (e.BirthYear > 0 && int.TryParse(a.BirthYear, NumberStyles.Integer, CultureInfo.InvariantCulture, out born) && born != e.BirthYear)
                    return false;
                string gender = SwimReference.NormalizeGender(a.Gender);
                if (e.Gender.Length > 0 && gender.Length > 0 && gender != e.Gender)
                    return false;
                string f, g;
                SwimNameMatch.Split(a.Name, out f, out g);
                return SwimNameMatch.Similarity(f, g, e.FamilyName, e.GivenName) >= SwimNameMatch.SameName ||
                    SwimNameMatch.Similarity(f, g, e.FamilyNameLatin, e.GivenNameLatin) >= SwimNameMatch.SameName;
            }).Take(2).ToList();
            return like.Count == 1 ? like[0] : null;
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
                Button ok = new Button { Text = "Επιλογή", DialogResult = DialogResult.OK, AutoSize = true };
                Button cancel = new Button { Text = "Άκυρο", DialogResult = DialogResult.Cancel, AutoSize = true };
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
