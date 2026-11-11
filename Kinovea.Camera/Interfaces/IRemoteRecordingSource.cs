#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;

namespace Kinovea.Camera
{
    /// <summary>
    /// (Swim 2.20.0) A camera that records on its own card, such as a GoPro controlled by Bluetooth: the record button of its capture
    /// screen starts and stops the camera itself and nothing is written on the computer. The images of its screen only show the state
    /// of the camera.
    /// </summary>
    public interface IRemoteRecordingSource
    {
        /// <summary>
        /// The camera records (what it says itself; while a command is on its way, what was asked).
        /// </summary>
        bool RemoteRecording { get; }

        /// <summary>
        /// Start or stop the recording on the camera. Returns at once; the change comes with RemoteRecordingChanged.
        /// The moment of the command is shared by all the cameras started together, to measure the start of each one from it.
        /// </summary>
        void SetRemoteRecording(bool record, DateTime commandTime);

        /// <summary>
        /// The camera started or stopped recording, from a command or from its own button, or a command failed.
        /// Raised on any thread.
        /// </summary>
        event EventHandler RemoteRecordingChanged;
    }

    /// <summary>
    /// (Swim 2.20.0) A camera whose live image can be turned on and off from its screen (a GoPro by its own Wi-Fi, one camera at a
    /// time); without it the screen shows the state of the camera.
    /// </summary>
    public interface ILivePreviewSource
    {
        /// <summary>The live image is on (or starting).</summary>
        bool LivePreview { get; }

        /// <summary>Turns the live image on or off (another camera's live image stops first). Returns at once.</summary>
        void SetLivePreview(bool on);
    }

    /// <summary>
    /// (Swim 2.20.0) A camera with ready-made sets of settings (a GoPro: for races, for technique analysis), applied to it or to all
    /// the cameras of its kind connected now. Each screen says how it went.
    /// </summary>
    public interface ICameraPresetSource
    {
        /// <summary>The sets: an id, a short name for a button and what the set puts (in the language of the interface).</summary>
        IList<CameraPreset> CameraPresets { get; }

        /// <summary>The number of cameras of this kind connected now (the ones "all" reaches).</summary>
        int ConnectedCameras { get; }

        /// <summary>Applies a set to this camera or to all the cameras of its kind connected now. Returns at once.</summary>
        void ApplyCameraPreset(string id, bool allCameras);
    }

    /// <summary>(Swim 2.20.0) A ready-made set of settings of a camera.</summary>
    public sealed class CameraPreset
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }

        public CameraPreset(string id, string name, string description)
        {
            Id = id;
            Name = name;
            Description = description;
        }
    }

    /// <summary>
    /// (Swim 2.20.0) The language of the texts that the camera modules draw on their images (Greek or English), set by the screens
    /// with the language of the swim analysis.
    /// </summary>
    public static class CameraUiLanguage
    {
        /// <summary>The texts in English (else in Greek).</summary>
        public static bool English { get; set; }
    }
}
