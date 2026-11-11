#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

        /// <summary>(2.21.0) The name on a small button ("Αγώνας"); the name when not given.</summary>
        public string ShortName { get; private set; }

        public CameraPreset(string id, string name, string description)
            : this(id, name, description, null)
        {
        }

        public CameraPreset(string id, string name, string description, string shortName)
        {
            Id = id;
            Name = name;
            Description = description;
            ShortName = string.IsNullOrEmpty(shortName) ? name : shortName;
        }
    }

    /// <summary>
    /// (Swim 2.21.0) A camera that records on its own card and gives its files to the computer: who it is (its serial number, also
    /// written in its files) and the download of some of its files by its own Wi-Fi.
    /// </summary>
    public interface IRemoteMediaSource
    {
        /// <summary>The serial number of the camera ("" while it is not known: before its first connection).</summary>
        string CameraSerial { get; }

        /// <summary>The model ("HERO9 Black"; "" while it is not known).</summary>
        string CameraModel { get; }

        /// <summary>
        /// The time of the last start command the camera did not do (not connected, refused, or taken but not started): that recording
        /// has no file of this camera. DateTime.MinValue when none.
        /// </summary>
        DateTime FailedStart { get; }

        /// <summary>
        /// (2.21.0) Seconds between a start command (its time) and the moment the camera said it records, as measured; NaN when not
        /// measured for that command. The videos of the cameras of a recording are lined up by it when their sounds do not match.
        /// </summary>
        double StartOffset(DateTime commandTime);

        /// <summary>
        /// The computer joins the Wi-Fi of the camera, lists the videos of its card, asks `choose` which ones go where (the files and
        /// their paths on the computer) and downloads them, then goes back to its network. One camera at a time. Returns null when it
        /// went, else what went wrong (in the language of the interface).
        /// </summary>
        Task<string> DownloadAsync(Func<IList<RemoteMediaFile>, IList<RemoteMediaDownload>> choose, IProgress<RemoteMediaProgress> progress, CancellationToken token);
    }

    /// <summary>(Swim 2.21.0) A video on the card of a camera.</summary>
    public sealed class RemoteMediaFile
    {
        /// <summary>The folder of the card ("100GOPRO") and the name of the file ("GX010123.MP4").</summary>
        public string Folder { get; set; }
        public string Name { get; set; }

        /// <summary>When the recording started, on the clock of the camera (the clock of the computer since the camera took it).</summary>
        public DateTime Created { get; set; }

        public long Size { get; set; }
    }

    /// <summary>(Swim 2.21.0) A file of the card and where it goes on the computer.</summary>
    public sealed class RemoteMediaDownload
    {
        public RemoteMediaFile File { get; set; }
        public string TargetPath { get; set; }
    }

    /// <summary>(Swim 2.21.0) How a download goes: the file, its bytes so far and in all.</summary>
    public sealed class RemoteMediaProgress
    {
        public string Text { get; set; }
        public long Done { get; set; }
        public long Total { get; set; }
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
