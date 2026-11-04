// 2.16.2: one file of analyses. Up to 2.16.1, when Google did not open the file of the analyses for a moment, the script made a
// new file with the same name and the setting went there: the next analyses apart from the old ones. Now:
//  - the file of the setting is opened with retries; if it does not open, a message with retry (Kinovea sends again by itself),
//    never a new file (also in the trash: a message to restore it);
//  - with no setting, an existing file with the name of the reports (the first one made, of those with analyses) before a new one;
//  - swimReportsFilesCheck only reads; swimReportsFilesMerge gathers everything in the first file (the original): the analyses
//    of the other file rebuilt there in the order they were made (newer ones replace older), the links of «Αναλύσεις Αγώνων»
//    there, the missing rows of the learning, elite references and levels, the setting there at once; it resumes after the time
//    limit, and at the end renames the other file to «… – παλιό», never deletes it.
// Fictional athletes and races only.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const TITLE = 'Αναλύσεις Αγώνων Κολύμβησης (Kinovea)';

function ctx() {
  const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'],
    [500101, 'ΦΑΝΤΑΣΤΙΚΟΣ ΚΟΛΥΜΒΗΤΗΣ Α', 'Ανδρών'], [500102, 'ΦΑΝΤΑΣΤΙΚΗ ΚΟΛΥΜΒΗΤΡΙΑ Β', 'Γυναικών']] });
  G.swimShowConnectionInfo = () => {};
  return G;
}
const callOf = G => body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);

// A fictional 100 m race (n: the number of the race, o: what changes).
function race(n, o) {
  o = o || {};
  const v = o.v || 1.8, cps = [], strokes = [];
  let t = 0;
  for (let L = 0; L < 2; L++) {
    const T = 50 / v * (L === 0 ? 0.92 : 1.03);
    [15, 25, 35, 45, 50].forEach(p => cps.push({ d: L * 50 + p, t: +(t + T * p / 50).toFixed(2) }));
    for (let s = t + 4.2; s < t + T - 0.4; s += 0.62) strokes.push(+s.toFixed(3));
    t += T;
  }
  return { id: 'race' + n, athlete: 'ΦΑΝΤΑΣΤΙΚΟΣ ΚΟΛΥΜΒΗΤΗΣ Α', athleteId: '500101', gender: 'Ανδρών', club: 'Όμιλος Δοκιμής', category: 'Άνδρες',
    style: 'fs', distance: 100, pool: 50, lane: '4', competition: o.comp || 'Αγώνες Δοκιμής ' + n, location: 'Πισίνα Δοκιμής', round: o.round || 'Τελικός',
    date: o.date || '1' + n + '.10.2026', video: 'fictional_' + n + '.mp4', checkpoints: cps, strokes, kicks: [1.4, 1.85, 2.3], breakouts: [3.8, 31.2],
    feetOff: [0.72, 28.5], entries: [1.05], manual: { reaction: 0.66, entryDistHands: 3.7, underwaterDist: 10.4, breakoutDist: 10.9, turns: { 50: { underwaterDist: 9.1 } } } };
}

// A fictional technique analysis (a turn) with one evaluation.
function training(n) {
  const groups = [{ label: 'Φάση δοκιμής', items: [1, 2, 3].map(i => ({ n: String(i), key: 'k' + i, text: 'Δοκιμαστικό σημείο ' + i, level: 'X', cue: 'Διόρθωση ' + i })) }];
  return { id: 'tech' + n, kind: 'training', athlete: 'ΦΑΝΤΑΣΤΙΚΗ ΚΟΛΥΜΒΗΤΡΙΑ Β', athleteId: '500102', gender: 'Γυναικών', style: 'fs', pool: 25,
    date: '0' + n + '.10.2026', checkpoints: [],
    tech: { phases: [{ id: 'tq1', kind: 'turn', attempt: 1, values: {} }], angles: [], snapshots: [], values: { notes: '' },
      sheets: { 'kv-turn-fs': { title: 'Στροφή – Ελεύθερο (δοκιμή)', source: 'Κ.Ο.Ε. – Kinovea Swim', scored: true, groups } },
      evals: [{ id: 'ev' + n, phase: 'tq1', phaseLabel: 'Στροφή 1', phaseKind: 'turn', sheet: 'kv-turn-fs', date: '0' + n + '/10/2026 09:00', notes: '',
        scores: { k1: { v: 1 }, k2: { v: 0 }, k3: { v: 1 } } }] } };
}

const indexOf = ss => ss.getSheetByName('Αγώνες');
const indexIds = ss => { const ix = indexOf(ss); return ix.getRange(2, 1, ix.getLastRow() - 1, 15).getValues().map(r => String(r[0])); };
const indexRow = (ss, id) => indexOf(ss).getRange(2, 1, indexOf(ss).getLastRow() - 1, 15).getValues().find(r => String(r[0]) === id);
// The column «Ενημέρωση» of the analyses (the mock always writes 09/10/2026 23:59 as "now": earlier stamps are older than "now").
function setStamps(ss, stamps) {
  const ix = indexOf(ss);
  ix.getRange(2, 1, ix.getLastRow() - 1, 1).getValues().forEach((r, i) => {
    if (stamps[String(r[0])]) ix.getRange(i + 2, 11).setValue(stamps[String(r[0])]);
  });
}
const sheetIds = ss => ss.getSheets().map(s => String(s.getSheetId()));

// ---------------------------------------------------------------------------------------------
// (a) Never a second file
// ---------------------------------------------------------------------------------------------
{
  const G = ctx(), call = callOf(G);
  G.swimSetup();
  const id = G._props.SWIM_REPORTS_SPREADSHEET_ID;
  assert.ok(id && G._all[id].getName() === TITLE, 'the reports file');
  assert.ok(/νέο αρχείο/.test(G._props.SWIM_REPORTS_LOG), 'the creation is noted: ' + G._props.SWIM_REPORTS_LOG);
  const files = () => Object.keys(G._all).length;
  const n0 = files();

  // Google does not answer twice: the third try opens it (after 1 s and 3 s).
  G._openFails[id] = 2;
  G._sleeps.length = 0;
  let r = call({ action: 'update', race: race(1) });
  assert.ok(r.ok, JSON.stringify(r).slice(0, 300));
  assert.deepStrictEqual(G._sleeps.slice(0, 2), [1000, 3000], 'waits between the tries');
  assert.strictEqual(files(), n0, 'no new file');
  assert.ok(G.SWIM_REPORTS_OPEN_ && G.SWIM_REPORTS_OPEN_.id === id, 'opened once for the request');

  // Google does not answer at all: a message with retry, the setting stays, no new file (the old script made one here).
  G._openFails[id] = Infinity;
  r = call({ action: 'update', race: race(2) });
  assert.ok(!r.ok && r.retry === true, JSON.stringify(r));
  assert.ok(/δεν άνοιξε αυτή τη στιγμή/.test(r.error) && /timed out/.test(r.error), r.error);
  assert.strictEqual(files(), n0, 'no new file when Google does not answer');
  assert.strictEqual(G._props.SWIM_REPORTS_SPREADSHEET_ID, id, 'the setting stays');
  r = call({ action: 'ping' });
  assert.ok(!r.ok && r.retry === true, 'ping: retry');
  r = call({ action: 'benchmarks' });
  assert.ok(!r.ok && r.retry === true, 'an action without the lock: retry');
  r = call({ action: 'update', race: race(2), lang: 'en' });
  assert.ok(/^The Google Sheet of the analyses did not open right now/.test(r.error) && r.retry, r.error);
  // In the trash: a message to restore it.
  G._drive._files[id]._trashed = true;
  r = call({ action: 'update', race: race(2) });
  assert.ok(!r.ok && r.retry && /στον κάδο του Drive/.test(r.error), r.error);
  assert.strictEqual(files(), n0, 'no new file when it is in the trash');
  G._drive._files[id]._trashed = false;
  // The KOE App does not open: the same.
  G._openFails[id] = 0;
  G._openFails.DATA = Infinity;
  r = call({ action: 'update', race: race(2) });
  assert.ok(!r.ok && r.retry && /KOE App/.test(r.error), r.error);
  G._openFails.DATA = 0;
  r = call({ action: 'update', race: race(2) });
  assert.ok(r.ok, 'it opens again: ' + JSON.stringify(r).slice(0, 200));
  // Other errors: no retry.
  r = call({ action: 'update', race: { id: 'bad' } });
  assert.ok(!r.ok && r.retry === undefined, 'no retry for other errors: ' + JSON.stringify(r));

  // No setting (e.g. deleted): the existing file, no new one.
  delete G._props.SWIM_REPORTS_SPREADSHEET_ID;
  r = call({ action: 'update', race: race(3) });
  assert.ok(r.ok);
  assert.strictEqual(G._props.SWIM_REPORTS_SPREADSHEET_ID, id, 'the existing file is found');
  assert.strictEqual(files(), n0);
  assert.ok(/βρέθηκε στον Drive \(2 αναλύσεις\)/.test(G._props.SWIM_REPORTS_LOG), G._props.SWIM_REPORTS_LOG);
  // No setting and the file does not open: a message, nothing new.
  delete G._props.SWIM_REPORTS_SPREADSHEET_ID;
  G._openFails[id] = Infinity;
  r = call({ action: 'update', race: race(4) });
  assert.ok(!r.ok && r.retry, 'no setting, the file does not open: ' + JSON.stringify(r));
  assert.ok(!G._props.SWIM_REPORTS_SPREADSHEET_ID && files() === n0, 'no setting, no new file');
  G._openFails[id] = 0;
  // No setting, two files with the name: the first one with analyses (not an empty one).
  const empty = G.SpreadsheetApp.create(TITLE);
  G.swimIndexSheet_(empty);
  r = call({ action: 'update', race: race(4) });
  assert.ok(r.ok && G._props.SWIM_REPORTS_SPREADSHEET_ID === id, 'the first file with analyses');
  assert.strictEqual(indexIds(empty).length === 0 || indexOf(empty).getLastRow() <= 1, true, 'nothing written to the empty one');
  // A file in the trash is not taken.
  delete G._props.SWIM_REPORTS_SPREADSHEET_ID;
  G._drive._files[id]._trashed = true;
  r = call({ action: 'update', race: race(5) });
  assert.ok(r.ok && G._props.SWIM_REPORTS_SPREADSHEET_ID === empty.getId(), 'not the one in the trash');
  G._drive._files[id]._trashed = false;
  // A first setup without any file: a new one (as before), with the settings of the KOE App when they write formulas with «,».
  const F = ctx();
  F.swimSetup();
  assert.ok(F._props.SWIM_REPORTS_SPREADSHEET_ID && Object.keys(F._all).length === 2, 'first setup: one new file');
  assert.strictEqual(F._all[F._props.SWIM_REPORTS_SPREADSHEET_ID].getSpreadsheetLocale(), 'en_US', 'the locale of the KOE App');
  // With a KOE App in Greek settings (formulas with «;»), en_GB: the formulas of the script work, the dates stay day/month.
  const F2 = ctx();
  F2._ss._locale = 'el_GR';
  F2.swimSetup();
  assert.strictEqual(F2._all[F2._props.SWIM_REPORTS_SPREADSHEET_ID].getSpreadsheetLocale(), 'en_GB', 'not a locale with «;»');
  assert.ok(F2.swimCommaLocale_('en_US') && F2.swimCommaLocale_('en_GB') && F2.swimCommaLocale_('en') && !F2.swimCommaLocale_('el_GR') &&
    !F2.swimCommaLocale_('de_DE') && !F2.swimCommaLocale_('en_ZA') && !F2.swimCommaLocale_(''), 'locales with «,»');
  console.log('test_reportsfile (a): no second file ok');
}

// ---------------------------------------------------------------------------------------------
// (b) Two files (as 2.16.1 left them): check, then merge
// ---------------------------------------------------------------------------------------------
{
  const G = ctx(), call = callOf(G);
  G.swimSetup();
  const oldId = G._props.SWIM_REPORTS_SPREADSHEET_ID;
  const OLD = G._all[oldId];
  // The old file: three races, a technique analysis, a race of the library (no sheet), an elite reference, two learning answers.
  [race(1), race(2), race(3), training(1)].forEach(x => assert.ok(call({ action: 'update', race: x }).ok, x.id));
  const lib = ['xl-1', '', 'ΦΑΝΤΑΣΤΙΚΟΣ ΚΟΛΥΜΒΗΤΗΣ Α', '500101', 'fs', '100', 'Τελικός', 'Παλιοί Αγώνες Δοκιμής', '01.01.2025', 61.2, '01/10/2026 08:00', '', '', '',
    JSON.stringify(Object.assign(race(0), { id: 'xl-1' }))];
  indexOf(OLD).getRange(indexOf(OLD).getLastRow() + 1, 1, 1, 15).setValues([lib]);
  const ref = n => ({ name: 'Αναφορά δοκιμής ' + n, gender: 'Ανδρών', style: 'fs', distance: 100, pool: 50, source: 'Δοκιμή', raceId: 'ref-' + n,
    times: [{ d: 50, t: 25.1 + n / 10 }, { d: 100, t: 52.3 + n / 10 }] });
  assert.ok(call({ action: 'saveReference', reference: ref(1) }).ok);
  const answer = id => ({ Id: id, Kind: 'race.split', Date: '2026-10-0' + id.slice(1) + 'T10:00:00.000+03:00', Value: 0.1 });
  assert.ok(call({ action: 'learningPush', origin: 'kv-one', records: [answer('a1'), answer('a2')] }).ok);
  // (2.16.5) and two scores of the technique learning.
  const score = id => ({ Id: id, Kind: 'techeval', Date: '2026-10-0' + id.slice(1), Key: 'tech1|start|s1|' + id, Check: 'knee', Measured: 90, Good: true });
  assert.ok(call({ action: 'techLearningPush', origin: 'kv-one', records: [score('t1'), score('t2')] }).stored === 2);
  setStamps(OLD, { race1: '01/10/2026 10:00', race2: '02/10/2026 10:00', race3: '03/10/2026 10:00', tech1: '03/10/2026 12:00' });

  // The split: a new file with the same name and the setting there (what 2.16.1 did when Google did not answer).
  const NEW = G.SpreadsheetApp.create(TITLE);
  const first = NEW.getSheets()[0];
  G.swimIndexSheet_(NEW);
  NEW.deleteSheet(first);
  G._props.SWIM_REPORTS_SPREADSHEET_ID = NEW.getId();
  // The new file: two new races, a newer state of race 2, an older one of race 3, a technique analysis, a second reference and
  // the learning answers of the next days (one of them already in the old file), a sheet of notes.
  const race2b = race(2, { round: 'Τελικός Α', comp: 'Αγώνες Δοκιμής 2 (διορθωμένοι)' });
  [race(4), race(5), race2b, race(3), training(2)].forEach(x => assert.ok(call({ action: 'update', race: x }).ok, x.id));
  assert.ok(call({ action: 'saveReference', reference: ref(2) }).ok);
  assert.ok(call({ action: 'learningPush', origin: 'kv-two', records: [answer('a2'), answer('a3')] }).ok);
  assert.ok(call({ action: 'techLearningPush', origin: 'kv-two', records: [score('t2'), score('t3'), score('t4')] }).stored === 3);
  NEW.insertSheet('Σημειώσεις').getRange(1, 1).setValue('δοκιμή');
  setStamps(NEW, { race4: '05/10/2026 10:00', race5: '06/10/2026 10:00', race2: '05/10/2026 09:00', race3: '02/10/2026 09:00', tech2: '05/10/2026 12:00' });
  // A row that does not read (broken data) stays in the old file.
  indexOf(NEW).getRange(indexOf(NEW).getLastRow() + 1, 1, 1, 15).setValues([['broken1', '', 'Κ', '', 'fs', '100', '', '', '', 50, '05/10/2026 11:00', '', '12345', '', '{not json']]);
  const summary = G._ss.getSheetByName('Αναλύσεις Αγώνων');
  const reportOf = raceId => { const r = summary.getRange(2, 1, summary.getLastRow() - 1, 23).getValues().find(x => String(x[21]) === raceId); return r ? String(r[19]) : null; };
  assert.ok(reportOf('race4').indexOf(NEW.getId()) > 0, 'before: race 4 links to the new file');

  // The new file in Greek settings (as the one of 31/10): the check says why its formulas show #ERROR!.
  NEW._locale = 'el_GR';
  // The check only reads.
  const snapshot = () => JSON.stringify({ props: G._props, files: Object.keys(G._all).sort().map(k => [k, G._all[k].getName()]),
    sheets: Object.keys(G._all).sort().map(k => G._all[k].getSheets().map(s => [s.getName(), s._cells])) });
  const before = snapshot();
  const text = G.swimReportsFilesCheck();
  assert.strictEqual(snapshot(), before, 'the check changes nothing');
  assert.ok(text.indexOf('Αρχεία «' + TITLE + '»: 2.') === 0, text);
  assert.ok(/1\) 5 αναλύσεις \(4 αγώνες, 1 τεχνική, 1 χωρίς φύλλο\)/.test(text), text);
  assert.ok(/2\) 6 αναλύσεις \(5 αγώνες, 1 τεχνική\), \d+ φύλλα/.test(text), 'the new file second, though it has more rows: ' + text);
  const lines = text.split('\n');
  const at = s => lines.findIndex(l => l.indexOf(s) >= 0);
  assert.ok(at('ΜΕΝΕΙ: εδώ θα μαζευτούν όλα') > at('1) ') && at('ΜΕΝΕΙ') < at('2) '), 'the old file stays');
  assert.ok(at('ΕΔΩ γράφει τώρα το Kinovea') > at('2) '), 'Kinovea writes now to the new file');
  assert.ok(/Θα μεταφερθούν στο αρχείο 1: 4 αναλύσεις \(3 αγώνες, 1 τεχνική· 1 υπάρχει και στο αρχείο 1 αλλά εδώ είναι νεότερη\), 1 είναι ήδη στο αρχείο 1\./.test(text), text);
  assert.ok(/Δεν διαβάζεται \(θα μείνει στο άλλο αρχείο\): .*broken1/.test(text), 'the broken row');
  assert.ok(/μάθηση Kinovea \+1, μάθηση τεχνικής \+2, elite αναφορές \+1/.test(text), 'the other sheets: ' + text);
  assert.ok(/δεν μεταφέρονται \(μένουν εκεί\): Σημειώσεις\./.test(text), 'a sheet of notes stays');
  assert.ok(/· ρυθμίσεις en_US/.test(text) && /⚠ Με τις ρυθμίσεις el_GR οι τύποι του script βγαίνουν #ERROR!/.test(text), 'the locale of each file: ' + text);
  assert.strictEqual(text.split('⚠').length, 2, 'the warning only for the file with «;»');
  // The labels: athlete and event when the link shows an error.
  assert.strictEqual(G.swimMergeItemLabel_({ id: 'x1', stamp: '01/11/2026 9:00', values: ['x1', '#ERROR!', 'Κολυμβητής Δοκιμής', '', 'τεχνική fs'] }),
    'Κολυμβητής Δοκιμής – τεχνική fs (x1, 01/11/2026 9:00)');
  assert.strictEqual(G.swimMergeItemLabel_({ id: 'x2', stamp: '', values: ['x2', 'Όνομα φύλλου', '', '', 'fs'] }), 'Όνομα φύλλου (x2, χωρίς ημερομηνία)');
  // The order: as they were made.
  const order = lines.filter(l => /^ {2}.*\((race|tech)\d, /.test(l)).map(l => /\(((?:race|tech)\d), /.exec(l)[1]);
  assert.deepStrictEqual(order, ['race2', 'race4', 'tech2', 'race5'], 'in the order they were made');

  // The merge: one analysis per run (as when the time limit comes), it resumes.
  G.SWIM_MERGE_BUDGET_MS = -1;
  let m = G.swimReportsFilesMerge();
  assert.strictEqual(G._props.SWIM_REPORTS_SPREADSHEET_ID, oldId, 'the setting goes to the old file at once');
  assert.ok(/1 μεταφέρθηκε τώρα, 1 ήταν ήδη εδώ, 3 απομένουν\./.test(m) && /ΣΥΝΕΧΕΙΑ: απομένουν 3\./.test(m), m);
  assert.ok(/Το Kinovea γράφει από τώρα σε αυτό/.test(m), m);
  // Meanwhile Kinovea writes to the old file.
  assert.ok(call({ action: 'update', race: race(6) }).ok);
  assert.ok(indexIds(OLD).includes('race6') && !indexIds(NEW).includes('race6'), 'a new analysis meanwhile: in the old file');
  let runs = 1;
  while (!/ΤΕΛΟΣ/.test(m) && runs < 10) { m = G.swimReportsFilesMerge(); runs++; }
  assert.strictEqual(runs, 4, 'one analysis per run: ' + m);
  assert.ok(/ΤΕΛΟΣ\. Το άλλο αρχείο μετονομάστηκε σε «Αναλύσεις Αγώνων Κολύμβησης \(Kinovea\) – παλιό \(συγχωνεύτηκε 09\/10\/2026\)»/.test(m), m);
  assert.ok(/Δεν διαβάζεται \(έμεινε μόνο στο παλιό αρχείο\): .*broken1/.test(m), m);

  // Everything in the old file, each with its sheet there.
  ['race1', 'race2', 'race3', 'tech1', 'xl-1', 'race4', 'race5', 'tech2', 'race6'].forEach(id => assert.ok(indexIds(OLD).includes(id), 'in the old file: ' + id));
  assert.strictEqual(indexIds(OLD).length, 9, 'no duplicates: ' + indexIds(OLD).join(','));
  ['race2', 'race4', 'race5', 'tech2'].forEach(id => {
    const row = indexRow(OLD, id);
    assert.ok(sheetIds(OLD).includes(String(row[12])), 'the sheet of ' + id + ' is in the old file');
  });
  assert.ok(/Τελικός Α/.test(String(indexRow(OLD, 'race2')[14])), 'the newer state of race 2');
  assert.ok(!/Τελικός Α/.test(String(indexRow(NEW, 'race3')[14])) && /Αγώνες Δοκιμής 3/.test(String(indexRow(OLD, 'race3')[14])), 'race 3 stays as it was');
  assert.strictEqual(String(indexRow(OLD, 'race3')[10]), '03/10/2026 10:00', 'the newer race 3 was not rebuilt');
  assert.strictEqual(String(indexRow(OLD, 'xl-1')[12]), '', 'the race of the library stays without a sheet');
  // The links of «Αναλύσεις Αγώνων» in the old file.
  ['race2', 'race4', 'race5', 'race6'].forEach(id => assert.ok(reportOf(id).indexOf(oldId) > 0 && reportOf(id).indexOf(NEW.getId()) < 0, 'link of ' + id + ': ' + reportOf(id)));
  // The evaluations of both technique analyses.
  const evals = OLD.getSheetByName('Αξιολογήσεις τεχνικής');
  const evalIds = evals.getRange(2, 18, evals.getLastRow() - 1, 1).getValues().map(r => String(r[0]));
  assert.ok(evalIds.includes('ev1') && evalIds.includes('ev2') && evalIds.length === 2, 'evaluations: ' + evalIds.join(','));
  // The learning: the missing answer, with the next number, no duplicates.
  const learn = OLD.getSheetByName('Μάθηση Kinovea');
  const lrows = learn.getRange(2, 1, learn.getLastRow() - 1, 6).getValues();
  assert.deepStrictEqual(lrows.map(r => String(r[1])), ['a1', 'a2', 'a3'], 'learning ids');
  assert.deepStrictEqual(lrows.map(r => Number(r[0])), [1, 2, 3], 'learning numbers');
  const tlearn = OLD.getSheetByName('Μάθηση τεχνικής');
  const trows = tlearn.getRange(2, 1, tlearn.getLastRow() - 1, 6).getValues();
  assert.deepStrictEqual(trows.map(r => String(r[1])), ['t1', 't2', 't3', 't4'], 'technique learning ids');
  assert.deepStrictEqual(trows.map(r => Number(r[0])), [1, 2, 3, 4], 'technique learning numbers');
  assert.ok(trows.every(r => String(r[4]) === 'techeval'), 'technique learning kinds');
  // The two references, each a block.
  const refs = OLD.getSheetByName('Elite αναφορές');
  const rr = refs.getRange(2, 1, refs.getLastRow() - 1, 10).getValues();
  assert.deepStrictEqual(rr.map(r => [String(r[0]), String(r[6]), String(r[8])]),
    [['Αναφορά δοκιμής 1', '50', 'ref-1'], ['', '100', ''], ['Αναφορά δοκιμής 2', '50', 'ref-2'], ['', '100', '']], 'references');
  // The new file stays (renamed), with all it had.
  assert.ok(G._all[NEW.getId()] && !G._drive._files[NEW.getId()]._trashed, 'not deleted');
  assert.strictEqual(indexIds(NEW).length, 6, 'its index as it was');
  assert.ok(NEW.getSheetByName('Σημειώσεις'), 'its other sheets as they were');
  assert.ok(/συγχώνευση/.test(G._props.SWIM_REPORTS_LOG), G._props.SWIM_REPORTS_LOG);

  // Again: one file, nothing to do; the check says so.
  m = G.swimReportsFilesMerge();
  assert.ok(/^Ένα αρχείο αναλύσεων \(9 αναλύσεις\): τίποτα για συγχώνευση\./.test(m), m);
  const again = G.swimReportsFilesCheck();
  assert.ok(/Ένα αρχείο μόνο: όλα εντάξει/.test(again) && /ΕΔΩ γράφει τώρα το Kinovea · το αρχείο των αναλύσεων/.test(again), again);
  // The analyses after the merge go to the old file.
  assert.ok(call({ action: 'update', race: race(7) }).ok);
  assert.ok(indexIds(OLD).includes('race7') && !indexIds(NEW).includes('race7'));
  console.log('test_reportsfile (b): check and merge ok (' + runs + ' runs)');
}

// ---------------------------------------------------------------------------------------------
// (c) The merge does nothing while a file does not open; from the menu of the sheet the text is in a window
// ---------------------------------------------------------------------------------------------
{
  const G = ctx(), call = callOf(G);
  G.swimSetup();
  const a = G._props.SWIM_REPORTS_SPREADSHEET_ID;
  assert.ok(call({ action: 'update', race: race(1) }).ok);
  const B = G.SpreadsheetApp.create(TITLE);
  G.swimIndexSheet_(B);
  G._openFails[B.getId()] = Infinity;
  const m = G.swimReportsFilesMerge();
  assert.ok(/Δεν άνοιξαν αυτή τη στιγμή: R\d+ \(Service Spreadsheets timed out/.test(m) && /Ξανατρέξε σε λίγο/.test(m), m);
  assert.strictEqual(G._props.SWIM_REPORTS_SPREADSHEET_ID, a, 'nothing changed');
  assert.ok(G._ui._dialogs.some(d => d.title === 'Αρχείο αναλύσεων: συγχώνευση' && d.modeless && /Δεν άνοιξαν αυτή τη στιγμή/.test(d.html)),
    'a window that does not wait for OK');
  assert.strictEqual(G._ui._alerts.length, 0, 'no alert');
  // Only from the project of the Kinovea connection (the dashboard project has its own settings, without the token).
  const D = ctx();
  delete D._props.SWIM_TOKEN;
  assert.ok(/Τρέξε το από το Apps Script του KOE App/.test(D.swimReportsFilesMerge()), 'not from another project');
  assert.ok(!D._props.SWIM_REPORTS_SPREADSHEET_ID && Object.keys(D._all).length === 1, 'nothing made from another project');
  console.log('test_reportsfile (c): nothing while a file does not open ok');
}
// ---------------------------------------------------------------------------------------------
// (d) «Ενημέρωση» as the sheet shows it (written as dd/MM/yyyy HH:mm, shown in the order of the language of the file)
// ---------------------------------------------------------------------------------------------
{
  const G = ctx();
  const k = G.swimStampKey_;
  ['03/11/2026 14:05', '3/11/2026 14:05:00', '3/11/2026, 2:05:00 μ.μ.', '3.11.2026 14:05', '3-11-2026 14:05', '2026-11-03 14:05:00', '3/11/2026 2:05:00 PM']
    .forEach(x => assert.strictEqual(k(x), 202611031405, x));
  assert.strictEqual(k('12/11/2026 12:30:00 π.μ.'), 202611120030);
  assert.strictEqual(k('30/10/2026'), 202610300000);
  assert.ok(k('30/10/2026 23:59') < k('1/11/2026 0:01'), 'later dates are greater');
  [null, undefined, '', 'χθες', 42].forEach(x => assert.strictEqual(k(x), 0, String(x)));
  console.log('test_reportsfile (d): stamps ok');
}
console.log('ALL REPORTS FILE TESTS PASSED');
