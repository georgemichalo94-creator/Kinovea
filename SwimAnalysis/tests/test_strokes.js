// Strokes confirmed or typed in Kinovea at each checkpoint (segmentStrokes) have priority over the stroke times.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const S = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [500001, 'ΔΟΚΙΜΑΣΤΙΚΟΣ ΑΘΛΗΤΗΣ', 'Ανδρών']] });
const near = (a, b, eps) => Math.abs(a - b) < (eps || 1e-9);

// 50 m freestyle with a stroke every 0.55 s from the breakout.
const strokes = [];
for (let t = 4.6; t < 23.0; t += 0.55) strokes.push(+t.toFixed(2));
const base = {
  id: 's1', athlete: 'ΔΟΚΙΜΑΣΤΙΚΟΣ ΑΘΛΗΤΗΣ', athleteId: '500001', gender: 'Ανδρών', style: 'fs', distance: 50, pool: 50,
  checkpoints: [[15, 5.9], [25, 10.6], [35, 15.4], [45, 20.3], [50, 23.1]].map(([d, t]) => ({ d, t })), strokes, strokeRounding: 0.5
};

// Without typed counts: from the stroke times.
const auto = S.swimAnalyze_(S.swimNormalizeRace_(base));
const autoCounts = auto.rows.map(r => r.strokes);
assert.ok(autoCounts.every(n => n !== null && n > 0), 'counts from the times: ' + autoCounts);
assert.ok(auto.rows.every(r => !r.strokesTyped));

// Typed counts for two segments: they replace the counts from the times, the others stay.
const typed = Object.assign({}, base, { segmentStrokes: { 25: 9, 35: 8.5 } });
const an = S.swimAnalyze_(S.swimNormalizeRace_(typed));
const byD = d => an.rows.find(r => r.d === d);
assert.strictEqual(byD(25).strokes, 9);
assert.strictEqual(byD(35).strokes, 8.5);
assert.ok(byD(25).strokesTyped && byD(35).strokesTyped && !byD(45).strokesTyped);
assert.strictEqual(byD(15).strokes, autoCounts[0]);
assert.strictEqual(byD(45).strokes, autoCounts[3]);

// Stroke rate and length of the clean swimming segments follow the typed counts.
const r35 = byD(35);
assert.ok(r35.isFree, '25-35 is clean swimming');
assert.ok(near(r35.sr, (8.5 / 2) * 60 / (15.4 - 10.6), 1e-9), 'sr ' + r35.sr);
assert.ok(near(r35.dps, 10 / 8.5, 1e-9), 'dps ' + r35.dps);

// A typed count for a checkpoint without time is ignored.
const missing = Object.assign({}, base, {
  checkpoints: base.checkpoints.map(c => c.d === 45 ? { d: 45, t: null } : c), segmentStrokes: { 45: 7 }
});
const am = S.swimAnalyze_(S.swimNormalizeRace_(missing));
assert.strictEqual(am.rows.find(r => r.d === 45).strokes, null);

// Backstroke after the dive: the first pull (the breakout) is not counted in the first segment; the others as in freestyle.
const bkStrokes = [];
for (let t = 5.6; t < 30.0; t += 0.5) bkStrokes.push(+t.toFixed(2));
const bk = Object.assign({}, base, {
  style: 'bk', strokes: bkStrokes, checkpoints: [[15, 6.3], [25, 11.3], [35, 16.3], [45, 21.3], [50, 24.1]].map(([d, t]) => ({ d, t }))
});
const ab = S.swimAnalyze_(S.swimNormalizeRace_(bk));
const bkByD = d => ab.rows.find(r => r.d === d);
assert.strictEqual(bkByD(15).strokes, 1.5, 'bk 15 m: 2.4 pulls by the times, the breakout pull not counted');
assert.strictEqual(bkByD(25).strokes, 10, 'bk 25 m as counted');
const fsFirst = S.swimAnalyze_(S.swimNormalizeRace_(Object.assign({}, bk, { style: 'fs' }))).rows[0].strokes;
assert.strictEqual(fsFirst, 2.5, 'freestyle 15 m keeps the first stroke');

// Through doPost: the sheet gets the typed counts (column of the strokes).
S.swimShowConnectionInfo = () => {};
S.swimSetup();
const call = body => JSON.parse(S.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
const res = call({ action: 'update', race: typed });
assert.ok(res.ok, JSON.stringify(res));
assert.strictEqual(call({ action: 'ping' }).version, '2.5.0');

console.log('counts from times', autoCounts.join(' '), '· typed 25→9, 35→8.5');
console.log('ALL STROKE COUNT TESTS PASSED');
