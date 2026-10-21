// Backend 2.x: pools (50 m, 25 m, 25 yd), ΔΑΚ, comparisons (PB, elite, final, levels), technical analysis, images.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [500001, 'ΔΟΚΙΜΑΣΤΙΚΟΣ ΑΘΛΗΤΗΣ', 'Ανδρών']] });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
const near = (a, b, eps) => Math.abs(a - b) < (eps || 1e-9);
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const sheetOf = r => reports.getSheets().find(s => s.getSheetId() === r.sheetId);
const cell = (sh, r, c) => sh.getRange(r, c).getValue();

function checkGrid(layout, label) {
  const ms = layout.grid.merges;
  const ov = (a, b) => !(a[2] < b[0] || b[2] < a[0] || a[3] < b[1] || b[3] < a[1]);
  for (let i = 0; i < ms.length; i++) for (let j = i + 1; j < ms.length; j++) assert.ok(!ov(ms[i], ms[j]), `${label}: overlapping merges ${ms[i]} / ${ms[j]}`);
  Object.values(layout.grid.cells).forEach(c => {
    if (typeof c.v === 'string' && c.v.startsWith('=')) assert.ok(!/undefined|NaN|null|Infinity/.test(c.v), `${label}: bad formula at ${c.r}:${c.c}: ${c.v}`);
  });
  layout.charts.forEach(ch => ch.ranges.forEach(a1 => {
    const m = a1.match(/^[A-Z]+(\d+):[A-Z]+(\d+)$/);
    assert.ok(m && +m[1] >= 1 && +m[2] <= layout.grid.maxRow && +m[1] <= +m[2], `${label}: chart range ${a1} (${ch.title})`);
  }));
}

// ---------------------------------------------------------------------------------------------
// 1) 100yd ελεύθερο σε πισίνα 25yd: ταχύτητες σε m/s και yd/s, ΔΑΚ ανά 10μ. και ανά 10yd
// ---------------------------------------------------------------------------------------------
const cps25 = dist => { const o = []; for (let w = 0; w < dist; w += 25) [15, 20, 25].forEach(p => o.push(w + p)); return o; };
const yt = [5.9, 8.6, 11.3, 18.2, 20.9, 23.6, 30.5, 33.2, 35.9, 42.7, 45.4, 48.0];
const yStrokes = []; for (let L = 0; L < 4; L++) { const t0 = [4.2, 13.9, 26.2, 38.5][L]; for (let i = 0; i < 9; i++) yStrokes.push(+(t0 + i * 0.75).toFixed(2)); }
const yard = { id: 'y1', athlete: 'ΔΟΚΙΜΑΣΤΙΚΟΣ ΑΘΛΗΤΗΣ', athleteId: '500001', gender: 'Ανδρών', style: 'fs', distance: 100, pool: 25, poolUnit: 'yd',
  competition: 'NCAA test', date: '1.3.2026', round: 'Final', checkpoints: cps25(100).map((d, i) => ({ d, t: yt[i] })), strokes: yStrokes };
let r = call({ action: 'update', race: yard });
assert.ok(r.ok, JSON.stringify(r));
let race = G.swimNormalizeRace_(yard);
assert.strictEqual(race.poolUnit, 'yd'); assert.strictEqual(race.k, 0.9144);
assert.strictEqual(G.swimNormalizeRace_(Object.assign({}, yard, { poolUnit: undefined, pool: '25y' })).poolUnit, 'yd', '"25y" means yards');
assert.strictEqual(G.swimNormalizeRace_(Object.assign({}, yard, { poolUnit: undefined, pool: 'SCY' })).pool, 25, '"SCY" is a 25 pool');
let an = G.swimAnalyze_(race);
assert.ok(near(an.rows[0].vel, 15 * 0.9144 / 5.9), 'velocity in m/s');
assert.ok(near(an.rows[0].velYd, 15 / 5.9), 'velocity in yd/s');
assert.ok(near(an.summary.avgVel, 100 * 0.9144 / 48.0));
// ΔΑΚ του πρώτου 50: μήκη 15→25 και 40→50 (10yd το καθένα): (χρόνοι + χεριές) / (20 × 0.9144 / 10)
const b0 = an.blocks[0];
const s15_25 = an.rows[1].strokes + an.rows[2].strokes, s40_50 = an.rows[4].strokes + an.rows[5].strokes;
assert.ok(near(b0.dak, ((11.3 - 5.9) + (23.6 - 18.2) + s15_25 + s40_50) / 1.8288), 'ΔΑΚ per 10 m ' + b0.dak);
assert.ok(near(b0.dakYd, ((11.3 - 5.9) + (23.6 - 18.2) + s15_25 + s40_50) / 2.0), 'ΔΑΚ per 10 yd');
assert.ok(r.blocks && r.blocks.length === 2 && near(r.blocks[0].dak, b0.dak), 'response carries the 50s with ΔΑΚ');
let layout = G.swimBuildLayout_(race, an, {});
checkGrid(layout, '100yd');
assert.strictEqual(layout.lastCol, 17, 'yards: extra yd/s and yd/stroke columns');
assert.ok(/yd\/s/.test(layout.grid.get(layout.T0 - 1, 16).v));
assert.ok(/\*0\.9144/.test(layout.grid.get(layout.T0 + 1, 3).v), 'sheet velocity formula converts yards: ' + layout.grid.get(layout.T0 + 1, 3).v);
// 2.9.5: ο μέσος όρος, και από κάτω ο ΔΑΚ κάθε 50άρι (δύο 50άρια).
assert.strictEqual(layout.grid.get(layout.kpi.dak.r, layout.kpi.dak.c - 0) && layout.grid.get(8, 1).v, 'ΔΑΚ (μ.ο.)', 'ΔΑΚ is the first tile');
let sh = sheetOf(r);
assert.ok(/100yd Ελεύθερο/.test(cell(sh, 5, 9)) && /25yd/.test(cell(sh, 5, 9)), cell(sh, 5, 9));
assert.ok(/SCY/.test(sh.getName()), 'sheet name says SCY: ' + sh.getName());
const sumRow = G._ss.getSheetByName('Αναλύσεις Αγώνων').getRange(2, 1, 1, 23).getValues()[0];
assert.strictEqual(sumRow[10], '25yd', 'summary pool label');

// ---------------------------------------------------------------------------------------------
// 2) 25μ.: ΔΑΚ με διαιρέτη 1,0 ανά μήκος
// ---------------------------------------------------------------------------------------------
const scm = Object.assign({}, yard, { id: 's1', poolUnit: 'm', competition: 'Χειμερινό', date: '2.12.2025' });
race = G.swimNormalizeRace_(scm); an = G.swimAnalyze_(race);
assert.ok(near(an.blocks[0].dak, ((11.3 - 5.9) + (23.6 - 18.2) + s15_25 + s40_50) / 2.0), '25 m: /2.0 per 50');
assert.strictEqual(an.blocks[0].dakYd, null);
assert.ok(near(an.rows[0].vel, 15 / 5.9));
r = call({ action: 'update', race: scm });
assert.ok(r.ok && /SCM/.test(r.sheetName), r.sheetName);

// ---------------------------------------------------------------------------------------------
// 3) Συγκρίσεις: PB αυτόματα (καλύτερος προηγούμενος χρόνος), elite με χεριές, τελικός, επίπεδα
// ---------------------------------------------------------------------------------------------
const lcmCps = [15, 25, 35, 45, 50, 65, 75, 85, 95, 100];
const mk = (id, scale, date) => ({ id, athlete: 'ΔΟΚΙΜΑΣΤΙΚΟΣ ΑΘΛΗΤΗΣ', athleteId: '500001', gender: 'Ανδρών', style: 'fs', distance: 100, pool: 50,
  competition: 'Πανελλήνιο', round: 'Final', date, compare: { pb: 'auto' },
  checkpoints: lcmCps.map((d, i) => ({ d, t: +([6.1, 10.9, 16.0, 21.1, 23.8, 30.1, 35.5, 41.0, 46.5, 49.4][i] * scale).toFixed(2) })),
  strokes: Array.from({ length: 60 }, (_, i) => +(4.9 + i * 0.75 * scale).toFixed(2)).filter(t => t < 49.2 * scale && (t < 23.5 * scale || t > 28.5 * scale)) });
r = call({ action: 'update', race: mk('pb-old', 1.03, '1.5.2025') });
assert.ok(r.ok);
r = call({ action: 'update', race: mk('pb-best', 1.01, '1.6.2025') });
r = call({ action: 'update', race: mk('later', 0.98, '1.9.2026') });   // μεταγενέστερος: δεν είναι PB του επόμενου
let list = call({ action: 'analyses', query: { athleteId: '500001', style: 'fs', distance: 100, pool: 50, poolUnit: 'm', raceId: 'now' } });
assert.ok(list.ok && list.analyses.length === 3, JSON.stringify(list));
assert.strictEqual(list.analyses[0].id, 'later', 'sorted by final time');
// Elite αναφορές της ίδιας πηγής (τελικός) με χεριές
const refSheet = reports.getSheetByName('Elite αναφορές');
const finalRows = [];
[['WR holder', 0.94], ['2nd', 0.95], ['3rd', 0.955]].forEach(([name, sc]) => {
  lcmCps.forEach((d, i) => finalRows.push([i === 0 ? name : '', i === 0 ? 'Ανδρών' : '', i === 0 ? 'fs' : '', i === 0 ? '100' : '', i === 0 ? '50' : '',
    i === 0 ? 'Paris 2024 Final' : '', d, String(+([6.1, 10.9, 16.0, 21.1, 23.8, 30.1, 35.5, 41.0, 46.5, 49.4][i] * sc).toFixed(2)), '', [4, 4, 4.5, 4.5, 2.5, 4, 4.5, 4.5, 4.5, 2.5][i]]));
});
refSheet.getRange(2, 1, finalRows.length, 10).setValues(finalRows);
const refs = call({ action: 'references' });
assert.ok(refs.ok && refs.references.length === 3, JSON.stringify(refs).slice(0, 300));
// Επίπεδα (μορφή του φύλλου "Επίπεδα")
const benchSheet = reports.getSheetByName('Επίπεδα');
benchSheet.getRange(2, 1, 2, 13).setValues([
  ['Εκκίνηση 0-15μ.', 'Ανδρών', 'fs', '100', '50', '', 5.2, 5.4, 5.5, 5.6, 5.7, 5.9, 'Δεδομένα 2015–2024'],
  ['Στροφή τοίχος-15μ.', 'Ανδρών', 'fs', '100', '50', '', 6.0, 6.2, 6.3, 6.4, 6.6, 6.8, 'Δεδομένα 2015–2024']]);
const bm = call({ action: 'benchmarks' });
assert.ok(bm.ok && bm.benchmarks.length === 2 && bm.levels.length === 6, JSON.stringify(bm).slice(0, 300));

const now = mk('now', 1.0, '1.7.2025');
now.reference = Object.assign({}, refs.references[0]);
r = call({ action: 'update', race: now });
assert.ok(r.ok, JSON.stringify(r));
assert.ok(/PB:/.test(r.compare.pb) && /1\.6\.2025/.test(r.compare.pb), 'auto PB = best earlier race: ' + r.compare.pb);
assert.strictEqual(r.compare.elite, 'WR holder');
assert.strictEqual(r.compare.finalists, 3);
assert.ok(r.compare.benchmarks);
race = G.swimNormalizeRace_(now);
const extras = G.swimComparisons_(race);
assert.strictEqual(extras.pb.id, 'pb-best');
assert.ok(extras.elite.analysis.rows[1].strokes === 4 && extras.elite.analysis.rows[2].sr > 0, 'elite strokes per segment give SR');
layout = G.swimBuildLayout_(race, G.swimAnalyze_(race), extras);
checkGrid(layout, 'compare');
const titles = layout.charts.map(c => c.title);
['Ταχύτητα ανά τμήμα (m/s)', 'Swolf & ΔΑΚ ανά 50μ.', 'ΔΑΚ ανά 50μ.: PB – αγώνας – elite', 'Φάσεις: PB – αγώνας – elite (s)',
  'Εκκίνηση 0-15μ.: επίπεδα και αθλητής (s)', 'Στροφή τοίχος → +15μ.: επίπεδα και αθλητής (s)', 'Ανά 50μ.: αθλητής – μ.ο. τελικού – καλύτερος (s)']
  .forEach(t => assert.ok(titles.indexOf(t) >= 0, 'missing chart ' + t + ' in ' + titles.join(' | ')));
assert.ok(layout.charts.length >= 17, 'charts: ' + layout.charts.length);

// Ο τελικός από το Kinovea (π.χ. οι 8 του ολυμπιακού τελικού, φανταστικά ονόματα): προηγείται των αναφορών της ίδιας πηγής.
const withFinals = Object.assign(mk('now-finals', 1.0, '1.7.2025'), {
  reference: Object.assign({}, refs.references[0]),
  finals: { label: 'Δοκιμαστικός τελικός · 100μ. ελεύθερο Ανδρών', source: 'Δοκιμή',
    swimmers: [['Alpha ONE', 0.93], ['Beta TWO', 0.94], ['Gamma THREE', 0.945], ['Delta FOUR', 0.95]].map(([name, sc], i) => ({
      rank: i + 1, name, times: [15, 50, 100].map(d => ({ d, t: +({ 15: 6.1, 50: 23.8, 100: 49.4 }[d] * sc).toFixed(2) })) })),
    metrics: [{ group: 'Εκκίνηση', label: '15μ. εκκίνηση', athlete: '6.10', rank: '5/5', winner: '5.67', mean: '5.75', best: '5.67', diff: '+0.43' },
      { group: 'Περάσματα', label: '50μ.', athlete: '23.80', rank: '5/5', winner: '22.13', mean: '22.37', best: '22.13', diff: '+1.67' }] }
});
let rf = call({ action: 'update', race: withFinals });
assert.ok(rf.ok, JSON.stringify(rf));
assert.strictEqual(rf.compare.finalists, 4, 'finals of Kinovea in the report');
const finalsRace = G.swimNormalizeRace_(withFinals);
assert.strictEqual(finalsRace.finals.swimmers.length, 4);
const fx = G.swimComparisons_(finalsRace);
assert.strictEqual(fx.finalists.length, 4, 'finals from Kinovea replace the same-source references');
assert.strictEqual(fx.finalists[0].label, '1. Alpha ONE');
assert.strictEqual(fx.finalsLabel, 'Δοκιμαστικός τελικός · 100μ. ελεύθερο Ανδρών');
const finalsLayout = G.swimBuildLayout_(finalsRace, G.swimAnalyze_(finalsRace), fx);
checkGrid(finalsLayout, 'finals');
assert.ok(JSON.stringify(finalsLayout.grid).indexOf('αθλητής – Δοκιμαστικός τελικός') >= 0, 'section title with the final');
assert.strictEqual(G.swimFinalsPayload_({ swimmers: [{ name: 'x', times: [] }, { name: 'y', times: [{ d: 50, t: 25 }] }] }), null, 'fewer than two with times: no final');
// Η σύγκριση τιμή προς τιμή με τους φιναλίστ: πίνακας με τις τιμές στα inputs (αλλάζουν χωρίς ανακατασκευή).
assert.ok(JSON.stringify(finalsLayout.grid).indexOf('Ο αγώνας ανάμεσα στους φιναλίστ: Δοκιμαστικός τελικός') >= 0, 'table of the values among the finalists');
const fmVals = finalsLayout.inputs.blocks.map(b => b.values[0][0]);
['15μ. εκκίνηση', '6.10', '5/5', '5.67', '+0.43', '50μ.', '+1.67'].forEach(v => assert.ok(fmVals.indexOf(v) >= 0, 'value ' + v + ' in the table'));
withFinals.finals.metrics[0].athlete = '6.05';
rf = call({ action: 'update', race: withFinals });
assert.ok(rf.ok && !rf.rebuilt, 'a value among the finalists changes without a rebuild');
sh = sheetOf(r);
assert.ok(sh._charts.length === layout.charts.length, 'all charts inserted');
assert.ok(sh._charts.some(c => c.type === 'combo' && c.opts.series && c.opts.series[1] && c.opts.series[1].targetAxisIndex === 1), 'combo with a second axis');
// Επίπεδο αθλητή: τύπος με τα επίπεδα
const lvl = Object.values(layout.grid.cells).find(c => typeof c.v === 'string' && c.v.indexOf('κάτω από Junior μετάλλιο') >= 0);
assert.ok(lvl && /Top 8/.test(lvl.v), 'level formula');
// Η αλλαγή του PB (π.χ. νέα ανάλυση με καλύτερο χρόνο) αλλάζει την υπογραφή → ανακατασκευή
const sigBefore = layout.signature;
r = call({ action: 'update', race: mk('pb-new', 0.99, '15.6.2025') });
const sigAfter = G.swimBuildLayout_(race, G.swimAnalyze_(race), G.swimComparisons_(race)).signature;
assert.notStrictEqual(sigAfter, sigBefore, 'new PB rebuilds the comparison');
r = call({ action: 'update', race: now });
assert.ok(r.ok && r.rebuilt && /15\.6\.2025/.test(r.compare.pb), r.compare.pb);
r = call({ action: 'update', race: now });
assert.ok(r.ok && !r.rebuilt, 'same data: no rebuild');

// ---------------------------------------------------------------------------------------------
// 4) Μικτή με επίπεδα στροφών (radar) και συμμετοχή των στυλ
// ---------------------------------------------------------------------------------------------
const im = { id: 'im1', athlete: 'ΔΟΚΙΜΑΣΤΙΚΟΣ ΑΘΛΗΤΗΣ', athleteId: '500001', gender: 'Ανδρών', style: 'im', distance: 200, pool: 50,
  checkpoints: [15, 25, 35, 45, 50, 65, 75, 85, 95, 100, 115, 125, 135, 145, 150, 165, 175, 185, 195, 200].map((d, i) => ({ d, t: +(d * 0.6 + (i % 5) * 0.01).toFixed(2) })) };
benchSheet.getRange(4, 1, 3, 13).setValues([
  ['Στροφή', 'Ανδρών', 'im', '200', '50', 'Πεταλούδα → Ύπτιο', 6.1, 6.3, 6.4, 6.6, '', '', ''],
  ['Στροφή', 'Ανδρών', 'im', '200', '50', 'Ύπτιο → Πρόσθιο', 6.9, 7.1, 7.2, 7.4, '', '', ''],
  ['Στροφή', 'Ανδρών', 'im', '200', '50', 'Πρόσθιο → Ελεύθερο', 6.5, 6.7, 6.8, 7.0, '', '', '']]);
race = G.swimNormalizeRace_(im);
const imx = G.swimComparisons_(race);
assert.strictEqual(imx.benchmarks.im.length, 3);
layout = G.swimBuildLayout_(race, G.swimAnalyze_(race), imx);
checkGrid(layout, 'im');
assert.ok(layout.charts.some(c => c.type === 'radar'), 'radar for the IM turn types');
assert.ok(layout.charts.some(c => c.title === 'Συμμετοχή των στυλ στον χρόνο'));
r = call({ action: 'update', race: im });
assert.ok(r.ok, JSON.stringify(r));

// ---------------------------------------------------------------------------------------------
// 5) Εικόνες: στιγμιότυπο, elite καρέ, προεπισκόπηση μόνο για τις δικές μας εικόνες
// ---------------------------------------------------------------------------------------------
const jpeg = Buffer.from([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 4, 0xff, 0xd9]).toString('base64');
const up = call({ action: 'image', raceId: 'tr1', snapshotId: 'sn1', mime: 'image/jpeg', data: jpeg });
assert.ok(up.ok && up.fileId, JSON.stringify(up));
const up2 = call({ action: 'image', raceId: 'tr1', snapshotId: 'sn1', mime: 'image/jpeg', data: jpeg });
assert.ok(up2.ok && up2.fileId !== up.fileId, 're-upload makes a new file');
const snapFolder = G._drive._root._folders[0]._folders.find(f => f._name === 'Στιγμιότυπα');
assert.strictEqual(snapFolder._files.filter(f => !f._trashed).length, 1, 'the old upload went to the bin');
const img = call({ action: 'getImage', fileId: up2.fileId });
assert.ok(img.ok && img.data === jpeg && img.mime === 'image/jpeg', JSON.stringify(img).slice(0, 200));
assert.strictEqual(call({ action: 'getImage', fileId: 'someone-elses-file-id-0000000000' }).ok, false, 'only our images');
assert.strictEqual(call({ action: 'image', raceId: 'tr1', snapshotId: 'big', data: Buffer.alloc(2.5 * 1024 * 1024).toString('base64') }).ok, false, '2 MB limit');
const ef = call({ action: 'saveEliteFrame', frame: { label: 'Elite – είσοδος', style: 'fs', kind: 'start', stage: 'Είσοδος', athlete: 'Elite', source: 'Paris 2024' }, mime: 'image/jpeg', data: jpeg });
assert.ok(ef.ok && ef.frame.fileId && ef.frame.kind === 'start' && ef.frame.style === 'fs', JSON.stringify(ef));
const frames = call({ action: 'eliteFrames' });
assert.ok(frames.ok && frames.frames.length === 1 && frames.frames[0].stage === 'Είσοδος', JSON.stringify(frames));
assert.ok(call({ action: 'getImage', fileId: 'https://drive.google.com/file/d/' + ef.frame.fileId + '/view' }).ok, 'URL form accepted');

// ---------------------------------------------------------------------------------------------
// 6) Τεχνική ανάλυση προπόνησης: 3 εκκινήσεις + 14 στροφές (2 πίνακες), γωνίες, στιγμιότυπα με elite καρέ
// ---------------------------------------------------------------------------------------------
const phases = [];
for (let i = 1; i <= 3; i++) phases.push({ id: 'st' + i, kind: 'start', attempt: i, values: { reaction: 0.2 + i / 100, blockTime: 0.7, flight: 0.3, t5: 1.6, t10: 3.6 + i / 50, t15: 6.0 + i / 20, kicks: 6 + i, breakoutDist: 11 + i / 2, kickRate: 2.4 } });
for (let i = 1; i <= 14; i++) phases.push({ id: 'tu' + i, kind: 'turn', attempt: i, values: { in5: 2.6, turnTime: 0.8 + i / 100, contact: 0.25, rotation: 0.45, out5: 1.3, out10: 3.5, out15: 6.3 + i / 50, kicks: 5, breakoutDist: 9 } });
const training = { id: 'tr1', kind: 'training', athlete: 'ΔΟΚΙΜΑΣΤΙΚΟΣ ΑΘΛΗΤΗΣ', athleteId: '500001', style: 'fs', pool: 50, date: '10.10.2026', location: 'ΟΑΚΑ',
  checkpoints: [], tech: { phases, angles: [{ id: 'a1', name: 'Γόνατο', t: 0.1, deg: 131.5, phase: 'st1' }, { id: 'a2', name: 'Γόνατο', t: 30.1, deg: 128, phase: 'st2' }, { id: 'a3', name: 'Κορμός', t: 50, deg: 12, phase: '' }],
    snapshots: [{ id: 'sn1', t: 0.12, phase: 'st1', stage: 'Θέση ετοιμότητας', comment: 'Το βάρος πολύ πίσω.', fileId: up2.fileId, eliteFileId: ef.frame.fileId, eliteLabel: 'Elite – είσοδος', eliteComment: 'Πιο ψηλά ισχία.' },
      { id: 'sn2', t: 40.2, phase: 'tu3', stage: 'Επαφή', comment: '' }], values: { notes: 'Στόχος: γρηγορότερη αποδέσμευση.' } } };
r = call({ action: 'update', race: training });
assert.ok(r.ok && r.rebuilt, JSON.stringify(r));
sh = sheetOf(r);
assert.ok(/Τεχνική/.test(sh.getName()), sh.getName());
race = G.swimNormalizeRace_(training);
// Με το ίδιο ύψος σελίδας με το φύλλο (μετρημένο), ώστε οι γραμμές να είναι οι ίδιες.
layout = G.swimBuildLayout_(race, G.swimAnalyze_(race), { pageHeight: w => G.swimPdfPageHeight_(reports, w) });
checkGrid(layout, 'training');
assert.ok(layout.technical);
assert.strictEqual(layout.images.length, 2, 'athlete image + elite frame');
assert.strictEqual(sh._images.length, 2, 'images inserted over the grid');
assert.ok(sh._images.every(im => im.w === 546 && im.h === 307), JSON.stringify(sh._images.map(im => [im.w, im.h])));
assert.ok(layout.charts.length >= 4, 'training charts: ' + layout.charts.length);
// Οι στροφές χωρίζονται σε 2 πίνακες (12 + 2) με καλύτερη/μέσο όρο από όλες
const headerRows = Object.values(layout.grid.cells).filter(c => c.c === 1 && /^Τιμές ανά προσπάθεια \(χρόνοι από την επαφή/.test(c.v));
assert.strictEqual(headerRows.length, 2, 'turn table split in two');
const bestTurn = Object.values(layout.grid.cells).find(c => typeof c.v === 'string' && /^=IF\(COUNT\(D\d+:M\d+,D\d+:G\d+\)=0,"",MIN\(/.test(c.v));
assert.ok(bestTurn, 'best value over both turn tables');
// Οι τιμές γράφονται στο φύλλο (blocks): ο καλύτερος χρόνος 15μ. στο πλακίδιο
assert.ok(near(cell(sh, 9, 3), 6.05), 'best 0-15 tile ' + cell(sh, 9, 3));
const angleRow = Object.values(layout.grid.cells).find(c => c.c === 1 && c.v === 'Γωνία: Γόνατο (°)');
assert.ok(angleRow && cell(sh, angleRow.r, 4) === 131.5 && cell(sh, angleRow.r, 5) === 128, 'angles per attempt');
assert.ok(Object.values(layout.grid.cells).some(c => c.v === 'Κορμός'), 'angle without a phase is listed too');
const commentCell = Object.values(sh._cells).length && layout.inputs.blocks.find(b => b.values[0][0] === 'Το βάρος πολύ πίσω.\nElite: Πιο ψηλά ισχία.');
assert.ok(commentCell && cell(sh, commentCell.r, 1) === 'Το βάρος πολύ πίσω.\nElite: Πιο ψηλά ισχία.', 'snapshot comment written');
// Αλλαγή τιμής ή σχολίου: χωρίς ανακατασκευή · νέο στιγμιότυπο: ανακατασκευή
training.tech.phases[0].values.t15 = 5.95;
training.tech.snapshots[1].comment = 'Αργή περιστροφή.';
r = call({ action: 'update', race: training });
assert.ok(r.ok && !r.rebuilt, 'value/comment change: no rebuild');
assert.ok(near(cell(sh, 9, 3), 5.95), 'tile updated without rebuild');
training.tech.snapshots.push({ id: 'sn3', t: 41, phase: 'tu3', stage: 'Αποκόλληση', comment: 'x' });
r = call({ action: 'update', race: training });
assert.ok(r.ok && r.rebuilt, 'new snapshot: rebuild');
// Αυτόματα στιγμιότυπα των γεγονότων: το γεγονός στον τίτλο πριν από το στάδιο (μία φορά όταν είναι ίδια).
training.tech.snapshots.push({ id: 'sn4', t: -0.62, phase: 'tu3', stage: 'Προσέγγιση', event: 'Τελευταία χεριά', comment: '' },
  { id: 'sn5', t: 0, phase: 'tu3', stage: 'Επαφή στον τοίχο', event: 'Επαφή στον τοίχο', comment: '' });
const autoRace = G.swimNormalizeRace_(training);
assert.strictEqual(autoRace.tech.snapshots[3].event, 'Τελευταία χεριά');
const autoLayout = G.swimBuildLayout_(autoRace, G.swimAnalyze_(autoRace), {});
const autoTitles = autoLayout.inputs.blocks.map(b => b.values[0][0]).filter(v => typeof v === 'string' && /Τελευταία χεριά|Επαφή στον τοίχο ·/.test(v));
assert.ok(autoTitles.some(v => /Τελευταία χεριά · Προσέγγιση · t = /.test(v)), 'event before the stage: ' + autoTitles.join(' | '));
assert.ok(autoTitles.some(v => /Επαφή στον τοίχο · t = /.test(v)) && !autoTitles.some(v => /Επαφή στον τοίχο · Επαφή στον τοίχο/.test(v)), 'event = stage once: ' + autoTitles.join(' | '));
// Οι προπονήσεις δεν μπαίνουν στη λίστα PB ούτε στον πίνακα "Αναλύσεις Αγώνων"
list = call({ action: 'analyses', query: { athleteId: '500001', style: 'fs', distance: 100, pool: 50, raceId: 'x' } });
assert.ok(list.analyses.every(a => a.id !== 'tr1'));
const sumIds = G._ss.getSheetByName('Αναλύσεις Αγώνων').getRange(2, 22, 20, 1).getValues().map(x => x[0]);
assert.ok(sumIds.indexOf('tr1') < 0, 'training not in the race summary');

// ---------------------------------------------------------------------------------------------
// 7) Τεχνική ενότητα στο report αγώνα (το demo) και πολλές στροφές σε 1500μ.
// ---------------------------------------------------------------------------------------------
const demo = G.swimDemoRace_();
r = call({ action: 'update', race: demo });
assert.ok(r.ok, JSON.stringify(r));
race = G.swimNormalizeRace_(demo);
layout = G.swimBuildLayout_(race, G.swimAnalyze_(race), {});
checkGrid(layout, 'demo');
assert.ok(Object.values(layout.grid.cells).some(c => c.v === 'Τεχνική ανάλυση εκκίνησης και στροφών'));
const longCps = []; for (let w = 0; w < 1500; w += 50) [15, 45, 50].forEach(p => longCps.push(w + p));
const longRace = { id: 'l1500', style: 'fs', distance: 1500, pool: 50, checkpoints: longCps.map(d => ({ d, t: d * 0.62 })),
  tech: { phases: Array.from({ length: 29 }, (_, i) => ({ id: 't' + i, kind: 'turn', wall: 50 * (i + 1), values: { turnTime: 0.8, out15: 6.5 + i / 100 } })) } };
race = G.swimNormalizeRace_(longRace);
layout = G.swimBuildLayout_(race, G.swimAnalyze_(race), {});
checkGrid(layout, '1500');
assert.strictEqual(Object.values(layout.grid.cells).filter(c => c.c === 1 && /^Στροφές \(χρόνοι από την επαφή στον τοίχο\) \(\d\/3\)$/.test(c.v)).length, 3, '29 turns in 3 tables');
r = call({ action: 'update', race: longRace });
assert.ok(r.ok, JSON.stringify(r).slice(0, 300));

console.log('V2 OK: pools, ΔΑΚ, comparisons, levels, technical analysis, images');
