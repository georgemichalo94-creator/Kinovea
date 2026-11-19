// The start signal heard in the sound of a video.
// Port of the detection of the desktop program (Kinovea.ScreenManager/SwimAnalysis/SwimStartSound.cs, region "Detection"):
// the same frames (64 ms every 5 ms at 16 kHz), the same rules and the same scores, so that a signal reads the same on
// the phone and on the computer. The function is self-contained: it also runs in a worker from its own source.
function swimHornFactory() {
  'use strict';

  var Rate = 16000, N = 1024, Hop = 80, Bin = Rate / N, Ratio = 8 / 7, FirstBin = 32, LastBin = 103;
  var Bins = LastBin - FirstBin + 1;
  var SingleFrom = 520, SingleTo = 1550, SingleAbove = 10, SingleRise = 10;
  var SingleMinSeconds = 0.18, SingleMaxSeconds = 1.0, SingleWeight = 0.8;
  var Sustain = 20, Lead = 50, Sequence = 70, BeforeFrom = 100, BeforeTo = 10;

  /** Weaker signals than this (dB) are not a start; single tones from SingleMinScore. */
  var MinScore = 10, SingleMinScore = 5, AmbiguousShare = 0.75;

  function median(values) {
    var v = Float64Array.from(values).sort(), n = v.length;
    return n === 0 ? NaN : n % 2 === 1 ? v[(n - 1) / 2] : 0.5 * (v[n / 2 - 1] + v[n / 2]);
  }

  function log10(x) { return Math.log(x) / Math.LN10; }

  function roundHalfEven(x) {
    var f = Math.floor(x), d = x - f;
    if (d < 0.5) return f;
    if (d > 0.5) return f + 1;
    return f % 2 === 0 ? f : f + 1;
  }

  // In-place radix-2 FFT with the tables of one length.
  function makeFft(n) {
    var rev = new Uint32Array(n), cos = new Float64Array(n / 2), sin = new Float64Array(n / 2), i, j, bit;
    for (i = 1, j = 0; i < n; i++) {
      bit = n >> 1;
      for (; j & bit; bit >>= 1) j ^= bit;
      j ^= bit;
      rev[i] = j;
    }
    for (i = 0; i < n / 2; i++) {
      cos[i] = Math.cos(-2 * Math.PI * i / n);
      sin[i] = Math.sin(-2 * Math.PI * i / n);
    }
    return function (re, im) {
      var a, b, k, len, half, step, tr, ti, ur, ui, vr, vi, wr, wi;
      for (a = 1; a < n; a++) {
        b = rev[a];
        if (a < b) {
          tr = re[a]; re[a] = re[b]; re[b] = tr;
          ti = im[a]; im[a] = im[b]; im[b] = ti;
        }
      }
      for (len = 2; len <= n; len <<= 1) {
        half = len >> 1; step = n / len;
        for (a = 0; a < n; a += len) {
          for (k = 0; k < half; k++) {
            wr = cos[k * step]; wi = sin[k * step];
            b = a + k + half;
            ur = re[a + k]; ui = im[a + k];
            vr = re[b] * wr - im[b] * wi; vi = re[b] * wi + im[b] * wr;
            re[a + k] = ur + vr; im[a + k] = ui + vi;
            re[b] = ur - vr; im[b] = ui - vi;
          }
        }
      }
    };
  }

  /** Frames of 64 ms every 5 ms (Hann window): the bins of the tones, the background, the level. */
  function analyse(x) {
    var frames = Math.floor((x.length - N) / Hop) + 1;
    var s = {
      frames: frames,
      tones: new Float32Array(frames * Bins),
      background: new Float64Array(frames),
      level: new Float64Array(frames),
      valid: new Uint8Array(frames)
    };
    var window = new Float64Array(N), n, t, b, o, p, total, level;
    for (n = 0; n < N; n++) window[n] = 0.5 - 0.5 * Math.cos(2 * Math.PI * n / N);
    var scale = 1 / ((N / 2) * (N / 2));
    var bgFrom = Math.floor(600 / Bin), bgTo = Math.floor(1500 / Bin), m = bgTo - bgFrom;
    var re = new Float64Array(N), im = new Float64Array(N), bg = new Float64Array(m), fft = makeFft(N);
    for (t = 0; t < frames; t++) {
      o = t * Hop;
      for (n = 0; n < N; n++) { re[n] = x[o + n] * window[n]; im[n] = 0; }
      fft(re, im);
      total = 0; level = 0;
      for (b = 0; b < 256; b++) {
        p = (re[b] * re[b] + im[b] * im[b]) * scale;
        if (b >= 5 && b < 250) total += p;
        if (b >= 20) level += p;
        if (b >= bgFrom && b < bgTo) bg[b - bgFrom] = p;
        if (b >= FirstBin && b <= LastBin) s.tones[t * Bins + b - FirstBin] = p;
      }
      s.valid[t] = total > 1e-14 * N ? 1 : 0;
      s.level[t] = 10 * log10(level + 1e-30);
      bg.sort();
      s.background[t] = (m % 2 === 1 ? bg[(m - 1) / 2] : 0.5 * (bg[m / 2 - 1] + bg[m / 2])) + 1e-30;
    }
    return s;
  }

  function time(t) { return (t * Hop + N / 2) / Rate; }
  function power(s, t, bin) { return s.tones[t * Bins + bin - FirstBin]; }

  /** A tone near this bin: its power and the stronger neighbour. */
  function toneEnergy(s, t, bin) {
    return power(s, t, bin) + Math.max(power(s, t, bin - 1), power(s, t, bin + 1));
  }

  /** Median of the 0.5–0.05 s before each frame (by steps of 4 frames), NaN without sound all along. */
  function before(v, valid) {
    var T = v.length, result = new Float64Array(T).fill(NaN), m = BeforeFrom - BeforeTo, buffer = new Float64Array(m);
    // Frames without sound up to each frame, to know at once whether the half second before has sound all along.
    var silent = new Uint32Array(T + 1), t, k, med;
    for (t = 0; t < T; t++) silent[t + 1] = silent[t] + (valid[t] ? 0 : 1);
    for (t = BeforeFrom; t < T; t += 4) {
      if (silent[t - BeforeTo] - silent[t - BeforeFrom] > 0) continue;
      for (k = 0; k < m; k++) buffer[k] = v[t - BeforeFrom + k];
      buffer.sort();
      med = m % 2 === 1 ? buffer[(m - 1) / 2] : 0.5 * (buffer[m / 2 - 1] + buffer[m / 2]);
      for (k = t; k < Math.min(T, t + 4); k++) result[k] = med;
    }
    return result;
  }

  function fraction(on, from, count) {
    var n = 0, c = 0, t, to = Math.min(on.length, from + count);
    for (t = from; t < to; t++) { n++; if (on[t]) c++; }
    return n > 0 ? c / n : 0;
  }

  /** Frames where a tone comes on and stays (most of 0.1 s). */
  function sustained(on) {
    var result = [], t;
    for (t = 1; t < on.length - Sustain;) {
      if (on[t] && !on[t - 1] && fraction(on, t, Sustain) >= 0.7) { result.push(t); t += Sustain; }
      else t++;
    }
    return result;
  }

  function maxOf(a, b) { return isNaN(a) ? b : isNaN(b) ? a : Math.max(a, b); }

  /** Median rise of a tone over 0.1 s from a frame (each at most 35 dB; NaN frames left out). */
  function medianRise(rise, from) {
    var v = [], t, to = Math.min(rise.length, from + Sustain);
    for (t = from; t < to; t++) if (!isNaN(rise[t])) v.push(Math.min(rise[t], 35));
    return v.length > 0 ? median(v) : NaN;
  }

  function meanPower(s, from, to) {
    to = Math.min(s.frames, Math.max(from + 1, to));
    var mean = new Float64Array(Bins), t, b;
    for (t = from; t < to; t++) for (b = 0; b < Bins; b++) mean[b] += s.tones[t * Bins + b];
    return mean;
  }

  function peak(mean, near) {
    var b = roundHalfEven(near), k = b - 1, j;
    for (j = b; j <= b + 1; j++) if (mean[j - FirstBin] > mean[k - FirstBin]) k = j;
    var l0 = Math.log(mean[k - 1 - FirstBin] + 1e-30), l1 = Math.log(mean[k - FirstBin] + 1e-30), l2 = Math.log(mean[k + 1 - FirstBin] + 1e-30);
    var den = l0 - 2 * l1 + l2, off = den < 0 ? 0.5 * (l0 - l2) / den : 0;
    return (k + off) * Bin;
  }

  /** Exact frequency of a single tone: the strongest bin of the mean power within 3 bins, and the parabola there. */
  function singlePeak(s, from, to, bin) {
    var mean = meanPower(s, from, to);
    var lo = Math.max(FirstBin + 1, bin - 3), hi = Math.min(LastBin - 1, bin + 3), pk = lo, k;
    for (k = lo + 1; k <= hi; k++) if (mean[k - FirstBin] > mean[pk - FirstBin]) pk = k;
    var l0 = Math.log(mean[pk - 1 - FirstBin] + 1e-30), l1 = Math.log(mean[pk - FirstBin] + 1e-30), l2 = Math.log(mean[pk + 1 - FirstBin] + 1e-30);
    var den = l0 - 2 * l1 + l2, off = den < 0 ? Math.max(-0.5, Math.min(0.5, 0.5 * (l0 - l2) / den)) : 0;
    return { hz: (pk + off) * Bin, peak: pk };
  }

  /** Share of the power of 312–3984 Hz in a tone (its bin and the two next to it) over these frames. */
  function purity(s, from, to, pk) {
    var tone = 0, total = 0, t;
    for (t = from; t < Math.min(s.frames, to); t++) {
      if (!s.valid[t]) continue;
      tone += power(s, t, pk - 1) + power(s, t, pk) + power(s, t, pk + 1);
      total += Math.pow(10, s.level[t] / 10);
    }
    return total > 0 ? Math.min(1, tone / total) : 0;
  }

  /** One event per 0.25 s: the clearest, from the first onset of them (else at its own onset). */
  function onePerQuarter(all, fromFirst) {
    var result = [], sorted = all.slice().sort(function (a, b) { return a.frame - b.frame; });
    sorted.forEach(function (e) {
      var last = result.length > 0 ? result[result.length - 1] : null;
      if (last && time(e.frame) - time(last.frame) < 0.25) {
        if (e.score > last.score) {
          var first = Math.min(last.frame, e.frame);
          result[result.length - 1] = e;
          if (fromFirst) e.frame = first;
        }
      } else {
        result.push(e);
      }
    });
    return result;
  }

  /** One tone, for each bin of 520–1550 Hz (see the desktop source for the rule). */
  function singleTones(s) {
    var T = s.frames, result = [], gap = 10, longest = Math.floor(SingleMaxSeconds * Rate / Hop);
    var energy = new Float64Array(T), on = new Uint8Array(T), rise = new Float64Array(T);
    var b, t, any, c, r, bef, starts, i, t0, last, seconds, r0, f, pur, score;
    for (b = Math.ceil(SingleFrom / Bin); b <= Math.floor(SingleTo / Bin); b++) {
      any = false;
      for (t = 0; t < T; t++) {
        energy[t] = toneEnergy(s, t, b);
        if (s.valid[t] && 10 * log10(energy[t] / (3 * s.background[t])) > SingleAbove) any = true;
      }
      // Most frequencies never stand out: the levels before are only needed where one does.
      if (!any) continue;

      bef = before(energy, s.valid);
      for (t = 0; t < T; t++) {
        c = 10 * log10(energy[t] / (3 * s.background[t]));
        r = isNaN(bef[t]) ? NaN : 10 * log10(energy[t] / bef[t]);
        rise[t] = r;
        on[t] = !isNaN(r) && c > SingleAbove && r > SingleRise ? 1 : 0;
      }

      starts = sustained(on);
      for (i = 0; i < starts.length; i++) {
        t0 = starts[i];
        // How long it lasts: on, with gaps of at most 50 ms (above the neighbours, not necessarily still rising).
        last = t0; t = t0;
        while (t < T && t - t0 <= longest + gap) {
          c = 10 * log10(energy[t] / (3 * s.background[t]));
          if (c > SingleAbove - 3) last = t;
          else if (t - last > gap) break;
          t++;
        }
        seconds = (last - t0 + 1) * Hop / Rate;
        if (seconds < SingleMinSeconds || seconds > SingleMaxSeconds) continue;
        r0 = medianRise(rise, t0);
        if (isNaN(r0)) continue;
        f = singlePeak(s, t0, last + 1, b);
        if (f.hz < SingleFrom || f.hz > SingleTo) continue;
        pur = purity(s, t0, last + 1, f.peak);
        score = r0 * SingleWeight * (seconds >= 0.25 && seconds <= 0.75 ? 1.0 : 0.8) * pur;
        result.push({ frame: t0, bin: f.peak, rise: r0, score: score, low: f.hz, high: f.hz, together: true, single: true, duration: seconds, purity: pur });
      }
    }
    return onePerQuarter(result, false);
  }

  /** The two tones on together or one after the other, for each lower tone of 860–950 Hz; one event per 0.25 s. */
  function events(s) {
    var T = s.frames, all = [], b1, bc, b2a, b2b, t, i, k, e, c, r, bef, t0, t1, u, next, ra, rb, mean, low, high, off, score;
    for (b1 = Math.floor(860 / Bin); b1 <= Math.ceil(950 / Bin); b1++) {
      bc = b1 * Ratio; b2a = Math.floor(bc); b2b = Math.ceil(bc);
      var energy = [new Float64Array(T), new Float64Array(T)];
      for (t = 0; t < T; t++) {
        energy[0][t] = toneEnergy(s, t, b1);
        energy[1][t] = Math.max(toneEnergy(s, t, b2a), toneEnergy(s, t, b2b));
      }
      var on = [new Uint8Array(T), new Uint8Array(T)], rise = [new Float64Array(T), new Float64Array(T)];
      for (i = 0; i < 2; i++) {
        bef = before(energy[i], s.valid);
        for (t = 0; t < T; t++) {
          e = energy[i][t];
          c = 10 * log10(e / (3 * s.background[t]));
          r = isNaN(bef[t]) ? NaN : 10 * log10(e / bef[t]);
          rise[i][t] = r;
          on[i][t] = !isNaN(r) && c > 6 && r > 8 ? 1 : 0;
        }
      }

      var found = [];

      // Together.
      var both = new Uint8Array(T);
      for (t = 0; t < T; t++) both[t] = on[0][t] && on[1][t] ? 1 : 0;
      for (t = 1; t < T - Sustain;) {
        if (both[t] && !both[t - 1] && fraction(both, t, Sustain) >= 0.6) {
          t0 = t;
          for (i = 0; i < 2; i++) {
            k = t;
            while (k > 0 && t - k < Lead && on[i][k - 1]) k--;
            t0 = Math.min(t0, k);
          }
          found.push([t0, t, true]);
          t += Sustain;
        } else {
          t++;
        }
      }

      // One after the other.
      var starts = [sustained(on[0]), sustained(on[1])];
      for (i = 0; i < 2; i++) {
        for (k = 0; k < starts[i].length; k++) {
          t = starts[i][k]; next = -1;
          for (u = 0; u < starts[1 - i].length; u++) {
            var v = starts[1 - i][u];
            if (v > t && v <= t + Sequence && (next < 0 || v < next)) next = v;
          }
          if (next >= 0) found.push([t, next, false]);
        }
      }

      for (k = 0; k < found.length; k++) {
        t0 = found[k][0]; t1 = found[k][1];
        ra = maxOf(medianRise(rise[0], t0), medianRise(rise[0], t1));
        rb = maxOf(medianRise(rise[1], t1), medianRise(rise[1], t0));
        if (isNaN(ra) || isNaN(rb)) continue;
        mean = meanPower(s, t0, t1 + Sustain);
        low = peak(mean, b1); high = peak(mean, bc);
        r = (ra + rb) / 2;
        off = Math.abs(high / Math.max(1e-9, low) / Ratio - 1);
        score = r * (off <= 0.006 ? 1.0 : off <= 0.015 ? 0.7 : 0.5) * (low >= 878 && low <= 930 ? 1.0 : 0.85) * (found[k][2] ? 1.0 : 0.9);
        all.push({ frame: t0, bin: b1, rise: r, score: score, low: low, high: high, together: found[k][2], single: false, duration: 0, purity: 0 });
      }
    }

    // One event per 0.25 s: the clearest, from the first onset of them. The single tones apart: the two tones of a signal
    // are each a tone too, and a sound at the onset of a single tone can look like two tones for a moment.
    return onePerQuarter(all, true).concat(singleTones(s));
  }

  /** Second order Butterworth low pass, forward then backward (no delay). */
  function lowPassBothWays(v, cutoff, rate) {
    var k = Math.tan(Math.PI * cutoff / rate), norm = 1 / (1 + Math.SQRT2 * k + k * k);
    var b0 = k * k * norm, b1 = 2 * b0, b2 = b0, a1 = 2 * (k * k - 1) * norm, a2 = (1 - Math.SQRT2 * k + k * k) * norm;
    var pass, j, i, x0, x1, x2, y0, y1, y2, n = v.length;
    for (pass = 0; pass < 2; pass++) {
      // Started at the first value (as if it had always been there): no step at the edge.
      x1 = v[pass === 0 ? 0 : n - 1]; x2 = x1; y1 = x1; y2 = x1;
      for (j = 0; j < n; j++) {
        i = pass === 0 ? j : n - 1 - j;
        x0 = v[i];
        y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
        x2 = x1; x1 = x0; y2 = y1; y1 = y0;
        v[i] = y0;
      }
    }
  }

  /**
   * Onset of the signal near 'time' (s): envelope of the two tones (each demodulated, low pass 40 Hz forward and backward),
   * its peak in the 0.10 s before to 0.25 s after, back to where it is below the level before + 20 % of the rise.
   */
  function onset(x, at, low, high) {
    var a0 = Math.max(0, Math.floor((at - 0.45) * Rate)), b0 = Math.min(x.length, Math.floor((at + 0.45) * Rate));
    var n = b0 - a0, i, f, phase;
    if (n < Rate / 4) return at;
    var envelope = new Float64Array(n), tones = [low, high], re = new Float64Array(n), im = new Float64Array(n);
    for (f = 0; f < 2; f++) {
      for (i = 0; i < n; i++) {
        phase = -2 * Math.PI * tones[f] * (a0 + i) / Rate;
        re[i] = x[a0 + i] * Math.cos(phase);
        im[i] = x[a0 + i] * Math.sin(phase);
      }
      lowPassBothWays(re, 40, Rate);
      lowPassBothWays(im, 40, Rate);
      for (i = 0; i < n; i++) envelope[i] += re[i] * re[i] + im[i] * im[i];
    }
    for (i = 0; i < n; i++) envelope[i] = Math.sqrt(envelope[i]);

    var i0 = Math.max(0, Math.floor((at - 0.10) * Rate) - a0), i1 = Math.min(n, Math.floor((at + 0.25) * Rate) - a0);
    if (i1 <= i0) return at;
    var pk = i0;
    for (i = i0; i < i1; i++) if (envelope[i] > envelope[pk]) pk = i;
    var n0 = Math.max(0, Math.floor((at - 0.40) * Rate) - a0), n1 = Math.max(n0 + 1, Math.floor((at - 0.08) * Rate) - a0);
    var bef = [];
    for (i = n0; i < Math.min(n, n1); i++) bef.push(envelope[i]);
    var noise = bef.length > 0 ? median(bef) : 0, threshold = noise + 0.2 * (envelope[pk] - noise), k = pk;
    while (k > 0 && envelope[k - 1] > threshold) k--;
    return (a0 + k) / Rate;
  }

  /** The start signals heard in the sound (samples at 16 kHz), the clearest first. */
  function find(samples) {
    var result = [];
    if (!samples || samples.length < N + 2 * Rate) return result;
    var s = analyse(samples), ev = events(s), t, i;
    if (ev.length === 0) return result;

    // The quiet before the start: the level of the 0.6–0.1 s before, from the usual level of the recording.
    var levels = [];
    for (t = 0; t < s.frames; t++) if (s.valid[t]) levels.push(s.level[t]);
    var usual = levels.length > 0 ? median(levels) : 0;
    for (i = 0; i < ev.length; i++) {
      var e = ev[i], a = Math.max(0, e.frame - 120), b = Math.max(a + 1, e.frame - 20), bef = [];
      for (t = a; t < b && t < s.frames; t++) bef.push(s.level[t]);
      var quiet = bef.length > 0 ? median(bef) - usual : 0;
      result.push({
        onset: onset(samples, time(e.frame), e.low, e.high),
        score: e.score - 0.5 * Math.min(10, Math.max(-10, quiet)),
        rise: e.rise, quiet: quiet, low: e.low, high: e.high, together: e.together, single: e.single,
        duration: e.duration, purity: e.purity
      });
    }
    return result.sort(function (p, q) { return q.score - p.score; });
  }

  /**
   * The signals worth showing, the clearest first, and whether the first one can be taken without asking:
   * two tones from MinScore, single tones from SingleMinScore; taken alone when no other one at least 0.3 s away is
   * nearly as clear (AmbiguousShare).
   */
  function choose(beeps) {
    var shown = beeps.filter(function (b) { return b.score >= (b.single ? SingleMinScore : MinScore); });
    var two = shown.filter(function (b) { return !b.single; });
    var list = two.length > 0 ? two.concat(shown.filter(function (b) { return b.single && b.score >= two[0].score; })) : shown;
    list.sort(function (p, q) { return q.score - p.score; });
    var best = list[0] || null, clear = false;
    if (best && best.score >= MinScore) {
      clear = !list.slice(1).some(function (b) { return Math.abs(b.onset - best.onset) >= 0.3 && b.score >= AmbiguousShare * best.score; });
    }
    return { list: list.slice(0, 5), best: best, clear: clear };
  }

  /** One channel at 16 kHz from the sound of a video (any rate): low pass, then the nearest samples joined by a line. */
  function to16k(x, rate) {
    if (rate === Rate) return x;
    var v = Float64Array.from(x);
    if (rate > Rate) {
      lowPassBothWays(v, 6500, rate);
      lowPassBothWays(v, 6500, rate);
    }
    var n = Math.floor(v.length * Rate / rate), out = new Float32Array(n), i, p, k, f;
    for (i = 0; i < n; i++) {
      p = i * rate / Rate; k = Math.floor(p); f = p - k;
      out[i] = k + 1 < v.length ? v[k] * (1 - f) + v[k + 1] * f : v[v.length - 1];
    }
    return out;
  }

  /** Loudest sample of every step (s) of the sound: what the strip under the video draws. */
  function loudness(x, rate, step) {
    var per = Math.max(1, Math.round(rate * step)), n = Math.ceil(x.length / per), out = new Float32Array(n), i, j, m, a, end;
    for (i = 0; i < n; i++) {
      m = 0; end = Math.min(x.length, (i + 1) * per);
      for (j = i * per; j < end; j++) { a = x[j] < 0 ? -x[j] : x[j]; if (a > m) m = a; }
      out[i] = m;
    }
    return { values: out, step: per / rate };
  }

  return {
    Rate: Rate, MinScore: MinScore, SingleMinScore: SingleMinScore,
    find: find, choose: choose, to16k: to16k, loudness: loudness
  };
}

if (typeof module !== 'undefined' && module.exports) module.exports = swimHornFactory;
