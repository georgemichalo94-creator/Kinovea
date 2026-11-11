#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;

namespace Kinovea.Camera.GoPro
{
    /// <summary>
    /// (Swim 2.20.0) The Wi-Fi of the computer for the live image of a GoPro: joins the network of the camera (a free adapter first, so
    /// that the computer keeps its internet when it has a second one) and goes back to the network it was on. Native Wi-Fi API of
    /// Windows (wlanapi.dll); the profile of the camera is kept for the user only.
    /// </summary>
    internal sealed class GoProWifi : IDisposable
    {
        #region Native
        private const uint ClientVersion = 2;
        private const uint ProfileUser = 0x00000002;
        private const int OpcodeCurrentConnection = 7;
        private const int StateConnected = 1;
        private const int ModeProfile = 0;
        private const int BssInfrastructure = 1;
        private const int InterfaceInfoSize = 532;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WlanConnectionParameters
        {
            public int ConnectionMode;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Profile;
            public IntPtr Ssid;
            public IntPtr DesiredBssidList;
            public int BssType;
            public uint Flags;
        }

        [DllImport("wlanapi.dll")]
        private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

        [DllImport("wlanapi.dll")]
        private static extern void WlanFreeMemory(IntPtr memory);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        private static extern uint WlanSetProfile(IntPtr clientHandle, ref Guid interfaceGuid, uint flags, string profileXml, string allUserProfileSecurity,
            [MarshalAs(UnmanagedType.Bool)] bool overwrite, IntPtr reserved, out uint reasonCode);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanConnect(IntPtr clientHandle, ref Guid interfaceGuid, ref WlanConnectionParameters parameters, IntPtr reserved);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanDisconnect(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr reserved);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opCode, IntPtr reserved, out uint dataSize, out IntPtr data, out int opcodeValueType);
        #endregion

        private sealed class Interface
        {
            public Guid Id;
            public string Description;
            public int State;
        }

        private IntPtr handle = IntPtr.Zero;
        private Guid used;
        private string previousProfile;
        private bool joined;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>The adapter that joined the camera (for the messages).</summary>
        public string AdapterName { get; private set; }

        /// <summary>The adapter that joined the camera was the only one (the computer has no internet meanwhile).</summary>
        public bool SharedAdapter { get; private set; }

        /// <summary>
        /// Joins the network of the camera and waits until the computer is on it (up to `timeoutMs`). Returns null when it worked, else
        /// what went wrong.
        /// </summary>
        public string Join(string ssid, string password, int timeoutMs)
        {
            uint negotiated;
            uint error = WlanOpenHandle(ClientVersion, IntPtr.Zero, out negotiated, out handle);
            if (error != 0)
                return GoProText.T("Το Wi-Fi των Windows δεν απαντά (κωδικός " + error + ").", "The Wi-Fi of Windows does not answer (code " + error + ").");

            List<Interface> interfaces = Interfaces();
            if (interfaces.Count == 0)
                return GoProText.T("Δεν βρέθηκε Wi-Fi στον υπολογιστή.", "No Wi-Fi was found on this computer.");

            // A free adapter first (a second Wi-Fi keeps the internet of the computer), else the one of the computer.
            Interface chosen = interfaces.Find(i => i.State != StateConnected) ?? interfaces[0];
            used = chosen.Id;
            AdapterName = chosen.Description;
            SharedAdapter = chosen.State == StateConnected;
            previousProfile = CurrentProfile(used);
            if (previousProfile == ssid)
                previousProfile = null;

            uint reason;
            error = WlanSetProfile(handle, ref used, ProfileUser, ProfileXml(ssid, password), null, true, IntPtr.Zero, out reason);
            if (error != 0)
                return GoProText.T("Το δίκτυο της κάμερας δεν μπήκε στα Windows (κωδικός " + error + "/" + reason + ").",
                    "The network of the camera could not be added to Windows (code " + error + "/" + reason + ").");

            WlanConnectionParameters parameters = new WlanConnectionParameters
            {
                ConnectionMode = ModeProfile,
                Profile = ssid,
                Ssid = IntPtr.Zero,
                DesiredBssidList = IntPtr.Zero,
                BssType = BssInfrastructure,
                Flags = 0
            };

            error = WlanConnect(handle, ref used, ref parameters, IntPtr.Zero);
            if (error != 0)
                return GoProText.T("Ο υπολογιστής δεν συνδέθηκε στο Wi-Fi της κάμερας (κωδικός " + error + ").",
                    "The computer could not join the Wi-Fi of the camera (code " + error + ").");

            joined = true;
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (CurrentProfile(used) == ssid)
                    return null;

                Thread.Sleep(300);
            }

            return GoProText.T("Ο υπολογιστής δεν μπήκε στο Wi-Fi της κάμερας σε " + (timeoutMs / 1000) + " s: δες αν το Wi-Fi του υπολογιστή είναι ανοιχτό.",
                "The computer did not join the Wi-Fi of the camera within " + (timeoutMs / 1000) + " s: check that the Wi-Fi of the computer is on.");
        }

        /// <summary>Back to the network of before (or off the network of the camera).</summary>
        public void Leave()
        {
            if (handle == IntPtr.Zero || !joined)
                return;

            try
            {
                if (!string.IsNullOrEmpty(previousProfile))
                {
                    WlanConnectionParameters parameters = new WlanConnectionParameters
                    {
                        ConnectionMode = ModeProfile,
                        Profile = previousProfile,
                        Ssid = IntPtr.Zero,
                        DesiredBssidList = IntPtr.Zero,
                        BssType = BssInfrastructure,
                        Flags = 0
                    };

                    uint error = WlanConnect(handle, ref used, ref parameters, IntPtr.Zero);
                    if (error == 0)
                        return;

                    log.ErrorFormat("Could not go back to the previous Wi-Fi network: {0}.", error);
                }

                WlanDisconnect(handle, ref used, IntPtr.Zero);
            }
            catch (Exception e)
            {
                log.Error("Error while leaving the Wi-Fi of the camera.", e);
            }
            finally
            {
                joined = false;
            }
        }

        public void Dispose()
        {
            Leave();
            if (handle != IntPtr.Zero)
            {
                WlanCloseHandle(handle, IntPtr.Zero);
                handle = IntPtr.Zero;
            }
        }

        private List<Interface> Interfaces()
        {
            List<Interface> result = new List<Interface>();
            IntPtr list;
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0 || list == IntPtr.Zero)
                return result;

            try
            {
                int count = Marshal.ReadInt32(list, 0);
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = IntPtr.Add(list, 8 + i * InterfaceInfoSize);
                    Interface info = new Interface();
                    info.Id = (Guid)Marshal.PtrToStructure(item, typeof(Guid));
                    info.Description = Marshal.PtrToStringUni(IntPtr.Add(item, 16));
                    info.State = Marshal.ReadInt32(item, 16 + 512);
                    result.Add(info);
                }
            }
            finally
            {
                WlanFreeMemory(list);
            }

            return result;
        }

        /// <summary>The profile (network) an adapter is connected to, or null.</summary>
        private string CurrentProfile(Guid id)
        {
            uint size;
            IntPtr data;
            int type;
            if (WlanQueryInterface(handle, ref id, OpcodeCurrentConnection, IntPtr.Zero, out size, out data, out type) != 0 || data == IntPtr.Zero)
                return null;

            try
            {
                int state = Marshal.ReadInt32(data, 0);
                if (state != StateConnected)
                    return null;

                return Marshal.PtrToStringUni(IntPtr.Add(data, 8));
            }
            finally
            {
                WlanFreeMemory(data);
            }
        }

        private static string ProfileXml(string ssid, string password)
        {
            string name = SecurityElement.Escape(ssid);
            string key = SecurityElement.Escape(password);
            return "<?xml version=\"1.0\"?>" +
                "<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">" +
                "<name>" + name + "</name>" +
                "<SSIDConfig><SSID><name>" + name + "</name></SSID></SSIDConfig>" +
                "<connectionType>ESS</connectionType>" +
                "<connectionMode>manual</connectionMode>" +
                "<MSM><security>" +
                "<authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>" +
                "<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>" + key + "</keyMaterial></sharedKey>" +
                "</security></MSM>" +
                "</WLANProfile>";
        }
    }
}
