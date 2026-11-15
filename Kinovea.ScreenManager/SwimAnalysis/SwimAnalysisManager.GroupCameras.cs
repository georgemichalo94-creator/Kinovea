#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.23.0) The snapshots of a group of screens from the camera of the moment. The cameras follow the swimmer from the side, one
    /// out of the water and one in the water: the snapshot of an event is the picture of the camera that shows it (a start from its
    /// signal to the entry out of the water, what follows and the whole of a turn in the water; SwimCameraPick), alone and in the
    /// shape of its video, not the pictures of all the screens joined. The analyst says once where each camera films from (the window
    /// «Κάμερες της ομάδας»; kept by camera in the settings); until then the snapshots stay the picture of all the screens. The
    /// snapshot key pressed again on another screen of the group at the frame of a snapshot gives it the picture of that screen. For
    /// the training videos (the technique analysis of starts and turns); the choice «Όλες οι οθόνες μαζί» keeps the joined pictures.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>Settings.GroupSnapshots: the picture of the camera of the moment.</summary>
        public const int GroupSnapshotsCamera = 0;

        /// <summary>Settings.GroupSnapshots: the pictures of all the screens of the group joined (as before 2.23.0).</summary>
        public const int GroupSnapshotsAll = 1;

        /// <summary>The cameras known only by their video file kept in the settings (the latest ones).</summary>
        private const int MaxFileCameras = 200;

        /// <summary>The snapshot key pressed again this soon gives the snapshot the picture of the screen it was pressed on (s).</summary>
        private const double RetakeSeconds = 20;

        /// <summary>The screen the command in progress came from (a key or a button of it); null for the analysis's own commands.</summary>
        private PlayerScreenUserInterface commandScreen;

        /// <summary>The camera of each video opened in this session (who it is; its role is in the settings).</summary>
        private readonly Dictionary<string, SwimGroupCamera> videoCameras = new Dictionary<string, SwimGroupCamera>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The cameras the window was shown for by itself, and the ones the missing roles were said for (once each in a session).</summary>
        private string camerasAsked = "";
        private string camerasHinted = "";

        /// <summary>The snapshot whose picture the snapshot key, pressed again on this screen, takes from it.</summary>
        private string retakeSnapshotId;
        private PlayerScreenUserInterface retakeScreen;
        private DateTime retakeOffered;

        /// <summary>Snapshots whose picture was taken again while the one before was on its way to the Drive.</summary>
        private readonly HashSet<string> retakenSnapshots = new HashSet<string>();

        /// <summary>
        /// A picture was taken from a screen that showed another moment than the one of the mark and is not tied to the screen of the
        /// analysis by the synchronisation (the frame it showed): said with the snapshot, in full once for these cameras.
        /// </summary>
        private bool snapshotUntied;
        private string untiedHinted = "";

        /// <summary>The snapshots of the group one camera at a time: a group of screens on a training video, with the choice of the settings.</summary>
        internal bool GroupCameraMode
        {
            get { return InGroup && Race != null && Race.IsTraining && Settings != null && Settings.GroupSnapshots == GroupSnapshotsCamera && GroupScreens.Count >= 2; }
        }

        /// <summary>A camera of the group has no role yet: the snapshots stay the picture of all the screens until it is said.</summary>
        internal bool GroupCamerasMissing
        {
            get { return GroupCamerasOf(GroupMembers()).Any(c => c.Role == SwimCameraPick.None); }
        }

        /// <summary>The screens of the group of screens with a video, in their order (none without a group): wherever the analysis is.</summary>
        private static List<PlayerScreenUserInterface> GroupMembers()
        {
            if (!VideoScreenGroup.Active)
                return new List<PlayerScreenUserInterface>();
            return VideoScreenGroup.Members.Where(m => m != null && !m.IsDisposed && m.SwimFrameServer != null && m.SwimFrameServer.Loaded).ToList();
        }

        /// <summary>
        /// The camera of a screen: who it is (the recordings of the GoPro cameras, else the name of its video file) and its role as
        /// the settings keep it (None: not said yet).
        /// </summary>
        internal SwimGroupCamera CameraOf(PlayerScreenUserInterface screen)
        {
            string path = "";
            try
            {
                if (screen != null && !screen.IsDisposed && screen.SwimFrameServer != null && screen.SwimFrameServer.Loaded)
                    path = screen.SwimFrameServer.VideoReader.FilePath ?? "";
            }
            catch (Exception e)
            {
                log.Error("Could not read the video of a screen of the group.", e);
            }

            SwimGroupCamera who;
            if (!videoCameras.TryGetValue(path, out who))
            {
                List<SwimTake> takes = null;
                try
                {
                    takes = SwimGoProTakes.Takes;
                }
                catch (Exception e)
                {
                    log.Error("Could not read the recordings of the GoPro cameras.", e);
                }

                who = SwimCameraPick.Identify(path, takes);
                videoCameras[path] = who;
            }

            SwimGroupCamera kept = Settings != null && Settings.GroupCameras != null ? Settings.GroupCameras.FirstOrDefault(c => c != null && c.Key == who.Key) : null;
            return new SwimGroupCamera { Key = who.Key, Name = who.Name, Role = kept != null ? kept.Role : SwimCameraPick.None };
        }

        private List<SwimGroupCamera> GroupCamerasOf(List<PlayerScreenUserInterface> screens)
        {
            return screens.Select(s => CameraOf(s)).ToList();
        }

        private static string RoleText(int role)
        {
            switch (role)
            {
                case SwimCameraPick.Above: return SwimLang.T("έξω από το νερό");
                case SwimCameraPick.Water: return SwimLang.T("μέσα στο νερό");
                case SwimCameraPick.Extra: return SwimLang.T("μόνο όταν πατάω πάνω της");
                default: return SwimLang.T("δεν έχει οριστεί");
            }
        }

        /// <summary>The cameras of the group in words: "οθόνη 1 έξω από το νερό · οθόνη 2 μέσα στο νερό".</summary>
        internal string GroupCamerasText()
        {
            List<PlayerScreenUserInterface> screens = GroupMembers();
            List<SwimGroupCamera> cameras = GroupCamerasOf(screens);
            List<string> parts = new List<string>();
            for (int i = 0; i < screens.Count; i++)
                parts.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("οθόνη {0} {1}"), screens[i].SwimScreenNumber, RoleText(cameras[i].Role)));
            return string.Join(" · ", parts.ToArray());
        }

        /// <summary>What the group says of its snapshots when it is made or changed (a training video): the cameras, or that they are not said yet.</summary>
        private string GroupCamerasSaid()
        {
            if (!GroupCameraMode)
                return "";
            if (GroupCamerasMissing)
                return SwimLang.T(" Στιγμιότυπα: πες μία φορά ποια κάμερα είναι έξω και ποια μέσα στο νερό («Κάμερες…» στη σελίδα Στιγμιότυπα)· ως τότε όλες οι οθόνες μαζί.");
            return string.Format(CultureInfo.InvariantCulture, SwimLang.T(" Στιγμιότυπα από την κάμερα της στιγμής: {0}."), GroupCamerasText());
        }

        /// <summary>The snapshots of a group: the camera of the moment (GroupSnapshotsCamera) or all the screens together (GroupSnapshotsAll).</summary>
        public void SetGroupSnapshots(int choice)
        {
            if (Settings == null || Settings.GroupSnapshots == choice)
                return;

            Settings.GroupSnapshots = choice;
            Settings.Save();
            retakeSnapshotId = null;
            if (form != null && !form.IsDisposed)
                form.RefreshGroupChoices();
            if (choice == GroupSnapshotsAll)
                Status(SwimLang.T("Στιγμιότυπα της ομάδας: όλες οι οθόνες μαζί."), false);
            else
                Status(SwimLang.T("Στιγμιότυπα της ομάδας: η κάμερα της στιγμής.") + GroupCamerasSaid(), false);
        }

        /// <summary>
        /// The window «Κάμερες της ομάδας»: where each camera of the group films from, kept by camera in the settings. What is known
        /// is chosen already (the name of a camera that says it, the other side of the surface for the second of two cameras).
        /// True when the cameras were saved.
        /// </summary>
        public bool EditGroupCameras()
        {
            List<PlayerScreenUserInterface> screens = GroupMembers();
            if (screens.Count < 2)
            {
                Status(SwimLang.T("Οι κάμερες ορίζονται με μια ομάδα οθονών με βίντεο (κουμπί «Ομάδα»)."), true);
                return false;
            }

            List<SwimGroupCamera> cameras = GroupCamerasOf(screens);

            // What is not said yet is proposed: what the name of the camera says, the other side of the surface for the second of two
            // cameras, else the role its screen had the last time. The window asks to check a proposal.
            int[] roles = cameras.Select(c => c.Role != SwimCameraPick.None ? c.Role : SwimCameraPick.SuggestRole(c.Name)).ToArray();
            SwimCameraPick.Complete(roles);
            List<int> last = Settings.GroupCameraRoles;
            for (int i = 0; i < roles.Length; i++)
            {
                if (roles[i] == SwimCameraPick.None && last != null && i < last.Count && last[i] >= SwimCameraPick.Above && last[i] <= SwimCameraPick.Extra)
                    roles[i] = last[i];
            }

            bool proposed = false;
            for (int i = 0; i < roles.Length; i++)
                proposed |= roles[i] != SwimCameraPick.None && cameras[i].Role == SwimCameraPick.None;

            List<string> titles = new List<string>();
            for (int i = 0; i < screens.Count; i++)
                titles.Add(CameraTitle(screens[i], cameras[i]));

            int[] chosen;
            using (FormSwimGroupCameras dialog = new FormSwimGroupCameras(titles, roles, proposed))
            {
                IWin32Window owner = DialogOwner;
                DialogResult result = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
                if (result != DialogResult.OK)
                    return false;
                chosen = dialog.Roles;
            }

            if (chosen == null || chosen.Length != cameras.Count || chosen.Any(r => r == SwimCameraPick.None))
                return false;

            if (Settings.GroupCameras == null)
                Settings.GroupCameras = new List<SwimGroupCamera>();
            for (int i = 0; i < cameras.Count; i++)
            {
                string key = cameras[i].Key;
                Settings.GroupCameras.RemoveAll(c => c == null || c.Key == key);
                Settings.GroupCameras.Add(new SwimGroupCamera { Key = key, Name = cameras[i].Name, Role = chosen[i] });
            }

            Settings.GroupCameraRoles = chosen.ToList();

            // The cameras known only by their video file: the latest ones stay.
            List<SwimGroupCamera> files = Settings.GroupCameras.Where(c => c.Key != null && c.Key.StartsWith("file:", StringComparison.Ordinal)).ToList();
            for (int i = 0; i < files.Count - MaxFileCameras; i++)
                Settings.GroupCameras.Remove(files[i]);

            Settings.Save();
            retakeSnapshotId = null;
            if (form != null && !form.IsDisposed)
                form.RefreshGroupChoices();
            Status(SwimLang.T("✓ Οι κάμερες της ομάδας: ") + GroupCamerasText() + ".", false);
            return true;
        }

        /// <summary>"Οθόνη 2 · GoPro B": the screen and the name of its camera (the name of its video when the camera is not known).</summary>
        private static string CameraTitle(PlayerScreenUserInterface screen, SwimGroupCamera camera)
        {
            string name = camera.Name ?? "";
            if (name.Length == 0)
            {
                try
                {
                    name = Path.GetFileNameWithoutExtension(screen.SwimFrameServer.VideoReader.FilePath ?? "") ?? "";
                }
                catch (Exception)
                {
                    name = "";
                }
            }

            string title = string.Format(CultureInfo.InvariantCulture, SwimLang.T("Οθόνη {0}"), screen.SwimScreenNumber);
            return name.Length > 0 ? title + "  ·  " + name : title;
        }

        /// <summary>
        /// The cameras of the group have no role: the window is shown by itself once in a session for these cameras, at a snapshot
        /// the analyst takes while the videos are still (a command of a screen; not for the marks the program puts by itself).
        /// </summary>
        private bool AskGroupCameras(List<PlayerScreenUserInterface> screens, List<SwimGroupCamera> cameras)
        {
            string set = string.Join("|", cameras.Select(c => c.Key).ToArray());
            if (set == camerasAsked || commandScreen == null || AnalysisBusy || screens.Any(s => s.IsCurrentlyPlaying))
                return false;

            camerasAsked = set;
            string said = LastStatus;
            bool warning = LastStatusIsWarning;
            try
            {
                bool saved = EditGroupCameras() && !GroupCamerasMissing;

                // The words of the command stay (the snapshot says the screen its picture came from).
                if (saved && !string.IsNullOrEmpty(said))
                    Status(said, warning);
                return saved;
            }
            catch (Exception e)
            {
                log.Error("Could not ask for the cameras of the group.", e);
                return false;
            }
        }

        /// <summary>
        /// Said once for these cameras: the snapshot is the picture of all the screens because the cameras of the group are not said.
        /// </summary>
        private string CamerasHint()
        {
            bool untied = snapshotUntied;
            snapshotUntied = false;
            if (!GroupCameraMode)
                return "";

            List<SwimGroupCamera> cameras = GroupCamerasOf(GroupScreens);
            string set = string.Join("|", cameras.Select(c => c.Key).ToArray());
            if (cameras.Any(c => c.Role == SwimCameraPick.None))
            {
                if (set == camerasHinted)
                    return "";

                camerasHinted = set;
                return SwimLang.T(" Όλες οι οθόνες μαζί: για την κάμερα της στιγμής πες ποια κάμερα είναι έξω και ποια μέσα στο νερό («Κάμερες…» στη σελίδα Στιγμιότυπα).");
            }

            // A screen that is not tied to the others showed another moment: its picture is the frame it showed.
            if (!untied)
                return "";
            if (set == untiedHinted)
                return SwimLang.T(" ⚠ Οι οθόνες δεν είναι συγχρονισμένες μεταξύ τους.");

            untiedHinted = set;
            return SwimLang.T(" ⚠ Οι οθόνες της ομάδας δεν είναι συγχρονισμένες μεταξύ τους: η εικόνα είναι το καρέ που δείχνει η οθόνη της κάμερας. Με όλες τις οθόνες στην ίδια στιγμή πάτα «Συγχρονισμός» στα κοινά χειριστήρια (ή «Συγχρονισμός από τον ήχο» στο μενού Ομάδα).");
        }

        /// <summary>
        /// The screen is tied to the screen of the analysis by the synchronisation (the sync button or the sound): the same time from
        /// their origins is the same moment on both, so it can be moved to the moment of a mark for its picture.
        /// </summary>
        private bool TiedToAnalysis(PlayerScreenUserInterface screen)
        {
            Func<PlayerScreenUserInterface, bool> tied = PlayerScreenUserInterface.SwimGroupTiedAsked;
            if (tied == null || screen == null || player == null)
                return false;

            try
            {
                return tied(player) && (screen == player || tied(screen));
            }
            catch (Exception e)
            {
                log.Error("Could not tell whether the screens of the group are synchronised.", e);
                return false;
            }
        }

        /// <summary>The camera that shows a moment of the video of the analysis (SwimCameraPick.RoleAt of its phase).</summary>
        private int CameraRoleAt(long timestamp)
        {
            SwimTechPhase phase = PhaseOf(timestamp);
            if (phase == null || Timing == null)
                return SwimCameraPick.None;
            return SwimCameraPick.RoleAt(phase, Timing.Seconds(timestamp, phase.Origin));
        }

        /// <summary>The camera that shows the event of a mark (SwimCameraPick.RoleOfMark).</summary>
        private int CameraRoleOf(SwimMark mark)
        {
            if (mark == null)
                return SwimCameraPick.None;
            SwimTechPhase phase = PhaseOf(mark.Timestamp);
            return SwimCameraPick.RoleOfMark(mark.Type, phase != null ? (SwimPhaseKind?)phase.Kind : null);
        }

        /// <summary>
        /// The picture of a snapshot at a moment of the video of the analysis, with the graphics as the exports have them: in a
        /// group of screens with its cameras said, the picture of the camera of the moment alone (wanted: SwimCameraPick.Above or
        /// Water, None when the moment says nothing), else as CaptureFrame (all the screens of a group joined, or the one video).
        /// The camera of the picture goes with it (the Tag of the image) to its snapshot.
        /// </summary>
        private Bitmap CaptureSnapshotFrame(long timestamp, bool live, int wanted)
        {
            bool graphics = Settings.OverlayInExports;
            if (GroupCameraMode)
            {
                try
                {
                    Bitmap one = CaptureCameraOfMoment(timestamp, graphics, live, wanted, null);
                    if (one != null)
                        return one;
                }
                catch (Exception e)
                {
                    log.Error("Could not take the picture of the camera of the moment.", e);
                }
            }

            return CaptureFrame(timestamp, graphics, false, live);
        }

        /// <summary>
        /// The picture of one camera of the group at a moment of the video of the analysis: the first of the screens wanted
        /// (SwimCameraPick.Order) whose video has the moment. only: this screen whatever the moment (the analyst asked for its
        /// picture). Null: no camera gives it (the cameras are not said, no video has the moment), the caller takes all the screens.
        /// </summary>
        private Bitmap CaptureCameraOfMoment(long timestamp, bool graphics, bool live, int wanted, PlayerScreenUserInterface only)
        {
            List<PlayerScreenUserInterface> screens = GroupScreens;
            if (screens.Count < 2 || !screens.Contains(player))
                return null;

            List<SwimGroupCamera> cameras = GroupCamerasOf(screens);
            List<int> order;
            if (only != null)
            {
                order = new List<int>();
                if (screens.Contains(only))
                    order.Add(screens.IndexOf(only));
            }
            else
            {
                if (cameras.Any(c => c.Role == SwimCameraPick.None))
                {
                    if (!AskGroupCameras(screens, cameras))
                        return null;
                    cameras = GroupCamerasOf(screens);
                }

                PlayerScreenUserInterface pressed = commandScreen;
                order = SwimCameraPick.Order(cameras.Select(c => c.Role).ToList(), wanted, pressed != null ? screens.IndexOf(pressed) : -1, screens.IndexOf(player));
            }

            bool moving = screens.Any(s => s.IsCurrentlyPlaying);
            long frame = screens.Max(s => FrameTime(s));
            foreach (int i in order)
            {
                Bitmap image = CaptureScreenAt(screens[i], timestamp, graphics, live, moving, frame);
                if (image == null)
                    continue;

                image.Tag = cameras[i].Key;
                return image;
            }

            return null;
        }

        /// <summary>The time between two frames of the video of a screen (microseconds of real time, as SwimTimeFromOrigin).</summary>
        private static long FrameTime(PlayerScreenUserInterface screen)
        {
            try
            {
                long step = (long)Math.Round(Math.Max(1.0, screen.SwimFrameServer.VideoReader.Info.AverageTimeStampsPerFrame));
                long now = screen.CurrentTimestamp;
                return Math.Max(1, Math.Abs(screen.SwimTimeFromOrigin(now + step) - screen.SwimTimeFromOrigin(now)));
            }
            catch (Exception)
            {
                return 40000;
            }
        }

        /// <summary>
        /// The picture of a screen of the group at a moment of the video of the analysis, as an image of the report (at most
        /// 1280 x 720): the frame of its video at that moment (the same time from the time origin of each video, as the marks of the
        /// group), with its own drawings and the panels of the analysis (not when the group shows them on its panel). A screen that
        /// shows another moment goes there for the picture and comes back when it is tied to the screen of the analysis by the
        /// synchronisation (the sync button or the sound); a screen that is not tied gives the frame it shows (the time from the
        /// origins may not be the same moment on it, even when it shows that time: two cameras that started at different moments and
        /// were never synchronised), and the snapshot always says so. A tied screen is not moved while the videos play, and
        /// gives nothing when its video does not have the moment (null: the next screen wanted). frame: the longest time between two
        /// frames of the videos of the group.
        /// </summary>
        private Bitmap CaptureScreenAt(PlayerScreenUserInterface screen, long timestamp, bool graphics, bool live, bool moving, long frame)
        {
            if (screen == null || screen.IsDisposed || screen.SwimFrameServer == null || !screen.SwimFrameServer.Loaded)
                return null;

            long moment = player.SwimTimeFromOrigin(timestamp);
            long at = screen == player ? timestamp : screen.SwimTimestampFromOrigin(moment);

            // The frame of the moment: within a frame of it (while the videos play, as near as they keep together: three frames, a
            // tenth of a second at least).
            long near = frame + frame / 20;
            bool has = screen.SwimInVideo(at);
            bool shows = has && Math.Abs(screen.SwimTimeFromOrigin(screen.CurrentTimestamp) - moment) <= (moving ? Math.Max(3 * frame, 100000) : near);
            long back = 0;
            bool moved = false;
            bool tied = TiedToAnalysis(screen);

            // (2.23.0) The picture of another screen that is not tied: said always (its clock may show the time of the moment while
            // its video shows another moment).
            if (screen != player && !tied)
                snapshotUntied = true;

            if (!shows)
            {
                if (!tied)
                {
                    // Not tied by the synchronisation (the time from the origins may not be the same moment on it): the frame it
                    // shows, as in the picture of all the screens; the snapshot says so.
                    snapshotUntied = true;
                }
                else if (moving || !has)
                {
                    return null;
                }
                else
                {
                    // The frame nearest to the moment (the player shows the first frame at or after the time asked: half a frame earlier).
                    back = screen.CurrentTimestamp;
                    moved = true;
                    long half = (long)Math.Floor(Math.Max(1.0, screen.SwimFrameServer.VideoReader.Info.AverageTimeStampsPerFrame) * 0.5);
                    screen.ForcePosition(Math.Max(screen.SwimFrameServer.VideoReader.WorkingZone.Start, at - half), true);
                    if (Math.Abs(screen.SwimTimeFromOrigin(screen.CurrentTimestamp) - moment) > near)
                    {
                        screen.ForcePosition(back, true);
                        return null;
                    }
                }
            }

            Bitmap full = null;
            try
            {
                bool panels = graphics && !GroupPanelShown;
                if (screen == player)
                {
                    full = CaptureAnalysisTile(timestamp, panels, false, live);
                }
                else
                {
                    full = CaptureOtherTile(screen, live);
                    if (full != null && panels)
                    {
                        using (Graphics g = Graphics.FromImage(full))
                            DrawPanelsOnImage(g, full.Size, timestamp);
                    }
                }
            }
            catch (Exception e)
            {
                log.Error("Could not take the picture of a screen of the group.", e);
                if (full != null)
                    full.Dispose();
                full = null;
            }
            finally
            {
                if (moved)
                {
                    try
                    {
                        screen.ForcePosition(back, true);
                    }
                    catch (Exception e)
                    {
                        log.Error("Could not bring a screen of the group back to its frame.", e);
                    }
                }
            }

            return full != null ? FitReportImage(full) : null;
        }

        /// <summary>
        /// An image at the size of the images of the report: at most 1280 x 720 (Google Sheets accepts images up to 1 million pixels
        /// and 2 MB). The image given is disposed when a smaller one takes its place.
        /// </summary>
        private static Bitmap FitReportImage(Bitmap full)
        {
            double scale = Math.Min(1.0, Math.Min(1280.0 / full.Width, 720.0 / full.Height));
            if (scale >= 0.999)
                return full;

            Bitmap small = new Bitmap(Math.Max(1, (int)Math.Round(full.Width * scale)), Math.Max(1, (int)Math.Round(full.Height * scale)), PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(full, new Rectangle(0, 0, small.Width, small.Height));
            }

            full.Dispose();
            return small;
        }

        /// <summary>" (οθόνη 2)" for the snapshot of a camera of the group (the screen that shows its video now), else "".</summary>
        private string ScreenSaid(SwimSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.Camera) || !InGroup)
                return "";

            PlayerScreenUserInterface screen = GroupScreens.FirstOrDefault(s => CameraOf(s).Key == snapshot.Camera);
            return screen != null ? string.Format(CultureInfo.InvariantCulture, SwimLang.T(" (οθόνη {0})"), screen.SwimScreenNumber) : "";
        }

        /// <summary>
        /// The screen the snapshot key was pressed on, when its picture could take the place of the snapshot's: a group with the
        /// camera of the moment and its cameras said, another camera than the one of the snapshot. Null otherwise.
        /// </summary>
        private PlayerScreenUserInterface RetakeScreenFor(SwimSnapshot snapshot)
        {
            PlayerScreenUserInterface screen = commandScreen;
            if (snapshot == null || screen == null || screen.IsDisposed || !GroupCameraMode || !GroupScreens.Contains(screen) || GroupCamerasMissing)
                return null;

            return string.Equals(snapshot.Camera ?? "", CameraOf(screen).Key, StringComparison.Ordinal) ? null : screen;
        }

        /// <summary>
        /// After the snapshot key: when the screen it was pressed on is another camera than the one of the snapshot, the key pressed
        /// again there gives the snapshot the picture of that screen; said in the status ("" when there is nothing to say).
        /// </summary>
        private string RetakeHint(SwimSnapshot snapshot)
        {
            PlayerScreenUserInterface screen = RetakeScreenFor(snapshot);
            if (screen == null)
            {
                retakeSnapshotId = null;
                return "";
            }

            retakeSnapshotId = snapshot.Id;
            retakeScreen = screen;
            retakeOffered = DateTime.UtcNow;
            return string.Format(CultureInfo.InvariantCulture, SwimLang.T(" Για την εικόνα της οθόνης {0}: πάτα ξανά το 📷."), screen.SwimScreenNumber);
        }

        /// <summary>
        /// The snapshot key at the frame of a snapshot, pressed again on the screen the status named: the snapshot gets the picture of
        /// that screen (its comments, its event and its place in the report stay; the picture on the Drive is replaced). False when
        /// this press is not that one (the caller says the snapshot is there already).
        /// </summary>
        private bool RetakeSnapshot(SwimSnapshot snapshot, long timestamp)
        {
            PlayerScreenUserInterface screen = RetakeScreenFor(snapshot);
            bool again = screen != null && retakeSnapshotId == snapshot.Id && retakeScreen == screen && (DateTime.UtcNow - retakeOffered).TotalSeconds <= RetakeSeconds;
            if (!again)
                return false;

            retakeSnapshotId = null;
            Bitmap image = null;
            try
            {
                image = CaptureCameraOfMoment(timestamp, Settings.OverlayInExports, false, SwimCameraPick.None, screen);
            }
            catch (Exception e)
            {
                log.Error("Could not take the picture of the screen for the snapshot.", e);
            }

            if (image == null)
            {
                Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Το βίντεο της οθόνης {0} δεν έχει αυτή τη στιγμή: το στιγμιότυπο έμεινε όπως ήταν."), screen.SwimScreenNumber), true);
                return true;
            }

            string old = SnapshotPath(snapshot);
            try
            {
                // Another file: the window shows the new picture, the old one goes.
                snapshot.File = Path.GetFileName(SaveSnapshotImage(image, snapshot.Id + "_" + DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture)));
                snapshot.Width = image.Width;
                snapshot.Height = image.Height;
                snapshot.Camera = image.Tag as string;
            }
            catch (Exception e)
            {
                log.Error("Could not save the new picture of the snapshot.", e);
                Status(SwimLang.T("Δεν αποθηκεύτηκε το στιγμιότυπο: ") + e.Message, true);
                return true;
            }
            finally
            {
                image.Dispose();
            }

            try
            {
                string now = SnapshotPath(snapshot);
                if (old != null && now != null && !string.Equals(old, now, StringComparison.OrdinalIgnoreCase) && File.Exists(old))
                    File.Delete(old);
            }
            catch (Exception e)
            {
                log.Error("Could not delete the picture the snapshot had before.", e);
            }

            // The new picture goes to the Drive in the place of the one before (one on its way is followed by this one).
            if (uploading.Contains(snapshot.Id))
                retakenSnapshots.Add(snapshot.Id);
            snapshot.FileId = "";
            uploadFailed.Remove(snapshot.Id);
            Status(string.Format(CultureInfo.InvariantCulture, SwimLang.T("📷 Το στιγμιότυπο πήρε την εικόνα της οθόνης {0}."), screen.SwimScreenNumber) + CamerasHint(), false);
            Changed(false);
            UploadSnapshot(snapshot);
            if (form != null)
                form.SnapshotAdded(snapshot);
            return true;
        }
    }
}
