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
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The official results when the program cannot read them itself (since 2.8.1): the swimmer's row pasted by the analyst from
    /// the results page (the same check and the same use as the results read on the web), and an international athlete found in the
    /// public search of World Aquatics for the registry.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        private BackgroundWorker waWorker;

        /// <summary>The button "📋 Επικόλληση": the results pasted, whatever the link of the race.</summary>
        public void PasteOfficialResults()
        {
            PasteOfficialResults(null);
        }

        /// <summary>
        /// The window of the paste (the text copied from the results page already in it when the clipboard holds results), then what
        /// was read checked and put in as the official results of the web are (OfferOfficial). 'intro': why it opens by itself.
        /// </summary>
        private void PasteOfficialResults(string intro)
        {
            if (Race == null)
                return;

            string clip = "";
            try
            {
                if (Clipboard.ContainsText())
                    clip = Clipboard.GetText();
            }
            catch (Exception)
            {
                clip = "";
            }

            if (!LooksLikeResults(clip))
                clip = "";

            SwimPasteRecord chosen;
            bool byName;
            using (SwimPasteDialog dialog = new SwimPasteDialog(intro, clip, Race.Distance, Race.Athlete, RaceLaneNumber()))
            {
                if (dialog.ShowDialog(DialogOwner) != DialogResult.OK || dialog.Chosen == null || !dialog.Chosen.Time.HasValue)
                {
                    Status(SwimLang.T("📋 Δεν μπήκαν επίσημοι χρόνοι."), false);
                    return;
                }

                chosen = dialog.Chosen;
                byName = dialog.ChosenByName;
            }

            if (byName)
                CompleteInitial(chosen, Race.Athlete);

            OfficialQuery q = NewOfficialQuery(Race.ResultsLink, false);
            SwimOfficialEntry e = PasteEntry(chosen, q.Gender);
            SwimOfficialUnit unit = new SwimOfficialUnit
            {
                Source = "paste",
                Event = Race.Distance.ToString(CultureInfo.InvariantCulture) + SwimLang.T("μ. ") + SwimPresets.StyleShort(Race.Style),
                Distance = Race.Distance,
                Style = Race.Style,
                Relay = Race.Relay,
                Gender = q.Gender,
                Phase = Race.Round ?? "",
                Round = RaceRound(Race.Round),
                Date = q.Date
            };

            OfficialFound found = new OfficialFound
            {
                Source = SwimLang.T("Επικόλληση αποτελεσμάτων"),
                Unit = unit,
                Entry = e,
                How = SwimLang.T("από την επικόλληση"),
                ByAthlete = byName || string.IsNullOrWhiteSpace(Race.Athlete)
            };

            OfferOfficialWhenFree(found, q, Race.Id);
        }

        /// <summary>Text that may be results: a time (12.34, 1:02.34) and letters, not a whole book.</summary>
        private static bool LooksLikeResults(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && text.Length <= 200000 &&
                Regex.IsMatch(text, @"\d{1,2}[.,]\d{2}") && Regex.IsMatch(text, @"\p{L}{2,}");
        }

        /// <summary>
        /// A given name published as its initial ("M." of "M. ROSSINI") made the race's athlete's when he is the swimmer (the same family
        /// name and initial): the whole name for the registry and its matching.
        /// </summary>
        private static void CompleteInitial(SwimPasteRecord r, string athlete)
        {
            string letters = new string(r.GivenName.Where(char.IsLetter).ToArray());
            if (letters.Length == 0 || letters.Length > 3 || !r.GivenName.Contains(".") || string.IsNullOrWhiteSpace(athlete))
                return;

            string a1, a2;
            SwimNameMatch.Split(athlete, out a1, out a2);
            foreach (string[] pair in new[] { new[] { a1, a2 }, new[] { a2, a1 } })
            {
                string given = SwimNameMatch.Latin(pair[1]);
                if (pair[1].Length > 1 && given.Length > 0 && SwimNameMatch.FamilySimilarity(pair[0], r.FamilyName) >= 0.95 &&
                    given[0] == SwimNameMatch.Latin(letters.Substring(0, 1))[0])
                {
                    r.GivenName = pair[1];
                    return;
                }
            }
        }

        /// <summary>The pasted swimmer as a result of the web: his names (Greek or Latin letters), country, year, club, times, lane.</summary>
        private SwimOfficialEntry PasteEntry(SwimPasteRecord r, string gender)
        {
            SwimOfficialEntry e = new SwimOfficialEntry
            {
                Heat = 0,
                Lane = r.Lane >= 0 ? r.Lane : RaceLaneNumber() > 0 ? RaceLaneNumber() : -1,
                Nation = r.Nation,
                Club = r.Club,
                BirthYear = r.BirthYear,
                Gender = gender ?? "",
                Time = r.Time,
                Splits = new List<double>(r.Splits),
                Reaction = r.Reaction,
                Status = r.Status
            };

            if (SwimNameMatch.HasGreek(r.FamilyName + r.GivenName))
            {
                e.FamilyName = r.FamilyName;
                e.GivenName = r.GivenName;
            }
            else
            {
                e.FamilyNameLatin = r.FamilyName;
                e.GivenNameLatin = r.GivenName;
            }

            return e;
        }

        /// <summary>
        /// The button "🌐" of the athlete: the swimmer of this name in the public search of World Aquatics (several: the analyst
        /// chooses), his code of the results looked for, then registered as an international athlete and made the race's athlete.
        /// </summary>
        public void SearchWorldAquatics(string name)
        {
            if (Race == null)
                return;

            name = (name ?? "").Trim();
            if (name.Length < 3)
            {
                Status(SwimLang.T("🌐 Γράψε το όνομα του αθλητή (στην αναζήτηση ή στο «Όνομα στο report») και πάτησε ξανά 🌐."), true);
                return;
            }

            if (waWorker != null && waWorker.IsBusy)
            {
                Status(SwimLang.T("🌐 Η αναζήτηση στη World Aquatics τρέχει ήδη."), false);
                return;
            }

            string raceId = Race.Id;
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("🌐 Αναζήτηση του «{0}» στη World Aquatics…"), name), false);
            waWorker = new BackgroundWorker();
            waWorker.DoWork += (s, e) => e.Result = SwimOfficialResults.WorldAquaticsAthletes(name);
            waWorker.RunWorkerCompleted += (s, e) =>
            {
                if (Race == null || Race.Id != raceId)
                    return;
                if (e.Error != null)
                    log.Error("The World Aquatics search of athletes failed.", e.Error);
                List<SwimWaAthlete> list = e.Error == null ? e.Result as List<SwimWaAthlete> : null;
                if (list == null || list.Count == 0)
                {
                    Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("🌐 Δεν βρέθηκε κολυμβητής «{0}» στη World Aquatics (έλεγξε το όνομα και τη σύνδεση στο διαδίκτυο)."), name), true);
                    return;
                }

                // The race's gender first, then the most alike names.
                string gender = SwimReference.NormalizeGender(Race.AthleteGender);
                list = list.OrderByDescending(a => gender.Length > 0 && a.Gender == gender)
                    .ThenByDescending(a => WaNameScore(name, a)).ToList();
                int i = SwimChoiceDialog.Choose(DialogOwner, SwimLang.T("🌐 World Aquatics"), SwimLang.T("Ποιος είναι ο αθλητής του αγώνα;"),
                    list.Select(a => a.ToString()).ToList());
                if (i < 0)
                {
                    Status(SwimLang.T("🌐 Δεν επιλέχθηκε αθλητής."), false);
                    return;
                }

                RegisterWorldAquatics(list[i], raceId);
            };
            waWorker.RunWorkerAsync();
        }

        private static double WaNameScore(string typed, SwimWaAthlete a)
        {
            string f, g;
            SwimNameMatch.Split(typed, out f, out g);
            return Math.Max(SwimNameMatch.Similarity(f, g, a.LastName, a.FirstName), SwimNameMatch.Similarity(g, f, a.LastName, a.FirstName));
        }

        /// <summary>The chosen swimmer: his code of the results looked for (background), then the registry (or the race only).</summary>
        private void RegisterWorldAquatics(SwimWaAthlete a, string raceId)
        {
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("🌐 {0}: ο κωδικός του στα αποτελέσματα της World Aquatics…"), a.FullName), false);
            BackgroundWorker gms = new BackgroundWorker();
            gms.DoWork += (s, e) => e.Result = SwimOfficialResults.WorldAquaticsGmsId(a);
            gms.RunWorkerCompleted += (s, e) =>
            {
                if (Race == null || Race.Id != raceId)
                    return;

                string code = e.Error == null ? (e.Result as string ?? "") : "";
                if (e.Error != null)
                    log.Error("The World Aquatics code of the athlete could not be read.", e.Error);
                else
                    log.Info(code.Length > 0 ? "World Aquatics: the athlete's code of the results found." : "World Aquatics: no code of the results found for the athlete.");
                SwimOfficialEntry entry = new SwimOfficialEntry
                {
                    FamilyNameLatin = a.LastName,
                    GivenNameLatin = a.FirstName,
                    Nation = a.Nation,
                    BirthYear = a.BirthYear,
                    Gender = a.Gender,
                    IfId = code
                };

                // Already in the registry (his code, or his name, year, gender and country): he is the race's athlete.
                SwimAthlete known = RegistryAthlete(entry, Athletes);
                if (known != null)
                {
                    SetAthlete(known);
                    return;
                }

                if (Settings == null || !Settings.IsConfigured)
                {
                    // No sheet: the race takes his name, gender and year (the registry when the connection is there).
                    Race.Athlete = entry.NameLatin;
                    Race.AthleteGender = a.Gender == "F" ? "Γυναικών" : a.Gender == "M" ? "Ανδρών" : Race.AthleteGender;
                    if (a.BirthYear > 0)
                        Race.AthleteBirthYear = a.BirthYear.ToString(CultureInfo.InvariantCulture);
                    Changed(false);
                    if (form != null)
                        form.RefreshAll();
                    Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("🌐 Αθλητής: {0} ({1}). Χωρίς σύνδεση με το Google Sheet δεν καταχωρήθηκε στους διεθνείς αθλητές."), entry.NameLatin, a.Nation), true);
                    return;
                }

                AddInternationalAthlete(entry, false);
            };
            gms.RunWorkerAsync();
        }
    }
}
