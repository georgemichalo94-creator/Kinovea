#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace Kinovea.Camera.GoPro
{
    /// <summary>The state of the link with a camera.</summary>
    internal enum GoProLink
    {
        Idle,
        NoBluetooth,
        BluetoothOff,
        Searching,
        Connecting,
        Pairing,
        NeedsPairing,
        Connected
    }

    /// <summary>What a screen shows of a camera at one moment (a copy, safe on any thread).</summary>
    internal sealed class GoProState
    {
        public string Alias;
        public string BleName;
        public string ModelName;
        public string Firmware;
        public GoProLink Link;
        public double LinkSeconds;
        public string Problem;

        public bool Recording;
        public bool CommandPending;
        public long RecordingSeconds;
        public double LastStartOffset = double.NaN;
        public DateTime LastStartAt = DateTime.MinValue;

        public int BatteryPercent = -1;
        public int BatteryBars = -1;
        public bool Hot;
        public bool Cold;
        public bool Busy;
        public int PrimaryStorage = -1;
        public long RemainingVideoSeconds = -1;
        public long SdRemainingKB = -1;
        public bool SdSpeedError;
        public bool WifiOn;

        public int Resolution = -1;
        public int Fps = -1;
        public int Lens = -1;
        public int AntiFlicker = -1;
        public int Hypersmooth = -1;

        public short Rssi;
        public DateTime ClockSetAt = DateTime.MinValue;
        public bool PreviewOn;

        /// <summary>The seconds before the live image closes by itself (-1 when it is not on).</summary>
        public int PreviewSecondsLeft = -1;

        /// <summary>How an action went (a set of settings), for a while; amber when something did not go.</summary>
        public string Notice;
        public bool NoticeError;
    }

    /// <summary>
    /// (Swim 2.20.0) The Bluetooth connection with one GoPro (HERO9 Black and later, the Open GoPro interface): pairing the first time,
    /// the commands (shutter, clock, Wi-Fi), the statuses and settings pushed by the camera, the keep alive every 3 s, and the
    /// connection again when it is lost. One object per camera, shared by the screens that show it.
    /// </summary>
    internal sealed class GoProDevice
    {
        #region Registry
        private static readonly object registryLock = new object();
        private static readonly Dictionary<ulong, GoProDevice> registry = new Dictionary<ulong, GoProDevice>();

        /// <summary>The camera at this address, connected while at least one screen holds it.</summary>
        public static GoProDevice Acquire(ulong address)
        {
            GoProDevice device;
            bool begin;
            lock (registryLock)
            {
                if (!registry.TryGetValue(address, out device))
                {
                    device = new GoProDevice(address);
                    registry[address] = device;
                }

                device.refs++;
                begin = device.refs == 1;
            }

            if (begin)
                device.Begin();

            return device;
        }

        /// <summary>A screen lets the camera go; the connection ends with the last one (the recording on the camera goes on).</summary>
        public void Release()
        {
            lock (registryLock)
            {
                if (refs == 0)
                    return;

                refs--;
                if (refs > 0)
                    return;
            }

            End();
        }

        /// <summary>The cameras held by a screen now.</summary>
        public static List<GoProDevice> Active()
        {
            lock (registryLock)
            {
                return registry.Values.Where(d => d.refs > 0).ToList();
            }
        }

        /// <summary>The camera at this address if a screen holds it, else null.</summary>
        public static GoProDevice FindActive(ulong address)
        {
            lock (registryLock)
            {
                GoProDevice device;
                return registry.TryGetValue(address, out device) && device.refs > 0 ? device : null;
            }
        }
        #endregion

        #region Events and properties
        /// <summary>The recording of the camera (or what was asked of it while a command is on its way) changed. Any thread.</summary>
        public event EventHandler RecordingChanged;

        public ulong Address { get; private set; }

        public bool IsConnected
        {
            get { lock (locker) { return connected; } }
        }

        /// <summary>The camera records (what it says; while a command is on its way, what was asked).</summary>
        public bool EffectiveRecording
        {
            get { lock (locker) { return EffectiveUnlocked(); } }
        }
        #endregion

        #region Members
        private int refs;
        private readonly object locker = new object();
        private CancellationTokenSource cancel;
        private Task loop;

        // Bluetooth objects (set by the connection, read under the lock).
        private BluetoothLEDevice ble;
        private GattSession session;
        private readonly List<GattDeviceService> services = new List<GattDeviceService>();
        private GattCharacteristic command;
        private GattCharacteristic commandResponse;
        private GattCharacteristic settings;
        private GattCharacteristic settingsResponse;
        private GattCharacteristic query;
        private GattCharacteristic queryResponse;
        private GattCharacteristic networkCommand;
        private GattCharacteristic networkResponse;
        private GattCharacteristic wifiSsid;
        private GattCharacteristic wifiPassword;
        private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, TaskCompletionSource<byte[]>> pending = new Dictionary<string, TaskCompletionSource<byte[]>>();
        private readonly Dictionary<Guid, GoProAssembler> assemblers = new Dictionary<Guid, GoProAssembler>();

        // State (under the lock).
        private bool connected;
        private bool lostSignal;
        private GoProLink link = GoProLink.Idle;
        private DateTime linkSince = DateTime.UtcNow;
        private string problem;
        private DateTime problemUntil = DateTime.MinValue;
        private string notice;
        private bool noticeError;
        private DateTime noticeUntil = DateTime.MinValue;
        private int applying;
        private string modelName;
        private string firmware;
        private int batteryPercent = -1;
        private int batteryBars = -1;
        private int primaryStorage = -1;
        private long remainingVideo = -1;
        private long sdRemainingKB = -1;
        private long encodingSeconds = -1;
        private bool hot;
        private bool cold;
        private bool busy;
        private bool encoding;
        private bool encodingKnown;
        private bool apOn;
        private bool sdSpeedError;
        private int resolution = -1;
        private int fps = -1;
        private int lens = -1;
        private int antiFlicker = -1;
        private int hypersmooth = -1;
        private DateTime clockSetAt = DateTime.MinValue;
        private DateTime encodingSince = DateTime.MinValue;
        private bool desiredRecording;
        private DateTime pendingUntil = DateTime.MinValue;
        private DateTime lastCommandTime = DateTime.MinValue;
        private bool lastCommandStart;
        private double lastStartOffset = double.NaN;
        private DateTime lastStartAt = DateTime.MinValue;
        private bool reportedRecording;
        private bool previewWanted;

        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        #endregion

        private GoProDevice(ulong address)
        {
            Address = address;
        }

        #region Life
        private void Begin()
        {
            Task previous = loop;
            cancel = new CancellationTokenSource();
            CancellationToken token = cancel.Token;
            loop = Task.Run(async () =>
            {
                // The previous connection of this camera finishes first.
                if (previous != null)
                {
                    try { await previous.ConfigureAwait(false); }
                    catch (Exception) { }
                }

                await RunAsync(token).ConfigureAwait(false);
            });
        }

        private void End()
        {
            if (cancel != null)
                cancel.Cancel();
        }

        private async Task RunAsync(CancellationToken token)
        {
            int ticks = 0;
            int failures = 0;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!IsConnected)
                    {
                        GoProScanner.Want();
                        await ConnectAsync(token).ConfigureAwait(false);
                        ticks = 0;
                        failures = 0;
                    }
                    else
                    {
                        bool lost;
                        lock (locker)
                            lost = lostSignal;

                        if (lost)
                        {
                            log.DebugFormat("Connection lost with the GoPro {0:X12}.", Address);
                            DisconnectGatt();
                            SetLink(GoProLink.Searching, GoProText.T("Η σύνδεση χάθηκε: ξανασυνδέεται…", "The connection was lost: connecting again…"));
                        }
                        else
                        {
                            // The keep alive every 3 s; the main statuses asked again every 15 s in case a change was not pushed.
                            bool ok = await WriteAsync(Get(c => c.settings), new byte[] { GoProIds.SettingLed, 0x01, GoProIds.KeepAliveValue }).ConfigureAwait(false);
                            failures = ok ? 0 : failures + 1;
                            if (failures >= 3)
                            {
                                lock (locker)
                                    lostSignal = true;
                            }
                            else if (ticks % 5 == 4)
                            {
                                await QueryStatusesAsync(MainStatuses).ConfigureAwait(false);
                            }

                            ticks++;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    // The details go to the log; the screen says what happens next.
                    log.Error(string.Format("Error with the GoPro {0:X12}.", Address), e);
                    DisconnectGatt();
                    SetLink(GoProLink.Searching, GoProText.T("Η σύνδεση δεν ολοκληρώθηκε: ξαναδοκιμάζεται σε λίγα δευτερόλεπτα…",
                        "The connection did not complete: it is tried again in a few seconds…"));
                }

                try
                {
                    await Task.Delay(IsConnected ? 3000 : 4000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            DisconnectGatt();
            SetLink(GoProLink.Idle, null);
        }
        #endregion

        #region Connection
        private async Task ConnectAsync(CancellationToken token)
        {
            // The Bluetooth of the computer.
            BluetoothAdapter adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(token).ConfigureAwait(false);
            if (adapter == null || !adapter.IsLowEnergySupported)
            {
                SetLink(GoProLink.NoBluetooth, GoProText.T("Δεν βρέθηκε Bluetooth στον υπολογιστή.", "No Bluetooth was found on this computer."));
                return;
            }

            try
            {
                Radio radio = await adapter.GetRadioAsync().AsTask(token).ConfigureAwait(false);
                if (radio != null && radio.State != RadioState.On)
                {
                    SetLink(GoProLink.BluetoothOff, GoProText.T("Το Bluetooth του υπολογιστή είναι κλειστό: άνοιξέ το από τις ρυθμίσεις των Windows.",
                        "The Bluetooth of the computer is off: turn it on in the settings of Windows."));
                    return;
                }
            }
            catch (Exception)
            {
                // The state of the radio is not always given to desktop programs.
            }

            SetLink(GoProLink.Connecting, null);
            BluetoothLEDevice device = await BluetoothLEDevice.FromBluetoothAddressAsync(Address).AsTask(token).ConfigureAwait(false);
            if (device == null)
            {
                SetLink(GoProLink.Searching, NotAnsweringHint());
                return;
            }

            // Pairing, once per computer: the camera must be in pairing mode. (Windows can give a device without its information
            // while it is still being added: tried again at the next round.)
            DeviceInformation information = device.DeviceInformation;
            DeviceInformationPairing pairing = information != null ? information.Pairing : null;
            if (pairing == null)
            {
                device.Dispose();
                log.WarnFormat("The GoPro {0:X12} came without its pairing information: tried again.", Address);
                SetLink(GoProLink.Searching, GoProText.T("Η κάμερα βρέθηκε, η σύνδεση ξαναδοκιμάζεται…", "The camera was found, the connection is tried again…"));
                return;
            }

            if (!pairing.IsPaired)
            {
                if (!pairing.CanPair || !InPairingMode())
                {
                    device.Dispose();
                    SetLink(GoProLink.NeedsPairing, PairingHint());
                    return;
                }

                SetLink(GoProLink.Pairing, GoProText.T("Σύζευξη με τον υπολογιστή…", "Pairing with the computer…"));
                DeviceInformationCustomPairing custom = pairing.Custom;
                DevicePairingResult result;
                custom.PairingRequested += Custom_PairingRequested;
                try
                {
                    DevicePairingKinds kinds = DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.DisplayPin;
                    result = await custom.PairAsync(kinds, DevicePairingProtectionLevel.Encryption).AsTask(token).ConfigureAwait(false);
                }
                finally
                {
                    custom.PairingRequested -= Custom_PairingRequested;
                }

                DevicePairingResultStatus status = result != null ? result.Status : DevicePairingResultStatus.Failed;
                log.WarnFormat("Pairing with the GoPro {0:X12}: {1}.", Address, status);
                if (status != DevicePairingResultStatus.Paired && status != DevicePairingResultStatus.AlreadyPaired)
                {
                    device.Dispose();
                    SetLink(GoProLink.NeedsPairing, PairingHint() + " (" + status + ")");
                    return;
                }
            }

            // The services of the camera: this is the connection itself.
            GattDeviceServicesResult found = await device.GetGattServicesForUuidAsync(GoProIds.ControlQueryService, BluetoothCacheMode.Uncached).AsTask(token).ConfigureAwait(false);
            if (found.Status != GattCommunicationStatus.Success || found.Services.Count == 0)
            {
                device.Dispose();
                if (found.Status == GattCommunicationStatus.AccessDenied)
                    SetLink(GoProLink.NeedsPairing, PairingHint());
                else
                    SetLink(GoProLink.Searching, NotAnsweringHint());

                return;
            }

            lock (locker)
            {
                ble = device;
                services.Add(found.Services[0]);
            }

            device.ConnectionStatusChanged += Device_ConnectionStatusChanged;

            GattCharacteristicsResult characteristics = await found.Services[0].GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(token).ConfigureAwait(false);
            if (characteristics.Status != GattCommunicationStatus.Success)
            {
                Fail(characteristics.Status);
                return;
            }

            lock (locker)
            {
                foreach (GattCharacteristic c in characteristics.Characteristics)
                {
                    if (c.Uuid == GoProIds.Command) command = c;
                    else if (c.Uuid == GoProIds.CommandResponse) commandResponse = c;
                    else if (c.Uuid == GoProIds.Settings) settings = c;
                    else if (c.Uuid == GoProIds.SettingsResponse) settingsResponse = c;
                    else if (c.Uuid == GoProIds.Query) query = c;
                    else if (c.Uuid == GoProIds.QueryResponse) queryResponse = c;
                }
            }

            if (Get(c => c.command) == null || Get(c => c.commandResponse) == null || Get(c => c.settings) == null ||
                Get(c => c.settingsResponse) == null || Get(c => c.query) == null || Get(c => c.queryResponse) == null)
            {
                Fail(GattCommunicationStatus.Unreachable);
                return;
            }

            foreach (GattCharacteristic c in new GattCharacteristic[] { Get(x => x.commandResponse), Get(x => x.settingsResponse), Get(x => x.queryResponse) })
            {
                GattCommunicationStatus status = await c.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(token).ConfigureAwait(false);
                if (status != GattCommunicationStatus.Success)
                {
                    // Windows keeps a pairing that the camera forgot (a reset): it is dropped, the camera in pairing mode pairs again.
                    if ((status == GattCommunicationStatus.AccessDenied || status == GattCommunicationStatus.ProtocolError) && InPairingMode())
                    {
                        try { await device.DeviceInformation.Pairing.UnpairAsync().AsTask(token).ConfigureAwait(false); }
                        catch (Exception e) { log.Debug("Could not drop the old pairing.", e); }
                    }

                    Fail(status);
                    return;
                }

                c.ValueChanged += Response_ValueChanged;
            }

            // Optional services: the network management (the end of the pairing) and the Wi-Fi of the camera (the live image).
            await FindOptionalAsync(device, token).ConfigureAwait(false);

            try
            {
                GattSession s = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(token).ConfigureAwait(false);
                if (s != null)
                {
                    s.MaintainConnection = true;
                    lock (locker)
                        session = s;
                }
            }
            catch (Exception e)
            {
                log.Debug("Could not keep the Bluetooth session.", e);
            }

            lock (locker)
            {
                connected = true;
                lostSignal = false;
            }

            SetLink(GoProLink.Connected, null);
            log.DebugFormat("Connected to the GoPro {0:X12}.", Address);
            await PrepareAsync(token).ConfigureAwait(false);
        }

        private async Task FindOptionalAsync(BluetoothLEDevice device, CancellationToken token)
        {
            try
            {
                GattDeviceServicesResult network = await device.GetGattServicesForUuidAsync(GoProIds.NetworkService, BluetoothCacheMode.Uncached).AsTask(token).ConfigureAwait(false);
                if (network.Status == GattCommunicationStatus.Success && network.Services.Count > 0)
                {
                    lock (locker)
                        services.Add(network.Services[0]);

                    GattCharacteristicsResult chars = await network.Services[0].GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(token).ConfigureAwait(false);
                    if (chars.Status == GattCommunicationStatus.Success)
                    {
                        GattCharacteristic cmd = chars.Characteristics.FirstOrDefault(c => c.Uuid == GoProIds.NetworkCommand);
                        GattCharacteristic rsp = chars.Characteristics.FirstOrDefault(c => c.Uuid == GoProIds.NetworkResponse);
                        if (cmd != null && rsp != null)
                        {
                            GattCommunicationStatus status = await rsp.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(token).ConfigureAwait(false);
                            if (status == GattCommunicationStatus.Success)
                            {
                                rsp.ValueChanged += Response_ValueChanged;
                                lock (locker)
                                {
                                    networkCommand = cmd;
                                    networkResponse = rsp;
                                }
                            }
                        }
                    }
                }

                GattDeviceServicesResult wifi = await device.GetGattServicesForUuidAsync(GoProIds.WifiApService, BluetoothCacheMode.Uncached).AsTask(token).ConfigureAwait(false);
                if (wifi.Status == GattCommunicationStatus.Success && wifi.Services.Count > 0)
                {
                    lock (locker)
                        services.Add(wifi.Services[0]);

                    GattCharacteristicsResult chars = await wifi.Services[0].GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(token).ConfigureAwait(false);
                    if (chars.Status == GattCommunicationStatus.Success)
                    {
                        lock (locker)
                        {
                            wifiSsid = chars.Characteristics.FirstOrDefault(c => c.Uuid == GoProIds.WifiApSsid);
                            wifiPassword = chars.Characteristics.FirstOrDefault(c => c.Uuid == GoProIds.WifiApPassword);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                log.Debug("The optional services of the camera were not found.", e);
            }
        }

        /// <summary>
        /// After the connection: wait until the camera answers, finish the pairing, the clock of the computer, all the statuses and
        /// settings with their changes, the Wi-Fi off (unless the live image is on).
        /// </summary>
        private async Task PrepareAsync(CancellationToken token)
        {
            for (int i = 0; i < 10 && !token.IsCancellationRequested; i++)
            {
                byte[] info = await RequestAsync(Get(c => c.command), GoProIds.CommandResponse, GoProIds.CmdGetHardwareInfo, new byte[] { GoProIds.CmdGetHardwareInfo }, 1000).ConfigureAwait(false);
                if (info != null && info.Length >= 2 && info[1] == 0)
                {
                    ReadHardwareInfo(info);
                    break;
                }

                await Task.Delay(500, token).ConfigureAwait(false);
            }

            GattCharacteristic network = Get(c => c.networkCommand);
            if (network != null)
            {
                // RequestPairingFinish { result = SUCCESS (0), phoneName = "Kinovea" } on the feature 0x03, action 0x01.
                byte[] name = Encoding.ASCII.GetBytes("Kinovea");
                List<byte> message = new List<byte> { GoProIds.FeatureWirelessManagement, GoProIds.ActionSetPairingState, 0x08, 0x00, 0x12, (byte)name.Length };
                message.AddRange(name);
                await RequestAsync(network, GoProIds.NetworkResponse, GoProIds.FeatureWirelessManagement, message.ToArray(), 1500).ConfigureAwait(false);
            }

            await RequestAsync(Get(c => c.command), GoProIds.CommandResponse, GoProIds.CmdSetThirdPartyClient, new byte[] { GoProIds.CmdSetThirdPartyClient }, 1000).ConfigureAwait(false);
            await SetClockAsync(token).ConfigureAwait(false);
            await RequestAsync(Get(c => c.query), GoProIds.QueryResponse, GoProIds.QueryRegisterStatuses, new byte[] { GoProIds.QueryRegisterStatuses }, 4000).ConfigureAwait(false);
            await RequestAsync(Get(c => c.query), GoProIds.QueryResponse, GoProIds.QueryRegisterSettings, new byte[] { GoProIds.QueryRegisterSettings }, 4000).ConfigureAwait(false);

            bool preview;
            lock (locker)
                preview = previewWanted;

            if (!preview)
                await SetWifiAsync(false).ConfigureAwait(false);
        }

        /// <summary>The clock of the camera from the computer, sent at the start of a second (the files of all the cameras then share it).</summary>
        private async Task SetClockAsync(CancellationToken token)
        {
            int wait = 1000 - DateTime.Now.Millisecond;
            if (wait > 0 && wait < 1000)
                await Task.Delay(wait, token).ConfigureAwait(false);

            DateTime now = DateTime.Now;
            byte[] message = new byte[]
            {
                GoProIds.CmdSetDateTime, 0x07,
                (byte)(now.Year >> 8), (byte)(now.Year & 0xFF), (byte)now.Month, (byte)now.Day, (byte)now.Hour, (byte)now.Minute, (byte)now.Second
            };

            byte[] response = await RequestAsync(Get(c => c.command), GoProIds.CommandResponse, GoProIds.CmdSetDateTime, message, 1500).ConfigureAwait(false);
            if (response != null && response.Length >= 2 && response[1] == 0)
            {
                lock (locker)
                    clockSetAt = now;
            }
        }

        private void ReadHardwareInfo(byte[] message)
        {
            // [id, status, (length, value)…]: model number, model name, (deprecated), firmware, serial number, AP SSID, AP MAC.
            List<byte[]> fields = GoProPackets.ParseLengthValues(message, 2);
            lock (locker)
            {
                if (fields.Count > 1)
                    modelName = GoProPackets.Text(fields[1]);

                if (fields.Count > 3)
                    firmware = GoProPackets.Text(fields[3]);
            }

            GoProSighting seen = GoProScanner.Find(Address);
            log.DebugFormat("The GoPro {0:X12} is \"{1}\", {2}, firmware {3}.", Address, seen != null ? seen.Name : "?", modelName, firmware);
        }

        private void Fail(GattCommunicationStatus status)
        {
            DisconnectGatt();
            if (status == GattCommunicationStatus.AccessDenied || status == GattCommunicationStatus.ProtocolError)
                SetLink(GoProLink.NeedsPairing, PairingHint());
            else
                SetLink(GoProLink.Searching, NotAnsweringHint());
        }

        /// <summary>The camera was heard in pairing mode in the last 20 s.</summary>
        private bool InPairingMode()
        {
            GoProSighting seen = GoProScanner.Find(Address);
            return seen != null && seen.Pairing && (DateTime.UtcNow - seen.LastSeen).TotalSeconds < 20;
        }

        private void Custom_PairingRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
        {
            // GoPro pairs without a code ("just works").
            args.Accept();
        }

        private void Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            try
            {
                if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                {
                    lock (locker)
                    {
                        if (connected)
                            lostSignal = true;
                    }
                }
            }
            catch (Exception e)
            {
                log.Error("Error with the connection status of the camera.", e);
            }
        }

        /// <summary>Lets the Bluetooth objects go (the camera keeps recording if it does).</summary>
        private void DisconnectGatt()
        {
            List<GattCharacteristic> notified = new List<GattCharacteristic>();
            List<GattDeviceService> held;
            BluetoothLEDevice device;
            GattSession s;
            List<TaskCompletionSource<byte[]>> waiting;
            lock (locker)
            {
                foreach (GattCharacteristic c in new GattCharacteristic[] { commandResponse, settingsResponse, queryResponse, networkResponse })
                {
                    if (c != null)
                        notified.Add(c);
                }

                held = services.ToList();
                services.Clear();
                device = ble;
                s = session;
                ble = null;
                session = null;
                command = commandResponse = settings = settingsResponse = query = queryResponse = null;
                networkCommand = networkResponse = wifiSsid = wifiPassword = null;
                connected = false;
                lostSignal = false;
                waiting = pending.Values.ToList();
                pending.Clear();
                assemblers.Clear();
            }

            foreach (TaskCompletionSource<byte[]> tcs in waiting)
                tcs.TrySetResult(null);

            foreach (GattCharacteristic c in notified)
            {
                try { c.ValueChanged -= Response_ValueChanged; }
                catch (Exception) { }
            }

            try
            {
                if (s != null)
                {
                    s.MaintainConnection = false;
                    s.Dispose();
                }
            }
            catch (Exception) { }

            foreach (GattDeviceService service in held)
            {
                try { service.Dispose(); }
                catch (Exception) { }
            }

            if (device != null)
            {
                try
                {
                    device.ConnectionStatusChanged -= Device_ConnectionStatusChanged;
                    device.Dispose();
                }
                catch (Exception) { }
            }
        }
        #endregion

        #region Messages
        private GattCharacteristic Get(Func<GoProDevice, GattCharacteristic> pick)
        {
            lock (locker)
                return pick(this);
        }

        /// <summary>Writes a message in its packets, one message at a time. False if it could not be written.</summary>
        private async Task<bool> WriteAsync(GattCharacteristic characteristic, byte[] message)
        {
            if (characteristic == null)
                return false;

            await writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                GattWriteOption option = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ?
                    GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;

                foreach (byte[] packet in GoProPackets.Split(message))
                {
                    GattCommunicationStatus status = await characteristic.WriteValueAsync(GoProBuffers.FromBytes(packet), option).AsTask().ConfigureAwait(false);
                    if (status != GattCommunicationStatus.Success)
                        return false;
                }

                return true;
            }
            catch (Exception e)
            {
                log.Debug("Could not write to the camera.", e);
                return false;
            }
            finally
            {
                writeLock.Release();
            }
        }

        /// <summary>Writes a message and waits for its response (identified by its first byte on its response characteristic).</summary>
        private async Task<byte[]> RequestAsync(GattCharacteristic characteristic, Guid response, byte id, byte[] message, int timeoutMs)
        {
            if (characteristic == null)
                return null;

            TaskCompletionSource<byte[]> tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            string key = Key(response, id);
            lock (locker)
            {
                TaskCompletionSource<byte[]> previous;
                if (pending.TryGetValue(key, out previous))
                    previous.TrySetResult(null);

                pending[key] = tcs;
            }

            try
            {
                if (!await WriteAsync(characteristic, message).ConfigureAwait(false))
                    return null;

                Task done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
                return done == tcs.Task ? tcs.Task.Result : null;
            }
            finally
            {
                lock (locker)
                {
                    TaskCompletionSource<byte[]> current;
                    if (pending.TryGetValue(key, out current) && current == tcs)
                        pending.Remove(key);
                }
            }
        }

        private static string Key(Guid characteristic, byte id)
        {
            return characteristic.ToString("N") + ":" + id.ToString("X2");
        }

        private void Response_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                byte[] packet = GoProBuffers.ToBytes(args.CharacteristicValue);
                Guid uuid = sender.Uuid;
                byte[] message;
                lock (locker)
                {
                    GoProAssembler assembler;
                    if (!assemblers.TryGetValue(uuid, out assembler))
                    {
                        assembler = new GoProAssembler();
                        assemblers[uuid] = assembler;
                    }

                    message = assembler.Push(packet);
                }

                if (message == null || message.Length == 0)
                    return;

                Handle(uuid, message);
            }
            catch (Exception e)
            {
                log.Error("Error while reading a message of the camera.", e);
            }
        }

        private void Handle(Guid uuid, byte[] message)
        {
            byte id = message[0];
            if (uuid == GoProIds.QueryResponse && message.Length >= 2)
            {
                if (id == GoProIds.QueryGetStatuses || id == GoProIds.QueryRegisterStatuses || id == GoProIds.QueryStatusPush)
                    ApplyStatuses(GoProPackets.ParseTlv(message, 2));
                else if (id == GoProIds.QueryGetSettings || id == GoProIds.QueryRegisterSettings || id == GoProIds.QuerySettingPush)
                    ApplySettings(GoProPackets.ParseTlv(message, 2));
            }

            TaskCompletionSource<byte[]> tcs = null;
            lock (locker)
            {
                string key = Key(uuid, id);
                if (pending.TryGetValue(key, out tcs))
                    pending.Remove(key);
            }

            if (tcs != null)
                tcs.TrySetResult(message);

            CheckRecordingChange();
        }

        private static readonly byte[] MainStatuses = new byte[]
        {
            GoProIds.StatusBatteryPercent, GoProIds.StatusBatteryBars, GoProIds.StatusHot, GoProIds.StatusBusy, GoProIds.StatusEncoding,
            GoProIds.StatusEncodingSeconds, GoProIds.StatusPrimaryStorage, GoProIds.StatusRemainingVideo, GoProIds.StatusSdRemaining,
            GoProIds.StatusApMode
        };

        private async Task QueryStatusesAsync(byte[] ids)
        {
            byte[] message = new byte[ids.Length + 1];
            message[0] = GoProIds.QueryGetStatuses;
            Array.Copy(ids, 0, message, 1, ids.Length);
            await RequestAsync(Get(c => c.query), GoProIds.QueryResponse, GoProIds.QueryGetStatuses, message, 2000).ConfigureAwait(false);
        }

        private void ApplyStatuses(Dictionary<int, byte[]> values)
        {
            lock (locker)
            {
                byte[] v;
                if (values.TryGetValue(GoProIds.StatusBatteryPercent, out v)) batteryPercent = (int)GoProPackets.Number(v);
                if (values.TryGetValue(GoProIds.StatusBatteryBars, out v)) batteryBars = (int)GoProPackets.Number(v);
                if (values.TryGetValue(GoProIds.StatusHot, out v)) hot = GoProPackets.Number(v) > 0;
                if (values.TryGetValue(GoProIds.StatusCold, out v)) cold = GoProPackets.Number(v) > 0;
                if (values.TryGetValue(GoProIds.StatusBusy, out v)) busy = GoProPackets.Number(v) > 0;
                if (values.TryGetValue(GoProIds.StatusPrimaryStorage, out v) && v.Length > 0) primaryStorage = (sbyte)v[v.Length - 1];
                if (values.TryGetValue(GoProIds.StatusRemainingVideo, out v)) remainingVideo = GoProPackets.Number(v);
                if (values.TryGetValue(GoProIds.StatusSdRemaining, out v)) sdRemainingKB = GoProPackets.Number(v);
                if (values.TryGetValue(GoProIds.StatusApMode, out v)) apOn = GoProPackets.Number(v) > 0;
                if (values.TryGetValue(GoProIds.StatusSdSpeedError, out v)) sdSpeedError = GoProPackets.Number(v) > 0;

                if (values.TryGetValue(GoProIds.StatusEncoding, out v))
                {
                    bool now = GoProPackets.Number(v) > 0;
                    if (!encodingKnown || now != encoding)
                    {
                        if (now)
                        {
                            encodingSince = DateTime.Now;
                            if (lastCommandStart && lastCommandTime != DateTime.MinValue && (DateTime.Now - lastCommandTime).TotalSeconds < 15)
                            {
                                lastStartOffset = (DateTime.Now - lastCommandTime).TotalSeconds;
                                lastStartAt = DateTime.Now;
                                log.DebugFormat("The GoPro {0:X12} started recording {1:0.000} s after the command.", Address, lastStartOffset);
                            }
                        }
                        else
                        {
                            encodingSince = DateTime.MinValue;
                        }

                        encoding = now;
                        encodingKnown = true;
                    }

                    // The command is confirmed by the camera.
                    if (pendingUntil > DateTime.UtcNow && desiredRecording == encoding)
                        pendingUntil = DateTime.MinValue;
                }

                if (values.TryGetValue(GoProIds.StatusEncodingSeconds, out v))
                {
                    encodingSeconds = GoProPackets.Number(v);

                    // A recording that started before the connection: its time from the camera.
                    if (encoding && encodingSeconds > 0)
                    {
                        DateTime since = DateTime.Now - TimeSpan.FromSeconds(encodingSeconds);
                        if (encodingSince == DateTime.MinValue || Math.Abs((since - encodingSince).TotalSeconds) > 2)
                            encodingSince = since;
                    }
                }
            }
        }

        private void ApplySettings(Dictionary<int, byte[]> values)
        {
            lock (locker)
            {
                byte[] v;
                if (values.TryGetValue(GoProIds.SettingResolution, out v)) resolution = (int)GoProPackets.Number(v);
                if (values.TryGetValue(GoProIds.SettingFps, out v)) fps = (int)GoProPackets.Number(v);
                if (values.TryGetValue(GoProIds.SettingLens, out v)) lens = (int)GoProPackets.Number(v);
                if (values.TryGetValue(GoProIds.SettingAntiFlicker, out v)) antiFlicker = (int)GoProPackets.Number(v);
                if (values.TryGetValue(GoProIds.SettingHypersmooth, out v)) hypersmooth = (int)GoProPackets.Number(v);
            }
        }
        #endregion

        #region Recording
        /// <summary>
        /// Start or stop the recording. Returns at once; the screens see the asked state until the camera confirms it (or for 8 s).
        /// The moment of the command is shared by the cameras started together: the start of each is measured from it.
        /// </summary>
        public void SetRecording(bool record, DateTime commandTime)
        {
            lock (locker)
            {
                desiredRecording = record;
                pendingUntil = DateTime.UtcNow.AddSeconds(8);
                lastCommandTime = commandTime;
                lastCommandStart = record;

                // The message of a previous command goes (the one of the link stays).
                if (problemUntil != DateTime.MinValue)
                {
                    problem = null;
                    problemUntil = DateTime.MinValue;
                }
            }

            CheckRecordingChange();
            Task.Run(() => SendShutterAsync(record));
        }

        private async Task SendShutterAsync(bool record)
        {
            bool accepted = false;
            try
            {
                for (int attempt = 0; attempt < 4 && !accepted; attempt++)
                {
                    // Not connected yet: the command waits up to 4 s.
                    for (int w = 0; w < 20 && !IsConnected; w++)
                        await Task.Delay(200).ConfigureAwait(false);

                    if (!IsConnected)
                        break;

                    byte[] message = new byte[] { GoProIds.CmdSetShutter, 0x01, (byte)(record ? 1 : 0) };
                    byte[] response = await RequestAsync(Get(c => c.command), GoProIds.CommandResponse, GoProIds.CmdSetShutter, message, 2500).ConfigureAwait(false);
                    if (response != null && response.Length >= 2 && response[1] == 0)
                        accepted = true;
                    else
                        await Task.Delay(400).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                log.Error("Error while sending the command of the shutter.", e);
            }

            if (!accepted)
            {
                lock (locker)
                {
                    if (desiredRecording == record)
                    {
                        pendingUntil = DateTime.MinValue;
                        string reason = record ? BlockedReasonUnlocked() : null;
                        problem = reason ?? (record ?
                            GoProText.T("Η κάμερα δεν δέχτηκε την εντολή έναρξης: δες αν είναι συνδεδεμένη και όχι σε μενού.",
                                "The camera did not take the start command: check that it is connected and not in a menu.") :
                            GoProText.T("Η κάμερα δεν δέχτηκε την εντολή σταματήματος: σταμάτα την από το κουμπί της.",
                                "The camera did not take the stop command: stop it with its own button."));
                        problemUntil = DateTime.UtcNow.AddSeconds(90);
                    }
                }

                log.WarnFormat("The GoPro {0:X12} did not take the command to {1} recording.", Address, record ? "start" : "stop");
                CheckRecordingChange();
                return;
            }

            // The camera says when it starts or stops (pushed); asked again in case the change is not pushed.
            bool confirmed = false;
            for (int i = 0; i < 3 && !confirmed; i++)
            {
                await Task.Delay(1200).ConfigureAwait(false);
                lock (locker)
                    confirmed = encodingKnown && encoding == record;

                if (confirmed)
                    break;

                await QueryStatusesAsync(new byte[] { GoProIds.StatusEncoding, GoProIds.StatusEncodingSeconds }).ConfigureAwait(false);
                lock (locker)
                    confirmed = encodingKnown && encoding == record;

                CheckRecordingChange();
            }

            if (confirmed)
                return;

            // Taken but not done: the screen says why once the time of the command is over.
            await Task.Delay(TimeSpan.FromSeconds(4.5)).ConfigureAwait(false);
            await QueryStatusesAsync(MainStatuses).ConfigureAwait(false);
            lock (locker)
            {
                confirmed = encodingKnown && encoding == record;
                if (!confirmed && desiredRecording == record)
                {
                    pendingUntil = DateTime.MinValue;
                    string reason = record ? BlockedReasonUnlocked() : null;
                    problem = reason ?? (record ?
                        GoProText.T("Η κάμερα δέχτηκε την εντολή αλλά δεν ξεκίνησε να γράφει: δες την οθόνη της.",
                            "The camera took the command but did not start recording: look at its screen.") :
                        GoProText.T("Η κάμερα δέχτηκε την εντολή αλλά γράφει ακόμη: σταμάτα την από το κουμπί της.",
                            "The camera took the command but is still recording: stop it with its own button."));
                    problemUntil = DateTime.UtcNow.AddSeconds(90);
                }
            }

            if (!confirmed)
                log.WarnFormat("The GoPro {0:X12} took the command to {1} recording but did not do it.", Address, record ? "start" : "stop");

            CheckRecordingChange();
        }

        /// <summary>Why the camera cannot record, from what it said (null when nothing it said explains it). Under the lock.</summary>
        private string BlockedReasonUnlocked()
        {
            switch (primaryStorage)
            {
                case 1:
                    return GoProText.T("Δεν γράφει: η κάρτα είναι γεμάτη.", "Not recording: the card is full.");
                case 2:
                    return GoProText.T("Δεν γράφει: η κάμερα δεν έχει κάρτα.", "Not recording: the camera has no card.");
                case 3:
                    return GoProText.T("Δεν γράφει: η κάρτα έχει πρόβλημα (φορμάρισμα από το μενού της κάμερας).", "Not recording: the card has a problem (format it from the menu of the camera).");
                case 4:
                    return GoProText.T("Δεν γράφει: η κάρτα είναι απασχολημένη (σύνδεση USB ή μεταφορά αρχείων;).", "Not recording: the card is busy (USB connection or file transfer?).");
            }

            if (hot)
                return GoProText.T("Δεν γράφει: η κάμερα ζεστάθηκε πολύ (σκιά, άνοιξε το πορτάκι της μπαταρίας).", "Not recording: the camera is too hot (shade, open the battery door).");

            if (batteryPercent >= 0 && batteryPercent <= 2)
                return GoProText.T("Δεν γράφει: η μπαταρία τελείωσε.", "Not recording: the battery is empty.");

            if (busy)
                return GoProText.T("Δεν γράφει: η κάμερα είναι απασχολημένη (μενού ή αναπαραγωγή στην οθόνη της).", "Not recording: the camera is busy (a menu or playback on its screen).");

            return null;
        }

        private bool EffectiveUnlocked()
        {
            if (pendingUntil > DateTime.UtcNow)
                return desiredRecording;

            return encoding;
        }

        /// <summary>Raises RecordingChanged if what the screens should show changed (also when a command expired).</summary>
        public void CheckRecordingChange()
        {
            bool changed;
            lock (locker)
            {
                bool now = EffectiveUnlocked();
                changed = now != reportedRecording;
                reportedRecording = now;
            }

            if (changed)
            {
                EventHandler handler = RecordingChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
        }
        #endregion

        #region Sets of settings
        /// <summary>
        /// Puts a set of settings in the camera (the video mode first): each setting with the values its model takes, twice when one
        /// depends on another (a resolution that needs another frame rate first). The screen says how it went. Not while it records.
        /// </summary>
        public async Task ApplyPresetAsync(GoProPreset preset)
        {
            if (preset == null)
                return;

            if (Interlocked.Exchange(ref applying, 1) == 1)
                return;

            try
            {
                string name = preset.Name;
                if (!IsConnected)
                {
                    SetNotice(name + ": " + GoProText.T("η κάμερα δεν είναι συνδεδεμένη ακόμη.", "the camera is not connected yet."), true, 60);
                    return;
                }

                bool recording;
                lock (locker)
                    recording = encoding || (pendingUntil > DateTime.UtcNow && desiredRecording);

                if (recording)
                {
                    SetNotice(name + ": " + GoProText.T("σταμάτα πρώτα την εγγραφή.", "stop the recording first."), true, 60);
                    return;
                }

                SetNotice(name + "…", false, 30);
                log.DebugFormat("Putting \"{0}\" in the GoPro {1:X12}.", preset.Id, Address);

                // The video mode (not photos or time lapses).
                byte[] group = new byte[] { GoProIds.CmdLoadPresetGroup, 0x02, (byte)(GoProIds.PresetGroupVideo >> 8), (byte)(GoProIds.PresetGroupVideo & 0xFF) };
                byte[] groupResponse = await RequestAsync(Get(c => c.command), GoProIds.CommandResponse, GoProIds.CmdLoadPresetGroup, group, 2500).ConfigureAwait(false);
                if (groupResponse == null || groupResponse.Length < 2 || groupResponse[1] != 0)
                    log.DebugFormat("The GoPro {0:X12} did not load the video presets: {1}.", Address, groupResponse != null && groupResponse.Length >= 2 ? groupResponse[1].ToString() : "no answer");

                List<GoProSettingValue> left = preset.Settings.ToList();
                for (int pass = 0; pass < 2 && left.Count > 0; pass++)
                {
                    List<GoProSettingValue> failed = new List<GoProSettingValue>();
                    foreach (GoProSettingValue setting in left)
                    {
                        bool ok = false;
                        foreach (int value in setting.Values)
                        {
                            byte[] message = new byte[] { setting.Setting, 0x01, (byte)value };
                            byte[] response = await RequestAsync(Get(c => c.settings), GoProIds.SettingsResponse, setting.Setting, message, 2500).ConfigureAwait(false);
                            if (response != null && response.Length >= 2 && response[1] == 0)
                            {
                                ok = true;
                                break;
                            }

                            log.DebugFormat("The GoPro {0:X12} did not take {1} for the setting {2}: {3}.", Address, value, setting.Setting,
                                response != null && response.Length >= 2 ? response[1].ToString() : "no answer");
                        }

                        if (!ok && !setting.Optional)
                            failed.Add(setting);
                    }

                    left = failed;
                }

                // The settings as the camera has them now (the tile of the settings shows them).
                byte[] query = new byte[] { GoProIds.QueryGetSettings, GoProIds.SettingResolution, GoProIds.SettingFps, GoProIds.SettingLens, GoProIds.SettingAntiFlicker, GoProIds.SettingHypersmooth };
                await RequestAsync(Get(c => c.query), GoProIds.QueryResponse, GoProIds.QueryGetSettings, query, 2500).ConfigureAwait(false);
                await Task.Delay(300).ConfigureAwait(false);

                // A camera can take each value and still change one by itself (a resolution without that frame rate).
                GoProState now = Snapshot(null);
                if (left.Count == 0 && now.Resolution >= 0 && !preset.Matches(now))
                {
                    SetNotice(name + ": " + GoProText.T("η κάμερα έμεινε σε ", "the camera stayed at ") + GoProPresets.Describe(now) +
                        GoProText.T(" αντί για ", " instead of ") + preset.Description + GoProText.T(". Ξαναπάτα το κουμπί ή άλλαξέ το στην κάμερα.", ". Press the button again or change it on the camera."), true, 180);
                    log.WarnFormat("\"{0}\" taken by the GoPro {1:X12} but it stayed at {2}.", preset.Id, Address, GoProPresets.Describe(now));
                }
                else if (left.Count == 0)
                {
                    SetNotice(name + " ✓  " + preset.Description, false, 90);
                    log.DebugFormat("\"{0}\" is in the GoPro {1:X12}.", preset.Id, Address);
                }
                else
                {
                    string missing = string.Join(", ", left.Select(s => s.Label));
                    SetNotice(name + ": " + GoProText.T("η κάμερα δεν δέχτηκε ", "the camera did not take ") + missing +
                        GoProText.T(" (άλλαξέ τα στην κάμερα· αν είναι σε μενού, βγες από αυτό).", " (change them on the camera; if it is in a menu, leave it)."), true, 180);
                    log.WarnFormat("\"{0}\" is not all in the GoPro {1:X12}: {2}.", preset.Id, Address, missing);
                }
            }
            catch (Exception e)
            {
                log.Error("Error while putting a set of settings in the GoPro.", e);
                SetNotice(preset.Name + ": " + GoProText.T("δεν ολοκληρώθηκε, ξαναδοκίμασε.", "did not complete, try again."), true, 90);
            }
            finally
            {
                Interlocked.Exchange(ref applying, 0);
            }
        }

        private void SetNotice(string text, bool error, int seconds)
        {
            lock (locker)
            {
                notice = text;
                noticeError = error;
                noticeUntil = DateTime.UtcNow.AddSeconds(seconds);
            }
        }
        #endregion

        #region Wi-Fi
        /// <summary>The access point of the camera on or off (off saves battery and heat; on for the live image).</summary>
        public async Task<bool> SetWifiAsync(bool on)
        {
            byte[] response = await RequestAsync(Get(c => c.command), GoProIds.CommandResponse, GoProIds.CmdSetWifiAp, new byte[] { GoProIds.CmdSetWifiAp, 0x01, (byte)(on ? 1 : 0) }, 2500).ConfigureAwait(false);
            return response != null && response.Length >= 2 && response[1] == 0;
        }

        /// <summary>The name and password of the Wi-Fi of the camera (null if they could not be read).</summary>
        public async Task<string[]> ReadWifiCredentialsAsync()
        {
            GattCharacteristic ssid = Get(c => c.wifiSsid);
            GattCharacteristic password = Get(c => c.wifiPassword);
            if (ssid == null || password == null)
                return null;

            GattReadResult name = await ssid.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask().ConfigureAwait(false);
            GattReadResult key = await password.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask().ConfigureAwait(false);
            if (name.Status != GattCommunicationStatus.Success || key.Status != GattCommunicationStatus.Success)
                return null;

            string n = GoProPackets.Text(GoProBuffers.ToBytes(name.Value));
            string k = GoProPackets.Text(GoProBuffers.ToBytes(key.Value));
            if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(k))
                return null;

            return new string[] { n, k };
        }

        /// <summary>The live image wants the Wi-Fi of the camera on (it is not turned off at the next connection).</summary>
        public void SetPreviewWanted(bool wanted)
        {
            lock (locker)
                previewWanted = wanted;
        }
        #endregion

        #region State
        private void SetLink(GoProLink value, string hint)
        {
            lock (locker)
            {
                if (link != value)
                {
                    link = value;
                    linkSince = DateTime.UtcNow;
                }

                problem = hint;
                problemUntil = DateTime.MinValue;
            }
        }

        private static string NotAnsweringHint()
        {
            return GoProText.T("Δεν απαντά. Άναψέ την με τις ασύρματες συνδέσεις ανοιχτές. Αν είναι συνδεδεμένη στο Quik του κινητού, κλείσε το Quik: δέχεται μία σύνδεση τη φορά.",
                "It does not answer. Turn it on with the wireless connections on. If it is connected to Quik on a phone, close Quik: it takes one connection at a time.");
        }

        private static string PairingHint()
        {
            return GoProText.T("Πρώτη φορά σε αυτόν τον υπολογιστή: βάλε την κάμερα σε σύζευξη (Συνδέσεις › Σύνδεση συσκευής › GoPro Quik). Συνδέεται μόνη της.",
                "First time on this computer: put the camera in pairing mode (Connections › Connect Device › GoPro Quik). It connects by itself.");
        }

        /// <summary>A copy of the state for the screens (the alias is the name given by the user).</summary>
        public GoProState Snapshot(string alias)
        {
            GoProSighting seen = GoProScanner.Find(Address);
            GoProState s = new GoProState();
            lock (locker)
            {
                s.Alias = alias;
                s.ModelName = modelName;
                s.Firmware = firmware;
                s.Link = link;
                s.LinkSeconds = (DateTime.UtcNow - linkSince).TotalSeconds;
                s.Problem = problemUntil == DateTime.MinValue || problemUntil > DateTime.UtcNow ? problem : null;
                s.Recording = EffectiveUnlocked();
                s.CommandPending = pendingUntil > DateTime.UtcNow && (!encodingKnown || desiredRecording != encoding);
                s.RecordingSeconds = encoding && encodingSince != DateTime.MinValue ? (long)(DateTime.Now - encodingSince).TotalSeconds : 0;
                s.LastStartOffset = lastStartOffset;
                s.LastStartAt = lastStartAt;
                s.BatteryPercent = batteryPercent;
                s.BatteryBars = batteryBars;
                s.Hot = hot;
                s.Cold = cold;
                s.Busy = busy;
                s.PrimaryStorage = primaryStorage;
                s.RemainingVideoSeconds = remainingVideo;
                s.SdRemainingKB = sdRemainingKB;
                s.SdSpeedError = sdSpeedError;
                s.WifiOn = apOn;
                s.Resolution = resolution;
                s.Fps = fps;
                s.Lens = lens;
                s.AntiFlicker = antiFlicker;
                s.Hypersmooth = hypersmooth;
                s.ClockSetAt = clockSetAt;
                s.PreviewOn = previewWanted;
                if (noticeUntil > DateTime.UtcNow)
                {
                    s.Notice = notice;
                    s.NoticeError = noticeError;
                }
            }

            if (seen != null)
            {
                s.BleName = seen.Name;
                s.Rssi = seen.Rssi;
                if (string.IsNullOrEmpty(s.ModelName))
                    s.ModelName = GoProIds.ModelName(seen.ModelId);
            }

            return s;
        }
        #endregion
    }
}
