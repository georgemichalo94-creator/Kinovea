#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Kinovea.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.19.0) What AI pro asks Claude: the instructions, the text of the analysis (the event, the athlete without his name, the moments of
    /// the frames, Kinovea's measurements, the coach's list with what to correct and the targets, the coach's corrections of Claude's
    /// earlier answers) and the form of the answer (a tool with its schema: the answer is always JSON).
    /// </summary>
    public static class SwimAiPrompt
    {
        public const string TechTool = "swim_technique_evaluation";
        public const string RaceTool = "swim_race_report";

        private static string LanguageLine(string lang)
        {
            return lang == "en"
                ? "Write every text in English."
                : "Write every text in Greek: natural Greek as Greek swimming coaches write it, with the swimming terms of the checklist.";
        }

        public static string TechSystem(string lang)
        {
            return string.Join("\n", new string[]
            {
                "You are an elite swimming coach and biomechanics analyst who specialises in starts and turns. You work with the video analysts " +
                "of the Hellenic Swimming Federation (KOE). You evaluate one start or one turn of a swimmer from synchronised video frames, " +
                "against the coach's own checklist.",
                "",
                "How to work:",
                "1. Look at every frame carefully. The frames are numbered by moment. When there are several screens, every moment is shown by " +
                "each screen at the same instant (different cameras, e.g. a side view above the water and one underwater).",
                "2. In observations, write briefly what you see at each moment: the body line, the head, the arms and hands, the hips, the legs " +
                "and feet, the block or the wall, the entry, the streamline, the kicks.",
                "3. Then judge every point of the checklist (every key, once):",
                "   - done = true when the swimmer does what the point says; on a list of errors, when the error is NOT there.",
                "   - done = false when the swimmer does not do it; on a list of errors, when the error IS there.",
                "   - done = null when these frames do not show it (camera angle, out of frame, blur, splash, a moment that is not among the " +
                "frames). Never guess: a point you cannot see is null.",
                "   - sure: how sure you are, from 0.5 (it could go either way) to 1 (clearly visible).",
                "   - moment: the number of the moment you judged it at.",
                "   - why: one short sentence with what you saw, concrete and technical, as a coach writes it.",
                "4. Kinovea's measurements come from the coach's marks on the video and are more precise than a frame: use them as facts. A " +
                "point with a measurement and a target is judged by it, unless the frames clearly show that a mark is wrong; then say so in doubts.",
                "5. If the coach's corrections of your earlier answers are given, follow them: they say where this coach is stricter or more " +
                "lenient than you.",
                "6. summary: 3 to 5 sentences for the report: what is good, what to correct first, and how it compares with what is expected " +
                "for the athlete's gender, category, age and level.",
                "7. priorities: the 1 to 3 corrections that matter most, each with a short cue or drill the coach can use.",
                "8. doubts: marks or measurements that look wrong from the frames (e.g. the hand entry mark looks two frames late); else none.",
                "",
                "No name of any person is given, and you must not try to identify anyone.",
                LanguageLine(lang),
            });
        }

        public static string RaceSystem(string lang)
        {
            return string.Join("\n", new string[]
            {
                "You are an elite swimming coach and race analyst. You work with the video analysts of the Hellenic Swimming Federation (KOE) " +
                "and you write the comments of a race report from the analysis of the race (checkpoints and splits, velocity, stroke rate, " +
                "distance per stroke, the start, the turns, the finish, the comparison with the reference) and frames at key moments of the race.",
                "",
                "- Base every comment on the numbers given, and quote the ones that matter (e.g. 15 m in 6.52 s).",
                "- start, swim, turns, finish: 2 to 4 sentences each: what happened, what was good, where time was lost.",
                "- comparison: how the race compares with the reference given (where time was lost or gained); empty when there is no reference.",
                "- priorities: the 1 to 3 things to train first, concrete.",
                "- doubts: values or marks that look wrong (e.g. a split too fast for its stroke rate, a breakout mark that disagrees with its " +
                "frame); else none.",
                "- The coach's evaluations of the start and the turns, when given, are his: build on them.",
                "",
                "No name of any person is given, and you must not try to identify anyone.",
                LanguageLine(lang),
            });
        }

        /// <summary>The form of the answer of a technique evaluation.</summary>
        public static JObject TechToolSchema()
        {
            return JObject.Parse(@"{
  ""name"": ""swim_technique_evaluation"",
  ""description"": ""Your evaluation of the start or the turn against the coach's checklist."",
  ""input_schema"": {
    ""type"": ""object"",
    ""properties"": {
      ""observations"": {
        ""type"": ""array"",
        ""description"": ""Briefly, what you see at each moment (on every screen), before judging."",
        ""items"": {
          ""type"": ""object"",
          ""properties"": { ""moment"": { ""type"": ""integer"" }, ""seen"": { ""type"": ""string"" } },
          ""required"": [ ""moment"", ""seen"" ]
        }
      },
      ""points"": {
        ""type"": ""array"",
        ""description"": ""One entry for every key of the checklist."",
        ""items"": {
          ""type"": ""object"",
          ""properties"": {
            ""key"": { ""type"": ""string"" },
            ""done"": { ""type"": [ ""boolean"", ""null"" ], ""description"": ""true: done as the point says (a list of errors: the error is not there); false: not done (the error is there); null: the frames do not show it."" },
            ""sure"": { ""type"": ""number"", ""description"": ""From 0.5 (either way) to 1 (clearly visible)."" },
            ""moment"": { ""type"": [ ""integer"", ""null"" ] },
            ""why"": { ""type"": ""string"", ""description"": ""One short sentence: what you saw."" }
          },
          ""required"": [ ""key"", ""done"", ""sure"", ""why"" ]
        }
      },
      ""summary"": { ""type"": ""string"" },
      ""priorities"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } },
      ""doubts"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
    },
    ""required"": [ ""observations"", ""points"", ""summary"", ""priorities"" ]
  }
}");
        }

        /// <summary>The form of the answer of the comments of a race.</summary>
        public static JObject RaceToolSchema()
        {
            return JObject.Parse(@"{
  ""name"": ""swim_race_report"",
  ""description"": ""The comments of the race report."",
  ""input_schema"": {
    ""type"": ""object"",
    ""properties"": {
      ""start"": { ""type"": ""string"" },
      ""swim"": { ""type"": ""string"" },
      ""turns"": { ""type"": ""string"" },
      ""finish"": { ""type"": ""string"" },
      ""comparison"": { ""type"": ""string"" },
      ""priorities"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } },
      ""doubts"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
    },
    ""required"": [ ""start"", ""swim"", ""turns"", ""finish"", ""comparison"", ""priorities"" ]
  }
}");
        }

        #region The text of the analysis
        /// <summary>
        /// The text of a technique evaluation. Nothing that names a person: no athlete, club, competition, place, video file or reference
        /// name (the event, the athlete's gender and category and what the coach typed for AI pro only).
        /// </summary>
        public static string TechContext(SwimRace race, SwimTechPhase phase, SwimTechSheet sheet, SwimTechSheetCatalog cat,
            IDictionary<string, SwimTechMeasure> measures, SwimAiAthlete athlete, IList<SwimAiMoment> moments, int screens, int images,
            string lang, SwimAiCalibration calibration)
        {
            StringBuilder b = new StringBuilder();
            bool turn = phase.Kind == SwimPhaseKind.Turn;
            b.AppendLine("# The analysis");
            b.AppendLine("Technique evaluation of one " + (turn ? "turn" : "start") + " of " + (race.IsTraining ? "a training video" : "a race") + ".");
            b.AppendLine("Event: " + EventText(race));
            string title = lang == "en" ? phase.Title : (phase.TitleGreek ?? phase.Title);
            b.AppendLine("Phase: " + (title ?? "") + (turn ? (phase.Flip ? " (flip turn)" : " (open turn)") : ""));
            b.AppendLine();
            AthleteText(b, race, athlete);
            b.AppendLine();
            FramesText(b, moments, screens, turn ? "the wall contact (negative: before it)" : "the start signal");
            b.AppendLine();

            b.AppendLine("# Kinovea's measurements of this phase (from the coach's marks on the video)");
            if (PhaseValues(b, race, phase) == 0)
                b.AppendLine("- none (the phase has no marks yet)");
            b.AppendLine();

            b.AppendLine("# The coach's checklist: " + sheet.Title.In(lang));
            if (!sheet.Scored)
                b.AppendLine("This is a list of ERRORS: done = true means that the error is NOT there.");
            int count = 0;
            foreach (SwimTechSheetPhase g in sheet.Phases)
            {
                if (g.Items.Count == 0)
                    continue;
                string group = g.Label.In(lang);
                if (group.Length > 0)
                    b.AppendLine("## " + group);
                foreach (SwimTechSheetItem it in g.Items)
                {
                    count++;
                    b.Append("- key " + it.Key + ": ");
                    if (!string.IsNullOrEmpty(it.N))
                        b.Append("(" + it.N + ") ");
                    b.Append(OneLine(it.Criterion.Label.In(lang)));

                    string anchor = it.Criterion.Anchor;
                    if (!string.IsNullOrEmpty(anchor))
                    {
                        SwimTechText at;
                        string label = cat != null && cat.Anchors.TryGetValue(anchor, out at) ? at.In(lang) : anchor;
                        SwimAiMoment m = moments != null ? moments.FirstOrDefault(x => x.Anchors.Contains(anchor)) : null;
                        b.Append(" | judged at: " + label + (m != null ? " (moment " + m.N.ToString(CultureInfo.InvariantCulture) + ")" : " (not among the frames)"));
                    }

                    string cue = it.Criterion.Cue != null ? it.Criterion.Cue.In(lang) : "";
                    if (cue.Length > 0)
                        b.Append(" | correction: " + OneLine(cue));

                    SwimTechCheck check = cat != null ? cat.CheckOf(it.Criterion) : null;
                    SwimTechMeasure me;
                    if (check != null && measures != null && measures.TryGetValue(check.Id, out me) && me != null)
                    {
                        string target = Target(check);
                        b.Append(" | Kinovea measured: " + me.Text);
                        if (target.Length > 0)
                            b.Append(" (target " + target + (me.Good.HasValue ? (me.Good.Value ? ", in range" : ", out of range") : ", near the limit") + ")");
                    }

                    b.AppendLine();
                }
            }

            List<string> corrections = calibration != null ? calibration.Lines(sheet, lang) : new List<string>();
            if (corrections.Count > 0)
            {
                b.AppendLine();
                b.AppendLine("# This coach's corrections of your earlier answers (other videos, his final answers)");
                foreach (string line in corrections)
                    b.AppendLine(line);
            }

            b.AppendLine();
            b.AppendLine("# What to do");
            b.AppendLine("Look at the " + images.ToString(CultureInfo.InvariantCulture) + " frames that follow, then call " + TechTool + " once, with every key of the checklist (" +
                count.ToString(CultureInfo.InvariantCulture) + " points).");
            return b.ToString();
        }

        /// <summary>The text of the comments of a race: its numbers, the coach's evaluations of its start and turns, the moments of the frames.</summary>
        public static string RaceContext(SwimRace race, SwimComputed c, IList<SwimTechPhase> phases, SwimTechSheetCatalog cat, SwimAiAthlete athlete,
            IList<SwimAiMoment> moments, int screens, int images, string lang)
        {
            StringBuilder b = new StringBuilder();
            string unit = race.IsYards ? "yd" : "m";
            b.AppendLine("# The analysis");
            b.AppendLine("Comments of the report of a race.");
            b.AppendLine("Event: " + EventText(race));
            b.AppendLine();
            AthleteText(b, race, athlete);
            b.AppendLine();

            b.AppendLine("# The race (from the analysis of the video)");
            Line(b, "final time", Sec(c != null ? c.FinalTime : null));
            double? reaction = race.Reaction ?? (c != null ? c.ReactionTime : null);
            Line(b, "reaction", Sec(reaction));
            if (c != null)
            {
                Line(b, "feet off the block", Sec(c.BlockTime));
                Line(b, "flight", Sec(c.FlightTime));
                Line(b, "15 " + unit, Sec(c.Start15));
                Line(b, "underwater after the start", Sec(c.StartUnderwaterTime));
                Line(b, "kicks after the start", c.StartKicks.HasValue ? c.StartKicks.Value.ToString(CultureInfo.InvariantCulture) : "");
                Line(b, "average velocity", Num(c.AvgVel, "0.00", " m/s"));
                Line(b, "average stroke rate", Num(c.AvgSr, "0.0", " /min"));
                Line(b, "average distance per stroke", Num(c.AvgDps, "0.00", " m"));
                Line(b, "last 5 " + unit + " of the finish", Sec(c.FinishLast5));
                Line(b, "last stroke to the wall", Sec(c.FinishLastStrokeGap));
            }

            Line(b, "breakout distance after the start", Num(race.BreakoutDist, "0.0", " " + unit));
            Line(b, "underwater distance after the start", Num(race.UnderwaterDist, "0.0", " " + unit) + (race.UnderwaterEstimated && race.UnderwaterDist.HasValue ? " (estimated)" : ""));

            if (c != null && c.Rows.Count > 0)
            {
                b.AppendLine();
                b.AppendLine("## Checkpoints (distance | time | split | velocity m/s | strokes | stroke rate /min | distance per stroke m | reference | difference)");
                foreach (SwimRow r in c.Rows)
                {
                    b.AppendLine("- " + r.D.ToString(CultureInfo.InvariantCulture) + " " + unit + " | " + Sec(r.T) + " | " + Sec(r.SegTime) + " | " +
                        Num(r.Vel, "0.00", "") + " | " + Num(r.Strokes, "0.#", "") + " | " + Num(r.Sr, "0.0", "") + " | " + Num(r.Dps, "0.00", "") + " | " +
                        Sec(r.RefT) + " | " + (r.Delta.HasValue ? (r.Delta.Value >= 0 ? "+" : "") + r.Delta.Value.ToString("0.00", CultureInfo.InvariantCulture) : ""));
                }
            }

            if (c != null && c.Turns.Count > 0)
            {
                b.AppendLine();
                b.AppendLine("## Turns");
                foreach (SwimTurnComputed t in c.Turns)
                {
                    b.AppendLine("- wall " + t.Wall.ToString(CultureInfo.InvariantCulture) + " " + unit + ": in 5 " + Sec(t.In5) + " · out 15 " + Sec(t.Out15) +
                        " · in-out " + Sec(t.InOut) + (t.Exchange ? " (relay exchange)" : ""));
                }
            }

            if (phases != null && phases.Count > 0)
            {
                b.AppendLine();
                b.AppendLine("## Start and turns (technical values)");
                foreach (SwimTechPhase p in phases)
                {
                    b.AppendLine("### " + (lang == "en" ? p.Title : (p.TitleGreek ?? p.Title)));
                    if (PhaseValues(b, race, p) == 0)
                        b.AppendLine("- no marks");
                }
            }

            List<string> evals = CoachEvaluations(race, phases, cat, lang);
            if (evals.Count > 0)
            {
                b.AppendLine();
                b.AppendLine("## The coach's evaluations of the start and the turns (his own scores)");
                foreach (string e in evals)
                    b.AppendLine(e);
            }

            SwimReference reference = c != null ? c.Reference : null;
            b.AppendLine();
            b.AppendLine("# The reference");
            b.AppendLine(reference == null ? "None." : "The reference column above is " + ReferenceKind(reference) + ".");
            b.AppendLine();
            FramesText(b, moments, screens, "the start signal");
            b.AppendLine();
            b.AppendLine("# What to do");
            b.AppendLine("Look at the " + images.ToString(CultureInfo.InvariantCulture) + " frames that follow, then call " + RaceTool + " once.");
            return b.ToString();
        }

        private static string ReferenceKind(SwimReference r)
        {
            if (r.Kind == SwimReference.KindOwn)
                return "an earlier race of the same athlete";
            if (r.Kind == SwimReference.KindVideo)
                return "another race analysed on video";
            return "a reference race (for example a final)";
        }

        /// <summary>The errors the coach found in his evaluations (his own scores, not the proposals).</summary>
        private static List<string> CoachEvaluations(SwimRace race, IList<SwimTechPhase> phases, SwimTechSheetCatalog cat, string lang)
        {
            List<string> lines = new List<string>();
            if (race.TechEvals == null || cat == null || phases == null)
                return lines;

            foreach (SwimTechEval e in race.TechEvals)
            {
                SwimTechSheet sheet = cat.Sheet(e.SheetId);
                SwimTechPhase phase = phases.FirstOrDefault(p => p.Id == e.PhaseId);
                if (sheet == null || phase == null)
                    continue;

                int good = 0, bad = 0;
                List<string> errors = new List<string>();
                foreach (SwimTechSheetItem it in sheet.Items)
                {
                    SwimTechScore s;
                    if (!e.Scores.TryGetValue(it.Key, out s) || s.Auto || s.Value < 0)
                        continue;
                    bool error = sheet.Scored ? s.Value == 0 : s.Value == 1;
                    if (error)
                    {
                        bad++;
                        errors.Add(OneLine(it.Criterion.Label.In(lang)));
                    }
                    else
                    {
                        good++;
                    }
                }

                if (good + bad == 0)
                    continue;
                lines.Add("- " + (lang == "en" ? phase.Title : (phase.TitleGreek ?? phase.Title)) + ": " + good.ToString(CultureInfo.InvariantCulture) + " points well, " +
                    bad.ToString(CultureInfo.InvariantCulture) + " errors" + (errors.Count > 0 ? ": " + string.Join("; ", errors.ToArray()) : ""));
            }

            return lines;
        }

        private static void AthleteText(StringBuilder b, SwimRace race, SwimAiAthlete a)
        {
            b.AppendLine("# The athlete (no name is given)");
            string gender = SwimTechEvaluator.GenderCode(a != null && !string.IsNullOrWhiteSpace(a.Gender) ? a.Gender : race.AthleteGender);
            b.AppendLine("- gender: " + (gender == "F" ? "female" : gender == "M" ? "male" : "not given"));
            string category = a != null && !string.IsNullOrWhiteSpace(a.Category) ? a.Category : race.AthleteCategory;
            Line(b, "category", category);
            if (a == null)
                return;
            Line(b, "age", a.Age);
            Line(b, "height", string.IsNullOrWhiteSpace(a.Height) ? "" : a.Height.Trim() + (a.Height.Trim().All(char.IsDigit) ? " cm" : ""));
            Line(b, "best time in this event", a.Best);
            Line(b, "level", a.Level);
            Line(b, "the coach asks you to look especially at", a.Focus);
        }

        private static void FramesText(StringBuilder b, IList<SwimAiMoment> moments, int screens, string from)
        {
            b.AppendLine("# The frames");
            b.AppendLine(screens > 1
                ? screens.ToString(CultureInfo.InvariantCulture) + " synchronised screens (cameras): every moment below is shown by each screen at the same instant."
                : "One screen (camera).");
            if (moments == null || moments.Count == 0)
                return;
            b.AppendLine("The moments, with their time in seconds from " + from + ":");
            foreach (SwimAiMoment m in moments)
                b.AppendLine("- moment " + m.N.ToString(CultureInfo.InvariantCulture) + ": " + m.Label + " (" + Signed(m.Seconds) + " s)");
        }

        /// <summary>The values of the phase (SWIM_TECH_METRICS) with their labels; how many.</summary>
        private static int PhaseValues(StringBuilder b, SwimRace race, SwimTechPhase phase)
        {
            int n = 0;
            if (phase == null || phase.Values == null)
                return 0;

            foreach (KeyValuePair<string, double> kv in phase.Values)
            {
                if (double.IsNaN(kv.Value) || double.IsInfinity(kv.Value))
                    continue;
                SwimTechMetric metric = SwimTechCalculator.Metrics.FirstOrDefault(m => m.Key == kv.Key && m.Kind.HasValue && m.Kind.Value == phase.Kind) ??
                    SwimTechCalculator.Metrics.FirstOrDefault(m => m.Key == kv.Key);
                if (metric == null)
                    continue;
                b.AppendLine("- " + metric.Label + ": " + metric.Format(kv.Value) + UnitText(metric.Unit, race));
                n++;
            }

            return n;
        }

        private static string UnitText(string unit, SwimRace race)
        {
            switch (unit)
            {
                case "s": return " s";
                case "d": return race.IsYards ? " yd" : " m";
                case "v": return " m/s";
                case "r": return " /s";
                default: return "";
            }
        }

        private static string Target(SwimTechCheck check)
        {
            try
            {
                return SwimTechLearning.Effective(check).GoodText;
            }
            catch (Exception)
            {
                return check.GoodText;
            }
        }

        public static string EventText(SwimRace race)
        {
            string style = StyleEnglish(race.Style);
            string pool = race.Pool.ToString(CultureInfo.InvariantCulture) + (race.IsYards ? " yd" : " m") + " pool";
            if (race.IsTraining)
                return style + " · training · " + pool;
            string distance = (race.Relay ? "relay " : "") + race.DistanceLabel + (race.IsYards ? " yd" : " m");
            return distance + " " + style + " · " + pool + (string.IsNullOrWhiteSpace(race.Round) ? "" : " · round: " + race.Round.Trim());
        }

        private static string StyleEnglish(string style)
        {
            switch (style)
            {
                case "fs": return "freestyle";
                case "bk": return "backstroke";
                case "br": return "breaststroke";
                case "fly": return "butterfly";
                case "im": return "individual medley";
                default: return style ?? "";
            }
        }

        private static void Line(StringBuilder b, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                b.AppendLine("- " + label + ": " + OneLine(value.Trim()));
        }

        private static string Sec(double? v)
        {
            return v.HasValue && !double.IsNaN(v.Value) ? v.Value.ToString("0.00", CultureInfo.InvariantCulture) : "";
        }

        private static string Num(double? v, string format, string unit)
        {
            return v.HasValue && !double.IsNaN(v.Value) ? v.Value.ToString(format, CultureInfo.InvariantCulture) + unit : "";
        }

        public static string Signed(double seconds)
        {
            return (seconds < 0 ? "−" : "+") + Math.Abs(seconds).ToString("0.00", CultureInfo.InvariantCulture);
        }

        internal static string OneLine(string text)
        {
            return (text ?? "").Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Trim();
        }
        #endregion
    }

    /// <summary>
    /// (2.19.0) The moments of the video Claude sees: for a start or a turn the moments of the points of its list (one moment per frame,
    /// with all its points), frames spread over the phase when the marks give few of them, at most max (the nearest ones join); for a race
    /// its start, walls, breakouts and finish.
    /// </summary>
    public static class SwimAiMoments
    {
        public static List<SwimAiMoment> Tech(SwimTechPhase phase, SwimTechSheet sheet, SwimTiming timing, SwimTechSheetCatalog cat, long first, long last,
            int max, string lang)
        {
            List<SwimAiMoment> list = new List<SwimAiMoment>();
            if (phase == null || sheet == null || timing == null || !timing.IsValid)
                return list;

            double frame = Math.Max(1, timing.AverageTimestampsPerFrame);
            List<KeyValuePair<long, string>> found = new List<KeyValuePair<long, string>>();
            foreach (string anchor in sheet.Items.Select(i => i.Criterion.Anchor).Where(a => !string.IsNullOrEmpty(a)).Distinct())
            {
                long? ts = SwimTechEvaluator.AnchorTimestamp(phase, anchor, timing);
                if (ts.HasValue && ts.Value >= first && ts.Value <= last)
                    found.Add(new KeyValuePair<long, string>(ts.Value, anchor));
            }

            foreach (KeyValuePair<long, string> f in found.OrderBy(x => x.Key))
            {
                SwimAiMoment same = list.LastOrDefault(m => Math.Abs(m.Timestamp - f.Key) < frame * 1.5);
                if (same != null)
                {
                    same.Anchors.Add(f.Value);
                    continue;
                }

                SwimAiMoment moment = new SwimAiMoment { Timestamp = f.Key };
                moment.Anchors.Add(f.Value);
                list.Add(moment);
            }

            // Few marks: frames between them where they are farthest apart, near them (half a second around them; without marks, the
            // moments around the origin of the phase: the signal, the wall contact).
            int wanted = Math.Min(max, 8);
            double second = timing.Timestamp(1, 0) - timing.Timestamp(0, 0);
            long from, to;
            if (list.Count > 0)
            {
                from = list.Min(m => m.Timestamp) - (long)(second * 0.5);
                to = list.Max(m => m.Timestamp) + (long)(second * 0.5);
            }
            else
            {
                from = phase.Origin - (long)(second * (phase.Kind == SwimPhaseKind.Turn ? 1.5 : 0.2));
                to = phase.Origin + (long)(second * 3);
            }

            from = Math.Max(Math.Max(first, phase.WindowStart), from);
            to = Math.Min(Math.Min(last, phase.WindowEnd - 1), to);
            if (list.Count < wanted && to > from)
            {
                List<long> candidates = Enumerable.Range(0, 24).Select(i => from + (long)((to - from) * (i + 0.5) / 24)).ToList();
                while (list.Count < wanted && candidates.Count > 0)
                {
                    long best = candidates.OrderByDescending(c => list.Count == 0 ? double.MaxValue : list.Min(m => (double)Math.Abs(m.Timestamp - c))).First();
                    candidates.Remove(best);
                    if (list.Any(m => Math.Abs(m.Timestamp - best) < frame * 3))
                        continue;
                    list.Add(new SwimAiMoment { Timestamp = best });
                }
            }

            list = list.OrderBy(m => m.Timestamp).ToList();
            JoinNearest(list, max);

            for (int i = 0; i < list.Count; i++)
            {
                SwimAiMoment m = list[i];
                m.N = i + 1;
                m.Seconds = timing.Seconds(m.Timestamp, phase.Origin);
                List<string> labels = new List<string>();
                foreach (string a in m.Anchors)
                {
                    SwimTechText t;
                    string label = cat != null && cat.Anchors.TryGetValue(a, out t) ? t.In(lang) : a;
                    if (label.Length > 0 && !labels.Contains(label))
                        labels.Add(label);
                }

                m.Label = labels.Count > 0 ? string.Join(" / ", labels.ToArray()) : "a frame between the marks";
            }

            return list;
        }

        /// <summary>At most max moments: the moment nearest to the one before it joins it (its points are judged there).</summary>
        private static void JoinNearest(List<SwimAiMoment> list, int max)
        {
            while (list.Count > Math.Max(2, max))
            {
                int at = 1;
                long gap = long.MaxValue;
                for (int i = 1; i < list.Count; i++)
                {
                    long g = list[i].Timestamp - list[i - 1].Timestamp;
                    if (g < gap)
                    {
                        gap = g;
                        at = i;
                    }
                }

                SwimAiMoment gone = list[at];
                SwimAiMoment into = list[at - 1];
                foreach (string a in gone.Anchors)
                {
                    if (!into.Anchors.Contains(a))
                        into.Anchors.Add(a);
                }

                list.RemoveAt(at);
            }
        }

        /// <summary>The moments of a race for its comments: the start (signal, block, entry, breakout), each wall and the breakout after it, the finish.</summary>
        public static List<SwimAiMoment> Race(SwimRace race, SwimComputed computed, SwimTiming timing, long first, long last, int max)
        {
            List<SwimAiMoment> list = new List<SwimAiMoment>();
            if (race == null || race.IsTraining || !race.StartTimestamp.HasValue || computed == null || timing == null || !timing.IsValid)
                return list;

            long origin = race.StartTimestamp.Value;
            double frame = Math.Max(1, timing.AverageTimestampsPerFrame);
            string unit = race.IsYards ? " yd" : " m";
            Action<double?, string> add = (seconds, label) =>
            {
                if (!seconds.HasValue || double.IsNaN(seconds.Value) || double.IsInfinity(seconds.Value))
                    return;
                long ts = timing.Timestamp(seconds.Value, origin);
                if (ts < first || ts > last || list.Any(m => Math.Abs(m.Timestamp - ts) < frame * 1.5))
                    return;
                list.Add(new SwimAiMoment { Timestamp = ts, Seconds = seconds.Value, Label = label });
            };

            add(0, "start signal");
            add(computed.BlockTime, "feet off the block");
            add(computed.EntryTime, "hand entry");
            List<double> breakouts = computed.Breakouts.OrderBy(b => b).ToList();
            if (breakouts.Count > 0)
                add(breakouts[0], "breakout after the start");
            foreach (SwimRow r in computed.Rows)
            {
                if (race.Pool <= 0 || r.D <= 0 || r.D % race.Pool != 0)
                    continue;
                double t = r.OnVideo;
                if (double.IsNaN(t))
                    continue;
                add(t, r.D >= race.Distance ? "finish" : "wall " + r.D.ToString(CultureInfo.InvariantCulture) + unit);
                if (r.D < race.Distance)
                {
                    double? after = breakouts.Where(b => b > t).Select(b => (double?)b).FirstOrDefault();
                    add(after, "breakout after the wall " + r.D.ToString(CultureInfo.InvariantCulture) + unit);
                }
            }

            list = list.OrderBy(m => m.Timestamp).ToList();
            while (list.Count > Math.Max(2, max))
                list.RemoveAt(list.Count / 2);
            for (int i = 0; i < list.Count; i++)
                list[i].N = i + 1;
            return list;
        }
    }

    /// <summary>(2.19.0) The body of a request of AI pro to the Messages API.</summary>
    public static class SwimAiRequest
    {
        public const int TechMaxTokens = 8192;
        public const int RaceMaxTokens = 4096;

        /// <summary>The text, then each moment's frames (one per screen) after a line that names them, then the tool to call.</summary>
        public static JObject Build(string model, string system, string context, IList<SwimAiMoment> moments, IList<SwimAiImage> images, int screens,
            JObject tool, int maxTokens)
        {
            JArray content = new JArray();
            content.Add(Text(context));
            if (moments != null && images != null)
            {
                foreach (SwimAiMoment m in moments.OrderBy(x => x.N))
                {
                    foreach (SwimAiImage i in images.Where(x => x.Moment == m.N && x.Jpeg != null && x.Jpeg.Length > 0).OrderBy(x => x.Screen))
                    {
                        content.Add(Text("Moment " + m.N.ToString(CultureInfo.InvariantCulture) + (screens > 1 ? " · screen " + i.Screen.ToString(CultureInfo.InvariantCulture) : "") +
                            " · " + m.Label + " (" + SwimAiPrompt.Signed(m.Seconds) + " s)"));
                        content.Add(Image(i));
                    }
                }
            }

            string name = (string)tool["name"];
            content.Add(Text("Now call " + name + "."));

            JObject message = new JObject();
            message["role"] = "user";
            message["content"] = content;
            JObject choice = new JObject();
            choice["type"] = "tool";
            choice["name"] = name;

            JObject body = new JObject();
            body["model"] = model;
            body["max_tokens"] = maxTokens;
            body["system"] = system;
            body["messages"] = new JArray(message);
            body["tools"] = new JArray(tool);
            body["tool_choice"] = choice;
            return body;
        }

        private static JObject Text(string text)
        {
            JObject o = new JObject();
            o["type"] = "text";
            o["text"] = text ?? "";
            return o;
        }

        private static JObject Image(SwimAiImage image)
        {
            JObject source = new JObject();
            source["type"] = "base64";
            source["media_type"] = "image/jpeg";
            source["data"] = Convert.ToBase64String(image.Jpeg);
            JObject o = new JObject();
            o["type"] = "image";
            o["source"] = source;
            return o;
        }

        /// <summary>The length of the texts of a body (for the estimate of its cost).</summary>
        public static int TextLength(JObject body)
        {
            int n = ((string)body["system"] ?? "").Length;
            JArray messages = body["messages"] as JArray;
            if (messages == null)
                return n;
            foreach (JToken m in messages)
            {
                JArray content = m["content"] as JArray;
                if (content == null)
                    continue;
                foreach (JToken c in content)
                {
                    if ((string)c["type"] == "text")
                        n += ((string)c["text"] ?? "").Length;
                }
            }

            return n;
        }
    }

    /// <summary>(2.19.0) Claude's answer on a point of a list.</summary>
    public class SwimAiPoint
    {
        public string Key = "";
        public bool? Done;
        public double Sure;
        public int? Moment;
        public string Why = "";
    }

    /// <summary>(2.19.0) Claude's evaluation of a start or a turn.</summary>
    public class SwimAiTechAnswer
    {
        public List<SwimAiPoint> Points = new List<SwimAiPoint>();
        public List<string> Observations = new List<string>();
        public string Summary = "";
        public List<string> Priorities = new List<string>();
        public List<string> Doubts = new List<string>();
    }

    /// <summary>(2.19.0) Claude's comments of a race.</summary>
    public class SwimAiRaceAnswer
    {
        public string Start = "";
        public string Swim = "";
        public string Turns = "";
        public string Finish = "";
        public string Comparison = "";
        public List<string> Priorities = new List<string>();
        public List<string> Doubts = new List<string>();
    }

    /// <summary>(2.19.0) What an evaluation of Claude changed in the list.</summary>
    public class SwimAiApplied
    {
        /// <summary>Points Claude answered (✓ ✗ or –).</summary>
        public int Answered;

        /// <summary>Put in the list as proposals (AI) for the coach to confirm.</summary>
        public int Proposed;

        /// <summary>Points with the coach's own score: kept as he scored them (Claude's answer only beside them).</summary>
        public int Coach;

        /// <summary>Points the program measured (🤖): its measurement stays.</summary>
        public int Measured;

        /// <summary>Points Claude was not sure enough of (not put in the list).</summary>
        public int Unsure;

        /// <summary>Points the frames do not show.</summary>
        public int NotSeen;

        /// <summary>Errors Claude saw.</summary>
        public int Errors;
    }

    /// <summary>
    /// (2.19.0) Claude's answers into the analysis: the points of the list (proposals AI, as the program's 🤖, which the coach confirms or
    /// changes: the evaluation stays his), the notes of the evaluation and of the analysis for the report (a block of AI pro that the next
    /// answer replaces, the coach's own notes stay).
    /// </summary>
    public static class SwimAiApply
    {
        /// <summary>The beginning of the block of AI pro in the notes.</summary>
        public const string BlockPrefix = "AI pro (";

        /// <summary>Below this certainty an answer of Claude is not put in the list (it stays in the notes and beside the point).</summary>
        public const double MinSure = 0.6;

        public static SwimAiTechAnswer ParseTech(JObject input, SwimTechSheet sheet, int moments)
        {
            SwimAiTechAnswer a = new SwimAiTechAnswer();
            if (input == null)
                return a;

            JArray points = input["points"] as JArray;
            if (points != null)
            {
                foreach (JToken p in points)
                {
                    string key = (Str(p["key"]) ?? "").Trim();
                    if (key.Length == 0 || a.Points.Any(x => x.Key == key))
                        continue;
                    if (sheet != null && !sheet.Items.Any(i => i.Key == key))
                        continue;

                    double sure = Num(p["sure"]) ?? 0.5;
                    if (sure > 1 && sure <= 100)
                        sure /= 100;
                    int? moment = Int(p["moment"]);
                    if (moment.HasValue && (moment.Value < 1 || moment.Value > moments))
                        moment = null;
                    a.Points.Add(new SwimAiPoint
                    {
                        Key = key,
                        Done = Bool(p["done"]),
                        Sure = Math.Max(0, Math.Min(1, sure)),
                        Moment = moment,
                        Why = SwimAiPrompt.OneLine(Str(p["why"])),
                    });
                }
            }

            JArray seen = input["observations"] as JArray;
            if (seen != null)
            {
                foreach (JToken o in seen)
                {
                    string text = SwimAiPrompt.OneLine(Str(o["seen"]));
                    int? m = Int(o["moment"]);
                    if (text.Length > 0)
                        a.Observations.Add((m.HasValue ? m.Value.ToString(CultureInfo.InvariantCulture) + ". " : "") + text);
                }
            }

            a.Summary = SwimAiPrompt.OneLine(Str(input["summary"]));
            a.Priorities = Strings(input["priorities"]);
            a.Doubts = Strings(input["doubts"]);
            return a;
        }

        public static SwimAiRaceAnswer ParseRace(JObject input)
        {
            SwimAiRaceAnswer a = new SwimAiRaceAnswer();
            if (input == null)
                return a;
            a.Start = SwimAiPrompt.OneLine(Str(input["start"]));
            a.Swim = SwimAiPrompt.OneLine(Str(input["swim"]));
            a.Turns = SwimAiPrompt.OneLine(Str(input["turns"]));
            a.Finish = SwimAiPrompt.OneLine(Str(input["finish"]));
            a.Comparison = SwimAiPrompt.OneLine(Str(input["comparison"]));
            a.Priorities = Strings(input["priorities"]);
            a.Doubts = Strings(input["doubts"]);
            return a;
        }

        /// <summary>The value of an answer in the list: 1 ✓, 0 ✗, -1 not visible; on a list of errors 1 the error is there.</summary>
        public static int ValueOf(SwimTechSheet sheet, bool? done)
        {
            if (!done.HasValue)
                return -1;
            return sheet.Scored ? (done.Value ? 1 : 0) : (done.Value ? 0 : 1);
        }

        /// <summary>A value of the list is "done well" (✓, or no error on a list of errors).</summary>
        public static bool Done(SwimTechSheet sheet, int value)
        {
            return sheet.Scored ? value == 1 : value == 0;
        }

        /// <summary>
        /// Claude's evaluation of a phase with a sheet: its answer kept on every point (SwimTechEval.Ai); on a point that nobody scored (or
        /// that Claude proposed before) a proposal (AI, Auto) when Claude is sure enough; the coach's scores and the program's measurements
        /// stay. The block of AI pro in the notes of the evaluation.
        /// </summary>
        public static SwimAiApplied Tech(SwimRace race, SwimTechPhase phase, SwimTechSheet sheet, SwimAiTechAnswer a, string model, DateTime when)
        {
            SwimAiApplied r = new SwimAiApplied();
            if (race == null || phase == null || sheet == null || a == null)
                return r;

            SwimTechEval eval = race.TechEvals.FirstOrDefault(x => x.PhaseId == phase.Id && x.SheetId == sheet.Id);
            if (eval == null)
            {
                eval = new SwimTechEval { PhaseId = phase.Id, SheetId = sheet.Id };
                race.TechEvals.Add(eval);
            }

            string date = when.ToString("d/M/yyyy HH:mm", CultureInfo.InvariantCulture);
            eval.Ai = new Dictionary<string, SwimAiMark>();
            eval.AiModel = model;
            eval.AiDate = date;
            foreach (SwimAiPoint p in a.Points)
            {
                SwimTechSheetItem it = sheet.Items.FirstOrDefault(i => i.Key == p.Key);
                if (it == null)
                    continue;

                int v = ValueOf(sheet, p.Done);
                r.Answered++;
                if (v < 0)
                    r.NotSeen++;
                else if (!Done(sheet, v))
                    r.Errors++;
                eval.Ai[it.Key] = new SwimAiMark { Value = v, Sure = Math.Round(p.Sure, 2), Moment = p.Moment, Why = p.Why ?? "" };

                SwimTechScore s;
                eval.Scores.TryGetValue(it.Key, out s);
                if (s != null && !s.Auto)
                {
                    r.Coach++;
                    continue;
                }

                if (s != null && !s.ByAi)
                {
                    r.Measured++;
                    continue;
                }

                if (v >= 0 && p.Sure >= MinSure - 1e-9)
                {
                    eval.Scores[it.Key] = new SwimTechScore { Value = v, Auto = true, ByAi = true };
                    r.Proposed++;
                    continue;
                }

                // Not visible, or not sure enough: a proposal of Claude from before goes.
                if (s != null)
                    eval.Scores.Remove(it.Key);
                if (v >= 0)
                    r.Unsure++;
            }

            eval.Notes = NotesWith(eval.Notes, TechBlock(sheet, a, model, date));
            eval.Date = date;
            return r;
        }

        /// <summary>The block of AI pro of an evaluation: summary, priorities, the errors seen with why, the points not seen, the doubts.</summary>
        public static string TechBlock(SwimTechSheet sheet, SwimAiTechAnswer a, string model, string date)
        {
            List<string> lines = new List<string>();
            lines.Add(BlockPrefix + SwimAiModels.Short(model) + " · " + date + "):");
            if (a.Summary.Length > 0)
                lines.Add(a.Summary);
            if (a.Priorities.Count > 0)
                lines.Add(SwimLang.T("Προτεραιότητες: ") + Numbered(a.Priorities));

            List<string> unseen = new List<string>();
            foreach (SwimTechSheetItem it in sheet.Items)
            {
                SwimAiPoint p = a.Points.FirstOrDefault(x => x.Key == it.Key);
                if (p == null)
                    continue;
                string name = !string.IsNullOrEmpty(it.N) ? it.N : Short(it.Criterion.Label.Text, 40);
                if (!p.Done.HasValue)
                {
                    unseen.Add(name);
                    continue;
                }

                if (p.Done.Value)
                    continue;
                lines.Add("✗ " + (!string.IsNullOrEmpty(it.N) ? it.N + ". " : "") + Short(it.Criterion.Label.Text, 70) + (p.Why.Length > 0 ? " — " + p.Why : ""));
            }

            if (unseen.Count > 0)
                lines.Add(SwimLang.T("Δεν φαίνονται στα καρέ: ") + string.Join(", ", unseen.ToArray()));
            if (a.Doubts.Count > 0)
                lines.Add(SwimLang.T("Αμφιβολίες: ") + string.Join(" · ", a.Doubts.ToArray()));
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        /// <summary>The comments of a race in the notes of the analysis (the block of AI pro; the coach's notes stay).</summary>
        public static void Race(SwimRace race, SwimAiRaceAnswer a, string model, DateTime when)
        {
            if (race == null || a == null)
                return;
            race.TechNotes = NotesWith(race.TechNotes, RaceBlock(a, model, when.ToString("d/M/yyyy HH:mm", CultureInfo.InvariantCulture)));
        }

        public static string RaceBlock(SwimAiRaceAnswer a, string model, string date)
        {
            List<string> lines = new List<string>();
            lines.Add(BlockPrefix + SwimAiModels.Short(model) + " · " + date + "):");
            AddPart(lines, SwimLang.T("Εκκίνηση: "), a.Start);
            AddPart(lines, SwimLang.T("Κολύμβηση: "), a.Swim);
            AddPart(lines, SwimLang.T("Στροφές: "), a.Turns);
            AddPart(lines, SwimLang.T("Τερματισμός: "), a.Finish);
            AddPart(lines, SwimLang.T("Σύγκριση: "), a.Comparison);
            if (a.Priorities.Count > 0)
                lines.Add(SwimLang.T("Προτεραιότητες: ") + Numbered(a.Priorities));
            if (a.Doubts.Count > 0)
                lines.Add(SwimLang.T("Αμφιβολίες: ") + string.Join(" · ", a.Doubts.ToArray()));
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private static void AddPart(List<string> lines, string label, string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                lines.Add(label + text.Trim());
        }

        /// <summary>
        /// The notes with the block of AI pro in place of the one before (the paragraphs are separated by an empty line; the block is one
        /// paragraph that begins with "AI pro ("). The coach's own paragraphs stay, in their order.
        /// </summary>
        public static string NotesWith(string notes, string block)
        {
            string text = (notes ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
            List<string> parts = new List<string>();
            foreach (string p in System.Text.RegularExpressions.Regex.Split(text, @"\n[ \t]*\n"))
            {
                string t = p.Trim('\n');
                if (t.Trim().Length == 0 || t.TrimStart().StartsWith(BlockPrefix, StringComparison.Ordinal))
                    continue;
                parts.Add(t);
            }

            if (!string.IsNullOrWhiteSpace(block))
                parts.Add(block.Replace("\r\n", "\n").Replace("\r", "\n").Trim('\n'));
            return string.Join("\n\n", parts.ToArray()).Replace("\n", Environment.NewLine);
        }

        /// <summary>How often Claude's answers agree with the coach's own scores in this analysis (points both answered, ✓ or ✗).</summary>
        public static void Agreement(SwimRace race, out int same, out int total)
        {
            same = 0;
            total = 0;
            if (race == null || race.TechEvals == null)
                return;

            foreach (SwimTechEval e in race.TechEvals)
            {
                if (e.Ai == null)
                    continue;
                foreach (KeyValuePair<string, SwimAiMark> kv in e.Ai)
                {
                    SwimTechScore s;
                    if (kv.Value == null || kv.Value.Value < 0 || !e.Scores.TryGetValue(kv.Key, out s) || s.Auto || s.Value < 0)
                        continue;
                    total++;
                    if (s.Value == kv.Value.Value)
                        same++;
                }
            }
        }

        private static string Numbered(List<string> items)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < items.Count; i++)
                parts.Add((i + 1).ToString(CultureInfo.InvariantCulture) + ") " + items[i]);
            return string.Join(" ", parts.ToArray());
        }

        private static string Short(string text, int max)
        {
            string t = SwimAiPrompt.OneLine(text);
            return t.Length <= max ? t : t.Substring(0, max - 1).TrimEnd() + "…";
        }

        #region JSON
        private static string Str(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null)
                return null;
            return t.Type == JTokenType.String ? (string)t : t.ToString(Formatting.None);
        }

        private static double? Num(JToken t)
        {
            if (t == null)
                return null;
            if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer)
                return (double)t;
            double v;
            return t.Type == JTokenType.String && double.TryParse(((string)t).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? (double?)v : null;
        }

        private static int? Int(JToken t)
        {
            double? v = Num(t);
            return v.HasValue && !double.IsNaN(v.Value) && Math.Abs(v.Value) < 1e6 ? (int?)(int)Math.Round(v.Value) : null;
        }

        private static bool? Bool(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null)
                return null;
            if (t.Type == JTokenType.Boolean)
                return (bool)t;
            string s = (Str(t) ?? "").Trim().ToLowerInvariant();
            if (s == "true" || s == "yes" || s == "1")
                return true;
            if (s == "false" || s == "no" || s == "0")
                return false;
            return null;
        }

        private static List<string> Strings(JToken t)
        {
            List<string> list = new List<string>();
            JArray a = t as JArray;
            if (a != null)
            {
                foreach (JToken x in a)
                {
                    string s = SwimAiPrompt.OneLine(Str(x));
                    if (s.Length > 0)
                        list.Add(s);
                }
            }
            else if (t != null && t.Type == JTokenType.String && SwimAiPrompt.OneLine((string)t).Length > 0)
            {
                list.Add(SwimAiPrompt.OneLine((string)t));
            }

            return list;
        }
        #endregion
    }

    /// <summary>(2.19.0) The coach's final answer beside Claude's on a point of a list (done well or not), for the calibration.</summary>
    public class SwimAiSample
    {
        public string Criterion = "";
        public bool Ai;
        public bool Coach;
        public string Date = "";
    }

    /// <summary>
    /// (2.19.0) The coach's final answers beside Claude's on the points of the lists, on this computer (SwimAiCalibration.json next to the
    /// settings): how often Claude agrees with him on each point and where it is more lenient or stricter than him. Its summary goes with
    /// the next requests, so that Claude follows the coach's way of judging. One answer per point of an evaluation (the race, the phase,
    /// the sheet and the point): the coach changing his score replaces it.
    /// </summary>
    public class SwimAiCalibration
    {
        /// <summary>A point needs this many answers before its summary goes with the requests.</summary>
        public const int MinAnswers = 3;

        public Dictionary<string, SwimAiSample> Samples = new Dictionary<string, SwimAiSample>();

        [JsonIgnore]
        public string Path;

        private static SwimAiCalibration current;

        public static string FilePath
        {
            get { return System.IO.Path.Combine(Software.SettingsDirectory, "SwimAiCalibration.json"); }
        }

        public static SwimAiCalibration Current
        {
            get
            {
                if (current == null)
                    current = Load(FilePath);
                return current;
            }
        }

        public static SwimAiCalibration Load(string path)
        {
            SwimAiCalibration c = null;
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    c = JsonConvert.DeserializeObject<SwimAiCalibration>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                c = null;
            }

            if (c == null)
                c = new SwimAiCalibration();
            if (c.Samples == null)
                c.Samples = new Dictionary<string, SwimAiSample>();
            c.Path = path;
            return c;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(Path))
                return;
            try
            {
                string temp = Path + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(this, Formatting.Indented));
                if (File.Exists(Path))
                    File.Delete(Path);
                File.Move(temp, Path);
            }
            catch (Exception)
            {
                // The calibration is a help: the evaluation is saved with the analysis anyway.
            }
        }

        public static string KeyOf(SwimRace race, string phaseId, string sheetId, string pointKey)
        {
            return (race != null ? race.Id : "") + "|" + phaseId + "|" + sheetId + "|" + pointKey;
        }

        /// <summary>
        /// The points of an evaluation after it changed: the coach's own score (✓ or ✗) beside Claude's answer goes in; a point without one
        /// of them goes out. True when something changed.
        /// </summary>
        public bool Sync(SwimRace race, SwimTechEval eval, SwimTechSheet sheet)
        {
            if (race == null || eval == null || sheet == null)
                return false;

            bool changed = false;
            string date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (SwimTechSheetItem it in sheet.Items)
            {
                string key = KeyOf(race, eval.PhaseId, eval.SheetId, it.Key);
                SwimAiMark mark = null;
                SwimTechScore s = null;
                bool both = eval.Ai != null && eval.Ai.TryGetValue(it.Key, out mark) && mark != null && mark.Value >= 0 &&
                    eval.Scores.TryGetValue(it.Key, out s) && s != null && !s.Auto && s.Value >= 0;
                if (!both)
                {
                    changed |= Samples.Remove(key);
                    continue;
                }

                bool ai = SwimAiApply.Done(sheet, mark.Value), coach = SwimAiApply.Done(sheet, s.Value);
                SwimAiSample old;
                if (Samples.TryGetValue(key, out old) && old != null && old.Ai == ai && old.Coach == coach && old.Criterion == it.Criterion.Key)
                    continue;
                Samples[key] = new SwimAiSample { Criterion = it.Criterion.Key, Ai = ai, Coach = coach, Date = date };
                changed = true;
            }

            return changed;
        }

        /// <summary>The answers of an evaluation that was deleted go.</summary>
        public bool RemoveEval(SwimRace race, string phaseId, string sheetId)
        {
            string prefix = KeyOf(race, phaseId, sheetId, "");
            List<string> keys = Samples.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (string k in keys)
                Samples.Remove(k);
            return keys.Count > 0;
        }

        public void Stats(string criterion, out int agree, out int total, out int lenient, out int strict)
        {
            agree = total = lenient = strict = 0;
            foreach (SwimAiSample s in Samples.Values)
            {
                if (s == null || s.Criterion != criterion)
                    continue;
                total++;
                if (s.Ai == s.Coach)
                    agree++;
                else if (s.Ai)
                    lenient++;
                else
                    strict++;
            }
        }

        /// <summary>The lines of the request: the points of the sheet with enough answers of the coach beside Claude's.</summary>
        public List<string> Lines(SwimTechSheet sheet, string lang)
        {
            List<string> lines = new List<string>();
            if (sheet == null)
                return lines;

            foreach (SwimTechSheetItem it in sheet.Items)
            {
                int agree, total, lenient, strict;
                Stats(it.Criterion.Key, out agree, out total, out lenient, out strict);
                if (total < MinAnswers)
                    continue;
                string line = "- key " + it.Key + " (" + SwimAiPrompt.OneLine(it.Criterion.Label.In(lang)) + "): you agreed with the coach on " +
                    agree.ToString(CultureInfo.InvariantCulture) + " of " + total.ToString(CultureInfo.InvariantCulture) + " earlier answers";
                if (lenient > 0)
                    line += "; " + lenient.ToString(CultureInfo.InvariantCulture) + " times you said done and he said not done (be stricter here)";
                if (strict > 0)
                    line += "; " + strict.ToString(CultureInfo.InvariantCulture) + " times you said not done and he said done (be less strict here)";
                lines.Add(line + ".");
            }

            return lines;
        }

        /// <summary>All the answers: how often Claude agreed with the coach.</summary>
        public void Totals(out int agree, out int total)
        {
            agree = Samples.Values.Count(s => s != null && s.Ai == s.Coach);
            total = Samples.Values.Count(s => s != null);
        }
    }
}
