using System;
using BGAPI2;
using Kinovea.Services;

namespace Kinovea.Camera.Baumer
{
    public static class BaumerHelper
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Get the maximum possible framerate based on image size, exposure and user value.
        /// Note: The resulting value is only correct when using grayscale output.
        /// </summary>
        public static float GetResultingFramerate(Device device)
        {
            if (device == null || !device.IsOpen)
                return 0;
            float resultingFramerate = 0;

            try
            {
                Node nodeReadOut = GetNode(device.RemoteNodeList, "ReadOutTime");
                Node nodeExposure = GetNode(device.RemoteNodeList, "ExposureTime");
                if (nodeReadOut == null || !nodeReadOut.IsAvailable || !nodeReadOut.IsReadable || 
                    nodeExposure == null || !nodeExposure.IsAvailable || !nodeExposure.IsReadable)
                    return resultingFramerate;

                double framerateReadout = 1000000.0 / nodeReadOut.Value;
                double framerateExposure = 1000000.0 / nodeExposure.Value;
                resultingFramerate = (float)Math.Min(framerateReadout, framerateExposure);

                Node nodeFramerate = GetNode(device.RemoteNodeList, "AcquisitionFrameRate");
                Node nodeFramerateEnable = GetNode(device.RemoteNodeList, "AcquisitionFrameRateEnable");
                if (nodeFramerate == null || !nodeFramerate.IsAvailable || !nodeFramerate.IsReadable || 
                    nodeFramerateEnable == null || !nodeFramerateEnable.IsAvailable || !nodeFramerateEnable.IsReadable)
                    return resultingFramerate;

                double framerateSelected = nodeFramerate.Value;
                bool framerateEnabled = nodeFramerateEnable.Value;
                if (!framerateEnabled)
                    return resultingFramerate;

                resultingFramerate = (float)Math.Min(resultingFramerate, framerateSelected);
            }
            catch (Exception e)
            {
                log.ErrorFormat("Error while computing resulting framerate. {0}", e.Message);
            }

            return resultingFramerate;
        }

        /// <summary>
        /// (Swim 2.26.4) The settings of the camera that decide how many frames reach the program, for the log: why a camera gives
        /// fewer frames than asked (link at 100 Mb/s, limit of bandwidth, long exposure, size of the packets). Only the nodes the
        /// camera has.
        /// </summary>
        public static string Describe(Device device)
        {
            return DescribeNodes(device.RemoteNodeList, "PixelFormat", "Width", "Height", "AcquisitionFrameRateEnable", "AcquisitionFrameRate",
                "ExposureAuto", "ExposureTime", "ReadOutTime", "GainAuto", "Gain", "DeviceLinkSpeed", "GevLinkSpeed",
                "DeviceLinkThroughputLimitMode", "DeviceLinkThroughputLimit", "GevSCPSPacketSize", "GevSCPD", "ImageCompressionMode");
        }

        /// <summary>
        /// (Swim 2.26.4) "Name=value" for the readable nodes of a node map among the given names, for the log.
        /// </summary>
        public static string DescribeNodes(NodeMap map, params string[] names)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            foreach (string name in names)
            {
                try
                {
                    Node node = GetNode(map, name);
                    if (node == null || !node.IsReadable)
                        continue;

                    text.AppendFormat("{0}={1} ", name, NodeText(node));
                }
                catch
                {
                }
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// The value of a node as text, whatever its type.
        /// </summary>
        private static string NodeText(Node node)
        {
            try
            {
                string value = node.Value;
                return value;
            }
            catch
            {
            }

            try
            {
                double value = node.Value;
                return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
            }

            try
            {
                bool value = node.Value;
                return value ? "true" : "false";
            }
            catch
            {
            }

            return "?";
        }

        public static Node GetNode(NodeMap map, string name)
        {
            bool present = map.GetNodePresent(name);
            if (!present)
                return null;

            Node node = map[name];
            if (!node.IsImplemented || !node.IsAvailable)
                return null;

            return node;
        }

        public static bool NodeIsReadable(Device device, string name)
        {
            if (device == null)
                throw new InvalidProgramException();

            if (!device.IsOpen)
                return false;

            bool present = device.RemoteNodeList.GetNodePresent(name);
            if (!present)
                return false;

            Node node = device.RemoteNodeList[name];
            if (!node.IsImplemented || !node.IsAvailable)
                return false;

            return node.IsReadable;
        }

        public static int GetInteger(Device device, string name)
        {
            return (int)device.RemoteNodeList[name].Value;
        }

        public static string GetString(Device device, string name)
        {
            return (string)device.RemoteNodeList[name].Value;
        }

        /// <summary>
        /// Takes a Baumer pixel format and determines the output image format.
        /// </summary>
        public static ImageFormat ConvertImageFormat(string pixelFormat, bool compression, bool demosaicing)
        {
            if (compression)
                return ImageFormat.JPEG;

            if (pixelFormat.StartsWith("Bayer"))
            {
                if (!demosaicing)
                    return ImageFormat.Y800;
                else
                    return ImageFormat.RGB24;
            }
            else if (pixelFormat.StartsWith("Mono"))
            {
                return ImageFormat.Y800;
            }
            else
            {
                return ImageFormat.RGB24;
            }
        }

        /// <summary>
        /// Return true if the input buffer format is already grayscale 8-bit per pixel.
        /// This means it can directly be put into the Y800 output frame witohut conversion.
        /// </summary>
        public static bool IsY800(string pixelFormat)
        {
            return pixelFormat == "Mono8" ||
                pixelFormat == "BayerBG8" ||
                pixelFormat == "BayerGB8" ||
                pixelFormat == "BayerGR8" ||
                pixelFormat == "BayerRG8";
        }

        public static bool IsBayer(string pixelFormat)
        {
            return pixelFormat.StartsWith("Bayer");
        }

        /// <summary>
        /// Whether the device supports hardware JPEG compression.
        /// </summary>
        public static bool SupportsJPEG(Device device)
        {
            if (device == null)
                return false;

            bool isReadable = NodeIsReadable(device, "ImageCompressionMode");
            if (!isReadable)
                return false;
            
            BGAPI2.NodeMap enumCompression = device.RemoteNodeList["ImageCompressionMode"].EnumNodeList;
            return enumCompression.GetNodePresent("JPEG");
        }

        /// <summary>
        /// Whether the pixel format is compatible with hardware compression.
        /// </summary>
        public static bool FormatCanCompress(Device device, string pixelFormat)
        {
            return pixelFormat == "Mono8" || pixelFormat == "YCbCr422_8";
        }

        /// <summary>
        /// Enable or disable JPEG compression.
        /// </summary>
        public static void SetJPEG(Device device, bool enable)
        {
            if (enable)
                device.RemoteNodeList["ImageCompressionMode"].Value = "JPEG";
            else 
                device.RemoteNodeList["ImageCompressionMode"].Value = "Off";
        }

        /// <summary>
        /// Reads the current value of JPEG compression.
        /// </summary>
        public static bool GetJPEG(Device device)
        {
            if (!SupportsJPEG(device))
                return false;

            return device.RemoteNodeList["ImageCompressionMode"].Value == "JPEG";
        }

        public static void WriteEnum(Device device, string enumName, string enumValue)
        {
            if (device == null || !device.IsOpen)
                throw new InvalidProgramException();

            bool present = device.RemoteNodeList.GetNodePresent(enumName);
            if (!present)
                return;

            Node node = device.RemoteNodeList[enumName];
            if (!node.IsImplemented || !node.IsAvailable)
                return;

            node.Value = enumValue;
        }
    }
}
