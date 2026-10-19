#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Globalization;
using System.Threading;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The language of the analysis (since 2.7.7), from the switch ΕΛ / EN of the panel: the panel opens again in it where it was,
    /// the graphics of the video and the next PDF follow, and so do the menus of Kinovea. The analyses themselves do not change:
    /// their values stay in Greek in the files and in the sheet (SwimLang).
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>The panel is closed to open again in the other language: the analysis stays on.</summary>
        private bool reopeningForm;

        /// <summary>The language of the analysis: English or Greek.</summary>
        public void SetLanguage(bool english)
        {
            if (SwimLang.English == english)
                return;

            // A reading or the auto pilot shows its own questions and texts on the way: the language changes after them.
            if (ReadingBusy || PilotActive)
            {
                Status(SwimLang.T("Η γλώσσα αλλάζει όταν τελειώσει η ανάγνωση του βίντεο."), true);
                if (form != null && !form.IsDisposed)
                    form.RefreshLanguageSwitch();
                return;
            }

            SwimLang.English = english;
            SwitchKinoveaLanguage(english);
        }

        private void SwimLang_Changed(object sender, EventArgs e)
        {
            Settings.Language = SwimLang.Code;
            Settings.Save();

            // The texts kept by the manager, in the new language.
            ReferencesMessage = ReferencesSummary();
            AthletesMessage = AthletesSummary();
            if (SyncState == SwimSyncState.Idle)
                SyncMessage = SwimLang.T("Έτοιμο");
            else if (SyncState == SwimSyncState.NotConfigured)
                SyncMessage = SwimLang.T("Δεν έχει ρυθμιστεί η σύνδεση με το Google Sheet");

            // The titles of the phases.
            if (Race != null)
                Recompute();

            ReopenForm();
            UpdateBars();

            // The final frame of the report has the graphics of the video: again in the new language, at the same frame (the other
            // graphics of the PDF are drawn again before each PDF).
            if (Race != null && Race.FinalFrame != null && HasVideo)
                RecaptureFinalFrame(false, true);

            InvalidatePlayer();
            Status(SwimLang.English ? "Language: English (the panel, the graphics of the video, the PDF)." :
                "Γλώσσα: Ελληνικά (το πάνελ, τα γραφικά του βίντεο, το PDF).", false);
        }

        /// <summary>The panel built again in the language of the analysis, where it was and on the same page.</summary>
        private void ReopenForm()
        {
            if (form == null || form.IsDisposed)
                return;

            FormSwimAnalysis old = form;
            int page = old.PageIndex;
            reopeningForm = true;
            try
            {
                old.Close();
            }
            finally
            {
                reopeningForm = false;
            }

            form = null;
            EnsureForm(true);
            if (form != null)
                form.PageIndex = page;
        }

        /// <summary>
        /// The menus of Kinovea in the same language, as with Options → Language (the preferences and the other windows too).
        /// Nothing happens when Kinovea has no such language.
        /// </summary>
        private static void SwitchKinoveaLanguage(bool english)
        {
            try
            {
                string name = english ? "en" : "el";
                PreferencesManager.BeforeRead();
                CultureInfo now = Thread.CurrentThread.CurrentUICulture;
                if (now.TwoLetterISOLanguageName != name)
                {
                    PreferencesManager.GeneralPreferences.SetCulture(name);
                    Thread.CurrentThread.CurrentUICulture = PreferencesManager.GeneralPreferences.GetSupportedCulture();
                }

                // The menus again, also the one of the swim analysis.
                NotificationCenter.RaiseTriggerPreferencesUpdated(true);
            }
            catch (Exception e)
            {
                log.Error("Could not change the language of Kinovea.", e);
            }
        }
    }
}
