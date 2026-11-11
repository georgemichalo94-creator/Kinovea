#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Kinovea.Camera.GoPro
{
    /// <summary>
    /// (Swim 2.20.0) The live image of one GoPro at a time: the camera turns its Wi-Fi on (by Bluetooth), the computer joins it, the
    /// camera sends its preview stream (MPEG-TS over UDP, port 8554), Kinovea receives it and the ffmpeg.exe next to the program decodes
    /// it into images of the size of the screen. It closes by itself after three minutes (the Wi-Fi heats the camera and uses its
    /// battery, and the computer is off its own network meanwhile when it has only one Wi-Fi). When it ends, the computer goes back to
    /// its network and the camera turns its Wi-Fi off.
    /// </summary>
    internal sealed class GoProPreview
    {
        public const int Width = GoProStatusPainter.Width;
        public const int Height = GoProStatusPainter.Height;

        /// <summary>The live image closes by itself after this time.</summary>
        public const int MaxSeconds = 180;

        private const int FrameBytes = Width * Height * 3;
        private const int UdpPort = 8554;
        private const int SioUdpConnReset = -1744830452;
        private const string CameraIp = "10.5.5.9";
        private const string CameraAddress = "http://" + CameraIp + ":8080";

        private static readonly object currentLock = new object();
        private static GoProPreview current;
        private static readonly Dictionary<ulong, KeyValuePair<DateTime, string>> outcomes = new Dictionary<ulong, KeyValuePair<DateTime, string>>();
        private static bool exitHooked;

        /// <summary>The address of the camera whose live image is on (0 when none).</summary>
        public static ulong CurrentAddress
        {
            get { lock (currentLock) { return current != null ? current.address : 0; } }
        }

        private readonly ulong address;
        private readonly GoProDevice device;
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private readonly object frameLock = new object();
        private byte[] latest;
        private DateTime latestAt = DateTime.MinValue;
        private DateTime firstFrameAt = DateTime.MinValue;
        private long frames;
        private long packets;
        private string status;
        private GoProWifi wifi;
        private UdpClient udp;
        private Process ffmpeg;
        private Task running;
        private int cleaned;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private GoProPreview(GoProDevice device)
        {
            this.device = device;
            this.address = device.Address;
        }

        #region Start and stop
        /// <summary>The live image of this camera (the one of another camera stops first).</summary>
        public static void Start(GoProDevice device)
        {
            HookExit();

            // (2.21.0) The Wi-Fi of the computer is taken by a download of videos: the live image waits for it.
            if (GoProMedia.Busy)
            {
                lock (currentLock)
                    outcomes[device.Address] = new KeyValuePair<DateTime, string>(DateTime.UtcNow,
                        GoProText.T("περιμένει το κατέβασμα των βίντεο (Wi-Fi) να τελειώσει.", "waits for the download of the videos (Wi-Fi) to end."));

                return;
            }

            GoProPreview previous;
            GoProPreview preview = new GoProPreview(device);
            lock (currentLock)
            {
                previous = current;
                current = preview;
                outcomes.Remove(device.Address);
            }

            Task stopping = previous != null ? previous.StopAsync() : Task.FromResult(0);
            preview.running = Task.Run(async () =>
            {
                await stopping.ConfigureAwait(false);
                await preview.RunAsync().ConfigureAwait(false);
            });
        }

        /// <summary>(2.21.0) Stops the live image of any camera and waits until the computer is back on its network.</summary>
        public static Task StopAnyAsync()
        {
            GoProPreview preview;
            lock (currentLock)
            {
                preview = current;
                current = null;
            }

            return preview != null ? preview.StopAsync() : Task.FromResult(0);
        }

        /// <summary>Stops the live image of this camera (if it is on).</summary>
        public static void Stop(ulong address)
        {
            GoProPreview preview = null;
            lock (currentLock)
            {
                if (current != null && current.address == address)
                {
                    preview = current;
                    current = null;
                }
            }

            if (preview != null)
            {
                Task t = preview.StopAsync();
            }
        }

        /// <summary>The live image of this camera, if it is on (null otherwise).</summary>
        public static GoProPreview For(ulong address)
        {
            lock (currentLock)
                return current != null && current.address == address ? current : null;
        }

        /// <summary>How the last live image of this camera ended by itself (an error, the time limit), for a minute; else null.</summary>
        public static string LastOutcome(ulong address)
        {
            lock (currentLock)
            {
                KeyValuePair<DateTime, string> outcome;
                if (!outcomes.TryGetValue(address, out outcome))
                    return null;

                return (DateTime.UtcNow - outcome.Key).TotalSeconds < 60 ? outcome.Value : null;
            }
        }

        private Task StopAsync()
        {
            cancel.Cancel();
            Task r = running;
            return Task.Run(async () =>
            {
                if (r != null)
                {
                    try { await r.ConfigureAwait(false); }
                    catch (Exception) { }
                }

                await CleanUpAsync().ConfigureAwait(false);
            });
        }

        /// <summary>The program closes with the live image on: the computer goes back to its network at once.</summary>
        private static void HookExit()
        {
            lock (currentLock)
            {
                if (exitHooked)
                    return;

                exitHooked = true;
            }

            AppDomain.CurrentDomain.ProcessExit += (s, e) => LeaveNow();
            System.Windows.Forms.Application.ApplicationExit += (s, e) => LeaveNow();
        }

        private static void LeaveNow()
        {
            GoProPreview preview;
            lock (currentLock)
            {
                preview = current;
                current = null;
            }

            if (preview == null)
                return;

            try
            {
                preview.cancel.Cancel();
                if (preview.ffmpeg != null && !preview.ffmpeg.HasExited)
                    preview.ffmpeg.Kill();

                if (preview.udp != null)
                    preview.udp.Close();

                if (preview.wifi != null)
                    preview.wifi.Dispose();
            }
            catch (Exception e)
            {
                log.Error("Error while giving the Wi-Fi back at the end of the program.", e);
            }
        }
        #endregion

        #region Frames
        /// <summary>What the live image is doing (null once the images come).</summary>
        public string Status
        {
            get { lock (frameLock) { return status; } }
        }

        /// <summary>The seconds before the live image closes by itself (counted from its first image).</summary>
        public int SecondsLeft
        {
            get
            {
                lock (frameLock)
                {
                    if (firstFrameAt == DateTime.MinValue)
                        return MaxSeconds;

                    return Math.Max(0, MaxSeconds - (int)(DateTime.UtcNow - firstFrameAt).TotalSeconds);
                }
            }
        }

        /// <summary>Copies the last image (BGR, top-down) if it is less than a second old.</summary>
        public bool CopyLatest(byte[] target)
        {
            lock (frameLock)
            {
                if (latest == null || (DateTime.UtcNow - latestAt).TotalSeconds > 1)
                    return false;

                Array.Copy(latest, target, Math.Min(latest.Length, target.Length));
                return true;
            }
        }

        private void SetStatus(string text)
        {
            lock (frameLock)
                status = text;
        }
        #endregion

        private async Task RunAsync()
        {
            CancellationToken token = cancel.Token;
            string outcome = null;
            try
            {
                outcome = await StreamAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                log.Error("Error with the live image of the GoPro.", e);
                outcome = GoProText.T("σταμάτησε από λάθος: ξαναδοκίμασε.", "stopped on an error: try again.");
            }

            if (token.IsCancellationRequested)
                return;

            // Ended by itself (an error or the time limit): everything goes back now and the screen says why for a minute.
            lock (currentLock)
            {
                if (current == this)
                    current = null;

                if (outcome != null)
                    outcomes[address] = new KeyValuePair<DateTime, string>(DateTime.UtcNow, outcome);
            }

            await CleanUpAsync().ConfigureAwait(false);
        }

        /// <summary>The live image until it is stopped (returns null) or ends by itself (returns why).</summary>
        private async Task<string> StreamAsync(CancellationToken token)
        {
            device.SetPreviewWanted(true);
            SetStatus(GoProText.T("ανοίγει το Wi-Fi της κάμερας…", "turning the Wi-Fi of the camera on…"));
            if (!device.IsConnected)
                return GoProText.T("θέλει πρώτα τη σύνδεση Bluetooth της κάμερας.", "it first needs the Bluetooth connection of the camera.");

            await device.SetWifiAsync(true).ConfigureAwait(false);
            string[] credentials = null;
            for (int i = 0; i < 5 && credentials == null && !token.IsCancellationRequested; i++)
            {
                credentials = await device.ReadWifiCredentialsAsync().ConfigureAwait(false);
                if (credentials == null)
                    await Task.Delay(1000, token).ConfigureAwait(false);
            }

            if (credentials == null)
                return GoProText.T("δεν διαβάστηκε το Wi-Fi της κάμερας από το Bluetooth.", "the Wi-Fi of the camera could not be read by Bluetooth.");

            SetStatus(GoProText.T("ο υπολογιστής μπαίνει στο Wi-Fi της κάμερας…", "the computer joins the Wi-Fi of the camera…"));
            wifi = new GoProWifi();
            string ssid = credentials[0];
            string password = credentials[1];
            string error = await Task.Run(() => wifi.Join(ssid, password, 30000, token)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (error != null)
                return error;

            SetStatus(GoProText.T("η κάμερα ξεκινά την εικόνα της…", "the camera starts its image…"));
            StartReceiving();

            using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
            {
                bool started = false;
                for (int i = 0; i < 15 && !started && !token.IsCancellationRequested; i++)
                {
                    Punch();
                    started = await GetAsync(http, "/gopro/camera/stream/start").ConfigureAwait(false);
                    if (!started)
                        await Task.Delay(1000, token).ConfigureAwait(false);
                }

                token.ThrowIfCancellationRequested();
                if (!started)
                    return GoProText.T("η κάμερα δεν ξεκίνησε την εικόνα της (Wi-Fi).", "the camera did not start its image (Wi-Fi).");

                log.Debug("The stream of the live image is asked.");
                Stopwatch sinceStart = Stopwatch.StartNew();
                Stopwatch sinceAsk = Stopwatch.StartNew();
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);

                    DateTime first;
                    bool fresh;
                    long count;
                    lock (frameLock)
                    {
                        first = firstFrameAt;
                        fresh = latest != null && (DateTime.UtcNow - latestAt).TotalSeconds < 3;
                        count = frames;
                    }

                    // The time limit (from the first image).
                    if (first != DateTime.MinValue && (DateTime.UtcNow - first).TotalSeconds >= MaxSeconds)
                    {
                        log.DebugFormat("The live image closes after {0} s ({1} images).", MaxSeconds, count);
                        return GoProText.T("έκλεισε μόνη της μετά από 3 λεπτά (ζέστη και μπαταρία της κάμερας). Πάτα ξανά για να την ανοίξεις.",
                            "closed by itself after 3 minutes (heat and battery of the camera). Press again to open it.");
                    }

                    // No image at all: nothing comes (the firewall of Windows may hold the stream), or it comes and is not decoded.
                    if (first == DateTime.MinValue)
                    {
                        bool arriving = Interlocked.Read(ref packets) > 0;
                        if (sinceStart.Elapsed.TotalSeconds > 30)
                        {
                            log.DebugFormat("No image after 30 s ({0} packets).", Interlocked.Read(ref packets));
                            return arriving ?
                                GoProText.T("η εικόνα της κάμερας έρχεται αλλά δεν διαβάζεται (ffmpeg). Ξαναδοκίμασε.", "the image of the camera comes but cannot be read (ffmpeg). Try again.") :
                                GoProText.T("δεν ήρθε εικόνα από την κάμερα. Αν τα Windows ρώτησαν για το τείχος προστασίας, πάτα «Να επιτρέπεται η πρόσβαση» και ξαναδοκίμασε.",
                                    "no image came from the camera. If Windows asked about the firewall, press \"Allow access\" and try again.");
                        }

                        if (sinceStart.Elapsed.TotalSeconds > 6)
                            SetStatus(arriving ?
                                GoProText.T("η εικόνα έρχεται, διαβάζεται…", "the image comes, it is being read…") :
                                GoProText.T("δεν έρχεται ακόμη εικόνα. Αν τα Windows ρωτούν για το τείχος προστασίας, πάτα «Να επιτρέπεται η πρόσβαση».",
                                    "no image yet. If Windows asks about the firewall, press \"Allow access\"."));
                    }

                    // The camera is kept awake on its Wi-Fi; a stream that stopped (a mode without preview) is asked again.
                    if (sinceAsk.Elapsed.TotalSeconds >= 2.5)
                    {
                        sinceAsk.Restart();
                        await GetAsync(http, "/gopro/camera/keep_alive").ConfigureAwait(false);
                        if (!fresh)
                        {
                            if (first != DateTime.MinValue)
                                SetStatus(GoProText.T("περιμένει την εικόνα της κάμερας…", "waiting for the image of the camera…"));

                            Punch();
                            await GetAsync(http, "/gopro/camera/stream/start").ConfigureAwait(false);
                        }
                    }
                }
            }

            return null;
        }

        private static async Task<bool> GetAsync(HttpClient http, string path)
        {
            try
            {
                using (HttpResponseMessage response = await http.GetAsync(CameraAddress + path).ConfigureAwait(false))
                    return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// A small packet from the port of the stream to the camera: the stream that comes back on the same ports is then an answer for
        /// the firewall of Windows.
        /// </summary>
        private void Punch()
        {
            try
            {
                UdpClient u = udp;
                if (u != null)
                    u.Send(new byte[] { 0 }, 1, new IPEndPoint(IPAddress.Parse(CameraIp), UdpPort));
            }
            catch (Exception e)
            {
                log.Debug("Could not send to the camera.", e);
            }
        }

        /// <summary>The stream of the camera (UDP) into ffmpeg, and its images out of it.</summary>
        private void StartReceiving()
        {
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (!File.Exists(exe))
                throw new FileNotFoundException(GoProText.T("Δεν βρέθηκε το ffmpeg.exe δίπλα στο πρόγραμμα.", "ffmpeg.exe was not found next to the program."));

            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "-hide_banner -loglevel error -fflags nobuffer -flags low_delay -probesize 32768 -analyzeduration 0 " +
                    "-f mpegts -i pipe:0 -an -vf scale=" + Width + ":" + Height + " -pix_fmt bgr24 -f rawvideo pipe:1",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            ffmpeg = Process.Start(info);
            ffmpeg.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    log.Debug("ffmpeg (live image): " + e.Data);
            };
            ffmpeg.BeginErrorReadLine();

            udp = new UdpClient(new IPEndPoint(IPAddress.Any, UdpPort));
            udp.Client.ReceiveBufferSize = 4 * 1024 * 1024;

            // Windows reports the "port unreachable" answer to a packet sent from this socket (the packet towards the camera) as an
            // error of the next receive, which would end the stream: turned off.
            try
            {
                udp.Client.IOControl((IOControlCode)SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch (Exception e)
            {
                log.Debug("Could not turn off the reset of the UDP socket.", e);
            }

            Stream input = ffmpeg.StandardInput.BaseStream;
            Stream output = ffmpeg.StandardOutput.BaseStream;
            CancellationToken token = cancel.Token;

            Thread receive = new Thread(() => Receive(input, token)) { IsBackground = true, Name = "GoPro live image (UDP)" };
            Thread decode = new Thread(() => Decode(output, token)) { IsBackground = true, Name = "GoPro live image (frames)" };
            receive.Start();
            decode.Start();
        }

        private void Receive(Stream input, CancellationToken token)
        {
            IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                while (!token.IsCancellationRequested)
                {
                    byte[] datagram;
                    try
                    {
                        datagram = udp.Receive(ref from);
                    }
                    catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        // An answer to the packet towards the camera, not the end of the stream.
                        continue;
                    }

                    if (Interlocked.Increment(ref packets) == 1)
                        log.DebugFormat("The stream of the live image comes from {0}.", from);

                    input.Write(datagram, 0, datagram.Length);
                    input.Flush();
                }
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested)
                    log.Debug("The stream of the live image stopped.", e);
            }
        }

        private void Decode(Stream output, CancellationToken token)
        {
            byte[] frame = new byte[FrameBytes];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int read = 0;
                    while (read < FrameBytes)
                    {
                        int n = output.Read(frame, read, FrameBytes - read);
                        if (n <= 0)
                            return;

                        read += n;
                    }

                    lock (frameLock)
                    {
                        if (latest == null)
                            latest = new byte[FrameBytes];

                        Array.Copy(frame, latest, FrameBytes);
                        latestAt = DateTime.UtcNow;
                        if (firstFrameAt == DateTime.MinValue)
                        {
                            firstFrameAt = latestAt;
                            log.Debug("The first image of the live image came.");
                        }

                        frames++;
                        status = null;
                    }
                }
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested)
                    log.Debug("The images of the live image stopped.", e);
            }
        }

        /// <summary>Everything back (once): the stream stopped, the computer on its network again, the Wi-Fi of the camera off.</summary>
        private async Task CleanUpAsync()
        {
            if (Interlocked.Exchange(ref cleaned, 1) == 1)
                return;

            try
            {
                if (wifi != null)
                {
                    using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                        await GetAsync(http, "/gopro/camera/stream/stop").ConfigureAwait(false);
                }
            }
            catch (Exception) { }

            try
            {
                if (udp != null)
                    udp.Close();
            }
            catch (Exception) { }

            try
            {
                if (ffmpeg != null && !ffmpeg.HasExited)
                    ffmpeg.Kill();
            }
            catch (Exception) { }

            try
            {
                if (wifi != null)
                    wifi.Dispose();
            }
            catch (Exception e)
            {
                log.Error("Error while giving the Wi-Fi back.", e);
            }

            // Not when the live image of the same camera was opened again meanwhile.
            if (For(address) == null)
            {
                device.SetPreviewWanted(false);
                try
                {
                    if (device.IsConnected)
                        await device.SetWifiAsync(false).ConfigureAwait(false);
                }
                catch (Exception) { }
            }

            lock (frameLock)
                latest = null;

            log.DebugFormat("The live image ended ({0} images).", frames);
        }
    }
}
