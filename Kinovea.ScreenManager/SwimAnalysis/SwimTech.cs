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

namespace Kinovea.ScreenManager
{
    public enum SwimPhaseKind
    {
        Start,
        Turn
    }

    /// <summary>
    /// One start or one turn of the technical analysis: the start and the turns of a race, or the attempts of a
    /// training video. Times are in seconds from the origin of the phase: the start signal or the contact with the wall.
    /// </summary>
    public class SwimTechPhase
    {
        /// <summary>Race: "start" or "turn-50". Training: id of the mark that begins the attempt (signal or wall contact).</summary>
        public string Id;
        public SwimPhaseKind Kind;

        /// <summary>Race: distance of the wall (0 for the start). Training: 0.</summary>
        public int Wall;

        /// <summary>Training: number of the attempt of this kind (1, 2, …). Race: 0.</summary>
        public int Attempt;

        /// <summary>The title in the language of the analysis ("Turn 50m").</summary>
        public string Title;

        /// <summary>The title in Greek ("Στροφή 50μ."), as the sheet gets it.</summary>
        public string TitleGreek;
        public bool Flip;

        /// <summary>Video timestamp of t = 0 of the phase.</summary>
        public long Origin;

        /// <summary>
        /// Part of the video shown as this phase (panel, overlay, snapshots and angles): [WindowStart, WindowEnd).
        /// A start begins a little before its signal, a turn with its approach; the phase lasts at least until its last event.
        /// </summary>
        public long WindowStart;
        public long WindowEnd;

        /// <summary>
        /// The events of the phase are searched in [EventStart, EventEnd): from the previous origin to the next one
        /// (the last stroke, rotation and −5 before the contact, the first push-off, breakout and stroke after it).
        /// </summary>
        public long EventStart;
        public long EventEnd;

        // Events, in seconds from the origin (negative before the contact of a turn).
        public double? Reaction;
        public double? HandsOff;
        public double? BackFoot;
        public double? Block;
        public double? Entry;
        public double? LastStroke;

        /// <summary>(2.9.9) A turn of a training video: the first stroke marked in its approach (the technical analysis marks every stroke).</summary>
        public double? FirstApproachStroke;
        public double? RotationStart;
        public double? Pushoff;
        public double? Breakout;
        public double? FirstStroke;
        public List<double> Kicks = new List<double>();

        /// <summary>Head at a technical distance: offset (5, 10, 15 or −5, +5, +10, +15) → seconds from the origin.</summary>
        public SortedDictionary<int, double> Splits = new SortedDictionary<int, double>();

        /// <summary>
        /// Distances measured on the video (pool units from the wall). (2.41.0) EntryDist: where the head went into the water (EntryHead),
        /// in an analysis marked with the hands before 2.41.0 where the hands went in.
        /// </summary>
        public double? EntryDist;
        public double? BreakoutDist;

        /// <summary>
        /// (2.41.0) The entry of a start is where the head goes into the water (the analyst's request): Entry and EntryDist are the head's.
        /// False in an analysis marked with the entry of the hands before 2.41.0 (SwimRace.HeadEntry): its speeds take the head
        /// HeadBehindHands behind them at that moment.
        /// </summary>
        public bool EntryHead = true;

        /// <summary>
        /// (2.35.0) A start marked with the entry of the hands (EntryHead false): how far the head is behind the hands when they enter the
        /// water (pool units), from the typical height of the swimmer (SwimHead). (2.41.0) Only for those analyses.
        /// </summary>
        public double? HeadBehindHands;

        /// <summary>
        /// (2.9.8) Where the head came out for the speeds of the breakout (pool units from the wall): BreakoutDist, at the start of a race
        /// else the underwater distance; OutEstimated: it is the estimate from the times (no speeds from it).
        /// </summary>
        public double? OutDist;
        public bool OutEstimated;

        /// <summary>
        /// (2.32.0) The distance of the breakout is past what a swimmer can have (SwimReel.PossibleBreakout: a measurement with the
        /// calibration of another frame of a moving camera, 39.61 m for about 10): no speed or distance per kick from it (the note says so).
        /// </summary>
        public bool OutImpossible;

        /// <summary>
        /// (2.15.0) A turn: where the head is at the push-off (pool units from the wall): the toes leave the wall with the body straight,
        /// so the head is the body length away. (2.35.0) The typical height of the swimmer + the pointed toes (SwimHead): the analyst
        /// marks only the push-off, the head is not seen well under the water (2.36.0: a body length measured before is not used).
        /// (2.41.0) The speed under the water of a turn starts there again, the analyst's request («τα νούμερα … κεφάλι - κεφάλι είναι πιο
        /// σωστά»; «δεν θα έχω το ύψος, εσύ βάλε το μέσα στον κανόνα»): 2.38.6–2.40.0 from the wall, 20–45% faster.
        /// </summary>
        public double? HeadAtPushoff;

        /// <summary>
        /// (2.38.6) The speed of the clean swimming after the 15 of this phase (m/s): the segment from the 15 (+15 after a wall) to the next
        /// checkpoint, as the table of the race has it; null without it (a training video). For the rule of the breakout.
        /// </summary>
        public double? NextSpeed;

        /// <summary>
        /// (2.38.6) Where the head comes out for the speeds (pool units from the wall). (2.41.1) OutDist, as measured: the rule of the
        /// breakout is a check (SlowAfterBreakout); 2.38.6–2.41.0 nearer by it.
        /// </summary>
        public double? OutForSpeeds;

        /// <summary>
        /// (2.41.1) The check of the breakout (the analyst's rule 1, a check since 2.41.1): from the breakout to the 15 (+15 after a wall)
        /// the swimmer was slower than in the swimming after it: he loses speed at the breakout, or the breakout was measured too far.
        /// The panel (RuleNote) and the reports (⚠) say so; the speeds stay as measured.
        /// </summary>
        public bool SlowAfterBreakout;

        /// <summary>
        /// (2.38.6) A turn: where its speed under the water starts (pool units from the wall). (2.41.0) Where the head is at the push-off
        /// (HeadAtPushoff), or farther by the rule of the start (SwimSpeedRules.PushAway) when the marks give the turn faster than the start
        /// even from the head (PastHead: a mark or a distance is off; 2.38.6–2.40.0 from the wall).
        /// </summary>
        public double PushAway;

        /// <summary>(2.38.6) The rule of the start needed the speed of a turn to start past where the head is at the push-off: a mark is off.</summary>
        public bool PastHead;

        /// <summary>(2.38.6) A start: its mean speed under the water and the highest the video shows there (m/s), for the rule of the turns.</summary>
        public double? UnderwaterMean;
        public double? UnderwaterPeak;

        /// <summary>(2.38.6) What the analyst's rules changed in the speeds of this phase, for the panel (not the reports); null when nothing.</summary>
        public string RuleNote;

        /// <summary>(2.9.8) Start of a race: the official reaction time (the release from the block) when the release is not marked.</summary>
        public double? OfficialRelease;

        /// <summary>(2.9.8) Why a speed of the breakout is not given (in the language of the analysis), null when nothing is missing.</summary>
        public string SpeedNote;

        /// <summary>Values sent to the report, with the keys of SWIM_TECH_METRICS of the backend.</summary>
        public Dictionary<string, double> Values = new Dictionary<string, double>();

        /// <summary>
        /// (2.41.0) The hands go into the water about this long before the head (s): the head is 0.27 × the height behind them (≈0.5 m)
        /// and the swimmer goes in at about 4 m/s.
        /// </summary>
        public const double HandsBeforeHead = 0.12;

        /// <summary>
        /// (2.41.0) A start as the technical evaluation sees it: its moments of the entry are from the hands (the hand entry, the hips
        /// 0.12 s after it, the glide…), so with the entry of the head the hands HandsBeforeHead before it. The phase itself otherwise.
        /// </summary>
        public static SwimTechPhase HandsView(SwimTechPhase p)
        {
            if (p == null || p.Kind != SwimPhaseKind.Start || !p.EntryHead || !p.Entry.HasValue)
                return p;

            SwimTechPhase c = (SwimTechPhase)p.MemberwiseClone();
            c.Entry = p.Entry.Value - HandsBeforeHead;
            c.EntryHead = false;
            return c;
        }

        public double? Value(string key)
        {
            double v;
            return Values.TryGetValue(key, out v) ? (double?)v : null;
        }

        public bool Contains(long timestamp)
        {
            return timestamp >= WindowStart && timestamp < WindowEnd;
        }
    }

    /// <summary>
    /// A technical value: key (as in the backend), label and unit for the panel.
    /// </summary>
    public class SwimTechMetric
    {
        public string Key;

        /// <summary>The label in Greek (the key of its English).</summary>
        public string LabelGreek;

        /// <summary>The label in the language of the analysis.</summary>
        public string Label
        {
            get { return SwimLang.T(LabelGreek); }
        }

        /// <summary>s, d (distance), v (m/s), n (count), r (kicks/s).</summary>
        public string Unit;
        public SwimPhaseKind? Kind;

        public SwimTechMetric(string key, string label, string unit, SwimPhaseKind? kind)
        {
            Key = key;
            LabelGreek = label;
            Unit = unit;
            Kind = kind;
        }

        public string Format(double value)
        {
            switch (Unit)
            {
                case "n": return value.ToString("0", CultureInfo.InvariantCulture);
                case "s": return value.ToString("0.00", CultureInfo.InvariantCulture);
                default: return value.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// Phases and values of the technical analysis of starts and turns, from the marks of the video.
    /// </summary>
    /// <summary>
    /// (Swim 2.23.0) The stage of the movement at the event of a mark, for the snapshot of that event: the stage its moment gives with
    /// all the events of the phase marked, without waiting for them (the analyst marks the reaction before the release, the entry and
    /// the breakout; the moment alone gave "Κολύμβηση" then). Null when the event does not say it (a stroke, a distance: its moment does).
    /// </summary>
    public static class SwimStages
    {
        public static string OfMark(SwimMarkType type, SwimPhaseKind kind)
        {
            if (kind == SwimPhaseKind.Start)
            {
                switch (type)
                {
                    case SwimMarkType.Signal:
                        return "Θέση ετοιμότητας";
                    case SwimMarkType.Reaction:
                    case SwimMarkType.HandsOff:
                    case SwimMarkType.BackFoot:
                        return "Φάση βατήρα";
                    case SwimMarkType.FeetOff:
                        return "Αποδέσμευση";
                    case SwimMarkType.Entry:
                        return "Είσοδος στο νερό";
                    case SwimMarkType.Kick:
                        return "Υποβρύχια φάση";
                    case SwimMarkType.Breakout:
                        return "Έξοδος από το νερό";
                    default:
                        return null;
                }
            }

            switch (type)
            {
                case SwimMarkType.RotationStart:
                    return "Περιστροφή";
                case SwimMarkType.WallContact:
                    return "Επαφή στον τοίχο";
                case SwimMarkType.FeetOff:
                    return "Αποκόλληση";
                case SwimMarkType.Kick:
                    return "Υποβρύχια φάση";
                case SwimMarkType.Breakout:
                    return "Έξοδος από το νερό";
                default:
                    return null;
            }
        }
    }

    public static class SwimTechCalculator
    {
        public static readonly SwimTechMetric[] Metrics = new SwimTechMetric[]
        {
            new SwimTechMetric("reaction", "Αντίδραση – πρώτη κίνηση", "s", SwimPhaseKind.Start),
            new SwimTechMetric("handsOff", "Χέρια φεύγουν από τον βατήρα", "s", SwimPhaseKind.Start),
            new SwimTechMetric("backFoot", "Πίσω πόδι φεύγει", "s", SwimPhaseKind.Start),
            new SwimTechMetric("blockTime", "Αποδέσμευση (μπροστινό πόδι)", "s", SwimPhaseKind.Start),
            new SwimTechMetric("blockPhase", "Φάση βατήρα: κίνηση → αποδέσμευση", "s", SwimPhaseKind.Start),
            new SwimTechMetric("flight", "Πτήση: αποδέσμευση → είσοδος", "s", SwimPhaseKind.Start),
            new SwimTechMetric("entryTime", "Είσοδος κεφαλιού", "s", SwimPhaseKind.Start),
            new SwimTechMetric("entryDist", "Απόσταση εισόδου", "d", SwimPhaseKind.Start),
            new SwimTechMetric("in5", "Προσέγγιση −5 → τοίχος", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("lastStrokeGap", "Τελευταία χεριά → τοίχος", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("rotation", "Περιστροφή: αρχή → επαφή", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("contact", "Επαφή → αποκόλληση", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("turnTime", "Χρόνος στροφής", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("t5", "Χρόνος 5", "s", SwimPhaseKind.Start),
            new SwimTechMetric("t10", "Χρόνος 10", "s", SwimPhaseKind.Start),
            new SwimTechMetric("t15", "Χρόνος 15", "s", SwimPhaseKind.Start),
            new SwimTechMetric("out5", "Τοίχος → +5", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("out10", "Τοίχος → +10", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("out15", "Τοίχος → +15", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("inOut5", "−5 → +5", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("inOut10", "−5 → +10", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("inOut15", "−5 → +15", "s", SwimPhaseKind.Turn),
            new SwimTechMetric("v0_5", "Ταχύτητα 0 → 5", "v", null),
            new SwimTechMetric("v5_10", "Ταχύτητα 5 → 10", "v", null),
            new SwimTechMetric("v10_15", "Ταχύτητα 10 → 15", "v", null),
            new SwimTechMetric("underwater", "Υποβρύχια φάση", "s", null),
            new SwimTechMetric("kicks", "Δελφινισμοί", "n", null),
            new SwimTechMetric("kickRate", "Συχνότητα δελφινισμών (/s)", "r", null),
            new SwimTechMetric("kickDist", "Απόσταση ανά δελφινισμό", "d", null),
            new SwimTechMetric("breakoutTime", "Έξοδος από το νερό", "s", null),
            new SwimTechMetric("breakoutDist", "Απόσταση εξόδου", "d", null),
            // (2.9.8) The speeds of the breakout: before it (start: from the entry and from the release; turn: from the push-off,
            // the same value as before under another name) and after it to the 15. (2.35.0) The phases of a start as the analyst
            // asked: signal → entry, entry → breakout, breakout → 15. (2.41.0) The entry of the head: each from the head to the head.
            new SwimTechMetric("sigEntryVel", "Ταχύτητα σήμα → είσοδος κεφαλιού", "v", SwimPhaseKind.Start),
            new SwimTechMetric("uwVel", "Ταχύτητα υποβρύχιας φάσης (είσοδος → έξοδος)", "v", SwimPhaseKind.Start),
            new SwimTechMetric("pushVel", "Ταχύτητα αποδέσμευση → έξοδος", "v", SwimPhaseKind.Start),
            new SwimTechMetric("uwVel", "Ταχύτητα αποκόλληση → έξοδος", "v", SwimPhaseKind.Turn),
            new SwimTechMetric("bo15Vel", "Ταχύτητα έξοδος → 15", "v", null),
            new SwimTechMetric("firstStroke", "Πρώτη χεριά", "s", null)
        };

        /// <summary>Technical distances of a start and of a turn, in the order they happen.</summary>
        public static readonly int[] StartOffsets = new int[] { 5, 10, 15 };
        public static readonly int[] TurnOffsets = new int[] { -5, 5, 10, 15 };

        /// <summary>A training attempt keeps the marks up to this long after its origin.</summary>
        private const double AttemptSeconds = 25;

        /// <summary>On the panel a turn begins this long before its contact (approach: −5, last stroke, rotation).</summary>
        private const double ApproachSeconds = 5;

        /// <summary>On the panel a start begins this long before its signal (position on the block).</summary>
        private const double LeadSeconds = 3;

        /// <summary>Training: the approach of a turn is searched this long before its contact (after the previous attempt).</summary>
        private const double ApproachSearchSeconds = 10;

        /// <summary>The key T marks 5/10/15 (+5/+10/+15) up to this long after the signal or the contact, then −5 of the next turn.</summary>
        private const double PositiveSeconds = 15;

        /// <summary>The first stroke is at most this long after the breakout, the last stroke at most this long before the contact.</summary>
        private const double FirstStrokeSeconds = 2.5;
        private const double LastStrokeSeconds = 4;

        public static List<SwimTechPhase> Compute(SwimRace race, SwimComputed computed, SwimTiming timing)
        {
            List<SwimTechPhase> phases = new List<SwimTechPhase>();
            if (race == null || timing == null || !timing.IsValid)
                return phases;

            if (race.IsTraining)
                TrainingPhases(race, timing, phases);
            else
                RacePhases(race, computed, timing, phases);

            // (2.38.6) The start first: the turns of a race are held to it (the analyst's rule, SwimSpeedRules.PushAway); not a relay
            // (another swimmer dived) or a training video (its attempts are not one race).
            SwimTechPhase first = race.IsTraining || race.Relay ? null : phases.FirstOrDefault(p => p.Kind == SwimPhaseKind.Start);
            if (first != null)
                ComputeValues(race, first, null);
            foreach (SwimTechPhase phase in phases)
            {
                if (phase != first)
                    ComputeValues(race, phase, first);
            }

            return phases;
        }

        /// <summary>
        /// The phase that contains a video timestamp (or null).
        /// </summary>
        public static SwimTechPhase PhaseAt(List<SwimTechPhase> phases, long timestamp)
        {
            if (phases == null)
                return null;

            return phases.LastOrDefault(p => p.Contains(timestamp));
        }

        public static string Label(SwimTechMetric metric, string unit)
        {
            string label = metric.Label;
            if (label.EndsWith(" 5", StringComparison.Ordinal) || label.EndsWith(" 10", StringComparison.Ordinal) || label.EndsWith(" 15", StringComparison.Ordinal) ||
                label.EndsWith("+5", StringComparison.Ordinal) || label.EndsWith("+10", StringComparison.Ordinal) || label.EndsWith("+15", StringComparison.Ordinal))
                label += unit;

            switch (metric.Unit)
            {
                case "s": return label + " (s)";
                case "d": return label + " (" + unit + ")";
                case "v": return label + " (m/s)";
                default: return label;
            }
        }

        #region Phases
        private static void RacePhases(SwimRace race, SwimComputed computed, SwimTiming timing, List<SwimTechPhase> phases)
        {
            if (!race.StartTimestamp.HasValue || computed == null)
                return;

            long origin = race.StartTimestamp.Value;
            // (2.35.0) Where the head is at the entry of the hands and at the push-offs (m → pool units).
            SwimHead head = SwimHead.Of(race);
            double k = race.K > 0 ? race.K : 1;

            // (2.32.0) A checkpoint with an official time is at that time, also when its mark is a little before or after it (the analyst's
            // request: the touch of a wall, its −5 and +15 as the splits of the race; until 2.31.0 the frames of the marks, 2.74 / 8.14 s
            // beside 2.86 / 8.01 of the splits for a touch marked 0.12 s before its official time).
            Dictionary<int, double> official = race.OfficialTimes();
            Func<int, long?> splitAt = d =>
            {
                double typed;
                if (official.TryGetValue(d, out typed) && typed > 0)
                    return timing.Timestamp(typed, origin);

                SwimMark m = race.GetSplit(d);
                return m != null ? (long?)m.Timestamp : null;
            };

            long? finish = splitAt(race.Distance);
            long end = finish.HasValue ? finish.Value + 1 : long.MaxValue;

            SwimTechPhase start = new SwimTechPhase();
            start.Id = "start";
            start.Kind = SwimPhaseKind.Start;
            start.Wall = 0;
            start.TitleGreek = "Εκκίνηση";
            start.Title = SwimLang.Label(start.TitleGreek);
            start.Origin = origin;
            phases.Add(start);

            // Contacts in time order: the start signal and every wall marked. A relay exchange bounds the phases
            // around it but is not a turn (the next swimmer starts from the block).
            List<KeyValuePair<long, SwimTechPhase>> contacts = new List<KeyValuePair<long, SwimTechPhase>>();
            contacts.Add(new KeyValuePair<long, SwimTechPhase>(origin, start));
            foreach (int wall in TurnWalls(race))
            {
                long? tw = splitAt(wall);
                if (!tw.HasValue || tw.Value <= origin)
                    continue;

                if (IsExchange(race, wall))
                {
                    contacts.Add(new KeyValuePair<long, SwimTechPhase>(tw.Value, null));
                    continue;
                }

                SwimTurnComputed turn = computed.Turns.FirstOrDefault(t => t.Wall == wall);
                SwimTechPhase phase = new SwimTechPhase();
                phase.Id = "turn-" + wall.ToString(CultureInfo.InvariantCulture);
                phase.Kind = SwimPhaseKind.Turn;
                phase.Wall = wall;
                phase.TitleGreek = "Στροφή " + wall.ToString(CultureInfo.InvariantCulture) + (race.IsYards ? "yd" : "μ.");
                phase.Title = SwimLang.Label(phase.TitleGreek);
                phase.Flip = turn != null && turn.Flip;
                phase.Origin = tw.Value;
                phases.Add(phase);
                contacts.Add(new KeyValuePair<long, SwimTechPhase>(tw.Value, phase));
            }

            contacts.Sort((a, b) => a.Key.CompareTo(b.Key));
            long lead = Before(timing, LeadSeconds);
            for (int i = 0; i < contacts.Count; i++)
            {
                SwimTechPhase p = contacts[i].Value;
                if (p == null)
                    continue;

                p.EventStart = p == start ? origin - lead : contacts[i - 1].Key + 1;
                p.EventEnd = i + 1 < contacts.Count ? contacts[i + 1].Key : end;
                Fill(race, timing, p, true);

                if (p == start)
                {
                    if (race.Reaction.HasValue)
                        p.Reaction = race.Reaction.Value;
                    // (2.41.0) The entry of the head; in an analysis marked with the hands, the head behind them (SwimHead).
                    p.EntryHead = race.HeadEntry();
                    p.EntryDist = race.EntryDistance();
                    p.HeadBehindHands = p.EntryHead ? (double?)null : head.BehindHands / k;
                    p.BreakoutDist = race.BreakoutDist;

                    // (2.9.8) The speeds of the breakout: where the head came out (else the underwater distance, as the video shows it).
                    p.OutDist = race.BreakoutDist ?? race.UnderwaterDist;
                    p.OutEstimated = !race.BreakoutDist.HasValue && race.UnderwaterDist.HasValue && race.UnderwaterEstimated;
                    p.OutImpossible = !SwimReel.PossibleBreakout(race, 0, p.OutDist) || !SwimReel.PossibleBreakout(race, 0, p.BreakoutDist);
                    if (race.OfficialReaction.HasValue && race.OfficialReaction.Value > 0)
                        p.OfficialRelease = race.OfficialReaction.Value;
                }
                else
                {
                    // The turn values of the race analysis (underwater distance, last stroke, push-off) also hold here.
                    SwimTurnManual manual = race.GetTurn(p.Wall, false);
                    if (manual != null)
                    {
                        p.BreakoutDist = manual.UnderwaterDist;
                        p.OutDist = manual.UnderwaterDist;
                        p.OutEstimated = manual.UnderwaterDist.HasValue && manual.UnderwaterEstimated;
                        p.OutImpossible = !SwimReel.PossibleBreakout(race, p.Wall, manual.UnderwaterDist);
                    }

                    SwimTurnComputed turn = computed.Turns.FirstOrDefault(t => t.Wall == p.Wall);
                    if (turn != null && turn.LastStrokeGap.HasValue)
                        p.LastStroke = -turn.LastStrokeGap.Value;
                    // (2.15.0) From the wall's mark on the video (OnVideo), as every other event of the phase: an official time of the
                    // wall (SwimRow.T) a little off its frame made the contact, the underwater time and its speed off by that much.
                    SwimRow wallRow = computed.Rows.FirstOrDefault(r => r.D == p.Wall);
                    // (2.22.0) In whole milliseconds, as the push-off of the turn (SwimCalculator.Ms).
                    double wallVideo = wallRow != null && !double.IsNaN(wallRow.OnVideo) ? SwimCalculator.Ms(wallRow.OnVideo) : double.NaN;
                    if (turn != null && turn.Pushoff.HasValue && !double.IsNaN(wallVideo) && turn.Pushoff.Value >= wallVideo)
                        p.Pushoff = turn.Pushoff.Value - wallVideo;

                    // (2.15.0) The head at the push-off: the body length of the swimmer (the same at every wall of the race). (2.35.0)
                    // The typical height + the pointed toes (SwimHead). (2.41.0) Where its speed under the water starts again.
                    p.HeadAtPushoff = head.AtPushoff / k;
                }

                ApplyCheckpointSplits(race, timing, p, splitAt);

                // (2.38.6) The swimming after the 15, for the rule of the breakout.
                p.NextSpeed = SwimSpeedRules.NextSpeed(computed, p.Wall);
            }

            // What the panel shows: the approach of a turn (at most half of the length) after the last event of the previous phase.
            for (int i = 0; i < contacts.Count; i++)
            {
                SwimTechPhase p = contacts[i].Value;
                if (p == null)
                    continue;

                p.WindowStart = p == start ? origin - lead : Boundary(timing, contacts[i - 1].Value, contacts[i - 1].Key, p, p.Origin);
                p.WindowEnd = i + 1 < contacts.Count ? Boundary(timing, p, p.Origin, contacts[i + 1].Value, contacts[i + 1].Key) : end;
            }
        }

        /// <summary>
        /// Race: the checkpoints at the technical distances count when there is no technical split
        /// (15 of the start, −5 and +15 of a turn, 20 in a 25 pool).
        /// </summary>
        private static void ApplyCheckpointSplits(SwimRace race, SwimTiming timing, SwimTechPhase phase, Func<int, long?> splitAt)
        {
            int[] offsets = phase.Kind == SwimPhaseKind.Start ? StartOffsets : TurnOffsets;
            foreach (int offset in offsets)
            {
                if (phase.Splits.ContainsKey(offset))
                    continue;

                int d = phase.Wall + offset;
                if (d <= 0 || d >= race.Distance || !race.Checkpoints.Contains(d))
                    continue;

                long? t = splitAt(d);
                if (t.HasValue)
                {
                    double seconds = timing.Seconds(t.Value, phase.Origin);
                    if ((offset < 0 && seconds <= 0) || (offset > 0 && seconds > 0))
                        phase.Splits[offset] = seconds;
                }
            }
        }

        private static void TrainingPhases(SwimRace race, SwimTiming timing, List<SwimTechPhase> phases)
        {
            SwimHead head = SwimHead.Of(race);
            double k = race.K > 0 ? race.K : 1;
            List<SwimMark> origins = race.Marks
                .Where(m => m.Type == SwimMarkType.Signal || m.Type == SwimMarkType.WallContact)
                .OrderBy(m => m.Timestamp)
                .ToList();

            int starts = 0, turns = 0;
            foreach (SwimMark o in origins)
            {
                SwimTechPhase phase = new SwimTechPhase();
                phase.Id = o.Id;
                phase.Kind = o.Type == SwimMarkType.Signal ? SwimPhaseKind.Start : SwimPhaseKind.Turn;
                phase.Attempt = phase.Kind == SwimPhaseKind.Start ? ++starts : ++turns;
                phase.TitleGreek = (phase.Kind == SwimPhaseKind.Start ? "Εκκίνηση " : "Στροφή ") + phase.Attempt.ToString(CultureInfo.InvariantCulture);
                phase.Title = SwimLang.Label(phase.TitleGreek);
                phase.Flip = phase.Kind == SwimPhaseKind.Turn && (race.Style == "fs" || race.Style == "bk");
                phase.Origin = o.Timestamp;
                phases.Add(phase);
            }

            // Events: the approach after the previous attempt, the rest until the next attempt (at most 25 s).
            long lead = Before(timing, LeadSeconds);
            long search = Before(timing, ApproachSearchSeconds);
            long attempt = Before(timing, AttemptSeconds);
            for (int i = 0; i < phases.Count; i++)
            {
                SwimTechPhase p = phases[i];
                long before = p.Origin - (p.Kind == SwimPhaseKind.Start ? lead : search);
                p.EventStart = i > 0 ? Math.Max(phases[i - 1].Origin + 1, before) : before;
                p.EventEnd = Math.Min(i + 1 < phases.Count ? phases[i + 1].Origin : long.MaxValue, p.Origin + attempt);
                Fill(race, timing, p, false);
                SwimPhaseManual manual = race.GetPhaseValues(p.Id, false);
                if (manual != null)
                {
                    p.EntryDist = manual.EntryDist;
                    p.BreakoutDist = manual.BreakoutDist;
                    p.OutDist = manual.BreakoutDist;
                }

                // (2.15.0) A turn of the training video: the head at the push-off is the body length of the swimmer of the video.
                // (2.35.0) From his typical height (SwimHead). (2.41.0) Where the speed under the water starts; a start: the entry of
                // the head (of the hands in a video marked so before 2.41.0, the head behind them).
                if (p.Kind == SwimPhaseKind.Turn)
                    p.HeadAtPushoff = head.AtPushoff / k;
                else
                {
                    p.EntryHead = race.HeadEntry();
                    p.HeadBehindHands = p.EntryHead ? (double?)null : head.BehindHands / k;
                }
            }

            long frame = Frame(timing);
            for (int i = 0; i < phases.Count; i++)
            {
                SwimTechPhase p = phases[i];
                long approach = Math.Max(Before(timing, ApproachSeconds), Before(timing, -FirstApproachEvent(p)) + frame);
                long begin = p.Origin - (p.Kind == SwimPhaseKind.Start ? lead : approach);
                long finish = Math.Max(p.Origin + attempt, p.Origin + Before(timing, LastAfterEvent(p)) + frame);
                p.WindowStart = i > 0 ? Math.Max(begin, Boundary(timing, phases[i - 1], phases[i - 1].Origin, p, p.Origin)) : begin;
                p.WindowEnd = i + 1 < phases.Count ? Math.Min(finish, Boundary(timing, p, p.Origin, phases[i + 1], phases[i + 1].Origin)) : finish;
            }
        }

        /// <summary>
        /// Where the panel passes from one phase to the next (b begins at bTime): the approach of a turn or the lead of a start,
        /// at most half of the time between them, earlier for an approach marked before it (slow −5),
        /// never before the last event of the previous phase and never after the contact.
        /// </summary>
        private static long Boundary(SwimTiming timing, SwimTechPhase a, long aTime, SwimTechPhase b, long bTime)
        {
            long lead = Before(timing, b != null && b.Kind == SwimPhaseKind.Start ? LeadSeconds : ApproachSeconds);
            // One frame of margin around the events (the seconds of the marks are rounded to frames).
            long frame = Frame(timing);
            long boundary = Math.Max(bTime - lead, aTime + (bTime - aTime) / 2);
            long afterA = long.MinValue;
            if (a != null)
            {
                double last = LastAfterEvent(a);
                if (last > 0)
                    afterA = aTime + Before(timing, last) + frame;
            }

            boundary = Math.Max(boundary, afterA);
            double approach = b != null ? FirstApproachEvent(b) : 0;
            if (approach < 0)
            {
                long approachStart = bTime - Before(timing, -approach) - frame;
                if (approachStart < boundary)
                    boundary = Math.Max(approachStart, afterA);
            }

            // Walls marked in the wrong order: the windows still follow each other.
            boundary = Math.Max(boundary, aTime + 1);
            return Math.Min(boundary, bTime);
        }

        /// <summary>Seconds (negative) of the first event of the approach of a turn (−5, its strokes, rotation), 0 without one.</summary>
        private static double FirstApproachEvent(SwimTechPhase p)
        {
            double first = 0;
            double tIn;
            if (p.Splits.TryGetValue(-5, out tIn) && tIn < first)
                first = tIn;
            if (p.LastStroke.HasValue && p.LastStroke.Value < first)
                first = p.LastStroke.Value;
            if (p.RotationStart.HasValue && p.RotationStart.Value < first)
                first = p.RotationStart.Value;
            if (p.FirstApproachStroke.HasValue && p.FirstApproachStroke.Value < first)
                first = p.FirstApproachStroke.Value;
            return first;
        }

        /// <summary>Seconds of the last event after the origin of a phase (push-off, kicks, breakout, first stroke, 5/10/15).</summary>
        private static double LastAfterEvent(SwimTechPhase p)
        {
            double last = 0;
            foreach (double? v in new double?[] { p.Block, p.Entry, p.Pushoff, p.Breakout, p.FirstStroke })
            {
                if (v.HasValue && v.Value > last)
                    last = v.Value;
            }

            if (p.Kicks.Count > 0 && p.Kicks[p.Kicks.Count - 1] > last)
                last = p.Kicks[p.Kicks.Count - 1];
            foreach (KeyValuePair<int, double> split in p.Splits)
            {
                if (split.Key > 0 && split.Value > last)
                    last = split.Value;
            }

            return last;
        }

        private static long Before(SwimTiming timing, double seconds)
        {
            // Inverse of SwimTiming.Seconds: timestamps for a duration in race time.
            double hsf = timing.HighSpeedFactor <= 0 ? 1.0 : timing.HighSpeedFactor;
            double frames = seconds * 1000.0 * hsf / timing.FrameIntervalMilliseconds;
            return (long)Math.Round(frames * timing.AverageTimestampsPerFrame);
        }

        /// <summary>Timestamps for a duration in race time (seconds).</summary>
        public static long Ticks(SwimTiming timing, double seconds)
        {
            return timing == null || !timing.IsValid ? 0 : Before(timing, seconds);
        }

        private static long Frame(SwimTiming timing)
        {
            return Math.Max(1, (long)Math.Round(timing.AverageTimestampsPerFrame));
        }

        /// <summary>Walls where the swimmer turns or a relay leg ends.</summary>
        public static List<int> TurnWalls(SwimRace race)
        {
            List<int> walls = new List<int>();
            int pool = race == null || race.Pool <= 0 ? 50 : race.Pool;
            if (race == null)
                return walls;

            for (int w = pool; w < race.Distance; w += pool)
                walls.Add(w);
            return walls;
        }

        /// <summary>Relay: the end of a leg is an exchange, not a turn (as in SwimCalculator).</summary>
        public static bool IsExchange(SwimRace race, int wall)
        {
            return race.Relay && race.Distance % 4 == 0 && wall % (race.Distance / 4) == 0;
        }

        /// <summary>
        /// Race: the last wall already touched at a timestamp (0 before the first one). Tolerance: a wall up to this much later is
        /// touched too (half a frame: the walls of the auto pilot fall between two frames).
        /// </summary>
        public static int LastWallBefore(SwimRace race, long timestamp, long tolerance = 0)
        {
            int wall = 0;
            foreach (int w in TurnWalls(race))
            {
                SwimMark split = race.GetSplit(w);
                if (split != null && split.Timestamp <= timestamp + tolerance)
                    wall = w;
            }

            return wall;
        }

        /// <summary>Race: the technical distance is a checkpoint of the race (marked with the split key, not with T).</summary>
        public static bool IsCheckpoint(SwimRace race, int wall, int offset)
        {
            int d = wall + offset;
            return !race.IsTraining && d > 0 && d < race.Distance && race.Checkpoints.Contains(d);
        }

        /// <summary>
        /// Events of the phase from the marks of its event window. In a race the technical splits say their wall,
        /// in a training video the offset decides (−5 before the contact, the others after it).
        /// </summary>
        private static void Fill(SwimRace race, SwimTiming timing, SwimTechPhase p, bool wallMarks)
        {
            List<SwimMark> marks = race.Marks.Where(m => m.Timestamp >= p.EventStart && m.Timestamp < p.EventEnd).OrderBy(m => m.Timestamp).ToList();
            Func<SwimMark, double> rel = m => timing.Seconds(m.Timestamp, p.Origin);
            Func<SwimMarkType, Func<double, bool>, double?> first = (type, predicate) =>
            {
                foreach (SwimMark m in marks)
                {
                    if (m.Type != type)
                        continue;
                    double t = rel(m);
                    if (predicate(t))
                        return t;
                }

                return null;
            };
            Func<SwimMarkType, Func<double, bool>, double?> last = (type, predicate) =>
            {
                double? found = null;
                foreach (SwimMark m in marks)
                {
                    if (m.Type != type)
                        continue;
                    double t = rel(m);
                    if (predicate(t))
                        found = t;
                }

                return found;
            };

            IEnumerable<SwimMark> techSplits = wallMarks
                ? race.Marks.Where(m => m.Type == SwimMarkType.TechSplit && (m.Wall == p.Wall || (m.Wall < 0 && m.Timestamp >= p.EventStart && m.Timestamp < p.EventEnd)))
                    .OrderBy(m => m.Timestamp)
                : marks.Where(m => m.Type == SwimMarkType.TechSplit);
            foreach (SwimMark m in techSplits)
            {
                double t = rel(m);
                if ((m.Distance < 0 && t <= 0 && p.Kind == SwimPhaseKind.Turn) || (m.Distance > 0 && t > 0))
                    p.Splits[m.Distance] = t;
            }

            if (p.Kind == SwimPhaseKind.Start)
            {
                p.Reaction = first(SwimMarkType.Reaction, t => t > 0);
                p.HandsOff = first(SwimMarkType.HandsOff, t => t > 0);
                p.BackFoot = first(SwimMarkType.BackFoot, t => t > 0);
                p.Block = first(SwimMarkType.FeetOff, t => t > 0);
                p.Entry = first(SwimMarkType.Entry, t => t > 0);
                double after = p.Entry ?? p.Block ?? 0;
                p.Breakout = first(SwimMarkType.Breakout, t => t > after);
            }
            else
            {
                p.LastStroke = last(SwimMarkType.Stroke, t => t < 0 && t >= -LastStrokeSeconds);
                // A training video (the technical analysis): the window of the turn begins with the strokes of its approach (a race
                // marks its strokes all along the length: its windows stay as they were).
                if (!wallMarks)
                    p.FirstApproachStroke = first(SwimMarkType.Stroke, t => t < 0 && t >= -ApproachSearchSeconds);
                p.RotationStart = last(SwimMarkType.RotationStart, t => t <= 0);
                p.Pushoff = first(SwimMarkType.FeetOff, t => t >= 0);
                double after = p.Pushoff ?? 0;
                p.Breakout = first(SwimMarkType.Breakout, t => t > after);
            }

            double from = p.Kind == SwimPhaseKind.Start ? (p.Entry ?? p.Block ?? 0) : (p.Pushoff ?? 0);
            double to = p.Breakout ?? double.MaxValue;
            p.Kicks = marks.Where(m => m.Type == SwimMarkType.Kick).Select(rel).Where(t => t > from && t <= to).OrderBy(t => t).ToList();
            if (p.Breakout.HasValue)
            {
                // A stroke much later is not the first one (it was not marked).
                double b = p.Breakout.Value;
                p.FirstStroke = first(SwimMarkType.Stroke, t => t > b && t <= b + FirstStrokeSeconds);
            }
        }
        #endregion

        #region Values
        /// <summary>
        /// The values of a phase. (2.38.6) start: the start of the race already computed, for the rule of the turns (null: none, a
        /// relay or a training video).
        /// </summary>
        private static void ComputeValues(SwimRace race, SwimTechPhase p, SwimTechPhase start)
        {
            double k = race.K;
            Dictionary<string, double> v = p.Values;
            v.Clear();
            p.SpeedNote = null;
            p.RuleNote = null;
            p.OutForSpeeds = p.OutDist;
            p.SlowAfterBreakout = false;
            p.PushAway = 0;
            p.PastHead = false;
            p.UnderwaterMean = null;
            p.UnderwaterPeak = null;
            Action<string, double?> set = (key, value) =>
            {
                if (value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value))
                    v[key] = Math.Round(value.Value, 3);
            };
            Func<int, double?> split = offset =>
            {
                double t;
                return p.Splits.TryGetValue(offset, out t) ? (double?)t : null;
            };
            Func<double?, double?, double?> diff = (a, b) => a.HasValue && b.HasValue ? a.Value - b.Value : (double?)null;
            Func<double, double?, double?> vel = (units, time) => time.HasValue && time.Value > 0 ? units * k / time.Value : (double?)null;

            if (p.Kind == SwimPhaseKind.Start)
            {
                set("reaction", p.Reaction);
                set("handsOff", p.HandsOff);
                set("backFoot", p.BackFoot);
                set("blockTime", p.Block);
                // Only in the order they happen (a mark on a wrong frame would give a negative time).
                if (p.Block.HasValue && p.Reaction.HasValue && p.Block.Value > p.Reaction.Value)
                    set("blockPhase", p.Block.Value - p.Reaction.Value);
                if (p.Entry.HasValue && p.Block.HasValue && p.Entry.Value > p.Block.Value)
                    set("flight", p.Entry.Value - p.Block.Value);
                set("entryTime", p.Entry);
                set("entryDist", p.EntryDist);
                double? t5 = split(5), t10 = split(10), t15 = split(15);
                set("t5", t5);
                set("t10", t10);
                set("t15", t15);
                set("v0_5", vel(5, t5));
                set("v5_10", vel(5, diff(t10, t5)));
                set("v10_15", vel(5, diff(t15, t10)));
                set("underwater", diff(p.Breakout, p.Entry));
                // Distances are in pool units (μ. or yd), velocities in m/s.
                // (2.35.0) The phases of a start the analyst's way: from the signal to the entry, from the entry to the breakout and from
                // the breakout to the 15 (below). (2.41.0) From the head to the head, the analyst's request: the entry is where the head
                // went into the water (2.38.6–2.40.0 from where the hands entered: 3–7% slower), with the rule of the breakout. None from
                // a distance estimated from the times or that no swimmer has.
                BreakoutRule(p, t15, k);
                set("sigEntryVel", SignalEntrySpeed(p, k));
                double? under = EntryBreakoutSpeed(p, k);
                set("uwVel", under);
                // The distance per kick from where the head went in, as measured (2.38.6–2.40.0 from where the hands entered).
                double? headIn = HeadAtEntry(p);
                if (headIn.HasValue && p.OutDist.HasValue && p.Kicks.Count > 0 && !p.OutEstimated && !p.OutImpossible &&
                    p.OutDist.Value - headIn.Value >= UnderwaterMinSpan)
                    set("kickDist", (p.OutDist.Value - headIn.Value) / p.Kicks.Count);

                // (2.9.8) The speeds of the breakout: from the release from the block (marked, else the official reaction time) to the
                // breakout, and from the breakout to the head at 15.
                set("pushVel", BreakoutSpeed(p, p.Block ?? p.OfficialRelease, k, SwimLang.T("αποδέσμευση → έξοδος")));
                double? after = SpeedTo15(p, t15, k);
                set("bo15Vel", after);

                // (2.38.6) What the turns are held to: the mean speed under the water and the highest the video shows there.
                if (under.HasValue && after.HasValue)
                {
                    p.UnderwaterMean = under;
                    p.UnderwaterPeak = SwimSpeedRules.Peak(under.Value, after.Value, p.Breakout.Value - p.Entry.Value, t15.Value - p.Breakout.Value);
                }
            }
            else
            {
                double? tIn = split(-5), o5 = split(5), o10 = split(10), o15 = split(15);
                set("in5", tIn.HasValue ? -tIn.Value : (double?)null);
                set("lastStrokeGap", p.LastStroke.HasValue ? -p.LastStroke.Value : (double?)null);
                if (p.RotationStart.HasValue && p.RotationStart.Value <= 0)
                    set("rotation", -p.RotationStart.Value);
                set("contact", p.Pushoff);
                if (p.Pushoff.HasValue)
                {
                    if (!p.Flip)
                        set("turnTime", p.Pushoff);
                    else if (p.LastStroke.HasValue)
                        set("turnTime", p.Pushoff.Value - p.LastStroke.Value);
                }

                if (!race.IsTraining)
                {
                    SwimTurnManual manual = race.GetTurn(p.Wall, false);
                    if (manual != null && manual.TurnTime.HasValue)
                        set("turnTime", manual.TurnTime);
                }

                set("out5", o5);
                set("out10", o10);
                set("out15", o15);
                set("inOut5", diff(o5, tIn));
                set("inOut10", diff(o10, tIn));
                set("inOut15", diff(o15, tIn));
                set("v0_5", vel(5, o5));
                set("v5_10", vel(5, diff(o10, o5)));
                set("v10_15", vel(5, diff(o15, o10)));
                set("underwater", diff(p.Breakout, p.Pushoff));
                // (2.9.8) The speed from the push-off to the breakout (the "underwater" speed of a turn) and from the breakout to the head
                // at +15; not from a distance estimated from the times (the speed after the breakout would be the one the estimate took).
                // (2.41.0) From the head to the head, the analyst's request: at the push-off the head is HeadAtPushoff from the wall (the
                // typical height + 0.10; 2.38.6–2.40.0 from the wall: 20–45% faster, so that every turn of a tumble was held to the start),
                // with the analyst's two rules: the breakout (BreakoutRule) and the start (below). The distance per kick from the head too.
                BreakoutRule(p, o15, k);
                double headOut = p.HeadAtPushoff ?? 0;
                if (p.OutDist.HasValue && p.Kicks.Count > 0 && !p.OutEstimated && !p.OutImpossible && p.OutDist.Value - headOut >= UnderwaterMinSpan)
                    set("kickDist", (p.OutDist.Value - headOut) / p.Kicks.Count);
                // The speed to the +15 for the rule of the start first, its note after the one of the speed under the water (as before).
                string note = p.SpeedNote;
                double? after = SpeedTo15(p, o15, k);
                p.SpeedNote = note;
                StartRule(p, start, o15, after, k);
                set("uwVel", UnderwaterSpeed(p, k));
                set("bo15Vel", SpeedTo15(p, o15, k));
            }

            // 0 kicks only when kicks are marked in this video (otherwise they were not counted).
            if (p.Kicks.Count > 0 || (p.Breakout.HasValue && race.Marks.Any(m => m.Type == SwimMarkType.Kick)))
                set("kicks", p.Kicks.Count);
            if (p.Kicks.Count >= 2)
            {
                double span = p.Kicks[p.Kicks.Count - 1] - p.Kicks[0];
                if (span > 0)
                    set("kickRate", (p.Kicks.Count - 1) / span);
            }

            set("breakoutTime", p.Breakout);
            set("breakoutDist", p.BreakoutDist);
            set("firstStroke", p.FirstStroke);
        }

        /// <summary>(2.9.8) Shortest time a speed of the breakout is measured over (s); not the one from the breakout to 15 (2.35.1).</summary>
        internal const double SpeedMinTime = 0.3;

        /// <summary>
        /// (2.9.8) Plausible speeds (m/s) from the release or the push-off to the breakout, and from the breakout to 15: out of them a
        /// mark or the distance is wrong and no speed is given (the note says so).
        /// </summary>
        internal const double UnderwaterSpeedMin = 0.7, UnderwaterSpeedMax = 4.5, After15SpeedMin = 0.6, After15SpeedMax = 3.0;

        /// <summary>
        /// (2.9.8) Mean speed from 'from' (release from the block, push-off from the wall; seconds from the origin) to the breakout: the
        /// distance where the head came out from the wall over the time, as the race sheets have it. Null when something is missing, the
        /// distance is the estimate from the times or the speed is not plausible ('name' for the note).
        /// </summary>
        private static double? BreakoutSpeed(SwimTechPhase p, double? from, double k, string name)
        {
            if (!p.Breakout.HasValue || !from.HasValue || !Usable(p))
                return null;

            double time = p.Breakout.Value - from.Value;
            if (time < SpeedMinTime)
                return null;

            // (2.38.6) Where the head comes out for the speeds (the rule of the breakout), as the other speeds.
            double speed = Out(p) * k / time;
            if (speed >= UnderwaterSpeedMin && speed <= UnderwaterSpeedMax)
                return speed;

            Note(p, string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}: {1:0.00} m/s δεν γίνεται· έλεγξε την απόσταση της εξόδου και τα σημάδια"), name, speed));
            return null;
        }

        /// <summary>
        /// (2.15.0) Shortest way of the head under the water after a push-off for its speed (pool units): nearer, the clicks of the two
        /// distances (a few tenths each) would weigh too much.
        /// </summary>
        internal const double UnderwaterMinSpan = 1.0;

        /// <summary>(2.15.0) Plausible body lengths, toes to head (m): out of them the measurement is wrong.</summary>
        internal const double BodyLengthMin = 0.9, BodyLengthMax = 2.6;

        /// <summary>(2.35.0) Plausible mean speeds from the signal to the entry of the hands (m/s): out of them a mark or the distance is wrong.</summary>
        internal const double EntrySpeedMin = 1.0, EntrySpeedMax = 8.0;

        /// <summary>
        /// (2.35.0) Mean speed of a start from the signal to the entry of the hands: on the block at the signal the hands are at the
        /// wall, they enter the water at EntryDist. The time on the block is in it, as in the time to the 15.
        /// </summary>
        private static double? SignalEntrySpeed(SwimTechPhase p, double k)
        {
            if (!p.Entry.HasValue || p.Entry.Value < SpeedMinTime)
                return null;

            if (!p.EntryDist.HasValue || p.EntryDist.Value <= 0)
            {
                Note(p, p.EntryHead ? SwimLang.T("χρειάζεται η απόσταση εισόδου του κεφαλιού (📍 Μέτρηση ή γράψ' την)")
                    : SwimLang.T("χρειάζεται η απόσταση εισόδου των χεριών (📍 Μέτρηση ή γράψ' την)"));
                return null;
            }

            double speed = p.EntryDist.Value * k / p.Entry.Value;
            if (speed >= EntrySpeedMin && speed <= EntrySpeedMax)
                return speed;

            Note(p, string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}: {1:0.00} m/s δεν γίνεται· έλεγξε την απόσταση εισόδου και τα σημάδια"),
                SwimLang.T("σήμα → είσοδος"), speed));
            return null;
        }

        /// <summary>(2.38.6) Where the head comes out for the speeds: OutForSpeeds (the rule of the breakout), else OutDist.</summary>
        private static double Out(SwimTechPhase p)
        {
            return p.OutForSpeeds ?? p.OutDist ?? 0;
        }

        /// <summary>
        /// (2.41.0) Where the head is at the entry of a start (pool units from the wall): where it went into the water (EntryDist); in an
        /// analysis marked with the entry of the hands, HeadBehindHands behind them at that moment. Null without the distance.
        /// </summary>
        internal static double? HeadAtEntry(SwimTechPhase p)
        {
            if (p == null || !p.EntryDist.HasValue || p.EntryDist.Value <= 0)
                return null;

            return p.EntryHead ? p.EntryDist.Value : p.EntryDist.Value - (p.HeadBehindHands ?? 0);
        }

        /// <summary>
        /// (2.41.1) The analyst's rule of the breakout as a check, the analyst's choice («Μόνο έλεγχος»): from the breakout to the 15 (+15
        /// after a wall) the swimmer is expected to be not slower than in the swimming after it (NextSpeed). Slower: the speeds stay as
        /// measured (OutForSpeeds = OutDist) and the panel and the reports say it (SlowAfterBreakout, ⚠): he loses speed at the breakout,
        /// or the breakout was measured too far. 2.38.6–2.41.0 the speeds took the breakout nearer, where the speed to the 15 was the
        /// swimming's: from the head at the push-off the turns of a 400 were then about as slow as the swimming, the curve flat after the
        /// push-off. Only from a distance that gives speeds.
        /// </summary>
        private static void BreakoutRule(SwimTechPhase p, double? at15, double k)
        {
            p.OutForSpeeds = p.OutDist;
            p.SlowAfterBreakout = false;
            if (!p.OutDist.HasValue || p.OutDist.Value <= 0 || p.OutEstimated || p.OutImpossible || !p.Breakout.HasValue || !at15.HasValue ||
                !p.NextSpeed.HasValue)
                return;

            double time = at15.Value - p.Breakout.Value;
            if (!SwimSpeedRules.SlowerAfterBreakout(p.OutDist.Value, time, p.NextSpeed.Value, k))
                return;

            p.SlowAfterBreakout = true;
            bool start = p.Kind == SwimPhaseKind.Start;
            AddRule(p, string.Format(CultureInfo.InvariantCulture,
                SwimLang.T(start ? "⚠ έξοδος → 15: {0:0.00}, πιο αργά από την κολύμβηση μετά τα 15 ({1:0.00}): χάνει ταχύτητα στην έξοδο ή η απόσταση εξόδου μετρήθηκε πιο μακριά (έλεγξέ τη με 📍)"
                    : "⚠ έξοδος → +15: {0:0.00}, πιο αργά από την κολύμβηση μετά τα +15 ({1:0.00}): χάνει ταχύτητα στην έξοδο ή η απόσταση εξόδου μετρήθηκε πιο μακριά (έλεγξέ τη με 📍)"),
                (15 - p.OutDist.Value) * k / time, p.NextSpeed.Value));
        }

        /// <summary>
        /// (2.38.6) The analyst's rule of the start (SwimSpeedRules.PushAway): under the water after the push off a wall the swimmer is not
        /// faster than after the start, neither on average (the panel, the PDF) nor at the highest speed the video shows. A turn above the
        /// start's starts its speed a little out from the wall (PushAway), just so far that it is as the start; past where the head is at
        /// the push-off (the body length) its marks are off (PastHead: the warning of the panel). 'after': its speed to the +15 (m/s).
        /// </summary>
        private static void StartRule(SwimTechPhase p, SwimTechPhase start, double? at15, double? after, double k)
        {
            // (2.41.0) The speed under the water of a turn starts where the head is at the push-off (2.38.6–2.40.0 at the wall).
            p.PushAway = p.HeadAtPushoff ?? 0;
            p.PastHead = false;
            if (start == null || start == p || !start.UnderwaterMean.HasValue || !start.UnderwaterPeak.HasValue || !after.HasValue ||
                !at15.HasValue || !p.Pushoff.HasValue || !p.Breakout.HasValue || !p.OutForSpeeds.HasValue || p.OutEstimated || p.OutImpossible)
                return;

            double under = p.Breakout.Value - p.Pushoff.Value, afterTime = at15.Value - p.Breakout.Value;
            double out0 = p.OutForSpeeds.Value;
            if (under < SpeedMinTime || afterTime <= 0 || out0 < UnderwaterMinSpan)
                return;

            // (2.41.0) From the head at the push-off: farther only when even from there the turn is faster than the start (a mark is off).
            double head = Math.Max(0, Math.Min(p.PushAway, out0 - UnderwaterMinSpan));
            double mean = start.UnderwaterMean.Value, peak = start.UnderwaterPeak.Value;
            double away = SwimSpeedRules.PushAway(out0, under, after.Value, afterTime, k, mean, peak, out0 - UnderwaterMinSpan, head);
            if (!(away > head + 1e-9))
                return;

            p.PushAway = away;
            p.PastHead = true;
            AddRule(p, string.Format(CultureInfo.InvariantCulture,
                SwimLang.T("αποκόλληση → έξοδος: {0:0.00} από τα σημάδια, γράφεται {1:0.00}: όχι πιο γρήγορα από την εκκίνηση, ούτε στο βίντεο (κανόνας)"),
                (out0 - head) * k / under, (out0 - away) * k / under));
        }

        private static void AddRule(SwimTechPhase p, string note)
        {
            p.RuleNote = string.IsNullOrEmpty(p.RuleNote) ? note : p.RuleNote + "\n" + note;
        }

        /// <summary>
        /// (2.35.0) Mean speed of the underwater phase of a start, from the entry to the breakout. (2.41.0) From the head to the head, the
        /// analyst's request: from where the head went into the water (HeadAtEntry; in an analysis marked with the hands, the head behind
        /// them) to where it comes out (OutForSpeeds, the rule of the breakout). 2.38.6–2.40.0 from where the hands entered: 3–7% slower.
        /// Null, with a note, when something is missing, the distance is the estimate from the times or the result is not plausible.
        /// </summary>
        private static double? EntryBreakoutSpeed(SwimTechPhase p, double k)
        {
            string name = SwimLang.T("είσοδος → έξοδος");
            if (!p.Breakout.HasValue || !p.Entry.HasValue || !p.EntryDist.HasValue || p.EntryDist.Value <= 0 || !Usable(p))
                return null;

            double time = p.Breakout.Value - p.Entry.Value;
            if (time < SpeedMinTime)
                return null;

            double way = Out(p) - (HeadAtEntry(p) ?? p.EntryDist.Value);
            if (way < UnderwaterMinSpan)
            {
                Note(p, string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}: η έξοδος ({1:0.0}) είναι πολύ κοντά στην είσοδο ({2:0.0})· έλεγξε τις δύο αποστάσεις"),
                    name, p.OutDist.Value, p.EntryDist.Value));
                return null;
            }

            double speed = way * k / time;
            if (speed >= UnderwaterSpeedMin && speed <= UnderwaterSpeedMax)
                return speed;

            Note(p, string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}: {1:0.00} m/s δεν γίνεται· έλεγξε την απόσταση της εξόδου και τα σημάδια"), name, speed));
            return null;
        }

        /// <summary>
        /// (2.38.5) The analyst's rule: under the water after the push off a wall the swimmer is not faster than after the start (the dive
        /// gives more speed than the wall). (2.38.6) The speeds keep it (StartRule): a turn is warned only when the rule had to start its
        /// speed past where the head is at the push-off, that is when its marks give it faster than the start even from the head (a mark
        /// or a distance is off): the warning for the panel, else null. Not in a relay (another swimmer started) or a technique video.
        /// </summary>
        public static string FasterThanStart(SwimRace race, SwimTechPhase turn, SwimTechPhase start)
        {
            if (race == null || race.Relay || race.IsTraining || turn == null || start == null || turn.Kind != SwimPhaseKind.Turn ||
                start.Kind != SwimPhaseKind.Start || !turn.PastHead)
                return null;

            double? s = start.Value("uwVel");
            return string.Format(CultureInfo.InvariantCulture,
                SwimLang.T("⚠ τα σημάδια δίνουν τη στροφή πολύ πιο γρήγορη από την εκκίνηση ({0:0.00} m/s): έλεγξε την αποκόλληση, την έξοδο και την απόσταση της εξόδου (και της εκκίνησης)"),
                s ?? 0);
        }

        /// <summary>
        /// (2.15.0) Mean speed of the underwater phase of a turn, from the push-off to the breakout. (2.41.0) From the head to the head, the
        /// analyst's request: at the push-off the toes leave the wall with the body straight, so the head is HeadAtPushoff from it (the
        /// typical height + 0.10, SwimHead: nothing typed), at the breakout where it comes out (OutForSpeeds, the rule of the breakout);
        /// farther than the head only by the rule of the start (PushAway, a warning). 2.38.6–2.40.0 from the wall: 20–25% faster with a
        /// breakout at 9–10 m, 40–50% at 5–6 m. Null, with a note, when something is missing, the distance is the estimate from the times
        /// or the result is not plausible.
        /// </summary>
        private static double? UnderwaterSpeed(SwimTechPhase p, double k)
        {
            string name = SwimLang.T("αποκόλληση → έξοδος");
            if (!p.Breakout.HasValue || !p.Pushoff.HasValue || !Usable(p))
                return null;

            double time = p.Breakout.Value - p.Pushoff.Value;
            if (time < SpeedMinTime)
                return null;

            double way = Out(p) - p.PushAway;
            if (way < UnderwaterMinSpan)
            {
                Note(p, string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}: η έξοδος ({1:0.0}) είναι πολύ κοντά στο κεφάλι στην αποκόλληση ({2:0.0})· έλεγξε την απόσταση της εξόδου"),
                    name, p.OutDist.Value, p.PushAway));
                return null;
            }

            double speed = way * k / time;
            if (speed >= UnderwaterSpeedMin && speed <= UnderwaterSpeedMax)
                return speed;

            Note(p, string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}: {1:0.00} m/s δεν γίνεται· έλεγξε την απόσταση της εξόδου και τα σημάδια"), name, speed));
            return null;
        }

        /// <summary>
        /// (2.9.8) Mean speed from the breakout to the head at 15 ('at15': seconds from the origin): what is left to the 15 over the time.
        /// (2.35.1) For every breakout before the 15, the analyst's request (2.9.8–2.35.0 only for one 2 and more before it: the late
        /// breakouts, often in backstroke and butterfly, had none). The nearer the 15, the more the breakout's distance weighs: a tenth
        /// off at 1 before the 15 is a tenth of the speed, at 3 before it a thirtieth. (2.38.6) From OutForSpeeds (the rule of the
        /// breakout). Null when something is missing, the breakout is not before the 15, its mark is not before the head at 15 or the
        /// speed is not plausible (the note says which).
        /// </summary>
        private static double? SpeedTo15(SwimTechPhase p, double? at15, double k)
        {
            if (!p.Breakout.HasValue || !at15.HasValue || !Usable(p))
                return null;

            string name = p.Kind == SwimPhaseKind.Start ? SwimLang.T("έξοδος → 15") : SwimLang.T("έξοδος → +15");
            double span = 15 - Out(p);
            if (span <= 0)
            {
                Note(p, string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}: η έξοδος ({1:0.0}) δεν είναι πριν από τα 15"), name, p.OutDist.Value));
                return null;
            }

            double time = at15.Value - p.Breakout.Value;
            if (time <= 0)
            {
                Note(p, string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}: το σημάδι της εξόδου είναι μετά το κεφάλι στα 15· έλεγξε τα σημάδια"), name));
                return null;
            }

            double speed = span * k / time;
            if (speed >= After15SpeedMin && speed <= After15SpeedMax)
                return speed;

            Note(p, string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0}: {1:0.00} m/s δεν γίνεται· έλεγξε την απόσταση της εξόδου και τα σημάδια"), name, speed));
            return null;
        }

        /// <summary>(2.9.8) The distance where the head came out can give the speeds (else the note says why).</summary>
        private static bool Usable(SwimTechPhase p)
        {
            if (!p.OutDist.HasValue || p.OutDist.Value <= 0)
            {
                Note(p, SwimLang.T("χρειάζεται η απόσταση της εξόδου (📍 Μέτρηση ή γράψ' την)"));
                return false;
            }

            if (p.OutEstimated)
            {
                Note(p, SwimLang.T("η απόσταση της εξόδου είναι εκτίμηση από τους χρόνους: μέτρησέ τη (📍) για τις ταχύτητες"));
                return false;
            }

            if (p.OutImpossible)
            {
                Note(p, string.Format(CultureInfo.InvariantCulture, SwimLang.T("η απόσταση της εξόδου ({0:0.00}) δεν γίνεται: μέτρησέ τη ξανά (📍)"), p.OutDist.Value));
                return false;
            }

            return true;
        }

        private static void Note(SwimTechPhase p, string note)
        {
            if (p.SpeedNote == null)
                p.SpeedNote = note;
        }
        #endregion

        #region Next technical split
        /// <summary>
        /// Offset of the next technical split for a press of T at this timestamp: the next distance of the phase not marked yet
        /// (5, 10, 15 after a signal; +5, +10, +15 after a contact), then −5 before the next wall. The checkpoints of a race
        /// are skipped (they are marked with the split key). Null when there is nothing left to mark with T.
        /// </summary>
        public static int? NextOffset(SwimRace race, SwimTiming timing, List<SwimTechPhase> phases, long timestamp, out SwimTechPhase phase)
        {
            phase = null;
            if (race == null || timing == null || !timing.IsValid || phases == null)
                return null;

            SwimTechPhase current = OwnerOf(phases, timestamp);
            if (current != null && current.Contains(timestamp))
            {
                double t = timing.Seconds(timestamp, current.Origin);
                if (t > 0 && t <= PositiveSeconds)
                {
                    foreach (int offset in current.Kind == SwimPhaseKind.Start ? StartOffsets : TurnOffsets)
                    {
                        if (offset <= 0 || current.Splits.ContainsKey(offset) || IsCheckpoint(race, current.Wall, offset))
                            continue;

                        // Passed already: a farther distance is marked before this press.
                        if (current.Splits.Any(s => s.Key > offset && s.Value <= t))
                            continue;

                        // Too late for this distance at the pace of the previous ones (e.g. +15 not visible on the video):
                        // the press is for the next wall.
                        if (t > Latest(race, current, offset))
                            break;

                        phase = current;
                        return offset;
                    }
                }
            }

            if (race.IsTraining)
            {
                // The approach of the next contact (if it is marked already).
                phase = phases.FirstOrDefault(p => p.Kind == SwimPhaseKind.Turn && p.Origin >= timestamp && p.Contains(timestamp));
                return -5;
            }

            int pool = race.Pool <= 0 ? 50 : race.Pool;
            int wall = LastWallBefore(race, timestamp) + pool;
            if (wall >= race.Distance || IsExchange(race, wall) || IsCheckpoint(race, wall, -5))
                return null;

            phase = phases.FirstOrDefault(p => p.Kind == SwimPhaseKind.Turn && p.Wall == wall);
            return -5;
        }

        /// <summary>
        /// Latest plausible time of the head at a distance, from the times at the previous distances of the phase.
        /// In a 25 pool the −5 of the next wall comes only two intervals of 5 later, so the margin is smaller.
        /// </summary>
        private static double Latest(SwimRace race, SwimTechPhase p, int offset)
        {
            bool shortPool = race.Pool > 0 && race.Pool <= 25;
            double tp, tpp;
            bool hasPrevious = p.Splits.TryGetValue(offset - 5, out tp);
            bool hasBefore = p.Splits.TryGetValue(offset - 10, out tpp);
            if (hasPrevious && hasBefore && tp > tpp)
            {
                double interval = tp - tpp;
                if (shortPool)
                    return tp + Math.Max(1.5, (p.Kind == SwimPhaseKind.Start ? 2.0 : 1.5) * interval);
                return tp + Math.Max(3.0, 3 * interval);
            }

            if (hasPrevious && tp > 0)
                return tp + Math.Max(3.0, 2.5 * tp);
            return PositiveSeconds;
        }

        /// <summary>The phase whose origin (signal or contact) is the last one before a timestamp, while its events last.</summary>
        public static SwimTechPhase OwnerOf(List<SwimTechPhase> phases, long timestamp)
        {
            if (phases == null)
                return null;

            return phases.Where(p => p.Origin <= timestamp && timestamp < p.EventEnd).OrderBy(p => p.Origin).LastOrDefault();
        }
        #endregion
    }
    /// <summary>
    /// (2.38.6) The analyst's two rules on the speeds of the start and the turns, the same in the panel and the PDF (SwimTechCalculator)
    /// and on the video (SwimReel), each on its own times:
    /// 1. The breakout: from the breakout to the 15 (+15 after a wall) the swimmer is not slower than in the swimming after it (the
    ///    push-off still carries him; in the finals of a European championship, 303 turns with both times, the same speed on average).
    ///    (2.41.1) A check, the analyst's choice («Μόνο έλεγχος»): slower, the speeds stay as measured and the panel and the reports
    ///    warn (SlowerAfterBreakout: he loses speed at the breakout, or the breakout was measured too far). 2.38.6–2.41.0 the speeds took
    ///    the breakout nearer, where the speed to the 15 was the swimming's («Πιο πάνω να είναι, πιο κάτω είναι λάθος»): from the head
    ///    at the push-off (2.41.0) the turns of a 400 lost their speed after the push-off, the analyst saw it on the video.
    /// 2. The start: under the water after the push off a wall the swimmer is not faster than after the start (the dive gives more than
    ///    the wall), neither on average nor at the highest speed the video shows. Above it, the speed of the turn starts a little out
    ///    from the wall (where the head is at the push-off is between the wall and the body length), just so far that it is as the start.
    ///    (2.41.0) Both from the head to the head: the speed of a turn starts where the head is at the push-off, farther only when even
    ///    from there it is faster than the start (a mark or a distance is off: the panel warns).
    /// </summary>
    public static class SwimSpeedRules
    {
        /// <summary>
        /// (2.39.4) The breakout is never taken nearer than this to a wall (pool units): the head is already about a body length out at
        /// the push-off, and 📍 writes no breakout under 2 m. A rule that would need it nearer: the marks are off, as measured.
        /// </summary>
        public const double LeastOut = 2.0;

        /// <summary>
        /// The speed of the clean swimming after the 15 of a wall (0: the start), as the table of the race has it (m/s): the segment from
        /// the 15 (+15) to the next checkpoint; null without one (no checkpoint there, or not clean swimming).
        /// </summary>
        public static double? NextSpeed(SwimComputed computed, int wall)
        {
            if (computed == null || computed.Rows == null)
                return null;

            SwimRow row = computed.Rows.FirstOrDefault(r => r.PrevD == wall + 15 && r.IsFree && r.Vel.HasValue && r.Vel.Value > 0);
            return row != null ? row.Vel : null;
        }

        /// <summary>
        /// Rule 1, a check (2.41.1): the speed from the breakout (outDist, pool units from the wall) to the 15 in timeTo15 (s) is below the
        /// swimming after it (nextSpeed, m/s). k: metres per pool unit. (2.39.3) As fast to a millionth of a m/s (the same speed, other
        /// roundings of the times): not slower.
        /// </summary>
        public static bool SlowerAfterBreakout(double outDist, double timeTo15, double nextSpeed, double k)
        {
            if (!(timeTo15 > 0) || !(nextSpeed > 0) || !(outDist > 0) || !(outDist < 15) || !(k > 0))
                return false;

            return (15 - outDist) * k / timeTo15 < nextSpeed - 1e-6;
        }

        /// <summary>
        /// The highest speed the curve of the video shows under the water after the entry or the push-off, in the unit of the speeds given:
        /// its piece from there to the breakout (SwimReel.BuildSlopes) starts with PCHIP's end slope from the two segments (under the water,
        /// then to the 15) and ends with the speed to the 15 (2.9.5); the highest of the cubic between. under, after: the mean speeds under
        /// the water and from the breakout to the 15; underTime, afterTime: their times (s).
        /// </summary>
        public static double Peak(double under, double after, double underTime, double afterTime)
        {
            double m0 = SwimReel.EndSlope(underTime, afterTime, under, after), m1 = after;
            // The speed along the piece (s from 0 to 1): m0 + b s + c s², the derivative of the cubic of SwimReel.Slope.
            double b = 6 * under - 4 * m0 - 2 * m1, c = -6 * under + 3 * m0 + 3 * m1;
            double peak = Math.Max(m0, m1);
            if (c < 0)
            {
                double s = -b / (2 * c);
                if (s > 0 && s < 1)
                    peak = Math.Max(peak, m0 + b * s + c * s * s);
            }

            return peak;
        }

        /// <summary>
        /// Rule 2: the speed under the water of a turn, starting 'away' from the wall (pool units), is not above the start's: its mean
        /// (outDist − away over underTime) not above startMean, its highest on the video (Peak, with afterSpeed over afterTime to the +15)
        /// not above startPeak. k: metres per pool unit; the speeds in m/s.
        /// </summary>
        public static bool NotFaster(double outDist, double away, double underTime, double afterSpeed, double afterTime, double k, double startMean,
            double startPeak)
        {
            if (!(underTime > 0) || !(afterTime > 0) || !(k > 0))
                return true;

            double under = (outDist - away) * k / underTime;
            return under <= startMean + 1e-9 && Peak(under, afterSpeed, underTime, afterTime) <= startPeak + 1e-9;
        }

        /// <summary>
        /// Rule 2: how far from the wall (pool units) the speed under the water of a turn starts: 'least' (2.41.0: where the head is at the
        /// push-off; 0, the wall, before) when it is not faster than the start (NotFaster), else just so far that it is not; at most 'most'
        /// (then it may still be).
        /// </summary>
        public static double PushAway(double outDist, double underTime, double afterSpeed, double afterTime, double k, double startMean, double startPeak,
            double most, double least = 0)
        {
            least = Math.Max(0, least);
            if (NotFaster(outDist, least, underTime, afterSpeed, afterTime, k, startMean, startPeak) || !(most > least))
                return least;
            if (!NotFaster(outDist, most, underTime, afterSpeed, afterTime, k, startMean, startPeak))
                return most;

            double lo = least, hi = most;
            for (int i = 0; i < 60; i++)
            {
                double mid = (lo + hi) / 2;
                if (NotFaster(outDist, mid, underTime, afterSpeed, afterTime, k, startMean, startPeak))
                    hi = mid;
                else
                    lo = mid;
            }

            return hi;
        }
    }

    /// <summary>
    /// (2.35.0) Where the head of the swimmer is at the two moments the analyst marks without it: the entry of the hands of a start
    /// (the head is behind the hands) and the push-off of a turn (the toes leave the wall, the head is a body length away). The head
    /// is not clicked there (under the water it is not seen well): its place comes from the height of the swimmer, typical for his
    /// gender and age (the athlete of the registry: nothing is typed). Behind the hands: 0.27 × height, the reach of the arms over
    /// the head (≈ 1/3 of the height) at an entry of 30–40°; at the push-off: height + 0.10 m (the toes pointed). (2.36.0) A body
    /// length measured or typed before (SwimRace.BodyLength, toes → head) is no longer used: its field went, at the analyst's request,
    /// and a value nobody sees would make the speeds of one race differ from the others.
    /// An error of 10 cm in the height: about 1.5% in the underwater speed of a turn, less than 0.5% in that of a start.
    /// </summary>
    public sealed class SwimHead
    {
        /// <summary>The head behind the hands at their entry, a part of the height.</summary>
        public const double BehindHandsShare = 0.27;

        /// <summary>The pointed toes beyond the soles (m): the body length at the push-off is the height + this.</summary>
        public const double ToesPointed = 0.10;

        /// <summary>
        /// Typical heights of swimmers (m) by age, 10 years and younger up to the adults (18+): about 4–5 cm above the medians of
        /// the growth charts, as swimmers of a national level are.
        /// </summary>
        private static readonly double[] MaleHeights = { 1.42, 1.49, 1.56, 1.63, 1.70, 1.76, 1.80, 1.83, 1.85 };
        private static readonly double[] FemaleHeights = { 1.42, 1.50, 1.56, 1.62, 1.66, 1.69, 1.71, 1.72, 1.72 };

        /// <summary>The height used (m).</summary>
        public double Height;

        /// <summary>"M", "F" or "" (unknown: the middle of the two).</summary>
        public string Gender = "";

        /// <summary>Age in the year of the race, 0 when unknown (the adult heights).</summary>
        public int Age;

        /// <summary>The head behind the hands at their entry (m).</summary>
        public double BehindHands;

        /// <summary>The head from the wall at the push-off of a turn (m).</summary>
        public double AtPushoff;

        public static SwimHead Of(SwimRace race)
        {
            SwimHead h = new SwimHead();
            if (race == null)
            {
                h.Height = Typical("", 0);
                h.BehindHands = BehindHandsShare * h.Height;
                h.AtPushoff = h.Height + ToesPointed;
                return h;
            }

            string g = SwimReference.NormalizeGender(race.AthleteGender);
            h.Gender = g == "M" || g == "F" ? g : "";
            h.Age = AgeOf(race.AthleteBirthYear, race.Date);
            h.Height = Typical(h.Gender, h.Age);
            h.BehindHands = BehindHandsShare * h.Height;
            h.AtPushoff = h.Height + ToesPointed;
            return h;
        }

        /// <summary>The typical height (m) for a gender ("M", "F", "") and an age (0: unknown, the adults).</summary>
        public static double Typical(string gender, int age)
        {
            int i = age <= 0 ? MaleHeights.Length - 1 : Math.Max(0, Math.Min(MaleHeights.Length - 1, age - 10));
            if (gender == "M")
                return MaleHeights[i];
            if (gender == "F")
                return FemaleHeights[i];
            return Math.Round((MaleHeights[i] + FemaleHeights[i]) / 2, 3);
        }

        /// <summary>The age in the year of the race (its date "d.M.yyyy", else this year); 0 when the year of birth is not known.</summary>
        public static int AgeOf(string birthYear, string raceDate)
        {
            int born;
            if (string.IsNullOrWhiteSpace(birthYear) || !int.TryParse(birthYear.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out born))
                return 0;

            int year = DateTime.Now.Year;
            DateTime d;
            string[] formats = { "d.M.yyyy", "dd.MM.yyyy", "d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "d-M-yyyy" };
            if (!string.IsNullOrWhiteSpace(raceDate) &&
                DateTime.TryParseExact(raceDate.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                year = d.Year;

            if (born < 100)
                born += born <= year % 100 ? 2000 : 1900;
            int age = year - born;
            return age >= 5 && age <= 90 ? age : 0;
        }

        /// <summary>Who the typical height is of, for the notes of the panel: "άνδρες 18+", "κορίτσια 14 ετών"…</summary>
        public string Who()
        {
            bool adult = Age <= 0 || Age >= 18;
            if (Gender == "M")
                return adult ? SwimLang.T("άνδρες 18+") : string.Format(CultureInfo.InvariantCulture, SwimLang.T("αγόρια {0} ετών"), Age);
            if (Gender == "F")
                return adult ? SwimLang.T("γυναίκες 18+") : string.Format(CultureInfo.InvariantCulture, SwimLang.T("κορίτσια {0} ετών"), Age);
            return adult ? SwimLang.T("χωρίς φύλο, 18+") : string.Format(CultureInfo.InvariantCulture, SwimLang.T("χωρίς φύλο, {0} ετών"), Age);
        }
    }
}
