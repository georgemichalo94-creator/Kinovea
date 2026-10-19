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
using System.Runtime.InteropServices;
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

        /// <summary>The walls after which the swimmer was lost and taken again without an answer (2.7.2).</summary>
        private readonly List<int> pilotRefound = new List<int>();

        /// <summary>The wall after which the pilot has just taken the swimmer again by itself, until the reading from there ends (-1: none).</summary>
        private int pilotRefoundPending = -1;

        /// <summary>The reading of the pilot goes on after a swimmer lost (the click after the breakout).</summary>
        private bool pilotWasLost;
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
                        return SwimLang.T("έλεγξε την εκκίνηση");
                    case PilotStep.Swimmer:
                        return pilotCountdown > 0 ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("συνεχίζει σε {0} s"), pilotCountdown) : SwimLang.T("δείξε τον αθλητή");
                    case PilotStep.Lost:
                        return pilotCountdown > 0 ? string.Format(CultureInfo.InvariantCulture, SwimLang.T("συνεχίζει σε {0} s"), pilotCountdown) : SwimLang.T("δείξε ξανά τον αθλητή");
                    default:
                        return SwimLang.T("αναλύει…");
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
            // Official times typed in the panel that cannot be read: corrected first (the walls would be read without them).
            if (form != null && !form.IsDisposed && form.OfficialTypedWrong())
                return;

            if (Race == null || !HasVideo)
            {
                Status(SwimLang.T("Άνοιξε πρώτα ένα βίντεο αγώνα σε οθόνη αναπαραγωγής."), true);
                return;
            }

            if (Race.IsTraining)
            {
                Status(SwimLang.T("Ο αυτόματος πιλότος είναι για βίντεο αγώνα (το «Είδος βίντεο» στα Στοιχεία)."), true);
                return;
            }

            if (!PoolHasMarks)
            {
                Status(SwimLang.T("✈ Αυτόματος πιλότος: πισίνα 50 ή 25 (με τα σημάδια των σχοινιών)."), true);
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
                Status(SwimLang.T("Δεν είναι γνωστός ο ρυθμός καρέ του βίντεο: ο αυτόματος πιλότος δεν μπορεί να ξεκινήσει."), true);
                return;
            }

            StopPilot();
            EndReview(false);
            if (PickTarget != SwimPickTarget.None)
                CancelPick();
            pilotRace = Race.Id;
            pilotStep = PilotStep.None;
            pilotSwimmerAuto = false;
            pilotRefound.Clear();
            pilotRefoundPending = -1;
            pilotStartNote = Race.StartTimestamp.HasValue ? SwimLang.T("ήταν ήδη βαλμένη") : null;
            RefreshPilotUi();

            if (!Race.StartTimestamp.HasValue)
            {
                Status(SwimLang.T("✈ Αυτόματος πιλότος: βρίσκει την εκκίνηση…"), false);
                FindStartLight();
                if (!PilotActive)
                    return;

                if (startLightCancelled)
                {
                    StopPilot();
                    Status(SwimLang.T("✈ Ο αυτόματος πιλότος σταμάτησε: η αναζήτηση της εκκίνησης δεν ολοκληρώθηκε."), true);
                    return;
                }

                if (!Race.StartTimestamp.HasValue)
                {
                    SwimProposal best = Race.Proposals.Where(p => p.Kind == KindRaceStart).OrderBy(p => p.Index).FirstOrDefault();
                    if (best == null)
                    {
                        StopPilot();
                        Status(SwimLang.T("✈ Ο πιλότος σταμάτησε: δεν βρέθηκε το σήμα εκκίνησης (δεν φαίνεται φως ούτε ακούγεται το σήμα). Βάλε την εκκίνηση ") +
                            SwimLang.T("με το κουμπί «Εκκίνηση» στο καρέ του σήματος και πάτα ξανά «✈ Αυτόματος πιλότος»."), true);
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
                    // (A start put meanwhile by a key of a mark is Kinovea's zero, not the signal: the check again.)
                    SwimProposal start = Race.Proposals.Where(p => p.Kind == KindRaceStart).OrderBy(p => p.Index).FirstOrDefault();
                    if (start == null)
                    {
                        StopPilot();
                        Status(SwimLang.T("✈ Ο πιλότος σταμάτησε: βάλε την εκκίνηση με το κουμπί «Εκκίνηση» (G) στο καρέ του σήματος και πάτα ξανά ") +
                            SwimLang.T("«✈ Αυτόματος πιλότος»."), true);
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
                        // (Another click replaced it: cancelled without stopping the pilot, then armed again.)
                        pilotStep = PilotStep.None;
                        StartWholeRace();
                        if (!PilotActive)
                            return;
                        if (!PickingTrack)
                        {
                            StopPilot();
                            return;
                        }

                        pilotStep = PilotStep.Lost;
                    }

                    PilotPrompt();
                    return;
            }
        }

        /// <summary>A start proposal the pilot takes without asking: sure enough, and with video enough after it for the race.</summary>
        private bool PilotTrustsStart(SwimProposal best)
        {
            if (best.Confidence < PilotStartConfidence || best.Cues.ContainsKey("dive") || best.Cues.ContainsKey("line"))
                return false;

            double left = Timing.Seconds(frameServer.VideoReader.WorkingZone.End, best.Timestamp);
            return Race.Distance <= 0 || left >= MinRaceSeconds();
        }

        /// <summary>The start proposed becomes the start of the race; the proposal waits for the check (the answer teaches it).</summary>
        private void PilotSetStart(SwimProposal best)
        {
            Race.Proposals.RemoveAll(x => x.Kind == KindRaceStart && x.Id != best.Id);
            Race.PilotStart = true;
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
            string from = p.Cues.ContainsKey("dive") ? SwimLang.T("λίγο πριν τη βουτιά (δεν φάνηκε φως ούτε ακούστηκε σήμα)") :
                p.Cues.ContainsKey("line") ? SwimLang.T("λίγο πριν φύγουν όλοι μαζί από τους βατήρες (κανένα σήμα δεν ταίριαζε με τον αγώνα)") :
                p.Cues.ContainsKey("light") ? (p.Cues.ContainsKey("sound") ? SwimLang.T("από το φως και τον ήχο του σήματος") : SwimLang.T("από το φως του σήματος")) :
                p.Cues.ContainsKey("sound") ? SwimLang.T("από τον ήχο του σήματος") : SwimLang.T("από το βίντεο");
            return string.Format(CultureInfo.InvariantCulture, "{0} ({1:0}%)", from, 100 * p.Confidence);
        }

        /// <summary>"1 χεριά", "65 χεριές".</summary>
        private static string CountOf(int n, string one, string many)
        {
            return n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);
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

            // The search of the swimmer cancelled (its progress window): the pilot stops, as with the other readings.
            if (autoPickCancelled)
            {
                CancelPick();
                StopPilot();
                Status(SwimLang.T("✈ Ο αυτόματος πιλότος σταμάτησε (ακύρωση). Ό,τι έβαλε μένει και περιμένει τον «Έλεγχο»."), false);
                return;
            }

            pilotStep = PilotStep.Swimmer;
            AutoPickResult pick = autoPick;
            if (pick != null && PilotTrustsPick(pick))
                StartPilotCountdown();

            PilotPrompt();
        }

        /// <summary>The swimmer proposed is taken after PilotPickSeconds without an answer.</summary>
        private void StartPilotCountdown()
        {
            pilotCountdown = PilotPickSeconds;
            if (pilotTimer == null)
            {
                pilotTimer = new Timer { Interval = 1000 };
                pilotTimer.Tick += PilotTimer_Tick;
            }

            pilotTimer.Start();
        }

        /// <summary>
        /// The swimmer proposed may be taken without an answer: a file of a camcorder (AVCHD .mts/.m2ts), all the probes found his
        /// lane and he is seen near (his length as the delays of the checkpoints count it). Lab, 97 races: 17 camcorder files so,
        /// 15 of them read as well as from the analyst's click, 2 with checkpoints about 0.5 s off (the swimmer not in the middle);
        /// the 2 phones of the stands so had another swimmer in the middle. Anything else: the analyst is asked.
        /// </summary>
        private bool PilotTrustsPick(AutoPickResult p)
        {
            // The swimmer of a lane of a view of the whole pool: when the numbering of that pool was confirmed in this folder and he
            // stands out of the motion of his lane.
            if (p.LaneNumber > 0)
                return p.LaneLearned && p.Clear >= LanePickClear;

            string ext = string.IsNullOrEmpty(videoPath) ? "" : System.IO.Path.GetExtension(videoPath).ToLowerInvariant();
            if ((ext != ".mts" && ext != ".m2ts") || p.Agree < AutoPickProbes.Length)
                return false;

            Size reference = ReadingReader.Info.ReferenceSize;
            double scale = (double)StrokeWorkWidth / Math.Max(1, reference.Width);
            double dx = p.Head.X - p.Feet.X, dy = p.Head.Y - p.Feet.Y;
            return SwimSplitDetector.FarShare(scale * Math.Sqrt(dx * dx + dy * dy)) <= 0;
        }

        private void PilotTimer_Tick(object sender, EventArgs e)
        {
            bool waiting = pilotStep == PilotStep.Swimmer || pilotStep == PilotStep.Lost;
            if (!PilotActive || !waiting || autoPick == null || !pickForRace || !PickingTrack || player == null || player.IsDisposed)
            {
                StopPilotCountdown();
                if (PilotActive && waiting)
                    PilotPrompt();
                return;
            }

            // Not while another reading or a window waits (a progress, a dialog, a message): the countdown waits with them.
            if (StartLightRunning || AutoStrokesRunning || SkeletonRunning || ModalOpen())
                return;

            pilotCountdown--;
            if (pilotCountdown > 0)
            {
                PilotPrompt();
                return;
            }

            StopPilotCountdown();

            // Taken without an answer: the swimmer of the start, or the swimmer again after a wall (in the report once the reading from
            // there has read: PilotAfterReading).
            bool lost = pilotStep == PilotStep.Lost;
            if (lost)
                pilotRefoundPending = pilotLostWall;
            else
                pilotSwimmerAuto = true;
            if (!AcceptAutoPick(player))
            {
                pilotRefoundPending = -1;
                if (!lost)
                    pilotSwimmerAuto = false;
                PilotPrompt();
            }
        }

        private void StopPilotCountdown()
        {
            pilotCountdown = 0;
            if (pilotTimer != null)
                pilotTimer.Stop();
        }

        /// <summary>A modal window of the program is open: the progress of a reading, a dialog, a message (its owner is disabled).</summary>
        private bool ModalOpen()
        {
            foreach (Form f in Application.OpenForms)
            {
                if (f.Modal)
                    return true;
            }

            Form owner = player != null && !player.IsDisposed ? player.FindForm() : null;
            if (owner != null && owner.IsHandleCreated && !IsWindowEnabled(owner.Handle))
                return true;

            // A message of the panel (shown with the panel as its owner) disables the panel only.
            return form != null && !form.IsDisposed && form.IsHandleCreated && !IsWindowEnabled(form.Handle);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        /// <summary>What the pilot waits for, on the status line.</summary>
        private void PilotPrompt()
        {
            if (!PilotActive)
                return;

            // The head was clicked: the feet now.
            if ((pilotStep == PilotStep.Swimmer || pilotStep == PilotStep.Lost) && PickTarget == SwimPickTarget.StrokeFeet)
            {
                Status(SwimLang.T("✈ Τώρα κλικ στα πόδια του αθλητή, στο ίδιο καρέ: μετά ο πιλότος συνεχίζει μόνος του ως το PDF (Esc: σταμάτημα)."), false);
                RefreshPilotUi();
                return;
            }

            switch (pilotStep)
            {
                case PilotStep.Start:
                    SwimProposal s = CurrentProposal;
                    Status(SwimLang.T("✈ Η εκκίνηση που βρέθηκε δεν είναι σίγουρη") +
                        (s != null && s.Kind == KindRaceStart ? string.Format(CultureInfo.InvariantCulture, " ({0:0}%)", 100 * s.Confidence) : "") +
                        SwimLang.T(": δες την στο βίντεο. Enter αν είναι σωστή (←/→ και Enter: διόρθωση · Delete: η επόμενη υποψήφια) και ο πιλότος ") +
                        SwimLang.T("συνεχίζει μόνος του ως το PDF · Esc: σταμάτημα."), true);
                    break;
                case PilotStep.Swimmer:
                    if (autoPick != null && pilotCountdown > 0)
                        Status(string.Format(CultureInfo.InvariantCulture,
                            SwimLang.T("✈ Σε {0} s ο πιλότος συνεχίζει με {1} (σημειωμένος στο βίντεο) · Enter: τώρα · ") +
                            SwimLang.T("↑/↓: άλλη διαδρομή · κλικ στο κεφάλι και στα πόδια: άλλος αθλητής · Esc: σταμάτημα."), pilotCountdown, PickWho(autoPick)), false);
                    else if (autoPick != null)
                        Status(SwimLang.T("✈ Ο πιλότος περιμένει: είναι αυτός ο αθλητής (σημειωμένος στο βίντεο); Enter: ναι · ↑/↓: άλλη διαδρομή · ") +
                            SwimLang.T("ή κλικ στο κεφάλι και μετά στα πόδια του · Esc: σταμάτημα. Μετά συνεχίζει μόνος του ως το PDF."), true);
                    else
                        Status(SwimLang.T("✈ Ο πιλότος περιμένει τον αθλητή: σε ένα καρέ μετά την έξοδο από το νερό, κλικ στο κεφάλι του και μετά ") +
                            SwimLang.T("στα πόδια του · Esc: σταμάτημα. Μετά συνεχίζει μόνος του ως το PDF."), true);
                    break;
                case PilotStep.Lost:
                    if (autoPick != null && pilotCountdown > 0)
                        Status(string.Format(CultureInfo.InvariantCulture,
                            SwimLang.T("✈ Μετά τον τοίχο των {0}{1} ο αθλητής χάθηκε: σε {2} s ο πιλότος συνεχίζει με {3} ") +
                            SwimLang.T("(σημειωμένος στο βίντεο) · Enter: τώρα · ↑/↓: άλλη διαδρομή · κλικ στο κεφάλι και στα πόδια: άλλος αθλητής · Esc: σταμάτημα."),
                            pilotLostWall, Race.Unit, pilotCountdown, PickWho(autoPick)), false);
                    else if (autoPick != null)
                        Status(string.Format(CultureInfo.InvariantCulture,
                            SwimLang.T("✈ Μετά τον τοίχο των {0}{1} ο αθλητής χάθηκε: είναι αυτός (σημειωμένος στο βίντεο); Enter: ναι · ↑/↓: άλλη διαδρομή · ") +
                            SwimLang.T("ή κλικ στο κεφάλι και μετά στα πόδια του · ο πιλότος συνεχίζει μόνος του ως το PDF (Esc: σταμάτημα)."), pilotLostWall, Race.Unit), true);
                    else
                        Status(string.Format(CultureInfo.InvariantCulture,
                            SwimLang.T("✈ Μετά τον τοίχο των {0}{1} ο αθλητής χάθηκε: σε ένα καρέ μετά την έξοδο από το νερό, κλικ στο κεφάλι του και ") +
                            SwimLang.T("μετά στα πόδια του · ο πιλότος συνεχίζει μόνος του ως το PDF (Esc: σταμάτημα)."), pilotLostWall, Race.Unit), true);
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
                SeekNearest(end.Timestamp);
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

            // A proposal at an official time: the mark knows it (and where the reading itself put it), to go back with it when that
            // official time goes.
            p.Pilot = true;
            if (existing != null && SameFrame(existing.Timestamp, p.Timestamp))
            {
                p.PilotMark = existing.Id;
                if (p.Official && existing.Pilot != null && !existing.Pilot.Official)
                {
                    existing.Pilot.Official = true;
                    existing.Pilot.Read = p.Read != null ? p.Read.Timestamp : existing.Timestamp;
                }

                return false;
            }

            SwimMark mark = new SwimMark
            {
                Type = SwimMarkType.Split,
                Timestamp = p.Timestamp,
                Distance = distance,
                Pilot = new SwimPilotMark { Wall = distance, Official = p.Official, Read = p.Official && p.Read != null ? (long?)p.Read.Timestamp : null }
            };
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
                // The length was read again: its new automatic strokes before the breakout go as with Enter (the ones kept
                // before were of the old reading).
                p.PilotMark = old.Id;
                List<SwimMark> again = TrimAutoStrokesBeforeBreakout(p.Index, p.Timestamp);
                old.Pilot.Strokes = again.Count > 0 ? again : null;
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

        /// <summary>
        /// The mark the pilot put for the event of a proposal, not checked yet: the mark of the pilot's proposal, or the pilot's mark of
        /// the same event (the checkpoint of its distance, the breakout after its wall) for a proposal of a later reading. Null if
        /// none (or the analyst made it his).
        /// </summary>
        private SwimMark PilotMarkOf(SwimProposal p)
        {
            if (p == null || Race == null)
                return null;

            if (p.Pilot && !string.IsNullOrEmpty(p.PilotMark))
            {
                SwimMark own = Race.Marks.FirstOrDefault(m => m.Id == p.PilotMark && m.Pilot != null);
                if (own != null)
                    return own;
            }

            if (p.Kind == KindRaceSplit)
            {
                SwimMark split = Race.GetSplit(p.Index);
                return split != null && split.Pilot != null ? split : null;
            }

            if (p.Kind == KindRaceBreakout)
                return Race.Marks.FirstOrDefault(m => m.Type == SwimMarkType.Breakout && m.Pilot != null && m.Pilot.Wall == p.Index);

            return null;
        }

        /// <summary>
        /// Two breakouts closer than this (s) are the same one (the breakouts of two lengths are many seconds apart): the skeleton
        /// does not propose a breakout there, the pilot's own proposal is the one to answer.
        /// </summary>
        private const double PilotSameBreakout = 4.0;

        /// <summary>A breakout of the pilot, not checked yet, near this frame (the same breakout).</summary>
        private bool PilotBreakoutNear(long timestamp)
        {
            return Race != null && Timing != null && Timing.IsValid &&
                Race.Marks.Any(m => m.Type == SwimMarkType.Breakout && m.Pilot != null && Math.Abs(Timing.Seconds(m.Timestamp, timestamp)) <= PilotSameBreakout);
        }

        /// <summary>A proposal the pilot put as a mark, not checked yet: it stays at the frame of its mark (not moved by the answers).</summary>
        private bool IsPilotPut(SwimProposal p)
        {
            return p.Pilot && PilotMarkOf(p) != null;
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

            // The final frame of the report was of this finish (with its time): it goes with it.
            if (pilot.Type == SwimMarkType.Split && pilot.Distance == Race.Distance && Race.FinalFrame != null && SameFrame(Race.FinalFrame.Timestamp, pilot.Timestamp))
                Race.FinalFrame = null;
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
                // Only while the length has the strokes of that reading (read again, its strokes are the new ones).
                HashSet<string> batches = new HashSet<string>(Race.Marks.Where(m => m.Type == SwimMarkType.Stroke && m.Batch != null).Select(m => m.Batch));
                foreach (SwimMark s in pilot.Strokes)
                {
                    if (s.Batch != null && batches.Contains(s.Batch) && !Race.Marks.Any(m => m.Type == SwimMarkType.Stroke && SameFrame(m.Timestamp, s.Timestamp)))
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
            int refound = pilotRefoundPending;
            pilotRefoundPending = -1;
            if (!PilotActive)
                return;

            if (refound >= 0 && read > 0)
                pilotRefound.Add(refound);

            if (cancelled)
            {
                StopPilot();
                Status(string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("✈ Ο αυτόματος πιλότος σταμάτησε (ακύρωση) μετά από {0}: όσα βρέθηκαν μπήκαν και περιμένουν τον «Έλεγχο»."),
                    CountOf(read, SwimLang.T("μήκος"), SwimLang.T("μήκη"))), true);
                return;
            }

            if (read == 0 && pilotWasLost && Race.StartTimestamp.HasValue)
            {
                // The click after the lost swimmer read nothing (another frame, another swimmer): the click again.
                pilotStep = PilotStep.None;
                StartWholeRace();
                if (!PilotActive)
                    return;
                if (!PickingTrack)
                {
                    StopPilot();
                    return;
                }

                pilotStep = PilotStep.Lost;
                if (refound >= 0)
                {
                    // The swimmer the pilot took by itself read nothing, and nobody may be watching: a message waits for the analyst.
                    RefreshPilotUi();
                    PilotPrompt();
                    MessageBox.Show(DialogOwner, string.Format(CultureInfo.InvariantCulture,
                        SwimLang.T("Μετά τον τοίχο των {0}{1} ο αθλητής χάθηκε. Ο πιλότος τον βρήκε μόνος του, αλλά από εκεί δεν διαβάστηκε κανένα μήκος.\n\n") +
                        SwimLang.T("Σε ένα καρέ μετά την έξοδο από το νερό κάνε κλικ στο κεφάλι του αθλητή και μετά στα πόδια του: ο πιλότος συνεχίζει μόνος ") +
                        SwimLang.T("του ως τον τερματισμό και το PDF (Esc: σταμάτημα)."), refound, Race.Unit),
                        SwimLang.T("✈ Ο αυτόματος πιλότος χρειάζεται εσένα"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Status(SwimLang.T("✈ Από αυτό το κλικ δεν διαβάστηκε κανένα μήκος: κλικ ξανά στο κεφάλι και μετά στα πόδια του αθλητή, σε ένα καρέ μετά ") +
                    SwimLang.T("την έξοδο από το νερό (Esc: σταμάτημα· ό,τι μπήκε μένει)."), true);
                RefreshPilotUi();
                return;
            }

            if (resumeWall > 0 && Race.StartTimestamp.HasValue)
            {
                // The swimmer lost after a wall (the camera turned fast): the next click goes on from there.
                pilotStep = PilotStep.None;
                pilotLostWall = resumeWall - Race.Pool;
                long at = resumeAt;
                StartWholeRace();
                if (!PilotActive)
                    return;
                if (!PickingTrack)
                {
                    StopPilot();
                    return;
                }

                pilotStep = PilotStep.Lost;

                if (at != long.MinValue)
                    SeekTo(Math.Min(frameServer.VideoReader.WorkingZone.End, at + RealSpan(3.0)));

                // Since 2.7.2: the swimmer the camera follows proposed there, as at the start, and taken after the countdown when the
                // pilot trusts the proposal (lab, the 3 races of the analysts lost after a wall: 1 taken, read on with 13 of its 15
                // checkpoints within 0.5 s; the 2 others, where a proposal would have been another swimmer, still asked).
                long? here = CurrentVideoTimestamp;
                if (here.HasValue)
                    ProposeAutoPick(here.Value);
                if (!PilotActive)
                    return;
                if (autoPickCancelled)
                {
                    CancelPick();
                    StopPilot();
                    Status(SwimLang.T("✈ Ο αυτόματος πιλότος σταμάτησε (ακύρωση). Ό,τι έβαλε μένει και περιμένει τον «Έλεγχο»."), false);
                    return;
                }

                if (!pickForRace || !PickingTrack)
                {
                    StopPilot();
                    return;
                }

                if (autoPick != null && PilotTrustsPick(autoPick))
                {
                    StartPilotCountdown();
                    PilotPrompt();
                    return;
                }

                PilotPrompt();
                MessageBox.Show(DialogOwner, string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("Μετά τον τοίχο των {0}{1} ο αθλητής χάθηκε (η κάμερα γύρισε γρήγορα). Όσα μήκη διαβάστηκαν μπήκαν ήδη.\n\n{2}") +
                    SwimLang.T("ο πιλότος συνεχίζει μόνος του ως τον τερματισμό και το PDF (Esc: σταμάτημα)."), pilotLostWall, Race.Unit,
                    autoPick != null ? SwimLang.T("Ο πιλότος σημείωσε ") + PickWho(autoPick) + SwimLang.T(": Enter αν είναι αυτός (↑/↓: άλλη διαδρομή), αλλιώς σε ένα ") +
                        SwimLang.T("καρέ μετά την έξοδο από το νερό κλικ στο κεφάλι του αθλητή και μετά στα πόδια του: ") :
                        SwimLang.T("Σε ένα καρέ μετά την έξοδο από το νερό κάνε κλικ στο κεφάλι του αθλητή και μετά στα πόδια του: ")),
                    SwimLang.T("✈ Ο αυτόματος πιλότος χρειάζεται εσένα"), MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            string title = SwimLang.T("✈ Αυτόματος πιλότος");

            // The video at the start, to watch the race with its graphics.
            if (Race.StartTimestamp.HasValue)
                SeekTo(Math.Max(frameServer.VideoReader.WorkingZone.Start, Race.StartTimestamp.Value - RealSpan(1.0)));

            if (raceSplits == 0)
            {
                string why = SwimLang.T("✈ Ο αυτόματος πιλότος δεν μπόρεσε να διαβάσει τον αγώνα: δεν βρέθηκαν τα σημάδια των σχοινιών στη διαδρομή του ") +
                    SwimLang.T("αθλητή (π.χ. πλάνο χωρίς σχοινιά, ή ο αθλητής δεν ακολουθήθηκε). Δοκίμασε «🤖 Όλος ο αγώνας» με κλικ στον αθλητή σε άλλο ") +
                    SwimLang.T("καρέ, ή την ανάλυση με το χέρι.");
                Status(why, true);
                MessageBox.Show(DialogOwner, why, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // The PDF, once the final frame is on the Drive: it opens by itself.
            bool pdf = finish != null && Settings.IsConfigured;
            if (pdf)
                PilotPdf();

            StringBuilder b = new StringBuilder();
            b.Append(SwimLang.T("Ο αγώνας αναλύθηκε από τον αυτόματο πιλότο.\n\n"));
            if (!string.IsNullOrEmpty(pilotStartNote))
                b.Append(SwimLang.T("• Εκκίνηση: ")).Append(pilotStartNote).Append(".\n");
            b.Append(pilotSwimmerAuto ? SwimLang.T("• Αθλητής: αυτός που ακολουθεί η κάμερα, χωρίς ερώτηση (δες ότι είναι ο σωστός).\n") :
                SwimLang.T("• Αθλητής: αυτός που επέλεξες.\n"));
            foreach (int wall in pilotRefound)
                b.AppendFormat(CultureInfo.InvariantCulture, SwimLang.T("• Μετά τον τοίχο των {0}{1} ο αθλητής χάθηκε και ο πιλότος τον βρήκε ξανά μόνος του ") +
                    SwimLang.T("(δες ότι είναι ο ίδιος από εκεί και μετά).\n"), wall, Race.Unit);
            b.Append(SwimLang.T("• Μπήκαν ")).Append(CountOf(splits, SwimLang.T("πέρασμα ή τοίχος"), SwimLang.T("περάσματα και τοίχοι"))).Append(", ")
                .Append(CountOf(breakouts, SwimLang.T("έξοδος από το νερό"), SwimLang.T("έξοδοι από το νερό"))).Append(SwimLang.T(" και ")).Append(CountOf(strokes, SwimLang.T("χεριά"), SwimLang.T("χεριές"))).Append(".\n");
            if (raceSplits - splits == 1)
                b.Append(SwimLang.T("• 1 πέρασμα ή τοίχος ήταν ήδη δικό σου: έμεινε όπως ήταν.\n"));
            else if (raceSplits > splits)
                b.AppendFormat(CultureInfo.InvariantCulture, SwimLang.T("• {0} περάσματα και τοίχοι ήταν ήδη δικά σου: έμειναν όπως ήταν.\n"), raceSplits - splits);
            // The final time of the analysis: the official one when it stands at the finish (since 2026-10-19).
            Recompute();
            SwimRow finalRow = Computed == null ? null : Computed.Rows.FirstOrDefault(r => r.D == Race.Distance && r.T.HasValue);
            if (finish != null && Race.StartTimestamp.HasValue)
                b.Append(SwimLang.T("• Τερματισμός: ")).Append(SwimReference.FormatTime(finalRow != null && finalRow.Marked ? finalRow.T.Value :
                    Timing.Seconds(finish.Timestamp, Race.StartTimestamp.Value))).Append(finalRow != null && finalRow.Official ? SwimLang.T(" (επίσημος χρόνος)") : "").Append(".\n");
            else
                b.Append(SwimLang.T("• ⚠ Δεν βρέθηκε ο τερματισμός: μάρκαρέ τον στο καρέ της επαφής και πάτα «PDF».\n"));
            string official = OfficialNote(put);
            if (official.Length > 0)
                b.Append("•").Append(official).Append('\n');
            int paceOnly = put.Count(IsDoubtfulPace);
            if (doubtful > 0)
                b.Append("• ⚠ ").Append(CountOf(doubtful, SwimLang.T("αβέβαιο"), SwimLang.T("αβέβαια"))).Append(SwimLang.T(" (κάτω από 50%, πορτοκαλί στον έλεγχο): δες "))
                    .Append(doubtful == 1 ? SwimLang.T("αυτό") : SwimLang.T("αυτά")).Append(SwimLang.T(" πρώτα.\n"));
            if (paceOnly > 0)
                b.Append("• ⚠ ").Append(doubtful > paceOnly ? SwimLang.T("Από αυτά, ") : "").Append(PaceOnlyText(paceOnly)).Append(".\n");
            if (waiting > 0)
                b.AppendFormat(CultureInfo.InvariantCulture,
                    SwimLang.T("• {0} τον έλεγχό σου: όσα η κάμερα δεν δείχνει καθαρά (πρώτη κίνηση, αποδέσμευση, είσοδος, αποκόλληση ") +
                    SwimLang.T("από τους τοίχους) προτείνονται στους συνήθεις χρόνους και μπαίνουν στην ανάλυση μόνο αν τα επιβεβαιώσεις.\n"),
                    waiting == 1 ? SwimLang.T("1 σημείο περιμένει") : CountOf(waiting, SwimLang.T("σημείο"), SwimLang.T("σημεία")) + SwimLang.T(" περιμένουν"));
            if (pdf)
                b.Append(SwimLang.T("• PDF: ετοιμάζεται και ανοίγει μόνο του.\n"));
            else if (finish != null)
                b.Append(SwimLang.T("• PDF: δεν έχει ρυθμιστεί η σύνδεση με το KOE App (καρτέλα «Σύνδεση»)· μετά πάτα «PDF».\n"));
            b.Append(SwimLang.T("\nΌ,τι έβαλε ο πιλότος δεν διδάσκει το πρόγραμμα μέχρι να το ελέγξεις: στον «Έλεγχο» Enter = σωστό, ←/→ και Enter = ") +
                SwimLang.T("διόρθωση, Delete = λάθος. Από αυτές τις απαντήσεις μαθαίνει.\n\n"));
            b.Append(doubtful > 0 ? SwimLang.T("Να ξεκινήσει τώρα ο έλεγχος, από τα αβέβαια;") : SwimLang.T("Να ξεκινήσει τώρα ο έλεγχος;"));

            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("✈ Ο πιλότος τελείωσε: {0}, {1}, {2}{3}{4} · «✨ Έλεγχος» ") +
                SwimLang.T("για να τα δεις (μαθαίνει από τις απαντήσεις σου)."), CountOf(splits, SwimLang.T("πέρασμα ή τοίχος"), SwimLang.T("περάσματα και τοίχοι")),
                CountOf(breakouts, SwimLang.T("έξοδος"), SwimLang.T("έξοδοι")), CountOf(strokes, SwimLang.T("χεριά"), SwimLang.T("χεριές")),
                doubtful > 0 ? " · ⚠ " + CountOf(doubtful, SwimLang.T("αβέβαιο"), SwimLang.T("αβέβαια")) : "",
                pdf ? SwimLang.T(" · το PDF ανοίγει μόλις είναι έτοιμο") : ""), doubtful > 0 || finish == null);
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
