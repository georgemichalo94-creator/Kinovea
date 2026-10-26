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
using System.Windows.Forms;
using Kinovea.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.9.7) Where the shared learning of this Kinovea is with its Google Sheet (SwimLearningSync.json in the settings folder): the id of
    /// this Kinovea, the sheet it shares with, the last answer of the sheet taken and the date of the last answer of its own sent.
    /// </summary>
    public class SwimLearningSyncState
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>This Kinovea (its folder of settings): made once.</summary>
        public string InstallId;

        /// <summary>The web app the answers below are about (another sheet: everything again).</summary>
        public string Url = "";

        /// <summary>The last answer of the sheet taken (its number there).</summary>
        public long PulledSeq;

        /// <summary>The answers of this Kinovea up to this date are in the sheet.</summary>
        public DateTime PushedThrough = DateTime.MinValue;

        public DateTime? LastSync;

        private static string FilePath
        {
            get { return Path.Combine(Software.SettingsDirectory, "SwimLearningSync.json"); }
        }

        public static SwimLearningSyncState Load()
        {
            SwimLearningSyncState s = null;
            try
            {
                if (File.Exists(FilePath))
                    s = JsonConvert.DeserializeObject<SwimLearningSyncState>(File.ReadAllText(FilePath, Encoding.UTF8));
            }
            catch (Exception e)
            {
                log.Error("Could not read the state of the shared learning.", e);
            }

            if (s == null)
                s = new SwimLearningSyncState();
            if (string.IsNullOrEmpty(s.InstallId))
            {
                s.InstallId = Guid.NewGuid().ToString("N").Substring(0, 16);
                s.Save();
            }

            return s;
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
                log.Error("Could not save the state of the shared learning.", e);
            }
        }
    }

    /// <summary>
    /// (2.9.7) The shared learning: every Kinovea connected to the same Google Sheet sends the answers of its analysts to the sheet
    /// "Μάθηση Kinovea" (Apps Script 2.9.7) and takes the answers of the others, so that all of them learn from all the analysts at once
    /// and every copy has the latest learning. In the background, a little after Kinovea starts, half a minute after an answer, and every
    /// ten minutes; never while a reading of the video uses the learning. Only numbers and codes travel (no names, no images). Settings:
    /// SharedLearning (on). Without the 2.9.7 script the learning stays on this computer (said once).
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Shared learning
        private Timer learningTimer;
        private SwimLearningSyncState learningSync;
        private bool learningSyncBusy;
        private bool learningSyncUnsupported;
        private DateTime learningNextSync = DateTime.MaxValue;
        private int learningMerged;

        /// <summary>Answers taken from the sheet at a time (the first time the latest of them).</summary>
        private const int LearningPage = 2000;

        /// <summary>Answers sent at a time.</summary>
        private const int LearningPushBatch = 400;

        private static readonly TimeSpan LearningFirst = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan LearningAfterAnswer = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan LearningEvery = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan LearningRetry = TimeSpan.FromMinutes(3);

        /// <summary>The id of this Kinovea and the first exchange a little after it starts.</summary>
        private void StartLearningSync()
        {
            try
            {
                learningSync = SwimLearningSyncState.Load();
                SwimLearning.Here = learningSync.InstallId;
            }
            catch (Exception e)
            {
                log.Error("The shared learning could not start.", e);
                return;
            }

            learningNextSync = DateTime.Now + LearningFirst;
            learningTimer = new Timer { Interval = 5000 };
            learningTimer.Tick += (s, e) => LearningSyncTick();
            learningTimer.Start();
        }

        /// <summary>An answer was kept or taken away: it goes to the other Kinovea soon.</summary>
        private void LearningChanged()
        {
            DateTime soon = DateTime.Now + LearningAfterAnswer;
            if (learningNextSync > soon)
                learningNextSync = soon;
        }

        /// <summary>The learning of this Kinovea (not the copy a reading in the background reads with).</summary>
        private SwimLearning MainLearning
        {
            get
            {
                if (learning == null)
                    learning = SwimLearning.Load();
                return learning;
            }
        }

        /// <summary>Answers of this learning from the other Kinovea (for the panel).</summary>
        private int SharedAnswers
        {
            get
            {
                string here = SwimLearning.Here;
                return here == null ? 0 : MainLearning.Records.Count(r => r.Origin != null && r.Origin != here);
            }
        }

        /// <summary>A reading of the video uses the learning in the background: the answers of the others wait.</summary>
        private bool LearningInUse
        {
            get { return ReadingBusy || AutoStrokesRunning || StartLightRunning || SkeletonRunning; }
        }

        private void LearningSyncTick()
        {
            if (learningSync == null || learningSyncBusy || learningSyncUnsupported || DateTime.Now < learningNextSync)
                return;
            if (Settings == null || !Settings.SharedLearning || !Settings.IsConfigured || LearningInUse)
                return;

            SyncLearning();
        }

        /// <summary>The answers of the others taken, then the new ones of this Kinovea sent.</summary>
        private void SyncLearning()
        {
            learningSyncBusy = true;
            learningMerged = 0;
            string url = Settings.WebAppUrl.Trim();
            if (!string.Equals(learningSync.Url ?? "", url, StringComparison.Ordinal))
            {
                // Another sheet: everything again, there.
                learningSync.Url = url;
                learningSync.PulledSeq = 0;
                learningSync.PushedThrough = DateTime.MinValue;
                foreach (SwimLearningForget f in MainLearning.Forgotten)
                    f.Sent = false;
            }

            PullLearning(url, Settings.Token);
        }

        private void PullLearning(string url, string token)
        {
            client.PullLearning(url, token, learningSync.PulledSeq, LearningPage, result =>
            {
                try
                {
                    if (!result.Ok)
                    {
                        EndLearningSync(result.Error, false);
                        return;
                    }

                    // A reading started meanwhile: the answers are taken again later.
                    if (LearningInUse)
                    {
                        EndLearningSync(null, false);
                        learningNextSync = DateTime.Now + LearningAfterAnswer;
                        return;
                    }

                    JArray records = result.Raw != null ? result.Raw["records"] as JArray : null;
                    List<SwimLearningRecord> incoming = records == null ? new List<SwimLearningRecord>() : records.ToObject<List<SwimLearningRecord>>();
                    int merged = MainLearning.Merge(incoming);
                    if (merged > 0)
                    {
                        MainLearning.Save();
                        learningMerged += merged;
                    }
                    else if (incoming.Any(r => r != null && r.Kind == SwimLearning.ForgetKind))
                    {
                        MainLearning.Save();
                    }

                    long seq = result.Raw != null && result.Raw["seq"] != null ? (long)result.Raw["seq"] : learningSync.PulledSeq;
                    bool more = result.Raw != null && result.Raw["more"] != null && (bool)result.Raw["more"];
                    learningSync.PulledSeq = seq;
                    learningSync.Save();
                    if (more && incoming.Count > 0)
                        PullLearning(url, token);
                    else
                        PushLearning(url, token);
                }
                catch (Exception e)
                {
                    log.Error("The answers of the shared learning could not be taken.", e);
                    EndLearningSync(e.Message, false);
                }
            });
        }

        private void PushLearning(string url, string token)
        {
            string here = SwimLearning.Here;
            DateTime through = learningSync.PushedThrough;
            List<SwimLearningRecord> own = MainLearning.Records
                .Where(r => (r.Origin == null || r.Origin == here) && r.Date > through)
                .OrderBy(r => r.Date).ToList();

            // One batch, not cutting answers of the same date apart.
            List<SwimLearningRecord> batch = own.Take(LearningPushBatch).ToList();
            if (batch.Count > 0 && batch.Count < own.Count)
            {
                DateTime lastDate = batch[batch.Count - 1].Date;
                batch.AddRange(own.Skip(batch.Count).TakeWhile(r => r.Date == lastDate));
            }

            List<SwimLearningForget> forgets = MainLearning.Forgotten.Where(f => !f.Shared && !f.Sent).Take(LearningPushBatch).ToList();
            if (batch.Count == 0 && forgets.Count == 0)
            {
                EndLearningSync(null, true);
                return;
            }

            JArray payload = new JArray();
            foreach (SwimLearningRecord r in batch)
            {
                if (r.Origin == null)
                    r.Origin = here;
                payload.Add(JObject.FromObject(r));
            }

            foreach (SwimLearningForget f in forgets)
            {
                payload.Add(JObject.FromObject(new SwimLearningRecord
                {
                    Id = f.Id ?? Guid.NewGuid().ToString("N"),
                    Origin = here,
                    Date = f.Date,
                    Kind = SwimLearning.ForgetKind,
                    Key = f.Key,
                    Right = false
                }));
            }

            DateTime pushedTo = batch.Count > 0 ? batch[batch.Count - 1].Date : through;
            bool rest = own.Count > batch.Count || MainLearning.Forgotten.Count(f => !f.Shared && !f.Sent) > forgets.Count;
            client.PushLearning(url, token, here, payload, result =>
            {
                try
                {
                    if (!result.Ok)
                    {
                        EndLearningSync(result.Error, false);
                        return;
                    }

                    learningSync.PushedThrough = pushedTo;
                    learningSync.Save();
                    foreach (SwimLearningForget f in forgets)
                        f.Sent = true;
                    MainLearning.Save();
                    if (rest)
                        PushLearning(url, token);
                    else
                        EndLearningSync(null, true);
                }
                catch (Exception e)
                {
                    log.Error("The answers of the shared learning could not be sent.", e);
                    EndLearningSync(e.Message, false);
                }
            });
        }

        /// <summary>The exchange is over (done, or an error: tried again in a few minutes; a script older than 2.9.7: not again now).</summary>
        private void EndLearningSync(string error, bool done)
        {
            learningSyncBusy = false;
            if (done)
            {
                learningSync.LastSync = DateTime.Now;
                learningSync.Save();
                learningNextSync = DateTime.Now + LearningEvery;
            }
            else
            {
                learningNextSync = DateTime.Now + LearningRetry;
            }

            if (!string.IsNullOrEmpty(error) && error.IndexOf("learning", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (error.Contains("Άγνωστη ενέργεια") || error.IndexOf("Unknown action", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                learningSyncUnsupported = true;
                Status(SwimLang.T("Η κοινή μάθηση με τα άλλα Kinovea θέλει το script 2.9.7 στο Google Sheet: ως τότε η μάθηση μένει σε αυτόν τον υπολογιστή."), false);
            }
            else if (!string.IsNullOrEmpty(error))
            {
                log.DebugFormat("Shared learning: {0} (again in a few minutes).", error);
            }

            if (learningMerged > 0)
            {
                Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("🔄 Κοινή μάθηση: +{0} απαντήσεις από τα άλλα Kinovea."), learningMerged), false);
                learningMerged = 0;
                RefreshProposalsUi();
            }
        }
        #endregion
    }
}
