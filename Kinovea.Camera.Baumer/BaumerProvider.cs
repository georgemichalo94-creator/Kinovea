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
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Open a specific device and allocate buffers.
        /// </summary>
        public bool Open(string systemKey, string interfaceKey, string deviceKey)
        {
            Close();

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

            DiscardBuffers();
            CloseDataStream();
            CloseDevice();
            //CloseInterface();
            //CloseSystem();
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
            //device.RemoteNodeList["AcquisitionMode"].Value = "Continuous";
            device.RemoteNodeList["AcquisitionStart"].Execute();
            started = true;

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
            device.RemoteNodeList["AcquisitionMode"].Value = "Continuous";
            device.RemoteNodeList["AcquisitionStart"].Execute();

            started = true;

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

            if (grabThread != null && grabThread.IsAlive)
            {
                grabThreadRun = false;
                grabThread.Join();
            }

            if (device.RemoteNodeList.GetNodePresent("AcquisitionAbort"))
                device.RemoteNodeList["AcquisitionAbort"].Execute();

            device.RemoteNodeList["AcquisitionStop"].Execute();
            dataStream.StopAcquisition();
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
                    // The stream itself refused (camera unplugged, stream stopped): try again in a moment, until stopped.
                    log.WarnFormat("Baumer {0}: error while waiting for a frame. {1}", name, e.Message);
                    Thread.Sleep(200);
                    continue;
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
