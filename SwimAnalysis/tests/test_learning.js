// The shared learning (2.9.7): the answers of every Kinovea into one sheet, each one once, and the new ones back to every Kinovea.
// Fictional answers only (numbers and codes, no names).
const { makeContext } = require('./mock_gas');
const assert = require('assert');
const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ'], [900001, 'ΔΟΚΙΜΗ ΑΛΦΑ']] });
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);

const answer = (id, kind, error, extra) => Object.assign({ Id: id, Date: '2026-10-26T20:00:0' + (id.length % 10) + '.000+02:00', Kind: kind, Video: 'a1b2c3d4e5', Right: true, Error: error, Cues: { 'far.6@br': error + 0.05 }, Confidence: 0.85 }, extra || {});

// Nothing yet: an empty answer, no sheet made by a pull.
let p = call({ action: 'learningPull', since: 0, max: 100 });
assert.ok(p.ok && p.records.length === 0 && p.seq === 0 && p.more === false, JSON.stringify(p));

// A Kinovea sends three answers: all in, with its id where they have none.
let r = call({ action: 'learningPush', origin: 'kv-one', records: [answer('r1', 'race.split', 0.10), answer('r2', 'race.split', -0.05), answer('r3', 'race.breakout', 0.30, { Origin: 'kv-old' })] });
assert.ok(r.ok && r.stored === 3 && r.seq === 3, JSON.stringify(r));
const sheet = G._reportsSs ? G._reportsSs().getSheetByName('Μάθηση Kinovea') : null;

// The same answers again (another copy of the same learning) and one new: only the new one.
r = call({ action: 'learningPush', origin: 'kv-two', records: [answer('r2', 'race.split', -0.05), answer('r3', 'race.breakout', 0.30), answer('r4', 'race.split', 0.20)] });
assert.ok(r.ok && r.stored === 1 && r.seq === 4, JSON.stringify(r));

// Bad entries are left out (no id, no kind, not an object).
r = call({ action: 'learningPush', origin: 'kv-two', records: [{ Kind: 'race.split' }, { Id: 'x1' }, 'text', null, [1, 2]] });
assert.ok(r.ok && r.stored === 0 && r.seq === 4, JSON.stringify(r));

// The first pull of a Kinovea: the latest (max), with where the next one starts.
p = call({ action: 'learningPull', since: 0, max: 2 });
assert.ok(p.ok && p.records.length === 2 && p.seq === 4 && p.total === 4 && !p.more, JSON.stringify(p));
assert.deepStrictEqual(p.records.map(x => x.Id), ['r3', 'r4']);
assert.strictEqual(p.records[0].Origin, 'kv-old');
assert.strictEqual(p.records[1].Origin, 'kv-two');

// From the start in pages: everything in order, the origin of each.
p = call({ action: 'learningPull', since: 0, max: 100 });
assert.deepStrictEqual(p.records.map(x => x.Id), ['r1', 'r2', 'r3', 'r4']);
assert.deepStrictEqual(p.records.map(x => x.Origin), ['kv-one', 'kv-one', 'kv-old', 'kv-two']);
assert.ok(Math.abs(p.records[0].Cues['far.6@br'] - 0.15) < 1e-9);
let q = call({ action: 'learningPull', since: 1, max: 2 });
assert.ok(q.ok && q.records.length === 2 && q.seq === 3 && q.more === true, JSON.stringify(q));
assert.deepStrictEqual(q.records.map(x => x.Id), ['r2', 'r3']);
q = call({ action: 'learningPull', since: 3, max: 2 });
assert.ok(q.ok && q.records.length === 1 && q.seq === 4 && q.more === false, JSON.stringify(q));

// Nothing new since the last one; a number past the end (the sheet emptied): from the start again.
q = call({ action: 'learningPull', since: 4, max: 10 });
assert.ok(q.ok && q.records.length === 0 && q.seq === 4, JSON.stringify(q));
q = call({ action: 'learningPull', since: 99, max: 10 });
assert.ok(q.ok && q.records.length === 4 && q.seq === 4, JSON.stringify(q));

// An answer taken away travels as its own row (kind ~forget, the key of the event).
r = call({ action: 'learningPush', origin: 'kv-one', records: [{ Id: 'f1', Kind: '~forget', Key: 'a1b2c3d4e5|race|race.split|25', Date: '2026-10-26T20:05:00.000+02:00' }] });
assert.ok(r.ok && r.stored === 1 && r.seq === 5, JSON.stringify(r));
q = call({ action: 'learningPull', since: 4, max: 10 });
assert.ok(q.records.length === 1 && q.records[0].Kind === '~forget' && q.records[0].Key.endsWith('|25'), JSON.stringify(q));

// The wrong token: refused.
const bad = JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify({ token: 'nope', app: 'kinovea-swim', action: 'learningPull' }) } }).text);
assert.ok(!bad.ok);

// (2.16.5) The learning of the technique evaluation: the same rows in its own sheet, only its kinds (techeval, techeval~forget).
const score = (id, key, measured, good, extra) => Object.assign({ Id: id, Kind: 'techeval', Date: '2026-11-04', At: '2026-11-04T09:00:0' + (id.length % 10) + '.000Z',
  Key: 'race0001|start|sheetA|' + key, Check: 'knee.angle', Criterion: key, Sheet: 'sheetA', Style: 'Free', Measured: measured, Good: good }, extra || {});
let t = call({ action: 'techLearningPull', since: 0, max: 100 });
assert.ok(t.ok && t.records.length === 0 && t.seq === 0 && t.more === false, JSON.stringify(t));
assert.strictEqual(G.swimReportsSpreadsheet_().getSheetByName('Μάθηση τεχνικής'), null, 'a pull makes no sheet');
t = call({ action: 'techLearningPush', origin: 'kv-one', records: [score('t1', 'c1', 92.5, true), score('t2', 'c2', 131.0, false),
  { Id: 'm9', Kind: 'race.split', Error: 0.1 }, { Id: 'm8', Kind: 'tech.turn.wall', Error: 0.2 }] });
assert.ok(t.ok && t.stored === 2 && t.seq === 2, 'only the scores go in the technique sheet: ' + JSON.stringify(t));
// The same again from another copy and one taken away: only the new row.
t = call({ action: 'techLearningPush', origin: 'kv-two', records: [score('t2', 'c2', 131.0, false),
  { Id: 'tf1', Kind: 'techeval~forget', Key: 'race0001|start|sheetA|c1', Prefix: false, Date: '2026-11-04', At: '2026-11-04T09:10:00.000Z' }] });
assert.ok(t.ok && t.stored === 1 && t.seq === 3, JSON.stringify(t));
t = call({ action: 'techLearningPull', since: 0, max: 100 });
assert.deepStrictEqual(t.records.map(x => x.Id), ['t1', 't2', 'tf1']);
assert.deepStrictEqual(t.records.map(x => x.Origin), ['kv-one', 'kv-one', 'kv-two']);
assert.ok(t.records[0].Measured === 92.5 && t.records[0].Good === true && t.records[1].Good === false, JSON.stringify(t.records));
t = call({ action: 'techLearningPull', since: 2, max: 100 });
assert.ok(t.records.length === 1 && t.records[0].Kind === 'techeval~forget' && t.seq === 3 && !t.more, JSON.stringify(t));
// The two learnings stay apart: the marks one did not get the scores, and kept the 'tech.turn.*' answers of the start/turn pilot.
p = call({ action: 'learningPull', since: 0, max: 100 });
assert.ok(p.records.every(x => x.Kind !== 'techeval' && x.Kind !== 'techeval~forget'), JSON.stringify(p.records.map(x => x.Kind)));
r = call({ action: 'learningPush', origin: 'kv-one', records: [answer('r5', 'tech.turn.wall', 0.04)] });
assert.ok(r.ok && r.stored === 1 && r.seq === 6, JSON.stringify(r));
const techSheet = G.swimReportsSpreadsheet_().getSheetByName('Μάθηση τεχνικής');
assert.deepStrictEqual(techSheet.getRange(1, 1, 1, 6).getValues()[0], ['Αύξων', 'Id', 'Kinovea', 'Ημερομηνία', 'Είδος', 'Απάντηση']);
assert.strictEqual(techSheet.getLastRow(), 4);

console.log('learning: ok');
