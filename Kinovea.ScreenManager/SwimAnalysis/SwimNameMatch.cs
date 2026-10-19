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
using System.Text;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Names of athletes compared across alphabets and spellings (since 2026-10-19): a Greek athlete of the registry written in Latin
    /// letters in the results of a foreign competition ("PAPADOPOULOS Georgios", "Papadopoulos Giorgos"), or the same foreign
    /// athlete in two competitions. Greek is transliterated as ELOT 743 (the passports'), then both sides lose the differences of
    /// the usual spellings (OU/U, Y/I, CH/H/KH, PH/F, MP/B, NT/D, GK/G, double letters, the endings of given names).
    /// </summary>
    public static class SwimNameMatch
    {
        /// <summary>Greek letters in Latin ones (ELOT 743, capitals, no accents); other letters as they are, in capitals.</summary>
        public static string Latin(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            string s = RemoveAccents(text).ToUpperInvariant();
            StringBuilder b = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                char next = i + 1 < s.Length ? s[i + 1] : '\0';
                char prev = i > 0 ? s[i - 1] : '\0';
                switch (c)
                {
                    case 'Α':
                        if (next == 'Υ') { b.Append(VoicedNext(s, i + 2) ? "AV" : "AF"); i++; } else b.Append('A');
                        break;
                    case 'Ε':
                        if (next == 'Υ') { b.Append(VoicedNext(s, i + 2) ? "EV" : "EF"); i++; } else b.Append('E');
                        break;
                    case 'Η':
                        if (next == 'Υ') { b.Append(VoicedNext(s, i + 2) ? "IV" : "IF"); i++; } else b.Append('I');
                        break;
                    case 'Ο':
                        if (next == 'Υ') { b.Append("OU"); i++; } else b.Append('O');
                        break;
                    case 'Β': b.Append('V'); break;
                    case 'Γ':
                        // ΓΓ NG, ΓΞ NX, ΓΧ NCH; ΓΚ GK (ELOT 743).
                        if (next == 'Γ') { b.Append("NG"); i++; }
                        else if (next == 'Ξ' || next == 'Χ') b.Append('N');
                        else b.Append('G');
                        break;
                    case 'Δ': b.Append('D'); break;
                    case 'Ζ': b.Append('Z'); break;
                    case 'Θ': b.Append("TH"); break;
                    case 'Ι': b.Append('I'); break;
                    case 'Κ': b.Append('K'); break;
                    case 'Λ': b.Append('L'); break;
                    case 'Μ':
                        if (next == 'Π' && (prev == '\0' || !IsGreekLetter(prev))) { b.Append('B'); i++; } else b.Append('M');
                        break;
                    case 'Ν': b.Append('N'); break;
                    case 'Ξ': b.Append('X'); break;
                    case 'Π': b.Append('P'); break;
                    case 'Ρ': b.Append('R'); break;
                    case 'Σ': case 'Ϲ': b.Append('S'); break;
                    case 'Τ': b.Append('T'); break;
                    case 'Υ': b.Append('Y'); break;
                    case 'Φ': b.Append('F'); break;
                    case 'Χ': b.Append("CH"); break;
                    case 'Ψ': b.Append("PS"); break;
                    case 'Ω': b.Append('O'); break;
                    default: b.Append(c); break;
                }
            }

            return b.ToString();
        }

        /// <summary>
        /// A name as a key for comparing spellings: Latin letters only (Greek transliterated), the usual variants made one, double
        /// letters single. "ΓΕΩΡΓΙΟΣ" and "GEORGIOS" give the same key.
        /// </summary>
        public static string Key(string name)
        {
            string s = Latin(name);
            StringBuilder b = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c >= 'A' && c <= 'Z')
                    b.Append(c);
                else if (c == ' ' || c == '-' || c == '\'')
                    b.Append(' ');
            }

            s = " " + b.ToString().Trim() + " ";
            string[,] same = {
                { "OU", "U" }, { "Y", "I" }, { "KH", "H" }, { "CH", "H" }, { "PH", "F" }, { "TH", "T" }, { "MP", "B" }, { "NT", "D" },
                { "GK", "G" }, { "NG", "G" }, { "KS", "X" }, { "W", "V" }, { "C", "K" }, { "J", "I" }, { "EI", "I" }, { "OI", "I" },
                { "AI", "E" }
            };
            for (int i = 0; i < same.GetLength(0); i++)
                s = s.Replace(same[i, 0], same[i, 1]);

            // Double letters single ("PAPPAS", "PAPAS").
            StringBuilder d = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (d.Length > 0 && d[d.Length - 1] == c && c != ' ')
                    continue;
                d.Append(c);
            }

            return d.ToString().Trim();
        }

        /// <summary>A given name's key without its ending (GEORGIOS, GIORGOS → GIORG… ; DIMITRIOS, DIMITRIS → DIMITR…).</summary>
        public static string GivenKey(string name)
        {
            string k = Key(name);
            string first = k.Split(' ').FirstOrDefault() ?? "";
            foreach (string ending in new[] { "IOS", "IOU", "IA", "OS", "IS", "AS", "ES", "OU", "A", "I", "O", "E", "S" })
            {
                if (first.Length > ending.Length + 2 && first.EndsWith(ending, StringComparison.Ordinal))
                {
                    first = first.Substring(0, first.Length - ending.Length);
                    break;
                }
            }

            return first.Replace("EO", "IO");
        }

        /// <summary>
        /// How alike two persons' names are (0-1): the family names' keys (Jaro-Winkler), and the given names' stems when both have
        /// one (a stem the start of the other counts as the same). Another given name (under GivenSame) halves it: the same family,
        /// another person.
        /// </summary>
        public static double Similarity(string family1, string given1, string family2, string given2)
        {
            double f = JaroWinkler(Key(family1), Key(family2));
            string g1 = GivenKey(given1), g2 = GivenKey(given2);
            if (g1.Length == 0 || g2.Length == 0)
                return f;

            double g = g1.StartsWith(g2, StringComparison.Ordinal) || g2.StartsWith(g1, StringComparison.Ordinal) ? 1.0 : JaroWinkler(g1, g2);
            return g >= GivenSame ? 0.65 * f + 0.35 * g : 0.5 * Math.Min(f, g);
        }

        /// <summary>The same person by the name (Similarity): the family names this alike and the given names GivenSame.</summary>
        public const double SameName = 0.93;

        /// <summary>Two given names' stems this alike are the same name (GIORG/GEORG).</summary>
        public const double GivenSame = 0.85;

        /// <summary>
        /// A full name "FAMILY Given" (or "Family Given") split at the first space: the results publish the family name first.
        /// </summary>
        public static void Split(string full, out string family, out string given)
        {
            string s = (full ?? "").Trim();
            int space = s.IndexOf(' ');
            family = space < 0 ? s : s.Substring(0, space);
            given = space < 0 ? "" : s.Substring(space + 1).Trim();
        }

        /// <summary>The text has Greek letters (a name published in Greek, not in Latin letters).</summary>
        public static bool HasGreek(string text)
        {
            return !string.IsNullOrEmpty(text) && text.Any(c => (c >= '\u0370' && c <= '\u03FF') || (c >= '\u1F00' && c <= '\u1FFF'));
        }

        /// <summary>Jaro-Winkler similarity of two strings (0-1).</summary>
        public static double JaroWinkler(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return 0;
            if (a == b)
                return 1;

            int range = Math.Max(0, Math.Max(a.Length, b.Length) / 2 - 1);
            bool[] ma = new bool[a.Length], mb = new bool[b.Length];
            int matches = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int from = Math.Max(0, i - range), to = Math.Min(b.Length - 1, i + range);
                for (int j = from; j <= to; j++)
                {
                    if (mb[j] || a[i] != b[j])
                        continue;
                    ma[i] = mb[j] = true;
                    matches++;
                    break;
                }
            }

            if (matches == 0)
                return 0;

            int t = 0, k = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (!ma[i])
                    continue;
                while (!mb[k])
                    k++;
                if (a[i] != b[k])
                    t++;
                k++;
            }

            double m = matches;
            double jaro = (m / a.Length + m / b.Length + (m - t / 2.0) / m) / 3.0;
            int prefix = 0;
            while (prefix < 4 && prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
                prefix++;
            return jaro + prefix * 0.1 * (1 - jaro);
        }

        private static bool IsGreekLetter(char c)
        {
            return (c >= 'Α' && c <= 'Ω') || (c >= 'α' && c <= 'ω');
        }

        /// <summary>After ΑΥ/ΕΥ/ΗΥ: a vowel or a voiced consonant makes it V, else F (ELOT 743).</summary>
        private static bool VoicedNext(string s, int i)
        {
            if (i >= s.Length)
                return false;
            return "ΑΕΗΙΟΥΩΒΓΔΖΛΜΝΡ".IndexOf(s[i]) >= 0;
        }

        private static string RemoveAccents(string text)
        {
            string d = text.Normalize(NormalizationForm.FormD);
            StringBuilder b = new StringBuilder(d.Length);
            foreach (char c in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    b.Append(c);
            }

            return b.ToString().Normalize(NormalizationForm.FormC).Replace('ς', 'σ');
        }
    }
}
