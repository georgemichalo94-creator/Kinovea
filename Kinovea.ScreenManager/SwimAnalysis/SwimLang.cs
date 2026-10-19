#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The language of the swim analysis (since 2026-10-19): Greek, the texts of the code, or English, for the panel, the graphics
    /// of the video, the chart images and the PDF. The Greek text is the key of its English (SwimLangEnglish).
    /// The values kept in the files and in the sheet (stages and events of the snapshots, angles, marks, titles of the phases,
    /// the values of the finals) stay in Greek, so that the analyses of both languages go together; Label() shows them.
    /// </summary>
    public static class SwimLang
    {
        private static bool english = SavedEnglish();

        /// <summary>The analysis is shown in English (else in Greek).</summary>
        public static bool English
        {
            get { return english; }
            set
            {
                if (english == value)
                    return;

                english = value;
                EventHandler changed = Changed;
                if (changed != null)
                    changed(null, EventArgs.Empty);
            }
        }

        /// <summary>The language of the settings (the program starts in it, before the panel is opened).</summary>
        private static bool SavedEnglish()
        {
            try
            {
                return SwimSettings.Load().Language == "en";
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>"en" or "el": the language sent to the sheet for the report and the PDF.</summary>
        public static string Code
        {
            get { return english ? "en" : "el"; }
        }

        /// <summary>The language changed.</summary>
        public static event EventHandler Changed;

        /// <summary>A text of the program in the language of the analysis (the text itself when it has no English).</summary>
        public static string T(string greek)
        {
            if (!english || string.IsNullOrEmpty(greek))
                return greek;

            string en;
            return SwimLangEnglish.Map.TryGetValue(greek, out en) ? en : greek;
        }

        /// <summary>An interpolated text in the language of the analysis: the Greek composite format ("{0} για τα {1}") is the key.</summary>
        public static string F(FormattableString text)
        {
            if (text == null)
                return null;

            string en;
            if (!english || !SwimLangEnglish.Map.TryGetValue(text.Format, out en))
                return text.ToString();

            try
            {
                return string.Format(CultureInfo.CurrentCulture, en, text.GetArguments());
            }
            catch (FormatException)
            {
                return text.ToString();
            }
        }

        /// <summary>A place: "3η" (θέση) in Greek, "3rd" in English.</summary>
        public static string Ordinal(int n)
        {
            string number = n.ToString(CultureInfo.InvariantCulture);
            if (!english)
                return number + "η";

            int tens = Math.Abs(n) % 100;
            if (tens >= 11 && tens <= 13)
                return number + "th";

            switch (Math.Abs(n) % 10)
            {
                case 1: return number + "st";
                case 2: return number + "nd";
                case 3: return number + "rd";
                default: return number + "th";
            }
        }

        private static readonly Regex numbered = new Regex(@"^(?<pre>\D*?)(?<n>\d+(?:[.,]\d+)?(?:–\d+)?)(?<unit>μ\.|yd|m)?(?<post>.*)$", RegexOptions.Compiled);

        /// <summary>
        /// A value kept in Greek (a stage or an event of a snapshot, an angle, a mark, the title of a phase, a value of the finals) in
        /// the language of the analysis: its English, else the English of the words around its number ("Στροφή 50μ." → "Turn 50m",
        /// "Εκκίνηση 2" → "Start 2"), else the value itself (a name typed by the analyst).
        /// </summary>
        public static string Label(string greek)
        {
            if (!english || string.IsNullOrEmpty(greek))
                return greek;

            string en;
            if (SwimLangEnglish.Map.TryGetValue(greek, out en))
                return en;

            Match m = numbered.Match(greek);
            if (!m.Success)
                return greek;

            string pre = m.Groups["pre"].Value;
            string post = m.Groups["post"].Value;
            string unit = m.Groups["unit"].Value;
            string preEn = pre.Length == 0 ? "" : Words(pre);
            string postEn = post.Length == 0 ? "" : Words(post);
            if (preEn == null || postEn == null)
                return greek;

            return preEn + m.Groups["n"].Value + (unit == "μ." ? "m" : unit) + postEn;
        }

        /// <summary>The English of words around a number, with their spaces ("Στροφή " → "Turn "); null if unknown.</summary>
        private static string Words(string greek)
        {
            string en;
            if (SwimLangEnglish.Map.TryGetValue(greek, out en))
                return en;

            string trimmed = greek.Trim();
            if (trimmed.Length == 0)
                return greek;

            if (!SwimLangEnglish.Map.TryGetValue(trimmed, out en))
            {
                // Greek letters left: not known.
                foreach (char c in trimmed)
                {
                    if (c >= 'Ͱ' && c <= 'Ͽ')
                        return null;
                }

                en = trimmed;
            }

            int lead = greek.Length - greek.TrimStart().Length;
            int trail = greek.Length - greek.TrimEnd().Length;
            return greek.Substring(0, lead) + en + greek.Substring(greek.Length - trail);
        }

        /// <summary>The Greek of a text shown in English (the item of a list picked in English): the value kept in the files.</summary>
        public static string Greek(string shown, IEnumerable<string> greekValues)
        {
            if (string.IsNullOrEmpty(shown) || greekValues == null)
                return shown;

            foreach (string g in greekValues)
            {
                if (string.Equals(g, shown, StringComparison.CurrentCultureIgnoreCase) ||
                    string.Equals(Label(g), shown, StringComparison.CurrentCultureIgnoreCase))
                    return g;
            }

            return shown;
        }
    }
}
