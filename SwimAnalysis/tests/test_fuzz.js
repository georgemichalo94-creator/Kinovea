// 2.15.0 (έλεγχος εγκυρότητας): η αναφορά με τυχαίους φανταστικούς αγώνες (στυλ, αποστάσεις, πισίνες, σκυταλοδρομίες, σημεία χωρίς
// χρόνο, εκτιμήσεις, ακυρώσεις, ρεκόρ και αναφορά): καμία εξαίρεση, κανένα undefined / NaN / null στα κελιά και στα κείμενα, και στα
// αγγλικά κανένα ελληνικό γράμμα (εκτός από τα ονόματα και το Δ). Σταθερός σπόρος: ίδιοι αγώνες κάθε φορά.
const { makeContext } = require('./mock_gas');
const assert = require('assert');
let seed = 12345; const rnd = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648;
const pick = a => a[Math.floor(rnd() * a.length)];
function race(id) {
  const style = pick(['fs', 'bk', 'br', 'fly', 'im']);
  const pool = pick([50, 50, 25]);
  const relay = rnd() < 0.15;
  let distance = pick(style === 'im' ? [100, 200, 400] : [50, 100, 200, 400, 800, 1500]);
  if (style === 'im' && pool === 50 && distance === 100) distance = 200;
  if (relay) distance = pick([200, 400, 800]);
  const cps = [], strokes = [], kicks = [], breakouts = [], feetOff = [];
  let t = 0;
  const lens = distance / pool;
  for (let L = 0; L < lens; L++) {
    const T = (pool === 50 ? 28 : 13.5) * (0.95 + rnd() * 0.2) * (style === 'br' ? 1.15 : 1);
    const pts = pool === 50 ? [15, 25, 35, 45, 50] : [15, 20, 25];
    pts.forEach(p => { const c = { d: L * pool + p, t: +(t + T * (p / pool) * (0.98 + rnd() * 0.04)).toFixed(2) }; if (rnd() < 0.05) c.t = null; if (rnd() < 0.3 && c.t !== null) c.v = +(c.t - rnd() * 0.1).toFixed(3); cps.push(c); });
    for (let s = t + 4 + rnd(); s < t + T - 0.3; s += 0.6 + rnd() * 0.8) if (rnd() > 0.05) strokes.push(+s.toFixed(3));
    if (rnd() < 0.7) { feetOff.push(+(t + (L ? 0.6 : 0.65)).toFixed(3)); for (let k = 0; k < 3 + Math.floor(rnd() * 5); k++) kicks.push(+(t + 1 + k * 0.45).toFixed(3)); breakouts.push(+(t + 3.5 + rnd()).toFixed(3)); }
    t += T;
  }
  cps.sort((a, b) => a.d - b.d);
  const turns = {}; for (let w = pool; w < distance; w += pool) if (rnd() < 0.5) turns[w] = { underwaterDist: +(6 + rnd() * 6).toFixed(1), underwaterEstimated: rnd() < 0.3, turnTime: rnd() < 0.3 ? 0.8 : undefined };
  return { id, athlete: 'ΦΑΝΤΑΣΤΙΚΟΣ ' + id, style, distance, pool, relay, dq: rnd() < 0.1, round: pick(['Τελικός', 'Προκριματικός', 'Ημιτελικός', '']), date: '1.1.2026',
    checkpoints: cps, strokes, kicks, breakouts, feetOff, entries: [1.0], releaseMark: rnd() < 0.3 ? 0.7 : undefined,
    manual: { reaction: 0.66, underwaterDist: rnd() < 0.5 ? 11.2 : undefined, underwaterEstimated: rnd() < 0.3, breakoutDist: rnd() < 0.3 ? 12 : undefined, turns, bodyLength: rnd() < 0.5 ? 1.8 : undefined },
    strokesPer50: false };
}
function run(lang, count) {
const G = makeContext({});
G.SWIM_LANG_ = lang;
let n = 0, errs = 0;
const greek = s => lang === 'en' && /[\u0370-\u03FF]/.test(s.replace(/ΦΑΝΤΑΣΤΙΚΟΣ [a-z0-9]+/g, '').replace(/Δ/g, ''));
const bad = s => typeof s === 'string' && (/undefined|NaN|Infinity|\[object/.test(s) || (lang !== 'en' && /\bnull\b/.test(s)) || greek(s));
for (let i = 0; i < count; i++) {
  const raw = race('f' + i), pbRaw = race('p' + i), refRaw = race('r' + i);
  [pbRaw, refRaw].forEach(o => { o.style = raw.style; o.distance = raw.distance; o.pool = raw.pool; o.relay = raw.relay; });
  try {
    const r = G.swimNormalizeRace_(raw), a = G.swimAnalyze_(r);
    const pbN = G.swimNormalizeRace_(pbRaw);
    const pbA = G.swimAnalyze_(pbN), refN = G.swimNormalizeRace_(refRaw), refA = G.swimAnalyze_(refN);
    const extras = { pb: { race: pbN, analysis: pbA, label: 'PB' }, elite: { race: refN, analysis: refA, label: 'Ref' } };
    const layout = G.swimBuildLayout_(r, a, extras);
    Object.values(layout.grid.cells).forEach(c => { if (bad(c.v)) throw new Error('cell ' + c.r + ':' + c.c + ' ' + c.v); });
    const q = G.swimQuickLookData_(r, a, extras, null, '50');
    q.points.forEach(p => { if (bad(p.text)) throw new Error('quick ' + p.text); });
    if (!r.relay) {
      const d = G.swimV2Data_(r, a, extras, null);
      [G.swimV2SpeedText_(d), G.swimV2StrokeText_(d)].forEach(x => { if (bad(x)) throw new Error('v2 text ' + x); });
      d.points.forEach(p => { if (bad(p.text) || bad(p.title)) throw new Error('v2 point ' + p.title + ' ' + p.text); });
      const notes = G.swimNotesData_(r, a, d, pbA);
      notes.good.concat(notes.watch).forEach(x => { if (bad(x)) throw new Error('note ' + x); });
      notes.explain.forEach(x => { if (bad(x[1])) throw new Error('explain ' + x[1]); });
      const v2 = G.swimBuildV2_(r, a, extras, null, { height: 1567, cols: 15 }, null);
      if (v2) Object.values(v2.grid.cells).forEach(c => { if (bad(c.v)) throw new Error('v2 cell ' + c.v); });
    }
    if (JSON.stringify(G.swimCompactRace_(r)).length > 49000) throw new Error('index entry too big');
    n++;
  } catch (e) {
    errs++;
    if (errs <= 8) console.log('ERR', i, raw.style, raw.distance, raw.pool, raw.relay ? 'relay' : '', String(e && e.stack || e).split('\n').slice(0, 3).join(' | '));
  }
}
assert.strictEqual(errs, 0, lang + ': ' + errs + ' races with errors');
return n;
}
const el = run('el', 150), en = run('en', 150);
console.log('test_fuzz: ' + el + ' + ' + en + ' random fictional races, no errors, no undefined/NaN, English without Greek');
