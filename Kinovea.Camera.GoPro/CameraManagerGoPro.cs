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
using System.Linq;
using System.Windows.Forms;
using Kinovea.Pipeline;
using Kinovea.Services;

namespace Kinovea.Camera.GoPro
{
    /// <summary>
    /// (Swim 2.20.0) The GoPro cameras (HERO9 Black and later) controlled by Bluetooth: found by their advertisements, each one in a
    /// capture screen that shows its state and starts and stops its recording on its own card.
    /// </summary>
    public class CameraManagerGoPro : CameraManager
    {
        /// <summary>The type of the GoPro cameras in the camera history (also known by the screens).</summary>
        public const string TypeId = "5B2F7C1E-3A94-4D6B-9E0F-0C7A1D2E8B64";

        #region Properties
        public override bool Enabled
        {
            get { return true; }
        }

        public override string CameraType
        {
            get { return TypeId; }
        }

        public override string CameraTypeFriendlyName
        {
            get { return "GoPro"; }
        }

        public override bool HasConnectionWizard
        {
            get { return false; }
        }
        #endregion

        #region Members
        private readonly Dictionary<string, CameraSummary> cache = new Dictionary<string, CameraSummary>();
        private readonly Bitmap defaultIcon;
        private bool scannerFailed;
        private static readonly TimeSpan recent = TimeSpan.FromSeconds(120);
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        #endregion

        public CameraManagerGoPro()
        {
            defaultIcon = IconLibrary.GetIcon("camcorder");
        }

        public override bool SanityCheck()
        {
            // The Bluetooth of Windows 10 or later (WinRT) is needed; it is looked at when the cameras are looked for.
            return true;
        }

        public override List<CameraSummary> DiscoverCameras(IEnumerable<CameraBlurb> blurbs)
        {
            List<CameraSummary> summaries = new List<CameraSummary>();

            // Only the Open GoPro cameras (heard with the service or the data of GoPro): a device that only has a name like "GoPro Cam"
            // cannot be controlled and would take a screen for nothing.
            List<GoProSighting> heard = Listen().Where(x => x.Confirmed).ToList();
            HashSet<ulong> listed = new HashSet<ulong>();
            HashSet<ulong> active = new HashSet<ulong>(ActiveAddresses());
            Dictionary<ulong, GoProSighting> byAddress = heard.ToDictionary(x => x.Address);

            // The cameras known from before (with the name given by the user), when they are heard or held by a screen.
            if (blurbs != null)
            {
                foreach (CameraBlurb blurb in blurbs)
                {
                    if (blurb.CameraType != CameraType)
                        continue;

                    SpecificInfo info = SpecificInfo.FromXml(blurb.Specific);
                    if (info == null || info.Address == 0 || listed.Contains(info.Address))
                        continue;

                    if (!byAddress.ContainsKey(info.Address) && !active.Contains(info.Address))
                        continue;

                    GoProSighting seen;
                    if (byAddress.TryGetValue(info.Address, out seen))
                    {
                        if (seen.ModelId >= 0)
                            info.ModelId = seen.ModelId;

                        if (!string.IsNullOrEmpty(seen.Name))
                            info.BleName = seen.Name;
                    }

                    summaries.Add(FromBlurb(blurb, info));
                    listed.Add(info.Address);
                }
            }

            // The cameras heard now and not known yet.
            foreach (GoProSighting s in heard)
            {
                if (listed.Contains(s.Address))
                    continue;

                SpecificInfo info = new SpecificInfo { Address = s.Address, ModelId = s.ModelId, BleName = s.Name };
                summaries.Add(new CameraSummary(s.DisplayName, ModelName(info), Identifier(s.Address), defaultIcon, Rectangle.Empty,
                    CaptureAspectRatio.Auto, ImageRotation.Rotate0, false, info, this));
                listed.Add(s.Address);
            }

            lock (cache)
            {
                cache.Clear();
                foreach (CameraSummary summary in summaries)
                    cache[summary.Identifier] = summary;
            }

            return summaries;
        }

        /// <summary>Listen to the advertisements; the cameras heard in the last two minutes.</summary>
        private List<GoProSighting> Listen()
        {
            try
            {
                return ListenWinRT();
            }
            catch (Exception e)
            {
                // Windows without the Bluetooth of WinRT: the module stays silent after the first error.
                if (!scannerFailed)
                    log.Error("The GoPro cameras cannot be looked for by Bluetooth on this computer.", e);

                scannerFailed = true;
                return new List<GoProSighting>();
            }
        }

        private static List<GoProSighting> ListenWinRT()
        {
            GoProScanner.Want();
            return GoProScanner.Recent(recent);
        }

        private static IEnumerable<ulong> ActiveAddresses()
        {
            try
            {
                return GoProDevice.Active().Select(d => d.Address).ToList();
            }
            catch (Exception)
            {
                return new List<ulong>();
            }
        }

        private CameraSummary FromBlurb(CameraBlurb blurb, SpecificInfo info)
        {
            Bitmap icon = blurb.Icon ?? defaultIcon;
            CaptureAspectRatio aspectRatio = CaptureAspectRatio.Auto;
            if (!string.IsNullOrEmpty(blurb.AspectRatio))
                Enum.TryParse(blurb.AspectRatio, out aspectRatio);

            ImageRotation rotation = ImageRotation.Rotate0;
            if (!string.IsNullOrEmpty(blurb.Rotation))
                Enum.TryParse(blurb.Rotation, out rotation);

            return new CameraSummary(blurb.Alias, ModelName(info), Identifier(info.Address), icon, blurb.DisplayRectangle, aspectRatio,
                rotation, blurb.Mirror, info, this);
        }

        private static string Identifier(ulong address)
        {
            return "GoPro-" + address.ToString("X12", CultureInfo.InvariantCulture);
        }

        private static string ModelName(SpecificInfo info)
        {
            return GoProIds.ModelName(info.ModelId) ?? "GoPro";
        }

        public override void ForgetCamera(CameraSummary summary)
        {
            lock (cache)
                cache.Remove(summary.Identifier);
        }

        public override CameraSummary GetCameraSummary(string alias)
        {
            lock (cache)
                return cache.Values.FirstOrDefault(s => s.Alias == alias);
        }

        public override void StartThumbnail(CameraSummary summary)
        {
            // The image of the camera is drawn at once: its name and what it said last.
            Bitmap thumbnail = null;
            bool error = false;
            try
            {
                SpecificInfo info = summary.Specific as SpecificInfo;
                GoProState state = new GoProState { Alias = summary.Alias, Link = GoProLink.Searching };
                if (info != null)
                {
                    state.ModelName = GoProIds.ModelName(info.ModelId);
                    state.BleName = info.BleName;
                    FillFromScanner(state, info.Address);
                }

                thumbnail = GoProCaptureSource.Thumbnail(state);
            }
            catch (Exception e)
            {
                log.Error("Error while drawing the image of a GoPro for the list of cameras.", e);
                error = true;
            }

            ImageDescriptor descriptor = thumbnail != null ?
                new ImageDescriptor(Kinovea.Services.ImageFormat.RGB24, thumbnail.Width, thumbnail.Height, true, ImageFormatHelper.ComputeBufferSize(thumbnail.Width, thumbnail.Height, Kinovea.Services.ImageFormat.RGB24)) :
                ImageDescriptor.Invalid;

            Bitmap image = thumbnail;
            Invoke(() => OnCameraThumbnailProduced(new CameraThumbnailProducedEventArgs(summary, image, descriptor, error, false)));
        }

        private static void FillFromScanner(GoProState state, ulong address)
        {
            try
            {
                FillFromScannerWinRT(state, address);
            }
            catch (Exception)
            {
                // Without the Bluetooth of WinRT the image shows only the name.
            }
        }

        private static void FillFromScannerWinRT(GoProState state, ulong address)
        {
            GoProSighting seen = GoProScanner.Find(address);
            if (seen == null)
                return;

            state.Rssi = seen.Rssi;
            if (seen.Pairing)
            {
                state.Link = GoProLink.NeedsPairing;
                state.Problem = GoProText.T("Σε σύζευξη: βάλ' την σε μια οθόνη εγγραφής και συνδέεται.", "In pairing mode: put it in a capture screen and it connects.");
            }
        }

        public override void StopAllThumbnails()
        {
        }

        public override CameraBlurb BlurbFromSummary(CameraSummary summary)
        {
            SpecificInfo info = summary.Specific as SpecificInfo;
            string specific = info != null ? info.ToXml() : null;
            return new CameraBlurb(CameraType, summary.Identifier, summary.Alias, summary.Icon, summary.DisplayRectangle,
                summary.AspectRatio.ToString(), summary.Rotation.ToString(), summary.Mirror, specific);
        }

        public override ICaptureSource CreateCaptureSource(CameraSummary summary)
        {
            SpecificInfo info = summary.Specific as SpecificInfo;
            if (info == null || info.Address == 0)
                return null;

            return new GoProCaptureSource(summary, info.Address);
        }

        public override bool Configure(CameraSummary summary)
        {
            using (FormGoProCamera form = new FormGoProCamera(summary))
            {
                if (form.ShowDialog() == DialogResult.OK && form.AliasChanged)
                {
                    summary.UpdateAlias(form.Alias, summary.Icon);
                    CameraTypeManager.UpdatedCameraSummary(summary);
                }
            }

            return false;
        }

        public override string GetSummaryAsText(CameraSummary summary)
        {
            SpecificInfo info = summary.Specific as SpecificInfo;
            string model = info != null ? (GoProIds.ModelName(info.ModelId) ?? "GoPro") : "GoPro";
            return string.Format("{0} · {1} · {2}", summary.Alias, model, GoProText.T("εγγραφή στην κάρτα της κάμερας", "recording on the card of the camera"));
        }

        public override Control GetConnectionWizard()
        {
            return null;
        }
    }
}
