// 2.15.0 (έλεγχος εγκυρότητας): τα κείμενα και οι συγκρίσεις της αναφοράς λένε μόνο ό,τι στέκει. Η μικτή χωρίς συγκρίσεις από στυλ
// σε στυλ· «η πτώση της ταχύτητας» μόνο όταν έπεσε· «σταθερή ταχύτητα» μόνο όταν όλα τα τμήματα είναι μέσα στο 3%· οι μέσοι όροι του
// ρεκόρ στα ίδια τμήματα· η μονάδα μόνο μετά από απόσταση· το τελευταίο κομμάτι του 1500 ανά 200· η έξοδος της στροφής από τα μέτρα
// κάτω από το νερό· η αναφορά στις στροφές στους ίδιους τοίχους· η βιβλιοθήκη χωρίς τους ύποπτους χρόνους χωρίς μισές χεριές· οι
// χεριές ανά 50 του παλιού Excel χωρίς σύγκριση· κανένα «νέο ρεκόρ» σε ακύρωση. Φανταστικοί αθλητές.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [700001, 'ΔΟΚΙΜΗ ΑΝΑΦΟΡΑΣ', 'Ανδρών']] });
const near = (a, b, eps) => Math.abs(a - b) < (eps || 1e-9);

// Ένας αγώνας σε πισίνα 50μ. με τα σημεία 15/25/35/45/50 κάθε μήκους και χεριές με σταθερό ρυθμό ανά μήκος.
function race50(id, style, distance, lengthTimes, rates, extra) {
  const cps = [], strokes = [];
  let t0 = 0;
  for (let L = 0; L < distance / 50; L++) {
    const T = lengthTimes[L], base = L * 50;
    // 15μ. στο 25% του μήκους, μετά σταθερή ταχύτητα ως τον τοίχο.
    const t15 = t0 + T * 0.25, v = 35 / (T * 0.75);
    [15, 25, 35, 45, 50].forEach(p => cps.push({ d: base + p, t: +(t15 + (p - 15) / v).toFixed(2) }));
    for (let s = t0 + T * 0.18; s < t0 + T - 0.3; s += rates[L]) strokes.push(+s.toFixed(2));
    t0 += T;
  }
  return Object.assign({ id, athlete: 'ΔΟΚΙΜΗ ΑΝΑΦΟΡΑΣ', athleteId: '700001', gender: 'Ανδρών', style, distance, pool: 50,
    date: '29.10.2026', round: 'Τελικός', checkpoints: cps, strokes, kicks: [], breakouts: [], feetOff: [], entries: [], manual: {} }, extra || {});
}

// ---------------------------------------------------------------------------------------------
// (a) 200 μικτή: συχνότητα και μήκος χεριάς ανά στυλ, καμία «πτώση» ή «σταθερή ταχύτητα» από στυλ σε στυλ
// ---------------------------------------------------------------------------------------------
let race = G.swimNormalizeRace_(race50('im1', 'im', 200, [27.0, 31.0, 36.0, 29.0], [1.15, 0.72, 1.30, 0.70]));
let an = G.swimAnalyze_(race);
let d = G.swimV2Data_(race, an, {}, null);
assert.strictEqual(d.im, true, 'individual medley');
assert.deepStrictEqual(Array.from(new Set(Array.from(d.clean.styles))), ['fly', 'bk', 'br', 'fs'], 'the styles of the clean segments');
let text = G.swimV2StrokeText_(d);
['Πεταλούδα:', 'Ύπτιο:', 'Πρόσθιο:', 'Ελεύθερο:'].forEach(w => assert.ok(text.indexOf(w) >= 0, 'per style: ' + w + ' in ' + text));
assert.ok(!/πτώση της ταχύτητας/.test(text), 'no drop of speed across styles: ' + text);
assert.ok(!/Η πιο γρήγορη/.test(G.swimV2SpeedText_(d)), 'no fastest / slowest across styles');
let notes = G.swimNotesData_(race, an, d, null);
const all = n => n.good.concat(n.watch).join(' | ');
assert.ok(!/Σταθερή ταχύτητα|Η συχνότητα των χεριών έπεσε|Σταθερές χεριές/.test(all(notes)), 'no comparisons across styles: ' + all(notes));
let q = G.swimQuickLookData_(race, an, {}, null, '50');
const quick = q.points.map(p => p.text).join(' | ');
assert.ok(!/στο τελευταίο 50 η συχνότητα/.test(quick), 'quick look: no rate change from butterfly to freestyle: ' + quick);
assert.ok(!/από το καλύτερο τμήμα/.test(quick), 'quick look: the speed only within the last style (one length of it here): ' + quick);
// The halves: the times only (no rate, no slowest swimming).
const half = d.points.filter(p => /2ο 100/.test(p.title))[0];
assert.ok(half && !/συχνότητα|μήκος χεριάς|χαμηλότερη ταχύτητα/.test(half.text) && half.tone === 'accent', 'IM halves: ' + JSON.stringify(half));

// ---------------------------------------------------------------------------------------------
// (b) «Η πτώση της ταχύτητας…» μόνο όταν η ταχύτητα έπεσε
// ---------------------------------------------------------------------------------------------
const clean = (sr, dps, speed) => ({ U: 'μ.', sr: null, dps: null, im: false, clean: { cats: sr.map((x, i) => 15 + 50 * i + '–' + (45 + 50 * i)),
  sr: { race: sr, pb: sr.map(() => null), ref: [] }, dps: { race: dps, pb: dps.map(() => null), ref: [] }, speed: { race: speed, pb: [], ref: [] },
  styles: sr.map(() => 'fs') } });
// The rate down 5%, the distance per stroke up 9%: faster at the end.
text = G.swimV2StrokeText_(clean([50, 47.5], [1.80, 1.96], [1.50, 1.55]));
assert.ok(!/πτώση της ταχύτητας/.test(text), 'speed went up: ' + text);
text = G.swimV2StrokeText_(clean([50, 46], [1.80, 1.80], [1.50, 1.38]));
assert.ok(/πτώση της ταχύτητας έρχεται κυρίως από τη συχνότητα/.test(text), 'speed went down with the rate: ' + text);

// ---------------------------------------------------------------------------------------------
// (c) «Σταθερή ταχύτητα ως το τέλος» μόνο όταν όλα τα τμήματα είναι μέσα στο 3%
// ---------------------------------------------------------------------------------------------
race = G.swimNormalizeRace_(race50('fs1', 'fs', 200, [28.0, 30.0, 28.0, 28.0], [0.70, 0.70, 0.70, 0.70]));
an = G.swimAnalyze_(race);
d = G.swimV2Data_(race, an, {}, null);
const v = d.clean.speed.race;
assert.ok((Math.max(...v) - Math.min(...v)) / Math.max(...v) > 0.05 && (Math.max(...v) - v[v.length - 1]) / Math.max(...v) < 0.03,
  'a dip in the middle, the end near the best: ' + v.map(x => x.toFixed(3)));
notes = G.swimNotesData_(race, an, d, null);
assert.ok(!/Σταθερή ταχύτητα/.test(all(notes)), 'not steady with a dip in the middle: ' + all(notes));
race = G.swimNormalizeRace_(race50('fs2', 'fs', 200, [28.0, 28.4, 28.5, 28.6], [0.70, 0.70, 0.70, 0.70]));
an = G.swimAnalyze_(race);
d = G.swimV2Data_(race, an, {}, null);
notes = G.swimNotesData_(race, an, d, null);
assert.ok(/Σταθερή ταχύτητα/.test(all(notes)), 'steady: ' + all(notes) + ' / ' + d.clean.speed.race.map(x => x.toFixed(3)));

// ---------------------------------------------------------------------------------------------
// (d) Ο μέσος όρος του ρεκόρ μόνο στα ίδια τμήματα
// ---------------------------------------------------------------------------------------------
assert.ok(near(G.swimV2SameMean_([1.5, 1.6], [1.4, 1.5]), 1.45), 'all segments');
assert.strictEqual(G.swimV2SameMean_([1.5, 1.6, 1.4], [1.4, null, 1.3]), null, 'a segment without the PB: no mean');
assert.ok(near(G.swimV2SameMean_([1.5, null], [1.4, 1.9]), 1.4), 'only the segments of the race');

// ---------------------------------------------------------------------------------------------
// (e) Η μονάδα μετά την τιμή του ρεκόρ μόνο όταν είναι η μονάδα της τιμής
// ---------------------------------------------------------------------------------------------
assert.strictEqual(G.swimV2With_('τα 15{1} μετά τον τοίχο σε {0}', 6.35, 6.4, 2, 'μ.'), 'τα 15μ. μετά τον τοίχο σε 6.35 (ρεκόρ 6.40)');
assert.strictEqual(G.swimV2With_('έξοδος από το νερό στα {0}{1}', 12.1, 11.6, 1, 'μ.'), 'έξοδος από το νερό στα 12.1μ. (ρεκόρ 11.6μ.)');

// ---------------------------------------------------------------------------------------------
// (f) Ανά 200 στο 1500: και τα 1400–1500
// ---------------------------------------------------------------------------------------------
const per = { step: 100, rows: [] };
for (let b = 0; b < 1500; b += 100) per.rows.push({ from: b, to: b + 100, label: b + '–' + (b + 100) + 'μ.', t: 62, dPb: 0.1, dRef: null, sr: 40, dps: 2.0, dak: 12 });
const m200 = G.swimV2Merge_(per, 200);
assert.strictEqual(m200.rows.length, 8, '7 × 200 and the last 100');
assert.strictEqual(m200.rows[7].label, '1400–1500μ.');
assert.ok(near(m200.rows[7].t, 62) && near(m200.rows[0].t, 124), 'times summed');

// ---------------------------------------------------------------------------------------------
// (g) Η έξοδος της στροφής: τα μέτρα κάτω από το νερό (όχι εκτίμηση)· ο χρόνος στροφής, όχι «περιστροφή»
// ---------------------------------------------------------------------------------------------
const m = { manual: { turns: { 50: { underwaterDist: 9.4 }, 100: { underwaterDist: 8.0, underwaterEstimated: true }, 150: { breakoutDist: 7.5, underwaterDist: 9.0 } } } };
assert.strictEqual(G.swimV2TurnBreakoutDist_(m, 50), 9.4, 'measured');
assert.strictEqual(G.swimV2TurnBreakoutDist_(m, 100), null, 'estimated: not a measured breakout');
assert.strictEqual(G.swimV2TurnBreakoutDist_(m, 150), 7.5, 'typed');
assert.strictEqual(G.swimV2TurnBreakoutDist_(m, 200), null, 'none');
race = G.swimNormalizeRace_(race50('fs3', 'fs', 100, [27.0, 29.5], [0.70, 0.70], { manual: { turns: { 50: { underwaterDist: 9.4, turnTime: 0.81 } } } }));
an = G.swimAnalyze_(race);
d = G.swimV2Data_(race, an, {}, null);
assert.strictEqual(d.turns[0].breakoutDist, 9.4, 'the turn card has its breakout');
const det = G.swimV2Details_({ kind: 'turn', wall: 50 }, d, an, null).join(', ');
assert.ok(/χρόνος στροφής 0\.81 δλ\./.test(det) && !/περιστροφή/.test(det) && /έξοδος από το νερό στα 9\.4μ\./.test(det), det);

// ---------------------------------------------------------------------------------------------
// (h) «Με μια ματιά»: η διαφορά των στροφών από την αναφορά στους ίδιους τοίχους· όχι «νέο ρεκόρ» σε ακύρωση
// ---------------------------------------------------------------------------------------------
const four = race50('fs4', 'fs', 200, [26.0, 28.4, 28.5, 28.6], [0.70, 0.70, 0.70, 0.70]);
race = G.swimNormalizeRace_(four);
an = G.swimAnalyze_(race);
const refRace = G.swimNormalizeRace_(race50('ref4', 'fs', 200, [25.5, 27.9, 28.0, 28.1], [0.68, 0.68, 0.68, 0.68]));
const refAn = G.swimAnalyze_(refRace);
// The race without its turn at 150 (no time at 145): the averages are over 2 and 3 walls.
an.turns.forEach(t => { if (t.wall === 150) t.inOut = null; });
q = G.swimQuickLookData_(race, an, { elite: { race: refRace, analysis: refAn, label: 'Ref' } }, null, '50');
const same = an.turns.filter(t => t.inOut !== null).map(t => t.inOut - refAn.turns.filter(x => x.wall === t.wall)[0].inOut);
assert.ok(near(q.refTurnDelta, same.reduce((a, b) => a + b, 0) / same.length), 'the same walls: ' + q.refTurnDelta);
const pbRace = G.swimNormalizeRace_(race50('pb4', 'fs', 200, [26.5, 29.0, 29.0, 29.2], [0.70, 0.70, 0.70, 0.70]));
const pbAn = G.swimAnalyze_(pbRace);
const dq = G.swimNormalizeRace_(Object.assign({}, four, { dq: true }));
q = G.swimQuickLookData_(dq, G.swimAnalyze_(dq), { pb: { race: pbRace, analysis: pbAn, label: 'PB' } }, null, '50');
assert.ok(!q.points.some(p => /Νέο ατομικό ρεκόρ/.test(p.text)), 'no new PB with a DQ');

// ---------------------------------------------------------------------------------------------
// (i) Βιβλιοθήκη, «Χωρίς τους ύποπτους χρόνους»: το τμήμα που μεγάλωσε χωρίς χεριές (όχι μόνο οι μισές)
// ---------------------------------------------------------------------------------------------
const row = new Array(19).fill('');
row[1] = 'Χωρίς τους ύποπτους χρόνους';
const rec = { id: 'lib1', style: 'fs', distance: 50, pool: 50, checkpoints: [{ d: 15, t: 6.4 }, { d: 25, t: 11.6 }, { d: 35, t: 16.0 }, { d: 45, t: 22.2 }, { d: 50, t: 25.0 }],
  segmentStrokes: { 25: 8, 35: 9, 45: 10, 50: 3 }, drop: [35], athlete: 'ΔΟΚΙΜΗ ΑΝΑΦΟΡΑΣ' };
const lib = G.swimLibraryRace_(rec, row, { byId: {} });
assert.deepStrictEqual(lib.checkpoints.map(c => c.d), [15, 25, 45, 50], 'the suspect time dropped');
assert.deepStrictEqual(Object.keys(lib.segmentStrokes).map(Number).sort((a, b) => a - b), [25, 50], 'no strokes for 25–45 (10 of 19)');
// The suspect checkpoint had no strokes of its own: the 45's 10 are still part of 25–45.
const lib2 = G.swimLibraryRace_(Object.assign({}, rec, { segmentStrokes: { 25: 8, 45: 10, 50: 3 } }), row, { byId: {} });
assert.deepStrictEqual(Object.keys(lib2.segmentStrokes).map(Number).sort((a, b) => a - b), [25, 50], 'no strokes for 25–45 either');
// A fix that takes a checkpoint away: the strokes of both segments summed, none when one is unknown.
const fixRow = new Array(19).fill(''); fixRow[1] = 'Εισαγωγή'; fixRow[17] = '35=';
const lib3 = G.swimLibraryRace_(rec, fixRow, { byId: {} });
assert.strictEqual(lib3.segmentStrokes[45], 19, '35 + 45 strokes');
const lib4 = G.swimLibraryRace_(Object.assign({}, rec, { segmentStrokes: { 25: 8, 45: 10, 50: 3 } }), fixRow, { byId: {} });
assert.strictEqual(lib4.segmentStrokes[45], undefined, 'the 25–35 count unknown: none for 25–45');

// ---------------------------------------------------------------------------------------------
// (j) Οι χεριές ανά 50 του παλιού Excel (400–1500): η συχνότητα και ο ΔΑΚ του ρεκόρ όλου του 50άρι, χωρίς σύγκριση
// ---------------------------------------------------------------------------------------------
race = G.swimNormalizeRace_(four);
an = G.swimAnalyze_(race);
const pb50 = G.swimNormalizeRace_(Object.assign(race50('pb50', 'fs', 200, [26.5, 29.0, 29.0, 29.2], [0.70, 0.70, 0.70, 0.70]),
  { strokes: [], strokesPer50: true, segmentStrokes: { 50: 34, 100: 38, 150: 38, 200: 39 } }));
const pb50An = G.swimAnalyze_(pb50);
assert.ok(pb50An.blocks[0].sr > 0 && pb50An.blocks[0].whole, 'the PB has the rate of the whole 50');
const layout = G.swimBuildLayout_(race, an, { pb: { race: pb50, analysis: pb50An, label: 'PB' } });
let hdr = null;
Object.values(layout.grid.cells).forEach(c => { if (c.v === 'ΔΑΚ PB') hdr = c; });
assert.ok(hdr, 'the per 50 comparison');
const first = hdr.r + 1;
assert.strictEqual(layout.grid.get(first, 2).v, pb50An.blocks[0].time, 'the time of the PB is compared');
assert.strictEqual(layout.grid.get(first, 5).v, '', 'no ΔΑΚ of the PB (whole 50)');
assert.strictEqual(layout.grid.get(first, 11).v, '', 'no rate of the PB (whole 50)');

// ---------------------------------------------------------------------------------------------
// (k) Ένας πολύ μεγάλος αγώνας για ένα κελί του ευρετηρίου (1500 με τελικό και στιγμιότυπα): μικραίνει ώστε να γράφεται, οι
// μετρήσεις του αγώνα μένουν
// ---------------------------------------------------------------------------------------------
const big = race50('big', 'fs', 1500, Array.from({ length: 30 }, () => 30.5), Array.from({ length: 30 }, () => 1.3));
big.kicks = big.checkpoints.filter(c => c.d % 50 === 0).map(c => +(c.t + 1).toFixed(2));
big.tech = { snapshots: Array.from({ length: 60 }, (_, i) => ({ id: 's' + i, t: i, phase: 'Στροφή', stage: 'Επαφή', event: 'x', comment: 'σχόλιο '.repeat(120), fileId: 'f'.repeat(33) })) };
big.finals = { label: 'Τελικός', swimmers: Array.from({ length: 8 }, (_, i) => ({ rank: i + 1, name: 'Κολυμβητής ' + i, times: big.checkpoints.map(c => ({ d: c.d, t: c.t + i })) })),
  metrics: Array.from({ length: 40 }, (_, i) => ({ group: 'Ομάδα', label: 'Τιμή ' + i, athlete: '1.00', rank: '1/9', winner: '0.99', mean: '1.01', values: [1, 2, 3, 4, 5, 6, 7, 8] })) };
const bigN = G.swimNormalizeRace_(big);
assert.ok(JSON.stringify(bigN).length > 50000, 'too big for a cell');
const small = G.swimCompactRace_(bigN);
assert.ok(JSON.stringify(small).length <= 49000, 'fits: ' + JSON.stringify(small).length);
assert.strictEqual(small.kicks.length, bigN.kicks.length, 'the kicks kept');
assert.strictEqual(small.checkpoints.length, bigN.checkpoints.length, 'the checkpoints kept');
assert.ok(small.compacted.indexOf('tech.comments') >= 0, 'what was left out is said: ' + small.compacted);

// ---------------------------------------------------------------------------------------------
// (l) Αναφορά με χρόνους μόνο ανά 50: ο χρόνος στα 50 συγκρίνεται, το τμήμα της (0–50) όχι με το 45–50 του αγώνα
// ---------------------------------------------------------------------------------------------
race = G.swimNormalizeRace_(four);
an = G.swimAnalyze_(race);
const ref50 = G.swimNormalizeRace_({ id: 'r50', athlete: 'Αναφορά', style: 'fs', distance: 200, pool: 50,
  checkpoints: [{ d: 50, t: 25.8 }, { d: 100, t: 53.9 }, { d: 150, t: 82.1 }, { d: 200, t: 110.4 }] });
const lay = G.swimBuildLayout_(race, an, { elite: { race: ref50, analysis: G.swimAnalyze_(ref50), label: 'Ref' } });
let h = null;
Object.values(lay.grid.cells).forEach(c => { if (c.v === 'Χρόνος elite' && c.c === 4 && (!h || c.r < h.r)) h = c; });
assert.ok(h, 'the comparison of the passes');
const at = d => { for (let r = h.r + 1; r < h.r + 40; r++) { const x = lay.grid.get(r, 1); if (x && x.v === d) return r; } return null; };
const r50 = at(50);
assert.strictEqual(lay.grid.get(r50, 4).v, 25.8, 'the time at 50 compared');
assert.strictEqual(lay.grid.get(r50, 9).v, '', 'no speed of 0–50 against 45–50');
assert.strictEqual(lay.grid.get(r50, 12).v, '', 'no segment time of 0–50 against 45–50');
assert.strictEqual(lay.grid.get(r50, 15).v, '', 'no speed change');

// ---------------------------------------------------------------------------------------------
// (m) Τα επίπεδα των ατομικών αγωνισμάτων όχι σε σκυταλοδρομία
// ---------------------------------------------------------------------------------------------
const levels = [{ kind: 'turn', style: 'fs', distance: 400, pool: 50, poolUnit: 'm', gender: '', values: [1, 2, 3] }];
assert.ok(G.swimBenchmarksFor_(G.swimNormalizeRace_({ style: 'fs', distance: 400, pool: 50, checkpoints: [] }), levels), 'individual 400');
assert.strictEqual(G.swimBenchmarksFor_(G.swimNormalizeRace_({ style: 'fs', distance: 400, pool: 50, relay: true, checkpoints: [] }), levels), null, 'relay');

console.log('test_reports: IM per style, drops only when slower, steady only when steady, same segments and walls, units, last part, ' +
  'turn breakout, library suspect times, whole-50 strokes, no PB with DQ');
