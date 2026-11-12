#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Devices.Bluetooth.Advertisement;

namespace Kinovea.Camera.GoPro
{
    /// <summary>A GoPro heard by Bluetooth: its address, name, model, signal and state, from its advertisements.</summary>
    internal sealed class GoProSighting
    {
        public ulong Address;
        public string Name;
        public int ModelId = -1;
        public short Rssi;
        public bool Pairing;
        public bool WifiOn;
        public bool Awake;
        public DateTime LastSeen;

        /// <summary>(2.22.0) The kind of its last advertisement (ConnectableUndirected, ScannableUndirected…), not of a scan response.</summary>
        public string Kind = "";

        /// <summary>
        /// Heard with the service or the manufacturer data of GoPro (an Open GoPro camera). A device heard only by a name that starts
        /// with "GoPro " is not one (another device, or a camera that cannot be controlled).
        /// </summary>
        public bool Confirmed;

        public GoProSighting Copy()
        {
            return (GoProSighting)MemberwiseClone();
        }

        /// <summary>The name of the camera: its Bluetooth name ("GoPro 1234"), else from its address.</summary>
        public string DisplayName
        {
            get { return !string.IsNullOrEmpty(Name) ? Name : "GoPro " + (Address & 0xFFFF).ToString("X4"); }
        }
    }

    /// <summary>
    /// (Swim 2.20.0) Listens to the Bluetooth advertisements of the GoPro cameras (service 0xFEA6, manufacturer data of GoPro) while
    /// the cameras are looked for, and keeps what each camera says of itself. It stops listening a minute after the last request.
    /// </summary>
    internal static class GoProScanner
    {
        private static readonly object locker = new object();
        private static readonly Dictionary<ulong, GoProSighting> sightings = new Dictionary<ulong, GoProSighting>();
        private static BluetoothLEAdvertisementWatcher watcher;
        private static DateTime lastWanted = DateTime.MinValue;
        private static System.Threading.Timer idleTimer;
        private static readonly TimeSpan idleAfter = TimeSpan.FromSeconds(60);
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>The last error of the listening (null when it works).</summary>
        public static string LastError { get; private set; }

        /// <summary>(2.22.0) A camera was heard (its address): a camera that lost its connection is looked for at once.</summary>
        public static event Action<ulong> Heard;

        // (2.22.0) While a camera that lost its connection is looked for: what is heard goes to the log (each kind once per device).
        private static DateTime verboseUntil = DateTime.MinValue;
        private static readonly HashSet<string> verboseSaid = new HashSet<string>();

        // (2.22.0) The whole advertisement of the devices of GoPro and of a remote (a name with "GoPro" or "Remote", the company or the
        // services of GoPro), once each and at most 60 per session: how a remote presents itself, for Kinovea in its place (a HERO9
        // calls its remote by itself, while it takes a computer only in pairing mode).
        private static readonly HashSet<string> surveyed = new HashSet<string>();

        /// <summary>(2.22.0) What is heard goes to the log for this long (a camera is looked for after a loss).</summary>
        public static void Verbose(TimeSpan span)
        {
            lock (locker)
            {
                DateTime until = DateTime.UtcNow + span;
                if (until > verboseUntil)
                    verboseUntil = until;
            }
        }

        /// <summary>Listen (or keep listening) for the cameras.</summary>
        public static void Want()
        {
            lock (locker)
            {
                lastWanted = DateTime.UtcNow;
                try
                {
                    if (watcher != null && watcher.Status == BluetoothLEAdvertisementWatcherStatus.Aborted)
                    {
                        Release();
                    }

                    if (watcher == null)
                    {
                        watcher = new BluetoothLEAdvertisementWatcher();
                        watcher.ScanningMode = BluetoothLEScanningMode.Active;
                        watcher.Received += Watcher_Received;
                        watcher.Stopped += Watcher_Stopped;
                    }

                    if (watcher.Status != BluetoothLEAdvertisementWatcherStatus.Started)
                    {
                        watcher.Start();
                        LastError = null;
                        log.Debug("Listening to the advertisements of GoPro cameras.");
                    }

                    if (idleTimer == null)
                        idleTimer = new System.Threading.Timer(IdleCheck, null, 5000, 5000);
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    log.Error("Could not listen to the Bluetooth advertisements.", e);
                    Release();
                }
            }
        }

        /// <summary>The cameras heard within `maxAge`, as copies.</summary>
        public static List<GoProSighting> Recent(TimeSpan maxAge)
        {
            DateTime limit = DateTime.UtcNow - maxAge;
            lock (locker)
            {
                return sightings.Values.Where(s => s.LastSeen >= limit).Select(s => s.Copy()).ToList();
            }
        }

        /// <summary>What a camera said last (a copy), or null if it was never heard.</summary>
        public static GoProSighting Find(ulong address)
        {
            lock (locker)
            {
                GoProSighting s;
                return sightings.TryGetValue(address, out s) ? s.Copy() : null;
            }
        }

        private static void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            try
            {
                BluetoothLEAdvertisement advertisement = args.Advertisement;
                bool goPro = false;
                foreach (Guid uuid in advertisement.ServiceUuids)
                {
                    if (uuid == GoProIds.ControlQueryService)
                    {
                        goPro = true;
                        break;
                    }
                }

                int model = -1;
                bool pairing = false;
                bool wifi = false;
                bool awake = false;
                foreach (BluetoothLEManufacturerData data in advertisement.ManufacturerData)
                {
                    if (data.CompanyId != GoProIds.CompanyId && data.CompanyId != 0xF202)
                        continue;

                    // Schema version, camera status (awake, Wi-Fi, pairing), model.
                    byte[] bytes = GoProBuffers.ToBytes(data.Data);
                    if (bytes.Length >= 3)
                    {
                        awake = (bytes[1] & 0x01) != 0;
                        wifi = (bytes[1] & 0x02) != 0;
                        pairing = (bytes[1] & 0x04) != 0;
                        model = bytes[2];
                    }

                    goPro = true;
                }

                string name = advertisement.LocalName;
                Survey(args, advertisement, name);
                lock (locker)
                {
                    // The scan response with the name comes apart from the advertisement with the service: the address joins them.
                    GoProSighting sighting;
                    bool known = sightings.TryGetValue(args.BluetoothAddress, out sighting);
                    if (!known && !goPro && !(name != null && name.StartsWith("GoPro ", StringComparison.Ordinal)))
                        return;

                    if (!known)
                    {
                        sighting = new GoProSighting { Address = args.BluetoothAddress };
                        sightings[args.BluetoothAddress] = sighting;
                        log.DebugFormat("GoPro heard: {0:X12} \"{1}\" (service or data of GoPro: {2}).", args.BluetoothAddress, name, goPro);
                    }

                    // (2.22.0) The name of a camera heard at another address than before: written to the log once.
                    if (!string.IsNullOrEmpty(name) && sighting.Name != name)
                    {
                        foreach (GoProSighting other in sightings.Values)
                        {
                            if (other.Address != args.BluetoothAddress && other.Name == name)
                            {
                                log.WarnFormat("GoPro \"{0}\" heard at {1:X12}, known before at {2:X12}.", name, args.BluetoothAddress, other.Address);
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(name))
                        sighting.Name = name;

                    if (goPro && !sighting.Confirmed)
                    {
                        sighting.Confirmed = true;
                        log.DebugFormat("GoPro confirmed: {0:X12} (model {1}).", args.BluetoothAddress, model);
                    }

                    if (model >= 0)
                    {
                        sighting.ModelId = model;
                        sighting.Pairing = pairing;
                        sighting.WifiOn = wifi;
                        sighting.Awake = awake;
                    }

                    sighting.Rssi = args.RawSignalStrengthInDBm;
                    sighting.LastSeen = DateTime.UtcNow;
                    if (args.AdvertisementType != BluetoothLEAdvertisementType.ScanResponse)
                        sighting.Kind = args.AdvertisementType.ToString();

                    if (DateTime.UtcNow < verboseUntil)
                    {
                        string key = string.Format("{0:X12}|{1}|{2}|{3}|{4}", args.BluetoothAddress, sighting.Name, args.AdvertisementType, goPro, pairing);
                        if (verboseSaid.Add(key))
                            log.WarnFormat("Heard while a camera is looked for: {0:X12} \"{1}\" ({2}, data of GoPro {3}, pairing {4}, awake {5}, {6} dBm).",
                                args.BluetoothAddress, sighting.Name, args.AdvertisementType, goPro, pairing, awake, args.RawSignalStrengthInDBm);
                    }
                }

                Action<ulong> heard = Heard;
                if (heard != null)
                    heard(args.BluetoothAddress);
            }
            catch (Exception e)
            {
                log.Error("Error while reading a Bluetooth advertisement.", e);
            }
        }

        private static void Survey(BluetoothLEAdvertisementReceivedEventArgs args, BluetoothLEAdvertisement advertisement, string name)
        {
            bool company = advertisement.ManufacturerData.Any(d => d.CompanyId == GoProIds.CompanyId || d.CompanyId == 0xF202);
            bool service = advertisement.ServiceUuids.Any(u => u == GoProIds.ControlQueryService || u.ToString("D").EndsWith("-aa8d-11e3-9046-0002a5d5c51b", StringComparison.OrdinalIgnoreCase));
            bool named = !string.IsNullOrEmpty(name) && (name.IndexOf("gopro", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!company && !service && !named)
                return;

            string sections = string.Join(" ", advertisement.DataSections.Select(s => s.DataType.ToString("X2") + ":" + BitConverter.ToString(GoProBuffers.ToBytes(s.Data))));
            string key = string.Format("{0:X12}|{1}|{2}|{3}", args.BluetoothAddress, args.AdvertisementType, name, sections);
            lock (locker)
            {
                if (surveyed.Count >= 60 || !surveyed.Add(key))
                    return;
            }

            log.WarnFormat("Advertisement {0:X12} \"{1}\" ({2}, {3}, {4} dBm): {5}.", args.BluetoothAddress, name, args.AdvertisementType,
                args.BluetoothAddressType, args.RawSignalStrengthInDBm, sections);
        }

        private static void Watcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            if (args.Error != Windows.Devices.Bluetooth.BluetoothError.Success)
            {
                LastError = args.Error.ToString();
                log.ErrorFormat("The listening to the Bluetooth advertisements stopped: {0}.", args.Error);
            }
        }

        private static void IdleCheck(object state)
        {
            lock (locker)
            {
                if (DateTime.UtcNow - lastWanted < idleAfter)
                    return;

                Release();
                log.Debug("Stopped listening to the advertisements of GoPro cameras.");
            }
        }

        /// <summary>Stops listening (under the lock).</summary>
        private static void Release()
        {
            if (idleTimer != null)
            {
                idleTimer.Dispose();
                idleTimer = null;
            }

            if (watcher == null)
                return;

            try
            {
                watcher.Received -= Watcher_Received;
                watcher.Stopped -= Watcher_Stopped;
                if (watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                    watcher.Stop();
            }
            catch (Exception e)
            {
                log.Error("Error while stopping the listening to the Bluetooth advertisements.", e);
            }

            watcher = null;
        }
    }

    /// <summary>The bytes of the buffers of Windows (IBuffer) and back.</summary>
    internal static class GoProBuffers
    {
        public static byte[] ToBytes(Windows.Storage.Streams.IBuffer buffer)
        {
            if (buffer == null || buffer.Length == 0)
                return new byte[0];

            byte[] bytes = new byte[buffer.Length];
            Windows.Storage.Streams.DataReader reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer);
            reader.ReadBytes(bytes);
            return bytes;
        }

        public static Windows.Storage.Streams.IBuffer FromBytes(byte[] bytes)
        {
            Windows.Storage.Streams.DataWriter writer = new Windows.Storage.Streams.DataWriter();
            writer.WriteBytes(bytes);
            return writer.DetachBuffer();
        }
    }
}
