// 2.17.0: the technique report of a training video in its new form and the comparison with a previous analysis of the athlete and
// with elite (a final from Kinovea, or an elite technique analysis of the sheet), alone or together; without a comparison the report
// as before (images two in a row, across the page). All names and values fictional.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [500101, 'ΦΑΝΤΑΣΤΙΚΗ ΔΟΚΙΜΗ', 'Γυναικών'], [500102, 'ΑΛΛΗ ΔΟΚΙΜΗ', 'Γυναικών']] });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const sheetOf = r => reports.getSheets().find(s => s.getSheetId() === r.sheetId);
const texts = sh => Object.values(sh._cells).map(c => c.v).filter(v => typeof v === 'string');
const values = sh => Object.values(sh._cells).map(c => c.v);
const layoutOf = (race, cmp) => {
  const rr = G.swimNormalizeRace_(race);
  const extras = { pageHeight: w => G.swimPdfPageHeight_(reports, w) };
  if (cmp !== undefined) extras.techCompare = cmp; else extras.techCompare = G.swimTechCompare_(rr);
  return G.swimBuildLayout_(rr, G.swimAnalyze_(rr), extras);
};
const ov = (a, b) => !(a[2] < b[0] || b[2] < a[0] || a[3] < b[1] || b[3] < a[1]);
const noOverlaps = (built, what) => {
  const ms = built.grid.merges;
  for (let i = 0; i < ms.length; i++) for (let j = i + 1; j < ms.length; j++) assert.ok(!ov(ms[i], ms[j]), `${what}: overlapping merges ${ms[i]} / ${ms[j]}`);
  // Images of a row side by side, inside the page.
  const byRow = {};
  built.images.forEach(im => { (byRow[im.row] = byRow[im.row] || []).push(im); assert.ok(im.x >= 0 && im.x + im.width <= 1124, `${what}: image inside the page ${JSON.stringify(im)}`); });
  Object.values(byRow).forEach(list => {
    list.sort((a, b) => a.x - b.x);
    for (let i = 1; i < list.length; i++) assert.ok(list[i].x >= list[i - 1].x + list[i - 1].width, `${what}: images overlap ${JSON.stringify(list)}`);
  });
};

// The sheet of criteria: a few points, scored.
const sheet = { title: 'Δοκιμαστικό φύλλο στροφής', source: 'Δοκιμή', scored: true, groups: [
  { label: 'Προσέγγιση', items: [{ n: '1', key: 'k1', text: 'Σημείο ένα' }, { n: '2', key: 'k2', text: 'Σημείο δύο', cue: 'Διόρθωση δύο' }] },
  { label: 'Ώθηση', items: [{ n: '3', key: 'k3', text: 'Σημείο τρία', cue: 'Διόρθωση τρία' }, { n: '4', key: 'k4', text: 'Σημείο τέσσερα' }] }] };
const turnValues = (k) => ({ in5: 3.06 + k, lastStrokeGap: 1.12, lastStrokeDist: 1.8, rotation: 0.86 + k, contact: 0.34, turnTime: 1.46 + k,
  out5: 2.38 + k, out10: 5.41 + k, out15: 8.62 + k, inOut5: 5.44 + 2 * k, inOut10: 8.47 + 2 * k, inOut15: 11.68 + 2 * k,
  v0_5: 2.10, v5_10: 1.65, v10_15: 1.56, underwater: 2.61, kicks: 5, kickRate: 1.92, kickDist: 0.96, uwVel: 1.84, breakoutTime: 2.95,
  breakoutDist: 6.2, bo15Vel: 1.55, firstStroke: 3.10 });
const race = (id, date, k, extra) => Object.assign({
  id, kind: 'training', athlete: 'ΦΑΝΤΑΣΤΙΚΗ ΔΟΚΙΜΗ', athleteId: '500101', gender: 'Γυναικών', style: 'fs', pool: 25, date, checkpoints: [],
  tech: { phases: [{ id: id + '-t1', kind: 'turn', attempt: 1, values: turnValues(k) }, { id: id + '-t2', kind: 'turn', attempt: 2, values: turnValues(k + 0.04) }],
    angles: [{ id: id + '-a1', name: 'Γωνία γονάτων στην επαφή', t: 0, deg: 131 + 10 * k, phase: id + '-t1' }],
    snapshots: [
      { id: id + '-s1', t: -1.12, phase: id + '-t1', stage: 'Προσέγγιση', event: 'Τελευταία χεριά', comment: 'Σχόλιο ' + id, fileId: 'file-' + id + '-last' },
      { id: id + '-s2', t: 0, phase: id + '-t1', stage: 'Επαφή', event: 'Επαφή στον τοίχο', comment: '', fileId: 'file-' + id + '-contact' },
      { id: id + '-s3', t: 2.95, phase: id + '-t1', stage: 'Έξοδος', event: 'Έξοδος', comment: '', fileId: 'file-' + id + '-out' },
      { id: id + '-e2', t: -0.5, phase: id + '-t1', stage: 'Προσέγγιση', event: 'eval:k2', comment: '✗ Σημείο δύο', fileId: 'file-' + id + '-err2' },
      { id: id + '-e3', t: 0.3, phase: id + '-t1', stage: 'Ώθηση', event: 'eval:k3', comment: '✗ Σημείο τρία', fileId: 'file-' + id + '-err3' }],
    values: { notes: 'Σημειώσεις ' + id }, sheets: { 'test-turn': sheet },
    evals: [{ id: id + '-ev', phase: id + '-t1', phaseLabel: 'Στροφή 1', phaseKind: 'turn', sheet: 'test-turn', date: date,
      scores: k > 0 ? { k1: { v: 1 }, k2: { v: 0 }, k3: { v: 1 }, k4: { v: 1 } } : { k1: { v: 1 }, k2: { v: 0, f: 'file-' + id + '-err2' }, k3: { v: 0, f: 'file-' + id + '-err3', m: '131°' }, k4: { v: 1 } } }] }
}, extra || {});

// ---- 1) Without a comparison: as before, the numbers first, the images two in a row across the page.
const plain = race('tcA', '12.11.2026', 0);
let r = call({ action: 'update', race: plain });
assert.ok(r.ok && r.rebuilt, JSON.stringify(r));
let sh = sheetOf(r), t = texts(sh);
assert.ok(t.includes('Τεχνική ανάλυση (προπόνηση) · Στροφές'), 'title: ' + t.filter(v => /Τεχνική ανάλυση/.test(v)).join(' | '));
assert.ok(t.includes('Όλα τα νούμερα της στροφής') && t.some(v => /μέσος όρος 2 στροφών/.test(v)), 'the table of all the numbers, the mean of the attempts');
assert.ok(t.includes('Στροφές: τιμές ανά προσπάθεια (χρόνοι από την επαφή στον τοίχο)'), 'the values of each attempt after it');
assert.ok(!t.some(v => /^Σύγκριση/.test(v || '')) && !t.includes('Δ elite'), 'no comparison line or columns');
['Προσέγγιση', 'Στροφή', 'Υποβρύχια φάση', 'Μετά την έξοδο'].forEach(gr => assert.ok(t.includes(gr), 'group ' + gr));
// Without a comparison (as the sample 2): the phases in cards, two columns; the times at +5/+10/+15 in a small table; the speeds per 5 m in
// the speed chart (not again in the cards).
assert.ok(t.includes('Χρόνοι (s)') && t.includes('Από τον τοίχο') && t.includes('Από τα -5μ.') && t.includes('+15μ.'), 'the table of the times');
assert.ok(!t.includes('Ταχύτητα ανά 5μ.') && !t.includes('Χρόνοι από τον τοίχο'), 'no card of the speeds per 5 m or of the times');
{
  const b0 = layoutOf(plain, null);
  const cellOf = text => Object.values(b0.grid.cells).find(c => c.v === text);
  const left = cellOf('Προσέγγιση'), right = cellOf('Υποβρύχια φάση');
  assert.ok(left && right && left.c === 1 && right.c === 8 && left.r === right.r, 'two columns of cards: ' + JSON.stringify([left, right].map(c => c && [c.r, c.c])));
  const vals = b0.inputs.blocks.filter(b => b.r === left.r && (b.c === 7 || b.c === 15));
  assert.strictEqual(vals.length, 2, 'one block of values per column: ' + JSON.stringify(b0.inputs.blocks.map(b => [b.r, b.c, b.values.length])));
  const inRow = cellOf('Προσέγγιση: -5 → τοίχος (s)').r;
  const lb = vals.find(b => b.c === 7);
  assert.strictEqual(Math.round(lb.values[inRow - lb.r][0] * 100), 308, 'the mean of the approach in its card');
}
assert.ok(t.includes('Ταχύτητα προσέγγισης (-5 → τοίχος) (m/s)'), 'the approach speed from the time of the -5');
assert.ok(t.includes('ΧΡΟΝΟΣ ΣΤΡΟΦΗΣ') && t.includes('ΑΞΙΟΛΟΓΗΣΗ'), 'tiles');
let built = layoutOf(plain, null);
noOverlaps(built, 'plain');
let speed = built.charts.find(c => /ταχύτητα ανά τμήμα/.test(c.title));
assert.ok(speed && speed.colors.length === 1 && speed.ranges.length === 2, 'the speed chart: one series: ' + JSON.stringify(speed && speed.ranges));
const imgs = f => built.images.filter(im => im.fileId === f);
assert.ok(imgs('file-tcA-last').length === 1 && imgs('file-tcA-last')[0].width === 546, 'a snapshot two in a row: ' + JSON.stringify(imgs('file-tcA-last')));
assert.ok(imgs('file-tcA-err2').length === 1 && imgs('file-tcA-err2')[0].width === 546, 'an error frame two in a row, once');
// The order of the sections: numbers, speed, evaluation, phases, errors.
const firstRow = text => { const c = Object.values(built.grid.cells).filter(c => c.v === text).sort((a, b) => a.r - b.r)[0]; return c ? c.r : -1; };
const order = ['Όλα τα νούμερα της στροφής', 'Ταχύτητα ανά τμήμα της στροφής (m/s)', 'Αξιολόγηση τεχνικής', 'Οι φάσεις της στροφής', 'Τα λάθη στο βίντεο'].map(firstRow);
assert.ok(order.every(x => x > 0) && order.every((x, i) => i === 0 || x > order[i - 1]), 'order of the sections: ' + order.join(','));
assert.ok(firstRow('Σημειώσεις') > firstRow('Αξιολόγηση τεχνικής') && firstRow('Σημειώσεις') < firstRow('Οι φάσεις της στροφής'), 'notes after the evaluation');

// A value that changes (no comparison): written without rebuilding the report (as before).
const plain2 = JSON.parse(JSON.stringify(plain));
plain2.tech.phases[0].values.in5 = 3.01;
r = call({ action: 'update', race: plain2 });
assert.ok(r.ok && !r.rebuilt, 'a changed value: no rebuild');

// ---- 2) The previous analysis of the athlete (sent before), "auto": the previous one before this date.
r = call({ action: 'update', race: race('tcP', '10.10.2026', 0.08) });
assert.ok(r.ok, JSON.stringify(r));
// The list for Kinovea: the technique analyses of the athlete (same style and pool) and the elite technique analyses.
let list = call({ action: 'analyses', query: { tech: true, athleteId: '500101', athlete: 'ΦΑΝΤΑΣΤΙΚΗ ΔΟΚΙΜΗ', style: 'fs', pool: 25, poolUnit: 'm', raceId: 'tcA' } });
assert.ok(list.ok && list.analyses.length === 1 && list.analyses[0].id === 'tcP' && /10\.10\.2026 · 2 στροφές/.test(list.analyses[0].label), JSON.stringify(list));
assert.ok(Array.isArray(list.techElites) && list.techElites.length === 0, 'no elite technique analysis yet');
const withPrev = race('tcA', '12.11.2026', 0, { compare: { techPrev: 'auto' } });
r = call({ action: 'update', race: withPrev });
assert.ok(r.ok && r.rebuilt, 'the comparison rebuilds the report: ' + JSON.stringify(r));
sh = sheetOf(r); t = texts(sh);
assert.ok(t.some(v => /^⇄  Σύγκριση με την ίδια αθλήτρια \(10\.10\.2026\)$/.test(v)), 'comparison line: ' + t.filter(v => /Σύγκριση/.test(v)).join(' | '));
assert.ok(t.includes('12/11') && t.includes('10/10') && t.includes('Δ 10/10'), 'columns today / 10/10 / Δ');
built = layoutOf(withPrev);
noOverlaps(built, 'prev');
// Δ: 3,06 − 3,14 = −0,08 (better: green), the turn time 1,46 − 1,54 = −0,08.
const blocks = built.inputs.blocks;
const main = blocks.find(b => b.c === 13 && b.values.length > 10);
assert.ok(main, 'the values of the main table in one block: ' + JSON.stringify(blocks.map(b => [b.r, b.c, b.values.length])));
const rowOf = label => Object.values(built.grid.cells).find(c => c.v === label).r;
const vRow = rowOf('Προσέγγιση: -5 → τοίχος (s)') - main.r;
assert.deepStrictEqual(JSON.parse(JSON.stringify(main.values[vRow].map(x => typeof x === 'number' ? Math.round(x * 100) / 100 : x))), [3.08, 3.16, -0.08], 'mean today, 10/10, Δ');
const dCell = built.grid.cells[(main.r + vRow) + ':15'];
assert.strictEqual(dCell.bg, G.SWIM_TC.goodBg, 'Δ better: green');
const kRow = rowOf('Υποβρύχιοι δελφινισμοί') - main.r;
assert.strictEqual(built.grid.cells[(main.r + kRow) + ':15'].bg, G.SWIM_TC.naBg, 'Δ without "better": grey');
// The evaluation: the result of 10/10 beside each point, ↓ where it got worse (↑ where better).
assert.ok(t.includes('✓ ↓') && !t.includes('✓ ↑'), 'point 3 was right on 10/10 and is wrong today: ' + t.filter(v => /[↑↓]/.test(v)).join(' | '));
// The phases: the same event of 10/10 beside today's, two in a row; the error frames as before.
const last = built.images.filter(im => /-last$/.test(im.fileId)).sort((a, b) => a.x - b.x);
assert.ok(last.length === 2 && last[0].fileId === 'file-tcP-last' && last[1].fileId === 'file-tcA-last' && last[0].row === last[1].row && last[0].width === 546,
  'the same moment side by side: ' + JSON.stringify(last));
speed = built.charts.find(c => /ταχύτητα ανά τμήμα/.test(c.title));
assert.ok(speed && speed.colors.length === 2 && speed.colors[0] === G.SWIM_TC.prev && speed.colors[1] === G.SWIM_TC.now, 'speed: 10/10 and today');
// A value that changes but keeps the colours of the Δ: no rebuild; one that turns a Δ from green to red: rebuilt.
const wp2 = JSON.parse(JSON.stringify(withPrev));
wp2.tech.phases[0].values.in5 = 3.05;
r = call({ action: 'update', race: wp2 });
assert.ok(r.ok && !r.rebuilt, 'same colours: no rebuild');
wp2.tech.phases[0].values.in5 = 3.40;
r = call({ action: 'update', race: wp2 });
assert.ok(r.ok && r.rebuilt, 'a Δ changes colour: rebuilt');

// ---- 3) Both: the previous one and elite (a final from Kinovea), with the levels of its event.
const levels = reports.getSheetByName('Επίπεδα') || G.swimBenchmarksSheet_(reports);
levels.getRange(2, 1, 1, 13).setValues([['Στροφή τοίχος-15μ.', 'Γυναικών', 'Ελεύθερο', 100, 50, '', 6.45, 6.58, 6.70, 6.88, 7.04, 7.30, 'Δοκιμή']]);
const elite = { type: 'final', id: 'TEST · 100 fs F', label: 'TEST · 100μ. fs Γυναικών · μέσος όρος 8', gender: 'F', style: 'fs', distance: 100, pool: 50,
  values: { turn: { in5: 2.55, out15: 6.90, inOut15: 9.45, breakoutTime: 3.10, breakoutDist: 8.0 } } };
const both = race('tcA', '12.11.2026', 0, { compare: { techPrev: 'tcP', techElite: elite } });
both.tech.snapshots[1].eliteFileId = 'file-elite-contact';
both.tech.snapshots[1].eliteLabel = 'Elite 1';
r = call({ action: 'update', race: both });
assert.ok(r.ok && r.rebuilt, JSON.stringify(r));
sh = sheetOf(r); t = texts(sh);
assert.ok(t.some(v => /^⇄  Σύγκριση με την ίδια αθλήτρια \(10\.10\.2026\) και με elite: TEST · 100μ\. fs Γυναικών · μέσος όρος 8$/.test(v)), 'both in the line');
assert.ok(t.includes('Elite') && t.includes('Δ elite') && t.includes('Δ 10/10'), 'both columns');
assert.ok(t.some(v => /^Θέση στα Επίπεδα · τοίχος → \+15μ\.$/.test(v)) && t.includes('Junior μετάλλιο') && t.includes('Αθλήτρια (σήμερα)'), 'levels: ' + t.filter(v => /Επίπεδα|Junior/.test(v)).join(' | '));
built = layoutOf(both);
noOverlaps(built, 'both');
const main2 = built.inputs.blocks.find(b => b.c === 11 && b.values.length > 10);
assert.ok(main2 && main2.values[0].length === 5, 'today, 10/10, Δ, elite, Δ');
const rowOf2 = label => Object.values(built.grid.cells).find(c => c.v === label).r;
const in5 = main2.values[rowOf2('Προσέγγιση: -5 → τοίχος (s)') - main2.r];
assert.deepStrictEqual(JSON.parse(JSON.stringify(in5.map(x => typeof x === 'number' ? Math.round(x * 100) / 100 : x))), [3.08, 3.16, -0.08, 2.55, 0.53], 'elite Δ: ' + JSON.stringify(in5));
const rot = main2.values[rowOf2('Περιστροφή: αρχή → επαφή (s)') - main2.r];
assert.strictEqual(rot[3], '—', 'no elite value: a dash');
speed = built.charts.find(c => /ταχύτητα ανά τμήμα/.test(c.title));
assert.ok(speed && speed.colors.length === 3 && speed.ranges.length === 4, 'three series');
// With elite: the segments the final has (approach, wall → breakout, breakout → 15, all).
const segRows = Object.values(built.grid.cells).filter(c => ['τοίχος → έξοδος', 'έξοδος → +15μ.', 'τοίχος → +15μ.'].includes(c.v));
assert.strictEqual(segRows.length, 3, 'segments of the final');
const contact = built.images.filter(im => /contact$/.test(im.fileId)).sort((a, b) => a.x - b.x);
assert.ok(contact.length === 3 && contact.map(x => x.fileId).join(',') === 'file-tcP-contact,file-tcA-contact,file-elite-contact' && contact.every(x => x.width === 360),
  'three in a row: ' + JSON.stringify(contact));
const lastEl = built.images.filter(im => /-last$/.test(im.fileId));
assert.strictEqual(lastEl.length, 2, 'no elite frame there: the box says so');
assert.ok(texts(sh).includes('χωρίς elite καρέ'), 'missing elite frame written');

// ---- 4) An elite technique analysis of the sheet («Ως elite αναφορά…»), compared with all its numbers and its snapshots.
const other = race('tcE', '01.09.2026', -0.4, { athlete: 'ΑΛΛΗ ΔΟΚΙΜΗ', athleteId: '500102' });
r = call({ action: 'update', race: other });
assert.ok(r.ok, JSON.stringify(r));
let saved = call({ action: 'saveTechElite', raceId: 'tcE', name: 'Δοκιμαστική elite στροφή' });
assert.ok(saved.ok && saved.id === 'tcE', JSON.stringify(saved));
saved = call({ action: 'saveTechElite', raceId: 'tcE', name: 'Δοκιμαστική elite στροφή 2' });
const eliteSheet = reports.getSheetByName('Elite τεχνική');
assert.strictEqual(eliteSheet.getLastRow(), 2, 'the same analysis again: updated, not a second row');
list = call({ action: 'analyses', query: { tech: true, athleteId: '500101', athlete: 'ΦΑΝΤΑΣΤΙΚΗ ΔΟΚΙΜΗ', style: 'fs', pool: 25, poolUnit: 'm', raceId: 'tcA' } });
assert.ok(list.techElites.length === 1 && list.techElites[0].id === 'tcE' && list.techElites[0].label === 'Δοκιμαστική elite στροφή 2' && list.techElites[0].turns === 2, JSON.stringify(list.techElites));
const withRef = race('tcA', '12.11.2026', 0, { compare: { techElite: { type: 'ref', id: 'tcE', label: 'Δοκιμαστική elite στροφή 2' } } });
r = call({ action: 'update', race: withRef });
assert.ok(r.ok && r.rebuilt, JSON.stringify(r));
built = layoutOf(withRef);
noOverlaps(built, 'ref');
const main3 = built.inputs.blocks.find(b => b.c === 13 && b.values.length > 10);
const rowOf3 = label => Object.values(built.grid.cells).find(c => c.v === label).r;
const rot3 = main3.values[rowOf3('Περιστροφή: αρχή → επαφή (s)') - main3.r];
assert.ok(typeof rot3[1] === 'number' && Math.abs(rot3[1] - 0.48) < 1e-6, 'every number of the elite analysis: ' + JSON.stringify(rot3));
const last3 = built.images.filter(im => /-last$/.test(im.fileId)).sort((a, b) => a.x - b.x);
assert.ok(last3.length === 2 && last3[1].fileId === 'file-tcE-last', 'the elite analysis\' snapshot of the same event: ' + JSON.stringify(last3));

// ---- 5) "best": the best −5 → +15 of the athlete; a comparison never with itself; nothing of another style.
const best = G.swimTechAutoPrev_(G.swimNormalizeRace_(race('tcX', '13.11.2026', 0)), 'best');
assert.strictEqual(best, 'tcA', 'best: the faster one (tcA 11,68 against tcP 11,84)');
assert.strictEqual(G.swimTechAutoPrev_(G.swimNormalizeRace_(race('tcY', '01.01.2026', 0)), 'auto'), '', 'nothing before the date');
const bk = G.swimTechCompare_(G.swimNormalizeRace_(race('tcB', '12.11.2026', 0, { style: 'bk', compare: { techPrev: 'tcP' } })));
assert.strictEqual(bk.prev, null, 'another style is not compared');

// ---- 6) English: no Greek in what the report shows (but the names and texts of the analyst).
r = call({ action: 'update', race: Object.assign({}, both, { id: 'tcEN' }), lang: 'en' });
assert.ok(r.ok, JSON.stringify(r));
sh = sheetOf(r);
const GREEK = /[Ͱ-Ͽ]/;
const mine = new Set(['ΦΑΝΤΑΣΤΙΚΗ ΔΟΚΙΜΗ', 'Σημειώσεις tcA', 'Σχόλιο tcA', 'Σχόλιο tcP', 'Σημείο ένα', 'Σημείο δύο', 'Σημείο τρία', 'Σημείο τέσσερα',
  'Διόρθωση δύο', 'Διόρθωση τρία', 'Δοκιμαστικό φύλλο στροφής', 'Δοκιμή', 'Προσέγγιση', 'Ώθηση', 'Γωνία γονάτων στην επαφή']);
const leftover = texts(sh).filter(v => GREEK.test(v.replace(/(^|[^Ͱ-Ͽ])Δ(?![Ͱ-Ͽ])/g, '$1')) && ![...mine].some(m => v.indexOf(m) >= 0)
  && !/Γυναικών|μέσος όρος 8|Elite 1/.test(v));
assert.strictEqual(leftover.length, 0, 'Greek left in the English report: ' + leftover.slice(0, 12).join(' | '));

console.log('test_techcompare OK: without a comparison, the previous analysis, both with levels, an elite technique analysis, best/auto, English');
