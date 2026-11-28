#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.42.0) The relays: their four swimmers from the official results (the KOE live results by their registry numbers, pasted
    /// results by their names) or chosen by the analyst from the KOE App, so that the analysis of each leg goes to the card of its
    /// swimmer; and the two moments of each exchange (the touch, the feet of the next swimmer off the block) kept as snapshots for
    /// the report.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region The swimmers
        /// <summary>
        /// The registry's athlete of a swimmer of a relay: by his number (the KOE live results) with his family name, else by his name,
        /// year of birth, gender and nation when one athlete only is alike. how: SwimRelayLeg.ByCode, ByName or "" (none); alike: the
        /// athletes of the registry who may be him (several alike, or his family name only), to choose from.
        /// </summary>
        private static SwimAthlete RegistryMember(SwimOfficialEntry m, SwimAthleteList registry, out string how, out List<SwimAthlete> alike)
        {
            how = "";
            alike = new List<SwimAthlete>();
            if (registry == null || m == null)
                return null;

            SwimAthlete byCode = (m.Code.Length > 0 ? registry.Find(m.Code) : null) ?? (m.IfId.Length > 0 ? registry.Find(WorldAquaticsId(m.IfId)) : null);
            if (byCode != null && NationFits(AthleteNation(byCode), m.Nation) &&
                (LikeAthlete(byCode, m, false) >= SwimNameMatch.SameName || LikeAthlete(byCode, m, true) >= FamilyByCode))
            {
                how = SwimRelayLeg.ByCode;
                alike.Add(byCode);
                return byCode;
            }

            Func<SwimAthlete, bool> fits = a =>
            {
                int born;
                if (m.BirthYear > 0 && int.TryParse(a.BirthYear, NumberStyles.Integer, CultureInfo.InvariantCulture, out born) && born != m.BirthYear)
                    return false;
                string gender = SwimReference.NormalizeGender(a.Gender);
                if (m.Gender.Length > 0 && gender.Length > 0 && gender != m.Gender)
                    return false;
                return NationFits(AthleteNation(a), m.Nation);
            };

            alike = registry.Athletes.Where(a => fits(a) && LikeAthlete(a, m, false) >= SwimNameMatch.SameName).Take(8).ToList();
            if (alike.Count == 1)
            {
                how = SwimRelayLeg.ByName;
                return alike[0];
            }

            // None alike by the whole name: the ones of his family name (another given name, a short one), to choose from.
            if (alike.Count == 0)
                alike = registry.Athletes.Where(a => fits(a) && LikeAthlete(a, m, true) >= FamilyByCode)
                    .OrderByDescending(a => LikeAthlete(a, m, false)).Take(8).ToList();
            return null;
        }

        /// <summary>
        /// The swimmers of a relay's result as the legs of the race, each with his athlete of the registry when sure. A leg whose
        /// athlete the analyst chose for the same swimmer of the results keeps him.
        /// </summary>
        private static List<SwimRelayLeg> RelayLegsOf(SwimOfficialEntry team, SwimAthleteList registry, List<SwimRelayLeg> before)
        {
            List<SwimRelayLeg> legs = new List<SwimRelayLeg>();
            foreach (SwimOfficialEntry m in team.Members.Where(x => x.Order >= 1 && x.Order <= 4).OrderBy(x => x.Order))
            {
                if (legs.Any(l => l.Leg == m.Order))
                    continue;

                string how;
                List<SwimAthlete> alike;
                SwimAthlete a = RegistryMember(m, registry, out how, out alike);
                SwimRelayLeg leg = new SwimRelayLeg
                {
                    Leg = m.Order,
                    OfficialName = m.Name.Length > 0 ? m.Name : m.NameLatin,
                    OfficialBirthYear = m.BirthYear,
                    OfficialCode = m.Code ?? "",
                    OfficialTime = m.LegTime,
                    OfficialExchange = m.Reaction
                };

                SwimRelayLeg old = before != null ? before.FirstOrDefault(l => l.Leg == m.Order) : null;
                if (old != null && old.Match == SwimRelayLeg.ByAnalyst && old.HasAthlete && SameSwimmer(old, leg) && (a == null || how != SwimRelayLeg.ByCode))
                {
                    leg.Athlete = old.Athlete;
                    leg.AthleteId = old.AthleteId;
                    leg.AthleteGender = old.AthleteGender;
                    leg.AthleteBirthYear = old.AthleteBirthYear;
                    leg.AthleteCategory = old.AthleteCategory;
                    leg.AthleteClub = old.AthleteClub;
                    leg.Match = SwimRelayLeg.ByAnalyst;
                }
                else if (a != null)
                {
                    TakeAthlete(leg, a, how);
                }
                else
                {
                    leg.Athlete = leg.OfficialName;
                    leg.AthleteGender = m.Gender == "F" ? "Γυναικών" : m.Gender == "M" ? "Ανδρών" : "";
                    leg.AthleteBirthYear = m.BirthYear > 0 ? m.BirthYear.ToString(CultureInfo.InvariantCulture) : "";
                }

                leg.Candidates = alike.Select(x => x.Id).ToList();
                legs.Add(leg);
            }

            return legs;
        }

        /// <summary>The same swimmer of the results (his code, else his name and year as published).</summary>
        private static bool SameSwimmer(SwimRelayLeg a, SwimRelayLeg b)
        {
            if (a.OfficialCode.Length > 0 && b.OfficialCode.Length > 0)
                return a.OfficialCode == b.OfficialCode;
            return a.OfficialName.Length > 0 && SwimNameMatch.Key(a.OfficialName) == SwimNameMatch.Key(b.OfficialName) &&
                (a.OfficialBirthYear == 0 || b.OfficialBirthYear == 0 || a.OfficialBirthYear == b.OfficialBirthYear);
        }

        private static void TakeAthlete(SwimRelayLeg leg, SwimAthlete a, string how)
        {
            leg.Athlete = a.Name;
            leg.AthleteId = a.Id;
            leg.AthleteGender = a.Gender ?? "";
            leg.AthleteBirthYear = a.BirthYear ?? "";
            leg.AthleteCategory = a.Category ?? "";
            leg.AthleteClub = a.Club ?? "";
            leg.Match = how ?? "";
        }

        /// <summary>
        /// The swimmers of a relay for the window that asks whether the results go in: each with his leg, the time of it and how he was
        /// found in the KOE App.
        /// </summary>
        private static string RelayLegsText(List<SwimRelayLeg> legs)
        {
            StringBuilder b = new StringBuilder();
            b.Append(SwimLang.T("Κολυμβητές (η ανάλυση του σκέλους του στην καρτέλα του καθενός):")).Append('\n');
            foreach (SwimRelayLeg l in legs)
            {
                b.Append(l.Leg.ToString(CultureInfo.InvariantCulture)).Append(" · ").Append(l.Athlete);
                if (l.OfficialTime.HasValue)
                    b.Append(" · ").Append(SwimReference.FormatTime(l.OfficialTime.Value));
                b.Append(" · ").Append(LegNote(l)).Append('\n');
            }

            return b.ToString();
        }

        /// <summary>How the athlete of a leg was found, as written beside him: "✔ ΑΜ 990101", "✔ όνομα + έτος", "⚠ 2 στο KOE App: διάλεξε".</summary>
        public static string LegNote(SwimRelayLeg l)
        {
            if (l == null)
                return "";
            if (l.HasAthlete)
            {
                if (l.Match == SwimRelayLeg.ByCode)
                    return SwimLang.T("✔ ΑΜ ") + l.AthleteId;
                if (l.Match == SwimRelayLeg.ByName)
                    return SwimLang.T("✔ όνομα + έτος");
                return SwimLang.T("✔ από το KOE App");
            }

            int alike = l.Candidates != null ? l.Candidates.Count : 0;
            if (alike > 0)
                return string.Format(CultureInfo.InvariantCulture, SwimLang.T("⚠ {0} στο KOE App: διάλεξε"), alike);
            return string.IsNullOrWhiteSpace(l.Athlete) ? "" : SwimLang.T("όχι στο KOE App");
        }

        /// <summary>The leg's swimmer is the athlete of the registry chosen by the analyst (the KOE App list).</summary>
        public void SetRelayLegAthlete(int leg, SwimAthlete a)
        {
            if (Race == null || !Race.IsRelayOfFour || a == null || leg < 1 || leg > 4)
                return;

            SwimRelayLeg l = Race.RelayLegFor(leg);
            TakeAthlete(l, a, SwimRelayLeg.ByAnalyst);
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Σκέλος {0}: {1} (μητρώο {2})."), leg, a.Name, a.Id), false);
            RelayLegsChanged();
        }

        /// <summary>The leg's swimmer by his name only (not in the KOE App: the analysis of his leg stays with the team).</summary>
        public void SetRelayLegName(int leg, string name)
        {
            if (Race == null || !Race.IsRelayOfFour || leg < 1 || leg > 4)
                return;

            name = (name ?? "").Trim();
            SwimRelayLeg l = Race.RelayLegFor(leg);
            if (l.Athlete == name && !l.HasAthlete)
                return;
            l.Athlete = name;
            l.AthleteId = "";
            l.AthleteCategory = "";
            l.AthleteClub = "";
            l.Match = "";
            Status(name.Length > 0 ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("Σκέλος {0}: {1} (χωρίς το KOE App)."), leg, name)
                : string.Format(CultureInfo.InvariantCulture, SwimLang.T("Σκέλος {0}: χωρίς κολυμβητή."), leg), false);
            RelayLegsChanged();
        }

        /// <summary>The athletes of the registry who may be the leg's swimmer (the alike ones of the results, else by the name given).</summary>
        public List<SwimAthlete> RelayLegCandidates(int leg)
        {
            List<SwimAthlete> list = new List<SwimAthlete>();
            SwimRelayLeg l = Race != null ? Race.RelayLeg(leg) : null;
            if (l == null || Athletes == null)
                return list;

            if (l.Candidates == null)
            {
                // After the analysis was opened again: looked for once from the swimmer of the results (or the name given).
                SwimOfficialEntry m = new SwimOfficialEntry { BirthYear = l.OfficialBirthYear, Code = l.OfficialCode ?? "" };
                string family, given;
                SwimNameMatch.Split(l.OfficialName.Length > 0 ? l.OfficialName : l.Athlete, out family, out given);
                if (SwimNameMatch.HasGreek(family + given))
                {
                    m.FamilyName = family;
                    m.GivenName = given;
                }
                else
                {
                    m.FamilyNameLatin = family;
                    m.GivenNameLatin = given;
                }

                string how;
                List<SwimAthlete> alike;
                RegistryMember(m, Athletes, out how, out alike);
                l.Candidates = alike.Select(x => x.Id).ToList();
            }

            foreach (string id in l.Candidates)
            {
                SwimAthlete a = Athletes.Find(id);
                if (a != null && !list.Contains(a))
                    list.Add(a);
            }

            return list;
        }

        private void RelayLegsChanged()
        {
            Changed(false);
            ScheduleSend();
            if (form != null)
                form.RefreshAll();
        }
        #endregion

        #region The snapshots of the exchanges
        /// <summary>The frames of feet off a block kept until the touch before them is marked (an early start marked first).</summary>
        private readonly List<KeyValuePair<string, Bitmap>> exchangeWaiting = new List<KeyValuePair<string, Bitmap>>();

        /// <summary>
        /// The two moments of each exchange of a relay are kept as snapshots, whatever the automatic snapshots of the technical
        /// analysis: the touch of the swimmer who arrives (S at the end of his leg) and the feet of the next one off the block (F), as the
        /// video shows them (without the graphics: the report writes the times beside them). The same moment marked again replaces its
        /// snapshot; undoing the mark removes it (UndoAutoSnapshots). They are not uploaded: they go into the PDF only.
        /// </summary>
        private void ExchangeSnapshots(PlayerScreenCommands command, long timestamp, string newMarkId)
        {
            if (Race == null || !HasVideo || !Race.IsRelayOfFour || !Race.StartTimestamp.HasValue || Timing == null || !Timing.IsValid)
                return;
            if (command != PlayerScreenCommands.SwimSplit && command != PlayerScreenCommands.SwimFeetOff)
                return;

            SwimMark mark = newMarkId != null ? Race.Marks.FirstOrDefault(m => m.Id == newMarkId) : null;
            if (mark == null || !SameFrame(mark.Timestamp, timestamp))
                return;

            List<SwimSnapshot> taken = new List<SwimSnapshot>();
            if (mark.Type == SwimMarkType.Split)
            {
                int n = SwimRelay.ExchangeAt(Race, mark.Distance);
                if (n == 0)
                    return;
                SwimSnapshot touch = ExchangeSnapshot(null, timestamp, SwimRelay.TouchLabel(n), mark.Id);
                if (touch != null)
                    taken.Add(touch);

                // The feet off the block marked before this touch (an early start): their frame now.
                for (int i = exchangeWaiting.Count - 1; i >= 0; i--)
                {
                    KeyValuePair<string, Bitmap> w = exchangeWaiting[i];
                    SwimMark off = Race.Marks.FirstOrDefault(m => m.Id == w.Key);
                    if (off != null && ExchangeOfFeetOff(off) != n)
                        continue;
                    exchangeWaiting.RemoveAt(i);
                    try
                    {
                        SwimSnapshot early = off != null ? ExchangeSnapshot(w.Value, off.Timestamp, SwimRelay.OffLabel(n), off.Id) : null;
                        if (early != null)
                            taken.Add(early);
                    }
                    finally
                    {
                        w.Value.Dispose();
                    }
                }
            }
            else
            {
                int n = ExchangeOfFeetOff(mark);
                if (n > 0)
                {
                    SwimSnapshot off = ExchangeSnapshot(null, timestamp, SwimRelay.OffLabel(n), mark.Id);
                    if (off != null)
                        taken.Add(off);
                }
                else if (n < 0)
                {
                    // Its touch is not marked yet: the frame waits for it.
                    Bitmap image = CaptureExchangeFrame(timestamp);
                    if (image != null)
                    {
                        exchangeWaiting.Add(new KeyValuePair<string, Bitmap>(mark.Id, image));
                        while (exchangeWaiting.Count > 3)
                        {
                            exchangeWaiting[0].Value.Dispose();
                            exchangeWaiting.RemoveAt(0);
                        }
                    }
                }
            }

            if (taken.Count == 0)
                return;

            string said = "📷 " + string.Join(", ", taken.OrderBy(x => x.Timestamp).Select(x => x.Event).ToArray());
            Status(string.IsNullOrEmpty(LastStatus) ? said : LastStatus + "   " + said, LastStatusIsWarning);
            MarkDirty();
            if (form != null)
                form.SnapshotAdded(taken[taken.Count - 1]);
        }

        /// <summary>
        /// The exchange (1–3) of feet off a block: within half a second before the touch of the end of a leg, or the running time after it
        /// (SwimRelay.RunningSeconds). 0: none; −1: the touch of the next exchange is not marked yet and the feet are after the last
        /// checkpoint marked before it (an early start marked before the touch: its frame waits for the touch).
        /// </summary>
        private int ExchangeOfFeetOff(SwimMark mark)
        {
            if (mark == null || !Race.StartTimestamp.HasValue)
                return 0;

            double f = Timing.Seconds(mark.Timestamp, Race.StartTimestamp.Value);
            int leg = Race.LegDistance;
            for (int n = 1; n <= 3; n++)
            {
                int wall = n * leg;
                double? touch = MarkedTime(wall);
                if (touch.HasValue)
                {
                    if (f > touch.Value - 0.5 && f < touch.Value + SwimRelay.RunningSeconds)
                        return n;
                    continue;
                }

                // The first exchange not marked (the ones after it are not reached yet).
                int before = Race.Checkpoints.Where(d => d < wall && d > wall - leg).DefaultIfEmpty(0).Max();
                double? at = before > 0 ? MarkedTime(before) : null;
                return at.HasValue && f > at.Value ? -1 : 0;
            }

            return 0;
        }

        /// <summary>The race time of a checkpoint: its time (the official one where given), null when it is not marked.</summary>
        private double? MarkedTime(int distance)
        {
            SwimRow row = Computed != null && Computed.Rows != null ? Computed.Rows.FirstOrDefault(r => r.D == distance) : null;
            if (row != null && row.T.HasValue && row.Marked)
                return row.T;
            SwimMark split = Race.GetSplit(distance);
            return split != null ? (double?)Timing.Seconds(split.Timestamp, Race.StartTimestamp.Value) : null;
        }

        /// <summary>
        /// The snapshot of a moment of an exchange (from the image given, else the frame shown): the one of the same moment before goes
        /// (back with the Undo of this mark).
        /// </summary>
        private SwimSnapshot ExchangeSnapshot(Bitmap image, long timestamp, string label, string markId)
        {
            bool own = image == null;
            if (own)
                image = CaptureExchangeFrame(timestamp);
            if (image == null)
                return null;

            try
            {
                SwimSnapshot snapshot = AddSnapshot(image, timestamp, label, markId);
                if (snapshot == null)
                    return null;

                List<SwimSnapshot> old = Race.Snapshots.Where(x => x != snapshot && x.Event == label).ToList();
                foreach (SwimSnapshot before in old)
                    Race.Snapshots.Remove(before);
                if (old.Count > 0 && !string.IsNullOrEmpty(markId))
                    replacedSnapshots[markId] = old;
                return snapshot;
            }
            finally
            {
                if (own)
                    image.Dispose();
            }
        }

        /// <summary>The frame shown without the graphics (a group of screens: the camera above the water when its cameras are said).</summary>
        private Bitmap CaptureExchangeFrame(long timestamp)
        {
            if (GroupCameraMode)
            {
                try
                {
                    Bitmap one = CaptureCameraOfMoment(timestamp, false, true, SwimCameraPick.Above, null);
                    if (one != null)
                        return one;
                }
                catch (Exception e)
                {
                    log.Error("Could not take the picture of the camera of the exchange.", e);
                }
            }

            return CaptureFrame(timestamp, false, false, true);
        }

        private void ClearExchangeWaiting()
        {
            foreach (KeyValuePair<string, Bitmap> w in exchangeWaiting)
                w.Value.Dispose();
            exchangeWaiting.Clear();
        }
        #endregion
    }
}
