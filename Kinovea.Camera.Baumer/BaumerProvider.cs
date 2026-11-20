using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using BGAPI2;
using System.Drawing.Imaging;
using System.Threading;

namespace Kinovea.Camera.Baumer
{
    /// <summary>
    /// Helper class to wrap Baumer camera.
    /// Provide open/close and acquisition once / acquisition continuous in a separate thread.
    /// </summary>
    public class BaumerProvider
    {
        public event EventHandler<BufferEventArgs> BufferProduced;

        public bool IsOpen 
        {
            get { return opened; }
        }

        public Device Device
        {
            get { return device; }
        }

        /// <summary>
        /// (Swim 2.26.4) The camera is used for a moment only (a picture of the camera list): a capture screen that wants it waits for
        /// it. Behind a capture screen there is nothing to wait for.
        /// </summary>
        public bool BriefUse { get; set; }

        /// <summary>
        /// Frames of the camera that never reached the program since the start of the streaming: frames that arrived
        /// incomplete (packets lost on the network) and frames missing between two that arrived (frame numbers skipped).
        /// </summary>
        public long LostFrames
        {
            get { return Interlocked.Read(ref lostFrames); }
        }

        private ulong bufferFilledTimeoutMS = 1000;
        private BGAPI2.System system;
        private BGAPI2.Interface interf;
        private BGAPI2.Device device;
        private BGAPI2.DataStream dataStream;
        private BGAPI2.BufferList bufferList;
        private bool opened;
        private bool started;
        private bool grabThreadRun = true;
        private Thread grabThread;

        // (Swim 2.26.4) Buffers for the frames on their way: 4 were too few for several cameras at 50 fps (80 ms).
        private const int countBuffers = 16;
        private long lostFrames;
        private long incompleteFrames;
        private ulong lastFrameId;
        private bool haveFrameId;
        private bool frameIdUnavailable;

        // (Swim 2.26.4) One user per camera at a time in the program (see Open). SdkLock: the lists of the Baumer API are refreshed,
        // and the cameras opened and closed, by one thread at a time.
        internal static readonly object SdkLock = new object();
        private static readonly object usersLock = new object();
        private static readonly Dictionary<string, BaumerProvider> users = new Dictionary<string, BaumerProvider>();
        private string userKey;

        // (Swim 2.26.4) GigE systems of the API already set for several cameras (see ConfigureSystem), and the socket buffer asked for.
        private static readonly HashSet<string> configuredSystems = new HashSet<string>();
        private const long streamSocketSize = 32L * 1024 * 1024;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Open a specific device and allocate buffers. Gives up at once if the camera is used elsewhere in the program.
        /// </summary>
        public bool Open(string systemKey, string interfaceKey, string deviceKey)
        {
            return Open(systemKey, interfaceKey, deviceKey, 0);
        }

        /// <summary>
        /// Open a specific device and allocate buffers.
        /// (Swim 2.26.4) A camera has one user at a time in the program. The objects of the Baumer API are shared by the whole
        /// program: the picture of the camera list and a capture screen used to open the same camera together, and the one that
        /// failed closed the camera under the other (the program closed a few seconds after the start, with the screens of five
        /// cameras to reconnect). waitMS: how long to wait for the other user to let the camera go (a picture for the camera list
        /// takes about a second); 0 gives up at once.
        /// </summary>
        public bool Open(string systemKey, string interfaceKey, string deviceKey, int waitMS)
        {
            Close();

            string key = string.Format("{0}|{1}|{2}", systemKey, interfaceKey, deviceKey);
            if (!Claim(key, waitMS))
            {
                log.DebugFormat("Baumer: camera {0} is used elsewhere in the program, not opened a second time.", deviceKey);
                return false;
            }

            try
            {
                lock (SdkLock)
                    OpenDevice(systemKey, interfaceKey, deviceKey);
            }
            finally
            {
                if (!opened)
                    Release();
            }

            return opened;
        }

        /// <summary>
        /// Look for the device, open it and allocate buffers.
        /// </summary>
        private bool OpenDevice(string systemKey, string interfaceKey, string deviceKey)
        {
            try
            {
                // Look for the device.
                SystemList systemList = SystemList.Instance;
                systemList.Refresh();
                foreach (KeyValuePair<string, BGAPI2.System> systemPair in systemList)
                {
                    if (systemPair.Key != systemKey)
                        continue;

                    system = systemPair.Value;
                    if (!system.IsOpen)
                        system.Open();

                    break;
                }

                if (system == null || !system.IsOpen)
                    return false;

                ConfigureSystem(systemKey);

                system.Interfaces.Refresh(100);
                foreach (KeyValuePair<string, BGAPI2.Interface> interfacePair in system.Interfaces)
                {
                    if (interfacePair.Key != interfaceKey)
                        continue;

                    interf = interfacePair.Value;
                    if (!interf.IsOpen)
                        interf.Open();
                    break;
                }

                if (interf == null || !interf.IsOpen)
                    return false;
                

                interf.Devices.Refresh(100);
                foreach (KeyValuePair<string, BGAPI2.Device> devicePair in interf.Devices)
                {
                    if (devicePair.Key != deviceKey)
                        continue;

                    device = devicePair.Value;
                    if (!device.IsOpen)
                        device.Open();
                    break;
                }

                if (device == null || !device.IsOpen)
                    return false;

                DataStreamList dataStreamList = device.DataStreams;
                dataStreamList.Refresh();
                foreach (KeyValuePair<string, BGAPI2.DataStream> dataStreamPair in dataStreamList)
                {
                    if (string.IsNullOrEmpty(dataStreamPair.Key))
                        continue;

                    dataStream = dataStreamPair.Value;
                    dataStream.Open();
                    break;
                }

                if (dataStream == null)
                {
                    CloseDevice();
                    return false;
                }

                // Use buffers internal to the API.
                bufferList = dataStream.BufferList;
                for (int i = 0; i < countBuffers; i++)
                {
                    BGAPI2.Buffer buffer = new BGAPI2.Buffer();
                    bufferList.Add(buffer);
                    //ulong memSize = buffer.MemSize;
                    //log.DebugFormat("Buffer mem size: {0}", memSize);
                }

                // Make buffers available to the Baumer producer.
                if (bufferList != null && bufferList.Count == countBuffers)
                {
                    foreach (KeyValuePair<string, BGAPI2.Buffer> bufferPair in bufferList)
                        bufferPair.Value.QueueBuffer();
                }

                opened = true;

                // (Swim 2.26.4) How the frames travel, in the log at each connection of a capture screen.
                if (!BriefUse)
                {
                    log.InfoFormat("Baumer {0}: stream: {1}; packets: {2}.", DeviceName(),
                        BaumerHelper.DescribeNodes(dataStream.NodeList, "StreamDriverModel", "FilterDriverBufferCount"),
                        BaumerHelper.DescribeNodes(device.NodeList, "DeviceStreamChannelPacketSize"));
                }
            }
            catch (Exception e)
            {
                log.ErrorFormat("Failed to open device. {0}", e);
                DiscardBuffers();
                CloseDataStream();
                CloseDevice();
            }

            return opened;
        }

        /// <summary>
        /// Close the currently opened device and deallocate buffers.
        /// </summary>
        public void Close()
        {
            if (!opened)
                return;

            if (started)
                Stop();

            opened = false;

            lock (SdkLock)
            {
                DiscardBuffers();
                CloseDataStream();
                CloseDevice();
                //CloseInterface();
                //CloseSystem();
            }

            Release();
        }

        /// <summary>
        /// Start acquisition, get one frame synchronously, convert to RGB24 Bitmap, stop acquisition.
        /// </summary>
        public void AcquireOne()
        {
            if (!opened)
                return;

            if (started)
                Stop();

            dataStream.StartAcquisition();
            started = true;
            //device.RemoteNodeList["AcquisitionMode"].Value = "Continuous";
            device.RemoteNodeList["AcquisitionStart"].Execute();

            // Wait for one frame.
            BGAPI2.Buffer bufferFilled = dataStream.GetFilledBuffer(bufferFilledTimeoutMS);
            if (bufferFilled == null || bufferFilled.IsIncomplete || bufferFilled.MemPtr == IntPtr.Zero)
            {
                // Timeout or error while waiting for the frame.
                Stop();
                return;
            }

            // At this point we have an image buffer available.
            // .MemPtr contains native memory of the raw frame.
            if (BufferProduced != null)
                BufferProduced(this, new BufferEventArgs(bufferFilled));

            // Make the buffer available again.
            bufferFilled.QueueBuffer();
            Stop();
        }

        public void AcquireContinuous()
        {
            // Start a background thread and post new buffers.
            if (!opened)
                return;

            if (!dataStream.IsOpen)
                return;

            // setup
            dataStream.StartAcquisition();
            started = true;
            device.RemoteNodeList["AcquisitionMode"].Value = "Continuous";
            device.RemoteNodeList["AcquisitionStart"].Execute();

            // TODO: use ThreadPool instead ?
            Interlocked.Exchange(ref lostFrames, 0);
            Interlocked.Exchange(ref incompleteFrames, 0);
            haveFrameId = false;
            grabThreadRun = true;
            grabThread = new Thread(Grab);
            grabThread.IsBackground = true;
            grabThread.Start();
        }

        public void Stop()
        {
            if (!opened || !started)
                return;

            // (Swim 2.26.4) A camera is stopped once (Close and the picture of the camera list used to stop it again), and an error
            // of the camera here does not get out: on the thread of the picture it closed the program.
            started = false;
            grabThreadRun = false;
            bool continuous = grabThread != null;
            if (grabThread != null && grabThread.IsAlive && grabThread != Thread.CurrentThread)
            {
                if (!grabThread.Join(5000))
                    log.ErrorFormat("Baumer {0}: the reading of the frames did not stop in time.", DeviceName());
            }

            grabThread = null;
            if (continuous)
                LogStreamCounts();

            try
            {
                if (device.RemoteNodeList.GetNodePresent("AcquisitionAbort"))
                    device.RemoteNodeList["AcquisitionAbort"].Execute();

                device.RemoteNodeList["AcquisitionStop"].Execute();
            }
            catch (Exception e)
            {
                log.WarnFormat("Baumer {0}: the camera did not take the order to stop. {1}", DeviceName(), e.Message);
            }

            try
            {
                dataStream.StopAcquisition();
            }
            catch (Exception e)
            {
                log.WarnFormat("Baumer {0}: the stream did not stop. {1}", DeviceName(), e.Message);
            }
        }
        
        /// <summary>
        /// Thread method.
        /// </summary>
        private void Grab()
        {
            // (Swim 2.26.4) The thread used to end for good at the first frame that arrived incomplete (one packet lost on the
            // network) or at the first second without a frame: the image of the camera froze without a word, as soon as
            // several cameras streamed together. Now a lost frame is counted and the stream goes on; a pause is waited out.
            string name = DeviceName();
            int secondsWithoutFrame = 0;
            long reported = 0;

            while (grabThreadRun)
            {
                BGAPI2.Buffer bufferFilled = null;
                try
                {
                    bufferFilled = dataStream.GetFilledBuffer(bufferFilledTimeoutMS);
                }
                catch (Exception e)
                {
                    // The stream itself refused (camera unplugged, stream closed): the reading ends here, as it always did. Asking a
                    // stream that is gone again and again could take the program down.
                    log.ErrorFormat("Baumer {0}: the camera stopped sending frames. {1}", name, e.Message);
                    grabThreadRun = false;
                    break;
                }

                if (bufferFilled == null)
                {
                    // No frame for a second (no light on the trigger, camera busy): keep waiting.
                    secondsWithoutFrame++;
                    if (secondsWithoutFrame == 1 || secondsWithoutFrame % 30 == 0)
                        log.WarnFormat("Baumer {0}: no frame for {1} s.", name, secondsWithoutFrame);
                    continue;
                }

                if (secondsWithoutFrame > 0)
                {
                    log.WarnFormat("Baumer {0}: frames again after {1} s without.", name, secondsWithoutFrame);
                    secondsWithoutFrame = 0;
                }

                try
                {
                    CountSkippedFrames(bufferFilled);

                    if (bufferFilled.IsIncomplete || bufferFilled.MemPtr == IntPtr.Zero)
                    {
                        // Packets lost on the network: the frame cannot be used.
                        Interlocked.Increment(ref lostFrames);
                        Interlocked.Increment(ref incompleteFrames);
                    }
                    else if (BufferProduced != null)
                    {
                        BufferProduced(this, new BufferEventArgs(bufferFilled));
                    }
                }
                catch (Exception e)
                {
                    log.ErrorFormat("Baumer {0}: error while handling a frame. {1}", name, e.Message);
                }
                finally
                {
                    // Make the buffer available to Baumer internals again, whatever happened to the frame.
                    try
                    {
                        bufferFilled.QueueBuffer();
                    }
                    catch (Exception e)
                    {
                        log.ErrorFormat("Baumer {0}: could not give the buffer back. {1}", name, e.Message);
                    }
                }

                long lost = Interlocked.Read(ref lostFrames);
                if (lost > reported && (reported == 0 || lost - reported >= 100))
                {
                    log.WarnFormat("Baumer {0}: {1} frames lost since the start ({2} arrived incomplete).",
                        name, lost, Interlocked.Read(ref incompleteFrames));
                    reported = lost;
                }
            }

            long total = Interlocked.Read(ref lostFrames);
            if (total > 0)
                log.WarnFormat("Baumer {0}: stopped, {1} frames lost in all ({2} arrived incomplete).",
                    name, total, Interlocked.Read(ref incompleteFrames));
        }

        /// <summary>
        /// Frames that never arrived between the previous one and this one, from the frame numbers of the camera.
        /// The number goes back to the start when it overflows (16 bits on GigE) or when the camera restarts: no count then.
        /// </summary>
        private void CountSkippedFrames(BGAPI2.Buffer buffer)
        {
            if (frameIdUnavailable)
                return;

            ulong id;
            try
            {
                id = buffer.FrameID;
            }
            catch
            {
                frameIdUnavailable = true;
                return;
            }

            if (haveFrameId && id > lastFrameId + 1 && id - lastFrameId < 10000)
                Interlocked.Add(ref lostFrames, (long)(id - lastFrameId - 1));

            lastFrameId = id;
            haveFrameId = true;
        }

        /// <summary>
        /// (Swim 2.26.4) The counts of the stream of the Baumer API at the stop, for the log: frames started and delivered, and frames
        /// lost because no buffer was free (underrun). Read by name: not every version of the API has them.
        /// </summary>
        private void LogStreamCounts()
        {
            try
            {
                StringBuilder counts = new StringBuilder();
                foreach (string name in new string[] { "StartedCount", "DeliveredCount", "UnderrunCount" })
                {
                    System.Reflection.PropertyInfo property = dataStream.GetType().GetProperty(name);
                    if (property != null)
                        counts.AppendFormat(" {0}={1}", name, property.GetValue(dataStream, null));
                }

                if (counts.Length > 0)
                    log.InfoFormat("Baumer {0}: counts of the stream at the stop:{1}.", DeviceName(), counts);

                log.InfoFormat("Baumer {0}: statistics of the stream at the stop: {1}.", DeviceName(),
                    BaumerHelper.DescribeNodes(dataStream.NodeList, "DataBlockComplete", "DataBlockInComplete", "DataBlockMissing",
                    "DataBlockDroppedBufferUnderrun", "DataBlockDroppedResendUnderrun", "PacketReceiveIncomplete", "PacketResendRequestSingle",
                    "PacketResendRequestRange", "PacketResendReceive", "StreamDriverModel"));
            }
            catch (Exception e)
            {
                log.DebugFormat("Baumer {0}: no counts of the stream. {1}", DeviceName(), e.Message);
            }
        }

        /// <summary>
        /// (Swim 2.26.4) The GigE system of the API, once per program, before its first camera is opened: a socket buffer of 32 MB per
        /// stream (a third of a second of one camera at 50 fps) so that a busy moment of the computer does not lose packets (with five
        /// cameras, frames arrived incomplete while recording), and the automatic size of the packets (larger packets when the network
        /// card allows them). The values go to the log.
        /// </summary>
        private void ConfigureSystem(string systemKey)
        {
            lock (configuredSystems)
            {
                if (configuredSystems.Contains(systemKey))
                    return;

                configuredSystems.Add(systemKey);
            }

            try
            {
                NodeMap nodes = system.NodeList;
                Node socketSize = BaumerHelper.GetNode(nodes, "StreamSocketSize");
                if (socketSize != null && socketSize.IsReadable && socketSize.IsWriteable)
                {
                    long current = socketSize.Value;
                    long max = socketSize.Max;
                    long wanted = Math.Min(streamSocketSize, max);
                    if (wanted > current)
                        socketSize.Value = wanted;
                }

                Node mtuDetection = BaumerHelper.GetNode(nodes, "UseMTUDetection");
                if (mtuDetection != null && mtuDetection.IsReadable && mtuDetection.IsWriteable)
                {
                    bool detection = mtuDetection.Value;
                    if (!detection)
                        mtuDetection.Value = true;
                }

                string described = BaumerHelper.DescribeNodes(nodes, "StreamSocketSize", "UseMTUDetection", "DeviceStreamChannelPacketSizeMax",
                    "DeviceStreamChannelPacketSizeFix");
                if (!string.IsNullOrEmpty(described))
                    log.InfoFormat("Baumer system {0}: {1}.", system.DisplayName, described);
            }
            catch (Exception e)
            {
                log.WarnFormat("Baumer system {0}: could not be set for several cameras. {1}", systemKey, e.Message);
            }
        }

        /// <summary>
        /// Take the camera for this provider, waiting up to waitMS for another user of the program to let it go.
        /// </summary>
        private bool Claim(string key, int waitMS)
        {
            lock (usersLock)
            {
                System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                BaumerProvider user;
                while (users.TryGetValue(key, out user) && user != this)
                {
                    int left = waitMS - (int)stopwatch.ElapsedMilliseconds;
                    if (left <= 0 || !user.BriefUse)
                        return false;

                    System.Threading.Monitor.Wait(usersLock, left);
                }

                users[key] = this;
                userKey = key;
                return true;
            }
        }

        /// <summary>
        /// Let the camera go, for another user that waits for it.
        /// </summary>
        private void Release()
        {
            lock (usersLock)
            {
                BaumerProvider user;
                if (userKey != null && users.TryGetValue(userKey, out user) && user == this)
                {
                    users.Remove(userKey);
                    System.Threading.Monitor.PulseAll(usersLock);
                }

                userKey = null;
            }
        }

        private string DeviceName()
        {
            try
            {
                return string.Format("{0} {1}", device.Model, device.SerialNumber);
            }
            catch
            {
                return "camera";
            }
        }

        private void DiscardBuffers()
        {
            try
            {
                if (bufferList != null)
                {
                    bufferList.DiscardAllBuffers();
                    while (bufferList.Count > 0)
                    {
                        BGAPI2.Buffer buffer = (BGAPI2.Buffer)bufferList.Values.First();
                        bufferList.RevokeBuffer(buffer);
                    }

                    bufferList = null;
                }
            }
            catch (Exception e)
            {
                log.Error(e.Message);
            }
        }

        private void CloseDataStream()
        {
            try
            {
                if (dataStream != null)
                {
                    if (dataStream.IsOpen)
                        dataStream.Close();

                    dataStream = null;
                }
            }
            catch (Exception e)
            {
                log.Error(e.Message);
            }
        }

        private void CloseDevice()
        {
            try
            {
                if (device != null)
                {
                    if (device.IsOpen)
                        device.Close();

                    device = null;
                }
            }
            catch (Exception e)
            {
                log.Error(e.Message);
            }
        }

        private void CloseInterface()
        {
            try
            {
                if (interf != null)
                {
                    if (interf.IsOpen)
                        interf.Close();

                    interf = null;
                }
            }
            catch (Exception e)
            {
                log.Error(e.Message);
            }
        }

        private void CloseSystem()
        {
            try
            {
                if (system != null)
                {
                    if (system.IsOpen)
                        system.Close();

                    system = null;
                }
            }
            catch (Exception e)
            {
                log.Error(e.Message);
            }
        }
    }
}
