const { makeContext } = require('./mock_gas');
const assert = require('assert');
const G = makeContext();
const call = body => JSON.parse(G.doPost({ postData: { contents: JSON.stringify(body) } }).text);

// wrong token
let r = call({ token: 'bad', action: 'ping' });
assert.strictEqual(r.ok, false);
// ping
r = call({ token: 'tok', action: 'ping' });
assert.ok(r.ok, JSON.stringify(r));

// live sequence: first only header, then progressively more events, then rename athlete, then pdf
const race = { id: 'r1', athlete: '', style: 'br', distance: 100, pool: 50, round: 'Final', competition: 'EJC', date: '2.7.2026',
  checkpoints: [15,25,35,45,50,65,75,85,95,100].map(d => ({ d, t: null })), strokes: [], kicks: [], breakouts: [], feetOff: [], entries: [], manual: {} };
r = call({ token: 'tok', action: 'update', race });
assert.ok(r.ok, JSON.stringify(r)); assert.ok(r.rebuilt);
const firstName = r.sheetName;
const times = [6.68,12.66,18.78,24.87,27.89,35.1,41.4,47.8,54.2,57.6];
for (let i = 0; i < times.length; i++) {
  race.checkpoints[i].t = times[i];
  for (let k = 0; k < 5; k++) race.strokes.push(+(times[i] - 1 + k * 0.2).toFixed(2));
  race.strokes.sort((a, b) => a - b);
  r = call({ token: 'tok', action: 'update', race });
  assert.ok(r.ok, JSON.stringify(r)); assert.strictEqual(r.rebuilt, false);
}
race.athlete = 'Ντούμας'; race.feetOff = [0.7, 28.3]; race.breakouts = [5.2, 31.0]; race.kicks = [2, 2.4, 29, 29.4]; race.entries = [0.98];
race.manual = { reaction: 0.26, underwaterDist: 12.17, turns: { '50': { underwaterDist: 8.5 } }, finishLastStrokeDist: 0.3 };
r = call({ token: 'tok', action: 'update', race });
assert.ok(r.ok, JSON.stringify(r));
assert.notStrictEqual(r.sheetName, firstName, 'sheet renamed after athlete name typed');
assert.strictEqual(r.turns[0].kicks, 2); assert.ok(Math.abs(r.turns[0].turnTime - 0.41) < 1e-9);
// structure change -> rebuild
race.checkpoints = [15,25,45,50,65,75,95,100].map((d, i) => ({ d, t: [6.68,12.66,24.87,27.89,35.1,41.4,54.2,57.6][i] }));
r = call({ token: 'tok', action: 'update', race });
assert.ok(r.ok && r.rebuilt, JSON.stringify(r));
// pdf
r = call({ token: 'tok', action: 'pdf', race });
assert.ok(r.ok && r.pdf && r.pdf.url, JSON.stringify(r));
// second swimmer, same names -> unique sheet name
r = call({ token: 'tok', action: 'update', race: Object.assign({}, race, { id: 'r2' }) });
assert.ok(r.ok); assert.notStrictEqual(r.sheetName, G._ss.getSheets()[1].getName());
// index sheet holds 2 races
const idx = G._ss.getSheetByName('Αγώνες');
assert.strictEqual(idx.getLastRow(), 3);
// demo race builds
G.swimUpsertRace_(G.swimDemoRace_());
// all presets end to end
[[50,'fs'],[200,'im'],[400,'fs'],[800,'fs'],[1500,'fs']].forEach(([dist, style], k) => {
  const per = dist <= 200 ? [15,25,35,45,50] : dist <= 400 ? [15,25,45,50] : [15,45,50];
  const cps = []; for (let w = 0; w < dist; w += 50) per.forEach(p => cps.push(w + p));
  const res = call({ token: 'tok', action: 'update', race: { id: 'p' + k, athlete: 'P' + k, style, distance: dist, pool: 50, checkpoints: cps.map(d => ({ d, t: d * 0.6 })) } });
  assert.ok(res.ok, JSON.stringify(res));
});
console.log('E2E OK; sheets:', G._ss.getSheets().map(s => s.getName()).join(' | '));
