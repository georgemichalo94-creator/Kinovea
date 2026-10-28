#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.10.0) The package for the coaches («📦 Δημιουργία αποστολής»): the PDF of the report, the video of the race with the graphics
    /// of the analysis (MP4, at most 1280 wide) and the message (recipients: the coaches of the athlete in the KOE App), in a folder per
    /// competition and club, checked in a preview and then sent by the analyst with Smash (or by email).
    /// </summary>
    public class SwimPackage
    {
        /// <summary>The longer side of the video of the package (pixels).</summary>
        public const int VideoMaxSide = 1280;

        public string RaceId;
        public string Folder;
        public string BaseName;
        public string PdfPath;
        public string VideoPath;
        public string MessagePath;
        public List<string> Recipients = new List<string>();
        public string Subject = "";
        public string Body = "";

        /// <summary>The PDF in Google Drive (also when it could not come into the folder: an older Apps Script).</summary>
        public string PdfUrl;

        /// <summary>The PDF of the folder is the one of the analysis as it is now.</summary>
        public bool PdfReady;

        /// <summary>The PDF is being made in the Google Sheet.</summary>
        public bool PdfWaiting;

        /// <summary>Why the PDF did not come (an error of the sending: it can be asked again), or why there is none (not connected, an older script).</summary>
        public string PdfError;
        public string PdfNote;

        /// <summary>The report in the KOE App («Αρχεία Αθλητών/τριων»): the answer of the Apps Script (null: not asked or not known).</summary>
        public bool? Archived;
        public string ArchiveNote;

        public double VideoSeconds;
        public Size VideoSize;
        public bool VideoReady;
        public bool VideoHasSound;
        public List<SwimPackageThumb> Thumbs = new List<SwimPackageThumb>();

        public string PdfFileName
        {
            get { return BaseName + ".pdf"; }
        }

        public string VideoFileName
        {
            get { return BaseName + ".mp4"; }
        }

        /// <summary>The files to send (the ones there).</summary>
        public string[] Files
        {
            get
            {
                List<string> files = new List<string>();
                if (PdfReady && !string.IsNullOrEmpty(PdfPath) && File.Exists(PdfPath))
                    files.Add(PdfPath);
                if (VideoReady && !string.IsNullOrEmpty(VideoPath) && File.Exists(VideoPath))
                    files.Add(VideoPath);
                return files.ToArray();
            }
        }

        public long FilesBytes
        {
            get { return Files.Sum(f => new FileInfo(f).Length); }
        }

        #region Folder and names
        /// <summary>The folder of all the packages: the one of the settings, else «Αποστολές προπονητών» on the desktop.</summary>
        public static string RootFolder(SwimSettings settings)
        {
            if (settings != null && !string.IsNullOrWhiteSpace(settings.PackageFolder))
                return settings.PackageFolder.Trim();
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), SwimLang.T("Αποστολές προπονητών"));
        }

        /// <summary>
        /// The folder of the package of a race: «‹date› ‹competition›\‹club›» in the folder of the packages, so that all the athletes of
        /// a club in a competition go to their coach together.
        /// </summary>
        public static string FolderFor(SwimRace race, SwimSettings settings)
        {
            DateTime date;
            string day = DateTime.TryParseExact((race.Date ?? "").Trim(), new[] { "d.M.yyyy", "d/M/yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string competition = Clean(race.Competition);
            if (competition.Length == 0)
                competition = race.IsTraining ? SwimLang.T("Τεχνική ανάλυση") : SwimLang.T("Αγώνας");
            string club = Clean(race.AthleteClub);
            if (club.Length == 0)
                club = SwimLang.T("Χωρίς σύλλογο");
            return Path.Combine(RootFolder(settings), Cut(day + " " + competition, 80), Cut(club, 60));
        }

        /// <summary>The name of the files of a race: «‹athlete› - ‹event› - ‹round›» (a technique analysis: «‹athlete› - Τεχνική ‹style› - ‹date›»).</summary>
        public static string BaseNameFor(SwimRace race)
        {
            string athlete = Clean(race.Athlete);
            if (athlete.Length == 0)
                athlete = SwimLang.T("Χωρίς όνομα");
            string name;
            if (race.IsTraining)
            {
                name = athlete + " - " + SwimLang.T("Τεχνική") + " " + SwimPresets.StyleName(race.Style) + " - " + Clean(race.Date).Replace('/', '.');
            }
            else
            {
                name = athlete + " - " + race.DistanceLabel + (race.IsYards ? "yd" : "") + " " + SwimPresets.StyleName(race.Style);
                string round = Clean(race.Round);
                if (round.Length > 0)
                    name += " - " + round;
            }

            return Cut(Clean(name), 110);
        }

        /// <summary>A name for a file or a folder: no characters that Windows refuses, single spaces, no dot or space at the ends.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder b = new StringBuilder(text.Length);
            foreach (char c in text)
                b.Append(bad.Contains(c) || char.IsControl(c) ? ' ' : c);
            string s = System.Text.RegularExpressions.Regex.Replace(b.ToString(), @"\s+", " ");
            return s.Trim(' ', '.');
        }

        private static string Cut(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max).Trim(' ', '.');
        }

        /// <summary>
        /// The names of the files of the package of a race: the ones it had (made again) when they are in its folder, else free ones (a
        /// «(2)» when another race of the folder has the same name).
        /// </summary>
        public static SwimPackage For(SwimRace race, SwimSettings settings)
        {
            SwimPackage p = new SwimPackage { RaceId = race.Id };
            p.Folder = FolderFor(race, settings);
            string wanted = BaseNameFor(race);
            string name = wanted;
            if (!(string.Equals(race.PackageFolder, p.Folder, StringComparison.OrdinalIgnoreCase) && race.PackageBase == wanted))
            {
                for (int i = 2; i < 100 && (File.Exists(Path.Combine(p.Folder, name + ".mp4")) || File.Exists(Path.Combine(p.Folder, name + ".pdf"))); i++)
                    name = wanted + " (" + i.ToString(CultureInfo.InvariantCulture) + ")";
            }

            p.BaseName = name;
            p.PdfPath = Path.Combine(p.Folder, p.PdfFileName);
            p.VideoPath = Path.Combine(p.Folder, p.VideoFileName);
            p.MessagePath = Path.Combine(p.Folder, name + " - " + SwimLang.T("μήνυμα") + ".txt");
            return p;
        }

        /// <summary>The size of the video of the package: the frame at most VideoMaxSide on its longer side, even sides (H.264).</summary>
        public static Size VideoSizeFor(Size frame)
        {
            int w = Math.Max(2, frame.Width), h = Math.Max(2, frame.Height);
            double k = Math.Min(1.0, VideoMaxSide / (double)Math.Max(w, h));
            int width = Math.Max(2, (int)Math.Floor(w * k / 2) * 2);
            int height = Math.Max(2, (int)Math.Floor(h * k / 2) * 2);
            return new Size(width, height);
        }
        #endregion

        #region Message
        /// <summary>«100μ. ελεύθερο» (relays «4x100μ.», yards «100yd»), in the language of the analysis.</summary>
        public static string EventText(SwimRace race)
        {
            string unit = race.IsYards ? "yd" : SwimLang.T("μ.");
            string style = SwimPresets.StyleName(race.Style);
            CultureInfo culture = SwimLang.English ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("el-GR");
            return race.DistanceLabel + unit + " " + (SwimLang.English ? style.ToLowerInvariant() : style.ToLower(culture));
        }

        /// <summary>The subject of the message.</summary>
        public static string SubjectFor(SwimRace race)
        {
            string athlete = string.IsNullOrWhiteSpace(race.Athlete) ? SwimLang.T("Χωρίς όνομα") : race.Athlete.Trim();
            if (race.IsTraining)
                return string.Format(SwimLang.T("Τεχνική ανάλυση: {0} – {1} – {2}"), athlete, SwimPresets.StyleName(race.Style), race.Date);

            string subject = string.Format(SwimLang.T("Ανάλυση αγώνα: {0} – {1}"), athlete, EventText(race));
            string where = string.Join(", ", new[] { race.Round, race.Competition }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray());
            return where.Length > 0 ? subject + " (" + where + ")" : subject;
        }

        /// <summary>
        /// The text of the message: what is sent and to whom it is about (the athlete, the event, the competition), the final time and the
        /// times of the 50s, and that the report is in the KOE App when it went there. The signature of the settings at the end.
        /// </summary>
        public static string BodyFor(SwimRace race, SwimComputed computed, bool archived, string signature)
        {
            string athlete = string.IsNullOrWhiteSpace(race.Athlete) ? SwimLang.T("Χωρίς όνομα") : race.Athlete.Trim();
            string g = (race.AthleteGender ?? "").Trim();
            bool female = g.StartsWith("Γυν", StringComparison.OrdinalIgnoreCase) || g == "F" || g == "W";
            bool male = g.StartsWith("Ανδ", StringComparison.OrdinalIgnoreCase) || g == "M";
            string whose = female ? SwimLang.T("της αθλήτριας") : male ? SwimLang.T("του αθλητή") : SwimLang.T("του/της αθλητή/τριας");

            StringBuilder b = new StringBuilder();
            b.AppendLine(SwimLang.T("Καλησπέρα σας,"));
            b.AppendLine();
            string where = string.Join(", ", new[] { race.Round, race.Competition, race.Location, race.Date }.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).ToArray());
            if (race.IsTraining)
            {
                b.AppendLine(string.Format(SwimLang.T("σας στέλνουμε την τεχνική ανάλυση (εκκινήσεις και στροφές) {0} {1} στο {2}{3}."), whose, athlete,
                    SwimPresets.StyleName(race.Style).ToLowerInvariant(), where.Length > 0 ? " (" + where + ")" : ""));
            }
            else
            {
                b.AppendLine(string.Format(SwimLang.T("σας στέλνουμε την ανάλυση αγώνα {0} {1} στα {2}{3}."), whose, athlete, EventText(race),
                    where.Length > 0 ? " (" + where + ")" : ""));
                if (computed != null && computed.FinalTime.HasValue)
                {
                    b.AppendLine();
                    b.AppendLine(SwimLang.T("Τελικός χρόνος: ") + SwimReference.FormatTime(computed.FinalTime.Value));
                    List<string> fifties = (computed.Blocks ?? new List<SwimBlock>()).Where(x => x.Time.HasValue)
                        .Select(x => SwimReference.FormatTime(x.Time.Value)).ToList();
                    if (fifties.Count >= 2 && fifties.Count <= 8)
                        b.AppendLine(string.Format(SwimLang.T("Ανά 50{0}: "), race.IsYards ? "yd" : SwimLang.T("μ.")) + string.Join(" · ", fifties.ToArray()));
                }
            }

            b.AppendLine();
            b.AppendLine(SwimLang.T("Στην αποστολή:"));
            b.AppendLine(SwimLang.T("• Η αναφορά της ανάλυσης (PDF)"));
            b.AppendLine(race.IsTraining ? SwimLang.T("• Το βίντεο με τα γραφικά της ανάλυσης (MP4)") : SwimLang.T("• Το βίντεο του αγώνα με τα γραφικά της ανάλυσης (MP4)"));
            if (archived)
            {
                b.AppendLine();
                b.AppendLine(SwimLang.T("Η αναφορά είναι και στο KOE App, στα «Αρχεία Αθλητών/τριων»."));
            }

            b.AppendLine();
            b.AppendLine(SwimLang.T("Με εκτίμηση,"));
            if (!string.IsNullOrWhiteSpace(signature))
                b.AppendLine(signature.Trim());
            return b.ToString().TrimEnd() + Environment.NewLine;
        }

        /// <summary>The message file: recipients, subject and text (UTF-8 with BOM, opened well by the Notepad).</summary>
        public void WriteMessage()
        {
            if (string.IsNullOrEmpty(MessagePath))
                return;
            StringBuilder b = new StringBuilder();
            b.AppendLine(SwimLang.T("Προς: ") + string.Join(", ", Recipients.ToArray()));
            b.AppendLine(SwimLang.T("Θέμα: ") + Subject);
            b.AppendLine();
            b.Append(Body);
            Directory.CreateDirectory(Path.GetDirectoryName(MessagePath));
            File.WriteAllText(MessagePath, b.ToString(), new UTF8Encoding(true));
        }

        /// <summary>«12.4 MB».</summary>
        public static string SizeText(long bytes)
        {
            if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        /// <summary>The emails of a text (separated by commas, semicolons, spaces or lines), each once.</summary>
        public static List<string> ParseRecipients(string text)
        {
            List<string> list = new List<string>();
            foreach (string part in (text ?? "").Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string e = part.Trim().Trim('<', '>');
                if (e.IndexOf('@') > 0 && !list.Any(x => string.Equals(x, e, StringComparison.OrdinalIgnoreCase)))
                    list.Add(e);
            }

            return list;
        }
        #endregion
    }

    /// <summary>A picture of the video of the package for the preview: the moment and the image (at most 480 wide).</summary>
    public class SwimPackageThumb
    {
        public string Label;
        public long Timestamp;
        public Bitmap Image;
    }

    /// <summary>
    /// (2.10.0) The video of the package: the images with the graphics given one by one (24-bit BGR, the size of the video) to the
    /// ffmpeg next to the program, which makes an MP4 (H.264, plays on phones and in the browsers), with the sound of the video when
    /// it has some. The errors of ffmpeg are kept for the message.
    /// </summary>
    public sealed class SwimVideoEncoder : IDisposable
    {
        private Process process;
        private Stream input;
        private readonly StringBuilder errors = new StringBuilder();
        private readonly object errorsLock = new object();
        private string output;
        private int frameBytes;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Frames { get; private set; }

        /// <summary>The last lines of ffmpeg (for an error message).</summary>
        public string Errors
        {
            get
            {
                lock (errorsLock)
                {
                    string[] lines = errors.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    return string.Join(" ", lines.Skip(Math.Max(0, lines.Length - 3)).Select(l => l.Trim()).ToArray());
                }
            }
        }

        /// <summary>The arguments of ffmpeg: the images on its input, the sound of the source (seconds from its start), the MP4.</summary>
        public static string Arguments(int width, int height, double fps, string soundSource, double soundFrom, double soundSeconds, string output)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder a = new StringBuilder("-hide_banner -nostats -loglevel error -y ");
            a.AppendFormat(ci, "-f rawvideo -pix_fmt bgr24 -s {0}x{1} -r {2:0.###} -i - ", width, height, fps);
            bool sound = !string.IsNullOrEmpty(soundSource);
            if (sound)
                a.AppendFormat(ci, "-ss {0:0.000} -t {1:0.000} -i \"{2}\" ", Math.Max(0, soundFrom), Math.Max(0.1, soundSeconds), soundSource);
            a.Append("-map 0:v:0 ");
            if (sound)
                a.Append("-map 1:a:0 ");
            // H.264 4:2:0 (every player), quality first (the graphics must stay sharp), the bitrate capped for the sending.
            a.Append("-c:v libx264 -preset veryfast -crf 24 -maxrate 2500k -bufsize 5000k -pix_fmt yuv420p ");
            a.AppendFormat(ci, "-g {0} ", Math.Max(1, (int)Math.Round(fps * 2)));
            // The sound padded with silence and cut at the end of the images: never longer or shorter than the video.
            if (sound)
                a.Append("-af apad -c:a aac -strict -2 -b:a 128k -ac 2 -shortest ");
            a.Append("-movflags +faststart -f mp4 \"" + output + "\"");
            return a.ToString();
        }

        /// <summary>The video has a sound stream (asked to ffmpeg; false when it cannot tell).</summary>
        public static bool HasSound(string ffmpeg, string video)
        {
            if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg) || string.IsNullOrEmpty(video) || !File.Exists(video))
                return false;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(ffmpeg, "-hide_banner -nostdin -i \"" + video + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (Process p = Process.Start(info))
                {
                    Task<string> err = p.StandardError.ReadToEndAsync();
                    Task<string> outText = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(15000))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        return false;
                    }

                    string text = err.Result;
                    return text.Split('\n').Any(l => l.Contains("Stream #") && l.Contains("Audio:"));
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Starts ffmpeg. False (and the error) when it cannot.</summary>
        public bool Start(string ffmpeg, string output, int width, int height, double fps, string soundSource, double soundFrom, double soundSeconds, out string error)
        {
            error = null;
            this.output = output;
            Width = width;
            Height = height;
            frameBytes = width * height * 3;
            if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg))
            {
                error = SwimLang.T("Δεν βρέθηκε το ffmpeg.exe δίπλα στο πρόγραμμα.");
                return false;
            }

            try
            {
                ProcessStartInfo info = new ProcessStartInfo(ffmpeg, Arguments(width, height, fps, soundSource, soundFrom, soundSeconds, output))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    StandardErrorEncoding = Encoding.UTF8
                };
                process = new Process { StartInfo = info };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null)
                        return;
                    lock (errorsLock)
                    {
                        if (errors.Length < 20000)
                            errors.AppendLine(e.Data);
                    }
                };
                process.OutputDataReceived += (s, e) => { };
                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                input = process.StandardInput.BaseStream;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Abort();
                return false;
            }
        }

        /// <summary>One image (24-bit BGR rows, Width x Height, without padding). False when ffmpeg stopped (Errors says why).</summary>
        public bool Write(byte[] bgr)
        {
            if (input == null || bgr == null || bgr.Length < frameBytes)
                return false;
            try
            {
                input.Write(bgr, 0, frameBytes);
                Frames++;
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        /// <summary>The end of the images: ffmpeg finishes the file. False (and the error) when the file is not made.</summary>
        public bool Finish(int timeoutMilliseconds, out string error)
        {
            error = null;
            if (process == null)
            {
                error = SwimLang.T("Το ffmpeg δεν ξεκίνησε.");
                return false;
            }

            try
            {
                input.Flush();
                input.Close();
            }
            catch (Exception)
            {
                // ffmpeg already stopped: its exit code and messages say why.
            }

            input = null;
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                Abort();
                error = SwimLang.T("Το ffmpeg δεν τελείωσε το βίντεο εγκαίρως.");
                return false;
            }

            process.WaitForExit();
            int code = process.ExitCode;
            process.Dispose();
            process = null;
            if (code != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
            {
                error = string.Format(SwimLang.T("Το ffmpeg σταμάτησε ({0}): {1}"), code, Errors);
                try
                {
                    if (File.Exists(output))
                        File.Delete(output);
                }
                catch (Exception)
                {
                }

                return false;
            }

            return true;
        }

        /// <summary>Stops ffmpeg (cancel or error) and removes what it wrote.</summary>
        public void Abort()
        {
            try
            {
                if (input != null)
                    input.Close();
            }
            catch (Exception)
            {
            }

            input = null;
            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception)
                {
                }

                process.Dispose();
                process = null;
            }

            try
            {
                if (!string.IsNullOrEmpty(output) && File.Exists(output))
                    File.Delete(output);
            }
            catch (Exception)
            {
            }
        }

        public void Dispose()
        {
            if (process != null)
                Abort();
        }
    }

    /// <summary>
    /// (2.10.0) The pages of a PDF as images, drawn by Windows itself (Windows.Data.Pdf, Windows 10 and 11) through reflection, without
    /// a library more in the program. Null (and the error) where Windows cannot: the preview then opens the PDF in its program.
    /// </summary>
    public static class SwimPdfPages
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        public static List<Bitmap> Render(string path, int width, int maxPages, out string error)
        {
            error = null;
            List<Bitmap> pages = new List<Bitmap>();
            try
            {
                Type storageFile = WinRt("Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime");
                Type pdfDocument = WinRt("Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType=WindowsRuntime");
                Type pdfPage = WinRt("Windows.Data.Pdf.PdfPage, Windows.Data.Pdf, ContentType=WindowsRuntime");
                Type options = WinRt("Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType=WindowsRuntime");
                Type memory = WinRt("Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType=WindowsRuntime");
                Type randomAccess = WinRt("Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType=WindowsRuntime");
                Assembly runtime = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                Type extensions = runtime.GetType("System.WindowsRuntimeSystemExtensions", true);
                Type streams = runtime.GetType("System.IO.WindowsRuntimeStreamExtensions", true);

                object file = Await(extensions, storageFile.GetMethod("GetFileFromPathAsync", Any).Invoke(null, new object[] { Path.GetFullPath(path) }), storageFile);
                MethodInfo load = pdfDocument.GetMethods(Any).First(m => m.Name == "LoadFromFileAsync" && m.GetParameters().Length == 1);
                object document = Await(extensions, load.Invoke(null, new[] { file }), pdfDocument);
                uint count = (uint)pdfDocument.GetProperty("PageCount", Any).GetValue(document, null);
                MethodInfo getPage = pdfDocument.GetMethod("GetPage", Any);
                MethodInfo render = pdfPage.GetMethods(Any).First(m => m.Name == "RenderToStreamAsync" && m.GetParameters().Length == 2);
                MethodInfo asStream = streams.GetMethods(Any).First(m => m.Name == "AsStream" && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.Name == "IRandomAccessStream");
                for (uint i = 0; i < count && i < maxPages; i++)
                {
                    object page = getPage.Invoke(document, new object[] { i });
                    try
                    {
                        object o = Activator.CreateInstance(options);
                        options.GetProperty("DestinationWidth", Any).SetValue(o, (uint)Math.Max(64, width), null);
                        object stream = Activator.CreateInstance(memory);
                        AwaitAction(extensions, render.Invoke(page, new[] { stream, o }));
                        using (Stream s = (Stream)asStream.Invoke(null, new[] { stream }))
                        {
                            s.Position = 0;
                            using (Image image = Image.FromStream(s))
                                pages.Add(new Bitmap(image));
                        }

                        IDisposable d = stream as IDisposable;
                        if (d != null)
                            d.Dispose();
                    }
                    finally
                    {
                        IDisposable d = page as IDisposable;
                        if (d != null)
                            d.Dispose();
                    }
                }

                if (pages.Count == 0)
                    error = SwimLang.T("Το PDF δεν έχει σελίδες.");
                return pages;
            }
            catch (Exception e)
            {
                while ((e is TargetInvocationException || e is AggregateException) && e.InnerException != null)
                    e = e.InnerException;
                error = e.Message;
                foreach (Bitmap b in pages)
                    b.Dispose();
                return null;
            }
        }

        private static Type WinRt(string name)
        {
            Type t = Type.GetType(name, false);
            if (t == null)
                throw new PlatformNotSupportedException(name.Split(',')[0]);
            return t;
        }

        /// <summary>The result of a Windows asynchronous operation (IAsyncOperation&lt;T&gt;), waited for here (at most a minute).</summary>
        private static object Await(Type extensions, object operation, Type resultType)
        {
            MethodInfo asTask = extensions.GetMethods(Any).First(m => m.Name == "AsTask" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1").MakeGenericMethod(resultType);
            Task task = (Task)asTask.Invoke(null, new[] { operation });
            if (!task.Wait(60000))
                throw new TimeoutException("Windows.Data.Pdf");
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }

        private static void AwaitAction(Type extensions, object action)
        {
            MethodInfo asTask = extensions.GetMethods(Any).First(m => m.Name == "AsTask" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.Name == "IAsyncAction");
            Task task = (Task)asTask.Invoke(null, new[] { action });
            if (!task.Wait(60000))
                throw new TimeoutException("Windows.Data.Pdf");
        }
    }
}
