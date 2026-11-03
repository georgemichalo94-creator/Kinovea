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
using Kinovea.Services;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.13.0) A text of the catalogue in Greek and English: the one of the language of the analysis (Greek when there is no English).
    /// </summary>
    public class SwimTechText
    {
        public string El = "";
        public string En = "";

        public string Text
        {
            get { return SwimLang.English && !string.IsNullOrEmpty(En) ? En : El; }
        }

        /// <summary>The text in the language of a report ("en" or "el").</summary>
        public string In(string lang)
        {
            return lang == "en" && !string.IsNullOrEmpty(En) ? En : El;
        }

        public override string ToString()
        {
            return Text;
        }

        internal static SwimTechText Of(JToken t)
        {
            SwimTechText x = new SwimTechText();
            if (t == null || t.Type != JTokenType.Object)
                return x;

            x.El = (string)t["el"] ?? "";
            x.En = (string)t["en"] ?? "";
            return x;
        }
    }

    /// <summary>
    /// (2.13.0) An automatic check: what the program measures (kind "time" from the marked moments, "pose" from the skeleton, "rule"
    /// a rule of the sport), its unit and the range taken as good to start with (Min, Max, Eq).
    /// </summary>
    public class SwimTechCheck
    {
        public string Id = "";
        public string Kind = "";
        public SwimTechText Label = new SwimTechText();
        public string Unit = "";
        public double? Min;
        public double? Max;
        public double? Eq;

        /// <summary>A value in the good range: true, false, or null when the check has no range.</summary>
        public bool? Good(double value)
        {
            if (!Min.HasValue && !Max.HasValue && !Eq.HasValue)
                return null;

            if (Eq.HasValue && Math.Abs(value - Eq.Value) > 1e-9)
                return false;
            if (Min.HasValue && value < Min.Value - 1e-9)
                return false;
            if (Max.HasValue && value > Max.Value + 1e-9)
                return false;
            return true;
        }

        /// <summary>The good range as a short text ("≥ 2", "75–110 °"), empty without one.</summary>
        public string GoodText
        {
            get
            {
                string u = string.IsNullOrEmpty(Unit) ? "" : (Unit == "%" || Unit == "°" ? Unit : " " + Unit);
                Func<double, string> f = v => v.ToString(v == Math.Floor(v) ? "0" : "0.0#", CultureInfo.InvariantCulture);
                if (Eq.HasValue)
                    return "= " + f(Eq.Value) + u;
                if (Min.HasValue && Max.HasValue)
                    return f(Min.Value) + "–" + f(Max.Value) + u;
                if (Min.HasValue)
                    return "≥ " + f(Min.Value) + u;
                if (Max.HasValue)
                    return "≤ " + f(Max.Value) + u;
                return "";
            }
        }
    }

    /// <summary>
    /// (2.13.0) A criterion of a sheet: its label, how far the program goes on its own (Level: "A" it measures and scores it, "P" it
    /// measures what it can and proposes, "X" the coach judges it), the check that measures it, the moment of the video it is judged
    /// at (Anchor) and what to correct (Cue).
    /// </summary>
    public class SwimTechCriterion
    {
        public string Key = "";
        public SwimTechText Label = new SwimTechText();
        public string Level = "X";
        public string Check;
        public string Anchor = "";
        public SwimTechText Cue;
    }

    public class SwimTechSheetItem
    {
        /// <summary>The number of the point on the sheet ("1", "4β", "2,3", "1-2"); empty when the sheet has none.</summary>
        public string N = "";
        public string Key = "";
        public SwimTechCriterion Criterion;

        /// <summary>
        /// (2.16.0) The points of the older lists this point of a single list stands for (the same point said by several sheets, an error of
        /// a list of errors said as what is done well): their scores move here and the coach's answers on them count for its learning.
        /// </summary>
        public List<string> Same = new List<string>();

        /// <summary>This point, or one of the older points it stands for.</summary>
        public bool StandsFor(string key)
        {
            return key == Key || Same.Contains(key);
        }
    }

    public class SwimTechSheetPhase
    {
        public string Id = "";
        public SwimTechText Label = new SwimTechText();
        public List<SwimTechSheetItem> Items = new List<SwimTechSheetItem>();
    }

    /// <summary>
    /// (2.13.0) A sheet of the catalogue: a checklist of a start or a turn, a list of common errors (Scored false: a mark means the error
    /// is there), the underwater phase, or the rules. (2.16.0) The single lists: one for the start and one for the turn of each style (and
    /// one for each change of stroke of a medley) with everything in it; the older sheets stay Hidden, for the evaluations made with them.
    /// </summary>
    public class SwimTechSheet
    {
        public string Id = "";
        public SwimTechText Title = new SwimTechText();
        public string Source = "";

        /// <summary>"start", "turn", "pullout", "underwater" or "rules".</summary>
        public string Kind = "";
        public List<string> Styles = new List<string>();

        /// <summary>Turns: "flip", "open" or "crossover" (null for the others).</summary>
        public string Turn;
        public bool Scored = true;

        /// <summary>The level table of the sheet ("start_15", "turn_5_10"), null without one.</summary>
        public string Levels;
        public SwimTechText Note;

        /// <summary>(2.16.0) An older sheet: not offered any more, read for the evaluations made with it (they move to the single list).</summary>
        public bool Hidden;

        /// <summary>(2.16.0) The turn of a medley from one stroke to the next it is for ("fly-bk", "bk-br", "br-fs"), null for the others.</summary>
        public string Transition;
        public List<SwimTechSheetPhase> Phases = new List<SwimTechSheetPhase>();

        public IEnumerable<SwimTechSheetItem> Items
        {
            get { return Phases.SelectMany(p => p.Items); }
        }

        /// <summary>
        /// The sheet fits a start or a turn of this style ("fs", "bk", "br", "fly", "im"; (2.16.0) "im-fly-bk", "im-bk-br", "im-br-fs": the
        /// turn of a medley where the stroke changes, which only the list of that change fits).
        /// </summary>
        public bool Fits(SwimPhaseKind kind, string style)
        {
            // The pull-out of breaststroke follows the dive as it follows the push from the wall (its sheets are for breaststroke only).
            bool kindOk = kind == SwimPhaseKind.Start
                ? Kind == "start" || Kind == "pullout" || Kind == "underwater" || Kind == "rules"
                : Kind == "turn" || Kind == "pullout" || Kind == "underwater" || Kind == "rules";
            if (!kindOk)
                return false;
            style = style ?? "";
            if (style.StartsWith("im-", StringComparison.Ordinal))
                return kind == SwimPhaseKind.Turn && Transition == style.Substring(3);
            return Styles.Count == 0 || Styles.Contains(style);
        }
    }

    /// <summary>(2.13.0) A table of levels (1 = best): by style ("all" for every style) and gender ("M"/"F", or the column names).</summary>
    public class SwimTechLevelTable
    {
        public string Id = "";
        public SwimTechText Label = new SwimTechText();
        public string Check;
        public bool HigherIsBetter;
        public Dictionary<string, Dictionary<string, double[]>> Rows = new Dictionary<string, Dictionary<string, double[]>>();

        /// <summary>The level (1 = best) of a value for a style and a gender, null when the table has no row for them.</summary>
        public int? Level(string style, string gender, double value)
        {
            Dictionary<string, double[]> row;
            if (!Rows.TryGetValue(style ?? "", out row) && !Rows.TryGetValue("all", out row))
                return null;

            double[] limits;
            if (string.IsNullOrEmpty(gender) || !row.TryGetValue(gender, out limits) || limits == null || limits.Length == 0)
                return null;

            for (int i = 0; i < limits.Length; i++)
            {
                if (HigherIsBetter ? value >= limits[i] - 1e-9 : value <= limits[i] + 1e-9)
                    return i + 1;
            }

            return limits.Length + 1;
        }

        public int Count(string style, string gender)
        {
            Dictionary<string, double[]> row;
            double[] limits;
            if (!Rows.TryGetValue(style ?? "", out row) && !Rows.TryGetValue("all", out row))
                return 0;
            return !string.IsNullOrEmpty(gender) && row.TryGetValue(gender, out limits) && limits != null ? limits.Length : 0;
        }
    }

    /// <summary>
    /// (2.13.0) The catalogue of the technique evaluation: the sheets of criteria the coach scores, the automatic checks, the moments of
    /// the video and the level tables. It is a file of the coach (SwimTechSheets.json, next to the settings: the sheets are his own
    /// documents), read again when it changes.
    /// </summary>
    public class SwimTechSheetCatalog
    {
        public int Version;
        public string Updated = "";
        public Dictionary<string, SwimTechText> Anchors = new Dictionary<string, SwimTechText>();
        public Dictionary<string, SwimTechCheck> Checks = new Dictionary<string, SwimTechCheck>();
        public Dictionary<string, SwimTechCriterion> Criteria = new Dictionary<string, SwimTechCriterion>();
        public List<SwimTechSheet> Sheets = new List<SwimTechSheet>();
        public Dictionary<string, SwimTechLevelTable> Levels = new Dictionary<string, SwimTechLevelTable>();

        /// <summary>Why the catalogue could not be read (empty when it was read).</summary>
        public string Error = "";

        private static SwimTechSheetCatalog cached;
        private static DateTime cachedWrite;
        private static string cachedPath;

        public static string FilePath
        {
            get { return Path.Combine(Software.SettingsDirectory, "SwimTechSheets.json"); }
        }

        /// <summary>The catalogue of the settings folder (read again when the file changes); an empty one with Error when it is missing.</summary>
        public static SwimTechSheetCatalog Current
        {
            get
            {
                string path = FilePath;
                DateTime write = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (cached == null || path != cachedPath || write != cachedWrite)
                {
                    cached = Load(path);
                    cachedPath = path;
                    cachedWrite = write;
                }

                return cached;
            }
        }

        public static SwimTechSheetCatalog Load(string path)
        {
            if (!File.Exists(path))
                return new SwimTechSheetCatalog { Error = SwimLang.T("Δεν βρέθηκε ο κατάλογος κριτηρίων (SwimTechSheets.json).") };

            try
            {
                return Parse(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                return new SwimTechSheetCatalog { Error = SwimLang.T("Ο κατάλογος κριτηρίων δεν διαβάστηκε: ") + e.Message };
            }
        }

        public static SwimTechSheetCatalog Parse(string json)
        {
            JObject root = JObject.Parse(json);
            if ((string)root["format"] != "kinovea-swim-tech-sheets")
                throw new FormatException(SwimLang.T("δεν είναι κατάλογος κριτηρίων"));

            SwimTechSheetCatalog c = new SwimTechSheetCatalog();
            c.Version = (int?)root["version"] ?? 0;
            c.Updated = (string)root["updated"] ?? "";

            JObject anchors = root["anchors"] as JObject;
            if (anchors != null)
            {
                foreach (JProperty p in anchors.Properties())
                    c.Anchors[p.Name] = SwimTechText.Of(p.Value);
            }

            JObject checks = root["checks"] as JObject;
            if (checks != null)
            {
                foreach (JProperty p in checks.Properties())
                {
                    JToken v = p.Value;
                    JToken good = v["good"];
                    c.Checks[p.Name] = new SwimTechCheck
                    {
                        Id = p.Name,
                        Kind = (string)v["kind"] ?? "",
                        Label = SwimTechText.Of(v),
                        Unit = (string)v["unit"] ?? "",
                        Min = good != null && good.Type == JTokenType.Object ? (double?)good["min"] : null,
                        Max = good != null && good.Type == JTokenType.Object ? (double?)good["max"] : null,
                        Eq = good != null && good.Type == JTokenType.Object ? (double?)good["eq"] : null,
                    };
                }
            }

            JObject criteria = root["criteria"] as JObject;
            if (criteria != null)
            {
                foreach (JProperty p in criteria.Properties())
                {
                    JToken v = p.Value;
                    JToken cue = v["cue"];
                    string check = (string)v["check"];
                    c.Criteria[p.Name] = new SwimTechCriterion
                    {
                        Key = p.Name,
                        Label = SwimTechText.Of(v),
                        Level = (string)v["level"] ?? "X",
                        Check = string.IsNullOrEmpty(check) ? null : check,
                        Anchor = (string)v["anchor"] ?? "",
                        Cue = cue != null && cue.Type == JTokenType.Object ? SwimTechText.Of(cue) : null,
                    };
                }
            }

            JArray sheets = root["sheets"] as JArray;
            if (sheets != null)
            {
                foreach (JToken s in sheets)
                {
                    SwimTechSheet sheet = new SwimTechSheet
                    {
                        Id = (string)s["id"] ?? "",
                        Title = SwimTechText.Of(s),
                        Source = (string)s["source"] ?? "",
                        Kind = (string)s["kind"] ?? "",
                        Turn = (string)s["turn"],
                        Scored = (bool?)s["scored"] ?? true,
                        Levels = (string)s["levels"],
                        Note = s["note"] != null && s["note"].Type == JTokenType.Object ? SwimTechText.Of(s["note"]) : null,
                        Hidden = (bool?)s["hidden"] ?? false,
                        Transition = string.IsNullOrEmpty((string)s["transition"]) ? null : (string)s["transition"],
                    };
                    JArray styles = s["styles"] as JArray;
                    if (styles != null)
                        sheet.Styles = styles.Select(x => (string)x ?? "").Where(x => x.Length > 0).ToList();

                    JArray phases = s["phases"] as JArray;
                    if (phases != null)
                    {
                        foreach (JToken ph in phases)
                        {
                            SwimTechSheetPhase phase = new SwimTechSheetPhase { Id = (string)ph["id"] ?? "", Label = SwimTechText.Of(ph) };
                            JArray items = ph["items"] as JArray;
                            if (items != null)
                            {
                                foreach (JToken it in items)
                                {
                                    string key = (string)it["key"] ?? "";
                                    SwimTechCriterion crit;
                                    if (!c.Criteria.TryGetValue(key, out crit))
                                        continue;
                                    SwimTechSheetItem item = new SwimTechSheetItem { N = (string)it["n"] ?? "", Key = key, Criterion = crit };
                                    JArray same = it["same"] as JArray;
                                    if (same != null)
                                        item.Same = same.Select(x => (string)x ?? "").Where(x => x.Length > 0 && x != key).Distinct().ToList();
                                    phase.Items.Add(item);
                                }
                            }

                            if (phase.Items.Count > 0)
                                sheet.Phases.Add(phase);
                        }
                    }

                    if (sheet.Id.Length > 0 && sheet.Phases.Count > 0 && !c.Sheets.Any(x => x.Id == sheet.Id))
                        c.Sheets.Add(sheet);
                }
            }

            JObject levels = root["levels"] as JObject;
            if (levels != null)
            {
                foreach (JProperty p in levels.Properties())
                {
                    JObject v = p.Value as JObject;
                    if (v == null)
                        continue;

                    SwimTechLevelTable table = new SwimTechLevelTable
                    {
                        Id = p.Name,
                        Label = SwimTechText.Of(v),
                        Check = (string)v["check"],
                        HigherIsBetter = (bool?)v["higher_is_better"] ?? false,
                    };
                    foreach (JProperty row in v.Properties())
                    {
                        JObject cols = row.Value as JObject;
                        if (cols == null)
                            continue;

                        Dictionary<string, double[]> r = new Dictionary<string, double[]>();
                        foreach (JProperty col in cols.Properties())
                        {
                            JArray a = col.Value as JArray;
                            if (a != null)
                                r[col.Name] = a.Select(x => (double?)x ?? double.NaN).Where(x => !double.IsNaN(x)).ToArray();
                        }

                        if (r.Count > 0)
                            table.Rows[row.Name] = r;
                    }

                    c.Levels[p.Name] = table;
                }
            }

            return c;
        }

        public SwimTechSheet Sheet(string id)
        {
            return string.IsNullOrEmpty(id) ? null : Sheets.FirstOrDefault(s => s.Id == id);
        }

        public SwimTechCheck CheckOf(SwimTechCriterion criterion)
        {
            SwimTechCheck check;
            return criterion != null && criterion.Check != null && Checks.TryGetValue(criterion.Check, out check) ? check : null;
        }

        public string AnchorLabel(string anchor)
        {
            SwimTechText t;
            return anchor != null && Anchors.TryGetValue(anchor, out t) ? t.Text : "";
        }

        /// <summary>The sheets offered for a start or a turn of this style (not the older ones of a catalogue with single lists).</summary>
        public List<SwimTechSheet> SheetsFor(SwimPhaseKind kind, string style)
        {
            return Sheets.Where(s => !s.Hidden && s.Fits(kind, style)).ToList();
        }

        /// <summary>(2.16.0) The catalogue has the single lists (the older sheets hidden): the evaluations of those move to them.</summary>
        public bool SingleLists
        {
            get { return Sheets.Any(s => s.Hidden) && Sheets.Any(s => !s.Hidden); }
        }

        /// <summary>
        /// (2.16.0) The evaluations of a phase made with the older lists move to its single list: each score to the point that stands for it
        /// (the same point, or an error of a list of errors as what is done well: its answer turned over), with its frame, unless the point
        /// has the coach's own score there already (the score that did not move goes to dropped, for its frame). The notes go along. An
        /// older evaluation goes when nothing of the coach's is left in it (an automatic score of a point the single list has not is made
        /// again by the program). Returns how many scores and notes moved.
        /// </summary>
        public int MergeOld(List<SwimTechEval> evals, string phaseId, SwimTechSheet single, List<SwimTechScore> dropped, string date)
        {
            if (evals == null || single == null || single.Hidden)
                return 0;

            List<SwimTechEval> olds = evals.Where(e => e.PhaseId == phaseId && e.SheetId != single.Id && Sheet(e.SheetId) != null && Sheet(e.SheetId).Hidden).ToList();
            if (olds.Count == 0)
                return 0;

            int moved = 0;
            SwimTechEval target = evals.FirstOrDefault(e => e.PhaseId == phaseId && e.SheetId == single.Id);
            foreach (SwimTechEval old in olds)
            {
                SwimTechSheet from = Sheet(old.SheetId);
                bool keep = false;
                foreach (KeyValuePair<string, SwimTechScore> kv in old.Scores.ToList())
                {
                    SwimTechScore s = kv.Value;
                    SwimTechSheetItem item = single.Items.FirstOrDefault(i => i.Key == kv.Key) ?? single.Items.FirstOrDefault(i => i.StandsFor(kv.Key));
                    if (item == null)
                    {
                        if (s.Auto)
                            old.Scores.Remove(kv.Key);
                        else
                            keep = true;
                        continue;
                    }

                    if (target == null)
                    {
                        target = new SwimTechEval { PhaseId = phaseId, SheetId = single.Id };
                        evals.Add(target);
                    }

                    old.Scores.Remove(kv.Key);
                    SwimTechScore have;
                    target.Scores.TryGetValue(item.Key, out have);
                    if (have != null && !(have.Auto && !s.Auto))
                    {
                        if (dropped != null && !string.IsNullOrEmpty(s.SnapshotId) && s.SnapshotId != have.SnapshotId)
                            dropped.Add(s);
                        continue;
                    }

                    if (have != null && dropped != null && !string.IsNullOrEmpty(have.SnapshotId) && have.SnapshotId != s.SnapshotId)
                        dropped.Add(have);

                    int value = s.Value;
                    if (value >= 0 && from.Scored != single.Scored)
                        value = value == 1 ? 0 : 1;
                    target.Scores[item.Key] = new SwimTechScore { Value = value, Auto = s.Auto, Measured = s.Measured, Unit = s.Unit, SnapshotId = s.SnapshotId, Comment = s.Comment };
                    moved++;
                }

                string notes = (old.Notes ?? "").Trim();
                if (notes.Length > 0)
                {
                    if (target == null)
                    {
                        target = new SwimTechEval { PhaseId = phaseId, SheetId = single.Id };
                        evals.Add(target);
                    }

                    string had = (target.Notes ?? "").Trim();
                    if (had.IndexOf(notes, StringComparison.Ordinal) < 0)
                    {
                        target.Notes = had.Length > 0 ? had + Environment.NewLine + notes : notes;
                        moved++;
                    }
                }

                // An older evaluation with nothing of the coach's left goes (a change too); one that keeps a score stays as it is.
                if (!keep)
                {
                    evals.Remove(old);
                    moved++;
                }
            }

            if (moved > 0 && target != null && !string.IsNullOrEmpty(date))
                target.Date = date;
            return moved;
        }

        /// <summary>
        /// (2.16.1) A point of a single list whose words changed got a new key (the old key in its "same"): the scores of the evaluation of
        /// the list under the old key move to the point, unless the point has the coach's own score there already (the score that did not
        /// move goes to dropped, for its frame, and is removed: nothing shows it any more). Returns how many scores moved or went.
        /// </summary>
        public static int MoveRenamed(SwimTechEval eval, SwimTechSheet single, List<SwimTechScore> dropped)
        {
            if (eval == null || single == null || eval.SheetId != single.Id)
                return 0;

            int moved = 0;
            foreach (KeyValuePair<string, SwimTechScore> kv in eval.Scores.ToList())
            {
                if (single.Items.Any(i => i.Key == kv.Key))
                    continue;

                SwimTechSheetItem item = single.Items.FirstOrDefault(i => i.StandsFor(kv.Key));
                if (item == null)
                    continue;

                SwimTechScore s = kv.Value;
                eval.Scores.Remove(kv.Key);
                moved++;
                SwimTechScore have;
                eval.Scores.TryGetValue(item.Key, out have);
                if (have != null && !(have.Auto && !s.Auto))
                {
                    if (dropped != null && s != null && !string.IsNullOrEmpty(s.SnapshotId) && s.SnapshotId != have.SnapshotId)
                        dropped.Add(s);
                    continue;
                }

                if (have != null && dropped != null && s != null && !string.IsNullOrEmpty(have.SnapshotId) && have.SnapshotId != s.SnapshotId)
                    dropped.Add(have);

                if (s != null)
                    eval.Scores[item.Key] = s;
            }

            return moved;
        }

        /// <summary>(2.16.0) The catalogue has lists for the turns of a medley where the stroke changes.</summary>
        public bool HasTransitions
        {
            get { return Sheets.Any(s => !s.Hidden && s.Transition != null); }
        }

        /// <summary>A sheet of the program's automatic error lists (🤖, kv-auto-…).</summary>
        public static bool IsAuto(string sheetId)
        {
            return sheetId != null && sheetId.StartsWith("kv-auto-", StringComparison.Ordinal);
        }

        /// <summary>The program's automatic error list for a start or a turn of a stroke (a flip or an open turn), null when none fits.</summary>
        public SwimTechSheet AutoFor(SwimPhaseKind kind, string style, bool flip)
        {
            string turn = flip ? "flip" : "open";
            return SheetsFor(kind, style).FirstOrDefault(s => IsAuto(s.Id) && (kind == SwimPhaseKind.Start || s.Turn == null || s.Turn == turn));
        }

        /// <summary>
        /// The sheet proposed for a phase: the coach's last choice for this kind and style when it still fits, else the first checklist of
        /// the KOE for the style, else the first sheet that fits (with the single lists: the one of the style).
        /// </summary>
        public SwimTechSheet DefaultFor(SwimPhaseKind kind, string style, bool flip, string lastChoice)
        {
            List<SwimTechSheet> fit = SheetsFor(kind, style);
            SwimTechSheet last = fit.FirstOrDefault(s => s.Id == lastChoice);
            if (last != null)
                return last;

            string turn = flip ? "flip" : "open";
            SwimTechSheet best = fit.FirstOrDefault(s => s.Kind == (kind == SwimPhaseKind.Start ? "start" : "turn") && s.Scored && s.Id.StartsWith("koe-", StringComparison.Ordinal) &&
                (kind == SwimPhaseKind.Start || s.Turn == null || s.Turn == turn));
            if (best == null)
                best = fit.FirstOrDefault(s => s.Kind == (kind == SwimPhaseKind.Start ? "start" : "turn") && s.Scored && (kind == SwimPhaseKind.Start || s.Turn == null || s.Turn == turn));
            return best ?? fit.FirstOrDefault();
        }
    }
}
