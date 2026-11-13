// 2.16.0: the report of a technique analysis follows what the analysis has. With only an evaluation and snapshots (no times,
// distances or speeds), its title and tiles are those of the evaluation. With times, the measured tiles come first, then the
// evaluation's tiles, and the speeds per segment get their charts. A long single list shows only its scored points. A snapshot
// alone, or an error frame of a long list, takes the width of the page in its own shape (for example all the screens of a group).
// 2.17.0: the new form of the report: the title «Τεχνική ανάλυση (προπόνηση) · …», the tiles of the means, the speed per segment as
// one chart with its table; the error frames two in a row (a very wide one, all the screens of a group, alone across the page).
// Older sheet titles lose the name of the old organisation. Fictional athletes and sheets only.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [500002, 'ΦΑΝΤΑΣΤΙΚΗ ΚΟΛΥΜΒΗΤΡΙΑ', 'Γυναικών']] });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const sheetOf = r => reports.getSheets().find(s => s.getSheetId() === r.sheetId);
const texts = sh => Object.values(sh._cells).map(c => c.v).filter(v => typeof v === 'string');
const layoutOf = race => { const rr = G.swimNormalizeRace_(race); return G.swimBuildLayout_(rr, G.swimAnalyze_(rr), { pageHeight: w => G.swimPdfPageHeight_(reports, w) }); };

// A long single list (30 points in three phases), five of them scored, two errors with their frames.
const groups = ['Πρώτη φάση', 'Δεύτερη φάση', 'Τρίτη φάση'].map((label, gi) => ({
  label, items: Array.from({ length: 10 }, (_, i) => ({ n: String(gi * 10 + i + 1), key: 'k' + (gi * 10 + i + 1), text: 'Δοκιμαστικό σημείο ' + (gi * 10 + i + 1), level: 'X', cue: 'Δοκιμαστική διόρθωση ' + (gi * 10 + i + 1) }))
}));
const single = { title: 'Στροφή – Ελεύθερο (δοκιμή)', source: 'Κ.Ο.Ε. – Kinovea Swim', scored: true, groups };
const evalLong = { id: 'evL', phase: 'tq1', phaseLabel: 'Στροφή 1', phaseKind: 'turn', sheet: 'kv-turn-fs', date: '01/11/2026 09:00', notes: '',
  scores: { k1: { v: 1 }, k2: { v: 0, f: 'file-e1' }, k12: { v: 1 }, k13: { v: -1 }, k25: { v: 0, f: 'file-e2' } } };
const snapshots = [
  // The snapshot of a group of three screens in a row (3 × 16:9) and an error frame of the same group.
  { id: 'sg1', t: 0.2, phase: 'tq1', stage: 'Αποκόλληση', event: '', comment: 'Όλες οι οθόνες', fileId: 'file-group', w: 1280, h: 240 },
  { id: 'sg2', t: -0.4, phase: 'tq1', stage: 'Προσέγγιση', event: 'eval:k2', comment: '✗ Δοκιμαστικό σημείο 2', fileId: 'file-e1', w: 1280, h: 240 },
  { id: 'sg3', t: 0.5, phase: 'tq1', stage: 'Ώθηση', event: 'eval:k25', comment: '✗ Δοκιμαστικό σημείο 25', fileId: 'file-e2', w: 1280, h: 720 }
];
const onlyEval = { id: 'trA', kind: 'training', athlete: 'ΦΑΝΤΑΣΤΙΚΗ ΚΟΛΥΜΒΗΤΡΙΑ', athleteId: '500002', gender: 'Γυναικών', style: 'fs', pool: 25, date: '01.11.2026',
  checkpoints: [], tech: { phases: [{ id: 'tq1', kind: 'turn', attempt: 1, values: {} }], angles: [], snapshots, values: { notes: '' },
    sheets: { 'kv-turn-fs': single }, evals: [evalLong] } };

// ---- 1) Only the evaluation and snapshots: the report of the evaluation.
let r = call({ action: 'update', race: onlyEval });
assert.ok(r.ok, JSON.stringify(r));
let t = texts(sheetOf(r));
assert.ok(t.includes('Τεχνική ανάλυση (προπόνηση) · Αξιολόγηση τεχνικής'), 'title of an evaluation report: ' + t.filter(v => /Τεχνική ανάλυση/.test(v)).join(' | '));
['ΑΞΙΟΛΟΓΗΣΕΙΣ', 'ΣΩΣΤΑ (%)', 'ΛΑΘΗ', 'ΣΤΙΓΜΙΟΤΥΠΑ'].forEach(cap => assert.ok(t.includes(cap), 'tile ' + cap));
assert.ok(!t.includes('ΚΑΛΥΤΕΡΟ 0-15ΜΕΤ') && !t.some(v => /^ΚΑΛΥΤΕΡΟ/.test(v)) && !t.includes('ΔΕΛΦΙΝΙΣΜΟΙ'), 'no empty tiles of times');
assert.ok(t.includes('4 σημεία βαθμολογήθηκαν') && t.includes('2 από 4'), 'the evaluation in numbers: ' + t.filter(v => /από|βαθμολογ/.test(v)).join(' | '));
assert.ok(t.includes('Τα 5 σημεία με απάντηση (η λίστα έχει 30).'), 'the long list shows its answered points');
assert.ok(t.includes('Δοκιμαστικό σημείο 1') && t.includes('Δοκιμαστικό σημείο 13') && !t.includes('Δοκιμαστικό σημείο 3') && !t.includes('Δοκιμαστικό σημείο 30'), 'only the scored points');
assert.ok(t.includes('Πρώτη φάση   1/2') && t.includes('Τρίτη φάση   0/1'), 'phase rows: done well of those scored');
let built = layoutOf(onlyEval);
const big = built.images.find(im => im.fileId === 'file-group');
assert.ok(big && big.fit && big.width >= 1000 && Math.abs(big.width / big.height - 1280 / 240) < 0.05, 'the group snapshot across the page in its shape: ' + JSON.stringify(big));
const e1 = built.images.find(im => im.fileId === 'file-e1'), e2 = built.images.find(im => im.fileId === 'file-e2');
assert.ok(e1 && e1.fit && e1.width >= 1000 && Math.abs(e1.width / e1.height - 1280 / 240) < 0.05, 'a very wide error frame alone across the page: ' + JSON.stringify(e1));
assert.ok(e2 && e2.fit && e2.width === 546 && e2.height === 307, 'a 16:9 error frame two in a row (not alone on a page): ' + JSON.stringify(e2));
assert.strictEqual(built.images.filter(im => im.fileId === 'file-e1').length, 1, 'an error frame once');
const ms = built.grid.merges;
const ov = (a, b) => !(a[2] < b[0] || b[2] < a[0] || a[3] < b[1] || b[3] < a[1]);
for (let i = 0; i < ms.length; i++) for (let j = i + 1; j < ms.length; j++) assert.ok(!ov(ms[i], ms[j]), `overlapping merges ${ms[i]} / ${ms[j]}`);

// ---- 2) Times and the evaluation: the measured tiles first, the speeds per segment with their charts.
const withTimes = JSON.parse(JSON.stringify(onlyEval));
withTimes.id = 'trB';
withTimes.tech.phases = [
  { id: 'tq1', kind: 'turn', attempt: 1, values: { in5: 2.4, turnTime: 0.82, out5: 1.6, out10: 3.3, out15: 5.6, v0_5: 3.12, v5_10: 2.94, v10_15: 2.17 } },
  { id: 'tq2', kind: 'turn', attempt: 2, values: { in5: 2.5, turnTime: 0.8, out5: 1.7, out10: 3.4, out15: 5.7, v0_5: 2.94, v5_10: 2.94, v10_15: 2.17 } }
];
r = call({ action: 'update', race: withTimes });
assert.ok(r.ok, JSON.stringify(r));
t = texts(sheetOf(r));
assert.ok(t.includes('Τεχνική ανάλυση (προπόνηση) · Στροφές'), 'the title of a report with times');
assert.ok(t.includes('ΤΟΙΧΟΣ → +15Μ.') && t.includes('ΧΡΟΝΟΣ ΣΤΡΟΦΗΣ') && t.includes('ΑΞΙΟΛΟΓΗΣΗ'), 'measured tiles and the evaluation: ' + t.filter(v => /^[Α-ΩΆ-Ώ -→+0-9.]+$/.test(v)).join(' | '));
assert.ok(!t.includes('ΑΠΟΔΕΣΜΕΥΣΗ') && !t.some(v => /^ΣΤΑ 15/.test(v)), 'no tile for a start without one');
built = layoutOf(withTimes);
const titles = built.charts.map(c => c.title);
assert.ok(titles.includes('Στροφή: ταχύτητα ανά τμήμα (m/s)'), 'chart of the speeds per segment: ' + titles.join(' | '));
assert.ok(t.includes('τοίχος → +5μ.') && t.includes('+5 → +10μ.') && t.includes('+10 → +15μ.') && t.includes('-5μ. → τοίχος'), 'its table: the segments of 5 m (no elite)');
const speedChart = built.charts.find(c => /ταχύτητα ανά τμήμα/.test(c.title));
assert.strictEqual(speedChart.vTitle, 'm/s', 'speeds in m/s');

// ---- 3) The English report of the evaluation (no Greek of the new texts).
const en = JSON.parse(JSON.stringify(onlyEval));
en.id = 'trC';
en.lang = 'en';
r = call({ action: 'update', race: en });
assert.ok(r.ok, JSON.stringify(r));
t = texts(sheetOf(r));
['Technical analysis (training) · Technique evaluation', 'EVALUATIONS', 'DONE WELL (%)', 'ERRORS', 'SNAPSHOTS', 'The 5 points answered (the list has 30).'].forEach(x => assert.ok(t.includes(x), 'English: ' + x + ' in ' + t.filter(v => /Technical|EVAL/.test(v)).join(' | ')));

// ---- 4) Older titles without the old organisation's name.
assert.strictEqual(G.swimNoBrand_('Στροφή ελεύθερο (British Swimming)'), 'Στροφή ελεύθερο');
assert.strictEqual(G.swimNoBrand_('Μικτή: ύπτιο → πρόσθιο, crossover (British Swimming, άνδρες)'), 'Μικτή: ύπτιο → πρόσθιο, crossover (άνδρες)');
assert.strictEqual(G.swimNoBrand_('British Swimming – Start and Turn Checklists'), 'Start and Turn Checklists');
assert.strictEqual(G.swimNoBrand_('Κ.Ο.Ε. – Kinovea Swim'), 'Κ.Ο.Ε. – Kinovea Swim');

console.log('test_adaptive: all passed');
