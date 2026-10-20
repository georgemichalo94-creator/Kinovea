#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The race's details from the event of its results (since 2.8.2): a results link that names the event (the results of the
    /// European Aquatics championships), the title of pasted results, the title of the results page: the style, the distance, the
    /// round, the gender, the competition and its city, the pool of a short course competition.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>
        /// The change of the event the analyst refused ("race|results' event|race's event"): not asked again in the same action
        /// (🔗 or 📋: the link, then the title of the pasted results of the same event); asked again at the next one.
        /// </summary>
        private string declinedEvent;

        /// <summary>
        /// The event into the race's details: the distance, the style and the round as the results say (the analyst is asked first
        /// when the race has marks already and the distance, the style or the pool change: its checkpoints become those of the
        /// event; a change refused is not asked again in the same action); the gender, the competition and the city only when not
        /// typed (or one or two letters). What changed is said in the status. 'from': where it was read ("τον σύνδεσμο"). False when
        /// nothing changed.
        /// </summary>
        private bool ApplyEvent(SwimEventInfo ev, string from)
        {
            if (Race == null || ev == null || !ev.Any)
                return false;

            SwimRace r = Race;
            bool styleChange = ev.Style.Length > 0 && ev.Style != r.Style;
            bool poolChange = ev.Pool == 25 && r.Pool != 25 && !r.IsYards;
            bool distanceChange = ev.Distance > 0 && (ev.Distance != r.Distance || ev.Relay != r.Relay);
            if ((styleChange || poolChange || distanceChange) && r.Marks.Count > 0)
            {
                string evText = EventText(distanceChange ? ev.Distance : r.Distance, distanceChange ? ev.Relay : r.Relay,
                    styleChange ? ev.Style : r.Style, poolChange ? 25 : r.Pool);
                string raceText = EventText(r.Distance, r.Relay, r.Style, r.Pool);
                string key = (r.Id ?? "") + "|" + evText + "|" + raceText;
                bool yes = false;
                if (key != declinedEvent)
                {
                    string question = string.Format(CultureInfo.InvariantCulture,
                        SwimLang.T("Τα αποτελέσματα είναι για {0}· ο αγώνας είναι {1} και έχει ήδη σημάδια.\n\nΝα γίνει ο αγώνας {0}; (Τα σημεία ελέγχου γίνονται τα προεπιλεγμένα του αγωνίσματος.)"),
                        evText, raceText);
                    yes = MessageBox.Show(DialogOwner, question, SwimLang.T("Στοιχεία αγώνα"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                    declinedEvent = yes ? null : key;
                }

                if (!yes)
                    styleChange = poolChange = distanceChange = false;
            }

            List<string> done = new List<string>();
            if (styleChange)
                r.Style = ev.Style;
            if (poolChange)
                SwimPresets.ApplyPool(r, 1);
            if (distanceChange)
            {
                r.Distance = ev.Distance;
                r.Relay = ev.Relay;
            }

            bool structure = styleChange || poolChange || distanceChange;
            if (distanceChange || poolChange)
                r.Checkpoints = SwimPresets.DefaultCheckpoints(r.Distance, r.Pool, r.Relay);
            if (structure)
                done.Add(EventText(r.Distance, r.Relay, r.Style, r.Pool));

            if (ev.Round.Length > 0 && RaceRound(r.Round) != ev.Round)
            {
                r.Round = RoundName(ev.Round);
                done.Add(r.Round);
            }

            if ((ev.Gender == "M" || ev.Gender == "F") && string.IsNullOrWhiteSpace(r.AthleteGender))
            {
                r.AthleteGender = ev.Gender == "F" ? "Γυναικών" : "Ανδρών";
                done.Add(SwimLang.T(r.AthleteGender));
            }

            if (ev.Competition.Length > 0 && (r.Competition ?? "").Trim().Length < 3)
            {
                r.Competition = ev.Competition;
                done.Add(ev.Competition);
            }

            if (ev.Location.Length > 0 && (r.Location ?? "").Trim().Length < 3)
            {
                r.Location = ev.Location;
                done.Add(ev.Location);
            }

            if (done.Count == 0)
                return false;

            RaceInfoChanged(structure);
            if (form != null)
                form.RefreshAll();
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Στοιχεία αγώνα από {0}: {1}."), from, string.Join(" · ", done.ToArray())), false);
            return true;
        }

        /// <summary>"100μ. Ύπτιο (50μ.)", "4x100μ. Μικτή (25μ.)".</summary>
        private static string EventText(int distance, bool relay, string style, int pool)
        {
            string d = relay ? "4x" + (distance / 4).ToString(CultureInfo.InvariantCulture) : distance.ToString(CultureInfo.InvariantCulture);
            return d + SwimLang.T("μ. ") + SwimPresets.StyleName(style) + " (" + pool.ToString(CultureInfo.InvariantCulture) + SwimLang.T("μ.") + ")";
        }
    }
}
