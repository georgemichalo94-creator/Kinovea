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
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.38.0) The PDF of a race is the report of the 2nd version (Apps Script 2.22.0 with its file SwimReportV2): the requests ask for
    /// it (v2), the web app answers a PDF with the pages of the report (HTML) in place of the PDF of the sheet, Kinovea prints them
    /// (SwimPdfPrinter: the Edge of the computer, with the fonts of the report) and sends the PDF back ("pdfStore"); it goes where the PDF
    /// of the sheet went (the folder of the reports, the summary, «Αρχεία Αθλητών/τριων», the package) and its answer is the answer of
    /// a PDF, as always. Pages that cannot be printed or kept: the PDF of the sheet, as before, and why.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>The Apps Script that makes the pages of the 2nd version.</summary>
        private static readonly Version v2Backend = new Version(2, 22, 0);

        /// <summary>The races whose pages are being printed.</summary>
        private readonly HashSet<string> v2Printing = new HashSet<string>();

        /// <summary>The next request of these races without the 2nd version (its pages were not printed or not kept).</summary>
        private readonly HashSet<string> v2SkipOnce = new HashSet<string>();

        /// <summary>The seconds of the request of the pages and of their print, until the PDF is kept (the time of the PDF).</summary>
        private readonly Dictionary<string, double[]> v2Times = new Dictionary<string, double[]>();

        /// <summary>The races whose pages the web app could not make (said once each).</summary>
        private readonly HashSet<string> v2ErrorSaid = new HashSet<string>();

        /// <summary>Prints that failed in a row: after two the PDF of the sheet until Kinovea starts again.</summary>
        private int v2Failures;
        private bool v2Off;
        private bool v2BrowserWarned;
        private bool v2BackendWarned;

        /// <summary>
        /// The flags of the requests of a race: those of its package (the PDF itself and the coaches' emails) and, for a race, the PDF of
        /// the 2nd version when this computer can print it (an update carries it too: an update that becomes a PDF keeps it).
        /// </summary>
        private JObject RequestFlags(SwimRace race)
        {
            JObject flags = PackageFlags(race);
            if (race == null || v2SkipOnce.Remove(race.Id ?? "") || !WantsV2(race))
                return flags;

            flags = flags ?? new JObject();
            flags["v2"] = true;
            return flags;
        }

        private bool WantsV2(SwimRace race)
        {
            return race != null && !race.IsTraining && Settings != null && Settings.PdfV2 && !v2Off && !SwimLang.English &&
                SwimPdfPrinter.BrowserPath != null;
        }

        /// <summary>The PDF button: without a browser to print the pages, the PDF of the sheet as before (said once).</summary>
        private void V2BrowserNote()
        {
            if (v2BrowserWarned || Settings == null || !Settings.PdfV2 || Race == null || Race.IsTraining || SwimLang.English ||
                SwimPdfPrinter.BrowserPath != null)
                return;

            v2BrowserWarned = true;
            Status(SwimLang.T("Δεν βρέθηκε ο Microsoft Edge (ούτε το Google Chrome) για να τυπωθεί η νέα αναφορά: το PDF γίνεται από το φύλλο, όπως πριν."), true);
        }

        /// <summary>
        /// The answer of a request of a race: its pages to print (v2), or why the web app made the PDF of the sheet instead (the pages
        /// failed there; its project without the file SwimReportV2; an Apps Script before 2.22.0).
        /// </summary>
        private void V2Answer(SwimRace race, SwimSheetResult result)
        {
            JObject raw = result != null ? result.Raw : null;
            if (race == null || raw == null)
                return;

            JObject v2 = raw["v2"] as JObject;
            if (v2 != null)
            {
                PrintV2ForPdf(race, v2, result.Seconds);
                return;
            }

            string error = raw.Value<string>("v2Error");
            if (!string.IsNullOrEmpty(error))
            {
                log.Warn("The web app did not make the pages of the report: " + error);
                if (v2ErrorSaid.Add(race.Id ?? ""))
                    Status(string.Format(SwimLang.T("Η νέα αναφορά δεν έγινε στο Apps Script ({0}): το PDF του φύλλου, όπως πριν."), error), true);
                return;
            }

            if (raw.Value<bool?>("v2Missing") == true)
            {
                if (!v2BackendWarned)
                {
                    v2BackendWarned = true;
                    Status(SwimLang.T("Το Apps Script του KOE App δεν έχει το αρχείο SwimReportV2: το PDF του φύλλου, όπως πριν. Πρόσθεσέ το (δες τον οδηγό, «Ενημέρωση του script»)."), true);
                }

                return;
            }

            // An Apps Script before 2.22.0 made the PDF of the sheet (it does not know the pages): said once.
            Version backend;
            string version = raw.Value<string>("version");
            if (!v2BackendWarned && !string.IsNullOrEmpty(result.PdfUrl) && WantsV2(race) && !string.IsNullOrEmpty(version) &&
                Version.TryParse(version, out backend) && backend < v2Backend)
            {
                v2BackendWarned = true;
                Status(string.Format(CultureInfo.InvariantCulture,
                    SwimLang.T("Το PDF το έφτιαξε το Apps Script {0} από το φύλλο: για τη νέα αναφορά βάλε την έκδοση {1} με το αρχείο SwimReportV2 (δες τον οδηγό, «Ενημέρωση του script»)."),
                    version, v2Backend.ToString(3)), true);
            }
        }

        /// <summary>The pages printed on a background thread (Edge takes a few seconds), then sent to be kept.</summary>
        private void PrintV2ForPdf(SwimRace race, JObject v2, double requestSeconds)
        {
            string html = v2.Value<string>("html");
            if (string.IsNullOrEmpty(html) || !v2Printing.Add(race.Id ?? ""))
                return;

            string htmlShort = v2.Value<string>("htmlShort");
            int pages = v2.Value<int?>("pages") ?? 0;

            // (2.42.0) The pictures of the snapshots the pages ask for ({{SNAP:id}}: the exchanges of a relay), from their files here.
            html = WithSnapshots(race, html);
            htmlShort = WithSnapshots(race, htmlShort);

            // (2.39.0) The slides of the race for its package, asked with its pages: made after its PDF (ShootSlides). An Apps Script
            // before 2.26.0 does not send them: said in the window of the package.
            SwimPackage slidesFor = PackageWaitingPdf(race);
            JObject slides = v2["slides"] as JObject;
            string slidesHtml = slides != null ? slides.Value<string>("html") : null;
            if (slidesFor != null && (!slidesFor.SlidesWanted || !slidesFor.SlidesWaiting || slidesFor.SlidesShooting))
                slidesFor = null;
            if (slidesFor != null && string.IsNullOrEmpty(slidesHtml))
            {
                string slidesError = v2.Value<string>("slidesError");
                slidesFor.SlidesWaiting = false;
                slidesFor.SlidesNote = !string.IsNullOrEmpty(slidesError)
                    ? string.Format(SwimLang.T("Οι διαφάνειες δεν έγιναν στο Apps Script ({0}): το βίντεο χωρίς αυτές."), slidesError)
                    : SwimLang.T("Χωρίς διαφάνειες: θέλουν το Apps Script 2.26.0 στο KOE App (δες τον οδηγό, «Ενημέρωση του script»).");
                if (!string.IsNullOrEmpty(slidesError))
                    log.Warn("The web app did not make the slides: " + slidesError);
                slidesFor = null;
            }

            if (slidesFor != null)
                slidesFor.SlidesShooting = true;
            SyncState = SwimSyncState.Sending;
            SyncMessage = SwimLang.T("🖨️ Το PDF τυπώνεται…");
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("🖨️ Η αναφορά ({0} σελίδες) τυπώνεται σε PDF…"), pages), false);
            if (form != null)
                form.RefreshSync();

            SynchronizationContext ui = SynchronizationContext.Current ?? new SynchronizationContext();
            Task.Factory.StartNew(() =>
            {
                SwimPdfPrinter.Result main = SwimPdfPrinter.Print(html);
                SwimPdfPrinter.Result part = main.Ok && !string.IsNullOrEmpty(htmlShort) ? SwimPdfPrinter.Print(htmlShort) : null;
                return new[] { main, part };
            }).ContinueWith(t =>
            {
                SwimPdfPrinter.Result[] printed = t.IsFaulted
                    ? new[] { new SwimPdfPrinter.Result { Error = t.Exception.GetBaseException().Message }, null }
                    : t.Result;
                ui.Post(_ => V2Printed(race, printed[0], printed[1], pages, requestSeconds), null);

                // (2.39.0) Then the slides of its package (the browser of the PDF closed first).
                if (slidesFor != null)
                    ShootSlides(slidesFor, slidesHtml, ui);
            });
        }

        /// <summary>A picture that is not there: one transparent pixel (the page keeps its place and its background).</summary>
        private const string NoPicture = "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

        /// <summary>The widest a picture of a snapshot goes into the pages (pixels): sharp in print, a small PDF.</summary>
        private const int SnapshotPdfWidth = 1100;

        /// <summary>
        /// (2.42.0) The pages with the pictures of the snapshots they ask for: {{SNAP:id}} becomes the picture of the race's snapshot of that
        /// id (a JPEG data URI, at most SnapshotPdfWidth wide), or one transparent pixel when it has none.
        /// </summary>
        private string WithSnapshots(SwimRace race, string html)
        {
            return FillSnapshots(html, id =>
            {
                SwimSnapshot s = race != null && race.Snapshots != null ? race.Snapshots.FirstOrDefault(x => x.Id == id) : null;
                return SnapshotDataUri(s != null ? SnapshotPath(s) : null);
            });
        }

        /// <summary>
        /// (2.42.0) {{SNAP:id}} in the pages → pictureOf(id) (a data URI), or one transparent pixel when it gives none; each id read once.
        /// Nothing of the screen: also in the CI.
        /// </summary>
        internal static string FillSnapshots(string html, Func<string, string> pictureOf)
        {
            if (string.IsNullOrEmpty(html) || html.IndexOf("{{SNAP:", StringComparison.Ordinal) < 0)
                return html;

            Dictionary<string, string> done = new Dictionary<string, string>();
            return Regex.Replace(html, @"\{\{SNAP:([0-9A-Za-z_\-]{1,64})\}\}", m =>
            {
                string id = m.Groups[1].Value;
                string uri;
                if (!done.TryGetValue(id, out uri))
                {
                    string picture = pictureOf != null ? pictureOf(id) : null;
                    uri = string.IsNullOrEmpty(picture) ? NoPicture : picture;
                    done[id] = uri;
                }

                return uri;
            });
        }

        /// <summary>A picture as a JPEG data URI, at most SnapshotPdfWidth wide; null when it cannot be read.</summary>
        internal static string SnapshotDataUri(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                using (Image image = Image.FromFile(path))
                {
                    int w = Math.Min(SnapshotPdfWidth, image.Width);
                    int h = Math.Max(1, (int)Math.Round(image.Height * (double)w / Math.Max(1, image.Width)));
                    using (Bitmap small = new Bitmap(w, h, PixelFormat.Format24bppRgb))
                    using (MemoryStream stream = new MemoryStream())
                    {
                        using (Graphics g = Graphics.FromImage(small))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(image, new Rectangle(0, 0, w, h));
                        }

                        ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                        if (jpeg == null)
                            return null;
                        using (EncoderParameters quality = new EncoderParameters(1))
                        {
                            quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 84L);
                            small.Save(stream, jpeg, quality);
                        }

                        return "data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray());
                    }
                }
            }
            catch (Exception e)
            {
                log.Warn("A picture of a snapshot could not go into the pages: " + e.Message);
                return null;
            }
        }

        private void V2Printed(SwimRace race, SwimPdfPrinter.Result main, SwimPdfPrinter.Result part, int pages, double requestSeconds)
        {
            v2Printing.Remove(race.Id ?? "");
            if (main == null || !main.Ok || main.Pdf == null)
            {
                string why = main != null && !string.IsNullOrEmpty(main.Error) ? main.Error : "?";
                log.Warn("The pages of the report were not printed: " + why);
                if (++v2Failures >= 2)
                    v2Off = true;
                Status(string.Format(SwimLang.T("Το PDF της νέας αναφοράς δεν τυπώθηκε ({0}): γίνεται το PDF του φύλλου, όπως πριν."), why), true);
                SheetPdfInstead(race);
                return;
            }

            v2Failures = 0;
            log.InfoFormat(CultureInfo.InvariantCulture, "The pages of the report printed by {0} ({1}): {2} pages of {3} in {4:0.0} s{5}.",
                main.Browser, main.Method, main.Pages, pages, main.Seconds,
                part == null ? "" : part.Ok ? string.Format(CultureInfo.InvariantCulture, ", the short analysis {0} pages in {1:0.0} s", part.Pages, part.Seconds) : ", the short analysis not: " + part.Error);
            if (pages > 0 && main.Pages > 0 && main.Pages != pages)
                log.Warn(string.Format(CultureInfo.InvariantCulture, "The PDF of the report has {0} pages, its pages are {1}.", main.Pages, pages));

            if (Settings == null || !Settings.IsConfigured)
                return;

            v2Times[race.Id ?? ""] = new[] { requestSeconds, main.Seconds + (part != null ? part.Seconds : 0) };
            JObject extra = PackageFlags(race) ?? new JObject();
            extra["raceId"] = race.Id ?? "";
            extra["pdf"] = Convert.ToBase64String(main.Pdf);
            if (part != null && part.Ok && part.Pdf != null)
                extra["pdfShort"] = Convert.ToBase64String(part.Pdf);

            string body;
            if (race == Race)
            {
                body = SwimPayload.Build(Race, Computed, TechPhases, Timing, AnalysisOrigin, Settings, VideoName, "pdfStore", FinalForRace(), false, extra);
            }
            else
            {
                SwimComputed computed = SwimCalculator.Compute(race, Timing, Settings.StrokeRounding);
                List<SwimTechPhase> tech = SwimTechCalculator.Compute(race, computed, Timing);
                body = SwimPayload.Build(race, computed, tech, Timing, AnalysisOrigin, Settings, VideoName, "pdfStore",
                    Finals != null ? Finals.For(race, Settings.FinalsSet) : null, false, extra);
            }

            race.SheetPending = true;
            client.EnqueueStore(Settings.WebAppUrl, body, race.Id);
        }

        /// <summary>A PDF printed here that the web app did not keep (an error, not the connection): the PDF of the sheet, as before.</summary>
        private void V2StoreFailed(SwimSyncEventArgs e)
        {
            if (e == null || !e.Store || e.State != SwimSyncState.Error || string.IsNullOrEmpty(e.RaceId) || VideoFile == null)
                return;

            SwimRace race = VideoFile.Races.FirstOrDefault(r => r.Id == e.RaceId);
            v2Times.Remove(e.RaceId);
            if (race == null)
                return;

            log.Warn("The PDF printed here was not kept by the web app: " + e.Message);
            Status(string.Format(SwimLang.T("Το PDF της νέας αναφοράς δεν αποθηκεύτηκε ({0}): γίνεται το PDF του φύλλου, όπως πριν."), e.Message), true);
            SheetPdfInstead(race);
        }

        /// <summary>The PDF of the sheet for this race, as before the 2nd version (this request only).</summary>
        private void SheetPdfInstead(SwimRace race)
        {
            v2SkipOnce.Add(race.Id ?? "");
            try
            {
                SendRace(race, true, true);
            }
            finally
            {
                v2SkipOnce.Remove(race.Id ?? "");
            }
        }

        /// <summary>
        /// The time of the PDF kept: from the PDF button, the pages of the web app («report»), their print («PDF») and the Drive with the
        /// KOE App («KOE App»), as the PDF of the sheet says its parts (PdfTimeText).
        /// </summary>
        private void V2Timings(SwimRace race, SwimSheetResult result)
        {
            double[] before;
            if (race == null || result == null || result.Raw == null || !v2Times.TryGetValue(race.Id ?? "", out before))
                return;

            v2Times.Remove(race.Id ?? "");
            double store = Math.Max(0, result.Seconds);
            JObject timings = new JObject();
            timings["report"] = (long)Math.Round(before[0] * 1000);
            timings["pdf"] = (long)Math.Round(before[1] * 1000);
            timings["archive"] = (long)Math.Round(store * 1000);
            timings["total"] = (long)Math.Round((before[0] + before[1] + store) * 1000);
            result.Raw["timings"] = timings;
            result.Seconds = before[0] + before[1] + store;
        }
    }
}
