#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;

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
    /// (Swim 2.20.0) The language of the texts that the camera modules draw on their images (Greek or English), set by the screens
    /// with the language of the swim analysis.
    /// </summary>
    public static class CameraUiLanguage
    {
        /// <summary>The texts in English (else in Greek).</summary>
        public static bool English { get; set; }
    }
}
