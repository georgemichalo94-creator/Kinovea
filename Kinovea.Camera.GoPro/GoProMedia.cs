#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Kinovea.Camera.GoPro
{
    /// <summary>
    /// (Swim 2.21.0) The videos of the card of a GoPro by its Wi-Fi: the camera turns its Wi-Fi on (by Bluetooth), the computer joins it
    /// (as for the live image, which stops first), lists the videos of the card (Open GoPro: /gopro/media/list), downloads the chosen
    /// ones (/videos/DCIM/…) to a temporary name then to their name, and goes back to its network; the camera turns its Wi-Fi off.
    /// One camera at a time.
    /// </summary>
    internal static class GoProMedia
    {
        private const string CameraAddress = "http://10.5.5.9:8080";
        private static int busy;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>A download takes the Wi-Fi of the computer now.</summary>
        public static bool Busy
        {
            get { return Volatile.Read(ref busy) == 1; }
        }

        public static async Task<string> DownloadAsync(GoProDevice device, string alias, Func<IList<RemoteMediaFile>, IList<RemoteMediaDownload>> choose,
            IProgress<RemoteMediaProgress> progress, CancellationToken token)
        {
            if (device == null || !device.IsConnected)
                return GoProText.T("η κάμερα δεν είναι συνδεδεμένη (Bluetooth).", "the camera is not connected (Bluetooth).");

            if (Interlocked.Exchange(ref busy, 1) == 1)
                return GoProText.T("γίνεται ήδη ένα κατέβασμα: μία κάμερα τη φορά.", "a download is already running: one camera at a time.");

            GoProWifi wifi = null;
            try
            {
                // The live image of any camera ends first: the Wi-Fi of the computer is one.
                await GoProPreview.StopAnyAsync().ConfigureAwait(false);

                Report(progress, alias + ": " + GoProText.T("ανοίγει το Wi-Fi της κάμερας…", "turning the Wi-Fi of the camera on…"), 0, 0);
                device.SetPreviewWanted(true);
                await device.SetWifiAsync(true).ConfigureAwait(false);
                string[] credentials = null;
                for (int i = 0; i < 6 && credentials == null && !token.IsCancellationRequested; i++)
                {
                    credentials = await device.ReadWifiCredentialsAsync().ConfigureAwait(false);
                    if (credentials == null)
                        await Task.Delay(1000, token).ConfigureAwait(false);
                }

                if (credentials == null)
                    return GoProText.T("δεν διαβάστηκε το Wi-Fi της κάμερας από το Bluetooth.", "the Wi-Fi of the camera could not be read by Bluetooth.");

                Report(progress, alias + ": " + GoProText.T("ο υπολογιστής μπαίνει στο Wi-Fi της κάμερας…", "the computer joins the Wi-Fi of the camera…"), 0, 0);
                wifi = new GoProWifi();
                GoProWifi w = wifi;
                string ssid = credentials[0];
                string password = credentials[1];
                string error = await Task.Run(() => w.Join(ssid, password, 30000, token)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (error != null)
                    return error;

                using (HttpClient http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
                {
                    // The list of the card (the HTTP server of the camera may need a few seconds after the join).
                    IList<RemoteMediaFile> files = null;
                    for (int i = 0; i < 10 && files == null && !token.IsCancellationRequested; i++)
                    {
                        files = await ListAsync(http, token).ConfigureAwait(false);
                        if (files == null)
                            await Task.Delay(1000, token).ConfigureAwait(false);
                    }

                    if (files == null)
                        return GoProText.T("η κάμερα δεν έδωσε τη λίστα της κάρτας της (Wi-Fi).", "the camera did not give the list of its card (Wi-Fi).");

                    log.DebugFormat("The card of the GoPro {0:X12} has {1} files.", device.Address, files.Count);
                    IList<RemoteMediaDownload> wanted = choose(files) ?? new List<RemoteMediaDownload>();
                    long total = 0;
                    foreach (RemoteMediaDownload d in wanted)
                        total += Math.Max(0, d.File.Size);

                    long done = 0;
                    int n = 0;
                    foreach (RemoteMediaDownload d in wanted)
                    {
                        token.ThrowIfCancellationRequested();
                        n++;
                        string label = string.Format(CultureInfo.InvariantCulture, "{0}: {1} ({2}/{3})", alias, d.File.Name, n, wanted.Count);
                        long before = done;
                        string failed = await DownloadFileAsync(http, d, token, bytes =>
                        {
                            Report(progress, label, before + bytes, total);
                            device.SetNotice(GoProText.T("Κατέβασμα βίντεο ", "Downloading videos ") +
                                (total > 0 ? ((before + bytes) * 100 / total).ToString(CultureInfo.InvariantCulture) + " %" : "…"), false, 20);
                        }).ConfigureAwait(false);

                        if (failed != null)
                            return d.File.Name + ": " + failed;

                        done += Math.Max(0, d.File.Size);
                    }

                    device.SetNotice(GoProText.T("Τα βίντεο κατέβηκαν στον υπολογιστή ✓", "The videos are on the computer ✓"), false, 60);
                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                return GoProText.T("σταμάτησε.", "stopped.");
            }
            catch (Exception e)
            {
                log.Error("Error while downloading the videos of the GoPro.", e);
                return GoProText.T("το κατέβασμα σταμάτησε από λάθος: ξαναδοκίμασε.", "the download stopped on an error: try again.");
            }
            finally
            {
                if (wifi != null)
                {
                    try { wifi.Dispose(); }
                    catch (Exception e) { log.Error("Error while giving the Wi-Fi back.", e); }
                }

                device.SetPreviewWanted(false);
                try
                {
                    if (device.IsConnected)
                        await device.SetWifiAsync(false).ConfigureAwait(false);
                }
                catch (Exception) { }

                Interlocked.Exchange(ref busy, 0);
            }
        }

        private static void Report(IProgress<RemoteMediaProgress> progress, string text, long done, long total)
        {
            if (progress != null)
                progress.Report(new RemoteMediaProgress { Text = text, Done = done, Total = total });
        }

        /// <summary>The videos of the card (MP4), or null when the camera did not answer.</summary>
        private static async Task<IList<RemoteMediaFile>> ListAsync(HttpClient http, CancellationToken token)
        {
            try
            {
                using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    using (HttpResponseMessage response = await http.GetAsync(CameraAddress + "/gopro/media/list", timeout.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            return null;

                        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return ParseList(json);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (token.IsCancellationRequested)
                    throw;

                return null;
            }
            catch (Exception e)
            {
                log.Debug("The list of the card did not come.", e);
                return null;
            }
        }

        /// <summary>{"media":[{"d":"100GOPRO","fs":[{"n":"GX010123.MP4","cre":"1731320000","s":"123456"}…]}…]}: the MP4 files.</summary>
        internal static IList<RemoteMediaFile> ParseList(string json)
        {
            List<RemoteMediaFile> result = new List<RemoteMediaFile>();
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Dictionary<string, object> root = serializer.Deserialize<Dictionary<string, object>>(json);
            object media;
            if (root == null || !root.TryGetValue("media", out media) || !(media is ArrayList))
                return result;

            foreach (object folderObject in (ArrayList)media)
            {
                Dictionary<string, object> folder = folderObject as Dictionary<string, object>;
                if (folder == null)
                    continue;

                string d = folder.ContainsKey("d") ? Convert.ToString(folder["d"], CultureInfo.InvariantCulture) : "";
                object fs;
                if (!folder.TryGetValue("fs", out fs) || !(fs is ArrayList))
                    continue;

                foreach (object fileObject in (ArrayList)fs)
                {
                    Dictionary<string, object> file = fileObject as Dictionary<string, object>;
                    if (file == null || !file.ContainsKey("n"))
                        continue;

                    string name = Convert.ToString(file["n"], CultureInfo.InvariantCulture);
                    if (!name.EndsWith(".MP4", StringComparison.OrdinalIgnoreCase))
                        continue;

                    long created = 0;
                    long size = -1;
                    if (file.ContainsKey("cre"))
                        long.TryParse(Convert.ToString(file["cre"], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out created);

                    if (file.ContainsKey("s"))
                        long.TryParse(Convert.ToString(file["s"], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out size);

                    // The camera writes its own clock as seconds since 1970: the local time of the computer since it took its clock.
                    result.Add(new RemoteMediaFile
                    {
                        Folder = d,
                        Name = name,
                        Created = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddSeconds(created),
                        Size = size
                    });
                }
            }

            return result;
        }

        /// <summary>One file to a temporary name next to its place, then renamed (a stopped download leaves no file that looks whole).</summary>
        private static async Task<string> DownloadFileAsync(HttpClient http, RemoteMediaDownload d, CancellationToken token, Action<long> onBytes)
        {
            string target = d.TargetPath;
            string partial = target + ".part";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target))
                    return GoProText.T("υπάρχει ήδη αρχείο με αυτό το όνομα.", "a file with this name already exists.");

                string url = CameraAddress + "/videos/DCIM/" + Uri.EscapeDataString(d.File.Folder) + "/" + Uri.EscapeDataString(d.File.Name);
                using (HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        return GoProText.T("η κάμερα δεν έδωσε το αρχείο (", "the camera did not give the file (") + (int)response.StatusCode + ").";

                    using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
                    {
                        byte[] buffer = new byte[1 << 20];
                        long written = 0;
                        DateTime lastReport = DateTime.MinValue;
                        while (true)
                        {
                            int read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                            if (read <= 0)
                                break;

                            await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                            written += read;
                            if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 400)
                            {
                                lastReport = DateTime.UtcNow;
                                onBytes(written);
                            }
                        }

                        onBytes(written);
                        if (d.File.Size > 0 && written != d.File.Size)
                            return GoProText.T("το αρχείο δεν ήρθε ολόκληρο.", "the file did not come whole.");
                    }
                }

                File.Move(partial, target);
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                log.Error("Error while downloading a video of the GoPro.", e);
                return GoProText.T("το αρχείο δεν κατέβηκε: ", "the file did not download: ") + e.Message;
            }
            finally
            {
                try
                {
                    if (File.Exists(partial))
                        File.Delete(partial);
                }
                catch (Exception) { }
            }
        }
    }
}
