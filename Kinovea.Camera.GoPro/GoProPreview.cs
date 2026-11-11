#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
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
    /// it into images of the size of the screen. When it ends, the computer goes back to its network and the camera turns its Wi-Fi off.
    /// </summary>
    internal sealed class GoProPreview
    {
        public const int Width = GoProStatusPainter.Width;
        public const int Height = GoProStatusPainter.Height;
        private const int FrameBytes = Width * Height * 3;
        private const int UdpPort = 8554;
        private const string CameraAddress = "http://10.5.5.9:8080";

        private static readonly object currentLock = new object();
        private static GoProPreview current;

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
        private string status;
        private GoProWifi wifi;
        private UdpClient udp;
        private Process ffmpeg;
        private Task running;
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
            GoProPreview previous;
            GoProPreview preview = new GoProPreview(device);
            lock (currentLock)
            {
                previous = current;
                current = preview;
            }

            Task stopping = previous != null ? previous.StopAsync() : Task.FromResult(0);
            preview.running = Task.Run(async () =>
            {
                await stopping.ConfigureAwait(false);
                await preview.RunAsync().ConfigureAwait(false);
            });
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
        #endregion

        #region Frames
        /// <summary>What the live image is doing (null once the images come).</summary>
        public string Status
        {
            get { lock (frameLock) { return status; } }
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
            try
            {
                device.SetPreviewWanted(true);
                SetStatus(GoProText.T("Ανοίγει το Wi-Fi της κάμερας…", "Turning the Wi-Fi of the camera on…"));
                if (!device.IsConnected)
                {
                    SetStatus(GoProText.T("Η ζωντανή εικόνα θέλει πρώτα τη σύνδεση Bluetooth της κάμερας.", "The live image first needs the Bluetooth connection of the camera."));
                    return;
                }

                await device.SetWifiAsync(true).ConfigureAwait(false);
                string[] credentials = null;
                for (int i = 0; i < 5 && credentials == null && !token.IsCancellationRequested; i++)
                {
                    credentials = await device.ReadWifiCredentialsAsync().ConfigureAwait(false);
                    if (credentials == null)
                        await Task.Delay(1000, token).ConfigureAwait(false);
                }

                if (credentials == null)
                {
                    SetStatus(GoProText.T("Δεν διαβάστηκε το Wi-Fi της κάμερας από το Bluetooth.", "The Wi-Fi of the camera could not be read by Bluetooth."));
                    return;
                }

                SetStatus(GoProText.T("Ο υπολογιστής μπαίνει στο Wi-Fi της κάμερας…", "The computer joins the Wi-Fi of the camera…"));
                wifi = new GoProWifi();
                string ssid = credentials[0];
                string password = credentials[1];
                string error = await Task.Run(() => wifi.Join(ssid, password, 25000)).ConfigureAwait(false);
                if (error != null)
                {
                    SetStatus(error);
                    return;
                }

                token.ThrowIfCancellationRequested();
                SetStatus(GoProText.T("Η κάμερα ξεκινά την εικόνα…", "The camera starts its image…"));
                StartReceiving();

                using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
                {
                    bool started = false;
                    for (int i = 0; i < 15 && !started && !token.IsCancellationRequested; i++)
                    {
                        started = await GetAsync(http, "/gopro/camera/stream/start").ConfigureAwait(false);
                        if (!started)
                            await Task.Delay(1000, token).ConfigureAwait(false);
                    }

                    if (!started)
                    {
                        SetStatus(GoProText.T("Η κάμερα δεν ξεκίνησε την εικόνα της (Wi-Fi).", "The camera did not start its image (Wi-Fi)."));
                        return;
                    }

                    if (wifi.SharedAdapter)
                        log.Debug("The live image uses the only Wi-Fi of the computer: no internet meanwhile.");

                    // The image comes; the camera is kept awake on its Wi-Fi too.
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(2500, token).ConfigureAwait(false);
                        await GetAsync(http, "/gopro/camera/keep_alive").ConfigureAwait(false);
                        bool fresh;
                        lock (frameLock)
                            fresh = latest != null && (DateTime.UtcNow - latestAt).TotalSeconds < 3;

                        if (!fresh)
                        {
                            // The stream can stop (the camera started recording in a mode without preview): asked again.
                            SetStatus(GoProText.T("Περιμένει την εικόνα της κάμερας…", "Waiting for the image of the camera…"));
                            await GetAsync(http, "/gopro/camera/stream/start").ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                log.Error("Error with the live image of the GoPro.", e);
                SetStatus(e.Message);
            }
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
                    byte[] datagram = udp.Receive(ref from);
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

        /// <summary>Everything back: the stream stopped, the computer on its network again, the Wi-Fi of the camera off.</summary>
        private async Task CleanUpAsync()
        {
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

            device.SetPreviewWanted(false);
            try
            {
                if (device.IsConnected)
                    await device.SetWifiAsync(false).ConfigureAwait(false);
            }
            catch (Exception) { }

            lock (frameLock)
                latest = null;
        }
    }
}
