#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Media;
using System.Windows.Forms;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Automatic proposals of the technical analysis: after the skeleton of a start or a turn is read, the detectors
    /// (SwimEventDetector) propose the marks (first movement, hands, back foot, feet, entry, dolphin kicks, breakout, contact,
    /// push off). The analyst checks them one by one on the video: Enter keeps the proposal at the frame shown (moved with the
    /// arrows if it was not right), Delete says there is no such event, Esc stops the check. A mark typed with its usual key
    /// near a proposal answers it too. Each answer goes to the learning of this computer (SwimLearning): the next proposals
    /// move by the usual correction of the analyst, and in the same video (same camera) at once.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Proposals
        private SwimLearning learning;
        private SwimRegionFrames lastRegion;
        private string lastRegionRace;
        private string reviewId;
        private int reviewAnswered;
        private int reviewCorrected;
        private int reviewWrong;
        private bool resolvingProposal;

        /// <summary>The distance of the technical split proposal being answered (−5, +5): T marks that one, not the next of the phase.</summary>
        private int? answeringSplit;
        private SwimPhaseKind answeringSplitKind = SwimPhaseKind.Turn;

        /// <summary>The learning of this computer (read once).</summary>
        public SwimLearning Learning
        {
            get
            {
                // A reading in the background reads with its own copy (the answers of the analyst change this one meanwhile).
                QuietReading q = quietThread;
                if (q != null)
                    return q.Learning;

                if (learning == null)
                    learning = SwimLearning.Load();
                return learning;
            }
        }

        /// <summary>Proposals of the race not answered yet, in the order of the video.</summary>
        public List<SwimProposal> PendingProposals
        {
            get { return Race == null ? new List<SwimProposal>() : Race.Proposals.OrderBy(p => p.Timestamp).ToList(); }
        }

        /// <summary>The proposals are being checked one by one (Enter, Delete, Esc on the video).</summary>
        public bool Reviewing
        {
            get { return reviewId != null && CurrentProposal != null; }
        }

        /// <summary>The proposal being checked, null if none.</summary>
        public SwimProposal CurrentProposal
        {
            get { return reviewId == null || Race == null ? null : Race.Proposals.FirstOrDefault(p => p.Id == reviewId); }
        }

        /// <summary>For the panel: proposals waiting and what was learned ("3 προτάσεις · μάθηση από 12 απαντήσεις").</summary>
        public string ProposalsDescription
        {
            get
            {
                int pending = Race == null ? 0 : Race.Proposals.Count;
                int answers = Learning.Records.Count;
                string p = pending == 0 ? SwimLang.T("καμία πρόταση σε αναμονή") : pending == 1 ? SwimLang.T("1 πρόταση σε αναμονή") : pending + SwimLang.T(" προτάσεις σε αναμονή");
                string l = answers == 0 ? SwimLang.T("δεν έχει μάθει ακόμα") : SwimLang.T("έμαθε από ") + answers + (answers == 1 ? SwimLang.T(" απάντηση") : SwimLang.T(" απαντήσεις"));
                int shared = quietThread == null ? SharedAnswers : 0;
                if (shared > 0)
                    l += string.Format(CultureInfo.InvariantCulture, SwimLang.T(" ({0} από τα άλλα Kinovea)"), shared);
                return p + " · " + l;
            }
        }

        public void SetProposeAfterSkeleton(bool on)
        {
            Settings.ProposeAfterSkeleton = on;
            Settings.Save();
        }

        /// <summary>Forget what was learned (button of the panel, after a confirmation).</summary>
        public void ResetLearning()
        {
            Learning.Reset();
            Learning.Save();
            LearningChanged();
            Status(SwimLang.T("Η μάθηση των προτάσεων ξεκινά από την αρχή."), false);
            RefreshProposalsUi();
        }

        /// <summary>Button of the panel: proposals from the skeleton at the current frame (or the last one).</summary>
        public void ProposeHere()
        {
            if (Race == null || !HasVideo)
            {
                Status(SwimLang.T("Άνοιξε πρώτα ένα βίντεο σε οθόνη αναπαραγωγής."), true);
                return;
            }

            long? now = CurrentVideoTimestamp;
            SwimSkeleton skeleton = now.HasValue ? Race.SkeletonAt(now.Value) : null;
            if (skeleton == null)
                skeleton = Race.Skeletons.OrderBy(s => s.Start).LastOrDefault();
            if (skeleton == null)
            {
                Status(SwimLang.T("Για τις προτάσεις χρειάζεται ο σκελετός: «🦴 Σκελετός» και κλικ στον αθλητή πριν την κίνηση (π.χ. στη θέση ετοιμότητας)."), true);
                return;
            }

            // The image of the start kept by the reading of this swimmer, if it begins in this part (it goes on a little after the
            // swimmer is lost).
            SwimRegionFrames region = lastRegion != null && lastRegion.Count > 0 && lastRegionRace == Race.Id &&
                lastRegion.Timestamps[0] >= skeleton.Start - (long)skeleton.Step && lastRegion.Timestamps[0] <= skeleton.End ? lastRegion : null;
            Propose(skeleton, region);
        }

        /// <summary>The detectors on a skeleton (and the image of the start when it was kept), the proposals, then the check.</summary>
        private void Propose(SwimSkeleton skeleton, SwimRegionFrames region)
        {
            RefreshTiming();
            if (skeleton == null || skeleton.Count < 8 || !Timing.IsValid)
                return;

            double secondsPerSample = Timing.Seconds(skeleton.Start + (long)Math.Round(skeleton.Step), skeleton.Start);
            if (secondsPerSample <= 0)
                return;

            double sps = 1.0 / secondsPerSample;
            if (region != null)
                region.Align(skeleton.Start, skeleton.Step);

            string video = SwimLearning.VideoCode(videoPath);
            SwimLearning l = Learning;
            SwimDetectorContext context = new SwimDetectorContext { Style = Race.Style };
            context.Measure = delegate (string kind, string name, double fallback, out int sameVideo)
            {
                int count;
                bool same;
                double value = l.Measure(kind, name, video, fallback, out count, out same);
                sameVideo = same ? count : 0;
                return value;
            };

            List<SwimDetection> found;
            try
            {
                found = SwimEventDetector.Detect(SwimPoseSeries.FromData(skeleton.Data, sps), region, context);
            }
            catch (Exception e)
            {
                log.Error("Automatic proposals failed.", e);
                Status(SwimLang.T("Οι αυτόματες προτάσεις απέτυχαν: ") + e.Message, true);
                return;
            }

            // The proposals of an older reading of the same part go (not the ones the auto pilot put: their marks wait for the check).
            Race.Proposals.RemoveAll(p => p.Timestamp >= skeleton.Start && p.Timestamp <= skeleton.End && !IsPilotPut(p) &&
                !(p.Pilot && p.Kind == KindRaceStart && Race.PilotStart));
            string batch = Guid.NewGuid().ToString("N");
            int added = 0;
            foreach (SwimDetection d in found)
            {
                // Too unclear for the detector itself: not proposed. What the analyst deleted before only lowers the confidence
                // shown (the kind can come back with the next answers).
                double confidence;
                double sample = SwimProposalMath.Combine(d, l, video, sps, out confidence);
                if (double.IsNaN(sample) || d.Confidence < 0.25)
                    continue;

                long ts = skeleton.Start + (long)Math.Round(sample * skeleton.Step);
                if (ts < skeleton.Start || ts > skeleton.End)
                    continue;

                // Already marked by the analyst near there: nothing to propose. A breakout the auto pilot put near there: its own
                // proposal waits for the check (one answer for one breakout).
                if (HasMarkNear(d.Type, ts, d.Type == SwimMarkType.Kick ? 0.12 : 0.3) || (d.Type == SwimMarkType.Breakout && PilotBreakoutNear(ts)))
                    continue;

                SwimProposal p = new SwimProposal
                {
                    Type = d.Type,
                    Timestamp = ts,
                    Kind = d.Kind,
                    Confidence = Math.Round(confidence, 2),
                    Index = d.Index,
                    Batch = batch
                };

                foreach (KeyValuePair<string, double> c in d.Cues)
                    p.Cues[c.Key] = skeleton.Start + (long)Math.Round(c.Value * skeleton.Step);
                if (d.Measures.Count > 0)
                    p.Measures = new Dictionary<string, double>(d.Measures);
                Race.Proposals.Add(p);
                added++;
            }

            Changed(false);
            if (added == 0)
            {
                Status(SwimLang.T("Αυτόματες προτάσεις: δεν βρέθηκε γεγονός σε αυτό το κομμάτι (εκκίνηση από τη θέση ετοιμότητας, δελφινισμοί, στροφή)."), false);
                RefreshProposalsUi();
                return;
            }

            BeginReview(Race.Proposals.Where(p => p.Batch == batch).OrderBy(p => p.Timestamp).First());
        }

        private bool HasMarkNear(SwimMarkType type, long timestamp, double seconds)
        {
            Func<SwimMark, bool> near = m => Math.Abs(Timing.Seconds(m.Timestamp, timestamp)) <= seconds;
            if (type == SwimMarkType.WallContact && !Race.IsTraining)
                return Race.Marks.Any(m => m.Type == SwimMarkType.Split && near(m));
            return Race.Marks.Any(m => m.Type == type && near(m));
        }

        /// <summary>Check of the proposals from this one (or the first waiting): the video goes to it.</summary>
        public void BeginReview(SwimProposal from = null)
        {
            if (Race == null || !HasVideo)
                return;

            SwimProposal p = from ?? PendingProposals.FirstOrDefault();
            if (p == null)
            {
                Status(SwimLang.T("Δεν υπάρχουν προτάσεις σε αναμονή. «✨ Προτάσεις» μετά τον σκελετό."), false);
                EndReview(false);
                return;
            }

            if (reviewId == null)
            {
                reviewAnswered = 0;
                reviewCorrected = 0;
                reviewWrong = 0;
            }

            reviewId = p.Id;
            ShowProposal(p);
        }

        private void ShowProposal(SwimProposal p, string done = null)
        {
            SeekNearest(p.Timestamp);
            int index = PendingProposals.FindIndex(x => x.Id == p.Id) + 1;
            bool warning = done != null && done.StartsWith("⚠", StringComparison.Ordinal);
            bool pilot = PilotMarkOf(p) != null;
            Status((string.IsNullOrEmpty(done) ? "" : done + "  →  ") + string.Format(CultureInfo.InvariantCulture,
                SwimLang.T("✨ Πρόταση {0}/{1}: {2}{3}. Enter: σωστό · ←/→ και Enter: διόρθωση · Delete: λάθος{4} · Esc: τέλος."),
                index, Race.Proposals.Count, ProposalLabel(p), pilot ? SwimLang.T(" · ✈ την έβαλε ο πιλότος (η απάντησή σου διδάσκει)") : "",
                pilot ? SwimLang.T(" (σβήνεται)") : ""), warning);
            RefreshProposalsUi();
            InvalidatePlayer();
        }

        /// <summary>Stops the check (the proposals not answered wait).</summary>
        public void EndReview(bool report = true, string done = null)
        {
            bool was = reviewId != null;
            reviewId = null;
            reviewDoubtful = false;

            // The auto pilot waited in this check for the start (Esc, «Τέλος», the last candidate deleted): it stops, what it put stays.
            if (PilotActive && pilotStep == PilotStep.Start)
            {
                StopPilot();
                done = (string.IsNullOrEmpty(done) ? "" : done + "  →  ") + SwimLang.T("✈ Ο αυτόματος πιλότος σταμάτησε (βάλε την εκκίνηση και πάτα ξανά ✈)");
            }
            if (was && report)
            {
                int left = Race == null ? 0 : Race.Proposals.Count;
                bool warning = done != null && done.StartsWith("⚠", StringComparison.Ordinal);
                Status((string.IsNullOrEmpty(done) ? "" : done + "  →  ") + string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("Έλεγχος προτάσεων: {0} ({1}, {2}, {3} λάθος){4}."),
                    CountOf(reviewAnswered, SwimLang.T("απάντηση"), SwimLang.T("απαντήσεις")), CountOf(reviewAnswered - reviewCorrected - reviewWrong, SwimLang.T("σωστή"), SwimLang.T("σωστές")),
                    CountOf(reviewCorrected, SwimLang.T("διορθωμένη"), SwimLang.T("διορθωμένες")), reviewWrong,
                    left > 0 ? string.Format(CultureInfo.InvariantCulture, SwimLang.T(" · {0} σε αναμονή («Έλεγχος» για να συνεχίσεις)"), left) : ""), warning);
            }

            RefreshProposalsUi();
            InvalidatePlayer();
        }

        /// <summary>
        /// Keys of the video during the check: Enter keeps the proposal at the frame shown, Delete rejects it, Esc stops.
        /// Returns true when the key was used.
        /// </summary>
        public bool ReviewKey(PlayerScreenUserInterface screen, Keys key, long timestamp)
        {
            // Enter while the swimmer of the whole race is proposed: the reading starts with him; ↑/↓ propose the lane above / below.
            if (key == Keys.Enter && AcceptAutoPick(screen, true))
                return true;
            if ((key == Keys.Up || key == Keys.Down) && MoveAutoPick(screen, key == Keys.Up ? -1 : 1))
                return true;

            if (!Reviewing || !HasVideo || screen != player)
                return false;

            SwimProposal p = CurrentProposal;
            if (key == Keys.Enter)
            {
                AcceptProposal(p, timestamp);
                return true;
            }

            if (key == Keys.Delete)
            {
                RejectProposal(p);
                return true;
            }

            if (key == Keys.Escape)
            {
                EndReview();
                return true;
            }

            return false;
        }

        /// <summary>Panel and video bar: "Σωστό" for the proposal being checked at the current frame.</summary>
        public void AcceptCurrentProposal()
        {
            long? now = CurrentVideoTimestamp;
            if (Reviewing && now.HasValue)
                AcceptProposal(CurrentProposal, now.Value);
        }

        public void RejectCurrentProposal()
        {
            if (Reviewing)
                RejectProposal(CurrentProposal);
        }

        /// <summary>The event is at this frame: the mark is added with its usual command, the answer goes to the learning.</summary>
        private void AcceptProposal(SwimProposal p, long timestamp)
        {
            if (p == null || Race == null)
                return;

            // (2.9.5) The analyst's frame is his answer however far it is from the proposal (until 2.9.4: within 1 s; the frame proposed
            // can be many frames off). Only the order of the events is kept: an event of a length (a breakout, a push off, the events of
            // the start) is not in another length (InLengthOfEvent). A checkpoint out of order is kept with a warning, as with S.
            if (!InLengthOfEvent(p, timestamp))
            {
                SystemSounds.Beep.Play();
                Status(ProposalLabel(p) + SwimLang.T(": το καρέ είναι σε άλλο μήκος του αγώνα (το γεγονός είναι μετά τον τοίχο του μήκους του ή την εκκίνηση, και πριν από τον επόμενο τοίχο). Πήγαινε σε καρέ αυτού του μήκους, ή Delete αν δεν υπάρχει τέτοιο γεγονός."), true);
                return;
            }

            // A proposal the auto pilot put: at the frame of its mark the mark becomes the analyst's (his answer teaches); at another
            // frame the mark of his answer replaces it (a checkpoint: replaced as any split, Undo puts it back).
            SwimMark pilot = PilotMarkOf(p);
            if (pilot != null && SameFrame(pilot.Timestamp, timestamp))
            {
                pilot.Pilot = null;
                pilot = null;
            }

            // The wall of the breakout answered (0: the start; -1: not a breakout of the race): what Enter does after it.
            int breakoutWall = p.Kind == KindRaceBreakout ? p.Index : pilot != null && pilot.Type == SwimMarkType.Breakout && pilot.Pilot != null ? pilot.Pilot.Wall : -1;

            // The start signal of a race: the start of the race, not a mark.
            if (p.Kind == KindRaceStart)
            {
                AcceptStartProposal(p, timestamp);
                return;
            }

            // A checkpoint of a race: the split of its distance.
            if (p.Kind == KindRaceSplit)
            {
                AcceptSplitProposal(p, timestamp);
                return;
            }

            // A mark of this kind already on this frame (a kick: within 0.2 s, not counted twice): only the answer.
            SwimMarkType markType = p.Type == SwimMarkType.WallContact && !Race.IsTraining ? SwimMarkType.Split : p.Type;
            bool exists = Race.Marks.Any(m => m.Type == markType &&
                (SameFrame(m.Timestamp, timestamp) || (markType == SwimMarkType.Kick && Math.Abs(Timing.Seconds(m.Timestamp, timestamp)) <= 0.2)));
            PlayerScreenCommands command;
            string done;
            if (exists || !CommandOf(p, out command))
            {
                string again = "";
                if (pilot != null)
                {
                    ReplacePilotMark(pilot, Race.Marks.FirstOrDefault(m => m.Type == markType && m.Pilot == null && SameFrame(m.Timestamp, timestamp)));
                    if (breakoutWall >= 0)
                    {
                        TrimAutoStrokesBeforeBreakout(breakoutWall, timestamp);
                        again = EstimateUnderwaterDistance(breakoutWall, timestamp);
                    }
                }

                Answer(p, timestamp, true);
                Changed(pilot != null);
                done = "✓ " + ProposalLabel(p) + (exists ? SwimLang.T(": υπήρχε ήδη σημάδι σε αυτό το καρέ.") : ".") + again;
            }
            else
            {
                resolvingProposal = true;
                answeringSplit = p.Type == SwimMarkType.TechSplit && p.Index != 0 ? p.Index : (int?)null;
                answeringSplitKind = p.Kind == KindTechHeadStart ? SwimPhaseKind.Start : SwimPhaseKind.Turn;
                try
                {
                    HandleCommand(player, frameServer, command, timestamp);
                }
                finally
                {
                    resolvingProposal = false;
                    answeringSplit = null;
                }

                // The command refused the mark (for example a wall that already has its time): the proposal goes without an answer
                // (the learning does not change) and the reason stays visible with the next one.
                if (!Race.Marks.Any(m => m.Type == markType && SameFrame(m.Timestamp, timestamp)))
                {
                    string reason = "⚠ " + (string.IsNullOrEmpty(LastStatus) ? ProposalLabel(p) + SwimLang.T(": δεν μπήκε σημάδι.") : LastStatus);
                    Race.Proposals.Remove(p);
                    Changed(false);
                    NextProposal(timestamp, reason);
                    return;
                }

                // The pilot's mark of this event at another frame goes (what it changed with it too): this one replaces it.
                if (pilot != null)
                    ReplacePilotMark(pilot, Race.Marks.FirstOrDefault(m => m.Type == markType && m.Pilot == null && SameFrame(m.Timestamp, timestamp)));
                Answer(p, timestamp, true);
                string estimated = "";
                if (p.Kind == KindRaceBreakout || (pilot != null && breakoutWall >= 0))
                {
                    TrimAutoStrokesBeforeBreakout(breakoutWall, timestamp);
                    estimated = EstimateUnderwaterDistance(breakoutWall, timestamp);
                }

                Changed(estimated.Length > 0 || pilot != null);

                // What the command said stays visible with the next proposal.
                done = (LastStatusIsWarning ? "⚠ " : "✓ ") + (string.IsNullOrEmpty(LastStatus) ? ProposalLabel(p) : LastStatus) + estimated;
            }

            // The answer moves the waiting proposals of its kind (with the learning now) and the event after it (the feet off from
            // the first movement, the entry from the feet off).
            RecombineWaitingProposals(p.Kind);
            MoveEventProposals(p.Kind, p.Index, timestamp);
            NextProposal(timestamp, done);
        }

        private void RejectProposal(SwimProposal p)
        {
            if (p == null)
                return;

            long at = p.Timestamp;

            // The mark the auto pilot put for it goes (there is no such event there): its own mark, or the pilot's mark of the event
            // near this proposal of another reading (one far from it is not what was answered). The start it put stays: the right
            // one is put with «Εκκίνηση» (all the times follow it).
            SwimMark pilot = PilotMarkOf(p);
            if (pilot != null && (pilot.Id == p.PilotMark || Math.Abs(Timing.Seconds(pilot.Timestamp, p.Timestamp)) <= 1.0))
                RemovePilotMark(pilot);
            else
                pilot = null;
            bool pilotStart = p.Pilot && p.Kind == KindRaceStart;
            Answer(p, at, false);
            Changed(pilot != null);
            NextProposal(at, pilotStart ? SwimLang.T("⚠ Η εκκίνηση του πιλότου μένει: βάλε τη σωστή με το «Εκκίνηση» στο καρέ του σήματος (οι χρόνοι υπολογίζονται ξανά).") :
                ProposalLabel(p) + SwimLang.T(": δεν κρατήθηκε.") + (pilot != null ? SwimLang.T(" Το σημάδι του πιλότου σβήστηκε.") : ""));
        }

        private void NextProposal(long after, string done = null)
        {
            if (reviewId == null)
            {
                RefreshProposalsUi();
                return;
            }

            List<SwimProposal> pending = PendingProposals;
            SwimProposal next = null;
            if (reviewDoubtful)
            {
                // The doubtful proposals of the auto pilot first, then all of them in the order of the video.
                List<SwimProposal> doubtful = pending.Where(IsDoubtfulPilot).ToList();
                next = doubtful.FirstOrDefault(x => x.Timestamp > after) ?? doubtful.FirstOrDefault();
                if (next == null)
                    reviewDoubtful = false;
            }

            if (next == null)
                next = pending.FirstOrDefault(x => x.Timestamp > after) ?? pending.FirstOrDefault();
            if (next == null)
            {
                EndReview(true, done);
                return;
            }

            reviewId = next.Id;
            ShowProposal(next, done);
        }

        /// <summary>
        /// A mark added by the analyst (with its key or by accepting): the proposal of the same kind near it is answered with
        /// this frame (the one being checked first).
        /// </summary>
        private void ResolveProposalsByMark(SwimMarkType type, long timestamp)
        {
            // Accepting a proposal answers that proposal only (not a neighbour of the same kind).
            if (resolvingProposal || Race == null)
                return;

            // Manual analysis: the mark teaches also when nothing was proposed for its event.
            LearnFromMark(type, timestamp);
            if (Race.Proposals.Count == 0)
                return;

            // During the check, a mark of the kind being checked answers that proposal however far from it (2.9.5, as Enter; until 2.9.4
            // within 1 s): a kick within 0.2 s (they are many), a checkpoint of its distance, an event of a length in that length. Else
            // the proposal of the same event of the race (2.9.5: the checkpoint of this distance, the event of this length), else the
            // nearest proposal of its kind waiting (a kick within 0.2 s, the others within 0.6 s). A kick added far from the proposals
            // (one the detector missed) answers none.
            SwimProposal current = CurrentProposal;
            SwimMark split = type == SwimMarkType.Split ?
                Race.Marks.LastOrDefault(m => m.Type == SwimMarkType.Split && m.Pilot == null && SameFrame(m.Timestamp, timestamp)) : null;
            Func<SwimProposal, bool> sameEvent = x => x.Kind == KindRaceSplit ? split != null && split.Distance == x.Index :
                LengthOfEvent(x) >= 0 && InLengthOfEvent(x, timestamp);
            double near = type == SwimMarkType.Kick ? 0.2 : 0.6;
            SwimProposal p = null;
            if (current != null && current.Type == type)
            {
                if (type == SwimMarkType.Kick)
                    p = Math.Abs(Timing.Seconds(timestamp, current.Timestamp)) <= 0.2 ? current : null;
                else if (current.Kind == KindRaceSplit || LengthOfEvent(current) >= 0)
                    p = sameEvent(current) ? current : null;
                else
                    p = current;
            }

            if (p == null && type != SwimMarkType.Kick)
                p = Race.Proposals.Where(x => x.Type == type && sameEvent(x)).OrderBy(x => Math.Abs(timestamp - x.Timestamp)).FirstOrDefault();
            if (p == null)
            {
                p = Race.Proposals.Where(x => x.Type == type && Math.Abs(Timing.Seconds(timestamp, x.Timestamp)) <= near &&
                        (x.Kind != KindRaceSplit || sameEvent(x)))
                    .OrderBy(x => Math.Abs(timestamp - x.Timestamp)).FirstOrDefault();
            }

            if (p == null)
                return;

            // A proposal the auto pilot put: the analyst's mark replaces the pilot's mark at another frame (a checkpoint: of the
            // same distance only; else it is not the answer to it).
            SwimMark pilot = PilotMarkOf(p);
            if (pilot != null)
            {
                SwimMarkType markType = type == SwimMarkType.WallContact ? SwimMarkType.Split : type;
                SwimMark added = Race.Marks.LastOrDefault(m => m.Type == markType && m.Pilot == null && SameFrame(m.Timestamp, timestamp));
                if (added == null || (markType == SwimMarkType.Split && added.Distance != pilot.Distance))
                    return;
                int wall = pilot.Type == SwimMarkType.Breakout && pilot.Pilot != null ? pilot.Pilot.Wall : -1;
                ReplacePilotMark(pilot, added);

                // As Enter at this frame: the automatic strokes before the breakout go, its underwater distance again.
                if (wall >= 0)
                {
                    TrimAutoStrokesBeforeBreakout(wall, timestamp);
                    EstimateUnderwaterDistance(wall, timestamp);
                }
            }

            Answer(p, timestamp, true);
            RecombineWaitingProposals(p.Kind);
            MoveEventProposals(p.Kind, p.Index, timestamp);
            if (!resolvingProposal && p.Id == reviewId)
            {
                // After the command of the key has finished (it still works on this frame), the next proposal.
                string done = "✓ " + ProposalLabel(p);
                if (player != null && !player.IsDisposed && player.IsHandleCreated)
                    player.BeginInvoke((Action)(() => NextProposal(timestamp, done)));
                else
                    NextProposal(timestamp, done);
            }
        }

        /// <summary>Further from the proposal than this (s), the frame kept answers it but its cues do not teach their delay (2.9.5).</summary>
        private const double FarAnswer = 1.0;

        /// <summary>
        /// (2.9.5) The wall of the length of an event of the race (0: the start, for the first movement, the release from the block, the
        /// entry and the breakout after the start); -1 for the others (the checkpoints, the start signal, the events of the skeleton).
        /// </summary>
        private static int LengthOfEvent(SwimProposal p)
        {
            if (p.Kind == KindRaceBreakout || p.Kind == KindRacePushoff)
                return p.Index;
            if (p.Kind == KindRaceReaction || p.Kind == KindRaceBlock || p.Kind == KindRaceEntry)
                return 0;
            return -1;
        }

        /// <summary>
        /// (2.9.5) The frame can be the event of this proposal, however far from it: an event of a length is in that length, after its
        /// wall (or the start) less 1 s (the wall marked can be a little late) and before the next wall; walls not marked do not limit it.
        /// Any frame for the others.
        /// </summary>
        private bool InLengthOfEvent(SwimProposal p, long timestamp)
        {
            int wall = LengthOfEvent(p);
            if (wall < 0 || Race == null || Timing == null || !Timing.IsValid)
                return true;

            SwimMark from = wall > 0 ? Race.GetSplit(wall) : null;
            long? origin = wall > 0 ? (from != null ? from.Timestamp : (long?)null) : Race.StartTimestamp;
            if (origin.HasValue && Timing.Seconds(timestamp, origin.Value) < -1.0)
                return false;

            SwimMark to = Race.Pool > 0 ? Race.GetSplit(wall + Race.Pool) : null;
            return to == null || timestamp < to.Timestamp;
        }

        /// <summary>The answer of the analyst to a proposal: kept for the learning, the proposal goes.</summary>
        private void Answer(SwimProposal p, long timestamp, bool right)
        {
            bool shown = Race.Proposals.Remove(p);

            // One at an official time (since 2026-10-19) has no cues: the frame kept teaches what the reading itself found there (Read)
            // when it is near it (as the analyst's marks teach), or nothing (none, far, or Delete: not an answer about the reading).
            SwimProposal taught = !p.Official ? p : right && p.Read != null && Math.Abs(Timing.Seconds(timestamp, p.Read.Timestamp)) <= 1.5 ? ReadOf(p) : null;
            SwimLearningRecord record = taught == null ? null : new SwimLearningRecord
            {
                Date = DateTime.Now,
                Kind = taught.Kind,
                Video = SwimLearning.VideoCode(videoPath),
                Right = right,
                Confidence = taught.Confidence,
                Key = AnswerKey(taught)
            };

            if (record != null && right)
            {
                record.Error = Timing.Seconds(timestamp, taught.Timestamp);

                // (2.9.5) A frame kept far from the proposal (more than FarAnswer, accepted since 2.9.5): the event is there, but the
                // reading found something else, its cues do not teach their delay (one such answer would widen the spread of each cue).
                if (p.Official || Math.Abs(record.Error) <= FarAnswer)
                {
                    foreach (KeyValuePair<string, long> c in taught.Cues)
                        record.Cues[c.Key] = Timing.Seconds(timestamp, c.Value);
                }

                record.Measures = MeasuresAt(taught, timestamp);
            }

            if (record != null)
            {
                Learning.Add(record);
                Learning.Save();
                LearningChanged();
            }

            // The mark of the event keeps its answer: undone, deleted or replaced, the answer goes with it.
            if (record != null && right && record.Key != null)
            {
                SwimMarkType markType = p.Type == SwimMarkType.WallContact && !Race.IsTraining ? SwimMarkType.Split : p.Type;
                SwimMark mark = Race.Marks.Where(m => m.Type == markType && SameFrame(m.Timestamp, timestamp)).FirstOrDefault();
                if (mark != null)
                    mark.Taught = record.Key;
            }

            if (reviewId != null && shown)
            {
                reviewAnswered++;
                if (!right)
                    reviewWrong++;
                else if (!SameFrame(timestamp, p.Timestamp))
                    reviewCorrected++;
            }

            if (p.Id == reviewId && !Race.Proposals.Any(x => x.Id == reviewId))
                RefreshProposalsUi();
        }

        /// <summary>A start kept this near (s) the one proposed from a signal heard is that signal (its tones are learned, 2.9.3).</summary>
        private const double StartToneError = 0.2;

        /// <summary>Values of the image at the frame kept (the entry: level of the hands under the block, in body sizes).</summary>
        private Dictionary<string, double> MeasuresAt(SwimProposal p, long timestamp)
        {
            // (2.9.3) A start kept at its signal heard (within StartToneError): the tones of that signal, the start system of the pool
            // (SwimLearning.StartTones). Kept elsewhere (the start was another signal, or none heard): nothing.
            if (p.Kind == KindRaceStart)
            {
                if (p.Measures == null || !p.Measures.ContainsKey(ToneLow) || !p.Measures.ContainsKey(ToneHigh) ||
                    Math.Abs(Timing.Seconds(timestamp, p.Timestamp)) > StartToneError)
                    return null;

                Dictionary<string, double> tones = new Dictionary<string, double>();
                foreach (string key in new[] { ToneLow, ToneHigh, ToneSeconds, TonePurity })
                {
                    if (p.Measures.ContainsKey(key))
                        tones[key] = p.Measures[key];
                }

                return tones;
            }

            if (p.Kind != SwimEventDetector.StartEntry || p.Measures == null || !p.Measures.ContainsKey("block") || !p.Measures.ContainsKey("L"))
                return null;

            SwimSkeleton s = Race.SkeletonAt(timestamp);
            PointF[] points = new PointF[SwimJoints.Count];
            float[] visibility = new float[SwimJoints.Count];
            if (s == null || !s.TryGet(timestamp, 2, points, visibility))
                return null;

            double wrist = double.NaN;
            foreach (int j in new[] { SwimJoints.LeftWrist, SwimJoints.RightWrist })
            {
                if (visibility[j] >= 0.3f && (double.IsNaN(wrist) || points[j].Y > wrist))
                    wrist = points[j].Y;
            }

            double L = p.Measures["L"];
            if (double.IsNaN(wrist) || L <= 0)
                return null;

            return new Dictionary<string, double> { { "level", (wrist - p.Measures["block"]) / L } };
        }

        /// <summary>Delete the proposals waiting (all, or the ones of the skeleton at the current frame).</summary>
        public void DeleteProposals()
        {
            if (Race == null || Race.Proposals.Count == 0)
                return;

            int n = Race.Proposals.Count;
            bool pilotStops = PilotActive && pilotStep == PilotStep.Start;
            Race.Proposals.Clear();
            EndReview(false);
            Changed(false);
            int pilot = Race.Marks.Count(m => m.Pilot != null);
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Διαγράφηκαν {0} προτάσεις (χωρίς απάντηση: η μάθηση δεν αλλάζει).{1}{2}"), n,
                pilot > 0 ? string.Format(CultureInfo.InvariantCulture, SwimLang.T(" Τα {0} σημάδια του πιλότου μένουν στην ανάλυση, χωρίς έλεγχο (δεν διδάσκουν)."), pilot) : "",
                pilotStops ? SwimLang.T(" ✈ Ο αυτόματος πιλότος σταμάτησε.") : ""), false);
        }

        /// <summary>The command of a proposal: its type's, and C for the strokes the technical pilot proposes (a stroke of the other proposals only teaches).</summary>
        private static bool CommandOf(SwimProposal p, out PlayerScreenCommands command)
        {
            if (p.Type == SwimMarkType.Stroke && (p.Kind == KindTechLastStroke || p.Kind == KindTechFirstStroke || p.Kind == KindTechStroke))
            {
                command = PlayerScreenCommands.SwimStroke;
                return true;
            }

            // The head at a technical distance of the pilot (−5 / +5): T at its distance (answeringSplit).
            if (p.Type == SwimMarkType.TechSplit && p.Index != 0)
            {
                command = PlayerScreenCommands.SwimTechSplit;
                return true;
            }

            return CommandOf(p.Type, out command);
        }

        private static bool CommandOf(SwimMarkType type, out PlayerScreenCommands command)
        {
            switch (type)
            {
                case SwimMarkType.Reaction: command = PlayerScreenCommands.SwimReaction; return true;
                case SwimMarkType.HandsOff: command = PlayerScreenCommands.SwimHandsOff; return true;
                case SwimMarkType.BackFoot: command = PlayerScreenCommands.SwimBackFoot; return true;
                case SwimMarkType.FeetOff: command = PlayerScreenCommands.SwimFeetOff; return true;
                case SwimMarkType.Entry: command = PlayerScreenCommands.SwimEntry; return true;
                case SwimMarkType.Kick: command = PlayerScreenCommands.SwimKick; return true;
                case SwimMarkType.Breakout: command = PlayerScreenCommands.SwimBreakout; return true;
                case SwimMarkType.WallContact: command = PlayerScreenCommands.SwimWallContact; return true;
                case SwimMarkType.RotationStart: command = PlayerScreenCommands.SwimRotation; return true;
                default: command = PlayerScreenCommands.SwimUndo; return false;
            }
        }

        /// <summary>"Αποκόλληση ποδιών (82%)", "Δελφινισμός 3 (70%)".</summary>
        public static string ProposalLabel(SwimProposal p)
        {
            string name;
            switch (p.Type)
            {
                case SwimMarkType.Reaction: name = SwimLang.T("Πρώτη κίνηση (αντίδραση)"); break;
                case SwimMarkType.HandsOff: name = SwimLang.T("Χέρια φεύγουν από τον βατήρα"); break;
                case SwimMarkType.BackFoot: name = SwimLang.T("Πίσω πόδι φεύγει"); break;
                case SwimMarkType.FeetOff:
                    name = p.Kind == KindRacePushoff ? SwimLang.T("Αποκόλληση από τον τοίχο των ") + p.Index.ToString(CultureInfo.InvariantCulture) + SwimLang.T("μ.") :
                        p.Kind == SwimEventDetector.TurnFeet || p.Kind == KindTechPushoff ? SwimLang.T("Αποκόλληση από τον τοίχο") : p.Kind == KindRaceBlock ? SwimLang.T("Αποκόλληση ποδιών (εκκίνηση)") : SwimLang.T("Αποκόλληση ποδιών");
                    break;
                case SwimMarkType.Entry: name = SwimLang.T("Είσοδος χεριών στο νερό"); break;
                case SwimMarkType.Kick: name = SwimLang.T("Δελφινισμός") + (p.Index > 0 ? " " + p.Index : ""); break;
                case SwimMarkType.Breakout: name = SwimLang.T("Έξοδος από το νερό") + (p.Kind == KindRaceBreakout ? (p.Index > 0 ? SwimLang.T(" μετά τα ") + p.Index.ToString(CultureInfo.InvariantCulture) + SwimLang.T("μ.") : SwimLang.T(" μετά την εκκίνηση")) : ""); break;
                case SwimMarkType.WallContact: name = SwimLang.T("Επαφή στον τοίχο"); break;
                case SwimMarkType.RotationStart: name = SwimLang.T("Αρχή περιστροφής"); break;
                case SwimMarkType.Stroke:
                    name = p.Kind == KindTechLastStroke ? SwimLang.T("Τελευταία χεριά πριν τον τοίχο") : p.Kind == KindTechFirstStroke ? SwimLang.T("Πρώτη χεριά μετά την έξοδο") :
                        SwimLang.T("Χεριά") + (p.Kind == KindTechStroke && p.Index > 0 ? " " + p.Index.ToString(CultureInfo.InvariantCulture) : "");
                    break;
                case SwimMarkType.Split:
                    name = (p.Kind == KindRaceSplit ? SwimLang.T("Πέρασμα ") + p.Index.ToString(CultureInfo.InvariantCulture) + SwimLang.T("μ.") : SwimLang.T("Πέρασμα")) +
                        (p.Official ? SwimLang.T(" · από τον επίσημο χρόνο") : "");
                    break;
                case SwimMarkType.TechSplit:
                    name = p.Kind == KindTechHeadStart ? SwimLang.T("Κεφάλι στα 5 μ. από την εκκίνηση") :
                        p.Index < 0 ? SwimLang.T("Κεφάλι στα −5 πριν τον τοίχο") : SwimLang.T("Κεφάλι στα +5 μετά τον τοίχο");
                    if (p.Kind == KindTechHeadIn || p.Kind == KindTechHeadOut || p.Kind == KindTechHeadStart)
                        name += SwimLang.T(" · σημαίες ύπτιου");
                    break;
                case SwimMarkType.Signal: name = p.Kind == KindRaceStart ? SwimLang.T("Σήμα εκκίνησης (φως)") + (p.Index > 1 ? SwimLang.T(" · εναλλακτική ") + (p.Index - 1) : "") : SwimLang.T("Σήμα εκκίνησης"); break;
                default: name = p.Type.ToString(); break;
            }

            return string.Format(CultureInfo.InvariantCulture, "{0} ({1:0}%)", name, 100 * p.Confidence);
        }

        private void RefreshProposalsUi()
        {
            if (form != null && !form.IsDisposed)
                form.RefreshSkeleton();
            DataVersion++;
        }

        /// <summary>On the video: the proposal being checked (or one at this frame), its keys and how far the frame shown is from it.</summary>
        private void DrawProposalPrompt(Graphics g, long timestamp, Size renderingSize)
        {
            if (Race == null || Race.Proposals.Count == 0)
                return;

            SwimProposal p = Reviewing ? CurrentProposal : Race.Proposals.FirstOrDefault(x => SameFrame(x.Timestamp, timestamp));
            if (p == null)
                return;

            long frames = (long)Math.Round((timestamp - p.Timestamp) / Math.Max(1, Timing.AverageTimestampsPerFrame));
            int index = PendingProposals.FindIndex(x => x.Id == p.Id) + 1;
            string title = string.Format(CultureInfo.InvariantCulture, "✨ {0}/{1}  {2}", index, Race.Proposals.Count, ProposalLabel(p));
            string where = frames == 0 ? SwimLang.T("στο καρέ της πρότασης") : string.Format(CultureInfo.InvariantCulture, SwimLang.T("{0:+0;−0} καρέ από την πρόταση"), frames);
            SwimMark pilotMark = PilotMarkOf(p);
            bool pilot = pilotMark != null;
            if (pilot)
            {
                long away = (long)Math.Round((pilotMark.Timestamp - p.Timestamp) / Math.Max(1, Timing.AverageTimestampsPerFrame));
                title += away == 0 ? SwimLang.T(" · ✈ μπήκε") : string.Format(CultureInfo.InvariantCulture, SwimLang.T(" · ✈ μπήκε στο {0:+0;−0} καρέ"), away);
            }
            string keys = Reviewing ? (pilot ? SwimLang.T("Enter σωστό · ← → καρέ · Delete λάθος (σβήνεται) · Esc τέλος") : SwimLang.T("Enter σωστό · ← → καρέ · Delete λάθος · Esc τέλος")) :
                SwimLang.T("«Έλεγχος» για να απαντήσεις");

            // A doubtful proposal (less than half of such proposals within 0.2 s of the analysts) says so (since 2026-10-18).
            bool doubtful = p.Confidence < DoubtfulConfidence;
            if (doubtful)
                title = "⚠ " + title + SwimLang.T(" · έλεγξε προσεκτικά");

            PaintProposalPrompt(g, title, where, keys, frames == 0, renderingSize, doubtful);
        }

        /// <summary>Below this confidence a proposal is shown as doubtful (amber, "check carefully").</summary>
        internal const double DoubtfulConfidence = 0.5;

        /// <summary>The box of a proposal at the top of the video: title, where the frame is, the keys; a doubtful one in amber.</summary>
        public static void PaintProposalPrompt(Graphics g, string title, string where, string keys, bool atProposal, Size renderingSize, bool doubtful = false)
        {
            // Fonts of the theme are shared: not disposed here.
            Font big = SwimTheme.Display(11f);
            Font small = SwimTheme.Ui(8.5f, FontStyle.Bold);
            SizeF s1 = g.MeasureString(title, big), s2 = g.MeasureString(where + "   " + keys, small);
            float w = Math.Max(s1.Width, s2.Width) + 24, h = s1.Height + s2.Height + 14;
            float x = Math.Max(8, (renderingSize.Width - w) / 2), y = 10;
            // HUD (2.9.0): a glass panel, its brackets and the line under it cyan at the proposal, amber away from it or doubtful.
            RectangleF box = new RectangleF(x, y, w, h);
            Color accent = atProposal && !doubtful ? SwimTheme.Accent : SwimTheme.Amber;
            using (SolidBrush under = new SolidBrush(Color.FromArgb(70, 2, 12, 20)))
                g.FillRectangle(under, box);
            SwimTheme.HudPanel(g, box, accent);
            SwimTheme.GlowLine(g, atProposal ? SwimTheme.Accent : SwimTheme.Amber, 1.6f, new PointF(box.Left + 10, box.Bottom - 3), new PointF(box.Right - 10, box.Bottom - 3));
            using (SolidBrush white = new SolidBrush(SwimTheme.Text))
            using (SolidBrush dim = new SolidBrush(SwimTheme.Dim))
            using (SolidBrush amber = new SolidBrush(SwimTheme.Amber))
            {
                g.DrawString(title, big, doubtful ? amber : white, x + 12, y + 5);
                g.DrawString(where, small, atProposal ? dim : amber, x + 12, y + 7 + s1.Height);
                g.DrawString(keys, small, dim, x + 12 + g.MeasureString(where + "   ", small).Width, y + 7 + s1.Height);
            }
        }
        #endregion
    }
}
