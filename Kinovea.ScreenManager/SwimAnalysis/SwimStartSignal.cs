#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;
using System.Xml;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (Swim 2.22.0) The start signal of the computer for the starts and turns of technique analysis: «Take your marks» (the voice of
    /// Windows, an English one when there is one), a pause of 1.5 s and the horn: two tones together (890 and 1017 Hz, as the start
    /// systems of the pools), short and sharp as at the meets (0.3 s, rising and falling in 2 ms), hard and at the top of the scale
    /// (two square tones driven together into a soft limit, as a horn speaker driven hard). The cameras that hear the horn are lined up
    /// by it (their sounds), and the start analysis finds its moment (the start of the attempt). Played by the speaker of the computer
    /// (or the one connected to it); one signal at a time. The words go without the band of the horn's tones (820 – 1120 Hz): the start
    /// analysis hears two tones 8/7 apart there, as the 7th and 8th harmonics of a voice can be, so that only the horn is a start signal.
    /// Without an English voice, a Greek one says the words as written in Greek letters («Τέικ γιορ μάρκς»).
    /// (12.11.2026) The analyst's own sounds take the place of the voice and of the made horn, each on its own, when there are: in the
    /// folder of the settings «SwimStartWords.wav» and «SwimStartHorn.wav»; else those of TEMPLO (Contemplas) on the computer, in its
    /// working folder: «swimstart_2.wav» (the words) and «swimstart_3.wav» (the horn). They are read where they are (never copied, never
    /// in the code): the words from their first sound to their last, without the band of the horn; the horn from its first sound.
    /// </summary>
    public static class SwimStartSignal
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>The rate of the signal (that of the sounds of TEMPLO: nothing to change in them).</summary>
        public const int Rate = 44100;
        public const double LowTone = 890;
        public const double HighTone = 1017;
        /// <summary>The horn (s): short, as the start systems of the meets (11.11.2026: «πιο κοφτός»).</summary>
        public const double HornSeconds = 0.3;

        /// <summary>From the end of the words to the horn (s), as the analyst asked (11.11.2026: 1.5 s).</summary>
        public const double Pause = 1.5;

        /// <summary>The peak of the horn (of the scale).</summary>
        public const double HornPeak = 0.95;

        /// <summary>How hard the two tones are driven together into the soft limit (a hard sound; louder for the same peak).</summary>
        public const double HornDrive = 6;

        /// <summary>The harmonics of the square tones go up to here (Hz), under half the rate: nothing folds back.</summary>
        private const double HornTop = 10000;
        public const string Words = "Take your marks";
        public const string GreekWords = "Τέικ γιορ μάρκς";

        /// <summary>The band of the horn's tones, out of the words (Hz), with smooth edges of BandEdge.</summary>
        public const double BandFrom = 820, BandTo = 1120, BandEdge = 40;

        /// <summary>The own sounds in the folder of the settings.</summary>
        public const string WordsFile = "SwimStartWords.wav", HornFile = "SwimStartHorn.wav";

        /// <summary>The sounds of TEMPLO (Contemplas) in its working folder: the words and the horn of its swimming start.</summary>
        public const string ContemplasWords = "swimstart_2.wav", ContemplasHorn = "swimstart_3.wav";

        private static readonly object locker = new object();
        private static short[] voice;
        private static bool voiceTried;
        private static OwnSound ownWords = new OwnSound(), ownHorn = new OwnSound();

        /// <summary>An own sound, read again when its file changes.</summary>
        private class OwnSound
        {
            public string Path = "";
            public DateTime Written = DateTime.MinValue;
            public short[] Samples;
        }
        private static SoundPlayer player;
        private static MemoryStream playing;
        private static DateTime busyUntil = DateTime.MinValue;

        /// <summary>A signal is being played.</summary>
        public static bool Playing
        {
            get { lock (locker) return DateTime.Now < busyUntil; }
        }

        /// <summary>
        /// Plays the signal now (nothing while one plays: then MinValue). Returns when the horn will sound on the clock of the computer
        /// (as the sound is given to Windows: the speaker may be a little later).
        /// </summary>
        public static DateTime Play()
        {
            lock (locker)
            {
                if (DateTime.Now < busyUntil)
                    return DateTime.MinValue;

                double hornAt;
                byte[] wave = Build(WordsSound(), HornSound(), Pause, out hornAt);
                try
                {
                    if (player != null)
                        player.Stop();

                    if (playing != null)
                        playing.Dispose();

                    playing = new MemoryStream(wave);
                    player = new SoundPlayer(playing);
                    player.Load();
                    DateTime start = DateTime.Now;
                    player.Play();
                    double length = (wave.Length - 44) / 2.0 / Rate;
                    busyUntil = start.AddSeconds(length + 0.3);
                    log.DebugFormat("Start signal: the horn {0:0.00} s after the words ({1}).", Pause, Sounds());
                    return start.AddSeconds(hornAt);
                }
                catch (Exception e)
                {
                    log.Error("The start signal could not be played.", e);
                    return DateTime.MinValue;
                }
            }
        }

        /// <summary>
        /// The signal as a WAV (16 bits, one channel, Rate): the words (none when empty), the pause, the horn and a little silence.
        /// hornAt: the second of the horn's start in it.
        /// </summary>
        public static byte[] Build(short[] words, double pause, out double hornAt)
        {
            return Build(words, Horn(), pause, out hornAt);
        }

        /// <summary>The signal with this horn (the made one when null).</summary>
        public static byte[] Build(short[] words, short[] horn, double pause, out double hornAt)
        {
            words = words ?? new short[0];
            horn = horn != null && horn.Length > 0 ? horn : Horn();
            int lead = (int)(0.05 * Rate);
            int gap = (int)(Math.Max(0, pause) * Rate);
            int tail = (int)(0.3 * Rate);
            int n = lead + words.Length + gap + horn.Length + tail;
            short[] all = new short[n];
            Array.Copy(words, 0, all, lead, words.Length);
            int hornStart = lead + words.Length + gap;
            Array.Copy(horn, 0, all, hornStart, horn.Length);
            hornAt = hornStart / (double)Rate;

            using (MemoryStream ms = new MemoryStream(44 + 2 * n))
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write(36 + 2 * n);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(Rate);
                w.Write(Rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write(2 * n);
                foreach (short s in all)
                    w.Write(s);

                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>
        /// The horn: the two tones together, each a square tone (its odd harmonics under HornTop), both driven into a soft limit (tanh,
        /// HornDrive: the hard sound of a horn speaker driven hard, about 1 dB louder than the tones alone and harsher), at HornPeak; it
        /// rises and falls in 2 ms (sharp, as at the meets). The start analysis hears the two tones over what the limit adds (28 dB under).
        /// </summary>
        public static short[] Horn()
        {
            int n = (int)(HornSeconds * Rate);
            int edge = Math.Max(1, (int)(0.002 * Rate));
            double top = Math.Min(HornTop, Rate / 2.0 - 100);
            double[] sum = new double[n];
            foreach (double tone in new[] { LowTone, HighTone })
            {
                double[] v = new double[n];
                double peak = 0;
                for (int i = 0; i < n; i++)
                {
                    double t = i / (double)Rate, s = 0;
                    for (int k = 1; k * tone < top; k += 2)
                        s += Math.Sin(2 * Math.PI * k * tone * t) / k;

                    v[i] = s;
                    peak = Math.Max(peak, Math.Abs(s));
                }

                for (int i = 0; i < n; i++)
                    sum[i] += v[i] / Math.Max(1e-9, peak);
            }

            double most = sum.Max(x => Math.Abs(x));
            double limit = Math.Tanh(HornDrive);
            short[] horn = new short[n];
            for (int i = 0; i < n; i++)
            {
                double envelope = Math.Min(1, Math.Min(i / (double)edge, (n - 1 - i) / (double)edge));
                double s = Math.Tanh(HornDrive * sum[i] / Math.Max(1e-9, most)) / limit;
                horn[i] = (short)Math.Round(32767 * HornPeak * envelope * s);
            }

            return horn;
        }

        /// <summary>The words of the signal: the analyst's or TEMPLO's when there are, else the voice of Windows.</summary>
        public static short[] WordsSound()
        {
            short[] own = Own(ownWords, WordsFile, ContemplasWords, true);
            return own ?? Voice();
        }

        /// <summary>The horn of the signal: the analyst's or TEMPLO's when there is, else the made one.</summary>
        public static short[] HornSound()
        {
            short[] own = Own(ownHorn, HornFile, ContemplasHorn, false);
            return own ?? Horn();
        }

        /// <summary>Where the words and the horn come from (for the log and the tests).</summary>
        public static string Sounds()
        {
            WordsSound();
            HornSound();
            lock (locker)
            {
                return "words: " + (ownWords.Samples != null ? Path.GetFileName(ownWords.Path) : "voice of Windows") +
                    "; horn: " + (ownHorn.Samples != null ? Path.GetFileName(ownHorn.Path) : "made");
            }
        }

        /// <summary>The working folder of TEMPLO (Contemplas): the one in its settings, else c:\contemplas.</summary>
        public static string ContemplasFolder()
        {
            try
            {
                string settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CONTEMPLAS", "contemplas.system.settings");
                if (File.Exists(settings))
                {
                    XmlDocument doc = new XmlDocument();
                    doc.Load(settings);
                    XmlNode node = doc.SelectSingleNode("//param[@key='workingfolder']");
                    string folder = node != null && node.Attributes["value"] != null ? node.Attributes["value"].Value : null;
                    if (!string.IsNullOrWhiteSpace(folder))
                        return folder.Trim();
                }
            }
            catch (Exception e)
            {
                log.Debug("The settings of TEMPLO could not be read.", e);
            }

            return @"c:\contemplas";
        }

        /// <summary>An own sound: the analyst's in the folder of the settings, else TEMPLO's; read again when its file changes; null: none.</summary>
        private static short[] Own(OwnSound own, string mine, string contemplas, bool words)
        {
            string path = null;
            try
            {
                string candidate = string.IsNullOrEmpty(Software.SettingsDirectory) ? null : Path.Combine(Software.SettingsDirectory, mine);
                if (candidate != null && File.Exists(candidate))
                    path = candidate;
                else
                {
                    candidate = Path.Combine(ContemplasFolder(), contemplas);
                    if (File.Exists(candidate))
                        path = candidate;
                }
            }
            catch (Exception)
            {
                path = null;
            }

            lock (locker)
            {
                if (path == null)
                {
                    own.Path = "";
                    own.Samples = null;
                    return null;
                }

                DateTime written;
                try
                {
                    written = File.GetLastWriteTimeUtc(path);
                }
                catch (Exception)
                {
                    written = DateTime.MinValue;
                }

                if (own.Path == path && own.Written == written)
                    return own.Samples;

                own.Path = path;
                own.Written = written;
                short[] read = ReadWav(path);
                own.Samples = read == null ? null : (words ? OwnWords(read) : OwnHorn(read));
                if (own.Samples != null && own.Samples.Length == 0)
                    own.Samples = null;

                log.DebugFormat("The {0} of the start signal: «{1}» ({2}).", words ? "words" : "horn", Path.GetFileName(path),
                    own.Samples != null ? string.Format(CultureInfo.InvariantCulture, "{0:0.00} s", own.Samples.Length / (double)Rate) : "it could not be read");
                return own.Samples;
            }
        }

        /// <summary>Own words: from their first sound to their last (30 dB under the loudest, by 10 ms, the rumble under 100 Hz out), without the band of the horn, loud.</summary>
        private static short[] OwnWords(short[] s)
        {
            int hop = Rate / 100;
            int n = s.Length / hop;
            if (n < 3)
                return new short[0];

            double a = 1 / (1 + 2 * Math.PI * 100.0 / Rate);
            double previous = 0, y = 0;
            double[] energy = new double[n];
            for (int i = 0; i < n * hop; i++)
            {
                y = a * (y + s[i] - previous);
                previous = s[i];
                energy[i / hop] += y * y;
            }

            double most = energy.Max();
            if (most <= 0)
                return new short[0];

            double limit = most * Math.Pow(10, -30 / 10.0);
            int first = Array.FindIndex(energy, e => e >= limit);
            int last = Array.FindLastIndex(energy, e => e >= limit);
            int start = Math.Max(0, first * hop - hop);
            int end = Math.Min(s.Length, (last + 1) * hop + hop);
            short[] cut = new short[end - start];
            Array.Copy(s, start, cut, 0, cut.Length);
            return Normalize(WithoutHornBand(cut));
        }

        /// <summary>An own horn: from its first sound (a tenth of its peak, 2 ms before) to its last (a hundredth, 5 ms after), at HornPeak.</summary>
        private static short[] OwnHorn(short[] s)
        {
            int peak = s.Length == 0 ? 0 : s.Max(x => Math.Abs((int)x));
            if (peak < 100)
                return new short[0];

            int first = Array.FindIndex(s, x => Math.Abs((int)x) >= peak / 10);
            int last = Array.FindLastIndex(s, x => Math.Abs((int)x) >= peak / 100);
            int start = Math.Max(0, first - (int)(0.002 * Rate));
            int end = Math.Min(s.Length, last + 1 + (int)(0.005 * Rate));
            double gain = HornPeak * 32767 / peak;
            short[] cut = new short[end - start];
            for (int i = 0; i < cut.Length; i++)
                cut[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(s[start + i] * gain)));

            return cut;
        }

        /// <summary>
        /// A WAV (PCM of 8, 16, 24 or 32 bits, or 32-bit float; any channels and rate) as one channel at Rate (the channels together; another
        /// rate by straight lines between the samples); null when it cannot be read.
        /// </summary>
        public static short[] ReadWav(string path)
        {
            try
            {
                byte[] b = File.ReadAllBytes(path);
                if (b.Length < 44 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE")
                    return null;

                int format = 0, channels = 0, rate = 0, bits = 0, dataStart = -1, dataLength = 0;
                int pos = 12;
                while (pos + 8 <= b.Length)
                {
                    string id = Encoding.ASCII.GetString(b, pos, 4);
                    int size = BitConverter.ToInt32(b, pos + 4);
                    int body = pos + 8;
                    if (size < 0)
                        break;

                    if (id == "fmt " && size >= 16 && body + 16 <= b.Length)
                    {
                        format = BitConverter.ToUInt16(b, body);
                        channels = BitConverter.ToUInt16(b, body + 2);
                        rate = BitConverter.ToInt32(b, body + 4);
                        bits = BitConverter.ToUInt16(b, body + 14);
                        if (format == 0xFFFE && size >= 26 && body + 26 <= b.Length)
                            format = BitConverter.ToUInt16(b, body + 24);
                    }
                    else if (id == "data")
                    {
                        dataStart = body;
                        dataLength = Math.Min(size, b.Length - body);
                        break;
                    }

                    pos = body + size + (size & 1);
                }

                bool pcm = format == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32);
                bool single = format == 3 && bits == 32;
                if (dataStart < 0 || channels < 1 || rate < 8000 || !(pcm || single))
                    return null;

                int bytes = bits / 8, frame = bytes * channels;
                int frames = dataLength / frame;
                double[] mono = new double[frames];
                for (int i = 0; i < frames; i++)
                {
                    double sum = 0;
                    for (int c = 0; c < channels; c++)
                    {
                        int at = dataStart + i * frame + c * bytes;
                        if (single)
                            sum += BitConverter.ToSingle(b, at);
                        else if (bits == 8)
                            sum += (b[at] - 128) / 128.0;
                        else if (bits == 16)
                            sum += BitConverter.ToInt16(b, at) / 32768.0;
                        else if (bits == 24)
                            sum += (b[at] | (b[at + 1] << 8) | ((sbyte)b[at + 2] << 16)) / 8388608.0;
                        else
                            sum += BitConverter.ToInt32(b, at) / 2147483648.0;
                    }

                    mono[i] = sum / channels;
                }

                int n = rate == Rate ? frames : (int)((long)frames * Rate / rate);
                short[] result = new short[n];
                for (int i = 0; i < n; i++)
                {
                    double t = rate == Rate ? i : i * (double)rate / Rate;
                    int k = (int)t;
                    double f = t - k;
                    double v = k + 1 < frames ? mono[k] * (1 - f) + mono[k + 1] * f : (k < frames ? mono[k] : 0);
                    result[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(v * 32767)));
                }

                return result;
            }
            catch (Exception e)
            {
                log.Error("A sound of the start signal could not be read.", e);
                return null;
            }
        }

        /// <summary>«Take your marks» by the voice of Windows (an English one when there is one), once; empty when there is no voice.</summary>
        public static short[] Voice()
        {
            lock (locker)
            {
                if (voiceTried)
                    return voice ?? new short[0];

                voiceTried = true;
                try
                {
                    using (SpeechSynthesizer synth = new SpeechSynthesizer())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        InstalledVoice english = synth.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo != null &&
                            v.VoiceInfo.Culture != null && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "en");
                        string words = Words;
                        if (english != null)
                            synth.SelectVoice(english.VoiceInfo.Name);
                        else if (synth.Voice != null && synth.Voice.Culture != null && synth.Voice.Culture.TwoLetterISOLanguageName == "el")
                            words = GreekWords;

                        synth.Rate = -1;
                        synth.Volume = 100;
                        synth.SetOutputToAudioStream(ms, new SpeechAudioFormatInfo(Rate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                        synth.Speak(words);
                        synth.SetOutputToNull();
                        byte[] bytes = ms.ToArray();
                        short[] samples = new short[bytes.Length / 2];
                        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);
                        voice = Normalize(WithoutHornBand(Trim(samples)));
                        log.DebugFormat("The words of the start signal: {0:0.00} s ({1}, «{2}»).", voice.Length / (double)Rate,
                            synth.Voice != null ? synth.Voice.Name : "the voice of Windows", words);
                    }
                }
                catch (Exception e)
                {
                    log.Error("The words of the start signal could not be made (only the horn).", e);
                    voice = new short[0];
                }

                return voice;
            }
        }

        /// <summary>
        /// The sound without the band of the horn (BandFrom – BandTo, smooth edges): one transform of all of it (padded to twice its length,
        /// so nothing comes round from the end), the band cut, back.
        /// </summary>
        public static short[] WithoutHornBand(short[] s)
        {
            if (s == null || s.Length == 0)
                return s ?? new short[0];

            int n = 1;
            while (n < 2 * s.Length)
                n <<= 1;

            double[] re = new double[n], im = new double[n];
            for (int i = 0; i < s.Length; i++)
                re[i] = s[i];

            SwimStartSound.Fft(re, im);
            double bin = (double)Rate / n;
            for (int k = 0; k <= n / 2; k++)
            {
                double f = k * bin, keep = 1;
                if (f >= BandFrom && f <= BandTo)
                    keep = 0;
                else if (f > BandFrom - BandEdge && f < BandFrom)
                    keep = 0.5 + 0.5 * Math.Cos(Math.PI * (f - (BandFrom - BandEdge)) / BandEdge);
                else if (f > BandTo && f < BandTo + BandEdge)
                    keep = 0.5 - 0.5 * Math.Cos(Math.PI * (f - BandTo) / BandEdge);

                if (keep >= 1)
                    continue;

                re[k] *= keep;
                im[k] *= keep;
                if (k > 0 && k < n / 2)
                {
                    re[n - k] *= keep;
                    im[n - k] *= keep;
                }
            }

            // Back: the transform of the conjugate, conjugated, over n (only the real part is kept).
            for (int k = 0; k < n; k++)
                im[k] = -im[k];

            SwimStartSound.Fft(re, im);
            short[] result = new short[s.Length];
            for (int i = 0; i < s.Length; i++)
                result[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(re[i] / n)));

            return result;
        }

        /// <summary>The silence before and after the words out.</summary>
        private static short[] Trim(short[] s)
        {
            int first = 0, last = s.Length - 1;
            while (first < s.Length && Math.Abs((int)s[first]) < 300)
                first++;

            while (last > first && Math.Abs((int)s[last]) < 300)
                last--;

            if (last <= first)
                return new short[0];

            short[] result = new short[last - first + 1];
            Array.Copy(s, first, result, 0, result.Length);
            return result;
        }

        /// <summary>The words loud (their peak at 90 %).</summary>
        private static short[] Normalize(short[] s)
        {
            int peak = s.Length == 0 ? 0 : s.Max(x => Math.Abs((int)x));
            if (peak < 100)
                return s;

            double gain = 0.9 * 32767 / peak;
            return s.Select(x => (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(x * gain)))).ToArray();
        }
    }
}
