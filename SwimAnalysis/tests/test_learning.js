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

console.log('learning: ok');
