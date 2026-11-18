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
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Kinovea.Services;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.26.0) A topic of the digital assistant: what a task is, where it is in the menu (the item named Target, found in the menu as it
    /// is now: its path is always the real one, in the language of the menu), its steps and the words that find it.
    /// In its texts: {path} is the path of the target, {path:name} the path of another item, {key:Command} the key of a command of the
    /// video in the settings now, {t:Greek} a text of the program in the language of the analysis.
    /// </summary>
    internal sealed class SwimAssistantTopic
    {
        public string Id;
        public string Target;
        public string TitleEl;
        public string TitleEn;
        public string ChipEl;
        public string ChipEn;
        public string LeadEl = "";
        public string LeadEn = "";
        public string[] StepsEl = new string[0];
        public string[] StepsEn = new string[0];
        public string[] Keys = new string[0];
        public string[] Related = new string[0];

        /// <summary>What it needs to run: "video" (a video in a screen), "analysis" (the analysis of a video), or null.</summary>
        public string Needs;

        /// <summary>«Άνοιξέ το» runs it (not what closes, deletes or leaves: those are only shown).</summary>
        public bool CanOpen = true;

        /// <summary>Its steps are the keys of the program now (menus and video).</summary>
        public bool KeyList;

        private List<string> searchKeys;

        public string Title
        {
            get { return SwimLang.English ? TitleEn : TitleEl; }
        }

        public string ChipText
        {
            get { return SwimLang.English ? (ChipEn ?? TitleEn) : (ChipEl ?? TitleEl); }
        }

        public string LeadText
        {
            get { return SwimLang.English ? LeadEn : LeadEl; }
        }

        public string[] StepTexts
        {
            get { return SwimLang.English ? StepsEn : StepsEl; }
        }

        public bool IsChip
        {
            get { return ChipEl != null; }
        }

        /// <summary>The words that find it, normalized: its own, its titles in both languages and its chip.</summary>
        public List<string> SearchKeys
        {
            get
            {
                if (searchKeys == null)
                {
                    HashSet<string> all = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string k in Keys)
                    {
                        string n = SwimAssistantText.Normalize(k);
                        if (n.Length > 0)
                            all.Add(n);
                    }

                    foreach (string text in new[] { TitleEl, TitleEn, ChipEl, ChipEn })
                    {
                        foreach (string w in SwimAssistantText.Words(SwimAssistantText.Normalize(text)))
                            all.Add(w);
                    }

                    searchKeys = all.ToList();
                }

                return searchKeys;
            }
        }

        public SwimAssistantTopic Lead(string el, string en)
        {
            LeadEl = el;
            LeadEn = en;
            return this;
        }

        public SwimAssistantTopic Steps(string[] el, string[] en)
        {
            StepsEl = el ?? new string[0];
            StepsEn = en ?? new string[0];
            return this;
        }

        /// <summary>The words that find it, separated by commas (a phrase is matched as a whole; a word also by its start).</summary>
        public SwimAssistantTopic Words(string words)
        {
            Keys = (words ?? "").Split(',').Select(w => w.Trim()).Where(w => w.Length > 0).ToArray();
            searchKeys = null;
            return this;
        }

        public SwimAssistantTopic See(string ids)
        {
            Related = (ids ?? "").Split(' ').Where(i => i.Length > 0).ToArray();
            return this;
        }

        public SwimAssistantTopic Chip(string el, string en)
        {
            ChipEl = el;
            ChipEn = en;
            return this;
        }

        public SwimAssistantTopic Need(string what)
        {
            Needs = what;
            return this;
        }

        public SwimAssistantTopic OnlyShow()
        {
            CanOpen = false;
            return this;
        }

        public SwimAssistantTopic Keyboard()
        {
            KeyList = true;
            return this;
        }
    }

    /// <summary>(2.26.0) An item of the menu as the assistant knows it now: its path, its words, its key.</summary>
    internal sealed class SwimAssistantEntry
    {
        public ToolStripItem Item;
        public string Name;
        public string Text;
        public string Path;
        public string Tip;
        public string Shortcut;
        public bool Enabled;
        internal List<string> TextKeys;
        internal List<string> TipKeys;
        internal List<string> PathKeys;
    }

    /// <summary>(2.26.0) What the assistant found for a question.</summary>
    internal sealed class SwimAssistantResult
    {
        public string Query = "";
        public SwimAssistantTopic Topic;
        public double Score;
        public List<SwimAssistantEntry> Entries = new List<SwimAssistantEntry>();
        public List<SwimAssistantTopic> Guesses = new List<SwimAssistantTopic>();

        public bool Asked
        {
            get { return Query.Length > 0; }
        }

        public bool NoMatch
        {
            get { return Asked && Topic == null && Entries.Count == 0; }
        }
    }

    /// <summary>
    /// (2.26.0) The words of a question as the assistant compares them: small letters without accents (ά → α, final ς → σ), without
    /// the small words (πού, πώς, το, how, the…), each word found by the start of a word (εξαγωγ → εξαγωγή) and with one wrong
    /// letter (εξαγογη).
    /// </summary>
    internal static class SwimAssistantText
    {
        private static readonly HashSet<string> Small = new HashSet<string>(StringComparer.Ordinal)
        {
            "ο", "η", "το", "οι", "τα", "του", "τησ", "των", "τον", "την", "τη", "στο", "στη", "στην", "στον", "στα", "στουσ", "στισ",
            "σε", "με", "για", "απο", "και", "να", "θα", "που", "πωσ", "τι", "ποιο", "ποια", "ποιοσ", "ειναι", "θελω", "μπορω", "μπορει",
            "γινεται", "κανω", "κανει", "βρισκω", "βρω", "βρισκεται", "εχω", "εχει", "ενα", "μια", "εναν", "μου", "σου", "μασ", "αυτο",
            "αυτη", "εδω", "εκει", "πρεπει", "λιγο", "ωστε", "οταν", "αν", "η", "ειμαι", "δειξε", "δειξ", "πεσ", "θελουμε",
            "the", "a", "an", "to", "of", "in", "on", "at", "for", "with", "how", "do", "does", "i", "can", "where", "is", "are", "what",
            "which", "my", "me", "it", "this", "that", "find", "get", "want", "please", "show", "there", "you", "be", "we"
        };

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";

            string d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder b = new StringBuilder(d.Length);
            bool space = true;
            foreach (char c in d)
            {
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.EnclosingMark || cat == UnicodeCategory.SpacingCombiningMark)
                    continue;

                char x = c == 'ς' ? 'σ' : c;
                if (char.IsLetterOrDigit(x))
                {
                    b.Append(x);
                    space = false;
                }
                else if (!space)
                {
                    b.Append(' ');
                    space = true;
                }
            }

            return b.ToString().Trim();
        }

        /// <summary>The words of a normalized text, without the small ones.</summary>
        public static List<string> Words(string normalized)
        {
            List<string> words = new List<string>();
            if (string.IsNullOrEmpty(normalized))
                return words;

            foreach (string w in normalized.Split(' '))
            {
                if (w.Length == 0 || Small.Contains(w))
                    continue;
                if (w.Length < 2 && !char.IsDigit(w[0]))
                    continue;
                if (!words.Contains(w))
                    words.Add(w);
            }

            return words;
        }

        public static int CommonStart(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length);
            int i = 0;
            while (i < n && a[i] == b[i])
                i++;
            return i;
        }

        /// <summary>The letters to change, add, take away or swap to go from one word to the other (optimal string alignment).</summary>
        public static int Distance(string a, string b)
        {
            int n = a.Length, m = b.Length;
            int[,] d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++)
                d[i, 0] = i;
            for (int j = 0; j <= m; j++)
                d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        v = Math.Min(v, d[i - 2, j - 2] + 1);
                    d[i, j] = v;
                }
            }

            return d[n, m];
        }

        /// <summary>How well a word of the question matches a word that finds a topic (0: not at all, 3: the same word).</summary>
        public static double Match(string word, string key)
        {
            if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(key))
                return 0;
            if (word == key)
                return 3;

            // The key is the stem of the word (εξαγωγ: εξαγωγή, εξαγωγές), or the word the start of the key (εξαγ…).
            if (key.Length >= 3 && word.StartsWith(key, StringComparison.Ordinal))
                return 2.5;
            if (word.Length >= 4 && key.StartsWith(word, StringComparison.Ordinal))
                return 2;

            if (CommonStart(word, key) >= 5)
                return 1.5;

            // One wrong letter (two in long words): the word against the key, or its start against a stem.
            if (word.Length >= 5 && key.Length >= 4)
            {
                int allowed = Math.Min(word.Length, key.Length) >= 8 ? 2 : 1;
                string start = word.Length > key.Length ? word.Substring(0, key.Length) : word;
                if (Distance(start, key) <= allowed || Distance(word, key) <= allowed)
                    return 1.6;
            }

            return 0;
        }

        /// <summary>
        /// The score of a question against words that find something: for each word of the question its best match, and 3 for each phrase
        /// of the keys in the question. Strong: the words matched well (2 or more).
        /// </summary>
        public static double Score(string query, List<string> words, IList<string> keys, out int strong)
        {
            strong = 0;
            double total = 0;
            if (keys == null || keys.Count == 0)
                return 0;

            foreach (string k in keys)
            {
                if (k.IndexOf(' ') > 0 && (" " + query + " ").IndexOf(" " + k, StringComparison.Ordinal) >= 0)
                {
                    total += 3;
                    strong++;
                }
            }

            foreach (string w in words)
            {
                double best = 0;
                foreach (string k in keys)
                {
                    if (k.IndexOf(' ') > 0)
                        continue;
                    double m = Match(w, k);
                    if (m > best)
                        best = m;
                }

                total += best;
                if (best >= 2)
                    strong++;
            }

            return total;
        }

        /// <summary>The groups of three letters of a normalized text (for «Μήπως ψάχνεις»).</summary>
        public static HashSet<string> Grams(string normalized)
        {
            HashSet<string> g = new HashSet<string>(StringComparer.Ordinal);
            string t = " " + (normalized ?? "") + " ";
            for (int i = 0; i + 3 <= t.Length; i++)
            {
                string x = t.Substring(i, 3);
                if (x.Trim().Length == 3)
                    g.Add(x);
            }

            return g;
        }

        /// <summary>The text of an item without its marks: the &amp; of its letter, the dot of the chip, the spaces at its ends.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            string t = text.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&");
            if (t.StartsWith(SwimMenuChip.DotText, StringComparison.Ordinal))
                t = t.Substring(SwimMenuChip.DotText.Length);
            return t.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }

    /// <summary>(2.26.0) The menu as it is now: every item that does something, with its path (the lists that change are not in it).</summary>
    internal static class SwimAssistantMenuMap
    {
        public const string Arrow = " › ";

        /// <summary>Items whose name says something of the moment after their word (closing: the file; undo: the drawing): only their word.</summary>
        private static readonly HashSet<string> Changing = new HashSet<string>(StringComparer.Ordinal) { "mnuCloseFile", "mnuCloseFile2", "mnuUndo", "mnuRedo" };

        /// <summary>Items whose tip says something of the moment (the state of the sending, with its analysis): not their tip.</summary>
        private static readonly HashSet<string> NoTip = new HashSet<string>(StringComparer.Ordinal) { "swimSheetChip" };

        /// <summary>A name of a file in a text (never in the map: it can be the name of an athlete).</summary>
        private static readonly Regex FileName = new Regex(@"[^\s\\/:*?""<>|]+\.(mp4|mov|avi|mkv|wmv|mts|m2ts|m4v|mpg|mpeg|kva|json|jpg|jpeg|png|bmp|pdf|csv|xlsx|docx|odt|ods)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The text of an item for the map: without names of files, only the word of an item that names something of the moment.</summary>
        public static string Safe(string name, string text)
        {
            string t = text ?? "";
            if (name != null && Changing.Contains(name))
            {
                int cut = t.IndexOfAny(new[] { '(', ':', '[', '-', '–', '"', '«' });
                if (cut > 0)
                    t = t.Substring(0, cut);
            }

            return FileName.Replace(t, "…").Trim();
        }

        /// <summary>
        /// The items of a bar of menus and of their menus. Only the items with a name (all the commands of Kinovea and of the analysis)
        /// and the filters of the video: the lists that change (the recent files, the folders watched, the windows, the buttons of the
        /// moment) are not named, so neither they nor their names of files are ever in the map.
        /// </summary>
        public static List<SwimAssistantEntry> Build(ToolStrip strip, Func<ToolStripItem, string> shortcutOf)
        {
            List<SwimAssistantEntry> list = new List<SwimAssistantEntry>();
            if (strip == null)
                return list;

            foreach (ToolStripItem item in strip.Items)
                Walk(item, new List<string>(), list, shortcutOf);
            return list;
        }

        private static void Walk(ToolStripItem item, List<string> path, List<SwimAssistantEntry> list, Func<ToolStripItem, string> shortcutOf)
        {
            if (item == null || item.IsDisposed || item is ToolStripSeparator || item is SwimMenuHeader || item is SwimMenuCaption || !item.Available)
                return;

            bool listed = !string.IsNullOrEmpty(item.Name) || item.Tag is VideoFilterType;
            if (!listed)
                return;

            string text = Safe(item.Name, SwimAssistantText.Clean(item.Text));
            if (text.Length == 0)
                return;

            List<string> here = new List<string>(path) { text };
            SwimAssistantEntry e = new SwimAssistantEntry
            {
                Item = item,
                Name = item.Name ?? "",
                Text = text,
                Path = string.Join(Arrow, here),
                Tip = NoTip.Contains(item.Name ?? "") ? "" : Safe(null, SwimAssistantText.Clean(item.ToolTipText)),
                Shortcut = shortcutOf != null ? shortcutOf(item) : null,
                Enabled = item.Enabled
            };
            e.TextKeys = SwimAssistantText.Words(SwimAssistantText.Normalize(text));
            e.TipKeys = SwimAssistantText.Words(SwimAssistantText.Normalize(e.Tip));
            e.PathKeys = SwimAssistantText.Words(SwimAssistantText.Normalize(string.Join(" ", path)));
            list.Add(e);

            ToolStripDropDownItem d = item as ToolStripDropDownItem;
            if (d != null && d.HasDropDownItems)
            {
                foreach (ToolStripItem child in d.DropDownItems)
                    Walk(child, here, list, shortcutOf);
            }
        }

        /// <summary>The path of an item from its menu on the bar ("Αναφορά › Βίντεο › Βίντεο…").</summary>
        public static string PathOf(ToolStripItem item)
        {
            List<string> parts = new List<string>();
            for (ToolStripItem i = item; i != null; i = i.OwnerItem)
                parts.Insert(0, Safe(i.Name, SwimAssistantText.Clean(i.Text)));
            return string.Join(Arrow, parts.Where(p => p.Length > 0));
        }

        /// <summary>The key of an item as its menu shows it ("Ctrl+O"), or null.</summary>
        public static string ShortcutOf(ToolStripItem item)
        {
            ToolStripMenuItem m = item as ToolStripMenuItem;
            if (m == null)
                return null;
            if (!string.IsNullOrEmpty(m.ShortcutKeyDisplayString))
                return m.ShortcutKeyDisplayString;
            if (m.ShortcutKeys != Keys.None)
                return new KeysConverter().ConvertToInvariantString(m.ShortcutKeys);
            return null;
        }
    }

    /// <summary>(2.26.0) The search of the assistant: the topic of a question, the items of the menu it names, the guesses.</summary>
    internal static class SwimAssistantSearch
    {
        public static SwimAssistantResult Find(string query, IList<SwimAssistantTopic> topics, IList<SwimAssistantEntry> entries)
        {
            SwimAssistantResult r = new SwimAssistantResult { Query = (query ?? "").Trim() };
            string q = SwimAssistantText.Normalize(r.Query);
            List<string> words = SwimAssistantText.Words(q);
            if (words.Count == 0)
                return r;

            double need = words.Count == 1 ? 1.5 : 2.0;
            SwimAssistantTopic best = null;
            double bestScore = 0;
            int bestStrong = 0;
            if (topics != null)
            {
                foreach (SwimAssistantTopic t in topics)
                {
                    int strong;
                    double s = SwimAssistantText.Score(q, words, t.SearchKeys, out strong);
                    if (s > bestScore + 1e-9 || (best != null && Math.Abs(s - bestScore) < 1e-9 && strong > bestStrong))
                    {
                        best = t;
                        bestScore = s;
                        bestStrong = strong;
                    }
                }
            }

            if (best != null && bestScore >= need)
            {
                r.Topic = best;
                r.Score = bestScore;
            }

            r.Entries = Items(q, words, entries, need, r.Topic != null ? r.Topic.Target : null);
            if (r.Topic == null && r.Entries.Count == 0)
                r.Guesses = Guess(q, topics);
            return r;
        }

        /// <summary>The items of the menu the question names (the target of the topic found is in its card, not here): the 6 best.</summary>
        public static List<SwimAssistantEntry> Items(string q, List<string> words, IList<SwimAssistantEntry> entries, double need, string skip)
        {
            List<KeyValuePair<SwimAssistantEntry, double>> found = new List<KeyValuePair<SwimAssistantEntry, double>>();
            if (entries == null)
                return new List<SwimAssistantEntry>();

            foreach (SwimAssistantEntry e in entries)
            {
                if (!string.IsNullOrEmpty(skip) && e.Name == skip)
                    continue;

                double total = 0;
                bool own = false;
                foreach (string w in words)
                {
                    double text = 0, tip = 0, path = 0;
                    foreach (string k in e.TextKeys)
                        text = Math.Max(text, SwimAssistantText.Match(w, k));
                    foreach (string k in e.TipKeys)
                        tip = Math.Max(tip, 0.6 * SwimAssistantText.Match(w, k));
                    foreach (string k in e.PathKeys)
                        path = Math.Max(path, 0.4 * SwimAssistantText.Match(w, k));
                    if (text >= 1.5)
                        own = true;
                    total += Math.Max(text, Math.Max(tip, path));
                }

                if (own && total >= need)
                    found.Add(new KeyValuePair<SwimAssistantEntry, double>(e, total));
            }

            return found.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Path.Length).Select(kv => kv.Key).Take(6).ToList();
        }

        /// <summary>«Μήπως ψάχνεις»: the topics with the most groups of letters of the question, else the ones asked most.</summary>
        public static List<SwimAssistantTopic> Guess(string q, IList<SwimAssistantTopic> topics)
        {
            List<SwimAssistantTopic> guesses = new List<SwimAssistantTopic>();
            if (topics == null || topics.Count == 0)
                return guesses;

            HashSet<string> grams = SwimAssistantText.Grams(q);
            var scored = topics.Select((t, i) =>
            {
                HashSet<string> tg = SwimAssistantText.Grams(string.Join(" ", t.SearchKeys));
                int score = grams.Count(g => tg.Contains(g));
                return new { Topic = t, Score = score, Index = i };
            }).Where(x => x.Score >= 2).OrderByDescending(x => x.Score).ThenBy(x => x.Index).Take(3);
            guesses.AddRange(scored.Select(x => x.Topic));

            foreach (string id in new[] { "package", "sync", "exportvideo", "racepanel" })
            {
                if (guesses.Count >= 3)
                    break;
                SwimAssistantTopic t = topics.FirstOrDefault(x => x.Id == id);
                if (t != null && !guesses.Contains(t))
                    guesses.Add(t);
            }

            return guesses;
        }
    }

    /// <summary>
    /// (2.26.0) What the assistant knows: the tasks of Kinovea Swim and where they are. The paths in its answers are those of the menu
    /// as it is (the items found by their names), the keys those of the settings now.
    /// </summary>
    internal static class SwimAssistantKnowledge
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static List<SwimAssistantTopic> topics;

        public static List<SwimAssistantTopic> Topics
        {
            get
            {
                if (topics == null)
                    topics = Build();
                return topics;
            }
        }

        public static string L(string el, string en)
        {
            return SwimLang.English ? en : el;
        }

        private static SwimAssistantTopic T(string id, string target, string el, string en)
        {
            return new SwimAssistantTopic { Id = id, Target = target, TitleEl = el, TitleEn = en };
        }

        private static string[] S(params string[] steps)
        {
            return steps;
        }

        private static List<SwimAssistantTopic> Build()
        {
            List<SwimAssistantTopic> list = new List<SwimAssistantTopic>
            {
                // ---- The tasks asked most (the chips of the panel first).
                T("package", "swimPackage", "Αποστολή στους προπονητές", "Send to the coaches")
                    .Chip("Αποστολή στον προπονητή", "Send to the coach")
                    .Lead("Φτιάχνει μαζί το PDF, το βίντεο με τα γραφικά και το μήνυμα, έτοιμα για το Smash.",
                        "Makes the PDF, the video with the graphics and the message together, ready for Smash.")
                    .Steps(S("Άνοιξε την ανάλυση του αθλητή (το βίντεο με τις μπάρες του).",
                            "Πάτα {path}.",
                            "Βιάζεσαι; «{t:⏩ Συνέχισε με άλλον αθλητή}»: η αποστολή τελειώνει μόνη της στο παρασκήνιο και η πρόοδος φαίνεται κάτω δεξιά.",
                            "Στο τέλος «{t:📤 Αποστολή με Smash}» και σύρε τα αρχεία στη σελίδα του Smash."),
                        S("Open the analysis of the athlete (the video with its bars).",
                            "Click {path}.",
                            "In a hurry? «{t:⏩ Συνέχισε με άλλον αθλητή}»: the package finishes by itself in the background; its progress shows at the bottom right.",
                            "At the end «{t:📤 Αποστολή με Smash}» and drag the files onto the Smash page."))
                    .Words("αποστολ, προπονητ, στειλ, στελν, πακετ, smash, email, mail, coach, coaches, send, package, μοιρασ, share")
                    .Need("analysis").See("pdf exportvideo sendsheet"),

                T("sync", "swimScreensGroup", "Ομάδα οθονών και συγχρονισμός", "Group of screens and synchronization")
                    .Chip("Συγχρονισμός οθονών", "Sync the screens")
                    .Lead("Οι κάμερες του ίδιου αγώνα γίνονται μία ομάδα: παίζουν μαζί και η ανάλυση είναι μία.",
                        "The cameras of the same race become one group: they play together and share one analysis.")
                    .Steps(S("Φόρτωσε τα βίντεο του ίδιου αγώνα σε 2 ή περισσότερες οθόνες ({path:swimScreens}).",
                            "Πάτα {path} και διάλεξε «{t:Όλες οι οθόνες}».",
                            "{t:Πρώτα συγχρονισμός στο σήμα εκκίνησης.} Ή «{t:Συγχρονισμός από τον ήχο}»."),
                        S("Load the videos of the same race in 2 or more screens ({path:swimScreens}).",
                            "Click {path} and choose «{t:Όλες οι οθόνες}».",
                            "{t:Πρώτα συγχρονισμός στο σήμα εκκίνησης.} Or «{t:Συγχρονισμός από τον ήχο}»."))
                    .Words("συγχρον, ομαδα, ομαδ, καμερεσ, καμερ, μαζι, sync, synchron, group, cameras, together, ηχο, sound, πολλεσ καμερεσ")
                    .Need("video").See("screens cameras"),

                T("exportvideo", "mnuExportVideoVideo", "Εξαγωγή βίντεο", "Export a video")
                    .Chip("Εξαγωγή βίντεο", "Export a video")
                    .Lead("Αποθηκεύει το βίντεο της οθόνης, με τα σχέδιά του, σε νέο αρχείο.",
                        "Saves the video of the screen, with its drawings, as a new file.")
                    .Steps(S("Κλικ στην οθόνη με το βίντεο.",
                            "Πάτα {path}.",
                            "Δώσε όνομα και φάκελο και πάτα Αποθήκευση.",
                            "Για το βίντεο με τα γραφικά της ανάλυσης για τους προπονητές: {path:swimPackage}."),
                        S("Click the screen with the video.",
                            "Click {path}.",
                            "Give a name and a folder and click Save.",
                            "For the video with the graphics of the analysis for the coaches: {path:swimPackage}."))
                    .Words("εξαγωγ, export, mp4, mkv, avi, αποθηκευση βιντεο, βιντεο αρχειο, save video, κοψιμο, cut")
                    .Need("video").See("sidebyside pauses package"),

                T("connection", "swimConnection", "Σύνδεση με το Google Sheet", "Connection with the Google Sheet")
                    .Chip("Σύνδεση με Google Sheet", "Google Sheet connection")
                    .Lead("Η διεύθυνση της εφαρμογής (…/exec) και το token, για να πηγαίνουν οι αναλύσεις στο φύλλο.",
                        "The address of the app (…/exec) and the token, so that the analyses go to the sheet.")
                    .Steps(S("Πάτα {path}.",
                            "Επικόλλησε τη διεύθυνση και το token, πάτα Αποθήκευση και μετά «{t:Δοκιμή σύνδεσης}».",
                            "Η κατάσταση φαίνεται πάνω δεξιά (● Google Sheet)· με κλικ ανοίγει η ίδια σελίδα."),
                        S("Click {path}.",
                            "Paste the address and the token, click Save, then «{t:Δοκιμή σύνδεσης}».",
                            "The state shows at the top right (● Google Sheet); a click on it opens the same page."))
                    .Words("google, sheet, sheets, συνδεσ, συνδε, token, exec, φυλλ, koe, app, connection, connect, apps script")
                    .See("sendsheet opensheet pdf"),

                T("racepanel", "swimRacePanel", "Ανάλυση αγώνα", "Race analysis")
                    .Chip("Ανάλυση αγώνα", "Race analysis")
                    .Lead("Ο πίνακας της ανάλυσης του βίντεο: στοιχεία, εκκίνηση, περάσματα, στροφές, τερματισμός.",
                        "The panel of the analysis of the video: details, start, splits, turns, finish.")
                    .Steps(S("Άνοιξε το βίντεο του αγώνα ({path:mnuOpenFile}).",
                            "Πάτα {path} ({key:SwimShowPanel}).",
                            "Στα «{t:Στοιχεία και αποτελέσματα}» συμπλήρωσε αθλητή και αγώνισμα· μετά σημάδευε με τα κουμπιά των μπαρών ή με τα πλήκτρα."),
                        S("Open the video of the race ({path:mnuOpenFile}).",
                            "Click {path} ({key:SwimShowPanel}).",
                            "In «{t:Στοιχεία και αποτελέσματα}» fill in the athlete and the event; then mark with the buttons of the bars or with the keys."))
                    .Words("αναλυσ, αγων, πινακ, μπαρ, race, analysis, panel, νεα αναλυση, ξεκινω, ξεκιν, start analysis")
                    .Need("video").See("newrace keys raceinfo"),

                T("keys", "mnuPreferences", "Πλήκτρα συντόμευσης", "Keyboard shortcuts")
                    .Chip("Πλήκτρα", "Keys")
                    .Lead("Τα πλήκτρα όπως είναι ρυθμισμένα τώρα. Αλλάζουν στο {path}, καρτέλα «Πληκτρολόγιο».",
                        "The keys as they are set now. They change in {path}, «Keyboard» tab.")
                    .Words("πληκτρ, συντομευσ, ctrl, shortcut, shortcuts, keys, key, keyboard, hotkey, πληκτρολογ, κουμπια πληκτρολογιου")
                    .Keyboard().OnlyShow().See("racepanel"),

                // ---- The analysis of a race.
                T("newrace", "swimNewAthlete", "Δεύτερος αθλητής στο ίδιο βίντεο", "Another swimmer in the same video")
                    .Lead("Νέα ανάλυση στο ίδιο βίντεο, με το ίδιο σήμα εκκίνησης.", "A new analysis in the same video, with the same start signal.")
                    .Steps(S("Με ανοιχτή την ανάλυση του πρώτου αθλητή, πάτα {path}.", "Συμπλήρωσε το όνομα του αθλητή και τη διαδρομή του."),
                        S("With the analysis of the first athlete open, click {path}.", "Fill in the name of the athlete and the lane."))
                    .Words("νεοσ αθλητ, αλλοσ αθλητ, δευτερ, ιδιο βιντεο, new athlete, another, swimmer, second, αθλητ, αθλητη")
                    .Need("analysis").See("racepanel raceinfo"),

                T("raceinfo", "swimRaceInfo", "Στοιχεία και αποτελέσματα", "Details and results")
                    .Lead("Τα στοιχεία του αθλητή και του αγωνίσματος και τα αποτελέσματα.", "The details of the athlete and of the event, and the results.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("στοιχει, αποτελεσμ, αγωνισμ, ονομα, διαδρομ, details, results, event, name, lane, χρονοσ αγωνα")
                    .Need("video").See("racepanel finals"),

                T("start", "swimRaceStart", "Εκκίνηση", "Start")
                    .Lead("Σήμα, αντίδραση, αποδέσμευση, είσοδος, δελφινισμοί και έξοδος της εκκίνησης.",
                        "Signal, reaction, block, entry, kicks and breakout of the start.")
                    .Steps(S("Πάτα {path}.",
                            "Σημάδευε με τα κουμπιά της μπάρας ή με τα πλήκτρα: {key:SwimStart} σήμα, {key:SwimReaction} αντίδραση, {key:SwimFeetOff} αποδέσμευση, {key:SwimEntry} είσοδος, {key:SwimKick} δελφινισμός, {key:SwimBreakout} έξοδος."),
                        S("Click {path}.",
                            "Mark with the buttons of the bar or with the keys: {key:SwimStart} signal, {key:SwimReaction} reaction, {key:SwimFeetOff} block, {key:SwimEntry} entry, {key:SwimKick} kick, {key:SwimBreakout} breakout."))
                    .Words("εκκινησ, σημα, αντιδρασ, αποδεσμευσ, βατηρ, start, reaction, block, dive, βουτια, signal")
                    .Need("video").See("turns swim techpanel"),

                T("swim", "swimRaceSwim", "Κολύμβηση · περάσματα", "Swimming · splits")
                    .Lead("Τα περάσματα (splits) και οι χεριές του αγώνα.", "The splits and the strokes of the race.")
                    .Steps(S("Πάτα {path}.", "{key:SwimSplit} πέρασμα, {key:SwimStroke} χεριά."),
                        S("Click {path}.", "{key:SwimSplit} split, {key:SwimStroke} stroke."))
                    .Words("περασμ, split, splits, χεριεσ, χερια, stroke, strokes, κολυμβησ, ενδιαμεσ, συχνοτητ, rate, ρυθμοσ")
                    .Need("video").See("turns finish"),

                T("turns", "swimRaceTurns", "Στροφές", "Turns")
                    .Lead("Η αποκόλληση, οι δελφινισμοί και η έξοδος μετά από κάθε τοίχο.", "The push-off, the kicks and the breakout after each wall.")
                    .Steps(S("Πάτα {path}.", "{key:SwimFeetOff} αποκόλληση, {key:SwimKick} δελφινισμός, {key:SwimBreakout} έξοδος."),
                        S("Click {path}.", "{key:SwimFeetOff} push-off, {key:SwimKick} kick, {key:SwimBreakout} breakout."))
                    .Words("στροφ, turn, turns, τοιχ, wall, αποκολλησ, push off, ταχυτητα στροφησ")
                    .Need("video").See("swim techpanel"),

                T("finish", "swimRaceFinish", "Τερματισμός και ΔΑΚ", "Finish and ΔΑΚ")
                    .Lead("Ο τερματισμός και ο ΔΑΚ του αγώνα.", "The finish and the ΔΑΚ of the race.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("τερματισμ, δακ, finish, τελοσ, αφιξ, τερμα")
                    .Need("video").See("swim raceinfo"),

                T("finals", "swimFinals", "Σύγκριση με τελικό", "Comparison with a final")
                    .Lead("Ο αγώνας δίπλα στους 8 του τελικού του αγωνίσματος (π.χ. ολυμπιακοί αγώνες).",
                        "The race next to the 8 of the final of the event (e.g. the Olympic Games).")
                    .Steps(S("Πάτα {path}.", "Διάλεξε τη διοργάνωση, όταν υπάρχουν πολλές."), S("Click {path}.", "Choose the competition, when there are several."))
                    .Words("τελικ, συγκρισ, final, finals, compare, comparison, ολυμπιακ, olympic, κορυφαι")
                    .Need("video").See("raceinfo"),

                T("aipro", "swimRaceAi", "AI pro · ανάλυση με τον Claude", "AI pro · analysis with Claude")
                    .Lead("Ο Claude σχολιάζει τον αγώνα για την αναφορά και αξιολογεί την εκκίνηση και τις στροφές με τις λίστες σου (με κωδικό).",
                        "Claude comments on the race for the report and evaluates the start and the turns with your lists (with a password).")
                    .Steps(S("Πάτα {path}.", "Δώσε τον κωδικό του AI pro και διάλεξε τι θα αναλυθεί."),
                        S("Click {path}.", "Give the password of AI pro and choose what to analyse."))
                    .Words("ai, claude, τεχνητ, νοημοσυν, σχολι, comment, artificial, intelligence")
                    .Need("analysis").See("techeval racepanel"),

                T("closeanalysis", "swimCloseAnalysis", "Κλείσιμο ανάλυσης", "Close the analysis")
                    .Lead("Κλείνουν οι μπάρες και ο πίνακας· η ανάλυση μένει αποθηκευμένη στο βίντεο.",
                        "The bars and the panel close; the analysis stays saved with the video.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("κλεισιμο, κλεισ, close, κρυψ, hide, μπαρεσ")
                    .Need("analysis").OnlyShow().See("racepanel"),

                T("deleteanalysis", "swimDeleteAnalysis", "Διαγραφή ανάλυσης", "Delete the analysis")
                    .Lead("Σβήνει την ανάλυση από το βίντεο (ρωτά πρώτα). Το φύλλο στο Google Sheet δεν διαγράφεται.",
                        "Deletes the analysis from the video (it asks first). The sheet in the Google Sheet is not deleted.")
                    .Steps(S("Πάτα {path} και απάντησε στην ερώτηση."), S("Click {path} and answer the question."))
                    .Words("διαγραφ, σβησ, delete, remove, erase")
                    .Need("analysis").OnlyShow().See("closeanalysis"),

                // ---- The technical analysis.
                T("techpanel", "swimTechPanel", "Τεχνική ανάλυση", "Technical analysis")
                    .Lead("Εκκινήσεις και στροφές καρέ-καρέ, στιγμιότυπα, γωνίες και αξιολόγηση.",
                        "Starts and turns frame by frame, snapshots, angles and evaluation.")
                    .Steps(S("Πάτα {path}.", "Οι σελίδες του είναι και στο μενού: Εκκινήσεις, Στροφές, Στιγμιότυπα και γωνίες, Αξιολόγηση."),
                        S("Click {path}.", "Its pages are also in the menu: Starts, Turns, Snapshots and angles, Evaluation."))
                    .Words("τεχνικ, technique, technical, tech, καρε καρε, εκκινησεισ, προπονησ")
                    .Need("video").See("snapshots techeval aipro"),

                T("snapshots", "swimTechMedia", "Στιγμιότυπα και γωνίες", "Snapshots and angles")
                    .Lead("Στιγμιότυπα του καρέ για το report και μετρήσεις γωνιών με κλικ στο βίντεο.",
                        "Snapshots of the frame for the report and angles measured with clicks on the video.")
                    .Steps(S("Πάτα {path}.", "{key:SwimSnapshot} στιγμιότυπο, {key:SwimAngle} γωνία."), S("Click {path}.", "{key:SwimSnapshot} snapshot, {key:SwimAngle} angle."))
                    .Words("στιγμιοτυπ, φωτογραφ, snapshot, γωνι, angle, μετρησ γωνιασ, μοιρεσ")
                    .Need("video").See("techpanel exportimage"),

                T("techeval", "swimTechEval", "Αξιολόγηση τεχνικής", "Evaluation of the technique")
                    .Lead("Η λίστα αξιολόγησης της εκκίνησης ή της στροφής.", "The evaluation list of the start or of the turn.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("αξιολογησ, evaluation, evaluate, λιστα, checklist, βαθμολογ")
                    .Need("video").See("techpanel aipro"),

                // ---- The report.
                T("pdf", "swimPdf", "PDF report", "PDF report")
                    .Lead("Η αναφορά του αγώνα σε PDF. Γίνεται στο Google Sheet, οπότε θέλει σύνδεση.",
                        "The report of the race as a PDF. It is made in the Google Sheet, so it needs the connection.")
                    .Steps(S("Πάτα {path}.", "Χωρίς σύνδεση: {path:swimConnection}."), S("Click {path}.", "Without a connection: {path:swimConnection}."))
                    .Words("pdf, αναφορ, report, εκτυπωσ, print, χαρτι")
                    .Need("analysis").See("package connection"),

                T("sendsheet", "swimSendSheet", "Αποστολή στο Google Sheet", "Send to the Google Sheet")
                    .Lead("Στέλνει τώρα την ανάλυση στο φύλλο της.", "Sends the analysis to its sheet now.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("αποστολη στο sheet, ανεβασ, upload, send sheet, sheet, στειλε στο φυλλο")
                    .Need("analysis").See("opensheet connection"),

                T("opensheet", "swimOpenSheet", "Άνοιγμα φύλλου", "Open the sheet")
                    .Lead("Ανοίγει στον browser το φύλλο της ανάλυσης.", "Opens the sheet of the analysis in the browser.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("ανοιγμα φυλλου, open sheet, browser, φυλλο, sheet")
                    .Need("analysis").See("sendsheet"),

                T("sidebyside", "mnuExportVideoSideBySide", "Βίντεο δίπλα-δίπλα", "Side-by-side video")
                    .Lead("Ένα βίντεο με δύο οθόνες μαζί, για σύγκριση.", "One video with two screens together, to compare.")
                    .Steps(S("Φόρτωσε τα δύο βίντεο σε δύο οθόνες.", "Πάτα {path}."), S("Load the two videos in two screens.", "Click {path}."))
                    .Words("διπλα, side by side, δυο βιντεο σε ενα, συγκριση βιντεο, compare videos")
                    .Need("video").See("exportvideo sync"),

                T("pauses", "mnuExportVideoWithPauses", "Βίντεο με παύσεις", "Video with pauses")
                    .Lead("Βίντεο που σταματά στα βασικά καρέ.", "A video that stops at the key images.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("παυσ, pauses, παγωμ, freeze, σταματα")
                    .Need("video").See("exportvideo"),

                T("exportimage", "mnuExportImageImage", "Εξαγωγή εικόνας (καρέ)", "Export an image (frame)")
                    .Lead("Αποθηκεύει το καρέ που βλέπεις ως εικόνα.", "Saves the frame you see as an image.")
                    .Steps(S("Πήγαινε στο καρέ.", "Πάτα {path}."), S("Go to the frame.", "Click {path}."))
                    .Words("εικονα, καρε, φωτογραφια, screenshot, image, frame, jpg, png, εξαγωγη εικονασ")
                    .Need("video").See("snapshots exportvideo"),

                T("exportdata", "mnuExportSpreadsheet", "Δεδομένα σε Excel, CSV, JSON", "Data to Excel, CSV, JSON")
                    .Lead("Τα δεδομένα των σχεδίων του Kinovea σε πίνακα.", "The data of the drawings of Kinovea in a table.")
                    .Steps(S("Πάτα {path} και διάλεξε τη μορφή."), S("Click {path} and choose the format."))
                    .Words("excel, csv, json, δεδομεν, data, spreadsheet, xlsx, λογιστικο φυλλο")
                    .Need("video").See("exportdoc"),

                T("exportdoc", "mnuExportDocument", "Έγγραφο Word, Writer, Markdown", "Word, Writer or Markdown document")
                    .Lead("Οι σημειώσεις και τα καρέ του βίντεο σε έγγραφο.", "The notes and the key images of the video in a document.")
                    .Steps(S("Πάτα {path} και διάλεξε τη μορφή."), S("Click {path} and choose the format."))
                    .Words("word, εγγραφο, docx, writer, markdown, document, odt")
                    .Need("video").See("exportdata"),

                // ---- The screens and what is on the video.
                T("screens", "swimScreens", "Πολλά βίντεο μαζί (οθόνες)", "Several videos together (screens)")
                    .Lead("Από 1 έως 5 βίντεο ή κάμερες, το ένα δίπλα στο άλλο.", "From 1 to 5 videos or cameras, side by side.")
                    .Steps(S("Πάτα {path} και διάλεξε πόσα βίντεο.", "Σύρε κάθε βίντεο στη δική του οθόνη."),
                        S("Click {path} and choose how many videos.", "Drag each video into its own screen."))
                    .Words("οθον, screens, screen, δυο βιντεο, πολλα βιντεο, 2 βιντεο, split screen, τεσσερα")
                    .See("sync cameras bigscreens"),

                T("cameras", "mnuOneCapture", "Κάμερα ζωντανά", "Live camera")
                    .Lead("Η εικόνα της κάμερας ζωντανά, με εγγραφή.", "The image of the camera live, with recording.")
                    .Steps(S("Πάτα {path} (ή 2 έως 5 κάμερες από το ίδιο μενού)."), S("Click {path} (or 2 to 5 cameras from the same menu)."))
                    .Words("καμερα, ζωντανα, live, capture, εγγραφ, record, webcam, usb")
                    .See("gopro screens"),

                T("gopro", "mnuGoProScreens", "GoPro σε οθόνες (Bluetooth)", "GoPro in screens (Bluetooth)")
                    .Lead("Οι GoPro που βρίσκονται με Bluetooth, η καθεμία στη δική της οθόνη.", "The GoPro cameras found by Bluetooth, each in its own screen.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("gopro, bluetooth, go pro")
                    .See("cameras"),

                T("fullscreen", "mnuFullScreen", "Πλήρης οθόνη", "Full screen")
                    .Lead("Μόνο οι εικόνες των οθονών, σε όλη την οθόνη. Επιστροφή με F11 ή Esc.",
                        "Only the images of the screens, on the whole screen. Back with F11 or Esc.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("πληρησ οθονη, full screen, fullscreen, f11, ολη την οθονη, μεγαλωμα")
                    .See("bigscreens dial"),

                T("bigscreens", "swimBigScreens", "Μεγάλες οθόνες", "Big screens")
                    .Lead("Οι οθόνες δείχνουν μόνο τις εικόνες τους· τα μενού μένουν.", "The screens show only their images; the menus stay.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("μεγαλεσ οθονεσ, big screens, μονο εικονεσ, μεγαλ")
                    .Need("video").See("fullscreen"),

                T("graphics", "swimGraphics", "Γραφικά της ανάλυσης στο βίντεο", "Graphics of the analysis on the video")
                    .Lead("Δείχνει ή κρύβει τα γραφικά της ανάλυσης πάνω στο βίντεο ({key:SwimGraphics}).",
                        "Shows or hides the graphics of the analysis on the video ({key:SwimGraphics}).")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("γραφικ, graphics, overlay, κρυψ, εμφανισ, πανω στο βιντεο")
                    .See("hud reel"),

                T("hud", "swimHud", "HUD ή πινακάκια", "HUD or small panels")
                    .Lead("Ο τρόπος που δείχνονται οι τιμές πάνω στο βίντεο.", "How the values are shown on the video.")
                    .Steps(S("Πάτα {path} και διάλεξε."), S("Click {path} and choose."))
                    .Words("hud, πινακακι, demo, panels, ταμπλο")
                    .See("graphics reel"),

                T("reel", "swimReel", "Τεχνική στο βίντεο (reel)", "Technique on the video (reel)")
                    .Lead("Η τεχνική εικόνα του αγώνα πάνω στο βίντεο, όπως στα reels.", "The technical view of the race on the video, as in the reels.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("reel, reels, τεχνικη στο βιντεο")
                    .See("hud graphics"),

                T("dial", "swimDial", "Στρογγυλό κουμπί", "Round button")
                    .Lead("Στην πλήρη οθόνη, όταν δεν χωρούν οι μπάρες δίπλα στο βίντεο: το στρογγυλό κουμπί πάνω στο βίντεο ({key:SwimDial}).",
                        "In full screen, when the bars do not fit next to the video: the round button on the video ({key:SwimDial}).")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("στρογγυλ, κουμπι, dial, κυκλ, round")
                    .See("fullscreen"),

                T("commonctrls", "mnuToggleCommonCtrls", "Κοινά χειριστήρια", "Common controls")
                    .Lead("Η μπάρα κάτω από τις οθόνες που τις παίζει όλες μαζί.", "The bar under the screens that plays them all together.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("κοινα χειριστηρι, common controls, μπαρα κατω, αναπαραγωγη ολων")
                    .See("sync"),

                T("filelist", "mnuToggleNavigationPane", "Λίστα αρχείων", "File list")
                    .Lead("Τα αρχεία και οι φάκελοι αριστερά.", "The files and the folders on the left.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("λιστα αρχειων, φακελ, explorer, αριστερα, navigation, browser αρχειων")
                    .See("open recent"),

                T("image", "swimPicture", "Εικόνα: περιστροφή, καθρέφτης, αναλογία", "Image: rotation, mirror, aspect ratio")
                    .Lead("Γύρισμα, καθρέφτης και αναλογία της εικόνας του βίντεο.", "Rotation, mirror and aspect ratio of the image of the video.")
                    .Steps(S("Πάτα {path} και διάλεξε."), S("Click {path} and choose."))
                    .Words("περιστροφ, καθρεφτ, αναλογ, rotate, rotation, mirror, aspect, γυρισ, αναποδα, ανεστραμμεν")
                    .Need("video").See("filters"),

                T("filters", "swimFilters", "Φίλτρο βίντεο (κινόγραμμα)", "Video filter (kinogram)")
                    .Lead("Κινόγραμμα και άλλα φίλτρα της εικόνας.", "Kinogram and other filters of the image.")
                    .Steps(S("Πάτα {path} και διάλεξε."), S("Click {path} and choose."))
                    .Words("φιλτρ, κινογραμμ, kinogram, filter, filters")
                    .Need("video").See("image"),

                // ---- Files, drawings, the window.
                T("open", "mnuOpenFile", "Άνοιγμα βίντεο", "Open a video")
                    .Lead("Ανοίγει ένα βίντεο. Μπορείς και να το σύρεις με το ποντίκι μέσα στο παράθυρο.",
                        "Opens a video. You can also drag it with the mouse into the window.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("ανοιγ, ανοιξ, ανοιγμα, open, φορτω, load, αρχειο, file")
                    .See("recent filelist screens"),

                T("recent", "mnuHistory", "Πρόσφατα βίντεο", "Recent videos")
                    .Lead("Τα βίντεο που άνοιξες τελευταία.", "The videos you opened last.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("προσφατ, recent, ιστορικ, history, τελευται")
                    .See("open"),

                T("watcher", "mnuOpenReplayWatcher", "Παρακολούθηση φακέλου", "Watch a folder")
                    .Lead("Ανοίγει μόνο του το νεότερο βίντεο ενός φακέλου, για επανάληψη αμέσως μετά την εγγραφή.",
                        "Opens by itself the newest video of a folder, for a replay right after the recording.")
                    .Steps(S("Πάτα {path} και διάλεξε τον φάκελο."), S("Click {path} and choose the folder."))
                    .Words("παρακολουθ, replay, observer, watch, folder, νεο βιντεο, αυτοματ")
                    .See("open cameras"),

                T("save", "mnuSave", "Αποθήκευση", "Save")
                    .Lead("Η ανάλυση κολύμβησης αποθηκεύεται μόνη της δίπλα στο βίντεο (αρχείο .swim.json). Η Αποθήκευση κρατά τα σχέδια και τις σημειώσεις του Kinovea.",
                        "The swim analysis is saved by itself next to the video (a .swim.json file). Save keeps the drawings and the notes of Kinovea.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("αποθηκευσ, save, σωσ, σωζ, κρατησ, χαθει, saved")
                    .Need("video").See("undo"),

                T("undo", "mnuUndo", "Αναίρεση", "Undo")
                    .Lead("Αναίρεση και επανάληψη των σχεδίων. Για τα σημάδια της ανάλυσης: {key:SwimUndo}.",
                        "Undo and redo of the drawings. For the marks of the analysis: {key:SwimUndo}.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("αναιρεσ, undo, επαναληψ, redo, λαθοσ, σβησε σημαδι, πισω")
                    .OnlyShow().See("save"),

                T("drawings", "mnuCopyDrawing", "Σχέδια: αντιγραφή και επικόλληση", "Drawings: copy and paste")
                    .Lead("Αντιγραφή, αποκοπή και επικόλληση ενός σχεδίου του Kinovea.", "Copy, cut and paste a drawing of Kinovea.")
                    .Steps(S("Διάλεξε το σχέδιο στο βίντεο.", "Πάτα {path}."), S("Select the drawing on the video.", "Click {path}."))
                    .Words("σχεδι, drawing, drawings, αντιγραφ, επικολλησ, copy, paste, αποκοπ, cut")
                    .Need("video").OnlyShow().See("undo"),

                T("calibration", "mnuCoordinateSystem", "Βαθμονόμηση", "Calibration")
                    .Lead("Αποστάσεις σε μέτρα στο βίντεο· για βίντεο υψηλής ταχύτητας και η βαθμονόμηση χρόνου ({path:mnuTimeCalibration}).",
                        "Distances in meters on the video; for high speed videos also the time calibration ({path:mnuTimeCalibration}).")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("βαθμονομησ, calibration, calibrate, αποστασ, μετρα, κλιμακ, scale, συντεταγμεν, slow motion, fps, high speed")
                    .Need("video").See("diagrams"),

                T("diagrams", "mnuLinearKinematics", "Διαγράμματα κινηματικής", "Kinematics diagrams")
                    .Lead("Θέση, ταχύτητα και επιτάχυνση των σημείων που ακολουθείς.", "Position, speed and acceleration of the points you track.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("διαγραμμ, γραφημ, chart, kinematics, κινηματικ, τροχι, trajectory, επιταχυνσ")
                    .Need("video").See("calibration"),

                // ---- Settings and help.
                T("language", "swimLangEn", "Γλώσσα: ελληνικά ή αγγλικά", "Language: Greek or English")
                    .Lead("Τα ΕΛ και EN πάνω δεξιά αλλάζουν τη γλώσσα της ανάλυσης και του Kinovea.",
                        "ΕΛ and EN at the top right change the language of the analysis and of Kinovea.")
                    .Steps(S("Πάτα ΕΛ ή EN πάνω δεξιά."), S("Click ΕΛ or EN at the top right."))
                    .Words("γλωσσ, αγγλικ, ελληνικ, english, greek, language, μεταφρασ")
                    .See("classic"),

                T("classic", "swimClassic", "Κλασικό μενού του Kinovea", "Classic menu of Kinovea")
                    .Lead("Το παλιό μενού του Kinovea, από το επόμενο άνοιγμα του προγράμματος.", "The old menu of Kinovea, from the next start of the program.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("κλασικ, classic, παλιο μενου, old menu")
                    .OnlyShow().See("language"),

                T("preferences", "mnuPreferences", "Προτιμήσεις", "Preferences")
                    .Lead("Οι ρυθμίσεις του Kinovea (και τα πλήκτρα, στην καρτέλα «Πληκτρολόγιο»).", "The settings of Kinovea (and the keys, in the «Keyboard» tab).")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("προτιμησ, ρυθμισ, preferences, settings, options")
                    .See("keys"),

                T("help", "mnuHelpContents", "Οδηγός του Kinovea", "Guide of Kinovea")
                    .Lead("Ο οδηγός χρήσης του Kinovea στο internet.", "The user guide of Kinovea on the internet.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("οδηγ, manual, εγχειριδ, guide, documentation")
                    .See("about"),

                T("about", "mnuAbout", "Έκδοση του Kinovea", "Version of Kinovea")
                    .Lead("Η έκδοση του Kinovea. Η έκδοση της ανάλυσης φαίνεται στο HUD.", "The version of Kinovea. The version of the analysis shows in the HUD.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("εκδοσ, version, περι, about")
                    .See("help"),

                T("settingsfolder", "mnuApplicationFolder", "Φάκελος ρυθμίσεων", "Settings folder")
                    .Lead("Ο φάκελος με τις ρυθμίσεις και τα αρχεία καταγραφής (log) του Kinovea.", "The folder with the settings and the logs of Kinovea.")
                    .Steps(S("Πάτα {path}."), S("Click {path}."))
                    .Words("φακελοσ ρυθμισεων, appdata, settings folder, log, logs, καταγραφ")
                    .See("preferences"),

                T("assistant", "swimAssistant", "Ο ψηφιακός βοηθός", "The digital assistant")
                    .Lead("Γράψε τι θέλεις να κάνεις: ο βοηθός λέει τα βήματα και δείχνει πού είναι στο μενού. Ανοίγει και κλείνει με F1.",
                        "Write what you want to do: the assistant gives the steps and shows where it is in the menu. F1 opens and closes it.")
                    .Steps(S("Πάτα {path} ή το ΒΟΗΘΟΣ πάνω δεξιά."), S("Click {path} or ASSISTANT at the top right."))
                    .Words("βοηθ, assistant, help me, f1")
                    .OnlyShow().See("keys")
            };

            return list;
        }

        public static SwimAssistantTopic Find(string id)
        {
            return Topics.FirstOrDefault(t => t.Id == id);
        }

        private static readonly Regex Hole = new Regex(@"\{(path|key|t)(?::([^}]*))?\}", RegexOptions.CultureInvariant);

        /// <summary>A text of a topic with its paths, its keys and the texts of the program as they are now.</summary>
        public static string Expand(string text, SwimAssistantTopic topic, Func<string, string> pathOf)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            return Hole.Replace(text, m =>
            {
                string kind = m.Groups[1].Value;
                string arg = m.Groups[2].Success ? m.Groups[2].Value : null;
                switch (kind)
                {
                    case "path":
                        string name = arg ?? (topic != null ? topic.Target : null);
                        string p = name != null && pathOf != null ? pathOf(name) : null;
                        return string.IsNullOrEmpty(p) ? (arg == null && topic != null ? topic.Title : "") : p;
                    case "key":
                        return VideoKey(arg) ?? "—";
                    default:
                        return SwimLang.T(arg ?? "");
                }
            });
        }

        /// <summary>The key of a command of the video in the settings of Kinovea now ("G", "Ctrl+Shift+W"), or null.</summary>
        public static string VideoKey(string command)
        {
            if (string.IsNullOrEmpty(command))
                return null;

            try
            {
                PlayerScreenCommands c;
                if (!Enum.TryParse(command, out c))
                    return null;

                HotkeyCommand[] hotkeys = HotkeySettingsManager.LoadHotkeys("PlayerScreen");
                if (hotkeys == null)
                    return null;

                foreach (HotkeyCommand h in hotkeys)
                {
                    if (h != null && h.KeyData != Keys.None && h.CommandCode == (int)c)
                        return h.KeyData == Keys.Back ? "Backspace" : new KeysConverter().ConvertToInvariantString(h.KeyData);
                }
            }
            catch (Exception e)
            {
                log.Error("Could not read the keys of the player screen for the assistant.", e);
            }

            return null;
        }

        /// <summary>The keys of the program now: those of the menus (their items) and those of the video (its settings).</summary>
        public static List<KeyValuePair<string, string>> KeyLines(Func<string, string> menuKey)
        {
            List<KeyValuePair<string, string>> lines = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (key, what) =>
            {
                if (!string.IsNullOrEmpty(key))
                    lines.Add(new KeyValuePair<string, string>(key, what));
            };

            if (menuKey != null)
            {
                add(menuKey("swimAssistant"), L("ο βοηθός", "the assistant"));
                add(menuKey("mnuOpenFile"), L("άνοιγμα βίντεο", "open a video"));
                add(menuKey("mnuSave"), L("αποθήκευση των σχεδίων", "save the drawings"));
                add(menuKey("mnuUndo"), L("αναίρεση", "undo"));
                add(menuKey("mnuRedo"), L("επανάληψη", "redo"));
                add(menuKey("mnuFullScreen"), L("πλήρης οθόνη", "full screen"));
                add(menuKey("mnuToggleNavigationPane"), L("λίστα αρχείων", "file list"));
                add(menuKey("mnuToggleCommonCtrls"), L("κοινά χειριστήρια", "common controls"));
            }

            string[,] video = new string[,]
            {
                { "TogglePlay", "αναπαραγωγή / παύση", "play / pause" },
                { "GotoPreviousImage", "καρέ πίσω", "previous frame" },
                { "GotoNextImage", "καρέ μπροστά", "next frame" },
                { "SwimShowPanel", "πίνακας ανάλυσης", "analysis panel" },
                { "SwimStart", "σήμα εκκίνησης", "start signal" },
                { "SwimReaction", "αντίδραση", "reaction" },
                { "SwimFeetOff", "αποδέσμευση / αποκόλληση", "block / push-off" },
                { "SwimEntry", "είσοδος χεριών", "hands entry" },
                { "SwimKick", "δελφινισμός", "kick" },
                { "SwimBreakout", "έξοδος", "breakout" },
                { "SwimStroke", "χεριά", "stroke" },
                { "SwimSplit", "πέρασμα", "split" },
                { "SwimWallContact", "επαφή με τον τοίχο (τεχνική)", "wall contact (technique)" },
                { "SwimRotation", "περιστροφή (τεχνική)", "rotation (technique)" },
                { "SwimTechSplit", "κεφάλι στα 5 / 10 / 15 μ (τεχνική)", "head at 5 / 10 / 15 m (technique)" },
                { "SwimSnapshot", "στιγμιότυπο", "snapshot" },
                { "SwimAngle", "γωνία", "angle" },
                { "SwimUndo", "αναίρεση σημαδιού", "undo a mark" },
                { "SwimGraphics", "γραφικά στο βίντεο", "graphics on the video" },
                { "SwimDial", "στρογγυλό κουμπί", "round button" }
            };

            for (int i = 0; i < video.GetLength(0); i++)
                add(VideoKey(video[i, 0]), L(video[i, 1], video[i, 2]));

            return lines;
        }
    }

    /// <summary>
    /// (2.26.0) A free question to Claude (AI pro, with its password and its key): the question and the map of the menu (names of
    /// menus and of commands, nothing of an analysis or of a file); the answer in JSON, with the paths it names.
    /// </summary>
    internal static class SwimAssistantAi
    {
        public const int AnswerTokens = 900;

        public static string SystemPrompt(IList<SwimAssistantEntry> entries, IList<SwimAssistantTopic> topics, Func<string, string> pathOf, Func<string, string> menuKey)
        {
            bool english = SwimLang.English;
            StringBuilder b = new StringBuilder();
            b.AppendLine("You are the digital assistant inside Kinovea Swim: Kinovea (video analysis) with the swimming race analysis of the Hellenic Swimming Federation (bars on the video, a panel of the analysis, a report in a Google Sheet, packages for the coaches).");
            b.AppendLine("Answer the question of the user about how to do something in the program and where to find it.");
            b.AppendLine("Rules:");
            b.AppendLine(english ? "- Answer in English." : "- Answer in Greek (Ελληνικά).");
            b.AppendLine("- Short: one or two sentences, then numbered steps, each on its own line.");
            b.AppendLine("- Name the menu paths exactly as in the MENU list, with \" › \" between their levels.");
            b.AppendLine("- Only the commands of the MENU list and the TOPICS: if the program cannot do it, say so; never invent a button or a menu.");
            b.AppendLine("- Reply with only a JSON object: {\"answer\": \"<the answer>\", \"paths\": [\"<each menu path you named, exactly as in the MENU list>\"]}.");
            b.AppendLine();
            b.AppendLine("MENU (path | key | tip | enabled now):");
            if (entries != null)
            {
                foreach (SwimAssistantEntry e in entries)
                {
                    b.Append(e.Path);
                    if (!string.IsNullOrEmpty(e.Shortcut))
                        b.Append(" | ").Append(e.Shortcut);
                    if (!string.IsNullOrEmpty(e.Tip))
                        b.Append(" | ").Append(e.Tip);
                    if (!e.Enabled)
                        b.Append(" | (disabled now)");
                    b.AppendLine();
                }
            }

            b.AppendLine();
            b.AppendLine("TOPICS:");
            if (topics != null)
            {
                foreach (SwimAssistantTopic t in topics)
                {
                    b.Append("- ").Append(t.Title).Append(": ").Append(SwimAssistantKnowledge.Expand(t.LeadText, t, pathOf));
                    foreach (string s in t.StepTexts)
                        b.Append(" / ").Append(SwimAssistantKnowledge.Expand(s, t, pathOf));
                    b.AppendLine();
                }
            }

            b.AppendLine();
            b.AppendLine("KEYS NOW:");
            foreach (KeyValuePair<string, string> kv in SwimAssistantKnowledge.KeyLines(menuKey))
                b.Append(kv.Key).Append(" = ").AppendLine(kv.Value);

            return b.ToString();
        }

        public static JObject Body(string question, string model, string system)
        {
            return new JObject
            {
                ["model"] = model,
                ["max_tokens"] = AnswerTokens,
                ["system"] = system,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = question ?? ""
                    }
                }
            };
        }

        /// <summary>The answer of Claude (its JSON, else its text when it gave plain text), or null.</summary>
        public static string AnswerOf(SwimAiReply reply)
        {
            if (reply == null)
                return null;

            JObject o = reply.Input;
            string answer = o != null ? (string)o["answer"] : null;
            if (string.IsNullOrWhiteSpace(answer) && !string.IsNullOrWhiteSpace(reply.Text) && reply.StopReason == "end_turn")
                answer = reply.Text;
            return string.IsNullOrWhiteSpace(answer) ? null : answer.Trim();
        }

        /// <summary>The items of the menu whose paths the answer names (its "paths", else the paths written in its text).</summary>
        public static List<SwimAssistantEntry> PathsOf(SwimAiReply reply, string answer, IList<SwimAssistantEntry> entries)
        {
            List<SwimAssistantEntry> found = new List<SwimAssistantEntry>();
            if (entries == null)
                return found;

            HashSet<string> named = new HashSet<string>(StringComparer.Ordinal);
            JArray paths = reply != null && reply.Input != null ? reply.Input["paths"] as JArray : null;
            if (paths != null)
            {
                foreach (JToken p in paths)
                {
                    string s = (string)p;
                    if (!string.IsNullOrWhiteSpace(s))
                        named.Add(s.Trim());
                }
            }

            foreach (SwimAssistantEntry e in entries)
            {
                if (found.Count >= 6)
                    break;
                if (named.Contains(e.Path) || (!string.IsNullOrEmpty(answer) && e.Path.IndexOf(SwimAssistantMenuMap.Arrow, StringComparison.Ordinal) > 0 &&
                    answer.IndexOf(e.Path, StringComparison.Ordinal) >= 0))
                {
                    found.Add(e);
                }
            }

            return found;
        }
    }

    /// <summary>
    /// (2.26.0) The sign of the assistant, in the style of the HUD: the ring in four arcs with its marks, a hexagon and a spark in the
    /// middle, a dot on the ring (amber: something new). Drawn by code at any size (only the ring and the spark below 19 pixels).
    /// </summary>
    internal static class SwimAssistantIcon
    {
        private static readonly PointF[] Hexagon =
        {
            new PointF(32f, 15f), new PointF(46.72f, 23.5f), new PointF(46.72f, 40.5f),
            new PointF(32f, 49f), new PointF(17.28f, 40.5f), new PointF(17.28f, 23.5f)
        };

        /// <param name="turn">The angle of the ring and of its dot (degrees, it turns while the assistant is open).</param>
        /// <param name="dot">The colour of the dot on the ring (null: none).</param>
        /// <param name="marks">The four marks of the ring (the big sign).</param>
        /// <param name="glow">0 to 1: the light around the ring.</param>
        public static void Draw(Graphics g, RectangleF r, float turn, Color? dot, bool marks, float glow)
        {
            float size = Math.Min(r.Width, r.Height);
            if (g == null || size < 6)
                return;

            GraphicsState state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float s = size / 64f;
                g.TranslateTransform(r.X + (r.Width - size) / 2f, r.Y + (r.Height - size) / 2f);
                g.ScaleTransform(s, s);
                bool simple = size < 19f;
                float ring = Math.Max(1.15f, size * 0.05f) / s;
                float line = Math.Max(0.9f, size * 0.034f) / s;

                if (glow > 0.01f)
                {
                    using (Pen halo = new Pen(Color.FromArgb((int)Math.Max(0, Math.Min(255, 75 * glow)), SwimTheme.Accent), ring * 3.4f))
                        Ring(g, halo, turn);
                }

                using (Pen pen = new Pen(SwimTheme.Accent, ring) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    Ring(g, pen, turn);

                if (marks && !simple)
                {
                    using (Pen m = new Pen(SwimTheme.AccentLight, line) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(m, 32f, 1.5f, 32f, 7.5f);
                        g.DrawLine(m, 62.5f, 32f, 56.5f, 32f);
                        g.DrawLine(m, 32f, 62.5f, 32f, 56.5f);
                        g.DrawLine(m, 1.5f, 32f, 7.5f, 32f);
                    }
                }

                if (!simple)
                {
                    using (Pen h = new Pen(SwimTheme.AccentLight, line) { LineJoin = LineJoin.Round })
                        g.DrawPolygon(h, Hexagon);
                }

                using (GraphicsPath spark = Spark(simple ? 17f : 11f))
                using (LinearGradientBrush b = new LinearGradientBrush(new PointF(20f, 20f), new PointF(44f, 44f), SwimTheme.AccentPale, SwimTheme.Accent))
                    g.FillPath(b, spark);

                if (dot.HasValue)
                {
                    double a = (turn - 45.0) * Math.PI / 180.0;
                    float cx = 32f + 27f * (float)Math.Cos(a), cy = 32f + 27f * (float)Math.Sin(a);
                    bool amber = dot.Value.ToArgb() == SwimTheme.Amber.ToArgb();
                    float rd = (amber ? Math.Max(2.6f, size * 0.12f) : Math.Max(1.4f, size * 0.055f)) / s;
                    using (SolidBrush back = new SolidBrush(SwimTheme.Back))
                        g.FillEllipse(back, cx - rd - 1.2f / s, cy - rd - 1.2f / s, (rd + 1.2f / s) * 2, (rd + 1.2f / s) * 2);
                    using (SolidBrush d = new SolidBrush(dot.Value))
                        g.FillEllipse(d, cx - rd, cy - rd, rd * 2, rd * 2);
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        /// <summary>The four arcs of the ring (its gaps at the marks, at 0°, 90°, 180° and 270°).</summary>
        private static void Ring(Graphics g, Pen pen, float turn)
        {
            for (int i = 0; i < 4; i++)
                g.DrawArc(pen, 5f, 5f, 54f, 54f, turn + 8f + 90f * i, 74f);
        }

        /// <summary>The spark in the middle: a star of four points (k: from its middle to a point), its curves as in the drawing of the sign.</summary>
        private static GraphicsPath Spark(float k)
        {
            float a = 0.109f * k, b = 0.309f * k;
            GraphicsPath p = new GraphicsPath();
            p.AddBezier(32f, 32f - k, 32f + a, 32f - b, 32f + b, 32f - a, 32f + k, 32f);
            p.AddBezier(32f + k, 32f, 32f + b, 32f + a, 32f + a, 32f + b, 32f, 32f + k);
            p.AddBezier(32f, 32f + k, 32f - a, 32f + b, 32f - b, 32f + a, 32f - k, 32f);
            p.AddBezier(32f - k, 32f, 32f - b, 32f - a, 32f - a, 32f - b, 32f, 32f - k);
            p.CloseFigure();
            return p;
        }

        /// <summary>The sign in a picture (the item of the menu Βοήθεια).</summary>
        public static Bitmap Picture(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                Draw(g, new RectangleF(0, 0, size, size), 0f, null, false, 0f);
            }

            return bmp;
        }
    }
}
