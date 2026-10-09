#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.IO;
using System.Text;
using Kinovea.Services;
using Newtonsoft.Json;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Settings of the swim analysis panel, stored in the Kinovea settings folder (SwimAnalysis.json).
    /// </summary>
    public class SwimSettings
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Google Apps Script web app URL (ends with /exec).</summary>
        public string WebAppUrl = "";

        /// <summary>Shared secret shown in the Google Sheet menu "Swim Analysis → Στοιχεία σύνδεσης".</summary>
        public string Token = "";

        /// <summary>Stroke count rounding at checkpoints: 0.5 (half strokes), 1 (whole) or 0 (exact).</summary>
        public double StrokeRounding = 0.5;

        /// <summary>Send every change to the sheet as it happens.</summary>
        public bool AutoSend = true;

        [JsonIgnore]
        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(WebAppUrl) && !string.IsNullOrWhiteSpace(Token); }
        }

        private static string FilePath
        {
            get { return Path.Combine(Software.SettingsDirectory, "SwimAnalysis.json"); }
        }

        public static SwimSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    SwimSettings settings = JsonConvert.DeserializeObject<SwimSettings>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (settings != null)
                        return settings;
                }
            }
            catch (Exception e)
            {
                log.Error("Could not read swim analysis settings.", e);
            }

            return new SwimSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception e)
            {
                log.Error("Could not save swim analysis settings.", e);
            }
        }
    }
}
