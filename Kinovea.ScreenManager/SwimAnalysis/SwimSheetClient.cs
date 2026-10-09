#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
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

        public event EventHandler<SwimSyncEventArgs> StateChanged;

        public bool Busy
        {
            get { return inFlight; }
        }

        private readonly SynchronizationContext ui;
        private bool inFlight;
        private Request pending;
        private Request lastFailed;
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
                // Keep a pending PDF request even if a plain update arrives for the same race.
                if (pending != null && pending.Pdf && pending.RaceId == raceId)
                    request.Pdf = true;

                pending = request;
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

        private void Simple(string url, string token, string action, Action<SwimSheetResult> callback)
        {
            JObject o = new JObject();
            o["app"] = SwimPayload.AppId;
            o["token"] = token;
            o["action"] = action;
            string body = o.ToString(Newtonsoft.Json.Formatting.None);

            Task.Factory.StartNew(() => Post(url, body)).ContinueWith(t =>
            {
                SwimSheetResult result = t.IsFaulted ? new SwimSheetResult { Ok = false, Error = t.Exception.GetBaseException().Message } : t.Result;
                ui.Post(_ => callback(result), null);
            });
        }

        private void Start(Request request)
        {
            inFlight = true;
            retryTimer.Stop();
            Raise(SwimSyncState.Sending, request.Pdf ? "Δημιουργία PDF…" : "Αποστολή…", request.RaceId, null);

            Task.Factory.StartNew(() => Post(request.Url, request.Body)).ContinueWith(t =>
            {
                SwimSheetResult result = t.IsFaulted ? new SwimSheetResult { Ok = false, Retry = true, Error = t.Exception.GetBaseException().Message } : t.Result;
                ui.Post(_ => Completed(request, result), null);
            });
        }

        private void Completed(Request request, SwimSheetResult result)
        {
            inFlight = false;

            if (result.Ok)
            {
                failures = 0;
                lastFailed = null;
                string message = "Συγχρονίστηκε " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                Raise(SwimSyncState.Synced, message, request.RaceId, result);
            }
            else
            {
                failures++;
                SwimSyncState state = result.Retry ? SwimSyncState.Offline : SwimSyncState.Error;
                Raise(state, result.Error, request.RaceId, result);

                if (result.Retry && pending == null)
                {
                    // Retry later with the same state, unless a newer state arrives first.
                    lastFailed = request;
                    int delay = failures <= 1 ? 5000 : (failures <= 3 ? 15000 : 30000);
                    retryTimer.Interval = delay;
                    retryTimer.Start();
                }
            }

            if (pending != null)
            {
                Request next = pending;
                pending = null;
                Start(next);
            }
        }

        private void RetryTimer_Tick(object sender, EventArgs e)
        {
            retryTimer.Stop();
            if (lastFailed == null || inFlight)
                return;

            Request request = lastFailed;
            lastFailed = null;
            Start(request);
        }

        private void Raise(SwimSyncState state, string message, string raceId, SwimSheetResult result)
        {
            EventHandler<SwimSyncEventArgs> handler = StateChanged;
            if (handler != null)
                handler(this, new SwimSyncEventArgs { State = state, Message = message, RaceId = raceId, Result = result });
        }

        private static SwimSheetResult Post(string url, string body)
        {
            SwimSheetResult result = new SwimSheetResult();
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url.Trim());
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";
                request.AllowAutoRedirect = true;
                request.Timeout = 60000;
                request.ReadWriteTimeout = 60000;
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
                    result.Error = string.Format("Σφάλμα HTTP {0} από το Google. Έλεγξε τη διεύθυνση της Εφαρμογής Ιστού.", code);
                    result.Retry = code >= 500 || code == 429;
                }
                else
                {
                    result.Error = "Χωρίς σύνδεση με το Google (" + e.Status + "). Τα δεδομένα είναι αποθηκευμένα, θα ξαναπροσπαθήσει.";
                    result.Retry = true;
                }
            }
            catch (UriFormatException)
            {
                result.Ok = false;
                result.Error = "Μη έγκυρη διεύθυνση Εφαρμογής Ιστού.";
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
                result.Error = "Η απάντηση δεν είναι JSON. Έλεγξε ότι η Εφαρμογή Ιστού έχει πρόσβαση \"Οποιοσδήποτε\" και ότι η διεύθυνση τελειώνει σε /exec.";
                return;
            }

            result.Raw = o;
            result.Ok = o.Value<bool?>("ok") ?? false;
            result.Retry = o.Value<bool?>("retry") ?? false;
            result.Error = o.Value<string>("error");
            result.SheetName = o.Value<string>("sheetName");
            result.SheetUrl = o.Value<string>("sheetUrl") ?? o.Value<string>("url");
            result.SpreadsheetName = o.Value<string>("spreadsheet");

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

        public static string Build(SwimRace race, SwimComputed computed, SwimSettings settings, string videoName, string action)
        {
            JObject r = new JObject();
            r["id"] = race.Id;
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
            r["competition"] = race.Competition ?? "";
            r["location"] = race.Location ?? "";
            r["round"] = race.Round ?? "";
            r["date"] = race.Date ?? "";
            r["video"] = videoName ?? "";
            r["strokeRounding"] = settings.StrokeRounding;

            JArray checkpoints = new JArray();
            foreach (SwimRow row in computed.Rows)
            {
                JObject cp = new JObject();
                cp["d"] = row.D;
                cp["t"] = row.T.HasValue ? (double?)Math.Round(row.T.Value, 3) : null;
                checkpoints.Add(cp);
            }

            r["checkpoints"] = checkpoints;
            r["strokes"] = Times(computed.Strokes);
            r["kicks"] = Times(computed.Kicks);
            r["breakouts"] = Times(computed.Breakouts);
            r["feetOff"] = Times(computed.FeetOff);
            r["entries"] = Times(computed.Entries);

            JObject manual = new JObject();
            manual["reaction"] = race.Reaction;
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

            JObject body = new JObject();
            body["app"] = AppId;
            body["token"] = settings.Token ?? "";
            body["action"] = action;
            body["race"] = r;
            return body.ToString(Newtonsoft.Json.Formatting.None);
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
