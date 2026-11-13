// 2.17.1: an analysis too big for a cell of the Google Sheet (50,000 characters). Until 2.17.0 a technique analysis with many attempts
// and evaluations (6 starts and 6 turns scored with the sheet of criteria) passed the limit even with less of it kept, and its update
// stopped with the error of the Sheets; smaller ones lost their snapshots in the index. Now: kept whole, compressed (gzip in base64),
// read again as it was sent; the next analysis of the athlete finds it; a later smaller update is plain JSON again; an enormous one
// still goes in (with less of it); a long race with every stroke keeps all its marks. Fictional data only.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [610001, 'ΜΕΓΑΛΗ ΔΟΚΙΜΗ', 'Γυναικών']] });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const post = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const index = () => reports.getSheetByName(G.SWIM_INDEX_SHEET);
const cellOf = id => String(index().getRange(G.swimIndexRow_(index(), id), G.SWIM_IDX.json + 1).getValue());

// (a) The mock holds the limit of the Sheets (the tests would not see the error otherwise).
const probe = reports.insertSheet('Όριο κελιού');
probe.getRange(1, 1).setValue('x'.repeat(50000));
assert.throws(() => probe.getRange(1, 1).setValue('x'.repeat(50001)), /maximum of 50000 characters/);
assert.throws(() => probe.getRange(1, 1, 1, 2).setValues([['ok', 'x'.repeat(50001)]]), /maximum of 50000 characters/);

const TURN_KEYS = ['in5', 'lastStrokeGap', 'lastStrokeDist', 'rotation', 'contact', 'turnTime', 'out5', 'out10', 'out15', 'inOut5', 'inOut10', 'inOut15',
  'v0_5', 'v5_10', 'v10_15', 'underwater', 'kicks', 'kickRate', 'kickDist', 'uwVel', 'breakoutTime', 'breakoutDist', 'bo15Vel', 'firstStroke'];
const START_KEYS = ['reaction', 'handsOff', 'backFoot', 'blockTime', 'blockPhase', 'flight', 'entryTime', 'entryDist', 'underwater', 'kicks', 'kickRate', 'kickDist',
  'uwVel', 'pushVel', 'breakoutTime', 'breakoutDist', 'bo15Vel', 'firstStroke', 't5', 't10', 't15', 'v0_5', 'v5_10', 'v10_15'];
// Texts that do not repeat (a real sheet of criteria compresses less than one sentence written again and again).
const WORDS = ['ώθηση', 'τοίχος', 'γόνατα', 'κεφάλι', 'σφιχτό', 'ευθεία', 'αργά', 'νωρίς', 'δελφινισμός', 'ανάσα', 'λεκάνη', 'ώμοι', 'πτήση', 'είσοδος',
  'βατήρας', 'αντίδραση', 'περιστροφή', 'επαφή', 'έξοδος', 'χεριά', 'ρυθμός', 'βάθος', 'γωνία', 'ταχύτητα', 'ολίσθηση', 'πόδια', 'χέρια', 'κορμός'];
let seed = 7;
const next = () => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed; };
const words = n => Array.from({ length: n }, () => WORDS[next() % WORDS.length]).join(' ');

function sheet(id, items) {
  const groups = [];
  let n = 0;
  for (let g = 0; g < 6; g++) {
    const list = [];
    for (let i = 0; i < items / 6; i++) { n++; list.push({ n: String(n), key: id + '.k' + n, text: words(11), target: '≤ 0.' + (30 + n) + ' s', cue: words(13) }); }
    groups.push({ label: 'Φάση ' + (g + 1), items: list });
  }
  return { title: 'Φύλλο ' + id, source: 'Δοκιμή', scored: true, groups };
}

function training(id, date, attempts, snapsPer, items, more) {
  const phases = [], angles = [], snapshots = [], evals = [], sheets = {};
  ['start', 'turn'].forEach(kind => {
    for (let a = 1; a <= attempts; a++) {
      const pid = id + '-' + kind[0] + a;
      const v = {};
      (kind === 'turn' ? TURN_KEYS : START_KEYS).forEach((k, i) => { v[k] = k === 'kicks' ? 5 + (a % 3) : +(1 + i * 0.137 + a * 0.011).toFixed(3); });
      phases.push({ id: pid, kind, attempt: a, values: v });
      for (let k = 0; k < 2; k++) angles.push({ id: pid + '-a' + k, name: 'Γωνία γονάτων στην επαφή', t: 0.5 + k, deg: 95.5 + a, phase: pid });
      for (let s = 0; s < snapsPer; s++)
        snapshots.push({ id: pid + '-s' + s, t: +(0.25 * s).toFixed(2), phase: pid, stage: 'Υποβρύχια φάση', event: s ? '' : 'Πρώτος δελφινισμός', comment: words(9),
          fileId: ('f' + pid + 's' + s).padEnd(33, 'x'), w: 1920, h: 1080 });
      if (items) {
        const sid = 'sheet-' + kind;
        if (!sheets[sid]) sheets[sid] = sheet(sid, items);
        const scores = {};
        sheets[sid].groups.forEach(g => g.items.forEach(it => { scores[it.key] = { v: (next() % 3) - 1, m: '0.' + (10 + next() % 80) + ' s', a: 1 }; }));
        evals.push({ id: pid + '-ev', phase: pid, phaseLabel: (kind === 'turn' ? 'Στροφή ' : 'Εκκίνηση ') + a, phaseKind: kind, sheet: sid, date, notes: words(12), scores });
      }
    }
  });
  return Object.assign({ id, kind: 'training', athlete: 'ΜΕΓΑΛΗ ΔΟΚΙΜΗ', athleteId: '610001', gender: 'Γυναικών', style: 'fs', pool: 50, date, checkpoints: [],
    tech: { phases, angles, snapshots, values: { notes: words(20) }, sheets, evals } }, more || {});
}

// (b) 6 starts and 6 turns, each scored with a sheet of 36 criteria, 8 snapshots each: kept whole.
const big = training('bigA', '10.11.2026', 6, 8, 36);
assert.ok(JSON.stringify(big).length > 60000, 'the payload is big: ' + JSON.stringify(big).length);
let r = post({ action: 'update', race: big });
assert.ok(r.ok, 'the big technique analysis is kept: ' + JSON.stringify(r).slice(0, 300));
let cell = cellOf('bigA');
assert.ok(cell.indexOf('gz:') === 0, 'compressed in its cell: ' + cell.slice(0, 20));
assert.ok(cell.length <= 50000, 'the cell holds it: ' + cell.length);
let stored = G.swimStoredRace_('bigA');
assert.ok(stored && stored.race, 'read again');
assert.strictEqual(stored.race.compacted, undefined, 'nothing dropped');
assert.strictEqual(stored.race.tech.phases.length, 12);
assert.strictEqual(stored.race.tech.snapshots.length, 96, 'every snapshot');
assert.strictEqual(stored.race.tech.evals.length, 12, 'every evaluation');
assert.strictEqual(stored.race.tech.angles.length, 24);
assert.deepStrictEqual(stored.race.tech.snapshots.map(s => s.comment), big.tech.snapshots.map(s => s.comment), 'the comments as sent');
assert.deepStrictEqual(Object.keys(stored.race.tech.sheets).sort(), ['sheet-start', 'sheet-turn']);
assert.strictEqual(stored.race.tech.sheets['sheet-turn'].groups[5].items[5].cue, big.tech.sheets['sheet-turn'].groups[5].items[5].cue, 'the texts of the sheet as sent');
// (The script keeps each score with its fields filled: the value, the measurement and the confirmation are the ones sent.)
const plain = scores => Object.keys(scores).sort().map(k => [k, scores[k].v, scores[k].m, scores[k].a]);
assert.deepStrictEqual(plain(stored.race.tech.evals[11].scores), plain(big.tech.evals[11].scores), 'the scores as sent');

// The same race written again by the script from what it kept (menu «ξανά»): the same report, no error.
const again = G.swimWithLang_(stored.race.lang, () => G.swimUpsertRace_(stored.race));
assert.ok(again, 'made again from the index');
assert.ok(cellOf('bigA').indexOf('gz:') === 0);

// (c) The next analysis of the athlete finds it (the list of Kinovea, the comparison of the report).
const list = post({ action: 'analyses', query: { tech: true, athleteId: '610001', athlete: 'ΜΕΓΑΛΗ ΔΟΚΙΜΗ', style: 'fs', pool: 50, poolUnit: 'm', raceId: 'bigB' } });
assert.ok(list.ok && list.analyses.some(a => a.id === 'bigA'), 'in the list of the athlete: ' + JSON.stringify(list).slice(0, 300));
const nextOne = training('bigB', '12.11.2026', 2, 4, 36, { compare: { techPrev: 'auto' } });
r = post({ action: 'update', race: nextOne });
assert.ok(r.ok, 'the next analysis: ' + JSON.stringify(r).slice(0, 300));
const cmp = G.swimTechCompare_(G.swimNormalizeRace_(nextOne));
assert.ok(cmp.prev && cmp.prev.id === 'bigA', 'compared with the big one: ' + JSON.stringify(cmp.prev).slice(0, 200));
assert.strictEqual(cmp.prev.snapshots.length, 96, 'with its snapshots (side by side in the report)');
assert.strictEqual(cmp.prev.evals.length, 12, 'and its evaluations');
assert.deepStrictEqual([cmp.prev.n.turn, cmp.prev.n.start], [6, 6]);

// (d) The same analysis sent again smaller: plain JSON again.
const small = training('bigA', '10.11.2026', 1, 2, 0);
r = post({ action: 'update', race: small });
assert.ok(r.ok);
cell = cellOf('bigA');
assert.ok(cell.charAt(0) === '{', 'plain JSON: ' + cell.slice(0, 20));
assert.strictEqual(JSON.parse(cell).tech.phases.length, 2);

// (e) An enormous one (40 starts and 40 turns scored, 10 snapshots each): still kept, with less of it when it must.
const huge = training('bigH', '11.11.2026', 40, 10, 60);
r = post({ action: 'update', race: huge });
assert.ok(r.ok, 'the enormous analysis is kept: ' + JSON.stringify(r).slice(0, 300));
cell = cellOf('bigH');
assert.ok(cell.length <= 50000, 'the cell holds it: ' + cell.length);
stored = G.swimStoredRace_('bigH');
assert.ok(stored && stored.race && stored.race.tech.phases.length === 80, 'every attempt with its numbers');

// (f) A long race with every stroke and kick (1500 m): all its marks kept (compressed when it must), nothing rounded or dropped.
const cps = [], strokes = [], kicks = [], breakouts = [], feetOff = [];
let t = 0;
for (let L = 0; L < 30; L++) {
  [15, 25, 35, 45, 50].forEach(p => cps.push({ d: L * 50 + p, t: +(t + 31.2 * p / 50).toFixed(3), v: +(t + 31.2 * p / 50 - 0.013).toFixed(3) }));
  for (let s = t + 4.1; s < t + 30.9; s += 0.757) strokes.push(+s.toFixed(3));
  feetOff.push(+(t + 0.613).toFixed(3));
  for (let k = 0; k < 6; k++) kicks.push(+(t + 1.007 + k * 0.431).toFixed(3));
  breakouts.push(+(t + 3.707).toFixed(3));
  t += 31.2;
}
const snaps = [];
for (let s = 0; s < 60; s++)
  snaps.push({ id: 'long-s' + s, t: +(s * 7.5).toFixed(2), phase: '', stage: 'Κολύμβηση', event: '', comment: words(20), fileId: ('flong' + s).padEnd(33, 'x'), w: 1920, h: 1080 });
const long = { id: 'bigL', athlete: 'ΜΕΓΑΛΗ ΔΟΚΙΜΗ', athleteId: '610001', gender: 'Γυναικών', style: 'fs', distance: 1500, distanceLabel: '1500', pool: 50, round: 'Τελικός',
  date: '9.11.2026', checkpoints: cps, strokes, kicks, breakouts, feetOff, entries: [1.013], manual: { reaction: 0.66, turns: {} }, tech: { snapshots: snaps } };
r = post({ action: 'update', race: long });
assert.ok(r.ok, 'the long race: ' + JSON.stringify(r).slice(0, 300));
cell = cellOf('bigL');
assert.ok(cell.length <= 50000, 'the cell holds it: ' + cell.length);
stored = G.swimStoredRace_('bigL');
assert.strictEqual(stored.race.compacted, undefined, 'nothing dropped from the long race');
assert.deepStrictEqual(stored.race.strokes, strokes, 'every stroke to the millisecond');
assert.deepStrictEqual(stored.race.kicks, kicks, 'every kick to the millisecond');
assert.strictEqual(stored.race.tech.snapshots.length, 60);

// (g) The lists that read every analysis of the index read the compressed ones too (PB candidates of the event).
const pbs = post({ action: 'analyses', query: { athleteId: '610001', athlete: 'ΜΕΓΑΛΗ ΔΟΚΙΜΗ', style: 'fs', distance: 1500, pool: 50, poolUnit: 'm', raceId: 'other' } });
assert.ok(pbs.ok && pbs.analyses.some(a => a.id === 'bigL'), 'the long race among the analyses of the event: ' + JSON.stringify(pbs).slice(0, 300));

// (h) The check of the compression from the editor of the Apps Script.
assert.ok(/εντάξει/.test(G.swimPackCheck()), G.swimPackCheck());

// (i) A cell that is neither JSON nor compressed (damaged by hand) is skipped as before, not an error of the whole list.
index().getRange(G.swimIndexRow_(index(), 'bigB'), G.SWIM_IDX.json + 1).setValue('gz:not-base64-at-all!!');
assert.strictEqual(G.swimStoredRace_('bigB'), null);
const after = post({ action: 'analyses', query: { tech: true, athleteId: '610001', athlete: 'ΜΕΓΑΛΗ ΔΟΚΙΜΗ', style: 'fs', pool: 50, poolUnit: 'm', raceId: 'x' } });
assert.ok(after.ok, 'the list without the damaged one');

console.log('test_bigindex: big analyses kept whole in the index (compressed), read again, compared, shrunk, enormous and long ones');
