#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.14.1) How the sound of a second camera lines up with the sound of a first one: the moment at sample i of the first sound is at
    /// sample i + Lag of the second (SwimSoundSync.Rate samples per second, both sounds from the start of what was read).
    /// </summary>
    public sealed class SwimSoundMatch
    {
        public double Lag;

        /// <summary>The low and the high sounds give the same moment, each clearly, and the waveform agrees with them.</summary>
        public bool Reliable;

        /// <summary>The best match over the next best one away from it (low sounds, high sounds).</summary>
        public double Clarity, ClarityHigh;

        /// <summary>Seconds between the low and the high sounds' match.</summary>
        public double Spread;

        /// <summary>Seconds between the waveform's match and the sounds' one (NaN: not checked).</summary>
        public double Check = double.NaN;

        public double LagSeconds
        {
            get { return Lag / SwimSoundSync.Rate; }
        }
    }

    /// <summary>
    /// (2.14.1) Cameras of the same moment lined up by their sound: what all of them heard (whistles, the start signal, voices, the
    /// splashes) gives the difference of their clocks, to the millisecond. The onsets of the sounds every 5 ms (low and high sounds apart)
    /// are matched over the whole sound, and the waveform of the 20 s with the most sounds checks the match (phase correlation).
    /// Lab (2026-10-29, 6 takes of 3 cameras: above the water and under it on one pole, and under the water on the wall): the two
    /// cameras of the pole matched in all 6 (within 7 ms between the ways of matching), the camera on the wall in none (its sound is the
    /// water at the wall): it is told, not lined up.
    /// </summary>
    public static class SwimSoundSync
    {
        /// <summary>Samples per second of the sounds compared (the sound read at SwimStartSound.Rate, halved).</summary>
        public const int Rate = 8000;

        private const int Window = 256;
        private const int Hop = 40;

        /// <summary>Seconds between two onset values.</summary>
        public const double HopSeconds = (double)Hop / Rate;

        /// <summary>The low and the high sounds' match agree within this (s).</summary>
        public const double MaxSpread = 0.015;

        /// <summary>
        /// The best match at least this many times the next best one (low sounds; high sounds: MinClarityHigh). The cameras of the pole
        /// 2.5–4.5 (low sounds) in the lab, the wall camera 1.0–1.5 against them (its matches also far apart between the ways of matching).
        /// </summary>
        public const double MinClarity = 1.6;
        public const double MinClarityHigh = 1.4;

        /// <summary>The waveform checked over this many seconds of the first sound, its match looked for this far around the sounds' one.</summary>
        public const double CheckSeconds = 20;
        public const double CheckSlack = 2;

        /// <summary>At most this many seconds of each video are compared.</summary>
        public const double MaxSeconds = 300;

        /// <summary>The sound read at SwimStartSound.Rate (16 kHz) to Rate (the mean of two samples).</summary>
        public static float[] Half(float[] x)
        {
            if (x == null)
                return null;

            float[] y = new float[x.Length / 2];
            for (int i = 0; i < y.Length; i++)
                y[i] = 0.5f * (x[2 * i] + x[2 * i + 1]);
            return y;
        }

        /// <summary>
        /// The onsets of the sound between lo and hi Hz every HopSeconds: the rise of the log magnitudes over the previous 32 ms, above its
        /// half-second mean, to zero mean and unit deviation.
        /// </summary>
        public static double[] Onsets(float[] x, double lo, double hi)
        {
            int n = x == null || x.Length < Window ? 0 : 1 + (x.Length - Window) / Hop;
            double[] flux = new double[n];
            if (n == 0)
                return flux;

            double[] hann = new double[Window];
            for (int i = 0; i < Window; i++)
                hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Window - 1));

            int b0 = Math.Max(1, (int)Math.Ceiling(lo * Window / Rate)), b1 = Math.Min(Window / 2, (int)Math.Floor(hi * Window / Rate));
            if (b1 < b0)
                return new double[n];

            double[] re = new double[Window], im = new double[Window];
            double[] cur = new double[b1 - b0 + 1], prev = new double[b1 - b0 + 1];
            for (int t = 0; t < n; t++)
            {
                int s = t * Hop;
                for (int i = 0; i < Window; i++)
                {
                    re[i] = x[s + i] * hann[i];
                    im[i] = 0;
                }

                SwimStartSound.Fft(re, im);
                double f = 0;
                for (int k = b0; k <= b1; k++)
                {
                    double v = Math.Log(1 + 1000 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
                    if (t > 0 && v > prev[k - b0])
                        f += v - prev[k - b0];
                    cur[k - b0] = v;
                }

                flux[t] = f;
                double[] swap = prev;
                prev = cur;
                cur = swap;
            }

            // Above the half-second mean only (the zeros past the ends counted, as the sounds compared are cut the same way).
            int half = (int)(0.5 * Rate / Hop) / 2;
            int width = 2 * half + 1;
            double[] sum = new double[n + 1];
            for (int t = 0; t < n; t++)
                sum[t + 1] = sum[t] + flux[t];

            double[] e = new double[n];
            double mean = 0;
            for (int t = 0; t < n; t++)
            {
                int a = Math.Max(0, t - half), b = Math.Min(n, t + half + 1);
                double v = flux[t] - (sum[b] - sum[a]) / width;
                e[t] = v > 0 ? v : 0;
                mean += e[t];
            }

            mean /= n;
            double sd = 0;
            for (int t = 0; t < n; t++)
                sd += (e[t] - mean) * (e[t] - mean);
            sd = Math.Sqrt(sd / n);
            for (int t = 0; t < n; t++)
                e[t] = sd > 0 ? (e[t] - mean) / sd : 0;
            return e;
        }

        /// <summary>
        /// The correlation c[l] = Σ a[t] b[t + l] for l from -maxLag to maxLag (at index l + maxLag): b later by l. Whitened (the phase only,
        /// between lo and hi Hz at 'rate') when asked.
        /// </summary>
        public static double[] Correlate(double[] a, double[] b, int maxLag, bool whiten = false, double rate = 0, double lo = 0, double hi = 0)
        {
            int n = a.Length + b.Length, m = 1;
            while (m < n)
                m <<= 1;

            double[] ar = new double[m], ai = new double[m], br = new double[m], bi = new double[m];
            Array.Copy(a, ar, a.Length);
            Array.Copy(b, br, b.Length);
            SwimStartSound.Fft(ar, ai);
            SwimStartSound.Fft(br, bi);
            for (int k = 0; k < m; k++)
            {
                double gr = ar[k] * br[k] + ai[k] * bi[k];
                double gi = ar[k] * bi[k] - ai[k] * br[k];
                if (whiten)
                {
                    double f = (k <= m / 2 ? k : m - k) * rate / m;
                    double mag = Math.Sqrt(gr * gr + gi * gi);
                    if (f < lo || f > hi || mag < 1e-12)
                    {
                        gr = 0;
                        gi = 0;
                    }
                    else
                    {
                        gr /= mag;
                        gi /= mag;
                    }
                }

                // The inverse transform as the forward one of the conjugate.
                ar[k] = gr;
                ai[k] = -gi;
            }

            SwimStartSound.Fft(ar, ai);
            maxLag = Math.Max(0, Math.Min(maxLag, m / 2 - 1));
            double[] c = new double[2 * maxLag + 1];
            for (int l = -maxLag; l <= maxLag; l++)
                c[l + maxLag] = ar[l >= 0 ? l : m + l] / m;
            return c;
        }

        /// <summary>
        /// The best value of c (index i = lag + offset) for lags lo..hi, refined between samples, and how much clearer it is than the best
        /// one more than 'exclude' lags away.
        /// </summary>
        private static void Peak(double[] c, int offset, int lo, int hi, int exclude, out double lag, out double clarity)
        {
            lo = Math.Max(lo, -offset);
            hi = Math.Min(hi, c.Length - 1 - offset);
            int best = lo;
            for (int l = lo; l <= hi; l++)
            {
                if (c[l + offset] > c[best + offset])
                    best = l;
            }

            double d = 0;
            int i = best + offset;
            if (best > lo && best < hi)
            {
                double den = c[i - 1] - 2 * c[i] + c[i + 1];
                if (den < 0)
                    d = 0.5 * (c[i - 1] - c[i + 1]) / den;
            }

            double second = double.MinValue;
            for (int l = lo; l <= hi; l++)
            {
                if (Math.Abs(l - best) > exclude && c[l + offset] > second)
                    second = c[l + offset];
            }

            lag = best + d;
            clarity = second > 0 ? c[i] / second : (c[i] > 0 ? 99 : 0);
        }

        /// <summary>
        /// How the second sound lines up with the first (both at Rate, as read from their videos), looked for within maxSeconds either way.
        /// Null without enough sound (2 s).
        /// </summary>
        public static SwimSoundMatch Match(float[] first, float[] second, double maxSeconds)
        {
            if (first == null || second == null || first.Length < 2 * Rate || second.Length < 2 * Rate)
                return null;

            SwimSoundMatch m = new SwimSoundMatch();
            int maxLag = (int)Math.Ceiling(maxSeconds / HopSeconds);
            int exclude = (int)Math.Round(0.25 / HopSeconds);

            double[] lowA = Onsets(first, 100, 1000), lowB = Onsets(second, 100, 1000);
            double lagLow, lagHigh;
            double[] c = Correlate(lowA, lowB, maxLag);
            int off = (c.Length - 1) / 2;
            Peak(c, off, -off, off, exclude, out lagLow, out m.Clarity);

            double[] highA = Onsets(first, 300, 3800), highB = Onsets(second, 300, 3800);
            c = Correlate(highA, highB, maxLag);
            off = (c.Length - 1) / 2;
            Peak(c, off, -off, off, exclude, out lagHigh, out m.ClarityHigh);

            m.Spread = Math.Abs(lagLow - lagHigh) * HopSeconds;
            double coarse = 0.5 * (lagLow + lagHigh) * Hop;
            m.Lag = coarse;

            // The waveform of the 20 s of the first sound with the most onsets, against the second around the match.
            int w = Math.Min(first.Length, (int)(CheckSeconds * Rate)), slack = (int)(CheckSlack * Rate);
            int hops = w / Hop;
            int a0 = 0;
            if (lowA.Length > hops && hops > 0)
            {
                double run = 0, bestRun = double.MinValue;
                for (int t = 0; t < lowA.Length; t++)
                {
                    run += Math.Max(0, lowA[t]);
                    if (t >= hops)
                        run -= Math.Max(0, lowA[t - hops]);
                    if (t >= hops - 1 && run > bestRun)
                    {
                        bestRun = run;
                        a0 = (t - hops + 1) * Hop;
                    }
                }
            }

            a0 = Math.Max(0, Math.Min(a0, first.Length - w));
            int lc = (int)Math.Round(coarse);
            int b0 = Math.Max(0, a0 + lc - slack), b1 = Math.Min(second.Length, a0 + lc + w + slack);
            if (b1 - b0 > Rate && w > Rate)
            {
                double[] aw = new double[w], bw = new double[b1 - b0];
                for (int i = 0; i < w; i++)
                    aw[i] = first[a0 + i];
                for (int i = 0; i < bw.Length; i++)
                    bw[i] = second[b0 + i];

                c = Correlate(aw, bw, bw.Length + w, true, Rate, 150, 3500);
                off = (c.Length - 1) / 2;

                // The lag in the sounds is b0 + l - a0 for the lag l of the windows: around the sounds' match only.
                double l3, q3;
                Peak(c, off, lc - slack - b0 + a0, lc + slack - b0 + a0, (int)(0.05 * Rate), out l3, out q3);
                double lag3 = b0 + l3 - a0;
                m.Check = Math.Abs(lag3 - coarse) / Rate;
                if (m.Check <= MaxSpread)
                    m.Lag = lag3;
            }

            m.Reliable = m.Spread <= MaxSpread && m.Clarity >= MinClarity && m.ClarityHigh >= MinClarityHigh && !double.IsNaN(m.Check) && m.Check <= MaxSpread;
            return m;
        }
    }
}
