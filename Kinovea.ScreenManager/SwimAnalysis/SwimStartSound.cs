#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kinovea.ScreenManager
{
    /// <summary>A start signal heard in the sound of a video.</summary>
    public sealed class SwimBeep
    {
        /// <summary>Onset of the tones (seconds from the first sample given).</summary>
        public double Onset;

        /// <summary>How clear it is (dB): the rise of the two tones, less when the sound before was loud.</summary>
        public double Score;

        /// <summary>Mean rise of the two tones over the half second before (dB, at most 35).</summary>
        public double Rise;

        /// <summary>Level of the sound in the half second before the signal, from the usual level of the recording (dB).</summary>
        public double Quiet;

        /// <summary>The two tones (Hz).</summary>
        public double Low, High;

        /// <summary>The two tones started together (else one after the other).</summary>
        public bool Together;

        /// <summary>One tone only (since 2.8.3: the start systems that sound a single tone, e.g. 719 Hz for 0.4 s): Low = High.</summary>
        public bool Single;

        /// <summary>How long the tone lasted (s), for a single tone.</summary>
        public double Duration;

        /// <summary>Share of the sound (300–4000 Hz) in the tone while it lasts, for a single tone (a start signal: most of it).</summary>
        public double Purity;
    }

    /// <summary>
    /// A start signal the analyst has confirmed (2.9.3): the tones of the start system of a pool (Low = High for a single tone),
    /// learned from the starts he kept (SwimLearning.StartTones). A signal heard with the same tones is that start system.
    /// </summary>
    public sealed class SwimStartTone
    {
        /// <summary>The tones (Hz); Low = High: a single tone.</summary>
        public double Low, High;

        /// <summary>Starts kept with these tones.</summary>
        public int Count;

        public bool Single
        {
            get { return Math.Abs(High - Low) < 1; }
        }

        public override string ToString()
        {
            return Single ? string.Format(CultureInfo.InvariantCulture, "{0:0} Hz", Low) : string.Format(CultureInfo.InvariantCulture, "{0:0}/{1:0} Hz", Low, High);
        }

        /// <summary>
        /// "715" (a single tone) or "890/1015" (two tones), several separated by ';' (the lab gives the learned tones so).
        /// </summary>
        public static List<SwimStartTone> Parse(string text)
        {
            List<SwimStartTone> result = new List<SwimStartTone>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            foreach (string part in text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] tones = part.Split('/');
                double low, high;
                if (!double.TryParse(tones[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out low) || low <= 0)
                    continue;
                high = low;
                if (tones.Length > 1 && (!double.TryParse(tones[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out high) || high <= 0))
                    continue;
                result.Add(new SwimStartTone { Low = low, High = high, Count = 1 });
            }

            return result;
        }
    }

    /// <summary>
    /// Start signal from the sound of the video: the start systems sound two tones together, the higher one 8/7 of the lower one
    /// (about 890 and 1015 Hz, 900–920 and 1030–1045 Hz in other pools), for a few tenths of a second. Other start systems sound
    /// one tone (since 2.8.3: 719 Hz for 0.40–0.46 s in the outdoor pool of the Multinations meets, where the two tones were never
    /// heard): a tone of 520–1550 Hz that comes on sharply, stands well above the neighbouring frequencies and lasts 0.18–1 s
    /// (a whistle is higher, a voice moves, a horn or a splash is not one frequency); it counts a little less than two tones.
    ///
    /// The sound (16 kHz, mono) in frames of 64 ms every 5 ms:
    /// 1. for each lower tone of 860–950 Hz and its higher tone, each is "on" when it is 6 dB above the usual level of the
    ///    neighbouring frequencies (600–1500 Hz) and 8 dB above its own level of the half second before;
    /// 2. the signal: the two tones on together (most of 0.1 s), or one after the other within 0.35 s (in some places of the pool
    ///    one of them is weak at first); its onset goes back to the first of the two that rose;
    /// 3. score: their mean rise, less when the exact frequencies are not 8/7 apart (0.6 %: all the start signals heard; 0.7 within
    ///    1.5 %, else half), when the lower tone is not one of the start systems seen (878–930 Hz: 0.85), when the tones came one
    ///    after the other (0.9), and when the sound of the half second before was loud (the quiet before the start);
    /// 4. the onset to the millisecond: envelope of the two tones (demodulated, 40 Hz low pass both ways), where it rises above the
    ///    level before by 20 % of the rise.
    /// The sound reaches the camera after the light of the start (0.10–0.22 s: distance and recording): the start is the frame of
    /// the light seen just before the signal, else the onset less the usual delay (learned from the answers of the analyst).
    /// Reference implementation and evaluation: Python prototype of the same algorithm (38 phone videos of Greek pools and 25
    /// camera videos of two European championships: the signal found in all the videos where it is heard).
    /// </summary>
    public static class SwimStartSound
    {
        /// <summary>Samples per second of the sound read.</summary>
        public const int Rate = 16000;

        /// <summary>Usual delay from the light of the start to the onset of the signal heard (s), before any answer.</summary>
        public const double Delay = 0.13;

        private const int N = 1024;
        private const int Hop = 80;
        private const double Bin = (double)Rate / N;
        private const double Ratio = 8.0 / 7.0;
        private const int FirstBin = 32;
        private const int LastBin = 103;

        /// <summary>The single tones looked for (Hz) and how they are told from the other sounds.</summary>
        private const double SingleFrom = 520, SingleTo = 1550;
        private const double SingleAbove = 10, SingleRise = 10, SingleMinSeconds = 0.18, SingleMaxSeconds = 1.0, SingleWeight = 0.8;

        private static readonly int Sustain = (int)(0.10 * Rate / Hop);
        private static readonly int Lead = (int)(0.25 * Rate / Hop);
        private static readonly int Sequence = (int)(0.35 * Rate / Hop);
        private static readonly int BeforeFrom = (int)(0.5 * Rate / Hop);
        private static readonly int BeforeTo = (int)(0.05 * Rate / Hop);

        /// <summary>Debug output (null: none).</summary>
        public static Action<string> Trace;

        #region Sound of the video
        /// <summary>
        /// The sound of the video (mono, Rate samples per second) from 'from' (seconds of the stream clock, as the time stamps of the
        /// frames) for 'seconds': ffmpeg (next to the program) decodes it, sample 0 at 'from' exactly (silence where there is no
        /// sound). Null when the video has no sound or ffmpeg is missing or fails (error: why).
        /// </summary>
        public static float[] Read(string ffmpeg, string video, double from, double seconds, CancellationToken cancel, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg))
            {
                error = "ffmpeg not found";
                return null;
            }

            if (string.IsNullOrEmpty(video) || !File.Exists(video) || seconds <= 0)
            {
                error = "no video";
                return null;
            }

            // The stream clock is kept (-copyts); a second resampler pads or trims the sound so that its first sample is at 'from'
            // (its first_pts counts the samples of its input: the first resampler gives them at Rate). The seek (well before 'from')
            // only saves decoding a long beginning.
            CultureInfo ci = CultureInfo.InvariantCulture;
            double seek = from > 30 ? from - 3 : 0;
            long firstPts = (long)Math.Round(from * Rate);
            string args = string.Format(ci,
                "-nostdin -v error -copyts {0}-i \"{1}\" -map 0:a:0 -vn -af \"aresample={4},aresample=async=1:min_hard_comp=0.001:first_pts={2}\" -t {3:0.000} -ac 1 -ar {4} -f f32le -",
                seek > 0 ? string.Format(ci, "-ss {0:0.000} ", seek) : "", video, firstPts, seconds, Rate);

            ProcessStartInfo psi = new ProcessStartInfo(ffmpeg, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            List<float> samples = new List<float>((int)Math.Min(int.MaxValue / 8, seconds * Rate + Rate));
            System.Text.StringBuilder messages = new System.Text.StringBuilder();
            try
            {
                using (Process p = Process.Start(psi))
                {
                    p.ErrorDataReceived += (o, e) =>
                    {
                        if (e.Data != null && messages.Length < 2000)
                            lock (messages) messages.AppendLine(e.Data);
                    };
                    p.BeginErrorReadLine();
                    using (CancellationTokenRegistration r = cancel.Register(() => { try { if (!p.HasExited) p.Kill(); } catch (Exception) { } }))
                    {
                        Stream s = p.StandardOutput.BaseStream;
                        byte[] buffer = new byte[65536];
                        int carry = 0;
                        while (true)
                        {
                            int read = s.Read(buffer, carry, buffer.Length - carry);
                            if (read <= 0)
                                break;

                            int total = carry + read, whole = total / 4 * 4;
                            for (int i = 0; i < whole; i += 4)
                                samples.Add(BitConverter.ToSingle(buffer, i));
                            carry = total - whole;
                            if (carry > 0)
                                Buffer.BlockCopy(buffer, whole, buffer, 0, carry);
                        }

                        if (!p.WaitForExit(10000))
                        {
                            try { p.Kill(); } catch (Exception) { }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }

            if (cancel.IsCancellationRequested)
            {
                error = "cancelled";
                return null;
            }

            if (samples.Count < Rate)
            {
                string m;
                lock (messages) m = messages.ToString().Trim();
                error = m.Length > 0 ? m : "no sound";
                return null;
            }

            return samples.ToArray();
        }
        #endregion

        #region Start from the light and the sound
        /// <summary>Weaker signals than this (dB) are not a start.</summary>
        public const double MinScore = 10;

        /// <summary>Weight of a signal heard while the camera already follows a race (its score is multiplied by it).</summary>
        public const double InRaceWeight = 0.3;

        /// <summary>
        /// Single tones (2.8.3): weaker ones than this are not a start, and the clearest SingleCandidates of them (not heard in a
        /// race) are looked at (the start signal was among the 3 clearest in all the recordings of the Multinations meets).
        /// </summary>
        public const double SingleMinScore = 5;
        public const int SingleCandidates = 8;

        /// <summary>
        /// A single tone goes before a clearer signal heard first ("take your marks" is no dive after it) only when it is at least
        /// this share as clear (a single tone just after a clear start signal of two tones is the noise of the race).
        /// </summary>
        public const double SinglePromote = 0.8;

        /// <summary>
        /// The camera starts to move after a single tone (the camera of the stands starts to follow the swimmers): the motion of the
        /// 3 s after it at least this many times the motion of the 1.5 s before it (at least 1) and more than 4 (96 % of the start
        /// signals of the Multinations meets, 26–39 % of the other single tones).
        /// </summary>
        public const double CameraStartsRatio = 2.5;

        /// <summary>
        /// A signal heard is the one of a start system the analyst has confirmed (2.9.3, Known) when its tones are within this share
        /// of the tones learned (the single tone of the Multinations pool: 713–715 Hz in its 50 recordings; the two tones of a
        /// system within 1 %), and, for a single tone, it is pure (at least KnownPurity of the sound: 44 of its 50 start signals,
        /// none of the tones like it heard elsewhere before or after a start) and lasts like a start signal (KnownMinSeconds; the
        /// whistles and the beeps of the clocks are shorter).
        /// </summary>
        public const double KnownTolerance = 0.01;
        public const double KnownPurity = 0.6;
        public const double KnownMinSeconds = 0.3;

        /// <summary>
        /// Two signals heard nearly as clear (2.9.3): another one at least this share of the first by the rule that ranks them (Rank),
        /// away from it and leaving the video for the race, makes the start proposed one to check (its confidence below the one the
        /// auto pilot takes without asking). Lab (2026-10-20, the starts known): the Multinations recordings from the stands 6 of
        /// their 8 wrong starts so (and 8 of the 42 right ones), the other 94 recordings 3 of their right starts.
        /// </summary>
        public const double AmbiguousShare = 0.75;

        /// <summary>Confidence of a start proposed when another signal heard is nearly as clear (Ambiguous): the pilot asks.</summary>
        public const double AmbiguousConfidence = 0.55;

        /// <summary>The signal heard has the tones of a start system the analyst has confirmed (known: the learned ones, null: none).</summary>
        public static bool Known(SwimBeep b, IList<SwimStartTone> known)
        {
            if (b == null || known == null || known.Count == 0)
                return false;

            foreach (SwimStartTone k in known)
            {
                if (k == null || k.Single != b.Single)
                    continue;
                if (Math.Abs(b.Low - k.Low) > KnownTolerance * k.Low || Math.Abs(b.High - k.High) > KnownTolerance * k.High)
                    continue;
                if (b.Single && (b.Purity < KnownPurity || b.Duration < KnownMinSeconds))
                    continue;
                return true;
            }

            return false;
        }

        /// <summary>
        /// How a start heard ranks (Choose): how clear its signal is, more with a light just before it (×1.2) and with the race after
        /// it (the dive or the camera following: ×1.3).
        /// </summary>
        public static double Rank(SwimStartCandidate c)
        {
            if (c == null)
                return 0;

            return c.BeepScore * (c.Source == "light+sound" ? 1.2 : 1.0) * (c.Follow >= 0 || c.Dive >= 0 ? 1.3 : 1.0);
        }

        /// <summary>
        /// Another start heard is nearly as clear as the first one (AmbiguousShare of its Rank), at least 0.3 s away from it and
        /// leaving the video for the race (fits; null: all do). When the first one has the tones of a start system confirmed, only
        /// another one with them counts (the other sounds are not that start system).
        /// </summary>
        public static bool Ambiguous(List<SwimStartCandidate> candidates, double fps, Func<SwimStartCandidate, bool> fits = null)
        {
            if (candidates == null || candidates.Count < 2 || double.IsNaN(candidates[0].Beep))
                return false;

            SwimStartCandidate best = candidates[0];
            double rank = Rank(best);
            int apart = Math.Max(1, (int)Math.Round(0.3 * fps));
            return rank > 0 && candidates.Skip(1).Any(c => !double.IsNaN(c.Beep) && Math.Abs(c.Frame - best.Frame) >= apart && (fits == null || fits(c))
                && (!best.KnownTone || c.KnownTone) && Rank(c) >= AmbiguousShare * rank);
        }

        /// <summary>
        /// The time of the last signal heard that the start is looked for at (s of the sound; NaN: no clear signal of two tones, the
        /// frames are read to the end): the frames are read until 8 s after it, for the checks of the race after each signal. The
        /// clear signals of two tones; a single tone (2.8.3) only when it is as clear as the clearest of them (else it is looked at
        /// only in the frames read for them); the signals with the tones of a start system the analyst has confirmed (2.9.3, known:
        /// the learned ones).
        /// </summary>
        public static double LastToCheck(List<SwimBeep> beeps, IList<SwimStartTone> known = null)
        {
            if (beeps == null)
                return double.NaN;

            List<SwimBeep> two = beeps.Where(b => !b.Single && b.Score >= MinScore).ToList();
            if (two.Count == 0)
                return double.NaN;

            double clearest = two.Max(b => b.Score), last = two.Max(b => b.Onset);
            foreach (SwimBeep b in beeps.Where(x => x.Single && x.Score >= SingleMinScore).OrderByDescending(x => x.Score).Take(SingleCandidates))
            {
                if (b.Score >= clearest)
                    last = Math.Max(last, b.Onset);
            }

            // (2.9.3) And the signals of a start system the analyst has confirmed, wherever they are.
            foreach (SwimBeep b in beeps.Where(x => x.Score >= (x.Single ? SingleMinScore : MinScore) && Known(x, known)))
                last = Math.Max(last, b.Onset);
            return last;
        }

        /// <summary>A light at this frame against the light expected from the signal heard: 1 there, 0.3 at 0.3 s and further.</summary>
        public static double Nearness(double frame, double expected, double fps)
        {
            return Math.Max(0.3, 1 - Math.Abs(frame - expected) / Math.Max(1e-9, 0.3 * fps) * 0.7);
        }

        /// <summary>
        /// The starts from the lights and the signals heard, the most likely first. For each clear signal (the three clearest, at
        /// least 70 % of the clearest: the start signal is far clearer than the sounds like it): the light of the start 0.05–0.25 s
        /// before it, the nearest to the usual delay (a light found, else a small flash there, else a tiny flash in the frames at
        /// full size), else the signal less the usual delay. The signals are ranked by how clear they are, more with a light (×1.2)
        /// and with the race after it (the swimmers start to move, or the camera starts to follow within 3 s: ×1.3). Order: the
        /// first of them when the race follows it; else a start light of several lights checked with the dive (in a recording with
        /// little sound, a music or a voice can be clearer than the signal), then the first signal; the other signals; the other
        /// lights. Single tones (2.8.3) apart from the signals of two tones: many sounds of a recording are one tone (a voice, music,
        /// the stands), so of the clearest of them (SingleCandidates, not heard in a race) only those with the race just after them
        /// count (the swimmers start to move 0.10–0.55 s after the start they give, or the camera starts to follow within 3 s);
        /// they are ranked with the signals of two tones by the same rule (their score is lower: 0.8 × the share of the sound).
        /// The signals with the tones of a start system the analyst has confirmed (2.9.3, known) are the start system of the pool:
        /// looked at wherever they are (not heard in a race), a single tone of it without the race signs too (the camera of the stands
        /// does not move and does not see the dive), and they come first (the other sounds of the recording, "take your marks" or
        /// lights, after them). Lab (2026-10-20, the 50 Multinations recordings with the tone of its pool learned): 47 starts right
        /// instead of 42, the 94 others unchanged.
        /// </summary>
        /// <param name="found">The starts from the lights (SwimStartLight.Find).</param>
        /// <param name="frameAt">Frame (fraction) of a time of the sound (s), NaN outside the frames read.</param>
        /// <param name="fullSize">A tiny flash in the frames at full size between two frames, the nearer to the frame expected
        /// (third) the better (null: not looked for).</param>
        /// <param name="delay">Delay from the light to the onset of the signal heard (s).</param>
        /// <param name="known">The start systems the analyst has confirmed (SwimLearning.StartTones; null: none).</param>
        public static List<SwimStartCandidate> Choose(List<SwimStartCandidate> found, List<SwimBeep> beeps, SwimStartLight detector,
            Func<double, double> frameAt, Func<int, int, double, SwimStartCandidate> fullSize, double delay, IList<SwimStartTone> known = null)
        {
            found = found ?? new List<SwimStartCandidate>();
            if (beeps == null || beeps.Count == 0 || detector == null || frameAt == null)
                return found;

            double fps = detector.FramesPerSecond;
            int last = detector.Count - 1;
            List<int> follows = detector.FollowFrames();
            List<SwimStartCandidate> heard = new List<SwimStartCandidate>();

            Func<double, bool> inFrames = f => !double.IsNaN(f) && f >= 1 && f <= last;

            // A signal heard while the camera already follows a race (sounds of the stands in the middle of a race) counts little.
            var weighted = beeps.Where(x => !x.Single).Select(x =>
            {
                double f = frameAt(x.Onset);
                return new { Beep = x, Frame = f, Score = x.Score * (inFrames(f) && detector.InRace(f) ? InRaceWeight : 1.0) };
            }).ToList();
            double clearest = weighted.Count > 0 ? weighted.Max(x => x.Score) : 0;

            // (2.9.3) The signals of a start system the analyst has confirmed are looked at too, wherever they are (not in a race).
            var twoTones = weighted.Where(x => x.Beep.Score >= MinScore && x.Score >= 0.7 * clearest).OrderByDescending(x => x.Score).Take(3)
                .Concat(weighted.Where(x => x.Beep.Score >= MinScore && Known(x.Beep, known) && inFrames(x.Frame) && !detector.InRace(x.Frame)))
                .Distinct().ToList();
            foreach (var w in twoTones)
            {
                if (!inFrames(w.Frame))
                    continue;

                SwimStartCandidate c0 = StartOfSignal(w.Frame, w.Score, found, detector, fullSize, delay, follows);
                AddHeard(heard, c0, w.Beep, known);
            }

            // Single tones (2.8.3): only with the race just after them (the swimmers start to move 0.10–0.55 s after the start they
            // give, the camera starts to follow within 3 s, or the camera of the stands starts to move), never heard in a race. The
            // single tone of a start system the analyst has confirmed (2.9.3) without them too, and wherever it is in the clearest.
            var singles = beeps.Where(x => x.Single && x.Score >= SingleMinScore)
                .Select(x => new { Beep = x, Frame = frameAt(x.Onset) })
                .Where(x => inFrames(x.Frame) && !detector.InRace(x.Frame))
                .OrderByDescending(x => x.Beep.Score).ToList();
            singles = singles.Take(SingleCandidates).Concat(singles.Skip(SingleCandidates).Where(x => Known(x.Beep, known))).ToList();
            foreach (var w in singles)
            {
                int at = Math.Max(0, Math.Min(last, (int)Math.Round(w.Frame - delay * fps)));
                if (!Known(w.Beep, known) && detector.DiveAfterFrame(at) < 0 && !follows.Any(f => f >= at + 0.2 * fps && f <= at + 3.0 * fps) &&
                    !CameraStarts(detector, w.Frame))
                    continue;

                SwimStartCandidate c0 = StartOfSignal(w.Frame, w.Beep.Score, found, detector, fullSize, delay, follows);
                c0.SingleTone = true;
                AddHeard(heard, c0, w.Beep, known);
            }

            if (heard.Count == 0)
                return found;

            Func<SwimStartCandidate, bool> race = c => c.Follow >= 0 || c.Dive >= 0;
            heard = heard
                .Select((c, i) => new { c, i })
                .OrderByDescending(x => x.c.KnownTone)
                .ThenByDescending(x => Rank(x.c))
                .ThenBy(x => x.i)
                .Select(x => x.c)
                .ToList();

            // A light found at the frame of a light heard is the same light (a single tone does not take its place: in the list
            // once, where it comes first).
            List<SwimStartCandidate> lights = found.Where(c => heard.All(h => h.SingleTone || h.Frame != c.Frame)).ToList();
            List<SwimStartCandidate> result = new List<SwimStartCandidate>();
            SwimStartCandidate best = heard[0];
            if (!race(best) && !best.KnownTone)
                result.AddRange(lights.Where(c => c.Source == "light" && c.Lights >= 2));
            result.AddRange(heard);
            result.AddRange(lights.Where(c => !result.Contains(c)));
            result = result.Where((c, i) => result.FindIndex(x => x.Frame == c.Frame) == i).ToList();
            result = AfterMarks(result, detector, fps);

            if (Trace != null)
            {
                foreach (SwimStartCandidate c in result.Take(4))
                    Trace(string.Format(CultureInfo.InvariantCulture, "start {0} frame {1} beep {2:0.0} score {3:0.0} dive {4} follow {5}{6}",
                        c.Source, c.Frame, c.Beep, c.BeepScore, c.Dive, c.Follow, c.KnownTone ? " (start system confirmed)" : ""));
            }

            return result;
        }

        /// <summary>
        /// A start heard, with the tones of its signal (and whether they are those of a start system confirmed), in the list once
        /// (one at the same frame already there: it keeps its place, known when either is).
        /// </summary>
        private static void AddHeard(List<SwimStartCandidate> heard, SwimStartCandidate c0, SwimBeep beep, IList<SwimStartTone> known)
        {
            c0.ToneLow = beep.Low;
            c0.ToneHigh = beep.High;
            c0.ToneSeconds = beep.Single ? beep.Duration : double.NaN;
            c0.TonePurity = beep.Single ? beep.Purity : double.NaN;
            c0.KnownTone = Known(beep, known);
            SwimStartCandidate same = heard.FirstOrDefault(h => h.Frame == c0.Frame);
            if (same == null)
            {
                heard.Add(c0);
                return;
            }

            if (c0.KnownTone && !same.KnownTone)
            {
                same.KnownTone = true;
                same.ToneLow = c0.ToneLow;
                same.ToneHigh = c0.ToneHigh;
                same.ToneSeconds = c0.ToneSeconds;
                same.TonePurity = c0.TonePurity;
            }
        }

        /// <summary>The camera starts to move after this frame (fraction): see CameraStartsRatio.</summary>
        private static bool CameraStarts(SwimStartLight detector, double frame)
        {
            double fps = detector.FramesPerSecond;
            double before = detector.MotionBefore(frame), after = detector.MotionBefore(frame + 3.2 * fps, 3.0, 0.2);
            return !double.IsNaN(before) && !double.IsNaN(after) && after > 4 && after >= CameraStartsRatio * Math.Max(1.0, before);
        }

        /// <summary>
        /// The start of a signal heard at this frame (fraction): the light 0.05–0.25 s before it, the nearest to the usual delay
        /// (a light found, else a small flash there, else a tiny flash in the frames at full size 0.07–0.15 s before it), else the
        /// signal less the usual delay; with the dive and the camera following after it.
        /// </summary>
        private static SwimStartCandidate StartOfSignal(double frame, double score, List<SwimStartCandidate> found, SwimStartLight detector,
            Func<int, int, double, SwimStartCandidate> fullSize, double delay, List<int> follows)
        {
            double fps = detector.FramesPerSecond;
            int last = detector.Count - 1;

            // The light 0.05–0.25 s before the signal heard, the nearest to the usual delay first.
            int lo = (int)Math.Ceiling(frame - 0.25 * fps), hi = (int)Math.Floor(frame - 0.05 * fps);
            double expected = frame - delay * fps;
            SwimStartCandidate light = found.Where(c => c.Source != "dive" && c.Lights > 0 && c.Frame >= lo && c.Frame <= hi)
                .OrderByDescending(c => c.Flash * Nearness(c.Frame, expected, fps)).FirstOrDefault();
            SwimStartCandidate c0;
            if (light != null)
            {
                c0 = new SwimStartCandidate
                {
                    Frame = light.Frame, Flash = light.Flash, Strength = light.Strength, Lights = light.Lights, Follow = light.Follow,
                    Places = light.Places, Source = "light+sound", Dive = light.Dive
                };
            }
            else
            {
                // At full size the tiny flashes are many (glints, highlights on people near the camera): only 0.07–0.15 s
                // before the signal (the light of the start heard from the stands in all the pools seen).
                c0 = detector.FlashBefore(lo, hi, expected);
                if (c0 == null && fullSize != null)
                    c0 = fullSize((int)Math.Ceiling(frame - 0.15 * fps), (int)Math.Floor(frame - 0.07 * fps), expected);
                if (c0 != null)
                    c0.Source = "light+sound";
            }

            if (c0 == null)
            {
                c0 = new SwimStartCandidate
                {
                    Frame = Math.Max(0, Math.Min(last, (int)Math.Round(frame - delay * fps))), Flash = 0, Lights = 0, Source = "sound"
                };
            }

            c0.Beep = frame;
            c0.BeepScore = score;
            c0.Strength = score;
            c0.Dive = detector.DiveAfterFrame(c0.Frame);
            c0.Follow = follows.Where(f => f >= c0.Frame + 0.2 * fps && f <= c0.Frame + 3.0 * fps).DefaultIfEmpty(-1).First();
            return c0;
        }

        /// <summary>
        /// "Take your marks": the starter's command through the speakers can be heard more clearly than the signal (an outdoor pool,
        /// the wind), 1.4-2.4 s before the start. The swimmers do not move just after it (after a start they move 0.22-0.53 s after
        /// the signal in the test videos). When the start proposed has no dive 0.10-0.55 s after it but a light or a signal 0.5-3 s
        /// later has its dive just after it, that one is the start: it goes first, the command second.
        /// </summary>
        /// <summary>A single tone goes before the first start only when it is as clear (SinglePromote) as the signal heard there.</summary>
        private static bool AsClear(SwimStartCandidate c, SwimStartCandidate first)
        {
            return !c.SingleTone || first.BeepScore <= 0 || c.BeepScore >= SinglePromote * first.BeepScore;
        }

        internal static List<SwimStartCandidate> AfterMarks(List<SwimStartCandidate> result, SwimStartLight detector, double fps)
        {
            // (A signal of a start system the analyst has confirmed is not the command: 2.9.3.)
            if (result == null || result.Count < 2 || detector == null || result[0].Dive >= 0 || result[0].Source == "dive" || result[0].KnownTone)
                return result;

            SwimStartCandidate first = result[0];

            // No race seen after it (no dive, the camera not following within 3 s: the referee's whistle, a voice before the
            // swimmers are on the blocks): a light or a signal up to 15 s later with the dive or the camera following just after it
            // is the start (in the test videos the starts without race signs had the next such candidate 25 s or more later, in the race).
            if (first.Follow < 0)
            {
                SwimStartCandidate seen = result.Skip(1).Where(c => (c.Dive >= 0 || c.Follow >= 0) && c.Frame > first.Frame
                    && c.Frame <= first.Frame + 15 * fps && AsClear(c, first)).FirstOrDefault();
                if (seen != null)
                {
                    if (Trace != null)
                        Trace(string.Format(CultureInfo.InvariantCulture, "start: {0} at frame {1} has no race after it, the start {2} at {3}",
                            first.Source, first.Frame, seen.Source, seen.Frame));
                    List<SwimStartCandidate> moved = new List<SwimStartCandidate> { seen };
                    moved.AddRange(result.Where(c => c != seen));
                    return moved;
                }
            }

            int dive = detector.FirstDiveAfter(first.Frame, 3.0);
            if (dive < 0 || dive < first.Frame + 0.7 * fps)
                return result;

            int lo = dive - (int)Math.Round(0.55 * fps), hi = dive - (int)Math.Round(0.10 * fps);
            SwimStartCandidate start = result.Skip(1).Where(c => c.Source != "dive" && c.Dive >= 0 && c.Frame > first.Frame + 0.5 * fps
                && c.Frame >= lo && c.Frame <= hi && AsClear(c, first)).FirstOrDefault();
            if (start == null)
                return result;

            if (Trace != null)
                Trace(string.Format(CultureInfo.InvariantCulture, "start: {0} at frame {1} is the command (the dive {2} frames later), the start {3} at {4}",
                    first.Source, first.Frame, dive - first.Frame, start.Source, start.Frame));
            List<SwimStartCandidate> ordered = new List<SwimStartCandidate> { start };
            ordered.AddRange(result.Where(c => c != start));
            return ordered;
        }
        #endregion

        #region Detection
        /// <summary>Power of the frequencies of the frames that the detection needs.</summary>
        private sealed class Spectrum
        {
            public int Frames;

            /// <summary>Power of bins FirstBin..LastBin of each frame.</summary>
            public float[][] Tones;

            /// <summary>Usual level of the neighbouring frequencies (median power of 600–1500 Hz).</summary>
            public double[] Background;

            /// <summary>Level of the frame, 300–4000 Hz (dB).</summary>
            public double[] Level;

            /// <summary>The frame has sound (not digital silence).</summary>
            public bool[] Valid;

            public double Time(int t)
            {
                return (t * (double)Hop + N / 2.0) / Rate;
            }

            public double Power(int t, int bin)
            {
                return Tones[t][bin - FirstBin];
            }
        }

        /// <summary>The start signals heard in the sound (samples at Rate), the clearest first.</summary>
        public static List<SwimBeep> Find(float[] samples)
        {
            List<SwimBeep> result = new List<SwimBeep>();
            if (samples == null || samples.Length < N + 2 * Rate)
                return result;

            Spectrum s = Analyse(samples);
            List<Event> events = Events(s);
            if (events.Count == 0)
                return result;

            // The quiet before the start: the level of the 0.6–0.1 s before, from the usual level of the recording.
            List<double> levels = new List<double>();
            for (int t = 0; t < s.Frames; t++)
            {
                if (s.Valid[t])
                    levels.Add(s.Level[t]);
            }

            double usual = levels.Count > 0 ? Median(levels) : 0;
            foreach (Event e in events)
            {
                int a = Math.Max(0, e.Frame - (int)(0.6 * Rate / Hop)), b = Math.Max(a + 1, e.Frame - (int)(0.1 * Rate / Hop));
                List<double> before = new List<double>();
                for (int t = a; t < b && t < s.Frames; t++)
                    before.Add(s.Level[t]);
                double quiet = before.Count > 0 ? Median(before) - usual : 0;
                double score = e.Score - 0.5 * Math.Min(10, Math.Max(-10, quiet));
                double onset = Onset(samples, s.Time(e.Frame), e.Low, e.High);
                result.Add(new SwimBeep
                {
                    Onset = onset, Score = score, Rise = e.Rise, Quiet = quiet, Low = e.Low, High = e.High, Together = e.Together,
                    Single = e.Single, Duration = e.Duration, Purity = e.Purity
                });
            }

            result = result.OrderByDescending(x => x.Score).ToList();
            if (Trace != null)
            {
                foreach (SwimBeep x in result.Take(5))
                    Trace(string.Format(CultureInfo.InvariantCulture, "beep {0:0.000} s score {1:0.0} rise {2:0.0} quiet {3:+0.0;-0.0} tones {4:0}/{5:0} {6}",
                        x.Onset, x.Score, x.Rise, x.Quiet, x.Low, x.High,
                        x.Single ? string.Format(CultureInfo.InvariantCulture, "one tone {0:0.00} s, {1:0%} of the sound", x.Duration, x.Purity)
                        : x.Together ? "together" : "one after the other"));
            }

            return result;
        }

        /// <summary>Frames of 64 ms every 5 ms (Hann window): the bins of the tones, the background, the level.</summary>
        private static Spectrum Analyse(float[] x)
        {
            int frames = (x.Length - N) / Hop + 1;
            Spectrum s = new Spectrum
            {
                Frames = frames,
                Tones = new float[frames][],
                Background = new double[frames],
                Level = new double[frames],
                Valid = new bool[frames]
            };

            double[] window = new double[N];
            for (int n = 0; n < N; n++)
                window[n] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / N);

            // The power as numpy's stft (window sum scaling): absolute levels only matter for the digital silence.
            double scale = 1.0 / ((N / 2.0) * (N / 2.0));
            int bgFrom = (int)(600 / Bin), bgTo = (int)(1500 / Bin);
            Parallel.For(0, (frames + 255) / 256, chunk =>
            {
                double[] re = new double[N], im = new double[N];
                double[] bg = new double[bgTo - bgFrom];
                for (int t = chunk * 256; t < Math.Min(frames, (chunk + 1) * 256); t++)
                {
                    int o = t * Hop;
                    for (int n = 0; n < N; n++)
                    {
                        re[n] = x[o + n] * window[n];
                        im[n] = 0;
                    }

                    Fft(re, im);
                    double total = 0, level = 0;
                    float[] tones = new float[LastBin - FirstBin + 1];
                    for (int b = 0; b < 256; b++)
                    {
                        double p = (re[b] * re[b] + im[b] * im[b]) * scale;
                        if (b >= 5 && b < 250)
                            total += p;
                        if (b >= 20)
                            level += p;
                        if (b >= bgFrom && b < bgTo)
                            bg[b - bgFrom] = p;
                        if (b >= FirstBin && b <= LastBin)
                            tones[b - FirstBin] = (float)p;
                    }

                    s.Tones[t] = tones;
                    s.Valid[t] = total > 1e-14 * N;
                    s.Level[t] = 10 * Math.Log10(level + 1e-30);
                    Array.Sort(bg);
                    int m = bg.Length;
                    s.Background[t] = (m % 2 == 1 ? bg[m / 2] : 0.5 * (bg[m / 2 - 1] + bg[m / 2])) + 1e-30;
                }
            });

            return s;
        }

        private sealed class Event
        {
            public int Frame;
            public int Bin;
            public double Rise;
            public double Score;
            public double Low, High;
            public bool Together;
            public bool Single;
            public double Duration;
            public double Purity;
        }

        /// <summary>The two tones on together or one after the other, for each lower tone of 860–950 Hz; one event per 0.25 s.</summary>
        private static List<Event> Events(Spectrum s)
        {
            int T = s.Frames;
            List<Event> all = new List<Event>();
            for (int b1 = (int)Math.Floor(860 / Bin); b1 <= (int)Math.Ceiling(950 / Bin); b1++)
            {
                double bc = b1 * Ratio;
                int b2a = (int)Math.Floor(bc), b2b = (int)Math.Ceiling(bc);
                double[][] energy = new double[2][];
                energy[0] = new double[T];
                energy[1] = new double[T];
                for (int t = 0; t < T; t++)
                {
                    energy[0][t] = ToneEnergy(s, t, b1);
                    energy[1][t] = Math.Max(ToneEnergy(s, t, b2a), ToneEnergy(s, t, b2b));
                }

                bool[][] on = new bool[2][];
                double[][] rise = new double[2][];
                for (int i = 0; i < 2; i++)
                {
                    double[] before = Before(energy[i], s.Valid);
                    on[i] = new bool[T];
                    rise[i] = new double[T];
                    for (int t = 0; t < T; t++)
                    {
                        double e = energy[i][t];
                        double c = 10 * Math.Log10(e / (3 * s.Background[t]));
                        double r = double.IsNaN(before[t]) ? double.NaN : 10 * Math.Log10(e / before[t]);
                        rise[i][t] = r;
                        on[i][t] = !double.IsNaN(r) && c > 6 && r > 8;
                    }
                }

                List<Tuple<int, int, bool>> found = new List<Tuple<int, int, bool>>();

                // Together.
                bool[] both = new bool[T];
                for (int t = 0; t < T; t++)
                    both[t] = on[0][t] && on[1][t];
                for (int t = 1; t < T - Sustain;)
                {
                    if (both[t] && !both[t - 1] && Fraction(both, t, Sustain) >= 0.6)
                    {
                        int t0 = t;
                        for (int i = 0; i < 2; i++)
                        {
                            int k = t;
                            while (k > 0 && t - k < Lead && on[i][k - 1])
                                k--;
                            t0 = Math.Min(t0, k);
                        }

                        found.Add(Tuple.Create(t0, t, true));
                        t += Sustain;
                    }
                    else
                    {
                        t++;
                    }
                }

                // One after the other.
                List<int>[] starts = { Sustained(on[0]), Sustained(on[1]) };
                for (int i = 0; i < 2; i++)
                {
                    foreach (int t in starts[i])
                    {
                        int next = -1;
                        foreach (int u in starts[1 - i])
                        {
                            if (u > t && u <= t + Sequence && (next < 0 || u < next))
                                next = u;
                        }

                        if (next >= 0)
                            found.Add(Tuple.Create(t, next, false));
                    }
                }

                foreach (Tuple<int, int, bool> f in found)
                {
                    int t0 = f.Item1, t1 = f.Item2;
                    double ra = MaxOf(MedianRise(rise[0], t0), MedianRise(rise[0], t1));
                    double rb = MaxOf(MedianRise(rise[1], t1), MedianRise(rise[1], t0));
                    if (double.IsNaN(ra) || double.IsNaN(rb))
                        continue;

                    double low, high;
                    ExactTones(s, t0, t1 + Sustain, b1, bc, out low, out high);
                    double r = (ra + rb) / 2;
                    double off = Math.Abs(high / Math.Max(1e-9, low) / Ratio - 1);
                    double score = r * (off <= 0.006 ? 1.0 : off <= 0.015 ? 0.7 : 0.5) * (low >= 878 && low <= 930 ? 1.0 : 0.85) * (f.Item3 ? 1.0 : 0.9);
                    all.Add(new Event
                    {
                        Frame = t0, Bin = b1, Rise = r, Score = score, Low = low, High = high, Together = f.Item3
                    });
                }
            }

            // One event per 0.25 s: the clearest, from the first onset of them. The single tones (2.8.3) apart: the two tones of a
            // signal are each a tone too, and a sound at the onset of a single tone can look like two tones for a moment.
            List<Event> result = OnePerQuarter(s, all, true);
            result.AddRange(SingleTones(s));
            return result;
        }

        /// <summary>One event per 0.25 s: the clearest, from the first onset of them (else at its own onset).</summary>
        private static List<Event> OnePerQuarter(Spectrum s, List<Event> all, bool fromFirst)
        {
            List<Event> result = new List<Event>();
            foreach (Event e in all.OrderBy(x => x.Frame))
            {
                Event last = result.Count > 0 ? result[result.Count - 1] : null;
                if (last != null && s.Time(e.Frame) - s.Time(last.Frame) < 0.25)
                {
                    if (e.Score > last.Score)
                    {
                        int first = Math.Min(last.Frame, e.Frame);
                        result[result.Count - 1] = e;
                        if (fromFirst)
                            e.Frame = first;
                    }
                }
                else
                {
                    result.Add(e);
                }
            }

            return result;
        }

        /// <summary>
        /// One tone (since 2.8.3), for each bin of 520–1550 Hz: on when it is 10 dB above the usual level of the neighbouring
        /// frequencies and 10 dB above its own level of the half second before; it comes on and stays (most of 0.1 s) and lasts
        /// 0.18–1 s (gaps up to 50 ms). Score: its median rise over 0.1 s (at most 35 dB) × 0.8, × 0.8 more when it lasts less than
        /// 0.25 s or more than 0.75 s, × its share of the sound while it lasts (Purity: the start signal is most of the sound, 70–95 %
        /// in the recordings of the Multinations meets; a voice, music or the stands are many frequencies). One per 0.25 s (a tone is
        /// heard in the frequencies next to it and in its harmonics too): the clearest.
        /// </summary>
        private static List<Event> SingleTones(Spectrum s)
        {
            int T = s.Frames;
            List<Event> result = new List<Event>();
            int gap = (int)(0.05 * Rate / Hop), longest = (int)(SingleMaxSeconds * Rate / Hop);
            for (int b = (int)Math.Ceiling(SingleFrom / Bin); b <= (int)Math.Floor(SingleTo / Bin); b++)
            {
                double[] energy = new double[T];
                bool any = false;
                for (int t = 0; t < T; t++)
                {
                    energy[t] = ToneEnergy(s, t, b);
                    if (s.Valid[t] && 10 * Math.Log10(energy[t] / (3 * s.Background[t])) > SingleAbove)
                        any = true;
                }

                // Most frequencies never stand out: the levels before are only needed where one does.
                if (!any)
                    continue;

                double[] before = Before(energy, s.Valid);
                bool[] on = new bool[T];
                double[] rise = new double[T];
                for (int t = 0; t < T; t++)
                {
                    double c = 10 * Math.Log10(energy[t] / (3 * s.Background[t]));
                    double r = double.IsNaN(before[t]) ? double.NaN : 10 * Math.Log10(energy[t] / before[t]);
                    rise[t] = r;
                    on[t] = !double.IsNaN(r) && c > SingleAbove && r > SingleRise;
                }

                foreach (int t0 in Sustained(on))
                {
                    // How long it lasts: on, with gaps of at most 50 ms (above the neighbours, not necessarily still rising).
                    int last = t0, t = t0;
                    while (t < T && t - t0 <= longest + gap)
                    {
                        double c = 10 * Math.Log10(energy[t] / (3 * s.Background[t]));
                        if (c > SingleAbove - 3)
                            last = t;
                        else if (t - last > gap)
                            break;
                        t++;
                    }

                    double seconds = (last - t0 + 1) * (double)Hop / Rate;
                    if (seconds < SingleMinSeconds || seconds > SingleMaxSeconds)
                        continue;

                    double r0 = MedianRise(rise, t0);
                    if (double.IsNaN(r0))
                        continue;

                    int peak;
                    double f = SinglePeak(s, t0, last + 1, b, out peak);
                    if (f < SingleFrom || f > SingleTo)
                        continue;

                    double purity = Purity(s, t0, last + 1, peak);
                    double score = r0 * SingleWeight * (seconds >= 0.25 && seconds <= 0.75 ? 1.0 : 0.8) * purity;
                    result.Add(new Event
                    {
                        Frame = t0, Bin = peak, Rise = r0, Score = score, Low = f, High = f, Together = true, Single = true, Duration = seconds,
                        Purity = purity
                    });
                }
            }

            return OnePerQuarter(s, result, false);
        }

        /// <summary>
        /// Exact frequency of a single tone: the strongest bin of the mean power of its frames within 3 bins of the bin where it was
        /// heard (peak), and the parabola of the log power there.
        /// </summary>
        private static double SinglePeak(Spectrum s, int from, int to, int bin, out int peak)
        {
            to = Math.Min(s.Frames, Math.Max(from + 1, to));
            double[] mean = new double[LastBin - FirstBin + 1];
            for (int t = from; t < to; t++)
            {
                for (int k = 0; k < mean.Length; k++)
                    mean[k] += s.Tones[t][k];
            }

            int lo = Math.Max(FirstBin + 1, bin - 3), hi = Math.Min(LastBin - 1, bin + 3);
            peak = lo;
            for (int k = lo + 1; k <= hi; k++)
            {
                if (mean[k - FirstBin] > mean[peak - FirstBin])
                    peak = k;
            }

            double l0 = Math.Log(mean[peak - 1 - FirstBin] + 1e-30), l1 = Math.Log(mean[peak - FirstBin] + 1e-30), l2 = Math.Log(mean[peak + 1 - FirstBin] + 1e-30);
            double den = l0 - 2 * l1 + l2;
            double off = den < 0 ? Math.Max(-0.5, Math.Min(0.5, 0.5 * (l0 - l2) / den)) : 0;
            return (peak + off) * Bin;
        }

        /// <summary>Share of the power of 312–3984 Hz (the level of the frames) in a tone (its bin and the two next to it) over these frames.</summary>
        private static double Purity(Spectrum s, int from, int to, int peak)
        {
            double tone = 0, total = 0;
            for (int t = from; t < Math.Min(s.Frames, to); t++)
            {
                if (!s.Valid[t])
                    continue;

                tone += s.Power(t, peak - 1) + s.Power(t, peak) + s.Power(t, peak + 1);
                total += Math.Pow(10, s.Level[t] / 10);
            }

            return total > 0 ? Math.Min(1, tone / total) : 0;
        }

        /// <summary>A tone near this bin: its power and the stronger neighbour.</summary>
        private static double ToneEnergy(Spectrum s, int t, int bin)
        {
            return s.Power(t, bin) + Math.Max(s.Power(t, bin - 1), s.Power(t, bin + 1));
        }

        /// <summary>Median of the 0.5–0.05 s before each frame (by steps of 4 frames), NaN without sound all along.</summary>
        private static double[] Before(double[] v, bool[] valid)
        {
            int T = v.Length;
            double[] result = new double[T];
            for (int t = 0; t < T; t++)
                result[t] = double.NaN;
            double[] buffer = new double[BeforeFrom - BeforeTo];
            for (int t = BeforeFrom; t < T; t += 4)
            {
                bool ok = true;
                for (int k = t - BeforeFrom; k < t - BeforeTo && ok; k++)
                    ok = valid[k];
                if (!ok)
                    continue;

                Array.Copy(v, t - BeforeFrom, buffer, 0, buffer.Length);
                Array.Sort(buffer);
                int m = buffer.Length;
                double median = m % 2 == 1 ? buffer[m / 2] : 0.5 * (buffer[m / 2 - 1] + buffer[m / 2]);
                for (int k = t; k < Math.Min(T, t + 4); k++)
                    result[k] = median;
            }

            return result;
        }

        private static double Fraction(bool[] on, int from, int count)
        {
            int n = 0, c = 0;
            for (int t = from; t < Math.Min(on.Length, from + count); t++)
            {
                n++;
                if (on[t])
                    c++;
            }

            return n > 0 ? (double)c / n : 0;
        }

        /// <summary>Frames where a tone comes on and stays (most of 0.1 s).</summary>
        private static List<int> Sustained(bool[] on)
        {
            List<int> result = new List<int>();
            for (int t = 1; t < on.Length - Sustain;)
            {
                if (on[t] && !on[t - 1] && Fraction(on, t, Sustain) >= 0.7)
                {
                    result.Add(t);
                    t += Sustain;
                }
                else
                {
                    t++;
                }
            }

            return result;
        }

        /// <summary>The larger of two values, NaN only when both are.</summary>
        private static double MaxOf(double a, double b)
        {
            return double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Max(a, b);
        }

        /// <summary>Median rise of a tone over 0.1 s from a frame (each at most 35 dB; NaN frames left out).</summary>
        private static double MedianRise(double[] rise, int from)
        {
            List<double> v = new List<double>();
            for (int t = from; t < Math.Min(rise.Length, from + Sustain); t++)
            {
                if (!double.IsNaN(rise[t]))
                    v.Add(Math.Min(rise[t], 35));
            }

            return v.Count > 0 ? Median(v) : double.NaN;
        }

        /// <summary>Exact frequencies of the two tones: peaks of the mean power of the frames (parabola of the log power).</summary>
        private static void ExactTones(Spectrum s, int from, int to, int b1, double bc, out double low, out double high)
        {
            to = Math.Min(s.Frames, Math.Max(from + 1, to));
            double[] mean = new double[LastBin - FirstBin + 1];
            for (int t = from; t < to; t++)
            {
                for (int b = 0; b < mean.Length; b++)
                    mean[b] += s.Tones[t][b];
            }

            low = Peak(mean, b1);
            high = Peak(mean, bc);
        }

        private static double Peak(double[] mean, double near)
        {
            int b = (int)Math.Round(near, MidpointRounding.ToEven);
            int k = b - 1;
            for (int j = b; j <= b + 1; j++)
            {
                if (mean[j - FirstBin] > mean[k - FirstBin])
                    k = j;
            }

            double l0 = Math.Log(mean[k - 1 - FirstBin] + 1e-30), l1 = Math.Log(mean[k - FirstBin] + 1e-30), l2 = Math.Log(mean[k + 1 - FirstBin] + 1e-30);
            double den = l0 - 2 * l1 + l2;
            double off = den < 0 ? 0.5 * (l0 - l2) / den : 0;
            return (k + off) * Bin;
        }

        /// <summary>
        /// Onset of the signal near 'time' (s): envelope of the two tones (each demodulated, low pass 40 Hz forward and backward),
        /// its peak in the 0.10 s before to 0.25 s after, back to where it is below the level before + 20 % of the rise.
        /// </summary>
        private static double Onset(float[] x, double time, double low, double high)
        {
            int a0 = Math.Max(0, (int)((time - 0.45) * Rate)), b0 = Math.Min(x.Length, (int)((time + 0.45) * Rate));
            int n = b0 - a0;
            if (n < Rate / 4)
                return time;

            double[] envelope = new double[n];
            foreach (double f in new[] { low, high })
            {
                double[] re = new double[n], im = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double phase = -2 * Math.PI * f * (a0 + i) / Rate;
                    re[i] = x[a0 + i] * Math.Cos(phase);
                    im[i] = x[a0 + i] * Math.Sin(phase);
                }

                LowPassBothWays(re, 40, Rate);
                LowPassBothWays(im, 40, Rate);
                for (int i = 0; i < n; i++)
                    envelope[i] += re[i] * re[i] + im[i] * im[i];
            }

            for (int i = 0; i < n; i++)
                envelope[i] = Math.Sqrt(envelope[i]);

            int i0 = Math.Max(0, (int)((time - 0.10) * Rate) - a0), i1 = Math.Min(n, (int)((time + 0.25) * Rate) - a0);
            if (i1 <= i0)
                return time;

            int peak = i0;
            for (int i = i0; i < i1; i++)
            {
                if (envelope[i] > envelope[peak])
                    peak = i;
            }

            int n0 = Math.Max(0, (int)((time - 0.40) * Rate) - a0), n1 = Math.Max(n0 + 1, (int)((time - 0.08) * Rate) - a0);
            List<double> before = new List<double>();
            for (int i = n0; i < Math.Min(n, n1); i++)
                before.Add(envelope[i]);
            double noise = before.Count > 0 ? Median(before) : 0;
            double threshold = noise + 0.2 * (envelope[peak] - noise);
            int k = peak;
            while (k > 0 && envelope[k - 1] > threshold)
                k--;

            return (double)(a0 + k) / Rate;
        }

        /// <summary>Second order Butterworth low pass, forward then backward (no delay).</summary>
        private static void LowPassBothWays(double[] v, double cutoff, double rate)
        {
            double k = Math.Tan(Math.PI * cutoff / rate);
            double norm = 1 / (1 + Math.Sqrt(2) * k + k * k);
            double b0 = k * k * norm, b1 = 2 * b0, b2 = b0;
            double a1 = 2 * (k * k - 1) * norm, a2 = (1 - Math.Sqrt(2) * k + k * k) * norm;
            for (int pass = 0; pass < 2; pass++)
            {
                // Started at the first value (as if it had always been there): no step at the edge.
                double x1 = v[pass == 0 ? 0 : v.Length - 1], x2 = x1, y1 = x1, y2 = x1;
                for (int j = 0; j < v.Length; j++)
                {
                    int i = pass == 0 ? j : v.Length - 1 - j;
                    double x0 = v[i];
                    double y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                    x2 = x1;
                    x1 = x0;
                    y2 = y1;
                    y1 = y0;
                    v[i] = y0;
                }
            }
        }

        /// <summary>In-place radix-2 FFT (length a power of two).</summary>
        private static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                    j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double tr = re[i]; re[i] = re[j]; re[j] = tr;
                    double ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2 * Math.PI / len;
                double wr = Math.Cos(angle), wi = Math.Sin(angle);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, cim = 0;
                    for (int j = 0; j < len / 2; j++)
                    {
                        int a = i + j, b = i + j + len / 2;
                        double ur = re[a], ui = im[a];
                        double vr = re[b] * cr - im[b] * cim, vi = re[b] * cim + im[b] * cr;
                        re[a] = ur + vr;
                        im[a] = ui + vi;
                        re[b] = ur - vr;
                        im[b] = ui - vi;
                        double nr = cr * wr - cim * wi;
                        cim = cr * wi + cim * wr;
                        cr = nr;
                    }
                }
            }
        }

        private static double Median(List<double> values)
        {
            List<double> v = values.OrderBy(x => x).ToList();
            int n = v.Count;
            return n == 0 ? double.NaN : n % 2 == 1 ? v[n / 2] : 0.5 * (v[n / 2 - 1] + v[n / 2]);
        }
        #endregion
    }
}
