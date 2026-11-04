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
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.16.5) The shared learning of the technique evaluation: every Kinovea connected to the same Google Sheet sends the answers of its
    /// coaches on the criteria the program measures (SwimTechLearning: the measurement and whether the technique was good) to the sheet
    /// "Μάθηση τεχνικής" (Apps Script 2.16.5) and takes the answers of the others, so that the learned limits and the automatic evaluation
    /// learn from all the coaches at once. Right after the exchange of the learning of the marks (same times, same setting SharedLearning),
    /// never while a reading of the video runs. Only numbers and codes travel (the ids of the analysis, the phase, the list and the
    /// criterion; no names, no images). Without the 2.16.5 script the learning of the technique stays on this computer (said once).
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Shared learning of the technique evaluation
        private bool techShareUnsupported;
        private int techShareMerged;

        /// <summary>The technique exchange may run now (the script has it, there is a learning on this computer).</summary>
        private bool TechShareReady
        {
            get { return !techShareUnsupported && learningSync != null && !LearningInUse && SwimTechLearning.Current != null; }
        }

        /// <summary>Another sheet: every answer again, there.</summary>
        private void TechShareAgain()
        {
            learningSync.TechPulledSeq = 0;
            learningSync.TechPushedThrough = DateTime.MinValue;
            SwimTechLearning tl = SwimTechLearning.Current;
            if (tl != null)
                foreach (SwimTechForget f in tl.Forgotten)
                    f.Sent = false;
        }

        private void SyncTechLearning()
        {
            techShareMerged = 0;
            PullTechLearning(Settings.WebAppUrl.Trim(), Settings.Token);
        }

        private void PullTechLearning(string url, string token)
        {
            client.PullTechLearning(url, token, learningSync.TechPulledSeq, LearningPage, result =>
            {
                try
                {
                    if (!result.Ok)
                    {
                        EndTechSync(result.Error);
                        return;
                    }

                    // A reading started meanwhile: the answers are taken again later.
                    SwimTechLearning tl = SwimTechLearning.Current;
                    if (LearningInUse || tl == null)
                    {
                        EndTechSync(null);
                        return;
                    }

                    JArray records = result.Raw != null ? result.Raw["records"] as JArray : null;
                    List<SwimTechSample> samples;
                    List<SwimTechForget> forgets;
                    int count = ReadTechShare(records, out samples, out forgets);
                    techShareMerged += tl.Merge(samples, forgets);

                    long seq = result.Raw != null && result.Raw["seq"] != null ? (long)result.Raw["seq"] : learningSync.TechPulledSeq;
                    bool more = result.Raw != null && result.Raw["more"] != null && (bool)result.Raw["more"];
                    learningSync.TechPulledSeq = seq;
                    learningSync.Save();
                    if (more && count > 0)
                        PullTechLearning(url, token);
                    else
                        PushTechLearning(url, token);
                }
                catch (Exception e)
                {
                    log.Error("The answers of the shared technique learning could not be taken.", e);
                    EndTechSync(e.Message);
                }
            });
        }

        /// <summary>The rows of the sheet: the answers and the answers taken away (rows of other kinds are left out). Returns how many rows.</summary>
        public static int ReadTechShare(JArray records, out List<SwimTechSample> samples, out List<SwimTechForget> forgets)
        {
            samples = new List<SwimTechSample>();
            forgets = new List<SwimTechForget>();
            if (records == null)
                return 0;

            foreach (JToken t in records)
            {
                JObject o = t as JObject;
                if (o == null)
                    continue;
                try
                {
                    string kind = (string)o["Kind"];
                    if (kind == SwimTechLearning.ShareForgetKind)
                        forgets.Add(o.ToObject<SwimTechForget>());
                    else if (kind == SwimTechLearning.ShareKind)
                        samples.Add(o.ToObject<SwimTechSample>());
                }
                catch (Exception e)
                {
                    // A row changed by hand: left out.
                    log.DebugFormat("A row of the shared technique learning does not read: {0}", e.Message);
                }
            }

            return records.Count;
        }

        /// <summary>A row of the sheet for an answer of this Kinovea.</summary>
        public static JObject TechShareRow(SwimTechSample s)
        {
            JObject o = JObject.FromObject(s);
            o["Kind"] = SwimTechLearning.ShareKind;
            return o;
        }

        /// <summary>A row of the sheet for an answer taken away here.</summary>
        public static JObject TechShareRow(SwimTechForget f, string origin)
        {
            return new JObject
            {
                ["Id"] = f.Id,
                ["Origin"] = origin,
                ["Kind"] = SwimTechLearning.ShareForgetKind,
                ["Date"] = f.At.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["At"] = f.At,
                ["Key"] = f.Key,
                ["Prefix"] = f.Prefix
            };
        }

        private void PushTechLearning(string url, string token)
        {
            SwimTechLearning tl = SwimTechLearning.Current;
            if (tl == null)
            {
                EndTechSync(null);
                return;
            }

            string here = SwimLearning.Here;
            DateTime through = learningSync.TechPushedThrough;
            List<SwimTechSample> own = tl.Samples.Where(s => SwimTechLearning.Own(s) && SwimTechLearning.When(s) > through)
                .OrderBy(SwimTechLearning.When).ToList();

            // One batch, not cutting answers of the same moment apart.
            List<SwimTechSample> batch = own.Take(LearningPushBatch).ToList();
            if (batch.Count > 0 && batch.Count < own.Count)
            {
                DateTime lastWhen = SwimTechLearning.When(batch[batch.Count - 1]);
                batch.AddRange(own.Skip(batch.Count).TakeWhile(s => SwimTechLearning.When(s) == lastWhen));
            }

            List<SwimTechForget> forgets = tl.Forgotten.Where(f => !f.Shared && !f.Sent).Take(LearningPushBatch).ToList();
            if (batch.Count == 0 && forgets.Count == 0)
            {
                EndTechSync(null);
                return;
            }

            JArray payload = new JArray();
            foreach (SwimTechSample s in batch)
            {
                if (string.IsNullOrEmpty(s.Origin))
                    s.Origin = here;
                payload.Add(TechShareRow(s));
            }

            foreach (SwimTechForget f in forgets)
                payload.Add(TechShareRow(f, here));

            DateTime pushedTo = batch.Count > 0 ? SwimTechLearning.When(batch[batch.Count - 1]) : through;
            bool rest = own.Count > batch.Count || tl.Forgotten.Count(f => !f.Shared && !f.Sent) > forgets.Count;
            client.PushTechLearning(url, token, here, payload, result =>
            {
                try
                {
                    if (!result.Ok)
                    {
                        EndTechSync(result.Error);
                        return;
                    }

                    learningSync.TechPushedThrough = pushedTo;
                    learningSync.Save();
                    foreach (SwimTechForget f in forgets)
                        f.Sent = true;
                    tl.SaveShared();
                    if (rest)
                        PushTechLearning(url, token);
                    else
                        EndTechSync(null);
                }
                catch (Exception e)
                {
                    log.Error("The answers of the shared technique learning could not be sent.", e);
                    EndTechSync(e.Message);
                }
            });
        }

        /// <summary>The technique exchange is over: done, an error (again in a few minutes), or a script older than 2.16.5 (not again now).</summary>
        private void EndTechSync(string error)
        {
            bool unsupported = !string.IsNullOrEmpty(error) && error.IndexOf("techLearning", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (error.Contains("Άγνωστη ενέργεια") || error.IndexOf("Unknown action", StringComparison.OrdinalIgnoreCase) >= 0);
            if (unsupported)
            {
                techShareUnsupported = true;
                Status(SwimLang.T("Η κοινή μάθηση της τεχνικής ανάλυσης με τα άλλα Kinovea θέλει το script 2.16.5 στο Google Sheet: ως τότε μένει σε αυτόν τον υπολογιστή."), false);
            }
            else if (!string.IsNullOrEmpty(error))
            {
                log.DebugFormat("Shared technique learning: {0} (again in a few minutes).", error);
            }

            if (techShareMerged > 0)
            {
                Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("🔄 Κοινή μάθηση τεχνικής: +{0} απαντήσεις από τα άλλα Kinovea."), techShareMerged), false);
                techShareMerged = 0;
                TechLearningCameIn();
            }

            // The learning of the marks was done: the next exchange in ten minutes, unless this one failed (a few minutes).
            FinishLearningSync(unsupported || string.IsNullOrEmpty(error));
        }

        /// <summary>The learned limits and the agreements changed: the automatic scores of the training open now follow, and the page.</summary>
        private void TechLearningCameIn()
        {
            try
            {
                if (UpdateAutoEvals() > 0)
                    MarkDirty();
                if (form != null && !form.IsDisposed)
                    form.RefreshData();
            }
            catch (Exception e)
            {
                log.Error("Could not follow the shared technique learning.", e);
            }
        }
        #endregion
    }
}
