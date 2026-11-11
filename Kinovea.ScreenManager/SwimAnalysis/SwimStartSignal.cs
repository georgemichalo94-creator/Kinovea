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

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (Swim 2.22.0) The start signal of the computer for the starts and turns of technique analysis: «Take your marks» (the voice of
    /// Windows, an English one when there is one), a pause of about two seconds (1.8 – 2.2 s, not always the same, as a starter) and the
    /// horn: two tones together (890 and 1017 Hz, as the start systems of the pools), 0.6 s. The cameras that hear the horn are lined up
    /// by it (their sounds), and the start analysis finds its moment (the start of the attempt). Played by the speaker of the computer
    /// (or the one connected to it); one signal at a time. The words go without the band of the horn's tones (820 – 1120 Hz): the start
    /// analysis hears two tones 8/7 apart there, as the 7th and 8th harmonics of a voice can be, so that only the horn is a start signal.
    /// Without an English voice, a Greek one says the words as written in Greek letters («Τέικ γιορ μάρκς»).
    /// </summary>
    public static class SwimStartSignal
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public const int Rate = 22050;
        public const double LowTone = 890;
        public const double HighTone = 1017;
        public const double HornSeconds = 0.6;
        public const double MinPause = 1.8;
        public const double MaxPause = 2.2;
        public const string Words = "Take your marks";
        public const string GreekWords = "Τέικ γιορ μάρκς";

        /// <summary>The band of the horn's tones, out of the words (Hz), with smooth edges of BandEdge.</summary>
        public const double BandFrom = 820, BandTo = 1120, BandEdge = 40;

        private static readonly object locker = new object();
        private static readonly Random random = new Random();
        private static short[] voice;
        private static bool voiceTried;
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

                double pause = MinPause + (MaxPause - MinPause) * random.NextDouble();
                double hornAt;
                byte[] wave = Build(Voice(), pause, out hornAt);
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
                    log.DebugFormat("Start signal: the horn {0:0.00} s after the words.", pause);
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
            words = words ?? new short[0];
            int lead = (int)(0.05 * Rate);
            int gap = (int)(Math.Max(0, pause) * Rate);
            short[] horn = Horn();
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

        /// <summary>The horn: the two tones together, a quick rise and fall (no click).</summary>
        public static short[] Horn()
        {
            int n = (int)(HornSeconds * Rate);
            int edge = (int)(0.012 * Rate);
            short[] horn = new short[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double envelope = Math.Min(1, Math.Min(i / (double)edge, (n - 1 - i) / (double)edge));
                double v = 0.38 * Math.Sin(2 * Math.PI * LowTone * t) + 0.38 * Math.Sin(2 * Math.PI * HighTone * t);
                horn[i] = (short)Math.Round(32767 * envelope * v);
            }

            return horn;
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
