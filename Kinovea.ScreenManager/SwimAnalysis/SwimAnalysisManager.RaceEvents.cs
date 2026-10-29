#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The events of the whole race that a camera in the stands does not show clearly enough for a detector: the first movement,
    /// the feet off the block and the entry of the hands after the start, and the feet off the wall after each turn. They are
    /// proposed at their usual times (medians of the analyses of the library, 372 races) moved by the learning (the usual
    /// correction of the analyst; in the same video at once), and each answer moves the next one: the feet off the block from the
    /// first movement, the entry from the feet off, the push off from the wall kept.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Start and turn events
        /// <summary>Proposal of the first movement after the start signal.</summary>
        public const string KindRaceReaction = "race.reaction";

        /// <summary>Proposal of the feet off the block (backstroke: off the wall) after the start.</summary>
        public const string KindRaceBlock = "race.block";

        /// <summary>Proposal of the entry of the hands in the water after the start.</summary>
        public const string KindRaceEntry = "race.entry";

        /// <summary>Proposal of the feet off the wall after a turn (Index: the wall).</summary>
        public const string KindRacePushoff = "race.pushoff";

        /// <summary>The proposals of the whole race (a new reading replaces the waiting ones of the same kind and index).</summary>
        private static bool IsWholeRaceKind(string kind)
        {
            return kind == KindRaceSplit || kind == KindRaceBreakout || kind == KindRaceReaction || kind == KindRaceBlock || kind == KindRaceEntry ||
                kind == KindRacePushoff;
        }

        // Origins of the cues: the start signal, the first movement kept, the feet off kept, the wall.
        private const string CueFromStart = "start";
        private const string CueFromReaction = "react";
        private const string CueFromBlock = "block";
        private const string CueFromWall = "wall";

        /// <summary>The marks of the start are searched this long after the signal (s).</summary>
        private const double StartEventsSeconds = 2.5;

        /// <summary>
        /// Usual time (s) from the origin of a cue to the event and its spread, before any learning: medians of the analyses of the
        /// library (backstroke starts in the water). The push off: about 0.27 s after the feet touch in a flip turn, about a second
        /// after the hands touch in an open turn. False if the cue has no usual time for this event.
        /// </summary>
        private static bool EventPrior(string kind, string cue, string style, string wallKind, out double delay, out double spread)
        {
            bool back = style == "bk";
            delay = double.NaN;
            spread = 0.05;
            switch (kind)
            {
                case KindRaceReaction:
                    if (cue == CueFromStart)
                    {
                        delay = back ? 0.20 : 0.25;
                        spread = 0.03;
                    }
                    break;
                case KindRaceBlock:
                    if (cue == CueFromStart)
                    {
                        delay = back ? 0.58 : 0.67;
                        spread = 0.045;
                    }
                    else if (cue == CueFromReaction)
                    {
                        delay = back ? 0.37 : 0.42;
                        spread = 0.035;
                    }
                    break;
                case KindRaceEntry:
                    if (cue == CueFromStart)
                    {
                        delay = back ? 0.67 : style == "br" ? 0.985 : style == "fly" ? 0.96 : 0.97;
                        spread = 0.06;
                    }
                    else if (cue == CueFromBlock)
                    {
                        delay = back ? 0.09 : 0.30;
                        spread = 0.04;
                    }
                    break;
                case KindRacePushoff:
                    if (cue == CueFromWall)
                    {
                        if (wallKind == "flip")
                        {
                            delay = 0.27;
                            spread = 0.08;
                        }
                        else
                        {
                            delay = style == "fly" ? 1.02 : style == "br" ? 1.09 : 1.05;
                            spread = 0.12;
                        }
                    }
                    break;
            }

            return !double.IsNaN(delay);
        }

        /// <summary>Name of a cue for the learning: its origin, the kind of the wall ("wall.flip") and the style ("@fs").</summary>
        private static string EventCueName(string cue, string style, string wallKind)
        {
            return StyleCue(string.IsNullOrEmpty(wallKind) ? cue : cue + "." + wallKind, style);
        }

        /// <summary>Origin, kind of wall and style of a cue name made by EventCueName.</summary>
        private static void ParseEventCue(string name, out string cue, out string style, out string wallKind)
        {
            style = "";
            wallKind = "";
            cue = name;
            int at = cue.IndexOf('@');
            if (at > 0)
            {
                style = cue.Substring(at + 1);
                cue = cue.Substring(0, at);
            }

            int dot = cue.IndexOf('.');
            if (dot > 0)
            {
                wallKind = cue.Substring(dot + 1);
                cue = cue.Substring(0, dot);
            }
        }

        /// <summary>A proposal of these events from its cues (timestamps of the usual time from each origin) with the learning.</summary>
        private bool CombineEvent(SwimProposal p, double detectorConfidence)
        {
            if (p.Cues.Count == 0)
                return false;

            long origin = p.Cues.Values.Min();
            SwimDetection d = new SwimDetection(p.Kind, p.Type, p.Index) { Confidence = detectorConfidence };
            foreach (KeyValuePair<string, long> c in p.Cues)
            {
                string cue, style, wallKind;
                ParseEventCue(c.Key, out cue, out style, out wallKind);
                double delay, spread;
                if (!EventPrior(p.Kind, cue, style, wallKind, out delay, out spread))
                    spread = 0.1;
                d.Cue(c.Key, Timing.Seconds(c.Value, origin), 1.0, spread);
            }

            double confidence;
            double seconds = SwimProposalMath.Combine(d, Learning, SwimLearning.VideoCode(videoPath), 1.0, out confidence);
            if (double.IsNaN(seconds))
                return false;

            p.Timestamp = origin + RealSpan(seconds);
            p.Confidence = Math.Round(confidence, 2);
            return true;
        }

        /// <summary>Confidence of these proposals before the learning: their usual times only, the analyst checks them.</summary>
        private static double EventConfidence(string kind)
        {
            return kind == KindRacePushoff ? 0.35 : kind == KindRaceReaction ? 0.4 : 0.45;
        }

        /// <summary>
        /// A proposal of one of these events from one origin, or null when the analyst has marked it already (his frame then
        /// teaches the learning when it is near).
        /// </summary>
        private SwimProposal EventProposal(string kind, SwimMarkType type, int index, string cue, long originTimestamp, string style, string wallKind,
            long searchFrom, long searchTo, string batch)
        {
            SwimProposal p = PriorEventProposal(kind, type, index, cue, originTimestamp, style, wallKind, batch);
            if (p == null)
                return null;

            SwimMark marked = Race.Marks.Where(m => m.Type == type && m.Pilot == null && m.Timestamp >= searchFrom && m.Timestamp <= searchTo)
                .OrderBy(m => Math.Abs(m.Timestamp - p.Timestamp)).FirstOrDefault();
            if (marked != null)
            {
                if (Math.Abs(Timing.Seconds(marked.Timestamp, p.Timestamp)) <= 0.5)
                {
                    Race.Proposals.Add(p);
                    Answer(p, marked.Timestamp, true);
                    learnedFromMarks++;
                }

                return null;
            }

            return p;
        }

        /// <summary>A proposal of one of these events from one origin: its usual time moved by the learning (null if it has none).</summary>
        private SwimProposal PriorEventProposal(string kind, SwimMarkType type, int index, string cue, long originTimestamp, string style, string wallKind,
            string batch)
        {
            double delay, spread;
            if (!EventPrior(kind, cue, style, wallKind, out delay, out spread))
                return null;

            SwimProposal p = new SwimProposal { Type = type, Kind = kind, Index = index, Batch = batch };
            p.Cues[EventCueName(cue, style, wallKind)] = originTimestamp + RealSpan(delay);
            return CombineEvent(p, EventConfidence(kind)) ? p : null;
        }

        /// <summary>The start events not marked yet: first movement, feet off, entry (each from the start signal).</summary>
        private List<SwimProposal> StartEventProposals(string batch)
        {
            List<SwimProposal> made = new List<SwimProposal>();
            if (Race == null || Race.IsTraining || !Race.StartTimestamp.HasValue || !Timing.IsValid)
                return made;

            long start = Race.StartTimestamp.Value;
            long to = start + RealSpan(StartEventsSeconds);
            string style = SwimPresets.StyleAt(Race, Race.Pool);
            foreach (Tuple<string, SwimMarkType> e in new[]
            {
                Tuple.Create(KindRaceReaction, SwimMarkType.Reaction),
                Tuple.Create(KindRaceBlock, SwimMarkType.FeetOff),
                Tuple.Create(KindRaceEntry, SwimMarkType.Entry)
            })
            {
                SwimProposal p = EventProposal(e.Item1, e.Item2, 0, CueFromStart, start, style, "", start, to, batch);
                if (p != null)
                    made.Add(p);
            }

            // An event the analyst marked already moves the next ones (as his answer would).
            foreach (Tuple<string, SwimMarkType> e in new[] { Tuple.Create(KindRaceReaction, SwimMarkType.Reaction), Tuple.Create(KindRaceBlock, SwimMarkType.FeetOff) })
            {
                SwimMark marked = Race.Marks.Where(m => m.Type == e.Item2 && m.Pilot == null && m.Timestamp >= start && m.Timestamp <= to).OrderBy(m => m.Timestamp).FirstOrDefault();
                if (marked != null)
                    MoveEventProposals(e.Item1, 0, marked.Timestamp, made);
            }

            // The official reaction time of the results (since 2026-10-19): the release from the block at it, without cues (its answer
            // teaches what the usual times gave, Read), and the entry from it; not where the analyst marked the release himself.
            long? release = OfficialReleaseStamp();
            if (release.HasValue && !Race.Marks.Any(m => m.Type == SwimMarkType.FeetOff && m.Pilot == null && m.Timestamp >= start && m.Timestamp <= to))
            {
                SwimProposal block = made.FirstOrDefault(x => x.Kind == KindRaceBlock);
                if (block == null)
                {
                    block = new SwimProposal { Type = SwimMarkType.FeetOff, Kind = KindRaceBlock, Index = 0, Batch = batch };
                    int at = made.FindIndex(x => x.Kind == KindRaceEntry);
                    made.Insert(at >= 0 ? at : made.Count, block);
                }

                SetOfficialProposal(block, release.Value, SwimSplitDetector.OfficialConfidence, null);
                MoveEventProposals(KindRaceBlock, 0, release.Value, made);
            }

            return made;
        }

        /// <summary>(2.15.0) The waiting proposals of the start events moved by 'delta' (the start signal moved): their frames and cues.</summary>
        private void ShiftStartEventProposals(long delta)
        {
            if (Race == null || delta == 0)
                return;

            foreach (SwimProposal p in Race.Proposals.Where(x => x.Kind == KindRaceReaction || x.Kind == KindRaceBlock || x.Kind == KindRaceEntry))
            {
                p.Timestamp += delta;
                foreach (string key in p.Cues.Keys.ToList())
                    p.Cues[key] += delta;
                if (p.Read != null)
                {
                    p.Read.Timestamp += delta;
                    foreach (string key in p.Read.Cues.Keys.ToList())
                        p.Read.Cues[key] += delta;
                }
            }
        }

        /// <summary>
        /// The feet off the wall after the turn at 'wall' (not at the end of a leg of a relay: the next swimmer leaves the block),
        /// from the wall marked or proposed; null if there is no wall yet or the analyst marked it already.
        /// </summary>
        private SwimProposal PushoffProposal(int wall, IEnumerable<SwimProposal> proposed, string batch)
        {
            if (Race == null || !Race.StartTimestamp.HasValue || wall <= 0 || wall >= Race.Distance)
                return null;

            string kind = WallKind(wall);
            if (kind == "finish")
                return null;

            // A wall of the auto pilot not checked yet: the wall proposed now first (a new reading puts it in its place).
            SwimMark mark = Race.GetSplit(wall);
            SwimProposal p = proposed.FirstOrDefault(x => x.Kind == KindRaceSplit && x.Index == wall);
            long? wallTs = mark != null && (mark.Pilot == null || p == null) ? mark.Timestamp : p != null ? p.Timestamp : (long?)null;
            if (!wallTs.HasValue)
                return null;

            string style = SwimPresets.StyleAt(Race, wall);
            return EventProposal(KindRacePushoff, SwimMarkType.FeetOff, wall, CueFromWall, wallTs.Value, style, kind,
                wallTs.Value - RealSpan(0.2), wallTs.Value + RealSpan(2.5), batch);
        }

        /// <summary>
        /// An event kept (answer or mark) moves the event after it that waits: the feet off the block from the first movement, the
        /// entry from the feet off, the push off from the wall of its turn (the wall kept replaces the wall proposed).
        /// 'pending': where the proposal waits (the proposals of the race when null).
        /// </summary>
        private void MoveEventProposals(string kind, int index, long timestamp, List<SwimProposal> pending = null)
        {
            if (Race == null || !Timing.IsValid)
                return;

            string next = null, cue = null;
            int nextIndex = 0;
            if (kind == KindRaceReaction)
            {
                next = KindRaceBlock;
                cue = CueFromReaction;
            }
            else if (kind == KindRaceBlock)
            {
                next = KindRaceEntry;
                cue = CueFromBlock;
            }
            else if (kind == KindRaceSplit && index > 0 && index < Race.Distance && Race.Pool > 0 && index % Race.Pool == 0)
            {
                next = KindRacePushoff;
                cue = CueFromWall;
                nextIndex = index;
            }

            if (next == null)
                return;

            SwimProposal p = (pending ?? Race.Proposals).FirstOrDefault(x => x.Kind == next && x.Index == nextIndex);
            // (2.15.0) One at an official time stays there (the release at the official reaction time: the answer to the first movement
            // moved it to reaction + 0.42 s, and the flight came from it).
            if (p == null || p.Official)
                return;

            string style = next == KindRacePushoff ? SwimPresets.StyleAt(Race, nextIndex) : SwimPresets.StyleAt(Race, Race.Pool);
            string wallKind = next == KindRacePushoff ? WallKind(nextIndex) : "";
            double delay, spread;
            if (!EventPrior(next, cue, style, wallKind, out delay, out spread))
                return;

            // The event kept replaces what the proposal had from before it: the wall proposed (by the wall kept), the start signal
            // (its usual time to this event is the usual time to the event kept and from it: counted once).
            foreach (string old in p.Cues.Keys.Where(k => k.StartsWith(cue, StringComparison.Ordinal) || k.StartsWith(CueFromStart, StringComparison.Ordinal)).ToList())
                p.Cues.Remove(old);

            p.Cues[EventCueName(cue, style, wallKind)] = timestamp + RealSpan(delay);
            CombineEvent(p, EventConfidence(next));
        }

        /// <summary>
        /// After an answer the proposals of the same kind still waiting are made again from their cues with the learning now (in
        /// the same video the answers count more: the next push off or breakout needs fewer corrections).
        /// </summary>
        private void RecombineWaitingProposals(string kind)
        {
            if (Race == null || !Timing.IsValid)
                return;

            if (kind == KindRaceSplit)
            {
                RecombineSplitProposals();
                return;
            }

            bool events = kind == KindRaceReaction || kind == KindRaceBlock || kind == KindRaceEntry || kind == KindRacePushoff;
            if (!events && kind != KindRaceBreakout)
                return;

            string video = SwimLearning.VideoCode(videoPath);
            foreach (SwimProposal p in Race.Proposals.Where(x => x.Kind == kind && x.Cues.Count > 0).ToList())
            {
                // A breakout the auto pilot put stays at the frame of its mark (Enter there: right).
                if (IsPilotPut(p))
                    continue;

                if (events)
                {
                    CombineEvent(p, EventConfidence(kind));
                    continue;
                }

                long origin = p.Cues.Values.Min();
                SwimDetection d = new SwimDetection(kind, p.Type, p.Index) { Confidence = 1.0 };
                foreach (KeyValuePair<string, long> c in p.Cues)
                {
                    double weight, spread;
                    CueWeight(c.Key, out weight, out spread);
                    d.Cue(c.Key, Timing.Seconds(c.Value, origin), weight, spread);
                }

                double confidence;
                double seconds = SwimProposalMath.Combine(d, Learning, video, 1.0, out confidence);
                if (!double.IsNaN(seconds))
                    p.Timestamp = origin + RealSpan(seconds);
            }
        }
        #endregion
    }
}
