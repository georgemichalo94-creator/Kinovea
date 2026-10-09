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
// formula-style SR: strokes/2*60/segTime = 9.5/2*60/4.9
assert.ok(near(rows[1].sr, 9.5 / 2 * 60 / 4.9), 'sr');
// wall at 24.0: last press 23.5 before wall, next 28.0 after -> no interpolation across the wall
const n50 = rows.slice(0, 5).reduce((s, r) => s + r.strokes, 0);
assert.strictEqual(n50, 38, 'strokes to 50 = number of presses before wall'); // 5.0..23.5 => 38 presses
// 50-65 segment (24.0 -> 30.3): presses 28.0..30.0 (5) + 0.6 -> 5.6 -> 5.5
assert.strictEqual(rows[5].strokes, 5.5, '50-65');
assert.strictEqual(rows[0].isSwim, false); assert.strictEqual(rows[1].isSwim, true); assert.strictEqual(rows[4].isSwim, true);
assert.strictEqual(rows[5].isSwim, false); assert.strictEqual(rows[4].isFree, false); assert.strictEqual(rows[3].isFree, true);
// start
assert.strictEqual(a.start.blockTime, 0.7); assert.strictEqual(a.start.entryTime, 1.0);
assert.strictEqual(a.start.underwaterTime, 4.5); assert.strictEqual(a.start.kicks, 4);
// turn at 50
const t = a.turns[0];
assert.strictEqual(t.wall, 50); assert.ok(near(t.turnTime, 0.6)); assert.ok(near(t.underwaterTime, 27.5 - 24.6));
assert.strictEqual(t.kicks, 3); assert.ok(near(t.lastStrokeGap, 0.5));
// finish: last press 49.0 -> gap 0.3
assert.ok(near(a.finish.lastStrokeGap, 49.3 - 49.0), 'finish gap ' + a.finish.lastStrokeGap);

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
