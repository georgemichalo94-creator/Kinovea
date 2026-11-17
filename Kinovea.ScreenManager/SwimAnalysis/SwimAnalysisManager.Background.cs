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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.25.0) The package for the coaches finished in the background: «Συνέχισε με άλλον αθλητή» in its window. Its screens leave the
    /// window as they are (their videos, their drawings, their time origins, their group), hidden, and new empty screens take their
    /// places. This analysis goes with them, with everything it had (the graphics as they were on the screen, the comparison, the panel of
    /// the group…): its video and its PDF are made by the same code as in the window, on the same screens. A new analysis takes the screens
    /// for the next athlete, with the same settings, lists and learning and the same way of working (the bars, and the window when it was
    /// open, come on the video loaded in the place of the screen of the analysis; the group comes back on the new screens of the group).
    /// The analysis of the package writes no file of the analyses nor the settings: what its package changes in its race (the PDF, the
    /// KOE App, the ready package) goes to the analysis of the screens, in memory when that video is open again, else into its file (a
    /// value the analyst changed meanwhile stays his). One video at a time (SwimPackageQueue). The box at the bottom right of Kinovea
    /// shows the packages in the background (FormSwimPackageTray), a notice says when one is ready; Kinovea asks before closing while one
    /// is being made.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        #region Package in the background
        /// <summary>Set by the screen manager: the screens of these views can leave the window (Kinovea's question about their drawings).</summary>
        public static Func<List<PlayerScreenUserInterface>, bool> ScreensKeepAsked;

        /// <summary>
        /// Set by the screen manager: the screens of these views leave the window (their controls into the host given), new empty screens in
        /// their places. Each screen taken with the view of the new screen in its place.
        /// </summary>
        public static Func<List<PlayerScreenUserInterface>, Control, List<KeyValuePair<PlayerScreen, PlayerScreenUserInterface>>> ScreensLendAsked;

        /// <summary>Set by the screen manager: the screens taken by ScreensLendAsked closed (their videos given back).</summary>
        public static Action<List<PlayerScreen>> LentScreensCloseAsked;

        /// <summary>A package going on in the background with the analysis it went with.</summary>
        private sealed class BackgroundPackage
        {
            public SwimAnalysisManager Manager;
            public PackageJob Job;
            public FormSwimPackage Form;
            public SwimRace Race;
            public SwimPackage Package;
            public List<PlayerScreenUserInterface> Views = new List<PlayerScreenUserInterface>();
            public List<PlayerScreen> Screens = new List<PlayerScreen>();
            public Form Host;
            public Form Owner;
            public string Title;
            public string FolderBefore;
            public string BaseBefore;
            public string MadeBefore;
            public DateTime RunningSince = DateTime.MinValue;

            /// <summary>The notice of its end was shown and answered (or its window opened since).</summary>
            public bool Noticed;

            /// <summary>Its window closed for good (OK, «Κλείσιμο», stopped): nothing in the box any more.</summary>
            public bool Closed;

            /// <summary>Its screens closed (its video ended).</summary>
            public bool Released;

            /// <summary>Everything ended: the analysis let go.</summary>
            public bool Finished;
        }

        private static readonly List<BackgroundPackage> background = new List<BackgroundPackage>();
        private static readonly object backgroundLock = new object();

        /// <summary>A package in the background waits at most this long for its PDF after its window closed, as in its window (PackagePdfMinutes).</summary>
        private static readonly TimeSpan BackgroundPdfWait = TimeSpan.FromMinutes(PackagePdfMinutes + 1);

        // The analysis of a package in the background: it follows no screen of the window and writes no file (SaveNow gives its changes to
        // the analysis of the screens); the analysis was on the screen when it went (its graphics on the video of the package), the time
        // origin of its video (after its screens closed), the analysis as it was last given, the settings it had (the analysis of the
        // screens keeps them).
        private bool retired;
        private bool activeAtRetire;
        private long retiredOrigin;
        private string routedBaseline;
        private SwimSettings settingsBeforeRetire;
        private BackgroundPackage retiredPackage;
        private System.Windows.Forms.Timer backgroundWait;

        // The analysis of the screens after a package went to the background: the screen in the place of the screen of the analysis, where
        // the analysis comes back with the next video loaded there, and its window opened again then.
        private PlayerScreenUserInterface armedScreen;
        private bool armedWindow;

        /// <summary>The time origin of the video of the analysis (a package in the background whose screens closed: the one it had).</summary>
        private long AnalysisOrigin
        {
            get { return frameServer != null && frameServer.Loaded ? frameServer.Metadata.TimeOrigin : (retired ? retiredOrigin : 0); }
        }

        /// <summary>The screen belongs to a package made in the background (it left the window).</summary>
        internal static bool InBackground(PlayerScreenUserInterface screen)
        {
            if (screen == null)
                return false;
            lock (backgroundLock)
                return background.Any(b => !b.Released && b.Views.Contains(screen));
        }

        /// <summary>
        /// The graphics of the analysis on an exported image of a screen (the video export, a snapshot, the package), drawn by the analysis
        /// of that screen: the analysis of a package in the background for its screens, else the analysis of the screens. Any thread.
        /// </summary>
        public static void ExportOverlayOf(PlayerScreenUserInterface screen, Graphics g, Size outputSize, long timestamp, Bitmap frame)
        {
            SwimAnalysisManager m = null;
            if (screen != null)
            {
                lock (backgroundLock)
                {
                    BackgroundPackage b = background.FirstOrDefault(x => !x.Released && x.Views.Contains(screen));
                    if (b != null)
                        m = b.Manager;
                }
            }

            if (m == null)
                m = instance;
            if (m != null)
                m.DrawExportOverlay(screen, g, outputSize, timestamp, frame);
        }

        /// <summary>A package in the background still makes its video, waits for it, or waits for its PDF (the language does not change then).</summary>
        internal static bool BackgroundBusy
        {
            get { return Background().Any(b => !b.Finished && (b.Form.VideoRunning || b.Form.VideoWaiting || PdfPending(b))); }
        }

        private static List<BackgroundPackage> Background()
        {
            lock (backgroundLock)
                return background.ToList();
        }

        private static bool PdfPending(BackgroundPackage b)
        {
            SwimPackage p = b.Package;
            return p.PdfWaiting && !p.PdfReady && string.IsNullOrEmpty(p.PdfError) && b.Manager.packagePdfPending.ContainsKey(b.Race.Id) &&
                DateTime.Now - b.Manager.packagePdfPending[b.Race.Id].Value < BackgroundPdfWait;
        }

        /// <summary>
        /// «Συνέχισε με άλλον αθλητή» in the window of the package (ShowDialog returned Ignore): Kinovea's question first for drawings of its
        /// screens not saved (cancelled: the window stays); then its screens leave the window, this analysis goes with them (saved and sent,
        /// it writes no file any more) and a new analysis takes the screens. False: the package stays in its window.
        /// </summary>
        private bool ToBackground(PackageJob job, FormSwimPackage form, SwimRace race, SwimPackage package, Form owner, List<PlayerScreenUserInterface> views,
            string folderBefore, string baseBefore, string madeBefore)
        {
            if (retired || ScreensKeepAsked == null || ScreensLendAsked == null || views == null || views.Count == 0 || views.Any(v => v == null || v.IsDisposed))
                return false;

            if (!ScreensKeepAsked(views))
                return false;

            Form host = null;
            try
            {
                // A window never shown that keeps the screens (the graphics of the package are drawn on their thread through them).
                host = new Form
                {
                    Text = "Kinovea Swim – package",
                    ShowInTaskbar = false,
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-32000, -32000),
                    Size = new Size(1280, 800)
                };
                IntPtr handle = host.Handle;
                if (handle == IntPtr.Zero)
                    throw new InvalidOperationException("No window for the screens.");
            }
            catch (Exception e)
            {
                log.Error("The package could not go to the background.", e);
                if (host != null)
                    host.Dispose();
                return false;
            }

            BackgroundPackage bp = new BackgroundPackage
            {
                Manager = this,
                Job = job,
                Form = form,
                Race = race,
                Package = package,
                Owner = owner,
                Host = host,
                Title = race.DisplayName,
                FolderBefore = folderBefore,
                BaseBefore = baseBefore,
                MadeBefore = madeBefore
            };
            bp.Views.AddRange(views);

            bool windowOpen = PanelOpen;
            PlayerScreenUserInterface analysisScreen = player;

            // The screens of the package draw their graphics with this analysis from now on (its video goes on meanwhile).
            lock (backgroundLock)
                background.Add(bp);
            Retire(bp);
            SwimAnalysisManager next = new SwimAnalysisManager(this);
            instance = next;

            List<KeyValuePair<PlayerScreen, PlayerScreenUserInterface>> taken = null;
            try
            {
                taken = ScreensLendAsked(views, host);
            }
            catch (Exception e)
            {
                log.Error("The screens of the package could not leave the window.", e);
            }

            if (taken == null || taken.Count == 0)
            {
                // Nothing left the window: this analysis stays the analysis of the screens, the package in its window.
                instance = this;
                next.Discard();
                Unretire();
                lock (backgroundLock)
                    background.Remove(bp);
                host.Dispose();
                return false;
            }

            bp.Screens.AddRange(taken.Select(t => t.Key));
            KeyValuePair<PlayerScreen, PlayerScreenUserInterface> place = taken.FirstOrDefault(t => t.Key.view == analysisScreen);
            next.armedScreen = place.Key != null ? place.Value : taken[0].Value;
            next.armedWindow = windowOpen;

            form.QueueTitle = bp.Title;
            form.VideoFinished += (s, e) => BackgroundVideoFinished(bp);
            form.FormClosed += (s, e) => BackgroundWindowClosed(bp);
            form.DialogResult = DialogResult.None;
            if (!form.VideoRunning && !form.VideoWaiting)
                BackgroundVideoFinished(bp);

            log.InfoFormat("Swim package in the background: {0} screen(s).", taken.Count);
            next.Status(string.Format(SwimLang.T("📦 Η αποστολή «{0}» τελειώνει στο παρασκήνιο: οι οθόνες είναι ελεύθερες για τον επόμενο αθλητή."), bp.Title), false);
            FormSwimPackageTray.UpdateFor(owner);
            return true;
        }

        /// <summary>
        /// This analysis goes to the background with its package: its group as it is, what is typed in its window, the analysis saved and
        /// the send that waits gone; then it follows no screen of the window (no handlers of the language, of the full screen and of the
        /// group, its timers stopped, its window hidden, its round buttons gone) and keeps a copy of the settings it never saves.
        /// </summary>
        private void Retire(BackgroundPackage bp)
        {
            bool grouped = InGroup;
            frozenGroup = grouped ? VideoScreenGroup.Members.ToList() : new List<PlayerScreenUserInterface>();
            frozenLead = grouped ? VideoScreenGroup.Lead : null;
            activeAtRetire = Active;
            retiredOrigin = frameServer != null && frameServer.Loaded ? frameServer.Metadata.TimeOrigin : 0;

            if (form != null && !form.IsDisposed)
                form.Retire();
            FlushPendingSend();
            if (saveTimer != null)
                saveTimer.Stop();
            SaveNow();
            try
            {
                routedBaseline = VideoFile != null ? JsonConvert.SerializeObject(VideoFile) : null;
            }
            catch (Exception e)
            {
                log.Error("Could not keep the analysis of the package in the background.", e);
                routedBaseline = null;
            }

            retired = true;
            retiredPackage = bp;
            settingsBeforeRetire = Settings;
            Settings = Settings.ReadOnlyCopy();

            SwimLang.Changed -= SwimLang_Changed;
            VideoFullScreen.ActiveChanged -= VideoFullScreen_ActiveChanged;
            VideoBigScreens.ActiveChanged -= VideoBigScreens_ActiveChanged;
            VideoScreenGroup.Changed -= VideoScreenGroup_Changed;
            foreach (System.Windows.Forms.Timer t in new[] { sendTimer, barsTimer, dialTimer, learningTimer, pilotTimer, pdfTimer, officialWait })
            {
                if (t != null)
                    t.Stop();
            }

            try
            {
                RemoveDials();
            }
            catch (Exception e)
            {
                log.Error("Could not take away the round buttons of the analysis of the package.", e);
            }
        }

        /// <summary>The package could not go to the background: this analysis is the analysis of the screens again, as it was.</summary>
        private void Unretire()
        {
            retired = false;
            retiredPackage = null;
            if (settingsBeforeRetire != null)
                Settings = settingsBeforeRetire;
            settingsBeforeRetire = null;
            frozenGroup = null;
            frozenLead = null;
            routedBaseline = null;

            SwimLang.Changed += SwimLang_Changed;
            VideoFullScreen.ActiveChanged += VideoFullScreen_ActiveChanged;
            VideoBigScreens.ActiveChanged += VideoBigScreens_ActiveChanged;
            VideoScreenGroup.Changed += VideoScreenGroup_Changed;
            if (learningTimer != null)
                learningTimer.Start();
            if (barsTimer != null && BarsShown)
                barsTimer.Start();
            if (form != null && !form.IsDisposed)
                form.Unretire();
            UpdateBars();
            InvalidatePlayer();
        }

        /// <summary>
        /// (2.25.0) The analysis of the screens after the one before went to the background with its package: the same settings (the one
        /// before keeps a copy), the same lists of athletes, references and finals, the same learning and its sync with the other Kinovea, the
        /// same way of working (the view, the analysis on the screen, the warnings already said).
        /// </summary>
        private SwimAnalysisManager(SwimAnalysisManager before)
        {
            Settings = before.settingsBeforeRetire ?? before.Settings;
            Athletes = before.Athletes;
            References = before.References;
            Finals = before.Finals;
            EliteLibrary = before.EliteLibrary;
            ReferencesMessage = before.ReferencesMessage;
            AthletesMessage = before.AthletesMessage;
            EliteMessage = before.EliteMessage;
            Timing = new SwimTiming();
            Computed = new SwimComputed();
            CalibrationLine1 = before.CalibrationLine1;
            CalibrationLine2 = before.CalibrationLine2;
            TechnicalView = before.TechnicalView;
            AngleName = before.AngleName;
            SyncState = Settings.IsConfigured ? SwimSyncState.Idle : SwimSyncState.NotConfigured;
            SyncMessage = Settings.IsConfigured ? SwimLang.T("Έτοιμο") : SwimLang.T("Δεν έχει ρυθμιστεί η σύνδεση με το Google Sheet");

            analysisOn = before.analysisOn;
            panelBeforeFullScreen = before.panelBeforeFullScreen;
            backendWarned = before.backendWarned;
            chartsWarned = before.chartsWarned;
            languageWarned = before.languageWarned;
            typedStrokesWarned = before.typedStrokesWarned;
            pdfChartsWarned = before.pdfChartsWarned;
            pdfPartsWarned = before.pdfPartsWarned;
            pdfSeparateWarned = before.pdfSeparateWarned;
            quickLookWarned = before.quickLookWarned;
            camerasAsked = before.camerasAsked;
            camerasHinted = before.camerasHinted;
            untiedHinted = before.untiedHinted;
            foreach (string id in before.quietAsked)
                quietAsked.Add(id);
            foreach (KeyValuePair<string, SwimGroupCamera> kv in before.videoCameras)
                videoCameras[kv.Key] = kv.Value;

            Wire();

            // The learning shared with the other Kinovea: the same, its sync goes on here.
            learning = before.learning;
            learningSync = before.learningSync;
            learningSyncUnsupported = before.learningSyncUnsupported;
            techShareUnsupported = before.techShareUnsupported;
            DateTime soonest = DateTime.Now + TimeSpan.FromMinutes(1);
            learningNextSync = before.learningNextSync > soonest ? before.learningNextSync : soonest;
            if (learningSync != null)
            {
                learningTimer = new System.Windows.Forms.Timer { Interval = 5000 };
                learningTimer.Tick += (s, e) => LearningSyncTick();
                learningTimer.Start();
            }
        }

        /// <summary>The analysis of the screens made for a package that could not go to the background: it lets go of what it holds.</summary>
        private void Discard()
        {
            retired = true;
            if (client != null)
                client.StateChanged -= Client_StateChanged;
            SwimLang.Changed -= SwimLang_Changed;
            VideoFullScreen.ActiveChanged -= VideoFullScreen_ActiveChanged;
            VideoBigScreens.ActiveChanged -= VideoBigScreens_ActiveChanged;
            VideoScreenGroup.Changed -= VideoScreenGroup_Changed;
            foreach (System.Windows.Forms.Timer t in new[] { saveTimer, sendTimer, learningTimer })
            {
                if (t != null)
                    t.Stop();
            }
        }

        /// <summary>
        /// A video was loaded in a screen (the screen manager): after a package went to the background, the analysis of the screens comes on
        /// the video loaded in the place of the screen of the analysis (as it followed a video replaced in its screen), with its window when
        /// it was open.
        /// </summary>
        public void VideoLoadedIn(PlayerScreenUserInterface screen, FrameServerPlayer server)
        {
            try
            {
                if (retired || armedScreen == null || screen == null || screen != armedScreen || server == null || !server.Loaded)
                    return;

                bool window = armedWindow;
                armedWindow = false;
                if (!analysisOn && !window)
                {
                    armedScreen = null;
                    return;
                }

                SwitchTo(screen, server);
                if (window)
                    ShowAnalysis(true);
            }
            catch (Exception e)
            {
                log.Error("Could not bring the analysis on the next video.", e);
            }
        }

        /// <summary>The video of a package in the background ended (made, failed or stopped): what the package keeps, its screens closed.</summary>
        private void BackgroundVideoFinished(BackgroundPackage bp)
        {
            packageRendering = false;
            suppressExportOverlay = false;
            try
            {
                if (bp.Package.VideoReady && !bp.Form.VideoCancelled)
                    RememberPackage(bp.Race, bp.Package);
            }
            catch (Exception e)
            {
                log.Error("Could not keep the package made in the background.", e);
            }

            ReleaseScreens(bp);
            FormSwimPackageTray.UpdateFor(bp.Owner);
        }

        /// <summary>
        /// The window of a package in the background closed for good (OK, «Κλείσιμο», stopped): as after its window (the signature of the
        /// messages for the next ones, the package as it was when it stopped, Smash on OK); the analysis waits for the PDF if it still comes.
        /// </summary>
        private void BackgroundWindowClosed(BackgroundPackage bp)
        {
            if (bp.Closed)
                return;
            bp.Closed = true;
            FormSwimPackage form = bp.Form;
            DialogResult result = form.DialogResult;
            try
            {
                SwimAnalysisManager live = instance;
                if (live != null && live != this && live.Settings != null && !string.Equals(form.Signature, live.Settings.PackageSignature ?? "", StringComparison.Ordinal))
                {
                    live.Settings.PackageSignature = form.Signature;
                    live.SaveSettingsQuietly();
                }

                bool cancelled = form.VideoCancelled;
                if (cancelled || !bp.Package.VideoReady)
                {
                    bp.Race.PackageFolder = bp.FolderBefore;
                    bp.Race.PackageBase = bp.BaseBefore;
                    bp.Race.PackageMade = bp.MadeBefore;
                    MarkDirty();
                }
                else
                {
                    RememberPackage(bp.Race, bp.Package);
                }

                if (packageJob == bp.Job)
                    packageJob = null;
                if (!cancelled && bp.Package.VideoReady && result == DialogResult.OK)
                    SendWithSmash(bp.Race, bp.Package);
            }
            catch (Exception e)
            {
                log.Error("Error after the window of a package in the background.", e);
            }

            ReleaseScreens(bp);
            FinishIfDone(bp);
            FormSwimPackageTray.UpdateFor(bp.Owner);
        }

        /// <summary>The screens of a package in the background closed once its video ended (its PDF does not need them).</summary>
        private void ReleaseScreens(BackgroundPackage bp)
        {
            if (bp.Released || bp.Form.VideoRunning || bp.Form.VideoWaiting)
                return;

            bp.Released = true;
            try
            {
                if (LentScreensCloseAsked != null && bp.Screens.Count > 0)
                    LentScreensCloseAsked(bp.Screens);
            }
            catch (Exception e)
            {
                log.Error("Could not close the screens of a package made in the background.", e);
            }

            try
            {
                if (bp.Host != null && !bp.Host.IsDisposed)
                    bp.Host.Dispose();
            }
            catch (Exception e)
            {
                log.Error("Could not close the window of the screens of a package made in the background.", e);
            }
        }

        /// <summary>Everything of a package in the background ended (its window closed, its PDF came or waited long enough): the analysis lets go.</summary>
        private void FinishIfDone(BackgroundPackage bp)
        {
            if (bp.Finished || !bp.Closed || !bp.Released)
                return;

            if (PdfPending(bp))
            {
                // Its PDF still comes into its folder (as after its window closed): looked at again later.
                if (backgroundWait == null)
                {
                    backgroundWait = new System.Windows.Forms.Timer { Interval = 20000 };
                    backgroundWait.Tick += (s, e) => FinishIfDone(bp);
                }

                backgroundWait.Start();
                return;
            }

            bp.Finished = true;
            if (backgroundWait != null)
            {
                backgroundWait.Stop();
                backgroundWait.Dispose();
                backgroundWait = null;
            }

            try
            {
                if (saveTimer != null)
                    saveTimer.Stop();
                SaveNow();
                if (client != null)
                    client.StateChanged -= Client_StateChanged;
                if (saveTimer != null)
                    saveTimer.Stop();
                FormSwimAnalysis window = form;
                form = null;
                if (window != null && !window.IsDisposed)
                {
                    window.FormClosed -= Form_FormClosed;
                    window.Dispose();
                }

                if (groupDashboard != null && !groupDashboard.IsDisposed)
                    groupDashboard.Dispose();
                groupDashboard = null;
            }
            catch (Exception e)
            {
                log.Error("Could not let go of the analysis of a package made in the background.", e);
            }

            lock (backgroundLock)
                background.Remove(bp);
            FormSwimPackageTray.UpdateFor(bp.Owner);
        }

        /// <summary>
        /// What the package changed in the analysis since it went to the background (its race: the PDF, the KOE App, the ready package) goes
        /// to the analysis of the screens: in memory when that video is open there, else into its file. Instead of a save.
        /// </summary>
        private void RouteBackgroundChanges()
        {
            dirty = false;
            if (VideoFile == null || string.IsNullOrEmpty(videoPath))
                return;

            string now;
            try
            {
                now = JsonConvert.SerializeObject(VideoFile);
            }
            catch (Exception e)
            {
                log.Error("Could not read the analysis of a package made in the background.", e);
                return;
            }

            if (routedBaseline == null)
            {
                routedBaseline = now;
                return;
            }

            if (string.Equals(now, routedBaseline, StringComparison.Ordinal))
                return;

            List<SwimFileChange> changes;
            try
            {
                changes = BackgroundChanges(routedBaseline, now);
            }
            catch (Exception e)
            {
                log.Error("Could not compare the analysis of a package made in the background.", e);
                return;
            }

            if (changes.Count == 0)
            {
                routedBaseline = now;
                return;
            }

            SwimAnalysisManager live = instance;
            bool taken = live != null && live != this && !live.retired ? live.TakeBackgroundChanges(videoPath, changes) : WriteBackgroundChanges(videoPath, changes);
            if (taken)
                routedBaseline = now;
            else
                dirty = true;
        }

        /// <summary>
        /// The changes of a package in the background into its analysis: in memory when its video is open in this analysis (saved as any
        /// change), else into its file. True when they went (or there was nothing to change).
        /// </summary>
        private bool TakeBackgroundChanges(string path, List<SwimFileChange> changes)
        {
            if (VideoFile == null || string.IsNullOrEmpty(videoPath) || !string.Equals(videoPath, path, StringComparison.OrdinalIgnoreCase))
                return WriteBackgroundChanges(path, changes);

            try
            {
                JObject current = JObject.FromObject(VideoFile);
                List<SwimFileChange> applied = ApplyChanges(current, changes);
                foreach (SwimFileChange c in applied)
                {
                    object target = c.RaceId == null ? (object)VideoFile : VideoFile.Races.FirstOrDefault(r => r.Id == c.RaceId);
                    if (target != null)
                        SetJsonMember(target, c.Name, c.After);
                }

                if (applied.Count > 0)
                {
                    MarkDirty();
                    if (form != null && !form.IsDisposed)
                        form.RefreshAll();
                }

                return true;
            }
            catch (Exception e)
            {
                log.Error("Could not take what a package made in the background changed in its analysis.", e);
                return false;
            }
        }

        /// <summary>The changes of a package in the background into the file of its analysis (the one Kinovea reads), as a save (whole, a daily copy).</summary>
        private static bool WriteBackgroundChanges(string path, List<SwimFileChange> changes)
        {
            foreach (string candidate in AnalysisFiles(path))
            {
                if (!File.Exists(candidate))
                    continue;

                try
                {
                    SwimVideoFile file = ReadVideoFile(candidate, path);
                    if (file == null)
                        continue;

                    List<SwimFileChange> applied = ApplyChanges(JObject.FromObject(file), changes);
                    if (applied.Count == 0)
                        return true;

                    foreach (SwimFileChange c in applied)
                    {
                        object target = c.RaceId == null ? (object)file : file.Races.FirstOrDefault(r => r.Id == c.RaceId);
                        if (target != null)
                            SetJsonMember(target, c.Name, c.After);
                    }

                    string json = JsonConvert.SerializeObject(file, Formatting.Indented);
                    SwimFiles.BackupDaily(candidate, BackupFolder, BackupPrefix(path), BackupExtension, BackupDays, BackupBytes);
                    SwimFiles.WriteText(candidate, json, Encoding.UTF8);
                    return true;
                }
                catch (Exception e)
                {
                    log.Error("Could not write what a package made in the background changed in its analysis.", e);
                    return false;
                }
            }

            log.Warn("The analysis of a package made in the background has no file to take its changes.");
            return false;
        }

        /// <summary>A value of an analysis (a field of a race or of the file, by its name in the file) set from its JSON (null: none).</summary>
        private static void SetJsonMember(object target, string name, JToken value)
        {
            JsonSerializer serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
            Newtonsoft.Json.Serialization.JsonObjectContract contract = serializer.ContractResolver.ResolveContract(target.GetType()) as Newtonsoft.Json.Serialization.JsonObjectContract;
            if (contract == null)
                return;

            Newtonsoft.Json.Serialization.JsonProperty member = contract.Properties.GetClosestMatchProperty(name);
            if (member == null || member.Ignored || !member.Writable || member.ValueProvider == null)
                return;

            object v = value == null || value.Type == JTokenType.Null ? null : value.ToObject(member.PropertyType, serializer);
            if (v == null && member.PropertyType.IsValueType && Nullable.GetUnderlyingType(member.PropertyType) == null)
                v = Activator.CreateInstance(member.PropertyType);
            member.ValueProvider.SetValue(target, v);
        }
        #endregion

        #region Changes of an analysis (pure, tested by the CI)
        /// <summary>(2.25.0) A value of the analyses of a video changed: at the top of the file (RaceId null) or of a race, before and after.</summary>
        internal sealed class SwimFileChange
        {
            public string RaceId;
            public string Name;
            public JToken Before;
            public JToken After;
        }

        /// <summary>
        /// (2.25.0) What changed between two JSON of the analyses of a video: each value at the top of the file (not its races) and of each
        /// race found in both (by its id), with its value before (a value not there: null).
        /// </summary>
        internal static List<SwimFileChange> BackgroundChanges(string baseline, string now)
        {
            List<SwimFileChange> list = new List<SwimFileChange>();
            JObject b = JObject.Parse(baseline), n = JObject.Parse(now);
            foreach (string name in Names(b, n).Where(x => x != "Races"))
                Diff(null, name, b[name], n[name], list);

            Dictionary<string, JObject> before = RacesOf(b), after = RacesOf(n);
            foreach (KeyValuePair<string, JObject> race in after)
            {
                JObject was;
                if (!before.TryGetValue(race.Key, out was))
                    continue;
                foreach (string name in Names(was, race.Value))
                    Diff(race.Key, name, was[name], race.Value[name], list);
            }

            return list;
        }

        /// <summary>
        /// (2.25.0) The changes applied to the JSON of the analyses as they are now: each only when its value there is still the value before
        /// (a value changed meanwhile, by the analyst, stays as it is), and in a race that is still there. Returns those applied.
        /// </summary>
        internal static List<SwimFileChange> ApplyChanges(JObject current, List<SwimFileChange> changes)
        {
            List<SwimFileChange> applied = new List<SwimFileChange>();
            Dictionary<string, JObject> races = RacesOf(current);
            foreach (SwimFileChange c in changes)
            {
                JObject target = current;
                if (c.RaceId != null && !races.TryGetValue(c.RaceId, out target))
                    continue;

                if (!JToken.DeepEquals(Plain(target[c.Name]), c.Before))
                    continue;

                target[c.Name] = c.After != null ? c.After.DeepClone() : JValue.CreateNull();
                applied.Add(c);
            }

            return applied;
        }

        private static IEnumerable<string> Names(JObject a, JObject b)
        {
            return a.Properties().Select(p => p.Name).Concat(b.Properties().Select(p => p.Name)).Distinct();
        }

        private static void Diff(string race, string name, JToken a, JToken b, List<SwimFileChange> list)
        {
            JToken before = Plain(a), after = Plain(b);
            if (!JToken.DeepEquals(before, after))
                list.Add(new SwimFileChange { RaceId = race, Name = name, Before = before, After = after });
        }

        /// <summary>A value of a JSON, null when it is not there or null.</summary>
        private static JToken Plain(JToken t)
        {
            return t == null || t.Type == JTokenType.Null ? null : t;
        }

        private static Dictionary<string, JObject> RacesOf(JObject file)
        {
            Dictionary<string, JObject> races = new Dictionary<string, JObject>(StringComparer.Ordinal);
            JArray list = file["Races"] as JArray;
            if (list == null)
                return races;

            foreach (JObject r in list.OfType<JObject>())
            {
                string id = (string)r["Id"];
                if (!string.IsNullOrEmpty(id) && !races.ContainsKey(id))
                    races[id] = r;
            }

            return races;
        }
        #endregion

        #region The box of the packages and Kinovea closing
        /// <summary>What the box of the packages shows of a package in the background.</summary>
        internal sealed class BackgroundRow
        {
            public object Key;
            public string Title;
            public BackgroundState State;
            public int Percent;
            public string Line;
            public string Error;
            public bool Noticed;
        }

        internal enum BackgroundState
        {
            Waiting,
            Making,
            Stopping,
            Ready,
            Failed
        }

        /// <summary>The packages in the background whose window is not closed, for the box (UI thread).</summary>
        internal static List<BackgroundRow> BackgroundRows()
        {
            List<BackgroundRow> rows = new List<BackgroundRow>();
            foreach (BackgroundPackage b in Background())
            {
                if (b.Closed || b.Form == null || b.Form.IsDisposed)
                    continue;

                FormSwimPackage f = b.Form;
                SwimPackage p = b.Package;
                BackgroundRow row = new BackgroundRow { Key = b, Title = b.Title, Noticed = b.Noticed };
                if (f.VideoWaiting)
                    row.State = BackgroundState.Waiting;
                else if (f.VideoRunning)
                    row.State = f.VideoCancelled ? BackgroundState.Stopping : BackgroundState.Making;
                else if (f.VideoDone)
                    row.State = BackgroundState.Ready;
                else
                    row.State = BackgroundState.Failed;

                if (row.State == BackgroundState.Making && b.RunningSince == DateTime.MinValue)
                    b.RunningSince = DateTime.Now;
                row.Percent = f.VideoPercent;

                List<string> parts = new List<string>();
                if (row.State == BackgroundState.Making)
                {
                    parts.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Βίντεο {0}%"), row.Percent));
                    double seconds = (DateTime.Now - b.RunningSince).TotalSeconds;
                    if (row.Percent >= 3 && seconds > 3)
                    {
                        TimeSpan left = TimeSpan.FromSeconds(Math.Max(0, seconds * (100 - row.Percent) / row.Percent));
                        parts.Add(string.Format(CultureInfo.InvariantCulture, "~{0}:{1:00}", (int)left.TotalMinutes, left.Seconds));
                    }
                }
                else if (row.State == BackgroundState.Waiting)
                {
                    parts.Add(SwimLang.T("το βίντεο περιμένει τη σειρά του"));
                }
                else if (row.State == BackgroundState.Stopping)
                {
                    parts.Add(SwimLang.T("σταματά…"));
                }
                else if (row.State == BackgroundState.Ready)
                {
                    TimeSpan d = TimeSpan.FromSeconds(Math.Max(0, p.VideoSeconds));
                    parts.Add(string.Format(CultureInfo.InvariantCulture, SwimLang.T("Βίντεο {0}:{1:00}"), (int)d.TotalMinutes, d.Seconds) +
                        (p.VideoScreens >= 2 ? string.Format(CultureInfo.InvariantCulture, SwimLang.T(" · {0} οθόνες μαζί"), p.VideoScreens) : "") +
                        (p.VideoHasSound ? SwimLang.T(" · με ήχο") : ""));
                }
                else
                {
                    row.Error = f.VideoFailure;
                }

                if (row.State != BackgroundState.Failed)
                {
                    if (p.PdfReady)
                        parts.Add("PDF ✓");
                    else if (!string.IsNullOrEmpty(p.PdfError))
                        parts.Add("PDF ⚠");
                    else if (p.PdfWaiting)
                        parts.Add("PDF…");
                    if (p.Archived == true)
                        parts.Add("KOE App ✓");
                    else if (p.Archived == false)
                        parts.Add("KOE App ⚠");
                }

                row.Line = string.Join(" · ", parts.ToArray());
                rows.Add(row);
            }

            return rows;
        }

        /// <summary>The window of a package in the background, from the box (not modal); its notice answered.</summary>
        internal static void OpenBackground(object key)
        {
            BackgroundPackage b = key as BackgroundPackage;
            if (b == null || b.Form == null || b.Form.IsDisposed)
                return;
            b.Noticed = true;
            b.Form.ShowAgain(b.Owner);
        }

        /// <summary>The notice of the end of a package answered («Αργότερα»): it stays in the box.</summary>
        internal static void NoticedBackground(object key)
        {
            BackgroundPackage b = key as BackgroundPackage;
            if (b != null)
                b.Noticed = true;
        }

        /// <summary>
        /// «×» of a package ended in the box: its window closed as with «Κλείσιμο» (the package stays in its folder; «📦 Δημιουργία αποστολής»
        /// of its analysis opens it again, ready).
        /// </summary>
        internal static void CloseBackground(object key)
        {
            BackgroundPackage b = key as BackgroundPackage;
            if (b == null || b.Form == null || b.Form.IsDisposed || b.Form.VideoRunning || b.Form.VideoWaiting)
                return;
            b.Noticed = true;
            b.Form.CloseForGood();
        }

        /// <summary>
        /// Kinovea closes (the screen manager asks): a package still made in the background, or waiting for its PDF, is said first; closing
        /// stops its video (its folder keeps what was made). True: Kinovea can close.
        /// </summary>
        public static bool CanCloseWithBackgroundPackages()
        {
            List<BackgroundPackage> making = Background().Where(b => !b.Finished && b.Form != null && !b.Form.IsDisposed && (b.Form.VideoRunning || b.Form.VideoWaiting)).ToList();
            List<BackgroundPackage> pdf = Background().Where(b => !b.Finished && !making.Contains(b) && PdfPending(b)).ToList();
            if (making.Count == 0 && pdf.Count == 0)
                return true;

            string text;
            if (making.Count > 0)
            {
                text = string.Format(SwimLang.T("Φτιάχνεται ακόμα στο παρασκήνιο η αποστολή: {0}.\n\nΑν κλείσει τώρα το Kinovea, το βίντεο της σταματά (θα χρειαστεί «Νέα δημιουργία»).\n\nΝα κλείσει τώρα;"),
                    string.Join(", ", making.Select(b => "«" + b.Title + "» (" + b.Form.VideoPercent.ToString(CultureInfo.InvariantCulture) + "%)").ToArray()));
            }
            else
            {
                text = string.Format(SwimLang.T("Η αποστολή {0} περιμένει ακόμα το PDF της από το Google Sheet.\n\nΑν κλείσει τώρα το Kinovea, το PDF δεν θα μπει στον φάκελό της (είναι στο KOE App).\n\nΝα κλείσει τώρα;"),
                    string.Join(", ", pdf.Select(b => "«" + b.Title + "»").ToArray()));
            }

            Form owner = Background().Select(b => b.Owner).FirstOrDefault(o => o != null && !o.IsDisposed);
            DialogResult answer = owner != null
                ? MessageBox.Show(owner, text, SwimLang.T("📦 Αποστολή στο παρασκήνιο"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                : MessageBox.Show(text, SwimLang.T("📦 Αποστολή στο παρασκήνιο"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
                return false;

            // Their videos stop (ffmpeg let go), at most a few seconds.
            foreach (BackgroundPackage b in making)
                b.Form.StopForClose();
            DateTime end = DateTime.Now.AddSeconds(10);
            while (DateTime.Now < end && making.Any(b => !b.Form.IsDisposed && b.Form.VideoRunning))
            {
                Application.DoEvents();
                Thread.Sleep(30);
            }

            return true;
        }
        #endregion
    }
}
