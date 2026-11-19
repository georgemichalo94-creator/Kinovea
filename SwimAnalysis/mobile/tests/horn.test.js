// Checks of the start signal heard: synthetic sounds only (tones over noise), no recording of a pool.
'use strict';
const horn = require('../src/horn.js')();
let failed = 0;
function check(name, ok, detail) { console.log((ok ? 'ok   ' : 'FAIL ') + name + (detail ? '  ' + detail : '')); if (!ok) failed++; }

// A small generator with a seed: the same noise at every run.
function rng(seed) { let s = seed >>> 0; return () => { s = (Math.imul(s, 1664525) + 1013904223) >>> 0; return s / 4294967296; }; }
function noise(seconds, rate, level, seed) {
  const r = rng(seed), x = new Float32Array(Math.round(seconds * rate));
  let prev = 0;
  for (let i = 0; i < x.length; i++) { const w = (r() + r() + r() - 1.5) * 2; prev = 0.85 * prev + 0.15 * w; x[i] = level * (0.6 * w + 2.5 * prev); }
  return x;
}
function tone(x, rate, hz, from, seconds, amp, attack) {
  const a = Math.round(from * rate), n = Math.round(seconds * rate), att = Math.max(1, Math.round((attack || 0.004) * rate));
  for (let i = 0; i < n && a + i < x.length; i++) {
    const env = Math.min(1, i / att, (n - i) / att);
    x[a + i] += amp * env * Math.sin(2 * Math.PI * hz * (a + i) / rate);
  }
}
const f3 = v => v.toFixed(3);

// 1. Two tones together (ratio 8/7) at a time that is not on a frame of the video.
{
  const x = noise(8, 16000, 0.02, 7);
  tone(x, 16000, 895, 3.2137, 0.5, 0.25); tone(x, 16000, 895 * 8 / 7, 3.2137, 0.5, 0.25);
  const t0 = Date.now(), beeps = horn.find(x), ms = Date.now() - t0, c = horn.choose(beeps), b = c.best;
  check('two tones: found', !!b && !b.single, b ? `onset ${f3(b.onset)} score ${b.score.toFixed(1)} tones ${b.low.toFixed(0)}/${b.high.toFixed(0)} (${ms} ms for 8 s)` : 'none');
  check('two tones: onset within 10 ms', !!b && Math.abs(b.onset - 3.2137) < 0.010, b ? `${((b.onset - 3.2137) * 1000).toFixed(1)} ms` : '');
  check('two tones: the tones', !!b && Math.abs(b.low - 895) < 4 && Math.abs(b.high - 1022.9) < 4);
  check('two tones: taken without asking', c.clear && b.score >= horn.MinScore);
}

// 2. A single tone (719 Hz for 0.4 s), as some start systems sound.
{
  const x = noise(8, 16000, 0.02, 11);
  tone(x, 16000, 719, 4.5071, 0.4, 0.3);
  const c = horn.choose(horn.find(x)), b = c.best;
  check('single tone: found', !!b && b.single, b ? `onset ${f3(b.onset)} score ${b.score.toFixed(1)} tone ${b.low.toFixed(0)} lasts ${b.duration.toFixed(2)} purity ${b.purity.toFixed(2)}` : 'none');
  check('single tone: onset within 10 ms', !!b && Math.abs(b.onset - 4.5071) < 0.010, b ? `${((b.onset - 4.5071) * 1000).toFixed(1)} ms` : '');
}

// 3. Noise only, and a whistle (3 kHz): no start.
{
  const x = noise(8, 16000, 0.05, 23);
  tone(x, 16000, 3000, 2.0, 1.2, 0.3);
  const c = horn.choose(horn.find(x));
  check('noise and a whistle: no start', !c.best, c.best ? `score ${c.best.score.toFixed(1)} at ${f3(c.best.onset)}` : '');
}

// 4. Two signals as clear as each other (a false start, then the start): shown, not taken without asking.
{
  const x = noise(14, 16000, 0.02, 31);
  for (const at of [3.0, 9.4]) { tone(x, 16000, 900, at, 0.5, 0.25); tone(x, 16000, 900 * 8 / 7, at, 0.5, 0.25); }
  const c = horn.choose(horn.find(x));
  check('two signals: both shown', c.list.filter(b => !b.single).length >= 2, c.list.map(b => f3(b.onset) + ':' + b.score.toFixed(1)).join(' '));
  check('two signals: asks', !c.clear);
}

// 5. The same signal recorded at 48 kHz and at 44.1 kHz reads the same after the change of rate.
for (const rate of [48000, 44100]) {
  const x = noise(6, rate, 0.02, 5);
  tone(x, rate, 895, 2.4444, 0.5, 0.25); tone(x, rate, 895 * 8 / 7, 2.4444, 0.5, 0.25);
  const y = horn.to16k(x, rate), c = horn.choose(horn.find(y)), b = c.best;
  check(`rate ${rate}: onset within 10 ms`, !!b && Math.abs(b.onset - 2.4444) < 0.010, b ? `${((b.onset - 2.4444) * 1000).toFixed(1)} ms, ${y.length} samples` : 'none');
}

// 6. The strip of the sound: one value per step, the loudest sample.
{
  const x = new Float32Array(16000); x[8000] = -0.9; x[100] = 0.3;
  const l = horn.loudness(x, 16000, 0.002);
  check('loudness: steps of 2 ms', l.values.length === 500 && Math.abs(l.step - 0.002) < 1e-9 && Math.abs(l.values[250] - 0.9) < 1e-6 && Math.abs(l.values[3] - 0.3) < 1e-6);
}

console.log(failed ? `\n${failed} FAILED` : '\nall ok');
process.exit(failed ? 1 : 0);
