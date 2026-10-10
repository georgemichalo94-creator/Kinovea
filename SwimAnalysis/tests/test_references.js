// Elite references: sheet parsing, save / replace from Kinovea, reference from a stored analysis.
const { makeContext } = require('./mock_gas');
const assert = require('assert');
const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ'], [1, 'A']] });
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ app: 'kinovea-swim', token: 'tok' }, body)) } }).text);
G.swimShowConnectionInfo = () => {};
G.swimSetup();

const reports = () => G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const sheet = reports().getSheetByName('Elite αναφορές');
assert.ok(sheet, 'references sheet created by setup');
assert.strictEqual(sheet.getRange(1, 1).getValue(), 'Αναφορά');

let r = call({ action: 'references' });
assert.ok(r.ok, JSON.stringify(r));
assert.strictEqual(r.references.length, 0);

// The analyst pastes references (one row per checkpoint, name only on the first row).
const rows = [
  ['WR test', 'Ανδρών', 'ελεύθερο', '100', 50, 'test', 50, '22,28', ''],
  ['', '', '', '', '', '', 100, '46.40', ''],
  ['EJC Final', '', 'bk', '200', '', 'EJC', 0, '0.61', ''],
  ['', '', '', '', '', '', 15, '6.46', ''],
  ['', '', '', '', '', '', 25, '12.28', ''],
  ['', '', '', '', '', '', 50, '27.36', ''],
  ['', '', '', '', '', '', 100, '56.65', ''],
  ['', '', '', '', '', '', 150, '1:26.50', ''],
  ['', '', '', '', '', '', 200, '1:56.00', ''],
  ['Bad style', '', 'xyz', '100', 50, '', 50, '25', ''],
  ['', '', '', '', '', '', 100, '52', ''],
  ['Relay', 'Μικτή', 'medley', '4x100', 50, '', 100, '53.50', ''],
  ['', '', '', '', '', '', 200, '1:52.10', ''],
  ['Bad time', '', 'fs', '50', 25, '', 50, 'abc', ''],
  ['WR test', 'Ανδρών', 'fs', '100', 50, 'duplicate name', 100, '47.00', ''],
];
sheet.getRange(2, 1, rows.length, 9).setValues(rows);
r = call({ action: 'references' });
assert.ok(r.ok, JSON.stringify(r));
const names = r.references.map(x => x.name);
assert.deepStrictEqual(names, ['WR test', 'EJC Final', 'Relay', 'WR test'], names.join(','));
const wr = r.references[0];
assert.strictEqual(wr.style, 'fs'); assert.strictEqual(wr.distance, 100); assert.strictEqual(wr.relay, false); assert.strictEqual(wr.pool, 50);
assert.strictEqual(JSON.stringify(wr.times), JSON.stringify([{ d: 50, t: 22.28 }, { d: 100, t: 46.4 }]));
const ejc = r.references[1];
assert.strictEqual(ejc.pool, 50, 'empty pool = 50');
assert.strictEqual(JSON.stringify(ejc.times.map(p => p.d)), JSON.stringify([15, 25, 50, 100, 150, 200]), 'reaction row (0 m) ignored');
assert.strictEqual(ejc.times[4].t, 86.5); assert.strictEqual(ejc.times[5].t, 116);
const relay = r.references[2];
assert.strictEqual(relay.style, 'im'); assert.strictEqual(relay.distance, 400); assert.strictEqual(relay.relay, true);
assert.notStrictEqual(r.references[0].id, r.references[3].id, 'duplicate ids made unique');
assert.ok(r.skipped.some(x => x.includes('άγνωστο στυλ')), JSON.stringify(r.skipped));
assert.ok(r.skipped.some(x => x.includes('μη έγκυρος χρόνος')), JSON.stringify(r.skipped));

// Save from Kinovea, then save again (same race id) with other times: the block is replaced.
const ref = { raceId: 'aabbccddeeff00112233445566778899', name: 'Muratory – EJC Final', gender: '', style: 'bk', distance: 200, relay: false, pool: 50, source: 'EJC · Final',
  times: [{ d: 15, t: 6.46 }, { d: 25, t: 12.28 }, { d: 50, t: 27.36 }] };
r = call({ action: 'saveReference', reference: ref });
assert.ok(r.ok, JSON.stringify(r)); assert.strictEqual(r.count, 3);
const lastBefore = sheet.getLastRow();
ref.times.push({ d: 100, t: 56.65 }, { d: 200, t: 116.0 });
r = call({ action: 'saveReference', reference: ref });
assert.ok(r.ok, JSON.stringify(r)); assert.strictEqual(r.count, 5);
assert.strictEqual(sheet.getLastRow(), lastBefore + 2, 'replaced, not appended twice');
r = call({ action: 'references' });
const saved = r.references.filter(x => x.id === ref.raceId);
assert.strictEqual(saved.length, 1);
assert.strictEqual(JSON.stringify(saved[0].times.map(p => p.d)), JSON.stringify([15, 25, 50, 100, 200]));
assert.strictEqual(saved[0].times[4].t, 116);
assert.strictEqual(r.references.length, 5);

// Deleting a block in the middle keeps the following references intact.
assert.strictEqual(G.swimDeleteReferenceBlock_(sheet, ref.raceId), 5);
r = call({ action: 'references' });
assert.strictEqual(r.references.length, 4);
assert.deepStrictEqual(r.references.map(x => x.name), ['WR test', 'EJC Final', 'Relay', 'WR test']);

// Reference from a stored analysis (menu "Η επιλεγμένη ανάλυση ως elite αναφορά").
const race = G.swimNormalizeRace_(G.swimDemoRace_());
const fromRace = G.swimReferenceFromRace_(race);
assert.strictEqual(fromRace.times.length, 10);
assert.strictEqual(fromRace.raceId, 'demo-100fs');
r = G.swimSaveReference_(fromRace);
assert.ok(r.ok);
r = call({ action: 'references' });
assert.ok(r.references.some(x => x.id === 'demo-100fs' && x.times.length === 10));

// Bad input
r = call({ action: 'saveReference', reference: { name: 'x', style: 'zz', distance: 100, times: [{ d: 50, t: 25 }] } });
assert.strictEqual(r.ok, false);
r = call({ action: 'saveReference', reference: { name: 'x', style: 'fs', distance: 100, times: [] } });
assert.strictEqual(r.ok, false);

// Time parsing
assert.strictEqual(G.swimRefTime_('1:02.35', ''), 62.35);
assert.strictEqual(G.swimRefTime_('26,12', ''), 26.12);
assert.strictEqual(G.swimRefTime_(27.5, ''), 27.5);
assert.strictEqual(G.swimRefTime_(new Date(0), '0:58.20'), 58.2);
assert.strictEqual(G.swimRefTime_('', ''), null);
assert.strictEqual(G.swimStyleCode_('Πεταλούδα'), 'fly');
assert.strictEqual(G.swimStyleCode_('Ύπτιο'), 'bk');
assert.strictEqual(G.swimStyleCode_('Πρόσθιο'), 'br');
assert.strictEqual(G.swimStyleCode_('Freestyle'), 'fs');
assert.strictEqual(G.swimStyleCode_('Μικτή ατομική'), 'im');
assert.deepStrictEqual(JSON.parse(JSON.stringify(G.swimDistanceValue_('4 x 200'))), { distance: 800, relay: true });
assert.strictEqual(G.swimPoolValue_('SCM'), 25);
console.log('REFERENCES OK');
