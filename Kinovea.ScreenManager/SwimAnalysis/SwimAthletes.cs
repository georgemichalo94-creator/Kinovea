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

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// One athlete of the federation registry (sheet "Στοιχεία Αθλητών/τριων" of the main Google Sheet).
    /// </summary>
    public class SwimAthlete
    {
        public string Id = "";
        public string Name = "";
        public string Gender = "";
        public string BirthYear = "";
        public string Category = "";
        public string Club = "";

        [JsonIgnore]
        public string SearchKey;

        public override string ToString()
        {
            List<string> parts = new List<string> { Name, Id };
            if (!string.IsNullOrEmpty(Club))
                parts.Add(Club);
            if (!string.IsNullOrEmpty(Category))
                parts.Add(Category);
            return string.Join(" · ", parts.ToArray());
        }
    }

    /// <summary>
    /// Local copy of the athlete list, so the analysis works offline. Stored in the settings folder.
    /// </summary>
    public class SwimAthleteList
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public DateTime Updated;
        public string Sheet = "";
        public List<SwimAthlete> Athletes = new List<SwimAthlete>();

        private Dictionary<string, SwimAthlete> byId;

        private static string FilePath
        {
            get { return Path.Combine(Software.SettingsDirectory, "SwimAthletes.json"); }
        }

        public static SwimAthleteList Load()
        {
            SwimAthleteList list = null;
            try
            {
                if (File.Exists(FilePath))
                    list = JsonConvert.DeserializeObject<SwimAthleteList>(File.ReadAllText(FilePath, Encoding.UTF8));
            }
            catch (Exception e)
            {
                log.Error("Could not read the athlete list.", e);
            }

            if (list == null || list.Athletes == null)
                list = new SwimAthleteList();

            list.BuildIndex();
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
                log.Error("Could not save the athlete list.", e);
            }
        }

        public void BuildIndex()
        {
            byId = new Dictionary<string, SwimAthlete>();
            foreach (SwimAthlete a in Athletes)
            {
                a.SearchKey = Normalize(a.Name + " " + a.Id + " " + a.Club + " " + a.Category + " " + a.BirthYear);
                if (!string.IsNullOrEmpty(a.Id) && !byId.ContainsKey(a.Id))
                    byId[a.Id] = a;
            }
        }

        public SwimAthlete Find(string id)
        {
            if (string.IsNullOrEmpty(id) || byId == null)
                return null;

            SwimAthlete a;
            return byId.TryGetValue(id.Trim(), out a) ? a : null;
        }

        /// <summary>
        /// Athletes whose name, registry number, club or category contain every word of the query
        /// (case and accent insensitive). An exact registry number comes first.
        /// </summary>
        public List<SwimAthlete> Search(string query, int max)
        {
            List<SwimAthlete> result = new List<SwimAthlete>();
            string q = Normalize(query);
            if (q.Length == 0)
                return result;

            SwimAthlete exact = Find(query);
            if (exact != null)
                result.Add(exact);

            string[] words = q.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (SwimAthlete a in Athletes)
            {
                if (result.Count >= max)
                    break;
                if (a == exact)
                    continue;
                if (words.All(w => a.SearchKey.Contains(w)))
                    result.Add(a);
            }

            return result;
        }

        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            string decomposed = text.Normalize(NormalizationForm.FormD);
            StringBuilder b = new StringBuilder(decomposed.Length);
            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    b.Append(c);
            }

            // The final sigma is not made upper case by every version of the .NET Framework.
            string upper = b.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant().Replace('ς', 'Σ');
            return string.Join(" ", upper.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
