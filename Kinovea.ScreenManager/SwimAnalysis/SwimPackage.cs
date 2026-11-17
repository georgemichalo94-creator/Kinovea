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
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

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

        /// <summary>(2.12.0) The short analysis in its own file (the PDF «σε δύο αρχεία»), next to the full one.</summary>
        public string PdfShortPath;
        public bool PdfShortReady;
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

        /// <summary>(2.16.4) The screens in the video: all the screens of a group joined (2 or more), else 1.</summary>
        public int VideoScreens = 1;
        public List<SwimPackageThumb> Thumbs = new List<SwimPackageThumb>();

        /// <summary>(2.24.3) A package made before (Ready): the moments of the pictures of its video, taken from it by ffmpeg.</summary>
        public List<SwimPackageMoment> Moments = new List<SwimPackageMoment>();

        /// <summary>(2.24.3) The recipients, the subject and the text came from the message file of the package.</summary>
        public bool MessageKept;

        public string PdfFileName
        {
            get { return BaseName + ".pdf"; }
        }

        public string PdfShortFileName
        {
            get { return BaseName + SwimLang.T(" - Σύντομη ανάλυση") + ".pdf"; }
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
                if (PdfShortReady && !string.IsNullOrEmpty(PdfShortPath) && File.Exists(PdfShortPath))
                    files.Add(PdfShortPath);
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
            string folder = FolderFor(race, settings);
            string wanted = BaseNameFor(race);
            string name = wanted;
            if (!(string.Equals(race.PackageFolder, folder, StringComparison.OrdinalIgnoreCase) && race.PackageBase == wanted))
            {
                for (int i = 2; i < 100 && (File.Exists(Path.Combine(folder, name + ".mp4")) || File.Exists(Path.Combine(folder, name + ".pdf"))); i++)
                    name = wanted + " (" + i.ToString(CultureInfo.InvariantCulture) + ")";
            }

            return At(race, folder, name);
        }

        /// <summary>(2.24.3) The package of a race in a folder with the name of its files (the paths of its files).</summary>
        public static SwimPackage At(SwimRace race, string folder, string name)
        {
            SwimPackage p = new SwimPackage { RaceId = race.Id, Folder = folder, BaseName = name };
            p.PdfPath = Path.Combine(p.Folder, p.PdfFileName);
            p.PdfShortPath = Path.Combine(p.Folder, p.PdfShortFileName);
            p.VideoPath = Path.Combine(p.Folder, p.VideoFileName);
            p.MessagePath = Path.Combine(p.Folder, name + " - " + SwimLang.T("μήνυμα") + ".txt");
            return p;
        }

        /// <summary>
        /// (2.24.3) The package made before for a race, as it is in its folder, for its window without making it again (null: none, or its
        /// video is not there any more): the video (what the race kept about it, else what ffmpeg reads in it, and the moments of its
        /// pictures), the PDF, and the recipients, the subject and the text of its message file.
        /// </summary>
        public static SwimPackage Ready(SwimRace race, string ffmpeg)
        {
            if (race == null || string.IsNullOrWhiteSpace(race.PackageFolder) || string.IsNullOrWhiteSpace(race.PackageBase))
                return null;

            SwimPackage p;
            try
            {
                p = At(race, race.PackageFolder, race.PackageBase);
                if (!File.Exists(p.VideoPath))
                    return null;
            }
            catch (ArgumentException)
            {
                // A folder or a name the paths do not take.
                return null;
            }

            p.VideoReady = true;
            SwimPackageInfo info = race.PackageInfo;
            if (info != null && info.Width > 0 && info.Height > 0)
            {
                p.VideoSeconds = info.Seconds;
                p.VideoSize = new Size(info.Width, info.Height);
                p.VideoHasSound = info.Sound;
                p.VideoScreens = Math.Max(1, info.Screens);
                p.Archived = info.Archived;
                p.ArchiveNote = info.ArchiveNote;
            }
            else
            {
                // A package of an earlier version: what ffmpeg reads in its video.
                double seconds;
                Size size;
                bool sound;
                if (SwimVideoEncoder.Probe(ffmpeg, p.VideoPath, out seconds, out size, out sound))
                {
                    p.VideoSeconds = seconds;
                    p.VideoSize = size;
                    p.VideoHasSound = sound;
                }
            }

            p.Moments = info != null && info.Pictures != null && info.Pictures.Count > 0
                ? info.Pictures.Where(m => m != null && m.Seconds >= 0).ToList()
                : EvenMoments(p.VideoSeconds);
            p.PdfReady = File.Exists(p.PdfPath);
            p.PdfShortReady = File.Exists(p.PdfShortPath);
            if (!p.PdfReady)
                p.PdfNote = SwimLang.T("Το PDF δεν είναι στον φάκελο της αποστολής: η «Νέα δημιουργία» το φτιάχνει.");
            p.MessageKept = p.ReadMessage();
            return p;
        }

        /// <summary>(2.24.3) Six moments spread over a video of that many seconds, named by their time («0:12»); none without its length.</summary>
        public static List<SwimPackageMoment> EvenMoments(double seconds)
        {
            List<SwimPackageMoment> list = new List<SwimPackageMoment>();
            if (seconds <= 0)
                return list;
            for (int i = 0; i < 6; i++)
            {
                double at = seconds * (i + 0.5) / 6.0;
                TimeSpan t = TimeSpan.FromSeconds(at);
                list.Add(new SwimPackageMoment { Seconds = at, Label = string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", (int)t.TotalMinutes, t.Seconds) });
            }

            return list;
        }

        /// <summary>
        /// (2.24.3) The signature of the analysis of a race: all that it keeps (the marks, the values, the notes, the evaluations, the
        /// comparisons…) but what the program keeps about its sending (the Sheet, the PDF, the images of the report, the package) and the
        /// proposals of the reading. Another one: the analysis changed.
        /// </summary>
        public static string Signature(SwimRace race)
        {
            if (race == null)
                return "";
            JObject o = JObject.FromObject(race);
            foreach (string name in new[] { "Graphics", "ChartImages", "SheetName", "SheetUrl", "LastPdfUrl", "SheetPending", "PackageFolder",
                "PackageBase", "PackageMade", "PackageSent", "PackageInfo", "Proposals", "QuietProposals", "QuietRead" })
                o.Remove(name);
            using (SHA1 sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(o.ToString(Newtonsoft.Json.Formatting.None)));
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 20);
            }
        }

        /// <summary>(2.24.3) The analysis changed after its package was made: null when it is not known (a package of an earlier version).</summary>
        public static bool? ChangedSince(SwimRace race)
        {
            if (race == null || race.PackageInfo == null || string.IsNullOrEmpty(race.PackageInfo.Sig))
                return null;
            return !string.Equals(race.PackageInfo.Sig, Signature(race), StringComparison.Ordinal);
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
            return BodyFor(race, computed, archived, signature, 1);
        }

        /// <summary>(2.16.4) The message, with the video of all the screens of a group together when screens is 2 or more.</summary>
        public static string BodyFor(SwimRace race, SwimComputed computed, bool archived, string signature, int screens)
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
            if (screens >= 2)
                b.AppendLine(race.IsTraining ? SwimLang.T("• Το βίντεο με τα γραφικά της ανάλυσης, όλες οι οθόνες μαζί (MP4)")
                    : SwimLang.T("• Το βίντεο του αγώνα με τα γραφικά της ανάλυσης, όλες οι οθόνες μαζί (MP4)"));
            else
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

        /// <summary>
        /// (2.24.3) The recipients, the subject and the text from the message file (as WriteMessage writes it: «Προς: …», «Θέμα: …», an
        /// empty line, the text). False when there is none.
        /// </summary>
        public bool ReadMessage()
        {
            try
            {
                if (string.IsNullOrEmpty(MessagePath) || !File.Exists(MessagePath))
                    return false;
                string[] lines = File.ReadAllLines(MessagePath, Encoding.UTF8);
                if (lines.Length < 2)
                    return false;
                Func<string, string> after = line =>
                {
                    int i = line.IndexOf(':');
                    return i >= 0 ? line.Substring(i + 1).Trim() : line.Trim();
                };
                Recipients = ParseRecipients(after(lines[0]));
                Subject = after(lines[1]);
                Body = string.Join(Environment.NewLine, lines.Skip(3).ToArray()).TrimEnd() + Environment.NewLine;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
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

        /// <summary>(2.24.3) Its moment in the video of the package (seconds from its start).</summary>
        public double Seconds;
    }

    /// <summary>
    /// (2.24.3) What a race keeps about its package when it is made, for its window without making it again: the signature of the analysis
    /// it was made from (SwimPackage.Signature), its video (seconds, size, sound, screens), the moments of its pictures and the line of the
    /// KOE App.
    /// </summary>
    public class SwimPackageInfo
    {
        public string Sig = "";
        public double Seconds;
        public int Width;
        public int Height;
        public bool Sound;
        public int Screens = 1;
        public List<SwimPackageMoment> Pictures = new List<SwimPackageMoment>();
        public bool? Archived;
        public string ArchiveNote;
    }

    /// <summary>(2.24.3) A picture of the video of a package: its moment (seconds from the start of the video) and its name.</summary>
    public class SwimPackageMoment
    {
        public double Seconds;
        public string Label = "";
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

        /// <summary>The bitrate cap of the videos (kbit/s): for the sending (a video of a group of screens may have more, MaxRateK).</summary>
        public const int DefaultMaxRateK = 2500;

        /// <summary>(2.16.4) The bitrate cap of this video (kbit/s).</summary>
        public int MaxRateK = DefaultMaxRateK;

        /// <summary>The arguments of ffmpeg: the images on its input, the sound of the source (seconds from its start), the MP4.</summary>
        public static string Arguments(int width, int height, double fps, string soundSource, double soundFrom, double soundSeconds, string output)
        {
            return Arguments(width, height, fps, soundSource, soundFrom, soundSeconds, output, DefaultMaxRateK);
        }

        /// <summary>The arguments of ffmpeg with the bitrate capped at maxRateK kbit/s.</summary>
        public static string Arguments(int width, int height, double fps, string soundSource, double soundFrom, double soundSeconds, string output, int maxRateK)
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
            int rate = Math.Max(500, maxRateK);
            a.AppendFormat(ci, "-c:v libx264 -preset veryfast -crf 24 -maxrate {0}k -bufsize {1}k -pix_fmt yuv420p ", rate, rate * 2);
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

        /// <summary>
        /// (2.24.3) What ffmpeg reads in a video: its length, the size of its images and whether it has sound. False when it could not be
        /// read.
        /// </summary>
        public static bool Probe(string ffmpeg, string video, out double seconds, out Size size, out bool sound)
        {
            seconds = 0;
            size = Size.Empty;
            sound = false;
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
                string text;
                using (Process p = Process.Start(info))
                {
                    Task<string> err = p.StandardError.ReadToEndAsync();
                    Task<string> outText = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(15000))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        return false;
                    }

                    text = err.Result;
                }

                Match d = Regex.Match(text, @"Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)");
                if (d.Success)
                {
                    seconds = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 + int.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture) * 60 +
                        double.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture);
                }

                string videoLine = text.Split('\n').FirstOrDefault(l => l.Contains("Stream #") && l.Contains("Video:"));
                if (videoLine != null)
                {
                    Match s = Regex.Match(videoLine, @"[ ,](\d{2,5})x(\d{2,5})[ ,\[]");
                    if (s.Success)
                        size = new Size(int.Parse(s.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(s.Groups[2].Value, CultureInfo.InvariantCulture));
                }

                sound = text.Split('\n').Any(l => l.Contains("Stream #") && l.Contains("Audio:"));
                return d.Success;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// (2.24.3) The image of a video at a moment (seconds from its start), that many pixels wide, by ffmpeg: null when there is none (after
        /// its end) or it could not be read.
        /// </summary>
        public static Bitmap Frame(string ffmpeg, string video, double seconds, int width)
        {
            if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg) || string.IsNullOrEmpty(video) || !File.Exists(video))
                return null;
            try
            {
                string args = string.Format(CultureInfo.InvariantCulture, "-hide_banner -nostdin -ss {0:0.###} -i \"{1}\" -frames:v 1 -vf scale={2}:-1 -f image2pipe -vcodec bmp -",
                    Math.Max(0, seconds), video, Math.Max(16, width));
                ProcessStartInfo info = new ProcessStartInfo(ffmpeg, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                using (Process p = Process.Start(info))
                using (MemoryStream data = new MemoryStream())
                {
                    Task<string> err = p.StandardError.ReadToEndAsync();
                    Task copy = p.StandardOutput.BaseStream.CopyToAsync(data);
                    if (!p.WaitForExit(15000))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        return null;
                    }

                    if (!copy.Wait(5000) || data.Length < 64)
                        return null;
                    data.Position = 0;
                    using (Bitmap read = new Bitmap(data))
                        return new Bitmap(read);
                }
            }
            catch (Exception)
            {
                return null;
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
                ProcessStartInfo info = new ProcessStartInfo(ffmpeg, Arguments(width, height, fps, soundSource, soundFrom, soundSeconds, output, MaxRateK))
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
