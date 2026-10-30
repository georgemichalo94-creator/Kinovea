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

// Stroke rate and length of the clean swimming segments follow the typed counts. 2.11.0: one rate. A count corrected by the
// analyst (25 m: 9, the times give 8.5) gives strokes ÷ time; a count as the times give it (35 m: 8.5, confirmed) the rate from
// the stroke times (a stroke every 0.55 s: 60 / 1.1); the length always from the count.
const r25 = byD(25), r35 = byD(35);
assert.ok(r25.isFree && r35.isFree, '15-25 and 25-35 are clean swimming');
assert.strictEqual(r25.strokesAuto, 8.5);
assert.ok(near(r25.sr, (9 / 2) * 60 / (10.6 - 5.9), 1e-9) && r25.srTimes === false, 'sr of a corrected count ' + r25.sr);
assert.strictEqual(r35.strokesAuto, 8.5);
assert.ok(near(r35.sr, 60 / 1.1, 1e-9) && r35.srTimes === true, 'sr from the times ' + r35.sr);
assert.ok(near(r35.dps, 10 / 8.5, 1e-9), 'dps ' + r35.dps);
assert.ok(near(r25.dps, 10 / 9, 1e-9), 'dps ' + r25.dps);
// Without the stroke times (counts typed only) the rate is strokes ÷ time.
const onlyTyped = S.swimAnalyze_(S.swimNormalizeRace_(Object.assign({}, typed, { strokes: [] })));
const o35 = onlyTyped.rows.find(r => r.d === 35);
assert.ok(near(o35.sr, (8.5 / 2) * 60 / (15.4 - 10.6), 1e-9) && o35.srTimes === false, 'sr without the times ' + o35.sr);
// A stroke the automatic reading missed (25-35 m): the times give one stroke less and a lower rate. The analyst corrects the
// count (8.5): the rate is the corrected strokes ÷ time.
const gap = Object.assign({}, base, { strokes: base.strokes.filter(t => Math.abs(t - 12.85) > 0.01) });
const ag = S.swimAnalyze_(S.swimNormalizeRace_(gap)).rows.find(r => r.d === 35);
assert.ok(ag.srExact < 60 / 1.1 - 3 && ag.strokes === 7.5, 'the times with a press missing ' + ag.srExact + ' ' + ag.strokes);
const fixed = S.swimAnalyze_(S.swimNormalizeRace_(Object.assign({}, gap, { segmentStrokes: { 35: 8.5 } }))).rows.find(r => r.d === 35);
assert.ok(fixed.srTimes === false && near(fixed.sr, (8.5 / 2) * 60 / (15.4 - 10.6), 1e-9), 'sr of the corrected count ' + fixed.sr);
// Times that disagree with their own count (the reading lost the strokes in the second half of 25-35 m) give strokes ÷ time.
const lost = Object.assign({}, base, { strokes: base.strokes.filter(t => t < 13.0 || t > 16.5) });
const af = S.swimAnalyze_(S.swimNormalizeRace_(lost)).rows.find(r => r.d === 35);
assert.ok(af.srExact !== null && Math.abs(af.srExact * 4.8 / 60 * 2 - af.strokes) > 0.75, 'times against their count ' + af.srExact + ' ' + af.strokes);
assert.ok(af.srTimes === false && near(af.sr, (af.strokes / 2) * 60 / (15.4 - 10.6), 1e-9), 'sr when the times disagree ' + af.sr);
// Freestyle: the rate of the whole cycles (one hand to the same hand) is not changed by a difference between the two arms.
const uneven = [];
for (let t = 4.6, k = 0; t < 23.0; t += (k++ % 2 ? 0.5 : 0.6)) uneven.push(+t.toFixed(2));
const au = S.swimAnalyze_(S.swimNormalizeRace_(Object.assign({}, base, { strokes: uneven }))).rows.find(r => r.d === 35);
assert.ok(near(au.srExact, 60 / 1.1, 1e-6), 'whole cycles with uneven arms ' + au.srExact);

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
assert.strictEqual(call({ action: 'ping' }).version, '2.15.0');

console.log('counts from times', autoCounts.join(' '), '· typed 25→9, 35→8.5');
console.log('ALL STROKE COUNT TESTS PASSED');
