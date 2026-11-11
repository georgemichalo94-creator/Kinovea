#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Kinovea.Camera.GoPro
{
    /// <summary>
    /// (Swim 2.20.0) The settings of a GoPro in Kinovea: its name in the screens (K1, K2…), what is known of it, and a new pairing
    /// when the old one does not work any more.
    /// </summary>
    internal sealed class FormGoProCamera : Form
    {
        private readonly TextBox txtAlias = new TextBox();
        private readonly Label lblInfo = new Label();
        private readonly Button btnPair = new Button();
        private readonly string original;
        private readonly ulong address;

        public string Alias
        {
            get { return txtAlias.Text.Trim(); }
        }

        public bool AliasChanged
        {
            get { return Alias.Length > 0 && Alias != original; }
        }

        public FormGoProCamera(CameraSummary summary)
        {
            original = summary.Alias;
            SpecificInfo info = summary.Specific as SpecificInfo;
            address = info != null ? info.Address : 0;

            Text = "GoPro · " + summary.Alias;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(480, 300);

            Label lblAlias = new Label
            {
                Text = GoProText.T("Όνομα της κάμερας στις οθόνες (π.χ. K1, K2, K3):", "Name of the camera in the screens (e.g. K1, K2, K3):"),
                Location = new Point(16, 16),
                AutoSize = true
            };

            txtAlias.Location = new Point(18, 40);
            txtAlias.Width = 444;
            txtAlias.Text = summary.Alias;

            lblInfo.Location = new Point(16, 78);
            lblInfo.Size = new Size(448, 160);
            lblInfo.Text = Describe(summary, info);

            btnPair.Text = GoProText.T("Νέα σύζευξη…", "New pairing…");
            btnPair.Location = new Point(16, 256);
            btnPair.Size = new Size(150, 28);
            btnPair.Enabled = address != 0;
            btnPair.Click += BtnPair_Click;

            Button btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(292, 256), Size = new Size(84, 28) };
            Button btnCancel = new Button { Text = GoProText.T("Άκυρο", "Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(380, 256), Size = new Size(84, 28) };
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { lblAlias, txtAlias, lblInfo, btnPair, btnOk, btnCancel });
        }

        private static string Describe(CameraSummary summary, SpecificInfo info)
        {
            List<string> lines = new List<string>();
            if (info != null)
            {
                lines.Add(GoProText.T("Μοντέλο: ", "Model: ") + (GoProIds.ModelName(info.ModelId) ?? "GoPro"));
                if (!string.IsNullOrEmpty(info.BleName))
                    lines.Add(GoProText.T("Όνομα Bluetooth: ", "Bluetooth name: ") + info.BleName);

                lines.Add(GoProText.T("Διεύθυνση: ", "Address: ") + info.Address.ToString("X12", CultureInfo.InvariantCulture));
                GoProState state = ActiveState(summary.Alias, info.Address);
                if (state != null)
                {
                    if (!string.IsNullOrEmpty(state.Firmware))
                        lines.Add(GoProText.T("Λογισμικό κάμερας: ", "Camera firmware: ") + state.Firmware);

                    if (state.BatteryPercent >= 0)
                        lines.Add(GoProText.T("Μπαταρία: ", "Battery: ") + state.BatteryPercent.ToString(CultureInfo.InvariantCulture) + " %");
                }
            }

            lines.Add("");
            lines.Add(GoProText.T("Το βίντεο γράφεται στην κάρτα της κάμερας. Η «Νέα σύζευξη» ξεχνά την παλιά: μετά βάλε την κάμερα σε σύζευξη (Συνδέσεις › Σύνδεση συσκευής › GoPro Quik).",
                "The video is written on the card of the camera. \"New pairing\" forgets the old one: then put the camera in pairing mode (Connections › Connect Device › GoPro Quik)."));
            return string.Join("\n", lines);
        }

        private static GoProState ActiveState(string alias, ulong address)
        {
            try
            {
                GoProDevice device = GoProDevice.FindActive(address);
                return device != null ? device.Snapshot(alias) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async void BtnPair_Click(object sender, EventArgs e)
        {
            btnPair.Enabled = false;
            string result;
            try
            {
                result = await GoProPairing.ForgetAsync(address);
            }
            catch (Exception ex)
            {
                result = ex.Message;
            }

            if (!IsDisposed)
            {
                btnPair.Enabled = true;
                MessageBox.Show(this, result, "GoPro", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }

    /// <summary>(Swim 2.20.0) Forgets the pairing of Windows with a camera (it pairs again in pairing mode).</summary>
    internal static class GoProPairing
    {
        public static async Task<string> ForgetAsync(ulong address)
        {
            BluetoothLEDevice device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask().ConfigureAwait(false);
            if (device == null)
                return GoProText.T("Η κάμερα δεν βρέθηκε.", "The camera was not found.");

            using (device)
            {
                if (!device.DeviceInformation.Pairing.IsPaired)
                    return GoProText.T("Δεν ήταν σε σύζευξη με αυτόν τον υπολογιστή. Βάλε την κάμερα σε σύζευξη: συνδέεται μόνη της.",
                        "It was not paired with this computer. Put the camera in pairing mode: it connects by itself.");

                DeviceUnpairingResult result = await device.DeviceInformation.Pairing.UnpairAsync().AsTask().ConfigureAwait(false);
                if (result.Status == DeviceUnpairingResultStatus.Unpaired || result.Status == DeviceUnpairingResultStatus.AlreadyUnpaired)
                    return GoProText.T("Η παλιά σύζευξη ξεχάστηκε. Βάλε την κάμερα σε σύζευξη: συνδέεται μόνη της.",
                        "The old pairing is forgotten. Put the camera in pairing mode: it connects by itself.");

                return result.Status.ToString();
            }
        }
    }
}
