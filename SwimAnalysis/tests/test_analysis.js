const S = require('./harness');
const assert = require('assert');
function near(a, b, eps = 1e-6) { return Math.abs(a - b) < eps; }

// 1) Regular strokes: one press every 0.5 s from 5.0 s; 100 free, wall at 24.0
const strokes = [];
for (let t = 5.0; t < 23.8; t += 0.5) strokes.push(+t.toFixed(3));   // last ~23.5
for (let t = 28.0; t < 50; t += 0.5) strokes.push(+t.toFixed(3));
const race = S.swimNormalizeRace_({
  id: 'x', style: 'fs', distance: 100, pool: 50,
  checkpoints: [{d:15,t:6.2},{d:25,t:11.1},{d:35,t:16.0},{d:45,t:21.2},{d:50,t:24.0},{d:65,t:30.3},{d:75,t:35.6},{d:85,t:41.0},{d:95,t:46.4},{d:100,t:49.3}],
  strokes, kicks: [1.5,2.0,2.5,3.0, 25.0,25.5,26.0, 60], breakouts: [4.5, 27.5], feetOff: [0.7, 24.6], entries: [1.0],
});
const a = S.swimAnalyze_(race);
const rows = a.rows;
// 0-15: presses at 5.0,5.5,6.0 (3) + fraction to 6.2: (6.2-6.0)/0.5=0.4 -> N=3.4 -> rounds 3.5
assert.strictEqual(rows[0].strokes, 3.5, 'first segment');
// 15-25 (6.2 -> 11.1): N(11.1) = 13 + 0.2 = 13.2 -> 13.0 ; minus 3.5 = 9.5
assert.strictEqual(rows[1].strokes, 9.5, '15-25');
// exact SR 15-25: interval 0.5 s, fs => cycle 1.0 s => 60
assert.ok(near(rows[1].srExact, 60), 'srExact ' + rows[1].srExact);
// 2.11.0: one rate. From the stroke times (60) where the strokes of the segment are those of the times (not corrected) and agree
// with them (60 × 4.9 / 60 × 2 = 9.8 strokes ≈ 9.5); otherwise strokes / 2 × 60 / segTime (test_strokes).
assert.ok(near(rows[1].sr, 60), 'sr ' + rows[1].sr);
assert.strictEqual(rows[1].srTimes, true, 'rate from the times');
assert.strictEqual(rows[1].strokesAuto, 9.5);
// wall at 24.0: last press 23.5 before wall, next 28.0 after -> no interpolation across the wall
const n50 = rows.slice(0, 5).reduce((s, r) => s + r.strokes, 0);
assert.strictEqual(n50, 38, 'strokes to 50 = number of presses before wall'); // 5.0..23.5 => 38 presses
// 50-65 segment (24.0 -> 30.3): presses 28.0..30.0 (5) + 0.6 -> 5.6 -> 5.5
assert.strictEqual(rows[5].strokes, 5.5, '50-65');
assert.strictEqual(rows[0].isSwim, false); assert.strictEqual(rows[1].isSwim, true); assert.strictEqual(rows[4].isSwim, true);
assert.strictEqual(rows[5].isSwim, false); assert.strictEqual(rows[4].isFree, false); assert.strictEqual(rows[3].isFree, true);
// SR / DPS / SL only in clean swimming: not in the last 5 m before the wall, not in 0-15 / 50-65
assert.strictEqual(rows[4].sr, null, 'no SR in 45-50'); assert.strictEqual(rows[4].dps, null, 'no DPS in 45-50');
assert.strictEqual(rows[0].sr, null); assert.strictEqual(rows[5].sr, null); assert.strictEqual(rows[9].sr, null, 'no SR in 95-100');
assert.ok(near(rows[1].sl, 10 / (9.5 / 2)), 'SL per cycle ' + rows[1].sl);
assert.ok(near(rows[1].dps * 2, rows[1].sl), 'fs: SL = 2 x DPS');
// average SR weighted by time over the clean segments
const clean = rows.filter(r => r.sr !== null);
const wsr = clean.reduce((s, r) => s + r.sr * r.segTime, 0) / clean.reduce((s, r) => s + r.segTime, 0);
assert.ok(near(a.summary.avgSr, wsr), 'weighted avg SR');
// start
assert.strictEqual(a.start.blockTime, 0.7); assert.strictEqual(a.start.entryTime, 1.0);
assert.strictEqual(a.start.underwaterTime, 4.5); assert.strictEqual(a.start.kicks, 4);
// turn at 50
const t = a.turns[0];
// freestyle flip turn: turn time from the last hand entry (23.5) to the feet off (24.6)
assert.strictEqual(t.wall, 50); assert.ok(t.flip, 'fs turn is a flip turn');
assert.ok(near(t.turnTime, 24.6 - 23.5), 'flip turn time ' + t.turnTime); assert.ok(near(t.underwaterTime, 27.5 - 24.6));
assert.strictEqual(t.kicks, 3); assert.ok(near(t.lastStrokeGap, 0.5));
// finish: last press 49.0 -> gap 0.3
assert.ok(near(a.finish.lastStrokeGap, 49.3 - 49.0), 'finish gap ' + a.finish.lastStrokeGap);

// 1b) Relay exchange: the next swimmer may leave the block just before the touch
const relay = S.swimNormalizeRace_({ id: 'rl', style: 'fs', relay: true, distance: 400, pool: 50,
  checkpoints: [{d:15,t:6.0},{d:25,t:11},{d:35,t:16},{d:45,t:21},{d:50,t:23.8},{d:65,t:30},{d:75,t:35},{d:85,t:40},{d:95,t:45},{d:100,t:48.0},{d:115,t:54.2}],
  feetOff: [0.7, 24.1, 47.98], breakouts: [4.5, 27.0, 52.0] });
const ra = S.swimAnalyze_(relay);
assert.ok(ra.turns[1].exchange, 'leg boundary is an exchange');
assert.ok(near(ra.turns[1].turnTime, -0.02), 'exchange time may be negative: ' + ra.turns[1].turnTime);
assert.ok(!ra.turns[0].exchange && ra.turns[0].flip, 'turn inside a freestyle leg is a flip turn');
assert.strictEqual(ra.turns[0].turnTime, null, 'flip turn time needs the last stroke press before the wall');
const ra2 = S.swimAnalyze_(Object.assign({}, relay, { strokes: [21.0, 22.5, 23.1, 46.0, 47.4] }));
assert.ok(near(ra2.turns[0].turnTime, 24.1 - 23.1), 'flip turn from the last hand entry: ' + ra2.turns[0].turnTime);
assert.ok(near(ra2.turns[1].turnTime, -0.02), 'exchange still from the touch');

// 1c) Open turns from the touch: breaststroke, butterfly and the medley changes of style; flip turns inside fs/bk legs
const brRace = S.swimNormalizeRace_({ id: 'br', style: 'br', distance: 100, pool: 50,
  checkpoints: [{d:15,t:7},{d:50,t:30.0},{d:65,t:38},{d:100,t:63}], strokes: [8, 9.2, 29.2, 40, 62.1], feetOff: [0.7, 30.95] });
const bra = S.swimAnalyze_(brRace);
assert.ok(!bra.turns[0].flip && near(bra.turns[0].turnTime, 0.95), 'open turn from the touch: ' + bra.turns[0].turnTime);
function turnKinds(style, relay, distance) {
  const r = S.swimNormalizeRace_({ id: 'k', style, relay, distance, pool: 50, checkpoints: [] });
  return S.swimAnalyze_(r).turns.map(x => x.wall + (x.exchange ? 'x' : x.flip ? 'f' : 'o')).join(' ');
}
assert.strictEqual(turnKinds('medley', false, 200), '50o 100o 150o', '200 IM: all changes of style are open turns');
assert.strictEqual(turnKinds('medley', false, 400), '50o 100o 150f 200o 250o 300o 350f', '400 IM');
assert.strictEqual(turnKinds('medley', true, 400), '50f 100x 150o 200x 250o 300x 350f', '4x100 medley relay');
assert.strictEqual(turnKinds('bk', false, 200), '50f 100f 150f', '200 bk');
assert.strictEqual(turnKinds('fly', false, 200), '50o 100o 150o', '200 fly');

// 2) Medley styles
const im = S.swimNormalizeRace_({ id: 'im', style: 'medley', distance: 200, pool: 50, checkpoints: [50,100,150,200].map(d=>({d})) });
assert.strictEqual(JSON.stringify(im.checkpoints.map(c => S.swimStyleAt_(im, c.d))), JSON.stringify(['fly','bk','br','fs']));
const mr = S.swimNormalizeRace_({ id: 'mr', style: 'im', relay: true, distance: 400, pool: 50, checkpoints: [100,200,300,400].map(d=>({d})) });
assert.strictEqual(JSON.stringify(mr.checkpoints.map(c => S.swimStyleAt_(mr, c.d))), JSON.stringify(['bk','br','fly','fs']));

// 3) Partially marked race (live): only first 2 splits, strokes only up to now
const live = S.swimNormalizeRace_({ id: 'l', style: 'br', distance: 50, pool: 50,
  checkpoints: [{d:15,t:6.68},{d:25,t:12.66},{d:35},{d:45},{d:50}], strokes: [5.2, 6.4, 7.6, 8.9, 10.1, 11.4, 12.6, 13.9] });
const la = S.swimAnalyze_(live);
assert.strictEqual(la.rows[2].strokes, null); assert.strictEqual(la.rows[2].t, null);
assert.ok(la.rows[1].strokes > 0);
assert.strictEqual(la.finish.lastStrokeGap, null);

// 4) No stroke events -> strokes stay null (manual counts in the sheet are preserved)
const ns = S.swimAnalyze_(S.swimNormalizeRace_({ id: 'n', style: 'fs', distance: 50, checkpoints: [{d:15,t:6},{d:50,t:25}] }));
assert.strictEqual(ns.rows[1].strokes, null);
const plan = S.swimInputPlan_(S.swimNormalizeRace_({ id: 'n', style: 'fs', distance: 50, checkpoints: [{d:15,t:6},{d:50,t:25}] }), ns,
  S.swimBuildLayout_(S.swimNormalizeRace_({ id: 'n', style: 'fs', distance: 50, checkpoints: [{d:15,t:6},{d:50,t:25}] }), ns));
assert.strictEqual(plan.columns.length, 1, 'only times column written when no strokes were marked');

// 5) Exact rounding mode
const ex = S.swimNormalizeRace_({ id: 'e', style: 'fs', distance: 50, strokeRounding: 0, checkpoints: [{d:15,t:6.2},{d:25,t:11.1}], strokes });
const exa = S.swimAnalyze_(ex);
assert.ok(near(exa.rows[0].strokes, 3.4), 'exact ' + exa.rows[0].strokes);

// 6) Every layout preset builds without overlapping merges
function overlaps(m1, m2) { return !(m1[2] < m2[0] || m2[2] < m1[0] || m1[3] < m2[1] || m2[3] < m1[1]); }
const presets = {
  50: [15,25,35,45,50], 100: [15,25,35,45,50,65,75,85,95,100],
};
function lcm(dist, per) { const out = []; for (let w = 0; w < dist; w += 50) per.forEach(p => { const d = w + p; if (d <= dist && (w === 0 || p !== 0)) out.push(d); }); return out; }
const configs = [
  ['fs', 50, [15,25,35,45,50]], ['fs', 100, lcm(100,[15,25,35,45,50])], ['fs', 200, lcm(200,[15,25,35,45,50])],
  ['fs', 400, lcm(400,[15,25,45,50])], ['fs', 800, lcm(800,[15,45,50])], ['fs', 1500, lcm(1500,[15,45,50])],
  ['im', 400, lcm(400,[15,25,45,50])],
];
configs.forEach(([style, dist, cps]) => {
  const r = S.swimNormalizeRace_({ id: 'p', style, distance: dist, pool: 50, checkpoints: cps.map(d => ({ d })) });
  const an = S.swimAnalyze_(r);
  const lay = S.swimBuildLayout_(r, an);
  const ms = lay.grid.merges;
  for (let i = 0; i < ms.length; i++) for (let j = i + 1; j < ms.length; j++)
    assert.ok(!overlaps(ms[i], ms[j]), `overlapping merges ${dist}: ${ms[i]} / ${ms[j]}`);
  // every formula references only rows that exist in the grid
  Object.values(lay.grid.cells).forEach(c => {
    if (typeof c.v === 'string' && c.v.startsWith('=')) {
      assert.ok(!/undefined|NaN|null/.test(c.v), `bad formula at ${c.r}:${c.c}: ${c.v}`);
    }
  });
  console.log(`preset ${style} ${dist}: rows=${an.rows.length} turns=${an.turnWalls.length} gridRows=${lay.grid.maxRow} charts=${lay.charts.length}`);
});
console.log('ALL ANALYSIS TESTS PASSED');
