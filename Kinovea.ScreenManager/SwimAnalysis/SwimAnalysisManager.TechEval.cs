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

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.13.0) The technique evaluation: each start or turn scored with a sheet of criteria of the catalogue ((2.16.0) one single list
    /// for the start and one for the turn of each style). The program measures what it can (times of the marks, angles of the skeleton)
    /// and fills those criteria (🤖, until the coach confirms them); the coach scores the rest at their frames. An error keeps its frame
    /// for the report.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>The catalogue of the sheets (read again when its file changes).</summary>
        public SwimTechSheetCatalog TechCatalog
        {
            get { return SwimTechSheetCatalog.Current; }
        }

        private string ChoiceKey(SwimTechPhase phase)
        {
            return (phase.Kind == SwimPhaseKind.Start ? "start|" : "turn|") + TechStyle(phase);
        }

        /// <summary>
        /// (2.16.0) The style of the lists of a phase: the race's. In a medley with lists for its changes of stroke, a turn where the stroke
        /// changes is that change ("im-fly-bk", "im-bk-br", "im-br-fs") and a turn inside a stroke is of that stroke; its start is of the
        /// stroke of its first length (butterfly, backstroke in a medley relay).
        /// </summary>
        public string TechStyle(SwimTechPhase phase)
        {
            string style = Race != null ? Race.Style ?? "" : "";
            if (style == "im" && phase != null && phase.Kind == SwimPhaseKind.Start && Race.Pool > 0 && TechCatalog.HasTransitions)
            {
                string first = SwimPresets.StyleAt(Race, Math.Min(Race.Distance, Race.Pool));
                return string.IsNullOrEmpty(first) ? style : first;
            }

            if (style != "im" || phase == null || phase.Kind != SwimPhaseKind.Turn || phase.Wall <= 0 || Race.Pool <= 0 || !TechCatalog.HasTransitions)
                return style;

            string before = SwimPresets.StyleAt(Race, phase.Wall);
            string after = SwimPresets.StyleAt(Race, Math.Min(Race.Distance, phase.Wall + Race.Pool));
            if (string.IsNullOrEmpty(before) || string.IsNullOrEmpty(after))
                return style;
            if (before == after || Race.Relay)
                return after;

            string change = "im-" + before + "-" + after;
            return TechCatalog.SheetsFor(SwimPhaseKind.Turn, change).Count > 0 ? change : after;
        }

        private static bool Offered(SwimTechSheet sheet)
        {
            return sheet != null && !sheet.Hidden;
        }

        /// <summary>
        /// The sheet shown for a phase: the coach's last choice when the phase was evaluated with it, else the sheet the coach evaluated it
        /// with last (not the program's automatic list), else his last choice, else the KOE checklist. (2.16.0) Never an older sheet of a
        /// catalogue with single lists, unless no list fits the phase (its evaluations move to the single list).
        /// </summary>
        public SwimTechSheet TechSheetFor(SwimTechPhase phase)
        {
            SwimTechSheetCatalog cat = TechCatalog;
            if (phase == null || Race == null || cat.Sheets.Count == 0)
                return null;

            string choice;
            Settings.TechSheetChoice.TryGetValue(ChoiceKey(phase), out choice);
            if (choice != null && Race.TechEvals.Any(e => e.PhaseId == phase.Id && e.SheetId == choice) && Offered(cat.Sheet(choice)))
                return cat.Sheet(choice);

            SwimTechEval last = Race.TechEvals.LastOrDefault(e => e.PhaseId == phase.Id && Offered(cat.Sheet(e.SheetId)) && !SwimTechSheetCatalog.IsAuto(e.SheetId));
            if (last != null)
                return cat.Sheet(last.SheetId);

            SwimTechSheet def = cat.DefaultFor(phase.Kind, TechStyle(phase), phase.Flip, choice);
            if (def != null)
                return def;

            last = Race.TechEvals.LastOrDefault(e => e.PhaseId == phase.Id && cat.Sheet(e.SheetId) != null);
            return last != null ? cat.Sheet(last.SheetId) : null;
        }

        public void ChooseTechSheet(SwimTechPhase phase, SwimTechSheet sheet)
        {
            if (phase == null || sheet == null || Race == null)
                return;

            Settings.TechSheetChoice[ChoiceKey(phase)] = sheet.Id;
            Settings.Save();
        }

        /// <summary>(2.16.0) The evaluations with the older lists of every phase in the single lists (before each send). Returns how many scores moved.</summary>
        private int MergeOldTechEvals()
        {
            if (Race == null || TechPhases == null || Race.TechEvals.Count == 0 || !TechCatalog.SingleLists)
                return 0;

            int moved = 0;
            try
            {
                foreach (SwimTechPhase p in TechPhases)
                    moved += MergeOldEvals(p);
            }
            catch (Exception e)
            {
                log.Error("Could not move the evaluations of the older lists.", e);
            }

            return moved;
        }

        /// <summary>(2.16.0) The evaluations of a phase with the older lists in its single list (when the page shows it).</summary>
        public int MergeOldTechEvals(SwimTechPhase phase)
        {
            int moved = 0;
            try
            {
                moved = MergeOldEvals(phase);
            }
            catch (Exception e)
            {
                log.Error("Could not move the evaluations of the older lists.", e);
            }

            if (moved > 0)
            {
                Changed(false);
                ScheduleSend();
            }

            return moved;
        }

        /// <summary>
        /// (2.16.0) The evaluations of a phase made with the older lists move to its single list (SwimTechSheetCatalog.MergeOld); a frame of
        /// a score that did not move (the point had the coach's own score) goes when no other score keeps it. (2.16.1) The scores of the
        /// single list under the old key of a point whose words changed move to the point (SwimTechSheetCatalog.MoveRenamed). Returns how
        /// many moved.
        /// </summary>
        private int MergeOldEvals(SwimTechPhase phase)
        {
            SwimTechSheetCatalog cat = TechCatalog;
            if (Race == null || phase == null || Race.TechEvals.Count == 0 || !cat.SingleLists)
                return 0;
            bool older = Race.TechEvals.Any(e => e.PhaseId == phase.Id && cat.Sheet(e.SheetId) != null && cat.Sheet(e.SheetId).Hidden);

            SwimTechSheet single = cat.DefaultFor(phase.Kind, TechStyle(phase), phase.Flip, null);
            if (!Offered(single))
                return 0;

            SwimTechEval own = Race.TechEvals.FirstOrDefault(e => e.PhaseId == phase.Id && e.SheetId == single.Id);
            bool renamed = own != null && own.Scores.Keys.Any(k => !single.Items.Any(i => i.Key == k) && single.Items.Any(i => i.StandsFor(k)));
            if (!older && !renamed)
                return 0;

            List<SwimTechScore> dropped = new List<SwimTechScore>();
            int moved = SwimTechSheetCatalog.MoveRenamed(own, single, dropped);
            if (older)
                moved += cat.MergeOld(Race.TechEvals, phase.Id, single, dropped, DateTime.Now.ToString("d/M/yyyy HH:mm", CultureInfo.InvariantCulture));
            foreach (SwimTechScore s in dropped)
            {
                if (!SnapshotOfScores(s.SnapshotId))
                    RemoveEvalSnapshot(s);
            }

            return moved;
        }

        /// <summary>A frame some score of an evaluation of the analysis keeps.</summary>
        private bool SnapshotOfScores(string snapshotId)
        {
            return !string.IsNullOrEmpty(snapshotId) && Race != null && Race.TechEvals.Any(e => e.Scores.Values.Any(s => s.SnapshotId == snapshotId));
        }

        /// <summary>The evaluation of a phase with a sheet (null when there is none and create is false).</summary>
        public SwimTechEval TechEval(SwimTechPhase phase, SwimTechSheet sheet, bool create)
        {
            if (Race == null || phase == null || sheet == null)
                return null;

            SwimTechEval e = Race.TechEvals.FirstOrDefault(x => x.PhaseId == phase.Id && x.SheetId == sheet.Id);
            if (e == null && create)
            {
                e = new SwimTechEval { PhaseId = phase.Id, SheetId = sheet.Id };
                Race.TechEvals.Add(e);
            }

            return e;
        }

        /// <summary>What the program measures in this phase now, by check.</summary>
        public Dictionary<string, SwimTechMeasure> TechMeasures(SwimTechPhase phase)
        {
            return SwimTechEvaluator.Measure(Race, phase, Timing, TechCatalog);
        }

        /// <summary>
        /// Fills the criteria the program measured and the coach has not scored (🤖, Auto) and updates the ones it filled before. A
        /// criterion the coach scored stays as he scored it. Returns how many changed.
        /// </summary>
        public int ApplyTechProposals(SwimTechPhase phase, SwimTechSheet sheet, Dictionary<string, SwimTechMeasure> measures)
        {
            int changed = ApplyProposals(phase, sheet, measures);
            if (changed > 0)
            {
                Changed(false);
                ScheduleSend();
            }

            return changed;
        }

        /// <summary>
        /// The program's automatic error list (🤖) of each start and turn of a training analysis kept up to date before each send, so that
        /// the report has it without the page being opened (the coach confirms or corrects it there; that is how the program learns).
        /// (2.16.0) With the single lists: the list of the phase, whose points include the errors. Returns how many scores changed.
        /// </summary>
        private int UpdateAutoEvals()
        {
            if (Race == null || !Race.IsTraining || Settings == null || !Settings.TechEvalAuto || TechPhases == null || TechPhases.Count == 0)
                return 0;

            int changed = 0;
            try
            {
                SwimTechSheetCatalog cat = TechCatalog;
                foreach (SwimTechPhase p in TechPhases)
                {
                    SwimTechSheet auto = cat.SingleLists ? TechSheetFor(p) : cat.AutoFor(p.Kind, Race.Style, p.Flip);
                    if (Offered(auto))
                        changed += ApplyProposals(p, auto, TechMeasures(p));
                }
            }
            catch (Exception e)
            {
                log.Error("Could not update the automatic error lists.", e);
            }

            return changed;
        }

        /// <summary>How often the program agrees with the coach's answers on a criterion (null without answers on this computer).</summary>
        public SwimTechAgreement TechAgreement(SwimTechCriterion criterion)
        {
            SwimTechLearning learning = SwimTechLearning.Current;
            SwimTechCheck check = criterion != null ? TechCatalog.CheckOf(criterion) : null;
            if (learning == null || check == null)
                return null;
            return learning.AgreementOf(criterion.Key, check);
        }

        /// <summary>(2.16.0) The same for a point of a sheet: with the answers on the older points it stands for.</summary>
        public SwimTechAgreement TechAgreement(SwimTechSheetItem item)
        {
            SwimTechLearning learning = SwimTechLearning.Current;
            SwimTechCheck check = item != null ? TechCatalog.CheckOf(item.Criterion) : null;
            if (learning == null || check == null)
                return null;
            return learning.AgreementOf(new[] { item.Key }.Concat(item.Same), check);
        }

        private int ApplyProposals(SwimTechPhase phase, SwimTechSheet sheet, Dictionary<string, SwimTechMeasure> measures)
        {
            if (Settings == null || !Settings.TechEvalAuto || phase == null || sheet == null || measures == null)
                return 0;

            int changed = 0;
            SwimTechEval eval = TechEval(phase, sheet, false);
            foreach (SwimTechSheetItem it in sheet.Items)
            {
                SwimTechMeasure m;
                if (it.Criterion.Check == null || !measures.TryGetValue(it.Criterion.Check, out m))
                    continue;

                int? proposal = SwimTechEvaluator.Proposal(it.Criterion, m, !sheet.Scored, TechAgreement(it));
                SwimTechScore score = null;
                if (eval != null)
                    eval.Scores.TryGetValue(it.Key, out score);
                if (score != null && !score.Auto)
                    continue;

                if (!proposal.HasValue)
                {
                    // The measurement no longer says: an automatic score goes.
                    if (score != null && eval != null)
                    {
                        eval.Scores.Remove(it.Key);
                        changed++;
                    }

                    continue;
                }

                if (score != null && score.Value == proposal.Value && score.Measured.HasValue && Math.Abs(score.Measured.Value - m.Value) < 1e-6)
                    continue;

                if (eval == null)
                    eval = TechEval(phase, sheet, true);
                eval.Scores[it.Key] = new SwimTechScore { Value = proposal.Value, Auto = true, Measured = Math.Round(m.Value, 3), Unit = CheckUnit(it.Criterion) };
                changed++;
            }

            if (changed > 0)
                eval.Date = DateTime.Now.ToString("d/M/yyyy HH:mm", CultureInfo.InvariantCulture);

            return changed;
        }

        private string CheckUnit(SwimTechCriterion c)
        {
            SwimTechCheck check = TechCatalog.CheckOf(c);
            return check != null && !string.IsNullOrEmpty(check.Unit) ? check.Unit : null;
        }

        /// <summary>
        /// The coach scores a criterion: 1 ✓, 0 ✗, -1 not visible, null clears it. An error (✗, or a present error of a list) keeps
        /// the frame shown when the video is in this phase: it goes to the report with the criterion and what to correct.
        /// </summary>
        public void ScoreTechCriterion(SwimTechPhase phase, SwimTechSheet sheet, SwimTechSheetItem item, int? value, SwimTechMeasure measure)
        {
            if (Race == null || phase == null || sheet == null || item == null)
                return;

            SwimTechEval eval = TechEval(phase, sheet, value.HasValue);
            if (eval == null)
                return;

            SwimTechScore old;
            eval.Scores.TryGetValue(item.Key, out old);
            Learn(phase, sheet, item, value, measure != null ? (double?)measure.Value : null);
            if (!value.HasValue)
            {
                eval.Scores.Remove(item.Key);
                RemoveEvalSnapshot(old);
            }
            else
            {
                SwimTechScore score = new SwimTechScore { Value = value.Value, Comment = old != null ? old.Comment : null, SnapshotId = old != null ? old.SnapshotId : null };
                if (measure != null)
                {
                    score.Measured = Math.Round(measure.Value, 3);
                    score.Unit = CheckUnit(item.Criterion);
                }

                bool error = sheet.Scored ? value.Value == 0 : value.Value == 1;
                if (error)
                {
                    if (string.IsNullOrEmpty(score.SnapshotId) || !Race.Snapshots.Any(s => s.Id == score.SnapshotId))
                        score.SnapshotId = TakeEvalSnapshot(phase, item, ErrorMoment(phase, item, measure));
                }
                else
                {
                    RemoveEvalSnapshot(score);
                    score.SnapshotId = null;
                }

                eval.Scores[item.Key] = score;
            }

            eval.Date = DateTime.Now.ToString("d/M/yyyy HH:mm", CultureInfo.InvariantCulture);
            Changed(false);
            ScheduleSend();
        }

        /// <summary>
        /// The coach confirms the scores the program filled in this evaluation (they stay as they are, without 🤖): they count as his
        /// answers for the learning, and each error gets its frame (the moment of its measurement) for the report.
        /// </summary>
        public void ConfirmTechProposals(SwimTechPhase phase, SwimTechSheet sheet)
        {
            SwimTechEval eval = TechEval(phase, sheet, false);
            if (eval == null)
                return;

            Dictionary<string, SwimTechMeasure> measures = null;
            long? was = HasVideo ? (long?)player.CurrentTimestamp : null;
            bool moved = false;
            int n = 0;
            foreach (SwimTechSheetItem it in sheet.Items)
            {
                SwimTechScore s;
                if (!eval.Scores.TryGetValue(it.Key, out s) || !s.Auto)
                    continue;

                s.Auto = false;
                n++;
                Learn(phase, sheet, it, s.Value, s.Measured);

                bool error = sheet.Scored ? s.Value == 0 : s.Value == 1;
                if (!error || (!string.IsNullOrEmpty(s.SnapshotId) && Race.Snapshots.Any(x => x.Id == s.SnapshotId)) || !HasVideo)
                    continue;

                if (measures == null)
                    measures = TechMeasures(phase);
                SwimTechMeasure m = null;
                if (it.Criterion.Check != null)
                    measures.TryGetValue(it.Criterion.Check, out m);
                long? at = ErrorMoment(phase, it, m);
                if (at.HasValue && phase.Contains(at.Value))
                {
                    SeekNearest(at.Value);
                    moved = true;
                    s.SnapshotId = TakeEvalSnapshot(phase, it, null);
                }
            }

            if (moved && was.HasValue)
                SeekTo(was.Value);

            if (n == 0)
                return;
            eval.Date = DateTime.Now.ToString("d/M/yyyy HH:mm", CultureInfo.InvariantCulture);
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("✓ Επιβεβαιώθηκαν {0} αυτόματες βαθμολογίες."), n), false);
            Changed(false);
            ScheduleSend();
        }

        public void SetTechEvalNotes(SwimTechPhase phase, SwimTechSheet sheet, string notes)
        {
            SwimTechEval eval = TechEval(phase, sheet, !string.IsNullOrEmpty(notes));
            if (eval == null)
                return;
            eval.Notes = notes ?? "";
            Changed(false);
            ScheduleSend();
        }

        /// <summary>Deletes the evaluation of a phase with a sheet (its frames of errors too).</summary>
        public void DeleteTechEval(SwimTechPhase phase, SwimTechSheet sheet)
        {
            SwimTechEval eval = TechEval(phase, sheet, false);
            if (eval == null)
                return;
            foreach (SwimTechScore s in eval.Scores.Values)
                RemoveEvalSnapshot(s);
            Race.TechEvals.Remove(eval);
            SwimTechLearning learning = SwimTechLearning.Current;
            if (learning != null)
                learning.Remove(LearnKey(phase, sheet, null), true);
            Changed(true);
        }

        private string LearnKey(SwimTechPhase phase, SwimTechSheet sheet, SwimTechSheetItem item)
        {
            return Race.Id + "|" + phase.Id + "|" + sheet.Id + "|" + (item != null ? item.Key : "");
        }

        /// <summary>
        /// The coach's answer on a criterion the program measured goes to the learning (SwimTechLearning, on this computer): 1 or 0 with
        /// the measurement; "not visible" or cleared takes the answer away.
        /// </summary>
        private void Learn(SwimTechPhase phase, SwimTechSheet sheet, SwimTechSheetItem item, int? value, double? measured)
        {
            try
            {
                SwimTechLearning learning = SwimTechLearning.Current;
                if (learning == null || Race == null || item == null || item.Criterion.Check == null)
                    return;

                string key = LearnKey(phase, sheet, item);
                if (!value.HasValue || value.Value < 0 || !measured.HasValue)
                {
                    learning.Remove(key, false);
                    return;
                }

                learning.Put(new SwimTechSample
                {
                    Key = key,
                    Check = item.Criterion.Check,
                    Criterion = item.Criterion.Key,
                    Sheet = sheet.Id,
                    Style = Race.Style ?? "",
                    Measured = Math.Round(measured.Value, 4),
                    Good = sheet.Scored ? value.Value == 1 : value.Value == 0,
                    Date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                });
            }
            catch (Exception e)
            {
                log.Error("Could not keep the answer for the learning.", e);
            }
        }

        /// <summary>The moment of an error: where the program measured it (inside the phase), else the moment of the criterion.</summary>
        private long? ErrorMoment(SwimTechPhase phase, SwimTechSheetItem item, SwimTechMeasure measure)
        {
            if (measure != null && measure.Timestamp.HasValue && phase.Contains(measure.Timestamp.Value))
                return measure.Timestamp;
            return SwimTechEvaluator.AnchorTimestamp(phase, item.Criterion.Anchor, Timing);
        }

        /// <summary>
        /// The frame of an error: the frame shown when the video is in this phase (the coach looks at it when he scores), else the moment
        /// of the error (the video goes there). The snapshot carries the criterion and what to correct; it is the error's image in the report.
        /// </summary>
        private string TakeEvalSnapshot(SwimTechPhase phase, SwimTechSheetItem item, long? moment)
        {
            if (!HasVideo)
                return null;
            if (!phase.Contains(player.CurrentTimestamp) && moment.HasValue && phase.Contains(moment.Value))
                SeekNearest(moment.Value);
            if (!phase.Contains(player.CurrentTimestamp))
                return null;

            long ts = player.CurrentTimestamp;
            SwimSnapshot same = Race.Snapshots.FirstOrDefault(x => SameFrame(x.Timestamp, ts));
            string label = item.Criterion.Label.El;
            string cue = item.Criterion.Cue != null ? item.Criterion.Cue.El : "";
            string comment = "✗ " + label + (cue.Length > 0 ? " → " + cue : "");
            if (same != null)
            {
                if (string.IsNullOrEmpty(same.Comment))
                    same.Comment = comment;
                return same.Id;
            }

            Bitmap image = CaptureFrame(ts, Settings.OverlayInExports, false);
            if (image == null)
                return null;

            SwimSnapshot snapshot;
            try
            {
                snapshot = AddSnapshot(image, ts, "eval:" + item.Key, "");
            }
            finally
            {
                image.Dispose();
            }

            if (snapshot == null)
                return null;

            snapshot.Comment = comment;
            UploadSnapshot(snapshot);
            if (form != null)
                form.SnapshotAdded(snapshot);
            return snapshot.Id;
        }

        /// <summary>The snapshot of an error the evaluation took (not one of the analyst) goes with the error.</summary>
        private void RemoveEvalSnapshot(SwimTechScore score)
        {
            if (score == null || string.IsNullOrEmpty(score.SnapshotId) || Race == null)
                return;

            SwimSnapshot snap = Race.Snapshots.FirstOrDefault(s => s.Id == score.SnapshotId);
            if (snap != null && snap.Event != null && snap.Event.StartsWith("eval:", StringComparison.Ordinal))
                Race.Snapshots.Remove(snap);
        }

        /// <summary>Go to the frame of a criterion in a phase (its moment, or the start of the phase).</summary>
        public void SeekToCriterion(SwimTechPhase phase, SwimTechSheetItem item)
        {
            if (phase == null || item == null)
                return;

            long? ts = SwimTechEvaluator.AnchorTimestamp(phase, item.Criterion.Anchor, Timing);
            SeekNearest(ts ?? phase.Origin);
        }
    }
}
