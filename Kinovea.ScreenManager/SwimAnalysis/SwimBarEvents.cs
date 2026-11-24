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
    /// <summary>(2.37.0) A distance of the race that a row of the events of the bar changes (typed or with the wheel).</summary>
    public enum SwimBarDistance
    {
        None,

        /// <summary>The entry of the hands after the start, from the start wall (SwimRace.EntryDistHands).</summary>
        EntryHands,

        /// <summary>The head out of the water after the start (SwimRace.BreakoutDist).</summary>
        StartBreakout,

        /// <summary>The head out of the water after a turn, from its wall (SwimTurnManual.UnderwaterDist).</summary>
        TurnBreakout,

        /// <summary>The last stroke to the wall of the finish (SwimRace.FinishLastStrokeDist).</summary>
        FinishLastStroke
    }

    /// <summary>
    /// (2.37.0) One event of the race in the list of the bar beside the video («ΓΕΓΟΝΟΤΑ»): its group, its name, its race time (none: not
    /// marked yet) and its value (a text, or a distance that is changed in the row).
    /// </summary>
    public sealed class SwimBarEvent
    {
        /// <summary>Unique in the race and stable while it is analysed ("start", "cp25", "pushoff50", …).</summary>
        public string Id;

        /// <summary>The title of its group ("ΕΚΚΙΝΗΣΗ", "ΣΤΡΟΦΗ 50μ.", …): a header in the list where it changes.</summary>
        public string Group;

        /// <summary>0 start, 1 swimming, 2 turn (or exchange), 3 finish: the color of its dot.</summary>
        public int Phase;

        public string Label;

        /// <summary>Race time (s from the start signal); null: not marked yet.</summary>
        public double? Time;

        /// <summary>The time on the video when it differs from Time (an official time: the mark of the analyst is elsewhere).</summary>
        public double? SeekTime;

        /// <summary>A checkpoint (its distance): the click goes through the checkpoint (it can be marked again there).</summary>
        public int Checkpoint;

        /// <summary>The checkpoint is marked (an official time without a mark is still to mark).</summary>
        public bool Marked;

        /// <summary>The value written beside the time ("2.24 m/s", "9 χερ. ✓", "× 5", "0.72 s"); null for none.</summary>
        public string Value;

        /// <summary>The value is the analyst's (strokes typed or accepted): in green.</summary>
        public bool ValueOk;

        /// <summary>The strokes of the checkpoint's segment can be changed from the value (SwimAnalysisManager.EditSegmentStrokes).</summary>
        public bool StrokesEditable;

        /// <summary>The value is the strokes of the checkpoint's segment (else its speed).</summary>
        public bool StrokesValue;

        /// <summary>A distance changed in the row, and its wall (TurnBreakout).</summary>
        public SwimBarDistance Distance;
        public int Wall;
        public double? DistanceValue;

        /// <summary>The distance is the estimate from the times (written ≈, not measured or typed).</summary>
        public bool DistanceEstimated;

        /// <summary>From empty, the first step of the wheel goes to this value (the usual distance).</summary>
        public double Seed;

        /// <summary>Put by the auto pilot and not checked yet (written ✈).</summary>
        public bool Pilot;

        /// <summary>An official time of the results.</summary>
        public bool Official;

        public string Tip;

        /// <summary>The time to show on the video for it (the mark, else the time), null when it has none.</summary>
        public double? VideoTime
        {
            get { return SeekTime ?? Time; }
        }
    }

    /// <summary>
    /// (2.37.0) The events of a race in their order, from the start signal to the finish, for the list of the bar beside the video: the
    /// start (signal, first movement, release, entry, kicks, breakout), the checkpoints of each length, each turn (the touch, the push off,
    /// the kicks, the breakout) and the finish. The events not marked yet are in their place without a time: the analyst sees what is
    /// still to mark. Built from the race and its computation (SwimCalculator), nothing of the screen: also in the CI check.
    /// </summary>
    public static class SwimBarEvents
    {
        /// <summary>The usual distances (pool units) where the wheel starts from an empty value.</summary>
        public const double SeedEntry = 3.0;
        public const double SeedBreakout = 10.0;
        public const double SeedTurnBreakout = 8.0;
        public const double SeedLastStroke = 1.0;

        public static List<SwimBarEvent> Build(SwimRace race, SwimComputed c)
        {
            List<SwimBarEvent> list = new List<SwimBarEvent>();
            if (race == null || race.IsTraining)
                return list;
            if (c == null)
                c = new SwimComputed();

            string unit = race.Unit;
            int pool = Math.Max(1, race.Pool);
            int distance = Math.Max(pool, race.Distance);
            List<SwimRow> rows = c.Rows ?? new List<SwimRow>();
            Dictionary<int, SwimRow> rowAt = new Dictionary<int, SwimRow>();
            foreach (SwimRow r in rows)
                rowAt[r.D] = r;

            // Start.
            string start = SwimLang.T("ΕΚΚΙΝΗΣΗ");
            double? firstWall = WallTime(rowAt, Math.Min(pool, distance));
            list.Add(new SwimBarEvent
            {
                Id = "start",
                Group = start,
                Phase = 0,
                Label = SwimLang.T("Σήμα εκκίνησης"),
                Time = race.StartTimestamp.HasValue ? (double?)0 : null,
                Value = race.StartTimestamp.HasValue ? (race.PilotStart ? "✈?" : "✓") : null,
                ValueOk = race.StartTimestamp.HasValue && !race.PilotStart,
                Pilot = race.PilotStart,
                Tip = race.PilotStart ? SwimLang.T("Την έβαλε ο αυτόματος πιλότος: έλεγξέ τη (G στο σωστό καρέ).") : SwimLang.T("Το σήμα εκκίνησης: t = 0.")
            });
            list.Add(Event("reaction", start, 0, SwimLang.T("Αντίδραση"), c.ReactionTime, null, SwimLang.T("Η πρώτη κίνηση μετά το σήμα.")));
            SwimBarEvent block = Event("block", start, 0, SwimLang.T("Αποδέσμευση"), c.BlockTime, null,
                SwimLang.T("Τα πόδια φεύγουν από τον βατήρα (ο επίσημος χρόνος αντίδρασης)."));
            if (c.BlockOfficial)
            {
                block.Official = true;
                block.SeekTime = c.BlockMarkTime;
                block.Value = SwimLang.T("επίσ.");
                block.Tip += SwimLang.T("\nΟ χρόνος είναι ο επίσημος των αποτελεσμάτων.");
            }
            list.Add(block);

            SwimBarEvent entry = Event("entry", start, 0, SwimLang.T("Είσοδος χεριών"), c.EntryTime, null,
                SwimLang.T("Τα χέρια μπαίνουν στο νερό · απόσταση από τον τοίχο εκκίνησης."));
            SetDistance(entry, SwimBarDistance.EntryHands, 0, race.EntryDistHands, false, SeedEntry);
            list.Add(entry);

            double? startBreakout = c.StartUnderwaterTime;
            List<double> kicks = c.Kicks ?? new List<double>();
            double kicksFrom = c.EntryTime ?? 0;
            double kicksTo = startBreakout ?? firstWall ?? double.MaxValue;
            double? firstKick = First(kicks, t => t > kicksFrom && t <= kicksTo);
            list.Add(Event("kicks0", start, 0, SwimLang.T("Δελφινισμοί"), c.StartKicks.HasValue ? firstKick : null, Count(c.StartKicks),
                SwimLang.T("Οι υποβρύχιοι δελφινισμοί μετά την είσοδο (ο χρόνος του πρώτου).")));

            SwimBarEvent breakout = Event("breakout0", start, 0, SwimLang.T("Έξοδος"), startBreakout, null,
                SwimLang.T("Το κεφάλι βγαίνει από το νερό · απόσταση από τον τοίχο εκκίνησης."));
            // The breakout typed or measured, else the distance under the water (≈ when it is the estimate from the times).
            if (race.BreakoutDist.HasValue)
                SetDistance(breakout, SwimBarDistance.StartBreakout, 0, race.BreakoutDist, false, SeedBreakout);
            else
                SetDistance(breakout, SwimBarDistance.StartBreakout, 0, race.UnderwaterDist, race.UnderwaterEstimated && race.UnderwaterDist.HasValue, SeedBreakout);
            list.Add(breakout);

            // The lengths: their checkpoints, the turn at their end, and the finish.
            List<int> checkpoints = rows.Select(r => r.D).Where(d => d > 0).Distinct().OrderBy(d => d).ToList();
            int next = rows.Where(r => !r.Marked).Select(r => r.D).DefaultIfEmpty(-1).Min();
            for (int wall = 0; wall < distance; wall += pool)
            {
                int end = Math.Min(wall + pool, distance);
                string swim = string.Format(CultureInfo.InvariantCulture, SwimLang.T("ΚΟΛΥΜΒΗΣΗ {0}–{1}{2}"), wall, end, unit);
                foreach (int d in checkpoints.Where(x => x > wall && x < end))
                    list.Add(Checkpoint(rowAt[d], swim, 1, d.ToString(CultureInfo.InvariantCulture) + unit, unit, next));

                if (end >= distance)
                    break;

                SwimTurnComputed turn = (c.Turns ?? new List<SwimTurnComputed>()).FirstOrDefault(t => t.Wall == end);
                bool exchange = turn != null ? turn.Exchange : race.Relay && distance % 4 == 0 && end % (distance / 4) == 0;
                string styleIn = SwimPresets.StyleAt(race, end);
                string styleOut = SwimPresets.StyleAt(race, end + pool);
                string title = exchange ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("ΑΛΛΑΓΗ ΣΚΥΤΑΛΗΣ {0}{1}"), end, unit)
                    : styleIn != styleOut ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("ΑΛΛΑΓΗ ΣΤΥΛ {0}{1}"), end, unit)
                    : string.Format(CultureInfo.InvariantCulture, SwimLang.T("ΣΤΡΟΦΗ {0}{1}"), end, unit);

                SwimRow wallRow;
                if (rowAt.TryGetValue(end, out wallRow))
                    list.Add(Checkpoint(wallRow, title, 2, SwimLang.T("Επαφή ") + end.ToString(CultureInfo.InvariantCulture) + unit, unit, next));

                string pushoffLabel = exchange ? SwimLang.T("Ξεκίνημα επόμενου") : SwimLang.T("Αποκόλληση");
                string pushoffTip = exchange ? SwimLang.T("Ο επόμενος κολυμβητής φεύγει από τον βατήρα · ο χρόνος της αλλαγής από την επαφή.")
                    : turn != null && turn.Flip ? SwimLang.T("Τα πόδια φεύγουν από τον τοίχο · ο χρόνος της στροφής από την τελευταία χεριά.")
                    : SwimLang.T("Τα πόδια φεύγουν από τον τοίχο · ο χρόνος της στροφής από την επαφή.");
                list.Add(Event("pushoff" + end.ToString(CultureInfo.InvariantCulture), title, 2, pushoffLabel, turn != null ? turn.Pushoff : null,
                    turn != null && turn.TurnTime.HasValue ? Seconds(turn.TurnTime.Value) + " s" : null, pushoffTip));

                double? wallT = WallTime(rowAt, end);
                double? turnBreakout = turn != null ? turn.Breakout : null;
                double kickFrom = turn != null && turn.Pushoff.HasValue ? turn.Pushoff.Value : (wallT ?? double.MaxValue);
                double kickTo = turnBreakout ?? WallTime(rowAt, Math.Min(end + pool, distance)) ?? double.MaxValue;
                double? turnKick = First(kicks, t => t > kickFrom && t <= kickTo);
                int? turnKicks = turn != null ? turn.Kicks : null;
                list.Add(Event("kicks" + end.ToString(CultureInfo.InvariantCulture), title, 2, SwimLang.T("Δελφινισμοί"),
                    turnKicks.HasValue && turnKicks.Value > 0 ? turnKick : null, Count(turnKicks), SwimLang.T("Οι υποβρύχιοι δελφινισμοί μετά τον τοίχο (ο χρόνος του πρώτου).")));

                SwimTurnManual manual = null;
                if (race.Turns != null)
                    race.Turns.TryGetValue(end, out manual);
                SwimBarEvent turnOut = Event("breakout" + end.ToString(CultureInfo.InvariantCulture), title, 2, SwimLang.T("Έξοδος"), turnBreakout, null,
                    SwimLang.T("Το κεφάλι βγαίνει από το νερό · απόσταση από τον τοίχο της στροφής."));
                SetDistance(turnOut, SwimBarDistance.TurnBreakout, end, manual != null ? manual.UnderwaterDist : null,
                    manual != null && manual.UnderwaterEstimated && manual.UnderwaterDist.HasValue, SeedTurnBreakout);
                list.Add(turnOut);
            }

            // The finish: the last stroke to the wall, the touch.
            string finish = SwimLang.T("ΤΕΡΜΑΤΙΣΜΟΣ");
            double? lastStroke = c.FinalTime.HasValue && c.FinishLastStrokeGap.HasValue ? c.FinalTime.Value - c.FinishLastStrokeGap.Value : (double?)null;
            SwimBarEvent last = Event("last", finish, 3, SwimLang.T("Τελ. χεριά → τοίχος"), lastStroke,
                null, SwimLang.T("Η τελευταία χεριά πριν τον τερματισμό · η απόσταση της από τον τοίχο."));
            SetDistance(last, SwimBarDistance.FinishLastStroke, 0, race.FinishLastStrokeDist, false, SeedLastStroke);
            if (c.FinishLastStrokeGap.HasValue)
                last.Tip += string.Format(CultureInfo.InvariantCulture, SwimLang.T("\nΧρόνος ως τον τοίχο: {0} s."), Seconds(c.FinishLastStrokeGap.Value));
            list.Add(last);

            SwimRow finishRow;
            if (rowAt.TryGetValue(distance, out finishRow))
                list.Add(Checkpoint(finishRow, finish, 3, SwimLang.T("Τερματισμός ") + distance.ToString(CultureInfo.InvariantCulture) + unit, unit, next));

            return list;
        }

        /// <summary>
        /// The event of the moment: the last one passed at the race time 'now' (null: before the start) — the list follows it. The events
        /// without a time do not count; one with a time before an earlier one in the list (a mark out of order) neither.
        /// </summary>
        public static int CurrentIndex(List<SwimBarEvent> events, double? now)
        {
            if (events == null || !now.HasValue)
                return -1;

            int current = -1;
            double lastTime = double.NegativeInfinity;
            for (int i = 0; i < events.Count; i++)
            {
                double? t = events[i].VideoTime;
                if (!t.HasValue || t.Value < lastTime - 1e-9)
                    continue;
                if (t.Value > now.Value + 0.005)
                    break;
                lastTime = t.Value;
                current = i;
            }

            return current;
        }

        /// <summary>The next checkpoint to mark (the first one not marked), -1 when all are.</summary>
        public static int NextIndex(List<SwimBarEvent> events)
        {
            if (events == null)
                return -1;
            return events.FindIndex(e => e.Checkpoint > 0 && !e.Marked);
        }

        private static SwimBarEvent Event(string id, string title, int phase, string label, double? time, string value, string tip)
        {
            return new SwimBarEvent { Id = id, Group = title, Phase = phase, Label = label, Time = time, Value = value, Tip = tip };
        }

        private static SwimBarEvent Checkpoint(SwimRow row, string title, int phase, string label, string unit, int next)
        {
            SwimBarEvent e = new SwimBarEvent
            {
                Id = "cp" + row.D.ToString(CultureInfo.InvariantCulture),
                Group = title,
                Phase = phase,
                Label = label,
                Time = row.T,
                Checkpoint = row.D,
                Marked = row.Marked,
                Official = row.Official
            };

            // A checkpoint with an official time: the mark on the video where it is apart from it.
            if (row.Official && row.MarkApart && row.MarkT.HasValue)
                e.SeekTime = row.MarkT;

            if (row.Strokes.HasValue)
            {
                bool ok = row.StrokesTyped || row.StrokesConfirmed;
                e.Value = SwimLang.Decimals(row.Strokes.Value.ToString("0.#", CultureInfo.InvariantCulture)) + SwimLang.T(" χερ.") + (ok ? " ✓" : "");
                e.ValueOk = ok;
                e.StrokesValue = true;
            }
            else if (row.Vel.HasValue)
            {
                e.Value = row.Vel.Value.ToString("0.00", CultureInfo.InvariantCulture) + " m/s";
            }

            e.StrokesEditable = row.Marked;
            string tip = SwimLang.T("Κλικ: το βίντεο στο πέρασμα των ") + row.D.ToString(CultureInfo.InvariantCulture) + unit +
                SwimLang.T(" (με «Πέρασμα» στο σωστό καρέ μετακινείται εκεί).");
            if (row.Strokes.HasValue)
                tip += SwimLang.T("\nΚλικ στις χεριές: διόρθωση των χεριών του τμήματος.");
            if (row.Vel.HasValue && row.Strokes.HasValue)
                tip += "\n" + row.Vel.Value.ToString("0.00", CultureInfo.InvariantCulture) + " m/s";
            if (row.Official)
                tip += SwimLang.T("\nΟ χρόνος είναι ο επίσημος των αποτελεσμάτων.");
            if (!row.Marked)
                tip = row.D == next ? SwimLang.T("Το επόμενο πέρασμα που θα μαρκάρεις (S).") : SwimLang.T("Δεν έχει μαρκαριστεί ακόμα.");
            e.Tip = tip;
            return e;
        }

        private static void SetDistance(SwimBarEvent e, SwimBarDistance which, int wall, double? value, bool estimated, double seed)
        {
            e.Distance = which;
            e.Wall = wall;
            e.DistanceValue = value;
            e.DistanceEstimated = estimated;
            e.Seed = seed;
        }

        /// <summary>The time of a wall (or of the finish) of the race, null when it is not marked.</summary>
        private static double? WallTime(Dictionary<int, SwimRow> rowAt, int d)
        {
            SwimRow r;
            return rowAt.TryGetValue(d, out r) && r.T.HasValue ? r.T : null;
        }

        private static double? First(List<double> times, Func<double, bool> where)
        {
            foreach (double t in times.OrderBy(x => x))
            {
                if (where(t))
                    return t;
            }

            return null;
        }

        private static string Count(int? n)
        {
            return n.HasValue && n.Value > 0 ? "× " + n.Value.ToString(CultureInfo.InvariantCulture) : null;
        }

        private static string Seconds(double s)
        {
            return (s < 0 ? "−" : "") + Math.Abs(s).ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
