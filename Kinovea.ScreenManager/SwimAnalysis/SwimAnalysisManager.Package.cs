#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Kinovea.Services;
using Kinovea.Video;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.10.0) «📦 Δημιουργία αποστολής»: the package for the coaches. The PDF of the report is asked like with the PDF button, with the
    /// PDF itself and the emails of the athlete's coaches in the answer (Apps Script 2.10.0, which also puts it in the KOE App), while
    /// the video of the race is made with the graphics of the analysis (the frames of the video export, given to ffmpeg: an MP4 at
    /// most 1280 wide); then the preview and OK: the sending with Smash. The files stay in a folder per competition and club.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>The Apps Script that gives the PDF and the coaches' emails for the package (and puts the PDF in the KOE App).</summary>
        private static readonly Version packageBackend = new Version(2, 10, 0);

        /// <summary>Seconds of the video before the start signal and after the finish (a race); before the first and after the last mark (technique).</summary>
        private const double PackageBefore = 3.0;
        private const double PackageAfter = 4.0;
        private const double PackageTechMargin = 2.0;

        /// <summary>The pictures of the video in the preview (at most).</summary>
        private const int PackageThumbs = 8;

        /// <summary>The package being made (null: none).</summary>
        private PackageJob packageJob;

        /// <summary>The frames of the package are being drawn: the graphics of the analysis on them, whatever is chosen for the video.</summary>
        private volatile bool packageRendering;

        /// <summary>
        /// The packages whose PDF was asked and has not come yet, by race, with the time they asked (also after their window closed: the PDF
        /// still goes into their folder and does not open in the browser). Forgotten after PackagePdfMinutes or at the PDF button.
        /// </summary>
        private readonly Dictionary<string, KeyValuePair<SwimPackage, DateTime>> packagePdfPending = new Dictionary<string, KeyValuePair<SwimPackage, DateTime>>();
        private const double PackagePdfMinutes = 15;

        private sealed class PackageJob
        {
            public string RaceId;
            public SwimPackage Package;
            public FormSwimPackage Form;
        }

        /// <summary>A package can be made now: a race with its video, no reading of the video running, no other package.</summary>
        public bool CanCreatePackage
        {
            get { return Race != null && HasVideo && Timing.IsValid && !ReadingBusy && packageJob == null; }
        }

        /// <summary>
        /// «📦 Δημιουργία αποστολής»: the PDF asked (when the Google Sheet is connected) and the video made with the graphics, in the
        /// window of the package (progress, then preview); OK there opens the sending with Smash.
        /// </summary>
        public void CreatePackage()
        {
            if (Race == null)
                return;

            if (packageJob != null)
            {
                if (packageJob.Form != null && !packageJob.Form.IsDisposed)
                    packageJob.Form.Activate();
                return;
            }

            if (!HasVideo)
            {
                Status(SwimLang.T("Η αποστολή χρειάζεται το βίντεο της ανάλυσης ανοιχτό."), true);
                return;
            }

            if (ReadingBusy)
            {
                Status(SwimLang.T("Η αποστολή γίνεται μόλις τελειώσει η ανάγνωση του βίντεο."), true);
                return;
            }

            if (!Timing.IsValid)
            {
                Status(SwimLang.T("Δεν είναι γνωστός ο ρυθμός καρέ του βίντεο: η αποστολή δεν μπορεί να γίνει."), true);
                return;
            }

            string ffmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpeg))
            {
                Status(SwimLang.T("Δεν βρέθηκε το ffmpeg.exe δίπλα στο πρόγραμμα: το βίντεο της αποστολής δεν μπορεί να γίνει."), true);
                return;
            }

            long from, to;
            PackageRange(out from, out to);
            if (to <= from)
            {
                Status(SwimLang.T("Δεν υπάρχει βίντεο για την αποστολή (η ζώνη εργασίας είναι άδεια)."), true);
                return;
            }

            SwimRace race = Race;
            SwimPackage package = SwimPackage.For(race, Settings);
            try
            {
                Directory.CreateDirectory(package.Folder);
            }
            catch (Exception e)
            {
                Status(SwimLang.T("Ο φάκελος της αποστολής δεν έγινε: ") + e.Message, true);
                return;
            }

            package.Subject = SwimPackage.SubjectFor(race);
            package.Body = SwimPackage.BodyFor(race, Computed, false, Settings.PackageSignature);
            race.PackageFolder = package.Folder;
            race.PackageBase = package.BaseName;
            race.PackageMade = DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            MarkDirty();

            PackageJob job = new PackageJob { RaceId = race.Id, Package = package };
            packageJob = job;

            // The PDF as with the PDF button (its graphics and charts first), with the PDF and the coaches' emails in the answer.
            if (Settings.IsConfigured)
            {
                package.PdfWaiting = true;
                packagePdfPending[race.Id] = new KeyValuePair<SwimPackage, DateTime>(package, DateTime.Now);
                SendByUser(true);
            }
            else
            {
                package.PdfNote = SwimLang.T("Χωρίς σύνδεση με το Google Sheet (καρτέλα «Σύνδεση»): η αποστολή χωρίς PDF.");
            }

            List<KeyValuePair<long, string>> moments = PackageMoments(race, from, to);
            Form owner = player != null && !player.IsDisposed ? player.FindForm() : null;
            FormSwimPackage form = new FormSwimPackage(package, race.DisplayName, Settings.PackageSignature,
                worker => RenderPackageVideo(package, from, to, moments, worker),
                () =>
                {
                    packageRendering = true;
                    player.BeforeExportVideo();
                },
                () =>
                {
                    packageRendering = false;
                    frameServer.VideoReader.AfterFrameEnumeration();
                    if (player != null && !player.IsDisposed)
                        player.AfterExportVideo();
                },
                () => RetryPackagePdf(job),
                (archived, signature) => SwimPackage.BodyFor(race, Computed, archived, signature));
            job.Form = form;
            DialogResult result;
            try
            {
                result = owner != null ? form.ShowDialog(owner) : form.ShowDialog();
            }
            finally
            {
                job.Form = null;
                packageJob = null;
                packageRendering = false;
            }

            if (!string.Equals(form.Signature, Settings.PackageSignature ?? "", StringComparison.Ordinal))
            {
                Settings.PackageSignature = form.Signature;
                SaveSettingsQuietly();
            }

            bool cancelled = form.VideoCancelled;
            form.Dispose();
            if (cancelled)
            {
                Status(SwimLang.T("Η αποστολή σταμάτησε."), false);
                return;
            }

            if (result != DialogResult.OK)
            {
                Status(string.Format(SwimLang.T("Η αποστολή είναι στον φάκελο «{0}»."), package.Folder), false);
                return;
            }

            // OK: Smash in the browser, the emails in the clipboard for its first field, and the window with the files and the texts.
            try
            {
                if (package.Recipients.Count > 0)
                    Clipboard.SetText(string.Join(", ", package.Recipients.ToArray()));
            }
            catch (Exception e)
            {
                log.Error("Could not copy the recipients of the package.", e);
            }

            FormSwimPackage.Open("https://fromsmash.com/");
            FormSwimSmash smash = new FormSwimSmash(package, () => PackageSent(race, package));
            smash.Show();
            Status(SwimLang.T("📤 Smash: σύρε τα αρχεία από το παράθυρο της αποστολής στη σελίδα, επικόλλησε τα email (είναι ήδη στο πρόχειρο) και πάτα Αποστολή."), false);
        }

        /// <summary>The settings saved without a message (the signature of the messages).</summary>
        private void SaveSettingsQuietly()
        {
            try
            {
                Settings.Save();
            }
            catch (Exception e)
            {
                log.Error("Could not save the settings of the swim analysis.", e);
            }
        }

        /// <summary>«✓ Στάλθηκε» in the window of Smash: kept with the race.</summary>
        private void PackageSent(SwimRace race, SwimPackage package)
        {
            race.PackageSent = DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            MarkDirty();
            Status(string.Format(SwimLang.T("✓ Η αποστολή σημειώθηκε ως σταλμένη ({0})."), race.PackageSent), false);
        }

        /// <summary>The PDF asked again from the window of the package (after an error of the sending).</summary>
        private void RetryPackagePdf(PackageJob job)
        {
            if (packageJob != job || Race == null || Race.Id != job.RaceId || !Settings.IsConfigured)
                return;
            job.Package.PdfError = null;
            job.Package.PdfWaiting = true;
            packagePdfPending[job.RaceId] = new KeyValuePair<SwimPackage, DateTime>(job.Package, DateTime.Now);
            SendByUser(true);
        }

        /// <summary>
        /// The PDF buttons: the parts of the PDF are chosen (2.11.0: short analysis, full, both), then the PDF of the race opens as always
        /// (a package that still waited for it lets it go).
        /// </summary>
        public void PdfByUser()
        {
            if (Race != null && !Race.IsTraining && Settings.IsConfigured && !ChoosePdfParts())
                return;
            if (Race != null && packageJob == null)
                packagePdfPending.Remove(Race.Id);
            SendByUser(true);
        }

        /// <summary>(2.11.0) The parts of the PDF chosen by the analyst, kept in the settings (the package uses them too). False: cancelled.</summary>
        private bool ChoosePdfParts()
        {
            Form owner = player != null && !player.IsDisposed ? player.FindForm() : null;
            using (FormSwimPdfChoice choice = new FormSwimPdfChoice(Settings.PdfShort, Settings.PdfFull))
            {
                DialogResult result = owner != null ? choice.ShowDialog(owner) : choice.ShowDialog();
                if (result != DialogResult.OK || (!choice.Short && !choice.Full))
                    return false;
                if (choice.Short != Settings.PdfShort || choice.Full != Settings.PdfFull)
                {
                    Settings.PdfShort = choice.Short;
                    Settings.PdfFull = choice.Full;
                    SaveSettingsQuietly();
                }

                return true;
            }
        }

        /// <summary>The package of a race that waits for its PDF (null: none, or asked too long ago).</summary>
        private SwimPackage PackageWaitingPdf(SwimRace race)
        {
            KeyValuePair<SwimPackage, DateTime> pending;
            if (race == null || !packagePdfPending.TryGetValue(race.Id, out pending))
                return null;
            if ((DateTime.Now - pending.Value).TotalMinutes > PackagePdfMinutes && (packageJob == null || packageJob.RaceId != race.Id))
            {
                packagePdfPending.Remove(race.Id);
                return null;
            }

            return pending.Key;
        }

        /// <summary>The flags of the requests of a race while its package waits for the PDF: the PDF itself and the coaches' emails.</summary>
        private JObject PackageFlags(SwimRace race)
        {
            if (PackageWaitingPdf(race) == null)
                return null;
            JObject flags = new JObject();
            flags["pdfData"] = true;
            flags["coaches"] = true;
            return flags;
        }

        /// <summary>
        /// The answer of the PDF of a race whose package waits for it: the PDF into the folder (not opened in the browser), the coaches'
        /// emails as the recipients, the line of the KOE App (in the window of the package, or in the status when it is closed). False
        /// when it is not for a package (the PDF opens as always).
        /// </summary>
        private bool PackageTakesPdf(SwimRace race, SwimSheetResult result)
        {
            SwimPackage p = PackageWaitingPdf(race);
            if (p == null)
                return false;

            packagePdfPending.Remove(race.Id);
            PackageJob job = packageJob != null && packageJob.Package == p ? packageJob : null;
            p.PdfWaiting = false;
            p.PdfError = null;
            p.PdfUrl = result.PdfUrl;
            JObject pdf = result.Raw != null ? result.Raw["pdf"] as JObject : null;
            string data = pdf != null ? pdf.Value<string>("data") : null;
            if (!string.IsNullOrEmpty(data))
            {
                try
                {
                    WritePackagePdf(p, Convert.FromBase64String(data));
                }
                catch (Exception e)
                {
                    log.Error("Could not write the PDF of the package.", e);
                    p.PdfError = SwimLang.T("Το PDF δεν γράφτηκε στον φάκελο: ") + e.Message;
                }
            }
            else
            {
                string version = result.Raw != null ? result.Raw.Value<string>("version") : null;
                p.PdfNote = string.Format(SwimLang.T("Το Apps Script του KOE App είναι η έκδοση {0}: το PDF δεν έρχεται στον φάκελο («Άνοιγμα PDF» το ανοίγει από το Drive). Βάλε την έκδοση {1} (δες τον οδηγό, «Ενημέρωση του script»)."),
                    string.IsNullOrEmpty(version) ? "?" : version, packageBackend.ToString(3));
            }

            JArray coaches = result.Raw != null ? result.Raw["coaches"] as JArray : null;
            if (coaches != null)
            {
                foreach (string email in coaches.Select(c => (string)c).Where(c => !string.IsNullOrWhiteSpace(c)))
                {
                    if (!p.Recipients.Any(x => string.Equals(x, email.Trim(), StringComparison.OrdinalIgnoreCase)))
                        p.Recipients.Add(email.Trim());
                }
            }

            JObject archive = result.Raw != null ? result.Raw["archive"] as JObject : null;
            if (archive != null)
            {
                p.Archived = archive.Value<bool?>("ok") == true;
                p.ArchiveNote = p.Archived == true ? SwimLang.T("✓ Καταχωρίστηκε στο KOE App («Αρχεία Αθλητών/τριων»).") :
                    "⚠ " + SwimLang.T("Όχι στο KOE App: ") + (archive.Value<string>("message") ?? "");
            }

            if (job != null && job.Form != null && !job.Form.IsDisposed)
                job.Form.PdfChanged();
            else if (p.PdfReady)
                Status(string.Format(SwimLang.T("📦 Το PDF της αποστολής μπήκε στον φάκελό της: «{0}»."), p.PdfPath), false);
            return true;
        }

        /// <summary>The PDF in the folder of the package (in place of the one before; another name when that one is open in a program).</summary>
        private static void WritePackagePdf(SwimPackage p, byte[] bytes)
        {
            if (bytes == null || bytes.Length < 5 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F')
                throw new InvalidDataException(SwimLang.T("η απάντηση δεν είναι PDF"));

            string temp = p.PdfPath + ".part";
            File.WriteAllBytes(temp, bytes);
            string target = ReplaceFile(temp, p.PdfPath);
            p.PdfPath = target;
            p.PdfReady = true;
        }

        /// <summary>A file made (temp) in place of its target; when the target is open in a program, a free name next to it. Returns where.</summary>
        private static string ReplaceFile(string temp, string target)
        {
            for (int i = 1; i < 20; i++)
            {
                string path = i == 1 ? target : Path.Combine(Path.GetDirectoryName(target),
                    Path.GetFileNameWithoutExtension(target) + " (" + i.ToString(CultureInfo.InvariantCulture) + ")" + Path.GetExtension(target));
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                    File.Move(temp, path);
                    return path;
                }
                catch (IOException)
                {
                    // Open in a program (a player, a PDF reader): the next name.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            throw new IOException(SwimLang.T("Το αρχείο είναι ανοιχτό σε άλλο πρόγραμμα: ") + Path.GetFileName(target));
        }

        /// <summary>An error of the sending of a race whose package waits for its PDF: the window says it, the PDF can be asked again.</summary>
        private void PackageSyncFailed(SwimSyncEventArgs e)
        {
            PackageJob job = packageJob;
            if (job == null || e == null || e.RaceId != job.RaceId || !job.Package.PdfWaiting)
                return;
            if (e.State != SwimSyncState.Error && e.State != SwimSyncState.Offline)
                return;

            job.Package.PdfError = string.IsNullOrEmpty(e.Message) ? SwimLang.T("Το PDF δεν έγινε.") : e.Message;
            if (job.Form != null && !job.Form.IsDisposed)
                job.Form.PdfChanged();
        }

        /// <summary>
        /// The part of the video of the package: a race from 3 s before the signal to 4 s after the finish (or the last mark); a technique
        /// analysis from 2 s before its first mark to 2 s after its last; else the working zone. Always inside the working zone.
        /// </summary>
        private void PackageRange(out long from, out long to)
        {
            SwimRace race = Race;
            VideoSection zone = frameServer.VideoReader.WorkingZone;
            long a = zone.Start, b = zone.End;
            List<long> marks = race.Marks.Select(m => m.Timestamp).ToList();
            if (!race.IsTraining && race.StartTimestamp.HasValue)
            {
                a = race.StartTimestamp.Value - RealSpan(PackageBefore);
                SwimMark finish = race.GetSplit(race.Distance);
                if (finish != null)
                    b = finish.Timestamp + RealSpan(PackageAfter);
                else if (marks.Count > 0)
                    b = Math.Max(marks.Max(), race.StartTimestamp.Value) + RealSpan(PackageAfter);
            }
            else if (marks.Count > 0)
            {
                a = marks.Min() - RealSpan(PackageTechMargin);
                b = marks.Max() + RealSpan(PackageTechMargin);
            }

            from = Math.Max(zone.Start, Math.Min(a, zone.End));
            to = Math.Min(zone.End, Math.Max(b, from));
        }

        /// <summary>The moments of the pictures of the preview: the start, the 15, the walls (a few) and the finish; else evenly.</summary>
        private List<KeyValuePair<long, string>> PackageMoments(SwimRace race, long from, long to)
        {
            List<KeyValuePair<long, string>> list = new List<KeyValuePair<long, string>>();
            string unit = race.IsYards ? "yd" : SwimLang.T("μ.");
            if (!race.IsTraining && race.StartTimestamp.HasValue)
            {
                list.Add(new KeyValuePair<long, string>(race.StartTimestamp.Value + RealSpan(0.8), SwimLang.T("Εκκίνηση")));
                SwimMark m15 = race.GetSplit(15);
                if (m15 != null)
                    list.Add(new KeyValuePair<long, string>(m15.Timestamp, "15" + unit));
                List<SwimMark> walls = new List<SwimMark>();
                for (int d = race.Pool; d < race.Distance; d += race.Pool)
                {
                    SwimMark w = race.GetSplit(d);
                    if (w != null)
                        walls.Add(w);
                }

                int keep = PackageThumbs - 3;
                for (int i = 0; i < walls.Count && keep > 0; i++)
                {
                    // At most a few walls, spread over the race.
                    if (walls.Count > keep && (i * keep) / walls.Count == ((i - 1) * keep) / walls.Count && i > 0)
                        continue;
                    list.Add(new KeyValuePair<long, string>(walls[i].Timestamp, walls[i].Distance.ToString(CultureInfo.InvariantCulture) + unit));
                }

                SwimMark finish = race.GetSplit(race.Distance);
                if (finish != null)
                    list.Add(new KeyValuePair<long, string>(finish.Timestamp, SwimLang.T("Τερματισμός")));
            }

            if (list.Count < 3)
            {
                list.Clear();
                double tsPerSecond = Math.Max(1e-9, frameServer.VideoReader.Info.AverageTimeStampsPerSeconds);
                for (int i = 0; i < 6; i++)
                {
                    long t = from + (long)Math.Round((to - from) * (i + 0.5) / 6.0);
                    TimeSpan at = TimeSpan.FromSeconds((t - from) / tsPerSecond);
                    list.Add(new KeyValuePair<long, string>(t, string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", (int)at.TotalMinutes, at.Seconds)));
                }
            }

            return list.Where(kv => kv.Key >= from && kv.Key <= to).OrderBy(kv => kv.Key).ToList();
        }

        /// <summary>
        /// The video of the package (on the thread of the window's worker): each frame of the part with the graphics of the analysis (as
        /// the video export draws it), at most 1280 wide, given to ffmpeg; the sound of the video when it has one (not for a video in
        /// slow motion). The pictures of the preview at the moments. Returns the error, null when done (or stopped: the window knows).
        /// </summary>
        private string RenderPackageVideo(SwimPackage package, long from, long to, List<KeyValuePair<long, string>> moments, BackgroundWorker worker)
        {
            string ffmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            VideoReader reader = frameServer.VideoReader;
            double tsPerSecond = reader.Info.AverageTimeStampsPerSeconds;
            double tsPerFrame = reader.Info.AverageTimeStampsPerFrame;
            double fps = tsPerSecond > 0 && tsPerFrame > 0 ? tsPerSecond / tsPerFrame : 1000.0 / Math.Max(1e-6, Timing.FrameIntervalMilliseconds);
            double hsf = Timing.HighSpeedFactor <= 0 ? 1.0 : Timing.HighSpeedFactor;
            worker.ReportProgress(0, SwimLang.T("Το βίντεο γίνεται με τα γραφικά…"));
            bool sound = Math.Abs(hsf - 1) < 0.01 && tsPerSecond > 0 && SwimVideoEncoder.HasSound(ffmpeg, videoPath);
            int total = (int)Math.Max(1, Math.Round((to - from) / Math.Max(1.0, tsPerFrame))) + 1;
            string temp = package.VideoPath + ".part";
            Size size = Size.Empty;
            byte[] buffer = null;
            Bitmap flushed = null;
            int count = 0, moment = 0;
            DateTime began = DateTime.Now;
            using (SwimVideoEncoder encoder = new SwimVideoEncoder())
            {
                try
                {
                    reader.BeforeFrameEnumeration();
                    long current = reader.Current != null ? reader.Current.Timestamp : from;
                    bool hasMore = reader.MoveTo(current, from);
                    bool started = false;
                    while (true)
                    {
                        if (worker.CancellationPending)
                        {
                            encoder.Abort();
                            return null;
                        }

                        VideoFrame frame = reader.Current;
                        if (frame == null || frame.Image == null || frame.Timestamp > to)
                            break;

                        if (!started)
                        {
                            size = SwimPackage.VideoSizeFor(frame.Image.Size);
                            buffer = new byte[size.Width * size.Height * 3];
                            double soundFrom = (frame.Timestamp - reader.Info.FirstTimeStamp) / Math.Max(1e-9, tsPerSecond);
                            double soundSeconds = (to - frame.Timestamp) / Math.Max(1e-9, tsPerSecond) + 2.0 / fps;
                            string error;
                            if (!encoder.Start(ffmpeg, temp, size.Width, size.Height, fps, sound ? videoPath : null, soundFrom, soundSeconds, out error))
                                return error;
                            package.VideoSize = size;
                            package.VideoHasSound = sound;
                            started = true;
                        }

                        if (flushed == null || flushed.Width != frame.Image.Width || flushed.Height != frame.Image.Height)
                        {
                            if (flushed != null)
                                flushed.Dispose();
                            flushed = new Bitmap(frame.Image.Width, frame.Image.Height, PixelFormat.Format24bppRgb);
                        }

                        player.GetFlushedImage(frame, flushed);
                        using (OpenCvSharp.Mat bgr = SwimStrokeVideo.ToWorking(flushed, size.Width, size.Height))
                        {
                            CopyBgr(bgr, buffer, size);
                            if (moment < moments.Count && frame.Timestamp >= moments[moment].Key)
                            {
                                string label = moments[moment].Value;
                                while (moment < moments.Count && frame.Timestamp >= moments[moment].Key)
                                    moment++;
                                SwimPackageThumb thumb = new SwimPackageThumb { Label = label, Timestamp = frame.Timestamp, Image = Thumb(bgr, size) };
                                package.Thumbs.Add(thumb);
                                worker.ReportProgress(Percent(count, total), thumb);
                            }
                        }

                        if (!encoder.Write(buffer))
                        {
                            // ffmpeg stopped taking images: a video it finished well is kept (the images to here), else its error.
                            string stopped;
                            if (!encoder.Finish(60000, out stopped))
                                return string.Format(SwimLang.T("Το ffmpeg σταμάτησε: {0}"), string.IsNullOrEmpty(stopped) ? encoder.Errors : stopped);
                            log.WarnFormat("Swim package video: ffmpeg ended after {0} of {1} frames.", count, total);
                            package.VideoPath = ReplaceFile(temp, package.VideoPath);
                            package.VideoSeconds = count / fps;
                            package.VideoReady = true;
                            return null;
                        }

                        count++;
                        if (count % 5 == 0)
                        {
                            double seconds = (DateTime.Now - began).TotalSeconds;
                            double left = count > 0 ? seconds * (total - count) / count : 0;
                            worker.ReportProgress(Percent(count, total), string.Format(CultureInfo.InvariantCulture,
                                SwimLang.T("Το βίντεο γίνεται με τα γραφικά: καρέ {0}/{1} · απομένουν ~{2} δλ."), count, total, Math.Max(0, (int)Math.Round(left))));
                        }

                        if (!hasMore)
                            break;
                        hasMore = reader.MoveNext(0, true);
                    }

                    if (!started)
                        return SwimLang.T("Δεν διαβάστηκε κανένα καρέ του βίντεο.");

                    worker.ReportProgress(99, SwimLang.T("Το βίντεο κλείνει…"));
                    string finishError;
                    if (!encoder.Finish(300000, out finishError))
                        return finishError;

                    package.VideoPath = ReplaceFile(temp, package.VideoPath);
                    package.VideoSeconds = count / fps;
                    package.VideoReady = true;
                    log.InfoFormat("Swim package video: {0} frames in {1:0.0} s ({2}x{3}, sound {4}).", count, (DateTime.Now - began).TotalSeconds, size.Width, size.Height, sound);
                    return null;
                }
                catch (Exception e)
                {
                    log.Error("The video of the package failed.", e);
                    encoder.Abort();
                    return e.Message;
                }
                finally
                {
                    if (flushed != null)
                        flushed.Dispose();
                }
            }
        }

        private static int Percent(int count, int total)
        {
            return Math.Max(0, Math.Min(98, (int)(100.0 * count / Math.Max(1, total))));
        }

        /// <summary>The rows of an image (8-bit BGR) without their padding.</summary>
        private static void CopyBgr(OpenCvSharp.Mat bgr, byte[] buffer, Size size)
        {
            int row = size.Width * 3;
            if (bgr.IsContinuous() && (long)bgr.Step() == row)
            {
                Marshal.Copy(bgr.Data, buffer, 0, row * size.Height);
                return;
            }

            for (int y = 0; y < size.Height; y++)
                Marshal.Copy(bgr.Ptr(y), buffer, y * row, row);
        }

        /// <summary>A picture of the preview (480 wide at most) from an image of the video of the package.</summary>
        private static Bitmap Thumb(OpenCvSharp.Mat bgr, Size size)
        {
            int width = Math.Min(480, size.Width);
            int height = Math.Max(2, (int)Math.Round(width * (double)size.Height / Math.Max(1, size.Width)));
            using (OpenCvSharp.Mat small = new OpenCvSharp.Mat())
            {
                OpenCvSharp.Cv2.Resize(bgr, small, new OpenCvSharp.Size(width, height), 0, 0, OpenCvSharp.InterpolationFlags.Area);
                return OpenCvSharp.Extensions.BitmapConverter.ToBitmap(small);
            }
        }

        /// <summary>(2.11.0) A part of the PDF made or not (the answer of the Apps Script: pdf.short, pdf.notes); null when not said.</summary>
        private static bool? PdfPartMade(SwimSheetResult result, string part)
        {
            JObject pdf = result != null && result.Raw != null ? result.Raw["pdf"] as JObject : null;
            return pdf != null ? pdf.Value<bool?>(part) : null;
        }

        /// <summary>
        /// (2.10.0) The PDF of the report in the KOE App («Αρχεία Αθλητών/τριων», Apps Script 2.10.0): a short line in the status. Athletes
        /// outside the KOE registry (international) are not said; an older script says nothing.
        /// </summary>
        private void ArchiveStatus(SwimRace race, SwimSheetResult result)
        {
            JObject archive = result != null && result.Raw != null ? result.Raw["archive"] as JObject : null;
            if (archive == null || race == null)
                return;

            if (archive.Value<bool?>("ok") == true)
            {
                Status(SwimLang.T("✓ Το PDF μπήκε στο «Αρχεία Αθλητών/τριων» του KOE App, με τον αριθμό μητρώου του αθλητή."), false);
                return;
            }

            string reason = archive.Value<string>("reason") ?? "";
            string id = (race.AthleteId ?? "").Trim();
            bool international = id.StartsWith("WA", StringComparison.OrdinalIgnoreCase) || id.StartsWith("X-", StringComparison.OrdinalIgnoreCase);
            if (reason == "registry" && international)
                return;
            Status(SwimLang.T("Το PDF δεν μπήκε στο KOE App: ") + (archive.Value<string>("message") ?? reason), true);
        }
    }
}
