#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Kinovea.Camera.GoPro
{
    /// <summary>
    /// (Swim 2.20.0) The identifiers of the Open GoPro interface by Bluetooth Low Energy (HERO9 Black and later): services,
    /// characteristics, commands, queries, statuses and settings, as published by GoPro (gopro.github.io/OpenGoPro).
    /// </summary>
    internal static class GoProIds
    {
        // Services and characteristics.
        public static readonly Guid ControlQueryService = new Guid("0000fea6-0000-1000-8000-00805f9b34fb");
        public static readonly Guid WifiApService = Gp("0001");
        public static readonly Guid WifiApSsid = Gp("0002");
        public static readonly Guid WifiApPassword = Gp("0003");
        public static readonly Guid Command = Gp("0072");
        public static readonly Guid CommandResponse = Gp("0073");
        public static readonly Guid Settings = Gp("0074");
        public static readonly Guid SettingsResponse = Gp("0075");
        public static readonly Guid Query = Gp("0076");
        public static readonly Guid QueryResponse = Gp("0077");
        public static readonly Guid NetworkService = Gp("0090");
        public static readonly Guid NetworkCommand = Gp("0091");
        public static readonly Guid NetworkResponse = Gp("0092");

        /// <summary>The company identifier of GoPro in the manufacturer data of the advertisements.</summary>
        public const ushort CompanyId = 0x02F2;

        // Commands (Command characteristic): [id, (length, value)…] → [id, status, …].
        public const byte CmdSetShutter = 0x01;
        public const byte CmdSetDateTime = 0x0D;
        public const byte CmdSetWifiAp = 0x17;
        public const byte CmdGetHardwareInfo = 0x3C;
        public const byte CmdLoadPresetGroup = 0x3E;
        public const byte CmdSetThirdPartyClient = 0x50;

        /// <summary>The group of the video presets (Load Preset Group): the camera records video, not photos or time lapses.</summary>
        public const int PresetGroupVideo = 1000;

        // Settings (Settings characteristic): [id, length, value] → [id, status].
        public const byte SettingResolution = 2;
        public const byte SettingFps = 3;
        public const byte SettingLed = 91;
        public const byte SettingLens = 121;
        public const byte SettingAntiFlicker = 134;
        public const byte SettingHypersmooth = 135;

        /// <summary>The keep alive is the value 0x42 of the setting 91 (every 3 s after the connection).</summary>
        public const byte KeepAliveValue = 0x42;

        // Queries (Query characteristic): [id, ids…] → [id, status, (id, length, value)…].
        public const byte QueryGetSettings = 0x12;
        public const byte QueryGetStatuses = 0x13;
        public const byte QueryRegisterSettings = 0x52;
        public const byte QueryRegisterStatuses = 0x53;
        public const byte QuerySettingPush = 0x92;
        public const byte QueryStatusPush = 0x93;

        // Statuses.
        public const byte StatusBatteryBars = 2;
        public const byte StatusHot = 6;
        public const byte StatusBusy = 8;
        public const byte StatusEncoding = 10;
        public const byte StatusEncodingSeconds = 13;
        public const byte StatusConnectedDevices = 31;
        public const byte StatusPrimaryStorage = 33;
        public const byte StatusRemainingVideo = 35;
        public const byte StatusSdRemaining = 54;
        public const byte StatusApMode = 69;
        public const byte StatusBatteryPercent = 70;
        public const byte StatusCold = 85;
        public const byte StatusSdSpeedError = 111;

        // Network management (protobuf, characteristic 0091): the end of the pairing, to clear the pairing screen of the camera.
        public const byte FeatureWirelessManagement = 0x03;
        public const byte ActionSetPairingState = 0x01;

        private static Guid Gp(string id)
        {
            return new Guid("b5f9" + id + "-aa8d-11e3-9046-0002a5d5c51b");
        }

        /// <summary>The name of a camera from the model number of its advertisement (null if unknown).</summary>
        public static string ModelName(int modelId)
        {
            switch (modelId)
            {
                case 55: return "HERO9 Black";
                case 57: return "HERO10 Black";
                case 58: return "HERO11 Black";
                case 60: return "HERO11 Black Mini";
                case 62: return "HERO12 Black";
                case 65: return "HERO13 Black";
                default: return null;
            }
        }

        public static string ResolutionName(int value)
        {
            switch (value)
            {
                case 1: return "4K";
                case 4: return "2.7K";
                case 6: return "2.7K 4:3";
                case 7: return "1440";
                case 9: return "1080";
                case 12: return "720";
                case 18: return "4K 4:3";
                case 21: return "5.6K";
                case 24: return "5K";
                case 25: return "5K 4:3";
                case 26: return "5.3K 8:7";
                case 27: return "5.3K 4:3";
                case 28: return "4K 8:7";
                case 31: return "8K";
                case 35: return "5.3K 21:9";
                case 36: return "4K 21:9";
                case 37: return "4K 1:1";
                case 38: return "900";
                case 100: return "5.3K";
                case 107: return "5.3K 8:7";
                case 108: return "4K 8:7";
                case 109: return "4K 9:16";
                case 110: return "1080 9:16";
                case 111: return "2.7K 4:3";
                case 112: return "4K 4:3";
                case 113: return "5.3K 4:3";
                default: return value < 0 ? null : "#" + value.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static int Fps(int value)
        {
            switch (value)
            {
                case 0: return 240;
                case 1: return 120;
                case 2: return 100;
                case 3: return 90;
                case 5: return 60;
                case 6: return 50;
                case 8: return 30;
                case 9: return 25;
                case 10: return 24;
                case 13: return 200;
                case 15: return 400;
                case 16: return 360;
                case 17: return 300;
                default: return -1;
            }
        }

        public static string LensName(int value)
        {
            switch (value)
            {
                case 0: return "Wide";
                case 2: return "Narrow";
                case 3: return "SuperView";
                case 4: return "Linear";
                case 7: return "Max SuperView";
                case 8: return "Linear + Horizon Leveling";
                case 9: return "HyperView";
                case 10: return "Linear + Horizon Lock";
                case 11: return "Max HyperView";
                case 12: return "Ultra SuperView";
                case 13: return "Ultra Wide";
                case 14: return "Ultra Linear";
                case 104: return "Ultra HyperView";
                default: return value < 0 ? null : "#" + value.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>50 Hz (PAL), 60 Hz (NTSC), or null if unknown.</summary>
        public static string AntiFlickerName(int value)
        {
            switch (value)
            {
                case 0: return "60 Hz";
                case 1: return "50 Hz";
                case 2: return "60 Hz";
                case 3: return "50 Hz";
                default: return null;
            }
        }

        public static bool AntiFlicker50(int value)
        {
            return value == 1 || value == 3;
        }

        public static string HypersmoothName(int value)
        {
            switch (value)
            {
                case 0: return "Off";
                case 1: return "Low";
                case 2: return "High";
                case 3: return "Boost";
                case 4: return "Auto Boost";
                case 100: return "Standard";
                default: return null;
            }
        }
    }

    /// <summary>
    /// The packets of a message: a general header (5-bit length) for a message of up to 19 bytes, else an extended header (13-bit
    /// length) and continuation packets (with a 4-bit counter), each packet at most 20 bytes.
    /// </summary>
    internal static class GoProPackets
    {
        public const int MaxPacket = 20;

        public static List<byte[]> Split(byte[] message)
        {
            List<byte[]> packets = new List<byte[]>();
            if (message.Length <= MaxPacket - 1)
            {
                byte[] single = new byte[message.Length + 1];
                single[0] = (byte)message.Length;
                Array.Copy(message, 0, single, 1, message.Length);
                packets.Add(single);
                return packets;
            }

            int first = Math.Min(message.Length, MaxPacket - 2);
            byte[] head = new byte[first + 2];
            head[0] = (byte)(0x20 | ((message.Length >> 8) & 0x1F));
            head[1] = (byte)(message.Length & 0xFF);
            Array.Copy(message, 0, head, 2, first);
            packets.Add(head);

            int offset = first;
            int counter = 0;
            while (offset < message.Length)
            {
                int count = Math.Min(message.Length - offset, MaxPacket - 1);
                byte[] packet = new byte[count + 1];
                packet[0] = (byte)(0x80 | (counter & 0x0F));
                Array.Copy(message, offset, packet, 1, count);
                packets.Add(packet);
                offset += count;
                counter++;
            }

            return packets;
        }

        /// <summary>The values of a query response or push: (id, length, value) triplets from `start`.</summary>
        public static Dictionary<int, byte[]> ParseTlv(byte[] message, int start)
        {
            Dictionary<int, byte[]> values = new Dictionary<int, byte[]>();
            int i = start;
            while (i + 2 <= message.Length)
            {
                int id = message[i];
                int length = message[i + 1];
                if (i + 2 + length > message.Length)
                    break;

                byte[] value = new byte[length];
                Array.Copy(message, i + 2, value, 0, length);
                values[id] = value;
                i += 2 + length;
            }

            return values;
        }

        /// <summary>A big endian unsigned number (-1 for an empty value).</summary>
        public static long Number(byte[] value)
        {
            if (value == null || value.Length == 0)
                return -1;

            long result = 0;
            foreach (byte b in value)
                result = (result << 8) | b;

            return result;
        }

        /// <summary>The fields of a response made of (length, value) pairs from `start` (Get Hardware Info).</summary>
        public static List<byte[]> ParseLengthValues(byte[] message, int start)
        {
            List<byte[]> fields = new List<byte[]>();
            int i = start;
            while (i < message.Length)
            {
                int length = message[i];
                if (i + 1 + length > message.Length)
                    break;

                byte[] value = new byte[length];
                Array.Copy(message, i + 1, value, 0, length);
                fields.Add(value);
                i += 1 + length;
            }

            return fields;
        }

        public static string Text(byte[] value)
        {
            if (value == null || value.Length == 0)
                return null;

            return Encoding.UTF8.GetString(value).TrimEnd('\0').Trim();
        }
    }

    /// <summary>
    /// The message of a response characteristic, put together from its packets (a start packet then continuation packets).
    /// </summary>
    internal sealed class GoProAssembler
    {
        private byte[] message;
        private int filled;

        /// <summary>Adds a packet; returns the whole message when it is complete, else null.</summary>
        public byte[] Push(byte[] packet)
        {
            if (packet == null || packet.Length == 0)
                return null;

            int start;
            if ((packet[0] & 0x80) != 0)
            {
                // A continuation without its start is dropped.
                if (message == null)
                    return null;

                start = 1;
            }
            else
            {
                int type = (packet[0] >> 5) & 0x03;
                int length;
                if (type == 0)
                {
                    length = packet[0] & 0x1F;
                    start = 1;
                }
                else if (type == 1)
                {
                    if (packet.Length < 2)
                        return null;

                    length = ((packet[0] & 0x1F) << 8) | packet[1];
                    start = 2;
                }
                else if (type == 2)
                {
                    if (packet.Length < 3)
                        return null;

                    length = (packet[1] << 8) | packet[2];
                    start = 3;
                }
                else
                {
                    return null;
                }

                message = new byte[length];
                filled = 0;
            }

            int count = Math.Min(packet.Length - start, message.Length - filled);
            if (count > 0)
            {
                Array.Copy(packet, start, message, filled, count);
                filled += count;
            }

            if (filled < message.Length)
                return null;

            byte[] done = message;
            message = null;
            filled = 0;
            return done;
        }
    }
}
