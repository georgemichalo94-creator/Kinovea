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
            declinedEvent = null;
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

            // The paste replaces results found on the web and waiting for the end of the reading.
            CancelPendingOffer();

            // A link that names the event: the race's details from it (since 2.8.2). Only from the button: the window that opens by
            // itself comes from 🔗, which did it already (the analyst is not asked twice).
            if (intro == null)
                ApplyEvent(SwimResultsPaste.EventFromLink(Race.ResultsLink), SwimLang.T("τον σύνδεσμο"));

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
            SwimEventInfo titleEvent;
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
                titleEvent = dialog.Event;
            }

            // The event in the title of the pasted results (since 2.8.2): the race's details from it (its times were read for it).
            // (2.15.0) Here the distance, the style and the pool (asked when the race has marks); the round, the gender and the
            // competition when its times are taken (OfferOfficial).
            ApplyEvent(titleEvent, SwimLang.T("τον τίτλο των αποτελεσμάτων"), false);

            OfficialQuery q = NewOfficialQuery(Race.ResultsLink, false);
            // (2.15.0) The gender of the title for the search (the athletes of the registry, the international ones kept) when the race
            // has none: the race gets it when the times are taken.
            if (q.Gender.Length == 0 && titleEvent != null && (titleEvent.Gender == "M" || titleEvent.Gender == "F"))
                q.Gender = titleEvent.Gender;

            // Found by the race's athlete's name: him (another country is another swimmer of the same name); an initial for his given
            // name ("M. ROSSI") made his given name.
            if (byName && q.Nation.Length > 0 && chosen.Nation.Length > 0 && !string.Equals(q.Nation, chosen.Nation, StringComparison.OrdinalIgnoreCase))
                byName = false;
            if (byName)
                CompleteInitial(chosen, Race.Athlete);

            SwimOfficialEntry e = PasteEntry(chosen, q.Gender, byName);
            SwimOfficialUnit unit = new SwimOfficialUnit
            {
                Source = "paste",
                Event = Race.Distance.ToString(CultureInfo.InvariantCulture) + SwimLang.T("μ. ") + SwimPresets.StyleShort(Race.Style),
                Distance = Race.Distance,
                Style = Race.Style,
                Relay = Race.Relay,
                Gender = q.Gender,
                Phase = titleEvent != null && titleEvent.Round.Length > 0 ? RoundName(titleEvent.Round) : Race.Round ?? "",
                Round = titleEvent != null && titleEvent.Round.Length > 0 ? titleEvent.Round : RaceRound(Race.Round),
                Date = q.Date
            };

            OfficialFound found = new OfficialFound
            {
                Source = SwimLang.T("Επικόλληση αποτελεσμάτων"),
                Unit = unit,
                Entry = e,
                How = SwimLang.T("από την επικόλληση"),
                ByAthlete = byName,
                TitleEvent = titleEvent
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

            foreach (string[] pair in SwimResultsPaste.NameSplits(athlete))
            {
                // (The given name in the letters of the pasted row: a Greek name for a Greek row, a Latin one for a Latin row.)
                string given = SwimNameMatch.Latin(pair[1]);
                if (pair[1].Length > 1 && given.Length > 0 && SwimNameMatch.HasGreek(pair[1]) == SwimNameMatch.HasGreek(r.FamilyName) &&
                    SwimNameMatch.FamilySimilarity(pair[0], r.FamilyName) >= 0.95 && given[0] == SwimNameMatch.Latin(letters.Substring(0, 1))[0])
                {
                    r.GivenName = pair[1];
                    return;
                }
            }
        }

        /// <summary>
        /// The pasted swimmer as a result of the web: his names (Greek or Latin letters), country, year, club, times, lane; found by the
        /// race's athlete's name, the athlete's registry code (the registry's athlete is him, whatever the spelling pasted).
        /// </summary>
        private SwimOfficialEntry PasteEntry(SwimPasteRecord r, string gender, bool byName)
        {
            SwimOfficialEntry e = new SwimOfficialEntry
            {
                Heat = 0,
                Code = byName ? (Race.AthleteId ?? "").Trim() : "",
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
                    // No sheet: the race takes his name, gender and year (the registry when the connection is there); the details of
                    // the athlete before him go.
                    Race.Athlete = entry.NameLatin;
                    Race.AthleteId = "";
                    Race.AthleteCategory = "";
                    Race.AthleteClub = "";
                    Race.AthleteGender = a.Gender == "F" ? "Γυναικών" : a.Gender == "M" ? "Ανδρών" : "";
                    Race.AthleteBirthYear = a.BirthYear > 0 ? a.BirthYear.ToString(CultureInfo.InvariantCulture) : "";
                    Changed(false);
                    ScheduleSend();
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
