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
using System.Windows.Forms;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The auto pilot ("✈ Αυτόματος πιλότος", since 2026-10-18): the whole analysis of a race video without the analyst, then the
    /// report. The start from the light or the signal heard (a doubtful one is checked first), the swimmer the camera follows (taken
    /// after a few seconds without an answer when he is seen near; otherwise the analyst shows him), the whole race read as
    /// "🤖 Όλος ο αγώνας", every measured proposal (checkpoints, walls, breakouts) put as its mark as Enter would put it, and the PDF
    /// of the report. The events a camera in the stands does not show (first movement, feet off the block, entry, feet off the
    /// walls: their usual times only) are not put: they wait for the check. What the pilot puts does not teach the learning: its
    /// proposals stay waiting, and the analyst's answer in «Έλεγχος» (Enter at the frame: right; arrows and Enter: corrected, the
    /// pilot's mark replaced; Delete: wrong, the mark goes) teaches as any answer. "🤖 Όλος ο αγώνας" and the rest work as before.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Auto pilot
        /// <summary>What the pilot is doing or waiting for.</summary>
        private enum PilotStep
        {
            None,

            /// <summary>The start found is doubtful: the analyst checks it (Enter), then the pilot goes on.</summary>
            Start,

            /// <summary>The swimmer: proposed (Enter, or taken after the countdown when seen near) or clicked.</summary>
            Swimmer,

            /// <summary>The race is being read.</summary>
            Reading,

            /// <summary>The swimmer was lost after a wall: the analyst clicks him again after the breakout.</summary>
            Lost
        }

        /// <summary>
        /// A start proposal at least this sure is taken without asking: the light seen, the signal heard (a flash of a camera, a light
        /// seen weakly or the start put before the dive of the swimmers are checked first).
        /// </summary>
        internal const double PilotStartConfidence = 0.6;

        /// <summary>
        /// Seconds the swimmer proposed waits for an answer before the pilot takes him, when he is seen near (a camera following him;
        /// lab: on the phones of the stands the swimmer in the middle of the image was another one in 7 of 9, so there it asks).
        /// </summary>
        private const int PilotPickSeconds = 20;

        private string pilotRace;
        private PilotStep pilotStep;
        private Timer pilotTimer;
        private int pilotCountdown;
        private string pilotStartNote;
        private bool pilotSwimmerAuto;
        private int pilotLostWall;
        private Timer pdfTimer;
        private string pdfRace;
        private int pdfWait;

        /// <summary>The last reading of the start was cancelled or failed (the pilot stops then).</summary>
        private bool startLightCancelled;

        /// <summary>The check goes through the doubtful proposals of the pilot first.</summary>
        private bool reviewDoubtful;

        /// <summary>The auto pilot works on the race of the video (it may be waiting for the analyst).</summary>
        public bool PilotActive
        {
            get { return pilotRace != null && Race != null && Race.Id == pilotRace; }
        }

        /// <summary>For the buttons: what the pilot does or waits for, empty when it does not work.</summary>
        public string PilotWaiting
        {
            get
            {
                if (!PilotActive)
                    return "";

                switch (pilotStep)
                {
                    case PilotStep.Start:
                        return "έλεγξε την εκκίνηση";
                    case PilotStep.Swimmer:
                        return pilotCountdown > 0 ? string.Format(CultureInfo.InvariantCulture, "συνεχίζει σε {0} s", pilotCountdown) : "δείξε τον αθλητή";
                    case PilotStep.Lost:
                        return "δείξε ξανά τον αθλητή";
                    default:
                        return "αναλύει…";
                }
            }
        }

        /// <summary>The proposals the pilot put that are doubtful (less than half of such proposals within 0.2 s of the analysts).</summary>
        public int PilotDoubtful
        {
            get { return Race == null ? 0 : Race.Proposals.Count(IsDoubtfulPilot); }
        }

        private static bool IsDoubtfulPilot(SwimProposal p)
        {
            return p.Pilot && p.Confidence < DoubtfulConfidence;
        }

        /// <summary>The button "✈ Αυτόματος πιλότος": the whole analysis of the race of the video, then the report.</summary>
        public void RunAutoPilot()
        {
            if (Race == null || !HasVideo)
            {
                Status("Άνοιξε πρώτα ένα βίντεο αγώνα σε οθόνη αναπαραγωγής.", true);
                return;
            }

            if (Race.IsTraining)
            {
                Status("Ο αυτόματος πιλότος είναι για βίντεο αγώνα (το «Είδος βίντεο» στα Στοιχεία).", true);
                return;
            }

            if (!PoolHasMarks)
            {
                Status("✈ Αυτόματος πιλότος: πισίνα 50 ή 25 (με τα σημάδια των σχοινιών).", true);
                return;
            }

            if (AutoStrokesRunning || StartLightRunning)
                return;

            // Pressed again while it waits for the analyst: what it waits for (it goes on from there).
            if (PilotActive && pilotStep != PilotStep.None && pilotStep != PilotStep.Reading)
            {
                PilotResume();
                return;
            }

            RefreshTiming();
            if (!Timing.IsValid)
            {
                Status("Δεν είναι γνωστός ο ρυθμός καρέ του βίντεο: ο αυτόματος πιλότος δεν μπορεί να ξεκινήσει.", true);
                return;
            }

            StopPilot();
            EndReview(false);
            if (PickTarget != SwimPickTarget.None)
                CancelPick();
            pilotRace = Race.Id;
            pilotStep = PilotStep.None;
            pilotSwimmerAuto = false;
            pilotStartNote = Race.StartTimestamp.HasValue ? "ήταν ήδη βαλμένη" : null;
            RefreshPilotUi();

            if (!Race.StartTimestamp.HasValue)
            {
                Status("✈ Αυτόματος πιλότος: βρίσκει την εκκίνηση…", false);
                FindStartLight();
                if (!PilotActive)
                    return;

                if (startLightCancelled)
                {
                    StopPilot();
                    Status("✈ Ο αυτόματος πιλότος σταμάτησε: η αναζήτηση της εκκίνησης δεν ολοκληρώθηκε.", true);
                    return;
                }

                if (!Race.StartTimestamp.HasValue)
                {
                    SwimProposal best = Race.Proposals.Where(p => p.Kind == KindRaceStart).OrderBy(p => p.Index).FirstOrDefault();
                    if (best == null)
                    {
                        StopPilot();
                        Status("✈ Ο πιλότος σταμάτησε: δεν βρέθηκε το σήμα εκκίνησης (δεν φαίνεται φως ούτε ακούγεται το σήμα). Βάλε την εκκίνηση " +
                            "με το κουμπί «Εκκίνηση» στο καρέ του σήματος και πάτα ξανά «✈ Αυτόματος πιλότος».", true);
                        return;
                    }

                    if (!PilotTrustsStart(best))
                    {
                        // The check of the start (begun by the search): Enter there and the pilot goes on (AcceptStartProposal).
                        pilotStep = PilotStep.Start;
                        if (CurrentProposal == null || CurrentProposal.Kind != KindRaceStart)
                            BeginReview(best);
                        PilotPrompt();
                        return;
                    }

                    PilotSetStart(best);
                }
            }

            PilotRace();
        }

        /// <summary>The pilot waits for the analyst and its button is pressed again: the question again (the click armed again if needed).</summary>
        private void PilotResume()
        {
            switch (pilotStep)
            {
                case PilotStep.Start:
                    if (Race.StartTimestamp.HasValue)
                    {
                        PilotRace();
                        return;
                    }

                    SwimProposal start = Race.Proposals.Where(p => p.Kind == KindRaceStart).OrderBy(p => p.Index).FirstOrDefault();
                    if (start == null)
                    {
                        StopPilot();
                        RunAutoPilot();
                        return;
                    }

                    if (CurrentProposal == null || CurrentProposal.Kind != KindRaceStart)
                        BeginReview(start);
                    PilotPrompt();
                    return;
                case PilotStep.Swimmer:
                    if (!pickForRace || !PickingTrack)
                    {
                        PilotRace();
                        return;
                    }

                    PilotPrompt();
                    return;
                case PilotStep.Lost:
                    if (!pickForRace || !PickingTrack)
                    {
                        StartWholeRace();
                        if (!PickingTrack)
                        {
                            StopPilot();
                            return;
                        }
                    }

                    PilotPrompt();
                    return;
            }
        }

        /// <summary>A start proposal the pilot takes without asking: sure enough, and with video enough after it for the race.</summary>
        private bool PilotTrustsStart(SwimProposal best)
        {
            if (best.Confidence < PilotStartConfidence || best.Cues.ContainsKey("dive"))
                return false;

            double left = Timing.Seconds(frameServer.VideoReader.WorkingZone.End, best.Timestamp);
            return Race.Distance <= 0 || left >= MinRaceSeconds();
        }

        /// <summary>The start proposed becomes the start of the race; the proposal waits for the check (the answer teaches it).</summary>
        private void PilotSetStart(SwimProposal best)
        {
            Race.Proposals.RemoveAll(x => x.Kind == KindRaceStart && x.Id != best.Id);
            resolvingProposal = true;
            try
            {
                HandleCommand(player, frameServer, PlayerScreenCommands.SwimStart, best.Timestamp);
            }
            finally
            {
                resolvingProposal = false;
            }

            best.Pilot = true;
            pilotStartNote = StartNote(best);
            EndReview(false);
            Changed(false);
        }

        /// <summary>"από το φως του σήματος (95%)".</summary>
        private static string StartNote(SwimProposal p)
        {
            string from = p.Cues.ContainsKey("dive") ? "λίγο πριν τη βουτιά (δεν φάνηκε φως ούτε ακούστηκε σήμα)" :
                p.Cues.ContainsKey("light") ? (p.Cues.ContainsKey("sound") ? "από το φως και τον ήχο του σήματος" : "από το φως του σήματος") :
                p.Cues.ContainsKey("sound") ? "από τον ήχο του σήματος" : "από το βίντεο";
            return string.Format(CultureInfo.InvariantCulture, "{0} ({1:0}%)", from, 100 * p.Confidence);
        }

        /// <summary>The swimmer, then the whole race (it starts at once with a swimmer seen near, after the countdown).</summary>
        private void PilotRace()
        {
            if (!PilotActive || !HasVideo || !Race.StartTimestamp.HasValue)
            {
                StopPilot();
                return;
            }

            // A new reading of the whole race (not the next lengths after a swimmer lost in an earlier reading).
            pilotStep = PilotStep.None;
            StopPilotCountdown();
            EndReview(false);
            if (PickTarget != SwimPickTarget.None)
                CancelPick();
            resumeWall = -1;
            resumeLapStart = long.MinValue;
            resumeAt = long.MinValue;

            // The swimmer is looked for a few seconds after the start, as the analysts click him (after the breakout).
            SeekTo(Math.Min(frameServer.VideoReader.WorkingZone.End, Race.StartTimestamp.Value + RealSpan(AutoPickSeconds)));
            StartWholeRace();
            if (!PilotActive)
                return;

            if (!PickingTrack)
            {
                StopPilot();
                return;
            }

            pilotStep = PilotStep.Swimmer;
            AutoPickResult pick = autoPick;
            if (pick != null && PilotTrustsPick(pick))
            {
                pilotCountdown = PilotPickSeconds;
                if (pilotTimer == null)
                {
                    pilotTimer = new Timer { Interval = 1000 };
                    pilotTimer.Tick += PilotTimer_Tick;
                }

                pilotTimer.Start();
            }

            PilotPrompt();
        }

        /// <summary>
        /// The swimmer proposed is seen near (his length at least the size of a swimmer near the camera, as the delays of the
        /// checkpoints count it): a camera following him. Far (a phone in the stands), the swimmer in the middle of the image is
        /// often another one.
        /// </summary>
        private bool PilotTrustsPick(AutoPickResult p)
        {
            Size reference = ReadingReader.Info.ReferenceSize;
            double scale = (double)StrokeWorkWidth / Math.Max(1, reference.Width);
            double dx = p.Head.X - p.Feet.X, dy = p.Head.Y - p.Feet.Y;
            return SwimSplitDetector.FarShare(scale * Math.Sqrt(dx * dx + dy * dy)) <= 0;
        }

        private void PilotTimer_Tick(object sender, EventArgs e)
        {
            if (!PilotActive || pilotStep != PilotStep.Swimmer || autoPick == null || !pickForRace || !PickingTrack || player == null || player.IsDisposed)
            {
                StopPilotCountdown();
                if (PilotActive && pilotStep == PilotStep.Swimmer)
                    PilotPrompt();
                return;
            }

            pilotCountdown--;
            if (pilotCountdown > 0)
            {
                PilotPrompt();
                return;
            }

            StopPilotCountdown();
            pilotSwimmerAuto = true;
            AcceptAutoPick(player);
        }

        private void StopPilotCountdown()
        {
            pilotCountdown = 0;
            if (pilotTimer != null)
                pilotTimer.Stop();
        }

        /// <summary>What the pilot waits for, on the status line.</summary>
        private void PilotPrompt()
        {
            if (!PilotActive)
                return;

            switch (pilotStep)
            {
                case PilotStep.Start:
                    SwimProposal s = CurrentProposal;
                    Status("✈ Η εκκίνηση που βρέθηκε δεν είναι σίγουρη" +
                        (s != null && s.Kind == KindRaceStart ? string.Format(CultureInfo.InvariantCulture, " ({0:0}%)", 100 * s.Confidence) : "") +
                        ": δες την στο βίντεο. Enter αν είναι σωστή (←/→ και Enter: διόρθωση · Delete: η επόμενη υποψήφια) και ο πιλότος " +
                        "συνεχίζει μόνος του ως το PDF · Esc: σταμάτημα.", true);
                    break;
                case PilotStep.Swimmer:
                    if (autoPick != null && pilotCountdown > 0)
                        Status(string.Format(CultureInfo.InvariantCulture,
                            "✈ Σε {0} s ο πιλότος συνεχίζει με τον αθλητή που ακολουθεί η κάμερα (σημειωμένος στο βίντεο) · Enter: τώρα · " +
                            "↑/↓: άλλη διαδρομή · κλικ στο κεφάλι και στα πόδια: άλλος αθλητής · Esc: σταμάτημα.", pilotCountdown), false);
                    else if (autoPick != null)
                        Status("✈ Ο πιλότος περιμένει: είναι αυτός ο αθλητής (σημειωμένος στο βίντεο); Enter: ναι · ↑/↓: άλλη διαδρομή · " +
                            "ή κλικ στο κεφάλι και μετά στα πόδια του · Esc: σταμάτημα. Μετά συνεχίζει μόνος του ως το PDF.", true);
                    else
                        Status("✈ Ο πιλότος περιμένει τον αθλητή: σε ένα καρέ μετά την έξοδο από το νερό, κλικ στο κεφάλι του και μετά " +
                            "στα πόδια του · Esc: σταμάτημα. Μετά συνεχίζει μόνος του ως το PDF.", true);
                    break;
                case PilotStep.Lost:
                    Status(string.Format(CultureInfo.InvariantCulture,
                        "✈ Μετά τον τοίχο των {0}{1} ο αθλητής χάθηκε: σε ένα καρέ μετά την έξοδο από το νερό, κλικ στο κεφάλι του και " +
                        "μετά στα πόδια του · ο πιλότος συνεχίζει μόνος του ως το PDF (Esc: σταμάτημα).", pilotLostWall, Race.Unit), true);
                    break;
            }

            RefreshPilotUi();
        }

        /// <summary>The pilot stops (what it put stays, waiting for the check).</summary>
        private void StopPilot()
        {
            bool was = pilotRace != null;
            pilotRace = null;
            pilotStep = PilotStep.None;
            StopPilotCountdown();
            if (was)
                RefreshPilotUi();
        }

        /// <summary>The next step of the pilot after the command or the key being handled.</summary>
        private void PilotLater(Action next)
        {
            if (player != null && !player.IsDisposed && player.IsHandleCreated)
                player.BeginInvoke(next);
            else
                next();
        }

        private void RefreshPilotUi()
        {
            if (form != null && !form.IsDisposed)
                form.RefreshPilot();
            DataVersion++;
        }

        /// <summary>
        /// The proposals of a reading of the pilot put as their marks, as Enter on each would put them but without the answer (the
        /// learning waits for the analyst's check): the checkpoints and walls first (the breakouts need the walls), then the
        /// breakouts with what Enter does after them (the automatic strokes before them go, the underwater distance is estimated).
        /// The start events and the feet off the walls (their usual times only) wait for the check.
        /// </summary>
        private void PilotAccept(List<SwimProposal> made)
        {
            bool finish = false;
            foreach (SwimProposal p in made.Where(x => x.Kind == KindRaceSplit).OrderBy(x => x.Index).ToList())
                finish |= PilotSplit(p);
            foreach (SwimProposal p in made.Where(x => x.Kind == KindRaceBreakout).OrderBy(x => x.Index).ToList())
                PilotBreakout(p);

            // The final frame of the report: the frame of the finish shown (it is the frame painted), with the graphics of all the race.
            SwimMark end = Race.GetSplit(Race.Distance);
            if (finish && end != null && HasVideo)
            {
                Recompute();
                SeekTo(end.Timestamp);
                CaptureFinalFrame(end.Timestamp);
            }
        }

        /// <summary>The checkpoint of the proposal put by the pilot. True when it is a new finish (its final frame is taken).</summary>
        private bool PilotSplit(SwimProposal p)
        {
            // The analyst's checkpoint stays (none is proposed where he has one).
            int distance = p.Index;
            SwimMark existing = Race.GetSplit(distance);
            if (existing != null && existing.Pilot == null)
                return false;

            p.Pilot = true;
            if (existing != null && SameFrame(existing.Timestamp, p.Timestamp))
            {
                p.PilotMark = existing.Id;
                return false;
            }

            SwimMark mark = new SwimMark { Type = SwimMarkType.Split, Timestamp = p.Timestamp, Distance = distance, Pilot = new SwimPilotMark { Wall = distance } };
            if (existing != null)
            {
                AddReplacing(mark, m => m.Type == SwimMarkType.Split && m.Distance == distance);
            }
            else
            {
                Race.Marks.Add(mark);
                undoStack.Add(mark.Id);
            }

            p.PilotMark = mark.Id;
            return distance == Race.Distance;
        }

        private void PilotBreakout(SwimProposal p)
        {
            // A breakout of the analyst on this frame is his.
            if (Race.Marks.Any(m => m.Type == SwimMarkType.Breakout && m.Pilot == null && SameFrame(m.Timestamp, p.Timestamp)))
                return;

            // The pilot's breakout after this wall from an earlier reading: the same, or replaced.
            SwimMark old = Race.Marks.FirstOrDefault(m => m.Type == SwimMarkType.Breakout && m.Pilot != null && m.Pilot.Wall == p.Index);
            p.Pilot = true;
            if (old != null && SameFrame(old.Timestamp, p.Timestamp))
            {
                p.PilotMark = old.Id;
                return;
            }

            SwimMark mark = new SwimMark { Type = SwimMarkType.Breakout, Timestamp = p.Timestamp, Pilot = new SwimPilotMark { Wall = p.Index } };
            Race.Marks.Add(mark);
            undoStack.Add(mark.Id);
            if (old != null)
                ReplacePilotMark(old, mark);
            p.PilotMark = mark.Id;

            // As Enter on it: the automatic strokes before it go (they come back if it moves), the underwater distance is estimated.
            List<SwimMark> trimmed = TrimAutoStrokesBeforeBreakout(p.Index, p.Timestamp);
            mark.Pilot.Strokes = trimmed.Count > 0 ? trimmed : null;
            if (EstimateUnderwaterDistance(p.Index, p.Timestamp).Length > 0)
            {
                SwimTurnManual turn = p.Index > 0 ? Race.GetTurn(p.Index, false) : null;
                mark.Pilot.Underwater = p.Index > 0 ? (turn != null ? turn.UnderwaterDist : null) : Race.UnderwaterDist;
            }
        }

        /// <summary>The mark the pilot put for a proposal that waits for the check, null if none (or the analyst made it his).</summary>
        private SwimMark PilotMarkOf(SwimProposal p)
        {
            if (p == null || Race == null || !p.Pilot || string.IsNullOrEmpty(p.PilotMark))
                return null;

            return Race.Marks.FirstOrDefault(m => m.Id == p.PilotMark && m.Pilot != null);
        }

        /// <summary>
        /// The analyst's mark of an event replaces the pilot's mark of it at another frame: what the pilot did with it is undone, and
        /// Undo of the analyst's mark puts it back.
        /// </summary>
        private void ReplacePilotMark(SwimMark pilot, SwimMark by)
        {
            if (pilot == null || by == null || pilot == by || !Race.Marks.Remove(pilot))
                return;

            UndoPilotEffects(pilot);
            ForgetTaught(pilot);
            List<SwimMark> replaced;
            if (!replacedBy.TryGetValue(by.Id, out replaced))
                replacedBy[by.Id] = replaced = new List<SwimMark>();
            replaced.Add(pilot);
        }

        /// <summary>The pilot's mark goes (Delete on its proposal: there is no such event there).</summary>
        private void RemovePilotMark(SwimMark pilot)
        {
            if (pilot == null || !Race.Marks.Remove(pilot))
                return;

            UndoPilotEffects(pilot);
            ForgetTaught(pilot);
            undoStack.Remove(pilot.Id);
            if (pilot.Type == SwimMarkType.Split)
                Race.StrokeCounts.Remove(pilot.Distance);
        }

        /// <summary>
        /// What putting a mark of the pilot changed goes back: the automatic strokes it removed, the underwater distance it estimated
        /// (unless it was changed since).
        /// </summary>
        private void UndoPilotEffects(SwimMark mark)
        {
            SwimPilotMark pilot = mark.Pilot;
            if (pilot == null || Race == null)
                return;

            if (pilot.Strokes != null)
            {
                foreach (SwimMark s in pilot.Strokes)
                {
                    if (!Race.Marks.Any(m => m.Type == SwimMarkType.Stroke && SameFrame(m.Timestamp, s.Timestamp)))
                        Race.Marks.Add(s);
                }
            }

            if (pilot.Underwater.HasValue)
            {
                SwimTurnManual turn = pilot.Wall > 0 ? Race.GetTurn(pilot.Wall, false) : null;
                double? now = pilot.Wall > 0 ? (turn != null ? turn.UnderwaterDist : null) : Race.UnderwaterDist;
                if (now.HasValue && Math.Abs(now.Value - pilot.Underwater.Value) < 1e-6)
                {
                    if (turn != null)
                        turn.UnderwaterDist = null;
                    else
                        Race.UnderwaterDist = null;
                }
            }

            pilot.Strokes = null;
            pilot.Underwater = null;
        }

        /// <summary>After a reading of the pilot: the swimmer lost (the analyst shows him again, then it goes on), or the report.</summary>
        private void PilotAfterReading(bool cancelled, int read)
        {
            if (!PilotActive)
                return;

            if (cancelled)
            {
                StopPilot();
                Status(string.Format(CultureInfo.InvariantCulture,
                    "✈ Ο αυτόματος πιλότος σταμάτησε (ακύρωση) μετά από {0} μήκη: όσα βρέθηκαν μπήκαν και περιμένουν τον «Έλεγχο».", read), true);
                return;
            }

            if (resumeWall > 0 && Race.StartTimestamp.HasValue)
            {
                // The swimmer lost after a wall (the camera turned fast): the next click goes on from there.
                pilotStep = PilotStep.Lost;
                pilotLostWall = resumeWall - Race.Pool;
                long at = resumeAt;
                StartWholeRace();
                if (!PickingTrack)
                {
                    StopPilot();
                    return;
                }

                if (at != long.MinValue)
                    SeekTo(Math.Min(frameServer.VideoReader.WorkingZone.End, at + RealSpan(3.0)));
                PilotPrompt();
                MessageBox.Show(DialogOwner, string.Format(CultureInfo.InvariantCulture,
                    "Μετά τον τοίχο των {0}{1} ο αθλητής χάθηκε (η κάμερα γύρισε γρήγορα). Όσα μήκη διαβάστηκαν μπήκαν ήδη.\n\n" +
                    "Σε ένα καρέ μετά την έξοδο από το νερό κάνε κλικ στο κεφάλι του αθλητή και μετά στα πόδια του: ο πιλότος συνεχίζει " +
                    "μόνος του ως τον τερματισμό και το PDF (Esc: σταμάτημα).", pilotLostWall, Race.Unit),
                    "✈ Ο αυτόματος πιλότος χρειάζεται εσένα", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PilotFinish(read);
        }

        /// <summary>The race is read: the video at the start, the PDF of the report, and what the pilot did (the check offered).</summary>
        private void PilotFinish(int read)
        {
            StopPilot();
            List<SwimProposal> put = Race.Proposals.Where(p => p.Pilot && PilotMarkOf(p) != null).ToList();
            int splits = put.Count(p => p.Kind == KindRaceSplit);
            int breakouts = put.Count(p => p.Kind == KindRaceBreakout);
            int raceSplits = Race.Marks.Count(m => m.Type == SwimMarkType.Split);
            int doubtful = PilotDoubtful;
            int waiting = Race.Proposals.Count(p => !p.Pilot);
            int strokes = AutoStrokeCount;
            SwimMark finish = Race.GetSplit(Race.Distance);
            string title = "✈ Αυτόματος πιλότος";

            // The video at the start, to watch the race with its graphics.
            if (Race.StartTimestamp.HasValue)
                SeekTo(Math.Max(frameServer.VideoReader.WorkingZone.Start, Race.StartTimestamp.Value - RealSpan(1.0)));

            if (read == 0 || raceSplits == 0)
            {
                string why = "✈ Ο αυτόματος πιλότος δεν μπόρεσε να διαβάσει τον αγώνα: δεν βρέθηκαν τα σημάδια των σχοινιών στη διαδρομή του " +
                    "αθλητή (π.χ. πλάνο χωρίς σχοινιά, ή ο αθλητής δεν ακολουθήθηκε). Δοκίμασε «🤖 Όλος ο αγώνας» με κλικ στον αθλητή σε άλλο " +
                    "καρέ, ή την ανάλυση με το χέρι.";
                Status(why, true);
                MessageBox.Show(DialogOwner, why, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // The PDF, once the final frame is on the Drive: it opens by itself.
            bool pdf = finish != null && Settings.IsConfigured;
            if (pdf)
                PilotPdf();

            StringBuilder b = new StringBuilder();
            b.Append("Ο αγώνας αναλύθηκε από τον αυτόματο πιλότο.\n\n");
            if (!string.IsNullOrEmpty(pilotStartNote))
                b.Append("• Εκκίνηση: ").Append(pilotStartNote).Append(".\n");
            b.Append(pilotSwimmerAuto ? "• Αθλητής: αυτός που ακολουθεί η κάμερα, χωρίς ερώτηση (δες ότι είναι ο σωστός).\n" :
                "• Αθλητής: αυτός που επέλεξες.\n");
            b.AppendFormat(CultureInfo.InvariantCulture, "• Μπήκαν {0} περάσματα και τοίχοι, {1} έξοδοι από το νερό και {2} χεριές.\n", splits, breakouts, strokes);
            if (raceSplits > splits)
                b.AppendFormat(CultureInfo.InvariantCulture, "• {0} περάσματα και τοίχοι ήταν ήδη δικά σου: έμειναν όπως ήταν.\n", raceSplits - splits);
            if (finish != null && Race.StartTimestamp.HasValue)
                b.Append("• Τερματισμός: ").Append(SwimReference.FormatTime(Timing.Seconds(finish.Timestamp, Race.StartTimestamp.Value))).Append(".\n");
            else
                b.Append("• ⚠ Δεν βρέθηκε ο τερματισμός: μάρκαρέ τον στο καρέ της επαφής και πάτα «PDF».\n");
            if (doubtful > 0)
                b.AppendFormat(CultureInfo.InvariantCulture, "• ⚠ {0} αβέβαια (κάτω από 50%, πορτοκαλί στον έλεγχο): δες αυτά πρώτα.\n", doubtful);
            if (waiting > 0)
                b.AppendFormat(CultureInfo.InvariantCulture,
                    "• {0} σημεία περιμένουν τον έλεγχό σου: όσα η κάμερα δεν δείχνει καθαρά (πρώτη κίνηση, αποδέσμευση, είσοδος, αποκόλληση " +
                    "από τους τοίχους) προτείνονται στους συνήθεις χρόνους και μπαίνουν στην ανάλυση μόνο αν τα επιβεβαιώσεις.\n", waiting);
            if (pdf)
                b.Append("• PDF: ετοιμάζεται και ανοίγει μόνο του.\n");
            else if (finish != null)
                b.Append("• PDF: δεν έχει ρυθμιστεί η σύνδεση με το KOE App (καρτέλα «Σύνδεση»)· μετά πάτα «PDF».\n");
            b.Append("\nΌ,τι έβαλε ο πιλότος δεν διδάσκει το πρόγραμμα μέχρι να το ελέγξεις: στον «Έλεγχο» Enter = σωστό, ←/→ και Enter = " +
                "διόρθωση, Delete = λάθος. Από αυτές τις απαντήσεις μαθαίνει.\n\n");
            b.Append(doubtful > 0 ? "Να ξεκινήσει τώρα ο έλεγχος, από τα αβέβαια;" : "Να ξεκινήσει τώρα ο έλεγχος;");

            Status(string.Format(CultureInfo.InvariantCulture, "✈ Ο πιλότος τελείωσε: {0} περάσματα και τοίχοι, {1} έξοδοι, {2} χεριές{3}{4} · «✨ Έλεγχος» " +
                "για να τα δεις (μαθαίνει από τις απαντήσεις σου).", splits, breakouts, strokes,
                doubtful > 0 ? string.Format(CultureInfo.InvariantCulture, " · ⚠ {0} αβέβαια", doubtful) : "",
                pdf ? " · το PDF ανοίγει μόλις είναι έτοιμο" : ""), doubtful > 0 || finish == null);
            RefreshProposalsUi();
            InvalidatePlayer();

            if (MessageBox.Show(DialogOwner, b.ToString(), title, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                BeginPilotReview();
        }

        /// <summary>The check of the proposals, the doubtful ones the pilot put first (then all of them in the order of the video).</summary>
        public void BeginPilotReview()
        {
            if (Race == null)
                return;

            SwimProposal first = Race.Proposals.Where(IsDoubtfulPilot).OrderBy(p => p.Timestamp).FirstOrDefault();
            BeginReview(first);
            reviewDoubtful = first != null && reviewId != null;
        }

        /// <summary>The PDF of the report once the final frame is on the Drive (at most a minute): it opens by itself.</summary>
        private void PilotPdf()
        {
            pdfRace = Race.Id;
            pdfWait = 0;
            if (pdfTimer == null)
            {
                pdfTimer = new Timer { Interval = 500 };
                pdfTimer.Tick += PdfTimer_Tick;
            }

            pdfTimer.Start();
        }

        private void PdfTimer_Tick(object sender, EventArgs e)
        {
            pdfWait++;
            if (Race == null || Race.Id != pdfRace || !Settings.IsConfigured)
            {
                pdfTimer.Stop();
                return;
            }

            if (FinalFrameUploading && pdfWait < 120)
                return;

            pdfTimer.Stop();
            SendByUser(true);
        }
        #endregion
    }
}
