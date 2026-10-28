// 2.13.0: technique evaluation with sheets of criteria: the section of the report (points by phase, ✓/✗/–, 🤖, measurement, what to
// correct, frames of the errors), the sheet «Αξιολογήσεις τεχνικής» (one row per evaluation), the progress of the athlete, the lists
// of errors, the level line, a race without evaluations unchanged, and the English report.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [500001, 'ΔΟΚΙΜΑΣΤΙΚΟΣ ΑΘΛΗΤΗΣ', 'Ανδρών']] });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const sheetOf = r => reports.getSheets().find(s => s.getSheetId() === r.sheetId);
const texts = sh => Object.values(sh._cells).map(c => c.v).filter(v => typeof v === 'string');

// A fictional sheet (the real ones stay on the coach's computer): two phases, five points.
const sheet = {
  title: 'Δοκιμαστικό φύλλο στροφής', source: 'Δοκιμή', scored: true,
  groups: [
    { label: 'Προσέγγιση', items: [{ n: '1', key: 'a.one', text: 'Σημείο ένα', level: 'A', target: '≤ 0.3 s' }, { n: '2', key: 'a.two', text: 'Σημείο δύο', level: 'P', cue: 'Διόρθωση δύο' }] },
    { label: 'Ώθηση', items: [{ n: '3', key: 'p.three', text: 'Σημείο τρία', level: 'A', cue: 'Διόρθωση τρία' }, { n: '4', key: 'p.four', text: 'Σημείο τέσσερα', level: 'X' },
      { n: '5', key: 'p.five', text: 'Σημείο πέντε', level: 'P' }] }
  ]
};
const errors = {
  title: 'Δοκιμαστικά λάθη', source: 'Δοκιμή', scored: false,
  groups: [{ label: 'Λάθη', items: [{ n: '1-1', key: 'e.one', text: 'Λάθος ένα', level: 'A', cue: 'Άσκηση ένα' }, { n: '1-2', key: 'e.two', text: 'Λάθος δύο', level: 'P' }] }]
};
const phases = [
  { id: 'tu1', kind: 'turn', attempt: 1, values: { in5: 2.6, turnTime: 0.8, contact: 0.3, out10: 3.5, inOut10: 6.1 } },
  { id: 'tu2', kind: 'turn', attempt: 2, values: { in5: 2.5, turnTime: 0.78, contact: 0.28, out10: 3.4, inOut10: 5.9 } }
];
const evalOne = {
  id: 'ev1', phase: 'tu1', phaseLabel: 'Στροφή 1', phaseKind: 'turn', sheet: 'test-turn', date: '20/10/2026 10:15', notes: 'Καλή προσπάθεια.',
  scores: { 'a.one': { v: 1, a: 1, m: '0.25 s' }, 'a.two': { v: 0, f: 'file-err-1' }, 'p.three': { v: 0, f: 'file-err-2', m: '125°' }, 'p.four': { v: -1 } },
  level: { label: 'Στροφή 5 μ. μέσα + 10 μ. έξω', value: 9.84, text: '9.84 s · επίπεδο 4', level: 4, levels: 8 }
};
const evalErrors = { id: 'ev2', phase: 'tu2', phaseLabel: 'Στροφή 2', phaseKind: 'turn', sheet: 'test-errors', date: '20/10/2026 10:20', notes: '',
  scores: { 'e.one': { v: 1, f: 'file-err-3' }, 'e.two': { v: 0 } } };
const training = { id: 'trE', kind: 'training', athlete: 'ΔΟΚΙΜΑΣΤΙΚΟΣ ΑΘΛΗΤΗΣ', athleteId: '500001', gender: 'Ανδρών', style: 'br', pool: 50, date: '20.10.2026',
  checkpoints: [], tech: { phases, angles: [], snapshots: [
    { id: 'sx1', t: -0.5, phase: 'tu1', stage: 'Προσέγγιση', event: 'eval:a.two', comment: '✗ Σημείο δύο', fileId: 'file-err-1' },
    { id: 'sx2', t: 0.3, phase: 'tu1', stage: 'Αποκόλληση', event: 'eval:p.three', comment: '✗ Σημείο τρία', fileId: 'file-err-2' },
    { id: 'sx3', t: 0.1, phase: 'tu2', stage: 'Επαφή', event: '', comment: 'Του αναλυτή', fileId: 'file-own' }],
    values: { notes: '' }, sheets: { 'test-turn': sheet, 'test-errors': errors }, evals: [evalOne, evalErrors] } };

// ---- 1) The section of the training report
let r = call({ action: 'update', race: training });
assert.ok(r.ok && r.rebuilt, JSON.stringify(r));
let sh = sheetOf(r);
let t = texts(sh);
assert.ok(t.includes('Αξιολόγηση τεχνικής'), 'section title');
assert.ok(t.some(v => v === 'Αξιολόγηση τεχνικής · Στροφή 1 · Δοκιμαστικό φύλλο στροφής'), 'title of the evaluation: ' + t.filter(v => /Αξιολόγηση/.test(v)).join(' | '));
assert.ok(t.some(v => /^Σύνολο 1\/5 \(βαθμολογήθηκαν 3\) · Στροφή 5 μ\. μέσα \+ 10 μ\. έξω: 9\.84 s · επίπεδο 4 \(από 8 επίπεδα, 1 = καλύτερο\)/.test(v)), 'summary with the level: ' + t.filter(v => /Σύνολο/.test(v)).join(' | '));
assert.ok(t.includes('Προσέγγιση   1/2') && t.includes('Ώθηση   0/3'), 'phase rows with their totals');
assert.ok(t.includes('🤖✓') && t.includes('✗') && t.includes('–') && t.includes('·'), 'results: auto ✓, ✗, not visible, not scored');
assert.ok(t.includes('0.25 s\nστόχος ≤ 0.3 s'), 'measurement with its target');
assert.ok(t.includes('Διόρθωση δύο') && t.includes('Διόρθωση τρία'), 'what to correct beside the errors');
assert.ok(t.includes('🤖 = από τις μετρήσεις του προγράμματος, χωρίς επιβεβαίωση του προπονητή.'), 'footnote of the automatic scores');
assert.ok(t.includes('Καλή προσπάθεια.'), 'notes of the evaluation');
// The error frames: two in a row with the point and what to correct; the analyst's own snapshot stays in «Στιγμιότυπα».
const built = (() => { const rr = G.swimNormalizeRace_(training); return G.swimBuildLayout_(rr, G.swimAnalyze_(rr), { pageHeight: w => G.swimPdfPageHeight_(reports, w) }); })();
const files = built.images.map(im => im.fileId);
assert.ok(files.includes('file-err-1') && files.includes('file-err-2') && files.includes('file-err-3'), 'frames of the errors: ' + files.join(','));
assert.ok(files.includes('file-own'), 'own snapshot kept');
assert.strictEqual(files.filter(f => f === 'file-err-1').length, 1, 'an error frame once (not again among the snapshots)');
assert.strictEqual(sh._images.length, files.length, 'images inserted over the grid');
const ms = built.grid.merges;
const ov = (a, b) => !(a[2] < b[0] || b[2] < a[0] || a[3] < b[1] || b[3] < a[1]);
for (let i = 0; i < ms.length; i++) for (let j = i + 1; j < ms.length; j++) assert.ok(!ov(ms[i], ms[j]), `overlapping merges ${ms[i]} / ${ms[j]}`);
assert.ok(t.includes('✗ 2. Σημείο δύο') && t.includes('→ Διόρθωση δύο'), 'caption of an error frame');
// The list of errors: «ναι» for an error there, «όχι» for one not there, the count of the errors.
assert.ok(t.includes('ναι') && t.includes('όχι'), 'yes/no on a list of errors');
assert.ok(t.some(v => /^Λάθη που υπάρχουν: 1 από 2/.test(v)), 'errors present');
assert.ok(t.includes('✗ 1-1. Λάθος ένα'), 'frame of a present error');

// ---- 2) The sheet of the evaluations (one row each) and the progress
const evSheet = reports.getSheetByName('Αξιολογήσεις τεχνικής');
assert.ok(evSheet, 'evaluations sheet');
let rows = evSheet.getRange(2, 1, evSheet.getLastRow() - 1, 18).getValues();
assert.strictEqual(rows.length, 2, 'two evaluations');
const row1 = rows.find(x => x[17] === 'ev1');
assert.ok(row1 && row1[1] === '500001' && row1[5] === 'test-turn' && row1[8] === 1 && row1[9] === 3 && row1[10] === 5 && row1[11] === 2 && row1[12] === 1 && row1[13] === 4,
  JSON.stringify(row1));
assert.ok(/2\. Σημείο δύο \| 3\. Σημείο τρία/.test(row1[15]), 'the errors of the evaluation: ' + row1[15]);
// Again: the same rows updated, not added.
evalOne.scores['p.three'] = { v: 1 };
r = call({ action: 'update', race: training });
assert.ok(r.ok && r.rebuilt, 'a changed score rebuilds the section');
rows = evSheet.getRange(2, 1, evSheet.getLastRow() - 1, 18).getValues();
assert.strictEqual(rows.length, 2, 'rows updated in place');
assert.strictEqual(rows.find(x => x[17] === 'ev1')[8], 2, 'new total');
t = texts(sheetOf(r));
assert.ok(!t.includes('Διόρθωση τρία'), 'no correction for a point done well');
// A later session of the same athlete with the same sheet: the progress (oldest first).
const later = JSON.parse(JSON.stringify(training));
later.id = 'trF'; later.date = '27.10.2026';
later.tech.evals = [Object.assign({}, evalOne, { id: 'ev3', date: '27/10/2026 09:00', scores: { 'a.one': { v: 1 }, 'a.two': { v: 1 }, 'p.three': { v: 1 }, 'p.four': { v: 1 }, 'p.five': { v: 0 } } })];
r = call({ action: 'update', race: later });
assert.ok(r.ok, JSON.stringify(r));
t = texts(sheetOf(r));
assert.ok(t.includes('Πρόοδος: Δοκιμαστικό φύλλο στροφής'), 'progress table');
const iOld = t.indexOf('20/10/2026 10:15'), iNew = t.indexOf('27/10/2026 09:00');
assert.ok(iOld >= 0 && iNew > iOld, 'oldest first');
assert.ok(t.includes('2/5') && t.includes('4/5'), 'totals of both');

// ---- 3) A training without evaluations: the stored JSON has no evaluation keys (the reports stay as they were)
const plain = JSON.parse(JSON.stringify(training));
plain.id = 'trP'; delete plain.tech.sheets; delete plain.tech.evals;
const norm = G.swimNormalizeRace_(plain);
assert.ok(!('evals' in norm.tech) && !('sheets' in norm.tech), 'no evaluation keys without evaluations');
assert.strictEqual(G.swimTechEvalList_(norm).length, 0);
// An evaluation of a sheet whose texts did not come is left out.
const lost = JSON.parse(JSON.stringify(training));
delete lost.tech.sheets['test-errors'];
assert.strictEqual(G.swimTechEvalList_(G.swimNormalizeRace_(lost)).length, 1);

// ---- 4) English (texts of the sheet come from Kinovea in English): no Greek of the script in the section
const en = JSON.parse(JSON.stringify(training));
en.id = 'trEN'; en.lang = 'en';
en.tech.sheets = { 'test-turn': { title: 'Test turn sheet', source: 'Test', scored: true, groups: [{ label: 'Approach', items: [{ n: '1', key: 'a.one', text: 'Point one', level: 'A', target: '≤ 0.3 s' },
  { n: '2', key: 'a.two', text: 'Point two', level: 'P', cue: 'Fix two' }] }] } };
en.tech.evals = [{ id: 'ev9', phase: 'tu1', phaseLabel: 'Turn 1', phaseKind: 'turn', sheet: 'test-turn', date: '20/10/2026 10:15', notes: '',
  scores: { 'a.one': { v: 1, a: 1, m: '0.25 s' }, 'a.two': { v: 0, f: 'file-err-1' } }, level: { label: 'Turn 5 m in + 10 m out', value: 9.84, text: '9.84 s · level 4', level: 4, levels: 8 } }];
en.tech.snapshots = [];
en.athlete = 'TEST ATHLETE';
r = JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify({ token: 'tok', app: 'kinovea-swim', action: 'update', race: en, lang: 'en' }) } }).text);
assert.ok(r.ok, JSON.stringify(r));
const layoutEn = G.swimWithLang_('en', () => { const rr = G.swimNormalizeRace_(en); return G.swimBuildLayout_(rr, G.swimAnalyze_(rr), {}); });
const enTexts = Object.values(layoutEn.grid.cells).map(c => c.v).filter(v => typeof v === 'string' && v.length);
const section = enTexts.filter(v => /Technique evaluation|Point|Result|Measured|What to correct|Total|target|from the program|errors on the video/.test(v));
assert.ok(section.includes('Technique evaluation · Turn 1 · Test turn sheet'), 'English title: ' + section.join(' | '));
assert.ok(section.some(v => /^Total 1\/2 · Turn 5 m in \+ 10 m out: 9\.84 s · level 4 \(of 8 levels, 1 = best\)/.test(v)), 'English summary');
assert.ok(section.includes('0.25 s\ntarget ≤ 0.3 s'), 'English target');
const greekLeft = enTexts.filter(v => /[α-ωάέήίόύώ]/i.test(v) && !/ΔΟΚΙΜΑΣΤΙΚΟΣ/.test(v));
assert.deepStrictEqual(greekLeft.filter(v => /Αξιολόγηση|Σύνολο|Μέτρηση|στόχος|Τι να διορθώσει|Πρόοδος|Λάθη/.test(v)), [], 'no Greek of the evaluation section');

console.log('test_techeval OK');
