#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The charts of the PDF report. The PDF of Google draws only the title of most of the charts of a sheet: the Apps Script sends the
    /// values of the charts that have no image for their current values (action "pdf" with drawCharts), Kinovea draws them with the
    /// graphics of the video, uploads them to the Drive and asks for the PDF again, which then has them in place of the charts.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>Images in each upload: a request of the web app stays short (it is not cut by a timeout).</summary>
        private const int ChartsPerUpload = 6;

        /// <summary>Charts kept for a race (the newest): the report has about twenty.</summary>
        private const int ChartsKept = 40;

        /// <summary>The races whose charts are being drawn and uploaded before their PDF.</summary>
        private readonly HashSet<string> chartsUploading = new HashSet<string>();

        /// <summary>The charts asked for by the Apps Script: drawn, uploaded, then the PDF (made with what there is, even after an error).</summary>
        private void DrawChartsForPdf(SwimRace race, JArray charts)
        {
            if (race == null || chartsUploading.Contains(race.Id))
                return;

            List<SwimChartData> list = (charts ?? new JArray()).OfType<JObject>().Select(SwimChartData.FromJson)
                .Where(c => !string.IsNullOrEmpty(c.Key) && !string.IsNullOrEmpty(c.Hash)).ToList();
            List<KeyValuePair<SwimChartData, byte[]>> images = new List<KeyValuePair<SwimChartData, byte[]>>();
            foreach (SwimChartData chart in list)
            {
                try
                {
                    images.Add(new KeyValuePair<SwimChartData, byte[]>(chart, SwimChartPainter.RenderPng(chart)));
                }
                catch (Exception e)
                {
                    log.Error("Could not draw a chart of the report (" + chart.Title + ").", e);
                }
            }

            if (images.Count == 0)
            {
                SendRace(race, true, false);
                return;
            }

            chartsUploading.Add(race.Id);
            SyncState = SwimSyncState.Sending;
            SyncMessage = string.Format(SwimLang.T("📊 Γραφήματα για το PDF: {0}…"), images.Count);
            Status(string.Format(SwimLang.T("📊 Τα γραφήματα του PDF ({0}) σχεδιάζονται με τα γραφικά του βίντεο και ανεβαίνουν…"), images.Count), false);
            UploadCharts(race, images, 0);
        }

        private void UploadCharts(SwimRace race, List<KeyValuePair<SwimChartData, byte[]>> images, int index)
        {
            if (index >= images.Count)
            {
                chartsUploading.Remove(race.Id);
                if (race.ChartImages != null && race.ChartImages.Count > ChartsKept)
                    race.ChartImages.RemoveRange(0, race.ChartImages.Count - ChartsKept);
                MarkDirty();
                SendRace(race, true, false);
                return;
            }

            List<KeyValuePair<SwimChartData, byte[]>> batch = images.Skip(index).Take(ChartsPerUpload).ToList();
            List<KeyValuePair<string, byte[]>> files = batch.Select(kv => new KeyValuePair<string, byte[]>(kv.Key.Key, kv.Value)).ToList();
            client.UploadImages(Settings.WebAppUrl, Settings.Token, race.Id, files, result =>
            {
                JArray uploaded = result.Ok && result.Raw != null ? result.Raw["files"] as JArray : null;
                if (uploaded == null)
                    log.Warn("The charts of the report were not uploaded: " + result.Error);

                foreach (JObject f in (uploaded ?? new JArray()).OfType<JObject>())
                {
                    string id = f.Value<string>("id") ?? "";
                    string fileId = f.Value<string>("fileId") ?? "";
                    SwimChartData chart = batch.Select(kv => kv.Key).FirstOrDefault(c => c.Key == id);
                    if (chart == null || fileId.Length == 0)
                    {
                        log.Warn("A chart of the report was not uploaded (" + id + "): " + f.Value<string>("error"));
                        continue;
                    }

                    if (race.ChartImages == null)
                        race.ChartImages = new List<SwimChartImage>();
                    race.ChartImages.RemoveAll(x => x == null || x.Id == id);
                    race.ChartImages.Add(new SwimChartImage { Id = id, FileId = fileId, Hash = chart.Hash, Painter = SwimChartPainter.Version });
                }

                UploadCharts(race, images, index + ChartsPerUpload);
            });
        }
    }
}
