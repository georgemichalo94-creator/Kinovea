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
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    public enum SwimSyncState
    {
        NotConfigured,
        Idle,
        Sending,
        Synced,
        Error,
        Offline
    }

    public class SwimSheetResult
    {
        public bool Ok;
        public bool Retry;
        public string Error;
        public string SheetName;
        public string SheetUrl;
        public string PdfUrl;
        public string SpreadsheetName;

        /// <summary>Google Drive file of an uploaded image.</summary>
        public string FileId;
        public JObject Raw;
    }

    public class SwimSyncEventArgs : EventArgs
    {
        public SwimSyncState State;
        public string Message;
        public string RaceId;
        public SwimSheetResult Result;
    }

    /// <summary>
    /// Sends the race state to the Google Apps Script web app.
    /// Requests are coalesced: while a request is in flight only the latest state is kept and sent next,
    /// so a fast series of key presses produces at most one queued request.
    /// Network work happens on a background thread, events are raised on the UI thread.
    /// </summary>
    public class SwimSheetClient
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private class Request
        {
            public string Url;
            public string Body;
            public string RaceId;
            public bool Pdf;
        }

        /// <summary>A PDF can take minutes (the Apps Script can run 6 minutes): it is not cut and sent again meanwhile.</summary>
        private const int PdfTimeout = 330000;
        private const int UpdateTimeout = 120000;
        private const int ImagesTimeout = 180000;

        public event EventHandler<SwimSyncEventArgs> StateChanged;

        public bool Busy
        {
            get { return inFlight; }
        }

        private readonly SynchronizationContext ui;
        private bool inFlight;
        /// <summary>Requests waiting for the one in flight: at most one per race (the newest state of the race).</summary>
        private readonly List<Request> pending = new List<Request>();

        /// <summary>The last state of each race that could not be sent (offline), retried later.</summary>
        private readonly Dictionary<string, Request> failed = new Dictionary<string, Request>();
        private int failures;
        private System.Windows.Forms.Timer retryTimer;

        public SwimSheetClient()
        {
            ui = SynchronizationContext.Current ?? new SynchronizationContext();
            retryTimer = new System.Windows.Forms.Timer();
            retryTimer.Tick += RetryTimer_Tick;

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (Exception e)
            {
                log.Error("Could not enable TLS 1.2.", e);
            }
        }

        /// <summary>
        /// Queue an update (or an update followed by a PDF export) of a race.
        /// </summary>
        public void Enqueue(string url, string body, string raceId, bool pdf)
        {
            Request request = new Request { Url = url, Body = body, RaceId = raceId, Pdf = pdf };

            if (inFlight)
            {
                // The newest state of a race replaces its older pending request (a pending PDF stays a PDF);
                // the requests of the other races keep their turn.
                int index = pending.FindIndex(p => p.RaceId == raceId);
                if (index >= 0)
                {
                    if (pending[index].Pdf)
                        AsPdf(request);
                    pending[index] = request;
                }
                else
                {
                    pending.Add(request);
                }

                return;
            }

            Start(request);
        }

        /// <summary>
        /// Test the connection. The callback runs on the UI thread.
        /// </summary>
        public void Ping(string url, string token, Action<SwimSheetResult> callback)
        {
            Simple(url, token, "ping", callback);
        }

        /// <summary>
        /// Download the athlete list of the main Google Sheet. The callback runs on the UI thread.
        /// </summary>
        public void GetAthletes(string url, string token, Action<SwimSheetResult> callback)
        {
            Simple(url, token, "athletes", callback);
        }

        /// <summary>
        /// An international athlete in the sheet "Διεθνείς αθλητές" (since 2.7.6): Raw["id"] his unique code (a new one, or his own when he
        /// is there already); Raw["duplicates"] the athletes he may be (same name and country, a year of birth unknown) unless force.
        /// The callback runs on the UI thread.
        /// </summary>
        public void AddAthlete(string url, string token, JObject athlete, bool force, Action<SwimSheetResult> callback)
        {
            JObject o = NewRequest(token, "addAthlete");
            o["athlete"] = athlete;
            o["force"] = force;
            Send(url, o.ToString(Newtonsoft.Json.Formatting.None), callback);
        }

        /// <summary>Another spelling of the name of an athlete (since 2.7.6). The callback runs on the UI thread.</summary>
        public void AddAlias(string url, string token, string id, string alias, Action<SwimSheetResult> callback)
        {
            JObject o = NewRequest(token, "addAlias");
            o["id"] = id;
            o["alias"] = alias;
            Send(url, o.ToString(Newtonsoft.Json.Formatting.None), callback);
        }

        /// <summary>
        /// Download the elite references (sheet "Elite αναφορές"). The callback runs on the UI thread.
        /// </summary>
        public void GetReferences(string url, string token, Action<SwimSheetResult> callback)
        {
            Simple(url, token, "references", callback);
        }

        /// <summary>
        /// Add (or replace) a reference in the sheet "Elite αναφορές". The callback runs on the UI thread.
        /// </summary>
        public void SaveReference(string url, string token, SwimReference reference, Action<SwimSheetResult> callback)
        {
            JObject o = new JObject();
            o["app"] = SwimPayload.AppId;
            o["token"] = token;
            o["action"] = "saveReference";
            o["reference"] = reference.ToJson();
            Send(url, o.ToString(Newtonsoft.Json.Formatting.None), callback);
        }

        /// <summary>
        /// Other analyses of the athlete in the same event (for the comparison with a previous race). The callback runs on the UI thread.
        /// </summary>
        public void GetAnalyses(string url, string token, JObject query, Action<SwimSheetResult> callback)
        {
            JObject o = NewRequest(token, "analyses");
            o["query"] = query;
            Send(url, o.ToString(Newtonsoft.Json.Formatting.None), callback);
        }

        /// <summary>
        /// Upload several images of a race together (the charts of the PDF report, PNG): Raw["files"] has [{ id, fileId } or { id, error }].
        /// The callback runs on the UI thread.
        /// </summary>
        public void UploadImages(string url, string token, string raceId, IList<KeyValuePair<string, byte[]>> images, Action<SwimSheetResult> callback)
        {
            JObject o = NewRequest(token, "images");
            o["raceId"] = raceId;
            JArray list = new JArray();
            foreach (KeyValuePair<string, byte[]> image in images)
            {
                JObject item = new JObject();
                item["id"] = image.Key;
                item["mime"] = "image/png";
                item["data"] = Convert.ToBase64String(image.Value);
                list.Add(item);
            }

            o["images"] = list;
            Send(url, o.ToString(Newtonsoft.Json.Formatting.None), callback, ImagesTimeout);
        }

        /// <summary>
        /// Upload a snapshot of the technical analysis (JPEG) to the Drive. The callback runs on the UI thread.
        /// </summary>
        public void UploadSnapshot(string url, string token, string raceId, string snapshotId, byte[] image, Action<SwimSheetResult> callback, string mime = "image/jpeg")
        {
            JObject o = NewRequest(token, "image");
            o["raceId"] = raceId;
            o["snapshotId"] = snapshotId;
            o["mime"] = mime;
            o["data"] = Convert.ToBase64String(image);
            Send(url, o.ToString(Newtonsoft.Json.Formatting.None), callback);
        }

        /// <summary>
        /// The library of elite frames (sheet "Elite καρέ"). The callback runs on the UI thread.
        /// </summary>
        public void GetEliteFrames(string url, string token, Action<SwimSheetResult> callback)
        {
            Simple(url, token, "eliteFrames", callback);
        }

        /// <summary>
        /// Add a frame to the elite library. The callback runs on the UI thread.
        /// </summary>
        public void SaveEliteFrame(string url, string token, JObject frame, byte[] jpeg, Action<SwimSheetResult> callback)
        {
            JObject o = NewRequest(token, "saveEliteFrame");
            o["frame"] = frame;
            o["mime"] = "image/jpeg";
            o["data"] = Convert.ToBase64String(jpeg);
            Send(url, o.ToString(Newtonsoft.Json.Formatting.None), callback);
        }

        /// <summary>
        /// Download an image of the snapshots or of the elite library (base64 in Raw["data"]). The callback runs on the UI thread.
        /// </summary>
        public void GetImage(string url, string token, string fileId, Action<SwimSheetResult> callback)
        {
            JObject o = NewRequest(token, "getImage");
            o["fileId"] = fileId;
            Send(url, o.ToString(Newtonsoft.Json.Formatting.None), callback);
        }

        private static JObject NewRequest(string token, string action)
        {
            JObject o = new JObject();
            o["app"] = SwimPayload.AppId;
            o["token"] = token;
            o["action"] = action;
            o["lang"] = SwimLang.Code;
            return o;
        }

        private void Simple(string url, string token, string action, Action<SwimSheetResult> callback)
        {
            Send(url, NewRequest(token, action).ToString(Newtonsoft.Json.Formatting.None), callback);
        }

        private void Send(string url, string body, Action<SwimSheetResult> callback, int timeout = 60000)
        {
            Task.Factory.StartNew(() => Post(url, body, timeout)).ContinueWith(t =>
            {
                SwimSheetResult result = t.IsFaulted ? new SwimSheetResult { Ok = false, Error = t.Exception.GetBaseException().Message } : t.Result;
                ui.Post(_ => callback(result), null);
            });
        }

        private void Start(Request request)
        {
            inFlight = true;

            // A newer state of a race that failed replaces its retry (and keeps a PDF that was asked for).
            Request old;
            if (request.RaceId != null && failed.TryGetValue(request.RaceId, out old))
            {
                if (old.Pdf)
                    AsPdf(request);
                failed.Remove(request.RaceId);
                if (failed.Count == 0)
                    retryTimer.Stop();
            }

            Raise(SwimSyncState.Sending, request.Pdf ? SwimLang.T("Δημιουργία PDF…") : SwimLang.T("Αποστολή…"), request.RaceId, null);

            Task.Factory.StartNew(() => Post(request.Url, request.Body, request.Pdf ? PdfTimeout : UpdateTimeout)).ContinueWith(t =>
            {
                SwimSheetResult result = t.IsFaulted ? new SwimSheetResult { Ok = false, Retry = true, Error = t.Exception.GetBaseException().Message } : t.Result;
                ui.Post(_ => Completed(request, result), null);
            });
        }

        /// <summary>
        /// A newer state of a race replaces a PDF that was asked for: it is sent as a PDF (its body too, which was built as an update),
        /// with its charts drawn by Kinovea when the Apps Script asks for them.
        /// </summary>
        private static void AsPdf(Request request)
        {
            request.Pdf = true;
            try
            {
                JObject o = JObject.Parse(request.Body);
                if (o.Value<string>("action") == "pdf")
                    return;
                o["action"] = "pdf";
                o["drawCharts"] = true;
                request.Body = o.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception e)
            {
                log.Error("Could not make a PDF request of an update.", e);
            }
        }

        private void Completed(Request request, SwimSheetResult result)
        {
            inFlight = false;

            if (result.Ok)
            {
                failures = 0;
                string message = SwimLang.T("Συγχρονίστηκε ") + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                Raise(SwimSyncState.Synced, message, request.RaceId, result);
            }
            else
            {
                failures++;
                SwimSyncState state = result.Retry ? SwimSyncState.Offline : SwimSyncState.Error;
                Raise(state, result.Error, request.RaceId, result);

                if (result.Retry)
                {
                    int index = pending.FindIndex(p => p.RaceId == request.RaceId);
                    if (index >= 0)
                    {
                        // A newer state of the race is waiting: it is sent instead (with the PDF if one was asked for).
                        if (request.Pdf)
                            AsPdf(pending[index]);
                    }
                    else
                    {
                        // Retry later with the same state, unless a newer state arrives first.
                        if (request.RaceId != null)
                            failed[request.RaceId] = request;
                        int delay = failures <= 1 ? 5000 : (failures <= 3 ? 15000 : 30000);
                        retryTimer.Interval = delay;
                        retryTimer.Start();
                    }
                }
            }

            if (pending.Count == 0 && result.Ok && failed.Count > 0)
            {
                // The connection is back: the states of the other races that failed are sent now.
                MoveFailedToPending();
            }

            if (pending.Count > 0)
            {
                Request next = pending[0];
                pending.RemoveAt(0);
                Start(next);
            }
        }

        private void MoveFailedToPending()
        {
            retryTimer.Stop();
            foreach (Request request in failed.Values)
            {
                if (!pending.Any(p => p.RaceId == request.RaceId))
                    pending.Add(request);
            }

            failed.Clear();
        }

        private void RetryTimer_Tick(object sender, EventArgs e)
        {
            retryTimer.Stop();
            if (failed.Count == 0)
                return;

            if (inFlight)
            {
                // Busy with another request: try again a little later.
                retryTimer.Start();
                return;
            }

            MoveFailedToPending();
            if (pending.Count > 0)
            {
                Request next = pending[0];
                pending.RemoveAt(0);
                Start(next);
            }
        }

        private void Raise(SwimSyncState state, string message, string raceId, SwimSheetResult result)
        {
            EventHandler<SwimSyncEventArgs> handler = StateChanged;
            if (handler != null)
                handler(this, new SwimSyncEventArgs { State = state, Message = message, RaceId = raceId, Result = result });
        }

        private static SwimSheetResult Post(string url, string body, int timeout)
        {
            SwimSheetResult result = new SwimSheetResult();
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url.Trim());
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";
                request.AllowAutoRedirect = true;
                request.Timeout = timeout;
                request.ReadWriteTimeout = timeout;
                request.UserAgent = "Kinovea-SwimAnalysis";

                byte[] data = Encoding.UTF8.GetBytes(body);
                request.ContentLength = data.Length;
                using (Stream stream = request.GetRequestStream())
                    stream.Write(data, 0, data.Length);

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    Parse(reader.ReadToEnd(), result);
                }
            }
            catch (WebException e)
            {
                result.Ok = false;
                HttpWebResponse response = e.Response as HttpWebResponse;
                if (response != null)
                {
                    int code = (int)response.StatusCode;
                    result.Error = string.Format(SwimLang.T("Σφάλμα HTTP {0} από το Google. Έλεγξε τη διεύθυνση της Εφαρμογής Ιστού."), code);
                    result.Retry = code >= 500 || code == 429;
                }
                else
                {
                    result.Error = SwimLang.T("Χωρίς σύνδεση με το Google (") + e.Status + SwimLang.T("). Τα δεδομένα είναι αποθηκευμένα, θα ξαναπροσπαθήσει.");
                    result.Retry = true;
                }
            }
            catch (UriFormatException)
            {
                result.Ok = false;
                result.Error = SwimLang.T("Μη έγκυρη διεύθυνση Εφαρμογής Ιστού.");
            }
            catch (Exception e)
            {
                result.Ok = false;
                result.Error = e.Message;
                result.Retry = true;
            }

            return result;
        }

        private static void Parse(string text, SwimSheetResult result)
        {
            JObject o;
            try
            {
                o = JObject.Parse(text);
            }
            catch (Exception)
            {
                result.Ok = false;
                result.Error = SwimLang.T("Η απάντηση δεν είναι JSON. Έλεγξε ότι η Εφαρμογή Ιστού έχει πρόσβαση \"Οποιοσδήποτε\" και ότι η διεύθυνση τελειώνει σε /exec.");
                return;
            }

            result.Raw = o;
            result.Ok = o.Value<bool?>("ok") ?? false;
            result.Retry = o.Value<bool?>("retry") ?? false;
            result.Error = o.Value<string>("error");
            result.SheetName = o.Value<string>("sheetName");
            result.SheetUrl = o.Value<string>("sheetUrl") ?? o.Value<string>("url");
            result.SpreadsheetName = o.Value<string>("spreadsheet");
            result.FileId = o.Value<string>("fileId");

            JObject pdf = o["pdf"] as JObject;
            if (pdf != null)
                result.PdfUrl = pdf.Value<string>("url");
        }
    }

    /// <summary>
    /// Builds the JSON body expected by the Apps Script backend (SwimRaceAnalysis.gs).
    /// All event times are sent in seconds after the start signal; the backend does the analysis.
    /// </summary>
    public static class SwimPayload
    {
        /// <summary>Lets the backend recognize our requests inside a project that has its own doPost.</summary>
        public const string AppId = "kinovea-swim";

        public static string Build(SwimRace race, SwimComputed computed, List<SwimTechPhase> tech, SwimTiming timing, long timeOrigin,
            SwimSettings settings, string videoName, string action, SwimFinal finalOfEvent = null, bool drawCharts = false)
        {
            JObject r = new JObject();
            r["id"] = race.Id;
            r["kind"] = race.IsTraining ? SwimRace.KindTraining : SwimRace.KindRace;
            r["athlete"] = race.Athlete ?? "";
            r["athleteId"] = race.AthleteId ?? "";
            r["gender"] = race.AthleteGender ?? "";
            r["club"] = race.AthleteClub ?? "";
            r["category"] = race.AthleteCategory ?? "";
            r["birthYear"] = race.AthleteBirthYear ?? "";
            r["lane"] = race.Lane > 0 ? race.Lane.ToString(CultureInfo.InvariantCulture) : "";
            r["style"] = race.Style;
            r["relay"] = race.Relay;
            r["distance"] = race.Distance;
            r["distanceLabel"] = race.DistanceLabel;
            r["pool"] = race.Pool;
            r["poolUnit"] = race.IsYards ? SwimRace.UnitYards : SwimRace.UnitMeters;
            r["competition"] = race.Competition ?? "";
            r["location"] = race.Location ?? "";
            r["round"] = race.Round ?? "";
            r["date"] = race.Date ?? "";
            r["video"] = videoName ?? "";
            r["strokeRounding"] = settings.StrokeRounding;

            // The language of the report and the PDF (since 2.7.7; an older script makes them in Greek). The values stay in Greek.
            r["lang"] = SwimLang.Code;

            JArray checkpoints = new JArray();
            if (!race.IsTraining)
            {
                foreach (SwimRow row in computed.Rows)
                {
                    JObject cp = new JObject();
                    cp["d"] = row.D;
                    cp["t"] = row.T.HasValue ? (double?)Math.Round(row.T.Value, 3) : null;
                    checkpoints.Add(cp);
                }
            }

            r["checkpoints"] = checkpoints;
            r["strokes"] = Times(computed.Strokes);

            // Strokes of the segments confirmed or typed by the analyst: they have priority over the stroke times.
            if (!race.IsTraining && race.StrokeCounts != null && race.StrokeCounts.Count > 0)
            {
                JObject counts = new JObject();
                foreach (KeyValuePair<int, double> pair in race.StrokeCounts.OrderBy(p => p.Key))
                {
                    if (race.GetSplit(pair.Key) != null)
                        counts[pair.Key.ToString(CultureInfo.InvariantCulture)] = pair.Value;
                }

                if (counts.Count > 0)
                    r["segmentStrokes"] = counts;
            }
            r["kicks"] = Times(computed.Kicks);
            r["breakouts"] = Times(computed.Breakouts);
            // The release from the block at the official reaction time when it stands (since 2026-10-19).
            List<double> feetOff = new List<double>(computed.FeetOff);
            if (computed.BlockOfficial && computed.BlockTime.HasValue)
            {
                if (computed.BlockMarkTime.HasValue)
                    feetOff.Remove(computed.BlockMarkTime.Value);
                feetOff.Add(computed.BlockTime.Value);
                feetOff.Sort();
            }

            r["feetOff"] = Times(feetOff);
            r["entries"] = Times(computed.Entries);

            JObject manual = new JObject();
            // The typed (official) reaction time has priority over the one marked on the video.
            double? reaction = race.Reaction ?? (computed.ReactionTime.HasValue ? (double?)Math.Round(computed.ReactionTime.Value, 3) : null);
            manual["reaction"] = reaction;
            manual["entryDistHands"] = race.EntryDistHands;
            manual["entryDistHips"] = race.EntryDistHips;
            manual["entryVelHands"] = race.EntryVelHands;
            manual["entryVelHips"] = race.EntryVelHips;
            manual["entryVelFeet"] = race.EntryVelFeet;
            manual["underwaterDist"] = race.UnderwaterDist;
            manual["breakoutDist"] = race.BreakoutDist;
            manual["finishLastStrokeDist"] = race.FinishLastStrokeDist;

            JObject turns = new JObject();
            foreach (var pair in race.Turns)
            {
                JObject t = new JObject();
                t["underwaterDist"] = pair.Value.UnderwaterDist;
                t["turnTime"] = pair.Value.TurnTime;
                turns[pair.Key.ToString(CultureInfo.InvariantCulture)] = t;
            }

            manual["turns"] = turns;
            r["manual"] = manual;

            if (computed.Reference != null)
                r["reference"] = computed.Reference.ToJson();

            JObject compare = new JObject();
            compare["pb"] = race.ComparePb ?? "";
            r["compare"] = compare;

            // The final of the event kept on this computer (e.g. the 8 of an Olympic final): the report compares the race with them.
            if (finalOfEvent != null && finalOfEvent.Swimmers.Count >= 2 && !race.IsTraining)
                r["finals"] = Finals(finalOfEvent, SwimFinalsCompare.Metrics(race, computed, finalOfEvent));

            // Frame of the finish with the graphics of the video (once uploaded to the Drive, and while the finish is marked).
            if (race.FinalFrame != null && !string.IsNullOrEmpty(race.FinalFrame.FileId) && race.StartTimestamp.HasValue &&
                race.GetSplit(race.Distance) != null)
            {
                JObject final = new JObject();
                final["fileId"] = race.FinalFrame.FileId;
                final["t"] = Math.Round(timing.Seconds(race.FinalFrame.Timestamp, race.StartTimestamp.Value), 3);
                r["finalFrame"] = final;
            }

            // Graphics of the video for the report (the summary card, the race against the final), once uploaded to the Drive.
            JArray graphics = new JArray();
            foreach (SwimReportGraphic graphic in race.Graphics.Where(x => x != null && !string.IsNullOrEmpty(x.FileId)))
            {
                JObject item = new JObject();
                item["id"] = graphic.Id;
                item["fileId"] = graphic.FileId;
                graphics.Add(item);
            }

            if (graphics.Count > 0 && !race.IsTraining)
                r["graphics"] = graphics;

            // The charts of the report drawn by Kinovea (with the current drawing), with the hash of the values they show.
            JArray charts = new JArray();
            foreach (SwimChartImage chart in (race.ChartImages ?? new List<SwimChartImage>()).Where(x => x != null && !string.IsNullOrEmpty(x.FileId) &&
                !string.IsNullOrEmpty(x.Hash) && x.Painter == SwimChartPainter.Version))
            {
                JObject item = new JObject();
                item["id"] = chart.Id;
                item["fileId"] = chart.FileId;
                item["hash"] = chart.Hash;
                charts.Add(item);
            }

            if (charts.Count > 0 && !race.IsTraining)
                r["chartImages"] = charts;
            r["tech"] = Tech(race, tech, timing, timeOrigin);

            JObject body = new JObject();
            body["app"] = AppId;
            body["token"] = settings.Token ?? "";
            body["action"] = action;
            body["lang"] = SwimLang.Code;
            body["race"] = r;
            if (drawCharts && !race.IsTraining)
                body["drawCharts"] = true;
            return body.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// The swimmers of a final, fastest first (rank, name and time at each checkpoint in pool units), and each value of the race
        /// among them as the panel shows it: the athlete, his place, the winner, the mean, the best and the gap to the winner (text).
        /// </summary>
        private static JObject Finals(SwimFinal final, List<SwimFinalsMetric> metrics)
        {
            JObject f = new JObject();
            f["label"] = final.LabelGreek;
            f["source"] = final.Source ?? "";
            JArray swimmers = new JArray();
            foreach (SwimFinalist s in final.Swimmers.OrderBy(x => x.Rank > 0 ? x.Rank : int.MaxValue).Take(8))
            {
                JArray times = new JArray();
                foreach (KeyValuePair<int, double> c in s.Checkpoints)
                {
                    JObject t = new JObject();
                    t["d"] = c.Key;
                    t["t"] = Math.Round(c.Value, 2);
                    times.Add(t);
                }

                JObject o = new JObject();
                o["rank"] = s.Rank;
                o["name"] = s.Name ?? "";
                o["times"] = times;
                swimmers.Add(o);
            }

            f["swimmers"] = swimmers;

            // The swimmer of the final with the best place: the winner.
            int winner = -1;
            for (int i = 0; i < final.Swimmers.Count; i++)
            {
                if (final.Swimmers[i].Rank > 0 && (winner < 0 || final.Swimmers[i].Rank < final.Swimmers[winner].Rank))
                    winner = i;
            }

            JArray values = new JArray();
            foreach (SwimFinalsMetric m in metrics ?? new List<SwimFinalsMetric>())
            {
                string unit = m.Unit == "s" ? "" : " " + m.Unit;
                double? won = winner >= 0 && winner < m.Finalists.Count ? m.Finalists[winner] : null;
                JObject o = new JObject();
                o["group"] = m.Group;
                o["label"] = m.Label;
                o["athlete"] = m.Athlete.HasValue ? m.Format(m.Athlete) + unit : "";
                o["rank"] = m.Rank > 0 ? m.Rank.ToString(CultureInfo.InvariantCulture) + "/" + (m.Count + 1).ToString(CultureInfo.InvariantCulture) : "";
                o["winner"] = won.HasValue ? m.Format(won) + unit : "";
                o["mean"] = m.Mean.HasValue ? m.Format(m.Mean) + unit : "";
                o["best"] = m.Best.HasValue ? m.Format(m.Best) + unit : "";
                o["diff"] = m.Athlete.HasValue && won.HasValue
                    ? (m.Athlete.Value - won.Value >= 0 ? "+" : "−") + Math.Abs(m.Athlete.Value - won.Value).ToString(m.Decimals == 0 ? "0" : m.Decimals == 1 ? "0.0" : "0.00", CultureInfo.InvariantCulture)
                    : "";
                values.Add(o);
            }

            f["metrics"] = values;
            return f;
        }

        /// <summary>
        /// Technical analysis: values of each start and turn, angles and snapshots (times from the origin of their phase).
        /// </summary>
        private static JObject Tech(SwimRace race, List<SwimTechPhase> phases, SwimTiming timing, long timeOrigin)
        {
            JObject tech = new JObject();
            JArray list = new JArray();
            phases = phases ?? new List<SwimTechPhase>();
            foreach (SwimTechPhase p in phases)
            {
                if (p.Values.Count == 0)
                    continue;

                JObject o = new JObject();
                o["id"] = p.Id;
                o["kind"] = p.Kind == SwimPhaseKind.Turn ? "turn" : "start";
                o["wall"] = p.Wall;
                o["attempt"] = p.Attempt;
                o["label"] = p.TitleGreek ?? p.Title;
                JObject values = new JObject();
                foreach (KeyValuePair<string, double> pair in p.Values)
                    values[pair.Key] = pair.Value;
                o["values"] = values;
                list.Add(o);
            }

            tech["phases"] = list;

            Func<long, SwimTechPhase> phaseOf = ts => SwimTechCalculator.PhaseAt(phases, ts);
            Func<long, SwimTechPhase, double> seconds = (ts, phase) =>
                Math.Round(timing.Seconds(ts, phase != null ? phase.Origin : (race.StartTimestamp ?? timeOrigin)), 3);

            JArray angles = new JArray();
            foreach (SwimAngle a in race.Angles)
            {
                double? deg = a.Degrees;
                if (!deg.HasValue)
                    continue;

                SwimTechPhase phase = phaseOf(a.Timestamp);
                JObject o = new JObject();
                o["id"] = a.Id;
                o["name"] = string.IsNullOrWhiteSpace(a.Name) ? "Γωνία" : a.Name.Trim();
                o["t"] = seconds(a.Timestamp, phase);
                o["deg"] = Math.Round(deg.Value, 1);
                o["phase"] = phase != null ? phase.Id : "";
                angles.Add(o);
            }

            tech["angles"] = angles;

            JArray snapshots = new JArray();
            foreach (SwimSnapshot s in race.Snapshots)
            {
                SwimTechPhase phase = phaseOf(s.Timestamp);
                JObject o = new JObject();
                o["id"] = s.Id;
                o["t"] = seconds(s.Timestamp, phase);
                o["phase"] = phase != null ? phase.Id : "";
                o["stage"] = s.Stage ?? "";
                o["event"] = s.Event ?? "";
                o["comment"] = s.Comment ?? "";
                o["fileId"] = s.FileId ?? "";
                o["eliteFileId"] = s.EliteFileId ?? "";
                o["eliteLabel"] = s.EliteLabel ?? "";
                o["eliteComment"] = s.EliteComment ?? "";
                snapshots.Add(o);
            }

            tech["snapshots"] = snapshots;
            JObject extra = new JObject();
            extra["notes"] = race.TechNotes ?? "";
            tech["values"] = extra;
            return tech;
        }

        private static JArray Times(System.Collections.Generic.List<double> list)
        {
            JArray array = new JArray();
            foreach (double t in list)
                array.Add(Math.Round(t, 3));
            return array;
        }
    }
}
