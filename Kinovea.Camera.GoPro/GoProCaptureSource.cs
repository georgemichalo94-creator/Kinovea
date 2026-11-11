#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using Kinovea.Pipeline;
using Kinovea.Services;

namespace Kinovea.Camera.GoPro
{
    /// <summary>
    /// (Swim 2.20.0) The source of a capture screen with a GoPro: its images show the state of the camera (4 per second), its record
    /// button starts and stops the camera itself (the video is written on the card of the camera, not on the computer).
    /// </summary>
    public class GoProCaptureSource : ICaptureSource, IRemoteRecordingSource, ILivePreviewSource, ICameraPresetSource, IRemoteMediaSource
    {
        public event EventHandler<FrameProducedEventArgs> FrameProduced;
        public event EventHandler GrabbingStatusChanged;
        public event EventHandler RemoteRecordingChanged;

        #region Properties
        public bool Grabbing
        {
            get { return grabbing; }
        }

        /// <summary>The nominal rate of the images (the state is drawn 4 times per second; the live image comes faster).</summary>
        public float Framerate
        {
            get { return 30; }
        }

        public double LiveDataRate
        {
            get { return dataRateAverager.Average; }
        }

        public bool RemoteRecording
        {
            get
            {
                GoProDevice d = device;
                return d != null && d.EffectiveRecording;
            }
        }

        /// <summary>The live image of this camera is on (or starting).</summary>
        public bool LivePreview
        {
            get { return GoProPreview.CurrentAddress == address; }
        }
        #endregion

        #region Members
        private readonly CameraSummary summary;
        private readonly ulong address;
        private GoProDevice device;
        private DateTime failedStart = DateTime.MinValue;
        private System.Threading.Timer timer;
        private bool grabbing;
        private int ticking;
        private byte[] frameBuffer;
        private Bitmap canvas;
        private GoProStatusPainter painter;
        private readonly Stopwatch swDataRate = new Stopwatch();
        private readonly Averager dataRateAverager = new Averager(0.02);
        private byte[] liveBuffer;
        private int period = 250;
        private static bool networkLossAccepted;
        private const int width = GoProStatusPainter.Width;
        private const int height = GoProStatusPainter.Height;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        #endregion

        public GoProCaptureSource(CameraSummary summary, ulong address)
        {
            this.summary = summary;
            this.address = address;
        }

        #region ICaptureSource
        public ImageDescriptor Prepare()
        {
            int bufferSize = ImageFormatHelper.ComputeBufferSize(width, height, Kinovea.Services.ImageFormat.RGB24);
            return new ImageDescriptor(Kinovea.Services.ImageFormat.RGB24, width, height, true, bufferSize);
        }

        public ImageDescriptor GetPrepareFailedImageDescriptor(ImageDescriptor input)
        {
            return input;
        }

        public void Start()
        {
            if (grabbing)
                return;

            log.DebugFormat("Starting the GoPro {0} ({1:X12}).", summary.Alias, address);
            if (frameBuffer == null)
                frameBuffer = new byte[width * height * 3];

            if (canvas == null)
                canvas = new Bitmap(width, height, PixelFormat.Format24bppRgb);

            if (painter == null)
                painter = new GoProStatusPainter();

            try
            {
                device = GoProDevice.Acquire(address);
                device.RecordingChanged += Device_RecordingChanged;
            }
            catch (Exception e)
            {
                // Without the Bluetooth of Windows 10 (WinRT) the screen still shows why.
                log.Error("Could not start the Bluetooth connection with the GoPro.", e);
                device = null;
            }

            grabbing = true;
            swDataRate.Restart();
            GrabbingStatusChanged?.Invoke(this, EventArgs.Empty);
            period = 250;
            timer = new System.Threading.Timer(Tick, null, 0, period);
        }

        public void Stop()
        {
            if (!grabbing)
                return;

            log.DebugFormat("Stopping the GoPro {0}.", summary.Alias);
            System.Threading.Timer t = timer;
            timer = null;
            if (t != null)
                t.Dispose();

            // No new image; one being drawn finishes first. The live image of this camera ends with its screen.
            grabbing = false;
            SpinWait.SpinUntil(() => Volatile.Read(ref ticking) == 0, 1000);
            GoProPreview.Stop(address);

            GoProDevice d = device;
            device = null;
            if (d != null)
            {
                d.RecordingChanged -= Device_RecordingChanged;
                d.Release();
            }

            GrabbingStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Close()
        {
            Stop();
            if (painter != null)
            {
                painter.Dispose();
                painter = null;
            }

            if (canvas != null)
            {
                canvas.Dispose();
                canvas = null;
            }
        }
        #endregion

        #region IRemoteRecordingSource
        public void SetRemoteRecording(bool record, DateTime commandTime)
        {
            GoProDevice d = device;
            if (d == null)
            {
                if (record)
                    failedStart = commandTime;

                RemoteRecordingChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            d.SetRecording(record, commandTime);
        }

        private void Device_RecordingChanged(object sender, EventArgs e)
        {
            RemoteRecordingChanged?.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region ILivePreviewSource
        /// <summary>
        /// The live image of this camera on or off (the one of another camera stops first). The first time, when the computer has only
        /// one Wi-Fi and is on it, the user is told that the computer is off its network while the live image is on. UI thread.
        /// </summary>
        public void SetLivePreview(bool on)
        {
            GoProDevice d = device;
            if (!on || d == null)
            {
                GoProPreview.Stop(address);
                return;
            }

            if (!networkLossAccepted && WouldTakeTheNetwork())
            {
                string text = GoProText.T(
                    "Για τη ζωντανή εικόνα ο υπολογιστής μπαίνει στο Wi-Fi της κάμερας.\n\n" +
                    "Ο υπολογιστής έχει ένα Wi-Fi: όσο είναι ανοιχτή η εικόνα (έως 3 λεπτά) δεν θα έχει ίντερνετ " +
                    "(Google Sheet, απομακρυσμένη σύνδεση). Μετά ξαναγυρίζει μόνος του στο δίκτυό του.\n" +
                    "Με καλώδιο δικτύου ή ένα δεύτερο Wi-Fi (USB) κρατά το ίντερνετ.\n\n" +
                    "Την πρώτη φορά τα Windows μπορεί να ρωτήσουν για το τείχος προστασίας: πάτα «Να επιτρέπεται η πρόσβαση».\n\n" +
                    "Να ανοίξει η ζωντανή εικόνα;",
                    "For the live image the computer joins the Wi-Fi of the camera.\n\n" +
                    "The computer has one Wi-Fi: while the image is on (up to 3 minutes) it has no internet " +
                    "(Google Sheet, remote connection). Then it goes back to its network by itself.\n" +
                    "With a network cable or a second Wi-Fi (USB) it keeps the internet.\n\n" +
                    "The first time Windows may ask about the firewall: press \"Allow access\".\n\n" +
                    "Open the live image?");

                System.Windows.Forms.DialogResult answer = System.Windows.Forms.MessageBox.Show(text, GoProText.T("Ζωντανή εικόνα GoPro", "GoPro live image"),
                    System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Information);

                if (answer != System.Windows.Forms.DialogResult.Yes)
                    return;

                networkLossAccepted = true;
            }

            GoProPreview.Start(d);
        }

        private static bool WouldTakeTheNetwork()
        {
            try
            {
                return GoProWifi.WouldTakeTheNetwork();
            }
            catch (Exception)
            {
                return false;
            }
        }
        #endregion

        #region IRemoteMediaSource
        /// <summary>(2.21.0) The serial number of the camera, read at its connection ("" before).</summary>
        public string CameraSerial
        {
            get
            {
                GoProDevice d = device;
                return d != null ? d.SerialNumber : "";
            }
        }

        public string CameraModel
        {
            get
            {
                GoProDevice d = device;
                return d != null ? d.ModelName : "";
            }
        }

        /// <summary>(2.21.0) Seconds from a start command to the camera recording (NaN: not measured for it).</summary>
        public double StartOffset(DateTime commandTime)
        {
            GoProDevice d = device;
            return d != null ? d.StartOffsetOf(commandTime) : double.NaN;
        }

        /// <summary>(2.21.0) The last start command this camera did not do (its recording has no file of it).</summary>
        public DateTime FailedStart
        {
            get
            {
                GoProDevice d = device;
                DateTime fromDevice = d != null ? d.FailedStart : DateTime.MinValue;
                return fromDevice > failedStart ? fromDevice : failedStart;
            }
        }

        /// <summary>(2.21.0) Videos of the card of this camera by its Wi-Fi (the live image of any camera stops first).</summary>
        public System.Threading.Tasks.Task<string> DownloadAsync(Func<IList<RemoteMediaFile>, IList<RemoteMediaDownload>> choose,
            IProgress<RemoteMediaProgress> progress, CancellationToken token)
        {
            return GoProMedia.DownloadAsync(device, summary.Alias, choose, progress, token);
        }
        #endregion

        #region ICameraPresetSource
        /// <summary>The sets of settings of the GoPro cameras (race, technique analysis), in the language of the interface.</summary>
        public IList<CameraPreset> CameraPresets
        {
            get { return GoProPresets.All.Select(p => new CameraPreset(p.Id, p.Name, p.Description, p.Button)).ToList(); }
        }

        /// <summary>The GoPro cameras of the screens that are connected now.</summary>
        public int ConnectedCameras
        {
            get { return GoProDevice.Active().Count(d => d.IsConnected); }
        }

        /// <summary>The set in this camera or in all the GoPro cameras of the screens (each one at its own pace). Returns at once.</summary>
        public void ApplyCameraPreset(string id, bool allCameras)
        {
            GoProPreset preset = GoProPresets.Find(id);
            if (preset == null)
                return;

            List<GoProDevice> devices = new List<GoProDevice>();
            if (allCameras)
            {
                devices.AddRange(GoProDevice.Active());
            }
            else
            {
                GoProDevice d = device;
                if (d != null)
                    devices.Add(d);
            }

            foreach (GoProDevice d in devices)
            {
                GoProDevice target = d;
                System.Threading.Tasks.Task.Run(() => target.ApplyPresetAsync(preset));
            }
        }
        #endregion

        #region Images
        private void Tick(object state)
        {
            if (Interlocked.Exchange(ref ticking, 1) == 1)
                return;

            try
            {
                if (!grabbing || canvas == null || painter == null)
                    return;

                GoProDevice d = device;
                GoProState s;
                if (d != null)
                {
                    d.CheckRecordingChange();
                    s = d.Snapshot(summary.Alias);
                }
                else
                {
                    s = new GoProState { Alias = summary.Alias, Link = GoProLink.NoBluetooth, Problem = GoProText.T("Η σύνδεση Bluetooth δεν ξεκίνησε: χρειάζονται Windows 10 ή νεότερα.", "The Bluetooth connection did not start: it needs Windows 10 or later.") };
                }

                // The live image when it comes (with the state on a band over it), else the state of the camera.
                GoProPreview preview = GoProPreview.For(address);
                bool live = false;
                if (preview != null)
                {
                    if (liveBuffer == null)
                        liveBuffer = new byte[width * height * 3];

                    live = preview.CopyLatest(liveBuffer);
                    s.PreviewSecondsLeft = preview.SecondsLeft;
                    string previewStatus = preview.Status;
                    if (!live && previewStatus != null)
                        s.Problem = GoProText.T("Ζωντανή εικόνα: ", "Live image: ") + previewStatus;
                }
                else if (string.IsNullOrEmpty(s.Problem))
                {
                    // How the last live image ended by itself (an error, the time limit), for a minute.
                    string outcome = GoProPreview.LastOutcome(address);
                    if (outcome != null)
                        s.Problem = GoProText.T("Ζωντανή εικόνα: ", "Live image: ") + outcome;
                }

                SetPeriod(preview != null ? 40 : 250);
                if (live)
                {
                    FillCanvas(liveBuffer);
                    using (Graphics g = Graphics.FromImage(canvas))
                        painter.PaintOverlay(g, s);
                }
                else
                {
                    using (Graphics g = Graphics.FromImage(canvas))
                        painter.Paint(g, s);
                }

                CopyCanvas();
                ComputeDataRate(frameBuffer.Length);
                FrameProduced?.Invoke(this, new FrameProducedEventArgs(frameBuffer, frameBuffer.Length));
            }
            catch (Exception e)
            {
                log.Error("Error while drawing the image of the GoPro.", e);
            }
            finally
            {
                Volatile.Write(ref ticking, 0);
            }
        }

        /// <summary>The image into the buffer of the frames (RGB24 in the byte order of Windows bitmaps, top-down).</summary>
        private void CopyCanvas()
        {
            Rectangle rect = new Rectangle(0, 0, width, height);
            BitmapData data = canvas.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int row = width * 3;
                for (int y = 0; y < height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), frameBuffer, y * row, row);
            }
            finally
            {
                canvas.UnlockBits(data);
            }
        }

        /// <summary>The live image into the canvas (BGR, top-down, the size of the screen).</summary>
        private void FillCanvas(byte[] bgr)
        {
            Rectangle rect = new Rectangle(0, 0, width, height);
            BitmapData data = canvas.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                int row = width * 3;
                for (int y = 0; y < height; y++)
                    Marshal.Copy(bgr, y * row, IntPtr.Add(data.Scan0, y * data.Stride), row);
            }
            finally
            {
                canvas.UnlockBits(data);
            }
        }

        /// <summary>Faster images while the live image is on (25 per second), else 4 per second.</summary>
        private void SetPeriod(int milliseconds)
        {
            if (milliseconds == period)
                return;

            period = milliseconds;
            System.Threading.Timer t = timer;
            if (t != null)
                t.Change(milliseconds, milliseconds);
        }

        private void ComputeDataRate(int bytes)
        {
            double seconds = swDataRate.Elapsed.TotalSeconds;
            if (seconds > 0)
                dataRateAverager.Post(((double)bytes / (1024 * 1024)) / seconds);

            swDataRate.Restart();
        }
        #endregion

        /// <summary>The image of a camera for the list of cameras (its state as last heard).</summary>
        internal static Bitmap Thumbnail(GoProState state)
        {
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (GoProStatusPainter p = new GoProStatusPainter())
            using (Graphics g = Graphics.FromImage(bmp))
                p.Paint(g, state);

            return bmp;
        }
    }
}
