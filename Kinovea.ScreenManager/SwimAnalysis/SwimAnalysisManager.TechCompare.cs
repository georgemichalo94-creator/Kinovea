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
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.22.0) The comparisons of the report of a technique analysis (training): with a previous technique analysis of the athlete
    /// ("auto": the newest one before, "best": the fastest, or one chosen) and with elite (a final kept on this computer, as the mean of its
    /// swimmers or its winner, or an elite technique analysis of the Google Sheet). Each alone or both: the report (Apps Script 2.17.0)
    /// puts them in the numbers, the tiles, the speed chart, the evaluation and the phases.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>The technique analyses of the athlete in the Google Sheet (same style and pool), the newest first.</summary>
        public List<SwimTechAnalysisItem> TechCandidates { get; private set; } = new List<SwimTechAnalysisItem>();

        /// <summary>The elite technique analyses of the Google Sheet (sheet «Elite τεχνική»), of the style of the analysis.</summary>
        public List<SwimTechElite> TechEliteRefs { get; private set; } = new List<SwimTechElite>();

        /// <summary>What the lists of the comparison have (or why they are empty).</summary>
        public string TechCompareMessage { get; private set; } = "";

        /// <summary>«Ως elite αναφορά…»: the analysis waits for its sending to the Google Sheet, then becomes an elite reference.</summary>
        private string techEliteRace;
        private string techEliteName;

        /// <summary>The finals of this computer as the elite of this analysis: each as the mean of its swimmers and as its winner.</summary>
        public List<SwimTechElite> TechEliteFinals()
        {
            return Race == null || !Race.IsTraining ? new List<SwimTechElite>() : SwimTechEliteFinals.For(Finals, Race);
        }

        /// <summary>The analysis that "auto" (best = false) or "best" gives now (as the report picks it), null if none.</summary>
        public SwimTechAnalysisItem TechAutoPrev(bool best)
        {
            if (Race == null)
                return null;

            // As the report: of the turns when the analysis has turns, else of the starts.
            bool turns = TechPhases.Any(p => p.Kind == SwimPhaseKind.Turn);
            return SwimTechEliteFinals.Pick(TechCandidates, Race, turns, best);
        }

        /// <summary>The technique analyses of the athlete and the elite technique analyses, from the Google Sheet.</summary>
        public void RefreshTechCandidates()
        {
            if (Race == null || !Race.IsTraining)
                return;

            if (!Settings.IsConfigured)
            {
                TechCandidates = new List<SwimTechAnalysisItem>();
                TechEliteRefs = new List<SwimTechElite>();
                TechCompareMessage = SwimLang.T("Για τη σύγκριση με προηγούμενες αναλύσεις χρειάζεται η σύνδεση με το Google Sheet (καρτέλα Σύνδεση).");
                if (form != null)
                    form.RefreshTechCompare();
                return;
            }

            JObject query = new JObject();
            query["tech"] = true;
            query["athleteId"] = Race.AthleteId ?? "";
            query["athlete"] = Race.Athlete ?? "";
            query["style"] = Race.Style;
            query["pool"] = Race.Pool;
            query["poolUnit"] = Race.IsYards ? SwimRace.UnitYards : SwimRace.UnitMeters;
            query["raceId"] = Race.Id;
            string raceId = Race.Id;
            bool noAthlete = string.IsNullOrWhiteSpace(Race.AthleteId) && string.IsNullOrWhiteSpace(Race.Athlete);
            TechCompareMessage = SwimLang.T("Φόρτωση των αναλύσεων τεχνικής…");
            if (form != null)
                form.RefreshTechCompare();

            client.GetAnalyses(Settings.WebAppUrl, Settings.Token, query, result =>
            {
                if (Race == null || Race.Id != raceId)
                    return;

                List<SwimTechAnalysisItem> list = new List<SwimTechAnalysisItem>();
                List<SwimTechElite> refs = new List<SwimTechElite>();
                if (result.Ok && result.Raw != null && result.Raw["techElites"] is JArray)
                {
                    JArray analyses = result.Raw["analyses"] as JArray;
                    if (analyses != null)
                    {
                        foreach (JObject o in analyses.OfType<JObject>())
                        {
                            string id = o.Value<string>("id") ?? "";
                            if (id.Length == 0 || id == raceId)
                                continue;
                            list.Add(new SwimTechAnalysisItem
                            {
                                Id = id,
                                Label = o.Value<string>("label") ?? id,
                                Date = o.Value<string>("date") ?? "",
                                Key = Number(o["key"]),
                                Turns = Number(o["turns"]),
                                Starts = Number(o["starts"]),
                                Turn15 = Value(o["turn15"]),
                                Start15 = Value(o["start15"])
                            });
                        }
                    }

                    foreach (JObject o in ((JArray)result.Raw["techElites"]).OfType<JObject>())
                    {
                        string id = o.Value<string>("id") ?? "";
                        if (id.Length == 0 || id == raceId)
                            continue;
                        refs.Add(new SwimTechElite
                        {
                            Type = SwimTechElite.TypeRef,
                            Id = id,
                            Label = o.Value<string>("label") ?? id,
                            Style = o.Value<string>("style") ?? "",
                            Gender = o.Value<string>("gender") ?? ""
                        });
                    }

                    if (noAthlete)
                        TechCompareMessage = SwimLang.T("Επίλεξε αθλητή για τη σύγκριση με προηγούμενη ανάλυση.");
                    else
                        TechCompareMessage = list.Count == 0 ? SwimLang.T("Καμία άλλη ανάλυση τεχνικής του αθλητή σε αυτό το στυλ και την πισίνα.") :
                            string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0} αναλύσεις τεχνικής του αθλητή."), list.Count);
                }
                else if (result.Ok || IsUnknownAction(result))
                {
                    // An Apps Script before 2.17.0 answers with the analyses of the races (or does not know the action).
                    TechCompareMessage = SwimLang.T("Για τη σύγκριση των αναλύσεων τεχνικής χρειάζεται η νέα έκδοση του Apps Script (2.17.0).");
                }
                else
                {
                    TechCompareMessage = SwimLang.T("Δεν φορτώθηκαν οι αναλύσεις: ") + result.Error;
                }

                TechCandidates = list;
                TechEliteRefs = refs;
                if (form != null)
                    form.RefreshTechCompare();
            });
        }

        private static int Number(JToken token)
        {
            double? v = Value(token);
            return v.HasValue ? (int)Math.Round(v.Value) : 0;
        }

        private static double? Value(JToken token)
        {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                return null;
            double v = (double)token;
            return double.IsNaN(v) || double.IsInfinity(v) ? (double?)null : v;
        }

        /// <summary>The previous analysis of the comparison: "" (none), "auto", "best" or its Race ID (label: how the list shows it).</summary>
        public void SetTechPrev(string id, string label)
        {
            if (Race == null)
                return;

            id = id ?? "";
            label = id == "" || id == "auto" || id == "best" ? "" : (label ?? "");
            if (Race.TechPrev == id && (Race.TechPrevLabel ?? "") == label)
                return;

            Race.TechPrev = id;
            Race.TechPrevLabel = label;
            Changed(true);
            if (form != null)
                form.RefreshTechCompareLater();
        }

        /// <summary>The elite of the comparison (null: none).</summary>
        public void SetTechElite(SwimTechElite elite)
        {
            if (Race == null)
                return;

            if (elite != null && !elite.IsSet)
                elite = null;
            if ((elite == null && Race.TechElite == null) || (elite != null && elite.Same(Race.TechElite) && elite.Label == Race.TechElite.Label &&
                SameValues(elite, Race.TechElite)))
                return;

            Race.TechElite = elite;
            Changed(true);
            if (form != null)
                form.RefreshTechCompareLater();
        }

        private static bool SameValues(SwimTechElite a, SwimTechElite b)
        {
            Func<Dictionary<string, double>, Dictionary<string, double>, bool> same = (x, y) =>
                x.Count == y.Count && x.All(p => y.ContainsKey(p.Key) && Math.Abs(y[p.Key] - p.Value) < 1e-9);
            return same(a.Turn, b.Turn) && same(a.Start, b.Start);
        }

        /// <summary>
        /// «Ως elite αναφορά…»: this technique analysis goes to the Google Sheet (its latest state) and then into the sheet «Elite τεχνική»
        /// under this name: other technique analyses then choose it as their elite (all its numbers and its snapshots).
        /// </summary>
        public void SaveAsTechElite(string name)
        {
            if (Race == null || !Race.IsTraining)
                return;

            if (!Settings.IsConfigured)
            {
                Status(SwimLang.T("Συμπλήρωσε διεύθυνση και token στην καρτέλα Σύνδεση."), true);
                return;
            }

            if (TechPhases.Count == 0)
            {
                Status(SwimLang.T("Η ανάλυση δεν έχει ακόμη εκκινήσεις ή στροφές για να γίνει elite αναφορά."), true);
                return;
            }

            techEliteRace = Race.Id;
            techEliteName = name ?? "";
            Status(SwimLang.T("Αποστολή της ανάλυσης στο Google Sheet και μετά στο φύλλο «Elite τεχνική»…"), false);
            SendNow(false);
        }

        /// <summary>The analysis waiting to become an elite reference was sent (or could not be).</summary>
        private void TechEliteAfterSync(SwimSyncEventArgs e)
        {
            if (techEliteRace == null || e == null || e.RaceId != techEliteRace || client.HasWaiting(techEliteRace))
                return;

            if (e.State == SwimSyncState.Error)
            {
                techEliteRace = null;
                Status(SwimLang.T("Δεν έγινε elite αναφορά: ") + e.Message, true);
                return;
            }

            if (e.State != SwimSyncState.Synced)
                return;

            string raceId = techEliteRace, name = techEliteName;
            techEliteRace = null;
            client.SaveTechElite(Settings.WebAppUrl, Settings.Token, raceId, name, result =>
            {
                if (result.Ok)
                {
                    string saved = result.Raw != null ? result.Raw.Value<string>("name") : null;
                    Status(string.Format(SwimLang.T("✔ Η ανάλυση μπήκε στο φύλλο «Elite τεχνική» ως «{0}»: διαλέγεται στη λίστα Elite των αναλύσεων τεχνικής."),
                        string.IsNullOrEmpty(saved) ? name : saved), false);
                    RefreshTechCandidates();
                }
                else if (IsUnknownAction(result))
                {
                    Status(SwimLang.T("Για την elite αναφορά τεχνικής χρειάζεται η νέα έκδοση του Apps Script (2.17.0)."), true);
                }
                else
                {
                    Status(SwimLang.T("Δεν έγινε elite αναφορά: ") + result.Error, true);
                }
            });
        }
    }
}
