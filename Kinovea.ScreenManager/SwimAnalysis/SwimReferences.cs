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
    /// Time of a reference at one checkpoint (seconds after the start signal).
    /// </summary>
    public class SwimReferenceSplit
    {
        public int D;
        public double T;

        /// <summary>Strokes of the segment that ends at this checkpoint (optional, like the column "Χεριές" of the Excel).</summary>
        public double? N;
    }

    /// <summary>
    /// An elite performance used for comparison (delta at the checkpoints, velocity profile).
    /// It comes from the sheet "Elite αναφορές" of the Google Sheet, or from one of the analyses.
    /// </summary>
    public class SwimReference
    {
        public string Id = "";
        public string Name = "";
        public string Gender = "";
        public string Style = "fs";

        /// <summary>Total distance (400 for a 4x100 relay).</summary>
        public int Distance;
        public bool Relay;
        public int Pool = 50;

        /// <summary>"m" or "yd".</summary>
        public string PoolUnit = SwimRace.UnitMeters;
        public string Source = "";
        public List<SwimReferenceSplit> Times = new List<SwimReferenceSplit>();

        /// <summary>Saved on this computer only (not in the Google Sheet yet).</summary>
        public bool Local;

        [JsonIgnore]
        public double? FinalTime
        {
            get { return TimeAt(Distance); }
        }

        [JsonIgnore]
        public string DistanceLabel
        {
            get { return Relay ? "4x" + (Distance / 4).ToString(CultureInfo.InvariantCulture) : Distance.ToString(CultureInfo.InvariantCulture); }
        }

        [JsonIgnore]
        public string Label
        {
            get
            {
                double? final = FinalTime;
                string text = Name;
                if (final.HasValue)
                    text += " (" + FormatTime(final.Value) + ")";
                if (Local)
                    text += SwimLang.T(" · μόνο σε αυτόν τον υπολογιστή");
                return text;
            }
        }

        public double? TimeAt(int d)
        {
            foreach (SwimReferenceSplit s in Times)
            {
                if (s.D == d)
                    return s.T;
            }

            return null;
        }

        /// <summary>
        /// Same event (style, distance, pool).
        /// </summary>
        public bool Matches(SwimRace race)
        {
            return race != null && Style == race.Style && Distance == race.Distance && Relay == race.Relay && Pool == race.Pool &&
                (string.IsNullOrEmpty(PoolUnit) ? SwimRace.UnitMeters : PoolUnit) == (string.IsNullOrEmpty(race.PoolUnit) ? SwimRace.UnitMeters : race.PoolUnit);
        }

        public bool GenderMatches(string gender)
        {
            string a = NormalizeGender(Gender);
            string b = NormalizeGender(gender);
            return a.Length == 0 || b.Length == 0 || a == b;
        }

        public SwimReference Copy()
        {
            SwimReference r = (SwimReference)MemberwiseClone();
            r.Times = Times.Select(s => new SwimReferenceSplit { D = s.D, T = s.T, N = s.N }).ToList();
            return r;
        }

        /// <summary>
        /// "M", "F", "X" (mixed) or "" when unknown. Accepts the values of the federation registry
        /// (Ανδρών / Γυναικών) and the usual Greek and English words or letters.
        /// </summary>
        public static string NormalizeGender(string text)
        {
            string g = SwimAthleteList.Normalize(text);
            if (g.Length == 0)
                return "";

            string[] female = { "ΓΥΝ", "ΘΗΛ", "ΚΟΡ", "ΝΕΑΝ", "ΠΑΓΚΟΡ", "ΚΟΡΑΣ", "FEM", "WOM", "GIRL", "LAD" };
            string[] male = { "ΑΝΔ", "ΑΡΡ", "ΑΓΟΡ", "ΕΦΗΒ", "ΠΑΜΠ", "ΠΑΙΔ", "MAL", "MEN", "MAN", "BOY" };
            string[] mixed = { "ΜΙΚΤ", "MIX" };

            if (mixed.Any(p => g.StartsWith(p, StringComparison.Ordinal)))
                return "X";
            if (female.Any(p => g.StartsWith(p, StringComparison.Ordinal)) || g == "Γ" || g == "Θ" || g == "F" || g == "W")
                return "F";
            if (male.Any(p => g.StartsWith(p, StringComparison.Ordinal)) || g == "Α" || g == "A" || g == "M")
                return "M";

            return "";
        }

        /// <summary>
        /// Difference athlete − reference: "+0.84" (slower), "-0.12" (faster).
        /// </summary>
        public static string FormatDelta(double delta)
        {
            string sign = delta > 0.0049 ? "+" : (delta < -0.0049 ? "-" : "±");
            return sign + Math.Abs(delta).ToString("0.00", CultureInfo.InvariantCulture);
        }

        public static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || Math.Abs(seconds) > 1e9)
                return seconds.ToString("0.00", CultureInfo.InvariantCulture);

            // Rounded to the hundredth as shown first (since 2026-10-19): 59.996 is 1:00.00 and 119.996 2:00.00, not 60.00 and 1:60.00
            // (an official time typed back is read as it is shown).
            decimal t = Math.Round((decimal)seconds, 2, MidpointRounding.AwayFromZero);
            if (t < 60)
                return t.ToString("0.00", CultureInfo.InvariantCulture);

            int minutes = (int)(t / 60);
            decimal rest = t - minutes * 60;
            return minutes.ToString(CultureInfo.InvariantCulture) + ":" + rest.ToString("00.00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reference made from an analysis: the times of all its checkpoints.
        /// </summary>
        public static SwimReference FromRace(SwimRace race, SwimComputed computed, string name)
        {
            SwimReference r = new SwimReference();
            r.Id = race.Id;
            r.Name = string.IsNullOrWhiteSpace(name) ? DefaultName(race) : name.Trim();
            r.Gender = race.AthleteGender ?? "";
            r.Style = race.Style;
            r.Distance = race.Distance;
            r.Relay = race.Relay;
            r.Pool = race.Pool;
            r.PoolUnit = race.PoolUnit;
            r.Source = string.Join(" · ", new string[] { race.Competition, race.Round, race.Date }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray());
            foreach (SwimRow row in computed.Rows)
            {
                if (row.T.HasValue && row.T.Value > 0)
                    r.Times.Add(new SwimReferenceSplit { D = row.D, T = Math.Round(row.T.Value, 2), N = row.Strokes });
            }

            return r;
        }

        public static string DefaultName(SwimRace race)
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(race.Athlete))
                parts.Add(race.Athlete.Trim());
            string place = string.Join(" ", new string[] { race.Competition, race.Round }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray());
            if (place.Length > 0)
                parts.Add(place);
            return parts.Count > 0 ? string.Join(" – ", parts.ToArray()) : SwimLang.T("Αναφορά");
        }

        public JObject ToJson()
        {
            JObject o = new JObject();
            o["id"] = Id ?? "";
            o["raceId"] = Local || IsRaceId(Id) ? (Id ?? "") : "";
            o["name"] = Name ?? "";
            o["gender"] = Gender ?? "";
            o["style"] = Style ?? "fs";
            o["distance"] = Distance;
            o["relay"] = Relay;
            o["pool"] = Pool;
            o["poolUnit"] = string.IsNullOrEmpty(PoolUnit) ? SwimRace.UnitMeters : PoolUnit;
            o["source"] = Source ?? "";
            JArray times = new JArray();
            foreach (SwimReferenceSplit s in Times)
            {
                JObject t = new JObject();
                t["d"] = s.D;
                t["t"] = Math.Round(s.T, 3);
                if (s.N.HasValue)
                    t["n"] = s.N.Value;
                times.Add(t);
            }

            o["times"] = times;
            return o;
        }

        public static SwimReference FromJson(JObject o)
        {
            if (o == null)
                return null;

            SwimReference r = new SwimReference();
            r.Id = o.Value<string>("id") ?? "";
            r.Name = o.Value<string>("name") ?? "";
            r.Gender = o.Value<string>("gender") ?? "";
            r.Style = o.Value<string>("style") ?? "fs";
            r.Distance = (int)Math.Round(o.Value<double?>("distance") ?? 0);
            r.Relay = o.Value<bool?>("relay") ?? false;
            r.Pool = (int)Math.Round(o.Value<double?>("pool") ?? 50);
            r.PoolUnit = o.Value<string>("poolUnit") == SwimRace.UnitYards ? SwimRace.UnitYards : SwimRace.UnitMeters;
            r.Source = o.Value<string>("source") ?? "";

            JArray times = o["times"] as JArray;
            if (times != null)
            {
                foreach (JToken token in times)
                {
                    JObject t = token as JObject;
                    if (t == null)
                        continue;

                    double? d = t.Value<double?>("d");
                    double? time = t.Value<double?>("t");
                    double? n = t.Value<double?>("n");
                    if (d.HasValue && time.HasValue && d.Value > 0 && time.Value > 0)
                        r.Times.Add(new SwimReferenceSplit { D = (int)Math.Round(d.Value), T = time.Value, N = n.HasValue && n.Value >= 0 ? n : null });
                }
            }

            r.Times = r.Times.GroupBy(s => s.D).Select(g => g.First()).OrderBy(s => s.D).ToList();
            if (string.IsNullOrEmpty(r.Id))
                r.Id = "sheet|" + r.Name + "|" + r.Style + "|" + r.DistanceLabel + "|" + r.Pool + (r.PoolUnit == SwimRace.UnitYards ? "y" : "");

            return r;
        }

        private static bool IsRaceId(string id)
        {
            // Analyses have 32 hexadecimal characters (Guid "N"), references typed in the sheet do not.
            if (string.IsNullOrEmpty(id) || id.Length != 32)
                return false;

            return id.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        }
    }

    /// <summary>
    /// Local copy of the elite references, so the comparison works offline. Stored in the settings folder.
    /// </summary>
    public class SwimReferenceList
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public DateTime Updated;
        public List<SwimReference> References = new List<SwimReference>();

        private static string FilePath
        {
            get { return Path.Combine(Software.SettingsDirectory, "SwimReferences.json"); }
        }

        public static SwimReferenceList Load()
        {
            SwimReferenceList list = null;
            try
            {
                if (File.Exists(FilePath))
                    list = JsonConvert.DeserializeObject<SwimReferenceList>(File.ReadAllText(FilePath, Encoding.UTF8));
            }
            catch (Exception e)
            {
                log.Error("Could not read the reference list.", e);
            }

            if (list == null || list.References == null)
                list = new SwimReferenceList();

            list.References.RemoveAll(r => r == null || r.Times == null);
            return list;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.None), Encoding.UTF8);
            }
            catch (Exception e)
            {
                log.Error("Could not save the reference list.", e);
            }
        }

        public SwimReference Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            return References.FirstOrDefault(r => r.Id == id);
        }

        /// <summary>
        /// References of the event of the race (and of the gender of the athlete when known), fastest first.
        /// </summary>
        public List<SwimReference> For(SwimRace race)
        {
            if (race == null)
                return new List<SwimReference>();

            return References
                .Where(r => r.Matches(race) && r.GenderMatches(race.AthleteGender))
                .OrderBy(r => r.FinalTime ?? double.MaxValue)
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Replace the references of the Google Sheet, keeping the local ones that are not there.
        /// </summary>
        public void ReplaceFromSheet(List<SwimReference> fromSheet)
        {
            HashSet<string> ids = new HashSet<string>(fromSheet.Select(r => r.Id));
            List<SwimReference> keep = References.Where(r => r.Local && !ids.Contains(r.Id)).ToList();
            References = fromSheet.Concat(keep).ToList();
            Updated = DateTime.Now;
        }

        public void AddOrReplace(SwimReference reference)
        {
            References.RemoveAll(r => r.Id == reference.Id);
            References.Add(reference);
        }
    }
}
